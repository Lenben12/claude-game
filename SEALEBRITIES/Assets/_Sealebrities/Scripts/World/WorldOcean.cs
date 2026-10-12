using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Low-poly ocean ("How to Fish": steel-blue faceted sea; round 2 Q4: a wide turquoise lagoon ring around the main
    /// island, then deep blue; at night a calmer, darker and less saturated navy instead of a vivid blue).
    /// A faceted near grid (3 m triangles, unwelded, flat normals from Waves.HeightAt) follows the camera, snapped to 12 m
    /// so every vertex always samples the same world position; a flat 12 m far ring and plain outer rings reach the horizon.
    /// Colour and opacity come from ONE small ramp texture: every vertex gets uv.x = WorldWaterMap ramp coordinate of the
    /// seafloor depth at its own position (smooth depth colour, no classes, no squares) and every near triangle a
    /// world-anchored uv.y row (±8 % facet brightness). No colliders: boats and players sample Waves directly.
    /// Round 3 (GDD §19 "the water must hide what is under it"): the opacity also rises with the viewing angle – a
    /// Fresnel term, the longer refracted path through the water and a grazing cut-off (ViewOpacity) – so from the
    /// beach, the dock or the boat only the water near the viewer shows the lagoon floor; further out the sea is opaque
    /// and reflective and the reef, the seabed and the fish below stay hidden. The ramp texture holds, per facet
    /// brightness row, two texels along y (straight-down alpha → opaque); alpha is linear in between, so a per-vertex
    /// uv.y picks the exact opacity. The view part is refreshed when the camera moved 2.5 m or changed height (and on
    /// every grid snap), only above water – seen from below the ocean uses its own material and stays as it was.
    [DefaultExecutionOrder(100)]
    public class WorldOcean : MonoBehaviour
    {
        [StructLayout(LayoutKind.Sequential)]
        struct OceanVertex
        {
            public Vector3 pos;
            public Vector3 nrm;
        }

        public const float Spacing = 3f;
        public const int Cells = 80;                        // 240 x 240 m near grid
        public const float HalfSize = Cells * Spacing * 0.5f;
        public const float SnapStep = 12f;                  // multiple of Spacing and FarCell
        public const float FarCell = 12f;
        public const int FarCells = 100;                    // 1200 x 1200 m far ring (hole = near grid)
        public const float FarHalf = FarCells * FarCell * 0.5f;
        public const float FarLevel = -0.05f;
        public const int RenderQueue = 2980;                // before regular transparents (particles above water stay on top)
        public const int RampWidth = 64;
        /// Facet brightness variation levels in the ramp: full, half, none (grid edge, far ring, outer rings).
        const int KeepLevels = 3;
        /// Texels per facet block along y: straight-down alpha, fully opaque.
        const int OpacityTexels = 2;
        static int FacetCount => WorldWaterMap.FacetRows.Length;
        static int RampRows => KeepLevels * FacetCount * OpacityTexels;
        static int AverageBlock => (KeepLevels - 1) * FacetCount;
        /// Lowest eye height above the sea used for the viewing angle (swimming with the head out of the water).
        public const float MinEye = 0.35f;
        const float ViewMove = 2.5f;                        // camera travel that re-evaluates the near view opacity

        const float FadeStart = 64f, FadeEnd = 112f;        // wave amplitude fade (meters from grid center)
        const float FacetFadeStart = 78f, FacetFadeEnd = 112f;
        const float InnerRadius = 58f;                      // displaced every frame; the rest every second frame
        const float Smoothness = 0.55f;

        // seen from below: a bright but saturated turquoise ceiling (the old #9FE4EC α 0.70 read milky, #A9E2EC on screen)
        // (second pass: #6CCFDD α 0.58 still measured #93DBDF – darker, more saturated)
        static readonly Color UnderColor = new Color(0.298f, 0.737f, 0.816f, 0.52f);   // #4CBCD0
        static readonly Color UnderGlow = new Color(0.078f, 0.416f, 0.471f);           // #146A78
        // Testversion-3 feedback "das Nachtmeer ist knallig blau": at night the surface albedo is darkened and loses more
        // blue than red/green (a calm grey-navy under the moon), and the glints get softer.
        static readonly Color NightTint = new Color(0.80f, 0.80f, 0.68f, 1f);
        const float NightSmoothness = 0.40f;

        WorldWaterMap water;
        Mesh nearMesh, farMesh, outerMesh;
        Transform nearT, farT, outerT;
        MeshRenderer nearR, farR, outerR;
        Material topMat, underMat;
        Texture2D ramp;

        // near grid: welded heights + unwelded, flat-shaded render vertices
        float[] gridY, fade;
        int[] innerIdx, outerIdx;
        int[] triGrid;          // 3 grid indices per triangle (dynamic triangles first)
        int dynTris;            // triangles with at least one displaced corner
        float[] triLX, triLZ;   // local x/z per render vertex
        OceanVertex[] vbuf;     // interleaved position + normal, uploaded in place (dynamic range only)
        Vector2[] nearUv;       // stream 1: x = depth (on snap), y = facet block + view opacity (on view refresh)
        float[] gridU;          // ramp coordinate per near grid vertex (refreshed on snap)
        float[] gridOp;         // view opacity boost per near grid vertex (scratch)
        int[] triCellI, triCellJ, triHalf;
        float[] triFacetKeep;   // 1 = full facet brightness variation .. 0 = average (grid edge)
        byte[] triBlock;        // ramp facet block per triangle (refreshed on snap)

        // far ring (welded)
        Vector3[] farLocal;
        Vector2[] farUv;
        float[] farU;

        float snapX = float.NaN, snapZ = float.NaN;
        // last view-opacity evaluation
        float viewX, viewZ, viewEye = -1f, viewTime = -100f;
        bool viewDirty = true;
        int frame;
        bool under;
        int idEmission;
        Camera cam;

        public bool CameraUnderwater => under;
        /// The colour ramp (64 x 24: depth along x; facet brightness blocks of [straight-down alpha, opaque] along y).
        public Texture2D Ramp => ramp;
        /// Eye height above the sea used by the last view-opacity refresh (-1 before the first; tests).
        public float ViewEye => viewEye;

        public static WorldOcean Create(Transform parent, WorldWaterMap waterMap)
        {
            var go = new GameObject("Ocean");
            go.transform.SetParent(parent, false);
            var o = go.AddComponent<WorldOcean>();
            o.water = waterMap;
            o.Build();
            return o;
        }

        // ------------------------------------------------------------------ build
        void Build()
        {
            idEmission = Shader.PropertyToID("_EmissionColor");
            idBaseColor = Shader.PropertyToID("_BaseColor");
            idSmoothness = Shader.PropertyToID("_Smoothness");

            ramp = BuildRamp();
            topMat = new Material(MaterialLibrary.Lit) { name = "SB_OceanTop" };
            topMat.SetTexture("_BaseMap", ramp);
            topMat.SetColor("_BaseColor", Color.white);
            topMat.SetFloat("_Smoothness", Smoothness);
            topMat.SetFloat("_Metallic", 0f);
            topMat.SetFloat("_EnvironmentReflections", 1f);
            topMat.SetFloat("_SpecularHighlights", 1f);
            MaterialLibrary.MakeTransparent(topMat, true);
            topMat.renderQueue = RenderQueue;

            underMat = new Material(MaterialLibrary.Lit) { name = "SB_OceanUnder" };
            underMat.SetColor("_BaseColor", UnderColor);
            underMat.SetFloat("_Smoothness", 0.3f);
            underMat.SetFloat("_Metallic", 0f);
            underMat.SetColor("_EmissionColor", UnderGlow);
            underMat.EnableKeyword("_EMISSION");
            underMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            MaterialLibrary.MakeTransparent(underMat, true);
            underMat.renderQueue = RenderQueue;

            BuildNear();
            BuildFar();
            BuildOuter();
            ApplyMaterials(false);
        }

        /// 64 x 24 sRGB ramp: x = sqrt(depth / 35 m) (shore turquoise-blue → steel blue → navy, alpha 0.55 → 1),
        /// y = 12 facet blocks (3 variation levels × the brightness rows 0.915 / 0.97 / 1.03 / 1.085) of two texels each:
        /// the straight-down alpha and fully opaque (same colour). Built once.
        static Texture2D BuildRamp()
        {
            int facets = FacetCount, rows = RampRows;
            var px = new Color32[RampWidth * rows];
            for (int i = 0; i < RampWidth; i++)
            {
                float u = (i + 0.5f) / RampWidth;
                Color c = WorldWaterMap.RampColor(WorldWaterMap.DepthOfU(u));
                for (int kl = 0; kl < KeepLevels; kl++)
                {
                    float keep = 1f - kl / (float)(KeepLevels - 1);
                    for (int r = 0; r < facets; r++)
                    {
                        float k = Mathf.Lerp(1f, WorldWaterMap.FacetRows[r], keep);
                        var col = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
                        int row = (kl * facets + r) * OpacityTexels;
                        px[row * RampWidth + i] = col;
                        col.a = 1f;
                        px[(row + 1) * RampWidth + i] = col;
                    }
                }
            }
            var t = new Texture2D(RampWidth, rows, TextureFormat.RGBA32, false, false)
            {
                name = "SB_OceanRamp",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0,
            };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        /// uv.y inside a facet block: 0 = the straight-down alpha of the depth, 1 = opaque (linear in between).
        static float BlockV(int block, float opaque01) =>
            (block * OpacityTexels + 0.5f + (opaque01 <= 0f ? 0f : opaque01 >= 1f ? 1f : opaque01)) / RampRows;

        // ------------------------------------------------------------------ view opacity (pure math, allocation-free)
        static float[] alphaLut;

        /// Straight-down alpha for a ramp coordinate (the water map stores u in bytes, so 256 entries are exact).
        static float AlphaOfU(float u)
        {
            if (alphaLut == null)
            {
                var lut = new float[256];
                for (int i = 0; i < 256; i++) lut[i] = WorldWaterMap.RampAlpha(WorldWaterMap.DepthOfU(i / 255f));
                alphaLut = lut;
            }
            int k = (int)(u * 255f + 0.5f);
            return alphaLut[k < 0 ? 0 : k > 255 ? 255 : k];
        }

        /// How far (0..1) the surface moves from its straight-down alpha a toward opaque when seen from an eye this high
        /// above the sea at this horizontal distance: Schlick Fresnel (F0 0.02), the longer refracted path through the
        /// water (n = 1.33) and a grazing cut-off below ~2° (scattering / glare – the far sea is never see-through).
        public static float OpaqueBoost(float a, float horizontalDistSq, float eye)
        {
            if (a >= 0.999f) return 1f;
            if (eye < MinEye) eye = MinEye;
            float inv = 1f / Mathf.Sqrt(horizontalDistSq + eye * eye);
            float sinE = eye * inv;                                   // sine of the angle above the surface
            float cosE = Mathf.Sqrt(horizontalDistSq) * inv;
            float m = 1f - sinE, m2 = m * m;
            float fresnel = 0.02f + 0.98f * m2 * m2 * m;
            float s = cosE * 0.75f;                                   // sin of the refracted angle (1 / 1.33)
            float path = 1f / Mathf.Sqrt(1f - s * s);                 // 1 straight down .. 1.51 at grazing
            float g = Mathf.Clamp01((sinE - 0.03f) / 0.11f);
            float graze = g * g * (3f - 2f * g);
            float through = Mathf.Pow(1f - a, path - 1f) * (1f - fresnel) * graze;
            return 1f - through;
        }

        /// Effective opacity of the sea surface over a seafloor depth, seen from an eye height above the sea at a
        /// horizontal distance (what the player gets: the shallow lagoon stays readable near the viewer only).
        public static float ViewOpacity(float seafloorDepth, float horizontalDistance, float eyeHeight)
        {
            float a = WorldWaterMap.RampAlpha(seafloorDepth);
            float b = OpaqueBoost(a, horizontalDistance * horizontalDistance, eyeHeight);
            return 1f - (1f - a) * (1f - b);
        }

        void BuildNear()
        {
            int n = Cells + 1;
            gridY = new float[n * n];
            gridU = new float[n * n];
            gridOp = new float[n * n];
            fade = new float[n * n];
            int innerCount = 0, outerCount = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float lx = i * Spacing - HalfSize, lz = j * Spacing - HalfSize;
                    float d = Mathf.Sqrt(lx * lx + lz * lz);
                    float f = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeStart, FadeEnd, d));
                    fade[k] = f;
                    gridY[k] = FarLevel;
                    if (f > 0f) { if (d < InnerRadius) innerCount++; else outerCount++; }
                }
            innerIdx = new int[innerCount];
            outerIdx = new int[outerCount];
            innerCount = outerCount = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float lx = i * Spacing - HalfSize, lz = j * Spacing - HalfSize;
                    float d = Mathf.Sqrt(lx * lx + lz * lz);
                    if (fade[k] > 0f) { if (d < InnerRadius) innerIdx[innerCount++] = k; else outerIdx[outerCount++] = k; }
                }

            // triangles, alternating diagonals; the cell parity is world-anchored because the grid snaps by 4 cells
            int triCount = Cells * Cells * 2;
            var tmpGrid = new int[triCount * 3];
            var tmpI = new int[triCount];
            var tmpJ = new int[triCount];
            var tmpH = new int[triCount];
            var dynamic = new bool[triCount];
            int t = 0;
            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                    for (int h = 0; h < 2; h++)
                    {
                        int p0, p1, p2;
                        if (((i + j) & 1) == 0) { if (h == 0) { p0 = a; p1 = c; p2 = e; } else { p0 = a; p1 = e; p2 = b; } }
                        else { if (h == 0) { p0 = a; p1 = c; p2 = b; } else { p0 = b; p1 = c; p2 = e; } }
                        tmpGrid[t * 3] = p0; tmpGrid[t * 3 + 1] = p1; tmpGrid[t * 3 + 2] = p2;
                        tmpI[t] = i; tmpJ[t] = j; tmpH[t] = h;
                        dynamic[t] = fade[p0] > 0f || fade[p1] > 0f || fade[p2] > 0f;
                        t++;
                    }
                }

            // dynamic triangles first so only the front of the vertex buffer is re-uploaded each frame
            triGrid = new int[triCount * 3];
            triCellI = new int[triCount]; triCellJ = new int[triCount]; triHalf = new int[triCount];
            triFacetKeep = new float[triCount];
            triBlock = new byte[triCount];
            int w = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int s = 0; s < triCount; s++)
                {
                    if (dynamic[s] != (pass == 0)) continue;
                    triGrid[w * 3] = tmpGrid[s * 3]; triGrid[w * 3 + 1] = tmpGrid[s * 3 + 1]; triGrid[w * 3 + 2] = tmpGrid[s * 3 + 2];
                    triCellI[w] = tmpI[s]; triCellJ[w] = tmpJ[s]; triHalf[w] = tmpH[s];
                    float cx = (tmpI[s] + 0.5f) * Spacing - HalfSize, cz = (tmpJ[s] + 0.5f) * Spacing - HalfSize;
                    triFacetKeep[w] = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FacetFadeStart, FacetFadeEnd, Mathf.Sqrt(cx * cx + cz * cz)));
                    if (pass == 0) dynTris = w + 1;
                    w++;
                }

            int vc = triCount * 3;
            vbuf = new OceanVertex[vc];
            nearUv = new Vector2[vc];
            triLX = new float[vc];
            triLZ = new float[vc];
            for (int v = 0; v < vc; v++)
            {
                int g = triGrid[v];
                float lx = (g % n) * Spacing - HalfSize, lz = (g / n) * Spacing - HalfSize;
                triLX[v] = lx; triLZ[v] = lz;
                vbuf[v] = new OceanVertex { pos = new Vector3(lx, FarLevel, lz), nrm = Vector3.up };
                nearUv[v] = new Vector2(1f, BlockV(AverageBlock, 1f));
            }

            var idx = new ushort[vc];
            for (int v = 0; v < vc; v++) idx[v] = (ushort)v;

            nearMesh = new Mesh { name = "SB_OceanNear" };
            nearMesh.MarkDynamic();
            nearMesh.SetVertexBufferParams(vc,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 1));
            nearMesh.SetVertexBufferData(vbuf, 0, 0, vc, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            nearMesh.SetVertexBufferData(nearUv, 0, 0, vc, 1, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            nearMesh.SetIndexBufferParams(vc, IndexFormat.UInt16);
            nearMesh.SetIndexBufferData(idx, 0, 0, vc, MeshUpdateFlags.DontValidateIndices);
            nearMesh.subMeshCount = 1;
            var bounds = new Bounds(Vector3.zero, new Vector3(HalfSize * 2f + 4f, 8f, HalfSize * 2f + 4f));
            nearMesh.SetSubMesh(0, new SubMeshDescriptor(0, vc, MeshTopology.Triangles) { firstVertex = 0, vertexCount = vc, bounds = bounds }, SubFlags);
            nearMesh.bounds = bounds;

            var near = new GameObject("OceanNear");
            near.transform.SetParent(transform, false);
            near.AddComponent<MeshFilter>().sharedMesh = nearMesh;
            nearR = near.AddComponent<MeshRenderer>();
            SetupRenderer(nearR);
            nearT = near.transform;
        }

        void BuildFar()
        {
            int n = FarCells + 1;
            farLocal = new Vector3[n * n];
            farUv = new Vector2[n * n];
            farU = new float[n * n];
            var nrm = new Vector3[n * n];
            float vOpaque = BlockV(AverageBlock, 1f);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    farLocal[j * n + i] = new Vector3(i * FarCell - FarHalf, FarLevel, j * FarCell - FarHalf);
                    nrm[j * n + i] = Vector3.up;
                    farUv[j * n + i] = new Vector2(1f, vOpaque);
                    farU[j * n + i] = 1f;
                }
            int hole0 = Mathf.RoundToInt((FarHalf - HalfSize) / FarCell), hole1 = FarCells - hole0;
            int cells = FarCells * FarCells - (hole1 - hole0) * (hole1 - hole0);
            var tris = new int[cells * 6];
            int t = 0;
            for (int j = 0; j < FarCells; j++)
                for (int i = 0; i < FarCells; i++)
                {
                    if (i >= hole0 && i < hole1 && j >= hole0 && j < hole1) continue;
                    int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                    // one diagonal direction everywhere: the ring snaps by single cells
                    tris[t++] = a; tris[t++] = c; tris[t++] = e;
                    tris[t++] = a; tris[t++] = e; tris[t++] = b;
                }

            farMesh = new Mesh { name = "SB_OceanFar" };
            farMesh.MarkDynamic();
            farMesh.SetVertices(farLocal);
            farMesh.SetNormals(nrm);
            farMesh.SetUVs(0, farUv);
            farMesh.SetTriangles(tris, 0, false);
            farMesh.bounds = new Bounds(Vector3.zero, new Vector3(FarHalf * 2f + 4f, 4f, FarHalf * 2f + 4f));

            var far = new GameObject("OceanFar");
            far.transform.SetParent(transform, false);
            far.AddComponent<MeshFilter>().sharedMesh = farMesh;
            farR = far.AddComponent<MeshRenderer>();
            SetupRenderer(farR);
            farT = far.transform;
        }

        void BuildOuter()
        {
            float[] rings = { FarHalf, 1800f, 3500f, 6000f };
            var v = new Vector3[rings.Length * 4];
            var nrm = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int r = 0; r < rings.Length; r++)
            {
                float s = rings[r];
                v[r * 4 + 0] = new Vector3(-s, FarLevel, -s);
                v[r * 4 + 1] = new Vector3(s, FarLevel, -s);
                v[r * 4 + 2] = new Vector3(s, FarLevel, s);
                v[r * 4 + 3] = new Vector3(-s, FarLevel, s);
            }
            float vOpaque = BlockV(AverageBlock, 1f);
            for (int i = 0; i < v.Length; i++) { nrm[i] = Vector3.up; uv[i] = new Vector2(1f, vOpaque); }
            var tris = new int[(rings.Length - 1) * 4 * 6];
            int t = 0;
            for (int r = 0; r < rings.Length - 1; r++)
                for (int side = 0; side < 4; side++)
                {
                    int a = r * 4 + side, b = r * 4 + (side + 1) % 4;
                    int c = (r + 1) * 4 + side, d = (r + 1) * 4 + (side + 1) % 4;
                    tris[t++] = a; tris[t++] = d; tris[t++] = c;
                    tris[t++] = a; tris[t++] = b; tris[t++] = d;
                }
            outerMesh = new Mesh { name = "SB_OceanOuter" };
            outerMesh.vertices = v; outerMesh.normals = nrm; outerMesh.uv = uv; outerMesh.triangles = tris;
            outerMesh.bounds = new Bounds(Vector3.zero, new Vector3(12000f, 4f, 12000f));

            var outer = new GameObject("OceanOuter");
            outer.transform.SetParent(transform, false);
            outer.AddComponent<MeshFilter>().sharedMesh = outerMesh;
            outerR = outer.AddComponent<MeshRenderer>();
            SetupRenderer(outerR);
            outerT = outer.transform;
        }

        const MeshUpdateFlags SubFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

        static void SetupRenderer(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            r.allowOcclusionWhenDynamic = false;
        }

        void ApplyMaterials(bool underwater)
        {
            var m = underwater ? underMat : topMat;
            nearR.sharedMaterial = m;
            farR.sharedMaterial = m;
            outerR.sharedMaterial = m;
        }

        Camera ActiveCamera()
        {
            if (cam != null && cam.isActiveAndEnabled) return cam;
            cam = Camera.main;
            if (cam == null && Refs.Player != null) cam = Refs.Player.View;
            return cam;
        }

        // ------------------------------------------------------------------ per frame
        void LateUpdate()
        {
            var c = ActiveCamera();
            if (c == null) return;
            Vector3 cp = c.transform.position;

            // ---- follow the camera, snapped (refresh the depth UVs only when the grid moves)
            float sx = Mathf.Round(cp.x / SnapStep) * SnapStep;
            float sz = Mathf.Round(cp.z / SnapStep) * SnapStep;
            bool snapped = sx != snapX || sz != snapZ;
            if (snapped)
            {
                snapX = sx; snapZ = sz;
                var p = new Vector3(sx, 0f, sz);
                nearT.position = p;
                farT.position = p;
                outerT.position = p;
                RefreshNearDepth();
                RefreshFarDepth();
            }

            // ---- deep below the surface (wreck, drop-off, trench) it lies far beyond the underwater fog: freeze the
            // waves (no displacement, no triangle rebuild, no upload) until the camera comes back up
            float surfaceY = Waves.HeightAt(cp.x, cp.z);
            bool frozen = surfaceY - cp.y > Mathf.Min(c.farClipPlane * 0.9f, FXVisibility.Meters * 1.5f);
            if (!frozen) UpdateSurface(snapped || wavesFrozen);
            wavesFrozen = frozen;

            // ---- above / below the surface
            bool u = cp.y < surfaceY;
            if (u != under) { under = u; ApplyMaterials(u); }

            // ---- view-dependent opacity (above water only: the underside material ignores the ramp). The same frame as
            // a snap (the depth coordinates moved with the grid), else when the eye moved 2.5 m or changed height.
            if (!under)
            {
                float eye = Mathf.Clamp(cp.y - Waves.SeaLevel, MinEye, 5000f);
                bool forced = snapped || viewDirty;
                bool eyeChanged = Mathf.Abs(eye - viewEye) > Mathf.Max(0.3f, viewEye * 0.12f);
                float mx = cp.x - viewX, mz = cp.z - viewZ;
                bool moved = mx * mx + mz * mz > ViewMove * ViewMove;
                float now = Time.unscaledTime;
                // (a bobbing boat changes the eye height all the time: those refreshes are throttled to 4 per second)
                if (forced || (eyeChanged && now - viewTime > 0.25f) || (moved && now - viewTime > 0.08f))
                {
                    RefreshNearView(cp, eye);
                    if (forced || eyeChanged) RefreshFarView(cp, eye);
                    viewX = cp.x; viewZ = cp.z; viewEye = eye; viewTime = now; viewDirty = false;
                }
            }
            else viewDirty = true;

            if (under)
            {
                // the surface seen from below glows with the daylight above
                float day = Mathf.Lerp(0.06f, 1f, EnvState.SunIntensity01);
                underMat.SetColor(idEmission, UnderGlow * day);
            }
            else
            {
                // calmer night sea (only re-applied when the night blend moved noticeably)
                float night = Mathf.Clamp01(EnvState.Night01);
                if (Mathf.Abs(night - nightApplied) > 0.01f)
                {
                    nightApplied = night;
                    topMat.SetColor(idBaseColor, Color.Lerp(Color.white, NightTint, night));
                    topMat.SetFloat(idSmoothness, Mathf.Lerp(Smoothness, NightSmoothness, night));
                }
            }
        }

        bool wavesFrozen;
        float nightApplied = -1f;
        int idBaseColor, idSmoothness;

        /// Displaces the welded grid, then rebuilds and uploads the flat-shaded dynamic triangles.
        void UpdateSurface(bool full)
        {
            frame++;
            Displace(innerIdx);
            if (full || (frame & 1) == 0) Displace(outerIdx);

            for (int t = 0; t < dynTris; t++)
            {
                int o = t * 3;
                float ax = triLX[o], az = triLZ[o], ay = gridY[triGrid[o]];
                float bx = triLX[o + 1], bz = triLZ[o + 1], by = gridY[triGrid[o + 1]];
                float cx = triLX[o + 2], cz = triLZ[o + 2], cy = gridY[triGrid[o + 2]];
                float e1x = bx - ax, e1y = by - ay, e1z = bz - az;
                float e2x = cx - ax, e2y = cy - ay, e2z = cz - az;
                float nx = e1y * e2z - e1z * e2y, ny = e1z * e2x - e1x * e2z, nz = e1x * e2y - e1y * e2x;
                float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                var nrm = new Vector3(nx * inv, ny * inv, nz * inv);
                vbuf[o] = new OceanVertex { pos = new Vector3(ax, ay, az), nrm = nrm };
                vbuf[o + 1] = new OceanVertex { pos = new Vector3(bx, by, bz), nrm = nrm };
                vbuf[o + 2] = new OceanVertex { pos = new Vector3(cx, cy, cz), nrm = nrm };
            }
            const MeshUpdateFlags vf = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;
            nearMesh.SetVertexBufferData(vbuf, 0, 0, dynTris * 3, 0, vf);
        }

        void Displace(int[] idx)
        {
            int n = Cells + 1;
            float ox = snapX - HalfSize, oz = snapZ - HalfSize;
            for (int q = 0; q < idx.Length; q++)
            {
                int k = idx[q];
                float h = Waves.HeightAt(ox + (k % n) * Spacing, oz + (k / n) * Spacing);
                gridY[k] = FarLevel + (h - FarLevel) * fade[k];
            }
        }

        // ------------------------------------------------------------------ depth (on snap) + view opacity UVs
        /// Depth ramp coordinate per near grid vertex and the world-anchored facet block per triangle (on snap only).
        void RefreshNearDepth()
        {
            int n = Cells + 1;
            int baseI = Mathf.RoundToInt((snapX - HalfSize) / Spacing), baseJ = Mathf.RoundToInt((snapZ - HalfSize) / Spacing);
            for (int k = 0; k < gridU.Length; k++)
                gridU[k] = water != null ? water.UAt(baseI + k % n, baseJ + k / n) : 1f;

            int rows = FacetCount;
            int triCount = triCellI.Length;
            for (int t = 0; t < triCount; t++)
            {
                int wi = baseI + triCellI[t], wj = baseJ + triCellJ[t];
                int row = (int)(WorldTerrainData.Hash(wi, wj, 31 + triHalf[t]) % (uint)rows);
                float keep = triFacetKeep[t];
                int level = keep >= 0.75f ? 0 : keep >= 0.25f ? 1 : KeepLevels - 1;
                triBlock[t] = (byte)(level * rows + row);
            }
        }

        /// Opacity boost per near grid vertex for the eye position, written with the depth into uv stream 1.
        void RefreshNearView(Vector3 eyePos, float eye)
        {
            int n = Cells + 1;
            float ox = snapX - HalfSize - eyePos.x, oz = snapZ - HalfSize - eyePos.z;
            for (int k = 0; k < gridOp.Length; k++)
            {
                float lx = (k % n) * Spacing + ox, lz = (k / n) * Spacing + oz;
                gridOp[k] = OpaqueBoost(AlphaOfU(gridU[k]), lx * lx + lz * lz, eye);
            }
            int triCount = triBlock.Length;
            for (int t = 0; t < triCount; t++)
            {
                int b = triBlock[t];
                int o = t * 3;
                int g0 = triGrid[o], g1 = triGrid[o + 1], g2 = triGrid[o + 2];
                nearUv[o] = new Vector2(gridU[g0], BlockV(b, gridOp[g0]));
                nearUv[o + 1] = new Vector2(gridU[g1], BlockV(b, gridOp[g1]));
                nearUv[o + 2] = new Vector2(gridU[g2], BlockV(b, gridOp[g2]));
            }
            nearMesh.SetVertexBufferData(nearUv, 0, 0, nearUv.Length, 1, SubFlags);
        }

        void RefreshFarDepth()
        {
            int n = FarCells + 1;
            int step = Mathf.RoundToInt(FarCell / WorldWaterMap.CellSize);
            int baseI = Mathf.RoundToInt((snapX - FarHalf) / WorldWaterMap.CellSize), baseJ = Mathf.RoundToInt((snapZ - FarHalf) / WorldWaterMap.CellSize);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    farU[j * n + i] = water != null ? water.UAt(baseI + i * step, baseJ + j * step) : 1f;
        }

        /// The far ring (≥ 120 m) is opaque from any low eye; from the air the lagoon may still show its floor.
        void RefreshFarView(Vector3 eyePos, float eye)
        {
            int n = FarCells + 1;
            float ox = snapX - FarHalf - eyePos.x, oz = snapZ - FarHalf - eyePos.z;
            int avg = AverageBlock;
            for (int j = 0; j < n; j++)
            {
                float lz = j * FarCell + oz;
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float u = farU[k];
                    float lx = i * FarCell + ox;
                    float b = u >= 0.999f ? 1f : OpaqueBoost(AlphaOfU(u), lx * lx + lz * lz, eye);
                    farUv[k] = new Vector2(u, BlockV(avg, b));
                }
            }
            farMesh.SetUVs(0, farUv);
        }

        void OnDestroy()
        {
            if (nearMesh != null) Destroy(nearMesh);
            if (farMesh != null) Destroy(farMesh);
            if (outerMesh != null) Destroy(outerMesh);
            if (topMat != null) Destroy(topMat);
            if (underMat != null) Destroy(underMat);
            if (ramp != null) Destroy(ramp);
        }
    }
}
