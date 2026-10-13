using UnityEngine;

namespace Sealebrities
{
    public enum ResearchKind : byte
    {
        /// A photo of `target` (species) – conditions: a = max camera pitch (−45 = from above), flags lamp/night/depth.
        Photo,
        /// A clip of `target` – a = min crowd, b = min tier, flags: boat speed > 6 m/s.
        Film,
        /// Catch `count` fish of kind `target` heavier than a kg (b = 1 → from the boat).
        Catch,
        /// Take `count` water samples at the research stakes of site `target` (E, 2 s).
        Samples,
        /// Count sightings of the species in `target` (csv) – `count` animals.
        Census,
        /// Stay a seconds within b metres of `target` while it is around (whale song).
        Listen
    }

    public sealed class ResearchDef
    {
        public string id, de, en;
        public ResearchKind kind;
        public string target;
        public int count = 1;
        public float a, b;
        public bool needLamp, needNight;
        public float minDepth;
        public bool boatFast;
        public long money;
        /// 1–4 for the rod chain R1–R4 (always offered in order), 0 = normal pool.
        public int rod;
        public string needSite;
        public string Title => Loc.Tr(de, en);
    }

    /// Dr. Fin's research quests (B.4 + K.3 research additions). Rewards are balance constants (J.5: npc).
    public static class ResearchDatabase
    {
        public const int MaxOpen = 3;

        public static readonly ResearchDef[] All =
        {
            new ResearchDef { id = "fin_photo_ray", de = "Foto: Blaupunktrochen von oben", en = "Photo: bluespotted ray from above", kind = ResearchKind.Photo, target = "bluespotted_ray", a = -45f, money = 120 },
            new ResearchDef { id = "fin_photo_octo_light", de = "Foto: Krake im Lampenlicht", en = "Photo: octopus in lamplight", kind = ResearchKind.Photo, target = "octopus", needLamp = true, money = 180 },
            new ResearchDef { id = "fin_film_garibaldi3", de = "3 Garibaldis in einem Clip", en = "3 garibaldi in one clip", kind = ResearchKind.Film, target = "garibaldi", a = 2f, money = 250, needSite = "kelp_forest" },
            new ResearchDef { id = "fin_measure_bass", de = "Riesen-Wrackbarsch, Stufe Groß+", en = "Giant sea bass, Large or bigger", kind = ResearchKind.Film, target = "giant_sea_bass", b = (float)SizeTier.Large, money = 600, needSite = "kelp_forest" },
            new ResearchDef { id = "fin_photo_jelly_night", de = "Foto: Mondqualle bei Nacht", en = "Photo: moon jelly at night", kind = ResearchKind.Photo, target = "moon_jelly", needNight = true, money = 300 },
            new ResearchDef { id = "fin_photo_starfish", de = "Foto: Dornenkronen-Seestern", en = "Photo: crown-of-thorns starfish", kind = ResearchKind.Photo, target = "crown_star", money = 200 },
            new ResearchDef { id = "fin_film_dolphins", de = "Delfine an der Bugwelle filmen", en = "Film dolphins on the bow wave", kind = ResearchKind.Film, target = "dolphin", boatFast = true, money = 900, needSite = "kelp_forest" },
            new ResearchDef { id = "fin_photo_goblin", de = "Foto: Koboldhai", en = "Photo: goblin shark", kind = ResearchKind.Photo, target = "goblin_shark", money = 6000, needSite = "deep_trench" },
            new ResearchDef { id = "fin_census_star", de = "Zähle 10 Seesterne", en = "Count 10 starfish", kind = ResearchKind.Census, target = "blue_starfish,crown_star", count = 10, money = 250 },
            new ResearchDef { id = "fin_depth_grouper", de = "Wie tief lebt der Zackenbarsch?", en = "How deep does the grouper live?", kind = ResearchKind.Photo, target = "grouper", minDepth = 25f, money = 400, needSite = "wreck" },
            new ResearchDef { id = "fin_listen_whale", de = "20 s beim singenden Wal bleiben", en = "Stay 20 s with the singing whale", kind = ResearchKind.Listen, target = "humpback", a = 20f, b = 30f, money = 1000, needSite = "blue_drop" },
            new ResearchDef { id = "fin_samples_house_reef", de = "3 Wasserproben am Hausriff", en = "3 water samples at the House Reef", kind = ResearchKind.Samples, target = "house_reef", count = 3, money = 150 },
            new ResearchDef { id = "fin_samples_kelp_forest", de = "3 Wasserproben am Kelpwald", en = "3 water samples at the Kelp Forest", kind = ResearchKind.Samples, target = "kelp_forest", count = 3, money = 300, needSite = "kelp_forest" },
            new ResearchDef { id = "fin_samples_wreck", de = "3 Wasserproben am Wrack", en = "3 water samples at the Wreck", kind = ResearchKind.Samples, target = "wreck", count = 3, money = 500, needSite = "wreck" },
            new ResearchDef { id = "fin_samples_blue_drop", de = "3 Wasserproben an der Kante", en = "3 water samples at the Drop-off", kind = ResearchKind.Samples, target = "blue_drop", count = 3, money = 900, needSite = "blue_drop" },
            new ResearchDef { id = "fin_samples_deep_trench", de = "3 Wasserproben im Graben", en = "3 water samples in the Trench", kind = ResearchKind.Samples, target = "deep_trench", count = 3, money = 1500, needSite = "deep_trench" },
            new ResearchDef { id = "fin_samples_fog_isle", de = "3 Wasserproben an der Nebelinsel", en = "3 water samples at the Fog Isle", kind = ResearchKind.Samples, target = "fog_isle", count = 3, money = 600, needSite = "fog_isle" },
            // rod chain R1 → R4 (D.3, always one of them open until R4 is done)
            new ResearchDef { id = "fin_fish_bream", de = "3 Brassen über 1,2 kg", en = "3 sea bream over 1.2 kg", kind = ResearchKind.Catch, target = "bream", count = 3, a = 1.2f, money = 150, rod = 1 },
            new ResearchDef { id = "fin_fish_snapper_boat", de = "Roter Schnapper vom Boot, über 2 kg", en = "Red snapper from the boat, over 2 kg", kind = ResearchKind.Catch, target = "snapper", a = 2f, b = 1f, money = 400, rod = 2 },
            new ResearchDef { id = "fin_fish_tuna", de = "Gelbflossen-Thun, über 8 kg", en = "Yellowfin tuna, over 8 kg", kind = ResearchKind.Catch, target = "tuna", a = 8f, money = 1200, rod = 3 },
            new ResearchDef { id = "fin_fish_sword", de = "Schwertfisch für die Forschung", en = "A swordfish for science", kind = ResearchKind.Catch, target = "swordfish", money = 0, rod = 4 },
        };

        public static ResearchDef Get(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        public static string TitleOf(string id) { var d = Get(id); return d != null ? d.Title : id; }
    }
}
