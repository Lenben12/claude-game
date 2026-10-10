using System;
using UnityEngine;

namespace Sealebrities
{
    /// Sharks: torpedo body, falcate dorsal, heterocercal tail, gill slits. Variants "hammer" and "whale".
    internal static class CreatureMeshShark
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            string v = s.variant ?? "";
            bool hammer = v == "hammer", whale = v == "whale";
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.06f, 0.4f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.06f, 0.4f);
            float F = CreatureMeshMath.Clamp(s.finSize, 0.4f, 2f);
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Shark;
            ctx.SkinSmoothness = 0.5f;

            float Lb = whale ? 0.72f : 0.74f;
            CreatureMeshCurve top, bot, wid;
            float nose;
            if (whale)
            {
                top = new CreatureMeshCurve(0f, 0.2f, 0.03f, 0.3f, 0.08f, 0.36f, 0.18f, 0.44f, 0.32f, 0.5f, 0.48f, 0.48f, 0.65f, 0.38f, 0.82f, 0.24f, 0.93f, 0.16f, 1f, 0.13f);
                bot = new CreatureMeshCurve(0f, -0.14f, 0.03f, -0.24f, 0.1f, -0.35f, 0.22f, -0.46f, 0.36f, -0.5f, 0.5f, -0.46f, 0.66f, -0.34f, 0.82f, -0.2f, 0.93f, -0.13f, 1f, -0.1f);
                wid = new CreatureMeshCurve(0f, 0.72f, 0.03f, 0.84f, 0.1f, 0.93f, 0.25f, 1f, 0.45f, 0.92f, 0.65f, 0.7f, 0.85f, 0.4f, 1f, 0.25f);
                nose = 0.022f;
            }
            else if (hammer)
            {
                top = new CreatureMeshCurve(0f, 0.12f, 0.05f, 0.2f, 0.12f, 0.3f, 0.22f, 0.42f, 0.34f, 0.5f, 0.46f, 0.5f, 0.6f, 0.42f, 0.75f, 0.3f, 0.88f, 0.2f, 1f, 0.15f);
                bot = new CreatureMeshCurve(0f, -0.08f, 0.05f, -0.16f, 0.12f, -0.27f, 0.22f, -0.4f, 0.34f, -0.48f, 0.46f, -0.48f, 0.6f, -0.38f, 0.75f, -0.24f, 0.88f, -0.15f, 1f, -0.12f);
                wid = new CreatureMeshCurve(0f, 0.45f, 0.05f, 0.5f, 0.12f, 0.65f, 0.25f, 0.9f, 0.38f, 1f, 0.55f, 0.88f, 0.75f, 0.55f, 0.9f, 0.3f, 1f, 0.22f);
                nose = 0.03f;
            }
            else
            {
                top = new CreatureMeshCurve(0f, 0.06f, 0.04f, 0.2f, 0.1f, 0.32f, 0.2f, 0.43f, 0.32f, 0.5f, 0.45f, 0.5f, 0.6f, 0.42f, 0.75f, 0.3f, 0.88f, 0.2f, 1f, 0.15f);
                bot = new CreatureMeshCurve(0f, -0.03f, 0.04f, -0.14f, 0.1f, -0.26f, 0.2f, -0.4f, 0.32f, -0.48f, 0.45f, -0.48f, 0.6f, -0.38f, 0.75f, -0.24f, 0.88f, -0.15f, 1f, -0.12f);
                wid = new CreatureMeshCurve(0f, 0.25f, 0.05f, 0.55f, 0.12f, 0.78f, 0.25f, 0.95f, 0.38f, 1f, 0.55f, 0.88f, 0.75f, 0.55f, 0.9f, 0.3f, 1f, 0.22f);
                nose = 0.05f;
            }

            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, nose, 34, whale ? 40 : 22);
            if (whale)
            {
                body.Square = t => CreatureMeshMath.Lerp(2.9f, 2.15f, CreatureMeshMath.Smooth(0f, 0.3f, t));
                body.Radial = (t, th) =>
                {
                    float env = CreatureMeshMath.Smooth(0.1f, 0.2f, t) * (1f - CreatureMeshMath.Smooth(0.72f, 0.88f, t));
                    float r = 0f;
                    float[] off = { 0.36f, 0.82f, 1.3f };
                    for (int i = 0; i < off.Length; i++)
                    {
                        float a = 0.055f - 0.008f * i;
                        r += a * (CreatureMeshMath.Gauss(th, Pi + off[i], 0.055f) + CreatureMeshMath.Gauss(th, Pi - off[i], 0.055f));
                    }
                    float keel = 0.14f * CreatureMeshMath.Smooth(0.8f, 0.95f, t) * (CreatureMeshMath.Gauss(th, Pi * 0.5f, 0.18f) + CreatureMeshMath.Gauss(th, Pi * 1.5f, 0.18f));
                    return 1f + r * env + keel;
                };
            }

            var chain = ctx.SpineAlong(body, new[] { 0.14f, 0.3f, 0.45f, 0.6f, 0.74f, 0.87f }, -1);
            int head = ctx.Spine[0];
            int last = ctx.Spine[ctx.Spine.Count - 1];
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("TailFin", last, ped);
            ctx.TailFin = tail;

            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.SkinFrom(body);
            ctx.GillU = whale ? 0.12f : (hammer ? 0.19f : 0.17f);
            ctx.PeduncleU = 0.92f;

            // ---- head features
            if (hammer) Cephalofoil(ctx, body, H, head);
            else
            {
                float et = whale ? 0.07f : 0.075f;
                float eth = whale ? Pi * 0.5f + 0.02f : Pi * 0.5f + 0.28f;
                float er = whale ? 0.011f : Math.Max(0.008f, 0.085f * H);
                ctx.EyeU = et;
                CreatureMeshParts.EyePair(b, body, et, eth, er, 0.15f, 0.5f, 0.8f, CreatureMeshSkin.Fixed(head));
            }

            if (whale)
            {
                // wide terminal mouth across the blunt head
                Vector3 c = body.Center(0.012f);
                float hw = body.HalfW(0.03f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, new Vector3(0f, c.y - body.HalfBot(0.03f) * 0.28f, 0.5f - 0.006f), Vector3.forward, Vector3.up,
                    hw * 0.86f, 0.0085f, 0.016f, 7, 16, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), CreatureMeshSkin.Fixed(head));
            }
            else
            {
                // crescent mouth under the snout
                var path = new Vector3[9];
                float t0 = hammer ? 0.07f : 0.085f;
                for (int k = 0; k < 9; k++)
                {
                    float th = CreatureMeshMath.Lerp(-0.85f, 0.85f, k / 8f);
                    float t = t0 + 0.028f * (th / 0.85f) * (th / 0.85f);
                    path[k] = body.Point(t, th) - body.Normal(t, th) * 0.0012f;
                }
                float mr = Math.Max(0.0025f, 0.03f * H);
                var tube = CreatureMeshParts.Tube(path, t => mr * (0.35f + 0.65f * CreatureMeshMath.Sin(Pi * t)), 12, 6, t => Vector3.forward);
                tube.Build(b, CreatureMeshSlot.Mouth, (p, a, t) => CreatureMeshSkin.One(head));
            }

            // ---- fins
            float dH = H * F * (hammer ? 1.15f : whale ? 0.62f : 0.8f);
            float d0 = whale ? 0.5f : (hammer ? 0.3f : 0.33f), d1 = d0 + (whale ? 0.12f : 0.14f);
            CreatureMeshAnatomy.MedianFin(ctx, body, d0, d1, true, q => FalcateH(q) * dH, q => FalcateRake(q) * dH, 18, 0, chain.ByAnchor, 0.012f * H / 0.13f);
            float d2 = whale ? 0.74f : 0.72f;
            CreatureMeshAnatomy.MedianFin(ctx, body, d2, d2 + 0.065f, true, q => FalcateH(q) * dH * 0.32f, q => FalcateRake(q) * dH * 0.32f, 10, 0, chain.ByAnchor, 0.006f);
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.76f, 0.82f, false, q => FalcateH(q) * dH * 0.3f, q => FalcateRake(q) * dH * 0.3f, 10, 0, chain.ByAnchor, 0.006f);

            // pelvic pair (skinned)
            {
                int pb = CreatureMeshContext.DominantBone(chain, body.Center(0.62f));
                float len = dH * 0.38f;
                CreatureMeshAnatomy.PairedFin(ctx,
                    q => body.Point(0.6f + 0.05f * q, 0.42f),
                    q => body.Point(0.6f + 0.05f * q, 0.42f) + CreatureMeshMath.Norm(new Vector3(0.45f, -0.35f, -0.82f)) * (len * (1f - 0.8f * q)),
                    pb, pb, 6, 3, 2, 0.006f);
            }

            // pectorals (bones)
            {
                float pt = whale ? 0.22f : 0.21f;
                float pl = (whale ? 0.24f : hammer ? 0.16f : 0.17f) * F;
                Vector3 pa = body.Point(pt, Pi * 0.5f - 0.72f), pbp = body.Point(pt + 0.07f, Pi * 0.5f - 0.78f);
                Vector3 mid = (pa + pbp) * 0.5f;
                int bone = CreatureMeshContext.DominantBone(chain, mid);
                int pr = ctx.Bone("PectoralR", bone, mid);
                int plb = ctx.Bone("PectoralL", bone, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(pr);
                ctx.Pectorals.Add(plb);
                CreatureMeshAnatomy.PairedFin(ctx,
                    q => Vector3.Lerp(pa, pbp, q) + Vector3.left * 0.003f,
                    q =>
                    {
                        Vector3 d = CreatureMeshMath.Norm(new Vector3(0.82f - 0.25f * q, -0.32f, -0.42f - 0.65f * q));
                        return Vector3.Lerp(pa, pbp, q) + d * (pl * (1f - 0.82f * CreatureMeshMath.Pow(q, 1.25f)));
                    },
                    pr, plb, 10, 4, 2, 0.012f * H / 0.13f,
                    (q, r) => Vector3.up * (-0.01f * r * r));
            }

            // heterocercal / lunate caudal
            {
                float bh = Math.Max(0.01f, body.HalfTop(0.97f) * 1.2f);
                float Lc = 0.27f * (0.85f + 0.15f * F);
                CreatureMeshCurve len, lift;
                if (whale)
                {
                    len = new CreatureMeshCurve(0f, 0.72f, 0.25f, 0.36f, 0.45f, 0.24f, 0.7f, 0.55f, 1f, 1f);
                    lift = new CreatureMeshCurve(0f, -0.72f, 0.45f, -0.02f, 1f, 0.8f);
                }
                else
                {
                    len = new CreatureMeshCurve(0f, 0.4f, 0.12f, 0.42f, 0.3f, 0.18f, 0.45f, 0.22f, 0.7f, 0.56f, 0.9f, 0.86f, 1f, 1f);
                    lift = new CreatureMeshCurve(0f, -0.5f, 0.3f, -0.1f, 0.5f, 0.06f, 0.75f, 0.3f, 1f, 0.52f);
                }
                CreatureMeshAnatomy.CaudalFin(ctx, ped, bh, q => len[q] * Lc, q => lift[q] * Lc, tail, 18, 0.012f * H / 0.13f);
            }
        }

        // falcate (shark) fin outline, q 0 = front base .. 1 = rear base
        static float FalcateH(float q)
        {
            const float apex = 0.62f;
            if (q <= apex) return CreatureMeshMath.Pow(q / apex, 0.8f);
            return CreatureMeshMath.Pow(1f - (q - apex) / (1f - apex), 1.7f);
        }

        static float FalcateRake(float q)
        {
            const float apex = 0.62f;
            if (q <= apex) return 0.55f * CreatureMeshMath.Pow(q / apex, 1.3f);
            float f = (q - apex) / (1f - apex);
            return 0.55f * CreatureMeshMath.Pow(1f - f, 0.55f) + 0.12f * CreatureMeshMath.Sin(Pi * f);
        }

        static void Cephalofoil(CreatureMeshContext ctx, CreatureMeshSweep body, float H, int head)
        {
            var b = ctx.B;
            float zH = 0.5f - 0.038f;
            float y = body.Center(0.03f).y + 0.002f;
            float hw = 0.14f;
            Func<float, float> xOf = tau => CreatureMeshMath.Lerp(-hw, hw, tau);
            var foil = new CreatureMeshSweep
            {
                Rings = 30, Sides = 14,
                Center = tau => new Vector3(xOf(tau), y, zH - 0.055f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * tau - 1f), 1.5f)),
                HalfW = tau => CreatureMeshMath.Lerp(0.058f, 0.032f, CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * tau - 1f), 1.3f)) * CreatureMeshMath.Nose(Math.Min(tau, 1f - tau), 0.02f),
                HalfTop = tau => CreatureMeshMath.Lerp(0.015f, 0.009f, CreatureMeshMath.Abs(2f * tau - 1f)) * CreatureMeshMath.Nose(Math.Min(tau, 1f - tau), 0.02f),
                HalfBot = tau => CreatureMeshMath.Lerp(0.012f, 0.007f, CreatureMeshMath.Abs(2f * tau - 1f)) * CreatureMeshMath.Nose(Math.Min(tau, 1f - tau), 0.02f),
                Radial = (tau, th) =>
                {
                    float sn = CreatureMeshMath.Sin(th);
                    if (sn <= 0f) return 1f;
                    float x = CreatureMeshMath.Abs(xOf(tau));
                    float notch = 0.22f * CreatureMeshMath.Gauss(x, 0f, 0.016f) + 0.1f * CreatureMeshMath.Gauss(x, 0.07f, 0.018f);
                    return 1f - notch * sn * sn * sn * sn;
                },
                Spacing = 1, SpacingStrength = 0.6f,
                CapStart = false, CapEnd = false,
                Uv = (tau, vv) => new Vector2(0.035f, vv),
            };
            foil.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(head));
            ctx.EyeU = 0.035f;
            for (int side = -1; side <= 1; side += 2)
            {
                float tau = side < 0 ? 0f : 1f;
                Vector3 tip = foil.Center(tau);
                Vector3 c = tip + new Vector3(side * 0.003f, 0.001f, 0.004f);
                Vector3 look = CreatureMeshMath.Norm(new Vector3(side * 1f, 0.05f, 0.45f));
                CreatureMeshParts.Eye(b, c, look, Vector3.up, 0.0115f, 0.85f, CreatureMeshSkin.Fixed(head));
            }
        }
    }
}
