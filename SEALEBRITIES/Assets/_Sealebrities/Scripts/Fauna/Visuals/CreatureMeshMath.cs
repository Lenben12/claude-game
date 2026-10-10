using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Managed math helpers for the creature mesh factory. Deliberately avoids native Unity calls
    /// (Quaternion.Euler, Mathf.PerlinNoise, Random ...) so the geometry code is deterministic and testable.
    internal static class CreatureMeshMath
    {
        public const float Pi = 3.14159265f;
        public const float TwoPi = Pi * 2f;
        public const float HalfPi = Pi * 0.5f;

        public static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
        public static float Clamp(float x, float a, float b) => x < a ? a : (x > b ? b : x);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;
        public static float InvLerp(float a, float b, float x) => Clamp01((x - a) / (b - a));

        public static float Smooth(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        public static float Gauss(float x, float c, float w)
        {
            float d = (x - c) / w;
            return (float)Math.Exp(-d * d);
        }

        public static float Frac(float x) => x - (float)Math.Floor(x);
        public static float Pow(float x, float p) => x <= 0f ? 0f : (float)Math.Pow(x, p);
        public static float Sin(float x) => (float)Math.Sin(x);
        public static float Cos(float x) => (float)Math.Cos(x);
        public static float Sqrt(float x) => x <= 0f ? 0f : (float)Math.Sqrt(x);
        public static float Abs(float x) => x < 0f ? -x : x;
        public static float Sign(float x) => x < 0f ? -1f : 1f;

        /// Quarter-ellipse rise from 0 (t = 0) to 1 (t >= tn): rounded snouts and caps.
        public static float Nose(float t, float tn)
        {
            if (tn <= 0f || t >= tn) return 1f;
            if (t <= 0f) return 0f;
            float k = 1f - t / tn;
            return Sqrt(1f - k * k);
        }

        /// Rodrigues rotation of v around a normalized axis.
        public static Vector3 Rotate(Vector3 v, Vector3 axis, float radians)
        {
            float c = Cos(radians), s = Sin(radians);
            return v * c + Vector3.Cross(axis, v) * s + axis * (Vector3.Dot(axis, v) * (1f - c));
        }

        public static Vector3 RotateAround(Vector3 p, Vector3 pivot, Vector3 axis, float radians) => pivot + Rotate(p - pivot, axis, radians);

        public static Vector3 MirrorX(Vector3 p) => new Vector3(-p.x, p.y, p.z);

        public static Vector3 Norm(Vector3 v)
        {
            float m = v.magnitude;
            return m > 1e-9f ? v / m : Vector3.zero;
        }

        /// Any unit vector perpendicular to n.
        public static Vector3 Perp(Vector3 n)
        {
            Vector3 a = Abs(n.y) < 0.9f ? Vector3.up : Vector3.right;
            return Norm(Vector3.Cross(n, a));
        }

        public static float Luma(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

        public static Color Mix(Color a, Color b, float t)
        {
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }

        public static Color Mul(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);

        public static Color Saturate(Color c, float k)
        {
            float l = Luma(c);
            return new Color(Clamp01(l + (c.r - l) * k), Clamp01(l + (c.g - l) * k), Clamp01(l + (c.b - l) * k), c.a);
        }

        /// Uniform Catmull-Rom spline through the given points, t in [0, 1].
        public static Vector3 PathPoint(IList<Vector3> pts, float t)
        {
            int n = pts.Count;
            if (n == 0) return Vector3.zero;
            if (n == 1) return pts[0];
            float f = Clamp01(t) * (n - 1);
            int i = (int)Math.Floor(f);
            if (i >= n - 1) i = n - 2;
            float u = f - i;
            Vector3 p0 = pts[i > 0 ? i - 1 : 0], p1 = pts[i], p2 = pts[i + 1], p3 = pts[i + 2 < n ? i + 2 : n - 1];
            if (i == 0) p0 = p1 + (p1 - p2);
            if (i + 2 >= n) p3 = p2 + (p2 - p1);
            float u2 = u * u, u3 = u2 * u;
            return 0.5f * (2f * p1 + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
        }

        /// Resamples a polyline (spline through pts) into count points with roughly equal arc length.
        public static Vector3[] Resample(IList<Vector3> pts, int count)
        {
            const int fine = 160;
            var p = new Vector3[fine + 1];
            var cum = new float[fine + 1];
            for (int i = 0; i <= fine; i++)
            {
                p[i] = PathPoint(pts, i / (float)fine);
                if (i > 0) cum[i] = cum[i - 1] + (p[i] - p[i - 1]).magnitude;
            }
            var res = new Vector3[count];
            float total = cum[fine];
            int k = 0;
            for (int j = 0; j < count; j++)
            {
                float target = total * j / (count - 1);
                while (k < fine - 1 && cum[k + 1] < target) k++;
                float seg = cum[k + 1] - cum[k];
                float f = seg > 1e-9f ? (target - cum[k]) / seg : 0f;
                res[j] = Vector3.LerpUnclamped(p[k], p[k + 1], Clamp01(f));
            }
            return res;
        }

        public static float PathLength(IList<Vector3> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += (pts[i] - pts[i - 1]).magnitude;
            return l;
        }
    }

    /// Monotone cubic (Fritsch-Carlson) curve through (x, y) keys: no overshoot, ideal for body profiles.
    internal sealed class CreatureMeshCurve
    {
        readonly float[] xs, ys, ms;

        /// keys: x0, y0, x1, y1, ... with increasing x.
        public CreatureMeshCurve(params float[] keys)
        {
            int n = Math.Max(1, keys.Length / 2);
            xs = new float[n];
            ys = new float[n];
            ms = new float[n];
            for (int i = 0; i < n && 2 * i + 1 < keys.Length; i++) { xs[i] = keys[2 * i]; ys[i] = keys[2 * i + 1]; }
            if (n < 2) return;
            var d = new float[n - 1];
            for (int i = 0; i < n - 1; i++) d[i] = (ys[i + 1] - ys[i]) / Math.Max(1e-6f, xs[i + 1] - xs[i]);
            ms[0] = d[0];
            ms[n - 1] = d[n - 2];
            for (int i = 1; i < n - 1; i++) ms[i] = d[i - 1] * d[i] <= 0f ? 0f : (d[i - 1] + d[i]) * 0.5f;
            for (int i = 0; i < n - 1; i++)
            {
                if (Math.Abs(d[i]) < 1e-9f) { ms[i] = 0f; ms[i + 1] = 0f; continue; }
                float a = ms[i] / d[i], b = ms[i + 1] / d[i];
                float h = a * a + b * b;
                if (h > 9f)
                {
                    float tau = 3f / (float)Math.Sqrt(h);
                    ms[i] = tau * a * d[i];
                    ms[i + 1] = tau * b * d[i];
                }
            }
        }

        public float this[float x] => Eval(x);

        public float Eval(float x)
        {
            int n = xs.Length;
            if (n == 1 || x <= xs[0]) return ys[0];
            if (x >= xs[n - 1]) return ys[n - 1];
            int i = 0;
            while (i < n - 2 && x > xs[i + 1]) i++;
            float h = xs[i + 1] - xs[i];
            float t = (x - xs[i]) / h;
            float t2 = t * t, t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * ys[i] + (t3 - 2f * t2 + t) * h * ms[i] + (-2f * t3 + 3f * t2) * ys[i + 1] + (t3 - t2) * h * ms[i + 1];
        }

        public float Max
        {
            get { float m = ys[0]; for (int i = 1; i < ys.Length; i++) if (ys[i] > m) m = ys[i]; return m; }
        }
    }

    /// Deterministic hash noise (gradient noise, fBm, Worley cells) for procedural skin painting.
    internal static class CreatureMeshNoise
    {
        static uint Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u + 0x7F4A7C15u;
                h ^= (uint)x * 0x85EBCA77u; h = (h << 13) | (h >> 19); h *= 0xC2B2AE3Du;
                h ^= (uint)y * 0x27D4EB2Fu; h = (h << 13) | (h >> 19); h *= 0x165667B1u;
                h ^= (uint)z * 0x9E3779B1u; h = (h << 13) | (h >> 19); h *= 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return h;
            }
        }

        /// Random value in [0, 1).
        public static float Rand(int x, int y, int z, int seed) => (Hash(x, y, z, seed) & 0xFFFFFF) / 16777216f;

        static int Floor(float x) { int i = (int)x; return x < i ? i - 1 : i; }

        static float Grad(int x, int y, int z, int seed, float dx, float dy, float dz)
        {
            switch (Hash(x, y, z, seed) % 12u)
            {
                case 0: return dx + dy;
                case 1: return -dx + dy;
                case 2: return dx - dy;
                case 3: return -dx - dy;
                case 4: return dx + dz;
                case 5: return -dx + dz;
                case 6: return dx - dz;
                case 7: return -dx - dz;
                case 8: return dy + dz;
                case 9: return -dy + dz;
                case 10: return dy - dz;
                default: return -dy - dz;
            }
        }

        /// Gradient noise in roughly [0, 1] (0.5 average).
        public static float Perlin(float x, float y, float z, int seed)
        {
            int xi = Floor(x), yi = Floor(y), zi = Floor(z);
            float fx = x - xi, fy = y - yi, fz = z - zi;
            float u = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float v = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            float w = fz * fz * fz * (fz * (fz * 6f - 15f) + 10f);
            float n000 = Grad(xi, yi, zi, seed, fx, fy, fz);
            float n100 = Grad(xi + 1, yi, zi, seed, fx - 1f, fy, fz);
            float n010 = Grad(xi, yi + 1, zi, seed, fx, fy - 1f, fz);
            float n110 = Grad(xi + 1, yi + 1, zi, seed, fx - 1f, fy - 1f, fz);
            float n001 = Grad(xi, yi, zi + 1, seed, fx, fy, fz - 1f);
            float n101 = Grad(xi + 1, yi, zi + 1, seed, fx - 1f, fy, fz - 1f);
            float n011 = Grad(xi, yi + 1, zi + 1, seed, fx, fy - 1f, fz - 1f);
            float n111 = Grad(xi + 1, yi + 1, zi + 1, seed, fx - 1f, fy - 1f, fz - 1f);
            float x00 = n000 + (n100 - n000) * u, x10 = n010 + (n110 - n010) * u;
            float x01 = n001 + (n101 - n001) * u, x11 = n011 + (n111 - n011) * u;
            float y0 = x00 + (x10 - x00) * v, y1 = x01 + (x11 - x01) * v;
            float r = y0 + (y1 - y0) * w;
            return CreatureMeshMath.Clamp01(r * 0.5f + 0.5f);
        }

        public static float Perlin(Vector3 p, int seed) => Perlin(p.x, p.y, p.z, seed);

        public static float Fbm(float x, float y, float z, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Perlin(x, y, z, seed + o * 131);
                norm += amp;
                x = x * 2.03f + 17.1f; y = y * 2.03f + 3.7f; z = z * 2.03f + 9.3f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        public static float Fbm(Vector3 p, int octaves, int seed) => Fbm(p.x, p.y, p.z, octaves, seed);

        /// 3D Worley noise. Returns F1 (distance to the nearest feature point, cell units); f2 = second nearest,
        /// cellRand = random 0..1 of the nearest cell (for per-spot variation).
        public static float Worley(float x, float y, float z, int seed, out float f2, out float cellRand)
        {
            int xi = Floor(x), yi = Floor(y), zi = Floor(z);
            float d1 = 1e9f, d2 = 1e9f;
            cellRand = 0f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                        float px = cx + Rand(cx, cy, cz, seed);
                        float py = cy + Rand(cx, cy, cz, seed + 1);
                        float pz = cz + Rand(cx, cy, cz, seed + 2);
                        float ex = px - x, ey = py - y, ez = pz - z;
                        float d = ex * ex + ey * ey + ez * ez;
                        if (d < d1)
                        {
                            d2 = d1; d1 = d;
                            cellRand = Rand(cx, cy, cz, seed + 3);
                        }
                        else if (d < d2) d2 = d;
                    }
            f2 = (float)Math.Sqrt(d2);
            return (float)Math.Sqrt(d1);
        }

        public static float Worley(Vector3 p, int seed, out float f2, out float cellRand) => Worley(p.x, p.y, p.z, seed, out f2, out cellRand);
    }
}
