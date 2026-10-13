using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 survival / treasure props (core step – the WORLD step may move or replace every placement here):
    ///  * hut stations: the water filter (PropWaterFilter, east wall by the door) and the grinder (PropGrinder, west wall)
    ///    – BuildHutStations is called by PropHut.BuildInterior;
    ///  * campfire spots (PropCampfire: „Lagerfeuer bauen“ with 2 driftwood, then „braten“), shared via Survival;
    ///  * world pickups (PropLoot: whetstones, driftwood, fruit – shared per day through Loot) and the player's dropped
    ///    items (local, Backpack.DropHandler);
    ///  * the map pieces (PropMapPiece lying / PropDigSpot buried, MapPieces) and the treasure the finished map points to
    ///    (PropTreasureDig at MapPieces.SecretSpot).
    /// BuildIsland runs at the end of IslandProps (after the collectibles). Every spot is deterministic.
    public static class PropSurvival
    {
        static Mesh barrelMesh, filterTopMesh, tapMesh, benchMesh, wheelMesh, crankMesh, pitMesh, logsMesh, moundMesh, crossMesh, paperMesh;
        static Mesh stoneMesh, woodMesh, mangoMesh, bananaMesh, genericMesh, spearMesh, chestMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            barrelMesh = filterTopMesh = tapMesh = benchMesh = wheelMesh = crankMesh = pitMesh = logsMesh = moundMesh = crossMesh = paperMesh = null;
            stoneMesh = woodMesh = mangoMesh = bananaMesh = genericMesh = spearMesh = chestMesh = null;
        }

        static float G(float x, float z) => PropGround.H(x, z);

        static Mesh Finish(PropMeshBuilder b, string name)
        {
            var m = b.ToMesh(name);
            LowPoly.Sanitize(m);
            b.Clear();
            return m;
        }

        static void BuildMeshes()
        {
            if (barrelMesh != null) return;
            var b = new PropMeshBuilder();
            // ---- water filter: blue barrel, white ceramic filter on top, a brass tap
            b.Cylinder(Vector3.zero, Quaternion.identity, 0.25f, 0.27f, 0.62f, 10, true, true);
            b.Cylinder(new Vector3(0f, 0.2f, 0f), Quaternion.identity, 0.275f, 0.275f, 0.04f, 10, false, false);
            b.Cylinder(new Vector3(0f, 0.45f, 0f), Quaternion.identity, 0.275f, 0.275f, 0.04f, 10, false, false);
            barrelMesh = Finish(b, "FilterBarrel");
            b.Cylinder(new Vector3(0f, 0.62f, 0f), Quaternion.identity, 0.2f, 0.18f, 0.28f, 10, true, true);
            b.Cylinder(new Vector3(0f, 0.9f, 0f), Quaternion.identity, 0.12f, 0.03f, 0.06f, 8, false, true);
            filterTopMesh = Finish(b, "FilterTop");
            b.Box(new Vector3(0f, 0.12f, 0.28f), new Vector3(0.05f, 0.05f, 0.08f), Quaternion.identity);
            b.Box(new Vector3(0f, 0.08f, 0.31f), new Vector3(0.03f, 0.06f, 0.03f), Quaternion.identity);
            b.Box(new Vector3(0f, 0.17f, 0.3f), new Vector3(0.08f, 0.02f, 0.02f), Quaternion.identity);
            tapMesh = Finish(b, "FilterTap");

            // ---- grinder: a small bench with a whetstone wheel and a crank
            b.Box(new Vector3(0f, 0.62f, 0f), new Vector3(0.9f, 0.06f, 0.45f), Quaternion.identity);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    b.Box(new Vector3(sx * 0.38f, 0.3f, sz * 0.17f), new Vector3(0.06f, 0.6f, 0.06f), Quaternion.identity);
            b.Box(new Vector3(0f, 0.72f, 0f), new Vector3(0.06f, 0.16f, 0.3f), Quaternion.identity);
            benchMesh = Finish(b, "GrinderBench");
            b.Cylinder(new Vector3(-0.045f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.21f, 0.21f, 0.09f, 12, true, true);
            wheelMesh = Finish(b, "GrinderWheel");
            b.Rod(new Vector3(0.05f, 0f, 0f), new Vector3(0.18f, 0f, 0f), 0.015f, 0.015f, 6, true);
            b.Rod(new Vector3(0.18f, 0f, 0f), new Vector3(0.18f, -0.14f, 0f), 0.015f, 0.015f, 6, true);
            b.Rod(new Vector3(0.18f, -0.14f, 0f), new Vector3(0.26f, -0.14f, 0f), 0.022f, 0.022f, 6, true);
            crankMesh = Finish(b, "GrinderCrank");

            // ---- campfire pit + logs
            var rng = new System.Random(9137);
            b.Disk(new Vector3(0f, 0.03f, 0f), 0.55f, 8, Quaternion.identity, true);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f;
                float s = PropUtil.Range(rng, 0.14f, 0.2f);
                PropRocks.Build(b, new Vector3(Mathf.Cos(a) * 0.68f, 0.05f, Mathf.Sin(a) * 0.68f), new Vector3(s, s * 0.8f, s), Quaternion.Euler(0f, a * 57f, 0f), rng.Next(), 0, 0.2f);
            }
            pitMesh = Finish(b, "CampfirePit");
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                b.Rod(d * 0.42f + Vector3.up * 0.04f, Vector3.up * 0.48f - d * 0.04f, 0.06f, 0.045f, 5, true);
            }
            logsMesh = Finish(b, "CampfireLogs");

            // ---- dig spot: a sandy mound with a cross of two sticks
            b.Ico(Vector3.zero, new Vector3(0.55f, 0.16f, 0.5f), Quaternion.identity, 1);
            moundMesh = Finish(b, "DigMound");
            b.Box(new Vector3(0f, 0.17f, 0f), new Vector3(0.7f, 0.05f, 0.08f), Quaternion.Euler(0f, 45f, 0f));
            b.Box(new Vector3(0f, 0.19f, 0f), new Vector3(0.7f, 0.05f, 0.08f), Quaternion.Euler(0f, -45f, 0f));
            crossMesh = Finish(b, "DigCross");

            // ---- a map piece (torn paper)
            b.Quad2(new Vector3(-0.1f, 0.01f, -0.08f), new Vector3(-0.09f, 0.012f, 0.08f), new Vector3(0.1f, 0.008f, 0.07f), new Vector3(0.08f, 0.014f, -0.09f), Vector3.up);
            paperMesh = Finish(b, "MapPiecePaper");

            // ---- loot
            PropRocks.Build(b, Vector3.zero, new Vector3(0.16f, 0.09f, 0.12f), Quaternion.identity, 4242, 0, 0.18f);
            stoneMesh = Finish(b, "LootStone");
            b.Rod(new Vector3(-0.42f, 0.06f, 0f), new Vector3(0.05f, 0.08f, 0.05f), 0.06f, 0.05f, 6, true);
            b.Rod(new Vector3(0.05f, 0.08f, 0.05f), new Vector3(0.4f, 0.05f, -0.06f), 0.05f, 0.035f, 6, true);
            b.Rod(new Vector3(0.05f, 0.08f, 0.05f), new Vector3(0.18f, 0.22f, 0.12f), 0.025f, 0.015f, 5, true);
            woodMesh = Finish(b, "LootDriftwood");
            b.Ico(new Vector3(0f, 0.07f, 0f), new Vector3(0.08f, 0.07f, 0.1f), Quaternion.Euler(0f, 0f, 15f), 1);
            mangoMesh = Finish(b, "LootMango");
            b.Rod(new Vector3(-0.1f, 0.03f, 0f), new Vector3(0f, 0.05f, 0.02f), 0.025f, 0.03f, 6, true);
            b.Rod(new Vector3(0f, 0.05f, 0.02f), new Vector3(0.1f, 0.06f, 0f), 0.03f, 0.022f, 6, true);
            bananaMesh = Finish(b, "LootBanana");
            b.Ico(new Vector3(0f, 0.06f, 0f), new Vector3(0.08f, 0.06f, 0.08f), Quaternion.identity, 1);
            genericMesh = Finish(b, "LootGeneric");
            b.Rod(new Vector3(-0.7f, 0.04f, 0f), new Vector3(0.6f, 0.04f, 0f), 0.02f, 0.018f, 6, true);
            b.Cylinder(new Vector3(0.6f, 0.04f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.03f, 0f, 0.14f, 6, true, false);
            spearMesh = Finish(b, "LootSpear");
            b.Box(new Vector3(0f, 0.18f, 0f), new Vector3(0.7f, 0.36f, 0.45f), Quaternion.identity);
            b.Cylinder(new Vector3(-0.35f, 0.36f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.22f, 0.22f, 0.7f, 8, true, true);
            chestMesh = Finish(b, "TreasureChest");
        }

        // ================================================================== hut stations
        /// Water filter + grinder inside the hut (positions in world space, on the hut floor). Called by PropHut.
        public static void BuildHutStations(Transform root, Vector3 filterPos, float filterYaw, Vector3 grinderPos, float grinderYaw)
        {
            BuildMeshes();
            // ---- water filter
            var f = new GameObject("WaterFilter");
            f.transform.SetParent(root, false);
            f.transform.SetPositionAndRotation(filterPos, Quaternion.Euler(0f, filterYaw, 0f));
            PropUtil.MeshObject(f.transform, "Barrel", barrelMesh, PropMats.Flat(LowPoly.Hex("#3D7BC4")), Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(f.transform, "Filter", filterTopMesh, PropMats.Flat(LowPoly.Hex("#EDE6DA")), Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(f.transform, "Tap", tapMesh, PropMats.Gold, Vector3.zero, Quaternion.identity);
            var fc = f.AddComponent<BoxCollider>();
            fc.center = new Vector3(0f, 0.48f, 0f);
            fc.size = new Vector3(0.58f, 0.96f, 0.62f);
            f.AddComponent<PropWaterFilter>();
            PropInfo.SetSpot("water_filter", filterPos, filterYaw);

            // ---- grinder (whetstone wheel)
            var g = new GameObject("Grinder");
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(grinderPos, Quaternion.Euler(0f, grinderYaw, 0f));
            PropUtil.MeshObject(g.transform, "Bench", benchMesh, PropMats.Wood2, Vector3.zero, Quaternion.identity);
            var wheel = new GameObject("WheelPivot").transform;
            wheel.SetParent(g.transform, false);
            wheel.localPosition = new Vector3(0f, 0.86f, 0f);
            PropUtil.MeshObject(wheel, "Wheel", wheelMesh, PropMats.Flat(LowPoly.Hex("#A9AEB4")), Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(wheel, "Crank", crankMesh, PropMats.DarkMetal, Vector3.zero, Quaternion.identity);
            var gc = g.AddComponent<BoxCollider>();
            gc.center = new Vector3(0f, 0.55f, 0f);
            gc.size = new Vector3(0.95f, 1.1f, 0.5f);
            g.AddComponent<PropGrinder>().wheel = wheel;
            PropInfo.SetSpot("grinder", grinderPos, grinderYaw);
        }

        // ================================================================== island: campfires, loot, map pieces
        public static IEnumerator BuildIsland(Transform island, PropExclusion excl)
        {
            BuildMeshes();
            InstallDropHandler();
            var root = new GameObject("SurvivalProps");
            root.transform.SetParent(island, false);

            // ---- campfire spots (shared fires) – round 3 world step: hut beach, driftwood beach, hidden bay, the south beach
            // by the old jetty and the volcano's black beach (+ the hermits' fire, PropCamp)
            CreateCampfire(root.transform, "camp_hut_beach", NearGround(WorldLayout.DockStart + new Vector3(-8f, 0f, -16f), 0.5f, 3.5f, excl, 101));
            CreateCampfire(root.transform, "camp_driftwood", NearGround(WorldLayout.DriftwoodBeach + new Vector3(-7f, 0f, -5f), 0.5f, 4f, excl, 102));
            CreateCampfire(root.transform, "camp_bay", NearGround(WorldLayout.HiddenBay + new Vector3(9f, 0f, 5f), 0.5f, 4f, excl, 103));
            CreateCampfire(root.transform, "camp_south_beach", NearGround(WorldLayout.Village + new Vector3(-9f, 0f, -6f), 0.8f, 3f, excl, 104));
            CreateCampfire(root.transform, "camp_volcano", NearGround(VolcanoBeachPoint(-5f, 4f), 1.0f, 4f, excl, 105));
            if (PropRun.Due()) yield return null;

            // ---- daily pickups: driftwood on the beaches, whetstones on the rocky shores, fruit inland
            for (int i = 0; i < 5; i++)
                CreateLoot(root.transform, "wood_drift_" + i, ItemIds.Wood, 1, NearGround(WorldLayout.DriftwoodBeach + Ring(i, 5, 7f), 0.3f, 3f, excl, 200 + i), i * 71f);
            for (int i = 0; i < 3; i++)
                CreateLoot(root.transform, "wood_hut_" + i, ItemIds.Wood, 1, NearGround(WorldLayout.DockStart + new Vector3(-6f, 0f, -12f) + Ring(i, 3, 5f), 0.3f, 3f, excl, 210 + i), i * 53f);
            for (int i = 0; i < 2; i++)
                CreateLoot(root.transform, "wood_bay_" + i, ItemIds.Wood, 1, NearGround(WorldLayout.HiddenBay + new Vector3(6f, 0f, 3f) + Ring(i, 2, 4f), 0.3f, 3f, excl, 220 + i), i * 97f);
            for (int i = 0; i < 4; i++)
                CreateLoot(root.transform, "stone_tide_" + i, ItemIds.Stone, 1, NearGround(WorldLayout.TidePools + Ring(i, 4, 6f), 0.3f, 6f, excl, 300 + i), i * 41f);
            for (int i = 0; i < 2; i++)
                CreateLoot(root.transform, "stone_hut_" + i, ItemIds.Stone, 1, NearGround(WorldLayout.Hut + new Vector3(8f, 0f, -9f) + Ring(i, 2, 2.5f), 0.5f, 8f, excl, 310 + i), i * 33f);
            for (int i = 0; i < 3; i++)
                CreateLoot(root.transform, "mango_" + i, "mango", 1, NearGround(WorldLayout.WaterfallPool + new Vector3(6f, 0f, -8f) + Ring(i, 3, 4f), 1f, 30f, excl, 400 + i), i * 120f);
            for (int i = 0; i < 3; i++)
                CreateLoot(root.transform, "banana_" + i, "banana", 1, NearGround(WorldLayout.Hut + new Vector3(-14f, 0f, -16f) + Ring(i, 3, 3f), 1f, 30f, excl, 410 + i), i * 120f);
            // round 3 world step: the south beach (driftwood, coconuts), the radio tower ruins (whetstones from the old
            // foundations) and the volcano's black beach (driftwood + hard lava whetstones)
            for (int i = 0; i < 3; i++)
                CreateLoot(root.transform, "wood_south_" + i, ItemIds.Wood, 1, NearGround(WorldLayout.Village + new Vector3(-4f, 0f, -14f) + Ring(i, 3, 6f), 0.3f, 2.5f, excl, 230 + i), i * 61f);
            for (int i = 0; i < 2; i++)
                CreateLoot(root.transform, "coconut_south_" + i, FoodDatabase.Coconut, 1, NearGround(WorldLayout.Village + new Vector3(10f, 0f, -6f) + Ring(i, 2, 4f), 0.6f, 3f, excl, 240 + i), i * 90f);
            for (int i = 0; i < 2; i++)
                CreateLoot(root.transform, "stone_radio_" + i, ItemIds.Stone, 1, NearGround(WorldLayout.RadioTowerRuins + new Vector3(-5f, 0f, -6f) + Ring(i, 2, 2.5f), 5f, 30f, excl, 320 + i), i * 77f);
            for (int i = 0; i < 2; i++)
                CreateLoot(root.transform, "wood_volcano_" + i, ItemIds.Wood, 1, NearGround(VolcanoBeachPoint(6f + i * 7f, 2f), 0.3f, 2.5f, excl, 250 + i), i * 45f);
            for (int i = 0; i < 3; i++)
                CreateLoot(root.transform, "stone_volcano_" + i, ItemIds.Stone, 1, NearGround(VolcanoBeachPoint(-10f - i * 4f, -2f), 0.4f, 4f, excl, 330 + i), i * 52f);
            if (PropRun.Due()) yield return null;

            // ---- map pieces (shared collectibles) + the treasure
            Collectibles.RegisterTotal(CollectibleKind.MapPiece, MapPieces.Count);
            if (!MapPieces.UseDefaultPlacement)
            {
                // round 3 world step: 4 lying (sparkle) + 4 buried (sand mound with a stick cross, a stone arrow nearby),
                // spread over the whole island, each in a place worth finding
                PlaceMapPieces(root.transform, excl);
            }
            else
            {
                Vector3[] targets =
                {
                    WorldLayout.Hut + new Vector3(12f, 0f, 9f),
                    WorldLayout.DockStart + new Vector3(-4f, 0f, -14f),
                    WorldLayout.Lighthouse + new Vector3(5f, 0f, -4f),
                    WorldLayout.WaterfallPool + new Vector3(5f, 0f, -3f),
                    WorldLayout.RadioTowerRuins + new Vector3(3f, 0f, 4f),
                    WorldLayout.HiddenBay + new Vector3(6f, 0f, -2f),
                    WorldLayout.TidePools + new Vector3(-6f, 0f, 5f),
                    WorldLayout.DriftwoodBeach + new Vector3(-5f, 0f, 3f),
                };
                bool[] buried = { false, true, false, true, false, true, true, false };
                for (int i = 0; i < MapPieces.Count; i++)
                    CreatePiece(root.transform, i, NearGround(targets[i], 0.4f, 40f, excl, 500 + i), buried[i]);
            }
            if (!MapPieces.SecretSpotValid)
            {
                // round 3: the complete map points to a hidden cove under the south-west cliffs (below the radio tower
                // ruins): a tiny beach you only reach swimming / by boat – medium difficulty, not visible from any trail.
                // (The volcano opens only with the wreck unlock, so the treasure must not wait for it.) Fallback: hidden bay.
                Vector3 s = SandNear(new Vector3(-58f, 0f, -102.5f), 0.4f, 2.5f, excl, 599, 9f, out bool ok, BuriedMinPath);
                if (!ok) s = NearGround(PropInfo.Spot("bay_cave", out var cave) ? cave + new Vector3(2.5f, 0f, 1.5f) : WorldLayout.HiddenBay + new Vector3(3f, 0f, 8f), 0.4f, 20f, excl, 599);
                MapPieces.SecretSpot = s;
                MapPieces.SecretSpotValid = true;
            }
            CreateTreasure(root.transform, MapPieces.SecretSpot);

            // round 3: the prehistoric monster's lair (fauna step) – centre of the Urzeit-Schlund floor
            Vector3 ab = WorldLayout.VolcanoAbyss;
            PropInfo.SetSpot("volcano_abyss", new Vector3(ab.x, G(ab.x, ab.z) + 2f, ab.z));
            // round 3: StoryKeys.VolcanoLanded once someone sets foot on the volcano island (hook for fauna / story)
            var landing = new GameObject("VolcanoLanding");
            landing.transform.SetParent(root.transform, false);
            landing.transform.position = WorldLayout.VolcanoIsland;
            var flag = landing.AddComponent<PropCampDiscovery>();
            flag.flag = StoryKeys.VolcanoLanded;
            flag.radius = WorldLayout.VolcanoShoreRadius - 4f;
            flag.minY = 0.2f;
        }

        /// A point on the volcano's black-sand beach: 'along' metres along the beach (north = +), 'up' metres up the
        /// beach (+ = toward the cone) from WorldFeatures.VolcanoBeach. y = ground.
        public static Vector3 VolcanoBeachPoint(float along, float up)
        {
            Vector3 b = WorldFeatures.VolcanoBeachDir;
            Vector3 lat = new Vector3(-b.z, 0f, b.x);
            if (lat.z < 0f) lat = -lat;
            Vector3 p = WorldFeatures.VolcanoBeach + lat * along - b * up;
            p.y = G(p.x, p.z);
            return p;
        }

        /// Like NearGround but only on sand / soil that can be dug (no rock platform, no wet sand), within maxR.
        static Vector3 SandNear(Vector3 target, float minG, float maxG, PropExclusion excl, int seed, float maxR, out bool ok, float minPath = 1.5f)
        {
            for (float r = 0f; r <= maxR; r += 0.7f)
            {
                int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 0.7f);
                int start = seed % Mathf.Max(1, n);
                for (int k = 0; k < n; k++)
                {
                    int i = (k + start) % n;
                    float a = i * Mathf.PI * 2f / n;
                    float x = target.x + Mathf.Cos(a) * r, z = target.z + Mathf.Sin(a) * r;
                    float g = G(x, z);
                    if (g < minG || g > maxG) continue;
                    if (excl != null && excl.Blocked(x, z)) continue;
                    if (PropPaths.DistanceToPath(x, z) < minPath) continue;
                    if (PropGround.Slope(x, z, 0.8f) > 0.45f) continue;
                    var hint = WorldTerrainData.GroundAt(x, z, g);
                    if (hint == GroundHint.Rock || hint == GroundHint.WetSand) continue;
                    ok = true;
                    return new Vector3(x, g, z);
                }
            }
            ok = false;
            return new Vector3(target.x, G(target.x, target.z), target.z);
        }

        /// Buried map pieces lie at least this far from every marked trail (player answer: secrets of medium difficulty,
        /// not visible from the main paths).
        const float BuriedMinPath = 12f;

        /// The 8 map pieces (round 3 world step). Lying: the sea floor under the swim raft (dive), the alcove
        /// behind the waterfall, the Waterfall Grotto's chamber, the hermit camp (on Kokos' crate). Buried (dig with the
        /// stick): the driftwood beach, the sand by the tide pools, the south beach by the old jetty, the north beach at
        /// the foot of the lighthouse rope ladder. Every buried one gets an arrow of pebbles pointing at it.
        static void PlaceMapPieces(Transform root, PropExclusion excl)
        {
            var spots = new Vector3[MapPieces.Count];
            var buried = new bool[MapPieces.Count];
            // 0 – lying on the sea floor under the swim raft (dive 3.7 m; a glint in the sand) – fallback: the hut roof
            if (PropInfo.Spot("swim_raft", out var raftP))
            {
                Vector3 rp0 = raftP + new Vector3(1.3f, 0f, 1.1f);
                spots[0] = new Vector3(rp0.x, G(rp0.x, rp0.z) + 0.04f, rp0.z);
            }
            else spots[0] = PropInfo.Spot("hut_roof", out var roof) ? roof + new Vector3(-2.4f, 0.08f, 0f) : NearGround(WorldLayout.Hut + new Vector3(12f, 0f, 9f), 0.4f, 40f, excl, 500);
            // 1 – buried on the driftwood beach (north-east)
            spots[1] = SandNear(WorldLayout.DriftwoodBeach + new Vector3(10f, 0f, -12f), 0.5f, 2.2f, excl, 501, 24f, out bool ok1, BuriedMinPath);
            if (!ok1) spots[1] = NearGround(WorldLayout.DriftwoodBeach + new Vector3(4f, 0f, -6f), 0.4f, 3f, excl, 501);
            buried[1] = true;
            // 2 – lying in the alcove behind the waterfall (through the squeeze)
            spots[2] = PropInfo.Spot("waterfall_alcove", out var alc) ? new Vector3(alc.x, G(alc.x, alc.z) + 0.03f, alc.z) : NearGround(WorldLayout.WaterfallPool + new Vector3(5f, 0f, -3f), 0.4f, 40f, excl, 502);
            // 3 – buried in the sand pocket south-west of the tide pools
            spots[3] = SandNear(WorldLayout.TidePools + new Vector3(-12f, 0f, -14f), 0.4f, 2f, excl, 503, 24f, out bool ok3, BuriedMinPath);
            if (!ok3) spots[3] = NearGround(WorldLayout.TidePools + new Vector3(-6f, 0f, 5f), 0.4f, 40f, excl, 503);
            buried[3] = true;
            // 4 – lying deep in the Waterfall Grotto (the chamber behind the passage)
            Vector3 ch = PropInfo.CaveChamber != Vector3.zero ? PropInfo.CaveChamber : WorldFeatures.CaveChamber;
            Vector3 chp = ch + WorldFeatures.CaveDir * 1.6f + Vector3.Cross(Vector3.up, WorldFeatures.CaveDir) * 2.2f;
            spots[4] = new Vector3(chp.x, G(chp.x, chp.z) + 0.03f, chp.z);
            // 5 – buried on the south beach where the village used to be, west of the old jetty
            spots[5] = SandNear(WorldLayout.Village + new Vector3(-18f, 0f, -14f), 0.6f, 2f, excl, 505, 24f, out bool ok5, BuriedMinPath);
            if (!ok5) spots[5] = NearGround(WorldLayout.Village + new Vector3(-12f, 0f, -14f), 0.4f, 3f, excl, 505);
            buried[5] = true;
            // 6 – lying in the hermit camp (on the crate under Kokos, the coconut friend)
            spots[6] = PropInfo.Spot("camp_map_piece", out var cmp) ? cmp : NearGround(WorldLayout.HermitCamp, 10f, 40f, excl, 506);
            // 7 – buried on the north beach at the foot of the lighthouse rope ladder
            Vector3 rl = PropInfo.Spot("rope_ladder_bottom", out var rlb) ? rlb : new Vector3(-4f, 0f, 108f);
            spots[7] = SandNear(rl + new Vector3(3f, 0f, 2f), 0.35f, 2.5f, excl, 507, 12f, out bool ok7, BuriedMinPath);
            if (!ok7) spots[7] = NearGround(rl, 0.3f, 4f, excl, 507);
            buried[7] = true;

            var rng = new System.Random(5151);
            for (int i = 0; i < MapPieces.Count; i++)
            {
                CreatePiece(root, i, spots[i], buried[i]);
                if (buried[i])
                {
                    excl?.Add(spots[i].x, spots[i].z, 1.4f);
                    PebbleArrow(root, spots[i], rng);
                }
            }
        }

        /// Clue: an arrow of 7 pebbles on the sand, 2.5–3.5 m from the dig spot, pointing at it.
        static void PebbleArrow(Transform root, Vector3 target, System.Random rng)
        {
            float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Vector3 tip = target - dir * PropUtil.Range(rng, 2.5f, 3.5f);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            var b = new PropMeshBuilder();
            Vector3[] pts =
            {
                tip, tip - dir * 0.45f, tip - dir * 0.9f, tip - dir * 1.35f,
                tip - dir * 0.35f + side * 0.35f, tip - dir * 0.35f - side * 0.35f, tip - dir * 0.7f + side * 0.6f,
            };
            for (int k = 0; k < pts.Length; k++)
            {
                Vector3 p = pts[k];
                p.y = G(p.x, p.z) + 0.02f;
                float s = PropUtil.Range(rng, 0.09f, 0.14f);
                PropRocks.Build(b, p - tip, new Vector3(s, s * 0.6f, s * 0.85f), Quaternion.Euler(0f, k * 47f, 0f), 6100 + k, 0, 0.15f);
            }
            var m = b.ToMesh("PebbleArrow");
            LowPoly.Sanitize(m);
            var go = new GameObject("MapClue");
            go.transform.SetParent(root, false);
            go.transform.position = tip;
            PropUtil.MeshObject(go.transform, "Pebbles", m, PropMats.RockMid, Vector3.zero, Quaternion.identity);
        }

        static Vector3 Ring(int i, int n, float r)
        {
            float a = (i + 0.5f) / Mathf.Max(1, n) * Mathf.PI * 2f;
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        /// Nearest point with ground in [minG, maxG] (spiral up to 14 m, off the trails, outside the exclusion), on the
        /// ground. Deterministic.
        public static Vector3 NearGround(Vector3 target, float minG, float maxG, PropExclusion excl, int seed)
        {
            for (float r = 0f; r <= 14f; r += 0.7f)
            {
                int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 0.7f);
                int start = seed % Mathf.Max(1, n);
                for (int k = 0; k < n; k++)
                {
                    int i = (k + start) % n;
                    float a = i * Mathf.PI * 2f / n;
                    float x = target.x + Mathf.Cos(a) * r, z = target.z + Mathf.Sin(a) * r;
                    float g = G(x, z);
                    if (g < minG || g > maxG) continue;
                    if (excl != null && excl.Blocked(x, z)) continue;
                    if (PropPaths.DistanceToPath(x, z) < 1.2f) continue;
                    return new Vector3(x, g, z);
                }
            }
            return new Vector3(target.x, G(target.x, target.z), target.z);
        }

        // ================================================================== factories (the world step may call these)
        public static PropCampfire CreateCampfire(Transform parent, string id, Vector3 pos)
        {
            BuildMeshes();
            var go = new GameObject("Campfire_" + id);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            PropUtil.MeshObject(go.transform, "Pit", pitMesh, PropMats.RockDark, Vector3.zero, Quaternion.identity);
            var logs = PropUtil.MeshObject(go.transform, "Logs", logsMesh, PropMats.WoodDark, Vector3.zero, Quaternion.identity).gameObject;
            var fire = PropFX.Fire(go.transform, pos + Vector3.up * 0.12f);
            var light = PropUtil.AddPointLight(go.transform, pos + Vector3.up * 0.6f, new Color(1f, 0.55f, 0.22f), 8f, 2.4f, "CampfireLight");
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.9f;
            sc.center = Vector3.up * 0.3f;
            // round 3: the stone ring + logs are solid (low – you step onto the stones instead of walking through them)
            var pit = new GameObject("PitCollider");
            pit.transform.SetParent(go.transform, false);
            var pc = pit.AddComponent<CapsuleCollider>();
            pc.direction = 1;
            pc.radius = 0.78f;
            pc.height = 1.56f;
            pc.center = new Vector3(0f, -0.56f, 0f);
            var c = go.AddComponent<PropCampfire>();
            c.id = id;
            c.logs = logs;
            c.fire = fire;
            c.fireLight = light;
            c.Apply(false);
            PropInfo.SetSpot("campfire_" + id, pos);
            return c;
        }

        public static PropLoot CreateLoot(Transform parent, string lootId, string itemId, int count, Vector3 pos, float yaw)
        {
            BuildMeshes();
            var go = new GameObject("Loot_" + lootId);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            AddLootModel(model.transform, itemId);
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.45f;
            sc.center = Vector3.up * 0.15f;
            var l = go.AddComponent<PropLoot>();
            l.lootId = lootId;
            l.itemId = itemId;
            l.count = count;
            l.model = model;
            l.trigger = sc;
            return l;
        }

        static void AddLootModel(Transform t, string itemId)
        {
            var d = ItemDatabase.Get(itemId);
            switch (itemId)
            {
                case ItemIds.Stone: PropUtil.MeshObject(t, "Stone", stoneMesh, PropMats.Flat(LowPoly.Hex("#8C96A3")), Vector3.zero, Quaternion.identity); break;
                case ItemIds.Wood: PropUtil.MeshObject(t, "Wood", woodMesh, PropMats.Flat(LowPoly.Hex("#B9A58A")), Vector3.zero, Quaternion.identity); break;
                case "mango": PropUtil.MeshObject(t, "Mango", mangoMesh, PropMats.Flat(LowPoly.Hex("#F2A93B")), Vector3.zero, Quaternion.identity); break;
                case "banana": PropUtil.MeshObject(t, "Banana", bananaMesh, PropMats.Flat(LowPoly.Hex("#F2D94C")), Vector3.zero, Quaternion.identity); break;
                case FoodDatabase.Coconut: PropUtil.MeshObject(t, "Coconut", genericMesh, PropMats.Coconut, Vector3.zero, Quaternion.identity); break;
                default:
                    if (d != null && (d.hold == HoldItem.Spear || d.hold == HoldItem.Stick || d.hold == HoldItem.SpearGun))
                        PropUtil.MeshObject(t, "Weapon", spearMesh, PropMats.Flat(LowPoly.Hex(d.hold == HoldItem.Stick ? "#9A6B43" : "#B88A5A")), Vector3.zero, Quaternion.identity);
                    else
                        PropUtil.MeshObject(t, "Thing", genericMesh, PropMats.Flat(LowPoly.Hex(d != null && d.category == ItemCategory.Raw ? "#8FB3C8" : "#E8C65A")), Vector3.zero, Quaternion.identity);
                    break;
            }
        }

        /// Map piece i: lying (E – Aufheben) or buried (E – Graben, faster with a stick).
        public static Component CreatePiece(Transform parent, int index, Vector3 pos, bool buried)
        {
            BuildMeshes();
            if (index < 0 || index >= MapPieces.Count) return null;
            MapPieces.Spots[index] = new MapPieceSpot { position = pos, buried = buried, valid = true };
            var go = new GameObject("MapPiece_" + (index + 1));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, index * 47f, 0f));
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = buried ? 0.75f : 0.45f;
            sc.center = Vector3.up * 0.15f;
            if (buried)
            {
                PropUtil.MeshObject(model.transform, "Mound", moundMesh, PropMats.Flat(LowPoly.Hex("#D9C08A")), Vector3.zero, Quaternion.identity);
                PropUtil.MeshObject(model.transform, "Cross", crossMesh, PropMats.Flat(LowPoly.Hex("#6E4A2E")), Vector3.zero, Quaternion.identity);
                var d = go.AddComponent<PropDigSpot>();
                d.index = index;
                d.model = model;
                d.trigger = sc;
                return d;
            }
            PropUtil.MeshObject(model.transform, "Paper", paperMesh, PropMats.Paper, Vector3.zero, Quaternion.identity);
            var p = go.AddComponent<PropMapPiece>();
            p.index = index;
            p.model = model;
            p.trigger = sc;
            p.sparkle = PropFX.Sparkle(go.transform, pos + Vector3.up * 0.25f, 0.22f);
            return p;
        }

        static void CreateTreasure(Transform parent, Vector3 pos)
        {
            var go = new GameObject("TreasureDig");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            PropUtil.MeshObject(model.transform, "Mound", moundMesh, PropMats.Flat(LowPoly.Hex("#CDB27A")), Vector3.zero, Quaternion.identity);
            var chest = PropUtil.MeshObject(go.transform, "Chest", chestMesh, PropMats.Wood, new Vector3(0f, 0.02f, 0f), Quaternion.Euler(0f, 25f, 0f)).gameObject;
            // round 3: the dug-up chest is solid
            var cb = chest.AddComponent<BoxCollider>();
            cb.center = new Vector3(0f, 0.27f, 0f);
            cb.size = new Vector3(0.72f, 0.56f, 0.47f);
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.9f;
            sc.center = Vector3.up * 0.2f;
            var t = go.AddComponent<PropTreasureDig>();
            t.mound = model;
            t.chest = chest;
            PropInfo.SetSpot("treasure_map_x", pos);
        }

        // ================================================================== dropped items (local)
        public static void InstallDropHandler() { Backpack.DropHandler = SpawnDropped; }

        /// A stack the local player dropped: lies on the ground in front of them for 5 minutes (only on this machine).
        public static void SpawnDropped(InvStack st, Vector3 pos)
        {
            if (st == null || st.count <= 0) return;
            BuildMeshes();
            var parent = Refs.WorldRoot;
            var go = new GameObject("Dropped_" + st.id);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            AddLootModel(model.transform, st.id);
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.45f;
            sc.center = Vector3.up * 0.15f;
            var l = go.AddComponent<PropLoot>();
            l.itemId = st.id;
            l.count = st.count;
            l.dropped = new InvStack { id = st.id, count = st.count, durability = st.durability, sharpness = st.sharpness };
            l.model = model;
            l.trigger = sc;
            l.BeginFall(pos);
        }
    }

    // ====================================================================== components

    /// The hut's water filter: „E – Meerwasser filtern“ puts every seawater bottle in (45 in-game minutes), „E –
    /// Trinkwasser nehmen“ takes the ready bottles out (Survival, per player).
    public class PropWaterFilter : PropHold
    {
        int keyN = -1, keySec = -1;
        Language keyLang;
        string verbDe = "Meerwasser filtern", verbEn = "Filter seawater", waitText;

        void Awake() { seconds = 1.2f; range = 3f; }

        void RefreshVerb()
        {
            int ready = Survival.FilterReadyCount, sea = Backpack.Count(ItemIds.BottleSea);
            int n = ready > 0 ? 1000 + ready : sea;
            if (n == keyN && keyLang == Loc.Current) return;
            keyN = n;
            keyLang = Loc.Current;
            if (ready > 0) { verbDe = "Trinkwasser nehmen (" + ready + ")"; verbEn = "Take fresh water (" + ready + ")"; }
            else { verbDe = "Meerwasser filtern (" + sea + ")"; verbEn = "Filter seawater (" + sea + ")"; }
        }

        protected override string VerbDe { get { RefreshVerb(); return verbDe; } }
        protected override string VerbEn { get { RefreshVerb(); return verbEn; } }

        protected override bool Available() => Survival.FilterReadyCount > 0 || Backpack.Has(ItemIds.BottleSea);

        protected override string UnavailableHint
        {
            get
            {
                if (Survival.FilterCount > 0)
                {
                    // „Filtert … 0:42“ (cached per second)
                    int sec = Mathf.CeilToInt(Survival.FilterSecondsLeft);
                    if (sec != keySec || keyLang != Loc.Current || waitText == null)
                    {
                        keySec = sec;
                        keyLang = Loc.Current;
                        waitText = Loc.Tr("Wasser filtert … ", "Water filtering… ") + UIKit.Clock(sec);
                    }
                    return waitText;
                }
                return PropPrompt.Text("Wasserfilter: Meerwasser in Flaschen bringen", "Water filter: bring seawater in bottles");
            }
        }

        protected override void Complete()
        {
            int taken = Survival.FilterTake();
            int put = Survival.FilterPut();
            if (taken == 0 && put == 0) GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Nichts zu filtern", "Nothing to filter"));
            keyN = -1;
        }
    }

    /// The grinder: „E – Speer schärfen“ (+25 % sharpness, 1 whetstone) once the hermits taught the technique.
    public class PropGrinder : PropHold
    {
        public Transform wheel;
        float spin;

        protected override string VerbDe => "Speer schärfen";
        protected override string VerbEn => "Sharpen spear";

        void Awake() { seconds = 3f; range = 3f; }

        protected override bool Available() => Combat.SharpenBlocker() == null;

        protected override string UnavailableHint => Combat.SharpenBlocker();

        protected override void Complete()
        {
            Combat.Sharpen();
        }

        protected override void Update()
        {
            base.Update();
            if (wheel == null) return;
            spin = Mathf.MoveTowards(spin, Running ? 720f : 0f, Time.deltaTime * 900f);
            if (spin > 1f) wheel.Rotate(spin * Time.deltaTime, 0f, 0f, Space.Self);
        }
    }

    /// A fixed campfire spot (shared fire, Survival): „E – Lagerfeuer bauen“ (2 driftwood) → burns 3 in-game hours →
    /// „E – Roher Fisch braten“ cooks one raw item of the backpack per 4 s.
    public class PropCampfire : PropHold
    {
        public string id;
        public GameObject logs;
        public ParticleSystem fire;
        public Light fireLight;
        bool lit, applied;
        float poll;
        string rawKey;
        Language rawLang;
        string verbDe = "Braten", verbEn = "Cook";

        void Awake() { seconds = 2f; range = 3.4f; }

        // final test: the hermits sat around a cold fire pit in the evening – they keep their own fire burning
        // 18:30–23:30 and at dawn (shared clock → the same on every co-op machine); anyone may cook there then
        bool Lit => Survival.CampfireLit(id) || HermitsTend;

        bool HermitsTend
        {
            get
            {
                if (id != "camp_hermits") return false;
                var gm = GameManager.I;
                if (gm == null || gm.State == null) return false;
                float h = gm.State.timeOfDay * 24f;
                return (h >= 18.5f && h < 23.5f) || (h >= 5.5f && h < 7.5f);
            }
        }

        void RefreshVerb()
        {
            if (!Lit) { verbDe = "Lagerfeuer bauen (2 Holz)"; verbEn = "Build a campfire (2 wood)"; rawKey = null; return; }
            string raw = Survival.FirstRaw();
            if (raw == rawKey && rawLang == Loc.Current) return;
            rawKey = raw;
            rawLang = Loc.Current;
            var d = ItemDatabase.Get(raw);
            if (d == null) { verbDe = "Braten"; verbEn = "Cook"; return; }
            verbDe = d.nameDe + " braten";
            verbEn = "Cook " + d.nameEn;
        }

        protected override string VerbDe { get { RefreshVerb(); return verbDe; } }
        protected override string VerbEn { get { RefreshVerb(); return verbEn; } }

        protected override bool Available() => Lit ? Survival.RawCount > 0 : Backpack.Has(ItemIds.Wood, Survival.CampfireWood);

        protected override string UnavailableHint => Lit
            ? PropPrompt.Text("Feuer brennt – roher Fisch zum Braten?", "Fire's burning – anything raw to cook?")
            : PropPrompt.Text("Feuerstelle – 2 Treibholz nötig", "Fire pit – needs 2 driftwood");

        protected override void OnStart() { seconds = Lit ? Survival.CookSeconds : 2f; }

        protected override void Complete()
        {
            if (!Lit)
            {
                if (Survival.TryLightCampfire(id)) Apply(true);
                return;
            }
            string raw = Survival.FirstRaw();
            var d = ItemDatabase.Get(raw);
            if (Survival.CookOne()) GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Gebraten!", "Cooked!"), d != null && ItemDatabase.Get(d.cookTo) != null ? ItemDatabase.Get(d.cookTo).Name : null);
        }

        protected override void Update()
        {
            base.Update();
            poll -= Time.deltaTime;
            if (poll > 0f && applied) return;
            poll = 0.5f;
            Apply(Lit);
        }

        public void Apply(bool on)
        {
            if (applied && on == lit) return;
            applied = true;
            lit = on;
            if (logs != null && logs.activeSelf != on) logs.SetActive(on);
            if (fireLight != null) fireLight.enabled = on;
            if (fire != null)
            {
                if (on && !fire.isPlaying) fire.Play(true);
                else if (!on && fire.isPlaying) fire.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    /// A world pickup (shared per day through Loot – lootId) or an item the local player dropped (dropped != null).
    public class PropLoot : MonoBehaviour, IInteractable
    {
        public string lootId, itemId;
        public int count = 1;
        public InvStack dropped;
        public GameObject model;
        public Collider trigger;
        float poll, life, fallT = 1f;
        Vector3 fallFrom, fallTo;
        bool shown = true;
        string promptKey;
        Language promptLang;
        string promptDe, promptEn;

        public void BeginFall(Vector3 from)
        {
            fallFrom = from;
            float g = PropGround.H(from.x, from.z);
            fallTo = new Vector3(from.x, g + 0.02f, from.z);
            fallT = 0f;
        }

        bool Available => GameManager.I != null && GameManager.I.InGame && (dropped != null || Loot.Available(lootId));

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            if (promptKey != itemId || promptLang != Loc.Current)
            {
                promptKey = itemId;
                promptLang = Loc.Current;
                var d = ItemDatabase.Get(itemId);
                promptDe = (d != null ? d.nameDe : itemId) + " aufheben";
                promptEn = "Pick up " + (d != null ? d.nameEn : itemId);
            }
            return PropPrompt.Interact(promptDe, promptEn);
        }

        public bool CanInteract() => Available && fallT >= 1f;

        public void Interact()
        {
            if (!CanInteract()) return;
            if (dropped != null)
            {
                if (Backpack.AddStack(dropped)) Destroy(gameObject);
                return;
            }
            if (Loot.TryTake(lootId, itemId, count)) Refresh();
        }

        void Refresh()
        {
            bool on = Available;
            if (on == shown) return;
            shown = on;
            if (model != null) model.SetActive(on);
            if (trigger != null) trigger.enabled = on;
        }

        void Update()
        {
            if (fallT < 1f)
            {
                fallT = Mathf.Min(1f, fallT + Time.deltaTime / 0.45f);
                Vector3 p = Vector3.Lerp(fallFrom, fallTo, fallT * fallT);
                transform.position = p;
            }
            if (dropped != null)
            {
                life += Time.deltaTime;
                if (life > 300f || GameManager.I == null || !GameManager.I.InGame) Destroy(gameObject);
                return;
            }
            poll -= Time.unscaledDeltaTime;
            if (poll > 0f) return;
            poll = 0.5f;
            Refresh();
        }
    }

    /// A buried map piece under a marked mound: „E – Graben“ (2.5 s with a stick or spear in hand, 5 s with bare hands).
    public class PropDigSpot : PropHold
    {
        public int index;
        public GameObject model;
        public Collider trigger;
        float poll;
        bool shown = true;

        protected override string VerbDe => "Graben";
        protected override string VerbEn => "Dig";

        void Awake() { seconds = 5f; range = 3f; }

        protected override bool Available() => GameManager.I != null && GameManager.I.InGame && !MapPieces.IsFound(index);

        protected override void OnStart()
        {
            bool tool = Hands.IsHeld(HoldItem.Stick) || Hands.IsHeld(HoldItem.Spear);
            seconds = tool ? 2.5f : 5f;
        }

        protected override void Complete()
        {
            if (MapPieces.Collect(index))
                GameEvents.RaiseNotify(NotifyKind.Collectible, Loc.Tr("Kartenstück ausgegraben!", "Map piece dug up!"), MapPieces.Found + "/" + MapPieces.Count);
        }

        protected override void Update()
        {
            base.Update();
            poll -= Time.unscaledDeltaTime;
            if (poll > 0f) return;
            poll = 0.5f;
            bool on = Available();
            if (on == shown) return;
            shown = on;
            if (model != null) model.SetActive(on);
            if (trigger != null) trigger.enabled = on;
        }
    }

    /// A map piece lying around (paper, a little sparkle): „E – Aufheben“.
    public class PropMapPiece : MonoBehaviour, IInteractable
    {
        public int index;
        public GameObject model;
        public Collider trigger;
        public ParticleSystem sparkle;
        float poll;
        bool shown = true;

        bool Available => GameManager.I != null && GameManager.I.InGame && !MapPieces.IsFound(index);

        public string GetPrompt() => CanInteract() ? PropPrompt.Interact("Kartenstück aufheben", "Pick up the map piece") : null;
        public bool CanInteract() => Available;

        public void Interact()
        {
            if (!CanInteract()) return;
            MapPieces.Collect(index);
            Refresh();
        }

        void Refresh()
        {
            bool on = Available;
            if (on == shown) return;
            shown = on;
            if (model != null) model.SetActive(on);
            if (trigger != null) trigger.enabled = on;
            if (sparkle != null)
            {
                if (on) sparkle.Play(true);
                else sparkle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        void Update()
        {
            poll -= Time.unscaledDeltaTime;
            if (poll > 0f) return;
            poll = 0.5f;
            Refresh();
        }
    }

    /// The X of the treasure map (placeholder secret – the world step may put its own secret at MapPieces.SecretSpot):
    /// only with the complete map: „E – Schatz ausgraben“ → StoryKeys.TreasureFound, 500 $ for the channel.
    public class PropTreasureDig : PropHold
    {
        public const long Reward = 500;
        public GameObject mound, chest;
        float poll;

        protected override string VerbDe => "Schatz ausgraben";
        protected override string VerbEn => "Dig up the treasure";

        void Awake() { seconds = 4f; range = 3.2f; }

        protected override bool Available() => GameManager.I != null && GameManager.I.InGame && MapPieces.Complete && !StoryFlags.Has(StoryKeys.TreasureFound);

        protected override void OnStart()
        {
            bool tool = Hands.IsHeld(HoldItem.Stick) || Hands.IsHeld(HoldItem.Spear);
            seconds = tool ? 3f : 6f;
        }

        protected override void Complete()
        {
            if (StoryFlags.Has(StoryKeys.TreasureFound)) return;
            StoryFlags.Set(StoryKeys.TreasureFound);
            GameManager.I.AddMoney(Reward);
            GameEvents.RaiseBanner(Loc.Tr("Der Schatz der Insel!", "The island's treasure!"), new Color(0.96f, 0.82f, 0.4f));
            GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("Schatz", "Treasure"), "+" + Loc.Money(Reward));
            UIWaypoint.Clear();
        }

        protected override void Update()
        {
            base.Update();
            poll -= Time.unscaledDeltaTime;
            if (poll > 0f) return;
            poll = 0.5f;
            bool found = GameManager.I != null && StoryFlags.Has(StoryKeys.TreasureFound);
            bool complete = GameManager.I != null && MapPieces.Complete;
            if (mound != null && mound.activeSelf != (complete && !found)) mound.SetActive(complete && !found);
            if (chest != null && chest.activeSelf != found) chest.SetActive(found);
        }
    }
}
