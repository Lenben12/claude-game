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
            Eyes = "Eyes", Brows = "Brows", Mask = "Mask", Fins = "Fins", BoatFlag = "BoatFlag", HandProp = "HandProp";

        public static readonly Color[] SuitColors =
        {
            Hex("#EE7F2D"), Hex("#3FB8C4"), Hex("#E0607E"), Hex("#F2C94C"), Hex("#8E6CD9"), Hex("#6E9445"),
            Hex("#4F7FAE"), Hex("#EDE6DA"), Hex("#C94F3D"), Hex("#2F8F6E"), Hex("#F29BBE"), Hex("#3A3F5C"),
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
        };

        /// Mask frames – index 0 = default #1E1B2B, 6 = Gold (all 13 secrets).
        public static readonly Color[] MaskColors =
        {
            Hex("#1E1B2B"), Hex("#EE7F2D"), Hex("#3FB8C4"), Hex("#F2C94C"), Hex("#E0607E"), Hex("#EDE6DA"), Hex("#E8C65A"),
        };

        /// Fin colours – index 0 = default #2B2533, 4 = Gold (Gus mission M1).
        public static readonly Color[] FinColors =
        {
            Hex("#2B2533"), Hex("#F2C94C"), Hex("#3FB8C4"), Hex("#E0607E"), Hex("#E8C65A"),
        };

        public const int HatCount = 15, PatternCount = 8, EyesCount = 5, BrowsCount = 4, MouthCount = 5;

        public static readonly List<CosmeticDef> All = Build();

        static List<CosmeticDef> Build()
        {
            var l = new List<CosmeticDef>();
            string[] suitDe = { "Orange", "Türkis", "Koralle", "Sonnengelb", "Lila", "Seetang", "Ozeanblau", "Sand", "Rost", "Smaragd", "Bonbonrosa", "Mitternacht" };
            string[] suitEn = { "Orange", "Teal", "Coral", "Sun Yellow", "Purple", "Kelp", "Ocean Blue", "Sand", "Rust", "Emerald", "Candy Pink", "Midnight" };
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
            l.Add(Locked(Hat, 13, "Perles Strohhut", "Pearl's Straw Hat", "Perles Auftrag „Kartenabend“", "Pearl's quest “Card Night”", "quest:P2"));
            l.Add(Locked(Hat, 14, "Papierkrone", "Paper Crown", "Lios Auftrag „Ein echter Hai!“", "Lio's quest “A Real Shark!”", "quest:L2"));

            string[] skinDe = { "Hell", "Honig", "Karamell", "Kakao", "Porzellan", "Espresso" };
            string[] skinEn = { "Light", "Honey", "Caramel", "Cocoa", "Porcelain", "Espresso" };
            for (int i = 0; i < SkinTones.Length; i++) l.Add(Col(Skin, i, skinDe[i], skinEn[i], SkinTones[i]));

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

            l.Add(Opt(Brows, 0, "Keine", "None"));
            l.Add(Opt(Brows, 1, "Buschig", "Bushy"));
            l.Add(Opt(Brows, 2, "Grimmig", "Grumpy"));
            l.Add(Opt(Brows, 3, "Überrascht", "Surprised"));

            string[] maskDe = { "Klassik", "Orange", "Türkis", "Gelb", "Koralle", "Sand" };
            string[] maskEn = { "Classic", "Orange", "Teal", "Yellow", "Coral", "Sand" };
            for (int i = 0; i < 6; i++) l.Add(Col(Mask, i, maskDe[i], maskEn[i], MaskColors[i]));
            var goldMask = Locked(Mask, 6, "Gold", "Gold", "Alle 13 Geheimnisse", "All 13 secrets", "secrets_all");
            goldMask.color = MaskColors[6];
            l.Add(goldMask);

            l.Add(Col(Fins, 0, "Klassik", "Classic", FinColors[0]));
            l.Add(Col(Fins, 1, "Quietschgelb", "Squeaky Yellow", FinColors[1]));
            l.Add(Col(Fins, 2, "Türkis", "Teal", FinColors[2]));
            l.Add(Col(Fins, 3, "Koralle", "Coral", FinColors[3]));
            var goldFins = Locked(Fins, 4, "Gold", "Gold", "Gus' Auftrag „Die Glücksflosse“", "Gus's job “The Lucky Fin”", "mission:M1");
            goldFins.color = FinColors[4];
            l.Add(goldFins);

            l.Add(Opt(BoatFlag, 0, "Keine", "None"));
            l.Add(Locked(BoatFlag, 1, "Wellen", "Waves", "Miras Auftrag „Gus' altes Paddel“", "Mira's quest “Gus's Old Paddle”", "quest:M2"));
            l.Add(Locked(BoatFlag, 2, "Altriff", "Oldreef", "Miras Auftrag „Das erste Paddeln“", "Mira's quest “First Paddle”", "quest:M3"));

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
