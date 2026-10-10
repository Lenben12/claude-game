using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Creates (main thread) the textures and URP/Lit materials of a creature from its bake.
    /// Per-species materials are cached by the kit; eye / mouth / teeth materials are shared globally.
    internal static class CreatureMeshMaterials
    {
        static readonly Dictionary<CreatureMeshEyeStyle, Material> EyeMats = new Dictionary<CreatureMeshEyeStyle, Material>();
        static Material mouth, teeth;

        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        static readonly int BumpScale = Shader.PropertyToID("_BumpScale");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { EyeMats.Clear(); mouth = null; teeth = null; }

        public static Texture2D Texture(string name, int w, int h, Color32[] px, bool linear, TextureWrapMode wrapU, TextureWrapMode wrapV)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapModeU = wrapU,
                wrapModeV = wrapV,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// Materials in sub-mesh order (one per used slot).
        public static Material[] For(CreatureMeshBake bake, int[] slots, List<Object> owned)
        {
            var ctx = bake.Ctx;
            var s = ctx.S;
            string id = s.id;
            var mats = new Material[slots.Length];
            Texture2D skinTex = null, skinN = null, finTex = null, finN = null;
            Material skin = null, fin = null;

            for (int i = 0; i < slots.Length; i++)
            {
                switch (slots[i])
                {
                    case CreatureMeshSlot.Skin:
                        if (skin == null)
                        {
                            bool bell = ctx.Layout == CreatureMeshLayout.Bell;
                            skinTex = Texture("SB_Skin_" + id, bake.SkinW, bake.SkinH, bake.Skin, false,
                                bell ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, ctx.Layout == CreatureMeshLayout.Loft ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
                            skinN = Texture("SB_SkinN_" + id, bake.SkinW, bake.SkinH, bake.SkinNormal, true,
                                bell ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, ctx.Layout == CreatureMeshLayout.Loft ? TextureWrapMode.Repeat : TextureWrapMode.Clamp);
                            owned.Add(skinTex); owned.Add(skinN);
                            if (bell) skin = JellyBell(s, skinTex, skinN);
                            else
                            {
                                skin = MaterialLibrary.Textured(skinTex, Color.white, ctx.SkinSmoothness, 0f, null, skinN);
                                skin.SetFloat(BumpScale, 1f);
                            }
                            skin.name = "SB_CreatureSkin_" + id;
                            owned.Add(skin);
                        }
                        mats[i] = skin;
                        break;
                    case CreatureMeshSlot.Fin:
                        if (fin == null)
                        {
                            finTex = Texture("SB_Fin_" + id, bake.FinW, bake.FinH, bake.Fins, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
                            finN = Texture("SB_FinN_" + id, bake.FinW, bake.FinH, bake.FinNormal, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
                            owned.Add(finTex); owned.Add(finN);
                            fin = MaterialLibrary.Textured(finTex, Color.white, ctx.FinSmoothness, 0f, null, finN);
                            fin.name = "SB_CreatureFin_" + id;
                            owned.Add(fin);
                        }
                        mats[i] = fin;
                        break;
                    case CreatureMeshSlot.Eye:
                        mats[i] = Eye(ctx.EyeStyle);
                        break;
                    case CreatureMeshSlot.Mouth:
                        if (mouth == null) mouth = MaterialLibrary.Opaque(new Color(0.17f, 0.05f, 0.05f), 0.62f);
                        mats[i] = mouth;
                        break;
                    case CreatureMeshSlot.Teeth:
                        if (teeth == null) teeth = MaterialLibrary.Opaque(new Color(0.93f, 0.9f, 0.82f), 0.72f);
                        mats[i] = teeth;
                        break;
                    case CreatureMeshSlot.Glow:
                    {
                        Color c = s.plan == BodyPlan.Anglerfish ? ctx.LureColor : s.colorC;
                        c.a = 1f;
                        var m = new Material(MaterialLibrary.Lit) { name = "SB_CreatureGlow_" + id };
                        m.SetColor(BaseColor, c);
                        m.SetColor(EmissionColor, c * (s.plan == BodyPlan.Anglerfish ? 4f : 3f));
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        m.SetFloat(Smoothness, 0.8f);
                        owned.Add(m);
                        mats[i] = m;
                        break;
                    }
                    case CreatureMeshSlot.Shell:
                    {
                        Material m;
                        if (s.plan == BodyPlan.Turtle && bake.Shell != null)
                        {
                            var t = Texture("SB_Shell_" + id, bake.ShellW, bake.ShellH, bake.Shell, false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
                            var n = Texture("SB_ShellN_" + id, bake.ShellW, bake.ShellH, bake.ShellNormal, true, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
                            owned.Add(t); owned.Add(n);
                            m = MaterialLibrary.Textured(t, Color.white, 0.48f, 0f, null, n);
                        }
                        else
                        {
                            // jelly organs: denser translucent color with a soft glow
                            m = new Material(MaterialLibrary.Lit);
                            Color c = s.colorB; c.a = 0.7f;
                            m.SetColor(BaseColor, c);
                            m.SetFloat(Smoothness, 0.85f);
                            MaterialLibrary.MakeTransparent(m, false);
                            m.SetColor(EmissionColor, s.colorB * (s.glows ? 0.45f : 0.15f));
                            m.EnableKeyword("_EMISSION");
                        }
                        m.name = "SB_CreatureShell_" + id;
                        owned.Add(m);
                        mats[i] = m;
                        break;
                    }
                    default:
                    {
                        // jelly tentacles / oral arms
                        var m = new Material(MaterialLibrary.Lit) { name = "SB_CreatureExtra_" + id };
                        Color c = Color.Lerp(s.colorC, Color.white, 0.2f); c.a = 0.42f;
                        m.SetColor(BaseColor, c);
                        m.SetFloat(Smoothness, 0.85f);
                        MaterialLibrary.MakeTransparent(m, true);
                        m.SetColor(EmissionColor, s.colorC * (s.glows ? 0.25f : 0.08f));
                        m.EnableKeyword("_EMISSION");
                        owned.Add(m);
                        mats[i] = m;
                        break;
                    }
                }
            }
            return mats;
        }

        static Material JellyBell(SpeciesDef s, Texture2D tex, Texture2D normal)
        {
            var m = new Material(MaterialLibrary.Lit);
            m.SetTexture(BaseMap, tex);
            m.SetColor(BaseColor, Color.white);
            m.SetFloat(Smoothness, 0.9f);
            m.SetTexture("_BumpMap", normal);
            m.EnableKeyword("_NORMALMAP");
            m.SetFloat(BumpScale, 0.6f);
            // the bell is modelled as an outer and an inner sheet, so back faces are not needed
            MaterialLibrary.MakeTransparent(m, false);
            m.SetTexture(EmissionMap, tex);
            m.SetColor(EmissionColor, s.colorA * (s.glows ? 0.35f : 0.1f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        public static Material Eye(CreatureMeshEyeStyle style)
        {
            if (EyeMats.TryGetValue(style, out var m) && m != null) return m;
            var img = CreatureMeshPaint.Eye(style, 128);
            var tex = Texture("SB_Eye_" + style, img.W, img.H, img.ToColor32(), false, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            m = MaterialLibrary.Textured(tex, Color.white, 0.93f, 0f);
            m.name = "SB_CreatureEye_" + style;
            EyeMats[style] = m;
            return m;
        }
    }
}
