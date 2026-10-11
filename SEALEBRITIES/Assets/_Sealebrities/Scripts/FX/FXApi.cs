using System;
using UnityEngine;

namespace Sealebrities
{
    // ---------------------------------------------------------------------------------------------
    // Public read/call surface of the FX module for the other modules. Everything here is cheap and
    // allocation-free; calls made before the FX module is installed are ignored.
    // ---------------------------------------------------------------------------------------------

    /// Current sight distance, published by the FX module every frame from the fog it actually applies.
    /// Filming uses it to limit the camera scan range, Fauna for spawn/visibility distances.
    public static class FXVisibility
    {
        /// Distance in meters at which an object keeps ~10 % of its own colour (the fog limit) for the current
        /// camera. Under water ≈ 19 m in the bright shallows, ≈ 13 m at 25 m, ≈ 10 m in the deep, ~1.5x less at
        /// night. Above water ≈ 1400 m by day (≈ 90 m inside the Fog Isle's fog bank).
        public static float Meters = 1400f;
        /// Distance in meters at which an object still keeps half of its own colour (clearly recognisable).
        public static float ClearMeters = 430f;
        /// True when the camera is below the surface (exponential-squared underwater fog is active).
        public static bool Underwater;
        /// Applied fog density and mode (exp2 = underwater).
        public static float Density = 0.0016f;
        public static bool Exp2;

        /// Recommended maximum distance for the hand camera to recognise ("scan") an animal: never beyond what
        /// the player can actually see clearly, capped at 15 m under water (art bible §2.2, feedback #16).
        public static float ScanMeters => Underwater ? Mathf.Clamp(ClearMeters * 1.35f, 4f, 15f) : 30f;

        /// Fraction 0..1 of an object's own colour that survives the current fog at a distance.
        public static float Fraction(float meters)
        {
            float k = Density * Mathf.Max(0f, meters);
            return Exp2 ? Mathf.Exp(-k * k) : Mathf.Exp(-k);
        }

        /// Under-water sight limit (10 % colour left) at a camera depth, independent of the current camera.
        public static float UnderwaterMeters(float depth, float night01) =>
            FXMath.SqrtLn10 / FXMath.UnderwaterFogDensity(depth, night01);

        /// Under-water "clearly recognisable" range (50 % colour left) at a camera depth.
        public static float UnderwaterClearMeters(float depth, float night01) =>
            FXMath.SqrtLn2 / FXMath.UnderwaterFogDensity(depth, night01);

        internal static void Publish(bool exp2, float density, bool underwater)
        {
            Exp2 = exp2;
            Density = Mathf.Max(1e-6f, density);
            Underwater = underwater;
            if (exp2)
            {
                Meters = FXMath.SqrtLn10 / Density;
                ClearMeters = FXMath.SqrtLn2 / Density;
            }
            else
            {
                Meters = FXMath.Ln10 / Density;
                ClearMeters = FXMath.Ln2 / Density;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Meters = 1400f; ClearMeters = 430f; Underwater = false; Density = 0.0016f; Exp2 = false;
        }
    }

    /// Graphics tier facts (Settings.Data.quality, applied by the FX module on Settings.Changed). Props/World can
    /// read the densities and cull distances here so every module uses the same art-bible table (§1.5).
    public static class FXQuality
    {
        static bool applied;
        static GraphicsQuality tier = GraphicsQuality.High;

        /// The tier in effect. Before the FX module is installed (e.g. while the world builds) it reads the saved
        /// setting directly, so props built during boot already use the right densities.
        public static GraphicsQuality Tier
        {
            get
            {
                if (applied) return tier;
                var d = Settings.Data;
                return d != null ? d.quality : GraphicsQuality.High;
            }
        }

        /// Raised after the FX module applied a tier different from the one in effect before.
        public static event Action Changed;

        /// Grass clump density multiplier (High 100 %, Medium 60 %, Low 30 %).
        public static float GrassDensity => Tier == GraphicsQuality.High ? 1f : Tier == GraphicsQuality.Medium ? 0.6f : 0.3f;
        /// Grass clump cull distance in meters.
        public static float GrassCullDistance => Tier == GraphicsQuality.High ? 60f : Tier == GraphicsQuality.Medium ? 40f : 25f;
        /// Tree / prop cull distance in meters.
        public static float PropCullDistance => Tier == GraphicsQuality.High ? 140f : Tier == GraphicsQuality.Medium ? 100f : 70f;
        /// Particle count multiplier for ambient particle effects.
        public static float ParticleScale => Tier == GraphicsQuality.High ? 1f : Tier == GraphicsQuality.Medium ? 0.6f : 0.35f;
        /// Number of underwater light shafts.
        public static int GodRays => Tier == GraphicsQuality.High ? 8 : Tier == GraphicsQuality.Medium ? 5 : 3;
        /// Animated caustics on the seabed.
        public static bool Caustics => Tier != GraphicsQuality.Low;

        internal static void Set(GraphicsQuality q)
        {
            bool changed = q != Tier;
            applied = true;
            tier = q;
            if (changed) Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { applied = false; tier = GraphicsQuality.High; Changed = null; }
    }

    /// One-shot sounds of the FX sound bank that other modules can trigger (all scaled by the SFX volume).
    public enum FXSound
    {
        Click, Ping, Coin, Shutter, Equip, Cast, Reel, Catch, Splash, Sparkle, Error, Warning, Discover, Fanfare
    }

    /// Play a procedural FX sound: FXSfx.Play(FXSound.Cast) when the rod whips forward, FXSound.Shutter for a
    /// photo, FXSound.Ping for a notification. 2D (UI-like); ignored until the sound bank is ready.
    public static class FXSfx
    {
        internal static Action<FXSound, float> Handler;

        public static void Play(FXSound sound, float volume = 1f)
        {
            var h = Handler;
            if (h != null) h(sound, Mathf.Clamp01(volume));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Handler = null; }
    }

    /// State of the procedural background music (K.6: menu, island day / evening / night, under water, deep). Read-only
    /// for other modules, e.g. the intro can cut its shots on the beat: FXMusic.Playing && FXMusic.BeatPhase < 0.1f.
    public static class FXMusic
    {
        /// Tempo of the track that plays (the menu track: 104 BPM).
        public static float Bpm { get; internal set; } = 104f;
        public static float BeatSeconds => 60f / Mathf.Max(1f, Bpm);
        /// True while a music track is audible.
        public static bool Playing;
        /// Seconds into the current loop.
        public static float Time;
        /// The track that plays (None while resting / silent).
        public static FXMusicTrack Current { get; internal set; }
        /// Beat index inside the loop (4 beats per bar, 32 bars).
        public static int Beat => (int)(Time / BeatSeconds);
        /// 0..1 position inside the current beat.
        public static float BeatPhase => Time / BeatSeconds - Beat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Playing = false; Time = 0f; Bpm = 104f; Current = FXMusicTrack.None; }
    }

    /// K.8 fog bank around the Fog Isle, published by the FX module every frame (read-only for other modules).
    public static class FXFogBank
    {
        /// 0..1 how deep the camera is inside the fog (0 outside the ring; thinned in the buoy lane once the map is known).
        public static float Thick01 { get; internal set; }
        /// The camera is inside the fog ring (WorldLayout.FogBankRadius around WorldLayout.FogIsland).
        public static bool CameraInside { get; internal set; }
        /// Toni's map is known (StoryKeys.FogMap): the fog is only a light haze and the lane is clear.
        public static bool Map { get; internal set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Thick01 = 0f; CameraInside = false; Map = false; }
    }

    /// K.3 currents: whoever builds a current volume (props: Riffkanal, kelp surge, the wreck hull, the blue-drop drift,
    /// the Fog Isle channel) registers it once as a lane – the FX module then shows streaming particles along it while
    /// the camera is near and under water. The push itself is the player module's (CurrentZone force).
    public static class FXCurrents
    {
        public struct Lane
        {
            public Vector3 start, end;
            public float radius, speed;
        }

        internal static readonly System.Collections.Generic.List<Lane> Lanes = new System.Collections.Generic.List<Lane>();

        /// A current flowing from start to end (world positions, centre line), radius in metres, speed in m/s.
        public static void Add(Vector3 start, Vector3 end, float radius, float speed)
        {
            if ((end - start).sqrMagnitude < 1f || radius <= 0.1f || float.IsNaN(speed)) return;
            if (Lanes.Count >= 32) return;
            Lanes.Add(new Lane { start = start, end = end, radius = Mathf.Min(radius, 40f), speed = Mathf.Clamp(speed, 0.05f, 3f) });
        }

        /// Removes every registered lane (world rebuilt).
        public static void Clear() { Lanes.Clear(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Lanes.Clear(); }
    }

    /// World-space particle effects other modules can spawn (fishing float splash, co-op divers, boat …).
    public static class FXEffects
    {
        internal static Action<Vector3, float> SplashHandler;
        internal static Action<Vector3, int> BubblesHandler;

        /// Water splash (droplets) at a world position; strength 0..1.
        public static void Splash(Vector3 position, float strength = 1f)
        {
            var h = SplashHandler;
            if (h != null) h(position, Mathf.Clamp01(strength));
        }

        /// A few rising bubbles at a world position (only below the surface).
        public static void Bubbles(Vector3 position, int count = 6)
        {
            var h = BubblesHandler;
            if (h != null) h(position, Mathf.Clamp(count, 1, 40));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SplashHandler = null; BubblesHandler = null; }
    }
}
