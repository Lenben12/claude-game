using UnityEngine;

namespace Sealebrities
{
    /// Per-frame facts gathered once by FXDirector and shared by all FX subsystems (no allocations).
    sealed class FXFrame
    {
        public float dt, time, unscaledTime;
        /// True while the game is really paused (Time.timeScale == 0, offline pause menu / finale).
        public bool paused;
        public bool inGame;

        public bool hasPlayer;
        public Camera cam;
        public Transform camT;
        public Vector3 camPos, camFwd = Vector3.forward, camRight = Vector3.right, camUp = Vector3.up;

        public bool underwater, inWater, fainting, flashlight;
        /// Player root position and vertical speed (m/s, negative = falling) – for the body splash (Q9).
        public Vector3 playerPos;
        public float velY;
        /// Meters below the surface at the camera (0 above water).
        public float depth;
        public float air01 = 1f;
        public float maxSafeDepth = 9999f;

        /// Smoothed blends (0..1).
        public float under01, flash01, faint01, warn01, narcosis01, splash01;
        /// Real seconds since the camera last went below the surface (large while above water).
        public float sinceSubmerge = 100f;

        public float night01, sun01, day01;
        /// Sun elevation in degrees read from the World sun transform (negative at night).
        public float sunElevation = 45f;
        /// 0..1 golden-hour weight (sun a few degrees above the horizon, 0 at night and at high sun).
        public float golden01;

        public GraphicsQuality quality = GraphicsQuality.High;
    }

    static class FXMath
    {
        public const float TwoPi = 6.28318530718f;
        public const float Ln2 = 0.693147f, Ln10 = 2.302585f;
        public const float SqrtLn2 = 0.832555f, SqrtLn10 = 1.517427f;

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

        /// Piecewise-linear lookup in a key table (keys ascending).
        public static float Interp(float[] keys, float[] values, float x)
        {
            if (x <= keys[0]) return values[0];
            int last = keys.Length - 1;
            if (x >= keys[last]) return values[last];
            for (int i = 1; i <= last; i++)
            {
                if (x < keys[i])
                {
                    float t = (x - keys[i - 1]) / (keys[i] - keys[i - 1]);
                    return values[i - 1] + (values[i] - values[i - 1]) * t;
                }
            }
            return values[last];
        }

        // ------------------------------------------------------------------ underwater look (art bible §2.1 / §2.2)
        // Pure math only (no Unity API in the static initializers): FXMath is also used by worker threads.
        // Fog + background colour by camera depth: bright saturated turquoise in the shallows, blue at 25 m, dark
        // blue at 55 m, near black from 80 m on. These are the on-screen targets; ACES + the underwater exposure
        // lift (+0.35) bring the grading output back to roughly the same values.
        static readonly float[] StopDepth = { 0f, 3f, 10f, 18f, 25f, 40f, 55f, 80f, 120f };
        // (art polish: the shallow stops are pre-compensated for the grade – #5ECAD9 measured #86CED5 on screen, milky;
        // #45CCE4 lands near the bright, saturated turquoise target)
        // (round 2, Q10 residual light: the deep never turns black – fog floor #0E2E52 at 80 m, #0A2340 from 120 m on,
        // so silhouettes and the seabed stay readable without the lamp)
        static readonly Color[] StopColor =
        {
            Hex(0x50CDE2), Hex(0x50CDE2), Hex(0x3AABCE), Hex(0x2D90BC), Hex(0x2478AA),
            Hex(0x1A5A8C), Hex(0x14426F), Hex(0x0E2E52), Hex(0x0A2340)
        };
        /// Moonlight scattered in the upper water column at night (readable navy instead of pure black).
        static readonly Color MoonNavy = new Color(0.030f, 0.062f, 0.125f, 0f);

        /// Day colour of the water at a camera depth (no time-of-day dimming).
        public static Color UnderwaterDayColor(float depth)
        {
            if (depth <= StopDepth[0]) return StopColor[0];
            for (int i = 1; i < StopDepth.Length; i++)
            {
                if (depth < StopDepth[i])
                {
                    float t = (depth - StopDepth[i - 1]) / (StopDepth[i] - StopDepth[i - 1]);
                    return Color.Lerp(StopColor[i - 1], StopColor[i], t);
                }
            }
            return StopColor[StopColor.Length - 1];
        }

        /// Underwater fog + camera background colour. Dimmed a little at dawn/dusk (sun01) and turned into a dark
        /// moonlit navy at night, so the unlit fog matches the World module's attenuated sun/ambient light.
        public static Color UnderwaterColor(float depth, float night01, float sun01 = 1f)
        {
            Color c = UnderwaterDayColor(depth);
            // dusk keeps at least 60 % of the day colour: entering the water in the evening is never black
            Color day = c * Mathf.Lerp(0.6f, 1f, SmoothRange(0.06f, 0.5f, sun01));
            if (night01 > 0f)
            {
                Color n = c * 0.12f + MoonNavy * (1f - SmoothRange(0f, 45f, depth));
                day = Color.Lerp(day, n, Clamp01(night01));
            }
            day.a = 1f;
            return day;
        }

        // Exponential-squared fog density k by camera depth. Visibility (10 % of an object's own colour left) =
        // sqrt(ln 10) / k: ~19 m in the shallows, ~17 m at 15 m, ~13 m at 25 m, ~11 m at 40 m, ~10 m in the deep
        // (round 2, Q10: was ~8 m – with the residual-light floor the seabed reads at ~10 m without the lamp).
        // Objects within 6 m keep ~80 % of their colour in the shallows. Night: k x 1.5 (much shorter sight).
        static readonly float[] KDepth = { 0f, 8f, 15f, 25f, 40f, 60f, 80f };
        static readonly float[] KValue = { 0.080f, 0.080f, 0.090f, 0.118f, 0.135f, 0.145f, 0.152f };
        public const float NightFogBoost = 0.5f;

        public static float UnderwaterFogDensity(float depth, float night01) =>
            Interp(KDepth, KValue, Mathf.Max(0f, depth)) * (1f + NightFogBoost * Clamp01(night01));

        /// Exponential (not squared) haze above water: 8 % at 50 m, 21 % at 150 m, 62 % at 600 m by day.
        public static float SkyFogDensity(float night01) => 0.0016f + 0.0006f * Clamp01(night01);

        /// Distance at which the exponential and the exponential-squared fog are equally dense (blend while the
        /// lens crosses the surface).
        public const float FogMatchDistance = 15f;

        /// Above-water fog must never read grey by day (HSV saturation >= 0.25 at high sun, blue hue when grey).
        /// Main thread only (uses Color.RGBToHSV).
        public static Color GuardSkyFog(Color c, float sun01)
        {
            c.a = 1f;
            float need = 0.25f * SmoothRange(0.55f, 0.75f, sun01);
            if (need <= 0.001f) return c;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (s >= need) return c;
            if (s < 0.03f) h = 0.58f;   // pure grey has no hue: take the sky blue (~209°)
            Color o = Color.HSVToRGB(h, need, v);
            o.a = 1f;
            return o;
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
        public float Period = 4.2f;
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

            // Q12: a calm breath every 4.2 s (inhale 1.2 s, exhale + bubbles 1.6 s), 2.6 s below 25 % air, 1.8 s below 10 %
            // (the clips are pitched up a little so they fit the shorter rhythm)
            float k1 = Mathf.InverseLerp(0.22f, 0.28f, f.air01);
            float k2 = Mathf.InverseLerp(0.08f, 0.12f, f.air01);
            Period = Mathf.Lerp(1.8f, Mathf.Lerp(2.6f, 4.2f, k1), k2);
            Pitch = Mathf.Lerp(1.3f, Mathf.Lerp(1.12f, 1f, k1), k2);

            timer += f.dt;
            float exStart = Mathf.Min(1.2f / Pitch, 0.36f * Period);
            float exLen = 1.6f / Pitch;
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
