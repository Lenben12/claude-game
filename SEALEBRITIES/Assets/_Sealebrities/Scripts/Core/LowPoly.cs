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
        public static readonly Color Sand = Hex("#F2D9A6");
        public static readonly Color WetSand = Hex("#DCC08A");
        public static readonly Color Seabed = Hex("#E6D2A2");
        public static readonly Color Grass = Hex("#7CC04B");
        public static readonly Color GrassDark = Hex("#55A23C");
        public static readonly Color Soil = Hex("#A06C46");
        public static readonly Color Rock = Hex("#8E9098");
        public static readonly Color RockDark = Hex("#61636E");
        public static readonly Color DeepRock = Hex("#3E4256");
        public static readonly Color Water = Hex("#4DB3D6");
        public static readonly Color WaterDeep = Hex("#2C72AA");
        public static readonly Color WaterShallow = Hex("#7FD8E0");
        public static readonly Color Foam = Hex("#F4FBFF");
        public static readonly Color PalmTrunk = Hex("#7A3B32");
        public static readonly Color PalmLeaf = Hex("#6CC24A");
        public static readonly Color PalmLeafDark = Hex("#4FA03D");
        public static readonly Color PalmLeafDry = Hex("#C9B45A");
        public static readonly Color Wood = Hex("#C99A6B");
        public static readonly Color WoodDark = Hex("#8B5A3C");
        public static readonly Color WoodLight = Hex("#E2C79F");
        public static readonly Color Roof = Hex("#B24A3A");
        public static readonly Color Metal = Hex("#9AA3AB");
        public static readonly Color Rust = Hex("#B5603A");
        public static readonly Color White = Hex("#F4F1EA");
        public static readonly Color Ink = Hex("#22252E");
        public static readonly Color CoralPink = Hex("#FF7A8A");
        public static readonly Color CoralOrange = Hex("#FF9F5A");
        public static readonly Color CoralPurple = Hex("#9B6BD6");
        public static readonly Color CoralYellow = Hex("#FFD25E");
        public static readonly Color Kelp = Hex("#8FA83A");
        public static readonly Color KelpDark = Hex("#5F7A2A");
        public static readonly Color SkyTop = Hex("#79B4E8");
        public static readonly Color SkyHorizon = Hex("#CFE6F2");
        public static readonly Color Skin = Hex("#E8B98F");
        public static readonly Color Neoprene = Hex("#262A33");
        public static readonly Color SuitOrange = Hex("#FF7A2E");

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
