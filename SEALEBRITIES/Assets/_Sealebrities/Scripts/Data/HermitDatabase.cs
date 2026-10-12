using UnityEngine;

namespace Sealebrities
{
    /// The signs of the gesture wheel (GDD §20 Q5: hold the key, pick a symbol – the hermits do not speak your
    /// language; the wrong sign annoys them). The same wheel offers these as actions at every other NPC.
    /// Replicated as a byte – APPEND only.
    public enum HermitSign : byte { Hello, Sea, Peace, Food, Trade, Learn, Hint, Gift, Thanks, Film }

    /// One barter offer of a hermit (Q47): give → get (+ get2). "*valuable" = any treasure from the pouch (the
    /// cheapest one is taken). special: "hint" (a clue in signs), "respect" (the whole camp warms up).
    public sealed class HermitTrade
    {
        public string give;
        public int giveN = 1;
        public string get, get2;
        public int getN = 1, get2N;
        public string special;
    }

    /// One hermit: role, start mood (they distrust strangers), work place + motion, likes / dislikes for gifts,
    /// barter offers and the craft he teaches (Q8).
    public sealed class HermitDef
    {
        public string id;
        public int startMood;
        /// QuestSpots id of the work place and the motion there.
        public string work;
        public NpcMotion workMotion;
        /// Night: a camp hammock (PropBed id) or null (sleeps in a tent – hidden).
        public string hammock;
        public string[] likes, dislikes;
        public HermitTrade[] trades;
        /// Story flag set when he taught his craft, its name and where he shows it (QuestSpots id).
        public string craftFlag, craftDe, craftEn, craftSpot;
        /// What he likes in one short line (Tasks app card).
        public string likesDe, likesEn;
        public string Craft => craftDe != null ? Loc.Tr(craftDe, craftEn) : null;
        public string Likes => Loc.Tr(likesDe, likesEn);
    }

    /// The six hermits (GDD §20 Q2/Q3: eccentric dropouts who revere the sea and distrust strangers – leader, smith,
    /// hunter, cook, lookout, gardener). No real people, no stereotypes: old sailcloth, nets, a colander for a hat.
    public static class HermitDatabase
    {
        public const string AnyValuable = "*valuable";

        static HermitTrade T(string give, int giveN, string get, int getN, string get2 = null, int get2N = 0, string special = null) =>
            new HermitTrade { give = give, giveN = giveN, get = get, getN = getN, get2 = get2, get2N = get2N, special = special };

        public static readonly HermitDef[] All =
        {
            new HermitDef { id = NpcDatabase.HLeader, startMood = -30, work = "hermit_role_leader", workMotion = NpcMotion.Sit, hammock = "camp_hammock",
                likes = new[] { ItemIds.Pearl, ItemIds.Figurine, ItemIds.Goblet, ItemIds.BottleFresh, "mango" },
                dislikes = new[] { "shark_gummies", "krill_bar" },
                likesDe = "Perlen, seltsame Figuren, Trinkwasser", likesEn = "Pearls, strange figurines, fresh water",
                trades = new[] { T(AnyValuable, 1, ItemIds.BottleFresh, 2), T(ItemIds.Pearl, 1, null, 0, null, 0, "respect") } },
            new HermitDef { id = NpcDatabase.HSmith, startMood = -40, work = "hermit_role_smith", workMotion = NpcMotion.Carve,
                likes = new[] { ItemIds.Stone, ItemIds.Wood, ItemIds.Coins, "galley_coffee" },
                dislikes = new[] { "krill_bar", "glow_soda" },
                likesDe = "Schleifsteine, Treibholz, Kaffee", likesEn = "Whetstones, driftwood, coffee",
                trades = new[] { T(AnyValuable, 1, ItemIds.Stone, 3), T(ItemIds.Wood, 3, ItemIds.SpearT1, 1) },
                craftFlag = StoryKeys.GrindLearned, craftDe = "Speere schärfen", craftEn = "Sharpening spears", craftSpot = "hermit_grind_lesson" },
            new HermitDef { id = NpcDatabase.HHunter, startMood = -55, work = "hermit_role_hunter", workMotion = NpcMotion.Mend,
                likes = new[] { ItemIds.RawMeat, ItemIds.RawFish, FoodDatabase.GrilledFish, "fish_sandwich" },
                dislikes = new[] { "seaweed_chips", "krill_bar" },
                likesDe = "Rohes Fleisch, Fisch, Fischbrötchen", likesEn = "Raw meat, fish, fish sandwiches",
                trades = new[] { T(AnyValuable, 1, ItemIds.RawMeat, 2), T(ItemIds.Wood, 2, ItemIds.RawMeat, 1) },
                craftFlag = NpcKeys.TrapLearned, craftDe = "Fischfallen bauen", craftEn = "Building fish traps", craftSpot = "hermit_drying" },
            new HermitDef { id = NpcDatabase.HCook, startMood = -15, work = "hermit_role_cook", workMotion = NpcMotion.Cook,
                likes = new[] { ItemIds.RawFish, FoodDatabase.Coconut, "mango", "banana", "seaweed_chips" },
                dislikes = new[] { "krill_bar", "water_bottle" },
                likesDe = "Roher Fisch, Kokosnüsse, Obst", likesEn = "Raw fish, coconuts, fruit",
                trades = new[] { T(ItemIds.RawFish, 2, "coconut_curry", 1), T(FoodDatabase.Coconut, 1, "ginger_fizz", 1) },
                craftFlag = NpcKeys.RecipeCurry, craftDe = "Kokos-Fischcurry kochen", craftEn = "Cooking coconut fish curry", craftSpot = "hermit_role_cook" },
            new HermitDef { id = NpcDatabase.HLookout, startMood = -25, work = "hermit_role_lookout", workMotion = NpcMotion.Idle, hammock = "camp_hammock_2",
                likes = new[] { ItemIds.Spyglass, ItemIds.Compass, "galley_coffee", "glow_soda" },
                dislikes = new[] { "shark_gummies" },
                likesDe = "Fernrohre, Kompasse, Kaffee", likesEn = "Spyglasses, compasses, coffee",
                trades = new[] { T(AnyValuable, 1, null, 0, null, 0, "hint"), T(FoodDatabase.Coconut, 1, ItemIds.BottleFresh, 1) } },
            new HermitDef { id = NpcDatabase.HGardener, startMood = -20, work = "hermit_role_gardener", workMotion = NpcMotion.Mend,
                likes = new[] { "mango", "banana", FoodDatabase.Coconut, ItemIds.BottleFresh, "sea_grape_salad" },
                dislikes = new[] { "shark_gummies", "krill_bar" },
                likesDe = "Obst, Kokosnüsse, Trinkwasser", likesEn = "Fruit, coconuts, fresh water",
                trades = new[] { T(AnyValuable, 1, "mango", 2, "banana", 2), T(ItemIds.RawFish, 1, "sea_grape_salad", 1) },
                craftFlag = NpcKeys.HarvestLearned, craftDe = "Früchte ernten", craftEn = "Harvesting fruit", craftSpot = "hermit_garden" },
        };

        public static HermitDef Get(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        public static bool Likes(HermitDef d, string item) => d != null && Contains(d.likes, item);
        public static bool Dislikes(HermitDef d, string item) => d != null && Contains(d.dislikes, item);

        static bool Contains(string[] a, string v)
        {
            if (a == null || v == null) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] == v) return true;
            return false;
        }

        // ---------------------------------------------------------------- the wheel's symbols
        public const int SignCount = 10;

        public static string SignName(HermitSign s)
        {
            switch (s)
            {
                case HermitSign.Hello: return Loc.Tr("Hallo", "Hello");
                case HermitSign.Sea: return Loc.Tr("Das Meer ehren", "Honour the sea");
                case HermitSign.Peace: return Loc.Tr("Frieden", "Peace");
                case HermitSign.Food: return Loc.Tr("Essen", "Food");
                case HermitSign.Trade: return Loc.Tr("Tausch", "Trade");
                case HermitSign.Learn: return Loc.Tr("Zeig's mir", "Teach me");
                case HermitSign.Hint: return Loc.Tr("Hinweis", "A hint?");
                case HermitSign.Gift: return Loc.Tr("Geschenk", "Gift");
                case HermitSign.Thanks: return Loc.Tr("Danke", "Thanks");
                default: return Loc.Tr("Filmen", "Film");
            }
        }

        /// The action a sign means at an NPC who speaks (Horst, Wanda, Dr. Fin, Toni, the brothers).
        public static string ActionName(HermitSign s)
        {
            switch (s)
            {
                case HermitSign.Hello: return Loc.Tr("Reden", "Talk");
                case HermitSign.Sea: return Loc.Tr("Übers Meer", "About the sea");
                case HermitSign.Peace: return Loc.Tr("Entschuldigen", "Apologise");
                case HermitSign.Food: return Loc.Tr("Essen?", "Food?");
                case HermitSign.Trade: return Loc.Tr("Tausch", "Trade");
                case HermitSign.Learn: return Loc.Tr("Was tun?", "What now?");
                case HermitSign.Hint: return Loc.Tr("Tipp", "Tip");
                case HermitSign.Gift: return Loc.Tr("Geschenk", "Gift");
                case HermitSign.Thanks: return Loc.Tr("Danke", "Thanks");
                default: return Loc.Tr("Interview", "Interview");
            }
        }
    }
}
