using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    public class SpeciesDef
    {
        public string id;
        public string nameDe, nameEn, latin;
        public string siteId;
        public float minDepth, maxDepth;          // meters below sea level where it spawns
        public float minCm, maxCm;                // body length range (wingspan for rays, bell for jellies)
        public Rarity rarity;
        public int baseValue;                     // views/money base value of one good clip
        public BodyPlan plan;
        /// Special model variant for the mesh factory: "", "hammer", "manta", "whale", "barracuda", "lionfish", "tall", "garibaldi", "grouper"
        public string variant = "";
        public BehaviorType behavior;
        public int groupMin = 1, groupMax = 1;
        public float speed = 1f;                  // cruising speed in body lengths per second (clamped by spawner)
        public Color colorA = Color.gray;         // main body color
        public Color colorB = Color.white;        // secondary / belly / pattern color
        public Color colorC = Color.black;        // fins / accents / spots
        public PatternType pattern;
        public float bodyHeight = 0.35f;          // max body height / length
        public float bodyWidth = 0.18f;           // max body width / length
        public float finSize = 1f;                // relative fin size
        public bool glows;
        public string factDe, factEn;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Fact => Loc.Tr(factDe, factEn);
        public float SpawnWeight => SpeciesDatabase.RarityWeight(rarity);
        public bool IsDangerous => behavior == BehaviorType.Predator || behavior == BehaviorType.Ambush;
    }

    public static class SpeciesDatabase
    {
        static List<SpeciesDef> all;
        static Dictionary<string, SpeciesDef> byId;

        public static IReadOnlyList<SpeciesDef> All { get { Ensure(); return all; } }

        public static SpeciesDef Get(string id)
        {
            Ensure();
            return id != null && byId.TryGetValue(id, out var s) ? s : null;
        }

        public static List<SpeciesDef> BySite(string siteId)
        {
            Ensure();
            return all.FindAll(s => s.siteId == siteId);
        }

        public static float RarityWeight(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return 10f;
                case Rarity.Uncommon: return 5f;
                case Rarity.Rare: return 2f;
                case Rarity.Epic: return 0.6f;
                default: return 0.2f;
            }
        }

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        static void Ensure()
        {
            if (all != null) return;
            all = new List<SpeciesDef>
            {
                // ---------------- Hausriff (house_reef) 0-12 m ----------------
                new SpeciesDef { id = "clownfish", nameDe = "Clownfisch", nameEn = "Clownfish", latin = "Amphiprion ocellaris",
                    siteId = "house_reef", minDepth = 1, maxDepth = 12, minCm = 6, maxCm = 11, rarity = Rarity.Common, baseValue = 5,
                    plan = BodyPlan.Fish, behavior = BehaviorType.Schooling, groupMin = 3, groupMax = 6, speed = 1.4f,
                    colorA = C("#FF7A1A"), colorB = C("#FFFFFF"), colorC = C("#151515"), pattern = PatternType.VerticalBands,
                    bodyHeight = 0.42f, bodyWidth = 0.22f, finSize = 0.9f,
                    factDe = "Lebt in Seeanemonen und ist gegen deren Nesselgift immun.", factEn = "Lives inside sea anemones and is immune to their sting." },
                new SpeciesDef { id = "blue_tang", nameDe = "Paletten-Doktorfisch", nameEn = "Blue Tang", latin = "Paracanthurus hepatus",
                    siteId = "house_reef", minDepth = 2, maxDepth = 12, minCm = 15, maxCm = 30, rarity = Rarity.Common, baseValue = 8,
                    plan = BodyPlan.Fish, variant = "tall", behavior = BehaviorType.Schooling, groupMin = 4, groupMax = 10, speed = 1.6f,
                    colorA = C("#1E50D8"), colorB = C("#0E1430"), colorC = C("#FFD400"), pattern = PatternType.HorizontalStripes,
                    bodyHeight = 0.55f, bodyWidth = 0.16f, finSize = 1.0f,
                    factDe = "Hat am Schwanzstiel eine messerscharfe Klinge – daher der Name Doktorfisch.", factEn = "Has a scalpel-sharp spine at its tail – hence the name surgeonfish." },
                new SpeciesDef { id = "moon_jelly", nameDe = "Mondqualle", nameEn = "Moon Jelly", latin = "Aurelia aurita",
                    siteId = "house_reef", minDepth = 1, maxDepth = 12, minCm = 10, maxCm = 40, rarity = Rarity.Common, baseValue = 6,
                    plan = BodyPlan.Jelly, behavior = BehaviorType.Drifter, groupMin = 2, groupMax = 6, speed = 0.15f,
                    colorA = C("#CFE8FF"), colorB = C("#FFB3D9"), colorC = C("#E8F4FF"), pattern = PatternType.Translucent,
                    bodyHeight = 0.5f, bodyWidth = 1.0f, finSize = 1f, glows = true,
                    factDe = "Besteht zu 98 % aus Wasser und hat weder Herz noch Gehirn.", factEn = "Is 98% water and has neither heart nor brain." },
                new SpeciesDef { id = "parrotfish", nameDe = "Papageifisch", nameEn = "Parrotfish", latin = "Scarus frenatus",
                    siteId = "house_reef", minDepth = 2, maxDepth = 12, minCm = 30, maxCm = 90, rarity = Rarity.Common, baseValue = 15,
                    plan = BodyPlan.Fish, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 3, speed = 0.9f,
                    colorA = C("#2EC4A0"), colorB = C("#FF8FB1"), colorC = C("#F2D24B"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.38f, bodyWidth = 0.2f, finSize = 1.0f,
                    factDe = "Knabbert Korallen ab – ein Großteil des weißen Sandstrands ist Papageifisch-Kot.", factEn = "Chews coral – much of the white beach sand is parrotfish poop." },
                new SpeciesDef { id = "emperor_angelfish", nameDe = "Imperator-Kaiserfisch", nameEn = "Emperor Angelfish", latin = "Pomacanthus imperator",
                    siteId = "house_reef", minDepth = 3, maxDepth = 12, minCm = 20, maxCm = 40, rarity = Rarity.Uncommon, baseValue = 20,
                    plan = BodyPlan.Fish, variant = "tall", behavior = BehaviorType.Skittish, groupMin = 1, groupMax = 2, speed = 1.0f,
                    colorA = C("#1D4ED8"), colorB = C("#FFD23F"), colorC = C("#0B1E5B"), pattern = PatternType.HorizontalStripes,
                    bodyHeight = 0.68f, bodyWidth = 0.13f, finSize = 1.2f,
                    factDe = "Jungtiere sehen völlig anders aus: dunkelblau mit weißen Kreisen.", factEn = "Juveniles look completely different: dark blue with white circles." },
                new SpeciesDef { id = "lionfish", nameDe = "Rotfeuerfisch", nameEn = "Lionfish", latin = "Pterois volitans",
                    siteId = "house_reef", minDepth = 3, maxDepth = 12, minCm = 20, maxCm = 38, rarity = Rarity.Uncommon, baseValue = 25,
                    plan = BodyPlan.Fish, variant = "lionfish", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.35f,
                    colorA = C("#B3261E"), colorB = C("#F5E6D3"), colorC = C("#7A1A14"), pattern = PatternType.VerticalBands,
                    bodyHeight = 0.42f, bodyWidth = 0.2f, finSize = 1.9f,
                    factDe = "Seine Flossenstacheln sind giftig – nicht anfassen, nur filmen!", factEn = "Its fin spines are venomous – film it, don't touch it!" },
                new SpeciesDef { id = "bluespotted_ray", nameDe = "Blaupunktrochen", nameEn = "Bluespotted Ray", latin = "Taeniura lymma",
                    siteId = "house_reef", minDepth = 2, maxDepth = 12, minCm = 30, maxCm = 70, rarity = Rarity.Uncommon, baseValue = 30,
                    plan = BodyPlan.Ray, behavior = BehaviorType.BottomDweller, groupMin = 1, groupMax = 2, speed = 0.6f,
                    colorA = C("#8A6A3A"), colorB = C("#2EA8FF"), colorC = C("#D9C8A0"), pattern = PatternType.Spots,
                    bodyHeight = 0.12f, bodyWidth = 0.9f, finSize = 1f,
                    factDe = "Versteckt sich tagsüber unter Felsvorsprüngen im Sand.", factEn = "Hides under rocky ledges in the sand during the day." },
                new SpeciesDef { id = "seahorse", nameDe = "Seepferdchen", nameEn = "Seahorse", latin = "Hippocampus kuda",
                    siteId = "house_reef", minDepth = 1, maxDepth = 10, minCm = 5, maxCm = 20, rarity = Rarity.Rare, baseValue = 50,
                    plan = BodyPlan.Seahorse, behavior = BehaviorType.BottomDweller, groupMin = 1, groupMax = 2, speed = 0.1f,
                    colorA = C("#F2C14E"), colorB = C("#E08A2E"), colorC = C("#8A5A1E"), pattern = PatternType.Mottled,
                    bodyHeight = 0.5f, bodyWidth = 0.15f, finSize = 0.6f,
                    factDe = "Hier bekommen die Männchen die Babys.", factEn = "Here, the males give birth." },
                new SpeciesDef { id = "octopus", nameDe = "Gemeiner Krake", nameEn = "Common Octopus", latin = "Octopus vulgaris",
                    siteId = "house_reef", minDepth = 3, maxDepth = 12, minCm = 30, maxCm = 120, rarity = Rarity.Rare, baseValue = 70,
                    plan = BodyPlan.Octopus, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 1, speed = 0.5f,
                    colorA = C("#A0522D"), colorB = C("#E8C9A0"), colorC = C("#6B2E14"), pattern = PatternType.Mottled,
                    bodyHeight = 0.4f, bodyWidth = 0.4f, finSize = 1f,
                    factDe = "Hat drei Herzen, blaues Blut und kann in Sekunden die Farbe wechseln.", factEn = "Has three hearts, blue blood and changes color in seconds." },
                new SpeciesDef { id = "green_turtle", nameDe = "Suppenschildkröte „Gerald“", nameEn = "Green Sea Turtle “Gerald”", latin = "Chelonia mydas",
                    siteId = "house_reef", minDepth = 1, maxDepth = 12, minCm = 80, maxCm = 150, rarity = Rarity.Uncommon, baseValue = 60,
                    plan = BodyPlan.Turtle, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 1, speed = 0.45f,
                    colorA = C("#5B6B2E"), colorB = C("#9AAA66"), colorC = C("#3A4520"), pattern = PatternType.Mottled,
                    bodyHeight = 0.32f, bodyWidth = 0.75f, finSize = 1f,
                    factDe = "Gerald ist der Star des Hausriffs. Seine Fans wollen jeden Tag Updates.", factEn = "Gerald is the star of the house reef. His fans want daily updates." },

                // ---------------- Kelpwald (kelp_forest) 10-25 m ----------------
                new SpeciesDef { id = "garibaldi", nameDe = "Garibaldi", nameEn = "Garibaldi", latin = "Hypsypops rubicundus",
                    siteId = "kelp_forest", minDepth = 4, maxDepth = 25, minCm = 20, maxCm = 35, rarity = Rarity.Common, baseValue = 12,
                    plan = BodyPlan.Fish, variant = "garibaldi", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 3, speed = 0.9f,
                    colorA = C("#FF6A00"), colorB = C("#FF9A3C"), colorC = C("#E85A00"), pattern = PatternType.Solid,
                    bodyHeight = 0.5f, bodyWidth = 0.17f, finSize = 1.0f,
                    factDe = "Verteidigt sein Revier lautstark mit Klickgeräuschen.", factEn = "Defends its territory with loud clicking noises." },
                new SpeciesDef { id = "cuttlefish", nameDe = "Sepia", nameEn = "Cuttlefish", latin = "Sepia officinalis",
                    siteId = "kelp_forest", minDepth = 6, maxDepth = 25, minCm = 20, maxCm = 50, rarity = Rarity.Uncommon, baseValue = 40,
                    plan = BodyPlan.Cuttlefish, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 2, speed = 0.5f,
                    colorA = C("#7A5C3A"), colorB = C("#F0E6D2"), colorC = C("#4A3420"), pattern = PatternType.Mottled,
                    bodyHeight = 0.28f, bodyWidth = 0.32f, finSize = 1f,
                    factDe = "Lässt Farbwellen über den Körper laufen, um Beute zu hypnotisieren.", factEn = "Sends waves of color across its body to hypnotize prey." },
                new SpeciesDef { id = "sea_lion", nameDe = "Seelöwe „Sir Flapsalot“", nameEn = "Sea Lion “Sir Flapsalot”", latin = "Zalophus californianus",
                    siteId = "kelp_forest", minDepth = 1, maxDepth = 25, minCm = 150, maxCm = 250, rarity = Rarity.Uncommon, baseValue = 80,
                    plan = BodyPlan.SeaLion, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 2, speed = 1.3f,
                    colorA = C("#6B5440"), colorB = C("#9C8466"), colorC = C("#3E2F22"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.24f, bodyWidth = 0.26f, finSize = 1f,
                    factDe = "Liebt Kameras und platzt gern in fremde Videos.", factEn = "Loves cameras and photobombs other people's videos." },
                new SpeciesDef { id = "leopard_shark", nameDe = "Leopardenhai", nameEn = "Leopard Shark", latin = "Triakis semifasciata",
                    siteId = "kelp_forest", minDepth = 5, maxDepth = 25, minCm = 100, maxCm = 180, rarity = Rarity.Uncommon, baseValue = 90,
                    plan = BodyPlan.Shark, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 3, speed = 0.8f,
                    colorA = C("#B8AE9C"), colorB = C("#3A3530"), colorC = C("#E8E2D6"), pattern = PatternType.Spots,
                    bodyHeight = 0.13f, bodyWidth = 0.14f, finSize = 1f,
                    factDe = "Harmlos für Menschen – sieht aber in jedem Thumbnail gefährlich aus.", factEn = "Harmless to humans – but looks dangerous in every thumbnail." },
                new SpeciesDef { id = "giant_sea_bass", nameDe = "Riesen-Wrackbarsch", nameEn = "Giant Sea Bass", latin = "Stereolepis gigas",
                    siteId = "kelp_forest", minDepth = 10, maxDepth = 25, minCm = 100, maxCm = 250, rarity = Rarity.Rare, baseValue = 120,
                    plan = BodyPlan.Fish, variant = "grouper", behavior = BehaviorType.Curious, groupMin = 1, groupMax = 1, speed = 0.4f,
                    colorA = C("#3A4048"), colorB = C("#6C747C"), colorC = C("#20252B"), pattern = PatternType.Spots,
                    bodyHeight = 0.36f, bodyWidth = 0.22f, finSize = 1.0f,
                    factDe = "Kann über 75 Jahre alt und schwerer als ein Klavier werden.", factEn = "Can live over 75 years and weigh more than a piano." },

                // ---------------- Wrack (wreck) 22-40 m ----------------
                new SpeciesDef { id = "moray", nameDe = "Riesenmuräne", nameEn = "Giant Moray", latin = "Gymnothorax javanicus",
                    siteId = "wreck", minDepth = 15, maxDepth = 40, minCm = 60, maxCm = 250, rarity = Rarity.Uncommon, baseValue = 50,
                    plan = BodyPlan.Eel, behavior = BehaviorType.Ambush, groupMin = 1, groupMax = 2, speed = 0.4f,
                    colorA = C("#8B7D3A"), colorB = C("#2E2A14"), colorC = C("#C4B26A"), pattern = PatternType.Mottled,
                    bodyHeight = 0.08f, bodyWidth = 0.07f, finSize = 1f,
                    factDe = "Öffnet ständig das Maul – nicht aus Wut, sondern zum Atmen.", factEn = "Constantly opens its mouth – not out of anger, but to breathe." },
                new SpeciesDef { id = "grouper", nameDe = "Zackenbarsch", nameEn = "Grouper", latin = "Epinephelus lanceolatus",
                    siteId = "wreck", minDepth = 15, maxDepth = 40, minCm = 50, maxCm = 250, rarity = Rarity.Uncommon, baseValue = 70,
                    plan = BodyPlan.Fish, variant = "grouper", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 2, speed = 0.45f,
                    colorA = C("#7B5B3E"), colorB = C("#D8C29A"), colorC = C("#4A3422"), pattern = PatternType.Mottled,
                    bodyHeight = 0.36f, bodyWidth = 0.24f, finSize = 1.0f,
                    factDe = "Kann Taucher mit einem einzigen Schnapp-Sog fast einsaugen.", factEn = "Its suction gulp is strong enough to startle any diver." },
                new SpeciesDef { id = "barracuda", nameDe = "Großer Barrakuda", nameEn = "Great Barracuda", latin = "Sphyraena barracuda",
                    siteId = "wreck", minDepth = 10, maxDepth = 40, minCm = 60, maxCm = 180, rarity = Rarity.Uncommon, baseValue = 60,
                    plan = BodyPlan.Fish, variant = "barracuda", behavior = BehaviorType.Predator, groupMin = 1, groupMax = 4, speed = 1.6f,
                    colorA = C("#B8C4CC"), colorB = C("#3F4A52"), colorC = C("#7C8890"), pattern = PatternType.VerticalBands,
                    bodyHeight = 0.13f, bodyWidth = 0.09f, finSize = 0.8f,
                    factDe = "Schießt mit bis zu 58 km/h auf alles, was glänzt.", factEn = "Strikes at anything shiny at up to 58 km/h." },
                new SpeciesDef { id = "hammerhead", nameDe = "Bogenstirn-Hammerhai", nameEn = "Scalloped Hammerhead", latin = "Sphyrna lewini",
                    siteId = "wreck", minDepth = 20, maxDepth = 40, minCm = 200, maxCm = 450, rarity = Rarity.Rare, baseValue = 250,
                    plan = BodyPlan.Shark, variant = "hammer", behavior = BehaviorType.Predator, groupMin = 1, groupMax = 3, speed = 0.9f,
                    colorA = C("#6E7B85"), colorB = C("#F2F2F0"), colorC = C("#4E5A63"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.13f, bodyWidth = 0.13f, finSize = 1.2f,
                    factDe = "Der Hammerkopf funktioniert wie ein Metalldetektor für Beute.", factEn = "Its hammer head works like a metal detector for prey." },

                // ---------------- Blauwasser / Drop-off (blue_drop) 30-75 m ----------------
                new SpeciesDef { id = "manta", nameDe = "Riesenmanta", nameEn = "Giant Manta Ray", latin = "Mobula birostris",
                    siteId = "blue_drop", minDepth = 15, maxDepth = 58, minCm = 300, maxCm = 700, rarity = Rarity.Rare, baseValue = 300,
                    plan = BodyPlan.Ray, variant = "manta", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 2, speed = 0.35f,
                    colorA = C("#1A1F26"), colorB = C("#F0F0F0"), colorC = C("#2C333D"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.08f, bodyWidth = 1.6f, finSize = 1f,
                    factDe = "Hat im Verhältnis das größte Gehirn aller Fische.", factEn = "Has the largest brain relative to body size of any fish." },
                new SpeciesDef { id = "sunfish", nameDe = "Mondfisch", nameEn = "Ocean Sunfish", latin = "Mola mola",
                    siteId = "blue_drop", minDepth = 10, maxDepth = 58, minCm = 100, maxCm = 330, rarity = Rarity.Rare, baseValue = 200,
                    plan = BodyPlan.Sunfish, behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 1, speed = 0.2f,
                    colorA = C("#9AA5AE"), colorB = C("#D9DEE2"), colorC = C("#6F7A84"), pattern = PatternType.BellyGradient,
                    bodyHeight = 1.0f, bodyWidth = 0.18f, finSize = 1f,
                    factDe = "Der schwerste Knochenfisch der Welt – sieht aus wie ein Kopf ohne Körper.", factEn = "The world's heaviest bony fish – looks like a head without a body." },
                new SpeciesDef { id = "whale_shark", nameDe = "Walhai", nameEn = "Whale Shark", latin = "Rhincodon typus",
                    siteId = "blue_drop", minDepth = 5, maxDepth = 58, minCm = 400, maxCm = 1200, rarity = Rarity.Epic, baseValue = 600,
                    plan = BodyPlan.Shark, variant = "whale", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.25f,
                    colorA = C("#2F3F55"), colorB = C("#F2F2F2"), colorC = C("#1E2A3A"), pattern = PatternType.Spots,
                    bodyHeight = 0.16f, bodyWidth = 0.17f, finSize = 1f,
                    factDe = "Der größte Fisch der Welt frisst nur Plankton.", factEn = "The world's biggest fish eats only plankton." },

                // ---------------- Tiefseegraben (deep_trench) 80-160 m ----------------
                new SpeciesDef { id = "anglerfish", nameDe = "Anglerfisch „Lumen“", nameEn = "Anglerfish “Lumen”", latin = "Melanocetus johnsonii",
                    siteId = "deep_trench", minDepth = 70, maxDepth = 98, minCm = 10, maxCm = 100, rarity = Rarity.Rare, baseValue = 150,
                    plan = BodyPlan.Anglerfish, behavior = BehaviorType.Ambush, groupMin = 1, groupMax = 1, speed = 0.25f,
                    colorA = C("#1A1A22"), colorB = C("#FFE680"), colorC = C("#0C0C10"), pattern = PatternType.Solid,
                    bodyHeight = 0.62f, bodyWidth = 0.5f, finSize = 1f, glows = true,
                    factDe = "Lockt Beute mit einem leuchtenden Köder an – Licht ist ihr letzter Anblick.", factEn = "Lures prey with a glowing bait – the light is the last thing they see." },
                new SpeciesDef { id = "vampire_squid", nameDe = "Vampirtintenfisch", nameEn = "Vampire Squid", latin = "Vampyroteuthis infernalis",
                    siteId = "deep_trench", minDepth = 80, maxDepth = 98, minCm = 10, maxCm = 30, rarity = Rarity.Rare, baseValue = 120,
                    plan = BodyPlan.VampireSquid, behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 2, speed = 0.3f,
                    colorA = C("#5A0F1E"), colorB = C("#2A0610"), colorC = C("#4FB8FF"), pattern = PatternType.Solid,
                    bodyHeight = 0.5f, bodyWidth = 0.5f, finSize = 1f, glows = true,
                    factDe = "Stülpt sich bei Gefahr um wie ein Regenschirm im Sturm.", factEn = "Turns itself inside out like an umbrella in a storm when threatened." },
                new SpeciesDef { id = "giant_squid", nameDe = "Riesenkalmar", nameEn = "Giant Squid", latin = "Architeuthis dux",
                    siteId = "deep_trench", minDepth = 90, maxDepth = 98, minCm = 500, maxCm = 1300, rarity = Rarity.Legendary, baseValue = 1500,
                    plan = BodyPlan.Squid, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.3f,
                    colorA = C("#B5474A"), colorB = C("#E8A0A0"), colorC = C("#7A2A2E"), pattern = PatternType.Mottled,
                    bodyHeight = 0.12f, bodyWidth = 0.12f, finSize = 1f,
                    factDe = "Hat die größten Augen im Tierreich – so groß wie ein Fußball.", factEn = "Has the largest eyes in the animal kingdom – the size of a football." },
            };

            byId = new Dictionary<string, SpeciesDef>();
            foreach (var s in all) byId[s.id] = s;
        }
    }
}
