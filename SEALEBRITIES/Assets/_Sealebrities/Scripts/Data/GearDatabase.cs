using System.Collections.Generic;

namespace Sealebrities
{
    public class GearItem
    {
        public string id;
        public GearSlot slot;
        public int tier;
        public string nameDe, nameEn, descDe, descEn;
        public long price;
        public long followersNeeded;
        public bool comingSoon;           // visible in the shop but not purchasable in this build
        public bool ownedAtStart;

        // Stats (only the ones relevant to the slot are used)
        public float airSeconds;          // Tank: seconds of air at the surface (consumption × Progression.AirRate(depth))
        public float maxDepth;            // Tank/Submarine: safe depth rating in meters
        public float swimSpeed = 1f;      // Fins: speed multiplier
        public float lampIntensity;       // Lamp: 0 = no lamp
        public float lampRange;           // Lamp: meters
        public float cameraQuality = 1f;  // Camera: clip quality multiplier
        public float tapeSeconds;         // Camera: recording seconds per dive
        public float maxZoom = 1.5f;      // Camera: max zoom factor
        public float boatSpeed = 1f;      // Boat: speed multiplier
        public float fishSpeed = 1f;      // Rod: bite delay divisor
        public float fishLuck;            // Rod: 0..1, shifts catches toward rarer, heavier fish

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);
    }

    /// Effective stats from the LOCAL player's equipped items.
    public struct GearStats
    {
        public float airSeconds, maxDepth, swimSpeed, lampIntensity, lampRange, cameraQuality, tapeSeconds, maxZoom, boatSpeed;
        public float fishSpeed, fishLuck;
        public bool hasTank, hasLamp, hasSubmarine, hasRod;
    }

    /// Gear catalogue. Prices are balanced with the economy simulation (see Progression): roughly one upgrade every
    /// 1–3 play days early on, the 6K rig near the end. Air: breath-hold 20 s, tanks 90 → 220 s (modest steps; dives
    /// stay ~80–90 s at each site's depth because consumption grows with depth).
    public static class GearDatabase
    {
        public static readonly List<GearItem> All = new List<GearItem>
        {
            // ----- Tanks (air + depth rating) -----
            new GearItem { id = "tank_t0", slot = GearSlot.Tank, tier = 0, ownedAtStart = true, nameDe = "Schnorchel-Set", nameEn = "Snorkel Set",
                descDe = "Luft anhalten: ca. 20 Sekunden, ein paar Meter tief.", descEn = "Hold your breath: about 20 seconds, a few meters deep.", airSeconds = 20f, maxDepth = 6f },
            new GearItem { id = "tank_t1", slot = GearSlot.Tank, tier = 1, price = 130, nameDe = "Schrott-Pressluftflasche", nameEn = "Scrap Air Tank",
                descDe = "Verbeult, aber dicht. 90 Sekunden Luft, bis 15 m.", descEn = "Dented but airtight. 90 seconds of air, down to 15 m.", airSeconds = 90f, maxDepth = 15f },
            new GearItem { id = "tank_t2", slot = GearSlot.Tank, tier = 2, price = 900, followersNeeded = 250, nameDe = "12-Liter-Stahlflasche", nameEn = "12 L Steel Tank",
                descDe = "Etwas mehr Luft, bis 25 m. Für den Kelpwald.", descEn = "A bit more air, down to 25 m. For the kelp forest.", airSeconds = 115f, maxDepth = 25f },
            new GearItem { id = "tank_t3", slot = GearSlot.Tank, tier = 3, price = 5000, followersNeeded = 2500, nameDe = "Doppelflasche", nameEn = "Twin Tanks",
                descDe = "Zwei Flaschen, bis 40 m. Für das Wrack.", descEn = "Two tanks, down to 40 m. For the wreck.", airSeconds = 140f, maxDepth = 40f },
            new GearItem { id = "tank_t4", slot = GearSlot.Tank, tier = 4, price = 25000, followersNeeded = 15000, nameDe = "Nitrox-Set", nameEn = "Nitrox Set",
                descDe = "Spezialgas, bis 60 m. Für die Blauwasser-Kante.", descEn = "Special gas, down to 60 m. For the blue water drop-off.", airSeconds = 170f, maxDepth = 60f },
            new GearItem { id = "tank_t5", slot = GearSlot.Tank, tier = 5, price = 120000, followersNeeded = 100000, nameDe = "Trimix-Rig", nameEn = "Trimix Rig",
                descDe = "Profi-Tieftauchen bis 100 m. Für den Tiefseegraben.", descEn = "Pro deep diving down to 100 m. For the deep trench.", airSeconds = 220f, maxDepth = 100f },

            // ----- Submarines (later in the game) -----
            new GearItem { id = "sub_t1", slot = GearSlot.Submarine, tier = 1, price = 400000, followersNeeded = 50000, comingSoon = true, nameDe = "Mini-U-Boot „Boiler“", nameEn = "Mini Sub “Boiler”",
                descDe = "Ein Heißwasserboiler mit Bullauge. Bis 200 m.", descEn = "A water heater with a porthole. Down to 200 m.", maxDepth = 200f },
            new GearItem { id = "sub_t2", slot = GearSlot.Submarine, tier = 2, price = 2000000, followersNeeded = 250000, comingSoon = true, nameDe = "Tiefsee-U-Boot", nameEn = "Deep Sea Submarine",
                descDe = "Für den Abgrund.", descEn = "For the abyss.", maxDepth = 1000f },

            // ----- Fins -----
            new GearItem { id = "fins_t0", slot = GearSlot.Fins, tier = 0, ownedAtStart = true, nameDe = "Pool-Flossen", nameEn = "Pool Fins",
                descDe = "Quietschgelb, etwas zu klein.", descEn = "Squeaky yellow, slightly too small.", swimSpeed = 1f },
            new GearItem { id = "fins_t1", slot = GearSlot.Fins, tier = 1, price = 450, followersNeeded = 150, nameDe = "Split-Flossen", nameEn = "Split Fins",
                descDe = "25 % schneller schwimmen.", descEn = "Swim 25% faster.", swimSpeed = 1.25f },
            new GearItem { id = "fins_t2", slot = GearSlot.Fins, tier = 2, price = 4000, followersNeeded = 2000, nameDe = "Carbon-Flossen", nameEn = "Carbon Fins",
                descDe = "50 % schneller schwimmen.", descEn = "Swim 50% faster.", swimSpeed = 1.5f },
            new GearItem { id = "fins_t3", slot = GearSlot.Fins, tier = 3, price = 40000, followersNeeded = 25000, nameDe = "Tauchscooter", nameEn = "Dive Scooter",
                descDe = "Zieht dich mit mehr als doppelter Geschwindigkeit durchs Wasser.", descEn = "Pulls you through the water at more than double speed.", swimSpeed = 2.2f },

            // ----- Lamps (the flashlight is a starting hotbar item) -----
            new GearItem { id = "lamp_t1", slot = GearSlot.Lamp, tier = 1, ownedAtStart = true, nameDe = "Taschenlampe mit Gaffa-Tape", nameEn = "Duct-Taped Flashlight",
                descDe = "Bringt in der Tiefe die Farben zurück.", descEn = "Brings colors back in the depths.", lampIntensity = 1f, lampRange = 12f },
            new GearItem { id = "lamp_t2", slot = GearSlot.Lamp, tier = 2, price = 2500, followersNeeded = 800, nameDe = "Video-Leuchte", nameEn = "Video Light",
                descDe = "Breiter, heller Lichtkegel. Bessere Clips im Dunkeln.", descEn = "Wide, bright beam. Better clips in the dark.", lampIntensity = 2f, lampRange = 20f },
            new GearItem { id = "lamp_t3", slot = GearSlot.Lamp, tier = 3, price = 20000, followersNeeded = 10000, nameDe = "Doppel-Strobes", nameEn = "Twin Strobes",
                descDe = "Profi-Licht für die Tiefsee.", descEn = "Pro lighting for the deep sea.", lampIntensity = 3f, lampRange = 30f },

            // ----- Cameras -----
            new GearItem { id = "cam_t0", slot = GearSlot.Camera, tier = 0, ownedAtStart = true, nameDe = "Handy im Gefrierbeutel", nameEn = "Phone in a Freezer Bag",
                descDe = "Billig, wackelig, 90 Sekunden Speicher pro Tauchgang.", descEn = "Cheap, shaky, 90 seconds of storage per dive.", cameraQuality = 0.6f, tapeSeconds = 90f, maxZoom = 1.5f },
            new GearItem { id = "cam_t1", slot = GearSlot.Camera, tier = 1, price = 450, followersNeeded = 100, nameDe = "Action-Cam am Stick", nameEn = "Action Cam on a Stick",
                descDe = "Schärfer und 4 Minuten Speicher.", descEn = "Sharper, with 4 minutes of storage.", cameraQuality = 0.8f, tapeSeconds = 240f, maxZoom = 2f },
            new GearItem { id = "cam_t2", slot = GearSlot.Camera, tier = 2, price = 6000, followersNeeded = 1500, nameDe = "Unterwasser-Camcorder", nameEn = "Underwater Camcorder",
                descDe = "Echter Zoom, 8 Minuten Speicher.", descEn = "Real zoom, 8 minutes of storage.", cameraQuality = 1.0f, tapeSeconds = 480f, maxZoom = 3f },
            new GearItem { id = "cam_t3", slot = GearSlot.Camera, tier = 3, price = 45000, followersNeeded = 25000, nameDe = "Profi-Kamera im Gehäuse", nameEn = "Pro Camera in Housing",
                descDe = "Kinoreife Bilder, 15 Minuten Speicher.", descEn = "Cinematic images, 15 minutes of storage.", cameraQuality = 1.25f, tapeSeconds = 900f, maxZoom = 4f },
            new GearItem { id = "cam_t4", slot = GearSlot.Camera, tier = 4, price = 300000, followersNeeded = 250000, nameDe = "Kino-Rig 6K", nameEn = "6K Cinema Rig",
                descDe = "Die beste Kamera im Ozean.", descEn = "The best camera in the ocean.", cameraQuality = 1.6f, tapeSeconds = 1800f, maxZoom = 6f },

            // ----- Boat -----
            new GearItem { id = "boat_t0", slot = GearSlot.Boat, tier = 0, ownedAtStart = true, nameDe = "Boot „Rostlaube“", nameEn = "Boat “Rust Bucket”",
                descDe = "Onkel Gus' altes Fischerboot. Qualmt.", descEn = "Uncle Gus's old fishing boat. It smokes.", boatSpeed = 1f },
            new GearItem { id = "boat_t1", slot = GearSlot.Boat, tier = 1, price = 1500, followersNeeded = 400, nameDe = "Neuer Außenborder", nameEn = "New Outboard Motor",
                descDe = "40 % schneller zu den Tauchplätzen.", descEn = "40% faster to the dive sites.", boatSpeed = 1.4f },
            new GearItem { id = "boat_t2", slot = GearSlot.Boat, tier = 2, price = 30000, followersNeeded = 10000, nameDe = "Schlauchboot-Umbau (RIB)", nameEn = "RIB Conversion",
                descDe = "Fast doppelt so schnell und viel ruhiger im Seegang.", descEn = "Almost twice as fast and much calmer in waves.", boatSpeed = 1.9f },

            // ----- Fishing rods (two-handed hotbar item) -----
            new GearItem { id = "rod_t0", slot = GearSlot.Rod, tier = 0, ownedAtStart = true, nameDe = "Bambusrute", nameEn = "Bamboo Rod",
                descDe = "Onkel Gus' alte Angel. Geduld ist alles.", descEn = "Uncle Gus's old rod. Patience is everything.", fishSpeed = 1f, fishLuck = 0f },
            new GearItem { id = "rod_t1", slot = GearSlot.Rod, tier = 1, price = 350, nameDe = "Carbon-Angel", nameEn = "Carbon Rod",
                descDe = "Beißt schneller an, öfter größere Fische.", descEn = "Faster bites, bigger fish more often.", fishSpeed = 1.35f, fishLuck = 0.35f },
            new GearItem { id = "rod_t2", slot = GearSlot.Rod, tier = 2, price = 2500, followersNeeded = 1000, nameDe = "Hochsee-Rute", nameEn = "Deep Sea Rod",
                descDe = "Für Thunfisch und Goldmakrele.", descEn = "For tuna and mahi-mahi.", fishSpeed = 1.7f, fishLuck = 0.7f },
        };

        static Dictionary<string, GearItem> byId;

        public static GearItem Get(string id)
        {
            if (id == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, GearItem>();
                foreach (var g in All) byId[g.id] = g;
            }
            return byId.TryGetValue(id, out var item) ? item : null;
        }

        /// Item used in a slot: the player's choice (GameState.equipped) when owned, otherwise the best owned item.
        public static GearItem Equipped(GameState s, GearSlot slot)
        {
            if (s == null) return null;
            var eq = s.equipped;
            if (eq != null)
            {
                for (int i = 0; i < eq.Count; i++)
                {
                    var g = Get(eq[i]);
                    if (g != null && g.slot == slot && s.ownedItems.Contains(g.id)) return g;
                }
            }
            return BestOwned(s, slot);
        }

        public static GearItem BestOwned(GameState s, GearSlot slot)
        {
            GearItem best = null;
            var owned = s.ownedItems;
            for (int i = 0; i < owned.Count; i++)
            {
                var g = Get(owned[i]);
                if (g != null && g.slot == slot && (best == null || g.tier > best.tier)) best = g;
            }
            return best;
        }

        /// Fills `into` with the owned items of a slot, lowest tier first (inventory screen).
        public static void OwnedInSlot(GameState s, GearSlot slot, List<GearItem> into)
        {
            into.Clear();
            if (s == null) return;
            for (int i = 0; i < All.Count; i++)
                if (All[i].slot == slot && s.ownedItems.Contains(All[i].id)) into.Add(All[i]);
        }

        public static GearStats Stats(GameState s)
        {
            var st = new GearStats
            {
                swimSpeed = 1f, cameraQuality = 0.6f, tapeSeconds = 90f, maxZoom = 1.5f, boatSpeed = 1f,
                airSeconds = Progression.BreathHoldSeconds, maxDepth = 6f, fishSpeed = 1f, fishLuck = 0f
            };
            if (s == null) return st;
            var tank = Equipped(s, GearSlot.Tank);
            if (tank != null) { st.airSeconds = tank.airSeconds; st.maxDepth = tank.maxDepth; st.hasTank = tank.tier > 0; }
            var fins = Equipped(s, GearSlot.Fins);
            if (fins != null) st.swimSpeed = fins.swimSpeed;
            var lamp = Equipped(s, GearSlot.Lamp);
            if (lamp != null) { st.lampIntensity = lamp.lampIntensity; st.lampRange = lamp.lampRange; st.hasLamp = true; }
            var cam = Equipped(s, GearSlot.Camera);
            if (cam != null) { st.cameraQuality = cam.cameraQuality; st.tapeSeconds = cam.tapeSeconds; st.maxZoom = cam.maxZoom; }
            var boat = Equipped(s, GearSlot.Boat);
            if (boat != null) st.boatSpeed = boat.boatSpeed;
            var sub = Equipped(s, GearSlot.Submarine);
            if (sub != null) { st.hasSubmarine = true; }
            var rod = Equipped(s, GearSlot.Rod);
            if (rod != null) { st.fishSpeed = rod.fishSpeed; st.fishLuck = rod.fishLuck; st.hasRod = true; }
            st.airSeconds *= DifficultySettings.For(s.difficulty).airMultiplier;
            return st;
        }

        public static void GiveStartingGear(GameState s)
        {
            foreach (var g in All)
                if (g.ownedAtStart && !s.ownedItems.Contains(g.id)) s.ownedItems.Add(g.id);
        }
    }

    /// Equipment choice of the LOCAL player (per player, never synced): which owned tank/fins/lamp/camera/rod to use.
    public static class Gear
    {
        static GameState S => GameManager.I != null ? GameManager.I.State : null;

        /// Equips an owned item for the local player. Returns false when it is unknown or not owned.
        public static bool Equip(string itemId)
        {
            var s = S;
            var item = GearDatabase.Get(itemId);
            if (s == null || item == null || !s.ownedItems.Contains(itemId)) return false;
            if (item.slot == GearSlot.Tank && LocalInWater())
            {
                var cur = GearDatabase.Equipped(s, GearSlot.Tank);
                if (cur == null || cur.id != itemId)
                {
                    // feedback #17: the tank is only refilled (and swapped) on land or on the boat
                    GameEvents.RaiseToast(Loc.Tr("Flasche nur an Land oder an Bord wechseln", "Swap tanks only on land or aboard"));
                    return false;
                }
            }
            RemoveSlot(s, item.slot);
            s.equipped.Add(itemId);
            GameEvents.RaiseGearChanged();
            GameManager.I.Save();
            return true;
        }

        /// The local player swims or dives right now.
        public static bool LocalInWater()
        {
            var p = Refs.Player;
            return p != null && !(p is UnityEngine.Object o && o == null) && p.IsInWater;
        }

        /// Forgets the choice for a slot → the best owned item is used again.
        public static void ClearChoice(GearSlot slot)
        {
            var s = S;
            if (s == null) return;
            if (RemoveSlot(s, slot)) GameEvents.RaiseGearChanged();
        }

        public static bool IsEquipped(string itemId)
        {
            var s = S;
            var item = GearDatabase.Get(itemId);
            if (s == null || item == null) return false;
            var e = GearDatabase.Equipped(s, item.slot);
            return e != null && e.id == itemId;
        }

        public static GearItem Equipped(GearSlot slot) => GearDatabase.Equipped(S, slot);

        static bool RemoveSlot(GameState s, GearSlot slot)
        {
            bool removed = false;
            for (int i = s.equipped.Count - 1; i >= 0; i--)
            {
                var g = GearDatabase.Get(s.equipped[i]);
                if (g == null || g.slot == slot)
                {
                    s.equipped.RemoveAt(i);
                    removed = true;
                }
            }
            return removed;
        }
    }
}
