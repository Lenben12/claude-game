using System;
using UnityEngine;

namespace Sealebrities
{
    /// Moray eel: long (about 15-20:1) body with 8 spine bones, skin-covered dorsal/anal fin ridge that wraps
    /// around the tail tip, gaping mouth with fangs, tubular nostrils.
    internal static class CreatureMeshEel
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.03f, 0.2f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.03f, 0.2f);
            ctx.EyeStyle = CreatureMeshEyeStyle.Fish;
            ctx.SkinSmoothness = 0.62f;

            var top = new CreatureMeshCurve(0f, 0.1f, 0.02f, 0.3f, 0.05f, 0.42f, 0.1f, 0.5f, 0.3f, 0.5f, 0.6f, 0.45f, 0.85f, 0.32f, 0.97f, 0.14f, 1f, 0.05f);
            var bot = new CreatureMeshCurve(0f, -0.08f, 0.02f, -0.25f, 0.05f, -0.4f, 0.12f, -0.48f, 0.35f, -0.48f, 0.6f, -0.42f, 0.85f, -0.3f, 0.97f, -0.13f, 1f, -0.05f);
            var wid = new CreatureMeshCurve(0f, 0.45f, 0.05f, 0.85f, 0.12f, 1f, 0.4f, 0.95f, 0.7f, 0.62f, 0.9f, 0.32f, 1f, 0.12f);
            var body = CreatureMeshAnatomy.Body(0.5f, 1f, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, 0.02f, 56, 14);
            body.Spacing = 2; body.SpacingStrength = 0.45f;
            var jaw = new CreatureMeshJaw
            {
                HingeT = 0.078f, MouthFrac = 0.12f, GapeDeg = 24f, Extend = -0.003f, JawWidth = 0.96f,
                Teeth = 7, ToothLen = 0.0065f, ToothRadius = 0.0011f, FangBoost = 0.9f, ToothInward = 0.2f,
            };
            CreatureMeshAnatomy.PrepareUpperJaw(body, jaw);

            var chain = ctx.SpineAlong(body, new[] { 0.05f, 0.17f, 0.3f, 0.43f, 0.56f, 0.69f, 0.81f, 0.91f }, -1);
            int head = ctx.Spine[0];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.SkinFrom(body);
            ctx.GillU = -1f;
            ctx.EyeU = 0.035f;
            CreatureMeshAnatomy.BuildJaw(ctx, body, jaw, chain.ByAnchor);

            // eyes: small, high on the snout
            CreatureMeshParts.EyePair(b, body, 0.036f, Pi * 0.5f + 0.72f, Math.Max(0.0065f, 0.11f * H), 0.35f, 0.4f, 0.8f, CreatureMeshSkin.Fixed(head));

            // tubular nostrils at the snout tip
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = body.Point(0.01f, Pi * 0.5f + 0.6f);
                p.x *= side;
                Vector3 tip = p + new Vector3(side * 0.0015f, 0.004f, 0.006f);
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, tip, 0.0018f, 5, new Vector2(0.01f, 0.6f), new Vector2(0.01f, 0.6f), CreatureMeshSkin.One(head));
            }

            // skin-covered fin ridge: dorsal from behind the head to the tail, anal from mid body; both use the skin texture
            float fh = Math.Max(0.008f, H * 0.2f);
            Func<float, float, Vector2> uvTop = (q, r) => new Vector2(CreatureMeshMath.Lerp(0.1f, 1f, q), 0.5f);
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.1f, 0.995f, true,
                q => fh * CreatureMeshMath.Nose(q, 0.08f) * CreatureMeshMath.Lerp(0.8f, 1.15f, q) * CreatureMeshMath.Nose(1f - q, 0.02f),
                q => fh * 0.15f + 0.012f * CreatureMeshMath.Smooth(0.96f, 1f, q),
                64, 0, chain.ByVertex, 0.004f, CreatureMeshSlot.Skin, uvTop, 3);
            Func<float, float, Vector2> uvBot = (q, r) => new Vector2(CreatureMeshMath.Lerp(0.5f, 1f, q), 0.02f);
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.5f, 0.995f, false,
                q => fh * 0.85f * CreatureMeshMath.Nose(q, 0.1f) * CreatureMeshMath.Nose(1f - q, 0.02f),
                q => fh * 0.12f + 0.012f * CreatureMeshMath.Smooth(0.96f, 1f, q),
                36, 0, chain.ByVertex, 0.004f, CreatureMeshSlot.Skin, uvBot, 3);
        }
    }
}
