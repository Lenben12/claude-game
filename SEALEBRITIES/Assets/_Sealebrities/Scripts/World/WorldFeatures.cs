using UnityEngine;

namespace Sealebrities
{
    /// Hint for the terrain colouring (and for props) about what a terrain feature wants the ground to be.
    public enum GroundHint : byte { None, Rock, Sand, Soil, WetSand }

    /// A small round spot (tide pool, sea stack …): x/z centre, y = reference height, radius in meters.
    public struct WorldSpot
    {
        public Vector3 position;
        public float radius;
        public WorldSpot(float x, float y, float z, float r) { position = new Vector3(x, y, z); radius = r; }
    }

    /// A small landmark island next to an outer dive site.
    public struct MiniIsland
    {
        public string id;
        public Vector3 center;
        /// Average shoreline radius (the coast wobbles about ±4 m around it; never beyond WorldLayout.MiniIslandRadius).
        public float shoreRadius;
        /// Highest point above sea level.
        public float peak;
        /// Underwater flank steepness (meters of depth per meter outward below the 6 m shallow shelf).
        public float steepness;
        /// 0 = sandy/green cay .. 1 = bare rock.
        public float rocky;
    }

    /// Exact geometry of the terrain features the World module shapes for the points of interest (GDD 15.33).
    /// Deterministic constants (identical on every co-op machine); thread-safe (read-only after the static
    /// constructor). Props use these to place their content: waterfall ribbons and pool water, the cave roof and
    /// secret, rope bridge anchors, tide pool decoration, radio tower foundation and the landmark islands.
    public static class WorldFeatures
    {
        // ------------------------------------------------------------------ waterfall + pool
        /// Height of the cliff edge where the water pours over (≈ 8 m above the foot, ≈ 8.8 m above the pool).
        public const float FallTopY = 22.3f;
        /// Water surface of the plunge pool (the basin floor lies up to 1.8 m below it).
        public const float PoolWaterY = 13.5f;
        /// Radius of the pool's water surface around WorldLayout.WaterfallPool (the basin rim is ~0.3 m higher 0.5 m further out).
        public const float PoolRadius = 7.5f;
        /// Floor level at the cliff foot (the creek runs from here to the pool, 0.45 m deep).
        public const float FallFootY = PoolWaterY + 1.0f;
        /// Unit direction (y = 0) from the cliff edge toward the pool (the waterfall faces this way).
        public static readonly Vector3 FallDir;
        /// Edge point of the waterfall (y = FallTopY − creek depth: the water surface on the edge).
        public static readonly Vector3 FallLip;
        /// Foot of the falls (y = FallFootY − creek depth).
        public static readonly Vector3 FallFoot;
        /// Centre of the pool's water surface (y = PoolWaterY).
        public static readonly Vector3 PoolCenter;
        /// Creek polyline (water surface heights): plateau source → lip → foot → pool edge.
        public static readonly Vector3[] Creek;
        /// Small sandy beach at the far side of the pool (y = ground).
        public static readonly Vector3 PoolBeach;
        /// Walkable ramp (≈ 31°, 5 m wide) cut diagonally down the cliff east of the falls, under the props' "waterfall"
        /// trail: top end on the plateau, bottom end on the amphitheater floor (y = ground heights).
        public static readonly Vector3 FallRampTop, FallRampBottom;

        // ------------------------------------------------------------------ cave
        /// Floor height of the clearing in front of the cave, the passage and the chamber.
        public const float CaveFloorY = 14.4f;
        /// Half width of the passage carved into the hill (walls are near-vertical rock).
        public const float CaveHalfWidth = 3.2f;
        /// Length of the passage from the mouth (rock face) to the chamber entry.
        public const float CaveLength = 13f;
        public const float CaveChamberRadius = 6f;
        /// The hill above the passage reaches at least CaveFloorY + this (props add the rock roof at ~CaveFloorY + 4.5).
        public const float CaveHillHeight = 7.3f;
        /// Horizontal unit vector pointing from the mouth INTO the hill (the mouth faces −CaveDir: north-east, toward the trail).
        public static readonly Vector3 CaveDir;
        /// Centre of the mouth on the rock face (y = CaveFloorY).
        public static readonly Vector3 CaveMouth;
        /// Floor centre of the inner chamber (y = floor) – the place for the secret.
        public static readonly Vector3 CaveChamber;

        // ------------------------------------------------------------------ ravine + rope bridge
        /// Level of both rims at the rope bridge.
        public const float RavineRimY = 18.5f;
        public const float RavineDepth = 7.5f;
        /// Half width at the top (≈ 13 m wide) and at the floor.
        public const float RavineHalfWidthTop = 6.5f, RavineHalfWidthFloor = 2.2f;
        /// West and east end of the ravine axis (y = 0; the floor fades out at both ends).
        public const float RavineWestX = -41f, RavineEastX = 16f;
        /// Rope bridge anchors on the north and south rim (y = RavineRimY), 14 m apart.
        public static readonly Vector3 BridgeNorth, BridgeSouth;

        // ------------------------------------------------------------------ tide pools
        /// Rock platform level (~0.4–1.1 m above sea level, knobs up to ~1.8 m).
        public const float TidePlatformY = 0.75f;
        /// Unit vector along the shore and pointing out to sea at WorldLayout.TidePools.
        public static readonly Vector3 TideAlong, TideSeaward;
        /// The pools: position.y = pool floor (sea water fills them), radius = water radius at sea level.
        public static readonly WorldSpot[] TidePoolSpots;

        // ------------------------------------------------------------------ radio tower ruins
        public const float RadioPlateauY = 15f;
        public const float RadioPlateauRadius = 13f;

        // ------------------------------------------------------------------ second beach
        /// The southern beach: WorldLayout.SecondBeach lies on dry sand ~1 m above the water; the beach faces −Z.
        public static readonly WorldSpot[] SecondBeachHeadlands;

        // ------------------------------------------------------------------ hidden bay
        /// The small sandy beach at the inner (east) end of the hidden cove (y = 0; the ground is ~0.5-2 m there).
        public static readonly Vector3 HiddenBayBeach = new Vector3(WorldLayout.HiddenBay.x + 27f, 0f, WorldLayout.HiddenBay.z);
        /// A walkable gully (≈ 21°, 5 m wide) cut through the cove's southern ridge from the radio tower plateau down to
        /// HiddenBayBeach: top end (y = ground) and bottom end on the beach. A trail to the hidden bay should use it.
        public static readonly Vector3 BayGullyTop = new Vector3(-76f, 14.5f, -60f), BayGullyBottom = new Vector3(-89f, 0.9f, -26f);
        /// Round 2 (0.75 plan): a walkable ramp from the lighthouse trail down into the cave clearing.
        /// Round 2b (L.5): no cave ramp any more (the cave mouth is in the waterfall cliff) – both zero = unused.
        public static readonly Vector3 CaveRampTop = Vector3.zero, CaveRampBottom = Vector3.zero;

        // ------------------------------------------------------------------ village (round 2b)
        /// Flat village terrace behind the second beach (ellipse half axes along x / z, y = terrace height).
        public const float VillageTerraceY = 1.7f, VillageHalfX = 24f, VillageHalfZ = 12f;

        // ------------------------------------------------------------------ sea stacks + landmark islands
        /// Rocky islets just off the main island coast (position.y = top height).
        public static readonly WorldSpot[] SeaStacks;
        public static readonly MiniIsland[] MiniIslands;

        // ------------------------------------------------------------------ detail pass (round 2b, L.5 "much more detailed", L.3)
        /// Small coves bitten into the coastline away from the points of interest: position = the shoreline point the cove
        /// is centred on (y = 0), radius = how far the bay reaches inland. Each has a narrow sandy beach at its back, rock
        /// or cliff around it. Good spots for props (rowboat, campfire, a secret under a cliff).
        public static readonly WorldSpot[] SmallCoves;
        /// Rock ledges / outcrops on the hill flanks (≥ 11 m from every trail, ≥ 20 m from every anchor): position.y =
        /// height of the flat rock top above the slope it sits on, radius of the flat top + steep sides. Painted as rock.
        public static readonly WorldSpot[] RockLedges;
        /// Flat skerries just above the water off the cliff coasts (position.y = top height above sea level, radius).
        public static readonly WorldSpot[] Skerries;
        /// L.3: patch reefs in the lagoon around the main island (6–12 m across, 1.5–4 m deep, none in the boat lanes, the
        /// village lagoon, the hidden-bay mouth or the house reef). The terrain raises a knobbly rock mound under each
        /// (position.y = seabed height of the mound top, negative) inside a slightly deeper sand halo; props grow the coral
        /// patch on it and register its habitat anchors. Deterministic list, identical on every machine.
        public static readonly WorldSpot[] LagoonPatchReefs;
        /// L.3 house reef: the reef crest runs along the lagoon edge at this distance from the island centre (3–4 m deep,
        /// ≈ 34 m wide), a sandy back-reef lies inside it, spur-and-groove channels cut the fore-reef outside it.
        public const float ReefCrestR = 176f, ReefCrestHalfWidth = 17f;
        /// Grooves of the fore-reef: spacing along the crest (m) and how deep they cut (m).
        public const float SpurSpacing = 11f, GrooveDepth = 1.7f;
        /// Seabed detail zone of the house reef (x0, z0, x1, z1 – aligned to the 128 m terrain chunks): the terrain uses
        /// 1.6 m facets here, so the short sand ripples (3–6 m wavelength) and the spur-and-groove channels stay crisp.
        public static readonly Vector4 ReefDetailRect = new Vector4(128f, -128f, 256f, 128f);

        static WorldFeatures()
        {
            // waterfall
            Vector3 top = WorldLayout.WaterfallTop, pool = WorldLayout.WaterfallPool;
            Vector3 a = pool - top; a.y = 0f; a.Normalize();
            FallDir = a;
            const float creek = 0.45f;
            FallLip = new Vector3(top.x, FallTopY - creek, top.z);
            FallFoot = new Vector3(top.x + a.x * 2.8f, FallFootY - creek, top.z + a.z * 2.8f);
            PoolCenter = new Vector3(pool.x, PoolWaterY, pool.z);
            Creek = new[]
            {
                new Vector3(top.x - a.x * 13f, FallTopY + 13f * 0.05f - creek, top.z - a.z * 13f),
                new Vector3(top.x - a.x * 6f, FallTopY + 6f * 0.05f - creek, top.z - a.z * 6f),
                FallLip,
                FallFoot,
                new Vector3(pool.x - a.x * (PoolRadius - 0.5f), PoolWaterY, pool.z - a.z * (PoolRadius - 0.5f)),
            };
            PoolBeach = new Vector3(pool.x + a.x * (PoolRadius + 2.5f), PoolWaterY + 0.5f, pool.z + a.z * (PoolRadius + 2.5f));
            FallRampTop = new Vector3(top.x + 5.5f, FallTopY, top.z + 3.4f);
            FallRampBottom = new Vector3(top.x + 15f, FallFootY - 0.3f, top.z - 6f);

            // cave (round 2b): the mouth sits in the waterfall cliff and the passage runs straight into the plateau
            CaveDir = -FallDir;
            Vector3 e = WorldLayout.CaveEntrance;
            CaveMouth = new Vector3(e.x, CaveFloorY, e.z);
            float chamberS = CaveLength + CaveChamberRadius - 1.5f;
            CaveChamber = new Vector3(e.x + CaveDir.x * chamberS, CaveFloorY + 0.02f * chamberS, e.z + CaveDir.z * chamberS);

            // ravine bridge (the axis meanders; RavineAxisZ gives the exact centre line)
            float zc = RavineAxisZ(WorldLayout.Ravine.x);
            BridgeNorth = new Vector3(WorldLayout.Ravine.x, RavineRimY, zc + 7f);
            BridgeSouth = new Vector3(WorldLayout.Ravine.x, RavineRimY, zc - 7f);

            // tide pools
            TideAlong = new Vector3(-0.5621f, 0f, -0.8271f);
            TideSeaward = new Vector3(0.8271f, 0f, -0.5621f);
            // (the props' trail reaches the platform along v ≈ -4..-7 from the south-east: keep that strip dry)
            float[] pu = { -16f, -9.5f, -3f, 3.5f, 10f, 16.5f, -12f, 7.5f, 0.5f };
            float[] pv = { 0.5f, 3f, -1.5f, 2.5f, -0.5f, 2.5f, -5f, 5.6f, 5f };
            float[] pr = { 2.6f, 3.1f, 2.3f, 3.3f, 2.5f, 2.8f, 2.1f, 2.3f, 2.0f };
            Vector3 tp = WorldLayout.TidePools;
            TidePoolSpots = new WorldSpot[pu.Length];
            for (int i = 0; i < pu.Length; i++)
            {
                float x = tp.x + TideAlong.x * pu[i] + TideSeaward.x * pv[i];
                float z = tp.z + TideAlong.z * pu[i] + TideSeaward.z * pv[i];
                TidePoolSpots[i] = new WorldSpot(x, TidePoolFloor, z, pr[i] * 0.72f);
            }

            Vector3 sb = WorldLayout.SecondBeach;
            SecondBeachHeadlands = new[]
            {
                new WorldSpot(sb.x - 31f, 3.2f, sb.z - 1f, 8f),
                new WorldSpot(sb.x + 30f, 2.6f, sb.z + 3f, 7f),
            };

            // in the lagoon just off the coast, away from the dock, the house reef, the second beach and the boat lanes
            SeaStacks = new[]
            {
                new WorldSpot(-42f, 6f, 117f, 5f),
                new WorldSpot(-116f, 7.5f, 67f, 6f),
                new WorldSpot(-151f, 8.5f, 26f, 6.5f),
                new WorldSpot(-126f, 6.5f, -107f, 7f),
                new WorldSpot(-46f, 5f, -128f, 5f),
                new WorldSpot(69f, 6f, -119f, 5.5f),
                new WorldSpot(25f, 4.5f, 143f, 4.5f),
            };

            MiniIslands = new[]
            {
                new MiniIsland { id = "kelp_forest", center = WorldLayout.KelpIsland, shoreRadius = 35f, peak = 7f, steepness = 1.0f, rocky = 0.15f },
                new MiniIsland { id = "wreck", center = WorldLayout.WreckIsland, shoreRadius = 35f, peak = 10f, steepness = 1.4f, rocky = 0.6f },
                new MiniIsland { id = "blue_drop", center = WorldLayout.BlueDropIsland, shoreRadius = 33f, peak = 12f, steepness = 3.2f, rocky = 0.85f },
                new MiniIsland { id = "deep_trench", center = WorldLayout.TrenchIsland, shoreRadius = 34f, peak = 4f, steepness = 4.6f, rocky = 0.1f },
                new MiniIsland { id = "fog_isle", center = WorldLayout.FogIsland, shoreRadius = 30f, peak = 16f, steepness = 1.2f, rocky = 0.45f },
            };

            // ---- detail pass (positions probed against the 0.62 island: away from anchors, trails, boat lanes)
            SmallCoves = new[]
            {
                new WorldSpot(101f, 0f, 62f, 10f),     // north of the dock beach: a little swimming bight
                new WorldSpot(-4f, 0f, 113f, 8f),      // under the lighthouse cliff: the "north beach"
                new WorldSpot(-73f, 0f, 87f, 9f),      // north-west cliff coast
                new WorldSpot(-120f, 0f, 44f, 10f),    // west coast, skerries in its mouth
                new WorldSpot(-64.5f, 0f, -112f, 9f),  // south-west, below the radio tower hills
            };
            RockLedges = new[]
            {
                new WorldSpot(36f, 2.2f, 96f, 4.5f),
                new WorldSpot(-52f, 3.0f, -28f, 5.5f),
                new WorldSpot(24f, 2.4f, 44f, 5f),
                new WorldSpot(-104f, 2.6f, -56f, 5f),
                new WorldSpot(4f, 3.2f, -4f, 6f),
                new WorldSpot(-8f, 2.5f, -56f, 5f),
                new WorldSpot(0f, 2.8f, 56f, 5.5f),
                new WorldSpot(-40f, 3.0f, 80f, 5f),
                new WorldSpot(28f, 1.8f, -20f, 4.5f),
                new WorldSpot(-84f, 2.8f, 40f, 5.5f),
                new WorldSpot(12f, 2.0f, 96f, 4.5f),
                new WorldSpot(-48f, 2.4f, 12f, 4.5f),
                new WorldSpot(-100f, 2.0f, -80f, 4.5f),
                new WorldSpot(-32f, 2.6f, -64f, 5f),
            };
            Skerries = new[]
            {
                new WorldSpot(-22f, 0.7f, 124f, 3f),
                new WorldSpot(-94f, 0.9f, 79f, 3.5f),
                new WorldSpot(-130f, 0.6f, 47f, 2.8f),
                new WorldSpot(-138f, 1.0f, -50f, 3.5f),
                new WorldSpot(-96f, 0.6f, -114f, 3f),
            };
            LagoonPatchReefs = new[]
            {
                new WorldSpot(90.3f, -1.5f, 100.3f, 4.5f),
                new WorldSpot(31.4f, -2.45f, 178.3f, 6f),
                new WorldSpot(7.2f, -1.5f, 137.3f, 4.5f),
                new WorldSpot(-41.5f, -2.3f, 144.7f, 5.5f),
                new WorldSpot(-84.4f, -1.65f, 125.2f, 5f),
                new WorldSpot(-119.8f, -2.1f, 93.6f, 4f),
                new WorldSpot(-157.4f, -2.15f, 57.3f, 6f),
                new WorldSpot(-165.5f, -1.85f, -57f, 5f),
                new WorldSpot(-126.1f, -2.25f, -130.6f, 5.5f),
                new WorldSpot(-77.5f, -1.65f, -134.2f, 4.5f),
                new WorldSpot(-29.3f, -1.55f, -137.9f, 5f),
                new WorldSpot(69.5f, -2.25f, -149.1f, 6f),
                new WorldSpot(96.4f, -1.5f, -110.9f, 4.5f),
                new WorldSpot(134f, -1.6f, -83.7f, 5f),
            };
        }

        /// Pool floor of the tide pools (sea water stands ~0.55 m deep in them at calm sea).
        public const float TidePoolFloor = -0.55f;

        /// Z of the ravine centre line at world x (gentle meander around WorldLayout.Ravine).
        public static float RavineAxisZ(float x)
        {
            float u = x - WorldLayout.Ravine.x;
            return WorldLayout.Ravine.z + 3.0f * Mathf.Sin(u * 0.06f) + 1.5f * Mathf.Sin(u * 0.13f + 1f) - 1.5f * Mathf.Sin(1f);
        }

        /// True where the shoreline foam must be suppressed (inside the tide pools).
        public static bool SuppressFoam(float x, float z)
        {
            var spots = TidePoolSpots;
            for (int i = 0; i < spots.Length; i++)
            {
                float dx = x - spots[i].position.x, dz = z - spots[i].position.z;
                float r = spots[i].radius + 1.6f;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        /// The landmark island at a position (null when none is within its shore radius + margin).
        public static bool TryGetMiniIsland(Vector3 p, float margin, out MiniIsland island)
        {
            for (int i = 0; i < MiniIslands.Length; i++)
            {
                var m = MiniIslands[i];
                float dx = p.x - m.center.x, dz = p.z - m.center.z;
                float r = m.shoreRadius + margin;
                if (dx * dx + dz * dz < r * r) { island = m; return true; }
            }
            island = default;
            return false;
        }
    }
}
