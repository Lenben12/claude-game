using System;
using UnityEngine;

namespace Sealebrities
{
    /// Bony fish (BodyPlan.Fish) with variants: default, tall, lionfish, barracuda, grouper, garibaldi.
    internal static class CreatureMeshFish
    {
        const float Pi = CreatureMeshMath.Pi;

        enum Tail { Rounded, Emarginate, Forked, Lunate, Truncate }

        sealed class Spec
        {
            public float Lb = 0.8f, Nose = 0.06f;
            public CreatureMeshCurve Top, Bot, Wid;
            public float EyeT = 0.115f, EyeTheta = 0.32f, EyeR = 0.1f;   // EyeR relative to H
            public CreatureMeshJaw Jaw = new CreatureMeshJaw { HingeT = 0.065f, MouthFrac = 0.2f, GapeDeg = 14f };
            public float D0 = 0.24f, D1 = 0.8f, DH = 0.3f; public int DSpines = 10; public float DSpineEnd = 0.5f, DMembrane = 0.75f;
            public float DRake = 0.45f, DTrail = 0f;
            public float A0 = 0.6f, A1 = 0.82f, AH = 0.26f, ARake = 0.6f, ATrail = 0f;
            public bool SecondDorsal; public float D2a, D2b, D2H;
            public float PecT = 0.25f, PecLen = 0.16f, PecUp = 0.05f; public bool PecFan; public int PecRays;
            public float PelT = 0.31f, PelLen = 0.11f;
            public Tail Caudal = Tail.Emarginate; public float CLen = 0.2f, CSpread = 0.42f;
            public float GillT = 0.2f;
            public bool Brows;   // lionfish supraocular tentacles
        }

        static Spec SpecFor(SpeciesDef s)
        {
            var p = new Spec();
            string v = s.variant ?? "";
            switch (v)
            {
                case "tall":
                {
                    bool tang = CreatureMeshMath.Luma(s.colorB) < CreatureMeshMath.Luma(s.colorA);
                    p.Top = new CreatureMeshCurve(0f, 0.03f, 0.05f, 0.15f, 0.12f, 0.33f, 0.22f, 0.46f, 0.36f, 0.5f, 0.5f, 0.46f, 0.66f, 0.32f, 0.8f, 0.16f, 0.9f, 0.085f, 1f, 0.085f);
                    p.Bot = new CreatureMeshCurve(0f, -0.05f, 0.06f, -0.17f, 0.14f, -0.33f, 0.28f, -0.46f, 0.42f, -0.48f, 0.56f, -0.4f, 0.7f, -0.25f, 0.82f, -0.12f, 0.9f, -0.075f, 1f, -0.075f);
                    p.Wid = new CreatureMeshCurve(0f, 0.3f, 0.1f, 0.8f, 0.25f, 1f, 0.5f, 0.85f, 0.75f, 0.45f, 1f, 0.25f);
                    p.Nose = 0.04f;
                    p.EyeT = 0.13f; p.EyeTheta = 0.55f; p.EyeR = 0.085f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.05f, MouthFrac = 0.1f, GapeDeg = 12f };
                    p.D0 = 0.2f; p.D1 = 0.88f; p.DH = tang ? 0.13f : 0.12f; p.DSpines = 0; p.DRake = 0.35f; p.DTrail = tang ? 0f : 0.07f;
                    p.A0 = 0.42f; p.A1 = 0.88f; p.AH = tang ? 0.12f : 0.11f; p.ARake = 0.35f; p.ATrail = tang ? 0f : 0.06f;
                    p.Caudal = tang ? Tail.Lunate : Tail.Truncate; p.CLen = 0.2f; p.CSpread = 0.34f;
                    p.PecT = 0.27f; p.PecLen = 0.14f; p.PelT = 0.33f; p.PelLen = tang ? 0.09f : 0.15f;
                    p.GillT = 0.22f;
                    break;
                }
                case "lionfish":
                    p.Lb = 0.78f;
                    p.Top = new CreatureMeshCurve(0f, 0.06f, 0.08f, 0.28f, 0.2f, 0.44f, 0.35f, 0.5f, 0.52f, 0.44f, 0.7f, 0.28f, 0.85f, 0.14f, 1f, 0.12f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.08f, -0.24f, 0.2f, -0.4f, 0.38f, -0.44f, 0.56f, -0.36f, 0.72f, -0.22f, 0.86f, -0.12f, 1f, -0.11f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.1f, 0.85f, 0.3f, 1f, 0.55f, 0.75f, 0.8f, 0.35f, 1f, 0.25f);
                    p.Nose = 0.06f;
                    p.EyeT = 0.12f; p.EyeTheta = 0.45f; p.EyeR = 0.09f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.08f, MouthFrac = 0.15f, GapeDeg = 12f, Extend = 0.006f };
                    p.D0 = 0.17f; p.D1 = 0.6f; p.DH = 0.44f; p.DSpines = 12; p.DSpineEnd = 1f; p.DMembrane = 0.28f; p.DRake = 0.3f;
                    p.SecondDorsal = true; p.D2a = 0.6f; p.D2b = 0.84f; p.D2H = 0.2f;
                    p.A0 = 0.62f; p.A1 = 0.84f; p.AH = 0.24f; p.ARake = 0.5f;
                    p.PecT = 0.27f; p.PecLen = 0.2f; p.PecFan = true; p.PecRays = 13;
                    p.PelT = 0.33f; p.PelLen = 0.16f;
                    p.Caudal = Tail.Rounded; p.CLen = 0.22f; p.CSpread = 0.45f;
                    p.Brows = true; p.GillT = 0.22f;
                    break;
                case "barracuda":
                    p.Lb = 0.84f;
                    p.Top = new CreatureMeshCurve(0f, 0.03f, 0.06f, 0.15f, 0.14f, 0.3f, 0.24f, 0.42f, 0.4f, 0.5f, 0.6f, 0.45f, 0.8f, 0.28f, 0.93f, 0.18f, 1f, 0.18f);
                    p.Bot = new CreatureMeshCurve(0f, -0.02f, 0.06f, -0.12f, 0.14f, -0.28f, 0.26f, -0.42f, 0.44f, -0.48f, 0.62f, -0.42f, 0.8f, -0.26f, 0.93f, -0.17f, 1f, -0.17f);
                    p.Wid = new CreatureMeshCurve(0f, 0.2f, 0.08f, 0.55f, 0.2f, 0.85f, 0.4f, 1f, 0.65f, 0.8f, 0.85f, 0.45f, 1f, 0.35f);
                    p.Nose = 0.015f;
                    p.EyeT = 0.12f; p.EyeTheta = 0.3f; p.EyeR = 0.13f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.13f, MouthFrac = 0.1f, GapeDeg = 7f, Extend = 0.022f, JawWidth = 0.9f, Teeth = 7, ToothLen = 0.011f, ToothRadius = 0.0018f, FangBoost = 0.8f, ToothInward = 0.15f };
                    p.D0 = 0.38f; p.D1 = 0.48f; p.DH = 0.5f; p.DSpines = 5; p.DSpineEnd = 1f; p.DMembrane = 0.75f; p.DRake = 0.7f;
                    p.SecondDorsal = true; p.D2a = 0.69f; p.D2b = 0.77f; p.D2H = 0.42f;
                    p.A0 = 0.69f; p.A1 = 0.78f; p.AH = 0.38f; p.ARake = 0.7f;
                    p.PecT = 0.22f; p.PecLen = 0.07f; p.PelT = 0.42f; p.PelLen = 0.05f;
                    p.Caudal = Tail.Forked; p.CLen = 0.17f; p.CSpread = 0.62f;
                    p.GillT = 0.2f;
                    break;
                case "grouper":
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.2f, 0.15f, 0.36f, 0.3f, 0.48f, 0.45f, 0.5f, 0.62f, 0.43f, 0.78f, 0.28f, 0.9f, 0.18f, 1f, 0.18f);
                    p.Bot = new CreatureMeshCurve(0f, -0.08f, 0.06f, -0.26f, 0.16f, -0.42f, 0.32f, -0.48f, 0.5f, -0.46f, 0.66f, -0.36f, 0.8f, -0.22f, 0.9f, -0.16f, 1f, -0.16f);
                    p.Wid = new CreatureMeshCurve(0f, 0.4f, 0.08f, 0.8f, 0.2f, 1f, 0.45f, 0.95f, 0.7f, 0.6f, 0.9f, 0.35f, 1f, 0.3f);
                    p.Nose = 0.05f;
                    p.EyeT = 0.1f; p.EyeTheta = 0.55f; p.EyeR = 0.075f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.15f, MouthFrac = 0.18f, GapeDeg = 13f, Extend = 0.02f, JawWidth = 1.04f, JawDepth = 1.05f, Teeth = 6, ToothLen = 0.006f, ToothRadius = 0.0018f, FangBoost = 0.3f };
                    p.D0 = 0.25f; p.D1 = 0.8f; p.DH = 0.22f; p.DSpines = 11; p.DSpineEnd = 0.55f; p.DMembrane = 0.72f; p.DRake = 0.35f;
                    p.A0 = 0.6f; p.A1 = 0.8f; p.AH = 0.24f; p.ARake = 0.45f;
                    p.PecT = 0.26f; p.PecLen = 0.17f; p.PelT = 0.3f; p.PelLen = 0.12f;
                    p.Caudal = Tail.Rounded; p.CLen = 0.2f; p.CSpread = 0.36f;
                    p.GillT = 0.24f;
                    break;
                case "garibaldi":
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.22f, 0.14f, 0.38f, 0.25f, 0.48f, 0.4f, 0.5f, 0.56f, 0.44f, 0.72f, 0.3f, 0.86f, 0.15f, 1f, 0.14f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.06f, -0.22f, 0.15f, -0.36f, 0.3f, -0.46f, 0.45f, -0.46f, 0.6f, -0.38f, 0.74f, -0.24f, 0.86f, -0.13f, 1f, -0.12f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.08f, 0.75f, 0.2f, 0.95f, 0.35f, 1f, 0.55f, 0.85f, 0.75f, 0.5f, 1f, 0.25f);
                    p.Nose = 0.06f;
                    p.EyeT = 0.12f; p.EyeTheta = 0.4f; p.EyeR = 0.085f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.06f, MouthFrac = 0.15f, GapeDeg = 12f };
                    p.D0 = 0.25f; p.D1 = 0.84f; p.DH = 0.2f; p.DSpines = 12; p.DSpineEnd = 0.5f; p.DMembrane = 0.85f; p.DRake = 0.35f;
                    p.A0 = 0.6f; p.A1 = 0.84f; p.AH = 0.22f; p.ARake = 0.45f;
                    p.PecT = 0.26f; p.PecLen = 0.15f; p.PelT = 0.31f; p.PelLen = 0.1f;
                    p.Caudal = Tail.Lunate; p.CLen = 0.2f; p.CSpread = 0.42f;
                    break;
                default:
                {
                    bool banded = s.pattern == PatternType.VerticalBands;
                    p.Top = new CreatureMeshCurve(0f, 0.04f, 0.06f, 0.2f, 0.14f, 0.36f, 0.26f, 0.48f, 0.38f, 0.52f, 0.52f, 0.47f, 0.68f, 0.32f, 0.82f, 0.17f, 0.9f, 0.12f, 1f, 0.12f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.06f, -0.2f, 0.14f, -0.34f, 0.28f, -0.45f, 0.42f, -0.46f, 0.56f, -0.38f, 0.7f, -0.24f, 0.83f, -0.13f, 0.9f, -0.11f, 1f, -0.11f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.08f, 0.72f, 0.2f, 0.95f, 0.32f, 1f, 0.5f, 0.88f, 0.68f, 0.6f, 0.85f, 0.3f, 1f, 0.22f);
                    p.Caudal = banded ? Tail.Rounded : Tail.Truncate;
                    p.DSpines = banded ? 10 : 9;
                    break;
                }
            }
            return p;
        }

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var sp = SpecFor(s);
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.06f, 1.2f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.04f, 0.8f);
            float F = CreatureMeshMath.Clamp(s.finSize, 0.3f, 2.5f);
            float Lb = sp.Lb;
            var b = ctx.B;
            ctx.EyeStyle = ctx.Friendly ? CreatureMeshEyeStyle.Friendly : CreatureMeshEyeStyle.Fish;

            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => sp.Top[t] * H, t => sp.Bot[t] * H, t => sp.Wid[t] * W * 0.5f, sp.Nose, 30, 20);
            if (sp.Jaw != null) CreatureMeshAnatomy.PrepareUpperJaw(body, sp.Jaw);

            var chain = ctx.SpineAlong(body, new[] { 0.16f, 0.32f, 0.47f, 0.61f, 0.74f, 0.87f }, -1);
            int head = ctx.Spine[0];
            int last = ctx.Spine[ctx.Spine.Count - 1];
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("TailFin", last, ped);
            ctx.TailFin = tail;

            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.SkinFrom(body);
            ctx.GillU = sp.GillT;
            ctx.EyeU = sp.EyeT;
            ctx.PeduncleU = 0.92f;

            if (sp.Jaw != null) CreatureMeshAnatomy.BuildJaw(ctx, body, sp.Jaw, chain.ByAnchor);

            // eyes
            float eyeR = sp.EyeR * H * (ctx.Friendly ? 1.15f : 1f);
            CreatureMeshParts.EyePair(b, body, sp.EyeT, Pi * 0.5f + sp.EyeTheta, eyeR, 0.25f, 0.45f, 0.72f, CreatureMeshSkin.Fixed(head));

            if (sp.Brows)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = body.Point(sp.EyeT + 0.005f, Pi * 0.5f + sp.EyeTheta + 0.5f);
                    p.x *= side;
                    Vector3 tip = p + new Vector3(side * 0.01f, eyeR * 1.6f, -eyeR * 0.6f);
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Fin, p, tip, eyeR * 0.28f, 5, CreatureMeshParts.FinUv(3, 0.5f, 0.1f), CreatureMeshParts.FinUv(3, 0.5f, 0.9f), CreatureMeshSkin.One(head));
                }
            }

            BuildFins(ctx, body, chain, sp, H, F, head, tail, ped);
        }

        static void BuildFins(CreatureMeshContext ctx, CreatureMeshSweep body, CreatureMeshChain chain, Spec sp, float H, float F, int head, int tail, Vector3 ped)
        {
            var b = ctx.B;

            // ---- dorsal
            if (sp.DH > 0f)
            {
                int cols = sp.DSpines > 0 ? Math.Max(14, sp.DSpines * 5) : 16;
                float span = sp.D1 - sp.D0;
                Func<float, float> h = q =>
                {
                    float baseH = H * F * sp.DH;
                    float round = CreatureMeshMath.Nose(q, sp.DSpines > 0 ? 0.06f : 0.1f) * CreatureMeshMath.Nose(1f - q, 0.22f);
                    float shape;
                    if (sp.DSpines > 0 && sp.DSpineEnd < 1f)
                    {
                        // spiny front part lower, soft rear part taller
                        float spiny = q < sp.DSpineEnd ? CreatureMeshAnatomy.Spikes(q / sp.DSpineEnd, sp.DSpines, sp.DMembrane, 6f) : 1f;
                        shape = CreatureMeshMath.Lerp(0.72f, 1.12f, CreatureMeshMath.Smooth(sp.DSpineEnd - 0.08f, sp.DSpineEnd + 0.12f, q)) * spiny;
                    }
                    else if (sp.DSpines > 0)
                    {
                        // all spines (lionfish / barracuda first dorsal): long separate rays, rising then falling
                        float env = CreatureMeshMath.Lerp(0.75f, 1f, CreatureMeshMath.Sin(Pi * Math.Min(1f, q * 1.4f)));
                        shape = env * CreatureMeshAnatomy.Spikes(q, sp.DSpines, sp.DMembrane, 18f);
                        round = CreatureMeshMath.Nose(1f - q, 0.12f) * Math.Max(0.35f, CreatureMeshMath.Nose(q, 0.04f));
                    }
                    else shape = 1f;
                    return baseH * shape * round;
                };
                Func<float, float> rake = q => h(q) * (sp.DRake + 0.4f * q) + sp.DTrail * CreatureMeshMath.Pow(q, 4f);
                CreatureMeshAnatomy.MedianFin(ctx, body, sp.D0, sp.D1, true, h, rake, cols, 0, chain.ByAnchor, 0.006f * F);
            }
            if (sp.SecondDorsal)
            {
                Func<float, float> h2 = q => H * F * sp.D2H * CreatureMeshMath.Nose(q, 0.12f) * CreatureMeshMath.Nose(1f - q, 0.35f) * CreatureMeshMath.Lerp(1.1f, 0.6f, q);
                Func<float, float> r2 = q => h2(q) * (0.5f + 0.5f * q) + 0.01f * q;
                CreatureMeshAnatomy.MedianFin(ctx, body, sp.D2a, sp.D2b, true, h2, r2, 12, 0, chain.ByAnchor, 0.005f * F);
            }

            // ---- anal
            {
                Func<float, float> h = q => H * F * sp.AH * CreatureMeshMath.Nose(q, 0.15f) * CreatureMeshMath.Nose(1f - q, 0.3f) * CreatureMeshMath.Lerp(1f, 0.85f, q);
                Func<float, float> rake = q => h(q) * (sp.ARake + 0.3f * q) + sp.ATrail * CreatureMeshMath.Pow(q, 4f);
                CreatureMeshAnatomy.MedianFin(ctx, body, sp.A0, sp.A1, false, h, rake, 12, 0, chain.ByAnchor, 0.005f * F);
            }

            // ---- pelvic pair (skinned to the spine)
            {
                int pb = CreatureMeshContext.DominantBone(chain, body.Center(sp.PelT));
                float len = sp.PelLen * H * F * 1.2f + 0.02f * F;
                CreatureMeshAnatomy.PairedFin(ctx,
                    q => body.Point(sp.PelT + 0.035f * q, 0.38f) + Vector3.up * 0.003f,
                    q => body.Point(sp.PelT + 0.035f * q, 0.38f) + new Vector3(0.12f, -0.5f, -0.85f) * (len * CreatureMeshMath.Lerp(1f, 0.55f, q)),
                    pb, pb, 6, 3, 2, 0.004f * F);
            }

            // ---- pectoral pair (own bones)
            {
                float t = sp.PecT;
                Vector3 pa = body.Point(t, Pi * 0.5f - 0.15f), pbp = body.Point(t + 0.03f, Pi * 0.5f - 0.5f);
                Vector3 n = body.Normal(t + 0.015f, Pi * 0.5f - 0.3f);
                Vector3 mid = (pa + pbp) * 0.5f - n * 0.003f;
                int pr = ctx.Bone("PectoralR", CreatureMeshContext.DominantBone(chain, mid), mid);
                Vector3 midL = CreatureMeshMath.MirrorX(mid);
                int pl = ctx.Bone("PectoralL", CreatureMeshContext.DominantBone(chain, mid), midL);
                ctx.Pectorals.Add(pr);
                ctx.Pectorals.Add(pl);
                if (sp.PecFan)
                {
                    // lionfish: huge fan with free ray tips, spread from down-back to back
                    float len = sp.PecLen * F;
                    CreatureMeshAnatomy.PairedFin(ctx,
                        q => Vector3.Lerp(pa, pbp, q) - n * 0.004f,
                        q =>
                        {
                            float ang = CreatureMeshMath.Lerp(0.35f, -1.25f, q);
                            Vector3 d = new Vector3(0.38f, CreatureMeshMath.Sin(ang), -CreatureMeshMath.Cos(ang));
                            float l = len * (0.75f + 0.25f * CreatureMeshMath.Sin(Pi * q)) * CreatureMeshAnatomy.Spikes(q, sp.PecRays, 0.72f, 12f);
                            return Vector3.Lerp(pa, pbp, q) + CreatureMeshMath.Norm(d) * l;
                        },
                        pr, pl, sp.PecRays * 4, 4, 2, 0.004f,
                        (q, r) => Vector3.right * (0.02f * r * r));
                }
                else
                {
                    float len = sp.PecLen * F;
                    CreatureMeshAnatomy.PairedFin(ctx,
                        q => Vector3.Lerp(pa, pbp, q) - n * 0.003f,
                        q =>
                        {
                            Vector3 d = new Vector3(0.42f, 0.12f - 0.25f * q, -0.9f);
                            float l = len * (0.62f + 0.38f * CreatureMeshMath.Sin(Pi * (0.25f + 0.6f * q)));
                            return Vector3.Lerp(pa, pbp, q) + CreatureMeshMath.Norm(d) * l;
                        },
                        pr, pl, 8, 4, 2, 0.004f,
                        (q, r) => Vector3.right * (0.012f * r * r));
                }
            }

            // ---- caudal
            {
                float bh = Math.Max(0.012f, body.HalfTop(0.97f) * 1.15f);
                float L = sp.CLen * (0.85f + 0.15f * F);
                float spread = sp.CSpread * H * F;
                Func<float, float> len, lift;
                switch (sp.Caudal)
                {
                    case Tail.Rounded:
                        len = q => L * (1f - 0.28f * (2f * q - 1f) * (2f * q - 1f));
                        lift = q => (2f * q - 1f) * spread * 0.85f;
                        break;
                    case Tail.Forked:
                        len = q => L * (0.42f + 0.58f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * q - 1f), 0.9f));
                        lift = q => (2f * q - 1f) * spread * (0.5f + 0.5f * CreatureMeshMath.Abs(2f * q - 1f));
                        break;
                    case Tail.Lunate:
                        len = q => L * (0.62f + 0.38f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * q - 1f), 1.3f));
                        lift = q => (2f * q - 1f) * spread;
                        break;
                    case Tail.Truncate:
                        len = q => L * (0.9f + 0.1f * CreatureMeshMath.Abs(2f * q - 1f));
                        lift = q => (2f * q - 1f) * spread * 0.9f;
                        break;
                    default:
                        len = q => L * (0.8f + 0.2f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * q - 1f), 1.5f));
                        lift = q => (2f * q - 1f) * spread * 0.9f;
                        break;
                }
                CreatureMeshAnatomy.CaudalFin(ctx, ped, bh, len, lift, tail, 16, 0.006f * F, 0.006f);
            }
        }
    }
}
