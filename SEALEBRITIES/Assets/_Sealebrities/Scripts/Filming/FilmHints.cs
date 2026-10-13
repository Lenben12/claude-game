using UnityEngine;

namespace Sealebrities
{
    /// Viewfinder hint vocabulary (Q30 → K.2): one icon + one or two words per ShotHint, so every viewfinder (ui), the
    /// selfie frame and the 3D camera screen say the same thing. Strings are cached per language and key binding –
    /// nothing is allocated per frame. Icon ids: "rec", "fish", "lamp", "zoom", "sealog", "star", "" (none).
    public static class FilmHints
    {
        static readonly string[] text = new string[8];
        static int cacheKey = int.MinValue;

        // HUD palette (art bible §1.14)
        static readonly Color White = new Color(1f, 1f, 1f, 0.95f);
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.7f);
        static readonly Color Leaf = new Color(0.608f, 0.898f, 0.392f);     // #9BE564
        static readonly Color Sun = new Color(0.949f, 0.788f, 0.298f);      // #F2C94C
        static readonly Color Warn = new Color(1f, 0.702f, 0.278f);         // #FFB347
        static readonly Color Danger = new Color(1f, 0.353f, 0.353f);       // #FF5A5A

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cacheKey = int.MinValue; for (int i = 0; i < text.Length; i++) text[i] = null; }

        /// One short line for the hint ("Foto fürs Sealog!", "Näher / Zoom", "[F] Licht!" …); "" for None.
        public static string Text(ShotHint h)
        {
            int key = GameInput.BindingsVersion * 31 + (int)Loc.Current;
            if (key != cacheKey)
            {
                cacheKey = key;
                string lamp = "[" + GameInput.BindingLabel(GameAction.LightToggle) + "] ";
                text[(int)ShotHint.None] = "";
                text[(int)ShotHint.Recording] = "REC";
                text[(int)ShotHint.NoAnimals] = Loc.Tr("Kein Tier im Bild", "No animal in frame");
                text[(int)ShotHint.TooDark] = lamp + Loc.Tr("Licht!", "Light!");
                text[(int)ShotHint.Closer] = Loc.Tr("Näher ran / Zoom", "Closer / zoom");
                text[(int)ShotHint.SealogReady] = Loc.Tr("Foto fürs Sealog!", "Photo for the Sealog!");
                text[(int)ShotHint.New] = Loc.Tr("NEU! Foto fürs Sealog!", "NEW! Photo for the Sealog!");
            }
            int k = (int)h;
            return k >= 0 && k < text.Length && text[k] != null ? text[k] : "";
        }

        /// Icon id for the hint.
        public static string Icon(ShotHint h)
        {
            switch (h)
            {
                case ShotHint.Recording: return "rec";
                case ShotHint.NoAnimals: return "fish";
                case ShotHint.TooDark: return "lamp";
                case ShotHint.Closer: return "zoom";
                case ShotHint.SealogReady: return "sealog";
                case ShotHint.New: return "star";
                default: return "";
            }
        }

        /// Text / frame colour for the hint (Sealog frame green when ready, gold for a new species).
        public static Color Tint(ShotHint h)
        {
            switch (h)
            {
                case ShotHint.Recording: return Danger;
                case ShotHint.NoAnimals: return Dim;
                case ShotHint.TooDark: return Warn;
                case ShotHint.SealogReady: return Leaf;
                case ShotHint.New: return Sun;
                default: return White;
            }
        }
    }
}
