using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Blue-water drop-off (chunky wall-edge rock formations, sea whips, sparse coral, boulders) and the deep trench
    /// (faceted basalt spires, black-smoker vents with glowing mouths, a chunky whale skeleton, bioluminescent specks).
    /// Also a generic fallback for unknown sites. Low-poly flat colors; the glowing bits stay emissive.
    public static class PropDeep
    {
        static float G(float x, float z) => PropGround.H(x, z);

        // ================================================================== blue drop
        public static IEnumerator BuildDropOff(Transform decor, DiveSite site)
        {
            var rng = new System.Random(6060);
            var batch = new PropBatch(64f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);
            Vector3 c = site.center;
            float R = site.radius;

            // find the wall: steep cells inside the site
            var steep = new List<Vector3>();
            const float step = 6f;
            for (float x = -R; x <= R; x += step)
            {
                for (float z = -R; z <= R; z += step)
                {
                    if (x * x + z * z > R * R) continue;
                    float wx = c.x + x + PropUtil.Range(rng, -2f, 2f), wz = c.z + z + PropUtil.Range(rng, -2f, 2f);
                    float g = G(wx, wz);
                    if (-g < 18f || -g > 95f) continue;
                    if (PropGround.Slope(wx, wz, 3f) > 0.65f) steep.Add(new Vector3(wx, g, wz));
                }
                if (PropRun.Due()) yield return null;
            }
            // prefer the upper part of the wall (shallow side first), with some randomness (stable precomputed keys)
            var keys = new float[steep.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = -(steep[i].y + (float)rng.NextDouble() * 12f);
            var arr = steep.ToArray();
            System.Array.Sort(keys, arr);
            steep.Clear();
            steep.AddRange(arr);
            var spacing = new PropExclusion(16f);
            int formations = 0;
            foreach (var p in steep)
            {
                if (formations >= 40) break;
                if (spacing.Blocked(p.x, p.z)) continue;
                Formation(batch, cols, rng, p);
                spacing.Add(p.x, p.z, 13f);
                formations++;
                if (PropRun.Due()) yield return null;
            }
            // fallback ring when the terrain has no clear wall
            for (int i = 0; formations < 24 && i < 200; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f), d = PropUtil.Range(rng, 0.35f, 0.9f) * R;
                float x = c.x + Mathf.Cos(a) * d, z = c.z + Mathf.Sin(a) * d;
                if (spacing.Blocked(x, z)) continue;
                float g = G(x, z);
                if (-g < 18f) continue;
                Formation(batch, cols, rng, new Vector3(x, g, z));
                spacing.Add(x, z, 13f);
                formations++;
            }

            // big boulders
            for (int i = 0; i < 32; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, R);
                float x = c.x + o.x, z = c.z + o.y;
                float g = G(x, z);
                if (-g < 20f || spacing.Blocked(x, z, -6f)) continue;
                float s = PropUtil.Range(rng, 2f, 5.5f);
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 1f, 1.4f), s * PropUtil.Range(rng, 0.6f, 1f), s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion r = Quaternion.Euler(PropUtil.Range(rng, -15f, 15f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -15f, 15f));
                var mesh = PropRocks.Mesh(sc, rng.Next(), 1, 0.16f);
                batch.Add(mesh, i % 3 == 0 ? PropMats.RockDark : PropMats.Rock, Matrix4x4.TRS(p, r, Vector3.one));
                PropCol.Mesh(cols, mesh, p, r, Vector3.one, "Boulder");
                if (PropRun.Due()) yield return null;
            }

            // sea whips (on the wall and the plateau) and sparse deep coral
            int whips = 0, corals = 0, attempts = 0;
            while ((whips < 150 || corals < 70) && attempts < 5000)
            {
                attempts++;
                Vector3 p;
                if (steep.Count > 0 && rng.NextDouble() < 0.55)
                {
                    var s = steep[rng.Next(steep.Count)];
                    float x = s.x + PropUtil.Range(rng, -6f, 6f), z = s.z + PropUtil.Range(rng, -6f, 6f);
                    p = new Vector3(x, G(x, z), z);
                }
                else
                {
                    Vector2 o = PropUtil.InDisc(rng, R);
                    p = new Vector3(c.x + o.x, G(c.x + o.x, c.z + o.y), c.z + o.y);
                }
                if (-p.y < 26f || -p.y > 85f) continue;
                Vector3 n = PropGround.Normal(p.x, p.z, 1f);
                if (whips < 150 && rng.NextDouble() < 0.65)
                {
                    if (PropReef.Place(batch, rng, p, n, PropCoralType.Whip, PropUtil.Range(rng, 0.9f, 1.4f), -20f)) whips++;
                }
                else if (corals < 70)
                {
                    if (PropReef.Place(batch, rng, p, n, PropReef.RandomType(rng, 1, 1, 2, 3, 3, 0), 0.9f, -20f)) corals++;
                }
                if (PropRun.Due()) yield return null;
            }

            PropReef.QuietShadows(batch);
            batch.Flush(decor, "DropOff", ShadowCastingMode.On, 220f);
        }

        static void Formation(PropBatch batch, Transform cols, System.Random rng, Vector3 p)
        {
            int n = 2 + rng.Next(4);
            float s0 = PropUtil.Range(rng, 2.5f, 6.5f);
            Material mat = rng.NextDouble() < 0.5 ? PropMats.Rock : PropMats.RockDark;
            for (int i = 0; i < n; i++)
            {
                float s = s0 * (i == 0 ? 1f : PropUtil.Range(rng, 0.4f, 0.8f));
                Vector3 off = Vector3.zero;
                if (i > 0)
                {
                    Vector2 o = PropUtil.InDisc(rng, s0 * 0.7f);
                    off = new Vector3(o.x, s0 * PropUtil.Range(rng, 0.2f, 0.9f), o.y);
                }
                bool pillar = rng.NextDouble() < 0.3;
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 0.8f, 1.3f), s * (pillar ? PropUtil.Range(rng, 1.4f, 2.2f) : PropUtil.Range(rng, 0.6f, 1.1f)), s * PropUtil.Range(rng, 0.8f, 1.2f));
                Vector3 pos = p + off;
                float g = G(pos.x, pos.z);
                if (pos.y < g) pos.y = g;
                pos.y -= sc.y * 0.25f;
                Quaternion r = Quaternion.Euler(PropUtil.Range(rng, -12f, 12f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -12f, 12f));
                var mesh = PropRocks.Mesh(sc, rng.Next(), 1, 0.16f);
                batch.Add(mesh, mat, Matrix4x4.TRS(pos, r, Vector3.one));
                if (s > 1.8f) PropCol.Mesh(cols, mesh, pos, r, Vector3.one, "WallRock");
                // a sea fan or sponge clinging to the formation
                if (rng.NextDouble() < 0.6)
                {
                    Vector3 fp = pos + new Vector3(PropUtil.Range(rng, -0.3f, 0.3f) * sc.x, sc.y * 0.7f, PropUtil.Range(rng, -0.3f, 0.3f) * sc.z);
                    PropReef.Place(batch, rng, fp, Vector3.up, rng.NextDouble() < 0.5 ? PropCoralType.Fan : PropCoralType.Sponge, 1.1f, -20f);
                }
            }
        }

        // ================================================================== deep trench
        public static IEnumerator BuildTrench(Transform decor, DiveSite site)
        {
            var rng = new System.Random(9090);
            var batch = new PropBatch(64f);
            var glow = new PropBatch(64f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);
            Vector3 c = site.center;
            float R = site.radius;
            var spacing = new PropExclusion(16f);

            // ---------------------------------------------------------------- whale skeleton (needs a flat spot)
            Vector3 best = new Vector3(c.x, G(c.x, c.z), c.z);
            float bestSlope = float.MaxValue;
            for (int i = 0; i < 30; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, R * 0.45f);
                float x = c.x + o.x, z = c.z + o.y;
                float sl = PropGround.Slope(x, z, 4f);
                if (sl < bestSlope) { bestSlope = sl; best = new Vector3(x, G(x, z), z); }
            }
            float wyaw = PropUtil.Range(rng, 0f, 360f);
            Matrix4x4 wtrs = Matrix4x4.TRS(best + Vector3.up * 0.05f, Quaternion.Euler(0f, wyaw, 0f) * Quaternion.Euler(0f, 0f, 9f), Vector3.one);
            var skel = new PropMeshBuilder();
            WhaleSkeleton(skel, rng);
            batch.B(PropMats.Bone, best).Append(skel, wtrs);
            PropCol.Capsule(cols, wtrs.MultiplyPoint3x4(new Vector3(0f, 0.7f, 8f)), 1.0f, 5f, Quaternion.Euler(0f, wyaw, 0f) * Quaternion.Euler(90f, 0f, 0f));
            PropInfo.WhaleSkeleton = best;
            spacing.Add(best.x, best.z, 16f);
            if (PropRun.Due()) yield return null;

            // ---------------------------------------------------------------- hydrothermal vents
            int vents = 0, attempts = 0;
            while (vents < 6 && attempts < 400)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, R * 0.6f);
                float x = c.x + o.x, z = c.z + o.y;
                float g = G(x, z);
                if (-g < 70f || spacing.Blocked(x, z) || PropGround.Slope(x, z, 2f) > 0.6f) continue;
                Vent(decor, batch, glow, cols, rng, new Vector3(x, g, z));
                spacing.Add(x, z, 22f);
                vents++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- basalt spires
            int spires = 0;
            attempts = 0;
            var spirePos = new List<Vector3>();
            while (spires < 40 && attempts < 1200)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, R * 0.97f);
                float x = c.x + o.x, z = c.z + o.y;
                float g = G(x, z);
                if (-g < 60f || spacing.Blocked(x, z)) continue;
                float h = PropUtil.Range(rng, 10f, 34f);
                h = Mathf.Min(h, -g - 25f);
                if (h < 8f) continue;
                float br = PropUtil.Range(rng, 2.5f, 6.5f);
                var sb = new PropMeshBuilder();
                Spire(sb, h, br, rng.Next());
                var mesh = sb.ToMesh("Spire");
                LowPoly.Sanitize(mesh);
                Vector3 p = new Vector3(x, g - 1.2f, z);
                Quaternion r = Quaternion.Euler(PropUtil.Range(rng, -6f, 6f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -6f, 6f));
                batch.Add(mesh, spires % 3 == 0 ? PropMats.Chimney : PropMats.Basalt, Matrix4x4.TRS(p, r, Vector3.one));
                PropCol.Mesh(cols, mesh, p, r, Vector3.one, "Spire");
                spacing.Add(x, z, br + 4f);
                spirePos.Add(new Vector3(x, g, z));
                spires++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- basalt boulders
            for (int i = 0; i < 45; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, R);
                float x = c.x + o.x, z = c.z + o.y;
                if (spacing.Blocked(x, z)) continue;
                float g = G(x, z);
                if (-g < 50f) continue;
                float s = PropUtil.Range(rng, 0.6f, 3.2f);
                Vector3 sc = new Vector3(s * 1.3f, s * 0.75f, s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion r = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                if (s > 1.8f)
                {
                    var mesh = PropRocks.Mesh(sc, rng.Next(), 1, 0.2f);
                    batch.Add(mesh, PropMats.Basalt, Matrix4x4.TRS(p, r, Vector3.one));
                    PropCol.Mesh(cols, mesh, p, r, Vector3.one, "Basalt");
                }
                else PropRocks.Build(batch.B(PropMats.Basalt, p), p, sc, r, rng.Next(), s > 1f ? 1 : 0, 0.2f);
            }

            // ---------------------------------------------------------------- bioluminescent specks
            int specks = 0;
            attempts = 0;
            while (specks < 1800 && attempts < 6000)
            {
                attempts++;
                Vector3 anchor;
                double k = rng.NextDouble();
                if (k < 0.45 && spirePos.Count > 0) anchor = spirePos[rng.Next(spirePos.Count)];
                else if (k < 0.65) anchor = best;
                else { Vector2 o0 = PropUtil.InDisc(rng, R); anchor = new Vector3(c.x + o0.x, 0f, c.z + o0.y); }
                Vector2 o = PropUtil.InDisc(rng, 9f);
                float x = anchor.x + o.x, z = anchor.z + o.y;
                float g = G(x, z);
                if (-g < 55f) continue;
                Vector3 p = new Vector3(x, g + PropUtil.Range(rng, 0.03f, 1f) * (rng.NextDouble() < 0.2 ? 6f : 1f), z);
                glow.B(rng.NextDouble() < 0.7 ? PropMats.Bio : PropMats.BioGreen, p).Octa(p, PropUtil.Range(rng, 0.025f, 0.07f));
                specks++;
            }
            // drifting glow clouds
            for (int i = 0; i < 6; i++)
            {
                Vector3 anchor = i == 0 ? best : (spirePos.Count > 0 ? spirePos[rng.Next(spirePos.Count)] : c);
                float g = G(anchor.x, anchor.z);
                PropFX.Specks(decor, new Vector3(anchor.x, g + 4f, anchor.z), 7f, 140, i % 2 == 0 ? new Color(0.3f, 0.85f, 1f) : new Color(0.45f, 1f, 0.55f));
            }

            batch.Flush(decor, "Trench", ShadowCastingMode.On, 200f);
            if (PropRun.Due()) yield return null;
            glow.Flush(decor, "TrenchGlow", ShadowCastingMode.Off, 90f, false);
        }

        /// Faceted tapered spire: 6-sided rings with jittered radii (no smooth normals), local base at the origin.
        static void Spire(PropMeshBuilder b, float h, float baseR, int seed)
        {
            const int K = 7, sides = 6;
            var prof = new Vector2[K];
            float ph = (seed % 100) * 0.1f;
            for (int i = 0; i < K; i++)
            {
                float t = (float)i / (K - 1);
                float r = baseR * Mathf.Pow(1f - t, 0.85f) * (1f + 0.14f * Mathf.Sin(t * 7f + ph)) + 0.15f;
                prof[i] = new Vector2(i == K - 1 ? 0f : r, t * h);
            }
            b.Lathe(Vector3.zero, Quaternion.identity, prof, sides, 1f, 1f, (a, t) =>
            {
                int ai = Mathf.RoundToInt(a * sides) % sides;
                int ti = Mathf.RoundToInt(t * (K - 1));
                return 1f + 0.3f * (PropNoise.Hash01(ai, ti, seed) - 0.5f) * 2f;
            });
        }

        static void Vent(Transform decor, PropBatch batch, PropBatch glow, Transform cols, System.Random rng, Vector3 p)
        {
            PropRocks.Build(batch.B(PropMats.Basalt, p), p - Vector3.up * 0.7f, new Vector3(2.8f, 1.3f, 2.5f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 1, 0.2f);
            float r = PropUtil.Range(rng, 0.75f, 1.0f);
            float total = PropUtil.Range(rng, 3.5f, 7.5f);
            const int segs = 4;
            Vector3 cur = p + Vector3.up * 0.2f;
            var chim = batch.B(PropMats.Chimney, p);
            for (int i = 0; i < segs; i++)
            {
                float h = total / segs;
                float rTop = r * PropUtil.Range(rng, 0.72f, 0.88f);
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -7f, 7f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -7f, 7f));
                chim.Cylinder(cur, q, r * PropUtil.Range(rng, 1.0f, 1.15f), rTop, h, 6, false, i == segs - 1);
                // lumpy mineral crust
                if (rng.NextDouble() < 0.7)
                    PropRocks.Build(chim, cur + q * new Vector3(r * 0.7f, h * 0.5f, 0f), new Vector3(r * 0.5f, h * 0.4f, r * 0.5f), q, rng.Next(), 0, 0.2f);
                cur += q * new Vector3(0f, h, 0f);
                r = rTop;
            }
            Vector3 top = cur;
            glow.B(PropMats.VentGlow, top).Torus(top - Vector3.up * 0.05f, Quaternion.identity, r * 0.75f, 0.11f, 6, 3);
            glow.B(PropMats.VentGlow, top).Disk(top + Vector3.up * 0.01f, r * 0.7f, 6, Quaternion.identity, true);
            PropCol.Capsule(cols, (p + top) * 0.5f, r + 0.15f, (top - p).magnitude + 0.5f, Quaternion.identity);

            // tube worms around the base (white tubes, glowing red plumes)
            for (int i = 0; i < 24; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f), d = PropUtil.Range(rng, 1.6f, 3f);
                float x = p.x + Mathf.Cos(a) * d, z = p.z + Mathf.Sin(a) * d;
                float g = G(x, z);
                float h = PropUtil.Range(rng, 0.35f, 1.2f);
                Vector3 b0 = new Vector3(x, g - 0.1f, z);
                Vector3 b1 = b0 + new Vector3(PropUtil.Range(rng, -0.12f, 0.12f), h, PropUtil.Range(rng, -0.12f, 0.12f));
                batch.B(PropMats.TubeWorm, b0).Rod(b0, b1, 0.045f, 0.04f, 4);
                glow.B(PropMats.WormPlume, b0).Octa(b1 + Vector3.up * 0.07f, new Vector3(0.07f, 0.1f, 0.07f));
            }

            var light = PropUtil.AddPointLight(decor, top + Vector3.up * 0.7f, new Color(1f, 0.45f, 0.15f), 11f, 2.4f, "VentGlow");
            light.renderMode = LightRenderMode.ForcePixel;
            PropFX.Bubbles(decor, top + Vector3.up * 0.1f, 9f, 0.25f);
            PropFX.Smoke(decor, top + Vector3.up * 0.2f);
            PropInfo.VentPositions.Add(top);
        }

        /// Chunky ~20 m whale skeleton lying on the seabed. Local: z = body axis (skull at +z), y up, origin on the ground.
        static void WhaleSkeleton(PropMeshBuilder b, System.Random rng)
        {
            // spine: vertebrae as short 6-sided drums with little spikes
            var spine = new List<Vector3>();
            for (float z = -10f; z <= 6f; z += 0.55f)
            {
                float y = z < -4f ? Mathf.Lerp(-0.25f, 0.85f, (z + 10f) / 6f) : 0.85f + 0.1f * Mathf.Sin(z * 0.3f);
                spine.Add(new Vector3(Mathf.Sin(z * 0.22f) * 0.7f, y, z));
            }
            for (int i = 0; i < spine.Count; i++)
            {
                Vector3 p = spine[i];
                Vector3 dir = (i + 1 < spine.Count ? spine[i + 1] - p : p - spine[i - 1]);
                dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
                float t = (p.z + 10f) / 16f;
                float vr = Mathf.Lerp(0.14f, 0.3f, t);
                b.Rod(p - dir * 0.16f, p + dir * 0.16f, vr, vr, 6, true);
                Vector3 upv = Vector3.Cross(dir, Vector3.right);
                upv = upv.sqrMagnitude > 1e-6f ? upv.normalized : Vector3.up;
                if (upv.y < 0f) upv = -upv;
                b.Cylinder(p, Quaternion.FromToRotation(Vector3.up, upv), 0.08f, 0f, Mathf.Lerp(0.3f, 0.65f, t), 4);
                if (t > 0.15f && t < 0.75f)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, dir);
                    side = side.sqrMagnitude > 1e-6f ? side.normalized : Vector3.right;
                    b.Rod(p, p + side * 0.5f * t, 0.06f, 0.03f, 4);
                    b.Rod(p, p - side * 0.5f * t, 0.06f, 0.03f, 4);
                }
            }
            // ribs: 4-sided arches
            var pts = new Vector3[4];
            var rad = new[] { 0.1f, 0.09f, 0.075f, 0.06f };
            for (int k = 0; k < 12; k++)
            {
                float z = 5.2f - k * 0.6f;
                int si = Mathf.Clamp(Mathf.RoundToInt((z + 10f) / 0.55f), 0, spine.Count - 1);
                Vector3 sp = spine[si];
                float len = Mathf.Lerp(1f, 0.55f, k / 11f);
                for (int s = -1; s <= 1; s += 2)
                {
                    if (rng.NextDouble() < 0.14) continue;
                    bool fallen = rng.NextDouble() < 0.12;
                    if (!fallen)
                    {
                        pts[0] = sp + new Vector3(s * 0.2f, 0f, 0f);
                        pts[1] = sp + new Vector3(s * 1.3f * len, 0.25f, -0.15f);
                        pts[2] = new Vector3(sp.x + s * 1.8f * len, 0.55f, sp.z - 0.4f);
                        pts[3] = new Vector3(sp.x + s * 1.5f * len, -0.15f, sp.z - 0.55f);
                    }
                    else
                    {
                        float zz = sp.z + PropUtil.Range(rng, -1f, 1f);
                        float x0 = sp.x + s * PropUtil.Range(rng, 2f, 3.2f);
                        for (int q = 0; q < 4; q++) pts[q] = new Vector3(x0 + s * q * 0.6f, 0.06f + Mathf.Sin(q * 0.9f) * 0.15f, zz + q * 0.4f);
                    }
                    b.Tube(pts, rad, 4, 1f, 1f, true);
                }
            }
            // skull: faceted cranium + 6-sided rostrum cone
            var radii = PropRocks.Jitter(rng, 1, 0.08f, 1, 0.7f);
            b.Ico(new Vector3(0f, 0.75f, 7.2f), new Vector3(1.35f, 0.75f, 1.3f), Quaternion.identity, 1, radii);
            b.Cylinder(new Vector3(0f, 0.55f, 8f), Quaternion.Euler(87f, 0f, 0f), 0.9f, 0.2f, 4.2f, 6, false, true);
            // jaw bones (one displaced)
            var jaw = new Vector3[4];
            var jr = new[] { 0.16f, 0.14f, 0.12f, 0.09f };
            for (int s = -1; s <= 1; s += 2)
            {
                bool moved = s > 0;
                for (int q = 0; q < 4; q++)
                {
                    float t = q / 3f;
                    float x = s * (0.9f + Mathf.Sin(t * Mathf.PI) * 0.6f) + (moved ? s * 1.4f : 0f);
                    jaw[q] = new Vector3(x, 0.12f + Mathf.Sin(t * Mathf.PI) * 0.15f, 6.2f + t * 5.2f + (moved ? -1.2f : 0f));
                }
                b.Tube(jaw, jr, 5, 1f, 1f, true);
            }
            // flipper bones on the ground
            for (int i = 0; i < 5; i++)
            {
                Vector3 a = new Vector3(2.4f + i * 0.3f, 0.06f, 3.6f - i * 0.35f);
                b.Rod(a, a + new Vector3(0.6f, 0.02f, -0.15f), 0.07f, 0.05f, 4, true);
            }
        }

        // ================================================================== fallback
        public static IEnumerator BuildGeneric(Transform decor, DiveSite site)
        {
            var rng = new System.Random(site.id.GetHashCode());
            var batch = new PropBatch(64f);
            for (int i = 0; i < 60; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = site.center.x + o.x, z = site.center.z + o.y;
                float g = G(x, z);
                if (-g < 4f) continue;
                float s = PropUtil.Range(rng, 0.5f, 3f);
                Vector3 p = new Vector3(x, g - s * 0.2f, z);
                PropRocks.Build(batch.B(PropMats.Rock, p), p, new Vector3(s * 1.2f, s * 0.7f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), s > 1.2f ? 1 : 0);
                if (rng.NextDouble() < 0.5) PropReef.Place(batch, rng, new Vector3(x + 1.5f, G(x + 1.5f, z), z), Vector3.up, PropCoralType.Whip, 1f, -3f);
                if (PropRun.Due()) yield return null;
            }
            batch.Flush(decor, "Generic", ShadowCastingMode.On, 160f);
        }
    }
}
