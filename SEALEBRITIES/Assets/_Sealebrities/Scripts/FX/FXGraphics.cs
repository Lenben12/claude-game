using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Applies the graphics tier (Settings.Data.quality: Low / Medium / High, art bible §1.5) to the active URP
    /// asset, the SSAO renderer feature and the main camera. Runs only when the tier changes (never per frame).
    ///
    ///                 High              Medium             Low
    ///   shadows       2048 / 2c / 120 m 1024 / 2c / 80 m   1024 / 2c / 45 m, hard
    ///   SSAO          on                on, half res       off (renderer without SSAO)
    ///   MSAA / AA     4x / -            2x / -             off / FXAA
    ///   render scale  1.0               1.0                0.85
    ///   (+ bloom quality, god rays, caustics and particle counts in the other FX parts)
    ///
    /// Shader-variant safety: URP strips the "SSAO off" variants when every renderer has SSAO. The editor setup
    /// therefore adds a second renderer without SSAO (PC_Renderer_NoAO); Low switches the camera to it. Without that
    /// renderer SSAO is only disabled where it is known to be safe (editor, or the asset keeps both variants),
    /// otherwise it stays on at half resolution. The cascade count stays 2 on every tier for the same reason.
    ///
    /// In the editor every asset change is undone when play mode ends (the URP asset is a project file).
    sealed class FXGraphics
    {
        struct TierValues
        {
            public float renderScale;
            public int msaa;
            public float shadowDistance;
            public int shadowRes;
            public bool softShadows;
            public bool ssao;
            public bool ssaoHalfRes;
        }

        static TierValues For(GraphicsQuality q)
        {
            switch (q)
            {
                case GraphicsQuality.Low:
                    return new TierValues { renderScale = 0.85f, msaa = 1, shadowDistance = 45f, shadowRes = 1024, softShadows = false, ssao = false, ssaoHalfRes = true };
                case GraphicsQuality.Medium:
                    return new TierValues { renderScale = 1f, msaa = 2, shadowDistance = 80f, shadowRes = 1024, softShadows = true, ssao = true, ssaoHalfRes = true };
                default:
                    return new TierValues { renderScale = 1f, msaa = 4, shadowDistance = 120f, shadowRes = 2048, softShadows = true, ssao = true, ssaoHalfRes = false };
            }
        }

        // art bible §1.3 / §1.5: shared by every tier
        const int Cascades = 2;
        const float Cascade2Split = 0.25f;
        const float DepthBias = 1.0f;
        const float NormalBias = 0.6f;
        const float SsaoIntensity = 1.0f;
        const float SsaoRadius = 0.24f;            // bible 0.30, 0.22 when the view-model hands blotch
        const float SsaoDirectLighting = 0.25f;

        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo SoftShadowsField = typeof(UniversalRenderPipelineAsset).GetField("m_SoftShadowsSupported", Inst);
        static readonly FieldInfo SsaoPrefilterField = typeof(UniversalRenderPipelineAsset).GetField("m_PrefilteringModeScreenSpaceOcclusion", Inst);
        static readonly FieldInfo SsaoSettingsField = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Inst);
        static readonly string[] SsaoFieldNames = { "Intensity", "Radius", "DirectLightingStrength", "Downsample", "BlurQuality", "AfterOpaque" };

        UniversalRenderPipelineAsset asset;
        readonly List<ScreenSpaceAmbientOcclusion> ssao = new List<ScreenSpaceAmbientOcclusion>();
        int noAoRenderer = -1;      // renderer index without an active SSAO feature, -1 = none
        bool ssaoOffSafe;            // the build keeps the SSAO-off shader variants

        bool hasTier;
        GraphicsQuality tier = GraphicsQuality.High;
        public GraphicsQuality Tier => tier;

        // ---- editor restore (the URP asset and renderer data are project files)
        bool snapshot;
        float oRenderScale, oShadowDistance, oCascade2, oDepthBias, oNormalBias;
        int oMsaa, oCascades, oShadowRes;
        bool oSoft;
        bool[] oSsaoActive;
        object[][] oSsaoValues;

        // ---- camera
        Camera cam;
        UniversalAdditionalCameraData camData;
        float camOriginalFar = -1f, lastFarSet = -1f;

        public void Apply(GraphicsQuality q)
        {
            tier = q;
            hasTier = true;
            // PropQuality builds the LOD screen heights for bias 1; the "PC" quality level used to carry lodBias 2,
            // which silently doubled every prop draw distance on every tier (~4x the drawn prop area)
            if (!Mathf.Approximately(QualitySettings.lodBias, 1f)) QualitySettings.lodBias = 1f;
            FXQuality.Set(q);

            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a == null) return;
            if (a != asset)
            {
                asset = a;
                snapshot = false;
                FindFeatures();
            }
            TakeSnapshot();

            var t = For(q);
            if (!Mathf.Approximately(a.renderScale, t.renderScale)) a.renderScale = t.renderScale;
            if (a.msaaSampleCount != t.msaa) a.msaaSampleCount = t.msaa;
            if (!Mathf.Approximately(a.shadowDistance, t.shadowDistance)) a.shadowDistance = t.shadowDistance;
            if (a.mainLightShadowmapResolution != t.shadowRes) a.mainLightShadowmapResolution = t.shadowRes;
            if (a.shadowCascadeCount != Cascades) a.shadowCascadeCount = Cascades;
            if (!Mathf.Approximately(a.cascade2Split, Cascade2Split)) a.cascade2Split = Cascade2Split;
            if (!Mathf.Approximately(a.shadowDepthBias, DepthBias)) a.shadowDepthBias = DepthBias;
            if (!Mathf.Approximately(a.shadowNormalBias, NormalBias)) a.shadowNormalBias = NormalBias;
            SetSoftShadows(a, t.softShadows);
            ApplySsao(t);

            if (cam != null) ConfigureCamera(cam);
        }

        void FindFeatures()
        {
            ssao.Clear();
            noAoRenderer = -1;
            ssaoOffSafe = Application.isEditor;
            try
            {
                if (SsaoPrefilterField != null)
                {
                    // PrefilteringMode: 0 Remove, 1 Select (keep on + off), 2 SelectOnly (off variant stripped)
                    int mode = Convert.ToInt32(SsaoPrefilterField.GetValue(asset));
                    if (mode != 2) ssaoOffSafe = true;
                }
            }
            catch (Exception) { }

            var list = asset.rendererDataList;
            // 1) a non-default renderer without an active SSAO feature (added by the editor setup) serves Low
            for (int i = 1; i < list.Length && noAoRenderer < 0; i++)
            {
                var data = list[i];
                if (data == null) continue;
                bool hasActiveAo = false;
                var feats = data.rendererFeatures;
                for (int k = 0; k < feats.Count; k++)
                    if (feats[k] is ScreenSpaceAmbientOcclusion s && s.isActive) hasActiveAo = true;
                if (!hasActiveAo) noAoRenderer = i;
            }
            // 2) the SSAO features this class tunes (never the ones of the no-AO renderer)
            for (int i = 0; i < list.Length; i++)
            {
                if (i == noAoRenderer) continue;
                var data = list[i];
                if (data == null) continue;
                var feats = data.rendererFeatures;
                for (int k = 0; k < feats.Count; k++)
                    if (feats[k] is ScreenSpaceAmbientOcclusion s && !ssao.Contains(s)) ssao.Add(s);
            }
        }

        static void SetSoftShadows(UniversalRenderPipelineAsset a, bool soft)
        {
            if (SoftShadowsField == null) return;
            try
            {
                if ((bool)SoftShadowsField.GetValue(a) != soft) SoftShadowsField.SetValue(a, soft);
            }
            catch (Exception) { }
        }

        void ApplySsao(TierValues t)
        {
            for (int i = 0; i < ssao.Count; i++)
            {
                var f = ssao[i];
                if (f == null) continue;
                // Low: the camera renders with the no-AO renderer when there is one; otherwise disable the feature
                // only where the off variants exist, else keep it at half resolution (cheap, never broken).
                bool active = t.ssao || (noAoRenderer < 0 && !ssaoOffSafe);
                if (noAoRenderer >= 0) active = true;   // renderer 0 keeps its AO (the camera switches renderer)
                if (f.isActive != active) f.SetActive(active);

                object s = SsaoSettingsField != null ? SsaoSettingsField.GetValue(f) : null;
                if (s == null) continue;
                SetField(s, "Intensity", SsaoIntensity);
                SetField(s, "Radius", SsaoRadius);
                SetField(s, "DirectLightingStrength", SsaoDirectLighting);
                SetField(s, "Downsample", t.ssaoHalfRes);
                SetEnum(s, "BlurQuality", t.ssaoHalfRes ? 2 : 0);   // High = 0 (bilateral), Low = 2 (kawase)
            }
        }

        static void SetField(object target, string name, object value)
        {
            try
            {
                var fi = target.GetType().GetField(name, Inst);
                if (fi == null || fi.FieldType != value.GetType()) return;
                if (!Equals(fi.GetValue(target), value)) fi.SetValue(target, value);
            }
            catch (Exception) { }
        }

        static void SetEnum(object target, string name, int value)
        {
            try
            {
                var fi = target.GetType().GetField(name, Inst);
                if (fi == null || !fi.FieldType.IsEnum) return;
                object v = Enum.ToObject(fi.FieldType, value);
                if (!Equals(fi.GetValue(target), v)) fi.SetValue(target, v);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ camera
        /// Once per new main camera and on every tier change: post-processing on, no opaque/depth texture copies
        /// (nothing samples them), MSAA or FXAA depending on the tier, renderer without SSAO on Low.
        public void ConfigureCamera(Camera c)
        {
            if (c != cam)
            {
                cam = c;
                camData = null;
                camOriginalFar = c != null ? c.farClipPlane : -1f;
                lastFarSet = -1f;
            }
            if (c == null) return;
            if (camData == null) camData = c.GetUniversalAdditionalCameraData();
            var d = camData;
            int msaa = asset != null ? asset.msaaSampleCount : 1;
            if (c.allowMSAA != (msaa > 1)) c.allowMSAA = msaa > 1;
            if (d == null) return;
            if (!d.renderPostProcessing) d.renderPostProcessing = true;
            if (d.requiresColorOption != CameraOverrideOption.Off) d.requiresColorOption = CameraOverrideOption.Off;
            if (d.requiresDepthOption != CameraOverrideOption.Off) d.requiresDepthOption = CameraOverrideOption.Off;
            var aa = msaa > 1 ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
            if (d.antialiasing != aa) d.antialiasing = aa;
            if (d.stopNaN) d.stopNaN = false;

            bool useNoAo = hasTier && !For(tier).ssao && noAoRenderer >= 0;
            try { d.SetRenderer(useNoAo ? noAoRenderer : -1); } catch (Exception) { }
        }

        /// Under water nothing beyond the fog limit is visible (the background is the fog colour): pull the far
        /// plane in so the island, the far reef and the shadow casters behind the fog are culled. Restored the
        /// moment the camera leaves the water. A far plane set by another module is respected as the new original.
        public void TickFarClip(Camera c, bool underwater, float visibleMeters)
        {
            if (c == null || c != cam) return;
            float current = c.farClipPlane;
            if (lastFarSet < 0f || !Mathf.Approximately(current, lastFarSet))
            {
                if (lastFarSet >= 0f || camOriginalFar < 0f) camOriginalFar = current;
                lastFarSet = current;
            }
            float want = camOriginalFar;
            if (underwater) want = Mathf.Clamp(visibleMeters * 2.4f, 40f, camOriginalFar);
            if (Mathf.Abs(want - current) > 0.5f)
            {
                c.farClipPlane = want;
                lastFarSet = want;
            }
        }

        // ------------------------------------------------------------------ editor restore
        void TakeSnapshot()
        {
            if (snapshot || asset == null || !Application.isEditor) return;
            snapshot = true;
            oRenderScale = asset.renderScale;
            oMsaa = asset.msaaSampleCount;
            oShadowDistance = asset.shadowDistance;
            oShadowRes = asset.mainLightShadowmapResolution;
            oCascades = asset.shadowCascadeCount;
            oCascade2 = asset.cascade2Split;
            oDepthBias = asset.shadowDepthBias;
            oNormalBias = asset.shadowNormalBias;
            try { oSoft = SoftShadowsField != null && (bool)SoftShadowsField.GetValue(asset); } catch (Exception) { oSoft = true; }
            oSsaoActive = new bool[ssao.Count];
            oSsaoValues = new object[ssao.Count][];
            for (int i = 0; i < ssao.Count; i++)
            {
                var f = ssao[i];
                if (f == null) continue;
                oSsaoActive[i] = f.isActive;
                object s = SsaoSettingsField != null ? SsaoSettingsField.GetValue(f) : null;
                if (s == null) continue;
                var vals = new object[SsaoFieldNames.Length];
                for (int k = 0; k < SsaoFieldNames.Length; k++)
                {
                    var fi = s.GetType().GetField(SsaoFieldNames[k], Inst);
                    vals[k] = fi != null ? fi.GetValue(s) : null;
                }
                oSsaoValues[i] = vals;
            }
        }

        /// Editor only: puts the URP asset and the SSAO feature back the way they were before play mode.
        public void RestoreEditorDefaults()
        {
            if (!snapshot || asset == null || !Application.isEditor) return;
            try
            {
                asset.renderScale = oRenderScale;
                asset.msaaSampleCount = oMsaa;
                asset.shadowDistance = oShadowDistance;
                asset.mainLightShadowmapResolution = oShadowRes;
                asset.shadowCascadeCount = oCascades;
                asset.cascade2Split = oCascade2;
                asset.shadowDepthBias = oDepthBias;
                asset.shadowNormalBias = oNormalBias;
                SetSoftShadows(asset, oSoft);
                for (int i = 0; i < ssao.Count; i++)
                {
                    var f = ssao[i];
                    if (f == null) continue;
                    if (oSsaoActive != null && i < oSsaoActive.Length && f.isActive != oSsaoActive[i]) f.SetActive(oSsaoActive[i]);
                    object s = SsaoSettingsField != null ? SsaoSettingsField.GetValue(f) : null;
                    var vals = oSsaoValues != null && i < oSsaoValues.Length ? oSsaoValues[i] : null;
                    if (s == null || vals == null) continue;
                    for (int k = 0; k < SsaoFieldNames.Length; k++)
                    {
                        if (vals[k] == null) continue;
                        var fi = s.GetType().GetField(SsaoFieldNames[k], Inst);
                        if (fi != null) fi.SetValue(s, vals[k]);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities FX: could not restore the URP asset: " + e.Message); }
            snapshot = false;
        }
    }
}
