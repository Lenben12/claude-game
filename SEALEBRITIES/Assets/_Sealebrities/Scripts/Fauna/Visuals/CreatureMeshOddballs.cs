using System;
using UnityEngine;

namespace Sealebrities
{
    /// Seahorse, sea lion, ocean sunfish and anglerfish.
    internal static class CreatureMeshOddballs
    {
        const float Pi = CreatureMeshMath.Pi;

        // ============================================================================================ seahorse

        /// Upright, 1 m tall (+Y), snout pointing +Z, belly facing +Z, curled tail at the bottom.
        public static void Seahorse(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.SkinSmoothness = 0.5f;
            var path = new[]
            {
                new Vector3(0f, 0.405f, -0.042f), new Vector3(0f, 0.27f, -0.078f), new Vector3(0f, 0.15f, -0.045f),
                new Vector3(0f, 0.02f, -0.02f), new Vector3(0f, -0.1f, -0.05f), new Vector3(0f, -0.22f, -0.085f),
                new Vector3(0f, -0.33f, -0.075f), new Vector3(0f, -0.42f, -0.03f), new Vector3(0f, -0.452f, 0.035f),
                new Vector3(0f, -0.42f, 0.088f), new Vector3(0f, -0.365f, 0.08f), new Vector3(0f, -0.35f, 0.042f),
            };
            var pts = CreatureMeshMath.Resample(path, 40);
            float bh = CreatureMeshMath.Clamp(s.bodyHeight, 0.3f, 0.7f) / 0.5f;
            var front = new CreatureMeshCurve(0f, 0.045f, 0.12f, 0.05f, 0.25f, 0.068f, 0.36f, 0.078f, 0.45f, 0.06f, 0.55f, 0.042f, 0.7f, 0.03f, 0.85f, 0.02f, 1f, 0.008f);
            var backC = new CreatureMeshCurve(0f, 0.045f, 0.12f, 0.042f, 0.25f, 0.05f, 0.36f, 0.052f, 0.45f, 0.045f, 0.55f, 0.036f, 0.7f, 0.028f, 0.85f, 0.018f, 1f, 0.007f);
            var body = new CreatureMeshSweep
            {
                Rings = 64, Sides = 14,
                Center = t => CreatureMeshMath.PathPoint(pts, t),
                UpHint = t => Vector3.back,                          // dorsal side faces -Z
                HalfTop = t => backC[t] * bh,
                HalfBot = t => front[t] * bh,
                HalfW = t => Math.Min(front[t], backC[t]) * 0.82f * bh,
                Square = t => 2.7f,
                Radial = (t, th) => 1f + 0.07f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(CreatureMeshMath.Sin(t * Pi * 30f)), 6f) * (1f - t * 0.5f),
                Spacing = 0, CapStart = true, CapEnd = true, CapBulgeStart = 0.3f,
            };
            var chain = ctx.SpineAlong(body, new[] { 0f, 0.15f, 0.32f, 0.48f, 0.63f, 0.78f, 0.9f }, -1);
            int head = ctx.Spine[0];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByVertex);
            ctx.SkinFrom(body);

            // head (horizontal) + snout
            var hpth = new[] { new Vector3(0f, 0.43f, -0.075f), new Vector3(0f, 0.427f, -0.02f), new Vector3(0f, 0.405f, 0.04f), new Vector3(0f, 0.385f, 0.085f) };
            var hd = new CreatureMeshSweep
            {
                Rings = 16, Sides = 16,
                Center = t => CreatureMeshMath.PathPoint(hpth, t),
                HalfTop = t => CreatureMeshMath.Lerp(0.052f, 0.022f, CreatureMeshMath.Pow(t, 1.3f)) * CreatureMeshMath.Nose(t, 0.2f) * bh,
                HalfBot = t => CreatureMeshMath.Lerp(0.05f, 0.02f, CreatureMeshMath.Pow(t, 1.1f)) * CreatureMeshMath.Nose(t, 0.2f) * bh,
                HalfW = t => CreatureMeshMath.Lerp(0.038f, 0.017f, t) * CreatureMeshMath.Nose(t, 0.2f) * bh,
                Spacing = 2, SpacingStrength = 0.5f, CapStart = true, CapEnd = false,
                Uv = (t, v) => new Vector2(0.02f, v),
            };
            hd.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(head));
            var snout = CreatureMeshParts.Tube(new[] { new Vector3(0f, 0.39f, 0.07f), new Vector3(0f, 0.38f, 0.14f), new Vector3(0f, 0.37f, 0.205f) },
                t => (0.0155f + 0.004f * CreatureMeshMath.Smooth(0.8f, 1f, t)) * bh, 12, 12, t => Vector3.up);
            snout.CapStart = false;
            snout.CapEnd = false;
            snout.Uv = (t, v) => new Vector2(0.01f, v);
            snout.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(head));
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, new Vector3(0f, 0.37f, 0.204f), Vector3.forward, Vector3.up, 0.014f * bh, 0.012f * bh, 0.004f, 5, 10,
                Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(head));
            // coronet
            Vector3 cr = new Vector3(0f, 0.465f, -0.035f);
            CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, cr - Vector3.up * 0.02f, cr + new Vector3(0f, 0.03f, -0.006f), 0.018f, 6, new Vector2(0.02f, 0.5f), new Vector2(0.02f, 0.5f), CreatureMeshSkin.One(head));
            for (int k = 0; k < 3; k++)
            {
                float a = k / 3f * CreatureMeshMath.TwoPi;
                Vector3 p = cr + new Vector3(CreatureMeshMath.Cos(a) * 0.01f, 0.018f, CreatureMeshMath.Sin(a) * 0.01f);
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, p + new Vector3(CreatureMeshMath.Cos(a) * 0.008f, 0.012f, CreatureMeshMath.Sin(a) * 0.008f), 0.004f, 4, new Vector2(0.02f, 0.5f), new Vector2(0.02f, 0.5f), CreatureMeshSkin.One(head));
            }
            // eyes
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 c = new Vector3(side * 0.031f * bh, 0.418f, 0.0f);
                CreatureMeshParts.Eye(b, c, CreatureMeshMath.Norm(new Vector3(side, 0.15f, 0.35f)), Vector3.up, 0.021f, 0.8f, CreatureMeshSkin.Fixed(head));
            }
            // knobby spines along the back
            for (int k = 0; k < 9; k++)
            {
                float t = 0.04f + k * 0.055f;
                Vector3 p = body.Point(t, Pi);
                Vector3 n = body.Normal(t, Pi);
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 q = p + new Vector3(side * body.HalfW(t) * 0.55f, 0f, 0f) - n * 0.003f;
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, q, q + n * 0.012f + new Vector3(side * 0.004f, 0f, 0f), 0.005f, 4,
                        new Vector2(t, 0.5f), new Vector2(t, 0.5f), chain.Weigh(q));
                }
            }

            // dorsal fin (flutters: registered as a pectoral so the animator buzzes it)
            {
                float t0 = 0.43f, t1 = 0.53f;
                Vector3 mid = body.Point((t0 + t1) * 0.5f, Pi);
                int db = ctx.Bone("DorsalFin", CreatureMeshContext.DominantBone(chain, mid), mid);
                var fin = new CreatureMeshFin
                {
                    Base = q => body.Point(CreatureMeshMath.Lerp(t0, t1, q), Pi) + Vector3.forward * 0.004f,
                    Normal = q => Vector3.right,
                    Cols = 12, Rows = 3, Thickness = 0.003f, Band = 0,
                };
                fin.Edge = q => fin.Base(q) + Vector3.back * (0.065f * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * q), 0.5f) * CreatureMeshMath.Lerp(1f, 0.85f, q)) + Vector3.down * 0.01f * q;
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, CreatureMeshSkin.Fixed(db));
                // pectoral fins behind the head
                Vector3 pa = body.Point(0.06f, Pi * 0.5f + 0.4f), pb = body.Point(0.1f, Pi * 0.5f + 0.4f);
                Vector3 pm = (pa + pb) * 0.5f;
                int pr = ctx.Bone("PectoralR", head, pm), pl = ctx.Bone("PectoralL", head, CreatureMeshMath.MirrorX(pm));
                CreatureMeshAnatomy.PairedFin(ctx, q => Vector3.Lerp(pa, pb, q),
                    q => Vector3.Lerp(pa, pb, q) + CreatureMeshMath.Norm(new Vector3(0.6f, 0.1f, -0.8f)) * (0.035f * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * q), 0.6f)),
                    pr, pl, 6, 3, 2, 0.002f);
                ctx.Pectorals.Add(pr); ctx.Pectorals.Add(pl); ctx.Pectorals.Add(db);
            }
        }

        // ============================================================================================ sea lion

        public static void SeaLion(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Mammal;
            ctx.SkinSmoothness = 0.6f;
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.15f, 0.4f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.15f, 0.4f);
            float Lb = 0.82f;
            var top = new CreatureMeshCurve(0f, 0.03f, 0.03f, 0.16f, 0.07f, 0.3f, 0.1f, 0.36f, 0.16f, 0.34f, 0.26f, 0.44f, 0.38f, 0.5f, 0.55f, 0.47f, 0.72f, 0.35f, 0.86f, 0.22f, 1f, 0.12f);
            var bot = new CreatureMeshCurve(0f, -0.03f, 0.03f, -0.1f, 0.07f, -0.17f, 0.12f, -0.24f, 0.2f, -0.36f, 0.32f, -0.48f, 0.45f, -0.5f, 0.6f, -0.44f, 0.76f, -0.3f, 0.9f, -0.18f, 1f, -0.1f);
            var wid = new CreatureMeshCurve(0f, 0.15f, 0.04f, 0.32f, 0.08f, 0.5f, 0.12f, 0.52f, 0.17f, 0.48f, 0.3f, 0.85f, 0.42f, 1f, 0.6f, 0.88f, 0.78f, 0.6f, 0.92f, 0.35f, 1f, 0.22f);
            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => top[t] * H, t => bot[t] * H, t => wid[t] * W * 0.5f, 0.045f, 36, 22);
            var chain = ctx.SpineAlong(body, new[] { 0.08f, 0.2f, 0.38f, 0.58f, 0.78f }, -1);
            int head = ctx.Spine[0];
            int last = ctx.Spine[ctx.Spine.Count - 1];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.SkinFrom(body);
            ctx.EyeU = 0.075f;

            // big dark eyes
            CreatureMeshParts.EyePair(b, body, 0.078f, Pi * 0.5f + 0.62f, 0.019f, 0.35f, 0.42f, 0.8f, CreatureMeshSkin.Fixed(head));
            // nostrils
            for (int side = -1; side <= 1; side += 2)
            {
                float th = Pi + side * 0.4f;
                Vector3 p = body.Point(0.012f, th);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, p, body.Normal(0.012f, th), Vector3.forward, 0.0045f, 0.0025f, 0.0025f, 4, 8,
                    Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(head));
            }
            // whiskers
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < 5; k++)
                {
                    float t = 0.022f + 0.006f * (k % 3);
                    float th = Pi * 0.5f - 0.12f - k * 0.1f;
                    Vector3 p = body.Point(t, th);
                    Vector3 n = body.Normal(t, th);
                    if (side < 0) { p = CreatureMeshMath.MirrorX(p); n = CreatureMeshMath.MirrorX(n); }
                    Vector3 dir = CreatureMeshMath.Norm(n + Vector3.back * 0.9f + Vector3.down * (0.1f + 0.05f * k));
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Teeth, p - n * 0.001f, p + dir * 0.045f, 0.0011f, 3, Vector2.zero, Vector2.zero, CreatureMeshSkin.One(head));
                }
            // ears
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 p = body.Point(0.125f, Pi * 0.5f + 0.7f);
                if (side < 0) p = CreatureMeshMath.MirrorX(p);
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Skin, p, p + new Vector3(side * 0.003f, -0.001f, -0.012f), 0.0035f, 5, new Vector2(0.12f, 0.6f), new Vector2(0.12f, 0.6f), CreatureMeshSkin.One(head));
            }

            // fore flippers (long, wing-like)
            int chest = CreatureMeshContext.DominantBone(chain, body.Center(0.27f));
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                Vector3 root = body.Point(0.27f, Pi * 0.5f - 0.55f);
                root.x *= sd;
                root -= new Vector3(sd * 0.02f, 0f, 0f);
                var fp = new[] { root, root + new Vector3(sd * 0.1f, -0.035f, -0.06f), root + new Vector3(sd * 0.19f, -0.06f, -0.14f), root + new Vector3(sd * 0.25f, -0.075f, -0.24f) };
                int fb = ctx.Bone(side > 0 ? "ForeFlipperR" : "ForeFlipperL", chest, root);
                ctx.Flippers.Add(fb);
                CreatureMeshAnatomy.Paddle(b, fp, t => CreatureMeshMath.Lerp(0.04f, 0.05f, CreatureMeshMath.Sin(Pi * Math.Min(1f, t * 1.4f))) * CreatureMeshMath.Nose(1f - t, 0.2f) + 0.004f,
                    t => CreatureMeshMath.Lerp(0.016f, 0.004f, t), t => Vector3.up, (t, v) => CreatureMeshParts.FinUv(3, v, t), CreatureMeshSkin.Fixed(fb), CreatureMeshSlot.Fin);
            }
            // hind flippers: trailing, soles facing each other (vertical planes)
            Vector3 hip = body.Center(0.93f);
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                Vector3 root = hip + new Vector3(sd * 0.022f, -0.004f, 0.01f);
                var hp = new[] { root, root + new Vector3(sd * 0.022f, -0.004f, -0.08f), root + new Vector3(sd * 0.045f, -0.008f, -0.16f), root + new Vector3(sd * 0.06f, -0.01f, -0.205f) };
                int hb = ctx.Bone(side > 0 ? "HindFlipperR" : "HindFlipperL", last, root);
                ctx.Flippers.Add(hb);
                CreatureMeshAnatomy.Paddle(b, hp, t => CreatureMeshMath.Lerp(0.02f, 0.058f, CreatureMeshMath.Pow(t, 0.8f)) * CreatureMeshMath.Nose(1f - t, 0.06f) + 0.003f,
                    t => CreatureMeshMath.Lerp(0.014f, 0.003f, t), t => new Vector3(sd, 0f, 0f), (t, v) => CreatureMeshParts.FinUv(3, v, t), CreatureMeshSkin.Fixed(hb), CreatureMeshSlot.Fin,
                    (t, th) => 1f + 0.12f * CreatureMeshMath.Smooth(0.85f, 1f, t) * CreatureMeshMath.Abs(CreatureMeshMath.Sin(th * 2.5f)));
            }
        }

        // ============================================================================================ sunfish

        public static void Sunfish(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Friendly;
            ctx.SkinSmoothness = 0.45f;
            float hmax = 0.5f * CreatureMeshMath.Clamp(s.bodyHeight, 0.5f, 1.2f) * 0.82f;
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.1f, 0.35f);
            float F = CreatureMeshMath.Clamp(s.finSize, 0.5f, 1.8f);
            float Lb = 0.88f;
            var top = new CreatureMeshCurve(0f, 0.1f, 0.05f, 0.38f, 0.12f, 0.6f, 0.25f, 0.82f, 0.4f, 0.9f, 0.55f, 0.86f, 0.7f, 0.74f, 0.85f, 0.62f, 1f, 0.56f);
            var bot = new CreatureMeshCurve(0f, -0.12f, 0.05f, -0.36f, 0.12f, -0.56f, 0.25f, -0.78f, 0.4f, -0.86f, 0.55f, -0.82f, 0.7f, -0.7f, 0.85f, -0.58f, 1f, -0.52f);
            var wid = new CreatureMeshCurve(0f, 0.3f, 0.08f, 0.72f, 0.2f, 1f, 0.45f, 0.9f, 0.75f, 0.6f, 1f, 0.38f);
            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => top[t] * hmax, t => bot[t] * hmax, t => wid[t] * W * 0.5f, 0.04f, 30, 24);
            body.CapBulgeEnd = 0.15f;
            var jaw = new CreatureMeshJaw { HingeT = 0.035f, MouthFrac = 0.05f, GapeDeg = 20f };
            CreatureMeshAnatomy.PrepareUpperJaw(body, jaw);
            var chain = ctx.SpineAlong(body, new[] { 0.15f, 0.45f, 0.72f }, -1);
            int head = ctx.Spine[0], rear = ctx.Spine[2];
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.SkinFrom(body);
            ctx.EyeU = 0.11f;
            CreatureMeshAnatomy.BuildJaw(ctx, body, jaw, chain.ByAnchor);
            CreatureMeshParts.EyePair(b, body, 0.11f, Pi * 0.5f + 0.12f, 0.034f, 0.2f, 0.45f, 0.75f, CreatureMeshSkin.Fixed(head));

            // gill opening
            {
                Vector3 p = body.Point(0.27f, Pi * 0.5f - 0.02f);
                for (int side = -1; side <= 1; side += 2)
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, side > 0 ? p : CreatureMeshMath.MirrorX(p), new Vector3(side, 0f, 0f), Vector3.up, 0.008f, 0.016f, 0.004f, 5, 10,
                        Vector2.zero, Vector2.zero, chain.ByAnchor);
            }

            // tall dorsal and anal fins on their own bones (animator sculls them as vertical flippers)
            float dt0 = 0.63f, dt1 = 0.84f;
            float finH = 0.5f * F;
            for (int k = 0; k < 2; k++)
            {
                bool topFin = k == 0;
                float th = topFin ? Pi : 0f;
                Vector3 mid = body.Point((dt0 + dt1) * 0.5f, th);
                int fb = ctx.Bone(topFin ? "DorsalFin" : "AnalFin", rear, mid);
                ctx.Flippers.Add(fb);
                float dir = topFin ? 1f : -1f;
                int finBone = fb;
                var fin = new CreatureMeshFin
                {
                    Base = q => body.Point(CreatureMeshMath.Lerp(dt0, dt1, q), th) - Vector3.up * (dir * 0.01f),
                    Normal = q => Vector3.right, Cols = 16, Rows = 5, Thickness = 0.03f, Band = 0, RowPower = 0.9f,
                    // root glued to the body, the blade sculls with the fin bone
                    SkinSR = (q, r, bp) => CreatureMeshSkin.Blend(chain.Weigh(bp), CreatureMeshSkin.One(finBone), CreatureMeshMath.Smooth(0f, 0.3f, r)),
                };
                fin.Edge = q =>
                {
                    float hgt = finH * (q < 0.72f ? CreatureMeshMath.Pow(q / 0.72f, 0.7f) : CreatureMeshMath.Pow(1f - (q - 0.72f) / 0.28f, 1.4f));
                    float rake = finH * 0.42f * (q < 0.72f ? CreatureMeshMath.Pow(q / 0.72f, 1.4f) : 1f - 0.5f * (q - 0.72f) / 0.28f);
                    return fin.Base(q) + Vector3.up * (dir * hgt) + Vector3.back * rake;
                };
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, CreatureMeshSkin.Fixed(fb));
            }

            // clavus: scalloped rudder replacing the tail
            {
                Vector3 c = body.Center(1f);
                float yb = bot[1f] * hmax * 0.98f, yt = top[1f] * hmax * 0.98f;
                int cb = ctx.Bone("Clavus", rear, c);
                ctx.TailFin = cb;
                var fin = new CreatureMeshFin
                {
                    Base = q => new Vector3(0f, CreatureMeshMath.Lerp(yb, yt, q) + c.y * 0f, c.z + 0.02f),
                    Normal = q => Vector3.right, Cols = 28, Rows = 4, Thickness = 0.03f, Band = 1,
                };
                fin.Edge = q =>
                {
                    float l = 0.12f * (0.78f + 0.22f * CreatureMeshMath.Abs(CreatureMeshMath.Sin(q * Pi * 6f))) * CreatureMeshMath.Nose(Math.Min(q, 1f - q), 0.12f);
                    return fin.Base(q) + Vector3.back * (l + 0.02f);
                };
                CreatureMeshParts.Fin(b, CreatureMeshSlot.Fin, fin, CreatureMeshSkin.Fixed(cb));
            }

            // small rounded pectorals
            {
                Vector3 pa = body.Point(0.3f, Pi * 0.5f + 0.15f), pbp = body.Point(0.33f, Pi * 0.5f - 0.25f);
                Vector3 mid = (pa + pbp) * 0.5f;
                int pr = ctx.Bone("PectoralR", head, mid), pl = ctx.Bone("PectoralL", head, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(pr); ctx.Pectorals.Add(pl);
                CreatureMeshAnatomy.PairedFin(ctx, q => Vector3.Lerp(pa, pbp, q),
                    q => Vector3.Lerp(pa, pbp, q) + CreatureMeshMath.Norm(new Vector3(0.35f, 0.05f, -1f)) * (0.075f * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * (0.15f + 0.7f * q)), 0.6f)),
                    pr, pl, 8, 3, 2, 0.008f);
            }
        }

        // ============================================================================================ anglerfish

        public static void Anglerfish(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Angler;
            ctx.SkinSmoothness = 0.42f;
            float hmax = 0.5f * CreatureMeshMath.Clamp(s.bodyHeight, 0.3f, 0.9f);
            float hw = 0.5f * CreatureMeshMath.Clamp(s.bodyWidth, 0.25f, 0.8f);
            float Lb = 0.78f;
            var top = new CreatureMeshCurve(0f, 0.3f, 0.04f, 0.55f, 0.12f, 0.8f, 0.25f, 0.96f, 0.4f, 1f, 0.55f, 0.9f, 0.7f, 0.6f, 0.84f, 0.3f, 0.94f, 0.18f, 1f, 0.16f);
            var bot = new CreatureMeshCurve(0f, -0.3f, 0.04f, -0.55f, 0.12f, -0.78f, 0.25f, -0.92f, 0.4f, -0.95f, 0.55f, -0.82f, 0.7f, -0.52f, 0.84f, -0.26f, 0.94f, -0.15f, 1f, -0.14f);
            var wid = new CreatureMeshCurve(0f, 0.5f, 0.08f, 0.82f, 0.2f, 0.96f, 0.35f, 1f, 0.55f, 0.86f, 0.75f, 0.5f, 0.9f, 0.3f, 1f, 0.25f);
            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => top[t] * hmax, t => bot[t] * hmax, t => wid[t] * hw, 0.04f, 32, 24);
            var jaw = new CreatureMeshJaw
            {
                HingeT = 0.3f, MouthFrac = 0.04f, GapeDeg = 30f, Extend = 0.045f, JawLift = 0.025f, JawWidth = 1.03f, JawDepth = 0.95f,
                Teeth = 9, ToothLen = 0.05f, ToothRadius = 0.0035f, UpperTeeth = 0.75f, LowerTeeth = 1.15f, FangBoost = 0.5f, ToothInward = 0.32f,
            };
            CreatureMeshAnatomy.PrepareUpperJaw(body, jaw);
            var chain = ctx.SpineAlong(body, new[] { 0.2f, 0.45f, 0.65f, 0.85f }, -1);
            int head = ctx.Spine[0], last = ctx.Spine[3];
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("TailFin", last, ped);
            ctx.TailFin = tail;
            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.SkinFrom(body);
            ctx.GillU = -1f;
            ctx.EyeU = 0.2f;
            CreatureMeshAnatomy.BuildJaw(ctx, body, jaw, chain.ByAnchor);

            // small milky eyes high on the head
            CreatureMeshParts.EyePair(b, body, 0.2f, Pi * 0.5f + 0.8f, 0.017f, 0.4f, 0.4f, 0.85f, CreatureMeshSkin.Fixed(head));

            // illicium (fishing rod) with the glowing esca
            Vector3 rb = body.Point(0.15f, Pi) - Vector3.up * 0.004f;
            var rod = new[] { rb, rb + new Vector3(0f, 0.1f, 0.025f), rb + new Vector3(0f, 0.17f, 0.11f), rb + new Vector3(0f, 0.155f, 0.22f) };
            int lure = ctx.Bone("Lure", head, rb);
            ctx.Lure = lure;
            var stalk = CreatureMeshParts.Tube(rod, t => CreatureMeshMath.Lerp(0.009f, 0.0045f, t), 20, 7, t => Vector3.right);
            stalk.CapStart = false;
            stalk.Uv = (t, v) => CreatureMeshParts.FinUv(0, v, 0.2f + 0.6f * t);
            stalk.Build(b, CreatureMeshSlot.Fin, (p, a, t) => CreatureMeshSkin.Blend(CreatureMeshSkin.One(head), CreatureMeshSkin.One(lure), CreatureMeshMath.Smooth(0f, 0.15f, t)));
            Vector3 bulb = rod[3] + new Vector3(0f, -0.022f, 0.012f);
            ctx.LurePos = bulb;
            ctx.LureColor = new Color(s.colorB.r, s.colorB.g, s.colorB.b, 1f);
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Glow, bulb, Vector3.forward, Vector3.up, 0.03f, 0.034f, 0.03f, 9, 14, Vector2.zero, Vector2.zero, CreatureMeshSkin.Fixed(lure));
            for (int k = 0; k < 3; k++)
            {
                float a = k * 2.1f + 0.4f;
                Vector3 d = CreatureMeshMath.Norm(new Vector3(CreatureMeshMath.Cos(a) * 0.6f, -0.8f, CreatureMeshMath.Sin(a) * 0.6f));
                CreatureMeshParts.Cone(b, CreatureMeshSlot.Glow, bulb + d * 0.026f, bulb + d * 0.05f, 0.004f, 4, Vector2.zero, Vector2.zero, CreatureMeshSkin.One(lure));
            }

            // fins
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.72f, 0.86f, true, q => 0.07f * CreatureMeshMath.Nose(q, 0.2f) * CreatureMeshMath.Nose(1f - q, 0.3f), q => 0.03f * q, 10, 0, chain.ByAnchor, 0.006f);
            CreatureMeshAnatomy.MedianFin(ctx, body, 0.75f, 0.88f, false, q => 0.06f * CreatureMeshMath.Nose(q, 0.2f) * CreatureMeshMath.Nose(1f - q, 0.3f), q => 0.03f * q, 10, 0, chain.ByAnchor, 0.006f);
            {
                Vector3 pa = body.Point(0.56f, Pi * 0.5f - 0.1f), pbp = body.Point(0.6f, Pi * 0.5f - 0.45f);
                Vector3 mid = (pa + pbp) * 0.5f;
                int bone = CreatureMeshContext.DominantBone(chain, mid);
                int pr = ctx.Bone("PectoralR", bone, mid), pl = ctx.Bone("PectoralL", bone, CreatureMeshMath.MirrorX(mid));
                ctx.Pectorals.Add(pr); ctx.Pectorals.Add(pl);
                CreatureMeshAnatomy.PairedFin(ctx, q => Vector3.Lerp(pa, pbp, q),
                    q => Vector3.Lerp(pa, pbp, q) + CreatureMeshMath.Norm(new Vector3(0.45f, 0f, -0.9f)) * (0.09f * CreatureMeshMath.Pow(CreatureMeshMath.Sin(Pi * (0.1f + 0.8f * q)), 0.6f)),
                    pr, pl, 8, 3, 2, 0.006f);
            }
            {
                float bh = Math.Max(0.02f, body.HalfTop(0.97f) * 1.1f);
                CreatureMeshAnatomy.CaudalFin(ctx, ped, bh, q => 0.2f * (1f - 0.3f * (2f * q - 1f) * (2f * q - 1f)), q => (2f * q - 1f) * 0.12f, tail, 12, 0.008f);
            }
        }
    }
}
