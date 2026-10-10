using UnityEngine;

namespace Sealebrities
{
    /// Particle sprites of the Boat module. Low-poly look: crisp, flat cartoon shapes (no noise, no photo detail).
    /// Everything else in the module is solid-colored geometry (see BoatMats).
    public static class BoatTextures
    {
        static Texture2D soft, foam;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { soft = foam = null; }

        /// Round flat puff (smoke, spray, dust): solid disc with a thin anti-aliased edge.
        public static Texture2D Soft { get { if (soft == null) soft = BuildDisc(); return soft; } }

        /// Flat foam patch: a white blob with a few round bumps on its outline.
        public static Texture2D Foam { get { if (foam == null) foam = BuildFoam(); return foam; } }

        static Texture2D NewTex(int n, string name)
        {
            return new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
        }

        static Color32 White(float a) => new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));

        static Texture2D BuildDisc()
        {
            const int N = 64;
            var px = new Color32[N * N];
            float edge = 1.5f / N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    px[y * N + x] = White(Mathf.Clamp01((0.92f - r) / (edge * 2f)));
                }
            var t = NewTex(N, "BoatPuff");
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }

        static Texture2D BuildFoam()
        {
            const int N = 64;
            var px = new Color32[N * N];
            float edge = 1.5f / N;
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float outline = 0.78f + 0.1f * Mathf.Cos(ang * 5f + 0.6f);
                    float a = Mathf.Clamp01((outline - r) / (edge * 2f));
                    // a darker-free, slightly transparent center keeps overlapping patches readable
                    a *= Mathf.Lerp(0.7f, 1f, Mathf.Clamp01((r - 0.25f) / 0.35f));
                    px[y * N + x] = White(a);
                }
            var t = NewTex(N, "BoatFoam");
            t.SetPixels32(px);
            t.Apply(true, true);
            return t;
        }
    }
}
