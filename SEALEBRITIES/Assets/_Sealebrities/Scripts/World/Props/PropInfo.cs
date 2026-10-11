using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
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
        /// Mini island centers (top surface) keyed like DiveSites ids: kelp_forest, wreck, blue_drop, deep_trench.
        public static readonly List<Vector3> MiniIslandTops = new List<Vector3>();
        /// Landmark spot per mini island (keys: kelp_forest, wreck, blue_drop, deep_trench).
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

        public static bool IslandBuilt, UnderwaterBuilt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HutFloorY = float.NaN; HelipadTopY = float.NaN;
            AnemoneSpots.Clear(); WreckHoles.Clear(); WreckHoleNormals.Clear(); VentPositions.Clear(); PathPoints.Clear(); MiniIslandTops.Clear(); MiniLandmarks.Clear(); MiniShellSpots.Clear();
            IslandBuilt = false; UnderwaterBuilt = false;
        }
    }
}
