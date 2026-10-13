using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Global post-processing volume + the only writer of RenderSettings.fog* and the main camera clear flags/background.
    ///
    /// "How to Fish" grading (art bible §1.5): medium-key and filmic instead of pastel-washed. ACES tonemapping with a
    /// +0.45 exposure lift, contrast +12, saturation +8, a neutral white balance, mildly cool shadows and warm
    /// highlights (shadows/midtones/highlights), a dark-violet vignette 0.28 and a soft bloom on real highlights only.
    /// Golden hour: warmer and more saturated. Night: moonlit and readable (exposure +0.25, cooler, a small shadow
    /// lift). No film grain, no DoF, no motion blur; chromatic aberration only for narcosis, lens distortion only
    /// for the splash and narcosis.
    ///
    /// Fog: above water a light exponential haze in the sky-horizon colour (21 % at 150 m, the landmark islands stay
    /// visible); under water exponential-squared fog in bright turquoise that limits the sight to ~19 m in the
    /// shallows and ~8 m in the deep (§2.1/§2.2). The mode switches while the lens crosses the surface (the splash
    /// hides it). Entering the water never looks dark: for 1.5 s the fog stays at least as bright as the shallow
    /// stop, the splash vignette is capped at 0.15.
    sealed class FXPostProcess
    {
        readonly GameObject go;
        readonly VolumeProfile profile;
        readonly Tonemapping tonemap;
        readonly Bloom bloom;
        readonly ColorAdjustments color;
        readonly Vignette vignette;
        readonly LensDistortion lens;
        readonly ChromaticAberration chroma;
        readonly WhiteBalance white;
        readonly ShadowsMidtonesHighlights smh;

        // ---- above water, day (art bible §1.5)
        // Art polish pass: the bible's white balance (-3 / +5) and SMH trackballs were measured to push the whole frame
        // violet (URP converts the trackballs gamma→linear, so 0.95/1.07 became 0.89/1.16 on every dark tone; the
        // sky measured 10° toward violet, palm trunks magenta, mornings lavender). Neutral white balance, a mild
        // cool-violet shadow tint and warm highlights now give How to Fish's clean blue sky and warm sand.
        const float DayExposure = 0.38f;   // (0.45 read pastel: lit grass/wood ~1.3× brighter than How to Fish) – keep SkyGrade.Exposure equal
        const float DayContrast = 12f;
        const float DaySaturation = 8f;
        const float DayTemperature = 0f;
        const float DayTint = 0f;
        // time-of-day deltas: golden hour warmer (orange, not magenta), night cooler but less saturated (navy, not royal blue)
        const float GoldenSaturation = 6f, GoldenTemperature = 14f, GoldenTint = -4f;
        // Q41: the main menu's golden hour is warmer still (temperature +18 in total), a touch more saturated, with
        // violet shadows (Testversion 3: "menu and evening read cool instead of golden")
        const float MenuTemperature = 4f, MenuSaturation = 3f;
        static readonly Vector4 SmhShadowsMenu = new Vector4(0.98f, 0.965f, 1.065f, 0f);
        const float NightSaturation = -20f, NightTemperature = -4f, NightExposure = 0.30f;
        const float NightShadowLift = 0.03f;   // moonlit nights stay readable navy, never black
        const float AboveVignette = 0.28f;

        const float BloomThreshold = 1.0f;
        const float BloomIntensity = 0.45f;
        const float BloomScatter = 0.70f;

        static readonly Color BloomTint = FXMath.Hex(0xFFF3E4);
        static readonly Color VignetteAboveColor = FXMath.Hex(0x140E26);
        static readonly Color VignetteUnderColor = FXMath.Hex(0x03203A);
        static readonly Color WarnRed = new Color(0.72f, 0.04f, 0.03f, 1f);
        static readonly Vector4 SmhNeutral = new Vector4(1f, 1f, 1f, 0f);
        static readonly Vector4 SmhShadows = new Vector4(0.99f, 0.985f, 1.04f, 0f);   // cool grey shadows on sand (measured warm #8B8477)
        static readonly Vector4 SmhHighlights = new Vector4(1.012f, 1.00f, 0.985f, 0f);

        bool fogInit;
        float menu01 = 1f;
        bool bankColorReady;
        Color bankColor;
        FogMode fogMode = FogMode.ExponentialSquared;
        Color lastFogColor = new Color(-1f, -1f, -1f, -1f);
        float lastFogDensity = -1f;

        public FXPostProcess(Transform parent)
        {
            go = new GameObject("FX_GlobalVolume");
            go.transform.SetParent(parent, false);
            go.layer = 0;   // Default layer: included in the camera's default volume layer mask

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "FX_Profile";

            tonemap = profile.Add<Tonemapping>(true);
            bloom = profile.Add<Bloom>(true);
            color = profile.Add<ColorAdjustments>(true);
            vignette = profile.Add<Vignette>(true);
            lens = profile.Add<LensDistortion>(true);
            chroma = profile.Add<ChromaticAberration>(true);
            white = profile.Add<WhiteBalance>(true);
            smh = profile.Add<ShadowsMidtonesHighlights>(true);

            // Pinned to 0 (= inactive, no cost) so a project default volume can never sneak them back in.
            var grain = profile.Add<FilmGrain>(true);
            if (grain != null) grain.intensity.Override(0f);
            var motion = profile.Add<MotionBlur>(true);
            if (motion != null) motion.intensity.Override(0f);
            var dof = profile.Add<DepthOfField>(true);
            if (dof != null) dof.mode.Override(DepthOfFieldMode.Off);
            var mixer = profile.Add<ChannelMixer>(true);   // identity: the per-channel light loss comes from the World lighting
            if (mixer != null)
            {
                mixer.redOutRedIn.Override(100f); mixer.redOutGreenIn.Override(0f); mixer.redOutBlueIn.Override(0f);
                mixer.greenOutRedIn.Override(0f); mixer.greenOutGreenIn.Override(100f); mixer.greenOutBlueIn.Override(0f);
                mixer.blueOutRedIn.Override(0f); mixer.blueOutGreenIn.Override(0f); mixer.blueOutBlueIn.Override(100f);
            }

            if (tonemap != null) tonemap.mode.Override(TonemappingMode.ACES);
            if (bloom != null)
            {
                bloom.threshold.Override(BloomThreshold);
                bloom.intensity.Override(BloomIntensity);
                bloom.scatter.Override(BloomScatter);
                bloom.tint.Override(BloomTint);
                bloom.highQualityFiltering.Override(true);
                bloom.downscale.Override(BloomDownscaleMode.Half);
                bloom.maxIterations.Override(6);
                bloom.dirtIntensity.Override(0f);
            }
            if (color != null)
            {
                color.postExposure.Override(DayExposure);
                color.contrast.Override(DayContrast);
                color.saturation.Override(DaySaturation);
                color.hueShift.Override(0f);
                color.colorFilter.Override(Color.white);
            }
            if (vignette != null)
            {
                vignette.color.Override(VignetteAboveColor);
                vignette.center.Override(new Vector2(0.5f, 0.5f));
                vignette.intensity.Override(AboveVignette);
                vignette.smoothness.Override(0.42f);
                vignette.rounded.Override(false);
            }
            if (lens != null)
            {
                lens.intensity.Override(0f);
                lens.xMultiplier.Override(1f);
                lens.yMultiplier.Override(1f);
                lens.center.Override(new Vector2(0.5f, 0.5f));
                lens.scale.Override(1f);
            }
            if (chroma != null) chroma.intensity.Override(0f);
            if (white != null)
            {
                white.temperature.Override(DayTemperature);
                white.tint.Override(DayTint);
            }
            if (smh != null)
            {
                smh.shadows.Override(SmhShadows);
                smh.midtones.Override(SmhNeutral);
                smh.highlights.Override(SmhHighlights);
                smh.shadowsStart.Override(0f);
                smh.shadowsEnd.Override(0.35f);
                smh.highlightsStart.Override(0.55f);
                smh.highlightsEnd.Override(1f);
            }

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
        }

        public void Dispose()
        {
            if (profile == null) return;
            for (int i = 0; i < profile.components.Count; i++)
                if (profile.components[i] != null) Object.Destroy(profile.components[i]);
            Object.Destroy(profile);
        }

        /// Bloom cost by graphics tier: HQ filtering on High, plain on Medium, quarter resolution on Low.
        public void SetTier(GraphicsQuality q)
        {
            if (bloom == null) return;
            bloom.highQualityFiltering.value = q == GraphicsQuality.High;
            bloom.downscale.value = q == GraphicsQuality.Low ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half;
            bloom.maxIterations.value = q == GraphicsQuality.Low ? 4 : 6;
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(FXFrame f)
        {
            ApplyFog(f);

            float u = f.under01;
            float d = f.depth;
            float t = f.time;
            float night = f.night01;
            float golden = f.golden01 * (1f - night);
            menu01 = Mathf.MoveTowards(menu01, f.inGame ? 0f : 1f, Mathf.Min(Time.unscaledDeltaTime, 0.1f) / 1.5f);
            float menuGold = menu01 * golden;

            // above water: filmic day, warm golden hour (warmer in the menu), moonlit night
            float aExp = DayExposure + NightExposure * night;
            float aSat = DaySaturation + GoldenSaturation * golden + MenuSaturation * menuGold + NightSaturation * night;
            float aTemp = DayTemperature + GoldenTemperature * golden + MenuTemperature * menuGold + NightTemperature * night;
            float aTint = DayTint + GoldenTint * golden;

            // under water by depth: vivid bright shallows, calmer and bluer in the deep (§2.2). The shallow exposure is
            // +0.40 instead of the bible's +0.35: URP's ACES fit lands the #5ECAD9 background ~12 below target
            // otherwise, and feedback #5 asks for bright, readable shallows.
            float deep = FXMath.SmoothRange(0f, 60f, d);
            // the shallows read milky through the ACES shoulder (sand #CCCAA5 instead of #DCCB94): a lower lift and
            // more saturation keep sand and corals vivid; the fog stops are pre-compensated for this grade
            float uExp = Mathf.Lerp(0.28f, 0.10f, FXMath.SmoothRange(8f, 40f, d)) + 0.15f * night;
            float uSat = Mathf.Lerp(20f, -16f, FXMath.SmoothRange(10f, 60f, d)) - 6f * night;
            float uContrast = Mathf.Lerp(6f, 0f, deep);
            float uTemp = Mathf.Lerp(1f, -9f, deep);
            float uVig = Mathf.Lerp(0.30f, 0.40f, deep);

            float narc = f.narcosis01;
            float faint = f.faint01;

            float sat = Mathf.Lerp(aSat, uSat, u) + 12f * narc - 50f * faint;
            float exposure = Mathf.Lerp(aExp, uExp, u) - 1.4f * faint;
            float contrast = Mathf.Lerp(DayContrast, uContrast, u);
            float hue = 7f * narc * Mathf.Sin(t * 0.37f);
            float temp = Mathf.Lerp(aTemp, uTemp, u);
            float tint = Mathf.Lerp(aTint, 0f, u);

            // warning: pulsing red screen edge (too deep for the gear, or low air)
            float pulseHz = f.air01 < 0.1f ? 2.1f : 1.25f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(f.unscaledTime * FXMath.TwoPi * pulseHz);
            float w = f.warn01 * pulse;
            float vig = Mathf.Lerp(AboveVignette, uVig, u) + 0.16f * w + 0.15f * f.splash01 + 0.45f * faint;
            Color vigColor = Color.Lerp(Color.Lerp(VignetteAboveColor, VignetteUnderColor, u), WarnRed, w);

            // lens distortion only for the splash bulge and narcosis; chromatic aberration only for narcosis
            float wob = Mathf.Sin(t * 0.83f) * 0.55f + Mathf.Sin(t * 1.37f + 1.1f) * 0.3f + Mathf.Sin(t * 2.3f + 2.3f) * 0.15f;
            float lensI = wob * 0.26f * narc - 0.15f * f.splash01;
            float lensX = 1f - 0.3f * narc * (0.5f + 0.5f * Mathf.Sin(t * 0.61f));
            float lensY = 1f - 0.3f * narc * (0.5f + 0.5f * Mathf.Sin(t * 0.47f + 1.3f));
            float ca = 0.62f * narc;

            // violet-tinted shadows above water, neutral under water; a small lift keeps moonlit nights readable
            float above = 1f - u;
            Vector4 shadows = Vector4.Lerp(SmhNeutral, Vector4.Lerp(SmhShadows, SmhShadowsMenu, menuGold), above);
            shadows.w = NightShadowLift * night * above;
            Vector4 highlights = Vector4.Lerp(SmhNeutral, SmhHighlights, above);

            if (color != null)
            {
                SetF(color.saturation, Mathf.Clamp(sat, -100f, 100f), 0.05f);
                SetF(color.postExposure, exposure, 0.003f);
                SetF(color.contrast, contrast, 0.05f);
                SetF(color.hueShift, hue, 0.05f);
            }
            if (white != null)
            {
                SetF(white.temperature, temp, 0.05f);
                SetF(white.tint, tint, 0.05f);
            }
            if (smh != null)
            {
                SetV(smh.shadows, shadows, 0.001f);
                SetV(smh.highlights, highlights, 0.001f);
            }
            if (vignette != null)
            {
                SetF(vignette.intensity, Mathf.Clamp01(vig), 0.002f);
                SetC(vignette.color, vigColor, 0.004f);
            }
            if (lens != null)
            {
                SetF(lens.intensity, Mathf.Clamp(lensI, -1f, 1f), 0.0015f);
                SetF(lens.xMultiplier, lensX, 0.002f);
                SetF(lens.yMultiplier, lensY, 0.002f);
            }
            if (chroma != null) SetF(chroma.intensity, Mathf.Clamp01(ca), 0.002f);
            if (bloom != null) SetF(bloom.intensity, BloomIntensity + 0.05f * u, 0.005f);
        }

        void ApplyFog(FXFrame f)
        {
            Color uw = FXMath.UnderwaterColor(f.depth, f.night01, f.sun01);
            // entering the water must never look dark: hold the shallow stop for the first 1.5 s
            if (f.underwater && f.sinceSubmerge < 1.5f)
            {
                Color floor = FXMath.UnderwaterColor(0f, f.night01, f.sun01);
                float k = 1f - FXMath.SmoothRange(1.0f, 1.5f, f.sinceSubmerge);
                uw.r = Mathf.Max(uw.r, Mathf.Lerp(uw.r, floor.r, k));
                uw.g = Mathf.Max(uw.g, Mathf.Lerp(uw.g, floor.g, k));
                uw.b = Mathf.Max(uw.b, Mathf.Lerp(uw.b, floor.b, k));
            }
            Color sky = FXMath.GuardSkyFog(EnvState.SkyFogColor, f.sun01);
            float skyDensity = FXMath.SkyFogDensity(f.night01);
            // K.8: inside the fog bank around the Fog Isle the haze turns into thick pale fog (a light haze with the map)
            float bank = FXFogBank.Thick01;
            if (bank > 0.001f)
            {
                if (!bankColorReady) { bankColorReady = true; bankColor = SkyGrade.InverseGamma(FXFogBankRenderer.FogColorScreen); }
                Color bc = bankColor * Mathf.Lerp(0.22f, 1f, f.sun01);
                bc.a = 1f;
                sky = Color.Lerp(sky, bc, bank);
                skyDensity = Mathf.Lerp(skyDensity, FXFogBankRenderer.Density(FXFogBank.Map), bank);
            }
            Color fogColor = Color.Lerp(sky, uw, f.under01);
            fogColor.a = 1f;

            float uwK = FXMath.UnderwaterFogDensity(f.depth, f.night01);
            bool exp2 = f.under01 >= 0.5f;
            float density;
            if (exp2) density = uwK;
            else
            {
                // exponential density with the same visibility as the underwater fog at ~15 m, blended geometrically
                float eq = uwK * uwK * FXMath.FogMatchDistance;
                density = skyDensity * Mathf.Pow(eq / skyDensity, Mathf.Clamp01(f.under01 * 2f));
            }
            FogMode mode = exp2 ? FogMode.ExponentialSquared : FogMode.Exponential;

            if (!fogInit)
            {
                fogInit = true;
                RenderSettings.fog = true;
                RenderSettings.fogMode = mode;
                fogMode = mode;
            }
            else
            {
                if (!RenderSettings.fog) RenderSettings.fog = true;
                if (fogMode != mode || RenderSettings.fogMode != mode)
                {
                    RenderSettings.fogMode = mode;
                    fogMode = mode;
                    lastFogDensity = -1f;
                }
            }

            if (FXMath.Differs(fogColor, lastFogColor, 0.0015f))
            {
                RenderSettings.fogColor = fogColor;
                lastFogColor = fogColor;
            }
            if (Mathf.Abs(density - lastFogDensity) > density * 0.004f)
            {
                RenderSettings.fogDensity = density;
                lastFogDensity = density;
            }
            FXVisibility.Publish(exp2, density, f.underwater);

            Camera cam = f.cam;
            if (cam == null) return;
            var want = f.underwater ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (cam.clearFlags != want) cam.clearFlags = want;
            if (want == CameraClearFlags.SolidColor && FXMath.Differs(cam.backgroundColor, fogColor, 0.0015f))
                cam.backgroundColor = fogColor;
        }

        static void SetF(VolumeParameter<float> p, float v, float eps)
        {
            if (Mathf.Abs(p.value - v) > eps) p.value = v;
        }

        static void SetV(VolumeParameter<Vector4> p, Vector4 v, float eps)
        {
            Vector4 c = p.value;
            if (Mathf.Abs(c.x - v.x) > eps || Mathf.Abs(c.y - v.y) > eps || Mathf.Abs(c.z - v.z) > eps || Mathf.Abs(c.w - v.w) > eps)
                p.value = v;
        }

        static void SetC(VolumeParameter<Color> p, Color c, float eps)
        {
            if (FXMath.Differs(p.value, c, eps)) p.value = c;
        }
    }
}
