using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Tiny 5x7 bitmap font rendered as smooth strokes (cells joined to set neighbours), for painted signs and hull names.
    public static class PropFont
    {
        static Dictionary<char, byte[]> glyphs;

        static void Ensure()
        {
            if (glyphs != null) return;
            glyphs = new Dictionary<char, byte[]>
            {
                ['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
                ['B'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E },
                ['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
                ['D'] = new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
                ['E'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F },
                ['F'] = new byte[] { 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10 },
                ['G'] = new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
                ['H'] = new byte[] { 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
                ['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
                ['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
                ['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
                ['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
                ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
                ['N'] = new byte[] { 0x11, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11 },
                ['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
                ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
                ['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
                ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
                ['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
                ['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
                ['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
                ['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
                ['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A },
                ['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
                ['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
                ['Z'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
                ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
                ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
                ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
                ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
                ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
                ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
                ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
                ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
                ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
                ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C },
                ['.'] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C },
                [','] = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x0C, 0x04, 0x08 },
                ['-'] = new byte[] { 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00 },
                ['!'] = new byte[] { 0x04, 0x04, 0x04, 0x04, 0x04, 0x00, 0x04 },
                ['?'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x00, 0x04 },
                ['&'] = new byte[] { 0x0C, 0x12, 0x14, 0x08, 0x15, 0x12, 0x0D },
                ['\''] = new byte[] { 0x04, 0x04, 0x08, 0x00, 0x00, 0x00, 0x00 },
                [':'] = new byte[] { 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00 },
                ['/'] = new byte[] { 0x01, 0x01, 0x02, 0x04, 0x08, 0x10, 0x10 },
                ['+'] = new byte[] { 0x00, 0x04, 0x04, 0x1F, 0x04, 0x04, 0x00 },
                ['$'] = new byte[] { 0x04, 0x0F, 0x14, 0x0E, 0x05, 0x1E, 0x04 },
                [' '] = new byte[] { 0, 0, 0, 0, 0, 0, 0 },
            };
        }

        public static byte[] Glyph(char c)
        {
            Ensure();
            return glyphs.TryGetValue(char.ToUpperInvariant(c), out var g) ? g : null;
        }

        public static bool Bit(char c, int col, int row) => B(Glyph(c), col, row);

        static bool B(byte[] g, int col, int row)
        {
            if (g == null || col < 0 || col > 4 || row < 0 || row > 6) return false;
            return ((g[row] >> (4 - col)) & 1) != 0;
        }

        static float SegDist(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Vector2.Dot(ab, ab));
            return Vector2.Distance(p, a + ab * t);
        }

        static readonly Vector2Int[] Links = { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(-1, 1) };

        /// Distance (in cell units) from glyph-space point (gx in [0,5), gy in [0,7) rows from the top) to the stroke skeleton.
        public static float Dist(char c, float gx, float gy)
        {
            byte[] g = Glyph(c);
            if (g == null) return 99f;
            int cx = Mathf.FloorToInt(gx), cy = Mathf.FloorToInt(gy);
            var p = new Vector2(gx, gy);
            float best = 99f;
            for (int dy = -2; dy <= 1; dy++)
            {
                for (int dx = -2; dx <= 1; dx++)
                {
                    int col = cx + dx, row = cy + dy;
                    if (!B(g, col, row)) continue;
                    var c0 = new Vector2(col + 0.5f, row + 0.5f);
                    best = Mathf.Min(best, Vector2.Distance(p, c0));
                    for (int l = 0; l < Links.Length; l++)
                    {
                        var o = Links[l];
                        if (!B(g, col + o.x, row + o.y)) continue;
                        // skip diagonal if an orthogonal path exists (keeps corners crisp)
                        if (o.x != 0 && o.y != 0 && (B(g, col + o.x, row) || B(g, col, row + o.y))) continue;
                        best = Mathf.Min(best, SegDist(p, c0, c0 + new Vector2(o.x, o.y)));
                    }
                }
            }
            return best;
        }

        /// Text width in cell units (5 per glyph + spacing).
        public static float Width(string text, float spacing = 1.3f) => text.Length * 5f + Mathf.Max(0, text.Length - 1) * spacing;

        /// Ink coverage 0..1 of a text line at text-space point (x in cell units from the left, y in cell units from the top).
        public static float Coverage(string text, float x, float y, float stroke, float aa, float spacing = 1.3f)
        {
            if (y < -1f || y > 8f || x < -1f) return 0f;
            float adv = 5f + spacing;
            int i = Mathf.FloorToInt(x / adv);
            float best = 99f;
            for (int k = i - 1; k <= i + 1; k++)
            {
                if (k < 0 || k >= text.Length) continue;
                char c = text[k];
                if (c == ' ') continue;
                best = Mathf.Min(best, Dist(c, x - k * adv, y));
            }
            return Mathf.Clamp01((stroke - best) / Mathf.Max(0.0001f, aa) + 0.5f);
        }
    }
}
