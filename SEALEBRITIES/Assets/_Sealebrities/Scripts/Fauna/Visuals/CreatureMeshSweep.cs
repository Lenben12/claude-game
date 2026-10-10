using System;
using UnityEngine;

namespace Sealebrities
{
    /// Lofted body: superelliptic cross sections swept along a center curve.
    /// Parameter t runs 0..1 along the curve (head to tail). Theta runs around the section:
    /// 0 = belly (-up), pi/2 = right side (+side), pi = back (+up).
    /// With ArcLength = true the ring vertices are spaced by arc length, so v (0..1 around) is
    /// proportional to surface distance - textures stay undistorted on tall or flat bodies.
    internal sealed class CreatureMeshSweep
    {
        public int Rings = 24, Sides = 16;
        public Func<float, Vector3> Center;
        public Func<float, Vector3> UpHint;            // null = +Y
        public Func<float, float> HalfW, HalfTop, HalfBot;
        public Func<float, float> Square;              // superellipse exponent per t (2 = ellipse)
        public Func<float, float, float> Radial;       // radius multiplier (t, theta)
        public Func<float, float, Vector2> Offset;     // extra local (x = side, y = up) offset (t, theta)
        public Func<float, float> FloorY, CeilY;       // clamp of the local up coordinate (NaN = none)
        public Func<float, float, Vector2> Uv;         // override (t, v01) -> uv
        public float U0 = 0f, U1 = 1f, V0 = 0f, V1 = 1f;
        public bool CapStart = true, CapEnd = true;
        public float CapBulgeStart = 0.4f, CapBulgeEnd = 0.4f;
        public bool ArcLength = true;
        public int Spacing = 1;            // 0 uniform, 1 dense at both ends, 2 dense at start, 3 dense at end
        public float SpacingStrength = 1f;
        public bool Inward;                // flip winding (cavities seen from inside)
        public float T0 = 0f, T1 = 1f;     // build only this part of the profile

        public int FirstVertex, VertexCount;

        public float T(int i)
        {
            float f = Rings > 1 ? i / (float)(Rings - 1) : 0f;
            float g;
            switch (Spacing)
            {
                case 1: g = 0.5f - 0.5f * CreatureMeshMath.Cos(CreatureMeshMath.Pi * f); break;
                case 2: g = 1f - CreatureMeshMath.Cos(CreatureMeshMath.HalfPi * f); break;
                case 3: g = CreatureMeshMath.Sin(CreatureMeshMath.HalfPi * f); break;
                default: g = f; break;
            }
            float u = CreatureMeshMath.Lerp(f, g, SpacingStrength);
            return CreatureMeshMath.Lerp(T0, T1, u);
        }

        public void Frame(float t, out Vector3 c, out Vector3 side, out Vector3 up, out Vector3 back)
        {
            c = Center(t);
            const float e = 0.0015f;
            float ta = t - e, tb = t + e;
            if (ta < 0f) ta = 0f;
            if (tb > 1f) tb = 1f;
            back = Center(tb) - Center(ta);
            if (back.sqrMagnitude < 1e-14f)
            {
                back = Center(Math.Min(1f, t + 0.02f)) - Center(Math.Max(0f, t - 0.02f));
                if (back.sqrMagnitude < 1e-14f) back = Vector3.back;
            }
            back = CreatureMeshMath.Norm(back);
            Vector3 uh = UpHint != null ? UpHint(t) : Vector3.up;
            side = Vector3.Cross(back, uh);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.Cross(back, Vector3.forward);
            if (side.sqrMagnitude < 1e-8f) side = Vector3.right;
            side = CreatureMeshMath.Norm(side);
            up = Vector3.Cross(side, back);
        }

        /// Local section offset (x along side, y along up) at (t, theta).
        public Vector2 Local(float t, float theta)
        {
            float sx = CreatureMeshMath.Sin(theta), sy = -CreatureMeshMath.Cos(theta);
            float hw = HalfW(t);
            float hy = sy >= 0f ? HalfTop(t) : HalfBot(t);
            float n = Square != null ? Square(t) : 2f;
            float x, y;
            if (n > 1.999f && n < 2.001f) { x = sx * hw; y = sy * hy; }
            else
            {
                float ex = 2f / Math.Max(0.3f, n);
                x = CreatureMeshMath.Sign(sx) * CreatureMeshMath.Pow(CreatureMeshMath.Abs(sx), ex) * hw;
                y = CreatureMeshMath.Sign(sy) * CreatureMeshMath.Pow(CreatureMeshMath.Abs(sy), ex) * hy;
            }
            if (Radial != null) { float r = Radial(t, theta); x *= r; y *= r; }
            if (Offset != null) { Vector2 o = Offset(t, theta); x += o.x; y += o.y; }
            if (FloorY != null) { float f = FloorY(t); if (!float.IsNaN(f) && y < f) y = f; }
            if (CeilY != null) { float f = CeilY(t); if (!float.IsNaN(f) && y > f) y = f; }
            return new Vector2(x, y);
        }

        public Vector3 Point(float t, float theta)
        {
            Frame(t, out Vector3 c, out Vector3 side, out Vector3 up, out _);
            Vector2 l = Local(t, theta);
            return c + side * l.x + up * l.y;
        }

        /// Outward surface normal at (t, theta).
        public Vector3 Normal(float t, float theta)
        {
            const float dt = 0.004f, da = 0.02f;
            float ta = Math.Max(0f, t - dt), tb = Math.Min(1f, t + dt);
            Vector3 pt = Point(tb, theta) - Point(ta, theta);
            Vector3 pa = Point(t, theta + da) - Point(t, theta - da);
            Vector3 n = Vector3.Cross(pt, pa);
            if (n.sqrMagnitude < 1e-14f)
            {
                Frame(t, out _, out Vector3 side, out Vector3 up, out _);
                n = side * CreatureMeshMath.Sin(theta) - up * CreatureMeshMath.Cos(theta);
            }
            Vector3 c = Center(t);
            Vector3 p = Point(t, theta);
            if (Vector3.Dot(n, p - c) < 0f) n = -n;
            return CreatureMeshMath.Norm(n);
        }

        /// Ring perimeter at t.
        public float Perimeter(float t)
        {
            const int m = 48;
            float sum = 0f;
            Vector2 prev = Local(t, 0f);
            for (int k = 1; k <= m; k++)
            {
                Vector2 p = Local(t, CreatureMeshMath.TwoPi * k / m);
                sum += (p - prev).magnitude;
                prev = p;
            }
            return sum;
        }

        void Thetas(float t, float[] thetas, float[] cum, Vector2[] tmp)
        {
            int s = thetas.Length - 1;
            if (!ArcLength)
            {
                for (int j = 0; j <= s; j++) thetas[j] = CreatureMeshMath.TwoPi * j / s;
                return;
            }
            int m = cum.Length - 1;
            for (int k = 0; k <= m; k++) tmp[k] = Local(t, CreatureMeshMath.TwoPi * k / m);
            cum[0] = 0f;
            for (int k = 1; k <= m; k++) cum[k] = cum[k - 1] + (tmp[k] - tmp[k - 1]).magnitude;
            float total = cum[m];
            if (total < 1e-7f)
            {
                for (int j = 0; j <= s; j++) thetas[j] = CreatureMeshMath.TwoPi * j / s;
                return;
            }
            int kk = 0;
            for (int j = 0; j <= s; j++)
            {
                float target = total * j / s;
                while (kk < m - 1 && cum[kk + 1] < target) kk++;
                float seg = cum[kk + 1] - cum[kk];
                float f = seg > 1e-9f ? CreatureMeshMath.Clamp01((target - cum[kk]) / seg) : 0f;
                thetas[j] = CreatureMeshMath.TwoPi * (kk + f) / m;
            }
            thetas[0] = 0f;
            thetas[s] = CreatureMeshMath.TwoPi;
        }

        /// Builds the sweep into b. Returns the first vertex index.
        public int Build(CreatureMeshBuilder b, int slot, CreatureMeshSkinFn skin)
        {
            int R = Math.Max(2, Rings), S = Math.Max(3, Sides);
            FirstVertex = b.Count;
            var ring = new int[R];
            var centers = new Vector3[R];
            var backs = new Vector3[R];
            var ts = new float[R];
            var thetas = new float[S + 1];
            var cum = new float[97];
            var tmp = new Vector2[97];
            for (int i = 0; i < R; i++)
            {
                float t = T(i);
                ts[i] = t;
                Frame(t, out Vector3 c, out Vector3 side, out Vector3 up, out Vector3 back);
                centers[i] = c;
                backs[i] = back;
                Thetas(t, thetas, cum, tmp);
                ring[i] = b.Count;
                for (int j = 0; j <= S; j++)
                {
                    Vector2 l = Local(t, thetas[j]);
                    Vector3 p = c + side * l.x + up * l.y;
                    float v01 = j / (float)S;
                    Vector2 uv = Uv != null ? Uv(t, v01) : new Vector2(CreatureMeshMath.Lerp(U0, U1, t), CreatureMeshMath.Lerp(V0, V1, v01));
                    b.Add(p, uv, skin != null ? skin(p, c, t) : CreatureMeshSkin.One(0));
                }
            }

            // winding: test quads around the middle ring until one is not degenerate
            bool flip = false;
            bool found = false;
            for (int pass = 0; pass < R - 1 && !found; pass++)
            {
                int i = (R / 2 + pass) % (R - 1);
                for (int jj = 0; jj < S && !found; jj++)
                {
                    int j = (S / 4 + jj) % S;
                    int a = ring[i] + j, bb = ring[i + 1] + j, cc = ring[i + 1] + j + 1;
                    Vector3 n = b.Cross(a, bb, cc);
                    Vector3 o = b.Verts[a] - centers[i];
                    if (n.sqrMagnitude < 1e-16f || o.sqrMagnitude < 1e-12f) continue;
                    flip = Vector3.Dot(n, o) < 0f;
                    found = true;
                }
            }
            if (Inward) flip = !flip;

            for (int i = 0; i < R - 1; i++)
                for (int j = 0; j < S; j++)
                    b.Quad(slot, ring[i] + j, ring[i + 1] + j, ring[i + 1] + j + 1, ring[i] + j + 1, flip);

            if (CapStart) Cap(b, slot, skin, ring[0], S, centers[0], -backs[0], ts[0], CapBulgeStart);
            if (CapEnd) Cap(b, slot, skin, ring[R - 1], S, centers[R - 1], backs[R - 1], ts[R - 1], CapBulgeEnd);

            VertexCount = b.Count - FirstVertex;
            return FirstVertex;
        }

        void Cap(CreatureMeshBuilder b, int slot, CreatureMeshSkinFn skin, int ringStart, int S, Vector3 c, Vector3 outward, float t, float bulge)
        {
            float radius = 0f;
            for (int j = 0; j < S; j++) radius = Math.Max(radius, (b.Verts[ringStart + j] - c).magnitude);
            if (radius < 1e-5f) return;
            // center of the ring (sections may be offset by clamps)
            Vector3 mid = Vector3.zero;
            for (int j = 0; j < S; j++) mid += b.Verts[ringStart + j];
            mid /= S;
            Vector3 tip = mid + outward * (radius * bulge);
            Vector2 uv = Uv != null ? Uv(t, 0.5f) : new Vector2(CreatureMeshMath.Lerp(U0, U1, t), CreatureMeshMath.Lerp(V0, V1, 0.5f));
            int ti = b.Add(tip, uv, skin != null ? skin(tip, c, t) : CreatureMeshSkin.One(0));
            bool flip = false;
            for (int j = 0; j < S; j++)
            {
                Vector3 n = b.Cross(ti, ringStart + j, ringStart + j + 1);
                if (n.sqrMagnitude < 1e-16f) continue;
                flip = Vector3.Dot(n, outward) < 0f;
                break;
            }
            if (Inward) flip = !flip;
            for (int j = 0; j < S; j++)
            {
                if (!flip) b.Tri(slot, ti, ringStart + j, ringStart + j + 1);
                else b.Tri(slot, ti, ringStart + j + 1, ringStart + j);
            }
        }
    }
}
