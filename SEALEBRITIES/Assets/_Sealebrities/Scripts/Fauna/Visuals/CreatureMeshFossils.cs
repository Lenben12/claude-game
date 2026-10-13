using System;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q14): the volcano island's living fossils and its prehistoric monster in the faceted low-poly
    /// style of the other creatures.
    ///  * Nautilus (Squid plan, variant "nautilus"): a striped logarithmic-spiral shell (tube along the spiral, the
    ///    aperture at the front bottom), a hood, small eyes and a bunch of short tentacles on bone chains.
    ///  * Mosasaurus (Shark plan, variant "mosasaur"): long reptile body on a spine chain, a crocodile snout with an
    ///    upper tooth row, a hinged lower jaw on its own bone (ctx.Jaw – the gait animator opens it), four paddle
    ///    flippers and a down-turned (hypocercal) tail fluke.
    ///  * Sea lily / crinoid (Starfish plan, variant "crinoid"): holdfast roots, a segmented stalk, a cup and ten
    ///    feathery arms on bone chains (they sway like tentacles).
    /// Model conventions as in CreatureFactory (1 unit long along +Z, head at +Z; walkers stand at y = −bodyHeight/2).
    internal static class CreatureMeshFossils
    {
        const float Pi = CreatureMeshMath.Pi;

        // ============================================================================================ nautilus
        public static void Nautilus(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Squid;
            ctx.Facet(true, true);
            // spiral in the YZ plane around the coil centre C: phi grows to -90° (bottom) at the aperture, so the
            // tube's growth direction there points forward (+Z)
            Vector3 C = new Vector3(0f, 0.1f, -0.08f);
            const int N = 34;
            const float thetaEnd = 6.6f;
            var path = new Vector3[N];
            var rad = new float[N];
            for (int i = 0; i < N; i++)
            {
                float th = thetaEnd * i / (N - 1);
                float R = 0.03f * (float)Math.Exp(0.33f * th);
                float phi = -0.5f * Pi - (thetaEnd - th);
                path[i] = C + new Vector3(0f, CreatureMeshMath.Sin(phi) * R, CreatureMeshMath.Cos(phi) * R);
                rad[i] = Math.Max(0.004f, R * 0.6f);
            }
            int head = ctx.Bone("Head", -1, new Vector3(0f, path[N - 1].y, path[N - 1].z + 0.08f));
            int shell = ctx.Bone("Shell", head, C);
            ctx.Spine.Add(head);
            ctx.Spine.Add(shell);
            ctx.Bell = -1;
            var tube = CreatureMeshParts.Tube(path, t => rad[Math.Min(N - 1, (int)(t * (N - 1) + 0.5f))], N - 1, 8, t => Vector3.right);
            tube.CapStart = true;
            tube.CapEnd = false;
            tube.Uv = (t, v) => new Vector2(CreatureMeshMath.Clamp01(0.05f + 0.9f * t), v);
            tube.Build(b, CreatureMeshSlot.Skin, CreatureMeshSkin.Fixed(shell));
            // the aperture: dark cavity + the soft body poking out of it
            Vector3 ap = path[N - 1];
            float ar = rad[N - 1];
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, ap + new Vector3(0f, 0f, 0.004f), Vector3.forward, Vector3.up, ar * 0.95f, ar * 0.9f, 0.02f, 3, 8,
                Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(head));
            Vector3 hood = ap + new Vector3(0f, ar * 0.45f, 0.05f);
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Fin, hood, Vector3.forward, Vector3.up, ar * 0.9f, ar * 0.45f, ar * 0.65f, 4, 8,
                new Vector2(0.1f, 0.1f), new Vector2(0.4f, 0.4f), CreatureMeshSkin.Fixed(head));
            Vector3 bodyC = ap + new Vector3(0f, -ar * 0.1f, 0.06f);
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Fin, bodyC, Vector3.forward, Vector3.up, ar * 0.75f, ar * 0.6f, ar * 0.55f, 4, 8,
                new Vector2(0.3f, 0.3f), new Vector2(0.6f, 0.6f), CreatureMeshSkin.Fixed(head));
            // eyes on the sides of the head
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 e = bodyC + new Vector3(side * ar * 0.72f, ar * 0.12f, -ar * 0.1f);
                CreatureMeshParts.Eye(ctx, e, new Vector3(side, 0.1f, 0.3f), Math.Max(0.012f, ar * 0.18f), CreatureMeshSkin.Fixed(head));
            }
            // tentacles: two rings of short curling arms out of the front
            for (int k = 0; k < 12; k++)
            {
                float a = k * CreatureMeshMath.TwoPi / 12f;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(a), CreatureMeshMath.Sin(a) * 0.7f, 0f);
                Vector3 p0 = bodyC + d * (ar * 0.45f) + Vector3.forward * (ar * 0.45f);
                float len = (k % 2 == 0 ? 0.2f : 0.15f);
                var pth = CreatureMeshParts.GrowPath(p0, Vector3.forward + d * 0.45f, len, 8, i => Vector3.forward + Vector3.down * 0.3f, 0.12f,
                    Vector3.Cross(d, Vector3.forward), f => 0.06f * f);
                var chain = CreatureMeshParts.ChainAlong(ctx, "Tent" + k + "_", head, pth, new[] { 0f, 0.4f, 0.75f });
                ctx.Tentacles.Add(chain.Bones[0]);
                var sw = CreatureMeshParts.Tube(pth, t => CreatureMeshMath.Lerp(0.011f, 0.003f, t), 6, 4, t => Vector3.up);
                sw.CapStart = false;
                sw.Uv = (t, v) => CreatureMeshParts.FinUv(2, v, t);
                sw.Build(b, CreatureMeshSlot.Fin, (p, aa, t) => chain.Weigh(aa));
            }
            ctx.CastShadows = true;
        }

        // ============================================================================================ mosasaurus
        public static void Mosasaur(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Shark;
            ctx.Facet(true, true);
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.06f, 0.2f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.06f, 0.2f);
            // long snout → head → neck → deep chest → long tapering tail
            var top = new CreatureMeshCurve(0f, 0.04f, 0.05f, 0.14f, 0.13f, 0.28f, 0.18f, 0.33f, 0.24f, 0.36f, 0.36f, 0.5f, 0.5f, 0.48f, 0.65f, 0.36f, 0.82f, 0.22f, 0.95f, 0.12f, 1f, 0.08f);
            var bot = new CreatureMeshCurve(0f, -0.03f, 0.05f, -0.12f, 0.13f, -0.26f, 0.18f, -0.28f, 0.24f, -0.34f, 0.36f, -0.5f, 0.5f, -0.5f, 0.65f, -0.4f, 0.82f, -0.26f, 0.95f, -0.14f, 1f, -0.1f);
            var wid = new CreatureMeshCurve(0f, 0.2f, 0.05f, 0.42f, 0.13f, 0.66f, 0.2f, 0.62f, 0.3f, 0.8f, 0.42f, 1f, 0.55f, 0.92f, 0.72f, 0.6f, 0.88f, 0.32f, 1f, 0.15f);
            float[] ts = { 0f, 0.02f, 0.05f, 0.09f, 0.13f, 0.17f, 0.21f, 0.26f, 0.32f, 0.4f, 0.48f, 0.56f, 0.64f, 0.72f, 0.8f, 0.87f, 0.93f, 0.97f, 1f };
            var body = CreatureMeshAnatomy.Body(0.5f, 0.86f, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, 0.015f, ts, 8);
            var chain = ctx.SpineAlong(body, new[] { 0.12f, 0.24f, 0.38f, 0.52f, 0.66f, 0.78f, 0.9f }, -1);
            int head = ctx.Spine[0];
            int last = ctx.Spine[ctx.Spine.Count - 1];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.EyeU = 0.17f;
            CreatureMeshParts.EyePair(ctx, body, 0.165f, Pi * 0.5f + 0.42f, Math.Max(0.008f, 0.1f * H), 0.15f, 0.5f, CreatureMeshSkin.Fixed(head));

            // upper tooth row along the snout's underside
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < 7; k++)
                {
                    float t = 0.02f + k * 0.019f;
                    Vector3 p = body.Point(t, Pi * 0.5f - 0.95f);
                    p.x *= side;
                    Vector3 dir = CreatureMeshMath.Norm(new Vector3(-side * 0.15f, -1f, 0.15f));
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Teeth, p, p + dir * (0.012f + 0.004f * (k % 2)), 0.0035f, 3,
                        new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.9f), CreatureMeshSkin.One(head));
                }
            // hinged lower jaw on its own bone (opens with the action channel)
            Vector3 c18 = body.Center(0.17f);
            Vector3 hinge = new Vector3(0f, c18.y - body.HalfBot(0.17f) * 0.55f, c18.z);
            int jaw = ctx.Bone("Jaw", head, hinge);
            ctx.Jaw = jaw;
            ctx.Gait = CreatureGait.Jaw;
            float jawLen = 0.5f - hinge.z - 0.006f;
            var lower = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.1f, 0.35f, 0.65f, 0.9f, 1f }, Sides = 6,
                Center = t => hinge + new Vector3(0f, -0.006f + 0.004f * t, jawLen * t),
                HalfW = t => Math.Max(1e-4f, CreatureMeshMath.Lerp(body.HalfW(0.17f) * 0.85f, 0.008f, CreatureMeshMath.Pow(t, 0.8f))),
                HalfTop = t => Math.Max(1e-4f, CreatureMeshMath.Lerp(0.012f, 0.004f, t)),
                HalfBot = t => Math.Max(1e-4f, CreatureMeshMath.Lerp(0.02f, 0.006f, t)),
                CapStart = true, CapEnd = true,
                Uv = (t, v) => new Vector2(0.05f, 0.1f),
            };
            lower.Build(b, CreatureMeshSlot.Skin, CreatureMeshSkin.Fixed(jaw));
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < 6; k++)
                {
                    float t = 0.2f + k * 0.13f;
                    Vector3 p = lower.Center(t) + new Vector3(side * lower.HalfW(t) * 0.8f, lower.HalfTop(t) * 0.9f, 0f);
                    Vector3 dir = CreatureMeshMath.Norm(new Vector3(-side * 0.1f, 1f, 0.12f));
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Teeth, p, p + dir * 0.011f, 0.003f, 3,
                        new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.9f), CreatureMeshSkin.One(jaw));
                }
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, hinge + new Vector3(0f, 0.004f, jawLen * 0.45f), Vector3.forward, Vector3.up,
                body.HalfW(0.17f) * 0.55f, 0.006f, jawLen * 0.42f, 3, 6, Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(jaw));

            // four paddle flippers (front pair animated as pectorals, rear pair glued to the hip)
            float F = CreatureMeshMath.Clamp(s.finSize, 0.6f, 1.6f);
            {
                float pt = 0.3f;
                Vector3 pa = body.Point(pt, Pi * 0.5f - 0.75f), pb = body.Point(pt + 0.05f, Pi * 0.5f - 0.8f);
                Vector3 mid = (pa + pb) * 0.5f;
                int bone = CreatureMeshContext.DominantBone(chain, mid);
                int pr = ctx.Bone("PectoralR", bone, mid), pl = ctx.Bone("PectoralL", bone, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(pr);
                ctx.Pectorals.Add(pl);
                float len = 0.14f * F;
                CreatureMeshAnatomy.PairedFin(ctx,
                    q => Vector3.Lerp(pa, pb, q),
                    q => Vector3.Lerp(pa, pb, q) + CreatureMeshMath.Norm(new Vector3(0.85f, -0.35f, -0.45f - 0.4f * q)) * (len * (1f - 0.7f * q)),
                    pr, pl, 3, 1, 2, 0.008f);
            }
            {
                float pt = 0.6f;
                Vector3 pa = body.Point(pt, Pi * 0.5f - 0.8f), pb = body.Point(pt + 0.04f, Pi * 0.5f - 0.85f);
                int hip = CreatureMeshContext.DominantBone(chain, (pa + pb) * 0.5f);
                float len = 0.1f * F;
                CreatureMeshAnatomy.PairedFin(ctx,
                    q => Vector3.Lerp(pa, pb, q),
                    q => Vector3.Lerp(pa, pb, q) + CreatureMeshMath.Norm(new Vector3(0.8f, -0.4f, -0.55f - 0.3f * q)) * (len * (1f - 0.7f * q)),
                    hip, hip, 3, 1, 2, 0.007f);
            }
            // low dorsal ridge
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.4f, 0.75f, true, q => H * 0.12f * CreatureMeshMath.Sin(Pi * q), q => H * 0.05f, 4, 0, chain.ByAnchor, 0.005f);

            // hypocercal tail: the spine bends down into the big lower lobe
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("TailFin", last, ped);
            ctx.TailFin = tail;
            float bh = Math.Max(0.01f, body.HalfTop(0.97f) * 1.3f);
            float Lc = 0.16f * F;
            var len2 = new CreatureMeshCurve(0f, 1f, 0.3f, 0.75f, 0.55f, 0.25f, 0.8f, 0.4f, 1f, 0.55f);
            var lift = new CreatureMeshCurve(0f, -0.95f, 0.5f, -0.1f, 1f, 0.35f);
            CreatureMeshAnatomy.CaudalFin(ctx, ped, bh, q => len2[q] * Lc, q => lift[q] * Lc, tail, 5, 0.008f);
            ctx.CastShadows = true;
        }

        // ============================================================================================ sea lily
        public static void Crinoid(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.Layout = CreatureMeshLayout.Custom;
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.Gait = CreatureGait.Star;
            ctx.Facet(true, true);
            ctx.CastShadows = false;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.3f, 0.8f);
            float yBot = -H * 0.5f;
            float yCup = yBot + H * 0.78f;
            int body = ctx.Bone("Body", -1, new Vector3(0f, yBot, 0f));
            ctx.BodyBone = body;
            // holdfast roots
            for (int k = 0; k < 5; k++)
            {
                float a = k * CreatureMeshMath.TwoPi / 5f + 0.3f;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(a), 0f, CreatureMeshMath.Sin(a));
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, new Vector3(0f, yBot + 0.03f, 0f), new Vector3(0f, yBot + 0.005f, 0f) + d * 0.12f, 0.012f, 4,
                    new Vector2(0.2f, 0.5f), new Vector2(0.2f, 0.5f), CreatureMeshSkin.One(body));
            }
            // segmented, slightly bent stalk (stack of short frustums)
            var stalk = new[] { new Vector3(0f, yBot, 0f), new Vector3(0.01f, yBot + H * 0.3f, 0.015f), new Vector3(0.025f, yBot + H * 0.6f, 0.01f), new Vector3(0.02f, yCup, 0f) };
            int stalkBone = ctx.Bone("Stalk", body, stalk[2]);
            var sw = CreatureMeshParts.Tube(stalk, t => 0.018f - 0.004f * t, 10, 6, t => Vector3.forward);
            sw.Uv = (t, v) => new Vector2(0.15f + 0.1f * ((int)(t * 10f) % 2), v);
            sw.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.Blend(CreatureMeshSkin.One(body), CreatureMeshSkin.One(stalkBone), CreatureMeshMath.Smooth(0.3f, 0.8f, t)));
            // the cup
            Vector3 cup = stalk[3] + new Vector3(0f, 0.02f, 0f);
            int cupBone = ctx.Bone("Cup", stalkBone, cup);
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, cup, Vector3.up, Vector3.forward, 0.045f, 0.045f, 0.035f, 4, 8,
                new Vector2(0.5f, 0.3f), new Vector2(0.6f, 0.6f), CreatureMeshSkin.Fixed(cupBone));
            // ten feathery arms curling outward and up
            for (int k = 0; k < 10; k++)
            {
                float a = k * CreatureMeshMath.TwoPi / 10f;
                Vector3 d = new Vector3(CreatureMeshMath.Cos(a), 0f, CreatureMeshMath.Sin(a));
                Vector3 p0 = cup + d * 0.03f + Vector3.up * 0.02f;
                var pth = CreatureMeshParts.GrowPath(p0, d + Vector3.up * 0.9f, 0.36f, 10, i => d * 0.6f + Vector3.up * 0.2f, 0.15f,
                    Vector3.Cross(Vector3.up, d), f => f > 0.6f ? -0.35f * (f - 0.6f) / 0.4f : 0f);
                var chain = CreatureMeshParts.ChainAlong(ctx, "Arm" + k + "_", cupBone, pth, new[] { 0f, 0.5f });
                ctx.Tentacles.Add(chain.Bones[0]);
                var arm = CreatureMeshParts.Tube(pth, t => CreatureMeshMath.Lerp(0.008f, 0.003f, t), 8, 4, t => Vector3.up);
                arm.CapStart = false;
                arm.Uv = (t, v) => CreatureMeshParts.FinUv(1, v, t);
                arm.Build(b, CreatureMeshSlot.Fin, (p, aa, t) => chain.Weigh(aa));
                // feather barbs: little flat fins along both sides of the arm
                for (int j = 1; j < 6; j++)
                {
                    float f = j / 6f;
                    Vector3 pp = CreatureMeshMath.PathPoint(pth, f);
                    Vector3 tg = CreatureMeshMath.Norm(CreatureMeshMath.PathPoint(pth, Math.Min(1f, f + 0.05f)) - pp);
                    Vector3 side = CreatureMeshMath.Norm(Vector3.Cross(tg, Vector3.up));
                    if (side.sqrMagnitude < 0.5f) side = Vector3.right;
                    for (int sd = -1; sd <= 1; sd += 2)
                        CreatureMeshParts.Cone(b, CreatureMeshSlot.Fin, pp, pp + side * (sd * 0.035f * (1f - f * 0.5f)) + tg * 0.012f, 0.004f, 3,
                            CreatureMeshParts.FinUv(1, 0.5f, f), CreatureMeshParts.FinUv(1, 1f, f), chain.Weigh(pp));
                }
            }
        }
    }
}
