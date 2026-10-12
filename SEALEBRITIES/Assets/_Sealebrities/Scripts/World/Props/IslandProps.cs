using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Entry point (props engineer): the main island in the "How to Fish" look – hut (veranda, hammocks, sofa bunk,
    /// roof ladder, locker, fridge, Gus' letter), dock (berths, Horst's bench), helipad with Wanda's hangar, the
    /// lighthouse (cellar, maintenance ladder, rope ladder to the north beach, coin telescope), the points of interest
    /// (Waterfall Grotto with the root curtain and the squeeze, the alcove behind the falls, rope swing, stepping
    /// stones, ravine ladders, tide pools, radio tower platform, hidden bay grotto), the south beach jetty (round 3: the
    /// village Altriff is gone) and the hidden hermit camp (PropCamp, round 3), the trail network with signposts, beach
    /// life, the L.5 detail pass, the layered jungle with GPU-instanced grass, the landmark islands (rest spots, secrets,
    /// the Fog Isle with Toni's camp), the volcano island (PropVolcano, round 3), the 94 collectibles (L.6) and the round-3
    /// survival props (campfires, daily finds, the 8 map pieces + the treasure X – PropSurvival).
    /// Called by WorldBuilder after the terrain exists: "yield return IslandProps.Build(root)".
    /// Every group is isolated (a failure is logged and the rest still builds) and work is spread over frames.
    /// All placement is deterministic (fixed seeds) so every co-op machine builds the identical island; the graphics
    /// quality only changes render distances / grass density (PropQuality), never placement or colliders.
    public static class IslandProps
    {
        public static IEnumerator Build(Transform root)
        {
            PropInfo.IslandBuilt = false;
            PropInfo.ClearIsland();
            PropInfo.PathPoints.Clear();
            PropPaths.ClearConnections();
            PropCoop.Install();
            // round 3 world step: the map pieces, campfires and daily finds are placed by PropSurvival.PlaceMapPieces
            MapPieces.UseDefaultPlacement = false;
            var island = PropUtil.WorldRoot("IslandProps", root);

            // reserve the key spots first so nothing grows into them
            var excl = new PropExclusion(16f);
            var pathMask = new PropExclusion(16f);
            var skip = new PropExclusion(16f);   // no trail strip here (bridge deck, cave, pool)
            excl.Add(WorldLayout.Hut.x + 1.3f, WorldLayout.Hut.z, 10f);
            excl.AddLine(WorldLayout.DockStart - new Vector3(6f, 0f, 0f), WorldLayout.DockEnd, 3.5f);
            excl.Add(WorldLayout.Helipad.x, WorldLayout.Helipad.z, 10.5f);
            excl.Add(WorldLayout.HangarSpot.x, WorldLayout.HangarSpot.z, 6f);
            excl.Add(WorldLayout.Lighthouse.x, WorldLayout.Lighthouse.z, 10f);
            excl.Add(WorldLayout.PlayerSpawn.x, WorldLayout.PlayerSpawn.z, 2f);
            excl.Add(WorldLayout.BoatSpawn.x, WorldLayout.BoatSpawn.z, 7f);
            excl.Add(WorldLayout.FishingSpot.x, WorldLayout.FishingSpot.z, 3f);
            excl.Add(WorldLayout.DriftwoodBeach.x, WorldLayout.DriftwoodBeach.z, 6f);

            yield return PropRun.Safe(PropMats.PrewarmIsland(), "island materials");
            yield return PropRun.Safe(PropJungle.PrepareKit(), "plant kit");

            // buildings
            yield return PropRun.Safe(PropHut.Build(island, excl), "hut");
            yield return PropRun.Safe(PropStructures.BuildDock(island, excl), "dock");
            yield return PropRun.Safe(PropStructures.BuildHelipad(island, excl), "helipad");
            yield return PropRun.Safe(PropStructures.BuildLighthouse(island, excl), "lighthouse");

            // points of interest (+ the round-2b additions: alcove, rope swing, ladders, platform, grotto …)
            yield return PropRun.Safe(PropPOI.Waterfall(island, excl, skip), "waterfall");
            yield return PropRun.Safe(PropPOI.Cave(island, excl, skip), "cave");
            yield return PropRun.Safe(PropPOI.Bridge(island, excl, skip), "rope bridge");
            yield return PropRun.Safe(PropPOI2.TidePools(island, excl), "tide pools");
            yield return PropRun.Safe(PropPOI2.RadioTower(island, excl), "radio tower ruins");
            yield return PropRun.Safe(PropBeach.BuildHiddenBay(island, excl), "hidden bay");
            yield return PropRun.Safe(PropPOI3.Waterfall(island, excl), "waterfall extras");
            yield return PropRun.Safe(PropPOI3.Ravine(island, excl), "ravine extras");
            yield return PropRun.Safe(PropPOI3.RadioTower(island, excl), "radio tower extras");
            yield return PropRun.Safe(PropPOI3.TidePools(island, excl), "tide pool extras");
            yield return PropRun.Safe(PropPOI3.HiddenBay(island, excl), "hidden bay extras");

            // round 3: the village Altriff is gone (only its jetty + a few canoes remain on the south beach); the hidden
            // hermit camp lies on the far (west) side of the island
            yield return PropRun.Safe(PropVillage.Build(island, excl), "south beach jetty");
            yield return PropRun.Safe(PropCamp.Build(island, excl), "hermit camp");

            // trails, beach life, the detail pass, vegetation
            yield return PropRun.Safe(PropPaths.Build(island, excl, pathMask, skip), "paths");
            yield return PropRun.Safe(PropBeach.Build(island, excl), "beach props");
            // round 3: Horst's corner on the beach south of the hut (his hammock between two palms – off the dock)
            yield return PropRun.Safe(PropHorst.Build(island, excl), "horst corner");
            yield return PropRun.Safe(PropDetails.Build(island, excl), "detail pass");
            var grass = PropGrassField.Create(island, "Grass");
            yield return PropRun.Safe(PropJungle.Build(island, excl, pathMask, grass), "jungle");

            // landmark islands next to the outer dive sites (+ rest spots, secrets, the Fog Isle)
            yield return PropRun.Safe(PropMiniIslands.Build(island, grass), "mini islands");
            // round 3: the volcano island (black sand, smoking crater, ferns – home of the living fossils)
            yield return PropRun.Safe(PropVolcano.Build(island, grass), "volcano island");
            try { grass.Finish(); }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: grass failed: " + e); }

            // optional collectibles (need every POI built)
            yield return PropRun.Safe(PropCollect.Build(island, excl), "collectibles");
            // round 3: campfire spots, daily pickups (driftwood, whetstones, fruit), map pieces + the treasure
            yield return PropRun.Safe(PropSurvival.BuildIsland(island, excl), "survival props");

            try { PublishSpots(); }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: spot publishing failed: " + e); }

            PropJungle.ReleaseKit();
            PropVolcanoPlants.Release();   // no-op when PropVolcano already released it (covers a failed volcano build)
            PropInfo.IslandBuilt = true;
        }

        /// The npc module's QuestSpots ids for spots of the props' own geometry (Wanda's hangar, the helipad, …).
        static void PublishSpots()
        {
            if (PropInfo.Spot("hangar_door", out var door) && PropInfo.Spot("hangar", out var hangar))
            {
                Vector3 outward = door - hangar;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : Vector3.forward;
                Vector3 side = Vector3.Cross(Vector3.up, outward);
                Vector3 front = door + outward * 1.6f;
                front.y = PropGround.H(front.x, front.z);
                QuestSpots.Register("hangar", front);
                Vector3 wanda = door + outward * 0.9f + side * 1.6f;
                wanda.y = PropGround.H(wanda.x, wanda.z);
                QuestSpots.Register("wanda_hangar", wanda);
            }
            if (PropInfo.Spot("wanda_pad", out var pad)) QuestSpots.Register("wanda_pad", pad);
            if (PropInfo.Spot("lighthouse_lamp", out var lamp) && PropInfo.LighthouseGallery != Vector3.zero)
                QuestSpots.Register("m3_lamp", PropInfo.LighthouseGallery + new Vector3(0f, 0.2f, 0f));
            if (PropInfo.LighthouseGallery != Vector3.zero)
            {
                Vector3 toHut = WorldLayout.Hut - WorldLayout.Lighthouse;
                toHut.y = 0f;
                toHut.Normalize();
                // Lio's kite hangs at the gallery railing on the hut side, the M3 shard lies on the gallery floor
                QuestSpots.Register("q_kite", PropInfo.LighthouseGallery + toHut * 2.55f + Vector3.up * 0.95f);
                QuestSpots.Register("m3_shard1", PropInfo.LighthouseGallery - toHut * 1.9f + Vector3.Cross(Vector3.up, toHut) * 0.9f + Vector3.up * 0.05f);
            }
            if (PropInfo.Spot("bay_campfire", out var fire)) QuestSpots.Register("q_liochest", fire + new Vector3(2.2f, 0f, -1.4f));
        }
    }
}
