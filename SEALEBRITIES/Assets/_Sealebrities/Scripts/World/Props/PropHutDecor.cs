using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Round 3 hut furnishing (GDD §19 „individueller einrichten“): the CUSTOMIZABLE parts of the hut interior, built
    /// once as their own renderers / objects so a choice applies live without rebuilding the hut:
    ///  * wall lining (vertical tongue-and-groove boards, 3 tone renderers → sharedMaterial swap from HutDecor.Walls),
    ///  * floor boards (3 tone renderers → HutDecor.Floors),
    ///  * rugs (one object per pattern, toggled → HutDecor.Rugs),
    ///  * curtains (one renderer, colour swap / hidden → HutDecor.Curtains),
    ///  * decor items (one object per HutDecor.Items entry with its own colliders, toggled when placed).
    /// Everything else of the hut stays merged in PropHut's batch. Coordinates are PropHut's local frame (origin at the
    /// hut floor, +x east / door side, +z north; interior x [-4.43, 4.43], z [-4.93, 4.93]).
    public sealed class PropHutDecor : MonoBehaviour
    {
        public static PropHutDecor Instance { get; private set; }

        public Vector3 origin;
        public readonly MeshRenderer[] wall = new MeshRenderer[3];
        public readonly MeshRenderer[] floor = new MeshRenderer[3];
        public MeshRenderer curtain;
        public readonly GameObject[] rugs = new GameObject[HutDecor.Rugs.Length];
        public readonly GameObject[] items = new GameObject[HutDecor.Items.Length];

        int applied;
        bool hasApplied;
        float poll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        void OnEnable()
        {
            Instance = this;
            HutDecor.Changed += MarkDirty;
            hasApplied = false;
        }

        void OnDisable()
        {
            HutDecor.Changed -= MarkDirty;
            if (Instance == this) Instance = null;
        }

        void MarkDirty() { hasApplied = false; }

        void Update()
        {
            poll -= Time.unscaledDeltaTime;
            if (hasApplied && poll > 0f) return;
            poll = 0.25f;
            ApplyNow(false);
        }

        /// Shows the current GameState.hut (cheap no-op while the signature is unchanged).
        public void ApplyNow(bool force)
        {
            var gm = GameManager.I;
            var s = gm != null ? gm.State : null;
            int sig = HutDecor.Signature(s);
            if (!force && hasApplied && sig == applied) return;
            applied = sig;
            hasApplied = true;
            Apply(s != null ? s.hut : null);
        }

        void Apply(HutDecorState h)
        {
            int w = h != null ? Mathf.Clamp(h.wall, 0, HutDecor.Walls.Length - 1) : 0;
            int f = h != null ? Mathf.Clamp(h.floor, 0, HutDecor.Floors.Length - 1) : 0;
            int r = h != null ? Mathf.Clamp(h.rug, 0, HutDecor.Rugs.Length - 1) : 0;
            int c = h != null ? Mathf.Clamp(h.curtains, 0, HutDecor.Curtains.Length - 1) : 0;
            var ws = HutDecor.Walls[w];
            SetMat(wall[0], ws.a); SetMat(wall[1], ws.b); SetMat(wall[2], ws.c);
            var fs = HutDecor.Floors[f];
            SetMat(floor[0], fs.a); SetMat(floor[1], fs.b); SetMat(floor[2], fs.c);
            for (int i = 0; i < rugs.Length; i++)
                if (rugs[i] != null && rugs[i].activeSelf != (i == r)) rugs[i].SetActive(i == r);
            if (curtain != null)
            {
                bool on = c != HutDecor.CurtainsNone;
                if (curtain.gameObject.activeSelf != on) curtain.gameObject.SetActive(on);
                if (on) SetMat(curtain, HutDecor.Curtains[c].a);
            }
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] == null) continue;
                bool on = h != null && h.placed != null && h.placed.Contains(HutDecor.Items[i].id);
                if (items[i].activeSelf != on) items[i].SetActive(on);
            }
        }

        static void SetMat(Renderer r, Color c)
        {
            if (r == null) return;
            var m = PropMats.Flat(c);
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }

        // ------------------------------------------------------------------ test / screenshot accessors
        static Color ColorOf(Renderer r) => r != null && r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : Color.clear;
        public Color WallColor => ColorOf(wall[0]);
        public Color FloorColor => ColorOf(floor[0]);
        public Color CurtainColor => ColorOf(curtain);
        public bool CurtainsShown => curtain != null && curtain.gameObject.activeSelf;

        public int ActiveRug
        {
            get
            {
                for (int i = 0; i < rugs.Length; i++) if (rugs[i] != null && rugs[i].activeSelf) return i;
                return HutDecor.RugNone;
            }
        }

        public bool ItemShown(string id)
        {
            int i = HutDecor.ItemIndex(id);
            return i >= 0 && items[i] != null && items[i].activeSelf;
        }

        public bool ItemBuilt(string id)
        {
            int i = HutDecor.ItemIndex(id);
            return i >= 0 && items[i] != null;
        }

        public Vector3 W(Vector3 local) => origin + local;

        // ================================================================== building
        /// Per-object batch in the hut's local frame (flushed into its own toggled object).
        sealed class Kit
        {
            public readonly PropBatch batch = new PropBatch(0f);
            readonly Matrix4x4 to;
            public readonly Vector3 o;
            public Kit(Vector3 origin) { o = origin; to = Matrix4x4.Translate(origin); }
            public PropMeshBuilder B(Material m)
            {
                var b = batch.B(m);
                b.M = to;
                return b;
            }
            public void Flush(Transform parent, string name)
            {
                if (!batch.IsEmpty) batch.Flush(parent, name, ShadowCastingMode.On, 0f);
            }
        }

        const float IX = 4.43f, IZ = 4.93f, Lin = 0.024f;

        /// Builds every customizable part under the hut root (at the world origin) and returns the component.
        /// holes: the wall openings per wall (west, east, south, north) as (s0, s1, v0, v1) – PropHut.Holes.
        public static PropHutDecor Build(Transform root, Vector3 o, IList<Vector4>[] holes)
        {
            var go = new GameObject("HutDecor");
            go.transform.SetParent(root, false);
            var d = go.AddComponent<PropHutDecor>();
            d.origin = o;
            try { d.BuildLining(holes); } catch (System.Exception e) { Debug.LogError("Sealebrities props: hut lining failed: " + e); }
            try { d.BuildFloor(); } catch (System.Exception e) { Debug.LogError("Sealebrities props: hut floor failed: " + e); }
            try { d.BuildCurtains(holes); } catch (System.Exception e) { Debug.LogError("Sealebrities props: hut curtains failed: " + e); }
            for (int i = 0; i < d.rugs.Length; i++)
            {
                try { d.rugs[i] = d.BuildRug(i); } catch (System.Exception e) { Debug.LogError("Sealebrities props: hut rug " + i + " failed: " + e); }
            }
            for (int i = 0; i < d.items.Length; i++)
            {
                try { d.items[i] = d.BuildItem(HutDecor.Items[i].id); } catch (System.Exception e) { Debug.LogError("Sealebrities props: hut decor " + HutDecor.Items[i].id + " failed: " + e); }
            }
            d.ApplyNow(true);
            return d;
        }

        MeshRenderer Renderer(string name, PropMeshBuilder b, Color c)
        {
            if (b == null || b.Empty) return null;
            var mesh = b.ToMesh("Hut" + name);
            mesh.RecalculateBounds();
            var mr = PropUtil.MeshObject(transform, name, mesh, PropMats.Flat(c), origin, Quaternion.identity, true);
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            return mr;
        }

        static uint Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)(a * 374761393 + b * 668265263 + c * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        // ---- wall lining: vertical boards 0.16 m on all four inner faces, cut around windows + the door
        void BuildLining(IList<Vector4>[] holes)
        {
            var b = new[] { new PropMeshBuilder(), new PropMeshBuilder(), new PropMeshBuilder() };
            LiningWall(b, true, -IX, 1f, -IZ, IZ, holes[0], 1);
            LiningWall(b, true, IX, -1f, -IZ, IZ, holes[1], 2);
            LiningWall(b, false, -IZ, 1f, -IX + Lin, IX - Lin, holes[2], 3);
            LiningWall(b, false, IZ, -1f, -IX + Lin, IX - Lin, holes[3], 4);
            var sw = HutDecor.Walls[0];
            wall[0] = Renderer("WallPaint0", b[0], sw.a);
            wall[1] = Renderer("WallPaint1", b[1], sw.b);
            wall[2] = Renderer("WallPaint2", b[2], sw.c);
        }

        static readonly List<float> Breaks = new List<float>(16);

        static void LiningWall(PropMeshBuilder[] b, bool alongZ, float face, float inward, float s0, float s1, IList<Vector4> holes, int wallId)
        {
            const float y0 = 0.12f, y1 = 2.6f, colW = 0.16f, gap = 0.004f;
            Breaks.Clear();
            Breaks.Add(s0);
            Breaks.Add(s1);
            for (int i = 0; i < holes.Count; i++)
            {
                if (holes[i].x > s0 && holes[i].x < s1) Breaks.Add(holes[i].x);
                if (holes[i].y > s0 && holes[i].y < s1) Breaks.Add(holes[i].y);
            }
            Breaks.Sort();
            for (int iv = 0; iv + 1 < Breaks.Count; iv++)
            {
                float a = Breaks[iv], e = Breaks[iv + 1];
                if (e - a < 0.01f) continue;
                float mid = (a + e) * 0.5f;
                int hole = -1;
                for (int i = 0; i < holes.Count; i++) if (mid > holes[i].x && mid < holes[i].y) { hole = i; break; }
                int n = Mathf.Max(1, Mathf.RoundToInt((e - a) / colW));
                float w = (e - a) / n;
                for (int k = 0; k < n; k++)
                {
                    float c0 = a + k * w, c1 = c0 + w - gap;
                    uint h = Hash(k + iv * 37, wallId, Mathf.RoundToInt(a * 10f));
                    int v = (int)(h % 3u);
                    float off = ((h >> 8) & 255) / 255f * 0.006f;
                    float depth = face + inward * (Lin * 0.5f + 0.002f + off);
                    if (hole < 0) Board(b[v], alongZ, depth, c0, c1, y0, y1);
                    else
                    {
                        var hv = holes[hole];
                        if (hv.z - y0 > 0.03f) Board(b[v], alongZ, depth, c0, c1, y0, hv.z);
                        if (y1 - hv.w > 0.03f) Board(b[v], alongZ, depth, c0, c1, hv.w, y1);
                    }
                }
            }
        }

        static void Board(PropMeshBuilder b, bool alongZ, float depth, float s0, float s1, float y0, float y1)
        {
            float s = (s0 + s1) * 0.5f, y = (y0 + y1) * 0.5f;
            if (alongZ) b.Box(new Vector3(depth, y, s), new Vector3(Lin, y1 - y0, s1 - s0));
            else b.Box(new Vector3(s, y, depth), new Vector3(s1 - s0, y1 - y0, Lin));
        }

        // ---- floor: 0.22 m boards along z with staggered butt joints, three tones
        void BuildFloor()
        {
            var b = new[] { new PropMeshBuilder(), new PropMeshBuilder(), new PropMeshBuilder() };
            const float z0 = -5.15f, z1 = 5.15f;
            int k = 0;
            for (float x = -4.5f; x < 4.66f; x += 0.25f, k++)
            {
                float z = z0 + (Hash(k, 5, 1) % 100u) / 100f * 2.4f - 2.4f;
                int seg = 0;
                while (z < z1)
                {
                    float len = 2.2f + (Hash(k, seg, 9) % 100u) / 100f * 1.6f;
                    float za = Mathf.Max(z, z0), zb = Mathf.Min(z + len, z1);
                    if (zb - za > 0.05f)
                    {
                        int v = (int)(Hash(k, seg, 3) % 3u);
                        float dy = ((Hash(k, seg, 4) >> 4) & 15) / 15f * 0.006f;
                        b[v].Box(new Vector3(x, -0.06f + dy, (za + zb) * 0.5f), new Vector3(0.22f, 0.12f, zb - za - (zb < z1 ? 0.012f : 0f)));
                    }
                    z += len;
                    seg++;
                }
            }
            var fs = HutDecor.Floors[0];
            floor[0] = Renderer("Floor0", b[0], fs.a);
            floor[1] = Renderer("Floor1", b[1], fs.b);
            floor[2] = Renderer("Floor2", b[2], fs.c);
        }

        // ---- curtains: two gathered panels beside every window (inside)
        void BuildCurtains(IList<Vector4>[] holes)
        {
            var b = new PropMeshBuilder();
            for (int wi = 0; wi < 4; wi++)
            {
                bool alongZ = wi < 2;
                float face = wi == 0 ? -IX : wi == 1 ? IX : wi == 2 ? -IZ : IZ;
                float inward = wi == 0 || wi == 2 ? 1f : -1f;
                var list = holes[wi];
                for (int i = 0; i < list.Count; i++)
                {
                    var h = list[i];
                    if (h.z <= 0.001f) continue;   // the door
                    CurtainPanels(b, alongZ, face, inward, h);
                }
            }
            curtain = Renderer("Curtains", b, HutDecor.Curtains[0].a);
            if (curtain != null) curtain.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void CurtainPanels(PropMeshBuilder b, bool alongZ, float face, float inward, Vector4 h)
        {
            float top = h.w + 0.13f, bottom = h.z - 0.12f;
            Vector3 n = alongZ ? new Vector3(inward, 0f, 0f) : new Vector3(0f, 0f, inward);
            Vector3 P(float s, float y, float d) => alongZ ? new Vector3(face + inward * (0.15f + d), y, s) : new Vector3(s, y, face + inward * (0.15f + d));
            for (int side = 0; side < 2; side++)
            {
                float sA = side == 0 ? h.x - 0.3f : h.y + 0.02f;
                const int folds = 4;
                const float fw = 0.07f, fd = 0.035f;
                for (int f = 0; f < folds; f++)
                {
                    float a = sA + f * fw, e = a + fw;
                    float d0 = f % 2 == 0 ? 0f : fd, d1 = f % 2 == 0 ? fd : 0f;
                    // gathered at the tie-back: the panel narrows toward the window edge at 1/3 height
                    float tie = bottom + (top - bottom) * 0.38f;
                    float pull = side == 0 ? -0.04f : 0.04f;
                    b.Quad2(P(a, bottom, d0), P(a + pull * 0.5f, tie, d0), P(e + pull * 0.5f, tie, d1), P(e, bottom, d1), n);
                    b.Quad2(P(a + pull * 0.5f, tie, d0), P(a, top, d0), P(e, top, d1), P(e + pull * 0.5f, tie, d1), n);
                }
                float tieY = bottom + (top - bottom) * 0.38f;
                Vector3 c = P(sA + folds * fw * 0.5f + (side == 0 ? -0.02f : 0.02f), tieY, fd * 0.5f);
                b.Box(c, alongZ ? new Vector3(0.05f, 0.05f, folds * fw + 0.02f) : new Vector3(folds * fw + 0.02f, 0.05f, 0.05f));
            }
        }

        // ---- rugs (living area under the armchair)
        static readonly Vector3 RugC = new Vector3(1.5f, 0f, -2.3f);

        GameObject BuildRug(int i)
        {
            if (i == HutDecor.RugNone) return null;
            var sw = HutDecor.Rugs[i];
            var k = new Kit(origin);
            Material A = PropMats.Flat(sw.a), Bm = PropMats.Flat(sw.b), Cm = PropMats.Flat(sw.c);
            Vector3 c = RugC;
            const float w = 2.4f, dpt = 1.8f;
            switch (i)
            {
                case 0:   // today's stripes
                    k.B(A).Box(c + new Vector3(0f, 0.008f, 0f), new Vector3(w, 0.016f, dpt));
                    for (int s = -1; s <= 1; s++) k.B(Bm).Box(c + new Vector3(s * 0.7f, 0.018f, 0f), new Vector3(0.2f, 0.006f, dpt));
                    break;
                case 1:   // round, concentric rings
                    k.B(A).Cylinder(c, Quaternion.identity, 1.05f, 1.05f, 0.014f, 14, false, true);
                    k.B(Bm).Cylinder(c, Quaternion.identity, 0.8f, 0.8f, 0.017f, 14, false, true);
                    k.B(Cm).Cylinder(c, Quaternion.identity, 0.55f, 0.55f, 0.02f, 14, false, true);
                    k.B(A).Cylinder(c, Quaternion.identity, 0.28f, 0.28f, 0.023f, 12, false, true);
                    break;
                case 2:   // checks
                    k.B(Bm).Box(c + new Vector3(0f, 0.007f, 0f), new Vector3(w, 0.014f, dpt));
                    for (int ix = 0; ix < 6; ix++)
                        for (int iz = 0; iz < 4; iz++)
                            if (((ix + iz) & 1) == 0)
                                k.B(A).Box(c + new Vector3(-w * 0.5f + (ix + 0.5f) * (w / 6f), 0.016f, -dpt * 0.5f + (iz + 0.5f) * (dpt / 4f)), new Vector3(w / 6f, 0.006f, dpt / 4f));
                    break;
                case 3:   // zigzag kilim with tassels
                    k.B(A).Box(c + new Vector3(0f, 0.007f, 0f), new Vector3(w, 0.014f, dpt));
                    for (int row = -1; row <= 1; row++)
                        for (int s = 0; s < 8; s++)
                        {
                            float x0 = -w * 0.5f + 0.15f + s * 0.2625f, x1 = x0 + 0.2625f;
                            float zA = row * 0.5f + (s % 2 == 0 ? -0.1f : 0.1f), zB = row * 0.5f + (s % 2 == 0 ? 0.1f : -0.1f);
                            k.B(Bm).Beam(c + new Vector3(x0, 0.017f, zA), c + new Vector3(x1, 0.017f, zB), 0.06f, 0.006f);
                        }
                    k.B(Cm).Box(c + new Vector3(0f, 0.017f, dpt * 0.5f - 0.06f), new Vector3(w, 0.006f, 0.06f));
                    k.B(Cm).Box(c + new Vector3(0f, 0.017f, -dpt * 0.5f + 0.06f), new Vector3(w, 0.006f, 0.06f));
                    var tas = k.B(PropMats.Flat(PropPal.Paper));
                    for (int s = -1; s <= 1; s += 2)
                        for (float z = -dpt * 0.5f + 0.08f; z < dpt * 0.5f - 0.05f; z += 0.12f)
                            tas.Box(c + new Vector3(s * (w * 0.5f + 0.05f), 0.005f, z), new Vector3(0.1f, 0.006f, 0.025f));
                    break;
                default:  // woven straw mat
                    k.B(Bm).Box(c + new Vector3(0f, 0.007f, 0f), new Vector3(w, 0.014f, dpt));
                    k.B(A).Box(c + new Vector3(0f, 0.012f, 0f), new Vector3(w - 0.16f, 0.012f, dpt - 0.16f));
                    for (float z = -dpt * 0.5f + 0.16f; z < dpt * 0.5f - 0.1f; z += 0.13f)
                        k.B(Bm).Box(c + new Vector3(0f, 0.0185f, z), new Vector3(w - 0.2f, 0.003f, 0.02f));
                    break;
            }
            var holder = new GameObject("Rug" + i);
            holder.transform.SetParent(transform, false);
            k.Flush(holder.transform, "Rug" + i);
            holder.SetActive(false);
            return holder;
        }

        // ---- decor items (each at its fixed spot, own colliders, toggled)
        GameObject BuildItem(string id)
        {
            var holder = new GameObject("Decor_" + id);
            holder.transform.SetParent(transform, false);
            var k = new Kit(origin);
            var t = holder.transform;
            switch (id)
            {
                case "palm": Palm(k, t); break;
                case "lights": StringLights(k, holder); break;
                case "surfboard": WallSurfboard(k); break;
                case "records": RecordPlayer(k, t); break;
                case "hangchair": HangingChair(k, t); break;
                case "marlin": Marlin(k); break;
                case "aquarium": Aquarium(k, t); break;
                case "shells": ShellGarland(k); break;
                case "logframe": LogFrame(k); break;
                case "bottles": Bottles(k); break;
                case "finbutton": FinButton(k); break;
            }
            k.Flush(t, "Mesh");
            holder.SetActive(false);
            return holder;
        }

        void Col(Transform parent, Vector3 local, Vector3 size, Quaternion rot) => PropCol.Box(parent, origin + local, size, rot, "DecorCol");

        static void Leaf(Kit k, Vector3 root, Vector3 dir, float len, float width, float lift, Material top, Material under)
        {
            dir.y = 0f;
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            const int n = 4;
            Vector3 prevL = root, prevR = root, prevC = root;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                Vector3 c = root + dir * (len * t) + Vector3.up * (len * (lift * t - 0.9f * t * t));
                float wd = width * Mathf.Sin(Mathf.Min(1f, t * 1.15f) * Mathf.PI) * 0.5f;
                Vector3 l = c + side * wd + Vector3.down * (wd * 0.3f), r = c - side * wd + Vector3.down * (wd * 0.3f);
                k.B(top).Face3(prevC, prevL, l, Vector3.up); k.B(top).Face3(prevC, l, c, Vector3.up);
                k.B(top).Face3(prevC, c, r, Vector3.up); k.B(top).Face3(prevC, r, prevR, Vector3.up);
                k.B(under).Face3(prevC, prevL, l, Vector3.down); k.B(under).Face3(prevC, l, c, Vector3.down);
                k.B(under).Face3(prevC, c, r, Vector3.down); k.B(under).Face3(prevC, r, prevR, Vector3.down);
                prevL = l; prevR = r; prevC = c;
            }
        }

        void Palm(Kit k, Transform t)
        {
            Vector3 p = new Vector3(-2.62f, 0f, -4.42f);
            var pot = PropMats.Flat(LowPoly.Hex("#B8644A"));
            k.B(pot).Cylinder(p, Quaternion.identity, 0.16f, 0.21f, 0.4f, 8, true, false);
            k.B(PropMats.Flat(PropMats.Value(LowPoly.Hex("#B8644A"), 0.88f))).Cylinder(p + new Vector3(0f, 0.36f, 0f), Quaternion.identity, 0.225f, 0.225f, 0.07f, 8, false, true);
            k.B(PropMats.Flat(PropPal.WoodWet)).Cylinder(p + new Vector3(0f, 0.4f, 0f), Quaternion.identity, 0.19f, 0.19f, 0.025f, 8, false, true);
            Vector3 a = p + new Vector3(0f, 0.42f, 0f), m = p + new Vector3(0.04f, 0.85f, 0.02f), top = p + new Vector3(0.02f, 1.3f, 0.05f);
            var trunk = PropMats.Flat(PropPal.PalmTrunk);
            k.B(trunk).Rod(a, m, 0.055f, 0.045f, 6);
            k.B(trunk).Rod(m, top, 0.045f, 0.035f, 6);
            Material lt = PropMats.Flat(PropPal.PalmLeaf), ld = PropMats.Flat(PropPal.PalmLeafDark);
            for (int i = 0; i < 7; i++)
            {
                float ang = i * 51.4f + 10f;
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * Vector3.forward;
                // keep the fronds off the wall (south) and the workbench (west)
                float len = dir.z < -0.4f || dir.x < -0.6f ? 0.42f : 0.62f;
                Leaf(k, top, dir, len, 0.2f, 0.9f, lt, ld);
            }
            Col(t, new Vector3(p.x, 0.25f, p.z), new Vector3(0.42f, 0.5f, 0.42f), Quaternion.identity);
        }

        void StringLights(Kit k, GameObject holder)
        {
            var bulb = PropMats.EmissiveInstance(LowPoly.Hex("#F6E3A8"), Color.black);
            var wire = PropMats.Ink;
            Color[] tints = { LowPoly.Hex("#F6E3A8") };
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * (IZ - 0.07f);
                const int hooks = 5;
                for (int seg = 0; seg + 1 < hooks; seg++)
                {
                    float xa = -4.2f + seg * 2.1f, xb = xa + 2.1f;
                    Vector3 prev = new Vector3(xa, 2.56f, z);
                    const int n = 8;
                    for (int i = 1; i <= n; i++)
                    {
                        float u = i / (float)n;
                        Vector3 q = new Vector3(Mathf.Lerp(xa, xb, u), 2.56f - 0.11f * Mathf.Sin(u * Mathf.PI), z);
                        k.B(wire).Rod(prev, q, 0.006f, 0.006f, 3);
                        if (i < n) k.B(bulb).Ico(q - new Vector3(0f, 0.035f, 0f), new Vector3(0.028f, 0.04f, 0.028f), Quaternion.identity, 0);
                        prev = q;
                    }
                    k.B(PropMats.DarkMetal).Box(new Vector3(xa, 2.57f, z), new Vector3(0.02f, 0.03f, 0.03f));
                }
            }
            PropNightLight.Attach(holder, null, bulb, new Color(2.6f, 1.9f, 0.9f), 0.1f, 0.4f, false);
        }

        void WallSurfboard(Kit k)
        {
            Vector3 c = new Vector3(IX - Lin - 0.075f, 2.42f, 0.9f);
            Quaternion r = Quaternion.LookRotation(Vector3.forward, Vector3.left);
            PropBeach.Surfboard(k.batch, k.o + c, r, PropMats.Flat(LowPoly.Hex("#F28C4A")));
            var peg = k.B(PropMats.WoodDark);
            peg.Box(new Vector3(IX - Lin - 0.03f, 2.2f, 0.35f), new Vector3(0.06f, 0.04f, 0.05f));
            peg.Box(new Vector3(IX - Lin - 0.03f, 2.2f, 1.45f), new Vector3(0.06f, 0.04f, 0.05f));
            // a painted stripe so it reads as "decor", not the rack board
            k.B(PropMats.Flat(PropPal.Turquoise)).Box(c + new Vector3(-0.04f, 0f, -0.1f), new Vector3(0.004f, 0.5f, 0.08f));
        }

        void RecordPlayer(Kit k, Transform t)
        {
            Vector3 p = new Vector3(-IX + Lin + 0.24f, 0f, 0.45f);
            var wood = k.B(PropMats.WoodLight);
            wood.Box(p + new Vector3(0f, 0.58f, 0f), new Vector3(0.46f, 0.04f, 0.42f));
            wood.Box(p + new Vector3(0f, 0.22f, 0f), new Vector3(0.42f, 0.03f, 0.38f));
            var leg = k.B(PropMats.Wood);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    leg.Box(p + new Vector3(sx * 0.19f, 0.28f, sz * 0.17f), new Vector3(0.04f, 0.56f, 0.04f));
            // vinyl sleeves on the lower board
            Color[] sleeves = { LowPoly.Hex("#E07A62"), LowPoly.Hex("#3FA9B5"), LowPoly.Hex("#F2C94C"), LowPoly.Hex("#8E5CC2"), LowPoly.Hex("#EDE6DA") };
            for (int i = 0; i < 5; i++)
                k.B(PropMats.Flat(sleeves[i])).Box(p + new Vector3(-0.14f + i * 0.06f, 0.39f, 0f), new Vector3(0.012f, 0.3f, 0.3f), Quaternion.Euler(0f, 0f, (i - 2) * 4f));
            // the player: case, platter, record, tone arm
            k.B(PropMats.Flat(LowPoly.Hex("#7A4448"))).Box(p + new Vector3(0f, 0.65f, 0f), new Vector3(0.4f, 0.1f, 0.34f));
            k.B(PropMats.Metal).Cylinder(p + new Vector3(0.02f, 0.7f, -0.02f), Quaternion.identity, 0.14f, 0.14f, 0.012f, 12, false, true);
            k.B(PropMats.Ink).Cylinder(p + new Vector3(0.02f, 0.712f, -0.02f), Quaternion.identity, 0.13f, 0.13f, 0.006f, 12, false, true);
            k.B(PropMats.Flat(PropPal.Sun)).Cylinder(p + new Vector3(0.02f, 0.718f, -0.02f), Quaternion.identity, 0.04f, 0.04f, 0.002f, 8, false, true);
            k.B(PropMats.Metal).Rod(p + new Vector3(0.15f, 0.72f, 0.13f), p + new Vector3(0.07f, 0.725f, -0.06f), 0.008f, 0.006f, 4);
            k.B(PropMats.Metal).Cylinder(p + new Vector3(0.15f, 0.7f, 0.13f), Quaternion.identity, 0.02f, 0.02f, 0.03f, 6, false, true);
            Col(t, p + new Vector3(0f, 0.36f, 0f), new Vector3(0.46f, 0.72f, 0.42f), Quaternion.identity);
        }

        void HangingChair(Kit k, Transform t)
        {
            Vector3 p = new Vector3(1.8f, 0f, -0.85f);
            Vector3 face = new Vector3(3.4f - p.x, 0f, -4f - p.z).normalized;
            Quaternion r = Quaternion.LookRotation(face, Vector3.up);
            var rope = k.B(PropMats.Rope);
            Vector3 ring = p + new Vector3(0f, 1.78f, 0f);
            rope.Rod(ring, p + new Vector3(0f, 2.68f, 0f), 0.015f, 0.015f, 4);
            k.B(PropMats.DarkMetal).Torus(ring, Quaternion.Euler(90f, 0f, 0f), 0.06f, 0.012f, 8, 3);
            var rattan = k.B(PropMats.Flat(LowPoly.Hex("#C9A46E")));
            rattan.Cylinder(p + new Vector3(0f, 0.45f, 0f), Quaternion.identity, 0.3f, 0.42f, 0.1f, 10, true, true);
            // egg-shaped back of curved slats (open toward the front)
            for (int i = 0; i <= 10; i++)
            {
                float a = Mathf.Lerp(70f, 290f, i / 10f) * Mathf.Deg2Rad;
                Vector3 dl = new Vector3(Mathf.Sin(a), 0f, -Mathf.Cos(a));     // local: -z = back
                Vector3 d = r * dl;
                Vector3 q0 = p + d * 0.42f + Vector3.up * 0.55f;
                Vector3 q1 = p + d * 0.47f + Vector3.up * 0.95f;
                Vector3 q2 = p + d * 0.33f + Vector3.up * 1.42f;
                rattan.Rod(q0, q1, 0.018f, 0.018f, 4);
                rattan.Rod(q1, q2, 0.018f, 0.016f, 4);
                rattan.Rod(q2, ring, 0.016f, 0.012f, 4);
            }
            for (int ring2 = 0; ring2 < 2; ring2++)
            {
                float y = ring2 == 0 ? 0.95f : 1.25f, rad = ring2 == 0 ? 0.47f : 0.42f;
                for (int i = 0; i < 10; i++)
                {
                    float a0 = Mathf.Lerp(70f, 290f, i / 10f) * Mathf.Deg2Rad, a1 = Mathf.Lerp(70f, 290f, (i + 1) / 10f) * Mathf.Deg2Rad;
                    rattan.Rod(p + r * new Vector3(Mathf.Sin(a0), 0f, -Mathf.Cos(a0)) * rad + Vector3.up * y, p + r * new Vector3(Mathf.Sin(a1), 0f, -Mathf.Cos(a1)) * rad + Vector3.up * y, 0.014f, 0.014f, 3);
                }
            }
            k.B(PropMats.CushionA).Cylinder(p + new Vector3(0f, 0.55f, 0f), Quaternion.identity, 0.34f, 0.32f, 0.1f, 10, false, true);
            k.B(PropMats.CushionB).Box(p + r * new Vector3(0f, 0.78f, -0.26f), new Vector3(0.36f, 0.3f, 0.12f), r * Quaternion.Euler(-14f, 0f, 0f));
            Col(t, p + new Vector3(0f, 0.95f, 0f), new Vector3(0.9f, 1.05f, 0.9f), Quaternion.identity);
        }

        void Marlin(Kit k)
        {
            float z = -IZ + Lin;
            Vector3 c = new Vector3(-1.0f, 2.22f, z);
            k.B(PropMats.WoodDark).Box(c + new Vector3(0f, 0f, 0.02f), new Vector3(0.62f, 0.3f, 0.04f));
            k.B(PropMats.Wood).Box(c + new Vector3(0f, 0f, 0.045f), new Vector3(0.5f, 0.22f, 0.01f));
            Color navy = LowPoly.Hex("#2E4E7A"), silver = LowPoly.Hex("#C8CDD3");
            Vector3 body = c + new Vector3(0f, 0f, 0.12f);
            k.B(PropMats.Flat(navy)).Ico(body + new Vector3(0f, 0.025f, 0f), new Vector3(0.48f, 0.12f, 0.06f), Quaternion.identity, 1);
            k.B(PropMats.Flat(silver)).Ico(body - new Vector3(0f, 0.035f, 0f), new Vector3(0.44f, 0.09f, 0.055f), Quaternion.identity, 1);
            // bill, sail fin, tail, pectoral fin
            k.B(PropMats.Flat(navy)).Rod(body + new Vector3(0.42f, 0.01f, 0f), body + new Vector3(0.85f, 0.03f, 0f), 0.022f, 0.006f, 5);
            var fin = k.B(PropMats.Flat(PropMats.Value(navy, 0.85f)));
            fin.Tri2(body + new Vector3(0.28f, 0.1f, 0f), body + new Vector3(-0.2f, 0.1f, 0f), body + new Vector3(0.12f, 0.36f, 0f), Vector3.back);
            fin.Tri2(body + new Vector3(-0.2f, 0.1f, 0f), body + new Vector3(-0.32f, 0.08f, 0f), body + new Vector3(0.12f, 0.36f, 0f), Vector3.back);
            fin.Tri2(body + new Vector3(-0.44f, 0f, 0f), body + new Vector3(-0.72f, 0.22f, 0f), body + new Vector3(-0.6f, 0f, 0f), Vector3.back);
            fin.Tri2(body + new Vector3(-0.44f, 0f, 0f), body + new Vector3(-0.6f, 0f, 0f), body + new Vector3(-0.72f, -0.2f, 0f), Vector3.back);
            fin.Tri2(body + new Vector3(0.15f, -0.04f, 0.04f), body + new Vector3(0.0f, -0.05f, 0.05f), body + new Vector3(0.02f, -0.16f, 0.07f), Vector3.back);
            k.B(PropMats.Ink).Ico(body + new Vector3(0.33f, 0.02f, 0.045f), new Vector3(0.018f, 0.018f, 0.018f), Quaternion.identity, 0);
        }

        void Aquarium(Kit k, Transform t)
        {
            Vector3 a = new Vector3(1.7f, 0f, -4.6f);
            k.B(PropMats.WoodDark).Box(a + new Vector3(0f, 0.27f, 0f), new Vector3(0.95f, 0.54f, 0.42f));
            k.B(PropMats.Wood).Box(a + new Vector3(-0.22f, 0.27f, 0.212f), new Vector3(0.42f, 0.44f, 0.01f));
            k.B(PropMats.Wood).Box(a + new Vector3(0.22f, 0.27f, 0.212f), new Vector3(0.42f, 0.44f, 0.01f));
            k.B(PropMats.Metal).Box(a + new Vector3(-0.05f, 0.3f, 0.22f), new Vector3(0.02f, 0.08f, 0.015f));
            k.B(PropMats.Metal).Box(a + new Vector3(0.05f, 0.3f, 0.22f), new Vector3(0.02f, 0.08f, 0.015f));
            k.B(PropMats.Ink).Box(a + new Vector3(0f, 0.56f, 0f), new Vector3(0.92f, 0.04f, 0.38f));
            k.B(PropMats.Ink).Box(a + new Vector3(0f, 0.985f, 0f), new Vector3(0.92f, 0.03f, 0.38f));
            k.B(PropMats.Flat(PropPal.Sand)).Box(a + new Vector3(0f, 0.6f, 0f), new Vector3(0.86f, 0.05f, 0.32f));
            k.B(PropMats.PoolWater).Box(a + new Vector3(0f, 0.77f, 0f), new Vector3(0.86f, 0.3f, 0.32f));
            k.B(PropMats.Glass).Box(a + new Vector3(0f, 0.775f, 0f), new Vector3(0.9f, 0.4f, 0.36f));
            var weed = k.B(PropMats.Flat(PropPal.FoliageLight));
            for (int i = 0; i < 5; i++)
            {
                Vector3 s = a + new Vector3(-0.35f + i * 0.17f, 0.62f, -0.08f + (i % 2) * 0.1f);
                weed.Rod(s, s + new Vector3(0.02f * (i - 2), 0.12f + (i % 3) * 0.05f, 0f), 0.015f, 0.004f, 3);
            }
            k.B(PropMats.Rock).Ico(a + new Vector3(0.25f, 0.65f, 0.02f), new Vector3(0.07f, 0.06f, 0.06f), Quaternion.identity, 0);
            Color[] fish = { LowPoly.Hex("#F28C4A"), LowPoly.Hex("#F2C94C"), LowPoly.Hex("#3FA9B5") };
            for (int i = 0; i < 3; i++)
            {
                Vector3 f = a + new Vector3(-0.2f + i * 0.2f, 0.74f + (i % 2) * 0.08f, 0.04f - i * 0.03f);
                k.B(PropMats.Flat(fish[i])).Ico(f, new Vector3(0.045f, 0.025f, 0.012f), Quaternion.identity, 0);
                k.B(PropMats.Flat(fish[i])).Face3(f + new Vector3(-0.04f, 0f, 0f), f + new Vector3(-0.07f, 0.02f, 0f), f + new Vector3(-0.07f, -0.02f, 0f), Vector3.back);
            }
            Col(t, a + new Vector3(0f, 0.5f, 0f), new Vector3(0.95f, 1.0f, 0.42f), Quaternion.identity);
        }

        void ShellGarland(Kit k)
        {
            float x = IX - Lin - 0.03f;
            Vector3 a = new Vector3(x, 2.3f, -3.55f), b = new Vector3(x, 2.3f, -1.65f);
            var twine = k.B(PropMats.Rope);
            Color[] cols = { PropPal.Sun, PropPal.FlowerPink, PropPal.White, PropPal.CoralOrange, PropPal.Paper };
            const int n = 14;
            Vector3 prev = a;
            for (int i = 1; i <= n; i++)
            {
                float u = i / (float)n;
                Vector3 q = Vector3.Lerp(a, b, u) - Vector3.up * (0.2f * Mathf.Sin(u * Mathf.PI));
                twine.Rod(prev, q, 0.006f, 0.006f, 3);
                if (i < n)
                {
                    Vector3 s = q - new Vector3(0.01f, 0.07f, 0f);
                    twine.Rod(q, s + Vector3.up * 0.03f, 0.004f, 0.004f, 3);
                    k.B(PropMats.Flat(cols[i % cols.Length])).Cylinder(s, Quaternion.Euler(0f, 0f, 90f) * Quaternion.Euler(0f, i * 40f, 0f), 0.045f, 0.0f, 0.03f, 6, true, false);
                }
                prev = q;
            }
            k.B(PropMats.DarkMetal).Box(a, new Vector3(0.03f, 0.02f, 0.02f));
            k.B(PropMats.DarkMetal).Box(b, new Vector3(0.03f, 0.02f, 0.02f));
        }

        void LogFrame(Kit k)
        {
            float x = -IX + Lin + 0.02f;
            Vector3 c = new Vector3(x, 1.65f, -2.75f);
            var fr = k.B(PropMats.WoodDark);
            fr.Box(c + new Vector3(0f, 0.31f, 0f), new Vector3(0.04f, 0.05f, 1.0f));
            fr.Box(c - new Vector3(0f, 0.31f, 0f), new Vector3(0.04f, 0.05f, 1.0f));
            fr.Box(c + new Vector3(0f, 0f, 0.48f), new Vector3(0.04f, 0.67f, 0.05f));
            fr.Box(c - new Vector3(0f, 0f, 0.48f), new Vector3(0.04f, 0.67f, 0.05f));
            k.B(PropMats.Flat(LowPoly.Hex("#2F4A5E"))).Box(c - new Vector3(0.01f, 0f, 0f), new Vector3(0.015f, 0.58f, 0.92f));
            var page = k.B(PropMats.Flat(PropPal.Paper));
            var ink = k.B(PropMats.Flat(LowPoly.Hex("#7A6A5A")));
            for (int i = 0; i < 3; i++)
            {
                Vector3 pc = c + new Vector3(0.008f, 0.01f * (i - 1), -0.29f + i * 0.29f);
                Quaternion r = Quaternion.Euler((i - 1) * 4f, 0f, 0f);
                page.Box(pc, new Vector3(0.006f, 0.42f, 0.22f), r);
                for (int l = 0; l < 6; l++) ink.Box(pc + r * new Vector3(0.004f, 0.14f - l * 0.055f, -0.01f), new Vector3(0.002f, 0.008f, 0.15f - (l % 3) * 0.03f), r);
            }
        }

        void Bottles(Kit k)
        {
            float z = IZ - 0.06f;
            var glass = PropMats.BottleGlass;
            for (int i = 0; i < 5; i++)
            {
                Vector3 p = new Vector3(-0.58f + i * 0.17f, 1.0f, z);
                float h = 0.18f + (i % 3) * 0.04f;
                k.B(glass).Cylinder(p, Quaternion.identity, 0.035f, 0.035f, h, 6, true, false);
                k.B(glass).Cylinder(p + Vector3.up * h, Quaternion.identity, 0.035f, 0.014f, 0.05f, 6);
                k.B(PropMats.Wood).Cylinder(p + Vector3.up * (h + 0.05f), Quaternion.identity, 0.015f, 0.015f, 0.025f, 5, false, true);
                k.B(PropMats.Flat(PropPal.Paper)).Cylinder(p + new Vector3(0f, 0.03f, 0f), Quaternion.Euler(0f, 0f, 70f), 0.012f, 0.012f, 0.11f, 5, true, true);
            }
        }

        void FinButton(Kit k)
        {
            float z = IZ - Lin;
            Vector3 c = new Vector3(-2.6f, 1.58f, z);
            k.B(PropMats.WoodDark).Box(c - new Vector3(0f, 0f, 0.02f), new Vector3(0.4f, 0.52f, 0.03f));
            k.B(PropMats.Flat(LowPoly.Hex("#C8CDD3"))).Box(c - new Vector3(0f, 0f, 0.04f), new Vector3(0.32f, 0.42f, 0.012f));
            var mark = k.B(PropMats.Flat(PropPal.Turquoise));
            Vector3 m = c - new Vector3(0f, -0.04f, 0.05f);
            mark.Face3(m + new Vector3(-0.08f, -0.08f, 0f), m + new Vector3(0.09f, -0.08f, 0f), m + new Vector3(-0.02f, 0.11f, 0f), Vector3.back);
            k.B(PropMats.Ink).Box(c + new Vector3(0f, -0.15f, -0.05f), new Vector3(0.2f, 0.025f, 0.004f));
        }
    }

    /// „E – Einrichten“: the furnishing catalogue (clipboard) on the hut desk – opens the UI (HutDecor.OpenUI).
    public sealed class PropHutCatalog : MonoBehaviour, IInteractable
    {
        float last = -10f;

        public string GetPrompt() => CanInteract() ? PropPrompt.Interact("Einrichten", "Furnish") : null;

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && !UIState.DecorOpen && Time.unscaledTime - last > 0.6f;

        public void Interact()
        {
            if (!CanInteract()) return;
            last = Time.unscaledTime;
            HutDecor.OpenUI();
        }
    }
}
