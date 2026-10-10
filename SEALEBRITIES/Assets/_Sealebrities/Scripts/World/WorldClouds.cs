using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// A handful of chunky, faceted low-poly cloud puffs drifting slowly with the wind. They live in a toroidal field
    /// around the camera (world-anchored, so they show parallax) and shrink to nothing before wrapping to the far side.
    /// Lit by sun/moon like everything else (pink-gold at sunset), with a soft emission floor so shaded sides stay bright.
    public sealed class WorldClouds
    {
        const int Count = 15;
        const int Variants = 5;
        const float Half = 1500f;                       // field half-size around the camera
        static readonly Vector2 Wind = new Vector2(-1.5f, 0.6f);   // m/s, trade wind from the east

        readonly Transform[] objs = new Transform[Count];
        readonly MeshRenderer[] rends = new MeshRenderer[Count];
        readonly Vector2[] anchor = new Vector2[Count];
        readonly float[] altitude = new float[Count];
        readonly Vector3[] shape = new Vector3[Count];
        readonly Mesh[] meshes = new Mesh[Variants];
        readonly Material mat;
        readonly int idEmission;
        Vector2 drift;
        bool shown = true;
        Color lastGlow = new Color(-1f, 0f, 0f, 0f);

        public Material Material => mat;

        public WorldClouds(Transform parent)
        {
            idEmission = Shader.PropertyToID("_EmissionColor");
            mat = new Material(MaterialLibrary.Lit) { name = "SB_Cloud" };
            mat.SetColor("_BaseColor", new Color(0.97f, 0.97f, 1f));
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetColor("_EmissionColor", new Color(0.2f, 0.22f, 0.26f));
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

            var rng = new System.Random(2718);
            for (int v = 0; v < Variants; v++) meshes[v] = BuildCloudMesh(rng, v);

            var root = new GameObject("Clouds").transform;
            root.SetParent(parent, false);
            for (int i = 0; i < Count; i++)
            {
                var go = new GameObject("Cloud" + i);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[i % Variants];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.allowOcclusionWhenDynamic = false;
                objs[i] = go.transform;
                rends[i] = r;

                // spread on a jittered grid so the sky never clumps
                int gx = i % 4, gz = i / 4;
                anchor[i] = new Vector2((gx + 0.5f + Range(rng, -0.35f, 0.35f)) / 4f * 2f * Half - Half,
                                        (gz + 0.5f + Range(rng, -0.35f, 0.35f)) / 4f * 2f * Half - Half);
                altitude[i] = Range(rng, 150f, 250f);
                float len = Range(rng, 34f, 62f);
                shape[i] = new Vector3(len, len * Range(rng, 0.55f, 0.75f), len * Range(rng, 0.7f, 1f));
                go.transform.rotation = Quaternion.Euler(0f, Range(rng, -25f, 25f) + (Mathf.Atan2(Wind.x, Wind.y) * Mathf.Rad2Deg + 90f), 0f);
            }
        }

        static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

        /// Moves the field with the wind around the camera; glow = emission floor (already scaled for day/night).
        public void Tick(Vector3 cam, float dt, bool show, Color glow)
        {
            if (show != shown)
            {
                shown = show;
                if (!show) for (int i = 0; i < Count; i++) rends[i].enabled = false;
            }
            if (!show) return;

            if (Mathf.Abs(glow.r - lastGlow.r) + Mathf.Abs(glow.g - lastGlow.g) + Mathf.Abs(glow.b - lastGlow.b) > 0.004f)
            {
                mat.SetColor(idEmission, glow);
                lastGlow = glow;
            }

            drift += Wind * dt;
            if (drift.x > 100000f || drift.x < -100000f) drift.x = Mathf.Repeat(drift.x, 2f * Half);
            if (drift.y > 100000f || drift.y < -100000f) drift.y = Mathf.Repeat(drift.y, 2f * Half);
            for (int i = 0; i < Count; i++)
            {
                float rx = Mathf.Repeat(anchor[i].x + drift.x - cam.x + Half, 2f * Half) - Half;
                float rz = Mathf.Repeat(anchor[i].y + drift.y - cam.z + Half, 2f * Half) - Half;
                float edge = Mathf.Max(Mathf.Abs(rx), Mathf.Abs(rz)) / Half;
                float k = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 0.98f, edge));
                bool on = k > 0.02f;
                var r = rends[i];
                if (r.enabled != on) r.enabled = on;
                if (!on) continue;
                var t = objs[i];
                t.position = new Vector3(cam.x + rx, altitude[i], cam.z + rz);
                t.localScale = shape[i] * k;
            }
        }

        public void Destroy()
        {
            if (mat != null) Object.Destroy(mat);
            for (int v = 0; v < Variants; v++) if (meshes[v] != null) Object.Destroy(meshes[v]);
        }

        // ------------------------------------------------------------------ meshes
        /// 3-6 overlapping jittered icosphere puffs along x with a flat bottom, flat shaded. Unit size ~2 x 0.8 x 1.
        static Mesh BuildCloudMesh(System.Random rng, int variant)
        {
            var verts = new List<Vector3>(512);
            var tris = new List<int>(1536);
            int puffs = 3 + variant % 4;
            float span = 1.7f;
            for (int p = 0; p < puffs; p++)
            {
                float f = puffs == 1 ? 0.5f : p / (float)(puffs - 1);
                float x = (f - 0.5f) * span + Range(rng, -0.12f, 0.12f);
                float mid = 1f - Mathf.Abs(f - 0.5f) * 2f;                   // bigger puffs in the middle
                float r = Mathf.Lerp(0.32f, 0.55f, mid) * Range(rng, 0.85f, 1.15f);
                var c = new Vector3(x, r * Range(rng, 0.2f, 0.45f), Range(rng, -0.18f, 0.18f));
                AddPuff(verts, tris, c, r, rng);
            }
            // a couple of small side puffs for a lumpier silhouette
            for (int p = 0; p < 2; p++)
            {
                float r = Range(rng, 0.2f, 0.3f);
                var c = new Vector3(Range(rng, -0.5f, 0.5f), r * 0.6f + 0.15f, (p == 0 ? 1f : -1f) * Range(rng, 0.25f, 0.4f));
                AddPuff(verts, tris, c, r, rng);
            }
            // flat base
            for (int i = 0; i < verts.Count; i++)
            {
                var v = verts[i];
                if (v.y < 0f) { v.y = 0f; verts[i] = v; }
            }
            var m = new Mesh { name = "SB_CloudPuff" + variant };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            var flat = LowPoly.Flatten(m);
            Object.Destroy(m);
            flat.name = "SB_CloudPuff" + variant;
            flat.RecalculateBounds();
            return flat;
        }

        static readonly Dictionary<long, int> MidCache = new Dictionary<long, int>();

        /// Icosphere (one subdivision, 80 faces) with jittered radius.
        static void AddPuff(List<Vector3> verts, List<int> tris, Vector3 center, float radius, System.Random rng)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var pts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (int i = 0; i < pts.Count; i++) pts[i] = pts[i].normalized;
            int[] faces =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            MidCache.Clear();
            var sub = new List<int>(240);
            for (int f = 0; f < faces.Length; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(pts, a, b), bc = Mid(pts, b, c), ca = Mid(pts, c, a);
                sub.Add(a); sub.Add(ab); sub.Add(ca);
                sub.Add(b); sub.Add(bc); sub.Add(ab);
                sub.Add(c); sub.Add(ca); sub.Add(bc);
                sub.Add(ab); sub.Add(bc); sub.Add(ca);
            }
            int baseIndex = verts.Count;
            for (int i = 0; i < pts.Count; i++)
            {
                float j = Range(rng, 0.88f, 1.1f);
                var p = pts[i] * radius * j;
                p.y *= 0.82f;
                verts.Add(center + p);
            }
            // this face list has Cross(b - a, c - a) pointing outward = Unity's front face (clockwise from outside)
            for (int i = 0; i < sub.Count; i++) tris.Add(baseIndex + sub[i]);
        }

        static int Mid(List<Vector3> pts, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (MidCache.TryGetValue(key, out int id)) return id;
            id = pts.Count;
            pts.Add(((pts[a] + pts[b]) * 0.5f).normalized);
            MidCache[key] = id;
            return id;
        }
    }
}
