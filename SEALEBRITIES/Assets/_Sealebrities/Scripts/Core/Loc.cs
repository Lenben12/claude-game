using System.Globalization;

namespace Sealebrities
{
    /// Inline localization: every UI string is written as Loc.Tr("Deutsch", "English") at the call site.
    public static class Loc
    {
        public static Language Current = Language.German;

        static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

        public static CultureInfo Culture => Current == Language.German ? De : En;

        public static string Tr(string de, string en) => Current == Language.German ? de : en;

        /// Switches the UI language (stored in Settings, per machine).
        public static void SetLanguage(Language language)
        {
            if (Current == language) return;
            Current = language;
            if (Settings.Data.language != language)
            {
                Settings.Data.language = language;
                Settings.Save();
            }
            if (GameManager.I != null) GameManager.I.State.language = language;
            GameEvents.RaiseLanguageChanged();
        }

        public static string Number(long n) => n.ToString("N0", Culture);

        public static string Money(long n) => Number(n) + " $";

        /// 8 cm / 64 cm / 1,84 m / 11,20 m
        public static string Length(float cm)
        {
            if (cm >= 100f) return (cm / 100f).ToString("0.00", Culture) + " m";
            return cm.ToString("0", Culture) + " cm";
        }

        public static string Meters(float m) => m.ToString("0", Culture) + " m";

        public static string Percent(float f01) => (f01 * 100f).ToString("0", Culture) + " %";

        public static string TierName(SizeTier t)
        {
            switch (t)
            {
                case SizeTier.Tiny: return Tr("Winzig", "Tiny");
                case SizeTier.Normal: return Tr("Normal", "Normal");
                case SizeTier.Large: return Tr("Groß", "Large");
                case SizeTier.Huge: return Tr("Riesig", "Huge");
                default: return Tr("Legendär", "Legendary");
            }
        }

        public static string RarityName(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return Tr("Häufig", "Common");
                case Rarity.Uncommon: return Tr("Selten", "Uncommon");
                case Rarity.Rare: return Tr("Sehr selten", "Rare");
                case Rarity.Epic: return Tr("Episch", "Epic");
                default: return Tr("Legendär", "Legendary");
            }
        }

        /// Entspannt / Normal / Schwer (old Technical/Abyss saves count as Schwer).
        public static string DifficultyName(Difficulty d)
        {
            switch (DifficultySettings.Normalize(d))
            {
                case Difficulty.Snorkeler: return Tr("Entspannt", "Relaxed");
                case Difficulty.OpenWater: return Tr("Normal", "Normal");
                default: return Tr("Schwer", "Hard");
            }
        }

        public static string TitleStyleName(TitleStyle s)
        {
            switch (s)
            {
                case TitleStyle.Honest: return Tr("Ehrlich", "Honest");
                case TitleStyle.Spicy: return Tr("Spicy", "Spicy");
                default: return "Clickbait";
            }
        }
    }
}
