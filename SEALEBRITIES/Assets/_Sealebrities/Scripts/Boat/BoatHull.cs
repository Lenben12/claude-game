using UnityEngine;

namespace Sealebrities
{
    /// Hull lines of the "Rostlaube" (local space: +Z bow, +X starboard, y = 0 is the design waterline).
    /// The smooth lines (stations t = 0 transom .. 1 stem, each section a quadratic Bezier from the keel to the sheer)
    /// drive physics, colliders and deck queries. The visible hull is a chunky low-poly loft of a few stations whose
    /// profile points lie exactly on the paint band borders, so every color band is a clean flat-colored strip:
    /// dark red antifouling, a rust boot stripe, teal topsides (with a few cartoon rust streaks) and a white sheer strake.
    public static class BoatHull
    {
        public const float Length = 7.5f;
        public const float HalfLength = 3.75f;
        public const float DeckY = 0.25f;
        public const float WallThickness = 0.06f;
        public const int Stations = 32;

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

        /// Outer half-width of the (smooth) hull at height y.
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

        // ================================================================== low-poly visual hull
        /// Stations of the faceted hull: coarse amidships, a little denser toward the curved bow.
        static readonly float[] VisT = { 0f, 0.11f, 0.23f, 0.35f, 0.47f, 0.58f, 0.68f, 0.77f, 0.85f, 0.91f, 0.96f, 1f };
        const int Prof = 7;
        const float WaterlineY = 0.03f, BootTopY = 0.15f;
        static float StrakeY(float t) => SheerY(t) - 0.16f;

        static Vector2[,] prof;      // [station, profile point] starboard (x, y)
        static Vector2[,] profN;     // smooth outward normal hints

        static void EnsureProfiles()
        {
            if (prof != null) return;
            int n = VisT.Length;
            prof = new Vector2[n, Prof];
            profN = new Vector2[n, Prof];
            for (int i = 0; i < n; i++)
            {
                float t = VisT[i];
                for (int j = 0; j < Prof; j++)
                {
                    float s = ProfileS(t, j);
                    prof[i, j] = Section(t, s);
                    Vector2 tan = SectionTangent(t, s);
                    profN[i, j] = new Vector2(tan.y, -tan.x);
                }
            }
        }

        /// Section parameter of profile point j: keel, chine, waterline, boot-stripe top, mid topsides, strake, sheer.
        static float ProfileS(float t, int j)
        {
            float ky = KeelY(t);
            switch (j)
            {
                case 0: return 0f;
                case 1: return SectionSAtY(t, Mathf.Lerp(ky, WaterlineY, 0.55f));
                case 2: return SectionSAtY(t, WaterlineY);
                case 3: return SectionSAtY(t, BootTopY);
                case 4: return SectionSAtY(t, Mathf.Lerp(BootTopY, StrakeY(t), 0.5f));
                case 5: return SectionSAtY(t, StrakeY(t));
                default: return 1f;
            }
        }

        /// Paint of the band between profile points j and j + 1.
        static Material BandMat(int j)
        {
            if (j <= 1) return BoatMats.HullBottom;
            if (j == 2) return BoatMats.HullRust;
            if (j == 5) return BoatMats.HullWhite;
            return BoatMats.HullTeal;
        }

        /// Half-width of the faceted hull section of station i at height y.
        static float FacetX(int i, float y)
        {
            EnsureProfiles();
            if (y <= prof[i, 0].y) return prof[i, 0].x;
            for (int j = 0; j < Prof - 1; j++)
            {
                Vector2 a = prof[i, j], b = prof[i, j + 1];
                if (y <= b.y)
                {
                    float d = b.y - a.y;
                    return d > 1e-5f ? Mathf.Lerp(a.x, b.x, (y - a.y) / d) : b.x;
                }
            }
            return prof[i, Prof - 1].x;
        }

        static Vector3 P(int side, int i, int j) => new Vector3(side * prof[i, j].x, prof[i, j].y, Z(VisT[i]));
        static Vector3 N(int side, int i, int j) => new Vector3(side * profN[i, j].x, profN[i, j].y, 0f);

        /// Exact point on the faceted outer hull at (z, y) of one side (horizontal ray cast onto the hull triangles,
        /// split the same way as BuildOuter), lifted by 'lift' along the facet normal. False if nothing is hit.
        static bool FacetHit(int side, float z, float y, float lift, out Vector3 point, out Vector3 normal)
        {
            EnsureProfiles();
            Vector3 o = new Vector3(side * 4f, y, z), dir = new Vector3(-side, 0f, 0f);
            float best = float.MaxValue;
            point = Vector3.zero;
            normal = new Vector3(side, 0f, 0f);
            for (int i = 0; i < VisT.Length - 1; i++)
            {
                if (z < Z(VisT[i]) - 1e-4f || z > Z(VisT[i + 1]) + 1e-4f) continue;
                for (int j = 0; j < Prof - 1; j++)
                {
                    Vector3 a = P(side, i, j), b = P(side, i + 1, j), c = P(side, i + 1, j + 1), d = P(side, i, j + 1);
                    for (int k = 0; k < 2; k++)
                    {
                        Vector3 p1 = k == 0 ? b : c, p2 = k == 0 ? c : d;
                        if (!Ray(o, dir, a, p1, p2, out float t) || t >= best) continue;
                        Vector3 nn = Vector3.Cross(p1 - a, p2 - a);
                        if (nn.sqrMagnitude < 1e-12f) continue;
                        nn.Normalize();
                        if (nn.x * side < 0f) nn = -nn;
                        best = t;
                        normal = nn;
                    }
                }
            }
            if (best == float.MaxValue) return false;
            point = o + dir * best + normal * lift;
            return true;
        }

        /// Möller–Trumbore ray / triangle test (both faces).
        static bool Ray(Vector3 o, Vector3 d, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
            float det = Vector3.Dot(e1, p);
            if (Mathf.Abs(det) < 1e-9f) return false;
            float inv = 1f / det;
            Vector3 s = o - a;
            float u = Vector3.Dot(s, p) * inv;
            if (u < -1e-5f || u > 1f + 1e-5f) return false;
            Vector3 q = Vector3.Cross(s, e1);
            float v = Vector3.Dot(d, q) * inv;
            if (v < -1e-5f || u + v > 1f + 1e-5f) return false;
            t = Vector3.Dot(e2, q) * inv;
            return t > 0f;
        }

        // ------------------------------------------------------------------ meshes
        /// Outer shell: both sides, stem and transom, every band in its own flat color, plus a few rust streaks.
        public static void BuildOuter(BoatMeshSet set)
        {
            EnsureProfiles();
            int n = VisT.Length;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < n - 1; i++)
                    for (int j = 0; j < Prof - 1; j++)
                    {
                        Vector3 a = P(side, i, j), b = P(side, i + 1, j), c = P(side, i + 1, j + 1), d = P(side, i, j + 1);
                        Vector3 hint = N(side, i, j) + N(side, i + 1, j) + N(side, i + 1, j + 1) + N(side, i, j + 1);
                        var band = set[BandMat(j)];
                        band.TriOut(a, b, c, hint);
                        band.TriOut(a, c, d, hint);
                    }
            BuildRustStreaks(set[BoatMats.HullRust]);

            // stem: close the thin slit between both sides at the bow
            int last = n - 1;
            for (int j = 0; j < Prof - 1; j++)
                set[BandMat(j)].QuadOut(P(-1, last, j), P(-1, last, j + 1), P(1, last, j + 1), P(1, last, j), Vector3.forward);

            // transom: horizontal band strips between port and starboard
            for (int j = 0; j < Prof - 1; j++)
                set[BandMat(j)].QuadOut(P(-1, 0, j), P(1, 0, j), P(1, 0, j + 1), P(-1, 0, j + 1), Vector3.back);
        }

        // ------------------------------------------------------------------ rust streaks
        static readonly float[] StreakT = { 0.07f, 0.19f, 0.31f, 0.44f, 0.56f, 0.95f };

        /// Cartoon rust streaks running down from under the white strake: tapered flat strips laid onto the facets.
        static void BuildRustStreaks(BoatMeshBuilder b)
        {
            const int segs = 3;
            var l = new Vector3[segs + 1];
            var r = new Vector3[segs + 1];
            var nrm = new Vector3[segs + 1];
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < StreakT.Length; k++)
                {
                    float t = StreakT[k] + (side > 0 ? 0f : 0.03f);
                    float rnd = Mathf.Repeat(Mathf.Sin(k * 12.9898f + side * 78.233f) * 43758.5453f, 1f);
                    float z = Z(t);
                    float yTop = StrakeY(t) - 0.012f;
                    float len = Mathf.Min(0.2f + rnd * 0.3f, yTop - Mathf.Max(BootTopY, KeelY(t)) - 0.04f);
                    if (len < 0.08f) continue;
                    bool ok = true;
                    for (int s = 0; s <= segs && ok; s++)
                    {
                        float f = s / (float)segs;
                        float y = yTop - len * f;
                        float hw = Mathf.Lerp(0.04f, 0.012f, f);
                        float dz = Mathf.Sin(f * 3f + k) * 0.012f;
                        ok = FacetHit(side, z + dz - hw, y, 0.005f, out l[s], out nrm[s])
                          && FacetHit(side, z + dz + hw, y, 0.005f, out r[s], out _);
                    }
                    if (!ok) continue;
                    for (int s = 0; s < segs; s++)
                        b.QuadOut(l[s], r[s], r[s + 1], l[s + 1], nrm[s]);
                }
        }

        /// Visible deck half-width at station i (0 where the keel rises above the deck near the stem).
        static float DeckHalfVis(int i)
        {
            float t = VisT[i];
            return KeelY(t) >= DeckY - 0.02f ? 0f : Mathf.Max(0f, FacetX(i, DeckY) - WallThickness + 0.01f);
        }

        /// z of the deck tip between station i (with deck) and i + 1 (without): where the faceted keel line rises
        /// through the deck level, so the tapering deck never pokes out of the hull bottom.
        static float DeckTipZ(int i)
        {
            float y0 = prof[i, 0].y, y1 = prof[i + 1, 0].y;
            float f = y1 - y0 > 1e-4f ? Mathf.Clamp01((DeckY - 0.03f - y0) / (y1 - y0)) : 1f;
            return Mathf.Lerp(Z(VisT[i]), Z(VisT[i + 1]), f);
        }

        /// Deck outline: half-width and z per station, the first deck-less station moved back to the deck tip.
        static void DeckOutline(float[] half, float[] z)
        {
            int n = VisT.Length;
            for (int i = 0; i < n; i++) { half[i] = DeckHalfVis(i); z[i] = Z(VisT[i]); }
            for (int i = 0; i < n - 1; i++)
                if (half[i] > 0f && half[i + 1] <= 0f) z[i + 1] = DeckTipZ(i);
        }

        /// Inner bulwark faces (above the deck), the forepeak floor and the inner transom face.
        public static void BuildInner(BoatMeshBuilder b)
        {
            EnsureProfiles();
            int n = VisT.Length;
            var half = new float[n];
            var dz = new float[n];
            DeckOutline(half, dz);
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < n - 1; i++)
                {
                    for (int k = 0; k < 2; k++)
                    {
                        // two wall rows per station pair: deck..strake, strake..sheer
                        Vector3 a = InnerPoint(side, i, k), bb = InnerPoint(side, i + 1, k);
                        Vector3 c = InnerPoint(side, i + 1, k + 1), d = InnerPoint(side, i, k + 1);
                        b.QuadOut(a, bb, c, d, new Vector3(-side, 0f, 0f));
                    }
                    // forepeak: close the gap between the deck tip and the walls that start at the rising keel
                    if (!(half[i] > 0f && half[i + 1] <= 0f)) continue;
                    Vector3 e0 = new Vector3(side * half[i], DeckY, dz[i]), tip = new Vector3(0f, DeckY, dz[i + 1]);
                    Vector3 w0 = InnerPoint(side, i, 0), w1 = InnerPoint(side, i + 1, 0);
                    Vector3 up = new Vector3(-side * 0.5f, 1f, -0.3f);
                    b.TriOut(e0, tip, w1, up);
                    b.TriOut(e0, w1, w0, up);
                }
            // inner transom face
            float zt = Z(0f) + WallThickness;
            float xd = FacetX(0, DeckY) - WallThickness, xs = prof[0, Prof - 1].x - WallThickness, sy0 = SheerY(0f);
            b.QuadOut(new Vector3(-xd, DeckY, zt), new Vector3(xd, DeckY, zt), new Vector3(xs, sy0, zt), new Vector3(-xs, sy0, zt), Vector3.forward);
        }

        /// Inner wall point of station i: row 0 = deck (or keel near the stem), 1 = strake height, 2 = sheer.
        static Vector3 InnerPoint(int side, int i, int row)
        {
            float t = VisT[i];
            float y0 = Mathf.Max(DeckY, KeelY(t)), y2 = SheerY(t);
            float y = row == 0 ? y0 : row == 2 ? y2 : Mathf.Clamp(StrakeY(t), y0, y2);
            float x = Mathf.Max(0f, FacetX(i, y) - WallThickness);
            return new Vector3(side * x, y, Z(t));
        }

        /// Deck boards: planks along the boat in three wood tones over a dark sub-deck that shows through the seams.
        public static void BuildDeck(BoatMeshSet set)
        {
            EnsureProfiles();
            int n = VisT.Length;
            var half = new float[n];
            var dz = new float[n];
            DeckOutline(half, dz);

            // dark sub-deck (visible in the plank seams)
            var sub = set[BoatMats.DeckDark];
            for (int i = 0; i < n - 1; i++)
            {
                float z0 = dz[i], z1 = dz[i + 1];
                const float y = DeckY - 0.006f;
                sub.QuadOut(new Vector3(-half[i], y, z0), new Vector3(half[i], y, z0), new Vector3(half[i + 1], y, z1), new Vector3(-half[i + 1], y, z1), Vector3.up);
            }

            const int planks = 10;
            const float pw = 0.29f, gap = 0.016f;
            int[] tone = { 0, 1, 2, 1, 0, 2, 1, 0, 2, 1 };
            for (int p = 0; p < planks; p++)
            {
                float xa = -planks * pw * 0.5f + p * pw + gap * 0.5f, xb = xa + pw - gap;
                Material m = tone[p] == 0 ? BoatMats.Deck : tone[p] == 1 ? BoatMats.DeckB : BoatMats.DeckC;
                var bld = set[m];
                for (int i = 0; i < n - 1; i++)
                {
                    float l0 = Mathf.Clamp(xa, -half[i], half[i]), r0 = Mathf.Clamp(xb, -half[i], half[i]);
                    float l1 = Mathf.Clamp(xa, -half[i + 1], half[i + 1]), r1 = Mathf.Clamp(xb, -half[i + 1], half[i + 1]);
                    if (r0 - l0 + r1 - l1 < 1e-4f) continue;
                    float z0 = dz[i], z1 = dz[i + 1];
                    bld.QuadOut(new Vector3(l0, DeckY, z0), new Vector3(r0, DeckY, z0), new Vector3(r1, DeckY, z1), new Vector3(l1, DeckY, z1), Vector3.up);
                }
            }
        }

        /// Wooden cap rail on top of the bulwark (both sides + transom).
        public static void BuildGunwale(BoatMeshBuilder b)
        {
            EnsureProfiles();
            int n = VisT.Length;
            for (int side = -1; side <= 1; side += 2)
                for (int i = 0; i < n - 1; i++)
                {
                    GunwaleSection(side, i, out var oa0, out var ob0, out var ia0, out var ib0);
                    GunwaleSection(side, i + 1, out var oa1, out var ob1, out var ia1, out var ib1);
                    b.QuadOut(oa0, oa1, ob1, ob0, new Vector3(side, 0f, 0f));        // outer face
                    b.QuadOut(ob0, ob1, ib1, ib0, Vector3.up);                        // top
                    b.QuadOut(ib0, ib1, ia1, ia0, new Vector3(-side, 0f, 0f));       // inner face
                    b.QuadOut(oa0, oa1, ia1, ia0, Vector3.down);                      // underside (seen from the water)
                    if (i == 0) b.QuadOut(oa0, ob0, ib0, ia0, Vector3.back);           // stern end cap
                    if (i == n - 2) b.QuadOut(oa1, ob1, ib1, ia1, Vector3.forward);    // bow end cap
                }
            float sy0 = SheerY(0f), w0 = prof[0, Prof - 1].x;
            b.Box(new Vector3(0f, sy0 - 0.0075f, Z(0f) + 0.03f), new Vector3(2f * (w0 + 0.03f), 0.085f, 0.12f));
        }

        static void GunwaleSection(int side, int i, out Vector3 outerLow, out Vector3 outerTop, out Vector3 innerLow, out Vector3 innerTop)
        {
            float z = Z(VisT[i]), w = prof[i, Prof - 1].x, sy = prof[i, Prof - 1].y;
            float xo = w + 0.03f, xi = Mathf.Max(0f, w - 0.09f);
            outerLow = new Vector3(side * xo, sy - 0.05f, z);
            outerTop = new Vector3(side * xo, sy + 0.035f, z);
            innerLow = new Vector3(side * xi, sy - 0.05f, z);
            innerTop = new Vector3(side * xi, sy + 0.035f, z);
        }

        // ------------------------------------------------------------------ painted name "ROSTLAUBE" (blocky letters)
        static readonly string[] GlyphKeys = { "R", "O", "S", "T", "L", "A", "U", "B", "E" };
        static readonly string[][] GlyphRows =
        {
            new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" },
            new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" },
            new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" },
            new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" },
            new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" },
            new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" },
            new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" },
            new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" },
            new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" },
        };

        static string[] Glyph(char ch)
        {
            for (int i = 0; i < GlyphKeys.Length; i++) if (GlyphKeys[i][0] == ch) return GlyphRows[i];
            return null;
        }

        /// Blocky white boat name near the bow on both sides, laid onto the faceted topsides (reads left to right from outside).
        public static void BuildName(BoatMeshBuilder b, string text)
        {
            EnsureProfiles();
            const float cell = 0.03f, yTop = 0.62f, lift = 0.006f;
            float span = (text.Length * 6 - 1) * cell;
            float zStart = Z(0.655f);
            for (int side = -1; side <= 1; side += 2)
            {
                // starboard reads stern -> bow (+z), port reads bow -> stern (-z)
                float z0 = side > 0 ? zStart : zStart + span;
                float dir = side > 0 ? 1f : -1f;
                for (int c = 0; c < text.Length; c++)
                {
                    var g = Glyph(text[c]);
                    if (g == null) continue;
                    for (int gy = 0; gy < 7; gy++)
                        for (int gx = 0; gx < 5; gx++)
                        {
                            if (g[gy][gx] != '1') continue;
                            float za = z0 + dir * (c * 6 + gx) * cell, zb = za + dir * cell;
                            float ya = yTop - (gy + 1) * cell, yb = yTop - gy * cell;
                            if (!FacetHit(side, za, ya, lift, out var p0, out var n0)) continue;
                            if (!FacetHit(side, zb, ya, lift, out var p1, out _)) continue;
                            if (!FacetHit(side, zb, yb, lift, out var p2, out _)) continue;
                            if (!FacetHit(side, za, yb, lift, out var p3, out _)) continue;
                            b.QuadOut(p0, p1, p2, p3, n0);
                        }
                }
            }
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
                        b.V(new Vector3(side * q.x, q.y, z), new Vector3(side, -0.3f, 0f));
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
