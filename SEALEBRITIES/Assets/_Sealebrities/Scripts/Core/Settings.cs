using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    public enum GraphicsQuality { Low, Medium, High }

    public enum VoiceMode { PushToTalk, OpenMic, Off }

    /// One rebound action (only actions that differ from the defaults are stored).
    [Serializable]
    public class BindingEntry
    {
        public string action;           // GameAction name
        public string key;              // UnityEngine.InputSystem.Key name ("None" for mouse bindings)
        public int mouseButton = -1;    // -1 none, 0 left, 1 right, 2 middle, 3 back, 4 forward
    }

    /// Per-machine player settings (never part of the save / never synced in co-op).
    [Serializable]
    public class SettingsData
    {
        public int version = Settings.CurrentVersion;
        // audio (0..1). AudioListener.volume = master; modules scale their sources by music / sfx / voice.
        public float master = 1f, music = 0.7f, sfx = 1f, voice = 1f;
        public float mouseSensitivity = 1f;
        public bool invertY;
        /// Frames per second cap, 0 = unlimited.
        public int fpsLimit = 60;
        public bool vsync = true;
        public GraphicsQuality quality = GraphicsQuality.High;
        /// F.5: open mic by default (v3 migrates PushToTalk → OpenMic once – the player never chose PTT).
        public VoiceMode voiceMode = VoiceMode.OpenMic;
        public bool micMuted;
        public Language language = Language.German;
        // co-op identity / character look (1c – edited on the character screen)
        public string playerName = "";
        public Color suitColor = new Color(1f, 0.55f, 0.2f);
        public int hat;
        /// Legacy combined index skin * 5 + mouth (kept in sync by Validate).
        public int face;
        public List<BindingEntry> bindings = new List<BindingEntry>();

        // ---- v3 (round 2)
        /// Field of view 60–100° (Q98).
        public float fov = Settings.DefaultFov;
        /// Q99 toggles: every shake source × 0 when off (footage steadiness is not affected).
        public bool headBob = true, cameraShake = true;
        /// A.2 "Tutorial-Hilfe von Horst" (off = beats are skipped, the phone task list still works).
        public bool npcTutorial = true;
        /// NPC/radio subtitles.
        public bool subtitles = true;
        /// Q42: the launch intro plays only until it was seen once.
        public bool introSeen;
        /// Q79: a finale was finished on Normal or Hard on this PC → Schwer unlocked.
        public bool normalCompleted;
        /// F.3 stable random key of this machine (GUID "N"). Never the Steam id, never logged. Use Settings.PlayerKey.
        public string playerKey = "";
        /// Q44: looks (no names) of the friends from the last co-op session – the menu crew.
        public List<DiverLook> lastCrew = new List<DiverLook>();
        /// 1c look fields. cuffColor alpha 0 = Auto; mask/fins alpha 0 = default colour.
        public Color cuffColor = new Color(0f, 0f, 0f, 0f);
        public int pattern, eyes, brows, skin, mouth;
        public Color maskColor = new Color(0f, 0f, 0f, 0f);
        public Color finsColor = new Color(0f, 0f, 0f, 0f);
        public bool snorkel = true;
        /// F.5 noise gate for the open mic in dBFS (−45, 300 ms hang time).
        public float voiceGateDb = -45f;
        /// Every cosmetic this machine has ever seen unlocked (character screen in the main menu / as a co-op guest).
        public List<string> unlockedCosmetics = new List<string>();
        /// Achievements of THIS machine (F.6 – each machine keeps its own list).
        public List<string> achievements = new List<string>();

        // ---- v3 (round 2b)
        /// K.2: recording starts by itself 0.5 s after going under water with the camera in hand.
        public bool autoRecordUnderwater = true;
        /// K.6: background music also plays under water.
        public bool musicWhileDiving = true;
        /// L.10 task display: 0 Normal, 1 Small (text only, 18 px), 2 Off.
        public int questDisplay;
    }

    /// Settings in persistentDataPath/settings.json. Load + Apply run in GameManager.Awake.
    /// Apply sets the frame cap, vSync, master volume, language and input bindings and raises Changed
    /// (FX applies the graphics quality, audio modules re-read their volumes, the Net module the voice mode).
    public static class Settings
    {
        /// Values offered by the settings screen (0 = unlimited).
        public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144, 0 };
        public const int MaxNameLength = 16;
        public const float DefaultFov = 70f, MinFov = 60f, MaxFov = 100f;
        public const float DefaultMaster = 1f, DefaultMusic = 0.7f, DefaultSfx = 1f, DefaultVoice = 1f, DefaultSensitivity = 1f;
        public const float DefaultVoiceGateDb = -45f;

        public static SettingsData Data { get; private set; } = new SettingsData();
        /// False when settings.json did not exist at the last Load (first start of this version).
        public static bool FileExisted { get; private set; }
        public static event Action Changed;

        /// 2: the graphics tier is picked once from the GPU (files of version 1 all carried the old default "High").
        /// 3: round 2 – look fields (skin/mouth from face), PushToTalk → OpenMic once, playerKey, introSeen for existing
        ///    players, NaN-safe values (1e).
        public const int CurrentVersion = 3;

        static bool dirty;
        // "-quality low|medium|high" on the command line: used for this run only, never written to settings.json
        // (the two-window co-op test shares one settings file)
        static bool qualityOverridden;
        static GraphicsQuality qualityOverride, persistedQuality;

        static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static void Load()
        {
            var d = new SettingsData();
            FileExisted = false;
            bool migrate = true;
            int fromVersion = 0;
            try
            {
                if (File.Exists(FilePath))
                {
                    FileExisted = true;
                    var loaded = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        d = loaded;
                        fromVersion = d.version;
                        migrate = d.version < CurrentVersion;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: could not read settings: " + e.Message); }
            if (FileExisted && fromVersion < 3) MigrateV3(d, fromVersion);
            Validate(d);
            if (migrate)
            {
                // first start (or a settings file from before the hardware detection): pick the tier for this PC once
                if (fromVersion < 2) d.quality = DetectQuality();
                d.version = CurrentVersion;
                Debug.Log("Sealebrities: graphics quality " + d.quality + " for '" + SystemInfo.graphicsDeviceName + "' (" + SystemInfo.graphicsMemorySize + " MB)");
            }
            bool keyCreated = false;
            if (string.IsNullOrEmpty(d.playerKey))
            {
                d.playerKey = Guid.NewGuid().ToString("N");
                keyCreated = true;   // write the new key at once (never logged)
            }
            persistedQuality = d.quality;
            qualityOverridden = TryQualityArg(out qualityOverride);
            if (qualityOverridden) d.quality = qualityOverride;
            Data = d;
            Loc.Current = d.language;
            GameInput.ApplyBindings(d.bindings);
            dirty = false;
            cachedPlayerKey = null;
            if ((migrate && FileExisted) || keyCreated) Save();
        }

        /// One-time v2 → v3 migration of an existing settings file.
        static void MigrateV3(SettingsData d, int from)
        {
            // the player never chose push-to-talk – it was the old default (F.5)
            if (d.voiceMode == VoiceMode.PushToTalk) d.voiceMode = VoiceMode.OpenMic;
            // look: the combined face index becomes skin + mouth
            int f = Mathf.Max(0, d.face);
            d.skin = Mathf.Clamp(f / 5, 0, 5);
            d.mouth = Mathf.Clamp(f % 5, 0, 4);
            // JsonUtility keeps field initializers only for fields present in the class – old files lack the new ones
            d.fov = DefaultFov;
            d.headBob = true; d.cameraShake = true; d.npcTutorial = true; d.subtitles = true;
            d.snorkel = true;
            d.voiceGateDb = DefaultVoiceGateDb;
            d.autoRecordUnderwater = true; d.musicWhileDiving = true; d.questDisplay = 0;
            d.cuffColor = new Color(0f, 0f, 0f, 0f);
            d.maskColor = new Color(0f, 0f, 0f, 0f);
            d.finsColor = new Color(0f, 0f, 0f, 0f);
            // players with an existing settings file have seen the launch intro already (Q42)
            d.introSeen = true;
        }

        static string cachedPlayerKey;

        /// The stable player key of this machine for the host's per-player records (F.3). Two windows on one PC share
        /// settings.json, so "-netname X" (or "-playerkey X") on the command line derives a separate key for that window.
        /// Never log it.
        public static string PlayerKey
        {
            get
            {
                if (cachedPlayerKey != null) return cachedPlayerKey;
                string k = string.IsNullOrEmpty(Data.playerKey) ? "local" : Data.playerKey;
                string over = CommandLineValue("-playerkey");
                if (!string.IsNullOrEmpty(over)) k = over;
                else
                {
                    string netName = CommandLineValue("-netname");
                    if (!string.IsNullOrEmpty(netName)) k = k + "-" + FindsDatabase.StableHash(netName).ToString("x8");
                }
                cachedPlayerKey = k;
                return k;
            }
        }

        static string CommandLineValue(string flag)
        {
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { return null; }
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        /// Set by the automated co-op test (AutoPlaytestMP): its two windows share the player's settings.json, so neither
        /// writes it (achievements, menu crew, look changes of the test stay in memory).
        public static bool WritesDisabled;

        public static void Save()
        {
            dirty = false;
            if (WritesDisabled) return;
            var d = Data;
            GraphicsQuality q = d.quality;
            try
            {
                Validate(d);
                q = d.quality;
                // a command-line override is not the player's choice: keep the stored tier unless it was changed in-game
                if (qualityOverridden && q == qualityOverride) d.quality = persistedQuality;
                else { persistedQuality = q; qualityOverridden = false; }
                File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: could not save settings: " + e.Message); }
            finally { d.quality = q; }
        }

        /// One-time pick of the graphics tier. Integrated GPUs (AMD "Radeon(TM) Graphics"/Vega, Intel UHD/Iris/HD) →
        /// Medium (their graphicsMemorySize is only the carve-out, so it is not used for them); dedicated GPUs by VRAM.
        static GraphicsQuality DetectQuality()
        {
            string n = SystemInfo.graphicsDeviceName ?? "";
            if (Has(n, "Radeon(TM) Graphics") || Has(n, "Radeon Graphics") || Has(n, "Radeon Vega") || Has(n, "Radeon(TM) Vega")
                || Has(n, "Intel(R) UHD") || Has(n, "Intel(R) Iris") || Has(n, "Intel(R) HD"))
                return GraphicsQuality.Medium;
            int mem = SystemInfo.graphicsMemorySize;
            if (mem > 0 && mem < 1500) return GraphicsQuality.Low;
            if (mem > 0 && mem < 3000) return GraphicsQuality.Medium;
            return GraphicsQuality.High;
        }

        static bool Has(string s, string part) => s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        static bool TryQualityArg(out GraphicsQuality q)
        {
            q = GraphicsQuality.High;
            string[] args;
            try { args = Environment.GetCommandLineArgs(); }
            catch (Exception) { return false; }
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "-quality", StringComparison.OrdinalIgnoreCase)) continue;
                string v = args[i + 1].Trim().ToLowerInvariant();
                if (v == "low") { q = GraphicsQuality.Low; return true; }
                if (v == "medium") { q = GraphicsQuality.Medium; return true; }
                if (v == "high") { q = GraphicsQuality.High; return true; }
            }
            return false;
        }

        /// Applies the current values to the engine and notifies listeners. Call after changing Data (then Save).
        public static void Apply()
        {
            var d = Data;
            Validate(d);
            // vSync ignores targetFrameRate on desktop. An explicit cap below the refresh rate wins: it uses every n-th
            // vblank when that hits the cap (60 on 120/180/240 Hz), otherwise vSync is traded for the frame cap
            // (before, 30 and 60 both gave 72 fps on a 144 Hz screen).
            float refresh = 60f;
            try
            {
                float r = (float)Screen.currentResolution.refreshRateRatio.value;
                if (r >= 20f && !float.IsNaN(r) && !float.IsInfinity(r)) refresh = r;
            }
            catch (Exception) { }
            int vs, target;
            if (!d.vsync) { vs = 0; target = d.fpsLimit > 0 ? d.fpsLimit : -1; }
            else if (d.fpsLimit <= 0 || d.fpsLimit >= refresh - 1f) { vs = 1; target = -1; }
            else
            {
                int n = Mathf.RoundToInt(refresh / d.fpsLimit);
                if (n >= 1 && n <= 4 && Mathf.Abs(refresh / n - d.fpsLimit) <= 2f) { vs = n; target = -1; }
                else { vs = 0; target = d.fpsLimit; }
            }
            QualitySettings.vSyncCount = vs;
            Application.targetFrameRate = target;
            AudioListener.volume = d.master;
            if (Loc.Current != d.language) Loc.SetLanguage(d.language);
            // in a session the Net module owns the name (it may come from -netname); it is re-read when a session starts
            if (!Net.IsOnline) Net.LocalName = DisplayName;
            Changed?.Invoke();
        }

        /// Apply + Save in one call (settings screen "OK" / every slider release).
        public static void ApplyAndSave()
        {
            Apply();
            Save();
        }

        /// Marks the settings as changed; GameManager writes them to disk within ~2 s (cheap for sliders).
        public static void MarkDirty() => dirty = true;

        public static void SaveIfDirty()
        {
            if (dirty) Save();
        }

        /// Restores audio/video/control defaults but keeps language, name, look, profile flags and the player key.
        public static void ResetToDefaults()
        {
            var old = Data;
            var d = new SettingsData
            {
                language = old.language, playerName = old.playerName, suitColor = old.suitColor, hat = old.hat, face = old.face,
                quality = DetectQuality(),
                // identity / look / profile (never reset by "Standard")
                playerKey = old.playerKey, introSeen = old.introSeen, normalCompleted = old.normalCompleted,
                lastCrew = old.lastCrew, cuffColor = old.cuffColor, pattern = old.pattern, eyes = old.eyes, brows = old.brows,
                skin = old.skin, mouth = old.mouth, maskColor = old.maskColor, finsColor = old.finsColor, snorkel = old.snorkel,
                unlockedCosmetics = old.unlockedCosmetics, achievements = old.achievements,
            };
            Data = d;
            GameInput.ResetBindings();   // also clears d.bindings and saves
            Apply();
            Save();
        }

        /// The player's name for co-op (never empty).
        public static string DisplayName =>
            string.IsNullOrWhiteSpace(Data.playerName) ? Loc.Tr("Taucher", "Diver") : Data.playerName.Trim();

        /// Character look of the local player for DiverModels.Build (menu crew, co-op avatar, character preview).
        /// Cuff is resolved (Auto → suit lightened 55 %, alpha 1) so callers can use it as a colour directly; the
        /// "Auto" choice itself lives in Data.cuffColor (alpha 0). Visible gear (Q51) and the rank badge (Q50) come
        /// from the running game when one is loaded.
        public static DiverLook Look
        {
            get
            {
                var d = Data;
                var l = new DiverLook
                {
                    lookVersion = DiverLook.CurrentVersion,
                    suit = d.suitColor,
                    hat = d.hat,
                    skin = d.skin,
                    mouth = d.mouth,
                    face = d.skin * 5 + d.mouth,
                    pattern = d.pattern,
                    eyes = d.eyes,
                    brows = d.brows,
                    mask = d.maskColor,
                    fins = d.finsColor,
                    snorkel = d.snorkel,
                    sitting = false
                };
                l.cuff = new Color(d.cuffColor.r, d.cuffColor.g, d.cuffColor.b, d.cuffColor.a);
                l.cuff = l.ResolvedCuff;
                var gm = GameManager.I;
                if (gm != null && gm.InGame && gm.State != null)
                {
                    var g = gm.Gear;
                    l.tankTier = g.tankTier;
                    l.finTier = g.finTier;
                    l.lampOnStrap = g.hasLamp && !Hands.IsHeld(HoldItem.Flashlight);
                    l.cameraOnStrap = !Hands.IsHeld(HoldItem.Camera);
                    l.rank = Progression.RankIndex(gm.State.followers);
                }
                return l;
            }
        }

        /// Writes a look edited on the character screen into the settings (call ApplyAndSave afterwards).
        public static void SetLook(DiverLook l, bool cuffAuto)
        {
            var d = Data;
            l = l.Normalized();
            d.suitColor = l.suit;
            d.cuffColor = cuffAuto ? new Color(0f, 0f, 0f, 0f) : new Color(l.cuff.r, l.cuff.g, l.cuff.b, 1f);
            d.hat = l.hat;
            d.skin = l.skin;
            d.mouth = l.mouth;
            d.face = l.face;
            d.pattern = l.pattern;
            d.eyes = l.eyes;
            d.brows = l.brows;
            d.maskColor = l.mask;
            d.finsColor = l.fins;
            d.snorkel = l.snorkel;
        }

        /// Volume helpers (already excluding master, which is applied by AudioListener.volume).
        public static float MusicVolume => Data.music;
        public static float SfxVolume => Data.sfx;
        public static float VoiceVolume => Data.voice;
        /// True when the local microphone may transmit right now (voice mode, mute and push-to-talk key).
        public static bool MicLive
        {
            get
            {
                var d = Data;
                if (d.micMuted) return false;
                switch (d.voiceMode)
                {
                    case VoiceMode.OpenMic: return true;
                    case VoiceMode.PushToTalk: return GameInput.Held(GameAction.PushToTalk);
                    default: return false;
                }
            }
        }

        /// A finite number or the fallback (NaN / ±∞ – a misclicked slider once stored NaN, 1e).
        public static float Finite(float v, float fallback) => float.IsNaN(v) || float.IsInfinity(v) ? fallback : v;

        /// 0..1 volume; NaN/∞ (Clamp01 lets NaN through) → the default, then clamped.
        public static float Volume01(float v, float fallback) => Mathf.Clamp01(Finite(v, fallback));

        static Color FiniteColor(Color c, Color fallback)
        {
            if (float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b) || float.IsNaN(c.a)
                || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b) || float.IsInfinity(c.a)) return fallback;
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), Mathf.Clamp01(c.a));
        }

        /// Repairs every value (NaN/∞ → default, then clamped; indices into range). Public so the settings screen and the
        /// AutoPlaytest slider step can run it (1e).
        public static void Validate(SettingsData d)
        {
            if (d == null) return;
            d.master = Volume01(d.master, DefaultMaster);
            d.music = Volume01(d.music, DefaultMusic);
            d.sfx = Volume01(d.sfx, DefaultSfx);
            d.voice = Volume01(d.voice, DefaultVoice);
            d.mouseSensitivity = Finite(d.mouseSensitivity, DefaultSensitivity);
            if (d.mouseSensitivity <= 0f) d.mouseSensitivity = DefaultSensitivity;
            d.mouseSensitivity = Mathf.Clamp(d.mouseSensitivity, 0.1f, 5f);
            d.fov = Mathf.Clamp(Finite(d.fov, DefaultFov), MinFov, MaxFov);
            d.voiceGateDb = Mathf.Clamp(Finite(d.voiceGateDb, DefaultVoiceGateDb), -80f, -10f);
            if (d.fpsLimit < 0) d.fpsLimit = 0;
            if (d.fpsLimit > 0) d.fpsLimit = Mathf.Clamp(d.fpsLimit, 20, 500);
            if ((int)d.quality < 0 || (int)d.quality > (int)GraphicsQuality.High) d.quality = GraphicsQuality.High;
            if ((int)d.voiceMode < 0 || (int)d.voiceMode > (int)VoiceMode.Off) d.voiceMode = VoiceMode.OpenMic;
            if ((int)d.language < 0 || (int)d.language > (int)Language.English) d.language = Language.German;
            if (d.playerName == null) d.playerName = "";
            if (d.playerName.Length > MaxNameLength) d.playerName = d.playerName.Substring(0, MaxNameLength);
            d.suitColor = FiniteColor(d.suitColor, new Color(1f, 0.55f, 0.2f));
            d.suitColor.a = 1f;
            d.cuffColor = FiniteColor(d.cuffColor, new Color(0f, 0f, 0f, 0f));
            d.maskColor = FiniteColor(d.maskColor, new Color(0f, 0f, 0f, 0f));
            d.finsColor = FiniteColor(d.finsColor, new Color(0f, 0f, 0f, 0f));
            d.hat = Mathf.Clamp(d.hat, 0, CosmeticsDatabase.HatCount - 1);
            d.pattern = Mathf.Clamp(d.pattern, 0, CosmeticsDatabase.PatternCount - 1);
            d.eyes = Mathf.Clamp(d.eyes, 0, CosmeticsDatabase.EyesCount - 1);
            d.brows = Mathf.Clamp(d.brows, 0, CosmeticsDatabase.BrowsCount - 1);
            d.skin = Mathf.Clamp(d.skin, 0, CosmeticsDatabase.SkinTones.Length - 1);
            d.mouth = Mathf.Clamp(d.mouth, 0, CosmeticsDatabase.MouthCount - 1);
            d.face = d.skin * 5 + d.mouth;
            d.questDisplay = Mathf.Clamp(d.questDisplay, 0, 2);
            if (d.bindings == null) d.bindings = new List<BindingEntry>();
            if (d.lastCrew == null) d.lastCrew = new List<DiverLook>();
            if (d.lastCrew.Count > 3) d.lastCrew.RemoveRange(3, d.lastCrew.Count - 3);
            if (d.unlockedCosmetics == null) d.unlockedCosmetics = new List<string>();
            if (d.achievements == null) d.achievements = new List<string>();
            if (d.playerKey == null) d.playerKey = "";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Data = new SettingsData();
            FileExisted = false;
            Changed = null;
            dirty = false;
        }
    }
}
