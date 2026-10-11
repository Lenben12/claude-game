using System.Collections.Generic;

namespace Sealebrities
{
    /// Chopper Shop categories (L.12 – big tiles in the redesigned app).
    public enum ShopCategory { Diving, Camera, Boat, Fishing, Snacks, Parts }

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
        /// Never sold in the shop (rewards, e.g. rod_t3 from Dr. Fin R4).
        public bool notSold;
        /// Only listed while this story flag is set (e.g. radio_battery during Toni's quest step T4).
        public string requiredFlag;

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

        // ---- round 2 (D.3 rods, Q32 camera mods)
        public float castMin = 4f, castMax = 17f;   // Rod: cast distance range in meters
        public float reelPower = 1f;      // Rod: reel progress multiplier (D.1)
        public float greenBonus;          // Rod: widens the green tension zone on each side, in %-points
        public float lineStrength = 1f;   // Rod: snap-time multiplier at 100 % tension
        public bool openSea;              // Rod: may use the open-sea table (D.2); the bamboo rod may not
        public bool rareBitePing;         // Rod: bobber glows #F2C94C when a rare+ fish bites (rod_t3)
        public float zoomBonus = 1f;      // CameraMod: multiplier on the camera's max zoom
        public bool nightVision;          // CameraMod: night-vision viewfinder, darkness counts as full light

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);

        public ShopCategory Category
        {
            get
            {
                switch (slot)
                {
                    case GearSlot.Camera: case GearSlot.CameraMod: return ShopCategory.Camera;
                    case GearSlot.Boat: case GearSlot.Compressor: case GearSlot.BoatWinch: case GearSlot.Submarine: return ShopCategory.Boat;
                    case GearSlot.Rod: return ShopCategory.Fishing;
                    case GearSlot.Part: return ShopCategory.Parts;
                    default: return ShopCategory.Diving;   // tank, fins, lamp, radio
                }
            }
        }
    }

    /// Effective stats from the LOCAL player's equipped items (boat equipment and camera mods: owned by the crew).
    public struct GearStats
    {
        public float airSeconds, maxDepth, swimSpeed, lampIntensity, lampRange, cameraQuality, tapeSeconds, maxZoom, boatSpeed;
        public float fishSpeed, fishLuck;
        public bool hasTank, hasLamp, hasSubmarine, hasRod;
        // ---- round 2
        public float castMin, castMax, reelPower, greenBonus, lineStrength, zoomBonus;
        public bool rodOpenSea, rodRarePing;
        public bool hasBoatCompressor, hasRadio, nightVision, hasBoatWinch;
        /// Q51 visible gear: tier of the equipped tank / fins / rod (0 = starter).
        public int tankTier, finTier, rodTier, cameraTier;
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
            // K.1: the first tank costs 100 $ (was 130) – ordered after ≈ 7 minutes of fishing
            new GearItem { id = "tank_t1", slot = GearSlot.Tank, tier = 1, price = 100, nameDe = "Schrott-Pressluftflasche", nameEn = "Scrap Air Tank",
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

            // ----- Fishing rods (two-handed hotbar item) – D.3: farther, rarer, faster; Dr. Fin's rod chain R1–R4 -----
            new GearItem { id = "rod_t0", slot = GearSlot.Rod, tier = 0, ownedAtStart = true, nameDe = "Bambusrute", nameEn = "Bamboo Rod",
                descDe = "Onkel Gus' alte Angel. Geduld ist alles.", descEn = "Uncle Gus's old rod. Patience is everything.", fishSpeed = 1f, fishLuck = 0f,
                castMin = 4f, castMax = 17f, reelPower = 1f, greenBonus = 0f, lineStrength = 1f, openSea = false },
            new GearItem { id = "rod_t1", slot = GearSlot.Rod, tier = 1, price = 350, nameDe = "Carbon-Angel", nameEn = "Carbon Rod",
                descDe = "Weiter, schneller, öfter größere Fische. Auch für die Hochsee.", descEn = "Farther, faster, bigger fish more often. Fit for the open sea.", fishSpeed = 1.35f, fishLuck = 0.35f,
                castMin = 4f, castMax = 20f, reelPower = 1.15f, greenBonus = 3f, lineStrength = 1.25f, openSea = true },
            new GearItem { id = "rod_t2", slot = GearSlot.Rod, tier = 2, price = 2500, followersNeeded = 1000, nameDe = "Hochsee-Rute", nameEn = "Deep Sea Rod",
                descDe = "Für Thunfisch, Goldmakrele – und Kapitän Pieks.", descEn = "For tuna, mahi-mahi – and Captain Poke.", fishSpeed = 1.7f, fishLuck = 0.7f,
                castMin = 5f, castMax = 24f, reelPower = 1.3f, greenBonus = 5f, lineStrength = 1.5f, openSea = true },
            new GearItem { id = "rod_t3", slot = GearSlot.Rod, tier = 3, notSold = true, nameDe = "Forschungsrute", nameEn = "Research Rod",
                descDe = "Von Dr. Fin. Der Schwimmer leuchtet, wenn ein seltener Fisch beißt.", descEn = "From Dr. Fin. The bobber glows when a rare fish bites.", fishSpeed = 2f, fishLuck = 1f,
                castMin = 6f, castMax = 28f, reelPower = 1.45f, greenBonus = 7f, lineStrength = 1.8f, openSea = true, rareBitePing = true },

            // ----- Boat equipment (owned by the crew) -----
            new GearItem { id = "boat_compressor", slot = GearSlot.Compressor, tier = 1, price = 600, followersNeeded = 250, nameDe = "Boot-Kompressor", nameEn = "Boat Compressor",
                descDe = "Füllt deine Flasche direkt an Bord.", descEn = "Refills your tank right on board." },
            new GearItem { id = "boat_winch", slot = GearSlot.BoatWinch, tier = 1, price = 400, followersNeeded = 250, nameDe = "Bootswinde", nameEn = "Boat Winch",
                descDe = "Haken dran, hochkurbeln: schwere Bergungsfunde bis 40 m vom Boot.", descEn = "Hook it, crank it: heavy salvage up to 40 m from the boat." },

            // ----- Diving extras -----
            new GearItem { id = "uw_radio", slot = GearSlot.Radio, tier = 1, price = 2500, followersNeeded = 2500, nameDe = "Unterwasser-Funk", nameEn = "Underwater Radio",
                descDe = "Quatschen beim Tauchen. Krächzt ein bisschen.", descEn = "Chat while diving. Crackles a bit." },

            // ----- Camera upgrades (Q32) -----
            new GearItem { id = "cammod_zoom", slot = GearSlot.CameraMod, tier = 1, price = 3000, followersNeeded = 1500, nameDe = "Zoom-Objektiv", nameEn = "Zoom Lens",
                descDe = "Anderthalbfacher Zoom für jede Kamera. Fürs Sealog-Foto aus sicherer Entfernung.", descEn = "1.5× zoom for any camera. Sealog photos from a safe distance.", zoomBonus = 1.5f },
            new GearItem { id = "cammod_nv", slot = GearSlot.CameraMod, tier = 2, price = 12000, followersNeeded = 10000, nameDe = "Nachtsicht-Modul", nameEn = "Night Vision Module",
                descDe = "Lampentaste beim Zielen: grüne Nachtsicht. Dunkle Clips zählen voll.", descEn = "Lamp key while aiming: green night vision. Dark clips count in full.", nightVision = true },

            // ----- Parts (L.12 category „Teile“) -----
            new GearItem { id = "radio_battery", slot = GearSlot.Part, tier = 1, price = 80, requiredFlag = StoryKeys.ToniT4Active, nameDe = "Funkbatterie", nameEn = "Radio Battery",
                descDe = "Für Tonis altes Funkgerät. Riecht nach 1987.", descEn = "For Tony's old radio. Smells like 1987." },
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
                airSeconds = Progression.BreathHoldSeconds, maxDepth = 6f, fishSpeed = 1f, fishLuck = 0f,
                castMin = 4f, castMax = 17f, reelPower = 1f, greenBonus = 0f, lineStrength = 1f, zoomBonus = 1f
            };
            if (s == null) return st;
            var tank = Equipped(s, GearSlot.Tank);
            if (tank != null) { st.airSeconds = tank.airSeconds; st.maxDepth = tank.maxDepth; st.hasTank = tank.tier > 0; st.tankTier = tank.tier; }
            var fins = Equipped(s, GearSlot.Fins);
            if (fins != null) { st.swimSpeed = fins.swimSpeed; st.finTier = fins.tier; }
            var lamp = Equipped(s, GearSlot.Lamp);
            if (lamp != null) { st.lampIntensity = lamp.lampIntensity; st.lampRange = lamp.lampRange; st.hasLamp = true; }
            var cam = Equipped(s, GearSlot.Camera);
            if (cam != null) { st.cameraQuality = cam.cameraQuality; st.tapeSeconds = cam.tapeSeconds; st.maxZoom = cam.maxZoom; st.cameraTier = cam.tier; }
            var boat = Equipped(s, GearSlot.Boat);
            if (boat != null) st.boatSpeed = boat.boatSpeed;
            var sub = Equipped(s, GearSlot.Submarine);
            if (sub != null) { st.hasSubmarine = true; }
            var rod = Equipped(s, GearSlot.Rod);
            if (rod != null)
            {
                st.fishSpeed = rod.fishSpeed; st.fishLuck = rod.fishLuck; st.hasRod = true; st.rodTier = rod.tier;
                st.castMin = rod.castMin; st.castMax = rod.castMax; st.reelPower = rod.reelPower; st.greenBonus = rod.greenBonus;
                st.lineStrength = rod.lineStrength; st.rodOpenSea = rod.openSea; st.rodRarePing = rod.rareBitePing;
            }
            // crew equipment: owned = in use (one boat, one compressor, one winch; camera mods stack)
            var owned = s.ownedItems;
            for (int i = 0; i < owned.Count; i++)
            {
                var g = Get(owned[i]);
                if (g == null) continue;
                switch (g.slot)
                {
                    case GearSlot.Compressor: st.hasBoatCompressor = true; break;
                    case GearSlot.BoatWinch: st.hasBoatWinch = true; break;
                    case GearSlot.CameraMod:
                        if (g.zoomBonus > st.zoomBonus) st.zoomBonus = g.zoomBonus;
                        if (g.nightVision) st.nightVision = true;
                        break;
                }
            }
            st.maxZoom *= st.zoomBonus;
            // the underwater radio is owned by the crew and equipped per player (Gear.ClearChoice/Equip keep the choice)
            var radio = Equipped(s, GearSlot.Radio);
            st.hasRadio = radio != null;
            st.airSeconds *= DifficultySettings.For(s.difficulty).airMultiplier;
            return st;
        }

        /// Shown in the Chopper Shop at all (not a reward-only item, story flag set when one is required).
        public static bool IsListed(GearItem g)
        {
            if (g == null || g.notSold || g.ownedAtStart) return false;
            return string.IsNullOrEmpty(g.requiredFlag) || StoryFlags.Has(g.requiredFlag);
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
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Flasche nur an Land wechseln", "Swap tanks on land"), Loc.Tr("oder an Bord", "or aboard"));
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
