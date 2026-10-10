using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Low-poly rocks: jittered icospheres with a few flat cut planes (chunky facets), flattened at the bottom.
    public static class PropRocks
    {
        static readonly float[] Radii = new float[200];
        static readonly Vector3[] Pn = new Vector3[8];
        static readonly float[] Po = new float[8];

        /// Fills the shared radius table for an icosphere of the given detail (random jitter, cut planes, flat bottom).
        /// The returned array is reused: pass it to PropMeshBuilder.Ico immediately.
        public static float[] Jitter(System.Random rng, int detail, float rough, int cuts, float flatBottom = 0.6f)
        {
            var dirs = PropIco.Dirs(detail);
            cuts = Mathf.Clamp(cuts, 0, Pn.Length);
            for (int i = 0; i < cuts; i++)
            {
                Vector3 n = new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, -0.25f, 1f), PropUtil.Range(rng, -1f, 1f));
                Pn[i] = n.sqrMagnitude > 1e-4f ? n.normalized : Vector3.up;
                Po[i] = PropUtil.Range(rng, 0.7f, 0.9f);
            }
            for (int v = 0; v < dirs.Length; v++)
            {
                Vector3 d = dirs[v];
                float r = 1f + ((float)rng.NextDouble() * 2f - 1f) * rough;
                for (int i = 0; i < cuts; i++)
                {
                    float dd = Vector3.Dot(d, Pn[i]);
                    if (dd > 0.05f) r = Mathf.Min(r, Po[i] / dd);
                }
                if (d.y < -0.3f) r *= Mathf.Lerp(1f, flatBottom, (-d.y - 0.3f) / 0.7f);
                Radii[v] = Mathf.Max(0.25f, r);
            }
            return Radii;
        }

        /// Adds a rock. pos = center, scale = half extents in meters. detail 0 = 20 faces (pebbles), 1 = 80 faces.
        public static void Build(PropMeshBuilder b, Vector3 pos, Vector3 scale, Quaternion rot, int seed, int detail = 1, float rough = 0.14f)
        {
            var rng = new System.Random(seed);
            var radii = Jitter(rng, detail, rough, 2 + rng.Next(4));
            b.Ico(pos, scale, rot, detail, radii);
        }

        /// Rock as a standalone flat-shaded mesh (shared by the renderer batch and a MeshCollider), local space.
        public static Mesh Mesh(Vector3 scale, int seed, int detail = 1, float rough = 0.14f)
        {
            var b = new PropMeshBuilder();
            Build(b, Vector3.zero, scale, Quaternion.identity, seed, detail, rough);
            var m = b.ToMesh("PropRock");
            LowPoly.Sanitize(m);
            return m;
        }
    }

    /// Low-poly island vegetation ("How to Fish" look): segmented red-brown palms with big angular fronds,
    /// round faceted jungle trees, faceted bushes, broad-leaf plants, ferns and grass tufts.
    /// Variants are built once in local space and instanced into combined batches.
    public static class PropPlants
    {
        static readonly Vector3 Up = Vector3.up;

        /// Safe leaf width profile (0..1): narrow base, widest a bit before the middle, small at the tip.
        /// The argument stays inside (0, PI) so the result is always > 0 (the old Pow(sin(PI)) produced NaN).
        static float Profile(float s) => Mathf.Sin(Mathf.PI * Mathf.Clamp(0.1f + Mathf.Clamp01(s) * 0.82f, 0.02f, 0.98f));

        // ------------------------------------------------------------------ fronds / leaves
        /// Palm (or fern) frond: midrib along a drooping arc with saw-tooth leaflet triangles on both sides, folded down
        /// along the midrib. Two-sided flat triangles. Every direction used is a unit vector by construction.
        public static void Frond(PropMeshBuilder b, Vector3 origin, float azDeg, float elevDeg, float droopDeg, float len, float halfW, float foldDeg, int segs)
        {
            segs = Mathf.Clamp(segs, 2, 12);
            len = Mathf.Max(0.05f, len);
            halfW = Mathf.Max(0f, halfW);
            float az = azDeg * Mathf.Deg2Rad;
            Vector3 hor = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 lat = new Vector3(hor.z, 0f, -hor.x);          // = cross(up, hor), unit
            float fold = Mathf.Clamp(foldDeg, 0f, 80f) * Mathf.Deg2Rad;
            float cf = Mathf.Cos(fold), sf = Mathf.Sin(fold);
            float seg = len / segs;
            Vector3 p = origin;
            for (int i = 0; i < segs; i++)
            {
                float s1 = (float)(i + 1) / segs;
                float e = (elevDeg - droopDeg * Mathf.Pow(s1, 1.5f)) * Mathf.Deg2Rad;
                Vector3 dir = hor * Mathf.Cos(e) + Up * Mathf.Sin(e);  // unit (hor ⟂ up)
                Vector3 q = p + dir * seg;
                Vector3 nUp = Vector3.Cross(dir, lat);                 // unit (dir ⟂ lat)
                float w = halfW * Profile(s1);
                if (w > 0.01f)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 tip = q + dir * (seg * 0.25f) + lat * (side * w * cf) - nUp * (w * sf);
                        b.Tri2(p, q, tip, nUp);
                    }
                }
                p = q;
            }
        }

        /// Broad diamond leaf folded along the midrib (two-sided), 8 triangles per side.
        public static void Leaf(PropMeshBuilder b, Vector3 basePos, Vector3 hor, float len, float width, float elevDeg, float droopDeg)
        {
            hor.y = 0f;
            if (hor.sqrMagnitude < 1e-6f) hor = Vector3.forward;
            hor.Normalize();
            Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
            const int K = 3;
            var mid = new Vector3[K + 1];
            mid[0] = basePos;
            for (int i = 0; i < K; i++)
            {
                float s = (float)(i + 1) / K;
                float e = (elevDeg - droopDeg * s * s) * Mathf.Deg2Rad;
                mid[i + 1] = mid[i] + (hor * Mathf.Cos(e) + Up * Mathf.Sin(e)) * (len / K);
            }
            Vector3 along = mid[K] - mid[0];
            if (along.sqrMagnitude < 1e-8f) along = hor;
            Vector3 nUp = Vector3.Cross(along.normalized, lat);
            if (nUp.sqrMagnitude < 1e-8f) nUp = Up;
            nUp.Normalize();
            float w = width * 0.5f;
            const float fold = 0.3f;
            Vector3 E(int i, float side, float f) => mid[i] + lat * (side * w * f) - nUp * (w * f * fold);
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 e1 = E(1, side, 1f), e2 = E(2, side, 0.72f);
                b.Tri2(mid[0], mid[1], e1, nUp);
                b.Tri2(e1, mid[1], mid[2], nUp);
                b.Tri2(e1, mid[2], e2, nUp);
                b.Tri2(e2, mid[2], mid[3], nUp);
            }
        }

        // ------------------------------------------------------------------ palm
        public static PropModel Palm(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = PropUtil.Range(rng, 6.5f, 11.5f);
            float lean = PropUtil.Range(rng, 0.1f, 0.3f);
            float wig = PropUtil.Range(rng, -0.3f, 0.3f);
            int segs = 7 + rng.Next(3);
            Vector3 Curve(float t) => new Vector3(lean * H * Mathf.Pow(t, 1.6f) + Mathf.Sin(t * Mathf.PI) * wig, H * t, Mathf.Sin(t * Mathf.PI * 0.8f) * wig * 0.4f);

            // trunk: stacked, slightly rotated 6-sided segments, each tapering → visible rings; alternating dark rings
            var ta = PropMats.PalmTrunk;
            var tb = PropMats.PalmTrunkDark;
            float yaw0 = PropUtil.Range(rng, 0f, 60f);
            Vector3 top = Curve(1f), topDir = Up;
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                Vector3 a = Curve(t0), c = Curve(t1);
                if (i == 0) a.y = -0.45f;
                Vector3 d = c - a;
                float l = d.magnitude;
                if (l < 1e-3f) continue;
                d /= l;
                float r0 = Mathf.Lerp(0.27f, 0.16f, t0) + (i == 0 ? 0.07f : 0f);
                float r1 = Mathf.Lerp(0.27f, 0.16f, t1) * 0.84f;
                Quaternion q = Quaternion.FromToRotation(Up, d) * Quaternion.Euler(0f, yaw0 + i * 17f, 0f);
                mb.B(i % 2 == 0 ? ta : tb).Cylinder(a, q, r0, r1, l + 0.05f, 6, i > 0, false);
                top = c;
                topDir = d;
            }
            Quaternion topRot = Quaternion.FromToRotation(Up, topDir);
            mb.B(tb).Cylinder(top - topDir * 0.05f, topRot, 0.2f, 0.07f, 0.42f, 6, true, false);
            Vector3 crown = top + topDir * 0.3f;

            // coconuts
            int nuts = 2 + rng.Next(3);
            for (int i = 0; i < nuts; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 np = top - Up * PropUtil.Range(rng, 0.08f, 0.22f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.24f;
                float s = PropUtil.Range(rng, 0.13f, 0.17f);
                mb.B(PropMats.Coconut).Ico(np, new Vector3(s, s * 1.1f, s), Quaternion.Euler(PropUtil.Range(rng, -30f, 30f), PropUtil.Range(rng, 0f, 360f), 0f), 0);
            }

            // crown: 7-10 big angular fronds, alternating light / dark green
            int nf = 7 + rng.Next(4);
            float azOff = PropUtil.Range(rng, 0f, 360f);
            bool darkFirst = (seed & 1) == 0;
            float sizeK = Mathf.Pow(H / 9f, 0.35f);
            for (int f = 0; f < nf; f++)
            {
                float az = azOff + f * 360f / nf + PropUtil.Range(rng, -10f, 10f);
                bool young = f < 2;
                float elev = young ? PropUtil.Range(rng, 50f, 68f) : PropUtil.Range(rng, 14f, 38f);
                float droop = young ? PropUtil.Range(rng, 25f, 45f) : PropUtil.Range(rng, 60f, 95f);
                float len = PropUtil.Range(rng, 2.8f, 3.9f) * sizeK * (young ? 0.75f : 1f);
                var lm = ((f % 2 == 0) ^ darkFirst) ? PropMats.PalmLeaf : PropMats.PalmLeafDark;
                Frond(mb.B(lm), crown, az, elev, droop, len, len * 0.21f, PropUtil.Range(rng, 22f, 34f), 6);
            }
            // a dry frond or two hanging down
            int nd = rng.Next(3);
            for (int f = 0; f < nd; f++)
            {
                float az = PropUtil.Range(rng, 0f, 360f);
                float len = PropUtil.Range(rng, 2.0f, 2.8f) * sizeK;
                Frond(mb.B(PropMats.PalmLeafDry), crown - Up * 0.22f, az, PropUtil.Range(rng, -35f, -15f), PropUtil.Range(rng, 25f, 40f), len, len * 0.17f, 30f, 5);
            }
            return mb.Build("Palm" + seed, H, 4f);
        }

        // ------------------------------------------------------------------ round jungle tree
        public static PropModel Tree(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = PropUtil.Range(rng, 4.2f, 7f);
            Vector3 lean = new Vector3(PropUtil.Range(rng, -0.5f, 0.5f), 0f, PropUtil.Range(rng, -0.5f, 0.5f));
            Vector3 b0 = new Vector3(0f, -0.4f, 0f), b1 = lean * 0.4f + Up * H * 0.45f, b2 = lean + Up * H * 0.78f;
            var trunk = mb.B(PropMats.TreeTrunk);
            trunk.Rod(b0, b1, 0.3f, 0.22f, 6);
            trunk.Rod(b1, b2, 0.22f, 0.12f, 6, true);
            // one side branch
            float ba = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
            Vector3 br = b1 + new Vector3(Mathf.Cos(ba), 0f, Mathf.Sin(ba)) * 1.1f + Up * 1.0f;
            trunk.Rod(b1, br, 0.12f, 0.06f, 5, true);

            int n = 2 + rng.Next(2);
            Material[] greens = { PropMats.Grass, PropMats.GrassDark, PropMats.GrassLight };
            int g0 = rng.Next(greens.Length);
            for (int k = 0; k < n; k++)
            {
                Vector3 c = k == 0 ? b2 + Up * 0.6f : (k == 1 ? br + Up * 0.4f : b2 + new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, -0.6f, 0.2f), PropUtil.Range(rng, -1f, 1f)));
                float s = (k == 0 ? PropUtil.Range(rng, 1.7f, 2.3f) : PropUtil.Range(rng, 1.1f, 1.6f)) * Mathf.Sqrt(H / 5.5f);
                var radii = PropRocks.Jitter(rng, 1, 0.12f, 1, 0.75f);
                mb.B(greens[(g0 + k) % greens.Length]).Ico(c, new Vector3(s, s * 0.82f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), 1, radii);
            }
            return mb.Build("Tree" + seed, H + 2f, 2.5f);
        }

        // ------------------------------------------------------------------ ground plants
        public static PropModel Fern(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            int n = 7 + rng.Next(4);
            float size = PropUtil.Range(rng, 0.6f, 1.1f);
            var mat = (seed & 1) == 0 ? PropMats.GrassDark : PropMats.PalmLeafDark;
            for (int i = 0; i < n; i++)
            {
                float az = i * 360f / n + PropUtil.Range(rng, -15f, 15f);
                float len = size * PropUtil.Range(rng, 0.75f, 1.1f);
                Frond(mb.B(mat), new Vector3(0f, 0.04f, 0f), az, PropUtil.Range(rng, 38f, 62f), PropUtil.Range(rng, 55f, 95f), len, len * 0.24f, 18f, 5);
            }
            return mb.Build("Fern" + seed, size, size);
        }

        /// Faceted bush: 2-4 overlapping jittered icospheres, sometimes with berries.
        public static PropModel Bush(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float R = PropUtil.Range(rng, 0.6f, 1.1f);
            int n = 2 + rng.Next(3);
            Material[] greens = { PropMats.GrassDark, PropMats.Grass, PropMats.GrassLight };
            int g0 = rng.Next(greens.Length);
            float maxH = 0f;
            var centers = new List<Vector4>(4);
            for (int k = 0; k < n; k++)
            {
                Vector2 o = k == 0 ? Vector2.zero : PropUtil.InDisc(rng, R * 0.6f);
                float s = R * (k == 0 ? 1f : PropUtil.Range(rng, 0.55f, 0.8f));
                Vector3 c = new Vector3(o.x, s * 0.62f, o.y);
                int detail = s > 0.75f ? 1 : 0;
                var radii = PropRocks.Jitter(rng, detail, 0.12f, 1, 0.7f);
                mb.B(greens[(g0 + k) % greens.Length]).Ico(c, new Vector3(s, s * 0.8f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), detail, radii);
                maxH = Mathf.Max(maxH, c.y + s * 0.8f);
                centers.Add(new Vector4(c.x, c.y, c.z, s));
            }
            if (rng.NextDouble() < 0.35)
            {
                var bm = rng.NextDouble() < 0.5 ? PropMats.FlowerRed : PropMats.FlowerYellow;
                int berries = 4 + rng.Next(4);
                for (int i = 0; i < berries; i++)
                {
                    var c = centers[rng.Next(centers.Count)];
                    Vector3 d = new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, 0.1f, 1f), PropUtil.Range(rng, -1f, 1f)).normalized;
                    Vector3 p = new Vector3(c.x, c.y, c.z) + Vector3.Scale(d, new Vector3(c.w, c.w * 0.8f, c.w)) * 0.95f;
                    mb.B(bm).Octa(p, 0.06f);
                }
            }
            return mb.Build("Bush" + seed, maxH, R);
        }

        public static PropModel BroadLeaf(int seed, int flowerKind)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            int n = 5 + rng.Next(3);
            float maxH = 0f;
            var leafA = (seed & 1) == 0 ? PropMats.PalmLeaf : PropMats.Grass;
            var leafB = PropMats.GrassDark;
            for (int i = 0; i < n; i++)
            {
                float az = i * 360f / n + PropUtil.Range(rng, -20f, 20f);
                float ar = az * Mathf.Deg2Rad;
                Vector3 hor = new Vector3(Mathf.Cos(ar), 0f, Mathf.Sin(ar));
                float h = PropUtil.Range(rng, 0.45f, 1.0f);
                float reach = PropUtil.Range(rng, 0.2f, 0.45f);
                Vector3 tip = hor * reach + Up * h;
                mb.B(PropMats.Stem).Rod(Vector3.zero, tip, 0.03f, 0.018f, 3);
                maxH = Mathf.Max(maxH, h);
                Leaf(mb.B(i % 2 == 0 ? leafA : leafB), tip, hor, PropUtil.Range(rng, 0.55f, 1.0f), PropUtil.Range(rng, 0.35f, 0.55f), PropUtil.Range(rng, 5f, 25f), PropUtil.Range(rng, 25f, 50f));
            }
            if (flowerKind > 0)
            {
                Material fm = flowerKind == 1 ? PropMats.FlowerRed : flowerKind == 2 ? PropMats.FlowerOrange : PropMats.FlowerPink;
                int spikes = 1 + rng.Next(3);
                for (int s = 0; s < spikes; s++)
                {
                    float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                    Vector3 basep = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f;
                    float h = maxH * PropUtil.Range(rng, 0.85f, 1.15f);
                    Vector3 top = basep + Up * h;
                    mb.B(PropMats.Stem).Rod(basep, top, 0.022f, 0.016f, 3);
                    if (flowerKind == 3)
                    {
                        mb.B(fm).Ico(top + Up * 0.1f, new Vector3(0.09f, 0.14f, 0.09f), Quaternion.identity, 0);
                    }
                    else
                    {
                        // heliconia-like alternating bracts (little cones)
                        int bracts = 3 + rng.Next(3);
                        for (int k = 0; k < bracts; k++)
                        {
                            Vector3 bp = top - Up * (k * 0.1f);
                            float side = (k % 2 == 0) ? 1f : -1f;
                            Quaternion q = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * Quaternion.Euler(0f, 0f, side * 58f);
                            mb.B(fm).Cylinder(bp, q, 0.055f, 0f, 0.22f - k * 0.02f, 4);
                        }
                    }
                }
            }
            return mb.Build("BroadLeaf" + seed, maxH, 1f);
        }

        /// Grass tuft: 3-5 flat two-sided blades (one triangle each), optionally with a little flower on a stalk.
        public static PropModel GrassTuft(int seed, Material blade, Material flower)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var b = mb.B(blade);
            int n = 3 + rng.Next(3);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.12f);
                Vector3 bp = new Vector3(o.x, -0.03f, o.y);
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 outDir = o.sqrMagnitude > 1e-6f ? new Vector3(o.x, 0f, o.y).normalized : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float h = PropUtil.Range(rng, 0.22f, 0.5f);
                float w = PropUtil.Range(rng, 0.05f, 0.085f);
                Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * w;
                Vector3 tip = bp + Up * h + outDir * (h * PropUtil.Range(rng, 0.15f, 0.5f));
                Vector3 n0 = Vector3.Cross(side, tip - bp);
                if (n0.sqrMagnitude < 1e-10f) n0 = outDir;
                b.Tri2(bp - side, bp + side, tip, n0.normalized);
                maxH = Mathf.Max(maxH, tip.y);
            }
            if (flower != null)
            {
                Vector3 top = new Vector3(PropUtil.Range(rng, -0.05f, 0.05f), maxH + PropUtil.Range(rng, 0.05f, 0.15f), PropUtil.Range(rng, -0.05f, 0.05f));
                Vector3 side = new Vector3(0.012f, 0f, 0f);
                b.Tri2(new Vector3(0f, -0.03f, 0f) - side, new Vector3(0f, -0.03f, 0f) + side, top, Vector3.forward);
                mb.B(flower).Octa(top, new Vector3(0.07f, 0.04f, 0.07f));
                maxH = top.y;
            }
            return mb.Build("Tuft" + seed, maxH, 0.25f);
        }
    }
}
