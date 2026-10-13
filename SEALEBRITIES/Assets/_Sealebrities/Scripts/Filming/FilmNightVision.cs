using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Q32 night-vision module (cammod_nv): toggled with the lamp key while aiming. The viewfinder turns green and
    /// brighter (a soft full-screen tint just in front of the lens, on the view-model layer so the overlay camera draws
    /// it last), animals in darkness count with full light, and photos taken with it get the green night-vision look.
    /// One quad + one material, no per-frame allocations.
    public sealed class FilmNightVision
    {
        const float FadeSeconds = 0.18f;
        static readonly Color Tint = new Color(0.30f, 1f, 0.46f, 0.34f);
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        readonly Transform host;
        GameObject go;
        MeshRenderer renderer;
        Material mat;
        MaterialPropertyBlock mpb;
        float blend, time;

        public FilmNightVision(Transform host) { this.host = host; }

        /// The tint is on screen (fading in or out).
        public bool Visible => blend > 0f;

        /// on = night vision active right now (owned, switched on, aiming, gameplay).
        public void Tick(Camera view, bool on, float dt)
        {
            blend = Mathf.MoveTowards(blend, on && view != null ? 1f : 0f, dt / FadeSeconds);
            if (blend <= 0f || view == null)
            {
                if (go != null && go.activeSelf) go.SetActive(false);
                return;
            }
            if (!Ensure()) return;
            var t = go.transform;
            if (t.parent != view.transform) t.SetParent(view.transform, false);
            int layer = PlayerViewModelCamera.Overlay != null ? GameLayers.ViewModel : 0;
            if (go.layer != layer) go.layer = layer;
            float z = Mathf.Max(0.02f, view.nearClipPlane + 0.025f);
            float h = 2f * z * Mathf.Tan(Mathf.Clamp(view.fieldOfView, 1f, 170f) * 0.5f * Mathf.Deg2Rad) * 1.25f;
            t.localPosition = new Vector3(0f, 0f, z);
            t.localRotation = Quaternion.identity;
            t.localScale = new Vector3(h * Mathf.Max(1f, view.aspect) * 1.25f, h, 1f);
            if (!go.activeSelf) go.SetActive(true);
            time += dt;
            float flicker = 0.96f + 0.04f * Mathf.Sin(time * 47f) * Mathf.Sin(time * 3.1f);
            var c = Tint;
            c.a *= blend * flicker;
            mpb.SetColor(BaseColorId, c);
            renderer.SetPropertyBlock(mpb);
        }

        /// Hidden while a photo renders (fallback mode without the overlay camera: the world camera would see it).
        public void SetCaptureHidden(bool hidden)
        {
            if (go == null) return;
            bool want = !hidden && blend > 0f;
            if (go.activeSelf != want) go.SetActive(want);
        }

        public void Hide()
        {
            blend = 0f;
            if (go != null && go.activeSelf) go.SetActive(false);
        }

        bool Ensure()
        {
            if (mat == null)
            {
                var shader = MaterialLibrary.UnlitShader;
                if (shader == null) return false;
                mat = new Material(shader) { name = "SB_NightVision" };
                mat.SetColor(BaseColorId, Tint);
                MaterialLibrary.MakeTransparent(mat, true);
                if (mat.HasProperty("_ZTest")) mat.SetFloat("_ZTest", (float)CompareFunction.Always);
                mat.renderQueue = 3990;   // over the world and the held items, under the shutter flash (4000)
            }
            if (go == null)
            {
                go = new GameObject("SB_NightVision");
                if (host != null) go.transform.SetParent(host, false);
                go.AddComponent<MeshFilter>().sharedMesh = FilmMesh.Quad(1f, 1f);
                renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = mat;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                go.SetActive(false);
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();
            return true;
        }

        public void Dispose()
        {
            if (go != null) Object.Destroy(go);
            go = null;
            renderer = null;
            if (mat != null) { Object.Destroy(mat); mat = null; }
        }

        /// Green night-vision look for a photo (in place, RGB24 / RGBA32 readable texture; one pass, no allocations).
        public static void TintPhoto(Texture2D tex)
        {
            if (tex == null) return;
            int bpp = tex.format == TextureFormat.RGB24 ? 3 : tex.format == TextureFormat.RGBA32 ? 4 : 0;
            if (bpp == 0) return;
            var data = tex.GetRawTextureData<byte>();
            int len = data.Length - (bpp - 1);
            for (int i = 0; i < len; i += bpp)
            {
                float r = data[i] / 255f, g = data[i + 1] / 255f, b = data[i + 2] / 255f;
                float lum = 0.30f * r + 0.59f * g + 0.11f * b;
                lum = Mathf.Clamp01(Mathf.Pow(lum, 0.62f) * 1.12f);
                data[i] = (byte)Mathf.Clamp(Mathf.RoundToInt((lum * 0.26f * 0.85f + r * 0.15f) * 255f), 0, 255);
                data[i + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt((lum * 0.85f + g * 0.15f) * 255f), 0, 255);
                data[i + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt((lum * 0.40f * 0.85f + b * 0.15f) * 255f), 0, 255);
            }
            tex.Apply(false);
        }
    }
}
