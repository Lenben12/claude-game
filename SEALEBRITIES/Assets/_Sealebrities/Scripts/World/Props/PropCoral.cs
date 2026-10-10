using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    public enum PropCoralType { Branching, Brain, Table, Fan, Sponge, Anemone, Whip }

    /// Reusable coral/sponge/anemone/sea-whip variants (built once, instanced into the per-site batches).
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
            for (int i = 0; i < 8; i++) { Branching.Add(BuildBranching(100 + i, PropMats.Coral(i))); if (PropRun.Due()) yield return null; }
            for (int i = 0; i < 4; i++) { Brain.Add(BuildBrain(200 + i, PropMats.Brain(i))); if (PropRun.Due()) yield return null; }
            for (int i = 0; i < 4; i++) Table.Add(BuildTable(300 + i, PropMats.Table(i)));
            for (int i = 0; i < 5; i++) Fan.Add(BuildFan(400 + i, PropMats.Fan(i)));
            for (int i = 0; i < 5; i++) Sponge.Add(BuildSponge(500 + i, PropMats.Sponge(i)));
            if (PropRun.Due()) yield return null;
            for (int i = 0; i < 3; i++) { Anemone.Add(BuildAnemone(600 + i)); if (PropRun.Due()) yield return null; }
            for (int i = 0; i < 6; i++) Whip.Add(BuildWhip(700 + i, PropMats.Whip(i)));
            Ready = true;
        }

        // ------------------------------------------------------------------ builders
        static PropModel BuildBranching(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var b = mb.B(mat);
            int trunks = 3 + rng.Next(3);
            float maxH = 0f;
            for (int t = 0; t < trunks; t++)
            {
                Vector3 dir = Quaternion.Euler(PropUtil.Range(rng, 12f, 48f), PropUtil.Range(rng, 0f, 360f), 0f) * Vector3.up;
                Vector3 p = new Vector3(PropUtil.Range(rng, -0.06f, 0.06f), 0f, PropUtil.Range(rng, -0.06f, 0.06f));
                Branch(b, rng, p, dir, PropUtil.Range(rng, 0.24f, 0.34f), 0.045f, 2, ref maxH);
            }
            return mb.Build("CoralBranch" + seed, maxH, 0.6f);
        }

        static readonly Vector3[] Seg3 = new Vector3[3];
        static readonly float[] Rad3 = new float[3];

        static void Branch(PropMeshBuilder b, System.Random rng, Vector3 p, Vector3 dir, float len, float r, int depth, ref float maxH)
        {
            Vector3 side = Vector3.Cross(dir, Vector3.right);
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir, Vector3.forward);
            side.Normalize();
            Vector3 end = p + dir * len;
            Seg3[0] = p;
            Seg3[1] = p + dir * len * 0.5f + side * len * PropUtil.Range(rng, -0.08f, 0.08f);
            Seg3[2] = end;
            Rad3[0] = r; Rad3[1] = r * 0.9f; Rad3[2] = r * 0.8f;
            b.Tube(Seg3, Rad3, 4, 1f, 1f, depth == 0);
            maxH = Mathf.Max(maxH, end.y + r);
            if (depth <= 0) return;
            int n = 2 + (rng.NextDouble() < 0.35 ? 1 : 0);
            for (int k = 0; k < n; k++)
            {
                Vector3 rnd = new Vector3(PropUtil.Range(rng, -1f, 1f), PropUtil.Range(rng, -0.2f, 1f), PropUtil.Range(rng, -1f, 1f)).normalized;
                Vector3 nd = Vector3.Slerp(dir, rnd, PropUtil.Range(rng, 0.3f, 0.55f));
                nd = (nd + Vector3.up * 0.35f).normalized;
                Branch(b, rng, end, nd, len * PropUtil.Range(rng, 0.7f, 0.9f), r * 0.76f, depth - 1, ref maxH);
            }
        }

        static PropModel BuildBrain(int seed, Material mat)
        {
            var mb = new PropModelBuilder();
            Vector3 off = new Vector3(seed * 0.37f % 50f, seed * 0.11f % 50f, 3f);
            mb.B(mat).Blob(Vector3.zero, new Vector3(0.5f, 0.38f, 0.5f), Quaternion.identity, 9, d =>
            {
                float r = 1f + (PropNoise.Fbm3(d * 2.2f + off, 3, seed) - 0.5f) * 0.22f;
                if (d.y < 0f) r *= Mathf.Lerp(1f, 0.3f, -d.y);
                return r;
            }, 1f);
            return mb.Build("CoralBrain" + seed, 0.4f, 0.5f);
        }

        static PropModel BuildTable(int seed, Material mat)
        {
            var mb = new PropModelBuilder();
            float R = 0.85f;
            var prof = new[]
            {
                new Vector2(0.08f, 0f), new Vector2(0.055f, 0.18f), new Vector2(0.11f, 0.26f), new Vector2(R * 0.55f, 0.29f),
                new Vector2(R * 0.92f, 0.33f), new Vector2(R, 0.37f), new Vector2(R * 0.95f, 0.385f), new Vector2(R * 0.55f, 0.335f),
                new Vector2(0.1f, 0.315f), new Vector2(0f, 0.315f),
            };
            float ph = seed * 1.7f;
            mb.B(mat).Lathe(Vector3.zero, Quaternion.identity, prof, 24, 3f, 1f, (a, t) =>
                t < 0.3f ? 1f : 1f + 0.09f * Mathf.Sin(a * Mathf.PI * 2f * 5f + ph) + 0.06f * (PropNoise.Value(a * 9f, ph, seed, 9) - 0.5f));
            return mb.Build("CoralTable" + seed, 0.4f, R);
        }

        static PropModel BuildFan(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var b = mb.B(mat);
            float W = PropUtil.Range(rng, 0.5f, 0.75f), H = PropUtil.Range(rng, 0.7f, 1.05f);
            const int rings = 5, arcs = 12;
            int vs = b.VertexCount;
            for (int i = 0; i <= rings; i++)
            {
                float r = Mathf.Lerp(0.05f, 1f, (float)i / rings);
                for (int j = 0; j <= arcs; j++)
                {
                    float th = Mathf.Lerp(-75f, 75f, (float)j / arcs) * Mathf.Deg2Rad;
                    float x = Mathf.Sin(th) * r * W, y = 0.12f + Mathf.Cos(th) * r * H;
                    float z = Mathf.Sin(r * 3f + th * 2f) * 0.03f * r;
                    b.Vert(new Vector3(x, y, z), Vector3.forward, new Vector2(x / W * 0.5f + 0.5f, y / H));
                }
            }
            for (int i = 0; i < rings; i++)
            {
                for (int j = 0; j < arcs; j++)
                {
                    int a = vs + i * (arcs + 1) + j;
                    int c = a + arcs + 1;
                    b.Quad(a, c, c + 1, a + 1);
                }
            }
            mb.B(PropMats.Coral(5)).Rod(new Vector3(0f, -0.05f, 0f), new Vector3(0f, 0.17f, 0f), 0.025f, 0.018f, 5);
            return mb.Build("SeaFan" + seed, 0.12f + H, W);
        }

        static PropModel BuildSponge(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            int n = 3 + rng.Next(4);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float h = PropUtil.Range(rng, 0.3f, 0.95f);
                float r0 = PropUtil.Range(rng, 0.06f, 0.09f), r1 = r0 + PropUtil.Range(rng, 0.01f, 0.05f);
                var prof = new[] { new Vector2(r0, -0.05f), new Vector2(r1, h), new Vector2(r1 - 0.025f, h), new Vector2(r1 - 0.035f, h * 0.35f) };
                Vector3 p = new Vector3(PropUtil.Range(rng, -0.16f, 0.16f), 0f, PropUtil.Range(rng, -0.16f, 0.16f));
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -14f, 14f), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, -14f, 14f));
                mb.B(mat).Lathe(p, q, prof, 10, 1.2f, 1f);
                maxH = Mathf.Max(maxH, h);
            }
            return mb.Build("Sponge" + seed, maxH, 0.3f);
        }

        static PropModel BuildAnemone(int seed)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            mb.B(PropMats.AnemoneBody).Lathe(Vector3.zero, Quaternion.identity,
                new[] { new Vector2(0.1f, -0.03f), new Vector2(0.12f, 0.07f), new Vector2(0.135f, 0.13f), new Vector2(0.07f, 0.15f), new Vector2(0f, 0.15f) }, 14, 1f, 1f);
            var tb = mb.B(PropMats.AnemoneTentacle);
            int count = 64 + rng.Next(20);
            var pts = new Vector3[3];
            var rad = new[] { 0.012f, 0.009f, 0.004f };
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f;
                float rr = Mathf.Lerp(0.025f, 0.13f, Mathf.Sqrt((float)i / count));
                Vector3 outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 bp = outward * rr + Vector3.up * 0.145f;
                float len = PropUtil.Range(rng, 0.1f, 0.17f);
                float spread = Mathf.Lerp(0.3f, 0.9f, rr / 0.13f);
                pts[0] = bp;
                pts[1] = bp + (outward * spread + Vector3.up) .normalized * len * 0.5f;
                pts[2] = pts[1] + (outward * spread * 1.3f + Vector3.up * 0.7f + new Vector3(PropUtil.Range(rng, -0.3f, 0.3f), 0f, PropUtil.Range(rng, -0.3f, 0.3f))).normalized * len * 0.5f;
                tb.Tube(pts, rad, 3, 1f, 1f, true);
            }
            return mb.Build("Anemone" + seed, 0.32f, 0.3f);
        }

        static PropModel BuildWhip(int seed, Material mat)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float h = PropUtil.Range(rng, 0.9f, 2.1f);
            Vector3 bend = new Vector3(PropUtil.Range(rng, -1f, 1f), 0f, PropUtil.Range(rng, -1f, 1f)).normalized * h * PropUtil.Range(rng, 0.1f, 0.3f);
            var pts = new Vector3[6];
            var rad = new float[6];
            for (int i = 0; i < 6; i++)
            {
                float t = i / 5f;
                pts[i] = Vector3.up * (h * t) + bend * (t * t) + new Vector3(Mathf.Sin(t * 5f + seed) * 0.03f, 0f, 0f);
                rad[i] = Mathf.Lerp(0.018f, 0.006f, t);
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
            float follow = type == PropCoralType.Fan || type == PropCoralType.Whip ? 0.25f : 0.6f;
            Quaternion rot = PropUtil.OnGround(normal, follow, PropUtil.Range(rng, 0f, 360f));
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

        public static void QuietShadows(PropBatch batch)
        {
            for (int i = 0; i < PropMats.FanCount; i++) batch.SetShadows(PropMats.Fan(i), ShadowCastingMode.Off);
            for (int i = 0; i < PropMats.WhipCount; i++) batch.SetShadows(PropMats.Whip(i), ShadowCastingMode.Off);
            batch.SetShadows(PropMats.AnemoneTentacle, ShadowCastingMode.Off);
            batch.SetShadows(PropMats.SeaGrass, ShadowCastingMode.Off);
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
                Vector3 p = new Vector3(x, g, z);
                if (s >= 0.5f)
                {
                    Vector3 sc = new Vector3(s * PropUtil.Range(rng, 1.1f, 1.5f), s * 0.8f, s * PropUtil.Range(rng, 0.9f, 1.3f));
                    Quaternion rr = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                    Vector3 rp = p - Vector3.up * s * 0.2f;
                    if (s > 1.4f)
                    {
                        var mesh = PropRocks.Mesh(sc, rng.Next(), 6, 0.22f);
                        batch.Add(mesh, PropMats.RockLight, Matrix4x4.TRS(rp, rr, Vector3.one));
                        PropCol.Mesh(cols, mesh, rp, rr, Vector3.one, "ReefRock");
                    }
                    else PropRocks.Build(batch.B(PropMats.RockLight, rp), rp, sc, rr, rng.Next(), 6, 0.22f);
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

            // ---------------------------------------------------------------- scattered corals
            int scattered = 0;
            attempts = 0;
            while (scattered < 420 && attempts < 6000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g)) continue;
                Vector3 n = PropGround.Normal(x, z, 0.8f);
                if (Place(batch, rng, new Vector3(x, g, z), n, RandomType(rng, 4, 2, 2, 2, 2, 1), PropUtil.Range(rng, 0.7f, 1.1f))) scattered++;
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
                if (rs > 0.3f) PropRocks.Build(batch.B(PropMats.RockLight, p), p - Vector3.up * 0.1f, new Vector3(rs * 1.4f, rs * 0.6f, rs * 1.2f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 5);
                int n = 3 + rng.Next(4);
                for (int k = 0; k < n; k++)
                {
                    Vector2 ao = PropUtil.InDisc(rng, 0.9f);
                    float ax = x + ao.x, az = z + ao.y;
                    float ag = Mathf.Max(G(ax, az), g + (ao.magnitude < 0.5f && rs > 0.3f ? rs * 0.5f : 0f));
                    Place(batch, rng, new Vector3(ax, ag, az), Vector3.up, PropCoralType.Anemone, 1f, -1.0f);
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
                PropRocks.Build(small.B(PropMats.RockLight, p), p, new Vector3(s * 1.3f, s * 0.7f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 4);
            }

            QuietShadows(batch);
            batch.Flush(decor, "ReefCorals", ShadowCastingMode.On, 170f);
            if (PropRun.Due()) yield return null;
            small.Flush(decor, "ReefDetail", ShadowCastingMode.Off, 80f);
        }

        /// Patch of sea grass blades built directly into the batch (two-sided material).
        public static void SeaGrassPatch(PropBatch batch, System.Random rng, Vector3 center, float radius, PropExclusion keepOut)
        {
            var b = batch.B(PropMats.SeaGrass, center);
            int blades = (int)(radius * radius * 14f);
            for (int i = 0; i < blades; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius);
                float x = center.x + o.x, z = center.z + o.y;
                if (keepOut != null && keepOut.Blocked(x, z)) continue;
                float g = G(x, z);
                float edge = 1f - o.magnitude / radius;
                float h = PropUtil.Range(rng, 0.25f, 0.6f) * (0.5f + 0.5f * edge);
                if (g + h > -1.2f) continue;
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                Vector3 lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropUtil.Range(rng, 0.05f, 0.35f);
                Vector3 side = new Vector3(-Mathf.Sin(a + 1.2f), 0f, Mathf.Cos(a + 1.2f)) * 0.014f;
                Vector3 p0 = new Vector3(x, g - 0.02f, z);
                Vector3 p1 = p0 + Vector3.up * h * 0.55f + lean * h * 0.3f;
                Vector3 p2 = p0 + Vector3.up * h + lean * h;
                Vector3 n = Vector3.Cross(side, (p2 - p0)).normalized;
                int v0 = b.Vert(p0 - side, n, new Vector2(0f, 0f));
                b.Vert(p0 + side, n, new Vector2(1f, 0f));
                b.Vert(p1 - side * 0.8f, n, new Vector2(0f, 0.5f));
                b.Vert(p1 + side * 0.8f, n, new Vector2(1f, 0.5f));
                b.Vert(p2, n, new Vector2(0.5f, 1f));
                b.Quad(v0, v0 + 2, v0 + 3, v0 + 1);
                b.Tri(v0 + 2, v0 + 4, v0 + 3);
            }
        }
    }
}
