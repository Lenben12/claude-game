using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural low-poly meshes for the first-person view model (hands, gloves, dive lamp): few sides, chunky
    /// blocks, faceted flat shading (every triangle has its own vertices). Solid colors only, so no UVs.
    public static class PlayerMeshes
    {
        /// Revolves a profile (x = position along +Z, y = radius) around the Z axis with only a few sides. A radius of 0
        /// at the first/last point closes the shape with a pole; two identical consecutive points create a flat cap.
        public static Mesh Lathe(string name, Vector2[] profile, int segments)
        {
            var verts = new List<Vector3>(profile.Length * segments);
            var tris = new List<int>(profile.Length * segments * 6);
            int n = profile.Length;
            var start = new int[n];
            var pole = new bool[n];
            float step = Mathf.PI * 2f / segments;
            float offset = step * 0.5f;   // flat face on top / bottom

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
                        float a = j * step + offset;
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
            return Faceted(name, verts, tris);
        }

        /// Unwelds the triangles (flat shading) and drops degenerate ones.
        static Mesh Faceted(string name, List<Vector3> verts, List<int> tris)
        {
            var fv = new List<Vector3>(tris.Count);
            var fn = new List<Vector3>(tris.Count);
            var ft = new List<int>(tris.Count);
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], c = verts[tris[t + 2]];
                Vector3 f = Vector3.Cross(b - a, c - a);
                float sq = f.sqrMagnitude;
                if (!(sq >= 1e-14f)) continue;
                Vector3 nrm = f / Mathf.Sqrt(sq);   // Vector3.normalized would return zero for tiny faces (NaN pixels)
                int i = fv.Count;
                fv.Add(a); fv.Add(b); fv.Add(c);
                fn.Add(nrm); fn.Add(nrm); fn.Add(nrm);
                ft.Add(i); ft.Add(i + 1); ft.Add(i + 2);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(fv);
            mesh.SetNormals(fn);
            mesh.SetTriangles(ft, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// Unit-diameter faceted ball centered at the origin.
        public static Mesh Sphere()
        {
            const int rings = 4;
            var prof = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * i / rings;
                prof[i] = new Vector2(-0.5f * Mathf.Cos(a), i == 0 || i == rings ? 0f : 0.5f * Mathf.Sin(a));
            }
            return Lathe("SB_PlayerSphere", prof, 8);
        }

        /// Chunky capsule from z = 0 to z = 1, radius 0.5 (one bevel ring per end). Scale it per part.
        public static Mesh Capsule()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.08f, 0.38f), new Vector2(0.25f, 0.5f),
                new Vector2(0.75f, 0.5f), new Vector2(0.92f, 0.38f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerCapsule", prof, 6);
        }

        /// Forearm-like tapered tube from z = 0 (blunt elbow, radius 0.5) to z = 1 (wrist, radius ~0.4).
        public static Mesh Taper()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.03f, 0.42f), new Vector2(0.1f, 0.5f),
                new Vector2(0.92f, 0.43f), new Vector2(1f, 0.36f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerTaper", prof, 8);
        }

        /// Flat-capped 8-sided prism from z = 0 to z = 1, radius 0.5.
        public static Mesh Cylinder()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerCylinder", prof, 8);
        }

        /// Lamp head: narrow base flaring to radius 0.5, flat front (8 sides).
        public static Mesh Flare()
        {
            var prof = new[]
            {
                new Vector2(0f, 0f), new Vector2(0f, 0.36f), new Vector2(0f, 0.36f),
                new Vector2(0.55f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0f)
            };
            return Lathe("SB_PlayerFlare", prof, 8);
        }

        /// Blocky finger segment / palm: chamfered box spanning x,y in -0.5..0.5 and z in 0..1 (bevel 0.2 of the size).
        public static Mesh Block() => ChamferBox("SB_PlayerBlock", new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 1f), 0.2f);

        /// Chamfered box centered at the origin with unit size (bevel 0.18 of the size).
        public static Mesh BlockCentered() => ChamferBox("SB_PlayerBlockC", new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f), 0.18f);

        /// Box with one flat chamfer on every edge and a triangle on every corner (26 faces, 44 triangles).
        public static Mesh ChamferBox(string name, Vector3 min, Vector3 max, float bevel)
        {
            Vector3 c = (min + max) * 0.5f;
            Vector3 lo = min + Vector3.one * bevel, hi = max - Vector3.one * bevel;
            var verts = new List<Vector3>(132);
            var tris = new List<int>(132);

            // P(corner, axis): inner corner pushed out along one axis
            Vector3 Corner(int sx, int sy, int sz) => new Vector3(sx > 0 ? hi.x : lo.x, sy > 0 ? hi.y : lo.y, sz > 0 ? hi.z : lo.z);
            Vector3 P(int sx, int sy, int sz, int axis)
            {
                Vector3 p = Corner(sx, sy, sz);
                if (axis == 0) p.x += sx > 0 ? bevel : -bevel;
                else if (axis == 1) p.y += sy > 0 ? bevel : -bevel;
                else p.z += sz > 0 ? bevel : -bevel;
                return p;
            }
            void Tri(Vector3 a, Vector3 b, Vector3 d)
            {
                Vector3 f = Vector3.Cross(b - a, d - a);
                int i = verts.Count;
                if (Vector3.Dot(f, (a + b + d) / 3f - c) < 0f) { verts.Add(a); verts.Add(d); verts.Add(b); }
                else { verts.Add(a); verts.Add(b); verts.Add(d); }
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 d, Vector3 e) { Tri(a, b, d); Tri(a, d, e); }

            int[] s = { -1, 1 };
            // six main faces
            foreach (int sg in s)
            {
                Quad(P(sg, -1, -1, 0), P(sg, 1, -1, 0), P(sg, 1, 1, 0), P(sg, -1, 1, 0));
                Quad(P(-1, sg, -1, 1), P(1, sg, -1, 1), P(1, sg, 1, 1), P(-1, sg, 1, 1));
                Quad(P(-1, -1, sg, 2), P(1, -1, sg, 2), P(1, 1, sg, 2), P(-1, 1, sg, 2));
            }
            // twelve edge chamfers
            foreach (int a in s)
                foreach (int b in s)
                {
                    Quad(P(a, b, -1, 0), P(a, b, -1, 1), P(a, b, 1, 1), P(a, b, 1, 0));   // along z
                    Quad(P(a, -1, b, 0), P(a, -1, b, 2), P(a, 1, b, 2), P(a, 1, b, 0));   // along y
                    Quad(P(-1, a, b, 1), P(-1, a, b, 2), P(1, a, b, 2), P(1, a, b, 1));   // along x
                }
            // eight corner triangles
            foreach (int x in s)
                foreach (int y in s)
                    foreach (int z in s)
                        Tri(P(x, y, z, 0), P(x, y, z, 1), P(x, y, z, 2));

            return Faceted(name, verts, tris);
        }
    }
}
