using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Day/night cycle. The only writer of sun, moon, sky, ambient light and reflection intensity.
    /// Publishes EnvState every frame (before other modules update). "How to Fish" look (art bible §1.2/1.3/2.1):
    /// a real mid-blue sky with a saturated horizon, a 58° sun with strong cool-violet shadows (shadow strength 0.9,
    /// darker bluish trilight ambient), golden hour at ~8°, bright moonlit nights. Below the surface sun and ambient are
    /// attenuated per channel with the camera depth: bright turquoise in the shallows, dark blue at 50-60 m, near black
    /// beyond 80 m.
    [DefaultExecutionOrder(-50)]
    public class WorldDayNight : MonoBehaviour
    {
        /// Main menu time of day: low golden-hour sun (≈ 6.6°, 17:23; Q41 "warmer menu" – the Testversion-3 menu at 8°
        /// read cool). The warm menu look itself (peach horizon, warmer sun and grading) is MenuWarm below + FX.
        public const float MenuTime = 0.724f;
        /// Q41 menu look: horizon away from the sun turns golden peach (▣ #E7B98A), the sun a little more orange.
        static readonly Color MenuSun = Hex("#FFB680");
        public const float Sunrise = 0.235f, Sunset = 0.805f;
        public const float MaxElevation = 58f;
        const float MaxNightDepth = 50f;
        const float RiseAzimuth = 75f, SetAzimuth = 245f;     // compass degrees: rises ENE, sets WSW
        const float MoonTakeover = -7f;                       // sun elevation below which the moon is the main light

        Light sun, moon;
        Material sky, starMat, moonMat;
        WorldSkyDome dome;
        ReflectionProbe probe;
        MeshRenderer starR, moonR;
        WorldClouds clouds;
        Camera cam;

        float lastProbeTime = -100f, lastProbeElev = -999f, lastProbeUw = -1f, lastGiTime = -100f;
        bool lastProbeMoon;
        Color lastProbeColor;
        LightShadows sunShadowState = LightShadows.Soft;
        Vector3 moonDir = Vector3.up, sunDirection = Vector3.up;
        bool moonMain, subscribed;
        float sunAzimuth;
        float menuWarm = 1f;

        /// Time of day currently shown (0..1).
        public float TimeOfDay { get; private set; }
        /// Sun elevation in degrees (negative at night).
        public float SunElevation { get; private set; }
        /// Camera depth below the surface used for light attenuation (0 above water).
        public float CameraDepth { get; private set; }
        /// The sky dome (null-safe: Valid is false when the fallback skybox is used).
        public WorldSkyDome Dome => dome;
        /// The cumulus layer (round 3: weather-driven, drifting, coloured by the time of day).
        public WorldClouds Clouds => clouds;

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

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ------------------------------------------------------------------ keyframes (by sun elevation in degrees)
        // sun colour and intensity (art bible §1.2): warm white by day, peach at 12°, orange at 5°, coral at sunset
        static readonly float[] SunKeys = { -3f, 0f, 2f, 5f, 10f, 12f, 30f };
        static readonly Color[] SunColors =
        {
            Hex("#FF9E6A"), Hex("#FF9E6A"), Hex("#FFB37C"), Hex("#FFC58A"), Hex("#FFD6A6"), Hex("#FFDDB0"), Hex("#FFF2DE"),
        };
        static readonly float[] SunIntensity = { 0f, 0.35f, 0.70f, 0.90f, 1.15f, 1.19f, 1.35f };

        // ambient trilight – reduced and cool so shadows read strong and violet (art bible §1.3)
        static readonly float[] AmbKeys = { -12f, -4f, 0f, 10f, 30f };
        // (art polish: the dawn / golden keys are bluer-neutral instead of lavender – at wake-up time the hut interior is
        // lit almost only by this and read purple; How to Fish mornings are warm sand + blue sky, not violet)
        // (noon key a touch bluer so shadows on sand read cool grey like How to Fish, not warm brown; night keys
        // brighter: the moonlit island measured grass #1B2F2F / sea #0A255D against #2F4A3A / #2C4F80)
        static readonly Color[] AmbSky = { Hex("#33447A"), Hex("#48528A"), Hex("#6A72A2"), Hex("#8494BE"), Hex("#7C96C8") };
        static readonly Color[] AmbEquator = { Hex("#2A3658"), Hex("#545672"), Hex("#8A7E80"), Hex("#A89C8E"), Hex("#8A8EA6") };
        static readonly Color[] AmbGround = { Hex("#161C32"), Hex("#2C2C3C"), Hex("#463C46"), Hex("#5E5050"), Hex("#5E5662") };
        /// The sky probe mostly feeds the sea's Fresnel reflection: at full strength the sea 50-150 m away read
        /// #8AB3DE instead of How to Fish's #4F7FAE, so the probe is dimmed above water (glints come from the sun).
        const float ProbeAboveWater = 0.72f;   // (0.55 made the horizon band darker than the near sea)

        // fallback procedural skybox (only when the dome is unavailable)
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
        static readonly Color NightTint = new Color(0.28f, 0.38f, 0.75f);
        const float NightThickness = 0.7f, NightExposure = 0.45f;

        // underwater: per-channel light left at a camera depth (red fades first, but the shallows stay sand-coloured)
        static readonly float[] DepthKeys = { 0f, 4f, 10f, 18f, 25f, 40f, 55f, 80f, 120f };
        // (the shallows keep more red: the reef sand measured grey-green #B4BE99 instead of the warm #DCCB94 target)
        static readonly float[] AttR = { 1f, 0.97f, 0.86f, 0.64f, 0.48f, 0.24f, 0.11f, 0.035f, 0.012f };
        static readonly float[] AttG = { 1f, 0.98f, 0.93f, 0.82f, 0.70f, 0.47f, 0.27f, 0.085f, 0.025f };
        static readonly float[] AttB = { 1f, 1f, 0.98f, 0.91f, 0.82f, 0.60f, 0.38f, 0.13f, 0.04f };

        // underwater ambient (art bible §2.1): bright saturated teal in the shallows, scaled down with depth
        // (a touch less cyan than the bible's first values so the warm sun keeps the sand sandy, not mint)
        static readonly Color WaterSky = Hex("#8EC2D8");
        static readonly Color WaterEquator = Hex("#6AA8C4");
        static readonly Color WaterGround = Hex("#46829A");
        // (round 2, Q10 residual light: the trench and the wreck read pitch black, even the hands – the scale never drops
        // below ×0.10 at 80 m, ×0.07 at 120 m and ×0.05 at 160 m of the 0–3 m stop, so seabed and silhouettes stay readable)
        static readonly float[] UwAmbKeys = { 0f, 3f, 10f, 18f, 25f, 40f, 55f, 80f, 120f, 160f };
        static readonly float[] UwAmbScale = { 1f, 1f, 0.85f, 0.70f, 0.55f, 0.36f, 0.21f, 0.10f, 0.07f, 0.05f };

        public void Init(Light sun, Light moon, Material sky, WorldSkyDome dome, ReflectionProbe probe,
                         MeshRenderer stars, Material starMat, MeshRenderer moonDisc, Material moonMat, WorldClouds clouds)
        {
            InitIds();
            this.sun = sun; this.moon = moon; this.sky = sky; this.dome = dome; this.probe = probe;
            starR = stars; this.starMat = starMat;
            moonR = moonDisc; this.moonMat = moonMat;
            this.clouds = clouds;
            Subscribe();
        }

        /// Applies the lighting immediately (used right after building so the first rendered frame is lit).
        public void ApplyNow()
        {
            Apply();
            FollowCamera();
        }

        void OnEnable() { if (sun != null) Subscribe(); }
        void OnDisable() { Unsubscribe(); }

        void Subscribe()
        {
            if (subscribed) return;
            subscribed = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void Unsubscribe()
        {
            if (!subscribed) return;
            subscribed = false;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        /// Every game camera (player view, photo camera, intro shots) gets the sky dome centred on itself.
        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (dome == null || !dome.Valid || c == null) return;
            if (c.cameraType != CameraType.Game && c.cameraType != CameraType.SceneView) return;
            Vector3 p = c.transform.position;
            bool above = p.y > Waves.HeightAt(p.x, p.z) - 0.02f;
            dome.Follow(p, above && (c.cullingMask & 1) != 0);
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
                float s = Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(f)));   // sin(PI) is -8.7e-8 in float: Pow would give NaN at exactly Sunset
                // mornings brighten quickly; evenings keep a long, low golden hour; never overhead (side light)
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
            bool inGame = GameManager.I != null && GameManager.I.InGame;
            float t = inGame ? GameManager.I.State.timeOfDay : MenuTime;
            // Q41: the menu gets an extra warm golden look (fades in/out over ~1.5 s when a game starts / ends)
            menuWarm = Mathf.MoveTowards(menuWarm, inGame ? 0f : 1f, Mathf.Min(Time.unscaledDeltaTime, 0.1f) / 1.5f);
            t -= Mathf.Floor(t);
            TimeOfDay = t;
            SunPosition(t, out float elev, out float az);
            SunElevation = elev;
            sunAzimuth = az;

            Vector3 sunDir = Direction(elev, az);
            sunDirection = sunDir;
            sun.transform.rotation = Quaternion.LookRotation(-sunDir, Vector3.up);
            moonDir = -sunDir;
            float moonElev = -elev;
            moon.transform.rotation = Quaternion.LookRotation(sunDir, Vector3.up);

            float sunI = Eval(SunKeys, SunIntensity, elev);
            float sun01 = Mathf.Clamp01(sunI / 1.35f);
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

            // ---- main light: the sun by day, the moon at night
            bool wantMoon = elev < MoonTakeover;
            if (wantMoon != moonMain)
            {
                moonMain = wantMoon;
                sun.enabled = !wantMoon;
            }
            var mainLight = moonMain ? moon : sun;
            if (RenderSettings.sun != mainLight) RenderSettings.sun = mainLight;

            // ---- sun: strong, crisp shadows by day (the shadow colour comes from the cool ambient)
            Color sunCol = Eval(SunKeys, SunColors, elev);
            float warm = menuWarm * (1f - SmoothStep(12f, 24f, elev)) * SmoothStep(-3f, 1f, elev);
            if (warm > 0f) sunCol = Color.Lerp(sunCol, MenuSun, 0.45f * warm);
            sun.color = new Color(sunCol.r * att.r, sunCol.g * att.g, sunCol.b * att.b, 1f);
            // a little less sun under water keeps the shallow sand out of the ACES shoulder (warm sand, not milky)
            sun.intensity = Mathf.Max(0.002f, sunI * Mathf.Lerp(1f, 0.82f, uw));
            sun.shadowStrength = Mathf.Lerp(0.6f, 0.9f, SmoothStep(0f, 10f, elev)) * Mathf.Lerp(1f, 0.75f, uw);
            var wantShadows = sunI > 0.03f && depth < 60f ? LightShadows.Soft : LightShadows.None;
            if (wantShadows != sunShadowState) { sun.shadows = wantShadows; sunShadowState = wantShadows; }

            // ---- moon: bright, cool moonlight (art bible: 0.45, #A9BCFF)
            float moonI = 0.6f * night01 * SmoothStep(-2f, 12f, moonElev);
            bool moonOn = moonMain || moonI > 0.001f;
            if (moon.enabled != moonOn) moon.enabled = moonOn;
            if (moonOn)
            {
                moon.intensity = Mathf.Max(0.002f, moonI);
                // a little less blue than #A9BCFF: moonlit grass measured cyan-grey (#1B2E2F) instead of green-grey
                moon.color = new Color(0.72f * att.r, 0.80f * att.g, 1f * att.b, 1f);
            }

            // ---- ambient (trilight): darker and bluer above water, bright teal in the shallows
            Color aSky = Eval(AmbKeys, AmbSky, elev), aEq = Eval(AmbKeys, AmbEquator, elev), aGr = Eval(AmbKeys, AmbGround, elev);
            if (warm > 0f)
            {
                // warm bounce light from the golden horizon, the sky part stays blue-violet (violet shadows)
                aEq = Color.Lerp(aEq, new Color(aEq.r * 1.12f, aEq.g * 0.98f, aEq.b * 0.86f, 1f), warm);
                aGr = Color.Lerp(aGr, new Color(aGr.r * 1.1f, aGr.g * 0.96f, aGr.b * 0.9f, 1f), warm);
            }
            if (uw > 0f)
            {
                float day = Mathf.Lerp(0.07f, 1f, sun01) * Eval(UwAmbKeys, UwAmbScale, depth);
                // keep a hint of the per-channel colour shift (red fades first) on top of the teal table
                float nb = Mathf.Max(att.b, 0.01f);
                var tint = new Color(Mathf.Lerp(1f, att.r / nb, 0.5f), Mathf.Lerp(1f, att.g / nb, 0.5f), 1f, 1f);
                aSky = Color.Lerp(aSky, WaterSky * tint * day, uw);
                aEq = Color.Lerp(aEq, WaterEquator * tint * day, uw);
                aGr = Color.Lerp(aGr, WaterGround * tint * day, uw);
            }
            RenderSettings.ambientSkyColor = aSky;
            RenderSettings.ambientEquatorColor = aEq;
            RenderSettings.ambientGroundColor = aGr;

            // ---- sky dome colours, fog colour (= horizon away from the sun × 0.94, pre-compensated for the grade)
            float domeVis = FogVisibility(WorldSkyDome.Radius);
            Color fogLin = RenderSettings.fogColor.linear;
            // K.8: inside the Fog Isle's fog bank the sky disappears in the fog (a light haze once the map is known)
            float haze = FXFogBank.Thick01 * (FXFogBank.Map ? 0.45f : 0.92f);
            if (dome != null)
            {
                // far horizon cloud bands: as many / as grey as the day's cumulus, drifting with the same wind
                if (clouds != null) dome.BandYaw = clouds.BandYaw;
                dome.Update(sunDir, elev, az, domeVis, fogLin, false, uw < 0.5f, warm, haze,
                            clouds != null ? clouds.Cover : 0.35f, clouds != null ? clouds.Grey : 0f);
            }

            // ---- environment facts for FX / filming / fauna
            EnvState.SkyFogColor = dome != null ? dome.FogColor : new Color(0.52f, 0.70f, 0.86f);
            EnvState.SunIntensity01 = sun01;
            EnvState.Night01 = night01;
            WorldVisibility.Publish(depth, night01, uw > 0.5f);
            PropMats.SetDaylight(sun01);

            // ---- fallback skybox
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
                // full sky reflection at golden hour (the menu sea glows warm), dimmed at high sun
                float probeAbove = Mathf.Lerp(0.85f, ProbeAboveWater, SmoothStep(8f, 30f, elev));
                probe.intensity = reflect * Mathf.Lerp(probeAbove, 1f, uw);
                Color pc = dome != null && dome.Valid ? dome.ReflectionColor : probe.backgroundColor;
                bool colorChanged = Mathf.Abs(pc.r - lastProbeColor.r) + Mathf.Abs(pc.g - lastProbeColor.g) + Mathf.Abs(pc.b - lastProbeColor.b) > 0.01f;
                bool changed = colorChanged || Mathf.Abs(elev - lastProbeElev) > 0.5f || Mathf.Abs(uw - lastProbeUw) > 0.5f || moonMain != lastProbeMoon;
                if (changed && now - lastProbeTime > 1.5f)
                {
                    if (dome != null && dome.Valid) probe.backgroundColor = pc;
                    probe.RenderProbe();
                    lastProbeTime = now; lastProbeElev = elev; lastProbeUw = uw; lastProbeMoon = moonMain; lastProbeColor = pc;
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

            // ---- clouds: per-facet colours by sun elevation (white / golden / coral / night blue-grey) and weather,
            // their own horizon haze with the scene fog pre-compensated, drift on the shared game clock
            if (clouds != null && c != null)
            {
                Color hazeLin = (dome != null && dome.Valid ? dome.FogColor : EnvState.SkyFogColor).linear;
                clouds.Tick(c.transform.position, sunDir, elev, uw < 0.5f, hazeLin, FXFogBank.Thick01, Time.deltaTime);
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
            // the gradient dome also follows the main camera here (other cameras re-centre it before they render)
            if (dome != null) dome.Follow(p, p.y > Waves.HeightAt(p.x, p.z) - 0.02f);
        }

        void OnDestroy()
        {
            Unsubscribe();
            clouds?.Destroy();
            dome?.Destroy();
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
