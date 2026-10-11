using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// First-person view models (mitten hands, dive lamp, hand camera, fishing rod, 3D phone, shutter flash) are drawn
    /// by a URP overlay camera stacked on the main camera, so world geometry can never cover or cut them – the dock seen
    /// "through" the lamp held over it (feedback Testversion 3, d).
    ///  - Every mesh renderer below the main camera is moved to ViewModelLayer (the filming and fishing modules parent
    ///    their models there too); the main camera does not render that layer, so photos (FilmPhoto copies its mask)
    ///    never contain held items either.
    ///  - The overlay sits at the main camera's pose (child, identity), copies its FOV / near plane right before every
    ///    frame and clears the depth: held items keep their depth order among themselves but always win against the world.
    ///  - Post-processing runs ONCE for the whole stack on the overlay (the last camera); the main camera has it off.
    ///    World and held items get exactly the same grading, bloom, vignette and underwater look as before (FXGraphics and
    ///    FilmPhoto respect this). Fog and ambient are global, so held items are fogged/lit like before.
    ///  - Unity culls lights with the camera's culling mask: the overlay gets mirror copies of the directional lights
    ///    (sun, moon) on its own layer – rotation, colour, intensity and the caustics cookie copied every frame, no
    ///    shadows. The dive lamp's spot light stays on the main camera and lights the WORLD as before.
    ///  - The overlay uses the pipeline's renderer without SSAO when there is one (nothing to occlude on its layer).
    /// No per-frame allocations: the renderer scan runs only when the camera children change, Hands change or every 0.25 s.
    [DefaultExecutionOrder(90)]   // after PlayerHands (60) / PlayerFlashlight (65): new view-model parts are moved this frame
    public sealed class PlayerViewModelCamera : MonoBehaviour
    {
        /// Unnamed user layer for first-person view models (31 is the creature icon studio, 2 the player, 4 water).
        public const int ViewModelLayer = 30;
        public const int ViewModelMask = 1 << ViewModelLayer;
        const int IconStudioLayer = 31;
        const float OverlayFar = 60f;      // the rod tip reaches ~2.3 m, nothing else beyond 1 m
        const float RescanInterval = 0.25f;
        const int MaxMirrors = 6;

        static PlayerViewModelCamera instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        /// The overlay camera of the local player (null before the player exists).
        public static Camera Overlay => instance != null ? instance.overlay : null;

        /// A view-model part was (re)built below the camera: move it to the view-model layer this frame.
        public static void MarkDirty()
        {
            if (instance != null) instance.dirty = true;
        }

        Camera main, overlay;
        Transform mainT;
        UniversalAdditionalCameraData mainData, overlayData;
        bool active, subscribed, dirty = true;
        int lastChildCount = -1;
        float rescanTimer;
        readonly List<Renderer> scratch = new List<Renderer>(256);

        struct Mirror
        {
            public Light src, dst;
            public Transform srcT, dstT;
            public UniversalAdditionalLightData srcData, dstData;
        }

        readonly Mirror[] mirrors = new Mirror[MaxMirrors];
        int mirrorCount;
        float lightScanTimer;

        // ------------------------------------------------------------------ setup
        /// Creates the overlay camera below mainCam and puts it into mainCam's URP camera stack.
        public static PlayerViewModelCamera Attach(Camera mainCam)
        {
            if (mainCam == null) return null;
            var go = new GameObject("ViewModelCamera");
            go.layer = ViewModelLayer;
            go.transform.SetParent(mainCam.transform, false);
            var vm = go.AddComponent<PlayerViewModelCamera>();
            bool ok;
            try { ok = vm.Init(mainCam); }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            if (!ok)
            {
                Debug.LogWarning("Sealebrities: view-model camera unavailable – held items are drawn by the main camera.");
                Destroy(go);
                return null;
            }
            return vm;
        }

        bool Init(Camera mainCam)
        {
            main = mainCam;
            mainT = mainCam.transform;
            mainData = mainCam.GetUniversalAdditionalCameraData();
            if (mainData == null || mainData.renderType != CameraRenderType.Base) return false;

            overlay = gameObject.AddComponent<Camera>();
            overlay.clearFlags = CameraClearFlags.Depth;
            overlay.cullingMask = ViewModelMask;
            overlay.useOcclusionCulling = false;
            overlay.allowHDR = main.allowHDR;
            overlay.allowMSAA = main.allowMSAA;
            SyncLens();

            overlayData = overlay.GetUniversalAdditionalCameraData();
            if (overlayData == null) return false;
            overlayData.renderType = CameraRenderType.Overlay;
            // clearDepth (read-only in URP 17) defaults to true on a fresh component: the world's depth is cleared
            // before the held items are drawn, so nothing of the world can cover or cut them
            if (!overlayData.clearDepth) return false;
            overlayData.renderShadows = false;
            overlayData.requiresColorOption = CameraOverrideOption.Off;
            overlayData.requiresDepthOption = CameraOverrideOption.Off;
            overlayData.renderPostProcessing = true;
            overlayData.volumeLayerMask = mainData.volumeLayerMask;
            overlayData.volumeTrigger = mainT;
            int noAo = FindNoAoRenderer();
            if (noAo >= 0)
            {
                try { overlayData.SetRenderer(noAo); } catch (Exception) { }
            }

            instance = this;
            Activate();
            return true;
        }

        void OnEnable()
        {
            if (main != null) Activate();
        }

        void OnDisable() => Deactivate();

        void OnDestroy()
        {
            Deactivate();
            if (instance == this) instance = null;
        }

        void Activate()
        {
            if (main == null || overlay == null || mainData == null) return;
            if (!subscribed)
            {
                RenderPipelineManager.beginCameraRendering += OnBeginCamera;
                RenderPipelineManager.endCameraRendering += OnEndCamera;
                subscribed = true;
            }
            active = true;
            dirty = true;
            EnsureStack();
        }

        /// Back to the old single-camera rendering (held items on the main camera, post-processing on the main camera).
        void Deactivate()
        {
            if (subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
                RenderPipelineManager.endCameraRendering -= OnEndCamera;
                subscribed = false;
            }
            if (!active) return;
            active = false;
            if (main != null)
            {
                main.cullingMask |= ViewModelMask;
                if (mainData != null)
                {
                    try
                    {
                        var stack = mainData.cameraStack;
                        if (stack != null && overlay != null) stack.Remove(overlay);
                    }
                    catch (Exception) { }
                    mainData.renderPostProcessing = true;
                }
            }
            for (int i = 0; i < mirrorCount; i++)
                if (mirrors[i].dst != null) mirrors[i].dst.enabled = false;
        }

        /// Stack membership, culling masks and the post-processing split (cheap; checked every frame).
        void EnsureStack()
        {
            if (!active || main == null || mainData == null || overlay == null) return;
            if ((main.cullingMask & ViewModelMask) != 0) main.cullingMask &= ~ViewModelMask;
            if (overlay.cullingMask != ViewModelMask) overlay.cullingMask = ViewModelMask;
            List<Camera> stack = null;
            try { stack = mainData.cameraStack; } catch (Exception) { stack = null; }
            if (stack == null)
            {
                // not a base camera any more: fall back to the main camera rendering everything
                Deactivate();
                return;
            }
            if (!stack.Contains(overlay)) stack.Add(overlay);
            // the overlay is the last camera of the stack: it runs the post-processing for the whole image
            if (mainData.renderPostProcessing) mainData.renderPostProcessing = false;
            if (!overlayData.renderPostProcessing) overlayData.renderPostProcessing = true;
            if (overlay.allowHDR != main.allowHDR) overlay.allowHDR = main.allowHDR;
            if (overlay.allowMSAA != main.allowMSAA) overlay.allowMSAA = main.allowMSAA;
            if (overlayData.volumeLayerMask != mainData.volumeLayerMask) overlayData.volumeLayerMask = mainData.volumeLayerMask;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            if (main == null)
            {
                Destroy(gameObject);   // the main camera went away (it owns us anyway)
                return;
            }
            if (!active) return;
            EnsureStack();

            int count = mainT.childCount;
            rescanTimer -= Time.unscaledDeltaTime;
            if (dirty || count != lastChildCount || rescanTimer <= 0f) Rescan(count);

            lightScanTimer -= Time.unscaledDeltaTime;
            if (lightScanTimer <= 0f)
            {
                lightScanTimer = 1f;
                if (NeedLightScan()) ScanLights();
            }
        }

        /// Moves every mesh renderer below the main camera onto the view-model layer. Particle systems and line /
        /// trail renderers are world-space effects and stay with the world (depth-tested against it).
        void Rescan(int childCount)
        {
            dirty = false;
            lastChildCount = childCount;
            rescanTimer = RescanInterval;
            scratch.Clear();
            mainT.GetComponentsInChildren(true, scratch);
            for (int i = 0; i < scratch.Count; i++)
            {
                var r = scratch[i];
                if (r == null || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var go = r.gameObject;
                if (go.layer != ViewModelLayer) go.layer = ViewModelLayer;
            }
            scratch.Clear();
        }

        /// Right before the main camera renders (after every LateUpdate): same lens as the main camera. Right before the
        /// overlay renders (after the world, before its culling): the mirror lights are switched on with the current
        /// sun / moon state – and off again when it is done, so they never exist for any other camera (world, photo,
        /// creature icons), whatever layer rules apply there.
        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (!active || overlay == null) return;
            if (c == main) SyncLens();
            else if (c == overlay) UpdateMirrors();
        }

        void OnEndCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != overlay) return;
            for (int i = 0; i < mirrorCount; i++)
            {
                var l = mirrors[i].dst;
                if (l != null && l.enabled) l.enabled = false;
            }
        }

        void SyncLens()
        {
            if (overlay == null || main == null) return;
            if (overlay.orthographic != main.orthographic) overlay.orthographic = main.orthographic;
            float fov = main.fieldOfView;
            if (overlay.fieldOfView != fov) overlay.fieldOfView = fov;
            float near = main.nearClipPlane;
            if (overlay.nearClipPlane != near) overlay.nearClipPlane = near;
            float far = Mathf.Max(near + 1f, Mathf.Min(OverlayFar, main.farClipPlane));
            if (overlay.farClipPlane != far) overlay.farClipPlane = far;
            if (overlay.usePhysicalProperties != main.usePhysicalProperties) overlay.usePhysicalProperties = main.usePhysicalProperties;
            if (main.usePhysicalProperties)
            {
                overlay.sensorSize = main.sensorSize;
                overlay.lensShift = main.lensShift;
                overlay.gateFit = main.gateFit;
                overlay.focalLength = main.focalLength;
            }
        }

        // ------------------------------------------------------------------ mirrored sun / moon
        /// Allocation free; checked about once a second.
        bool NeedLightScan()
        {
            if (mirrorCount == 0) return true;
            for (int i = 0; i < mirrorCount; i++)
                if (mirrors[i].src == null) return true;
            // the sun, or the moon while it is the main light, not mirrored yet (world built later, rebuilt sky)
            return Unmirrored(WorldInfo.Sun) || Unmirrored(RenderSettings.sun);
        }

        bool Unmirrored(Light l)
        {
            if (l == null || l.type != LightType.Directional || mirrorCount >= MaxMirrors) return false;
            int layer = l.gameObject.layer;
            return layer != ViewModelLayer && layer != IconStudioLayer && !IsMirrored(l);
        }

        /// Finds the scene's directional lights (World: sun + moon) and builds a mirror for each one. Runs only while
        /// one is missing (see NeedLightScan), at most every 2 s (FindObjectsByType allocates).
        void ScanLights()
        {
            lightScanTimer = 2f;
            Light[] lights;
            try { lights = FindObjectsByType<Light>(); }
            catch (Exception) { return; }
            if (lights == null) return;

            // drop mirrors whose source is gone
            for (int i = mirrorCount - 1; i >= 0; i--)
            {
                if (mirrors[i].src != null) continue;
                if (mirrors[i].dst != null) Destroy(mirrors[i].dst.gameObject);
                mirrors[i] = mirrors[mirrorCount - 1];
                mirrors[mirrorCount - 1] = default;
                mirrorCount--;
            }

            for (int i = 0; i < lights.Length && mirrorCount < MaxMirrors; i++)
            {
                var l = lights[i];
                if (l == null || l.type != LightType.Directional) continue;
                int layer = l.gameObject.layer;
                if (layer == ViewModelLayer || layer == IconStudioLayer) continue;   // our mirrors / the icon studio
                if (IsMirrored(l)) continue;
                AddMirror(l);
            }
        }

        bool IsMirrored(Light l)
        {
            for (int i = 0; i < mirrorCount; i++)
                if (mirrors[i].src == l) return true;
            return false;
        }

        void AddMirror(Light src)
        {
            var go = new GameObject("VM_" + src.name);
            go.layer = ViewModelLayer;
            go.transform.SetParent(transform, false);
            var dst = go.AddComponent<Light>();
            dst.type = LightType.Directional;
            dst.shadows = LightShadows.None;            // held items do not receive shadows (and no extra shadow map)
            dst.renderMode = LightRenderMode.ForcePixel;
            dst.cullingMask = ViewModelMask;
            dst.enabled = false;
            var m = new Mirror { src = src, dst = dst, srcT = src.transform, dstT = go.transform };
            try
            {
                m.srcData = src.GetUniversalAdditionalLightData();
                m.dstData = dst.GetUniversalAdditionalLightData();
                if (m.dstData != null) m.dstData.usePipelineSettings = true;
            }
            catch (Exception) { }
            mirrors[mirrorCount++] = m;
        }

        /// Copies the state of the scene's directional lights onto the overlay's mirrors (allocation free).
        void UpdateMirrors()
        {
            for (int i = 0; i < mirrorCount; i++)
            {
                ref Mirror m = ref mirrors[i];
                if (m.dst == null) continue;
                bool on = m.src != null && m.src.isActiveAndEnabled && m.src.intensity > 0f;
                if (m.dst.enabled != on) m.dst.enabled = on;
                if (!on) continue;
                m.dstT.SetPositionAndRotation(m.srcT.position, m.srcT.rotation);
                m.dst.color = m.src.color;
                m.dst.intensity = m.src.intensity;
                if (m.dst.useColorTemperature != m.src.useColorTemperature) m.dst.useColorTemperature = m.src.useColorTemperature;
                if (m.src.useColorTemperature) m.dst.colorTemperature = m.src.colorTemperature;
                // the FX caustics cookie on the sun (under water) lights the held items too
                var cookie = m.src.cookie;
                if (!ReferenceEquals(m.dst.cookie, cookie)) m.dst.cookie = cookie;
                if (cookie != null && m.srcData != null && m.dstData != null)
                {
                    m.dstData.lightCookieSize = m.srcData.lightCookieSize;
                    m.dstData.lightCookieOffset = m.srcData.lightCookieOffset;
                }
            }
        }

        // ------------------------------------------------------------------ helpers
        /// Index of a non-default renderer without an active SSAO feature (the editor setup's PC_Renderer_NoAO), or -1.
        static int FindNoAoRenderer()
        {
            try
            {
                var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (asset == null) return -1;
                var list = asset.rendererDataList;
                for (int i = 1; i < list.Length; i++)
                {
                    var data = list[i];
                    if (data == null || !(data is UniversalRendererData)) continue;
                    bool ao = false;
                    var feats = data.rendererFeatures;
                    for (int k = 0; k < feats.Count; k++)
                        if (feats[k] is ScreenSpaceAmbientOcclusion s && s.isActive) ao = true;
                    if (!ao) return i;
                }
            }
            catch (Exception) { }
            return -1;
        }

        /// True when an active overlay camera in the base camera's stack runs the post-processing for the whole stack
        /// (then the base camera itself must have it off, or the world would be graded twice).
        public static bool StackRunsPostProcessing(UniversalAdditionalCameraData baseData)
        {
            if (baseData == null) return false;
            try
            {
                if (baseData.renderType != CameraRenderType.Base) return false;
                var stack = baseData.cameraStack;
                if (stack == null) return false;
                for (int i = 0; i < stack.Count; i++)
                {
                    var c = stack[i];
                    if (c == null || !c.isActiveAndEnabled) continue;
                    if (c.TryGetComponent(out UniversalAdditionalCameraData d) && d.renderPostProcessing) return true;
                }
            }
            catch (Exception) { }
            return false;
        }
    }
}
