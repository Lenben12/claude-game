using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Photos for the phone gallery (playtest feedback #9): renders the player's current view into a 512 × 288
    /// texture WITHOUT the first-person view models (the photo camera's near plane sits behind hands, held items and
    /// the phone), plus a white shutter flash over the screen. The shutter sound comes from the FX module
    /// (it listens to Gallery.Added). One render + one 512 × 288 readback per photo; render target and readback
    /// texture are reused.
    public sealed class FilmPhoto
    {
        public const int Width = 512, Height = 288;
        /// Hands, held items and the raised phone all sit closer than this to the eye.
        public const float PhotoNearClip = 0.65f;
        const float FlashSeconds = 0.32f;
        const float FlashAlpha = 0.85f;

        readonly Transform host;
        Camera cam;
        RenderTexture rt;
        Texture2D tex;

        GameObject flashGo;
        MeshRenderer flashRenderer;
        Material flashMat;
        MaterialPropertyBlock mpb;
        float flash;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public FilmPhoto(Transform host) { this.host = host; }

        // ------------------------------------------------------------------ capture
        /// Renders 'view' into the shared readable texture (valid until the next capture) or returns null on failure.
        /// The caller must hide its own view models that could reach past PhotoNearClip.
        public Texture2D Capture(Camera view)
        {
            if (view == null || host == null) return null;
            if (!EnsureTargets()) return null;
            if (cam == null)
            {
                var go = new GameObject("SB_PhotoCamera");
                go.transform.SetParent(host, false);
                cam = go.AddComponent<Camera>();
                cam.enabled = false;
            }

            var prevActive = RenderTexture.active;
            try
            {
                cam.CopyFrom(view);
                cam.enabled = false;
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                cam.targetTexture = rt;
                cam.aspect = Width / (float)Height;
                cam.nearClipPlane = Mathf.Max(view.nearClipPlane, PhotoNearClip);
                if (cam.farClipPlane < cam.nearClipPlane + 10f) cam.farClipPlane = cam.nearClipPlane + 10f;
                CopyPipelineData(view, cam);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                return tex;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities: photo could not be rendered: " + e.Message);
                return null;
            }
            finally
            {
                if (cam != null) cam.targetTexture = null;
                RenderTexture.active = prevActive;
            }
        }

        bool EnsureTargets()
        {
            if (rt != null && !rt.IsCreated()) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            if (rt == null)
            {
                rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
                {
                    name = "SB_PhotoTarget",
                    antiAliasing = 1,
                    useMipMap = false
                };
                if (!rt.Create()) return false;
            }
            if (tex == null) tex = new Texture2D(Width, Height, TextureFormat.RGB24, false) { name = "SB_Photo", wrapMode = TextureWrapMode.Clamp };
            return true;
        }

        /// Same post-processing / volumes / shadows as the player's view (TAA needs history → FXAA for the one-off render).
        static void CopyPipelineData(Camera from, Camera to)
        {
            try
            {
                var src = from.GetUniversalAdditionalCameraData();
                var dst = to.GetUniversalAdditionalCameraData();
                if (src == null || dst == null) return;
                dst.renderType = CameraRenderType.Base;
                dst.renderPostProcessing = src.renderPostProcessing;
                dst.antialiasing = src.antialiasing == AntialiasingMode.None ? AntialiasingMode.None : AntialiasingMode.FastApproximateAntialiasing;
                dst.renderShadows = src.renderShadows;
                dst.volumeLayerMask = src.volumeLayerMask;
                dst.volumeTrigger = src.volumeTrigger != null ? src.volumeTrigger : from.transform;
                dst.requiresColorOption = src.requiresColorOption;
                dst.requiresDepthOption = src.requiresDepthOption;
                dst.stopNaN = src.stopNaN;
                dst.dithering = src.dithering;
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ shutter flash
        /// White flash over the whole view, fading out in ~0.3 s (drawn last, after all scene geometry).
        public void Flash(Camera view)
        {
            if (view == null) return;
            if (!EnsureFlash()) return;
            var t = flashGo.transform;
            if (t.parent != view.transform) t.SetParent(view.transform, false);
            float z = Mathf.Max(0.02f, view.nearClipPlane + 0.02f);
            float h = 2f * z * Mathf.Tan(Mathf.Clamp(view.fieldOfView, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * 1.3f;
            t.localPosition = new Vector3(0f, 0f, z);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(h * Mathf.Max(1f, view.aspect) * 1.3f, h, 1f);
            flash = 1f;
            if (!flashGo.activeSelf) flashGo.SetActive(true);
            ApplyFlashAlpha(FlashAlpha);
        }

        public void Tick(float dt)
        {
            if (flash <= 0f) return;
            if (flashGo == null) { flash = 0f; return; }   // destroyed together with an old view camera
            flash = Mathf.Max(0f, flash - dt / FlashSeconds);
            if (flash <= 0f) { flashGo.SetActive(false); return; }
            ApplyFlashAlpha(FlashAlpha * flash * flash);
        }

        /// Hides the flash immediately (menu, faint).
        public void CancelFlash()
        {
            flash = 0f;
            if (flashGo != null && flashGo.activeSelf) flashGo.SetActive(false);
        }

        bool EnsureFlash()
        {
            if (flashMat == null)
            {
                var shader = MaterialLibrary.UnlitShader;
                if (shader == null) return false;
                flashMat = new Material(shader) { name = "SB_ShutterFlash" };
                flashMat.SetColor(BaseColorId, new Color(1f, 1f, 1f, 0f));
                MaterialLibrary.MakeTransparent(flashMat, true);
                if (flashMat.HasProperty("_ZTest")) flashMat.SetFloat("_ZTest", (float)CompareFunction.Always);
                flashMat.renderQueue = 4000;
            }
            if (flashGo == null)
            {
                flashGo = new GameObject("SB_ShutterFlash");
                flashGo.AddComponent<MeshFilter>().sharedMesh = FilmMesh.Quad(1f, 1f);
                flashRenderer = flashGo.AddComponent<MeshRenderer>();
                flashRenderer.sharedMaterial = flashMat;
                flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
                flashRenderer.receiveShadows = false;
                flashRenderer.lightProbeUsage = LightProbeUsage.Off;
                flashRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                flashRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                flashRenderer.allowOcclusionWhenDynamic = false;
                flashGo.SetActive(false);
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
            return true;
        }

        void ApplyFlashAlpha(float a)
        {
            if (flashRenderer == null) return;
            mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, a));
            flashRenderer.SetPropertyBlock(mpb);
        }

        // (the shutter SOUND is played by the FX module on Gallery.Added)

        // ------------------------------------------------------------------ cleanup
        public void Dispose()
        {
            if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
            cam = null;
            if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); rt = null; }
            if (tex != null) { UnityEngine.Object.Destroy(tex); tex = null; }
            if (flashGo != null) UnityEngine.Object.Destroy(flashGo);
            flashGo = null;
            flashRenderer = null;
            if (flashMat != null) { UnityEngine.Object.Destroy(flashMat); flashMat = null; }
        }
    }
}
