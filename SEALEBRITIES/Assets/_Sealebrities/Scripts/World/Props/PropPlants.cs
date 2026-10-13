using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Low-poly rocks: jittered icospheres with a few flat cut planes (chunky facets), flattened at the bottom.
    /// Art bible §1.10: violet-grey variants, crevice faces, moss on the up-facing faces of some inland rocks.
    public static class PropRocks
    {
        static readonly float[] Radii = new float[700];
        static readonly Vector3[] Pn = new Vector3[8];
        static readonly float[] Po = new float[8];
        static readonly PropMeshBuilder Scratch = new PropMeshBuilder();

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
            for (int v = 0; v < dirs.Length && v < Radii.Length; v++)
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

        /// Collider mesh (shared vertices, not flat) of the same rock as Build/BuildSplit with these arguments.
        public static Mesh ColliderMesh(Vector3 scale, int seed, int detail = 1, float rough = 0.14f)
        {
            var b = new PropMeshBuilder();
            Build(b, Vector3.zero, scale, Quaternion.identity, seed, detail, rough);
            var m = b.ToMesh("PropRockCol", false);
            return m;
        }

        /// Rock split into up to three materials by face orientation: faces with normal.y > 0.7 → top (moss, may be
        /// null = main), faces pointing steeply down → under (crevice, may be null = main), the rest → main.
        public static void BuildSplit(PropBatch batch, Vector3 cellPos, Vector3 pos, Vector3 scale, Quaternion rot, int seed, int detail, float rough,
            Material main, Material top, Material under)
        {
            Scratch.Clear();
            Build(Scratch, Vector3.zero, scale, Quaternion.identity, seed, detail, rough);
            Route(Scratch, Matrix4x4.TRS(pos, rot, Vector3.one), batch, cellPos, main, top, under, 0.7f, -0.35f);
        }

        /// Routes every triangle of src (transformed by m) into the batch builders by its face normal.
        public static void Route(PropMeshBuilder src, Matrix4x4 m, PropBatch batch, Vector3 cellPos, Material main, Material top, Material under,
            float topY, float underY)
        {
            var bm = batch.W(main, cellPos);
            var bt = top != null ? batch.W(top, cellPos) : bm;
            var bu = under != null ? batch.W(under, cellPos) : bm;
            var V = src.V;
            var T = src.T;
            for (int t = 0; t + 2 < T.Count; t += 3)
            {
                Vector3 a = m.MultiplyPoint3x4(V[T[t]]), b = m.MultiplyPoint3x4(V[T[t + 1]]), c = m.MultiplyPoint3x4(V[T[t + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a);
                float l = n.magnitude;
                if (!(l > 1e-9f)) continue;
                n /= l;
                var dst = n.y > topY ? bt : (n.y < underY ? bu : bm);
                dst.Face3(a, b, c, n);
            }
        }
    }

    /// Low-poly island vegetation in the "How to Fish" look (art bible §1.9): slender segmented mauve palms with broad
    /// toothed two-tone fronds, broadleaf canopy trees, tiered conifers, flowering bushes, ferns, taro, bananas and
    /// meadow flowers. Every model is built once in local space and instanced into combined, culled chunks.
    /// Trees have a "lite" variant (same seed, same shape, far fewer triangles) for the distant LOD.
    public static class PropPlants
    {
        static readonly Vector3 Up = Vector3.up;

        static float R(System.Random r, float a, float b) => PropUtil.Range(r, a, b);

        /// Adds a triangle as two faces in two materials: the face whose normal points up gets 'top', the other face
        /// 'under' (two-tone leaves: lit top, dark underside).
        public static void TwoTone(PropMeshBuilder top, PropMeshBuilder under, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (!(n.sqrMagnitude > 1e-14f)) return;
            if (n.y < 0f) n = -n;
            top.Face3(a, b, c, n);
            under.Face3(a, b, c, -n);
        }

        public static void TwoToneQuad(PropMeshBuilder top, PropMeshBuilder under, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            TwoTone(top, under, a, b, c);
            TwoTone(top, under, a, c, d);
        }

        // ------------------------------------------------------------------ fronds / leaves
        static readonly Vector3[] Mid = new Vector3[16];
        static readonly Vector3[] Dir = new Vector3[16];
        static readonly Vector3[] Nup = new Vector3[16];
        static readonly float[] Wd = new float[16];

        /// Palm frond: broad blade with a jagged outline (teeth = 35 % of the local width), V-folded along the midrib
        /// (the halves hang down like a roof), drooping from elev0 at the base to elev1 at the tip.
        public static void PalmFrond(PropMeshBuilder top, PropMeshBuilder under, Vector3 origin, float azDeg, float elev0, float elev1,
            float len, float halfW, float foldDeg, int teeth, bool toothed)
        {
            int n = Mathf.Clamp(teeth, 2, 12);
            len = Mathf.Max(0.1f, len);
            float az = azDeg * Mathf.Deg2Rad;
            Vector3 hor = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
            float fold = Mathf.Clamp(foldDeg, 0f, 70f) * Mathf.Deg2Rad;
            float cf = Mathf.Cos(fold), sf = Mathf.Sin(fold);
            float seg = len / n;

            Mid[0] = origin;
            for (int i = 0; i < n; i++)
            {
                float s = (i + 0.5f) / n;
                float e = Mathf.Lerp(elev0, elev1, Mathf.Pow(s, 1.3f)) * Mathf.Deg2Rad;
                Dir[i] = hor * Mathf.Cos(e) + Up * Mathf.Sin(e);
                Mid[i + 1] = Mid[i] + Dir[i] * seg;
            }
            Dir[n] = Dir[n - 1];
            for (int i = 0; i <= n; i++)
            {
                Vector3 d = i == 0 ? Dir[0] : (i == n ? Dir[n - 1] : (Dir[i - 1] + Dir[i]).normalized);
                Vector3 nu = Vector3.Cross(d, lat);
                Nup[i] = nu.sqrMagnitude > 1e-8f ? nu.normalized : Up;
                float s = i / (float)n;
                // narrow base, widest at ~30 %, tapering to the tip
                float prof = s < 0.3f ? Mathf.Lerp(0.3f, 1f, s / 0.3f) : Mathf.Lerp(1f, 0.08f, Mathf.Pow((s - 0.3f) / 0.7f, 1.1f));
                Wd[i] = i == n ? 0f : halfW * prof;
            }

            float solid = toothed ? 0.65f : 1f;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 in0 = Mid[i] + lat * (side * Wd[i] * solid * cf) - Nup[i] * (Wd[i] * solid * sf);
                    Vector3 in1 = Mid[i + 1] + lat * (side * Wd[i + 1] * solid * cf) - Nup[i + 1] * (Wd[i + 1] * solid * sf);
                    TwoTone(top, under, Mid[i], Mid[i + 1], in1);
                    TwoTone(top, under, Mid[i], in1, in0);
                    if (!toothed) continue;
                    float wt = Mathf.Max(Wd[i + 1], Wd[i] * 0.6f);
                    Vector3 tip = Mid[i + 1] + Dir[i] * (seg * 0.35f) + lat * (side * wt * cf) - Nup[i + 1] * (wt * sf);
                    TwoTone(top, under, in0, in1, tip);
                }
            }
        }

        /// Fern / leaflet frond: thin midrib with narrow leaflets pointing outward and forward, two-tone.
        public static void LeafletFrond(PropMeshBuilder top, PropMeshBuilder under, Vector3 origin, float azDeg, float elev0, float elev1,
            float len, float halfW, int segs)
        {
            int n = Mathf.Clamp(segs, 2, 12);
            float az = azDeg * Mathf.Deg2Rad;
            Vector3 hor = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
            Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
            float seg = len / n;
            Vector3 p = origin;
            for (int i = 0; i < n; i++)
            {
                float s = (i + 0.5f) / n;
                float e = Mathf.Lerp(elev0, elev1, s * s) * Mathf.Deg2Rad;
                Vector3 d = hor * Mathf.Cos(e) + Up * Mathf.Sin(e);
                Vector3 q = p + d * seg;
                float w = halfW * Mathf.Sin(Mathf.PI * Mathf.Clamp(0.15f + s * 0.8f, 0.05f, 0.95f));
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 tip = q + d * (seg * 0.6f) + lat * (side * w) - Up * (w * 0.25f);
                    TwoTone(top, under, p, q, tip);
                }
                p = q;
            }
        }

        /// Broad leaf (taro / heliconia) folded along the midrib, two-tone.
        public static void BroadLeaf(PropMeshBuilder top, PropMeshBuilder under, Vector3 basePos, Vector3 hor, float len, float width, float elevDeg, float droopDeg, bool heart)
        {
            hor.y = 0f;
            if (hor.sqrMagnitude < 1e-6f) hor = Vector3.forward;
            hor.Normalize();
            Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
            const int K = 4;
            for (int i = 0; i <= K; i++)
            {
                if (i == 0) { Mid[0] = basePos; continue; }
                float s = i / (float)K;
                float e = (elevDeg - droopDeg * s * s) * Mathf.Deg2Rad;
                Mid[i] = Mid[i - 1] + (hor * Mathf.Cos(e) + Up * Mathf.Sin(e)) * (len / K);
            }
            Vector3 along = Mid[K] - Mid[0];
            if (along.sqrMagnitude < 1e-8f) along = hor;
            Vector3 nUp = Vector3.Cross(along.normalized, lat);
            if (nUp.sqrMagnitude < 1e-8f) nUp = Up;
            nUp.Normalize();
            float w = width * 0.5f;
            const float fold = 0.25f;
            for (int side = -1; side <= 1; side += 2)
            {
                // edge points: widest at 35 %, heart-shaped leaves have lobes behind the base
                Vector3 E(int i, float f) => Mid[i] + lat * (side * w * f) - nUp * (w * f * fold);
                Vector3 e1 = E(1, 0.95f), e2 = E(2, 0.85f), e3 = E(3, 0.5f);
                if (heart)
                {
                    Vector3 lobe = Mid[0] - along.normalized * (len * 0.18f) + lat * (side * w * 0.55f) - nUp * (w * 0.1f);
                    TwoTone(top, under, Mid[0], e1, lobe);
                }
                TwoTone(top, under, Mid[0], Mid[1], e1);
                TwoTone(top, under, e1, Mid[1], Mid[2]);
                TwoTone(top, under, e1, Mid[2], e2);
                TwoTone(top, under, e2, Mid[2], Mid[3]);
                TwoTone(top, under, e2, Mid[3], e3);
                TwoTone(top, under, e3, Mid[3], Mid[4]);
            }
        }

        /// Flat five-petal star flower (0.08 m), single-sided facing 'normal'.
        public static void StarFlower(PropMeshBuilder petal, PropMeshBuilder center, Vector3 c, Vector3 normal, float radius, float spin)
        {
            if (normal.sqrMagnitude < 1e-8f) normal = Up;
            normal.Normalize();
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);
            Vector3 cc = c + normal * 0.01f;
            for (int k = 0; k < 5; k++)
            {
                float a0 = spin + k * Mathf.PI * 2f / 5f;
                float a1 = a0 + Mathf.PI / 5f, am = a0 - Mathf.PI / 5f;
                Vector3 tip = cc + (t1 * Mathf.Cos(a0) + t2 * Mathf.Sin(a0)) * radius;
                Vector3 r1 = cc + (t1 * Mathf.Cos(a1) + t2 * Mathf.Sin(a1)) * (radius * 0.42f);
                Vector3 r0 = cc + (t1 * Mathf.Cos(am) + t2 * Mathf.Sin(am)) * (radius * 0.42f);
                petal.Face3(cc, r0, tip, normal);
                petal.Face3(cc, tip, r1, normal);
            }
            if (center != null) center.Octa(cc + normal * 0.012f, radius * 0.22f);
        }

        // ------------------------------------------------------------------ palm
        /// Palm (art bible §1.9): 6–11 m, slender 8-sided trunk r 0.24 → 0.15 in 7–10 tapering segments with dark ring
        /// ledges, gentle S-curve leaning toward local +x, 7–9 broad toothed two-tone fronds (0.38–0.5 × H), 1–2 young
        /// fronds, 0–2 dry hanging fronds and a coconut cluster. 'lite' builds the same palm with ~1/5 of the triangles.
        public static PropModel Palm(int seed, bool lite = false)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = R(rng, 6f, 11f);
            float lean = R(rng, 0.18f, 0.35f) * H;
            float wig = R(rng, -0.07f, 0.07f) * H;
            int segs = Mathf.Clamp(Mathf.RoundToInt(H / 1.1f), 7, 10);
            float twist = R(rng, 0f, 40f);
            Vector3 Curve(float t) => new Vector3(lean * (2f * t - t * t) - 0.06f * lean * Mathf.Sin(t * Mathf.PI * 2f), H * t, wig * Mathf.Sin(t * Mathf.PI));

            // trunk
            var ta = mb.B(PropMats.PalmTrunk);
            var tb = mb.B(PropMats.PalmTrunkAlt);
            var ring = mb.B(PropMats.PalmRing);
            int ts = lite ? 4 : segs;
            int sides = lite ? 6 : 8;
            Vector3 top = Curve(1f), topDir = Up;
            for (int i = 0; i < ts; i++)
            {
                float t0 = (float)i / ts, t1 = (float)(i + 1) / ts;
                Vector3 a = Curve(t0), c = Curve(t1);
                Vector3 d = c - a;
                float l = d.magnitude;
                if (l < 1e-3f) continue;
                d /= l;
                if (i == 0) { a -= d * 0.6f; l += 0.6f; }
                float r0 = Mathf.Lerp(0.24f, 0.15f, t0) * (i == 0 ? 1.12f : 1f);
                float r1 = Mathf.Lerp(0.24f, 0.15f, t1) * (lite ? 1f : 0.92f);
                Quaternion q = Quaternion.FromToRotation(Up, d) * Quaternion.Euler(0f, twist + i * 17f, 0f);
                (i % 2 == 0 ? ta : tb).Cylinder(a, q, r0, r1, l + (lite ? 0.02f : 0f), sides, false, false);
                if (!lite && i > 0) ring.Cylinder(a, q, r0 * 1.035f, r0 * 1.01f, 0.08f, sides, false, false);
                top = c;
                topDir = d;
            }
            Quaternion topRot = Quaternion.FromToRotation(Up, topDir);
            ring.Cylinder(top - topDir * 0.05f, topRot, 0.17f, 0.07f, 0.42f, sides, false, true);
            Vector3 crown = top + topDir * 0.3f;

            // coconuts (random numbers are drawn in both detail levels so the crowns stay identical)
            int nuts = 3 + rng.Next(3);
            for (int i = 0; i < nuts; i++)
            {
                float a = R(rng, 0f, Mathf.PI * 2f);
                float drop = R(rng, 0.05f, 0.22f);
                float rx = R(rng, -30f, 30f), ry = R(rng, 0f, 360f);
                if (lite) continue;
                Vector3 np = top - Up * drop + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.24f;
                mb.B(PropMats.Coconut).Ico(np, new Vector3(0.14f, 0.15f, 0.14f), Quaternion.Euler(rx, ry, 0f), 0);
            }

            // crown
            var lt = mb.B(PropMats.PalmLeaf);
            var ld = mb.B(PropMats.PalmLeafDark);
            float L = Mathf.Clamp(R(rng, 0.38f, 0.5f) * H, 3.0f, 5.0f);
            int big = 7 + rng.Next(3);
            float az0 = R(rng, 0f, 360f);
            for (int f = 0; f < big; f++)
            {
                float az = az0 + f * 360f / big + R(rng, -12f, 12f);
                float e0 = R(rng, 16f, 32f), e1 = R(rng, -70f, -50f);
                float len = L * R(rng, 0.88f, 1.08f);
                // art polish: flatter and broader blades – with a 25-35° roof fold the lit top hid behind the
                // underside and the crowns read as thin dark feathers instead of How to Fish's broad olive fronds
                float fold = R(rng, 13f, 21f);
                int teeth = 6 + rng.Next(3);
                if (lite && f % 3 == 2) continue;
                PalmFrond(lt, ld, crown, az, e0, e1, len, len * 0.215f, fold, lite ? 3 : teeth, !lite);
            }
            int young = 1 + rng.Next(2);
            for (int f = 0; f < young; f++)
            {
                float az = R(rng, 0f, 360f);
                float e0 = R(rng, 58f, 72f), e1 = R(rng, 22f, 38f);
                if (lite && f > 0) continue;
                PalmFrond(lt, ld, crown + Up * 0.05f, az, e0, e1, L * 0.6f, L * 0.6f * 0.19f, 18f, lite ? 2 : 5, !lite);
            }
            int dry = rng.Next(3);
            var dm = mb.B(PropMats.PalmLeafDry);
            for (int f = 0; f < dry; f++)
            {
                float az = R(rng, 0f, 360f);
                float e0 = R(rng, -45f, -30f), e1 = R(rng, -88f, -72f);
                if (lite) continue;
                PalmFrond(dm, dm, crown - Up * 0.25f, az, e0, e1, L * 0.7f, L * 0.7f * 0.12f, 20f, 5, true);
            }
            return mb.Build("Palm" + seed + (lite ? "L" : ""), H + 1f, L);
        }

        // ------------------------------------------------------------------ broadleaf tree
        /// Broadleaf jungle tree (art bible §1.9): 8–14 m, trunk r 0.30–0.45 (6–8 sides) with root flares, 2–3 branches,
        /// canopy of 4–7 flattened faceted blobs in three foliage greens (light blob on top), a few hanging vines.
        public static PropModel BroadleafTree(int seed, bool lite = false)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = R(rng, 8f, 14f);
            float r0 = R(rng, 0.3f, 0.45f);
            Vector3 lean = new Vector3(R(rng, -0.7f, 0.7f), 0f, R(rng, -0.7f, 0.7f));
            int sides = lite ? 5 : 7;
            var bark = mb.B(PropMats.TreeBark);
            Vector3 b0 = new Vector3(0f, -0.5f, 0f), b1 = lean * 0.35f + Up * (H * 0.42f), b2 = lean + Up * (H * 0.62f);
            bark.Rod(b0, b1, r0, r0 * 0.78f, sides);
            bark.Rod(b1, b2, r0 * 0.78f, r0 * 0.5f, sides, true);
            float flareA = R(rng, 0f, 120f);
            if (!lite)
            {
                for (int k = 0; k < 3; k++)
                {
                    float a = (flareA + k * 120f) * Mathf.Deg2Rad;
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    bark.Rod(d * (r0 + 0.55f) - Up * 0.25f, d * 0.08f + Up * 1.1f, 0.13f, 0.05f, 4);
                }
            }

            // branches
            int nb = 2 + rng.Next(2);
            var tips = new List<Vector3>(4);
            float ba0 = R(rng, 0f, 360f);
            for (int k = 0; k < nb; k++)
            {
                float a = (ba0 + k * 360f / nb + R(rng, -25f, 25f)) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 s = Vector3.Lerp(b1, b2, R(rng, 0.2f, 0.7f));
                Vector3 e = s + d * R(rng, 1.8f, 2.8f) + Up * R(rng, 1.4f, 2.4f);
                tips.Add(e);
                if (!lite || k < 2) bark.Rod(s, e, r0 * 0.38f, r0 * 0.18f, lite ? 4 : 5, true);
            }

            // canopy blobs
            var greens = new[] { mb.B(PropMats.Foliage), mb.B(PropMats.FoliageDark), mb.B(PropMats.FoliageLight) };
            int nBlobs = 4 + rng.Next(4);
            Vector3 C = b2 + Up * (H * 0.1f);
            float scale = Mathf.Sqrt(H / 11f);
            var blobPos = new List<Vector4>(8);
            for (int k = 0; k < nBlobs; k++)
            {
                Vector3 p;
                float rad, ys;
                int g;
                if (k == 0) { p = C + Up * R(rng, 1.0f, 1.6f) * scale; rad = R(rng, 2.6f, 3.5f) * scale; ys = R(rng, 0.55f, 0.66f); g = 2; }
                else if (k - 1 < tips.Count) { p = tips[k - 1] + Up * R(rng, 0.3f, 0.8f); rad = R(rng, 2.0f, 2.9f) * scale; ys = R(rng, 0.55f, 0.7f); g = k % 2; }
                else
                {
                    float a = R(rng, 0f, Mathf.PI * 2f);
                    p = C + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(rng, 1.6f, 2.8f) * scale + Up * R(rng, -1.0f, 0.4f);
                    rad = R(rng, 2.0f, 3.0f) * scale; ys = R(rng, 0.55f, 0.7f); g = k % 2;
                }
                float yaw = R(rng, 0f, 360f);
                var jr = new System.Random(seed * 31 + k * 7 + 1);
                int detail = lite ? 0 : 1;
                if (lite && k > 3) continue;
                var radii = PropRocks.Jitter(jr, detail, 0.12f, 1, 0.8f);
                greens[g].Ico(p, new Vector3(rad, rad * ys, rad), Quaternion.Euler(0f, yaw, 0f), detail, radii);
                blobPos.Add(new Vector4(p.x, p.y, p.z, rad));
            }

            // hanging vines (full detail only)
            int vines = 2 + rng.Next(3);
            if (!lite)
            {
                var vb = mb.B(PropMats.Vine);
                for (int k = 0; k < vines && blobPos.Count > 1; k++)
                {
                    var bp = blobPos[1 + rng.Next(blobPos.Count - 1)];
                    float a = R(rng, 0f, Mathf.PI * 2f);
                    Vector3 start = new Vector3(bp.x, bp.y, bp.z) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * bp.w * 0.7f - Up * bp.w * 0.3f;
                    float len = R(rng, 1.5f, 3.5f);
                    Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * 0.04f;
                    Vector3 p = start;
                    for (int s = 0; s < 3; s++)
                    {
                        Vector3 q = p - Up * (len / 3f) + new Vector3(R(rng, -0.12f, 0.12f), 0f, R(rng, -0.12f, 0.12f));
                        vb.Quad2(p - side, p + side, q + side, q - side, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                        Vector3 leafDir = (s % 2 == 0 ? side : -side).normalized;
                        vb.Tri2(q, q + leafDir * 0.18f - Up * 0.06f, q - Up * 0.2f, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
                        p = q;
                    }
                }
            }
            return mb.Build("Tree" + seed + (lite ? "L" : ""), H + 2f, 4.5f * scale);
        }

        // ------------------------------------------------------------------ conifer
        /// Conifer (lighthouse hill / north cliffs): 9–16 m, dark trunk, 3–5 wide drooping tiers of flat two-tone cards.
        public static PropModel Conifer(int seed, bool lite = false)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = R(rng, 9f, 16f);
            int tiers = 3 + rng.Next(3);
            mb.B(PropMats.ConiferTrunk).Rod(new Vector3(0f, -0.5f, 0f), new Vector3(0f, H * 0.9f, 0f), 0.24f * H / 12f + 0.08f, 0.05f, lite ? 5 : 6);
            var top = mb.B(PropMats.ConiferLight);
            var under = mb.B(PropMats.Conifer);
            for (int k = 0; k < tiers; k++)
            {
                float t = tiers > 1 ? k / (float)(tiers - 1) : 0f;
                float y = Mathf.Lerp(0.24f, 0.84f, t) * H;
                float Rr = Mathf.Lerp(0.34f, 0.13f, t) * H * R(rng, 0.9f, 1.1f);
                float droop = Rr * R(rng, 0.45f, 0.6f);
                int cards = 7 + rng.Next(3);
                float a0 = R(rng, 0f, Mathf.PI * 2f);
                if (lite)
                {
                    // simple jagged cone (the card randoms are still drawn so every tier matches the full model)
                    for (int j = 0; j < cards; j++) { rng.NextDouble(); rng.NextDouble(); }
                    under.Cylinder(new Vector3(0f, y - droop, 0f), Quaternion.Euler(0f, a0 * Mathf.Rad2Deg, 0f), Rr, 0f, droop + Rr * 0.55f, 6, true, false);
                    continue;
                }
                Vector3 inner = new Vector3(0f, y + Rr * 0.3f, 0f);
                float half = Mathf.PI / cards * 1.05f;
                for (int j = 0; j < cards; j++)
                {
                    float a = a0 + j * Mathf.PI * 2f / cards;
                    float rr = Rr * R(rng, 0.88f, 1.08f);
                    float dr = droop * R(rng, 0.85f, 1.15f);
                    Vector3 l = new Vector3(Mathf.Cos(a - half) * rr * 0.86f, y - dr * 0.8f, Mathf.Sin(a - half) * rr * 0.86f);
                    Vector3 r = new Vector3(Mathf.Cos(a + half) * rr * 0.86f, y - dr * 0.8f, Mathf.Sin(a + half) * rr * 0.86f);
                    Vector3 tip = new Vector3(Mathf.Cos(a) * rr * 1.05f, y - dr * 1.1f, Mathf.Sin(a) * rr * 1.05f);
                    Vector3 mid = new Vector3(Mathf.Cos(a) * rr * 0.5f, y - dr * 0.25f + Rr * 0.08f, Mathf.Sin(a) * rr * 0.5f);
                    TwoTone(top, under, inner, mid, l);
                    TwoTone(top, under, inner, r, mid);
                    TwoTone(top, under, mid, tip, l);
                    TwoTone(top, under, mid, r, tip);
                }
            }
            if (!lite) top.Cylinder(new Vector3(0f, H * 0.84f, 0f), Quaternion.identity, H * 0.05f, 0f, H * 0.17f, 5, true, false);
            return mb.Build("Conifer" + seed + (lite ? "L" : ""), H, H * 0.36f);
        }

        // ------------------------------------------------------------------ undergrowth
        /// Faceted bush 0.8–1.8 m: 2–4 overlapping blobs in the foliage greens; flowerKind 1 magenta (bougainvillea),
        /// 2 pink, 3 white puts 10–16 flat star flowers on the upper half.
        public static PropModel Bush(int seed, int flowerKind)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float Rb = R(rng, 0.55f, 1.0f);
            int n = 2 + rng.Next(3);
            Material[] greens = { PropMats.FoliageDark, PropMats.Foliage, PropMats.FoliageLight };
            int g0 = rng.Next(2);
            float maxH = 0f;
            var centers = new List<Vector4>(4);
            for (int k = 0; k < n; k++)
            {
                Vector2 o = k == 0 ? Vector2.zero : PropUtil.InDisc(rng, Rb * 0.6f);
                float s = Rb * (k == 0 ? 1f : R(rng, 0.55f, 0.8f));
                Vector3 c = new Vector3(o.x, s * 0.7f, o.y);
                int detail = s > 0.7f ? 1 : 0;
                var radii = PropRocks.Jitter(rng, detail, 0.12f, 1, 0.7f);
                var mat = greens[k == n - 1 ? 2 : (g0 + k) % 2];
                mb.B(mat).Ico(c, new Vector3(s, s * 0.85f, s), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), detail, radii);
                maxH = Mathf.Max(maxH, c.y + s * 0.85f);
                centers.Add(new Vector4(c.x, c.y, c.z, s));
            }
            if (flowerKind > 0)
            {
                Material fm = flowerKind == 1 ? PropMats.FlowerMagenta : flowerKind == 2 ? PropMats.FlowerPink : PropMats.FlowerWhite;
                var pb = mb.B(fm);
                var cb = flowerKind == 3 ? mb.B(PropMats.FlowerYellow) : null;
                int count = 10 + rng.Next(7);
                for (int i = 0; i < count; i++)
                {
                    var c = centers[rng.Next(centers.Count)];
                    Vector3 d = new Vector3(R(rng, -1f, 1f), R(rng, 0.15f, 1f), R(rng, -1f, 1f));
                    if (d.sqrMagnitude < 1e-4f) d = Up;
                    d.Normalize();
                    Vector3 p = new Vector3(c.x, c.y, c.z) + Vector3.Scale(d, new Vector3(c.w, c.w * 0.85f, c.w)) * 0.97f;
                    StarFlower(pb, cb, p, d, R(rng, 0.07f, 0.1f), R(rng, 0f, 6.28f));
                }
            }
            return mb.Build("Bush" + seed, maxH, Rb);
        }

        /// Fern 0.6–1.2 m: 7–10 leaflet fronds, two-tone.
        public static PropModel Fern(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var top = mb.B(PropMats.Foliage);
            var under = mb.B(PropMats.PalmLeafDark);
            int n = 7 + rng.Next(4);
            float size = R(rng, 0.6f, 1.2f);
            for (int i = 0; i < n; i++)
            {
                float az = i * 360f / n + R(rng, -15f, 15f);
                float len = size * R(rng, 0.8f, 1.15f);
                LeafletFrond(top, under, new Vector3(0f, 0.04f, 0f), az, R(rng, 48f, 66f), R(rng, -35f, -10f), len, len * 0.16f, 6);
            }
            return mb.Build("Fern" + seed, size * 0.8f, size);
        }

        /// Taro / elephant ear: 4–7 big heart leaves (0.6–1.4 m) on curved stems, top #6E9445, underside #4E6F2E.
        public static PropModel Taro(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var top = mb.B(PropMats.Grass);
            var under = mb.B(PropMats.PalmLeafDark);
            var stem = mb.B(PropMats.Flat(PropPal.GrassMid));
            int n = 4 + rng.Next(4);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float az = (i * 360f / n + R(rng, -20f, 20f)) * Mathf.Deg2Rad;
                Vector3 hor = new Vector3(Mathf.Cos(az), 0f, Mathf.Sin(az));
                float h = R(rng, 0.6f, 1.15f);
                Vector3 tip = hor * R(rng, 0.25f, 0.5f) + Up * h;
                stem.Rod(Vector3.zero, Vector3.Lerp(Vector3.zero, tip, 0.55f) + Up * 0.08f, 0.035f, 0.028f, 4);
                stem.Rod(Vector3.Lerp(Vector3.zero, tip, 0.55f) + Up * 0.08f, tip, 0.028f, 0.02f, 4);
                float len = R(rng, 0.6f, 1.4f);
                BroadLeaf(top, under, tip, hor, len, len * R(rng, 0.65f, 0.8f), R(rng, 0f, 18f), R(rng, 30f, 55f), true);
                maxH = Mathf.Max(maxH, h + 0.2f);
            }
            return mb.Build("Taro" + seed, maxH, 1f);
        }

        /// Banana plant 2.5–4 m: pseudo-stem #8A8F4E and 5–7 long arching paddle leaves (two-tone, a little torn).
        public static PropModel Banana(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = R(rng, 2.5f, 4f);
            mb.B(PropMats.BananaStem).Rod(new Vector3(0f, -0.2f, 0f), new Vector3(0f, H, 0f), 0.15f, 0.09f, 6, true);
            var top = mb.B(PropMats.FoliageLight);
            var under = mb.B(PropMats.PalmLeafDark);
            int n = 5 + rng.Next(3);
            float a0 = R(rng, 0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float az = a0 + i * 360f / n + R(rng, -18f, 18f);
                float len = R(rng, 1.6f, 2.4f);
                float w = R(rng, 0.22f, 0.3f);
                float ar = az * Mathf.Deg2Rad;
                Vector3 hor = new Vector3(Mathf.Cos(ar), 0f, Mathf.Sin(ar));
                Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
                const int K = 6;
                Vector3 p = new Vector3(0f, H - 0.1f, 0f);
                float e0 = R(rng, 45f, 65f), e1 = R(rng, -40f, -15f);
                int tear = 1 + rng.Next(K - 2);
                for (int s = 0; s < K; s++)
                {
                    float t = (s + 0.5f) / K;
                    float e = Mathf.Lerp(e0, e1, t) * Mathf.Deg2Rad;
                    Vector3 d = hor * Mathf.Cos(e) + Up * Mathf.Sin(e);
                    Vector3 q = p + d * (len / K);
                    float ww0 = w * (s == 0 ? 0.35f : 1f), ww1 = w * (s == K - 1 ? 0.15f : 1f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (s == tear && side == 1) continue;   // a torn gap
                        Vector3 e0p = p + lat * (side * ww0) - Up * 0.04f, e1p = q + lat * (side * ww1) - Up * 0.04f;
                        TwoToneQuad(top, under, p, q, e1p, e0p);
                    }
                    p = q;
                }
            }
            return mb.Build("Banana" + seed, H + 1f, 2f);
        }

        /// Group of 3–9 meadow flowers on 0.3–0.6 m stems (flat star heads).
        public static PropModel Flowers(int seed, Material petal)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var stem = mb.B(PropMats.Flat(PropPal.GrassMid));
            var pb = mb.B(petal);
            var cb = mb.B(PropMats.FlowerYellow);
            bool yellow = petal == PropMats.FlowerYellow;
            int n = 3 + rng.Next(7);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.35f);
                float h = R(rng, 0.3f, 0.6f);
                Vector3 b = new Vector3(o.x, -0.03f, o.y);
                Vector3 t = b + new Vector3(R(rng, -0.06f, 0.06f), h, R(rng, -0.06f, 0.06f));
                Vector3 side = new Vector3(0.012f, 0f, 0.008f);
                stem.Tri2(b - side, b + side, t, Vector3.forward);
                Vector3 nrm = new Vector3(R(rng, -0.4f, 0.4f), 1f, R(rng, -0.4f, 0.4f));
                StarFlower(pb, yellow ? null : cb, t, nrm, R(rng, 0.06f, 0.09f), R(rng, 0f, 6.28f));
                maxH = Mathf.Max(maxH, t.y);
            }
            return mb.Build("Flowers" + seed, maxH, 0.4f);
        }

        /// Small tuft of 3–5 blades in a flat material (used on mini islands' beaches and as accents).
        public static PropModel Tuft(int seed, Material blade)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var b = mb.B(blade);
            int n = 4 + rng.Next(3);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.14f);
                Vector3 bp = new Vector3(o.x, -0.03f, o.y);
                float a = R(rng, 0f, Mathf.PI * 2f);
                Vector3 outDir = o.sqrMagnitude > 1e-6f ? new Vector3(o.x, 0f, o.y).normalized : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float h = R(rng, 0.3f, 0.6f);
                float w = R(rng, 0.05f, 0.08f);
                Vector3 side = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * w;
                Vector3 tip = bp + Up * h + outDir * (h * R(rng, 0.15f, 0.45f));
                Vector3 n0 = Vector3.Cross(side, tip - bp);
                if (n0.sqrMagnitude < 1e-10f) n0 = outDir;
                b.Tri2(bp - side, bp + side, tip, n0.normalized);
                maxH = Mathf.Max(maxH, tip.y);
            }
            return mb.Build("Tuft" + seed, maxH, 0.25f);
        }
    }
}
