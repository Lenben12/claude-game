using System;

namespace Sealebrities
{
    /// Thread-safe gradient noise used by the world generator (heightfield, splatmap, procedural textures).
    /// Pure C# (no Unity API) so it can run on worker threads. Output of Perlin() is roughly in [-1, 1]
    /// (standard deviation ~0.3). The periodic variants tile seamlessly over the given integer period.
    public static class WorldNoise
    {
        static readonly int[] P = BuildPermutation(91731);

        static int[] BuildPermutation(int seed)
        {
            var rng = new Random(seed);
            var p = new int[512];
            var src = new int[256];
            for (int i = 0; i < 256; i++) src[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = src[i]; src[i] = src[j]; src[j] = t;
            }
            for (int i = 0; i < 512; i++) p[i] = src[i & 255];
            return p;
        }

        static int FastFloor(float x) { int i = (int)x; return x < i ? i - 1 : i; }
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;

        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x * 1.41421f;
                case 5: return -x * 1.41421f;
                case 6: return y * 1.41421f;
                default: return -y * 1.41421f;
            }
        }

        /// Classic 2D gradient noise, roughly [-1, 1].
        public static float Perlin(float x, float y)
        {
            int xi = FastFloor(x), yi = FastFloor(y);
            float xf = x - xi, yf = y - yi;
            int X = xi & 255, Y = yi & 255;
            float u = Fade(xf), v = Fade(yf);
            int a = P[X] + Y, b = P[X + 1] + Y;
            float x1 = Lerp(Grad(P[a], xf, yf), Grad(P[b], xf - 1f, yf), u);
            float x2 = Lerp(Grad(P[a + 1], xf, yf - 1f), Grad(P[b + 1], xf - 1f, yf - 1f), u);
            return Lerp(x1, x2, v);
        }

        /// Fractal Brownian motion of Perlin noise, normalized to roughly [-1, 1].
        public static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x + i * 17.31f, y - i * 9.73f) * amp;
                norm += amp;
                amp *= 0.5f;
                x *= 2.03f; y *= 2.03f;
            }
            return sum / norm;
        }

        /// Ridged noise 0..1 (1 on the ridge lines).
        public static float Ridge(float x, float y)
        {
            float r = 1f - Math.Abs(Perlin(x, y)) * 1.3f;
            if (r < 0f) r = 0f;
            return r * r;
        }

        // ------------------------------------------------------------------ periodic (tileable) variants
        static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

        /// Gradient noise that tiles with integer period (px, py) in lattice units.
        public static float PerlinPeriodic(float x, float y, int px, int py, int seed = 0)
        {
            int xi = FastFloor(x), yi = FastFloor(y);
            float xf = x - xi, yf = y - yi;
            int x0 = Mod(xi, px), x1 = Mod(xi + 1, px);
            int y0 = Mod(yi, py), y1 = Mod(yi + 1, py);
            int s = seed & 255;
            int h00 = P[P[(x0 + s) & 255] + (y0 & 255)];
            int h10 = P[P[(x1 + s) & 255] + (y0 & 255)];
            int h01 = P[P[(x0 + s) & 255] + (y1 & 255)];
            int h11 = P[P[(x1 + s) & 255] + (y1 & 255)];
            float u = Fade(xf), v = Fade(yf);
            float a = Lerp(Grad(h00, xf, yf), Grad(h10, xf - 1f, yf), u);
            float b = Lerp(Grad(h01, xf, yf - 1f), Grad(h11, xf - 1f, yf - 1f), u);
            return Lerp(a, b, v);
        }

        /// Tileable fBm over the unit square: u, v in [0, 1), baseFreq lattice cells across the tile.
        public static float FbmTile(float u, float v, int baseFreq, int octaves, int seed = 0)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            int f = baseFreq;
            for (int i = 0; i < octaves; i++)
            {
                sum += PerlinPeriodic(u * f, v * f, f, f, seed + i * 31) * amp;
                norm += amp;
                amp *= 0.5f;
                f *= 2;
            }
            return sum / norm;
        }

        /// Tileable anisotropic noise (different lattice counts along u and v).
        public static float TileAniso(float u, float v, int fu, int fv, int seed = 0)
            => PerlinPeriodic(u * fu, v * fv, fu, fv, seed);

        /// Deterministic per-integer hash in [0, 1).
        public static float Hash01(int x, int y, int seed = 0)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        /// Tileable Voronoi border distance (F2 - F1): 0 on the cell borders, larger inside cells.
        public static float CellEdgeTile(float u, float v, int cells, int seed = 0)
        {
            float x = u * cells, y = v * cells;
            int xi = FastFloor(x), yi = FastFloor(y);
            float f1 = 9f, f2 = 9f;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = xi + ox, cy = yi + oy;
                    int wx = Mod(cx, cells), wy = Mod(cy, cells);
                    float fx = cx + Hash01(wx, wy, seed) - x;
                    float fy = cy + Hash01(wx, wy, seed + 7) - y;
                    float d = (float)Math.Sqrt(fx * fx + fy * fy);
                    if (d < f1) { f2 = f1; f1 = d; }
                    else if (d < f2) f2 = d;
                }
            return f2 - f1;
        }

        /// Tileable cellular (Worley F1) noise, 0 at feature points .. ~1 between them.
        public static float CellTile(float u, float v, int cells, int seed = 0)
        {
            float x = u * cells, y = v * cells;
            int xi = FastFloor(x), yi = FastFloor(y);
            float best = 9f;
            for (int oy = -1; oy <= 1; oy++)
                for (int ox = -1; ox <= 1; ox++)
                {
                    int cx = xi + ox, cy = yi + oy;
                    int wx = Mod(cx, cells), wy = Mod(cy, cells);
                    float fx = cx + Hash01(wx, wy, seed) - x;
                    float fy = cy + Hash01(wx, wy, seed + 7) - y;
                    float d = fx * fx + fy * fy;
                    if (d < best) best = d;
                }
            return (float)Math.Sqrt(best);
        }
    }
}
