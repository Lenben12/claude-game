using UnityEngine;

namespace Sealebrities
{
    /// Per-frame facts gathered once by FXDirector and shared by all FX subsystems (no allocations).
    sealed class FXFrame
    {
        public float dt, time, unscaledTime;
        public bool paused;
        public bool inGame;

        public bool hasPlayer;
        public Camera cam;
        public Transform camT;
        public Vector3 camPos, camFwd = Vector3.forward, camRight = Vector3.right, camUp = Vector3.up;

        public bool underwater, inWater, fainting, flashlight;
        /// Meters below the surface at the camera (0 above water).
        public float depth;
        public float air01 = 1f;
        public float maxSafeDepth = 9999f;

        /// Smoothed blends (0..1).
        public float under01, flash01, faint01, warn01, narcosis01, splash01;

        public float night01, sun01, day01;
    }

    static class FXMath
    {
        public const float TwoPi = 6.28318530718f;

        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        public static float Smooth01(float x)
        {
            x = Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// GLSL-style smoothstep between edges a and b.
        public static float SmoothRange(float a, float b, float x) => Smooth01((x - a) / (b - a));

        public static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        public static bool Differs(Color a, Color b, float eps) =>
            Mathf.Abs(a.r - b.r) > eps || Mathf.Abs(a.g - b.g) > eps || Mathf.Abs(a.b - b.b) > eps || Mathf.Abs(a.a - b.a) > eps;

        // ------------------------------------------------------------------ low-poly underwater look
        // Pure math only (no Unity API in the static initializers): FXMath is also used by worker threads.
        // Fog/background color by camera depth, picked from the LowPoly water palette (WaterShallow #7FD8E0,
        // Water #4DB3D6, WaterDeep #2C72AA): bright light turquoise in the shallow reef, bluer at 25 m,
        // dark blue at 50-60 m, near black from 80 m on.
        static readonly float[] StopDepth = { 0f, 4f, 10f, 18f, 25f, 38f, 52f, 65f, 80f, 120f };
        static readonly Color[] StopColor =
        {
            Hex(0x74D6E6), Hex(0x63CCE1), Hex(0x4CB7D9), Hex(0x3699CA), Hex(0x297DB8),
            Hex(0x1A5894), Hex(0x0E3768), Hex(0x09244A), Hex(0x041327), Hex(0x01060E)
        };
        static readonly Color MoonBlue = new Color(0.004f, 0.012f, 0.03f, 0f);

        /// Underwater fog + camera background color. Dimmed at dawn/dusk (sun01) and darkened at night, so the
        /// unlit fog matches the World module's attenuated sun/ambient light.
        public static Color UnderwaterColor(float depth, float night01, float sun01 = 1f)
        {
            Color c = StopColor[StopColor.Length - 1];
            if (depth <= 0f) c = StopColor[0];
            else
            {
                for (int i = 1; i < StopDepth.Length; i++)
                {
                    if (depth < StopDepth[i])
                    {
                        float t = (depth - StopDepth[i - 1]) / (StopDepth[i] - StopDepth[i - 1]);
                        c = Color.Lerp(StopColor[i - 1], StopColor[i], t);
                        break;
                    }
                }
            }
            Color day = c * (0.5f + 0.5f * SmoothRange(0.05f, 0.6f, sun01));
            if (night01 > 0f)
            {
                Color n = c * 0.1f + MoonBlue;
                day = Color.Lerp(day, n, Clamp01(night01));
            }
            day.a = 1f;
            return day;
        }

        /// Exponential-squared fog density underwater: the shallow reef stays crisp for ~15 m and readable to
        /// ~30-35 m; the deep closes in (flashlight territory).
        public static float UnderwaterFogDensity(float depth, float night01)
        {
            float k = 0.034f + 0.034f * Smooth01(depth / 90f);
            return k * (1f + 0.15f * Clamp01(night01));
        }

        /// Exponential-squared haze above water (soft pastel horizon, slightly closer at night).
        public static float SkyFogDensity(float night01) => 0.0011f + 0.0003f * Clamp01(night01);
    }

    /// Regulator breathing rhythm shared by the breathing audio and the exhale bubbles.
    sealed class FXBreath
    {
        float timer;
        bool active, inhaleFired, exhaleFired;

        public bool InhaleNow, ExhaleNow, Exhaling;
        /// 0..1 progress through the exhale window.
        public float ExhaleT;
        public float Period = 3.5f;
        public float Pitch = 1f;

        public void Tick(FXFrame f)
        {
            InhaleNow = false;
            ExhaleNow = false;
            bool on = f.hasPlayer && f.underwater && !f.fainting;
            if (f.paused) return;
            if (!on)
            {
                active = false;
                Exhaling = false;
                return;
            }
            if (!active)
            {
                active = true;
                timer = -0.6f;   // short pause after submerging before the first breath
                inhaleFired = false;
                exhaleFired = false;
            }

            float k = Mathf.InverseLerp(0.05f, 0.25f, f.air01);   // 0 = low air (fast breathing), 1 = calm
            Period = Mathf.Lerp(2.3f, 3.5f, k);
            Pitch = Mathf.Lerp(1.22f, 1f, k);

            timer += f.dt;
            float exStart = 0.4f * Period;
            float exLen = 0.42f * Period;
            if (timer >= 0f && !inhaleFired) { inhaleFired = true; InhaleNow = true; }
            if (timer >= exStart && !exhaleFired) { exhaleFired = true; ExhaleNow = true; }
            Exhaling = timer >= exStart && timer < exStart + exLen;
            ExhaleT = Exhaling ? (timer - exStart) / exLen : 0f;
            if (timer >= Period)
            {
                timer -= Period;
                inhaleFired = false;
                exhaleFired = false;
            }
        }
    }
}
