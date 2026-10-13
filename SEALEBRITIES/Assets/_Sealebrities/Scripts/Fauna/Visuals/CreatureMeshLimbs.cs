using System;
using UnityEngine;

namespace Sealebrities
{
    /// Limb helpers for the round-2 body plans (crabs, birds, land animals): tapered tube segments, ball joints and
    /// rigid / blended skinning. Colours come from the palette columns of the skin texture (CreatureMeshPaint.PalUv).
    internal static class CreatureMeshLimbs
    {
        const float Pi = CreatureMeshMath.Pi;

        /// Tapered tube from a to b (radius ra → rb), both ends capped, one flat palette colour.
        public static void Segment(CreatureMeshContext ctx, Vector3 a, Vector3 b, float ra, float rb, int bone, Vector2 uv, int sides = 6, int rings = 3)
            => Segment(ctx, a, b, ra, rb, (p, an, t) => CreatureMeshSkin.One(bone), uv, sides, rings);

        public static void Segment(CreatureMeshContext ctx, Vector3 a, Vector3 b, float ra, float rb, CreatureMeshSkinFn skin, Vector2 uv, int sides = 6, int rings = 3)
        {
            if ((b - a).sqrMagnitude < 1e-10f) return;
            Vector3 dir = CreatureMeshMath.Norm(b - a);
            Vector3 upHint = Math.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.forward;
            var sw = CreatureMeshParts.Tube(new[] { a, Vector3.Lerp(a, b, 0.5f), b }, t => CreatureMeshMath.Lerp(ra, rb, t), rings, sides, t => upHint);
            sw.CapBulgeStart = 0.35f;
            sw.CapBulgeEnd = 0.45f;
            sw.Uv = (t, v) => uv;
            sw.Build(ctx.B, CreatureMeshSlot.Skin, skin);
        }

        /// Curved tapered tube through a path (radius r(t)), palette colour.
        public static void Curve(CreatureMeshContext ctx, Vector3[] path, Func<float, float> r, CreatureMeshSkinFn skin, Vector2 uv, int sides = 6, int rings = 6, bool capStart = true)
        {
            var sw = CreatureMeshParts.Tube(path, r, rings, sides, null);
            sw.CapStart = capStart;
            sw.CapBulgeEnd = 0.4f;
            sw.Uv = (t, v) => uv;
            sw.Build(ctx.B, CreatureMeshSlot.Skin, skin);
        }

        /// Small sphere covering a joint (bends never show gaps).
        public static void Ball(CreatureMeshContext ctx, Vector3 c, float r, int bone, Vector2 uv, int rings = 4, int sides = 6)
        {
            CreatureMeshParts.Ellipsoid(ctx.B, CreatureMeshSlot.Skin, c, Vector3.forward, Vector3.up, r, r, r, rings, sides, uv, uv, CreatureMeshSkin.Fixed(bone));
        }

        public static void Blob(CreatureMeshContext ctx, Vector3 c, Vector3 fwd, Vector3 up, float rx, float ry, float rz, CreatureMeshSkinFn skin, Vector2 uv, int rings = 5, int sides = 8, int slot = CreatureMeshSlot.Skin)
        {
            CreatureMeshParts.Ellipsoid(ctx.B, slot, c, fwd, up, rx, ry, rz, rings, sides, uv, uv, skin);
        }

        /// Two-segment leg (hip → knee → foot) with a ball knee; registers the hip/knee bones as a leg.
        public static void Leg(CreatureMeshContext ctx, string name, int parent, Vector3 hip, Vector3 knee, Vector3 foot,
                               float rHip, float rKnee, float rFoot, Vector2 uvUpper, Vector2 uvLower, int sides = 6, bool register = true)
        {
            int h = ctx.Bone(name, parent, hip);
            int k = ctx.Bone(name + "Knee", h, knee);
            Segment(ctx, hip, knee, rHip, rKnee, h, uvUpper, sides, 3);
            Ball(ctx, knee, rKnee * 1.08f, k, uvUpper, 4, sides);
            Segment(ctx, knee, foot, rKnee, rFoot, k, uvLower, sides, 3);
            if (register)
            {
                ctx.Legs.Add(h);
                ctx.LegKnees.Add(k);
            }
        }

        /// Skin weights that blend from bone a (t = 0) to bone b (t = 1) along the segment a→b.
        public static CreatureMeshSkinFn Along(int boneA, int boneB, Vector3 a, Vector3 b, float blend0 = 0.35f, float blend1 = 0.65f)
        {
            Vector3 ab = b - a;
            float l2 = Math.Max(1e-8f, ab.sqrMagnitude);
            return (p, an, t) =>
            {
                float f = CreatureMeshMath.Clamp01(Vector3.Dot(p - a, ab) / l2);
                return CreatureMeshSkin.Two(boneA, boneB, CreatureMeshMath.Smooth(blend0, blend1, f));
            };
        }

        /// Pair of googly eyes on stalks (crabs) or flush on a head (stalkLen 0).
        public static void StalkEyes(CreatureMeshContext ctx, Vector3 baseR, float stalkLen, float eyeR, int parent, Vector2 stalkUv, bool registerStalks)
        {
            for (int side = 1; side >= -1; side -= 2)
            {
                Vector3 b0 = side > 0 ? baseR : CreatureMeshMath.MirrorX(baseR);
                Vector3 top = b0 + new Vector3(side * stalkLen * 0.18f, stalkLen, stalkLen * 0.08f);
                int bone = parent;
                if (registerStalks)
                {
                    bone = ctx.Bone(side > 0 ? "EyeStalkR" : "EyeStalkL", parent, b0);
                    ctx.EyeStalks.Add(bone);
                }
                if (stalkLen > 0.001f) Segment(ctx, b0, top, eyeR * 0.42f, eyeR * 0.32f, bone, stalkUv, 5, 2);
                Vector3 look = CreatureMeshMath.Norm(new Vector3(side * 0.55f, 0.25f, 1f));
                CreatureMeshParts.Eye(ctx, top + Vector3.up * (eyeR * 0.6f), look, eyeR, CreatureMeshSkin.Fixed(bone));
            }
        }
    }
}
