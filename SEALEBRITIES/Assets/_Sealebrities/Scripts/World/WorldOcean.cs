using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Low-poly ocean. A faceted near grid (3 m triangles, unwelded, flat normals from Waves.HeightAt) follows the camera,
    /// snapped to 12 m so every vertex always samples the same world position; a flat 12 m far ring and plain outer rings reach
    /// the horizon. Each triangle is colored from the world-anchored WorldWaterMap (shallow turquoise lagoons .. deep blue sea),
    /// re-sorted into four submeshes only when the grid snaps. Waves fade out toward the near grid edge so it joins the flat
    /// far ring seamlessly. No colliders: boats and players sample Waves directly.
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

        const float FadeStart = 64f, FadeEnd = 112f;        // wave amplitude fade (meters from grid center)
        const float InnerRadius = 58f;                      // displaced every frame; the rest every second frame
        const int Classes = WorldWaterMap.Classes;

        static readonly float[] TopAlpha = { 0.8f, 0.86f, 0.9f, 0.92f };
        static readonly Color UnderColor = new Color(0.50f, 0.86f, 0.88f, 0.62f);
        static readonly Color UnderGlow = new Color(0.16f, 0.46f, 0.52f);

        WorldWaterMap water;
        Mesh nearMesh, farMesh, outerMesh;
        Transform nearT, farT, outerT;
        MeshRenderer nearR, farR, outerR;
        Material underMat, nearDeepMat;
        Material[] topMats, nearTop, farTop, nearUnder, outerTop, outerUnder;

        // near grid: welded heights + unwelded, flat-shaded render vertices
        float[] gridY, fade;
        int[] innerIdx, outerIdx;
        int[] triGrid;          // 3 grid indices per triangle (dynamic triangles first)
        int dynTris;            // triangles with at least one displaced corner
        float[] triLX, triLZ;   // local x/z per render vertex
        OceanVertex[] vbuf;     // interleaved position + normal, uploaded in place (dynamic range only)
        int[] triCellI, triCellJ, triHalf;
        ushort[] nearIdx;
        byte[] nearCls;

        // far ring
        int[] farTriCell;       // packed cell index (j * FarCells + i) per triangle
        int[] farTriVerts;      // 3 vertex indices per triangle
        ushort[] farIdx;
        byte[] farCls;

        readonly int[] counts = new int[Classes], starts = new int[Classes], fills = new int[Classes];
        float snapX = float.NaN, snapZ = float.NaN;
        int frame;
        bool under;
        int idEmission;
        Camera cam;

        public bool CameraUnderwater => under;

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

            // shallow turquoise .. deep blue (water map classes 0..3). The lagoon class is pushed a little toward
            // Water so the shallows read as saturated turquoise over the pale sand instead of a milky mint.
            var topColors = new[] { Color.Lerp(LowPoly.WaterShallow, LowPoly.Water, 0.3f), LowPoly.Water, Color.Lerp(LowPoly.Water, LowPoly.WaterDeep, 0.55f), LowPoly.WaterDeep };
            topMats = new Material[Classes];
            for (int k = 0; k < Classes; k++)
            {
                var c = topColors[k];
                c.a = TopAlpha[k];
                var m = new Material(MaterialLibrary.Lit) { name = "SB_OceanTop" + k };
                m.SetColor("_BaseColor", c);
                // moderate gloss: soft sun glints, but the grazing-angle sky reflection no longer bleaches the
                // distant sea to white (the toy look keeps the water clearly blue up to the horizon haze)
                m.SetFloat("_Smoothness", 0.34f);
                m.SetFloat("_Metallic", 0f);
                MaterialLibrary.MakeTransparent(m, true);
                m.renderQueue = RenderQueue;
                topMats[k] = m;
            }
            underMat = new Material(MaterialLibrary.Lit) { name = "SB_OceanUnder" };
            underMat.SetColor("_BaseColor", UnderColor);
            underMat.SetFloat("_Smoothness", 0.3f);
            underMat.SetFloat("_Metallic", 0f);
            underMat.SetColor("_EmissionColor", UnderGlow);
            underMat.EnableKeyword("_EMISSION");
            underMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            MaterialLibrary.MakeTransparent(underMat, true);
            underMat.renderQueue = RenderQueue;

            // the camera-following near grid shows deep water a little lighter than the far ring: the open sea
            // darkens gently toward the distance (the step sits at ~115 m where the waves are already flat)
            nearDeepMat = new Material(topMats[Classes - 1]) { name = "SB_OceanTopNearDeep" };
            var nd = Color.Lerp(LowPoly.Water, LowPoly.WaterDeep, 0.78f);
            nd.a = TopAlpha[Classes - 1];
            nearDeepMat.SetColor("_BaseColor", nd);
            nearDeepMat.renderQueue = RenderQueue;
            nearTop = (Material[])topMats.Clone();
            nearTop[Classes - 1] = nearDeepMat;
            farTop = (Material[])topMats.Clone();
            nearUnder = new[] { underMat, underMat, underMat, underMat };
            outerTop = new[] { topMats[Classes - 1] };
            outerUnder = new[] { underMat };

            BuildNear();
            BuildFar();
            BuildOuter();
            ApplyMaterials(false);
        }

        void BuildNear()
        {
            int n = Cells + 1;
            gridY = new float[n * n];
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
            int w = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int s = 0; s < triCount; s++)
                {
                    if (dynamic[s] != (pass == 0)) continue;
                    triGrid[w * 3] = tmpGrid[s * 3]; triGrid[w * 3 + 1] = tmpGrid[s * 3 + 1]; triGrid[w * 3 + 2] = tmpGrid[s * 3 + 2];
                    triCellI[w] = tmpI[s]; triCellJ[w] = tmpJ[s]; triHalf[w] = tmpH[s];
                    if (pass == 0) dynTris = w + 1;
                    w++;
                }

            int vc = triCount * 3;
            vbuf = new OceanVertex[vc];
            triLX = new float[vc];
            triLZ = new float[vc];
            for (int v = 0; v < vc; v++)
            {
                int g = triGrid[v];
                float lx = (g % n) * Spacing - HalfSize, lz = (g / n) * Spacing - HalfSize;
                triLX[v] = lx; triLZ[v] = lz;
                vbuf[v] = new OceanVertex { pos = new Vector3(lx, FarLevel, lz), nrm = Vector3.up };
            }

            nearIdx = new ushort[vc];
            nearCls = new byte[triCount];
            for (int v = 0; v < vc; v++) nearIdx[v] = (ushort)v;

            nearMesh = new Mesh { name = "SB_OceanNear" };
            nearMesh.MarkDynamic();
            nearMesh.SetVertexBufferParams(vc,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3));
            nearMesh.SetVertexBufferData(vbuf, 0, 0, vc, 0, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            nearMesh.SetIndexBufferParams(vc, IndexFormat.UInt16);
            nearMesh.SetIndexBufferData(nearIdx, 0, 0, vc, MeshUpdateFlags.DontValidateIndices);
            nearMesh.subMeshCount = Classes;
            var bounds = new Bounds(Vector3.zero, new Vector3(HalfSize * 2f + 4f, 8f, HalfSize * 2f + 4f));
            for (int k = 0; k < Classes; k++)
                nearMesh.SetSubMesh(k, Sub(0, k == 1 ? vc : 0, vc, bounds), SubFlags);
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
            var v = new Vector3[n * n];
            var nrm = new Vector3[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    v[j * n + i] = new Vector3(i * FarCell - FarHalf, FarLevel, j * FarCell - FarHalf);
                    nrm[j * n + i] = Vector3.up;
                }
            int hole0 = Mathf.RoundToInt((FarHalf - HalfSize) / FarCell), hole1 = FarCells - hole0;
            int cells = FarCells * FarCells - (hole1 - hole0) * (hole1 - hole0);
            farTriCell = new int[cells * 2];
            farTriVerts = new int[cells * 6];
            int t = 0;
            for (int j = 0; j < FarCells; j++)
                for (int i = 0; i < FarCells; i++)
                {
                    if (i >= hole0 && i < hole1 && j >= hole0 && j < hole1) continue;
                    int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                    for (int h = 0; h < 2; h++)
                    {
                        // one diagonal direction everywhere: the ring snaps by single cells, so an alternating
                        // pattern would flip against the world on every snap (the ring is flat, only colors show)
                        int p0, p1, p2;
                        if (h == 0) { p0 = a; p1 = c; p2 = e; } else { p0 = a; p1 = e; p2 = b; }
                        farTriVerts[t * 3] = p0; farTriVerts[t * 3 + 1] = p1; farTriVerts[t * 3 + 2] = p2;
                        farTriCell[t] = (j * FarCells + i) * 2 + h;
                        t++;
                    }
                }
            int ic = t * 3;
            farIdx = new ushort[ic];
            farCls = new byte[t];
            for (int k = 0; k < ic; k++) farIdx[k] = (ushort)farTriVerts[k];

            farMesh = new Mesh { name = "SB_OceanFar" };
            farMesh.SetVertices(v);
            farMesh.SetNormals(nrm);
            farMesh.SetIndexBufferParams(ic, IndexFormat.UInt16);
            farMesh.SetIndexBufferData(farIdx, 0, 0, ic, MeshUpdateFlags.DontValidateIndices);
            farMesh.subMeshCount = Classes;
            var bounds = new Bounds(Vector3.zero, new Vector3(FarHalf * 2f + 4f, 4f, FarHalf * 2f + 4f));
            for (int k = 0; k < Classes; k++)
                farMesh.SetSubMesh(k, Sub(0, k == Classes - 1 ? ic : 0, v.Length, bounds), SubFlags);
            farMesh.bounds = bounds;

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
            for (int r = 0; r < rings.Length; r++)
            {
                float s = rings[r];
                v[r * 4 + 0] = new Vector3(-s, FarLevel, -s);
                v[r * 4 + 1] = new Vector3(s, FarLevel, -s);
                v[r * 4 + 2] = new Vector3(s, FarLevel, s);
                v[r * 4 + 3] = new Vector3(-s, FarLevel, s);
            }
            for (int i = 0; i < v.Length; i++) nrm[i] = Vector3.up;
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
            outerMesh.vertices = v; outerMesh.normals = nrm; outerMesh.triangles = tris;
            outerMesh.bounds = new Bounds(Vector3.zero, new Vector3(12000f, 4f, 12000f));

            var outer = new GameObject("OceanOuter");
            outer.transform.SetParent(transform, false);
            outer.AddComponent<MeshFilter>().sharedMesh = outerMesh;
            outerR = outer.AddComponent<MeshRenderer>();
            SetupRenderer(outerR);
            outerT = outer.transform;
        }

        const MeshUpdateFlags SubFlags = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers;

        static SubMeshDescriptor Sub(int start, int count, int vertexCount, Bounds b)
        {
            return new SubMeshDescriptor(start, count, MeshTopology.Triangles) { firstVertex = 0, vertexCount = vertexCount, bounds = b };
        }

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
            nearR.sharedMaterials = underwater ? nearUnder : nearTop;
            farR.sharedMaterials = underwater ? nearUnder : farTop;
            outerR.sharedMaterials = underwater ? outerUnder : outerTop;
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

            // ---- follow the camera, snapped (re-color the triangles only when the grid moves)
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
                ClassifyNear();
                ClassifyFar();
            }

            // ---- displace the welded grid, then rebuild the flat-shaded dynamic triangles
            frame++;
            Displace(innerIdx);
            if (snapped || (frame & 1) == 0) Displace(outerIdx);

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

            // ---- above / below the surface
            bool u = cp.y < Waves.HeightAt(cp.x, cp.z);
            if (u != under) { under = u; ApplyMaterials(u); }
            if (under)
            {
                // the surface seen from below glows with the daylight above
                float day = Mathf.Lerp(0.06f, 1f, EnvState.SunIntensity01);
                underMat.SetColor(idEmission, UnderGlow * day);
            }
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

        // ------------------------------------------------------------------ coloring (on snap only)
        void ClassifyNear()
        {
            int triCount = nearCls.Length;
            int baseI = Mathf.RoundToInt((snapX - HalfSize) / Spacing), baseJ = Mathf.RoundToInt((snapZ - HalfSize) / Spacing);
            for (int k = 0; k < Classes; k++) counts[k] = 0;
            for (int t = 0; t < triCount; t++)
            {
                int wi = baseI + triCellI[t], wj = baseJ + triCellJ[t];
                float cv = water != null ? water.ClassAt(wi, wj) : 1f;
                int k = WorldWaterMap.Pick(cv, WorldTerrainData.Hash(wi, wj, 77 + triHalf[t]));
                nearCls[t] = (byte)k;
                counts[k]++;
            }
            int acc = 0;
            for (int k = 0; k < Classes; k++) { starts[k] = acc; fills[k] = 0; acc += counts[k] * 3; }
            for (int t = 0; t < triCount; t++)
            {
                int k = nearCls[t];
                int o = starts[k] + fills[k];
                fills[k] += 3;
                int v = t * 3;
                nearIdx[o] = (ushort)v; nearIdx[o + 1] = (ushort)(v + 1); nearIdx[o + 2] = (ushort)(v + 2);
            }
            nearMesh.SetIndexBufferData(nearIdx, 0, 0, nearIdx.Length, SubFlags);
            var b = nearMesh.bounds;
            for (int k = 0; k < Classes; k++) nearMesh.SetSubMesh(k, Sub(starts[k], counts[k] * 3, vbuf.Length, b), SubFlags);
        }

        void ClassifyFar()
        {
            int triCount = farCls.Length;
            // world 3 m cell of each 12 m far cell center
            float ox = snapX - FarHalf, oz = snapZ - FarHalf;
            int baseI = Mathf.RoundToInt(ox / FarCell), baseJ = Mathf.RoundToInt(oz / FarCell);
            for (int k = 0; k < Classes; k++) counts[k] = 0;
            for (int t = 0; t < triCount; t++)
            {
                int packed = farTriCell[t];
                int h = packed & 1, cell = packed >> 1;
                int i = cell % FarCells, j = cell / FarCells;
                float wx = ox + (i + 0.5f) * FarCell, wz = oz + (j + 0.5f) * FarCell;
                float cv = water != null
                    ? water.ClassAt(Mathf.FloorToInt(wx / WorldWaterMap.CellSize), Mathf.FloorToInt(wz / WorldWaterMap.CellSize))
                    : 3f;
                int k = WorldWaterMap.Pick(cv, WorldTerrainData.Hash(baseI + i, baseJ + j, 91 + h));
                farCls[t] = (byte)k;
                counts[k]++;
            }
            int acc = 0;
            for (int k = 0; k < Classes; k++) { starts[k] = acc; fills[k] = 0; acc += counts[k] * 3; }
            for (int t = 0; t < triCount; t++)
            {
                int k = farCls[t];
                int o = starts[k] + fills[k];
                fills[k] += 3;
                int s = t * 3;
                farIdx[o] = (ushort)farTriVerts[s]; farIdx[o + 1] = (ushort)farTriVerts[s + 1]; farIdx[o + 2] = (ushort)farTriVerts[s + 2];
            }
            farMesh.SetIndexBufferData(farIdx, 0, 0, farIdx.Length, SubFlags);
            var b = farMesh.bounds;
            int vc = farMesh.vertexCount;
            for (int k = 0; k < Classes; k++) farMesh.SetSubMesh(k, Sub(starts[k], counts[k] * 3, vc, b), SubFlags);
        }

        void OnDestroy()
        {
            if (nearMesh != null) Destroy(nearMesh);
            if (farMesh != null) Destroy(farMesh);
            if (outerMesh != null) Destroy(outerMesh);
            if (topMats != null) foreach (var m in topMats) if (m != null) Destroy(m);
            if (nearDeepMat != null) Destroy(nearDeepMat);
            if (underMat != null) Destroy(underMat);
        }
    }
}
