using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// K.8: the fog bank around the secret Fog Isle (WorldLayout.FogIsland, radius WorldLayout.FogBankRadius 260 m).
    /// From outside a ring-shaped wall of soft, faceted fog stands on the water (the island behind it is invisible – from
    /// the main island it is a pale bank on the northern horizon). Inside the ring the above-water haze turns into thick
    /// grey-white fog (≈ 90 m sight) and the sky disappears in it. Once Toni's map is known (StoryKeys.FogMap) the wall
    /// thins to a light haze, the buoy lane (WorldLayout.FogLaneStart → End, 30 m wide) cuts a clear gap through it and the
    /// fog inside becomes a soft haze (≈ 380 m sight). The boat drift without the map is the fishing+boat module's.
    /// Deterministic shape (fixed seed); the flag is shared game state, so every co-op machine shows the same fog.
    sealed class FXFogBankRenderer
    {
        const int Segments = 96;
        const int Layers = 3;
        const float WallHeight = 30f;
        const float BankDensity = 0.025f;    // exponential: ~90 m sight inside the bank
        const float HazeDensity = 0.006f;    // with the map: ~380 m
        static readonly Color FogScreen = new Color(0.80f, 0.85f, 0.88f, 1f);   // pale blue-grey

        readonly GameObject go;
        readonly Mesh mesh;
        readonly MeshRenderer mr;
        readonly Material mat;
        readonly Vector3[] verts;
        readonly Color[] cols;
        readonly float[] baseAlpha;
        readonly float[] vertAngle;
        readonly Vector3 center;
        readonly float radius;
        bool mapKnown, colorsBuilt;

        float thick01;

        /// 0..1 how strongly the camera is inside the thick part of the bank (FXPostProcess blends the haze with it).
        public float Thick01 => thick01;
        public static Color FogColorScreen => FogScreen;
        public static float Density(bool map) => map ? HazeDensity : BankDensity;
        public bool MapKnown => mapKnown;

        public FXFogBankRenderer(Transform parent)
        {
            center = WorldLayout.FogIsland;
            radius = WorldLayout.FogBankRadius;

            go = new GameObject("FX_FogBank");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(center.x, 0f, center.z);

            // three concentric bands, each a strip of quads with a jagged, faceted top; alpha fades upward
            int ringVerts = (Segments + 1) * 3;      // bottom, middle, top row per segment edge
            verts = new Vector3[ringVerts * Layers];
            cols = new Color[verts.Length];
            baseAlpha = new float[verts.Length];
            vertAngle = new float[verts.Length];
            var tris = new int[Segments * 4 * 3 * Layers];
            var rng = new System.Random(8642);
            int t = 0;
            for (int l = 0; l < Layers; l++)
            {
                float r = radius + (l - 1) * 18f;
                float layerA = l == 1 ? 0.8f : 0.55f;
                int b0 = l * ringVerts;
                for (int s = 0; s <= Segments; s++)
                {
                    float a = s / (float)Segments * Mathf.PI * 2f;
                    float wob = s == Segments ? 0f : (float)rng.NextDouble();
                    float rr = r + (s == Segments ? 0f : ((float)rng.NextDouble() - 0.5f) * 10f);
                    float top = WallHeight * (0.6f + 0.55f * wob) + l * 3f;
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    int v = b0 + s * 3;
                    verts[v] = dir * rr + Vector3.up * -1.5f;
                    verts[v + 1] = dir * (rr + 2f) + Vector3.up * (top * 0.4f);
                    verts[v + 2] = dir * (rr - 3f) + Vector3.up * top;
                    baseAlpha[v] = 0.85f * layerA; baseAlpha[v + 1] = 0.6f * layerA; baseAlpha[v + 2] = 0f;
                    vertAngle[v] = vertAngle[v + 1] = vertAngle[v + 2] = a;
                }
                // the seam vertex of the last segment equals the first (closed ring)
                int last = b0 + Segments * 3;
                verts[last] = verts[b0]; verts[last + 1] = verts[b0 + 1]; verts[last + 2] = verts[b0 + 2];
                for (int s = 0; s < Segments; s++)
                {
                    int a0 = b0 + s * 3, a1 = a0 + 3;
                    tris[t++] = a0; tris[t++] = a0 + 1; tris[t++] = a1;
                    tris[t++] = a1; tris[t++] = a0 + 1; tris[t++] = a1 + 1;
                    tris[t++] = a0 + 1; tris[t++] = a0 + 2; tris[t++] = a1 + 1;
                    tris[t++] = a1 + 1; tris[t++] = a0 + 2; tris[t++] = a1 + 2;
                }
            }
            mesh = new Mesh { name = "FX_FogBank" };
            mesh.MarkDynamic();
            mesh.vertices = verts;
            mesh.colors = cols;
            mesh.triangles = tris;
            mesh.bounds = new Bounds(new Vector3(0f, WallHeight * 0.5f, 0f), new Vector3(radius * 2f + 80f, WallHeight + 20f, radius * 2f + 80f));

            var shader = MaterialLibrary.ParticlesUnlit != null ? MaterialLibrary.ParticlesUnlit : MaterialLibrary.UnlitShader;
            if (shader != null)
            {
                mat = new Material(shader) { name = "FX_FogBank" };
                mat.SetColor("_BaseColor", Color.white);
                MaterialLibrary.MakeTransparent(mat, true);
                mat.SetShaderPassEnabled("DepthNormals", false);
                mat.SetShaderPassEnabled("DepthNormalsOnly", false);
                mat.SetShaderPassEnabled("MotionVectors", false);
                mat.renderQueue = (int)RenderQueue.Transparent + 5;
            }
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;
            mr.enabled = false;
        }

        public void Dispose()
        {
            if (go != null) Object.Destroy(go);
            if (mesh != null) Object.Destroy(mesh);
            if (mat != null) Object.Destroy(mat);
            FXFogBank.Thick01 = 0f;
            FXFogBank.CameraInside = false;
        }

        /// Vertex colours: pale fog, thinner with the map and with a clear gap where the buoy lane crosses the ring.
        void BuildColors(float daylight)
        {
            colorsBuilt = true;
            Vector3 lane = WorldLayout.FogLaneStart - center;
            float laneAngle = Mathf.Atan2(lane.x, lane.z);
            if (laneAngle < 0f) laneAngle += Mathf.PI * 2f;
            float halfGap = (WorldLayout.FogLaneWidth * 0.5f + 12f) / radius;
            float k = mapKnown ? 0.35f : 1f;
            Color c = Color.Lerp(new Color(0.18f, 0.22f, 0.32f), FogScreen, daylight);
            bool lin = QualitySettings.activeColorSpace == ColorSpace.Linear;
            if (lin) c = c.linear;
            for (int i = 0; i < verts.Length; i++)
            {
                float a = baseAlpha[i] * k;
                if (mapKnown)
                {
                    float d = Mathf.Abs(Mathf.DeltaAngle(vertAngle[i] * Mathf.Rad2Deg, laneAngle * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                    a *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(halfGap, halfGap * 2.2f, d));
                }
                cols[i] = new Color(c.r, c.g, c.b, a);
            }
            mesh.colors = cols;
        }

        float lastDaylight = -1f;

        public void Tick(FXFrame f)
        {
            bool map = false;
            try { map = StoryFlags.Has(StoryKeys.FogMap); } catch (System.Exception) { map = false; }
            float daylight = Mathf.Lerp(0.25f, 1f, f.sun01);
            if (!colorsBuilt || map != mapKnown || Mathf.Abs(daylight - lastDaylight) > 0.05f)
            {
                mapKnown = map;
                lastDaylight = daylight;
                BuildColors(daylight);
            }

            // how deep inside the bank the camera is (0 outside .. 1 well inside), thinned by the map / the buoy lane
            Vector3 p = f.camPos;
            float dx = p.x - center.x, dz = p.z - center.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float inside = 1f - FXMath.SmoothRange(radius - 35f, radius + 8f, dist);
            if (map)
            {
                // the lane stays clear: haze only
                Vector3 a = WorldLayout.FogLaneStart, b = WorldLayout.FogLaneEnd;
                Vector3 ab = b - a; ab.y = 0f;
                float tt = Mathf.Clamp01(Vector3.Dot(new Vector3(p.x - a.x, 0f, p.z - a.z), ab) / Mathf.Max(1f, ab.sqrMagnitude));
                float laneDist = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(a.x + ab.x * tt, 0f, a.z + ab.z * tt));
                inside *= Mathf.Lerp(0.25f, 1f, FXMath.SmoothRange(WorldLayout.FogLaneWidth * 0.5f, WorldLayout.FogLaneWidth, laneDist));
            }
            float target = f.inGame ? inside : 0f;
            thick01 = Mathf.MoveTowards(thick01, target, f.dt / 1.5f);
            FXFogBank.Thick01 = thick01;
            FXFogBank.CameraInside = dist < radius;

            FXFogBank.Map = map;
            // the wall: drawn above water only
            bool show = f.inGame && f.under01 < 0.5f && mat != null;
            if (mr.enabled != show) mr.enabled = show;
        }
    }
}
