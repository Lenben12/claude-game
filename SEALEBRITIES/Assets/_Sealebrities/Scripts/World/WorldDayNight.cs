using UnityEngine;

namespace Sealebrities
{
    /// Day/night cycle. The only writer of sun, moon, skybox, ambient light and reflection intensity.
    /// Publishes EnvState every frame (before other modules update). Below the surface the sun and ambient
    /// light are attenuated per channel with the camera depth (red first), so a flashlight brings colors back.
    [DefaultExecutionOrder(-50)]
    public class WorldDayNight : MonoBehaviour
    {
        public const float MenuTime = 0.70f;
        public const float Sunrise = 0.235f, Sunset = 0.805f;
        const float MaxElevation = 72f, MaxNightDepth = 50f;
        const float RiseAzimuth = 75f, SetAzimuth = 245f;     // compass degrees: rises ENE, sets WSW

        Light sun, moon;
        Material sky, starMat, moonMat, cloudMat;
        ReflectionProbe probe;
        MeshRenderer starR, moonR, cloudR;
        Camera cam;

        float lastProbeTime = -100f, lastProbeElev = -999f, lastProbeUw = -1f, lastGiTime = -100f;
        float cloudSpin;
        LightShadows sunShadowState = LightShadows.Soft;
        Vector3 moonDir = Vector3.up;

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
            new Color(1f, 0.32f, 0.12f), new Color(1f, 0.42f, 0.18f), new Color(1f, 0.58f, 0.32f), new Color(1f, 0.72f, 0.47f),
            new Color(1f, 0.86f, 0.68f), new Color(1f, 0.95f, 0.88f), new Color(1f, 0.98f, 0.95f),
        };

        static readonly float[] AmbKeys = { -18f, -6f, 0f, 12f, 30f, 60f };
        static readonly Color[] AmbSky =
        {
            new Color(0.035f, 0.05f, 0.10f), new Color(0.10f, 0.12f, 0.22f), new Color(0.30f, 0.33f, 0.48f),
            new Color(0.40f, 0.48f, 0.64f), new Color(0.47f, 0.60f, 0.80f), new Color(0.50f, 0.65f, 0.86f),
        };
        static readonly Color[] AmbEquator =
        {
            new Color(0.03f, 0.04f, 0.075f), new Color(0.16f, 0.12f, 0.16f), new Color(0.52f, 0.36f, 0.28f),
            new Color(0.60f, 0.50f, 0.40f), new Color(0.56f, 0.60f, 0.62f), new Color(0.56f, 0.62f, 0.66f),
        };
        static readonly Color[] AmbGround =
        {
            new Color(0.015f, 0.018f, 0.025f), new Color(0.04f, 0.04f, 0.05f), new Color(0.14f, 0.11f, 0.09f),
            new Color(0.19f, 0.16f, 0.13f), new Color(0.23f, 0.22f, 0.19f), new Color(0.24f, 0.23f, 0.20f),
        };

        static readonly float[] FogKeys = { -18f, -6f, 0f, 8f, 18f, 35f, 60f };
        static readonly Color[] FogColors =
        {
            new Color(0.02f, 0.03f, 0.06f), new Color(0.12f, 0.12f, 0.20f), new Color(0.62f, 0.45f, 0.38f), new Color(0.78f, 0.62f, 0.50f),
            new Color(0.78f, 0.74f, 0.68f), new Color(0.66f, 0.77f, 0.86f), new Color(0.62f, 0.75f, 0.86f),
        };

        static readonly float[] SkyKeys = { -18f, -4f, 0f, 6f, 15f, 35f };
        static readonly float[] Exposure = { 0.55f, 0.9f, 1.1f, 1.15f, 1.2f, 1.3f };
        static readonly float[] Thickness = { 1.0f, 1.2f, 1.4f, 1.3f, 1.12f, 0.9f };
        static readonly float[] SunSize = { 0.06f, 0.06f, 0.06f, 0.055f, 0.045f, 0.035f };
        static readonly float[] SunConv = { 3f, 3f, 3f, 3.5f, 4.5f, 6f };
        static readonly Color[] SkyTint =
        {
            new Color(0.32f, 0.38f, 0.6f), new Color(0.42f, 0.42f, 0.55f), new Color(0.52f, 0.45f, 0.5f),
            new Color(0.5f, 0.47f, 0.52f), new Color(0.46f, 0.5f, 0.6f), new Color(0.43f, 0.52f, 0.64f),
        };

        // underwater scattered light (multiplied by daylight and depth attenuation)
        static readonly Color WaterSky = new Color(0.16f, 0.42f, 0.52f);
        static readonly Color WaterEquator = new Color(0.08f, 0.28f, 0.36f);
        static readonly Color WaterGround = new Color(0.03f, 0.10f, 0.13f);

        public void Init(Light sun, Light moon, Material sky, ReflectionProbe probe,
                         MeshRenderer stars, Material starMat, MeshRenderer moonDisc, Material moonMat,
                         MeshRenderer clouds, Material cloudMat)
        {
            InitIds();
            this.sun = sun; this.moon = moon; this.sky = sky; this.probe = probe;
            starR = stars; this.starMat = starMat;
            moonR = moonDisc; this.moonMat = moonMat;
            cloudR = clouds; this.cloudMat = cloudMat;
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

            // ---- camera depth -> per-channel light attenuation (red disappears first)
            float depth = 0f;
            var c = ActiveCamera();
            if (c != null)
            {
                Vector3 p = c.transform.position;
                depth = Mathf.Max(0f, Waves.HeightAt(p.x, p.z) - p.y);
            }
            CameraDepth = depth;
            float uw = SmoothStep(0f, 1.2f, depth);
            float dark = 1f - 0.94f * Mathf.Clamp01((depth - 12f) / 85f);
            var att = new Color(Mathf.Exp(-depth / 9f) * dark, Mathf.Exp(-depth / 45f) * dark, Mathf.Exp(-depth / 70f) * dark, 1f);

            // ---- sun
            Color sunCol = Eval(SunKeys, SunColors, elev);
            sun.color = new Color(sunCol.r * att.r, sunCol.g * att.g, sunCol.b * att.b, 1f);
            sun.intensity = Mathf.Max(0.002f, 1.75f * sun01);     // never 0: the sun must stay URP's main light
            sun.shadowStrength = Mathf.Lerp(0.6f, 0.88f, sun01);
            var wantShadows = sun01 > 0.02f && depth < 60f ? LightShadows.Soft : LightShadows.None;
            if (wantShadows != sunShadowState) { sun.shadows = wantShadows; sunShadowState = wantShadows; }

            // ---- moon
            float moonI = 0.32f * night01 * SmoothStep(-2f, 10f, moonElev);
            bool moonOn = moonI > 0.001f;
            if (moon.enabled != moonOn) moon.enabled = moonOn;
            if (moonOn)
            {
                moon.intensity = moonI;
                moon.color = new Color(0.55f * att.r, 0.66f * att.g, 1f * att.b, 1f);
            }

            // ---- ambient (trilight)
            Color aSky = Eval(AmbKeys, AmbSky, elev), aEq = Eval(AmbKeys, AmbEquator, elev), aGr = Eval(AmbKeys, AmbGround, elev);
            Color rawSky = aSky;
            float dayLevel = Mathf.Lerp(0.06f, 1f, sun01);
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
                sky.SetFloat(IdExposure, Eval(SkyKeys, Exposure, elev));
                sky.SetFloat(IdThickness, Eval(SkyKeys, Thickness, elev));
                sky.SetColor(IdSkyTint, Eval(SkyKeys, SkyTint, elev));
                sky.SetColor(IdGround, Color.Lerp(new Color(0.02f, 0.03f, 0.05f), new Color(0.15f, 0.25f, 0.31f), sun01));
                sky.SetFloat(IdSunSize, Eval(SkyKeys, SunSize, elev));
                sky.SetFloat(IdSunConv, Eval(SkyKeys, SunConv, elev));
            }

            // ---- reflections: refresh the sky probe when the sky changed noticeably
            float reflect = Mathf.Lerp(1f, 0.25f * (att.g + att.b) * 0.5f, uw);
            RenderSettings.reflectionIntensity = reflect;
            float now = Time.unscaledTime;
            if (probe != null)
            {
                probe.intensity = reflect;
                bool changed = Mathf.Abs(elev - lastProbeElev) > 0.5f || Mathf.Abs(uw - lastProbeUw) > 0.5f;
                if (changed && now - lastProbeTime > 1.5f)
                {
                    probe.RenderProbe();
                    lastProbeTime = now; lastProbeElev = elev; lastProbeUw = uw;
                }
            }
            if (now - lastGiTime > 6f)
            {
                DynamicGI.UpdateEnvironment();
                lastGiTime = now;
            }

            // ---- sky layers (unlit meshes are fogged by the FX fog: compensate so they stay readable)
            float vis = FogVisibility(DomeRadius(c) * 0.92f);
            float hide = (1f - 0.85f * uw) * SmoothStep(0.08f, 0.2f, vis);
            float boost = Mathf.Min(1f / Mathf.Max(vis, 0.05f), 4f);

            float starA = night01 * hide;
            SetLayer(starR, starMat, new Color(boost, boost, boost, starA));
            float moonA = SmoothStep(-4f, 2f, moonElev) * Mathf.Lerp(0.3f, 1f, night01) * hide;
            SetLayer(moonR, moonMat, new Color(boost, 0.98f * boost, 0.94f * boost, moonA));

            Color cl = sunCol * (1.05f * sun01) + rawSky * 0.6f + new Color(0.05f, 0.06f, 0.09f) * night01;
            cl.r = Mathf.Min(cl.r, 1.1f); cl.g = Mathf.Min(cl.g, 1.1f); cl.b = Mathf.Min(cl.b, 1.15f);
            Color fogCol = RenderSettings.fog ? RenderSettings.fogColor : Color.black;
            float inv = 1f / Mathf.Max(vis, 0.05f);
            var clBase = new Color(
                Mathf.Clamp((cl.r - (1f - vis) * fogCol.r) * inv, 0f, 3f),
                Mathf.Clamp((cl.g - (1f - vis) * fogCol.g) * inv, 0f, 3f),
                Mathf.Clamp((cl.b - (1f - vis) * fogCol.b) * inv, 0f, 3f),
                0.95f * (1f - 0.6f * uw) * SmoothStep(0.08f, 0.2f, vis));
            SetLayer(cloudR, cloudMat, clBase);
            cloudSpin += Time.deltaTime * 0.12f;
            if (cloudSpin > 360f) cloudSpin -= 360f;
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

        /// Radius of the camera-centered star/cloud dome: far enough to sit behind nearly all geometry,
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
            if (cloudR != null)
            {
                var t = cloudR.transform;
                t.position = p;
                t.rotation = Quaternion.AngleAxis(cloudSpin, Vector3.up);
                float r = R * 0.97f;
                t.localScale = new Vector3(r, r, r);
            }
            if (moonR != null)
            {
                var t = moonR.transform;
                float d = R * 0.92f;
                t.position = p + moonDir * d;
                t.rotation = Quaternion.LookRotation(moonDir, Vector3.up);
                float s = d * 0.06f;
                t.localScale = new Vector3(s, s, s);
            }
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
