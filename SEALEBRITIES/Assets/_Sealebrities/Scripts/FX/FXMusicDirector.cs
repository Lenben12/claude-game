using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// The background music tracks (K.6). None = silence (music off under water, resting between loops).
    public enum FXMusicTrack : byte { None, Menu, Day, Evening, Night, Water, Deep, Oldreef }

    /// K.6 relaxed background music. Picks the track for the moment (menu / prologue, island day, golden evening, moonlit
    /// night, under water above / below 30 m), renders it on a worker thread (FXMusicSynth, ≤ 1.5 s) into a 16-bit buffer
    /// that a streaming AudioClip plays through its PCM callback (no allocation on the audio thread), and crossfades over
    /// 4 s. At most two tracks live in memory (the one playing + the next). After two full loops in the game the music
    /// rests 30–60 s (ambience only). Volume = Settings music × 0.55, −6 dB under NPC lines / radio calls, −9 dB while
    /// streaming live; under water the FX low-pass applies (softer than the ambience's). Predators never change the music.
    /// "Musik beim Tauchen" (Settings.Data.musicWhileDiving) off = no music below the surface.
    sealed class FXMusicDirector
    {
        // ---- constants (J.5: music numbers live here)
        public const float MusicMix = 0.55f;
        const float CrossfadeSeconds = 4f;
        const float RestMin = 30f, RestMax = 60f;
        const int LoopsBeforeRest = 2;
        const float DeepEnter = 32f, DeepLeave = 27f;         // hysteresis around the 30 m border
        const float SwitchDelay = 2.5f;                        // a new context must hold this long (surface bobbing)
        const float DuckNpc = 0.5f, DuckLive = 0.355f;         // −6 dB, −9 dB
        const float CutoffOpen = 22000f, CutoffUnder = 1500f;
        // day / evening / night borders (time of day 0..1): day 06:30–17:00, evening 17:00–20:30, night 20:30–06:30
        const float DayStart = 6.5f / 24f, EveningStart = 17f / 24f, NightStart = 20.5f / 24f;
        /// Cosmetic-style id remembered in Settings.Data.unlockedCosmetics once Juno's quest J1 is done (K.6 / L.7).
        public const string OldreefUnlockId = "music:oldreef";
        const string JunoQuestId = "J1";
        /// The npc module's reward flag of J1 (NpcKeys.OldreefSong) – spelled out so FX does not depend on the npc code.
        const string OldreefSongFlag = "oldreef_song";

        sealed class Rendered
        {
            public FXMusicTrack track;
            public short[] pcm;
            public AudioClip clip;
            public int readPos;   // audio thread only
            public float bpm;

            public void OnRead(float[] data)
            {
                var p = pcm;
                if (p == null || p.Length == 0) { Array.Clear(data, 0, data.Length); return; }
                int n = p.Length, pos = readPos;
                if (pos >= n || pos < 0) pos = 0;
                const float k = 1f / 32768f;
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = p[pos] * k;
                    pos++;
                    if (pos >= n) pos = 0;
                }
                readPos = pos;
            }

            public void OnSetPosition(int position) { readPos = position >= 0 && position < pcm.Length ? position : 0; }
        }

        readonly AudioSource[] src = new AudioSource[2];
        readonly AudioLowPassFilter[] lpf = new AudioLowPassFilter[2];
        readonly Rendered[] slot = new Rendered[2];   // what each source plays
        readonly float[] gain = new float[2];          // crossfade gains
        int active;                                    // index of the source fading in / playing

        Task<short[]> renderTask;
        FXMusicTrack rendering = FXMusicTrack.None;
        Rendered ready;                                // rendered, waiting to start

        FXMusicTrack desired = FXMusicTrack.None, candidate = FXMusicTrack.None;
        float candidateSince;
        bool deep, menuToggle, wasMenu;
        int loops;
        float lastTime;
        float restUntil = -1f;
        bool resting;
        float duckNpcUntil;
        bool radioOn;
        float duck = 1f, level01;
        float vMusic = 0.7f;
        bool diveMusic = true;
        float cutoffApplied = -1f;
        float unlockCheck;
        bool oldreefSession;
        bool failed;

        /// 0..1 how loud the music currently is (before the volume setting) – FXAudio ducks the ambience with it.
        public float Level01 => level01;

        public FXMusicDirector(Transform parent)
        {
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "FX_MusicA" : "FX_MusicB");
                go.transform.SetParent(parent, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.loop = true;
                s.spatialBlend = 0f;
                s.dopplerLevel = 0f;
                s.priority = 20;
                s.volume = 0f;
                s.ignoreListenerPause = true;   // the music keeps playing in a paused game (relaxing, never cut off)
                var f = go.AddComponent<AudioLowPassFilter>();
                f.cutoffFrequency = CutoffOpen;
                f.lowpassResonanceQ = 1f;
                src[i] = s;
                lpf[i] = f;
            }
            ApplyVolumes();
        }

        public void Subscribe()
        {
            GameEvents.NpcLine += OnNpcLine;
            GameEvents.RadioCall += OnRadioCall;
        }

        public void Unsubscribe()
        {
            GameEvents.NpcLine -= OnNpcLine;
            GameEvents.RadioCall -= OnRadioCall;
        }

        void OnNpcLine(string speaker, string text, float seconds)
        {
            float s = float.IsNaN(seconds) || seconds < 0f ? 3f : Mathf.Min(seconds, 30f);
            duckNpcUntil = Mathf.Max(duckNpcUntil, Time.unscaledTime + s + 0.4f);
        }

        void OnRadioCall(string speaker, bool started) { radioOn = started; }

        public void ApplyVolumes()
        {
            var d = Settings.Data;
            float m = d != null ? d.music : 0.7f;
            vMusic = float.IsNaN(m) || float.IsInfinity(m) ? 0.7f : Mathf.Clamp01(m);
            diveMusic = d == null || d.musicWhileDiving;
        }

        public void Dispose()
        {
            Unsubscribe();
            FXMusic.Playing = false;
            for (int i = 0; i < 2; i++)
            {
                if (src[i] != null) src[i].Stop();
                Release(i);
            }
            if (ready != null) { DestroyClip(ready); ready = null; }
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(FXFrame f)
        {
            if (failed) return;
            float now = Time.unscaledTime;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

            CheckUnlock(f, now);
            FXMusicTrack want = Decide(f);
            // a new context must hold a moment (bobbing at the surface, a quick look over the dock edge) – the menu and
            // a starting game switch at once
            if (want != candidate) { candidate = want; candidateSince = now; }
            bool menuEdge = (want == FXMusicTrack.Menu || want == FXMusicTrack.Oldreef) != wasMenu;
            if (candidate != desired && (now - candidateSince >= SwitchDelay || menuEdge))
            {
                desired = candidate;
                wasMenu = desired == FXMusicTrack.Menu || desired == FXMusicTrack.Oldreef;
                loops = 0;
                resting = false;
            }

            PollRender();
            Rendered cur = slot[active];
            FXMusicTrack playing = cur != null ? cur.track : FXMusicTrack.None;

            // rest after two loops in the game (never in the menu); the rest also ends early when the context changes
            if (resting && (now >= restUntil || (cur != null && desired != cur.track))) { resting = false; loops = 0; }
            if (!resting && cur != null && desired == cur.track && !wasMenu && loops >= LoopsBeforeRest)
            {
                resting = true;
                restUntil = now + UnityEngine.Random.Range(RestMin, RestMax);
            }

            FXMusicTrack target = resting ? FXMusicTrack.None : desired;
            if (target != FXMusicTrack.None && target != playing)
            {
                if (ready != null && ready.track == target) StartReady();
                // a new render only once the previous crossfade is over: never more than two tracks in memory
                else if (renderTask == null && slot[1 - active] == null) StartRender(target);
            }
            else if (target != FXMusicTrack.None && target == playing && gain[active] <= 0.001f && cur != null && !src[active].isPlaying)
            {
                // back from a rest with the same track: start it again from the top
                RestartActive();
            }

            // crossfade gains: the active source rises to 1 (0 while resting / no target), the other falls to 0
            float step = dt / CrossfadeSeconds;
            bool activeOn = target != FXMusicTrack.None && playing == target;
            gain[active] = Mathf.MoveTowards(gain[active], activeOn ? 1f : 0f, step);
            int other = 1 - active;
            gain[other] = Mathf.MoveTowards(gain[other], 0f, step);
            if (gain[other] <= 0f && slot[other] != null) { src[other].Stop(); Release(other); }
            if (gain[active] <= 0f && !activeOn && slot[active] != null && src[active].isPlaying && (target == FXMusicTrack.None || resting))
                src[active].Pause();

            // ducking (NPC lines / radio −6 dB, livestream −9 dB)
            float duckTarget = 1f;
            if (now < duckNpcUntil || radioOn) duckTarget = DuckNpc;
            if (LiveState.IsLive) duckTarget = Mathf.Min(duckTarget, DuckLive);
            duck = Mathf.MoveTowards(duck, duckTarget, dt / 0.35f);

            float master = vMusic * MusicMix * duck;
            for (int i = 0; i < 2; i++)
            {
                if (src[i] == null) continue;
                float v = master * gain[i];
                if (Mathf.Abs(src[i].volume - v) > 0.001f || (v == 0f && src[i].volume != 0f)) src[i].volume = v;
            }
            level01 = Mathf.Max(gain[0] * (slot[0] != null ? 1f : 0f), gain[1] * (slot[1] != null ? 1f : 0f));

            // under water the music sounds through the water (a softer low-pass than the ambience)
            float cutoff = CutoffOpen * Mathf.Pow(CutoffUnder / CutoffOpen, f.under01);
            if (Mathf.Abs(cutoff - cutoffApplied) > cutoff * 0.01f)
            {
                cutoffApplied = cutoff;
                for (int i = 0; i < 2; i++) if (lpf[i] != null) lpf[i].cutoffFrequency = cutoff;
            }

            // loop counting + the public beat clock
            cur = slot[active];
            if (cur != null && src[active].isPlaying)
            {
                float t = src[active].time;
                if (t + 1f < lastTime) loops++;
                lastTime = t;
                FXMusic.Time = t;
                FXMusic.Bpm = cur.bpm;
                FXMusic.Current = cur.track;
            }
            else
            {
                FXMusic.Time = 0f;
                FXMusic.Current = FXMusicTrack.None;
            }
            FXMusic.Playing = level01 > 0.001f && vMusic > 0.001f && cur != null;
        }

        /// The track for the current moment (before the switch delay and the rests).
        FXMusicTrack Decide(FXFrame f)
        {
            var gm = GameManager.I;
            bool menu = !f.inGame || (gm != null && gm.NeedsPrologue);
            if (menu)
            {
                if (!wasMenuDecided) { wasMenuDecided = true; menuToggle = !menuToggle; }
                return OldreefUnlocked && menuToggle ? FXMusicTrack.Oldreef : FXMusicTrack.Menu;
            }
            wasMenuDecided = false;

            if (f.underwater && f.under01 > 0.5f)
            {
                if (!diveMusic) return FXMusicTrack.None;
                if (deep) { if (f.depth < DeepLeave) deep = false; }
                else if (f.depth > DeepEnter) deep = true;
                return deep ? FXMusicTrack.Deep : FXMusicTrack.Water;
            }
            deep = false;
            float t = gm != null ? gm.State.timeOfDay : 0.5f;
            t -= Mathf.Floor(t);
            if (t >= DayStart && t < EveningStart) return FXMusicTrack.Day;
            if (t >= EveningStart && t < NightStart) return FXMusicTrack.Evening;
            return FXMusicTrack.Night;
        }
        bool wasMenuDecided;

        bool OldreefUnlocked
        {
            get
            {
                if (oldreefSession) return true;
                var d = Settings.Data;
                return d != null && d.unlockedCosmetics != null && d.unlockedCosmetics.Contains(OldreefUnlockId);
            }
        }

        /// Juno's quest J1 (L.7) unlocks the „Altriff-Lied“ menu variation on this machine (checked every few seconds).
        void CheckUnlock(FXFrame f, float now)
        {
            if (!f.inGame || now < unlockCheck) return;
            unlockCheck = now + 5f;
            if (OldreefUnlocked) return;
            bool done;
            try { done = Quests.IsDone(JunoQuestId) || StoryFlags.Has(OldreefSongFlag); } catch (Exception) { done = false; }
            if (!done) return;
            oldreefSession = true;
            var d = Settings.Data;
            if (GameManager.AutomatedRun || d == null || d.unlockedCosmetics == null) return;
            d.unlockedCosmetics.Add(OldreefUnlockId);
            Settings.MarkDirty();
            Debug.Log("[Music] menu song \"Altriff-Lied\" unlocked");
        }

        // ------------------------------------------------------------------ rendering + streaming
        void StartRender(FXMusicTrack t)
        {
            if (ready != null) { DestroyClip(ready); ready = null; }   // never more than two tracks in memory
            rendering = t;
            try { renderTask = Task.Run(() => FXMusicSynth.Render(t)); }
            catch (Exception)
            {
                // no thread pool: render inline once (only happens on exotic platforms)
                renderTask = null;
                try { ready = Wrap(t, FXMusicSynth.Render(t)); }
                catch (Exception e) { Fail(e); }
            }
        }

        void PollRender()
        {
            if (renderTask == null || !renderTask.IsCompleted) return;
            var task = renderTask;
            renderTask = null;
            if (task.IsFaulted || task.IsCanceled)
            {
                Fail(task.Exception != null ? task.Exception.GetBaseException() : new Exception("cancelled"));
                return;
            }
            try { ready = Wrap(rendering, task.Result); }
            catch (Exception e) { Fail(e); }
        }

        void Fail(Exception e)
        {
            failed = true;
            FXMusic.Playing = false;
            Debug.LogWarning("Sealebrities FX: music disabled: " + (e != null ? e.Message : "?"));
        }

        Rendered Wrap(FXMusicTrack t, short[] pcm)
        {
            if (pcm == null || pcm.Length < FXMusicSynth.SR) return null;
            var r = new Rendered { track = t, pcm = pcm, bpm = FXMusicSynth.BpmOf(t) };
            r.clip = AudioClip.Create("FX_Music_" + t, pcm.Length, 1, FXMusicSynth.SR, true, r.OnRead, r.OnSetPosition);
            return r;
        }

        /// Puts the rendered track on the free source and lets the crossfade run.
        void StartReady()
        {
            var r = ready;
            ready = null;
            if (r == null || r.clip == null) return;
            int next = slot[active] != null ? 1 - active : active;
            if (slot[next] != null) { src[next].Stop(); Release(next); }
            slot[next] = r;
            gain[next] = 0f;
            var s = src[next];
            s.clip = r.clip;
            s.loop = true;
            s.volume = 0f;
            s.time = 0f;
            s.Play();
            active = next;
            loops = 0;
            lastTime = 0f;
            Debug.Log("[Music] " + r.track + " (" + r.bpm.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " BPM, " +
                      FXMusicSynth.LoopSeconds(r.track).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " s loop)");
        }

        void RestartActive()
        {
            var s = src[active];
            if (s == null || s.clip == null) return;
            s.Stop();
            s.time = 0f;
            s.Play();
            loops = 0;
            lastTime = 0f;
        }

        void Release(int i)
        {
            var r = slot[i];
            slot[i] = null;
            gain[i] = 0f;
            if (src[i] != null) src[i].clip = null;
            if (r != null) DestroyClip(r);
        }

        static void DestroyClip(Rendered r)
        {
            if (r.clip != null) UnityEngine.Object.Destroy(r.clip);
            r.clip = null;
            r.pcm = null;
        }
    }
}
