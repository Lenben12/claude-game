using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Small landmark islands next to the outer dive sites (GDD 15.34): palms, rocks, beach props and one landmark each
    /// – kelp: stilt fishing hut · wreck: rusty anchor + warning sign · blue drop: tall rock pillar with a bird nest ·
    /// trench: an old antenna. Round 3 (GDD §19 "Jede Insel sieht individuell aus") gives each its own identity
    /// (merged into "Identity_&lt;id&gt;" under MiniIsland_&lt;id&gt;): kelp = lush green cay with mangroves + sea-grass shallows ·
    /// wreck = rusty scrap beach (hull plates, girders, boiler, drums, chains, a washed-up lifeboat, dry grass, 2 palms) ·
    /// blue drop = basalt columns with seabird nests, wind-bent conifers, scrub, bare rock · trench = white coral-rubble
    /// atoll with one leaning palm and a ring of white sandbars · Fog Isle = tree ferns, moss mounds, mist, pink / violet
    /// glowing plants round Toni's camp. Every rock has a convex collider (≥ 0.35 m). When the world terrain has no island at WorldLayout's position
    /// the island itself is built here as a faceted low-poly mesh (beach, grass, rocky skirt down to the seabed) with a
    /// mesh collider and a foam ring. Deterministic (fixed seeds).
    public static class PropMiniIslands
    {
        sealed class Isle
        {
            public string siteId;
            public Vector3 c;
            public float R, top;
            public int seed;
            public bool own;              // built by props (true) or shaped by the world terrain (false)
            public float[] jit;           // coastline radius jitter per angle segment

            const int Seg = 32;
            static readonly float[] Tk = { 0f, 0.35f, 0.6f, 0.78f, 0.92f, 1.0f, 1.15f, 1.35f };

            public void Init()
            {
                jit = new float[Seg];
                for (int i = 0; i < Seg; i++)
                    jit[i] = 0.84f + 0.32f * PropNoise.Fbm(i * 0.35f, seed * 0.1f, 2, seed) * (0.85f + 0.3f * PropNoise.Hash01(i, seed, 3));
            }

            public float Jitter(float ang)
            {
                float f = Mathf.Repeat(ang / (Mathf.PI * 2f), 1f) * Seg;
                int i0 = Mathf.FloorToInt(f) % Seg, i1 = (i0 + 1) % Seg;
                return Mathf.Lerp(jit[i0], jit[i1], f - Mathf.Floor(f));
            }

            readonly float[] hk = new float[8];

            float Profile(float t, float seabed)
            {
                hk[0] = top; hk[1] = top * 0.85f; hk[2] = 1.8f; hk[3] = 0.7f; hk[4] = 0.15f; hk[5] = -0.6f; hk[6] = -3.0f; hk[7] = seabed;
                if (t <= 0f) return hk[0];
                for (int k = 0; k < Tk.Length - 1; k++)
                    if (t <= Tk[k + 1]) return Mathf.Lerp(hk[k], hk[k + 1], (t - Tk[k]) / (Tk[k + 1] - Tk[k]));
                return seabed;
            }

            /// Surface height of the island (own mesh) or of the world terrain.
            public float Height(float x, float z)
            {
                if (!own) return PropGround.H(x, z);
                float dx = x - c.x, dz = z - c.z;
                float r = Mathf.Sqrt(dx * dx + dz * dz);
                float t = r / (R * Jitter(Mathf.Atan2(dz, dx)));
                float seabed = Mathf.Max(PropGround.H(x, z), -45f);
                float h = Profile(t, Mathf.Min(seabed, -3.2f));
                if (t < 0.75f) h += 0.45f * (PropNoise.Value(x * 0.15f, z * 0.15f, seed) - 0.5f) * Mathf.Clamp01((0.75f - t) / 0.2f);
                return h;
            }

            public float ShoreRadius(float ang) => R * Jitter(ang) * 0.94f;
        }

        public static IEnumerator Build(Transform root, PropGrassField grass)
        {
            var isles = new[]
            {
                new Isle { siteId = "kelp_forest", c = WorldLayout.KelpIsland, seed = 41, top = 4.2f },
                new Isle { siteId = "wreck", c = WorldLayout.WreckIsland, seed = 53, top = 3.6f },
                new Isle { siteId = "blue_drop", c = WorldLayout.BlueDropIsland, seed = 67, top = 5.0f },
                new Isle { siteId = "deep_trench", c = WorldLayout.TrenchIsland, seed = 79, top = 6.0f },
                new Isle { siteId = "fog_isle", c = WorldLayout.FogIsland, seed = 91, top = 9.0f },
            };
            PropInfo.MiniIslandTops.Clear();
            PropInfo.MiniLandmarks.Clear();
            PropInfo.MiniShellSpots.Clear();
            for (int i = 0; i < isles.Length; i++)
            {
                var isle = isles[i];
                isle.R = WorldLayout.MiniIslandRadius * 0.86f;
                isle.own = PropGround.H(isle.c.x, isle.c.z) < 1.0f;
                isle.Init();
                if (!isle.own)
                {
                    // the world shaped this island: use its shoreline radius, no extra coastline jitter
                    foreach (var mi in WorldFeatures.MiniIslands)
                        if (mi.id == isle.siteId) isle.R = mi.shoreRadius / 0.94f;
                    for (int k = 0; k < isle.jit.Length; k++) isle.jit[k] = 1f;
                }
                yield return PropRun.Safe(BuildIsle(root, isle, grass), "mini island " + isle.siteId);
            }
        }

        static IEnumerator BuildIsle(Transform parent, Isle isle, PropGrassField grass)
        {
            var root = PropUtil.WorldRoot("MiniIsland_" + isle.siteId, parent);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var rng = new System.Random(isle.seed * 1000 + 7);
            string id = isle.siteId;
            // the dark volcanic look only for a props-built fallback trench island (the world-shaped one is the atoll)
            bool volcanic = isle.own && id == "deep_trench";
            Vector3 c = isle.c;
            float topY = isle.Height(c.x, c.z);
            PropInfo.MiniIslandTops.Add(new Vector3(c.x, topY, c.z));
            Vector3 toSite = Vector3.forward;
            var site = DiveSites.Get(isle.siteId);
            if (site != null) { toSite = site.center - c; toSite.y = 0f; toSite = toSite.sqrMagnitude > 1f ? toSite.normalized : Vector3.forward; }

            if (isle.own) BuildTerrain(root, cols, isle, volcanic);
            if (PropRun.Due()) yield return null;

            var batch = new PropBatch(0f);
            var full = new PropBatch(64f);
            var lite = new PropBatch(64f);
            var treeCols = new GameObject("PalmColliders");
            treeCols.transform.SetParent(root, false);
            treeCols.transform.position = Vector3.zero;
            var under = new PropBatch(0f);
            var spacing = new PropExclusion(8f);

            // ---- landmark first (keeps its space free)
            Vector3 lm;
            switch (isle.siteId)
            {
                case "kelp_forest": lm = StiltHut(root, batch, cols, isle, toSite, spacing); break;
                case "wreck": lm = AnchorAndSign(batch, cols, isle, toSite, spacing); break;
                case "blue_drop": lm = Pillar(batch, cols, isle, spacing, rng); break;
                case "fog_isle": lm = new Vector3(c.x, topY, c.z); break;   // Toni's camp: PropIslandExtras
                default: lm = Antenna(batch, cols, isle, spacing, rng); break;
            }
            PropInfo.MiniLandmarks[isle.siteId] = lm;

            // ---- round 3 (GDD §19 "Jede Insel sieht individuell aus"): the island's own identity
            var ident = new PropBatch(0f);
            var shallows = new PropBatch(0f);
            var temp = new List<PropModel>();
            try
            {
                switch (id)
                {
                    case "kelp_forest": KelpIdentity(ident, shallows, full, lite, cols, isle, spacing, rng); break;
                    case "wreck": WreckIdentity(ident, cols, isle, toSite, spacing, rng); break;
                    case "blue_drop": BlueIdentity(ident, full, lite, cols, isle, spacing, rng); break;
                    case "deep_trench": TrenchIdentity(ident, shallows, full, lite, cols, isle, toSite, spacing, rng); break;
                    case "fog_isle": FogIdentity(root, ident, full, lite, cols, isle, spacing, rng); break;
                }
            }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: island identity " + id + " failed: " + e); }
            if (PropRun.Due()) yield return null;

            // ---- palms (on sand / grass), leaning outward – lush on the kelp cay, almost none on the scrap / basalt
            // islands, the trench atoll has its single leaning palm (identity)
            int palms = volcanic ? 4 : id == "kelp_forest" ? 10 + rng.Next(4) : id == "wreck" ? 2 : id == "fog_isle" ? 5 : id == "blue_drop" || id == "deep_trench" ? 0 : 9 + rng.Next(4);
            for (int i = 0, a = 0; i < palms && a < 400; a++)
            {
                float ang = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * PropUtil.Range(rng, 0.25f, 0.82f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < 0.5f || spacing.Blocked(x, z)) continue;
                PropJungle.AddPalm(full, lite, treeCols, rng, new Vector3(x, g, z), new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)), isle.own ? g : float.NaN);
                spacing.Add(x, z, 2.5f);
                i++;
            }
            // ---- bushes and grass on the green top (not on a volcanic island built here; the world-shaped trench
            // island has a green top too, and a bare lawn is the #1 "not How to Fish" tell)
            if (!volcanic)
            {
                // kelp: lush (bushes, flowering bushes, taro, bananas) · wreck: none · blue drop: low wind-pruned scrub ·
                // trench atoll: a little beach scrub · Fog Isle: bushes + ferns under the tree ferns
                int plants = id == "kelp_forest" ? 16 : id == "wreck" ? 0 : id == "blue_drop" ? 9 : id == "deep_trench" ? 4 : 8;
                float minG = id == "deep_trench" ? 0.9f : 1.6f;
                for (int i = 0, a = 0; i < plants && a < 300; a++)
                {
                    Vector2 o = PropUtil.InDisc(rng, isle.R * (id == "kelp_forest" ? 0.65f : 0.5f));
                    float x = c.x + o.x, z = c.z + o.y;
                    float g = isle.Height(x, z);
                    if (g < minG || spacing.Blocked(x, z)) continue;
                    int kind; float sc = 1f;
                    switch (id)
                    {
                        case "kelp_forest": { double k = rng.NextDouble(); kind = k < 0.3 ? 0 : k < 0.55 ? 1 : k < 0.8 ? 3 : 4; break; }
                        case "blue_drop": kind = 0; sc = 0.55f; break;
                        case "deep_trench": kind = 0; sc = 0.6f; break;
                        case "fog_isle": kind = rng.NextDouble() < 0.5 ? 0 : 2; sc = 1.15f; break;
                        default: kind = rng.NextDouble() < 0.4 ? 1 : 0; break;
                    }
                    PropJungle.AddPlant(under, under, rng, new Vector3(x, g, z), kind, sc);
                    spacing.Add(x, z, 1.2f);
                    i++;
                }
                if (grass != null)
                {
                    // meadow grass on green tops; the wreck island only has dry yellow dune grass, the atoll and the
                    // basalt island are sparse
                    bool dry = id == "wreck" || id == "deep_trench";
                    float keep = id == "wreck" ? 0.55f : id == "deep_trench" ? 0.3f : id == "blue_drop" ? 0.35f : 1f;
                    for (float gx = -isle.R; gx <= isle.R; gx += 1.3f)
                        for (float gz = -isle.R; gz <= isle.R; gz += 1.3f)
                        {
                            float x = c.x + gx + PropUtil.Range(rng, -0.5f, 0.5f), z = c.z + gz + PropUtil.Range(rng, -0.5f, 0.5f);
                            float g = isle.Height(x, z);
                            double roll = rng.NextDouble();
                            if (g < (dry ? 0.7f : 1.4f) || spacing.Blocked(x, z, -0.6f)) continue;
                            if (roll > keep) continue;
                            bool top = g > 1.9f && !dry;
                            if (!top && !dry && rng.NextDouble() > 0.15) continue;
                            int v = top ? rng.Next(PropGrassField.MeadowCount) : PropGrassField.DuneFirst + rng.Next(PropGrassField.DuneCount);
                            grass.Add(v, new Vector3(x, g - 0.03f, z), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0.85f, 1.2f), Vector3.zero);
                        }
                }
            }
            // ---- rocks along the shore
            // (round 3) every rock gets a convex collider down to 0.35 m; material / count per island identity
            bool rocky = volcanic || id == "wreck" || id == "blue_drop";
            int rocks = volcanic ? 16 : id == "wreck" ? 16 : id == "blue_drop" ? 12 : id == "fog_isle" ? 10 : id == "deep_trench" ? 4 : 7;
            for (int i = 0; i < rocks; i++)
            {
                float ang = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * PropUtil.Range(rng, rocky ? 0.45f : 0.85f, 1.08f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                double mroll = rng.NextDouble();
                if (spacing.Blocked(x, z)) continue;
                float g = isle.Height(x, z);
                float s = PropUtil.Range(rng, 0.6f, rocky ? 2.2f : 1.8f);
                Vector3 sc = new Vector3(s * 1.3f, s * (rocky ? 0.85f : 0.6f), s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -8f, 8f), PropUtil.Range(rng, 0f, 360f), 0f);
                int seed = rng.Next();
                float rough = rocky ? 0.22f : 0.14f;
                Material main, top;
                switch (id)
                {
                    case "wreck": main = mroll < 0.5 ? PropMats.RockMid : PropMats.BeachRock; top = null; break;
                    case "blue_drop": main = mroll < 0.6 ? PropMats.Basalt : PropMats.RockDark; top = null; break;
                    case "fog_isle": main = PropMats.BeachRock; top = PropMats.Moss; break;
                    default: main = PropMats.BeachRock; top = PropMats.Moss; break;
                }
                if (volcanic) { main = mroll < 0.5 ? PropMats.Volcanic : PropMats.VolcanicDark; top = null; }
                PropRocks.BuildSplit(batch, p, p, sc, q, seed, 1, rough, main, top, PropMats.Crevice);
                if (s * 0.6f >= 0.35f) PropSolid.Rock(cols, p, sc, q, seed, 1, rough, p.y < 0.6f, "Rock");
                spacing.Add(x, z, s * 1.1f);
            }
            // ---- golden shell spots on the beach (wreck and blue drop islands)
            if (isle.siteId == "wreck" || isle.siteId == "blue_drop")
            {
                for (int tries = 0; tries < 20; tries++)
                {
                    float ang = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                    float rr = isle.ShoreRadius(ang) * 0.84f;
                    float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                    float g = isle.Height(x, z);
                    if (g < 0.2f || spacing.Blocked(x, z)) continue;
                    PropInfo.MiniShellSpots.Add(new Vector3(x, g + 0.02f, z));
                    spacing.Add(x, z, 1f);
                    break;
                }
            }

            // ---- a bit of flotsam on the beach
            for (int i = 0; i < 2; i++)
            {
                float ang = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * 0.86f;
                Vector3 p = new Vector3(c.x + Mathf.Cos(ang) * rr, 0f, c.z + Mathf.Sin(ang) * rr);
                p.y = isle.Height(p.x, p.z);
                if (p.y < 0.1f || spacing.Blocked(p.x, p.z)) continue;
                Quaternion lq = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 90f);
                Vector3 la = lq * Vector3.up;
                batch.W(PropMats.Driftwood, p).Cylinder(p + Vector3.up * 0.13f - la * 1.2f, lq, 0.16f, 0.11f, 2.4f, 6, true, true);
            }

            // ---- Q54 rest spot, shells, the island secret (and Toni's camp on the Fog Isle)
            try { PropIslandExtras.Build(root, batch, cols, isle.siteId, c, isle.R, isle.Height, isle.ShoreRadius, spacing, rng); }
            catch (System.Exception e) { Debug.LogError("Sealebrities props: island extras " + isle.siteId + " failed: " + e); }
            // the Fog Isle's odd glowing plants grow round Toni's camp (placed by the extras)
            if (id == "fog_isle")
            {
                try { FogGlowPlants(root, ident, isle, spacing, rng, temp); }
                catch (System.Exception e) { Debug.LogError("Sealebrities props: fog glow plants failed: " + e); }
            }

            if (!ident.IsEmpty) ident.Flush(root, "Identity_" + id, ShadowCastingMode.On, 700f);
            if (!shallows.IsEmpty) shallows.Flush(root, "Identity_" + id + "_Shallows", ShadowCastingMode.Off, 110f);
            for (int i = 0; i < temp.Count; i++)
                foreach (var part in temp[i].parts)
                    if (part.mesh != null) Object.Destroy(part.mesh);
            batch.Flush(root, "IslandProps", ShadowCastingMode.On, 900f);
            under.Flush(root, "IslandPlants", ShadowCastingMode.On, 120f);
            PropBatch.FlushLod(full, lite, root, "IslandPalms", ShadowCastingMode.On, PropJungle.TreeNear, 2400f);
        }

        // ================================================================== round 3: island identities (GDD §19)
        static float RR(System.Random r, float a, float b) => PropUtil.Range(r, a, b);

        static Vector3 FlatDir(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// Kelp island – a lush green cay: mangroves with arching prop roots along the shore and sea-grass meadows in the
        /// shallows (the plant pass adds bushes, flowering bushes, taro and bananas; the stilt hut is the landmark).
        static void KelpIdentity(PropBatch ident, PropBatch shallows, PropBatch full, PropBatch lite, Transform cols, Isle isle, PropExclusion spacing, System.Random rng)
        {
            Vector3 c = isle.c;
            // keep the "G+H 1989" carving cove (PropIslandExtras.KelpCarving: far side, +0.35 rad) free
            var site = DiveSites.Get(isle.siteId);
            if (site != null)
            {
                Vector3 far = FlatDir(c - site.center);
                float ca = Mathf.Atan2(far.z, far.x) + 0.35f;
                float cr = isle.ShoreRadius(ca) * 0.8f;
                spacing.Add(c.x + Mathf.Cos(ca) * cr, c.z + Mathf.Sin(ca) * cr, 7f);
            }
            for (int t = 0, n = 0; t < 260 && n < 8; t++)
            {
                float ang = RR(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.9f, 1.06f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < -0.9f || g > 0.7f || spacing.Blocked(x, z)) continue;
                PropVolcanoPlants.AddMangrove(full, lite, cols, rng, new Vector3(x, Mathf.Max(g, -0.3f), z));
                spacing.Add(x, z, 3.4f);
                n++;
            }
            // beach morning-glory carpets: low green leaf mats with pink flowers creeping over the upper beach
            var leafTop = ident.W(PropMats.Grass, c);
            var leafUnder = ident.W(PropMats.PalmLeafDark, c);
            var flower = ident.W(PropMats.FlowerPink, c);
            for (int t = 0, n = 0; t < 200 && n < 14; t++)
            {
                float ang = RR(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.62f, 0.86f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < 0.4f || g > 2.6f || spacing.Blocked(x, z, -0.5f)) continue;
                int leaves = 14 + rng.Next(10);
                for (int i = 0; i < leaves; i++)
                {
                    Vector2 o = PropUtil.InDisc(rng, 1.6f);
                    float lx = x + o.x, lz = z + o.y;
                    float lg = isle.Height(lx, lz) + 0.04f;
                    float a = RR(rng, 0f, Mathf.PI * 2f);
                    Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * RR(rng, 0.12f, 0.2f);
                    Vector3 l = new Vector3(-d.z, 0f, d.x) * 0.8f;
                    Vector3 b0 = new Vector3(lx, lg, lz);
                    PropPlants.TwoTone(leafTop, leafUnder, b0 - d, b0 + l + Vector3.up * 0.05f, b0 + d);
                    PropPlants.TwoTone(leafTop, leafUnder, b0 - d, b0 + d, b0 - l + Vector3.up * 0.05f);
                    if (i % 4 == 0) PropPlants.StarFlower(flower, null, b0 + Vector3.up * 0.08f, Vector3.up, 0.07f, a);
                }
                spacing.Add(x, z, 1.6f);
                n++;
            }
            // sea-grass meadows in 0.6–3 m of water
            int patches = 0;
            for (int t = 0; t < 140 && patches < 10; t++)
            {
                float ang = RR(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) + RR(rng, 3f, 24f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float d = -isle.Height(x, z);
                if (d < 0.6f || d > 3f) continue;
                float pr = RR(rng, 3f, 5.5f);
                int tufts = 16 + rng.Next(10);
                for (int i = 0; i < tufts; i++)
                {
                    Vector2 o = PropUtil.InDisc(rng, pr);
                    float tx = x + o.x, tz = z + o.y;
                    float tg = isle.Height(tx, tz);
                    if (-tg < 0.35f) continue;
                    SeaGrassTuft(shallows, new Vector3(tx, tg, tz), rng, Mathf.Min(1f, -tg - 0.1f));
                }
                if (patches < 5) PropInfo.AddHabitat(new Vector3(x, -d + 0.3f, z), pr, HabitatKind.SeaGrass, isle.siteId);
                patches++;
            }
        }

        static void SeaGrassTuft(PropBatch b, Vector3 p, System.Random rng, float maxH)
        {
            int blades = 5 + rng.Next(3);
            var a = b.W(PropMats.SeaGrass, p);
            var d = b.W(PropMats.SeaGrassDark, p);
            for (int i = 0; i < blades; i++)
            {
                float ang = RR(rng, 0f, Mathf.PI * 2f);
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 lat = new Vector3(dir.z, 0f, -dir.x);
                Vector3 bp = p + dir * RR(rng, 0f, 0.12f) - Vector3.up * 0.03f;
                float h = RR(rng, 0.4f, 0.9f) * Mathf.Max(0.4f, maxH);
                Vector3 tip = bp + Vector3.up * h + dir * RR(rng, 0.05f, 0.25f);
                (i % 2 == 0 ? a : d).Tri2(bp - lat * 0.035f, bp + lat * 0.035f, tip, dir);
            }
        }

        /// Wreck island – a rusty, rocky scrap beach: bent hull plates, girders and a boiler section half buried, oil
        /// drums, chains dragged across the sand and a washed-up orange lifeboat; dry yellow grass, two palms only.
        static void WreckIdentity(PropBatch ident, Transform cols, Isle isle, Vector3 toSite, PropExclusion spacing, System.Random rng)
        {
            Vector3 c = isle.c;
            float siteAng = Mathf.Atan2(toSite.z, toSite.x);
            var scrap = new List<Vector3>();
            for (int t = 0, n = 0; t < 180 && n < 8; t++)
            {
                float ang = siteAng + RR(rng, -1.9f, 1.9f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.6f, 0.93f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < 0.15f || g > 4f || spacing.Blocked(x, z)) continue;
                Vector3 p = new Vector3(x, g, z);
                Quaternion q = Quaternion.Euler(0f, RR(rng, 0f, 360f), 0f);
                switch (n % 3)
                {
                    case 0:
                    {
                        // a bent hull plate sticking out of the sand, a row of rivets
                        float w = RR(rng, 1.6f, 2.8f), h = RR(rng, 1.0f, 1.8f);
                        Quaternion pq = q * Quaternion.Euler(RR(rng, 25f, 55f), 0f, RR(rng, -10f, 10f));
                        Vector3 pc = p + pq * new Vector3(0f, h * 0.5f - 0.35f, 0f);
                        ident.W(PropMats.HullLower, p).Box(pc, new Vector3(w, h, 0.07f), pq);
                        Quaternion bq = pq * Quaternion.Euler(-28f, 0f, 0f);
                        Vector3 bc = pc + pq * new Vector3(0f, h * 0.5f, 0f) + bq * new Vector3(0f, h * 0.22f, 0f);
                        ident.W(PropMats.Rust, p).Box(bc, new Vector3(w * 0.96f, h * 0.45f, 0.07f), bq);
                        var rv = ident.W(PropMats.RustDeep, p);
                        for (int k = 0; k < 5; k++) rv.Box(pc + pq * new Vector3(-w * 0.4f + k * w * 0.2f, h * 0.3f, 0.05f), new Vector3(0.06f, 0.06f, 0.04f), pq);
                        PropSolid.Box(cols, pc, new Vector3(w, h, 0.3f), pq, "HullPlate");
                        break;
                    }
                    case 1:
                    {
                        // an I-girder lying half buried
                        float len = RR(rng, 3f, 5f);
                        Vector3 ax = q * Vector3.forward;
                        Vector3 a = p - ax * len * 0.5f + Vector3.up * 0.1f, b2 = p + ax * len * 0.5f + Vector3.up * 0.45f;
                        Vector3 ud = Vector3.Cross(Vector3.Cross((b2 - a).normalized, Vector3.up), (b2 - a).normalized).normalized;
                        if (ud.y < 0f) ud = -ud;
                        var m = ident.W(PropMats.RustDeep, p);
                        m.Beam(a, b2, 0.06f, 0.34f);
                        m.Beam(a + ud * 0.17f, b2 + ud * 0.17f, 0.3f, 0.05f);
                        m.Beam(a - ud * 0.17f, b2 - ud * 0.17f, 0.3f, 0.05f);
                        PropSolid.Segment(cols, a, b2, 0.2f, "Girder");
                        break;
                    }
                    default:
                    {
                        // a boiler / tank section on its side, dark inside
                        float len = RR(rng, 1.6f, 2.6f), r = RR(rng, 0.55f, 0.85f);
                        Quaternion tq = q * Quaternion.Euler(90f, 0f, 0f);
                        Vector3 ax = tq * Vector3.up;
                        Vector3 b0 = p + Vector3.up * (r * 0.75f) - ax * len * 0.5f;
                        ident.W(PropMats.Rust, p).Cylinder(b0, tq, r, r, len, 9, false, true);
                        ident.W(PropMats.HoleDark, p).Disk(b0 + ax * 0.02f, r * 0.9f, 9, tq, false);
                        PropSolid.Segment(cols, b0, b0 + ax * len, r, "Tank");
                        break;
                    }
                }
                scrap.Add(p);
                spacing.Add(x, z, 2.6f);
                n++;
            }
            // oil drums beside the scrap (some lying, faded colours)
            Material[] drum = { PropMats.Rust, PropMats.DrumRed, PropMats.DrumBlue, PropMats.RustDeep };
            for (int i = 0, n = 0; scrap.Count > 0 && i < scrap.Count * 4 && n < 7; i++)
            {
                Vector3 s = scrap[i % scrap.Count];
                float a = RR(rng, 0f, Mathf.PI * 2f);
                Vector3 p = s + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * RR(rng, 1.8f, 3.2f);
                p.y = isle.Height(p.x, p.z);
                if (p.y < 0.1f || spacing.Blocked(p.x, p.z)) continue;
                PropKit.Barrel(ident, cols, p, drum[rng.Next(drum.Length)], rng.NextDouble() < 0.5, RR(rng, 0f, 360f));
                spacing.Add(p.x, p.z, 0.9f);
                n++;
            }
            // chains dragged across the sand
            for (int k = 0; k < 2 && k < scrap.Count; k++)
            {
                Vector3 s = scrap[k];
                Vector3 dir = FlatDir(c - s);
                ChainRun(ident, s + dir * 1.4f, Quaternion.Euler(0f, k == 0 ? 25f : -30f, 0f) * dir, 12, isle);
            }
            // the washed-up lifeboat, tipped on its side
            for (int t = 0; t < 60; t++)
            {
                float ang = siteAng + Mathf.PI * 0.5f + RR(rng, -0.9f, 0.9f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.72f, 0.88f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < 0.2f || g > 2.8f || spacing.Blocked(x, z)) continue;
                Vector3 along = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Quaternion q = Quaternion.LookRotation(along) * Quaternion.Euler(0f, RR(rng, -20f, 20f), 24f);
                Lifeboat(ident, cols, Matrix4x4.TRS(new Vector3(x, g - 0.12f, z), q, Vector3.one));
                spacing.Add(x, z, 3.2f);
                PropInfo.SetSpot("wreck_lifeboat", new Vector3(x, g, z));
                break;
            }
        }

        static void ChainRun(PropBatch b, Vector3 start, Vector3 dir, int links, Isle isle)
        {
            dir.y = 0f;
            dir.Normalize();
            Vector3 lat = Vector3.Cross(Vector3.up, dir);
            Quaternion stand = Quaternion.FromToRotation(Vector3.up, lat);
            var m = b.W(PropMats.RustDeep, start);
            for (int i = 0; i < links; i++)
            {
                Vector3 p = start + dir * (i * 0.24f) + lat * (Mathf.Sin(i * 0.5f) * 0.25f);
                p.y = isle.Height(p.x, p.z) + 0.04f;
                if (i % 2 == 0) m.Torus(p, Quaternion.identity, 0.12f, 0.03f, 6, 3);
                else m.Torus(p + Vector3.up * 0.08f, stand, 0.12f, 0.03f, 6, 3);
            }
        }

        /// Orange lifeboat hull (4.4 m) with white gunwales, thwarts and a rope fender; one box collider.
        static void Lifeboat(PropBatch b, Transform cols, Matrix4x4 trs)
        {
            var hb = new PropMeshBuilder();
            const int secs = 8;
            const float L = 4.4f;
            var prev = new Vector3[5];
            var ring = new Vector3[5];
            for (int k = 0; k <= secs; k++)
            {
                float t = k / (float)secs;
                float z = (t - 0.5f) * L;
                float st = Mathf.Max(0f, Mathf.Sin(Mathf.PI * t));
                float hw = 0.9f * Mathf.Pow(st, 0.45f) + 0.06f;
                float depth = 0.78f * Mathf.Pow(st, 0.3f) + 0.06f;
                float sheer = 0.22f * (1f - st);
                ring[0] = new Vector3(-hw, depth + sheer, z);
                ring[1] = new Vector3(-hw * 0.85f, depth * 0.3f, z);
                ring[2] = new Vector3(0f, 0f, z);
                ring[3] = new Vector3(hw * 0.85f, depth * 0.3f, z);
                ring[4] = new Vector3(hw, depth + sheer, z);
                if (k > 0)
                    for (int j = 0; j < 4; j++)
                        hb.QuadAuto(prev[j], prev[j + 1], ring[j + 1], ring[j], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                            (prev[j] + ring[j + 1]) * 0.5f - new Vector3(0f, depth, z));
                for (int j = 0; j < 5; j++) prev[j] = ring[j];
            }
            hb.DoubleSide(0, 0);
            Vector3 cell = trs.GetColumn(3);
            b.B(PropMats.Orange, cell).Append(hb, trs);
            var tb = new PropMeshBuilder();
            for (int s = -1; s <= 1; s += 2) tb.Beam(new Vector3(s * 0.88f, 0.84f, -L * 0.34f), new Vector3(s * 0.88f, 0.84f, L * 0.34f), 0.08f, 0.1f);
            b.B(PropMats.White, cell).Append(tb, trs);
            var wb = new PropMeshBuilder();
            for (int i = -1; i <= 1; i++) wb.Box(new Vector3(0f, 0.6f, i * 1.1f), new Vector3(1.55f, 0.06f, 0.3f));
            b.B(PropMats.WoodLight, cell).Append(wb, trs);
            var rb = new PropMeshBuilder();
            for (int s = -1; s <= 1; s += 2) rb.Rod(new Vector3(s * 0.93f, 0.62f, -L * 0.3f), new Vector3(s * 0.93f, 0.62f, L * 0.3f), 0.025f, 0.025f, 4);
            b.B(PropMats.Rope, cell).Append(rb, trs);
            PropSolid.Box(cols, trs.MultiplyPoint3x4(new Vector3(0f, 0.45f, 0f)), new Vector3(1.9f, 0.9f, L), trs.rotation, "Lifeboat");
        }

        /// Blue drop island – a tall basalt island: hexagonal basalt column clusters (one at the pillar foot, three on
        /// the shore) with seabird nests and guano streaks, wind-bent conifers and low scrub on bare rock.
        static void BlueIdentity(PropBatch ident, PropBatch full, PropBatch lite, Transform cols, Isle isle, PropExclusion spacing, System.Random rng)
        {
            Vector3 c = isle.c;
            Vector3 pillar = c + new Vector3(4f, 0f, -3f);   // see Pillar()
            // first cluster on the far side of the pillar from its rope ladder (ladder dir ≈ (−0.93, 0, 0.37))
            var centers = new List<Vector3> { pillar + new Vector3(4.4f, 0f, -1.4f) };
            for (int t = 0; t < 80 && centers.Count < 4; t++)
            {
                float ang = RR(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.72f, 0.9f);
                Vector3 p = new Vector3(c.x + Mathf.Cos(ang) * rr, 0f, c.z + Mathf.Sin(ang) * rr);
                float g = isle.Height(p.x, p.z);
                if (g < 0.2f || g > 5f || spacing.Blocked(p.x, p.z)) continue;
                bool apart = true;
                for (int i = 0; i < centers.Count; i++) if ((centers[i] - p).sqrMagnitude < 14f * 14f) apart = false;
                if (apart) centers.Add(p);
            }
            for (int k = 0; k < centers.Count; k++)
            {
                Vector3 cc = centers[k];
                int n = 6 + rng.Next(7);
                float tallest = 0f;
                Vector3 top = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    Vector2 o = i == 0 ? Vector2.zero : PropUtil.InDisc(rng, 2.4f);
                    Vector3 b0 = new Vector3(cc.x + o.x, 0f, cc.z + o.y);
                    b0.y = isle.Height(b0.x, b0.z) - 0.4f;
                    float r = RR(rng, 0.42f, 0.66f);
                    float h = RR(rng, 1.2f, 4.6f) * (k == 0 ? 1.15f : 1f) * (1f - o.magnitude / 8f);
                    Quaternion q = Quaternion.Euler(RR(rng, -4f, 4f), RR(rng, 0f, 60f), RR(rng, -4f, 4f));
                    if (i > 0 && spacing.Blocked(b0.x, b0.z, -0.8f)) continue;
                    ident.W(i % 3 == 0 ? PropMats.RockDark : PropMats.Basalt, b0).Cylinder(b0, q, r, r * 0.97f, h, 6, false, false);
                    Vector3 tp = b0 + q * Vector3.up * h;
                    ident.W(PropMats.RockMid, b0).Cylinder(tp - q * Vector3.up * 0.04f, q, r * 0.97f, r * 0.9f, 0.1f, 6, false, true);
                    PropSolid.Upright(cols, b0, r, h + 0.05f, b0.y + h < 0.6f);
                    if (h > tallest) { tallest = h; top = tp + Vector3.up * 0.06f; }
                }
                spacing.Add(cc.x, cc.z, 3.4f);
                if (top == Vector3.zero) continue;
                SeabirdNest(ident, top, rng);
                PropInfo.AddLandSpot(top + Vector3.up * 0.05f, LandSpotKind.Nest, 0.5f, isle.siteId);
            }
            // wind-bent conifers (leaning away from the easterly trade wind) on the green top
            Vector3 lee = new Vector3(-0.93f, 0f, 0.37f);
            for (int t = 0, n = 0; t < 180 && n < 7; t++)
            {
                Vector2 o = PropUtil.InDisc(rng, isle.R * 0.7f);
                float x = c.x + o.x, z = c.z + o.y;
                float g = isle.Height(x, z);
                if (g < 2.2f || spacing.Blocked(x, z)) continue;
                PropVolcanoPlants.AddBentConifer(full, lite, cols, rng, new Vector3(x, g, z), lee, RR(rng, 0.38f, 0.55f));
                spacing.Add(x, z, 3f);
                n++;
            }
        }

        static void SeabirdNest(PropBatch b, Vector3 p, System.Random rng)
        {
            var tw = b.W(PropMats.Flat(PropMats.Value(PropPal.WoodLight, 0.8f)), p);
            for (int i = 0; i < 9; i++)
            {
                float a = i * 0.7f;
                tw.Rod(p + new Vector3(Mathf.Cos(a) * 0.3f, 0.04f, Mathf.Sin(a) * 0.3f), p + new Vector3(Mathf.Cos(a + 1.4f) * 0.28f, 0.08f, Mathf.Sin(a + 1.4f) * 0.28f), 0.025f, 0.02f, 3);
            }
            b.W(PropMats.FlowerWhite, p).Ico(p + new Vector3(0.05f, 0.08f, 0f), new Vector3(0.06f, 0.075f, 0.06f), Quaternion.identity, 0);
            var w = b.W(PropMats.White, p);
            for (int i = 0; i < 4; i++)
            {
                float a = RR(rng, 0f, Mathf.PI * 2f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float l = RR(rng, 0.5f, 1.3f);
                w.Box(p + d * 0.55f - Vector3.up * (l * 0.5f + 0.05f), new Vector3(0.08f, l, 0.03f), Quaternion.LookRotation(d));
            }
        }

        /// Deep-trench island – a tiny white-sand atoll: bleached coral rubble and dead staghorn branches on the beach,
        /// one palm leaning far out over the water, white sandbars in a ring offshore round a turquoise lagoon (the
        /// antenna stays the landmark).
        static void TrenchIdentity(PropBatch ident, PropBatch shallows, PropBatch full, PropBatch lite, Transform cols, Isle isle, Vector3 toSite, PropExclusion spacing, System.Random rng)
        {
            Vector3 c = isle.c;
            {
                // keep the buried TV's spot free (PropIslandExtras.TrenchTV: far side, −0.3 rad, 0.82 × shore)
                float ta = Mathf.Atan2(-toSite.z, -toSite.x) - 0.3f;
                float tr = isle.ShoreRadius(ta) * 0.82f;
                spacing.Add(c.x + Mathf.Cos(ta) * tr, c.z + Mathf.Sin(ta) * tr, 3.5f);
            }
            Material[] rubble ={ PropMats.Flat(LowPoly.Hex("#F2EEE4")), PropMats.Flat(LowPoly.Hex("#E6DCCB")), PropMats.Flat(LowPoly.Hex("#F2C4B4")) };
            for (int t = 0, n = 0; t < 360 && n < 64; t++)
            {
                float ang = RR(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.8f, 1.0f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < 0.02f || g > 1.6f || spacing.Blocked(x, z, -0.5f)) continue;
                float s = RR(rng, 0.07f, 0.26f);
                Vector3 p = new Vector3(x, g + s * 0.2f, z);
                var m = rubble[rng.NextDouble() < 0.15 ? 2 : rng.Next(2)];
                if (n % 4 == 0)
                {
                    // a dead staghorn branch
                    var br = ident.W(m, p);
                    Vector3 b0 = new Vector3(x, g + 0.03f, z);
                    for (int k = 0; k < 4; k++)
                    {
                        float a = RR(rng, 0f, Mathf.PI * 2f);
                        br.Rod(b0, b0 + new Vector3(Mathf.Cos(a) * 0.3f, RR(rng, 0.02f, 0.18f), Mathf.Sin(a) * 0.3f), 0.035f, 0.02f, 4);
                    }
                }
                else PropRocks.BuildSplit(ident, p, p, new Vector3(s * 1.4f, s * 0.6f, s), Quaternion.Euler(0f, RR(rng, 0f, 360f), 0f), rng.Next(), 0, 0.3f, m, null, null);
                n++;
            }
            // the one leaning palm, far out over the water on the side away from the antenna
            Vector3 away = -toSite;
            for (int t = 0; t < 40; t++)
            {
                float ang = Mathf.Atan2(away.z, away.x) + RR(rng, -0.9f, 0.9f);
                float rr = isle.ShoreRadius(ang) * RR(rng, 0.7f, 0.84f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                float g = isle.Height(x, z);
                if (g < 0.5f || spacing.Blocked(x, z)) continue;
                Vector3 lean = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var model = PropJungle.RandomPalm(rng, out var liteModel);
                Quaternion rot = Quaternion.LookRotation(lean) * Quaternion.Euler(0f, -90f, 0f) * Quaternion.Euler(0f, 0f, -24f);
                Vector3 bp = new Vector3(x, g - 0.15f, z);
                var trs = Matrix4x4.TRS(bp, rot, Vector3.one * 1.1f);
                full.Add(model, trs);
                lite.Add(liteModel, trs);
                PropSolid.Segment(cols, bp, bp + rot * Vector3.up * 3.2f + lean * 1.2f, 0.26f, "LeaningPalm");
                spacing.Add(x, z, 3f);
                PropInfo.SetSpot("trench_palm", bp);
                PropInfo.AddLandSpot(bp, LandSpotKind.Palm, 0.4f, isle.siteId);
                break;
            }
            // the atoll rim: white sandbars just breaking the surface in a ring offshore (boats pass, divers bump)
            var white = PropMats.Flat(LowPoly.Hex("#F4EEDC"));
            for (int k = 0; k < 14; k++)
            {
                float ang = k * Mathf.PI * 2f / 14f + RR(rng, -0.1f, 0.1f);
                Vector3 d = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                float shore = isle.ShoreRadius(ang);
                for (float r = shore + 4f; r < shore + 40f; r += 1f)
                {
                    float x = c.x + d.x * r, z = c.z + d.z * r;
                    float depth = -isle.Height(x, z);
                    if (depth < 1.0f) continue;
                    if (depth > 3.2f) break;
                    Vector3 along = new Vector3(-d.z, 0f, d.x);
                    float yaw = Mathf.Atan2(-along.z, along.x) * Mathf.Rad2Deg;
                    float len = RR(rng, 7f, 11f), wid = RR(rng, 2.2f, 3.4f);
                    Vector3 ctr = new Vector3(x, -0.03f, z);
                    WhiteBar(shallows, ctr, len, wid, yaw, 9500 + k, white);
                    PropSolid.Sandbar(cols, ctr, len, wid, yaw, 9500 + k, PropSolid.BoatPassLayer);
                    break;
                }
            }
        }

        /// A low white sandbar mound (as PropKit.Sandbar, own colour) just breaking the surface.
        static void WhiteBar(PropBatch b, Vector3 center, float len, float wid, float yaw, int seed, Material mat)
        {
            var mb = new PropMeshBuilder();
            var rng = new System.Random(seed);
            const int A = 12;
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            Vector3 top = center + Vector3.up * 0.18f;
            var ring = new Vector3[A];
            var ring2 = new Vector3[A];
            for (int i = 0; i < A; i++)
            {
                float a = i * Mathf.PI * 2f / A;
                float j = RR(rng, 0.85f, 1.1f);
                ring[i] = center + r * new Vector3(Mathf.Cos(a) * len * 0.5f * j, 0.08f, Mathf.Sin(a) * wid * 0.5f * j);
                ring2[i] = center + r * new Vector3(Mathf.Cos(a) * len * 0.75f * j, -1.2f, Mathf.Sin(a) * wid * 0.85f * j);
            }
            for (int i = 0; i < A; i++)
            {
                int k = (i + 1) % A;
                mb.Face3(top, ring[i], ring[k], Vector3.up);
                mb.QuadAuto(ring[i], ring[k], ring2[k], ring2[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, (ring[i] + ring[k]) * 0.5f - center);
            }
            b.B(mat, center).Append(mb, Matrix4x4.identity);
        }

        /// Fog Isle – mossy and misty: tree ferns, moss mounds, a low mist drifting round the island (the pink sand is
        /// the terrain's; the glowing plants round Toni's camp follow after the extras, FogGlowPlants).
        static void FogIdentity(Transform root, PropBatch ident, PropBatch full, PropBatch lite, Transform cols, Isle isle, PropExclusion spacing, System.Random rng)
        {
            Vector3 c = isle.c;
            for (int t = 0, n = 0; t < 180 && n < 8; t++)
            {
                Vector2 o = PropUtil.InDisc(rng, isle.R * 0.75f);
                float x = c.x + o.x, z = c.z + o.y;
                float g = isle.Height(x, z);
                if (g < 2f || spacing.Blocked(x, z)) continue;
                PropVolcanoPlants.AddTreeFern(full, lite, cols, rng, new Vector3(x, g, z), 0.9f);
                spacing.Add(x, z, 2.5f);
                n++;
            }
            var moss = ident.W(PropMats.Moss, c);
            var mossD = ident.W(PropMats.GrassDark, c);
            for (int t = 0, n = 0; t < 140 && n < 16; t++)
            {
                Vector2 o = PropUtil.InDisc(rng, isle.R * 0.8f);
                float x = c.x + o.x, z = c.z + o.y;
                float g = isle.Height(x, z);
                if (g < 1.2f || spacing.Blocked(x, z, -0.5f)) continue;
                float s = RR(rng, 0.35f, 0.8f);
                Quaternion q = Quaternion.Euler(0f, RR(rng, 0f, 360f), 0f);
                var radii = PropRocks.Jitter(rng, 0, 0.15f, 1, 0.6f);
                (n % 3 == 0 ? mossD : moss).Ico(new Vector3(x, g - s * 0.2f, z), new Vector3(s * 1.3f, s * 0.45f, s), q, 0, radii);
                n++;
            }
            PropVolcanoFX.CreateMist(root, new Vector3(c.x, 2.2f, c.z), isle.R * 1.25f);
        }

        /// Odd pink / violet glowing plants round Toni's camp and scattered over the Fog Isle (glow at night).
        static void FogGlowPlants(Transform root, PropBatch ident, Isle isle, PropExclusion spacing, System.Random rng, List<PropModel> temp)
        {
            Vector3 c = isle.c;
            Vector3 camp = PropInfo.Spot("toni_fog_camp", out var cp) ? cp : c;
            var pink = PropMats.EmissiveInstance(LowPoly.Hex("#E86FA0"), Color.black);
            pink.name = "FogGlow_Pink";
            var violet = PropMats.EmissiveInstance(LowPoly.Hex("#8E5CC2"), Color.black);
            violet.name = "FogGlow_Violet";
            var stem = PropMats.Flat(LowPoly.Hex("#6E5A8A"));
            var leaf = PropMats.Flat(LowPoly.Hex("#7A5C9E"));
            var models = new PropModel[4];
            for (int i = 0; i < models.Length; i++)
            {
                models[i] = PropVolcanoPlants.GlowPlant(9300 + i, i % 2 == 0 ? pink : violet, stem, leaf);
                temp.Add(models[i]);
            }
            for (int t = 0, n = 0; t < 300 && n < 26; t++)
            {
                bool nearCamp = n < 16;
                Vector2 o = nearCamp ? PropUtil.InDisc(rng, 9f) : PropUtil.InDisc(rng, isle.R * 0.85f);
                Vector3 b = nearCamp ? camp : c;
                float x = b.x + o.x, z = b.z + o.y;
                if (nearCamp && o.magnitude < 3.2f) continue;
                float g = isle.Height(x, z);
                if (g < 0.8f || spacing.Blocked(x, z, -0.6f)) continue;
                ident.Add(models[rng.Next(models.Length)], Matrix4x4.TRS(new Vector3(x, g - 0.03f, z), Quaternion.Euler(0f, RR(rng, 0f, 360f), 0f), Vector3.one * RR(rng, 0.9f, 1.5f)));
                n++;
            }
            var go = new GameObject("FogGlowPlants");
            go.transform.SetParent(root, false);
            go.transform.position = camp;
            PropNightLight.Attach(go, null, pink, new Color(2.4f, 0.6f, 1.5f), 0.08f, 0.45f, false);
            PropNightLight.Attach(go, null, violet, new Color(1.2f, 0.6f, 2.6f), 0.08f, 0.45f, false);
        }

        // ------------------------------------------------------------------ island mesh
        static void BuildTerrain(Transform root, Transform cols, Isle isle, bool volcanic)
        {
            var batch = new PropBatch(0f);
            var colB = new PropMeshBuilder();
            float[] ts = { 0f, 0.12f, 0.24f, 0.35f, 0.47f, 0.6f, 0.7f, 0.78f, 0.85f, 0.92f, 1.0f, 1.08f, 1.15f, 1.25f, 1.35f };
            const int A = 32;
            int nr = ts.Length;
            var v = new Vector3[nr, A];
            for (int k = 0; k < nr; k++)
            {
                for (int i = 0; i < A; i++)
                {
                    float ang = i * Mathf.PI * 2f / A + (k % 2) * (Mathf.PI / A);
                    float rad = ts[k] * isle.R * isle.Jitter(ang);
                    float x = isle.c.x + Mathf.Cos(ang) * rad, z = isle.c.z + Mathf.Sin(ang) * rad;
                    v[k, i] = new Vector3(x, isle.Height(x, z), z);
                }
            }
            Material grassA = volcanic ? PropMats.Volcanic : PropMats.Grass;
            Material grassB = volcanic ? PropMats.VolcanicDark : PropMats.GrassDark;
            void Tri(Vector3 a, Vector3 b, Vector3 cc, int hash)
            {
                Vector3 n = Vector3.Cross(b - a, cc - a);
                if (n.sqrMagnitude < 1e-10f) return;
                if (n.y < 0f) n = -n;
                n.Normalize();
                float h = (a.y + b.y + cc.y) / 3f;
                Material m;
                if (n.y < 0.62f) m = h > 0.3f ? (volcanic ? PropMats.VolcanicDark : PropMats.RockMid) : PropMats.RockDark;
                else if (h > 1.55f) m = (hash & 7) == 0 ? grassB : grassA;
                else if (h > 1.2f) m = (hash & 1) == 0 ? (volcanic ? PropMats.Volcanic : PropMats.Flat(PropPal.GrassDry)) : PropMats.Flat(PropPal.Sand);
                else if (h > 0.3f) m = PropMats.Flat(PropPal.Sand);
                else if (h > -0.7f) m = PropMats.Flat(PropPal.WetSand);
                else m = (hash & 15) == 0 ? PropMats.Flat(PropMats.Value(PropPal.Seabed, 0.93f)) : PropMats.Flat(PropPal.Seabed);
                batch.W(m, a).Face3(a, b, cc, n);
                colB.Face3(a, b, cc, n);
            }
            // center fan
            Vector3 ctr = new Vector3(isle.c.x, isle.Height(isle.c.x, isle.c.z), isle.c.z);
            for (int i = 0; i < A; i++) Tri(ctr, v[1, i], v[1, (i + 1) % A], i * 7);
            for (int k = 1; k < nr - 1; k++)
            {
                for (int i = 0; i < A; i++)
                {
                    int i1 = (i + 1) % A;
                    // rings are offset by half a segment every other ring: zig-zag triangles
                    if (k % 2 == 1)
                    {
                        // inner ring offset: inner_i lies between outer_i and outer_i+1
                        Tri(v[k, i], v[k + 1, i], v[k + 1, i1], k * 131 + i);
                        Tri(v[k, i], v[k + 1, i1], v[k, i1], k * 131 + i + 57);
                    }
                    else
                    {
                        // outer ring offset: outer_i lies between inner_i and inner_i+1
                        Tri(v[k, i], v[k + 1, i], v[k, i1], k * 131 + i);
                        Tri(v[k, i1], v[k + 1, i], v[k + 1, i1], k * 131 + i + 57);
                    }
                }
            }
            batch.Flush(root, "IslandMesh", ShadowCastingMode.On, 0f);
            PropCol.Mesh(cols, colB.ToMesh("MiniIslandCol", false), Vector3.zero, Quaternion.identity, Vector3.one, "IslandGround");

            // lumpy foam ring where the beach meets the sea
            var foam = new PropMeshBuilder();
            const int F = 48;
            for (int i = 0; i < F; i++)
            {
                float a0 = i * Mathf.PI * 2f / F, a1 = (i + 1) * Mathf.PI * 2f / F;
                float r0 = ShoreAt(isle, a0), r1 = ShoreAt(isle, a1);
                float w0 = 1.6f + 1.2f * PropNoise.Hash01(i, isle.seed, 9), w1 = 1.6f + 1.2f * PropNoise.Hash01((i + 1) % F, isle.seed, 9);
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 i0 = isle.c + d0 * (r0 - 0.5f) + Vector3.up * 0.06f, i1 = isle.c + d1 * (r1 - 0.5f) + Vector3.up * 0.06f;
                Vector3 o0 = isle.c + d0 * (r0 + w0) + Vector3.up * 0.04f, o1 = isle.c + d1 * (r1 + w1) + Vector3.up * 0.04f;
                foam.QuadAuto(i0, o0, o1, i1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
            }
            var fm = foam.ToMesh("MiniIslandFoam");
            var fr = PropUtil.MeshObject(root, "Foam", fm, PropMats.FallFoam, Vector3.zero, Quaternion.identity, false);
            fr.transform.position = Vector3.zero;
        }

        /// Radius where the island surface crosses sea level along an angle.
        static float ShoreAt(Isle isle, float ang)
        {
            float lo = isle.R * 0.6f, hi = isle.R * 1.3f;
            for (int i = 0; i < 18; i++)
            {
                float mid = (lo + hi) * 0.5f;
                float h = isle.Height(isle.c.x + Mathf.Cos(ang) * mid, isle.c.z + Mathf.Sin(ang) * mid);
                if (h > Waves.SeaLevel) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        // ------------------------------------------------------------------ landmarks
        /// Kelp island: a fishing hut on stilts over the water with a short jetty, nets and a buoy.
        static Vector3 StiltHut(Transform root, PropBatch batch, Transform cols, Isle isle, Vector3 toSite, PropExclusion spacing)
        {
            float ang = Mathf.Atan2(toSite.z, toSite.x);
            float shore = ShoreAt(isle, ang);
            Vector3 hp = isle.c + toSite * (shore + 1.5f);
            float floorY = 2.6f;
            float yaw = Mathf.Atan2(-toSite.x, -toSite.z) * Mathf.Rad2Deg;   // door faces the island
            var spec = PropShack.Default(3.4f, 3.6f, isle.seed);
            spec.windowLeft = true;
            spec.windowRight = true;
            Vector3 door = PropShack.Build(batch, cols, hp, yaw, floorY, spec, isle.Height);
            // long stilts down to the seabed
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 sp = hp + r * new Vector3(sx * 1.6f, 0f, sz * 1.7f);
                    float g = Mathf.Min(isle.Height(sp.x, sp.z), floorY - 0.5f);
                    batch.W(PropMats.WoodWet, sp).Cylinder(new Vector3(sp.x, g - 0.5f, sp.z), Quaternion.identity, 0.14f, 0.12f, floorY - g + 0.3f, 6);
                    PropCol.Capsule(cols, new Vector3(sp.x, (g + floorY) * 0.5f, sp.z), 0.15f, floorY - g, Quaternion.identity, "Stilt");
                }
            // jetty from the door back to the beach
            Vector3 jStart = door, jEnd = isle.c + toSite * (shore - 4.5f);
            jEnd.y = isle.Height(jEnd.x, jEnd.z);
            Vector3 jd = jEnd - jStart;
            jd.y = 0f;
            float jl = jd.magnitude;
            if (jl > 0.5f)
            {
                jd /= jl;
                Quaternion jr = Quaternion.LookRotation(jd);
                for (float s = 0f; s < jl; s += 0.3f)
                {
                    Vector3 p = jStart + jd * s;
                    float y = Mathf.Lerp(floorY, Mathf.Max(jEnd.y + 0.15f, 0.6f), s / jl);
                    batch.W(PropMats.Wood2, p).Box(new Vector3(p.x, y - 0.03f, p.z), new Vector3(1.2f, 0.06f, 0.26f), jr);
                }
                Vector3 a = new Vector3(jStart.x, floorY - 0.1f, jStart.z), b = new Vector3(jEnd.x, Mathf.Max(jEnd.y + 0.05f, 0.5f), jEnd.z);
                PropCol.Box(cols, (a + b) * 0.5f, new Vector3(1.2f, 0.12f, (b - a).magnitude), Quaternion.LookRotation((b - a).normalized), "Jetty");
            }
            // net drying on a frame and a buoy
            Vector3 net = hp + r * new Vector3(2.4f, 0f, 0.6f);
            var rope = batch.W(PropMats.Rope, net);
            for (int i = 0; i <= 5; i++)
            {
                rope.Rod(net + r * new Vector3(0f, floorY + 0.3f + i * 0.3f, -1f), net + r * new Vector3(0f, floorY + 0.3f + i * 0.3f, 1f), 0.012f, 0.012f, 3);
                rope.Rod(net + r * new Vector3(0f, floorY + 0.3f, -1f + i * 0.4f), net + r * new Vector3(0f, floorY + 1.8f, -1f + i * 0.4f), 0.012f, 0.012f, 3);
            }
            batch.W(PropMats.BuoyOrange, net).Ico(net + r * new Vector3(0.2f, floorY + 0.5f, 1.2f), Vector3.one * 0.22f, Quaternion.identity, 0);
            spacing.Add(hp.x, hp.z, 4.5f);
            spacing.AddLine(jStart, jEnd, 1.5f);
            return new Vector3(hp.x, floorY + 0.05f, hp.z);
        }

        /// Wreck island: a huge rusty anchor half buried in the beach and a warning sign.
        static Vector3 AnchorAndSign(PropBatch batch, Transform cols, Isle isle, Vector3 toSite, PropExclusion spacing)
        {
            float ang = Mathf.Atan2(toSite.z, toSite.x);
            float shore = ShoreAt(isle, ang);
            Vector3 p = isle.c + toSite * (shore - 4f);
            p.y = isle.Height(p.x, p.z);
            Quaternion r = Quaternion.LookRotation(toSite) * Quaternion.Euler(0f, 30f, -18f);
            var rust = batch.W(PropMats.Rust, p);
            var deep = batch.W(PropMats.RustDeep, p);
            Vector3 crown = p - Vector3.up * 0.4f;
            Vector3 top = crown + r * new Vector3(0f, 3.0f, 0f);
            rust.Beam(crown, top, 0.26f, 0.26f);
            rust.Beam(top + r * new Vector3(-0.8f, -0.3f, 0f), top + r * new Vector3(0.8f, -0.3f, 0f), 0.18f, 0.18f);
            rust.Torus(top + r * new Vector3(0f, 0.35f, 0f), r * Quaternion.Euler(0f, 0f, 90f), 0.3f, 0.07f, 8, 4);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 a = crown, b = crown + r * new Vector3(s * 1.0f, 0.5f, 0f), cc = crown + r * new Vector3(s * 1.4f, 1.3f, 0f);
                deep.Beam(a, b, 0.22f, 0.22f);
                deep.Beam(b, cc, 0.2f, 0.2f);
                Vector3 fl = cc + r * new Vector3(s * 0.05f, 0.25f, 0f);
                deep.Face3(cc + r * new Vector3(-0.25f * s, -0.35f, 0.12f), cc + r * new Vector3(0.35f * s, -0.15f, 0.12f), fl + r * new Vector3(0f, 0f, 0.12f), r * Vector3.forward);
                deep.Face3(cc + r * new Vector3(-0.25f * s, -0.35f, -0.12f), cc + r * new Vector3(0.35f * s, -0.15f, -0.12f), fl + r * new Vector3(0f, 0f, -0.12f), r * Vector3.back);
            }
            // a chain running into the sand
            Vector3 cp = top + r * new Vector3(0f, 0.35f, 0f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 n = cp + r * new Vector3(0.35f, -0.5f, 0.25f);
                rust.Torus((cp + n) * 0.5f, Quaternion.FromToRotation(Vector3.up, (n - cp).normalized) * Quaternion.Euler(0f, i * 90f, 90f), 0.14f, 0.04f, 6, 3);
                cp = n;
            }
            PropCol.Capsule(cols, (crown + top) * 0.5f, 0.35f, 3.4f, Quaternion.FromToRotation(Vector3.up, (top - crown).normalized), "Anchor");
            spacing.Add(p.x, p.z, 3.5f);

            // warning sign
            Vector3 sp = p - toSite * 5f + Vector3.Cross(Vector3.up, toSite) * 2f;
            sp.y = isle.Height(sp.x, sp.z);
            batch.W(PropMats.WoodDark, sp).Cylinder(sp - Vector3.up * 0.4f, Quaternion.identity, 0.07f, 0.06f, 2.4f, 6, false, true);
            Quaternion sr = Quaternion.LookRotation(toSite);
            Vector3 board = sp + Vector3.up * 1.7f + toSite * 0.06f;
            batch.W(PropMats.WoodDark, sp).Box(board - toSite * 0.03f, new Vector3(0.8f, 0.8f, 0.05f), sr);
            Vector3 u = sr * Vector3.right * 0.38f, vv = Vector3.up * 0.38f;
            batch.W(PropMats.SignIcon(PropIcon.Warning), sp).QuadAuto(board - u - vv, board - u + vv, board + u + vv, board + u - vv,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), toSite);
            PropCol.Capsule(cols, sp + Vector3.up * 1f, 0.1f, 2f, Quaternion.identity, "Sign");
            spacing.Add(sp.x, sp.z, 1.5f);
            Vector3 spot = p + toSite * 1.7f + Vector3.Cross(Vector3.up, toSite) * 0.6f;
            spot.y = isle.Height(spot.x, spot.z) + 0.02f;
            return spot;
        }

        /// Blue drop island: a tall rock pillar with a bird nest on top.
        static Vector3 Pillar(PropBatch batch, Transform cols, Isle isle, PropExclusion spacing, System.Random rng)
        {
            Vector3 p = isle.c + new Vector3(4f, 0f, -3f);
            p.y = isle.Height(p.x, p.z);
            float y = p.y - 1f;
            Material[] mats = { PropMats.Cliff, PropMats.Rock, PropMats.RockMid };
            float h = 0f;
            for (int i = 0; i < 4; i++)
            {
                float s = Mathf.Lerp(2.6f, 1.5f, i / 3f);
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 0.9f, 1.1f), s * 1.25f, s * PropUtil.Range(rng, 0.9f, 1.1f));
                Vector3 c = new Vector3(p.x + PropUtil.Range(rng, -0.3f, 0.3f), y + sc.y * 0.85f, p.z + PropUtil.Range(rng, -0.3f, 0.3f));
                PropPOI.BigRock(batch, cols, c, sc, Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), mats[i % 3], i == 3 ? PropMats.Moss : null);
                y += sc.y * 1.6f;
                h = c.y + sc.y * 0.9f;
            }
            // nest of twigs with eggs
            Vector3 nest = new Vector3(p.x, h, p.z);
            var tw = batch.W(PropMats.Flat(PropMats.Value(PropPal.WoodLight, 0.8f)), nest);
            for (int i = 0; i < 14; i++)
            {
                float a = i * 0.45f;
                Vector3 a0 = nest + new Vector3(Mathf.Cos(a) * 0.55f, 0.05f + (i % 3) * 0.06f, Mathf.Sin(a) * 0.55f);
                Vector3 a1 = nest + new Vector3(Mathf.Cos(a + 1.3f) * 0.5f, 0.1f + (i % 2) * 0.07f, Mathf.Sin(a + 1.3f) * 0.5f);
                tw.Rod(a0, a1, 0.03f, 0.025f, 3);
            }
            batch.W(PropMats.FlowerWhite, nest).Ico(nest + new Vector3(0.12f, 0.12f, 0.05f), new Vector3(0.08f, 0.1f, 0.08f), Quaternion.Euler(0f, 0f, 20f), 0);
            batch.W(PropMats.FlowerWhite, nest).Ico(nest + new Vector3(-0.1f, 0.12f, 0.1f), new Vector3(0.08f, 0.1f, 0.08f), Quaternion.Euler(0f, 0f, -15f), 0);
            spacing.Add(p.x, p.z, 4f);
            // a rope ladder up the pillar (climbable)
            Vector3 dir = new Vector3(-1f, 0f, 0.4f).normalized;
            Vector3 foot = p + dir * 2.9f;
            foot.y = isle.Height(foot.x, foot.z);
            var rope = batch.W(PropMats.Rope, foot);
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized * 0.25f;
            Vector3 topA = nest + dir * 0.9f + Vector3.up * 0.2f;
            rope.Rod(foot + side, topA + side, 0.02f, 0.02f, 3);
            rope.Rod(foot - side, topA - side, 0.02f, 0.02f, 3);
            for (float t = 0.05f; t < 1f; t += 0.06f)
                batch.W(PropMats.Wood2, foot).Rod(Vector3.Lerp(foot, topA, t) - side, Vector3.Lerp(foot, topA, t) + side, 0.025f, 0.025f, 4);
            var go = new GameObject("PillarLadder");
            go.transform.SetParent(cols, false);
            go.transform.position = (foot + topA) * 0.5f;
            go.transform.rotation = Quaternion.LookRotation(dir);
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(0.8f, (topA - foot).magnitude, 0.8f);
            var ladder = go.AddComponent<PropLadder>();
            ladder.bottomStand = foot + dir * 0.8f + Vector3.up * 0.05f;
            ladder.bottomYaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            ladder.topStand = nest + dir * 0.6f + Vector3.up * 0.3f;
            ladder.topYaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            ladder.foot = foot;
            ladder.proximityTop = true;
            ladder.proximityBottom = true;
            spacing.Add(foot.x, foot.z, 1.5f);
            return nest + Vector3.up * 0.15f;
        }

        /// Trench island: dark volcanic boulders and an old rusty antenna mast with a dish.
        static Vector3 Antenna(PropBatch batch, Transform cols, Isle isle, PropExclusion spacing, System.Random rng)
        {
            Vector3 p = isle.c + new Vector3(-2f, 0f, 2f);
            p.y = isle.Height(p.x, p.z);
            var leg = batch.W(PropMats.RustDeep, p);
            var br = batch.W(PropMats.Rust, p);
            const float H = 9f;
            for (int k = 0; k < 3; k++)
            {
                float a = k * 120f * Mathf.Deg2Rad, a2 = (k + 1) * 120f * Mathf.Deg2Rad;
                Vector3 b0 = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.0f - Vector3.up * 0.3f;
                Vector3 t0 = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.25f + Vector3.up * H;
                leg.Beam(b0, t0, 0.1f, 0.1f);
                for (float y = 1f; y < H; y += 1.5f)
                {
                    float r0 = Mathf.Lerp(1.0f, 0.25f, y / H), r1 = Mathf.Lerp(1.0f, 0.25f, Mathf.Min(H, y + 1.5f) / H);
                    Vector3 q0 = p + new Vector3(Mathf.Cos(a) * r0, y, Mathf.Sin(a) * r0);
                    Vector3 q1 = p + new Vector3(Mathf.Cos(a2) * r1, Mathf.Min(H, y + 1.5f), Mathf.Sin(a2) * r1);
                    Vector3 q2 = p + new Vector3(Mathf.Cos(a2) * r0, y, Mathf.Sin(a2) * r0);
                    br.Beam(q0, q2, 0.05f, 0.05f);
                    br.Beam(q0, q1, 0.04f, 0.04f);
                }
            }
            leg.Rod(p + Vector3.up * H, p + Vector3.up * (H + 2.5f), 0.05f, 0.03f, 4);
            Quaternion dr = Quaternion.Euler(-35f, 40f, 0f);
            var dm = batch.W(PropMats.Metal, p);
            Vector3 dish = p + Vector3.up * (H - 1.5f) + new Vector3(0.6f, 0f, 0.4f);
            dm.Cylinder(dish, dr * Quaternion.Euler(90f, 0f, 0f), 0.1f, 0.9f, 0.3f, 10, false, false);
            dm.Cylinder(dish + dr * Vector3.forward * 0.01f, dr * Quaternion.Euler(90f, 0f, 0f), 0.09f, 0.88f, 0.29f, 10, false, false, 1f, 1f, 0f, true);
            batch.W(PropMats.LighthouseRed, p).Ico(p + Vector3.up * (H + 2.6f), Vector3.one * 0.15f, Quaternion.identity, 0);
            PropCol.Capsule(cols, p + Vector3.up * (H * 0.5f), 0.8f, H, Quaternion.identity, "Antenna");
            spacing.Add(p.x, p.z, 3f);
            Vector3 spot = p + new Vector3(1.6f, 0f, -0.9f);
            spot.y = isle.Height(spot.x, spot.z) + 0.02f;
            return spot;
        }
    }
}
