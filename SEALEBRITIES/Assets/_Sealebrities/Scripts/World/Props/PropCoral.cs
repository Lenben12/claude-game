using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    public enum PropCoralType { Branching, Brain, Table, Fan, Sponge, Anemone, Whip }

    /// Reusable low-poly coral variants (built once, instanced into the per-site batches): boxy branching corals,
    /// faceted brain lumps, flat faceted table corals, triangle-fan sea fans, barrel sponges, cone-cluster anemones
    /// and thin sea whips – all in the bright reef palette.
    public static class PropCoralKit
    {
        public static readonly List<PropModel> Branching = new List<PropModel>();
        public static readonly List<PropModel> Brain = new List<PropModel>();
        public static readonly List<PropModel> Table = new List<PropModel>();
        public static readonly List<PropModel> Fan = new List<PropModel>();
        public static readonly List<PropModel> Sponge = new List<PropModel>();
        public static readonly List<PropModel> Anemone = new List<PropModel>();
        public static readonly List<PropModel> Whip = new List<PropModel>();
        public static bool Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Branching.Clear(); Brain.Clear(); Table.Clear(); Fan.Clear(); Sponge.Clear(); Anemone.Clear(); Whip.Clear();
            Ready = false;
        }

        public static List<PropModel> Of(PropCoralType t)
        {
            switch (t)
            {
                case PropCoralType.Branching: return Branching;
                case PropCoralType.Brain: return Brain;
                case PropCoralType.Table: return Table;
                case PropCoralType.Fan: return Fan;
                case PropCoralType.Sponge: return Sponge;
                case PropCoralType.Anemone: return Anemone;
                default: return Whip;
            }
        }

        public static IEnumerator Prepare()
        {
            if (Ready) yield break;
            for (int i = 0; i < 8; i++) Branching.Add(BuildBranching(100 + i, PropMats.CoralColor(i)));
            if (PropRun.Due()) yield return null;
            for (int i = 0; i < 4; i++) Brain.Add(BuildBrain(200 + i, PropMats.BrainColor(i)));
            for (int i = 0; i < 4; i++) Table.Add(BuildTable(300 + i, PropMats.TableColor(i)));
            for (int i = 0; i < 5; i++) Fan.Add(BuildFan(400 + i, PropMats.Fan(i)));
            for (int i = 0; i < 5; i++) Sponge.Add(BuildSponge(500 + i, PropMats.Sponge(i)));
            if (PropRun.Due()) yield return null;
            for (int i = 0; i < 3; i++) Anemone.Add(BuildAnemone(600 + i));
            for (int i = 0; i < 6; i++) Whip.Add(BuildWhip(700 + i, PropMats.Whip(i)));
            Ready = true;
        }

        // ------------------------------------------------------------------ builders
        /// Branching coral in three flat tones (art bible §2.4): darker base (−25 %), mid branches, lighter tips (+15 %).
        static PropModel BuildBranching(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var tones = new[] { mb.B(PropMats.ReefTip(c)), mb.B(PropMats.Flat(c)), mb.B(PropMats.ReefBase(c)) };
            int trunks = 3 + rng.Next(3);
            float maxH = 0f;
            for (int t = 0; t < trunks; t++)
            {
                Vector3 dir = Quaternion.Euler(PropUtil.Range(rng, 10f, 45f), PropUtil.Range(rng, 0f, 360f), 0f) * Vector3.up;
                Vector3 p = new Vector3(PropUtil.Range(rng, -0.07f, 0.07f), -0.05f, PropUtil.Range(rng, -0.07f, 0.07f));
                Branch(tones, rng, p, dir, PropUtil.Range(rng, 0.24f, 0.34f), 0.07f, 2, ref maxH);
            }
            return mb.Build("CoralBranch" + seed, maxH, 0.6f);
        }

        /// Boxy (4-sided) branch segments; tips are capped. tones[depth] = 0 tips, 1 middle, 2 base.
        static void Branch(PropMeshBuilder[] tones, System.Random rng, Vector3 p, Vector3 dir, float len, float r, int depth, ref float maxH)
        {
            Vector3 end = p + dir * len;
            tones[Mathf.Clamp(depth, 0, 2)].Rod(p, end, r, r * 0.82f, 4, depth == 0);
            maxH = Mathf.Max(maxH, end.y + r);
            if (depth <= 0) return;
            int n = 2 + (rng.NextDouble() < 0.35 ? 1 : 0);
            for (int k = 0; k < n; k++)
            {
                Vector3 rnd = new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, -0.2f, 1f), PropUtil.Range(rng, -1f, 1f));
                if (rnd.sqrMagnitude < 1e-4f) rnd = Vector3.up;
                Vector3 nd = Vector3.Slerp(dir, rnd.normalized, PropUtil.Range(rng, 0.3f, 0.55f)) + Vector3.up * 0.35f;
                nd = nd.sqrMagnitude > 1e-6f ? nd.normalized : Vector3.up;
                Branch(tones, rng, end - dir * r * 0.5f, nd, len * PropUtil.Range(rng, 0.7f, 0.9f), r * 0.8f, depth - 1, ref maxH);
            }
        }

        static readonly PropMeshBuilder Scratch = new PropMeshBuilder();

        /// Faceted brain coral (half ico, detail 1) in two tones: lighter crown, base-colored flanks.
        static PropModel BuildBrain(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var radii = PropRocks.Jitter(rng, 1, 0.1f, 1, 0.35f);
            Scratch.Clear();
            Scratch.Ico(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.38f, 0.5f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), 1, radii);
            var top = mb.B(PropMats.ReefTip(c));
            var mid = mb.B(PropMats.Flat(c));
            var low = mb.B(PropMats.ReefBase(c));
            var V = Scratch.V;
            var T = Scratch.T;
            for (int t = 0; t + 2 < T.Count; t += 3)
            {
                Vector3 a = V[T[t]], b = V[T[t + 1]], cc = V[T[t + 2]];
                Vector3 n = Vector3.Cross(b - a, cc - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                // checker the crown faces a little so the brain grooves read in two tones
                var dst = n.y > 0.55f ? ((t / 3) % 3 == 0 ? mid : top) : (n.y < 0.05f ? low : mid);
                dst.Face3(a, b, cc, n);
            }
            return mb.Build("CoralBrain" + seed, 0.42f, 0.5f);
        }

        /// Faceted table coral: a plate 0.12 m thick (not a paper-flat disc) on a short stem, lighter top, darker rim.
        static PropModel BuildTable(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var topB = mb.B(PropMats.ReefTip(c));
            var rimB = mb.B(PropMats.Flat(c));
            var baseB = mb.B(PropMats.ReefBase(c));
            float R = 0.8f;
            baseB.Cylinder(new Vector3(0f, -0.05f, 0f), Quaternion.identity, 0.13f, 0.09f, 0.34f, 5);
            const int n = 8;
            var rimTop = new Vector3[n];
            var rimBot = new Vector3[n];
            float a0 = PropUtil.Range(rng, 0f, 1f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / n;
                float r = R * PropUtil.Range(rng, 0.8f, 1.05f);
                float y = 0.4f + PropUtil.Range(rng, -0.03f, 0.05f);
                rimTop[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                rimBot[i] = new Vector3(Mathf.Cos(a) * r * 0.94f, y - 0.12f, Mathf.Sin(a) * r * 0.94f);
            }
            Vector3 ct = new Vector3(0f, 0.37f, 0f), cb = new Vector3(0f, 0.27f, 0f);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                topB.Face3(ct, rimTop[i], rimTop[j], Vector3.up);
                baseB.Face3(cb, rimBot[i], rimBot[j], Vector3.down);
                Vector3 outward = (rimTop[i] + rimTop[j]) * 0.5f;
                outward.y = 0f;
                rimB.QuadAuto(rimTop[i], rimTop[j], rimBot[j], rimBot[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
            }
            return mb.Build("CoralTable" + seed, 0.46f, R);
        }

        /// Sea fan: a flat fan of triangles (inner fan + outer band) with slightly jittered facets, two-sided.
        static PropModel BuildFan(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var b = mb.B(mat);
            float W = PropUtil.Range(rng, 0.5f, 0.75f), H = PropUtil.Range(rng, 0.7f, 1.05f);
            const int arcs = 6;
            var inner = new Vector3[arcs + 1];
            var outer = new Vector3[arcs + 1];
            Vector3 root = new Vector3(0f, 0.14f, 0f);
            for (int j = 0; j <= arcs; j++)
            {
                float th = Mathf.Lerp(-72f, 72f, (float)j / arcs) * Mathf.Deg2Rad;
                float ro = PropUtil.Range(rng, 0.88f, 1.02f);
                inner[j] = root + new Vector3(Mathf.Sin(th) * W * 0.5f, Mathf.Cos(th) * H * 0.5f, PropUtil.Range(rng, -0.03f, 0.03f));
                outer[j] = root + new Vector3(Mathf.Sin(th) * W * ro, Mathf.Cos(th) * H * ro, PropUtil.Range(rng, -0.05f, 0.05f));
            }
            for (int j = 0; j < arcs; j++)
            {
                b.Tri2(root, inner[j], inner[j + 1], Vector3.forward);
                b.Tri2(inner[j], outer[j], outer[j + 1], Vector3.forward);
                b.Tri2(inner[j], outer[j + 1], inner[j + 1], Vector3.forward);
            }
            mb.B(PropMats.Coral(1)).Rod(new Vector3(0f, -0.05f, 0f), root + Vector3.up * 0.04f, 0.035f, 0.025f, 4);
            return mb.Build("SeaFan" + seed, 0.14f + H, W);
        }

        /// Barrel / vase sponges: open 6-sided tubes with a dark inside.
        static PropModel BuildSponge(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            int n = 2 + rng.Next(4);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float h = PropUtil.Range(rng, 0.3f, 0.9f);
                float r0 = PropUtil.Range(rng, 0.06f, 0.1f), r1 = r0 + PropUtil.Range(rng, 0.02f, 0.06f);
                Vector3 p = new Vector3(PropUtil.Range(rng, -0.16f, 0.16f), -0.05f, PropUtil.Range(rng, -0.16f, 0.16f));
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -14f, 14f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -14f, 14f));
                mb.B(mat).Cylinder(p, q, r0, r1, h, 6);
                var ins = mb.B(PropMats.SpongeInside);
                ins.Cylinder(p + q * new Vector3(0f, h * 0.35f, 0f), q, Mathf.Lerp(r0, r1, 0.35f) - 0.02f, r1 - 0.02f, h * 0.65f, 6, false, false, 1f, 1f, 0f, true);
                ins.Disk(p + q * new Vector3(0f, h * 0.35f, 0f), Mathf.Lerp(r0, r1, 0.35f) - 0.02f, 6, q, true);
                // rim
                mb.B(mat).Ring(p + q * new Vector3(0f, h, 0f), r1 - 0.025f, r1, 6, q);
                maxH = Mathf.Max(maxH, h);
            }
            return mb.Build("Sponge" + seed, maxH, 0.3f);
        }

        /// Anemone: short 6-sided foot with a cluster of short cones (tentacles) on top.
        static PropModel BuildAnemone(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            mb.B(PropMats.AnemoneBody).Cylinder(new Vector3(0f, -0.03f, 0f), Quaternion.identity, 0.11f, 0.14f, 0.16f, 6, false, true);
            var tb = mb.B(PropMats.AnemoneTentacle);
            int count = 16 + rng.Next(8);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f;
                float rr = Mathf.Lerp(0.02f, 0.12f, Mathf.Sqrt((float)i / count));
                Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 bp = outward * rr + Vector3.up * 0.12f;
                float spread = Mathf.Lerp(0.2f, 1.0f, rr / 0.12f);
                Vector3 dir = (outward * spread + Vector3.up).normalized;
                float len = PropUtil.Range(rng, 0.1f, 0.17f);
                tb.Cylinder(bp, Quaternion.FromToRotation(Vector3.up, dir), 0.026f, 0f, len, 4, false, false, 1f, 1f, 0f, false, PropUtil.Range(rng, 0f, 90f));
            }
            return mb.Build("Anemone" + seed, 0.32f, 0.3f);
        }

        static PropModel BuildWhip(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float h = PropUtil.Range(rng, 0.9f, 2.1f);
            Vector3 bend = new Vector3(PropUtil.Range(rng, -1f, 1f), 0f, PropUtil.Range(rng, -1f, 1f));
            bend = (bend.sqrMagnitude > 1e-4f ? bend.normalized : Vector3.right) * h * PropUtil.Range(rng, 0.1f, 0.3f);
            var pts = new Vector3[4];
            var rad = new float[4];
            for (int i = 0; i < 4; i++)
            {
                float t = i / 3f;
                pts[i] = Vector3.up * (h * t) + bend * (t * t) + new Vector3(Mathf.Sin(t * 5f + seed) * 0.03f, -0.05f, 0f);
                rad[i] = Mathf.Lerp(0.03f, 0.012f, t);
            }
            mb.B(mat).Tube(pts, rad, 4, 1f, 1f, true);
            return mb.Build("Whip" + seed, h, 0.2f);
        }
    }

    /// The house reef: coral heads, scattered corals, anemone clusters, sea grass and rocks.
    public static class PropReef
    {
        static float G(float x, float z) => PropGround.H(x, z);

        /// Places one coral of the given type. Returns false when it would reach too close to the surface.
        public static bool Place(PropBatch batch, System.Random rng, Vector3 pos, Vector3 normal, PropCoralType type, float scaleMul, float maxTopY = -1.2f)
        {
            var list = PropCoralKit.Of(type);
            if (list.Count == 0) return false;
            var model = list[rng.Next(list.Count)];
            float s;
            switch (type)
            {
                case PropCoralType.Branching: s = PropUtil.Range(rng, 0.7f, 1.7f); break;
                case PropCoralType.Brain: s = PropUtil.Range(rng, 0.6f, 2.0f); break;
                case PropCoralType.Table: s = PropUtil.Range(rng, 0.7f, 1.7f); break;
                case PropCoralType.Fan: s = PropUtil.Range(rng, 0.8f, 1.6f); break;
                case PropCoralType.Sponge: s = PropUtil.Range(rng, 0.7f, 1.5f); break;
                case PropCoralType.Anemone: s = PropUtil.Range(rng, 0.8f, 1.4f); break;
                default: s = PropUtil.Range(rng, 0.8f, 1.3f); break;
            }
            s *= scaleMul;
            float top = pos.y + model.height * s;
            if (top > maxTopY)
            {
                s *= Mathf.Max(0f, (maxTopY - pos.y)) / Mathf.Max(0.01f, model.height * s);
                if (s < 0.35f) return false;
            }
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            float follow = type == PropCoralType.Fan || type == PropCoralType.Whip ? 0.25f : 0.6f;
            Quaternion rot = PropUtil.OnGround(normal.normalized, follow, PropUtil.Range(rng, 0f, 360f));
            batch.Add(model, Matrix4x4.TRS(pos - normal * 0.03f, rot, Vector3.one * s));
            return true;
        }

        public static PropCoralType RandomType(System.Random rng, float branching, float brain, float table, float fan, float sponge, float whip)
        {
            float total = branching + brain + table + fan + sponge + whip;
            float r = (float)rng.NextDouble() * total;
            if ((r -= branching) < 0f) return PropCoralType.Branching;
            if ((r -= brain) < 0f) return PropCoralType.Brain;
            if ((r -= table) < 0f) return PropCoralType.Table;
            if ((r -= fan) < 0f) return PropCoralType.Fan;
            if ((r -= sponge) < 0f) return PropCoralType.Sponge;
            return PropCoralType.Whip;
        }

        /// Thin and flat things do not need shadow casting.
        public static void QuietShadows(PropBatch batch)
        {
            for (int i = 0; i < PropMats.FanCount; i++) batch.SetShadows(PropMats.Fan(i), ShadowCastingMode.Off);
            for (int i = 0; i < PropMats.WhipCount; i++) batch.SetShadows(PropMats.Whip(i), ShadowCastingMode.Off);
            batch.SetShadows(PropMats.AnemoneTentacle, ShadowCastingMode.Off);
            batch.SetShadows(PropMats.SeaGrass, ShadowCastingMode.Off);
            batch.SetShadows(PropMats.SeaGrassDark, ShadowCastingMode.Off);
            batch.SetShadows(PropMats.Kelp, ShadowCastingMode.Off);
        }

        public static IEnumerator Build(Transform decor, DiveSite site)
        {
            var rng = new System.Random(5150);
            var batch = new PropBatch(60f);
            var small = new PropBatch(60f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);
            Vector3 c = site.center;

            var keepOut = new PropExclusion(16f);
            keepOut.AddLine(WorldLayout.DockStart, WorldLayout.DockEnd + new Vector3(8f, 0f, 0f), 7f);
            keepOut.Add(WorldLayout.BoatSpawn.x, WorldLayout.BoatSpawn.z, 11f);
            keepOut.Add(WorldLayout.FishingSpot.x, WorldLayout.FishingSpot.z, 6f);
            var spacing = new PropExclusion(12f);
            float minD = Mathf.Max(2.4f, site.minDepth - 0.8f), maxD = site.maxDepth + 4f;

            bool Valid(float x, float z, out float g)
            {
                g = G(x, z);
                float d = -g;
                return d >= minD && d <= maxD && !keepOut.Blocked(x, z);
            }

            // ---------------------------------------------------------------- coral heads (bommies)
            int bommies = 0, attempts = 0;
            while (bommies < 48 && attempts < 4000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius * 0.95f);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || spacing.Blocked(x, z)) continue;
                float s = Mathf.Min(PropUtil.Range(rng, 0.8f, 2.3f), (-1.6f - g) / 0.9f);
                if (s >= 0.5f)
                {
                    Vector3 sc = new Vector3(s * PropUtil.Range(rng, 1.1f, 1.5f), s * 0.8f, s * PropUtil.Range(rng, 0.9f, 1.3f));
                    Quaternion rr = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                    Vector3 rp = new Vector3(x, g - s * 0.2f, z);
                    var rockMat = rng.NextDouble() < 0.7 ? PropMats.ReefRock : PropMats.ReefRockDark;
                    if (s > 1.4f)
                    {
                        var mesh = PropRocks.Mesh(sc, rng.Next(), 1, 0.16f);
                        batch.Add(mesh, rockMat, Matrix4x4.TRS(rp, rr, Vector3.one));
                        PropCol.Mesh(cols, mesh, rp, rr, Vector3.one, "ReefRock");
                    }
                    else PropRocks.Build(batch.B(rockMat, rp), rp, sc, rr, rng.Next(), 1, 0.16f);
                    // corals on top of the head
                    int onTop = 2 + rng.Next(3);
                    for (int k = 0; k < onTop; k++)
                    {
                        Vector2 to = PropUtil.InDisc(rng, s * 0.6f);
                        Vector3 tp = rp + new Vector3(to.x, sc.y * 0.8f, to.y);
                        Place(batch, rng, tp, Vector3.up, RandomType(rng, 4, 3, 2, 1, 2, 0), 0.7f, -1.3f);
                    }
                }
                int around = 9 + rng.Next(10);
                for (int k = 0; k < around; k++)
                {
                    Vector2 ao = PropUtil.InDisc(rng, s * 1.6f + 2f);
                    float ax = x + ao.x, az = z + ao.y;
                    if (!Valid(ax, az, out float ag)) continue;
                    Vector3 n = PropGround.Normal(ax, az, 0.8f);
                    Place(batch, rng, new Vector3(ax, ag, az), n, RandomType(rng, 5, 3, 2, 2, 3, 1), 1f);
                }
                spacing.Add(x, z, s * 2f + 4f);
                bommies++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- coral patches 6–12 m across with sand gaps
            // (art bible §2.4: grouped colonies, about one per 3 m², instead of evenly sprinkled "confetti")
            int coralPatches = 0;
            attempts = 0;
            while (coralPatches < 24 && attempts < 3000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius * 0.95f);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || spacing.Blocked(x, z)) continue;
                float pr = PropUtil.Range(rng, 3f, 6f);
                int n = Mathf.RoundToInt(Mathf.PI * pr * pr / 3f);
                for (int k = 0; k < n; k++)
                {
                    Vector2 po = PropUtil.InDisc(rng, pr);
                    float px = x + po.x, pz = z + po.y;
                    if (!Valid(px, pz, out float pg)) continue;
                    Vector3 nrm = PropGround.Normal(px, pz, 0.8f);
                    Place(batch, rng, new Vector3(px, pg, pz), nrm, RandomType(rng, 5, 3, 1, 2, 3, 1), PropUtil.Range(rng, 0.75f, 1.2f));
                }
                spacing.Add(x, z, pr + 3.5f);
                coralPatches++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- anemone clusters (clownfish homes)
            int anemones = 0;
            attempts = 0;
            while (anemones < 9 && attempts < 3000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius * 0.8f);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || -g > 10f) continue;
                Vector3 p = new Vector3(x, g, z);
                float rs = Mathf.Min(0.7f, (-1.8f - g) / 1.2f);
                if (rs > 0.3f) PropRocks.Build(batch.B(PropMats.ReefRock, p), p - Vector3.up * 0.1f, new Vector3(rs * 1.4f, rs * 0.6f, rs * 1.2f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0);
                int n = 3 + rng.Next(4);
                for (int k = 0; k < n; k++)
                {
                    Vector2 ao = PropUtil.InDisc(rng, 0.9f);
                    float ax = x + ao.x, az = z + ao.y;
                    float ag = Mathf.Max(G(ax, az), g + (ao.magnitude < 0.5f && rs > 0.3f ? rs * 0.5f : 0f));
                    Place(batch, rng, new Vector3(ax, ag, az), Vector3.up, PropCoralType.Anemone, 1f, -1.0f);
                }
                // a small, dense coral garden around every clownfish home: the spots divers swim to (and the reef
                // screenshot) read as a colorful reef, not as single corals on bare sand
                int garden = 8 + rng.Next(5);
                for (int k = 0; k < garden; k++)
                {
                    Vector2 go = PropUtil.InDisc(rng, 4.2f);
                    if (go.sqrMagnitude < 1.3f * 1.3f) continue;
                    float gx = x + go.x, gz = z + go.y;
                    if (!Valid(gx, gz, out float gg)) continue;
                    Place(batch, rng, new Vector3(gx, gg, gz), PropGround.Normal(gx, gz, 0.8f), RandomType(rng, 5, 3, 1, 2, 3, 1), PropUtil.Range(rng, 0.9f, 1.3f));
                }
                PropInfo.AnemoneSpots.Add(p + Vector3.up * 0.3f);
                anemones++;
            }

            // ---------------------------------------------------------------- sea grass patches
            int patches = 0;
            attempts = 0;
            while (patches < 15 && attempts < 3000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || -g > 8f) continue;
                SeaGrassPatch(small, rng, new Vector3(x, g, z), PropUtil.Range(rng, 2.5f, 5f), keepOut);
                patches++;
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- loose rocks
            for (int i = 0; i < 45; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g)) continue;
                float s = Mathf.Min(PropUtil.Range(rng, 0.25f, 0.9f), (-1.5f - g));
                if (s < 0.2f) continue;
                Vector3 p = new Vector3(x, g - s * 0.2f, z);
                PropRocks.Build(small.B(PropMats.ReefRock, p), p, new Vector3(s * 1.3f, s * 0.7f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0);
            }

            QuietShadows(batch);
            batch.Flush(decor, "ReefCorals", ShadowCastingMode.On, 170f);
            if (PropRun.Due()) yield return null;
            small.Flush(decor, "ReefDetail", ShadowCastingMode.Off, 80f);
        }

        /// Patch of sea grass: flat two-sided triangle blades in two greens, built directly into the batch.
        public static void SeaGrassPatch(PropBatch batch, System.Random rng, Vector3 center, float radius, PropExclusion keepOut)
        {
            var b0 = batch.B(PropMats.SeaGrass, center);
            var b1 = batch.B(PropMats.SeaGrassDark, center);
            int blades = (int)(radius * radius * 9f);
            for (int i = 0; i < blades; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius);
                float x = center.x + o.x, z = center.z + o.y;
                if (keepOut != null && keepOut.Blocked(x, z)) continue;
                float g = G(x, z);
                float edge = 1f - o.magnitude / radius;
                float h = PropUtil.Range(rng, 0.3f, 0.7f) * (0.5f + 0.5f * edge);
                if (g + h > -1.2f) continue;
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropUtil.Range(rng, 0.05f, 0.3f);
                Vector3 side = new Vector3(-Mathf.Sin(a + 1.2f), 0f, Mathf.Cos(a + 1.2f)) * PropUtil.Range(rng, 0.03f, 0.05f);
                Vector3 p0 = new Vector3(x, g - 0.03f, z);
                Vector3 tip = p0 + Vector3.up * h + lean * h;
                Vector3 n = Vector3.Cross(side, tip - p0);
                if (n.sqrMagnitude < 1e-10f) continue;
                (i % 3 == 0 ? b1 : b0).Tri2(p0 - side, p0 + side, tip, n.normalized);
            }
        }
    }
}
