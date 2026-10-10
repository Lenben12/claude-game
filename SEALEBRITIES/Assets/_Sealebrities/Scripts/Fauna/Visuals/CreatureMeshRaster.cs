using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Tiny managed z-buffer rasterizer (orthographic, 2x supersampled). Used for the journal icon fallback
    /// when GPU rendering is unavailable. Output: straight-alpha RGBA, row 0 = bottom (Texture2D order).
    internal static class CreatureMeshRaster
    {
        /// Shading callback: slot, interpolated uv, unit normal (model space), position -> color with alpha.
        public delegate Color Shader(int slot, Vector2 uv, Vector3 normal, Vector3 pos);

        public static Color32[] Render(IList<Vector3> verts, IList<Vector2> uvs, Vector3[] normals, IList<int>[] tris, bool[] transparent,
                                       int size, Vector3 viewDir, Vector3 upDir, Shader shade, float margin = 0.07f)
        {
            int ss = 2;
            int W = size * ss;
            Vector3 fwd = CreatureMeshMath.Norm(viewDir);
            Vector3 right = CreatureMeshMath.Norm(Vector3.Cross(upDir, fwd));
            if (right.sqrMagnitude < 0.5f) right = CreatureMeshMath.Perp(fwd);
            Vector3 up = Vector3.Cross(fwd, right);

            int n = verts.Count;
            var sx = new float[n];
            var sy = new float[n];
            var sz = new float[n];
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var used = new bool[n];
            for (int s = 0; s < tris.Length; s++)
            {
                var l = tris[s];
                if (l == null) continue;
                for (int k = 0; k < l.Count; k++) used[l[k]] = true;
            }
            for (int i = 0; i < n; i++)
            {
                Vector3 p = verts[i];
                sx[i] = Vector3.Dot(p, right);
                sy[i] = Vector3.Dot(p, up);
                sz[i] = Vector3.Dot(p, fwd);
                if (!used[i]) continue;
                if (sx[i] < minX) minX = sx[i]; if (sx[i] > maxX) maxX = sx[i];
                if (sy[i] < minY) minY = sy[i]; if (sy[i] > maxY) maxY = sy[i];
            }
            if (minX > maxX) { minX = -0.5f; maxX = 0.5f; minY = -0.5f; maxY = 0.5f; }
            float ext = Math.Max(maxX - minX, maxY - minY);
            float scale = W * (1f - 2f * margin) / Math.Max(1e-5f, ext);
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                sx[i] = (sx[i] - cx) * scale + W * 0.5f;
                sy[i] = (sy[i] - cy) * scale + W * 0.5f;
            }

            var col = new Color[W * W];
            var depth = new float[W * W];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MaxValue;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int s = 0; s < tris.Length; s++)
                {
                    var l = tris[s];
                    if (l == null) continue;
                    bool tr = transparent != null && s < transparent.Length && transparent[s];
                    if ((pass == 0) == tr) continue;
                    for (int k = 0; k + 2 < l.Count; k += 3)
                    {
                        int a = l[k], b = l[k + 1], c = l[k + 2];
                        Vector3 fn = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                        if (Vector3.Dot(fn, fwd) >= 0f) continue;   // back face
                        Tri(a, b, c, s, tr, sx, sy, sz, verts, uvs, normals, col, depth, W, shade);
                    }
                }
            }

            // downsample with alpha-weighted colors
            var res = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = 0, g = 0, bb = 0, al = 0;
                    for (int dy = 0; dy < ss; dy++)
                        for (int dx = 0; dx < ss; dx++)
                        {
                            Color c = col[(y * ss + dy) * W + x * ss + dx];
                            r += c.r * c.a; g += c.g * c.a; bb += c.b * c.a; al += c.a;
                        }
                    float inv = al > 1e-5f ? 1f / al : 0f;
                    res[y * size + x] = new Color32(To8(r * inv), To8(g * inv), To8(bb * inv), To8(al / (ss * ss)));
                }
            return res;
        }

        static byte To8(float f) => (byte)(f <= 0f ? 0 : (f >= 1f ? 255 : (int)(f * 255f + 0.5f)));

        static void Tri(int a, int b, int c, int slot, bool transparent, float[] sx, float[] sy, float[] sz, IList<Vector3> verts, IList<Vector2> uvs,
                        Vector3[] normals, Color[] col, float[] depth, int W, Shader shade)
        {
            float x0 = sx[a], y0 = sy[a], x1 = sx[b], y1 = sy[b], x2 = sx[c], y2 = sy[c];
            float area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0);
            if (Math.Abs(area) < 1e-9f) return;
            int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
            int maxX = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
            int maxY = Math.Min(W - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));
            float inv = 1f / area;
            for (int y = minY; y <= maxY; y++)
            {
                float py = y + 0.5f;
                for (int x = minX; x <= maxX; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((x1 - px) * (y2 - py) - (x2 - px) * (y1 - py)) * inv;
                    float w1 = ((x2 - px) * (y0 - py) - (x0 - px) * (y2 - py)) * inv;
                    float w2 = 1f - w0 - w1;
                    if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                    float z = w0 * sz[a] + w1 * sz[b] + w2 * sz[c];
                    int idx = y * W + x;
                    if (z >= depth[idx]) continue;
                    Vector2 uv = uvs != null ? uvs[a] * w0 + uvs[b] * w1 + uvs[c] * w2 : Vector2.zero;
                    Vector3 nrm = normals != null ? CreatureMeshMath.Norm(normals[a] * w0 + normals[b] * w1 + normals[c] * w2) : Vector3.up;
                    Vector3 pos = verts[a] * w0 + verts[b] * w1 + verts[c] * w2;
                    Color s = shade(slot, uv, nrm, pos);
                    if (!transparent)
                    {
                        depth[idx] = z;
                        s.a = 1f;
                        col[idx] = s;
                    }
                    else
                    {
                        Color d = col[idx];
                        float al = CreatureMeshMath.Clamp01(s.a);
                        float outA = al + d.a * (1f - al);
                        if (outA <= 1e-5f) continue;
                        Color o = new Color((s.r * al + d.r * d.a * (1f - al)) / outA, (s.g * al + d.g * d.a * (1f - al)) / outA,
                                            (s.b * al + d.b * d.a * (1f - al)) / outA, outA);
                        col[idx] = o;
                    }
                }
            }
        }
    }
}
