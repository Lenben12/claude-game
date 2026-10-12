using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Giant kelp forest in the low-poly style: every stalk is a faceted, slowly twisting ribbon (flat triangles,
    /// two-sided) with a few side blades and a floating canopy, in two kelp greens. Stalks are bone chains grouped
    /// into skinned clusters (two submeshes = two draw calls per cluster); one PropKelpSway manager animates them.
    public static class PropKelp
    {
        public const int StalkTarget = 430;
        static float G(float x, float z) => PropGround.H(x, z);

        public static IEnumerator Build(Transform decor, DiveSite site)
        {
            var rng = new System.Random(8080);
            Vector3 c = site.center;
            var sway = decor.gameObject.AddComponent<PropKelpSway>();
            var batch = new PropBatch(64f);
            var cols = new GameObject("Colliders").transform;
            cols.SetParent(decor, false);

            // ---------------------------------------------------------------- stalk positions (patchy forest with clearings)
            var stalks = new List<Vector4>();
            var spacing = new PropExclusion(4f);
            int attempts = 0;
            while (stalks.Count < StalkTarget && attempts < 600)
            {
                attempts++;
                Vector2 pc = PropUtil.InDisc(rng, site.radius * 0.92f);
                float prad = PropUtil.Range(rng, 8f, 20f);
                int count = 12 + rng.Next(20);
                if (attempts == 1)
                {
                    // a dense stand around the site buoy, where players anchor and jump in (playtest: the centre was a
                    // bare clearing, so the "forest" looked empty on arrival)
                    pc = Vector2.zero;
                    prad = 22f;
                    count = 70;
                }
                for (int k = 0; k < count && stalks.Count < StalkTarget; k++)
                {
                    Vector2 o = PropUtil.InDisc(rng, prad);
                    float x = c.x + pc.x + o.x, z = c.z + pc.y + o.y;
                    float g = G(x, z);
                    float depth = -g;
                    if (depth < 9.4f || spacing.Blocked(x, z) || PropUnder.InKelpTunnel(x, z)) continue;
                    float h = Mathf.Min(PropUtil.Range(rng, 8f, 20f), depth - 1.3f);
                    if (h < 8f) continue;
                    stalks.Add(new Vector4(x, z, g, h));
                    spacing.Add(x, z, 1.15f);
                }
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- clusters (22 m grid)
            var clusters = new Dictionary<long, List<Vector4>>();
            foreach (var s in stalks)
            {
                long key = ((long)Mathf.FloorToInt(s.x / 22f) << 32) ^ (uint)Mathf.FloorToInt(s.y / 22f);
                if (!clusters.TryGetValue(key, out var list)) { list = new List<Vector4>(); clusters[key] = list; }
                list.Add(s);
            }
            float currentDir = PropUtil.Range(rng, 0f, 360f);
            Debug.Log("Sealebrities props: kelp forest with " + stalks.Count + " stalks in " + clusters.Count + " clusters (" + attempts + " patch attempts).");
            foreach (var kv in clusters)
            {
                try { BuildCluster(decor, sway, kv.Value, rng, currentDir); }
                catch (System.Exception e) { Debug.LogError("Sealebrities props: kelp cluster failed: " + e); }
                if (PropRun.Due()) yield return null;
            }

            // ---------------------------------------------------------------- boulders and rocky ground
            int boulders = 0;
            attempts = 0;
            while (boulders < 28 && attempts < 3000)
            {
                attempts++;
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                float g = G(x, z);
                if (-g < 8f) continue;
                float s = Mathf.Min(PropUtil.Range(rng, 1.2f, 4.5f), (-g - 3f) / 1.2f);
                if (s < 0.8f) continue;
                Vector3 sc = new Vector3(s * PropUtil.Range(rng, 1f, 1.5f), s * PropUtil.Range(rng, 0.6f, 1.1f), s);
                Vector3 p = new Vector3(x, g - sc.y * 0.3f, z);
                Quaternion r = Quaternion.Euler(PropUtil.Range(rng, -8f, 8f), PropUtil.Range(rng, 0f, 360f), 0f);
                var mesh = PropRocks.Mesh(sc, rng.Next(), 1, 0.16f);
                batch.Add(mesh, boulders % 3 == 0 ? PropMats.RockDark : PropMats.Rock, Matrix4x4.TRS(p, r, Vector3.one));
                // round 3 (GDD §19): every boulder is solid (was s > 1.6 only; they are all ≥ 0.8 m)
                PropCol.Mesh(cols, mesh, p, r, Vector3.one, "KelpBoulder");
                // a few cold-water sponges and anemones on the boulders
                int extras = rng.Next(4);
                for (int k = 0; k < extras; k++)
                {
                    Vector2 eo = PropUtil.InDisc(rng, sc.x * 0.6f);
                    Vector3 ep = p + new Vector3(eo.x, sc.y * 0.75f, eo.y);
                    PropReef.Place(batch, rng, ep, Vector3.up, rng.NextDouble() < 0.5 ? PropCoralType.Sponge : PropCoralType.Anemone, 0.8f, -3f, cols);
                }
                boulders++;
                if (PropRun.Due()) yield return null;
            }
            for (int i = 0; i < 90; i++)
            {
                Vector2 o = PropUtil.InDisc(rng, site.radius);
                float x = c.x + o.x, z = c.z + o.y;
                float g = G(x, z);
                if (-g < 5f) continue;
                float s = PropUtil.Range(rng, 0.25f, 0.9f);
                Vector3 p = new Vector3(x, g - s * 0.2f, z);
                Vector3 ssc = new Vector3(s * 1.3f, s * 0.7f, s);
                Quaternion sq = Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                int sseed = rng.Next();
                PropRocks.Build(batch.B(PropMats.Rock, p), p, ssc, sq, sseed, 0);
                // round 3: stones you would bump into (≥ 0.35) are solid – convex hull of the same seeded rock
                if (s >= 0.35f) PropSolid.Rock(cols, p, ssc, sq, sseed, 0, 0.14f, true, "KelpStone");
            }
            batch.Flush(decor, "KelpRocks", ShadowCastingMode.On, 170f);
        }

        // ------------------------------------------------------------------ flat, skinned cluster mesh
        sealed class FlatSkin
        {
            public readonly List<Vector3> V = new List<Vector3>(4096);
            public readonly List<Vector3> N = new List<Vector3>(4096);
            public readonly List<BoneWeight> W = new List<BoneWeight>(4096);
            public readonly List<int> T0 = new List<int>(4096);
            public readonly List<int> T1 = new List<int>(4096);

            static BoneWeight Bw(int bone) => new BoneWeight { boneIndex0 = bone, weight0 = 1f };

            /// Flat triangle (own vertices, face normal). Degenerate input is skipped.
            public void Tri(Vector3 a, Vector3 b, Vector3 c, int ba, int bb, int bc, bool sub1)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                float l = n.magnitude;
                if (!(l > 1e-9f) || float.IsInfinity(l)) return;
                n /= l;
                int k = V.Count;
                V.Add(a); V.Add(b); V.Add(c);
                N.Add(n); N.Add(n); N.Add(n);
                W.Add(Bw(ba)); W.Add(Bw(bb)); W.Add(Bw(bc));
                var t = sub1 ? T1 : T0;
                t.Add(k); t.Add(k + 1); t.Add(k + 2);
            }

            /// Two-sided flat triangle.
            public void Tri2(Vector3 a, Vector3 b, Vector3 c, int ba, int bb, int bc, bool sub1)
            {
                Tri(a, b, c, ba, bb, bc, sub1);
                Tri(a, c, b, ba, bc, bb, sub1);
            }
        }

        static void BuildCluster(Transform parent, PropKelpSway sway, List<Vector4> stalks, System.Random rng, float currentDir)
        {
            Vector3 center = Vector3.zero;
            float minG = float.MaxValue, maxH = 0f;
            foreach (var s in stalks) { center += new Vector3(s.x, 0f, s.y); minG = Mathf.Min(minG, s.z); maxH = Mathf.Max(maxH, s.w); }
            center /= stalks.Count;
            center.y = minG;

            var go = new GameObject("KelpCluster");
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.rotation = Quaternion.identity;

            var bones = new List<Transform>();
            var phases = new List<float>();
            var amps = new List<float>();
            var mesh = new FlatSkin();
            Quaternion current = Quaternion.Euler(0f, currentDir, 0f);
            Vector3 leanAxis = current * Vector3.right;

            foreach (var s in stalks)
            {
                float h = s.w;
                int segs = Mathf.Clamp(Mathf.RoundToInt(h / 2.3f), 4, 9);
                float segLen = h / segs;
                Vector3 basePos = new Vector3(s.x, s.z - 0.15f, s.y);
                int first = bones.Count;
                float phase = PropUtil.Range(rng, 0f, 20f);
                for (int i = 0; i <= segs; i++)
                {
                    var bt = new GameObject("k").transform;
                    if (i == 0)
                    {
                        bt.SetParent(go.transform, false);
                        bt.position = basePos;
                        bt.rotation = Quaternion.AngleAxis(PropUtil.Range(rng, -4f, 4f), leanAxis) * Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f);
                    }
                    else
                    {
                        bt.SetParent(bones[first + i - 1], false);
                        bt.localPosition = new Vector3(0f, segLen, 0f);
                        Vector3 localLean = Quaternion.Inverse(bones[first + i - 1].rotation) * leanAxis;
                        bt.localRotation = Quaternion.AngleAxis(2.2f + PropUtil.Range(rng, -3f, 3f), localLean) * Quaternion.Euler(PropUtil.Range(rng, -3f, 3f), 0f, PropUtil.Range(rng, -3f, 3f));
                    }
                    bones.Add(bt);
                    phases.Add(phase + i * 0.55f);
                    amps.Add(i == 0 ? 0f : Mathf.Min(7f, 1.6f + i * 0.85f));
                }

                // main ribbon: one cross-section per bone, twisting ~40° per segment → faceted light play
                bool dark = rng.NextDouble() < 0.5;
                float twist0 = PropUtil.Range(rng, 0f, 180f), twistStep = PropUtil.Range(rng, 28f, 48f) * (rng.NextDouble() < 0.5 ? -1f : 1f);
                Vector3 prevL = Vector3.zero, prevR = Vector3.zero;
                for (int i = 0; i <= segs; i++)
                {
                    Transform bt = bones[first + i];
                    float t = (float)i / segs;
                    float w = Mathf.Lerp(0.05f, 0.17f, Mathf.Clamp01(t * 3f)) * (1f - 0.45f * t * t);
                    float ang = (twist0 + twistStep * i) * Mathf.Deg2Rad;
                    Vector3 side = bt.right * Mathf.Cos(ang) + bt.forward * Mathf.Sin(ang);
                    Vector3 p = bt.position - center;
                    Vector3 L = p - side * w, R = p + side * w;
                    if (i > 0)
                    {
                        int b0 = first + i - 1, b1 = first + i;
                        mesh.Tri2(prevL, prevR, R, b0, b0, b1, dark);
                        mesh.Tri2(prevL, R, L, b0, b1, b1, dark);
                    }
                    prevL = L; prevR = R;
                }

                // side blades (other green), one per segment, alternating sides
                for (int i = 1; i < segs; i++)
                {
                    Transform bt = bones[first + i];
                    float az = i * 2.4f + PropUtil.Range(rng, -0.4f, 0.4f);
                    float e = PropUtil.Range(rng, 35f, 60f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(az) * Mathf.Cos(e));
                    Blade(mesh, bt.position + bt.up * segLen * 0.4f - center, dir, PropUtil.Range(rng, 0.9f, 1.5f), PropUtil.Range(rng, 0.16f, 0.24f), first + i, !dark);
                }
                // floating canopy at the tip
                Transform tip = bones[first + segs];
                for (int k = 0; k < 3; k++)
                {
                    float az = k * 2.1f + PropUtil.Range(rng, -0.5f, 0.5f);
                    float e = PropUtil.Range(rng, 5f, 25f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(az) * Mathf.Cos(e));
                    Blade(mesh, tip.position - center, dir, PropUtil.Range(rng, 1.4f, 2.1f), PropUtil.Range(rng, 0.18f, 0.26f), first + segs, k % 2 == 0);
                }
            }

            var m = new Mesh { name = "KelpCluster" };
            if (mesh.V.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(mesh.V);
            m.SetNormals(mesh.N);
            m.subMeshCount = 2;
            m.SetTriangles(mesh.T0, 0, false);
            m.SetTriangles(mesh.T1, 1, false);
            m.boneWeights = mesh.W.ToArray();
            var bind = new Matrix4x4[bones.Count];
            Matrix4x4 l2w = go.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Count; i++) bind[i] = bones[i].worldToLocalMatrix * l2w;
            m.bindposes = bind;
            m.RecalculateBounds();
            LowPoly.Sanitize(m);

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = m;
            smr.bones = bones.ToArray();
            smr.rootBone = go.transform;
            smr.sharedMaterials = new[] { PropMats.Kelp, PropMats.KelpDark };
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = true;
            var bounds = m.bounds;
            bounds.Expand(new Vector3(6f, 2f, 6f));
            smr.localBounds = bounds;

            float[] phaseArr = phases.ToArray(), ampArr = amps.ToArray();
            sway.Register(center + Vector3.up * maxH * 0.5f, smr, smr.bones, phaseArr, ampArr);
        }

        /// Faceted kelp blade (3 cross sections, zig-zag along its normal) bound to one bone, two-sided.
        static void Blade(FlatSkin m, Vector3 attach, Vector3 dir, float len, float width, int bone, bool sub1)
        {
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 nrm = Vector3.Cross(side, dir).normalized;
            Vector3 mid = attach + dir * len * 0.45f - Vector3.up * (0.05f * len) + nrm * 0.06f;
            Vector3 end = attach + dir * len - Vector3.up * (0.18f * len);
            Vector3 a0 = attach - side * width * 0.12f, a1 = attach + side * width * 0.12f;
            Vector3 m0 = mid - side * width * 0.5f, m1 = mid + side * width * 0.5f;
            m.Tri2(a0, a1, m1, bone, bone, bone, sub1);
            m.Tri2(a0, m1, m0, bone, bone, bone, sub1);
            m.Tri2(m0, m1, end, bone, bone, bone, sub1);
        }
    }
}
