using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Entry point (props engineer): the main island in the "How to Fish" look – hut, dock (working ladder), helipad,
    /// lighthouse area, points of interest (waterfall with a secret alcove, cave with Onkel Gus' hidden film grotto,
    /// rope bridge over the ravine, tide pools, second beach, radio tower ruins, hidden bay), the trail network with
    /// signposts, beach life, the layered jungle with GPU-instanced grass, the small landmark islands next to the
    /// outer dive sites and the optional collectibles (logbook pages, golden shells, treasures, secrets, message in a
    /// bottle). Called by WorldBuilder after the terrain exists: "yield return IslandProps.Build(root)".
    /// Every group is isolated (a failure is logged and the rest still builds) and work is spread over frames.
    /// All placement is deterministic (fixed seeds) so every co-op machine builds the identical island; the graphics
    /// quality only changes render distances / grass density (PropQuality), never placement or colliders.
    public static class IslandProps
    {
        public static IEnumerator Build(Transform root)
        {
            PropInfo.PathPoints.Clear();
            PropPaths.ClearConnections();
            var island = PropUtil.WorldRoot("IslandProps", root);

            // reserve the key spots first so nothing grows into them
            var excl = new PropExclusion(16f);
            var pathMask = new PropExclusion(16f);
            var skip = new PropExclusion(16f);   // no trail strip here (bridge deck, cave, pool)
            excl.Add(WorldLayout.Hut.x + 1.3f, WorldLayout.Hut.z, 10f);
            excl.AddLine(WorldLayout.DockStart - new Vector3(6f, 0f, 0f), WorldLayout.DockEnd, 3.5f);
            excl.Add(WorldLayout.Helipad.x, WorldLayout.Helipad.z, 10.5f);
            excl.Add(WorldLayout.Lighthouse.x, WorldLayout.Lighthouse.z, 10f);
            excl.Add(WorldLayout.PlayerSpawn.x, WorldLayout.PlayerSpawn.z, 2f);
            excl.Add(WorldLayout.BoatSpawn.x, WorldLayout.BoatSpawn.z, 7f);
            excl.Add(WorldLayout.FishingSpot.x, WorldLayout.FishingSpot.z, 3f);

            yield return PropRun.Safe(PropMats.PrewarmIsland(), "island materials");
            yield return PropRun.Safe(PropJungle.PrepareKit(), "plant kit");

            // buildings
            yield return PropRun.Safe(PropHut.Build(island, excl), "hut");
            yield return PropRun.Safe(PropStructures.BuildDock(island, excl), "dock");
            yield return PropRun.Safe(PropStructures.BuildHelipad(island, excl), "helipad");
            yield return PropRun.Safe(PropStructures.BuildLighthouse(island, excl), "lighthouse");

            // points of interest
            yield return PropRun.Safe(PropPOI.Waterfall(island, excl, skip), "waterfall");
            yield return PropRun.Safe(PropPOI.Cave(island, excl, skip), "cave");
            yield return PropRun.Safe(PropPOI.Bridge(island, excl, skip), "rope bridge");
            yield return PropRun.Safe(PropPOI2.TidePools(island, excl), "tide pools");
            yield return PropRun.Safe(PropPOI2.SecondBeach(island, excl), "second beach");
            yield return PropRun.Safe(PropPOI2.RadioTower(island, excl), "radio tower ruins");
            yield return PropRun.Safe(PropBeach.BuildHiddenBay(island, excl), "hidden bay");

            // trails, beach life, vegetation
            yield return PropRun.Safe(PropPaths.Build(island, excl, pathMask, skip), "paths");
            yield return PropRun.Safe(PropBeach.Build(island, excl), "beach props");
            var grass = PropGrassField.Create(island, "Grass");
            yield return PropRun.Safe(PropJungle.Build(island, excl, pathMask, grass), "jungle");

            // landmark islands next to the outer dive sites
            yield return PropRun.Safe(PropMiniIslands.Build(island, grass), "mini islands");
            try { grass.Finish(); }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: grass failed: " + e); }

            // optional collectibles (need every POI built)
            yield return PropRun.Safe(PropCollect.Build(island, excl), "collectibles");

            PropJungle.ReleaseKit();
            PropInfo.IslandBuilt = true;
        }
    }
}
