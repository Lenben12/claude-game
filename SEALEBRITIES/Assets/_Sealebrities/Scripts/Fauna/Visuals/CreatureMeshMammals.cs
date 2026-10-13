using System;
using UnityEngine;

namespace Sealebrities
{
    /// Marine mammals of round 2 (G.1): bottlenose dolphin (smooth back, beak with a smile, small cartoon eye,
    /// falcate dorsal, horizontal flukes) and the humpback "Big Bertha" (knobbly head, long white pectoral fins,
    /// ventral pleats, small dorsal hump, broad flukes). Smooth shading (they are the "characters" of the sea).
    /// Rig: spine chain (the animator undulates it vertically like the sea lion), tail fin = flukes, pectorals.
    internal static class CreatureMeshMammals
    {
        const float Pi = CreatureMeshMath.Pi;

        static readonly float[] DolphinTs = { 0f, 0.02f, 0.05f, 0.085f, 0.12f, 0.16f, 0.21f, 0.27f, 0.34f, 0.42f, 0.5f, 0.58f, 0.66f, 0.74f, 0.82f, 0.89f, 0.95f, 1f };

        // ============================================================================================ dolphin

        public static void Dolphin(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Mammal;
            ctx.SmoothBody(1f, 0.85f);
            ctx.SkinSmoothness = 0.24f;   // wet sheen
            ctx.Gait = CreatureGait.Marine;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.15f, 0.3f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.15f, 0.3f);
            float F = CreatureMeshMath.Clamp(s.finSize, 0.6f, 1.6f);
            float Lb = 0.86f;
            var top = new CreatureMeshCurve(0f, 0.1f, 0.05f, 0.17f, 0.09f, 0.22f, 0.13f, 0.44f, 0.18f, 0.58f, 0.25f, 0.66f, 0.38f, 0.66f, 0.52f, 0.56f, 0.68f, 0.38f, 0.84f, 0.2f, 1f, 0.11f);
            var bot = new CreatureMeshCurve(0f, -0.08f, 0.05f, -0.17f, 0.09f, -0.22f, 0.13f, -0.3f, 0.2f, -0.42f, 0.3f, -0.5f, 0.42f, -0.5f, 0.56f, -0.42f, 0.7f, -0.28f, 0.85f, -0.15f, 1f, -0.08f);
            var wid = new CreatureMeshCurve(0f, 0.22f, 0.05f, 0.36f, 0.09f, 0.42f, 0.13f, 0.7f, 0.2f, 0.92f, 0.32f, 1f, 0.48f, 0.9f, 0.64f, 0.6f, 0.8f, 0.34f, 1f, 0.2f);
            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, 0.03f, DolphinTs, 12);
            body.Square = t => 2.15f;

            var chain = ctx.SpineAlong(body, new[] { 0.14f, 0.3f, 0.46f, 0.62f, 0.77f, 0.9f }, -1);
            int head = ctx.Spine[0], last = ctx.Spine[ctx.Spine.Count - 1];
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("Flukes", last, ped);
            ctx.TailFin = tail;
            ctx.Head = head;
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.EyeU = 0.17f;

            // smile: a dark line along the beak that curls up at the corner
            {
                var path = new Vector3[6];
                for (int k = 0; k < 6; k++)
                {
                    float t = CreatureMeshMath.Lerp(0.012f, 0.14f, k / 5f);
                    float th = Pi * 0.5f - 0.32f + 0.22f * CreatureMeshMath.Smooth(0.6f, 1f, k / 5f);
                    path[k] = body.Point(t, th) - body.Normal(t, th) * 0.0015f;
                }
                for (int side = -1; side <= 1; side += 2)
                {
                    var p = new Vector3[6];
                    for (int k = 0; k < 6; k++) p[k] = side > 0 ? path[k] : CreatureMeshMath.MirrorX(path[k]);
                    var tube = CreatureMeshParts.Tube(p, t => 0.0045f * (0.55f + 0.45f * CreatureMeshMath.Sin(Pi * Math.Min(1f, t * 1.2f))), 6, 4, t => Vector3.up);
                    tube.Build(b, CreatureMeshSlot.Mouth, (q, a, t) => CreatureMeshSkin.One(head));
                }
                // the beak tip seam in front
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, body.Point(0.004f, 0f) + new Vector3(0f, 0.004f, -0.002f), Vector3.forward, Vector3.up,
                    0.012f, 0.004f, 0.006f, 3, 6, Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(head));
            }
            // eyes: small dark cartoon eyes with a glint, just above the end of the smile
            CreatureMeshParts.EyePair(ctx, body, 0.17f, Pi * 0.5f + 0.02f, 0.0125f, 0.25f, 0.42f, CreatureMeshSkin.Fixed(head));
            // blowhole
            {
                Vector3 p = body.Point(0.21f, Pi);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, p - Vector3.up * 0.002f, Vector3.up, Vector3.forward, 0.011f, 0.006f, 0.004f, 3, 6,
                    Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(head));
            }

            // dorsal fin (falcate)
            float dH = 0.13f * F;
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.42f, 0.58f, true, q => Falcate(q) * dH, q => FalcateRake(q) * dH * 1.1f, 5, 0, chain.ByAnchor, 0.016f);
            // pectoral flippers (own bones)
            {
                float pt = 0.27f;
                Vector3 pa = body.Point(pt, Pi * 0.5f - 0.75f), pb = body.Point(pt + 0.06f, Pi * 0.5f - 0.8f);
                Vector3 mid = (pa + pb) * 0.5f;
                int bone = CreatureMeshContext.DominantBone(chain, mid);
                int pr = ctx.Bone("PectoralR", bone, mid), pl = ctx.Bone("PectoralL", bone, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(pr); ctx.Pectorals.Add(pl);
                float len = 0.14f * F;
                CreatureMeshAnatomy.PairedFin(ctx, q => Vector3.Lerp(pa, pb, q) - Vector3.right * 0.004f,
                    q => Vector3.Lerp(pa, pb, q) + CreatureMeshMath.Norm(new Vector3(0.75f, -0.45f, -0.55f - 0.5f * q)) * (len * (1f - 0.75f * CreatureMeshMath.Pow(q, 1.2f))),
                    pr, pl, 4, 2, 2, 0.016f, (q, r) => Vector3.up * (-0.008f * r * r));
            }
            Flukes(ctx, ped, tail, 0.17f * F, 0.12f * F, 0.016f, false);
            ctx.CastShadows = true;
        }

        // ============================================================================================ humpback

        static readonly float[] WhaleTs = { 0f, 0.015f, 0.04f, 0.075f, 0.12f, 0.17f, 0.23f, 0.3f, 0.38f, 0.46f, 0.55f, 0.64f, 0.73f, 0.82f, 0.9f, 0.96f, 1f };

        public static void Whale(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Mammal;
            ctx.SmoothBody(1f, 0.85f);
            ctx.SkinSmoothness = 0.2f;
            ctx.Gait = CreatureGait.Marine;
            ctx.Facet(false, false);
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.14f, 0.3f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.15f, 0.35f);
            float F = CreatureMeshMath.Clamp(s.finSize, 0.8f, 1.8f);
            float Lb = 0.88f;
            var top = new CreatureMeshCurve(0f, 0.14f, 0.04f, 0.28f, 0.1f, 0.38f, 0.2f, 0.5f, 0.32f, 0.62f, 0.45f, 0.66f, 0.58f, 0.6f, 0.72f, 0.44f, 0.86f, 0.24f, 1f, 0.12f);
            var bot = new CreatureMeshCurve(0f, -0.2f, 0.04f, -0.4f, 0.1f, -0.56f, 0.2f, -0.7f, 0.32f, -0.72f, 0.45f, -0.66f, 0.58f, -0.54f, 0.72f, -0.36f, 0.86f, -0.2f, 1f, -0.1f);
            var wid = new CreatureMeshCurve(0f, 0.3f, 0.04f, 0.5f, 0.1f, 0.72f, 0.22f, 0.92f, 0.36f, 1f, 0.5f, 0.92f, 0.66f, 0.62f, 0.82f, 0.32f, 1f, 0.2f);
            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, 0.025f, WhaleTs, 12);
            body.Square = t => CreatureMeshMath.Lerp(2.6f, 2.1f, CreatureMeshMath.Smooth(0f, 0.3f, t));   // flat broad head

            var chain = ctx.SpineAlong(body, new[] { 0.16f, 0.32f, 0.48f, 0.63f, 0.77f, 0.9f }, -1);
            int head = ctx.Spine[0], last = ctx.Spine[ctx.Spine.Count - 1];
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("Flukes", last, ped);
            ctx.TailFin = tail;
            ctx.Head = head;
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.EyeU = 0.27f;

            // long arching mouth line from the snout to below the eye
            for (int side = -1; side <= 1; side += 2)
            {
                var p = new Vector3[7];
                for (int k = 0; k < 7; k++)
                {
                    float f = k / 6f;
                    float t = CreatureMeshMath.Lerp(0.01f, 0.27f, f);
                    float th = Pi * 0.5f - 0.18f - 0.32f * CreatureMeshMath.Sin(Pi * f * 0.85f);
                    Vector3 q = body.Point(t, th) - body.Normal(t, th) * 0.002f;
                    p[k] = side > 0 ? q : CreatureMeshMath.MirrorX(q);
                }
                var tube = CreatureMeshParts.Tube(p, t => 0.0055f, 7, 4, t => Vector3.up);
                tube.Build(b, CreatureMeshSlot.Mouth, (q, a, t) => chain.Weigh(a));
            }
            // tubercles: rows of knobs on the head and the chin
            for (int k = 0; k < 16; k++)
            {
                bool chin = k >= 11;
                float t = chin ? CreatureMeshMath.Lerp(0.02f, 0.1f, (k - 11) / 4f) : CreatureMeshMath.Lerp(0.025f, 0.2f, (k % 6) / 5f);
                float th = chin ? (k % 2 == 0 ? 0.25f : -0.25f) : Pi + (k < 6 ? 0.28f : -0.28f) * (k % 6 == 0 ? 0.3f : 1f);
                Vector3 p = body.Point(t, th);
                Vector3 nrm = body.Normal(t, th);
                float r = 0.008f * (chin ? 1.1f : 1f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, p - nrm * (r * 0.3f), nrm, Vector3.forward, r, r, r * 0.8f, 3, 5,
                    new Vector2(t, 0.5f), new Vector2(t, 0.5f), CreatureMeshSkin.Fixed(head));
            }
            // small dark eye with a glint above the mouth corner
            CreatureMeshParts.EyePair(ctx, body, 0.285f, Pi * 0.5f - 0.05f, 0.0075f, 0.2f, 0.45f, CreatureMeshSkin.Fixed(head));
            // blowholes
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = body.Point(0.24f, Pi + side * 0.06f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, p, Vector3.up, Vector3.forward, 0.006f, 0.004f, 0.012f, 3, 5,
                    Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(head));
            }

            // small dorsal hump
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.62f, 0.7f, true, q => 0.035f * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * Math.Min(1f, q * 1.25f)), 0.8f),
                q => 0.02f * q, 3, 0, chain.ByAnchor, 0.02f);
            // very long white pectoral fins with a knobbly leading edge
            {
                float pt = 0.31f;
                Vector3 pa = body.Point(pt, Pi * 0.5f - 0.62f), pb = body.Point(pt + 0.07f, Pi * 0.5f - 0.7f);
                Vector3 mid = (pa + pb) * 0.5f;
                int bone = CreatureMeshContext.DominantBone(chain, mid);
                int pr = ctx.Bone("PectoralR", bone, mid), pl = ctx.Bone("PectoralL", bone, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(pr); ctx.Pectorals.Add(pl);
                float len = 0.31f * F;
                CreatureMeshAnatomy.PairedFin(ctx, q => Vector3.Lerp(pa, pb, q),
                    q =>
                    {
                        Vector3 d = CreatureMeshMath.Norm(new Vector3(0.62f, -0.5f, -0.6f - 0.35f * q));
                        float knobs = 1f - 0.05f * CreatureMeshMath.Abs(CreatureMeshMath.Sin(q * Pi * 5f)) * (1f - q);
                        return Vector3.Lerp(pa, pb, q) + d * (len * (1f - 0.9f * CreatureMeshMath.Pow(q, 1.6f)) * knobs);
                    },
                    pr, pl, 6, 2, 2, 0.02f, (q, r) => Vector3.up * (-0.012f * r * r));
            }
            Flukes(ctx, ped, tail, 0.22f * F, 0.13f * F, 0.02f, true);
            ctx.CastShadows = true;
        }

        /// Horizontal tail flukes at the peduncle: two swept lobes with a central notch (whale: serrated trailing edge).
        static void Flukes(CreatureMeshContext ctx, Vector3 ped, int bone, float span, float chord, float thick, bool serrated)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float sd = side;
                var fin = new CreatureMeshFin
                {
                    // s runs from the centre (0) to the lobe tip (1) along the leading edge, r from the leading to the trailing edge
                    Base = q =>
                    {
                        float x = sd * span * CreatureMeshMath.Pow(q, 0.95f);
                        float z = ped.z + 0.012f - chord * 0.55f * CreatureMeshMath.Pow(q, 1.6f);
                        return new Vector3(x, ped.y, z);
                    },
                    Normal = q => Vector3.up,
                    Cols = 6, Rows = 1, Thickness = thick, Band = 1,
                };
                fin.Edge = q =>
                {
                    Vector3 bp = fin.Base(q);
                    float c = chord * (1f - CreatureMeshMath.Pow(q, 2.2f)) * 0.72f + 0.006f;
                    float notch = 1f - 0.45f * CreatureMeshMath.Gauss(q, 0f, 0.12f);
                    float ser = serrated ? 1f - 0.12f * CreatureMeshMath.Abs(CreatureMeshMath.Sin(q * Pi * 4f)) : 1f;
                    return bp + Vector3.back * (c * notch * ser);
                };
                CreatureMeshParts.Fin(ctx.B, CreatureMeshSlot.Fin, fin, CreatureMeshSkin.Fixed(bone));
            }
        }

        static float Falcate(float q)
        {
            const float apex = 0.6f;
            if (q <= apex) return CreatureMeshMath.Pow(q / apex, 0.75f);
            return CreatureMeshMath.Pow(1f - (q - apex) / (1f - apex), 1.8f);
        }

        static float FalcateRake(float q)
        {
            const float apex = 0.6f;
            if (q <= apex) return 0.6f * CreatureMeshMath.Pow(q / apex, 1.3f);
            float f = (q - apex) / (1f - apex);
            return 0.6f * CreatureMeshMath.Pow(1f - f, 0.55f) + 0.14f * CreatureMeshMath.Sin(Pi * f);
        }
    }
}
