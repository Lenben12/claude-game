using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Shared low-poly art style ("How to Fish" / Safari-TV look): faceted flat shading, solid colors, one palette.
    public static class LowPoly
    {
        public const float Smoothness = 0.08f;

        // ------------------------------------------------------------------ palette
        // "How to Fish" master palette (docs/ArtBible_HowToFish.md §1.7): albedo values measured/derived from the
        // How to Fish references – olive greens, violet-grey darks, steel-blue water, weathered wood, warm off-white.
        public static readonly Color Sand = Hex("#E6CFA0");
        public static readonly Color WetSand = Hex("#C9AF86");
        public static readonly Color Seabed = Hex("#EAC27A");   // warmer than the bible's #E2C890: the teal light still reads sandy
        // (art polish: lit lawns measured lime #74A037..#90AB58 vs How to Fish #596646/#668443 → darker, less saturated;
        // lit rock read light lavender-grey "concrete" → a step darker)
        public static readonly Color Grass = Hex("#5D7C45");
        public static readonly Color GrassDark = Hex("#486036");
        public static readonly Color Soil = Hex("#8E6A4C");
        public static readonly Color Rock = Hex("#787A86");
        public static readonly Color RockDark = Hex("#56545F");
        public static readonly Color DeepRock = Hex("#3B3A4E");
        /// Above-water sea blue (props that used Water for shutters/cushions should use Turquoise instead).
        public static readonly Color Water = Hex("#3778AB");
        public static readonly Color WaterDeep = Hex("#284F80");
        /// Above-water shallow (≈ 1.5 m deep) sea; under water the FX colour stops apply.
        public static readonly Color WaterShallow = Hex("#3D93B6");
        public static readonly Color Foam = Hex("#EEF4F6");
        public static readonly Color PalmTrunk = Hex("#7A4A50");   // HtF trunk: brown-mauve (measured lit #855F66), not magenta
        public static readonly Color PalmLeaf = Hex("#7FA243");
        public static readonly Color PalmLeafDark = Hex("#4E6F2E");
        public static readonly Color PalmLeafDry = Hex("#B5A050");
        public static readonly Color Wood = Hex("#8E6C50");
        public static readonly Color WoodDark = Hex("#5E4436");
        public static readonly Color WoodLight = Hex("#C9B08E");
        public static readonly Color Roof = Hex("#A8484A");
        public static readonly Color Metal = Hex("#8E949C");
        public static readonly Color Rust = Hex("#A0583A");
        /// Warm off-white: never use pure #FFFFFF on geometry.
        public static readonly Color White = Hex("#EDE6DA");
        /// Violet black.
        public static readonly Color Ink = Hex("#1E1B2B");
        public static readonly Color CoralPink = Hex("#F2708A");
        public static readonly Color CoralOrange = Hex("#F28C4A");
        public static readonly Color CoralPurple = Hex("#8E5CC2");
        public static readonly Color CoralYellow = Hex("#F2C94C");
        public static readonly Color Kelp = Hex("#7E9A3A");
        public static readonly Color KelpDark = Hex("#55702A");
        /// On-screen sky targets (zenith / horizon at noon).
        public static readonly Color SkyTop = Hex("#4A88BF");
        public static readonly Color SkyHorizon = Hex("#8DBDEA");
        public static readonly Color Skin = Hex("#E2B985");
        public static readonly Color Neoprene = Hex("#343849");
        public static readonly Color SuitOrange = Hex("#EE7F2D");

        // new How to Fish entries (art bible §1.7)
        public static readonly Color GrassBase = Hex("#34502A");
        public static readonly Color GrassTip = Hex("#8AA352");
        public static readonly Color GrassDry = Hex("#8C9654");
        public static readonly Color PathDirt = Hex("#A88763");
        public static readonly Color Foliage = Hex("#5E8A3A");
        public static readonly Color FoliageDark = Hex("#46682C");
        public static readonly Color FoliageLight = Hex("#7A9C45");
        public static readonly Color Conifer = Hex("#3F5F35");
        public static readonly Color ConiferLight = Hex("#567A3E");
        public static readonly Color TreeBark = Hex("#5E4A44");
        public static readonly Color Whitewash = Hex("#DCCFBE");
        public static readonly Color PlankGap = Hex("#3A2C2A");
        public static readonly Color AccentBoard = Hex("#7A4448");
        public static readonly Color Turquoise = Hex("#3FA9B5");
        public static readonly Color FlowerMagenta = Hex("#A8478F");
        public static readonly Color FlowerPink = Hex("#E86FA0");
        public static readonly Color FlowerWhite = Hex("#F4EFE6");
        public static readonly Color FlowerPurple = Hex("#8E5CC2");

        public static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        /// Flat, matte, shared material in the low-poly style.
        public static Material Mat(Color c) => MaterialLibrary.Opaque(c, Smoothness, 0f);

        /// Slight per-object color variation (keeps scenes from looking copy-pasted). amount ~0.04–0.1.
        public static Color Vary(Color c, float amount, System.Random rng)
        {
            float d = ((float)rng.NextDouble() * 2f - 1f) * amount;
            Color.RGBToHSV(c, out float h, out float s, out float v);
            return Color.HSVToRGB(Mathf.Repeat(h + d * 0.15f, 1f), Mathf.Clamp01(s + d * 0.5f), Mathf.Clamp01(v + d));
        }

        // ------------------------------------------------------------------ geometry
        /// Returns a new mesh where every triangle has its own vertices → faceted flat shading.
        /// Preserves submeshes, uv, colors, bone weights and bind poses. Invalid (NaN/∞) vertices are replaced by zero.
        public static Mesh Flatten(Mesh src)
        {
            if (src == null) return null;
            var verts = src.vertices;
            var uv = src.uv;
            var cols = src.colors;
            var bw = src.boneWeights;
            bool hasUv = uv != null && uv.Length == verts.Length;
            bool hasCol = cols != null && cols.Length == verts.Length;
            bool hasBw = bw != null && bw.Length == verts.Length;

            int subCount = src.subMeshCount;
            var nv = new List<Vector3>(verts.Length * 2);
            var nuv = hasUv ? new List<Vector2>(verts.Length * 2) : null;
            var ncol = hasCol ? new List<Color>(verts.Length * 2) : null;
            var nbw = hasBw ? new List<BoneWeight>(verts.Length * 2) : null;
            var subs = new List<int>[subCount];

            for (int s = 0; s < subCount; s++)
            {
                var tris = src.GetTriangles(s);
                var list = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i++)
                {
                    int idx = tris[i];
                    nv.Add(Valid(verts[idx]));
                    if (hasUv) nuv.Add(uv[idx]);
                    if (hasCol) ncol.Add(cols[idx]);
                    if (hasBw) nbw.Add(bw[idx]);
                    list.Add(nv.Count - 1);
                }
                subs[s] = list;
            }

            var m = new Mesh { name = src.name };
            if (nv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(nv);
            if (hasUv) m.SetUVs(0, nuv);
            if (hasCol) m.SetColors(ncol);
            m.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++) m.SetTriangles(subs[s], s, false);
            if (hasBw)
            {
                m.boneWeights = nbw.ToArray();
                m.bindposes = src.bindposes;
            }
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// Replaces the mesh of a MeshFilter with its flattened version (destroys the old mesh if it was not shared).
        public static void FlattenFilter(MeshFilter mf, bool destroyOld = true)
        {
            if (mf == null || mf.sharedMesh == null) return;
            var old = mf.sharedMesh;
            mf.sharedMesh = Flatten(old);
            if (destroyOld) Object.Destroy(old);
        }

        /// Replaces NaN/Infinity vertices with zero; returns true when something was fixed.
        public static bool Sanitize(Mesh m)
        {
            if (m == null) return false;
            var v = m.vertices;
            bool bad = false;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i];
                if (!IsFinite(p)) { v[i] = Vector3.zero; bad = true; }
            }
            if (bad)
            {
                m.vertices = v;
                m.RecalculateNormals();
                m.RecalculateBounds();
                Debug.LogWarning("Sealebrities: fixed invalid vertices in mesh " + m.name);
            }
            return bad;
        }

        static bool IsFinite(Vector3 p) =>
            !(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z));

        static Vector3 Valid(Vector3 p) => IsFinite(p) ? p : Vector3.zero;

        /// Tiny point-filtered texture for crisp color-block patterns (e.g. clownfish bands) without a "realistic" look.
        public static Texture2D BlockTexture(int width, int height, Color[] pixels, string name = "SB_Block")
        {
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            t.SetPixels(pixels);
            t.Apply(false, false);
            return t;
        }
    }
}
