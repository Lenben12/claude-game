using System;
using UnityEngine;

namespace Sealebrities
{
    /// Open-mouth description: the upper head is flattened at the mouth line, a separate lower jaw is
    /// hinged at the mouth corner, a dark palate/tongue/gullet fills the opening, teeth line both jaws.
    internal sealed class CreatureMeshJaw
    {
        public float HingeT = 0.08f;      // body t of the mouth corner
        public float MouthFrac = 0.25f;   // mouth line below the section center, fraction of the half height
        public float GapeDeg = 12f;
        public float Extend;              // lower jaw tip reaches this far beyond the snout (m)
        public float JawWidth = 1f, JawDepth = 1f;
        public float JawLift;             // extra upturn of the jaw tip (m)
        public int Teeth;                 // per side per jaw
        public float ToothLen = 0.01f, ToothRadius = 0.002f;
        public float UpperTeeth = 1f, LowerTeeth = 1f;  // length multipliers
        public float FangBoost = 0.6f;    // front teeth longer by this fraction
        public float ToothInward = 0.25f;
    }

    internal static class CreatureMeshAnatomy
    {
        const float Pi = CreatureMeshMath.Pi;

        /// Straight body along -Z: t = 0 at z0 (snout), t = 1 at z0 - len. Profiles are absolute y of the
        /// dorsal / ventral outline and the half width; nose rounds the first noseT of the body.
        /// ts: explicit ring positions (low-poly bodies use 10-14 rings), sides: 6-8 for chunky facets.
        public static CreatureMeshSweep Body(float z0, float len, Func<float, float> yTop, Func<float, float> yBot, Func<float, float> halfW,
                                             float noseT, float[] ts, int sides)
        {
            return new CreatureMeshSweep
            {
                Ts = ts, Rings = ts != null ? ts.Length : 12, Sides = sides,
                Center = t => new Vector3(0f, 0.5f * (yTop(t) + yBot(t)), z0 - len * t),
                HalfTop = t => Math.Max(1e-5f, 0.5f * (yTop(t) - yBot(t)) * CreatureMeshMath.Nose(t, noseT)),
                HalfBot = t => Math.Max(1e-5f, 0.5f * (yTop(t) - yBot(t)) * CreatureMeshMath.Nose(t, noseT)),
                HalfW = t => Math.Max(1e-5f, halfW(t) * CreatureMeshMath.Nose(t, noseT)),
                Spacing = 1, SpacingStrength = 0.75f,
                CapStart = true, CapEnd = true,
            };
        }

        static float MouthLine(CreatureMeshSweep body, CreatureMeshJaw jaw, float t) => -jaw.MouthFrac * body.HalfBot(t);

        static float HalfWidthAt(CreatureMeshSweep body, float t, float yLocal)
        {
            float hb = yLocal < 0f ? body.HalfBot(t) : body.HalfTop(t);
            float n = body.Square != null ? body.Square(t) : 2f;
            float f = hb > 1e-6f ? CreatureMeshMath.Clamp01(CreatureMeshMath.Abs(yLocal) / hb) : 1f;
            return body.HalfW(t) * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(f, n)), 1f / n);
        }

        /// Installs the floor clamp on the body (call before body.Build).
        public static void PrepareUpperJaw(CreatureMeshSweep body, CreatureMeshJaw jaw)
        {
            var prev = body.FloorY;
            body.FloorY = t =>
            {
                if (t <= jaw.HingeT) return MouthLine(body, jaw, t);
                return prev != null ? prev(t) : float.NaN;
            };
        }

        /// Builds lower jaw, palate, tongue, gullet and teeth (all low poly). Call after the body was built.
        public static void BuildJaw(CreatureMeshContext ctx, CreatureMeshSweep body, CreatureMeshJaw jaw, CreatureMeshSkinFn skin, float skinU = -1f)
        {
            var b = ctx.B;
            float h = jaw.HingeT;
            Vector3 hingeC = body.Center(h);
            body.Frame(h, out _, out _, out Vector3 hingeUp, out _);
            Vector3 pivot = hingeC + hingeUp * MouthLine(body, jaw, h);
            float gape = jaw.GapeDeg * Mathf.Deg2Rad;
            float mouthLen = (body.Center(0f) - hingeC).magnitude;
            Func<Vector3, Vector3> rot = p =>
            {
                Vector3 q = CreatureMeshMath.RotateAround(p, pivot, Vector3.right, gape);
                if (jaw.JawLift != 0f)
                {
                    float f = CreatureMeshMath.Clamp01((q.z - pivot.z) / Math.Max(0.001f, mouthLen + jaw.Extend));
                    q.y += jaw.JawLift * f * f;
                }
                return q;
            };

            // ---- palate (under the flattened upper jaw), faces down
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Mouth, 3, 1,
                (u, v) =>
                {
                    float t = u * h;
                    body.Frame(t, out Vector3 c, out Vector3 side, out Vector3 up, out _);
                    float y = MouthLine(body, jaw, t);
                    float xw = HalfWidthAt(body, t, y) * 0.97f;
                    return c + side * CreatureMeshMath.Lerp(-xw, xw, v) + up * (y - 0.0012f);
                },
                (u, v) => new Vector2(0.5f, 0.5f), p => Vector3.down, (p, u, v) => skin(p, p, u * h),
                u => CreatureMeshMath.Sqrt(u));

            // ---- lower jaw (lower part of the head section, extended forward, rotated open)
            int jawStart = b.Count;
            float ext = jaw.Extend;
            var lower = new CreatureMeshSweep
            {
                Rings = 4, Sides = body.Sides,
                Center = tau =>
                {
                    float t = tau * h;
                    return body.Center(t) + Vector3.forward * (ext * (1f - tau));
                },
                HalfW = tau => Math.Max(1e-5f, body.HalfW(tau * h) * jaw.JawWidth * (ext > 0f ? CreatureMeshMath.Lerp(0.55f, 1f, CreatureMeshMath.Nose(tau, 0.5f)) : 1f) + (ext > 0f ? 0.004f : 0f)),
                HalfTop = tau => Math.Max(1e-5f, body.HalfTop(tau * h)),
                HalfBot = tau => Math.Max(1e-5f, body.HalfBot(tau * h) * jaw.JawDepth + (ext > 0f ? 0.25f * ext * (1f - tau) : 0f)),
                Square = body.Square != null ? (Func<float, float>)(tau => body.Square(tau * h)) : null,
                CeilY = tau => MouthLine(body, jaw, tau * h),
                Spacing = 2, CapStart = false, CapEnd = true,
                Uv = (tau, v) => new Vector2(skinU >= 0f ? skinU : tau * h, v),
            };
            if (ext > 0f) lower.CapStart = true;
            lower.Build(b, CreatureMeshSlot.Skin, (p, a, t) => skin(p, a, t * h));

            // ---- tongue (top of the lower jaw), faces up
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Mouth, 3, 1,
                (u, v) =>
                {
                    float tau = Math.Max(0.02f, u);
                    lower.Frame(tau, out Vector3 c, out Vector3 side, out Vector3 up, out _);
                    float y = MouthLine(body, jaw, tau * h);
                    float hb = lower.HalfBot(tau);
                    float f = hb > 1e-6f ? CreatureMeshMath.Clamp01(CreatureMeshMath.Abs(y) / hb) : 1f;
                    float xw = lower.HalfW(tau) * CreatureMeshMath.Sqrt(Math.Max(0f, 1f - f * f)) * 0.95f;
                    return c + side * CreatureMeshMath.Lerp(-xw, xw, v) + up * (y + 0.0012f);
                },
                (u, v) => new Vector2(0.5f, 0.5f), p => Vector3.up, (p, u, v) => skin(p, p, u * h),
                u => CreatureMeshMath.Sqrt(u));

            // ---- lower teeth (built in the closed pose, rotated with the jaw): chunky 3-sided spikes
            bool teeth = jaw.Teeth > 0 && jaw.ToothLen > 0f;
            int n = Math.Max(1, jaw.Teeth);
            if (teeth)
            {
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < n; k++)
                    {
                        float f = (k + 0.6f) / (n + 0.4f);
                        float fang = 1f + jaw.FangBoost * (1f - f) * (1f - f);
                        float tau = Math.Max(0.06f, f * 0.95f);
                        lower.Frame(tau, out Vector3 c, out Vector3 sd, out Vector3 up, out _);
                        float y = MouthLine(body, jaw, tau * h);
                        float hb = lower.HalfBot(tau);
                        float ff = hb > 1e-6f ? CreatureMeshMath.Clamp01(CreatureMeshMath.Abs(y) / hb) : 1f;
                        float xw = lower.HalfW(tau) * CreatureMeshMath.Sqrt(Math.Max(0f, 1f - ff * ff)) * 0.84f;
                        Vector3 basePt = c + sd * (xw * side) + up * (y - 0.0015f);
                        Vector3 dir = CreatureMeshMath.Norm(up - sd * (side * jaw.ToothInward) + Vector3.back * 0.12f);
                        float l = jaw.ToothLen * jaw.LowerTeeth * fang * (0.85f + 0.3f * CreatureMeshNoise.Rand(k, side, 2, 7));
                        CreatureMeshParts.Cone(b, CreatureMeshSlot.Teeth, basePt, basePt + dir * l, jaw.ToothRadius * fang, 3,
                            new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.9f), skin(basePt, basePt, tau * h));
                    }
            }
            // open the mouth: rotate jaw sweep, tongue and lower teeth around the hinge
            b.TransformRange(jawStart, b.Count, rot);

            // ---- upper teeth
            if (teeth)
            {
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < n; k++)
                    {
                        float f = (k + 0.6f) / (n + 0.4f);
                        float fang = 1f + jaw.FangBoost * (1f - f) * (1f - f);
                        float t = f * h * 0.95f;
                        body.Frame(t, out Vector3 c, out Vector3 sd, out Vector3 up, out _);
                        float y = MouthLine(body, jaw, t);
                        float xw = HalfWidthAt(body, t, y) * 0.86f;
                        Vector3 basePt = c + sd * (xw * side) + up * (y + 0.0015f);
                        Vector3 dir = CreatureMeshMath.Norm(-up - sd * (side * jaw.ToothInward) + Vector3.back * 0.1f);
                        float l = jaw.ToothLen * jaw.UpperTeeth * fang * (0.85f + 0.3f * CreatureMeshNoise.Rand(k, side, 1, 7));
                        CreatureMeshParts.Cone(b, CreatureMeshSlot.Teeth, basePt, basePt + dir * l, jaw.ToothRadius * fang, 3,
                            new Vector2(0.5f, 0.2f), new Vector2(0.5f, 0.9f), skin(basePt, basePt, t));
                    }
            }

            // ---- gullet: dark cavity filling the back of the open mouth
            float tg = h * 0.62f;
            body.Frame(tg, out Vector3 gc, out _, out Vector3 gup, out _);
            float gy = MouthLine(body, jaw, tg);
            float gxw = HalfWidthAt(body, tg, gy);
            float drop = Math.Max(0.004f, CreatureMeshMath.Sin(gape) * mouthLen * 0.42f);
            Vector3 gcen = gc + gup * gy + Vector3.down * (drop * 0.45f) + Vector3.back * (mouthLen * 0.05f);
            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Mouth, gcen, Vector3.forward, Vector3.up,
                gxw * 0.86f, Math.Max(drop * 0.75f, body.HalfBot(h) * 0.25f), mouthLen * 0.48f, 4, 6,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), (p, a, t) => skin(p, p, tg));
        }

        // ------------------------------------------------------------------------------------------ fins

        /// Fin on the dorsal (top = true) or ventral midline between t0 and t1.
        public static void MedianFin(CreatureMeshContext ctx, CreatureMeshSweep body, float t0, float t1, bool top,
                                     Func<float, float> height, Func<float, float> rake, int cols, int band, CreatureMeshSkinFn skin,
                                     float thickness = 0.006f, int slot = CreatureMeshSlot.Fin, Func<float, float, Vector2> uv = null, int rows = 1)
        {
            float theta = top ? Pi : 0f;
            float dir = top ? 1f : -1f;
            var fin = new CreatureMeshFin
            {
                Base = s =>
                {
                    float t = CreatureMeshMath.Lerp(t0, t1, s);
                    Vector3 p = body.Point(t, theta);
                    return p - Vector3.up * (dir * Math.Min(0.004f, body.HalfTop(t) * 0.25f));
                },
                Normal = s => Vector3.right,
                Cols = cols, Rows = rows, Thickness = thickness, Band = band, Uv = uv,
            };
            fin.Edge = s => fin.Base(s) + Vector3.up * (dir * height(s)) + Vector3.back * rake(s);
            CreatureMeshParts.Fin(ctx.B, slot, fin, skin);
        }

        /// Pair of fins; base/edge given for the right side (+X), the left one is mirrored onto boneL.
        public static void PairedFin(CreatureMeshContext ctx, Func<float, Vector3> baseR, Func<float, Vector3> edgeR, int boneR, int boneL,
                                     int cols, int rows, int band, float thickness, Func<float, float, Vector3> bendR = null, int slot = CreatureMeshSlot.Fin,
                                     Func<float, float, Vector2> uv = null)
        {
            var r = new CreatureMeshFin { Base = baseR, Edge = edgeR, Bend = bendR, Cols = cols, Rows = rows, Thickness = thickness, Band = band, Uv = uv };
            CreatureMeshParts.Fin(ctx.B, slot, r, CreatureMeshSkin.Fixed(boneR));
            var l = new CreatureMeshFin
            {
                Base = s => CreatureMeshMath.MirrorX(baseR(s)),
                Edge = s => CreatureMeshMath.MirrorX(edgeR(s)),
                Bend = bendR != null ? (Func<float, float, Vector3>)((s, q) => CreatureMeshMath.MirrorX(bendR(s, q))) : null,
                Cols = cols, Rows = rows, Thickness = thickness, Band = band, Uv = uv,
            };
            CreatureMeshParts.Fin(ctx.B, slot, l, CreatureMeshSkin.Fixed(boneL));
        }

        /// Pair of flapping fins whose roots stay glued to the body: weights blend from attach(base) at the root
        /// to the fin bone at the edge (used for long-based fins that the animator rotates).
        public static void PairedFinBlend(CreatureMeshContext ctx, Func<float, Vector3> baseR, Func<float, Vector3> edgeR, int boneR, int boneL,
                                          Func<Vector3, BoneWeight> attach, int cols, int rows, int band, float thickness, float blendEnd = 0.35f,
                                          Func<float, float, Vector3> bendR = null)
        {
            for (int side = 1; side >= -1; side -= 2)
            {
                bool right = side > 0;
                int bone = right ? boneR : boneL;
                var fin = new CreatureMeshFin
                {
                    Base = right ? baseR : (s => CreatureMeshMath.MirrorX(baseR(s))),
                    Edge = right ? edgeR : (s => CreatureMeshMath.MirrorX(edgeR(s))),
                    Bend = bendR == null ? null : (right ? bendR : (Func<float, float, Vector3>)((s, q) => CreatureMeshMath.MirrorX(bendR(s, q)))),
                    Cols = cols, Rows = rows, Thickness = thickness, Band = band,
                    SkinSR = (s, r, bp) => CreatureMeshSkin.Blend(attach(bp), CreatureMeshSkin.One(bone), CreatureMeshMath.Smooth(0f, blendEnd, r)),
                };
                CreatureMeshParts.Fin(ctx.B, CreatureMeshSlot.Fin, fin, null);
            }
        }

        /// Caudal fin from a vertical base at the peduncle: len(s) backwards, lift(s) vertical offset, s = 0 bottom .. 1 top.
        public static void CaudalFin(CreatureMeshContext ctx, Vector3 peduncle, float baseHalf, Func<float, float> len, Func<float, float> lift,
                                     int bone, int cols, float thickness, float cup = 0.0f, int rows = 1)
        {
            var fin = new CreatureMeshFin
            {
                Base = s => peduncle + Vector3.up * CreatureMeshMath.Lerp(-baseHalf, baseHalf, s) + Vector3.forward * 0.01f,
                Normal = s => Vector3.right,
                Cols = cols, Rows = rows, Thickness = thickness, Band = 1,
            };
            fin.Edge = s => fin.Base(s) + Vector3.back * len(s) + Vector3.up * lift(s);
            if (cup != 0f) fin.Bend = (s, r) => Vector3.right * (cup * r * r * CreatureMeshMath.Sin(s * Pi));
            CreatureMeshParts.Fin(ctx.B, CreatureMeshSlot.Fin, fin, CreatureMeshSkin.Fixed(bone));
        }

        /// Flat paddle (flipper) along a path: chord in the plane perpendicular to upHint, thickness along it.
        public static void Paddle(CreatureMeshBuilder b, Vector3[] path, Func<float, float> chord, Func<float, float> thick, Func<float, Vector3> upHint,
                                  Func<float, float, Vector2> uv, CreatureMeshSkinFn skin, int slot, int rings = 5, int sides = 6)
        {
            var sw = CreatureMeshParts.Tube(path, chord, rings, sides, upHint);
            sw.HalfW = chord;
            sw.HalfTop = t => thick(t) * 0.55f;
            sw.HalfBot = t => thick(t) * 0.45f;
            sw.CapStart = false;
            sw.CapEnd = true;
            sw.CapBulgeEnd = 0.25f;
            sw.Uv = uv;
            sw.Build(b, slot, skin);
        }

        /// Spiky outline factor: 1 at the spines, membrane between them (n spines over s = 0..1).
        public static float Spikes(float s, int n, float membrane, float sharp = 10f)
        {
            float c = CreatureMeshMath.Abs(CreatureMeshMath.Cos(Pi * s * n));
            return CreatureMeshMath.Lerp(membrane, 1f, CreatureMeshMath.Pow(c, sharp));
        }

        /// Small closed cartoon mouth at the snout tip of a lofted body (dark "o" slightly below the nose).
        public static void SnoutMouth(CreatureMeshContext ctx, CreatureMeshSweep body, float tMouth, float width, float height, CreatureMeshSkinFn skin)
        {
            Vector3 c = body.Center(tMouth);
            float hb = body.HalfBot(tMouth);
            float hw = body.HalfW(tMouth);
            Vector3 p = c + Vector3.down * (hb * 0.3f) + Vector3.forward * 0.002f;
            CreatureMeshParts.Mouth(ctx.B, p, Vector3.forward, Math.Max(0.003f, hw * width), Math.Max(0.002f, hb * height), Math.Max(0.004f, hw * 0.5f), skin);
        }
    }
}
