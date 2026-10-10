using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Material slots of a creature mesh (one sub-mesh per used slot).
    internal static class CreatureMeshSlot
    {
        public const int Skin = 0;   // painted body texture + normal map
        public const int Fin = 1;    // painted fin texture (4 bands) + normal map; arms of cephalopods
        public const int Eye = 2;    // shared eye texture per eye style
        public const int Mouth = 3;  // dark mouth interior, nostrils, gill holes
        public const int Teeth = 4;  // ivory teeth, whiskers
        public const int Glow = 5;   // emissive lure / photophores
        public const int Shell = 6;  // turtle carapace, jelly gonads
        public const int Extra = 7;  // jelly tentacles / oral arms
        public const int Count = 8;
    }

    /// Skin weight callback: vertex position, anchor (ring center or fin base) and a curve parameter.
    internal delegate BoneWeight CreatureMeshSkinFn(Vector3 pos, Vector3 anchor, float t);

    /// Managed geometry accumulator (vertices, uvs, bone weights, triangles per material slot).
    internal sealed class CreatureMeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>(4096);
        public readonly List<Vector2> Uvs = new List<Vector2>(4096);
        public readonly List<BoneWeight> Weights = new List<BoneWeight>(4096);
        public readonly List<int>[] Tris = new List<int>[CreatureMeshSlot.Count];

        public CreatureMeshBuilder()
        {
            for (int i = 0; i < Tris.Length; i++) Tris[i] = new List<int>(1024);
        }

        public int Count => Verts.Count;

        public int Add(Vector3 p, Vector2 uv, BoneWeight w)
        {
            Verts.Add(p);
            Uvs.Add(uv);
            Weights.Add(w);
            return Verts.Count - 1;
        }

        public void Tri(int slot, int a, int b, int c)
        {
            var l = Tris[slot];
            l.Add(a); l.Add(b); l.Add(c);
        }

        /// Quad a-b-c-d (consecutive corners). Front face normal = cross(b - a, c - a) unless flipped.
        public void Quad(int slot, int a, int b, int c, int d, bool flip)
        {
            if (!flip) { Tri(slot, a, b, c); Tri(slot, a, c, d); }
            else { Tri(slot, a, c, b); Tri(slot, a, d, c); }
        }

        public Vector3 Cross(int a, int b, int c) => Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);

        /// Applies f to the positions of vertices [from, to).
        public void TransformRange(int from, int to, Func<Vector3, Vector3> f)
        {
            for (int i = from; i < to; i++) Verts[i] = f(Verts[i]);
        }

        /// Mirrors vertices [from, to) across the YZ plane into new vertices (with flipped winding) and
        /// remaps their bone weights through boneMirror (bone index -> mirrored bone index). Triangles
        /// (added after triFromPerSlot, see TriCounts) that reference only that range are duplicated.
        public void MirrorRange(int from, int to, int[] triFromPerSlot, Func<int, int> boneMirror)
        {
            int offset = Verts.Count - from;
            for (int i = from; i < to; i++)
            {
                Vector3 p = Verts[i];
                Add(new Vector3(-p.x, p.y, p.z), Uvs[i], MapBones(Weights[i], boneMirror));
            }
            for (int s = 0; s < Tris.Length; s++)
            {
                var l = Tris[s];
                int start = triFromPerSlot != null ? triFromPerSlot[s] : 0;
                int end = l.Count;
                start -= start % 3;
                for (int k = start; k + 2 < end; k += 3)
                {
                    int a = l[k], b = l[k + 1], c = l[k + 2];
                    if (a < from || a >= to || b < from || b >= to || c < from || c >= to) continue;
                    l.Add(a + offset); l.Add(c + offset); l.Add(b + offset);
                }
            }
        }

        public int[] TriCounts()
        {
            var r = new int[Tris.Length];
            for (int i = 0; i < Tris.Length; i++) r[i] = Tris[i].Count;
            return r;
        }

        static BoneWeight MapBones(BoneWeight w, Func<int, int> map)
        {
            if (map == null) return w;
            w.boneIndex0 = map(w.boneIndex0);
            w.boneIndex1 = map(w.boneIndex1);
            w.boneIndex2 = map(w.boneIndex2);
            w.boneIndex3 = map(w.boneIndex3);
            return w;
        }

        /// Area-weighted vertex normals; vertices sharing a position (uv seams, caps) are smoothed together
        /// when their normals point roughly the same way (keeps two-sided fins and creases intact).
        public Vector3[] ComputeNormals()
        {
            int n = Verts.Count;
            var acc = new Vector3[n];
            for (int s = 0; s < Tris.Length; s++)
            {
                var l = Tris[s];
                for (int k = 0; k + 2 < l.Count; k += 3)
                {
                    int a = l[k], b = l[k + 1], c = l[k + 2];
                    Vector3 fn = Vector3.Cross(Verts[b] - Verts[a], Verts[c] - Verts[a]);
                    acc[a] += fn; acc[b] += fn; acc[c] += fn;
                }
            }
            var unit = new Vector3[n];
            for (int i = 0; i < n; i++) unit[i] = CreatureMeshMath.Norm(acc[i]);

            var groups = new Dictionary<long, List<int>>(n);
            for (int i = 0; i < n; i++)
            {
                long key = PosKey(Verts[i]);
                if (!groups.TryGetValue(key, out var g)) { g = new List<int>(2); groups[key] = g; }
                g.Add(i);
            }
            var result = new Vector3[n];
            for (int i = 0; i < n; i++) result[i] = unit[i];
            foreach (var kv in groups)
            {
                var g = kv.Value;
                if (g.Count < 2) continue;
                for (int x = 0; x < g.Count; x++)
                {
                    int i = g[x];
                    Vector3 sum = acc[i];
                    for (int y = 0; y < g.Count; y++)
                    {
                        if (y == x) continue;
                        int j = g[y];
                        if (Vector3.Dot(unit[i], unit[j]) > 0.35f) sum += acc[j];
                    }
                    result[i] = CreatureMeshMath.Norm(sum);
                }
            }
            for (int i = 0; i < n; i++) if (result[i].sqrMagnitude < 0.5f) result[i] = Vector3.up;
            return result;
        }

        static long PosKey(Vector3 p)
        {
            long x = (long)Math.Round(p.x * 20000.0), y = (long)Math.Round(p.y * 20000.0), z = (long)Math.Round(p.z * 20000.0);
            unchecked { return (x * 73856093L) ^ (y * 19349663L) ^ (z * 83492791L); }
        }

        public Bounds ComputeBounds()
        {
            if (Verts.Count == 0) return new Bounds(Vector3.zero, Vector3.one);
            Vector3 mn = Verts[0], mx = Verts[0];
            for (int i = 1; i < Verts.Count; i++)
            {
                Vector3 p = Verts[i];
                if (p.x < mn.x) mn.x = p.x; if (p.y < mn.y) mn.y = p.y; if (p.z < mn.z) mn.z = p.z;
                if (p.x > mx.x) mx.x = p.x; if (p.y > mx.y) mx.y = p.y; if (p.z > mx.z) mx.z = p.z;
            }
            return new Bounds((mn + mx) * 0.5f, mx - mn);
        }
    }

    /// Bone weight helpers.
    internal static class CreatureMeshSkin
    {
        public static BoneWeight One(int bone)
        {
            return new BoneWeight { boneIndex0 = bone, weight0 = 1f };
        }

        public static BoneWeight Two(int a, int b, float tb)
        {
            if (a == b || tb <= 0f) return One(a);
            if (tb >= 1f) return One(b);
            return new BoneWeight { boneIndex0 = a, weight0 = 1f - tb, boneIndex1 = b, weight1 = tb };
        }

        public static CreatureMeshSkinFn Fixed(int bone)
        {
            var w = One(bone);
            return (p, a, t) => w;
        }

        /// Linear blend of two weight sets (keeps the 4 strongest influences, renormalized).
        public static BoneWeight Blend(BoneWeight x, BoneWeight y, float t)
        {
            if (t <= 0f) return x;
            if (t >= 1f) return y;
            var idx = new int[8];
            var wt = new float[8];
            int n = 0;
            void Put(int b, float w)
            {
                if (w <= 1e-5f) return;
                for (int i = 0; i < n; i++) if (idx[i] == b) { wt[i] += w; return; }
                idx[n] = b; wt[n] = w; n++;
            }
            float s = 1f - t;
            Put(x.boneIndex0, x.weight0 * s); Put(x.boneIndex1, x.weight1 * s); Put(x.boneIndex2, x.weight2 * s); Put(x.boneIndex3, x.weight3 * s);
            Put(y.boneIndex0, y.weight0 * t); Put(y.boneIndex1, y.weight1 * t); Put(y.boneIndex2, y.weight2 * t); Put(y.boneIndex3, y.weight3 * t);
            // selection sort descending
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    if (wt[j] > wt[i]) { float tw = wt[i]; wt[i] = wt[j]; wt[j] = tw; int ti = idx[i]; idx[i] = idx[j]; idx[j] = ti; }
            int m = n < 4 ? n : 4;
            float sum = 0f;
            for (int i = 0; i < m; i++) sum += wt[i];
            if (sum <= 0f) return x;
            var r = new BoneWeight();
            if (m > 0) { r.boneIndex0 = idx[0]; r.weight0 = wt[0] / sum; }
            if (m > 1) { r.boneIndex1 = idx[1]; r.weight1 = wt[1] / sum; }
            if (m > 2) { r.boneIndex2 = idx[2]; r.weight2 = wt[2] / sum; }
            if (m > 3) { r.boneIndex3 = idx[3]; r.weight3 = wt[3] / sum; }
            return r;
        }
    }

    /// A chain of bones along a polyline (pivot of each bone + end point). Weights blend smoothly
    /// across each joint so a bent chain deforms without pinching.
    internal sealed class CreatureMeshChain
    {
        public readonly int[] Bones;
        public readonly Vector3[] Pts;   // Bones.Length + 1 points (last = end of the chain)

        public CreatureMeshChain(int[] bones, Vector3[] pivots, Vector3 end)
        {
            Bones = bones;
            Pts = new Vector3[bones.Length + 1];
            for (int i = 0; i < bones.Length; i++) Pts[i] = pivots[i];
            Pts[bones.Length] = end;
        }

        /// Chain parameter in bone units (0 = first pivot, n = end).
        public float Param(Vector3 p)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int k = 0; k < Bones.Length; k++)
            {
                Vector3 a = Pts[k], ab = Pts[k + 1] - a;
                float l2 = ab.sqrMagnitude;
                float f = l2 > 1e-12f ? CreatureMeshMath.Clamp01(Vector3.Dot(p - a, ab) / l2) : 0f;
                float d = (a + ab * f - p).sqrMagnitude;
                if (d < best - 1e-10f) { best = d; bestS = k + f; }
            }
            return bestS;
        }

        public BoneWeight FromParam(float s)
        {
            int n = Bones.Length;
            if (n == 1 || s <= 0.5f) return CreatureMeshSkin.One(Bones[0]);
            if (s >= n - 0.5f) return CreatureMeshSkin.One(Bones[n - 1]);
            float x = s - 0.5f;
            int j = (int)Math.Floor(x);
            float f = x - j;
            f = f * f * (3f - 2f * f);
            return CreatureMeshSkin.Two(Bones[j], Bones[j + 1], f);
        }

        public BoneWeight Weigh(Vector3 p) => FromParam(Param(p));

        /// Skin callback weighting by the anchor point (ring center / fin base) instead of the vertex.
        public CreatureMeshSkinFn ByAnchor => (p, a, t) => Weigh(a);

        public CreatureMeshSkinFn ByVertex => (p, a, t) => Weigh(p);
    }
}
