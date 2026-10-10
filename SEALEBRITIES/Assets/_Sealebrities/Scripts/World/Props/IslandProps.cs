using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Entry point (props engineer): the lived-in scrap island – hut, dock, helipad, lighthouse, paths, beach life,
    /// hidden bay and the jungle. Called by WorldBuilder after the terrain exists: "yield return IslandProps.Build(root)".
    /// Every group is isolated (a failure is logged and the rest still builds) and work is spread over frames.
    public static class IslandProps
    {
        public static IEnumerator Build(Transform root)
        {
            PropInfo.PathPoints.Clear();
            var island = PropUtil.WorldRoot("IslandProps", root);

            // reserve the key spots first so nothing grows into them
            var excl = new PropExclusion(16f);
            excl.Add(WorldLayout.Hut.x + 1.3f, WorldLayout.Hut.z, 10f);
            excl.AddLine(WorldLayout.DockStart - new Vector3(6f, 0f, 0f), WorldLayout.DockEnd, 3.5f);
            excl.Add(WorldLayout.Helipad.x, WorldLayout.Helipad.z, 10.5f);
            excl.Add(WorldLayout.Lighthouse.x, WorldLayout.Lighthouse.z, 8f);
            excl.Add(WorldLayout.PlayerSpawn.x, WorldLayout.PlayerSpawn.z, 2f);
            excl.Add(WorldLayout.BoatSpawn.x, WorldLayout.BoatSpawn.z, 7f);
            excl.Add(WorldLayout.FishingSpot.x, WorldLayout.FishingSpot.z, 3f);

            yield return PropRun.Safe(PropMats.PrewarmIsland(), "island materials");
            yield return PropRun.Safe(PropHut.Build(island, excl), "hut");
            yield return PropRun.Safe(PropStructures.BuildDock(island, excl), "dock");
            yield return PropRun.Safe(PropStructures.BuildHelipad(island, excl), "helipad");
            yield return PropRun.Safe(PropStructures.BuildLighthouse(island, excl), "lighthouse");
            yield return PropRun.Safe(PropBeach.BuildPaths(island, excl), "paths");
            yield return PropRun.Safe(PropBeach.Build(island, excl), "beach props");
            yield return PropRun.Safe(PropBeach.BuildHiddenBay(island, excl), "hidden bay");
            yield return PropRun.Safe(PropJungle.Build(island, excl), "jungle");
            PropInfo.IslandBuilt = true;
        }
    }
}
