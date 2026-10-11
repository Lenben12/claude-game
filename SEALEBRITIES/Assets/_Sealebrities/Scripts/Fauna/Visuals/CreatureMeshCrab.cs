using System;
using UnityEngine;

namespace Sealebrities
{
    /// Crabs (G.1, L.11): chunky boxy carapace, 2-segment legs with ball knees, claws with a movable finger, googly
    /// eyes on stalks. Variants: "ghost" (pale, tall eye stalks, one big claw), "flat" (Sally Lightfoot: wide and flat),
    /// "hermit" (small crab in a striped spiral shell, hides inside), "coconut" (big purple-orange land crab).
    /// Model: 1 unit ≈ the size of the animal across the carapace incl. claws; feet at y = −bodyHeight / 2 so the
    /// spawner's half-height puts them on the ground. Rig: body bone (+ shell), legs (hip + knee), claws (arm, hand,
    /// finger), eye stalks. Painted from the palette columns of the skin texture (CreatureMeshPaint.PalUv).
    internal static class CreatureMeshCrab
    {
        const float Pi = CreatureMeshMath.Pi;

        // palette entries (CreatureMeshPaint.CrabPalette)
        const int PLeg = 0, PLegTip = 1, PClaw = 2, PClawTip = 3, PShellA = 4, PShellB = 5, PStalk = 6, PBelly = 7;

        sealed class Spec
        {
            public float Cw = 0.3f, Cl = 0.46f, Ch = 0.12f, Cb = 0.04f, Cy, Z0 = 0.22f, Square = 2.6f;
            public int Pairs = 4;
            public float Reach = 0.8f, KneeUp = 0.12f, RLeg = 0.03f, RTip = 0.008f;
            public float ClawR = 1f, ClawL = 1f, StalkLen = 0.05f, EyeR = 0.035f;
            public bool Antennae;
        }

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            string v = s.variant ?? "";
            bool hermit = v == "hermit", ghost = v == "ghost", flat = v == "flat", coconut = v == "coconut";
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.15f, 1f);
            float footY = -H * 0.5f;
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.Gait = CreatureGait.Crab;
            ctx.Smooth[CreatureMeshSlot.Skin] = 0.3f;
            ctx.Facet(true, false);

            var sp = new Spec();
            if (ghost) { sp.Cw = 0.29f; sp.Cl = 0.42f; sp.Ch = 0.13f; sp.Cb = 0.045f; sp.Reach = 0.78f; sp.KneeUp = 0.13f; sp.RLeg = 0.026f; sp.ClawR = 1.25f; sp.ClawL = 0.8f; sp.StalkLen = 0.15f; sp.EyeR = 0.036f; sp.Square = 2.9f; }
            else if (flat) { sp.Cw = 0.34f; sp.Cl = 0.42f; sp.Ch = 0.065f; sp.Cb = 0.03f; sp.Reach = 0.86f; sp.KneeUp = 0.07f; sp.RLeg = 0.025f; sp.ClawR = 0.75f; sp.ClawL = 0.75f; sp.StalkLen = 0.05f; sp.EyeR = 0.034f; sp.Square = 2.3f; }
            else if (hermit) { sp.Cw = 0.17f; sp.Cl = 0.22f; sp.Ch = 0.07f; sp.Cb = 0.04f; sp.Z0 = 0.34f; sp.Cy = footY + 0.17f; sp.Pairs = 3; sp.Reach = 0.42f; sp.KneeUp = 0.1f; sp.RLeg = 0.034f; sp.RTip = 0.01f; sp.ClawR = 0.8f; sp.ClawL = 1.3f; sp.StalkLen = 0.06f; sp.EyeR = 0.04f; }
            else if (coconut) { sp.Cw = 0.27f; sp.Cl = 0.44f; sp.Ch = 0.15f; sp.Cb = 0.05f; sp.Z0 = 0.26f; sp.Reach = 0.74f; sp.KneeUp = 0.15f; sp.RLeg = 0.045f; sp.RTip = 0.012f; sp.ClawR = 1.3f; sp.ClawL = 1.75f; sp.StalkLen = 0.04f; sp.EyeR = 0.03f; sp.Antennae = true; }
            if (!hermit) sp.Cy = footY + sp.KneeUp * 0.55f + 0.06f;

            int root = ctx.Bone("Root", -1, Vector3.zero);
            int body = ctx.Bone("Body", root, new Vector3(0f, sp.Cy, sp.Z0 - sp.Cl * 0.5f));
            ctx.BodyBone = body;
            int shellBone = -1;
            if (hermit)
            {
                shellBone = ctx.Bone("Shell", root, new Vector3(0f, footY + 0.42f, -0.12f));
                ctx.Head = shellBone;   // the shell wobbles a little while walking
                HermitShell(ctx, footY, shellBone);
            }

            // ---- carapace: boxy rounded loft (front at +Z)
            Func<float, float> rnd = t => CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * t - 1f), 4f)), 0.25f);
            Func<float, float> widthT = t => CreatureMeshMath.Lerp(0.9f, 1f, CreatureMeshMath.Gauss(t, 0.35f, 0.35f));
            var cara = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.03f, 0.08f, 0.16f, 0.28f, 0.42f, 0.58f, 0.72f, 0.84f, 0.92f, 0.97f, 1f },
                Sides = 10,
                Center = t => new Vector3(0f, sp.Cy + sp.Ch * 0.15f * CreatureMeshMath.Sin(Pi * t), sp.Z0 - sp.Cl * t),
                HalfW = t => Math.Max(1e-4f, sp.Cw * rnd(t) * widthT(t)),
                HalfTop = t => Math.Max(1e-4f, sp.Ch * rnd(t)),
                HalfBot = t => Math.Max(1e-4f, sp.Cb * rnd(t)),
                Square = t => sp.Square,
                U0 = 0f, U1 = CreatureMeshPaint.PalBodyU,
                Spacing = 0, CapStart = true, CapEnd = true,
            };
            cara.Build(ctx.B, CreatureMeshSlot.Skin, CreatureMeshSkin.Fixed(body));
            ctx.EyeU = 0.08f;

            if (coconut)
            {
                // tucked abdomen behind the carapace (orange, segmented look via two blobs)
                Vector3 ab = new Vector3(0f, sp.Cy - 0.02f, sp.Z0 - sp.Cl - 0.06f);
                CreatureMeshLimbs.Blob(ctx, ab, Vector3.forward, Vector3.up, 0.17f, 0.11f, 0.13f, CreatureMeshSkin.Fixed(body), CreatureMeshPaint.PalUv(PShellB), 5, 8);
                CreatureMeshLimbs.Blob(ctx, ab + new Vector3(0f, -0.05f, -0.07f), Vector3.forward, Vector3.up, 0.12f, 0.07f, 0.08f, CreatureMeshSkin.Fixed(body), CreatureMeshPaint.PalUv(PBelly), 4, 7);
            }

            // ---- mouth plate
            Vector3 front = new Vector3(0f, sp.Cy - sp.Cb * 0.1f, sp.Z0 - 0.004f);
            CreatureMeshParts.Mouth(ctx.B, front + new Vector3(0f, sp.Ch * 0.05f, 0f), Vector3.forward, sp.Cw * 0.22f, Math.Max(0.012f, sp.Ch * 0.18f), 0.012f, CreatureMeshSkin.Fixed(body));

            // ---- eyes on stalks at the front corners
            Vector3 eyeBase = new Vector3(sp.Cw * (hermit ? 0.32f : 0.42f), sp.Cy + sp.Ch * 0.55f, sp.Z0 - sp.Cl * 0.07f);
            CreatureMeshLimbs.StalkEyes(ctx, eyeBase, sp.StalkLen, sp.EyeR, body, CreatureMeshPaint.PalUv(PStalk), true);

            if (sp.Antennae)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a0 = new Vector3(side * sp.Cw * 0.18f, sp.Cy + sp.Ch * 0.2f, sp.Z0);
                    var path = new[] { a0, a0 + new Vector3(side * 0.05f, 0.08f, 0.12f), a0 + new Vector3(side * 0.14f, 0.12f, 0.26f), a0 + new Vector3(side * 0.26f, 0.08f, 0.36f) };
                    CreatureMeshLimbs.Curve(ctx, path, t => CreatureMeshMath.Lerp(0.009f, 0.003f, t), CreatureMeshSkin.Fixed(body), CreatureMeshPaint.PalUv(PLegTip), 4, 5);
                }
            }

            // ---- walking legs: hip on the carapace flank → knee (up and out) → foot on the ground
            for (int side = 1; side >= -1; side -= 2)
            {
                for (int k = 0; k < sp.Pairs; k++)
                {
                    float f = sp.Pairs > 1 ? k / (float)(sp.Pairs - 1) : 0.5f;
                    float t = CreatureMeshMath.Lerp(hermit ? 0.3f : 0.28f, hermit ? 0.85f : 0.86f, f);
                    float ang = CreatureMeshMath.Lerp(38f, -42f, f) * Mathf.Deg2Rad;      // front legs point forward, rear legs back
                    Vector3 dir = new Vector3(side * CreatureMeshMath.Cos(ang), 0f, CreatureMeshMath.Sin(ang));
                    float hx = sp.Cw * rnd(t) * widthT(t) * 0.86f;
                    Vector3 hip = new Vector3(side * hx, sp.Cy - sp.Cb * 0.2f, sp.Z0 - sp.Cl * t);
                    float reach = sp.Reach - hx;
                    float lenMul = CreatureMeshMath.Lerp(0.92f, 1.06f, CreatureMeshMath.Gauss(f, 0.45f, 0.4f));
                    Vector3 knee = hip + dir * (reach * 0.48f * lenMul) + Vector3.up * sp.KneeUp;
                    Vector3 foot = hip + dir * (reach * lenMul);
                    foot.y = footY;
                    float r0 = sp.RLeg * (coconut ? 1f : CreatureMeshMath.Lerp(1f, 0.85f, f));
                    CreatureMeshLimbs.Leg(ctx, (side > 0 ? "LegR" : "LegL") + k, body, hip, knee, foot, r0, r0 * 0.85f, sp.RTip,
                        CreatureMeshPaint.PalUv(PLeg), CreatureMeshPaint.PalUv(f > 0.5f && !coconut ? PLegTip : PLeg), 5);
                }
            }

            // ---- claws
            for (int side = 1; side >= -1; side -= 2)
            {
                float c = side > 0 ? sp.ClawR : sp.ClawL;
                float cc = 0.7f + 0.3f * c;
                Vector3 sh = new Vector3(side * sp.Cw * 0.6f, sp.Cy - sp.Cb * 0.1f, sp.Z0 - sp.Cl * 0.12f);
                Vector3 el = sh + new Vector3(side * 0.11f, 0.03f, 0.08f) * cc;
                Vector3 wr = el + new Vector3(-side * 0.05f, 0.025f, 0.1f) * cc;
                int arm = ctx.Bone(side > 0 ? "ClawR" : "ClawL", body, sh);
                int hand = ctx.Bone(side > 0 ? "ClawHandR" : "ClawHandL", arm, wr);
                float ra = (sp.RLeg * 1.05f) * cc;
                CreatureMeshLimbs.Segment(ctx, sh, el, ra, ra * 0.95f, arm, CreatureMeshPaint.PalUv(PClaw), 6, 2);
                CreatureMeshLimbs.Ball(ctx, el, ra * 1.05f, arm, CreatureMeshPaint.PalUv(PClaw), 4, 6);
                CreatureMeshLimbs.Segment(ctx, el, wr, ra * 0.95f, ra * 1.05f, CreatureMeshLimbs.Along(arm, hand, el, wr, 0.6f, 0.95f), CreatureMeshPaint.PalUv(PClaw), 6, 2);
                Vector3 fwdH = CreatureMeshMath.Norm(new Vector3(-side * 0.25f, 0.1f, 1f));
                Vector3 hc = wr + fwdH * (0.05f * c);
                CreatureMeshLimbs.Blob(ctx, hc, fwdH, Vector3.up, 0.05f * c, 0.062f * c, 0.085f * c, CreatureMeshSkin.Fixed(hand), CreatureMeshPaint.PalUv(PClaw), 5, 8);
                // fixed lower finger
                Vector3 fb = hc + fwdH * (0.06f * c) - Vector3.up * (0.022f * c);
                CreatureMeshParts.Cone(ctx.B, CreatureMeshSlot.Skin, fb, fb + fwdH * (0.095f * c) + new Vector3(-side * 0.012f * c, 0.01f * c, 0f), 0.026f * c, 5,
                    CreatureMeshPaint.PalUv(PClawTip), CreatureMeshPaint.PalUv(PClawTip), CreatureMeshSkin.One(hand));
                // movable upper finger on its own bone
                Vector3 hinge = hc + fwdH * (0.05f * c) + Vector3.up * (0.03f * c);
                int finger = ctx.Bone(side > 0 ? "FingerR" : "FingerL", hand, hinge);
                CreatureMeshParts.Cone(ctx.B, CreatureMeshSlot.Skin, hinge, hinge + fwdH * (0.1f * c) - Vector3.up * (0.018f * c), 0.024f * c, 5,
                    CreatureMeshPaint.PalUv(PClawTip), CreatureMeshPaint.PalUv(PClawTip), CreatureMeshSkin.One(finger));
                ctx.Claws.Add(arm);
                ctx.ClawFingers.Add(finger);
            }
            ctx.CastShadows = true;
        }

        /// Hermit crab shell: a curled conical tube behind the crab, striped in two shell colours, aperture at the front.
        static void HermitShell(CreatureMeshContext ctx, float footY, int bone)
        {
            var path = new[]
            {
                new Vector3(0f, footY + 0.24f, 0.08f),
                new Vector3(0f, footY + 0.2f, -0.16f),
                new Vector3(0f, footY + 0.36f, -0.36f),
                new Vector3(0f, footY + 0.62f, -0.32f),
                new Vector3(0f, footY + 0.74f, -0.12f),
                new Vector3(0f, footY + 0.66f, 0.02f),
                new Vector3(0f, footY + 0.55f, 0.0f),
            };
            var pts = CreatureMeshMath.Resample(path, 25);
            const int Bands = 6;
            for (int k = 0; k < Bands; k++)
            {
                float t0 = k / (float)Bands, t1 = (k + 1) / (float)Bands;
                var seg = new Vector3[4];
                for (int i = 0; i < 4; i++) seg[i] = CreatureMeshMath.PathPoint(pts, CreatureMeshMath.Lerp(t0, t1, i / 3f));
                float r0 = Radius(t0), r1 = Radius(t1);
                var sw = CreatureMeshParts.Tube(seg, t => CreatureMeshMath.Lerp(r0, r1, t), 3, 10, t => Vector3.right);
                sw.CapStart = k == 0;
                sw.CapEnd = k == Bands - 1;
                sw.CapBulgeStart = -0.35f;   // the aperture is dished in
                Vector2 uv = CreatureMeshPaint.PalUv((k & 1) == 0 ? PShellA : PShellB);
                sw.Uv = (t, vv) => uv;
                sw.Build(ctx.B, CreatureMeshSlot.Skin, CreatureMeshSkin.Fixed(bone));
            }
            // dark aperture opening (the crab comes out of it)
            Vector3 ap = path[0] + new Vector3(0f, -0.01f, 0.012f);
            CreatureMeshParts.Ellipsoid(ctx.B, CreatureMeshSlot.Mouth, ap, Vector3.forward, Vector3.up, 0.17f, 0.15f, 0.025f, 3, 10,
                Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(bone));
        }

        static float Radius(float t) => CreatureMeshMath.Lerp(0.235f, 0.03f, CreatureMeshMath.Pow(t, 0.85f));
    }
}
