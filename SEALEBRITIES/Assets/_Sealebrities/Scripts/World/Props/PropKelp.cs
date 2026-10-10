using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Giant kelp forest: 300-500 stalks (8-20 m) grouped into skinned clusters. Every stalk is a bone chain; one
    /// PropKelpSway manager rotates the bones (distance culled), so a whole cluster costs a single draw call.
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
                for (int k = 0; k < count && stalks.Count < StalkTarget; k++)
                {
                    Vector2 o = PropUtil.InDisc(rng, prad);
                    float x = c.x + pc.x + o.x, z = c.z + pc.y + o.y;
                    float g = G(x, z);
                    float depth = -g;
                    if (depth < 9.4f || spacing.Blocked(x, z)) continue;
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
            foreach (var kv in clusters)
            {
                BuildCluster(decor, sway, kv.Value, rng, currentDir);
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
                var mesh = PropRocks.Mesh(sc, rng.Next(), 7, 0.22f);
                batch.Add(mesh, PropMats.Rock, Matrix4x4.TRS(p, r, Vector3.one));
                if (s > 1.6f) PropCol.Mesh(cols, mesh, p, r, Vector3.one, "KelpBoulder");
                // a few cold-water sponges and anemones on the boulders
                int extras = rng.Next(4);
                for (int k = 0; k < extras; k++)
                {
                    Vector2 eo = PropUtil.InDisc(rng, sc.x * 0.6f);
                    Vector3 ep = p + new Vector3(eo.x, sc.y * 0.75f, eo.y);
                    PropReef.Place(batch, rng, ep, Vector3.up, rng.NextDouble() < 0.5 ? PropCoralType.Sponge : PropCoralType.Anemone, 0.8f, -3f);
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
                PropRocks.Build(batch.B(PropMats.Rock, p), p, new Vector3(s * 1.3f, s * 0.7f, s), Quaternion.Euler(0f, PropUtil.Range(rng, 0f, 360f), 0f), rng.Next(), 4);
            }
            batch.Flush(decor, "KelpRocks", ShadowCastingMode.On, 170f);
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
            var b = new PropMeshBuilder();
            var weights = new List<BoneWeight>();
            Quaternion current = Quaternion.Euler(0f, currentDir, 0f);

            foreach (var s in stalks)
            {
                float h = s.w;
                int segs = Mathf.Clamp(Mathf.RoundToInt(h / 2.3f), 4, 9);
                float segLen = h / segs;
                Vector3 basePos = new Vector3(s.x, s.z - 0.15f, s.y);
                int first = bones.Count;
                float phase = PropUtil.Range(rng, 0f, 20f);
                // a steady current leans every stalk the same way
                Vector3 leanAxis = current * Vector3.right;
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

                int vs = b.VertexCount, ts = b.IndexCount;
                // stipe: ring at each bone and one mid-segment ring, 4 sides
                const int sides = 4;
                const float r = 0.035f;
                int ringCount = segs * 2 + 1;
                for (int ri = 0; ri < ringCount; ri++)
                {
                    int bi = Mathf.Min(ri / 2, segs);
                    Transform bt = bones[first + bi];
                    Vector3 cpos = bt.position + (ri % 2 == 1 ? bt.up * segLen * 0.5f : Vector3.zero);
                    float rr = r * (ri == 0 ? 1.6f : 1f);
                    for (int j = 0; j <= sides; j++)
                    {
                        float a = j * Mathf.PI * 2f / sides;
                        Vector3 d = bt.right * Mathf.Cos(a) + bt.forward * Mathf.Sin(a);
                        AddV(b, weights, cpos + d * rr - center, d, new Vector2((float)j / sides * 0.3f, ri * segLen * 0.5f / 3f), first + bi);
                    }
                }
                for (int ri = 0; ri < ringCount - 1; ri++)
                {
                    for (int j = 0; j < sides; j++)
                    {
                        int a0 = vs + ri * (sides + 1) + j;
                        int b0 = a0 + sides + 1;
                        b.Quad(a0, b0, b0 + 1, a0 + 1);
                    }
                }
                b.SmoothNormals(vs, ts, false);

                // blades along the stipe
                for (int i = 1; i < segs; i++)
                {
                    Transform bt = bones[first + i];
                    for (int k = 0; k < 2; k++)
                    {
                        float f = k == 0 ? 0.3f : 0.75f;
                        Vector3 attach = bt.position + bt.up * segLen * f;
                        float az = (i * 2 + k) * 2.4f + PropUtil.Range(rng, -0.4f, 0.4f);
                        float e = PropUtil.Range(rng, 40f, 65f) * Mathf.Deg2Rad;
                        Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(az) * Mathf.Cos(e));
                        Blade(b, weights, attach - center, dir, PropUtil.Range(rng, 0.9f, 1.6f), PropUtil.Range(rng, 0.12f, 0.2f), first + i);
                    }
                }
                // floating canopy at the tip
                Transform tip = bones[first + segs];
                for (int k = 0; k < 4; k++)
                {
                    float az = k * 1.57f + PropUtil.Range(rng, -0.5f, 0.5f);
                    float e = PropUtil.Range(rng, 5f, 25f) * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(az) * Mathf.Cos(e));
                    Blade(b, weights, tip.position - center, dir, PropUtil.Range(rng, 1.4f, 2.2f), PropUtil.Range(rng, 0.14f, 0.22f), first + segs);
                }
            }

            var mesh = b.ToMesh("KelpCluster");
            mesh.boneWeights = weights.ToArray();
            var bind = new Matrix4x4[bones.Count];
            Matrix4x4 l2w = go.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Count; i++) bind[i] = bones[i].worldToLocalMatrix * l2w;
            mesh.bindposes = bind;
            mesh.RecalculateBounds();

            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones.ToArray();
            smr.rootBone = go.transform;
            smr.sharedMaterial = PropMats.Kelp;
            smr.quality = SkinQuality.Bone1;
            smr.updateWhenOffscreen = false;
            smr.shadowCastingMode = ShadowCastingMode.Off;
            smr.receiveShadows = true;
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(6f, 2f, 6f));
            smr.localBounds = bounds;

            float[] phaseArr = phases.ToArray(), ampArr = amps.ToArray();
            sway.Register(center + Vector3.up * maxH * 0.5f, smr, smr.bones, phaseArr, ampArr);
        }

        static void AddV(PropMeshBuilder b, List<BoneWeight> w, Vector3 p, Vector3 n, Vector2 uv, int bone)
        {
            b.Vert(p, n, uv);
            w.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
        }

        /// Wavy kelp blade (4 cross sections) bound to one bone.
        static void Blade(PropMeshBuilder b, List<BoneWeight> w, Vector3 attach, Vector3 dir, float len, float width, int bone)
        {
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 nrm = Vector3.Cross(side, dir).normalized;
            int v0 = b.VertexCount;
            for (int k = 0; k < 4; k++)
            {
                float t = k / 3f;
                float wk = width * (k == 0 ? 0.25f : k == 3 ? 0.3f : (k == 1 ? 1f : 0.85f)) * 0.5f;
                Vector3 p = attach + dir * len * t - Vector3.up * (0.18f * t * t * len) + nrm * Mathf.Sin(t * 6f) * 0.05f;
                AddV(b, w, p - side * wk, nrm, new Vector2(0f, t * len / 3f), bone);
                AddV(b, w, p + side * wk, nrm, new Vector2(1f, t * len / 3f), bone);
            }
            for (int k = 0; k < 3; k++)
            {
                int a = v0 + k * 2;
                b.Quad(a, a + 2, a + 3, a + 1);
            }
        }
    }
}
