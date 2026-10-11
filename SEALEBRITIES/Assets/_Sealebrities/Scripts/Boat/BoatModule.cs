using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the Boat module: the boat "Rostlaube" (Refs.Boat), the helicopter delivery service, the fishing
    /// rod (hotbar item HoldItem.FishingRod) with the rod rack on the dock, boarding from the dock and the dock ladder.
    /// Called once by GameBootstrap after the world and the player exist.
    public static class BoatModule
    {
        public static void Install(Transform root)
        {
            var moduleRoot = new GameObject("BoatModule").transform;
            moduleRoot.SetParent(root, false);

            Vector3 spawn = WorldInfo.BoatSpawn;
            float yaw = WorldInfo.BoatSpawnYaw;
            if (spawn == Vector3.zero)
            {
                spawn = WorldLayout.BoatSpawn;
                yaw = WorldLayout.BoatSpawnYaw;
            }
            var boat = BoatController.Create(moduleRoot, spawn, yaw);
            Refs.Boat = boat;

            Guard("HeliService", () => HeliService.Create(moduleRoot));
            Guard("FishingRodTool", () => FishingRodTool.Create(moduleRoot));
            Guard("FishingSpot", () => FishingSpot.Create(moduleRoot));
            Guard("BoatDockBoarding", () => BoatDockBoarding.Create(moduleRoot, boat));
            Guard("DockLadder", () => DockLadder.Create(moduleRoot));
        }

        /// One failing part must not take the rest of the module down (the boot logs and continues).
        static void Guard(string what, System.Action a)
        {
            try { a(); }
            catch (System.Exception e)
            {
                Debug.LogError("Sealebrities: BoatModule part " + what + " failed.");
                Debug.LogException(e);
            }
        }
    }

    /// Finds the walkable / floating surface below a point: highest static collider hit (ignoring crates, the player
    /// and dynamic bodies such as the boat), the terrain, or the moving water surface if that is higher.
    public static class BoatSurface
    {
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        /// The boat's rigidbody (ignored even while it is kinematic in co-op).
        public static Rigidbody BoatBody;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { BoatBody = null; }

        public static float Below(Vector3 from, float maxDist, out bool water)
        {
            float solid = WorldInfo.GroundHeight(from);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            var pl = Refs.Player;
            Transform player = pl != null && !(pl is Object po && po == null) ? pl.Root : null;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                if (col is CharacterController) continue;
                var body = col.attachedRigidbody;
                if (body != null && (!body.isKinematic || body == BoatBody)) continue;
                if (player != null && col.transform.IsChildOf(player)) continue;
                if (col.GetComponentInParent<HeliCrate>() != null) continue;
                float y = Hits[i].point.y;
                if (y > solid) solid = y;
            }
            float w = Waves.HeightAt(from.x, from.z);
            water = w > solid;
            return water ? w : solid;
        }

        /// True if a solid surface (dock, rock, beach) is above the water at this position.
        public static bool IsBlocked(Vector3 p)
        {
            Below(new Vector3(p.x, Waves.SeaLevel + 8f, p.z), 12f, out bool water);
            return !water;
        }
    }
}
