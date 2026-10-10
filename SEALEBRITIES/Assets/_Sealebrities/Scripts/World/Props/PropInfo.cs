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
            AnemoneSpots.Clear(); WreckHoles.Clear(); WreckHoleNormals.Clear(); VentPositions.Clear(); PathPoints.Clear();
            IslandBuilt = false; UnderwaterBuilt = false;
        }
    }
}
