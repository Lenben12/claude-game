using System;
using UnityEngine;

namespace Sealebrities
{
    /// Topic tags of a species, shared by the photo challenges (K.3), the sponsor deals (K.7, the livestream engineer's
    /// SponsorService) and the stream chat: "shark", "ray", "turtle", "octopus" (incl. cuttlefish and squid), "whale"
    /// (humpback, whale shark), "dolphin", "crab", "school", "deep", "night", "coral", "wreck", "island"/"land", "bird",
    /// "glow", "predator". Any other tag is compared with the species id ("moray", "green_turtle" …). Allocation free.
    public static class FilmTags
    {
        public static bool Matches(SpeciesDef sp, string tag)
        {
            if (sp == null || string.IsNullOrEmpty(tag)) return false;
            switch (tag)
            {
                case "shark": return sp.plan == BodyPlan.Shark || sp.id.EndsWith("_shark", StringComparison.Ordinal);
                case "ray": return sp.plan == BodyPlan.Ray;
                case "turtle": return sp.plan == BodyPlan.Turtle;
                case "octopus":
                    return sp.plan == BodyPlan.Octopus || sp.plan == BodyPlan.Cuttlefish || sp.plan == BodyPlan.Squid || sp.plan == BodyPlan.VampireSquid;
                case "whale": return sp.plan == BodyPlan.Whale || sp.id == "humpback" || sp.id == "whale_shark";
                case "dolphin": return sp.plan == BodyPlan.Dolphin || sp.id == "dolphin";
                case "crab": return sp.plan == BodyPlan.Crab || sp.id.IndexOf("crab", StringComparison.Ordinal) >= 0;
                case "school": return sp.behavior == BehaviorType.Schooling;
                case "deep": return sp.Page == "deep_trench" || sp.minDepth >= 40f;
                case "night": return sp.nightWeight > 1f || (sp.dayMask & DayMask.Day) == 0;
                case "coral": return !sp.land && (sp.Page == "house_reef" || sp.Page == "fog_isle");
                case "wreck": return sp.HasZone(Zones.Wreck);
                case "island":
                case "land": return sp.land;
                case "bird": return sp.plan == BodyPlan.Bird;
                case "glow": return sp.glows;
                case "predator": return sp.IsDangerous;
                default: return string.Equals(sp.id, tag, StringComparison.Ordinal);
            }
        }
    }

    /// The open-world zone (L.1 ids: dive-site ids, "lagoon", "open_sea", "deep_sea", "island", "tide_pools", "beach")
    /// of a position, for the Sealog "Fundorte" bits (JournalEntry.zoneMask, L.2). Mirrors the fauna module's L.1 rules
    /// (site radius + 60 m, lagoon = within island radius + 90 m and ≤ 8 m deep, open sea < 40 m deep) without its grid
    /// cache – the camera asks once per detection tick. Allocation free.
    public static class FilmZones
    {
        public const float SiteMargin = 60f, LagoonMargin = 90f, LagoonMaxDepth = 8f, DeepSeaDepth = 40f, TidePoolRadius = 16f;

        public static string ZoneAt(Vector3 p)
        {
            float ground = WorldInfo.GroundHeight(p);
            if (float.IsNaN(ground)) ground = -60f;
            Vector3 tp = PropInfo.TidePoolsCenter != Vector3.zero ? PropInfo.TidePoolsCenter : WorldLayout.TidePools;
            float dtp = Flat(p - tp);
            bool onLand = ground > Waves.SeaLevel - 0.15f && p.y >= Waves.SeaLevel - 0.3f;
            if (onLand)
            {
                if (dtp < TidePoolRadius) return Zones.TidePools;
                return ground < Waves.SeaLevel + 1.6f ? Zones.Beach : Zones.Island;
            }
            if (dtp < TidePoolRadius) return Zones.TidePools;

            DiveSite best = null;
            float bestOver = float.MaxValue;
            var sites = DiveSites.All;
            for (int i = 0; i < sites.Count; i++)
            {
                var s = sites[i];
                if (s == null) continue;
                float d = Flat(p - s.center);
                float over = d - s.radius;
                if (over < SiteMargin && over < bestOver) { bestOver = over; best = s; }
            }
            if (best != null) return best.id;

            float seafloor = Mathf.Max(0f, Waves.SeaLevel - ground);
            float r = Flat(p - WorldLayout.IslandCenter);
            if (r < WorldLayout.IslandRadius + LagoonMargin && seafloor <= LagoonMaxDepth) return Zones.Lagoon;
            return seafloor < DeepSeaDepth ? Zones.OpenSea : Zones.DeepSea;
        }

        static float Flat(Vector3 d) => Mathf.Sqrt(d.x * d.x + d.z * d.z);
    }
}
