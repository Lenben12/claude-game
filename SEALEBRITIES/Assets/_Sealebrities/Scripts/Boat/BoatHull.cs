using UnityEngine;

namespace Sealebrities
{
    /// Hull lines of the "Rostlaube" (local space: +Z bow, +X starboard, y = 0 is the design waterline).
    /// The hull is lofted from stations t = 0 (transom) .. 1 (stem); each section is a quadratic Bezier
    /// from the keel (x = 0) to the sheer line. The paint texture uses the same functions so its stripes follow the hull.
    public static class BoatHull
    {
        public const float Length = 7.5f;
        public const float HalfLength = 3.75f;
        public const float DeckY = 0.25f;
        public const float WallThickness = 0.06f;
        public const float TexY0 = -0.65f;
        public const float TexYRange = 1.95f;
        public const int Stations = 32;
        public const int ProfilePoints = 10;

        public static float T(float z) => (z + HalfLength) / Length;
        public static float Z(float t) => t * Length - HalfLength;

        static float Smooth(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        public static float SheerHalf(float t)
        {
            if (t <= 0.45f) return Mathf.Lerp(1.2f, 1.45f, Smooth(0f, 0.45f, t));
            float k = (t - 0.45f) / 0.55f;
            return Mathf.Max(0.035f, 1.45f * Mathf.Pow(Mathf.Max(0f, 1f - k * k), 0.55f));
        }

        public static float SheerY(float t) => 0.52f + 0.68f * Mathf.Pow(Mathf.Clamp01(t), 1.6f);

        public static float KeelY(float t)
        {
            if (t > 0.55f)
            {
                float k = (t - 0.55f) / 0.45f;
                return -0.6f + 1.55f * Mathf.Pow(Mathf.Clamp01(k), 2.2f);
            }
            return -0.6f + 0.1f * (1f - Smooth(0f, 0.15f, t));
        }

        static void Controls(float t, out Vector2 K, out Vector2 C, out Vector2 S)
        {
            float w = SheerHalf(t), ky = KeelY(t), sy = SheerY(t);
            float bow = Smooth(0.5f, 1f, t);
            K = new Vector2(0f, ky);
            S = new Vector2(w, sy);
            C = new Vector2(w * Mathf.Lerp(0.98f, 0.7f, bow), ky + (sy - ky) * Mathf.Lerp(0.16f, 0.42f, bow));
        }

        /// Point of the starboard half section at parameter s (0 keel .. 1 sheer).
        public static Vector2 Section(float t, float s)
        {
            Controls(t, out var K, out var C, out var S);
            float a = 1f - s;
            return a * a * K + 2f * a * s * C + s * s * S;
        }

        public static Vector2 SectionTangent(float t, float s)
        {
            Controls(t, out var K, out var C, out var S);
            Vector2 d = 2f * (1f - s) * (C - K) + 2f * s * (S - C);
            return d.sqrMagnitude > 1e-10f ? d.normalized : Vector2.up;
        }

        /// Section parameter where the hull reaches height y (clamped to 0..1).
        public static float SectionSAtY(float t, float y)
        {
            float ky = KeelY(t), sy = SheerY(t);
            if (y <= ky) return 0f;
            if (y >= sy) return 1f;
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 22; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Section(t, mid).y < y) lo = mid; else hi = mid;
            }
            return (lo + hi) * 0.5f;
        }

        /// Outer half-width of the hull at height y.
        public static float HullXAtY(float t, float y) => Section(t, SectionSAtY(t, y)).x;

        /// Inner half-width of the deck (0 where the keel rises above the deck near the stem).
        public static float DeckHalf(float t)
        {
            if (KeelY(t) >= DeckY - 0.02f) return 0f;
            return Mathf.Max(0f, HullXAtY(t, DeckY) - WallThickness);
        }

        static float[] deckTable;
        const int DeckTableSize = 96;

        /// Fast deck half-width lookup by local z.
        public static float DeckHalfAtZ(float z)
        {
            if (deckTable == null)
            {
                deckTable = new float[DeckTableSize + 1];
                for (int i = 0; i <= DeckTableSize; i++) deckTable[i] = DeckHalf(i / (float)DeckTableSize);
            }
            float t = T(z);
            if (t < 0f || t > 1f) return 0f;
            float f = t * DeckTableSize;
            int i0 = Mathf.Min(DeckTableSize - 1, Mathf.FloorToInt(f));
            return Mathf.Lerp(deckTable[i0], deckTable[i0 + 1], f - i0);
        }

        /// UV on the hull paint texture. side: +1 starboard (upper texture half), -1 port (lower half, u mirrored).
        public static Vector2 HullUV(int side, float t, float y)
        {
            float vn = (y - TexY0) / TexYRange;
            float v = (side > 0 ? 0.5f : 0f) + (0.02f + vn * 0.96f) * 0.5f;
            return new Vector2(side > 0 ? t : 1f - t, v);
        }

        // ------------------------------------------------------------------ meshes
        public static void BuildOuter(BoatMeshBuilder b)
        {
            int N = Stations, P = ProfilePoints;
            for (int side = -1; side <= 1; side += 2)
            {
                int start = b.Count;
                for (int i = 0; i < N; i++)
                {
                    float t = i / (N - 1f);
                    float z = Z(t);
                    for (int j = 0; j < P; j++)
                    {
                        float s = j / (P - 1f);
                        Vector2 q = Section(t, s);
                        Vector2 tan = SectionTangent(t, s);
                        Vector3 n = new Vector3(side * tan.y, -tan.x, 0f);
                        b.V(new Vector3(side * q.x, q.y, z), n, HullUV(side, t, q.y));
                    }
                }
                for (int i = 0; i < N - 1; i++)
                    for (int j = 0; j < P - 1; j++)
                    {
                        int a = start + i * P + j;
                        b.QuadAuto(a, a + P, a + P + 1, a + 1);
                    }
            }

            // stem: close the thin slit between both sides at the bow
            {
                float t = 1f, z = Z(1f);
                for (int j = 0; j < P - 1; j++)
                {
                    Vector2 q0 = Section(t, j / (P - 1f)), q1 = Section(t, (j + 1) / (P - 1f));
                    b.Quad(new Vector3(-q0.x, q0.y, z), new Vector3(-q1.x, q1.y, z), new Vector3(q1.x, q1.y, z), new Vector3(q0.x, q0.y, z),
                        Vector3.forward, HullUV(1, 1f, q0.y), HullUV(1, 1f, q1.y), HullUV(1, 0.999f, q1.y), HullUV(1, 0.999f, q0.y));
                }
            }

            // transom: fan from the section center
            {
                float z = Z(0f);
                float ky = KeelY(0f), sy = SheerY(0f);
                Vector3 c = new Vector3(0f, (ky + sy) * 0.5f, z);
                int ci = b.V(c, Vector3.back, TransomUV(c));
                int first = b.Count;
                for (int j = P - 1; j >= 0; j--)
                {
                    Vector2 q = Section(0f, j / (P - 1f));
                    var p = new Vector3(-q.x, q.y, z);
                    b.V(p, Vector3.back, TransomUV(p));
                }
                for (int j = 1; j < P; j++)
                {
                    Vector2 q = Section(0f, j / (P - 1f));
                    var p = new Vector3(q.x, q.y, z);
                    b.V(p, Vector3.back, TransomUV(p));
                }
                // close the top edge (sheer to sheer) with the center fan
                int count = b.Count - first;
                for (int k = 0; k < count - 1; k++) b.TriAuto(ci, first + k, first + k + 1);
                b.TriAuto(ci, first + count - 1, first);
            }
        }

        static Vector2 TransomUV(Vector3 p) => new Vector2((p.x + 1.25f) / Length, HullUV(1, 0f, p.y).y);

        /// Inner bulwark faces (above the deck) and the inner transom face.
        public static void BuildInner(BoatMeshBuilder b)
        {
            int N = Stations;
            const int K = 4;
            for (int side = -1; side <= 1; side += 2)
            {
                int start = b.Count;
                for (int i = 0; i < N; i++)
                {
                    float t = i / (N - 1f);
                    float z = Z(t);
                    float y0 = Mathf.Max(DeckY, KeelY(t)), y1 = SheerY(t);
                    for (int k = 0; k < K; k++)
                    {
                        float y = Mathf.Lerp(y0, y1, k / (K - 1f));
                        float x = Mathf.Max(0.004f, HullXAtY(t, y) - WallThickness);
                        b.V(new Vector3(side * x, y, z), new Vector3(-side, 0f, 0f), new Vector2(z, y));
                    }
                }
                for (int i = 0; i < N - 1; i++)
                    for (int k = 0; k < K - 1; k++)
                    {
                        int a = start + i * K + k;
                        b.QuadAuto(a, a + K, a + K + 1, a + 1);
                    }
            }
            // inner transom face
            float zt = Z(0f) + WallThickness;
            float xd = DeckHalf(0f), xs = HullXAtY(0f, SheerY(0f)) - WallThickness, sy0 = SheerY(0f);
            b.QuadPlanar(new Vector3(-xd, DeckY, zt), new Vector3(xd, DeckY, zt), new Vector3(xs, sy0, zt), new Vector3(-xs, sy0, zt),
                Vector3.forward, Vector3.right, Vector3.up, 1f);
        }

        public static void BuildDeck(BoatMeshBuilder b)
        {
            int N = Stations * 2;
            const float tile = 1f / 0.96f;
            int start = b.Count;
            for (int i = 0; i < N; i++)
            {
                float t = i / (N - 1f);
                float z = Z(t);
                float hw = DeckHalf(t) + 0.01f;
                if (DeckHalf(t) <= 0f) hw = 0f;
                b.V(new Vector3(-hw, DeckY, z), Vector3.up, new Vector2(-hw * tile, z * tile));
                b.V(new Vector3(hw, DeckY, z), Vector3.up, new Vector2(hw * tile, z * tile));
            }
            for (int i = 0; i < N - 1; i++)
            {
                int a = start + i * 2;
                b.QuadAuto(a, a + 2, a + 3, a + 1);
            }
        }

        /// Wooden cap rail on top of the bulwark (both sides + transom).
        public static void BuildGunwale(BoatMeshBuilder b)
        {
            int N = Stations;
            for (int side = -1; side <= 1; side += 2)
            {
                // strips: 0 outer face, 1 top, 2 inner face
                for (int strip = 0; strip < 3; strip++)
                {
                    int start = b.Count;
                    for (int i = 0; i < N; i++)
                    {
                        float t = i / (N - 1f);
                        float z = Z(t), w = SheerHalf(t), sy = SheerY(t);
                        float xo = w + 0.03f, xi = Mathf.Max(0f, w - 0.09f);
                        Vector3 pa, pb, n;
                        if (strip == 0) { pa = new Vector3(side * xo, sy - 0.05f, z); pb = new Vector3(side * xo, sy + 0.035f, z); n = new Vector3(side, 0f, 0f); }
                        else if (strip == 1) { pa = new Vector3(side * xo, sy + 0.035f, z); pb = new Vector3(side * xi, sy + 0.035f, z); n = Vector3.up; }
                        else { pa = new Vector3(side * xi, sy + 0.035f, z); pb = new Vector3(side * xi, sy - 0.05f, z); n = new Vector3(-side, 0f, 0f); }
                        // one plank strip (u 0.03..0.1) with the grain running along the boat (v = z)
                        b.V(pa, n, new Vector2(0.03f, z));
                        b.V(pb, n, new Vector2(0.1f, z));
                    }
                    for (int i = 0; i < N - 1; i++)
                    {
                        int a = start + i * 2;
                        b.QuadAuto(a, a + 2, a + 3, a + 1);
                    }
                }
            }
            float sy0 = SheerY(0f), w0 = SheerHalf(0f);
            b.Box(new Vector3(0f, sy0 - 0.0075f, Z(0f) + 0.03f), new Vector3(2f * (w0 + 0.03f), 0.085f, 0.12f), 1.5f);
        }

        /// Convex lower hull (keel up to the deck) used as the boat's main collider; its top face is the deck.
        public static Mesh BuildColliderMesh()
        {
            var b = new BoatMeshBuilder();
            const int N = 16, P = 6;
            int rows = 0;
            int start = b.Count;
            for (int i = 0; i < N; i++)
            {
                float t = i / (N - 1f) * 0.97f;
                if (KeelY(t) >= DeckY - 0.03f) break;
                float z = Z(t);
                float sDeck = SectionSAtY(t, DeckY);
                for (int side = -1; side <= 1; side += 2)
                    for (int j = 0; j < P; j++)
                    {
                        float s = sDeck * j / (P - 1f);
                        Vector2 q = Section(t, s);
                        if (j == P - 1) q.y = DeckY;
                        b.V(new Vector3(side * q.x, q.y, z), new Vector3(side, -0.3f, 0f), Vector2.zero);
                    }
                rows++;
            }
            int perRow = P * 2;
            for (int r = 0; r < rows - 1; r++)
                for (int k = 0; k < perRow - 1; k++)
                {
                    int a = start + r * perRow + k;
                    b.Tri(a, a + perRow, a + 1);
                    b.Tri(a + 1, a + perRow, a + perRow + 1);
                }
            var mesh = new Mesh { name = "BoatHullCollider" };
            mesh.SetVertices(b.Verts);
            mesh.SetTriangles(b.Tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
