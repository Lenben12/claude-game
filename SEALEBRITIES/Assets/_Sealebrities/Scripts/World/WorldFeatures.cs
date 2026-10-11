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
        public const float CaveFloorY = 13f;
        /// Half width of the passage carved into the hill (walls are near-vertical rock).
        public const float CaveHalfWidth = 3.2f;
        /// Length of the passage from the mouth (rock face) to the chamber entry.
        public const float CaveLength = 13f;
        public const float CaveChamberRadius = 6f;
        /// The hill above the passage reaches at least CaveFloorY + this (props add the rock roof at ~CaveFloorY + 4.5).
        public const float CaveHillHeight = 11f;
        /// Horizontal unit vector pointing from the mouth INTO the hill (the mouth faces −CaveDir: north-east, toward the trail).
        public static readonly Vector3 CaveDir;
        /// Centre of the mouth on the rock face (y = CaveFloorY).
        public static readonly Vector3 CaveMouth;
        /// Floor centre of the inner chamber (y = floor) – the place for the secret.
        public static readonly Vector3 CaveChamber;

        // ------------------------------------------------------------------ ravine + rope bridge
        /// Level of both rims at the rope bridge.
        public const float RavineRimY = 23.5f;
        public const float RavineDepth = 7.5f;
        /// Half width at the top (≈ 13 m wide) and at the floor.
        public const float RavineHalfWidthTop = 6.5f, RavineHalfWidthFloor = 2.2f;
        /// West and east end of the ravine axis (y = 0; the floor fades out at both ends).
        public const float RavineWestX = -66f, RavineEastX = 26f;
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
        public const float RadioPlateauY = 22f;
        public const float RadioPlateauRadius = 13f;

        // ------------------------------------------------------------------ second beach
        /// The southern beach: WorldLayout.SecondBeach lies on dry sand ~1 m above the water; the beach faces −Z.
        public static readonly WorldSpot[] SecondBeachHeadlands;

        // ------------------------------------------------------------------ hidden bay
        /// The small sandy beach at the inner (east) end of the hidden cove (y = 0; the ground is ~0.5-2 m there).
        public static readonly Vector3 HiddenBayBeach = new Vector3(WorldLayout.HiddenBay.x + 40f, 0f, WorldLayout.HiddenBay.z);
        /// A walkable gully (≈ 20°, 5 m wide) cut through the cove's southern ridge from the radio tower side down to
        /// HiddenBayBeach: top end (y = ground) and bottom end on the beach. A trail to the hidden bay should use it.
        public static readonly Vector3 BayGullyTop = new Vector3(-146f, 16.4f, -82f), BayGullyBottom = new Vector3(-146f, 0.9f, -38f);

        // ------------------------------------------------------------------ sea stacks + landmark islands
        /// Rocky islets just off the main island coast (position.y = top height).
        public static readonly WorldSpot[] SeaStacks;
        public static readonly MiniIsland[] MiniIslands;

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

            // cave: the mouth faces north-east toward the trail from the lighthouse, the hill rises to the south-west
            CaveDir = new Vector3(-0.6f, 0f, -0.8f);
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
                new WorldSpot(sb.x - 41f, 3.2f, sb.z - 3f, 9f),
                new WorldSpot(sb.x + 40f, 2.6f, sb.z + 4f, 8f),
            };

            // in 4-8 m of water, away from the dock, the house reef, the second beach and the boat lanes
            SeaStacks = new[]
            {
                new WorldSpot(-68f, 6f, 188f, 5f),
                new WorldSpot(-186f, 7.5f, 108f, 6f),
                new WorldSpot(-244f, 8.5f, 43f, 6.5f),
                new WorldSpot(-204f, 6.5f, -172f, 7f),
                new WorldSpot(-75f, 5f, -206f, 5f),
                new WorldSpot(110f, 6f, -192f, 5.5f),
                new WorldSpot(40f, 4.5f, 230f, 4.5f),
            };

            MiniIslands = new[]
            {
                new MiniIsland { id = "kelp_forest", center = WorldLayout.KelpIsland, shoreRadius = 35f, peak = 7f, steepness = 1.0f, rocky = 0.15f },
                new MiniIsland { id = "wreck", center = WorldLayout.WreckIsland, shoreRadius = 35f, peak = 10f, steepness = 1.4f, rocky = 0.6f },
                new MiniIsland { id = "blue_drop", center = WorldLayout.BlueDropIsland, shoreRadius = 33f, peak = 12f, steepness = 3.2f, rocky = 0.85f },
                new MiniIsland { id = "deep_trench", center = WorldLayout.TrenchIsland, shoreRadius = 34f, peak = 4f, steepness = 4.6f, rocky = 0.1f },
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
