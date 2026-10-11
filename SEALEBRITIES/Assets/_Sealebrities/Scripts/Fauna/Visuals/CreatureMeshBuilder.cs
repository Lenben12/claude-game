using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Material slots of a creature mesh (one sub-mesh per used slot).
    internal static class CreatureMeshSlot
    {
        public const int Skin = 0;   // body: small point-filtered color-block texture
        public const int Fin = 1;    // fins / arms: color-block atlas (4 bands)
        public const int Eye = 2;    // sclera (solid color per eye style)
        public const int Mouth = 3;  // dark mouth interior, nostrils, gill holes
        public const int Teeth = 4;  // white: teeth, whiskers, eye highlights
        public const int Glow = 5;   // emissive lure / photophores
        public const int Shell = 6;  // turtle carapace plates (palette texture), jelly gonads
        public const int Extra = 7;  // jelly tentacles / oral arms
        public const int Pupil = 8;  // black pupils
        public const int Count = 9;
    }

    /// Skin weight callback: vertex position, anchor (ring center or fin base) and a curve parameter.
    internal delegate BoneWeight CreatureMeshSkinFn(Vector3 pos, Vector3 anchor, float t);

    /// Managed geometry accumulator (vertices, uvs, bone weights, triangles per material slot).
    internal sealed class CreatureMeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>(2048);
        public readonly List<Vector2> Uvs = new List<Vector2>(2048);
        public readonly List<BoneWeight> Weights = new List<BoneWeight>(2048);
        public readonly List<int>[] Tris = new List<int>[CreatureMeshSlot.Count];

        public CreatureMeshBuilder()
        {
            for (int i = 0; i < Tris.Length; i++) Tris[i] = new List<int>(512);
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

    /// Faceted (flat shaded) version of a builder: every triangle owns its three vertices and the face normal.
    /// Degenerate triangles (collapsed poles, zero-size caps) are dropped, invalid positions are zeroed and bone
    /// weights are sorted / normalized for Unity. Slots flagged in facetUv get the triangle's centroid uv on all
    /// three corners, so a point-filtered block texture paints each facet in one flat color (low-poly mottling).
    /// Pure managed code: runs on the background bake thread.
    internal sealed class CreatureMeshFlat
    {
        public Vector3[] Verts;
        public Vector2[] Uvs;
        public Vector3[] Normals;
        public BoneWeight[] Weights;
        public readonly int[][] Tris = new int[CreatureMeshSlot.Count][];

        public int Count => Verts != null ? Verts.Length : 0;
        public int TriangleCount { get { int n = 0; for (int i = 0; i < Tris.Length; i++) n += Tris[i].Length / 3; return n; } }

        public static CreatureMeshFlat From(CreatureMeshBuilder b, bool[] facetUv) => From(b, facetUv, null, 50f);

        /// smooth[slot] 0 = faceted (face normal), 1 = smooth (area-weighted average of the faces that share the corner's
        /// position within creaseDeg), values in between soften the facets. Thin double-faced parts (fins) keep their
        /// two sides apart because opposite faces are beyond the crease angle.
        public static CreatureMeshFlat From(CreatureMeshBuilder b, bool[] facetUv, float[] smooth, float creaseDeg)
        {
            var f = Flatten(b, facetUv);
            if (smooth != null) f.SmoothNormals(smooth, creaseDeg);
            return f;
        }

        /// Replaces the face normals of the smooth slots (see From). Pure managed code (bake thread).
        void SmoothNormals(float[] smooth, float creaseDeg)
        {
            float cosCrease = (float)Math.Cos(creaseDeg * Math.PI / 180.0);
            const float Q = 4096f;   // position quantization (0.25 mm in model units)
            for (int s = 0; s < CreatureMeshSlot.Count; s++)
            {
                float k = s < smooth.Length ? CreatureMeshMath.Clamp01(smooth[s]) : 0f;
                if (k <= 0f) continue;
                var idx = Tris[s];
                if (idx == null || idx.Length == 0) continue;
                // area-weighted face normals per position bucket
                var buckets = new Dictionary<long, List<int>>(idx.Length);
                for (int t = 0; t < idx.Length; t++)
                {
                    long key = Key(Verts[idx[t]], Q);
                    if (!buckets.TryGetValue(key, out var l)) { l = new List<int>(6); buckets[key] = l; }
                    l.Add(t);
                }
                var faceN = new Vector3[idx.Length / 3];
                var faceA = new Vector3[idx.Length / 3];
                for (int t = 0; t < faceN.Length; t++)
                {
                    Vector3 pa = Verts[idx[t * 3]], pb = Verts[idx[t * 3 + 1]], pc = Verts[idx[t * 3 + 2]];
                    Vector3 cr = Vector3.Cross(pb - pa, pc - pa);
                    faceA[t] = cr;
                    faceN[t] = CreatureMeshMath.Norm(cr);
                }
                var outN = new Vector3[idx.Length];
                for (int t = 0; t < idx.Length; t++)
                {
                    Vector3 own = faceN[t / 3];
                    Vector3 sum = Vector3.zero;
                    var l = buckets[Key(Verts[idx[t]], Q)];
                    for (int j = 0; j < l.Count; j++)
                    {
                        int f2 = l[j] / 3;
                        if (Vector3.Dot(faceN[f2], own) < cosCrease) continue;
                        sum += faceA[f2];
                    }
                    Vector3 sm = sum.sqrMagnitude > 1e-20f ? CreatureMeshMath.Norm(sum) : own;
                    outN[t] = k >= 0.999f ? sm : CreatureMeshMath.Norm(Vector3.Lerp(own, sm, k));
                    if (outN[t].sqrMagnitude < 0.5f) outN[t] = own.sqrMagnitude > 0.5f ? own : Vector3.up;
                }
                for (int t = 0; t < idx.Length; t++) Normals[idx[t]] = outN[t];
            }
        }

        static long Key(Vector3 p, float q)
        {
            long x = (long)Math.Round(p.x * q), y = (long)Math.Round(p.y * q), z = (long)Math.Round(p.z * q);
            return (x & 0x1FFFFF) | ((y & 0x1FFFFF) << 21) | ((z & 0x1FFFFF) << 42);
        }

        static CreatureMeshFlat Flatten(CreatureMeshBuilder b, bool[] facetUv)
        {
            var src = b.Verts;
            var keep = new List<int>[CreatureMeshSlot.Count];
            int total = 0;
            for (int s = 0; s < CreatureMeshSlot.Count; s++)
            {
                var l = b.Tris[s];
                var k = new List<int>(l.Count / 3);
                for (int t = 0; t + 2 < l.Count; t += 3)
                {
                    Vector3 pa = Valid(src[l[t]]), pb = Valid(src[l[t + 1]]), pc = Valid(src[l[t + 2]]);
                    if (Vector3.Cross(pb - pa, pc - pa).sqrMagnitude < 1e-16f) continue;
                    k.Add(t);
                }
                keep[s] = k;
                total += k.Count * 3;
            }

            var f = new CreatureMeshFlat
            {
                Verts = new Vector3[total],
                Uvs = new Vector2[total],
                Normals = new Vector3[total],
                Weights = new BoneWeight[total],
            };
            int o = 0;
            for (int s = 0; s < CreatureMeshSlot.Count; s++)
            {
                var l = b.Tris[s];
                var k = keep[s];
                var idx = new int[k.Count * 3];
                bool facet = facetUv != null && s < facetUv.Length && facetUv[s];
                for (int q = 0; q < k.Count; q++)
                {
                    int t = k[q];
                    int ia = l[t], ib = l[t + 1], ic = l[t + 2];
                    Vector3 pa = Valid(src[ia]), pb = Valid(src[ib]), pc = Valid(src[ic]);
                    Vector3 n = CreatureMeshMath.Norm(Vector3.Cross(pb - pa, pc - pa));
                    Vector2 ua = b.Uvs[ia], ub = b.Uvs[ib], uc = b.Uvs[ic];
                    if (facet) { Vector2 m = (ua + ub + uc) / 3f; ua = ub = uc = m; }
                    f.Verts[o] = pa; f.Uvs[o] = ua; f.Normals[o] = n; f.Weights[o] = Sorted(b.Weights[ia]); idx[q * 3] = o; o++;
                    f.Verts[o] = pb; f.Uvs[o] = ub; f.Normals[o] = n; f.Weights[o] = Sorted(b.Weights[ib]); idx[q * 3 + 1] = o; o++;
                    f.Verts[o] = pc; f.Uvs[o] = uc; f.Normals[o] = n; f.Weights[o] = Sorted(b.Weights[ic]); idx[q * 3 + 2] = o; o++;
                }
                f.Tris[s] = idx;
            }
            return f;
        }

        static bool Finite(float x) => !(float.IsNaN(x) || float.IsInfinity(x));

        static Vector3 Valid(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) ? p : Vector3.zero;

        /// Unity expects weights in descending order (lower skin quality settings use the first entries).
        static BoneWeight Sorted(BoneWeight w)
        {
            int i0 = w.boneIndex0, i1 = w.boneIndex1, i2 = w.boneIndex2, i3 = w.boneIndex3;
            float w0 = Finite(w.weight0) ? Math.Max(0f, w.weight0) : 0f, w1 = Finite(w.weight1) ? Math.Max(0f, w.weight1) : 0f;
            float w2 = Finite(w.weight2) ? Math.Max(0f, w.weight2) : 0f, w3 = Finite(w.weight3) ? Math.Max(0f, w.weight3) : 0f;
            // tiny sorting network (descending)
            Swap(ref w0, ref i0, ref w1, ref i1); Swap(ref w2, ref i2, ref w3, ref i3);
            Swap(ref w0, ref i0, ref w2, ref i2); Swap(ref w1, ref i1, ref w3, ref i3);
            Swap(ref w1, ref i1, ref w2, ref i2);
            float sum = w0 + w1 + w2 + w3;
            if (sum <= 1e-6f) { w0 = 1f; w1 = w2 = w3 = 0f; sum = 1f; }
            return new BoneWeight
            {
                boneIndex0 = i0, weight0 = w0 / sum,
                boneIndex1 = w1 > 0f ? i1 : 0, weight1 = w1 / sum,
                boneIndex2 = w2 > 0f ? i2 : 0, weight2 = w2 / sum,
                boneIndex3 = w3 > 0f ? i3 : 0, weight3 = w3 / sum,
            };
        }

        static void Swap(ref float wa, ref int ia, ref float wb, ref int ib)
        {
            if (wb <= wa) return;
            float tw = wa; wa = wb; wb = tw;
            int ti = ia; ia = ib; ib = ti;
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
