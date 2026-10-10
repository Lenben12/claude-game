using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// A thin double-faced fin: columns run along s (base line), rows from the base (r = 0) to the edge (r = 1).
    /// Low-poly fins use very few columns (the outline becomes a handful of flat triangles) and one row.
    internal sealed class CreatureMeshFin
    {
        public Func<float, Vector3> Base;        // s -> point on the body
        public Func<float, Vector3> Edge;        // s -> outer edge point
        public Func<float, Vector3> Normal;      // optional fin plane normal (otherwise derived)
        public Func<float, float, Vector3> Bend; // optional extra offset (s, r): cupping, ripples
        public int Cols = 4, Rows = 1;
        public float Thickness = 0.008f;         // at the base, tapers to 0 at the edge
        public int Band;                         // fin texture band 0..3
        public Func<float, float, Vector2> Uv;   // optional (s, r) -> uv override
        public float RowPower = 1f;
        /// Optional weights by (s, r, base point); overrides the skin callback (e.g. body at the root, fin bone at the edge).
        public Func<float, float, Vector3, BoneWeight> SkinSR;
    }

    internal static class CreatureMeshParts
    {
        const float Pi = CreatureMeshMath.Pi;

        /// Fin texture uv for a band (4 horizontal bands, u = base->edge).
        public static Vector2 FinUv(int band, float s, float r)
        {
            return new Vector2(0.02f + 0.96f * CreatureMeshMath.Clamp01(r), (band + 0.05f + 0.9f * CreatureMeshMath.Clamp01(s)) * 0.25f);
        }

        // ------------------------------------------------------------------------------------------ ellipsoid

        /// Ellipsoid with radii (side, up, fwd) around center, oriented by fwd/up. t runs from the +fwd pole.
        public static CreatureMeshSweep Ellipsoid(Vector3 center, Vector3 fwd, Vector3 up, float rSide, float rUp, float rFwd, int rings, int sides)
        {
            fwd = CreatureMeshMath.Norm(fwd);
            Vector3 upn = CreatureMeshMath.Norm(up - fwd * Vector3.Dot(up, fwd));
            if (upn.sqrMagnitude < 0.5f) upn = CreatureMeshMath.Perp(fwd);
            return new CreatureMeshSweep
            {
                Rings = rings, Sides = sides,
                Center = t => center + fwd * (rFwd * (1f - 2f * t)),
                UpHint = t => upn,
                HalfW = t => rSide * Prof(t),
                HalfTop = t => rUp * Prof(t),
                HalfBot = t => rUp * Prof(t),
                CapStart = false, CapEnd = false,
                ArcLength = false, Spacing = 1,
            };
        }

        static float Prof(float t) => 2f * CreatureMeshMath.Sqrt(Math.Max(0f, t * (1f - t)));

        public static int Ellipsoid(CreatureMeshBuilder b, int slot, Vector3 center, Vector3 fwd, Vector3 up, float rSide, float rUp, float rFwd,
                                    int rings, int sides, Vector2 uv0, Vector2 uv1, CreatureMeshSkinFn skin)
        {
            var sw = Ellipsoid(center, fwd, up, rSide, rUp, rFwd, rings, sides);
            sw.Uv = (t, v) => new Vector2(CreatureMeshMath.Lerp(uv0.x, uv1.x, t), CreatureMeshMath.Lerp(uv0.y, uv1.y, v));
            return sw.Build(b, slot, skin);
        }

        // ------------------------------------------------------------------------------------------ cartoon eyes

        /// Goggle eye: faceted sclera ball, black pupil lens on its front, white glint (friendly styles).
        /// radius is the anatomical eye size; the eye style scales it (toy eyes for reef fish, tiny ones for the deep).
        public static void Eye(CreatureMeshContext ctx, Vector3 center, Vector3 lookDir, float radius, CreatureMeshSkinFn skin)
        {
            var b = ctx.B;
            var L = CreatureMeshEyes.Look(ctx.EyeStyle);
            float R = radius * L.Scale;
            Vector3 look = CreatureMeshMath.Norm(lookDir);
            if (look.sqrMagnitude < 0.5f) look = Vector3.forward;
            Vector3 up = Vector3.up - look * Vector3.Dot(Vector3.up, look);
            up = up.sqrMagnitude > 1e-4f ? CreatureMeshMath.Norm(up) : CreatureMeshMath.Perp(look);
            Vector3 fwd = Vector3.forward - look * Vector3.Dot(Vector3.forward, look);
            fwd = fwd.sqrMagnitude > 1e-4f ? CreatureMeshMath.Norm(fwd) : Vector3.Cross(look, up);

            const float depth = 0.92f;
            int rings = R > 0.025f ? 6 : 5;
            Ellipsoid(b, CreatureMeshSlot.Eye, center, look, up, R, R, R * depth, rings, 8, Vector2.zero, Vector2.zero, skin);

            float p = L.Pupil;
            float pw = p * L.PupilW, ph = p * L.PupilH;
            float lateral = Math.Max(pw, ph);
            float along = R * depth * CreatureMeshMath.Sqrt(Math.Max(0.05f, 1f - lateral * lateral)) * 0.97f;
            Vector3 pc = center + look * along;
            float pDepth = R * p * 0.42f;
            Ellipsoid(b, CreatureMeshSlot.Pupil, pc, look, up, R * pw, R * ph, pDepth, 4, 6, Vector2.zero, Vector2.zero, skin);

            if (L.Glint > 0f)
            {
                float g = R * L.Glint;
                Vector3 gc = pc + look * (pDepth * 0.8f) + up * (R * ph * 0.42f) + fwd * (R * pw * 0.3f);
                Ellipsoid(b, CreatureMeshSlot.Teeth, gc, look, up, g, g, g * 0.6f, 3, 4, Vector2.zero, Vector2.zero, skin);
            }
        }

        /// Places a pair of eyes on a sweep surface at (t, thetaRight) and the mirrored side. embed: how deep the
        /// eye ball sinks into the head (fraction of its radius; ~0.35 = goggle, ~0.6 = flush).
        public static void EyePair(CreatureMeshContext ctx, CreatureMeshSweep body, float t, float theta, float radius, float forwardLook, float embed, CreatureMeshSkinFn skin)
        {
            var L = CreatureMeshEyes.Look(ctx.EyeStyle);
            Vector3 p = body.Point(t, theta);
            Vector3 n = body.Normal(t, theta);
            Vector3 look = CreatureMeshMath.Norm(n + Vector3.forward * forwardLook);
            Vector3 c = p - n * (radius * L.Scale * embed);
            Eye(ctx, c, look, radius, skin);
            Eye(ctx, CreatureMeshMath.MirrorX(c), CreatureMeshMath.MirrorX(look), radius, skin);
        }

        /// Small dark mouth blob (cartoon "o" / smile) at a point, facing fwd.
        public static void Mouth(CreatureMeshBuilder b, Vector3 center, Vector3 fwd, float halfW, float halfH, float depth, CreatureMeshSkinFn skin)
        {
            Ellipsoid(b, CreatureMeshSlot.Mouth, center, fwd, Vector3.up, halfW, halfH, depth, 4, 6, Vector2.zero, Vector2.zero, skin);
        }

        // ------------------------------------------------------------------------------------------ cone / tube

        public static void Cone(CreatureMeshBuilder b, int slot, Vector3 basePt, Vector3 tipPt, float baseRadius, int sides, Vector2 uvBase, Vector2 uvTip, BoneWeight w)
        {
            Vector3 axis = tipPt - basePt;
            if (axis.sqrMagnitude < 1e-12f) return;
            Vector3 d = CreatureMeshMath.Norm(axis);
            Vector3 x = CreatureMeshMath.Perp(d);
            Vector3 y = Vector3.Cross(d, x);
            int start = b.Count;
            for (int j = 0; j < sides; j++)
            {
                float a = CreatureMeshMath.TwoPi * j / sides;
                b.Add(basePt + (x * CreatureMeshMath.Cos(a) + y * CreatureMeshMath.Sin(a)) * baseRadius, uvBase, w);
            }
            int tip = b.Add(tipPt, uvTip, w);
            Vector3 n0 = b.Cross(start, start + 1, tip);
            Vector3 out0 = (b.Verts[start] + b.Verts[start + 1]) * 0.5f - basePt;
            bool flip = Vector3.Dot(n0, out0) < 0f;
            for (int j = 0; j < sides; j++)
            {
                int a = start + j, c = start + (j + 1) % sides;
                if (!flip) b.Tri(slot, a, c, tip); else b.Tri(slot, a, tip, c);
            }
        }

        /// Tube along a spline through path. radius(t), t in 0..1 along the path.
        public static CreatureMeshSweep Tube(IList<Vector3> path, Func<float, float> radius, int rings, int sides, Func<float, Vector3> upHint)
        {
            var pts = new List<Vector3>(path);
            return new CreatureMeshSweep
            {
                Rings = rings, Sides = sides,
                Center = t => CreatureMeshMath.PathPoint(pts, t),
                UpHint = upHint,
                HalfW = radius, HalfTop = radius, HalfBot = radius,
                ArcLength = false, Spacing = 0,
                CapStart = true, CapEnd = true,
            };
        }

        // ------------------------------------------------------------------------------------------ fins

        public static void Fin(CreatureMeshBuilder b, int slot, CreatureMeshFin fin, CreatureMeshSkinFn skin)
        {
            int cols = Math.Max(1, fin.Cols), rows = Math.Max(1, fin.Rows);
            var normals = new Vector3[cols + 1];
            Vector3 nref = FinNormal(fin, 0.5f);
            for (int i = 0; i <= cols; i++)
            {
                Vector3 n = FinNormal(fin, i / (float)cols);
                if (Vector3.Dot(n, nref) < 0f) n = -n;
                normals[i] = n;
            }
            for (int face = 0; face < 2; face++)
            {
                float sgn = face == 0 ? 1f : -1f;
                int start = b.Count;
                for (int i = 0; i <= cols; i++)
                {
                    float s = i / (float)cols;
                    Vector3 bp = fin.Base(s), ep = fin.Edge(s), n = normals[i];
                    for (int j = 0; j <= rows; j++)
                    {
                        float r = CreatureMeshMath.Pow(j / (float)rows, fin.RowPower);
                        if (j == 0) r = 0f;
                        Vector3 p = bp + (ep - bp) * r;
                        if (fin.Bend != null) p += fin.Bend(s, r);
                        p += n * (sgn * 0.5f * fin.Thickness * CreatureMeshMath.Pow(1f - r, 1.3f));
                        Vector2 uv = fin.Uv != null ? fin.Uv(s, r) : FinUv(fin.Band, s, r);
                        BoneWeight w = fin.SkinSR != null ? fin.SkinSR(s, r, bp) : (skin != null ? skin(p, bp, s) : CreatureMeshSkin.One(0));
                        b.Add(p, uv, w);
                    }
                }
                bool flip = false;
                bool found = false;
                for (int ii = 0; ii < cols && !found; ii++)
                {
                    int i = (cols / 2 + ii) % cols;
                    for (int j = 0; j < rows && !found; j++)
                    {
                        int a = start + i * (rows + 1) + j;
                        int bb = start + (i + 1) * (rows + 1) + j;
                        int c = bb + 1;
                        Vector3 cr = b.Cross(a, bb, c);
                        if (cr.sqrMagnitude < 1e-16f) continue;
                        flip = Vector3.Dot(cr, normals[i] * sgn) < 0f;
                        found = true;
                    }
                }
                for (int i = 0; i < cols; i++)
                    for (int j = 0; j < rows; j++)
                    {
                        int a = start + i * (rows + 1) + j;
                        int bb = start + (i + 1) * (rows + 1) + j;
                        b.Quad(slot, a, bb, bb + 1, a + 1, flip);
                    }
            }
        }

        static Vector3 FinNormal(CreatureMeshFin fin, float s)
        {
            if (fin.Normal != null) return CreatureMeshMath.Norm(fin.Normal(s));
            const float e = 0.01f;
            float sa = Math.Max(0f, s - e), sb = Math.Min(1f, s + e);
            Vector3 db = fin.Base(sb) - fin.Base(sa);
            Vector3 o = fin.Edge(s) - fin.Base(s);
            Vector3 n = Vector3.Cross(db, o);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(fin.Edge(sb) - fin.Edge(sa), o);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(db, Vector3.up);
            if (n.sqrMagnitude < 1e-12f) n = Vector3.right;
            return CreatureMeshMath.Norm(n);
        }

        // ------------------------------------------------------------------------------------------ grid surface

        /// Generic parametric patch P(u, v), u in 0..1 (nu cells), v in 0..1 (nv cells).
        /// outward(p) gives the expected front direction (winding is chosen from the middle cell).
        public static int Surface(CreatureMeshBuilder b, int slot, int nu, int nv, Func<float, float, Vector3> pos, Func<float, float, Vector2> uv,
                                  Func<Vector3, Vector3> outward, Func<Vector3, float, float, BoneWeight> skin,
                                  Func<float, float> uMap = null, Func<float, float> vMap = null)
        {
            int start = b.Count;
            for (int i = 0; i <= nu; i++)
            {
                float u = i / (float)nu;
                if (uMap != null) u = uMap(u);
                for (int j = 0; j <= nv; j++)
                {
                    float v = j / (float)nv;
                    if (vMap != null) v = vMap(v);
                    Vector3 p = pos(u, v);
                    b.Add(p, uv(u, v), skin(p, u, v));
                }
            }
            bool flip = false;
            bool found = false;
            for (int ii = 0; ii < nu && !found; ii++)
            {
                int i = (nu / 2 + ii) % nu;
                for (int jj = 0; jj < nv && !found; jj++)
                {
                    int j = (nv / 2 + jj) % nv;
                    int a = start + i * (nv + 1) + j;
                    int bb = start + (i + 1) * (nv + 1) + j;
                    Vector3 cr = b.Cross(a, bb, bb + 1);
                    if (cr.sqrMagnitude < 1e-16f) continue;
                    flip = Vector3.Dot(cr, outward(b.Verts[a])) < 0f;
                    found = true;
                }
            }
            for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    int a = start + i * (nv + 1) + j;
                    int bb = start + (i + 1) * (nv + 1) + j;
                    b.Quad(slot, a, bb, bb + 1, a + 1, flip);
                }
            return start;
        }

        /// Faceted low pyramid over a convex polygon (shell plates): corners + raised apex, one flat color (uv).
        public static void Plate(CreatureMeshBuilder b, int slot, Vector3[] corners, Vector3 apex, Vector3 outward, Vector2 uv, BoneWeight w)
        {
            int n = corners.Length;
            int start = b.Count;
            for (int i = 0; i < n; i++) b.Add(corners[i], uv, w);
            int ai = b.Add(apex, uv, w);
            for (int i = 0; i < n; i++)
            {
                int a = start + i, c = start + (i + 1) % n;
                Vector3 cr = b.Cross(a, c, ai);
                if (Vector3.Dot(cr, outward) >= 0f) b.Tri(slot, a, c, ai); else b.Tri(slot, a, ai, c);
            }
        }

        // ------------------------------------------------------------------------------------------ chains

        /// Turtle-graphics path: starts at p0 heading h0, each step turns toward pull and rotates around curlAxis.
        public static Vector3[] GrowPath(Vector3 p0, Vector3 h0, float length, int steps, Func<int, Vector3> pull, float pullRate,
                                         Vector3 curlAxis, Func<float, float> curlRate)
        {
            var pts = new Vector3[steps + 1];
            pts[0] = p0;
            Vector3 h = CreatureMeshMath.Norm(h0);
            float step = length / steps;
            for (int i = 1; i <= steps; i++)
            {
                float f = i / (float)steps;
                Vector3 target = pull != null ? pull(i) : Vector3.zero;
                if (target.sqrMagnitude > 1e-8f) h = CreatureMeshMath.Norm(Vector3.Lerp(h, CreatureMeshMath.Norm(target), pullRate));
                if (curlRate != null && curlAxis.sqrMagnitude > 1e-8f)
                {
                    float c = curlRate(f);
                    if (c != 0f) h = CreatureMeshMath.Norm(CreatureMeshMath.Rotate(h, CreatureMeshMath.Norm(curlAxis), c));
                }
                pts[i] = pts[i - 1] + h * step;
            }
            return pts;
        }

        /// Creates a bone chain along a path (pivots at the given fractions) under parent and returns its skinning chain.
        public static CreatureMeshChain ChainAlong(CreatureMeshContext ctx, string name, int parent, IList<Vector3> path, float[] fractions)
        {
            var ids = new int[fractions.Length];
            var piv = new Vector3[fractions.Length];
            int p = parent;
            for (int i = 0; i < fractions.Length; i++)
            {
                piv[i] = CreatureMeshMath.PathPoint(path, fractions[i]);
                ids[i] = ctx.Bone(name + i, p, piv[i]);
                p = ids[i];
            }
            return new CreatureMeshChain(ids, piv, CreatureMeshMath.PathPoint(path, 1f));
        }
    }
}
