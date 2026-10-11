using UnityEngine;

namespace Sealebrities
{
    /// Single source of truth for the ocean surface. The ocean mesh, boat buoyancy and the player all sample this.
    public static class Waves
    {
        public const float SeaLevel = 0f;

        /// Global wave strength (1 = normal, 0 = flat, 2 = stormy). Weather can change this.
        public static float Strength = 1f;

        // direction (x, z), amplitude (m), wavelength (m)
        static readonly Vector4[] Components =
        {
            new Vector4( 0.94f,  0.34f, 0.22f, 31f),
            new Vector4(-0.42f,  0.91f, 0.14f, 17f),
            new Vector4( 0.70f, -0.71f, 0.08f, 9f),
            new Vector4(-0.95f, -0.31f, 0.05f, 5.3f),
        };

        static float T => Time.time;

        // per component, precomputed once (same float expressions as before → bit-identical heights):
        // wave number, phase speed, direction and amplitude. HeightAt runs ~3,000× per frame (ocean mesh).
        static readonly int N = Components.Length;
        static readonly float[] K = new float[N], S = new float[N], DX = new float[N], DZ = new float[N], A = new float[N];

        static Waves()
        {
            for (int i = 0; i < N; i++)
            {
                var c = Components[i];
                float k = 2f * Mathf.PI / c.w;
                K[i] = k;
                S[i] = Mathf.Sqrt(9.81f / k);
                DX[i] = c.x;
                DZ[i] = c.y;
                A[i] = c.z;
            }
        }

        public static float HeightAt(float x, float z)
        {
            float t = T;
            float h = 0f;
            for (int i = 0; i < N; i++)
                h += A[i] * Mathf.Sin(K[i] * (DX[i] * x + DZ[i] * z - S[i] * t));
            return SeaLevel + h * Strength;
        }

        public static float HeightAt(Vector3 p) => HeightAt(p.x, p.z);

        public static Vector3 NormalAt(float x, float z)
        {
            const float e = 0.4f;
            float hL = HeightAt(x - e, z), hR = HeightAt(x + e, z);
            float hD = HeightAt(x, z - e), hU = HeightAt(x, z + e);
            return new Vector3(hL - hR, 2f * e, hD - hU).normalized;
        }

        /// Positive meters below the moving surface (negative when above).
        public static float DepthOf(Vector3 p) => HeightAt(p.x, p.z) - p.y;

        public static bool IsUnderwater(Vector3 p) => p.y < HeightAt(p.x, p.z);
    }
}
