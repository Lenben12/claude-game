using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    // ---------------------------------------------------------------------------------------------
    // Cross-module contracts. Each module implements its interface and registers itself in Refs.
    // ---------------------------------------------------------------------------------------------

    /// Implemented by the first-person player (Player module).
    public interface IPlayer
    {
        Transform Root { get; }
        Camera View { get; }
        Vector3 Velocity { get; }
        /// Camera is below the water surface.
        bool IsUnderwater { get; }
        /// Body is in water (swimming at surface or diving).
        bool IsInWater { get; }
        /// Meters below the surface at the camera (0 when above water).
        float Depth { get; }
        float Air01 { get; }
        float AirSecondsLeft { get; }
        /// Current gear depth rating in meters.
        float MaxSafeDepth { get; }
        bool FlashlightOn { get; }
        bool IsSeated { get; }
        bool IsFainting { get; }
        /// Degrees per second of camera rotation (used for footage steadiness).
        float LookAngularSpeed { get; }
        /// Seat the player (e.g. boat driver seat). Movement is disabled, the player follows the seat.
        void SetSeated(Transform seat);
        void ClearSeated(Vector3 exitPosition);
        void TeleportTo(Vector3 position, float yawDegrees);
        /// Immediately refill air (e.g. on boat/land).
        void RefillAir();
    }

    /// Implemented by the boat (Boat module).
    public interface IBoat
    {
        Transform Root { get; }
        bool PlayerAboard { get; }
        bool PlayerDriving { get; }
        /// Safe deck position used when the player is rescued after fainting.
        Vector3 RescuePoint { get; }
        void PlaceAt(Vector3 position, float yawDegrees);
    }

    /// Anything the player can use with the interact key (E). Found via GetComponentInParent on a raycast hit.
    public interface IInteractable
    {
        /// Localized prompt, e.g. "E – Schlafen". Return null/empty to hide.
        string GetPrompt();
        bool CanInteract();
        void Interact();
    }

    /// A living (or hallucinated) animal. Implemented by the Fauna module.
    public interface ICreature
    {
        SpeciesDef Species { get; }
        float SizeCm { get; }
        SizeTier Tier { get; }
        Transform Transform { get; }
        Vector3 Position { get; }
        Bounds WorldBounds { get; }
        /// 0..1 how "action-packed" the animal is right now (fleeing, hunting, bumping, displaying).
        float Activity01 { get; }
        bool IsHallucination { get; }
        bool IsAlive { get; }
    }

    public interface ICreatureSpawner
    {
        ICreature Spawn(SpeciesDef species, Vector3 position, float sizeCm, bool hallucination);
        void Despawn(ICreature creature);
    }

    /// Implemented by the hand camera (Filming module).
    public interface IHandCamera
    {
        bool IsAiming { get; }
        bool IsRecording { get; }
        float TapeSecondsLeft { get; }
        float TapeSecondsTotal { get; }
        float Zoom { get; }
        /// Species currently being captured and its progress 0..1 toward a usable clip (null when none).
        SpeciesDef CurrentTarget { get; }
        float CurrentTargetProgress01 { get; }
        /// Quality 0..1.6 of the current target framing (for viewfinder feedback).
        float CurrentTargetQuality { get; }
    }

    public static class Refs
    {
        public static IPlayer Player;
        public static IBoat Boat;
        public static ICreatureSpawner Spawner;
        public static IHandCamera HandCamera;
        public static Transform WorldRoot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Player = null; Boat = null; Spawner = null; HandCamera = null; WorldRoot = null; }
    }

    public static class CreatureRegistry
    {
        public static readonly List<ICreature> Active = new List<ICreature>();

        public static void Register(ICreature c) { if (c != null && !Active.Contains(c)) Active.Add(c); }
        public static void Unregister(ICreature c) { Active.Remove(c); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active.Clear(); }
    }

    /// Modal UI flags. The UI module owns cursor locking based on these.
    public static class UIState
    {
        public static bool PhoneOpen;
        public static bool PauseOpen;
        public static bool MainMenuOpen;
        public static bool LoadingOpen;
        public static bool BlocksGameplay => PhoneOpen || PauseOpen || MainMenuOpen || LoadingOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { PhoneOpen = false; PauseOpen = false; MainMenuOpen = true; LoadingOpen = true; }
    }

    /// Current interaction hint written by the Player module's interactor, shown by the UI (null = none).
    public static class InteractionPrompt
    {
        public static string Text;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Text = null; }
    }

    /// Lighting facts published every frame by the World module's day/night cycle; read by FX, Filming and Fauna.
    public static class EnvState
    {
        /// Fog/haze color to use above water.
        public static Color SkyFogColor = new Color(0.62f, 0.75f, 0.85f);
        /// 0 at night .. 1 at noon (sunlight strength, already including sunrise/sunset fade).
        public static float SunIntensity01 = 1f;
        /// 1 deep night .. 0 day.
        public static float Night01;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SkyFogColor = new Color(0.62f, 0.75f, 0.85f); SunIntensity01 = 1f; Night01 = 0f; }
    }

    /// Screen fade service. The UI module assigns Handler; without it the action runs immediately.
    public static class Fader
    {
        public static Action<Action, float> Handler;

        public static void FadeThrough(Action atBlack, float duration = 1.2f)
        {
            if (Handler != null) Handler(atBlack, duration);
            else atBlack?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Handler = null; }
    }

    /// Data holder placed on every procedurally built creature model by the Fauna mesh factory.
    /// Model convention: built at a body length of exactly 1 m along +Z (head at +Z, tail at -Z),
    /// origin at the body center, +Y up. The spawner scales the root uniformly by sizeCm / 100.
    public class CreatureRig : MonoBehaviour
    {
        public BodyPlan plan;
        /// Spine segments from head (index 0) to tail; rotate around local Y for swimming waves.
        public Transform[] spine = new Transform[0];
        public Transform tailFin;
        public Transform[] pectoralFins = new Transform[0];
        /// Ray wings, turtle/sea-lion flippers.
        public Transform[] flippers = new Transform[0];
        /// Jelly/octopus/squid arms; each is the root of a chain or a single bone.
        public Transform[] tentacles = new Transform[0];
        /// Jelly bell / cuttlefish mantle for pulsing (optional).
        public Transform bell;
        public Light lureLight;
        public Renderer[] renderers = new Renderer[0];
        /// Length of the model in meters at localScale 1 (should be 1).
        public float bodyLength = 1f;
    }

    /// Fixed world layout shared by World, Boat, Player and Fauna modules. Heights (y) are resolved from the terrain at runtime.
    public static class WorldLayout
    {
        public const float TerrainSize = 2048f;          // terrain spans [-1024, 1024] on X and Z
        public const float TerrainMinY = -170f;          // deepest possible seafloor
        public const float TerrainHeightRange = 230f;    // terrain covers -170 .. +60
        public const int HeightmapResolution = 2049;

        public static readonly Vector3 IslandCenter = new Vector3(0f, 0f, 0f);
        public const float IslandRadius = 190f;          // shoreline radius (irregular)
        public const float IslandPeak = 42f;             // jungle hill / lighthouse cliff

        public static readonly Vector3 Hut = new Vector3(118f, 0f, 18f);
        public static readonly Vector3 Bed = new Vector3(116f, 0f, 22f);
        public static readonly Vector3 TV = new Vector3(121f, 0f, 14f);
        public static readonly Vector3 Helipad = new Vector3(70f, 0f, -70f);
        public static readonly Vector3 DockStart = new Vector3(168f, 0f, 6f);
        public static readonly Vector3 DockEnd = new Vector3(206f, 0f, 6f);
        public static readonly Vector3 BoatSpawn = new Vector3(212f, 0f, 14f);
        public const float BoatSpawnYaw = 90f;
        public static readonly Vector3 FishingSpot = new Vector3(205f, 0f, 3f);
        public static readonly Vector3 Lighthouse = new Vector3(-30f, 0f, 150f);
        public static readonly Vector3 HiddenBay = new Vector3(-185f, 0f, -30f);
        public static readonly Vector3 PlayerSpawn = new Vector3(114f, 0f, 22f);
        public const float PlayerSpawnYaw = 90f;
    }

    /// Runtime world facts filled in by the World module once the world is built.
    public static class WorldInfo
    {
        public static bool Built;
        public static Terrain Terrain;
        public static Vector3 PlayerSpawn;
        public static float PlayerSpawnYaw;
        public static Vector3 BedPosition;
        public static Vector3 DockEndPosition;
        public static Vector3 BoatSpawn;
        public static float BoatSpawnYaw;
        public static Vector3 HelipadPosition;
        public static Vector3 FishingSpot;
        public static Vector3 LighthousePosition;
        public static Vector3 MenuCameraPosition;
        public static Vector3 MenuCameraLookAt;
        public static Light Sun;

        /// Optional height source (x, z) → world y. The low-poly mesh terrain sets this; it wins over Terrain.
        public static Func<float, float, float> HeightProvider;

        /// Ground (terrain) height at a world position; -60 when no terrain exists.
        public static float GroundHeight(Vector3 p)
        {
            if (HeightProvider != null) return HeightProvider(p.x, p.z);
            if (Terrain == null) return -60f;
            return Terrain.SampleHeight(p) + Terrain.transform.position.y;
        }

        /// Water depth (positive meters) of the seafloor at a position.
        public static float SeafloorDepth(Vector3 p) => Mathf.Max(0f, Waves.SeaLevel - GroundHeight(p));

        public static DiveSite SiteAt(Vector3 p)
        {
            DiveSite best = null;
            float bestD = float.MaxValue;
            foreach (var s in DiveSites.All)
            {
                float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(s.center.x, s.center.z));
                if (d < s.radius && d < bestD) { best = s; bestD = d; }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Built = false; Terrain = null; Sun = null; HeightProvider = null; }
    }
}
