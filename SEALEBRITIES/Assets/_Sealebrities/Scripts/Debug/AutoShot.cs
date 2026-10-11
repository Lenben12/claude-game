using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// Automated screenshot tour for build verification. Does nothing unless the game is started with
    ///     SEALEBRITIES.exe -autoshot "C:/some/output/folder"
    /// It disables saving, waits for the boot (the launch intro skips itself for -autoshot), starts an Open Water game
    /// with good test gear and captures 01_menu.png … 12_night.png (menu crew on the dock, island, hut/dock/boat,
    /// Sealog on the raised 3D phone, reef with the camera in hand, kelp, wreck, drop-off, trench with camera + lamp in
    /// both hands, night), writes autoshot_log.txt and quits. Scenery shots are taken with empty hands.
    /// Every shot is guarded: a failing shot is logged and the tour continues.
    public sealed partial class AutoShot : MonoBehaviour
    {
        const float BootTimeout = 900f;

        string outDir;
        readonly StringBuilder log = new StringBuilder();
        readonly System.Random rng = new System.Random(4711);
        PlayerController pc;
        PlayerFlashlight lamp;
        Camera cam;
        int saved;

        // ------------------------------------------------------------------ activation (command line only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Activate()
        {
            string dir = ParseOutputDir(Environment.GetCommandLineArgs());
            if (dir == null) return;
            GameManager.SavingDisabled = true;   // never touch the player's save
            GameManager.AutomatedRun = true;
            var go = new GameObject("SB_AutoShot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoShot>().outDir = dir;
        }

        static string ParseOutputDir(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-autoshot", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-"))
                    return args[i + 1].Trim().Trim('"');
                Debug.LogError("AutoShot: -autoshot needs an output folder, e.g. -autoshot C:/shots");
                return null;
            }
            return null;
        }

        void Start()
        {
            DiverLod.ForceLod0 = true;   // screenshots always show the full character models (L.13 LODs)
            StartCoroutine(Run());
        }

        // ------------------------------------------------------------------ the tour
        IEnumerator Run()
        {
            try { Directory.CreateDirectory(outDir); }
            catch (Exception e)
            {
                Debug.LogError("AutoShot: cannot create output folder " + outDir + ": " + e.Message);
                Application.Quit(2);
                yield break;
            }
            Note("AutoShot started, output " + outDir);

            float waited = 0f;
            while (!BootState.Ready)
            {
                waited += Time.unscaledDeltaTime;
                if (waited > BootTimeout)
                {
                    Note("ERROR: boot did not finish within " + BootTimeout + " s");
                    Finish(3);
                    yield break;
                }
                yield return null;
            }
            Note("boot ready after " + BootState.BootSeconds.ToString("0.0") + " s" + (BootState.HadErrors ? " (boot had errors)" : ""));

            // 01 – main menu: the bean-diver crew on the dock at golden hour (the launch intro skips itself for -autoshot)
            if (PlayerIntro.Playing) PlayerIntro.Skip();
            yield return Wait(3f);
            if (GameObject.Find("MenuCrew") == null) Note("WARNING: no menu crew on the dock (PlayerMenuCamera)");
            yield return Capture("01_menu");

            // start a game with test gear and a partly filled journal
            yield return Guard(StartGame(), "start game");

            yield return Guard(ShotIslandAir(), "02_island_air");
            yield return Guard(ShotHutDock(), "03_hut_dock_boat");
            yield return Guard(ShotPhone(), "04/05 phone");
            yield return Guard(ShotReef(), "06/07 reef");
            yield return Guard(ShotKelp(), "08_kelp");
            yield return Guard(ShotWreck(), "09_wreck");
            yield return Guard(ShotBlueDrop(), "10_blue_drop");
            yield return Guard(ShotTrench(), "11_trench_night");
            yield return Guard(ShotNightIsland(), "12_night");
            // art comparison shots (How to Fish references: dock + island at noon, open sea, a sunny morning, the jungle)
            yield return Guard(ShotDockSea(), "13_dock_island / 14_sea_horizon");
            yield return Guard(ShotMorning(), "15_morning_beach");
            yield return Guard(ShotJungle(), "16_jungle");
            yield return Guard(ShotHutMorning(), "17_hut_morning");
            // checks of the Testversion 3 feedback fixes: held items vs. the dock (overlay camera), "Zur Hütte" brings
            // the boat home, slider / percent labels never show NaN
            yield return Guard(ShotLampDockLow(), "18_lamp_dock_low");
            yield return Guard(CheckBoatHome(), "19_boat_home");
            CheckSliderGuards();
            // round 2 (Design_Runde2 J.6 + the third player message): r2_*.png
            yield return Guard(ShotsRound2(), "round-2 shots");

            Finish(saved >= 12 ? 0 : 1);
        }

        // 18 – crouched at the dock with camera + lamp in hand, eye only ~0.35 m above the planks: the held items sit
        // inside the planks. Before the view-model overlay camera they were cut by the dock; now they must be whole.
        IEnumerator ShotLampDockLow()
        {
            SetTime(0.82f);
            yield return EnsureOnLand();
            Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Hands.StowAll();
            yield return Stand(dock + new Vector3(-6f, 0.05f, 0f), 262f);
            Hold(HoldItem.Camera);
            if (lamp != null) lamp.SetOn(true);
            else Note("WARNING: no flashlight component found");
            yield return Wait(1f);
            if (pc == null || cam == null) yield break;
            pc.enabled = false;
            Vector3 feet = pc.transform.position;
            Vector3 fwd = Flat(cam.transform.forward);
            Vector3 eye = feet + Vector3.up * 0.35f;
            Pose(eye, eye + fwd * 4f + Vector3.down * 0.6f);
            yield return Wait(1.5f);

            // layer checks: the overlay exists, the main camera skips the view-model layer, every held mesh is on it
            var overlay = PlayerViewModelCamera.Overlay;
            bool mainSkips = (cam.cullingMask & PlayerViewModelCamera.ViewModelMask) == 0;
            int meshes = 0, wrong = 0;
            var rs = cam.GetComponentsInChildren<MeshRenderer>(false);
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled) continue;
                meshes++;
                if (rs[i].gameObject.layer != PlayerViewModelCamera.ViewModelLayer) wrong++;
            }
            bool ok = overlay != null && overlay.isActiveAndEnabled && mainSkips && meshes > 0 && wrong == 0;
            Note((ok ? "OK" : "FAIL") + ": view-model overlay " + (overlay != null ? "present" : "MISSING")
                 + ", main camera skips layer " + PlayerViewModelCamera.ViewModelLayer + ": " + mainSkips
                 + ", held meshes " + meshes + " (" + wrong + " not on the view-model layer)");
            yield return Capture("18_lamp_dock_low");
            yield return MeasureOverlayAB("dock_low_held");
            if (lamp != null) lamp.SetOn(false);
            Hands.StowAll();
            pc.enabled = true;
        }

        // 19 – the boat drifts at the wreck, the player uses "Zur Hütte" (same calls as UIPauseMenu.OnReturnToHut):
        // the boat must lie at its mooring at the dock afterwards
        IEnumerator CheckBoatHome()
        {
            SetTime(0.5f);
            var bc = Refs.Boat as BoatController;
            if (bc == null) { Note("FAIL: boat-home check – no boat"); yield break; }
            yield return EnsureOnLand();
            var wreck = DiveSites.Get("wreck");
            Vector3 away = wreck != null ? wreck.center : bc.MooringPosition + new Vector3(120f, 0f, -60f);
            bc.PlaceAt(new Vector3(away.x, 0f, away.z), 30f);
            yield return Wait(1f);
            bool wasAway = !bc.AtMooring;

            Vector3 spawn = PlayerController.ResolveSpawn(out float yaw);
            pc.enabled = true;
            pc.TeleportTo(spawn, yaw);
            BoatHome.AfterLocalPlayerWentHome();
            yield return Wait(2f);
            Vector3 d = bc.transform.position - bc.MooringPosition;
            d.y = 0f;
            bool home = bc.AtMooring;
            Note((wasAway && home ? "OK" : "FAIL") + ": \"Zur Hütte\" boat home – away before: " + wasAway
                 + ", at mooring after: " + home + " (" + d.magnitude.ToString("0.0") + " m from the mooring)");

            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Vector3 eye = Vector3.Lerp(end, bc.transform.position, 0.5f) + new Vector3(-14f, 7f, -10f);
            FreeCamera(eye, bc.transform.position + Vector3.up * 1f);
            yield return Wait(1.5f);
            yield return Capture("19_boat_home");
        }

        /// Cost of the view-model overlay camera under the SAME conditions (other programs on this PC included): the
        /// overlay is switched off (old single-camera rendering) and on again in alternating 2 s windows, 3 rounds.
        IEnumerator MeasureOverlayAB(string label)
        {
            var ov = PlayerViewModelCamera.Overlay;
            var vm = ov != null ? ov.GetComponent<PlayerViewModelCamera>() : null;
            if (vm == null) { Note("fps A/B " + label + ": no overlay camera"); yield break; }
            int vsync = QualitySettings.vSyncCount, cap = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;          // uncapped for the measurement (restored below)
            Application.targetFrameRate = -1;
            float tOn = 0f, tOff = 0f;
            int fOn = 0, fOff = 0;
            for (int round = 0; round < 3; round++)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    bool on = pass == 0;
                    vm.enabled = on;
                    yield return Wait(0.5f);   // settle
                    float t = 0f;
                    int frames = 0;
                    while (t < 2f)
                    {
                        yield return null;
                        t += Time.unscaledDeltaTime;
                        frames++;
                    }
                    if (on) { tOn += t; fOn += frames; }
                    else { tOff += t; fOff += frames; }
                }
            }
            vm.enabled = true;
            QualitySettings.vSyncCount = vsync;
            Application.targetFrameRate = cap;
            float aOn = fOn / Mathf.Max(tOn, 0.001f), aOff = fOff / Mathf.Max(tOff, 0.001f);
            Note("fps A/B " + label + ": overlay on avg " + aOn.ToString("0.0") + ", overlay off avg " + aOff.ToString("0.0")
                 + " (" + ((aOn - aOff) / Mathf.Max(aOff, 0.001f) * 100f).ToString("+0;-0") + " %)");
            yield return Frames(2);
        }

        /// Slider / label guards (misclick → "-2147483648 %"): NaN and ∞ never reach the setting or the label.
        void CheckSliderGuards()
        {
            try
            {
                bool bad = false;
                var s = new UISlider(0f, 1f, 0.5f, v => { if (float.IsNaN(v) || float.IsInfinity(v)) bad = true; }, Color.white);
                s.SetValue(float.NaN, true);
                s.SetValue(float.PositiveInfinity, true);
                s.SetValue(float.NegativeInfinity, true);
                bool sliderOk = !bad && !float.IsNaN(s.Value) && s.Value >= 0f && s.Value <= 1f;
                bool labelOk = UIKit.Percent01(float.NaN) == "0 %" && UIKit.Percent01(7f) == "100 %" && UIKit.Percent01(0.456f) == "46 %";
                Note((sliderOk && labelOk ? "OK" : "FAIL") + ": slider guards – slider value " + s.Value.ToString("0.###")
                     + " after NaN/±inf, labels " + UIKit.Percent01(float.NaN) + " / " + UIKit.Percent01(7f));
            }
            catch (Exception e)
            {
                Note("FAIL: slider guard check threw " + e.Message);
            }
        }

        // 17 – waking up in the hut at 06:30 (the interior read purple in the playtest)
        IEnumerator ShotHutMorning()
        {
            SetTime(0.271f);
            yield return EnsureOnLand();
            Vector3 p = PlayerController.ResolveSpawn(out float yaw);
            Vector3 eye = p + Vector3.up * 1.6f;
            FreeCamera(eye, eye + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 5f);
            yield return Wait(6.5f);
            yield return Capture("17_hut_morning");
        }

        // 13/14 – from the dock end: back toward the island (How to Fish "2.png") and out to the open sea at noon
        IEnumerator ShotDockSea()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Vector3 eye = end + Vector3.up * 0.4f;
            FreeCamera(eye, new Vector3(WorldLayout.Hut.x, eye.y - 2.5f, WorldLayout.Hut.z - 4f));
            yield return Wait(6.5f);
            yield return MeasureFps("dock_island");
            yield return Capture("13_dock_island");
            yield return MeasureOverlayAB("dock_island_empty_hands");
            Pose(eye, eye + new Vector3(1f, -0.12f, -0.35f) * 10f);
            yield return Wait(2f);
            yield return Capture("14_sea_horizon");
        }

        // 15 – a sunny morning on the beach (07:00): blue sky with a warm glow, no lavender cast
        IEnumerator ShotMorning()
        {
            SetTime(0.292f);
            yield return EnsureOnLand();
            Vector3 beach = FindBeach(WorldLayout.Isl3(150f, 0f, 60f));
            Vector3 eye = new Vector3(beach.x, Mathf.Max(beach.y, Waves.SeaLevel) + 1.7f, beach.z);
            FreeCamera(eye, new Vector3(WorldLayout.DockEnd.x, 1.5f, WorldLayout.DockEnd.z));
            yield return Wait(6.5f);
            yield return Capture("15_morning_beach");
        }

        // 16 – jungle path toward the lighthouse hill at noon (How to Fish "3.png": layered grass, bushes, canopy)
        IEnumerator ShotJungle()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 g = Ground(WorldLayout.Isl3(55f, 0f, 30f));
            Vector3 eye = g + Vector3.up * 1.7f;
            FreeCamera(eye, Ground(WorldLayout.Isl3(0f, 0f, 75f)) + Vector3.up * 3f);
            yield return Wait(6.5f);
            yield return MeasureFps("jungle");
            yield return Capture("16_jungle");
        }

        /// Average frame rate over 3 s (unscaled) written to the AutoShot log (performance regression check).
        IEnumerator MeasureFps(string label)
        {
            float t = 0f, worst = 0f;
            int frames = 0;
            while (t < 3f)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt;
                frames++;
                if (dt > worst) worst = dt;
            }
            Note("fps " + label + ": avg " + (frames / Mathf.Max(t, 0.001f)).ToString("0.0") + ", worst frame " + (worst * 1000f).ToString("0") + " ms");
        }

        IEnumerator StartGame()
        {
            var gm = GameManager.I;
            if (gm == null) throw new InvalidOperationException("no GameManager");
            gm.NewGame(Difficulty.OpenWater);
            yield return Frames(3);

            var s = gm.State;
            // past the tutorial, so no quest push cards cover the shots
            gm.SetQuestIndex(TutorialQuests.Finished);
            foreach (var id in new[] { "tank_t5", "lamp_t3", "cam_t2", "fins_t1", "boat_t1", "rod_t1" })
                if (GearDatabase.Get(id) != null && !s.ownedItems.Contains(id)) s.ownedItems.Add(id);
            GameEvents.RaiseGearChanged();

            // a little progress so the HUD and the Sealog have something to show (no milestone banners)
            s.money = 1840;
            s.followers = 612;
            s.reachedMilestones.Add("m100");
            if (!s.unlockedSites.Contains("kelp_forest")) s.unlockedSites.Add("kelp_forest");
            GameEvents.RaiseMoneyChanged(s.money);
            GameEvents.RaiseFollowersChanged(s.followers);
            FillJournal();

            pc = Refs.Player as PlayerController;
            if (pc == null) throw new InvalidOperationException("no PlayerController registered");
            cam = pc.View;
            lamp = cam != null ? cam.GetComponentInChildren<PlayerFlashlight>(true) : null;
            pc.RefillAir();
            yield return Wait(1.5f);
        }

        void FillJournal()
        {
            var tiers = new[] { SizeTier.Legendary, SizeTier.Huge, SizeTier.Large, SizeTier.Normal, SizeTier.Large, SizeTier.Tiny, SizeTier.Huge, SizeTier.Normal };
            int k = 0;
            foreach (var id in new[] { "clownfish", "blue_tang", "moon_jelly", "parrotfish", "lionfish", "octopus", "green_turtle", "garibaldi" })
            {
                var sp = SpeciesDatabase.Get(id);
                if (sp == null) continue;
                var e = Journal.Get(id);
                var t = tiers[k++ % tiers.Length];
                e.discovered = true;
                e.firstDay = 1;
                e.recordCm = Progression.RollSizeCm(sp, t, rng);
                e.recordTier = (int)Progression.ClassifySize(sp, e.recordCm);
                e.timesFilmed = 1 + k;
                e.bestClipValue = sp.baseValue * Progression.SizeMultiplier((SizeTier)e.recordTier);
            }
        }

        // 02 – island overview from the air at golden hour
        IEnumerator ShotIslandAir()
        {
            SetTime(0.72f);
            yield return EnsureOnLand();
            FreeCamera(WorldLayout.Isl3(285f, 125f, -255f), WorldLayout.Isl3(10f, 2f, 25f));
            yield return Wait(6.5f);   // sky ambient refreshes every 6 s
            yield return Capture("02_island_air");
        }

        // 03 – hut, dock and boat at noon, seen from the beach
        IEnumerator ShotHutDock()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 beach = FindBeach(WorldLayout.Isl3(160f, 0f, -55f));
            Vector3 eye = new Vector3(beach.x, Mathf.Max(beach.y, Waves.SeaLevel) + 1.7f, beach.z);
            FreeCamera(eye, WorldLayout.Isl3(165f, 2.5f, 12f));
            yield return Wait(6.5f);
            yield return Capture("03_hut_dock_boat");
        }

        // 04/05 – the phone (old 3D phone raised in the left hand, UI rendered onto its screen): Sealog index and a
        // species page with its biggest specimen
        IEnumerator ShotPhone()
        {
            Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Hands.StowAll();
            yield return Stand(dock + new Vector3(-6f, 0.05f, 0f), 262f);
            if (!UIModule.DebugOpenPhone("sealog")) Note("WARNING: phone did not open");
            float t = 0f;
            while (!PhoneDisplay.Raised && t < 2f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            Note(PhoneDisplay.Raised ? "phone raised in 3D" : "WARNING: 3D phone not raised – the flat 2D phone is shown");
            yield return Wait(1.2f);
            yield return Capture("04_sealog");
            UIModule.DebugOpenPhone("sealog:green_turtle");
            yield return Wait(1.2f);
            yield return Capture("05_sealog_record");
            UIModule.DebugOpenPhone("close");
            yield return Frames(3);
        }

        // 06/07 – house reef (clownfish, green turtle, octopus, blue tang, lionfish)
        IEnumerator ShotReef()
        {
            SetTime(0.5f);
            var site = DiveSites.Get("house_reef");
            Vector3 anchor;
            if (PropInfo.AnemoneSpots.Count > 0) anchor = PropInfo.AnemoneSpots[0];
            else anchor = FindSeafloor(site.center, site.radius * 0.8f, -10.5f, -5.5f);

            Vector3 dir = Flat(new Vector3(-1f, 0f, -0.4f));
            Vector3 eye = anchor - dir * 5.5f + Vector3.up * 2.2f;
            eye = KeepInWater(eye, 1.4f, 1.6f);
            Vector3 look = anchor + Vector3.up * 0.6f;
            yield return Dive(eye, look);
            ClearCrowd();
            Hold(HoldItem.Camera);   // hotbar: the camera in the right hand (small How-to-Fish style model)

            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("clownfish", anchor + Vector3.up * 0.45f + r * 0.3f, SizeTier.Large);
            Spawn("clownfish", anchor + Vector3.up * 0.55f - r * 0.4f, SizeTier.Normal);
            Spawn("octopus", anchor - r * 1.6f + f * 0.5f + Vector3.up * 0.2f, SizeTier.Large);
            Spawn("lionfish", anchor + r * 1.9f + Vector3.up * 0.9f, SizeTier.Huge);
            Spawn("blue_tang", eye + f * 7f + r * 2.5f + Vector3.up * 0.8f, SizeTier.Large);
            Spawn("green_turtle", eye + f * 9f - r * 3f + Vector3.up * 1.2f, SizeTier.Huge);
            yield return Wait(1.2f);
            yield return Capture("06_reef");

            // second angle: wider, looking along the reef toward the turtle
            Vector3 eye2 = KeepInWater(eye - f * 3f + r * 4f + Vector3.up * 0.8f, 1.2f, 1.6f);
            Pose(eye2, eye + f * 8f - r * 2f);
            Spawn("green_turtle", eye2 + (eye + f * 8f - eye2).normalized * 6f, SizeTier.Legendary);
            yield return Wait(1.2f);
            yield return Capture("07_reef_wide");
        }

        // 08 – kelp forest with sea lion and leopard shark
        IEnumerator ShotKelp()
        {
            SetTime(0.45f);
            var site = DiveSites.Get("kelp_forest");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -20f, -12f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 4f, 2.5f, 2f);
            Vector3 look = eye + Flat(new Vector3(0.6f, 0f, -1f)) * 10f + Vector3.down * 1.5f;
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("sea_lion", eye + f * 5f + r * 1.2f + Vector3.up * 0.5f, SizeTier.Large);
            Spawn("leopard_shark", eye + f * 8f - r * 2f + Vector3.down * 2.5f, SizeTier.Huge);
            Spawn("garibaldi", eye + f * 4f - r * 1.5f + Vector3.down * 1f, SizeTier.Large);
            yield return Wait(1.2f);
            yield return Capture("08_kelp");
        }

        // 09 – wreck with moray, hammerhead and barracuda
        IEnumerator ShotWreck()
        {
            SetTime(0.55f);
            var site = DiveSites.Get("wreck");
            // first swim above the wreck so its decor and colliders are active (sites activate near the camera)
            Vector3 c0 = PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : Ground(site.center);
            yield return Dive(KeepInWater(c0 + Vector3.up * 16f, 3f, 2f), c0);

            Vector3 eye = Vector3.zero, look = Vector3.zero, moray = Vector3.zero;
            bool framed = false;

            // preferred: a hull breach in the side of the ship (moray home), seen from open water
            int best = -1;
            for (int i = 0; i < PropInfo.WreckHoles.Count && i < PropInfo.WreckHoleNormals.Count; i++)
            {
                if (Mathf.Abs(PropInfo.WreckHoleNormals[i].y) > 0.6f) continue;           // skip hatches/deck openings
                if (best < 0 || PropInfo.WreckHoles[i].y > PropInfo.WreckHoles[best].y) best = i;   // highest: best light
            }
            if (best >= 0)
            {
                Vector3 hole = PropInfo.WreckHoles[best];
                Vector3 n = Flat(PropInfo.WreckHoleNormals[best]);
                for (float dist = 8f; dist <= 20f && !framed; dist += 3f)
                {
                    Vector3 e = KeepInWater(hole + n * dist + Vector3.up * 2.5f, 3f, 2f);
                    if (IsClear(e)) { eye = e; framed = true; }
                }
                moray = hole + n * 0.6f;
                look = hole + Vector3.up * 1.2f;
            }
            if (!framed)
            {
                // fallback: circle the wreck center for an open spot that looks straight at the hull
                Vector3 c = c0;
                look = c + Vector3.up * 3f;
                for (int k = 0; k < 16 && !framed; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 e = KeepInWater(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 24f + Vector3.up * 6f, 3f, 2f);
                    if (!IsClear(e)) continue;
                    if (Physics.Raycast(e, (look - e).normalized, out RaycastHit hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && !IsTerrain(hit.collider))
                    {
                        eye = e;
                        moray = hit.point + hit.normal * 0.6f;
                        framed = true;
                    }
                }
                if (!framed)
                {
                    eye = KeepInWater(c + new Vector3(-20f, 7f, -12f), 3f, 2f);
                    moray = c + new Vector3(-6f, 1.5f, -4f);
                }
            }
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("moray", moray, SizeTier.Huge);
            Spawn("barracuda", eye + f * 5f - r * 2f + Vector3.up * 0.6f, SizeTier.Large);
            Spawn("hammerhead", eye + f * 10f + r * 3f + Vector3.up * 1.5f, SizeTier.Large);
            Spawn("grouper", eye + f * 6f + r * 1.5f - Vector3.up * 1f, SizeTier.Huge);
            yield return Wait(1.3f);
            yield return Capture("09_wreck");
        }

        // 10 – blue water drop-off with manta and whale shark
        IEnumerator ShotBlueDrop()
        {
            SetTime(0.5f);
            var site = DiveSites.Get("blue_drop");
            Vector3 c = Ground(site.center);
            Vector3 eye = new Vector3(c.x, -20f, c.z);
            eye = KeepInWater(eye, 4f, 2f);
            Vector3 look = eye + Flat(new Vector3(-1f, 0f, -0.3f)) * 12f + Vector3.down * 2f;
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("manta", eye + f * 7f + r * 2f + Vector3.down * 1f, SizeTier.Huge);
            Spawn("whale_shark", eye + f * 20f - r * 5f + Vector3.down * 4f, SizeTier.Large);
            yield return Wait(1.3f);
            yield return Capture("10_blue_drop");
        }

        // 11 – deep trench at night with the flashlight on. Taken on the canyon rim (~85-95 m) so the best test tank
        // (Trimix, 100 m) is not "too deep" – the canyon floor at ~150 m needs the submarine.
        IEnumerator ShotTrench()
        {
            SetTime(0.95f);
            var site = DiveSites.Get("deep_trench");
            Vector3 rim = FindSeafloor(site.center, site.radius, -96f, -82f);
            Vector3 eye = KeepInWater(rim + Vector3.up * 3.5f, 2.5f, 3f);
            Vector3 toCenter = Flat(site.center - rim);
            Vector3 target = eye + toCenter * 8f + Vector3.down * 2.5f;
            yield return Dive(eye, target);
            Hold(HoldItem.Camera);
            if (lamp != null) lamp.SetOn(true);   // also takes the lamp into the left hand (hotbar)
            else Note("WARNING: no flashlight component found");
            ClearCrowd();
            Vector3 f = (target - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("anglerfish", eye + f * 3.2f + r * 0.6f, SizeTier.Huge);
            Spawn("vampire_squid", eye + f * 5f - r * 1.6f + Vector3.up * 0.8f, SizeTier.Large);
            yield return Wait(6.5f);
            yield return Capture("11_trench_night");
            if (lamp != null) lamp.SetOn(false);
            Hands.StowAll();
        }

        // 12 – the island at night (hut lamp, dock lantern, torches, campfire, lighthouse beams)
        IEnumerator ShotNightIsland()
        {
            SetTime(0.95f);
            yield return EnsureOnLand();
            FreeCamera(WorldLayout.Isl3(240f, 16f, -45f), WorldLayout.Isl3(105f, 6f, 35f));
            yield return Wait(3f);
            yield return Capture("12_night");
        }

        // ------------------------------------------------------------------ player / camera helpers
        void SetTime(float t)
        {
            var gm = GameManager.I;
            if (gm != null) gm.State.timeOfDay = t;
        }

        /// Puts the (enabled) player back on land at the hut so the water state is "dry" before a free-camera shot.
        IEnumerator EnsureOnLand()
        {
            if (pc == null) yield break;
            if (pc.enabled && !pc.IsInWater && !pc.IsUnderwater) yield break;
            pc.enabled = true;
            Vector3 p = PlayerController.ResolveSpawn(out float yaw);
            pc.TeleportTo(p, yaw);
            yield return Frames(10);
        }

        /// The game window has keyboard focus during the tour: a stray Tab/P typed elsewhere opens the phone (and hides
        /// the camera model). Every non-phone shot starts with the phone closed.
        static void CloseStrayPhone()
        {
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
        }

        /// Free camera (player frozen where he stands): only for above-water views. Empty hands, so no view model
        /// floats in front of the scenery.
        void FreeCamera(Vector3 eye, Vector3 lookAt)
        {
            CloseStrayPhone();
            Hands.StowAll();
            if (pc == null || cam == null) return;
            pc.enabled = false;
            Pose(eye, lookAt);
        }

        /// Takes a hotbar item into its hand (no-op when it is already held or not on the hotbar).
        void Hold(HoldItem item)
        {
            if (Hands.IsHeld(item)) return;
            if (Hands.Find(item, out Hand h, out int slot)) Hands.Select(h, slot);
            if (!Hands.IsHeld(item)) Note("WARNING: could not take " + item + " into a hand");
        }

        void Pose(Vector3 eye, Vector3 lookAt)
        {
            if (cam == null) return;
            Vector3 d = lookAt - eye;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d.normalized, Vector3.up));
        }

        /// Player standing (controller active) at a position, looking along the yaw.
        IEnumerator Stand(Vector3 feet, float yaw)
        {
            if (pc == null) yield break;
            pc.enabled = true;
            pc.TeleportTo(feet, yaw);
            pc.RefillAir();
            yield return Wait(0.8f);
        }

        /// Teleports the diver so the eye is at the given point, lets the controller settle into dive mode
        /// (underwater state, depth), then freezes it and aims the camera.
        IEnumerator Dive(Vector3 eye, Vector3 lookAt)
        {
            CloseStrayPhone();
            if (pc == null) yield break;
            if (lamp != null) lamp.SetOn(false);
            pc.enabled = true;
            Vector3 d = lookAt - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pc.TeleportTo(eye - Vector3.up * PlayerController.EyeHeight, yaw);
            pc.RefillAir();
            yield return Wait(0.6f);
            pc.RefillAir();
            pc.enabled = false;
            Pose(eye, lookAt);
            yield return Wait(1.2f);   // site decor activation, fog blend, fauna near the camera
        }

        void ClearCrowd()
        {
            var sp = FaunaModule.Spawner;
            if (sp != null && sp.ActiveCount > 110) sp.DespawnAll();
        }

        void Spawn(string id, Vector3 pos, SizeTier tier)
        {
            var spawner = Refs.Spawner;
            var s = SpeciesDatabase.Get(id);
            if (spawner == null || s == null) { Note("WARNING: cannot spawn " + id); return; }
            try
            {
                float cm = Progression.RollSizeCm(s, tier, rng);
                var c = spawner.Spawn(s, pos, cm, false);
                if (c == null) Note("WARNING: spawner refused " + id);
            }
            catch (Exception e)
            {
                Note("WARNING: spawning " + id + " threw " + e.Message);
            }
        }

        // ------------------------------------------------------------------ terrain helpers
        static Vector3 Ground(Vector3 p) => new Vector3(p.x, WorldInfo.GroundHeight(p), p.z);

        /// True for the ground: the low-poly mesh terrain chunks ("Chunk_x_z" under the "Terrain" root, see
        /// WorldTerrain.CreateChunk) or a legacy Unity TerrainCollider.
        static bool IsTerrain(Collider c)
        {
            if (c == null) return false;
            if (c is TerrainCollider) return true;
            if (!(c is MeshCollider)) return false;
            var t = c.transform;
            return t.name.StartsWith("Chunk_", StringComparison.Ordinal) && t.parent != null && t.parent.name == "Terrain";
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// Spiral search around a center for a seafloor point whose height lies in [minY, maxY].
        static Vector3 FindSeafloor(Vector3 center, float radius, float minY, float maxY)
        {
            for (float r = 0f; r <= radius; r += 6f)
            {
                int steps = r < 1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r / 4f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps + r * 0.13f;
                    var p = new Vector3(center.x + Mathf.Cos(a) * r, 0f, center.z + Mathf.Sin(a) * r);
                    float g = WorldInfo.GroundHeight(p);
                    if (g >= minY && g <= maxY) return new Vector3(p.x, g, p.z);
                }
            }
            return Ground(center);
        }

        /// Walks from a start point toward (or away from) the island center until it stands on dry beach.
        static Vector3 FindBeach(Vector3 start)
        {
            Vector3 p = start;
            Vector3 toCenter = Flat(-start);
            for (int i = 0; i < 40; i++)
            {
                float g = WorldInfo.GroundHeight(p);
                if (g >= 0.3f && g <= 3f) return new Vector3(p.x, g, p.z);
                p += (g < 0.3f ? toCenter : -toCenter) * 3f;
            }
            return Ground(start);
        }

        /// True when no collider (hull, rock, terrain) is within 1.5 m of the point.
        static bool IsClear(Vector3 p) =>
            !Physics.CheckSphere(p, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && p.y > WorldInfo.GroundHeight(p) + 1.5f;

        /// Keeps an underwater eye point at least `aboveFloor` above the seafloor and `belowSurface` under the surface.
        static Vector3 KeepInWater(Vector3 p, float aboveFloor, float belowSurface)
        {
            float g = WorldInfo.GroundHeight(p);
            float top = Waves.SeaLevel - belowSurface;
            p.y = Mathf.Max(p.y, g + aboveFloor);
            if (p.y > top) p.y = top;
            return p;
        }

        // ------------------------------------------------------------------ capture / timing
        IEnumerator Capture(string name)
        {
            yield return Frames(2);
            string path = Path.Combine(outDir, name + ".png");
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                saved++;
                Note("captured " + name + ".png");
            }
            catch (Exception e)
            {
                Note("ERROR: capture " + name + " failed: " + e.Message);
            }
            yield return Frames(3);   // the file is written at the end of the capture frame
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// Runs a shot coroutine; an exception is logged and the tour moves on.
        IEnumerator Guard(IEnumerator inner, string what)
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
                    Note("ERROR in " + what + ": " + ex);
                    yield break;
                }
                if (current is IEnumerator nested && !(current is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        void Note(string line)
        {
            Debug.Log("AutoShot: " + line);
            log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
        }

        void Finish(int code)
        {
            Note("done, " + saved + " screenshots");
            try { File.WriteAllText(Path.Combine(outDir, "autoshot_log.txt"), log.ToString()); }
            catch (Exception e) { Debug.LogWarning("AutoShot: could not write log: " + e.Message); }
            if (pc != null) pc.enabled = true;
            Application.Quit(code);
        }
    }
}
