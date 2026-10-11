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
        public VoiceMode voiceMode = VoiceMode.PushToTalk;
        public bool micMuted;
        public Language language = Language.German;
        // co-op identity / character look
        public string playerName = "";
        public Color suitColor = new Color(1f, 0.55f, 0.2f);
        public int hat;
        public int face;
        public List<BindingEntry> bindings = new List<BindingEntry>();
    }

    /// Settings in persistentDataPath/settings.json. Load + Apply run in GameManager.Awake.
    /// Apply sets the frame cap, vSync, master volume, language and input bindings and raises Changed
    /// (FX applies the graphics quality, audio modules re-read their volumes, the Net module the voice mode).
    public static class Settings
    {
        /// Values offered by the settings screen (0 = unlimited).
        public static readonly int[] FpsOptions = { 30, 60, 90, 120, 144, 0 };
        public const int MaxNameLength = 16;

        public static SettingsData Data { get; private set; } = new SettingsData();
        /// False when settings.json did not exist at the last Load (first start of this version).
        public static bool FileExisted { get; private set; }
        public static event Action Changed;

        /// 2: the graphics tier is picked once from the GPU (files of version 1 all carried the old default "High").
        public const int CurrentVersion = 2;

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
            try
            {
                if (File.Exists(FilePath))
                {
                    FileExisted = true;
                    var loaded = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath));
                    if (loaded != null)
                    {
                        d = loaded;
                        migrate = d.version < CurrentVersion;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities: could not read settings: " + e.Message); }
            Validate(d);
            if (migrate)
            {
                // first start (or a settings file from before the hardware detection): pick the tier for this PC once
                d.quality = DetectQuality();
                d.version = CurrentVersion;
                Debug.Log("Sealebrities: graphics quality " + d.quality + " for '" + SystemInfo.graphicsDeviceName + "' (" + SystemInfo.graphicsMemorySize + " MB)");
            }
            persistedQuality = d.quality;
            qualityOverridden = TryQualityArg(out qualityOverride);
            if (qualityOverridden) d.quality = qualityOverride;
            Data = d;
            Loc.Current = d.language;
            GameInput.ApplyBindings(d.bindings);
            dirty = false;
            if (migrate && FileExisted) Save();
        }

        public static void Save()
        {
            dirty = false;
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

        /// Restores audio/video/control defaults but keeps language, name and look.
        public static void ResetToDefaults()
        {
            var old = Data;
            var d = new SettingsData
            {
                language = old.language, playerName = old.playerName, suitColor = old.suitColor, hat = old.hat, face = old.face,
                quality = DetectQuality()
            };
            Data = d;
            GameInput.ResetBindings();   // also clears d.bindings and saves
            Apply();
            Save();
        }

        /// The player's name for co-op (never empty).
        public static string DisplayName =>
            string.IsNullOrWhiteSpace(Data.playerName) ? Loc.Tr("Taucher", "Diver") : Data.playerName.Trim();

        /// Character look for DiverModels.Build (menu crew, co-op avatar).
        public static DiverLook Look => new DiverLook
        {
            suit = Data.suitColor,
            cuff = Color.Lerp(Data.suitColor, Color.white, 0.6f),
            hat = Data.hat,
            face = Data.face,
            sitting = false
        };

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

        static void Validate(SettingsData d)
        {
            d.master = Mathf.Clamp01(d.master);
            d.music = Mathf.Clamp01(d.music);
            d.sfx = Mathf.Clamp01(d.sfx);
            d.voice = Mathf.Clamp01(d.voice);
            if (float.IsNaN(d.mouseSensitivity) || d.mouseSensitivity <= 0f) d.mouseSensitivity = 1f;
            d.mouseSensitivity = Mathf.Clamp(d.mouseSensitivity, 0.1f, 5f);
            if (d.fpsLimit < 0) d.fpsLimit = 0;
            if (d.fpsLimit > 0) d.fpsLimit = Mathf.Clamp(d.fpsLimit, 20, 500);
            if ((int)d.quality < 0 || (int)d.quality > (int)GraphicsQuality.High) d.quality = GraphicsQuality.High;
            if ((int)d.voiceMode < 0 || (int)d.voiceMode > (int)VoiceMode.Off) d.voiceMode = VoiceMode.PushToTalk;
            if ((int)d.language < 0 || (int)d.language > (int)Language.English) d.language = Language.German;
            if (d.playerName == null) d.playerName = "";
            if (d.playerName.Length > MaxNameLength) d.playerName = d.playerName.Substring(0, MaxNameLength);
            d.suitColor.a = 1f;
            if (d.hat < 0) d.hat = 0;
            if (d.face < 0) d.face = 0;
            if (d.bindings == null) d.bindings = new List<BindingEntry>();
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
