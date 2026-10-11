using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Small landmark islands next to the outer dive sites (GDD 15.34): palms, rocks, beach props and one landmark each
    /// – kelp: stilt fishing hut · wreck: rusty anchor + warning sign · blue drop: tall rock pillar with a bird nest ·
    /// trench: dark volcanic rocks with an old antenna. When the world terrain has no island at WorldLayout's position
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
            bool volcanic = isle.siteId == "deep_trench";
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
                default: lm = Antenna(batch, cols, isle, spacing, rng); break;
            }
            PropInfo.MiniLandmarks[isle.siteId] = lm;

            // ---- palms (on sand / grass), leaning outward
            int palms = volcanic ? 4 : 9 + rng.Next(4);
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
            if (!volcanic || !isle.own)
            {
                for (int i = 0, a = 0; i < 6 && a < 200; a++)
                {
                    Vector2 o = PropUtil.InDisc(rng, isle.R * 0.5f);
                    float x = c.x + o.x, z = c.z + o.y;
                    float g = isle.Height(x, z);
                    if (g < 1.6f || spacing.Blocked(x, z)) continue;
                    PropJungle.AddPlant(under, under, rng, new Vector3(x, g, z), rng.NextDouble() < 0.4 ? 1 : 0);
                    spacing.Add(x, z, 1.2f);
                    i++;
                }
                if (grass != null)
                {
                    for (float gx = -isle.R; gx <= isle.R; gx += 1.3f)
                        for (float gz = -isle.R; gz <= isle.R; gz += 1.3f)
                        {
                            float x = c.x + gx + PropUtil.Range(rng, -0.5f, 0.5f), z = c.z + gz + PropUtil.Range(rng, -0.5f, 0.5f);
                            float g = isle.Height(x, z);
                            if (g < 1.4f || spacing.Blocked(x, z, -0.6f)) continue;
                            bool top = g > 1.9f;
                            if (!top && rng.NextDouble() > 0.15) continue;
                            int v = top ? rng.Next(PropGrassField.MeadowCount) : PropGrassField.DuneFirst + rng.Next(PropGrassField.DuneCount);
                            grass.Add(v, new Vector3(x, g - 0.03f, z), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0.85f, 1.2f), Vector3.zero);
                        }
                }
            }
            // ---- rocks along the shore
            int rocks = volcanic ? 16 : 7;
            for (int i = 0; i < rocks; i++)
            {
                float ang = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float rr = isle.ShoreRadius(ang) * PropUtil.Range(rng, volcanic ? 0.2f : 0.85f, 1.08f);
                float x = c.x + Mathf.Cos(ang) * rr, z = c.z + Mathf.Sin(ang) * rr;
                if (spacing.Blocked(x, z)) continue;
                float g = isle.Height(x, z);
                float s = PropUtil.Range(rng, 0.7f, volcanic ? 2.4f : 1.8f);
                Vector3 sc = new Vector3(s * 1.3f, s * (volcanic ? 0.9f : 0.6f), s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -8f, 8f), PropUtil.Range(rng, 0f, 360f), 0f);
                int seed = rng.Next();
                Material main = volcanic ? (rng.NextDouble() < 0.5 ? PropMats.Volcanic : PropMats.VolcanicDark) : PropMats.BeachRock;
                PropRocks.BuildSplit(batch, p, p, sc, q, seed, 1, volcanic ? 0.22f : 0.14f, main, volcanic ? null : PropMats.Moss, PropMats.Crevice);
                if (s > 1.1f) PropCol.Mesh(cols, PropRocks.ColliderMesh(sc, seed, 1, volcanic ? 0.22f : 0.14f), p, q, Vector3.one, "Rock");
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

            batch.Flush(root, "IslandProps", ShadowCastingMode.On, 900f);
            under.Flush(root, "IslandPlants", ShadowCastingMode.On, 120f);
            PropBatch.FlushLod(full, lite, root, "IslandPalms", ShadowCastingMode.On, PropJungle.TreeNear, 2400f);
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
