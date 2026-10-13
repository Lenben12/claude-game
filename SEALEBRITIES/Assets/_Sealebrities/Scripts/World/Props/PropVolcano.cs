using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round 3 (GDD §19 "Jede Insel sieht individuell aus, z. B. eine Vulkaninsel nur mit Urzeit-Tieren"): props of the
    /// volcano island (WorldLayout.VolcanoIsland – terrain by WorldHeightField.Volcano). Called by IslandProps after the
    /// mini islands (the shared PropJungle kit is still alive). Everything is deterministic (fixed seeds, terrain heights).
    ///  * crater: a glowing lava lake with crust plates and floor cracks, jagged rim blocks, an orange point light at night,
    ///  * one big smoke plume (single particle system, few huge particles, drifts with the trade wind, darker at night with
    ///    an orange underglow – PropVolcanoFX) + steam vents on the flank and where the lava tongue meets the sea,
    ///  * the lava tongue (ENE): glowing cracks down to the water, basalt rubble,
    ///  * black-sand beach (west, facing the main island): lava boulders, pumice, driftwood, a few wind-bent palms – wild,
    ///    no civilisation; a mooring buoy off the beach,
    ///  * slopes: dense ferns, tree ferns, cycads, horsetails behind the beach, burnt snags higher up, bare ash and lava
    ///    rocks on the upper cone; a fossil bed (ammonites + big bones in a layered rock face),
    ///  * under water: lava-rock reef with sea lilies on the black-sand shelf (4–20 m), sea-lily gardens on the steep east
    ///    drop-off (40–80 m) and a lava-tube overhang of rock arches (~46–59 m) to swim under,
    ///  * spawn anchors for the fauna step (PropInfo spots volcano_* + LandSpots tagged "volcano" + habitats zone
    ///    "volcano_isle").
    /// Colliders on every solid prop (rocks ≥ 0.35 m, trunks, logs, the fossil wall); ferns / horsetails / sea lilies stay
    /// passable. The cone silhouette is the terrain; rim blocks, palms / tree ferns (lite LOD) and the smoke read from the
    /// dock (≈ 580 m).
    public static class PropVolcano
    {
        public const string Area = "volcano";
        public const string Zone = "volcano_isle";
        /// Under-water decor is only active while the camera is within this horizontal distance of the island.
        public const float UnderwaterRange = 330f;

        static Vector3 C => WorldLayout.VolcanoIsland;
        static float G(float x, float z) => PropGround.H(x, z);
        static float R(System.Random r, float a, float b) => PropUtil.Range(r, a, b);

        static Vector3 Dir(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(r), 0f, Mathf.Sin(r));
        }

        static Vector3 Polar(float deg, float r)
        {
            Vector3 d = Dir(deg);
            float x = C.x + d.x * r, z = C.z + d.z * r;
            return new Vector3(x, G(x, z), z);
        }

        static float DegOf(Vector3 d) => Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        static float AngleAt(float x, float z) => Mathf.Atan2(z - C.z, x - C.x) * Mathf.Rad2Deg;
        static float RadiusAt(float x, float z) => Mathf.Sqrt((x - C.x) * (x - C.x) + (z - C.z) * (z - C.z));
        static float YawTo(Vector3 from, Vector3 to) => Mathf.Atan2(to.x - from.x, to.z - from.z) * Mathf.Rad2Deg;

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// Radius where the ground crosses sea level along an angle (marching outward from the cone).
        static float ShoreR(float deg)
        {
            Vector3 d = Dir(deg);
            float r = 30f;
            for (; r < 120f; r += 0.75f)
                if (G(C.x + d.x * r, C.z + d.z * r) < 0f) break;
            return r;
        }

        /// Reserves a spot (radius r) and a 3 m wide strip from it straight out to the water.
        static void KeepFree(PropExclusion spacing, Vector3 p, float r)
        {
            spacing.Add(p.x, p.z, r);
            Vector3 sea = Flat(p - C);
            spacing.AddLine(p, p + sea * 16f, 1.5f);
        }

        /// First seabed point along an angle (outside minR) whose depth reaches `depth`; false when deeper than maxDepth.
        static bool DepthPoint(float deg, float depth, float maxDepth, float minR, out Vector3 p)
        {
            Vector3 d = Dir(deg);
            for (float r = minR; r < 170f; r += 0.5f)
            {
                float x = C.x + d.x * r, z = C.z + d.z * r;
                float g = G(x, z);
                if (-g < depth) continue;
                if (-g > maxDepth) break;
                p = new Vector3(x, g, z);
                return true;
            }
            p = Vector3.zero;
            return false;
        }

        // ------------------------------------------------------------------ materials (one instance per build)
        static Material lavaMat, crackMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lavaMat = null; crackMat = null; }

        static readonly Color LavaAlbedo = new Color(1f, 0.42f, 0.1f);
        public static readonly Color LavaEmission = new Color(2.6f, 0.85f, 0.16f);
        public static readonly Color CrackEmission = new Color(2.2f, 0.6f, 0.1f);
        static Material Tuff => PropMats.Flat(LowPoly.Hex("#8A7F72"));
        static Material TuffLight => PropMats.Flat(LowPoly.Hex("#9C9180"));
        static Material TuffDark => PropMats.Flat(LowPoly.Hex("#6E655C"));
        static Material Pumice => PropMats.Flat(LowPoly.Hex("#BDB6A6"));
        static Material PumiceDark => PropMats.Flat(LowPoly.Hex("#A39B8A"));
        static Material Sulfur => PropMats.Flat(LowPoly.Hex("#D9C24A"));
        static Material Fossil => PropMats.Flat(LowPoly.Hex("#D8CDB4"));
        static Material FossilDark => PropMats.Flat(LowPoly.Hex("#B5A68A"));

        // ================================================================== build
        public static IEnumerator Build(Transform island, PropGrassField grass)
        {
            PropVolcanoPlants.Ensure();
            var root = PropUtil.WorldRoot("VolcanoIsland", island);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(root, false);
            var treeCols = new GameObject("TrunkColliders").transform;
            treeCols.SetParent(root, false);
            var fx = root.gameObject.AddComponent<PropVolcanoFX>();
            fx.center = C;
            lavaMat = PropMats.EmissiveInstance(LavaAlbedo, LavaEmission);
            lavaMat.name = "Volcano_Lava";
            crackMat = PropMats.EmissiveInstance(new Color(0.85f, 0.3f, 0.08f), CrackEmission);
            crackMat.name = "Volcano_Cracks";
            fx.AddLava(lavaMat, LavaEmission);
            fx.AddLava(crackMat, CrackEmission);

            var rng = new System.Random(7001);
            var spacing = new PropExclusion(8f);
            var far = new PropBatch(64f);          // rim blocks + big boulders (seen from the dock)
            var near = new PropBatch(48f);         // rocks, pumice, driftwood, fossils, crust
            var full = new PropBatch(64f);         // palms, tree ferns, cycads, snags (LOD0)
            var lite = new PropBatch(64f);         // … their lite silhouettes (LOD1, up to 2.4 km)
            var under = new PropBatch(48f);        // ground ferns, horsetails, bushes
            var glow = new PropBatch(0f);          // lava (emissive)
            var palmCols = new GameObject("PalmColliders");
            palmCols.transform.SetParent(root, false);
            palmCols.transform.position = Vector3.zero;

            float beachDeg = DegOf(WorldFeatures.VolcanoBeachDir);
            float lavaDeg = DegOf(WorldFeatures.VolcanoLavaDir);
            float craterY = G(C.x, C.z);

            // ---- named spots + kept-free areas (landing, mooring lane)
            Vector3 beach = WorldFeatures.VolcanoBeach;
            beach.y = G(beach.x, beach.z);
            PropInfo.SetSpot("volcano_beach", beach, YawTo(beach, C));
            spacing.Add(beach.x, beach.z, 3.5f);
            Vector3 moor = Vector3.zero;
            {
                Vector3 bd = Dir(beachDeg);
                for (float r = ShoreR(beachDeg) + 12f; r < 140f; r += 1f)
                {
                    float x = C.x + bd.x * r, z = C.z + bd.z * r;
                    if (-G(x, z) >= 3.4f) { moor = new Vector3(x, 0f, z); break; }
                }
                if (moor == Vector3.zero) moor = new Vector3(C.x + bd.x * 78f, 0f, C.z + bd.z * 78f);
                PropKit.MooringBuoy(root, moor, PropMats.BuoyOrange, 0.8f);
                PropInfo.SetSpot("volcano_mooring", moor, YawTo(moor, C));
                spacing.Add(moor.x, moor.z, 12f);
                spacing.AddLine(beach, moor, 5f);
            }
            // gameplay spots PropSurvival places on the black beach after us (treasure X of the map pieces, the campfire
            // spot, daily driftwood + lava whetstones): keep them free and reachable from the water
            KeepFree(spacing, new Vector3(653.6f, 0f, 124.3f), 3.5f);
            KeepFree(spacing, new Vector3(658.5f, 0f, 107.9f), 3f);
            KeepFree(spacing, new Vector3(652.6f, 0f, 118f), 2f);
            KeepFree(spacing, new Vector3(651.4f, 0f, 125f), 2f);
            for (int i = 0; i <= 4; i++) KeepFree(spacing, Vector3.Lerp(new Vector3(655.4f, 0f, 102f), new Vector3(656.8f, 0f, 94f), i / 4f), 1.8f);
            Vector3 rimSpot = Polar(beachDeg, WorldFeatures.CraterRimRadius + 0.6f);
            PropInfo.SetSpot("volcano_crater", rimSpot + Vector3.up * 0.05f, YawTo(rimSpot, C));
            PropInfo.SetSpot("volcano_crater_floor", new Vector3(C.x, craterY, C.z));
            spacing.Add(rimSpot.x, rimSpot.z, 2.5f);
            if (PropRun.Due()) yield return null;

            // ---- crater, lava tongue, smoke, steam
            Crater(root, glow, near, far, cols, fx, rng, craterY);
            if (PropRun.Due()) yield return null;
            LavaTongue(root, glow, near, cols, fx, rng, lavaDeg, spacing);
            if (PropRun.Due()) yield return null;
            fx.smoke = PropVolcanoFX.CreateSmoke(root, new Vector3(C.x, craterY + 1.2f, C.z));
            FlankVents(root, near, cols, fx, rng, beachDeg, lavaDeg, spacing);
            if (PropRun.Due()) yield return null;

            // ---- the lone dead palm stump inland of the treasure X ("the X at the foot of the dead palm")
            {
                Vector3 x = new Vector3(653.6f, 0f, 124.3f);
                Vector3 sp = x + Flat(C - x) * 2.6f;
                sp.y = G(sp.x, sp.z);
                var trunk = near.W(PropMats.PalmTrunkDark, sp);
                Quaternion q = Quaternion.Euler(-9f, 70f, 6f);
                trunk.Cylinder(sp - Vector3.up * 0.3f, q, 0.22f, 0.15f, 4.6f, 7, false, false);
                Vector3 tip = sp + q * Vector3.up * 4.3f;
                for (int i = 0; i < 3; i++)
                    near.W(PropMats.PalmLeafDry, tip).Tri2(tip, tip + new Vector3(Mathf.Cos(i * 2.1f) * 1.4f, -1.6f, Mathf.Sin(i * 2.1f) * 1.4f),
                        tip + new Vector3(Mathf.Cos(i * 2.1f + 0.3f) * 1.1f, -1.9f, Mathf.Sin(i * 2.1f + 0.3f) * 1.1f), Vector3.up);
                PropSolid.Segment(cols, sp - Vector3.up * 0.2f, tip, 0.22f, "DeadPalm");
                PropInfo.SetSpot("volcano_dead_palm", sp);
            }

            // ---- black-sand beach (wild: boulders, pumice, driftwood, a few bent palms)
            Beach(root, near, far, full, lite, palmCols, cols, rng, beachDeg, spacing);
            if (PropRun.Due()) yield return null;
            RockyShore(near, far, cols, rng, beachDeg, lavaDeg, spacing);
            if (PropRun.Due()) yield return null;
            Vector3 fossil = FossilBed(near, cols, rng, beachDeg - 30f, spacing);
            if (PropRun.Due()) yield return null;

            // ---- slopes: ferns, tree ferns, cycads, snags, horsetails, ash + lava rocks, grass
            var meadows = new List<Vector3>();
            var perches = new List<Vector3>();
            var bask = new List<Vector4>();
            yield return Slopes(near, far, full, lite, under, treeCols, cols, grass, rng, beachDeg, lavaDeg, spacing, meadows, perches, bask);

            // ---- land spawn anchors (fauna step: horseshoe crabs, tuatara-like reptiles, giant dragonflies …)
            LandAnchors(beachDeg, beach, fossil, meadows, perches, bask, spacing);

            // ---- flush the island side
            far.Flush(root, "VolcanoRocksFar", ShadowCastingMode.On, 1400f);
            near.Flush(root, "VolcanoDetail", ShadowCastingMode.On, 170f);
            under.Flush(root, "VolcanoUnderstory", ShadowCastingMode.On, 130f);
            PropBatch.FlushLod(full, lite, root, "VolcanoTrees", ShadowCastingMode.On, PropJungle.TreeNear, 2400f);
            glow.Flush(root, "VolcanoLava", ShadowCastingMode.Off, 2000f, false);
            if (PropRun.Due()) yield return null;

            // ---- under water: lava-rock reef on the shelf, the east drop-off, the lava-tube arches
            yield return Underwater(root, rng, beachDeg, lavaDeg, spacing);

            PropVolcanoPlants.Release();
        }

        // ================================================================== crater
        static void Crater(Transform root, PropBatch glow, PropBatch near, PropBatch far, Transform cols, PropVolcanoFX fx, System.Random rng, float craterY)
        {
            // lava lake: an irregular polygon a little above the highest floor point, a dark crust skirt down to the floor
            const int N = 14;
            var ring = new Vector3[N];
            Vector3 lc = new Vector3(C.x + 0.5f, 0f, C.z - 0.4f);
            float top = -999f;
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2f / N;
                float rr = R(rng, 3.4f, 4.6f);
                ring[i] = new Vector3(lc.x + Mathf.Cos(a) * rr, 0f, lc.z + Mathf.Sin(a) * rr);
                top = Mathf.Max(top, G(ring[i].x, ring[i].z));
            }
            top = Mathf.Max(top, G(lc.x, lc.z)) + 0.08f;
            Vector3 cen = new Vector3(lc.x, top, lc.z);
            var lb = glow.W(lavaMat, cen);
            var crust = near.W(PropMats.VolcanicDark, cen);
            for (int i = 0; i < N; i++)
            {
                Vector3 a = ring[i], b = ring[(i + 1) % N];
                a.y = top; b.y = top;
                lb.Face3(cen, a, b, Vector3.up);
                Vector3 ad = new Vector3(a.x, G(a.x, a.z) - 0.2f, a.z), bd = new Vector3(b.x, G(b.x, b.z) - 0.2f, b.z);
                Vector3 outN = Flat((a + b) * 0.5f - cen);
                Vector3 a2 = a + outN * 0.35f + Vector3.up * 0.12f, b2 = b + outN * 0.35f + Vector3.up * 0.12f;
                crust.QuadAuto(a, a2, b2, b, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                crust.QuadAuto(a2, ad + outN * 0.5f, bd + outN * 0.5f, b2, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outN);
            }
            // floating crust plates
            for (int i = 0; i < 6; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, 2.6f);
                float pr = R(rng, 0.45f, 0.95f);
                crust.Cylinder(new Vector3(lc.x + o.x, top - 0.04f, lc.z + o.y), Quaternion.Euler(R(rng, -3f, 3f), R(rng, 0f, 60f), R(rng, -3f, 3f)), pr, pr * 0.9f, 0.13f, 6, false, true);
            }
            // glowing cracks up the crater bowl
            var cb = glow.W(crackMat, cen);
            for (int i = 0; i < 7; i++)
            {
                float a = i * 51f + R(rng, -12f, 12f);
                Vector3 d = Dir(a);
                Vector3 s = new Vector3(lc.x + d.x * 3.6f, 0f, lc.z + d.z * 3.6f);
                Vector3 e = new Vector3(C.x + d.x * R(rng, 6.5f, 8.5f), 0f, C.z + d.z * R(rng, 6.5f, 8.5f));
                Crack(cb, s, e, R(rng, 0.18f, 0.32f), 0.05f, rng);
            }
            // jagged rim blocks (far batch: they break the cone's silhouette from the dock)
            for (int i = 0; i < 11; i++)
            {
                float a = i * 360f / 11f + R(rng, -10f, 10f);
                float rr = WorldFeatures.CraterRimRadius + R(rng, -0.6f, 1.6f);
                Vector3 p = Polar(a, rr);
                float s = R(rng, 0.9f, 2.0f);
                Vector3 sc = new Vector3(s * R(rng, 0.8f, 1.3f), s * R(rng, 0.9f, 1.5f), s);
                p.y -= sc.y * 0.35f;
                Quaternion q = Quaternion.Euler(R(rng, -14f, 14f), R(rng, 0f, 360f), R(rng, -14f, 14f));
                int seed = rng.Next();
                PropRocks.BuildSplit(far, p, p, sc, q, seed, 1, 0.22f, PropMats.VolcanicDark, PropMats.Volcanic, PropMats.Crevice);
                PropSolid.Rock(cols, p, sc, q, seed, 1, 0.22f, false, "VolcanoRock");
            }
            // the night glow
            var light = PropUtil.AddPointLight(root, new Vector3(C.x, craterY + 3.5f, C.z), new Color(1f, 0.48f, 0.18f), 32f, 4.5f, "CraterGlow");
            fx.AddLight(light);
        }

        /// A glowing ribbon following the ground from a to b (subdivided ~1.2 m, wobbling sideways).
        static void Crack(PropMeshBuilder b, Vector3 a, Vector3 c, float w, float lift, System.Random rng)
        {
            Vector3 d = c - a;
            d.y = 0f;
            float len = d.magnitude;
            if (len < 0.3f) return;
            Vector3 dir = d / len;
            Vector3 lat = new Vector3(dir.z, 0f, -dir.x);
            int n = Mathf.Max(2, Mathf.CeilToInt(len / 1.2f));
            Vector3 prevL = default, prevR = default;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 p = a + dir * (len * t) + lat * (i == 0 || i == n ? 0f : R(rng, -0.35f, 0.35f));
                float ww = w * (i == n ? 0.15f : Mathf.Lerp(1f, 0.55f, t)) * R(rng, 0.75f, 1.2f);
                Vector3 l = p - lat * ww, r = p + lat * ww;
                l.y = G(l.x, l.z) + lift;
                r.y = G(r.x, r.z) + lift;
                if (i > 0) b.QuadAuto(prevL, l, r, prevR, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector3.up);
                prevL = l;
                prevR = r;
            }
        }

        // ================================================================== lava tongue
        static void LavaTongue(Transform root, PropBatch glow, PropBatch near, Transform cols, PropVolcanoFX fx, System.Random rng, float lavaDeg, PropExclusion spacing)
        {
            var cb = glow.W(crackMat, C);
            float[] offs = { -11f, -4f, 3f, 10f };
            for (int k = 0; k < offs.Length; k++)
            {
                float a0 = lavaDeg + offs[k];
                float shore = ShoreR(a0);
                float r = R(rng, 13f, 17f);
                float a = a0;
                Vector3 prev = Polar(a, r);
                while (r < shore + 1.5f)
                {
                    float step = R(rng, 2.5f, 4f);
                    r += step;
                    a += R(rng, -2.2f, 2.2f) + (a0 - a) * 0.25f;
                    Vector3 p = Polar(a, Mathf.Min(r, shore + 1.5f));
                    Crack(cb, prev, p, Mathf.Lerp(0.22f, 0.5f, r / shore), 0.06f, rng);
                    // short side branches
                    if (rng.NextDouble() < 0.3)
                    {
                        Vector3 side = Polar(a + R(rng, -9f, 9f), r + R(rng, 2f, 4f));
                        Crack(cb, p, side, 0.16f, 0.05f, rng);
                    }
                    prev = p;
                }
            }
            // basalt rubble along the flow (dark, blocky; solid)
            int placed = 0;
            for (int t = 0; t < 140 && placed < 46; t++)
            {
                float a = lavaDeg + R(rng, -24f, 24f);
                float rr = R(rng, 14f, ShoreR(a) + 2f);
                Vector3 p = Polar(a, rr);
                if (spacing.Blocked(p.x, p.z)) continue;
                float s = R(rng, 0.35f, 1.5f);
                Vector3 sc = new Vector3(s * R(rng, 1.0f, 1.5f), s * R(rng, 0.55f, 0.9f), s);
                p.y -= sc.y * 0.3f;
                Quaternion q = Quaternion.Euler(R(rng, -10f, 10f), R(rng, 0f, 360f), R(rng, -10f, 10f));
                int seed = rng.Next();
                PropRocks.BuildSplit(near, p, p, sc, q, seed, s > 0.5f ? 1 : 0, 0.24f, PropMats.VolcanicDark, PropMats.Basalt, PropMats.Crevice);
                PropSolid.Rock(cols, p, sc, q, seed, s > 0.5f ? 1 : 0, 0.24f, p.y < 0.5f, "VolcanoRock");
                spacing.Add(p.x, p.z, s * 1.1f);
                placed++;
            }
            // where the lava meets the sea: two steam vents and a glow
            for (int k = 0; k < 2; k++)
            {
                float a = lavaDeg + (k == 0 ? -6f : 7f);
                float shore = ShoreR(a);
                Vector3 p = Polar(a, shore + 0.5f);
                p.y = Mathf.Max(p.y, Waves.SeaLevel) + 0.2f;
                fx.AddSteam(PropVolcanoFX.CreateSteam(root, p, 1.6f), p);
                if (k == 0)
                {
                    var l = PropUtil.AddPointLight(root, p + Vector3.up * 1.5f, new Color(1f, 0.45f, 0.15f), 14f, 2.2f, "LavaShoreGlow");
                    fx.AddLight(l);
                }
            }
            PropInfo.SetSpot("volcano_lava_sea", Polar(lavaDeg, ShoreR(lavaDeg)), lavaDeg);
        }

        // ================================================================== steam vents on the flanks
        static void FlankVents(Transform root, PropBatch near, Transform cols, PropVolcanoFX fx, System.Random rng, float beachDeg, float lavaDeg, PropExclusion spacing)
        {
            float[] ang = { lavaDeg + 62f, lavaDeg - 58f, beachDeg + 58f, beachDeg - 64f };
            float[] rad = { 22f, 27f, 26f, 30f };
            for (int i = 0; i < ang.Length; i++)
            {
                Vector3 p = Polar(ang[i], rad[i]);
                // a ring of small rocks with a yellow sulfur crust round the hole
                for (int k = 0; k < 6; k++)
                {
                    float a = k * 60f + R(rng, -15f, 15f);
                    Vector3 rp = p + Dir(a) * R(rng, 0.7f, 1.0f);
                    rp.y = G(rp.x, rp.z) - 0.05f;
                    float s = R(rng, 0.2f, 0.4f);
                    var sc = new Vector3(s * 1.2f, s * 0.7f, s);
                    int seed = rng.Next();
                    PropRocks.BuildSplit(near, rp, rp, sc, Quaternion.Euler(0f, a, 0f), seed, 0, 0.2f, PropMats.VolcanicDark, Sulfur, null);
                    if (s >= 0.35f) PropSolid.Rock(cols, rp, sc, Quaternion.Euler(0f, a, 0f), seed, 0, 0.2f, false, "VolcanoRock");
                }
                var sb = near.W(Sulfur, p);
                for (int k = 0; k < 5; k++)
                {
                    Vector2 o = PropUtil.InDisc(rng, 1.4f);
                    Vector3 sp = new Vector3(p.x + o.x, 0f, p.z + o.y);
                    sp.y = G(sp.x, sp.z) + 0.02f;
                    sb.Ico(sp, new Vector3(R(rng, 0.25f, 0.5f), 0.04f, R(rng, 0.2f, 0.4f)), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), 0);
                }
                Vector3 vp = p + Vector3.up * 0.15f;
                fx.AddSteam(PropVolcanoFX.CreateSteam(root, vp, 1f), vp);
                spacing.Add(p.x, p.z, 2.2f);
                PropInfo.SetSpot("volcano_vent_" + i, p);
            }
        }

        // ================================================================== beach
        static void Beach(Transform root, PropBatch near, PropBatch far, PropBatch full, PropBatch lite, GameObject palmCols, Transform cols, System.Random rng, float beachDeg, PropExclusion spacing)
        {
            // lava boulders on the black sand
            int placed = 0;
            for (int t = 0; t < 160 && placed < 20; t++)
            {
                float a = beachDeg + R(rng, -55f, 55f);
                float shore = ShoreR(a);
                Vector3 p = Polar(a, R(rng, shore - 12f, shore + 4f));
                if (p.y < -0.9f || p.y > 2.8f || spacing.Blocked(p.x, p.z)) continue;
                float s = R(rng, 0.45f, 1.7f);
                Vector3 sc = new Vector3(s * R(rng, 1.0f, 1.4f), s * R(rng, 0.6f, 0.95f), s);
                p.y -= sc.y * 0.3f;
                Quaternion q = Quaternion.Euler(R(rng, -8f, 8f), R(rng, 0f, 360f), R(rng, -8f, 8f));
                int seed = rng.Next();
                PropRocks.BuildSplit(s > 1.2f ? far : near, p, p, sc, q, seed, 1, 0.2f, PropMats.VolcanicDark, PropMats.Volcanic, PropMats.Crevice);
                PropSolid.Rock(cols, p, sc, q, seed, 1, 0.2f, p.y < 0.6f, "VolcanoRock");
                spacing.Add(p.x, p.z, s * 1.2f + 0.4f);
                placed++;
            }
            // pumice: light porous pebbles in drifts along the tide line (no colliders: < 0.35 m)
            for (int t = 0, n = 0; t < 200 && n < 48; t++)
            {
                float a = beachDeg + R(rng, -52f, 52f);
                Vector3 p = Polar(a, ShoreR(a) - R(rng, 0.5f, 6f));
                if (p.y < 0.05f || p.y > 2f || spacing.Blocked(p.x, p.z, -0.3f)) continue;
                float s = R(rng, 0.08f, 0.24f);
                PropRocks.BuildSplit(near, p, p + Vector3.up * s * 0.3f, new Vector3(s * 1.3f, s * 0.7f, s), Quaternion.Euler(0f, R(rng, 0f, 360f), 0f), rng.Next(), 0, 0.28f, rng.NextDouble() < 0.5 ? Pumice : PumiceDark, null, null);
                n++;
            }
            // driftwood logs (solid) and a bleached stump with roots
            for (int t = 0, n = 0; t < 80 && n < 6; t++)
            {
                float a = beachDeg + R(rng, -48f, 48f);
                Vector3 p = Polar(a, ShoreR(a) - R(rng, 2f, 7f));
                if (p.y < 0.25f || p.y > 2f || spacing.Blocked(p.x, p.z)) continue;
                float len = R(rng, 2f, 4.2f), r = R(rng, 0.12f, 0.22f);
                PropKit.Log(near, cols, p + Vector3.up * r * 0.8f, a + 90f + R(rng, -35f, 35f), len, r, PropMats.Driftwood);
                spacing.Add(p.x, p.z, len * 0.5f + 0.5f);
                n++;
            }
            {
                float a = beachDeg + 22f;
                Vector3 p = Polar(a, ShoreR(a) - 6f);
                var dw = near.W(PropMats.Driftwood, p);
                dw.Rod(p - Vector3.up * 0.2f, p + new Vector3(0.2f, 1.1f, 0.1f), 0.32f, 0.22f, 7, true);
                for (int k = 0; k < 7; k++)
                {
                    float ra = k * 51f * Mathf.Deg2Rad;
                    Vector3 rd = new Vector3(Mathf.Cos(ra), 0f, Mathf.Sin(ra));
                    dw.Rod(p + Vector3.up * 0.5f, p + rd * R(rng, 1.0f, 1.7f) + Vector3.up * R(rng, 0.2f, 1.3f), 0.09f, 0.03f, 4);
                }
                PropSolid.Upright(cols, p - Vector3.up * 0.2f, 0.4f, 1.4f, false);
                spacing.Add(p.x, p.z, 2f);
            }
            // tide lines of dark weed
            for (int k = 0; k < 3; k++)
            {
                float a = beachDeg - 30f + k * 28f;
                float sh = ShoreR(a) - 1.2f;
                Vector3 p0 = Polar(a - 4f, sh), p1 = Polar(a + 4f, ShoreR(a + 4f) - 1.2f);
                PropKit.TideLine(near, p0, p1, 9100 + k);
            }
            // a few wind-bent palms – only near the beach, leaning seaward
            Vector3 sea = Dir(beachDeg);
            for (int t = 0, n = 0; t < 120 && n < 7; t++)
            {
                float a = beachDeg + R(rng, -42f, 42f);
                Vector3 p = Polar(a, ShoreR(a) - R(rng, 5f, 13f));
                if (p.y < 0.6f || p.y > 3.2f || spacing.Blocked(p.x, p.z)) continue;
                Vector3 lean = Dir(a) * 0.8f + sea * 0.4f;
                PropJungle.AddPalm(full, lite, palmCols, rng, p, lean);
                spacing.Add(p.x, p.z, 4.5f);
                n++;
            }
        }

        // ================================================================== rocky lava coast (everything but the beach)
        static void RockyShore(PropBatch near, PropBatch far, Transform cols, System.Random rng, float beachDeg, float lavaDeg, PropExclusion spacing)
        {
            int placed = 0;
            for (int t = 0; t < 220 && placed < 64; t++)
            {
                float a = R(rng, 0f, 360f);
                if (Mathf.Abs(Mathf.DeltaAngle(a, beachDeg)) < 50f) continue;
                float shore = ShoreR(a);
                Vector3 p = Polar(a, shore + R(rng, -5f, 4f));
                if (p.y < -3.5f || spacing.Blocked(p.x, p.z)) continue;
                float s = R(rng, 0.6f, 2.4f);
                Vector3 sc = new Vector3(s * R(rng, 1.0f, 1.4f), s * R(rng, 0.6f, 1.0f), s);
                p.y -= sc.y * 0.3f;
                Quaternion q = Quaternion.Euler(R(rng, -12f, 12f), R(rng, 0f, 360f), R(rng, -12f, 12f));
                int seed = rng.Next();
                PropRocks.BuildSplit(s > 1.3f ? far : near, p, p, sc, q, seed, 1, 0.24f, rng.NextDouble() < 0.6 ? PropMats.VolcanicDark : PropMats.Volcanic, PropMats.Basalt, PropMats.Crevice);
                PropSolid.Rock(cols, p, sc, q, seed, 1, 0.24f, p.y < 0.8f, "VolcanoRock");
                spacing.Add(p.x, p.z, s * 1.1f);
                placed++;
            }
        }

        // ================================================================== fossil bed
        /// A low layered rock face (tuff / ash / basalt strata) at the back of the beach with three ammonites, a long bone,
        /// a rib arc, a vertebra chain and a big skull weathering out (visual only). Returns the standing spot in front.
        static Vector3 FossilBed(PropBatch near, Transform cols, System.Random rng, float deg, PropExclusion spacing)
        {
            Vector3 d = Dir(deg);
            float r = 30f;
            for (; r < 60f; r += 0.5f)
                if (G(C.x + d.x * r, C.z + d.z * r) < 3.4f) break;
            Vector3 p = Polar(deg, r - 1.5f);
            Vector3 outN = d;
            Vector3 lat = new Vector3(outN.z, 0f, -outN.x);
            Quaternion q = Quaternion.LookRotation(outN);
            float baseY = Mathf.Min(G(p.x - lat.x * 3.5f, p.z - lat.z * 3.5f), Mathf.Min(G(p.x, p.z), G(p.x + lat.x * 3.5f, p.z + lat.z * 3.5f))) - 0.4f;
            Material[] strata = { PropMats.VolcanicDark, Tuff, TuffDark, TuffLight, Tuff };
            float y = baseY;
            for (int i = 0; i < strata.Length; i++)
            {
                float h = R(rng, 0.45f, 0.75f);
                Vector3 c = p + Vector3.up * (y - p.y + h * 0.5f) - outN * (0.15f * i) + lat * R(rng, -0.2f, 0.2f);
                near.W(strata[i], c).Box(c, new Vector3(7.6f - i * 0.5f, h, 1.6f + i * 0.2f), q * Quaternion.Euler(0f, 0f, R(rng, -4f, 4f)));
                y += h;
            }
            float wallH = y - baseY;
            Vector3 face = p + outN * 0.81f;   // front face plane of the bottom layer (box depth 1.6 / 2)
            PropSolid.Box(cols, p + Vector3.up * (baseY - p.y + wallH * 0.5f), new Vector3(7.6f, wallH, 1.8f), q, "FossilWall");
            var bone = near.W(Fossil, p);
            var boneD = near.W(FossilDark, p);
            // ammonites: flat spiral tubes on the face
            float[] ax = { -2.4f, 0.4f, 2.6f };
            float[] ay = { 0.9f, 1.6f, 0.7f };
            float[] ar = { 0.42f, 0.6f, 0.32f };
            var pts = new List<Vector3>(40);
            var rad = new List<float>(40);
            for (int k = 0; k < ax.Length; k++)
            {
                Vector3 c0 = new Vector3(face.x, baseY + ay[k], face.z) + lat * ax[k] - outN * (0.15f * Mathf.Floor(ay[k] / 0.6f));
                pts.Clear();
                rad.Clear();
                const int S = 30;
                for (int i = 0; i <= S; i++)
                {
                    float t = i / (float)S;
                    float th = t * Mathf.PI * 2f * 2.6f;
                    float rr = ar[k] * Mathf.Exp((t - 1f) * 2.2f);
                    pts.Add(c0 + (lat * Mathf.Cos(th) + Vector3.up * Mathf.Sin(th)) * rr + outN * 0.05f);
                    rad.Add(Mathf.Max(0.015f, rr * 0.32f));
                }
                bone.Tube(pts, rad, 5);
                boneD.Disk(c0 + outN * 0.03f, ar[k] * 1.05f, 9, Quaternion.FromToRotation(Vector3.up, outN), true);
            }
            // a long bone (femur)
            {
                Vector3 b0 = new Vector3(face.x, baseY + 1.25f, face.z) - lat * 1.1f - outN * 0.1f, b1 = b0 + lat * 1.9f + Vector3.up * 0.25f;
                bone.Rod(b0, b1, 0.09f, 0.08f, 6);
                bone.Ico(b0, new Vector3(0.2f, 0.17f, 0.15f), Quaternion.identity, 0);
                bone.Ico(b1, new Vector3(0.18f, 0.2f, 0.14f), Quaternion.identity, 0);
            }
            // rib arc + vertebra chain on the upper layers
            {
                Vector3 spine0 = new Vector3(face.x, baseY + 2.35f, face.z) - lat * 3.0f - outN * 0.45f;
                for (int i = 0; i < 9; i++)
                {
                    Vector3 v = spine0 + lat * (i * 0.32f) + Vector3.up * Mathf.Sin(i * 0.4f) * 0.12f + outN * 0.05f;
                    bone.Box(v, new Vector3(0.2f, 0.16f, 0.14f), q * Quaternion.Euler(0f, 0f, i * 7f));
                    if (i % 2 == 1 && i < 8)
                    {
                        pts.Clear();
                        rad.Clear();
                        for (int j = 0; j <= 4; j++)
                        {
                            float tt = j / 4f;
                            pts.Add(v + Vector3.down * (tt * 0.75f) + lat * (Mathf.Sin(tt * 2.2f) * 0.32f) + outN * 0.03f);
                            rad.Add(0.04f - tt * 0.02f);
                        }
                        bone.Tube(pts, rad, 4);
                    }
                }
            }
            // a big skull (long snout, dark eye socket)
            {
                Vector3 sk = new Vector3(face.x, baseY + 2.05f, face.z) + lat * 1.9f - outN * 0.3f;
                bone.Ico(sk, new Vector3(0.55f, 0.32f, 0.22f), q * Quaternion.Euler(0f, 90f, -8f), 1);
                bone.Cylinder(sk + lat * 0.45f, q * Quaternion.Euler(0f, 0f, -90f), 0.17f, 0.07f, 0.7f, 6, false, true);
                near.W(PropMats.Ink, sk).Disk(sk - lat * 0.18f + Vector3.up * 0.08f + outN * 0.2f, 0.09f, 7, Quaternion.FromToRotation(Vector3.up, outN), true);
                // the jaw and a few teeth
                Vector3 j0 = sk - Vector3.up * 0.22f;
                bone.Beam(j0 - lat * 0.25f, j0 + lat * 0.95f, 0.12f, 0.06f);
                for (int i = 0; i < 6; i++)
                    bone.Cylinder(j0 + lat * (0.15f + i * 0.13f) + Vector3.up * 0.02f, Quaternion.identity, 0.025f, 0f, 0.09f, 4);
            }
            spacing.Add(p.x, p.z, 5.5f);
            Vector3 stand = p + outN * 2.6f;
            stand.y = G(stand.x, stand.z);
            PropInfo.SetSpot("volcano_fossils", stand, YawTo(stand, p));
            return stand;
        }

        // ================================================================== slopes
        static IEnumerator Slopes(PropBatch near, PropBatch far, PropBatch full, PropBatch lite, PropBatch under, Transform treeCols, Transform cols,
            PropGrassField grass, System.Random rng, float beachDeg, float lavaDeg, PropExclusion spacing, List<Vector3> meadows, List<Vector3> perches, List<Vector4> bask)
        {
            const float step = 2.6f;
            float ext = 64f;
            int rows = 0;
            for (float gz = -ext; gz <= ext; gz += step)
            {
                for (float gx = -ext; gx <= ext; gx += step)
                {
                    float x = C.x + gx + R(rng, -0.9f, 0.9f), z = C.z + gz + R(rng, -0.9f, 0.9f);
                    double roll = rng.NextDouble(), roll2 = rng.NextDouble();
                    float jitter = R(rng, 0f, 1f);
                    float rr = RadiusAt(x, z);
                    if (rr > 66f || rr < WorldFeatures.CraterRimRadius + 1.5f) continue;
                    float g = G(x, z);
                    if (g < 2.2f) continue;
                    if (spacing.Blocked(x, z)) continue;
                    var hint = WorldTerrainData.GroundAt(x, z, g);
                    float ang = AngleAt(x, z);
                    bool onFlow = Mathf.Abs(Mathf.DeltaAngle(ang, lavaDeg)) < 26f;
                    Vector3 p = new Vector3(x, g, z);
                    if (g > 21f || onFlow || (hint == GroundHint.Rock && g > 15f))
                    {
                        // bare upper cone / flow: ash and lava rocks, a few burnt snags
                        if (roll < 0.075)
                        {
                            float s = R(rng, 0.35f, 1.3f);
                            Vector3 sc = new Vector3(s * R(rng, 1f, 1.4f), s * R(rng, 0.55f, 0.9f), s);
                            Vector3 rp = p - Vector3.up * sc.y * 0.3f;
                            Quaternion q = Quaternion.Euler(R(rng, -10f, 10f), R(rng, 0f, 360f), R(rng, -10f, 10f));
                            int seed = rng.Next();
                            PropRocks.BuildSplit(g > 24f && s > 0.9f ? far : near, rp, rp, sc, q, seed, s > 0.5f ? 1 : 0, 0.22f, roll2 < 0.5 ? PropMats.VolcanicDark : PropMats.Ash, PropMats.Volcanic, PropMats.Crevice);
                            PropSolid.Rock(cols, rp, sc, q, seed, s > 0.5f ? 1 : 0, 0.22f, false, "VolcanoRock");
                            spacing.Add(x, z, s * 1.1f);
                            if (s > 0.8f && bask.Count < 8 && g < 26f) bask.Add(new Vector4(rp.x, rp.y + sc.y * 0.9f, rp.z, s));
                        }
                        else if (roll < 0.1 && g < 27f && !onFlow)
                        {
                            perches.Add(PropVolcanoPlants.AddSnag(full, lite, treeCols, rng, p));
                            spacing.Add(x, z, 2f);
                        }
                        continue;
                    }
                    if (hint == GroundHint.Rock)
                    {
                        // lava boulder fields between the fern patches
                        if (roll < 0.09)
                        {
                            float s = R(rng, 0.4f, 1.4f);
                            Vector3 sc = new Vector3(s * R(rng, 1f, 1.4f), s * R(rng, 0.6f, 0.95f), s);
                            Vector3 rp = p - Vector3.up * sc.y * 0.3f;
                            Quaternion q = Quaternion.Euler(R(rng, -10f, 10f), R(rng, 0f, 360f), R(rng, -10f, 10f));
                            int seed = rng.Next();
                            PropRocks.BuildSplit(near, rp, rp, sc, q, seed, 1, 0.2f, PropMats.VolcanicDark, PropMats.Moss, PropMats.Crevice);
                            PropSolid.Rock(cols, rp, sc, q, seed, 1, 0.2f, false, "VolcanoRock");
                            spacing.Add(x, z, s * 1.1f);
                            if (s > 0.75f && bask.Count < 8) bask.Add(new Vector4(rp.x, rp.y + sc.y * 0.9f, rp.z, s));
                        }
                        else if (roll < 0.3) PropJungle.AddPlant(under, under, rng, p, 2, 1.1f);
                        else if (roll < 0.34 && g < 14f) { PropVolcanoPlants.AddCycad(full, lite, treeCols, rng, p, 0.85f); spacing.Add(x, z, 1.6f); }
                        continue;
                    }
                    // green fern slopes: tree ferns, cycads, dense ferns, grass; snags higher up
                    if (g > 12.5f && roll < 0.045)
                    {
                        perches.Add(PropVolcanoPlants.AddSnag(full, lite, treeCols, rng, p));
                        spacing.Add(x, z, 2f);
                    }
                    else if (g < 18f && roll < 0.15)
                    {
                        PropVolcanoPlants.AddTreeFern(full, lite, treeCols, rng, p, g < 6f ? 0.9f : 1.05f);
                        spacing.Add(x, z, 2.2f);
                    }
                    else if (g < 13f && roll < 0.23)
                    {
                        PropVolcanoPlants.AddCycad(full, lite, treeCols, rng, p);
                        spacing.Add(x, z, 1.8f);
                    }
                    else if (roll < 0.25) PropJungle.AddPlant(under, under, rng, p, 0, 0.8f);
                    // ferns everywhere on the green slope (1–2 per cell)
                    if (roll2 < 0.7)
                    {
                        Vector3 fp = new Vector3(x + R(rng, -1f, 1f), 0f, z + R(rng, -1f, 1f));
                        fp.y = G(fp.x, fp.z);
                        if (!spacing.Blocked(fp.x, fp.z, -0.8f)) PropJungle.AddPlant(under, under, rng, fp, 2, R(rng, 1.1f, 1.5f));
                    }
                    if (roll2 < 0.3)
                    {
                        Vector3 fp = new Vector3(x + R(rng, -1.2f, 1.2f), 0f, z + R(rng, -1.2f, 1.2f));
                        fp.y = G(fp.x, fp.z);
                        if (!spacing.Blocked(fp.x, fp.z, -0.8f)) PropJungle.AddPlant(under, under, rng, fp, 2, R(rng, 0.9f, 1.3f));
                    }
                    if (grass != null && jitter < 0.65f)
                    {
                        int v = jitter < 0.3f ? PropGrassField.JungleFirst + rng.Next(PropGrassField.JungleCount) : rng.Next(PropGrassField.MeadowCount);
                        grass.Add(v, new Vector3(x + R(rng, -0.8f, 0.8f), g - 0.03f, z + R(rng, -0.8f, 0.8f)), R(rng, 0f, 360f), R(rng, 0.85f, 1.2f), Vector3.zero);
                    }
                    if (meadows.Count < 6 && g > 4f && g < 14f && (meadows.Count == 0 || MinDist(meadows, p) > 20f) && roll2 > 0.9) meadows.Add(p);
                }
                if (++rows % 4 == 0 && PropRun.Due()) yield return null;
            }

            // horsetails in the damp hollow behind the beach (groups of 2–4)
            for (int t = 0, n = 0; t < 120 && n < 9; t++)
            {
                float a = beachDeg + R(rng, -40f, 40f);
                Vector3 p = Polar(a, ShoreR(a) - R(rng, 8f, 16f));
                if (p.y < 1.2f || p.y > 5.5f || spacing.Blocked(p.x, p.z, -0.6f)) continue;
                int k = 2 + rng.Next(3);
                for (int i = 0; i < k; i++)
                {
                    Vector2 o = PropUtil.InDisc(rng, 1.6f);
                    Vector3 hp = new Vector3(p.x + o.x, 0f, p.z + o.y);
                    hp.y = G(hp.x, hp.z);
                    PropVolcanoPlants.AddHorsetail(under, rng, hp);
                }
                spacing.Add(p.x, p.z, 2.2f);
                n++;
            }
        }

        static float MinDist(List<Vector3> list, Vector3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < list.Count; i++) best = Mathf.Min(best, Vector3.Distance(list[i], p));
            return best;
        }

        // ================================================================== land anchors
        static void LandAnchors(float beachDeg, Vector3 beach, Vector3 fossil, List<Vector3> meadows, List<Vector3> perches, List<Vector4> bask, PropExclusion spacing)
        {
            // beach strip (horseshoe crabs, shore birds) – three stretches of black sand
            for (int k = 0; k < 3; k++)
            {
                float a = beachDeg + (k - 1) * 24f;
                Vector3 p = Polar(a, ShoreR(a) - 2.5f);
                PropInfo.AddLandSpot(p, LandSpotKind.Shore, 9f, Area);
            }
            // shallow water off the beach (wading)
            {
                Vector3 p = Polar(beachDeg + 12f, ShoreR(beachDeg + 12f) + 6f);
                PropInfo.AddLandSpot(p, LandSpotKind.Water, 6f, Area);
            }
            // sun-warmed boulders (tuatara / iguana-like reptiles)
            for (int i = 0; i < bask.Count; i++)
                PropInfo.AddLandSpot(new Vector3(bask[i].x, bask[i].y, bask[i].z), LandSpotKind.Bask, Mathf.Max(0.5f, bask[i].w), Area);
            // fern meadows (giant dragonflies) + burrows on the slope
            for (int i = 0; i < meadows.Count; i++)
                PropInfo.AddLandSpot(meadows[i], i % 3 == 2 ? LandSpotKind.Burrow : LandSpotKind.Meadow, i % 3 == 2 ? 1.5f : 9f, Area);
            // snag tops (perches)
            for (int i = 0; i < perches.Count && i < 6; i++)
                PropInfo.AddLandSpot(perches[i], LandSpotKind.Perch, 0.4f, Area);
            // named land spots 0..5: beach centre, beach north, beach south, fern slope, fossil bed, high fern / snag slope
            Vector3 n1 = Polar(beachDeg - 26f, ShoreR(beachDeg - 26f) - 4f);
            Vector3 s1 = Polar(beachDeg + 26f, ShoreR(beachDeg + 26f) - 4f);
            Vector3 slope = meadows.Count > 0 ? meadows[0] : Polar(beachDeg + 40f, 36f);
            Vector3 high = meadows.Count > 1 ? meadows[meadows.Count - 1] : Polar(beachDeg - 50f, 30f);
            if (perches.Count > 0)
            {
                // the highest snag's ground spot is the "high slope" spot
                Vector3 best = perches[0];
                for (int i = 1; i < perches.Count; i++) if (perches[i].y > best.y && perches[i].y < 30f) best = perches[i];
                high = new Vector3(best.x + 1.5f, 0f, best.z);
                high.y = G(high.x, high.z);
            }
            Vector3[] named = { beach, n1, s1, slope, fossil, high };
            for (int i = 0; i < named.Length; i++)
            {
                Vector3 p = named[i];
                p.y = G(p.x, p.z) + 0.02f;
                PropInfo.SetSpot("volcano_land_" + i, p, YawTo(p, C));
            }
        }

        // ================================================================== under water
        static IEnumerator Underwater(Transform root, System.Random rng, float beachDeg, float lavaDeg, PropExclusion spacing)
        {
            var uw = PropUtil.WorldRoot("VolcanoUnderwater", root);
            var decor = PropUtil.WorldRoot("Decor", uw);
            uw.gameObject.AddComponent<PropSiteActivation>().Init(decor.gameObject, C, UnderwaterRange);
            var ucols = new GameObject("Colliders").transform;
            ucols.SetParent(decor, false);
            var rocks = new PropBatch(48f);
            var soft = new PropBatch(48f);

            // ---- the black-sand / lava shelf (west) and the narrow shallow ring: lava-rock reef with sea lilies
            for (int k = 0; k < 6; k++)
            {
                float a = beachDeg - 75f + k * 30f;
                float want = k % 2 == 0 ? 6f : 10f;
                Vector3 p = Vector3.zero;
                for (int tries = 0; tries < 6; tries++)
                {
                    if (DepthPoint(a + tries * 4f, want + tries, 19f, ShoreR(a) + 4f, out p) && !spacing.Blocked(p.x, p.z)) break;
                    p = Vector3.zero;
                }
                if (p == Vector3.zero) continue;
                PropInfo.SetSpot("volcano_reef_" + k, p + Vector3.up * 0.3f, YawTo(p, C));
                ReefCluster(rocks, soft, ucols, rng, p, R(rng, 4f, 6f));
                PropInfo.AddHabitat(p + Vector3.up * 0.6f, 3f, HabitatKind.Crevice, Zone);
                PropInfo.AddHabitat(p + Dir(a) * 5f + Vector3.up * 0.4f, 4f, HabitatKind.SeaFan, Zone);
                Vector3 sand = p + Dir(a) * 9f;
                sand.y = G(sand.x, sand.z) + 0.3f;
                PropInfo.AddHabitat(sand, 6f, HabitatKind.Sand, Zone);
                spacing.Add(p.x, p.z, 7f);
                if (PropRun.Due()) yield return null;
            }
            // scattered lava boulders on the shelf and the shallow rocky ring (dark rock, few corals)
            for (int t = 0, n = 0; t < 260 && n < 48; t++)
            {
                float a = R(rng, 0f, 360f);
                bool shelf = Mathf.Abs(Mathf.DeltaAngle(a, beachDeg)) < 85f;
                float sh = ShoreR(a);
                Vector3 p = Polar(a, R(rng, sh + 2f, sh + (shelf ? 55f : 9f)));
                float d = -p.y;
                if (d < 1.6f || d > 20f || spacing.Blocked(p.x, p.z)) continue;
                float s = R(rng, 0.5f, 1.7f);
                Vector3 sc = new Vector3(s * R(rng, 1f, 1.4f), s * R(rng, 0.55f, 0.9f), s);
                p.y -= sc.y * 0.3f;
                Quaternion q = Quaternion.Euler(R(rng, -10f, 10f), R(rng, 0f, 360f), R(rng, -10f, 10f));
                int seed = rng.Next();
                PropRocks.BuildSplit(rocks, p, p, sc, q, seed, 1, 0.16f, PropMats.VolcanicDark, PropMats.ReefRockDark, PropMats.Crevice);
                PropSolid.Rock(ucols, p, sc, q, seed, 1, 0.16f, true, "VolcanoReefRock");
                spacing.Add(p.x, p.z, s * 1.2f);
                if (rng.NextDouble() < 0.5) PropVolcanoPlants.AddCrinoid(soft, rng, new Vector3(p.x + sc.x * 0.9f, G(p.x + sc.x * 0.9f, p.z), p.z));
                n++;
            }
            if (PropRun.Due()) yield return null;

            // ---- the steep east drop-off: four sea-lily gardens at 45 / 55 / 65 / 75 m
            float[] deepAng = { 300f, 325f, 15f, 50f };
            float[] deepD = { 45f, 55f, 65f, 75f };
            for (int k = 0; k < deepAng.Length; k++)
            {
                if (!DepthPoint(deepAng[k], deepD[k], 82f, 45f, out Vector3 p)) continue;
                PropInfo.SetSpot("volcano_deep_" + k, p + Vector3.up * 0.5f, YawTo(p, C) + 180f);
                PropInfo.AddHabitat(p + Vector3.up * 1f, 5f, HabitatKind.Crevice, Zone);
                Vector3 tan = new Vector3(-Dir(deepAng[k]).z, 0f, Dir(deepAng[k]).x);
                for (int i = 0; i < 9; i++)
                {
                    float off = (i - 4) * 1.6f + R(rng, -0.5f, 0.5f);
                    float aa = deepAng[k] + off / RadiusAt(p.x, p.z) * Mathf.Rad2Deg;
                    if (!DepthPoint(aa, deepD[k] + R(rng, -4f, 4f), 85f, 45f, out Vector3 cp)) continue;
                    PropVolcanoPlants.AddCrinoid(soft, rng, cp, R(rng, 1.1f, 1.5f));
                }
                for (int i = 0; i < 3; i++)
                {
                    Vector3 rp = p + tan * R(rng, -5f, 5f);
                    rp.y = G(rp.x, rp.z);
                    float s = R(rng, 0.7f, 1.5f);
                    Vector3 sc = new Vector3(s * 1.2f, s * 0.8f, s);
                    Quaternion q = Quaternion.Euler(R(rng, -15f, 15f), R(rng, 0f, 360f), 0f);
                    int seed = rng.Next();
                    PropRocks.BuildSplit(rocks, rp, rp, sc, q, seed, 1, 0.18f, PropMats.VolcanicDark, PropMats.ReefRockDark, PropMats.Crevice);
                    PropSolid.Rock(ucols, rp, sc, q, seed, 1, 0.18f, true, "VolcanoReefRock");
                }
            }
            if (PropRun.Due()) yield return null;

            // ---- the lava-tube overhang: five rock arches stepping down the east wall (≈46 → 59 m)
            LavaTube(rocks, soft, ucols, rng, 348f);

            rocks.Flush(decor, "VolcanoReefRocks", ShadowCastingMode.On, 150f);
            soft.Flush(decor, "VolcanoSeaLilies", ShadowCastingMode.Off, 70f);
        }

        static void ReefCluster(PropBatch rocks, PropBatch soft, Transform ucols, System.Random rng, Vector3 c, float radius)
        {
            int n = 6 + rng.Next(4);
            for (int i = 0; i < n; i++)
            {
                Vector2 o = i == 0 ? Vector2.zero : PropUtil.InDisc(rng, radius * 0.7f);
                Vector3 p = new Vector3(c.x + o.x, 0f, c.z + o.y);
                p.y = G(p.x, p.z);
                float s = i == 0 ? R(rng, 1.3f, 1.9f) : R(rng, 0.45f, 1.3f);
                // pillow lava: rounder, flatter rocks stacked
                Vector3 sc = new Vector3(s * R(rng, 1.1f, 1.5f), s * R(rng, 0.55f, 0.8f), s);
                p.y -= sc.y * 0.25f;
                Quaternion q = Quaternion.Euler(R(rng, -10f, 10f), R(rng, 0f, 360f), R(rng, -10f, 10f));
                int seed = rng.Next();
                PropRocks.BuildSplit(rocks, p, p, sc, q, seed, 1, 0.1f, PropMats.VolcanicDark, PropMats.ReefRockDark, PropMats.Crevice);
                PropSolid.Rock(ucols, p, sc, q, seed, 1, 0.1f, true, "VolcanoReefRock");
                if (i == 0)
                {
                    // a smaller pillow on top
                    Vector3 p2 = p + Vector3.up * sc.y * 0.85f + new Vector3(R(rng, -0.3f, 0.3f), 0f, R(rng, -0.3f, 0.3f));
                    Vector3 sc2 = sc * 0.55f;
                    int seed2 = rng.Next();
                    PropRocks.BuildSplit(rocks, p2, p2, sc2, q, seed2, 1, 0.1f, PropMats.VolcanicDark, PropMats.ReefRockDark, PropMats.Crevice);
                    PropSolid.Rock(ucols, p2, sc2, q, seed2, 1, 0.1f, true, "VolcanoReefRock");
                }
            }
            // sea lilies, a few barrel sponges and urchins (soft / small: passable)
            int lilies = 8 + rng.Next(7);
            for (int i = 0; i < lilies; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius);
                Vector3 p = new Vector3(c.x + o.x, 0f, c.z + o.y);
                p.y = G(p.x, p.z);
                PropVolcanoPlants.AddCrinoid(soft, rng, p);
            }
            for (int i = 0; i < 3; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius);
                Vector3 p = new Vector3(c.x + o.x, 0f, c.z + o.y);
                p.y = G(p.x, p.z) - 0.05f;
                float r = R(rng, 0.2f, 0.35f), h = R(rng, 0.45f, 0.85f);
                soft.W(PropMats.Sponge(rng.Next(6)), p).Cylinder(p, Quaternion.identity, r, r * 1.15f, h, 7, false, false);
                soft.W(PropMats.SpongeInside, p).Disk(p + Vector3.up * (h - 0.05f), r * 1.05f, 7, Quaternion.identity, true);
            }
            var urchin = soft.W(PropMats.Urchin, c);
            for (int i = 0; i < 5; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius * 0.8f);
                Vector3 p = new Vector3(c.x + o.x, 0f, c.z + o.y);
                p.y = G(p.x, p.z) + 0.08f;
                urchin.Octa(p, new Vector3(0.14f, 0.1f, 0.14f));
                urchin.Octa(p, new Vector3(0.1f, 0.16f, 0.1f));
            }
        }

        /// Five arches of big rocks (ring radius 3.5 m, ≈1.7 m clearance) around a path that runs diagonally down the
        /// steep east wall; two roof sections between the middle arches make it a real tube (the "cave").
        static void LavaTube(PropBatch rocks, PropBatch soft, Transform ucols, System.Random rng, float midDeg)
        {
            const int A = 5;
            var P = new Vector3[A];
            for (int i = 0; i < A; i++)
            {
                if (!DepthPoint(midDeg + (i - 2) * 5f, 46f + i * 3.2f, 70f, 50f, out P[i]))
                {
                    Debug.LogWarning("Sealebrities props: volcano lava tube – no wall point for arch " + i);
                    return;
                }
            }
            var centers = new Vector3[A];
            var outs = new Vector3[A];
            var tans = new Vector3[A];
            for (int i = 0; i < A; i++)
            {
                Vector3 t = Flat(P[Mathf.Min(A - 1, i + 1)] - P[Mathf.Max(0, i - 1)]);
                Vector3 o = Flat(P[i] - C);
                o = Flat(o - t * Vector3.Dot(o, t));
                tans[i] = t;
                outs[i] = o;
                centers[i] = P[i] + o * 1.0f + Vector3.up * 1.9f;
            }
            // the hoop leans on the wall: it meets the 67° slope near φ = −50° (above the path) and φ ≈ 185° (below it)
            float[] phis = { -50f, -15f, 20f, 55f, 90f, 125f, 160f, 192f };
            for (int i = 0; i < A; i++)
                ArchRing(rocks, ucols, rng, centers[i], outs[i], tans[i], phis, 3.5f);
            float[] roof = { -10f, 30f, 70f, 110f };
            for (int i = 1; i < A - 2; i++)
            {
                Vector3 m = (centers[i] + centers[i + 1]) * 0.5f;
                ArchRing(rocks, ucols, rng, m, Flat(outs[i] + outs[i + 1]), Flat(tans[i] + tans[i + 1]), roof, 3.6f);
            }
            // a few sea lilies on the arch tops and at the entrance
            for (int i = 0; i < A; i += 2)
            {
                Vector3 top = centers[i] + Vector3.up * 3.5f + outs[i] * 0.6f;
                PropVolcanoPlants.AddCrinoid(soft, rng, top + Vector3.up * 1.2f, 0.9f);
            }
            PropInfo.SetSpot("volcano_cave", centers[2], Mathf.Atan2(tans[2].x, tans[2].z) * Mathf.Rad2Deg);
            PropInfo.SetSpot("volcano_cave_in", centers[0] - tans[0] * 3f, Mathf.Atan2(tans[0].x, tans[0].z) * Mathf.Rad2Deg);
            PropInfo.SetSpot("volcano_cave_out", centers[A - 1] + tans[A - 1] * 3f, Mathf.Atan2(tans[A - 1].x, tans[A - 1].z) * Mathf.Rad2Deg);
            for (int i = 1; i < A - 1; i++) PropInfo.AddHabitat(centers[i], 2.2f, HabitatKind.Overhang, Zone);
        }

        static void ArchRing(PropBatch rocks, Transform ucols, System.Random rng, Vector3 c, Vector3 outN, Vector3 tan, float[] phis, float radius)
        {
            for (int k = 0; k < phis.Length; k++)
            {
                float ph = (phis[k] + R(rng, -6f, 6f)) * Mathf.Deg2Rad;
                Vector3 d = Vector3.up * Mathf.Cos(ph) + outN * Mathf.Sin(ph);
                Vector3 p = c + d * radius;
                float s = R(rng, 1.45f, 1.85f);
                Vector3 sc = new Vector3(s * R(rng, 1.0f, 1.25f), s * R(rng, 0.85f, 1.05f), s * R(rng, 1.1f, 1.4f));
                if (p.y + s < G(p.x, p.z) - 0.3f) continue;   // fully inside the wall
                Quaternion q = Quaternion.LookRotation(tan, d) * Quaternion.Euler(R(rng, -8f, 8f), R(rng, -8f, 8f), R(rng, -8f, 8f));
                int seed = rng.Next();
                PropRocks.BuildSplit(rocks, p, p, sc, q, seed, 1, 0.14f, PropMats.VolcanicDark, PropMats.ReefRockDark, PropMats.Crevice);
                PropSolid.Rock(ucols, p, sc, q, seed, 1, 0.14f, true, "VolcanoCaveRock");
            }
        }
    }

    /// Runtime side of the volcano: the smoke plume (wind drift, night tint + orange underglow), lava emission pulse,
    /// steam vents (stopped beyond 320 m), the crater / shore lights (night only, culled beyond 450 m). Polls 5× a second,
    /// no allocations.
    public sealed class PropVolcanoFX : MonoBehaviour
    {
        public Vector3 center;
        public ParticleSystem smoke;
        readonly List<ParticleSystem> steam = new List<ParticleSystem>();
        readonly List<Light> lights = new List<Light>();
        readonly List<float> lightBase = new List<float>();
        readonly List<Material> lava = new List<Material>();
        readonly List<Color> lavaColor = new List<Color>();
        readonly GradientColorKey[] ck = new GradientColorKey[3];
        readonly GradientAlphaKey[] ak = new GradientAlphaKey[3];
        Gradient grad;
        float next, nextWind, lastNight = -1f;
        bool steamOn = true;

        /// Trade wind from the east (same as WorldClouds): the plume leans west-north-west.
        static readonly Vector3 WindDir = new Vector3(-0.93f, 0f, 0.37f);
        public const float SteamRange = 320f, LightRange = 450f;

        public void AddSteam(ParticleSystem ps, Vector3 pos) { if (ps != null) steam.Add(ps); }
        public void AddLight(Light l) { if (l == null) return; lights.Add(l); lightBase.Add(l.intensity); l.enabled = false; }
        public void AddLava(Material m, Color emission) { if (m == null) return; lava.Add(m); lavaColor.Add(emission); }
        public int SteamCount => steam.Count;

        void Update()
        {
            float now = Time.unscaledTime;
            if (now < next) return;
            next = now + 0.2f;
            float night = EnvState.Night01;
            float camD = PropCam.Valid ? Vector2.Distance(new Vector2(PropCam.Position.x, PropCam.Position.z), new Vector2(center.x, center.z)) : 0f;

            // lava: always glowing, brighter at night, a slow pulse
            float pulse = 1f + 0.12f * Mathf.Sin(Time.time * 1.3f);
            float lk = (0.75f + 1.5f * night) * pulse;
            for (int i = 0; i < lava.Count; i++)
                if (lava[i] != null) PropMats.SetEmission(lava[i], lavaColor[i] * (lk * (i == 0 ? 1f : 0.9f + 0.1f * Mathf.Sin(Time.time * 2.1f + i))));

            // lights: night only, near only
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((night - 0.1f) / 0.4f));
            bool lightsOn = k > 0.02f && camD < LightRange;
            for (int i = 0; i < lights.Count; i++)
            {
                var l = lights[i];
                if (l == null) continue;
                if (l.enabled != lightsOn) l.enabled = lightsOn;
                if (lightsOn) l.intensity = lightBase[i] * k * (0.9f + 0.1f * pulse);
            }

            // steam vents: stop far away
            bool wantSteam = camD < SteamRange;
            if (wantSteam != steamOn)
            {
                steamOn = wantSteam;
                for (int i = 0; i < steam.Count; i++)
                {
                    if (steam[i] == null) continue;
                    if (wantSteam) steam[i].Play(true);
                    else steam[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (smoke == null) return;
            // plume tint: light grey by day, dark at night with an orange underglow from the lava
            if (Mathf.Abs(night - lastNight) > 0.02f)
            {
                lastNight = night;
                if (grad == null) grad = new Gradient();
                Color body = Color.Lerp(new Color(0.8f, 0.78f, 0.76f), new Color(0.2f, 0.19f, 0.2f), night);
                Color baseC = Color.Lerp(new Color(0.55f, 0.52f, 0.5f), new Color(1f, 0.45f, 0.18f), night);
                Color topC = Color.Lerp(new Color(0.9f, 0.89f, 0.88f), new Color(0.16f, 0.16f, 0.18f), night);
                ck[0] = new GradientColorKey(baseC, 0f);
                ck[1] = new GradientColorKey(body, 0.22f);
                ck[2] = new GradientColorKey(topC, 1f);
                ak[0] = new GradientAlphaKey(0.0f, 0f);
                ak[1] = new GradientAlphaKey(0.85f, 0.08f);
                ak[2] = new GradientAlphaKey(0f, 1f);
                grad.SetKeys(ck, ak);
                var col = smoke.colorOverLifetime;
                col.color = grad;
            }
            if (now >= nextWind)
            {
                nextWind = now + 5f;
                float w = 0.9f + 2.6f * Weather.Wind01(Weather.Today);
                var vel = smoke.velocityOverLifetime;
                vel.xMultiplier = WindDir.x * w;
                vel.zMultiplier = WindDir.z * w;
            }
        }

        // ------------------------------------------------------------------ particle systems
        static Material smokeMat, steamMat;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { smokeMat = null; steamMat = null; mistMat = null; }

        static Material ParticleMat(string name, Color color)
        {
            Shader sh = MaterialLibrary.ParticlesUnlit;
            if (sh == null) sh = MaterialLibrary.UnlitShader;
            var m = new Material(sh) { name = name };
            var tex = PropTex.SoftDot;
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static ParticleSystem Create(Transform parent, string name, Vector3 pos, Material mat, int max)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(-90f, 0f, 0f));
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// The volcano plume: one system, ≈150 huge soft particles (9–15 m, growing ×2.6), 70–90 s life, rising
        /// ~1.6 m/s and drifting with the wind – a 100+ m column readable from the dock at 580 m.
        public static ParticleSystem CreateSmoke(Transform parent, Vector3 pos)
        {
            if (smokeMat == null) smokeMat = ParticleMat("Volcano_Smoke", Color.white);
            var ps = Create(parent, "VolcanoSmoke", pos, smokeMat, 220);
            var main = ps.main;
            main.duration = 90f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(70f, 90f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.35f, 1.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(9f, 15f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission;
            em.rateOverTime = 1.8f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 7f;
            sh.radius = 3.5f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            var ramp = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.3f, 0.45f), new Keyframe(1f, 1f));
            vel.x = new ParticleSystem.MinMaxCurve(-1.6f, ramp);
            vel.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, -0.7f));
            vel.z = new ParticleSystem.MinMaxCurve(0.6f, ramp);
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.15f, 0.8f), new Keyframe(1f, 2.6f)));
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.04f, 0.04f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.55f, 0.52f, 0.5f), 0f), new GradientColorKey(new Color(0.8f, 0.78f, 0.76f), 0.22f), new GradientColorKey(new Color(0.9f, 0.89f, 0.88f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.08f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.04f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sortMode = ParticleSystemSortMode.Distance;
            r.maxParticleSize = 2.5f;
            ps.Play(true);
            return ps;
        }

        static Material mistMat;

        /// Low mist drifting round an island (Fog Isle): ≤ 40 huge, very faint particles in a flat box around `center`.
        public static ParticleSystem CreateMist(Transform parent, Vector3 center, float radius)
        {
            if (mistMat == null) mistMat = ParticleMat("Island_Mist", new Color(0.93f, 0.91f, 0.97f, 1f));
            var ps = Create(parent, "IslandMist", center, mistMat, 40);
            var main = ps.main;
            main.duration = 24f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(18f, 26f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(8f, 14f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = ps.emission;
            em.rateOverTime = 1.5f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(radius * 2f, radius * 2f, 2f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.2f, 0.3f), new GradientAlphaKey(0.2f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.05f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.maxParticleSize = 2f;
            ps.Play(true);
            return ps;
        }

        /// A small steam vent: white puffs (0.7–1.4 m × scale), 3–5.5 s.
        public static ParticleSystem CreateSteam(Transform parent, Vector3 pos, float scale)
        {
            if (steamMat == null) steamMat = ParticleMat("Volcano_Steam", new Color(0.96f, 0.96f, 0.95f, 1f));
            var ps = Create(parent, "VolcanoSteam", pos, steamMat, 36);
            var main = ps.main;
            main.duration = 5f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f * scale, 1.6f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.7f * scale, 1.4f * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.02f;
            var em = ps.emission;
            em.rateOverTime = 5f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 14f;
            sh.radius = 0.3f * scale;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 3f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.4f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            ps.Play(true);
            return ps;
        }
    }
}
