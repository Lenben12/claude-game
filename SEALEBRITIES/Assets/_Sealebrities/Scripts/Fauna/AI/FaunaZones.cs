using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Open-world spawn zones (L.1) and the population numbers of the spawner (J.5: spawn targets and zone margins live
    /// here). ZoneAt is deterministic (terrain + DiveSites + WorldLayout only) and cached per 16 m grid cell.
    ///   site id   – inside the site radius + ZoneMargin, or within LandmarkShoreRange of the site's landmark island
    ///   lagoon    – around the main island (r < IslandRadius + LagoonExtra) where the seafloor is ≤ LagoonMaxDepth
    ///   open_sea  – any other water whose seafloor is shallower than DeepSeaDepth
    ///   deep_sea  – any other water at least DeepSeaDepth deep
    ///   island    – land (ground above sea level)
    public static class FaunaZones
    {
        public const float CellSize = 16f;
        public const float ZoneMargin = 60f;
        public const float LandmarkShoreRange = 70f;
        public const float LagoonExtra = 90f;
        public const float LagoonMaxDepth = 8f;
        public const float DeepSeaDepth = 40f;
        /// Animals within this ring around a diver count for the targets.
        public const float Ring = 80f;
        /// Co-op: at most this many animals around one diver.
        public const int MaxPerDiverCoop = 64;
        /// Locked site: open-water species at this share of the open-sea density.
        public const float LockedDensity = 0.5f;
        public const float NightFactor = 0.85f;
        /// Keep this many surface animals within SurfaceRange of a diver at the surface / on the boat (open sea, lagoon).
        public const int SurfaceTarget = 6;
        public const float SurfaceRange = 40f;

        static readonly Dictionary<long, string> cache = new Dictionary<long, string>(4096);
        static int cacheGen = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cache.Clear(); cacheGen = -1; }

        /// Animal target within the 80 m ring of a diver for a zone (before night / co-op factors).
        public static int Target(string zone)
        {
            switch (zone)
            {
                // round 3 (GDD §20 Q15 "viele, überall Leben, auch im offenen Meer"): every zone denser
                case Zones.HouseReef: return 80;
                case Zones.Lagoon: return 46;
                case Zones.Kelp: return 60;
                case Zones.Wreck: return 56;
                case Zones.BlueDrop: return 42;
                case Zones.Trench: return 22;
                case Zones.FogIsle: return 62;
                case Zones.OpenSea: return 34;
                case Zones.DeepSea: return 16;
                case Zones.Volcano: return 52;
                default: return 0;
            }
        }

        /// Schools guaranteed on top of the target (open sea: "22 + 1 school").
        public static int ExtraSchools(string zone) =>
            zone == Zones.OpenSea ? 2 : zone == Zones.DeepSea || zone == Zones.Lagoon || zone == Zones.Volcano ? 1 : 0;

        public static bool IsSiteZone(string zone) => DiveSites.Get(zone) != null;

        /// Zone at a world position (deterministic, cached per 16 m cell).
        public static string ZoneAt(Vector3 p)
        {
            if (!WorldInfo.Built) return Compute(p);
            int gen = WorldInfo.HeightProvider != null ? 1 : 0;
            if (gen != cacheGen) { cache.Clear(); cacheGen = gen; }
            int cx = Mathf.FloorToInt(p.x / CellSize), cz = Mathf.FloorToInt(p.z / CellSize);
            long key = ((long)(cx + 100000) << 32) | (uint)(cz + 100000);
            if (cache.TryGetValue(key, out var z)) return z;
            var c = new Vector3((cx + 0.5f) * CellSize, 0f, (cz + 0.5f) * CellSize);
            z = Compute(c);
            cache[key] = z;
            return z;
        }

        static string Compute(Vector3 p)
        {
            float g = WorldInfo.GroundHeight(p);
            if (g > Waves.SeaLevel) return Zones.Island;
            float depth = Waves.SeaLevel - g;

            // round 3: the volcano island and its waters (living fossils + the monster's sinkhole)
            if (Dist(p, WorldLayout.VolcanoIsland) < WorldLayout.VolcanoZoneRadius
                || Dist(p, WorldLayout.VolcanoAbyss) < WorldLayout.VolcanoAbyssRadius + 30f) return Zones.Volcano;

            // dive sites (+ margin) and their landmark islands
            var all = DiveSites.All;
            DiveSite best = null;
            float bestOut = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                float d = Dist(p, s.center);
                float outside = d - s.radius;
                bool inside = outside <= ZoneMargin;
                if (!inside)
                {
                    if (LandmarkOf(s.id, out Vector3 island, out float shore) && Dist(p, island) - shore <= LandmarkShoreRange) { inside = true; outside = Mathf.Min(outside, Dist(p, island) - shore - LandmarkShoreRange); }
                }
                if (inside && outside < bestOut) { bestOut = outside; best = s; }
            }
            if (best != null) return best.id;

            if (Dist(p, WorldLayout.IslandCenter) < WorldLayout.IslandRadius + LagoonExtra && depth <= LagoonMaxDepth) return Zones.Lagoon;
            return depth >= DeepSeaDepth ? Zones.DeepSea : Zones.OpenSea;
        }

        /// Landmark island of a dive site (centre, shore radius).
        public static bool LandmarkOf(string siteId, out Vector3 center, out float shore)
        {
            shore = WorldLayout.MiniIslandRadius;
            switch (siteId)
            {
                case Zones.Kelp: center = WorldLayout.KelpIsland; return true;
                case Zones.Wreck: center = WorldLayout.WreckIsland; return true;
                case Zones.BlueDrop: center = WorldLayout.BlueDropIsland; return true;
                case Zones.Trench: center = WorldLayout.TrenchIsland; return true;
                case Zones.FogIsle: center = WorldLayout.FogIsland; shore = WorldLayout.FogIslandShoreRadius; return true;
                default: center = Vector3.zero; return false;
            }
        }

        /// The point a species' homeRange is measured from (landmark island of its home site, else the site centre).
        public static bool HomeAnchor(SpeciesDef s, out Vector3 center, out float baseRadius)
        {
            center = Vector3.zero;
            baseRadius = 0f;
            if (s == null || string.IsNullOrEmpty(s.homeSite)) return false;
            var site = DiveSites.Get(s.homeSite);
            if (site == null) return false;
            if (s.homeSite == Zones.Kelp && LandmarkOf(s.homeSite, out Vector3 isl, out float sh)) { center = isl; baseRadius = sh; return true; }
            center = site.center;
            baseRadius = site.radius;
            return true;
        }

        /// Tide-pool area (crab zone TP).
        public static bool IsTidePool(Vector3 p) => Dist(p, WorldLayout.TidePools) < 26f;

        static float Dist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
