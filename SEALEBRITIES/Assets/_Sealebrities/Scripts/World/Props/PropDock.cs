using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The dock in the How to Fish look (art bible §1.11): weathered 0.25 m planks (#9A7658 / #8A6A50 / #A88466) with
    /// 3 cm gaps over dark stringers, 8-sided posts r 0.12 with a wet band, lantern posts with bunting, benches, rope
    /// coils, crates, a flag, a boathouse on the beach, a rod rack, a ship's bell and the ladder at the dock end
    /// (wooden rails + rungs every 0.30 m; its climb points PropInfo.DockLadderTop/Bottom drive the Player module's
    /// animated ladder climb – the one way out of the water there).
    public static class PropDock
    {
        static float G(float x, float z) => PropGround.H(x, z);

        public static IEnumerator Build(Transform island, PropExclusion excl)
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

            // weathered planks across the dock with 3 cm gaps (the water shows through)
            Material[] boards = { PropMats.Wood, PropMats.Wood2, PropMats.Wood3 };
            const float bw = 0.25f, gap = 0.03f;
            for (float x = x0 + bw * 0.5f; x < x1 - 0.02f; x += bw + gap)
            {
                var mat = boards[rng.Next(boards.Length)];
                batch.W(mat).Box(new Vector3(x, deck - 0.025f + PropUtil.Range(rng, -0.008f, 0.008f), z0 + PropUtil.Range(rng, -0.03f, 0.03f)),
                    new Vector3(bw, 0.05f, 2f * halfW + PropUtil.Range(rng, -0.06f, 0.08f)), Quaternion.Euler(0f, PropUtil.Range(rng, -1.2f, 1.2f), PropUtil.Range(rng, -0.6f, 0.6f)));
            }
            var dark = batch.W(PropMats.WoodDark);
            for (int i = -1; i <= 1; i++)
                dark.Box(new Vector3((x0 + x1) * 0.5f, deck - 0.12f, z0 + i * 0.95f), new Vector3(x1 - x0, 0.14f, 0.16f));
            PropCol.Box(cols, new Vector3((x0 + x1) * 0.5f, deck - 0.15f, z0), new Vector3(x1 - x0, 0.3f, 2f * halfW), Quaternion.identity, "DockDeck");

            // 8-sided posts r 0.12 with a dark wet band up to 0.4 m above the waterline, cross beams and braces
            int pi = 0;
            var wet = batch.W(PropMats.WoodWet);
            for (float x = x1 - 0.2f; x >= x0 + 0.2f; x -= 3f, pi++)
            {
                bool any = false;
                for (int s = -1; s <= 1; s += 2)
                {
                    float z = z0 + s * (halfW + 0.04f);
                    float g = G(x, z);
                    if (g > deck - 0.35f) continue;
                    any = true;
                    float bottom = g - 0.6f, top = deck + 0.3f;
                    float wetTop = Mathf.Min(Waves.SeaLevel + 0.4f, top);
                    if (bottom < wetTop) wet.Cylinder(new Vector3(x, bottom, z), Quaternion.identity, 0.125f, 0.12f, wetTop - bottom, 8);
                    float dryBottom = Mathf.Max(bottom, wetTop);
                    dark.Cylinder(new Vector3(x, dryBottom, z), Quaternion.identity, 0.12f, 0.115f, top - dryBottom, 8, false, true);
                    PropCol.UprightCapsule(postCols, new Vector3(x, bottom, z), 0.13f, top - bottom);
                    if (pi % 2 == 1 && g < -1.2f)
                    {
                        float zb = z0 - s * (halfW - 0.1f);
                        dark.Beam(new Vector3(x, Mathf.Max(g + 0.3f, -2.5f), z), new Vector3(x - 1.4f, deck - 0.3f, zb), 0.09f, 0.12f);
                    }
                }
                if (any) dark.Box(new Vector3(x, deck - 0.25f, z0), new Vector3(0.16f, 0.14f, 2f * halfW + 0.3f));
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
                for (float t = 0.14f; t < L; t += 0.28f, k++)
                {
                    Vector3 p = a + dir * t + r * new Vector3(0f, -0.03f, 0f);
                    batch.W(boards[k % boards.Length]).Box(p, new Vector3(0.25f, 0.06f, 1.8f), r);
                }
                dark.Beam(a + new Vector3(0f, -0.12f, -0.8f), b + new Vector3(0f, -0.12f, -0.8f), 0.1f, 0.16f);
                dark.Beam(a + new Vector3(0f, -0.12f, 0.8f), b + new Vector3(0f, -0.12f, 0.8f), 0.1f, 0.16f);
                PropCol.Box(cols, (a + b) * 0.5f - r * new Vector3(0f, 0.08f, 0f), new Vector3(L, 0.16f, 1.8f), r, "DockRamp");
                rampStart = x0 - len;
            }
            excl.AddLine(new Vector3(rampStart - 2f, 0f, z0), new Vector3(x1 + 2f, 0f, z0), 3.5f);
            if (PropRun.Due()) yield return null;

            Ladder(root, batch, x1, z0, deck);
            Lanterns(root, batch, cols, x0, x1, z0, halfW, deck);
            if (PropRun.Due()) yield return null;

            // mooring bollards along the north edge (where the boat lies)
            float[] bxs = { x1 - 0.6f, x1 - 9.2f, x0 + 9f };
            foreach (float bx in bxs)
            {
                Vector3 bp = new Vector3(bx, deck, z0 + halfW - 0.2f);
                batch.W(PropMats.DarkMetal).Cylinder(bp, Quaternion.identity, 0.11f, 0.09f, 0.3f, 8);
                batch.W(PropMats.DarkMetal).Cylinder(bp + Vector3.up * 0.3f, Quaternion.identity, 0.16f, 0.16f, 0.07f, 8, true, true);
                batch.W(PropMats.Rope).Torus(bp + Vector3.up * 0.18f, Quaternion.identity, 0.13f, 0.03f, 8, 3);
            }

            // benches facing the sea (south edge)
            Bench(batch, cols, new Vector3(x0 + 8f, deck, z0 - halfW + 0.35f), 180f);
            Bench(batch, cols, new Vector3(x1 - 10f, deck, z0 - halfW + 0.35f), 180f);

            // rope coils
            Vector3[] coils = { new Vector3(x1 - 7.5f, deck, z0 + 0.75f), new Vector3(x0 + 12.5f, deck, z0 - 0.8f), new Vector3(x1 - 1.6f, deck, z0 + 0.8f) };
            foreach (var cp in coils)
                for (int k = 0; k < 3; k++)
                    batch.W(PropMats.Rope).Torus(cp + Vector3.up * (0.045f + k * 0.075f), Quaternion.Euler(0f, k * 25f, 0f), 0.3f - k * 0.05f, 0.045f, 8, 3);

            // crates and a barrel near the dock start (north side, away from the boarding spot at the end)
            Vector3[] crates = { new Vector3(x0 + 4.4f, deck, z0 + 0.65f), new Vector3(x0 + 4.95f, deck, z0 + 0.62f), new Vector3(x0 + 4.65f, deck + 0.55f, z0 + 0.66f) };
            for (int i = 0; i < crates.Length; i++)
            {
                float sz = i == 2 ? 0.45f : 0.55f;
                Quaternion r = Quaternion.Euler(0f, PropUtil.Range(rng, -12f, 12f), 0f);
                Vector3 c = crates[i] + new Vector3(0f, sz * 0.5f, 0f);
                batch.W(PropMats.Wood3).Box(c, Vector3.one * sz * 0.94f, r);
                var dk = batch.W(PropMats.WoodDark);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int szz = -1; szz <= 1; szz += 2)
                        dk.Box(c + r * new Vector3(sx * (sz * 0.5f - 0.035f), 0f, szz * (sz * 0.5f - 0.035f)), new Vector3(0.08f, sz, 0.08f), r);
            }
            PropCol.Box(cols, new Vector3(x0 + 4.65f, deck + 0.5f, z0 + 0.65f), new Vector3(1.2f, 1f, 0.6f), Quaternion.identity);
            PropStructures.Drum(batch, new Vector3(x0 + 6f, deck, z0 + 0.8f), PropMats.DrumBlue);
            PropCol.Capsule(cols, new Vector3(x0 + 6f, deck + 0.44f, z0 + 0.8f), 0.3f, 0.88f, Quaternion.identity);

            // bucket and tackle box a few steps from the fishing spot (the menu crew sits at x1 − 1 … x1 − 2.6 on the
            // south edge, so keep that stretch free)
            Vector3 bucket = new Vector3(Mathf.Max(x0 + 1f, x1 - 4.2f), deck, z0 - halfW + 0.3f);
            batch.W(PropMats.DrumBlue).Cylinder(bucket, Quaternion.identity, 0.15f, 0.19f, 0.32f, 6, true, false);
            batch.W(PropMats.DarkMetal).Cylinder(bucket + Vector3.up * 0.005f, Quaternion.identity, 0.145f, 0.185f, 0.31f, 6, false, false, 1f, 1f, 0f, true);
            Quaternion tbr = Quaternion.Euler(0f, 15f, 0f);
            Vector3 tbp = bucket + new Vector3(-0.55f, 0.11f, 0.1f);
            batch.W(PropMats.DrumYellow).Box(tbp, new Vector3(0.42f, 0.22f, 0.24f), tbr);
            batch.W(PropMats.Orange).Box(tbp + Vector3.up * 0.125f, new Vector3(0.44f, 0.05f, 0.26f), tbr);

            RodRack(batch, cols, new Vector3(x1 - 6.6f, deck, z0 - halfW + 0.25f));

            // fenders hanging at the end
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 fp = new Vector3(x1 - 1.5f, deck - 0.75f, z0 + s * (halfW + 0.2f));
                batch.W(PropMats.Orange).Cylinder(fp, Quaternion.identity, 0.14f, 0.14f, 0.5f, 6, true, true);
                batch.W(PropMats.White).Cylinder(fp + Vector3.up * 0.2f, Quaternion.identity, 0.145f, 0.145f, 0.1f, 6);
                batch.W(PropMats.Rope).Rod(fp + Vector3.up * 0.5f, new Vector3(fp.x, deck + 0.02f, z0 + s * halfW), 0.018f, 0.018f, 3);
            }

            Flag(root, batch, cols, new Vector3(x0 + 1.0f, deck, z0 - halfW + 0.15f));
            if (PropRun.Due()) yield return null;

            Boathouse(root, batch, cols, excl, x0, z0);

            batch.Flush(root, "DockMesh", ShadowCastingMode.On, 0f);
        }

        // ------------------------------------------------------------------ ladder (working)
        static void Ladder(Transform root, PropBatch batch, float x1, float z0, float deck)
        {
            float lx = x1 + 0.12f;
            var wood = batch.W(PropMats.WoodDark);
            for (int s = -1; s <= 1; s += 2)
            {
                float zr = z0 + s * 0.36f;
                wood.Beam(new Vector3(lx, -2.6f, zr), new Vector3(lx, deck + 0.85f, zr), 0.07f, 0.07f);
                wood.Beam(new Vector3(lx + 0.02f, deck + 0.85f, zr), new Vector3(lx - 0.42f, deck + 0.85f, zr), 0.06f, 0.06f);
                wood.Beam(new Vector3(lx - 0.42f, deck + 0.88f, zr), new Vector3(lx - 0.42f, deck, zr), 0.06f, 0.06f);
            }
            for (float y = -2.3f; y < deck - 0.05f; y += 0.3f)
                wood.Rod(new Vector3(lx, y, z0 - 0.36f), new Vector3(lx, y, z0 + 0.36f), 0.03f, 0.03f, 5);
            // algae band on the submerged rails
            var wet = batch.W(PropMats.WoodWet);
            for (int s = -1; s <= 1; s += 2)
                wet.Box(new Vector3(lx, -1.3f, z0 + s * 0.36f), new Vector3(0.08f, 2.6f, 0.08f));

            // The climb itself is the Player module's animated ladder climb ("Leertaste/E – Leiter hoch" while swimming
            // within reach of DockLadderBottom), so this is the one working way out of the water at the dock end and
            // no second interactable is added here. Bottom = where the body hangs while climbing (just seaward of
            // the rails), top = standing point on the planks.
            PropInfo.DockLadderTop = new Vector3(x1 - 0.45f, deck, z0);
            PropInfo.DockLadderBottom = new Vector3(lx + 0.45f, Waves.SeaLevel - 1.5f, z0);
            var go = new GameObject("DockLadder");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(lx, deck * 0.5f - 1.2f, z0);
        }

        // ------------------------------------------------------------------ lanterns + bunting + bell
        static void Lanterns(Transform root, PropBatch batch, Transform cols, float x0, float x1, float z0, float halfW, float deck)
        {
            float[] xs = { x0 + 2.5f, x0 + 14f, x1 - 13f, x1 - 0.3f };
            var tops = new List<Vector3>();
            for (int i = 0; i < xs.Length; i++)
            {
                float side = i % 2 == 0 ? 1f : -1f;
                Vector3 lp = new Vector3(xs[i], deck, z0 + side * (halfW - 0.1f));
                var d = batch.W(PropMats.WoodDark);
                // posts reach 2.6 m so the bunting hangs above head height (it crosses the walkway diagonally and
                // used to dangle at eye level right in front of the fishing spot)
                d.Box(lp + new Vector3(0f, 1.3f, 0f), new Vector3(0.12f, 2.6f, 0.12f));
                d.Box(lp + new Vector3(0f, 1.82f, -side * 0.17f), new Vector3(0.08f, 0.08f, 0.42f));
                Vector3 lan = lp + new Vector3(0f, 1.55f, -side * 0.34f);
                batch.W(PropMats.DarkMetal).Cylinder(lan + new Vector3(0f, 0.2f, 0f), Quaternion.identity, 0.12f, 0f, 0.1f, 6, true, false);
                batch.W(PropMats.DarkMetal).Cylinder(lan - new Vector3(0f, 0.03f, 0f), Quaternion.identity, 0.1f, 0.1f, 0.04f, 6, true, true);
                batch.W(PropMats.Glass).Cylinder(lan, Quaternion.identity, 0.085f, 0.085f, 0.2f, 6);
                batch.W(PropMats.DockBulb).Ico(lan + new Vector3(0f, 0.1f, 0f), new Vector3(0.05f, 0.07f, 0.05f), Quaternion.identity, 0);
                batch.W(PropMats.Rope).Rod(lan + new Vector3(0f, 0.3f, 0f), lp + new Vector3(0f, 1.78f, -side * 0.34f), 0.012f, 0.012f, 3);
                tops.Add(lp + Vector3.up * 2.58f);
                // real lights only at both ends (the shared bulb material glows on all four)
                if (i == 0 || i == xs.Length - 1)
                {
                    var light = PropUtil.AddPointLight(root, lan + new Vector3(0f, 0.1f, 0f), new Color(1f, 0.74f, 0.42f), 12f, 2.2f, "DockLantern");
                    var nl = PropNightLight.Attach(light.gameObject, light, i == 0 ? PropMats.DockBulb : null, new Color(3.5f, 2.4f, 1.2f), 0.15f, 0.5f, true);
                    nl.flickerAmount = 0.12f;
                }
            }
            // bunting between the lantern posts
            Material[] flagMats = { PropMats.Flat(PropPal.Coral), PropMats.Flat(PropPal.Sun), PropMats.Flat(PropPal.Turquoise), PropMats.Trim };
            int fi = 0;
            for (int i = 0; i < tops.Count - 1; i++)
            {
                Vector3 a = tops[i], b = tops[i + 1];
                float len = (b - a).magnitude;
                int n = Mathf.Max(2, Mathf.RoundToInt(len / 0.55f));
                Vector3 prev = a;
                var rope = batch.W(PropMats.Rope);
                for (int k = 1; k <= n; k++)
                {
                    float t = k / (float)n;
                    Vector3 p = Vector3.Lerp(a, b, t) - Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.22f);
                    rope.Rod(prev, p, 0.01f, 0.01f, 3);
                    if (k < n)
                    {
                        Vector3 along = (p - prev).normalized;
                        Vector3 nrm = Vector3.Cross(along, Vector3.up);
                        if (nrm.sqrMagnitude < 1e-6f) nrm = Vector3.forward;
                        batch.W(flagMats[fi++ % flagMats.Length]).Tri2(p - along * 0.13f, p + along * 0.13f, p - Vector3.up * 0.26f, nrm.normalized);
                    }
                    prev = p;
                }
            }
            // ship's bell on the last post
            Vector3 bell = tops[tops.Count - 1] + new Vector3(-0.32f, -0.42f, 0f);
            batch.W(PropMats.WoodDark).Box(tops[tops.Count - 1] + new Vector3(-0.17f, -0.1f, 0f), new Vector3(0.4f, 0.07f, 0.07f));
            batch.W(PropMats.Flat(LowPoly.Hex("#B8913A"))).Cylinder(bell, Quaternion.identity, 0.16f, 0.08f, 0.22f, 8, true, true);
            batch.W(PropMats.Rope).Rod(bell + Vector3.up * 0.22f, tops[tops.Count - 1] + new Vector3(-0.32f, -0.13f, 0f), 0.012f, 0.012f, 3);
            batch.W(PropMats.Rope).Rod(bell - Vector3.up * 0.02f, bell - Vector3.up * 0.32f, 0.012f, 0.012f, 3);
        }

        static void Bench(PropBatch batch, Transform cols, Vector3 p, float yaw)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            var w = batch.W(PropMats.Wood3);
            for (int i = 0; i < 3; i++) w.Box(p + r * new Vector3(0f, 0.45f, -0.12f + i * 0.13f), new Vector3(1.5f, 0.05f, 0.11f), r);
            for (int i = 0; i < 2; i++) w.Box(p + r * new Vector3(0f, 0.7f + i * 0.17f, -0.24f), new Vector3(1.5f, 0.12f, 0.04f), r * Quaternion.Euler(-10f, 0f, 0f));
            var d = batch.W(PropMats.WoodDark);
            for (int s = -1; s <= 1; s += 2)
            {
                d.Box(p + r * new Vector3(s * 0.62f, 0.22f, 0f), new Vector3(0.07f, 0.44f, 0.36f), r);
                d.Box(p + r * new Vector3(s * 0.62f, 0.66f, -0.25f), new Vector3(0.06f, 0.5f, 0.06f), r * Quaternion.Euler(-10f, 0f, 0f));
            }
            PropCol.Box(cols, p + r * new Vector3(0f, 0.3f, -0.05f), new Vector3(1.55f, 0.6f, 0.5f), r, "Bench");
        }

        static void RodRack(PropBatch batch, Transform cols, Vector3 p)
        {
            var d = batch.W(PropMats.WoodDark);
            d.Box(p + new Vector3(0f, 0.05f, 0f), new Vector3(0.9f, 0.1f, 0.25f));
            d.Box(p + new Vector3(-0.42f, 0.6f, 0.02f), new Vector3(0.06f, 1.2f, 0.06f));
            d.Box(p + new Vector3(0.42f, 0.6f, 0.02f), new Vector3(0.06f, 1.2f, 0.06f));
            d.Box(p + new Vector3(0f, 1.05f, 0.02f), new Vector3(0.9f, 0.06f, 0.08f));
            var rod = batch.W(PropMats.Flat(LowPoly.Hex("#2E2A40")));
            var band = batch.W(PropMats.Flat(LowPoly.Hex("#3D7BC4")));
            var reel = batch.W(PropMats.Metal);
            for (int i = 0; i < 3; i++)
            {
                Vector3 b = p + new Vector3(-0.28f + i * 0.28f, 0.1f, 0f);
                Vector3 t = b + new Vector3(0.05f * (i - 1), 2.2f, 0.18f);
                rod.Rod(b, t, 0.016f, 0.005f, 5);
                band.Cylinder(b + (t - b) * 0.12f, Quaternion.FromToRotation(Vector3.up, (t - b).normalized), 0.019f, 0.019f, 0.06f, 5);
                reel.Cylinder(b + (t - b) * 0.18f + new Vector3(0f, 0f, -0.05f), Quaternion.Euler(0f, 0f, 90f), 0.045f, 0.045f, 0.04f, 6, true, true);
            }
            PropCol.Box(cols, p + new Vector3(0f, 0.6f, 0f), new Vector3(0.95f, 1.2f, 0.3f), Quaternion.identity, "RodRack");
        }

        /// Flag pole with a waving turquoise flag (fish pictogram).
        static void Flag(Transform root, PropBatch batch, Transform cols, Vector3 p)
        {
            batch.W(PropMats.Trim).Cylinder(p - Vector3.up * 0.05f, Quaternion.identity, 0.045f, 0.035f, 4.2f, 6, false, true);
            batch.W(PropMats.Gold).Ico(p + Vector3.up * 4.2f, Vector3.one * 0.07f, Quaternion.identity, 0);
            PropCol.Capsule(cols, p + Vector3.up * 1.5f, 0.06f, 3f, Quaternion.identity, "FlagPole");
            var pivot = new GameObject("DockFlag").transform;
            pivot.SetParent(root, false);
            pivot.position = p + Vector3.up * 3.95f;
            var mb = new PropMeshBuilder();
            Vector3 a0 = new Vector3(0.04f, 0f, 0f), a1 = new Vector3(0.04f, -0.7f, 0f);
            Vector3 m0 = new Vector3(0.6f, -0.02f, 0.08f), m1 = new Vector3(0.6f, -0.68f, 0.08f);
            Vector3 e0 = new Vector3(1.1f, -0.05f, 0f), e1 = new Vector3(1.1f, -0.65f, 0f);
            mb.QuadAuto(a0, m0, m1, a1, new Vector2(0f, 1f), new Vector2(0.55f, 1f), new Vector2(0.55f, 0f), new Vector2(0f, 0f), Vector3.forward);
            mb.QuadAuto(m0, e0, e1, m1, new Vector2(0.55f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0.55f, 0f), Vector3.forward);
            mb.QuadAuto(a0, m0, m1, a1, new Vector2(0f, 1f), new Vector2(0.55f, 1f), new Vector2(0.55f, 0f), new Vector2(0f, 0f), Vector3.back);
            mb.QuadAuto(m0, e0, e1, m1, new Vector2(0.55f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Vector2(0.55f, 0f), Vector3.back);
            PropUtil.MeshObject(pivot, "Flag", mb.ToMesh("DockFlag"), PropMats.SignIcon(PropIcon.Fish), Vector3.zero, Quaternion.identity);
            var f = pivot.gameObject.AddComponent<PropFlutter>();
            f.baseYaw = 200f;
        }

        // ------------------------------------------------------------------ boathouse
        static void Boathouse(Transform root, PropBatch batch, Transform cols, PropExclusion excl, float dockX0, float z0)
        {
            Vector3 want = new Vector3(dockX0 - 6f, 0f, z0 - 9f);
            if (!PropBeach.FindLand(want, 0.3f, 3.5f, excl, out var pos, 10f)) return;
            PropGround.MinMaxRect(pos, Quaternion.identity, 2.2f, 2.5f, 0.8f, out float gmin, out float gmax);
            float floorY = Mathf.Max(gmax + 0.25f, 0.6f);
            var spec = PropShack.Default(3.6f, 4.2f, 41);
            spec.windowLeft = false;
            spec.windowRight = true;     // looks out to sea (east)
            spec.doorX = 0f;
            spec.doorW = 1.1f;
            // door faces north toward the dock
            Vector3 door = PropShack.Build(batch, cols, pos, 0f, floorY, spec);
            excl.Add(pos.x, pos.z, 4f);
            excl.Add(door.x, door.z, 1.5f);
            PropInfo.Boathouse = new Vector3(pos.x, floorY, pos.z);

            // inside: a small rowing boat on trestles, oars, a life ring
            var boatTrs = Matrix4x4.TRS(new Vector3(pos.x - 0.45f, floorY + 0.45f, pos.z - 0.2f), Quaternion.Euler(0f, 0f, 0f), Vector3.one * 0.8f);
            PropBeach.RowingBoat(batch, boatTrs, PropMats.Flat(PropPal.Coral), false);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 tp = new Vector3(pos.x - 0.45f, floorY, pos.z - 0.2f + s * 0.9f);
                batch.W(PropMats.WoodDark).Box(tp + Vector3.up * 0.22f, new Vector3(0.9f, 0.06f, 0.08f));
                batch.W(PropMats.WoodDark).Box(tp + new Vector3(-0.35f, 0.1f, 0f), new Vector3(0.06f, 0.24f, 0.08f));
                batch.W(PropMats.WoodDark).Box(tp + new Vector3(0.35f, 0.1f, 0f), new Vector3(0.06f, 0.24f, 0.08f));
            }
            PropCol.Box(cols, new Vector3(pos.x - 0.45f, floorY + 0.4f, pos.z - 0.2f), new Vector3(1.1f, 0.8f, 2.9f), Quaternion.identity, "Rowboat");
            for (int i = 0; i < 2; i++)
            {
                Vector3 ob = new Vector3(pos.x + 1.6f, floorY + 0.1f, pos.z - 1.4f + i * 0.35f);
                batch.W(PropMats.WoodLight).Rod(ob, ob + new Vector3(0.02f, 1.9f, 0.1f), 0.03f, 0.03f, 5, true);
                batch.W(PropMats.WoodLight).Box(ob + new Vector3(0.01f, 0.3f, 0.05f), new Vector3(0.04f, 0.5f, 0.16f));
            }
            Vector3 lr = new Vector3(pos.x, floorY + 1.6f, pos.z - 2.1f + 0.14f);
            const int segs = 8;
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 p0 = lr + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.28f;
                Vector3 p1 = lr + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.28f;
                batch.W(i % 2 == 0 ? PropMats.Orange : PropMats.White).Rod(p0, p1, 0.07f, 0.07f, 5, true);
            }
        }
    }

    /// Flag / pennant waving in a gentle, slowly changing wind.
    public class PropFlutter : MonoBehaviour
    {
        public float baseYaw;
        float seed, nextCheck;
        bool near = true;

        void Awake() { seed = Random.value * 50f; }

        void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.5f;
                near = !PropCam.Valid || (PropCam.Position - transform.position).sqrMagnitude < 250f * 250f;
            }
            if (!near) return;
            float t = Time.time;
            float yaw = baseYaw + (Mathf.PerlinNoise(seed, t * 0.05f) - 0.5f) * 60f + Mathf.Sin(t * 2.7f + seed) * 9f;
            transform.rotation = Quaternion.Euler(0f, yaw, Mathf.Sin(t * 3.9f + seed) * 3f);
        }
    }
}
