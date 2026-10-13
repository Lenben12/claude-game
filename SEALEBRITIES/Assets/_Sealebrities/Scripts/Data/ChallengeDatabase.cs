using System.Collections.Generic;

namespace Sealebrities
{
    /// A daily photo/film challenge (K.3). 2 per day (shared, seeded – Challenges.Today), shown in the Tasks app and on
    /// the site buoy signs. The Filming module checks photos/clips against `kind` + `args` and calls Challenges.Complete.
    public class ChallengeDef
    {
        public string id;
        public string de, en;
        /// What Filming checks:
        ///   "PhotoPitch"     – photo subject of species/tag args[0], camera pitch below args[1] degrees (−45)
        ///   "PhotoSpecies"   – ≥ args[0] different species in one photo
        ///   "PhotoFill"      – a subject ≤ args[0] cm filling ≥ args[1] of the frame (0.5)
        ///   "FrameCount"     – ≥ args[0] animals detected at once
        ///   "PhotoNightLamp" – photo between 21:00 and 04:00 with the lamp on and a subject
        ///   "PhotoSurface"   – subject species args[0] within args[1] m of the surface
        ///   "PhotoTagNear"   – subject with tag args[0] ("shark") within args[1] m
        ///   "SelfieCount"    – selfie photo with ≥ args[0] animals
        ///   "Record"         – a new size record (Sealog photo)
        ///   "Tier"           – subject of tier ≥ args[0] (3 = Huge)
        ///   "PhotoGlowDeep"  – glowing subject below args[0] m
        ///   "PhotoEvent"     – photo during an animal event (FaunaEventInfo)
        ///   "PhotoLand"      – land animal (SpeciesDef.land) within args[0] m
        ///   "PhotoLanterns"  – photo with ≥ args[0] lanterns in frame (Lantern Evening, L.8)
        ///   "PhotoInWreck"   – subject inside the wreck interior (PropInfo.WreckInterior)
        ///   "PhotoBow"       – dolphin in frame while the boat runs ≥ args[0] m/s
        public string kind;
        public string[] args;
        /// Only rolled once this dive site is unlocked (null = always possible).
        public string needsSite;
        /// Only rolled on Lantern Evening days (L.8).
        public bool lanternOnly;

        public string Title => Loc.Tr(de, en);
    }

    public static class ChallengeDatabase
    {
        /// Challenges per day (K.3).
        public const int PerDay = 2;
        /// Reward = clamp(RewardShare × ExpectedUploadMoney(F), RewardMin, RewardMax) $ + ViewsBonus on today's upload.
        public const float RewardShare = 0.3f;
        public const long RewardMin = 40, RewardMax = 400;
        public const float ViewsBonus = 0.10f;

        public static readonly List<ChallengeDef> All = new List<ChallengeDef>
        {
            C("ch_ray_top", "Ein Rochen von oben", "A ray from above", "PhotoPitch", null, "ray", "-45"),
            C("ch_three", "3 Arten auf einem Foto", "3 species in one photo", "PhotoSpecies", null, "3"),
            C("ch_macro", "Winzling ganz groß (≥ 50 % Bild)", "Tiny one, huge (≥ 50 % of the frame)", "PhotoFill", null, "15", "0.5"),
            C("ch_school20", "Schwarm: 20+ Fische im Bild", "School: 20+ fish in frame", "FrameCount", null, "20"),
            C("ch_lamp_night", "Tier im Lampenlicht bei Nacht", "Animal in lamplight at night", "PhotoNightLamp", null),
            C("ch_turtle_surface", "Gerald beim Luftholen", "Gerald taking a breath", "PhotoSurface", null, "green_turtle", "2"),
            C("ch_shark_eye", "Hai auf Augenhöhe (≤ 4 m)", "Shark at eye level (≤ 4 m)", "PhotoTagNear", "kelp_forest", "shark", "4"),
            C("ch_selfie_reef", "Selfie mit 5 Tieren", "Selfie with 5 animals", "SelfieCount", null, "5"),
            C("ch_record", "Ein neuer Größenrekord", "A new size record", "Record", null),
            C("ch_legend_tier", "Ein Riesig- oder Legendär-Tier", "A Huge or Legendary animal", "Tier", null, "3"),
            C("ch_deep_glow", "Leuchtendes Tier unter 40 m", "Glowing animal below 40 m", "PhotoGlowDeep", "blue_drop", "40"),
            C("ch_event", "Ein Foto während eines Tier-Events", "A photo during an animal event", "PhotoEvent", null),
            C("ch_land", "Ein Inseltier aus 2 m", "An island animal from 2 m", "PhotoLand", null, "2"),
            new ChallengeDef { id = "ch_village", de = "Laternen auf dem Wasser", en = "Lanterns on the water", kind = "PhotoLanterns", args = new[] { "5" }, lanternOnly = true },
            C("ch_wreck_inside", "Tier im Wrack", "Animal inside the wreck", "PhotoInWreck", "wreck"),
            C("ch_dolphin_bow", "Delfin an der Bugwelle", "Dolphin at the bow", "PhotoBow", "kelp_forest", "6"),
        };

        static ChallengeDef C(string id, string de, string en, string kind, string needsSite, params string[] args) =>
            new ChallengeDef { id = id, de = de, en = en, kind = kind, needsSite = needsSite, args = args ?? new string[0] };

        static Dictionary<string, ChallengeDef> byId;

        public static ChallengeDef Get(string id)
        {
            if (id == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, ChallengeDef>();
                foreach (var c in All) byId[c.id] = c;
            }
            return byId.TryGetValue(id, out var d) ? d : null;
        }
    }
}
