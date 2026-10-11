using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Points of interest on the main island (GDD 15.33, art bible §1.11): the waterfall with its pool and a secret
    /// alcove behind the falling water, the cave in the lighthouse hill with a hidden grotto (Onkel Gus' old film
    /// corner) and the rope bridge over the ravine. Every builder adapts to the terrain it finds (it samples the
    /// ground; when the world did not carve a cliff / ravine it builds a plausible one from props), always with fixed
    /// seeds so all co-op machines build the same thing.
    public static class PropPOI
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-8f ? v.normalized : Vector3.forward; }

        /// Big rock with a mesh collider, moss on top, crevices underneath.
        public static void BigRock(PropBatch batch, Transform cols, Vector3 pos, Vector3 scale, Quaternion rot, int seed, Material main, Material top)
        {
            PropRocks.BuildSplit(batch, pos, pos, scale, rot, seed, 1, 0.16f, main, top, PropMats.Crevice);
            PropCol.Mesh(cols, PropRocks.ColliderMesh(scale, seed, 1, 0.16f), pos, rot, Vector3.one, "Rock");
        }

        // ================================================================== waterfall
        /// True when the world terrain has the waterfall feature (plateau, cliff, amphitheater, plunge pool basin).
        static bool WorldWaterfall()
        {
            Vector3 d = WorldFeatures.FallDir, side = Vector3.Cross(Vector3.up, d);
            float plateau = G(WorldFeatures.FallLip - d * 5f + side * 3f);
            float floor = G(WorldFeatures.FallFoot + d * 3f + side * 4f);
            float pool = G(WorldFeatures.PoolCenter);
            return Mathf.Abs(plateau - WorldFeatures.FallTopY) < 1.6f && Mathf.Abs(floor - WorldFeatures.FallFootY) < 1.6f && pool < WorldFeatures.PoolWaterY - 0.2f;
        }

        public static IEnumerator Waterfall(Transform island, PropExclusion excl, PropExclusion skip)
        {
            if (WorldWaterfall())
            {
                yield return WaterfallOnTerrain(island, excl, skip);
                yield break;
            }
            yield return WaterfallFallback(island, excl, skip);
        }

        /// The waterfall on the world's cliff (WorldFeatures): creek water on the plateau, a faceted curtain that lands
        /// ~1.4 m in front of the cliff foot (a dry gap behind it hides Gus' stash), the creek down to the plunge pool,
        /// the pool surface, foam, spray, boulders and reeds.
        static IEnumerator WaterfallOnTerrain(Transform island, PropExclusion excl, PropExclusion skip)
        {
            var root = PropUtil.WorldRoot("Waterfall", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(6061);
            Vector3 d = WorldFeatures.FallDir;
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            Vector3 lip = WorldFeatures.FallLip;
            Vector3 top = WorldLayout.WaterfallTop;
            Vector3 P = WorldFeatures.PoolCenter;
            float pr = WorldFeatures.PoolRadius;

            // creek water on the plateau (source → lip), following the carved notch
            var cr = WorldFeatures.Creek;
            var creekW = batch.W(PropMats.PoolWater, lip);
            Vector3 prev = cr[0];
            for (int k = 0; k < 2; k++)
            {
                Vector3 a = cr[k], b = cr[k + 1];
                int n = Mathf.Max(2, Mathf.CeilToInt((b - a).magnitude / 1.5f));
                for (int i = 1; i <= n; i++)
                {
                    Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                    p.y = (k == 1 && i == n) ? lip.y + 0.02f : G(p) + 0.3f;
                    if (i == 1 && k == 0) prev.y = G(prev) + 0.3f;
                    creekW.QuadAuto(prev - side * 1.1f, prev + side * 1.1f, p + side * 1.1f, p - side * 1.1f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                    prev = p;
                }
            }
            // the curtain: from the lip to the landing 3.9 m out, bulging forward
            Vector3 land = lip + d * 3.9f;
            land.y = WorldFeatures.FallFootY - 0.42f;
            Ribbon(batch, lip + Vector3.up * 0.03f, land, side, 2.3f, d, rng);
            Foam(batch, land + d * 0.3f, 5, 0.6f, rng);
            PropFX.Spray(root, land + Vector3.up * 0.2f, 2.4f);
            if (PropRun.Due()) yield return null;

            // creek from the landing to the pool (meandering centre line, like the carved channel)
            float poolS = Vector2.Distance(new Vector2(P.x, P.z), new Vector2(top.x, top.z)) - pr + 0.8f;
            Vector3 Center(float s)
            {
                float q = 0.9f * Mathf.Sin(s * 0.21f) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((s - 2f) / 4f));
                Vector3 c = new Vector3(top.x, 0f, top.z) + d * s + side * (-q);
                c.y = G(c) + 0.3f;
                return c;
            }
            Vector3 cp = Center(4.0f);
            for (float s = 5.2f; s <= poolS; s += 1.2f)
            {
                Vector3 cn = Center(s);
                if (s + 1.2f > poolS) cn.y = Mathf.Max(cn.y, WorldFeatures.PoolWaterY + 0.01f);
                creekW.QuadAuto(cp - side * 1.0f, cp + side * 1.0f, cn + side * 1.0f, cn - side * 1.0f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                cp = cn;
            }

            // plunge pool surface (the world carved the basin)
            var water = batch.W(PropMats.PoolWater, P);
            const int seg = 16;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                float r0 = pr * (0.93f + 0.06f * PropNoise.Hash01(i, 5, 61)), r1 = pr * (0.93f + 0.06f * PropNoise.Hash01((i + 1) % seg, 5, 61));
                Vector3 e0 = new Vector3(P.x + Mathf.Cos(a0) * r0, P.y, P.z + Mathf.Sin(a0) * r0);
                Vector3 e1 = new Vector3(P.x + Mathf.Cos(a1) * r1, P.y, P.z + Mathf.Sin(a1) * r1);
                water.Face3(P, e0, e1, Vector3.up);
                if (i % 2 == 0 && Vector3.Dot(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), -d) < 0.7f && Vector3.Dot(new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d) < 0.8f)
                {
                    Vector3 rp = new Vector3(P.x + Mathf.Cos(a0) * (pr + 0.6f), 0f, P.z + Mathf.Sin(a0) * (pr + 0.6f));
                    float s = PropUtil.Range(rng, 0.45f, 1.0f);
                    rp.y = G(rp) + s * 0.15f;
                    PropRocks.BuildSplit(batch, rp, rp, new Vector3(s * 1.3f, s * 0.6f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0, 0.14f, PropMats.RockMid, PropMats.Moss, null);
                }
            }
            // reeds at the pool edge
            for (int i = 0; i < 10; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                if (Vector3.Dot(dir, -d) > 0.5f) continue;
                Vector3 rp = P + dir * (pr - 0.4f);
                rp.y = G(rp) - 0.05f;
                var reed = batch.W(PropMats.Flat(PropPal.GrassMid), rp);
                for (int k = 0; k < 5; k++)
                {
                    Vector3 o = new Vector3(PropUtil.Range(rng, -0.2f, 0.2f), 0f, PropUtil.Range(rng, -0.2f, 0.2f));
                    Vector3 tip = rp + o * 1.6f + Vector3.up * PropUtil.Range(rng, 0.9f, 1.5f);
                    Vector3 sw = new Vector3(0.03f, 0f, 0.02f);
                    reed.Tri2(rp + o - sw, rp + o + sw, tip, Vector3.Cross(sw, tip - rp).normalized);
                }
            }
            // boulders flanking the falls (on the plateau edge and at the cliff foot)
            for (int sgn = -1; sgn <= 1; sgn += 2)
            {
                Vector3 bt = lip + side * (sgn * PropUtil.Range(rng, 3.2f, 4.2f)) - d * 1.2f;
                bt.y = G(bt) - 0.3f;
                BigRock(batch, cols, bt, new Vector3(1.4f, 1.0f, 1.2f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), PropMats.Cliff, PropMats.Moss);
                Vector3 bf = lip + d * 3.2f + side * (sgn * PropUtil.Range(rng, 3.4f, 4.4f));
                bf.y = G(bf) - 0.2f;
                BigRock(batch, cols, bf, new Vector3(1.2f, 0.9f, 1.0f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), PropMats.RockMid, PropMats.Moss);
            }

            // Gus' stash in the dry gap behind the curtain
            Vector3 niche = lip + d * 2.95f + side * 0.55f;
            niche.y = G(niche) + 0.02f;
            PropInfo.WaterfallNiche = niche;
            PropInfo.WaterfallLip = lip;
            PropInfo.PoolCenter = P;

            excl.Add(P.x, P.z, pr + 1.8f);
            excl.AddLine(lip - d * 14f, P, 2.6f);
            excl.Add(lip.x + d.x * 2.5f, lip.z + d.z * 2.5f, 5f);
            skip.Add(P.x, P.z, pr - 0.2f);
            skip.AddLine(lip - d * 14f, lip + d * (poolS - 1f), 1.4f);
            batch.Flush(root, "WaterfallMesh", ShadowCastingMode.On, 600f);
        }

        /// Fallback when the terrain has no waterfall feature: builds a rock cliff, ledge, curtain and pool itself.
        static IEnumerator WaterfallFallback(Transform island, PropExclusion excl, PropExclusion skip)
        {
            var root = PropUtil.WorldRoot("Waterfall", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(6060);

            Vector3 T = WorldLayout.WaterfallTop, P0 = WorldLayout.WaterfallPool;
            Vector3 d = Flat(P0 - T);                // flow direction: from the cliff toward the pool
            Vector3 side = Vector3.Cross(Vector3.up, d).normalized;
            float gP0 = G(P0);

            // cliff line: just before the ground toward the top starts to rise (default 7.5 m from the pool spot)
            float total = Vector2.Distance(new Vector2(P0.x, P0.z), new Vector2(T.x, T.z));
            float cd = 7.5f;
            for (float s = 4f; s < Mathf.Min(total, 14f); s += 0.5f)
            {
                Vector3 q = P0 - d * s;
                if (G(q) > gP0 + 1.0f) { cd = Mathf.Max(2.5f, s - 1.5f); break; }
            }
            Vector3 C = P0 - d * cd;                  // foot of the cliff face

            // the pool hugs the foot of the cliff; a basin carved by the world keeps its level, otherwise the water
            // sits just above the highest ground under it (no terrain poking through) with a rock skirt below the rim
            float pr = 6.2f;
            Vector3 P = C + d * (pr * 0.85f);
            float gP = G(P), gMax = gP, gMin = gP, ring = float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                gMax = gP; gMin = gP; ring = float.MaxValue;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI / 6f;
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    ring = Mathf.Min(ring, G(P + dir * (pr + 0.3f)));
                    float h1 = G(P + dir * (pr * 0.45f)), h2 = G(P + dir * (pr * 0.85f));
                    gMax = Mathf.Max(gMax, Mathf.Max(h1, h2));
                    gMin = Mathf.Min(gMin, Mathf.Min(h1, h2));
                }
                if (pass == 0 && ring <= gP + 0.8f && gMax - gMin > 1.2f) { pr = 4.4f; P = C + d * (pr * 0.85f); gP = G(P); }
                else break;
            }
            bool basin = ring > gP + 0.8f;
            float wl = basin ? Mathf.Min(ring - 0.3f, gP + 1.5f) : gMax + 0.08f;
            wl = Mathf.Max(wl, 0.5f);
            float natural = G(C - d * 2.5f);
            float top = Mathf.Clamp(natural, wl + 7f, wl + 12f);
            float shelfY = wl + 2.8f;                 // roof of the secret alcove = ledge between the two falls

            // ---- cliff from big rocks (left and right of the falls, a back wall behind the alcove, the crown on top)
            Material[] cliffMats = { PropMats.Cliff, PropMats.Rock, PropMats.RockMid };
            for (int s = -1; s <= 1; s += 2)
            {
                for (int k = 0; k < 3; k++)
                {
                    float off = 3.4f + k * 2.6f + PropUtil.Range(rng, -0.3f, 0.3f);
                    float h = Mathf.Lerp(top - wl, (top - wl) * 0.55f, k / 2.5f);
                    Vector3 c = C + side * (s * off) - d * PropUtil.Range(rng, 1.2f, 2.2f);
                    Vector3 sc = new Vector3(PropUtil.Range(rng, 1.7f, 2.4f), h * 0.6f, PropUtil.Range(rng, 2.0f, 2.8f));
                    c.y = wl - 0.8f + sc.y * 0.8f;
                    BigRock(batch, cols, c, sc, Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), cliffMats[rng.Next(3)], PropMats.Moss);
                }
            }
            // back wall of the alcove (behind the lower fall) and the rock body above it
            Vector3 back = C - d * 2.8f;
            BigRock(batch, cols, new Vector3(back.x, wl + 1.2f, back.z), new Vector3(2.2f, 2.2f, 1.4f), Quaternion.LookRotation(d), rng.Next(), PropMats.RockMid, null);
            Vector3 crown = C - d * 2.8f;
            float crownH = Mathf.Max(0.8f, (top - shelfY) * 0.55f);
            BigRock(batch, cols, new Vector3(crown.x, shelfY + crownH * 0.9f, crown.z), new Vector3(2.6f, crownH, 2.2f), Quaternion.LookRotation(d), rng.Next(), PropMats.Cliff, PropMats.Moss);
            // the ledge: roof of the alcove, the upper fall lands on it
            Quaternion lr = Quaternion.LookRotation(d);
            Vector3 shelfC = C + d * 0.3f + Vector3.up * (shelfY + 0.3f);
            PropRocks.BuildSplit(batch, shelfC, shelfC, new Vector3(1.9f, 0.4f, 1.6f), lr, rng.Next(), 1, 0.08f, PropMats.RockMid, PropMats.Moss, PropMats.Crevice);
            PropCol.Box(cols, shelfC, new Vector3(3.4f, 0.6f, 2.6f), lr, "Ledge");
            // alcove floor (dry rock step above the pool)
            Vector3 floorC = C - d * 0.5f + Vector3.up * (wl + 0.05f);
            batch.W(PropMats.RockMid, floorC).Box(floorC - Vector3.up * 0.15f, new Vector3(2.6f, 0.3f, 2.0f), lr);
            PropCol.Box(cols, floorC - Vector3.up * 0.15f, new Vector3(2.6f, 0.3f, 2.0f), lr, "AlcoveFloor");
            if (PropRun.Due()) yield return null;

            // ---- water ribbons (stepped, faceted), foam at each step, spray
            Vector3 lip = C - d * 0.5f + Vector3.up * top;
            Vector3 ledgeTop = C + d * 0.4f + Vector3.up * (shelfY + 0.72f);
            Ribbon(batch, lip, ledgeTop + Vector3.up * 0.05f, side, 2.4f, d, rng);
            Vector3 lowerLip = C + d * 1.65f + Vector3.up * (shelfY + 0.66f);
            Vector3 impact = C + d * 1.9f + Vector3.up * wl;
            Ribbon(batch, lowerLip, impact, side, 3.0f, d, rng);
            // stream on top feeding the falls
            Vector3 up0 = lip - d * 3.5f + Vector3.up * 0.15f;
            var fw = batch.W(PropMats.FallWater, lip);
            fw.Quad2(up0 - side * 1.0f, up0 + side * 1.0f, lip + side * 1.2f + Vector3.up * 0.05f, lip - side * 1.2f + Vector3.up * 0.05f, Vector3.up);
            Foam(batch, ledgeTop + d * 0.6f, 3, 0.5f, rng);
            Foam(batch, impact + d * 0.4f, 5, 0.75f, rng);
            PropFX.Spray(root, impact + Vector3.up * 0.2f, 2.6f);
            PropInfo.WaterfallLip = lip;
            PropInfo.WaterfallNiche = floorC - d * 0.1f + Vector3.up * 0.05f;

            // ---- pool: water disc, sandy floor, rock skirt below the rim, rim of rocks, a few reeds
            PropInfo.PoolCenter = new Vector3(P.x, wl, P.z);
            var water = batch.W(PropMats.PoolWater, P);
            const int seg = 14;
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                float r0 = pr * (0.88f + 0.12f * PropNoise.Hash01(i, 3, 61)), r1 = pr * (0.88f + 0.12f * PropNoise.Hash01((i + 1) % seg, 3, 61));
                Vector3 e0 = new Vector3(P.x + Mathf.Cos(a0) * r0, wl, P.z + Mathf.Sin(a0) * r0);
                Vector3 e1 = new Vector3(P.x + Mathf.Cos(a1) * r1, wl, P.z + Mathf.Sin(a1) * r1);
                water.Face3(new Vector3(P.x, wl, P.z), e0, e1, Vector3.up);
                batch.W(PropMats.Flat(PropPal.Seabed), P).Face3(new Vector3(P.x, wl - 0.45f, P.z), e0 - Vector3.up * 0.3f, e1 - Vector3.up * 0.3f, Vector3.up);
                // skirt: where the ground falls away below the water line the pool shows a rock rim, not a floating disc
                float b0 = Mathf.Min(G(e0), wl) - 0.4f, b1 = Mathf.Min(G(e1), wl) - 0.4f;
                Vector3 outw = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                batch.W(PropMats.RockMid, P).QuadAuto(e0 + outw * 0.05f + Vector3.up * 0.06f, e1 + outw * 0.05f + Vector3.up * 0.06f,
                    new Vector3(e1.x, b1, e1.z) + outw * 0.35f, new Vector3(e0.x, b0, e0.z) + outw * 0.35f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outw);
                // rim rocks
                if (i % 2 == 0)
                {
                    Vector3 rp = new Vector3(P.x + Mathf.Cos(a0) * (pr + 0.3f), 0f, P.z + Mathf.Sin(a0) * (pr + 0.3f));
                    if (Vector3.Dot(Flat(rp - P), -d) > 0.75f) continue;   // open toward the falls
                    float s = PropUtil.Range(rng, 0.5f, 1.1f);
                    rp.y = Mathf.Min(G(rp), wl) + s * 0.2f;
                    PropRocks.BuildSplit(batch, rp, rp, new Vector3(s * 1.3f, s * 0.6f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0, 0.14f, PropMats.RockMid, PropMats.Moss, null);
                }
            }
            for (int i = 0; i < 9; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 rp = new Vector3(P.x + Mathf.Cos(a) * (pr - 0.6f), wl - 0.3f, P.z + Mathf.Sin(a) * (pr - 0.6f));
                if (Vector3.Dot(Flat(rp - P), -d) > 0.6f) continue;
                var reed = batch.W(PropMats.Flat(PropPal.GrassMid), rp);
                for (int k = 0; k < 5; k++)
                {
                    Vector3 o = new Vector3(PropUtil.Range(rng, -0.2f, 0.2f), 0f, PropUtil.Range(rng, -0.2f, 0.2f));
                    Vector3 tip = rp + o * 1.6f + Vector3.up * PropUtil.Range(rng, 0.9f, 1.5f);
                    Vector3 sw = new Vector3(0.03f, 0f, 0.02f);
                    reed.Tri2(rp + o - sw, rp + o + sw, tip, Vector3.Cross(sw, tip - rp).normalized);
                }
            }

            excl.Add(P.x, P.z, pr + 1.2f);
            excl.AddLine(C - side * 9f, C + side * 9f, 3.5f);
            excl.Add(crown.x, crown.z, 4f);
            skip.Add(P.x, P.z, pr - 0.3f);
            skip.Add(C.x, C.z, 3f);
            batch.Flush(root, "WaterfallMesh", ShadowCastingMode.On, 600f);
        }

        /// Falling water: a 3 x 4 grid of faceted quads from a to b, bulging forward, width w.
        static void Ribbon(PropBatch batch, Vector3 a, Vector3 b, Vector3 side, float w, Vector3 fwd, System.Random rng)
        {
            var bw = batch.W(PropMats.FallWater, a);
            const int cols = 3, rows = 4;
            var grid = new Vector3[(cols + 1) * (rows + 1)];
            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows;
                Vector3 c = Vector3.Lerp(a, b, t) + fwd * (Mathf.Sin(t * Mathf.PI) * 0.35f);
                float ww = w * Mathf.Lerp(0.85f, 1.15f, t);
                for (int k = 0; k <= cols; k++)
                {
                    float u = k / (float)cols - 0.5f;
                    Vector3 j = (r == 0 || r == rows) ? Vector3.zero : new Vector3(PropUtil.Range(rng, -0.08f, 0.08f), PropUtil.Range(rng, -0.08f, 0.08f), PropUtil.Range(rng, -0.08f, 0.08f));
                    grid[r * (cols + 1) + k] = c + side * (u * ww) + j;
                }
            }
            for (int r = 0; r < rows; r++)
                for (int k = 0; k < cols; k++)
                {
                    int i0 = r * (cols + 1) + k;
                    bw.Quad2(grid[i0], grid[i0 + 1], grid[i0 + cols + 2], grid[i0 + cols + 1], fwd);
                }
        }

        static void Foam(PropBatch batch, Vector3 c, int n, float size, System.Random rng)
        {
            var fb = batch.W(PropMats.FallFoam, c);
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, size * 1.6f);
                float s = size * PropUtil.Range(rng, 0.6f, 1.1f);
                var radii = PropRocks.Jitter(rng, 0, 0.2f, 1, 0.3f);
                fb.Ico(c + new Vector3(o.x, 0.05f, o.y), new Vector3(s, s * 0.35f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), 0, radii);
            }
        }

        // ================================================================== cave + hidden grotto
        // ring path (local: +z into the cave, +x right): x, z, half width, height
        static readonly Vector4[] CaveRings =
        {
            new Vector4(0f, -0.6f, 1.9f, 2.9f), new Vector4(0f, 1.4f, 2.3f, 3.3f), new Vector4(0f, 4.0f, 3.2f, 4.0f),
            new Vector4(0.3f, 6.6f, 3.0f, 3.8f), new Vector4(1.2f, 8.6f, 1.8f, 2.8f), new Vector4(2.6f, 9.7f, 0.85f, 2.35f),
            new Vector4(4.3f, 10.1f, 0.8f, 2.35f), new Vector4(6.0f, 10.5f, 1.7f, 2.6f), new Vector4(7.7f, 10.9f, 2.6f, 2.9f),
            new Vector4(9.3f, 11.1f, 2.3f, 2.6f),
        };

        /// True when the world carved the cave passage into the hill (WorldFeatures: floor, walls, chamber).
        static bool WorldCave()
        {
            Vector3 m = WorldFeatures.CaveMouth, dir = WorldFeatures.CaveDir;
            Vector3 lat = Vector3.Cross(Vector3.up, dir).normalized;
            float f = G(m + dir * 6f);
            float wall = Mathf.Min(G(m + dir * 6f + lat * 7f), G(m + dir * 6f - lat * 7f));
            return Mathf.Abs(f - (WorldFeatures.CaveFloorY + 0.12f)) < 1.0f && wall > WorldFeatures.CaveFloorY + 5f;
        }

        public static IEnumerator Cave(Transform island, PropExclusion excl, PropExclusion skip)
        {
            if (WorldCave())
            {
                yield return CaveOnTerrain(island, excl, skip);
                yield break;
            }
            yield return CaveFallback(island, excl, skip);
        }

        /// The cave on the world's carved passage: a faceted rock roof ("plug") over the passage and the round chamber,
        /// filled up to the hill surface; a boulder pile near the passage end hides the squeeze into the chamber, which
        /// holds Onkel Gus' old film corner (the secret). Glowing mushrooms and a lantern light the dark.
        static IEnumerator CaveOnTerrain(Transform island, PropExclusion excl, PropExclusion skip)
        {
            var root = PropUtil.WorldRoot("Cave", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(7172);
            Vector3 dir = WorldFeatures.CaveDir;
            Quaternion R = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 m0 = new Vector3(WorldFeatures.CaveMouth.x, WorldFeatures.CaveFloorY, WorldFeatures.CaveMouth.z);
            var M = Matrix4x4.TRS(m0, R, Vector3.one);
            Vector3 W(Vector3 local) => M.MultiplyPoint3x4(local);
            float chamberS = WorldFeatures.CaveLength + WorldFeatures.CaveChamberRadius - 1.5f;

            // ---- roof plug on a grid in the cave's local frame (+z into the hill, +x across)
            const float step = 1.2f;
            const float x0 = -8.4f, z0 = 0.4f;
            const int nx = 14, nz = 22;
            bool Inside(float x, float z)
            {
                if (z < z0 - 0.01f) return false;
                if (Mathf.Abs(x) <= 5.2f && z <= WorldFeatures.CaveLength + 2.5f) return true;
                float dz = z - chamberS;
                return x * x + dz * dz <= 7.7f * 7.7f;
            }
            float Ceil(float x, float z)
            {
                float dz = z - chamberS;
                float dc2 = x * x + dz * dz;
                float y = 0.02f * z + 4.3f + 0.45f * Mathf.Clamp01(1f - (x * x) / 27f);
                if (dc2 < 7.7f * 7.7f) y = Mathf.Max(y, 0.02f * z + 4.4f + 1.4f * (1f - dc2 / (7.7f * 7.7f)));
                return y + 0.45f * (PropNoise.Value(x * 0.7f + 3f, z * 0.7f, 811) - 0.5f);
            }
            // top surface: level with the hill just outside the carve (lateral samples along the passage, radial ones
            // around the chamber), never thinner than 2.2 m
            float Top(float x, float z, float ceil)
            {
                float hs;
                float dz = z - chamberS;
                if (z < WorldFeatures.CaveLength + 1f && x * x + dz * dz > 6.5f * 6.5f)
                {
                    float hl = G(W(new Vector3(-6.4f, 0f, z))), hr = G(W(new Vector3(6.4f, 0f, z)));
                    hs = Mathf.Lerp(hl, hr, Mathf.Clamp01((x + 6.4f) / 12.8f)) - WorldFeatures.CaveFloorY;
                }
                else
                {
                    Vector2 rd = new Vector2(x, dz);
                    rd = rd.sqrMagnitude > 0.01f ? rd.normalized : Vector2.up;
                    hs = G(W(new Vector3(rd.x * 8.8f, 0f, chamberS + rd.y * 8.8f))) - WorldFeatures.CaveFloorY;
                }
                hs = Mathf.Max(hs - 0.15f, ceil + 2.2f);
                return hs + 0.3f * (PropNoise.Value(x * 0.5f, z * 0.5f + 7f, 812) - 0.5f);
            }

            var ceilB = new PropMeshBuilder { M = M };
            var topB = new PropMeshBuilder { M = M };
            var faceB = new PropMeshBuilder { M = M };
            var cv = new Vector3[nx + 1, nz + 1];
            var tv = new Vector3[nx + 1, nz + 1];
            var inside = new bool[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float x = x0 + i * step, z = z0 + j * step;
                    inside[i, j] = Inside(x, z);
                    float c = Ceil(x, z);
                    cv[i, j] = new Vector3(x, c, z);
                    tv[i, j] = new Vector3(x, Top(x, z, c), z);
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!inside[i, j] || !inside[i + 1, j] || !inside[i, j + 1] || !inside[i + 1, j + 1]) continue;
                    ceilB.QuadAuto(cv[i, j], cv[i + 1, j], cv[i + 1, j + 1], cv[i, j + 1], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.down);
                    topB.QuadAuto(tv[i, j], tv[i + 1, j], tv[i + 1, j + 1], tv[i, j + 1], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                }
            // lintel over the mouth (front face of the plug)
            for (int i = 0; i < nx; i++)
            {
                if (!inside[i, 0] || !inside[i + 1, 0]) continue;
                faceB.QuadAuto(cv[i, 0], cv[i + 1, 0], tv[i + 1, 0], tv[i, 0], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            }
            batch.B(PropMats.CaveInside).Append(ceilB, Matrix4x4.identity);
            PropRocks.Route(topB, Matrix4x4.identity, batch, m0, PropMats.Cliff, PropMats.Moss, PropMats.Crevice, 0.8f, -0.5f);
            PropRocks.Route(faceB, Matrix4x4.identity, batch, m0, PropMats.Cliff, PropMats.Moss, PropMats.Crevice, 0.8f, -0.5f);
            var colB = new PropMeshBuilder();
            colB.Append(ceilB, Matrix4x4.identity);
            colB.Append(topB, Matrix4x4.identity);
            colB.Append(faceB, Matrix4x4.identity);
            colB.DoubleSide(0, 0);
            PropCol.Mesh(cols, colB.ToMesh("CaveRoofCol", false), Vector3.zero, Quaternion.identity, Vector3.one, "CaveRoof");
            if (PropRun.Due()) yield return null;

            // rugged rocks framing the mouth and on top of the plug
            for (int s = -1; s <= 1; s += 2)
                BigRock(batch, cols, W(new Vector3(s * 4.4f, 1.6f, -0.4f)), new Vector3(1.4f, 2.0f, 1.3f), R, rng.Next(), PropMats.Cliff, PropMats.Moss);
            BigRock(batch, cols, W(new Vector3(0.3f, 5.3f, -0.2f)), new Vector3(3.2f, 0.9f, 1.4f), R, rng.Next(), PropMats.Cliff, PropMats.Moss);

            // ---- boulder pile hiding the squeeze into the chamber (the way around is on the right)
            float pz = WorldFeatures.CaveLength - 0.6f;
            BigRock(batch, cols, W(new Vector3(-1.7f, 1.2f, pz)), new Vector3(1.7f, 1.6f, 1.3f), R * Quaternion.Euler(0f, 25f, 0f), rng.Next(), PropMats.CaveFloor, null);
            BigRock(batch, cols, W(new Vector3(0.55f, 1.15f, pz + 0.7f)), new Vector3(1.25f, 1.45f, 1.15f), R * Quaternion.Euler(0f, -30f, 0f), rng.Next(), PropMats.CaveFloor, null);
            BigRock(batch, cols, W(new Vector3(-0.4f, 2.9f, pz + 0.3f)), new Vector3(1.1f, 0.8f, 1.0f), R, rng.Next(), PropMats.CaveFloor, null);

            // ---- glowing mushrooms + light
            for (int i = 0; i < 16; i++)
            {
                float z = PropUtil.Range(rng, 2f, WorldFeatures.CaveLength + 8f);
                float xx = (rng.NextDouble() < 0.5 ? -1f : 1f) * PropUtil.Range(rng, 2.0f, 2.6f);
                Vector3 lp = W(new Vector3(xx, 0f, z));
                lp.y = G(lp) + 0.02f;
                Mushrooms(batch, lp, rng);
            }
            PropNearLight.Attach(PropUtil.AddPointLight(root, W(new Vector3(0f, 2.4f, 6f)), new Color(0.6f, 1f, 0.55f), 10f, 1.0f, "CaveGlow"), 45f);

            // ---- L.6 #1: a rock partition across the back of the chamber with a 0.95 × 1.25 m squeeze behind a curtain
            // of roots; Onkel Gus' film corner sits in the dark side grotto behind it (no light – bring the lamp)
            Vector3 g0 = new Vector3(0f, 0f, chamberS);
            float Floor(Vector3 local) => G(W(local)) - WorldFeatures.CaveFloorY;
            const float partZ = 1.8f, gapX0 = 2.2f, gapX1 = 3.15f, gapH = 1.25f;
            {
                float partZw = chamberS + partZ;
                float fy = Floor(new Vector3((gapX0 + gapX1) * 0.5f, 0f, partZw));
                // exact invisible walls (left of the gap, right of the gap, the lintel above it)
                PropCol.Box(cols, W(new Vector3((-8.4f + gapX0) * 0.5f, fy + 3.5f, partZw)), new Vector3(gapX0 + 8.4f, 9f, 0.9f), R, "Partition");
                PropCol.Box(cols, W(new Vector3((gapX1 + 8.4f) * 0.5f, fy + 3.5f, partZw)), new Vector3(8.4f - gapX1, 9f, 0.9f), R, "Partition");
                PropCol.Box(cols, W(new Vector3((gapX0 + gapX1) * 0.5f, fy + gapH + 4f, partZw)), new Vector3(gapX1 - gapX0 + 0.1f, 8f, 0.9f), R, "PartitionLintel");
                // visual rocks up to the ceiling (built without colliders; the boxes above are exact)
                for (float x = gapX0 - 0.95f; x > -8f; x -= 1.8f)
                    PropRocks.BuildSplit(batch, W(new Vector3(x, fy + 2.8f, partZw)), W(new Vector3(x, fy + 2.8f, partZw)), new Vector3(0.9f, 3.2f, 0.6f), R * Quaternion.Euler(0f, x * 13f, 0f), 7300 + (int)(x * 10f), 1, 0.08f, PropMats.CaveFloor, null, PropMats.Crevice);
                for (float x = gapX1 + 0.95f; x < 8f; x += 1.8f)
                    PropRocks.BuildSplit(batch, W(new Vector3(x, fy + 2.8f, partZw)), W(new Vector3(x, fy + 2.8f, partZw)), new Vector3(0.9f, 3.2f, 0.6f), R * Quaternion.Euler(0f, x * 17f, 0f), 7400 + (int)(x * 10f), 1, 0.08f, PropMats.CaveFloor, null, PropMats.Crevice);
                Vector3 lint = W(new Vector3((gapX0 + gapX1) * 0.5f, fy + gapH + 2.2f, partZw));
                PropRocks.BuildSplit(batch, lint, lint, new Vector3(0.75f, 2.0f, 0.6f), R, 7500, 1, 0.06f, PropMats.CaveFloor, null, PropMats.Crevice);
                // the root curtain hanging in front of the squeeze (no collider – the player pushes through)
                var roots = batch.W(PropMats.Vine, W(g0));
                for (int i = 0; i < 16; i++)
                {
                    float x = gapX0 - 0.5f + i * 0.13f;
                    Vector3 top = W(new Vector3(x, fy + 4.6f, partZw - 0.65f));
                    Vector3 mid = W(new Vector3(x + PropUtil.Range(rng, -0.12f, 0.12f), fy + 2.2f, partZw - 0.6f - PropUtil.Range(rng, 0f, 0.15f)));
                    Vector3 low = W(new Vector3(x + PropUtil.Range(rng, -0.15f, 0.15f), fy + PropUtil.Range(rng, 0.25f, 0.6f), partZw - 0.55f));
                    roots.Rod(top, mid, 0.03f, 0.025f, 3);
                    roots.Rod(mid, low, 0.025f, 0.012f, 3);
                }
                PropInfo.SetSpot("grotto_squeeze", W(new Vector3((gapX0 + gapX1) * 0.5f, fy, partZw)));
                PropInfo.SetSpot("root_curtain", W(new Vector3((gapX0 + gapX1) * 0.5f, fy, partZw - 0.7f)));
            }
            Vector3 pp = g0 + new Vector3(0f, 0f, 5.6f);
            pp.y = Floor(pp) + 1.2f;
            Quaternion pq = R * Quaternion.LookRotation(Vector3.back);
            var pb = batch.W(PropMats.WoodDark, W(pp));
            pb.Box(W(pp), new Vector3(1.0f, 1.3f, 0.06f), pq);
            Vector3 pc = W(pp) + pq * new Vector3(0f, 0f, 0.035f);
            Vector3 pu = pq * Vector3.up * 0.6f, px = pq * Vector3.right * 0.44f;
            batch.W(PropMats.GusPoster, pc).QuadAuto(pc - px - pu, pc - px + pu, pc + px + pu, pc + px - pu,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), pq * Vector3.forward);
            Vector3 camL = g0 + new Vector3(0.6f, 0f, 3.6f);
            camL.y = Floor(camL);
            FilmCamera(batch, cols, W(camL), R);
            Vector3 chairL = g0 + new Vector3(-1.5f, 0f, 3.2f);
            chairL.y = Floor(chairL);
            DirectorChair(batch, cols, W(chairL), R);
            Vector3 tableL = g0 + new Vector3(2.6f, 0f, 4.4f);
            tableL.y = Floor(tableL);
            Vector3 tW = W(tableL);
            var tw = batch.W(PropMats.Wood2, tW);
            tw.Box(tW + Vector3.up * 0.72f, new Vector3(0.9f, 0.06f, 0.6f), R);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    tw.Box(tW + R * new Vector3(sx * 0.38f, 0.36f, sz * 0.24f), new Vector3(0.06f, 0.72f, 0.06f), R);
            PropCol.Box(cols, tW + Vector3.up * 0.38f, new Vector3(0.9f, 0.76f, 0.6f), R, "Table");
            for (int i = 0; i < 4; i++)
                batch.W(PropMats.Flat(PropPal.HullBlue), tW).Cylinder(tW + R * new Vector3(-0.2f + (i % 2) * 0.05f, 0.75f + (i / 2) * 0.05f + (i % 2) * 0.05f, -0.05f), Quaternion.identity, 0.17f, 0.17f, 0.045f, 10, true, true);
            // the old lantern is out (L.6: darkness – the lamp is needed)
            Vector3 lantern = tW + R * new Vector3(0.25f, 0.75f, 0.1f);
            batch.W(PropMats.DarkMetal, lantern).Cylinder(lantern, Quaternion.identity, 0.09f, 0.09f, 0.03f, 6, true, true);
            batch.W(PropMats.Flat(LowPoly.Hex("#5E5A48")), lantern).Cylinder(lantern + Vector3.up * 0.03f, Quaternion.identity, 0.07f, 0.07f, 0.18f, 6, true, true);
            batch.W(PropMats.DarkMetal, lantern).Cylinder(lantern + Vector3.up * 0.21f, Quaternion.identity, 0.1f, 0f, 0.08f, 6, true, false);

            PropInfo.Grotto = W(g0 + new Vector3(0f, 0f, 3.6f));
            PropInfo.GrottoTable = tW + Vector3.up * 0.78f;
            PropInfo.GrottoCamera = W(camL);
            PropInfo.CaveMouth = W(new Vector3(0f, 0f, -1.5f));
            Vector3 cham = W(g0 + new Vector3(-1.6f, 0f, -1.8f));
            cham.y = G(cham) + 0.03f;
            PropInfo.CaveChamber = cham;
            Vector3 shard = W(g0 + new Vector3(-2.4f, 0f, 4.8f));
            shard.y = G(shard) + 0.05f;
            PropSpots.Register("m3_shard2", shard);
            PropInfo.SetSpot("lens_grotto", shard);

            // keep plants out of the passage, the chamber and the clearing in front of the mouth
            for (float z = -2f; z <= WorldFeatures.CaveLength + 2f; z += 2f)
            {
                Vector3 c = W(new Vector3(0f, 0f, z));
                excl.Add(c.x, c.z, 6.5f);
                skip.Add(c.x, c.z, 5f);
            }
            Vector3 ch = W(g0);
            excl.Add(ch.x, ch.z, 9.5f);
            skip.Add(ch.x, ch.z, 8f);
            Vector3 front = W(new Vector3(0f, 0f, -5f));
            excl.Add(front.x, front.z, 4f);
            batch.Flush(root, "CaveMesh", ShadowCastingMode.On, 500f);
        }

        /// Fallback when the terrain has no cave passage: a free-standing rock mound with the cave inside.
        static IEnumerator CaveFallback(Transform island, PropExclusion excl, PropExclusion skip)
        {
            var root = PropUtil.WorldRoot("Cave", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(7171);
            Vector3 E = WorldLayout.CaveEntrance;

            // ---- choose a spot and an orientation: flat footprint, pointing into the hill
            Vector3 n0 = PropGround.Normal(E.x, E.z, 4f);
            Vector3 uphill = Flat(new Vector3(-n0.x, 0f, -n0.z));
            float best = float.MaxValue, bestYaw = 0f, bestFloor = G(E), bestMin = G(E);
            Vector3 bestPos = E;
            for (int oi = 0; oi < 9; oi++)
            {
                Vector3 o = oi == 0 ? Vector3.zero : new Vector3(Mathf.Cos(oi * 0.785f), 0f, Mathf.Sin(oi * 0.785f)) * 7f;
                Vector3 m = E + o;
                for (int yi = 0; yi < 12; yi++)
                {
                    float yaw = yi * 30f;
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                    float hmax = float.MinValue, hmin = float.MaxValue;
                    for (int k = 0; k < CaveRings.Length; k++)
                    {
                        var r = CaveRings[k];
                        for (int s = -1; s <= 1; s++)
                        {
                            Vector3 p = m + q * new Vector3(r.x + s * r.z, 0f, r.y);
                            float h = G(p);
                            hmax = Mathf.Max(hmax, h);
                            hmin = Mathf.Min(hmin, h);
                        }
                    }
                    float mouth = G(m);
                    if (mouth < 0.8f) continue;
                    float cost = (hmax - hmin) + 0.35f * (hmax - mouth) - 1.2f * Vector3.Dot(q * Vector3.forward, uphill) + o.magnitude * 0.05f;
                    if (cost < best) { best = cost; bestYaw = yaw; bestPos = m; bestFloor = hmax + 0.05f; bestMin = hmin; }
                }
            }
            Quaternion R = Quaternion.Euler(0f, bestYaw, 0f);
            float floorY = bestFloor;
            float skirt = Mathf.Max(2.5f, floorY - bestMin + 0.8f);
            var M = Matrix4x4.TRS(new Vector3(bestPos.x, floorY, bestPos.z), R, Vector3.one);
            Vector3 W(Vector3 local) => M.MultiplyPoint3x4(local);

            // ---- rings
            int nr = CaveRings.Length;
            const int A = 8;
            var inner = new Vector3[nr, A + 1];
            var outer = new Vector3[nr, A + 3];
            var centers = new Vector3[nr];
            var lats = new Vector3[nr];
            float minGround = float.MaxValue;
            for (int k = 0; k < nr; k++)
            {
                var r = CaveRings[k];
                Vector3 p = new Vector3(r.x, 0f, r.y);
                Vector3 prev = k > 0 ? new Vector3(CaveRings[k - 1].x, 0f, CaveRings[k - 1].y) : p - Vector3.forward;
                Vector3 next = k < nr - 1 ? new Vector3(CaveRings[k + 1].x, 0f, CaveRings[k + 1].y) : p + (p - prev);
                Vector3 t = (next - prev).normalized;
                Vector3 lat = Vector3.Cross(Vector3.up, t).normalized;
                centers[k] = p;
                lats[k] = lat;
                float thick = k == 0 ? 1.4f : 1.8f;
                for (int j = 0; j <= A; j++)
                {
                    float th = Mathf.PI * j / A;
                    Vector3 v = p + lat * (Mathf.Cos(th) * r.z) + Vector3.up * (Mathf.Sin(th) * r.w);
                    if (j > 0 && j < A && k > 0)
                    {
                        float nz = PropNoise.Fbm3(W(v) * 0.45f, 2, 919) - 0.5f;
                        Vector3 inward = (p + Vector3.up * (r.w * 0.4f) - v).normalized;
                        v += inward * (nz * 0.55f * Mathf.Min(1f, r.z / 2f));   // calmer walls in the squeeze
                    }
                    inner[k, j] = v;
                    float tt = thick + 0.9f * PropNoise.Value(k * 1.7f, j * 1.3f, 77);
                    Vector3 o = p + lat * (Mathf.Cos(th) * (r.z + tt)) + Vector3.up * (Mathf.Sin(th) * (r.w + tt) + Mathf.Sin(th) * 0.9f);
                    outer[k, j + 1] = o;
                }
                // skirts below the floor at both ends of the outer arch
                outer[k, 0] = p + lat * (r.z + thick + 0.6f) - Vector3.up * skirt;
                outer[k, A + 2] = p - lat * (r.z + thick + 0.6f) - Vector3.up * skirt;
                for (int j = 0; j <= A + 2; j++) minGround = Mathf.Min(minGround, G(W(outer[k, j])));
            }

            var innerB = new PropMeshBuilder { M = M };
            var floorB = new PropMeshBuilder { M = M };
            var outerB = new PropMeshBuilder { M = M };
            for (int k = 0; k < nr - 1; k++)
            {
                Vector3 axis0 = centers[k] + Vector3.up * (CaveRings[k].w * 0.4f), axis1 = centers[k + 1] + Vector3.up * (CaveRings[k + 1].w * 0.4f);
                for (int j = 0; j < A; j++)
                {
                    Vector3 a = inner[k, j], b = inner[k, j + 1], c = inner[k + 1, j + 1], dd = inner[k + 1, j];
                    Vector3 mid = (a + b + c + dd) * 0.25f;
                    Vector3 ax = (axis0 + axis1) * 0.5f;
                    innerB.QuadAuto(a, b, c, dd, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, ax - mid);
                }
                for (int j = 0; j < A + 2; j++)
                {
                    Vector3 a = outer[k, j], b = outer[k, j + 1], c = outer[k + 1, j + 1], dd = outer[k + 1, j];
                    Vector3 mid = (a + b + c + dd) * 0.25f;
                    outerB.QuadAuto(a, b, c, dd, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, mid - (centers[k] + centers[k + 1]) * 0.5f);
                }
                // floor strip
                floorB.QuadAuto(inner[k, 0], inner[k, A], inner[k + 1, A], inner[k + 1, 0], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
            }
            // mouth rim (inner ring 0 → outer ring 0)
            for (int j = 0; j < A; j++)
                outerB.QuadAuto(inner[0, j], inner[0, j + 1], outer[0, j + 2], outer[0, j + 1], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            outerB.QuadAuto(inner[0, 0], outer[0, 1], outer[0, 0], inner[0, 0] - Vector3.up * skirt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            outerB.QuadAuto(inner[0, A], outer[0, A + 1], outer[0, A + 2], inner[0, A] - Vector3.up * skirt, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.back);
            // end caps
            {
                int k = nr - 1;
                Vector3 t = (centers[k] - centers[k - 1]).normalized;
                Vector3 ci = centers[k] + Vector3.up * (CaveRings[k].w * 0.45f) + t * 0.4f;
                for (int j = 0; j < A; j++) innerB.Face3(inner[k, j], inner[k, j + 1], ci, -t);
                innerB.Face3(inner[k, 0], inner[k, A], ci, -t);
                Vector3 co = centers[k] + Vector3.up * (CaveRings[k].w * 0.6f) + t * 1.8f;
                for (int j = 0; j < A + 2; j++) outerB.Face3(outer[k, j], outer[k, j + 1], co, t);
                outerB.Face3(outer[k, 0], outer[k, A + 2], co, t);
            }
            // floor apron out of the mouth (ramp down to the outside ground, never steeper than ~25°)
            Vector3 mouthL = inner[0, A], mouthR = inner[0, 0];
            float outsideY = Mathf.Min(0f, G(W(new Vector3(0f, 0f, -2.6f))) - floorY);
            float apronLen = Mathf.Max(2.2f, -outsideY * 2.2f);
            outsideY = Mathf.Min(0f, G(W(new Vector3(0f, 0f, -0.6f - apronLen))) - floorY) - 0.05f;
            Vector3 apL = mouthL + new Vector3(-0.3f, outsideY, -apronLen), apR = mouthR + new Vector3(0.3f, outsideY, -apronLen);
            floorB.QuadAuto(apL, apR, mouthR, mouthL, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
            // the apron's sides
            floorB.QuadAuto(apR, apR + Vector3.down * 1.5f, mouthR + Vector3.down * 1.5f, mouthR, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.right);
            floorB.QuadAuto(apL, apL + Vector3.down * 1.5f, mouthL + Vector3.down * 1.5f, mouthL, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.left);

            // rendering: dark violet inside, rock + moss outside
            batch.B(PropMats.CaveInside).Append(innerB, Matrix4x4.identity);
            batch.B(PropMats.CaveFloor).Append(floorB, Matrix4x4.identity);
            PropRocks.Route(outerB, Matrix4x4.identity, batch, W(Vector3.zero), PropMats.Cliff, PropMats.Moss, PropMats.Crevice, 0.62f, -0.5f);

            // colliders: inner shell (both sides) + floor + outer shell
            var colB = new PropMeshBuilder();
            colB.Append(innerB, Matrix4x4.identity);
            colB.Append(floorB, Matrix4x4.identity);
            colB.DoubleSide(0, 0);
            PropCol.Mesh(cols, colB.ToMesh("CaveInnerCol", false), Vector3.zero, Quaternion.identity, Vector3.one, "CaveInner");
            PropCol.Mesh(cols, outerB.ToMesh("CaveOuterCol", false), Vector3.zero, Quaternion.identity, Vector3.one, "CaveOuter");
            if (PropRun.Due()) yield return null;

            // ---- mound dressing: chunky rocks on top, a few at the mouth
            for (int i = 0; i < 7; i++)
            {
                int k = 1 + rng.Next(nr - 2);
                var r = CaveRings[k];
                Vector3 lp = centers[k] + lats[k] * PropUtil.Range(rng, -r.z, r.z) + Vector3.up * (r.w + 2.0f);
                float s = PropUtil.Range(rng, 1.4f, 2.4f);
                PropRocks.BuildSplit(batch, W(lp), W(lp), new Vector3(s * 1.3f, s * 0.7f, s), R * Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 1, 0.16f, PropMats.Cliff, PropMats.Moss, PropMats.Crevice);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 lp = new Vector3(s * 3.4f, 0.4f, -1.2f);
                BigRock(batch, cols, W(lp), new Vector3(1.3f, 1.4f, 1.1f), R, rng.Next(), PropMats.RockMid, PropMats.Moss);
            }
            // boulder hiding the squeeze to the grotto (the way around it is on the right)
            Vector3 boulder = new Vector3(0.55f, 0.9f, 8.25f);
            BigRock(batch, cols, W(boulder), new Vector3(1.35f, 1.25f, 1.1f), R * Quaternion.Euler(0f, 40f, 0f), rng.Next(), PropMats.CaveFloor, null);

            // ---- glowing mushrooms + light
            for (int i = 0; i < 14; i++)
            {
                int k = 1 + rng.Next(nr - 1);
                var r = CaveRings[k];
                float s = rng.NextDouble() < 0.5 ? 1f : -1f;
                Vector3 lp = centers[k] + lats[k] * (s * (r.z - 0.35f)) + Vector3.up * 0.02f;
                Mushrooms(batch, W(lp), rng);
            }
            var glow = PropUtil.AddPointLight(root, W(new Vector3(0.2f, 2.2f, 5.0f)), new Color(0.6f, 1f, 0.55f), 9f, 0.9f, "CaveGlow");
            PropNearLight.Attach(glow, 40f);

            // ---- Onkel Gus' film corner in the grotto (the secret)
            Vector3 g0 = new Vector3(CaveRings[8].x, 0f, CaveRings[8].y);
            Vector3 gBack = Flat(new Vector3(CaveRings[9].x - CaveRings[7].x, 0f, CaveRings[9].y - CaveRings[7].y));
            Vector3 gSide = Vector3.Cross(Vector3.up, gBack).normalized;
            // poster on a board at the back
            Vector3 pp = g0 + gBack * 1.4f + Vector3.up * 1.2f;
            Quaternion pq = R * Quaternion.LookRotation(-gBack);
            var pb = batch.W(PropMats.WoodDark, W(pp));
            pb.Box(W(pp), new Vector3(1.0f, 1.3f, 0.06f), pq);
            Vector3 pc = W(pp) + pq * new Vector3(0f, 0f, 0.035f);
            Vector3 pu = pq * Vector3.up * 0.6f, px = pq * Vector3.right * 0.44f;
            batch.W(PropMats.GusPoster, pc).QuadAuto(pc - px - pu, pc - px + pu, pc + px + pu, pc + px - pu,
                new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), pq * Vector3.forward);
            // film camera on a tripod pointing at the poster
            Vector3 camPos = g0 - gBack * 0.6f + gSide * 0.5f;
            Quaternion camRot = R * Quaternion.LookRotation(gBack);
            FilmCamera(batch, cols, W(camPos), camRot);
            // director's chair, table with film cans and a lantern
            Vector3 chair = g0 - gBack * 1.1f - gSide * 0.9f;
            DirectorChair(batch, cols, W(chair), R * Quaternion.LookRotation(gBack));
            Vector3 table = g0 + gSide * 1.5f + gBack * 0.3f;
            Vector3 tW = W(table);
            var tw = batch.W(PropMats.Wood2, tW);
            tw.Box(tW + Vector3.up * 0.72f, new Vector3(0.9f, 0.06f, 0.6f), R);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    tw.Box(tW + R * new Vector3(sx * 0.38f, 0.36f, sz * 0.24f), new Vector3(0.06f, 0.72f, 0.06f), R);
            PropCol.Box(cols, tW + Vector3.up * 0.38f, new Vector3(0.9f, 0.76f, 0.6f), R, "Table");
            for (int i = 0; i < 4; i++)
                batch.W(PropMats.Flat(PropPal.HullBlue), tW).Cylinder(tW + R * new Vector3(-0.2f + (i % 2) * 0.05f, 0.75f + (i / 2) * 0.05f + (i % 2) * 0.05f, -0.05f), Quaternion.identity, 0.17f, 0.17f, 0.045f, 10, true, true);
            Vector3 lantern = tW + R * new Vector3(0.25f, 0.75f, 0.1f);
            batch.W(PropMats.DarkMetal, lantern).Cylinder(lantern, Quaternion.identity, 0.09f, 0.09f, 0.03f, 6, true, true);
            batch.W(PropMats.LanternGlow, lantern).Cylinder(lantern + Vector3.up * 0.03f, Quaternion.identity, 0.07f, 0.07f, 0.18f, 6, true, true);
            batch.W(PropMats.DarkMetal, lantern).Cylinder(lantern + Vector3.up * 0.21f, Quaternion.identity, 0.1f, 0f, 0.08f, 6, true, false);
            PropNearLight.Attach(PropUtil.AddPointLight(root, lantern + Vector3.up * 0.25f, new Color(1f, 0.78f, 0.5f), 7f, 1.6f, "GrottoLantern"), 35f);
            PropInfo.Grotto = W(g0) + Vector3.up * 0.05f;
            PropInfo.GrottoTable = tW + Vector3.up * 0.78f;
            PropInfo.GrottoCamera = W(camPos);
            PropInfo.CaveMouth = W(new Vector3(0f, 0f, -1.5f));
            PropInfo.CaveChamber = W(new Vector3(0.6f, 0.05f, 4.5f));

            // footprint
            for (int k = 0; k < nr; k++)
            {
                Vector3 c = W(centers[k]);
                excl.Add(c.x, c.z, CaveRings[k].z + 3.2f);
                skip.Add(c.x, c.z, CaveRings[k].z + 2.2f);
            }
            Vector3 mouthW = W(new Vector3(0f, 0f, -2f));
            excl.Add(mouthW.x, mouthW.z, 2.5f);
            PropPaths.AddConnection(new Vector2(WorldLayout.CaveEntrance.x + 6f, WorldLayout.CaveEntrance.z + 1f), new Vector2(W(new Vector3(0f, 0f, -3.5f)).x, W(new Vector3(0f, 0f, -3.5f)).z));

            batch.Flush(root, "CaveMesh", ShadowCastingMode.On, 500f);
        }

        static void Mushrooms(PropBatch batch, Vector3 p, System.Random rng)
        {
            int n = 2 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.3f);
                Vector3 b = p + new Vector3(o.x, 0f, o.y);
                float h = PropUtil.Range(rng, 0.08f, 0.25f);
                batch.W(PropMats.Paper, b).Cylinder(b, Quaternion.identity, 0.02f, 0.018f, h, 5);
                float cr = PropUtil.Range(rng, 0.05f, 0.12f);
                batch.W(PropMats.MushroomGlow, b).Cylinder(b + Vector3.up * h, Quaternion.identity, cr, 0.01f, cr * 0.8f, 6, true, false);
            }
        }

        /// Old film camera on a wooden tripod (two reels on top, lens forward).
        public static void FilmCamera(PropBatch batch, Transform cols, Vector3 p, Quaternion r)
        {
            var w = batch.W(PropMats.WoodDark, p);
            Vector3 head = p + Vector3.up * 1.25f;
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                Vector3 foot = p + r * new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f);
                w.Rod(foot, head, 0.025f, 0.02f, 4);
            }
            var body = batch.W(PropMats.Flat(PropPal.Ink), p);
            body.Box(head + r * new Vector3(0f, 0.16f, 0f), new Vector3(0.24f, 0.28f, 0.4f), r);
            body.Cylinder(head + r * new Vector3(0f, 0.16f, 0.2f), r * Quaternion.Euler(90f, 0f, 0f), 0.08f, 0.09f, 0.2f, 8, true, true);
            batch.W(PropMats.Glass, p).Cylinder(head + r * new Vector3(0f, 0.16f, 0.4f), r * Quaternion.Euler(90f, 0f, 0f), 0.07f, 0.07f, 0.01f, 8, true, true);
            var reel = batch.W(PropMats.Metal, p);
            reel.Cylinder(head + r * new Vector3(-0.04f, 0.42f, 0.1f), r * Quaternion.Euler(0f, 0f, 90f), 0.14f, 0.14f, 0.05f, 10, true, true);
            reel.Cylinder(head + r * new Vector3(-0.04f, 0.42f, -0.18f), r * Quaternion.Euler(0f, 0f, 90f), 0.12f, 0.12f, 0.05f, 10, true, true);
            PropCol.Capsule(cols, p + Vector3.up * 0.75f, 0.3f, 1.5f, Quaternion.identity, "FilmCamera");
        }

        static void DirectorChair(PropBatch batch, Transform cols, Vector3 p, Quaternion r)
        {
            var w = batch.W(PropMats.WoodDark, p);
            for (int s = -1; s <= 1; s += 2)
            {
                w.Beam(p + r * new Vector3(s * 0.25f, 0f, -0.2f), p + r * new Vector3(s * 0.25f, 0.45f, 0.2f), 0.04f, 0.04f);
                w.Beam(p + r * new Vector3(s * 0.25f, 0f, 0.2f), p + r * new Vector3(s * 0.25f, 0.45f, -0.2f), 0.04f, 0.04f);
                w.Beam(p + r * new Vector3(s * 0.25f, 0.45f, 0.22f), p + r * new Vector3(s * 0.25f, 0.95f, 0.26f), 0.04f, 0.04f);
            }
            var cv = batch.W(PropMats.Flat(PropPal.LighthouseRed), p);
            cv.Box(p + r * new Vector3(0f, 0.46f, 0f), new Vector3(0.5f, 0.03f, 0.42f), r);
            cv.Box(p + r * new Vector3(0f, 0.82f, 0.25f), new Vector3(0.5f, 0.22f, 0.03f), r);
            PropCol.Box(cols, p + r * new Vector3(0f, 0.45f, 0f), new Vector3(0.6f, 0.9f, 0.55f), r, "Chair");
        }

        // ================================================================== ravine + rope bridge
        public static IEnumerator Bridge(Transform island, PropExclusion excl, PropExclusion skip)
        {
            var root = PropUtil.WorldRoot("RopeBridge", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(9191);
            Vector3 Rv = WorldLayout.Ravine;

            // find the crossing with the deepest dip between two rims
            float bestDip = -1f, bestA = 90f, bestSmin = 0f, bestL = 0f, bestR = 0f;
            const float span = 24f;
            for (int ai = 0; ai < 12; ai++)
            {
                float a = ai * 15f * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float hmin = float.MaxValue, smin = 0f;
                for (float s = -6f; s <= 6f; s += 0.5f)
                {
                    float h = G(Rv + dir * s);
                    if (h < hmin) { hmin = h; smin = s; }
                }
                float lmax = float.MinValue, rmax = float.MinValue;
                for (float s = smin - span; s < smin - 1f; s += 1f) lmax = Mathf.Max(lmax, G(Rv + dir * s));
                for (float s = smin + 1f; s <= smin + span; s += 1f) rmax = Mathf.Max(rmax, G(Rv + dir * s));
                float dip = Mathf.Min(lmax, rmax) - hmin;
                if (dip > bestDip) { bestDip = dip; bestA = a; bestSmin = smin; bestL = lmax; bestR = rmax; }
            }
            Vector3 axis = new Vector3(Mathf.Cos(bestA), 0f, Mathf.Sin(bestA));
            Vector3 lat = Vector3.Cross(Vector3.up, axis).normalized;
            Vector3 mid = Rv + axis * bestSmin;
            float deckY = Mathf.Min(bestL, bestR) + 0.12f;
            float s0 = bestSmin, s1 = bestSmin;
            for (float s = bestSmin; s > bestSmin - span; s -= 0.5f) { s0 = s; if (G(Rv + axis * s) >= deckY - 0.25f) break; }
            for (float s = bestSmin; s < bestSmin + span; s += 0.5f) { s1 = s; if (G(Rv + axis * s) >= deckY - 0.25f) break; }
            float L = s1 - s0 + 1.2f;
            bool real = bestDip > 1.8f && L > 4f && L < 44f;
            Vector3 A, B;
            // the world's ravine has exact anchor pads on both rims: use them when the terrain matches
            Vector3 bn = WorldFeatures.BridgeNorth, bs = WorldFeatures.BridgeSouth;
            bool worldRavine = Mathf.Abs(G(bn) - WorldFeatures.RavineRimY) < 1.2f && Mathf.Abs(G(bs) - WorldFeatures.RavineRimY) < 1.2f
                               && G((bn + bs) * 0.5f) < WorldFeatures.RavineRimY - 3f;
            if (worldRavine)
            {
                axis = bs - bn;
                axis.y = 0f;
                axis.Normalize();
                lat = Vector3.Cross(Vector3.up, axis).normalized;
                deckY = WorldFeatures.RavineRimY + 0.12f;
                A = bn - axis * 0.9f;
                B = bs + axis * 0.9f;
                A.y = deckY; B.y = deckY;
                mid = (bn + bs) * 0.5f;
                L = (B - A).magnitude;
                real = true;
            }
            else if (real)
            {
                A = Rv + axis * (s0 - 0.6f);
                B = Rv + axis * (s1 + 0.6f);
                A.y = deckY; B.y = deckY;
            }
            else
            {
                // fallback: footbridge over a small creek bed
                L = 7f;
                deckY = G(Rv) + 0.55f;
                A = Rv - axis * 3.5f; B = Rv + axis * 3.5f;
                A.y = deckY; B.y = deckY;
                CreekBed(batch, Rv, lat, axis, rng);
            }
            float sag = real ? Mathf.Min(1.0f, 0.04f * L) : 0.12f;
            Vector3 Deck(float t) => Vector3.Lerp(A, B, t) - Vector3.up * (sag * (1f - (2f * t - 1f) * (2f * t - 1f)));

            // planks
            Material[] planks = { PropMats.BridgePlank, PropMats.Flat(PropMats.Value(PropPal.BridgePlank, 0.92f)), PropMats.Flat(PropMats.Value(PropPal.BridgePlank, 1.07f)) };
            int n = Mathf.Max(6, Mathf.RoundToInt(L / 0.32f));
            Quaternion baseRot = Quaternion.LookRotation(axis);
            for (int i = 0; i < n; i++)
            {
                float t = (i + 0.5f) / n;
                Vector3 p = Deck(t);
                Vector3 tan = (Deck(Mathf.Min(1f, t + 0.01f)) - Deck(Mathf.Max(0f, t - 0.01f))).normalized;
                Quaternion r = Quaternion.LookRotation(tan, Vector3.up) * Quaternion.Euler(PropUtil.Range(rng, -2f, 2f), PropUtil.Range(rng, -3f, 3f), PropUtil.Range(rng, -2f, 2f));
                if (real && rng.NextDouble() < 0.04 && i > 2 && i < n - 3) continue;   // a missing plank here and there
                batch.W(planks[rng.Next(planks.Length)], p).Box(p - Vector3.up * 0.035f, new Vector3(1.3f, 0.07f, 0.26f), r);
            }
            // ropes: foot ropes, handrails, hangers
            var rope = batch.W(PropMats.Rope, mid);
            const int rs = 16;
            for (int sd = -1; sd <= 1; sd += 2)
            {
                Vector3 prevF = Deck(0f) + lat * (sd * 0.68f) - Vector3.up * 0.05f;
                Vector3 prevH = Deck(0f) + lat * (sd * 0.72f) + Vector3.up * 1.0f;
                for (int k = 1; k <= rs; k++)
                {
                    float t = k / (float)rs;
                    Vector3 f = Deck(t) + lat * (sd * 0.68f) - Vector3.up * 0.05f;
                    Vector3 h = Deck(t) + lat * (sd * 0.72f) + Vector3.up * (1.0f + 0.12f * (1f - (2f * t - 1f) * (2f * t - 1f)));
                    rope.Rod(prevF, f, 0.025f, 0.025f, 4);
                    rope.Rod(prevH, h, 0.03f, 0.03f, 4);
                    if (k < rs && k % 2 == 0) rope.Rod(h, f, 0.012f, 0.012f, 3);
                    prevF = f;
                    prevH = h;
                }
            }
            // end posts with anchor ropes
            var post = batch.W(PropMats.WoodDark, mid);
            foreach (var endT in new[] { 0f, 1f })
            {
                Vector3 e = Deck(endT);
                Vector3 outward = endT < 0.5f ? -axis : axis;
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    Vector3 pb = e + lat * (sd * 0.8f) + outward * 0.3f;
                    float g = G(pb);
                    pb.y = Mathf.Min(g, e.y) - 0.5f;
                    float top = e.y + 1.35f;
                    post.Cylinder(pb, Quaternion.identity, 0.12f, 0.1f, top - pb.y, 6, false, true);
                    PropCol.Capsule(cols, new Vector3(pb.x, (pb.y + top) * 0.5f, pb.z), 0.13f, top - pb.y, Quaternion.identity, "BridgePost");
                    Vector3 stake = e + lat * (sd * 1.0f) + outward * 2.4f;
                    stake.y = G(stake);
                    post.Cylinder(stake - Vector3.up * 0.3f, Quaternion.Euler(0f, 0f, sd * 12f), 0.06f, 0.05f, 0.7f, 5, false, true);
                    rope.Rod(new Vector3(pb.x, top - 0.1f, pb.z), stake + Vector3.up * 0.3f, 0.025f, 0.025f, 4);
                }
            }
            // colliders: deck segments + invisible side rails
            const int cs = 10;
            for (int k = 0; k < cs; k++)
            {
                Vector3 a = Deck(k / (float)cs), b = Deck((k + 1) / (float)cs);
                Vector3 seg = b - a;
                Quaternion r = Quaternion.LookRotation(seg.normalized, Vector3.up);
                Vector3 c = (a + b) * 0.5f;
                PropCol.Box(cols, c - Vector3.up * 0.08f, new Vector3(1.4f, 0.16f, seg.magnitude + 0.08f), r, "BridgeDeck");
                PropCol.Box(cols, c + lat * 0.76f + Vector3.up * 0.55f, new Vector3(0.08f, 1.1f, seg.magnitude + 0.05f), r, "BridgeRail");
                PropCol.Box(cols, c - lat * 0.76f + Vector3.up * 0.55f, new Vector3(0.08f, 1.1f, seg.magnitude + 0.05f), r, "BridgeRail");
            }
            PropInfo.BridgeNorth = A;
            PropInfo.BridgeSouth = B;
            PropInfo.BridgeMid = Deck(0.5f);
            PropInfo.BridgeBelow = new Vector3(mid.x, G(mid), mid.z);
            skip.AddLine(A, B, 1.6f);
            excl.AddLine(A - axis * 1.5f, B + axis * 1.5f, 2.6f);

            // connect the trails to the bridge ends (the routes stop ±11 m from the ravine point)
            Vector2 n2 = new Vector2(Rv.x, Rv.z + 11f), s2 = new Vector2(Rv.x, Rv.z - 11f);
            Vector2 a2 = new Vector2(A.x, A.z) - new Vector2(axis.x, axis.z) * 1.2f, b2 = new Vector2(B.x, B.z) + new Vector2(axis.x, axis.z) * 1.2f;
            if ((a2 - n2).sqrMagnitude + (b2 - s2).sqrMagnitude > (a2 - s2).sqrMagnitude + (b2 - n2).sqrMagnitude) { var t = a2; a2 = b2; b2 = t; }
            PropPaths.AddConnection(n2, a2);
            PropPaths.AddConnection(s2, b2);

            batch.Flush(root, "BridgeMesh", ShadowCastingMode.On, 400f);
            yield break;
        }

        static void CreekBed(PropBatch batch, Vector3 c, Vector3 lat, Vector3 axis, System.Random rng)
        {
            var water = batch.W(PropMats.PoolWater, c);
            Vector3 prev = c - lat * 9f;
            for (int i = 1; i <= 9; i++)
            {
                Vector3 p = c + lat * (-9f + i * 2f) + axis * PropUtil.Range(rng, -0.4f, 0.4f);
                float y0 = G(prev) + 0.08f, y1 = G(p) + 0.08f;
                Vector3 a0 = new Vector3(prev.x, y0, prev.z), a1 = new Vector3(p.x, y1, p.z);
                water.QuadAuto(a0 - axis * 0.7f, a0 + axis * 0.7f, a1 + axis * 0.7f, a1 - axis * 0.7f, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                float s = PropUtil.Range(rng, 0.2f, 0.45f);
                Vector3 rp = a1 + axis * (rng.NextDouble() < 0.5 ? 0.9f : -0.9f);
                PropRocks.Build(batch.W(PropMats.RockMid, rp), rp, new Vector3(s * 1.3f, s * 0.6f, s), Quaternion.identity, rng.Next(), 0);
                prev = p;
            }
        }
    }
}
