using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural audio runtime: ambience crossfade (waves + jungle above, drone below with low-pass muffling),
    /// regulator breathing, heartbeat at low air, warning beeps, deep groans, splash, reward/UI sounds driven by
    /// GameEvents, and the ukulele music loop in the main menu / launch intro.
    /// Volumes: master = AudioListener.volume (Settings.Apply); everything here is scaled by Settings sfx or music
    /// and follows Settings.Changed live (one-shot sources carry the category volume, so even playing sounds follow).
    /// All sources are 2D and follow the main camera. A real pause (Time.timeScale 0) pauses everything but the
    /// UI and music sources.
    sealed class FXAudio
    {
        const float CutoffOpen = 22000f;
        const float CutoffUnder = 650f;
        const float MusicMix = 0.55f;

        readonly GameObject root;
        readonly Transform rootT;
        readonly AudioSource waves, jungle, drone, music, breath, heart, deep, sfx, ui;
        readonly AudioLowPassFilter wavesLpf, jungleLpf;

        Task<FXSynthBank> task;
        Task<float[]> musicTask;
        FXSynthBank bank;
        bool failed, ready, musicFailed;
        int createStep;

        AudioClip cWaves, cJungle, cDrone, cGroan, cInhale, cExhale, cHeart, cSplash, cBump;
        AudioClip cBlip, cRecOn, cRecOff, cDiscover, cFanfare, cCoin;
        AudioClip cWarn, cShutter, cEquip, cCast, cReel, cCatch, cSparkle, cClick, cPing, cError, cMusic;
        readonly AudioClip[] created = new AudioClip[32];
        int createdCount;

        // volumes (Settings)
        float vSfx = 1f, vMusic = 0.7f;

        // runtime state
        bool inGame;
        long lastMoney;
        float lastCoin = -10f, lastBlip = -10f, lastFanfare = -10f, lastJingle = -10f, lastEquip = -10f, lastSfx = -10f;
        float gameStartTime = -100f;
        string lastQuest;
        bool pauseApplied;
        float heartTimer, groanTimer = 30f, warnTimer;
        bool warnActive;
        float vWaves = -1f, vJungle = -1f, vDrone = -1f, pDrone = -1f, cutoffApplied = -1f;
        float musicLevel, musicApplied = -1f;
        HoldItem lastRight, lastLeft;

        public FXAudio(Transform parent)
        {
            root = new GameObject("FX_Audio");
            rootT = root.transform;
            rootT.SetParent(parent, false);

            waves = MakeSource("FX_Waves", true, 40, true, out wavesLpf);
            jungle = MakeSource("FX_Jungle", true, 45, true, out jungleLpf);
            drone = MakeSource("FX_UnderwaterDrone", true, 40, false, out _);
            music = MakeSource("FX_Music", true, 20, false, out _);
            music.ignoreListenerPause = true;
            breath = MakeSource("FX_Breath", false, 60, false, out _);
            heart = MakeSource("FX_Heartbeat", false, 60, false, out _);
            deep = MakeSource("FX_DeepGroan", false, 90, false, out _);
            sfx = MakeSource("FX_Sfx", false, 70, false, out _);
            ui = MakeSource("FX_UI", false, 50, false, out _);
            ui.ignoreListenerPause = true;

            ApplyVolumes();

            try
            {
                musicTask = Task.Run(() => FXAudioSynth.Music());
                task = Task.Run(() => FXAudioSynth.Generate());
            }
            catch (Exception)
            {
                task = null;
                musicTask = null;
                bank = FXAudioSynth.Generate();
                try { cMusic = Make("FX_MenuMusic", FXAudioSynth.Music()); } catch (Exception) { musicFailed = true; }
                if (cMusic != null) StartMusic();
            }
        }

        AudioSource MakeSource(string name, bool loop, int priority, bool lowPass, out AudioLowPassFilter lpf)
        {
            var go = new GameObject(name);
            go.transform.SetParent(rootT, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            s.dopplerLevel = 0f;
            s.priority = priority;
            s.volume = loop ? 0f : 1f;
            lpf = null;
            if (lowPass)
            {
                lpf = go.AddComponent<AudioLowPassFilter>();
                lpf.cutoffFrequency = CutoffOpen;
                lpf.lowpassResonanceQ = 1f;
            }
            return s;
        }

        public void Dispose()
        {
            if (pauseApplied) AudioListener.pause = false;
            FXMusic.Playing = false;
            for (int i = 0; i < createdCount; i++)
                if (created[i] != null) UnityEngine.Object.Destroy(created[i]);
            createdCount = 0;
        }

        /// Re-reads the sfx / music volumes from Settings (initially and on Settings.Changed).
        public void ApplyVolumes()
        {
            var d = Settings.Data;
            vSfx = d != null ? Mathf.Clamp01(d.sfx) : 1f;
            vMusic = d != null ? Mathf.Clamp01(d.music) : 0.7f;
            // one-shot sources: PlayOneShot scales by the source volume, so playing sounds follow the slider too
            breath.volume = vSfx;
            heart.volume = vSfx;
            deep.volume = vSfx;
            sfx.volume = vSfx;
            ui.volume = vSfx;
            // loops: re-applied by the next tick
            vWaves = -1f; vJungle = -1f; vDrone = -1f; musicApplied = -1f;
        }

        // ------------------------------------------------------------------ events
        public void Subscribe()
        {
            GameEvents.RecordingChanged += OnRecordingChanged;
            GameEvents.SpeciesDiscovered += OnDiscovered;
            GameEvents.SizeRecord += OnSizeRecord;
            GameEvents.MilestoneReached += OnMilestone;
            GameEvents.MoneyChanged += OnMoney;
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.Toast += OnToast;
            GameEvents.PhoneOpened += OnPhoneOpened;
            GameEvents.QuestChanged += OnQuest;
            GameEvents.PlayerBumped += OnBumped;
            GameEvents.DeliveryReceived += OnDelivery;
            GameEvents.DeliveryOrdered += OnOrdered;
            GameEvents.FishCaught += OnFishCaught;
            Gallery.Added += OnPhoto;
            Collectibles.Found += OnCollectible;
            Hands.Changed += OnHandsChanged;
            FXSfx.Handler = PlayPublic;
        }

        public void Unsubscribe()
        {
            GameEvents.RecordingChanged -= OnRecordingChanged;
            GameEvents.SpeciesDiscovered -= OnDiscovered;
            GameEvents.SizeRecord -= OnSizeRecord;
            GameEvents.MilestoneReached -= OnMilestone;
            GameEvents.MoneyChanged -= OnMoney;
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.Toast -= OnToast;
            GameEvents.PhoneOpened -= OnPhoneOpened;
            GameEvents.QuestChanged -= OnQuest;
            GameEvents.PlayerBumped -= OnBumped;
            GameEvents.DeliveryReceived -= OnDelivery;
            GameEvents.DeliveryOrdered -= OnOrdered;
            GameEvents.FishCaught -= OnFishCaught;
            Gallery.Added -= OnPhoto;
            Collectibles.Found -= OnCollectible;
            Hands.Changed -= OnHandsChanged;
            if (FXSfx.Handler == (Action<FXSound, float>)PlayPublic) FXSfx.Handler = null;
        }

        static float Now => Time.unscaledTime;

        void OnRecordingChanged(bool rec) => Play(sfx, rec ? cRecOn : cRecOff, 0.45f);

        void OnDiscovered(SpeciesDef s, float cm, SizeTier tier)
        {
            lastJingle = Now;
            Play(sfx, cDiscover, 0.75f);
        }

        void OnSizeRecord(SpeciesDef s, float cm, SizeTier tier)
        {
            if (Now - lastFanfare < 1.5f) return;
            lastFanfare = Now;
            Play(sfx, cFanfare, 0.7f);
        }

        void OnMilestone(Milestone m)
        {
            if (Now - lastFanfare < 1.5f) return;
            lastFanfare = Now;
            Play(ui, cFanfare, 0.75f);
        }

        void OnMoney(long value)
        {
            bool gain = value > lastMoney;
            lastMoney = value;
            if (!inGame || !gain || Now - lastCoin < 0.12f || Now - gameStartTime < 1.5f) return;
            lastCoin = Now;
            Play(ui, cCoin, 0.5f);
        }

        void OnGameStarted()
        {
            inGame = true;
            gameStartTime = Now;
            lastMoney = GameManager.I != null ? GameManager.I.State.money : 0;
            lastQuest = null;
            lastRight = Hands.Active(Hand.Right);
            lastLeft = Hands.Active(Hand.Left);
        }

        void OnReturnedToMenu() { inGame = false; }

        void OnToast(string text) => Blip(0.3f);

        void OnPhoneOpened() => Blip(0.4f);

        void OnQuest(string text)
        {
            if (string.IsNullOrEmpty(text) || text == lastQuest) { lastQuest = text; return; }
            lastQuest = text;
            Blip(0.35f);
        }

        void OnBumped(float airLost) => Play(sfx, cBump, 0.85f);

        void OnDelivery(GearItem item)
        {
            if (Now - lastJingle < 1f) return;
            lastJingle = Now;
            Play(ui, cDiscover, 0.5f);
        }

        void OnOrdered(GearItem item) => Play(ui, cPing, 0.45f);

        /// Only the local angler hears the catch (the host also receives the co-op players' catches).
        void OnFishCaught(int value)
        {
            if (!inGame || !Hands.IsHeld(HoldItem.FishingRod)) return;
            Play(sfx, cCatch, 0.7f);
            Play(sfx, cReel, 0.35f);
        }

        void OnPhoto(PhotoEntry e) => Play(ui, cShutter, 0.7f);

        void OnCollectible(string id, CollectibleKind kind) => Play(ui, cSparkle, 0.65f);

        /// Hotbar: a soft "fwip" when the item in a hand really changed (state syncs do not count).
        void OnHandsChanged()
        {
            HoldItem r = Hands.Active(Hand.Right), l = Hands.Active(Hand.Left);
            bool changed = r != lastRight || l != lastLeft;
            lastRight = r;
            lastLeft = l;
            if (!changed || !inGame || UIState.LoadingOpen || Now - gameStartTime < 1f || Now - lastEquip < 0.06f) return;
            lastEquip = Now;
            Play(sfx, cEquip, 0.45f);
        }

        void Blip(float volume)
        {
            if (Now - lastBlip < 0.08f) return;
            lastBlip = Now;
            Play(ui, cBlip, volume);
        }

        static void Play(AudioSource src, AudioClip clip, float volume)
        {
            if (clip == null || src == null) return;
            src.PlayOneShot(clip, volume);
        }

        public void PlaySplash(float strength) => Play(sfx, cSplash, 0.75f * strength);

        /// FXSfx.Play from other modules.
        void PlayPublic(FXSound s, float volume)
        {
            AudioClip c;
            AudioSource src = ui;
            float v = volume;
            switch (s)
            {
                case FXSound.Click: c = cClick; v *= 0.5f; break;
                case FXSound.Ping: c = cPing; v *= 0.55f; break;
                case FXSound.Coin: c = cCoin; v *= 0.5f; break;
                case FXSound.Shutter: c = cShutter; v *= 0.7f; break;
                case FXSound.Equip: c = cEquip; src = sfx; v *= 0.45f; break;
                case FXSound.Cast: c = cCast; src = sfx; v *= 0.7f; break;
                case FXSound.Reel: c = cReel; src = sfx; v *= 0.45f; break;
                case FXSound.Catch: c = cCatch; src = sfx; v *= 0.7f; break;
                case FXSound.Splash: c = cSplash; src = sfx; v *= 0.6f; break;
                case FXSound.Sparkle: c = cSparkle; v *= 0.65f; break;
                case FXSound.Error: c = cError; v *= 0.5f; break;
                case FXSound.Warning: c = cWarn; src = sfx; v *= 0.45f; break;
                case FXSound.Discover: c = cDiscover; v *= 0.7f; break;
                case FXSound.Fanfare: c = cFanfare; v *= 0.7f; break;
                default: return;
            }
            if (s == FXSound.Click || s == FXSound.Equip)
            {
                if (Now - lastSfx < 0.03f) return;
                lastSfx = Now;
            }
            Play(src, c, v);
        }

        // ------------------------------------------------------------------ loading
        void TryFinishMusic()
        {
            if (cMusic != null || musicFailed || musicTask == null || !musicTask.IsCompleted) return;
            if (musicTask.IsFaulted || musicTask.IsCanceled)
            {
                musicFailed = true;
                Debug.LogWarning("Sealebrities FX: music synthesis failed: " + (musicTask.Exception != null ? musicTask.Exception.GetBaseException().Message : "cancelled"));
                musicTask = null;
                return;
            }
            cMusic = Make("FX_MenuMusic", musicTask.Result);
            musicTask = null;
            StartMusic();
        }

        void StartMusic()
        {
            if (cMusic == null) return;
            music.clip = cMusic;
            music.loop = true;
            music.volume = 0f;
            music.time = 0f;
            music.Play();
            musicApplied = -1f;
        }

        void TryFinishLoading()
        {
            if (failed) return;
            if (bank == null)
            {
                if (task == null || !task.IsCompleted) return;
                if (task.IsFaulted || task.IsCanceled)
                {
                    failed = true;
                    Debug.LogWarning("Sealebrities FX: audio synthesis failed: " + (task.Exception != null ? task.Exception.GetBaseException().Message : "cancelled"));
                    task = null;
                    return;
                }
                bank = task.Result;
                task = null;
            }

            // one clip per frame keeps the upload spikes small
            switch (createStep++)
            {
                case 0: cWaves = Make("FX_Waves", bank.waves); break;
                case 1: cJungle = Make("FX_Jungle", bank.jungle); break;
                case 2: cDrone = Make("FX_Drone", bank.drone); break;
                case 3: cGroan = Make("FX_Groan", bank.groan); break;
                case 4: cInhale = Make("FX_Inhale", bank.inhale); break;
                case 5: cExhale = Make("FX_Exhale", bank.exhale); break;
                case 6: cHeart = Make("FX_Heartbeat", bank.heartbeat); break;
                case 7: cSplash = Make("FX_Splash", bank.splash); break;
                case 8: cBump = Make("FX_Bump", bank.bump); break;
                case 9: cBlip = Make("FX_Blip", bank.blip); break;
                case 10: cRecOn = Make("FX_RecOn", bank.recOn); break;
                case 11: cRecOff = Make("FX_RecOff", bank.recOff); break;
                case 12: cDiscover = Make("FX_Discover", bank.discover); break;
                case 13: cFanfare = Make("FX_Fanfare", bank.fanfare); break;
                case 14: cCoin = Make("FX_Coin", bank.coin); break;
                case 15: cWarn = Make("FX_Warn", bank.warn); break;
                case 16: cShutter = Make("FX_Shutter", bank.shutter); break;
                case 17: cEquip = Make("FX_Equip", bank.equip); break;
                case 18: cCast = Make("FX_Cast", bank.cast); break;
                case 19: cReel = Make("FX_Reel", bank.reel); break;
                case 20: cCatch = Make("FX_Catch", bank.catchFish); break;
                case 21: cSparkle = Make("FX_Sparkle", bank.sparkle); break;
                case 22: cClick = Make("FX_Click", bank.click); break;
                case 23: cPing = Make("FX_Ping", bank.ping); break;
                case 24: cError = Make("FX_Error", bank.error); break;
                default:
                    bank = null;
                    ready = true;
                    StartLoop(waves, cWaves);
                    StartLoop(jungle, cJungle);
                    StartLoop(drone, cDrone);
                    break;
            }
        }

        AudioClip Make(string name, float[] data)
        {
            if (data == null || data.Length == 0) return null;
            var clip = AudioClip.Create(name, data.Length, 1, FXAudioSynth.SR, false);
            clip.SetData(data, 0);
            if (createdCount < created.Length) created[createdCount++] = clip;
            return clip;
        }

        static void StartLoop(AudioSource s, AudioClip clip)
        {
            if (clip == null) return;
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.time = UnityEngine.Random.Range(0f, clip.length * 0.9f);
            s.Play();
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(FXFrame f, FXBreath b)
        {
            bool pause = f.paused;
            if (pause != pauseApplied)
            {
                AudioListener.pause = pause;
                pauseApplied = pause;
            }

            if (f.camT != null) rootT.position = f.camPos;
            TryFinishMusic();
            TickMusic(f);
            if (!ready)
            {
                TryFinishLoading();
                return;
            }
            if (pause) return;

            TickAmbience(f);
            TickDiver(f, b);
        }

        /// Ukulele loop whenever no game runs (main menu, launch intro, boot); fades out when the game starts.
        void TickMusic(FXFrame f)
        {
            if (cMusic == null) { FXMusic.Playing = false; return; }
            bool want = !f.inGame;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            musicLevel = Mathf.MoveTowards(musicLevel, want ? 1f : 0f, dt / (want ? 2f : 1.2f));
            float v = musicLevel * MusicMix * vMusic;
            if (Mathf.Abs(v - musicApplied) > 0.002f || (v == 0f && musicApplied != 0f))
            {
                music.volume = v;
                musicApplied = v;
            }
            bool audible = musicLevel > 0.001f;
            if (audible && !music.isPlaying) music.Play();
            else if (!audible && music.isPlaying) { music.Stop(); music.time = 0f; }
            FXMusic.Playing = audible && vMusic > 0.001f;
            FXMusic.Time = music.isPlaying ? music.time : 0f;
        }

        void TickAmbience(FXFrame f)
        {
            Vector3 cp = f.camPos;
            float dx = cp.x - WorldLayout.IslandCenter.x, dz = cp.z - WorldLayout.IslandCenter.z;
            float distXZ = Mathf.Sqrt(dx * dx + dz * dz);
            float shore = Mathf.Abs(distXZ - WorldLayout.IslandRadius);
            float nearShore = 1f - FXMath.Smooth01(shore / 90f);
            float height = Mathf.Max(0f, cp.y - Waves.SeaLevel);
            float heightFall = Mathf.Lerp(1f, 0.45f, FXMath.Smooth01((height - 3f) / 40f));
            float musicDuck = 1f - 0.3f * musicLevel;

            float wavesAbove = (0.3f + 0.32f * nearShore) * heightFall * musicDuck;
            float jungleAmt = 1f - FXMath.Smooth01((distXZ - WorldLayout.IslandRadius * 0.55f) / (WorldLayout.IslandRadius * 0.9f));
            float jungleAbove = 0.36f * jungleAmt * Mathf.Lerp(1f, 0.55f, f.night01) * musicDuck;

            float u = f.under01;
            float shallow = 1f - FXMath.Smooth01(f.depth / 18f);
            float tWaves = Mathf.Lerp(wavesAbove, wavesAbove * 0.65f * shallow, u) * vSfx;
            float tJungle = jungleAbove * (1f - u) * vSfx;
            float tDrone = u * Mathf.Lerp(0.32f, 0.6f, FXMath.Smooth01(f.depth / 60f)) * vSfx;
            float cutoff = CutoffOpen * Mathf.Pow(CutoffUnder / CutoffOpen, u);
            float pitch = 1f - 0.12f * FXMath.Smooth01(f.depth / 100f) + 0.04f * f.narcosis01 * Mathf.Sin(f.time * 0.9f);

            float rate = f.dt * 0.9f;
            vWaves = Approach(waves, vWaves, tWaves, rate);
            vJungle = Approach(jungle, vJungle, tJungle, rate);
            vDrone = Approach(drone, vDrone, tDrone, rate * 1.5f);

            if (Mathf.Abs(cutoff - cutoffApplied) > cutoff * 0.01f)
            {
                if (wavesLpf != null) wavesLpf.cutoffFrequency = cutoff;
                if (jungleLpf != null) jungleLpf.cutoffFrequency = cutoff;
                cutoffApplied = cutoff;
            }
            if (Mathf.Abs(pitch - pDrone) > 0.002f)
            {
                drone.pitch = pitch;
                pDrone = pitch;
            }
        }

        static float Approach(AudioSource s, float current, float target, float maxDelta)
        {
            float v = current < 0f ? s.volume : current;
            float next = Mathf.MoveTowards(v, target, maxDelta);
            if (current < 0f || Mathf.Abs(next - current) > 0.0015f || (next == target && current != target))
            {
                s.volume = next;
                return next;
            }
            return current;
        }

        void TickDiver(FXFrame f, FXBreath b)
        {
            // regulator breathing (rhythm shared with the exhale bubbles)
            if (b.InhaleNow || b.ExhaleNow)
            {
                breath.pitch = b.Pitch;
                if (b.InhaleNow) Play(breath, cInhale, 0.42f);
                if (b.ExhaleNow) Play(breath, cExhale, 0.5f);
            }

            // heartbeat when the tank is nearly empty
            bool hb = f.hasPlayer && f.underwater && !f.fainting && f.air01 < 0.15f;
            if (hb)
            {
                heartTimer -= f.dt;
                if (heartTimer <= 0f)
                {
                    float k = 1f - f.air01 / 0.15f;
                    heartTimer += 60f / Mathf.Lerp(80f, 145f, k);
                    if (heartTimer < 0f) heartTimer = 0.3f;
                    Play(heart, cHeart, Mathf.Lerp(0.45f, 0.9f, k));
                }
            }
            else heartTimer = 0f;

            // warning beep with the red screen edge (low air / too deep), GDD 15.11
            bool warn = f.hasPlayer && f.underwater && !f.fainting && f.warn01 > 0.5f;
            if (warn)
            {
                if (!warnActive) { warnActive = true; warnTimer = 0f; }
                warnTimer -= f.dt;
                if (warnTimer <= 0f)
                {
                    warnTimer = f.air01 < 0.1f ? 1.4f : 2.4f;
                    Play(sfx, cWarn, 0.4f);
                }
            }
            else warnActive = false;

            // something big and far away in the deep...
            if (f.underwater && f.depth > 35f && !f.fainting)
            {
                groanTimer -= f.dt;
                if (groanTimer <= 0f)
                {
                    groanTimer = UnityEngine.Random.Range(35f, 90f);
                    deep.pitch = UnityEngine.Random.Range(0.82f, 1.12f);
                    Play(deep, cGroan, Mathf.Lerp(0.18f, 0.38f, FXMath.Smooth01((f.depth - 35f) / 60f)));
                }
            }
            else if (groanTimer < 20f) groanTimer = 20f;
        }
    }
}
