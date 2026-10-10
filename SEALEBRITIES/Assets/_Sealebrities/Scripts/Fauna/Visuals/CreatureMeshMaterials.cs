using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Creates (main thread) the matte low-poly materials of a creature from its bake: point-filtered color-block
    /// textures for skin / fins / shell, shared solid materials for eyes, pupils, mouth and teeth.
    internal static class CreatureMeshMaterials
    {
        public const float Smooth = 0.1f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        /// Small point-filtered texture (crisp color blocks, no mips); v wraps around lofted bodies.
        public static Texture2D BlockTexture(CreatureMeshImage img, string name, bool wrapV)
        {
            var tex = LowPoly.BlockTexture(img.W, img.H, img.C, name);
            tex.wrapModeU = TextureWrapMode.Clamp;
            tex.wrapModeV = wrapV ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            return tex;
        }

        /// Materials in sub-mesh order (one per used slot).
        public static Material[] For(CreatureMeshBake bake, int[] slots, List<Object> owned)
        {
            var ctx = bake.Ctx;
            var s = ctx.S;
            string id = s.id;
            var mats = new Material[slots.Length];
            var look = CreatureMeshEyes.Look(ctx.EyeStyle);
            Material skin = null, fin = null;

            for (int i = 0; i < slots.Length; i++)
            {
                switch (slots[i])
                {
                    case CreatureMeshSlot.Skin:
                        if (skin == null)
                        {
                            if (ctx.Layout == CreatureMeshLayout.Bell) skin = JellyBell(s);
                            else
                            {
                                var tex = BlockTexture(bake.Skin, "SB_Skin_" + id, ctx.Layout == CreatureMeshLayout.Loft);
                                owned.Add(tex);
                                skin = MaterialLibrary.Textured(tex, Color.white, ctx.SkinSmoothness, 0f);
                            }
                            skin.name = "SB_CreatureSkin_" + id;
                            owned.Add(skin);
                        }
                        mats[i] = skin;
                        break;
                    case CreatureMeshSlot.Fin:
                        if (fin == null)
                        {
                            var tex = BlockTexture(bake.Fins, "SB_Fin_" + id, false);
                            owned.Add(tex);
                            fin = MaterialLibrary.Textured(tex, Color.white, Smooth, 0f);
                            fin.name = "SB_CreatureFin_" + id;
                            owned.Add(fin);
                        }
                        mats[i] = fin;
                        break;
                    case CreatureMeshSlot.Eye:
                        mats[i] = MaterialLibrary.Opaque(look.Sclera, 0.35f, 0f);
                        break;
                    case CreatureMeshSlot.Pupil:
                        mats[i] = MaterialLibrary.Opaque(look.Iris, 0.5f, 0f);
                        break;
                    case CreatureMeshSlot.Mouth:
                        mats[i] = MaterialLibrary.Opaque(CreatureMeshColors.Mouth, Smooth, 0f);
                        break;
                    case CreatureMeshSlot.Teeth:
                        mats[i] = MaterialLibrary.Opaque(CreatureMeshColors.Tooth, 0.3f, 0f);
                        break;
                    case CreatureMeshSlot.Glow:
                    {
                        Color c = CreatureMeshColors.Glow(ctx);
                        var m = new Material(MaterialLibrary.Lit) { name = "SB_CreatureGlow_" + id };
                        m.SetColor(BaseColor, c);
                        m.SetColor(EmissionColor, c * (s.plan == BodyPlan.Anglerfish ? 4f : 3f));
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        m.SetFloat(Smoothness, 0.3f);
                        owned.Add(m);
                        mats[i] = m;
                        break;
                    }
                    case CreatureMeshSlot.Shell:
                    {
                        Material m;
                        if (s.plan == BodyPlan.Turtle && bake.Shell != null)
                        {
                            var tex = BlockTexture(bake.Shell, "SB_Shell_" + id, false);
                            owned.Add(tex);
                            m = MaterialLibrary.Textured(tex, Color.white, 0.14f, 0f);
                        }
                        else
                        {
                            // jelly gonads: denser pastel, faint glow
                            m = new Material(MaterialLibrary.Lit);
                            Color c = CreatureMeshColors.Shell(s);
                            m.SetColor(BaseColor, c);
                            m.SetFloat(Smoothness, 0.3f);
                            MaterialLibrary.MakeTransparent(m, false);
                            m.SetColor(EmissionColor, new Color(c.r, c.g, c.b) * (s.glows ? 0.35f : 0.12f));
                            m.EnableKeyword("_EMISSION");
                        }
                        m.name = "SB_CreatureShell_" + id;
                        owned.Add(m);
                        mats[i] = m;
                        break;
                    }
                    default:
                    {
                        // jelly tentacles / oral arms: transparent pastel, both sides
                        var m = new Material(MaterialLibrary.Lit) { name = "SB_CreatureExtra_" + id };
                        Color c = CreatureMeshColors.Extra(s);
                        m.SetColor(BaseColor, c);
                        m.SetFloat(Smoothness, 0.3f);
                        MaterialLibrary.MakeTransparent(m, true);
                        m.SetColor(EmissionColor, new Color(c.r, c.g, c.b) * (s.glows ? 0.22f : 0.08f));
                        m.EnableKeyword("_EMISSION");
                        owned.Add(m);
                        mats[i] = m;
                        break;
                    }
                }
            }
            return mats;
        }

        /// Transparent pastel faceted bell (outer + inner sheet are modelled, so no back faces are needed).
        static Material JellyBell(SpeciesDef s)
        {
            var m = new Material(MaterialLibrary.Lit);
            Color c = CreatureMeshColors.Bell(s);
            m.SetColor(BaseColor, c);
            m.SetFloat(Smoothness, 0.35f);
            MaterialLibrary.MakeTransparent(m, false);
            m.SetColor(EmissionColor, new Color(c.r, c.g, c.b) * (s.glows ? 0.28f : 0.08f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }
    }
}
