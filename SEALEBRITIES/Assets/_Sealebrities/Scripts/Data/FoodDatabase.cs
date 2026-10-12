using System.Collections.Generic;

namespace Sealebrities
{
    /// One food or drink (E.2, L.8). Food gives a gentle buff. Round 3 (GDD §19 „Überleben“): every food also fills the
    /// hunger / thirst meters (Survival) – hunger / thirst are points 0..100 restored when it is eaten.
    /// A food with value 0 gives no buff (plain water, fruit snacks).
    public class FoodDef
    {
        public string id, nameDe, nameEn;
        public FoodKind kind;
        /// Round 3: hunger / thirst points (0..100) restored when eaten / drunk.
        public float hunger, thirst;
        /// Price of one pack in the Chopper Shop (0 = not sold by chopper: grill, palm, village kiosk).
        public long price;
        public long followersNeeded;
        public BuffType buff;
        /// Buff strength: AirCapacity/SwimSpeed = +fraction (0.15 = +15 %); SteadyHands = camera-shake reduction 0..1;
        /// BumpResist = predator-bump air-loss reduction 0..1; NightEyes = +fraction deep residual light.
        public float value;
        /// Duration in REAL minutes.
        public float minutes;
        /// Items per Chopper Shop pack (snacks come in packs of 3).
        public int packSize = 3;
        /// Where it comes from: "grill", "palm", "chopper", "kiosk".
        public string source = "chopper";
        /// Kiosk items: side quest that unlocks it (A1/A2), null = from the start.
        public string unlockQuest;
        public string descDe, descEn;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);
        public bool SoldByChopper => source == "chopper" && price > 0;
    }

    /// Food table (E.2) + village kiosk food (L.8). Balance constants live here (J.5).
    public static class FoodDatabase
    {
        public const string GrilledFish = "grilled_fish", Coconut = "coconut";
        /// E.2: grilled fish of at least this weight gives +20 % instead of +15 %.
        public const float BigFishKg = 3f;
        public const float BigFishBonus = 0.05f;
        /// "Gemütlich" bonus: grilled a fish today + slept in the bed → next upload +5 % views.
        public const float CozyViewsBonus = 0.05f;

        public static readonly List<FoodDef> All = new List<FoodDef>
        {
            new FoodDef { id = GrilledFish, nameDe = "Gegrillter Fisch", nameEn = "Grilled Fish", kind = FoodKind.Food, source = "grill", packSize = 1,
                buff = BuffType.AirCapacity, value = 0.15f, minutes = 10f, hunger = 45f,
                descDe = "Satt und mehr Luft. Riecht nach Urlaub.", descEn = "Filling, more air. Smells like holiday." },
            new FoodDef { id = Coconut, nameDe = "Kokosnuss", nameEn = "Coconut", kind = FoodKind.Drink, source = "palm", packSize = 1,
                buff = BuffType.SwimSpeed, value = 0.10f, minutes = 6f, hunger = 10f, thirst = 35f,
                descDe = "Löscht den Durst. Schneller schwimmen.", descEn = "Quenches thirst. Swim faster." },
            new FoodDef { id = "seaweed_chips", nameDe = "Algen-Chips", nameEn = "Seaweed Chips", kind = FoodKind.Food, price = 20,
                buff = BuffType.AirCapacity, value = 0.10f, minutes = 8f, hunger = 20f,
                descDe = "Knusprig. Grün. Mehr Luft.", descEn = "Crunchy. Green. More air." },
            new FoodDef { id = "krill_bar", nameDe = "Krill-Riegel", nameEn = "Krill Bar", kind = FoodKind.Food, price = 35,
                buff = BuffType.SwimSpeed, value = 0.20f, minutes = 6f, hunger = 30f,
                descDe = "Proteinbombe. Schmeckt wie es heißt.", descEn = "Protein bomb. Tastes like it sounds." },
            new FoodDef { id = "galley_coffee", nameDe = "Kombüsen-Kaffee", nameEn = "Galley Coffee", kind = FoodKind.Drink, price = 30,
                buff = BuffType.SteadyHands, value = 0.40f, minutes = 8f, thirst = 20f,
                descDe = "Ruhige Hände: weniger Wackeln beim Filmen.", descEn = "Steady hands: less shake while filming." },
            new FoodDef { id = "fish_sandwich", nameDe = "Fischbrötchen", nameEn = "Fish Sandwich", kind = FoodKind.Food, price = 60, followersNeeded = 2500,
                buff = BuffType.AirCapacity, value = 0.25f, minutes = 10f, hunger = 50f,
                descDe = "Horsts Lieblingsessen. Viel mehr Luft.", descEn = "Horst's favourite. Much more air." },
            new FoodDef { id = "shark_gummies", nameDe = "Hai-Gummis", nameEn = "Shark Gummies", kind = FoodKind.Food, price = 50, followersNeeded = 5000,
                buff = BuffType.BumpResist, value = 0.50f, minutes = 8f, hunger = 15f,
                descDe = "Raubtier-Rempler kosten nur halb so viel Luft.", descEn = "Predator bumps cost only half the air." },
            new FoodDef { id = "glow_soda", nameDe = "Leucht-Limo", nameEn = "Glow Soda", kind = FoodKind.Drink, price = 80, followersNeeded = 25000,
                buff = BuffType.NightEyes, value = 0.50f, minutes = 6f, thirst = 35f,
                descDe = "In der Tiefe siehst du mehr. Leuchtet im Dunkeln.", descEn = "See more in the deep. Glows in the dark." },
            // ---- L.8 Ada's kiosk in Altriff (bought food goes to the pantry)
            new FoodDef { id = "coconut_curry", nameDe = "Kokos-Fischcurry", nameEn = "Coconut Fish Curry", kind = FoodKind.Food, price = 45, source = "kiosk", packSize = 1, unlockQuest = "A1",
                buff = BuffType.AirCapacity, value = 0.20f, minutes = 10f, hunger = 55f,
                descDe = "Tante Adas Rezept. „Nur ein bisschen scharf.“", descEn = "Auntie Ada's recipe. “Only a little spicy.”" },
            new FoodDef { id = "sea_grape_salad", nameDe = "Seetrauben-Salat", nameEn = "Sea Grape Salad", kind = FoodKind.Food, price = 25, source = "kiosk", packSize = 1, unlockQuest = "A2",
                buff = BuffType.SwimSpeed, value = 0.15f, minutes = 8f, hunger = 25f, thirst = 5f,
                descDe = "Platzt im Mund. Schneller schwimmen.", descEn = "Pops in your mouth. Swim faster." },
            new FoodDef { id = "ginger_fizz", nameDe = "Ingwer-Limo", nameEn = "Ginger Fizz", kind = FoodKind.Drink, price = 30, source = "kiosk", packSize = 1,
                buff = BuffType.SteadyHands, value = 0.40f, minutes = 8f, thirst = 35f,
                descDe = "Prickelt. Ruhige Hände.", descEn = "Fizzy. Steady hands." },
            // ---- round 3 (GDD §19 Überleben): cooked meat, fruit from the island, water from the chopper
            new FoodDef { id = GrilledMeat, nameDe = "Gebratenes Fleisch", nameEn = "Roast Meat", kind = FoodKind.Food, source = "grill", packSize = 1,
                buff = BuffType.AirCapacity, value = 0.15f, minutes = 10f, hunger = 50f,
                descDe = "Vom Lagerfeuer. Macht richtig satt.", descEn = "From the campfire. Really filling." },
            new FoodDef { id = "mango", nameDe = "Mango", nameEn = "Mango", kind = FoodKind.Food, source = "tree", packSize = 1,
                buff = BuffType.SwimSpeed, value = 0.05f, minutes = 4f, hunger = 18f, thirst = 12f,
                descDe = "Saftig. Klebt an den Fingern.", descEn = "Juicy. Sticks to your fingers." },
            new FoodDef { id = "banana", nameDe = "Banane", nameEn = "Banana", kind = FoodKind.Food, source = "tree", packSize = 1,
                buff = BuffType.AirCapacity, value = 0.05f, minutes = 4f, hunger = 22f,
                descDe = "Die Schale nicht auf den Steg werfen.", descEn = "Don't drop the peel on the dock." },
            new FoodDef { id = "water_bottle", nameDe = "Wasser vom Heli", nameEn = "Chopper Water", kind = FoodKind.Drink, price = 25, packSize = 3,
                buff = BuffType.SwimSpeed, value = 0f, minutes = 1f, thirst = 40f,
                descDe = "Teuer, aber nass. Billiger: Meerwasser filtern.", descEn = "Pricey but wet. Cheaper: filter seawater." },
            // the filtered bottle (ItemIds.BottleFresh links here for the drink animation; no own backpack item)
            new FoodDef { id = FreshWater, nameDe = "Trinkwasser", nameEn = "Fresh Water", kind = FoodKind.Drink, source = "filter", packSize = 1,
                buff = BuffType.SwimSpeed, value = 0f, minutes = 1f, thirst = 45f,
                descDe = "Gefiltert. Löscht den Durst.", descEn = "Filtered. Quenches thirst." },
        };

        /// Round 3: water from the hut's filter (the bottle item "bottle_fresh").
        public const string FreshWater = "fresh_water";

        /// Round 3: cooked meat (raw meat on the campfire / grill).
        public const string GrilledMeat = "grilled_meat";

        static Dictionary<string, FoodDef> byId;

        public static FoodDef Get(string id)
        {
            if (id == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, FoodDef>();
                foreach (var f in All) byId[f.id] = f;
            }
            return byId.TryGetValue(id, out var d) ? d : null;
        }

        /// Delivery item id of a chopper snack pack (Delivery.itemId) – "food:krill_bar".
        public const string DeliveryPrefix = "food:";
        public static string DeliveryId(FoodDef f) => DeliveryPrefix + f.id;

        /// Food of a delivery id ("food:krill_bar") or null when the delivery is gear.
        public static FoodDef FromDeliveryId(string itemId)
        {
            if (itemId == null || !itemId.StartsWith(DeliveryPrefix, System.StringComparison.Ordinal)) return null;
            return Get(itemId.Substring(DeliveryPrefix.Length));
        }
    }
}
