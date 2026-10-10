using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural textures and transparent particle materials used by the FX module.
    static class FXAssets
    {
        /// Soft round speck (white, alpha falloff).
        public static Texture2D SoftDot(int size)
        {
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = FXMath.Clamp01(1f - r);
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            return Finish("FX_SoftDot", size, size, px, TextureWrapMode.Clamp);
        }

        /// Air bubble: bright rim, faint body, small specular highlight.
        public static Texture2D BubbleRing(int size)
        {
            var px = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float rim = Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.11f, 2f));
                float body = r < 0.8f ? 0.14f : 0f;
                float hx = dx + 0.34f, hy = dy - 0.34f;
                float hl = Mathf.Exp(-(hx * hx + hy * hy) / 0.018f);
                float a = FXMath.Clamp01(rim * 0.9f + body + hl);
                if (r > 0.98f) a = 0f;
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            return Finish("FX_Bubble", size, size, px, TextureWrapMode.Clamp);
        }

        /// God ray strip: gaussian across the width, fades in just below the surface (v = 1) and dies out with length.
        public static Texture2D GodRay(int w, int h)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;   // 1 = top (surface)
                float s = 1f - v;
                float vertical = FXMath.SmoothRange(0f, 0.07f, s) * (1f - FXMath.SmoothRange(0.25f, 1f, s));
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w - 0.5f;
                    float across = Mathf.Exp(-(u * u) / (2f * 0.17f * 0.17f));
                    // soften the hard edges at u = +-0.5
                    across *= 1f - FXMath.SmoothRange(0.38f, 0.5f, Mathf.Abs(u));
                    float a = FXMath.Clamp01(across * vertical);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            return Finish("FX_GodRay", w, h, px, TextureWrapMode.Clamp);
        }

        static Texture2D Finish(string name, int w, int h, Color32[] px, TextureWrapMode wrap)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// Transparent URP Particles/Unlit material (alpha blended or additive), two-sided, no depth write.
        public static Material ParticleMaterial(string name, Texture2D tex, bool additive, Color tint)
        {
            Shader sh = MaterialLibrary.ParticlesUnlit;
            if (sh == null) sh = MaterialLibrary.UnlitShader;
            if (sh == null) return null;   // MaterialLibrary already logged the missing shader
            var m = new Material(sh) { name = name };
            m.SetTexture("_BaseMap", tex);
            m.SetTexture("_MainTex", tex);
            m.SetColor("_BaseColor", tint);
            m.SetColor("_Color", tint);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }
    }
}
