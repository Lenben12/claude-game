using UnityEngine;

namespace Sealebrities
{
    /// Deterministic hash / value noise / fBm / Worley noise used by the props module (textures and mesh displacement).
    /// 2D functions accept separate integer periods per axis so textures tile seamlessly.
    public static class PropNoise
    {
        public static uint Hash(uint x)
        {
            unchecked
            {
                x ^= x >> 16; x *= 0x7feb352dU;
                x ^= x >> 15; x *= 0x846ca68bU;
                x ^= x >> 16;
                return x;
            }
        }

        public static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                uint h = Hash((uint)x * 0x8da6b343U ^ Hash((uint)y * 0xd8163841U ^ (uint)seed * 0xcb1ab31fU));
                return (h & 0x00FFFFFF) * (1f / 16777216f);
            }
        }

        public static float Hash01(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = Hash((uint)x * 0x8da6b343U ^ Hash((uint)y * 0xd8163841U ^ Hash((uint)z * 0x9e3779b9U ^ (uint)seed * 0xcb1ab31fU)));
                return (h & 0x00FFFFFF) * (1f / 16777216f);
            }
        }

        static int Wrap(int v, int p)
        {
            if (p <= 0) return v;
            int r = v % p;
            return r < 0 ? r + p : r;
        }

        static float Fade(float t) => t * t * (3f - 2f * t);

        /// Value noise in [0,1]. px/py > 0 make it periodic.
        public static float Value(float x, float y, int seed, int px = 0, int py = 0)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            int x0 = Wrap(ix, px), x1 = Wrap(ix + 1, px);
            int y0 = Wrap(iy, py), y1 = Wrap(iy + 1, py);
            float a = Hash01(x0, y0, seed), b = Hash01(x1, y0, seed);
            float c = Hash01(x0, y1, seed), d = Hash01(x1, y1, seed);
            float ux = Fade(fx), uy = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
        }

        /// Fractal value noise in [0,1]. Periods double with each octave so tiling is kept.
        public static float Fbm(float x, float y, int octaves, int seed, int px = 0, int py = 0)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Value(x, y, seed + i * 131, px, py);
                norm += amp;
                x *= 2f; y *= 2f; px *= 2; py *= 2;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// Ridged fractal noise in [0,1] (1 on ridges).
        public static float Ridged(float x, float y, int octaves, int seed, int px = 0, int py = 0)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float r = 1f - Mathf.Abs(2f * Value(x, y, seed + i * 71, px, py) - 1f);
                sum += amp * r * r;
                norm += amp;
                x *= 2f; y *= 2f; px *= 2; py *= 2;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        public static float Value3(float x, float y, float z, int seed)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
            float fx = Fade(x - ix), fy = Fade(y - iy), fz = Fade(z - iz);
            float c000 = Hash01(ix, iy, iz, seed), c100 = Hash01(ix + 1, iy, iz, seed);
            float c010 = Hash01(ix, iy + 1, iz, seed), c110 = Hash01(ix + 1, iy + 1, iz, seed);
            float c001 = Hash01(ix, iy, iz + 1, seed), c101 = Hash01(ix + 1, iy, iz + 1, seed);
            float c011 = Hash01(ix, iy + 1, iz + 1, seed), c111 = Hash01(ix + 1, iy + 1, iz + 1, seed);
            float a = Mathf.Lerp(Mathf.Lerp(c000, c100, fx), Mathf.Lerp(c010, c110, fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(c001, c101, fx), Mathf.Lerp(c011, c111, fx), fy);
            return Mathf.Lerp(a, b, fz);
        }

        public static float Fbm3(Vector3 p, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Value3(p.x, p.y, p.z, seed + i * 97);
                norm += amp;
                p *= 2.03f;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        /// Tileable Worley noise: distance to the nearest (f1) and second nearest (f2) feature point, in cell units.
        public static void Cell(float x, float y, int seed, int period, out float f1, out float f2)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            f1 = 9f; f2 = 9f;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ix + dx, cy = iy + dy;
                    int hx = Wrap(cx, period), hy = Wrap(cy, period);
                    float fx = cx + Hash01(hx, hy, seed);
                    float fy = cy + Hash01(hx, hy, seed + 17);
                    float ddx = fx - x, ddy = fy - y;
                    float d = Mathf.Sqrt(ddx * ddx + ddy * ddy);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
            }
        }

        public static float Frac(float v) => v - Mathf.Floor(v);
    }
}
