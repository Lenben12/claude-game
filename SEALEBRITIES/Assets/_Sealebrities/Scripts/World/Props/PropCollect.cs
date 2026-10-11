using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Places the optional collectibles (GDD 14.30 / 15.33): 15 pages of Onkel Gus' logbook, 20 golden shells (some in
    /// shallow water), 6 treasures, 4 secrets (cave grotto, behind the waterfall, lighthouse top, radio tower) and the
    /// message in a bottle (from day 3 at a beach spot chosen by GameState.saveSeed). Every spot is derived from the
    /// built points of interest or from fixed seeds, so all co-op machines agree. Totals are registered per kind.
    public static class PropCollect
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        static Mesh pageMesh, ribbonMesh, shellMesh, coinsMesh, cupMesh, eggMesh, helmetMesh, helmetGlassMesh, compassMesh, compassFaceMesh, reelMesh;
        static Mesh bottleMesh, corkMesh, scrollMesh, tinsMesh, noteMesh, heartMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pageMesh = null; ribbonMesh = null; shellMesh = null; coinsMesh = null; cupMesh = null; eggMesh = null; helmetMesh = null; helmetGlassMesh = null;
            compassMesh = null; compassFaceMesh = null; reelMesh = null; bottleMesh = null; corkMesh = null; scrollMesh = null; tinsMesh = null; noteMesh = null; heartMesh = null;
        }

        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            BuildMeshes();
            var root = new GameObject("Collectibles");
            root.transform.SetParent(island, false);
            var mgr = root.AddComponent<PropCollectibleManager>();

            // ---------------------------------------------------------------- logbook pages
            // spots beside the rope bridge ends (never in the ravine itself – players could get stuck down there)
            Vector3 bridgePage = Vector3.zero, bridgeCoins = Vector3.zero;
            if (PropInfo.BridgeNorth != Vector3.zero && PropInfo.BridgeSouth != Vector3.zero)
            {
                Vector3 ax = PropInfo.BridgeSouth - PropInfo.BridgeNorth;
                ax.y = 0f;
                ax = ax.sqrMagnitude > 1e-4f ? ax.normalized : Vector3.forward;
                Vector3 lat = Vector3.Cross(Vector3.up, ax);
                bridgePage = NearDry(PropInfo.BridgeSouth + ax * 1.8f + lat * 1.7f, 0.5f);
                bridgeCoins = NearDry(PropInfo.BridgeNorth - ax * 2.6f - lat * 1.4f, 0.5f);
            }
            Vector3 poolPage = Vector3.zero;
            if (PropInfo.PoolCenter != Vector3.zero && PropInfo.WaterfallLip != Vector3.zero)
            {
                Vector3 away = PropInfo.PoolCenter - PropInfo.WaterfallLip;
                away.y = 0f;
                poolPage = NearDry(PropInfo.PoolCenter + away.normalized * 7.4f, 0.6f);
            }
            var pages = new List<Vector3>
            {
                Off(PropInfo.Boathouse, new Vector3(1.1f, 0.03f, 1.3f)),
                PropInfo.Campfire != Vector3.zero ? NearDry(PropInfo.Campfire + new Vector3(2.6f, 0f, -0.8f), 0.4f) : Vector3.zero,
                PropInfo.LighthouseBench,
                poolPage,
                PropInfo.CaveChamber,
                PropInfo.GrottoTable,
                PropInfo.BridgeMid != Vector3.zero ? PropInfo.BridgeMid + Vector3.up * 0.03f : Vector3.zero,
                bridgePage,
                PropInfo.RadioTowerBase != Vector3.zero ? PropInfo.RadioTowerBase + new Vector3(-2.2f, 0.02f, -2.0f) : Vector3.zero,
                PropInfo.BayArch != Vector3.zero ? NearDry(PropInfo.BayArch, 0.4f) : Vector3.zero,
                PropInfo.Hammock,
                PropInfo.TidePoolsCenter != Vector3.zero ? NearDry(PropInfo.TidePoolsCenter + new Vector3(3f, 0f, -2f), 0.9f) : Vector3.zero,
                JungleHeart(),
                Mini("kelp_forest"),
                Mini("deep_trench"),
            };
            int pageCount = 0;
            for (int i = 0; i < PropLore.PageCount; i++)
            {
                Vector3 p = i < pages.Count ? pages[i] : Vector3.zero;
                if (p == Vector3.zero) p = Fallback(i * 37 + 5, excl);
                var pk = Pickup(root.transform, mgr, PropLore.PageId(i), CollectibleKind.LogbookPage, p, i * 47f);
                pk.pageIndex = i;
                Part(pk, pageMesh, PropMats.Paper, Vector3.up * 0.01f, Quaternion.Euler(0f, i * 47f, 0f));
                Part(pk, ribbonMesh, PropMats.LighthouseRed, Vector3.up * 0.01f, Quaternion.Euler(0f, i * 47f, 0f));
                pageCount++;
            }
            Collectibles.RegisterTotal(CollectibleKind.LogbookPage, pageCount);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- golden shells (12 on beaches, 6 in shallow water, 2 on mini islands)
            var rng = new System.Random(9876);
            var shells = new List<Vector3>();
            var used = new PropExclusion(16f);
            for (int k = 0; k < 40 && shells.Count < 12; k++)
            {
                float a = (k * 137.508f + 11f) * Mathf.Deg2Rad;
                if (FindRing(a, 0.35f, 1.5f, excl, used, out var p)) { shells.Add(p); used.Add(p.x, p.z, 12f); }
            }
            int water = 0;
            for (int k = 0; k < 60 && water < 6; k++)
            {
                float a = (k * 97.3f + 40f) * Mathf.Deg2Rad;
                if (FindRing(a, -2.4f, -0.7f, null, used, out var p)) { shells.Add(p); used.Add(p.x, p.z, 12f); water++; }
            }
            foreach (var m in PropInfo.MiniShellSpots) if (shells.Count < 20) shells.Add(m);
            while (shells.Count < 20) shells.Add(Fallback(700 + shells.Count * 13, excl));
            for (int i = 0; i < shells.Count; i++)
            {
                var pk = Pickup(root.transform, mgr, Collectibles.Id(CollectibleKind.GoldenShell, "s" + (i + 1).ToString("00")), CollectibleKind.GoldenShell, shells[i], i * 61f);
                Part(pk, shellMesh, PropMats.Sparkle, Vector3.up * 0.015f, Quaternion.Euler(0f, i * 61f, 0f));
            }
            Collectibles.RegisterTotal(CollectibleKind.GoldenShell, shells.Count);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- treasures
            int treasures = 0;
            {
                Vector3 p = PropInfo.TreasureChest != Vector3.zero ? PropInfo.TreasureChest + Vector3.up * 0.7f : Fallback(901, excl);
                var pk = Pickup(root.transform, mgr, TreasureId(0), CollectibleKind.Treasure, p, 0f);
                Part(pk, coinsMesh, PropMats.Sparkle, Vector3.zero, Quaternion.identity);
                Part(pk, cupMesh, PropMats.Sparkle, new Vector3(0.1f, 0.02f, 0.05f), Quaternion.identity);
                treasures++;
            }
            {
                Vector3 p = Mini("blue_drop");
                if (p == Vector3.zero) p = Fallback(902, excl);
                var pk = Pickup(root.transform, mgr, TreasureId(1), CollectibleKind.Treasure, p, 0f);
                Part(pk, eggMesh, PropMats.Sparkle, Vector3.up * 0.1f, Quaternion.Euler(0f, 0f, 12f));
                treasures++;
            }
            {
                Vector3 p = PropInfo.HelmetSpot != Vector3.zero ? PropInfo.HelmetSpot : Fallback(903, excl);
                var pk = Pickup(root.transform, mgr, TreasureId(2), CollectibleKind.Treasure, p, 0f);
                Part(pk, helmetMesh, PropMats.Flat(LowPoly.Hex("#B8913A")), Vector3.up * 0.1f, Quaternion.Euler(-14f, 30f, 8f));
                Part(pk, helmetGlassMesh, PropMats.Flat(PropPal.HullBlue), Vector3.up * 0.1f, Quaternion.Euler(-14f, 30f, 8f));
                treasures++;
            }
            {
                Vector3 p = bridgeCoins != Vector3.zero ? bridgeCoins : Fallback(904, excl);
                var pk = Pickup(root.transform, mgr, TreasureId(3), CollectibleKind.Treasure, p, 0f);
                Part(pk, coinsMesh, PropMats.Sparkle, Vector3.zero, Quaternion.Euler(0f, 70f, 0f));
                treasures++;
            }
            {
                Vector3 p = Mini("wreck");
                if (p == Vector3.zero) p = Fallback(905, excl);
                var pk = Pickup(root.transform, mgr, TreasureId(4), CollectibleKind.Treasure, p, 0f);
                Part(pk, compassMesh, PropMats.Flat(LowPoly.Hex("#B8913A")), Vector3.up * 0.02f, Quaternion.Euler(0f, 20f, 0f));
                Part(pk, compassFaceMesh, PropMats.Paper, Vector3.up * 0.02f, Quaternion.Euler(0f, 20f, 0f));
                treasures++;
            }
            {
                Vector3 p = PropInfo.RadioTowerBase != Vector3.zero ? PropInfo.RadioTowerBase + new Vector3(2.2f, 0.02f, 1.6f) : Fallback(906, excl);
                var pk = Pickup(root.transform, mgr, TreasureId(5), CollectibleKind.Treasure, p, 0f);
                Part(pk, reelMesh, PropMats.Flat(PropPal.HullBlue), Vector3.up * 0.03f, Quaternion.identity);
                treasures++;
            }
            Collectibles.RegisterTotal(CollectibleKind.Treasure, treasures);

            // ---------------------------------------------------------------- secrets (the object stays, only the sparkle stops)
            int secrets = 0;
            if (PropInfo.GrottoCamera != Vector3.zero)
            {
                var pk = Pickup(root.transform, mgr, SecretId(0), CollectibleKind.Secret, PropInfo.GrottoCamera + Vector3.up * 1.35f, 0f, 0.6f);
                pk.keepModel = true;
            }
            else Pickup(root.transform, mgr, SecretId(0), CollectibleKind.Secret, Fallback(950, excl), 0f).keepModel = true;
            secrets++;
            {
                Vector3 p = PropInfo.WaterfallNiche != Vector3.zero ? PropInfo.WaterfallNiche : Fallback(951, excl);
                var pk = Pickup(root.transform, mgr, SecretId(1), CollectibleKind.Secret, p, 0f, 0.6f);
                Part(pk, tinsMesh, PropMats.LighthouseRed, Vector3.zero, Quaternion.identity);
                Part(pk, noteMesh, PropMats.Paper, Vector3.zero, Quaternion.identity);
                pk.keepModel = true;
                secrets++;
            }
            {
                Vector3 g = PropInfo.LighthouseGallery;
                Vector3 toHut = WorldLayout.Hut - WorldLayout.Lighthouse;
                toHut.y = 0f;
                toHut.Normalize();
                Vector3 p = g != Vector3.zero ? g + toHut * 2.45f + Vector3.up * 0.75f : Fallback(952, excl);
                var pk = Pickup(root.transform, mgr, SecretId(2), CollectibleKind.Secret, p, Mathf.Atan2(toHut.x, toHut.z) * Mathf.Rad2Deg, 0.6f);
                Part(pk, heartMesh, PropMats.Flat(PropPal.Coral), Vector3.zero, Quaternion.LookRotation(toHut));
                pk.keepModel = true;
                secrets++;
            }
            {
                Vector3 p = PropInfo.RadioShack != Vector3.zero ? PropInfo.RadioShack : Fallback(953, excl);
                var pk = Pickup(root.transform, mgr, SecretId(3), CollectibleKind.Secret, p, 0f, 0.5f);
                pk.keepModel = true;
                secrets++;
            }
            Collectibles.RegisterTotal(CollectibleKind.Secret, secrets);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- message in a bottle (day 3+, spot by save seed)
            for (int k = 0; k < 24; k++)
            {
                float a = k * 15f * Mathf.Deg2Rad;
                if (FindRing(a, 0.25f, 1.0f, excl, null, out var p)) mgr.bottleSpots.Add(p);
            }
            if (mgr.bottleSpots.Count == 0) mgr.bottleSpots.Add(Fallback(990, excl));
            {
                var pk = Pickup(root.transform, mgr, Collectibles.Id(CollectibleKind.BottleMessage, PropLore.BottleKey), CollectibleKind.BottleMessage, mgr.bottleSpots[0], 0f);
                pk.requireDay3 = true;
                Part(pk, bottleMesh, PropMats.BottleGlass, Vector3.up * 0.07f, Quaternion.Euler(0f, 0f, 84f));
                Part(pk, corkMesh, PropMats.WoodLight, Vector3.up * 0.07f, Quaternion.Euler(0f, 0f, 84f));
                Part(pk, scrollMesh, PropMats.Paper, Vector3.up * 0.07f, Quaternion.Euler(0f, 0f, 84f));
                mgr.bottle = pk;
            }
            Collectibles.RegisterTotal(CollectibleKind.BottleMessage, 1);
            mgr.RefreshAll();
        }

        static string TreasureId(int i) => Collectibles.Id(CollectibleKind.Treasure, PropLore.Treasures[i].key);
        static string SecretId(int i) => Collectibles.Id(CollectibleKind.Secret, PropLore.Secrets[i].key);

        static Vector3 Off(Vector3 basePos, Vector3 off) => basePos == Vector3.zero ? Vector3.zero : basePos + off;

        static Vector3 Ground(Vector3 p) => new Vector3(p.x, G(p) + 0.02f, p.z);

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
            return Ground(p);
        }

        static Vector3 Mini(string id) => PropInfo.MiniLandmarks.TryGetValue(id, out var p) ? p : Vector3.zero;

        /// A spot in the jungle core (densest jungle near the island center), deterministic.
        static Vector3 JungleHeart()
        {
            float best = -1f;
            Vector3 bp = Vector3.zero;
            for (int i = 0; i < 400; i++)
            {
                float a = i * 2.39996f, r = Mathf.Sqrt(i / 400f) * 120f;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                float g = G(x, z);
                if (g < 3f) continue;
                if (PropPaths.DistanceToPath(x, z) < 6f) continue;
                float j = PropJungle.JungleAt(x, z, g);
                if (j > best) { best = j; bp = new Vector3(x, g + 0.02f, z); }
            }
            return bp;
        }

        /// Walks inward along an angle from outside the island and returns the first point with ground in [minG, maxG].
        static bool FindRing(float ang, float minG, float maxG, PropExclusion excl, PropExclusion used, out Vector3 pos)
        {
            Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            for (float r = 260f; r > 60f; r -= 0.75f)
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
                Vector2 o = PropUtil.InDisc(rng, 150f);
                float g = G(o.x, o.y);
                if (g < 1.5f || g > 40f || excl.Blocked(o.x, o.y)) continue;
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
            pk.sparkle = PropFX.Sparkle(go.transform, pos + Vector3.up * 0.25f, 0.28f);
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

            // carved heart (flat on the railing)
            var heart = new Vector3[] { new Vector3(0f, -0.07f, 0f), new Vector3(0.07f, 0.01f, 0f), new Vector3(0.05f, 0.05f, 0f), new Vector3(0f, 0.03f, 0f), new Vector3(-0.05f, 0.05f, 0f), new Vector3(-0.07f, 0.01f, 0f) };
            for (int i = 0; i < heart.Length; i++) b.Tri2(Vector3.zero, heart[i], heart[(i + 1) % heart.Length], Vector3.forward);
            heartMesh = Finish(b, "Heart");
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
