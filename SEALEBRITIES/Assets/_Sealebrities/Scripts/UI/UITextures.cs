using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Species icons for the journal/FinTube. Uses the Fauna module's CreatureIcons.Get(SpeciesDef) and falls back to a
    /// simple procedural silhouette if that ever throws. Silhouette() returns the flat grey (#8C96A3) "not found yet" shape.
    public static class UICreatureIcons
    {
        static Func<SpeciesDef, Texture2D> faunaIcons;
        static bool resolved;
        static readonly Dictionary<string, Texture2D> Fallback = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Texture2D> Silhouettes = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { faunaIcons = null; resolved = false; Fallback.Clear(); Silhouettes.Clear(); }

        public static Texture2D Get(SpeciesDef s)
        {
            if (s == null) return null;
            Resolve();
            if (faunaIcons != null)
            {
                try
                {
                    var t = faunaIcons(s);
                    if (t != null) return t;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Sealebrities UI: CreatureIcons.Get failed, using fallback icons. " + e.Message);
                    faunaIcons = null;
                }
            }
            if (Fallback.TryGetValue(s.id, out var cached) && cached != null) return cached;
            var tex = BuildFallback(s);
            Fallback[s.id] = tex;
            return tex;
        }

        /// Flat grey silhouette of the species icon (built once per species). Null when the icon is not readable.
        public static Texture2D Silhouette(SpeciesDef s)
        {
            if (s == null) return null;
            if (Silhouettes.TryGetValue(s.id, out var cached)) return cached;
            Texture2D result = null;
            var src = Get(s);
            if (src != null)
            {
                try
                {
                    var px = src.GetPixels32(0);
                    var sil = (Color32)UIPalette.Silhouette;
                    for (int i = 0; i < px.Length; i++)
                    {
                        byte a = px[i].a;
                        px[i] = new Color32(sil.r, sil.g, sil.b, a);
                    }
                    result = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false)
                    {
                        wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SB_UI_Sil_" + s.id
                    };
                    result.SetPixels32(px);
                    result.Apply(false, true);
                }
                catch (Exception)
                {
                    result = null;
                }
            }
            Silhouettes[s.id] = result;
            return result;
        }

        static void Resolve()
        {
            if (resolved) return;
            resolved = true;
            // Fauna module entry point (direct call: survives managed code stripping, unlike a reflection lookup)
            faunaIcons = CreatureIcons.Get;
        }

        // ------------------------------------------------------------------ fallback silhouettes
        const int N = 128;

        static Texture2D BuildFallback(SpeciesDef s)
        {
            var px = new Color32[N * N];
            Color a = s.colorA, b = s.colorB, c = s.colorC;
            switch (s.plan)
            {
                case BodyPlan.Jelly:
                case BodyPlan.VampireSquid:
                    DrawJelly(px, a, b); break;
                case BodyPlan.Ray:
                    DrawRay(px, a, b, c); break;
                case BodyPlan.Octopus:
                case BodyPlan.Squid:
                case BodyPlan.Cuttlefish:
                    DrawCephalopod(px, a, b, s.plan); break;
                case BodyPlan.Turtle:
                    DrawTurtle(px, a, b, c); break;
                case BodyPlan.Eel:
                    DrawFish(px, a, b, c, 0.14f, 0.12f, 0.35f); break;
                case BodyPlan.Seahorse:
                    DrawSeahorse(px, a, b); break;
                case BodyPlan.Sunfish:
                    DrawFish(px, a, b, c, 0.8f, 0.6f, 0.08f); break;
                default:
                    DrawFish(px, a, b, c, Mathf.Clamp(s.bodyHeight, 0.18f, 0.7f), 0.5f, 0.2f); break;
            }
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SB_UI_Icon_" + s.id };
            t.SetPixels32(px);
            t.Apply(false, false);
            return t;
        }

        static void Put(Color32[] px, int x, int y, Color col, float alpha)
        {
            if (x < 0 || y < 0 || x >= N || y >= N || alpha <= 0f) return;
            int i = y * N + x;
            Color dst = px[i];
            float outA = alpha + dst.a * (1f - alpha);
            if (outA <= 0f) return;
            Color o = (col * alpha + dst * dst.a * (1f - alpha)) / outA;
            o.a = outA;
            px[i] = o;
        }

        static float Edge(float d) => Mathf.Clamp01(0.5f - d);   // antialiasing for a signed distance in pixels

        static void Ellipse(Color32[] px, float cx, float cy, float rx, float ry, Color top, Color bottom)
        {
            int x0 = Mathf.FloorToInt(cx - rx - 2), x1 = Mathf.CeilToInt(cx + rx + 2);
            int y0 = Mathf.FloorToInt(cy - ry - 2), y1 = Mathf.CeilToInt(cy + ry + 2);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x - cx) / rx, dy = (y - cy) / ry;
                    float d = (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
                    float t = Mathf.InverseLerp(cy - ry, cy + ry, y);
                    Put(px, x, y, Color.Lerp(bottom, top, t), Edge(d));
                }
        }

        static void Triangle(Color32[] px, Vector2 a, Vector2 b, Vector2 c, Color col)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))), x1 = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))), y1 = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x, y);
                    float d1 = Cross(b - a, p - a), d2 = Cross(c - b, p - b), d3 = Cross(a - c, p - c);
                    bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                    if (!(neg && pos)) Put(px, x, y, col, 1f);
                }
        }

        static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        static void Line(Color32[] px, Vector2 a, Vector2 b, float width, Color col)
        {
            int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - width), x1 = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + width);
            int y0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - width), y1 = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + width);
            var ab = b - a;
            float len2 = Mathf.Max(0.0001f, ab.sqrMagnitude);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2(x, y);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
                    float d = Vector2.Distance(p, a + ab * t) - width * 0.5f;
                    Put(px, x, y, col, Edge(d));
                }
        }

        static void DrawFish(Color32[] px, Color a, Color b, Color c, float height, float tailSize, float finTop)
        {
            float cx = 58f, cy = 64f, rx = 44f, ry = Mathf.Clamp(rx * height * 1.25f, 8f, 46f);
            Triangle(px, new Vector2(cx + rx * 0.82f, cy), new Vector2(cx + rx + 22f, cy + 8f + ry * tailSize), new Vector2(cx + rx + 22f, cy - 8f - ry * tailSize), c);
            if (finTop > 0.01f)
                Triangle(px, new Vector2(cx - rx * 0.25f, cy + ry * 0.8f), new Vector2(cx + rx * 0.35f, cy + ry * 0.7f), new Vector2(cx + rx * 0.15f, cy + ry + rx * finTop), c);
            Ellipse(px, cx, cy, rx, ry, a, Color.Lerp(a, b, 0.7f));
            Ellipse(px, cx - rx * 0.62f, cy + ry * 0.2f, 4.5f, 4.5f, Color.white, Color.white);
            Ellipse(px, cx - rx * 0.64f, cy + ry * 0.2f, 2.5f, 2.5f, Color.black, Color.black);
        }

        static void DrawJelly(Color32[] px, Color a, Color b)
        {
            for (int i = 0; i < 7; i++)
            {
                float x = 34f + i * 10f;
                Line(px, new Vector2(x, 70f), new Vector2(x + Mathf.Sin(i * 1.7f) * 6f, 14f + (i % 3) * 6f), 3f, A(b, 0.8f));
            }
            Ellipse(px, 64f, 78f, 40f, 30f, A(a, 0.95f), A(Color.Lerp(a, b, 0.5f), 0.85f));
            Ellipse(px, 64f, 74f, 40f, 8f, A(b, 0.6f), A(b, 0.6f));
        }

        static void DrawRay(Color32[] px, Color a, Color b, Color c)
        {
            Line(px, new Vector2(64f, 54f), new Vector2(64f, 6f), 4f, c);
            Triangle(px, new Vector2(64f, 110f), new Vector2(6f, 64f), new Vector2(64f, 40f), a);
            Triangle(px, new Vector2(64f, 110f), new Vector2(122f, 64f), new Vector2(64f, 40f), a);
            Ellipse(px, 64f, 70f, 22f, 30f, Color.Lerp(a, b, 0.2f), a);
        }

        static void DrawCephalopod(Color32[] px, Color a, Color b, BodyPlan plan)
        {
            int arms = plan == BodyPlan.Octopus ? 6 : 4;
            for (int i = 0; i < arms; i++)
            {
                float x = 40f + i * (48f / Mathf.Max(1, arms - 1));
                Line(px, new Vector2(64f, 56f), new Vector2(x + Mathf.Sin(i * 2.1f) * 8f, 10f), 6f, Color.Lerp(a, b, 0.3f));
            }
            if (plan == BodyPlan.Squid) Ellipse(px, 64f, 82f, 18f, 36f, a, Color.Lerp(a, b, 0.5f));
            else Ellipse(px, 64f, 76f, 30f, 32f, a, Color.Lerp(a, b, 0.5f));
            Ellipse(px, 52f, 62f, 4f, 4f, Color.white, Color.white);
            Ellipse(px, 76f, 62f, 4f, 4f, Color.white, Color.white);
        }

        static void DrawTurtle(Color32[] px, Color a, Color b, Color c)
        {
            Line(px, new Vector2(40f, 78f), new Vector2(14f, 100f), 12f, c);
            Line(px, new Vector2(88f, 78f), new Vector2(114f, 100f), 12f, c);
            Line(px, new Vector2(44f, 46f), new Vector2(26f, 30f), 10f, c);
            Line(px, new Vector2(84f, 46f), new Vector2(102f, 30f), 10f, c);
            Ellipse(px, 64f, 110f, 12f, 10f, c, c);
            Ellipse(px, 64f, 64f, 34f, 40f, a, Color.Lerp(a, b, 0.4f));
        }

        static void DrawSeahorse(Color32[] px, Color a, Color b)
        {
            Ellipse(px, 66f, 70f, 16f, 30f, a, Color.Lerp(a, b, 0.4f));
            Ellipse(px, 72f, 104f, 14f, 10f, a, a);
            Line(px, new Vector2(80f, 104f), new Vector2(104f, 100f), 6f, a);
            Line(px, new Vector2(60f, 44f), new Vector2(56f, 22f), 8f, a);
            Line(px, new Vector2(56f, 22f), new Vector2(72f, 14f), 6f, a);
            Ellipse(px, 66f, 106f, 3f, 3f, Color.black, Color.black);
        }

        static Color A(Color c, float a) { c.a = a; return c; }
    }

    /// Top-down "drawn" sea chart of the whole play area (main island, landmark islands, dive sites),
    /// sampled from the terrain once (lazily) in the light How-to-Fish palette.
    public static class UIMapTexture
    {
        static Texture2D tex;
        static bool fromTerrain;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { tex = null; fromTerrain = false; }

        public const float WorldHalf = WorldLayout.TerrainSize * 0.5f;

        /// Returns the map texture; rebuilds once the terrain exists.
        public static Texture2D Get()
        {
            bool terrainReady = WorldInfo.HeightProvider != null || WorldInfo.Terrain != null;
            if (tex != null && (fromTerrain || !terrainReady)) return tex;
            tex = Build(terrainReady);
            fromTerrain = terrainReady;
            return tex;
        }

        static Texture2D Build(bool useTerrain)
        {
            const int n = 384;
            var h = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float wx = -WorldHalf + (x + 0.5f) / n * WorldLayout.TerrainSize;
                    float wz = -WorldHalf + (y + 0.5f) / n * WorldLayout.TerrainSize;
                    float v;
                    if (useTerrain)
                    {
                        try { v = WorldInfo.GroundHeight(new Vector3(wx, 0f, wz)) - Waves.SeaLevel; }
                        catch (Exception) { v = ApproxHeight(wx, wz); }
                    }
                    else v = ApproxHeight(wx, wz);
                    h[y * n + x] = v;
                }

            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = h[y * n + x];
                    Color c = HeightColor(v);
                    float hl = h[y * n + Mathf.Max(0, x - 1)], hr = h[y * n + Mathf.Min(n - 1, x + 1)];
                    float hd = h[Mathf.Max(0, y - 1) * n + x], hu = h[Mathf.Min(n - 1, y + 1) * n + x];
                    if (v > 0f)
                    {
                        // soft hill shading (light from the north-west)
                        float shade = Mathf.Clamp((hl - hr + hu - hd) * 0.05f, -0.22f, 0.22f);
                        c = shade > 0 ? Color.Lerp(c, Color.white, shade) : Color.Lerp(c, UIPalette.Rgb(0x3B3A4E), -shade);
                        // ink coastline
                        bool coast = hl <= 0f || hr <= 0f || hd <= 0f || hu <= 0f;
                        if (coast) c = Color.Lerp(c, UIPalette.Rgb(0x5E4436), 0.55f);
                    }
                    else
                    {
                        // white surf ring + depth contour lines every 10 m
                        bool nearCoast = hl > 0f || hr > 0f || hd > 0f || hu > 0f;
                        if (nearCoast) c = Color.Lerp(c, Color.white, 0.75f);
                        else if (v < -3f)
                        {
                            float m = Mathf.Repeat(-v, 10f);
                            if (m < 0.7f) c = Color.Lerp(c, Color.white, 0.12f);
                        }
                    }
                    // light paper grid every 128 m
                    float wx2 = -WorldHalf + (x + 0.5f) / n * WorldLayout.TerrainSize;
                    float wz2 = -WorldHalf + (y + 0.5f) / n * WorldLayout.TerrainSize;
                    if (Mathf.Repeat(wx2, 256f) < 5.3f || Mathf.Repeat(wz2, 256f) < 5.3f) c = Color.Lerp(c, Color.white, 0.10f);
                    c.a = 1f;
                    px[y * n + x] = c;
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SB_UI_Map" };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// Approximate island shape when no terrain exists (radius ~190 m with a wobbly coast + mini islands).
        static float ApproxHeight(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            float a = Mathf.Atan2(z, x);
            float coast = WorldLayout.IslandRadius * (1f + 0.12f * Mathf.Sin(a * 3f + 0.7f) + 0.07f * Mathf.Sin(a * 7f + 2.1f));
            float best;
            if (d < coast) best = Mathf.Lerp(WorldLayout.IslandPeak, 1f, Mathf.Pow(d / coast, 1.6f));
            else
            {
                float o = d - coast;
                best = -Mathf.Min(150f, 2f + o * 0.12f + o * o * 0.0002f);
            }
            best = Mathf.Max(best, Mini(x, z, WorldLayout.KelpIsland));
            best = Mathf.Max(best, Mini(x, z, WorldLayout.WreckIsland));
            best = Mathf.Max(best, Mini(x, z, WorldLayout.BlueDropIsland));
            best = Mathf.Max(best, Mini(x, z, WorldLayout.TrenchIsland));
            // round 3: the volcano island (bigger, a dark cone)
            {
                Vector3 v = WorldLayout.VolcanoIsland;
                float dv = Mathf.Sqrt((x - v.x) * (x - v.x) + (z - v.z) * (z - v.z));
                float rv = WorldLayout.VolcanoShoreRadius;
                best = Mathf.Max(best, dv < rv ? Mathf.Lerp(WorldLayout.VolcanoPeak, 1f, dv / rv) : -Mathf.Min(150f, (dv - rv) * 0.25f + 2f));
            }
            return best;
        }

        static float Mini(float x, float z, Vector3 c)
        {
            float d = Mathf.Sqrt((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z));
            float r = WorldLayout.MiniIslandRadius;
            return d < r ? Mathf.Lerp(12f, 1f, d / r) : -Mathf.Min(150f, (d - r) * 0.2f + 2f);
        }

        static readonly float[] Stops = { -150f, -60f, -25f, -10f, -3f, 0f, 1.4f, 5f, 18f, 45f };
        static readonly Color[] StopColors =
        {
            UIPalette.Rgb(0x284F80), UIPalette.Rgb(0x2E5F93), UIPalette.Rgb(0x3778AB), UIPalette.Rgb(0x4F94C2),
            UIPalette.Rgb(0x6FB8D2), UIPalette.Rgb(0x9ADBE0), UIPalette.Rgb(0xE6CFA0), UIPalette.Rgb(0x8DB15A),
            UIPalette.Rgb(0x6E9445), UIPalette.Rgb(0x8A8C96)
        };

        static Color HeightColor(float h)
        {
            if (h <= Stops[0]) return StopColors[0];
            for (int i = 1; i < Stops.Length; i++)
                if (h <= Stops[i]) return Color.Lerp(StopColors[i - 1], StopColors[i], Mathf.InverseLerp(Stops[i - 1], Stops[i], h));
            return StopColors[StopColors.Length - 1];
        }
    }
}
