using System;
using UnityEngine;

namespace Sealebrities
{
    /// Jellyfish: 1 m bell diameter (axis +Y), translucent double-sheet bell with an in-curled rim, four
    /// horseshoe gonads, 4 frilly oral arms and 16 thin marginal tentacles (bone chains under the bell bone).
    internal static class CreatureMeshJelly
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.Layout = CreatureMeshLayout.Bell;
            ctx.CastShadows = false;
            ctx.SkinSmoothness = 0.9f;
            const float R = 0.5f;
            float Hb = CreatureMeshMath.Clamp(s.bodyHeight * 0.56f, 0.16f, 0.6f);
            const float aMax = 1.74f;
            float yApex = Hb * 0.55f;
            Func<float, Vector2> prof = a => new Vector2(R * CreatureMeshMath.Sin(a), yApex - Hb * (1f - CreatureMeshMath.Cos(a)));   // (radius, y)
            float rimY = prof(aMax).y;

            int bell = ctx.Bone("Bell", -1, new Vector3(0f, 0f, 0f));
            ctx.Bell = bell;
            var bellW = CreatureMeshSkin.One(bell);

            // ---- bell: outer sheet, inner sheet, rim lip (Skin slot = translucent bell material)
            const int NA = 16, NP = 40;
            Func<float, float, Vector3> outer = (u, v) =>
            {
                float a = v * aMax;
                Vector2 pr = prof(a);
                float ang = u * CreatureMeshMath.TwoPi;
                // subtle lobes at the margin (8 lappets)
                float lobe = 1f + 0.02f * CreatureMeshMath.Smooth(0.75f, 1f, v) * CreatureMeshMath.Cos(ang * 8f);
                return new Vector3(CreatureMeshMath.Cos(ang) * pr.x * lobe, pr.y, CreatureMeshMath.Sin(ang) * pr.x * lobe);
            };
            Func<float, float, Vector3> inner = (u, v) =>
            {
                Vector3 p = outer(u, v);
                float th = CreatureMeshMath.Lerp(0.07f, 0.008f, CreatureMeshMath.Pow(v, 0.8f)) * Hb / 0.28f;
                // offset roughly along the inward normal of the profile
                float a = v * aMax;
                Vector3 radial = new Vector3(p.x, 0f, p.z);
                float rl = radial.magnitude;
                radial = rl > 1e-5f ? radial / rl : Vector3.zero;
                Vector3 nOut = CreatureMeshMath.Norm(radial * CreatureMeshMath.Sin(a) * (Hb / R) + Vector3.up * CreatureMeshMath.Cos(a));
                return p - nOut * th;
            };
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NP, NA, (u, v) => outer(u, v), (u, v) => new Vector2(u, v),
                p => CreatureMeshMath.Norm(new Vector3(p.x, (p.y - rimY * 0.5f) * 1.2f + 0.05f, p.z)), (p, u, v) => bellW);
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NP, NA, (u, v) => inner(u, v), (u, v) => new Vector2(u, v * 0.98f),
                p => -CreatureMeshMath.Norm(new Vector3(p.x, (p.y - rimY * 0.5f) * 1.2f + 0.05f, p.z)), (p, u, v) => bellW);
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NP, 1, (u, v) => v < 0.5f ? outer(u, 1f) : inner(u, 1f), (u, v) => new Vector2(u, 0.99f),
                p => CreatureMeshMath.Norm(new Vector3(p.x, -0.4f, p.z)), (p, u, v) => bellW);

            // ---- gonads: four horseshoes seen through the bell (Shell slot)
            for (int k = 0; k < 4; k++)
            {
                float a0 = (k + 0.5f) * Pi * 0.5f;
                Vector3 c = new Vector3(CreatureMeshMath.Cos(a0) * 0.16f, 0f, CreatureMeshMath.Sin(a0) * 0.16f);
                var path = new Vector3[9];
                for (int i = 0; i < 9; i++)
                {
                    float ang = a0 + Pi + CreatureMeshMath.Lerp(-2.3f, 2.3f, i / 8f);
                    Vector3 p = c + new Vector3(CreatureMeshMath.Cos(ang), 0f, CreatureMeshMath.Sin(ang)) * 0.075f;
                    float rr = new Vector2(p.x, p.z).magnitude;
                    float aa = (float)Math.Asin(CreatureMeshMath.Clamp(rr / R, 0f, 1f));
                    p.y = prof(aa).y - 0.05f * Hb / 0.28f;
                    path[i] = p;
                }
                var g = CreatureMeshParts.Tube(path, t => 0.02f * CreatureMeshMath.Nose(Math.Min(t, 1f - t), 0.15f) + 0.004f, 18, 8, t => Vector3.up);
                g.HalfTop = t => 0.011f * CreatureMeshMath.Nose(Math.Min(t, 1f - t), 0.15f) + 0.003f;
                g.HalfBot = g.HalfTop;
                g.Uv = (t, vv) => new Vector2(0.5f, 0.5f);
                g.Build(b, CreatureMeshSlot.Shell, (p, a, t) => bellW);
            }

            // ---- oral arms: 4 frilly ribbons from the center underside
            float armLen = 0.55f;
            for (int k = 0; k < 4; k++)
            {
                float a0 = (k + 0.0f) * Pi * 0.5f + 0.2f;
                Vector3 dir = new Vector3(CreatureMeshMath.Cos(a0), 0f, CreatureMeshMath.Sin(a0));
                Vector3 p0 = dir * 0.04f + Vector3.up * (rimY + Hb * 0.35f);
                var path = new[]
                {
                    p0,
                    p0 + dir * 0.05f + Vector3.down * armLen * 0.3f,
                    p0 + dir * 0.07f + Vector3.down * armLen * 0.62f + Perp(dir) * 0.03f,
                    p0 + dir * 0.06f + Vector3.down * armLen - Perp(dir) * 0.02f,
                };
                var ch = CreatureMeshParts.ChainAlong(ctx, "OralArm" + k + "_", bell, path, new[] { 0f, 0.36f, 0.7f });
                ctx.Tentacles.Add(ch.Bones[0]);
                float twist0 = a0;
                var arm = new CreatureMeshSweep
                {
                    Rings = 30, Sides = 10,
                    Center = t => CreatureMeshMath.PathPoint(path, t),
                    UpHint = t => CreatureMeshMath.Rotate(dir, Vector3.up, Pi * 0.5f + t * 2.2f),
                    HalfW = t => CreatureMeshMath.Lerp(0.05f, 0.012f, t),
                    HalfTop = t => CreatureMeshMath.Lerp(0.007f, 0.003f, t),
                    HalfBot = t => CreatureMeshMath.Lerp(0.007f, 0.003f, t),
                    Radial = (t, th) => 1f + 0.35f * CreatureMeshMath.Sin(t * 70f + k) * CreatureMeshMath.Pow(CreatureMeshMath.Abs(CreatureMeshMath.Sin(th)), 6f),
                    ArcLength = false, Spacing = 0, CapStart = true, CapEnd = true,
                    Uv = (t, vv) => new Vector2(0.5f, t),
                };
                arm.Build(b, CreatureMeshSlot.Extra, (p, a, t) => ch.Weigh(a));
            }

            // ---- 16 thin marginal tentacles
            float tLen = 0.42f;
            for (int k = 0; k < 16; k++)
            {
                float ang = (k + 0.5f) / 16f * CreatureMeshMath.TwoPi;
                Vector3 dir = new Vector3(CreatureMeshMath.Cos(ang), 0f, CreatureMeshMath.Sin(ang));
                Vector2 rim = prof(aMax);
                Vector3 p0 = dir * (rim.x * 0.97f) + Vector3.up * (rim.y + 0.005f);
                float wave = CreatureMeshNoise.Rand(k, 3, 1, 5) * 0.5f + 0.75f;
                var path = new[]
                {
                    p0,
                    p0 + dir * 0.015f + Vector3.down * tLen * 0.3f * wave,
                    p0 - dir * 0.01f + Vector3.down * tLen * 0.62f * wave + Perp(dir) * 0.02f,
                    p0 - dir * 0.03f + Vector3.down * tLen * wave - Perp(dir) * 0.015f,
                };
                var ch = CreatureMeshParts.ChainAlong(ctx, "Tentacle" + k + "_", bell, path, new[] { 0f, 0.36f, 0.7f });
                ctx.Tentacles.Add(ch.Bones[0]);
                var tube = CreatureMeshParts.Tube(path, t => CreatureMeshMath.Lerp(0.0055f, 0.0015f, t), 14, 4, t => dir);
                tube.CapStart = false;
                tube.Uv = (t, vv) => new Vector2(0.5f, t);
                tube.Build(b, CreatureMeshSlot.Extra, (p, a, t) => ch.Weigh(a));
            }
        }

        static Vector3 Perp(Vector3 dir) => new Vector3(-dir.z, 0f, dir.x);
    }
}
