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
    /// world-anchored uv.y row (±8 % facet brightness). UVs are refreshed only when the grid snaps. No colliders: boats and
    /// players sample Waves directly.
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
        Vector2[] nearUv;       // stream 1, refreshed on snap
        float[] gridU;          // ramp coordinate per near grid vertex (scratch)
        int[] triCellI, triCellJ, triHalf;
        float[] triFacetKeep;   // 1 = full facet brightness variation .. 0 = average (grid edge)

        // far ring (welded)
        Vector3[] farLocal;
        Vector2[] farUv;

        float snapX = float.NaN, snapZ = float.NaN;
        int frame;
        bool under;
        int idEmission;
        Camera cam;

        public bool CameraUnderwater => under;
        /// The colour ramp (64 x 4: depth along x, facet brightness rows along y) – also usable by other modules.
        public Texture2D Ramp => ramp;

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

        /// 64 x 4 sRGB ramp: x = sqrt(depth / 35 m) (shore turquoise-blue → steel blue → navy, alpha 0.58 → 0.96),
        /// y = facet brightness rows (0.915 / 0.97 / 1.03 / 1.085). Built once.
        static Texture2D BuildRamp()
        {
            int rows = WorldWaterMap.FacetRows.Length;
            var px = new Color32[RampWidth * rows];
            for (int i = 0; i < RampWidth; i++)
            {
                float u = (i + 0.5f) / RampWidth;
                Color c = WorldWaterMap.RampColor(WorldWaterMap.DepthOfU(u));
                for (int r = 0; r < rows; r++)
                {
                    float k = WorldWaterMap.FacetRows[r];
                    px[r * RampWidth + i] = new Color(Mathf.Clamp01(c.r * k), Mathf.Clamp01(c.g * k), Mathf.Clamp01(c.b * k), c.a);
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

        static float RowV(int row) => (row + 0.5f) / WorldWaterMap.FacetRows.Length;

        void BuildNear()
        {
            int n = Cells + 1;
            gridY = new float[n * n];
            gridU = new float[n * n];
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
                nearUv[v] = new Vector2(1f, 0.5f);
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
            var nrm = new Vector3[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    farLocal[j * n + i] = new Vector3(i * FarCell - FarHalf, FarLevel, j * FarCell - FarHalf);
                    nrm[j * n + i] = Vector3.up;
                    farUv[j * n + i] = new Vector2(1f, 0.5f);
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
            for (int i = 0; i < v.Length; i++) { nrm[i] = Vector3.up; uv[i] = new Vector2(1f, 0.5f); }
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
                RefreshNearUv();
                RefreshFarUv();
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

        // ------------------------------------------------------------------ depth UVs (on snap only)
        void RefreshNearUv()
        {
            int n = Cells + 1;
            int baseI = Mathf.RoundToInt((snapX - HalfSize) / Spacing), baseJ = Mathf.RoundToInt((snapZ - HalfSize) / Spacing);
            for (int k = 0; k < gridU.Length; k++)
                gridU[k] = water != null ? water.UAt(baseI + k % n, baseJ + k / n) : 1f;

            int rows = WorldWaterMap.FacetRows.Length;
            int triCount = triCellI.Length;
            for (int t = 0; t < triCount; t++)
            {
                int wi = baseI + triCellI[t], wj = baseJ + triCellJ[t];
                int row = (int)(WorldTerrainData.Hash(wi, wj, 31 + triHalf[t]) % (uint)rows);
                float v = Mathf.Lerp(0.5f, RowV(row), triFacetKeep[t]);
                int o = t * 3;
                nearUv[o] = new Vector2(gridU[triGrid[o]], v);
                nearUv[o + 1] = new Vector2(gridU[triGrid[o + 1]], v);
                nearUv[o + 2] = new Vector2(gridU[triGrid[o + 2]], v);
            }
            nearMesh.SetVertexBufferData(nearUv, 0, 0, nearUv.Length, 1, SubFlags);
        }

        void RefreshFarUv()
        {
            int n = FarCells + 1;
            int step = Mathf.RoundToInt(FarCell / WorldWaterMap.CellSize);
            int baseI = Mathf.RoundToInt((snapX - FarHalf) / WorldWaterMap.CellSize), baseJ = Mathf.RoundToInt((snapZ - FarHalf) / WorldWaterMap.CellSize);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    farUv[j * n + i] = new Vector2(water != null ? water.UAt(baseI + i * step, baseJ + j * step) : 1f, 0.5f);
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
