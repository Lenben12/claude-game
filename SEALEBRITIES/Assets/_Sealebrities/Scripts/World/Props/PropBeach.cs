using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Beach life between hut and dock, the campfire, island paths (with tiki torches) and the hidden bay.
    public static class PropBeach
    {
        static float G(float x, float z) => PropGround.H(x, z);

        /// Spiral search around "desired" for ground within [minG, maxG] that is not excluded.
        public static bool FindLand(Vector3 desired, float minG, float maxG, PropExclusion excl, out Vector3 pos, float searchR = 18f)
        {
            for (float r = 0f; r <= searchR; r += 1.25f)
            {
                int n = r < 0.1f ? 1 : Mathf.CeilToInt(2f * Mathf.PI * r / 1.25f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n + r * 0.37f;
                    float x = desired.x + Mathf.Cos(a) * r, z = desired.z + Mathf.Sin(a) * r;
                    float g = G(x, z);
                    if (g < minG || g > maxG) continue;
                    if (excl != null && excl.Blocked(x, z)) continue;
                    pos = new Vector3(x, g, z);
                    return true;
                }
            }
            pos = new Vector3(desired.x, G(desired.x, desired.z), desired.z);
            return false;
        }

        // ================================================================== beach props
        public static IEnumerator Build(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("Beach", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(909);

            // two loungers with a parasol, facing the sea (east)
            if (FindLand(new Vector3(146f, 0f, 24f), 0.35f, 8f, excl, out var l1))
            {
                Lounger(batch, cols, l1, -90f + PropUtil.Range(rng, -8f, 8f));
                excl.Add(l1.x, l1.z, 2f);
                Vector3 l2d = l1 + new Vector3(0.6f, 0f, 3.2f);
                if (FindLand(l2d, 0.35f, 8f, excl, out var l2, 4f))
                {
                    Lounger(batch, cols, l2, -90f + PropUtil.Range(rng, -10f, 10f));
                    excl.Add(l2.x, l2.z, 2f);
                }
                Vector3 pp = l1 + new Vector3(-0.6f, 0f, 1.6f);
                pp.y = G(pp.x, pp.z);
                Parasol(batch, cols, pp);
            }

            // surfboard stuck in the sand
            if (FindLand(new Vector3(140f, 0f, 12.5f), 0.3f, 8f, excl, out var sp))
            {
                Quaternion r = Quaternion.Euler(0f, 30f, 0f) * Quaternion.Euler(-74f, 0f, 0f);
                Vector3 c = sp + Vector3.up * 0.8f;
                batch.B(PropMats.Surfboard).Blob(c, new Vector3(0.27f, 0.035f, 1.05f), r, 5, null, 0.8f);
                batch.B(PropMats.PlasticWhite).Box(c + r * new Vector3(0f, -0.07f, -0.8f), new Vector3(0.015f, 0.12f, 0.16f), r);
                PropCol.Box(cols, c, new Vector3(0.55f, 0.1f, 2.1f), r);
                excl.Add(sp.x, sp.z, 1.2f);
            }

            // oil drums beside the hut
            if (FindLand(new Vector3(WorldLayout.Hut.x + 3f, 0f, WorldLayout.Hut.z - 8.2f), 0.2f, 20f, excl, out var dp))
            {
                Drum(batch, cols, dp, PropMats.DrumBlue, false, 0f);
                Drum(batch, cols, dp + new Vector3(0.65f, 0f, 0.2f), PropMats.DrumRed, false, 0f);
                Drum(batch, cols, dp + new Vector3(0.2f, 0f, 1.1f), PropMats.DrumYellow, true, PropUtil.Range(rng, 0f, 180f));
                excl.Add(dp.x, dp.z, 2.5f);
            }

            // tire swing
            if (FindLand(new Vector3(136f, 0f, 33f), 0.4f, 12f, excl, out var tp))
            {
                TireSwing(root, batch, cols, tp, 15f);
                excl.Add(tp.x, tp.z, 3f);
            }

            // campfire with log benches
            if (FindLand(new Vector3(151f, 0f, 14f), 0.4f, 8f, excl, out var fp))
            {
                Campfire(root, batch, cols, fp, rng);
                excl.Add(fp.x, fp.z, 4f);
            }

            // fuel cans near the dock start and the porch
            Vector3 dockStart = WorldLayout.DockStart;
            if (FindLand(new Vector3(dockStart.x - 4f, 0f, dockStart.z - 4.5f), 0.3f, 8f, excl, out var fc))
            {
                FuelCan(batch, fc, 20f, PropMats.DrumRed);
                FuelCan(batch, fc + new Vector3(0.3f, 0f, 0.35f), -15f, PropMats.DrumRed);
                FuelCan(batch, fc + new Vector3(-0.35f, 0f, 0.25f), 75f, PropMats.DrumYellow);
                excl.Add(fc.x, fc.z, 1.5f);
            }
            if (FindLand(new Vector3(WorldLayout.Hut.x + 8.5f, 0f, WorldLayout.Hut.z - 3f), 0.2f, 20f, excl, out var fc2, 6f))
                FuelCan(batch, fc2, 50f, PropMats.DrumRed);

            // driftwood logs along the shore
            for (int i = 0; i < 7; i++)
            {
                float a = PropUtil.Range(rng, -0.6f, 0.9f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropUtil.Range(rng, 150f, 175f);
                if (!FindLand(d, 0.25f, 1.6f, excl, out var lp, 10f)) continue;
                float yaw = PropUtil.Range(rng, 0f, 360f);
                Quaternion r = Quaternion.Euler(0f, yaw, 90f + PropUtil.Range(rng, -6f, 6f));
                float len = PropUtil.Range(rng, 1.6f, 3.4f);
                Vector3 axis = r * Vector3.up;
                batch.B(PropMats.BarkCharred).Cylinder(lp + Vector3.up * 0.12f - axis * len * 0.5f, r, 0.16f, 0.12f, len, 7, true, true, 1f, 0.4f);
                excl.Add(lp.x, lp.z, 1.5f);
            }

            batch.Flush(root, "BeachMesh", ShadowCastingMode.On, 350f);
            yield break;
        }

        static void Lounger(PropBatch batch, Transform cols, Vector3 p, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            var dark = batch.B(PropMats.WoodDark);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    dark.Box(p + r * new Vector3(sx * 0.29f, 0.14f, sz * 0.82f), new Vector3(0.05f, 0.32f, 0.05f), r);
            dark.Box(p + r * new Vector3(0.3f, 0.29f, -0.2f), new Vector3(0.04f, 0.05f, 1.9f), r);
            dark.Box(p + r * new Vector3(-0.3f, 0.29f, -0.2f), new Vector3(0.04f, 0.05f, 1.9f), r);
            for (int i = 0; i < 9; i++)
                batch.B(PropMats.WoodPale).Box(p + r * new Vector3(0f, 0.325f, -1.0f + i * 0.145f), new Vector3(0.62f, 0.022f, 0.1f), r, 1f, new Vector2(i * 0.31f, 0.012f), PropUV.LongU);
            Quaternion back = r * Quaternion.Euler(-40f, 0f, 0f);
            Vector3 bc = p + r * new Vector3(0f, 0.6f, 0.62f);
            batch.B(PropMats.WoodPale).Box(bc, new Vector3(0.62f, 0.025f, 0.75f), back, 1f, new Vector2(0.4f, 0.27f), PropUV.Default);
            dark.Box(p + r * new Vector3(0f, 0.43f, 0.55f), new Vector3(0.6f, 0.04f, 0.04f), r * Quaternion.Euler(60f, 0f, 0f));
            batch.B(PropMats.FabricStripe).Box(p + r * new Vector3(0f, 0.345f, -0.35f), new Vector3(0.56f, 0.012f, 1.25f), r);
            batch.B(PropMats.FabricStripe).Box(bc + back * new Vector3(0f, 0.02f, 0f), new Vector3(0.56f, 0.012f, 0.7f), back);
            PropCol.Box(cols, p + r * new Vector3(0f, 0.3f, -0.1f), new Vector3(0.7f, 0.6f, 2f), r);
        }

        static void Parasol(PropBatch batch, Transform cols, Vector3 p)
        {
            batch.B(PropMats.WoodPale).Rod(p - Vector3.up * 0.3f, p + Vector3.up * 2.3f, 0.025f, 0.025f, 6);
            batch.B(PropMats.Parasol).Cylinder(p + Vector3.up * 1.95f, Quaternion.Euler(4f, 0f, 3f), 1.3f, 0.03f, 0.42f, 16, false, false, 1f, 1f);
            PropCol.Capsule(cols, p + Vector3.up * 1f, 0.06f, 2f, Quaternion.identity);
        }

        static void Drum(PropBatch batch, Transform cols, Vector3 p, Material mat, bool lying, float yaw)
        {
            const float r = 0.29f, h = 0.88f;
            if (!lying)
            {
                Vector3 b = new Vector3(p.x, G(p.x, p.z) - 0.03f, p.z);
                batch.B(mat).Cylinder(b, Quaternion.identity, r, r, h, 16, false, true, 1.85f, 1f);
                batch.B(PropMats.DarkMetal).Torus(b + Vector3.up * h, Quaternion.identity, r - 0.01f, 0.012f, 16, 4);
                PropCol.Capsule(cols, b + Vector3.up * h * 0.5f, r, h, Quaternion.identity);
            }
            else
            {
                Quaternion q = Quaternion.Euler(0f, yaw, 90f);
                Vector3 axis = q * Vector3.up;
                Vector3 c = new Vector3(p.x, G(p.x, p.z) + r - 0.04f, p.z);
                batch.B(mat).Cylinder(c - axis * h * 0.5f, q, r, r, h, 16, true, true, 1.85f, 1f);
                PropCol.Capsule(cols, c, r, h, q);
            }
        }

        static void FuelCan(PropBatch batch, Vector3 p, float yaw, Material mat)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 b = new Vector3(p.x, G(p.x, p.z) - 0.01f, p.z);
            batch.B(mat).Box(b + Vector3.up * 0.235f, new Vector3(0.17f, 0.47f, 0.35f), r);
            var dm = batch.B(PropMats.DarkMetal);
            dm.Box(b + r * new Vector3(0f, 0.5f, -0.05f), new Vector3(0.03f, 0.03f, 0.2f), r);
            dm.Box(b + r * new Vector3(0f, 0.485f, -0.14f), new Vector3(0.03f, 0.04f, 0.03f), r);
            dm.Box(b + r * new Vector3(0f, 0.485f, 0.04f), new Vector3(0.03f, 0.04f, 0.03f), r);
            dm.Cylinder(b + r * new Vector3(0f, 0.47f, 0.13f), r * Quaternion.Euler(30f, 0f, 0f), 0.025f, 0.022f, 0.07f, 6, false, true);
        }

        static void TireSwing(Transform root, PropBatch batch, Transform cols, Vector3 p, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 left = p + r * new Vector3(-1.2f, 0f, 0f), right = p + r * new Vector3(1.2f, 0f, 0f);
            left.y = G(left.x, left.z);
            right.y = G(right.x, right.z);
            float topY = Mathf.Max(left.y, right.y) + 3.1f;
            var dark = batch.B(PropMats.WoodDark);
            dark.Cylinder(left - Vector3.up * 0.4f, Quaternion.identity, 0.11f, 0.1f, topY - left.y + 0.5f, 7, false, true, 0.7f, 1f);
            dark.Cylinder(right - Vector3.up * 0.4f, Quaternion.identity, 0.11f, 0.1f, topY - right.y + 0.5f, 7, false, true, 0.7f, 1f);
            Vector3 a = new Vector3(left.x, topY, left.z), b = new Vector3(right.x, topY, right.z);
            dark.Rod(a - (b - a).normalized * 0.25f, b + (b - a).normalized * 0.25f, 0.09f, 0.09f, 7, true, 0.6f, 1f);
            PropCol.Capsule(cols, left + Vector3.up * 1.5f, 0.12f, 3f, Quaternion.identity);
            PropCol.Capsule(cols, right + Vector3.up * 1.5f, 0.12f, 3f, Quaternion.identity);

            var pivot = new GameObject("TireSwing");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = (a + b) * 0.5f;
            pivot.transform.rotation = r;
            var swing = new GameObject("Swing").transform;
            swing.SetParent(pivot.transform, false);
            var sb = new PropMeshBuilder();
            sb.Rod(new Vector3(0f, -1.65f, 0f), Vector3.zero, 0.015f, 0.015f, 4);
            PropUtil.MeshObject(swing, "Rope", sb.ToMesh("SwingRope"), PropMats.Rope, Vector3.zero, Quaternion.identity);
            var tb = new PropMeshBuilder();
            tb.Torus(new Vector3(0f, -2.0f, 0f), Quaternion.FromToRotation(Vector3.up, Vector3.forward), 0.33f, 0.12f, 18, 8);
            PropUtil.MeshObject(swing, "Tire", tb.ToMesh("SwingTire"), PropMats.Rubber, Vector3.zero, Quaternion.identity);
            swing.gameObject.AddComponent<PropSwing>();
        }

        static void Campfire(Transform root, PropBatch batch, Transform cols, Vector3 p, System.Random rng)
        {
            batch.B(PropMats.Ash).Disk(p + Vector3.up * 0.03f, 0.62f, 14, Quaternion.identity, true, 1f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f + PropUtil.Range(rng, -0.1f, 0.1f);
                Vector3 sp = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.78f;
                sp.y = G(sp.x, sp.z) + 0.05f;
                float s = PropUtil.Range(rng, 0.15f, 0.22f);
                PropRocks.Build(batch.B(PropMats.RockDark), sp, new Vector3(s, s * 0.8f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 3, 0.25f);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                batch.B(PropMats.BarkCharred).Rod(p + d * 0.45f + Vector3.up * 0.04f, p + Vector3.up * 0.5f - d * 0.05f, 0.06f, 0.04f, 6, true, 1f, 0.4f);
            }
            for (int i = 0; i < 5; i++)
            {
                Vector3 e = p + new Vector3(PropUtil.Range(rng, -0.25f, 0.25f), 0.05f, PropUtil.Range(rng, -0.25f, 0.25f));
                batch.B(PropMats.FireGlow).Blob(e, new Vector3(0.07f, 0.04f, 0.06f), Quaternion.identity, 2, null);
            }
            // log benches
            for (int i = 0; i < 2; i++)
            {
                float a = (i == 0 ? 0.3f : 3.6f);
                Vector3 bp = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.2f;
                bp.y = G(bp.x, bp.z);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Quaternion q = Quaternion.FromToRotation(Vector3.up, tangent);
                batch.B(PropMats.Bark).Cylinder(bp + Vector3.up * 0.2f - tangent * 0.9f, q, 0.21f, 0.19f, 1.8f, 9, true, true, 1f, 0.4f);
                PropCol.Capsule(cols, bp + Vector3.up * 0.2f, 0.21f, 1.8f, q);
            }

            var fireGo = new GameObject("Campfire");
            fireGo.transform.SetParent(root, false);
            fireGo.transform.position = p + Vector3.up * 0.6f;
            var fire = PropFX.Fire(root, p + Vector3.up * 0.12f);
            var light = fireGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.22f);
            light.range = 9f;
            light.intensity = 2.6f;
            light.shadows = LightShadows.None;
            var nl = PropNightLight.Attach(fireGo, light, PropMats.FireGlow, new Color(2.5f, 0.75f, 0.18f), 0.1f, 0.35f, true);
            nl.flickerAmount = 0.35f;
            nl.particles = fire;
        }

        // ================================================================== paths
        public static IEnumerator BuildPaths(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("Paths", island);
            var batch = new PropBatch(64f);
            var rng = new System.Random(3131);

            float stepsX = WorldLayout.Hut.x + 7.2f + 2.5f;
            var main = new List<Vector2>
            {
                new Vector2(stepsX + 1.5f, WorldLayout.Hut.z + 1f), new Vector2(stepsX - 1f, WorldLayout.Hut.z + 9f),
                new Vector2(110f, 36f), new Vector2(94f, 50f), new Vector2(72f, 70f), new Vector2(48f, 92f),
                new Vector2(22f, 114f), new Vector2(2f, 131f), new Vector2(-14f, 142f),
            };
            Vector3 lh = WorldLayout.Lighthouse;
            Vector3 toHut = (WorldLayout.Hut - lh);
            toHut.y = 0f;
            toHut.Normalize();
            Vector3 door = lh + toHut * 3.4f;
            main.Add(new Vector2(door.x, door.z));

            var pts = SampleSpline(main, 1.0f, 41);
            var torchSpots = new List<Vector3>();
            float acc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p2 = pts[i];
                Vector2 d2 = (i + 1 < pts.Count ? pts[i + 1] - p2 : p2 - pts[i - 1]).normalized;
                float g = G(p2.x, p2.y);
                if (g < 0.6f) continue;
                Vector3 p = new Vector3(p2.x, g, p2.y);
                PropInfo.PathPoints.Add(p);
                excl.Add(p.x, p.z, 2.3f);
                if (i > 0) acc += (pts[i] - pts[i - 1]).magnitude;
                Vector3 dir = new Vector3(d2.x, 0f, d2.y);
                Vector3 n = PropGround.Normal(p.x, p.z, 0.6f);
                float slope = Mathf.Abs(G(p.x + dir.x * 0.6f, p.z + dir.z * 0.6f) - G(p.x - dir.x * 0.6f, p.z - dir.z * 0.6f)) / 1.2f;
                Vector3 along = Vector3.ProjectOnPlane(dir, n).normalized;
                Quaternion r = Quaternion.LookRotation(along, n) * Quaternion.Euler(0f, PropUtil.Range(rng, -7f, 7f), 0f);
                if (slope > 0.28f || i % 9 == 4)
                {
                    float s = PropUtil.Range(rng, 0.28f, 0.4f);
                    PropRocks.Build(batch.B(PropMats.RockLight, p), p + n * 0.01f, new Vector3(s, 0.08f, s * 0.85f), r, rng.Next(), 3, 0.15f, 0.8f);
                }
                else
                {
                    batch.B(PropMats.WoodPale, p).Box(p + n * 0.015f, new Vector3(1.15f, 0.06f, 0.3f), r, 1f, new Vector2(PropUtil.Range(rng, 0f, 2f), (i % 8) * 0.25f + 0.02f), PropUV.LongU);
                }
                if (acc > 34f)
                {
                    acc = 0f;
                    Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * ((torchSpots.Count % 2 == 0) ? 1.1f : -1.1f);
                    Vector3 tp = p + side;
                    tp.y = G(tp.x, tp.z);
                    torchSpots.Add(tp);
                }
                if (PropRun.Due()) yield return null;
            }

            // short stepping-stone path from the hut to the helipad
            var heli = new List<Vector2>
            {
                new Vector2(WorldLayout.Hut.x + 6f, WorldLayout.Hut.z - 7f), new Vector2(112f, -8f), new Vector2(98f, -32f),
                new Vector2(WorldLayout.Helipad.x + 9f, WorldLayout.Helipad.z + 9f),
            };
            var hp = SampleSpline(heli, 1.25f, 77);
            for (int i = 0; i < hp.Count; i++)
            {
                float g = G(hp[i].x, hp[i].y);
                if (g < 0.5f) continue;
                Vector3 p = new Vector3(hp[i].x, g, hp[i].y);
                Vector3 n = PropGround.Normal(p.x, p.z, 0.6f);
                excl.Add(p.x, p.z, 1.8f);
                float s = PropUtil.Range(rng, 0.3f, 0.42f);
                PropRocks.Build(batch.B(PropMats.RockLight, p), p + n * 0.01f, new Vector3(s, 0.07f, s * 0.9f),
                    PropUtil.OnGround(n, 1f, PropUtil.Range(rng, 0f, 360f)), rng.Next(), 3, 0.15f, 0.8f);
            }

            // tiki torches along the main path
            for (int i = 0; i < torchSpots.Count && i < 5; i++) Torch(root, batch, torchSpots[i], i);

            batch.Flush(root, "PathMesh", ShadowCastingMode.Off, 160f);
        }

        /// Catmull-Rom spline through control points with gentle noise wobble, sampled at a fixed spacing.
        static List<Vector2> SampleSpline(List<Vector2> cps, float spacing, int seed)
        {
            var dense = new List<Vector2>();
            for (int i = 0; i < cps.Count - 1; i++)
            {
                Vector2 p0 = cps[Mathf.Max(0, i - 1)], p1 = cps[i], p2 = cps[i + 1], p3 = cps[Mathf.Min(cps.Count - 1, i + 2)];
                int steps = Mathf.Max(4, Mathf.CeilToInt((p2 - p1).magnitude / 0.25f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    float t2 = t * t, t3 = t2 * t;
                    Vector2 q = 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    dense.Add(q);
                }
            }
            dense.Add(cps[cps.Count - 1]);
            var outp = new List<Vector2>();
            float acc = 0f, total = 0f;
            outp.Add(dense[0]);
            for (int i = 1; i < dense.Count; i++)
            {
                float d = (dense[i] - dense[i - 1]).magnitude;
                acc += d;
                total += d;
                if (acc >= spacing)
                {
                    acc = 0f;
                    Vector2 dir = (dense[i] - dense[i - 1]).normalized;
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    float wob = (PropNoise.Value(total * 0.08f, 0.5f, seed) - 0.5f) * 1.6f;
                    outp.Add(dense[i] + perp * wob);
                }
            }
            return outp;
        }

        static void Torch(Transform root, PropBatch batch, Vector3 p, int index)
        {
            batch.B(PropMats.Bark, p).Cylinder(p - Vector3.up * 0.3f, Quaternion.Euler(PropUtil.Range(new System.Random(index), -3f, 3f), 0f, 0f), 0.045f, 0.035f, 1.9f, 6, false, true, 0.3f, 0.4f);
            Vector3 cup = p + Vector3.up * 1.6f;
            batch.B(PropMats.Rope, p).Cylinder(cup - Vector3.up * 0.05f, Quaternion.identity, 0.06f, 0.1f, 0.18f, 8, true, false, 1f, 1f);
            batch.B(PropMats.FireGlow, p).Blob(cup + Vector3.up * 0.14f, new Vector3(0.06f, 0.03f, 0.06f), Quaternion.identity, 2, null);
            var go = new GameObject("Torch");
            go.transform.SetParent(root, false);
            go.transform.position = cup + Vector3.up * 0.4f;
            var fire = PropFX.Fire(root, cup + Vector3.up * 0.12f);
            fire.transform.localScale = Vector3.one * 0.45f;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.6f, 0.25f);
            l.range = 7f;
            l.intensity = 1.6f;
            l.shadows = LightShadows.None;
            var nl = PropNightLight.Attach(go, l, PropMats.FireGlow, new Color(2.5f, 0.75f, 0.18f), 0.12f, 0.4f, true);
            nl.particles = fire;
            nl.cullDistance = 80f;
        }

        // ================================================================== hidden bay
        public static IEnumerator BuildHiddenBay(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("HiddenBay", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(2718);

            Vector3 bay = WorldLayout.HiddenBay;
            Vector3 dirIn = WorldLayout.IslandCenter - bay;
            dirIn.y = 0f;
            dirIn.Normalize();
            Vector3 beach = bay;
            for (float t = -25f; t < 90f; t += 1f)
            {
                Vector3 p = bay + dirIn * t;
                float g = G(p.x, p.z);
                if (g >= 0.25f) { beach = new Vector3(p.x, g, p.z); break; }
            }
            Vector3 along = Vector3.Cross(Vector3.up, dirIn).normalized;

            // rowing boat wreck at the waterline, half sunk and tilted
            Vector3 bp = beach - dirIn * 0.5f + along * 2f;
            bp.y = G(bp.x, bp.z) - 0.18f;
            float byaw = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg + 25f;
            Quaternion br = Quaternion.Euler(0f, byaw, 0f) * Quaternion.Euler(-4f, 0f, 19f);
            RowingBoat(batch, Matrix4x4.TRS(bp, br, Vector3.one), PropMats.WoodDark, true);
            PropCol.Box(cols, bp + br * new Vector3(0f, 0.3f, 0f), new Vector3(1.25f, 0.55f, 3.5f), br);
            // oar lying in the sand
            Vector3 op = beach + dirIn * 2.2f + along * 3.6f;
            op.y = G(op.x, op.z) + 0.04f;
            Quaternion orr = Quaternion.Euler(0f, byaw + 70f, 90f);
            Vector3 oaxis = orr * Vector3.up;
            batch.B(PropMats.WoodPale).Cylinder(op - oaxis * 1.1f, orr, 0.025f, 0.025f, 1.6f, 6, true, true);
            batch.B(PropMats.WoodPale).Box(op + oaxis * 0.75f, new Vector3(0.14f, 0.55f, 0.02f), orr);
            excl.Add(bp.x, bp.z, 4f);

            // small rock arch at the edge of the bay
            Vector3 ac = beach + along * -11f - dirIn * 2.5f;
            ac.y = Mathf.Min(G(ac.x, ac.z), 0.3f);
            var arch = new PropMeshBuilder();
            BuildArch(arch, along, 4.2f, 5.2f, rng.Next());
            var archMesh = arch.ToMesh("RockArch");
            batch.Add(archMesh, PropMats.RockDark, Matrix4x4.Translate(ac));
            PropCol.Mesh(cols, archMesh, ac, Quaternion.identity, Vector3.one, "ArchCol");
            excl.Add(ac.x, ac.z, 7f);

            // treasure chest, partly buried
            Vector3 cp = beach + dirIn * 5.5f + along * 6.5f;
            if (FindLand(cp, 0.35f, 6f, excl, out var cpos, 8f)) cp = cpos;
            cp.y = G(cp.x, cp.z) - 0.12f;
            Quaternion cr = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f) * Quaternion.Euler(6f, 0f, -8f);
            Chest(batch, cp, cr, rng);
            PropCol.Box(cols, cp + cr * new Vector3(0f, 0.35f, 0f), new Vector3(0.95f, 0.7f, 0.6f), cr);
            PropInfo.TreasureChest = cp;
            excl.Add(cp.x, cp.z, 2.5f);

            // boulders guarding the bay
            for (int i = 0; i < 7; i++)
            {
                Vector3 rp = beach + along * PropUtil.Range(rng, -20f, 20f) + dirIn * PropUtil.Range(rng, -6f, 10f);
                if (excl.Blocked(rp.x, rp.z)) continue;
                rp.y = G(rp.x, rp.z) - 0.4f;
                float s = PropUtil.Range(rng, 0.8f, 2.2f);
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 0.9f, 1.4f), s * PropUtil.Range(rng, 0.6f, 1f), s);
                var rm = PropRocks.Mesh(sc, rng.Next(), 6);
                Quaternion rr = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                batch.Add(rm, PropMats.RockDark, Matrix4x4.TRS(rp, rr, Vector3.one));
                PropCol.Mesh(cols, rm, rp, rr, Vector3.one, "Boulder");
                excl.Add(rp.x, rp.z, s + 1f);
            }

            batch.Flush(root, "HiddenBayMesh", ShadowCastingMode.On, 400f);
            yield break;
        }

        /// Arch of rock in the vertical plane containing "along"; legs sink 2.5 m below the base point.
        static void BuildArch(PropMeshBuilder b, Vector3 along, float span, float height, int seed)
        {
            var pts = new List<Vector3>();
            var radii = new List<float>();
            pts.Add(along * -span - Vector3.up * 2.5f);
            radii.Add(1.6f);
            const int n = 16;
            for (int k = 0; k <= n; k++)
            {
                float th = Mathf.PI * k / n;
                pts.Add(along * (-Mathf.Cos(th) * span) + Vector3.up * (Mathf.Sin(th) * height));
                radii.Add(Mathf.Lerp(1.45f, 0.95f, Mathf.Sin(th)));
            }
            pts.Add(along * span - Vector3.up * 2.5f);
            radii.Add(1.6f);
            int vs = b.VertexCount, ts = b.IndexCount;
            b.Tube(pts, radii, 10, 3f, 0.5f);
            Vector3 off = new Vector3(seed % 97, seed % 89, seed % 83);
            for (int i = vs; i < b.VertexCount; i++)
            {
                Vector3 v = b.V[i];
                float nz = PropNoise.Fbm3(v * 0.45f + off, 4, seed & 0xFFFF) - 0.5f;
                b.V[i] = v + b.N[i] * nz * 1.1f;
            }
            b.SmoothNormals(vs, ts);
        }

        /// Open wooden rowing boat (local: length along z, keel at y = 0). Optionally broken (missing planks).
        public static void RowingBoat(PropBatch batch, Matrix4x4 trs, Material mat, bool broken)
        {
            var b = new PropMeshBuilder();
            const int secs = 12, around = 8;
            const float L = 3.6f, HB = 0.65f, D = 0.52f;
            for (int k = 0; k <= secs; k++)
            {
                float t = (float)k / secs;
                float z = (t - 0.5f) * L;
                float hb = HB * Mathf.Max(Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.6f), t < 0.5f ? 0.38f : 0.03f);
                float depth = D + 0.14f * (2f * t - 1f) * (2f * t - 1f);
                for (int j = 0; j <= around; j++)
                {
                    float phi = Mathf.PI + Mathf.PI * j / around;
                    Vector3 p = new Vector3(hb * Mathf.Cos(phi), depth * (1f + Mathf.Sin(phi)) * 0.95f + (1f - Mathf.Pow(Mathf.Sin(Mathf.PI * t), 0.3f)) * 0.2f, z);
                    b.Vert(p, Vector3.up, new Vector2(z, j * 0.25f));
                }
            }
            for (int k = 0; k < secs; k++)
            {
                for (int j = 0; j < around; j++)
                {
                    if (broken && k >= 5 && k <= 7 && j >= 6 && j <= 7) continue;
                    if (broken && k == 2 && j == 1) continue;
                    int a = k * (around + 1) + j;
                    int c = a + around + 1;
                    b.Quad(a, a + 1, c + 1, c);
                }
            }
            b.SmoothNormals(0, 0);
            // transom closing the stern section (k = 0)
            Vector3 tc = Vector3.zero;
            for (int j = 0; j <= around; j++) tc += b.V[j];
            tc /= around + 1;
            int center = b.Vert(tc, Vector3.back, new Vector2(0f, 0f));
            for (int j = 0; j < around; j++) b.Tri(center, j, j + 1);
            b.DoubleSide(0, 0);
            // thwarts, keel and stern transom
            b.Box(new Vector3(0f, D * 0.8f, 0.3f), new Vector3(HB * 1.7f, 0.04f, 0.22f));
            b.Box(new Vector3(0f, D * 0.8f, -0.8f), new Vector3(HB * 1.5f, 0.04f, 0.22f));
            b.Box(new Vector3(0f, 0.02f, 0f), new Vector3(0.06f, 0.06f, L * 0.9f));
            batch.B(mat).Append(b, trs);
        }

        static void Chest(PropBatch batch, Vector3 p, Quaternion r, System.Random rng)
        {
            const float W = 0.9f, H = 0.48f, Dp = 0.55f;
            Vector3 c = p + r * new Vector3(0f, H * 0.5f, 0f);
            batch.B(PropMats.WoodDark).Box(c, new Vector3(W, H, Dp), r);
            Quaternion lidAxis = r * Quaternion.FromToRotation(Vector3.up, Vector3.right);
            Vector3 lidBase = p + r * new Vector3(-W * 0.5f, H, 0f);
            batch.B(PropMats.WoodDark).Cylinder(lidBase, lidAxis, Dp * 0.5f, Dp * 0.5f, W, 14, true, true, 1f, 1f);
            var band = batch.B(PropMats.Rust);
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.32f;
                band.Box(c + r * new Vector3(x, 0f, 0f), new Vector3(0.05f, H + 0.01f, Dp + 0.02f), r);
                band.Cylinder(p + r * new Vector3(x - 0.025f, H, 0f), lidAxis, Dp * 0.5f + 0.01f, Dp * 0.5f + 0.01f, 0.05f, 14, true, true);
            }
            batch.B(PropMats.Gold).Box(p + r * new Vector3(0f, H * 0.9f, Dp * 0.5f + 0.015f), new Vector3(0.12f, 0.15f, 0.02f), r);
            for (int i = 0; i < 7; i++)
            {
                Vector3 cp = p + r * new Vector3(PropUtil.Range(rng, -0.5f, 0.5f), 0f, Dp * 0.5f + PropUtil.Range(rng, 0.1f, 0.5f));
                cp.y = G(cp.x, cp.z) + 0.005f;
                batch.B(PropMats.Gold).Cylinder(cp, Quaternion.Euler(PropUtil.Range(rng, -20f, 20f), 0f, PropUtil.Range(rng, -20f, 20f)), 0.032f, 0.032f, 0.007f, 10, true, true);
            }
        }
    }
}
