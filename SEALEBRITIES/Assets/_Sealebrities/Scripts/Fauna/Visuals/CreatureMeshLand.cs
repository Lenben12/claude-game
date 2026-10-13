using System;
using UnityEngine;

namespace Sealebrities
{
    /// Land animals of the island (L.11): lizards (marine iguana with a spiky crest, gold dust day gecko with toe pads),
    /// quadrupeds (goat "Captain Bleat" with horns and beard, pig "Truffle" with a snout and a curly tail) and the
    /// capuchin "Clapper". Smooth rounded bodies (characters), big friendly eyes, legs with ball knees.
    /// 1 unit = nose to rump (lizards: nose to tail tip); feet at y = −bodyHeight / 2.
    internal static class CreatureMeshLand
    {
        const float Pi = CreatureMeshMath.Pi;

        // ============================================================================================ lizards

        // palette entries (CreatureMeshPaint.LizardSkin)
        const int LCrest = 0, LClaw = 1, LLeg = 2, LBelly = 3, LPatch = 4, LBump = 5, LMouth = 6, LToe = 7;

        public static void Lizard(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            bool gecko = (s.variant ?? "") == "gecko";
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.SmoothBody(gecko ? 1f : 0.7f, 1f);
            ctx.Gait = CreatureGait.Lizard;
            if (!gecko) ctx.Facet(true, false);
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.08f, 0.25f);
            float footY = -H * 0.5f;
            float g = gecko ? 0.05f : 0.065f;   // body half girth
            float bodyY = footY + (gecko ? 0.06f : 0.07f);

            var r = gecko
                ? new CreatureMeshCurve(0f, 0.15f, 0.03f, 0.62f, 0.07f, 0.95f, 0.11f, 0.85f, 0.15f, 0.62f, 0.22f, 0.9f, 0.32f, 1f, 0.45f, 0.8f, 0.55f, 0.45f, 0.75f, 0.24f, 1f, 0.04f)
                : new CreatureMeshCurve(0f, 0.2f, 0.03f, 0.6f, 0.07f, 0.85f, 0.11f, 0.78f, 0.16f, 0.72f, 0.24f, 0.95f, 0.34f, 1f, 0.46f, 0.85f, 0.56f, 0.5f, 0.75f, 0.25f, 1f, 0.04f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.02f, 0.05f, 0.09f, 0.13f, 0.17f, 0.22f, 0.28f, 0.35f, 0.42f, 0.5f, 0.58f, 0.67f, 0.77f, 0.88f, 1f },
                Sides = 10,
                Center = t => new Vector3(0f, bodyY + (t < 0.12f ? 0.012f * (1f - t / 0.12f) : 0f) - 0.01f * CreatureMeshMath.Smooth(0.5f, 1f, t), 0.5f - t),
                HalfW = t => Math.Max(1e-4f, g * r[t] * (t < 0.12f ? 1.05f : 1.15f)),
                HalfTop = t => Math.Max(1e-4f, g * r[t] * 0.8f),
                HalfBot = t => Math.Max(1e-4f, g * r[t] * 0.62f),
                U0 = 0f, U1 = CreatureMeshPaint.PalBodyU,
                Spacing = 0, CapStart = true, CapEnd = true,
            };
            var chain = ctx.SpineAlong(body, new[] { 0.08f, 0.24f, 0.4f, 0.56f, 0.7f, 0.84f }, -1);
            int head = ctx.Spine[0];
            ctx.Head = head;
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.EyeU = 0.06f;

            // eyes: geckos have huge ones; iguanas small ones
            CreatureMeshParts.EyePair(ctx, body, 0.07f, Pi * 0.5f + 0.55f, gecko ? 0.03f : 0.016f, 0.2f, 0.42f, CreatureMeshSkin.Fixed(head));
            // mouth line
            for (int side = -1; side <= 1; side += 2)
            {
                var p = new Vector3[4];
                for (int k = 0; k < 4; k++)
                {
                    float t = CreatureMeshMath.Lerp(0.008f, 0.1f, k / 3f);
                    Vector3 q = body.Point(t, Pi * 0.5f - 0.25f) - body.Normal(t, Pi * 0.5f - 0.25f) * 0.001f;
                    p[k] = side > 0 ? q : CreatureMeshMath.MirrorX(q);
                }
                var tube = CreatureMeshParts.Tube(p, t => 0.0028f, 4, 3, t => Vector3.up);
                tube.Build(b, CreatureMeshSlot.Mouth, (q, a, t) => CreatureMeshSkin.One(head));
            }
            if (!gecko)
            {
                // marine iguana: crest of spikes from the head to mid-tail, knobby head scales
                for (int k = 0; k < 16; k++)
                {
                    float t = CreatureMeshMath.Lerp(0.07f, 0.7f, k / 15f);
                    Vector3 p = body.Point(t, Pi) - Vector3.up * 0.003f;
                    float h = 0.03f * (1f - 0.6f * CreatureMeshMath.Abs(t - 0.3f)) * (k % 2 == 0 ? 1f : 0.7f);
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, p + new Vector3(0f, h, -h * 0.35f), 0.008f, 3,
                        CreatureMeshPaint.PalUv(LCrest), CreatureMeshPaint.PalUv(LCrest), chain.Weigh(p));
                }
                for (int k = 0; k < 6; k++)
                {
                    float t = 0.02f + 0.015f * k;
                    float th = Pi + (k % 2 == 0 ? 0.5f : -0.5f);
                    Vector3 p = body.Point(t, th);
                    CreatureMeshLimbs.Blob(ctx, p, body.Normal(t, th), Vector3.forward, 0.008f, 0.008f, 0.006f, CreatureMeshSkin.Fixed(head), CreatureMeshPaint.PalUv(LBump), 3, 5);
                }
            }

            // four sprawled legs: hip on the flank → knee out and up → foot on the ground, toes
            for (int side = 1; side >= -1; side -= 2)
            {
                for (int k = 0; k < 2; k++)
                {
                    bool frontLeg = k == 0;
                    float t = frontLeg ? 0.2f : 0.47f;
                    float sd = side;
                    Vector3 hip = body.Point(t, Pi * 0.5f - 0.35f);
                    if (side < 0) hip = CreatureMeshMath.MirrorX(hip);
                    hip -= new Vector3(sd * g * 0.2f, 0f, 0f);
                    Vector3 knee = hip + new Vector3(sd * (gecko ? 0.07f : 0.08f), 0.015f, frontLeg ? 0.02f : -0.02f);
                    Vector3 foot = new Vector3(knee.x + sd * 0.02f, footY + 0.008f, knee.z + (frontLeg ? 0.035f : -0.02f));
                    float rl = gecko ? 0.012f : 0.016f;
                    CreatureMeshLimbs.Leg(ctx, (side > 0 ? "LegR" : "LegL") + k, chain.Bones[frontLeg ? 1 : 2], hip, knee, foot, rl, rl * 0.85f, rl * 0.7f,
                        CreatureMeshPaint.PalUv(LLeg), CreatureMeshPaint.PalUv(LLeg), 6);
                    int kneeB = ctx.LegKnees[ctx.LegKnees.Count - 1];
                    // toes (gecko: round sticky pads)
                    for (int toe = 0; toe < 3; toe++)
                    {
                        float a = (toe - 1) * 0.55f + (frontLeg ? 0.3f : -0.3f) * sd;
                        Vector3 dir = new Vector3(sd * CreatureMeshMath.Sin(0.6f + a * sd), 0f, CreatureMeshMath.Cos(0.6f + a * sd) * (frontLeg ? 1f : -1f));
                        dir = CreatureMeshMath.Norm(dir);
                        Vector3 tip = foot + dir * (gecko ? 0.03f : 0.035f);
                        CreatureMeshLimbs.Segment(ctx, foot, tip, rl * 0.45f, rl * 0.3f, kneeB, CreatureMeshPaint.PalUv(gecko ? LLeg : LClaw), 4, 2);
                        if (gecko) CreatureMeshLimbs.Ball(ctx, tip, 0.008f, kneeB, CreatureMeshPaint.PalUv(LToe), 3, 5);
                    }
                }
            }
            ctx.CastShadows = true;
        }

        // ============================================================================================ quadrupeds

        // palette entries (GoatSkin / PigSkin)
        const int QHorn = 0, QHoof = 1, QLeg = 2, QBeard = 3, QEar = 4, QNose = 5, QPatch = 6, QTail = 7;

        public static void Quadruped(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            bool pig = (s.variant ?? "") == "pig";
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.SmoothBody(1f, 1f);
            ctx.Gait = CreatureGait.Quadruped;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.35f, 1f);
            float footY = -H * 0.5f;
            float legLen = pig ? H * 0.4f : H * 0.47f;
            float bodyY = footY + legLen + (pig ? 0.15f : 0.12f);
            float g = pig ? 0.19f : 0.15f;

            // barrel body from chest (z 0.3) to rump (z −0.4)
            var rr = pig ? new CreatureMeshCurve(0f, 0.55f, 0.08f, 0.88f, 0.25f, 1f, 0.6f, 1f, 0.85f, 0.9f, 1f, 0.45f)
                         : new CreatureMeshCurve(0f, 0.55f, 0.08f, 0.85f, 0.3f, 1f, 0.6f, 0.98f, 0.85f, 0.88f, 1f, 0.45f);
            var body = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.04f, 0.1f, 0.2f, 0.32f, 0.46f, 0.6f, 0.73f, 0.85f, 0.94f, 1f },
                Sides = 12,
                Center = t => new Vector3(0f, bodyY + 0.02f * CreatureMeshMath.Sin(Pi * t), 0.3f - 0.7f * t),
                HalfW = t => Math.Max(1e-4f, g * rr[t] * 0.92f),
                HalfTop = t => Math.Max(1e-4f, g * rr[t]),
                HalfBot = t => Math.Max(1e-4f, g * rr[t] * 1.05f),
                U0 = 0.2f * CreatureMeshPaint.PalBodyU, U1 = CreatureMeshPaint.PalBodyU,
                Spacing = 0, CapStart = true, CapEnd = true, CapBulgeStart = 0.5f, CapBulgeEnd = 0.6f,
            };
            int root = ctx.Bone("Body", -1, new Vector3(0f, bodyY, -0.05f));
            int chest = ctx.Bone("Chest", root, new Vector3(0f, bodyY, 0.2f));
            ctx.BodyBone = root;

            body.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.Two(chest, root, CreatureMeshMath.Smooth(0.15f, 0.5f, t)));

            // ---- neck + head
            Vector3 neckBase = new Vector3(0f, bodyY + g * 0.35f, 0.25f);
            Vector3 headC = pig ? new Vector3(0f, bodyY + 0.03f, 0.42f) : new Vector3(0f, bodyY + 0.22f, 0.41f);
            int headB = ctx.Bone("Head", chest, neckBase);
            ctx.Head = headB;
            CreatureMeshLimbs.Segment(ctx, neckBase - new Vector3(0f, 0.02f, 0.03f), headC + new Vector3(0f, -0.02f, -0.05f), pig ? 0.15f : 0.075f, pig ? 0.13f : 0.07f,
                CreatureMeshLimbs.Along(chest, headB, neckBase, headC, 0.2f, 0.6f), new Vector2(0.24f * CreatureMeshPaint.PalBodyU, 0.75f), 10, 3);
            Vector3 hf = pig ? Vector3.forward : CreatureMeshMath.Norm(new Vector3(0f, -0.55f, 1f));
            var headUv = new Vector2(0.05f * CreatureMeshPaint.PalBodyU, 0.75f);
            CreatureMeshLimbs.Blob(ctx, headC, hf, Vector3.up, pig ? 0.14f : 0.075f, pig ? 0.13f : 0.08f, pig ? 0.13f : 0.11f, CreatureMeshSkin.Fixed(headB), headUv, 6, 12);
            Vector3 muzzle = headC + hf * (pig ? 0.13f : 0.11f);
            if (pig)
            {
                // snout disc with two nostrils
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, muzzle, Vector3.forward, Vector3.up, 0.065f, 0.052f, 0.035f, 4, 10,
                    CreatureMeshPaint.PalUv(QHorn), CreatureMeshPaint.PalUv(QHorn), CreatureMeshSkin.Fixed(headB));
                for (int side = -1; side <= 1; side += 2)
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, muzzle + new Vector3(side * 0.022f, 0f, 0.03f), Vector3.forward, Vector3.up, 0.012f, 0.016f, 0.008f, 3, 6,
                        Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(headB));
            }
            else
            {
                CreatureMeshLimbs.Blob(ctx, muzzle, hf, Vector3.up, 0.048f, 0.05f, 0.05f, CreatureMeshSkin.Fixed(headB), headUv, 4, 8);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, muzzle + hf * 0.045f + Vector3.up * 0.01f, hf, Vector3.up, 0.022f, 0.016f, 0.012f, 3, 6,
                    CreatureMeshPaint.PalUv(QNose), CreatureMeshPaint.PalUv(QNose), CreatureMeshSkin.Fixed(headB));
                // beard
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, muzzle + new Vector3(0f, -0.04f, -0.02f), muzzle + new Vector3(0f, -0.13f, -0.04f), 0.025f, 5,
                    CreatureMeshPaint.PalUv(QBeard), CreatureMeshPaint.PalUv(QBeard), CreatureMeshSkin.One(headB));
                // curved horns
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 h0 = headC + new Vector3(side * 0.035f, 0.07f, -0.01f);
                    var hp = new[] { h0, h0 + new Vector3(side * 0.02f, 0.07f, -0.04f), h0 + new Vector3(side * 0.04f, 0.09f, -0.12f), h0 + new Vector3(side * 0.05f, 0.05f, -0.18f) };
                    CreatureMeshLimbs.Curve(ctx, hp, t => CreatureMeshMath.Lerp(0.022f, 0.004f, t), CreatureMeshSkin.Fixed(headB), CreatureMeshPaint.PalUv(QHorn), 6, 6);
                }
            }
            // mouth smile
            CreatureMeshParts.Mouth(b, muzzle + hf * (pig ? 0.0f : 0.02f) + new Vector3(0f, pig ? -0.055f : -0.035f, 0f), hf, pig ? 0.035f : 0.022f, 0.006f, 0.01f, CreatureMeshSkin.Fixed(headB));
            // eyes
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 ec = headC + new Vector3(side * (pig ? 0.075f : 0.055f), pig ? 0.06f : 0.035f, pig ? 0.08f : 0.02f);
                CreatureMeshParts.Eye(ctx, ec, CreatureMeshMath.Norm(new Vector3(side * 0.75f, 0.15f, 0.65f)), pig ? 0.026f : 0.024f, CreatureMeshSkin.Fixed(headB));
            }
            // ears (floppy forward for the pig, sideways for the goat)
            for (int side = -1; side <= 1; side += 2)
            {
                float sd = side;
                Vector3 e0 = headC + new Vector3(sd * (pig ? 0.08f : 0.06f), pig ? 0.11f : 0.05f, pig ? 0.0f : -0.04f);
                int ear = ctx.Bone(side > 0 ? "EarR" : "EarL", headB, e0);
                ctx.Ears.Add(ear);
                var fin = new CreatureMeshFin
                {
                    Base = q => e0 + new Vector3(0f, 0f, CreatureMeshMath.Lerp(-0.025f, 0.025f, q)),
                    Normal = q => pig ? new Vector3(0f, 1f, -0.3f) : Vector3.up,
                    Cols = 3, Rows = 1, Thickness = 0.014f, Band = 3,
                    Uv = (q, r) => CreatureMeshPaint.PalUv(QEar),
                };
                fin.Edge = q => fin.Base(q) + (pig ? new Vector3(sd * 0.05f, 0.03f, 0.06f) : new Vector3(sd * 0.09f, -0.02f, -0.01f)) * (1f - 0.55f * CreatureMeshMath.Abs(2f * q - 1f));
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Skin, fin, CreatureMeshSkin.Fixed(ear));
            }

            // ---- legs (hips under the barrel) with hooves
            for (int side = 1; side >= -1; side -= 2)
                for (int k = 0; k < 2; k++)
                {
                    bool front = k == 0;
                    float sd = side;
                    Vector3 hip = new Vector3(sd * g * 0.55f, bodyY - g * 0.45f, front ? 0.2f : -0.28f);
                    Vector3 knee = new Vector3(hip.x, footY + legLen * 0.48f, hip.z + (front ? 0.01f : -0.03f));
                    Vector3 foot = new Vector3(hip.x, footY + 0.025f, hip.z + (front ? 0.0f : 0.01f));
                    float rl = pig ? 0.045f : 0.032f;
                    CreatureMeshLimbs.Leg(ctx, (side > 0 ? "LegR" : "LegL") + k, front ? chest : root, hip, knee, foot, rl * 1.25f, rl, rl * 0.85f,
                        CreatureMeshPaint.PalUv(QLeg), CreatureMeshPaint.PalUv(QLeg), 8);
                    int kneeB = ctx.LegKnees[ctx.LegKnees.Count - 1];
                    CreatureMeshLimbs.Blob(ctx, new Vector3(foot.x, footY + 0.022f, foot.z + 0.005f), Vector3.forward, Vector3.up, rl * 1.05f, 0.025f, rl * 1.15f,
                        CreatureMeshSkin.Fixed(kneeB), CreatureMeshPaint.PalUv(QHoof), 3, 8);
                }

            // ---- tail
            Vector3 tb = body.Center(0.97f) + new Vector3(0f, g * 0.55f, 0f);
            int tailB = ctx.Bone("Tail", root, tb);
            ctx.TailChain.Add(tailB);
            if (pig)
            {
                var path = new Vector3[9];
                for (int k = 0; k < 9; k++)
                {
                    float f = k / 8f;
                    float a = f * Pi * 3.2f;
                    path[k] = tb + new Vector3(0.025f * CreatureMeshMath.Sin(a), 0.02f + 0.025f * (1f - CreatureMeshMath.Cos(a)) * 0.5f + f * 0.03f, -0.02f - f * 0.06f);
                }
                CreatureMeshLimbs.Curve(ctx, path, t => CreatureMeshMath.Lerp(0.011f, 0.006f, t), CreatureMeshSkin.Fixed(tailB), CreatureMeshPaint.PalUv(QTail), 5, 10);
            }
            else
            {
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, tb, tb + new Vector3(0f, 0.08f, -0.04f), 0.03f, 6,
                    CreatureMeshPaint.PalUv(QTail), CreatureMeshPaint.PalUv(QTail), CreatureMeshSkin.One(tailB));
            }
            ctx.CastShadows = true;
        }

        // ============================================================================================ capuchin

        // palette entries (CapuchinSkin)
        const int CFace = 0, CHand = 1, CLimb = 2, CCap = 3, CChest = 4, CTail = 5, CEar = 6, CNose = 7;

        public static void Primate(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Mammal;
            ctx.SmoothBody(1f, 1f);
            ctx.Gait = CreatureGait.Primate;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.4f, 0.9f);
            float footY = -H * 0.5f;
            float hipY = footY + 0.21f;

            int root = ctx.Bone("Body", -1, new Vector3(0f, hipY + 0.06f, -0.12f));
            int chest = ctx.Bone("Chest", root, new Vector3(0f, hipY + 0.1f, 0.14f));
            ctx.BodyBone = root;

            // torso: slightly raised front (walks on all fours, chest higher)
            var torso = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.06f, 0.16f, 0.3f, 0.46f, 0.62f, 0.78f, 0.9f, 1f },
                Sides = 12,
                Center = t => new Vector3(0f, hipY + 0.12f - 0.06f * t, 0.24f - 0.5f * t),
                HalfW = t => Math.Max(1e-4f, 0.1f * CreatureMeshMath.Sin(Pi * CreatureMeshMath.Lerp(0.18f, 0.9f, t)) + 0.01f),
                HalfTop = t => Math.Max(1e-4f, 0.105f * CreatureMeshMath.Sin(Pi * CreatureMeshMath.Lerp(0.18f, 0.9f, t)) + 0.01f),
                HalfBot = t => Math.Max(1e-4f, 0.1f * CreatureMeshMath.Sin(Pi * CreatureMeshMath.Lerp(0.18f, 0.9f, t)) + 0.01f),
                U0 = 0.1f * CreatureMeshPaint.PalBodyU, U1 = CreatureMeshPaint.PalBodyU,
                Spacing = 0, CapStart = true, CapEnd = true,
            };
            torso.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.Two(chest, root, CreatureMeshMath.Smooth(0.2f, 0.6f, t)));

            // head: round with a cream face disc, dark cap, ears, big eyes
            Vector3 headC = new Vector3(0f, hipY + 0.24f, 0.33f);
            int headB = ctx.Bone("Head", chest, new Vector3(0f, hipY + 0.17f, 0.27f));
            ctx.Head = headB;
            var headSkin = CreatureMeshSkin.Fixed(headB);
            CreatureMeshLimbs.Blob(ctx, headC, Vector3.forward, Vector3.up, 0.1f, 0.1f, 0.095f, headSkin, CreatureMeshPaint.PalUv(CLimb), 7, 12);
            // cream face fur ring, pink face, dark cap
            CreatureMeshLimbs.Blob(ctx, headC + new Vector3(0f, -0.01f, 0.045f), Vector3.forward, Vector3.up, 0.092f, 0.088f, 0.06f, headSkin, CreatureMeshPaint.PalUv(CChest), 6, 12);
            CreatureMeshLimbs.Blob(ctx, headC + new Vector3(0f, -0.015f, 0.07f), Vector3.forward, Vector3.up, 0.058f, 0.06f, 0.045f, headSkin, CreatureMeshPaint.PalUv(CFace), 5, 10);
            CreatureMeshLimbs.Blob(ctx, headC + new Vector3(0f, 0.07f, 0.005f), Vector3.up, Vector3.forward, 0.072f, 0.07f, 0.035f, headSkin, CreatureMeshPaint.PalUv(CCap), 4, 10);
            CreatureMeshLimbs.Blob(ctx, headC + new Vector3(0f, -0.04f, 0.095f), Vector3.forward, Vector3.up, 0.034f, 0.026f, 0.028f, headSkin, CreatureMeshPaint.PalUv(CFace), 4, 8);
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, headC + new Vector3(0f, -0.025f, 0.118f), Vector3.forward, Vector3.up, 0.01f, 0.006f, 0.005f, 3, 6,
                Vector2.zero, Vector2.zero, headSkin);
            CreatureMeshParts.Mouth(b, headC + new Vector3(0f, -0.058f, 0.112f), Vector3.forward, 0.018f, 0.005f, 0.006f, headSkin);
            for (int side = -1; side <= 1; side += 2)
            {
                CreatureMeshParts.Eye(ctx, headC + new Vector3(side * 0.03f, 0.012f, 0.088f), CreatureMeshMath.Norm(new Vector3(side * 0.25f, 0.05f, 1f)), 0.02f, headSkin);
                Vector3 e0 = headC + new Vector3(side * 0.095f, 0.01f, -0.005f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, e0, new Vector3(side, 0f, 0.2f), Vector3.up, 0.03f, 0.035f, 0.012f, 3, 8,
                    CreatureMeshPaint.PalUv(CEar), CreatureMeshPaint.PalUv(CEar), headSkin);
            }

            // limbs: arms from the chest (front legs), legs from the hips
            for (int side = 1; side >= -1; side -= 2)
                for (int k = 0; k < 2; k++)
                {
                    bool arm = k == 0;
                    float sd = side;
                    Vector3 hip = arm ? new Vector3(sd * 0.085f, hipY + 0.1f, 0.18f) : new Vector3(sd * 0.075f, hipY + 0.02f, -0.18f);
                    Vector3 knee = arm ? new Vector3(sd * 0.105f, footY + 0.12f, 0.15f) : new Vector3(sd * 0.095f, footY + 0.12f, -0.07f);
                    Vector3 foot = arm ? new Vector3(sd * 0.1f, footY + 0.025f, 0.22f) : new Vector3(sd * 0.09f, footY + 0.025f, -0.16f);
                    CreatureMeshLimbs.Leg(ctx, (side > 0 ? "LimbR" : "LimbL") + k, arm ? chest : root, hip, knee, foot, 0.034f, 0.028f, 0.022f,
                        CreatureMeshPaint.PalUv(arm ? CChest : CLimb), CreatureMeshPaint.PalUv(CLimb), 8);
                    int kneeB = ctx.LegKnees[ctx.LegKnees.Count - 1];
                    CreatureMeshLimbs.Blob(ctx, foot + new Vector3(0f, -0.005f, 0.02f), Vector3.forward, Vector3.up, 0.022f, 0.016f, 0.035f,
                        CreatureMeshSkin.Fixed(kneeB), CreatureMeshPaint.PalUv(CHand), 3, 8);
                }

            // long tail curling up at the end (3-bone chain)
            {
                Vector3 t0 = new Vector3(0f, hipY + 0.07f, -0.27f);
                var path = new[] { t0, t0 + new Vector3(0f, 0.04f, -0.2f), t0 + new Vector3(0f, 0.16f, -0.38f), t0 + new Vector3(0f, 0.34f, -0.42f), t0 + new Vector3(0f, 0.44f, -0.33f) };
                var ch = CreatureMeshParts.ChainAlong(ctx, "Tail", root, path, new[] { 0f, 0.33f, 0.66f });
                ctx.TailChain.AddRange(ch.Bones);
                var tube = CreatureMeshParts.Tube(path, t => CreatureMeshMath.Lerp(0.024f, 0.012f, t), 9, 7, null);
                tube.Uv = (t, v) => CreatureMeshPaint.PalUv(CTail);
                tube.Build(b, CreatureMeshSlot.Skin, (p, a, t) => ch.Weigh(a));
            }
            ctx.CastShadows = true;
        }
    }
}
