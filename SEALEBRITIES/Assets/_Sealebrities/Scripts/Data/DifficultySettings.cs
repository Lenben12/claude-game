namespace Sealebrities
{
    public struct DifficultySettings
    {
        public float airMultiplier;         // tank duration multiplier
        public long rescueFee;              // money lost when fainting
        public bool narcosis;               // nitrogen narcosis hallucinations from ~30 m
        public float predatorAggression;    // 0..2, chance/strength of predator bumps
        public float incomeMultiplier;      // views/money multiplier
        public bool permadeath;             // fainting ends the save (Abyss)
        public bool canFaint;               // Snorkeler: a rescue drone pulls you up before fainting

        public static DifficultySettings For(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Snorkeler:
                    return new DifficultySettings { airMultiplier = 2f, rescueFee = 0, narcosis = false, predatorAggression = 0.3f, incomeMultiplier = 1.3f, canFaint = false };
                case Difficulty.OpenWater:
                    return new DifficultySettings { airMultiplier = 1f, rescueFee = 40, narcosis = false, predatorAggression = 1f, incomeMultiplier = 1f, canFaint = true };
                case Difficulty.Advanced:
                    return new DifficultySettings { airMultiplier = 0.9f, rescueFee = 120, narcosis = true, predatorAggression = 1.3f, incomeMultiplier = 0.9f, canFaint = true };
                case Difficulty.Technical:
                    return new DifficultySettings { airMultiplier = 0.8f, rescueFee = 300, narcosis = true, predatorAggression = 1.6f, incomeMultiplier = 0.8f, canFaint = true };
                default:
                    return new DifficultySettings { airMultiplier = 0.75f, rescueFee = 500, narcosis = true, predatorAggression = 2f, incomeMultiplier = 0.75f, canFaint = true, permadeath = true };
            }
        }

        public static string Description(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Snorkeler:
                    return Loc.Tr("Sterben unmöglich: Eine Rettungsdrohne holt dich hoch. Doppelte Luft, mehr Einnahmen.",
                                  "No way to die: a rescue drone pulls you up. Double air, more income.");
                case Difficulty.OpenWater:
                    return Loc.Tr("Standard. Ohne Luft wirst du ohnmächtig, das Boot zieht dich hoch. Die Aufnahmen des Tauchgangs sind weg, die Rettung kostet 40 $.",
                                  "Standard. Run out of air and you faint, the boat pulls you up. That dive's footage is lost and the rescue costs $40.");
                case Difficulty.Advanced:
                    return Loc.Tr("Weniger Luft, aggressivere Raubtiere, teure Rettung. Ab 30 m Stickstoffnarkose: Du siehst Dinge, die nicht da sind.",
                                  "Less air, more aggressive predators, expensive rescue. Nitrogen narcosis from 30 m: you see things that aren't there.");
                case Difficulty.Technical:
                    return Loc.Tr("Noch weniger Luft, harte Raubtiere, Narkose. Für erfahrene Taucher.",
                                  "Even less air, tough predators, narcosis. For experienced divers.");
                default:
                    return Loc.Tr("Permadeath: Wirst du ohnmächtig, ist der Spielstand verloren. Narkose, wenig Luft, gnadenlose Raubtiere.",
                                  "Permadeath: if you faint, the save is gone. Narcosis, little air, merciless predators.");
            }
        }
    }
}
