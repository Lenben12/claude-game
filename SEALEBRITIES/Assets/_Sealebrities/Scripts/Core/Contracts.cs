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
        /// Inventory / equip screen (I key) – the world keeps running, but movement, look and the hotbar are blocked.
        public static bool InventoryOpen;
        /// Character screen (1c) is open – blocks gameplay; GameInput Left/Right/Forward/Back drive the preview instead.
        public static bool CharacterOpen;
        /// A text field has focus (FinTube title, name field – L.4): GameInput ignores every action except Pause (Esc);
        /// Enter is read by the UI itself (GameInput.KeyDown(Key.Enter)).
        public static bool TextInput;
        /// The selfie frame is shown (K.4). Does NOT block gameplay (WASD still moves at ×0.6).
        public static bool SelfieOpen;
        /// Round 3: the hut furnishing screen („Einrichten“, UIHutDecor) – blocks gameplay like the backpack.
        public static bool DecorOpen;
        /// Round 3 (npc step): the gesture wheel or the gift / trade picker at an NPC is open – blocks gameplay, frees the
        /// cursor (the wheel is picked with the mouse).
        public static bool NpcMenuOpen;
        /// Round 3 (GDD §19 „Während der Fahrt Zugriff auf die Karte“, Q36): the treasure-map overlay (M) is open. It does
        /// NOT block gameplay – the driver keeps steering with WASD, a passenger keeps sitting – but it frees the cursor
        /// (drag / zoom / pins), so mouse look, the hotbar wheel and the mouse actions (Use / Aim) pause meanwhile.
        public static bool MapOverlayOpen;
        public static bool BlocksGameplay => PhoneOpen || PauseOpen || MainMenuOpen || LoadingOpen || InventoryOpen || CharacterOpen || DecorOpen || NpcMenuOpen;
        /// The cursor is free (menus or the map overlay).
        public static bool CursorFree => BlocksGameplay || MapOverlayOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            PhoneOpen = false; PauseOpen = false; MainMenuOpen = true; LoadingOpen = true; InventoryOpen = false;
            CharacterOpen = false; TextInput = false; SelfieOpen = false; DecorOpen = false; NpcMenuOpen = false;
            MapOverlayOpen = false;
        }
    }

    /// Round 3 (GDD §19 „ein zweiter Sitzplatz, z. B. eine Sitzbank vorne“, Q36 „Beifahrer: Filmen + Karte ansehen“):
    /// seats on which the player keeps the hands free (the passenger bench on the boat's foredeck). Seated there the
    /// camera, the phone and the map work; walking / weapons / the selfie stay off like on every seat. The driver seat
    /// is NOT such a seat (both hands on the wheel).
    public static class SeatRules
    {
        static readonly List<Transform> passengerSeats = new List<Transform>();
        /// Eye height above a passenger seat's transform (the transform marks the sitter's root, hips at root + 0.36 m).
        public const float PassengerEyeHeight = 1.12f;
        /// Look range left / right on a passenger seat (degrees).
        public const float PassengerYawLimit = 150f;
        /// The LOCAL player sits on a passenger seat right now (set by the Player module).
        public static bool LocalPassenger;

        public static void RegisterPassengerSeat(Transform seat)
        {
            if (seat != null && !passengerSeats.Contains(seat)) passengerSeats.Add(seat);
        }

        public static bool IsPassengerSeat(Transform seat)
        {
            if (seat == null) return false;
            for (int i = passengerSeats.Count - 1; i >= 0; i--)
            {
                var s = passengerSeats[i];
                if (s == null) { passengerSeats.RemoveAt(i); continue; }
                if (s == seat) return true;
            }
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { passengerSeats.Clear(); LocalPassenger = false; }
    }

    /// Fixed render layers (1c, 1d). The world camera excludes Preview and ViewModel; CreatureIcons is the icon studio.
    public static class GameLayers
    {
        /// Character-screen preview stage at (0, 420, 0) – only the preview camera renders it.
        public const int Preview = 29;
        /// First-person held items (hands, lamp, camera, rod, phone, wrist computer) – drawn by the overlay camera.
        public const int ViewModel = 30;
        /// Creature icon studio (Fauna CreatureIcons).
        public const int CreatureIcons = 31;
        public const int PreviewMask = 1 << Preview, ViewModelMask = 1 << ViewModel, CreatureIconsMask = 1 << CreatureIcons;
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
    ///
    /// Round 2b (third player message s, Design_Runde2 L.5): the main island is 0.62 of its Testversion-3 width. Every
    /// map-scale anchor below is the TV3 value × 0.62 (verified in scratchpad/design2_l/r3); feature geometry (plateaus,
    /// dock width, cliff heights, cave passage, ravine width/depth, ladder lengths) keeps its size. Legacy map literals in
    /// other files are wrapped in Isl(x, z) (= TV3 value × IslandScale); offsets from an anchor stay unchanged.
    /// The four outer dive sites and their landmark islands do not move; the house reef moves with the dock (DiveSites).
    public static class WorldLayout
    {
        public const float TerrainSize = 2048f;          // terrain spans [-1024, 1024] on X and Z
        public const float TerrainMinY = -170f;          // deepest possible seafloor
        public const float TerrainHeightRange = 230f;    // terrain covers -170 .. +60
        public const int HeightmapResolution = 2049;

        /// Linear map scale of the main island against Testversion 3 (0.75 plan of 1a → 0.62, L.5). Used by Isl().
        public const float IslandScale = 0.62f;

        public static readonly Vector3 IslandCenter = new Vector3(0f, 0f, 0f);
        public const float IslandRadius = 118f;          // mean shoreline radius 122 m (irregular) – TV3 190
        public const float IslandPeak = 30f;             // lighthouse plateau – TV3 42

        public static readonly Vector3 Hut = new Vector3(73f, 0f, 11f);
        public static readonly Vector3 Bed = new Vector3(71f, 0f, 15f);                 // Hut + (-2, 4)
        public static readonly Vector3 TV = new Vector3(76f, 0f, 7f);                   // Hut + (3, -4)
        public static readonly Vector3 Helipad = new Vector3(43f, 0f, -43f);
        public static readonly Vector3 DockStart = new Vector3(104f, 0f, 4f);           // shoreline
        public static readonly Vector3 DockEnd = new Vector3(134f, 0f, 4f);             // dock 30 m, 2.75 m water at the end
        public static readonly Vector3 BoatSpawn = new Vector3(140f, 0f, 12f);          // DockEnd + (6, 8), water 2.9 m
        public const float BoatSpawnYaw = 90f;
        public static readonly Vector3 FishingSpot = new Vector3(133f, 0f, 1f);         // DockEnd + (-1, -3)
        public static readonly Vector3 Lighthouse = new Vector3(-19f, 0f, 93f);
        public static readonly Vector3 HiddenBay = new Vector3(-115f, 0f, -19f);
        /// Later days start at the bed. A NEW game starts at NewGameSpawn (prologue at the helipad, K.5).
        public static readonly Vector3 PlayerSpawn = new Vector3(69f, 0f, 15f);         // Hut + (-4, 4)
        public const float PlayerSpawnYaw = 90f;
        /// K.5 prologue: the player steps out of Wanda's chopper on the helipad (Helipad + (4, 0, 6)).
        public static readonly Vector3 NewGameSpawn = new Vector3(47f, 0f, -37f);
        public const float NewGameSpawnYaw = 45f;

        // Points of interest on the main island (World shapes the terrain for them, Props fills them with content).
        public static readonly Vector3 WaterfallTop = new Vector3(-43f, 0f, 43f);       // falls' lip (21.85 m)
        public static readonly Vector3 WaterfallPool = new Vector3(-28f, 0f, 18f);      // Top + (15, -25), pool beach
        /// L.5: the cave mouth sits in the waterfall cliff, 16 m west of the falls ("Wasserfall-Grotte", floor 14.4 m).
        public static readonly Vector3 CaveEntrance = new Vector3(-56f, 0f, 33.5f);
        public static readonly Vector3 Ravine = new Vector3(-12f, 0f, -25f);            // east-west ravine, rope bridge
        public static readonly Vector3 TidePools = new Vector3(93f, 0f, -59f);          // rocky south-east shore
        /// The old second beach – now the beach in front of the village Altriff (L.8).
        public static readonly Vector3 SecondBeach = new Vector3(25f, 0f, -110f);
        public static readonly Vector3 RadioTowerRuins = new Vector3(-72f, 0f, -72f);   // plateau 15 m

        // ---- round 2b anchors (K.5, K.8, L.5, L.8, L.12)
        /// Village terrace centre (1.7 m, ellipse 24 × 12 m half-axes); stilt houses stand in the lagoon south of it.
        public static readonly Vector3 Village = new Vector3(25f, 0f, -98f);
        /// Driftwood beach on the north-east coast – Toni washes up here (K.8); his raft lies in the shallows.
        public static readonly Vector3 DriftwoodBeach = new Vector3(60f, 0f, 100f);
        public static readonly Vector3 DriftwoodRaft = new Vector3(66f, 0f, 104f);
        /// Wanda's hangar shack on the helipad plateau (L.12).
        public static readonly Vector3 HangarSpot = new Vector3(56f, 0f, -52f);
        /// Floating swim raft with a diving board (L.5 detail spot 3). Round 3 (GDD §19 "Badeinsel weiter ins Meer"): moved
        /// from 14 m to ~40 m off the dock beach (was (118, 34)), north of the boat lane, ~3.5 m of water under it.
        public static readonly Vector3 SwimRaft = new Vector3(146f, 0f, 44f);

        // ---- round 3 (GDD §19 "Insel und Welt")
        /// The village Altriff is gone (round 3): WorldLayout.Village stays as the anchor of the open beach meadow on the
        /// south coast (the old terrace, jetty and lagoon remain; no houses). NPC / fauna code that still uses it is
        /// relocated by the NPC step (see docs/Runde3_Fortschritt.md, step 3).
        /// Hidden camp of the hermits / castaways on the far (west) side: a jungle hollow on the plateau between the
        /// waterfall grotto and the hidden bay, ringed by wooded ridges, no trail and no signpost leads there.
        public static readonly Vector3 HermitCamp = new Vector3(-92f, 0f, 16f);
        /// Radius of the flat camp floor (the wooded rim rises around it up to ~CampRimRadius).
        public const float HermitCampRadius = 12f, HermitCampRimRadius = 24f;
        /// Volcano island east of the main island (seen from the dock across the house reef): black-sand beach on its west
        /// side, a smoking crater, lava flows on the east flank. Fauna zone Zones.Volcano (prehistoric "living fossils").
        public static readonly Vector3 VolcanoIsland = new Vector3(700f, 0f, 120f);
        /// Mean shore radius and summit (crater rim) height of the volcano island.
        public const float VolcanoShoreRadius = 56f, VolcanoPeak = 36f;
        /// Spawn area of the volcano fauna zone (land + sea) around VolcanoIsland.
        public const float VolcanoZoneRadius = 190f;
        /// Player answer: the volcano becomes reachable with the WRECK unlock (VolcanoGate). Until then a strong current
        /// ring of this radius around VolcanoIsland pushes boats back out (the requirement is shown, nothing announced).
        public const float VolcanoGateRadius = 235f;
        /// "Urzeit-Schlund": a deep sinkhole on the volcano's east flank reserved for the big prehistoric monster
        /// (fauna step) – floor ~VolcanoAbyssDepth m, flat within ~22 m, steep walls out to VolcanoAbyssRadius.
        public static readonly Vector3 VolcanoAbyss = new Vector3(818f, 0f, 150f);
        public const float VolcanoAbyssRadius = 48f, VolcanoAbyssDepth = 128f;

        /// Small landmark islands next to the outer dive sites (center, radius in meters). UNCHANGED in round 2.
        public static readonly Vector3 KelpIsland = new Vector3(-470f, 0f, 420f);
        public static readonly Vector3 WreckIsland = new Vector3(600f, 0f, -470f);
        public static readonly Vector3 BlueDropIsland = new Vector3(-800f, 0f, -650f);
        public static readonly Vector3 TrenchIsland = new Vector3(915f, 0f, 840f);
        public const float MiniIslandRadius = 42f;

        /// The secret Fog Isle from Toni's map (K.8): landmark island (shore 30 m, peak 16 m), dive site "fog_isle".
        public static readonly Vector3 FogIsland = new Vector3(-60f, 0f, 700f);
        public const float FogIslandShoreRadius = 30f;
        /// Fog bank ring around the Fog Isle (K.8): without the map the boat drifts outward inside this radius.
        public const float FogBankRadius = 260f;
        /// Buoy lane through the fog (6 yellow buoys, 30 m wide) – clear once StoryKeys.FogMap is set.
        public static readonly Vector3 FogLaneStart = new Vector3(-60f, 0f, 420f);
        public static readonly Vector3 FogLaneEnd = new Vector3(-60f, 0f, 560f);
        public const float FogLaneWidth = 30f;

        /// A Testversion-3 map literal on the main island scaled to the round-2b island (x × IslandScale, z × IslandScale).
        /// Offsets from an anchor (hut.x + 6f …) are NOT wrapped.
        public static Vector2 Isl(float x, float z) => new Vector2(x * IslandScale, z * IslandScale);
        /// Same as Isl for a Vector3 literal (y unchanged).
        public static Vector3 Isl3(float x, float y, float z) => new Vector3(x * IslandScale, y, z * IslandScale);
    }

    /// Bridge between the UI (renders the phone screen into Texture) and the Player (shows a 3D phone in the hand
    /// with Texture on its screen and maps mouse positions onto it via ScreenToUV).
    public static class PhoneDisplay
    {
        public const int Width = 540;
        public const int Height = 1080;
        /// Created and rendered by the UI module.
        public static RenderTexture Texture;
        /// Set by the Player module: screen pixel position → phone screen UV (0..1, origin bottom-left), null when outside.
        public static Func<Vector2, Vector2?> ScreenToUV;
        /// Set by the Player module while the phone model is raised and visible.
        public static bool Raised;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Texture = null; ScreenToUV = null; Raised = false; }
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

    // =============================================================================================
    // Round 2 contracts (Design_Runde2 J.3 / J.3b). Stubs where the logic belongs to another module.
    // =============================================================================================

    // 1f "Zur Hütte" also brings the boat home: implemented by the Boat module in Boat/BoatHome.cs
    // (BoatHome.AfterLocalPlayerWentHome – the UI pause menu calls it; clients send NetCommand.Custom "BoatHome",
    // registered by BoatHome.Install through CoreCommands.RegisterCustom). J.3's BoatHome.Handler/Request is not needed.

    /// 1c character screen. Opened from the first start, main menu, co-op lobby, pause menu and the hut locker.
    public static class CharacterScreen
    {
        /// Set by the UI module (UICharacterScreen).
        public static Action<CharacterScreenContext> OpenHandler;
        /// Raised by the UI when the screen closes (saved = "Fertig" pressed, false = cancelled).
        public static event Action Closed;
        /// Raised by the UI after "Fertig" saved a new look (the Net module broadcasts NetSyncMsg.LookChanged).
        public static event Action LookSaved;

        public static bool IsOpen => UIState.CharacterOpen;

        public static void Open(CharacterScreenContext ctx)
        {
            if (OpenHandler != null) OpenHandler(ctx);
            else Debug.LogWarning("[Core] CharacterScreen.Open without a UI handler (" + ctx + ")");
        }

        public static void RaiseClosed() => Closed?.Invoke();
        public static void RaiseLookSaved() => LookSaved?.Invoke();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OpenHandler = null; Closed = null; LookSaved = null; }
    }

    /// 1c live 3D preview of a bean diver (implemented by Player/CharacterPreview on layer GameLayers.Preview).
    public interface ICharacterPreview
    {
        RenderTexture Texture { get; }
        void SetLook(DiverLook look);
        void AddYaw(float degrees);
        void Focus(PreviewFocus focus);
        /// The 0.6 s "ta-da" after a change.
        void PlayReaction();
        /// Destroys the stage, camera and render texture (call on close – zero cost while closed).
        void Dispose();
    }

    /// Set by the Player module. Argument = render texture size (1024 / 768 / 512 by quality tier).
    public static class CharacterPreviewFactory
    {
        public static Func<int, ICharacterPreview> Create;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Create = null; }
    }

    /// Published by the Boat module every FixedUpdate; read by fauna (dolphins), livestream (signal), wrist computer.
    public static class BoatTelemetry
    {
        public static bool Valid;
        public static Vector3 Position;
        /// m/s over ground.
        public static float Speed;
        public static bool Anchored;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Valid = false; Position = Vector3.zero; Speed = 0f; Anchored = false; }
    }

    /// Viewfinder feedback published by the Filming module every detection tick (K.2), drawn by the UI.
    /// Fixed arrays – no allocations; only the first BracketCount entries are valid.
    public static class ViewfinderInfo
    {
        public const int MaxBrackets = 8;
        public static ShotHint Hint;
        /// 0..1 photo/thumbnail quality of the current framing (Q31, L.4).
        public static float ThumbQuality01;
        /// Animals detected in frame right now ("● REC 0:42 · 3").
        public static int DetectedCount;
        /// Viewport rect of each detected animal: (xMin, yMin, xMax, yMax) in 0..1, origin bottom-left.
        public static readonly Vector4[] Brackets = new Vector4[MaxBrackets];
        /// (int)SizeTier of each bracket (colour).
        public static readonly byte[] BracketTier = new byte[MaxBrackets];
        /// Species not yet in the Sealog (pulsing #F2C94C bracket with "?").
        public static readonly bool[] BracketNew = new bool[MaxBrackets];
        public static int BracketCount;
        /// Valid Sealog photo subject inside the central 40 % frame (null = none) – the frame turns green.
        public static SpeciesDef SealogSubject;
        /// Round 3 (Q40 „kleine Symbole erkannter Tiere“): the distinct species detected in frame right now (max
        /// MaxBrackets) and whether each is already in the Sealog („?“ for new ones).
        public static readonly SpeciesDef[] DetectedSpecies = new SpeciesDef[MaxBrackets];
        public static readonly bool[] DetectedKnown = new bool[MaxBrackets];
        public static int DetectedSpeciesCount;

        public static void Clear()
        {
            Hint = ShotHint.None; ThumbQuality01 = 0f; DetectedCount = 0; BracketCount = 0; SealogSubject = null;
            for (int i = 0; i < DetectedSpecies.Length; i++) DetectedSpecies[i] = null;
            DetectedSpeciesCount = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clear(); }
    }

    /// K.4 selfie view state of the LOCAL player – set by the Player module, read by Filming / UI / Net (avatar bytes).
    public static class SelfieState
    {
        public static bool Active;
        /// (int)SelfieExpression currently shown (manual pick or automatic).
        public static int Expression;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; Expression = 0; }
    }

    /// One climbable ladder (L.13). Props publish every ladder in PropInfo.Ladders; the player climbs any of them.
    [Serializable]
    public struct LadderInfo
    {
        public const byte Wood = 0, Rope = 1, Metal = 2;
        public Vector3 bottom, top;
        /// Horizontal unit vector pointing away from the wall (the climber faces −outward).
        public Vector3 outward;
        /// 0.30 m by default.
        public float rungSpacing;
        /// Wood / Rope / Metal (grab sounds).
        public byte kind;
        /// The prop's ladder object (null = always usable). Inactive owner = the ladder is not there right now (the
        /// lighthouse cellar ladder until the trapdoor is open) – the generic climb must ignore it.
        public Transform owner;
        public float Length => Vector3.Distance(bottom, top);
        /// False while the owning prop object is switched off.
        public bool Usable => ReferenceEquals(owner, null) || (owner != null && owner.gameObject.activeInHierarchy);
    }

    /// Optional second animation surface of a built diver (L.13 motion set). The Player module's DiverPuppet implements
    /// it next to IDiverPuppet; avatars/NPCs check `puppet as IDiverMotion`.
    public interface IDiverMotion
    {
        /// state + speed (m/s) + phase (0..1 of the current cycle, sent by net so co-op avatars stay in step).
        void SetMotion(MotionState state, float speed, float phase, HoldItem right, HoldItem left, bool lightOn);
        /// Selfie / emote expression (SelfieExpression as int); 0 = automatic.
        void SetExpression(int expression);
    }

    /// K.3 animal event currently running near the local player (host: set by Fauna; clients: mirrored by Net 101).
    public static class FaunaEventInfo
    {
        /// Event id ("feeding_frenzy", "cleaning_station", "big_school", "encounter", "predator_hunt", "night_glow",
        /// "whale_song", "deep_parade") or null when none runs.
        public static string ActiveId;
        /// Zone of the event (site id, "lagoon", "open_sea" …).
        public static string ZoneId;
        public static Vector3 Center;
        /// Time.time on THIS machine when the event ends (net converts from remaining seconds).
        public static float EndsAt;
        public static event Action Changed;

        public static bool IsActive => !string.IsNullOrEmpty(ActiveId) && Time.time < EndsAt;

        /// Clips taken within this distance of the centre during the event get its eventId (K.3).
        public const float ClipRadius = 20f;

        public static void Start(string id, string zoneId, Vector3 center, float secondsLeft)
        {
            ActiveId = id; ZoneId = zoneId; Center = center; EndsAt = Time.time + Mathf.Max(0f, secondsLeft);
            Changed?.Invoke();
            GameEvents.RaiseFaunaEventStarted(id);
        }

        public static void End()
        {
            if (string.IsNullOrEmpty(ActiveId)) return;
            string id = ActiveId;
            ActiveId = null; ZoneId = null;
            Changed?.Invoke();
            GameEvents.RaiseFaunaEventEnded(id);
        }

        /// The event id a clip at this position should carry right now (null = none).
        public static string EventAt(Vector3 p) =>
            IsActive && (p - Center).sqrMagnitude <= ClipRadius * ClipRadius ? ActiveId : null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ActiveId = null; ZoneId = null; Center = Vector3.zero; EndsAt = 0f; Changed = null; }
    }

    /// Open-world spawn zones (L.1). FaunaZones.ZoneAt (fauna) returns one of these ids or a dive-site id; species list
    /// the zones they live in (SpeciesDef.zones). Bit() gives the JournalEntry.zoneMask bit (L.2).
    public static class Zones
    {
        public const string HouseReef = "house_reef", Kelp = "kelp_forest", Wreck = "wreck", BlueDrop = "blue_drop",
            Trench = "deep_trench", FogIsle = "fog_isle", Lagoon = "lagoon", OpenSea = "open_sea", DeepSea = "deep_sea",
            Island = "island", TidePools = "tide_pools", Beach = "beach";
        /// Round 3 (world step): the volcano island and its waters (WorldLayout.VolcanoIsland, VolcanoZoneRadius) – reserved
        /// for the prehistoric "living fossil" fauna (fauna step). Appended so the old zoneMask bits stay valid.
        public const string Volcano = "volcano_isle";

        /// Display order of the zone chips (Sealog "Fundorte").
        public static readonly string[] All =
            { HouseReef, Lagoon, Kelp, Wreck, BlueDrop, Trench, FogIsle, OpenSea, DeepSea, Island, TidePools, Beach, Volcano };

        /// Bit index of a zone in JournalEntry.zoneMask (-1 for unknown ids). Allocation free.
        public static int Index(string zone)
        {
            if (zone == null) return -1;
            for (int i = 0; i < All.Length; i++) if (string.Equals(All[i], zone, StringComparison.Ordinal)) return i;
            return -1;
        }

        public static int Bit(string zone) { int i = Index(zone); return i < 0 ? 0 : 1 << i; }

        public static string Name(string zone)
        {
            switch (zone)
            {
                case HouseReef: return Loc.Tr("Hausriff", "House Reef");
                case Kelp: return Loc.Tr("Kelpwald", "Kelp Forest");
                case Wreck: return Loc.Tr("Wrack", "Wreck");
                case BlueDrop: return Loc.Tr("Blauwasser", "Blue Water");
                case Trench: return Loc.Tr("Tiefseegraben", "Deep Trench");
                case FogIsle: return Loc.Tr("Nebelinsel", "Fog Isle");
                case Lagoon: return Loc.Tr("Lagune", "Lagoon");
                case OpenSea: return Loc.Tr("Hochsee", "Open Sea");
                case DeepSea: return Loc.Tr("Tiefsee", "Deep Sea");
                case Island: return Loc.Tr("Insel", "Island");
                case TidePools: return Loc.Tr("Gezeitentümpel", "Tide Pools");
                case Beach: return Loc.Tr("Strand", "Beach");
                case Volcano: return Loc.Tr("Vulkaninsel", "Volcano Isle");
                default: return zone ?? "";
            }
        }
    }

    /// Time-of-day phases for SpeciesDef.dayMask (L.1) and the music (K.6).
    public static class DayPhase
    {
        public const float DawnStart = 5.5f / 24f, DayStart = 7f / 24f, DuskStart = 17.5f / 24f, NightStart = 20.5f / 24f;

        public static DayMask Of(float timeOfDay)
        {
            float t = Mathf.Repeat(timeOfDay, 1f);
            if (t >= DayStart && t < DuskStart) return DayMask.Day;
            if ((t >= DuskStart && t < NightStart) || (t >= DawnStart && t < DayStart)) return DayMask.Dusk;
            return DayMask.Night;
        }

        public static DayMask Now => GameManager.I != null ? Of(GameManager.I.State.timeOfDay) : DayMask.Day;
    }

    // ---------------------------------------------------------------------------------------------
    // NPCs and dialogue (A, B, K.5, K.8, L.7, L.8). The npc+story module builds the NPCs and owns the lines
    // (Data/DialogueDatabase.cs); core only carries the bus so UI subtitles, music ducking and net sync work.
    // ---------------------------------------------------------------------------------------------

    /// A story NPC in the world (Horst, Wanda, Gus' radio, Dr. Fin, Chad & Brad, Pearl, villagers, Toni).
    public interface INpc
    {
        /// Stable id ("horst", "wanda", "gus", "fin", "chad", "brad", "pearl", "mira", "tomas", "ada", "juno", "lio", "toni" …).
        string Id { get; }
        Transform Root { get; }
        string DisplayName { get; }
        bool IsTalking { get; }
        /// Quest marker above the head: 0 none, 1 "!" (quest available), 2 "?" (ready to hand in) – L.7.
        int Marker { get; }
    }

    public static class NpcRegistry
    {
        public static readonly List<INpc> Active = new List<INpc>();

        public static void Register(INpc n) { if (n != null && !Active.Contains(n)) Active.Add(n); }
        public static void Unregister(INpc n) { Active.Remove(n); }

        public static INpc Find(string id)
        {
            for (int i = 0; i < Active.Count; i++) if (Active[i] != null && Active[i].Id == id) return Active[i];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active.Clear(); }
    }

    /// Dialogue bus. Lines travel by id + arguments; every machine localizes them itself (net StoryLine 98).
    public static class Dialogue
    {
        /// Set by the npc module: (lineId, args) → text in the CURRENT language. Null → the id itself is shown.
        public static Func<string, string[], string> Resolve;
        /// Raised for every spoken line (speakerId, lineId, seconds, args) – the Net module broadcasts it from the host.
        public static event Action<string, string, float, string[]> LineSpoken;

        /// Speaks a line locally: localizes, raises GameEvents.NpcLine (subtitles, babble, music ducking) and LineSpoken.
        public static void Say(string speakerId, string lineId, float seconds, params string[] args)
        {
            PlayLocal(speakerId, lineId, seconds, args);
            LineSpoken?.Invoke(speakerId, lineId, seconds, args);
        }

        /// Net: a line received from the host – shown locally without re-broadcasting.
        public static void PlayLocal(string speakerId, string lineId, float seconds, string[] args)
        {
            string text = Resolve != null ? Resolve(lineId, args) : lineId;
            GameEvents.RaiseNpcLine(speakerId, text ?? lineId, seconds);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Resolve = null; LineSpoken = null; }
    }
}
