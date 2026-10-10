using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural audio runtime: ambience crossfade (waves + jungle above, drone below with low-pass muffling),
    /// regulator breathing, heartbeat at low air, deep groans, splash and reward/UI sounds driven by GameEvents.
    /// All sources are 2D and follow the main camera. Respects pause via AudioListener.pause.
    sealed class FXAudio
    {
        const float CutoffOpen = 22000f;
        const float CutoffUnder = 650f;

        readonly GameObject root;
        readonly Transform rootT;
        readonly AudioSource waves, jungle, drone, breath, heart, deep, sfx, ui;
        readonly AudioLowPassFilter wavesLpf, jungleLpf;

        Task<FXSynthBank> task;
        FXSynthBank bank;
        bool failed, ready;
        int createStep;

        AudioClip cWaves, cJungle, cDrone, cGroan, cInhale, cExhale, cHeart, cSplash, cBump;
        AudioClip cBlip, cRecOn, cRecOff, cDiscover, cFanfare, cCoin;
        readonly AudioClip[] created = new AudioClip[15];
        int createdCount;

        // runtime state
        bool inGame;
        long lastMoney;
        float lastCoin = -10f, lastBlip = -10f, lastFanfare = -10f, lastJingle = -10f;
        string lastQuest;
        bool pauseApplied;
        float heartTimer, groanTimer = 30f;
        float vWaves = -1f, vJungle = -1f, vDrone = -1f, pDrone = -1f, cutoffApplied = -1f;

        public FXAudio(Transform parent)
        {
            root = new GameObject("FX_Audio");
            rootT = root.transform;
            rootT.SetParent(parent, false);

            waves = MakeSource("FX_Waves", true, 40, out wavesLpf);
            jungle = MakeSource("FX_Jungle", true, 45, out jungleLpf);
            drone = MakeSource("FX_UnderwaterDrone", true, 40, out _);
            breath = MakeSource("FX_Breath", false, 60, out _);
            heart = MakeSource("FX_Heartbeat", false, 60, out _);
            deep = MakeSource("FX_DeepGroan", false, 90, out _);
            sfx = MakeSource("FX_Sfx", false, 70, out _);
            ui = MakeSource("FX_UI", false, 50, out _);
            ui.ignoreListenerPause = true;

            try
            {
                task = Task.Run(() => FXAudioSynth.Generate());
            }
            catch (Exception)
            {
                task = null;
                bank = FXAudioSynth.Generate();
            }
        }

        AudioSource MakeSource(string name, bool loop, int priority, out AudioLowPassFilter lpf)
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
            if (name == "FX_Waves" || name == "FX_Jungle")
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
            for (int i = 0; i < createdCount; i++)
                if (created[i] != null) UnityEngine.Object.Destroy(created[i]);
            createdCount = 0;
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
            if (!inGame || !gain || Now - lastCoin < 0.12f) return;
            lastCoin = Now;
            Play(ui, cCoin, 0.5f);
        }

        void OnGameStarted()
        {
            inGame = true;
            lastMoney = GameManager.I != null ? GameManager.I.State.money : 0;
            lastQuest = null;
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

        // ------------------------------------------------------------------ loading
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
            if (!ready)
            {
                TryFinishLoading();
                return;
            }
            if (pause) return;

            TickAmbience(f);
            TickDiver(f, b);
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

            float wavesAbove = (0.3f + 0.32f * nearShore) * heightFall;
            float jungleAmt = 1f - FXMath.Smooth01((distXZ - WorldLayout.IslandRadius * 0.55f) / (WorldLayout.IslandRadius * 0.9f));
            float jungleAbove = 0.36f * jungleAmt * Mathf.Lerp(1f, 0.55f, f.night01);

            float u = f.under01;
            float shallow = 1f - FXMath.Smooth01(f.depth / 18f);
            float tWaves = Mathf.Lerp(wavesAbove, wavesAbove * 0.65f * shallow, u);
            float tJungle = jungleAbove * (1f - u);
            float tDrone = u * Mathf.Lerp(0.32f, 0.6f, FXMath.Smooth01(f.depth / 60f));
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
            float v = current < 0f ? 0f : current;
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
