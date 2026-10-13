using System;
using UnityEngine;

namespace Sealebrities
{
    /// Sea turtle: domed carapace made of separate faceted shell plates (low pyramids: vertebral row, costal rows,
    /// marginal ring) over a dark seam dome, flat plastron, beaked head on a neck, four paddle flippers.
    /// Skin texture: u < 0.45 head/neck/tail (loft), u >= 0.5 flippers (painted per facet). Shell slot: palette.
    internal static class CreatureMeshTurtle
    {
        const float Pi = CreatureMeshMath.Pi;

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var b = ctx.B;
            ctx.EyeStyle = CreatureMeshEyeStyle.Turtle;
            ctx.Facet(true, false);
            float shellLen = 0.64f;
            float zc = -0.03f;
            float zFront = zc + shellLen * 0.5f, zBack = zc - shellLen * 0.5f;
            float shellW = CreatureMeshMath.Clamp(s.bodyWidth, 0.5f, 0.95f) * shellLen * 1.05f;
            float hTop = CreatureMeshMath.Clamp(s.bodyHeight, 0.15f, 0.5f) * shellLen * 0.62f;
            float hBot = hTop * 0.32f;
            float halfW = shellW * 0.5f;
            const float rimH = 0.008f;

            // outline: z of the front and back edge at |x|
            Func<float, float> zF = x =>
            {
                float f = CreatureMeshMath.Clamp01(x / halfW);
                float z = zc + shellLen * 0.5f * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(f, 2.2f)), 0.45f);
                return z - 0.025f * CreatureMeshMath.Gauss(x, 0f, 0.06f);                     // neck notch
            };
            Func<float, float> zR = x =>
            {
                float f = CreatureMeshMath.Clamp01(x / halfW);
                return zc - shellLen * 0.5f * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(f, 1.7f)), 0.62f);
            };
            // dome height over a point (x, z)
            Func<float, float, float> domeY = (x, z) =>
            {
                float nx = x / halfW, nz = (z - zc) / (shellLen * 0.5f);
                float r = Math.Min(1f, CreatureMeshMath.Sqrt(nx * nx + nz * nz));
                float dome = CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(r, 2.1f)), 0.55f);
                float keel = 0.06f * hTop * CreatureMeshMath.Gauss(x, 0f, 0.05f) * dome;
                return rimH + hTop * dome + keel - 0.12f * hTop * CreatureMeshMath.Pow(Math.Max(0f, nz), 2f) * dome;
            };

            int bodyBone = ctx.Bone("Body", -1, new Vector3(0f, 0f, zc));
            int neck = ctx.Bone("Head", bodyBone, new Vector3(0f, 0.01f, zFront - 0.06f));
            int tailB = ctx.Bone("Tail", bodyBone, new Vector3(0f, -0.01f, zBack + 0.03f));
            ctx.Spine.Add(neck); ctx.Spine.Add(bodyBone); ctx.Spine.Add(tailB);
            var bw = CreatureMeshSkin.One(bodyBone);

            BuildShell(b, halfW, zc, rimH, hBot, zF, zR, domeY, bw);

            // ---- head + neck (skin u 0..0.45)
            var headPath = new[]
            {
                new Vector3(0f, 0.0f, zFront - 0.1f),
                new Vector3(0f, 0.014f, zFront + 0.01f),
                new Vector3(0f, 0.026f, zFront + 0.09f),
                new Vector3(0f, 0.022f, 0.5f),
            };
            var hp = new CreatureMeshCurve(0f, 0.8f, 0.35f, 0.76f, 0.55f, 0.97f, 0.72f, 1f, 0.88f, 0.86f, 1f, 0.5f);
            var head = new CreatureMeshSweep
            {
                Ts = new[] { 0f, 0.22f, 0.42f, 0.58f, 0.72f, 0.84f, 0.93f, 1f }, Sides = 8,
                Center = t => CreatureMeshMath.PathPoint(headPath, t),
                HalfW = t => 0.066f * hp[t] * CreatureMeshMath.Nose(1f - t, 0.14f),
                HalfTop = t => 0.06f * hp[t] * CreatureMeshMath.Nose(1f - t, 0.14f),
                HalfBot = t => 0.05f * hp[t] * CreatureMeshMath.Nose(1f - t, 0.14f),
                CapStart = true, CapEnd = false,
                Uv = (t, vv) => new Vector2(0.45f * (1f - t), vv),
            };
            head.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.Blend(CreatureMeshSkin.One(bodyBone), CreatureMeshSkin.One(neck), CreatureMeshMath.Smooth(0.1f, 0.35f, t)));
            ctx.EyeU = 0.45f * 0.22f;
            // beak: darker horny blob over the snout front
            {
                Vector3 tip = head.Center(0.95f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, tip + new Vector3(0f, -0.008f, -0.008f), Vector3.forward, Vector3.up, 0.034f, 0.025f, 0.034f, 4, 6,
                    new Vector2(0.004f, 0.5f), new Vector2(0.004f, 0.5f), CreatureMeshSkin.Fixed(neck));
            }
            // big friendly eyes with lid bulges
            {
                float et = 0.72f;
                Vector3 p = head.Point(et, Pi * 0.5f + 0.38f);
                Vector3 n = head.Normal(et, Pi * 0.5f + 0.38f);
                Vector3 c = p - n * 0.006f;
                Vector3 look = CreatureMeshMath.Norm(n + Vector3.forward * 0.4f);
                for (int side = 1; side >= -1; side -= 2)
                {
                    Vector3 cc = side > 0 ? c : CreatureMeshMath.MirrorX(c);
                    Vector3 lk = side > 0 ? look : CreatureMeshMath.MirrorX(look);
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, cc - lk * 0.006f + Vector3.up * 0.004f, lk, Vector3.up, 0.028f, 0.024f, 0.016f, 4, 6,
                        new Vector2(0.1f, 0.6f), new Vector2(0.1f, 0.6f), CreatureMeshSkin.Fixed(neck));
                    CreatureMeshParts.Eye(ctx, cc + lk * 0.006f, lk, 0.019f, CreatureMeshSkin.Fixed(neck));
                }
            }

            // ---- tail stub
            {
                var path = new[] { new Vector3(0f, -0.01f, zBack + 0.06f), new Vector3(0f, -0.018f, zBack - 0.03f), new Vector3(0f, -0.03f, zBack - 0.08f) };
                var tail = CreatureMeshParts.Tube(path, t => 0.026f * (1f - t) + 0.002f, 3, 5, t => Vector3.up);
                tail.CapStart = false;
                tail.Uv = (t, vv) => new Vector2(0.45f, vv);
                tail.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(tailB));
            }

            // ---- flippers (skin u 0.5..1)
            for (int side = 1; side >= -1; side -= 2)
            {
                float sd = side;
                // front: long paddle, swept back
                Vector3 root = new Vector3(sd * halfW * 0.62f, -0.012f, zFront - 0.07f);
                var fp = new[]
                {
                    root,
                    root + new Vector3(sd * 0.12f, -0.012f, -0.02f),
                    root + new Vector3(sd * 0.26f, -0.02f, -0.1f),
                    root + new Vector3(sd * 0.36f, -0.028f, -0.24f),
                };
                int fb = ctx.Bone(side > 0 ? "FlipperFrontR" : "FlipperFrontL", bodyBone, root);
                Flipper(b, fp, t => CreatureMeshMath.Lerp(0.03f, 0.075f, CreatureMeshMath.Sin(Pi * Math.Min(1f, t * 1.25f))) * CreatureMeshMath.Nose(1f - t, 0.22f) + 0.004f,
                    t => CreatureMeshMath.Lerp(0.015f, 0.004f, t), fb, 0.5f, 0.75f, 6);
                // rear: short rounded paddle
                Vector3 rroot = new Vector3(sd * halfW * 0.5f, -0.018f, zBack + 0.1f);
                var rp = new[]
                {
                    rroot,
                    rroot + new Vector3(sd * 0.06f, -0.006f, -0.05f),
                    rroot + new Vector3(sd * 0.11f, -0.01f, -0.12f),
                };
                int rb = ctx.Bone(side > 0 ? "FlipperRearR" : "FlipperRearL", bodyBone, rroot);
                Flipper(b, rp, t => CreatureMeshMath.Lerp(0.025f, 0.055f, CreatureMeshMath.Sin(Pi * Math.Min(1f, t * 1.4f))) * CreatureMeshMath.Nose(1f - t, 0.35f) + 0.004f,
                    t => CreatureMeshMath.Lerp(0.012f, 0.004f, t), rb, 0.78f, 1f, 4);
                ctx.Flippers.Add(fb);
                ctx.Flippers.Add(rb);
            }
            // order: FR, RR, FL, RL -> reorder to FR, FL, RR, RL for readability
            ctx.Flippers.Sort((x, y) => string.CompareOrdinal(ctx.Bones[x].Name, ctx.Bones[y].Name));
        }

        // ------------------------------------------------------------------------------------------ shell

        /// Dark seam dome + individual plates: 5 vertebral, 4 costal per side, 22 marginals, plus plastron and rim band.
        static void BuildShell(CreatureMeshBuilder b, float halfW, float zc, float rimH, float hBot,
                               Func<float, float> zF, Func<float, float> zR, Func<float, float, float> domeY, BoneWeight w)
        {
            const float inner = 0.84f;   // marginal ring starts here (fraction of the outline)
            Vector2 seamUv = CreatureMeshPaint.ShellUv(0);

            // outline point (u 0..0.5 front edge from -x to +x, 0.5..1 back edge from +x to -x) scaled toward the center
            Func<float, float, Vector3> outline = (u, k) =>
            {
                float x, z;
                if (u < 0.5f) { x = halfW * CreatureMeshMath.Sin(Pi * (u / 0.5f - 0.5f)); z = zF(CreatureMeshMath.Abs(x)); }
                else { x = -halfW * CreatureMeshMath.Sin(Pi * ((u - 0.5f) / 0.5f - 0.5f)); z = zR(CreatureMeshMath.Abs(x)); }
                return new Vector3(x * k, 0f, zc + (z - zc) * k);
            };
            Func<Vector3, Vector3> onDome = p => new Vector3(p.x, domeY(p.x, p.z), p.z);

            // ---- seam dome (slightly lower, shows between the plates)
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Shell, 10, 8,
                (u, v) =>
                {
                    float x = halfW * CreatureMeshMath.Sin(Pi * (u - 0.5f)), ax = CreatureMeshMath.Abs(x);
                    float z = CreatureMeshMath.Lerp(zF(ax), zR(ax), 0.5f - 0.5f * CreatureMeshMath.Cos(Pi * v));
                    return new Vector3(x * 0.995f, domeY(x, z) * 0.97f - 0.004f, zc + (z - zc) * 0.995f);
                },
                (u, v) => seamUv, p => Vector3.up, (p, u, v) => w);

            // ---- inner plates on a (column, row) grid inside the marginal ring
            // columns over normalized x: costal L | vertebral | costal R
            float[] colX = { -1f, -0.34f, 0.34f, 1f };
            float[] vertRows = { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
            float[] costRows = { 0f, 0.27f, 0.5f, 0.73f, 1f };
            Func<float, float, Vector3> grid = (xn, v) =>
            {
                float x = xn * halfW * inner, ax = CreatureMeshMath.Abs(x) / inner;
                float z = zc + (CreatureMeshMath.Lerp(zF(ax), zR(ax), v) - zc) * inner;
                return onDome(new Vector3(x, 0f, z));
            };
            for (int c = 0; c < 3; c++)
            {
                var rows = c == 1 ? vertRows : costRows;
                int nx = c == 1 ? 2 : 4;   // costal plates follow the curved rows toward the side point
                for (int r = 0; r + 1 < rows.Length; r++)
                {
                    var corners = PlateOutline(grid, colX[c], colX[c + 1], rows[r], rows[r + 1], nx, 2);
                    int entry = c == 1 ? (1 + (r & 1)) : (3 + ((r + c) & 1));
                    AddPlate(b, corners, onDome, c == 1 ? 0.016f : 0.012f, CreatureMeshPaint.ShellUv(entry), w);
                }
            }

            // ---- marginal ring: small plates between the inner outline and the rim
            const int M = 22;
            for (int k = 0; k < M; k++)
            {
                float u0 = (k + 0.5f) / M, u1 = (k + 1.5f) / M;   // half-step offset puts a seam on the midline at the back
                if (u1 > 1f) u1 -= 1f;
                Vector3 i0 = onDome(outline(u0, inner)), i1 = onDome(outline(u1, inner));
                Vector3 o0 = outline(u0, 1f), o1 = outline(u1, 1f);
                o0.y = rimH * 1.4f; o1.y = rimH * 1.4f;
                AddPlate(b, new[] { i0, o0, o1, i1 }, null, 0.006f, CreatureMeshPaint.ShellUv(5 + (k & 1)), w);
            }

            // ---- plastron (flat pale belly plate) and rim band joining it to the carapace edge
            Vector2 plUv = CreatureMeshPaint.ShellUv(7);
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Shell, 6, 5,
                (u, v) =>
                {
                    float x = halfW * CreatureMeshMath.Sin(Pi * (u - 0.5f)), ax = CreatureMeshMath.Abs(x);
                    float z = CreatureMeshMath.Lerp(zF(ax), zR(ax), 0.5f - 0.5f * CreatureMeshMath.Cos(Pi * v));
                    float nx = x / halfW, nz = (z - zc) / ((zF(0f) - zR(0f)) * 0.5f);
                    float r = Math.Min(1f, CreatureMeshMath.Sqrt(nx * nx + nz * nz));
                    return new Vector3(x, -rimH - hBot * CreatureMeshMath.Pow(Math.Max(0f, 1f - CreatureMeshMath.Pow(r, 2.6f)), 0.4f), z);
                },
                (u, v) => plUv, p => Vector3.down, (p, u, v) => w);
            Vector2 rimUv = CreatureMeshPaint.ShellUv(5);
            CreatureMeshParts.Surface(b, CreatureMeshSlot.Shell, 22, 1,
                (u, v) =>
                {
                    Vector3 p = outline(u, 1f);
                    p.y = v < 0.5f ? -rimH : rimH * 1.4f;
                    return p;
                },
                (u, v) => rimUv, p => new Vector3(p.x, 0f, p.z - zc), (p, u, v) => w);
        }

        /// Boundary of a grid cell [x0, x1] x [v0, v1] walked around once (nx / nv segments per edge).
        static Vector3[] PlateOutline(Func<float, float, Vector3> grid, float x0, float x1, float v0, float v1, int nx, int nv)
        {
            var pts = new Vector3[2 * (nx + nv)];
            int k = 0;
            for (int i = 0; i < nx; i++) pts[k++] = grid(CreatureMeshMath.Lerp(x0, x1, i / (float)nx), v0);
            for (int i = 0; i < nv; i++) pts[k++] = grid(x1, CreatureMeshMath.Lerp(v0, v1, i / (float)nv));
            for (int i = 0; i < nx; i++) pts[k++] = grid(CreatureMeshMath.Lerp(x1, x0, i / (float)nx), v1);
            for (int i = 0; i < nv; i++) pts[k++] = grid(x0, CreatureMeshMath.Lerp(v1, v0, i / (float)nv));
            return pts;
        }

        /// One shell plate: corners shrunk toward the plate center (dark seams show between plates), lifted off the
        /// dome, with a raised apex so the plate reads as a faceted pyramid.
        static void AddPlate(CreatureMeshBuilder b, Vector3[] corners, Func<Vector3, Vector3> onDome, float height, Vector2 uv, BoneWeight w)
        {
            Vector3 mid = Vector3.zero;
            for (int i = 0; i < corners.Length; i++) mid += corners[i];
            mid /= corners.Length;
            var cs = new Vector3[corners.Length];
            for (int i = 0; i < corners.Length; i++) cs[i] = Vector3.Lerp(corners[i], mid, 0.07f) + Vector3.up * 0.004f;
            Vector3 top = onDome != null ? onDome(mid) : mid;
            Vector3 apex = top + Vector3.up * (height + 0.004f);
            CreatureMeshParts.Plate(b, CreatureMeshSlot.Shell, cs, apex, Vector3.up, uv, w);
        }

        static void Flipper(CreatureMeshBuilder b, Vector3[] path, Func<float, float> chord, Func<float, float> thick, int bone, float u0, float u1, int rings)
        {
            var sw = CreatureMeshParts.Tube(path, chord, rings, 6, t => Vector3.up);
            sw.HalfW = chord;
            sw.HalfTop = t => thick(t) * 0.55f;
            sw.HalfBot = t => thick(t) * 0.45f;
            sw.CapStart = false;
            sw.CapEnd = true;
            sw.Uv = (t, vv) => new Vector2(CreatureMeshMath.Lerp(u0, u1, t), vv);
            sw.Build(b, CreatureMeshSlot.Skin, (p, a, t) => CreatureMeshSkin.One(bone));
        }
    }
}
