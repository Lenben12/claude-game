using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Places the 94 optional collectibles (H.2 + L.6): 24 pages of Onkel Gus' logbook, 40 golden shells, 14 treasures,
    /// 13 secrets and 3 messages in a bottle (day 3 / 6 / 10, beach spots chosen by GameState.saveSeed).
    /// L.6 rules: secrets have no sparkle (glint within 2.5 m) and sit behind a climb, a dive, darkness, a squeeze or a
    /// two-player action; pages and treasures stay ≥ 3 m off every trail centre line, more than half of them ≥ 10 m off
    /// or behind a climb / dive; golden shells may stay near the beaches (the easy set).
    /// Every spot comes from the built geometry (PropInfo spots) or fixed seeds – identical on every co-op machine.
    /// Island part: Build (end of IslandProps). Under-water part: BuildUnderwater (end of UnderwaterDecor).
    public static class PropCollect
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        public const int Pages = 24, Shells = 40, TreasureTotal = 14, SecretTotal = 13, Bottles = 3;

        static Mesh pageMesh, ribbonMesh, shellMesh, coinsMesh, cupMesh, eggMesh, helmetMesh, helmetGlassMesh, compassMesh, compassFaceMesh, reelMesh;
        static Mesh bottleMesh, corkMesh, scrollMesh, tinsMesh, noteMesh, heartMesh, hatMesh, hatTrimMesh, piggyMesh, micMesh, micHeadMesh, cheeseMesh, barsMesh, cameraMesh, tapesMesh;
        static PropCollectibleManager manager;
        static PropExclusion islandExcl;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pageMesh = null; ribbonMesh = null; shellMesh = null; coinsMesh = null; cupMesh = null; eggMesh = null; helmetMesh = null; helmetGlassMesh = null;
            compassMesh = null; compassFaceMesh = null; reelMesh = null; bottleMesh = null; corkMesh = null; scrollMesh = null; tinsMesh = null; noteMesh = null; heartMesh = null;
            hatMesh = null; hatTrimMesh = null; piggyMesh = null; micMesh = null; micHeadMesh = null; cheeseMesh = null; barsMesh = null; cameraMesh = null; tapesMesh = null;
            manager = null; islandExcl = null;
        }

        static Vector3 Spot(string key) => PropInfo.Spot(key, out var p) ? p : Vector3.zero;
        static Vector3 Off(Vector3 basePos, Vector3 off) => basePos == Vector3.zero ? Vector3.zero : basePos + off;
        static Vector3 Mini(string id) => PropInfo.MiniLandmarks.TryGetValue(id, out var p) ? p : Vector3.zero;

        // ================================================================== island
        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            BuildMeshes();
            islandExcl = excl;
            var root = new GameObject("Collectibles");
            root.transform.SetParent(island, false);
            var mgr = root.AddComponent<PropCollectibleManager>();
            manager = mgr;
            // totals of the whole set (the under-water part is added by BuildUnderwater)
            Collectibles.RegisterTotal(CollectibleKind.LogbookPage, Pages);
            Collectibles.RegisterTotal(CollectibleKind.GoldenShell, Shells);
            Collectibles.RegisterTotal(CollectibleKind.Treasure, TreasureTotal);
            Collectibles.RegisterTotal(CollectibleKind.Secret, SecretTotal);
            Collectibles.RegisterTotal(CollectibleKind.BottleMessage, Bottles);

            // ---------------------------------------------------------------- logbook pages (island: 22 of 24)
            Vector3 deskPage = Off(Spot("gus_letter"), new Vector3(0.25f, 0.005f, -0.12f));
            Vector3 galleryPage = PropInfo.LighthouseGallery != Vector3.zero ? GalleryPoint(0.6f) : Vector3.zero;
            var pages = new Vector3[Pages];
            pages[0] = deskPage;
            pages[1] = OffTrail(Off(PropInfo.Boathouse, new Vector3(1.1f, 0.03f, 1.3f)), 3.2f);
            pages[2] = galleryPage;
            pages[3] = Off(Spot("cellar"), new Vector3(0.9f, 0.03f, 0.7f));
            pages[4] = OffTrail(WaterfallPage(), 4f);
            pages[5] = PropInfo.CaveChamber;
            pages[6] = Off(Spot("bay_cave"), new Vector3(-0.7f, 0f, -0.9f));
            pages[7] = Spot("ravine_floor_page");
            pages[8] = Off(Spot("radio_platform"), new Vector3(-0.6f, -0.4f, -0.3f));
            pages[9] = Spot("tree_house_floor");
            pages[10] = Spot("base_camp_page");
            pages[11] = JungleHeart();
            pages[12] = OffTrail(Off(Spot("tide_bench"), new Vector3(0.35f, 0f, 0f)), 3.2f);
            pages[13] = Spot("tide_calendar_page");
            pages[14] = OffTrail(Spot("driftwood_board_page"), 3.2f);
            pages[15] = Off(Spot("swim_raft_deck"), new Vector3(0.4f, 0.02f, 0.5f));
            pages[16] = Spot("page_mini_wreck");
            pages[17] = Off(Spot("ravine_piggy"), new Vector3(0.6f, 0f, 0.35f));
            // 18 (captain's cabin) and 19 (galley) are under water
            pages[20] = Ground(Off(Spot("kelp_carving"), new Vector3(0f, 0f, 0f)), 0.2f);
            pages[21] = Spot("page_mini_blue_drop");
            pages[22] = Spot("page_mini_deep_trench");
            pages[23] = Off(Spot("rope_ladder_bottom"), new Vector3(1.2f, 0.03f, 0.8f));
            for (int i = 0; i < Pages; i++)
            {
                if (i == 18 || i == 19) continue;
                Vector3 p = pages[i];
                if (p == Vector3.zero) p = Fallback(i * 37 + 5, excl);
                Page(root.transform, mgr, i, p);
            }
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- golden shells (island: 28 of 40)
            var used = new PropExclusion(16f);
            int shells = 0;
            // 12 on the beaches
            for (int k = 0; k < 60 && shells < 12; k++)
            {
                float a = (k * 137.508f + 11f) * Mathf.Deg2Rad;
                if (!FindRing(a, 0.35f, 1.5f, excl, used, out var p)) continue;
                Shell(root.transform, mgr, shells++, p);
                used.Add(p.x, p.z, 14f);
            }
            // 6 in the lagoon shallows
            int water = 0;
            for (int k = 0; k < 80 && water < 6; k++)
            {
                float a = (k * 97.3f + 40f) * Mathf.Deg2Rad;
                if (!FindRing(a, -2.4f, -0.7f, null, used, out var p)) continue;
                Shell(root.transform, mgr, shells++, p);
                used.Add(p.x, p.z, 14f);
                water++;
            }
            // 8 on the landmark islands (2 each)
            var perIsland = new Dictionary<string, int>();
            foreach (var m in PropInfo.MiniShellSpots)
            {
                if (shells >= 20 + 8) break;
                string id = IslandOf(m);
                if (id == null || id == "fog_isle") continue;
                perIsland.TryGetValue(id, out int n);
                if (n >= 2) continue;
                perIsland[id] = n + 1;
                Shell(root.transform, mgr, shells++, m);
            }
            // 2 in the tide pools
            var tps = WorldFeatures.TidePoolSpots;
            for (int i = 0; tps != null && i < tps.Length && shells < 30; i += 4)
            {
                Vector3 p = tps[i].position;
                p.y = G(p) + 0.02f;
                Shell(root.transform, mgr, shells++, p);
                if (i >= 4) break;
            }
            while (shells < 28) Shell(root.transform, mgr, shells++, Fallback(700 + shells * 13, excl));
            nextShell = shells;
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- treasures (island: 8 of 14)
            Treasure(root.transform, mgr, "bay_chest", PropInfo.TreasureChest != Vector3.zero ? PropInfo.TreasureChest + Vector3.up * 0.7f : Fallback(901, excl), coinsMesh, cupMesh, PropMats.Sparkle, PropMats.Sparkle);
            Vector3 nest = Mini("blue_drop");
            Treasure(root.transform, mgr, "bird_nest", nest != Vector3.zero ? nest : Fallback(902, excl), eggMesh, null, PropMats.Sparkle, null, new Vector3(0f, 0.1f, 0f), Quaternion.Euler(0f, 0f, 12f));
            Vector3 helmet = Spot("helmet_spot");
            if (helmet == Vector3.zero) helmet = PropInfo.HelmetSpot;
            Treasure(root.transform, mgr, "diving_helmet", helmet != Vector3.zero ? helmet : Fallback(903, excl), helmetMesh, helmetGlassMesh, PropMats.Flat(LowPoly.Hex("#B8913A")), PropMats.Flat(PropPal.HullBlue), Vector3.up * 0.1f, Quaternion.Euler(-14f, 30f, 8f));
            Treasure(root.transform, mgr, "bridge_coins", BridgeCoins(excl), coinsMesh, null, PropMats.Sparkle, null, Vector3.zero, Quaternion.Euler(0f, 70f, 0f));
            Vector3 wl = Mini("wreck");
            Treasure(root.transform, mgr, "wreck_compass", wl != Vector3.zero ? wl : Fallback(905, excl), compassMesh, compassFaceMesh, PropMats.Flat(LowPoly.Hex("#B8913A")), PropMats.Paper, Vector3.up * 0.02f, Quaternion.Euler(0f, 20f, 0f));
            Vector3 reel = PropInfo.RadioTowerBase != Vector3.zero ? OffTrail(PropInfo.RadioTowerBase + new Vector3(2.2f, 0.02f, 1.6f), 3.2f) : Fallback(906, excl);
            Treasure(root.transform, mgr, "film_reel", reel, reelMesh, null, PropMats.Flat(PropPal.HullBlue), null, Vector3.up * 0.03f, Quaternion.identity);
            Treasure(root.transform, mgr, "bay_cave", Spot("bay_cave") != Vector3.zero ? Spot("bay_cave") + new Vector3(0.8f, 0f, -0.4f) : Fallback(907, excl), coinsMesh, cupMesh, PropMats.Sparkle, PropMats.Sparkle);
            Vector3 mic = Off(Spot("cellar_desk"), new Vector3(-0.45f, 0.01f, 0.2f));
            Treasure(root.transform, mgr, "cellar_mic", mic != Vector3.zero ? mic : Fallback(908, excl), micMesh, micHeadMesh, PropMats.Gold, PropMats.Flat(LowPoly.Hex("#5E5A48")), Vector3.zero, Quaternion.Euler(0f, 0f, 80f));
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- secrets (island: 10 of 13; no sparkle)
            Vector3 cam = PropInfo.GrottoCamera;
            Secret(root.transform, mgr, "cave_grotto", cam != Vector3.zero ? cam + Vector3.up * 1.35f : Fallback(950, excl), null, null, 0.6f);
            Vector3 niche = PropInfo.WaterfallNiche;
            var wf = Secret(root.transform, mgr, "waterfall", niche != Vector3.zero ? niche : Fallback(951, excl), tinsMesh, PropMats.LighthouseRed, 0.6f);
            Part(wf, noteMesh, PropMats.Paper, Vector3.zero, Quaternion.identity);
            Vector3 carving = Spot("lighthouse_carving");
            var lh = Secret(root.transform, mgr, "lighthouse_top", carving != Vector3.zero ? carving : Fallback(952, excl), heartMesh, PropMats.Flat(PropPal.Coral), 0.6f);
            if (carving != Vector3.zero && lh != null && lh.model != null) lh.model.transform.rotation = Quaternion.Euler(0f, PropInfo.SpotYaw("lighthouse_carving"), 0f);
            Vector3 radio = Spot("radio_platform");
            Secret(root.transform, mgr, "radio_tower", radio != Vector3.zero ? radio : (PropInfo.RadioShack != Vector3.zero ? PropInfo.RadioShack : Fallback(953, excl)), null, null, 0.6f);
            var piggy = Secret(root.transform, mgr, "ravine_piggy", Spot("ravine_piggy") != Vector3.zero ? Spot("ravine_piggy") : Fallback(954, excl), piggyMesh, PropMats.Flat(LowPoly.Hex("#F29BBE")), 0.55f);
            if (piggy != null) piggy.money = 150;
            Secret(root.transform, mgr, "kelp_carving", Spot("kelp_carving") != Vector3.zero ? Spot("kelp_carving") : Fallback(955, excl), null, null, 0.6f);
            Secret(root.transform, mgr, "wreck_hammock", Spot("wreck_hammock") != Vector3.zero ? Spot("wreck_hammock") : Fallback(956, excl), null, null, 0.8f);
            Secret(root.transform, mgr, "trench_tv", Spot("trench_tv") != Vector3.zero ? Spot("trench_tv") : Fallback(957, excl), null, null, 0.7f);
            Secret(root.transform, mgr, "lighthouse_cellar", Spot("cellar_desk") != Vector3.zero ? Spot("cellar_desk") + new Vector3(0f, 0.2f, 0f) : Fallback(958, excl), null, null, 0.8f);
            var loft = Secret(root.transform, mgr, "village_loft", Spot("village_loft") != Vector3.zero ? Spot("village_loft") : Fallback(959, excl), tinsMesh, PropMats.Flat(LowPoly.Hex("#8E5A3A")), 0.5f);
            if (loft != null) Part(loft, noteMesh, PropMats.Paper, Vector3.zero, Quaternion.identity);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- messages in a bottle (day 3 / 6 / 10, by save seed)
            for (int k = 0; k < 24; k++)
            {
                float a = k * 15f * Mathf.Deg2Rad;
                if (FindRing(a, 0.25f, 1.0f, excl, null, out var p)) mgr.bottleSpots.Add(p);
            }
            if (mgr.bottleSpots.Count == 0) mgr.bottleSpots.Add(Fallback(990, excl));
            for (int i = 0; i < Bottles; i++)
            {
                var pk = Pickup(root.transform, mgr, PropLore.BottleId(i), CollectibleKind.BottleMessage, mgr.bottleSpots[(i * 7) % mgr.bottleSpots.Count], 0f);
                pk.requireDay = PropLore.BottleDays[i];
                Part(pk, bottleMesh, PropMats.BottleGlass, Vector3.up * 0.07f, Quaternion.Euler(0f, 0f, 84f));
                Part(pk, corkMesh, PropMats.WoodLight, Vector3.up * 0.07f, Quaternion.Euler(0f, 0f, 84f));
                Part(pk, scrollMesh, PropMats.Paper, Vector3.up * 0.07f, Quaternion.Euler(0f, 0f, 84f));
                mgr.bottles.Add(pk);
            }
            mgr.RefreshAll();
        }

        static int nextShell;

        // ================================================================== under water
        public static IEnumerator BuildUnderwater(Transform uw)
        {
            BuildMeshes();
            var mgr = manager;
            Transform rootT;
            if (mgr == null)
            {
                var go = new GameObject("CollectiblesUnderwater");
                go.transform.SetParent(uw, false);
                mgr = go.AddComponent<PropCollectibleManager>();
                manager = mgr;
                rootT = go.transform;
            }
            else
            {
                var go = new GameObject("CollectiblesUnderwater");
                go.transform.SetParent(uw, false);
                rootT = go.transform;
            }
            var excl = islandExcl ?? new PropExclusion(16f);
            // ---- pages 19 (captain's cabin) and 20 (galley – the key clue)
            Page(rootT, mgr, 18, Spot("page_cabin") != Vector3.zero ? Spot("page_cabin") : Fallback(18 * 37 + 5, excl));
            Page(rootT, mgr, 19, Spot("page_galley") != Vector3.zero ? Spot("page_galley") : Fallback(19 * 37 + 5, excl));
            // ---- 12 shells at the sites (house reef 4, kelp 2, wreck 2, blue drop 2, trench 2)
            string[] sites = { "house_reef", "house_reef", "house_reef", "house_reef", "kelp_forest", "kelp_forest", "wreck", "wreck", "blue_drop", "blue_drop", "deep_trench", "deep_trench" };
            int shells = Mathf.Max(nextShell, 28);
            for (int i = 0; i < sites.Length; i++)
            {
                var site = DiveSites.Get(sites[i]);
                if (site == null) continue;
                float a = (i * 97f + 13f) * Mathf.Deg2Rad;
                Vector3 c = site.center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * site.radius * (0.3f + 0.12f * (i % 3));
                float min = Mathf.Max(2f, site.minDepth + 1f), max = Mathf.Min(site.maxDepth, site.minDepth + 16f);
                if (!PropReefs.FindSpot(c, site.radius * 0.5f, min, max, null, 500 + i, out Vector3 p)) continue;
                Shell(rootT, mgr, shells++, p + Vector3.up * 0.03f);
            }
            if (PropRun.Due()) yield return null;
            // ---- treasures under water (6 of 14)
            Treasure(rootT, mgr, "hold_gold", Spot("hold_gold"), barsMesh, null, PropMats.Gold, null);
            Treasure(rootT, mgr, "hold_cheese", Spot("hold_cheese"), cheeseMesh, null, PropMats.Flat(PropPal.Sun), null, Vector3.up * 0.05f, Quaternion.identity);
            Treasure(rootT, mgr, "hold_safe", Spot("hold_safe"), coinsMesh, cupMesh, PropMats.Sparkle, PropMats.Sparkle);
            var chest = Treasure(rootT, mgr, "kelp_chest", Spot("kelp_chest"), coinsMesh, cupMesh, PropMats.Sparkle, PropMats.Sparkle);
            if (chest != null) chest.requireFlag = "coop_kelp_chest";
            Treasure(rootT, mgr, "drop_ledge", Spot("drop_ledge"), compassMesh, compassFaceMesh, PropMats.Flat(LowPoly.Hex("#B8913A")), PropMats.Paper, Vector3.up * 0.03f, Quaternion.identity);
            Treasure(rootT, mgr, "trench_camera", Spot("trench_camera"), cameraMesh, null, PropMats.Flat(LowPoly.Hex("#3B3A4E")), null, Vector3.up * 0.05f, Quaternion.Euler(0f, 30f, 0f));
            // ---- secrets under water (3 of 13)
            var cabin = Secret(rootT, mgr, "wreck_cabin", Spot("wreck_cabin_hat"), hatMesh, PropMats.Ink, 0.6f);
            if (cabin != null)
            {
                Part(cabin, hatTrimMesh, PropMats.Gold, Vector3.zero, Quaternion.identity);
                cabin.requireFlag = PropKeyDoor.OpenFlag;
            }
            Secret(rootT, mgr, "blue_buoy", Spot("blue_buoy"), null, null, 0.8f);
            Secret(rootT, mgr, "fog_archive", Spot("fog_archive"), tapesMesh, PropMats.Ink, 0.7f);
            mgr.RefreshAll();
        }

        // ------------------------------------------------------------------ item helpers
        static void Page(Transform root, PropCollectibleManager mgr, int i, Vector3 p)
        {
            var pk = Pickup(root, mgr, PropLore.PageId(i), CollectibleKind.LogbookPage, p, i * 47f);
            pk.pageIndex = i;
            Part(pk, pageMesh, PropMats.Paper, Vector3.up * 0.01f, Quaternion.Euler(0f, i * 47f, 0f));
            Part(pk, ribbonMesh, PropMats.LighthouseRed, Vector3.up * 0.01f, Quaternion.Euler(0f, i * 47f, 0f));
        }

        static void Shell(Transform root, PropCollectibleManager mgr, int i, Vector3 p)
        {
            var pk = Pickup(root, mgr, Collectibles.Id(CollectibleKind.GoldenShell, "s" + (i + 1).ToString("00")), CollectibleKind.GoldenShell, p, i * 61f);
            Part(pk, shellMesh, PropMats.Sparkle, Vector3.up * 0.015f, Quaternion.Euler(0f, i * 61f, 0f));
        }

        static PropPickup Treasure(Transform root, PropCollectibleManager mgr, string key, Vector3 p, Mesh a, Mesh b, Material ma, Material mb, Vector3 off = default, Quaternion rot = default)
        {
            if (p == Vector3.zero) p = Fallback(900 + PropLore.TreasureIndex(key) * 3, islandExcl ?? new PropExclusion(16f));
            if (rot == default) rot = Quaternion.identity;
            var pk = Pickup(root, mgr, Collectibles.Id(CollectibleKind.Treasure, key), CollectibleKind.Treasure, p, 0f);
            Part(pk, a, ma, off, rot);
            if (b != null) Part(pk, b, mb, off + (a == coinsMesh ? new Vector3(0.1f, 0.02f, 0.05f) : Vector3.zero), rot);
            return pk;
        }

        /// A secret (L.6): no sparkle, a faint glint only within 2.5 m; the object stays after discovery.
        static PropPickup Secret(Transform root, PropCollectibleManager mgr, string key, Vector3 p, Mesh model, Material mat, float trigger)
        {
            if (p == Vector3.zero) p = Fallback(950 + PropLore.SecretIndex(key) * 3, islandExcl ?? new PropExclusion(16f));
            var pk = Pickup(root, mgr, Collectibles.Id(CollectibleKind.Secret, key), CollectibleKind.Secret, p, 0f, trigger);
            pk.keepModel = true;
            if (model != null) Part(pk, model, mat, Vector3.zero, Quaternion.identity);
            return pk;
        }

        static Vector3 Ground(Vector3 p, float minG)
        {
            if (p == Vector3.zero) return p;
            return NearDry(p, minG);
        }

        /// The lighthouse gallery floor at a distance from the lamp room (sea side, behind the railing).
        static Vector3 GalleryPoint(float side)
        {
            Vector3 g = PropInfo.LighthouseGallery;
            Vector3 toHut = WorldLayout.Hut - WorldLayout.Lighthouse;
            toHut.y = 0f;
            toHut.Normalize();
            Vector3 lat = Vector3.Cross(Vector3.up, toHut);
            return g - toHut * 2.1f + lat * side + Vector3.up * 0.02f;
        }

        static Vector3 WaterfallPage()
        {
            if (PropInfo.PoolCenter == Vector3.zero) return Vector3.zero;
            Vector3 d = WorldFeatures.FallDir;
            Vector3 side = Vector3.Cross(Vector3.up, d);
            // on the far side of the creek from the trail, behind the pool's rim rocks
            return NearDry(PropInfo.PoolCenter - side * (WorldFeatures.PoolRadius + 2.6f) - d * 2f, PropInfo.PoolCenter.y - 0.2f);
        }

        static Vector3 BridgeCoins(PropExclusion excl)
        {
            if (PropInfo.BridgeNorth == Vector3.zero) return Fallback(904, excl);
            Vector3 ax = PropInfo.BridgeSouth - PropInfo.BridgeNorth;
            ax.y = 0f;
            ax = ax.sqrMagnitude > 1e-4f ? ax.normalized : Vector3.forward;
            Vector3 lat = Vector3.Cross(Vector3.up, ax);
            // on the north rim, 5 m beside the bridge anchor, under a bush (≥ 3 m off the trail)
            return OffTrail(NearDry(PropInfo.BridgeNorth - ax * 1.5f + lat * 5f, 15f), 3.2f);
        }

        /// The island a point belongs to (landmark islands only).
        static string IslandOf(Vector3 p)
        {
            foreach (var mi in WorldFeatures.MiniIslands)
            {
                float dx = p.x - mi.center.x, dz = p.z - mi.center.z;
                if (dx * dx + dz * dz < (mi.shoreRadius + 15f) * (mi.shoreRadius + 15f)) return mi.id;
            }
            return null;
        }

        /// Moves a land point at least minDist away from every trail centre line (spiral up to 10 m, dry ground).
        static Vector3 OffTrail(Vector3 p, float minDist)
        {
            if (p == Vector3.zero) return p;
            if (PropPaths.DistanceToPath(p.x, p.z) >= minDist) return p;
            for (float r = 0.75f; r <= 10f; r += 0.75f)
            {
                int n = Mathf.CeilToInt(2f * Mathf.PI * r / 0.75f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                    if (PropPaths.DistanceToPath(x, z) < minDist) continue;
                    float g = G(x, z);
                    if (g < 0.3f || Mathf.Abs(g - (p.y - 0.02f)) > 2.5f) continue;
                    return new Vector3(x, g + 0.02f, z);
                }
            }
            return p;
        }

        /// Nearest ground at least minG high (spiral search up to 12 m), on the ground.
        static Vector3 NearDry(Vector3 p, float minG)
        {
            for (float r = 0f; r <= 12f; r += 0.75f)
            {
                int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 0.75f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                    float g = G(x, z);
                    if (g >= minG) return new Vector3(x, g + 0.02f, z);
                }
            }
            return new Vector3(p.x, G(p) + 0.02f, p.z);
        }

        /// A spot in the jungle core (densest jungle, ≥ 10 m off every trail), deterministic.
        static Vector3 JungleHeart()
        {
            float best = -1f;
            Vector3 bp = Vector3.zero;
            for (int i = 0; i < 400; i++)
            {
                float a = i * 2.39996f, r = Mathf.Sqrt(i / 400f) * 74f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                float g = G(x, z);
                if (g < 3f) continue;
                if (PropPaths.DistanceToPath(x, z) < 10f) continue;
                float j = PropJungle.JungleAt(x, z, g);
                if (j > best) { best = j; bp = new Vector3(x, g + 0.02f, z); }
            }
            return bp;
        }

        /// Walks inward along an angle from outside the island and returns the first point with ground in [minG, maxG].
        static bool FindRing(float ang, float minG, float maxG, PropExclusion excl, PropExclusion used, out Vector3 pos)
        {
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            for (float r = 220f; r > 40f; r -= 0.75f)
            {
                float x = d.x * r, z = d.z * r;
                float g = G(x, z);
                if (g < minG || g > maxG) continue;
                if (excl != null && excl.Blocked(x, z)) continue;
                if (used != null && used.Blocked(x, z)) continue;
                pos = new Vector3(x, g + 0.02f, z);
                return true;
            }
            pos = Vector3.zero;
            return false;
        }

        static Vector3 Fallback(int seed, PropExclusion excl)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < 300; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 93f);
                float g = G(o.x, o.y);
                if (g < 1.5f || g > 40f || excl.Blocked(o.x, o.y) || PropPaths.DistanceToPath(o.x, o.y) < 3.2f) continue;
                return new Vector3(o.x, g + 0.02f, o.y);
            }
            return new Vector3(WorldLayout.Hut.x + 9f, G(WorldLayout.Hut.x + 9f, WorldLayout.Hut.z + 6f) + 0.02f, WorldLayout.Hut.z + 6f);
        }

        static PropPickup Pickup(Transform parent, PropCollectibleManager mgr, string id, CollectibleKind kind, Vector3 pos, float yaw, float triggerRadius = 0.45f)
        {
            var go = new GameObject("Pickup_" + id.Replace(':', '_'));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.identity);
            var pk = go.AddComponent<PropPickup>();
            pk.id = id;
            pk.kind = kind;
            var model = new GameObject("Model");
            model.transform.SetParent(go.transform, false);
            pk.model = model;
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = triggerRadius;
            sc.center = Vector3.up * 0.15f;
            pk.trigger = sc;
            pk.sparkle = PropFX.Sparkle(go.transform, pos + Vector3.up * 0.25f, kind == CollectibleKind.Secret ? 0.12f : 0.28f);
            mgr.pickups.Add(pk);
            return pk;
        }

        static void Part(PropPickup pk, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot)
        {
            if (pk == null || pk.model == null || mesh == null) return;
            PropUtil.MeshObject(pk.model.transform, mesh.name, mesh, mat, localPos, localRot, true);
        }

        // ------------------------------------------------------------------ small models (built once, shared)
        static void BuildMeshes()
        {
            if (pageMesh != null) return;
            var b = new PropMeshBuilder();
            // page: slightly folded sheet
            b.Quad2(new Vector3(-0.11f, 0.012f, -0.15f), new Vector3(-0.11f, 0.0f, 0.15f), new Vector3(0f, 0.004f, 0.15f), new Vector3(0f, 0.018f, -0.15f), Vector3.up);
            b.Quad2(new Vector3(0f, 0.018f, -0.15f), new Vector3(0f, 0.004f, 0.15f), new Vector3(0.11f, 0.014f, 0.15f), new Vector3(0.11f, 0.026f, -0.15f), Vector3.up);
            pageMesh = Finish(b, "Page");
            b.Box(new Vector3(0f, 0.022f, 0f), new Vector3(0.24f, 0.012f, 0.025f), Quaternion.Euler(0f, 0f, 3f));
            ribbonMesh = Finish(b, "PageRibbon");

            // golden scallop shell: ridged fan + hinge ears
            Vector3 hinge = new Vector3(0f, 0.01f, -0.07f);
            const int ribs = 9;
            for (int i = 0; i < ribs; i++)
            {
                float a0 = Mathf.Lerp(-70f, 70f, i / (float)ribs) * Mathf.Deg2Rad, a1 = Mathf.Lerp(-70f, 70f, (i + 1) / (float)ribs) * Mathf.Deg2Rad;
                float am = (a0 + a1) * 0.5f;
                Vector3 r0 = hinge + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * 0.14f + Vector3.up * 0.005f;
                Vector3 r1 = hinge + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * 0.14f + Vector3.up * 0.005f;
                Vector3 rm = hinge + new Vector3(Mathf.Sin(am), 0f, Mathf.Cos(am)) * 0.15f + Vector3.up * 0.035f;
                b.Face3(hinge + Vector3.up * 0.02f, r0, rm, Vector3.up);
                b.Face3(hinge + Vector3.up * 0.02f, rm, r1, Vector3.up);
            }
            b.Box(hinge + new Vector3(0f, 0.008f, -0.01f), new Vector3(0.08f, 0.015f, 0.03f), Quaternion.identity);
            shellMesh = Finish(b, "GoldenShell");

            // coin pile
            var rng = new System.Random(55);
            for (int i = 0; i < 12; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.16f);
                float y = (i / 12f) * 0.06f;
                b.Cylinder(new Vector3(o.x, y, o.y), Quaternion.Euler(PropUtil.Range(rng, -15f, 15f), 0f, PropUtil.Range(rng, -15f, 15f)), 0.04f, 0.04f, 0.012f, 7, true, true);
            }
            coinsMesh = Finish(b, "Coins");
            b.Lathe(Vector3.zero, Quaternion.identity, new[] { new Vector2(0.05f, 0f), new Vector2(0.02f, 0.02f), new Vector2(0.015f, 0.08f), new Vector2(0.06f, 0.12f), new Vector2(0.07f, 0.17f) }, 8);
            cupMesh = Finish(b, "GoldCup");
            b.Ico(Vector3.zero, new Vector3(0.075f, 0.1f, 0.075f), Quaternion.identity, 1);
            eggMesh = Finish(b, "GoldenEgg");

            // brass diving helmet with windows (half buried)
            b.Ico(Vector3.zero, new Vector3(0.24f, 0.24f, 0.24f), Quaternion.identity, 1);
            b.Cylinder(new Vector3(0f, -0.26f, 0f), Quaternion.identity, 0.26f, 0.26f, 0.08f, 10, false, false);
            helmetMesh = Finish(b, "Helmet");
            b.Cylinder(new Vector3(0f, 0.02f, 0.22f), Quaternion.Euler(90f, 0f, 0f), 0.09f, 0.09f, 0.04f, 8, true, true);
            b.Cylinder(new Vector3(0.22f, 0.02f, 0f), Quaternion.Euler(0f, 0f, -90f), 0.06f, 0.06f, 0.04f, 8, true, true);
            b.Cylinder(new Vector3(-0.22f, 0.02f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.06f, 0.06f, 0.04f, 8, true, true);
            helmetGlassMesh = Finish(b, "HelmetGlass");

            // compass
            b.Cylinder(Vector3.zero, Quaternion.identity, 0.09f, 0.09f, 0.04f, 10, true, false);
            compassMesh = Finish(b, "Compass");
            b.Disk(new Vector3(0f, 0.042f, 0f), 0.075f, 10, Quaternion.identity, true);
            compassFaceMesh = Finish(b, "CompassFace");

            // film reel
            b.Cylinder(Vector3.zero, Quaternion.identity, 0.16f, 0.16f, 0.03f, 12, true, true);
            b.Box(new Vector3(0.22f, 0.002f, 0.05f), new Vector3(0.2f, 0.004f, 0.035f), Quaternion.Euler(0f, 18f, 0f));
            reelMesh = Finish(b, "FilmReel");

            // message bottle (lying on its side)
            b.Cylinder(new Vector3(0f, -0.12f, 0f), Quaternion.identity, 0.06f, 0.06f, 0.16f, 8, true, false);
            b.Cylinder(new Vector3(0f, 0.04f, 0f), Quaternion.identity, 0.06f, 0.022f, 0.05f, 8);
            b.Cylinder(new Vector3(0f, 0.09f, 0f), Quaternion.identity, 0.022f, 0.022f, 0.05f, 8);
            bottleMesh = Finish(b, "Bottle");
            b.Cylinder(new Vector3(0f, 0.135f, 0f), Quaternion.identity, 0.02f, 0.024f, 0.04f, 6, false, true);
            corkMesh = Finish(b, "Cork");
            b.Cylinder(new Vector3(0f, -0.1f, 0f), Quaternion.identity, 0.028f, 0.028f, 0.13f, 6, true, true);
            scrollMesh = Finish(b, "Scroll");

            // Gus' cookie tins + note
            for (int i = 0; i < 3; i++) b.Cylinder(new Vector3(-0.18f + i * 0.18f, 0f, 0f), Quaternion.identity, 0.08f, 0.08f, 0.11f + i * 0.02f, 8, false, true);
            tinsMesh = Finish(b, "Tins");
            b.Box(new Vector3(0f, 0.17f, 0f), new Vector3(0.14f, 0.004f, 0.1f), Quaternion.Euler(0f, 15f, 0f));
            noteMesh = Finish(b, "Note");

            // carved heart (flat, facing +z)
            var heart = new Vector3[] { new Vector3(0f, -0.07f, 0f), new Vector3(0.07f, 0.01f, 0f), new Vector3(0.05f, 0.05f, 0f), new Vector3(0f, 0.03f, 0f), new Vector3(-0.05f, 0.05f, 0f), new Vector3(-0.07f, 0.01f, 0f) };
            for (int i = 0; i < heart.Length; i++) b.Tri2(Vector3.zero, heart[i], heart[(i + 1) % heart.Length], Vector3.forward);
            heartMesh = Finish(b, "Heart");

            // pirate tricorn hat (L.6 #6 / H.1)
            b.Cylinder(Vector3.zero, Quaternion.identity, 0.2f, 0.2f, 0.025f, 3, true, true, 1f, 1f, 0f, false, 30f);
            b.Cylinder(new Vector3(0f, 0.02f, 0f), Quaternion.identity, 0.11f, 0.09f, 0.12f, 8, false, true);
            hatMesh = Finish(b, "Tricorn");
            b.Ring(new Vector3(0f, 0.03f, 0f), 0.17f, 0.2f, 3, Quaternion.Euler(0f, -30f, 0f));
            hatTrimMesh = Finish(b, "TricornTrim");

            // Gus' piggy bank
            b.Ico(new Vector3(0f, 0.12f, 0f), new Vector3(0.14f, 0.11f, 0.17f), Quaternion.identity, 1);
            b.Cylinder(new Vector3(0f, 0.12f, 0.15f), Quaternion.Euler(90f, 0f, 0f), 0.05f, 0.05f, 0.05f, 6, true, true);
            for (int s = -1; s <= 1; s += 2) for (int t = -1; t <= 1; t += 2) b.Cylinder(new Vector3(s * 0.07f, 0f, t * 0.08f), Quaternion.identity, 0.025f, 0.025f, 0.05f, 5, false, true);
            for (int s = -1; s <= 1; s += 2) b.Face3(new Vector3(s * 0.06f, 0.2f, 0.09f), new Vector3(s * 0.1f, 0.2f, 0.06f), new Vector3(s * 0.08f, 0.27f, 0.07f), Vector3.forward);
            piggyMesh = Finish(b, "PiggyBank");

            // golden microphone (lying)
            b.Cylinder(new Vector3(0f, -0.12f, 0f), Quaternion.identity, 0.025f, 0.035f, 0.18f, 8, true, true);
            micMesh = Finish(b, "GoldenMic");
            b.Ico(new Vector3(0f, 0.09f, 0f), Vector3.one * 0.05f, Quaternion.identity, 1);
            micHeadMesh = Finish(b, "GoldenMicHead");

            // a wheel of cheese, gold bars, an old film camera, a stack of tapes
            b.Cylinder(Vector3.zero, Quaternion.identity, 0.2f, 0.2f, 0.12f, 10, true, true);
            cheeseMesh = Finish(b, "CheeseWheelPickup");
            for (int i = 0; i < 3; i++) b.Box(new Vector3(-0.09f + i * 0.09f, 0.025f, 0f), new Vector3(0.08f, 0.05f, 0.18f), Quaternion.identity);
            b.Box(new Vector3(-0.045f, 0.075f, 0f), new Vector3(0.08f, 0.05f, 0.18f), Quaternion.identity);
            barsMesh = Finish(b, "GoldBars");
            b.Box(new Vector3(0f, 0.09f, 0f), new Vector3(0.2f, 0.16f, 0.12f), Quaternion.identity);
            b.Cylinder(new Vector3(0f, 0.09f, 0.06f), Quaternion.Euler(90f, 0f, 0f), 0.05f, 0.04f, 0.1f, 8, true, true);
            b.Cylinder(new Vector3(-0.05f, 0.2f, 0f), Quaternion.Euler(0f, 0f, 90f), 0.07f, 0.07f, 0.03f, 10, true, true);
            cameraMesh = Finish(b, "OldCamera");
            for (int i = 0; i < 4; i++) b.Cylinder(new Vector3(0f, i * 0.035f, 0f), Quaternion.Euler(0f, i * 20f, 0f), 0.1f, 0.1f, 0.03f, 8, true, true);
            tapesMesh = Finish(b, "TapeStack");
        }

        static Mesh Finish(PropMeshBuilder b, string name)
        {
            var m = b.ToMesh(name);
            LowPoly.Sanitize(m);
            b.Clear();
            return m;
        }
    }
}
