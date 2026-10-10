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
        public float airSeconds;          // Tank: seconds of air at the surface-equivalent rate
        public float maxDepth;            // Tank/Submarine: safe depth rating in meters
        public float swimSpeed = 1f;      // Fins: speed multiplier
        public float lampIntensity;       // Lamp: 0 = no lamp
        public float lampRange;           // Lamp: meters
        public float cameraQuality = 1f;  // Camera: clip quality multiplier
        public float tapeSeconds;         // Camera: recording seconds per dive
        public float maxZoom = 1.5f;      // Camera: max zoom factor
        public float boatSpeed = 1f;      // Boat: speed multiplier

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);
    }

    /// Effective stats from the currently equipped items.
    public struct GearStats
    {
        public float airSeconds, maxDepth, swimSpeed, lampIntensity, lampRange, cameraQuality, tapeSeconds, maxZoom, boatSpeed;
        public bool hasTank, hasLamp, hasSubmarine;
    }

    public static class GearDatabase
    {
        public static readonly List<GearItem> All = new List<GearItem>
        {
            // ----- Tanks (Atemluft + Tiefen-Freigabe) -----
            new GearItem { id = "tank_t0", slot = GearSlot.Tank, tier = 0, ownedAtStart = true, nameDe = "Schnorchel-Set", nameEn = "Snorkel Set",
                descDe = "Luft anhalten. Reicht für ein paar Meter.", descEn = "Hold your breath. Good for a few meters.", airSeconds = 45f, maxDepth = 6f },
            new GearItem { id = "tank_t1", slot = GearSlot.Tank, tier = 1, price = 80, nameDe = "Schrott-Pressluftflasche", nameEn = "Scrap Air Tank",
                descDe = "Verbeult, aber dicht. Ca. 5 Minuten Luft, bis 15 m.", descEn = "Dented but airtight. About 5 minutes of air, down to 15 m.", airSeconds = 360f, maxDepth = 15f },
            new GearItem { id = "tank_t2", slot = GearSlot.Tank, tier = 2, price = 450, followersNeeded = 150, nameDe = "12-Liter-Stahlflasche", nameEn = "12 L Steel Tank",
                descDe = "Mehr Luft, bis 25 m. Für den Kelpwald.", descEn = "More air, down to 25 m. For the kelp forest.", airSeconds = 540f, maxDepth = 25f },
            new GearItem { id = "tank_t3", slot = GearSlot.Tank, tier = 3, price = 1800, followersNeeded = 800, nameDe = "Doppelflasche", nameEn = "Twin Tanks",
                descDe = "Zwei Flaschen, bis 40 m. Für das Wrack.", descEn = "Two tanks, down to 40 m. For the wreck.", airSeconds = 780f, maxDepth = 40f },
            new GearItem { id = "tank_t4", slot = GearSlot.Tank, tier = 4, price = 6500, followersNeeded = 5000, nameDe = "Nitrox-Set", nameEn = "Nitrox Set",
                descDe = "Spezialgas, bis 60 m. Für die Blauwasser-Kante.", descEn = "Special gas, down to 60 m. For the blue water drop-off.", airSeconds = 960f, maxDepth = 60f },
            new GearItem { id = "tank_t5", slot = GearSlot.Tank, tier = 5, price = 25000, followersNeeded = 25000, nameDe = "Trimix-Rig", nameEn = "Trimix Rig",
                descDe = "Profi-Tieftauchen bis 100 m.", descEn = "Pro deep diving down to 100 m.", airSeconds = 1200f, maxDepth = 100f },

            // ----- Submarines (later in the game) -----
            new GearItem { id = "sub_t1", slot = GearSlot.Submarine, tier = 1, price = 120000, followersNeeded = 50000, comingSoon = true, nameDe = "Mini-U-Boot „Boiler“", nameEn = "Mini Sub “Boiler”",
                descDe = "Ein Heißwasserboiler mit Bullauge. Bis 200 m.", descEn = "A water heater with a porthole. Down to 200 m.", maxDepth = 200f },
            new GearItem { id = "sub_t2", slot = GearSlot.Submarine, tier = 2, price = 750000, followersNeeded = 250000, comingSoon = true, nameDe = "Tiefsee-U-Boot", nameEn = "Deep Sea Submarine",
                descDe = "Für den Abgrund und das Finale.", descEn = "For the abyss and the finale.", maxDepth = 1000f },

            // ----- Fins -----
            new GearItem { id = "fins_t0", slot = GearSlot.Fins, tier = 0, ownedAtStart = true, nameDe = "Pool-Flossen", nameEn = "Pool Fins",
                descDe = "Quietschgelb, etwas zu klein.", descEn = "Squeaky yellow, slightly too small.", swimSpeed = 1f },
            new GearItem { id = "fins_t1", slot = GearSlot.Fins, tier = 1, price = 150, nameDe = "Split-Flossen", nameEn = "Split Fins",
                descDe = "25 % schneller schwimmen.", descEn = "Swim 25% faster.", swimSpeed = 1.25f },
            new GearItem { id = "fins_t2", slot = GearSlot.Fins, tier = 2, price = 1200, followersNeeded = 1000, nameDe = "Carbon-Flossen", nameEn = "Carbon Fins",
                descDe = "50 % schneller schwimmen.", descEn = "Swim 50% faster.", swimSpeed = 1.5f },
            new GearItem { id = "fins_t3", slot = GearSlot.Fins, tier = 3, price = 9000, followersNeeded = 2500, nameDe = "Tauchscooter", nameEn = "Dive Scooter",
                descDe = "Zieht dich mit mehr als doppelter Geschwindigkeit durchs Wasser.", descEn = "Pulls you through the water at more than double speed.", swimSpeed = 2.2f },

            // ----- Lamps -----
            new GearItem { id = "lamp_t1", slot = GearSlot.Lamp, tier = 1, price = 60, nameDe = "Taschenlampe mit Gaffa-Tape", nameEn = "Duct-Taped Flashlight",
                descDe = "Bringt in der Tiefe die Farben zurück.", descEn = "Brings colors back in the depths.", lampIntensity = 1f, lampRange = 12f },
            new GearItem { id = "lamp_t2", slot = GearSlot.Lamp, tier = 2, price = 600, followersNeeded = 300, nameDe = "Video-Leuchte", nameEn = "Video Light",
                descDe = "Breiter, heller Lichtkegel. Bessere Clips im Dunkeln.", descEn = "Wide, bright beam. Better clips in the dark.", lampIntensity = 2f, lampRange = 20f },
            new GearItem { id = "lamp_t3", slot = GearSlot.Lamp, tier = 3, price = 4000, followersNeeded = 3000, nameDe = "Doppel-Strobes", nameEn = "Twin Strobes",
                descDe = "Profi-Licht für die Tiefsee.", descEn = "Pro lighting for the deep sea.", lampIntensity = 3f, lampRange = 30f },

            // ----- Cameras -----
            new GearItem { id = "cam_t0", slot = GearSlot.Camera, tier = 0, ownedAtStart = true, nameDe = "Handy im Gefrierbeutel", nameEn = "Phone in a Freezer Bag",
                descDe = "Billig, wackelig, 90 Sekunden Speicher pro Tauchgang.", descEn = "Cheap, shaky, 90 seconds of storage per dive.", cameraQuality = 0.6f, tapeSeconds = 90f, maxZoom = 1.5f },
            new GearItem { id = "cam_t1", slot = GearSlot.Camera, tier = 1, price = 250, nameDe = "Action-Cam am Stick", nameEn = "Action Cam on a Stick",
                descDe = "Schärfer und 4 Minuten Speicher.", descEn = "Sharper, with 4 minutes of storage.", cameraQuality = 0.85f, tapeSeconds = 240f, maxZoom = 2f },
            new GearItem { id = "cam_t2", slot = GearSlot.Camera, tier = 2, price = 2200, followersNeeded = 500, nameDe = "Unterwasser-Camcorder", nameEn = "Underwater Camcorder",
                descDe = "Echter Zoom, 8 Minuten Speicher.", descEn = "Real zoom, 8 minutes of storage.", cameraQuality = 1.05f, tapeSeconds = 480f, maxZoom = 3f },
            new GearItem { id = "cam_t3", slot = GearSlot.Camera, tier = 3, price = 12000, followersNeeded = 5000, nameDe = "Profi-Kamera im Gehäuse", nameEn = "Pro Camera in Housing",
                descDe = "Kinoreife Bilder, 15 Minuten Speicher.", descEn = "Cinematic images, 15 minutes of storage.", cameraQuality = 1.3f, tapeSeconds = 900f, maxZoom = 4f },
            new GearItem { id = "cam_t4", slot = GearSlot.Camera, tier = 4, price = 60000, followersNeeded = 50000, nameDe = "Kino-Rig 6K", nameEn = "6K Cinema Rig",
                descDe = "Die beste Kamera im Ozean.", descEn = "The best camera in the ocean.", cameraQuality = 1.6f, tapeSeconds = 1800f, maxZoom = 6f },

            // ----- Boat -----
            new GearItem { id = "boat_t0", slot = GearSlot.Boat, tier = 0, ownedAtStart = true, nameDe = "Boot „Rostlaube“", nameEn = "Boat “Rust Bucket”",
                descDe = "Onkel Gus' altes Fischerboot. Qualmt.", descEn = "Uncle Gus's old fishing boat. It smokes.", boatSpeed = 1f },
            new GearItem { id = "boat_t1", slot = GearSlot.Boat, tier = 1, price = 900, followersNeeded = 200, nameDe = "Neuer Außenborder", nameEn = "New Outboard Motor",
                descDe = "40 % schneller zu den Tauchplätzen.", descEn = "40% faster to the dive sites.", boatSpeed = 1.4f },
            new GearItem { id = "boat_t2", slot = GearSlot.Boat, tier = 2, price = 7500, followersNeeded = 2500, nameDe = "Schlauchboot-Umbau (RIB)", nameEn = "RIB Conversion",
                descDe = "Fast doppelt so schnell und viel ruhiger im Seegang.", descEn = "Almost twice as fast and much calmer in waves.", boatSpeed = 1.9f },
        };

        public static GearItem Get(string id) => All.Find(g => g.id == id);

        /// Best owned item per slot is equipped automatically.
        public static GearItem Equipped(GameState s, GearSlot slot)
        {
            GearItem best = null;
            foreach (var id in s.ownedItems)
            {
                var g = Get(id);
                if (g != null && g.slot == slot && (best == null || g.tier > best.tier)) best = g;
            }
            return best;
        }

        public static GearStats Stats(GameState s)
        {
            var st = new GearStats { swimSpeed = 1f, cameraQuality = 0.6f, tapeSeconds = 90f, maxZoom = 1.5f, boatSpeed = 1f, airSeconds = 45f, maxDepth = 6f };
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
            st.airSeconds *= DifficultySettings.For(s.difficulty).airMultiplier;
            return st;
        }

        public static void GiveStartingGear(GameState s)
        {
            foreach (var g in All)
                if (g.ownedAtStart && !s.ownedItems.Contains(g.id)) s.ownedItems.Add(g.id);
        }
    }
}
