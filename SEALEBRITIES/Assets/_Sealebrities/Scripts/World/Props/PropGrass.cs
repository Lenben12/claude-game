using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Grass clumps in the "How to Fish" look (art bible §1.9): 12–24 tapering blades per clump that lean outward,
    /// colored by one 1 x 8 ramp texture (dark base → light tip). Rendered with GPU instancing (Graphics.RenderMeshInstanced)
    /// per 16 m cell: full clumps within 20 m, 6–8 blade clumps beyond, culled at the tier distance (60/40/25 m) and drawn
    /// at the tier density (100/60/30 %). No shadow casting, no colliders (grass is always passable).
    /// Placement is deterministic (fixed seeds); only the drawn subset depends on the local graphics quality.
    public sealed class PropGrassField : MonoBehaviour
    {
        public const float CellSize = 16f;
        const int MaxPerCall = 1000;

        /// Variant groups: meadow (short), jungle floor / forest edge (tall), dune (dry, sparse).
        public const int MeadowFirst = 0, MeadowCount = 4, JungleFirst = 4, JungleCount = 3, DuneFirst = 7, DuneCount = 2;
        public const int VariantCount = 9;

        sealed class BuildCell
        {
            public readonly List<Matrix4x4>[] lists = new List<Matrix4x4>[VariantCount];
            public float minY = float.MaxValue, maxY = float.MinValue;
            public int cx, cz;
        }

        struct Cell
        {
            public Vector2 center;
            public Bounds bounds;
            public int start;           // first instance of variant 0; the variants follow each other
        }

        Mesh[] lod0, lod1;
        Material[] mats;
        RenderParams[] rp;
        readonly Dictionary<long, BuildCell> building = new Dictionary<long, BuildCell>();
        Matrix4x4[] data;
        Cell[] cells;
        int[] counts;                   // cells.Length * VariantCount
        int[] starts;
        public int InstanceCount { get; private set; }

        // ------------------------------------------------------------------ setup
        public static PropGrassField Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<PropGrassField>();
            f.InitMeshes();
            return f;
        }

        void InitMeshes()
        {
            lod0 = new Mesh[VariantCount];
            lod1 = new Mesh[VariantCount];
            mats = new Material[VariantCount];
            for (int v = 0; v < VariantCount; v++)
            {
                int seed = 9100 + v * 37;
                if (v < JungleFirst) BuildClump(seed, 12, 18, 0.45f, 0.7f, 0.25f, 0.4f, 6, out lod0[v], out lod1[v]);
                else if (v < DuneFirst) BuildClump(seed, 16, 24, 0.7f, 1.1f, 0.35f, 0.5f, 8, out lod0[v], out lod1[v]);
                else BuildClump(seed, 8, 12, 0.35f, 0.6f, 0.2f, 0.3f, 5, out lod0[v], out lod1[v]);
                mats[v] = v < DuneFirst ? PropMats.GrassRamp : PropMats.DuneRamp;
            }
            rp = new RenderParams[VariantCount];
            for (int v = 0; v < VariantCount; v++)
            {
                rp[v] = new RenderParams(mats[v])
                {
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    layer = gameObject.layer,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                };
            }
        }

        struct Blade
        {
            public Vector3 basePos, outDir, side;
            public float h, w, lean;
        }

        static readonly List<Vector3> SV = new List<Vector3>(512);
        static readonly List<Vector3> SN = new List<Vector3>(512);
        static readonly List<Vector2> SU = new List<Vector2>(512);
        static readonly List<int> ST = new List<int>(1024);

        /// Builds one clump variant (full + reduced). Blades bend once when longer than 0.65 m.
        static void BuildClump(int seed, int minBlades, int maxBlades, float hMin, float hMax, float rMin, float rMax, int lodBlades, out Mesh full, out Mesh lite)
        {
            var rng = new System.Random(seed);
            int n = minBlades + rng.Next(maxBlades - minBlades + 1);
            float radius = PropUtil.Range(rng, rMin, rMax);
            var blades = new Blade[n];
            for (int i = 0; i < n; i++)
            {
                float a = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float d = Mathf.Sqrt((float)rng.NextDouble()) * radius * 0.6f;
                Vector3 bp = new Vector3(Mathf.Cos(a) * d, -0.04f, Mathf.Sin(a) * d);
                float oa = d > 0.02f ? a + PropUtil.Range(rng, -0.4f, 0.4f) : PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                float fa = PropUtil.Range(rng, 0f, Mathf.PI * 2f);
                blades[i] = new Blade
                {
                    basePos = bp,
                    outDir = new Vector3(Mathf.Cos(oa), 0f, Mathf.Sin(oa)),
                    side = new Vector3(-Mathf.Sin(fa), 0f, Mathf.Cos(fa)),
                    h = PropUtil.Range(rng, hMin, hMax),
                    w = PropUtil.Range(rng, 0.07f, 0.12f) * 0.5f,
                    lean = PropUtil.Range(rng, 8f, 30f) * Mathf.Deg2Rad
                };
            }
            // taller blades first so the reduced clump keeps the silhouette
            System.Array.Sort(blades, (x, y) => y.h.CompareTo(x.h));
            full = MakeMesh("GrassClump" + seed, blades, n, 1f);
            lite = MakeMesh("GrassClumpLod" + seed, blades, Mathf.Min(n, lodBlades), 1.45f);
        }

        static Mesh MakeMesh(string name, Blade[] blades, int count, float widthMul)
        {
            SV.Clear(); SN.Clear(); SU.Clear(); ST.Clear();
            for (int i = 0; i < count; i++)
            {
                var b = blades[i];
                Vector3 o = b.outDir, s = b.side * (b.w * widthMul);
                // normals point mostly up so both blade sides light like the ground (two-sided material)
                Vector3 nrm = (Vector3.up + o * 0.35f).normalized;
                int k = SV.Count;
                if (b.h > 0.65f)
                {
                    float l1 = b.h * 0.55f, l2 = b.h * 0.45f;
                    Vector3 mid = b.basePos + o * (Mathf.Sin(b.lean * 0.6f) * l1) + Vector3.up * (Mathf.Cos(b.lean * 0.6f) * l1);
                    Vector3 tip = mid + o * (Mathf.Sin(b.lean * 1.7f) * l2) + Vector3.up * (Mathf.Cos(b.lean * 1.7f) * l2);
                    SV.Add(b.basePos - s); SV.Add(b.basePos + s); SV.Add(mid + s * 0.55f); SV.Add(mid - s * 0.55f); SV.Add(tip);
                    SU.Add(new Vector2(0.5f, 0f)); SU.Add(new Vector2(0.5f, 0f)); SU.Add(new Vector2(0.5f, 0.55f)); SU.Add(new Vector2(0.5f, 0.55f)); SU.Add(new Vector2(0.5f, 1f));
                    for (int j = 0; j < 5; j++) SN.Add(nrm);
                    ST.Add(k); ST.Add(k + 1); ST.Add(k + 2);
                    ST.Add(k); ST.Add(k + 2); ST.Add(k + 3);
                    ST.Add(k + 3); ST.Add(k + 2); ST.Add(k + 4);
                }
                else
                {
                    Vector3 tip = b.basePos + o * (Mathf.Sin(b.lean) * b.h) + Vector3.up * (Mathf.Cos(b.lean) * b.h);
                    SV.Add(b.basePos - s); SV.Add(b.basePos + s); SV.Add(tip);
                    SU.Add(new Vector2(0.5f, 0f)); SU.Add(new Vector2(0.5f, 0f)); SU.Add(new Vector2(0.5f, 1f));
                    for (int j = 0; j < 3; j++) SN.Add(nrm);
                    ST.Add(k); ST.Add(k + 1); ST.Add(k + 2);
                }
            }
            var m = new Mesh { name = name };
            m.SetVertices(SV);
            m.SetNormals(SN);
            m.SetUVs(0, SU);
            m.SetTriangles(ST, 0, true);
            m.UploadMeshData(true);
            return m;
        }

        // ------------------------------------------------------------------ placement
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        public void Add(int variant, Vector3 pos, float yawDeg, float scale, Vector3 tilt)
        {
            if (variant < 0 || variant >= VariantCount) return;
            int cx = Mathf.FloorToInt(pos.x / CellSize), cz = Mathf.FloorToInt(pos.z / CellSize);
            long k = Key(cx, cz);
            if (!building.TryGetValue(k, out var bc))
            {
                bc = new BuildCell { cx = cx, cz = cz };
                building[k] = bc;
            }
            var list = bc.lists[variant];
            if (list == null) { list = new List<Matrix4x4>(32); bc.lists[variant] = list; }
            Quaternion q = Quaternion.FromToRotation(Vector3.up, (Vector3.up + tilt).normalized) * Quaternion.Euler(0f, yawDeg, 0f);
            list.Add(Matrix4x4.TRS(pos, q, new Vector3(scale, scale * PropUtil.Range(Shuffle, 0.9f, 1.15f), scale)));
            if (pos.y < bc.minY) bc.minY = pos.y;
            if (pos.y > bc.maxY) bc.maxY = pos.y;
        }

        static System.Random Shuffle = new System.Random(4242);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Shuffle = new System.Random(4242); }

        /// Packs the instances into one array (per cell: variant after variant, each list shuffled so that a prefix is an
        /// even subset for the lower quality tiers).
        public void Finish()
        {
            var keys = new List<long>(building.Keys);
            keys.Sort();
            int total = 0;
            foreach (var bc in building.Values)
                for (int v = 0; v < VariantCount; v++)
                    if (bc.lists[v] != null) total += bc.lists[v].Count;
            data = new Matrix4x4[total];
            cells = new Cell[keys.Count];
            counts = new int[keys.Count * VariantCount];
            starts = new int[keys.Count * VariantCount];
            var rng = new System.Random(777);
            int at = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                var bc = building[keys[i]];
                var c = new Cell { start = at, center = new Vector2((bc.cx + 0.5f) * CellSize, (bc.cz + 0.5f) * CellSize) };
                float y0 = bc.minY - 0.2f, y1 = bc.maxY + 1.4f;
                c.bounds = new Bounds(new Vector3(c.center.x, (y0 + y1) * 0.5f, c.center.y), new Vector3(CellSize + 2f, y1 - y0, CellSize + 2f));
                for (int v = 0; v < VariantCount; v++)
                {
                    var list = bc.lists[v];
                    starts[i * VariantCount + v] = at;
                    if (list == null) continue;
                    for (int j = list.Count - 1; j > 0; j--)
                    {
                        int r = rng.Next(j + 1);
                        var t = list[j]; list[j] = list[r]; list[r] = t;
                    }
                    for (int j = 0; j < list.Count; j++) data[at + j] = list[j];
                    counts[i * VariantCount + v] = list.Count;
                    at += list.Count;
                }
                cells[i] = c;
            }
            InstanceCount = total;
            building.Clear();
        }

        // ------------------------------------------------------------------ drawing
        void Update()
        {
            PropQuality.Poll();
            if (cells == null || data == null || data.Length == 0 || !PropCam.Valid) return;
            Vector3 cam = PropCam.Position;
            float maxD = PropQuality.GrassDistance;
            float dens = PropQuality.GrassDensity;
            float reach = maxD + CellSize * 0.71f;
            float reach2 = reach * reach;
            for (int i = 0; i < cells.Length; i++)
            {
                ref Cell c = ref cells[i];
                float dx = c.center.x - cam.x, dz = c.center.y - cam.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > reach2) continue;
                float dy = Mathf.Max(0f, cam.y - c.bounds.max.y);
                if (dy > maxD) continue;
                if (!PropCam.InView(c.bounds)) continue;   // cells behind the camera: no draw call submission at all
                float d = Mathf.Sqrt(d2) - CellSize * 0.71f;
                bool near = d < PropQuality.GrassNearLod;
                int baseIdx = i * VariantCount;
                for (int v = 0; v < VariantCount; v++)
                {
                    int n = counts[baseIdx + v];
                    if (n == 0) continue;
                    int k = dens >= 0.999f ? n : Mathf.Max(0, Mathf.RoundToInt(n * dens));
                    if (k == 0) continue;
                    int s = starts[baseIdx + v];
                    rp[v].worldBounds = c.bounds;
                    Mesh mesh = near ? lod0[v] : lod1[v];
                    while (k > 0)
                    {
                        int m = k > MaxPerCall ? MaxPerCall : k;
                        Graphics.RenderMeshInstanced(rp[v], mesh, 0, data, m, s);
                        s += m;
                        k -= m;
                    }
                }
            }
        }

        void OnDestroy()
        {
            if (lod0 != null) foreach (var m in lod0) if (m != null) Destroy(m);
            if (lod1 != null) foreach (var m in lod1) if (m != null) Destroy(m);
        }
    }
}
