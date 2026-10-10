using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural rock geometry (faceted noisy cube-spheres).
    public static class PropRocks
    {
        /// Adds a rock to the builder. pos = rock center, scale = half extents in meters.
        /// texScale multiplies the UV density (UVs are in meters, so world-tiled rock materials keep their texel size).
        public static void Build(PropMeshBuilder b, Vector3 pos, Vector3 scale, Quaternion rot, int seed, int res = 7, float rough = 0.18f, float texScale = 1f)
        {
            var rng = new System.Random(seed);
            int planes = 5 + rng.Next(4);
            var pn = new Vector3[planes];
            var po = new float[planes];
            for (int i = 0; i < planes; i++)
            {
                pn[i] = new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, -0.4f, 1f), PropUtil.Range(rng, -1f, 1f)).normalized;
                po[i] = PropUtil.Range(rng, 0.72f, 0.95f);
            }
            Vector3 off = new Vector3(PropUtil.Range(rng, 0f, 90f), PropUtil.Range(rng, 0f, 90f), PropUtil.Range(rng, 0f, 90f));
            int nseed = seed & 0xFFFF;
            // a cube-sphere face spans about 2 x the largest half extent in meters
            float faceMeters = 2f * Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z)) * texScale;
            b.Blob(pos, scale, rot, res, d =>
            {
                float r = 1f;
                for (int i = 0; i < planes; i++)
                {
                    float dd = Vector3.Dot(d, pn[i]);
                    if (dd > 0.05f) r = Mathf.Min(r, po[i] / dd);
                }
                r *= 1f + (PropNoise.Fbm3(d * 1.8f + off, 4, nseed) - 0.5f) * 2f * rough;
                if (d.y < -0.2f) r *= Mathf.Lerp(1f, 0.6f, (-d.y - 0.2f) / 0.8f);
                return r;
            }, Mathf.Max(0.05f, faceMeters));
        }

        /// Rock as a standalone mesh (for MeshColliders), built in local space.
        public static Mesh Mesh(Vector3 scale, int seed, int res = 7, float rough = 0.18f)
        {
            var b = new PropMeshBuilder();
            Build(b, Vector3.zero, scale, Quaternion.identity, seed, res, rough);
            return b.ToMesh("PropRock");
        }
    }

    /// Palm, fern, shrub and broad-leaf plant variants (built once in local space, instanced into batches).
    public static class PropPlants
    {
        // ------------------------------------------------------------------ fronds
        /// V-folded leaf strip along a drooping curve. UV u across (0..1), v along (0..1).
        public static void Frond(PropMeshBuilder b, Vector3 origin, float azDeg, float elev0, float droop, float len, float halfW, float fold, bool doubleSide, int segs = 10, float twist = 0f)
        {
            float az = azDeg * Mathf.Deg2Rad;
            Vector3 hor = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 lateral = Vector3.Cross(Vector3.up, hor).normalized;
            int vs = b.VertexCount, ts = b.IndexCount;
            Vector3 p = origin;
            for (int i = 0; i <= segs; i++)
            {
                float s = (float)i / segs;
                float e = (elev0 - droop * Mathf.Pow(s, 1.4f)) * Mathf.Deg2Rad;
                Vector3 dir = hor * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e);
                if (i > 0) p += dir * (len / segs);
                Vector3 lat = Quaternion.AngleAxis(twist * s, dir) * lateral;
                Vector3 nUp = Vector3.Cross(dir, lat).normalized;
                float w = halfW * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Min(1f, s * 1.05f + 0.04f)), 0.6f);
                float cf = Mathf.Cos(fold), sf = Mathf.Sin(fold);
                Vector3 left = p + lat * (w * cf) - nUp * (w * sf);
                Vector3 right = p - lat * (w * cf) - nUp * (w * sf);
                b.Vert(left, nUp, new Vector2(0f, s));
                b.Vert(p, nUp, new Vector2(0.5f, s));
                b.Vert(right, nUp, new Vector2(1f, s));
            }
            for (int i = 0; i < segs; i++)
            {
                // wound so the face normal is cross(dir, lateral) = the frond's upper side
                int r0 = vs + i * 3, r1 = r0 + 3;
                b.Quad(r0, r0 + 1, r1 + 1, r1);
                b.Quad(r0 + 1, r0 + 2, r1 + 2, r1 + 1);
            }
            b.SmoothNormals(vs, ts, false);
            if (doubleSide) b.DoubleSide(vs, ts);
        }

        // ------------------------------------------------------------------ palm
        public static PropModel Palm(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = PropUtil.Range(rng, 7f, 13.5f);
            float lean = PropUtil.Range(rng, 0.06f, 0.3f);
            float wig = PropUtil.Range(rng, -0.25f, 0.25f);
            const int segs = 11;
            var pts = new List<Vector3>(segs + 2);
            var radii = new List<float>(segs + 2);
            pts.Add(new Vector3(0f, -0.5f, 0f));
            radii.Add(0.34f);
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                float x = lean * H * Mathf.Pow(t, 1.7f) + Mathf.Sin(t * Mathf.PI * 1.5f) * wig;
                float z = Mathf.Sin(t * Mathf.PI) * wig * 0.5f;
                pts.Add(new Vector3(x, H * t, z));
                float r = Mathf.Lerp(0.22f, 0.135f, t) + Mathf.Max(0f, (0.1f - t) / 0.1f) * 0.11f;
                radii.Add(r);
            }
            mb.B(PropMats.Bark).Tube(pts, radii, 8, 1f, 0.4f);
            Vector3 top = pts[pts.Count - 1];
            Vector3 tan = (top - pts[pts.Count - 2]).normalized;
            mb.B(PropMats.Bark).Rod(top - tan * 0.35f, top + tan * 0.3f, 0.19f, 0.1f, 7, true);

            // coconuts
            int nuts = 2 + rng.Next(5);
            for (int i = 0; i < nuts; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 np = top - Vector3.up * PropUtil.Range(rng, 0.2f, 0.45f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.22f;
                float s = PropUtil.Range(rng, 0.1f, 0.13f);
                mb.B(PropMats.Coconut).Blob(np, new Vector3(s, s * 1.15f, s), Quaternion.Euler(PropUtil.Range(rng, -30f, 30f), 0f, 0f), 2, null);
            }

            // living fronds
            int nf = 8 + rng.Next(5);
            float azOff = PropUtil.Range(rng, 0f, 360f);
            for (int f = 0; f < nf; f++)
            {
                float az = azOff + f * 360f / nf + PropUtil.Range(rng, -12f, 12f);
                bool young = f < 2;
                float elev = young ? PropUtil.Range(rng, 55f, 75f) : PropUtil.Range(rng, 12f, 45f);
                float droop = young ? PropUtil.Range(rng, 30f, 60f) : PropUtil.Range(rng, 70f, 120f);
                float len = PropUtil.Range(rng, 3.0f, 4.5f) * Mathf.Pow(H / 10f, 0.3f) * (young ? 0.8f : 1f);
                Frond(mb.B(PropMats.PalmLeaf), top + Vector3.up * 0.1f, az, elev, droop, len, len * 0.2f, PropUtil.Range(rng, 0.35f, 0.6f), true, 10, PropUtil.Range(rng, -25f, 25f));
            }
            // dry hanging fronds
            int nd = 1 + rng.Next(3);
            for (int f = 0; f < nd; f++)
            {
                float az = PropUtil.Range(rng, 0f, 360f);
                float len = PropUtil.Range(rng, 2.2f, 3.0f);
                Frond(mb.B(PropMats.PalmLeafDry), top - Vector3.up * 0.15f, az, PropUtil.Range(rng, -30f, -10f), PropUtil.Range(rng, 40f, 60f), len, len * 0.14f, 0.9f, true, 7);
            }
            return mb.Build("Palm" + seed, H, 4.5f);
        }

        // ------------------------------------------------------------------ ground plants
        public static PropModel Fern(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            int n = 10 + rng.Next(7);
            float size = PropUtil.Range(rng, 0.7f, 1.3f);
            for (int i = 0; i < n; i++)
            {
                float az = i * 360f / n + PropUtil.Range(rng, -15f, 15f);
                float len = size * PropUtil.Range(rng, 0.7f, 1.1f);
                Frond(mb.B(PropMats.FernLeaf), new Vector3(0f, 0.04f, 0f), az, PropUtil.Range(rng, 40f, 72f), PropUtil.Range(rng, 55f, 105f), len, len * 0.2f, 0.12f, false, 6);
            }
            return mb.Build("Fern" + seed, size, size);
        }

        public static PropModel Shrub(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var b = mb.B(PropMats.BushLeaf);
            float R = PropUtil.Range(rng, 0.6f, 1.1f);
            float Hh = PropUtil.Range(rng, 0.8f, 1.6f);
            Vector3 center = new Vector3(0f, Hh * 0.5f, 0f);
            int clusters = 7 + rng.Next(5);
            for (int c = 0; c < clusters; c++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float r = Mathf.Sqrt(PropUtil.R01(rng)) * R * 0.75f;
                Vector3 cp = new Vector3(Mathf.Cos(a) * r, PropUtil.Range(rng, 0.25f, 1f) * Hh, Mathf.Sin(a) * r);
                float s = PropUtil.Range(rng, 0.55f, 0.95f) * Mathf.Max(0.8f, R);
                float yaw = PropUtil.Range(rng, 0f, 180f);
                for (int k = 0; k < 3; k++)
                {
                    Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -20f, 20f), yaw + k * 60f, PropUtil.Range(rng, -15f, 15f));
                    Vector3 rx = q * Vector3.right * s, ry = q * Vector3.up * s;
                    int v0 = b.VertexCount;
                    b.Vert(cp - rx - ry, Vector3.up, new Vector2(0f, 0f));
                    b.Vert(cp - rx + ry, Vector3.up, new Vector2(0f, 1f));
                    b.Vert(cp + rx + ry, Vector3.up, new Vector2(1f, 1f));
                    b.Vert(cp + rx - ry, Vector3.up, new Vector2(1f, 0f));
                    b.Quad(v0, v0 + 1, v0 + 2, v0 + 3);
                    // foliage normals point away from the bush center for soft, volumetric shading
                    for (int i = v0; i < v0 + 4; i++) b.N[i] = ((b.V[i] - center).normalized + Vector3.up * 0.6f).normalized;
                }
            }
            // a few woody stems
            var stems = mb.B(PropMats.BarkCharred);
            for (int i = 0; i < 4; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                stems.Rod(Vector3.zero, new Vector3(Mathf.Cos(a) * R * 0.4f, Hh * 0.6f, Mathf.Sin(a) * R * 0.4f), 0.035f, 0.015f, 4);
            }
            return mb.Build("Shrub" + seed, Hh, R);
        }

        public static PropModel BroadLeaf(int seed, int flowerKind)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            int n = 5 + rng.Next(5);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float az = i * 360f / n + PropUtil.Range(rng, -20f, 20f);
                float ar = az * Mathf.Deg2Rad;
                Vector3 hor = new Vector3(Mathf.Cos(ar), 0f, Mathf.Sin(ar));
                float h = PropUtil.Range(rng, 0.5f, 1.2f);
                float reach = PropUtil.Range(rng, 0.25f, 0.55f);
                Vector3 p0 = Vector3.zero, p1 = hor * reach * 0.3f + Vector3.up * h * 0.6f, p2 = hor * reach + Vector3.up * h;
                mb.B(PropMats.Stem).Tube(new[] { p0, p1, p2 }, new[] { 0.025f, 0.02f, 0.014f }, 4);
                maxH = Mathf.Max(maxH, h);
                Leaf(mb.B(PropMats.BigLeaf), p2, hor, PropUtil.Range(rng, 0.6f, 1.15f), PropUtil.Range(rng, 0.35f, 0.6f), PropUtil.Range(rng, 10f, 35f), PropUtil.Range(rng, 40f, 75f));
            }
            if (flowerKind > 0)
            {
                Material fm = flowerKind == 1 ? PropMats.FlowerRed : flowerKind == 2 ? PropMats.FlowerOrange : PropMats.FlowerPink;
                int spikes = 1 + rng.Next(3);
                for (int s = 0; s < spikes; s++)
                {
                    float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                    Vector3 basep = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.12f;
                    float h = maxH * PropUtil.Range(rng, 0.8f, 1.15f);
                    Vector3 top = basep + Vector3.up * h;
                    mb.B(PropMats.Stem).Rod(basep, top, 0.02f, 0.015f, 4);
                    if (flowerKind == 3)
                    {
                        mb.B(fm).Blob(top + Vector3.up * 0.1f, new Vector3(0.07f, 0.14f, 0.07f), Quaternion.identity, 3, null);
                    }
                    else
                    {
                        // heliconia-like alternating bracts
                        int bracts = 4 + rng.Next(3);
                        for (int k = 0; k < bracts; k++)
                        {
                            Vector3 bp = top - Vector3.up * (k * 0.09f);
                            float side = (k % 2 == 0) ? 1f : -1f;
                            Quaternion q = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f) * Quaternion.Euler(0f, 0f, side * 58f);
                            mb.B(fm).Cylinder(bp, q, 0.045f, 0.004f, 0.2f - k * 0.015f, 5);
                        }
                    }
                }
            }
            return mb.Build("BroadLeaf" + seed, maxH, 1f);
        }

        /// Broad heart-shaped leaf blade with a folded midrib (u across, v along).
        public static void Leaf(PropMeshBuilder b, Vector3 basePos, Vector3 hor, float len, float width, float elev0, float droop)
        {
            const int along = 7, across = 5;
            Vector3 lateral = Vector3.Cross(Vector3.up, hor).normalized;
            int vs = b.VertexCount, ts = b.IndexCount;
            Vector3 p = basePos;
            for (int i = 0; i <= along; i++)
            {
                float s = (float)i / along;
                float e = (elev0 - droop * s * s) * Mathf.Deg2Rad;
                Vector3 dir = hor * Mathf.Cos(e) + Vector3.up * Mathf.Sin(e);
                if (i > 0) p += dir * (len / along);
                Vector3 nUp = Vector3.Cross(dir, lateral).normalized;
                float w = width * 0.5f * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(s * 0.92f + 0.08f)), 0.8f) * (1f + 0.25f * (1f - s));
                for (int j = 0; j <= across; j++)
                {
                    float x = (float)j / across * 2f - 1f;
                    Vector3 q = p + lateral * (x * w) - nUp * (Mathf.Abs(x) * w * 0.15f) - nUp * (x * x * w * 0.1f);
                    b.Vert(q, nUp, new Vector2(x * 0.5f + 0.5f, s));
                }
            }
            for (int i = 0; i < along; i++)
            {
                for (int j = 0; j < across; j++)
                {
                    int a = vs + i * (across + 1) + j;
                    int c = a + across + 1;
                    b.Quad(a, c, c + 1, a + 1);
                }
            }
            b.SmoothNormals(vs, ts, false);
        }
    }
}
