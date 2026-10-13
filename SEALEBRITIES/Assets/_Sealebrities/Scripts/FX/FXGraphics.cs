using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Applies the graphics tier (Settings.Data.quality: Low / Medium / High, art bible §1.5) to the active URP
    /// asset and the main camera. Runs only when the tier changes (never per frame).
    ///
    ///                 High              Medium             Low
    ///   shadows       2048 / 2c / 120 m 1024 / 2c / 80 m   1024 / 2c / 45 m, hard
    ///   SSAO          off               off                off      (round 2, Q3: it made the hut walls grainy)
    ///   MSAA / AA     4x / -            2x / -             off / FXAA
    ///   render scale  1.0               1.0                0.85
    ///   (+ bloom quality, god rays, caustics and particle counts in the other FX parts)
    ///
    /// SSAO: the editor setup removes the feature from the PC renderer. Should a project still carry an SSAO feature
    /// (setup not re-run), it is switched off at runtime where the "SSAO off" shader variants are known to exist (editor,
    /// prefiltering keeps both, or a second renderer without SSAO is in the build – then the camera also uses that
    /// renderer); otherwise its intensity is pinned to 0 (no darkening, no grain). It is never switched on.
    /// The cascade count stays 2 on every tier (URP strips the single-cascade shadow variants).
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
        }

        static TierValues For(GraphicsQuality q)
        {
            switch (q)
            {
                case GraphicsQuality.Low:
                    return new TierValues { renderScale = 0.85f, msaa = 1, shadowDistance = 45f, shadowRes = 1024, softShadows = false };
                case GraphicsQuality.Medium:
                    return new TierValues { renderScale = 1f, msaa = 2, shadowDistance = 80f, shadowRes = 1024, softShadows = true };
                default:
                    return new TierValues { renderScale = 1f, msaa = 4, shadowDistance = 120f, shadowRes = 2048, softShadows = true };
            }
        }

        // art bible §1.3 / §1.5: shared by every tier
        const int Cascades = 2;
        const float Cascade2Split = 0.25f;
        const float DepthBias = 1.0f;
        const float NormalBias = 0.6f;

        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo SoftShadowsField = typeof(UniversalRenderPipelineAsset).GetField("m_SoftShadowsSupported", Inst);
        static readonly FieldInfo SsaoPrefilterField = typeof(UniversalRenderPipelineAsset).GetField("m_PrefilteringModeScreenSpaceOcclusion", Inst);
        static readonly FieldInfo SsaoSettingsField = typeof(ScreenSpaceAmbientOcclusion).GetField("m_Settings", Inst);
        static readonly string[] SsaoFieldNames = { "Intensity", "Radius", "DirectLightingStrength", "Downsample", "BlurQuality", "AfterOpaque" };

        UniversalRenderPipelineAsset asset;
        readonly List<ScreenSpaceAmbientOcclusion> ssao = new List<ScreenSpaceAmbientOcclusion>();
        int noAoRenderer = -1;      // renderer index without an active SSAO feature, -1 = none
        bool ssaoOffSafe;            // the build keeps the SSAO-off shader variants
        bool ssaoStillOn;            // an SSAO feature of the default renderer could not be switched off (intensity 0)

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
            SsaoOff();

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

        /// Q3: no SSAO on any tier. Normally the editor setup already removed the feature; this only handles projects
        /// that still carry one (never switches it on).
        void SsaoOff()
        {
            ssaoStillOn = false;
            for (int i = 0; i < ssao.Count; i++)
            {
                var f = ssao[i];
                if (f == null || !f.isActive) continue;
                if (ssaoOffSafe || noAoRenderer >= 0) { f.SetActive(false); continue; }
                // the "SSAO off" variants may be stripped from this build: keep the pass but without any occlusion
                ssaoStillOn = true;
                object s = SsaoSettingsField != null ? SsaoSettingsField.GetValue(f) : null;
                if (s != null) SetField(s, "Intensity", 0f);
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

        // ------------------------------------------------------------------ camera
        /// Once per new main camera and on every tier change: post-processing on (on the view-model overlay when the
        /// camera has one, see PlayerViewModelCamera), no opaque/depth texture copies
        /// (nothing samples them), MSAA or FXAA depending on the tier, the default renderer (no SSAO, Q3).
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
            // post-processing on the main camera – unless the player's view-model overlay (last camera of the stack)
            // runs it for the whole stack; on both cameras the world would be graded twice
            bool post = !PlayerViewModelCamera.StackRunsPostProcessing(d);
            if (d.renderPostProcessing != post) d.renderPostProcessing = post;
            if (d.requiresColorOption != CameraOverrideOption.Off) d.requiresColorOption = CameraOverrideOption.Off;
            if (d.requiresDepthOption != CameraOverrideOption.Off) d.requiresDepthOption = CameraOverrideOption.Off;
            var aa = msaa > 1 ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
            if (d.antialiasing != aa) d.antialiasing = aa;
            if (d.stopNaN) d.stopNaN = false;

            // SSAO is off in the default renderer on every tier (Q3): the camera always uses the pipeline's default renderer
            try { d.SetRenderer(-1); } catch (Exception) { }
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

    /// Round 3 (GDD §19 "the water must hide what is under it"): above water the sea is (nearly) opaque beyond a few
    /// metres (WorldOcean.ViewOpacity), so creatures and props deep below it need not be drawn from the beach, the dock
    /// or the boat. A module puts such renderers on a layer of its own and registers that layer here (Register); while
    /// the main camera is above the surface those layers get Camera.layerCullDistances = AboveWaterMeters (merged:
    /// a shorter distance another module set is kept, every other layer is untouched), under water the camera's own
    /// distances come back exactly. Allocation-free per frame (the camera array is only read / written on a change).
    /// Nothing is registered by default (fauna and props live on the Default layer today).
    public static class FXSurfaceCull
    {
        /// Bit mask of the registered "under the surface" layers.
        public static int Layers { get; private set; }
        /// Cull distance for those layers while the camera is above the water.
        public static float AboveWaterMeters = 22f;
        /// True while the distances are written on the camera (camera above water and at least one layer registered).
        public static bool Active { get; private set; }
        /// Last state seen by Tick (true = camera above the surface).
        public static bool CameraAboveWater { get; private set; } = true;

        static Camera cam;
        static int appliedMask;
        static float appliedMeters;
        static readonly float[] saved = new float[32];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Layers = 0; AboveWaterMeters = 22f; Active = false; CameraAboveWater = true; cam = null; appliedMask = 0; appliedMeters = 0f; }

        public static void Register(int layer) { if (layer >= 0 && layer < 32) Layers |= 1 << layer; }
        public static void Unregister(int layer) { if (layer >= 0 && layer < 32) Layers &= ~(1 << layer); }

        /// Called every frame by the FX director with the main camera.
        internal static void Tick(Camera c, bool underwater)
        {
            CameraAboveWater = !underwater;
            if (c != cam)
            {
                if (cam != null && appliedMask != 0) Restore(cam);
                cam = c;
                appliedMask = 0;
                Active = false;
            }
            if (c == null) return;
            int want = underwater ? 0 : Layers;
            float meters = Mathf.Max(1f, AboveWaterMeters);
            if (want == appliedMask && (want == 0 || Mathf.Approximately(meters, appliedMeters))) return;

            float[] d = c.layerCullDistances;   // copy (allocates – only on a state change)
            for (int l = 0; l < 32; l++)
            {
                int bit = 1 << l;
                bool had = (appliedMask & bit) != 0, wants = (want & bit) != 0;
                if (wants)
                {
                    if (!had) saved[l] = d[l];
                    d[l] = saved[l] > 0f ? Mathf.Min(saved[l], meters) : meters;
                }
                else if (had) d[l] = saved[l];
            }
            c.layerCullDistances = d;
            appliedMask = want;
            appliedMeters = meters;
            Active = want != 0;
        }

        static void Restore(Camera c)
        {
            if (c == null) return;
            float[] d = c.layerCullDistances;
            for (int l = 0; l < 32; l++) if ((appliedMask & (1 << l)) != 0) d[l] = saved[l];
            c.layerCullDistances = d;
        }
    }
}
