using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// More points of interest: tide pools on the rocky south-east shore (teal pools with starfish, urchins,
    /// anemones and scuttling crabs), the second beach in the south (hammock between palms, sandcastle, flotsam) and
    /// the radio tower ruins (broken rusty lattice tower, ruined concrete radio shack, generator, fallen dish).
    public static class PropPOI2
    {
        static float G(float x, float z) => PropGround.H(x, z);
        static float G(Vector3 p) => PropGround.H(p.x, p.z);

        // ================================================================== tide pools
        /// True when the world shaped the tide pool platform (pools carved below sea level, the sea fills them).
        static bool WorldTidePools()
        {
            var spots = WorldFeatures.TidePoolSpots;
            if (spots == null || spots.Length == 0) return false;
            int ok = 0;
            for (int i = 0; i < spots.Length; i++)
                if (G(spots[i].position) < -0.15f) ok++;
            return ok >= spots.Length / 2 && G(WorldLayout.TidePools) > -0.2f;
        }

        public static IEnumerator TidePools(Transform island, PropExclusion excl)
        {
            if (WorldTidePools())
            {
                yield return TidePoolsOnTerrain(island, excl);
                yield break;
            }
            yield return TidePoolsFallback(island, excl);
        }

        /// Decorates the world's tide pools: the sea itself fills them (no water discs), starfish / urchins /
        /// anemones on the pool floors, a ring of small rocks, bigger rocks on the platform and scuttling crabs.
        static IEnumerator TidePoolsOnTerrain(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("TidePools", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(1213);
            var spots = WorldFeatures.TidePoolSpots;
            Material[] content = { PropMats.Starfish, PropMats.FlowerPurple, PropMats.Flat(PropPal.Coral) };
            int crabs = 0;
            for (int i = 0; i < spots.Length; i++)
            {
                Vector3 s = spots[i].position;
                float r = spots[i].radius;
                float fl = G(s);
                // pool floor life
                int stars = 1 + rng.Next(2);
                for (int k = 0; k < stars; k++)
                {
                    Vector2 o = PropUtil.InDisc(rng, r * 0.55f);
                    Vector3 p = new Vector3(s.x + o.x, 0f, s.z + o.y);
                    p.y = G(p) - 0.01f;
                    Starfish(batch, p, PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0.13f, 0.21f), content[rng.Next(content.Length)]);
                }
                Vector2 uo = PropUtil.InDisc(rng, r * 0.5f);
                Vector3 up = new Vector3(s.x + uo.x, 0f, s.z + uo.y);
                up.y = G(up);
                Urchins(batch, up, rng);
                Vector2 ao = PropUtil.InDisc(rng, r * 0.5f);
                Vector3 ap = new Vector3(s.x + ao.x, 0f, s.z + ao.y);
                ap.y = G(ap);
                Anemone(batch, ap, rng.NextDouble() < 0.5 ? PropMats.Flat(PropPal.CoralTeal) : PropMats.FlowerPink, rng);
                // small rim rocks
                int n = 4 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n + PropUtil.Range(rng, -0.3f, 0.3f);
                    Vector3 rp = new Vector3(s.x + Mathf.Cos(a) * (r + 0.5f), 0f, s.z + Mathf.Sin(a) * (r + 0.5f));
                    float rs = PropUtil.Range(rng, 0.22f, 0.45f);
                    rp.y = G(rp) - rs * 0.25f;
                    PropRocks.BuildSplit(batch, rp, rp, new Vector3(rs * 1.4f, rs * 0.6f, rs), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), rng.Next(), 0, 0.18f, PropMats.BeachRock, null, PropMats.Crevice);
                }
                excl.Add(s.x, s.z, r + 1.2f);
                if (crabs < 6 && i % 2 == 0)
                {
                    Vector3 cp = s + WorldFeatures.TideAlong * (r + 1.4f);
                    cp.y = G(cp) + 0.01f;
                    if (cp.y > 0.1f) { PropCrab.Spawn(root, cp, PropUtil.Range(rng, 0f, 360f), crabs); crabs++; }
                }
            }
            // a few bigger rocks on the platform (seaward side, away from the trail)
            Vector3 c = WorldLayout.TidePools;
            for (int i = 0; i < 7; i++)
            {
                float u = PropUtil.Range(rng, -18f, 18f), v = PropUtil.Range(rng, 2f, 8f);
                Vector3 p = c + WorldFeatures.TideAlong * u + WorldFeatures.TideSeaward * v;
                if (excl.Blocked(p.x, p.z)) continue;
                float g = G(p);
                if (g < -0.8f) continue;
                float rs = PropUtil.Range(rng, 0.6f, 1.4f);
                Vector3 sc = new Vector3(rs * 1.5f, rs * 0.5f, rs * 1.1f);
                Vector3 rp = new Vector3(p.x, g - sc.y * 0.25f, p.z);
                Quaternion rr = Quaternion.Euler(PropUtil.Range(rng, -5f, 5f), PropUtil.Range(rng, 0f, 360f), 0f);
                int seed = rng.Next();
                PropRocks.BuildSplit(batch, rp, rp, sc, rr, seed, 1, 0.12f, PropMats.BeachRock, null, PropMats.Crevice);
                if (rs > 0.9f) PropCol.Mesh(cols, PropRocks.ColliderMesh(sc, seed, 1, 0.12f), rp, rr, Vector3.one, "ShelfRock");
                excl.Add(p.x, p.z, rs * 1.3f);
            }
            PropInfo.TidePoolsCenter = new Vector3(c.x, G(c), c.z);
            excl.Add(c.x, c.z, 8f);
            batch.Flush(root, "TidePoolsMesh", ShadowCastingMode.On, 300f);
            yield break;
        }

        /// Fallback when the terrain has no tide pool platform: pools with their own water discs at the shore.
        static IEnumerator TidePoolsFallback(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("TidePools", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(1212);
            Vector3 c = WorldLayout.TidePools;

            // intertidal spots around the POI (fall back to the shoreline toward the island center)
            var spots = new List<Vector3>();
            for (int a = 0; a < 900 && spots.Count < 7; a++)
            {
                Vector2 o = PropUtil.InDisc(rng, a < 500 ? 22f : 40f);
                float x = c.x + o.x, z = c.z + o.y;
                float g = G(x, z);
                if (g < -0.45f || g > 0.9f) continue;
                bool far = true;
                foreach (var s in spots) if ((s.x - x) * (s.x - x) + (s.z - z) * (s.z - z) < 3.4f * 3.4f) { far = false; break; }
                if (!far || excl.Blocked(x, z)) continue;
                spots.Add(new Vector3(x, g, z));
            }
            if (spots.Count == 0)
            {
                Vector3 dir = (WorldLayout.IslandCenter - c);
                dir.y = 0f;
                dir.Normalize();
                for (float t = -30f; t < 60f; t += 1f)
                {
                    Vector3 p = c + dir * t;
                    float g = G(p);
                    if (g > -0.3f && g < 0.8f) { spots.Add(new Vector3(p.x, g, p.z)); break; }
                }
            }
            if (spots.Count == 0) { batch.Flush(root, "TidePoolsMesh"); yield break; }
            PropInfo.TidePoolsCenter = spots[0];

            Material[] content = { PropMats.Starfish, PropMats.FlowerPurple, PropMats.Flat(PropPal.Coral) };
            for (int i = 0; i < spots.Count; i++)
            {
                Vector3 s = spots[i];
                float r = PropUtil.Range(rng, 0.8f, 1.5f);
                float wl = Mathf.Max(s.y, -0.1f) + 0.24f;
                // rock ring
                int n = 7 + rng.Next(4);
                for (int k = 0; k < n; k++)
                {
                    float a = k * Mathf.PI * 2f / n + PropUtil.Range(rng, -0.2f, 0.2f);
                    Vector3 rp = new Vector3(s.x + Mathf.Cos(a) * (r + 0.25f), 0f, s.z + Mathf.Sin(a) * (r + 0.25f));
                    float rs = PropUtil.Range(rng, 0.3f, 0.6f);
                    rp.y = wl - 0.08f;
                    Vector3 sc = new Vector3(rs * 1.4f, rs * 0.55f, rs);
                    Quaternion rr = Quaternion.Euler(0f, a * Mathf.Rad2Deg + PropUtil.Range(rng, -20f, 20f), 0f);
                    int seed = rng.Next();
                    PropRocks.BuildSplit(batch, rp, rp, sc, rr, seed, 0, 0.18f, PropMats.BeachRock, null, PropMats.Crevice);
                    if (rs > 0.45f) PropCol.Mesh(cols, PropRocks.ColliderMesh(sc, seed, 0, 0.18f), rp, rr, Vector3.one, "PoolRock");
                }
                // water + sandy floor
                var water = batch.W(PropMats.PoolWater, s);
                var floor = batch.W(PropMats.Flat(PropPal.Seabed), s);
                const int seg = 10;
                for (int k = 0; k < seg; k++)
                {
                    float a0 = k * Mathf.PI * 2f / seg, a1 = (k + 1) * Mathf.PI * 2f / seg;
                    Vector3 e0 = new Vector3(s.x + Mathf.Cos(a0) * r, wl, s.z + Mathf.Sin(a0) * r);
                    Vector3 e1 = new Vector3(s.x + Mathf.Cos(a1) * r, wl, s.z + Mathf.Sin(a1) * r);
                    water.Face3(new Vector3(s.x, wl, s.z), e0, e1, Vector3.up);
                    floor.Face3(new Vector3(s.x, wl - 0.3f, s.z), e0 - Vector3.up * 0.18f, e1 - Vector3.up * 0.18f, Vector3.up);
                }
                // starfish, urchins, anemones
                int stars = 1 + rng.Next(2);
                for (int k = 0; k < stars; k++)
                {
                    Vector2 o = PropUtil.InDisc(rng, r * 0.6f);
                    Starfish(batch, new Vector3(s.x + o.x, wl - 0.24f, s.z + o.y), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0.12f, 0.2f), content[rng.Next(content.Length)]);
                }
                Vector2 uo = PropUtil.InDisc(rng, r * 0.6f);
                Urchins(batch, new Vector3(s.x + uo.x, wl - 0.24f, s.z + uo.y), rng);
                Vector2 ao = PropUtil.InDisc(rng, r * 0.5f);
                Anemone(batch, new Vector3(s.x + ao.x, wl - 0.24f, s.z + ao.y), rng.NextDouble() < 0.5 ? PropMats.Flat(PropPal.CoralTeal) : PropMats.FlowerPink, rng);
                excl.Add(s.x, s.z, r + 1.2f);
            }

            // rock shelf around the pools, a few crabs
            int crabs = 0;
            for (int i = 0; i < 14; i++)
            {
                var s = spots[rng.Next(spots.Count)];
                Vector2 o = PropUtil.InDisc(rng, 7f);
                float x = s.x + o.x, z = s.z + o.y;
                if (excl.Blocked(x, z)) continue;
                float g = G(x, z);
                if (g < -1.2f || g > 2.5f) continue;
                float rs = PropUtil.Range(rng, 0.6f, 1.6f);
                Vector3 sc = new Vector3(rs * 1.5f, rs * 0.45f, rs * 1.1f);
                Vector3 rp = new Vector3(x, g - sc.y * 0.2f, z);
                Quaternion rr = Quaternion.Euler(PropUtil.Range(rng, -5f, 5f), PropUtil.Range(rng, 0f, 360f), 0f);
                int seed = rng.Next();
                PropRocks.BuildSplit(batch, rp, rp, sc, rr, seed, 1, 0.12f, PropMats.BeachRock, null, PropMats.Crevice);
                PropCol.Mesh(cols, PropRocks.ColliderMesh(sc, seed, 1, 0.12f), rp, rr, Vector3.one, "ShelfRock");
                excl.Add(x, z, rs * 1.4f);
                if (crabs < 5 && rng.NextDouble() < 0.5)
                {
                    PropCrab.Spawn(root, rp + Vector3.up * (sc.y * 0.8f), PropUtil.Range(rng, 0f, 360f), crabs);
                    crabs++;
                }
            }
            for (; crabs < 3; crabs++)
            {
                var s = spots[crabs % spots.Count];
                Vector3 p = s + new Vector3(1.6f, 0f, 0.4f);
                p.y = Mathf.Max(G(p), 0.05f);
                PropCrab.Spawn(root, p, crabs * 70f, crabs);
            }
            batch.Flush(root, "TidePoolsMesh", ShadowCastingMode.On, 300f);
        }

        public static void Starfish(PropBatch batch, Vector3 p, float yaw, float size, Material mat)
        {
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 c = p + Vector3.up * 0.03f;
            var b = batch.W(mat, p);
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

        static void Urchins(PropBatch batch, Vector3 p, System.Random rng)
        {
            var b = batch.W(PropMats.Urchin, p);
            int n = 2 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.25f);
                Vector3 c = p + new Vector3(o.x, 0.05f, o.y);
                float s = PropUtil.Range(rng, 0.05f, 0.09f);
                b.Octa(c, s);
                for (int k = 0; k < 6; k++)
                {
                    Vector3 d = new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, 0.1f, 1f), PropUtil.Range(rng, -1f, 1f)).normalized;
                    b.Cylinder(c + d * s * 0.6f, Quaternion.FromToRotation(Vector3.up, d), 0.012f, 0f, s * 1.6f, 3);
                }
            }
        }

        static void Anemone(PropBatch batch, Vector3 p, Material mat, System.Random rng)
        {
            var b = batch.W(mat, p);
            int n = 9 + rng.Next(6);
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.39996f;
                float rr = Mathf.Lerp(0.01f, 0.09f, Mathf.Sqrt((float)i / n));
                Vector3 o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 dir = (o * Mathf.Lerp(0.2f, 0.9f, rr / 0.09f) + Vector3.up).normalized;
                b.Cylinder(p + o * rr, Quaternion.FromToRotation(Vector3.up, dir), 0.02f, 0f, PropUtil.Range(rng, 0.08f, 0.14f), 4);
            }
        }

        // ================================================================== second beach
        public static IEnumerator SecondBeach(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("SecondBeach", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var full = new PropBatch(64f);
            var lite = new PropBatch(64f);
            var treeCols = new GameObject("PalmColliders");
            treeCols.transform.SetParent(root, false);
            treeCols.transform.position = Vector3.zero;
            var rng = new System.Random(4545);

            if (!PropBeach.FindLand(WorldLayout.SecondBeach, 0.4f, 1.8f, excl, out var S, 22f)) { batch.Flush(root, "SecondBeachMesh"); yield break; }
            Vector3 sea;
            if (!PropJungle.NearShore(S.x, S.z, 36f, out sea)) sea = (S - WorldLayout.IslandCenter).normalized;
            sea.y = 0f;
            sea.Normalize();
            Vector3 along = Vector3.Cross(Vector3.up, sea).normalized;
            PropInfo.SecondBeachSpot = S;

            // ---- hammock between two palms, set back from the water
            Vector3 hc = S - sea * 2.5f;
            Vector3 pa = hc - along * 2.7f, pb = hc + along * 2.7f;
            pa.y = G(pa); pb.y = G(pb);
            if (pa.y > 0.3f && pb.y > 0.3f)
            {
                PropJungle.AddPalm(full, lite, treeCols, rng, pa, sea + along * 0.15f);
                PropJungle.AddPalm(full, lite, treeCols, rng, pb, sea - along * 0.15f);
                Vector3 ha = pa + Vector3.up * 1.35f + along * 0.3f, hb = pb + Vector3.up * 1.35f - along * 0.3f;
                Hammock(batch, cols, ha, hb, sea);
                PropInfo.Hammock = (ha + hb) * 0.5f - Vector3.up * 0.55f;
                excl.Add(hc.x, hc.z, 4f);
            }

            // ---- parasol + towel, sandcastle, flotsam
            Vector3 pp = S + along * 6f + sea * 0.5f;
            pp.y = G(pp);
            if (pp.y > 0.25f)
            {
                PropBeach.Parasol(batch, cols, pp);
                Vector3 tw = pp + sea * 1.3f + along * 0.4f;
                tw.y = G(tw);
                PropBeach.Towel(batch, tw, PropUtil.Range(rng, 0f, 360f));
                excl.Add(pp.x, pp.z, 2.5f);
            }
            Vector3 sc = S - along * 6.5f + sea * 1.5f;
            sc.y = G(sc);
            if (sc.y > 0.15f) { Sandcastle(batch, cols, sc, rng); excl.Add(sc.x, sc.z, 1.5f); }

            // washed-up crate, barrel, driftwood, broken surfboard near the waterline
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = S + along * PropUtil.Range(rng, -14f, 14f) + sea * PropUtil.Range(rng, 2f, 5f);
                p.y = G(p);
                if (p.y < 0.05f || excl.Blocked(p.x, p.z)) continue;
                Quaternion r = Quaternion.Euler(PropUtil.Range(rng, -8f, 8f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -10f, 10f));
                switch (i)
                {
                    case 0:
                        batch.W(PropMats.Wood2, p).Box(p + Vector3.up * 0.25f, new Vector3(0.7f, 0.5f, 0.6f), r);
                        batch.W(PropMats.WoodDark, p).Box(p + Vector3.up * 0.5f + r * new Vector3(0.1f, 0f, 0f), new Vector3(0.55f, 0.05f, 0.62f), r * Quaternion.Euler(0f, 0f, 14f));
                        PropCol.Box(cols, p + Vector3.up * 0.25f, new Vector3(0.7f, 0.5f, 0.6f), r, "Crate");
                        break;
                    case 1:
                        Quaternion q = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 90f);
                        Vector3 axis = q * Vector3.up;
                        batch.W(PropMats.DrumRed, p).Cylinder(p + Vector3.up * 0.27f - axis * 0.44f, q, 0.29f, 0.29f, 0.88f, 8, true, true);
                        PropCol.Capsule(cols, p + Vector3.up * 0.27f, 0.29f, 0.88f, q, "Barrel");
                        break;
                    case 2:
                        Quaternion lq = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 90f);
                        Vector3 la = lq * Vector3.up;
                        batch.W(PropMats.Driftwood, p).Cylinder(p + Vector3.up * 0.14f - la * 1.4f, lq, 0.18f, 0.12f, 2.8f, 6, true, true);
                        break;
                    default:
                        Quaternion sr = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f) * Quaternion.Euler(-62f, 0f, 0f);
                        PropBeach.Surfboard(batch, p + Vector3.up * 0.65f, sr, PropMats.Flat(PropPal.Turquoise));
                        PropCol.Box(cols, p + Vector3.up * 0.65f, new Vector3(0.6f, 0.1f, 2.1f), sr, "Surfboard");
                        break;
                }
                excl.Add(p.x, p.z, 1.5f);
            }
            // a cold campfire ring
            Vector3 cf = S - sea * 5f + along * 3f;
            cf.y = G(cf);
            if (cf.y > 0.3f)
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI * 2f / 8f;
                    Vector3 sp = cf + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.7f;
                    sp.y = G(sp) + 0.05f;
                    float s = PropUtil.Range(rng, 0.15f, 0.22f);
                    PropRocks.Build(batch.W(PropMats.RockMid, sp), sp, new Vector3(s, s * 0.8f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0, 0.2f);
                }
                batch.W(PropMats.Ash, cf).Disk(cf + Vector3.up * 0.03f, 0.5f, 8, Quaternion.identity, true);
                batch.W(PropMats.Flat(PropPal.Ink), cf).Rod(cf + new Vector3(-0.3f, 0.06f, 0f), cf + new Vector3(0.3f, 0.1f, 0.1f), 0.05f, 0.04f, 5, true);
                excl.Add(cf.x, cf.z, 1.6f);
            }
            PropInfo.HelmetSpot = S + sea * 1.2f - along * 9f;
            PropInfo.HelmetSpot = new Vector3(PropInfo.HelmetSpot.x, G(PropInfo.HelmetSpot), PropInfo.HelmetSpot.z);
            excl.Add(S.x, S.z, 3f);

            batch.Flush(root, "SecondBeachMesh", ShadowCastingMode.On, 350f);
            PropBatch.FlushLod(full, lite, root, "SecondBeachPalms", ShadowCastingMode.On, PropJungle.TreeNear, PropJungle.TreeFar);
        }

        static void Hammock(PropBatch batch, Transform cols, Vector3 a, Vector3 b, Vector3 across)
        {
            Vector3 ab = b - a;
            float len = ab.magnitude;
            Vector3 dir = ab / len;
            Vector3 lat = Vector3.Cross(Vector3.up, dir).normalized;
            const int n = 8;
            float sag = 0.6f;
            Vector3 P(float t, float w) => Vector3.Lerp(a, b, t) - Vector3.up * (sag * Mathf.Sin(t * Mathf.PI)) + lat * (w * Mathf.Sin(Mathf.Clamp01(t * 1.15f - 0.075f) * Mathf.PI) * 0.5f) - Vector3.up * (Mathf.Abs(w) * 0.12f * Mathf.Sin(t * Mathf.PI));
            Material[] stripes = { PropMats.Flat(PropPal.Coral), PropMats.Trim, PropMats.Flat(PropPal.Turquoise), PropMats.Trim };
            for (int s = 0; s < 4; s++)
            {
                float w0 = -1f + s * 0.5f, w1 = w0 + 0.5f;
                var m = batch.W(stripes[s], a);
                for (int i = 0; i < n; i++)
                {
                    float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                    m.Quad2(P(t0, w0), P(t1, w0), P(t1, w1), P(t0, w1), Vector3.up);
                }
            }
            var rope = batch.W(PropMats.Rope, a);
            rope.Rod(a - dir * 0.3f, P(0.05f, 0f), 0.02f, 0.02f, 3);
            rope.Rod(b + dir * 0.3f, P(0.95f, 0f), 0.02f, 0.02f, 3);
            Vector3 mid = P(0.5f, 0f);
            PropCol.Box(cols, mid + Vector3.up * 0.05f, new Vector3(1.0f, 0.25f, len * 0.6f), Quaternion.LookRotation(dir), "Hammock");
        }

        static void Sandcastle(PropBatch batch, Transform cols, Vector3 p, System.Random rng)
        {
            var sand = batch.W(PropMats.Flat(PropMats.Value(PropPal.Sand, 0.9f)), p);
            sand.Cylinder(p - Vector3.up * 0.05f, Quaternion.Euler(0f, 22.5f, 0f), 0.62f, 0.55f, 0.28f, 8, false, true);
            for (int i = 0; i < 4; i++)
            {
                float a = (45f + i * 90f) * Mathf.Deg2Rad;
                Vector3 t = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.42f + Vector3.up * 0.22f;
                sand.Cylinder(t, Quaternion.identity, 0.13f, 0.12f, 0.3f, 6, false, true);
                sand.Cylinder(t + Vector3.up * 0.3f, Quaternion.identity, 0.14f, 0f, 0.14f, 6);
            }
            sand.Cylinder(p + Vector3.up * 0.22f, Quaternion.identity, 0.22f, 0.2f, 0.42f, 6, false, true);
            batch.W(PropMats.WoodLight, p).Rod(p + Vector3.up * 0.6f, p + Vector3.up * 0.95f, 0.01f, 0.01f, 3);
            batch.W(PropMats.Flat(PropPal.Coral), p).Tri2(p + Vector3.up * 0.95f, p + Vector3.up * 0.82f, p + new Vector3(0.16f, 0.88f, 0f), Vector3.forward);
            PropCol.Capsule(cols, p + Vector3.up * 0.3f, 0.6f, 0.6f, Quaternion.identity, "Sandcastle");
        }

        // ================================================================== radio tower ruins
        public static IEnumerator RadioTower(Transform island, PropExclusion excl)
        {
            var root = PropUtil.WorldRoot("RadioTowerRuins", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var batch = new PropBatch(0f);
            var rng = new System.Random(3535);

            // flattest spot nearby
            Vector3 c = WorldLayout.RadioTowerRuins;
            float best = float.MaxValue;
            Vector3 bc = c;
            for (int i = 0; i < 40; i++)
            {
                Vector2 o = i == 0 ? Vector2.zero : PropUtil.InDisc(rng, 12f);
                float x = c.x + o.x, z = c.z + o.y;
                PropGround.MinMax(x, z, 5f, out float mn, out float mx);
                if (mn < 1.5f) continue;
                float cost = (mx - mn) + o.magnitude * 0.03f;
                if (cost < best) { best = cost; bc = new Vector3(x, 0f, z); }
            }
            PropGround.MinMax(bc.x, bc.z, 3.4f, out float gmin, out float gmax);
            float padY = gmax + 0.2f;
            Vector3 pad = new Vector3(bc.x, padY, bc.z);
            Quaternion yaw = Quaternion.Euler(0f, 17f, 0f);
            PropInfo.RadioTowerBase = pad;

            // concrete pad (top exactly at padY, reaching below the lowest ground) with cracks
            float slabH = padY - (gmin - 0.4f);
            Vector3 slabC = pad - Vector3.up * (slabH * 0.5f);
            batch.W(PropMats.Concrete, pad).Box(slabC, new Vector3(6.4f, slabH, 6.4f), yaw);
            for (int i = 0; i < 4; i++)
                batch.W(PropMats.Ink, pad).Box(pad + yaw * new Vector3(PropUtil.Range(rng, -2.5f, 2.5f), 0.005f, PropUtil.Range(rng, -2.5f, 2.5f)), new Vector3(0.04f, 0.01f, PropUtil.Range(rng, 0.6f, 1.8f)), yaw * Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 180f), 0f));
            PropCol.Box(cols, slabC, new Vector3(6.4f, slabH, 6.4f), yaw, "Pad");

            // standing part of the lattice tower (0 – 10.5 m), slightly tilted
            const float full = 18f, broken = 10.5f;
            Quaternion tilt = yaw * Quaternion.Euler(4f, 0f, -5f);
            var standM = Matrix4x4.TRS(pad, tilt, Vector3.one);
            Lattice(batch, standM, pad, 0f, broken, full);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 l0 = standM.MultiplyPoint3x4(new Vector3(sx * 1.6f, 0f, sz * 1.6f));
                    Vector3 l1 = standM.MultiplyPoint3x4(new Vector3(sx * TowerHalf(broken, full), broken, sz * TowerHalf(broken, full)));
                    PropCol.Capsule(cols, (l0 + l1) * 0.5f, 0.12f, (l1 - l0).magnitude, Quaternion.FromToRotation(Vector3.up, (l1 - l0).normalized), "TowerLeg");
                }
            // fallen top part (10.5 – 18 m) lying beside the tower
            Vector3 fallDir = yaw * new Vector3(0.8f, 0f, -0.6f);
            fallDir.Normalize();
            Vector3 fallBase = pad + fallDir * 3.6f;
            fallBase.y = G(fallBase) + 0.5f;
            Quaternion fallRot = Quaternion.FromToRotation(Vector3.up, (fallDir + Vector3.up * 0.08f).normalized) * Quaternion.Euler(0f, 23f, 0f);
            var fallM = Matrix4x4.TRS(fallBase - fallRot * new Vector3(0f, broken, 0f), fallRot, Vector3.one);
            Lattice(batch, fallM, pad, broken, full, full);
            Vector3 fc = fallM.MultiplyPoint3x4(new Vector3(0f, (broken + full) * 0.5f, 0f));
            PropCol.Box(cols, fc, new Vector3(1.4f, full - broken, 1.4f), fallRot, "FallenTower");
            // a red/white warning light on the fallen top, an antenna mast
            Vector3 tipW = fallM.MultiplyPoint3x4(new Vector3(0f, full, 0f));
            batch.W(PropMats.LighthouseRed, tipW).Ico(tipW, Vector3.one * 0.18f, Quaternion.identity, 0);
            batch.W(PropMats.Metal, tipW).Rod(tipW, tipW + fallRot * Vector3.up * 2.4f, 0.04f, 0.02f, 4);
            if (PropRun.Due()) yield return null;

            // ruined concrete radio shack
            Vector3 shack = pad - yaw * new Vector3(5.6f, 0f, -1.5f);
            shack.y = G(shack);
            Quaternion sy = yaw * Quaternion.Euler(0f, 90f, 0f);
            Vector3 table = Shack(batch, cols, shack, sy, rng);
            PropInfo.RadioShack = table;

            // generator, cable spool, fallen dish, fence posts
            Vector3 gen = pad + yaw * new Vector3(-2f, 0f, 4.6f);
            gen.y = G(gen);
            Quaternion gr = yaw * Quaternion.Euler(0f, 30f, 0f);
            batch.W(PropMats.Flat(PropMats.Value(PropPal.Sun, 0.75f)), gen).Box(gen + Vector3.up * 0.45f, new Vector3(1.4f, 0.8f, 0.8f), gr);
            batch.W(PropMats.Rust, gen).Cylinder(gen + gr * new Vector3(-0.2f, 0.85f, 0f), gr * Quaternion.Euler(0f, 0f, 90f), 0.25f, 0.25f, 0.9f, 8, true, true);
            batch.W(PropMats.DarkMetal, gen).Rod(gen + gr * new Vector3(0.55f, 0.8f, 0.2f), gen + gr * new Vector3(0.55f, 1.6f, 0.25f), 0.05f, 0.05f, 6);
            PropCol.Box(cols, gen + Vector3.up * 0.55f, new Vector3(1.5f, 1.1f, 0.9f), gr, "Generator");
            Vector3 spool = pad + yaw * new Vector3(3.8f, 0f, 3.4f);
            spool.y = G(spool);
            Quaternion spr = yaw * Quaternion.Euler(0f, 0f, 90f);
            batch.W(PropMats.Wood2, spool).Cylinder(spool + Vector3.up * 0.45f - spr * Vector3.up * 0.35f, spr, 0.45f, 0.45f, 0.06f, 10, true, true);
            batch.W(PropMats.Wood2, spool).Cylinder(spool + Vector3.up * 0.45f + spr * Vector3.up * 0.29f, spr, 0.45f, 0.45f, 0.06f, 10, true, true);
            batch.W(PropMats.Ink, spool).Cylinder(spool + Vector3.up * 0.45f - spr * Vector3.up * 0.29f, spr, 0.3f, 0.3f, 0.58f, 10);
            PropCol.Capsule(cols, spool + Vector3.up * 0.45f, 0.45f, 0.7f, spr, "Spool");
            Vector3 dish = pad + yaw * new Vector3(4.2f, 0f, -3.8f);
            dish.y = G(dish) + 0.3f;
            Quaternion dr = yaw * Quaternion.Euler(70f, 40f, 0f);
            var dm = batch.W(PropMats.Metal, dish);
            dm.Cylinder(dish, dr, 0.15f, 1.3f, 0.45f, 10, false, false);
            dm.Cylinder(dish + dr * Vector3.up * 0.01f, dr, 0.14f, 1.28f, 0.44f, 10, false, false, 1f, 1f, 0f, true);
            dm.Rod(dish, dish + dr * Vector3.up * 1.0f, 0.03f, 0.03f, 4);
            PropCol.Box(cols, dish, new Vector3(2.4f, 0.8f, 2.4f), dr, "Dish");
            for (int i = 0; i < 6; i++)
            {
                float a = (i * 40f + 200f) * Mathf.Deg2Rad;
                Vector3 fp = pad + yaw * new Vector3(Mathf.Cos(a) * 9f, 0f, Mathf.Sin(a) * 9f);
                fp.y = G(fp);
                Quaternion lean = Quaternion.Euler(PropUtil.Range(rng, -12f, 12f), 0f, PropUtil.Range(rng, -12f, 12f));
                batch.W(PropMats.Metal, fp).Cylinder(fp - Vector3.up * 0.3f, lean, 0.04f, 0.04f, 2.1f, 6, false, true);
                PropCol.Capsule(cols, fp + Vector3.up * 0.9f, 0.06f, 1.8f, lean, "FencePost");
            }

            excl.Add(pad.x, pad.z, 7f);
            excl.Add(shack.x, shack.z, 4f);
            excl.Add(gen.x, gen.z, 1.5f);
            excl.Add(fc.x, fc.z, 5f);
            batch.Flush(root, "RadioTowerMesh", ShadowCastingMode.On, 600f);
        }

        static float TowerHalf(float y, float full) => Mathf.Lerp(1.6f, 0.55f, y / full);

        /// Rusty lattice tower segment from y0 to y1 (4 legs, rings every 1.5 m, X braces on every face).
        static void Lattice(PropBatch batch, Matrix4x4 m, Vector3 cell, float y0, float y1, float full)
        {
            var leg = batch.W(PropMats.RustDeep, cell);
            var brace = batch.W(PropMats.Rust, cell);
            Vector3 P(float sx, float sz, float y) => m.MultiplyPoint3x4(new Vector3(sx * TowerHalf(y, full), y, sz * TowerHalf(y, full)));
            int[] sxs = { -1, 1, 1, -1 };
            int[] szs = { -1, -1, 1, 1 };
            for (int k = 0; k < 4; k++) leg.Beam(P(sxs[k], szs[k], y0), P(sxs[k], szs[k], y1), 0.13f, 0.13f);
            for (float y = y0; y < y1 - 0.1f; y += 1.5f)
            {
                float yn = Mathf.Min(y1, y + 1.5f);
                for (int k = 0; k < 4; k++)
                {
                    int k2 = (k + 1) % 4;
                    brace.Beam(P(sxs[k], szs[k], yn), P(sxs[k2], szs[k2], yn), 0.07f, 0.07f);
                    brace.Beam(P(sxs[k], szs[k], y), P(sxs[k2], szs[k2], yn), 0.05f, 0.05f);
                    brace.Beam(P(sxs[k2], szs[k2], y), P(sxs[k], szs[k], yn), 0.05f, 0.05f);
                }
            }
        }

        /// Ruined shack of concrete blocks (broken top rows, door and window gaps) with an old radio on a table.
        /// Returns the world position on top of the table (the radio).
        static Vector3 Shack(PropBatch batch, Transform cols, Vector3 c, Quaternion r, System.Random rng)
        {
            const float W = 3.2f, D = 4.0f, BH = 0.4f, BL = 0.8f;
            const int rows = 6;
            Material[] blocks = { PropMats.Concrete, PropMats.ConcreteDark, PropMats.Flat(PropMats.Value(PropPal.Concrete, 0.92f)) };
            var floor = batch.W(PropMats.ConcreteDark, c);
            floor.Box(c + Vector3.up * 0.05f, new Vector3(W, 0.2f, D), r);
            // walls: (center offset, along axis, length, opening center, opening width, opening bottom row, top row)
            for (int w = 0; w < 4; w++)
            {
                bool alongX = w < 2;
                float len = alongX ? W : D;
                Vector3 off = alongX ? new Vector3(0f, 0f, (w == 0 ? 1 : -1) * D * 0.5f) : new Vector3((w == 2 ? 1 : -1) * W * 0.5f, 0f, 0f);
                Vector3 axis = alongX ? Vector3.right : Vector3.forward;
                float openC = w == 0 ? 0.4f : (w == 2 ? -0.5f : float.NaN);
                float openW = w == 0 ? 1.0f : 0.9f;
                int openR0 = w == 0 ? 0 : 2, openR1 = w == 0 ? 4 : 3;
                for (int row = 0; row < rows; row++)
                {
                    float shift = (row % 2) * BL * 0.5f;
                    for (float s = -len * 0.5f - shift; s < len * 0.5f - 0.05f; s += BL)
                    {
                        float a0 = Mathf.Max(s, -len * 0.5f), a1 = Mathf.Min(s + BL - 0.02f, len * 0.5f);
                        if (a1 - a0 < 0.1f) continue;
                        float mid = (a0 + a1) * 0.5f;
                        if (!float.IsNaN(openC) && row >= openR0 && row <= openR1 && Mathf.Abs(mid - openC) < openW * 0.5f + 0.15f) continue;
                        uint h = (uint)(row * 7919 + w * 104729 + Mathf.RoundToInt(mid * 100f) * 31);
                        h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
                        if (row >= 4 && (h & 3) != 0) continue;   // broken top edge
                        Vector3 lp = off + axis * mid + Vector3.up * (0.15f + row * BH + BH * 0.5f);
                        batch.W(blocks[(int)(h >> 4) % 3], c).Box(c + r * lp, alongX ? new Vector3(a1 - a0, BH - 0.02f, 0.25f) : new Vector3(0.25f, BH - 0.02f, a1 - a0), r);
                    }
                }
                // collider up to 1.75 m (the intact rows), with the door gap
                float colH = 4 * BH + 0.15f;
                if (w == 0)
                {
                    float l0 = (openC - openW * 0.5f) + len * 0.5f, l1 = len * 0.5f - (openC + openW * 0.5f);
                    PropCol.Box(cols, c + r * (off + axis * (-len * 0.5f + l0 * 0.5f) + Vector3.up * colH * 0.5f), alongX ? new Vector3(l0, colH, 0.26f) : new Vector3(0.26f, colH, l0), r, "ShackWall");
                    PropCol.Box(cols, c + r * (off + axis * (len * 0.5f - l1 * 0.5f) + Vector3.up * colH * 0.5f), alongX ? new Vector3(l1, colH, 0.26f) : new Vector3(0.26f, colH, l1), r, "ShackWall");
                }
                else PropCol.Box(cols, c + r * (off + Vector3.up * colH * 0.5f), alongX ? new Vector3(len, colH, 0.26f) : new Vector3(0.26f, colH, len), r, "ShackWall");
            }
            // rusty beam fallen across the room
            batch.W(PropMats.RustDeep, c).Beam(c + r * new Vector3(-1.4f, 2.2f, 1.6f), c + r * new Vector3(1.2f, 0.3f, -1.0f), 0.14f, 0.2f);
            // table with the old radio, chair, papers
            Vector3 t = c + r * new Vector3(-0.8f, 0.15f, -1.1f);
            var wood = batch.W(PropMats.Wood2, c);
            wood.Box(t + Vector3.up * 0.74f, new Vector3(1.1f, 0.06f, 0.6f), r);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    wood.Box(t + r * new Vector3(sx * 0.48f, 0.37f, sz * 0.24f), new Vector3(0.06f, 0.74f, 0.06f), r);
            PropCol.Box(cols, t + Vector3.up * 0.38f, new Vector3(1.1f, 0.76f, 0.6f), r, "RadioTable");
            Vector3 radio = t + Vector3.up * 0.77f;
            batch.W(PropMats.Flat(PropPal.HullBlue), c).Box(radio + Vector3.up * 0.16f, new Vector3(0.55f, 0.32f, 0.3f), r);
            batch.W(PropMats.Flat(PropPal.Paper), c).Box(radio + r * new Vector3(-0.08f, 0.2f, 0.151f), new Vector3(0.28f, 0.14f, 0.01f), r);
            for (int i = 0; i < 2; i++)
                batch.W(PropMats.Ink, c).Cylinder(radio + r * new Vector3(0.14f + i * 0.0f, 0.08f + i * 0.13f, 0.15f), r * Quaternion.Euler(90f, 0f, 0f), 0.035f, 0.035f, 0.03f, 6, true, true);
            batch.W(PropMats.Metal, c).Rod(radio + r * new Vector3(0.2f, 0.32f, -0.1f), radio + r * new Vector3(0.45f, 0.95f, -0.15f), 0.008f, 0.006f, 3);
            Vector3 ch = c + r * new Vector3(-0.8f, 0.15f, -0.4f);
            var cw = batch.W(PropMats.Wood, c);
            cw.Box(ch + Vector3.up * 0.45f, new Vector3(0.44f, 0.05f, 0.44f), r);
            for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
                    cw.Box(ch + r * new Vector3(sx * 0.18f, 0.22f, sz * 0.18f), new Vector3(0.05f, 0.44f, 0.05f), r);
            for (int i = 0; i < 5; i++)
                batch.W(PropMats.Paper, c).Box(c + r * new Vector3(PropUtil.Range(rng, -1.2f, 1.2f), 0.16f, PropUtil.Range(rng, -1.5f, 1.5f)), new Vector3(0.21f, 0.005f, 0.29f), r * Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f));
            return radio + Vector3.up * 0.2f;
        }
    }

    /// Tiny low-poly crab that scuttles sideways back and forth (distance culled).
    public class PropCrab : MonoBehaviour
    {
        static Mesh body, eyes;
        Vector3 basePos;
        Quaternion baseRot;
        float phase, nextCheck;
        bool near = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { body = null; eyes = null; }

        public static void Spawn(Transform parent, Vector3 pos, float yaw, int index)
        {
            if (body == null) BuildMeshes();
            var go = new GameObject("Crab");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            PropUtil.MeshObject(go.transform, "Body", body, PropMats.Crab, Vector3.zero, Quaternion.identity);
            PropUtil.MeshObject(go.transform, "Eyes", eyes, PropMats.Ink, Vector3.zero, Quaternion.identity, false);
            var c = go.AddComponent<PropCrab>();
            c.basePos = pos;
            c.baseRot = go.transform.rotation;
            c.phase = index * 1.37f;
        }

        static void BuildMeshes()
        {
            var b = new PropMeshBuilder();
            b.Ico(new Vector3(0f, 0.06f, 0f), new Vector3(0.11f, 0.05f, 0.08f), Quaternion.identity, 0);
            for (int s = -1; s <= 1; s += 2)
            {
                b.Ico(new Vector3(s * 0.12f, 0.07f, 0.09f), new Vector3(0.045f, 0.03f, 0.035f), Quaternion.Euler(0f, s * 25f, 0f), 0);
                b.Rod(new Vector3(s * 0.07f, 0.06f, 0.05f), new Vector3(s * 0.11f, 0.065f, 0.085f), 0.012f, 0.01f, 3);
                for (int k = 0; k < 3; k++)
                {
                    Vector3 a = new Vector3(s * 0.08f, 0.05f, 0.03f - k * 0.035f);
                    Vector3 m = a + new Vector3(s * 0.06f, 0.025f, -0.01f);
                    Vector3 f = m + new Vector3(s * 0.04f, -0.075f, -0.01f);
                    b.Rod(a, m, 0.009f, 0.008f, 3);
                    b.Rod(m, f, 0.008f, 0.004f, 3);
                }
                b.Rod(new Vector3(s * 0.03f, 0.09f, 0.06f), new Vector3(s * 0.035f, 0.13f, 0.065f), 0.006f, 0.006f, 3);
            }
            body = b.ToMesh("Crab");
            var e = new PropMeshBuilder();
            for (int s = -1; s <= 1; s += 2) e.Octa(new Vector3(s * 0.035f, 0.135f, 0.066f), 0.014f);
            eyes = e.ToMesh("CrabEyes");
        }

        void Update()
        {
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.5f;
                near = !PropCam.Valid || (PropCam.Position - basePos).sqrMagnitude < 40f * 40f;
            }
            if (!near) return;
            float t = Time.time * 0.7f + phase;
            float x = Mathf.Clamp(Mathf.Sin(t) * 1.6f, -1f, 1f) * 0.45f;
            transform.SetPositionAndRotation(basePos + baseRot * new Vector3(x, 0f, 0f), baseRot * Quaternion.Euler(0f, Mathf.Sin(t * 3.1f) * 4f, 0f));
        }
    }
}
