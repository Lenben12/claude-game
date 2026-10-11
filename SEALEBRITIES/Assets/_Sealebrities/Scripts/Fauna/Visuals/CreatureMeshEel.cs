using System;
using UnityEngine;

namespace Sealebrities
{
    /// Eels. Default (moray): long hexagonal body with 8 spine bones, skin-covered fin ridge that wraps around the tail
    /// tip, gaping mouth with chunky fangs, tubular nostrils, skin painted per facet (leopard blotches).
    /// Variant "ribbon" (Fog Isle ribbon eel): slimmer, wide open mouth, leaf-shaped flared nostrils, a tall yellow-edged
    /// dorsal ridge. Variant "gulper" (pelican eel): tiny body with a whip tail and a glowing pink tip, a huge hinged
    /// mouth pouch on its own bone (ctx.Jaw) that the gait animator balloons up to 3× when a diver is close.
    internal static class CreatureMeshEel
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            string v = ctx.S.variant ?? "";
            if (v == "gulper") { Gulper(ctx); return; }
            bool ribbon = v == "ribbon";
            var s = ctx.S;
            var b = ctx.B;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.03f, 0.2f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.03f, 0.2f);
            ctx.EyeStyle = ribbon ? CreatureMeshEyeStyle.Friendly : CreatureMeshEyeStyle.Fish;
            ctx.Facet(!ribbon, !ribbon);

            var top = new CreatureMeshCurve(0f, 0.1f, 0.02f, 0.3f, 0.05f, 0.42f, 0.1f, 0.5f, 0.3f, 0.5f, 0.6f, 0.45f, 0.85f, 0.32f, 0.97f, 0.14f, 1f, 0.05f);
            var bot = new CreatureMeshCurve(0f, -0.08f, 0.02f, -0.25f, 0.05f, -0.4f, 0.12f, -0.48f, 0.35f, -0.48f, 0.6f, -0.42f, 0.85f, -0.3f, 0.97f, -0.13f, 1f, -0.05f);
            var wid = new CreatureMeshCurve(0f, 0.45f, 0.05f, 0.85f, 0.12f, 1f, 0.4f, 0.95f, 0.7f, 0.62f, 0.9f, 0.32f, 1f, 0.12f);
            var ts = new float[25];
            for (int i = 0; i < ts.Length; i++)
            {
                float f = i / (float)(ts.Length - 1);
                ts[i] = CreatureMeshMath.Lerp(f, f * f, 0.35f);   // denser rings at the head
            }
            var body = CreatureMeshAnatomy.Body(0.5f, 1f, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, 0.02f, ts, 6);
            var jaw = new CreatureMeshJaw
            {
                HingeT = ribbon ? 0.07f : 0.078f, MouthFrac = 0.12f, GapeDeg = ribbon ? 34f : 24f, Extend = ribbon ? 0.004f : -0.003f, JawWidth = 0.96f,
                Teeth = ribbon ? 3 : 4, ToothLen = ribbon ? 0.005f : 0.008f, ToothRadius = 0.0018f, FangBoost = 0.9f, ToothInward = 0.2f,
            };
            CreatureMeshAnatomy.PrepareUpperJaw(body, jaw);

            var chain = ctx.SpineAlong(body, new[] { 0.05f, 0.17f, 0.3f, 0.43f, 0.56f, 0.69f, 0.81f, 0.91f }, -1);
            int head = ctx.Spine[0];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.EyeU = 0.035f;
            CreatureMeshAnatomy.BuildJaw(ctx, body, jaw, chain.ByAnchor);

            // eyes: small, high on the snout
            CreatureMeshParts.EyePair(ctx, body, 0.036f, Pi * 0.5f + 0.72f, Math.Max(0.007f, (ribbon ? 0.16f : 0.12f) * H), 0.35f, 0.45f, CreatureMeshSkin.Fixed(head));

            if (ribbon)
            {
                // leaf-shaped flared nostrils at the snout tip (yellow)
                for (int side = -1; side <= 1; side += 2)
                {
                    float sd = side;
                    Vector3 p = body.Point(0.012f, Pi * 0.5f + 0.55f);
                    p.x *= sd;
                    var leaf = new CreatureMeshFin
                    {
                        Base = q => p + new Vector3(0f, 0f, CreatureMeshMath.Lerp(-0.003f, 0.003f, q)),
                        Normal = q => new Vector3(0.2f, 0f, 1f),
                        Cols = 2, Rows = 1, Thickness = 0.0015f, Band = 0,
                    };
                    leaf.Edge = q => leaf.Base(q) + new Vector3(sd * 0.012f * CreatureMeshMath.Sin(Pi * q), 0.016f, 0.006f) * (0.5f + 0.5f * CreatureMeshMath.Sin(Pi * q));
                    CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, leaf, CreatureMeshSkin.Fixed(head));
                }
                // tall dorsal ridge (yellow-edged) and a low anal ridge
                float fh = Math.Max(0.01f, H * 0.42f);
                CreatureMeshAnatomy.MedianFin(ctx, body, 0.09f, 0.995f, true,
                    q => fh * CreatureMeshMath.Nose(q, 0.06f) * CreatureMeshMath.Nose(1f - q, 0.03f),
                    q => fh * 0.25f + 0.01f * CreatureMeshMath.Smooth(0.96f, 1f, q), 22, 0, chain.ByVertex, 0.003f);
                CreatureMeshAnatomy.MedianFin(ctx, body, 0.45f, 0.995f, false,
                    q => fh * 0.6f * CreatureMeshMath.Nose(q, 0.08f) * CreatureMeshMath.Nose(1f - q, 0.03f),
                    q => fh * 0.2f, 12, 0, chain.ByVertex, 0.003f);
                return;
            }

            // tubular nostrils at the snout tip
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = body.Point(0.01f, Pi * 0.5f + 0.6f);
                p.x *= side;
                Vector3 tip = p + new Vector3(side * 0.0015f, 0.004f, 0.006f);
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, tip, 0.0022f, 3, new Vector2(0.01f, 0.6f), new Vector2(0.01f, 0.6f), CreatureMeshSkin.One(head));
            }

            // skin-covered fin ridge: dorsal from behind the head to the tail, anal from mid body; both use the skin texture
            float fhm = Math.Max(0.008f, H * 0.2f);
            Func<float, float, Vector2> uvTop = (q, r) => new Vector2(CreatureMeshMath.Lerp(0.1f, 1f, q), 0.5f);
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.1f, 0.995f, true,
                q => fhm * CreatureMeshMath.Nose(q, 0.08f) * CreatureMeshMath.Lerp(0.8f, 1.15f, q) * CreatureMeshMath.Nose(1f - q, 0.02f),
                q => fhm * 0.15f + 0.012f * CreatureMeshMath.Smooth(0.96f, 1f, q),
                20, 0, chain.ByVertex, 0.004f, CreatureMeshSlot.Skin, uvTop, 1);
            Func<float, float, Vector2> uvBot = (q, r) => new Vector2(CreatureMeshMath.Lerp(0.5f, 1f, q), 0.02f);
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.5f, 0.995f, false,
                q => fhm * 0.85f * CreatureMeshMath.Nose(q, 0.1f) * CreatureMeshMath.Nose(1f - q, 0.02f),
                q => fhm * 0.12f + 0.012f * CreatureMeshMath.Smooth(0.96f, 1f, q),
                10, 0, chain.ByVertex, 0.004f, CreatureMeshSlot.Skin, uvBot, 1);
        }

        // ============================================================================================ gulper eel

        static void Gulper(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Angler;
            ctx.Gait = CreatureGait.Jaw;
            ctx.Facet(true, false);
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.04f, 0.15f);
            // body: short head part, then a long whip tail; the head sits behind the mouth (z 0.32)
            var top = new CreatureMeshCurve(0f, 0.6f, 0.06f, 1f, 0.2f, 0.85f, 0.45f, 0.5f, 0.75f, 0.25f, 1f, 0.04f);
            var ts = new float[22];
            for (int i = 0; i < ts.Length; i++) { float f = i / (float)(ts.Length - 1); ts[i] = CreatureMeshMath.Lerp(f, f * f, 0.45f); }
            float z0 = 0.3f, len = 0.8f;
            var body = CreatureMeshAnatomy.Body(z0, len, t => top[t] * H * 0.5f, t => -top[t] * H * 0.45f, t => top[t] * H * 0.42f, 0.03f, ts, 6);
            var chain = ctx.SpineAlong(body, new[] { 0.02f, 0.14f, 0.28f, 0.42f, 0.56f, 0.7f, 0.82f, 0.92f }, -1);
            int head = ctx.Spine[0];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.EyeU = 0.02f;

            // mouth: the pivot (hinge) sits at the head; upper jaw is a thin beak plate, the lower jaw a deep pouch
            Vector3 hinge = new Vector3(0f, 0f, z0 + 0.01f);
            int jaw = ctx.Bone("Jaw", head, hinge);
            ctx.Jaw = jaw;
            var uvHead = new Vector2(0.02f, 0.6f);
            // upper jaw plate
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, hinge + new Vector3(0f, 0.012f, 0.1f), Vector3.forward, Vector3.up, 0.03f, 0.008f, 0.11f, 4, 8,
                uvHead, uvHead, CreatureMeshSkin.Fixed(head));
            // lower pouch: an open bag (outer skin + dark inside)
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, hinge + new Vector3(0f, -0.045f, 0.1f), Vector3.forward, Vector3.up, 0.06f, 0.058f, 0.12f, 5, 9,
                uvHead, uvHead, CreatureMeshSkin.Fixed(jaw));
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, hinge + new Vector3(0f, -0.02f, 0.115f), Vector3.forward, Vector3.up, 0.052f, 0.045f, 0.11f, 4, 8,
                Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(jaw));
            // tiny eyes right at the hinge
            for (int side = -1; side <= 1; side += 2)
                CreatureMeshParts.Eye(ctx, hinge + new Vector3(side * 0.022f, 0.016f, 0.012f), CreatureMeshMath.Norm(new Vector3(side, 0.3f, 0.6f)), 0.006f, CreatureMeshSkin.Fixed(head));

            // glowing pink tail tip
            Vector3 tip = body.Center(0.985f);
            int tipBone = ctx.Spine[ctx.Spine.Count - 1];
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Glow, tip, Vector3.back, Vector3.up, 0.01f, 0.01f, 0.025f, 3, 5, Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(tipBone));
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2.1f;
                Vector3 d = CreatureMeshMath.Norm(new Vector3(CreatureMeshMath.Cos(a), CreatureMeshMath.Sin(a), -0.8f));
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Glow, tip + d * 0.008f, tip + d * 0.03f, 0.004f, 3, Vector2.zero, Vector2.zero, CreatureMeshSkin.One(tipBone));
            }
            ctx.CastShadows = false;
        }
    }
}
