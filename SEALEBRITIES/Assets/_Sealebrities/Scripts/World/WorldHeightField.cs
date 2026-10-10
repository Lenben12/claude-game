using System;

namespace Sealebrities
{
    /// Analytic height function of the whole map in world meters (y relative to sea level 0).
    /// Thread-safe and allocation-free: only reads cached floats and WorldNoise, so the terrain builder
    /// evaluates it on worker threads. Layout comes from WorldLayout and DiveSites.
    public static class WorldHeightField
    {
        public const float HutHeight = 2.5f;
        public const float HelipadHeight = 4f;
        public const float LighthouseHeight = 42f;
        public const float MinHeight = WorldLayout.TerrainMinY + 2f;
        public const float MaxHeight = WorldLayout.TerrainMinY + WorldLayout.TerrainHeightRange - 2f;

        struct Site { public float x, z, r, minD, maxD; }

        // Layout cache (plain floats so worker threads never touch Unity structs after init).
        static readonly float HutX, HutZ, HeliX, HeliZ, DockStartX, DockEndX, DockZ, LhX, LhZ, BayX, BayZ;
        static readonly float CoveX, CoveZ, CoveR, MouthX, MouthZ;
        static readonly Site Reef, Kelp, Wreck, Blue, Trench;
        static readonly float BlueDirX, BlueDirZ, TrenchAxX, TrenchAxZ;

        static WorldHeightField()
        {
            HutX = WorldLayout.Hut.x; HutZ = WorldLayout.Hut.z;
            HeliX = WorldLayout.Helipad.x; HeliZ = WorldLayout.Helipad.z;
            DockStartX = WorldLayout.DockStart.x; DockEndX = WorldLayout.DockEnd.x; DockZ = WorldLayout.DockEnd.z;
            LhX = WorldLayout.Lighthouse.x; LhZ = WorldLayout.Lighthouse.z;
            BayX = WorldLayout.HiddenBay.x; BayZ = WorldLayout.HiddenBay.z;

            CoveX = BayX + 7f; CoveZ = BayZ; CoveR = 27f;
            MouthX = BayX - 60f; MouthZ = BayZ - 18f;

            Reef = SiteOf("house_reef", 260f, 60f, 120f, 3f, 12f);
            Kelp = SiteOf("kelp_forest", -340f, 300f, 130f, 10f, 25f);
            Wreck = SiteOf("wreck", 460f, -380f, 110f, 22f, 40f);
            Blue = SiteOf("blue_drop", -620f, -540f, 170f, 30f, 75f);
            Trench = SiteOf("deep_trench", 760f, 660f, 190f, 80f, 160f);

            float bl = (float)Math.Sqrt(Blue.x * Blue.x + Blue.z * Blue.z);
            BlueDirX = -Blue.x / bl; BlueDirZ = -Blue.z / bl;           // points from the drop-off toward the island
            TrenchAxX = 0.783f; TrenchAxZ = -0.622f;                     // canyon runs NW -> SE
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

        // ------------------------------------------------------------------ public API
        /// Terrain height in world meters at (x, z).
        public static float Height(float x, float z)
        {
            float r = MathF.Sqrt(x * x + z * z);
            float h;

            if (r < 620f)
            {
                Coast(x, z, r, out float L, out float cliff);
                if (L > 0f) h = LandHeight(x, z, L, cliff);
                else h = -SeaDepth(x, z, r, -L, cliff);
            }
            else h = -SeaDepth(x, z, r, r - 190f, 0f);

            // deep, smooth terrain border
            float edge = SmoothStep(860f, 1012f, MathF.Max(MathF.Abs(x), MathF.Abs(z)));
            if (edge > 0f) h = Lerp(h, -128f, edge);

            // flat building plateaus
            if (r < 260f)
            {
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

            return Clamp(h, MinHeight, MaxHeight);
        }

        /// 0..1 hint for rocky ground that is not explained by slope alone (reef bommies, kelp rock floor).
        public static float RockHint(float x, float z)
        {
            float rock = 0f;
            float dReef = Dist(x, z, Reef.x, Reef.z);
            if (dReef < Reef.r + 40f)
                rock = MathF.Max(rock, BommieMask(x, z) * 0.9f * (1f - SmoothStep(Reef.r, Reef.r + 40f, dReef)));
            float dKelp = Dist(x, z, Kelp.x, Kelp.z);
            if (dKelp < Kelp.r + 60f)
            {
                float k = 0.3f + 0.7f * SmoothStep(0.35f, 0.8f, KelpRidge(x, z));
                rock = MathF.Max(rock, k * (1f - SmoothStep(Kelp.r, Kelp.r + 60f, dKelp)));
            }
            float dBlue = Dist(x, z, Blue.x, Blue.z);
            if (dBlue < Blue.r + 30f)
                rock = MathF.Max(rock, 0.35f * SmoothStep(0.1f, 0.5f, WorldNoise.Perlin(x * 0.05f + 3f, z * 0.05f - 2f)));
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

        // ------------------------------------------------------------------ island
        /// Signed distance to the shoreline (L > 0 on land), plus a 0..1 cliff factor.
        static void Coast(float x, float z, float r, out float L, out float cliff)
        {
            float east = Gauss(x, z, 178f, 10f, 85f);
            float bay = Gauss(x, z, BayX - 10f, BayZ - 5f, 60f);
            float lhN = Gauss(x, z, LhX, LhZ + 40f, 50f);
            float calm = 1f - 0.85f * MathF.Max(east, MathF.Max(bay, lhN));

            float th = MathF.Atan2(z, x);
            float lobes = 17f * MathF.Sin(2f * th + 0.9f) + 11f * MathF.Sin(3f * th + 2.4f) + 6f * MathF.Sin(5f * th + 1f);
            float R = WorldLayout.IslandRadius
                      + calm * (lobes + 36f * WorldNoise.Fbm(x * 0.0085f + 11.3f, z * 0.0085f - 4.1f, 3)
                                + 9f * WorldNoise.Perlin(x * 0.03f + 2.2f, z * 0.03f + 6.6f))
                      - 14f * east + 34f * bay - 7f * lhN;
            float L0 = R - r;

            // hidden cove: roundish basin with an angled, narrow mouth
            float basin = Dist(x, z, CoveX, CoveZ) - CoveR - 4f * WorldNoise.Perlin(x * 0.06f + 3.3f, z * 0.06f + 1.1f);
            float channel = DistSeg(x, z, CoveX, CoveZ, MouthX, MouthZ, out _) - 10f;
            L = MathF.Min(L0, MathF.Min(basin, channel));

            float cliffL = Gauss(x, z, LhX, LhZ + 30f, 48f);
            float cliffB = Gauss(x, z, CoveX - 5f, CoveZ, 58f);
            float cliffN = 0.75f * SmoothStep(0.15f, 0.45f, WorldNoise.Perlin(x * 0.011f + 40.1f, z * 0.011f - 12.7f)) * (1f - east);
            cliff = Clamp01(MathF.Max(cliffL, MathF.Max(cliffB, cliffN)));
            cliff *= 1f - CoveBeach(x, z);
        }

        /// 1 on the small sandy beach at the inner (east) end of the hidden cove.
        static float CoveBeach(float x, float z) => 1f - SmoothStep(16f, 34f, Dist(x, z, CoveX + CoveR + 6f, CoveZ));

        static float Lowland(float x, float z)
        {
            float a = 1f - SmoothStep(30f, 90f, Dist(x, z, HutX, HutZ));
            float b = 1f - SmoothStep(24f, 75f, Dist(x, z, HeliX, HeliZ));
            float c = 1f - SmoothStep(14f, 55f, DistSeg(x, z, HutX, HutZ, HeliX, HeliZ, out _));
            float d = 1f - SmoothStep(30f, 80f, Dist(x, z, DockStartX - 10f, DockZ));
            return MathF.Max(MathF.Max(a, b), MathF.Max(c, d));
        }

        static float Interior(float x, float z)
        {
            float r = MathF.Sqrt(x * x + z * z);
            // jungle hills (west/center), flattening into a coastal plain toward the east where hut, dock and helipad are
            float hills = 6f + 26f * Clamp01(0.45f + 1.1f * WorldNoise.Fbm(x * 0.0085f + 3.7f, z * 0.0085f + 9.1f, 4));
            hills += 7f * (1f - SmoothStep(0f, 170f, r));
            hills += 2.2f * WorldNoise.Perlin(x * 0.035f + 1.1f, z * 0.035f - 3.3f);
            hills = MathF.Min(hills, 30f + 2f * WorldNoise.Perlin(x * 0.05f, z * 0.05f));
            hills *= Lerp(1f, 0.22f, SmoothStep(-10f, 100f, x));
            // ridges flanking the hidden cove keep it hidden
            hills += 11f * Gauss(x, z, CoveX - 12f, CoveZ + 42f, 28f) + 10f * Gauss(x, z, CoveX - 18f, CoveZ - 46f, 26f);
            // lighthouse hill with a flat top
            float dl = Dist(x, z, LhX, LhZ);
            float south = Clamp01((LhZ - z) / MathF.Max(dl, 1f));   // inland (south) flank is long and gentle
            float lh = LighthouseHeight * MathF.Pow(1f - SmoothStep(9f, 80f + 55f * south, dl), 1.25f);
            hills = MathF.Max(hills, lh);

            float flat = 2.7f + 1.0f * WorldNoise.Perlin(x * 0.02f + 8f, z * 0.02f + 8f);
            hills = Lerp(hills, flat - 0.4f, CoveBeach(x, z));
            float low = Lowland(x, z);
            if (low <= 0f) return hills;
            return Lerp(hills, flat, low);
        }

        static float LandHeight(float x, float z, float L, float cliff)
        {
            float H = Interior(x, z);
            float beach = MathF.Min(L * 0.085f, 2.3f);
            float s = SmoothStep(0f, Lerp(60f, 5f, cliff), L);
            float h = Lerp(beach, H, s);
            h += 0.5f * WorldNoise.Perlin(x * 0.09f + 0.3f, z * 0.09f + 0.7f) * s;
            return h;
        }

        // ------------------------------------------------------------------ open sea
        static float SeaDepth(float x, float z, float r, float D, float cliff)
        {
            float shelf = 8f + 12f * Clamp01(0.5f + 0.9f * WorldNoise.Fbm(x * 0.004f + 1.3f, z * 0.004f - 7.7f, 3));
            float near = shelf * (1f - MathF.Exp(-D / Lerp(65f, 9f, cliff)));

            float oc = SmoothStep(250f, 900f, r);
            oc = MathF.Pow(oc, 1.3f);
            float ocean = 62f + 48f * Clamp01(0.5f + 0.8f * WorldNoise.Fbm(x * 0.0025f + 5.5f, z * 0.0025f + 2.5f, 3));
            float depth = Lerp(near, ocean, oc);

            // submarine shelf arms leading out to the dive sites
            depth = Arm(x, z, depth, Kelp.x, Kelp.z, 18f, 190f);
            depth = Arm(x, z, depth, Wreck.x, Wreck.z, 31f, 180f);
            depth = Arm(x, z, depth, Blue.x, Blue.z, 33f, 210f);

            // the hidden cove stays shallow
            if (r < 320f)
            {
                float cove = 1f - SmoothStep(CoveR + 4f, CoveR + 34f, Dist(x, z, CoveX, CoveZ));
                if (cove > 0f) depth = Lerp(depth, MathF.Min(depth, 6.5f), cove);
            }

            float detail = 0.35f * WorldNoise.Perlin(x * 0.13f + 3f, z * 0.13f + 1f) + 0.12f * WorldNoise.Perlin(x * 0.45f, z * 0.45f);
            depth += detail * SmoothStep(0.5f, 3f, depth);
            return depth;
        }

        static float Arm(float x, float z, float depth, float sx, float sz, float endDepth, float halfWidth)
        {
            float len = MathF.Sqrt(sx * sx + sz * sz);
            float ax = sx / len * 235f, az = sz / len * 235f;
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

        /// House reef: sandy/rocky shelf, 3 m near the island to ~11 m on the outside, with coral bommies.
        static float HouseReefDepth(float x, float z, float r)
        {
            float d = Lerp(3.5f, 10.6f, SmoothStep(185f, 372f, r));
            d += 1.1f * WorldNoise.Fbm(x * 0.018f + 31f, z * 0.018f - 12f, 3);
            d -= BommieMask(x, z) * (2.1f + 0.9f * WorldNoise.Perlin(x * 0.31f, z * 0.31f));
            d += 0.12f * WorldNoise.Perlin(x * 0.5f + 9f, z * 0.5f);
            return Clamp(d, Reef.minD + 0.05f, Reef.maxD - 0.1f);
        }

        /// Kelp forest: rocky floor 10-25 m with ridges and boulders.
        static float KelpDepth(float x, float z)
        {
            float d = 18f + 4.5f * WorldNoise.Fbm(x * 0.011f + 7f, z * 0.011f - 3f, 3);
            d -= 4.2f * KelpRidge(x, z);
            d -= 1.8f * SmoothStep(0.25f, 0.6f, WorldNoise.Perlin(x * 0.12f + 3f, z * 0.12f - 9f));
            d += 0.5f * WorldNoise.Perlin(x * 0.3f + 1f, z * 0.3f + 2f);
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
            float xw = SmoothStep(124f, 138f, x) * (1f - SmoothStep(DockEndX + 19f, DockEndX + 42f, x));
            float w = zw * xw;
            if (w <= 0f) return h;

            // the beach curves gently: the dock sits in the middle of a shallow bight
            float dz = z - DockZ;
            float xb = x - 0.006f * dz * dz * SmoothStep(130f, 160f, x);
            float p;
            if (xb < 150f) p = Lerp(2.0f, HutHeight, SmoothStep(150f, 128f, xb));
            else if (xb < 170f) p = 2.0f * (1f - (xb - 150f) / 20f);
            else if (xb < DockEndX) p = -2.75f * (xb - 170f) / (DockEndX - 170f);
            else p = -2.75f - 1.0f * Clamp01((xb - DockEndX) / 40f);
            if (xb > 168f) p += 0.06f * WorldNoise.Perlin(x * 0.5f, z * 0.5f);
            return Lerp(h, p, w);
        }
    }
}
