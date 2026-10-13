using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Q10 "residual light": the first-person hands and held items never turn black (Testversion 3: in the trench even
    /// the hands were black). A soft directional fill light on the view-model layer tops the light on them up to 35 % of
    /// the bright shallow-water ambient whenever the real ambient is darker (deep water, night dives). It is switched on
    /// only while the player's view-model overlay camera renders (PlayerViewModelCamera.Overlay) and off again right
    /// after, so it never lights the world, photos, the selfie view or the creature icon studio.
    sealed class FXViewModelFill
    {
        const float Floor = 0.35f;
        // luminance (gamma) of the shallow-water ambient equator at noon (#6AA8C4, WorldDayNight.WaterEquator)
        const float ShallowLum = 0.6f;
        static readonly Color UnderTint = new Color(0.62f, 0.86f, 0.95f, 1f);
        static readonly Color AboveTint = new Color(0.84f, 0.87f, 1f, 1f);

        readonly GameObject go;
        readonly Light fill;
        bool subscribed;
        float intensity;
        Color color = Color.white;

        public FXViewModelFill(Transform parent)
        {
            go = new GameObject("FX_ViewModelFill");
            go.layer = GameLayers.ViewModel;
            go.transform.SetParent(parent, false);
            fill = go.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.shadows = LightShadows.None;
            fill.renderMode = LightRenderMode.ForcePixel;
            fill.cullingMask = GameLayers.ViewModelMask;
            fill.intensity = 0f;
            fill.enabled = false;
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            subscribed = true;
        }

        public void Dispose()
        {
            if (subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= OnBegin;
                RenderPipelineManager.endCameraRendering -= OnEnd;
                subscribed = false;
            }
            if (go != null) Object.Destroy(go);
        }

        public void Tick(FXFrame f)
        {
            Color amb = RenderSettings.ambientEquatorColor;
            float lum = 0.2126f * amb.r + 0.7152f * amb.g + 0.0722f * amb.b;
            float deficit = Mathf.Max(0f, Floor * ShallowLum - lum);
            // the flashlight already lights the hands well
            if (f.flashlight) deficit *= 0.4f;
            float target = f.hasPlayer && f.inGame ? deficit * 1.5f : 0f;
            intensity = Mathf.MoveTowards(intensity, target, f.dt * 0.5f);
            color = Color.Lerp(AboveTint, UnderTint, f.under01);
        }

        void OnBegin(ScriptableRenderContext ctx, Camera c)
        {
            if (c == null || fill == null || intensity < 0.003f) return;
            var overlay = PlayerViewModelCamera.Overlay;
            if (overlay == null || c != overlay) return;
            // from above and slightly behind the eye, so the visible faces of the hands catch it
            fill.transform.rotation = c.transform.rotation * Quaternion.Euler(38f, -18f, 0f);
            fill.color = color;
            fill.intensity = intensity;
            fill.enabled = true;
        }

        void OnEnd(ScriptableRenderContext ctx, Camera c)
        {
            if (fill != null && fill.enabled) fill.enabled = false;
        }
    }
}
