using UnityEngine;

namespace Sealebrities
{
    /// Under-water visibility at the main camera (art bible §2.2: ~19 m in the shallows, less deeper, less at night).
    /// A thin World-side facade over FXVisibility (the FX module owns the fog and is the single source of truth): the
    /// day/night cycle refreshes these values every frame for the camera depth, so modules that only know the World
    /// API read the same numbers the fog actually renders.
    public static class WorldVisibility
    {
        /// Current under-water sight distance in meters at the camera depth (distance where 10 % of an object's own
        /// colour is left). Above water it holds the value for the shallows.
        public static float UnderwaterMeters = 19.4f;
        /// Exponential-squared density matching UnderwaterMeters.
        public static float UnderwaterFogDensity = 0.078f;
        /// True while the main camera is under water.
        public static bool CameraUnderwater;

        /// sqrt(ln 10): exp(-(k d)^2) = 0.1  →  d = Ln10Sqrt / k.
        const float Ln10Sqrt = 1.5174f;

        /// Sight distance in meters for a camera depth (from the FX fog table).
        public static float MetersAt(float depth, float night01) => Mathf.Max(1f, FXVisibility.UnderwaterMeters(Mathf.Max(0f, depth), night01));

        /// Fraction (0..1) of an object's own colour left at a distance for a camera depth.
        public static float Clarity(float distance, float depth, float night01)
        {
            float x = Ln10Sqrt / MetersAt(depth, night01) * distance;
            return Mathf.Exp(-x * x);
        }

        internal static void Publish(float cameraDepth, float night01, bool underwater)
        {
            UnderwaterMeters = MetersAt(cameraDepth, night01);
            UnderwaterFogDensity = Ln10Sqrt / UnderwaterMeters;
            CameraUnderwater = underwater;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { UnderwaterMeters = 19.4f; UnderwaterFogDensity = 0.078f; CameraUnderwater = false; }
    }
}
