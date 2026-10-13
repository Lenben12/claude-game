using System;

namespace Sealebrities
{
    /// Thread-safe gradient noise used by the world generator (heightfield and terrain color classes).
    /// Pure C# (no Unity API) so it can run on worker threads. Output of Perlin() is roughly in [-1, 1]
    /// (standard deviation ~0.3).
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
    }
}
