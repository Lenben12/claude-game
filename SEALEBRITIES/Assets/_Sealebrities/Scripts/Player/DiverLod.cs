using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// L.13 level of detail for the bean characters (co-op divers, the menu crew, NPCs): LOD0 = the built model,
    /// LOD1 (from ~12 m) and LOD2 (from ~35 m) are cheap proxies on the SAME bones (siblings of each part, same local
    /// transform), so the puppet animation, toggled roots (strap lamp, scooter, mouth shapes …) and shadows keep working.
    ///   • lathe meshes (body, head, limbs, joints, eyes, tanks, hats – DiverMeshes.LatheY / NpcMesh.Lathe layout) are
    ///     rebuilt with fewer rings and radial segments (same submesh layout → same materials);
    ///   • small details (eyelids, brows, highlights, bands, buckles, mouth, freckles …) are left out; LOD2 keeps only the
    ///     big silhouette parts. Other meshes (plates, slabs) are reused when big enough.
    /// Low meshes are built once per source mesh and shared. ForceLod0 pins every registered character to LOD0 (character
    /// screen, AutoShot); Force(go) pins one model (selfie, character preview).
    public static class DiverLod
    {
        /// Screen-relative heights of the transitions (1.85 m bean, 70° FOV: ≈ 12 m, ≈ 35 m, culled beyond ≈ 330 m).
        public const float Lod0Height = 0.11f, Lod1Height = 0.038f, CullHeight = 0.004f;
        const float Lod1MinSize = 0.035f, Lod2MinSize = 0.12f;

        static readonly Dictionary<Mesh, Mesh> lowCache1 = new Dictionary<Mesh, Mesh>(), lowCache2 = new Dictionary<Mesh, Mesh>();
        static readonly List<LODGroup> groups = new List<LODGroup>();
        static bool forceLod0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { lowCache1.Clear(); lowCache2.Clear(); groups.Clear(); forceLod0 = false; }

        /// Pins every character model to LOD0 (screenshots, close-up screens). Applies to existing and new models.
        public static bool ForceLod0
        {
            get => forceLod0;
            set
            {
                forceLod0 = value;
                for (int i = groups.Count - 1; i >= 0; i--)
                {
                    var g = groups[i];
                    if (g == null) { groups.RemoveAt(i); continue; }
                    g.ForceLOD(value ? 0 : -1);
                }
            }
        }

        /// Pins one model to LOD0 (or releases it).
        public static void Force(GameObject go, bool lod0)
        {
            if (go == null) return;
            var g = go.GetComponentInChildren<LODGroup>(true);
            if (g != null) g.ForceLOD(lod0 || forceLod0 ? 0 : -1);
        }

        /// Adds LOD1 / LOD2 proxies and a LODGroup to a freshly built character (call once, after the model is complete).
        public static LODGroup Build(GameObject root)
        {
            if (root == null || root.GetComponent<LODGroup>() != null) return null;
            var rs = root.GetComponentsInChildren<MeshRenderer>(true);
            var lod0 = new List<Renderer>(rs.Length);
            var lod1 = new List<Renderer>(rs.Length / 2);
            var lod2 = new List<Renderer>(rs.Length / 4);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                var mf = r != null ? r.GetComponent<MeshFilter>() : null;
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                lod0.Add(r);
                string name = r.gameObject.name;
                float size = PartSize(mesh, r.transform);
                bool band = IsBand(mesh.name);
                if (!SkipLod1(name, band, size))
                {
                    var m1 = Low(mesh, 1);
                    if (m1 != null) lod1.Add(Proxy(r, m1, "_L1"));
                }
                if (!SkipLod2(name, band, size))
                {
                    var m2 = Low(mesh, 2);
                    if (m2 != null) lod2.Add(Proxy(r, m2, "_L2"));
                }
            }
            if (lod0.Count == 0) return null;
            var g = root.AddComponent<LODGroup>();
            g.fadeMode = LODFadeMode.None;
            g.SetLODs(new[]
            {
                new LOD(Lod0Height, lod0.ToArray()),
                new LOD(Lod1Height, lod1.ToArray()),
                new LOD(CullHeight, lod2.ToArray()),
            });
            g.RecalculateBounds();
            if (forceLod0) g.ForceLOD(0);
            for (int i = groups.Count - 1; i >= 0; i--) if (groups[i] == null) groups.RemoveAt(i);
            groups.Add(g);
            return g;
        }

        // ------------------------------------------------------------------ what goes where
        static float PartSize(Mesh m, Transform t)
        {
            Vector3 s = Vector3.Scale(m.bounds.size, t.lossyScale);
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        static bool IsBand(string meshName) =>
            meshName != null && (meshName.StartsWith("SB_Diver_torus", System.StringComparison.Ordinal) || meshName.StartsWith("SB_Npc_tor", System.StringComparison.Ordinal));

        static bool SkipLod1(string name, bool band, float size)
        {
            if (size < Lod1MinSize) return true;
            if (band && size < 0.3f) return true;   // straps, cuffs, tank bands (a big torus – the gull nest – stays)
            switch (name)
            {
                case "Lid": case "LidCap": case "Brow": case "Shine": case "Disc": case "Teeth": case "M": case "Tape": case "Dent":
                case "Buckle": case "Clip": case "RecDot": case "LanyardL": case "LanyardR": case "Freckle": case "Hook": case "Twig":
                case "Dot": case "Rail": case "Thumb": case "ValveStage": case "ValveKnob": case "Blush":
                    return true;
            }
            return false;
        }

        static bool SkipLod2(string name, bool band, float size)
        {
            if (SkipLod1(name, band, size) || size < Lod2MinSize) return true;
            switch (name)
            {
                case "White": case "Pupil": case "Star": case "Heart": case "Glass": case "Snorkel": case "SnorkelTip": case "Strap":
                case "Frame": case "Skirt": case "Valve": case "Manifold": case "Collar": case "Cuff": case "Stripe": case "Nose":
                case "Joint": case "Ball":
                    return true;
            }
            return name.EndsWith("Neck", System.StringComparison.Ordinal);
        }

        static MeshRenderer Proxy(MeshRenderer src, Mesh mesh, string suffix)
        {
            var go = new GameObject(src.gameObject.name + suffix);
            go.layer = src.gameObject.layer;
            var t = go.transform;
            var st = src.transform;
            t.SetParent(st.parent, false);
            t.localPosition = st.localPosition;
            t.localRotation = st.localRotation;
            t.localScale = st.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = src.sharedMaterials;
            r.shadowCastingMode = src.shadowCastingMode;
            r.receiveShadows = src.receiveShadows;
            r.lightProbeUsage = src.lightProbeUsage;
            r.reflectionProbeUsage = src.reflectionProbeUsage;
            r.enabled = src.enabled;
            if (!src.gameObject.activeSelf) go.SetActive(false);
            return r;
        }

        // ------------------------------------------------------------------ low meshes
        /// Lower-resolution version of a lathe mesh (level 1: ~½ rings × ½ segments, level 2: ~⅓ × ⅓); other meshes are
        /// reused as they are. Built once per (mesh, level).
        static Mesh Low(Mesh src, int level)
        {
            var cache = level == 1 ? lowCache1 : lowCache2;
            if (cache.TryGetValue(src, out var cached) && cached != null) return cached;
            Mesh m = TryLowLathe(src, level) ?? src;
            cache[src] = m;
            return m;
        }

        static Mesh TryLowLathe(Mesh src, int level)
        {
            if (!src.isReadable) return null;
            var v = src.vertices;
            int N = v.Length;
            if (N < 12) return null;
            int ring = DetectRing(v);
            if (ring < 4) return null;
            int n = N / ring, segs = ring - 1;
            if (n < 3) return null;
            // quad (profile segment i, radial segment j) → submesh, from the source triangles (A B D / A D C per quad)
            int subCount = Mathf.Max(1, src.subMeshCount);
            var map = new int[(n - 1) * segs];
            for (int k = 0; k < map.Length; k++) map[k] = -1;
            for (int q = 0; q < subCount; q++)
            {
                var tri = src.GetTriangles(q);
                for (int t = 0; t + 5 < tri.Length; t += 6)
                {
                    int A = tri[t];
                    int i = A / ring, j = A % ring;
                    if (i < n - 1 && j < segs) map[i * segs + j] = q;
                }
            }
            float rf = level == 1 ? 0.5f : 0.3f, sf = level == 1 ? 0.5f : 0.34f;
            int n2 = Mathf.Clamp(Mathf.RoundToInt(n * rf), 3, n);
            int s2 = Mathf.Clamp(Mathf.RoundToInt(segs * sf), 6, segs);
            if (n2 == n && s2 == segs) return null;
            var pick = new int[n2];
            var prof = new Vector2[n2];
            for (int k = 0; k < n2; k++)
            {
                int i = Mathf.RoundToInt(k * (n - 1) / (float)(n2 - 1));
                pick[k] = i;
                Vector3 p = v[i * ring];
                prof[k] = new Vector2(Mathf.Max(0f, p.x), p.y);
            }
            int segsSrc = segs;
            var low = DiverMeshes.LatheY(prof, s2, subCount, (k, j) =>
            {
                int i = Mathf.Clamp(pick[k], 0, n - 2);
                int js = Mathf.Clamp(Mathf.FloorToInt((j + 0.5f) * segsSrc / s2), 0, segsSrc - 1);
                for (int d = 0; d < n - 1; d++)
                {
                    int a = Mathf.Clamp(i + d, 0, n - 2), b = Mathf.Clamp(i - d, 0, n - 2);
                    int qa = map[a * segsSrc + js];
                    if (qa >= 0) return qa;
                    int qb = map[b * segsSrc + js];
                    if (qb >= 0) return qb;
                }
                return 0;
            });
            low.name = src.name + "_LOD" + level;
            return low;
        }

        /// Ring length of the LatheY / NpcMesh.Lathe vertex layout (n rings × (segs + 1), column 0 at angle 0 on +X,
        /// the last column back at 2π) – 0 when the mesh is no lathe (torus, plate …).
        static int DetectRing(Vector3[] v)
        {
            int N = v.Length;
            for (int r = 5; r <= N / 3; r++)
            {
                if (N % r != 0) continue;
                int rings = N / r;
                bool ok = true, sawAngle = false;
                float step = Mathf.PI * 2f / (r - 1);
                for (int i = 0; i < rings && ok; i++)
                {
                    Vector3 a = v[i * r], b = v[i * r + r - 1];
                    if ((a - b).sqrMagnitude > 1e-8f || Mathf.Abs(a.z) > 1e-4f || a.x < -1e-4f || Mathf.Abs(a.y - b.y) > 1e-5f) { ok = false; break; }
                    // every column of a ring has the same height and radius
                    Vector3 c = v[i * r + 1];
                    if (Mathf.Abs(c.y - a.y) > 1e-5f) { ok = false; break; }
                    float rad = new Vector2(c.x, c.z).magnitude;
                    if (Mathf.Abs(rad - a.x) > 1e-4f + a.x * 1e-3f) { ok = false; break; }
                    if (a.x > 1e-3f)
                    {
                        float ang = Mathf.Atan2(c.z, c.x);
                        if (Mathf.Abs(ang - step) > 1e-3f) { ok = false; break; }
                        sawAngle = true;
                    }
                }
                if (ok && sawAngle) return r;
            }
            return 0;
        }
    }
}
