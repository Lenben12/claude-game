using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
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
            Try("PC URP asset (MSAA 4x, soft shadows, How to Fish shadows)", SetupPcPipelineAsset);
            Try("PC renderer (screen space ambient occlusion)", SetupPcRendererSsao);
            Try("PC renderer without SSAO (Low graphics tier)", SetupPcRendererNoAo);
            Try("post-processing variant guard (ACES, HQ bloom)", SetupPostVariantGuard);
            Try("fog shader variants", SetupFogVariants);
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
        public const string ObsoleteIncludesFolder = Root + "/Obsolete/ShaderIncludes";

        /// Include materials of the old semi-realistic look (normal/detail maps, alpha-clipped leaves, Unity Terrain).
        /// The low-poly look uses none of these variants; materials in Resources are always built, so setup moves
        /// them out of Resources (non-destructive) to keep their variants and the Terrain/Lit shader out of builds.
        static readonly string[] ObsoleteIncludes =
        {
            "SB_Lit_Normal.mat", "SB_Lit_NormalEmissive.mat",
            "SB_Lit_AlphaClip.mat", "SB_Lit_AlphaClipNormal.mat", "SB_Lit_AlphaClipEmissive.mat",
            "SB_Lit_TransparentNormal.mat", "SB_Lit_TransparentNormalEmissive.mat",
            "SB_Lit_OceanTop.mat", "SB_Lit_OceanUnder.mat",
            "SB_TerrainLit.mat", "SB_TerrainLit_AddPass.mat", "SB_TerrainLit_BasePass.mat", "SB_TerrainLit_BasemapGen.mat",
            "SB_FlatNormal.asset",
        };

        /// Keeps exactly the URP variants the low-poly modules create at runtime: flat opaque (LowPoly.Mat, block
        /// textures), emissive (lamps, lure, glows, clouds), transparent one/two-sided (ocean top, glass, jelly) and
        /// transparent emissive (ocean underside, jelly), plus unlit, particles and the procedural skybox.
        static void SetupShaderIncludes()
        {
            RetireObsoleteIncludes();

            Shader lit = Find("Universal Render Pipeline/Lit");
            Shader unlit = Find("Universal Render Pipeline/Unlit");
            Shader particles = Find("Universal Render Pipeline/Particles/Unlit");
            Shader sky = Find("Skybox/Procedural");

            if (lit != null)
            {
                // opaque family (terrain chunks, props, boat, player, filming rig, creatures)
                Save("SB_Lit_Opaque", Lit(lit));
                Save("SB_Lit_Emissive", WithEmission(Lit(lit)));
                // transparent family: exactly MaterialLibrary.MakeTransparent (+ the keyword combos the modules use)
                Save("SB_Lit_Transparent", Transparent(Lit(lit), false));
                Save("SB_Lit_TransparentTwoSided", Transparent(Lit(lit), true));
                Save("SB_Lit_TransparentEmissive", Transparent(WithEmission(Lit(lit)), true));
            }
            if (unlit != null)
            {
                Save("SB_Unlit", Basic(unlit));
                var ut = Basic(unlit);
                MaterialLibrary.MakeTransparent(ut, true);
                Save("SB_Unlit_Transparent", ut);
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
            // art bible §1.2 option B: a runtime gradient cubemap sky on Skybox/Cubemap (World) must survive stripping
            Shader cube = Shader.Find("Skybox/Cubemap");
            if (cube != null) Save("SB_SkyboxCubemap", new Material(cube) { name = "SB_SkyboxCubemap" });
        }

        /// Moves the include materials of the old look from Resources/ShaderIncludes to Obsolete/ShaderIncludes
        /// (kept, not deleted; GUIDs survive). Idempotent: nothing happens once they are gone from Resources.
        static void RetireObsoleteIncludes()
        {
            int moved = 0;
            foreach (var file in ObsoleteIncludes)
            {
                string from = ShaderIncludesFolder + "/" + file;
                if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(from)) || AssetDatabase.LoadMainAssetAtPath(from) == null) continue;
                EnsureFolder(ObsoleteIncludesFolder);
                string to = AssetDatabase.GenerateUniqueAssetPath(ObsoleteIncludesFolder + "/" + file);
                string err = AssetDatabase.MoveAsset(from, to);
                if (string.IsNullOrEmpty(err)) moved++;
                else Debug.LogWarning("SEALEBRITIES setup: could not move " + from + " out of Resources: " + err);
            }
            if (moved > 0)
                Debug.Log("SEALEBRITIES setup: moved " + moved + " obsolete shader include asset(s) (normal/detail/alpha-clip/terrain) to " + ObsoleteIncludesFolder + ".");
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

        static Material WithEmission(Material m)
        {
            m.SetColor("_EmissionColor", new Color(0.5f, 0.5f, 0.5f, 1f));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
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
                changed |= SetFloat(so, "m_ShadowDistance", 120f);            // art bible §1.3 (High tier; FX lowers it per tier)
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

        // ------------------------------------------------------------------ low-poly look: PC renderer + URP asset
        // These are the High-tier defaults of the "How to Fish" art bible (§1.3 / §1.5). At runtime the FX module
        // (FXGraphics) applies the player's graphics tier (Low / Medium / High) on top of them.
        const string PcPipelineAssetPath = "Assets/Settings/PC_RPAsset.asset";
        const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";
        const string PcRendererNoAoPath = "Assets/Settings/PC_Renderer_NoAO.asset";
        const string DefaultVolumeProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
        const string SsaoTypeName = "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion";
        const string SsaoAssemblyQualifiedName = SsaoTypeName + ", Unity.RenderPipelines.Universal.Runtime";
        const int MsaaSamples = 4;
        const int SoftShadowQualityMedium = 2;      // SoftShadowQuality.Medium
        const int ShadowmapResolution = 2048;
        const int ShadowCascades = 2;
        const float Cascade2Split = 0.25f;
        const float ShadowDepthBias = 1.0f;
        const float ShadowNormalBias = 0.6f;
        // Art bible: intensity 1.0, radius 0.30 m, direct lighting 0.25 – and 0.22 m if the view-model hands blotch.
        // The hands are small now, but the earlier blotching with 0.3 m makes 0.24 m the safer middle.
        const float SsaoIntensity = 1.0f;
        const float SsaoRadius = 0.24f;
        const float SsaoDirectLighting = 0.25f;

        static RenderPipelineAsset PcPipelineAsset()
        {
            var a = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PcPipelineAssetPath);
            if (a == null) a = GraphicsSettings.defaultRenderPipeline;
            return a;
        }

        /// MSAA 4x keeps the faceted low-poly edges crisp (the FX module switches the main camera from post AA to
        /// MSAA when the asset has it) and soft shadows give the slightly soft "toy diorama" shadow edge.
        static void SetupPcPipelineAsset()
        {
            var asset = PcPipelineAsset();
            if (asset == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: PC URP asset not found (" + PcPipelineAssetPath + "): MSAA / soft shadows skipped.");
                return;
            }
            var so = new SerializedObject(asset);
            bool changed = false;
            if (so.FindProperty("m_MSAA") == null) Debug.LogWarning("SEALEBRITIES setup: m_MSAA not found on " + asset.name + ".");
            changed |= SetInt(so, "m_MSAA", MsaaSamples);
            changed |= SetFloat(so, "m_RenderScale", 1f);
            if (so.FindProperty("m_SoftShadowsSupported") == null) Debug.LogWarning("SEALEBRITIES setup: m_SoftShadowsSupported not found on " + asset.name + ".");
            changed |= SetBool(so, "m_SoftShadowsSupported", true);
            changed |= SetInt(so, "m_SoftShadowQuality", SoftShadowQualityMedium);   // Medium: soft but defined penumbra
            // strong How-to-Fish sun shadows: 2048 map, 2 cascades split at 25 %, 120 m (the cascade count stays 2 on
            // every tier: URP strips the single-cascade shadow variants once the asset uses cascades)
            changed |= SetInt(so, "m_MainLightShadowmapResolution", ShadowmapResolution);
            changed |= SetInt(so, "m_ShadowCascadeCount", ShadowCascades);
            changed |= SetFloat(so, "m_Cascade2Split", Cascade2Split);
            changed |= SetFloat(so, "m_ShadowDistance", 120f);
            changed |= SetFloat(so, "m_ShadowDepthBias", ShadowDepthBias);
            changed |= SetFloat(so, "m_ShadowNormalBias", ShadowNormalBias);
            // nothing samples the camera opaque/depth textures (no custom shaders, SSAO renders its own depth-normals):
            // the copies only cost a full-screen blit / depth pass every frame
            changed |= SetBool(so, "m_RequireOpaqueTexture", false);
            changed |= SetBool(so, "m_RequireDepthTexture", false);
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                Debug.Log("SEALEBRITIES setup: " + asset.name + " -> MSAA " + MsaaSamples + "x, soft shadows (medium), shadow map " +
                          ShadowmapResolution + " / " + ShadowCascades + " cascades / 120 m, no opaque/depth copies.");
            }
            else Debug.Log("SEALEBRITIES setup: " + asset.name + " already has the How to Fish render settings.");
        }

        static ScriptableObject PcRendererData()
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PcRendererPath);
            if (data != null) return data;
            // fallback: the first renderer of the PC pipeline asset
            var asset = PcPipelineAsset();
            if (asset == null) return null;
            var list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            if (list == null || !list.isArray || list.arraySize == 0) return null;
            return list.GetArrayElementAtIndex(0).objectReferenceValue as ScriptableObject;
        }

        static Type FindType(string assemblyQualifiedName, string fullName)
        {
            Type t = null;
            try { t = Type.GetType(assemblyQualifiedName, false); } catch (Exception) { t = null; }
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = asm.GetType(fullName, false); } catch (Exception) { t = null; }
                if (t != null) return t;
            }
            return null;
        }

        /// Soft ambient occlusion in the creases of the chunky low-poly props (How to Fish look). The feature
        /// class may be internal in some URP versions, so it is created by reflection and wired into the
        /// renderer's feature list + feature map exactly like the URP renderer inspector does. Idempotent.
        static void SetupPcRendererSsao()
        {
            var data = PcRendererData();
            if (data == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: PC renderer data not found (" + PcRendererPath + "): SSAO skipped.");
                return;
            }
            Type ssaoType = FindType(SsaoAssemblyQualifiedName, SsaoTypeName);
            if (ssaoType == null || ssaoType.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(ssaoType))
            {
                Debug.LogWarning("SEALEBRITIES setup: URP type " + SsaoTypeName + " not found: SSAO skipped.");
                return;
            }

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            if (features == null || !features.isArray)
            {
                Debug.LogWarning("SEALEBRITIES setup: m_RendererFeatures not found on " + data.name + ": SSAO skipped.");
                return;
            }

            ScriptableObject ssao = null;
            for (int i = 0; i < features.arraySize; i++)
            {
                var o = features.GetArrayElementAtIndex(i).objectReferenceValue;
                if (o != null && ssaoType.IsInstanceOfType(o)) { ssao = (ScriptableObject)o; break; }
            }

            bool created = false;
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance(ssaoType);
                if (ssao == null)
                {
                    Debug.LogWarning("SEALEBRITIES setup: could not create " + SsaoTypeName + ": SSAO skipped.");
                    return;
                }
                ssao.name = ssaoType.Name;
                AssetDatabase.AddObjectToAsset(ssao, data);

                int n = features.arraySize;
                int oldMapSize = map != null && map.isArray ? map.arraySize : 0;
                features.arraySize = n + 1;
                features.GetArrayElementAtIndex(n).objectReferenceValue = ssao;
                if (map != null && map.isArray)
                {
                    // the map holds the local file id of every feature sub-asset (URP uses it to re-link features)
                    map.arraySize = features.arraySize;
                    for (int i = oldMapSize; i < features.arraySize; i++)
                    {
                        var o = features.GetArrayElementAtIndex(i).objectReferenceValue;
                        long id = 0;
                        if (o != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long localId)) id = localId;
                        map.GetArrayElementAtIndex(i).longValue = id;
                    }
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                created = true;
            }

            var fso = new SerializedObject(ssao);
            bool changed = false;
            changed |= SetBool(fso, "m_Active", true);
            changed |= SetFloatLogged(fso, "m_Settings.Intensity", SsaoIntensity, data.name);
            changed |= SetFloatLogged(fso, "m_Settings.Radius", SsaoRadius, data.name);
            changed |= SetFloatLogged(fso, "m_Settings.DirectLightingStrength", SsaoDirectLighting, data.name);
            changed |= SetInt(fso, "m_Settings.Samples", 1);        // AOSampleOption.Medium
            changed |= SetBool(fso, "m_Settings.Downsample", false);   // full resolution on High (FX halves it on Medium)
            changed |= SetInt(fso, "m_Settings.BlurQuality", 0);    // bilateral (High)
            changed |= SetBool(fso, "m_Settings.AfterOpaque", false);   // AO only on ambient light, under the fog
            if (changed) fso.ApplyModifiedPropertiesWithoutUndo();

            if (created || changed)
            {
                EditorUtility.SetDirty(ssao);
                EditorUtility.SetDirty(data);
                if (data is UnityEngine.Rendering.Universal.ScriptableRendererData rd) rd.SetDirty();
                AssetDatabase.SaveAssets();
                Debug.Log("SEALEBRITIES setup: " + (created ? "added" : "updated") + " Screen Space Ambient Occlusion on " + data.name +
                          " (intensity " + SsaoIntensity + ", radius " + SsaoRadius + ").");
            }
            else Debug.Log("SEALEBRITIES setup: Screen Space Ambient Occlusion already configured on " + data.name + ".");
        }

        /// Low graphics tier: SSAO off. URP strips the "SSAO off" shader variants as soon as EVERY renderer of the
        /// pipeline has SSAO, so simply disabling the feature at runtime would hit missing variants in a build. A copy
        /// of the PC renderer with its SSAO inactive is added as renderer #1: both variants stay in the build and the
        /// FX module switches the camera to this renderer on Low. Idempotent.
        static void SetupPcRendererNoAo()
        {
            var asset = PcPipelineAsset();
            var main = PcRendererData();
            if (asset == null || main == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: PC pipeline asset or renderer missing: no-AO renderer skipped.");
                return;
            }

            var noAo = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PcRendererNoAoPath);
            if (noAo == null)
            {
                string from = AssetDatabase.GetAssetPath(main);
                if (string.IsNullOrEmpty(from) || !AssetDatabase.CopyAsset(from, PcRendererNoAoPath))
                {
                    Debug.LogWarning("SEALEBRITIES setup: could not copy " + from + " to " + PcRendererNoAoPath + ".");
                    return;
                }
                AssetDatabase.ImportAsset(PcRendererNoAoPath, ImportAssetOptions.ForceSynchronousImport);
                noAo = AssetDatabase.LoadAssetAtPath<ScriptableObject>(PcRendererNoAoPath);
                if (noAo == null) { Debug.LogWarning("SEALEBRITIES setup: " + PcRendererNoAoPath + " did not import."); return; }
                noAo.name = Path.GetFileNameWithoutExtension(PcRendererNoAoPath);
            }

            // every SSAO feature of the copy inactive
            Type ssaoType = FindType(SsaoAssemblyQualifiedName, SsaoTypeName);
            var so = new SerializedObject(noAo);
            var features = so.FindProperty("m_RendererFeatures");
            bool featureChanged = false;
            if (features != null && features.isArray)
            {
                for (int i = 0; i < features.arraySize; i++)
                {
                    var o = features.GetArrayElementAtIndex(i).objectReferenceValue;
                    if (o == null || ssaoType == null || !ssaoType.IsInstanceOfType(o)) continue;
                    var fso = new SerializedObject(o);
                    if (SetBool(fso, "m_Active", false))
                    {
                        fso.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(o);
                        featureChanged = true;
                    }
                }
            }
            if (featureChanged)
            {
                EditorUtility.SetDirty(noAo);
                if (noAo is ScriptableRendererData rd) rd.SetDirty();
            }

            // renderer list of the PC pipeline asset: [0] PC_Renderer (default), [1] PC_Renderer_NoAO
            var aso = new SerializedObject(asset);
            var list = aso.FindProperty("m_RendererDataList");
            if (list == null || !list.isArray)
            {
                Debug.LogWarning("SEALEBRITIES setup: m_RendererDataList not found on " + asset.name + ": no-AO renderer not registered.");
                return;
            }
            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == noAo) { present = true; break; }
            if (!present)
            {
                int n = list.arraySize;
                list.arraySize = n + 1;
                list.GetArrayElementAtIndex(n).objectReferenceValue = noAo;
                aso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            if (!present || featureChanged)
            {
                AssetDatabase.SaveAssets();
                Debug.Log("SEALEBRITIES setup: " + PcRendererNoAoPath + " (SSAO off) registered on " + asset.name + " for the Low graphics tier.");
            }
        }

        /// The FX volume profile is created at runtime, but "Strip Unused Post Processing Variants" only keeps the
        /// post variants used by volume profile assets. The pipeline's default profile (always in the build) gets
        /// ACES tonemapping and high-quality bloom filtering so the ACES / HQ-bloom uber variants survive; its bloom
        /// intensity stays 0, so nothing changes visually for cameras without the FX volume.
        static void SetupPostVariantGuard()
        {
            VolumeProfile profile = null;
            var asset = PcPipelineAsset();
            if (asset != null)
            {
                var p = new SerializedObject(asset).FindProperty("m_VolumeProfile");
                if (p != null) profile = p.objectReferenceValue as VolumeProfile;
            }
            if (profile == null) profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultVolumeProfilePath);
            if (profile == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: default volume profile not found: post variant guard skipped.");
                return;
            }
            bool changed = false;
            if (profile.TryGet(out Tonemapping tm) && tm != null)
            {
                if (!tm.mode.overrideState || tm.mode.value != TonemappingMode.ACES)
                {
                    tm.mode.overrideState = true;
                    tm.mode.value = TonemappingMode.ACES;
                    EditorUtility.SetDirty(tm);
                    changed = true;
                }
            }
            else Debug.LogWarning("SEALEBRITIES setup: " + profile.name + " has no Tonemapping component (ACES variants may be stripped).");
            if (profile.TryGet(out Bloom bloom) && bloom != null)
            {
                if (!bloom.highQualityFiltering.overrideState || !bloom.highQualityFiltering.value)
                {
                    bloom.highQualityFiltering.overrideState = true;
                    bloom.highQualityFiltering.value = true;
                    EditorUtility.SetDirty(bloom);
                    changed = true;
                }
            }
            if (changed)
            {
                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
                Debug.Log("SEALEBRITIES setup: " + profile.name + " -> ACES + HQ bloom (keeps the runtime FX post variants in builds).");
            }
        }

        /// The FX module turns fog on at runtime (exponential above water, exponential squared under water), but the
        /// boot scene itself has fog off: with "Automatic" fog stripping the player build would drop every fog shader
        /// variant and the fog would vanish in builds. Keep EXP + EXP2 explicitly; linear fog is never used.
        static void SetupFogVariants()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: GraphicsSettings.asset not found: fog variants not pinned.");
                return;
            }
            var so = new SerializedObject(assets[0]);
            if (so.FindProperty("m_FogStripping") == null)
            {
                Debug.LogWarning("SEALEBRITIES setup: m_FogStripping not found in GraphicsSettings: fog variants not pinned.");
                return;
            }
            bool changed = false;
            changed |= SetInt(so, "m_FogStripping", 1);   // 1 = Custom
            changed |= SetBool(so, "m_FogKeepExp", true);
            changed |= SetBool(so, "m_FogKeepExp2", true);
            changed |= SetBool(so, "m_FogKeepLinear", false);
            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                Debug.Log("SEALEBRITIES setup: fog stripping set to Custom (keep Exponential + Exponential Squared).");
            }
        }

        static bool SetInt(SerializedObject so, string prop, int v)
        {
            var p = so.FindProperty(prop);
            if (p == null) return false;
            if (p.propertyType != SerializedPropertyType.Integer && p.propertyType != SerializedPropertyType.Enum) return false;
            if (p.intValue == v) return false;
            p.intValue = v;
            return true;
        }

        static bool SetFloatLogged(SerializedObject so, string prop, float v, string owner)
        {
            var p = so.FindProperty(prop);
            if (p == null || p.propertyType != SerializedPropertyType.Float)
            {
                Debug.LogWarning("SEALEBRITIES setup: property " + prop + " not found on " + owner + ".");
                return false;
            }
            if (Mathf.Approximately(p.floatValue, v)) return false;
            p.floatValue = v;
            return true;
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
