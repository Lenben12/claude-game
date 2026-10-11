using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Global boot flags (read by AutoShot and tools). Ready = every module is installed and the main menu is open.
    public static class BootState
    {
        public static bool Ready;
        /// True when at least one boot step threw (the game still continues with the remaining modules).
        public static bool HadErrors;
        /// Real seconds the boot took (0 until Ready).
        public static float BootSeconds;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Ready = false; HadErrors = false; BootSeconds = 0f; }
    }

    /// The only object in Main.unity. Boot order:
    ///   Awake  – GameManager (Settings.Load + Settings.Apply: frame cap, vSync, volumes, language, key bindings),
    ///            Steam (SteamService.Init: SteamAPI.InitEx without RestartAppIfNecessary, relay network warm-up,
    ///            callbacks pumped every frame; fails quietly when Steam is not running – co-op per IP still works),
    ///            co-op module (NetBoot: transport pump, session, lobby, command line -host/-join, Steam join requests)
    ///            and net-sync (avatars, boat, creatures, voice), world root, loading camera.
    ///            (The test cheats are gone – player decision Q100. Test harnesses only run with their own flags:
    ///            -autoplaytest, -autoplaytest-mp, -autoshot, -steamselftest, -voiceloopback.)
    ///   Boot() – behind the loading screen: UI, World, Player, Boat, Fauna, Filming, FX, Npc (story cast),
    ///            Livestream (KRILL.tv + sponsor deals), creature prewarm.
    ///   Then   – UIModule.ShowMainMenu: on the first menu after launch the UI runs the launch intro
    ///            (UIIntro → PlayerIntro.Play, skippable, captions) and opens the menu when it ends; the Player module
    ///            shows the bean-diver crew on the dock behind the menu. PlayerIntro skips itself for -host, -join,
    ///            -autoshot, -autoplaytest, -autoplaytest-mp and -nointro. With -host / -join NetBoot starts or joins
    ///            the co-op session as soon as the menu is up.
    /// Every step is guarded: a module that throws is logged and the boot continues with the next one.
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        static GameBootstrap instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        Transform root;
        GameObject bootCamera;
        bool subscribed;

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            BootState.Ready = false;
            BootState.HadErrors = false;

            // Frame cap, vSync and volumes come from the player's settings (Settings.Apply, run by GameManager.Awake);
            // shadow distance comes from the URP asset (written by SealebritiesSetup) – URP ignores QualitySettings.
            if (GameManager.I == null)
            {
                var gm = new GameObject("GameManager");
                gm.AddComponent<GameManager>();   // Awake: Settings.Load + Settings.Apply
            }
            else
            {
                Step("Settings.Apply", Settings.Apply);
            }

            // Steam before co-op: the lobby screen offers "Über Steam" only when Steam is up, and Steam join requests
            // (+connect_lobby, accepted invites) are registered before the menu opens. Skipped for automated test runs.
            Step("SteamService.Init", SteamService.Init);

            // Co-op early, so the lobby, the command line (-host / -join) and the replication handlers exist before
            // any module installs. Both are idempotent (they also self-install after the scene load).
            Step("NetBoot.EnsureInstalled", NetBoot.EnsureInstalled);
            Step("NetWorldSync.Install", () => NetWorldSync.Install());

            var world = new GameObject("World");
            root = world.transform;
            Refs.WorldRoot = root;

            CreateBootCamera();
        }

        void Start()
        {
            if (instance != this) return;
            StartCoroutine(Boot());
        }

        void OnDestroy()
        {
            if (subscribed)
            {
                GameEvents.ReturnedToMenu -= OnReturnedToMenu;
                subscribed = false;
            }
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------------ boot sequence
        IEnumerator Boot()
        {
            float t0 = Time.realtimeSinceStartup;

            // 1. UI + loading screen
            Step("UIModule.Install", () => UIModule.Install(root));
            Step("UIModule.ShowLoading", () => UIModule.ShowLoading(0f, Loc.Tr("Lade …", "Loading …")));
            yield return null;   // let the loading screen draw once before the heavy work starts

            // 2. World (terrain, ocean, sky, props) – reports 0..85 %
            IEnumerator worldBuild = null;
            Step("WorldBuilder.Build", () => worldBuild = WorldBuilder.Build(root, OnWorldProgress));
            if (worldBuild != null) yield return Guarded(worldBuild, "WorldBuilder.Build");
            if (!WorldInfo.Built)
            {
                BootState.HadErrors = true;
                Debug.LogError("Sealebrities: the world did not finish building – continuing with what exists.");
                BootFallbacks.FillMissingWorldInfo();
            }

            // 3. Player (owns the Main Camera)
            Progress(0.86f, "Taucher wird angezogen …", "Suiting up the diver …");
            yield return null;
            Step("PlayerFactory.Create", () => PlayerFactory.Create(root));
            if (Refs.Player != null && Refs.Player.View != null) DestroyBootCamera();

            // 4. Boat, helicopter, fishing
            Progress(0.88f, "Die Rostlaube wird betankt …", "Fueling the rust bucket …");
            yield return null;
            Step("BoatModule.Install", () => BoatModule.Install(root));

            // 5. Fauna
            Progress(0.90f, "Fische werden geweckt …", "Waking up the fish …");
            yield return null;
            Step("FaunaModule.Install", () => FaunaModule.Install(root));

            // 6. Filming (hand camera + tutorial)
            Progress(0.91f, "Kamera wird geladen …", "Charging the camera …");
            yield return null;
            Step("FilmingModule.Install", () => FilmingModule.Install(root));

            // 7. FX (post, fog, caustics, particles, audio)
            Progress(0.92f, "Wasser wird nass gemacht …", "Making the water wet …");
            yield return null;
            Step("FXModule.Install", () => FXModule.Install(root));

            // 8. Story NPCs (Horst, Wanda, Gus' radio, the village, visitors) and the livestream / sponsor services.
            //    Both also install themselves; installing here makes the cast exist before the menu opens.
            Progress(0.92f, "Horst sucht seine Sonnenbrille …", "Horst is looking for his sunglasses …");
            yield return null;
            Step("NpcModule.Install", () => NpcModule.Install(root));
            Step("LivestreamModule.Install", () => LivestreamModule.Install(root));

            // Warm-up: build every creature model + journal icon now instead of mid-dive or on the first Sealog open.
            yield return PrewarmCreatures(0.92f, 0.98f);

            // A few frames so every Start() ran, shaders are compiled and the menu camera is in place.
            for (int i = 0; i < 6; i++)
            {
                Progress(0.98f + i * 0.0033f, "Gleich geht's los …", "Almost there …");
                yield return null;
            }

            if (bootCamera != null && Camera.main != null) DestroyBootCamera();

            Step("UIModule.HideLoading", UIModule.HideLoading);
            // First menu after launch: the UI plays the launch intro (PlayerIntro.Play) and opens the menu when it ends
            // or is skipped. Without a UI the intro still runs so the camera ends on the menu crew.
            if (UIModule.Controller != null) Step("UIModule.ShowMainMenu", UIModule.ShowMainMenu);
            else
            {
                UIState.MainMenuOpen = true;
                Step("PlayerIntro.Play", () => PlayerIntro.Play(null));
            }

            if (!subscribed)
            {
                GameEvents.ReturnedToMenu += OnReturnedToMenu;
                subscribed = true;
            }

            BootState.BootSeconds = Time.realtimeSinceStartup - t0;
            BootState.Ready = true;
            Debug.Log("Sealebrities: boot finished in " + BootState.BootSeconds.ToString("0.0") + " s" +
                      (BootState.HadErrors ? " (with errors, see log above)." : "."));
        }

        void OnReturnedToMenu()
        {
            // The UI already reopens the menu on this event; only step in if it did not.
            if (!UIState.MainMenuOpen) Step("UIModule.ShowMainMenu", UIModule.ShowMainMenu);
        }

        static void OnWorldProgress(float p, string text)
        {
            UIModule.ShowLoading(Mathf.Clamp01(p) * 0.85f, text);
        }

        static void Progress(float p, string de, string en)
        {
            Step("UIModule.ShowLoading", () => UIModule.ShowLoading(p, Loc.Tr(de, en)));
        }

        static void Step(string what, Action action)
        {
            try { action(); }
            catch (Exception e)
            {
                BootState.HadErrors = true;
                Debug.LogError("Sealebrities boot: " + what + " failed.");
                Debug.LogException(e);
            }
        }

        /// Steps a build coroutine manually so an exception inside it is logged instead of killing the boot.
        /// Nested IEnumerators are run inline; every other yield instruction is handed to Unity unchanged.
        static IEnumerator Guarded(IEnumerator inner, string what)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                var e = stack.Peek();
                object current;
                try
                {
                    if (!e.MoveNext()) { stack.Pop(); continue; }
                    current = e.Current;
                }
                catch (Exception ex)
                {
                    BootState.HadErrors = true;
                    Debug.LogError("Sealebrities boot: " + what + " threw – skipping the rest of it.");
                    Debug.LogException(ex);
                    yield break;
                }
                if (current is IEnumerator nested && !(current is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        IEnumerator PrewarmCreatures(float from, float to)
        {
            var all = SpeciesDatabase.All;
            int n = all.Count;
            float frameStart = Time.realtimeSinceStartup;
            for (int i = 0; i < n; i++)
            {
                var s = all[i];
                try { CreatureIcons.Get(s); }
                catch (Exception e)
                {
                    Debug.LogWarning("Sealebrities boot: creature prewarm failed for " + s.id + ": " + e.Message);
                }
                // keep the loading screen responsive: yield whenever this frame used more than ~40 ms
                if (Time.realtimeSinceStartup - frameStart > 0.04f || i == n - 1)
                {
                    Progress(Mathf.Lerp(from, to, (i + 1) / (float)n), "Meeresbewohner werden gezählt …", "Counting the sea creatures …");
                    yield return null;
                    frameStart = Time.realtimeSinceStartup;
                }
            }
        }

        // ------------------------------------------------------------------ loading camera
        /// Clears the screen while nothing else renders (the player creates the real Main Camera later).
        void CreateBootCamera()
        {
            if (Camera.main != null) return;
            bootCamera = new GameObject("BootCamera");
            bootCamera.transform.SetParent(transform, false);
            var cam = bootCamera.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.05f, 0.09f, 1f);
            cam.cullingMask = 0;
            cam.depth = -100f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 10f;
        }

        void DestroyBootCamera()
        {
            if (bootCamera == null) return;
            Destroy(bootCamera);
            bootCamera = null;
        }
    }

    /// Fallback values when a module failed during boot, so later modules still get sane positions.
    static class BootFallbacks
    {
        public static void FillMissingWorldInfo()
        {
            if (WorldInfo.PlayerSpawn == Vector3.zero)
            {
                var p = WorldLayout.PlayerSpawn;
                WorldInfo.PlayerSpawn = new Vector3(p.x, Mathf.Max(WorldInfo.GroundHeight(p), Waves.SeaLevel + 0.5f) + 1f, p.z);
                WorldInfo.PlayerSpawnYaw = WorldLayout.PlayerSpawnYaw;
            }
            if (WorldInfo.BedPosition == Vector3.zero) WorldInfo.BedPosition = WorldLayout.Bed;
            if (WorldInfo.DockEndPosition == Vector3.zero) WorldInfo.DockEndPosition = WorldLayout.DockEnd + Vector3.up * 1.3f;
            if (WorldInfo.BoatSpawn == Vector3.zero)
            {
                WorldInfo.BoatSpawn = WorldLayout.BoatSpawn;
                WorldInfo.BoatSpawnYaw = WorldLayout.BoatSpawnYaw;
            }
            if (WorldInfo.HelipadPosition == Vector3.zero) WorldInfo.HelipadPosition = WorldLayout.Helipad;
            if (WorldInfo.FishingSpot == Vector3.zero) WorldInfo.FishingSpot = WorldLayout.FishingSpot + Vector3.up * 1.3f;
            if (WorldInfo.LighthousePosition == Vector3.zero) WorldInfo.LighthousePosition = WorldLayout.Lighthouse;
            if (WorldInfo.MenuCameraPosition == Vector3.zero)
            {
                // L.5: the TV3 fallback (300, 55, −230) → (60, 10, 20) scaled to the 0.62 island (heights × 0.62 as well)
                WorldInfo.MenuCameraPosition = new Vector3(186f, 34f, -143f);
                WorldInfo.MenuCameraLookAt = new Vector3(37f, 6f, 12f);
            }
        }
    }
}
