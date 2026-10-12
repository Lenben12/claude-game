using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    public class SpeciesDef
    {
        public string id;
        public string nameDe, nameEn, latin;
        /// Legacy home site (the round-1 spawner picks species per site with BySite). Sealog page = Page.
        public string siteId;
        /// Depth band in meters below sea level (L.1). Negative = above sea level (beach / land animals).
        public float minDepth, maxDepth;
        public float minCm, maxCm;                // body length range (wingspan for rays/birds, bell for jellies)
        public Rarity rarity;
        public int baseValue;                     // views/money base value of one good clip
        public BodyPlan plan;
        /// Special model variant for the mesh factory: "", "hammer", "manta", "whale", "barracuda", "lionfish", "tall", "garibaldi", "grouper",
        /// round 2: "ghost", "hermit", "flat", "slim", "thorns", "goblin", "gulper", "flying", "eagle", "tuna", "lantern", "cleaner" …
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

        // ---------------- round 2b: open-world spawning (L.1), Sealog pages (L.2), island animals (L.11)
        /// Zones the species lives in (Zones.* ids: site ids, "lagoon", "open_sea", "deep_sea", "island", "tide_pools", "beach").
        public string[] zones;
        /// Preferred depth (weight 1.0); −1 = middle of the band.
        public float prefDepth = -1f;
        /// At night (DayMask.Night) the whole band moves by this many meters (lanternfish −30 = 30 m up).
        public float nightShift;
        /// When the species may spawn at all.
        public DayMask dayMask = DayMask.All;
        /// Spawn weight multiplier at dusk and night ("night ×2").
        public float nightWeight = 1f;
        /// Gate: spawns only once this dive site is unlocked (null = always, e.g. house reef and lagoon species).
        public string homeSite;
        /// Sealog page (null = siteId). Pages: SealogPages.
        public string page;
        /// Extra story gate (StoryFlags), e.g. "fog_map" for the Fog Isle species.
        public string unlockFlag;
        /// Island land animal (L.11) – spawned by LandFauna from PropInfo.LandSpots, filmable on land.
        public bool land;
        /// Only one individual exists (Gerald, Sir Flapsalot, Big Bertha, Lumen, Grandpa Fossil, Truffle, Clapper).
        public bool unique;
        /// Preferred habitat anchor kind (L.3 PropInfo.Habitats): "anemone", "crevice", "cleaning", "overhang", "sand", "seafan", "seagrass" (null = free).
        public string habitat;
        /// > 0: only within this many meters of the home site / its landmark island (sea lion 200, humpback 300).
        public float homeRange;

        // ---------------- round 3 (GDD §19/§20, Testversion 5)
        /// Q23: decoration animal (every island land animal) – reacts and can be filmed, but is NOT in the Sealog
        /// (no entry, no "?", not counted). Set for all `land` species in SpeciesDatabase.Ensure.
        public bool deco;
        /// Q18: a food fish – a spear / speargun hit catches it (raw fish into the backpack). Everything that is neither
        /// food nor hostile is protected: a hit is refused and brings haters.
        public bool food;
        /// Q16: may attack divers when the threat director (FaunaThreat) picks it – circling, music, one charge.
        public bool hunter;
        /// The big prehistoric monster of the volcano sinkhole (FaunaVolcano spawns it, never the population).
        public bool monster;
        /// Q19: a meme / curiosity (SeaContent places it as a filmable prop or show actor, never spawned by the fauna).
        public bool meme;

        /// Counts for the Sealog (pages, totals, golden camera) – decoration animals do not.
        public bool InSealog => !deco;
        /// Hits on it are self-defence (sharks, barracuda, the monster …): it gets knocked back and flees.
        public bool Hostile => monster || hunter || (IsDangerous && !unique);

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Fact => Loc.Tr(factDe, factEn);
        public float SpawnWeight => SpeciesDatabase.RarityWeight(rarity);
        public bool IsDangerous => behavior == BehaviorType.Predator || behavior == BehaviorType.Ambush;
        /// Sealog page id.
        public string Page => string.IsNullOrEmpty(page) ? siteId : page;
        public float PrefDepth => prefDepth >= 0f ? prefDepth : (minDepth + maxDepth) * 0.5f;

        public bool HasZone(string zone)
        {
            if (zones == null || zone == null) return false;
            for (int i = 0; i < zones.Length; i++) if (string.Equals(zones[i], zone, StringComparison.Ordinal)) return true;
            return false;
        }

        public bool FitsTime(DayMask now) => (dayMask & now) != 0;

        /// Band edges at this time of day (night shift applied, never above the surface for water species).
        public float BandMin(bool night) => night && nightShift != 0f ? Mathf.Max(land ? minDepth : 0f, minDepth + nightShift) : minDepth;
        public float BandMax(bool night) => night && nightShift != 0f ? Mathf.Max(BandMin(true) + 1f, maxDepth + nightShift) : maxDepth;

        /// L.1 depth preference: 1.0 at the preferred depth, linear to 0.35 at the band edges, then down to 0 over a soft
        /// margin of 10 % of the band width outside it.
        public float DepthWeight(float depth, bool night)
        {
            float lo = BandMin(night), hi = BandMax(night);
            float pref = Mathf.Clamp(PrefDepth + (night ? nightShift : 0f), lo, hi);
            float margin = Mathf.Max(0.5f, (hi - lo) * 0.1f);
            if (depth < lo) return depth < lo - margin ? 0f : 0.35f * (1f - (lo - depth) / margin);
            if (depth > hi) return depth > hi + margin ? 0f : 0.35f * (1f - (depth - hi) / margin);
            float span = depth <= pref ? pref - lo : hi - pref;
            if (span <= 0.001f) return 1f;
            float t = Mathf.Abs(depth - pref) / span;
            return Mathf.Lerp(1f, 0.35f, t);
        }
    }

    public static class SpeciesDatabase
    {
        static List<SpeciesDef> all;
        static Dictionary<string, SpeciesDef> byId;

        static int sealogCount;

        /// Every species incl. the decoration animals (net replication indexes into this list – append only).
        public static IReadOnlyList<SpeciesDef> All { get { Ensure(); return all; } }

        /// Number of Sealog entries (round 3: decoration animals excluded; volcano + curiosities added) – the golden
        /// camera needs all of them.
        public static int Count { get { Ensure(); return sealogCount; } }

        public static SpeciesDef Get(string id)
        {
            Ensure();
            return id != null && byId.TryGetValue(id, out var s) ? s : null;
        }

        /// Legacy spawn pool of a dive site (the round-1 spawner): siteId matches, the species lives in that site's zone and
        /// is not a land animal (so beach crabs / tide-pool-only species never swim at the reef). Allocates – cache it.
        /// The Sealog uses ByPage (pages hold every species, e.g. house reef 16).
        public static List<SpeciesDef> BySite(string siteId)
        {
            Ensure();
            return all.FindAll(s => s.siteId == siteId && !s.land && s.HasZone(siteId));
        }

        /// Species of a Sealog page (SealogPages ids). Allocation free: fills `into`.
        public static void ByPage(string pageId, List<SpeciesDef> into)
        {
            Ensure();
            into.Clear();
            for (int i = 0; i < all.Count; i++) if (all[i].Page == pageId) into.Add(all[i]);
        }

        public static List<SpeciesDef> ByPage(string pageId)
        {
            var l = new List<SpeciesDef>();
            ByPage(pageId, l);
            return l;
        }

        /// Species living in a zone (L.1). Allocation free: fills `into` (build once, e.g. in Awake).
        public static void ByZone(string zone, List<SpeciesDef> into)
        {
            Ensure();
            into.Clear();
            for (int i = 0; i < all.Count; i++) if (all[i].HasZone(zone)) into.Add(all[i]);
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
        static string[] Z(params string[] z) => z;

        const string HR = Zones.HouseReef, LG = Zones.Lagoon, KF = Zones.Kelp, WR = Zones.Wreck, BD = Zones.BlueDrop, DT = Zones.Trench,
            OS = Zones.OpenSea, DS = Zones.DeepSea, FI = Zones.FogIsle, TP = Zones.TidePools, BE = Zones.Beach, IS = Zones.Island,
            VI = Zones.Volcano;

        /// Round 3 Sealog pages of the new entries (SealogPages ids).
        public const string VolcanoPage = "volcano_isle", MemePage = "memes";

        /// Q18 food fish (spear fishing → raw fish). Everything else that is not hostile is protected.
        static readonly string[] FoodIds = { "sardine", "yellowfin", "grouper", "lionfish", "flying_fish", "mackerel", "mahi_mahi", "snapper", "jack" };
        /// Q16 sharks / predators the threat director may send at a diver (circling + one charge).
        static readonly string[] HunterIds = { "hammerhead", "barracuda", "whitetip_shark", "tiger_shark", "sixgill_shark" };

        static void Ensure()
        {
            if (all != null) return;
            all = new List<SpeciesDef>
            {
                // ================= Sealog page „Hausriff“ (16) – gate: always =================
                new SpeciesDef { id = "clownfish", nameDe = "Clownfisch", nameEn = "Clownfish", latin = "Amphiprion ocellaris",
                    siteId = "house_reef", minDepth = 1, maxDepth = 12, minCm = 6, maxCm = 11, rarity = Rarity.Common, baseValue = 5,
                    plan = BodyPlan.Fish, behavior = BehaviorType.Schooling, groupMin = 3, groupMax = 6, speed = 1.4f,
                    colorA = C("#FF7A1A"), colorB = C("#FFFFFF"), colorC = C("#151515"), pattern = PatternType.VerticalBands,
                    bodyHeight = 0.42f, bodyWidth = 0.22f, finSize = 0.9f,
                    zones = Z(HR, LG, FI), prefDepth = 4, habitat = "anemone",
                    factDe = "Lebt in Seeanemonen und ist gegen deren Nesselgift immun.", factEn = "Lives inside sea anemones and is immune to their sting." },
                new SpeciesDef { id = "blue_tang", nameDe = "Paletten-Doktorfisch", nameEn = "Blue Tang", latin = "Paracanthurus hepatus",
                    siteId = "house_reef", minDepth = 1, maxDepth = 15, minCm = 15, maxCm = 30, rarity = Rarity.Common, baseValue = 8,
                    plan = BodyPlan.Fish, variant = "tall", behavior = BehaviorType.Schooling, groupMin = 4, groupMax = 10, speed = 1.6f,
                    colorA = C("#1E50D8"), colorB = C("#0E1430"), colorC = C("#FFD400"), pattern = PatternType.HorizontalStripes,
                    bodyHeight = 0.55f, bodyWidth = 0.16f, finSize = 1.0f,
                    zones = Z(HR, LG, FI), prefDepth = 6, dayMask = DayMask.Day,
                    factDe = "Hat am Schwanzstiel eine messerscharfe Klinge – daher der Name Doktorfisch.", factEn = "Has a scalpel-sharp spine at its tail – hence the name surgeonfish." },
                new SpeciesDef { id = "moon_jelly", nameDe = "Mondqualle", nameEn = "Moon Jelly", latin = "Aurelia aurita",
                    siteId = "house_reef", minDepth = 0, maxDepth = 25, minCm = 10, maxCm = 40, rarity = Rarity.Common, baseValue = 6,
                    plan = BodyPlan.Jelly, behavior = BehaviorType.Drifter, groupMin = 2, groupMax = 6, speed = 0.15f,
                    colorA = C("#CFE8FF"), colorB = C("#FFB3D9"), colorC = C("#E8F4FF"), pattern = PatternType.Translucent,
                    bodyHeight = 0.5f, bodyWidth = 1.0f, finSize = 1f, glows = true,
                    zones = Z(HR, LG, KF, OS, VI), prefDepth = 3, nightWeight = 2f,
                    factDe = "Besteht zu 98 % aus Wasser und hat weder Herz noch Gehirn.", factEn = "Is 98% water and has neither heart nor brain." },
                new SpeciesDef { id = "parrotfish", nameDe = "Papageifisch", nameEn = "Parrotfish", latin = "Scarus frenatus",
                    siteId = "house_reef", minDepth = 2, maxDepth = 18, minCm = 30, maxCm = 90, rarity = Rarity.Common, baseValue = 15,
                    plan = BodyPlan.Fish, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 3, speed = 0.9f,
                    colorA = C("#2EC4A0"), colorB = C("#FF8FB1"), colorC = C("#F2D24B"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.38f, bodyWidth = 0.2f, finSize = 1.0f,
                    zones = Z(HR, LG, FI), prefDepth = 7, dayMask = DayMask.Day,
                    factDe = "Knabbert Korallen ab – ein Großteil des weißen Sandstrands ist Papageifisch-Kot.", factEn = "Chews coral – much of the white beach sand is parrotfish poop." },
                new SpeciesDef { id = "emperor_angelfish", nameDe = "Imperator-Kaiserfisch", nameEn = "Emperor Angelfish", latin = "Pomacanthus imperator",
                    siteId = "house_reef", minDepth = 3, maxDepth = 25, minCm = 20, maxCm = 40, rarity = Rarity.Uncommon, baseValue = 20,
                    plan = BodyPlan.Fish, variant = "tall", behavior = BehaviorType.Skittish, groupMin = 1, groupMax = 2, speed = 1.0f,
                    colorA = C("#1D4ED8"), colorB = C("#FFD23F"), colorC = C("#0B1E5B"), pattern = PatternType.HorizontalStripes,
                    bodyHeight = 0.68f, bodyWidth = 0.13f, finSize = 1.2f,
                    zones = Z(HR, WR, FI), prefDepth = 10, dayMask = DayMask.Day,
                    factDe = "Jungtiere sehen völlig anders aus: dunkelblau mit weißen Kreisen.", factEn = "Juveniles look completely different: dark blue with white circles." },
                new SpeciesDef { id = "lionfish", nameDe = "Rotfeuerfisch", nameEn = "Lionfish", latin = "Pterois volitans",
                    siteId = "house_reef", minDepth = 3, maxDepth = 30, minCm = 20, maxCm = 38, rarity = Rarity.Uncommon, baseValue = 25,
                    plan = BodyPlan.Fish, variant = "lionfish", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.35f,
                    colorA = C("#B3261E"), colorB = C("#F5E6D3"), colorC = C("#7A1A14"), pattern = PatternType.VerticalBands,
                    bodyHeight = 0.42f, bodyWidth = 0.2f, finSize = 1.9f,
                    zones = Z(HR, WR), prefDepth = 12, nightWeight = 1.5f,
                    factDe = "Seine Flossenstacheln sind giftig – nicht anfassen, nur filmen!", factEn = "Its fin spines are venomous – film it, don't touch it!" },
                new SpeciesDef { id = "bluespotted_ray", nameDe = "Blaupunktrochen", nameEn = "Bluespotted Ray", latin = "Taeniura lymma",
                    siteId = "house_reef", minDepth = 1, maxDepth = 15, minCm = 30, maxCm = 70, rarity = Rarity.Uncommon, baseValue = 30,
                    plan = BodyPlan.Ray, behavior = BehaviorType.BottomDweller, groupMin = 1, groupMax = 2, speed = 0.6f,
                    colorA = C("#8A6A3A"), colorB = C("#2EA8FF"), colorC = C("#D9C8A0"), pattern = PatternType.Spots,
                    bodyHeight = 0.12f, bodyWidth = 0.9f, finSize = 1f,
                    zones = Z(HR, LG), prefDepth = 5, dayMask = DayMask.Day, habitat = "sand",
                    factDe = "Versteckt sich tagsüber unter Felsvorsprüngen im Sand.", factEn = "Hides under rocky ledges in the sand during the day." },
                new SpeciesDef { id = "seahorse", nameDe = "Seepferdchen", nameEn = "Seahorse", latin = "Hippocampus kuda",
                    siteId = "house_reef", minDepth = 1, maxDepth = 10, minCm = 5, maxCm = 20, rarity = Rarity.Rare, baseValue = 50,
                    plan = BodyPlan.Seahorse, behavior = BehaviorType.BottomDweller, groupMin = 1, groupMax = 2, speed = 0.1f,
                    colorA = C("#F2C14E"), colorB = C("#E08A2E"), colorC = C("#8A5A1E"), pattern = PatternType.Mottled,
                    bodyHeight = 0.5f, bodyWidth = 0.15f, finSize = 0.6f,
                    zones = Z(HR, LG), prefDepth = 4, habitat = "seagrass",
                    factDe = "Hier bekommen die Männchen die Babys.", factEn = "Here, the males give birth." },
                new SpeciesDef { id = "octopus", nameDe = "Gemeiner Krake", nameEn = "Common Octopus", latin = "Octopus vulgaris",
                    siteId = "house_reef", minDepth = 2, maxDepth = 30, minCm = 30, maxCm = 120, rarity = Rarity.Rare, baseValue = 70,
                    plan = BodyPlan.Octopus, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 1, speed = 0.5f,
                    colorA = C("#A0522D"), colorB = C("#E8C9A0"), colorC = C("#6B2E14"), pattern = PatternType.Mottled,
                    bodyHeight = 0.4f, bodyWidth = 0.4f, finSize = 1f,
                    zones = Z(HR, KF, WR), prefDepth = 8, nightWeight = 1.5f, habitat = "crevice",
                    factDe = "Hat drei Herzen, blaues Blut und kann in Sekunden die Farbe wechseln.", factEn = "Has three hearts, blue blood and changes color in seconds." },
                new SpeciesDef { id = "green_turtle", nameDe = "Suppenschildkröte „Gerald“", nameEn = "Green Sea Turtle “Gerald”", latin = "Chelonia mydas",
                    siteId = "house_reef", minDepth = 0, maxDepth = 15, minCm = 80, maxCm = 150, rarity = Rarity.Uncommon, baseValue = 60,
                    plan = BodyPlan.Turtle, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 1, speed = 0.45f,
                    colorA = C("#5B6B2E"), colorB = C("#9AAA66"), colorC = C("#3A4520"), pattern = PatternType.Mottled,
                    bodyHeight = 0.32f, bodyWidth = 0.75f, finSize = 1f,
                    zones = Z(HR, LG), prefDepth = 5, dayMask = DayMask.Day, unique = true,
                    factDe = "Gerald ist der Star des Hausriffs. Seine Fans wollen jeden Tag Updates.", factEn = "Gerald is the star of the house reef. His fans want daily updates." },
                // ---- G.1 additions (house reef page)
                new SpeciesDef { id = "ghost_crab", nameDe = "Geisterkrabbe", nameEn = "Ghost Crab", latin = "Ocypode ceratophthalmus",
                    siteId = "house_reef", minDepth = -1.5f, maxDepth = 0, minCm = 3, maxCm = 9, rarity = Rarity.Common, baseValue = 8,
                    plan = BodyPlan.Crab, variant = "ghost", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 3, speed = 1.2f,
                    colorA = C("#E8D9B4"), colorB = C("#F4EFE6"), colorC = C("#2B2533"), pattern = PatternType.Solid,
                    bodyHeight = 0.35f, bodyWidth = 1.1f, finSize = 1f,
                    zones = Z(BE), prefDepth = -0.5f, dayMask = DayMask.Dusk | DayMask.Night,
                    factDe = "Rennt bis zu 4 m pro Sekunde – seitwärts. Und knurrt dabei mit dem Magen.", factEn = "Runs up to 4 m per second – sideways. And growls with its stomach." },
                new SpeciesDef { id = "hermit_crab", nameDe = "Einsiedlerkrebs", nameEn = "Hermit Crab", latin = "Dardanus megistos",
                    siteId = "house_reef", minDepth = 0, maxDepth = 4, minCm = 4, maxCm = 14, rarity = Rarity.Common, baseValue = 10,
                    plan = BodyPlan.Crab, variant = "hermit", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 4, speed = 0.3f,
                    colorA = C("#D9455A"), colorB = C("#E2C890"), colorC = C("#F2C94C"), pattern = PatternType.Spots,
                    bodyHeight = 0.8f, bodyWidth = 0.9f, finSize = 1f,
                    zones = Z(TP, HR, LG), prefDepth = 1,
                    factDe = "Zieht um, wenn das Haus zu klein wird – manchmal stehen sie in einer Warteschlange nach Größe.", factEn = "Moves house when it gets too small – sometimes they queue up by size." },
                new SpeciesDef { id = "sally_crab", nameDe = "Rote Klippenkrabbe", nameEn = "Sally Lightfoot Crab", latin = "Grapsus grapsus",
                    siteId = "house_reef", minDepth = 0, maxDepth = 2, minCm = 5, maxCm = 9, rarity = Rarity.Uncommon, baseValue = 18,
                    plan = BodyPlan.Crab, variant = "flat", behavior = BehaviorType.Skittish, groupMin = 1, groupMax = 3, speed = 1.6f,
                    colorA = C("#E2552E"), colorB = C("#F28C4A"), colorC = C("#3FA9B5"), pattern = PatternType.Mottled,
                    bodyHeight = 0.25f, bodyWidth = 1.2f, finSize = 1f,
                    zones = Z(TP), prefDepth = 0.5f, dayMask = DayMask.Day,
                    factDe = "Springt von Fels zu Fels und putzt Meerechsen die Zecken weg.", factEn = "Hops from rock to rock and picks ticks off marine iguanas." },
                new SpeciesDef { id = "blue_starfish", nameDe = "Blauer Seestern", nameEn = "Blue Sea Star", latin = "Linckia laevigata",
                    siteId = "house_reef", minDepth = 1, maxDepth = 15, minCm = 15, maxCm = 30, rarity = Rarity.Common, baseValue = 7,
                    plan = BodyPlan.Starfish, variant = "slim", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 2, speed = 0.02f,
                    colorA = C("#2F6FD6"), colorB = C("#4F8FE6"), colorC = C("#1E3A8A"), pattern = PatternType.Solid,
                    bodyHeight = 0.08f, bodyWidth = 1.0f, finSize = 1f,
                    zones = Z(HR, LG, FI), prefDepth = 5, habitat = "sand",
                    factDe = "Kann einen verlorenen Arm nachwachsen lassen – manchmal wächst aus dem Arm ein ganzer neuer Stern.", factEn = "Can regrow a lost arm – sometimes the arm grows into a whole new star." },
                new SpeciesDef { id = "crown_star", nameDe = "Dornenkronen-Seestern", nameEn = "Crown-of-Thorns Starfish", latin = "Acanthaster planci",
                    siteId = "house_reef", minDepth = 3, maxDepth = 15, minCm = 25, maxCm = 40, rarity = Rarity.Uncommon, baseValue = 22,
                    plan = BodyPlan.Starfish, variant = "thorns", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 1, speed = 0.02f,
                    colorA = C("#7A3E8E"), colorB = C("#D9455A"), colorC = C("#F2C94C"), pattern = PatternType.Spots,
                    bodyHeight = 0.15f, bodyWidth = 1.0f, finSize = 1f,
                    zones = Z(HR, FI), prefDepth = 8,
                    factDe = "Frisst Korallen und hat giftige Stacheln. Filmen ja, kuscheln nein.", factEn = "Eats coral and has venomous spines. Film yes, cuddle no." },
                // ---- L.1 addition (house reef page)
                new SpeciesDef { id = "cleaner_wrasse", nameDe = "Putzerlippfisch", nameEn = "Cleaner Wrasse", latin = "Labroides dimidiatus",
                    siteId = "house_reef", minDepth = 1, maxDepth = 30, minCm = 6, maxCm = 14, rarity = Rarity.Common, baseValue = 6,
                    plan = BodyPlan.Fish, variant = "cleaner", behavior = BehaviorType.Schooling, groupMin = 2, groupMax = 6, speed = 1.3f,
                    colorA = C("#2F6FD6"), colorB = C("#F4EFE6"), colorC = C("#1E1B2B"), pattern = PatternType.HorizontalStripes,
                    bodyHeight = 0.24f, bodyWidth = 0.14f, finSize = 0.9f,
                    zones = Z(HR, LG, KF, WR, FI), prefDepth = 8, dayMask = DayMask.Day, habitat = "cleaning",
                    factDe = "Betreibt eine Waschanlage: Große Fische stehen Schlange.", factEn = "Runs a car wash: big fish queue up." },

                // ================= Sealog page „Kelpwald“ (5) – gate: kelp_forest =================
                new SpeciesDef { id = "garibaldi", nameDe = "Garibaldi", nameEn = "Garibaldi", latin = "Hypsypops rubicundus",
                    siteId = "kelp_forest", minDepth = 4, maxDepth = 25, minCm = 20, maxCm = 35, rarity = Rarity.Common, baseValue = 12,
                    plan = BodyPlan.Fish, variant = "garibaldi", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 3, speed = 0.9f,
                    colorA = C("#FF6A00"), colorB = C("#FF9A3C"), colorC = C("#E85A00"), pattern = PatternType.Solid,
                    bodyHeight = 0.5f, bodyWidth = 0.17f, finSize = 1.0f,
                    zones = Z(KF), prefDepth = 10, dayMask = DayMask.Day, homeSite = "kelp_forest",
                    factDe = "Verteidigt sein Revier lautstark mit Klickgeräuschen.", factEn = "Defends its territory with loud clicking noises." },
                new SpeciesDef { id = "cuttlefish", nameDe = "Sepia", nameEn = "Cuttlefish", latin = "Sepia officinalis",
                    siteId = "kelp_forest", minDepth = 6, maxDepth = 30, minCm = 20, maxCm = 50, rarity = Rarity.Uncommon, baseValue = 40,
                    plan = BodyPlan.Cuttlefish, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 2, speed = 0.5f,
                    colorA = C("#7A5C3A"), colorB = C("#F0E6D2"), colorC = C("#4A3420"), pattern = PatternType.Mottled,
                    bodyHeight = 0.28f, bodyWidth = 0.32f, finSize = 1f,
                    zones = Z(KF, HR), prefDepth = 14, homeSite = "kelp_forest",
                    factDe = "Lässt Farbwellen über den Körper laufen, um Beute zu hypnotisieren.", factEn = "Sends waves of color across its body to hypnotize prey." },
                new SpeciesDef { id = "sea_lion", nameDe = "Seelöwe „Sir Flapsalot“", nameEn = "Sea Lion “Sir Flapsalot”", latin = "Zalophus californianus",
                    siteId = "kelp_forest", minDepth = 0, maxDepth = 25, minCm = 150, maxCm = 250, rarity = Rarity.Uncommon, baseValue = 80,
                    plan = BodyPlan.SeaLion, behavior = BehaviorType.Curious, groupMin = 1, groupMax = 2, speed = 1.3f,
                    colorA = C("#6B5440"), colorB = C("#9C8466"), colorC = C("#3E2F22"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.24f, bodyWidth = 0.26f, finSize = 1f,
                    zones = Z(KF, OS), prefDepth = 6, dayMask = DayMask.Day, homeSite = "kelp_forest", unique = true, homeRange = 200f,
                    factDe = "Liebt Kameras und platzt gern in fremde Videos.", factEn = "Loves cameras and photobombs other people's videos." },
                new SpeciesDef { id = "leopard_shark", nameDe = "Leopardenhai", nameEn = "Leopard Shark", latin = "Triakis semifasciata",
                    siteId = "kelp_forest", minDepth = 5, maxDepth = 25, minCm = 100, maxCm = 180, rarity = Rarity.Uncommon, baseValue = 90,
                    plan = BodyPlan.Shark, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 3, speed = 0.8f,
                    colorA = C("#B8AE9C"), colorB = C("#3A3530"), colorC = C("#E8E2D6"), pattern = PatternType.Spots,
                    bodyHeight = 0.13f, bodyWidth = 0.14f, finSize = 1f,
                    zones = Z(KF, LG), prefDepth = 15, homeSite = "kelp_forest",
                    factDe = "Harmlos für Menschen – sieht aber in jedem Thumbnail gefährlich aus.", factEn = "Harmless to humans – but looks dangerous in every thumbnail." },
                new SpeciesDef { id = "giant_sea_bass", nameDe = "Riesen-Wrackbarsch", nameEn = "Giant Sea Bass", latin = "Stereolepis gigas",
                    siteId = "kelp_forest", minDepth = 10, maxDepth = 35, minCm = 100, maxCm = 250, rarity = Rarity.Rare, baseValue = 120,
                    plan = BodyPlan.Fish, variant = "grouper", behavior = BehaviorType.Curious, groupMin = 1, groupMax = 1, speed = 0.4f,
                    colorA = C("#3A4048"), colorB = C("#6C747C"), colorC = C("#20252B"), pattern = PatternType.Spots,
                    bodyHeight = 0.36f, bodyWidth = 0.22f, finSize = 1.0f,
                    zones = Z(KF, WR), prefDepth = 20, homeSite = "kelp_forest",
                    factDe = "Kann über 75 Jahre alt und schwerer als ein Klavier werden.", factEn = "Can live over 75 years and weigh more than a piano." },

                // ================= Sealog page „Wrack“ (4) – gate: wreck =================
                new SpeciesDef { id = "moray", nameDe = "Riesenmuräne", nameEn = "Giant Moray", latin = "Gymnothorax javanicus",
                    siteId = "wreck", minDepth = 10, maxDepth = 40, minCm = 60, maxCm = 250, rarity = Rarity.Uncommon, baseValue = 50,
                    plan = BodyPlan.Eel, behavior = BehaviorType.Ambush, groupMin = 1, groupMax = 2, speed = 0.4f,
                    colorA = C("#8B7D3A"), colorB = C("#2E2A14"), colorC = C("#C4B26A"), pattern = PatternType.Mottled,
                    bodyHeight = 0.08f, bodyWidth = 0.07f, finSize = 1f,
                    zones = Z(WR, HR), prefDepth = 25, homeSite = "wreck", habitat = "crevice",
                    factDe = "Öffnet ständig das Maul – nicht aus Wut, sondern zum Atmen.", factEn = "Constantly opens its mouth – not out of anger, but to breathe." },
                new SpeciesDef { id = "grouper", nameDe = "Zackenbarsch", nameEn = "Grouper", latin = "Epinephelus lanceolatus",
                    siteId = "wreck", minDepth = 15, maxDepth = 45, minCm = 50, maxCm = 250, rarity = Rarity.Uncommon, baseValue = 70,
                    plan = BodyPlan.Fish, variant = "grouper", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 2, speed = 0.45f,
                    colorA = C("#7B5B3E"), colorB = C("#D8C29A"), colorC = C("#4A3422"), pattern = PatternType.Mottled,
                    bodyHeight = 0.36f, bodyWidth = 0.24f, finSize = 1.0f,
                    zones = Z(WR, BD), prefDepth = 28, homeSite = "wreck",
                    factDe = "Kann Taucher mit einem einzigen Schnapp-Sog fast einsaugen.", factEn = "Its suction gulp is strong enough to startle any diver." },
                new SpeciesDef { id = "barracuda", nameDe = "Großer Barrakuda", nameEn = "Great Barracuda", latin = "Sphyraena barracuda",
                    siteId = "wreck", minDepth = 5, maxDepth = 40, minCm = 60, maxCm = 180, rarity = Rarity.Uncommon, baseValue = 60,
                    plan = BodyPlan.Fish, variant = "barracuda", behavior = BehaviorType.Predator, groupMin = 1, groupMax = 4, speed = 1.6f,
                    colorA = C("#B8C4CC"), colorB = C("#3F4A52"), colorC = C("#7C8890"), pattern = PatternType.VerticalBands,
                    bodyHeight = 0.13f, bodyWidth = 0.09f, finSize = 0.8f,
                    zones = Z(WR, OS, BD, VI), prefDepth = 15, homeSite = "wreck",
                    factDe = "Schießt mit bis zu 58 km/h auf alles, was glänzt.", factEn = "Strikes at anything shiny at up to 58 km/h." },
                new SpeciesDef { id = "hammerhead", nameDe = "Bogenstirn-Hammerhai", nameEn = "Scalloped Hammerhead", latin = "Sphyrna lewini",
                    siteId = "wreck", minDepth = 20, maxDepth = 60, minCm = 200, maxCm = 450, rarity = Rarity.Rare, baseValue = 250,
                    plan = BodyPlan.Shark, variant = "hammer", behavior = BehaviorType.Predator, groupMin = 1, groupMax = 3, speed = 0.9f,
                    colorA = C("#6E7B85"), colorB = C("#F2F2F0"), colorC = C("#4E5A63"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.13f, bodyWidth = 0.13f, finSize = 1.2f,
                    zones = Z(WR, BD, OS, VI), prefDepth = 35, homeSite = "wreck",
                    factDe = "Der Hammerkopf funktioniert wie ein Metalldetektor für Beute.", factEn = "Its hammer head works like a metal detector for prey." },

                // ================= Sealog page „Blauwasser“ (5) – gate: blue_drop (dolphin: kelp) =================
                new SpeciesDef { id = "manta", nameDe = "Riesenmanta", nameEn = "Giant Manta Ray", latin = "Mobula birostris",
                    siteId = "blue_drop", minDepth = 5, maxDepth = 58, minCm = 300, maxCm = 700, rarity = Rarity.Rare, baseValue = 300,
                    plan = BodyPlan.Ray, variant = "manta", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 2, speed = 0.35f,
                    colorA = C("#1A1F26"), colorB = C("#F0F0F0"), colorC = C("#2C333D"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.08f, bodyWidth = 1.6f, finSize = 1f,
                    zones = Z(BD, OS), prefDepth = 15, dayMask = DayMask.Day, homeSite = "blue_drop",
                    factDe = "Hat im Verhältnis das größte Gehirn aller Fische.", factEn = "Has the largest brain relative to body size of any fish." },
                new SpeciesDef { id = "sunfish", nameDe = "Mondfisch", nameEn = "Ocean Sunfish", latin = "Mola mola",
                    siteId = "blue_drop", minDepth = 5, maxDepth = 60, minCm = 100, maxCm = 330, rarity = Rarity.Rare, baseValue = 200,
                    plan = BodyPlan.Sunfish, behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 1, speed = 0.2f,
                    colorA = C("#9AA5AE"), colorB = C("#D9DEE2"), colorC = C("#6F7A84"), pattern = PatternType.BellyGradient,
                    bodyHeight = 1.0f, bodyWidth = 0.18f, finSize = 1f,
                    zones = Z(BD, OS), prefDepth = 20, dayMask = DayMask.Day, homeSite = "blue_drop",
                    factDe = "Der schwerste Knochenfisch der Welt – sieht aus wie ein Kopf ohne Körper.", factEn = "The world's heaviest bony fish – looks like a head without a body." },
                new SpeciesDef { id = "whale_shark", nameDe = "Walhai", nameEn = "Whale Shark", latin = "Rhincodon typus",
                    siteId = "blue_drop", minDepth = 0, maxDepth = 58, minCm = 400, maxCm = 1200, rarity = Rarity.Epic, baseValue = 600,
                    plan = BodyPlan.Shark, variant = "whale", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.25f,
                    colorA = C("#2F3F55"), colorB = C("#F2F2F2"), colorC = C("#1E2A3A"), pattern = PatternType.Spots,
                    bodyHeight = 0.16f, bodyWidth = 0.17f, finSize = 1f,
                    zones = Z(BD, OS), prefDepth = 10, dayMask = DayMask.Day, homeSite = "blue_drop",
                    factDe = "Der größte Fisch der Welt frisst nur Plankton.", factEn = "The world's biggest fish eats only plankton." },
                new SpeciesDef { id = "dolphin", nameDe = "Großer Tümmler", nameEn = "Bottlenose Dolphin", latin = "Tursiops truncatus",
                    siteId = "blue_drop", minDepth = 0, maxDepth = 25, minCm = 200, maxCm = 400, rarity = Rarity.Uncommon, baseValue = 110,
                    plan = BodyPlan.Dolphin, behavior = BehaviorType.Playful, groupMin = 3, groupMax = 6, speed = 2.2f,
                    colorA = C("#7D8A96"), colorB = C("#D9DEE2"), colorC = C("#4E5A63"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.22f, bodyWidth = 0.22f, finSize = 1.1f,
                    zones = Z(OS, BD, LG), prefDepth = 3, dayMask = DayMask.Day, homeSite = "kelp_forest",
                    factDe = "Hat für jeden Freund einen eigenen Pfiff – eine Art Namen.", factEn = "Has a signature whistle for every friend – a kind of name." },
                new SpeciesDef { id = "humpback", nameDe = "Buckelwal „Big Bertha“", nameEn = "Humpback Whale “Big Bertha”", latin = "Megaptera novaeangliae",
                    siteId = "blue_drop", minDepth = 0, maxDepth = 40, minCm = 1100, maxCm = 1600, rarity = Rarity.Epic, baseValue = 900,
                    plan = BodyPlan.Whale, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.12f,
                    colorA = C("#2B3440"), colorB = C("#E8E4DC"), colorC = C("#1E2430"), pattern = PatternType.Mottled,
                    bodyHeight = 0.2f, bodyWidth = 0.25f, finSize = 1.4f,
                    zones = Z(BD, OS), prefDepth = 10, homeSite = "blue_drop", unique = true, homeRange = 300f,
                    factDe = "Singt Lieder, die stundenlang dauern. Big Bertha singt hauptsächlich falsch.", factEn = "Sings songs that last for hours. Big Bertha mostly sings off-key." },

                // ================= Sealog page „Tiefseegraben“ (5) – gate: deep_trench =================
                new SpeciesDef { id = "anglerfish", nameDe = "Anglerfisch „Lumen“", nameEn = "Anglerfish “Lumen”", latin = "Melanocetus johnsonii",
                    siteId = "deep_trench", minDepth = 60, maxDepth = 98, minCm = 10, maxCm = 100, rarity = Rarity.Rare, baseValue = 150,
                    plan = BodyPlan.Anglerfish, behavior = BehaviorType.Ambush, groupMin = 1, groupMax = 1, speed = 0.25f,
                    colorA = C("#1A1A22"), colorB = C("#FFE680"), colorC = C("#0C0C10"), pattern = PatternType.Solid,
                    bodyHeight = 0.62f, bodyWidth = 0.5f, finSize = 1f, glows = true,
                    zones = Z(DT, DS), prefDepth = 85, homeSite = "deep_trench", unique = true,
                    factDe = "Lockt Beute mit einem leuchtenden Köder an – Licht ist ihr letzter Anblick.", factEn = "Lures prey with a glowing bait – the light is the last thing they see." },
                new SpeciesDef { id = "vampire_squid", nameDe = "Vampirtintenfisch", nameEn = "Vampire Squid", latin = "Vampyroteuthis infernalis",
                    siteId = "deep_trench", minDepth = 70, maxDepth = 98, minCm = 10, maxCm = 30, rarity = Rarity.Rare, baseValue = 120,
                    plan = BodyPlan.VampireSquid, behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 2, speed = 0.3f,
                    colorA = C("#5A0F1E"), colorB = C("#2A0610"), colorC = C("#4FB8FF"), pattern = PatternType.Solid,
                    bodyHeight = 0.5f, bodyWidth = 0.5f, finSize = 1f, glows = true,
                    zones = Z(DT, DS), prefDepth = 90, homeSite = "deep_trench",
                    factDe = "Stülpt sich bei Gefahr um wie ein Regenschirm im Sturm.", factEn = "Turns itself inside out like an umbrella in a storm when threatened." },
                new SpeciesDef { id = "giant_squid", nameDe = "Riesenkalmar", nameEn = "Giant Squid", latin = "Architeuthis dux",
                    siteId = "deep_trench", minDepth = 88, maxDepth = 98, minCm = 500, maxCm = 1300, rarity = Rarity.Legendary, baseValue = 1500,
                    plan = BodyPlan.Squid, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.3f,
                    colorA = C("#B5474A"), colorB = C("#E8A0A0"), colorC = C("#7A2A2E"), pattern = PatternType.Mottled,
                    bodyHeight = 0.12f, bodyWidth = 0.12f, finSize = 1f,
                    zones = Z(DT), prefDepth = 95, homeSite = "deep_trench",
                    factDe = "Hat die größten Augen im Tierreich – so groß wie ein Fußball.", factEn = "Has the largest eyes in the animal kingdom – the size of a football." },
                new SpeciesDef { id = "goblin_shark", nameDe = "Koboldhai", nameEn = "Goblin Shark", latin = "Mitsukurina owstoni",
                    siteId = "deep_trench", minDepth = 80, maxDepth = 98, minCm = 250, maxCm = 620, rarity = Rarity.Rare, baseValue = 400,
                    plan = BodyPlan.Shark, variant = "goblin", behavior = BehaviorType.Ambush, groupMin = 1, groupMax = 1, speed = 0.5f,
                    colorA = C("#D99A9A"), colorB = C("#F0C4C0"), colorC = C("#8A4A52"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.13f, bodyWidth = 0.12f, finSize = 1f, glows = true,
                    zones = Z(DT), prefDepth = 90, homeSite = "deep_trench",
                    factDe = "Schießt seinen ganzen Kiefer nach vorne. Ein lebendes Fossil mit Schreckfaktor.", factEn = "Catapults its whole jaw forward. A living fossil with a fright factor." },
                new SpeciesDef { id = "gulper_eel", nameDe = "Pelikanaal", nameEn = "Gulper Eel", latin = "Eurypharynx pelecanoides",
                    siteId = "deep_trench", minDepth = 75, maxDepth = 98, minCm = 40, maxCm = 100, rarity = Rarity.Rare, baseValue = 260,
                    plan = BodyPlan.Eel, variant = "gulper", behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 1, speed = 0.3f,
                    colorA = C("#14121E"), colorB = C("#2E2A40"), colorC = C("#FF6EC7"), pattern = PatternType.Solid,
                    bodyHeight = 0.08f, bodyWidth = 0.07f, finSize = 1f, glows = true,
                    zones = Z(DT, DS), prefDepth = 88, homeSite = "deep_trench",
                    factDe = "Das Maul ist größer als der ganze restliche Fisch. Der Schwanz leuchtet rosa.", factEn = "Its mouth is bigger than the rest of the fish. Its tail glows pink." },

                // ================= Sealog page „Hochsee“ (5, L.1) =================
                new SpeciesDef { id = "sardine", nameDe = "Sardine", nameEn = "Sardine", latin = "Sardinops sagax",
                    siteId = "open_sea", minDepth = 0, maxDepth = 30, minCm = 10, maxCm = 25, rarity = Rarity.Common, baseValue = 4,
                    plan = BodyPlan.Fish, variant = "slender", behavior = BehaviorType.Schooling, groupMin = 60, groupMax = 150, speed = 2.0f,
                    colorA = C("#9FB4C4"), colorB = C("#F4EFE6"), colorC = C("#3F5A72"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.2f, bodyWidth = 0.11f, finSize = 0.8f,
                    zones = Z(OS, LG, KF, VI), prefDepth = 8, dayMask = DayMask.Day,
                    factDe = "Schwimmt im Schwarm, damit Räuber nicht wissen, wen sie nehmen sollen.", factEn = "Swims in a school so predators can't decide whom to pick." },
                new SpeciesDef { id = "flying_fish", nameDe = "Fliegender Fisch", nameEn = "Flying Fish", latin = "Exocoetus volitans",
                    siteId = "open_sea", minDepth = 0, maxDepth = 2, minCm = 15, maxCm = 35, rarity = Rarity.Common, baseValue = 10,
                    plan = BodyPlan.Fish, variant = "flying", behavior = BehaviorType.Skittish, groupMin = 2, groupMax = 6, speed = 2.4f,
                    colorA = C("#2F6FD6"), colorB = C("#D9DEE2"), colorC = C("#7FD3E0"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.18f, bodyWidth = 0.12f, finSize = 2.2f,
                    zones = Z(OS), prefDepth = 0.5f, dayMask = DayMask.Day,
                    factDe = "Gleitet bis zu 50 Meter weit über das Wasser.", factEn = "Glides up to 50 metres above the water." },
                new SpeciesDef { id = "spotted_eagle_ray", nameDe = "Gefleckter Adlerrochen", nameEn = "Spotted Eagle Ray", latin = "Aetobatus narinari",
                    siteId = "open_sea", minDepth = 1, maxDepth = 40, minCm = 100, maxCm = 300, rarity = Rarity.Uncommon, baseValue = 70,
                    plan = BodyPlan.Ray, variant = "eagle", behavior = BehaviorType.Wanderer, groupMin = 2, groupMax = 6, speed = 0.6f,
                    colorA = C("#2E3E5C"), colorB = C("#F4EFE6"), colorC = C("#F4EFE6"), pattern = PatternType.Spots,
                    bodyHeight = 0.1f, bodyWidth = 1.4f, finSize = 1f,
                    zones = Z(LG, OS, KF, VI), prefDepth = 10,   // Hochsee page: no site gate (L.1 – that page unlocks nothing)
                    factDe = "Springt manchmal einfach aus Spaß aus dem Wasser.", factEn = "Sometimes leaps out of the water just for fun." },
                new SpeciesDef { id = "yellowfin", nameDe = "Gelbflossen-Thun", nameEn = "Yellowfin Tuna", latin = "Thunnus albacares",
                    siteId = "open_sea", minDepth = 0, maxDepth = 60, minCm = 60, maxCm = 200, rarity = Rarity.Uncommon, baseValue = 55,
                    plan = BodyPlan.Fish, variant = "tuna", behavior = BehaviorType.Schooling, groupMin = 4, groupMax = 10, speed = 2.2f,
                    colorA = C("#2B3A5C"), colorB = C("#D9DEE2"), colorC = C("#F2C94C"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.24f, bodyWidth = 0.16f, finSize = 1.0f,
                    zones = Z(OS, BD, VI), prefDepth = 15,   // Hochsee page: no site gate
                    factDe = "Kann über 70 km/h schnell schwimmen.", factEn = "Can swim faster than 70 km/h." },
                new SpeciesDef { id = "lanternfish", nameDe = "Laternenfisch", nameEn = "Lanternfish", latin = "Myctophum punctatum",
                    siteId = "open_sea", minDepth = 40, maxDepth = 98, minCm = 3, maxCm = 15, rarity = Rarity.Common, baseValue = 12,
                    plan = BodyPlan.Fish, variant = "lantern", behavior = BehaviorType.Schooling, groupMin = 20, groupMax = 40, speed = 1.2f,
                    colorA = C("#2E3E5C"), colorB = C("#8EA6C4"), colorC = C("#7FD3E0"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.22f, bodyWidth = 0.12f, finSize = 0.9f, glows = true,
                    zones = Z(DS, DT, OS, VI), prefDepth = 60, nightShift = -30f,   // Hochsee page: no site gate – a night open-sea species
                    factDe = "Wandert jede Nacht hunderte Meter nach oben – die größte Reise der Welt.", factEn = "Rises hundreds of metres every night – the biggest migration on Earth." },

                // ================= Sealog page „Nebelinsel“ (5, K.8) – gate: fog_map =================
                new SpeciesDef { id = "pygmy_seahorse", nameDe = "Zwergseepferdchen", nameEn = "Pygmy Seahorse", latin = "Hippocampus bargibanti",
                    siteId = "fog_isle", minDepth = 8, maxDepth = 25, minCm = 1.5f, maxCm = 2.7f, rarity = Rarity.Rare, baseValue = 180,
                    plan = BodyPlan.Seahorse, variant = "pygmy", behavior = BehaviorType.BottomDweller, groupMin = 1, groupMax = 1, speed = 0.05f,
                    colorA = C("#F28CBE"), colorB = C("#F2C94C"), colorC = C("#D9455A"), pattern = PatternType.Spots,
                    bodyHeight = 0.5f, bodyWidth = 0.18f, finSize = 0.6f,
                    zones = Z(FI), prefDepth = 15, homeSite = "fog_isle", unlockFlag = StoryKeys.FogMap, habitat = "seafan",
                    factDe = "Sieht genau aus wie die Koralle, auf der es wohnt – Tarnung Stufe 100.", factEn = "Looks exactly like the coral it lives on – camouflage level 100." },
                new SpeciesDef { id = "mandarinfish", nameDe = "Mandarinfisch", nameEn = "Mandarinfish", latin = "Synchiropus splendidus",
                    siteId = "fog_isle", minDepth = 2, maxDepth = 15, minCm = 4, maxCm = 7, rarity = Rarity.Uncommon, baseValue = 90,
                    plan = BodyPlan.Fish, variant = "mandarin", behavior = BehaviorType.Skittish, groupMin = 1, groupMax = 2, speed = 0.7f,
                    colorA = C("#2F6FD6"), colorB = C("#F28C4A"), colorC = C("#2EC4A0"), pattern = PatternType.HorizontalStripes,
                    bodyHeight = 0.32f, bodyWidth = 0.2f, finSize = 1.3f,
                    zones = Z(FI), prefDepth = 6, dayMask = DayMask.Dusk, homeSite = "fog_isle", unlockFlag = StoryKeys.FogMap, habitat = "overhang",
                    factDe = "Kommt nur in der Dämmerung raus. Zum Tanzen.", factEn = "Only comes out at dusk. To dance." },
                new SpeciesDef { id = "flamboyant_cuttle", nameDe = "Flamboyant-Sepia", nameEn = "Flamboyant Cuttlefish", latin = "Metasepia pfefferi",
                    siteId = "fog_isle", minDepth = 4, maxDepth = 25, minCm = 5, maxCm = 8, rarity = Rarity.Rare, baseValue = 160,
                    plan = BodyPlan.Cuttlefish, variant = "flamboyant", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 1, speed = 0.2f,
                    colorA = C("#7A3E8E"), colorB = C("#F2C94C"), colorC = C("#D9455A"), pattern = PatternType.Mottled,
                    bodyHeight = 0.3f, bodyWidth = 0.34f, finSize = 1f,
                    zones = Z(FI), prefDepth = 12, homeSite = "fog_isle", unlockFlag = StoryKeys.FogMap, habitat = "sand",
                    factDe = "Läuft lieber, als zu schwimmen – auf seinen Armen.", factEn = "Prefers walking to swimming – on its arms." },
                new SpeciesDef { id = "ribbon_eel", nameDe = "Geistermuräne", nameEn = "Ribbon Eel", latin = "Rhinomuraena quaesita",
                    siteId = "fog_isle", minDepth = 3, maxDepth = 25, minCm = 65, maxCm = 130, rarity = Rarity.Uncommon, baseValue = 110,
                    plan = BodyPlan.Eel, variant = "ribbon", behavior = BehaviorType.Ambush, groupMin = 1, groupMax = 1, speed = 0.3f,
                    colorA = C("#2F6FD6"), colorB = C("#F2C94C"), colorC = C("#1E1B2B"), pattern = PatternType.Solid,
                    bodyHeight = 0.05f, bodyWidth = 0.04f, finSize = 1.2f,
                    zones = Z(FI), prefDepth = 10, homeSite = "fog_isle", unlockFlag = StoryKeys.FogMap, habitat = "crevice",
                    factDe = "Wechselt im Leben die Farbe und sogar das Geschlecht.", factEn = "Changes colour – and even sex – during its life." },
                new SpeciesDef { id = "coelacanth", nameDe = "Quastenflosser „Opa Fossil“", nameEn = "Coelacanth “Grandpa Fossil”", latin = "Latimeria chalumnae",
                    siteId = "fog_isle", minDepth = 22, maxDepth = 28, minCm = 150, maxCm = 200, rarity = Rarity.Legendary, baseValue = 1200,
                    plan = BodyPlan.Fish, variant = "coelacanth", behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 1, speed = 0.2f,
                    colorA = C("#2E3E5C"), colorB = C("#8EA6C4"), colorC = C("#F4EFE6"), pattern = PatternType.Spots,
                    bodyHeight = 0.3f, bodyWidth = 0.2f, finSize = 1.3f, glows = true,
                    zones = Z(FI), prefDepth = 25, homeSite = "fog_isle", unlockFlag = StoryKeys.FogMap, unique = true, habitat = "overhang",
                    factDe = "Galt 66 Millionen Jahre als ausgestorben. Hat sich nur versteckt.", factEn = "Was thought extinct for 66 million years. Was just hiding." },

                // ================= Sealog page „Insel“ (10, L.11) – land animals =================
                new SpeciesDef { id = "seagull", nameDe = "Möwe", nameEn = "Seagull", latin = "Larus argentatus",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 40, maxCm = 55, rarity = Rarity.Common, baseValue = 3,
                    plan = BodyPlan.Bird, variant = "gull", behavior = BehaviorType.Perch, groupMin = 1, groupMax = 3, speed = 1.5f,
                    colorA = C("#F4EFE6"), colorB = C("#9AA5AE"), colorC = C("#F2C94C"), pattern = PatternType.Solid,
                    bodyHeight = 0.3f, bodyWidth = 0.25f, finSize = 2.4f,
                    zones = Z(IS), land = true, dayMask = DayMask.Day | DayMask.Dusk,
                    factDe = "Klaut Pommes mit chirurgischer Präzision. Günther ist Profi.", factEn = "Steals chips with surgical precision. Günther is a pro." },
                new SpeciesDef { id = "frigatebird", nameDe = "Prachtfregattvogel", nameEn = "Magnificent Frigatebird", latin = "Fregata magnificens",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 90, maxCm = 110, rarity = Rarity.Uncommon, baseValue = 25,
                    plan = BodyPlan.Bird, variant = "frigate", behavior = BehaviorType.Soar, groupMin = 1, groupMax = 2, speed = 1.2f,
                    colorA = C("#1E1B2B"), colorB = C("#D9455A"), colorC = C("#4E5A63"), pattern = PatternType.Solid,
                    bodyHeight = 0.22f, bodyWidth = 0.2f, finSize = 3f,
                    zones = Z(IS), land = true, dayMask = DayMask.Day,
                    factDe = "Kann wochenlang fliegen, ohne zu landen.", factEn = "Can fly for weeks without landing." },
                new SpeciesDef { id = "tropicbird", nameDe = "Weißschwanz-Tropikvogel", nameEn = "White-tailed Tropicbird", latin = "Phaethon lepturus",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 70, maxCm = 85, rarity = Rarity.Uncommon, baseValue = 30,
                    plan = BodyPlan.Bird, variant = "tropic", behavior = BehaviorType.Perch, groupMin = 1, groupMax = 3, speed = 1.4f,
                    colorA = C("#F4EFE6"), colorB = C("#1E1B2B"), colorC = C("#EE7F2D"), pattern = PatternType.Solid,
                    bodyHeight = 0.22f, bodyWidth = 0.2f, finSize = 2.6f,
                    zones = Z(IS), land = true, dayMask = DayMask.Day,
                    factDe = "Die langen Schwanzfedern sind halb so lang wie der ganze Vogel.", factEn = "Its tail streamers are half as long as the whole bird." },
                new SpeciesDef { id = "marine_iguana", nameDe = "Meerechse", nameEn = "Marine Iguana", latin = "Amblyrhynchus cristatus",
                    siteId = "island", minDepth = -60, maxDepth = 5, minCm = 60, maxCm = 130, rarity = Rarity.Uncommon, baseValue = 40,
                    plan = BodyPlan.Lizard, variant = "iguana", behavior = BehaviorType.Bask, groupMin = 1, groupMax = 3, speed = 0.4f,
                    colorA = C("#3B3A4E"), colorB = C("#A8724A"), colorC = C("#6E6F86"), pattern = PatternType.Mottled,
                    bodyHeight = 0.14f, bodyWidth = 0.16f, finSize = 1f,
                    zones = Z(IS, LG), land = true, dayMask = DayMask.Day,
                    factDe = "Die einzige Echse der Welt, die im Meer taucht.", factEn = "The only lizard in the world that dives in the sea." },
                new SpeciesDef { id = "gecko", nameDe = "Taggecko", nameEn = "Gold Dust Day Gecko", latin = "Phelsuma laticauda",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 10, maxCm = 14, rarity = Rarity.Common, baseValue = 5,
                    plan = BodyPlan.Lizard, variant = "gecko", behavior = BehaviorType.Climb, groupMin = 1, groupMax = 2, speed = 1.0f,
                    colorA = C("#6E9445"), colorB = C("#F2C94C"), colorC = C("#D9455A"), pattern = PatternType.Spots,
                    bodyHeight = 0.12f, bodyWidth = 0.14f, finSize = 1f,
                    zones = Z(IS), land = true, dayMask = DayMask.Day,
                    factDe = "Läuft kopfüber an Fensterscheiben entlang – dank Millionen winziger Härchen.", factEn = "Walks upside down on windows – thanks to millions of tiny hairs." },
                new SpeciesDef { id = "coconut_crab", nameDe = "Palmendieb", nameEn = "Coconut Crab", latin = "Birgus latro",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 30, maxCm = 100, rarity = Rarity.Rare, baseValue = 45,
                    plan = BodyPlan.Crab, variant = "coconut", behavior = BehaviorType.Climb, groupMin = 1, groupMax = 1, speed = 0.3f,
                    colorA = C("#4F4A8E"), colorB = C("#EE7F2D"), colorC = C("#2B2533"), pattern = PatternType.Mottled,
                    bodyHeight = 0.35f, bodyWidth = 1.1f, finSize = 1f,
                    zones = Z(IS), land = true, dayMask = DayMask.Night,
                    factDe = "Größter Krebs an Land – knackt Kokosnüsse.", factEn = "Biggest crab on land – cracks coconuts." },
                new SpeciesDef { id = "goat", nameDe = "Ziege „Kapitän Meck“", nameEn = "Goat “Captain Bleat”", latin = "Capra hircus",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 100, maxCm = 140, rarity = Rarity.Common, baseValue = 4,
                    plan = BodyPlan.Quadruped, variant = "goat", behavior = BehaviorType.Herd, groupMin = 2, groupMax = 5, speed = 0.6f,
                    colorA = C("#EDE6DA"), colorB = C("#8E6C50"), colorC = C("#3B3A4E"), pattern = PatternType.Mottled,
                    bodyHeight = 0.7f, bodyWidth = 0.3f, finSize = 1f,
                    zones = Z(IS), land = true, dayMask = DayMask.Day | DayMask.Dusk,
                    factDe = "Frisst Segel, Hüte und Schnürsenkel. Gras eher selten.", factEn = "Eats sails, hats and shoelaces. Grass, rarely." },
                new SpeciesDef { id = "pig", nameDe = "Schwein „Trüffel“", nameEn = "Pig “Truffle”", latin = "Sus domesticus",
                    siteId = "island", minDepth = -60, maxDepth = 2, minCm = 90, maxCm = 140, rarity = Rarity.Uncommon, baseValue = 20,
                    plan = BodyPlan.Quadruped, variant = "pig", behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 1, speed = 0.5f,
                    colorA = C("#E9A27A"), colorB = C("#F0C4C0"), colorC = C("#6B4636"), pattern = PatternType.Spots,
                    bodyHeight = 0.55f, bodyWidth = 0.4f, finSize = 1f,
                    zones = Z(IS, LG), land = true, unique = true, dayMask = DayMask.Day,
                    factDe = "Schwimmt lieber als zu wühlen.", factEn = "Prefers swimming to digging." },
                new SpeciesDef { id = "capuchin", nameDe = "Kapuzineraffe „Klappe“", nameEn = "Capuchin “Clapper”", latin = "Cebus imitator",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 35, maxCm = 50, rarity = Rarity.Rare, baseValue = 60,
                    plan = BodyPlan.Primate, variant = "capuchin", behavior = BehaviorType.Mischief, groupMin = 1, groupMax = 1, speed = 1.2f,
                    colorA = C("#6B4636"), colorB = C("#E8D9B4"), colorC = C("#2B2533"), pattern = PatternType.BellyGradient,
                    bodyHeight = 0.6f, bodyWidth = 0.35f, finSize = 1f,
                    zones = Z(IS), land = true, unique = true, dayMask = DayMask.Day,
                    factDe = "War 1987 das Maskottchen des TV-Teams. Hat seitdem schlechte Manieren.", factEn = "Was the TV crew's mascot in 1987. Has had bad manners ever since." },
                new SpeciesDef { id = "fruit_bat", nameDe = "Flughund", nameEn = "Fruit Bat", latin = "Pteropus",
                    siteId = "island", minDepth = -60, maxDepth = 0, minCm = 15, maxCm = 30, rarity = Rarity.Common, baseValue = 15,
                    plan = BodyPlan.Bat, variant = "flyingfox", behavior = BehaviorType.Wanderer, groupMin = 3, groupMax = 10, speed = 1.6f,
                    colorA = C("#6B4636"), colorB = C("#A8724A"), colorC = C("#1E1B2B"), pattern = PatternType.Solid,
                    bodyHeight = 0.35f, bodyWidth = 0.3f, finSize = 3f,
                    zones = Z(IS), land = true, dayMask = DayMask.Dusk | DayMask.Night,
                    factDe = "Schläft kopfüber und pflanzt nebenbei ganze Wälder – mit Fruchtkernen.", factEn = "Sleeps upside down and plants whole forests on the side – with fruit seeds." },
            };
            AddRound3(all);

            byId = new Dictionary<string, SpeciesDef>();
            sealogCount = 0;
            foreach (var s in all)
            {
                byId[s.id] = s;
                if (s.zones == null) s.zones = new[] { s.siteId };
                // round 3 (Q23): island land animals are decoration – they react and can be filmed, but are no Sealog entry
                if (s.land) s.deco = true;
                if (s.InSealog) sealogCount++;
                // the glowing curiosities light themselves (camera light factor)
                if (s.id == "meme_gamingpc" || s.id == "meme_dance") s.glows = true;
            }
            for (int i = 0; i < FoodIds.Length; i++) if (byId.TryGetValue(FoodIds[i], out var f)) f.food = true;
            for (int i = 0; i < HunterIds.Length; i++) if (byId.TryGetValue(HunterIds[i], out var h)) h.hunter = true;
        }

        /// Round 3 (Testversion 5, GDD §19/§20): more life everywhere – night predators and rare night animals (Q44),
        /// food fish for spear fishing (Q18), sharks that may attack (Q16), the volcano island's living fossils + the
        /// prehistoric monster (Q14, own Sealog page) and the curiosities / memes (Q19, own page). Decoration animals of
        /// the island (Q22/Q23) follow at the end. APPEND ONLY (co-op replication sends the index into All).
        static void AddRound3(List<SpeciesDef> l)
        {
            // ---- house reef page: the night shift
            l.Add(new SpeciesDef { id = "whitetip_shark", nameDe = "Weißspitzen-Riffhai", nameEn = "Whitetip Reef Shark", latin = "Triaenodon obesus",
                siteId = "house_reef", minDepth = 3, maxDepth = 30, minCm = 100, maxCm = 170, rarity = Rarity.Rare, baseValue = 110,
                plan = BodyPlan.Shark, behavior = BehaviorType.Predator, groupMin = 1, groupMax = 2, speed = 0.8f,
                colorA = C("#7E8A8E"), colorB = C("#EEF0EE"), colorC = C("#F7F7F4"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.12f, bodyWidth = 0.12f, finSize = 1.1f,
                zones = Z(HR, LG, WR, FI), prefDepth = 12, nightWeight = 4f, habitat = "overhang",
                factDe = "Döst tagsüber in Höhlen und jagt nachts in Riffspalten. Kommt gern nachsehen, wer da taucht.", factEn = "Dozes in caves by day and hunts in reef crevices at night. Likes to check who is diving." });
            l.Add(new SpeciesDef { id = "flashlight_fish", nameDe = "Blitzlichtfisch", nameEn = "Flashlight Fish", latin = "Anomalops katoptron",
                siteId = "house_reef", minDepth = 2, maxDepth = 30, minCm = 10, maxCm = 30, rarity = Rarity.Rare, baseValue = 65,
                plan = BodyPlan.Fish, variant = "lantern", behavior = BehaviorType.Schooling, groupMin = 3, groupMax = 8, speed = 1.5f,
                colorA = C("#1B1F2E"), colorB = C("#2A3048"), colorC = C("#9FF5FF"), pattern = PatternType.Solid,
                bodyHeight = 0.3f, bodyWidth = 0.14f, finSize = 0.9f, glows = true,
                zones = Z(HR, WR, FI), prefDepth = 10, dayMask = DayMask.Night, nightWeight = 1.5f,
                factDe = "Blinkt mit Leuchtbakterien unter den Augen – wie eine Taschenlampe mit Schalter.", factEn = "Blinks with glowing bacteria under its eyes – like a torch with a switch." });

            // ---- wreck page: schools around the hull (food fish)
            l.Add(new SpeciesDef { id = "snapper", nameDe = "Rotschnapper", nameEn = "Red Snapper", latin = "Lutjanus campechanus",
                siteId = "wreck", minDepth = 8, maxDepth = 45, minCm = 30, maxCm = 80, rarity = Rarity.Common, baseValue = 18,
                plan = BodyPlan.Fish, behavior = BehaviorType.Schooling, groupMin = 6, groupMax = 14, speed = 1.2f,
                colorA = C("#D9454A"), colorB = C("#F6C1B0"), colorC = C("#B0303A"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.36f, bodyWidth = 0.15f, finSize = 1f,
                zones = Z(WR, KF, BD, VI), prefDepth = 22, homeSite = "wreck",
                factDe = "Ein beliebter Speisefisch – lebt am liebsten in Wracks und Riffen.", factEn = "A popular food fish – loves wrecks and reefs." });
            l.Add(new SpeciesDef { id = "jack", nameDe = "Großaugen-Stachelmakrele", nameEn = "Bigeye Trevally", latin = "Caranx sexfasciatus",
                siteId = "wreck", minDepth = 10, maxDepth = 50, minCm = 40, maxCm = 90, rarity = Rarity.Common, baseValue = 22,
                plan = BodyPlan.Fish, variant = "tall", behavior = BehaviorType.Schooling, groupMin = 10, groupMax = 24, speed = 1.6f,
                colorA = C("#AEB8C2"), colorB = C("#E8ECEF"), colorC = C("#4E5A66"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.38f, bodyWidth = 0.13f, finSize = 1f,
                zones = Z(WR, BD, OS, VI), prefDepth = 18, homeSite = "wreck",
                factDe = "Dreht in riesigen Schwärmen Tornados um Taucher.", factEn = "Forms huge tornado schools around divers." });

            // ---- open sea page: never empty water
            l.Add(new SpeciesDef { id = "mackerel", nameDe = "Makrele", nameEn = "Atlantic Mackerel", latin = "Scomber scombrus",
                siteId = "open_sea", minDepth = 0, maxDepth = 40, minCm = 25, maxCm = 45, rarity = Rarity.Common, baseValue = 6,
                plan = BodyPlan.Fish, variant = "slender", behavior = BehaviorType.Schooling, groupMin = 20, groupMax = 50, speed = 2.1f,
                colorA = C("#2E6B7A"), colorB = C("#E8ECEF"), colorC = C("#1E3A44"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.2f, bodyWidth = 0.12f, finSize = 0.8f,
                zones = Z(OS, LG, KF, VI), prefDepth = 10,
                factDe = "Hat keine Schwimmblase und muss deshalb immer schwimmen.", factEn = "Has no swim bladder, so it can never stop swimming." });
            l.Add(new SpeciesDef { id = "mahi_mahi", nameDe = "Goldmakrele", nameEn = "Mahi-Mahi", latin = "Coryphaena hippurus",
                siteId = "open_sea", minDepth = 0, maxDepth = 30, minCm = 70, maxCm = 150, rarity = Rarity.Uncommon, baseValue = 45,
                plan = BodyPlan.Fish, behavior = BehaviorType.Wanderer, groupMin = 1, groupMax = 4, speed = 1.8f,
                colorA = C("#2FB36B"), colorB = C("#F2D54B"), colorC = C("#2F6FD6"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.28f, bodyWidth = 0.12f, finSize = 1.2f,
                zones = Z(OS, BD), prefDepth = 6, dayMask = DayMask.Day,
                factDe = "Leuchtet grün-gold und wechselt beim Jagen blitzschnell die Farbe.", factEn = "Shines green and gold and flashes new colours while hunting." });
            l.Add(new SpeciesDef { id = "tiger_shark", nameDe = "Tigerhai", nameEn = "Tiger Shark", latin = "Galeocerdo cuvier",
                siteId = "open_sea", minDepth = 0, maxDepth = 80, minCm = 300, maxCm = 500, rarity = Rarity.Rare, baseValue = 320,
                plan = BodyPlan.Shark, behavior = BehaviorType.Predator, groupMin = 1, groupMax = 1, speed = 0.8f,
                colorA = C("#6F7466"), colorB = C("#E6E4DA"), colorC = C("#3E423A"), pattern = PatternType.VerticalBands,
                bodyHeight = 0.14f, bodyWidth = 0.15f, finSize = 1.1f,
                zones = Z(OS, DS, BD, VI), prefDepth = 20, nightWeight = 2.5f,
                factDe = "Frisst so ziemlich alles – in einem Magen fand man schon ein Nummernschild.", factEn = "Eats pretty much anything – one stomach held a licence plate." });
            l.Add(new SpeciesDef { id = "comb_jelly", nameDe = "Rippenqualle", nameEn = "Comb Jelly", latin = "Beroe cucumis",
                siteId = "open_sea", minDepth = 0, maxDepth = 40, minCm = 5, maxCm = 15, rarity = Rarity.Common, baseValue = 14,
                plan = BodyPlan.Jelly, behavior = BehaviorType.Drifter, groupMin = 2, groupMax = 6, speed = 0.12f,
                colorA = C("#CFE8FF"), colorB = C("#8EF5FF"), colorC = C("#F28CBE"), pattern = PatternType.Translucent,
                bodyHeight = 0.6f, bodyWidth = 0.7f, finSize = 1f, glows = true,
                zones = Z(OS, LG, DS, HR), prefDepth = 12, dayMask = DayMask.Dusk | DayMask.Night, nightWeight = 2f,
                factDe = "Hat keine Nesseln – die Regenbogenstreifen sind schlagende Wimpern.", factEn = "Has no sting – the rainbow stripes are beating hairs." });

            // ---- Vulkaninsel page (Q14): living fossils + the monster – gate: the wreck unlock (VolcanoGate)
            l.Add(new SpeciesDef { id = "nautilus", nameDe = "Perlboot", nameEn = "Chambered Nautilus", latin = "Nautilus pompilius",
                siteId = VolcanoPage, minDepth = 20, maxDepth = 90, minCm = 15, maxCm = 25, rarity = Rarity.Uncommon, baseValue = 90,
                plan = BodyPlan.Squid, variant = "nautilus", behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 3, speed = 0.25f,
                colorA = C("#F2E6D0"), colorB = C("#A8562E"), colorC = C("#6B3A22"), pattern = PatternType.VerticalBands,
                bodyHeight = 0.6f, bodyWidth = 0.4f, finSize = 1f,
                zones = Z(VI), prefDepth = 50, nightShift = -25f, homeSite = "wreck",
                factDe = "Schwimmt rückwärts per Rückstoß – seit 500 Millionen Jahren fast unverändert.", factEn = "Jets backwards – almost unchanged for 500 million years." });
            l.Add(new SpeciesDef { id = "horseshoe_crab", nameDe = "Pfeilschwanzkrebs", nameEn = "Horseshoe Crab", latin = "Tachypleus tridentatus",
                siteId = VolcanoPage, minDepth = 1, maxDepth = 25, minCm = 30, maxCm = 60, rarity = Rarity.Common, baseValue = 35,
                plan = BodyPlan.Crab, variant = "horseshoe", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 4, speed = 0.15f,
                colorA = C("#6B5A3A"), colorB = C("#A08A5E"), colorC = C("#3A3020"), pattern = PatternType.Mottled,
                bodyHeight = 0.28f, bodyWidth = 0.9f, finSize = 1f,
                zones = Z(VI), prefDepth = 6, homeSite = "wreck", habitat = "sand",
                factDe = "Hat blaues Blut und zehn Augen – und ist älter als die Dinosaurier.", factEn = "Has blue blood and ten eyes – and is older than the dinosaurs." });
            l.Add(new SpeciesDef { id = "sixgill_shark", nameDe = "Sechskiemer-Hai", nameEn = "Bluntnose Sixgill Shark", latin = "Hexanchus griseus",
                siteId = VolcanoPage, minDepth = 30, maxDepth = 90, minCm = 250, maxCm = 480, rarity = Rarity.Rare, baseValue = 280,
                plan = BodyPlan.Shark, behavior = BehaviorType.Predator, groupMin = 1, groupMax = 1, speed = 0.5f,
                colorA = C("#5A5650"), colorB = C("#8A847A"), colorC = C("#3A3632"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.14f, bodyWidth = 0.15f, finSize = 0.9f,
                zones = Z(VI), prefDepth = 60, nightShift = -30f, nightWeight = 2f, homeSite = "wreck",
                factDe = "Sechs Kiemenspalten statt fünf – so sahen Haie vor 200 Millionen Jahren aus.", factEn = "Six gill slits instead of five – that's what sharks looked like 200 million years ago." });
            l.Add(new SpeciesDef { id = "frilled_shark", nameDe = "Kragenhai", nameEn = "Frilled Shark", latin = "Chlamydoselachus anguineus",
                siteId = VolcanoPage, minDepth = 40, maxDepth = 100, minCm = 100, maxCm = 200, rarity = Rarity.Rare, baseValue = 240,
                plan = BodyPlan.Eel, variant = "frilled", behavior = BehaviorType.Drifter, groupMin = 1, groupMax = 1, speed = 0.3f,
                colorA = C("#6A4A3E"), colorB = C("#8A6A5A"), colorC = C("#3A2A22"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.09f, bodyWidth = 0.08f, finSize = 1.1f,
                zones = Z(VI), prefDepth = 70, homeSite = "wreck",
                factDe = "Ein Hai wie ein Aal mit 300 dreizackigen Zähnen. Kaum jemand hat ihn lebend gefilmt.", factEn = "A shark shaped like an eel with 300 three-pronged teeth. Hardly anyone has filmed one alive." });
            l.Add(new SpeciesDef { id = "sea_lily", nameDe = "Seelilie", nameEn = "Sea Lily", latin = "Metacrinus rotundus",
                siteId = VolcanoPage, minDepth = 8, maxDepth = 60, minCm = 30, maxCm = 80, rarity = Rarity.Common, baseValue = 25,
                plan = BodyPlan.Starfish, variant = "crinoid", behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 3, speed = 0.005f,
                colorA = C("#E8B04A"), colorB = C("#F2D27A"), colorC = C("#A86A2A"), pattern = PatternType.Solid,
                bodyHeight = 0.6f, bodyWidth = 0.9f, finSize = 1f,
                zones = Z(VI), prefDepth = 20, homeSite = "wreck", habitat = "seafan",
                factDe = "Sieht aus wie eine Pflanze, ist aber ein Tier – und kann sogar langsam laufen.", factEn = "Looks like a plant but is an animal – and can even walk, slowly." });
            l.Add(new SpeciesDef { id = "hagfish", nameDe = "Schleimaal", nameEn = "Hagfish", latin = "Eptatretus stoutii",
                siteId = VolcanoPage, minDepth = 30, maxDepth = 110, minCm = 30, maxCm = 60, rarity = Rarity.Common, baseValue = 30,
                plan = BodyPlan.Eel, behavior = BehaviorType.BottomDweller, groupMin = 1, groupMax = 3, speed = 0.3f,
                colorA = C("#C8A8A0"), colorB = C("#E0C8C0"), colorC = C("#8A6A66"), pattern = PatternType.Solid,
                bodyHeight = 0.07f, bodyWidth = 0.07f, finSize = 0.6f,
                zones = Z(VI), prefDepth = 70, homeSite = "wreck", habitat = "sand",
                factDe = "Macht in Sekunden einen Eimer voll Schleim – Haie spucken ihn sofort wieder aus.", factEn = "Makes a bucket of slime in seconds – sharks spit it straight back out." });
            l.Add(new SpeciesDef { id = "mosasaurus", nameDe = "Mosasaurus „Big Mo“", nameEn = "Mosasaurus “Big Mo”", latin = "Mosasaurus hoffmannii",
                siteId = VolcanoPage, minDepth = 20, maxDepth = 125, minCm = 1200, maxCm = 1500, rarity = Rarity.Legendary, baseValue = 3000,
                plan = BodyPlan.Shark, variant = "mosasaur", behavior = BehaviorType.Predator, groupMin = 1, groupMax = 1, speed = 0.18f,
                colorA = C("#3E4A42"), colorB = C("#C8C2A8"), colorC = C("#26302A"), pattern = PatternType.BellyGradient,
                bodyHeight = 0.11f, bodyWidth = 0.12f, finSize = 1.2f,
                zones = Z(), prefDepth = 100, homeSite = "wreck", unique = true, monster = true,
                factDe = "Galt seit 66 Millionen Jahren als ausgestorben. Wohnt im Urzeit-Schlund – und mag keinen Besuch.", factEn = "Thought extinct for 66 million years. Lives in the Primeval Sinkhole – and hates visitors." });

            // ---- Kuriositäten page (Q19): memes – placed by SeaContent, filmable for bonus views
            l.Add(Meme("meme_shades", "Cooler Barsch", "Deal-With-It Grouper", BodyPlan.Fish, "grouper", 70f, 90, "#7B5B3E", "#D8C29A", "#4A3422",
                "Trägt Sonnenbrille. Unter Wasser. Weil er es kann. (Hausriff)", "Wears sunglasses. Under water. Because he can. (House Reef)"));
            l.Add(Meme("meme_dance", "Disco-Krake", "Disco Octopus", BodyPlan.Octopus, "", 80f, 120, "#C2408A", "#F2A0C8", "#7A1E52",
                "Tanzt jeden Tag. Niemand weiß, woher die Musik kommt. (Kelpwald)", "Dances every day. Nobody knows where the music comes from. (Kelp Forest)"));
            l.Add(Meme("meme_stonks", "Stonks-Rochen", "Stonks Ray", BodyPlan.Ray, "eagle", 180f, 150, "#1E3A5C", "#F4EFE6", "#3FD46A",
                "Kennt nur eine Richtung: nach oben. Meistens. (Hochsee)", "Knows only one direction: up. Mostly. (Open Sea)"));
            l.Add(Meme("meme_crabrave", "Krabben-Rave", "Crab Rave", BodyPlan.Crab, "flat", 14f, 140, "#E2552E", "#F28C4A", "#3FA9B5",
                "Acht Krabben, ein Beat. Die Party läuft seit 1987. (Lagune)", "Eight crabs, one beat. The party has been going since 1987. (Lagoon)"));
            l.Add(Meme("meme_throne", "Thron am Meeresgrund", "Seabed Throne", BodyPlan.Fish, "", 180f, 110, "#E8C65A", "#B3261E", "#6B4A1E",
                "Wer hier wohl regiert? Die Krone ist jedenfalls echt. (Wrack)", "Who rules down here? The crown is real, at least. (Wreck)"));
            l.Add(Meme("meme_gamingpc", "Gaming-PC im Wrack", "Sunken Gaming PC", BodyPlan.Fish, "", 90f, 130, "#2B2D3A", "#3FD4FF", "#FF3FD4",
                "RGB läuft noch. Der Spielstand leider nicht. (Flugzeugwrack)", "The RGB still works. The save game doesn't. (Plane wreck)"));
            l.Add(Meme("meme_ducks", "Quietscheentchen-Flotte", "Rubber Duck Flotilla", BodyPlan.Fish, "", 400f, 100, "#F2D23C", "#F28C1E", "#1E1B2B",
                "Seit einem Container-Unfall auf Weltreise. (Hochsee, an der Oberfläche)", "On a world tour since a container spill. (Open sea, at the surface)"));
            l.Add(Meme("meme_cart", "Versunkener Einkaufswagen", "Sunken Shopping Cart", BodyPlan.Fish, "", 100f, 70, "#B8C4CC", "#D9454A", "#4E5A66",
                "Jemand hat seinen Einkauf vergessen. Der Fisch darin will ihn behalten. (Hausriff)", "Someone forgot their shopping. The fish inside wants to keep it. (House Reef)"));
            l.Add(Meme("meme_gnome", "Taucher-Gartenzwerg", "Scuba Garden Gnome", BodyPlan.Fish, "", 50f, 80, "#D9454A", "#2F6FD6", "#F4EFE6",
                "Bewacht den Kelpwald. Mit Schnorchel. (Kelpwald)", "Guards the kelp forest. With a snorkel. (Kelp Forest)"));
            l.Add(Meme("meme_dino", "Gummi-Dino", "Rubber Dino", BodyPlan.Fish, "", 60f, 160, "#4FB84A", "#F2D23C", "#2E6B2A",
                "Das Lieblingsspielzeug von Big Mo? Lieber nicht fragen. (Vulkaninsel)", "Big Mo's favourite toy? Better not ask. (Volcano Isle)"));

            // ---- island decoration (Q22/Q23: birds, crabs, lizards – no Sealog)
            l.Add(new SpeciesDef { id = "red_crab", nameDe = "Rote Landkrabbe", nameEn = "Red Land Crab", latin = "Gecarcoidea natalis",
                siteId = "island", minDepth = -60, maxDepth = 0, minCm = 6, maxCm = 11, rarity = Rarity.Common, baseValue = 4,
                plan = BodyPlan.Crab, behavior = BehaviorType.Crawler, groupMin = 1, groupMax = 3, speed = 0.9f,
                colorA = C("#C8282A"), colorB = C("#E85A3A"), colorC = C("#2B2533"), pattern = PatternType.Solid,
                bodyHeight = 0.4f, bodyWidth = 1.1f, finSize = 1f, zones = Z(IS), land = true });
            l.Add(new SpeciesDef { id = "booby", nameDe = "Blaufußtölpel", nameEn = "Blue-footed Booby", latin = "Sula nebouxii",
                siteId = "island", minDepth = -60, maxDepth = 0, minCm = 75, maxCm = 90, rarity = Rarity.Common, baseValue = 6,
                plan = BodyPlan.Bird, variant = "gull", behavior = BehaviorType.Perch, groupMin = 1, groupMax = 3, speed = 1.4f,
                colorA = C("#8A6A4A"), colorB = C("#F4EFE6"), colorC = C("#3FA9E0"), pattern = PatternType.Solid,
                bodyHeight = 0.3f, bodyWidth = 0.25f, finSize = 2.5f, zones = Z(IS), land = true, dayMask = DayMask.Day | DayMask.Dusk });
            l.Add(new SpeciesDef { id = "skink", nameDe = "Blauschwanz-Skink", nameEn = "Blue-tailed Skink", latin = "Emoia caeruleocauda",
                siteId = "island", minDepth = -60, maxDepth = 0, minCm = 12, maxCm = 20, rarity = Rarity.Common, baseValue = 4,
                plan = BodyPlan.Lizard, variant = "gecko", behavior = BehaviorType.Bask, groupMin = 1, groupMax = 2, speed = 1.2f,
                colorA = C("#4A3A2A"), colorB = C("#D8B070"), colorC = C("#2F8FE0"), pattern = PatternType.Solid,
                bodyHeight = 0.12f, bodyWidth = 0.14f, finSize = 1f, zones = Z(IS), land = true, dayMask = DayMask.Day });
            l.Add(new SpeciesDef { id = "reef_heron", nameDe = "Riffreiher", nameEn = "Reef Heron", latin = "Egretta sacra",
                siteId = "island", minDepth = -60, maxDepth = 0, minCm = 55, maxCm = 65, rarity = Rarity.Uncommon, baseValue = 8,
                plan = BodyPlan.Bird, variant = "tropic", behavior = BehaviorType.Perch, groupMin = 1, groupMax = 1, speed = 1.2f,
                colorA = C("#3A4048"), colorB = C("#E8ECEF"), colorC = C("#F2C94C"), pattern = PatternType.Solid,
                bodyHeight = 0.24f, bodyWidth = 0.18f, finSize = 2.4f, zones = Z(IS), land = true, dayMask = DayMask.Day | DayMask.Dusk });
        }

        static SpeciesDef Meme(string id, string de, string en, BodyPlan plan, string variant, float cm, int value, string a, string b, string c, string factDe, string factEn)
            => new SpeciesDef
            {
                id = id, nameDe = de, nameEn = en, latin = "Memeus maximus", siteId = MemePage, page = MemePage,
                minDepth = 0, maxDepth = 40, minCm = cm * 0.95f, maxCm = cm * 1.05f, rarity = Rarity.Epic, baseValue = value,
                plan = plan, variant = variant ?? "", behavior = BehaviorType.Wanderer, speed = 0.5f,
                colorA = C(a), colorB = C(b), colorC = C(c), pattern = PatternType.Mottled,
                bodyHeight = plan == BodyPlan.Ray ? 0.1f : plan == BodyPlan.Crab ? 0.25f : 0.36f,
                bodyWidth = plan == BodyPlan.Ray ? 1.4f : plan == BodyPlan.Crab ? 1.2f : plan == BodyPlan.Octopus ? 0.4f : 0.22f,
                finSize = 1f, zones = Z(), meme = true,
                factDe = factDe, factEn = factEn,
            };
    }
}
