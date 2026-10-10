using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Ocean surface: a wave-displaced near grid (Waves.HeightAt) that follows the active camera, snapped to its
    /// grid spacing so vertices always sample the same world positions, plus a flat far ring out to the horizon.
    /// Wave displacement fades out toward the edge of the near grid so it joins the far ring seamlessly.
    /// No colliders: boats and players sample Waves directly.
    [DefaultExecutionOrder(100)]
    public class WorldOcean : MonoBehaviour
    {
        public const float Spacing = 2f;
        public const int Cells = 130;                       // 131 x 131 vertices, 260 x 260 m
        public const float HalfSize = Cells * Spacing * 0.5f;
        public const float FarLevel = -0.05f;
        public const int RenderQueue = 2980;                // before regular transparents (particles above water stay on top)

        const float FadeStart = 72f, FadeEnd = 116f;        // wave amplitude fade (meters from grid center)
        const float InnerRadius = 70f;                      // updated every frame; the rest every second frame
        const float UvTile = 12f;                           // meters per normal-map tile

        static readonly Color TopColor = new Color(0.035f, 0.25f, 0.32f, 0.82f);
        static readonly Color UnderColor = new Color(0.30f, 0.64f, 0.68f, 0.6f);

        Mesh nearMesh;
        Transform nearT, farT;
        MeshRenderer nearR, farR;
        Material topMat, underMat;

        Vector3[] verts, normals;
        float[] fade;
        int[] innerIdx, outerIdx, normalIdx;
        float snapX = float.NaN, snapZ = float.NaN;
        int frame;
        bool under;
        Vector2 scrollA, scrollB;
        int idBaseMap, idDetailMap, idEmission;
        Camera cam;

        public bool CameraUnderwater => under;

        public static WorldOcean Create(Transform parent, Texture2D rippleNormals)
        {
            var go = new GameObject("Ocean");
            go.transform.SetParent(parent, false);
            var o = go.AddComponent<WorldOcean>();
            o.Build(rippleNormals);
            return o;
        }

        void Build(Texture2D ripples)
        {
            idBaseMap = Shader.PropertyToID("_BaseMap");
            idDetailMap = Shader.PropertyToID("_DetailAlbedoMap");
            idEmission = Shader.PropertyToID("_EmissionColor");

            topMat = new Material(MaterialLibrary.Lit) { name = "SB_OceanTop" };
            topMat.SetColor("_BaseColor", TopColor);
            topMat.SetFloat("_Smoothness", 0.93f);
            topMat.SetFloat("_Metallic", 0f);
            SetupRipples(topMat, ripples, 0.45f, 0.3f);
            MaterialLibrary.MakeTransparent(topMat, true);
            topMat.renderQueue = RenderQueue;

            underMat = new Material(MaterialLibrary.Lit) { name = "SB_OceanUnder" };
            underMat.SetColor("_BaseColor", UnderColor);
            underMat.SetFloat("_Smoothness", 0.55f);
            underMat.SetFloat("_Metallic", 0f);
            SetupRipples(underMat, ripples, 0.7f, 0.4f);
            underMat.SetColor("_EmissionColor", new Color(0.10f, 0.34f, 0.40f));
            underMat.EnableKeyword("_EMISSION");
            underMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            MaterialLibrary.MakeTransparent(underMat, true);
            underMat.renderQueue = RenderQueue;

            // ---- near grid
            int n = Cells + 1;
            verts = new Vector3[n * n];
            normals = new Vector3[n * n];
            fade = new float[n * n];
            var uvs = new Vector2[n * n];
            int innerCount = 0, outerCount = 0, normalCount = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float lx = i * Spacing - HalfSize, lz = j * Spacing - HalfSize;
                    float d = Mathf.Sqrt(lx * lx + lz * lz);
                    float f = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FadeStart, FadeEnd, d));
                    fade[k] = f;
                    verts[k] = new Vector3(lx, FarLevel, lz);
                    normals[k] = Vector3.up;
                    uvs[k] = new Vector2(lx / UvTile, lz / UvTile);
                    if (f > 0f) { if (d < InnerRadius) innerCount++; else outerCount++; }
                    if (d < FadeEnd + Spacing * 2f && i > 0 && j > 0 && i < n - 1 && j < n - 1) normalCount++;
                }
            innerIdx = new int[innerCount];
            outerIdx = new int[outerCount];
            normalIdx = new int[normalCount];
            innerCount = outerCount = normalCount = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    float lx = i * Spacing - HalfSize, lz = j * Spacing - HalfSize;
                    float d = Mathf.Sqrt(lx * lx + lz * lz);
                    if (fade[k] > 0f) { if (d < InnerRadius) innerIdx[innerCount++] = k; else outerIdx[outerCount++] = k; }
                    if (d < FadeEnd + Spacing * 2f && i > 0 && j > 0 && i < n - 1 && j < n - 1) normalIdx[normalCount++] = k;
                }

            var tris = new int[Cells * Cells * 6];
            int t = 0;
            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    int a = j * n + i, b = a + 1, c = a + n, e = c + 1;
                    // alternate the diagonal for a less directional look
                    if (((i + j) & 1) == 0) { tris[t++] = a; tris[t++] = c; tris[t++] = e; tris[t++] = a; tris[t++] = e; tris[t++] = b; }
                    else { tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = e; }
                }

            nearMesh = new Mesh { name = "SB_OceanNear" };
            nearMesh.indexFormat = n * n > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            nearMesh.MarkDynamic();
            nearMesh.vertices = verts;
            nearMesh.normals = normals;
            nearMesh.uv = uvs;
            nearMesh.triangles = tris;
            nearMesh.bounds = new Bounds(Vector3.zero, new Vector3(HalfSize * 2f + 4f, 8f, HalfSize * 2f + 4f));

            var near = new GameObject("OceanNear");
            near.transform.SetParent(transform, false);
            near.AddComponent<MeshFilter>().sharedMesh = nearMesh;
            nearR = near.AddComponent<MeshRenderer>();
            SetupRenderer(nearR);
            nearT = near.transform;

            // ---- far ring: concentric squares from the near grid edge out to the horizon
            var far = new GameObject("OceanFar");
            far.transform.SetParent(transform, false);
            far.AddComponent<MeshFilter>().sharedMesh = BuildFarRing();
            farR = far.AddComponent<MeshRenderer>();
            SetupRenderer(farR);
            farT = far.transform;

            ApplyMaterial(false);
        }

        static void SetupRipples(Material m, Texture2D ripples, float bump, float detailBump)
        {
            if (ripples == null) return;
            m.SetTexture("_BumpMap", ripples);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
            m.SetTexture("_DetailNormalMap", ripples);
            m.SetFloat("_DetailNormalMapScale", detailBump);
            m.SetTextureScale("_DetailAlbedoMap", new Vector2(2.7f, 2.7f));
            m.EnableKeyword("_DETAIL_MULX2");
        }

        static void SetupRenderer(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.BlendProbesAndSkybox;
            r.allowOcclusionWhenDynamic = false;
        }

        static Mesh BuildFarRing()
        {
            float[] rings = { HalfSize, 330f, 800f, 1800f, 3500f, 6000f };
            var v = new Vector3[rings.Length * 4];
            var uv = new Vector2[v.Length];
            var nrm = new Vector3[v.Length];
            for (int r = 0; r < rings.Length; r++)
            {
                float s = rings[r];
                v[r * 4 + 0] = new Vector3(-s, FarLevel, -s);
                v[r * 4 + 1] = new Vector3(s, FarLevel, -s);
                v[r * 4 + 2] = new Vector3(s, FarLevel, s);
                v[r * 4 + 3] = new Vector3(-s, FarLevel, s);
            }
            for (int i = 0; i < v.Length; i++) { uv[i] = new Vector2(v[i].x / UvTile, v[i].z / UvTile); nrm[i] = Vector3.up; }
            var tris = new int[(rings.Length - 1) * 4 * 6];
            int t = 0;
            for (int r = 0; r < rings.Length - 1; r++)
                for (int side = 0; side < 4; side++)
                {
                    int a = r * 4 + side, b = r * 4 + (side + 1) % 4;
                    int c = (r + 1) * 4 + side, d = (r + 1) * 4 + (side + 1) % 4;
                    // clockwise seen from above (front face up)
                    tris[t++] = a; tris[t++] = d; tris[t++] = c;
                    tris[t++] = a; tris[t++] = b; tris[t++] = d;
                }
            var m = new Mesh { name = "SB_OceanFar" };
            m.vertices = v; m.uv = uv; m.normals = nrm; m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, new Vector3(12000f, 4f, 12000f));
            return m;
        }

        void ApplyMaterial(bool underwater)
        {
            var m = underwater ? underMat : topMat;
            nearR.sharedMaterial = m;
            farR.sharedMaterial = m;
        }

        Camera ActiveCamera()
        {
            if (cam != null && cam.isActiveAndEnabled) return cam;
            cam = Camera.main;
            if (cam == null && Refs.Player != null) cam = Refs.Player.View;
            return cam;
        }

        void LateUpdate()
        {
            var c = ActiveCamera();
            if (c == null) return;
            Vector3 cp = c.transform.position;

            // ---- follow the camera, snapped to the grid
            float sx = Mathf.Round(cp.x / Spacing) * Spacing;
            float sz = Mathf.Round(cp.z / Spacing) * Spacing;
            bool snapped = sx != snapX || sz != snapZ;
            if (snapped)
            {
                snapX = sx; snapZ = sz;
                var p = new Vector3(sx, 0f, sz);
                nearT.position = p;
                farT.position = p;
            }

            // ---- displace
            frame++;
            Displace(innerIdx);
            if (snapped || (frame & 1) == 0) Displace(outerIdx);

            int n = Cells + 1;
            float twoS = 2f * Spacing;
            for (int q = 0; q < normalIdx.Length; q++)
            {
                int k = normalIdx[q];
                float hl = verts[k - 1].y, hr = verts[k + 1].y, hd = verts[k - n].y, hu = verts[k + n].y;
                float nx = hl - hr, nz = hd - hu;
                float inv = 1f / Mathf.Sqrt(nx * nx + twoS * twoS + nz * nz);
                normals[k] = new Vector3(nx * inv, twoS * inv, nz * inv);
            }
            nearMesh.SetVertices(verts, 0, verts.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
            nearMesh.SetNormals(normals, 0, normals.Length, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);

            // ---- above / below the surface
            bool u = cp.y < Waves.HeightAt(cp.x, cp.z);
            if (u != under) { under = u; ApplyMaterial(u); }

            // ---- scroll the ripple normals (world-anchored: offset compensates the snapped position)
            float dt = Time.deltaTime;
            scrollA += new Vector2(0.021f, 0.013f) * dt;
            scrollB += new Vector2(-0.017f, 0.026f) * dt;
            var m = under ? underMat : topMat;
            Vector2 anchor = new Vector2(snapX / UvTile, snapZ / UvTile);
            m.SetTextureOffset(idBaseMap, Frac(anchor + scrollA));
            m.SetTextureOffset(idDetailMap, Frac(anchor * 2.7f + scrollB));
            if (under)
            {
                // the surface seen from below glows with the daylight above
                float day = Mathf.Lerp(0.05f, 1f, EnvState.SunIntensity01);
                m.SetColor(idEmission, new Color(0.10f, 0.34f, 0.40f) * day);
            }
        }

        static Vector2 Frac(Vector2 v) => new Vector2(v.x - Mathf.Floor(v.x), v.y - Mathf.Floor(v.y));

        void Displace(int[] idx)
        {
            float ox = snapX, oz = snapZ;
            for (int q = 0; q < idx.Length; q++)
            {
                int k = idx[q];
                Vector3 v = verts[k];
                float h = Waves.HeightAt(ox + v.x, oz + v.z);
                v.y = FarLevel + (h - FarLevel) * fade[k];
                verts[k] = v;
            }
        }

        void OnDestroy()
        {
            if (nearMesh != null) Destroy(nearMesh);
            if (topMat != null) Destroy(topMat);
            if (underMat != null) Destroy(underMat);
        }
    }
}
