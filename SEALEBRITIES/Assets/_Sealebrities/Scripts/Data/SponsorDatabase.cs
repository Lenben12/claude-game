using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Sponsor size (K.7): local from 250 followers, national from 5,000, big brands from 50,000.
    public enum SponsorTier { Local, National, Big }

    /// K.7 deal condition types – checked by the livestream module's SponsorService (host decides, every machine
    /// reports what its own player did).
    public enum DealCondition
    {
        /// Clips of matching animals: count (or distinct species, or filmed seconds), optional min depth / site / crowd.
        FilmSpecies,
        /// An upload WITH `requireSpecies` and WITHOUT animals of `forbidTag` completes it – an upload with the
        /// forbidden tag breaks the deal (fail + angry mail).
        NoTagInUpload,
        /// Sealog photos of matching animals: count or distinct species, optional site / inside the wreck.
        PhotoTag,
        /// Seconds under water deeper than `minDepth`.
        DiveDepth,
        /// Seconds under water between 21:00 and 04:00.
        NightDive,
        /// Uploads (one per day) with the title style `style`.
        UploadStyle,
        /// Livestream seconds with at least `minViewers` viewers.
        StreamMinutes,
        /// Fish caught by the crew.
        CatchFish,
        /// Dive at the dive site `site`.
        VisitSite,
        /// Selfie clip or photo with matching animals (`minCrowd` other animals in frame).
        SelfieWith,
        /// Snacks eaten – completed by an upload made after that.
        EatFood,
        /// Order a chopper delivery AND film the chopper (two parts, any order).
        OrderAndFilm
    }

    /// A fictional sponsor (never a real brand).
    public sealed class SponsorDef
    {
        public string id, nameDe, nameEn;
        public SponsorTier tier;
        /// Logo colour (boat flag / suit patch, Q90) and the 1–2 letter fantasy logo.
        public Color color;
        public string logo;
        public string sloganDe, sloganEn;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Slogan => Loc.Tr(sloganDe, sloganEn);
    }

    /// A deal template (K.7 table). Offers are DealState instances in GameState.sponsor (core).
    public sealed class DealDef
    {
        public string id, sponsorId;
        /// Topic tags (English ids, see Deals.TagName) – two ACTIVE deals may not share one.
        public string[] tags;
        public DealCondition condition;
        /// Free-form argument summary of the spec table (documentation; the typed fields below are what counts).
        public string args;
        public int days;
        /// Reward factor: 0.8 easy / 1.0 normal / 1.4 hard.
        public float k;

        /// Count / seconds needed.
        public float target = 1f;
        /// Allowed species ids (null = any) …
        public string[] species;
        /// … or allowed body plans (null = any). Both null = every animal.
        public BodyPlan[] plans;
        /// Only island land animals (L.11).
        public bool landOnly;
        /// Count distinct species instead of clips/photos.
        public bool distinct;
        /// FilmSpecies: count filmed seconds instead of clips.
        public bool seconds;
        /// Minimum depth in meters (film/photo/dive).
        public float minDepth;
        /// Dive site / Sealog page filter (null = anywhere).
        public string site;
        /// Clip / selfie must show at least this many OTHER animals at once (FilmSpecies crowd, SelfieWith).
        public int minCrowd;
        /// PhotoTag: only photos taken inside the wreck.
        public bool inWreck;
        /// Reached → the deal completes with the next upload (that contains a matching clip).
        public bool needsUpload;
        /// NoTagInUpload.
        public string forbidTag, requireSpecies;
        /// UploadStyle.
        public TitleStyle style;
        /// StreamMinutes.
        public int minViewers;
        /// Offered only when this dive site is unlocked (null = always).
        public string requiresSite;

        /// Short task line (quest line, ≤ 32 characters) and the longer sponsor text for the Deals app.
        public string titleDe, titleEn, descDe, descEn;

        public string Title => Loc.Tr(titleDe, titleEn);
        public string Description => Loc.Tr(descDe, descEn);
        public SponsorDef Sponsor => SponsorDatabase.GetSponsor(sponsorId);

        public bool HasTag(string tag)
        {
            if (tags == null || tag == null) return false;
            for (int i = 0; i < tags.Length; i++) if (tags[i] == tag) return true;
            return false;
        }
    }

    /// K.7 sponsor deals – data and balance numbers (constants owner: livestream, J.5).
    /// Reward = round5(max(MinBase, share × ExpectedUploadMoney(F)) × days × k), share 0.45 below 5,000 followers and
    /// 0.25 from 5,000 (econsim_k: two slots ≈ +0.38 × upload money per day at 75 % completion).
    public static class SponsorDatabase
    {
        public const long MinBase = 70;
        public const float ShareSmall = 0.45f, ShareBig = 0.25f;
        public const float KEasy = 0.8f, KNormal = 1.0f, KHard = 1.4f;
        /// New offers per morning (max Deals.MaxOffers open).
        public const int OffersPerDay = 1;
        /// Open offers the morning tries to keep (a fresh channel gets two to choose from).
        public const int KeepOpenOffers = 2;

        static Color C(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static readonly List<SponsorDef> Sponsors = new List<SponsorDef>
        {
            // ---------------- local (250+)
            new SponsorDef { id = "horst_rolls", nameDe = "Horsts Fischbrötchen", nameEn = "Horst's Fish Rolls", tier = SponsorTier.Local,
                color = C("#E07B39"), logo = "HF", sloganDe = "Mit extra Engagement.", sloganEn = "With extra engagement." },
            new SponsorDef { id = "wanda_whirl", nameDe = "Wandas Wirbel-Lieferdienst", nameEn = "Wanda's Whirl Delivery", tier = SponsorTier.Local,
                color = C("#E0607E"), logo = "WW", sloganDe = "Meistens heil.", sloganEn = "Mostly in one piece." },
            new SponsorDef { id = "oldreef_weavers", nameDe = "Altriff Webstube", nameEn = "Oldreef Weavers", tier = SponsorTier.Local,
                color = C("#6E9445"), logo = "AW", sloganDe = "Seit Generationen gut vernetzt.", sloganEn = "Well connected for generations." },
            // ---------------- national (5,000+)
            new SponsorDef { id = "crabby", nameDe = "Krabbenkeks Co.", nameEn = "Crabby Cookies", tier = SponsorTier.National,
                color = C("#F28C4A"), logo = "KK", sloganDe = "Knackt jede Schale.", sloganEn = "Cracks every shell." },
            new SponsorDef { id = "finfix", nameDe = "Flossenfix", nameEn = "FinFix", tier = SponsorTier.National,
                color = C("#3FB8C4"), logo = "FF", sloganDe = "Schneller als dein Schatten.", sloganEn = "Faster than your shadow." },
            new SponsorDef { id = "sharksoda", nameDe = "SharkSoda", nameEn = "SharkSoda", tier = SponsorTier.National,
                color = C("#4F7FAE"), logo = "SS", sloganDe = "Beißt zurück.", sloganEn = "Bites back." },
            new SponsorDef { id = "turtletogs", nameDe = "Schildi-Schlafanzüge", nameEn = "TurtleTogs", tier = SponsorTier.National,
                color = C("#2F8F6E"), logo = "TT", sloganDe = "Schlaf wie ein Panzer.", sloganEn = "Sleep like a shell." },
            new SponsorDef { id = "goggle_optics", nameDe = "Glubsch-Optik", nameEn = "Goggle Optics", tier = SponsorTier.National,
                color = C("#8E6CD9"), logo = "GO", sloganDe = "Sieh mehr Meer.", sloganEn = "See more sea." },
            new SponsorDef { id = "tentacle_tours", nameDe = "Tentakel Tours", nameEn = "Tentacle Tours", tier = SponsorTier.National,
                color = C("#A8478F"), logo = "T8", sloganDe = "Acht Arme, ein Erlebnis.", sloganEn = "Eight arms, one adventure." },
            new SponsorDef { id = "mantastic", nameDe = "Mantastic Sonnencreme", nameEn = "Mantastic Sunscreen", tier = SponsorTier.National,
                color = C("#F2C94C"), logo = "M", sloganDe = "Schützt bis Lichtschutzfaktor Manta.", sloganEn = "Protects up to SPF Manta." },
            // ---------------- big brands (50,000+)
            new SponsorDef { id = "deepbyte", nameDe = "DeepByte Kameras", nameEn = "DeepByte Cameras", tier = SponsorTier.Big,
                color = C("#3A3F5C"), logo = "DB", sloganDe = "Jedes Pixel nass.", sloganEn = "Every pixel wet." },
            new SponsorDef { id = "blubber", nameDe = "Blubber Energy", nameEn = "Blubber Energy", tier = SponsorTier.Big,
                color = C("#C94F3D"), logo = "BE", sloganDe = "Gibt dir Flossen.", sloganEn = "Gives you fins." },
        };

        static string[] T(params string[] t) => t;

        /// The K.7 table (+ four extra deals so the local tier and the selfie/visit conditions have variety).
        public static readonly List<DealDef> All = new List<DealDef>
        {
            // ======================= local
            new DealDef { id = "d_fishroll", sponsorId = "horst_rolls", tags = T("fishing"), condition = DealCondition.CatchFish, args = "catch 8 fish",
                days = 1, k = KEasy, target = 8,
                titleDe = "8 Fische angeln", titleEn = "Catch 8 fish",
                descDe = "Horst braucht Nachschub für seine Fischbrötchen. Egal welche Fische!",
                descEn = "Horst needs supplies for his fish rolls. Any fish will do!" },
            new DealDef { id = "d_wanda_express", sponsorId = "wanda_whirl", tags = T("island"), condition = DealCondition.OrderAndFilm,
                args = "order 1 delivery and film the chopper landing", days = 1, k = KEasy, target = 2,
                titleDe = "Lieferung + Heli filmen", titleEn = "Delivery + film the chopper",
                descDe = "Bestell irgendwas im Chopper Shop und film Wandas Heli beim Anflug.",
                descEn = "Order anything in the Chopper Shop and film Wanda's chopper flying in." },
            new DealDef { id = "d_weavers", sponsorId = "oldreef_weavers", tags = T("island", "coral"), condition = DealCondition.PhotoTag,
                args = "photograph 3 coral-reef species at the house reef", days = 2, k = KNormal, target = 3, distinct = true, site = "house_reef",
                titleDe = "3 Riffarten am Hausriff fotografieren", titleEn = "Photograph 3 house-reef species",
                descDe = "Die Webstube webt Riff-Muster. Sie braucht Vorlagen: 3 verschiedene Arten am Hausriff fürs Sealog.",
                descEn = "The weavers make reef patterns and need models: 3 different species at the house reef, for the Sealog." },
            new DealDef { id = "d_reef_tour", sponsorId = "oldreef_weavers", tags = T("coral"), condition = DealCondition.FilmSpecies,
                args = "film 5 species at the house reef", days = 2, k = KNormal, target = 5, distinct = true, site = "house_reef",
                titleDe = "5 Arten am Hausriff filmen", titleEn = "Film 5 house-reef species",
                descDe = "Eine kleine Riff-Tour für die Webstube: 5 verschiedene Arten am Hausriff vor die Kamera.",
                descEn = "A little reef tour for the weavers: get 5 different species on camera at the house reef." },
            new DealDef { id = "d_island_life", sponsorId = "horst_rolls", tags = T("island"), condition = DealCondition.FilmSpecies,
                args = "film 2 island animals", days = 2, k = KEasy, target = 2, landOnly = true,
                titleDe = "2 Inseltiere filmen", titleEn = "Film 2 island animals",
                descDe = "Horst will „Lokalkolorit“: Film zwei Tiere an Land. Möwen zählen. Günther besonders.",
                descEn = "Horst wants \"local colour\": film two animals on land. Seagulls count. Günther especially." },

            // ======================= national
            new DealDef { id = "d_crabby", sponsorId = "crabby", tags = T("island"), condition = DealCondition.FilmSpecies, args = "film 3 crabs (any crab species)",
                days = 1, k = KEasy, target = 3, plans = new[] { BodyPlan.Crab },
                titleDe = "3 Krabben filmen", titleEn = "Film 3 crabs",
                descDe = "Krabbenkeks sucht neue Gesichter für die Packung. Film 3 Krabben – jede Art zählt.",
                descEn = "Crabby Cookies needs new faces for the box. Film 3 crabs – any species." },
            new DealDef { id = "d_snack", sponsorId = "crabby", tags = T("island"), condition = DealCondition.EatFood,
                args = "eat 2 Crabby snacks (pantry) and upload that day", days = 1, k = KEasy, target = 2, needsUpload = true,
                titleDe = "2 Snacks essen + hochladen", titleEn = "Eat 2 snacks + upload",
                descDe = "Iss 2 Snacks aus dem Kühlschrank und lad danach ein Video hoch. Genießen sieht man!",
                descEn = "Eat 2 snacks from the fridge, then upload a video. Enjoyment shows!" },
            new DealDef { id = "d_shark3", sponsorId = "sharksoda", tags = T("shark"), condition = DealCondition.FilmSpecies,
                args = "3 shark clips in one upload", days = 3, k = KNormal, target = 3, plans = new[] { BodyPlan.Shark }, needsUpload = true,
                titleDe = "3 Hai-Clips hochladen", titleEn = "Upload 3 shark clips",
                descDe = "SharkSoda will Zähne sehen: 3 Hai-Clips filmen und hochladen.",
                descEn = "SharkSoda wants to see teeth: film 3 shark clips and upload them." },
            new DealDef { id = "d_noshark", sponsorId = "turtletogs", tags = T("turtle", "shark"), condition = DealCondition.NoTagInUpload,
                args = "upload with Gerald and no shark", days = 2, k = KNormal, target = 1, requireSpecies = "green_turtle", forbidTag = "shark",
                titleDe = "Video mit Gerald, ohne Hai", titleEn = "Video with Gerald, no shark",
                descDe = "Gemütlich soll es sein: ein Video mit Gerald – und kein einziger Hai darin.",
                descEn = "Keep it cosy: a video with Gerald – and not a single shark in it." },
            new DealDef { id = "d_turtle_selfie", sponsorId = "turtletogs", tags = T("turtle"), condition = DealCondition.SelfieWith,
                args = "selfie with Gerald", days = 2, k = KNormal, target = 1, species = T("green_turtle"),
                titleDe = "Selfie mit Gerald", titleEn = "Selfie with Gerald",
                descDe = "Schildi-Schlafanzüge will Gerald und dich in einem Bild. Selfie-Ansicht, Kamera an!",
                descEn = "TurtleTogs wants Gerald and you in one shot. Selfie view, camera rolling!" },
            new DealDef { id = "d_ray_photo", sponsorId = "mantastic", tags = T("ray"), condition = DealCondition.PhotoTag,
                args = "photograph 2 ray species", days = 3, k = KNormal, target = 2, distinct = true, plans = new[] { BodyPlan.Ray },
                titleDe = "2 Rochenarten fotografieren", titleEn = "Photograph 2 ray species",
                descDe = "Für die neue Werbung: Fotos von 2 verschiedenen Rochenarten fürs Sealog.",
                descEn = "For the new ad: Sealog photos of 2 different ray species." },
            new DealDef { id = "d_manta", sponsorId = "mantastic", tags = T("ray", "depth"), condition = DealCondition.FilmSpecies,
                args = "film a manta deeper than 20 m", days = 3, k = KHard, target = 1, species = T("manta"), minDepth = 20f,
                titleDe = "Manta unter 20 m filmen", titleEn = "Film a manta below 20 m",
                descDe = "Der Star der Marke – aber bitte aus der Tiefe. Film einen Manta unter 20 m.",
                descEn = "The brand's star – from the deep, please. Film a manta below 20 m." },
            new DealDef { id = "d_octo", sponsorId = "tentacle_tours", tags = T("octopus"), condition = DealCondition.FilmSpecies,
                args = "film an octopus or cuttlefish for 8 s", days = 2, k = KNormal, target = 8, seconds = true,
                plans = new[] { BodyPlan.Octopus, BodyPlan.Cuttlefish },
                titleDe = "Krake oder Sepia 8 s filmen", titleEn = "Film an octopus for 8 s",
                descDe = "Acht Arme, acht Sekunden: Film eine Krake oder Sepia insgesamt 8 Sekunden lang.",
                descEn = "Eight arms, eight seconds: film an octopus or cuttlefish for 8 seconds in total." },
            new DealDef { id = "d_tour_wreck", sponsorId = "tentacle_tours", tags = T("wreck"), condition = DealCondition.VisitSite,
                args = "dive at the wreck", days = 2, k = KEasy, target = 1, site = "wreck", requiresSite = "wreck",
                titleDe = "Am Wrack tauchen", titleEn = "Dive at the wreck",
                descDe = "Tentakel Tours plant Wrack-Touren. Tauch zur MS Clickbait und zeig, wie schön es dort ist.",
                descEn = "Tentacle Tours is planning wreck tours. Dive to the MS Clickbait and show how pretty it is." },
            new DealDef { id = "d_school", sponsorId = "finfix", tags = T("school"), condition = DealCondition.FilmSpecies,
                args = "clip with ≥ 20 fish detected", days = 2, k = KNormal, target = 1, minCrowd = 20,
                titleDe = "20+ Fische in einem Clip", titleEn = "20+ fish in one clip",
                descDe = "Flossenfix liebt Schwärme: Film 20 oder mehr Fische gleichzeitig im Bild.",
                descEn = "FinFix loves schools: film 20 or more fish in frame at the same time." },
            new DealDef { id = "d_deep30", sponsorId = "finfix", tags = T("depth"), condition = DealCondition.DiveDepth,
                args = "60 s below 30 m", days = 2, k = KNormal, target = 60, minDepth = 30f, requiresSite = "wreck",
                titleDe = "60 s tiefer als 30 m", titleEn = "60 s below 30 m",
                descDe = "Zeig, was die Flossen können: 60 Sekunden tiefer als 30 m tauchen.",
                descEn = "Show what the fins can do: spend 60 seconds deeper than 30 m." },
            new DealDef { id = "d_night", sponsorId = "goggle_optics", tags = T("night"), condition = DealCondition.NightDive,
                args = "dive 3 min between 21:00 and 04:00", days = 2, k = KNormal, target = 180,
                titleDe = "3 Min. nachts tauchen", titleEn = "Dive 3 min at night",
                descDe = "Glubsch-Optik sieht auch im Dunkeln: Tauch 3 Minuten zwischen 21 und 4 Uhr.",
                descEn = "Goggle Optics sees in the dark too: dive for 3 minutes between 9 pm and 4 am." },
            new DealDef { id = "d_logo_selfie", sponsorId = "goggle_optics", tags = T("coral"), condition = DealCondition.SelfieWith,
                args = "selfie with our logo and 3 animals", days = 2, k = KNormal, target = 1, minCrowd = 2,
                titleDe = "Logo-Selfie mit 3 Tieren", titleEn = "Logo selfie with 3 animals",
                descDe = "Das Logo sitzt auf deiner Schulter. Ein Selfie unter Wasser mit mindestens 3 Tieren im Bild!",
                descEn = "The logo is on your shoulder. An underwater selfie with at least 3 animals in frame!" },

            // ======================= big brands
            new DealDef { id = "d_wreck_inside", sponsorId = "deepbyte", tags = T("wreck"), condition = DealCondition.PhotoTag,
                args = "3 animals photographed inside the wreck", days = 3, k = KHard, target = 3, inWreck = true, requiresSite = "wreck",
                titleDe = "3 Tiere im Wrack fotografieren", titleEn = "Photograph 3 animals in the wreck",
                descDe = "DeepByte testet das Dunkel: 3 Sealog-Fotos im Inneren der MS Clickbait.",
                descEn = "DeepByte is testing the dark: 3 Sealog photos inside the MS Clickbait." },
            new DealDef { id = "d_honest", sponsorId = "deepbyte", tags = T("livestream"), condition = DealCondition.UploadStyle,
                args = "2 uploads with honest titles", days = 3, k = KEasy, target = 2, style = TitleStyle.Honest,
                titleDe = "2 Videos mit ehrlichem Titel", titleEn = "2 videos with honest titles",
                descDe = "Glaubwürdigkeit verkauft Kameras: 2 Videos mit ehrlichem Titel hochladen.",
                descEn = "Credibility sells cameras: upload 2 videos with honest titles." },
            new DealDef { id = "d_stream20", sponsorId = "blubber", tags = T("livestream"), condition = DealCondition.StreamMinutes,
                args = "stream 8 min with ≥ 200 viewers", days = 2, k = KHard, target = 480, minViewers = 200,
                titleDe = "8 Min. live mit 200+ Zuschauern", titleEn = "8 min live with 200+ viewers",
                descDe = "Blubber Energy will Reichweite: 8 Minuten KRILL.tv mit mindestens 200 Zuschauern.",
                descEn = "Blubber Energy wants reach: 8 minutes on KRILL.tv with at least 200 viewers." },
            new DealDef { id = "d_whale", sponsorId = "blubber", tags = T("whale"), condition = DealCondition.FilmSpecies,
                args = "film Big Bertha or the whale shark", days = 4, k = KHard, target = 1, species = T("humpback", "whale_shark"),
                titleDe = "Big Bertha oder Walhai filmen", titleEn = "Film Big Bertha or a whale shark",
                descDe = "Groß denken! Film den Buckelwal Big Bertha oder einen Walhai.",
                descEn = "Think big! Film Big Bertha the humpback or a whale shark." },
        };

        static Dictionary<string, DealDef> byId;
        static Dictionary<string, SponsorDef> sponsorById;

        static void Ensure()
        {
            if (byId != null) return;
            byId = new Dictionary<string, DealDef>();
            for (int i = 0; i < All.Count; i++) byId[All[i].id] = All[i];
            sponsorById = new Dictionary<string, SponsorDef>();
            for (int i = 0; i < Sponsors.Count; i++) sponsorById[Sponsors[i].id] = Sponsors[i];
        }

        public static DealDef Get(string dealId)
        {
            Ensure();
            return dealId != null && byId.TryGetValue(dealId, out var d) ? d : null;
        }

        public static SponsorDef GetSponsor(string sponsorId)
        {
            Ensure();
            return sponsorId != null && sponsorById.TryGetValue(sponsorId, out var s) ? s : null;
        }

        /// Followers needed before a sponsor of this tier sends offers.
        public static long FollowersFor(SponsorTier t) =>
            t == SponsorTier.Local ? Progression.LocalSponsorFollowers
            : t == SponsorTier.National ? Progression.NationalSponsorFollowers
            : Progression.BigSponsorFollowers;

        /// K.7 reward of a deal at this follower count.
        public static long Reward(DealDef d, long followers)
        {
            if (d == null) return 0;
            float share = followers < Progression.NationalSponsorFollowers ? ShareSmall : ShareBig;
            float baseMoney = Mathf.Max(MinBase, share * Progression.ExpectedUploadMoney(followers));
            float raw = baseMoney * Mathf.Max(1, d.days) * Mathf.Max(0.1f, d.k);
            return (long)Mathf.Max(5f, Mathf.Round(raw / 5f) * 5f);
        }

        /// Does this species belong to a topic tag? (K.7 tags: shark, turtle, ray, octopus, school, wreck, night, depth,
        /// fishing, livestream, island, coral, whale – only the animal topics match species.)
        public static bool TagMatches(SpeciesDef s, string tag)
        {
            if (s == null || tag == null) return false;
            switch (tag)
            {
                case "shark": return s.plan == BodyPlan.Shark;
                case "turtle": return s.plan == BodyPlan.Turtle;
                case "ray": return s.plan == BodyPlan.Ray;
                case "octopus": return s.plan == BodyPlan.Octopus || s.plan == BodyPlan.Cuttlefish || s.plan == BodyPlan.Squid || s.plan == BodyPlan.VampireSquid;
                case "whale": return s.plan == BodyPlan.Whale || s.id == "whale_shark";
                case "school": return s.behavior == BehaviorType.Schooling;
                case "coral": return s.Page == "house_reef";
                case "island": return s.land;
                default: return false;
            }
        }

        /// Does this animal count for the deal's species / plan / land filter?
        public static bool Matches(DealDef d, SpeciesDef s)
        {
            if (d == null || s == null) return false;
            if (d.landOnly && !s.land) return false;
            if (d.species != null)
            {
                bool ok = false;
                for (int i = 0; i < d.species.Length; i++) if (d.species[i] == s.id) { ok = true; break; }
                if (!ok) return false;
            }
            if (d.plans != null)
            {
                bool ok = false;
                for (int i = 0; i < d.plans.Length; i++) if (d.plans[i] == s.plan) { ok = true; break; }
                if (!ok) return false;
            }
            return true;
        }

        /// True when at least one matching animal can currently spawn (its gate is open) – offers stay achievable.
        public static bool AnyMatchingSpeciesAvailable(DealDef d)
        {
            if (d == null) return false;
            if (d.species == null && d.plans == null && !d.landOnly) return true;
            var all = SpeciesDatabase.All;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (!Matches(d, s)) continue;
                if (Progression.IsGateOpen(s)) return true;
            }
            return false;
        }
    }
}
