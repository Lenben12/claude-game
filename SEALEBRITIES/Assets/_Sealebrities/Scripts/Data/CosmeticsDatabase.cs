using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// One cosmetic option of the character screen (1c) or the boat flag / hand prop (L.7).
    /// id = kind (lower case) + ":" + index, e.g. "hat:6". Free options are always unlocked.
    public class CosmeticDef
    {
        /// "Suit", "Cuff", "Pattern", "Hat", "Skin", "Mouth", "Eyes", "Brows", "Mask", "Fins", "BoatFlag", "HandProp".
        public string kind;
        public int index;
        public string id;
        public string nameDe, nameEn;
        /// Colour options (suit, cuff, skin, mask, fins); alpha 0 = "Auto"/default.
        public Color color;
        public bool free;
        public string unlockHintDe, unlockHintEn;
        /// Automatic unlock rule checked by Cosmetics.CheckRules on the host: "shells_all", "pages_all", "treasures_all",
        /// "secrets_all", "sealog_full", "tutorial", "followers:N", "achievement:ID", "gerald_days:N".
        /// Story rewards ("mission:M3", "quest:P2" …) are unlocked by the npc module with Cosmetics.Unlock(id).
        public string rule;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string UnlockHint => Loc.Tr(unlockHintDe, unlockHintEn);
    }

    /// Character-screen options (1c). Rendering lives in the Player module's DiverBuilder; ranges here are binding.
    public static class CosmeticsDatabase
    {
        public const string Suit = "Suit", Cuff = "Cuff", Pattern = "Pattern", Hat = "Hat", Skin = "Skin", Mouth = "Mouth",
            Eyes = "Eyes", Brows = "Brows", Mask = "Mask", Fins = "Fins", BoatFlag = "BoatFlag", HandProp = "HandProp",
            // round 3 (GDD §19 „mehr Optionen“)
            BodyBuild = "Build", Height = "Height", Hair = "Hair", HairColor = "HairColor", Beard = "Beard", Nose = "Nose", Accessory = "Accessory";

        public static readonly Color[] SuitColors =
        {
            Hex("#EE7F2D"), Hex("#3FB8C4"), Hex("#E0607E"), Hex("#F2C94C"), Hex("#8E6CD9"), Hex("#6E9445"),
            Hex("#4F7FAE"), Hex("#EDE6DA"), Hex("#C94F3D"), Hex("#2F8F6E"), Hex("#F29BBE"), Hex("#3A3F5C"),
            // round 3
            Hex("#B05A8A"), Hex("#E8A23A"), Hex("#7FC4AE"), Hex("#5C4E9E"), Hex("#A8B04A"), Hex("#2B2533"),
        };

        /// Round 3 hair colours (index = DiverLook.hairColor).
        public static readonly Color[] HairColors =
        {
            Hex("#4A3326"), Hex("#2A2220"), Hex("#7A5236"), Hex("#8E3B2A"), Hex("#D0702E"), Hex("#E3C27A"),
            Hex("#EDE3C8"), Hex("#9A9AA0"), Hex("#3FA9A0"), Hex("#E86FA0"),
        };

        /// Index 0 = "Auto" (alpha 0 → suit lightened 55 %).
        public static readonly Color[] CuffColors =
        {
            new Color(0f, 0f, 0f, 0f), Hex("#F4EFE6"), Hex("#F2C94C"), Hex("#3FB8C4"), Hex("#E0607E"),
            Hex("#EE7F2D"), Hex("#1E1B2B"), Hex("#6E9445"), Hex("#8E6CD9"),
        };

        /// Skin tones – index = DiverLook.skin (the first four are the round-1 tones, face / 5).
        public static readonly Color[] SkinTones =
        {
            Hex("#E8C08A"), Hex("#D6A574"), Hex("#A8724A"), Hex("#6B4636"), Hex("#F0D2B4"), Hex("#4A2F28"),
            // round 3
            Hex("#E3B98F"), Hex("#BC8055"), Hex("#87563C"), Hex("#F2C6A8"),
        };

        /// Mask frames – index 0 = default #1E1B2B, 6 = Gold (all 13 secrets).
        public static readonly Color[] MaskColors =
        {
            Hex("#1E1B2B"), Hex("#EE7F2D"), Hex("#3FB8C4"), Hex("#F2C94C"), Hex("#E0607E"), Hex("#EDE6DA"), Hex("#E8C65A"),
            // round 3
            Hex("#8E6CD9"), Hex("#6E9445"), Hex("#C94F3D"),
        };

        /// Fin colours – index 0 = default #2B2533, 4 = Gold (Gus mission M1).
        public static readonly Color[] FinColors =
        {
            Hex("#2B2533"), Hex("#F2C94C"), Hex("#3FB8C4"), Hex("#E0607E"), Hex("#E8C65A"),
            // round 3
            Hex("#EE7F2D"), Hex("#8E6CD9"), Hex("#EDE6DA"),
        };

        public const int HatCount = 20, PatternCount = 12, EyesCount = 6, BrowsCount = 6, MouthCount = 5;
        /// Round 3 body / face options (DiverLook.build, hair, beard, nose, accessory; height has 5 steps −2 … +2).
        public const int BuildCount = 4, HeightCount = 5, HairCount = 9, BeardCount = 6, NoseCount = 5, AccessoryCount = 8;
        /// The hair style index that means "bald" (the last one).
        public const int HairBald = HairCount - 1;

        public static readonly List<CosmeticDef> All = Build();

        static List<CosmeticDef> Build()
        {
            var l = new List<CosmeticDef>();
            string[] suitDe = { "Orange", "Türkis", "Koralle", "Sonnengelb", "Lila", "Seetang", "Ozeanblau", "Sand", "Rost", "Smaragd", "Bonbonrosa", "Mitternacht",
                                "Beere", "Mango", "Meerschaum", "Indigo", "Limette", "Schwarz" };
            string[] suitEn = { "Orange", "Teal", "Coral", "Sun Yellow", "Purple", "Kelp", "Ocean Blue", "Sand", "Rust", "Emerald", "Candy Pink", "Midnight",
                                "Berry", "Mango", "Seafoam", "Indigo", "Lime", "Black" };
            for (int i = 0; i < SuitColors.Length; i++) l.Add(Col(Suit, i, suitDe[i], suitEn[i], SuitColors[i]));

            l.Add(Col(Cuff, 0, "Auto", "Auto", CuffColors[0]));
            for (int i = 1; i < CuffColors.Length; i++) l.Add(Col(Cuff, i, "Farbe " + i, "Colour " + i, CuffColors[i]));

            l.Add(Opt(Pattern, 0, "Uni", "Plain"));
            l.Add(Opt(Pattern, 1, "Streifen", "Stripes"));
            l.Add(Opt(Pattern, 2, "Punkte", "Dots"));
            l.Add(Opt(Pattern, 3, "Haizähne", "Shark Teeth"));
            l.Add(Opt(Pattern, 4, "Fischgräte", "Herringbone"));
            l.Add(Opt(Pattern, 5, "Wellen", "Waves"));
            l.Add(Locked(Pattern, 6, "Gold", "Gold", "Alle 14 Fundstücke", "All 14 treasures", "treasures_all"));
            l.Add(Locked(Pattern, 7, "Leuchtturm", "Lighthouse", "Gus' Auftrag „Das Leuchtfeuer“", "Gus's job “The Beacon”", "mission:M3"));
            l.Add(Opt(Pattern, 8, "Tarnung", "Camo"));
            l.Add(Opt(Pattern, 9, "Zickzack", "Zigzag"));
            l.Add(Opt(Pattern, 10, "Sterne", "Stars"));
            l.Add(Opt(Pattern, 11, "Zweifarbig", "Two-tone"));

            l.Add(Opt(Hat, 0, "Ohne", "None"));
            l.Add(Opt(Hat, 1, "Fischerhut", "Bucket Hat"));
            l.Add(Opt(Hat, 2, "Wikingerhelm", "Viking Helmet"));
            l.Add(Opt(Hat, 3, "Haiflosse", "Shark Fin"));
            l.Add(Opt(Hat, 4, "Quietscheente", "Rubber Duck"));
            l.Add(Opt(Hat, 5, "Kapitänsmütze", "Captain's Cap"));
            l.Add(Locked(Hat, 6, "Goldener Fischerhut", "Golden Bucket Hat", "Alle 40 goldenen Muscheln", "All 40 golden shells", "shells_all"));
            l.Add(Locked(Hat, 7, "Gus' Originalmütze", "Gus's Original Cap", "Alle 24 Logbuch-Seiten", "All 24 logbook pages", "pages_all"));
            l.Add(Locked(Hat, 8, "Partyhut", "Party Hat", "100 Follower", "100 followers", "followers:100"));
            l.Add(Locked(Hat, 9, "Piraten-Dreispitz", "Pirate Tricorn", "Geheimnis in der Kapitänskajüte", "Secret in the captain's cabin", "secret:wreck_cabin"));
            l.Add(Locked(Hat, 10, "Horsts Köderhut", "Horst's Lure Hat", "Tutorial geschafft", "Finish the tutorial", "tutorial"));
            l.Add(Locked(Hat, 11, "Möwennest „Günther“", "Seagull Nest “Günther”", "Erfolg „Horst im Hafenbecken“", "Achievement “Horst Overboard”", "achievement:horst_overboard"));
            l.Add(Locked(Hat, 12, "Krone", "Crown", "1.000.000 Follower", "1,000,000 followers", "followers:1000000"));
            l.Add(Locked(Hat, 13, "Wurzels Strohhut", "Root's Straw Hat", "Wurzels Auftrag „Ziegen-Yoga“", "Root's job “Goat Yoga”", "quest:P2"));
            l.Add(Locked(Hat, 14, "Bordkarten-Krone", "Boarding Pass Crown", "Wandas Auftrag „Ein echter Hai!“", "Wanda's job “A Real Shark!”", "quest:L2"));
            l.Add(Opt(Hat, 15, "Mütze", "Beanie"));
            l.Add(Opt(Hat, 16, "Basecap", "Baseball Cap"));
            l.Add(Opt(Hat, 17, "Stirnband", "Headband"));
            l.Add(Opt(Hat, 18, "Kopftuch", "Head Scarf"));
            l.Add(Opt(Hat, 19, "Sonnenschild", "Sun Visor"));

            string[] skinDe = { "Hell", "Honig", "Karamell", "Kakao", "Porzellan", "Espresso", "Sand", "Bernstein", "Kastanie", "Rosé" };
            string[] skinEn = { "Light", "Honey", "Caramel", "Cocoa", "Porcelain", "Espresso", "Sand", "Amber", "Chestnut", "Rosy" };
            for (int i = 0; i < SkinTones.Length; i++) l.Add(Col(Skin, i, skinDe[i], skinEn[i], SkinTones[i]));

            // round 3: body, hair, beard, nose, accessories (all free)
            l.Add(Opt(BodyBuild, 0, "Normal", "Regular"));
            l.Add(Opt(BodyBuild, 1, "Schlank", "Slim"));
            l.Add(Opt(BodyBuild, 2, "Kräftig", "Sturdy"));
            l.Add(Opt(BodyBuild, 3, "Breite Schultern", "Broad"));
            l.Add(Opt(Height, 0, "Sehr klein", "Very short"));
            l.Add(Opt(Height, 1, "Klein", "Short"));
            l.Add(Opt(Height, 2, "Mittel", "Medium"));
            l.Add(Opt(Height, 3, "Groß", "Tall"));
            l.Add(Opt(Height, 4, "Sehr groß", "Very tall"));
            string[] hairDe = { "Kurz", "Stachelig", "Locken", "Zopf", "Dutt", "Lang", "Seitenscheitel", "Wuschelkopf", "Glatze" };
            string[] hairEn = { "Short", "Spiky", "Curls", "Ponytail", "Bun", "Long", "Side Part", "Shaggy", "Bald" };
            for (int i = 0; i < HairCount; i++) l.Add(Opt(Hair, i, hairDe[i], hairEn[i]));
            string[] hcDe = { "Braun", "Schwarz", "Hellbraun", "Kupfer", "Rot", "Blond", "Platin", "Grau", "Türkis", "Pink" };
            string[] hcEn = { "Brown", "Black", "Light Brown", "Copper", "Ginger", "Blond", "Platinum", "Grey", "Teal", "Pink" };
            for (int i = 0; i < HairColors.Length; i++) l.Add(Col(HairColor, i, hcDe[i], hcEn[i], HairColors[i]));
            string[] beardDe = { "Ohne", "Schnurrbart", "Ziegenbart", "Vollbart", "Kapitänsbart", "Stoppeln" };
            string[] beardEn = { "None", "Moustache", "Goatee", "Full Beard", "Captain's Beard", "Stubble" };
            for (int i = 0; i < BeardCount; i++) l.Add(Opt(Beard, i, beardDe[i], beardEn[i]));
            string[] noseDe = { "Knopf", "Knolle", "Lang", "Spitz", "Stups" };
            string[] noseEn = { "Button", "Round", "Long", "Pointy", "Snub" };
            for (int i = 0; i < NoseCount; i++) l.Add(Opt(Nose, i, noseDe[i], noseEn[i]));
            string[] accDe = { "Ohne", "Muschelkette", "Halstuch", "Ohrringe", "Blume", "Taucheruhr", "Sommersprossen", "Pflaster" };
            string[] accEn = { "None", "Shell Necklace", "Neckerchief", "Earrings", "Flower", "Dive Watch", "Freckles", "Plaster" };
            for (int i = 0; i < AccessoryCount; i++) l.Add(Opt(Accessory, i, accDe[i], accEn[i]));

            l.Add(Opt(Mouth, 0, "Lächeln", "Smile"));
            l.Add(Opt(Mouth, 1, "Offen", "Open"));
            l.Add(Opt(Mouth, 2, "Oh!", "Oh!"));
            l.Add(Opt(Mouth, 3, "Gerade", "Flat"));
            l.Add(Opt(Mouth, 4, "Grinsen", "Grin"));

            l.Add(Opt(Eyes, 0, "Glupsch", "Googly"));
            l.Add(Opt(Eyes, 1, "Müde", "Sleepy"));
            l.Add(Opt(Eyes, 2, "Schielend", "Cross-eyed"));
            l.Add(Locked(Eyes, 3, "Sternchen", "Stars", "Sealog zu 100 %", "Sealog 100 %", "sealog_full"));
            l.Add(Locked(Eyes, 4, "Herzchen", "Hearts", "Gerald an 5 verschiedenen Tagen filmen", "Film Gerald on 5 different days", "gerald_days:5"));
            l.Add(Opt(Eyes, 5, "Wimpern", "Lashes"));

            // round 3: 0 is now the natural default brow (a readable face); "none" moved to the end
            l.Add(Opt(Brows, 0, "Natürlich", "Natural"));
            l.Add(Opt(Brows, 1, "Buschig", "Bushy"));
            l.Add(Opt(Brows, 2, "Grimmig", "Grumpy"));
            l.Add(Opt(Brows, 3, "Überrascht", "Surprised"));
            l.Add(Opt(Brows, 4, "Dünn", "Thin"));
            l.Add(Opt(Brows, 5, "Keine", "None"));

            string[] maskDe = { "Klassik", "Orange", "Türkis", "Gelb", "Koralle", "Sand" };
            string[] maskEn = { "Classic", "Orange", "Teal", "Yellow", "Coral", "Sand" };
            for (int i = 0; i < 6; i++) l.Add(Col(Mask, i, maskDe[i], maskEn[i], MaskColors[i]));
            var goldMask = Locked(Mask, 6, "Gold", "Gold", "Alle 13 Geheimnisse", "All 13 secrets", "secrets_all");
            goldMask.color = MaskColors[6];
            l.Add(goldMask);
            l.Add(Col(Mask, 7, "Lila", "Purple", MaskColors[7]));
            l.Add(Col(Mask, 8, "Seetang", "Kelp", MaskColors[8]));
            l.Add(Col(Mask, 9, "Rot", "Red", MaskColors[9]));

            l.Add(Col(Fins, 0, "Klassik", "Classic", FinColors[0]));
            l.Add(Col(Fins, 1, "Quietschgelb", "Squeaky Yellow", FinColors[1]));
            l.Add(Col(Fins, 2, "Türkis", "Teal", FinColors[2]));
            l.Add(Col(Fins, 3, "Koralle", "Coral", FinColors[3]));
            var goldFins = Locked(Fins, 4, "Gold", "Gold", "Gus' Auftrag „Die Glücksflosse“", "Gus's job “The Lucky Fin”", "mission:M1");
            goldFins.color = FinColors[4];
            l.Add(goldFins);
            l.Add(Col(Fins, 5, "Orange", "Orange", FinColors[5]));
            l.Add(Col(Fins, 6, "Lila", "Purple", FinColors[6]));
            l.Add(Col(Fins, 7, "Weiß", "White", FinColors[7]));

            l.Add(Opt(BoatFlag, 0, "Keine", "None"));
            l.Add(Locked(BoatFlag, 1, "Wellen", "Waves", "Horsts Auftrag „Gus' altes Paddel“", "Horst's job “Gus's Old Paddle”", "quest:M2"));
            l.Add(Locked(BoatFlag, 2, "Kanu-Wimpel", "Canoe Pennant", "Harpos Auftrag „Das Kanu-Rennen“", "Harpo's job “The Canoe Race”", "quest:M3"));

            l.Add(Opt(HandProp, 0, "Keins", "None"));
            l.Add(Locked(HandProp, 1, "Goldener Selfie-Stick", "Golden Selfie Stick", "Behalte ihn (Chad & Brad)", "Keep it (Chad & Brad)", "quest:B1"));
            return l;
        }

        static CosmeticDef Opt(string kind, int i, string de, string en) =>
            new CosmeticDef { kind = kind, index = i, id = Id(kind, i), nameDe = de, nameEn = en, free = true };

        static CosmeticDef Col(string kind, int i, string de, string en, Color c)
        {
            var d = Opt(kind, i, de, en);
            d.color = c;
            return d;
        }

        static CosmeticDef Locked(string kind, int i, string de, string en, string hintDe, string hintEn, string rule) =>
            new CosmeticDef { kind = kind, index = i, id = Id(kind, i), nameDe = de, nameEn = en, free = false,
                unlockHintDe = hintDe, unlockHintEn = hintEn, rule = rule };

        public static string Id(string kind, int index) => kind.ToLowerInvariant() + ":" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        static Dictionary<string, CosmeticDef> byId;

        public static CosmeticDef Get(string id)
        {
            if (id == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, CosmeticDef>();
                foreach (var c in All) byId[c.id] = c;
            }
            return byId.TryGetValue(id, out var d) ? d : null;
        }

        public static CosmeticDef Get(string kind, int index) => Get(Id(kind, index));

        /// Options of one kind in index order (character screen tab). Fills `into`.
        public static void OfKind(string kind, List<CosmeticDef> into)
        {
            into.Clear();
            for (int i = 0; i < All.Count; i++) if (All[i].kind == kind) into.Add(All[i]);
        }

        /// "#RRGGBB" → Color (pure C#, safe inside static initializers).
        static Color Hex(string h)
        {
            int v = int.Parse(h.Substring(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
        }
    }
}
