using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Creates (main thread) the matte low-poly materials of a creature from its bake: point-filtered color-block
    /// textures for skin / fins / shell (bilinear for smooth "character" skins), creature-owned solid materials for
    /// eyes, pupils, mouth and teeth. Every opaque creature material gets an emission channel (albedo × floor) driven by
    /// CreatureGlow (G.2 – creatures stay readable and keep their colours in the deep). GPU instancing is on for all
    /// (the swarm renderer draws school buddies with them).
    internal static class CreatureMeshMaterials
    {
        public const float Smooth = 0.1f;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Smoothness = Shader.PropertyToID("_Smoothness");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");

        /// Creature-owned solid materials (eyes, mouth, teeth) per colour + water/land, shared between species.
        static readonly Dictionary<string, Material> Solids = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Solids.Clear(); }

        /// Small point-filtered texture (crisp color blocks, no mips); v wraps around lofted bodies.
        public static Texture2D BlockTexture(CreatureMeshImage img, string name, bool wrapV, bool bilinear = false)
        {
            var tex = LowPoly.BlockTexture(img.W, img.H, img.C, name);
            tex.wrapModeU = TextureWrapMode.Clamp;
            tex.wrapModeV = wrapV ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            if (bilinear) tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        /// Materials in sub-mesh order (one per used slot).
        public static Material[] For(CreatureMeshBake bake, int[] slots, List<Object> owned)
        {
            var ctx = bake.Ctx;
            var s = ctx.S;
            string id = s.id;
            bool land = s.land;
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
                                var tex = BlockTexture(bake.Skin, "SB_Skin_" + id, ctx.Layout == CreatureMeshLayout.Loft, ctx.SmoothTexture);
                                owned.Add(tex);
                                skin = MaterialLibrary.Textured(tex, Color.white, ctx.SkinSmoothness, 0f);
                                AddEmissionMap(skin, tex);
                                CreatureGlow.Register(skin, Color.white, land, false, 0f);
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
                            AddEmissionMap(fin, tex);
                            CreatureGlow.Register(fin, Color.white, land, false, 0f);
                            fin.name = "SB_CreatureFin_" + id;
                            owned.Add(fin);
                        }
                        mats[i] = fin;
                        break;
                    case CreatureMeshSlot.Eye:
                        mats[i] = Solid(look.Sclera, 0.35f, land);
                        break;
                    case CreatureMeshSlot.Pupil:
                        mats[i] = Solid(look.Iris, 0.5f, land);
                        break;
                    case CreatureMeshSlot.Mouth:
                        mats[i] = Solid(CreatureMeshColors.Mouth, Smooth, land);
                        break;
                    case CreatureMeshSlot.Teeth:
                        mats[i] = Solid(CreatureMeshColors.Tooth, 0.3f, land);
                        break;
                    case CreatureMeshSlot.Glow:
                    {
                        Color c = CreatureMeshColors.Glow(ctx);
                        var m = new Material(MaterialLibrary.Lit) { name = "SB_CreatureGlow_" + id };
                        m.SetColor(BaseColor, c);
                        float strength = s.plan == BodyPlan.Anglerfish ? 4f : 3f;
                        m.SetColor(EmissionColor, c * strength);
                        m.EnableKeyword("_EMISSION");
                        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        m.SetFloat(Smoothness, 0.3f);
                        m.enableInstancing = true;
                        // G.2: glowing species pulse ±15 % at 0.5 Hz
                        if (s.glows) CreatureGlow.RegisterPulse(m, c * strength);
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
                            AddEmissionMap(m, tex);
                            CreatureGlow.Register(m, Color.white, land, false, 0f);
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

        /// Emission = base texture × _EmissionColor (set by CreatureGlow).
        static void AddEmissionMap(Material m, Texture2D tex)
        {
            m.SetTexture(EmissionMap, tex);
            m.SetColor(EmissionColor, Color.black);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
        }

        /// Shared creature-owned solid material (never MaterialLibrary's cache: the glow floor changes its emission).
        static Material Solid(Color c, float smooth, bool land)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + (land ? "L" : "W") + smooth.ToString("F2");
            if (Solids.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(MaterialLibrary.Lit) { name = "SB_CreatureSolid_" + key };
            m.SetColor(BaseColor, c);
            m.SetFloat(Smoothness, smooth);
            m.SetColor(EmissionColor, Color.black);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.enableInstancing = true;
            CreatureGlow.Register(m, c, land, true, 0f);
            Solids[key] = m;
            return m;
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
