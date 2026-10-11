using System;
using UnityEngine;

namespace Sealebrities
{
    /// Island birds and the fruit bat (L.11). Smooth round bodies (characters), big friendly eyes, two-bone wings
    /// (shoulder + wrist) that the gait animator flaps, glides or folds against the body when perched, short legs
    /// (tucked in flight). 1 unit = beak tip to tail tip; finSize = wingspan / length; feet at y = −bodyHeight / 2.
    /// Variants: "gull" (Günther), "frigate" (red throat pouch, forked tail, hooked beak), "tropic" (tail streamers);
    /// Bat "flyingfox" (fox face, big ears, membrane wings with finger rays).
    internal static class CreatureMeshBird
    {
        const float Pi = CreatureMeshMath.Pi;

        // palette entries (CreatureMeshPaint.BirdSkin / BatSkin)
        const int PBeak = 0, PBeakTip = 1, PLegs = 2, PDark = 3, PTail = 4, PAccent = 5, PBack = 6, PWhite = 7;

        public static void Bird(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            string v = s.variant ?? "";
            bool frigate = v == "frigate", tropic = v == "tropic";
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.SmoothBody(1f, 0.6f);
            ctx.Gait = CreatureGait.Bird;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.18f, 0.4f);
            float footY = -H * 0.5f;
            float span = CreatureMeshMath.Clamp(s.finSize, 1.6f, 3.4f);
            float girth = frigate ? 0.085f : (tropic ? 0.095f : 0.11f);

            // body loft: forehead (z 0.36) → tail base (z −0.22); head is part of the loft
            float z0 = frigate ? 0.33f : 0.37f, len = frigate ? 0.56f : 0.6f;
            var r = new CreatureMeshCurve(0f, 0.02f, 0.05f, 0.62f, 0.12f, 0.72f, 0.2f, 0.58f, 0.3f, 0.72f, 0.45f, 1f, 0.62f, 0.95f, 0.8f, 0.6f, 1f, 0.2f);
            var yc = new CreatureMeshCurve(0f, 0.11f, 0.12f, 0.1f, 0.25f, 0.06f, 0.45f, 0f, 0.7f, 0.0f, 1f, 0.02f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.025f, 0.06f, 0.11f, 0.17f, 0.24f, 0.32f, 0.42f, 0.53f, 0.64f, 0.75f, 0.86f, 0.94f, 1f },
                Sides = 10,
                Center = t => new Vector3(0f, yc[t], z0 - len * t),
                HalfW = t => Math.Max(1e-4f, girth * r[t] * (t > 0.3f ? 0.92f : 0.85f)),
                HalfTop = t => Math.Max(1e-4f, girth * r[t] * 0.95f),
                HalfBot = t => Math.Max(1e-4f, girth * r[t] * (t > 0.3f && t < 0.8f ? 1.08f : 0.9f)),
                U0 = 0f, U1 = CreatureMeshPaint.PalBodyU,
                Spacing = 0, CapStart = true, CapEnd = true,
            };
            int root = ctx.Bone("Body", -1, new Vector3(0f, 0f, 0.02f));
            int headB = ctx.Bone("Head", root, new Vector3(0f, yc[0.2f], z0 - len * 0.2f));
            int tailB = ctx.Bone("Tail", root, body.Center(0.92f));
            ctx.BodyBone = root;
            ctx.Head = headB;
            ctx.TailChain.Add(tailB);
            body.Build(b, CreatureMeshSlot.Skin, (p, a, t) => t < 0.22f ? CreatureMeshSkin.Two(headB, root, CreatureMeshMath.Smooth(0.12f, 0.22f, t))
                                                                 : CreatureMeshSkin.Two(root, tailB, CreatureMeshMath.Smooth(0.8f, 1f, t)));
            ctx.EyeU = 0.08f;

            // ---- beak
            Vector3 bb = body.Center(0.03f) + new Vector3(0f, -0.008f, 0.004f);
            float beakLen = frigate ? 0.2f : (tropic ? 0.13f : 0.12f);
            float beakR = frigate ? 0.016f : 0.02f;
            var beak = new[] { bb, bb + new Vector3(0f, -0.006f, beakLen * 0.5f), bb + new Vector3(0f, frigate ? -0.008f : -0.014f, beakLen) };
            CreatureMeshLimbs.Curve(ctx, beak, t => CreatureMeshMath.Lerp(beakR, beakR * 0.22f, CreatureMeshMath.Pow(t, 0.8f)), CreatureMeshSkin.Fixed(headB),
                CreatureMeshPaint.PalUv(PBeak), 6, 4, false);
            Vector3 tip = beak[2];
            if (frigate)
            {
                // hooked tip + inflatable red throat pouch
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, tip + new Vector3(0f, 0.004f, -0.01f), tip + new Vector3(0f, -0.016f, 0.008f), 0.006f, 4,
                    CreatureMeshPaint.PalUv(PBeakTip), CreatureMeshPaint.PalUv(PBeakTip), CreatureMeshSkin.One(headB));
                CreatureMeshLimbs.Blob(ctx, body.Center(0.2f) + new Vector3(0f, -0.065f, 0.03f), Vector3.forward, Vector3.up, 0.05f, 0.055f, 0.07f,
                    CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(PAccent), 5, 8);
            }
            else if (v == "chicken")
            {
                // village chicken (ambient): red comb on the head and a wattle under the beak
                Vector3 hc = body.Center(0.08f);
                for (int k = 0; k < 3; k++)
                    CreatureMeshLimbs.Blob(ctx, hc + new Vector3(0f, girth * 0.62f + 0.012f * (k == 1 ? 1f : 0f), 0.03f - k * 0.025f), Vector3.forward, Vector3.up, 0.008f, 0.02f, 0.014f,
                        CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(PBeakTip), 3, 6);
                CreatureMeshLimbs.Blob(ctx, bb + new Vector3(0f, -0.03f, 0.01f), Vector3.forward, Vector3.up, 0.008f, 0.018f, 0.01f,
                    CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(PBeakTip), 3, 6);
            }
            else
            {
                // gull: red spot on the lower beak; tropicbird: dark tip
                CreatureMeshLimbs.Blob(ctx, tip + new Vector3(0f, -0.004f, -0.018f), Vector3.forward, Vector3.up, 0.007f, 0.006f, 0.01f,
                    CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(PBeakTip), 3, 6);
            }
            // eyes
            CreatureMeshParts.EyePair(ctx, body, 0.075f, Pi * 0.5f + 0.48f, 0.022f, 0.35f, 0.42f, CreatureMeshSkin.Fixed(headB));
            if (tropic)
            {
                // black eye stripe
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = body.Point(0.09f, Pi * 0.5f + 0.3f);
                    if (side < 0) p = CreatureMeshMath.MirrorX(p);
                    CreatureMeshLimbs.Blob(ctx, p, Vector3.back, Vector3.up, 0.004f, 0.012f, 0.05f, CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(PDark), 3, 5);
                }
            }

            // ---- wings: shoulder bone + wrist bone, one flat blade per side (extended rest pose)
            float half = span * 0.5f;
            float chordRoot = frigate ? 0.22f : 0.26f;
            float sweep = frigate ? 0.2f : 0.1f;   // frigate wings are swept back (crooked)
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                Vector3 sh = new Vector3(sd * girth * 0.78f, yc[0.42f] + girth * 0.38f, z0 - len * 0.36f);
                Vector3 wrist = new Vector3(sd * half * 0.46f, sh.y + 0.03f, sh.z - sweep * 0.6f);
                int wing = ctx.Bone(side > 0 ? "WingR" : "WingL", root, sh);
                int wtip = ctx.Bone(side > 0 ? "WingTipR" : "WingTipL", wing, wrist);
                ctx.Wings.Add(wing);
                ctx.WingTips.Add(wtip);
                var fin = new CreatureMeshFin
                {
                    Base = q =>
                    {
                        float x = sh.x + (sd * half - sh.x) * q;
                        float bend = frigate ? CreatureMeshMath.Gauss(q, 0.46f, 0.18f) * 0.05f : 0f;
                        float z = sh.z - sweep * CreatureMeshMath.Pow(q, 1.3f) + bend;
                        float y = sh.y + 0.03f * CreatureMeshMath.Sin(Pi * q * 0.8f) - 0.02f * q * q;
                        return new Vector3(x, y, z);
                    },
                    Normal = q => Vector3.up,
                    Cols = 8, Rows = 2, Thickness = 0.016f, Band = 2,
                    SkinSR = (q, rr, bp) => CreatureMeshSkin.Two(wing, wtip, CreatureMeshMath.Smooth(0.38f, 0.55f, q)),
                };
                fin.Edge = q =>
                {
                    float c = chordRoot * CreatureMeshMath.Lerp(1f, 0.16f, CreatureMeshMath.Pow(q, frigate ? 1.1f : 1.4f));
                    if (!frigate && q > 0.85f) c *= 1f - (q - 0.85f) * 2.5f;
                    return fin.Base(q) + new Vector3(0f, -0.004f, -c);
                };
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, null);
            }

            // ---- tail feathers
            {
                Vector3 tb = body.Center(0.93f) + new Vector3(0f, 0.004f, 0.02f);
                float tl = frigate ? 0.38f : (tropic ? 0.16f : 0.15f);
                float tw = frigate ? 0.05f : 0.07f;
                var fin = new CreatureMeshFin
                {
                    Base = q => tb + Vector3.right * CreatureMeshMath.Lerp(-tw * 0.6f, tw * 0.6f, q),
                    Normal = q => Vector3.up,
                    Cols = 4, Rows = 1, Thickness = 0.01f, Band = 1,
                };
                fin.Edge = q =>
                {
                    float a = 2f * q - 1f;
                    float l = frigate ? tl * (0.45f + 0.55f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(a), 0.7f)) : tl * (1f - 0.15f * a * a);
                    return fin.Base(q) + new Vector3(a * tw * (frigate ? 0.9f : 0.55f), -0.01f, -l);
                };
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, CreatureMeshSkin.Fixed(tailB));
                if (tropic)
                {
                    // two long white streamers
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var p = new[] { tb + new Vector3(k * 0.006f, 0f, -0.02f), tb + new Vector3(k * 0.012f, -0.02f, -0.3f), tb + new Vector3(k * 0.02f, -0.05f, -0.62f) };
                        CreatureMeshLimbs.Curve(ctx, p, t => CreatureMeshMath.Lerp(0.007f, 0.0025f, t), CreatureMeshSkin.Fixed(tailB), CreatureMeshPaint.PalUv(PTail), 4, 6);
                    }
                }
            }

            // ---- legs (tucked in flight) with webbed feet
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                Vector3 hip = new Vector3(sd * girth * 0.4f, yc[0.6f] - girth * 0.75f, z0 - len * 0.55f);
                Vector3 knee = hip + new Vector3(sd * 0.008f, (footY - hip.y) * 0.45f, 0.012f);
                Vector3 foot = new Vector3(hip.x, footY + 0.008f, hip.z + 0.01f);
                CreatureMeshLimbs.Leg(ctx, side > 0 ? "LegR" : "LegL", root, hip, knee, foot, 0.012f, 0.009f, 0.008f,
                    CreatureMeshPaint.PalUv(PLegs), CreatureMeshPaint.PalUv(PLegs), 5);
                int kneeB = ctx.LegKnees[ctx.LegKnees.Count - 1];
                var web = new CreatureMeshFin
                {
                    Base = q => foot + Vector3.right * (sd * CreatureMeshMath.Lerp(-0.006f, 0.006f, q)),
                    Normal = q => Vector3.up,
                    Cols = 2, Rows = 1, Thickness = 0.005f, Band = 3,
                    Uv = (q, rr) => CreatureMeshPaint.PalUv(PLegs),
                };
                web.Edge = q => foot + new Vector3(sd * CreatureMeshMath.Lerp(-0.03f, 0.03f, q), -0.004f, 0.045f * (1f - 0.3f * CreatureMeshMath.Abs(2f * q - 1f)));
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Skin, web, CreatureMeshSkin.Fixed(kneeB));
            }
            ctx.CastShadows = true;
        }

        // ============================================================================================ bat

        public static void Bat(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Mammal;
            ctx.SmoothBody(1f, 0.5f);
            ctx.Gait = CreatureGait.Bat;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.2f, 0.5f);
            float footY = -H * 0.5f;
            float span = CreatureMeshMath.Clamp(s.finSize, 2f, 4f);

            int root = ctx.Bone("Body", -1, Vector3.zero);
            ctx.BodyBone = root;
            // furry body
            CreatureMeshLimbs.Blob(ctx, new Vector3(0f, 0f, -0.05f), Vector3.forward, Vector3.up, 0.12f, 0.12f, 0.26f, CreatureMeshSkin.Fixed(root),
                CreatureMeshPaint.PalUv(6), 6, 10);
            // mane around the neck
            CreatureMeshLimbs.Blob(ctx, new Vector3(0f, 0.02f, 0.14f), Vector3.forward, Vector3.up, 0.13f, 0.13f, 0.09f, CreatureMeshSkin.Fixed(root),
                CreatureMeshPaint.PalUv(4), 5, 10);
            // fox head
            int headB = ctx.Bone("Head", root, new Vector3(0f, 0.04f, 0.24f));
            ctx.Head = headB;
            CreatureMeshLimbs.Blob(ctx, new Vector3(0f, 0.045f, 0.27f), Vector3.forward, Vector3.up, 0.095f, 0.09f, 0.09f, CreatureMeshSkin.Fixed(headB),
                CreatureMeshPaint.PalUv(6), 5, 10);
            var snout = new[] { new Vector3(0f, 0.03f, 0.32f), new Vector3(0f, 0.02f, 0.39f), new Vector3(0f, 0.012f, 0.44f) };
            CreatureMeshLimbs.Curve(ctx, snout, t => CreatureMeshMath.Lerp(0.055f, 0.022f, t), CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(5), 8, 4, false);
            CreatureMeshLimbs.Blob(ctx, new Vector3(0f, 0.016f, 0.445f), Vector3.forward, Vector3.up, 0.018f, 0.014f, 0.012f, CreatureMeshSkin.Fixed(headB),
                CreatureMeshPaint.PalUv(0), 3, 6);
            for (int side = -1; side <= 1; side += 2)
            {
                // ears
                Vector3 e0 = new Vector3(side * 0.05f, 0.11f, 0.25f);
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, e0, e0 + new Vector3(side * 0.03f, 0.08f, -0.01f), 0.03f, 5,
                    CreatureMeshPaint.PalUv(1), CreatureMeshPaint.PalUv(1), CreatureMeshSkin.One(headB));
                // big dark eyes
                Vector3 ec = new Vector3(side * 0.055f, 0.07f, 0.33f);
                CreatureMeshParts.Eye(ctx, ec, CreatureMeshMath.Norm(new Vector3(side * 0.6f, 0.2f, 1f)), 0.024f, CreatureMeshSkin.Fixed(headB));
            }

            // membrane wings: leading edge = arm, trailing edge scalloped between the finger tips
            float half = span * 0.5f;
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                Vector3 sh = new Vector3(sd * 0.08f, 0.06f, 0.08f);
                Vector3 wrist = new Vector3(sd * half * 0.42f, 0.09f, 0.04f);
                int wing = ctx.Bone(side > 0 ? "WingR" : "WingL", root, sh);
                int wtip = ctx.Bone(side > 0 ? "WingTipR" : "WingTipL", wing, wrist);
                ctx.Wings.Add(wing);
                ctx.WingTips.Add(wtip);
                var fin = new CreatureMeshFin
                {
                    Base = q => new Vector3(sh.x + (sd * half - sh.x) * q, sh.y + 0.04f * CreatureMeshMath.Sin(Pi * q), sh.z - 0.12f * q * q),
                    Normal = q => Vector3.up,
                    Cols = 12, Rows = 2, Thickness = 0.01f, Band = 2,
                    SkinSR = (q, rr, bp) => CreatureMeshSkin.Two(wing, wtip, CreatureMeshMath.Smooth(0.34f, 0.5f, q)),
                };
                fin.Edge = q =>
                {
                    float scallop = 1f - 0.35f * CreatureMeshMath.Abs(CreatureMeshMath.Sin(q * Pi * 3f)) * CreatureMeshMath.Smooth(0.2f, 0.4f, q);
                    float c = (0.4f * (1f - 0.85f * CreatureMeshMath.Pow(q, 1.5f)) + 0.02f) * scallop;
                    return fin.Base(q) + new Vector3(0f, -0.01f, -c);
                };
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, null);
                // arm bone visible along the leading edge
                CreatureMeshLimbs.Segment(ctx, sh, wrist, 0.018f, 0.012f, CreatureMeshLimbs.Along(wing, wtip, sh, wrist, 0.8f, 1f), CreatureMeshPaint.PalUv(2), 5, 3);
                // thumb claw at the wrist
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, wrist, wrist + new Vector3(sd * 0.01f, 0.02f, 0.04f), 0.01f, 4,
                    CreatureMeshPaint.PalUv(2), CreatureMeshPaint.PalUv(2), CreatureMeshSkin.One(wtip));
            }
            // small hind legs
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                Vector3 hip = new Vector3(sd * 0.06f, -0.06f, -0.22f);
                Vector3 knee = hip + new Vector3(sd * 0.03f, -0.04f, -0.04f);
                Vector3 foot = new Vector3(hip.x + sd * 0.04f, footY, hip.z - 0.07f);
                CreatureMeshLimbs.Leg(ctx, side > 0 ? "LegR" : "LegL", root, hip, knee, foot, 0.016f, 0.012f, 0.008f,
                    CreatureMeshPaint.PalUv(2), CreatureMeshPaint.PalUv(2), 5);
            }
            ctx.CastShadows = false;
        }
    }
}
