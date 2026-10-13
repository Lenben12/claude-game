using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Habitat anchor kinds (L.3) – the open-world spawner places bottom species at a matching anchor.
    /// SpeciesDef.habitat strings: "anemone", "crevice", "cleaning", "overhang", "sand", "seafan", "seagrass".
    public enum HabitatKind : byte { Anemone, Crevice, Cleaning, Overhang, Sand, SeaFan, SeaGrass }

    /// One habitat anchor on the seafloor (published by the reef / site builders in PropInfo.Habitats).
    [Serializable]
    public struct HabitatAnchor
    {
        public Vector3 position;
        /// Usable radius around the position (m).
        public float radius;
        public HabitatKind kind;
        /// Zone the anchor belongs to (dive-site id, "lagoon" …).
        public string zone;

        static readonly string[] Ids = { "anemone", "crevice", "cleaning", "overhang", "sand", "seafan", "seagrass" };
        /// The SpeciesDef.habitat string of this kind.
        public string Id => Ids[(int)kind < Ids.Length ? (int)kind : 0];

        public static bool TryParse(string id, out HabitatKind kind)
        {
            for (int i = 0; i < Ids.Length; i++)
                if (string.Equals(Ids[i], id, StringComparison.Ordinal)) { kind = (HabitatKind)i; return true; }
            kind = HabitatKind.Sand;
            return false;
        }
    }

    /// Land-fauna home spot kinds (L.11).
    public enum LandSpotKind : byte
    {
        /// Bird perch (post top, roof ridge, railing, mast) – normal = up.
        Perch,
        /// Bird nest / roost on a cliff ledge.
        Nest,
        /// Crab burrow on the sand / in the jungle floor.
        Burrow,
        /// Fenced pen (goats, chickens) – radius = pen size.
        Pen,
        /// Vertical wall a gecko can cling to – normal = outward.
        Wall,
        /// Palm trunk (gecko, coconut crab, fruit bat) – position = trunk base, normal = up.
        Palm,
        /// Sun-warmed rock to bask on (iguana) – radius = rock size.
        Bask,
        /// Open meadow area (goats grazing, butterflies) – radius = area.
        Meadow,
        /// Tree / palm crown to roost in (fruit bats).
        Roost,
        /// Railing (capuchin on the lighthouse gallery, gulls on the dock).
        Railing,
        /// Beach strip (crabs, gulls, pig walk) – radius = area.
        Shore,
        /// Shallow swim spot (pig at the village beach, iguana in the lagoon).
        Water,
    }

    /// One land-fauna home spot (published in PropInfo.LandSpots).
    [Serializable]
    public struct LandSpot
    {
        public Vector3 position;
        /// Up for perches/ground spots, outward for walls.
        public Vector3 normal;
        public float radius;
        public LandSpotKind kind;
        /// Area tag: "dock", "hut", "beach", "village", "lighthouse", "radio", "tidepools", "bay", "jungle", "helipad", "cliffs", "waterfall", "driftwood".
        public string area;
    }

    /// A sleep spot (L.9): the hut bed, two porch hammocks, a sofa bunk in the hut (one per possible player), plus
    /// Toni's hammock etc. The Player module lies the camera down at `lie` looking along `yaw`.
    [Serializable]
    public struct SleepSpotInfo
    {
        public const byte Bed = 0, Hammock = 1, Bunk = 2;
        public string id;
        /// Head-end eye position when lying (world).
        public Vector3 lie;
        /// Yaw (deg) of the body axis from head to feet.
        public float yaw;
        /// Where the player stands when getting up (world, on the floor).
        public Vector3 stand;
        public float standYaw;
        public byte kind;
        /// Round 3 (GDD §19 „Figur liegt wirklich in der Hängematte / im Bett“): the point on the mattress / cloth under
        /// the pelvis (the lying diver's root, Vector3.zero = unknown → derived from `lie`), the torso incline (head end
        /// up, degrees) and the leg incline (feet up). Filled by PropBed.RegisterBed / RegisterHammock.
        public Vector3 hip;
        public float headRise, feetRise;
    }

    /// A water current volume (K.3): a capsule from start to end with the given radius; inside it the flow runs
    /// start → end at `speed` m/s (fading over the outer 30 % of the radius). The Player module applies the force,
    /// worldfx draws streaming particles along it.
    [Serializable]
    public struct CurrentInfo
    {
        public string id;
        public string site;
        public Vector3 start, end;
        public float radius;
        public float speed;
        /// Flow inside a cave / swim-through (never pushes against the exit).
        public bool cave;
    }

    /// Optional facts published by the props module after IslandProps/UnderwaterDecor ran.
    /// Other modules may read these (e.g. Fauna for moray holes / anemones) but must not rely on them being filled.
    public static class PropInfo
    {
        /// Top surface height of the hut floor (world y).
        public static float HutFloorY = float.NaN;
        public static Vector3 HutDoor;
        public const float DockDeckY = 1.3f;
        public static Vector3 DockLadderTop, DockLadderBottom;
        public static float HelipadTopY = float.NaN;
        public static Vector3 TreasureChest;
        /// Floor center of the boathouse next to the dock start (zero when it could not be placed).
        public static Vector3 Boathouse;
        /// Lighthouse gallery floor (world) and the base of its exterior ladder.
        public static Vector3 LighthouseGallery, LighthouseLadderFoot, LighthouseBench;
        /// Points of interest as actually built (zero when a builder failed).
        public static Vector3 WaterfallLip, WaterfallNiche, PoolCenter, CaveMouth, CaveChamber, Grotto, GrottoTable, GrottoCamera;
        public static Vector3 BridgeNorth, BridgeSouth, BridgeMid, BridgeBelow;
        public static Vector3 TidePoolsCenter, SecondBeachSpot, Hammock, HelmetSpot, RadioShack, RadioTowerBase;
        /// Mini island centers (top surface) keyed like DiveSites ids: kelp_forest, wreck, blue_drop, deep_trench, fog_isle.
        public static readonly List<Vector3> MiniIslandTops = new List<Vector3>();
        /// Landmark spot per mini island (keys: kelp_forest, wreck, blue_drop, deep_trench, fog_isle).
        public static readonly Dictionary<string, Vector3> MiniLandmarks = new Dictionary<string, Vector3>();
        /// Golden shell spots on the mini island beaches.
        public static readonly List<Vector3> MiniShellSpots = new List<Vector3>();
        public static Vector3 Campfire, BayArch;
        public static Vector3 WreckCenter;
        public static Vector3 WhaleSkeleton;

        /// Anemone clusters on the house reef (clownfish homes).
        public static readonly List<Vector3> AnemoneSpots = new List<Vector3>();
        /// Dark hull openings of the wreck (moray homes) and their outward normals.
        public static readonly List<Vector3> WreckHoles = new List<Vector3>();
        public static readonly List<Vector3> WreckHoleNormals = new List<Vector3>();
        /// Hydrothermal vent tops in the deep trench.
        public static readonly List<Vector3> VentPositions = new List<Vector3>();
        /// Sample points of the island path (hut → lighthouse).
        public static readonly List<Vector3> PathPoints = new List<Vector3>();

        // =========================================================================================== round 2b contracts
        /// Every climbable ladder (L.13): dock, boat-side jetty ladders, ravine ×2, lighthouse wall ladder, rope ladder to
        /// the north beach (Q7), lighthouse maintenance ladder (L.6), radio tower (lowest 2.5 m missing), hut roof
        /// (L.12), tree house, boat-shed canoe rack, blue-drop pillar, wreck island … `bottom` = foot of the rails (ground,
        /// seafloor or water), `top` = the standing point after climbing out (on the platform / deck / roof),
        /// `outward` = horizontal unit vector away from the wall (the climber faces −outward), rung spacing 0.30 m.
        /// Until the Player module sets LaddersHandledByPlayer, every ladder also works as a fade + teleport fallback
        /// (PropLadder, E at the ladder).
        public static readonly List<LadderInfo> Ladders = new List<LadderInfo>();
        /// Set by the Player module once its generic ladder climb (L.13) reads Ladders: the props' fade-teleport
        /// fallback (PropLadder) then hides its prompts so E / Space reach the player's climb.
        public static bool LaddersHandledByPlayer;

        /// Old air stations (E.4) on the landmark islands' rest spots and the Fog Isle: E „Luft auffüllen“ (4 s, free).
        /// The props interactable refills by itself (IPlayer.RefillAir); the list is for the wrist computer / map.
        public static readonly List<Vector3> AirStations = new List<Vector3>();

        /// World-space AABB around the walk/swim-in interior of the MS Clickbait (H.1) – use InWreckInterior for the
        /// exact (rotated) test. Zero size until the wreck was built.
        public static Bounds WreckInterior;
        static Matrix4x4 wreckInteriorInv = Matrix4x4.identity;
        static Vector3 wreckInteriorHalf;
        static bool wreckInteriorValid;

        /// Exact test: is p inside the wreck interior volume (corridor, rooms, hold)?
        public static bool InWreckInterior(Vector3 p)
        {
            if (!wreckInteriorValid || !WreckInterior.Contains(p)) return false;
            Vector3 l = wreckInteriorInv.MultiplyPoint3x4(p);
            return Mathf.Abs(l.x) <= wreckInteriorHalf.x && Mathf.Abs(l.y) <= wreckInteriorHalf.y && Mathf.Abs(l.z) <= wreckInteriorHalf.z;
        }

        /// Called by the wreck builder: the interior as an oriented box (local half extents) + its world AABB.
        public static void SetWreckInterior(Matrix4x4 boxToWorld, Vector3 halfExtents)
        {
            wreckInteriorInv = boxToWorld.inverse;
            wreckInteriorHalf = halfExtents;
            var b = new Bounds(boxToWorld.MultiplyPoint3x4(Vector3.zero), Vector3.zero);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? -halfExtents.x : halfExtents.x, (i & 2) == 0 ? -halfExtents.y : halfExtents.y, (i & 4) == 0 ? -halfExtents.z : halfExtents.z);
                b.Encapsulate(boxToWorld.MultiplyPoint3x4(c));
            }
            WreckInterior = b;
            wreckInteriorValid = true;
        }

        /// Seafloor habitat anchors of every reef / site (L.3): anemones, crevices, cleaning bommies, overhangs, sand
        /// flats, sea fans, sea grass. The fauna spawner places bottom species at a matching anchor within 30 m.
        public static readonly List<HabitatAnchor> Habitats = new List<HabitatAnchor>();

        /// Nearest habitat anchor of a kind within maxDist (horizontal + vertical). No allocations.
        public static bool FindHabitat(HabitatKind kind, Vector3 near, float maxDist, out HabitatAnchor anchor)
        {
            anchor = default;
            float best = maxDist * maxDist;
            bool found = false;
            for (int i = 0; i < Habitats.Count; i++)
            {
                var h = Habitats[i];
                if (h.kind != kind) continue;
                float d = (h.position - near).sqrMagnitude;
                if (d < best) { best = d; anchor = h; found = true; }
            }
            return found;
        }

        /// Land-fauna home spots (L.11): perches, nests, burrows, pens, walls, palms, bask rocks, meadows, roosts.
        public static readonly List<LandSpot> LandSpots = new List<LandSpot>();

        /// 12 reachable, flat crate positions for the „falsche Stelle“ chopper mishap (L.12), ≥ 3 m from the water edge,
        /// ground height in y. GameState Delivery.dropZone indexes this list. Names in DropZoneIds (same order):
        /// village_beach, tide_pools, lighthouse_yard, pool_beach, radio_plateau, driftwood_beach, bay_beach,
        /// ravine_rim, headland, helipad_meadow, waterfall_plateau, hut_beach.
        public static readonly List<Vector3> DropZones = new List<Vector3>();
        public static readonly List<string> DropZoneIds = new List<string>();

        /// Sleep spots (L.9). The props interactable lies the local player down (SleepVote.SetLying) and sets
        /// LocalSleepSpot; the Player module may read it to lie the camera down (−1 = not lying).
        public static readonly List<SleepSpotInfo> SleepSpots = new List<SleepSpotInfo>();
        public static int LocalSleepSpot = -1;

        /// Water currents (K.3): reef channel, kelp surge, wreck hull current, blue-drop drift, Fog Isle channel, caves.
        public static readonly List<CurrentInfo> Currents = new List<CurrentInfo>();

        /// Flow velocity (m/s, world) of the strongest current at p (zero outside every current). No allocations.
        public static Vector3 CurrentAt(Vector3 p)
        {
            Vector3 best = Vector3.zero;
            float bestMag = 0f;
            for (int i = 0; i < Currents.Count; i++)
            {
                var c = Currents[i];
                Vector3 ab = c.end - c.start;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-4f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - c.start, ab) / l2);
                float d = (p - (c.start + ab * t)).magnitude;
                if (d >= c.radius) continue;
                float k = 1f - Mathf.Clamp01((d - c.radius * 0.7f) / (c.radius * 0.3f));
                // fade in / out over the first and last 8 % so nobody gets flung at the ends
                k *= Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((1f - t) / 0.08f) * 0.85f + 0.15f;
                float mag = c.speed * k;
                if (mag > bestMag) { bestMag = mag; best = ab / Mathf.Sqrt(l2) * mag; }
            }
            return best;
        }

        /// Named spots for other modules (NPC work / evening / home spots, quest givers, berths, story places). Keys:
        ///  horst_bench, horst_tent, horst_dock_end, hangar, hangar_door, hangar_hammock, wanda_pad, berth_a, berth_b,
        ///  visitor_mooring, village_jetty, village_mooring (boat berth at the village jetty, SpotYaw = boat yaw),
        ///  meeting_house, tide_calendar, boat_shed, kiosk, stage, goat_pen, chicken_run, lantern_jetty, guesthouse,
        ///  village_football, toni_beach, toni_raft, toni_camp, toni_fog_camp, fog_grotto, fog_beach, swim_raft,
        ///  base_camp, coin_telescope, rope_swing, tree_house, cellar, golden_camera, gus_letter, locker, fridge,
        ///  grill_hut, grill_village, compressor, lighthouse_lamp, antenna_mast (M4, 92 m ledge), gus_radio (M2),
        ///  lucky_fin (M1), lens_gallery / lens_grotto / lens_radio (M3), wreck_door_two_player, wreck_vent,
        ///  kelp_chest_two_player, npc:&lt;id&gt;[:evening|:home] for the villagers (pearl, mira, tomas, ada, juno, lio, bg1…bg6).
        public static readonly Dictionary<string, Vector3> Spots = new Dictionary<string, Vector3>();
        public static readonly Dictionary<string, float> SpotYaws = new Dictionary<string, float>();

        public static bool Spot(string key, out Vector3 pos) => Spots.TryGetValue(key, out pos);
        public static float SpotYaw(string key) => SpotYaws.TryGetValue(key, out var y) ? y : 0f;

        public static void SetSpot(string key, Vector3 pos, float yaw = 0f)
        {
            if (string.IsNullOrEmpty(key)) return;
            Spots[key] = pos;
            SpotYaws[key] = yaw;
        }

        public static bool IslandBuilt, UnderwaterBuilt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HutFloorY = float.NaN; HelipadTopY = float.NaN;
            AnemoneSpots.Clear(); WreckHoles.Clear(); WreckHoleNormals.Clear(); VentPositions.Clear(); PathPoints.Clear(); MiniIslandTops.Clear(); MiniLandmarks.Clear(); MiniShellSpots.Clear();
            Ladders.Clear(); LaddersHandledByPlayer = false; AirStations.Clear(); Habitats.Clear(); LandSpots.Clear();
            DropZones.Clear(); DropZoneIds.Clear(); SleepSpots.Clear(); LocalSleepSpot = -1; Currents.Clear(); Spots.Clear(); SpotYaws.Clear();
            WreckInterior = new Bounds(); wreckInteriorValid = false;
            IslandBuilt = false; UnderwaterBuilt = false;
        }

        /// Clears the island-side lists (+ the habitat anchors) before IslandProps rebuilds.
        public static void ClearIsland()
        {
            Ladders.Clear(); AirStations.Clear(); LandSpots.Clear(); DropZones.Clear(); DropZoneIds.Clear(); SleepSpots.Clear();
            LocalSleepSpot = -1;
            // the island builds first (WorldBuilder): it also adds lagoon habitats (wrecklets, sea grass), so the
            // habitat list is cleared here and the under-water builder only appends
            Habitats.Clear();
        }

        /// Clears the underwater-side lists before UnderwaterDecor rebuilds.
        public static void ClearUnderwater()
        {
            AnemoneSpots.Clear(); WreckHoles.Clear(); WreckHoleNormals.Clear(); VentPositions.Clear(); Currents.Clear();
            WreckInterior = new Bounds(); wreckInteriorValid = false;
            FXCurrents.Clear();
        }

        // ------------------------------------------------------------------ registration helpers
        public static void AddLadder(Vector3 bottom, Vector3 top, Vector3 outward, byte kind, Transform owner = null)
        {
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            Ladders.Add(new LadderInfo { bottom = bottom, top = top, outward = outward, rungSpacing = 0.3f, kind = kind, owner = owner });
        }

        public static void AddHabitat(Vector3 p, float radius, HabitatKind kind, string zone)
        {
            Habitats.Add(new HabitatAnchor { position = p, radius = radius, kind = kind, zone = zone });
        }

        public static void AddLandSpot(Vector3 p, LandSpotKind kind, float radius, string area, Vector3 normal = default)
        {
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            LandSpots.Add(new LandSpot { position = p, normal = normal.normalized, radius = radius, kind = kind, area = area });
        }

        public static void AddCurrent(string id, string site, Vector3 a, Vector3 b, float radius, float speed, bool cave = false)
        {
            Currents.Add(new CurrentInfo { id = id, site = site, start = a, end = b, radius = radius, speed = Mathf.Min(speed, 1.4f), cave = cave });
            // the FX module shows streaming particles along every registered lane
            FXCurrents.Add(a, b, radius, Mathf.Min(speed, 1.4f));
        }
    }
}
