using System;
using UnityEngine;

namespace Sealebrities
{
    /// Rays: flat disc (1 m wingspan along X), whip tail along -Z. Variant "manta": pointed wings,
    /// cephalic fins, wide terminal mouth, white belly. Stingrays get front + rear wing bones per side so
    /// the animator's phase lag produces an undulating disc margin.
    internal static class CreatureMeshRay
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            bool manta = (s.variant ?? "") == "manta";
            var b = ctx.B;
            float bw = CreatureMeshMath.Clamp(s.bodyWidth, 0.5f, 2.5f);
            float thick = CreatureMeshMath.Clamp(s.bodyHeight, 0.03f, 0.2f);
            ctx.Layout = CreatureMeshLayout.Disc;
            ctx.EyeStyle = manta ? CreatureMeshEyeStyle.Mammal : CreatureMeshEyeStyle.Friendly;
            ctx.SkinSmoothness = 0.5f;

            float discLen = manta ? 0.46f : CreatureMeshMath.Clamp(0.95f / bw, 0.45f, 1f);
            float zFront = manta ? 0.22f : discLen * 0.5f;
            float zBack = zFront - discLen;
            float zTip = manta ? -0.085f : 0f;

            Func<float, float> zF, zR;
            if (manta)
            {
                zF = x =>
                {
                    float head = zFront - 0.035f * CreatureMeshMath.Smooth(0.06f, 0.11f, x);
                    float w = CreatureMeshMath.Clamp01((x - 0.08f) / 0.42f);
                    return head - (zFront - 0.035f - zTip) * CreatureMeshMath.Pow(w, 1.3f);
                };
                zR = x =>
                {
                    float w = CreatureMeshMath.Clamp01(x / 0.5f);
                    return zBack + (zTip - zBack) * CreatureMeshMath.Pow(w, 2.6f);
                };
            }
            else
            {
                zF = x => zFront * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(Math.Min(1f, 2f * x), 2.3f)), 1f / 2.3f);
                zR = x => zBack * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(Math.Min(1f, 2f * x), 2f)), 0.5f);
            }
            Func<float, float> thickAt = x =>
            {
                float core = manta ? CreatureMeshMath.Gauss(x, 0f, 0.1f) : CreatureMeshMath.Gauss(x, 0f, 0.16f);
                return thick * ((manta ? 0.14f : 0.22f) + (manta ? 0.86f : 0.78f) * core);
            };
            Func<float, float> chord = sc => CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * CreatureMeshMath.Pow(CreatureMeshMath.Clamp01(sc), 0.78f)), 0.62f);

            ctx.DiscFront = zF(0f);
            ctx.DiscLength = zF(0f) - zR(0f);
            ctx.DiscWidth = 1f;
            ctx.DiscLeadZ = zF;
            ctx.DiscTrailZ = zR;

            // ---- bones
            int body = ctx.Bone("Body", -1, new Vector3(0f, 0f, (zF(0f) + zR(0f)) * 0.5f + 0.02f));
            ctx.Spine.Add(body);
            int[] wingBones;
            Func<Vector3, BoneWeight> discSkin;
            if (manta)
            {
                int wr = ctx.Bone("WingR", body, new Vector3(0.085f, 0f, 0f));
                int wtr = ctx.Bone("WingTipR", wr, new Vector3(0.3f, 0f, -0.05f));
                int wl = ctx.Bone("WingL", body, new Vector3(-0.085f, 0f, 0f));
                int wtl = ctx.Bone("WingTipL", wl, new Vector3(-0.3f, 0f, -0.05f));
                wingBones = new[] { wr, wl };
                discSkin = p =>
                {
                    float ax = CreatureMeshMath.Abs(p.x);
                    var wing = p.x >= 0f ? CreatureMeshSkin.Blend(CreatureMeshSkin.One(wr), CreatureMeshSkin.One(wtr), CreatureMeshMath.Smooth(0.24f, 0.42f, ax))
                                         : CreatureMeshSkin.Blend(CreatureMeshSkin.One(wl), CreatureMeshSkin.One(wtl), CreatureMeshMath.Smooth(0.24f, 0.42f, ax));
                    return CreatureMeshSkin.Blend(CreatureMeshSkin.One(body), wing, CreatureMeshMath.Smooth(0.05f, 0.17f, ax));
                };
            }
            else
            {
                float zc = (zF(0f) + zR(0f)) * 0.5f;
                int fr = ctx.Bone("WingFrontR", body, new Vector3(0.15f, 0f, zc + discLen * 0.18f));
                int fl = ctx.Bone("WingFrontL", body, new Vector3(-0.15f, 0f, zc + discLen * 0.18f));
                int rr = ctx.Bone("WingRearR", body, new Vector3(0.15f, 0f, zc - discLen * 0.2f));
                int rl = ctx.Bone("WingRearL", body, new Vector3(-0.15f, 0f, zc - discLen * 0.2f));
                wingBones = new[] { fr, fl, rr, rl };
                discSkin = p =>
                {
                    float ax = CreatureMeshMath.Abs(p.x);
                    float fz = CreatureMeshMath.Smooth(zc + discLen * 0.2f, zc - discLen * 0.22f, p.z);
                    var wing = p.x >= 0f ? CreatureMeshSkin.Blend(CreatureMeshSkin.One(fr), CreatureMeshSkin.One(rr), fz)
                                         : CreatureMeshSkin.Blend(CreatureMeshSkin.One(fl), CreatureMeshSkin.One(rl), fz);
                    return CreatureMeshSkin.Blend(CreatureMeshSkin.One(body), wing, CreatureMeshMath.Smooth(0.08f, 0.3f, ax));
                };
            }
            ctx.Flippers.AddRange(wingBones);

            // ---- disc: top and bottom surfaces over (x, chord fraction)
            const int NX = 40, NS = 18;
            Func<float, float> xMap = u => 0.5f * CreatureMeshMath.Sin(Pi * (u - 0.5f));
            Func<float, float> sMap = v => 0.5f - 0.5f * CreatureMeshMath.Cos(Pi * v);
            Func<float, float, Vector3> topP = (u, v) =>
            {
                float x = xMap(u), ax = CreatureMeshMath.Abs(x);
                float z = CreatureMeshMath.Lerp(zF(ax), zR(ax), v);
                float y = thickAt(ax) * chord(v) * 0.62f;
                if (!manta) y += thick * 0.18f * CreatureMeshMath.Gauss(x, 0f, 0.08f) * CreatureMeshMath.Gauss(v, 0.32f, 0.22f);
                return new Vector3(x, y, z);
            };
            Func<float, float, Vector3> botP = (u, v) =>
            {
                float x = xMap(u), ax = CreatureMeshMath.Abs(x);
                float z = CreatureMeshMath.Lerp(zF(ax), zR(ax), v);
                float y = -thickAt(ax) * chord(v) * 0.38f;
                return new Vector3(x, y, z);
            };
            float dl = ctx.DiscLength, df = ctx.DiscFront;
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NX, NS, (u, v) => topP(u, v),
                (u, v) => { Vector3 p = topP(u, v); return new Vector2(0.5f + p.x, 0.52f + 0.48f * CreatureMeshMath.Clamp01((df - p.z) / dl)); },
                p => Vector3.up, (p, u, v) => discSkin(p));
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Skin, NX, NS, (u, v) => botP(u, v),
                (u, v) => { Vector3 p = botP(u, v); return new Vector2(0.5f + p.x, 0.48f - 0.48f * CreatureMeshMath.Clamp01((df - p.z) / dl)); },
                p => Vector3.down, (p, u, v) => discSkin(p));

            // ---- tail
            float tailLen = manta ? 0.22f : discLen * 0.95f;
            float tr0 = manta ? 0.012f : thick * 0.28f;
            var tailPath = new[]
            {
                new Vector3(0f, 0.004f, zR(0f) + 0.05f),
                new Vector3(0f, 0.006f, zR(0f) - tailLen * 0.3f),
                new Vector3(0f, 0.012f, zR(0f) - tailLen * 0.65f),
                new Vector3(0f, 0.02f, zR(0f) - tailLen),
            };
            var tailChain = CreatureMeshParts.ChainAlong(ctx, "Tail", body, new[] { tailPath[0], tailPath[1], tailPath[2], tailPath[3] }, new[] { 0.12f, 0.42f, 0.7f });
            ctx.Spine.AddRange(tailChain.Bones);
            var tail = CreatureMeshParts.Tube(tailPath, t => tr0 * CreatureMeshMath.Lerp(1f, 0.08f, CreatureMeshMath.Pow(t, 0.7f)), 22, 8, t => Vector3.up);
            tail.HalfTop = t => tr0 * CreatureMeshMath.Lerp(0.8f, 0.08f, CreatureMeshMath.Pow(t, 0.7f));
            tail.HalfBot = tail.HalfTop;
            tail.CapStart = false;
            tail.Uv = (t, vv) => CreatureMeshParts.FinUv(3, vv, t);
            tail.Build(b, CreatureMeshSlot.Fin, (p, a, t) => tailChain.Weigh(a));

            if (!manta)
            {
                // venomous spine on the tail + pelvic lobes
                Vector3 sp0 = CreatureMeshMath.PathPoint(tailPath, 0.38f) + Vector3.up * tr0 * 0.4f;
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Fin, sp0, sp0 + new Vector3(0f, 0.012f, -0.09f), tr0 * 0.25f, 5,
                    CreatureMeshParts.FinUv(3, 0.5f, 0.8f), CreatureMeshParts.FinUv(3, 0.5f, 0.9f), tailChain.Weigh(sp0));
                for (int side = -1; side <= 1; side += 2)
                {
                    float sd = side;
                    var fin = new CreatureMeshFin
                    {
                        Base = q => new Vector3(sd * CreatureMeshMath.Lerp(0.03f, 0.075f, q), -0.004f, zR(CreatureMeshMath.Lerp(0.03f, 0.075f, q)) + 0.03f),
                        Edge = q => new Vector3(sd * CreatureMeshMath.Lerp(0.03f, 0.085f, q), -0.006f, zR(CreatureMeshMath.Lerp(0.03f, 0.075f, q)) - 0.07f * (1f - 0.6f * q)),
                        Normal = q => Vector3.up,
                        Cols = 5, Rows = 3, Thickness = 0.006f, Band = 2,
                    };
                    CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, (p, a, t) => CreatureMeshSkin.One(body));
                }
            }
            else
            {
                // small dorsal fin at the tail base
                float zb = zR(0f) + 0.035f;
                var fin = new CreatureMeshFin
                {
                    Base = q => new Vector3(0f, thickAt(0f) * chord(0.9f) * 0.5f, zb - 0.045f * q),
                    Edge = q => new Vector3(0f, thickAt(0f) * chord(0.9f) * 0.5f + 0.035f * CreatureMeshMath.Sin(Pi * Math.Min(1f, q * 1.3f)) * (1f - q * 0.6f), zb - 0.045f * q - 0.03f * q),
                    Normal = q => Vector3.right, Cols = 6, Rows = 3, Thickness = 0.006f, Band = 0,
                };
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, (p, a, t) => CreatureMeshSkin.One(body));
            }

            // ---- head
            if (manta)
            {
                // wide terminal mouth
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, new Vector3(0f, -0.004f, zF(0f) - 0.006f), Vector3.forward, Vector3.up,
                    0.062f, 0.014f, 0.02f, 7, 16, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), CreatureMeshSkin.Fixed(body));
                // cephalic fins: rolled paddles pointing forward, curling inward
                for (int side = -1; side <= 1; side += 2)
                {
                    float sd = side;
                    var path = new[]
                    {
                        new Vector3(sd * 0.085f, 0.002f, zF(0.085f) - 0.02f),
                        new Vector3(sd * 0.09f, -0.002f, zF(0f) + 0.035f),
                        new Vector3(sd * 0.082f, -0.012f, zF(0f) + 0.075f),
                        new Vector3(sd * 0.066f, -0.026f, zF(0f) + 0.095f),
                    };
                    var cf = CreatureMeshParts.Tube(path, t => CreatureMeshMath.Lerp(0.02f, 0.008f, t), 12, 10, t => Vector3.up);
                    cf.HalfTop = t => CreatureMeshMath.Lerp(0.0065f, 0.004f, t);
                    cf.HalfBot = cf.HalfTop;
                    cf.HalfW = t => CreatureMeshMath.Lerp(0.02f, 0.01f, t) * CreatureMeshMath.Nose(1f - t, 0.25f);
                    cf.CapEnd = false;
                    cf.Uv = (t, vv) => new Vector2(0.5f + sd * 0.085f, 0.53f);
                    cf.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(body));
                }
                // eyes on the sides of the head
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 c = new Vector3(side * 0.104f, 0.003f, zF(0.1f) - 0.03f);
                    CreatureMeshParts.Eye(b, c, CreatureMeshMath.Norm(new Vector3(side, 0.15f, 0.3f)), Vector3.up, 0.012f, 0.8f, CreatureMeshSkin.Fixed(body));
                }
            }
            else
            {
                float ez = zF(0f) - discLen * 0.2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float ex = side * 0.075f;
                    float v = CreatureMeshMath.Clamp01((zF(CreatureMeshMath.Abs(ex)) - ez) / (zF(CreatureMeshMath.Abs(ex)) - zR(CreatureMeshMath.Abs(ex))));
                    float ytop = thickAt(CreatureMeshMath.Abs(ex)) * chord(v) * 0.62f + thick * 0.18f * CreatureMeshMath.Gauss(ex, 0f, 0.08f) * CreatureMeshMath.Gauss(v, 0.32f, 0.22f);
                    Vector3 c = new Vector3(ex, ytop + 0.006f, ez);
                    // eye socket bump in skin color
                    Vector2 uv = new Vector2(0.5f + ex, 0.52f + 0.48f * CreatureMeshMath.Clamp01((df - ez) / dl));
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, c - Vector3.up * 0.006f, Vector3.up, Vector3.forward, 0.026f, 0.034f, 0.016f, 6, 12, uv, uv, CreatureMeshSkin.Fixed(body));
                    CreatureMeshParts.Eye(b, c + Vector3.up * 0.006f, CreatureMeshMath.Norm(new Vector3(side * 0.7f, 0.75f, 0.15f)), Vector3.up, 0.02f, 0.8f, CreatureMeshSkin.Fixed(body));
                    // spiracle behind the eye
                    Vector3 sp = new Vector3(ex * 1.05f, ytop + 0.002f, ez - 0.045f);
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, sp, Vector3.up, Vector3.forward, 0.012f, 0.016f, 0.004f, 5, 10,
                        new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), CreatureMeshSkin.Fixed(body));
                }
            }
            ctx.CastShadows = true;
        }
    }
}
