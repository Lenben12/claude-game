using System;

using System.Collections.Generic;

using UnityEngine;

namespace Sealebrities
{
    /// Habitat anchor kinds of the reef (L.3): the spawner puts bottom species at a matching anchor within 30 m.
    public enum FaunaHabitat : byte { None, Anemone, Crevice, Cleaning, Overhang, Sand, SeaFan, SeaGrass }

    /// Island home-spot kinds (L.11): perches, walls, pens, beaches …
    public enum FaunaSpotKind : byte { Ground, Perch, Wall, Roost, Pen, Beach, TidePool, Water, Soar, Jungle, Meadow, Rocks }

    public struct FaunaHabitatAnchor
    {
        public Vector3 pos;
        public float radius;
        public FaunaHabitat kind;
    }

    public struct FaunaLandSpot
    {
        public Vector3 pos;
        /// Wall spots: horizontal outward normal; others: up.
        public Vector3 normal;
        public FaunaSpotKind kind;
        public float radius;
        /// Free text from the publisher ("dock", "roof", "lighthouse", "village" …).
        public string tag;
        public bool valid;
    }

    /// Habitat anchors (reef, L.3) and island home spots (L.11) for the fauna.
    /// Sources, in this order: (1) whatever the props module registers through AddHabitat / AddLandSpot, (2) public
    /// static lists PropInfo.Habitats / PropInfo.LandSpots if the props module publishes them (read once by reflection,
    /// any element type with position/radius/kind/normal fields), (3) a deterministic fallback derived from the
    /// terrain and the WorldLayout anchors, so the fauna works even before the props publish anything.
    /// Lookups use a 32 m grid (no allocations after the build).
    public static class FaunaAnchors
    {
        public static readonly List<FaunaHabitatAnchor> Habitats = new List<FaunaHabitatAnchor>(1024);
        public static readonly List<FaunaLandSpot> LandSpots = new List<FaunaLandSpot>(512);

        const float Cell = 32f;
        static readonly Dictionary<long, List<int>> habitatGrid = new Dictionary<long, List<int>>();
        static bool built, habitatsBuilt, spotsBuilt;
        static float worldSeen = -1f;
        static int externalHabitats, externalSpots;
        static float nextImportTry;

        public static bool Built => built;
        /// True once the props module published real anchors (otherwise the fallback is in use).
        public static bool HasPropHabitats => externalHabitats > 0;
        public static bool HasPropSpots => externalSpots > 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Habitats.Clear(); LandSpots.Clear(); habitatGrid.Clear();
            built = false; habitatsBuilt = false; spotsBuilt = false; worldSeen = -1f; importedHabitatCount = -1; importedSpotCount = -1;
            externalHabitats = 0; externalSpots = 0; nextImportTry = 0f;
        }

        // ================================================================== public registration API (props)

        /// Props: register a reef habitat anchor. kind: "anemone", "crevice", "cleaning", "overhang", "sand", "seafan", "seagrass".
        public static void AddHabitat(Vector3 pos, float radius, string kind)
        {
            var k = ParseHabitat(kind);
            if (k == FaunaHabitat.None) return;
            if (externalHabitats == 0 && habitatsBuilt) DropFallbackHabitats();
            externalHabitats++;
            AddHabitatInternal(pos, radius, k);
        }

        /// Props: register an island home spot. kind: "ground", "perch", "wall", "roost", "pen", "beach", "tidepool",
        /// "water", "soar", "jungle", "meadow", "rocks". normal: outward wall normal (or up).
        public static void AddLandSpot(Vector3 pos, Vector3 normal, string kind, float radius, string tag)
        {
            var k = ParseSpot(kind);
            if (externalSpots == 0 && spotsBuilt) LandSpots.Clear();
            externalSpots++;
            LandSpots.Add(new FaunaLandSpot { pos = pos, normal = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.up, kind = k, radius = Mathf.Max(0.5f, radius), tag = tag, valid = true });
        }

        public static FaunaHabitat ParseHabitat(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "anemone": return FaunaHabitat.Anemone;
                case "crevice": return FaunaHabitat.Crevice;
                case "cleaning": return FaunaHabitat.Cleaning;
                case "overhang": return FaunaHabitat.Overhang;
                case "sand": return FaunaHabitat.Sand;
                case "seafan": case "sea_fan": return FaunaHabitat.SeaFan;
                case "seagrass": case "sea_grass": return FaunaHabitat.SeaGrass;
                default: return FaunaHabitat.None;
            }
        }

        public static FaunaSpotKind ParseSpot(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "perch": case "post": case "roof": case "railing": return FaunaSpotKind.Perch;
                case "wall": return FaunaSpotKind.Wall;
                case "roost": case "palm": return FaunaSpotKind.Roost;
                case "pen": return FaunaSpotKind.Pen;
                case "beach": return FaunaSpotKind.Beach;
                case "tidepool": case "tide_pool": case "tidepools": return FaunaSpotKind.TidePool;
                case "water": return FaunaSpotKind.Water;
                case "soar": return FaunaSpotKind.Soar;
                case "jungle": case "burrow": return FaunaSpotKind.Jungle;
                case "meadow": return FaunaSpotKind.Meadow;
                case "rocks": case "rock": return FaunaSpotKind.Rocks;
                default: return FaunaSpotKind.Ground;
            }
        }

        // ================================================================== build

        /// Builds the fallback once the world exists and retries the PropInfo import every few seconds.
        public static void Ensure()
        {
            if (!WorldInfo.Built) { worldSeen = -1f; return; }
            if (worldSeen < 0f) worldSeen = Time.unscaledTime;
            bool late = Time.unscaledTime - worldSeen > 15f;
            // the fallback casts against the props (roofs, walls): wait until they are built (or give up waiting)
            if (!habitatsBuilt && (PropInfo.UnderwaterBuilt || late))
            {
                habitatsBuilt = true;
                ImportHabitats();
                if (externalHabitats == 0) BuildFallbackHabitats();
            }
            if (!spotsBuilt && (PropInfo.IslandBuilt || late))
            {
                spotsBuilt = true;
                ImportSpots();
                if (externalSpots == 0) BuildFallbackSpots();
            }
            built = habitatsBuilt && spotsBuilt;
            // the props may (re)build later: pick their lists up when they change
            if (built && Time.unscaledTime >= nextImportTry)
            {
                nextImportTry = Time.unscaledTime + 5f;
                if (PropInfo.Habitats.Count > 0 && PropInfo.Habitats.Count != importedHabitatCount) ImportHabitats();
                if (PropInfo.LandSpots.Count > 0 && PropInfo.LandSpots.Count != importedSpotCount) ImportSpots();
            }
        }

        static int importedHabitatCount = -1, importedSpotCount = -1;

        /// PropInfo.Habitats (props, L.3) → anchors. Replaces the fallback when there are any.
        static void ImportHabitats()
        {
            var src = PropInfo.Habitats;
            importedHabitatCount = src.Count;
            if (src.Count == 0) return;
            DropFallbackHabitats();
            externalHabitats = 0;
            for (int i = 0; i < src.Count; i++)
            {
                var h = src[i];
                externalHabitats++;
                AddHabitatInternal(h.position, h.radius, (FaunaHabitat)((int)h.kind + 1));
            }
            // the older published spots still count (anemones / wreck holes / vents)
            for (int i = 0; i < PropInfo.AnemoneSpots.Count; i++) AddHabitatInternal(PropInfo.AnemoneSpots[i], 1.5f, FaunaHabitat.Anemone);
            for (int i = 0; i < PropInfo.WreckHoles.Count; i++) AddHabitatInternal(PropInfo.WreckHoles[i], 1f, FaunaHabitat.Crevice);
            Debug.Log("[Fauna] habitat anchors from props: " + externalHabitats);
        }

        /// PropInfo.LandSpots (props, L.11) → island home spots. Replaces the fallback when there are any.
        static void ImportSpots()
        {
            var src = PropInfo.LandSpots;
            importedSpotCount = src.Count;
            if (src.Count == 0) return;
            LandSpots.Clear();
            externalSpots = 0;
            for (int i = 0; i < src.Count; i++)
            {
                var s = src[i];
                FaunaSpotKind k;
                switch (s.kind)
                {
                    case LandSpotKind.Perch: k = FaunaSpotKind.Perch; break;
                    case LandSpotKind.Railing: k = FaunaSpotKind.Perch; break;
                    case LandSpotKind.Nest: k = FaunaSpotKind.Rocks; break;
                    case LandSpotKind.Burrow: k = s.area == "jungle" ? FaunaSpotKind.Jungle : FaunaSpotKind.Beach; break;
                    case LandSpotKind.Pen: k = FaunaSpotKind.Pen; break;
                    case LandSpotKind.Wall: k = FaunaSpotKind.Wall; break;
                    case LandSpotKind.Palm: k = FaunaSpotKind.Jungle; break;
                    case LandSpotKind.Bask: k = s.area == "tidepools" ? FaunaSpotKind.TidePool : FaunaSpotKind.Rocks; break;
                    case LandSpotKind.Meadow: k = FaunaSpotKind.Meadow; break;
                    case LandSpotKind.Roost: k = FaunaSpotKind.Roost; break;
                    case LandSpotKind.Shore: k = s.area == "tidepools" ? FaunaSpotKind.TidePool : FaunaSpotKind.Beach; break;
                    case LandSpotKind.Water: k = FaunaSpotKind.Water; break;
                    default: k = FaunaSpotKind.Ground; break;
                }
                externalSpots++;
                LandSpots.Add(new FaunaLandSpot { pos = s.position, normal = s.normal.sqrMagnitude > 1e-4f ? s.normal.normalized : Vector3.up, kind = k, radius = Mathf.Max(0.4f, s.radius), tag = s.area, valid = true });
                // round 3: the goats / chickens / Truffle live in the hidden hermit camp (the village is gone) – a pen or
                // the camp meadow also serves as walkable ground for the camp animals
                if (s.kind == LandSpotKind.Pen || s.area == "hermit_camp")
                    LandSpots.Add(new FaunaLandSpot { pos = s.position, normal = Vector3.up, kind = FaunaSpotKind.Ground, radius = Mathf.Max(0.4f, s.radius), tag = "camp", valid = true });
            }
            // the soaring centres are not props: lighthouse and village thermals
            Vector3 lh = WorldLayout.Lighthouse; lh.y = WorldInfo.GroundHeight(lh);
            AddSpot(lh + Vector3.up * 40f, Vector3.up, FaunaSpotKind.Soar, 25f, "lighthouse");
            Vector3 vil = WorldLayout.Village; vil.y = WorldInfo.GroundHeight(vil);
            AddSpot(vil + Vector3.up * 30f, Vector3.up, FaunaSpotKind.Soar, 22f, "village");
            Debug.Log("[Fauna] island home spots from props: " + externalSpots);
            if (LandFauna.Instance != null) LandFauna.Instance.Replan();
        }

        static void AddHabitatInternal(Vector3 pos, float radius, FaunaHabitat k)
        {
            Habitats.Add(new FaunaHabitatAnchor { pos = pos, radius = Mathf.Max(0.5f, radius), kind = k });
            long key = Key(pos);
            if (!habitatGrid.TryGetValue(key, out var l)) { l = new List<int>(8); habitatGrid[key] = l; }
            l.Add(Habitats.Count - 1);
        }

        static void DropFallbackHabitats()
        {
            Habitats.Clear();
            habitatGrid.Clear();
        }

        static long Key(Vector3 p) => Key(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.z / Cell));
        static long Key(int cx, int cz) => ((long)(cx + 100000) << 32) | (uint)(cz + 100000);

        /// Nearest anchor of a kind within range of a point (horizontal distance). Allocation free.
        public static bool FindHabitat(Vector3 near, float range, FaunaHabitat kind, int salt, out Vector3 pos)
        {
            pos = near;
            if (kind == FaunaHabitat.None || Habitats.Count == 0) return false;
            int cx = Mathf.FloorToInt(near.x / Cell), cz = Mathf.FloorToInt(near.z / Cell);
            int r = Mathf.CeilToInt(range / Cell);
            float best = range * range;
            int bestI = -1;
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (!habitatGrid.TryGetValue(Key(cx + dx, cz + dz), out var l)) continue;
                    for (int j = 0; j < l.Count; j++)
                    {
                        var a = Habitats[l[j]];
                        if (a.kind != kind) continue;
                        float ddx = a.pos.x - near.x, ddz = a.pos.z - near.z;
                        float d2 = ddx * ddx + ddz * ddz;
                        if (d2 > range * range) continue;
                        // not always the very nearest: a little salt spreads the animals over the anchors
                        float score = d2 * (0.6f + 0.8f * Hash01(l[j], salt));
                        if (score < best) { best = score; bestI = l[j]; }
                    }
                }
            if (bestI < 0) return false;
            var h = Habitats[bestI];
            float ang = Hash01(bestI, salt + 7) * Mathf.PI * 2f;
            float rr = h.radius * 0.6f * Hash01(bestI, salt + 13);
            pos = h.pos + new Vector3(Mathf.Cos(ang) * rr, 0f, Mathf.Sin(ang) * rr);
            return true;
        }

        static float Hash01(int a, int b)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                return (h & 0xFFFF) / 65535f;
            }
        }

        public static FaunaHabitat HabitatOf(SpeciesDef s) => s == null ? FaunaHabitat.None : ParseHabitat(s.habitat);

        // ================================================================== fallback: reef

        static void BuildFallbackHabitats()
        {
            // published spots of the older props code
            for (int i = 0; i < PropInfo.AnemoneSpots.Count; i++) AddHabitatInternal(PropInfo.AnemoneSpots[i], 1.5f, FaunaHabitat.Anemone);
            for (int i = 0; i < PropInfo.WreckHoles.Count; i++) AddHabitatInternal(PropInfo.WreckHoles[i], 1f, FaunaHabitat.Crevice);
            for (int i = 0; i < PropInfo.VentPositions.Count; i++) AddHabitatInternal(PropInfo.VentPositions[i], 3f, FaunaHabitat.Crevice);

            // terrain-derived anchors per dive site + the lagoon ring (deterministic)
            var sites = DiveSites.All;
            for (int i = 0; i < sites.Count; i++) SampleArea(sites[i].center, sites[i].radius + 40f, 9f, 1000 + i, sites[i].id);
            SampleArea(WorldLayout.IslandCenter, WorldLayout.IslandRadius + 90f, 11f, 77, Zones.Lagoon);
        }

        static void SampleArea(Vector3 center, float radius, float step, int seed, string zone)
        {
            var rng = new System.Random(seed);
            int n = Mathf.CeilToInt(radius / step);
            bool fog = zone == Zones.FogIsle;
            bool reef = zone == Zones.HouseReef || fog || zone == Zones.Lagoon;
            for (int ix = -n; ix <= n; ix++)
                for (int iz = -n; iz <= n; iz++)
                {
                    float x = center.x + ix * step + (float)(rng.NextDouble() - 0.5) * step * 0.8f;
                    float z = center.z + iz * step + (float)(rng.NextDouble() - 0.5) * step * 0.8f;
                    float dx = x - center.x, dz = z - center.z;
                    if (dx * dx + dz * dz > radius * radius) continue;
                    var p = new Vector3(x, 0f, z);
                    float g = WorldInfo.GroundHeight(p);
                    float depth = Waves.SeaLevel - g;
                    if (depth < 1.2f) continue;
                    p.y = g;
                    float slope = Slope(p);
                    double roll = rng.NextDouble();
                    if (slope > 0.55f)
                    {
                        AddHabitatInternal(p, 1.5f, roll < 0.6 ? FaunaHabitat.Crevice : FaunaHabitat.Overhang);
                        if (fog && roll > 0.5) AddHabitatInternal(p + Vector3.up * 0.5f, 1.2f, FaunaHabitat.SeaFan);
                    }
                    else if (slope < 0.18f)
                    {
                        if (roll < 0.45) AddHabitatInternal(p, 3f, FaunaHabitat.Sand);
                        else if (reef && depth < 7f && roll < 0.7) AddHabitatInternal(p, 3f, FaunaHabitat.SeaGrass);
                        else if (reef && roll < 0.78) AddHabitatInternal(p, 1.6f, FaunaHabitat.Anemone);
                    }
                    else
                    {
                        if (roll < 0.25) AddHabitatInternal(p, 1.5f, FaunaHabitat.Crevice);
                        else if (roll < 0.36) AddHabitatInternal(p, 2f, FaunaHabitat.Cleaning);
                        else if (fog && roll < 0.6) AddHabitatInternal(p, 1.2f, FaunaHabitat.SeaFan);
                        else if (roll < 0.5) AddHabitatInternal(p, 1.5f, FaunaHabitat.Overhang);
                    }
                }
        }

        static float Slope(Vector3 p)
        {
            const float e = 2f;
            float gx = WorldInfo.GroundHeight(new Vector3(p.x + e, 0f, p.z)) - WorldInfo.GroundHeight(new Vector3(p.x - e, 0f, p.z));
            float gz = WorldInfo.GroundHeight(new Vector3(p.x, 0f, p.z + e)) - WorldInfo.GroundHeight(new Vector3(p.x, 0f, p.z - e));
            return Mathf.Sqrt(gx * gx + gz * gz) / (2f * e);
        }

        // ================================================================== fallback: island spots

        static void BuildFallbackSpots()
        {
            var rng = new System.Random(4242);
            Vector3 c = WorldLayout.IslandCenter;

            // beaches + north cliffs around the shoreline (march inward from the sea)
            for (int a = 0; a < 120; a++)
            {
                float ang = a * Mathf.PI * 2f / 120f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                for (float r = WorldLayout.IslandRadius + 60f; r > 10f; r -= 1.5f)
                {
                    Vector3 p = c + dir * r;
                    float g = WorldInfo.GroundHeight(p);
                    if (g > 0.12f)
                    {
                        p.y = g;
                        if (g < 1.6f && Slope(p) < 0.35f) AddSpot(p, Vector3.up, FaunaSpotKind.Beach, 4f, "beach");
                        else if (g > 3f) AddSpot(p, -dir, FaunaSpotKind.Rocks, 3f, dir.z > 0.45f ? "cliff_north" : "cliff");
                        // a bit further inland: meadow / jungle ground
                        Vector3 q = c + dir * (r - 14f - (float)rng.NextDouble() * 10f);
                        float gq = WorldInfo.GroundHeight(q);
                        if (gq > 2f && gq < 22f) { q.y = gq; AddSpot(q, Vector3.up, Slope(q) < 0.4f ? FaunaSpotKind.Meadow : FaunaSpotKind.Jungle, 6f, "inland"); }
                        break;
                    }
                }
            }

            // tide pools and the hidden bay rocks
            AddArea(WorldLayout.TidePools, 22f, 4f, -0.9f, 1.4f, FaunaSpotKind.TidePool, "tidepools", 31);
            AddArea(WorldLayout.HiddenBay + new Vector3(18f, 0f, 0f), 18f, 5f, -0.6f, 2.5f, FaunaSpotKind.Rocks, "bay", 32);

            // dock posts / deck edges (gulls)
            Vector3 d0 = WorldLayout.DockStart, d1 = WorldLayout.DockEnd;
            float deckY = PropInfo.DockDeckY;
            for (int k = 0; k <= 7; k++)
            {
                Vector3 p = Vector3.Lerp(d0, d1, k / 7f);
                for (int s = -1; s <= 1; s += 2)
                    AddSpot(new Vector3(p.x, deckY + 0.02f, p.z + s * 1.25f), Vector3.up, FaunaSpotKind.Perch, 0.4f, "dock");
            }

            // roofs and railings found by casting down from above (hut, lighthouse, village)
            RoofSpots(WorldLayout.Hut, 7f, "roof");
            RoofSpots(WorldLayout.Village, 22f, "village_roof");
            if (PropInfo.LighthouseGallery != Vector3.zero) AddSpot(PropInfo.LighthouseGallery + Vector3.up * 0.05f, Vector3.up, FaunaSpotKind.Perch, 1.5f, "lighthouse");
            Vector3 lh = WorldLayout.Lighthouse;
            lh.y = WorldInfo.GroundHeight(lh);
            AddSpot(lh + Vector3.up * 40f, Vector3.up, FaunaSpotKind.Soar, 25f, "lighthouse");
            Vector3 vil = WorldLayout.Village;
            vil.y = WorldInfo.GroundHeight(vil);
            AddSpot(vil + Vector3.up * 30f, Vector3.up, FaunaSpotKind.Soar, 22f, "village");

            // walls for geckos: the hut (horizontal casts toward the centre)
            WallSpots(WorldLayout.Hut, 9f, "hut");

            // round 3: the hermit camp (pens, ground) – the village is gone; palms + the pig's swimming water stay on the south beach
            AddArea(WorldLayout.HermitCamp + new Vector3(6f, 0f, -4f), 5f, 2.5f, 0.8f, 40f, FaunaSpotKind.Pen, "camp_pen", 33);
            AddArea(WorldLayout.HermitCamp, 10f, 3.5f, 0.8f, 40f, FaunaSpotKind.Ground, "camp", 34);
            AddArea(WorldLayout.RadioTowerRuins + new Vector3(14f, 0f, 10f), 16f, 5f, 2f, 25f, FaunaSpotKind.Meadow, "radio_meadow", 35);
            for (int k = 0; k < 6; k++)
            {
                float ang = k * 1.05f;
                Vector3 p = WorldLayout.Village + new Vector3(Mathf.Cos(ang) * 16f, 0f, Mathf.Sin(ang) * 10f);
                float g = WorldInfo.GroundHeight(p);
                if (g > 0.2f) AddSpot(new Vector3(p.x, g + 7f, p.z), Vector3.up, FaunaSpotKind.Roost, 6f, "village_palm");
            }
            for (int k = 0; k < 8; k++)
            {
                Vector3 p = WorldLayout.Village + new Vector3(-12f + k * 3.5f, 0f, -24f);
                float g = WorldInfo.GroundHeight(p);
                if (g < -0.6f && g > -2.5f) AddSpot(new Vector3(p.x, g, p.z), Vector3.up, FaunaSpotKind.Water, 4f, "village_beach");
            }
        }

        static void AddSpot(Vector3 p, Vector3 n, FaunaSpotKind k, float r, string tag)
        {
            LandSpots.Add(new FaunaLandSpot { pos = p, normal = n, kind = k, radius = r, tag = tag, valid = true });
        }

        static void AddArea(Vector3 center, float radius, float step, float gMin, float gMax, FaunaSpotKind kind, string tag, int seed)
        {
            var rng = new System.Random(seed);
            int n = Mathf.CeilToInt(radius / step);
            for (int ix = -n; ix <= n; ix++)
                for (int iz = -n; iz <= n; iz++)
                {
                    float x = center.x + ix * step + (float)(rng.NextDouble() - 0.5) * step * 0.6f;
                    float z = center.z + iz * step + (float)(rng.NextDouble() - 0.5) * step * 0.6f;
                    if ((x - center.x) * (x - center.x) + (z - center.z) * (z - center.z) > radius * radius) continue;
                    var p = new Vector3(x, 0f, z);
                    float g = WorldInfo.GroundHeight(p);
                    if (g < gMin || g > gMax) continue;
                    p.y = g;
                    AddSpot(p, Vector3.up, kind, step * 0.5f, tag);
                }
        }

        static void RoofSpots(Vector3 center, float radius, string tag)
        {
            for (int k = 0; k < 10; k++)
            {
                float ang = k * 0.628f;
                float r = radius * (k % 2 == 0 ? 0.25f : 0.6f);
                Vector3 p = center + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                float g = WorldInfo.GroundHeight(p);
                if (Physics.Raycast(new Vector3(p.x, g + 20f, p.z), Vector3.down, out RaycastHit hit, 19.5f, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore)
                    && hit.point.y > g + 2.2f && hit.normal.y > 0.45f)
                    AddSpot(hit.point + Vector3.up * 0.02f, Vector3.up, FaunaSpotKind.Perch, 0.6f, tag);
            }
        }

        static void WallSpots(Vector3 center, float radius, string tag)
        {
            for (int k = 0; k < 16; k++)
            {
                float ang = k * Mathf.PI * 2f / 16f;
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                Vector3 from = center + dir * radius;
                float g = WorldInfo.GroundHeight(from);
                from.y = g + 1.3f;
                if (Physics.Raycast(from, -dir, out RaycastHit hit, radius, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore)
                    && Mathf.Abs(hit.normal.y) < 0.25f && hit.point.y > WorldInfo.GroundHeight(hit.point) + 0.6f)
                {
                    Vector3 n = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
                    AddSpot(hit.point, n, FaunaSpotKind.Wall, 1f, tag);
                }
            }
        }

    }
}
