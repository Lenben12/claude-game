using System;
using UnityEngine;

namespace Sealebrities
{
    /// Starfish (G.1): puffy flat star lying on the seafloor, two tones (top / underside), arms on 2-bone chains that
    /// curl slowly (rig.tentacles). Variants "slim" (blue sea star: 5 long round arms, small googly eyes) and "thorns"
    /// (crown-of-thorns: 13 short arms covered in yellow-tipped spikes). 1 unit = arm-tip diameter; the underside sits
    /// at y = −bodyHeight / 2.
    internal static class CreatureMeshStarfish
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            if ((s.variant ?? "") == "crinoid") { CreatureMeshFossils.Crinoid(ctx); return; }   // round 3: sea lily
            bool thorns = (s.variant ?? "") == "thorns";
            var b = ctx.B;
            ctx.Layout = CreatureMeshLayout.Custom;
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.Gait = CreatureGait.Star;
            ctx.Facet(true, false);
            ctx.Smooth[CreatureMeshSlot.Skin] = thorns ? 0.35f : 0.65f;
            ctx.CastShadows = false;

            int n = thorns ? 13 : 5;
            float R = 0.5f;
            float rc = thorns ? 0.24f : 0.09f;           // disc radius between the arms
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.05f, 0.25f);
            float yBot = -H * 0.5f;
            float hTop = thorns ? H * 1.25f : H * 1.4f;     // puffy top (the arms are round)
            float sharp = thorns ? 2.2f : 3.2f;
            float phase0 = Pi * 0.5f;                     // first arm points forward (+Z)

            float armHalfW = 0.062f;   // slim: arms of nearly constant width with round tips
            Func<float, float> outline = th =>
            {
                if (thorns)
                {
                    float a = CreatureMeshMath.Abs(CreatureMeshMath.Cos(n * (th - phase0) * 0.5f));
                    return rc + (R - rc) * CreatureMeshMath.Pow(a, sharp);
                }
                // angular distance to the nearest arm axis
                float sector = CreatureMeshMath.TwoPi / n;
                float rel = th - phase0;
                float d = rel - sector * (float)Math.Round(rel / sector);
                // stadium-shaped arm: straight sides (half width w) and a round tip (circle at c = R − w)
                float sd = CreatureMeshMath.Abs(CreatureMeshMath.Sin(d)), cd = CreatureMeshMath.Cos(d);
                float w = armHalfW, c = R - w;
                float rr;
                if (sd < 1e-4f) rr = R;
                else
                {
                    float rSide = w / sd;
                    if (rSide * cd <= c) rr = rSide;
                    else
                    {
                        float disc = w * w - c * c * sd * sd;
                        rr = disc > 0f ? c * cd + CreatureMeshMath.Sqrt(disc) : rSide;
                    }
                }
                return Math.Max(rc, Math.Min(R, rr));
            };
            Func<float, float, float> topY = (th, v) =>
            {
                float rr = v;   // 0 centre .. 1 outline
                float dome = CreatureMeshMath.Sqrt(Math.Max(0f, 1f - rr * rr));
                float arm = CreatureMeshMath.Pow(CreatureMeshMath.Abs(CreatureMeshMath.Cos(n * (th - phase0) * 0.5f)), 1.2f);
                float armLift = thorns ? 0.75f + 0.25f * arm : 0.55f + 0.45f * arm;
                float lift = CreatureMeshMath.Lerp(0.85f, armLift, CreatureMeshMath.Smooth(0f, 0.5f, rr));   // one height at the centre
                return yBot + 0.004f + hTop * dome * lift * CreatureMeshMath.Lerp(1f, 0.55f, rr);
            };

            // ---- bones: body + 2-bone chain per arm (tentacles)
            int body = ctx.Bone("Body", -1, new Vector3(0f, yBot + hTop * 0.4f, 0f));
            ctx.BodyBone = body;
            var chains = new CreatureMeshChain[n];
            for (int k = 0; k < n; k++)
            {
                float th = phase0 + k * CreatureMeshMath.TwoPi / n;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(th), 0f, CreatureMeshMath.Sin(th));
                float yA = yBot + hTop * 0.3f;
                var piv = new[] { new Vector3(0f, yA, 0f) + d * (rc * 0.9f), new Vector3(0f, yA * 0.9f, 0f) + d * (rc + (R - rc) * 0.5f) };
                int b0 = ctx.Bone("Arm" + k + "_0", body, piv[0]);
                int b1 = ctx.Bone("Arm" + k + "_1", b0, piv[1]);
                chains[k] = new CreatureMeshChain(new[] { b0, b1 }, piv, d * R + new Vector3(0f, yA, 0f));
                ctx.Tentacles.Add(b0);
            }
            Func<Vector3, BoneWeight> skin = p =>
            {
                float th = (float)Math.Atan2(p.z, p.x);
                float rel = th - phase0;
                float sector = CreatureMeshMath.TwoPi / n;
                int k = (int)Math.Round(rel / sector);
                k = ((k % n) + n) % n;
                float r = CreatureMeshMath.Sqrt(p.x * p.x + p.z * p.z);
                var w = chains[k].Weigh(p);
                return CreatureMeshSkin.Blend(CreatureMeshSkin.One(body), w, CreatureMeshMath.Smooth(rc * 0.7f, rc * 1.5f, r));
            };

            int NU = n * (thorns ? 4 : 12), NV = thorns ? 4 : 6;
            Func<float, float> vMap = vv => CreatureMeshMath.Pow(vv, 0.75f);
            // angular samples cluster around the arm axes (narrow arms keep round tips)
            Func<float, float> uMap = u =>
            {
                float f = u * n;
                int k = Math.Min(n - 1, (int)Math.Floor(f));
                float sl = f - k - 0.5f;
                float sm = CreatureMeshMath.Sign(sl) * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * sl), thorns ? 1f : 1.7f) * 0.5f;
                return (k + 0.5f + sm) / n;
            };
            // sectors start between two arms, so arm k's axis sits at u = (k + 0.5) / n
            float th0 = phase0 - CreatureMeshMath.Pi / n;
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NU, NV,
                (u, v) =>
                {
                    float th = th0 + u * CreatureMeshMath.TwoPi;
                    float r = outline(th) * v;
                    return new Vector3(CreatureMeshMath.Cos(th) * r, topY(th, v), CreatureMeshMath.Sin(th) * r);
                },
                (u, v) => CreatureMeshPaint.StarUv(u, v, true),
                p => Vector3.up + new Vector3(p.x, 0f, p.z) * 0.3f, (p, u, v) => skin(p), uMap, vMap);
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NU, Math.Max(2, NV - 2),
                (u, v) =>
                {
                    float th = th0 + u * CreatureMeshMath.TwoPi;
                    float r = outline(th) * v;
                    return new Vector3(CreatureMeshMath.Cos(th) * r, yBot, CreatureMeshMath.Sin(th) * r);
                },
                (u, v) => CreatureMeshPaint.StarUv(u, v, false),
                p => Vector3.down, (p, u, v) => skin(p), uMap);

            if (thorns)
            {
                // venomous spikes: rows along every arm + a ring on the disc
                var spikeUv = new Vector2(CreatureMeshPaint.StarSpikeU, 0.9f);
                for (int k = 0; k < n; k++)
                {
                    float th = phase0 + k * CreatureMeshMath.TwoPi / n;
                    for (int j = 0; j < 3; j++)
                    {
                        float v = 0.45f + j * 0.2f;
                        float r = outline(th) * v;
                        float jitter = (j - 1) * 0.06f;
                        float th2 = th + jitter;
                        Vector3 p = new Vector3(CreatureMeshMath.Cos(th2) * r, topY(th2, v) - 0.004f, CreatureMeshMath.Sin(th2) * r);
                        Vector3 dir = CreatureMeshMath.Norm(Vector3.up * 1.4f + new Vector3(CreatureMeshMath.Cos(th2), 0f, CreatureMeshMath.Sin(th2)));
                        float len = H * CreatureMeshMath.Lerp(0.9f, 0.55f, j / 2f);
                        CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, p + dir * len, H * 0.16f, 3, spikeUv, spikeUv, skin(p));
                    }
                }
                for (int k = 0; k < 9; k++)
                {
                    float th = k * CreatureMeshMath.TwoPi / 9f + 0.2f;
                    float r = rc * 0.55f;
                    Vector3 p = new Vector3(CreatureMeshMath.Cos(th) * r, topY(th, 0.4f), CreatureMeshMath.Sin(th) * r);
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, p + Vector3.up * (H * 0.75f), H * 0.15f, 3, spikeUv, spikeUv, CreatureMeshSkin.One(body));
                }
            }
            else
            {
                // tiny googly eyes on the disc, looking forward (cartoon starfish)
                for (int side = -1; side <= 1; side += 2)
                {
                    float x = side * rc * 0.42f;
                    Vector3 c = new Vector3(x, topY(Pi * 0.5f, 0.25f) + 0.012f, rc * 0.35f);
                    CreatureMeshParts.Eye(ctx, c, CreatureMeshMath.Norm(new Vector3(side * 0.25f, 0.9f, 0.55f)), 0.021f, CreatureMeshSkin.Fixed(body));
                }
                // small smile under the eyes
                CreatureMeshParts.Mouth(b, new Vector3(0f, topY(Pi * 0.5f, 0.45f) + 0.004f, rc * 0.62f), CreatureMeshMath.Norm(new Vector3(0f, 0.8f, 0.6f)), 0.022f, 0.008f, 0.006f, CreatureMeshSkin.Fixed(body));
            }
        }
    }
}
