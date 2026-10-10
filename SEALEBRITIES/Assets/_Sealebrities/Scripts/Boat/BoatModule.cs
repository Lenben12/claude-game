using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the Boat module: the boat "Rostlaube" (Refs.Boat), the helicopter delivery service and the
    /// fishing spot on the dock. Called once by GameBootstrap after the world and the player exist.
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

            HeliService.Create(moduleRoot);
            var fishing = FishingSpot.Create(moduleRoot);
            BoatDockBoarding.Create(moduleRoot, boat, fishing);
        }
    }

    /// Finds the walkable / floating surface below a point: highest static collider hit (ignoring crates, the player
    /// and dynamic bodies such as the boat), the terrain, or the moving water surface if that is higher.
    public static class BoatSurface
    {
        static readonly RaycastHit[] Hits = new RaycastHit[24];

        public static float Below(Vector3 from, float maxDist, out bool water)
        {
            float solid = WorldInfo.GroundHeight(from);
            int n = Physics.RaycastNonAlloc(from, Vector3.down, Hits, maxDist, ~0, QueryTriggerInteraction.Ignore);
            Transform player = Refs.Player != null ? Refs.Player.Root : null;
            for (int i = 0; i < n; i++)
            {
                var col = Hits[i].collider;
                if (col == null) continue;
                if (col is CharacterController) continue;
                if (col.attachedRigidbody != null && !col.attachedRigidbody.isKinematic) continue;
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
