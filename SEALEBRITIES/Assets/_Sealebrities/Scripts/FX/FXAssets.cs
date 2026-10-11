using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Procedural textures and transparent particle materials used by the FX module.
    /// Low-poly look: clean graphic shapes with crisp anti-aliased edges instead of soft photographic blobs.
    static class FXAssets
    {
        /// Clean round speck: solid white disc with a ~1 px anti-aliased edge.
        public static Texture2D CleanDot(int size)
        {
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float aa = 1.2f / half;   // edge width in normalized radius units
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = FXMath.Clamp01((0.9f - r) / aa + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            return Finish("FX_CleanDot", size, size, px, TextureWrapMode.Clamp);
        }

        /// Cartoon air bubble: crisp white outline, faint see-through body and a small solid highlight.
        public static Texture2D CleanBubble(int size)
        {
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float aa = 1.1f / half;
            const float Outer = 0.92f, Inner = 0.74f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float inside = Coverage(Outer - r, aa);          // whole bubble disc
                float rim = inside * Coverage(r - Inner, aa);     // ring between Inner and Outer
                float hx = dx + 0.3f, hy = dy - 0.3f;             // highlight up-left
                float hl = Coverage(0.17f - Mathf.Sqrt(hx * hx + hy * hy), aa);
                float a = Mathf.Max(Mathf.Max(rim * 0.95f, hl), inside * 0.2f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(FXMath.Clamp01(a) * 255f));
            }
            return Finish("FX_CleanBubble", size, size, px, TextureWrapMode.Clamp);
        }

        /// Faceted low-poly air bubble (art bible §2.3): an octagon with a crisp white rim, a faint see-through body
        /// and a small triangular highlight facet up-left. Read as white at ~60 % alpha through the particle colour.
        public static Texture2D FacetBubble(int size)
        {
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float aa = 1.1f / half;
            const int Sides = 8;
            const float Outer = 0.90f, Inner = 0.70f;
            // edge normals of the octagon (flat side on top)
            var nx = new float[Sides];
            var ny = new float[Sides];
            for (int k = 0; k < Sides; k++)
            {
                float a = (k + 0.5f) * FXMath.TwoPi / Sides + FXMath.TwoPi / (2f * Sides);
                nx[k] = Mathf.Cos(a);
                ny[k] = Mathf.Sin(a);
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = 0f;   // polygon "radius": the largest projection on an edge normal
                for (int k = 0; k < Sides; k++)
                {
                    float p = dx * nx[k] + dy * ny[k];
                    if (p > r) r = p;
                }
                float inside = Coverage(Outer - r, aa);
                float rim = inside * Coverage(r - Inner, aa);
                // highlight facet: a small triangle in the upper-left quadrant
                float hx = dx + 0.36f, hy = dy - 0.30f;
                float tri = Mathf.Max(Mathf.Max(-hy - 0.02f, hx - 0.17f), hy - hx * 0.9f - 0.16f);
                float hl = inside * Coverage(-tri, aa);
                float a2 = Mathf.Max(Mathf.Max(rim * 0.95f, hl), inside * 0.18f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(FXMath.Clamp01(a2) * 255f));
            }
            return Finish("FX_FacetBubble", size, size, px, TextureWrapMode.Clamp);
        }

        /// Bioluminescent spark: a crisp bright core with a short soft glow (additive).
        public static Texture2D Spark(int size)
        {
            var px = new Color32[size * size];
            float half = size * 0.5f;
            float aa = 1.2f / half;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float core = Coverage(0.32f - r, aa);
                float g = FXMath.Clamp01(1f - r);
                float glow = g * g * 0.45f;
                float a = Mathf.Max(core, glow);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(FXMath.Clamp01(a) * 255f));
            }
            return Finish("FX_Spark", size, size, px, TextureWrapMode.Clamp);
        }

        /// Signed distance (positive = inside) to 0..1 coverage over an anti-aliasing width.
        static float Coverage(float sd, float aa) => FXMath.Clamp01(sd / aa + 0.5f);

        /// God ray strip: a flat band with soft edges across the width (graphic, not gaussian), fading in just
        /// below the surface (v = 1) and dying out with length.
        public static Texture2D GodRay(int w, int h)
        {
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h;   // 1 = top (surface)
                float s = 1f - v;
                float vertical = FXMath.SmoothRange(0f, 0.06f, s) * (1f - FXMath.SmoothRange(0.3f, 1f, s));
                for (int x = 0; x < w; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / w - 0.5f);
                    float across = 1f - FXMath.SmoothRange(0.26f, 0.48f, u);
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
