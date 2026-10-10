using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural meshes and textures for the first-person view model (smooth lathe shapes, neoprene texture).
    public static class PlayerMeshes
    {
        /// Revolves a profile (x = position along +Z, y = radius) around the Z axis. A radius of 0 at the first/last
        /// point closes the shape with a pole; two identical consecutive points create a hard crease (flat caps).
        public static Mesh Lathe(string name, Vector2[] profile, int segments, float uvScale)
        {
            var verts = new List<Vector3>(profile.Length * segments);
            var uvs = new List<Vector2>(profile.Length * segments);
            var tris = new List<int>(profile.Length * segments * 6);
            int n = profile.Length;
            var start = new int[n];
            var pole = new bool[n];
            float step = Mathf.PI * 2f / segments;

            for (int i = 0; i < n; i++)
            {
                start[i] = verts.Count;
                float z = profile[i].x, r = profile[i].y;
                if (r <= 1e-5f)
                {
                    pole[i] = true;
                    verts.Add(new Vector3(0f, 0f, z));
                }
                else
                {
                    for (int j = 0; j < segments; j++)
                    {
                        float a = j * step;
                        verts.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                    }
                }
            }

            for (int i = 0; i < n - 1; i++)
            {
                if ((profile[i] - profile[i + 1]).sqrMagnitude < 1e-10f) continue;    // crease
                if (pole[i] && pole[i + 1]) continue;
                int a = start[i], b = start[i + 1];
                for (int j = 0; j < segments; j++)
                {
                    int j1 = (j + 1) % segments;
                    if (pole[i])
                    {
                        tris.Add(a); tris.Add(b + j1); tris.Add(b + j);
                    }
                    else if (pole[i + 1])
                    {
                        tris.Add(a + j); tris.Add(a + j1); tris.Add(b);
                    }
                    else
                    {
                        tris.Add(a + j); tris.Add(a + j1); tris.Add(b + j);
                        tris.Add(a + j1); tris.Add(b + j1); tris.Add(b + j);
                    }
                }
            }

            // seamless planar UVs (the neoprene texture is noise, no seam needed)
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                uvs.Add(new Vector2((v.x * 0.8f + v.z) * uvScale, (v.y + v.z * 0.35f) * uvScale));
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// Unit-diameter sphere centered at the origin.
        public static Mesh Sphere()
        {
            const int rings = 10;
            var prof = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                prof[i] = new Vector2(-0.5f * Mathf.Cos(a), i == 0 || i == rings ? 0f : 0.5f * Mathf.Sin(a));
            }
            return Lathe("SB_PlayerSphere", prof, 16, 3f);
        }

        /// Capsule from z = 0 to z = 1, radius 0.5 (rounded caps take a quarter of the length each). Scale it per part.
        public static Mesh Capsule()
        {
            const int capSteps = 4;
            var prof = new List<Vector2>();
            for (int i = 0; i <= capSteps; i++)
            {
                float a = Mathf.PI * 0.5f * i / capSteps;
                prof.Add(new Vector2(0.25f * (1f - Mathf.Cos(a)), i == 0 ? 0f : 0.5f * Mathf.Sin(a)));
            }
            for (int i = capSteps; i >= 0; i--)
            {
                float a = Mathf.PI * 0.5f * i / capSteps;
                prof.Add(new Vector2(1f - 0.25f * (1f - Mathf.Cos(a)), i == 0 ? 0f : 0.5f * Mathf.Sin(a)));
            }
            return Lathe("SB_PlayerCapsule", prof.ToArray(), 12, 3f);
        }

        /// Forearm-like tapered tube from z = 0 (rounded elbow, radius 0.5) to z = 1 (wrist, radius ~0.4).
        public static Mesh Taper()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.012f, 0.3f), new Vector2(0.04f, 0.45f), new Vector2(0.09f, 0.5f),
                new Vector2(0.5f, 0.48f), new Vector2(0.88f, 0.43f), new Vector2(0.96f, 0.41f), new Vector2(0.99f, 0.32f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerTaper", prof, 16, 3f);
        }

        /// Flat-capped cylinder from z = 0 to z = 1, radius 0.5.
        public static Mesh Cylinder()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerCylinder", prof, 16, 3f);
        }

        /// Lamp head: narrow base flaring to radius 0.5, flat front.
        public static Mesh Flare()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 0.36f), new Vector2(0f, 0.36f),
                new Vector2(0.55f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerFlare", prof, 16, 3f);
        }

        /// Tileable neoprene grain: fine noise, tiny knit pattern and soft blotches (grayscale, tinted by the material).
        public static Texture2D NeopreneTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "SB_Neoprene",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };
            var px = new Color32[size * size];
            float tau = Mathf.PI * 2f;
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)size;
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float grain = (Hash(x, y) - 0.5f) * 0.16f;
                    float knit = ((x >> 1) + (y >> 1)) % 2 == 0 ? 0.035f : -0.015f;
                    float blotch = Mathf.Sin(tau * (u * 3f + 0.25f * Mathf.Sin(tau * v * 2f))) * Mathf.Sin(tau * v * 3f) * 0.05f;
                    float val = Mathf.Clamp01(0.86f + grain + knit + blotch);
                    byte b = (byte)(val * 255f);
                    px[y * size + x] = new Color32(b, b, b, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }
    }
}
