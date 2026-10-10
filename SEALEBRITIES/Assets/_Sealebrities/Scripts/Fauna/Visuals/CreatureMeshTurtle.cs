using System;
using UnityEngine;

namespace Sealebrities
{
    /// Sea turtle: domed carapace with painted scutes, flat plastron, beaked head on a neck, four flippers.
    /// Skin texture: u < 0.5 head/neck/tail (loft), u >= 0.5 flippers. Shell slot: carapace/plastron texture.
    internal static class CreatureMeshTurtle
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Turtle;
            ctx.SkinSmoothness = 0.5f;
            float shellLen = 0.64f;
            float zc = -0.03f;
            float zFront = zc + shellLen * 0.5f, zBack = zc - shellLen * 0.5f;
            float shellW = CreatureMeshMath.Clamp(s.bodyWidth, 0.5f, 0.95f) * shellLen * 1.05f;
            float hTop = CreatureMeshMath.Clamp(s.bodyHeight, 0.15f, 0.5f) * shellLen * 0.62f;
            float hBot = hTop * 0.32f;
            float halfW = shellW * 0.5f;

            // outline: z of the front and back edge at |x|
            Func<float, float> zF = x =>
            {
                float f = CreatureMeshMath.Clamp01(x / halfW);
                float z = zc + shellLen * 0.5f * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(f, 2.2f)), 0.45f);
                return z - 0.025f * CreatureMeshMath.Gauss(x, 0f, 0.06f);                     // neck notch
            };
            Func<float, float> zR = x =>
            {
                float f = CreatureMeshMath.Clamp01(x / halfW);
                return zc - shellLen * 0.5f * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(f, 1.7f)), 0.62f);
            };

            int bodyBone = ctx.Bone("Body", -1, new Vector3(0f, 0f, zc));
            int neck = ctx.Bone("Head", bodyBone, new Vector3(0f, 0.01f, zFront - 0.06f));
            int tailB = ctx.Bone("Tail", bodyBone, new Vector3(0f, -0.01f, zBack + 0.03f));
            ctx.Spine.Add(neck); ctx.Spine.Add(bodyBone); ctx.Spine.Add(tailB);

            // ---- carapace (dome) and plastron
            const int NX = 30, NS = 26;
            Func<float, float> xMap = u => halfW * CreatureMeshMath.Sin(Pi * (u - 0.5f));
            Func<float, float> sMap = v => 0.5f - 0.5f * CreatureMeshMath.Cos(Pi * v);
            const float rimH = 0.008f;
            Func<float, float, Vector3> cara = (u, v) =>
            {
                float x = xMap(u), ax = CreatureMeshMath.Abs(x);
                float z = CreatureMeshMath.Lerp(zF(ax), zR(ax), sMap(v));
                float nx = x / halfW, nz = (z - zc) / (shellLen * 0.5f);
                float r = Math.Min(1f, CreatureMeshMath.Sqrt(nx * nx + nz * nz));
                float dome = CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(r, 2.1f)), 0.55f);
                float keel = 0.06f * hTop * CreatureMeshMath.Gauss(x, 0f, 0.05f) * dome;
                float y = rimH + hTop * dome + keel - 0.12f * hTop * CreatureMeshMath.Pow(Math.Max(0f, nz), 2f) * dome;
                return new Vector3(x, y, z);
            };
            Func<float, float, Vector3> plas = (u, v) =>
            {
                float x = xMap(u), ax = CreatureMeshMath.Abs(x);
                float z = CreatureMeshMath.Lerp(zF(ax), zR(ax), sMap(v));
                float nx = x / halfW, nz = (z - zc) / (shellLen * 0.5f);
                float r = Math.Min(1f, CreatureMeshMath.Sqrt(nx * nx + nz * nz));
                float y = -rimH - hBot * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(r, 2.6f)), 0.4f);
                return new Vector3(x, y, z);
            };
            Func<Vector3, bool, Vector2> shellUv = (p, top) =>
            {
                float u = 0.5f + p.x / shellW;
                float a = CreatureMeshMath.Clamp01((zF(0f) + 0.02f - p.z) / (shellLen + 0.02f));
                return new Vector2(u, top ? 0.52f + 0.48f * a : 0.48f - 0.48f * a);
            };
            CreatureMeshSkinFn bodySkin = CreatureMeshSkin.Fixed(bodyBone);
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Shell, NX, NS, cara, (u, v) => shellUv(cara(u, v), true), p => Vector3.up, (p, u, v) => CreatureMeshSkin.One(bodyBone));
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Shell, NX, NS, plas, (u, v) => shellUv(plas(u, v), false), p => Vector3.down, (p, u, v) => CreatureMeshSkin.One(bodyBone));
            // rim band joining both along the outline (u = 0 and u = 1 columns collapse at the sides)
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Shell, 64, 1,
                (u, v) =>
                {
                    Vector3 p = OutlinePoint(u, halfW, zF, zR);
                    p.y = v < 0.5f ? -rimH : rimH;
                    return p;
                },
                (u, v) => shellUv(OutlinePoint(u, halfW, zF, zR), true),
                p => new Vector3(p.x, 0f, p.z - zc), (p, u, v) => CreatureMeshSkin.One(bodyBone));

            // ---- head + neck (skin u 0..0.45)
            var headPath = new[]
            {
                new Vector3(0f, 0.0f, zFront - 0.1f),
                new Vector3(0f, 0.014f, zFront + 0.01f),
                new Vector3(0f, 0.026f, zFront + 0.09f),
                new Vector3(0f, 0.022f, 0.5f),
            };
            var hp = new CreatureMeshCurve(0f, 0.8f, 0.35f, 0.76f, 0.55f, 0.97f, 0.72f, 1f, 0.88f, 0.86f, 1f, 0.5f);
            var head = new CreatureMeshSweep
            {
                Rings = 22, Sides = 16,
                Center = t => CreatureMeshMath.PathPoint(headPath, t),
                HalfW = t => 0.066f * hp[t] * CreatureMeshMath.Nose(1f - t, 0.14f),
                HalfTop = t => 0.06f * hp[t] * CreatureMeshMath.Nose(1f - t, 0.14f),
                HalfBot = t => 0.05f * hp[t] * CreatureMeshMath.Nose(1f - t, 0.14f),
                Spacing = 3, SpacingStrength = 0.5f, CapStart = true, CapEnd = false,
                Uv = (t, vv) => new Vector2(0.45f * (1f - t), vv),
            };
            head.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.Blend(CreatureMeshSkin.One(bodyBone), CreatureMeshSkin.One(neck), CreatureMeshMath.Smooth(0.1f, 0.35f, t)));
            ctx.EyeU = 0.45f * 0.22f;
            // beak: darker horny ellipsoid over the snout front
            {
                Vector3 tip = head.Center(0.95f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, tip + new Vector3(0f, -0.008f, -0.008f), Vector3.forward, Vector3.up, 0.034f, 0.025f, 0.034f, 7, 12,
                    new Vector2(0.004f, 0.5f), new Vector2(0.004f, 0.5f), CreatureMeshSkin.Fixed(neck));
            }
            // eyes
            {
                float et = 0.74f;
                Vector3 p = head.Point(et, Pi * 0.5f + 0.38f);
                Vector3 n = head.Normal(et, Pi * 0.5f + 0.38f);
                Vector3 c = p - n * 0.007f;
                Vector3 look = CreatureMeshMath.Norm(n + Vector3.forward * 0.35f);
                // lid bulge
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, c - n * 0.003f, look, Vector3.up, 0.025f, 0.022f, 0.016f, 6, 12,
                    new Vector2(0.1f, 0.6f), new Vector2(0.1f, 0.6f), CreatureMeshSkin.Fixed(neck));
                CreatureMeshParts.Eye(b, c + look * 0.005f, look, Vector3.up, 0.019f, 0.8f, CreatureMeshSkin.Fixed(neck));
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, CreatureMeshMath.MirrorX(c - n * 0.003f), CreatureMeshMath.MirrorX(look), Vector3.up, 0.025f, 0.022f, 0.016f, 6, 12,
                    new Vector2(0.1f, 0.6f), new Vector2(0.1f, 0.6f), CreatureMeshSkin.Fixed(neck));
                CreatureMeshParts.Eye(b, CreatureMeshMath.MirrorX(c + look * 0.005f), CreatureMeshMath.MirrorX(look), Vector3.up, 0.019f, 0.8f, CreatureMeshSkin.Fixed(neck));
            }

            // ---- tail stub
            {
                var path = new[] { new Vector3(0f, -0.01f, zBack + 0.06f), new Vector3(0f, -0.018f, zBack - 0.03f), new Vector3(0f, -0.03f, zBack - 0.08f) };
                var tail = CreatureMeshParts.Tube(path, t => 0.026f * (1f - t) + 0.002f, 10, 8, t => Vector3.up);
                tail.CapStart = false;
                tail.Uv = (t, vv) => new Vector2(0.45f, vv);
                tail.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(tailB));
            }

            // ---- flippers (skin u 0.5..1)
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                // front: long paddle, swept back
                Vector3 root = new Vector3(sd * halfW * 0.62f, -0.012f, zFront - 0.07f);
                var fp = new[]
                {
                    root,
                    root + new Vector3(sd * 0.12f, -0.012f, -0.02f),
                    root + new Vector3(sd * 0.26f, -0.02f, -0.1f),
                    root + new Vector3(sd * 0.36f, -0.028f, -0.24f),
                };
                int fb = ctx.Bone(side > 0 ? "FlipperFrontR" : "FlipperFrontL", bodyBone, root);
                Flipper(b, fp, t => CreatureMeshMath.Lerp(0.03f, 0.075f, CreatureMeshMath.Sin(Pi * Math.Min(1f, t * 1.25f))) * CreatureMeshMath.Nose(1f - t, 0.22f) + 0.004f,
                    t => CreatureMeshMath.Lerp(0.013f, 0.0035f, t), fb, 0.5f, 0.75f);
                // rear: short rounded paddle
                Vector3 rroot = new Vector3(sd * halfW * 0.5f, -0.018f, zBack + 0.1f);
                var rp = new[]
                {
                    rroot,
                    rroot + new Vector3(sd * 0.06f, -0.006f, -0.05f),
                    rroot + new Vector3(sd * 0.11f, -0.01f, -0.12f),
                };
                int rb = ctx.Bone(side > 0 ? "FlipperRearR" : "FlipperRearL", bodyBone, rroot);
                Flipper(b, rp, t => CreatureMeshMath.Lerp(0.025f, 0.055f, CreatureMeshMath.Sin(Pi * Math.Min(1f, t * 1.4f))) * CreatureMeshMath.Nose(1f - t, 0.35f) + 0.004f,
                    t => CreatureMeshMath.Lerp(0.011f, 0.004f, t), rb, 0.78f, 1f);
                ctx.Flippers.Add(fb);
                ctx.Flippers.Add(rb);
            }
            // order: FR, RR, FL, RL -> reorder to FR, FL, RR, RL for readability
            ctx.Flippers.Sort((x, y) => string.CompareOrdinal(ctx.Bones[x].Name, ctx.Bones[y].Name));
        }

        static Vector3 OutlinePoint(float u, float halfW, Func<float, float> zF, Func<float, float> zR)
        {
            // walk around the outline: u 0..0.5 front edge from -x to +x, 0.5..1 back edge from +x to -x
            float x, z;
            if (u < 0.5f)
            {
                float f = u / 0.5f;
                x = halfW * CreatureMeshMath.Sin(Pi * (f - 0.5f));
                z = zF(CreatureMeshMath.Abs(x));
            }
            else
            {
                float f = (u - 0.5f) / 0.5f;
                x = -halfW * CreatureMeshMath.Sin(Pi * (f - 0.5f));
                z = zR(CreatureMeshMath.Abs(x));
            }
            return new Vector3(x, 0f, z);
        }

        static void Flipper(CreatureMeshBuilder b, Vector3[] path, Func<float, float> chord, Func<float, float> thick, int bone, float u0, float u1)
        {
            var sw = CreatureMeshParts.Tube(path, chord, 16, 12, t => Vector3.up);
            sw.HalfW = chord;
            sw.HalfTop = t => thick(t) * 0.55f;
            sw.HalfBot = t => thick(t) * 0.45f;
            sw.CapStart = false;
            sw.CapEnd = true;
            sw.Uv = (t, vv) => new Vector2(CreatureMeshMath.Lerp(u0, u1, t), vv);
            // leading edge thicker: shift section slightly
            sw.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(bone));
        }
    }
}
