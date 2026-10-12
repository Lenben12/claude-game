using System.Collections.Generic;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „Inventar als Rucksack“): what kind of thing a backpack item is.
    /// APPEND new values at the end (saved as ints nowhere, but the UI sorts by it).
    public enum ItemCategory { Gear, Tool, Weapon, Food, Drink, Raw, Material, Collectible, MapPiece, Valuable }

    /// One backpack item (stackable thing or a weapon instance). Gear the crew owns (tanks, fins, cameras, lamps, rods)
    /// stays in GameState.ownedItems – the three holdable gear items appear here as the pseudo items "Camera",
    /// "Flashlight" and "FishingRod" so the hotbar can reference everything by one id.
    public class ItemDef
    {
        public string id, nameDe, nameEn, descDe, descEn;
        public ItemCategory category;
        /// What the hands show while it is held (Tool = a generic small item, Food = the food model).
        public HoldItem hold = HoldItem.Tool;
        /// Uses both hands (fishing rod, speargun).
        public bool twoHanded;
        /// Max items per backpack slot (weapons / tools with durability: 1).
        public int maxStack = 10;
        /// FoodDatabase link: buff + hunger / thirst when eaten (null = not edible through the food table).
        public string foodId;
        /// Extra hunger / thirst points for items without a FoodDef (fresh water bottle).
        public float hunger, thirst;
        /// Raw food: item id after cooking at a campfire / the grill.
        public string cookTo;
        /// Item left in the backpack after using one (fresh water bottle → empty bottle).
        public string usedTo;
        /// Gear pseudo item: the gear slot that must be owned (camera / lamp / rod).
        public GearSlot gearSlot;
        public bool isGear;

        // ---- weapons (stick, spears, speargun)
        /// Base hit damage (0..100 "scare" points of the target).
        public float damage;
        /// Knockback impulse in m/s and stun seconds (slapstick, never lethal).
        public float knockback, stun;
        /// Hit reach in metres from the eye.
        public float reach;
        /// Damage reduction while blocking with it (0..1).
        public float defense;
        /// Uses until it breaks (hits; 0 = unbreakable).
        public int durability;
        /// Starting sharpness 0..1 (−1 = cannot be sharpened). Damage × (0.6 + 0.8 × sharpness).
        public float sharpness = -1f;
        /// Seconds between two swings / thrusts / shots.
        public float cooldown = 0.5f;
        /// Works under water / on land (the stick is weak under water, spears are made for it).
        public bool underwater, onLand;
        /// Effect factor in the "other" medium (stick under water 0.5, spear on land 0.6).
        public float offMediumFactor = 0.5f;

        // ---- round 3 (Q20): treasure from chests / wrecks – sold in the fish market app („Funde“ tab)
        /// Sale value in $ (0 = cannot be sold).
        public long value;

        // ---- chopper shop
        public long price, followersNeeded;
        public int packSize = 1;
        /// Sold in the Chopper Shop (ShopCategory.Weapons).
        public bool sold;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);
        public bool IsWeapon => category == ItemCategory.Weapon;
        public bool Edible => category == ItemCategory.Food || category == ItemCategory.Drink;
        public bool HasDurability => durability > 0;
        public bool Sharpenable => sharpness >= 0f;
        public bool Stackable => maxStack > 1;
        public FoodDef Food => FoodDatabase.Get(foodId);
    }

    /// Item id constants (never spell them inline).
    public static class ItemIds
    {
        // gear pseudo items (hotbar names since round 1)
        public const string Camera = "Camera", Flashlight = "Flashlight", FishingRod = "FishingRod", None = "None";
        // weapons
        public const string Stick = "stick", SpearT1 = "spear_t1", SpearT2 = "spear_t2", Speargun = "spear_t3";
        // materials
        public const string Stone = "stone", Wood = "wood";
        // water
        public const string BottleEmpty = "bottle_empty", BottleSea = "bottle_sea", BottleFresh = "bottle_fresh";
        // raw food
        public const string RawFish = "raw_fish", RawMeat = "raw_meat";
        // collectibles (counted in the shared Collectibles list; the backpack keeps the finder's own stack)
        public const string Shell = "col_shell", Page = "col_page", Message = "col_message", Treasure = "col_treasure";
        public const string MapPiece = "map_piece", TreasureMap = "treasure_map";
        // round 3 (Q20): treasure loot (valuables – no slot limit, sold in the fish market app)
        public const string Coins = "val_coins", Pearl = "val_pearl", Gem = "val_gem", Compass = "val_compass",
            Goblet = "val_goblet", FossilTooth = "val_fossil_tooth", Figurine = "val_figurine", Spyglass = "val_spyglass";
    }

    /// Round 3 backpack catalogue. Food items are generated from FoodDatabase (same ids). Balance constants live here.
    public static class ItemDatabase
    {
        public static readonly List<ItemDef> All = Build();

        static List<ItemDef> Build()
        {
            var l = new List<ItemDef>
            {
                // ---- holdable gear (owned by the crew, GameState.ownedItems)
                new ItemDef { id = ItemIds.Camera, nameDe = "Kamera", nameEn = "Camera", category = ItemCategory.Gear, hold = HoldItem.Camera, isGear = true, gearSlot = GearSlot.Camera, maxStack = 1,
                    descDe = "Linksklick filmt, Rechtsklick zielt fürs Sealog-Foto.", descEn = "Left click records, right click aims for the Sealog photo." },
                new ItemDef { id = ItemIds.Flashlight, nameDe = "Lampe", nameEn = "Lamp", category = ItemCategory.Gear, hold = HoldItem.Flashlight, isGear = true, gearSlot = GearSlot.Lamp, maxStack = 1,
                    descDe = "Licht in der Tiefe. Gut in der Zweithand.", descEn = "Light in the deep. Good in the offhand." },
                new ItemDef { id = ItemIds.FishingRod, nameDe = "Angel", nameEn = "Fishing rod", category = ItemCategory.Gear, hold = HoldItem.FishingRod, isGear = true, gearSlot = GearSlot.Rod, twoHanded = true, maxStack = 1,
                    descDe = "Beidhändig. Fische landen in der Kühlbox.", descEn = "Two-handed. Fish go into the cooler." },

                // ---- weapons (slapstick, never lethal)
                new ItemDef { id = ItemIds.Stick, nameDe = "Stock", nameEn = "Stick", category = ItemCategory.Weapon, hold = HoldItem.Stick, maxStack = 1,
                    damage = 10f, knockback = 7f, stun = 1.5f, reach = 2.3f, defense = 0.3f, durability = 0, sharpness = -1f, cooldown = 0.55f,
                    onLand = true, underwater = true, offMediumFactor = 0.5f,
                    descDe = "Zum Wegschubsen und Graben. Bonk!", descEn = "For shoving and digging. Bonk!" },
                new ItemDef { id = ItemIds.SpearT1, nameDe = "Holzspeer", nameEn = "Wooden Spear", category = ItemCategory.Weapon, hold = HoldItem.Spear, maxStack = 1,
                    damage = 16f, knockback = 3.5f, stun = 0.7f, reach = 2.5f, defense = 0.35f, durability = 25, sharpness = 0.3f, cooldown = 0.7f,
                    underwater = true, onLand = true, offMediumFactor = 0.6f,
                    sold = true, price = 150, followersNeeded = 100,
                    descDe = "Hält Haie auf Abstand. Schleifbar.", descEn = "Keeps sharks at a distance. Can be sharpened." },
                new ItemDef { id = ItemIds.SpearT2, nameDe = "Stahlspeer", nameEn = "Steel Spear", category = ItemCategory.Weapon, hold = HoldItem.Spear, maxStack = 1,
                    damage = 26f, knockback = 4.5f, stun = 0.9f, reach = 2.7f, defense = 0.45f, durability = 50, sharpness = 0.5f, cooldown = 0.65f,
                    underwater = true, onLand = true, offMediumFactor = 0.6f,
                    sold = true, price = 900, followersNeeded = 1000,
                    descDe = "Schwerer, länger, hält mehr aus.", descEn = "Heavier, longer, lasts longer." },
                new ItemDef { id = ItemIds.Speargun, nameDe = "Harpune", nameEn = "Speargun", category = ItemCategory.Weapon, hold = HoldItem.SpearGun, twoHanded = true, maxStack = 1,
                    damage = 36f, knockback = 2.5f, stun = 1.1f, reach = 7f, defense = 0.3f, durability = 80, sharpness = 0.6f, cooldown = 1.4f,
                    underwater = true, onLand = false, offMediumFactor = 0.3f,
                    sold = true, price = 4000, followersNeeded = 5000,
                    descDe = "Beidhändig. Schießt 7 m weit – nur unter Wasser.", descEn = "Two-handed. Shoots 7 m – under water only." },

                // ---- materials
                new ItemDef { id = ItemIds.Stone, nameDe = "Schleifstein", nameEn = "Whetstone", category = ItemCategory.Material, maxStack = 20,
                    descDe = "Am Schleifgerät: Speere schärfen.", descEn = "At the grinder: sharpen spears." },
                new ItemDef { id = ItemIds.Wood, nameDe = "Treibholz", nameEn = "Driftwood", category = ItemCategory.Material, maxStack = 20,
                    descDe = "2 Stück = ein Lagerfeuer.", descEn = "2 pieces = one campfire." },

                // ---- water
                new ItemDef { id = ItemIds.BottleEmpty, nameDe = "Leere Flasche", nameEn = "Empty Bottle", category = ItemCategory.Tool, maxStack = 3,
                    descDe = "Im Wasser benutzen: Meerwasser einfüllen.", descEn = "Use in the water: fill with seawater." },
                new ItemDef { id = ItemIds.BottleSea, nameDe = "Meerwasser", nameEn = "Seawater", category = ItemCategory.Tool, maxStack = 3,
                    descDe = "Salzig! Erst im Wasserfilter der Hütte filtern.", descEn = "Salty! Filter it at the hut's water filter first." },
                new ItemDef { id = ItemIds.BottleFresh, nameDe = "Trinkwasser", nameEn = "Fresh Water", category = ItemCategory.Drink, hold = HoldItem.Food, maxStack = 3,
                    foodId = FoodDatabase.FreshWater, usedTo = ItemIds.BottleEmpty,
                    descDe = "Gefiltert. Löscht den Durst.", descEn = "Filtered. Quenches thirst." },

                // ---- raw food (cook it at a campfire or the grill)
                new ItemDef { id = ItemIds.RawFish, nameDe = "Roher Fisch", nameEn = "Raw Fish", category = ItemCategory.Raw, hold = HoldItem.Food, maxStack = 5,
                    cookTo = FoodDatabase.GrilledFish,
                    descDe = "Erst braten: Lagerfeuer oder Grill.", descEn = "Cook it first: campfire or grill." },
                new ItemDef { id = ItemIds.RawMeat, nameDe = "Rohes Fleisch", nameEn = "Raw Meat", category = ItemCategory.Raw, hold = HoldItem.Food, maxStack = 5,
                    cookTo = FoodDatabase.GrilledMeat,
                    descDe = "Erst braten: Lagerfeuer oder Grill.", descEn = "Cook it first: campfire or grill." },

                // ---- collectibles (the finder's own copy; the channel counts them in Collectibles)
                new ItemDef { id = ItemIds.Shell, nameDe = "Goldene Muschel", nameEn = "Golden Shell", category = ItemCategory.Collectible, maxStack = 99,
                    descDe = "Glänzt. Sammeln lohnt sich.", descEn = "Shiny. Worth collecting." },
                new ItemDef { id = ItemIds.Page, nameDe = "Logbuch-Seite", nameEn = "Logbook Page", category = ItemCategory.Collectible, maxStack = 99,
                    descDe = "Onkel Gus' Logbuch. Im Handy nachlesen.", descEn = "Uncle Gus's logbook. Read it on the phone." },
                new ItemDef { id = ItemIds.Message, nameDe = "Flaschenpost", nameEn = "Message in a Bottle", category = ItemCategory.Collectible, maxStack = 99,
                    descDe = "Ein Zettel von „T.“", descEn = "A note from “T.”" },
                new ItemDef { id = ItemIds.Treasure, nameDe = "Fundstück", nameEn = "Treasure", category = ItemCategory.Collectible, maxStack = 99,
                    descDe = "Alte Schätze der Insel.", descEn = "Old treasures of the island." },
                new ItemDef { id = ItemIds.MapPiece, nameDe = "Kartenstück", nameEn = "Map Piece", category = ItemCategory.MapPiece, maxStack = 99,
                    descDe = "Alle Teile ergeben eine Schatzkarte.", descEn = "All pieces make a treasure map." },
                new ItemDef { id = ItemIds.TreasureMap, nameDe = "Schatzkarte", nameEn = "Treasure Map", category = ItemCategory.MapPiece, maxStack = 1,
                    descDe = "Benutzen: Das X zeigt den Weg.", descEn = "Use it: the X shows the way." },

                // ---- round 3 (Q20): treasure from sunken chests and wrecks (no slot limit; fish market app → „Funde“)
                Valuable(ItemIds.Coins, "Goldmünzen", "Gold Coins", 35, "Aus einer versunkenen Truhe.", "From a sunken chest."),
                Valuable(ItemIds.Pearl, "Perle", "Pearl", 90, "Schimmert in allen Farben.", "Shimmers in every colour."),
                Valuable(ItemIds.Compass, "Messingkompass", "Brass Compass", 70, "Zeigt immer noch nach Norden. Meistens.", "Still points north. Mostly."),
                Valuable(ItemIds.Spyglass, "Altes Fernrohr", "Old Spyglass", 110, "Von einem Kapitän, der zu weit geschaut hat.", "From a captain who looked too far."),
                Valuable(ItemIds.Gem, "Edelstein", "Gemstone", 160, "Funkelt sogar im Dunkeln.", "Sparkles even in the dark."),
                Valuable(ItemIds.Goblet, "Goldkelch", "Golden Goblet", 240, "Piratenschatz! Sammler zahlen gut.", "Pirate treasure! Collectors pay well."),
                Valuable(ItemIds.Figurine, "Seltsame Figur", "Strange Figurine", 320, "Ein Fisch mit Krone. Niemand weiß, woher.", "A fish wearing a crown. Nobody knows where from."),
                Valuable(ItemIds.FossilTooth, "Urzeit-Zahn", "Prehistoric Tooth", 400, "So groß wie eine Banane. Von wem wohl?", "As big as a banana. Whose could it be?"),
            };
            // every food / drink of the food table is a backpack item with the same id
            var foods = FoodDatabase.All;
            for (int i = 0; i < foods.Count; i++)
            {
                var f = foods[i];
                if (f.source == "filter") continue;   // the filtered water is the bottle item
                l.Add(new ItemDef
                {
                    id = f.id, nameDe = f.nameDe, nameEn = f.nameEn, descDe = f.descDe, descEn = f.descEn,
                    category = f.kind == FoodKind.Drink ? ItemCategory.Drink : ItemCategory.Food, hold = HoldItem.Food,
                    foodId = f.id, maxStack = 10,
                });
            }
            return l;
        }

        static ItemDef Valuable(string id, string de, string en, long value, string descDe, string descEn) => new ItemDef
        {
            id = id, nameDe = de, nameEn = en, category = ItemCategory.Valuable, maxStack = 99, value = value,
            descDe = descDe + " Im Fischmarkt-App verkaufen.", descEn = descEn + " Sell it in the fish market app.",
        };

        static Dictionary<string, ItemDef> byId;

        public static ItemDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, ItemDef>();
                foreach (var d in All) byId[d.id] = d;
            }
            return byId.TryGetValue(id, out var r) ? r : null;
        }

        /// Items sold in the Chopper Shop's weapons tab, cheapest first.
        public static void Sold(List<ItemDef> into)
        {
            into.Clear();
            for (int i = 0; i < All.Count; i++) if (All[i].sold && All[i].price > 0) into.Add(All[i]);
            into.Sort((a, b) => a.price.CompareTo(b.price));
        }

        /// Delivery item id of a shop item crate (Delivery.itemId) – "item:spear_t1".
        public const string DeliveryPrefix = "item:";
        public static string DeliveryId(ItemDef d) => DeliveryPrefix + d.id;

        public static ItemDef FromDeliveryId(string itemId)
        {
            if (itemId == null || !itemId.StartsWith(DeliveryPrefix, System.StringComparison.Ordinal)) return null;
            return Get(itemId.Substring(DeliveryPrefix.Length));
        }

        /// The backpack item that represents a collectible kind (null for secrets – they stay where they are).
        public static string ForCollectible(CollectibleKind k)
        {
            switch (k)
            {
                case CollectibleKind.GoldenShell: return ItemIds.Shell;
                case CollectibleKind.LogbookPage: return ItemIds.Page;
                case CollectibleKind.BottleMessage: return ItemIds.Message;
                case CollectibleKind.Treasure: return ItemIds.Treasure;
                case CollectibleKind.MapPiece: return ItemIds.MapPiece;
                default: return null;
            }
        }
    }
}
