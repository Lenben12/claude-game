using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Sealebrities.EditorTools
{
    /// One-click (and batch-mode) project setup: boot scene, build settings, player settings, UI Toolkit theme +
    /// panel settings, shader-include materials (so runtime-created materials survive shader stripping) and the
    /// URP asset values the modules rely on. Every step is idempotent.
    ///   Unity.exe -batchmode -projectPath ... -executeMethod Sealebrities.EditorTools.SealebritiesSetup.SetupOnly
    ///   Unity.exe -batchmode -projectPath ... -executeMethod Sealebrities.EditorTools.SealebritiesSetup.SetupAndBuild
    public static class SealebritiesSetup
    {
        public const string Root = "Assets/_Sealebrities";
        public const string ScenesFolder = Root + "/Scenes";
        public const string ScenePath = ScenesFolder + "/Main.unity";
        public const string ResourcesFolder = Root + "/Resources";
        public const string ShaderIncludesFolder = ResourcesFolder + "/ShaderIncludes";
        public const string ThemePath = ResourcesFolder + "/SealebritiesTheme.tss";
        public const string PanelSettingsPath = ResourcesFolder + "/SealebritiesPanelSettings.asset";
        public const string BuildPath = "C:/Users/Administrator/Desktop/ll/Builds/SEALEBRITIES/SEALEBRITIES.exe";

        /// Only the tiny built-in procedural skybox goes into "Always Included Shaders". The URP shaders must NOT:
        /// always-included shaders keep ALL keyword combinations (shader_feature too), i.e. hundreds of thousands of
        /// URP/Lit variants. The Resources/ShaderIncludes SB_* materials keep exactly the variants the modules use.
        public static bool AddAlwaysIncludedShaders = true;

        static readonly string[] AlwaysIncluded =
        {
            "Skybox/Procedural",
        };

        /// Removed from "Always Included Shaders" on every setup run (earlier setup versions added them).
        static readonly string[] NeverAlwaysIncluded =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Universal Render Pipeline/Terrain/Lit",
            "Universal Render Pipeline/Particles/Unlit",
        };

        // ------------------------------------------------------------------ menu / batch entry points
        [MenuItem("SEALEBRITIES/Setup Project")]
        static void MenuSetup()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RunSetup();
            AssetDatabase.SaveAssets();
            Debug.Log("SEALEBRITIES: project setup complete. Open " + ScenePath + " and press Play.");
        }

        [MenuItem("SEALEBRITIES/Setup + Build (Windows)")]
        static void MenuSetupAndBuild()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SetupAndBuild();
        }

        public static void SetupOnly()
        {
            int code = 0;
            try
            {
                RunSetup();
                AssetDatabase.SaveAssets();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        public static void SetupAndBuild()
        {
            bool ok = false;
            try
            {
                RunSetup();
                AssetDatabase.SaveAssets();
                Directory.CreateDirectory(Path.GetDirectoryName(BuildPath));
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = BuildPath,
                    target = BuildTarget.StandaloneWindows64,
                    targetGroup = BuildTargetGroup.Standalone,
                    options = BuildOptions.None,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                ok = LogReport(report);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ok = false;
            }
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool LogReport(BuildReport report)
        {
            if (report == null)
            {
                Debug.LogError("SEALEBRITIES build: no build report.");
                return false;
            }
            var s = report.summary;
            var sb = new StringBuilder();
            sb.Append("SEALEBRITIES build ").Append(s.result)
              .Append(" | output ").Append(s.outputPath)
              .Append(" | size ").Append((s.totalSize / (1024f * 1024f)).ToString("0.0")).Append(" MB")
              .Append(" | time ").Append(s.totalTime.TotalSeconds.ToString("0")).Append(" s")
              .Append(" | errors ").Append(s.totalErrors)
              .Append(" | warnings ").Append(s.totalWarnings);
            bool ok = s.result == BuildResult.Succeeded;
            if (ok) Debug.Log(sb.ToString());
            else
            {
                Debug.LogError(sb.ToString());
                foreach (var step in report.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception)
                            Debug.LogError("  [" + step.name + "] " + m.content);
            }
            return ok;
        }

        // ------------------------------------------------------------------ setup
        public static void RunSetup()
        {
            EnsureFolder(ScenesFolder);
            EnsureFolder(ResourcesFolder);
            EnsureFolder(ShaderIncludesFolder);

            Try("player settings", SetupPlayerSettings);
            Try("UI theme + panel settings", SetupUiTheme);
            Try("shader include materials", SetupShaderIncludes);
            Try("always included shaders cleanup", RemoveHeavyAlwaysIncludedShaders);
            if (AddAlwaysIncludedShaders) Try("always included shaders", SetupAlwaysIncludedShaders);
            Try("URP asset", SetupRenderPipelineAssets);
            Try("boot scene", SetupScene);
            Try("build settings", () =>
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            });
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
            {
                var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                if (sceneAsset != null) EditorSceneManager.playModeStartScene = sceneAsset;
            }
        }

        static void Try(string what, Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                Debug.LogError("SEALEBRITIES setup: " + what + " failed.");
                Debug.LogException(e);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string name = Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        // ------------------------------------------------------------------ player settings
        static void SetupPlayerSettings()
        {
            PlayerSettings.companyName = "SEALEBRITIES Team";
            PlayerSettings.productName = "SEALEBRITIES";
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground = true;
        }

        // ------------------------------------------------------------------ boot scene
        static void SetupScene()
        {
            Scene scene;
            if (File.Exists(ScenePath))
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }

            // exactly one "Bootstrap" object with a GameBootstrap; nothing else in the scene
            GameObject boot = null;
            var roots = scene.GetRootGameObjects();
            foreach (var go in roots)
            {
                if (boot == null && go.name == "Bootstrap") { boot = go; continue; }
                UnityEngine.Object.DestroyImmediate(go);
            }
            if (boot == null) boot = new GameObject("Bootstrap");
            if (boot.GetComponent<GameBootstrap>() == null) boot.AddComponent<GameBootstrap>();
            boot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                Debug.LogError("SEALEBRITIES setup: could not save " + ScenePath);
        }

        // ------------------------------------------------------------------ UI Toolkit
        static void SetupUiTheme()
        {
            const string themeText = "@import url(\"unity-theme://default\");\n";
            string full = Path.GetFullPath(ThemePath);
            if (!File.Exists(full) || File.ReadAllText(full) != themeText)
            {
                File.WriteAllText(full, themeText);
            }
            AssetDatabase.ImportAsset(ThemePath, ImportAssetOptions.ForceSynchronousImport);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme == null) Debug.LogWarning("SEALEBRITIES setup: " + ThemePath + " did not import as a ThemeStyleSheet.");

            var ps = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            bool created = false;
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                created = true;
            }
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            ps.match = 0.5f;
            if (theme != null) ps.themeStyleSheet = theme;
            if (created) AssetDatabase.CreateAsset(ps, PanelSettingsPath);
            else EditorUtility.SetDirty(ps);
        }

        // ------------------------------------------------------------------ shader stripping protection
        static Texture2D flatNormal;

        static void SetupShaderIncludes()
        {
            flatNormal = FlatNormalTexture();

            Shader lit = Find("Universal Render Pipeline/Lit");
            Shader unlit = Find("Universal Render Pipeline/Unlit");
            Shader terrain = Find("Universal Render Pipeline/Terrain/Lit");
            Shader particles = Find("Universal Render Pipeline/Particles/Unlit");
            Shader sky = Find("Skybox/Procedural");

            if (lit != null)
            {
                // opaque family (props, boat, player, filming rig, creatures)
                Save("SB_Lit_Opaque", Lit(lit));
                Save("SB_Lit_Normal", WithNormal(Lit(lit)));
                Save("SB_Lit_Emissive", WithEmission(Lit(lit)));
                Save("SB_Lit_NormalEmissive", WithEmission(WithNormal(Lit(lit))));
                // alpha-clipped leaves / sea fans
                Save("SB_Lit_AlphaClip", WithAlphaClip(Lit(lit)));
                Save("SB_Lit_AlphaClipNormal", WithAlphaClip(WithNormal(Lit(lit))));
                Save("SB_Lit_AlphaClipEmissive", WithEmission(WithAlphaClip(Lit(lit))));
                // transparent family: exactly MaterialLibrary.MakeTransparent (+ the keyword combos the modules use)
                Save("SB_Lit_Transparent", Transparent(Lit(lit), false));
                Save("SB_Lit_TransparentTwoSided", Transparent(Lit(lit), true));
                Save("SB_Lit_TransparentEmissive", Transparent(WithEmission(Lit(lit)), true));
                Save("SB_Lit_TransparentNormal", Transparent(WithNormal(Lit(lit)), false));
                Save("SB_Lit_TransparentNormalEmissive", Transparent(WithEmission(WithNormal(Lit(lit))), false));
                // ocean top / underside
                Save("SB_Lit_OceanTop", Transparent(WithDetail(WithNormal(Lit(lit))), true));
                Save("SB_Lit_OceanUnder", Transparent(WithEmission(WithDetail(WithNormal(Lit(lit)))), true));
            }
            if (unlit != null)
            {
                Save("SB_Unlit", Basic(unlit));
                var ut = Basic(unlit);
                MaterialLibrary.MakeTransparent(ut, true);
                Save("SB_Unlit_Transparent", ut);
            }
            if (terrain != null)
            {
                var m = new Material(terrain) { name = "SB_TerrainLit" };
                m.EnableKeyword("_NORMALMAP");
                if (m.HasProperty("_EnableInstancedPerPixelNormal"))
                {
                    m.SetFloat("_EnableInstancedPerPixelNormal", 1f);
                    m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                }
                m.enableInstancing = true;
                Save("SB_TerrainLit", m);

                // The runtime terrain has 8 splat layers (layers 5-8 are drawn by the Add Pass) and a base map
                // distance (Base Pass beyond it). No Terrain asset exists at build time, so these dependency shaders
                // need their own include materials. The keywords are set unconditionally: only TerrainLit declares
                // the _EnableInstancedPerPixelNormal property, the dependency shaders only declare the keyword.
                SaveTerrainDependency(terrain, "AddPassShader", "Hidden/Universal Render Pipeline/Terrain/Lit (Add Pass)", "SB_TerrainLit_AddPass", true);
                SaveTerrainDependency(terrain, "BaseMapShader", "Hidden/Universal Render Pipeline/Terrain/Lit (Base Pass)", "SB_TerrainLit_BasePass", true);
                // the base map is regenerated at runtime
                SaveTerrainDependency(terrain, "BaseMapGenShader", "Hidden/Universal Render Pipeline/Terrain/Lit (Basemap Gen)", "SB_TerrainLit_BasemapGen", false);
            }
            if (particles != null)
            {
                Save("SB_Particles_Alpha", Particle(particles, false));
                Save("SB_Particles_Additive", Particle(particles, true));
            }
            if (sky != null)
            {
                var m = new Material(sky) { name = "SB_SkyboxProcedural" };
                if (m.HasProperty("_SunDisk")) m.SetFloat("_SunDisk", 2f);
                m.DisableKeyword("_SUNDISK_NONE");
                m.DisableKeyword("_SUNDISK_SIMPLE");
                m.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                Save("SB_SkyboxProcedural", m);
            }
        }

        static void SaveTerrainDependency(Shader terrain, string dependency, string fallbackName, string assetName, bool litKeywords)
        {
            Shader sh = null;
            try { sh = terrain.GetDependency(dependency); }
            catch (Exception) { sh = null; }
            if (sh == null) sh = Shader.Find(fallbackName);
            if (sh == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: terrain dependency shader not found: " + fallbackName);
                return;
            }
            var m = new Material(sh);
            if (litKeywords)
            {
                m.EnableKeyword("_NORMALMAP");
                m.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                m.enableInstancing = true;
            }
            Save(assetName, m);
        }

        static Shader Find(string name)
        {
            var s = Shader.Find(name);
            if (s == null) Debug.LogWarning("SEALEBRITIES setup: shader not found: " + name);
            return s;
        }

        static Material Lit(Shader lit)
        {
            var m = new Material(lit);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.3f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            return m;
        }

        static Material Basic(Shader s)
        {
            var m = new Material(s);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            return m;
        }

        static Material WithNormal(Material m)
        {
            m.SetTexture("_BumpMap", flatNormal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            return m;
        }

        static Material WithDetail(Material m)
        {
            m.SetTexture("_DetailNormalMap", flatNormal);
            m.SetFloat("_DetailNormalMapScale", 0.3f);
            if (m.HasProperty("_DetailAlbedoMapScale")) m.SetFloat("_DetailAlbedoMapScale", 1f);
            m.EnableKeyword("_DETAIL_MULX2");
            return m;
        }

        static Material WithEmission(Material m)
        {
            m.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }

        static Material WithAlphaClip(Material m)
        {
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "TransparentCutout");
            m.renderQueue = (int)RenderQueue.AlphaTest;
            return m;
        }

        static Material Transparent(Material m, bool twoSided)
        {
            MaterialLibrary.MakeTransparent(m, twoSided);
            return m;
        }

        static Material Particle(Shader sh, bool additive)
        {
            var m = new Material(sh);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.DisableKeyword("_ALPHAMODULATE_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        /// Creates or updates (keeping the GUID) a material asset in Resources/ShaderIncludes.
        static void Save(string name, Material m)
        {
            string path = ShaderIncludesFolder + "/" + name + ".mat";
            m.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(m, path);
            }
            else
            {
                EditorUtility.CopySerialized(m, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(m);
            }
        }

        static Texture2D FlatNormalTexture()
        {
            string path = ShaderIncludesFolder + "/SB_FlatNormal.asset";
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) return tex;
            tex = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "SB_FlatNormal" };
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 255, 255);
            tex.SetPixels32(px);
            tex.Apply(false, false);
            AssetDatabase.CreateAsset(tex, path);
            return tex;
        }

        static bool OpenAlwaysIncluded(out SerializedObject so, out SerializedProperty list)
        {
            so = null;
            list = null;
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: GraphicsSettings.asset not found.");
                return false;
            }
            so = new SerializedObject(assets[0]);
            list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("SEALEBRITIES setup: m_AlwaysIncludedShaders not found in GraphicsSettings.");
                return false;
            }
            return true;
        }

        /// Runs on every setup, independent of AddAlwaysIncludedShaders: takes the URP shaders back out of
        /// "Always Included Shaders" (all-variant compilation would explode the player build).
        static void RemoveHeavyAlwaysIncludedShaders()
        {
            if (!OpenAlwaysIncluded(out var so, out var list)) return;
            var remove = new HashSet<Shader>();
            foreach (var name in NeverAlwaysIncluded)
            {
                var sh = Shader.Find(name);
                if (sh != null) remove.Add(sh);
            }
            if (remove.Count == 0) return;
            int removed = 0;
            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                var el = list.GetArrayElementAtIndex(i);
                var sh = el.objectReferenceValue as Shader;
                if (sh == null || !remove.Contains(sh)) continue;
                el.objectReferenceValue = null;   // object references must be cleared before the element can be deleted
                list.DeleteArrayElementAtIndex(i);
                removed++;
            }
            if (removed == 0) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("SEALEBRITIES setup: removed " + removed + " URP shader(s) from Always Included Shaders.");
        }

        static void SetupAlwaysIncludedShaders()
        {
            if (!OpenAlwaysIncluded(out var so, out var list)) return;
            var present = new HashSet<Shader>();
            for (int i = 0; i < list.arraySize; i++)
            {
                var sh = list.GetArrayElementAtIndex(i).objectReferenceValue as Shader;
                if (sh != null) present.Add(sh);
            }
            bool changed = false;
            foreach (var name in AlwaysIncluded)
            {
                var sh = Shader.Find(name);
                if (sh == null) { Debug.LogWarning("SEALEBRITIES setup: shader not found: " + name); continue; }
                if (present.Contains(sh)) continue;
                int idx = list.arraySize;
                list.InsertArrayElementAtIndex(idx);
                list.GetArrayElementAtIndex(idx).objectReferenceValue = sh;
                present.Add(sh);
                changed = true;
            }
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }
        }

        // ------------------------------------------------------------------ URP asset values the modules rely on
        static void SetupRenderPipelineAssets()
        {
            var assets = new List<RenderPipelineAsset>();
            if (GraphicsSettings.defaultRenderPipeline != null) assets.Add(GraphicsSettings.defaultRenderPipeline);
            int levels = QualitySettings.names.Length;
            for (int i = 0; i < levels; i++)
            {
                var a = QualitySettings.GetRenderPipelineAssetAt(i);
                if (a != null && !assets.Contains(a)) assets.Add(a);
            }
            foreach (var a in assets)
            {
                var so = new SerializedObject(a);
                bool changed = false;
                changed |= SetFloat(so, "m_ShadowDistance", 150f);            // World: soft sun shadows up to 150 m
                changed |= SetBool(so, "m_SupportsLightCookies", true);       // FX: caustics are a sun cookie
                changed |= SetBool(so, "m_SupportsHDR", true);
                changed |= SetBool(so, "m_MainLightShadowsSupported", true);
                if (changed)
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(a);
                }
            }
        }

        static bool SetFloat(SerializedObject so, string prop, float v)
        {
            var p = so.FindProperty(prop);
            if (p == null || p.propertyType != SerializedPropertyType.Float) return false;
            if (Mathf.Approximately(p.floatValue, v)) return false;
            p.floatValue = v;
            return true;
        }

        static bool SetBool(SerializedObject so, string prop, bool v)
        {
            var p = so.FindProperty(prop);
            if (p == null || p.propertyType != SerializedPropertyType.Boolean) return false;
            if (p.boolValue == v) return false;
            p.boolValue = v;
            return true;
        }
    }

    /// Editor convenience: pressing Play always boots the game from Main.unity (whatever scene is open), and a
    /// missing setup is pointed out once per editor session.
    [InitializeOnLoad]
    static class SealebritiesPlayModeHook
    {
        static SealebritiesPlayModeHook()
        {
            EditorApplication.delayCall += Apply;
        }

        static void Apply()
        {
            if (Application.isBatchMode) return;
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SealebritiesSetup.ScenePath);
            if (scene != null)
            {
                if (EditorSceneManager.playModeStartScene != scene) EditorSceneManager.playModeStartScene = scene;
                return;
            }
            if (SessionState.GetBool("SEALEBRITIES_SetupHint", false)) return;
            SessionState.SetBool("SEALEBRITIES_SetupHint", true);
            Debug.LogWarning("SEALEBRITIES: the boot scene does not exist yet. Run the menu SEALEBRITIES > Setup Project once, then press Play.");
        }
    }
}
