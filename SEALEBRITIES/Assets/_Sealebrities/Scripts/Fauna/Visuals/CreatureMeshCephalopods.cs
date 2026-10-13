using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Octopus, squid, cuttlefish and vampire squid in the faceted low-poly style. Arms are 5-sided tapered tubes
    /// with 3-5 bone chains (rig.tentacles); body and arms are painted per facet (mottled tones, pale oral side).
    /// Rig: spine[0] = head, spine[1] = mantle (= rig.bell).
    internal static class CreatureMeshCephalopods
    {
        const float Pi = CreatureMeshMath.Pi;

        sealed class ArmInfo
        {
            public Vector3[] Path;
            public CreatureMeshChain Chain;
            public Func<float, float> Radius;
            public Vector3 Aboral;
        }

        // ============================================================================================ helpers

        static ArmInfo Arm(CreatureMeshContext ctx, string name, int parent, Vector3[] path, Func<float, float> radius, Vector3 aboral, int bones, int rings, int sides)
        {
            var fr = new float[bones];
            for (int i = 0; i < bones; i++) fr[i] = i / (float)bones * 0.92f;
            var chain = CreatureMeshParts.ChainAlong(ctx, name, parent, path, fr);
            ctx.Tentacles.Add(chain.Bones[0]);
            // up hint: perpendicular to the tangent inside the plane of the arm's bend, starting at the aboral side
            Vector3 t0 = CreatureMeshMath.Norm(CreatureMeshMath.PathPoint(path, 0.05f) - path[0]);
            Vector3 bendN = Vector3.Cross(t0, aboral);
            if (bendN.sqrMagnitude < 1e-6f) bendN = CreatureMeshMath.Perp(t0);
            bendN = CreatureMeshMath.Norm(bendN);
            Func<float, Vector3> up = t =>
            {
                Vector3 a = CreatureMeshMath.PathPoint(path, Math.Max(0f, t - 0.01f)), c = CreatureMeshMath.PathPoint(path, Math.Min(1f, t + 0.01f));
                Vector3 u = Vector3.Cross(bendN, CreatureMeshMath.Norm(c - a));
                return u.sqrMagnitude > 1e-6f ? u : aboral;
            };
            // sign chosen once so the aboral side faces outward at the root (continuous along the arm)
            float sign = Vector3.Dot(up(0f), aboral) >= 0f ? 1f : -1f;
            var sw = CreatureMeshParts.Tube(path, radius, rings, sides, t => up(t) * sign);
            sw.CapStart = false;
            sw.Uv = (t, v) => CreatureMeshParts.FinUv(3, v, t);
            sw.Build(ctx.B, CreatureMeshSlot.Fin, (p, a, t) => chain.Weigh(a));
            return new ArmInfo { Path = path, Chain = chain, Radius = radius, Aboral = aboral };
        }

        /// Web membrane between two arms up to fraction reach (scalloped), double-sided.
        static void Web(CreatureMeshContext ctx, ArmInfo a, ArmInfo b, float reach, float scallop, int nu, int nv, int band)
        {
            var pa = a.Path; var pb = b.Path;
            Func<float, float, Vector3> pos = (u, v) =>
            {
                float s = v * reach * (1f - scallop * CreatureMeshMath.Sin(Pi * u));
                return Vector3.Lerp(CreatureMeshMath.PathPoint(pa, s), CreatureMeshMath.PathPoint(pb, s), u);
            };
            Func<float, float, BoneWeight> w = (u, v) =>
            {
                float s = v * reach * (1f - scallop * CreatureMeshMath.Sin(Pi * u));
                return CreatureMeshSkin.Blend(a.Chain.Weigh(CreatureMeshMath.PathPoint(pa, s)), b.Chain.Weigh(CreatureMeshMath.PathPoint(pb, s)), u);
            };
            Vector3 outDir = CreatureMeshMath.Norm(Vector3.Lerp(a.Aboral, b.Aboral, 0.5f));
            for (int face = 0; face < 2; face++)
            {
                float sg = face == 0 ? 1f : -1f;
                float off = 0.0015f * sg;
                CreatureMeshParts.Surface(ctx.B, CreatureMeshSlot.Fin, nu, nv,
                    (u, v) => pos(u, v) + outDir * off,
                    (u, v) => CreatureMeshParts.FinUv(band, u, v * reach),
                    p => outDir * sg, (p, u, v) => w(u, v));
            }
        }

        static void Basis(Vector3 axis, out Vector3 e1, out Vector3 e2)
        {
            e1 = Math.Abs(axis.x) < 0.9f ? CreatureMeshMath.Norm(Vector3.Cross(axis, Vector3.right)) : CreatureMeshMath.Norm(Vector3.Cross(axis, Vector3.up));
            e1 = CreatureMeshMath.Norm(Vector3.Cross(e1, axis));   // roughly along X for axes in the YZ plane
            if (e1.x < 0f) e1 = -e1;
            e2 = CreatureMeshMath.Norm(Vector3.Cross(axis, e1));
        }

        static float Rnd(string id, int k, int salt) => CreatureMeshNoise.Rand(k, salt, id != null ? id.Length : 0, 77);

        static void EyeWithLid(CreatureMeshContext ctx, CreatureMeshSweep body, float t, float theta, float r, int bone, float lidScale)
        {
            var b = ctx.B;
            Vector3 p = body.Point(t, theta);
            Vector3 n = body.Normal(t, theta);
            Vector3 look = CreatureMeshMath.Norm(n + Vector3.forward * 0.2f);
            float R = r * CreatureMeshEyes.Look(ctx.EyeStyle).Scale;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 pp = side > 0 ? p : CreatureMeshMath.MirrorX(p);
                Vector3 nn = side > 0 ? n : CreatureMeshMath.MirrorX(n);
                Vector3 lk = side > 0 ? look : CreatureMeshMath.MirrorX(look);
                if (lidScale > 0f)
                {
                    Vector2 uv = new Vector2(t, 0.25f);
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, pp - nn * (R * 0.5f), lk, Vector3.up, R * 1.3f * lidScale, R * 1.25f * lidScale, R * 0.7f, 4, 7,
                        uv, uv, CreatureMeshSkin.Fixed(bone));
                }
                CreatureMeshParts.Eye(ctx, pp + nn * (R * (lidScale > 0f ? 0.05f : -0.3f)), lk, r, CreatureMeshSkin.Fixed(bone));
            }
        }

        // ============================================================================================ octopus

        public static void Octopus(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Octopus;
            ctx.Facet(true, true);
            float Rm = 0.105f * CreatureMeshMath.Clamp(s.bodyWidth / 0.4f, 0.7f, 1.4f);
            var mp = new[] { new Vector3(0f, 0.16f, 0.49f), new Vector3(0f, 0.14f, 0.41f), new Vector3(0f, 0.1f, 0.31f), new Vector3(0f, 0.055f, 0.22f), new Vector3(0f, 0.025f, 0.16f), new Vector3(0f, -0.005f, 0.11f) };
            var rad = new CreatureMeshCurve(0f, 0.62f, 0.15f, 0.92f, 0.35f, 1f, 0.52f, 0.86f, 0.63f, 0.66f, 0.76f, 0.74f, 0.9f, 0.68f, 1f, 0.55f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.05f, 0.14f, 0.26f, 0.4f, 0.52f, 0.63f, 0.72f, 0.82f, 0.92f, 1f }, Sides = 8,
                Center = t => CreatureMeshMath.PathPoint(mp, t),
                HalfW = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.14f) * (t > 0.6f ? 1.08f : 1f),
                HalfTop = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.14f),
                HalfBot = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.14f) * 0.92f,
                CapStart = true, CapEnd = true, CapBulgeEnd = 0.2f,
            };
            int head = ctx.Bone("Head", -1, mp[4]);
            int mantle = ctx.Bone("Mantle", head, mp[2]);
            ctx.Spine.Add(head); ctx.Spine.Add(mantle);
            ctx.Bell = mantle;
            var chain = new CreatureMeshChain(new[] { head, mantle }, new[] { mp[4], mp[2] }, mp[0]);
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            EyeWithLid(ctx, body, 0.76f, Pi * 0.5f + 0.5f, 0.024f, head, 1f);

            // siphon on the right side
            {
                Vector3 p0 = body.Point(0.7f, Pi * 0.5f - 0.6f);
                var path = new[] { p0 - body.Normal(0.7f, Pi * 0.5f - 0.6f) * 0.01f, p0 + new Vector3(0.012f, -0.012f, -0.02f), p0 + new Vector3(0.02f, -0.022f, -0.035f) };
                var sip = CreatureMeshParts.Tube(path, t => CreatureMeshMath.Lerp(0.012f, 0.008f, t), 3, 5, t => Vector3.up);
                sip.CapStart = false;
                sip.Uv = (t, v) => new Vector2(0.7f, v);
                sip.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(head));
            }

            // arms
            Vector3 crown = mp[5] + new Vector3(0f, -0.01f, 0.005f);
            Vector3 A = CreatureMeshMath.Norm(new Vector3(0f, -0.42f, -1f));
            Basis(A, out Vector3 e1, out Vector3 e2);
            var arms = new List<ArmInfo>();
            for (int k = 0; k < 8; k++)
            {
                float phi = (k + 0.5f) / 8f * CreatureMeshMath.TwoPi;
                Vector3 d = CreatureMeshMath.Norm(e1 * CreatureMeshMath.Cos(phi) + e2 * CreatureMeshMath.Sin(phi));
                Vector3 p0 = crown + d * (Rm * 0.42f);
                float len = 0.6f * (0.9f + 0.2f * Rnd(s.id, k, 1));
                Vector3 c = CreatureMeshMath.Norm(Vector3.Cross(d, A));
                float curlDir = Rnd(s.id, k, 2) > 0.5f ? 1f : -1f;
                var path = CreatureMeshParts.GrowPath(p0, d * 0.8f + A * 0.55f, len, 16, i => A + d * 0.25f, 0.12f, c * curlDir,
                    f => f > 0.62f ? 0.42f * (f - 0.62f) / 0.38f : 0f);
                arms.Add(Arm(ctx, "Arm" + k + "_", head, path, t => 0.024f * CreatureMeshMath.Pow(1f - t, 1.1f) + 0.0028f, d, 4, 9, 5));
            }
            for (int k = 0; k < 8; k++) Web(ctx, arms[k], arms[(k + 1) % 8], 0.2f, 0.45f, 2, 2, 3);
        }

        // ============================================================================================ squid

        public static void Squid(CreatureMeshContext ctx)
        {
            if (ctx.S.variant == "nautilus") { CreatureMeshFossils.Nautilus(ctx); return; }   // round 3: volcano living fossil
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Squid;
            ctx.Facet(true, true);
            float Rm = 0.5f * CreatureMeshMath.Clamp(s.bodyHeight, 0.06f, 0.3f);
            float z0 = 0.5f, len = 0.47f;
            var rad = new CreatureMeshCurve(0f, 0.3f, 0.12f, 0.62f, 0.35f, 0.95f, 0.6f, 1f, 0.76f, 0.98f, 0.785f, 0.8f, 0.81f, 0.72f, 0.88f, 0.86f, 1f, 0.7f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.04f, 0.12f, 0.24f, 0.38f, 0.52f, 0.65f, 0.76f, 0.79f, 0.82f, 0.88f, 0.95f, 1f }, Sides = 8,
                Center = t => new Vector3(0f, 0f, z0 - len * t),
                HalfW = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.16f),
                HalfTop = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.16f),
                HalfBot = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.16f),
                CapStart = true, CapEnd = true, CapBulgeEnd = 0.15f,
            };
            // mantle as a short chain (head -> mantle tip); rig.bell = first mantle bone (breathing scales the whole mantle)
            var piv = new[] { new Vector3(0f, 0f, 0.09f), new Vector3(0f, 0f, 0.2f), new Vector3(0f, 0f, 0.31f), new Vector3(0f, 0f, 0.41f) };
            int head = ctx.Bone("Head", -1, piv[0]);
            int mantle = ctx.Bone("Mantle", head, piv[1]);
            int mantle2 = ctx.Bone("Mantle2", mantle, piv[2]);
            int mantle3 = ctx.Bone("Mantle3", mantle2, piv[3]);
            ctx.Spine.Add(head); ctx.Spine.Add(mantle); ctx.Spine.Add(mantle2); ctx.Spine.Add(mantle3);
            ctx.Bell = mantle;
            var chain = new CreatureMeshChain(new[] { head, mantle, mantle2, mantle3 }, piv, new Vector3(0f, 0f, 0.5f));
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            // the biggest eyes in the animal kingdom
            EyeWithLid(ctx, body, 0.89f, Pi * 0.5f + 0.1f, Rm * 0.58f, head, 0.85f);

            // rhombic fins at the mantle tip (pectoral bones so they undulate; roots stay on the mantle)
            {
                Vector3 fp = body.Point(0.16f, Pi * 0.5f);
                int finR = ctx.Bone("FinR", mantle3, fp), finL = ctx.Bone("FinL", mantle3, CreatureMeshMath.MirrorX(fp));
                ctx.Pectorals.Add(finR); ctx.Pectorals.Add(finL);
                CreatureMeshAnatomy.PairedFinBlend(ctx,
                    q => body.Point(CreatureMeshMath.Lerp(0.02f, 0.3f, q), Pi * 0.5f) - Vector3.right * 0.003f,
                    q =>
                    {
                        float t = CreatureMeshMath.Lerp(0.02f, 0.3f, q);
                        Vector3 bp = body.Point(t, Pi * 0.5f);
                        float w = 0.085f * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * CreatureMeshMath.Pow(q, 1.3f)), 0.9f);
                        return bp + Vector3.right * w + Vector3.back * (0.03f * q * q);
                    },
                    finR, finL, chain.Weigh, 4, 1, 0, 0.004f);
            }

            // 8 arms + 2 long tentacles from the crown
            Vector3 crown = new Vector3(0f, 0f, z0 - len + 0.005f);
            Vector3 A = Vector3.back;
            for (int k = 0; k < 10; k++)
            {
                bool tentacle = k >= 8;
                float phi = tentacle ? (k == 8 ? -0.35f : 0.35f) - Pi * 0.5f : (k + 0.5f) / 8f * CreatureMeshMath.TwoPi;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(phi), CreatureMeshMath.Sin(phi), 0f);
                Vector3 p0 = crown + d * (Rm * (tentacle ? 0.3f : 0.55f));
                if (!tentacle)
                {
                    float L = 0.24f * (0.92f + 0.16f * Rnd(s.id, k, 3));
                    var path = CreatureMeshParts.GrowPath(p0, A + d * 0.35f, L, 12, i => A - d * 0.08f, 0.14f, Vector3.Cross(d, A), f => f > 0.7f ? 0.12f : 0f);
                    Arm(ctx, "Arm" + k + "_", head, path, t => Rm * 0.22f * CreatureMeshMath.Pow(1f - t, 1.1f) + 0.0025f, d, 4, 6, 5);
                }
                else
                {
                    float L = 0.535f;
                    var path = CreatureMeshParts.GrowPath(p0, A + d * 0.12f, L, 18, i => A + Vector3.down * 0.05f, 0.1f, Vector3.right, f => 0.02f * CreatureMeshMath.Sin(f * 9f));
                    Arm(ctx, "Tentacle" + (k - 8) + "_", head, path,
                        t => t < 0.8f ? Rm * 0.1f * (1f - 0.35f * t) : Rm * 0.1f * 0.72f + Rm * 0.14f * CreatureMeshMath.Sin(Pi * CreatureMeshMath.Clamp01((t - 0.8f) / 0.2f)) + 0.002f,
                        d, 5, 11, 4);
                }
            }
        }

        // ============================================================================================ cuttlefish

        public static void Cuttlefish(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            bool flamboyant = (s.variant ?? "") == "flamboyant";
            ctx.EyeStyle = flamboyant ? CreatureMeshEyeStyle.Friendly : CreatureMeshEyeStyle.Cuttle;
            ctx.Facet(true, true);
            float hw = 0.5f * CreatureMeshMath.Clamp(s.bodyWidth, 0.2f, 0.5f);
            float hh = 0.5f * CreatureMeshMath.Clamp(s.bodyHeight, 0.18f, 0.45f);
            float z0 = 0.36f, len = 0.86f;
            var wid = new CreatureMeshCurve(0f, 0.5f, 0.06f, 0.7f, 0.13f, 0.66f, 0.17f, 0.6f, 0.24f, 0.86f, 0.5f, 1f, 0.75f, 0.92f, 0.9f, 0.72f, 1f, 0.5f);
            var top = new CreatureMeshCurve(0f, 0.42f, 0.07f, 0.62f, 0.13f, 0.6f, 0.17f, 0.55f, 0.24f, 0.72f, 0.5f, 0.82f, 0.75f, 0.74f, 0.9f, 0.6f, 1f, 0.4f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.03f, 0.08f, 0.14f, 0.2f, 0.28f, 0.4f, 0.52f, 0.64f, 0.76f, 0.87f, 0.95f, 1f }, Sides = 8,
                Center = t => new Vector3(0f, 0f, z0 - len * t),
                HalfW = t => hw * wid[t] * CreatureMeshMath.Nose(t, 0.05f) * CreatureMeshMath.Nose(1f - t, 0.1f),
                HalfTop = t => hh * top[t] * CreatureMeshMath.Nose(t, 0.05f) * CreatureMeshMath.Nose(1f - t, 0.1f),
                HalfBot = t => hh * top[t] * 0.78f * CreatureMeshMath.Nose(t, 0.05f) * CreatureMeshMath.Nose(1f - t, 0.1f),
                CapStart = true, CapEnd = true,
            };
            int head = ctx.Bone("Head", -1, new Vector3(0f, 0f, 0.28f));
            int m1 = ctx.Bone("Mantle", head, new Vector3(0f, 0f, 0.1f));
            int m2 = ctx.Bone("MantleRear", m1, new Vector3(0f, 0f, -0.2f));
            ctx.Spine.Add(head); ctx.Spine.Add(m1); ctx.Spine.Add(m2);
            ctx.Bell = m1;
            var chain = new CreatureMeshChain(new[] { head, m1, m2 }, new[] { new Vector3(0f, 0f, 0.28f), new Vector3(0f, 0f, 0.1f), new Vector3(0f, 0f, -0.2f) }, new Vector3(0f, 0f, -0.5f));
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            EyeWithLid(ctx, body, 0.1f, Pi * 0.5f + 0.55f, 0.03f, head, 1f);

            // undulating skirt fin along both sides of the mantle (pectoral bones; roots stay on the mantle)
            {
                Vector3 sp = body.Point(0.55f, Pi * 0.5f - 0.12f);
                int skR = ctx.Bone("SkirtR", m1, sp), skL = ctx.Bone("SkirtL", m1, CreatureMeshMath.MirrorX(sp));
                ctx.Pectorals.Add(skR); ctx.Pectorals.Add(skL);
                CreatureMeshAnatomy.PairedFinBlend(ctx,
                    q => body.Point(CreatureMeshMath.Lerp(0.2f, 0.985f, q), Pi * 0.5f - 0.12f),
                    q =>
                    {
                        float t = CreatureMeshMath.Lerp(0.2f, 0.985f, q);
                        Vector3 bp = body.Point(t, Pi * 0.5f - 0.12f);
                        float w = 0.04f * CreatureMeshMath.Nose(q, 0.08f) * (q > 0.9f ? CreatureMeshMath.Lerp(1f, 0.5f, (q - 0.9f) / 0.1f) : 1f);
                        return bp + new Vector3(w, -0.006f + 0.011f * CreatureMeshMath.Sin(q * Pi * 9f), -0.01f * q);
                    },
                    skR, skL, chain.Weigh, 18, 1, 0, 0.004f, 0.6f);
            }

            // 8 short arms clustered forward
            Vector3 crown = new Vector3(0f, -0.005f, z0 + 0.004f);
            for (int k = 0; k < 8; k++)
            {
                float phi = (k + 0.5f) / 8f * CreatureMeshMath.TwoPi;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(phi), CreatureMeshMath.Sin(phi) * 0.8f, 0f);
                Vector3 p0 = crown + d * 0.04f;
                bool walkArm = flamboyant && d.y < -0.3f;   // flamboyant cuttlefish: the lowest arms are its "legs"
                float L = 0.145f * (0.9f + 0.2f * Rnd(s.id, k, 4)) * (d.y < -0.3f ? (walkArm ? 1.7f : 1.12f) : 1f);
                var path = CreatureMeshParts.GrowPath(p0, Vector3.forward + d * 0.4f, L, 8, i => walkArm ? Vector3.forward * 0.25f + Vector3.down * 1.4f + d * 0.15f : Vector3.forward * 0.6f + Vector3.down * 0.45f + d * 0.1f, 0.18f,
                    Vector3.Cross(Vector3.forward, d), f => 0.05f + 0.08f * f);
                Arm(ctx, "Arm" + k + "_", head, path, t => 0.021f * CreatureMeshMath.Pow(1f - t, 0.75f) + 0.005f, d, 3, 5, 5);
            }
        }

        static Vector3 Mx(Vector3 p, float side) => side < 0f ? CreatureMeshMath.MirrorX(p) : p;

        // ============================================================================================ vampire squid

        public static void VampireSquid(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Vampire;
            ctx.Facet(true, false);
            float Rm = 0.125f * CreatureMeshMath.Clamp(s.bodyWidth / 0.5f, 0.7f, 1.4f);
            float z0 = 0.5f, len = 0.4f;
            var rad = new CreatureMeshCurve(0f, 0.55f, 0.15f, 0.9f, 0.35f, 1f, 0.58f, 0.9f, 0.72f, 0.74f, 0.85f, 0.86f, 1f, 0.78f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.06f, 0.16f, 0.3f, 0.45f, 0.58f, 0.7f, 0.8f, 0.9f, 1f }, Sides = 8,
                Center = t => new Vector3(0f, 0f, z0 - len * t),
                HalfW = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.2f),
                HalfTop = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.2f),
                HalfBot = t => Rm * rad[t] * CreatureMeshMath.Nose(t, 0.2f) * 0.95f,
                CapStart = true, CapEnd = true, CapBulgeEnd = 0.1f,
            };
            var piv = new[] { new Vector3(0f, 0f, 0.16f), new Vector3(0f, 0f, 0.28f), new Vector3(0f, 0f, 0.39f) };
            int head = ctx.Bone("Head", -1, piv[0]);
            int mantle1 = ctx.Bone("Mantle", head, piv[1]);
            int mantle = ctx.Bone("Mantle2", mantle1, piv[2]);
            ctx.Spine.Add(head); ctx.Spine.Add(mantle1); ctx.Spine.Add(mantle);
            var chain = new CreatureMeshChain(new[] { head, mantle1, mantle }, piv, new Vector3(0f, 0f, 0.5f));
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            EyeWithLid(ctx, body, 0.86f, Pi * 0.5f + 0.15f, Rm * 0.36f, head, 0.9f);

            // ear-like fins (pectoral bones so they flap)
            {
                Vector3 pa = body.Point(0.2f, Pi * 0.5f + 0.25f), pb = body.Point(0.34f, Pi * 0.5f + 0.25f);
                Vector3 mid = (pa + pb) * 0.5f;
                int fr = ctx.Bone("FinR", mantle, mid), fl = ctx.Bone("FinL", mantle, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(fr); ctx.Pectorals.Add(fl);
                CreatureMeshAnatomy.PairedFinBlend(ctx,
                    q => Vector3.Lerp(pa, pb, q) - Vector3.right * 0.004f,
                    q => Vector3.Lerp(pa, pb, q) + new Vector3(0.075f, 0.035f, -0.02f) * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * q), 0.6f) + Vector3.back * 0.02f * q,
                    fr, fl, chain.Weigh, 4, 1, 0, 0.005f, 0.3f);
                // photophores at the fin bases
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 c = Mx(pb + new Vector3(0.002f, 0.004f, -0.012f), side);
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Glow, c, Vector3.forward, Vector3.up, 0.012f, 0.012f, 0.01f, 3, 5, Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(mantle));
                }
            }
            // scattered small photophores on the mantle
            for (int k = 0; k < 6; k++)
            {
                float t = CreatureMeshMath.Lerp(0.15f, 0.65f, Rnd(s.id, k, 5));
                float th = CreatureMeshMath.Lerp(0.6f, CreatureMeshMath.TwoPi - 0.6f, Rnd(s.id, k, 6));
                Vector3 p = body.Point(t, th);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Glow, p, body.Normal(t, th), Vector3.up, 0.006f, 0.006f, 0.004f, 3, 4, Vector2.zero, Vector2.zero, chain.ByAnchor);
            }

            // webbed cloak: 8 arms flaring back like an umbrella, web up to ~85 %
            Vector3 crown = new Vector3(0f, 0f, z0 - len + 0.01f);
            Vector3 back = Vector3.back;
            var arms = new List<ArmInfo>();
            for (int k = 0; k < 8; k++)
            {
                float phi = (k + 0.5f) / 8f * CreatureMeshMath.TwoPi;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(phi), CreatureMeshMath.Sin(phi), 0f);
                Vector3 p0 = crown + d * (Rm * 0.62f);
                float L = 0.7f;
                var path = CreatureMeshParts.GrowPath(p0, d * 0.95f + back * 0.6f, L, 14, i => back + d * 0.35f, 0.1f, Vector3.Cross(back, d), f => f > 0.65f ? 0.06f : 0f);
                var arm = Arm(ctx, "Arm" + k + "_", head, path, t => 0.019f * CreatureMeshMath.Pow(1f - t, 0.9f) + 0.004f, d, 4, 8, 5);
                arms.Add(arm);
                // fleshy cirri on the inner side
                for (int c = 0; c < 3; c++)
                {
                    float f = 0.22f + c * 0.2f;
                    Vector3 p = CreatureMeshMath.PathPoint(path, f);
                    Vector3 inward = -d;
                    float r = arm.Radius(f);
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Fin, p + inward * (r * 0.6f), p + inward * (r + 0.016f) + back * 0.004f, r * 0.4f, 3,
                        CreatureMeshParts.FinUv(3, 0.05f, f), CreatureMeshParts.FinUv(3, 0.02f, f), arm.Chain.Weigh(p));
                }
                // glowing arm tip
                Vector3 tip = CreatureMeshMath.PathPoint(path, 0.985f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Glow, tip, CreatureMeshMath.Norm(tip - CreatureMeshMath.PathPoint(path, 0.95f)), d, 0.007f, 0.007f, 0.008f, 3, 4,
                    Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(arm.Chain.Bones[arm.Chain.Bones.Length - 1]));
            }
            for (int k = 0; k < 8; k++) Web(ctx, arms[k], arms[(k + 1) % 8], 0.86f, 0.2f, 2, 4, 0);
        }
    }
}
