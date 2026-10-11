namespace Sealebrities
{
    public struct DifficultySettings
    {
        public float airMultiplier;         // tank duration multiplier
        public long rescueFee;              // money lost when fainting
        public bool narcosis;               // nitrogen narcosis hallucinations from ~30 m
        public float predatorAggression;    // 0..2, chance/strength of predator bumps
        public float incomeMultiplier;      // views/money multiplier (uploads and fish)
        public bool permadeath;             // legacy, no selectable level uses it
        public bool canFaint;               // Entspannt: a rescue drone pulls you up before fainting

        /// The three levels offered in the menu: Entspannt, Normal, Schwer.
        public static readonly Difficulty[] Selectable = { Difficulty.Snorkeler, Difficulty.OpenWater, Difficulty.Advanced };

        /// Maps old/invalid values onto a selectable level (Technical/Abyss → Advanced = Schwer).
        public static Difficulty Normalize(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Snorkeler: return Difficulty.Snorkeler;
                case Difficulty.OpenWater: return Difficulty.OpenWater;
                case Difficulty.Advanced:
                case Difficulty.Technical:
                case Difficulty.Abyss: return Difficulty.Advanced;
                default: return Difficulty.OpenWater;
            }
        }

        public static DifficultySettings For(Difficulty d)
        {
            switch (Normalize(d))
            {
                case Difficulty.Snorkeler:
                    return new DifficultySettings { airMultiplier = 1.35f, rescueFee = 0, narcosis = false, predatorAggression = 0.4f, incomeMultiplier = 1.2f, canFaint = false };
                case Difficulty.Advanced:
                    return new DifficultySettings { airMultiplier = 0.85f, rescueFee = 75, narcosis = true, predatorAggression = 1.35f, incomeMultiplier = 0.9f, canFaint = true };
                default:
                    return new DifficultySettings { airMultiplier = 1f, rescueFee = 25, narcosis = false, predatorAggression = 1f, incomeMultiplier = 1f, canFaint = true };
            }
        }

        /// Entspannt / Normal / Schwer.
        public static string Name(Difficulty d) => Loc.DifficultyName(d);

        /// One short line for the menu card.
        public static string Description(Difficulty d)
        {
            switch (Normalize(d))
            {
                case Difficulty.Snorkeler:
                    return Loc.Tr("Keine Ohnmacht – eine Drohne holt dich hoch. Mehr Luft, mehr Einnahmen.",
                                  "No fainting – a drone pulls you up. More air, more income.");
                case Difficulty.Advanced:
                    return Loc.Tr("Weniger Luft, bissige Raubtiere, teure Rettung. Ab 30 m siehst du Dinge, die nicht da sind.",
                                  "Less air, snappy predators, costly rescue. From 30 m you see things that aren't there.");
                default:
                    return Loc.Tr("Ohne Luft wirst du ohnmächtig: Die Clips des Tauchgangs sind weg, die Rettung kostet 25 $.",
                                  "Run out of air and you faint: that dive's clips are lost and the rescue costs $25.");
            }
        }
    }
}
