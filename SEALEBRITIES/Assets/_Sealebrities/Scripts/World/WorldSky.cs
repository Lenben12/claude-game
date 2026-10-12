using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Builds the How to Fish sky: sun and moon lights, the calibrated gradient sky dome with a sun disc
    /// (WorldSkyDome; Unity's procedural skybox only as a fallback), a sky-only realtime reflection probe,
    /// camera-centered faceted stars and moon, and big drifting low-poly cumulus clouds. WorldDayNight animates all of it.
    public static class WorldSky
    {
        /// Round 3: stars and moon moved from the transparent range (2900/2905) into the opaque pass right before the
        /// clouds (WorldClouds.RenderQueue 2450) – the world still draws over them (depth test as before) and now the
        /// clouds cover them too, whatever their distance.
        public const int StarQueue = 2440, MoonQueue = 2441;

        public static WorldDayNight Create(Transform parent)
        {
            var root = new GameObject("Sky");
            root.transform.SetParent(parent, false);

            DisableForeignDirectionalLights();

            // ---- sun (shadow-casting main light by day, RenderSettings.sun so URP uses it)
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(root.transform, false);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.9f;
            sun.shadowBias = 0.05f;
            sun.shadowNormalBias = 0.4f;
            sun.renderMode = LightRenderMode.ForcePixel;
            sun.intensity = 1.35f;
            sun.color = new Color(1f, 0.95f, 0.87f);
            RenderSettings.sun = sun;

            // ---- moon: cool bluish light, no shadows; becomes the main light at night (moonlit, readable nights)
            var moonGo = new GameObject("Moon");
            moonGo.transform.SetParent(root.transform, false);
            var moon = moonGo.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.shadows = LightShadows.None;
            moon.renderMode = LightRenderMode.ForcePixel;
            moon.intensity = 0f;
            moon.color = new Color(0.663f, 0.737f, 1f);
            moon.enabled = false;

            // shadow distance: URP asset value set by the editor setup (QualitySettings.shadowDistance is ignored by URP)
            QualitySettings.realtimeReflectionProbes = true;

            // ---- the How to Fish gradient sky (exact art-bible colours) – vertex-coloured dome with the particles shader
            var domeShader = MaterialLibrary.ParticlesUnlit;
            if (domeShader != null && !domeShader.isSupported) domeShader = null;
            var dome = new WorldSkyDome(root.transform, domeShader);

            // ---- fallback skybox: Unity's procedural sky (only when the dome could not be built)
            Material sky = null;
            var skyShader = dome.Valid ? null : Shader.Find("Skybox/Procedural");
            if (dome.Valid) RenderSettings.skybox = null;   // the dome is the sky: no skybox pass draws over it
            if (skyShader != null)
            {
                sky = new Material(skyShader) { name = "SB_Sky" };
                sky.SetFloat("_SunDisk", 2f);
                sky.DisableKeyword("_SUNDISK_NONE");
                sky.DisableKeyword("_SUNDISK_SIMPLE");
                sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                sky.SetFloat("_SunSize", 0.045f);
                sky.SetFloat("_SunSizeConvergence", 3.5f);
                sky.SetFloat("_AtmosphereThickness", 1f);
                sky.SetColor("_SkyTint", new Color(0.40f, 0.50f, 0.64f));
                sky.SetColor("_GroundColor", new Color(0.40f, 0.55f, 0.66f));
                sky.SetFloat("_Exposure", 2.8f);
                RenderSettings.skybox = sky;
                Debug.LogWarning("Sealebrities: sky dome unavailable – using the procedural skybox fallback.");
            }
            else if (!dome.Valid) Debug.LogWarning("Sealebrities: no sky shader found – sky falls back to the camera background.");

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            // ---- sky-only reflection probe covering the whole map (the water's soft glints reflect the current sky;
            // with the dome it is a calibrated sky colour, so the sea brightens toward the horizon like How to Fish)
            var probeGo = new GameObject("SkyReflection");
            probeGo.transform.SetParent(root.transform, false);
            probeGo.transform.position = new Vector3(0f, 20f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 64;
            probe.hdr = true;
            probe.clearFlags = dome.Valid ? ReflectionProbeClearFlags.SolidColor : ReflectionProbeClearFlags.Skybox;
            probe.backgroundColor = dome.ReflectionColor;
            probe.cullingMask = 0;
            probe.size = new Vector3(8000f, 3000f, 8000f);
            probe.center = Vector3.zero;
            probe.boxProjection = false;
            probe.importance = 0;
            probe.blendDistance = 0f;
            probe.intensity = 1f;

            // ---- camera-centered sky layers (vertex colored, untextured)
            var vcShader = MaterialLibrary.ParticlesUnlit != null ? MaterialLibrary.ParticlesUnlit : MaterialLibrary.UnlitShader;
            var starMat = SkyMaterial(vcShader, StarQueue, "SB_Stars");
            var stars = SkyObject(root.transform, "Stars", BuildStars(950, 7331), starMat);
            var moonMat = SkyMaterial(vcShader, MoonQueue, "SB_MoonDisc");
            var moonDisc = SkyObject(root.transform, "MoonDisc", BuildMoonDisc(), moonMat);

            var clouds = new WorldClouds(root.transform);

            var dn = root.AddComponent<WorldDayNight>();
            dn.Init(sun, moon, sky, dome, probe, stars, starMat, moonDisc, moonMat, clouds);
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

        static Material SkyMaterial(Shader shader, int queue, string name)
        {
            // no depth / depth-normals prepass (they now sit in the opaque queue range), no shadows, no motion vectors
            var m = WorldSkyDome.SkyLayerMaterial(shader, name, queue);
            m.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0f));
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
        /// Tiny flat diamonds (4 vertices each) on the unit sphere; brightness lives in the vertex alpha.
        static Mesh BuildStars(int count, int seed)
        {
            var rng = new System.Random(seed);
            var v = new Vector3[count * 4];
            var col = new Color[count * 4];
            var tris = new int[count * 6];
            for (int s = 0; s < count; s++)
            {
                float y = 0.04f + 0.96f * (float)rng.NextDouble();
                float phi = (float)rng.NextDouble() * Mathf.PI * 2f;
                float rr = Mathf.Sqrt(1f - y * y);
                var dir = new Vector3(rr * Mathf.Cos(phi), y, rr * Mathf.Sin(phi));
                var t1 = Vector3.Cross(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.right : Vector3.up).normalized;
                var t2 = Vector3.Cross(dir, t1);
                float r3 = (float)rng.NextDouble();
                float size = Mathf.Lerp(0.0018f, 0.0042f, r3 * r3 * r3);
                float bright = 0.45f + 0.55f * (float)(rng.NextDouble() * rng.NextDouble());
                float tintPick = (float)rng.NextDouble();
                Color tint = tintPick < 0.2f ? new Color(0.78f, 0.88f, 1f) : tintPick < 0.32f ? new Color(1f, 0.92f, 0.75f) : Color.white;
                float horizon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.04f, 0.2f, y));
                var c = new Color(tint.r, tint.g, tint.b, bright * horizon);

                int b = s * 4;
                v[b + 0] = dir - t1 * size;
                v[b + 1] = dir + t2 * size;
                v[b + 2] = dir + t1 * size;
                v[b + 3] = dir - t2 * size;
                col[b] = col[b + 1] = col[b + 2] = col[b + 3] = c;
                int t = s * 6;
                tris[t] = b; tris[t + 1] = b + 1; tris[t + 2] = b + 2;
                tris[t + 3] = b; tris[t + 4] = b + 2; tris[t + 5] = b + 3;
            }
            var m = new Mesh { name = "SB_Stars" };
            m.vertices = v; m.colors = col; m.triangles = tris;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 2.2f);
            return m;
        }

        /// Faceted moon: a 12-sided disc of alternating light/pale triangles with three flat, darker "maria" polygons.
        static Mesh BuildMoonDisc()
        {
            const int seg = 12;
            var verts = new System.Collections.Generic.List<Vector3>();
            var cols = new System.Collections.Generic.List<Color>();
            var tris = new System.Collections.Generic.List<int>();
            var light = new Color(0.97f, 0.96f, 0.9f, 1f);
            var pale = new Color(0.9f, 0.9f, 0.86f, 1f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = (i + 0.25f) / seg * Mathf.PI * 2f, a1 = (i + 1.25f) / seg * Mathf.PI * 2f;
                int b = verts.Count;
                verts.Add(Vector3.zero);
                verts.Add(new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * 0.5f);
                verts.Add(new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * 0.5f);
                var c = (i & 1) == 0 ? light : pale;
                cols.Add(c); cols.Add(c); cols.Add(c);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }
            AddMare(verts, cols, tris, new Vector2(-0.14f, 0.12f), 0.12f, 6, 0.3f);
            AddMare(verts, cols, tris, new Vector2(0.15f, -0.06f), 0.09f, 5, 1.1f);
            AddMare(verts, cols, tris, new Vector2(-0.04f, -0.2f), 0.065f, 5, 2.0f);
            var m = new Mesh { name = "SB_MoonDisc" };
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 1.5f);
            return m;
        }

        static void AddMare(System.Collections.Generic.List<Vector3> v, System.Collections.Generic.List<Color> c,
                            System.Collections.Generic.List<int> t, Vector2 center, float r, int sides, float rot)
        {
            var col = new Color(0.78f, 0.79f, 0.8f, 1f);
            var ctr = new Vector3(center.x, center.y, -0.002f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = rot + i / (float)sides * Mathf.PI * 2f, a1 = rot + (i + 1) / (float)sides * Mathf.PI * 2f;
                int b = v.Count;
                v.Add(ctr);
                v.Add(ctr + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * r);
                v.Add(ctr + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * r);
                c.Add(col); c.Add(col); c.Add(col);
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
            }
        }
    }
}
