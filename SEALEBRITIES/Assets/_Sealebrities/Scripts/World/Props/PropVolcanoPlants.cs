using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19 "Jede Insel sieht individuell aus"): the extra low-poly plant models of the volcano island and the
    /// individual landmark islands, in the How-to-Fish language (art bible §1.9: faceted, two-tone leaves, flat colours):
    ///  * tree fern – slender rough trunk with frond stubs and a crown of 8–10 arching leaflet fronds (+ curled fiddleheads),
    ///  * cycad – short thick scaly trunk with a rosette of stiff palm-like leaves and a cone,
    ///  * burnt snag – dead charcoal trunk with a broken top and stub branches,
    ///  * horsetail clump – thin segmented stalks with whorls and spore cones,
    ///  * mangrove – arching prop roots, short trunk, broad dark canopy (kelp island shore),
    ///  * glow plant – curling stalks with glowing bulbs (Fog Isle; the bulb material is passed in),
    ///  * sea lily (crinoid) – a segmented stalk with a cup of feathery arms (volcano shelf / deep drop-off),
    ///  * wind-bent conifer – PropPlants.Conifer variants the caller tilts (blue drop island).
    /// Every model is built once (fixed seeds) and instanced into merged batches; trees have a lite variant for the far
    /// LOD. Ensure() builds the kit lazily (the mini islands build first), Release() frees the meshes (end of PropVolcano).
    public static class PropVolcanoPlants
    {
        sealed class Kit
        {
            public readonly List<PropModel> treeFern = new List<PropModel>(), treeFernLite = new List<PropModel>();
            public readonly List<PropModel> cycad = new List<PropModel>(), cycadLite = new List<PropModel>();
            public readonly List<PropModel> snag = new List<PropModel>(), snagLite = new List<PropModel>();
            public readonly List<PropModel> horsetail = new List<PropModel>();
            public readonly List<PropModel> mangrove = new List<PropModel>(), mangroveLite = new List<PropModel>();
            public readonly List<PropModel> conifer = new List<PropModel>(), coniferLite = new List<PropModel>();
            public readonly List<PropModel> crinoid = new List<PropModel>();

            public IEnumerable<List<PropModel>> All()
            {
                yield return treeFern; yield return treeFernLite; yield return cycad; yield return cycadLite;
                yield return snag; yield return snagLite; yield return horsetail; yield return mangrove; yield return mangroveLite;
                yield return conifer; yield return coniferLite; yield return crinoid;
            }
        }

        static Kit kit;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { kit = null; }

        static float R(System.Random r, float a, float b) => PropUtil.Range(r, a, b);

        // ------------------------------------------------------------------ colours (art bible palette family)
        public static Material FernTrunk => PropMats.Flat(LowPoly.Hex("#4A3A34"));
        public static Material FernTrunkDark => PropMats.Flat(LowPoly.Hex("#3A2D2A"));
        public static Material CycadTrunk => PropMats.Flat(LowPoly.Hex("#6B5A45"));
        public static Material CycadScale => PropMats.Flat(LowPoly.Hex("#54463A"));
        public static Material CycadCone => PropMats.Flat(LowPoly.Hex("#B08A3E"));
        public static Material Charcoal => PropMats.Flat(LowPoly.Hex("#2F2B2C"));
        public static Material CharcoalLight => PropMats.Flat(LowPoly.Hex("#4A4446"));
        public static Material Horsetail => PropMats.Flat(LowPoly.Hex("#6F8F3A"));
        public static Material HorsetailNode => PropMats.Flat(LowPoly.Hex("#3E4A2A"));
        public static Material SporeCone => PropMats.Flat(LowPoly.Hex("#8A7046"));
        public static Material MangroveBark => PropMats.Flat(LowPoly.Hex("#5E4A44"));

        /// Builds every variant once (cheap, synchronous; safe to call repeatedly).
        public static void Ensure()
        {
            if (kit != null) return;
            var k = new Kit();
            for (int i = 0; i < 5; i++)
            {
                k.treeFern.Add(TreeFern(8100 + i * 7, false));
                k.treeFernLite.Add(TreeFern(8100 + i * 7, true));
            }
            for (int i = 0; i < 4; i++)
            {
                k.cycad.Add(Cycad(8200 + i * 5, false));
                k.cycadLite.Add(Cycad(8200 + i * 5, true));
            }
            for (int i = 0; i < 4; i++)
            {
                k.snag.Add(Snag(8300 + i * 3, false));
                k.snagLite.Add(Snag(8300 + i * 3, true));
            }
            for (int i = 0; i < 3; i++) k.horsetail.Add(HorsetailClump(8400 + i));
            for (int i = 0; i < 3; i++)
            {
                k.mangrove.Add(Mangrove(8500 + i * 11, false));
                k.mangroveLite.Add(Mangrove(8500 + i * 11, true));
            }
            for (int i = 0; i < 3; i++)
            {
                k.conifer.Add(PropPlants.Conifer(8600 + i * 13, false));
                k.coniferLite.Add(PropPlants.Conifer(8600 + i * 13, true));
            }
            Color[] lily = { LowPoly.Hex("#F2C94C"), LowPoly.Hex("#F28C4A"), LowPoly.Hex("#B59AD9"), LowPoly.Hex("#F4EFE6"), LowPoly.Hex("#D9455A") };
            for (int i = 0; i < lily.Length; i++) k.crinoid.Add(Crinoid(8700 + i, lily[i]));
            kit = k;
        }

        public static void Release()
        {
            if (kit == null) return;
            foreach (var l in kit.All())
                foreach (var m in l)
                    foreach (var p in m.parts)
                        if (p.mesh != null) Object.Destroy(p.mesh);
            kit = null;
        }

        // ------------------------------------------------------------------ placement helpers
        /// Tree fern (2.8–5.2 m) with an upright trunk collider.
        public static void AddTreeFern(PropBatch full, PropBatch lite, Transform cols, System.Random rng, Vector3 ground, float scale = 1f)
        {
            Ensure();
            int i = rng.Next(kit.treeFern.Count);
            float s = R(rng, 0.85f, 1.2f) * scale;
            var trs = Matrix4x4.TRS(ground - Vector3.up * 0.06f, Quaternion.Euler(R(rng, -4f, 4f), R(rng, 0f, 360f), R(rng, -4f, 4f)), Vector3.one * s);
            full.Add(kit.treeFern[i], trs);
            lite.Add(kit.treeFernLite[i], trs);
            if (cols != null) PropSolid.Upright(cols, ground - Vector3.up * 0.2f, 0.17f * s, kit.treeFern[i].height * 0.75f * s, false);
        }

        /// Cycad (1.6–3 m) with a short thick trunk collider.
        public static void AddCycad(PropBatch full, PropBatch lite, Transform cols, System.Random rng, Vector3 ground, float scale = 1f)
        {
            Ensure();
            int i = rng.Next(kit.cycad.Count);
            float s = R(rng, 0.85f, 1.2f) * scale;
            var trs = Matrix4x4.TRS(ground - Vector3.up * 0.05f, Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), Vector3.one * s);
            full.Add(kit.cycad[i], trs);
            lite.Add(kit.cycadLite[i], trs);
            if (cols != null) PropSolid.Upright(cols, ground - Vector3.up * 0.2f, 0.36f * s, 1.2f * s, false);
        }

        /// Burnt snag; returns the top (bird / dragonfly perch).
        public static Vector3 AddSnag(PropBatch full, PropBatch lite, Transform cols, System.Random rng, Vector3 ground, float scale = 1f)
        {
            Ensure();
            int i = rng.Next(kit.snag.Count);
            float s = R(rng, 0.8f, 1.15f) * scale;
            Quaternion q = Quaternion.Euler(R(rng, -7f, 7f), R(rng, 0f, 360f), R(rng, -7f, 7f));
            var trs = Matrix4x4.TRS(ground - Vector3.up * 0.1f, q, Vector3.one * s);
            full.Add(kit.snag[i], trs);
            lite.Add(kit.snagLite[i], trs);
            float h = kit.snag[i].height * s;
            if (cols != null) PropSolid.Segment(cols, ground - Vector3.up * 0.2f, ground + q * Vector3.up * (h * 0.85f), 0.2f * s, "Snag");
            return ground + q * Vector3.up * h;
        }

        public static void AddHorsetail(PropBatch batch, System.Random rng, Vector3 ground, float scale = 1f)
        {
            Ensure();
            float s = R(rng, 0.8f, 1.25f) * scale;
            batch.Add(kit.horsetail[rng.Next(kit.horsetail.Count)], Matrix4x4.TRS(ground - Vector3.up * 0.03f, Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), Vector3.one * s));
        }

        /// Mangrove on the shore line (roots reach below the water); trunk collider.
        public static void AddMangrove(PropBatch full, PropBatch lite, Transform cols, System.Random rng, Vector3 ground, float scale = 1f)
        {
            Ensure();
            int i = rng.Next(kit.mangrove.Count);
            float s = R(rng, 0.85f, 1.2f) * scale;
            var trs = Matrix4x4.TRS(ground, Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), Vector3.one * s);
            full.Add(kit.mangrove[i], trs);
            lite.Add(kit.mangroveLite[i], trs);
            if (cols != null) PropSolid.Upright(cols, ground - Vector3.up * 0.5f, 0.45f * s, 2.6f * s, false);
        }

        /// Wind-bent conifer (scaled down, leaning away from the wind) with a trunk collider along the lean.
        public static void AddBentConifer(PropBatch full, PropBatch lite, Transform cols, System.Random rng, Vector3 ground, Vector3 leanDir, float scale)
        {
            Ensure();
            int i = rng.Next(kit.conifer.Count);
            leanDir.y = 0f;
            if (leanDir.sqrMagnitude < 1e-4f) leanDir = Vector3.right;
            leanDir.Normalize();
            float lean = R(rng, 12f, 22f);
            Vector3 axis = Vector3.Cross(Vector3.up, leanDir);
            Quaternion q = Quaternion.AngleAxis(lean, axis) * Quaternion.Euler(0f, R(rng, 0f, 360f), 0f);
            // squash a little: wind-pruned crowns are lower and wider
            var trs = Matrix4x4.TRS(ground - Vector3.up * 0.1f, q, new Vector3(scale * 1.15f, scale * 0.8f, scale * 1.15f));
            full.Add(kit.conifer[i], trs);
            lite.Add(kit.coniferLite[i], trs);
            if (cols != null)
            {
                Vector3 top = ground + q * Vector3.up * (kit.conifer[i].height * scale * 0.8f * 0.6f);
                PropSolid.Segment(cols, ground - Vector3.up * 0.2f, top, 0.25f * scale + 0.08f, "Conifer");
            }
        }

        /// Sea lily on the seabed (passable – soft animal).
        public static void AddCrinoid(PropBatch batch, System.Random rng, Vector3 ground, float scale = 1f)
        {
            Ensure();
            float s = R(rng, 0.75f, 1.3f) * scale;
            batch.Add(kit.crinoid[rng.Next(kit.crinoid.Count)], Matrix4x4.TRS(ground - Vector3.up * 0.04f, Quaternion.Euler(R(rng, -8f, 8f), R(rng, 0f, 360f), R(rng, -8f, 8f)), Vector3.one * s));
        }

        // ------------------------------------------------------------------ models
        /// Tree fern: slender rough trunk (2.8–5.2 m, r 0.18 → 0.12) with frond-base knobs, crown of 8–10 arching
        /// leaflet fronds (1.8–2.6 m) and two curled fiddleheads; lite = 4-sided trunk + 6 simple fronds.
        public static PropModel TreeFern(int seed, bool lite)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = R(rng, 2.8f, 5.2f);
            Vector3 bend = new Vector3(R(rng, -0.25f, 0.25f), 0f, R(rng, -0.25f, 0.25f));
            Vector3 p0 = new Vector3(0f, -0.3f, 0f), p1 = new Vector3(bend.x * 0.4f, H * 0.5f, bend.z * 0.4f), p2 = new Vector3(bend.x, H, bend.z);
            var trunk = mb.B(FernTrunk);
            trunk.Rod(p0, p1, 0.2f, 0.16f, lite ? 4 : 6);
            trunk.Rod(p1, p2, 0.16f, 0.13f, lite ? 4 : 6);
            var top = mb.B(PropMats.Foliage);
            var under = mb.B(PropMats.PalmLeafDark);
            int n = 8 + rng.Next(3);
            if (!lite)
            {
                // rough trunk: old frond bases spiralling up
                var knob = mb.B(FernTrunkDark);
                for (float y = 0.4f; y < H - 0.2f; y += 0.32f)
                {
                    float a = y * 2.4f;
                    Vector3 c = Vector3.Lerp(p0, p2, (y + 0.3f) / (H + 0.3f));
                    knob.Octa(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.15f, new Vector3(0.07f, 0.1f, 0.07f));
                }
                trunk.Cylinder(p2 - Vector3.up * 0.05f, Quaternion.identity, 0.2f, 0.12f, 0.25f, 6, false, true);
            }
            float a0 = R(rng, 0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float az = a0 + i * 360f / n + R(rng, -12f, 12f);
                float len = R(rng, 1.8f, 2.6f) * Mathf.Lerp(0.85f, 1.1f, (H - 2.8f) / 2.4f);
                float e0 = R(rng, 38f, 56f), e1 = R(rng, -62f, -40f);
                if (lite)
                {
                    float r1 = az * Mathf.Deg2Rad;
                    Vector3 hor = new Vector3(Mathf.Cos(r1), 0f, Mathf.Sin(r1));
                    Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
                    Vector3 mid = p2 + hor * len * 0.5f + Vector3.up * len * 0.28f;
                    Vector3 tip = p2 + hor * len * 0.95f - Vector3.up * len * 0.15f;
                    PropPlants.TwoTone(top, under, p2, mid + lat * len * 0.14f, mid - lat * len * 0.14f);
                    PropPlants.TwoTone(top, under, mid + lat * len * 0.14f, tip, mid - lat * len * 0.14f);
                    continue;
                }
                PropPlants.LeafletFrond(top, under, p2 + Vector3.up * 0.1f, az, e0, e1, len, len * 0.13f, 7);
            }
            if (!lite)
            {
                // fiddleheads: two short young fronds curled up in the middle
                for (int i = 0; i < 2; i++)
                {
                    float az = a0 + 90f + i * 170f;
                    PropPlants.LeafletFrond(top, under, p2 + Vector3.up * 0.12f, az, 80f, 30f, 0.55f, 0.06f, 4);
                    float r1 = az * Mathf.Deg2Rad;
                    mb.B(PropMats.FoliageLight).Torus(p2 + new Vector3(Mathf.Cos(r1) * 0.12f, 0.62f, Mathf.Sin(r1) * 0.12f), Quaternion.Euler(0f, -az, 90f), 0.06f, 0.025f, 6, 3);
                }
            }
            return mb.Build("TreeFern" + seed + (lite ? "L" : ""), H + 0.6f, 2.4f);
        }

        /// Cycad: short thick scaly trunk (0.5–1.4 m, r 0.32–0.42) with 12–16 stiff, straight leaflet leaves and a cone.
        public static PropModel Cycad(int seed, bool lite)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float h = R(rng, 0.5f, 1.4f);
            float r0 = R(rng, 0.34f, 0.44f);
            var trunk = mb.B(CycadTrunk);
            trunk.Cylinder(new Vector3(0f, -0.25f, 0f), Quaternion.identity, r0, r0 * 0.85f, h + 0.25f, lite ? 5 : 7, false, true);
            if (!lite)
            {
                // scale pattern: offset rings of leaf-base bumps
                var sc = mb.B(CycadScale);
                int ring = 0;
                for (float y = 0.08f; y < h - 0.05f; y += 0.16f, ring++)
                {
                    float rr = Mathf.Lerp(r0, r0 * 0.85f, (y + 0.25f) / (h + 0.25f));
                    for (int k = 0; k < 7; k++)
                    {
                        float a = (k + (ring % 2) * 0.5f) * Mathf.PI * 2f / 7f;
                        sc.Octa(new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr), new Vector3(0.07f, 0.05f, 0.07f));
                    }
                }
            }
            var top = mb.B(PropMats.FoliageDark);
            var under = mb.B(PropMats.Conifer);
            int n = lite ? 8 : 12 + rng.Next(5);
            Vector3 crown = new Vector3(0f, h, 0f);
            float a0 = R(rng, 0f, 360f);
            for (int i = 0; i < n; i++)
            {
                float az = a0 + i * 360f / n + R(rng, -10f, 10f);
                float len = R(rng, 1.2f, 1.8f);
                float e0 = R(rng, 30f, 62f), e1 = e0 - R(rng, 18f, 34f);
                if (lite)
                {
                    float r1 = az * Mathf.Deg2Rad;
                    Vector3 hor = new Vector3(Mathf.Cos(r1), 0f, Mathf.Sin(r1));
                    Vector3 lat = new Vector3(hor.z, 0f, -hor.x);
                    float ee = (e0 + e1) * 0.5f * Mathf.Deg2Rad;
                    Vector3 tip = crown + (hor * Mathf.Cos(ee) + Vector3.up * Mathf.Sin(ee)) * len;
                    Vector3 mid = Vector3.Lerp(crown, tip, 0.4f);
                    PropPlants.TwoTone(top, under, crown, mid + lat * 0.16f, tip);
                    PropPlants.TwoTone(top, under, crown, tip, mid - lat * 0.16f);
                    continue;
                }
                PropPlants.LeafletFrond(top, under, crown, az, e0, e1, len, len * 0.11f, 6);
            }
            if (!lite && rng.NextDouble() < 0.6)
                mb.B(CycadCone).Cylinder(crown - Vector3.up * 0.05f, Quaternion.identity, 0.16f, 0.04f, 0.45f, 6, false, false);
            return mb.Build("Cycad" + seed + (lite ? "L" : ""), h + 1.5f, 1.6f);
        }

        /// Burnt snag: charcoal trunk 3–6.5 m with a slight bend, a jagged broken top and 2–3 stub branches.
        public static PropModel Snag(int seed, bool lite)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float H = R(rng, 3f, 6.5f);
            var b = mb.B(Charcoal);
            Vector3 p0 = new Vector3(0f, -0.4f, 0f), p1 = new Vector3(R(rng, -0.2f, 0.2f), H * 0.55f, R(rng, -0.2f, 0.2f)), p2 = new Vector3(R(rng, -0.4f, 0.4f), H, R(rng, -0.4f, 0.4f));
            b.Rod(p0, p1, 0.24f, 0.17f, lite ? 4 : 6);
            b.Rod(p1, p2, 0.17f, 0.09f, lite ? 4 : 6);
            int branches = 2 + rng.Next(2);
            var light = lite ? b : mb.B(CharcoalLight);
            for (int i = 0; i < branches; i++)
            {
                float t = R(rng, 0.35f, 0.8f);
                Vector3 s = Vector3.Lerp(p0, p2, t);
                float a = R(rng, 0f, Mathf.PI * 2f);
                float l = R(rng, 0.6f, 1.5f) * (1.1f - t * 0.5f);
                Vector3 e = s + new Vector3(Mathf.Cos(a) * l, l * R(rng, 0.4f, 0.9f), Mathf.Sin(a) * l);
                light.Rod(s, e, 0.07f, 0.025f, lite ? 3 : 4);
            }
            if (!lite)
            {
                // splintered top
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f + R(rng, -0.3f, 0.3f);
                    Vector3 o = new Vector3(Mathf.Cos(a) * 0.05f, 0f, Mathf.Sin(a) * 0.05f);
                    b.Cylinder(p2 + o - Vector3.up * 0.05f, Quaternion.Euler(R(rng, -12f, 12f), 0f, R(rng, -12f, 12f)), 0.06f, 0f, R(rng, 0.25f, 0.6f), 4);
                }
            }
            return mb.Build("Snag" + seed + (lite ? "L" : ""), H, 1.2f);
        }

        /// Horsetail clump: 10–16 thin segmented stalks (0.6–1.3 m) with node rings, a few whorls and spore cones.
        public static PropModel HorsetailClump(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var st = mb.B(Horsetail);
            var node = mb.B(HorsetailNode);
            var cone = mb.B(SporeCone);
            int n = 10 + rng.Next(7);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 0.28f);
                float l = R(rng, 0.6f, 1.3f);
                Vector3 b = new Vector3(o.x, -0.03f, o.y);
                Vector3 lean = new Vector3(o.x * 0.5f + R(rng, -0.08f, 0.08f), 1f, o.y * 0.5f + R(rng, -0.08f, 0.08f)).normalized;
                Vector3 t = b + lean * l;
                st.Rod(b, t, 0.02f, 0.012f, 4);
                maxH = Mathf.Max(maxH, t.y);
                int segs = Mathf.Max(2, Mathf.RoundToInt(l / 0.2f));
                for (int k = 1; k < segs; k++)
                {
                    Vector3 c = Vector3.Lerp(b, t, k / (float)segs);
                    node.Cylinder(c, Quaternion.FromToRotation(Vector3.up, lean), 0.024f, 0.022f, 0.025f, 4);
                    if (i < 3 && k % 2 == 1)
                        for (int w = 0; w < 4; w++)
                        {
                            float a = w * Mathf.PI * 0.5f + k;
                            st.Rod(c, c + new Vector3(Mathf.Cos(a) * 0.13f, 0.06f, Mathf.Sin(a) * 0.13f), 0.006f, 0.004f, 3);
                        }
                }
                if (rng.NextDouble() < 0.35) cone.Cylinder(t, Quaternion.FromToRotation(Vector3.up, lean), 0.03f, 0.008f, 0.09f, 5, true, false);
            }
            return mb.Build("Horsetail" + seed, maxH, 0.4f);
        }

        /// Mangrove: 6–9 arching prop roots from 0.9–1.6 m down into the water / sand, short trunk, flattened dark canopy.
        public static PropModel Mangrove(int seed, bool lite)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var bark = mb.B(MangroveBark);
            float H = R(rng, 2.8f, 3.8f);
            Vector3 j = new Vector3(0f, R(rng, 0.9f, 1.3f), 0f);
            bark.Rod(j, new Vector3(R(rng, -0.2f, 0.2f), H * 0.75f, R(rng, -0.2f, 0.2f)), 0.16f, 0.1f, lite ? 4 : 6);
            int roots = lite ? 4 : 6 + rng.Next(4);
            var pts = new Vector3[4];
            var rad = new float[4];
            for (int i = 0; i < roots; i++)
            {
                float a = i * Mathf.PI * 2f / roots + R(rng, -0.25f, 0.25f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float reach = R(rng, 1.2f, 2.2f);
                float y0 = j.y + R(rng, -0.1f, 0.5f);
                pts[0] = new Vector3(0f, y0, 0f) + d * 0.08f;
                pts[1] = d * reach * 0.45f + Vector3.up * (y0 + 0.3f);
                pts[2] = d * reach * 0.85f + Vector3.up * (y0 * 0.45f);
                pts[3] = d * reach + Vector3.up * -0.7f;
                if (lite)
                {
                    bark.Rod(pts[0], pts[1], 0.06f, 0.05f, 3);
                    bark.Rod(pts[1], pts[3], 0.05f, 0.04f, 3);
                    continue;
                }
                rad[0] = 0.07f; rad[1] = 0.06f; rad[2] = 0.05f; rad[3] = 0.045f;
                bark.Tube(pts, rad, 5);
            }
            Material[] greens = { PropMats.FoliageDark, PropMats.Foliage, PropMats.FoliageLight };
            int blobs = lite ? 2 : 3 + rng.Next(3);
            for (int i = 0; i < blobs; i++)
            {
                Vector2 o = i == 0 ? Vector2.zero : PropUtil.InDisc(rng, 1.3f);
                float s = R(rng, 1.1f, 1.7f);
                Vector3 c = new Vector3(o.x, H * R(rng, 0.78f, 0.95f), o.y);
                var radii = PropRocks.Jitter(rng, lite ? 0 : 1, 0.12f, 1, 0.7f);
                mb.B(greens[i == blobs - 1 ? 2 : i % 2]).Ico(c, new Vector3(s, s * 0.55f, s), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), lite ? 0 : 1, radii);
            }
            return mb.Build("Mangrove" + seed + (lite ? "L" : ""), H + 1f, 2.6f);
        }

        /// Fog Isle glow plant: 3–6 curling stalks with glowing bulbs (bulb material from the caller) over a rosette of
        /// dark violet leaves. Built per call (few instances) – the caller owns the meshes via its batch.
        public static PropModel GlowPlant(int seed, Material bulb, Material stem, Material leaf)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var sb = mb.B(stem);
            var bb = mb.B(bulb);
            var lt = mb.B(leaf);
            var lu = mb.B(PropMats.Flat(PropMats.Value(LowPoly.Hex("#5A3E6E"), 0.8f)));
            int n = 3 + rng.Next(4);
            var pts = new Vector3[4];
            var rad = new float[] { 0.03f, 0.025f, 0.02f, 0.016f };
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + R(rng, -0.3f, 0.3f);
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float h = R(rng, 0.45f, 0.95f);
                pts[0] = d * 0.05f;
                pts[1] = d * 0.12f + Vector3.up * h * 0.5f;
                pts[2] = d * 0.22f + Vector3.up * h * 0.92f;
                pts[3] = d * 0.36f + Vector3.up * h * 0.85f;
                sb.Tube(pts, rad, 4);
                bb.Ico(pts[3] + Vector3.down * 0.04f, new Vector3(0.07f, 0.09f, 0.07f) * R(rng, 0.9f, 1.4f), Quaternion.identity, 0);
                maxH = Mathf.Max(maxH, h);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + R(rng, -20f, 20f)) * Mathf.Deg2Rad;
                PropPlants.BroadLeaf(lt, lu, new Vector3(0f, 0.02f, 0f), new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)), R(rng, 0.3f, 0.45f), R(rng, 0.16f, 0.24f), 25f, 50f, false);
            }
            return mb.Build("GlowPlant" + seed, maxH, 0.5f);
        }

        /// Sea lily (crinoid): segmented stalk 0.5–1.1 m with a holdfast and a cup of 8–10 feathery arms.
        public static PropModel Crinoid(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var stalk = mb.B(PropMats.Flat(PropMats.Value(c, 0.62f)));
            float h = R(rng, 0.55f, 1.1f);
            Vector3 top = new Vector3(R(rng, -0.08f, 0.08f), h, R(rng, -0.08f, 0.08f));
            Vector3 mid = new Vector3(top.x * 0.3f, h * 0.5f, top.z * 0.3f);
            stalk.Rod(Vector3.zero, mid, 0.03f, 0.026f, 5);
            stalk.Rod(mid, top, 0.026f, 0.022f, 5);
            for (int k = 1; k < 6; k++) stalk.Cylinder(Vector3.Lerp(Vector3.zero, top, k / 6f), Quaternion.identity, 0.034f, 0.034f, 0.02f, 5);
            stalk.Cylinder(Vector3.zero, Quaternion.identity, 0.12f, 0.03f, 0.08f, 6, false, false);
            var armTop = mb.B(PropMats.Flat(c));
            var armUnder = mb.B(PropMats.Flat(PropMats.Value(c, 0.78f)));
            mb.B(PropMats.Flat(PropMats.Value(c, 0.7f))).Cylinder(top - Vector3.up * 0.04f, Quaternion.identity, 0.04f, 0.08f, 0.08f, 6, false, true);
            int arms = 8 + rng.Next(3);
            for (int i = 0; i < arms; i++)
            {
                float az = i * 360f / arms + R(rng, -10f, 10f);
                float len = R(rng, 0.22f, 0.38f);
                PropPlants.LeafletFrond(armTop, armUnder, top + Vector3.up * 0.03f, az, R(rng, 55f, 75f), R(rng, -15f, 20f), len, 0.05f, 5);
            }
            return mb.Build("Crinoid" + seed, h + 0.3f, 0.5f);
        }
    }
}
