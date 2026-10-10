using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Dock, helipad and lighthouse.
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

            // deck boards
            float bw = 0.24f, gap = 0.035f;
            int row = 0;
            for (float x = x0 + bw * 0.5f; x < x1; x += bw + gap, row++)
            {
                var mat = rng.NextDouble() < 0.25 ? PropMats.WoodPale : PropMats.Wood;
                Vector2 uvo = new Vector2(PropUtil.Range(rng, 0f, 2f), (row % 8) * 0.25f + 0.012f);
                batch.B(mat).Box(new Vector3(x, deck - 0.025f + PropUtil.Range(rng, -0.006f, 0.006f), z0), new Vector3(bw, 0.05f, 2f * halfW),
                    Quaternion.Euler(0f, PropUtil.Range(rng, -0.6f, 0.6f), 0f), 1f, uvo, PropUV.LongU);
            }
            var dark = batch.B(PropMats.WoodDark);
            for (int i = -1; i <= 1; i++)
                dark.Box(new Vector3((x0 + x1) * 0.5f, deck - 0.11f, z0 + i * 0.95f), new Vector3(x1 - x0, 0.12f, 0.1f));
            PropCol.Box(cols, new Vector3((x0 + x1) * 0.5f, deck - 0.15f, z0), new Vector3(x1 - x0, 0.3f, 2f * halfW), Quaternion.identity, "DockDeck");

            // posts, caps, braces
            int pi = 0;
            for (float x = x1 - 0.2f; x >= x0 + 0.2f; x -= 3f, pi++)
            {
                bool any = false;
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = z0 + s * (halfW + 0.05f);
                    float g = G(x, z);
                    if (g > deck - 0.35f) continue;
                    any = true;
                    float bottom = g - 0.6f, top = deck - 0.17f;
                    float wetTop = Mathf.Min(0.2f, top);
                    if (bottom < wetTop)
                        batch.B(PropMats.WoodWet).Cylinder(new Vector3(x, bottom, z), Quaternion.identity, 0.15f, 0.14f, wetTop - bottom, 8, false, false, 0.9f, 1f);
                    float dryBottom = Mathf.Max(bottom, wetTop);
                    dark.Cylinder(new Vector3(x, dryBottom, z), Quaternion.identity, 0.14f, 0.13f, top + 0.35f - dryBottom, 8, false, true, 0.85f, 1f);
                    PropCol.UprightCapsule(postCols, new Vector3(x, bottom, z), 0.16f, top + 0.35f - bottom);
                    if (pi % 2 == 1 && g < -1.2f)
                    {
                        float zb = z0 - s * (halfW - 0.1f);
                        dark.Beam(new Vector3(x, Mathf.Max(g + 0.3f, -2.5f), z), new Vector3(x - 1.4f, deck - 0.3f, zb), 0.08f, 0.12f);
                    }
                }
                if (any) dark.Box(new Vector3(x, deck - 0.2f, z0), new Vector3(0.16f, 0.14f, 2f * halfW + 0.36f));
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
                for (float t = 0.15f; t < L; t += 0.28f)
                {
                    Vector3 p = a + dir * t + r * new Vector3(0f, -0.025f, 0f);
                    batch.B(PropMats.Wood).Box(p, new Vector3(0.24f, 0.05f, 1.8f), r, 1f, new Vector2(t, 0.012f), PropUV.LongU);
                }
                dark.Beam(a + new Vector3(0f, -0.1f, -0.8f), b + new Vector3(0f, -0.1f, -0.8f), 0.08f, 0.14f);
                dark.Beam(a + new Vector3(0f, -0.1f, 0.8f), b + new Vector3(0f, -0.1f, 0.8f), 0.08f, 0.14f);
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
                metal.Rod(new Vector3(lx, -2.4f, zr), new Vector3(lx, deck + 0.85f, zr), 0.025f, 0.025f, 6);
                metal.Rod(new Vector3(lx, deck + 0.85f, zr), new Vector3(lx - 0.38f, deck + 0.85f, zr), 0.025f, 0.025f, 6);
                metal.Rod(new Vector3(lx - 0.38f, deck + 0.85f, zr), new Vector3(lx - 0.38f, deck, zr), 0.025f, 0.025f, 6);
            }
            for (float y = -2.2f; y < deck - 0.05f; y += 0.3f)
                metal.Rod(new Vector3(lx, y, z0 - 0.38f), new Vector3(lx, y, z0 + 0.38f), 0.018f, 0.018f, 5);
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
                    batch.B(PropMats.DarkMetal).Cylinder(bp, Quaternion.identity, 0.11f, 0.1f, 0.32f, 10, false, false, 0.7f, 1f);
                    batch.B(PropMats.DarkMetal).Cylinder(bp + Vector3.up * 0.32f, Quaternion.identity, 0.16f, 0.16f, 0.06f, 10, true, true, 1f, 1f);
                }
            }

            // rope coils
            Vector3[] coils = { new Vector3(x1 - 7.5f, deck, z0 + 0.75f), new Vector3(x0 + 12.5f, deck, z0 - 0.8f), new Vector3(x1 - 1.2f, deck, z0 + 0.8f) };
            foreach (var cp in coils)
            {
                for (int k = 0; k < 3; k++)
                    batch.B(PropMats.Rope).Torus(cp + Vector3.up * (0.035f + k * 0.06f), Quaternion.Euler(0f, k * 40f, 0f), 0.3f - k * 0.05f, 0.035f, 16, 5, 6f, 1f);
            }

            // crates on the north side
            Vector3[] crates = { new Vector3(x1 - 4.4f, deck, z0 + 0.65f), new Vector3(x1 - 4.9f, deck, z0 + 0.62f), new Vector3(x1 - 4.6f, deck + 0.55f, z0 + 0.66f) };
            for (int i = 0; i < crates.Length; i++)
            {
                float sz = i == 2 ? 0.45f : 0.55f;
                Quaternion r = Quaternion.Euler(0f, PropUtil.Range(rng, -12f, 12f), 0f);
                batch.B(PropMats.Wood).Box(crates[i] + new Vector3(0f, sz * 0.5f, 0f), Vector3.one * sz, r, 1f, new Vector2(i * 0.3f, 0.25f));
            }
            PropCol.Box(cols, new Vector3(x1 - 4.65f, deck + 0.5f, z0 + 0.65f), new Vector3(1.2f, 1f, 0.6f), Quaternion.identity);

            // bucket and tackle box next to the fishing spot
            Vector3 fs = WorldLayout.FishingSpot;
            Vector3 bucket = new Vector3(Mathf.Clamp(fs.x - 1.2f, x0 + 1f, x1 - 1f), deck, z0 - halfW + 0.35f);
            batch.B(PropMats.DrumBlue).Cylinder(bucket, Quaternion.identity, 0.15f, 0.18f, 0.32f, 12, true, false, 1f, 1f);
            batch.B(PropMats.DarkMetal).Cylinder(bucket + Vector3.up * 0.005f, Quaternion.identity, 0.145f, 0.175f, 0.31f, 12, false, false, 1f, 1f, 0f, true);
            batch.B(PropMats.DrumYellow).Box(bucket + new Vector3(-0.55f, 0.11f, 0.1f), new Vector3(0.42f, 0.22f, 0.24f), Quaternion.Euler(0f, 15f, 0f));

            // fenders hanging at the end
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 fp = new Vector3(x1 - 1.5f, deck - 0.75f, z0 + s * (halfW + 0.2f));
                batch.B(PropMats.FabricSock).Cylinder(fp, Quaternion.identity, 0.13f, 0.13f, 0.5f, 10, true, true, 1f, 2f);
                batch.B(PropMats.Rope).Rod(fp + Vector3.up * 0.5f, new Vector3(fp.x, deck + 0.02f, z0 + s * halfW), 0.012f, 0.012f, 4);
            }

            // lantern on a post at the end (north corner)
            Vector3 lp = new Vector3(x1 - 0.25f, deck, z0 + halfW - 0.12f);
            dark.Box(lp + new Vector3(0f, 0.8f, 0f), new Vector3(0.1f, 1.6f, 0.1f));
            dark.Box(lp + new Vector3(-0.15f, 1.55f, 0f), new Vector3(0.36f, 0.06f, 0.06f));
            Vector3 lan = lp + new Vector3(-0.3f, 1.28f, 0f);
            batch.B(PropMats.DarkMetal).Cylinder(lan + new Vector3(0f, 0.22f, 0f), Quaternion.identity, 0.12f, 0.03f, 0.08f, 8, true, false);
            batch.B(PropMats.DarkMetal).Cylinder(lan - new Vector3(0f, 0.03f, 0f), Quaternion.identity, 0.1f, 0.1f, 0.04f, 8, true, true);
            batch.B(PropMats.Glass).Cylinder(lan, Quaternion.identity, 0.085f, 0.085f, 0.22f, 8);
            batch.B(PropMats.DockBulb).Blob(lan + new Vector3(0f, 0.1f, 0f), new Vector3(0.04f, 0.06f, 0.04f), Quaternion.identity, 2, null);
            batch.B(PropMats.Rope).Rod(lan + new Vector3(0f, 0.3f, 0f), lp + new Vector3(-0.3f, 1.55f, 0f), 0.01f, 0.01f, 4);
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
            PropGround.MinMax(c.x, c.z, R + 0.5f, out float gmin, out float gmax);
            float top = Mathf.Max(gmax + 0.2f, 0.9f);
            float bottom = gmin - 0.8f;
            PropInfo.HelipadTopY = top;

            var root = PropUtil.WorldRoot("Helipad", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);

            Vector3 ct = new Vector3(c.x, top, c.z);
            batch.B(PropMats.Helipad).Disk(ct, R, 56, Quaternion.identity, true, 1f, true);
            batch.B(PropMats.Concrete).Cylinder(new Vector3(c.x, bottom, c.z), Quaternion.identity, R + 0.12f, R, top - bottom, 56, false, false, 2f * Mathf.PI * R, 1f);
            var cb = new PropMeshBuilder();
            cb.Cylinder(new Vector3(0f, bottom - top, 0f), Quaternion.identity, R, R, top - bottom, 24, true, true);
            PropCol.Mesh(cols, cb.ToMesh("HelipadCollider"), ct, Quaternion.identity, Vector3.one, "HelipadCol");

            // edge lights (emissive, switched by a night controller)
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI * 2f / 12f;
                Vector3 p = ct + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R - 0.28f);
                batch.B(PropMats.DarkMetal).Cylinder(p, Quaternion.identity, 0.09f, 0.08f, 0.05f, 8, false, true);
                batch.B(PropMats.PadBulb).Blob(p + Vector3.up * 0.05f, new Vector3(0.07f, 0.06f, 0.07f), Quaternion.identity, 2, null);
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
                batch.B(PropMats.Concrete).Box(sc, new Vector3(2.2f, h, 0.32f), sr);
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
                batch.B(i == 1 ? PropMats.DrumRed : PropMats.DrumYellow).Cylinder(p, Quaternion.identity, 0.29f, 0.29f, 0.88f, 14, false, true, 1.85f, 1f);
                PropCol.Capsule(cols, p + Vector3.up * 0.44f, 0.3f, 0.88f, Quaternion.identity);
            }
            Vector3 pal = dp + dir * 2.2f;
            pal.y = G(pal.x, pal.z);
            for (int k = 0; k < 5; k++)
                batch.B(PropMats.WoodPale).Box(pal + sr * new Vector3(-0.48f + k * 0.24f, 0.13f, 0f), new Vector3(0.16f, 0.025f, 1.2f), sr, 1f, new Vector2(k * 0.25f, 0.012f), PropUV.LongU);
            for (int k = -1; k <= 1; k++)
                batch.B(PropMats.WoodDark).Box(pal + sr * new Vector3(0f, 0.06f, k * 0.5f), new Vector3(1.1f, 0.1f, 0.1f), sr);

            batch.Flush(root, "HelipadMesh", ShadowCastingMode.On, 0f);
            excl.Add(c.x, c.z, R + 4.5f);
            excl.Add(sockPos.x, sockPos.z, 1.5f);
            excl.Add(dp.x, dp.z, 3f);
            yield break;
        }

        static void Windsock(Transform root, Vector3 groundPos, PropBatch batch)
        {
            const float poleH = 5f;
            batch.B(PropMats.Metal).Cylinder(groundPos - Vector3.up * 0.3f, Quaternion.identity, 0.07f, 0.05f, poleH + 0.3f, 8, false, true, 0.4f, 1f);
            var pivot = new GameObject("Windsock");
            pivot.transform.SetParent(root, false);
            pivot.transform.position = groundPos + Vector3.up * poleH;
            var sockT = new GameObject("Sock").transform;
            sockT.SetParent(pivot.transform, false);

            var sb = new PropMeshBuilder();
            Quaternion along = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            sb.Cylinder(new Vector3(0.12f, 0f, 0f), along, 0.32f, 0.12f, 2.3f, 14, false, false, 1f, 1f / 2.3f);
            PropUtil.MeshObject(sockT, "Fabric", sb.ToMesh("WindsockFabric"), PropMats.FabricSock, Vector3.zero, Quaternion.identity);
            var hb = new PropMeshBuilder();
            hb.Torus(new Vector3(0.12f, 0f, 0f), along, 0.32f, 0.018f, 14, 4);
            hb.Rod(Vector3.zero, new Vector3(0.12f, 0f, 0f), 0.02f, 0.02f, 4);
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

            // octagonal foundation
            Quaternion oct = Quaternion.Euler(0f, 22.5f, 0f);
            batch.B(PropMats.Concrete).Cylinder(new Vector3(c.x, footY, c.z), oct, 3.75f, 3.5f, baseTop - footY, 8, false, true, 2f * Mathf.PI * 3.6f, 1f);
            var fcb = new PropMeshBuilder();
            fcb.Cylinder(Vector3.zero, oct, 3.75f, 3.5f, baseTop - footY, 8, true, true);
            PropCol.Mesh(cols, fcb.ToMesh("LighthouseFoundation"), new Vector3(c.x, footY, c.z), Quaternion.identity, Vector3.one);

            // tower
            const float H = 15.5f, r0 = 2.4f, r1 = 1.65f;
            Vector3 tb = new Vector3(c.x, baseTop, c.z);
            batch.B(PropMats.Lighthouse).Cylinder(tb, Quaternion.identity, r0, r1, H, 36, false, false, 3f, 1f / H);
            var tcb = new PropMeshBuilder();
            tcb.Cylinder(Vector3.zero, Quaternion.identity, r0, r1, H + 3.6f, 16, true, true);
            PropCol.Mesh(cols, tcb.ToMesh("LighthouseTower"), tb, Quaternion.identity, Vector3.one);

            // door facing the hut path
            Vector3 toHut = WorldLayout.Hut - c;
            toHut.y = 0f;
            toHut.Normalize();
            Quaternion dr = Quaternion.LookRotation(toHut);
            batch.B(PropMats.WoodDark).Box(tb + toHut * (r0 - 0.02f) + Vector3.up * 1.05f, new Vector3(1.1f, 2.1f, 0.22f), dr);
            batch.B(PropMats.DarkMetal).Box(tb + toHut * (r0 - 0.06f) + Vector3.up * 1.1f, new Vector3(1.32f, 2.3f, 0.2f), dr);
            batch.B(PropMats.Concrete).Box(tb + toHut * (r0 + 0.45f) + Vector3.up * -0.1f, new Vector3(1.6f, 0.2f, 0.9f), dr);
            // small windows spiralling up
            for (int i = 0; i < 4; i++)
            {
                float y = 4.2f + i * 3f;
                float a = (35f + i * 105f) * Mathf.Deg2Rad;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float rr = Mathf.Lerp(r0, r1, y / H);
                Quaternion wr = Quaternion.LookRotation(d);
                batch.B(PropMats.DarkMetal).Box(tb + d * (rr - 0.02f) + Vector3.up * y, new Vector3(0.6f, 0.85f, 0.14f), wr);
                batch.B(PropMats.Screen).Box(tb + d * (rr + 0.04f) + Vector3.up * y, new Vector3(0.46f, 0.7f, 0.06f), wr);
            }
            if (PropRun.Due()) yield return null;

            // gallery
            float gy = baseTop + H;
            var dm = batch.B(PropMats.DarkMetal);
            dm.Cylinder(new Vector3(c.x, gy - 0.6f, c.z), Quaternion.identity, r1 - 0.05f, 2.8f, 0.6f, 32, false, false, 6f, 1f);
            dm.Cylinder(new Vector3(c.x, gy, c.z), Quaternion.identity, 2.8f, 2.8f, 0.14f, 32, false, true, 6f, 1f);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * 2.68f, gy + 0.14f, c.z + Mathf.Sin(a) * 2.68f);
                dm.Rod(p, p + Vector3.up * 1.0f, 0.025f, 0.025f, 4);
            }
            dm.Torus(new Vector3(c.x, gy + 1.14f, c.z), Quaternion.identity, 2.68f, 0.035f, 40, 5);
            dm.Torus(new Vector3(c.x, gy + 0.64f, c.z), Quaternion.identity, 2.68f, 0.02f, 40, 4);

            // lamp room
            float ly = gy + 0.14f;
            batch.B(PropMats.RedMetal).Cylinder(new Vector3(c.x, ly, c.z), Quaternion.identity, 1.4f, 1.4f, 0.55f, 20, false, false, 8.8f, 1f);
            batch.B(PropMats.Glass).Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 1.32f, 1.32f, 1.8f, 20);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Vector3 p = new Vector3(c.x + Mathf.Cos(a) * 1.34f, ly + 0.55f, c.z + Mathf.Sin(a) * 1.34f);
                dm.Rod(p, p + Vector3.up * 1.8f, 0.04f, 0.04f, 4);
            }
            float roofY = ly + 2.35f;
            batch.B(PropMats.RedMetal).Lathe(new Vector3(c.x, roofY, c.z), Quaternion.identity,
                new[] { new Vector2(1.6f, 0f), new Vector2(1.55f, 0.12f), new Vector2(1.05f, 0.7f), new Vector2(0.38f, 1.15f), new Vector2(0.12f, 1.3f), new Vector2(0f, 1.32f) }, 24, 4f, 1f);
            batch.B(PropMats.RedMetal).Cylinder(new Vector3(c.x, roofY - 0.05f, c.z), Quaternion.identity, 1.6f, 1.6f, 0.06f, 24, true, false);
            dm.Blob(new Vector3(c.x, roofY + 1.5f, c.z), Vector3.one * 0.2f, Quaternion.identity, 3, null);
            dm.Rod(new Vector3(c.x, roofY + 1.6f, c.z), new Vector3(c.x, roofY + 2.8f, c.z), 0.02f, 0.01f, 4);

            // lamp: lens (static, emissive) + rotating pivot with beams and spot lights
            Vector3 lampC = new Vector3(c.x, ly + 1.35f, c.z);
            batch.B(PropMats.Lens).Cylinder(lampC - Vector3.up * 0.4f, Quaternion.identity, 0.42f, 0.42f, 0.8f, 14, true, true);
            dm.Cylinder(new Vector3(c.x, ly + 0.55f, c.z), Quaternion.identity, 0.25f, 0.2f, 0.42f, 10, false, true);

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
                bb.Cylinder(Vector3.zero, Quaternion.FromToRotation(Vector3.up, fwd), 0.3f, 8f, 110f, 16, false, false, 1f, 1f / 110f);
                var mr = PropUtil.MeshObject(pivot.transform, "Beam" + i, bb.ToMesh("LighthouseBeam"), beamMat, Vector3.zero, tiltDown, false);
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
