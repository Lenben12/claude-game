using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Lush, layered island vegetation (art bible §1.9, rule 7): ≈260 palms in coastal clusters of 3–7 with open sand
    /// between them (10 % inland), ≈180 broadleaf trees forming a closed canopy in the jungle core, ≈40 conifers on
    /// the lighthouse hill and the north cliffs, flowering bushes / ferns / taro / bananas / meadow flowers, rocks with
    /// moss and crevices, and dense grass clumps on every green area (GPU instanced, PropGrassField).
    /// Paths get the three height layers (grass < 1 m, bush 1–2 m, tree > 6 m) on both sides.
    /// Trees and palms: full detail up to 140 m, lite silhouettes up to 1300 m (skyline from the sea); colliders on
    /// trunks and big rocks only (bushes and grass stay passable). Everything is placed with fixed seeds.
    public static class PropJungle
    {
        public const int PalmTarget = 260;
        public const int TreeTarget = 180;
        public const int ConiferTarget = 40;
        public const float TreeNear = 180f, TreeFar = 1300f;   // 180: lodBias is 1 now (the old hidden x2 doubled it to 280)

        static float G(float x, float z) => PropGround.H(x, z);

        /// Ground class the terrain paints here: palms and plants avoid rock platforms, gravel / creek beds and wet sand;
        /// trees only grow on grass.
        static bool PalmGround(float x, float z, float g)
        {
            var h = WorldTerrainData.GroundAt(x, z, g);
            return h == GroundHint.None || h == GroundHint.Sand;
        }

        static bool GrassGround(float x, float z, float g) => WorldTerrainData.GroundAt(x, z, g) == GroundHint.None;

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// 0..1 jungle density (denser uphill and in noise patches – the same idea as the terrain's dark-grass zones).
        /// Round 3: open flower meadows (Meadow) break the canopy so hut, helipad and lighthouse read from afar.
        public static float JungleAt(float x, float z, float h)
        {
            float n = WorldNoise.Fbm(x * 0.012f - 4f, z * 0.012f + 6f, 2);
            return Mathf.Clamp01(Smooth(-0.2f, 0.25f, n) * 0.75f + 0.4f * Smooth(6f, 18f, h)) * (1f - 0.95f * Meadow(x, z));
        }

        /// Round 3 (GDD §19 "übersichtlich"): 0..1 inside the open meadows – the lowland between the hut and the helipad,
        /// the north lowland on the lighthouse trail and the slope south of the ravine. No canopy trees there (grass,
        /// flowers, a few bushes), soft wobbly borders.
        public static float Meadow(float x, float z)
        {
            float m = 0f;
            for (int i = 0; i < MeadowX.Length; i++)
            {
                float dx = x - MeadowX[i], dz = z - MeadowZ[i];
                float d = Mathf.Sqrt(dx * dx + dz * dz);
                float r = MeadowR[i] * (1f + 0.18f * (WorldNoise.Perlin(x * 0.06f + i * 7.1f, z * 0.06f - i * 3.3f)));
                m = Mathf.Max(m, 1f - Smooth(r * 0.7f, r, d));
            }
            return m;
        }

        static readonly float[] MeadowX = { 52f, 40f, 18f }, MeadowZ = { -12f, 50f, -38f }, MeadowR = { 20f, 14f, 12f };

        static readonly float[] ShoreDist = { 6f, 12f, 20f, 28f, 36f };

        /// True when sea (ground below 0.3 m) is within 'within' meters; dir = horizontal direction toward it.
        public static bool NearShore(float x, float z, float within, out Vector3 dir)
        {
            dir = Vector3.zero;
            for (int k = 0; k < ShoreDist.Length; k++)
            {
                float d = ShoreDist[k];
                if (d > within) break;
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * Mathf.PI * 0.25f + k * 0.3f;
                    float cx = Mathf.Cos(ang), cz = Mathf.Sin(ang);
                    if (G(x + cx * d, z + cz * d) < 0.3f)
                    {
                        dir = new Vector3(cx, 0f, cz);
                        return true;
                    }
                }
            }
            return false;
        }

        sealed class Variants
        {
            public readonly List<PropModel> palms = new List<PropModel>(), palmsLite = new List<PropModel>();
            public readonly List<PropModel> trees = new List<PropModel>(), treesLite = new List<PropModel>();
            public readonly List<PropModel> conifers = new List<PropModel>(), conifersLite = new List<PropModel>();
            public readonly List<PropModel> bushes = new List<PropModel>(), flowering = new List<PropModel>();
            public readonly List<PropModel> ferns = new List<PropModel>(), taro = new List<PropModel>(), banana = new List<PropModel>();
            public readonly List<PropModel> flowers = new List<PropModel>();

            public void DestroyAll()
            {
                foreach (var l in new[] { palms, palmsLite, trees, treesLite, conifers, conifersLite, bushes, flowering, ferns, taro, banana, flowers })
                    foreach (var m in l)
                        foreach (var p in m.parts)
                            if (p.mesh != null) Object.Destroy(p.mesh);
            }
        }

        static Variants kit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { kit = null; }

        /// The shared plant variants (also used by the mini islands); built once, destroyed by ReleaseKit.
        static IEnumerator BuildKit()
        {
            if (kit != null) yield break;
            var k = new Variants();
            for (int i = 0; i < 12; i++)
            {
                k.palms.Add(PropPlants.Palm(PalmSeed(i), false));
                k.palmsLite.Add(PropPlants.Palm(PalmSeed(i), true));
                if (PropRun.Due()) yield return null;
            }
            for (int i = 0; i < 8; i++)
            {
                k.trees.Add(PropPlants.BroadleafTree(1500 + i * 13, false));
                k.treesLite.Add(PropPlants.BroadleafTree(1500 + i * 13, true));
                if (PropRun.Due()) yield return null;
            }
            for (int i = 0; i < 5; i++)
            {
                k.conifers.Add(PropPlants.Conifer(1700 + i * 11, false));
                k.conifersLite.Add(PropPlants.Conifer(1700 + i * 11, true));
            }
            if (PropRun.Due()) yield return null;
            for (int i = 0; i < 5; i++) k.bushes.Add(PropPlants.Bush(3000 + i, 0));
            k.flowering.Add(PropPlants.Bush(3100, 1));
            k.flowering.Add(PropPlants.Bush(3101, 1));
            k.flowering.Add(PropPlants.Bush(3102, 2));
            k.flowering.Add(PropPlants.Bush(3103, 3));
            for (int i = 0; i < 4; i++) k.ferns.Add(PropPlants.Fern(2000 + i));
            if (PropRun.Due()) yield return null;
            for (int i = 0; i < 3; i++) k.taro.Add(PropPlants.Taro(4000 + i));
            for (int i = 0; i < 2; i++) k.banana.Add(PropPlants.Banana(4100 + i));
            k.flowers.Add(PropPlants.Flowers(4200, PropMats.FlowerPink));
            k.flowers.Add(PropPlants.Flowers(4201, PropMats.FlowerYellow));
            k.flowers.Add(PropPlants.Flowers(4202, PropMats.FlowerWhite));
            k.flowers.Add(PropPlants.Flowers(4203, PropMats.FlowerPurple));
            k.flowers.Add(PropPlants.Flowers(4204, PropMats.FlowerMagenta));
            kit = k;
        }

        public static IEnumerator PrepareKit() => BuildKit();

        /// Seed of palm variant i (PropPlants.Palm) – the trunk colliders replay its curve.
        static int PalmSeed(int i) => 1000 + i * 17;

        public static void ReleaseKit()
        {
            if (kit == null) return;
            kit.DestroyAll();
            kit = null;
        }

        // ------------------------------------------------------------------ shared placement helpers (also mini islands)
        public static PropModel RandomPalm(System.Random rng, out PropModel lite)
        {
            int i = rng.Next(kit.palms.Count);
            lite = kit.palmsLite[i];
            return kit.palms[i];
        }

        /// Adds a palm leaning toward 'leanDir' (horizontal) with its trunk collider. groundY overrides the terrain
        /// height (props-built mini islands).
        public static void AddPalm(PropBatch full, PropBatch lite, GameObject colHolder, System.Random rng, Vector3 groundPos, Vector3 leanDir, float groundY = float.NaN)
        {
            int palmIdx = rng.Next(kit.palms.Count);
            var model = kit.palms[palmIdx];
            var liteModel = kit.palmsLite[palmIdx];
            leanDir.y = 0f;
            if (leanDir.sqrMagnitude < 1e-4f)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                leanDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            Quaternion rot = Quaternion.LookRotation(leanDir.normalized) * Quaternion.Euler(0f, -90f, 0f);
            float s = PropUtil.Range(rng, 0.85f, 1.15f);
            float x = groundPos.x, z = groundPos.z;
            float y = !float.IsNaN(groundY) ? groundY - 0.15f
                : Mathf.Min(Mathf.Min(G(x + 0.4f, z), G(x - 0.4f, z)), Mathf.Min(G(x, z + 0.4f), G(x, z - 0.4f))) - 0.08f;
            var trs = Matrix4x4.TRS(new Vector3(x, y, z), rot, Vector3.one * s);
            full.Add(model, trs);
            lite.Add(liteModel, trs);
            // round 3 (GDD §19 collisions): capsules ALONG the leaning trunk (the old upright capsule at the foot let you
            // walk through the trunk 0.4–1.3 m above the ground)
            if (colHolder != null) PropSolid.PalmTrunk(colHolder.transform, PalmSeed(palmIdx), new Vector3(x, y, z), rot.eulerAngles.y, s);
        }

        public static void AddTree(PropBatch full, PropBatch lite, GameObject colHolder, System.Random rng, Vector3 groundPos, float scale)
        {
            int i = rng.Next(kit.trees.Count);
            Quaternion rot = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
            Vector3 p = new Vector3(groundPos.x, groundPos.y - 0.05f, groundPos.z);
            var trs = Matrix4x4.TRS(p, rot, Vector3.one * scale);
            full.Add(kit.trees[i], trs);
            lite.Add(kit.treesLite[i], trs);
            if (colHolder != null) PropCol.UprightCapsule(colHolder, p, 0.42f * scale, 4f);
        }

        public static void AddConifer(PropBatch full, PropBatch lite, GameObject colHolder, System.Random rng, Vector3 groundPos, float scale)
        {
            int i = rng.Next(kit.conifers.Count);
            Quaternion rot = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
            Vector3 p = new Vector3(groundPos.x, groundPos.y - 0.05f, groundPos.z);
            var trs = Matrix4x4.TRS(p, rot, Vector3.one * scale);
            full.Add(kit.conifers[i], trs);
            lite.Add(kit.conifersLite[i], trs);
            if (colHolder != null) PropCol.UprightCapsule(colHolder, p, 0.3f * scale, 4f);
        }

        /// kind: 0 bush, 1 flowering bush, 2 fern, 3 taro, 4 banana, 5 flowers.
        public static void AddPlant(PropBatch batch, PropBatch flowerBatch, System.Random rng, Vector3 groundPos, int kind, float scaleMul = 1f)
        {
            PropModel m;
            switch (kind)
            {
                case 1: m = kit.flowering[rng.Next(kit.flowering.Count)]; break;
                case 2: m = kit.ferns[rng.Next(kit.ferns.Count)]; break;
                case 3: m = kit.taro[rng.Next(kit.taro.Count)]; break;
                case 4: m = kit.banana[rng.Next(kit.banana.Count)]; break;
                case 5: m = kit.flowers[rng.Next(kit.flowers.Count)]; break;
                default: m = kit.bushes[rng.Next(kit.bushes.Count)]; break;
            }
            float s = PropUtil.Range(rng, 0.8f, 1.25f) * scaleMul;
            Vector3 n = PropGround.Normal(groundPos.x, groundPos.z, 0.8f);
            Quaternion rot = PropUtil.OnGround(n, kind == 4 ? 0.1f : 0.5f, PropUtil.Range(rng, 0f, 360f));
            var trs = Matrix4x4.TRS(new Vector3(groundPos.x, groundPos.y - 0.04f, groundPos.z), rot, Vector3.one * s);
            (kind == 5 ? flowerBatch : batch).Add(m, trs);
        }

        // ------------------------------------------------------------------ build
        public static IEnumerator Build(Transform island, PropExclusion excl, PropExclusion pathMask, PropGrassField grass)
        {
            var root = PropUtil.WorldRoot("Jungle", island);
            var rng = new System.Random(4711);
            yield return BuildKit();
            if (kit == null) yield break;

            float ext = WorldLayout.IslandRadius + 40f;
            Vector2 lh = new Vector2(WorldLayout.Lighthouse.x, WorldLayout.Lighthouse.z);

            var full = new PropBatch(64f);
            var lite = new PropBatch(64f);
            var treeCols = new GameObject("TreeColliders");
            treeCols.transform.SetParent(root, false);
            treeCols.transform.position = Vector3.zero;
            var spacing = new PropExclusion(8f);
            var treeSpots = new List<Vector3>(600);

            // ---------------------------------------------------------------- palms: coastal clusters
            var clusters = new PropExclusion(16f);
            int placed = 0, attempts = 0;
            int coastalTarget = Mathf.RoundToInt(PalmTarget * 0.9f);
            while (placed < coastalTarget && attempts < 30000)
            {
                attempts++;
                if (PropRun.Due()) yield return null;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 0.9f || g > 6f) continue;
                if (excl.Blocked(x, z, 1.5f) || clusters.Blocked(x, z)) continue;
                if (!NearShore(x, z, 36f, out var seaDir)) continue;
                clusters.Add(x, z, PropUtil.Range(rng, 10f, 16f));
                int n = 3 + rng.Next(5);
                for (int k = 0; k < n && placed < coastalTarget; k++)
                {
                    Vector2 o = k == 0 ? Vector2.zero : PropUtil.InDisc(rng, 6.5f);
                    float px = x + o.x, pz = z + o.y;
                    float pg = G(px, pz);
                    if (pg < 0.6f || pg > 9f) continue;
                    if (excl.Blocked(px, pz, 0.6f) || spacing.Blocked(px, pz, 1.4f)) continue;   // 3–4 m apart in a cluster
                    if (!PalmGround(px, pz, pg)) continue;
                    if (PropGround.Slope(px, pz, 1.2f) > 1.0f) continue;
                    Vector3 lean = Vector3.Slerp(seaDir, new Vector3(o.x, 0f, o.y).normalized + seaDir * 0.01f, 0.25f);
                    AddPalm(full, lite, treeCols, rng, new Vector3(px, pg, pz), lean);
                    spacing.Add(px, pz, PropUtil.Range(rng, 1.5f, 2.6f));
                    treeSpots.Add(new Vector3(px, pg, pz));
                    placed++;
                }
                if (PropRun.Due()) yield return null;
            }
            // inland palms (≈10 %)
            int inlandTarget = PalmTarget - placed;
            attempts = 0;
            int inland = 0;
            while (inland < inlandTarget && attempts < 10000)
            {
                attempts++;
                if (PropRun.Due()) yield return null;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 4f || g > 30f) continue;
                if (excl.Blocked(x, z, 1f) || spacing.Blocked(x, z, 2f)) continue;
                if (PropGround.Slope(x, z, 1.5f) > 0.9f) continue;
                if (Vector2.Distance(new Vector2(x, z), lh) < 24f) continue;
                if (Meadow(x, z) > 0.5f) continue;
                if (!PalmGround(x, z, g)) continue;
                Vector3 n = PropGround.Normal(x, z, 2f);
                AddPalm(full, lite, treeCols, rng, new Vector3(x, g, z), new Vector3(n.x, 0f, n.z));
                spacing.Add(x, z, 2.5f);
                treeSpots.Add(new Vector3(x, g, z));
                inland++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- broadleaf canopy trees (jungle core first)
            placed = 0;
            for (int pass = 0; pass < 3 && placed < TreeTarget; pass++)
            {
                float minJ = pass == 0 ? 0.62f : (pass == 1 ? 0.45f : 0.3f);
                attempts = 0;
                while (placed < TreeTarget && attempts < 9000)
                {
                    attempts++;
                    if (PropRun.Due()) yield return null;
                    float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                    float g = G(x, z);
                    if (g < 3.5f || g > 60f) continue;
                    float j = JungleAt(x, z, g);
                    if (j < minJ) continue;
                    if (excl.Blocked(x, z, 2.2f) || spacing.Blocked(x, z, 1.5f)) continue;
                    if (PropGround.Slope(x, z, 1.5f) > 0.85f) continue;
                    if (Vector2.Distance(new Vector2(x, z), lh) < 22f) continue;
                    if (!GrassGround(x, z, g)) continue;
                    if (NearShore(x, z, 12f, out _)) continue;
                    float s = PropUtil.Range(rng, 0.85f, 1.15f);
                    AddTree(full, lite, treeCols, rng, new Vector3(x, g, z), s);
                    // closed canopy in the core: 5.5–7 m apart, 8–11 m at the edge
                    float sp = j > 0.62f ? PropUtil.Range(rng, 2.8f, 3.5f) : PropUtil.Range(rng, 4f, 5.5f);
                    spacing.Add(x, z, sp);
                    treeSpots.Add(new Vector3(x, g, z));
                    placed++;
                    if (PropRun.Due()) yield return null;
                }
            }

            // ---------------------------------------------------------------- conifers: lighthouse hill and north cliffs
            placed = 0;
            attempts = 0;
            while (placed < ConiferTarget && attempts < 8000)
            {
                attempts++;
                if (PropRun.Due()) yield return null;
                float x, z;
                if (rng.NextDouble() < 0.65)
                {
                    Vector2 o = PropUtil.InDisc(rng, 95f);
                    x = lh.x + o.x; z = lh.y + o.y;
                    if (o.magnitude < 22f) continue;
                }
                else
                {
                    x = PropUtil.Range(rng, -ext, ext);
                    z = PropUtil.Range(rng, 90f, ext);
                }
                float g = G(x, z);
                if (g < 11f) continue;
                if (excl.Blocked(x, z, 2f) || spacing.Blocked(x, z, 1.5f)) continue;
                if (!GrassGround(x, z, g)) continue;
                if (PropGround.Slope(x, z, 1.5f) > 1.0f) continue;
                AddConifer(full, lite, treeCols, rng, new Vector3(x, g, z), PropUtil.Range(rng, 0.85f, 1.15f));
                spacing.Add(x, z, PropUtil.Range(rng, 3f, 4f));
                treeSpots.Add(new Vector3(x, g, z));
                placed++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- undergrowth
            var under = new PropBatch(48f);
            var flowerBatch = new PropBatch(48f);
            var plantSpacing = new PropExclusion(8f);
            bool FreeSpot(float x, float z, float r, out float g)
            {
                g = G(x, z);
                if (g < 1.2f || g > 60f) return false;
                if (excl.Blocked(x, z, 0.2f) || plantSpacing.Blocked(x, z)) return false;
                if (PropGround.Slope(x, z, 1f) > 1.1f) return false;
                if (!PalmGround(x, z, g)) return false;
                plantSpacing.Add(x, z, r);
                return true;
            }

            // forest floor around the trees
            for (int i = 0; i < treeSpots.Count; i++)
            {
                var t = treeSpots[i];
                int n = 1 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f), d = PropUtil.Range(rng, 2f, 5f);
                    float x = t.x + Mathf.Cos(a) * d, z = t.z + Mathf.Sin(a) * d;
                    if (!FreeSpot(x, z, 1.1f, out float g)) continue;
                    double r = rng.NextDouble();
                    int kind = r < 0.35 ? 2 : r < 0.6 ? 0 : r < 0.75 ? 1 : r < 0.92 ? 3 : 4;
                    if (g < 3f && kind == 4) kind = 0;
                    AddPlant(under, flowerBatch, rng, new Vector3(x, g, z), kind);
                }
                if (PropRun.Due()) yield return null;
            }

            // general scatter
            int plants = 0;
            attempts = 0;
            while (plants < 650 && attempts < 40000)
            {
                attempts++;
                if (PropRun.Due()) yield return null;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                if (!FreeSpot(x, z, 1.2f, out float g)) continue;
                float ga = WorldTerrainData.GrassAmount(x, z, g);
                bool sandy = ga < 0.5f;
                double r = rng.NextDouble();
                int kind;
                if (sandy) kind = r < 0.7 ? 0 : 1;
                else kind = r < 0.3 ? 0 : r < 0.42 ? 1 : r < 0.68 ? 2 : r < 0.9 ? 3 : 4;
                AddPlant(under, flowerBatch, rng, new Vector3(x, g, z), kind);
                plants++;
                if (PropRun.Due()) yield return null;
            }

            // meadow flowers (groups of 3–9) in the open meadows
            int fl = 0;
            attempts = 0;
            while (fl < 280 && attempts < 20000)
            {
                attempts++;
                if (PropRun.Due()) yield return null;
                float x = PropUtil.Range(rng, -ext, ext), z = PropUtil.Range(rng, -ext, ext);
                float g = G(x, z);
                if (g < 1.6f || g > 50f) continue;
                if (JungleAt(x, z, g) > 0.5f) continue;
                if (WorldTerrainData.GrassAmount(x, z, g) < 0.85f) continue;
                if (excl.Blocked(x, z, -0.3f) || plantSpacing.Blocked(x, z, -0.6f)) continue;
                AddPlant(under, flowerBatch, rng, new Vector3(x, g, z), 5);
                fl++;
                if (PropRun.Due()) yield return null;
            }

            // three height layers along every path: bush/fern at 1.5–3 m beside the trail, a tree within 10 m
            var samples = PropPaths.Samples;
            var trailTrees = new PropExclusion(16f);
            for (int i = 0; i < treeSpots.Count; i++) trailTrees.Add(treeSpots[i].x, treeSpots[i].z, 10f);
            for (int i = 0; i < samples.Count; i += 6)
            {
                var s = samples[i];
                if (s.halfWidth < 0.5f) continue;
                Vector3 side = Vector3.Cross(Vector3.up, s.dir).normalized * ((i / 6) % 2 == 0 ? 1f : -1f);
                float off = s.halfWidth + PropUtil.Range(rng, 1.3f, 2.6f);
                Vector3 p = s.pos + side * off + s.dir * PropUtil.Range(rng, -1.5f, 1.5f);
                if (FreeSpot(p.x, p.z, 1f, out float g))
                {
                    double r = rng.NextDouble();
                    int kind = r < 0.35 ? 0 : r < 0.55 ? 1 : r < 0.8 ? 2 : 3;
                    AddPlant(under, flowerBatch, rng, new Vector3(p.x, g, p.z), kind);
                }
                if ((i / 6) % 3 != 0) continue;
                if (trailTrees.Blocked(s.pos.x, s.pos.z)) continue;
                for (int tries = 0; tries < 6; tries++)
                {
                    Vector3 tp = s.pos + side * (s.halfWidth + PropUtil.Range(rng, 4.5f, 8f)) * (tries % 2 == 0 ? 1f : -1f) + s.dir * PropUtil.Range(rng, -3f, 3f);
                    float tg = G(tp.x, tp.z);
                    if (tg < 1.2f || excl.Blocked(tp.x, tp.z, 1.8f) || spacing.Blocked(tp.x, tp.z, 1f)) continue;
                    if (Meadow(tp.x, tp.z) > 0.3f) continue;
                    if (PropGround.Slope(tp.x, tp.z, 1.5f) > 0.9f) continue;
                    if (tg < 6f ? !PalmGround(tp.x, tp.z, tg) : !GrassGround(tp.x, tp.z, tg)) continue;
                    if (tg < 6f) AddPalm(full, lite, treeCols, rng, new Vector3(tp.x, tg, tp.z), Vector3.zero);
                    else AddTree(full, lite, treeCols, rng, new Vector3(tp.x, tg, tp.z), PropUtil.Range(rng, 0.85f, 1.1f));
                    spacing.Add(tp.x, tp.z, 3f);
                    trailTrees.Add(tp.x, tp.z, 10f);
                    break;
                }
                if (PropRun.Due()) yield return null;
            }

            Debug.Log("Sealebrities props: jungle with " + treeSpots.Count + " palms/trees/conifers, " + plants + " scattered plants, " + fl + " flower groups.");
            PropBatch.FlushLod(full, lite, root, "Trees", ShadowCastingMode.On, TreeNear, TreeFar, ShadowCastingMode.On);
            if (PropRun.Due()) yield return null;
            under.Flush(root, "Undergrowth", ShadowCastingMode.On, 110f);
            flowerBatch.Flush(root, "Flowers", ShadowCastingMode.Off, 75f);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- rocks
            yield return BuildRocks(root, excl, rng, ext, lh);

            // ---------------------------------------------------------------- grass clumps
            if (grass != null) yield return PlaceGrass(grass, excl, pathMask, ext);
        }

        static IEnumerator BuildRocks(Transform root, PropExclusion excl, System.Random rng, float ext, Vector2 lh)
        {
            var rockBatch = new PropBatch(64f);
            var rockCols = new GameObject("RockColliders").transform;
            rockCols.SetParent(root, false);
            // round 3 (GDD §19 "natürlicher", varied rocks): rocks lie in OUTCROPS – one big weathered boulder with 1–4
            // smaller ones and pebbles around it (inland: on slopes and hill shoulders, mossy; coast: dark beach rocks at
            // the waterline and on the points) – instead of single stones sprinkled evenly. Tall standing stones and flat
            // slabs vary the silhouettes.
            int inland = 0, coastal = 0, attempts = 0;
            const int InlandClusters = 17, CoastClusters = 16;
            while ((inland < InlandClusters || coastal < CoastClusters) && attempts < 30000)
            {
                attempts++;
                if (PropRun.Due()) yield return null;
                float x = PropUtil.Range(rng, -ext - 10f, ext + 10f), z = PropUtil.Range(rng, -ext - 10f, ext + 10f);
                float g = G(x, z);
                bool isCoast = g > -1.2f && g < 1.2f;
                bool isInland = g >= 3f && g < 70f;
                if (isCoast && coastal >= CoastClusters) continue;
                if (isInland && inland >= InlandClusters) continue;
                if (!isCoast && !isInland) continue;
                if (excl.Blocked(x, z, 3f)) continue;
                if (PropPaths.DistanceToPath(x, z) < 3.5f) continue;
                float slope = PropGround.Slope(x, z, 2f);
                // inland outcrops like slopes and shoulders (where rock would really break through), not flat meadows
                if (isInland && (slope < 0.18f || slope > 1.1f)) continue;
                int n = isCoast ? 2 + rng.Next(3) : 2 + rng.Next(4);
                float s0 = isCoast ? PropUtil.Range(rng, 1.1f, 2.5f) : PropUtil.Range(rng, 1.2f, 2.7f);
                Material main;
                Material top = null;
                if (isCoast) main = PropMats.BeachRock;
                else
                {
                    double r = rng.NextDouble();
                    main = r < 0.45 ? PropMats.Rock : r < 0.8 ? PropMats.RockMid : PropMats.RockDark;
                    if (rng.NextDouble() < 0.55) top = PropMats.Moss;
                }
                // downhill direction: the smaller rocks tumbled that way
                Vector3 nrm = PropGround.Normal(x, z, 2f);
                Vector2 down = new Vector2(nrm.x, nrm.z);
                down = down.sqrMagnitude > 1e-4f ? down.normalized : PropUtil.InDisc(rng, 1f).normalized;
                Vector2 at = new Vector2(x, z);
                float s = s0;
                for (int k = 0; k < n; k++)
                {
                    if (k > 0)
                    {
                        float dist = s * PropUtil.Range(rng, 1.0f, 1.6f) + 0.3f;
                        float ang = PropUtil.Range(rng, -1.2f, 1.2f);
                        Vector2 dir = new Vector2(down.x * Mathf.Cos(ang) - down.y * Mathf.Sin(ang), down.x * Mathf.Sin(ang) + down.y * Mathf.Cos(ang));
                        at += dir * dist;
                        s *= PropUtil.Range(rng, 0.45f, 0.75f);
                    }
                    float rg = G(at.x, at.y);
                    if (isCoast ? rg < -1.6f || rg > 2.5f : rg < 1.5f) break;
                    if (k > 0 && (excl.Blocked(at.x, at.y, s) || PropPaths.DistanceToPath(at.x, at.y) < 1.5f + s)) break;
                    // shape: mostly squat boulders, now and then a tall standing stone or a flat slab
                    double shape = rng.NextDouble();
                    Vector3 sc = shape < 0.15 && k == 0 && !isCoast
                        ? new Vector3(s * PropUtil.Range(rng, 0.6f, 0.8f), s * PropUtil.Range(rng, 1.3f, 1.7f), s * PropUtil.Range(rng, 0.55f, 0.75f))
                        : shape < 0.3
                            ? new Vector3(s * PropUtil.Range(rng, 1.3f, 1.7f), s * PropUtil.Range(rng, 0.35f, 0.5f), s * PropUtil.Range(rng, 1.0f, 1.3f))
                            : new Vector3(s * PropUtil.Range(rng, 0.9f, 1.4f), s * PropUtil.Range(rng, 0.6f, 0.95f), s * PropUtil.Range(rng, 0.8f, 1.2f));
                    Quaternion rot = Quaternion.Euler(PropUtil.Range(rng, -10f, 10f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -10f, 10f));
                    Vector3 p = new Vector3(at.x, rg - sc.y * 0.35f, at.y);
                    int seed = rng.Next();
                    int detail = s > 0.8f ? 1 : 0;
                    PropRocks.BuildSplit(rockBatch, p, p, sc, rot, seed, detail, 0.14f, main, top, PropMats.Crevice);
                    // round 3: every rock you would bump into is solid; pebbles stay passable
                    if (s > 0.45f) PropSolid.Rock(rockCols, p, sc, rot, seed, detail, 0.14f, isCoast, "Rock");
                    int pebbles = s > 1f ? 2 + rng.Next(3) : rng.Next(2);
                    for (int q = 0; q < pebbles; q++)
                    {
                        float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f), d = sc.x * PropUtil.Range(rng, 1.1f, 1.7f);
                        float px = at.x + Mathf.Cos(a) * d, pz = at.y + Mathf.Sin(a) * d;
                        float ps = PropUtil.Range(rng, 0.12f, 0.32f);
                        Vector3 pp = new Vector3(px, G(px, pz) - ps * 0.2f, pz);
                        PropRocks.Build(rockBatch.W(main, pp), pp, new Vector3(ps * 1.2f, ps * 0.7f, ps), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0);
                    }
                    excl.Add(at.x, at.y, s * 1.2f);
                }
                if (isCoast) coastal++; else inland++;
                if (PropRun.Due()) yield return null;
            }            // rocky outcrop around the lighthouse cliff
            for (int i = 0; i < 16; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float d = PropUtil.Range(rng, 11f, 28f);
                float x = lh.x + Mathf.Cos(a) * d, z = lh.y + Mathf.Sin(a) * d;
                if (excl.Blocked(x, z)) continue;
                float g = G(x, z);
                if (g < 0.5f) continue;
                float s = PropUtil.Range(rng, 0.8f, 2.4f);
                Vector3 sc = new Vector3(s * 1.2f, s * 0.7f, s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion rot = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                int seed = rng.Next();
                PropRocks.BuildSplit(rockBatch, p, p, sc, rot, seed, 1, 0.14f, PropMats.Cliff, PropMats.Moss, PropMats.Crevice);
                PropSolid.Rock(rockCols, p, sc, rot, seed, 1, 0.14f, false, "Rock");
                excl.Add(x, z, s);
            }
            rockBatch.Flush(root, "Rocks", ShadowCastingMode.On, 650f);
        }

        // ------------------------------------------------------------------ grass
        /// Dense grass clumps on every green area (meadow 0.6/m², jungle floor 0.35/m², forest edge 0.8/m²) and sparse
        /// dune grass on the sand (0.08/m²); none on the paths (their edges are broken by clumps) or under buildings.
        public static IEnumerator PlaceGrass(PropGrassField grass, PropExclusion excl, PropExclusion pathMask, float ext)
        {
            var rng = new System.Random(8080);
            const float step = 1f;
            int n = 0;
            for (float gx = -ext; gx <= ext; gx += step)
            {
                for (float gz = -ext; gz <= ext; gz += step)
                {
                    if (gx * gx + gz * gz > ext * ext) continue;
                    float x = gx + PropUtil.Range(rng, -0.45f, 0.45f);
                    float z = gz + PropUtil.Range(rng, -0.45f, 0.45f);
                    double roll = rng.NextDouble();
                    if (roll > 0.8) continue;   // no zone is denser than 0.8 / m²
                    float g = G(x, z);
                    if (g < 0.9f || g > 80f) continue;
                    float ga = WorldTerrainData.GrassAmount(x, z, g);
                    bool dune = ga < 0.2f;
                    if (!dune && ga < 0.75f) continue;
                    if (WorldTerrainData.IsBareSoil(x, z)) continue;
                    float density;
                    int variant;
                    float scale;
                    if (dune)
                    {
                        density = g < 4f ? 0.08f : 0f;
                        variant = PropGrassField.DuneFirst + rng.Next(PropGrassField.DuneCount);
                        scale = PropUtil.Range(rng, 0.8f, 1.15f);
                    }
                    else
                    {
                        float j = JungleAt(x, z, g);
                        if (j > 0.62f) { density = 0.35f; variant = PropGrassField.JungleFirst + rng.Next(PropGrassField.JungleCount); }
                        else if (j > 0.45f) { density = 0.8f; variant = rng.NextDouble() < 0.6 ? PropGrassField.JungleFirst + rng.Next(PropGrassField.JungleCount) : rng.Next(PropGrassField.MeadowCount); }
                        else { density = 0.6f; variant = rng.Next(PropGrassField.MeadowCount); }
                        scale = PropUtil.Range(rng, 0.85f, 1.2f);
                    }
                    if (roll > density) continue;
                    if (excl.Blocked(x, z, -0.5f) || pathMask.Blocked(x, z)) continue;
                    if (PropGround.Slope(x, z, 0.8f) > 1.2f) continue;
                    Vector3 nrm = PropGround.Normal(x, z, 0.6f);
                    grass.Add(variant, new Vector3(x, g - 0.02f, z), PropUtil.Range(rng, 0f, 360f), scale, new Vector3(nrm.x, 0f, nrm.z) * 0.6f);
                    n++;
                }
                if (PropRun.Due()) yield return null;
            }
            if (n == 0) Debug.Log("Sealebrities props: no grassy ground found for grass clumps");
            else Debug.Log("Sealebrities props: " + n + " grass clumps (GPU instanced).");
        }
    }
}
