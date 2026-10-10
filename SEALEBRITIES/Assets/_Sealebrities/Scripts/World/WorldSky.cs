using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Builds the sky: sun and moon lights, procedural skybox, a sky-only realtime reflection probe and the
    /// camera-centered star, moon and cloud layers. The WorldDayNight component animates all of it.
    public static class WorldSky
    {
        public const int StarQueue = 2900, MoonQueue = 2905, CloudQueue = 2910;

        public static WorldDayNight Create(Transform parent, Texture2D starTex, Texture2D moonTex, Texture2D cloudTex)
        {
            var root = new GameObject("Sky");
            root.transform.SetParent(parent, false);

            DisableForeignDirectionalLights();

            // ---- sun (the only shadow-casting light, also RenderSettings.sun so URP and the skybox use it)
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root.transform, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            sun.renderMode = LightRenderMode.ForcePixel;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.8f, 0.6f);
            RenderSettings.sun = sun;

            // ---- moon: weak, bluish, no shadows
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(root.transform, false);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.shadows = LightShadows.None;
            moon.renderMode = LightRenderMode.ForcePixel;
            moon.intensity = 0f;
            moon.color = new Color(0.55f, 0.66f, 1f);
            moon.enabled = false;

            // shadow distance: URP asset value set by the editor setup (QualitySettings.shadowDistance is ignored by URP)
            QualitySettings.realtimeReflectionProbes = true;

            // ---- skybox
            Material sky = null;
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                sky = new Material(skyShader) { name = "SB_Sky" };
                sky.SetFloat("_SunDisk", 2f);
                sky.DisableKeyword("_SUNDISK_NONE");
                sky.DisableKeyword("_SUNDISK_SIMPLE");
                sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                sky.SetFloat("_SunSize", 0.045f);
                sky.SetFloat("_SunSizeConvergence", 5f);
                sky.SetFloat("_AtmosphereThickness", 1f);
                sky.SetColor("_SkyTint", new Color(0.45f, 0.5f, 0.62f));
                sky.SetColor("_GroundColor", new Color(0.15f, 0.25f, 0.31f));
                sky.SetFloat("_Exposure", 1.2f);
                RenderSettings.skybox = sky;
            }
            else Debug.LogWarning("Sealebrities: Skybox/Procedural shader not found – sky falls back to the camera background.");

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            // ---- sky-only reflection probe covering the whole map (water and wet surfaces reflect the current sky)
            var probeGo = new GameObject("SkyReflection");
            probeGo.transform.SetParent(root.transform, false);
            probeGo.transform.position = new Vector3(0f, 20f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 128;
            probe.hdr = true;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = 0;
            probe.size = new Vector3(8000f, 3000f, 8000f);
            probe.center = Vector3.zero;
            probe.boxProjection = false;
            probe.importance = 0;
            probe.blendDistance = 0f;
            probe.intensity = 1f;

            // ---- camera-centered sky layers
            var starMat = SkyMaterial(MaterialLibrary.ParticlesUnlit != null ? MaterialLibrary.ParticlesUnlit : MaterialLibrary.UnlitShader, starTex, StarQueue, "SB_Stars");
            var stars = SkyObject(root.transform, "Stars", BuildStars(1700, 7331), starMat);

            var moonMat = SkyMaterial(MaterialLibrary.UnlitShader, moonTex, MoonQueue, "SB_MoonDisc");
            var moonDisc = SkyObject(root.transform, "MoonDisc", BuildQuad(), moonMat);

            var cloudMat = SkyMaterial(MaterialLibrary.UnlitShader, cloudTex, CloudQueue, "SB_Clouds");
            var clouds = SkyObject(root.transform, "Clouds", BuildCloudDome(), cloudMat);

            var dn = root.AddComponent<WorldDayNight>();
            dn.Init(sun, moon, sky, probe, stars, starMat, moonDisc, moonMat, clouds, cloudMat);
            WorldInfo.Sun = sun;
            dn.ApplyNow();
            return dn;
        }

        static void DisableForeignDirectionalLights()
        {
            var lights = Object.FindObjectsByType<Light>();
            foreach (var l in lights)
            {
                if (l == null || l.type != LightType.Directional || !l.enabled) continue;
                l.enabled = false;
                Debug.Log("Sealebrities: disabled scene directional light '" + l.name + "' (the world module owns sun and moon).");
            }
        }

        static Material SkyMaterial(Shader shader, Texture2D tex, int queue, string name)
        {
            var m = new Material(shader) { name = name };
            if (tex != null) m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
            MaterialLibrary.MakeTransparent(m, true);
            m.renderQueue = queue;
            return m;
        }

        static MeshRenderer SkyObject(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.allowOcclusionWhenDynamic = false;
            r.enabled = false;
            return r;
        }

        // ------------------------------------------------------------------ meshes (unit radius, scaled at runtime)
        static Mesh BuildStars(int count, int seed)
        {
            var rng = new System.Random(seed);
            var v = new Vector3[count * 4];
            var uv = new Vector2[count * 4];
            var col = new Color[count * 4];
            var tris = new int[count * 6];
            for (int s = 0; s < count; s++)
            {
                float y = 0.03f + 0.97f * (float)rng.NextDouble();
                float phi = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Sqrt(1f - y * y);
                var dir = new Vector3(rr * Mathf.Cos(phi), y, rr * Mathf.Sin(phi));
                var t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                var t2 = Vector3.Cross(dir, t1);
                float r3 = (float)rng.NextDouble();
                float size = Mathf.Lerp(0.0022f, 0.0058f, r3 * r3 * r3);
                float bright = 0.35f + 0.65f * (float)(rng.NextDouble() * rng.NextDouble());
                float tintPick = (float)rng.NextDouble();
                Color tint = tintPick < 0.18f ? new Color(0.75f, 0.85f, 1f) : tintPick < 0.3f ? new Color(1f, 0.9f, 0.72f) : Color.white;
                float horizon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.03f, 0.22f, y));
                var c = new Color(tint.r, tint.g, tint.b, bright * horizon);

                int b = s * 4;
                v[b + 0] = dir + (-t1 - t2) * size; uv[b + 0] = new Vector2(0f, 0f);
                v[b + 1] = dir + (t1 - t2) * size; uv[b + 1] = new Vector2(1f, 0f);
                v[b + 2] = dir + (t1 + t2) * size; uv[b + 2] = new Vector2(1f, 1f);
                v[b + 3] = dir + (-t1 + t2) * size; uv[b + 3] = new Vector2(0f, 1f);
                col[b] = col[b + 1] = col[b + 2] = col[b + 3] = c;
                int t = s * 6;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            var m = new Mesh { name = "SB_Stars" };
            m.vertices = v; m.uv = uv; m.colors = col; m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return m;
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "SB_MoonQuad" };
            m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            m.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
            return m;
        }

        /// Hemisphere whose UVs project a flat cloud layer: zenith at the texture center, horizon at the rim.
        static Mesh BuildCloudDome()
        {
            float[] elev = { -2f, 0f, 1.5f, 3f, 5f, 7.5f, 10f, 13f, 17f, 22f, 28f, 35f, 44f, 55f, 68f, 80f, 90f };
            const int seg = 64;
            int rows = elev.Length;
            var v = new Vector3[rows * (seg + 1)];
            var uv = new Vector2[v.Length];
            for (int r = 0; r < rows; r++)
            {
                float e = elev[r] * Mathf.Deg2Rad;
                float y = Mathf.Sin(e), h = Mathf.Cos(e);
                for (int s = 0; s <= seg; s++)
                {
                    float a = s / (float)seg * Mathf.PI * 2f;
                    var d = new Vector3(h * Mathf.Cos(a), y, h * Mathf.Sin(a));
                    int k = r * (seg + 1) + s;
                    v[k] = d;
                    float py = Mathf.Max(d.y, 0.012f);
                    float px = d.x / py, pz = d.z / py;
                    float len = Mathf.Sqrt(px * px + pz * pz);
                    float rho = len / (len + 2f);
                    uv[k] = len > 1e-5f ? new Vector2(0.5f + 0.5f * rho * px / len, 0.5f + 0.5f * rho * pz / len) : new Vector2(0.5f, 0.5f);
                }
            }
            var tris = new int[(rows - 1) * seg * 6];
            int t = 0;
            for (int r = 0; r < rows - 1; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = r * (seg + 1) + s, b = a + 1, c = a + seg + 1, d = c + 1;
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            var m = new Mesh { name = "SB_CloudDome" };
            m.vertices = v; m.uv = uv; m.triangles = tris;
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return m;
        }
    }
}
