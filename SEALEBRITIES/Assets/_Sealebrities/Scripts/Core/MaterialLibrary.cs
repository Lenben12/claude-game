using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Creates URP materials at runtime. All shaders used here are referenced by materials in
    /// Resources/ShaderIncludes (created by the editor setup) so they survive build stripping.
    public static class MaterialLibrary
    {
        static Shader lit, unlit, terrainLit, particles;
        static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        public static Shader Lit => lit != null ? lit : (lit = Find("Universal Render Pipeline/Lit", "Standard"));
        public static Shader UnlitShader => unlit != null ? unlit : (unlit = Find("Universal Render Pipeline/Unlit", "Unlit/Color"));
        public static Shader TerrainLit => terrainLit != null ? terrainLit : (terrainLit = Find("Universal Render Pipeline/Terrain/Lit", "Nature/Terrain/Standard"));
        public static Shader ParticlesUnlit => particles != null ? particles : (particles = Find("Universal Render Pipeline/Particles/Unlit", "Particles/Standard Unlit"));

        static Shader Find(string name, string fallback)
        {
            var s = Shader.Find(name);
            if (s == null) s = Shader.Find(fallback);
            if (s == null) Debug.LogError("Sealebrities: shader not found: " + name);
            return s;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); lit = null; unlit = null; terrainLit = null; particles = null; }

        static string Key(Color c, float s, float m) => $"{c.r:F3}{c.g:F3}{c.b:F3}{c.a:F3}_{s:F2}_{m:F2}";

        /// Shared opaque material (cached by color/smoothness/metallic so identical props batch together).
        public static Material Opaque(Color color, float smoothness = 0.3f, float metallic = 0f)
        {
            string k = "o" + Key(color, smoothness, metallic);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Lit) { name = "SB_Opaque" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.enableInstancing = true;
            Cache[k] = m;
            return m;
        }

        /// New (uncached) opaque material with a texture.
        public static Material Textured(Texture2D tex, Color tint, float smoothness = 0.25f, float metallic = 0f, Vector2? tiling = null, Texture2D normalMap = null)
        {
            var m = new Material(Lit) { name = "SB_Textured" };
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            if (tiling.HasValue) m.SetTextureScale("_BaseMap", tiling.Value);
            if (normalMap != null)
            {
                m.SetTexture("_BumpMap", normalMap);
                m.EnableKeyword("_NORMALMAP");
            }
            m.enableInstancing = true;
            return m;
        }

        public static Material Transparent(Color color, float smoothness = 0.9f, bool twoSided = false)
        {
            string k = "t" + Key(color, smoothness, twoSided ? 1 : 0);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Lit) { name = "SB_Transparent" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            MakeTransparent(m, twoSided);
            Cache[k] = m;
            return m;
        }

        public static void MakeTransparent(Material m, bool twoSided)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetFloat("_Cull", twoSided ? 0f : 2f);
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.SetShaderPassEnabled("DepthOnly", false);
        }

        public static void MakeTwoSided(Material m) => m.SetFloat("_Cull", 0f);

        public static Material Emissive(Color baseColor, Color emission, float intensity = 1f)
        {
            string k = "e" + Key(baseColor, intensity, emission.r + emission.g * 2 + emission.b * 3);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(Lit) { name = "SB_Emissive" };
            m.SetColor("_BaseColor", baseColor);
            m.SetColor("_EmissionColor", emission * intensity);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Cache[k] = m;
            return m;
        }

        public static Material Unlit(Color color, bool transparent = false)
        {
            string k = "u" + Key(color, transparent ? 1 : 0, 0);
            if (Cache.TryGetValue(k, out var m) && m != null) return m;
            m = new Material(UnlitShader) { name = "SB_Unlit" };
            m.SetColor("_BaseColor", color);
            if (transparent) MakeTransparent(m, true);
            Cache[k] = m;
            return m;
        }

        /// New URP terrain material for Terrain.materialTemplate; not cached.
        public static Material Terrain()
        {
            return new Material(TerrainLit) { name = "SB_Terrain" };
        }
    }
}
