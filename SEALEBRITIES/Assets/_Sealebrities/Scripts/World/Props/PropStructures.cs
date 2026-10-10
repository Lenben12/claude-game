using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Dock, helipad and lighthouse in the low-poly style (chunky planks, 6-8 sided posts, flat colors).
    public static class PropStructures
    {
        static float G(float x, float z) => PropGround.H(x, z);

        // ================================================================== dock
        public static IEnumerator BuildDock(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("Dock", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var postCols = new GameObject("PostColliders");
            postCols.transform.SetParent(cols, false);
            postCols.transform.position = Vector3.zero;
            var batch = new PropBatch(0f);
            var rng = new System.Random(77);

            float deck = PropInfo.DockDeckY;
            float z0 = WorldLayout.DockStart.z;
            float x1 = WorldLayout.DockEnd.x;
            float x0 = WorldLayout.DockStart.x;
            const float halfW = 1.2f;

            // extend the deck inland until it meets the beach (max 14 m), otherwise add a ramp
            if (G(x0, z0) < deck - 0.2f)
            {
                for (float x = x0; x > x0 - 14f; x -= 0.5f)
                {
                    if (G(x, z0) >= deck - 0.1f) { x0 = x; break; }
                }
            }

            // chunky deck planks across the dock with a little color variation
            Material[] boards = { PropMats.Wood, PropMats.Wood, PropMats.WoodLight, PropMats.Var(LowPoly.Wood, 1), PropMats.Var(LowPoly.Wood, 2) };
            float bw = 0.3f, gap = 0.045f;
            for (float x = x0 + bw * 0.5f; x < x1; x += bw + gap)
            {
                var mat = boards[rng.Next(boards.Length)];
                batch.B(mat).Box(new Vector3(x, deck - 0.045f + PropUtil.Range(rng, -0.008f, 0.008f), z0 + PropUtil.Range(rng, -0.03f, 0.03f)),
                    new Vector3(bw, 0.09f, 2f * halfW + PropUtil.Range(rng, -0.05f, 0.08f)), Quaternion.Euler(0f, PropUtil.Range(rng, -1f, 1f), 0f));
            }
            var dark = batch.B(PropMats.WoodDark);
            for (int i = -1; i <= 1; i++)
                dark.Box(new Vector3((x0 + x1) * 0.5f, deck - 0.15f, z0 + i * 0.95f), new Vector3(x1 - x0, 0.14f, 0.14f));
            PropCol.Box(cols, new Vector3((x0 + x1) * 0.5f, deck - 0.15f, z0), new Vector3(x1 - x0, 0.3f, 2f * halfW), Quaternion.identity, "DockDeck");

            // round 6-sided posts (darker where they are wet), cross beams, braces
            int pi = 0;
            for (float x = x1 - 0.2f; x >= x0 + 0.2f; x -= 3f, pi++)
            {
                bool any = false;
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = z0 + s * (halfW + 0.06f);
                    float g = G(x, z);
                    if (g > deck - 0.35f) continue;
                    any = true;
                    float bottom = g - 0.6f, top = deck - 0.2f;
                    float wetTop = Mathf.Min(0.25f, top);
                    if (bottom < wetTop)
                        batch.B(PropMats.WoodWet).Cylinder(new Vector3(x, bottom, z), Quaternion.identity, 0.17f, 0.16f, wetTop - bottom, 6);
                    float dryBottom = Mathf.Max(bottom, wetTop);
                    dark.Cylinder(new Vector3(x, dryBottom, z), Quaternion.identity, 0.16f, 0.15f, top + 0.45f - dryBottom, 6, false, true);
                    PropCol.UprightCapsule(postCols, new Vector3(x, bottom, z), 0.17f, top + 0.45f - bottom);
                    if (pi % 2 == 1 && g < -1.2f)
                    {
                        float zb = z0 - s * (halfW - 0.1f);
                        dark.Beam(new Vector3(x, Mathf.Max(g + 0.3f, -2.5f), z), new Vector3(x - 1.4f, deck - 0.3f, zb), 0.1f, 0.14f);
                    }
                }
                if (any) dark.Box(new Vector3(x, deck - 0.24f, z0), new Vector3(0.18f, 0.16f, 2f * halfW + 0.4f));
            }

            // ramp down to the beach if the deck could not reach it
            float gStart = G(x0, z0);
            float rampStart = x0;
            if (gStart < deck - 0.25f)
            {
                float rise = deck - gStart + 0.05f;
                float len = rise / Mathf.Tan(14f * Mathf.Deg2Rad);
                float ang = Mathf.Atan2(rise, len) * Mathf.Rad2Deg;
                Quaternion r = Quaternion.Euler(0f, 0f, ang);
                Vector3 a = new Vector3(x0 - len, gStart - 0.05f, z0), b = new Vector3(x0, deck, z0);
                float L = (b - a).magnitude;
                Vector3 dir = (b - a) / L;
                int k = 0;
                for (float t = 0.17f; t < L; t += 0.34f, k++)
                {
                    Vector3 p = a + dir * t + r * new Vector3(0f, -0.04f, 0f);
                    batch.B(boards[k % boards.Length]).Box(p, new Vector3(0.3f, 0.08f, 1.8f), r);
                }
                dark.Beam(a + new Vector3(0f, -0.12f, -0.8f), b + new Vector3(0f, -0.12f, -0.8f), 0.1f, 0.16f);
                dark.Beam(a + new Vector3(0f, -0.12f, 0.8f), b + new Vector3(0f, -0.12f, 0.8f), 0.1f, 0.16f);
                PropCol.Box(cols, (a + b) * 0.5f - r * new Vector3(0f, 0.08f, 0f), new Vector3(L, 0.16f, 1.8f), r, "DockRamp");
                rampStart = x0 - len;
            }
            excl.AddLine(new Vector3(rampStart - 2f, 0f, z0), new Vector3(x1 + 2f, 0f, z0), 3.5f);

            if (PropRun.Due()) yield return null;

            // ladder into the water at the end
            var metal = batch.B(PropMats.Metal);
            float lx = x1 + 0.1f;
            for (int s = -1; s <= 1; s += 2)
            {
                float zr = z0 + s * 0.38f;
                metal.Rod(new Vector3(lx, -2.4f, zr), new Vector3(lx, deck + 0.85f, zr), 0.035f, 0.035f, 4);
                metal.Rod(new Vector3(lx, deck + 0.85f, zr), new Vector3(lx - 0.38f, deck + 0.85f, zr), 0.035f, 0.035f, 4);
                metal.Rod(new Vector3(lx - 0.38f, deck + 0.85f, zr), new Vector3(lx - 0.38f, deck, zr), 0.035f, 0.035f, 4);
            }
            for (float y = -2.2f; y < deck - 0.05f; y += 0.3f)
                metal.Rod(new Vector3(lx, y, z0 - 0.38f), new Vector3(lx, y, z0 + 0.38f), 0.028f, 0.028f, 4);
            var ladderGo = new GameObject("DockLadder");
            ladderGo.transform.SetParent(root, false);
            ladderGo.transform.position = new Vector3(lx, deck * 0.5f - 1.2f, z0);
            PropInfo.DockLadderTop = new Vector3(x1 - 0.4f, deck, z0);
            PropInfo.DockLadderBottom = new Vector3(lx + 0.45f, -1.5f, z0);

            // bollards
            float[] bxs = { x1 - 0.45f, x1 - 15f, x0 + 6f };
            foreach (float bx in bxs)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 bp = new Vector3(bx, deck, z0 + s * (halfW - 0.18f));
                    batch.B(PropMats.DarkMetal).Cylinder(bp, Quaternion.identity, 0.12f, 0.1f, 0.32f, 8);
                    batch.B(PropMats.DarkMetal).Cylinder(bp + Vector3.up * 0.32f, Quaternion.identity, 0.17f, 0.17f, 0.07f, 8, true, true);
                }
            }

            // rope coils
            Vector3[] coils = { new Vector3(x1 - 7.5f, deck, z0 + 0.75f), new Vector3(x0 + 12.5f, deck, z0 - 0.8f), new Vector3(x1 - 1.2f, deck, z0 + 0.8f) };
            foreach (var cp in coils)
            {
                for (int k = 0; k < 3; k++)
                    batch.B(PropMats.Rope).Torus(cp + Vector3.up * (0.045f + k * 0.075f), Quaternion.Euler(0f, k * 25f, 0f), 0.3f - k * 0.05f, 0.045f, 8, 3);
            }

            // crates on the north side
            Vector3[] crates = { new Vector3(x1 - 4.4f, deck, z0 + 0.65f), new Vector3(x1 - 4.9f, deck, z0 + 0.62f), new Vector3(x1 - 4.6f, deck + 0.55f, z0 + 0.66f) };
            for (int i = 0; i < crates.Length; i++)
            {
                float sz = i == 2 ? 0.45f : 0.55f;
                Quaternion r = Quaternion.Euler(0f, PropUtil.Range(rng, -12f, 12f), 0f);
                Vector3 c = crates[i] + new Vector3(0f, sz * 0.5f, 0f);
                batch.B(PropMats.Wood).Box(c, Vector3.one * sz * 0.94f, r);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int szz = -1; szz <= 1; szz += 2)
                        dark.Box(c + r * new Vector3(sx * (sz * 0.5f - 0.035f), 0f, szz * (sz * 0.5f - 0.035f)), new Vector3(0.08f, sz, 0.08f), r);
            }
            PropCol.Box(cols, new Vector3(x1 - 4.65f, deck + 0.5f, z0 + 0.65f), new Vector3(1.2f, 1f, 0.6f), Quaternion.identity);

            // bucket and tackle box next to the fishing spot
            Vector3 fs = WorldLayout.FishingSpot;
            Vector3 bucket = new Vector3(Mathf.Clamp(fs.x - 1.2f, x0 + 1f, x1 - 1f), deck, z0 - halfW + 0.35f);
            batch.B(PropMats.DrumBlue).Cylinder(bucket, Quaternion.identity, 0.15f, 0.19f, 0.32f, 6, true, false);
            batch.B(PropMats.DarkMetal).Cylinder(bucket + Vector3.up * 0.005f, Quaternion.identity, 0.145f, 0.185f, 0.31f, 6, false, false, 1f, 1f, 0f, true);
            Quaternion tbr = Quaternion.Euler(0f, 15f, 0f);
            Vector3 tbp = bucket + new Vector3(-0.55f, 0.11f, 0.1f);
            batch.B(PropMats.DrumYellow).Box(tbp, new Vector3(0.42f, 0.22f, 0.24f), tbr);
            batch.B(PropMats.Orange).Box(tbp + Vector3.up * 0.125f, new Vector3(0.44f, 0.05f, 0.26f), tbr);

            // fenders hanging at the end
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 fp = new Vector3(x1 - 1.5f, deck - 0.75f, z0 + s * (halfW + 0.2f));
                batch.B(PropMats.Orange).Cylinder(fp, Quaternion.identity, 0.14f, 0.14f, 0.5f, 6, true, true);
                batch.B(PropMats.White).Cylinder(fp + Vector3.up * 0.2f, Quaternion.identity, 0.145f, 0.145f, 0.1f, 6);
                batch.B(PropMats.Rope).Rod(fp + Vector3.up * 0.5f, new Vector3(fp.x, deck + 0.02f, z0 + s * halfW), 0.018f, 0.018f, 3);
            }

            // lantern on a post at the end (north corner)
            Vector3 lp = new Vector3(x1 - 0.25f, deck, z0 + halfW - 0.12f);
            dark.Box(lp + new Vector3(0f, 0.8f, 0f), new Vector3(0.12f, 1.6f, 0.12f));
            dark.Box(lp + new Vector3(-0.15f, 1.55f, 0f), new Vector3(0.36f, 0.08f, 0.08f));
            Vector3 lan = lp + new Vector3(-0.3f, 1.28f, 0f);
            batch.B(PropMats.DarkMetal).Cylinder(lan + new Vector3(0f, 0.22f, 0f), Quaternion.identity, 0.13f, 0f, 0.1f, 6, true, false);
            batch.B(PropMats.DarkMetal).Cylinder(lan - new Vector3(0f, 0.03f, 0f), Quaternion.identity, 0.11f, 0.11f, 0.04f, 6, true, true);
            batch.B(PropMats.Glass).Cylinder(lan, Quaternion.identity, 0.09f, 0.09f, 0.22f, 6);
            batch.B(PropMats.DockBulb).Ico(lan + new Vector3(0f, 0.1f, 0f), new Vector3(0.05f, 0.07f, 0.05f), Quaternion.identity, 0);
            batch.B(PropMats.Rope).Rod(lan + new Vector3(0f, 0.3f, 0f), lp + new Vector3(-0.3f, 1.55f, 0f), 0.015f, 0.015f, 3);
            var light = PropUtil.AddPointLight(root, lan + new Vector3(0f, 0.1f, 0f), new Color(1f, 0.74f, 0.42f), 12f, 2.2f, "DockLantern");
            var nl = PropNightLight.Attach(light.gameObject, light, PropMats.DockBulb, new Color(3.5f, 2.4f, 1.2f), 0.15f, 0.5f, true);
            nl.flickerAmount = 0.12f;

            batch.Flush(root, "DockMesh", ShadowCastingMode.On, 0f);
        }

        // ================================================================== helipad
        public static IEnumerator BuildHelipad(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Helipad;
            const float R = 6f;
            const int Sides = 12;
            PropGround.MinMax(c.x, c.z, R + 0.5f, out float gmin, out float gmax);
            float top = Mathf.Max(gmax + 0.2f, 0.9f);
            float bottom = gmin - 0.8f;
            PropInfo.HelipadTopY = top;

            var root = PropUtil.WorldRoot("Helipad", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            // flat concrete disk: the top carries the crisp "H" block texture, a yellow ring is geometry
            Vector3 ct = new Vector3(c.x, top, c.z);
            batch.B(PropMats.Helipad).Disk(ct, R, Sides, Quaternion.identity, true, 1f, true, 15f);
            // (a +y rotation of -15° turns the ring's vertex angles by +15°, matching the disk's angle offset)
            batch.B(PropMats.Marking).Ring(ct + Vector3.up * 0.012f, R - 0.75f, R - 0.4f, Sides, Quaternion.Euler(0f, -15f, 0f));
            batch.B(PropMats.Concrete).Cylinder(new Vector3(c.x, top - 0.35f, c.z), Quaternion.identity, R + 0.05f, R, 0.35f, Sides, false, false, 1f, 1f, 0f, false, 15f);
            batch.B(PropMats.ConcreteDark).Cylinder(new Vector3(c.x, bottom, c.z), Quaternion.identity, R + 0.15f, R + 0.05f, top - 0.35f - bottom, Sides, false, false, 1f, 1f, 0f, false, 15f);
            var cb = new PropMeshBuilder();
            cb.Cylinder(new Vector3(0f, bottom - top, 0f), Quaternion.identity, R, R, top - bottom, 24, true, true);
            PropCol.Mesh(cols, cb.ToMesh("HelipadCollider", false), ct, Quaternion.identity, Vector3.one, "HelipadCol");

            // edge lights (emissive, switched by a night controller)
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 2f / 8f;
                Vector3 p = ct + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R - 0.2f);
                batch.B(PropMats.DarkMetal).Cylinder(p, Quaternion.identity, 0.1f, 0.09f, 0.06f, 6, false, true);
                batch.B(PropMats.PadBulb).Ico(p + Vector3.up * 0.07f, new Vector3(0.08f, 0.07f, 0.08f), Quaternion.identity, 0);
            }
            var lightsGo = new GameObject("PadLights");
            lightsGo.transform.SetParent(root, false);
            lightsGo.transform.position = ct + Vector3.up * 1.2f;
            var padLight = lightsGo.AddComponent<Light>();
            padLight.type = LightType.Point;
            padLight.color = new Color(1f, 0.85f, 0.55f);
            padLight.range = 11f;
            padLight.intensity = 0.9f;
            padLight.shadows = LightShadows.None;
            PropNightLight.Attach(lightsGo, padLight, PropMats.PadBulb, new Color(3f, 2.4f, 0.8f), 0.15f, 0.4f, false);

            // steps toward the hut when the pad stands above the ground
            Vector3 dir = WorldLayout.Hut - c;
            dir.y = 0f;
            dir.Normalize();
            Quaternion sr = Quaternion.LookRotation(dir);
            for (int i = 0; i < 25; i++)
            {
                float stepTop = top - (i + 1) * 0.2f;
                Vector3 p = c + dir * (R + 0.16f + i * 0.32f);
                float g = G(p.x, p.z);
                if (stepTop < g - 0.02f) break;
                float h = stepTop - (g - 0.3f);
                Vector3 sc = new Vector3(p.x, stepTop - h * 0.5f, p.z);
                batch.B(i % 2 == 0 ? PropMats.Concrete : PropMats.ConcreteDark).Box(sc, new Vector3(2.2f, h, 0.32f), sr);
                PropCol.Box(cols, sc, new Vector3(2.2f, h, 0.32f), sr);
            }

            // windsock
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 sockPos = c + side * (R + 2.2f) - dir * 1.5f;
            sockPos.y = G(sockPos.x, sockPos.z);
            Windsock(root, sockPos, batch);
            PropCol.Capsule(cols, sockPos + Vector3.up * 2.5f, 0.1f, 5f, Quaternion.identity);

            // drums and a pallet
            var rng = new System.Random(55);
            Vector3 dp = c - side * (R + 2.5f) - dir * 1f;
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = dp + new Vector3(PropUtil.Range(rng, -0.9f, 0.9f), 0f, PropUtil.Range(rng, -0.9f, 0.9f));
                p.y = G(p.x, p.z) - 0.04f;
                Drum(batch, p, i == 1 ? PropMats.DrumRed : PropMats.DrumYellow);
                PropCol.Capsule(cols, p + Vector3.up * 0.44f, 0.3f, 0.88f, Quaternion.identity);
            }
            Vector3 pal = dp + dir * 2.2f;
            pal.y = G(pal.x, pal.z);
            for (int k = 0; k < 5; k++)
                batch.B(PropMats.WoodLight).Box(pal + sr * new Vector3(-0.48f + k * 0.24f, 0.13f, 0f), new Vector3(0.18f, 0.04f, 1.2f), sr);
            for (int k = -1; k <= 1; k++)
                batch.B(PropMats.Wood).Box(pal + sr * new Vector3(0f, 0.06f, k * 0.5f), new Vector3(1.1f, 0.11f, 0.11f), sr);

            batch.Flush(root, "HelipadMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, R + 4.5f);
            excl.Add(sockPos.x, sockPos.z, 1.5f);
            excl.Add(dp.x, dp.z, 3f);
            yield break;
        }

        /// Standing oil drum: 8-sided body with two darker rims.
        public static void Drum(PropBatch batch, Vector3 basePos, Material mat)
        {
            const float r = 0.29f, h = 0.88f;
            batch.B(mat).Cylinder(basePos, Quaternion.identity, r, r, h, 8, false, true);
            var rim = batch.B(PropMats.DarkMetal);
            rim.Cylinder(basePos + Vector3.up * 0.28f, Quaternion.identity, r + 0.012f, r + 0.012f, 0.05f, 8);
            rim.Cylinder(basePos + Vector3.up * 0.6f, Quaternion.identity, r + 0.012f, r + 0.012f, 0.05f, 8);
        }

        static void Windsock(Transform root, Vector3 groundPos, PropBatch batch)
        {
            const float poleH = 5f;
            batch.B(PropMats.Metal).Cylinder(groundPos - Vector3.up * 0.3f, Quaternion.identity, 0.08f, 0.06f, poleH + 0.3f, 6, false, true);
            var pivot = new GameObject("Windsock");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = groundPos + Vector3.up * poleH;
            var sockT = new GameObject("Sock").transform;
            sockT.SetParent(pivot.transform, false);

            // four orange / white segments (6-sided frustums), each segment its own color
            var orange = new PropMeshBuilder();
            var white = new PropMeshBuilder();
            Quaternion along = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            const int segs = 4;
            const float len = 2.3f, rA = 0.34f, rB = 0.13f;
            for (int i = 0; i < segs; i++)
            {
                float t0 = (float)i / segs, t1 = (float)(i + 1) / segs;
                var b = i % 2 == 0 ? orange : white;
                b.Cylinder(new Vector3(0.12f + t0 * len, 0f, 0f), along, Mathf.Lerp(rA, rB, t0), Mathf.Lerp(rA, rB, t1), len / segs, 6);
                b.Cylinder(new Vector3(0.12f + t0 * len, 0f, 0f), along, Mathf.Lerp(rA, rB, t0) - 0.01f, Mathf.Lerp(rA, rB, t1) - 0.01f, len / segs, 6, false, false, 1f, 1f, 0f, true);
            }
            PropUtil.MeshObject(sockT, "SockOrange", orange.ToMesh("WindsockOrange"), PropMats.Orange, Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(sockT, "SockWhite", white.ToMesh("WindsockWhite"), PropMats.White, Vector3.zero, Quaternion.identity);
            var hb = new PropMeshBuilder();
            hb.Torus(new Vector3(0.12f, 0f, 0f), along, 0.34f, 0.025f, 8, 3);
            hb.Rod(Vector3.zero, new Vector3(0.12f, 0f, 0f), 0.03f, 0.03f, 4);
            PropUtil.MeshObject(sockT, "Hoop", hb.ToMesh("WindsockHoop"), PropMats.Metal, Vector3.zero, Quaternion.identity);
            var ws = pivot.AddComponent<PropWindsock>();
            ws.sock = sockT;
            ws.baseYaw = 35f;
        }

        // ================================================================== lighthouse
        public static IEnumerator BuildLighthouse(Transform island, PropExclusion excl)
        {
            Vector3 c = WorldLayout.Lighthouse;
            PropGround.MinMax(c.x, c.z, 3.8f, out float gmin, out float gmax);
            float baseTop = Mathf.Max(gmax + 0.4f, 1.5f);
            float footY = gmin - 1.5f;

            var root = PropUtil.WorldRoot("Lighthouse", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            // octagonal stone foundation
            batch.B(PropMats.Stone).Cylinder(new Vector3(c.x, footY, c.z), Quaternion.identity, 3.75f, 3.5f, baseTop - footY, 8, false, true, 1f, 1f, 0f, false, 22.5f);
            var fcb = new PropMeshBuilder();
            fcb.Cylinder(Vector3.zero, Quaternion.identity, 3.75f, 3.5f, baseTop - footY, 8, true, true, 1f, 1f, 0f, false, 22.5f);
            PropCol.Mesh(cols, fcb.ToMesh("LighthouseFoundation", false), new Vector3(c.x, footY, c.z), Quaternion.identity, Vector3.one);

            // chunky 8-sided tower with red / white bands
            const float H = 15.5f, r0 = 2.4f, r1 = 1.65f;
            const int bands = 6;
            Vector3 tb = new Vector3(c.x, baseTop, c.z);
            for (int i = 0; i < bands; i++)
            {
                float t0 = (float)i / bands, t1 = (float)(i + 1) / bands;
                var m = i % 2 == 0 ? PropMats.White : PropMats.RedMetal;
                batch.B(m).Cylinder(tb + Vector3.up * (H * t0), Quaternion.identity, Mathf.Lerp(r0, r1, t0), Mathf.Lerp(r0, r1, t1), H / bands + 0.01f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            }
            var tcb = new PropMeshBuilder();
            tcb.Cylinder(Vector3.zero, Quaternion.identity, r0, r1, H + 3.6f, 16, true, true);
            PropCol.Mesh(cols, tcb.ToMesh("LighthouseTower", false), tb, Quaternion.identity, Vector3.one);

            // door facing the hut path
            Vector3 toHut = WorldLayout.Hut - c;
            toHut.y = 0f;
            toHut.Normalize();
            Quaternion dr = Quaternion.LookRotation(toHut);
            float apothem = r0 * Mathf.Cos(Mathf.PI / 8f);
            batch.B(PropMats.Shutter).Box(tb + toHut * (apothem - 0.02f) + Vector3.up * 1.05f, new Vector3(1.1f, 2.1f, 0.24f), dr);
            batch.B(PropMats.Wood).Box(tb + toHut * (apothem - 0.06f) + Vector3.up * 1.1f, new Vector3(1.36f, 2.32f, 0.2f), dr);
            batch.B(PropMats.Stone).Box(tb + toHut * (r0 + 0.45f) + Vector3.up * -0.1f, new Vector3(1.6f, 0.2f, 0.9f), dr);
            // small windows spiralling up
            for (int i = 0; i < 4; i++)
            {
                float y = 4.2f + i * 3f;
                float a = 45f * (1 + i * 2) * Mathf.Deg2Rad;   // octagon face centers (vertices sit at 22.5° + k·45°)
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rr = Mathf.Lerp(r0, r1, y / H) * Mathf.Cos(Mathf.PI / 8f);
                Quaternion wr = Quaternion.LookRotation(d);
                batch.B(PropMats.Wood).Box(tb + d * (rr - 0.02f) + Vector3.up * y, new Vector3(0.7f, 0.95f, 0.16f), wr);
                batch.B(PropMats.Ink).Box(tb + d * (rr + 0.04f) + Vector3.up * y, new Vector3(0.48f, 0.72f, 0.06f), wr);
            }
            if (PropRun.Due()) yield return null;

            // gallery: chunky platform with a simple railing
            float gy = baseTop + H;
            var dm = batch.B(PropMats.DarkMetal);
            dm.Cylinder(new Vector3(c.x, gy - 0.6f, c.z), Quaternion.identity, r1 - 0.05f, 2.8f, 0.6f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            dm.Cylinder(new Vector3(c.x, gy, c.z), Quaternion.identity, 2.8f, 2.8f, 0.16f, 8, false, true, 1f, 1f, 0f, false, 22.5f);
            const int posts = 8;
            for (int i = 0; i < posts; i++)
            {
                float a0 = (22.5f + i * 45f) * Mathf.Deg2Rad, a1 = (22.5f + (i + 1) * 45f) * Mathf.Deg2Rad;
                Vector3 p0 = new Vector3(c.x + Mathf.Cos(a0) * 2.68f, gy + 0.16f, c.z + Mathf.Sin(a0) * 2.68f);
                Vector3 p1 = new Vector3(c.x + Mathf.Cos(a1) * 2.68f, gy + 0.16f, c.z + Mathf.Sin(a1) * 2.68f);
                dm.Rod(p0, p0 + Vector3.up * 1.0f, 0.045f, 0.045f, 4);
                dm.Beam(p0 + Vector3.up * 1.0f, p1 + Vector3.up * 1.0f, 0.08f, 0.08f);
                dm.Beam(p0 + Vector3.up * 0.5f, p1 + Vector3.up * 0.5f, 0.05f, 0.05f);
            }

            // lamp room: red base, glass, mullions, red cone roof with a ball
            float ly = gy + 0.16f;
            batch.B(PropMats.RedMetal).Cylinder(new Vector3(c.x, ly, c.z), Quaternion.identity, 1.4f, 1.4f, 0.55f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            batch.B(PropMats.Glass).Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 1.32f, 1.32f, 1.8f, 8, false, false, 1f, 1f, 0f, false, 22.5f);
            for (int i = 0; i < 8; i++)
            {
                float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * 1.33f, ly + 0.55f, c.z + Mathf.Sin(a) * 1.33f);
                dm.Rod(p, p + Vector3.up * 1.8f, 0.06f, 0.06f, 4);
            }
            float roofY = ly + 2.35f;
            batch.B(PropMats.RedMetal).Cylinder(new Vector3(c.x, roofY, c.z), Quaternion.identity, 1.7f, 0f, 1.35f, 8, true, false, 1f, 1f, 0f, false, 22.5f);
            dm.Cylinder(new Vector3(c.x, roofY - 0.1f, c.z), Quaternion.identity, 1.5f, 1.5f, 0.1f, 8, true, false, 1f, 1f, 0f, false, 22.5f);
            dm.Ico(new Vector3(c.x, roofY + 1.45f, c.z), Vector3.one * 0.22f, Quaternion.identity, 0);
            dm.Rod(new Vector3(c.x, roofY + 1.6f, c.z), new Vector3(c.x, roofY + 2.6f, c.z), 0.035f, 0.02f, 4);

            // lamp: lens (static, emissive) + rotating pivot with beams and spot lights
            Vector3 lampC = new Vector3(c.x, ly + 1.35f, c.z);
            batch.B(PropMats.Lens).Cylinder(lampC - Vector3.up * 0.4f, Quaternion.identity, 0.42f, 0.42f, 0.8f, 8, true, true);
            dm.Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 0.26f, 0.2f, 0.42f, 6, false, true);

            var pivot = new GameObject("LighthouseLamp");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = lampC;
            var lamp = pivot.AddComponent<PropLighthouseLamp>();
            lamp.pivot = pivot.transform;
            lamp.lens = PropMats.Lens;

            var beamMat = new Material(MaterialLibrary.UnlitShader) { name = "Prop_LighthouseBeam" };
            beamMat.SetTexture("_BaseMap", PropTex.Beam);
            beamMat.SetColor("_BaseColor", new Color(1f, 0.95f, 0.75f, 0f));
            MaterialLibrary.MakeTransparent(beamMat, true);
            beamMat.SetFloat("_DstBlend", (float)BlendMode.One);
            lamp.beamMat = beamMat;

            var beams = new Renderer[2];
            var spots = new Light[2];
            for (int i = 0; i < 2; i++)
            {
                Vector3 fwd = i == 0 ? Vector3.forward : Vector3.back;
                // pitch sign flips for the backward beam so both tilt slightly downward
                Quaternion tiltDown = Quaternion.Euler(i == 0 ? 2.5f : -2.5f, 0f, 0f);
                var bb = new PropMeshBuilder();
                bb.Cylinder(Vector3.zero, Quaternion.FromToRotation(Vector3.up, fwd), 0.3f, 8f, 110f, 10, false, false, 1f, 1f / 110f);
                var mr = PropUtil.MeshObject(pivot.transform, "Beam" + i, bb.ToMesh("LighthouseBeam", false), beamMat, Vector3.zero, tiltDown, false);
                mr.receiveShadows = false;
                mr.enabled = false;
                beams[i] = mr;

                var sgo = new GameObject("Spot" + i);
                sgo.transform.SetParent(pivot.transform, false);
                sgo.transform.localRotation = Quaternion.LookRotation(tiltDown * fwd);
                var sl = sgo.AddComponent<Light>();
                sl.type = LightType.Spot;
                sl.color = new Color(1f, 0.93f, 0.75f);
                sl.range = 180f;
                sl.spotAngle = 10f;
                sl.innerSpotAngle = 4f;
                sl.intensity = 0f;
                sl.shadows = LightShadows.None;
                sl.enabled = false;
                spots[i] = sl;
            }
            lamp.beams = beams;
            lamp.spots = spots;
            lamp.spotIntensity = 60f;
            var glow = PropUtil.AddPointLight(pivot.transform, lampC, new Color(1f, 0.85f, 0.6f), 14f, 0f, "LampGlow");
            glow.enabled = false;
            lamp.glow = glow;

            batch.Flush(root, "LighthouseMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, 8f);
            excl.AddLine(c, c + toHut * 6f, 2f);
        }
    }
}
