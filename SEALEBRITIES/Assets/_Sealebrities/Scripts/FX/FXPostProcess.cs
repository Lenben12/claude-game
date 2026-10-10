using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Global post-processing volume + the only writer of RenderSettings.fog* and the main camera clear flags/background.
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
        readonly FilmGrain grain;
        readonly WhiteBalance white;

        static readonly Color WarnRed = new Color(0.72f, 0.04f, 0.03f, 1f);

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
            grain = profile.Add<FilmGrain>(true);
            white = profile.Add<WhiteBalance>(true);

            if (tonemap != null) tonemap.mode.Override(TonemappingMode.ACES);
            if (bloom != null)
            {
                bloom.threshold.Override(1f);
                bloom.intensity.Override(0.5f);
                bloom.scatter.Override(0.65f);
                bloom.tint.Override(Color.white);
            }
            if (color != null)
            {
                color.postExposure.Override(0f);
                color.contrast.Override(8f);
                color.saturation.Override(5f);
                color.hueShift.Override(0f);
                color.colorFilter.Override(Color.white);
            }
            if (vignette != null)
            {
                vignette.color.Override(Color.black);
                vignette.center.Override(new Vector2(0.5f, 0.5f));
                vignette.intensity.Override(0.18f);
                vignette.smoothness.Override(0.42f);
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
            if (grain != null)
            {
                grain.type.Override(FilmGrainLookup.Medium1);
                grain.intensity.Override(0f);
                grain.response.Override(0.8f);
            }
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
            float dGrade = FXMath.Smooth01(d / 60f);

            // above water: tropical, slight warm grade at golden hour, cooler at night
            float golden = (1f - f.night01) * FXMath.SmoothRange(0.02f, 0.18f, f.sun01) * (1f - FXMath.SmoothRange(0.35f, 0.75f, f.sun01));
            float aSat = 3f - 10f * f.night01;
            float aTemp = 14f * golden - 12f * f.night01;
            float aTint = 3f * golden;
            float aVig = 0.18f + 0.06f * f.night01;

            // underwater by depth
            float uSat = Mathf.Lerp(-5f, -40f, dGrade);
            // The World module already dims sun/ambient/reflections per channel with depth, so the global exposure
            // drop stays moderate here (was -1.1 EV) to avoid darkening the deep sites twice.
            float uExp = Mathf.Lerp(-0.1f, -0.6f, dGrade);
            float uVig = Mathf.Lerp(0.22f, 0.42f, dGrade);

            float narc = f.narcosis01;
            float faint = f.faint01;

            float sat = Mathf.Lerp(aSat, uSat, u) + 12f * narc - 50f * faint;
            float exposure = Mathf.Lerp(0f, uExp, u) - 1.4f * faint;
            float contrast = Mathf.Lerp(5f, 4f, u);
            float hue = 7f * narc * Mathf.Sin(t * 0.37f);
            float temp = Mathf.Lerp(aTemp, -6f, u);
            float tint = Mathf.Lerp(aTint, -2f, u);

            // warning: pulsing red vignette (too deep for the gear, or low air)
            float pulseHz = f.air01 < 0.1f ? 2.1f : 1.25f;
            float pulse = 0.5f + 0.5f * Mathf.Sin(f.unscaledTime * FXMath.TwoPi * pulseHz);
            float w = f.warn01 * pulse;
            float vig = Mathf.Lerp(aVig, uVig, u) + 0.16f * w + 0.12f * f.splash01 + 0.45f * faint;
            Color vigColor = Color.Lerp(Color.black, WarnRed, w);

            // red loss with depth (flashlight brings 45 % of it back)
            float redLoss = 62f * FXMath.Clamp01(d / 30f) * (1f - 0.45f * f.flash01) * u;

            // gentle water wobble; narcosis makes it drunk; the splash gives a short lens bulge
            float wob = Mathf.Sin(t * 0.83f) * 0.55f + Mathf.Sin(t * 1.37f + 1.1f) * 0.3f + Mathf.Sin(t * 2.3f + 2.3f) * 0.15f;
            float lensI = wob * (0.035f * u + 0.26f * narc) - 0.22f * f.splash01;
            float lensX = 1f - 0.3f * narc * (0.5f + 0.5f * Mathf.Sin(t * 0.61f));
            float lensY = 1f - 0.3f * narc * (0.5f + 0.5f * Mathf.Sin(t * 0.47f + 1.3f));
            float ca = 0.04f * u + 0.62f * narc + 0.25f * f.splash01;

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
            if (grain != null) SetF(grain.intensity, 0.15f * u, 0.002f);
            if (bloom != null) SetF(bloom.intensity, 0.5f + 0.15f * u, 0.005f);
        }

        void ApplyFogAndCamera(FXFrame f)
        {
            Color uw = FXMath.UnderwaterColor(f.depth, f.night01);
            Color sky = EnvState.SkyFogColor;
            sky.a = 1f;
            Color fogColor = Color.Lerp(sky, uw, f.under01);

            float uwDensity = Mathf.Lerp(0.028f, 0.05f, FXMath.Smooth01(f.depth / 60f));
            // exponential interpolation reads as an even change in visibility
            float density = 0.0012f * Mathf.Pow(uwDensity / 0.0012f, f.under01);

            if (!fogInit)
            {
                fogInit = true;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Exponential;
            }
            else if (!RenderSettings.fog) RenderSettings.fog = true;

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
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null && !data.renderPostProcessing) data.renderPostProcessing = true;
            }
            var want = f.underwater ? CameraClearFlags.SolidColor : CameraClearFlags.Skybox;
            if (cam.clearFlags != want) cam.clearFlags = want;
            if (want == CameraClearFlags.SolidColor && FXMath.Differs(cam.backgroundColor, fogColor, 0.0015f))
                cam.backgroundColor = fogColor;
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
