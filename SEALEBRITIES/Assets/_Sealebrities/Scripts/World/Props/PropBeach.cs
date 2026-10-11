using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Beach life between hut and dock (chunky, colorful low-poly props), the campfire, island paths (planks,
    /// stepping stones, tiki torches) and the hidden bay.
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

        /// The beach campfire in front of the hut (E.1 grill #1 – PropInfo.Campfire): on the sand between the hut and
        /// the water, north of the dock boardwalk.
        public static Vector3 CampfireSpot => new Vector3(WorldLayout.Hut.x + 17f, 0f, WorldLayout.Hut.z + 6f);

        /// First land point (ground in [minG, maxG]) walking inward along an angle from far out at sea (main island).
        public static bool ShorePoint(float angRad, float minG, float maxG, out Vector3 pos, float rFrom = -1f)
        {
            if (rFrom <= 0f) rFrom = WorldLayout.IslandRadius + 70f;
            Vector3 d = new Vector3(Mathf.Cos(angRad), 0f, Mathf.Sin(angRad));
            for (float r = rFrom; r > 30f; r -= 0.75f)
            {
                float x = d.x * r, z = d.z * r;
                float g = G(x, z);
                if (g < minG || g > maxG) continue;
                pos = new Vector3(x, g, z);
                return true;
            }
            pos = Vector3.zero;
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
            Vector3 hut = WorldLayout.Hut;

            // L.5: the hut beach at the 0.62 layout – everything relative to the hut / dock, not TV3 map literals.
            // two loungers with a parasol and a towel, facing the sea (east), north of the campfire
            if (FindLand(new Vector3(hut.x + 24f, 0f, hut.z + 9f), 0.35f, 8f, excl, out var l1, 8f))
            {
                Lounger(batch, cols, l1, -90f + PropUtil.Range(rng, -8f, 8f), PropMats.CushionA);
                excl.Add(l1.x, l1.z, 2f);
                Vector3 l2d = l1 + new Vector3(0.6f, 0f, 3.2f);
                if (FindLand(l2d, 0.35f, 8f, excl, out var l2, 4f))
                {
                    Lounger(batch, cols, l2, -90f + PropUtil.Range(rng, -10f, 10f), PropMats.CushionB);
                    excl.Add(l2.x, l2.z, 2f);
                }
                Vector3 pp = l1 + new Vector3(-0.6f, 0f, 1.6f);
                pp.y = G(pp.x, pp.z);
                Parasol(batch, cols, pp);
                Vector3 tw = l1 + new Vector3(1.8f, 0f, 1.4f);
                tw.y = G(tw.x, tw.z);
                Towel(batch, tw, PropUtil.Range(rng, 0f, 360f));
            }

            // surfboard stuck in the sand south of the boardwalk
            if (FindLand(new Vector3(hut.x + 18f, 0f, hut.z - 9f), 0.3f, 8f, excl, out var sp, 6f))
            {
                Quaternion r = Quaternion.Euler(0f, 30f, 0f) * Quaternion.Euler(-74f, 0f, 0f);
                Vector3 c = sp + Vector3.up * 0.8f;
                Surfboard(batch, c, r, PropMats.Surfboard);
                PropCol.Box(cols, c, new Vector3(0.6f, 0.1f, 2.1f), r);
                excl.Add(sp.x, sp.z, 1.2f);
            }

            // oil drums behind the hut (west)
            if (FindLand(new Vector3(hut.x - 7.4f, 0f, hut.z - 4.5f), 0.2f, 20f, excl, out var dp, 5f))
            {
                Drum(batch, cols, dp, PropMats.DrumBlue, false, 0f);
                Drum(batch, cols, dp + new Vector3(0.65f, 0f, 0.2f), PropMats.DrumRed, false, 0f);
                Drum(batch, cols, dp + new Vector3(0.2f, 0f, 1.1f), PropMats.DrumYellow, true, PropUtil.Range(rng, 0f, 180f));
                excl.Add(dp.x, dp.z, 2.5f);
            }

            // tire swing on the beach north-east of the hut
            if (FindLand(new Vector3(hut.x + 13f, 0f, hut.z + 17f), 0.4f, 12f, excl, out var tp, 6f))
            {
                TireSwing(root, batch, cols, tp, 15f);
                excl.Add(tp.x, tp.z, 3f);
            }

            // campfire with log benches (the hut grill – E „Fisch grillen“)
            if (FindLand(CampfireSpot, 0.4f, 8f, excl, out var fp, 5f))
            {
                Campfire(root, batch, cols, fp, rng);
                excl.Add(fp.x, fp.z, 4f);
                PropInfo.Campfire = fp;
                PropInfo.SetSpot("grill_hut", fp);
            }

            // fuel cans near the dock start and the porch
            Vector3 dockStart = WorldLayout.DockStart;
            if (FindLand(new Vector3(dockStart.x - 6f, 0f, dockStart.z - 4.5f), 0.3f, 8f, excl, out var fc, 5f))
            {
                FuelCan(batch, fc, 20f, PropMats.DrumRed);
                FuelCan(batch, fc + new Vector3(0.3f, 0f, 0.35f), -15f, PropMats.DrumRed);
                FuelCan(batch, fc + new Vector3(-0.35f, 0f, 0.25f), 75f, PropMats.DrumYellow);
                excl.Add(fc.x, fc.z, 1.5f);
            }
            if (FindLand(new Vector3(hut.x + 9.5f, 0f, hut.z - 5.6f), 0.2f, 20f, excl, out var fc2, 4f))
                FuelCan(batch, fc2, 50f, PropMats.DrumRed);

            // driftwood logs along the east / south-east shore
            for (int i = 0; i < 9; i++)
            {
                float a = PropUtil.Range(rng, -0.75f, 0.95f);
                if (!ShorePoint(a, 0.3f, 1.4f, out var lp)) continue;
                if (excl.Blocked(lp.x, lp.z)) continue;
                float yaw = PropUtil.Range(rng, 0f, 360f);
                Quaternion r = Quaternion.Euler(0f, yaw, 90f + PropUtil.Range(rng, -6f, 6f));
                float len = PropUtil.Range(rng, 1.6f, 3.4f);
                Vector3 axis = r * Vector3.up;
                batch.B(PropMats.Driftwood).Cylinder(lp + Vector3.up * 0.12f - axis * len * 0.5f, r, 0.17f, 0.12f, len, 6, true, true);
                excl.Add(lp.x, lp.z, 1.5f);
            }

            // starfish on the wet sand
            for (int i = 0; i < 8; i++)
            {
                float a = PropUtil.Range(rng, -0.8f, 1.0f);
                if (!ShorePoint(a, 0.05f, 0.6f, out var sf)) continue;
                Starfish(batch, sf, PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0.12f, 0.2f), i % 2 == 0 ? PropMats.Orange : PropMats.FlowerPink);
            }

            batch.Flush(root, "BeachMesh", ShadowCastingMode.On, 350f);
            yield break;
        }

        static void Lounger(PropBatch batch, Transform cols, Vector3 p, float yaw, Material cushion)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            var frame = batch.B(PropMats.WoodLight);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    frame.Box(p + r * new Vector3(sx * 0.29f, 0.14f, sz * 0.82f), new Vector3(0.07f, 0.32f, 0.07f), r);
            frame.Box(p + r * new Vector3(0.3f, 0.29f, -0.2f), new Vector3(0.06f, 0.07f, 1.9f), r);
            frame.Box(p + r * new Vector3(-0.3f, 0.29f, -0.2f), new Vector3(0.06f, 0.07f, 1.9f), r);
            for (int i = 0; i < 5; i++)
                batch.B(i % 2 == 0 ? PropMats.Wood : PropMats.WoodLight).Box(p + r * new Vector3(0f, 0.33f, -1.0f + i * 0.26f), new Vector3(0.62f, 0.04f, 0.22f), r);
            Quaternion back = r * Quaternion.Euler(-40f, 0f, 0f);
            Vector3 bc = p + r * new Vector3(0f, 0.6f, 0.62f);
            batch.B(PropMats.Wood).Box(bc, new Vector3(0.62f, 0.04f, 0.75f), back);
            frame.Box(p + r * new Vector3(0f, 0.43f, 0.55f), new Vector3(0.6f, 0.06f, 0.06f), r * Quaternion.Euler(60f, 0f, 0f));
            batch.B(cushion).Box(p + r * new Vector3(0f, 0.38f, -0.35f), new Vector3(0.56f, 0.07f, 1.25f), r);
            batch.B(cushion).Box(bc + back * new Vector3(0f, 0.05f, 0f), new Vector3(0.56f, 0.07f, 0.7f), back);
            PropCol.Box(cols, p + r * new Vector3(0f, 0.3f, -0.1f), new Vector3(0.7f, 0.6f, 2f), r);
        }

        /// Beach parasol: 8 flat canopy segments in alternating colors (two-sided) on a white pole.
        public static void Parasol(PropBatch batch, Transform cols, Vector3 p)
        {
            batch.B(PropMats.White).Rod(p - Vector3.up * 0.3f, p + Vector3.up * 2.35f, 0.04f, 0.035f, 6);
            Quaternion tilt = Quaternion.Euler(4f, 0f, 3f);
            Vector3 apex = p + tilt * new Vector3(0f, 2.35f, 0f);
            const int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 e0 = p + tilt * new Vector3(Mathf.Cos(a0) * 1.4f, 1.9f, Mathf.Sin(a0) * 1.4f);
                Vector3 e1 = p + tilt * new Vector3(Mathf.Cos(a1) * 1.4f, 1.9f, Mathf.Sin(a1) * 1.4f);
                Vector3 up = Vector3.Cross(e0 - apex, e1 - apex);
                if (Vector3.Dot(up, tilt * Vector3.up) < 0f) up = -up;
                var m = i % 2 == 0 ? PropMats.ParasolA : PropMats.ParasolB;
                batch.B(m).Tri2(apex, e0, e1, up.normalized);
                // little hanging flap
                Vector3 mid = (e0 + e1) * 0.5f;
                Vector3 outward = (mid - (p + tilt * new Vector3(0f, 1.9f, 0f))).normalized;
                batch.B(m).Tri2(e0, e1, mid - tilt * Vector3.up * 0.16f, outward);
            }
            batch.B(PropMats.White).Ico(apex + tilt * Vector3.up * 0.04f, Vector3.one * 0.07f, Quaternion.identity, 0);
            PropCol.Capsule(cols, p + Vector3.up * 1f, 0.06f, 2f, Quaternion.identity);
        }

        public static void Towel(PropBatch batch, Vector3 p, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 4; i++)
                batch.B(i % 2 == 0 ? PropMats.CushionB : PropMats.White).Box(p + r * new Vector3(0f, 0.015f, -0.6f + i * 0.4f), new Vector3(0.8f, 0.02f, 0.4f), r);
        }

        /// Low-poly surfboard: a flat pointed outline with thickness, a white center stripe and a dark fin.
        public static void Surfboard(PropBatch batch, Vector3 c, Quaternion r, Material mat)
        {
            Vector2[] half = { new Vector2(0f, 1.05f), new Vector2(0.13f, 0.85f), new Vector2(0.24f, 0.45f), new Vector2(0.27f, 0f), new Vector2(0.24f, -0.55f), new Vector2(0.15f, -0.92f), new Vector2(0f, -1f) };
            int n = half.Length;
            var outline = new Vector3[2 * n - 2];
            for (int i = 0; i < n; i++) outline[i] = new Vector3(half[i].x, 0f, half[i].y);
            for (int i = n - 2, k = n; i >= 1; i--, k++) outline[k] = new Vector3(-half[i].x, 0f, half[i].y);
            // world-space geometry: W resets the builder matrix (PropHut / PropPOI2 leave their local frame on the shared
            // builders – B kept it and drew the rack boards in the sky) and picks the cull cell of the board
            var b = batch.W(mat, c);
            const float th = 0.035f;
            Vector3 up = r * Vector3.up;
            for (int i = 0; i < outline.Length; i++)
            {
                Vector3 a = c + r * (outline[i] + Vector3.up * th), bb = c + r * (outline[(i + 1) % outline.Length] + Vector3.up * th);
                Vector3 a2 = c + r * (outline[i] - Vector3.up * th), b2 = c + r * (outline[(i + 1) % outline.Length] - Vector3.up * th);
                b.Face3(c + up * th, a, bb, up);
                b.Face3(c - up * th, a2, b2, -up);
                Vector3 mid = (outline[i] + outline[(i + 1) % outline.Length]) * 0.5f;
                b.QuadAuto(a, bb, b2, a2, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, r * mid);
            }
            batch.W(PropMats.White, c).Box(c + r * new Vector3(0f, th + 0.004f, 0f), new Vector3(0.06f, 0.008f, 1.7f), r);
            batch.W(PropMats.Ink, c).Face3(c + r * new Vector3(0f, -th, -0.6f), c + r * new Vector3(0f, -th, -0.9f), c + r * new Vector3(0f, -th - 0.16f, -0.85f), r * Vector3.right);
            batch.W(PropMats.Ink, c).Face3(c + r * new Vector3(0f, -th, -0.6f), c + r * new Vector3(0f, -th, -0.9f), c + r * new Vector3(0f, -th - 0.16f, -0.85f), r * Vector3.left);
        }

        static void Drum(PropBatch batch, Transform cols, Vector3 p, Material mat, bool lying, float yaw)
        {
            const float r = 0.29f, h = 0.88f;
            if (!lying)
            {
                Vector3 b = new Vector3(p.x, G(p.x, p.z) - 0.03f, p.z);
                PropStructures.Drum(batch, b, mat);
                PropCol.Capsule(cols, b + Vector3.up * h * 0.5f, r, h, Quaternion.identity);
            }
            else
            {
                Quaternion q = Quaternion.Euler(0f, yaw, 90f);
                Vector3 axis = q * Vector3.up;
                Vector3 c = new Vector3(p.x, G(p.x, p.z) + r - 0.04f, p.z);
                batch.B(mat).Cylinder(c - axis * h * 0.5f, q, r, r, h, 8, true, true);
                batch.B(PropMats.DarkMetal).Cylinder(c - axis * 0.2f, q, r + 0.012f, r + 0.012f, 0.05f, 8);
                batch.B(PropMats.DarkMetal).Cylinder(c + axis * 0.15f, q, r + 0.012f, r + 0.012f, 0.05f, 8);
                PropCol.Capsule(cols, c, r, h, q);
            }
        }

        static void FuelCan(PropBatch batch, Vector3 p, float yaw, Material mat)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 b = new Vector3(p.x, G(p.x, p.z) - 0.01f, p.z);
            batch.B(mat).Box(b + Vector3.up * 0.235f, new Vector3(0.18f, 0.47f, 0.36f), r);
            var dm = batch.B(PropMats.DarkMetal);
            dm.Box(b + r * new Vector3(0f, 0.5f, -0.05f), new Vector3(0.04f, 0.04f, 0.2f), r);
            dm.Box(b + r * new Vector3(0f, 0.485f, -0.14f), new Vector3(0.04f, 0.05f, 0.04f), r);
            dm.Box(b + r * new Vector3(0f, 0.485f, 0.04f), new Vector3(0.04f, 0.05f, 0.04f), r);
            dm.Cylinder(b + r * new Vector3(0f, 0.47f, 0.13f), r * Quaternion.Euler(30f, 0f, 0f), 0.03f, 0.026f, 0.08f, 5, false, true);
        }

        static void Starfish(PropBatch batch, Vector3 p, float yaw, float size, Material mat)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 c = p + Vector3.up * 0.03f;
            var b = batch.B(mat);
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                Vector3 tip = c + r * new Vector3(Mathf.Cos(a), -0.12f, Mathf.Sin(a)) * size;
                Vector3 l = c + r * new Vector3(Mathf.Cos(a - 0.63f), 0f, Mathf.Sin(a - 0.63f)) * size * 0.38f;
                Vector3 rr = c + r * new Vector3(Mathf.Cos(a + 0.63f), 0f, Mathf.Sin(a + 0.63f)) * size * 0.38f;
                Vector3 top = c + Vector3.up * size * 0.12f;
                b.Face3(top, l, tip, Vector3.up);
                b.Face3(top, tip, rr, Vector3.up);
            }
        }

        static void TireSwing(Transform root, PropBatch batch, Transform cols, Vector3 p, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 left = p + r * new Vector3(-1.2f, 0f, 0f), right = p + r * new Vector3(1.2f, 0f, 0f);
            left.y = G(left.x, left.z);
            right.y = G(right.x, right.z);
            float topY = Mathf.Max(left.y, right.y) + 3.1f;
            var wood = batch.B(PropMats.Wood);
            wood.Cylinder(left - Vector3.up * 0.4f, Quaternion.identity, 0.12f, 0.11f, topY - left.y + 0.5f, 6, false, true);
            wood.Cylinder(right - Vector3.up * 0.4f, Quaternion.identity, 0.12f, 0.11f, topY - right.y + 0.5f, 6, false, true);
            Vector3 a = new Vector3(left.x, topY, left.z), b = new Vector3(right.x, topY, right.z);
            wood.Rod(a - (b - a).normalized * 0.25f, b + (b - a).normalized * 0.25f, 0.1f, 0.1f, 6, true);
            PropCol.Capsule(cols, left + Vector3.up * 1.5f, 0.12f, 3f, Quaternion.identity);
            PropCol.Capsule(cols, right + Vector3.up * 1.5f, 0.12f, 3f, Quaternion.identity);

            var pivot = new GameObject("TireSwing");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = (a + b) * 0.5f;
            pivot.transform.rotation = r;
            var swing = new GameObject("Swing").transform;
            swing.SetParent(pivot.transform, false);
            var sb = new PropMeshBuilder();
            sb.Rod(new Vector3(0f, -1.65f, 0f), Vector3.zero, 0.025f, 0.025f, 4);
            PropUtil.MeshObject(swing, "Rope", sb.ToMesh("SwingRope"), PropMats.Rope, Vector3.zero, Quaternion.identity);
            var tb = new PropMeshBuilder();
            tb.Torus(new Vector3(0f, -2.0f, 0f), Quaternion.FromToRotation(Vector3.up, Vector3.forward), 0.33f, 0.13f, 8, 4);
            PropUtil.MeshObject(swing, "Tire", tb.ToMesh("SwingTire"), PropMats.Rubber, Vector3.zero, Quaternion.identity);
            swing.gameObject.AddComponent<PropSwing>();

            // solid tire (feedback #10): a box that swings with it on a kinematic body, plus the top beam
            var rb = swing.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var tireCol = new GameObject("TireCol");
            tireCol.transform.SetParent(swing, false);
            tireCol.transform.localPosition = new Vector3(0f, -2.0f, 0f);
            var bc = tireCol.AddComponent<BoxCollider>();
            bc.size = new Vector3(0.94f, 0.94f, 0.3f);
            var ropeCol = new GameObject("RopeCol");
            ropeCol.transform.SetParent(swing, false);
            ropeCol.transform.localPosition = new Vector3(0f, -0.85f, 0f);
            var rc = ropeCol.AddComponent<CapsuleCollider>();
            rc.direction = 1;
            rc.radius = 0.06f;
            rc.height = 1.7f;
            PropCol.Capsule(cols, (a + b) * 0.5f, 0.11f, (b - a).magnitude + 0.5f, Quaternion.FromToRotation(Vector3.up, (b - a).normalized), "SwingBeam");
        }

        static void Campfire(Transform root, PropBatch batch, Transform cols, Vector3 p, System.Random rng)
        {
            batch.B(PropMats.Ash).Disk(p + Vector3.up * 0.03f, 0.62f, 8, Quaternion.identity, true);
            for (int i = 0; i < 9; i++)
            {
                float a = i * Mathf.PI * 2f / 9f + PropUtil.Range(rng, -0.1f, 0.1f);
                Vector3 sp = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.8f;
                sp.y = G(sp.x, sp.z) + 0.05f;
                float s = PropUtil.Range(rng, 0.16f, 0.24f);
                PropRocks.Build(batch.B(i % 3 == 0 ? PropMats.Rock : PropMats.RockDark), sp, new Vector3(s, s * 0.8f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0, 0.2f);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + 0.4f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 l0 = p + d * 0.45f + Vector3.up * 0.04f, l1 = p + Vector3.up * 0.5f - d * 0.05f;
                batch.B(PropMats.WoodDark).Rod(l0, Vector3.Lerp(l0, l1, 0.7f), 0.065f, 0.055f, 5, true);
                batch.B(PropMats.Ash).Rod(Vector3.Lerp(l0, l1, 0.7f), l1, 0.055f, 0.04f, 5, true);
            }
            for (int i = 0; i < 5; i++)
            {
                Vector3 e = p + new Vector3(PropUtil.Range(rng, -0.25f, 0.25f), 0.06f, PropUtil.Range(rng, -0.25f, 0.25f));
                batch.B(PropMats.FireGlow).Octa(e, new Vector3(0.08f, 0.05f, 0.07f));
            }
            // log benches
            for (int i = 0; i < 2; i++)
            {
                float a = (i == 0 ? 0.3f : 3.6f);
                Vector3 bp = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.2f;
                bp.y = G(bp.x, bp.z);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                Quaternion q = Quaternion.FromToRotation(Vector3.up, tangent);
                batch.B(PropMats.Wood).Cylinder(bp + Vector3.up * 0.22f - tangent * 0.9f, q, 0.22f, 0.2f, 1.8f, 6, true, true);
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
            GrillGrate(root, batch, p);
        }

        /// E.1: a grill grate on two stones over a fire pit + the grill interactable („E – Fisch grillen“, 6 s, smoke).
        public static void GrillGrate(Transform root, PropBatch batch, Vector3 p)
        {
            var grate = batch.W(PropMats.DarkMetal, p);
            for (int i = -3; i <= 3; i++) grate.Box(p + new Vector3(i * 0.09f, 0.44f, 0f), new Vector3(0.022f, 0.022f, 0.66f));
            grate.Box(p + new Vector3(0f, 0.44f, 0.33f), new Vector3(0.62f, 0.03f, 0.03f));
            grate.Box(p + new Vector3(0f, 0.44f, -0.33f), new Vector3(0.62f, 0.03f, 0.03f));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 st = p + new Vector3(s * 0.4f, 0.17f, 0f);
                PropRocks.Build(batch.W(PropMats.RockDark, st), st, new Vector3(0.16f, 0.26f, 0.32f), Quaternion.identity, 77 + s, 0, 0.12f);
            }
            var embers = PropMats.EmissiveInstance(new Color(0.35f, 0.12f, 0.05f), Color.black);
            batch.W(embers, p).Disk(p + Vector3.up * 0.06f, 0.34f, 7, Quaternion.identity, true);
            var go = new GameObject("Grill");
            go.transform.SetParent(root, false);
            go.transform.position = p + Vector3.up * 0.5f;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(1.4f, 0.9f, 1.4f);
            var g = go.AddComponent<PropGrill>();
            g.smoke = PropFX.GrillSmoke(root, p + Vector3.up * 0.55f);
            g.glow = embers;
        }

        // ================================================================== torches
        /// Tiki torch (lit at night) – placed along the trails by PropPaths.
        public static void Torch(Transform root, PropBatch batch, Vector3 p, int index)
        {
            batch.B(PropMats.Wood, p).Cylinder(p - Vector3.up * 0.3f, Quaternion.Euler(PropUtil.Range(new System.Random(index), -3f, 3f), 0f, 0f), 0.06f, 0.05f, 1.9f, 6, false, true);
            Vector3 cup = p + Vector3.up * 1.6f;
            batch.B(PropMats.Rope, p).Cylinder(cup - Vector3.up * 0.06f, Quaternion.identity, 0.07f, 0.12f, 0.2f, 6, true, false);
            batch.B(PropMats.FireGlow, p).Octa(cup + Vector3.up * 0.15f, new Vector3(0.08f, 0.04f, 0.08f));
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
            RowingBoat(batch, Matrix4x4.TRS(bp, br, Vector3.one), PropMats.Shutter, true);
            PropCol.Box(cols, bp + br * new Vector3(0f, 0.3f, 0f), new Vector3(1.25f, 0.55f, 3.5f), br);
            // oar lying in the sand
            Vector3 op = beach + dirIn * 2.2f + along * 3.6f;
            op.y = G(op.x, op.z) + 0.04f;
            Quaternion orr = Quaternion.Euler(0f, byaw + 70f, 90f);
            Vector3 oaxis = orr * Vector3.up;
            batch.B(PropMats.WoodLight).Cylinder(op - oaxis * 1.1f, orr, 0.035f, 0.035f, 1.6f, 5, true, true);
            batch.B(PropMats.WoodLight).Box(op + oaxis * 0.75f, new Vector3(0.16f, 0.55f, 0.03f), orr);
            excl.Add(bp.x, bp.z, 4f);

            // small rock arch at the edge of the bay
            Vector3 ac = beach + along * -11f - dirIn * 2.5f;
            ac.y = Mathf.Min(G(ac.x, ac.z), 0.3f);
            var arch = new PropMeshBuilder();
            BuildArch(arch, along, 4.2f, 5.2f, rng.Next());
            var archMesh = arch.ToMesh("RockArch");
            LowPoly.Sanitize(archMesh);
            batch.Add(archMesh, PropMats.Rock, Matrix4x4.Translate(ac));
            PropCol.Mesh(cols, archMesh, ac, Quaternion.identity, Vector3.one, "ArchCol");
            excl.Add(ac.x, ac.z, 7f);
            PropInfo.BayArch = ac + dirIn * 2.5f;

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
                var rm = PropRocks.Mesh(sc, rng.Next(), 1);
                Quaternion rr = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                batch.Add(rm, i % 2 == 0 ? PropMats.RockDark : PropMats.Rock, Matrix4x4.TRS(rp, rr, Vector3.one));
                PropCol.Mesh(cols, rm, rp, rr, Vector3.one, "Boulder");
                excl.Add(rp.x, rp.z, s + 1f);
            }

            batch.Flush(root, "HiddenBayMesh", ShadowCastingMode.On, 400f);
            yield break;
        }

        /// Chunky rock arch in the vertical plane containing "along"; legs sink 2.5 m below the base point.
        static void BuildArch(PropMeshBuilder b, Vector3 along, float span, float height, int seed)
        {
            var pts = new List<Vector3>();
            var radii = new List<float>();
            pts.Add(along * -span - Vector3.up * 2.5f);
            radii.Add(1.6f);
            const int n = 8;
            for (int k = 0; k <= n; k++)
            {
                float th = Mathf.PI * k / n;
                pts.Add(along * (-Mathf.Cos(th) * span) + Vector3.up * (Mathf.Sin(th) * height));
                radii.Add(Mathf.Lerp(1.45f, 0.95f, Mathf.Sin(th)));
            }
            pts.Add(along * span - Vector3.up * 2.5f);
            radii.Add(1.6f);
            int vs = b.VertexCount;
            b.Tube(pts, radii, 7);
            Vector3 off = new Vector3(seed % 97, seed % 89, seed % 83);
            for (int i = vs; i < b.VertexCount; i++)
            {
                Vector3 v = b.V[i];
                float nz = PropNoise.Fbm3(v * 0.45f + off, 2, seed & 0xFFFF) - 0.5f;
                b.V[i] = v + b.N[i] * nz * 1.0f;
            }
        }

        /// Open low-poly rowing boat (local: length along z, keel at y = 0). Optionally broken (missing planks).
        public static void RowingBoat(PropBatch batch, Matrix4x4 trs, Material mat, bool broken)
        {
            var b = new PropMeshBuilder();
            const int secs = 7, around = 4;
            const float L = 3.6f, HB = 0.65f, D = 0.52f;
            for (int k = 0; k <= secs; k++)
            {
                float t = (float)k / secs;
                float z = (t - 0.5f) * L;
                float st = Mathf.Max(0f, Mathf.Sin(Mathf.PI * t));     // >= 0: Pow of a tiny negative sin(PI) was NaN
                float hb = HB * Mathf.Max(Mathf.Pow(st, 0.6f), t < 0.5f ? 0.38f : 0.03f);
                float depth = D + 0.14f * (2f * t - 1f) * (2f * t - 1f);
                float sheer = (1f - Mathf.Pow(st, 0.3f)) * 0.2f;
                for (int j = 0; j <= around; j++)
                {
                    float phi = Mathf.PI + Mathf.PI * j / around;
                    Vector3 p = new Vector3(hb * Mathf.Cos(phi), depth * (1f + Mathf.Sin(phi)) * 0.95f + sheer, z);
                    b.Vert(p, Vector3.up, Vector2.zero);
                }
            }
            for (int k = 0; k < secs; k++)
            {
                for (int j = 0; j < around; j++)
                {
                    if (broken && k >= 3 && k <= 4 && j == 3) continue;
                    int a = k * (around + 1) + j;
                    int c = a + around + 1;
                    b.Quad(a, a + 1, c + 1, c);
                }
            }
            // transom closing the stern section (k = 0)
            Vector3 tc = Vector3.zero;
            for (int j = 0; j <= around; j++) tc += b.V[j];
            tc /= around + 1;
            int center = b.Vert(tc, Vector3.back, Vector2.zero);
            for (int j = 0; j < around; j++) b.Tri(center, j, j + 1);
            b.DoubleSide(0, 0);
            // thwarts and keel
            b.Box(new Vector3(0f, D * 0.8f, 0.3f), new Vector3(HB * 1.7f, 0.06f, 0.24f));
            b.Box(new Vector3(0f, D * 0.8f, -0.8f), new Vector3(HB * 1.5f, 0.06f, 0.24f));
            b.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.08f, 0.08f, L * 0.9f));
            batch.B(mat).Append(b, trs);
        }

        static void Chest(PropBatch batch, Vector3 p, Quaternion r, System.Random rng)
        {
            const float W = 0.9f, H = 0.48f, Dp = 0.55f;
            Vector3 c = p + r * new Vector3(0f, H * 0.5f, 0f);
            batch.B(PropMats.WoodDark).Box(c, new Vector3(W, H, Dp), r);
            Quaternion lidAxis = r * Quaternion.FromToRotation(Vector3.up, Vector3.right);
            Vector3 lidBase = p + r * new Vector3(-W * 0.5f, H, 0f);
            batch.B(PropMats.Wood).Cylinder(lidBase, lidAxis, Dp * 0.5f, Dp * 0.5f, W, 6, true, true);
            var band = batch.B(PropMats.Metal);
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.32f;
                band.Box(c + r * new Vector3(x, 0f, 0f), new Vector3(0.06f, H + 0.02f, Dp + 0.03f), r);
                band.Cylinder(p + r * new Vector3(x - 0.03f, H, 0f), lidAxis, Dp * 0.5f + 0.015f, Dp * 0.5f + 0.015f, 0.06f, 6, true, true);
            }
            batch.B(PropMats.Gold).Box(p + r * new Vector3(0f, H * 0.9f, Dp * 0.5f + 0.02f), new Vector3(0.14f, 0.16f, 0.03f), r);
            for (int i = 0; i < 7; i++)
            {
                Vector3 cp = p + r * new Vector3(PropUtil.Range(rng, -0.5f, 0.5f), 0f, Dp * 0.5f + PropUtil.Range(rng, 0.1f, 0.5f));
                cp.y = G(cp.x, cp.z) + 0.005f;
                batch.B(PropMats.Gold).Cylinder(cp, Quaternion.Euler(PropUtil.Range(rng, -20f, 20f), 0f, PropUtil.Range(rng, -20f, 20f)), 0.04f, 0.04f, 0.012f, 6, true, true);
            }
        }
    }
}
