using UnityEngine;

namespace Sealebrities
{
    /// Day/night cycle. The only writer of sun, moon, skybox, ambient light and reflection intensity.
    /// Publishes EnvState every frame (before other modules update). Look: pastel sky, soft bright ambient (no black
    /// shadows), warm-but-not-orange golden hour, navy moonlit nights (the moon becomes URP's main light and lights the
    /// skybox). Below the surface sun and ambient are attenuated per channel with the camera depth: bright and readable
    /// in the shallows, dimmer at 25 m, dark blue at 50-60 m (flashlight territory) and near black beyond 80 m.
    [DefaultExecutionOrder(-50)]
    public class WorldDayNight : MonoBehaviour
    {
        public const float MenuTime = 0.70f;
        public const float Sunrise = 0.235f, Sunset = 0.805f;
        const float MaxElevation = 72f, MaxNightDepth = 50f;
        const float RiseAzimuth = 75f, SetAzimuth = 245f;     // compass degrees: rises ENE, sets WSW
        const float MoonTakeover = -7f;                       // sun elevation below which the moon is the main light

        Light sun, moon;
        Material sky, starMat, moonMat;
        ReflectionProbe probe;
        MeshRenderer starR, moonR;
        WorldClouds clouds;
        Camera cam;

        float lastProbeTime = -100f, lastProbeElev = -999f, lastProbeUw = -1f, lastGiTime = -100f;
        bool lastProbeMoon;
        LightShadows sunShadowState = LightShadows.Soft;
        Vector3 moonDir = Vector3.up;
        bool moonMain;

        /// Time of day currently shown (0..1).
        public float TimeOfDay { get; private set; }
        /// Sun elevation in degrees (negative at night).
        public float SunElevation { get; private set; }
        /// Camera depth below the surface used for light attenuation (0 above water).
        public float CameraDepth { get; private set; }

        static int IdBaseColor = -1, IdExposure, IdThickness, IdSkyTint, IdGround, IdSunSize, IdSunConv;

        static void InitIds()
        {
            if (IdBaseColor >= 0) return;
            IdBaseColor = Shader.PropertyToID("_BaseColor");
            IdExposure = Shader.PropertyToID("_Exposure");
            IdThickness = Shader.PropertyToID("_AtmosphereThickness");
            IdSkyTint = Shader.PropertyToID("_SkyTint");
            IdGround = Shader.PropertyToID("_GroundColor");
            IdSunSize = Shader.PropertyToID("_SunSize");
            IdSunConv = Shader.PropertyToID("_SunSizeConvergence");
        }

        // ------------------------------------------------------------------ keyframes (by sun elevation in degrees)
        static readonly float[] SunKeys = { -4f, 0f, 5f, 12f, 22f, 40f, 70f };
        static readonly Color[] SunColors =
        {
            new Color(1f, 0.50f, 0.36f), new Color(1f, 0.62f, 0.46f), new Color(1f, 0.75f, 0.58f), new Color(1f, 0.86f, 0.71f),
            new Color(1f, 0.93f, 0.83f), new Color(1f, 0.97f, 0.92f), new Color(1f, 0.99f, 0.96f),
        };

        static readonly float[] AmbKeys = { -18f, -6f, 0f, 12f, 30f, 60f };
        static readonly Color[] AmbSky =
        {
            new Color(0.12f, 0.15f, 0.28f), new Color(0.22f, 0.24f, 0.38f), new Color(0.52f, 0.50f, 0.64f),
            new Color(0.60f, 0.66f, 0.82f), new Color(0.66f, 0.75f, 0.90f), new Color(0.68f, 0.78f, 0.93f),
        };
        static readonly Color[] AmbEquator =
        {
            new Color(0.10f, 0.12f, 0.21f), new Color(0.24f, 0.22f, 0.31f), new Color(0.68f, 0.54f, 0.50f),
            new Color(0.76f, 0.68f, 0.60f), new Color(0.72f, 0.76f, 0.78f), new Color(0.72f, 0.77f, 0.80f),
        };
        static readonly Color[] AmbGround =
        {
            new Color(0.06f, 0.07f, 0.11f), new Color(0.10f, 0.10f, 0.14f), new Color(0.32f, 0.27f, 0.24f),
            new Color(0.42f, 0.38f, 0.32f), new Color(0.48f, 0.46f, 0.40f), new Color(0.50f, 0.48f, 0.42f),
        };

        static readonly float[] FogKeys = { -18f, -6f, 0f, 8f, 18f, 35f, 60f };
        static readonly Color[] FogColors =
        {
            // golden hour: a light peach-lilac haze (a beige haze turned the far sea sand-colored)
            new Color(0.05f, 0.08f, 0.17f), new Color(0.17f, 0.18f, 0.31f), new Color(0.80f, 0.68f, 0.70f), new Color(0.84f, 0.81f, 0.82f),
            new Color(0.83f, 0.87f, 0.90f), new Color(0.76f, 0.86f, 0.95f), new Color(0.74f, 0.85f, 0.95f),
        };

        // sun-lit sky (values calibrated against the procedural sky's scattering math for a pastel look)
        // pastel blue by day, periwinkle at golden hour, lavender/violet at dusk (a thinner atmosphere avoids the
        // procedural sky's mustard-yellow low-sun horizon)
        static readonly float[] SkyKeys = { -7f, -4f, -1f, 3f, 8f, 20f, 40f };
        static readonly float[] Exposure = { 0.05f, 0.35f, 1.2f, 2.1f, 2.45f, 2.75f, 2.8f };
        static readonly float[] Thickness = { 0.9f, 0.9f, 0.88f, 0.86f, 0.92f, 1.0f, 1.0f };
        static readonly float[] SunSize = { 0.055f, 0.055f, 0.055f, 0.05f, 0.048f, 0.045f, 0.042f };
        static readonly float[] SunConv = { 3f, 3f, 3f, 3.2f, 3.4f, 3.6f, 3.8f };
        static readonly Color[] SkyTint =
        {
            new Color(0.62f, 0.40f, 0.62f), new Color(0.66f, 0.40f, 0.60f), new Color(0.72f, 0.38f, 0.52f), new Color(0.70f, 0.40f, 0.55f),
            new Color(0.60f, 0.44f, 0.60f), new Color(0.45f, 0.50f, 0.63f), new Color(0.40f, 0.50f, 0.64f),
        };
        static readonly Color SkyGroundDay = new Color(0.40f, 0.55f, 0.66f), SkyGroundNight = new Color(0.04f, 0.06f, 0.13f);

        // moon-lit night sky: deep navy zenith, soft blue horizon
        static readonly Color NightTint = new Color(0.28f, 0.38f, 0.75f);
        const float NightThickness = 0.7f, NightExposure = 0.45f;

        // underwater: per-channel light left at a camera depth (red fades first, but the shallows stay sand-colored)
        static readonly float[] DepthKeys = { 0f, 4f, 10f, 18f, 25f, 40f, 55f, 80f, 120f };
        static readonly float[] AttR = { 1f, 0.93f, 0.80f, 0.60f, 0.46f, 0.24f, 0.11f, 0.035f, 0.012f };
        static readonly float[] AttG = { 1f, 0.98f, 0.93f, 0.82f, 0.70f, 0.47f, 0.27f, 0.085f, 0.025f };
        static readonly float[] AttB = { 1f, 1f, 0.98f, 0.91f, 0.82f, 0.60f, 0.38f, 0.13f, 0.04f };

        // underwater scattered light (multiplied by daylight and depth attenuation): bright and only slightly aqua in
        // the shallows so sand stays sand-colored; the per-channel attenuation turns it blue with depth
        static readonly Color WaterSky = new Color(0.72f, 0.85f, 0.90f);
        static readonly Color WaterEquator = new Color(0.52f, 0.70f, 0.78f);
        static readonly Color WaterGround = new Color(0.32f, 0.44f, 0.48f);

        public void Init(Light sun, Light moon, Material sky, ReflectionProbe probe,
                         MeshRenderer stars, Material starMat, MeshRenderer moonDisc, Material moonMat, WorldClouds clouds)
        {
            InitIds();
            this.sun = sun; this.moon = moon; this.sky = sky; this.probe = probe;
            starR = stars; this.starMat = starMat;
            moonR = moonDisc; this.moonMat = moonMat;
            this.clouds = clouds;
        }

        /// Applies the lighting immediately (used right after building so the first rendered frame is lit).
        public void ApplyNow()
        {
            Apply();
            FollowCamera();
        }

        void Update() => Apply();

        void LateUpdate() => FollowCamera();

        Camera ActiveCamera()
        {
            if (cam != null && cam.isActiveAndEnabled) return cam;
            cam = Camera.main;
            if (cam == null && Refs.Player != null) cam = Refs.Player.View;
            return cam;
        }

        // ------------------------------------------------------------------ sun path
        /// Sun elevation and compass azimuth (degrees) for a time of day.
        public static void SunPosition(float t, out float elevation, out float azimuth)
        {
            t -= Mathf.Floor(t);
            if (t >= Sunrise && t <= Sunset)
            {
                float f = (t - Sunrise) / (Sunset - Sunrise);
                float s = Mathf.Sin(Mathf.PI * f);
                // mornings brighten quickly; evenings keep a long, low golden hour
                elevation = MaxElevation * Mathf.Pow(s, f < 0.5f ? 1.6f : 2.6f);
                azimuth = Mathf.Lerp(RiseAzimuth, SetAzimuth, f);
            }
            else
            {
                float span = 1f - (Sunset - Sunrise);
                float f = (t > Sunset ? t - Sunset : t + 1f - Sunset) / span;
                elevation = -MaxNightDepth * Mathf.Sin(Mathf.PI * f);
                azimuth = Mathf.Lerp(SetAzimuth, RiseAzimuth + 360f, f);
            }
        }

        /// Unit vector pointing toward the sky position (azimuth 0 = +Z north, 90 = +X east).
        public static Vector3 Direction(float elevation, float azimuth)
        {
            float e = elevation * Mathf.Deg2Rad, a = azimuth * Mathf.Deg2Rad;
            float c = Mathf.Cos(e);
            return new Vector3(Mathf.Sin(a) * c, Mathf.Sin(e), Mathf.Cos(a) * c);
        }

        /// Per-channel fraction of daylight left at a depth below the surface (alpha = 1).
        public static Color DepthAttenuation(float depth)
        {
            if (depth <= 0f) return Color.white;
            int i = Linear(DepthKeys, depth, out float f);
            return new Color(Mathf.Lerp(AttR[i], AttR[i + 1], f), Mathf.Lerp(AttG[i], AttG[i + 1], f), Mathf.Lerp(AttB[i], AttB[i + 1], f), 1f);
        }

        // ------------------------------------------------------------------ per frame
        void Apply()
        {
            if (sun == null) return;
            float t = GameManager.I != null && GameManager.I.InGame ? GameManager.I.State.timeOfDay : MenuTime;
            t -= Mathf.Floor(t);
            TimeOfDay = t;
            SunPosition(t, out float elev, out float az);
            SunElevation = elev;

            Vector3 sunDir = Direction(elev, az);
            sun.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
            moonDir = -sunDir;
            float moonElev = -elev;
            moon.transform.rotation = Quaternion.LookRotation(sunDir, Vector3.up);

            float horizon = SmoothStep(-3f, 8f, elev);
            float sinE = Mathf.Sin(Mathf.Max(0f, elev) * Mathf.Deg2Rad);
            float sun01 = horizon * (0.42f + 0.58f * Mathf.Clamp01(sinE / 0.94f));
            float night01 = 1f - SmoothStep(-12f, 2f, elev);

            // ---- camera depth -> per-channel light attenuation
            float depth = 0f;
            var c = ActiveCamera();
            if (c != null)
            {
                Vector3 p = c.transform.position;
                depth = Mathf.Max(0f, Waves.HeightAt(p.x, p.z) - p.y);
            }
            CameraDepth = depth;
            float uw = SmoothStep(0f, 1.2f, depth);
            Color att = DepthAttenuation(depth);

            // ---- main light: the sun by day, the moon at night (it then also lights the skybox -> navy night sky)
            bool wantMoon = elev < MoonTakeover;
            if (wantMoon != moonMain)
            {
                moonMain = wantMoon;
                sun.enabled = !wantMoon;
            }
            var mainLight = moonMain ? moon : sun;
            if (RenderSettings.sun != mainLight) RenderSettings.sun = mainLight;

            // ---- sun
            Color sunCol = Eval(SunKeys, SunColors, elev);
            sun.color = new Color(sunCol.r * att.r, sunCol.g * att.g, sunCol.b * att.b, 1f);
            sun.intensity = Mathf.Max(0.002f, 1.55f * sun01);
            sun.shadowStrength = Mathf.Lerp(0.45f, 0.62f, sun01);
            var wantShadows = sun01 > 0.02f && depth < 60f ? LightShadows.Soft : LightShadows.None;
            if (wantShadows != sunShadowState) { sun.shadows = wantShadows; sunShadowState = wantShadows; }

            // ---- moon
            float moonI = 0.38f * night01 * SmoothStep(-2f, 10f, moonElev);
            bool moonOn = moonMain || moonI > 0.001f;
            if (moon.enabled != moonOn) moon.enabled = moonOn;
            if (moonOn)
            {
                moon.intensity = Mathf.Max(0.002f, moonI);
                moon.color = new Color(0.62f * att.r, 0.72f * att.g, 1f * att.b, 1f);
            }

            // ---- ambient (trilight, soft and bright: shadows are never black)
            Color aSky = Eval(AmbKeys, AmbSky, elev), aEq = Eval(AmbKeys, AmbEquator, elev), aGr = Eval(AmbKeys, AmbGround, elev);
            Color rawSky = aSky;
            float dayLevel = Mathf.Lerp(0.05f, 1f, sun01);
            aSky = Color.Lerp(aSky, WaterSky * dayLevel, uw) * att;
            aEq = Color.Lerp(aEq, WaterEquator * dayLevel, uw) * att;
            aGr = Color.Lerp(aGr, WaterGround * dayLevel, uw) * att;
            RenderSettings.ambientSkyColor = aSky;
            RenderSettings.ambientEquatorColor = aEq;
            RenderSettings.ambientGroundColor = aGr;

            // ---- environment facts for FX / filming / fauna
            EnvState.SkyFogColor = Eval(FogKeys, FogColors, elev);
            EnvState.SunIntensity01 = sun01;
            EnvState.Night01 = night01;

            // ---- skybox
            if (sky != null)
            {
                if (moonMain)
                {
                    float m = 0.2f + 0.8f * SmoothStep(4f, 20f, moonElev);
                    sky.SetFloat(IdExposure, NightExposure * m);
                    sky.SetFloat(IdThickness, NightThickness);
                    sky.SetColor(IdSkyTint, NightTint);
                    sky.SetColor(IdGround, SkyGroundNight);
                    sky.SetFloat(IdSunSize, 0.03f);
                    sky.SetFloat(IdSunConv, 6f);
                }
                else
                {
                    sky.SetFloat(IdExposure, Eval(SkyKeys, Exposure, elev));
                    sky.SetFloat(IdThickness, Eval(SkyKeys, Thickness, elev));
                    sky.SetColor(IdSkyTint, Eval(SkyKeys, SkyTint, elev));
                    sky.SetColor(IdGround, Color.Lerp(SkyGroundNight, SkyGroundDay, sun01));
                    sky.SetFloat(IdSunSize, Eval(SkyKeys, SunSize, elev));
                    sky.SetFloat(IdSunConv, Eval(SkyKeys, SunConv, elev));
                }
            }

            // ---- reflections: refresh the sky probe when the sky changed noticeably
            float reflect = Mathf.Lerp(1f, 0.25f * (att.g + att.b) * 0.5f, uw);
            RenderSettings.reflectionIntensity = reflect;
            float now = Time.unscaledTime;
            if (probe != null)
            {
                probe.intensity = reflect;
                bool changed = Mathf.Abs(elev - lastProbeElev) > 0.5f || Mathf.Abs(uw - lastProbeUw) > 0.5f || moonMain != lastProbeMoon;
                if (changed && now - lastProbeTime > 1.5f)
                {
                    probe.RenderProbe();
                    lastProbeTime = now; lastProbeElev = elev; lastProbeUw = uw; lastProbeMoon = moonMain;
                }
            }
            if (now - lastGiTime > 6f)
            {
                DynamicGI.UpdateEnvironment();
                lastGiTime = now;
            }

            // ---- stars and moon disc (unlit meshes are fogged by the FX fog: compensate so they stay readable)
            float vis = FogVisibility(DomeRadius(c) * 0.92f);
            float hide = (1f - 0.85f * uw) * SmoothStep(0.08f, 0.2f, vis);
            float boost = Mathf.Min(1f / Mathf.Max(vis, 0.05f), 4f);

            float starA = night01 * hide;
            SetLayer(starR, starMat, new Color(boost, boost, boost, starA));
            float moonA = SmoothStep(-4f, 2f, moonElev) * Mathf.Lerp(0.35f, 1f, night01) * hide;
            SetLayer(moonR, moonMat, new Color(boost, 0.99f * boost, 0.96f * boost, moonA));

            // ---- clouds: lit like the world, plus a soft glow floor from the sky so shaded sides stay fluffy
            if (clouds != null && c != null)
            {
                // (emission is given in gamma space: ~0.8 here is ~0.6 linear, enough for soft light-grey undersides)
                Color glow = rawSky * (0.85f * (1f - 0.8f * night01)) + new Color(0.02f, 0.025f, 0.04f);
                glow.a = 1f;
                clouds.Tick(c.transform.position, Time.deltaTime, uw < 0.5f, glow);
            }
        }

        /// Fraction of a surface's own color that survives the current fog at a distance (1 = no fog).
        /// Only reads RenderSettings fog (owned by the FX module).
        static float FogVisibility(float dist)
        {
            if (!RenderSettings.fog) return 1f;
            switch (RenderSettings.fogMode)
            {
                case FogMode.Linear:
                {
                    float s = RenderSettings.fogStartDistance, e = RenderSettings.fogEndDistance;
                    return e > s ? Mathf.Clamp01((e - dist) / (e - s)) : 1f;
                }
                case FogMode.Exponential:
                    return Mathf.Exp(-RenderSettings.fogDensity * dist);
                default:
                {
                    float k = RenderSettings.fogDensity * dist;
                    return Mathf.Exp(-k * k);
                }
            }
        }

        /// Radius of the camera-centered star/moon dome: far enough to sit behind nearly all geometry,
        /// near enough that the haze does not wash it out.
        static float DomeRadius(Camera c)
        {
            if (c == null) return 600f;
            return Mathf.Clamp(c.farClipPlane * 0.85f, 250f, 650f);
        }

        static void SetLayer(MeshRenderer r, Material m, Color c)
        {
            if (r == null || m == null) return;
            bool on = c.a > 0.004f;
            if (r.enabled != on) r.enabled = on;
            if (on) m.SetColor(IdBaseColor, c);
        }

        void FollowCamera()
        {
            var c = ActiveCamera();
            if (c == null) return;
            Vector3 p = c.transform.position;
            float R = DomeRadius(c);
            if (starR != null)
            {
                var t = starR.transform;
                t.position = p;
                t.localScale = new Vector3(R, R, R);
            }
            if (moonR != null)
            {
                var t = moonR.transform;
                float d = R * 0.92f;
                t.position = p + moonDir * d;
                t.rotation = Quaternion.LookRotation(moonDir, Vector3.up);
                float s = d * 0.075f;
                t.localScale = new Vector3(s, s, s);
            }
        }

        void OnDestroy()
        {
            clouds?.Destroy();
        }

        // ------------------------------------------------------------------ helpers
        static float SmoothStep(float e0, float e1, float x)
        {
            float t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        static int Segment(float[] keys, float x, out float f)
        {
            if (x <= keys[0]) { f = 0f; return 0; }
            int last = keys.Length - 1;
            if (x >= keys[last]) { f = 1f; return last - 1; }
            for (int i = 0; i < last; i++)
            {
                if (x <= keys[i + 1])
                {
                    float t = (x - keys[i]) / (keys[i + 1] - keys[i]);
                    f = t * t * (3f - 2f * t);
                    return i;
                }
            }
            f = 1f;
            return last - 1;
        }

        /// Like Segment but with a linear blend (depth gradients read more even without easing).
        static int Linear(float[] keys, float x, out float f)
        {
            if (x <= keys[0]) { f = 0f; return 0; }
            int last = keys.Length - 1;
            if (x >= keys[last]) { f = 1f; return last - 1; }
            for (int i = 0; i < last; i++)
            {
                if (x <= keys[i + 1])
                {
                    f = (x - keys[i]) / (keys[i + 1] - keys[i]);
                    return i;
                }
            }
            f = 1f;
            return last - 1;
        }

        static Color Eval(float[] keys, Color[] values, float x)
        {
            int i = Segment(keys, x, out float f);
            return Color.Lerp(values[i], values[i + 1], f);
        }

        static float Eval(float[] keys, float[] values, float x)
        {
            int i = Segment(keys, x, out float f);
            return Mathf.Lerp(values[i], values[i + 1], f);
        }
    }
}
