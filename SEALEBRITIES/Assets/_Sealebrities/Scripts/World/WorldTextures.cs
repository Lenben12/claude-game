using UnityEngine;

namespace Sealebrities
{
    /// Turns WorldTextureGen pixel data into GPU textures and terrain layers (main thread only).
    public static class WorldTextures
    {
        public const int TerrainTextureSize = 512;

        /// Uploads raw RGBA32 pixels (row 0 = bottom) into a new texture with mipmaps; the CPU copy is released.
        public static Texture2D Make(byte[] rgba, int size, bool linear, string name, TextureWrapMode wrap = TextureWrapMode.Repeat, int aniso = 4)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear)
            {
                name = name,
                wrapMode = wrap,
                filterMode = FilterMode.Trilinear,
                anisoLevel = aniso,
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true);
            return tex;
        }

        /// Tile size in meters of each terrain surface.
        public static float TileSize(WorldSurface s)
        {
            switch (s)
            {
                case WorldSurface.BeachSand: return 5f;
                case WorldSurface.WetSand: return 5f;
                case WorldSurface.Grass: return 7f;
                case WorldSurface.JungleGrass: return 17f;
                case WorldSurface.Soil: return 6f;
                case WorldSurface.Rock: return 13f;
                case WorldSurface.SeabedSand: return 7.5f;
                default: return 12f;
            }
        }

        /// Smoothness multiplier of each layer (the per-pixel smoothness lives in the albedo alpha channel).
        static float Smoothness(WorldSurface s)
        {
            switch (s)
            {
                case WorldSurface.WetSand: return 1f;
                case WorldSurface.Rock: return 0.9f;
                default: return 1f;
            }
        }

        /// Creates one TerrainLayer per WorldSurface (in enum order) from generated pixel data.
        public static TerrainLayer[] CreateTerrainLayers(WorldPixels[] pixels)
        {
            var layers = new TerrainLayer[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                var s = (WorldSurface)i;
                var px = pixels[i];
                var albedo = Make(px.albedo, px.size, false, "SB_Terrain_" + s, TextureWrapMode.Repeat, 8);
                var normal = Make(px.normal, px.size, true, "SB_Terrain_" + s + "_N", TextureWrapMode.Repeat, 8);
                float tile = TileSize(s);
                layers[i] = new TerrainLayer
                {
                    name = "SB_Layer_" + s,
                    diffuseTexture = albedo,
                    normalMapTexture = normal,
                    normalScale = 1f,
                    tileSize = new Vector2(tile, tile),
                    tileOffset = Vector2.zero,
                    metallic = 0f,
                    smoothness = Smoothness(s),
                };
            }
            return layers;
        }
    }
}
