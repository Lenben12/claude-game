using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Coral colony kinds of the L.3 reef recipe (per reef colour one model each, built once).
    public enum ReefColony : byte { Branching, Brain, SmallBrain, Table, Staghorn, Pillar, Mushroom, SoftPuff, Encrust }

    /// L.3 coral kit: every colony kind in each of the 8 reef colours (PropMats.ReefColor), so a patch can use its own
    /// 3-colour sub-palette (one warm, one cool, one accent). New kinds: mushroom coral (flat dome), staghorn thicket,
    /// pillar coral, soft-coral puffs, small brains and encrusting blobs (kelp rocky reefs). Chunky, 2–3 flat tones.
    public static class PropReefKit
    {
        public const int Colors = 8;
        const int Kinds = 9;
        static readonly PropModel[,] models = new PropModel[Kinds, Colors];
        static readonly PropModel[,] modelsB = new PropModel[Kinds, Colors];
        public static bool Ready;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            System.Array.Clear(models, 0, models.Length);
            System.Array.Clear(modelsB, 0, modelsB.Length);
            Ready = false;
        }

        public static PropModel Get(ReefColony kind, int color, System.Random rng)
        {
            int c = ((color % Colors) + Colors) % Colors;
            var b = modelsB[(int)kind, c];
            if (b != null && rng != null && rng.NextDouble() < 0.5) return b;
            return models[(int)kind, c];
        }

        public static IEnumerator Prepare()
        {
            if (Ready) yield break;
            for (int c = 0; c < Colors; c++)
            {
                Color col = PropMats.ReefColor(c);
                models[(int)ReefColony.Branching, c] = Branching(1000 + c * 7, col, false);
                modelsB[(int)ReefColony.Branching, c] = Branching(1500 + c * 7, col, true);
                models[(int)ReefColony.Brain, c] = Brain(2000 + c, col, 1);
                models[(int)ReefColony.SmallBrain, c] = Brain(2500 + c, col, 0);
                models[(int)ReefColony.Table, c] = Table(3000 + c, col);
                models[(int)ReefColony.Staghorn, c] = Staghorn(4000 + c, col);
                models[(int)ReefColony.Pillar, c] = Pillar(5000 + c, col);
                models[(int)ReefColony.Mushroom, c] = Mushroom(6000 + c, col);
                models[(int)ReefColony.SoftPuff, c] = SoftPuff(7000 + c, col);
                models[(int)ReefColony.Encrust, c] = Encrust(8000 + c, col);
                if (PropRun.Due()) yield return null;
            }
            Ready = true;
        }

        /// Frees the meshes after the reefs were merged into their chunks.
        public static void Release()
        {
            for (int k = 0; k < Kinds; k++)
                for (int c = 0; c < Colors; c++)
                {
                    Free(models[k, c]);
                    Free(modelsB[k, c]);
                    models[k, c] = null;
                    modelsB[k, c] = null;
                }
            Ready = false;
        }

        static void Free(PropModel m)
        {
            if (m == null) return;
            for (int i = 0; i < m.parts.Count; i++) if (m.parts[i].mesh != null) Object.Destroy(m.parts[i].mesh);
        }

        // ------------------------------------------------------------------ builders
        static PropModel Branching(int seed, Color c, bool bushy)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var tones = new[] { mb.B(PropMats.ReefTip(c)), mb.B(PropMats.Flat(c)), mb.B(PropMats.ReefBase(c)) };
            int trunks = bushy ? 5 + rng.Next(2) : 3 + rng.Next(2);
            float maxH = 0f;
            for (int t = 0; t < trunks; t++)
            {
                Vector3 dir = Quaternion.Euler(PropUtil.Range(rng, bushy ? 25f : 10f, bushy ? 60f : 40f), t * (360f / trunks) + PropUtil.Range(rng, -20f, 20f), 0f) * Vector3.up;
                Vector3 p = new Vector3(PropUtil.Range(rng, -0.06f, 0.06f), -0.05f, PropUtil.Range(rng, -0.06f, 0.06f));
                Branch(tones, rng, p, dir, PropUtil.Range(rng, 0.22f, 0.32f), 0.065f, 2, ref maxH);
            }
            return mb.Build("ReefBranch" + seed, maxH, 0.6f);
        }

        static void Branch(PropMeshBuilder[] tones, System.Random rng, Vector3 p, Vector3 dir, float len, float r, int depth, ref float maxH)
        {
            Vector3 end = p + dir * len;
            tones[Mathf.Clamp(depth, 0, 2)].Rod(p, end, r, r * 0.82f, 4, depth == 0);
            maxH = Mathf.Max(maxH, end.y + r);
            if (depth <= 0) return;
            int n = 2;
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

        static PropModel Brain(int seed, Color c, int detail)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var radii = PropRocks.Jitter(rng, detail, 0.1f, 1, 0.35f);
            Scratch.Clear();
            Scratch.Ico(new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.38f, 0.5f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), detail, radii);
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
                if (n.y < -0.3f) continue;     // the buried underside never shows
                var dst = n.y > 0.55f ? ((t / 3) % 3 == 0 ? mid : top) : (n.y < 0.05f ? low : mid);
                dst.Face3(a, b, cc, n);
            }
            return mb.Build("ReefBrain" + seed, 0.42f, 0.5f);
        }

        static PropModel Table(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var topB = mb.B(PropMats.ReefTip(c));
            var rimB = mb.B(PropMats.Flat(c));
            var baseB = mb.B(PropMats.ReefBase(c));
            const float R = 0.8f;
            baseB.Cylinder(new Vector3(0f, -0.05f, 0f), Quaternion.identity, 0.14f, 0.09f, 0.36f, 5);
            const int n = 9;
            var rimTop = new Vector3[n];
            var rimBot = new Vector3[n];
            float a0 = PropUtil.Range(rng, 0f, 1f);
            for (int i = 0; i < n; i++)
            {
                float a = a0 + i * Mathf.PI * 2f / n;
                float r = R * PropUtil.Range(rng, 0.78f, 1.05f);
                float y = 0.42f + PropUtil.Range(rng, -0.04f, 0.06f);
                rimTop[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                rimBot[i] = new Vector3(Mathf.Cos(a) * r * 0.94f, y - 0.12f, Mathf.Sin(a) * r * 0.94f);
            }
            Vector3 ct = new Vector3(0f, 0.4f, 0f), cb = new Vector3(0f, 0.29f, 0f);
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                topB.Face3(ct, rimTop[i], rimTop[j], Vector3.up);
                baseB.Face3(cb, rimBot[i], rimBot[j], Vector3.down);
                Vector3 outward = (rimTop[i] + rimTop[j]) * 0.5f;
                outward.y = 0f;
                rimB.QuadAuto(rimTop[i], rimTop[j], rimBot[j], rimBot[i], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, outward);
            }
            return mb.Build("ReefTable" + seed, 0.5f, R);
        }

        /// Staghorn thicket: low and wide (1.5 m at scale 1), forked 4-sided antlers, tips lighter.
        static PropModel Staghorn(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var tip = mb.B(PropMats.ReefTip(c));
            var mid = mb.B(PropMats.Flat(c));
            var low = mb.B(PropMats.ReefBase(c));
            int n = 9 + rng.Next(4);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.39996f + PropUtil.Range(rng, -0.3f, 0.3f);
                float rr = Mathf.Sqrt((i + 0.5f) / n) * 0.62f;
                Vector3 p = new Vector3(Mathf.Cos(a) * rr, -0.04f, Mathf.Sin(a) * rr);
                Vector3 d = (new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropUtil.Range(rng, 0.5f, 1.1f) + Vector3.up).normalized;
                float len = PropUtil.Range(rng, 0.18f, 0.28f);
                Vector3 e = p + d * len;
                low.Rod(p, e, 0.035f, 0.03f, 4);
                for (int k = 0; k < 2; k++)
                {
                    Vector3 side = new Vector3(PropUtil.Range(rng, -1f, 1f), 0.5f, PropUtil.Range(rng, -1f, 1f)).normalized;
                    Vector3 d2 = (d + side * 0.6f).normalized;
                    Vector3 e2 = e + d2 * len * PropUtil.Range(rng, 0.8f, 1.1f);
                    mid.Rod(e, e2, 0.03f, 0.024f, 4);
                    Vector3 e3 = e2 + (d2 + Vector3.up * 0.4f).normalized * len * 0.55f;
                    tip.Rod(e2, e3, 0.024f, 0.016f, 4, true);
                    maxH = Mathf.Max(maxH, e3.y);
                }
            }
            return mb.Build("ReefStaghorn" + seed, maxH, 0.75f);
        }

        /// Pillar coral: 3–5 upright 6-sided columns (≈ 1.0 m at scale 1) with rounded lighter caps.
        static PropModel Pillar(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var body = mb.B(PropMats.Flat(PropMats.Value(c, 0.92f)));
            var cap = mb.B(PropMats.ReefTip(c));
            var foot = mb.B(PropMats.ReefBase(c));
            int n = 3 + rng.Next(3);
            float maxH = 0f;
            foot.Cylinder(new Vector3(0f, -0.06f, 0f), Quaternion.identity, 0.3f, 0.24f, 0.14f, 6);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + PropUtil.Range(rng, -0.3f, 0.3f);
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropUtil.Range(rng, 0.06f, 0.2f);
                float h = PropUtil.Range(rng, 0.55f, 1.1f), r = PropUtil.Range(rng, 0.07f, 0.12f);
                Quaternion q = Quaternion.Euler(PropUtil.Range(rng, -6f, 6f), PropUtil.Range(rng, 0f, 60f), PropUtil.Range(rng, -6f, 6f));
                body.Cylinder(p, q, r, r * 0.9f, h, 6);
                cap.Cylinder(p + q * new Vector3(0f, h, 0f), q, r * 0.9f, r * 0.45f, r * 0.8f, 6, false, true);
                maxH = Mathf.Max(maxH, h + r * 0.8f);
            }
            return mb.Build("ReefPillar" + seed, maxH, 0.3f);
        }

        /// Mushroom coral: a flat dome 0.4 m wide with radial ridges.
        static PropModel Mushroom(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            float w = PropUtil.Range(rng, 0.9f, 1.1f);
            mb.B(PropMats.Flat(c)).Lathe(Vector3.zero, Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 60f), 0f), new[]
            {
                new Vector2(0.2f * w, -0.02f), new Vector2(0.21f * w, 0.03f), new Vector2(0.15f * w, 0.08f), new Vector2(0.06f * w, 0.105f), new Vector2(0f, 0.11f)
            }, 8);
            var ridge = mb.B(PropMats.ReefTip(c));
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                ridge.Beam(d * 0.03f + Vector3.up * 0.11f, d * 0.17f * w + Vector3.up * 0.06f, 0.025f, 0.03f);
            }
            return mb.Build("ReefMushroom" + seed, 0.12f, 0.21f);
        }

        /// Soft-coral puffs: 3–5 faceted puffs on short stalks.
        static PropModel SoftPuff(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            Color pc = Color.Lerp(c, PropPal.CoralLilac, 0.3f);
            var puff = mb.B(PropMats.Flat(PropMats.Value(pc, 1.08f)));
            var puff2 = mb.B(PropMats.Flat(pc));
            var stalk = mb.B(PropMats.Flat(PropMats.Value(pc, 0.7f)));
            int n = 3 + rng.Next(3);
            float maxH = 0f;
            for (int i = 0; i < n; i++)
            {
                float a = i * 2.39996f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * PropUtil.Range(rng, 0.04f, 0.16f);
                float h = PropUtil.Range(rng, 0.12f, 0.3f);
                Vector3 top = p + new Vector3(PropUtil.Range(rng, -0.05f, 0.05f), h, PropUtil.Range(rng, -0.05f, 0.05f));
                stalk.Rod(p - Vector3.up * 0.03f, top, 0.035f, 0.025f, 4);
                float s = PropUtil.Range(rng, 0.09f, 0.14f);
                (i % 2 == 0 ? puff : puff2).Ico(top + Vector3.up * s * 0.6f, new Vector3(s, s * 0.85f, s), Quaternion.Euler(0f, a * 50f, 0f), 0);
                maxH = Mathf.Max(maxH, top.y + s * 1.4f);
            }
            return mb.Build("ReefSoftPuff" + seed, maxH, 0.25f);
        }

        /// Encrusting growth: a low lumpy pad (kelp rocky reefs, wreck plates).
        static PropModel Encrust(int seed, Color c)
        {
            var rng = new System.Random(seed);
            var mb = new PropModelBuilder();
            var radii = PropRocks.Jitter(rng, 0, 0.22f, 1, 0.5f);
            mb.B(PropMats.Flat(PropMats.Value(c, 0.85f))).Ico(new Vector3(0f, -0.02f, 0f), new Vector3(0.3f, 0.09f, 0.26f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), 0, radii);
            mb.B(PropMats.ReefTip(c)).Ico(new Vector3(0.08f, 0.04f, -0.05f), new Vector3(0.12f, 0.05f, 0.1f), Quaternion.identity, 0);
            return mb.Build("ReefEncrust" + seed, 0.1f, 0.3f);
        }
    }

    /// L.3 coral reefs everywhere: the patch recipe (1 hero colony + 3–5 mid + 6–12 small + a sand / sea-grass gap of
    /// ≥ 30 %, a 3-colour sub-palette per patch, 1 colony per 2 m²), bommie gardens, habitat anchors for the fauna
    /// spawner (PropInfo.Habitats), the house reef with its swim-through arch, Octopus Den and Riffkanal current, 14
    /// lagoon patch reefs, fringing reefs around every landmark island and giant clams. Small colonies (< 0.5 m) go
    /// into a separate batch culled beyond 30 m. Everything is seeded per site (identical on every co-op machine).
    public static class PropReefs
    {
        static float G(float x, float z) => PropGround.H(x, z);

        /// A patch's 3 colours (indices into PropMats.ReefColor): warm, cool, accent.
        public struct Palette { public int warm, cool, accent; }

        static readonly int[] Warm = { 0, 1, 3, 4 };
        static readonly int[] Cool = { 2, 5, 6 };
        static readonly int[] Accent = { 7, 3, 0, 6 };

        /// Neighbouring patches differ: the palette rotates with the patch index.
        public static Palette PaletteFor(int i, System.Random rng)
        {
            return new Palette
            {
                warm = Warm[(i + rng.Next(2)) % Warm.Length],
                cool = Cool[(i * 2 + rng.Next(2)) % Cool.Length],
                accent = Accent[(i * 3 + 1) % Accent.Length],
            };
        }

        static int Pick(Palette p, System.Random rng)
        {
            double r = rng.NextDouble();
            return r < 0.45 ? p.warm : r < 0.85 ? p.cool : p.accent;
        }

        /// Places one colony. Returns false when it would come too close to the surface.
        public static bool Colony(PropBatch big, PropBatch small, System.Random rng, ReefColony kind, int color, Vector3 pos, Vector3 normal, float size, float maxTopY)
        {
            var model = PropReefKit.Get(kind, color, rng);
            if (model == null) return false;
            // size = rough footprint / height in metres → model scale
            float baseSize = Mathf.Max(model.height, model.radius * 2f);
            float s = size / Mathf.Max(0.05f, baseSize);
            float top = pos.y + model.height * s;
            if (top > maxTopY)
            {
                s *= Mathf.Max(0f, maxTopY - pos.y) / Mathf.Max(0.01f, model.height * s);
                if (s * baseSize < 0.18f) return false;
            }
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            Quaternion rot = PropUtil.OnGround(normal.normalized, kind == ReefColony.Pillar ? 0.2f : 0.55f, PropUtil.Range(rng, 0f, 360f));
            // < 0.7 m: the detail batch (culled beyond 30 m – the water hides it from ≈ 19 m anyway)
            var batch = s * baseSize < 0.7f && small != null ? small : big;
            batch.Add(model, Matrix4x4.TRS(pos - normal * 0.03f, rot, Vector3.one * s));
            return true;
        }

        /// One of the classic kit models (sponge, fan, whip, anemone) at a size.
        static bool Classic(PropBatch big, PropBatch small, System.Random rng, PropCoralType type, Vector3 pos, Vector3 normal, float size, float maxTopY)
        {
            var list = PropCoralKit.Of(type);
            if (list.Count == 0) return false;
            var model = list[rng.Next(list.Count)];
            float baseSize = Mathf.Max(model.height, 0.3f);
            float s = size / baseSize;
            float top = pos.y + model.height * s;
            if (top > maxTopY)
            {
                s *= Mathf.Max(0f, maxTopY - pos.y) / Mathf.Max(0.01f, model.height * s);
                if (s * baseSize < 0.2f) return false;
            }
            if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
            float follow = type == PropCoralType.Fan || type == PropCoralType.Whip ? 0.25f : 0.6f;
            Quaternion rot = PropUtil.OnGround(normal.normalized, follow, PropUtil.Range(rng, 0f, 360f));
            var batch = s * baseSize < 0.5f && small != null ? small : big;
            batch.Add(model, Matrix4x4.TRS(pos - normal * 0.03f, rot, Vector3.one * s));
            return true;
        }

        public delegate bool ValidFn(float x, float z, out float ground);

        /// Recipe parameters of one site.
        public sealed class Recipe
        {
            public string zone = "house_reef";
            /// Colonies per m² inside the coral part of the patch (L.3: 0.5, Fog Isle 0.67).
            public float perM2 = 0.5f;
            /// Kelp: rocky reef with sponges, anemones and encrusting growth (no tropical corals).
            public bool rocky;
            public float maxTopY = -1.2f;
            /// Give every second patch an anemone (clownfish home).
            public bool anemones = true;
            public bool seaGrassGaps = true;
            /// Sea fans more often (Fog Isle pygmy seahorses).
            public float fanBias;
        }

        /// One patch reef (HtF recipe) centred on c (y = seabed). Registers its habitat anchors.
        public static void Patch(PropBatch big, PropBatch small, System.Random rng, Vector3 c, float radius, int index, Recipe rc, ValidFn valid, Transform cols = null)
        {
            var pal = PaletteFor(index, rng);
            float gapA = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
            float gapHalf = PropUtil.Range(rng, 55f, 72f) * Mathf.Deg2Rad;
            Vector3 gapDir = new Vector3(Mathf.Cos(gapA), 0f, Mathf.Sin(gapA));
            bool InGap(Vector2 o) => o.sqrMagnitude > 0.8f && Vector2.Angle(new Vector2(gapDir.x, gapDir.z), o) * Mathf.Deg2Rad < gapHalf;

            // ---- base rock + hero colony
            Vector2 ho = PropUtil.InDisc(rng, radius * 0.25f);
            if (InGap(ho)) ho = -ho;
            float hx = c.x + ho.x, hz = c.z + ho.y;
            if (!valid(hx, hz, out float hg)) { hx = c.x; hz = c.z; hg = c.y; }
            Vector3 hp = new Vector3(hx, hg, hz);
            float rockS = Mathf.Min(PropUtil.Range(rng, 0.6f, 1.1f), Mathf.Max(0.2f, (rc.maxTopY - 0.8f - hg) * 0.6f));
            if (rockS > 0.35f)
            {
                Vector3 rp = hp + new Vector3(-gapDir.x, 0f, -gapDir.z) * (radius * 0.35f);
                rp.y = G(rp.x, rp.z) - rockS * 0.25f;
                PropRocks.Build(big.B(rng.NextDouble() < 0.6 ? PropMats.ReefRock : PropMats.ReefRockDark, rp), rp,
                    new Vector3(rockS * 1.6f, rockS * 0.8f, rockS * 1.2f), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 1, 0.18f);
                PropInfo.AddHabitat(rp + Vector3.up * 0.2f, rockS * 1.2f, HabitatKind.Crevice, rc.zone);
            }
            bool heroTable = false, heroBrain = false, fan = false, anemone = false, grass = false;
            if (rc.rocky)
            {
                // kelp rocky reef: a big barrel sponge or a rock with sponges as the hero
                Classic(big, small, rng, PropCoralType.Sponge, hp, Vector3.up, PropUtil.Range(rng, 1.0f, 1.5f), rc.maxTopY);
            }
            else
            {
                int hk = rng.Next(100);
                if (rc.fanBias > 0f && rng.NextDouble() < rc.fanBias) hk = 95;
                Vector3 hn = PropGround.Normal(hx, hz, 0.8f);
                if (hk < 35) { heroTable = Colony(big, small, rng, ReefColony.Table, Pick(pal, rng), hp, hn, PropUtil.Range(rng, 1.2f, 1.8f), rc.maxTopY); }
                else if (hk < 70) { heroBrain = Colony(big, small, rng, ReefColony.Brain, Pick(pal, rng), hp, hn, PropUtil.Range(rng, 1.2f, 1.7f), rc.maxTopY); }
                else if (hk < 88) Classic(big, small, rng, PropCoralType.Sponge, hp, hn, PropUtil.Range(rng, 1.1f, 1.5f), rc.maxTopY);
                else { fan = Classic(big, small, rng, PropCoralType.Fan, hp, hn, PropUtil.Range(rng, 1.2f, 1.7f), rc.maxTopY); }
            }
            if (heroTable) PropInfo.AddHabitat(hp + Vector3.up * 0.25f, 1.2f, HabitatKind.Overhang, rc.zone);
            if (heroBrain && index % 3 == 0) PropInfo.AddHabitat(hp + Vector3.up * 0.9f, 2.5f, HabitatKind.Cleaning, rc.zone);

            // ---- 4–7 mid colonies (round 2: 3–5 left the patches thin)
            int mids = 4 + rng.Next(4);
            for (int k = 0; k < mids; k++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius * 0.75f);
                if (o.magnitude < radius * 0.22f || InGap(o)) continue;
                float x = c.x + o.x, z = c.z + o.y;
                if (!valid(x, z, out float g)) continue;
                Vector3 p = new Vector3(x, g, z);
                Vector3 n = PropGround.Normal(x, z, 0.8f);
                if (rc.rocky)
                {
                    if (rng.NextDouble() < 0.5) Classic(big, small, rng, PropCoralType.Sponge, p, n, PropUtil.Range(rng, 0.6f, 1.0f), rc.maxTopY);
                    else Colony(big, small, rng, ReefColony.Encrust, Pick(pal, rng), p, n, PropUtil.Range(rng, 0.7f, 1.0f), rc.maxTopY);
                    continue;
                }
                int t = rng.Next(100);
                ReefColony kind = t < 30 ? ReefColony.Branching : t < 50 ? ReefColony.Brain : t < 68 ? ReefColony.Staghorn : t < 84 ? ReefColony.Pillar : ReefColony.Table;
                // playtest round 2: a little bigger (0.6–1.0 → 0.75–1.25 m) – the patches read as sand with dots
                if (t >= 92) { fan |= Classic(big, small, rng, PropCoralType.Fan, p, n, PropUtil.Range(rng, 0.8f, 1.2f), rc.maxTopY); continue; }
                Colony(big, small, rng, kind, Pick(pal, rng), p, n, PropUtil.Range(rng, 0.75f, 1.25f), rc.maxTopY);
            }

            // ---- small colonies to 1 per 2 m² (≥ 6, ≤ 40)
            float area = Mathf.PI * radius * radius * (1f - gapHalf / Mathf.PI);
            int smalls = Mathf.Clamp(Mathf.RoundToInt(area * rc.perM2) - mids - 1, 6, 52);
            for (int k = 0; k < smalls; k++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius);
                if (InGap(o)) continue;
                float x = c.x + o.x, z = c.z + o.y;
                if (!valid(x, z, out float g)) continue;
                Vector3 p = new Vector3(x, g, z);
                Vector3 n = PropGround.Normal(x, z, 0.8f);
                if (rc.rocky)
                {
                    int rt = rng.Next(100);
                    if (rt < 40) Colony(big, small, rng, ReefColony.Encrust, Pick(pal, rng), p, n, PropUtil.Range(rng, 0.3f, 0.5f), rc.maxTopY);
                    else if (rt < 70) Classic(big, small, rng, PropCoralType.Sponge, p, n, PropUtil.Range(rng, 0.3f, 0.5f), rc.maxTopY);
                    else if (rt < 85) Classic(big, small, rng, PropCoralType.Anemone, p, n, 0.35f, rc.maxTopY);
                    else Classic(big, small, rng, PropCoralType.Whip, p, n, PropUtil.Range(rng, 0.5f, 0.9f), rc.maxTopY);
                    continue;
                }
                int st = rng.Next(100);
                if (rc.fanBias > 0f && st > 80) { fan |= Classic(big, small, rng, PropCoralType.Fan, p, n, PropUtil.Range(rng, 0.45f, 0.7f), rc.maxTopY); continue; }
                ReefColony kind = st < 28 ? ReefColony.Mushroom : st < 50 ? ReefColony.SmallBrain : st < 72 ? ReefColony.Branching : st < 90 ? ReefColony.SoftPuff : ReefColony.Pillar;
                // small colonies 0.3–0.5 → 0.38–0.65 m (they were confetti-sized dots on the sand)
                Colony(big, small, rng, kind, Pick(pal, rng), p, n, PropUtil.Range(rng, 0.38f, 0.65f), rc.maxTopY);
            }

            // ---- anemone with a clownfish home (1 per 2 patches)
            if (rc.anemones && index % 2 == 0)
            {
                Vector2 o = new Vector2(-gapDir.x, -gapDir.z) * (radius * 0.55f) + PropUtil.InDisc(rng, 0.6f);
                float x = c.x + o.x, z = c.z + o.y;
                if (valid(x, z, out float g) && g < -1.8f)
                {
                    int n = 3 + rng.Next(3);
                    for (int k = 0; k < n; k++)
                    {
                        Vector2 ao = PropUtil.InDisc(rng, 0.7f);
                        float ax = x + ao.x, az = z + ao.y;
                        Classic(big, small, rng, PropCoralType.Anemone, new Vector3(ax, G(ax, az), az), Vector3.up, PropUtil.Range(rng, 0.3f, 0.42f), -1.0f);
                    }
                    Vector3 ap = new Vector3(x, g + 0.3f, z);
                    PropInfo.AnemoneSpots.Add(ap);
                    PropInfo.AddHabitat(ap, 1.0f, HabitatKind.Anemone, rc.zone);
                    anemone = true;
                }
            }

            // ---- the gap: sand or sea grass
            Vector3 gc = new Vector3(c.x + gapDir.x * radius * 0.6f, 0f, c.z + gapDir.z * radius * 0.6f);
            gc.y = G(gc.x, gc.z);
            if (rc.seaGrassGaps && !rc.rocky && rng.NextDouble() < 0.5 && gc.y < -1.6f)
            {
                PropReef.SeaGrassPatch(small, rng, gc, radius * 0.38f, null);
                grass = true;
            }
            PropInfo.AddHabitat(gc + Vector3.up * 0.1f, radius * 0.4f, grass ? HabitatKind.SeaGrass : HabitatKind.Sand, rc.zone);
            if (fan) PropInfo.AddHabitat(hp + Vector3.up * 0.6f, radius * 0.6f, HabitatKind.SeaFan, rc.zone);
            if (!anemone && !heroTable && !heroBrain && index % 4 == 1) PropInfo.AddHabitat(hp + Vector3.up * 0.4f, radius * 0.5f, HabitatKind.Crevice, rc.zone);
        }

        /// A coral garden on a bommie peak (top + flanks), registers a cleaning station on every third.
        public static void BommieGarden(PropBatch big, PropBatch small, System.Random rng, Vector3 peak, int index, Recipe rc, ValidFn valid)
        {
            var pal = PaletteFor(index + 7, rng);
            int n = 8 + rng.Next(6);
            for (int k = 0; k < n; k++)
            {
                Vector2 o = PropUtil.InDisc(rng, 3.4f);
                float x = peak.x + o.x, z = peak.z + o.y;
                if (!valid(x, z, out float g)) continue;
                Vector3 p = new Vector3(x, g, z);
                Vector3 nrm = PropGround.Normal(x, z, 0.8f);
                bool flank = nrm.y < 0.8f;
                if (rc.rocky)
                {
                    if (k % 3 == 0) Classic(big, small, rng, PropCoralType.Sponge, p, nrm, PropUtil.Range(rng, 0.4f, 0.8f), rc.maxTopY);
                    else Colony(big, small, rng, ReefColony.Encrust, Pick(pal, rng), p, nrm, PropUtil.Range(rng, 0.4f, 0.8f), rc.maxTopY);
                    continue;
                }
                if (flank && k % 4 == 0) { Classic(big, small, rng, PropCoralType.Fan, p, nrm, PropUtil.Range(rng, 0.6f, 1.0f), rc.maxTopY); continue; }
                ReefColony kind = k == 0 ? ReefColony.Brain : (k % 5 == 1 ? ReefColony.Table : (k % 3 == 0 ? ReefColony.Branching : (k % 3 == 1 ? ReefColony.Mushroom : ReefColony.Pillar)));
                float size = kind == ReefColony.Brain || kind == ReefColony.Table ? PropUtil.Range(rng, 0.9f, 1.4f) : kind == ReefColony.Mushroom ? 0.35f : PropUtil.Range(rng, 0.5f, 0.9f);
                Colony(big, small, rng, kind, Pick(pal, rng), p, nrm, size, rc.maxTopY);
            }
            PropInfo.AddHabitat(peak + Vector3.up * 0.8f, 3f, index % 3 == 0 ? HabitatKind.Cleaning : HabitatKind.Crevice, rc.zone);
            if (index % 4 == 2) PropInfo.AddHabitat(peak + Vector3.up * 0.4f, 2.2f, HabitatKind.Overhang, rc.zone);
        }

        /// Local maxima of the seabed inside a disc (bommie peaks): higher than every sample 4.5 m around by ≥ minRise.
        public static List<Vector3> Peaks(Vector3 c, float radius, float minDepth, float maxDepth, float minRise, int max, PropExclusion keepOut)
        {
            var list = new List<Vector3>();
            const float step = 3f;
            int n = Mathf.CeilToInt(radius / step);
            var used = new PropExclusion(16f);
            for (int iz = -n; iz <= n && list.Count < max; iz++)
            {
                for (int ix = -n; ix <= n && list.Count < max; ix++)
                {
                    float x = c.x + ix * step + ((iz & 1) == 0 ? 0f : step * 0.5f), z = c.z + iz * step;
                    float dx = x - c.x, dz = z - c.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    float h = G(x, z), d = -h;
                    if (d < minDepth || d > maxDepth) continue;
                    if ((keepOut != null && keepOut.Blocked(x, z)) || used.Blocked(x, z)) continue;
                    bool peak = true;
                    for (int k = 0; k < 8 && peak; k++)
                    {
                        float a = k * Mathf.PI / 4f;
                        if (G(x + Mathf.Cos(a) * 4.5f, z + Mathf.Sin(a) * 4.5f) > h - minRise) peak = false;
                    }
                    if (!peak) continue;
                    list.Add(new Vector3(x, h, z));
                    used.Add(x, z, 9f);
                }
            }
            return list;
        }

        // ================================================================== house reef
        /// Coral discs of the house reef (x, z, radius) – patches and bommie gardens – for the playtest coverage metric.
        public static readonly List<Vector3> HouseReefDiscs = new List<Vector3>(256);

        static bool InHouseReefDisc(float x, float z)
        {
            var discs = HouseReefDiscs;
            for (int i = 0; i < discs.Count; i++)
            {
                float dx = x - discs[i].x, dz = z - discs[i].y;
                if (dx * dx + dz * dz <= discs[i].z * discs[i].z) return true;
            }
            return false;
        }

        public static IEnumerator HouseReef(Transform decor, DiveSite site)
        {
            HouseReefDiscs.Clear();
            var rng = new System.Random(5150);
            var big = new PropBatch(60f);
            var small = new PropBatch(60f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);
            Vector3 c = site.center;
            var keepOut = new PropExclusion(16f);
            keepOut.AddLine(WorldLayout.DockStart, WorldLayout.DockEnd + new Vector3(8f, 0f, 0f), 7f);
            keepOut.Add(WorldLayout.BoatSpawn.x, WorldLayout.BoatSpawn.z, 11f);
            keepOut.Add(WorldLayout.FishingSpot.x, WorldLayout.FishingSpot.z, 6f);
            keepOut.Add(WorldLayout.SwimRaft.x, WorldLayout.SwimRaft.z, 6f);
            // playtest round 2: denser patches (0.5 → 0.62 colonies per m², more and bigger patches below)
            var rc = new Recipe { zone = "house_reef", perM2 = 0.62f, maxTopY = -1.2f };
            bool Valid(float x, float z, out float g)
            {
                g = G(x, z);
                float d = -g;
                return d >= 2f && d <= site.maxDepth + 3f && !keepOut.Blocked(x, z);
            }

            // ---- bommies: coral gardens on 40 seabed peaks
            var peaks = Peaks(c, site.radius * 0.97f, 2.4f, 11.5f, 0.6f, 40, keepOut);
            var spacing = new PropExclusion(12f);
            for (int i = 0; i < peaks.Count; i++)
            {
                BommieGarden(big, small, rng, peaks[i], i, rc, Valid);
                spacing.Add(peaks[i].x, peaks[i].z, 5f);
                HouseReefDiscs.Add(new Vector3(peaks[i].x, peaks[i].z, 3.4f));
                if (PropRun.Due()) yield return null;
            }
            // T1 „Geisternetz“: the lost net hangs on three bommies 5–9 m deep (npc quest objects ghostnet:0..2)
            for (int i = 0, k = 0; i < peaks.Count && k < 3; i++)
            {
                float d = -peaks[i].y;
                if (d < 5f || d > 9.5f) continue;
                PropSpots.Register("ghostnet:" + k, peaks[i] + Vector3.up * 0.7f);
                k++;
                i += 4;
            }

            // ---- 185 patches (the first 45 on the shallow crest band 2.5–4.5 m) – playtest round 2: 90 patches covered
            // ~20 % of the 2–10 m seabed (L.3 asks ≥ 60 %; 140 patches measured 43 %), the reef read as sand with dots
            int patches = 0, attempts = 0;
            while (patches < 185 && attempts < 14000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius * 0.97f);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || spacing.Blocked(x, z)) continue;
                float d = -g;
                bool shallow = d < 4.5f;
                // the first 45 prefer the shallow crest band, the rest prefer the deeper fore-reef (soft rules)
                if (patches < 45 && !shallow && attempts < 3500) continue;
                if (patches >= 45 && shallow && attempts % 3 != 0) continue;
                float pr = shallow ? PropUtil.Range(rng, 4f, 6.5f) : PropUtil.Range(rng, 4.5f, 7.5f);
                Patch(big, small, rng, new Vector3(x, g, z), pr, patches, rc, Valid, cols);
                spacing.Add(x, z, pr + 0.8f);
                HouseReefDiscs.Add(new Vector3(x, z, pr));
                patches++;
                if (PropRun.Due()) yield return null;
            }

            // ---- L.3 fill pass (playtest r2 measured 54 % < 60 %): the 2–10 m cells of the playtest's 3 m grid that no
            // patch covers yet get compact patches – nearest to the dock ladder and the reef centre (first dive) first –
            // until 62 % of the band is coral. Same seed order every time (deterministic on every co-op machine).
            {
                Vector3 ladder = PropInfo.DockLadderBottom != Vector3.zero ? PropInfo.DockLadderBottom : WorldLayout.DockEnd;
                var cellList = new List<Vector2>(2048);
                var keyList = new List<float>(2048);
                int total = 0, coveredN = 0;
                for (float gx = -site.radius; gx <= site.radius; gx += 3f)
                {
                    for (float gz = -site.radius; gz <= site.radius; gz += 3f)
                    {
                        if (gx * gx + gz * gz > site.radius * site.radius) continue;
                        float x = c.x + gx, z = c.z + gz;
                        float d = -G(x, z);
                        if (d < 2f || d > 10f) continue;
                        total++;
                        if (InHouseReefDisc(x, z)) { coveredN++; continue; }
                        cellList.Add(new Vector2(x, z));
                        float dl = new Vector2(x - ladder.x, z - ladder.z).magnitude, dc = new Vector2(x - c.x, z - c.z).magnitude;
                        keyList.Add(Mathf.Min(dl, dc) + (float)rng.NextDouble() * 4f);
                    }
                    if (PropRun.Due()) yield return null;
                }
                var cellArr = cellList.ToArray();
                var keyArr = keyList.ToArray();
                System.Array.Sort(keyArr, cellArr);
                var done = new bool[cellArr.Length];
                int extra = 0;
                for (int i = 0; i < cellArr.Length && extra < 160 && coveredN < total * 0.62f; i++)
                {
                    if (done[i]) continue;
                    float x = cellArr[i].x, z = cellArr[i].y;
                    if (!Valid(x, z, out float g) || spacing.Blocked(x, z, -0.6f)) continue;
                    float pr = PropUtil.Range(rng, 3.2f, 4.6f);
                    Patch(big, small, rng, new Vector3(x, g, z), pr, patches + extra, rc, Valid, cols);
                    spacing.Add(x, z, pr + 0.4f);
                    HouseReefDiscs.Add(new Vector3(x, z, pr));
                    extra++;
                    for (int k = 0; k < cellArr.Length; k++)
                    {
                        if (done[k]) continue;
                        float dx = cellArr[k].x - x, dz = cellArr[k].y - z;
                        if (dx * dx + dz * dz <= pr * pr) { done[k] = true; coveredN++; }
                    }
                    if (PropRun.Due()) yield return null;
                }
                Debug.Log("[Props] house reef fill pass: +" + extra + " patches, coral on " + (total > 0 ? coveredN * 100 / total : 0) + " % of the 2–10 m band");
            }

            // ---- swim-through arch, the Octopus Den, the Riffkanal current, giant clams, sea grass, loose rocks
            Features(decor, big, small, cols, rng, c, site.radius, keepOut, rc, peaks);
            for (int i = 0; i < 10; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || -g > 8f || spacing.Blocked(x, z)) continue;
                PropReef.SeaGrassPatch(small, rng, new Vector3(x, g, z), PropUtil.Range(rng, 2.5f, 4.5f), keepOut);
                PropInfo.AddHabitat(new Vector3(x, g + 0.2f, z), 3f, HabitatKind.SeaGrass, "house_reef");
            }
            for (int i = 0; i < 40; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g)) continue;
                float s = Mathf.Min(PropUtil.Range(rng, 0.25f, 0.8f), -1.5f - g);
                if (s < 0.2f) continue;
                Vector3 p = new Vector3(x, g - s * 0.2f, z);
                PropRocks.Build(small.B(PropMats.ReefRock, p), p, new Vector3(s * 1.3f, s * 0.7f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 0);
            }
            Clams(decor, rng, c, site.radius, 7, 3f, 8f, keepOut, "house_reef");

            PropReef.QuietShadows(big);
            PropReef.QuietShadows(small);
            // 170 → 95 m: under water the sight ends at ~19 m and from above the sea is opaque below 2.5 m – the denser
            // round-2 reef must not cost frames where nobody can see it
            big.Flush(decor, "ReefCorals", ShadowCastingMode.On, 95f);
            if (PropRun.Due()) yield return null;
            small.Flush(decor, "ReefDetail", ShadowCastingMode.Off, 30f);
        }

        /// House reef features (K.3): the 2 m coral arch swim-through, the „Krakenhöhle“ overhang and the 40 m
        /// Riffkanal current (0.6 m/s) between two bommies.
        static void Features(Transform decor, PropBatch big, PropBatch small, Transform cols, System.Random rng, Vector3 c, float radius, PropExclusion keepOut, Recipe rc, List<Vector3> peaks)
        {
            // ---- arch: two rock pillars + a lintel, 2 m clear width and height, at 7–10 m
            if (FindSpot(c, radius * 0.8f, 7f, 10f, keepOut, 101, out Vector3 ap))
            {
                float yaw = PropUtil.Range(rng, 0f, 180f);
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                Vector3 side = q * Vector3.right;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 pp = ap + side * (s * 1.75f);
                    pp.y = G(pp.x, pp.z) - 0.3f;
                    var m = PropRocks.Mesh(new Vector3(1.4f, 3.0f, 1.6f), 7000 + s, 1, 0.14f);
                    big.Add(m, PropMats.ReefRockDark, Matrix4x4.TRS(pp + Vector3.up * 1.2f, q, Vector3.one));
                    PropCol.Mesh(cols, m, pp + Vector3.up * 1.2f, q, Vector3.one, "ArchPillar");
                }
                Vector3 lp = ap + Vector3.up * 2.75f;
                lp.y = Mathf.Max(G(ap.x + side.x * 1.75f, ap.z + side.z * 1.75f), G(ap.x - side.x * 1.75f, ap.z - side.z * 1.75f)) + 2.8f;
                lp.y = Mathf.Min(lp.y, -1.8f);
                var lm = PropRocks.Mesh(new Vector3(5.2f, 1.0f, 1.8f), 7010, 1, 0.12f);
                big.Add(lm, PropMats.ReefRock, Matrix4x4.TRS(lp, q, Vector3.one));
                PropCol.Mesh(cols, lm, lp, q, Vector3.one, "ArchLintel");
                // corals on the arch
                for (int k = 0; k < 8; k++)
                {
                    Vector3 tp = lp + q * new Vector3(PropUtil.Range(rng, -2.2f, 2.2f), 0.45f, PropUtil.Range(rng, -0.6f, 0.6f));
                    Colony(big, small, rng, k % 3 == 0 ? ReefColony.Brain : k % 3 == 1 ? ReefColony.SoftPuff : ReefColony.Branching, rng.Next(8), tp, Vector3.up, PropUtil.Range(rng, 0.4f, 0.8f), rc.maxTopY);
                }
                PropInfo.AddHabitat(ap + Vector3.up * 1f, 2.5f, HabitatKind.Overhang, "house_reef");
                PropInfo.SetSpot("reef_arch", ap + Vector3.up * 1f, yaw);
            }
            // ---- Octopus Den: a half-dome overhang with a dark hollow
            if (FindSpot(c + new Vector3(-30f, 0f, 20f), radius * 0.7f, 5.5f, 9f, keepOut, 202, out Vector3 op))
            {
                float yaw = PropUtil.Range(rng, 0f, 360f);
                var gr = PropGrotto.Build(big, cols, op, yaw, new[]
                {
                    new Vector4(0f, -0.2f, 1.1f, 1.2f), new Vector4(0f, 0.9f, 1.3f, 1.35f), new Vector4(0.2f, 2.0f, 1.0f, 1.0f),
                }, 303, PropMats.ReefRock, PropMats.ReefRockDark, 1.2f, 0.9f, true);
                for (int k = 0; k < 7; k++)
                {
                    Vector3 tp = gr.W(new Vector3(PropUtil.Range(rng, -1.6f, 1.6f), 2.2f, PropUtil.Range(rng, -0.2f, 2.0f)));
                    tp.y = Mathf.Min(tp.y, -1.6f);
                    Colony(big, small, rng, k % 2 == 0 ? ReefColony.SoftPuff : ReefColony.Mushroom, rng.Next(8), tp, Vector3.up, PropUtil.Range(rng, 0.35f, 0.5f), rc.maxTopY);
                }
                PropInfo.AddHabitat(gr.W(new Vector3(0f, 0.4f, 1.2f)), 1.4f, HabitatKind.Crevice, "house_reef");
                PropInfo.AddHabitat(gr.W(new Vector3(0f, 0.8f, 0.2f)), 2f, HabitatKind.Overhang, "house_reef");
                PropInfo.SetSpot("octopus_den", gr.W(new Vector3(0f, 0.4f, 1.0f)), yaw);
            }
            // ---- Riffkanal: 35–45 m between two bommie peaks (relaxed to 22–60 m when the 0.62 seabed has no such
            // pair – the house reef must always have its current lane, K.3)
            Vector3 a = Vector3.zero, b = Vector3.zero;
            float best = float.MaxValue;
            for (int pass = 0; pass < 2 && best == float.MaxValue; pass++)
            {
                float dMin = pass == 0 ? 35f : 22f, dMax = pass == 0 ? 45f : 60f;
                for (int i = 0; i < peaks.Count; i++)
                    for (int j = i + 1; j < peaks.Count; j++)
                    {
                        float d = Vector3.Distance(new Vector3(peaks[i].x, 0f, peaks[i].z), new Vector3(peaks[j].x, 0f, peaks[j].z));
                        if (d < dMin || d > dMax) continue;
                        float cost = Mathf.Abs(d - 40f) + Mathf.Abs(peaks[i].y + peaks[j].y + 12f) * 0.3f;
                        if (cost < best) { best = cost; a = peaks[i]; b = peaks[j]; }
                    }
            }
            // the 0.62 seabed of round 2 has hardly any bommie peaks: then the lane runs between two 5–9 m seabed spots
            // ~40 m apart near the site centre (deterministic)
            if (best == float.MaxValue
                && FindSpot(c + new Vector3(-20f, 0f, -6f), radius * 0.5f, 5f, 9f, keepOut, 211, out Vector3 fa)
                && FindSpot(c + new Vector3(20f, 0f, 6f), radius * 0.5f, 5f, 9f, keepOut, 212, out Vector3 fb)
                && Vector3.Distance(new Vector3(fa.x, 0f, fa.z), new Vector3(fb.x, 0f, fb.z)) > 20f)
            {
                a = fa; b = fb; best = 0f;
            }
            if (best < float.MaxValue)
            {
                Vector3 s0 = a + Vector3.up * 1.4f, s1 = b + Vector3.up * 1.4f;
                s0.y = Mathf.Min(s0.y, -1.8f);
                s1.y = Mathf.Min(s1.y, -1.8f);
                PropInfo.AddCurrent("reef_channel", "house_reef", s0, s1, 4.5f, 0.6f);
                PropInfo.SetSpot("reef_channel", s0);
            }
        }

        /// A seabed point of a depth band near p (spiral, deterministic).
        public static bool FindSpot(Vector3 p, float maxR, float minDepth, float maxDepth, PropExclusion keepOut, int salt, out Vector3 pos)
        {
            for (float r = 0f; r <= maxR; r += 2f)
            {
                int steps = r < 0.1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r * 1.2f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i / (float)steps * Mathf.PI * 2f + salt * 0.37f + r * 0.13f;
                    float x = p.x + Mathf.Cos(a) * r, z = p.z + Mathf.Sin(a) * r;
                    float d = -G(x, z);
                    if (d < minDepth || d > maxDepth) continue;
                    if (keepOut != null && keepOut.Blocked(x, z)) continue;
                    if (PropGround.Slope(x, z, 1.2f) > 0.5f) continue;
                    pos = new Vector3(x, -d, z);
                    return true;
                }
            }
            pos = Vector3.zero;
            return false;
        }

        /// Giant clams (L.3): blue lips, they close when a diver comes close (gag + sound).
        public static void Clams(Transform parent, System.Random rng, Vector3 c, float radius, int count, float minDepth, float maxDepth, PropExclusion keepOut, string zone)
        {
            int placed = 0;
            for (int i = 0; i < 400 && placed < count; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, radius * 0.9f);
                float x = c.x + o.x, z = c.z + o.y;
                float d = -G(x, z);
                if (d < minDepth || d > maxDepth || (keepOut != null && keepOut.Blocked(x, z))) continue;
                if (PropGround.Slope(x, z, 1f) > 0.45f) continue;
                PropClam.Create(parent, new Vector3(x, -d, z), PropUtil.Range(rng, 0f, 360f), PropUtil.Range(rng, 0.8f, 1.15f));
                PropInfo.AddHabitat(new Vector3(x, -d + 0.3f, z), 1f, HabitatKind.Sand, zone);
                placed++;
            }
        }

        // ================================================================== lagoon patch reefs (main island)
        /// 14 patch reefs in the lagoon all around the main island (6–12 m across, 1.5–4 m deep, ~60 m off the beach).
        public static IEnumerator Lagoon(Transform uw)
        {
            var root = PropUtil.WorldRoot("Lagoon", uw);
            var decor = PropUtil.WorldRoot("Decor", root);
            root.gameObject.AddComponent<PropSiteActivation>().Init(decor.gameObject, Vector3.zero, 420f);
            var rng = new System.Random(7140);
            var big = new PropBatch(60f);
            var small = new PropBatch(60f);
            var keepOut = new PropExclusion(16f);
            keepOut.AddLine(WorldLayout.DockStart, WorldLayout.DockEnd + new Vector3(8f, 0f, 0f), 8f);
            keepOut.Add(WorldLayout.BoatSpawn.x, WorldLayout.BoatSpawn.z, 12f);
            keepOut.Add(WorldLayout.SwimRaft.x, WorldLayout.SwimRaft.z, 8f);
            keepOut.Add(WorldLayout.DriftwoodRaft.x, WorldLayout.DriftwoodRaft.z, 8f);
            keepOut.Add(WorldLayout.Village.x + 6f, WorldLayout.Village.z - 26f, 14f);   // village jetty + canoe start
            var house = DiveSites.Get("house_reef");
            var rc = new Recipe { zone = "lagoon", perM2 = 0.5f, maxTopY = -0.7f };
            bool Valid(float x, float z, out float g)
            {
                g = G(x, z);
                float d = -g;
                return d >= 1.0f && d <= 5f && !keepOut.Blocked(x, z);
            }
            // the terrain raises a knobbly rock mound under each of its 14 lagoon patch reefs (WorldFeatures, L.3)
            var spots = WorldFeatures.LagoonPatchReefs;
            int placed = 0;
            for (int k = 0; spots != null && k < spots.Length; k++)
            {
                var sp = spots[k];
                float x = sp.position.x, z = sp.position.z;
                float g = G(x, z);
                if (-g < 0.9f) continue;
                if (house != null && new Vector2(x - house.center.x, z - house.center.z).magnitude < house.radius) continue;
                float pr = Mathf.Clamp(sp.radius, 3f, 6f);
                Patch(big, small, rng, new Vector3(x, g, z), pr, placed, rc, Valid);
                PropInfo.SetSpot("lagoon_patch_" + placed, new Vector3(x, g, z));
                placed++;
                if (PropRun.Due()) yield return null;
            }
            // playtest round 2 („das Korallenriff soll viel besser sein – überall“): between the 14 big patch reefs the
            // lagoon was bare sand – 70 small coral heads (2–3.5 m across) scattered all around the island, 1.2–5 m
            int heads = 0, tries = 0;
            var headSpacing = new PropExclusion(16f);
            for (int k = 0; spots != null && k < spots.Length; k++) headSpacing.Add(spots[k].position.x, spots[k].position.z, Mathf.Clamp(spots[k].radius, 3f, 6f) + 4f);
            while (heads < 70 && tries < 2500)
            {
                tries++;
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float r = PropUtil.Range(rng, WorldLayout.IslandRadius * 0.75f, WorldLayout.IslandRadius + 95f);
                float x = WorldLayout.IslandCenter.x + Mathf.Cos(a) * r, z = WorldLayout.IslandCenter.z + Mathf.Sin(a) * r;
                if (!Valid(x, z, out float g) || -g < 1.2f || headSpacing.Blocked(x, z)) continue;
                if (house != null && new Vector2(x - house.center.x, z - house.center.z).magnitude < house.radius) continue;
                float pr = PropUtil.Range(rng, 1.8f, 3.4f);
                Patch(big, small, rng, new Vector3(x, g, z), pr, placed + heads, rc, Valid);
                headSpacing.Add(x, z, pr + 5f);
                heads++;
                if (PropRun.Due()) yield return null;
            }
            PropReef.QuietShadows(big);
            PropReef.QuietShadows(small);
            big.Flush(decor, "LagoonCorals", ShadowCastingMode.On, 95f);
            small.Flush(decor, "LagoonDetail", ShadowCastingMode.Off, 30f);
            // K.3: today's small finds in the lagoon (3 per day, 1.5–5 m)
            PropFindsSite.Create(decor, FindsDatabase.LagoonSite, null);
        }

        // ================================================================== fringing reefs around the landmark islands
        /// Fringing reef on the 0–6 m shelf around a landmark island (kelp island: rocky reef, no tropical corals).
        public static IEnumerator Fringing(Transform uw, string id, Vector3 island, float shoreR, bool rocky, int patchCount, int seed)
        {
            var root = PropUtil.WorldRoot("Fringing_" + id, uw);
            var decor = PropUtil.WorldRoot("Decor", root);
            root.gameObject.AddComponent<PropSiteActivation>().Init(decor.gameObject, island, 420f);
            var rng = new System.Random(seed);
            var big = new PropBatch(60f);
            var small = new PropBatch(60f);
            var rc = new Recipe { zone = id, perM2 = 0.5f, rocky = rocky, maxTopY = -0.8f, anemones = true };
            var used = new PropExclusion(16f);
            bool Valid(float x, float z, out float g)
            {
                g = G(x, z);
                float d = -g;
                return d >= 1.2f && d <= 7f;
            }
            int placed = 0;
            for (int k = 0; k < patchCount * 3 && placed < patchCount; k++)
            {
                float a = (k * (360f / (patchCount * 3)) + seed % 17) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                for (float r = shoreR * 0.6f; r < shoreR + 70f; r += 1.5f)
                {
                    float x = island.x + dir.x * r, z = island.z + dir.z * r;
                    float d = -G(x, z);
                    if (d < 2.2f || d > 5.5f) continue;
                    if (used.Blocked(x, z)) break;
                    float pr = PropUtil.Range(rng, 2.6f, 4.5f);
                    Patch(big, small, rng, new Vector3(x, -d, z), pr, placed, rc, Valid);
                    used.Add(x, z, pr + 6f);
                    placed++;
                    break;
                }
                if (PropRun.Due()) yield return null;
            }
            PropReef.QuietShadows(big);
            PropReef.QuietShadows(small);
            big.Flush(decor, "FringeCorals", ShadowCastingMode.On, 110f);
            small.Flush(decor, "FringeDetail", ShadowCastingMode.Off, 30f);
        }

        // ================================================================== Fog Isle reef (densest)
        /// The Fog Isle seamount lagoon: densest corals (1 per 1.5 m²), sea fans with pygmy seahorses, bommie gardens,
        /// a few giant clams. The cave (Nebelgrotte), the buoy lane and the island props are built by PropFogIsle.
        public static IEnumerator FogReef(Transform decor, DiveSite site)
        {
            var rng = new System.Random(6464);
            var big = new PropBatch(60f);
            var small = new PropBatch(60f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);
            Vector3 c = site.center;
            var rc = new Recipe { zone = "fog_isle", perM2 = 0.67f, maxTopY = -1.0f, fanBias = 0.3f };
            bool Valid(float x, float z, out float g)
            {
                g = G(x, z);
                float d = -g;
                return d >= 1.6f && d <= site.maxDepth;
            }
            var peaks = Peaks(c, site.radius, 2f, 22f, 0.5f, 16, null);
            var spacing = new PropExclusion(12f);
            for (int i = 0; i < peaks.Count; i++)
            {
                BommieGarden(big, small, rng, peaks[i], i, rc, Valid);
                spacing.Add(peaks[i].x, peaks[i].z, 4.5f);
            }
            int patches = 0, attempts = 0;
            while (patches < 34 && attempts < 3000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                if (!Valid(x, z, out float g) || spacing.Blocked(x, z)) continue;
                // the island itself (its shore) is not part of the site's seabed
                if (new Vector2(x - WorldLayout.FogIsland.x, z - WorldLayout.FogIsland.z).magnitude < WorldLayout.FogIslandShoreRadius + 4f) continue;
                float pr = PropUtil.Range(rng, 3.5f, 6f);
                Patch(big, small, rng, new Vector3(x, g, z), pr, patches, rc, Valid, cols);
                spacing.Add(x, z, pr + 1.2f);
                patches++;
                if (PropRun.Due()) yield return null;
            }
            Clams(decor, rng, c, site.radius, 4, 3f, 12f, null, "fog_isle");
            PropReef.QuietShadows(big);
            PropReef.QuietShadows(small);
            big.Flush(decor, "FogCorals", ShadowCastingMode.On, 110f);
            small.Flush(decor, "FogDetail", ShadowCastingMode.Off, 30f);
        }
    }

    /// Giant clam (L.3): blue lips; closes when a diver comes within 1.3 m (bubbles + a click), opens again after 4 s.
    public class PropClam : MonoBehaviour
    {
        public Transform lid;
        float closedUntil, angle = 1f, next;
        static Mesh shellMesh, lipMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { shellMesh = null; lipMesh = null; }

        public static PropClam Create(Transform parent, Vector3 pos, float yaw, float scale)
        {
            Build();
            var go = new GameObject("GiantClam");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos - Vector3.up * 0.05f, Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = Vector3.one * scale;
            PropUtil.MeshObject(go.transform, "Bottom", shellMesh, PropMats.Flat(PropMats.Value(PropPal.Concrete, 0.95f)), Vector3.zero, Quaternion.identity, false);
            PropUtil.MeshObject(go.transform, "LipsBottom", lipMesh, PropMats.Flat(LowPoly.Hex("#3D7FD9")), Vector3.zero, Quaternion.identity, false);
            var hinge = new GameObject("Lid").transform;
            hinge.SetParent(go.transform, false);
            hinge.localPosition = new Vector3(0f, 0.12f, -0.22f);
            hinge.localRotation = Quaternion.Euler(-28f, 0f, 0f);
            // the top half = the bottom half mirrored at y 0.12 (180° about z; the shell is symmetric in x)
            PropUtil.MeshObject(hinge, "Top", shellMesh, PropMats.Flat(PropMats.Value(PropPal.Concrete, 1.02f)), new Vector3(0f, 0.12f, 0.22f), Quaternion.Euler(0f, 0f, 180f), false);
            PropUtil.MeshObject(hinge, "LipsTop", lipMesh, PropMats.Flat(LowPoly.Hex("#2E9FBF")), new Vector3(0f, 0.12f, 0.22f), Quaternion.Euler(0f, 0f, 180f), false);
            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.14f, 0f);
            col.size = new Vector3(0.62f, 0.28f, 0.48f);
            var c = go.AddComponent<PropClam>();
            c.lid = hinge;
            c.next = (pos.x * 0.13f + pos.z * 0.07f) % 0.3f;
            return c;
        }

        static void Build()
        {
            if (shellMesh != null) return;
            var b = new PropMeshBuilder();
            // fluted half shell: 5 ridges, faceted
            const int ribs = 5;
            for (int i = 0; i < ribs; i++)
            {
                float x0 = Mathf.Lerp(-0.3f, 0.3f, i / (float)ribs), x1 = Mathf.Lerp(-0.3f, 0.3f, (i + 1) / (float)ribs), xm = (x0 + x1) * 0.5f;
                float yEdge = 0.12f;
                Vector3 bl = new Vector3(x0, 0f, -0.2f), br = new Vector3(x1, 0f, -0.2f);
                Vector3 tl = new Vector3(x0, yEdge, 0.22f), tr = new Vector3(x1, yEdge, 0.22f);
                Vector3 ridgeF = new Vector3(xm, yEdge + 0.05f, 0.24f), ridgeB = new Vector3(xm, 0.02f, -0.22f);
                b.Face3(bl, ridgeB, ridgeF, Vector3.down + Vector3.left * 0.2f);
                b.Face3(bl, ridgeF, tl, Vector3.down);
                b.Face3(br, ridgeF, ridgeB, Vector3.down + Vector3.right * 0.2f);
                b.Face3(br, tr, ridgeF, Vector3.down);
            }
            b.DoubleSide(0, 0);
            shellMesh = b.ToMesh("ClamShell");
            b.Clear();
            for (int i = 0; i < 6; i++)
            {
                float x0 = Mathf.Lerp(-0.28f, 0.28f, i / 6f), x1 = Mathf.Lerp(-0.28f, 0.28f, (i + 1) / 6f);
                b.Box(new Vector3((x0 + x1) * 0.5f, 0.11f + (i % 2) * 0.012f, 0.2f), new Vector3(x1 - x0, 0.04f, 0.06f), Quaternion.identity);
            }
            lipMesh = b.ToMesh("ClamLips");
        }

        void Update()
        {
            if (Time.time >= next)
            {
                next = Time.time + 0.2f;
                var p = Refs.Player;
                if (p != null && p.Root != null && p.IsUnderwater)
                {
                    float d2 = (p.Root.position - transform.position).sqrMagnitude;
                    if (d2 < 1.6f * 1.6f || (p.View != null && (p.View.transform.position - transform.position).sqrMagnitude < 1.3f * 1.3f))
                    {
                        if (Time.time > closedUntil - 3.5f && angle > 0.5f)
                        {
                            FXEffects.Bubbles(transform.position + Vector3.up * 0.25f, 10);
                            FXSfx.Play(FXSound.Click, 0.6f);
                        }
                        closedUntil = Time.time + 4f;
                    }
                }
            }
            float target = Time.time < closedUntil ? 0f : 1f;
            if (Mathf.Abs(angle - target) < 0.001f) return;
            angle = Mathf.MoveTowards(angle, target, Time.deltaTime * (target < angle ? 6f : 0.8f));
            if (lid != null) lid.localRotation = Quaternion.Euler(-28f * angle, 0f, 0f);
        }
    }
}
