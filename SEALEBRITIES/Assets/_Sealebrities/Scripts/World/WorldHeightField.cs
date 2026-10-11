using System;

namespace Sealebrities
{
    /// Analytic height function of the whole map in world meters (y relative to sea level 0).
    /// Thread-safe and allocation-free: only reads cached floats and WorldNoise, so the terrain builder
    /// evaluates it on worker threads. Layout comes from WorldLayout, WorldFeatures and DiveSites.
    /// Deterministic: no time or random input – every co-op machine builds the identical island.
    public static class WorldHeightField
    {
        public const float HutHeight = 2.5f;
        public const float HelipadHeight = 4f;
        public const float LighthouseHeight = 30f;
        const float IslandOuter = 195f;          // POI / plateau evaluation radius (TV3 300, 0.75 plan 230)
        const float S = WorldLayout.IslandScale; // 0.62 (L.5): map-scale lengths of the Testversion-3 island × S
        const float HS = 0.72f;                  // interior hill height scale
        const float LagoonWidth = 70f, LagoonWidthCliff = 22f, LagoonEdgeDepth = 3.4f;
        const float OceanStart = 200f;           // TV3 250
        const float ArmStart = 146f;             // TV3 235
        const float ReefInnerR = 115f, ReefOuterR = 255f;   // TV3 185 / 372
        static readonly float VilX = WorldLayout.Village.x, VilZ = WorldLayout.Village.z;
        // the swimming hole is centred 4 m seaward of the raft so it slopes in gently from the beach
        static readonly float SwimX = WorldLayout.SwimRaft.x + 4f, SwimZ = WorldLayout.SwimRaft.z;
        public const float MinHeight = WorldLayout.TerrainMinY + 2f;
        public const float MaxHeight = WorldLayout.TerrainMinY + WorldLayout.TerrainHeightRange - 2f;

        struct Site { public float x, z, r, minD, maxD; }

        // Layout cache (plain floats so worker threads never touch Unity structs after init).
        static readonly float HutX, HutZ, HeliX, HeliZ, DockStartX, DockEndX, DockZ, LhX, LhZ, BayX, BayZ;
        static readonly float CoveX, CoveZ, CoveR, MouthX, MouthZ;
        static readonly Site Reef, Kelp, Wreck, Blue, Trench, Fog;
        static readonly float BlueDirX, BlueDirZ, TrenchAxX, TrenchAxZ;

        // points of interest (GDD 15.33)
        static readonly float FallX, FallZ, FallAX, FallAZ, PoolX, PoolZ, PoolDist;
        static readonly float CaveX, CaveZ, CaveDX, CaveDZ, CaveChamberS;
        static readonly float BridgeX;
        static readonly float TideX, TideZ, TideUX, TideUZ, TideVX, TideVZ;
        static readonly float[] TpX, TpZ, TpR;
        static readonly float RadioX, RadioZ;
        static readonly float SbX, SbZ;
        static readonly float[] HlX, HlZ, HlR, HlH;
        static readonly float[] StX, StZ, StR, StH;
        static readonly float[] MiX, MiZ, MiR, MiPeak, MiK, MiRock, MiBeachX, MiBeachZ, MiReach;
        // detail pass (round 2b): coves, rock ledges, skerries, lagoon patch reefs, house-reef detail zone
        static readonly float[] CvX, CvZ, CvR;
        static readonly float[] SkX, SkZ, SkR, SkH;
        static readonly float[] PrX, PrZ, PrR, PrTop;
        static readonly float RdX0, RdZ0, RdX1, RdZ1;
        static float[] LdX, LdZ, LdR, LdTop;     // filled last in the static constructor (the tops need the finished ground)

        static WorldHeightField()
        {
            HutX = WorldLayout.Hut.x; HutZ = WorldLayout.Hut.z;
            HeliX = WorldLayout.Helipad.x; HeliZ = WorldLayout.Helipad.z;
            DockStartX = WorldLayout.DockStart.x; DockEndX = WorldLayout.DockEnd.x; DockZ = WorldLayout.DockEnd.z;
            LhX = WorldLayout.Lighthouse.x; LhZ = WorldLayout.Lighthouse.z;
            BayX = WorldLayout.HiddenBay.x; BayZ = WorldLayout.HiddenBay.z;

            CoveX = BayX + 4f; CoveZ = BayZ; CoveR = 19f;
            MouthX = BayX - 37f; MouthZ = BayZ - 11f;

            Reef = SiteOf("house_reef", 188f, 38f, 105f, 3f, 12f);
            Fog = SiteOf("fog_isle", -60f, 640f, 85f, 4f, 28f);
            Kelp = SiteOf("kelp_forest", -340f, 300f, 130f, 10f, 25f);
            Wreck = SiteOf("wreck", 460f, -380f, 110f, 22f, 40f);
            Blue = SiteOf("blue_drop", -620f, -540f, 170f, 30f, 75f);
            Trench = SiteOf("deep_trench", 760f, 660f, 190f, 80f, 160f);

            float bl = (float)Math.Sqrt(Blue.x * Blue.x + Blue.z * Blue.z);
            BlueDirX = -Blue.x / bl; BlueDirZ = -Blue.z / bl;           // points from the drop-off toward the island
            TrenchAxX = 0.783f; TrenchAxZ = -0.622f;                     // canyon runs NW -> SE

            // ---- points of interest
            FallX = WorldLayout.WaterfallTop.x; FallZ = WorldLayout.WaterfallTop.z;
            FallAX = WorldFeatures.FallDir.x; FallAZ = WorldFeatures.FallDir.z;
            PoolX = WorldLayout.WaterfallPool.x; PoolZ = WorldLayout.WaterfallPool.z;
            PoolDist = Dist(PoolX, PoolZ, FallX, FallZ);

            CaveX = WorldLayout.CaveEntrance.x; CaveZ = WorldLayout.CaveEntrance.z;
            CaveDX = WorldFeatures.CaveDir.x; CaveDZ = WorldFeatures.CaveDir.z;
            CaveChamberS = WorldFeatures.CaveLength + WorldFeatures.CaveChamberRadius - 1.5f;

            BridgeX = WorldLayout.Ravine.x;

            TideX = WorldLayout.TidePools.x; TideZ = WorldLayout.TidePools.z;
            TideUX = WorldFeatures.TideAlong.x; TideUZ = WorldFeatures.TideAlong.z;
            TideVX = WorldFeatures.TideSeaward.x; TideVZ = WorldFeatures.TideSeaward.z;
            var tp = WorldFeatures.TidePoolSpots;
            TpX = new float[tp.Length]; TpZ = new float[tp.Length]; TpR = new float[tp.Length];
            for (int i = 0; i < tp.Length; i++) { TpX[i] = tp[i].position.x; TpZ[i] = tp[i].position.z; TpR[i] = tp[i].radius / 0.72f; }

            RadioX = WorldLayout.RadioTowerRuins.x; RadioZ = WorldLayout.RadioTowerRuins.z;
            SbX = WorldLayout.SecondBeach.x; SbZ = WorldLayout.SecondBeach.z;

            var hl = WorldFeatures.SecondBeachHeadlands;
            HlX = new float[hl.Length]; HlZ = new float[hl.Length]; HlR = new float[hl.Length]; HlH = new float[hl.Length];
            for (int i = 0; i < hl.Length; i++) { HlX[i] = hl[i].position.x; HlZ[i] = hl[i].position.z; HlR[i] = hl[i].radius; HlH[i] = hl[i].position.y; }

            var st = WorldFeatures.SeaStacks;
            StX = new float[st.Length]; StZ = new float[st.Length]; StR = new float[st.Length]; StH = new float[st.Length];
            for (int i = 0; i < st.Length; i++) { StX[i] = st[i].position.x; StZ[i] = st[i].position.z; StR[i] = st[i].radius; StH[i] = st[i].position.y; }

            var mi = WorldFeatures.MiniIslands;
            int n = mi.Length;
            MiX = new float[n]; MiZ = new float[n]; MiR = new float[n]; MiPeak = new float[n]; MiK = new float[n]; MiRock = new float[n];
            MiBeachX = new float[n]; MiBeachZ = new float[n]; MiReach = new float[n];
            for (int i = 0; i < n; i++)
            {
                MiX[i] = mi[i].center.x; MiZ[i] = mi[i].center.z; MiR[i] = mi[i].shoreRadius; MiPeak[i] = mi[i].peak;
                MiK[i] = mi[i].steepness; MiRock[i] = mi[i].rocky;
                // the sandy side of every landmark island faces its dive site (where the boat comes from)
                var site = DiveSites.Get(mi[i].id);
                float sx = site != null ? site.center.x - MiX[i] : -MiX[i], sz = site != null ? site.center.z - MiZ[i] : -MiZ[i];
                float sl = MathF.Sqrt(sx * sx + sz * sz);
                MiBeachX[i] = sl > 1e-3f ? sx / sl : 1f; MiBeachZ[i] = sl > 1e-3f ? sz / sl : 0f;
                MiReach[i] = MiR[i] + 4.5f + 6f + 175f / MathF.Max(0.5f, MiK[i]);
            }

            // ---- detail pass
            Spots(WorldFeatures.SmallCoves, out CvX, out CvZ, out CvR, out _);
            Spots(WorldFeatures.Skerries, out SkX, out SkZ, out SkR, out SkH);
            Spots(WorldFeatures.LagoonPatchReefs, out PrX, out PrZ, out PrR, out PrTop);
            var rd = WorldFeatures.ReefDetailRect;
            RdX0 = rd.x; RdZ0 = rd.y; RdX1 = rd.z; RdZ1 = rd.w;
            // rock ledges sit on the finished slope: their flat tops are measured last (the ledge arrays are still null,
            // so Height() skips them while measuring)
            var ld = WorldFeatures.RockLedges;
            var top = new float[ld.Length];
            for (int i = 0; i < ld.Length; i++) top[i] = Height(ld[i].position.x, ld[i].position.z) + ld[i].position.y;
            Spots(ld, out var lx, out var lz, out var lr, out _);
            LdX = lx; LdZ = lz; LdR = lr; LdTop = top;
        }

        static void Spots(WorldSpot[] s, out float[] x, out float[] z, out float[] r, out float[] y)
        {
            int n = s != null ? s.Length : 0;
            x = new float[n]; z = new float[n]; r = new float[n]; y = new float[n];
            for (int i = 0; i < n; i++) { x[i] = s[i].position.x; z[i] = s[i].position.z; r[i] = s[i].radius; y[i] = s[i].position.y; }
        }

        static Site SiteOf(string id, float x, float z, float r, float minD, float maxD)
        {
            var s = DiveSites.Get(id);
            if (s == null) return new Site { x = x, z = z, r = r, minD = minD, maxD = maxD };
            return new Site { x = s.center.x, z = s.center.z, r = s.radius, minD = s.minDepth, maxD = s.maxDepth };
        }

        // ------------------------------------------------------------------ math helpers
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        /// Works with e0 > e1 too (falling edge).
        static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
        static float Gauss(float x, float z, float cx, float cz, float sigma)
        {
            float dx = x - cx, dz = z - cz;
            return MathF.Exp(-(dx * dx + dz * dz) / (sigma * sigma));
        }
        static float Dist(float x, float z, float cx, float cz)
        {
            float dx = x - cx, dz = z - cz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        static float DistSeg(float px, float pz, float ax, float az, float bx, float bz, out float t)
        {
            float abx = bx - ax, abz = bz - az;
            t = Clamp01(((px - ax) * abx + (pz - az) * abz) / (abx * abx + abz * abz));
            float dx = px - (ax + abx * t), dz = pz - (az + abz * t);
            return MathF.Sqrt(dx * dx + dz * dz);
        }
        /// Noise around a centre that only depends on the direction (seamless all the way round, unlike a noise of the
        /// atan2 angle): k = how many wobbles per turn (roughly), seed = offset.
        static float RingNoise(float dx, float dz, float k, float seed)
        {
            float l = MathF.Sqrt(dx * dx + dz * dz);
            if (l < 1e-4f) return 0f;
            return WorldNoise.Perlin(dx / l * k + seed, dz / l * k - seed * 0.7f);
        }

        static float Hash01(float x, float z, int seed) =>
            WorldTerrainData.Hash01((int)MathF.Floor(x * 3f), (int)MathF.Floor(z * 3f), seed);

        // ------------------------------------------------------------------ public API
        /// Terrain height in world meters at (x, z).
        public static float Height(float x, float z)
        {
            float r = MathF.Sqrt(x * x + z * z);
            float h = IslandBase(x, z, r);

            // deep, smooth terrain border
            float edge = SmoothStep(860f, 1012f, MathF.Max(MathF.Abs(x), MathF.Abs(z)));
            if (edge > 0f) h = Lerp(h, -128f, edge);

            if (r < IslandOuter)
            {
                // points of interest on the main island, then the flat building plateaus (they always win)
                h = Features(x, z, h);
                h = Lerp(h, HutHeight, 1f - SmoothStep(14f, 30f, Dist(x, z, HutX, HutZ)));
                h = Lerp(h, HelipadHeight, 1f - SmoothStep(12f, 28f, Dist(x, z, HeliX, HeliZ)));
                h = Lerp(h, LighthouseHeight, 1f - SmoothStep(9f, 17f, Dist(x, z, LhX, LhZ)));
            }

            // dive sites (each blends into its surroundings outside its radius)
            float dReef = Dist(x, z, Reef.x, Reef.z);
            if (dReef < Reef.r + 70f)
            {
                float w = 1f - SmoothStep(Reef.r, Reef.r + 70f, dReef);
                // the reef only raises the seafloor: it never sinks the island or the beach
                h = Lerp(h, MathF.Max(h, -HouseReefDepth(x, z, r)), w);
            }

            h = DockBeach(x, z, h);
            h = SwimHole(x, z, h);

            float dKelp = Dist(x, z, Kelp.x, Kelp.z);
            if (dKelp < Kelp.r + 110f)
                h = Lerp(h, -KelpDepth(x, z), 1f - SmoothStep(Kelp.r, Kelp.r + 110f, dKelp));

            float dWreck = Dist(x, z, Wreck.x, Wreck.z);
            if (dWreck < Wreck.r + 110f)
                h = Lerp(h, -WreckDepth(x, z, dWreck), 1f - SmoothStep(Wreck.r, Wreck.r + 110f, dWreck));

            float dBlue = Dist(x, z, Blue.x, Blue.z);
            if (dBlue < Blue.r + 90f)
                h = Lerp(h, -BlueDropDepth(x, z), 1f - SmoothStep(Blue.r, Blue.r + 90f, dBlue));

            float dTrench = Dist(x, z, Trench.x, Trench.z);
            if (dTrench < Trench.r + 80f)
                h = Lerp(h, -TrenchDepth(x, z), 1f - SmoothStep(Trench.r, Trench.r + 80f, dTrench));

            float dFog = Dist(x, z, Fog.x, Fog.z);
            if (dFog < Fog.r + 110f)
                h = Lerp(h, -FogDepth(x, z, dFog), 1f - SmoothStep(Fog.r, Fog.r + 110f, dFog));

            // landmark islands next to the outer sites (they only ever raise the ground)
            if (r > 400f) h = MiniIslands(x, z, h);

            return Clamp(h, MinHeight, MaxHeight);
        }

        /// 0..1 hint for rocky ground that is not explained by slope alone (reef bommies, kelp rock floor).
        public static float RockHint(float x, float z)
        {
            float rock = 0f;
            float r = MathF.Sqrt(x * x + z * z);
            float dReef = Dist(x, z, Reef.x, Reef.z);
            if (dReef < Reef.r + 40f)
            {
                float site = 1f - SmoothStep(Reef.r, Reef.r + 40f, dReef);
                rock = MathF.Max(rock, BommieMask(x, z) * 0.9f * site);
                // L.3: the crest is coral rubble, the spurs between the grooves are rock (the grooves stay sandy)
                float g = Groove(x, z, r, out float fore);
                rock = MathF.Max(rock, site * MathF.Max(0.55f * Crest(x, z, r), 0.8f * fore * (1f - SmoothStep(0.05f, 0.4f, g))));
            }
            float dKelp = Dist(x, z, Kelp.x, Kelp.z);
            if (dKelp < Kelp.r + 60f)
            {
                float k = 0.3f + 0.7f * SmoothStep(0.35f, 0.8f, KelpRidge(x, z));
                rock = MathF.Max(rock, k * (1f - SmoothStep(Kelp.r, Kelp.r + 60f, dKelp)));
            }
            float dBlue = Dist(x, z, Blue.x, Blue.z);
            if (dBlue < Blue.r + 30f)
                rock = MathF.Max(rock, 0.35f * SmoothStep(0.1f, 0.5f, WorldNoise.Perlin(x * 0.05f + 3f, z * 0.05f - 2f)));
            // the flanks of the landmark islands and sea stacks are bare rock
            if (r > 400f)
            {
                for (int i = 0; i < MiX.Length; i++)
                {
                    float d = Dist(x, z, MiX[i], MiZ[i]);
                    if (d < MiReach[i]) rock = MathF.Max(rock, (0.45f + 0.5f * MiRock[i]) * SmoothStep(MiR[i] + 7f, MiR[i] + 12f, d) * (1f - SmoothStep(MiReach[i] - 25f, MiReach[i], d)));
                }
            }
            else if (r < IslandOuter)
            {
                for (int i = 0; i < StX.Length; i++)
                    rock = MathF.Max(rock, 1f - SmoothStep(StR[i] + 2f, StR[i] + 7f, Dist(x, z, StX[i], StZ[i])));
                rock = MathF.Max(rock, TideMask(x, z));
                // skerries and the lagoon patch reefs (rock under the coral, the sand halo stays sand)
                for (int i = 0; i < SkX.Length; i++)
                    rock = MathF.Max(rock, 1f - SmoothStep(SkR[i] + 0.5f, SkR[i] + 3f, Dist(x, z, SkX[i], SkZ[i])));
                for (int i = 0; i < PrX.Length; i++)
                    rock = MathF.Max(rock, 0.95f * (1f - SmoothStep(PrR[i] * 0.55f, PrR[i] * 0.95f, Dist(x, z, PrX[i], PrZ[i]))));
            }
            return rock;
        }

        /// 0..1 how strongly the ground is a trampled, sandy/dirt area (hut yard, helipad, paths).
        public static float YardHint(float x, float z)
        {
            float a = 1f - SmoothStep(15f, 24f, Dist(x, z, HutX, HutZ));
            float b = 1f - SmoothStep(13f, 20f, Dist(x, z, HeliX, HeliZ));
            float c = 1f - SmoothStep(10f, 16f, Dist(x, z, LhX, LhZ));
            return MathF.Max(a, MathF.Max(b, c));
        }

        /// What a terrain feature wants the ground to look like at (x, z) with ground height h (thread-safe,
        /// deterministic, per-position dithering at the feature borders). None = the regular colour rules apply.
        public static GroundHint Hint(float x, float z, float h)
        {
            float r = MathF.Sqrt(x * x + z * z);
            if (r > 400f) return MiniIslandHint(x, z, h);
            if (r > IslandOuter) return GroundHint.None;
            float dither = Hash01(x, z, 5151);

            // tide pool rock platform
            float tide = TideMask(x, z);
            if (tide > 0.2f + 0.6f * dither && h > -1.4f) return GroundHint.Rock;

            // sea stacks and the rocky headlands of the second beach
            for (int i = 0; i < StX.Length; i++)
                if (Dist(x, z, StX[i], StZ[i]) < StR[i] + 1.5f + 2f * dither) return GroundHint.Rock;
            for (int i = 0; i < HlX.Length; i++)
            {
                float d = Dist(x, z, HlX[i], HlZ[i]);
                if (d < HlR[i] * (0.55f + 0.35f * dither) && h > -2f) return GroundHint.Rock;
            }
            // detail pass: skerries and the rock ledges on the hill flanks (a few mossy facets on the ledge tops)
            for (int i = 0; i < SkX.Length; i++)
                if (Dist(x, z, SkX[i], SkZ[i]) < SkR[i] + 1f + 1.5f * dither) return GroundHint.Rock;
            var lx = LdX;
            if (lx != null && h > 0.5f)
            {
                for (int i = 0; i < lx.Length; i++)
                {
                    float d = Dist(x, z, lx[i], LdZ[i]);
                    if (d > LdR[i] + 1.6f) continue;
                    if (h < LdTop[i] - 0.6f || dither > 0.22f) return GroundHint.Rock;
                    break;
                }
            }

            // waterfall: pool basin and its little beach, the creek bed, the rocky lip
            float dp = Dist(x, z, PoolX, PoolZ);
            if (dp < WorldFeatures.PoolRadius + 4.5f)
            {
                if (h < WorldFeatures.PoolWaterY + 0.12f) return GroundHint.WetSand;
                float s0 = (x - FallX) * FallAX + (z - FallZ) * FallAZ;
                if (s0 > PoolDist - 2f || dp < WorldFeatures.PoolRadius + 1.5f + 2.5f * dither) return GroundHint.Sand;
            }
            float fx = x - FallX, fz = z - FallZ;
            if (fx * fx + fz * fz < 40f * 40f)
            {
                float s = fx * FallAX + fz * FallAZ, q = -fx * FallAZ + fz * FallAX;
                float cq = CreekQ(s, q);
                if (MathF.Abs(cq) < 1.6f && s > -14f && s < PoolDist - WorldFeatures.PoolRadius + 2f) return GroundHint.WetSand;
                if (MathF.Abs(q) < 6f + 3f * dither && s > -5f - 2f * dither && s < 3f) return GroundHint.Rock;
            }

            // cave: rocky passage, chamber and mouth
            float cx = x - CaveX, cz = z - CaveZ;
            if (cx * cx + cz * cz < 30f * 30f)
            {
                float s = cx * CaveDX + cz * CaveDZ, q = -cx * CaveDZ + cz * CaveDX;
                if (CaveCarve(s, q) > 0.25f) return GroundHint.Rock;
                if (s > -4f - 3f * dither && s < 1.5f && MathF.Abs(q) < 12f) return GroundHint.Rock;
                if (s < 0f && s > -9f - 3f * dither && MathF.Abs(q) < 6f + 3f * dither) return GroundHint.Soil;
            }

            // ravine floor: dry gravel bed
            if (x > WorldFeatures.RavineWestX && x < WorldFeatures.RavineEastX)
            {
                float q = z - WorldFeatures.RavineAxisZ(x);
                if (MathF.Abs(q) < WorldFeatures.RavineHalfWidthFloor + 0.6f + 1.2f * dither && RavineDepthAt(x) > 2f) return GroundHint.Soil;
            }

            // radio tower ruins: trampled ground and old concrete
            if (Dist(x, z, RadioX, RadioZ) < 8f + 4f * dither) return GroundHint.Soil;
            return GroundHint.None;
        }

        // ------------------------------------------------------------------ island
        /// Island and open sea without any point-of-interest shaping, building plateau or dive site.
        static float IslandBase(float x, float z, float r)
        {
            if (r < 470f)
            {
                Coast(x, z, r, out float L, out float cliff);
                return L > 0f ? LandHeight(x, z, L, cliff) : -SeaDepth(x, z, r, -L, cliff);
            }
            return -SeaDepth(x, z, r, r - WorldLayout.IslandRadius, 0f);
        }

        /// Signed distance to the shoreline (L > 0 on land), plus a 0..1 cliff factor.
        static void Coast(float x, float z, float r, out float L, out float cliff)
        {
            // round 2b: every map-scale length × S (0.62), noise frequencies ÷ S – same coastline shape, 62 % size
            float east = Gauss(x, z, 178f * S, 10f * S, 85f * S);
            float bay = Gauss(x, z, BayX - 10f * S, BayZ - 5f * S, 60f * S);
            float lhN = Gauss(x, z, LhX, LhZ + 40f * S, 50f * S);
            // the second beach: a wide sandy bulge on the south coast
            float sb = Gauss(x, z, SbX, SbZ - 8f * S, 50f * S);
            float calm = 1f - 0.85f * MathF.Max(MathF.Max(east, sb), MathF.Max(bay, lhN));

            float th = MathF.Atan2(z, x);
            float lobes = S * (17f * MathF.Sin(2f * th + 0.9f) + 11f * MathF.Sin(3f * th + 2.4f) + 6f * MathF.Sin(5f * th + 1f)
                          // a richer, more crinkled coastline: small coves and points between the big lobes
                          + 5.5f * MathF.Sin(7f * th + 0.4f) + 3.2f * MathF.Sin(11f * th + 2.1f) + 1.8f * MathF.Sin(17f * th + 4.4f));
            float xs = x / S, zs = z / S;   // noise is sampled in "old island" space
            float R = WorldLayout.IslandRadius
                      + calm * S * (lobes / S + 36f * WorldNoise.Fbm(xs * 0.0085f + 11.3f, zs * 0.0085f - 4.1f, 3)
                                + 9f * WorldNoise.Perlin(xs * 0.03f + 2.2f, zs * 0.03f + 6.6f)
                                + 4f * WorldNoise.Perlin(xs * 0.075f - 5.1f, zs * 0.075f + 1.9f))
                      + S * (-14f * east + 34f * bay - 7f * lhN + 12f * sb
                      // a slightly wider beach between the second beach and the tide pools (the coastal trail runs there)
                      + 7f * Gauss(x, z, 98f * S, -152f * S, 30f * S));
            float L0 = R - r;

            // small coves (detail pass): each bites a round bay ~1.1 R deep and ~2 R wide into the coast; their backs are
            // sandy (the cliff factor fades out inside), the rock or cliff around them stays
            float coveCore = 0f;
            for (int i = 0; i < CvX.Length; i++)
            {
                float dx = x - CvX[i], dz = z - CvZ[i], r2 = CvR[i] * CvR[i];
                float d2 = dx * dx + dz * dz;
                if (d2 > 9f * r2) continue;
                float g = MathF.Exp(-d2 / r2);
                float wob = 1f + 0.12f * WorldNoise.Perlin(x * 0.21f + i * 3.1f, z * 0.21f - i * 1.7f);
                L0 -= 2.6f * CvR[i] * g * wob;
                if (g > coveCore) coveCore = g;
            }

            // hidden cove: roundish basin with an angled, narrow mouth
            float basin = Dist(x, z, CoveX, CoveZ) - CoveR - 3f * WorldNoise.Perlin(xs * 0.06f + 3.3f, zs * 0.06f + 1.1f);
            float channel = DistSeg(x, z, CoveX, CoveZ, MouthX, MouthZ, out _) - 8f;
            L = MathF.Min(L0, MathF.Min(basin, channel));

            float cliffL = Gauss(x, z, LhX, LhZ + 30f * S, 48f * S);
            float cliffB = Gauss(x, z, CoveX - 5f * S, CoveZ, 58f * S);
            float cliffN = 0.75f * SmoothStep(0.15f, 0.45f, WorldNoise.Perlin(xs * 0.011f + 40.1f, zs * 0.011f - 12.7f)) * (1f - east);
            cliff = Clamp01(MathF.Max(cliffL, MathF.Max(cliffB, cliffN)));
            cliff *= (1f - CoveBeach(x, z)) * (1f - SmoothStep(0.2f, 0.6f, sb));
            if (coveCore > 0.3f) cliff *= 1f - 0.8f * SmoothStep(0.3f, 0.75f, coveCore);
        }

        /// 1 on the small sandy beach at the inner (east) end of the hidden cove.
        static float CoveBeach(float x, float z) => 1f - SmoothStep(10f, 22f, Dist(x, z, CoveX + CoveR + 4f, CoveZ));

        static float Lowland(float x, float z)
        {
            float a = 1f - SmoothStep(30f * S, 90f * S, Dist(x, z, HutX, HutZ));
            float b = 1f - SmoothStep(24f * S, 75f * S, Dist(x, z, HeliX, HeliZ));
            float c = 1f - SmoothStep(14f * S, 55f * S, DistSeg(x, z, HutX, HutZ, HeliX, HeliZ, out _));
            float d = 1f - SmoothStep(30f * S, 80f * S, Dist(x, z, DockStartX - 8f, DockZ));
            return MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
        }

        static float Interior(float x, float z)
        {
            float r = MathF.Sqrt(x * x + z * z);
            float xs = x / S, zs = z / S;
            // jungle hills (west/center), flattening into a coastal plain toward the east where hut, dock and helipad are
            float hills = HS * (6f + 26f * Clamp01(0.45f + 1.1f * WorldNoise.Fbm(xs * 0.0085f + 3.7f, zs * 0.0085f + 9.1f, 4)));
            hills += HS * 7f * (1f - SmoothStep(0f, 170f * S, r));
            hills += HS * 2.2f * WorldNoise.Perlin(xs * 0.035f + 1.1f, zs * 0.035f - 3.3f);
            hills = MathF.Min(hills, HS * (30f + 2f * WorldNoise.Perlin(xs * 0.05f, zs * 0.05f)));
            hills *= Lerp(1f, 0.22f, SmoothStep(-10f * S, 100f * S, x));
            // ridges flanking the hidden cove keep it hidden
            hills += HS * (11f * Gauss(x, z, CoveX - 12f * S, CoveZ + 42f * S, 28f * S) + 10f * Gauss(x, z, CoveX - 18f * S, CoveZ - 46f * S, 26f * S));
            // lighthouse hill with a flat top
            float dl = Dist(x, z, LhX, LhZ);
            float south = Clamp01((LhZ - z) / MathF.Max(dl, 1f));   // inland (south) flank is long and gentle
            float lh = LighthouseHeight * MathF.Pow(1f - SmoothStep(9f, 80f * S + 55f * south, dl), 1.25f);
            hills = MathF.Max(hills, lh);

            float flat = 2.7f + 1.0f * WorldNoise.Perlin(xs * 0.02f + 8f, zs * 0.02f + 8f);
            hills = Lerp(hills, flat - 0.4f, CoveBeach(x, z));
            float low = Lowland(x, z);
            if (low <= 0f) return hills;
            return Lerp(hills, flat, low);
        }

        static float LandHeight(float x, float z, float L, float cliff)
        {
            float H = Interior(x, z);
            float beach = MathF.Min(L * 0.085f, 2.3f);
            // the second beach is a wide, flat strip of sand before the jungle rises
            float wide = 18f * S * Gauss(x, z, SbX, SbZ - 8f * S, 55f * S);
            float s = SmoothStep(wide, wide + Lerp(60f * S, 5f, cliff), L);
            float h = Lerp(beach, H, s);
            h += 0.5f * WorldNoise.Perlin(x * 0.09f + 0.3f, z * 0.09f + 0.7f) * s;
            return h;
        }

        // ------------------------------------------------------------------ points of interest
        static float Features(float x, float z, float h)
        {
            h = Waterfall(x, z, h);
            h = CaveHill(x, z, h);
            h = Ravine(x, z, h);
            h = TidePlatform(x, z, h);
            // walkable gully through the hidden cove's southern ridge down to its beach
            var ga = WorldFeatures.BayGullyTop;
            var gb = WorldFeatures.BayGullyBottom;
            float dg = DistSeg(x, z, ga.x, ga.z, gb.x, gb.z, out float gt);
            if (dg < 5f) h = Lerp(h, Lerp(ga.y, gb.y, gt), 1f - SmoothStep(2.5f, 5f, dg));
            var cra = WorldFeatures.CaveRampTop;
            var crb = WorldFeatures.CaveRampBottom;
            if (cra != crb)
            {
                float dcr = DistSeg(x, z, cra.x, cra.z, crb.x, crb.z, out float crt);
                if (dcr < 5.5f) h = Lerp(h, Lerp(cra.y, crb.y, crt), 1f - SmoothStep(2.6f, 5.5f, dcr));
            }
            // village terrace behind the second beach (round 2b): flat ellipse, blends over 9 m
            {
                float ex = (x - VilX) / WorldFeatures.VillageHalfX, ez = (z - VilZ) / WorldFeatures.VillageHalfZ;
                float e = MathF.Sqrt(ex * ex + ez * ez);                    // 1 on the terrace edge
                float edgeM = (e - 1f) * MathF.Min(WorldFeatures.VillageHalfX, WorldFeatures.VillageHalfZ);
                if (edgeM < 9f)
                {
                    float terr = WorldFeatures.VillageTerraceY + 0.12f * WorldNoise.Perlin(x * 0.2f + 4f, z * 0.2f);
                    h = Lerp(h, terr, 1f - SmoothStep(0f, 9f, edgeM));
                }
            }
            float dr = Dist(x, z, RadioX, RadioZ);
            if (dr < WorldFeatures.RadioPlateauRadius + 14f)
            {
                float plat = WorldFeatures.RadioPlateauY + 0.15f * WorldNoise.Perlin(x * 0.2f + 6f, z * 0.2f);
                h = Lerp(h, plat, 1f - SmoothStep(WorldFeatures.RadioPlateauRadius, WorldFeatures.RadioPlateauRadius + 13f, dr));
            }
            // rocky points at both ends of the second beach
            for (int i = 0; i < HlX.Length; i++)
            {
                float d = Dist(x, z, HlX[i], HlZ[i]);
                if (d > HlR[i] + 2f) continue;
                float mound = HlH[i] * (1f - SmoothStep(HlR[i] * 0.25f, HlR[i], d)) + 0.7f * WorldNoise.Perlin(x * 0.35f + i * 3f, z * 0.35f) - 0.9f * SmoothStep(HlR[i] * 0.6f, HlR[i] + 2f, d);
                h = MathF.Max(h, mound);
            }
            // sea stacks: steep rocky islets off the coast
            for (int i = 0; i < StX.Length; i++)
            {
                float d = Dist(x, z, StX[i], StZ[i]);
                float rr = StR[i];
                if (d > rr + 9f) continue;
                float top = StH[i] + 0.9f * WorldNoise.Perlin(x * 0.4f + i * 5.1f, z * 0.4f - i);
                float prof = top * (1f - SmoothStep(rr * 0.45f, rr, d)) - 0.6f - 3.5f * SmoothStep(rr * 0.85f, rr + 8f, d);
                h = MathF.Max(h, prof);
            }
            return Details(x, z, h);
        }

        /// Swimming hole under the swim raft (L.5 detail spot 3): the dock beach is only ~0.7 m deep there, the diving
        /// board needs ~3 m – a sandy hollow that slopes in gently from the beach (applied after the dock beach).
        static float SwimHole(float x, float z, float h)
        {
            float dx = x - SwimX, dz = z - SwimZ;
            float d2 = dx * dx + dz * dz;
            if (d2 > 16f * 16f || h > 0.5f) return h;
            // an irregular, sandy hollow (not a round bowl)
            float wob = 1f + 0.22f * RingNoise(dx, dz, 1.6f, 7.7f) + 0.1f * WorldNoise.Perlin(x * 0.3f, z * 0.3f);
            float hole = -3.0f * (1f - SmoothStep(2.5f * wob, 13.5f * wob, MathF.Sqrt(d2)));
            return hole < h ? hole : h;
        }

        /// Detail pass (round 2b): rock ledges on the hill flanks, skerries off the cliff coasts and the lagoon patch
        /// reefs. Each only ever raises the ground (ledges also never touch the sea).
        static float Details(float x, float z, float h)
        {
            // rock ledges: a slightly tilted flat top with near-vertical sides that juts out of the slope
            var lx = LdX;
            if (lx != null && h > 0.5f)
            {
                for (int i = 0; i < lx.Length; i++)
                {
                    float dx = x - lx[i], dz = z - LdZ[i], rr = LdR[i];
                    float d2 = dx * dx + dz * dz, lim = rr + 1.6f;
                    if (d2 > lim * lim) continue;
                    float d = MathF.Sqrt(d2);
                    float top = LdTop[i] + 0.22f * WorldNoise.Perlin(x * 0.45f + i * 2.7f, z * 0.45f - i) + 0.05f * (dx - dz);
                    float edge = rr * (0.6f + 0.2f * RingNoise(dx, dz, 1.3f, i * 5f) + 0.12f * RingNoise(dx, dz, 3.1f, i + 2.5f));
                    float prof = top - 14f * SmoothStep(edge, lim, d);
                    if (prof > h) h = prof;
                }
            }
            if (h > 1.5f) return h;

            // skerries: two or three flat rocks just above the water
            for (int i = 0; i < SkX.Length; i++)
            {
                float dx = x - SkX[i], dz = z - SkZ[i], rr = SkR[i];
                float d2 = dx * dx + dz * dz, lim = rr + 5f;
                if (d2 > lim * lim) continue;
                float d = MathF.Sqrt(d2);
                // a lumpy outline: the rock splits into knobs where the noise dips
                float lump = WorldNoise.Perlin(x * 0.8f + i * 7.3f, z * 0.8f - i * 2.1f);
                float rim = rr * (0.7f + 0.45f * lump + 0.15f * RingNoise(dx, dz, 2.3f, i * 3f + 1.1f));
                float prof = SkH[i] + 0.25f * WorldNoise.Perlin(x * 0.9f, z * 0.9f + i) - (SkH[i] + 2.2f) * SmoothStep(rim * 0.5f, rim, d)
                             - 2f * SmoothStep(rim, rim + 3f, d);
                if (prof > h) h = prof;
            }

            // lagoon patch reefs: a knobbly rock mound (coral grows on it) in a 0.3 m deeper ring of bare sand
            for (int i = 0; i < PrX.Length; i++)
            {
                float dx = x - PrX[i], dz = z - PrZ[i], rr = PrR[i];
                float d2 = dx * dx + dz * dz, lim = rr + 6f;
                if (d2 > lim * lim) continue;
                float d = MathF.Sqrt(d2);
                float outline = rr * (0.85f + 0.25f * RingNoise(dx, dz, 1.7f, i * 4.1f + 1.3f));
                float knobs = 0.35f * WorldNoise.Perlin(x * 0.7f + i, z * 0.7f - i) + 0.18f * WorldNoise.Perlin(x * 1.6f, z * 1.6f + i * 2f);
                float mound = MathF.Min(PrTop[i] + knobs, -1.3f) - 1.1f * SmoothStep(outline * 0.45f, outline + 0.8f, d);
                float halo = 0.3f * SmoothStep(outline, outline + 1.5f, d) * (1f - SmoothStep(outline + 3f, lim, d));
                if (h < -0.8f) h -= halo;
                if (mound > h) h = mound;
            }
            return h;
        }

        /// Creek centre-line offset below the falls (it meanders a little on the way to the pool).
        static float CreekQ(float s, float q) => q - 0.9f * MathF.Sin(s * 0.21f) * SmoothStep(2f, 6f, s);

        /// Waterfall: a plateau ending in an ~8 m rock cliff at WaterfallTop, an amphitheater floor below it with a
        /// creek running into the plunge pool at WaterfallPool, and a small beach on the far side of the pool.
        static float Waterfall(float x, float z, float h)
        {
            float px = x - FallX, pz = z - FallZ;
            if (px * px + pz * pz > 80f * 80f) return h;
            float s = px * FallAX + pz * FallAZ;      // + toward the pool
            float q = -px * FallAZ + pz * FallAX;     // across
            float aq = MathF.Abs(q);

            // plateau behind the cliff edge (gently rising inland)
            float back = MathF.Max(0f, -s);
            float plat = WorldFeatures.FallTopY + 0.05f * back + 0.35f * WorldNoise.Perlin(x * 0.11f + 1.7f, z * 0.11f - 2.9f) * SmoothStep(2f, 6f, back);
            float wBack = (1f - SmoothStep(17f, 30f, aq)) * (1f - SmoothStep(22f, 42f, back));
            float hBack = Lerp(h, plat, wBack);

            // amphitheater floor in front of the cliff, widening toward the pool
            float fs = MathF.Max(0f, s);
            float floorY = WorldFeatures.FallFootY - 0.02f * fs + 0.15f * WorldNoise.Perlin(x * 0.2f, z * 0.2f + 4f) * SmoothStep(3f, 8f, fs);
            float wf = 9f + 0.25f * fs;
            float wFront = (1f - SmoothStep(wf, wf + 14f, aq)) * (1f - SmoothStep(40f, 56f, fs));
            float hFront = Lerp(h, floorY, wFront);

            // the cliff itself (wobbly edge, the lip at q = 0 stays put)
            float sEdge = 0.7f * WorldNoise.Perlin(q * 0.17f + 3.1f, 7.7f) * SmoothStep(2f, 6f, aq);
            float c = SmoothStep(0.15f + sEdge, 2.4f + sEdge, s);
            float hf = Lerp(hBack, hFront, c);

            // creek: a notch on the plateau above the lip and a channel from the foot to the pool
            float cw = 1f - SmoothStep(1.0f, 2.3f, MathF.Abs(CreekQ(s, q)));
            if (cw > 0f)
            {
                float along = SmoothStep(-15f, -11f, s) * (1f - SmoothStep(PoolDist - WorldFeatures.PoolRadius + 1f, PoolDist - WorldFeatures.PoolRadius + 3f, s));
                hf -= 0.45f * cw * along;
            }

            // walkable ramp cut diagonally down the cliff (under the props' waterfall trail)
            var ra = WorldFeatures.FallRampTop;
            var rb = WorldFeatures.FallRampBottom;
            float dRamp = DistSeg(x, z, ra.x, ra.z, rb.x, rb.z, out float rt);
            if (dRamp < 4.8f) hf = Lerp(hf, Lerp(ra.y, rb.y, rt), 1f - SmoothStep(2.6f, 4.8f, dRamp));

            // plunge pool basin
            float dp = Dist(x, z, PoolX, PoolZ);
            float pr = WorldFeatures.PoolRadius;
            if (dp < pr + 6f)
            {
                float basin = WorldFeatures.PoolWaterY - 1.8f + 2.4f * SmoothStep(2f, pr + 1.5f, dp);
                float wp = 1f - SmoothStep(pr + 1.5f, pr + 5.5f, dp);
                hf = Lerp(hf, basin, wp);
            }
            return hf;
        }

        /// 0..1 how much of the cave passage/chamber is carved at (s along CaveDir, q across).
        static float CaveCarve(float s, float q)
        {
            float aq = MathF.Abs(q);
            float hw = WorldFeatures.CaveHalfWidth;
            float wP = (1f - SmoothStep(hw - 0.4f, hw + 1.2f, aq)) * SmoothStep(-3f, -1f, s)
                       * (1f - SmoothStep(WorldFeatures.CaveLength, WorldFeatures.CaveLength + 2f, s));
            float dcs = s - CaveChamberS;
            float dc = MathF.Sqrt(dcs * dcs + q * q);
            float cr = WorldFeatures.CaveChamberRadius;
            float wCh = 1f - SmoothStep(cr - 0.6f, cr + 1.2f, dc);
            return MathF.Max(wP, wCh);
        }

        /// Cave: a flat clearing at CaveEntrance facing the north-west sea, a steep rock face behind it, a hill mass
        /// above, and a 6.4 m wide passage carved into the hill ending in a round chamber (props add the roof).
        static float CaveHill(float x, float z, float h)
        {
            float px = x - CaveX, pz = z - CaveZ;
            if (px * px + pz * pz > 62f * 62f) return h;
            float s = px * CaveDX + pz * CaveDZ;      // + into the hill
            float q = -px * CaveDZ + pz * CaveDX;
            float aq = MathF.Abs(q);
            float floorY = WorldFeatures.CaveFloorY;

            float front = MathF.Max(0f, -s);
            float wC = (1f - SmoothStep(9f, 15f, aq)) * (1f - SmoothStep(10f, 17f, front));
            float hFront = Lerp(h, floorY - 0.03f * front, wC);

            float back = MathF.Max(0f, s);
            float hill = floorY + WorldFeatures.CaveHillHeight + 0.05f * back + 0.4f * WorldNoise.Perlin(x * 0.09f + 5f, z * 0.09f - 1f);
            float wH = (1f - SmoothStep(15f, 26f, aq)) * (1f - SmoothStep(30f, 46f, back));
            float hBack = Lerp(h, MathF.Max(h, hill), wH);

            float c = SmoothStep(-0.4f, 1.6f, s + 0.5f * WorldNoise.Perlin(q * 0.2f, 3.3f) * SmoothStep(4f, 8f, aq));
            float hc = Lerp(hFront, hBack, c);
            float carve = CaveCarve(s, q);
            return Lerp(hc, floorY + 0.02f * back, carve);
        }

        /// Depth of the ravine at world x (fades out over 16 m at both ends).
        static float RavineDepthAt(float x) =>
            WorldFeatures.RavineDepth * SmoothStep(WorldFeatures.RavineWestX, WorldFeatures.RavineWestX + 16f, x)
            * (1f - SmoothStep(WorldFeatures.RavineEastX - 16f, WorldFeatures.RavineEastX, x));

        /// East-west ravine (~13 m wide, 7.5 m deep) with level rims and flat anchor pads at the rope bridge.
        static float Ravine(float x, float z, float h)
        {
            if (x < WorldFeatures.RavineWestX - 4f || x > WorldFeatures.RavineEastX + 4f) return h;
            float zc = WorldFeatures.RavineAxisZ(x);
            float q = z - zc;
            float aq = MathF.Abs(q);
            if (aq > 26f) return h;

            // flat anchor pads on both rims
            float dbx = x - BridgeX;
            float dN = MathF.Sqrt(dbx * dbx + (q - 9.5f) * (q - 9.5f)), dS = MathF.Sqrt(dbx * dbx + (q + 9.5f) * (q + 9.5f));
            float wPad = 1f - SmoothStep(4f, 9f, MathF.Min(dN, dS));
            h = Lerp(h, WorldFeatures.RavineRimY, wPad);

            float dep = RavineDepthAt(x);
            if (dep <= 0f) return h;
            float axisH = IslandBase(x, zc, MathF.Sqrt(x * x + zc * zc));
            float rim = Lerp(axisH, WorldFeatures.RavineRimY, 1f - SmoothStep(8f, 18f, MathF.Abs(dbx)));
            float floorY = rim - dep + 0.25f * WorldNoise.Perlin(x * 0.15f, 2.2f);
            float ht = WorldFeatures.RavineHalfWidthTop + 0.7f * WorldNoise.Perlin(x * 0.09f + 9f, q > 0f ? 1.5f : 6.5f);
            float prof = floorY + dep * SmoothStep(WorldFeatures.RavineHalfWidthFloor, ht, aq) + 0.8f * MathF.Max(0f, aq - ht);
            return Lerp(h, MathF.Min(h, prof), Clamp01(dep / 1.5f));   // round 2b: fade the cut with the depth (no seam at the ends)
        }

        /// 0..1 mask of the tide pool rock platform.
        static float TideMask(float x, float z)
        {
            float px = x - TideX, pz = z - TideZ;
            if (px * px + pz * pz > 40f * 40f) return 0f;
            float u = px * TideUX + pz * TideUZ, v = px * TideVX + pz * TideVZ;
            return (1f - SmoothStep(18f, 26f, MathF.Abs(u))) * SmoothStep(-13f, -8f, v) * (1f - SmoothStep(7f, 11f, v));
        }

        /// Tide pools: a knobbly rock platform just above sea level with pools the sea fills.
        static float TidePlatform(float x, float z, float h)
        {
            float px = x - TideX, pz = z - TideZ;
            if (px * px + pz * pz > 42f * 42f) return h;
            float u = px * TideUX + pz * TideUZ, v = px * TideVX + pz * TideVZ;
            float wU = 1f - SmoothStep(20f, 29f, MathF.Abs(u));
            if (wU <= 0f) return h;
            float plat = WorldFeatures.TidePlatformY + 0.3f * WorldNoise.Perlin(x * 0.35f + 2f, z * 0.35f - 7f)
                         + 1.1f * SmoothStep(0.3f, 0.7f, WorldNoise.Perlin(x * 0.16f - 4f, z * 0.16f + 9f));
            float wSea = SmoothStep(6f, 11f, v);
            float target = Lerp(plat, MathF.Min(h, -1.2f), wSea);
            float w = wU * SmoothStep(-15f, -8f, v);
            h = Lerp(h, target, w);
            for (int i = 0; i < TpX.Length; i++)
            {
                float d = Dist(x, z, TpX[i], TpZ[i]);
                float r = TpR[i];
                if (d > r + 1f) continue;
                h = MathF.Min(h, WorldFeatures.TidePoolFloor + 1.5f * SmoothStep(r * 0.35f, r + 0.7f, d));
            }
            return h;
        }

        // ------------------------------------------------------------------ landmark islands
        static float MiniIslands(float x, float z, float h)
        {
            for (int i = 0; i < MiX.Length; i++)
            {
                float dx = x - MiX[i], dz = z - MiZ[i];
                float d2 = dx * dx + dz * dz;
                if (d2 > MiReach[i] * MiReach[i]) continue;
                float d = MathF.Sqrt(d2);
                h = MathF.Max(h, MiniIslandHeight(i, x, z, dx, dz, d));
            }
            return h;
        }

        static float MiniShore(int i, float x, float z, float dx, float dz, float d)
        {
            float inv = d > 1e-3f ? 1f / d : 0f;
            float cx = dx * inv, cz = dz * inv;
            return MiR[i] + 3.2f * WorldNoise.Perlin(cx * 1.6f + i * 7.1f, cz * 1.6f - i * 3.3f) + 1.2f * WorldNoise.Perlin(x * 0.07f + i, z * 0.07f);
        }

        /// 0 on the beach side (facing the dive site) .. rocky on the far side.
        static float MiniRockiness(int i, float dx, float dz, float d)
        {
            float inv = d > 1e-3f ? 1f / d : 0f;
            float facing = (dx * MiBeachX[i] + dz * MiBeachZ[i]) * inv;
            return MiRock[i] * (1f - 0.85f * SmoothStep(0.1f, 0.7f, facing));
        }

        static float MiniIslandHeight(int i, float x, float z, float dx, float dz, float d)
        {
            float rs = MiniShore(i, x, z, dx, dz, d);
            float e = d - rs;
            if (e >= 0f)
            {
                // shallow turquoise shelf, then the steep flank down to the open sea floor
                float depth = e < 6f ? 0.32f * e : 1.92f + (e - 6f) * MiK[i];
                return -depth;
            }
            float L = -e;
            float rocky = MiniRockiness(i, dx, dz, d);
            float beach = MathF.Min(L * 0.13f, 1.4f);
            float t = Clamp01(1f - d / MathF.Max(rs, 1f));
            float dome = 0.72f * MiPeak[i] * MathF.Pow(1f - (1f - t) * (1f - t), Lerp(1.6f, 0.9f, rocky));
            // the summit (a craggy knoll on rocky islands) sits on the far side: the island rises from the beach
            // that faces the dive site toward a crest, which gives every landmark its own silhouette
            float kx = dx + MiBeachX[i] * rs * 0.32f, kz = dz + MiBeachZ[i] * rs * 0.32f;
            float kd = MathF.Sqrt(kx * kx + kz * kz) / MathF.Max(rs * Lerp(0.62f, 0.42f, MiRock[i]), 1f);
            float knoll = MiPeak[i] * MathF.Pow(Clamp01(1f - kd * kd), Lerp(1.3f, 0.8f, MiRock[i]));
            float hill = MathF.Max(dome, knoll) + rocky * 1.6f * WorldNoise.Perlin(x * 0.17f + i * 2.3f, z * 0.17f - i);
            float sL = SmoothStep(0f, Lerp(14f, 4f, rocky), L);
            return Lerp(beach, MathF.Max(beach, hill), sL) + 0.25f * WorldNoise.Perlin(x * 0.3f, z * 0.3f + i * 4f) * sL;
        }

        static GroundHint MiniIslandHint(float x, float z, float h)
        {
            if (h < 1.3f) return GroundHint.None;
            for (int i = 0; i < MiX.Length; i++)
            {
                float dx = x - MiX[i], dz = z - MiZ[i];
                float d2 = dx * dx + dz * dz;
                float lim = MiR[i] + 6f;
                if (d2 > lim * lim) continue;
                float d = MathF.Sqrt(d2);
                float rocky = MiniRockiness(i, dx, dz, d) * SmoothStep(1.3f, 3.5f, h);
                return Hash01(x, z, 7373 + i) < rocky ? GroundHint.Rock : GroundHint.None;
            }
            return GroundHint.None;
        }

        // ------------------------------------------------------------------ open sea
        static float SeaDepth(float x, float z, float r, float D, float cliff)
        {
            float shelf = 8f + 12f * Clamp01(0.5f + 0.9f * WorldNoise.Fbm(x * 0.004f + 1.3f, z * 0.004f - 7.7f, 3));
            // round 2 (GDD 17.4): a wide turquoise lagoon – 1.9 m within 9 m of the beach, 3.4 m at the lagoon edge
            // (70 m out on sandy coasts, 22 m under cliffs), then the old shelf profile
            float lw = Lerp(LagoonWidth, LagoonWidthCliff, cliff);
            float near;
            if (D < lw) near = 1.9f * SmoothStep(0f, 9f, D) + (LagoonEdgeDepth - 1.9f) * SmoothStep(9f, lw, D);
            else near = LagoonEdgeDepth + MathF.Max(0f, shelf - LagoonEdgeDepth) * (1f - MathF.Exp(-(D - lw) / Lerp(45f, 9f, cliff)));

            float oc = SmoothStep(OceanStart, 900f, r);
            oc = MathF.Pow(oc, 1.3f);
            float ocean = 62f + 48f * Clamp01(0.5f + 0.8f * WorldNoise.Fbm(x * 0.0025f + 5.5f, z * 0.0025f + 2.5f, 3));
            float depth = Lerp(near, ocean, oc);

            // submarine shelf arms leading out to the dive sites
            depth = Arm(x, z, depth, Kelp.x, Kelp.z, 18f, 190f);
            depth = Arm(x, z, depth, Wreck.x, Wreck.z, 31f, 180f);
            depth = Arm(x, z, depth, Blue.x, Blue.z, 33f, 210f);

            // the hidden cove stays shallow
            if (r < 200f)
            {
                float cove = 1f - SmoothStep(CoveR + 3f, CoveR + 22f, Dist(x, z, CoveX, CoveZ));
                if (cove > 0f) depth = Lerp(depth, MathF.Min(depth, 6.5f), cove);
            }

            float detail = 0.35f * WorldNoise.Perlin(x * 0.13f + 3f, z * 0.13f + 1f) + 0.12f * WorldNoise.Perlin(x * 0.45f, z * 0.45f);
            depth += detail * SmoothStep(0.5f, 3f, depth);
            // low sand dunes everywhere the light still reaches (art bible §2.4), never in the surf strip at the beach
            depth -= 0.24f * Ripple(x, z, 8.5f) * SmoothStep(1.6f, 3f, depth) * (1f - SmoothStep(28f, 42f, depth));
            return depth;
        }

        /// Sand ripples (detail pass): crests run across the trade-wind swell from the ENE, sharp crests and wide troughs,
        /// slowly meandering. About −0.4..+0.6 (× amplitude in metres); wavelength in metres.
        static float Ripple(float x, float z, float wavelength)
        {
            float u = (x * 0.83f + z * 0.56f) / wavelength + 0.45f * WorldNoise.Perlin(x * 0.035f + 7f, z * 0.035f - 2f);
            float s = 0.5f + 0.5f * MathF.Sin(u * 6.2831853f);
            return s * s - 0.375f;
        }

        /// 1 inside the house reef's 1.6 m detail zone (WorldFeatures.ReefDetailRect), fading out 8 m before its edges.
        static float ReefDetail(float x, float z) =>
            SmoothStep(RdX0, RdX0 + 8f, x) * (1f - SmoothStep(RdX1 - 8f, RdX1, x)) * SmoothStep(RdZ0, RdZ0 + 8f, z) * (1f - SmoothStep(RdZ1 - 8f, RdZ1, z));

        /// 0..1 in the grooves of the house reef's spur-and-groove fore-reef (outside the crest), and the fore-reef weight.
        static float Groove(float x, float z, float r, out float fore)
        {
            float cr = WorldFeatures.ReefCrestR;
            fore = SmoothStep(cr + 4f, cr + 14f, r) * (1f - SmoothStep(cr + 38f, cr + 58f, r));
            if (fore <= 0f) return 0f;
            float th = MathF.Atan2(z, x);
            float u = th * cr / WorldFeatures.SpurSpacing + 0.6f * WorldNoise.Perlin(r * 0.05f + 1f, th * 8f);
            float g = 0.5f + 0.5f * MathF.Cos(u * 6.2831853f);
            return SmoothStep(0.55f, 0.92f, g);
        }

        /// Reef crest weight (1 on the crest line) – the line wobbles a little.
        static float Crest(float x, float z, float r)
        {
            float c = (r - WorldFeatures.ReefCrestR - 4f * WorldNoise.Perlin(x * 0.02f + 3f, z * 0.02f)) / WorldFeatures.ReefCrestHalfWidth;
            return MathF.Exp(-c * c);
        }

        static float Arm(float x, float z, float depth, float sx, float sz, float endDepth, float halfWidth)
        {
            float len = MathF.Sqrt(sx * sx + sz * sz);
            float ax = sx / len * ArmStart, az = sz / len * ArmStart;
            float d = DistSeg(x, z, ax, az, sx, sz, out float t);
            float m = 1f - SmoothStep(halfWidth * 0.45f, halfWidth, d);
            if (m <= 0f) return depth;
            float armDepth = Lerp(15f, endDepth, SmoothStep(0f, 1f, t)) + 2.5f * WorldNoise.Perlin(x * 0.01f + 7.1f, z * 0.01f - 1.9f);
            return Lerp(depth, MathF.Min(depth, armDepth), m);
        }

        // ------------------------------------------------------------------ dive sites
        static float BommieMask(float x, float z)
            => SmoothStep(0.18f, 0.55f, WorldNoise.Perlin(x * 0.075f + 5.1f, z * 0.075f + 2.7f));

        static float KelpRidge(float x, float z) => WorldNoise.Ridge(x * 0.03f + 1.7f, z * 0.03f + 8.2f);

        /// House reef: sandy/rocky shelf, 3 m near the island to ~11 m on the outside, with coral bommies. L.3: a reef
        /// crest (3–4 m) along the lagoon edge, spur-and-groove channels on the fore-reef outside it, rippled sand.
        static float HouseReefDepth(float x, float z, float r)
        {
            float d = Lerp(3.5f, 10.6f, SmoothStep(ReefInnerR, ReefOuterR, r));
            d += 1.1f * WorldNoise.Fbm(x * 0.018f + 31f, z * 0.018f - 12f, 3);
            float crest = Crest(x, z, r);
            d = Lerp(d, 3.3f + 0.6f * WorldNoise.Perlin(x * 0.11f + 2f, z * 0.11f - 5f), crest * 0.85f);
            float groove = Groove(x, z, r, out float fore);
            d += WorldFeatures.GrooveDepth * fore * groove;
            float bommie = BommieMask(x, z);
            d -= bommie * (2.1f + 0.9f * WorldNoise.Perlin(x * 0.31f, z * 0.31f)) * (1f - 0.6f * crest);
            d += 0.12f * WorldNoise.Perlin(x * 0.5f + 9f, z * 0.5f);
            // rippled sand between the bommies: long dunes everywhere, short ripples in the 1.6 m detail zone
            float sand = (1f - bommie) * (1f - crest);
            d -= sand * (0.2f * Ripple(x, z, 8.5f) + 0.17f * ReefDetail(x, z) * Ripple(x + 13f, z - 7f, 4.3f));
            return Clamp(d, Reef.minD + 0.05f, Reef.maxD - 0.1f);
        }

        /// Fog Isle (secret site, round 2b): seamount lagoon, 5 m near the island to 26 m at the rim, a horseshoe reef
        /// crest (2.5 m) 58 m from the site centre with a boat channel on the south side, bommies inside.
        static float FogDepth(float x, float z, float ds)
        {
            float d = Lerp(5f, 26f, SmoothStep(18f, Fog.r, ds));
            d += 1.0f * WorldNoise.Fbm(x * 0.02f + 41f, z * 0.02f - 17f, 3);
            d -= BommieMask(x + 300f, z) * 1.8f * (1f - SmoothStep(40f, 60f, ds));
            float rz = z - Fog.z;
            float south = ds > 1e-3f ? -rz / ds : 0f;                      // 1 = due south of the site centre
            float channel = SmoothStep(0.93f, 0.985f, south);
            float crest = MathF.Exp(-((ds - 58f) * (ds - 58f)) / (7f * 7f)) * (1f - channel);
            d = Lerp(d, 2.5f + 0.6f * WorldNoise.Perlin(x * 0.2f, z * 0.2f), crest);
            // white sand ripples in the seamount lagoon (detail pass)
            d -= 0.24f * Ripple(x + 500f, z, 7.5f) * (1f - crest) * (1f - BommieMask(x + 300f, z));
            return Clamp(d, Fog.minD - 2f, Fog.maxD - 0.5f);
        }

        /// Kelp forest: rocky floor 10-25 m with ridges and boulders.
        static float KelpDepth(float x, float z)
        {
            float d = 18f + 4.5f * WorldNoise.Fbm(x * 0.011f + 7f, z * 0.011f - 3f, 3);
            d -= 4.2f * KelpRidge(x, z);
            d -= 1.8f * SmoothStep(0.25f, 0.6f, WorldNoise.Perlin(x * 0.12f + 3f, z * 0.12f - 9f));
            d += 0.5f * WorldNoise.Perlin(x * 0.3f + 1f, z * 0.3f + 2f);
            // rippled sand channels between the rock ridges (detail pass)
            d -= 0.22f * Ripple(x - 300f, z, 9f) * (1f - SmoothStep(0.3f, 0.6f, KelpRidge(x, z)));
            return Clamp(d, Kelp.minD + 0.5f, Kelp.maxD - 0.5f);
        }

        /// Wreck: flat sand at ~32 m in the middle (the wreck lies there), dunes and an outcrop around.
        static float WreckDepth(float x, float z, float dc)
        {
            float d = 32f + 0.15f * WorldNoise.Perlin(x * 0.4f, z * 0.4f);
            float outer = SmoothStep(28f, 70f, dc);
            float dunes = 5.5f * WorldNoise.Fbm(x * 0.016f + 2f, z * 0.016f + 5f, 3) + 1.2f * WorldNoise.Perlin(x * 0.06f, z * 0.06f + 4f);
            float outcrop = 7f * SmoothStep(0.2f, 0.55f, WorldNoise.Perlin(x * 0.02f + 11f, z * 0.02f - 6f));
            d += outer * (dunes - outcrop);
            // ripples on the dunes around the flat wreck bed (detail pass)
            d -= outer * 0.26f * Ripple(x, z + 200f, 8f) * (1f - SmoothStep(0.2f, 0.45f, WorldNoise.Perlin(x * 0.02f + 11f, z * 0.02f - 6f)));
            return Clamp(d, Wreck.minD + 0.5f, Wreck.maxD - 0.5f);
        }

        /// Blue drop-off: plateau ~30-35 m on the island side, then a steep wall down to the site's max depth.
        static float BlueDropDepth(float x, float z)
        {
            float rx = x - Blue.x, rz = z - Blue.z;
            float s = rx * BlueDirX + rz * BlueDirZ;            // + toward the island
            float along = -rx * BlueDirZ + rz * BlueDirX;
            float s0 = -12f + 16f * WorldNoise.Perlin(along * 0.012f + 4f, 1.3f) + 5f * WorldNoise.Perlin(along * 0.05f, 7.7f);

            float plateau = 32.6f + 1.7f * WorldNoise.Fbm(x * 0.02f + 9f, z * 0.02f + 1f, 3) + 0.25f * WorldNoise.Perlin(x * 0.3f, z * 0.3f);
            plateau = Clamp(plateau, Blue.minD + 0.4f, 34.8f);
            float deep = Blue.maxD - 1.6f + 1.1f * WorldNoise.Perlin(x * 0.02f + 2f, z * 0.02f + 5f);

            float t = SmoothStep(s0 + 5f, s0 - 9f, s);
            // a few ledges on the wall
            float ledge = 2.5f * WorldNoise.Perlin(along * 0.08f, s * 0.15f) * t * (1f - t) * 4f;
            float d = Lerp(plateau, deep, t) + ledge;
            return Clamp(d, Blue.minD + 0.4f, Blue.maxD - 0.3f);
        }

        /// Deep trench: canyon 80-160 m deep, steep walls, meandering axis.
        static float TrenchDepth(float x, float z)
        {
            float rx = x - Trench.x, rz = z - Trench.z;
            float along = rx * TrenchAxX + rz * TrenchAxZ;
            float q = -rx * TrenchAxZ + rz * TrenchAxX;
            q += 24f * WorldNoise.Perlin(along * 0.008f + 3f, 0.5f);
            float u = Clamp01((MathF.Abs(q) - 18f) / 125f);
            float wall = SmoothStep(0f, 1f, u);
            float bottom = Trench.maxD - 6f + 4f * WorldNoise.Perlin(x * 0.03f, z * 0.03f + 1f);
            float rim = Trench.minD + 6f + 5f * WorldNoise.Fbm(x * 0.01f + 1f, z * 0.01f + 1f, 3);
            float d = Lerp(bottom, rim, wall);
            d += 3f * WorldNoise.Perlin(x * 0.07f + 5f, z * 0.07f) * wall * (1f - wall) * 4f;   // terraces on the walls
            return Clamp(d, Trench.minD + 1f, Trench.maxD - 1f);
        }

        // ------------------------------------------------------------------ dock beach
        /// East beach in front of the dock: ~2 m at x=150 down to sea level at x=170 and ~2.75 m deep at the dock end.
        static float DockBeach(float x, float z, float h)
        {
            float zw = SmoothStep(DockZ - 48f, DockZ - 28f, z) * (1f - SmoothStep(DockZ + 34f, DockZ + 54f, z));
            if (zw <= 0f) return h;
            // round 2: every x is relative to DockStartX (was hard-coded for DockStart.x = 168)
            float x0 = DockStartX;
            float xw = SmoothStep(x0 - 44f, x0 - 30f, x) * (1f - SmoothStep(DockEndX + 19f, DockEndX + 42f, x));
            float w = zw * xw;
            if (w <= 0f) return h;

            // the beach curves gently: the dock sits in the middle of a shallow bight
            float dz = z - DockZ;
            float xb = x - 0.006f * dz * dz * SmoothStep(x0 - 38f, x0 - 8f, x);
            float p;
            if (xb < x0 - 18f) p = Lerp(2.0f, HutHeight, SmoothStep(x0 - 18f, x0 - 40f, xb));
            else if (xb < x0 + 2f) p = 2.0f * (1f - (xb - (x0 - 18f)) / 20f);
            else if (xb < DockEndX) p = -2.75f * (xb - (x0 + 2f)) / (DockEndX - (x0 + 2f));
            else p = -2.75f - 1.0f * Clamp01((xb - DockEndX) / 40f);
            if (xb > x0) p += 0.06f * WorldNoise.Perlin(x * 0.5f, z * 0.5f);
            // rippled sand in the clear water off the beach (detail pass)
            if (xb > x0 + 4f) p -= 0.14f * Ripple(x, z, 7.5f) * SmoothStep(x0 + 4f, x0 + 16f, xb);
            return Lerp(h, p, w);
        }
    }
}
