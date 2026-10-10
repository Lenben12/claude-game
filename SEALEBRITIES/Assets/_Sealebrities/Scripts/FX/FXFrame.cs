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

        /// Underwater fog color gradient by depth (#3BBBD3 0 m .. #020814 120 m+), darkened at night.
        static readonly float[] StopDepth = { 0f, 12f, 28f, 45f, 75f, 120f };
        static readonly Color[] StopColor =
        {
            Hex(0x3BBBD3), Hex(0x1C86B6), Hex(0x0F5591), Hex(0x0A3363), Hex(0x061C3A), Hex(0x020814)
        };
        static readonly Color MoonBlue = new Color(0.004f, 0.012f, 0.03f, 0f);

        public static Color UnderwaterColor(float depth, float night01)
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
            if (night01 > 0f)
            {
                Color n = c * 0.09f + MoonBlue;
                n.a = 1f;
                c = Color.Lerp(c, n, Clamp01(night01));
            }
            c.a = 1f;
            return c;
        }
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
