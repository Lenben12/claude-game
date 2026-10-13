using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sealebrities
{
    /// A kind of underwater find (K.3): small finds glint in sand mounds (E „Graben“, 1.5 s), heavy salvage is carried
    /// (both hands) or winched aboard. The VALUE comes from the site table (FindSiteDef); the kind picks the flavour and
    /// where in the site's range it lands (valueMin01..valueMax01 – J.3b called these minValue/maxValue).
    public class FindDef
    {
        public string id;
        public string nameDe, nameEn;
        public bool heavy;
        public bool rare;
        /// Position inside the site's value range (0 = site min, 1 = site max; rare kinds may exceed 1 slightly).
        public float valueMin01, valueMax01 = 1f;
        /// Allowed sites (null = everywhere).
        public string[] sites;
        public float weight = 1f;

        public string Name => Loc.Tr(nameDe, nameEn);
    }

    /// Finds per site and day (K.3 table; numbers checked with econsim_k: house reef ≈ 35 $, kelp 90 $, wreck 220 $,
    /// blue drop 450 $, trench 900 $ per dive day incl. salvage).
    public class FindSiteDef
    {
        /// Dive-site id or "lagoon" (main island lagoon).
        public string siteId;
        public int smallCount; public long smallMin, smallMax;
        public int heavyCount; public long heavyMin, heavyMax;
    }

    /// One generated find of a day (deterministic from save seed + day + site – identical on every co-op machine).
    public struct FindSpawn
    {
        /// "find:{site}:{day}:{i}" (small) or "salv:{site}:{day}:{i}" (heavy) – also the GameState.finds id.
        public string id;
        public string siteId;
        public FindDef def;
        public long value;
        public bool heavy;
        /// Deterministic 0..1 numbers for the placing module (pick an anchor / angle / radius).
        public float u, v, w;
    }

    public static class FindsDatabase
    {
        /// "Lost action cam": adds a ready-made funny clip of this value to the taker's dive footage (Filming/props).
        public const float ActionCamClipValue = 30f;
        public const string LagoonSite = "lagoon";

        public static readonly List<FindDef> Kinds = new List<FindDef>
        {
            // ---- small finds
            new FindDef { id = "coins", nameDe = "Alte Münzen", nameEn = "Old Coins", valueMin01 = 0.2f, valueMax01 = 0.7f, weight = 3f },
            new FindDef { id = "sea_glass", nameDe = "Meerglas", nameEn = "Sea Glass", valueMin01 = 0f, valueMax01 = 0.4f, weight = 3f },
            new FindDef { id = "shark_tooth", nameDe = "Haizahn", nameEn = "Shark Tooth", valueMin01 = 0.3f, valueMax01 = 0.8f, weight = 2f,
                sites = new[] { "kelp_forest", "wreck", "blue_drop", "deep_trench", "fog_isle" } },
            new FindDef { id = "sunglasses", nameDe = "Verlorene Sonnenbrille", nameEn = "Lost Sunglasses", valueMin01 = 0.1f, valueMax01 = 0.5f, weight = 2f },
            new FindDef { id = "ring", nameDe = "Ring", nameEn = "Ring", rare = true, valueMin01 = 0.8f, valueMax01 = 1.15f, weight = 0.35f },
            new FindDef { id = "pearl", nameDe = "Perle", nameEn = "Pearl", rare = true, valueMin01 = 0.85f, valueMax01 = 1.2f, weight = 0.3f },
            new FindDef { id = "action_cam", nameDe = "Verlorene Action-Cam", nameEn = "Lost Action Cam", rare = true, valueMin01 = 0.5f, valueMax01 = 0.9f, weight = 0.25f },
            // ---- heavy salvage (carried with both hands or winched aboard)
            new FindDef { id = "ship_bell", nameDe = "Schiffsglocke", nameEn = "Ship's Bell", heavy = true, valueMin01 = 0.3f, valueMax01 = 0.8f, weight = 2f },
            new FindDef { id = "amphora", nameDe = "Amphore", nameEn = "Amphora", heavy = true, valueMin01 = 0.2f, valueMax01 = 0.7f, weight = 2f },
            new FindDef { id = "outboard", nameDe = "Außenbordmotor", nameEn = "Outboard Motor", heavy = true, valueMin01 = 0f, valueMax01 = 0.5f, weight = 2f },
            new FindDef { id = "tv_1980", nameDe = "Fernseher von 1987", nameEn = "1987 TV Set", heavy = true, valueMin01 = 0.1f, valueMax01 = 0.6f, weight = 1.5f },
            new FindDef { id = "ship_wheel", nameDe = "Steuerrad", nameEn = "Ship's Wheel", heavy = true, valueMin01 = 0.3f, valueMax01 = 0.9f, weight = 1.5f },
            new FindDef { id = "cannon", nameDe = "Kleine Kanone", nameEn = "Small Cannon", heavy = true, valueMin01 = 0.5f, valueMax01 = 1f, weight = 1f,
                sites = new[] { "wreck", "blue_drop", "deep_trench", "fog_isle" } },
            new FindDef { id = "brass_helmet", nameDe = "Messing-Taucherhelm", nameEn = "Brass Diving Helmet", heavy = true, valueMin01 = 0.6f, valueMax01 = 1f, weight = 1f },
            new FindDef { id = "sea_chest", nameDe = "Seekiste", nameEn = "Sea Chest", heavy = true, rare = true, valueMin01 = 0.85f, valueMax01 = 1.2f, weight = 0.4f },
        };

        public static readonly List<FindSiteDef> Sites = new List<FindSiteDef>
        {
            new FindSiteDef { siteId = LagoonSite, smallCount = 3, smallMin = 3, smallMax = 12 },
            new FindSiteDef { siteId = "house_reef", smallCount = 4, smallMin = 5, smallMax = 25, heavyCount = 1, heavyMin = 40, heavyMax = 80 },
            new FindSiteDef { siteId = "kelp_forest", smallCount = 4, smallMin = 10, smallMax = 40, heavyCount = 1, heavyMin = 90, heavyMax = 160 },
            new FindSiteDef { siteId = "wreck", smallCount = 5, smallMin = 20, smallMax = 60, heavyCount = 2, heavyMin = 150, heavyMax = 300 },
            new FindSiteDef { siteId = "blue_drop", smallCount = 3, smallMin = 40, smallMax = 120, heavyCount = 1, heavyMin = 300, heavyMax = 500 },
            new FindSiteDef { siteId = "deep_trench", smallCount = 3, smallMin = 100, smallMax = 300, heavyCount = 1, heavyMin = 600, heavyMax = 1200 },
            new FindSiteDef { siteId = "fog_isle", smallCount = 4, smallMin = 30, smallMax = 90, heavyCount = 1, heavyMin = 200, heavyMax = 400 },
        };

        public static FindDef Kind(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < Kinds.Count; i++) if (Kinds[i].id == id) return Kinds[i];
            return null;
        }

        public static FindSiteDef Site(string siteId)
        {
            if (siteId == null) return null;
            for (int i = 0; i < Sites.Count; i++) if (Sites[i].siteId == siteId) return Sites[i];
            return null;
        }

        static readonly List<FindDef> candidates = new List<FindDef>();

        /// Today's finds of a site (small first, then heavy). Deterministic for (saveSeed, day, site). Fills `into`.
        public static void Daily(string siteId, int day, int saveSeed, List<FindSpawn> into)
        {
            into.Clear();
            var site = Site(siteId);
            if (site == null) return;
            var rng = new Random(unchecked(saveSeed * 92821 + day * 7919 + StableHash(siteId)));
            for (int i = 0; i < site.smallCount; i++) into.Add(Roll(rng, site, day, i, false));
            for (int i = 0; i < site.heavyCount; i++) into.Add(Roll(rng, site, day, i, true));
        }

        static FindSpawn Roll(Random rng, FindSiteDef site, int day, int index, bool heavy)
        {
            candidates.Clear();
            float total = 0f;
            for (int k = 0; k < Kinds.Count; k++)
            {
                var d = Kinds[k];
                if (d.heavy != heavy || !Allowed(d, site.siteId)) continue;
                candidates.Add(d);
                total += d.weight;
            }
            FindDef pick = candidates.Count > 0 ? candidates[0] : null;
            double r = rng.NextDouble() * total;
            for (int k = 0; k < candidates.Count; k++) { r -= candidates[k].weight; if (r <= 0) { pick = candidates[k]; break; } }
            long min = heavy ? site.heavyMin : site.smallMin, max = heavy ? site.heavyMax : site.smallMax;
            float t = pick != null ? pick.valueMin01 + (pick.valueMax01 - pick.valueMin01) * (float)rng.NextDouble() : 0.5f;
            long value = Math.Max(1, (long)Math.Round(min + (max - min) * t));
            return new FindSpawn
            {
                id = (heavy ? "salv:" : "find:") + site.siteId + ":" + day.ToString(CultureInfo.InvariantCulture) + ":" + index.ToString(CultureInfo.InvariantCulture),
                siteId = site.siteId, def = pick, value = value, heavy = heavy,
                u = (float)rng.NextDouble(), v = (float)rng.NextDouble(), w = (float)rng.NextDouble()
            };
        }

        static readonly List<FindSpawn> resolveScratch = new List<FindSpawn>();

        /// Re-creates the spawn behind a find id ("find:wreck:12:3") – the host uses it to value a guest's find.
        public static bool TryResolve(string findId, int saveSeed, out FindSpawn spawn)
        {
            spawn = default;
            if (string.IsNullOrEmpty(findId)) return false;
            var parts = findId.Split(':');
            if (parts.Length != 4) return false;
            bool heavy = parts[0] == "salv";
            if (!heavy && parts[0] != "find") return false;
            if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)) return false;
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)) return false;
            Daily(parts[1], day, saveSeed, resolveScratch);
            for (int i = 0; i < resolveScratch.Count; i++)
            {
                if (resolveScratch[i].id != findId) continue;
                spawn = resolveScratch[i];
                return true;
            }
            return false;
        }

        static bool Allowed(FindDef d, string siteId)
        {
            if (d.sites == null) return true;
            for (int i = 0; i < d.sites.Length; i++) if (d.sites[i] == siteId) return true;
            return false;
        }

        /// String hash that is identical on every machine/runtime (string.GetHashCode is not).
        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h;
            }
        }
    }
}
