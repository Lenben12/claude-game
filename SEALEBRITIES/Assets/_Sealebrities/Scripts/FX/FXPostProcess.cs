using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Global post-processing volume + the only writer of RenderSettings.fog* and the main camera clear flags/background.
    ///
    /// Low-poly "How to Fish" look: clean and toy-like. Neutral tonemapping keeps the flat LowPoly palette true
    /// (ACES would shift hues and crush the pastels), soft bloom only on real highlights, a little extra saturation
    /// in daylight, no film grain, no chromatic aberration (only the narcosis effect on hard difficulty uses it),
    /// vignette only underwater. Underwater the colour story is told by the fog (distance), not by global grading:
    /// objects near the camera keep their palette colours, the distance fades into bright turquoise in the
    /// shallows, blue at 25 m, dark blue at 50-60 m and near black below 80 m. The World module dims the actual
    /// light with depth, so exposure changes here stay small (no double darkening).
    sealed class FXPostProcess
    {
        readonly GameObject go;
        readonly VolumeProfile profile;
        readonly Tonemapping tonemap;
        readonly Bloom bloom;
        readonly ColorAdjustments color;
        readonly Vignette vignette;
        readonly ChannelMixer mixer;
        readonly LensDistortion lens;
        readonly ChromaticAberration chroma;
        readonly WhiteBalance white;

        static readonly Color WarnRed = new Color(0.72f, 0.04f, 0.03f, 1f);

        // above water
        const float DayExposure = 0.1f;
        const float DaySaturation = 10f;
        const float DayContrast = 8f;
        const float BloomThreshold = 1.3f;   // sunlit white huts/sand (~1.7) only feed a little glow: no milky veil at noon
        const float BloomIntensity = 0.35f;

        bool fogInit;
        Color lastFogColor = new Color(-1f, -1f, -1f, -1f);
        float lastFogDensity = -1f;
        Camera lastCam;

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
            mixer = profile.Add<ChannelMixer>(true);
            lens = profile.Add<LensDistortion>(true);
            chroma = profile.Add<ChromaticAberration>(true);
            white = profile.Add<WhiteBalance>(true);

            // No film grain in the low-poly look (clean flat colours). The override pins it to 0 (= inactive, no
            // cost) so a project default volume can never sneak grain back in.
            var grain = profile.Add<FilmGrain>(true);
            if (grain != null) grain.intensity.Override(0f);

            if (tonemap != null) tonemap.mode.Override(TonemappingMode.Neutral);
            if (bloom != null)
            {
                bloom.threshold.Override(BloomThreshold);
                bloom.intensity.Override(BloomIntensity);
                bloom.scatter.Override(0.55f);
                bloom.tint.Override(Color.white);
                bloom.highQualityFiltering.Override(false);
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
                vignette.color.Override(Color.black);
                vignette.center.Override(new Vector2(0.5f, 0.5f));
                vignette.intensity.Override(0f);
                vignette.smoothness.Override(0.5f);
                vignette.rounded.Override(false);
            }
            if (mixer != null)
            {
                mixer.redOutRedIn.Override(100f);
                mixer.redOutGreenIn.Override(0f);
                mixer.redOutBlueIn.Override(0f);
                mixer.greenOutRedIn.Override(0f);
                mixer.greenOutGreenIn.Override(100f);
                mixer.greenOutBlueIn.Override(0f);
                mixer.blueOutRedIn.Override(0f);
                mixer.blueOutGreenIn.Override(0f);
                mixer.blueOutBlueIn.Override(100f);
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
                white.temperature.Override(0f);
                white.tint.Override(0f);
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

        // ------------------------------------------------------------------ per frame
        public void Tick(FXFrame f)
        {
            ApplyFogAndCamera(f);

            float u = f.under01;
            float d = f.depth;
            float t = f.time;
            float dGrade = FXMath.Smooth01(d / 70f);
            float night = f.night01;

            // above water: bright and friendly by day, a warm golden hour, cooler and calmer at night
            float golden = (1f - night) * FXMath.SmoothRange(0.02f, 0.18f, f.sun01) * (1f - FXMath.SmoothRange(0.35f, 0.75f, f.sun01));
            float aSat = DaySaturation + 4f * golden - 22f * night;
            float aExp = DayExposure;
            float aTemp = 12f * golden - 10f * night;
            float aTint = 3f * golden;

            // underwater by depth: vivid shallow reef, desaturated deep; exposure moves only a little
            float uSat = Mathf.Lerp(8f, -28f, dGrade) - 10f * night;
            float uExp = Mathf.Lerp(0.05f, -0.25f, dGrade);
            float uTemp = Mathf.Lerp(-4f, -14f, dGrade);
            float uVig = Mathf.Lerp(0.14f, 0.3f, dGrade);

            float narc = f.narcosis01;
            float faint = f.faint01;

            float sat = Mathf.Lerp(aSat, uSat, u) + 12f * narc - 50f * faint;
            float exposure = Mathf.Lerp(aExp, uExp, u) - 1.4f * faint;
            float contrast = Mathf.Lerp(DayContrast, Mathf.Lerp(6f, 3f, dGrade), u);
            float hue = 7f * narc * Mathf.Sin(t * 0.37f);
            float temp = Mathf.Lerp(aTemp, uTemp, u);
            float tint = Mathf.Lerp(aTint, -2f, u);

            // warning: pulsing red vignette (too deep for the gear, or low air)
            float pulseHz = f.air01 < 0.1f ? 2.1f : 1.25f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(f.unscaledTime * FXMath.TwoPi * pulseHz);
            float w = f.warn01 * pulse;
            float vig = uVig * u + 0.16f * w + 0.12f * f.splash01 + 0.45f * faint;
            Color vigColor = Color.Lerp(Color.black, WarnRed, w);

            // gentle red loss: none in the bright shallows, noticeable from ~25 m (the flashlight brings 60 % back)
            float redLoss = 30f * FXMath.SmoothRange(6f, 50f, d) * (1f - 0.6f * f.flash01) * u;

            // very soft water wobble; narcosis makes it drunk; the splash gives a short lens bulge
            float wob = Mathf.Sin(t * 0.83f) * 0.55f + Mathf.Sin(t * 1.37f + 1.1f) * 0.3f + Mathf.Sin(t * 2.3f + 2.3f) * 0.15f;
            float lensI = wob * (0.02f * u + 0.26f * narc) - 0.22f * f.splash01;
            float lensX = 1f - 0.3f * narc * (0.5f + 0.5f * Mathf.Sin(t * 0.61f));
            float lensY = 1f - 0.3f * narc * (0.5f + 0.5f * Mathf.Sin(t * 0.47f + 1.3f));
            // chromatic aberration exists only as the narcosis effect (hard difficulty)
            float ca = 0.62f * narc;

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
            if (vignette != null)
            {
                SetF(vignette.intensity, Mathf.Clamp01(vig), 0.002f);
                SetC(vignette.color, vigColor, 0.004f);
            }
            if (mixer != null) SetF(mixer.redOutRedIn, 100f - redLoss, 0.1f);
            if (lens != null)
            {
                SetF(lens.intensity, Mathf.Clamp(lensI, -1f, 1f), 0.0015f);
                SetF(lens.xMultiplier, lensX, 0.002f);
                SetF(lens.yMultiplier, lensY, 0.002f);
            }
            if (chroma != null) SetF(chroma.intensity, Mathf.Clamp01(ca), 0.002f);
            if (bloom != null) SetF(bloom.intensity, BloomIntensity + 0.08f * u, 0.005f);
        }

        void ApplyFogAndCamera(FXFrame f)
        {
            Color uw = FXMath.UnderwaterColor(f.depth, f.night01, f.sun01);
            Color sky = EnvState.SkyFogColor;
            sky.a = 1f;
            Color fogColor = Color.Lerp(sky, uw, f.under01);

            // Exponential-squared fog: crisp near field, soft fade at the visibility limit (reads cleaner on flat
            // low-poly colours than plain exponential haze). Geometric blend = even change in visibility.
            float skyDensity = FXMath.SkyFogDensity(f.night01);
            float uwDensity = FXMath.UnderwaterFogDensity(f.depth, f.night01);
            float density = skyDensity * Mathf.Pow(uwDensity / skyDensity, f.under01);

            if (!fogInit)
            {
                fogInit = true;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
            }
            else
            {
                if (!RenderSettings.fog) RenderSettings.fog = true;
                if (RenderSettings.fogMode != FogMode.ExponentialSquared) RenderSettings.fogMode = FogMode.ExponentialSquared;
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

            Camera cam = f.cam;
            if (cam == null) return;
            if (cam != lastCam)
            {
                lastCam = cam;
                ConfigureCamera(cam);
            }
            var want = f.underwater ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (cam.clearFlags != want) cam.clearFlags = want;
            if (want == CameraClearFlags.SolidColor && FXMath.Differs(cam.backgroundColor, fogColor, 0.0015f))
                cam.backgroundColor = fogColor;
        }

        /// Once per new main camera: post-processing on, and MSAA for the crisp faceted edges when the active URP
        /// asset has MSAA enabled (the editor setup sets 4x on the PC asset). MSAA covers every geometric edge of
        /// the low-poly world, so the post AA (SMAA/FXAA) is dropped then: it would only soften the flat colours
        /// and the point-filtered patterns and cost extra GPU time.
        static void ConfigureCamera(Camera cam)
        {
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;

            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp == null || rp.msaaSampleCount <= 1) return;
            if (!cam.allowMSAA) cam.allowMSAA = true;
            if (data != null && data.antialiasing != AntialiasingMode.None) data.antialiasing = AntialiasingMode.None;
        }

        static void SetF(VolumeParameter<float> p, float v, float eps)
        {
            if (Mathf.Abs(p.value - v) > eps) p.value = v;
        }

        static void SetC(VolumeParameter<Color> p, Color c, float eps)
        {
            if (FXMath.Differs(p.value, c, eps)) p.value = c;
        }
    }
}
