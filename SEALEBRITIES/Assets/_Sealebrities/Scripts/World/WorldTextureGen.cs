using System;

namespace Sealebrities
{
    /// Pixel data of one procedural texture (RGBA32, row 0 = bottom). Normal maps are stored as linear RGB = n * 0.5 + 0.5, A = 1.
    public sealed class WorldPixels
    {
        public int size;
        public byte[] albedo;
        public byte[] normal;
    }

    public enum WorldSurface { BeachSand, WetSand, Grass, Soil, Rock, SeabedSand, Silt, JungleGrass }

    /// Generates the tileable terrain textures and the sky/ocean helper textures as raw pixel arrays.
    /// Pure C# without Unity API calls so it can run on worker threads; WorldTextures turns the data into Texture2D.
    public static class WorldTextureGen
    {
        const float TwoPi = 6.2831853f;

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static float Lerp(float a, float b, float t) => a + (b - a) * t;
        static float SmoothStep(float e0, float e1, float x)
        {
            float t = Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }
        static byte B(float v) => (byte)(Clamp01(v) * 255f + 0.5f);

        struct C3
        {
            public float r, g, b;
            public C3(float r, float g, float b) { this.r = r; this.g = g; this.b = b; }
            public static C3 Lerp(C3 a, C3 b, float t) => new C3(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t);
            public static C3 operator *(C3 a, float s) => new C3(a.r * s, a.g * s, a.b * s);
        }

        // ------------------------------------------------------------------ terrain surfaces
        public static WorldPixels Terrain(WorldSurface kind, int size)
        {
            var px = new WorldPixels { size = size, albedo = new byte[size * size * 4], normal = new byte[size * size * 4] };
            var height = new float[size * size];
            float bump;
            switch (kind)
            {
                case WorldSurface.BeachSand: bump = 2.2f; break;
                case WorldSurface.WetSand: bump = 1.6f; break;
                case WorldSurface.Grass: bump = 3.5f; break;
                case WorldSurface.JungleGrass: bump = 4f; break;
                case WorldSurface.Soil: bump = 3.5f; break;
                case WorldSurface.Rock: bump = 6f; break;
                case WorldSurface.SeabedSand: bump = 3f; break;
                default: bump = 2.5f; break;
            }

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    C3 c; float h, smooth;
                    switch (kind)
                    {
                        case WorldSurface.BeachSand: BeachSand(u, v, x, y, false, out c, out h, out smooth); break;
                        case WorldSurface.WetSand: BeachSand(u, v, x, y, true, out c, out h, out smooth); break;
                        case WorldSurface.Grass: Grass(u, v, x, y, false, out c, out h, out smooth); break;
                        case WorldSurface.JungleGrass: Grass(u, v, x, y, true, out c, out h, out smooth); break;
                        case WorldSurface.Soil: Soil(u, v, x, y, out c, out h, out smooth); break;
                        case WorldSurface.Rock: Rock(u, v, x, y, out c, out h, out smooth); break;
                        case WorldSurface.SeabedSand: SeabedSand(u, v, x, y, out c, out h, out smooth); break;
                        default: Silt(u, v, x, y, out c, out h, out smooth); break;
                    }
                    int i = (y * size + x) * 4;
                    px.albedo[i] = B(c.r); px.albedo[i + 1] = B(c.g); px.albedo[i + 2] = B(c.b); px.albedo[i + 3] = B(smooth);
                    height[y * size + x] = h;
                }

            HeightToNormal(height, size, bump, px.normal);
            return px;
        }

        static void BeachSand(float u, float v, int x, int y, bool wet, out C3 c, out float h, out float smooth)
        {
            float m = WorldNoise.FbmTile(u, v, 4, 4, 11);
            float f = WorldNoise.FbmTile(u, v, 32, 3, 23);
            float grain = WorldNoise.Hash01(x, y, 5) - 0.5f;
            float rip = MathF.Sin(TwoPi * (3f * u + 9f * v) + 2.6f * WorldNoise.FbmTile(u, v, 3, 3, 41));
            var light = wet ? new C3(0.60f, 0.52f, 0.40f) : new C3(0.90f, 0.82f, 0.64f);
            var dark = wet ? new C3(0.49f, 0.42f, 0.32f) : new C3(0.79f, 0.69f, 0.51f);
            c = C3.Lerp(light, dark, Clamp01(0.5f + 0.9f * m));
            float ripAmt = wet ? 0.015f : 0.035f;
            c = c * (1f + 0.10f * grain + 0.07f * f + ripAmt * rip);

            // shell fragments and dark pebbles
            float shell = WorldNoise.CellTile(u, v, 40, 77);
            if (shell < 0.09f) c = C3.Lerp(c, new C3(0.97f, 0.94f, 0.88f), (1f - shell / 0.09f) * 0.85f);
            float pebble = WorldNoise.CellTile(u, v, 26, 91);
            if (pebble < 0.06f) c = C3.Lerp(c, wet ? new C3(0.22f, 0.2f, 0.18f) : new C3(0.42f, 0.38f, 0.33f), (1f - pebble / 0.06f) * 0.7f);

            h = 0.5f + (wet ? 0.08f : 0.18f) * rip + 0.22f * f + 0.06f * grain;
            smooth = wet ? 0.55f + 0.1f * m : 0.08f + 0.04f * f;
        }

        /// jungle = darker, bluer undergrowth used at a larger tile size to break up repetition.
        static void Grass(float u, float v, int x, int y, bool jungle, out C3 c, out float h, out float smooth)
        {
            int sd = jungle ? 200 : 0;
            float g1 = WorldNoise.FbmTile(u, v, jungle ? 4 : 5, 4, 3 + sd);
            float g2 = WorldNoise.FbmTile(u, v, 20, 3, 7 + sd);
            float blades = WorldNoise.TileAniso(u, v, 192, 48, 13 + sd) * 0.6f + WorldNoise.TileAniso(u, v, 64, 256, 17 + sd) * 0.4f;
            float clump = 1f - WorldNoise.CellTile(u, v, jungle ? 28 : 48, 19 + sd);
            float grain = WorldNoise.Hash01(x, y, 9 + sd) - 0.5f;

            var dark = jungle ? new C3(0.07f, 0.17f, 0.07f) : new C3(0.12f, 0.23f, 0.07f);
            var light = jungle ? new C3(0.20f, 0.36f, 0.14f) : new C3(0.32f, 0.45f, 0.13f);
            c = C3.Lerp(dark, light, Clamp01(0.5f + 0.8f * g1 + 0.35f * blades + (jungle ? 0.3f : 0.15f) * clump));
            float dry = SmoothStep(0.2f, 0.5f, g2) * (jungle ? 0.4f : 1f);
            c = C3.Lerp(c, new C3(0.45f, 0.44f, 0.2f), dry * 0.45f);
            float dirt = SmoothStep(-0.25f, -0.5f, g1 + 0.35f * g2);
            c = C3.Lerp(c, new C3(0.30f, 0.22f, 0.14f), dirt * 0.75f);
            c = c * (1f + 0.08f * grain);

            h = 0.5f + 0.3f * blades + 0.2f * clump - 0.15f * dirt;
            smooth = 0.12f + 0.06f * clump;
        }

        static void Soil(float u, float v, int x, int y, out C3 c, out float h, out float smooth)
        {
            float m = WorldNoise.FbmTile(u, v, 6, 4, 29);
            float grain = WorldNoise.Hash01(x, y, 31) - 0.5f;
            c = C3.Lerp(new C3(0.30f, 0.22f, 0.14f), new C3(0.20f, 0.15f, 0.10f), Clamp01(0.5f + m));

            float l1 = WorldNoise.PerlinPeriodic(u * 40f, v * 40f, 40, 40, 51);
            float l2 = WorldNoise.PerlinPeriodic(u * 56f, v * 56f, 56, 56, 53);
            float l3 = WorldNoise.PerlinPeriodic(u * 30f, v * 30f, 30, 30, 57);
            float leafA = SmoothStep(0.3f, 0.42f, l1);
            float leafB = SmoothStep(0.34f, 0.46f, l2);
            float leafC = SmoothStep(0.38f, 0.5f, l3);
            c = C3.Lerp(c, new C3(0.40f, 0.27f, 0.13f), leafA * 0.5f);
            c = C3.Lerp(c, new C3(0.30f, 0.29f, 0.14f), leafB * 0.45f);
            c = C3.Lerp(c, new C3(0.46f, 0.36f, 0.21f), leafC * 0.4f);
            float twig = SmoothStep(0.9f, 0.97f, 1f - MathF.Abs(WorldNoise.TileAniso(u, v, 24, 96, 61)));
            c = C3.Lerp(c, new C3(0.16f, 0.11f, 0.07f), twig * 0.7f);
            c = c * (1f + 0.09f * grain);

            h = 0.45f + 0.25f * (leafA + leafB + leafC) / 3f + 0.2f * m + 0.15f * twig;
            smooth = 0.1f + 0.08f * leafB;
        }

        static void Rock(float u, float v, int x, int y, out C3 c, out float h, out float smooth)
        {
            float m = WorldNoise.FbmTile(u, v, 3, 5, 71);
            float detail = WorldNoise.FbmTile(u, v, 8, 5, 73);
            float grain = WorldNoise.Hash01(x, y, 75) - 0.5f;
            c = C3.Lerp(new C3(0.50f, 0.48f, 0.45f), new C3(0.36f, 0.31f, 0.27f), Clamp01(0.5f + 1.1f * m));
            float strata = MathF.Sin(TwoPi * 5f * v + 3f * WorldNoise.FbmTile(u, v, 2, 3, 79));
            c = c * (1f + 0.06f * strata + 0.12f * detail + 0.08f * grain);

            // fractures along warped Voronoi borders, partly masked so they break up
            float wu = u + 0.025f * WorldNoise.FbmTile(u, v, 6, 3, 93), wv = v + 0.025f * WorldNoise.FbmTile(u, v, 6, 3, 95);
            float maskA = SmoothStep(-0.15f, 0.2f, WorldNoise.FbmTile(u, v, 4, 2, 85));
            float maskB = SmoothStep(-0.05f, 0.3f, WorldNoise.FbmTile(u, v, 6, 2, 89));
            float crackA = SmoothStep(0.05f, 0.0f, WorldNoise.CellEdgeTile(wu, wv, 5, 81)) * maskA;
            float crackB = SmoothStep(0.06f, 0.0f, WorldNoise.CellEdgeTile(wu, wv, 13, 83)) * maskB;
            float crack = MathF.Max(crackA, crackB * 0.6f);
            c = c * (1f - 0.35f * crack);
            // granular speckle and blotches
            float blot = WorldNoise.FbmTile(u, v, 16, 3, 91);
            float fine = WorldNoise.FbmTile(u, v, 64, 2, 97);
            c = c * (1f + 0.08f * blot + 0.07f * fine);
            detail += 0.25f * fine;

            float lichen = SmoothStep(0.3f, 0.55f, WorldNoise.FbmTile(u, v, 12, 3, 87));
            c = C3.Lerp(c, new C3(0.56f, 0.57f, 0.46f), lichen * 0.3f);

            h = 0.5f + 0.4f * detail + 0.15f * m - 0.3f * crack + 0.05f * strata;
            smooth = 0.2f + 0.1f * (1f - lichen);
        }

        static void SeabedSand(float u, float v, int x, int y, out C3 c, out float h, out float smooth)
        {
            float m = WorldNoise.FbmTile(u, v, 4, 4, 101);
            float f = WorldNoise.FbmTile(u, v, 24, 3, 103);
            float grain = WorldNoise.Hash01(x, y, 105) - 0.5f;
            float ph = TwoPi * (2f * u + 11f * v) + 3f * WorldNoise.FbmTile(u, v, 3, 3, 107);
            float rip = MathF.Sin(ph);
            rip = rip > 0f ? MathF.Pow(rip, 0.7f) : -MathF.Pow(-rip, 1.6f);     // sharp crests, wide troughs

            c = C3.Lerp(new C3(0.70f, 0.68f, 0.58f), new C3(0.58f, 0.58f, 0.52f), Clamp01(0.5f + 0.9f * m));
            c = c * (0.9f + 0.1f * (rip * 0.5f + 0.5f) + 0.06f * f + 0.08f * grain);

            float shell = WorldNoise.CellTile(u, v, 50, 109);
            if (shell < 0.07f) c = C3.Lerp(c, new C3(0.93f, 0.92f, 0.88f), (1f - shell / 0.07f) * 0.8f);
            float algae = SmoothStep(0.35f, 0.6f, WorldNoise.FbmTile(u, v, 10, 3, 111));
            c = C3.Lerp(c, new C3(0.42f, 0.44f, 0.32f), algae * 0.3f);

            h = 0.5f + 0.32f * rip + 0.12f * f + 0.04f * grain;
            smooth = 0.16f + 0.06f * algae;
        }

        static void Silt(float u, float v, int x, int y, out C3 c, out float h, out float smooth)
        {
            float m = WorldNoise.FbmTile(u, v, 5, 5, 121);
            float f = WorldNoise.FbmTile(u, v, 28, 3, 123);
            float grain = WorldNoise.Hash01(x, y, 125) - 0.5f;
            c = C3.Lerp(new C3(0.27f, 0.28f, 0.25f), new C3(0.18f, 0.18f, 0.17f), Clamp01(0.5f + m));
            c = c * (1f + 0.07f * f + 0.06f * grain);

            float hole = WorldNoise.CellTile(u, v, 22, 127);
            float burrow = SmoothStep(0.09f, 0.03f, hole);
            float rim = SmoothStep(0.06f, 0.1f, hole) * SmoothStep(0.16f, 0.1f, hole);
            c = c * (1f - 0.65f * burrow + 0.12f * rim);
            float snow = WorldNoise.CellTile(u, v, 70, 129);
            if (snow < 0.05f) c = C3.Lerp(c, new C3(0.75f, 0.76f, 0.72f), (1f - snow / 0.05f) * 0.6f);

            h = 0.5f + 0.2f * m + 0.1f * f - 0.4f * burrow + 0.1f * rim;
            smooth = 0.3f;
        }

        static void HeightToNormal(float[] h, int size, float strength, byte[] outRgba)
        {
            for (int y = 0; y < size; y++)
            {
                int yu = (y + 1) % size, yd = (y - 1 + size) % size;
                for (int x = 0; x < size; x++)
                {
                    int xr = (x + 1) % size, xl = (x - 1 + size) % size;
                    float dx = (h[y * size + xr] - h[y * size + xl]) * 0.5f * strength;
                    float dy = (h[yu * size + x] - h[yd * size + x]) * 0.5f * strength;
                    float nx = -dx, ny = -dy, nz = 1f;
                    float inv = 1f / MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                    int i = (y * size + x) * 4;
                    outRgba[i] = B(nx * inv * 0.5f + 0.5f);
                    outRgba[i + 1] = B(ny * inv * 0.5f + 0.5f);
                    outRgba[i + 2] = B(nz * inv * 0.5f + 0.5f);
                    outRgba[i + 3] = 255;
                }
            }
        }

        // ------------------------------------------------------------------ ocean / sky helpers
        /// Tileable small-ripple height -> normal map for the ocean surface.
        public static byte[] OceanRipples(int size)
        {
            var h = new float[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float s = 0.35f * MathF.Sin(TwoPi * (3f * u + 1f * v) + 0.0f)
                            + 0.25f * MathF.Sin(TwoPi * (-2f * u + 4f * v) + 1.3f)
                            + 0.18f * MathF.Sin(TwoPi * (5f * u - 3f * v) + 2.1f)
                            + 0.12f * MathF.Sin(TwoPi * (1f * u + 7f * v) + 4.2f)
                            + 0.45f * WorldNoise.FbmTile(u, v, 6, 4, 141);
                    h[y * size + x] = s;
                }
            var n = new byte[size * size * 4];
            HeightToNormal(h, size, size / 64f, n);
            return n;
        }

        /// Soft round dot (white, alpha falloff) for stars.
        public static byte[] StarDot(int size)
        {
            var p = new byte[size * size * 4];
            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float r2 = dx * dx + dy * dy;
                    float a = MathF.Exp(-r2 * 9f) + 0.25f * MathF.Exp(-r2 * 2.5f);
                    if (r2 >= 1f) a = 0f;
                    int i = (y * size + x) * 4;
                    p[i] = 255; p[i + 1] = 255; p[i + 2] = 255; p[i + 3] = B(a);
                }
            return p;
        }

        /// Moon disc with maria and craters plus a faint halo.
        public static byte[] Moon(int size)
        {
            var p = new byte[size * size * 4];
            float half = size * 0.5f;
            const float R = 0.62f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half, dy = (y + 0.5f - half) / half;
                    float r = MathF.Sqrt(dx * dx + dy * dy);
                    float u = x / (float)size, v = y / (float)size;
                    float maria = SmoothStep(-0.05f, 0.2f, WorldNoise.FbmTile(u, v, 3, 4, 151));
                    float crater = WorldNoise.CellTile(u, v, 9, 153);
                    float ring = SmoothStep(0.12f, 0.17f, crater) * SmoothStep(0.24f, 0.18f, crater);
                    float bright = 0.95f - 0.25f * maria - 0.12f * SmoothStep(0.15f, 0.05f, crater) + 0.08f * ring;
                    float limb = 1f - 0.25f * MathF.Pow(Clamp01(r / R), 4f);
                    float disc = SmoothStep(R + 0.015f, R - 0.015f, r);
                    float halo = MathF.Exp(-MathF.Max(0f, r - R) * 9f) * 0.35f * (1f - disc);
                    float cr = bright * limb, cg = bright * limb * 0.98f, cb = bright * limb * 0.93f;
                    int i = (y * size + x) * 4;
                    p[i] = B(Lerp(0.8f, cr, disc)); p[i + 1] = B(Lerp(0.86f, cg, disc)); p[i + 2] = B(Lerp(1f, cb, disc));
                    p[i + 3] = B(disc + halo);
                }
            return p;
        }

        /// Cloud layer seen from below. The texture is mapped onto a camera-centered dome: the center is the zenith,
        /// the rim the horizon, so clouds fade out toward the rim. RGB = brightness (lit tops vs darker bases).
        public static byte[] Clouds(int size)
        {
            var p = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size, v = y / (float)size;
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float rho = MathF.Sqrt(dx * dx + dy * dy);
                    float warpU = 0.04f * WorldNoise.FbmTile(u, v, 4, 3, 161);
                    float warpV = 0.04f * WorldNoise.FbmTile(u, v, 4, 3, 163);
                    float baseN = WorldNoise.FbmTile(u + warpU, v + warpV, 5, 6, 165);
                    float cells = 1f - WorldNoise.CellTile(u + warpU, v + warpV, 9, 167);
                    float dens = baseN * 1.15f + 0.45f * cells - 0.36f;
                    float a = SmoothStep(0.0f, 0.38f, dens);
                    float core = SmoothStep(0.25f, 0.75f, dens);
                    float fade = 1f - SmoothStep(0.72f, 0.97f, rho);
                    float shade = 1f - 0.28f * core + 0.06f * WorldNoise.FbmTile(u, v, 30, 2, 169);
                    int i = (y * size + x) * 4;
                    byte s = B(shade);
                    p[i] = s; p[i + 1] = s; p[i + 2] = s;
                    p[i + 3] = B(a * fade * 0.92f);
                }
            return p;
        }
    }
}
