using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// K.7 / Q90: the sponsor's fantasy logo on the suit's left shoulder while a deal runs (the boat flag shows the
    /// pennant). The channel is shared, so every machine derives the patch from the SHARED deals – no wire data.
    public static class DiverSponsor
    {
        const int TexSize = 64;
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { mats.Clear(); }

        /// 1 + index into SponsorDatabase.Sponsors of the first active deal's sponsor, 0 = none / no game.
        public static int CurrentIndex
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null || !gm.InGame) return 0;
                var act = Deals.Active;
                if (act == null || act.Count == 0 || act[0] == null || string.IsNullOrEmpty(act[0].sponsorId)) return 0;
                var list = SponsorDatabase.Sponsors;
                for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].id == act[0].sponsorId) return i + 1;
                return 0;
            }
        }

        public static SponsorDef Def(int index)
        {
            var list = SponsorDatabase.Sponsors;
            return index > 0 && index <= list.Count ? list[index - 1] : null;
        }

        /// Patch material (sponsor colour, darker border, the 1–2 letter logo), built once per sponsor.
        public static Material Material(SponsorDef sp)
        {
            if (sp == null) return null;
            if (mats.TryGetValue(sp.id, out var m) && m != null) return m;
            m = MaterialLibrary.Textured(PatchTexture(sp), Color.white, 0.2f, 0f);
            m.name = "SB_SponsorPatch_" + sp.id;
            mats[sp.id] = m;
            return m;
        }

        static Texture2D PatchTexture(SponsorDef sp)
        {
            const int S = TexSize;
            var px = new Color32[S * S];
            string text = string.IsNullOrEmpty(sp.logo) ? "?" : sp.logo;
            Color bg = sp.color;
            bg.a = 1f;
            float lum = bg.r * 0.3f + bg.g * 0.59f + bg.b * 0.11f;
            Color ink = lum > 0.62f ? LowPoly.Hex("#1E1B2B") : Color.white;
            Color border = Color.Lerp(bg, Color.black, 0.35f);
            float tw = PropFont.Width(text), th = 7f;
            float cell = Mathf.Min(46f / Mathf.Max(1f, tw), 34f / th);
            float x0 = (S - tw * cell) * 0.5f, y0 = (S - th * cell) * 0.5f;
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int edge = Mathf.Min(Mathf.Min(x, y), Mathf.Min(S - 1 - x, S - 1 - y));
                    Color c = edge < 4 ? border : bg;
                    float tx = (x + 0.5f - x0) / cell, ty = (S - 1 - y + 0.5f - y0) / cell;   // text space: rows from the top
                    float cov = PropFont.Coverage(text, tx, ty, 0.62f, 1.2f / cell);
                    c = Color.Lerp(c, ink, cov);
                    c.a = 1f;
                    px[y * S + x] = c;
                }
            }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "SB_SponsorPatch_" + sp.id, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }
    }
}
