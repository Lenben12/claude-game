using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Automated single-player playtest of the whole game loop. Does nothing unless the game is started with
    ///     SEALEBRITIES.exe -autoplaytest "C:/some/output/folder"
    /// It drives the REAL systems through their public APIs and the simulated input of GameInput (hotbar keys, Tab,
    /// I, E, Space, LMB, RMB, P – the same code paths a player uses): menu → new game (Normal) → phone → hotbar →
    /// fishing → fish market → shop + same-day helicopter → inventory → boat helm → dock ladder → house reef dive
    /// (entry brightness, visibility, air) → camera scan rules, filming, photo → boat (banking, air refill) → FinTube
    /// upload → sleep → collectible → settings → FPS → save/load → menu, then a late-game smoke test (every dive site,
    /// mini islands, all gear, milestones, the 1,000,000 finale). Every step is timed, asserted and screenshotted;
    /// the results go to <folder>/playtest_report.md and <folder>/playtest.json (written after every step).
    /// Never touches the player's data for good: saving is disabled, settings.json and the save are backed up and
    /// restored, and the gallery photos taken by the test are deleted at the end.
    public sealed partial class AutoPlaytest : MonoBehaviour
    {
        const float BootTimeout = 900f;
        /// Real seconds of testing after which the remaining steps are skipped (the caller waits up to 25 min).
        /// The round-3 runs (-r3only = steps 50–89) are much longer than the 22.5-minute slot of the full run.
        static float TimeBudget => (Round3Only ? 85f : (Round3FaunaOnly || Round3NpcOnly || Round3UIOnly) ? 40f : 24f) * 60f;

        // ------------------------------------------------------------------ report data
        [Serializable]
        public class StepResult
        {
            public string name;
            public bool pass = true;
            public bool skipped;
            public float seconds;
            public int errors;
            public string details = "";
            public string screenshots = "";
        }

        [Serializable]
        public class FpsResult
        {
            public string scene;
            public string quality;
            public float avg;
            public float min;
            public float low1;
            public int frames;
        }

        [Serializable]
        public class Report
        {
            public string started;
            public string finished;
            public string device;
            public string resolution;
            public string version;
            public float bootSeconds;
            public bool bootErrors;
            public float totalSeconds;
            public int passed;
            public int failed;
            public int skipped;
            public int loggedErrors;
            public int loggedExceptions;
            public int screenshots;
            public List<StepResult> steps = new List<StepResult>();
            public List<FpsResult> fps = new List<FpsResult>();
            public List<string> metrics = new List<string>();
            public List<string> errors = new List<string>();
        }

        // ------------------------------------------------------------------ state
        string outDir;
        readonly Report report = new Report();
        readonly StringBuilder log = new StringBuilder();
        readonly System.Random rng = new System.Random(4242);
        StepResult cur;
        float startTime;
        int errorCount, exceptionCount;
        float lastLuma = -1f;

        PlayerController pc;
        Camera cam;
        PlayerFlashlight lamp;
        readonly List<ICreature> mySpawns = new List<ICreature>();
        readonly List<string> testPhotos = new List<string>();
        float fishSecondsPerFish, fishAvgValue;   // measured by the fishing step (bot rate)
        int milestoneEvents, siteEvents, sponsorEvents;

        // backups of the player's files
        bool settingsExisted, saveExisted;
        string settingsBackup, saveBackup;

        // last FollowFor result
        bool filmTargeted, filmRegistered, filmLost;
        int filmClips;
        float filmRegisterTime;
        FilmHint filmLastHint;
        string filmHints = "";

        static GameState S => GameManager.I.State;
        static string SettingsPath => Path.Combine(Application.persistentDataPath, "settings.json");
        static string SavePath => Path.Combine(Application.persistentDataPath, "sealebrities_save.json");
        static string SaveSidecar => SavePath + ".autoplaytest_backup";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ activation (command line only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Activate()
        {
            string dir = ParseOutputDir(Environment.GetCommandLineArgs());
            if (dir == null) return;
            GameManager.SavingDisabled = true;   // never touch the player's save
            GameManager.AutomatedRun = true;
            Application.runInBackground = true;   // keep testing when the window loses focus
            var go = new GameObject("SB_AutoPlaytest");
            DontDestroyOnLoad(go);
            var t = go.AddComponent<AutoPlaytest>();
            t.outDir = dir;
            t.BackupFiles();
        }

        static string ParseOutputDir(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-autoplaytest", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-"))
                    return args[i + 1].Trim().Trim('"');
                Debug.LogError("AutoPlaytest: -autoplaytest needs an output folder, e.g. -autoplaytest C:/playtest");
                return null;
            }
            return null;
        }

        void OnEnable()
        {
            Application.logMessageReceived += OnLog;
            Gallery.Added += OnPhotoAdded;
            GameEvents.MilestoneReached += OnMilestone;
            GameEvents.SiteUnlocked += OnSiteUnlocked;
            GameEvents.SponsorUnlocked += OnSponsor;
            SubscribeRound2();
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            Gallery.Added -= OnPhotoAdded;
            GameEvents.MilestoneReached -= OnMilestone;
            GameEvents.SiteUnlocked -= OnSiteUnlocked;
            GameEvents.SponsorUnlocked -= OnSponsor;
            UnsubscribeRound2();
        }

        // full path: the gallery folder belongs to the world being played (save slots), it changes during the run
        void OnPhotoAdded(PhotoEntry e) { if (e != null && !string.IsNullOrEmpty(e.file)) testPhotos.Add(Gallery.PathOf(e) ?? e.file); }
        void OnMilestone(Milestone m) => milestoneEvents++;
        void OnSiteUnlocked(DiveSite s) => siteEvents++;
        void OnSponsor(string id) => sponsorEvents++;

        void OnLog(string condition, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            errorCount++;
            if (type == LogType.Exception) exceptionCount++;
            string first = "";
            if (!string.IsNullOrEmpty(stack))
            {
                int nl = stack.IndexOf('\n');
                first = nl > 0 ? stack.Substring(0, nl) : stack;
            }
            string key = type + ": " + Trim(condition, 300) + (first.Length > 0 ? "  @ " + Trim(first, 200) : "");
            if (report.errors.Count < 80 && !report.errors.Contains(key)) report.errors.Add(key);
            if (cur != null && cur.details.Length < 6000) cur.details += "\n  LOG " + type + ": " + Trim(condition, 240);
        }

        static string Trim(string s, int max) => s == null ? "" : s.Length <= max ? s : s.Substring(0, max) + "…";

        // ------------------------------------------------------------------ the run
        void Start() { StartCoroutine(Run()); }

        IEnumerator Run()
        {
            startTime = Time.realtimeSinceStartup;
            report.started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            try { Directory.CreateDirectory(outDir); }
            catch (Exception e)
            {
                Debug.LogError("AutoPlaytest: cannot create output folder " + outDir + ": " + e.Message);
                Application.Quit(2);
                yield break;
            }
            Note("AutoPlaytest started, output " + outDir);

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
            report.bootSeconds = BootState.BootSeconds;
            report.bootErrors = BootState.HadErrors;
            report.device = SystemInfo.graphicsDeviceName + " | " + SystemInfo.processorType + " | " + SystemInfo.systemMemorySize + " MB RAM";
            report.resolution = Screen.width + "x" + Screen.height;
            report.version = Application.version + " (Unity " + Application.unityVersion + ")";
            Note("boot ready after " + BootState.BootSeconds.ToString("0.0", Inv) + " s" + (BootState.HadErrors ? " (boot had errors)" : ""));

            // ---------------- -r3fauna: a fresh late game + only the round-3 fauna / sea content steps (65–71)
            if (Round3FaunaOnly)
            {
                yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
                yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
                yield return RunRound3FaunaSteps();
                yield return RunStep("28 Quit to the main menu", StepReturnToMenu);
                Finish(report.failed == 0 ? 0 : 1);
                yield break;
            }

            // ---------------- -r3npc: a fresh late game + only the round-3 NPC / story / side-quest steps (72–79)
            if (Round3NpcOnly)
            {
                yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
                yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
                yield return RunRound3NpcSteps();
                yield return RunStep("28 Quit to the main menu", StepReturnToMenu);
                Finish(report.failed == 0 ? 0 : 1);
                yield break;
            }

            // ---------------- -r3ui: a fresh late game + only the round-3 phone / UI / boat steps (80–87)
            if (Round3UIOnly)
            {
                yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
                yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
                yield return RunRound3UISteps();
                yield return RunStep("28 Quit to the main menu", StepReturnToMenu);
                Finish(report.failed == 0 ? 0 : 1);
                yield break;
            }

            // ---------------- -r3slots: a fresh late game + only the save-slot / invite steps (88–89, GDD §19b)
            if (Round3SlotsOnly)
            {
                yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
                yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
                yield return RunStep("18 Save + continue (load)", StepSaveLoad);
                yield return RunRound3SlotSteps();
                yield return RunStep("28 Quit to the main menu", StepReturnToMenu);
                Finish(report.failed == 0 ? 0 : 1);
                yield break;
            }

            // ---------------- -r3only: a fresh late game + only the round-3 steps (Testversion 5: backpack, survival …)
            if (Round3Only)
            {
                yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
                yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
                yield return RunRound3Steps();
                yield return RunStep("28 Quit to the main menu", StepReturnToMenu);
                Finish(report.failed == 0 ? 0 : 1);
                yield break;
            }

            // ---------------- -r2only: a fresh late game + only the round-2 integration steps (fits one test slot)
            if (Round2Only)
            {
                yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
                yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
                yield return RunStep("20b Open-world spawning census (L.1): fish everywhere, depth bands, locked sites", StepR2Census, 150f);
                yield return RunRound2Steps();
                yield return RunStep("28 Quit to the main menu", StepReturnToMenu);
                Finish(report.failed == 0 ? 0 : 1);
                yield break;
            }

            // ---------------- the tutorial loop (Normal)
            yield return RunStep("01 Main menu (crew on the dock, 3 difficulties)", StepMenu);
            yield return RunStep("02 New game (Normal): wake up in the hut", StepNewGame);
            yield return RunStep("03 Phone: Tab raises the 3D phone, Aufgaben app shows the quest", StepPhone);
            yield return RunStep("04 Hotbar: camera / lamp / two-handed rod, only the held models visible", StepHotbar);
            yield return RunStep("05 Fishing: hold-to-cast swing, bite, strike, 5 fish into the cooler box", StepFishing, 420f);
            yield return RunStep("06 Fish market: list all, sold within ~1 minute", StepMarket, 150f);
            yield return RunStep("07 Shop: air tank, same-day helicopter, open the crate (E)", StepShop, 220f);
            yield return RunStep("08 Inventory: equip gear, drag items onto the hotbar", StepInventory);
            yield return RunStep("09 Boat: wheel only from the seat, drive, get up", StepBoat);
            yield return RunStep("10 Dock ladder: out of the water onto the dock", StepLadder);
            yield return RunStep("11 House reef dive: entry brightness, visibility, air", StepDive);
            yield return RunStep("12 Camera (K.2 + round 3): no auto-record – LMB records, auto-detection, ≥ 5 clips without aiming, smart tape, sightings, Sealog photos", StepCamera, 240f);
            yield return RunStep("13 Back on the boat: footage banked, E.4 refill only with the boat compressor", StepBoatReturn);
            yield return RunStep("14 FinTube upload + sleep: views / followers / money", StepUploadSleep);
            yield return RunStep("15 Collectible pickup (E)", StepCollectible);
            yield return RunStep("16 Settings: volumes, quality, FPS cap, voice, key rebinding", StepSettings);
            yield return RunStep("17 Performance: FPS on the island and under water (Low / High)", StepFps, 200f);
            yield return RunStep("18 Save + continue (load)", StepSaveLoad);
            yield return RunStep("19 Back to the main menu", StepReturnToMenu);

            // ---------------- late game smoke test
            yield return RunStep("20 Late game: new game, tutorial done", StepLateNewGame);
            yield return RunStep("20b Open-world spawning census (L.1): fish everywhere, depth bands, locked sites", StepR2Census, 150f);
            for (int i = 0; i < ProgressionSites; i++)
            {
                int k = i;
                yield return RunStep("2" + (k + 1) + " Site " + DiveSites.All[k].id + ": unlock, boat, mini island, dive, film, journal page", () => StepSite(k), 200f);
            }
            yield return RunStep("26 Finale: 1,000,000 followers", StepFinale);
            yield return RunStep("27 Buy every item (helicopter crates) + equip the best gear", StepBuyAll, 240f);
            yield return RunStep("27b Core round-2 contracts: settings NaN, economy K.1, Sealog photo/sightings, deals, Fog Isle, food, finds, quests", StepCoreContracts);
            yield return RunRound2Steps();
            yield return RunRound3Steps();
            yield return RunStep("28 Quit to the main menu", StepReturnToMenu);

            Finish(report.failed == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ step runner
        IEnumerator RunStep(string name, Func<IEnumerator> body, float timeout = 180f)
        {
            cur = new StepResult { name = name };
            if (Time.realtimeSinceStartup - startTime > TimeBudget)
            {
                cur.pass = false;
                cur.skipped = true;
                cur.details = "skipped: time budget used up";
                report.steps.Add(cur);
                Note("SKIP " + name);
                WriteReport();
                cur = null;
                yield break;
            }
            int e0 = errorCount, x0 = exceptionCount;
            float t0 = Time.realtimeSinceStartup;
            Note("STEP " + name);
            GameInput.SimClear();
            // round 3: hunger / thirst sink over the long run – every step starts fed (the survival step tests the meters)
            if (GameManager.I != null && GameManager.I.InGame) { Survival.Set(Survival.Max, Survival.Max); Combat.Reset(); }
            yield return Guard(body(), name, timeout);
            GameInput.SimClear();
            // leave the player in a usable state for the next step
            if (pc != null && !pc.enabled) pc.enabled = true;
            // stop through the API, not with a simulated LMB: a manual stop under water suppresses the K.2 auto-record
            // until the next dive (correct for players), which made step 12 start without auto-recording
            var hc = Refs.HandCamera as FilmHandCamera;
            if (hc != null && hc.IsRecording)
            {
                hc.StopRecording();
                yield return null;
                yield return null;
            }
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
            if (UIState.InventoryOpen) UIModule.DebugOpenPhone("inventory:close");
            cur.seconds = Time.realtimeSinceStartup - t0;
            cur.errors = errorCount - e0;
            if (exceptionCount > x0) Check(false, (exceptionCount - x0) + " exception(s) logged during this step");
            report.steps.Add(cur);
            Note((cur.pass ? "PASS " : "FAIL ") + name + " (" + cur.seconds.ToString("0.0", Inv) + " s)");
            cur = null;
            WriteReport();
        }

        /// Runs a step coroutine (nested IEnumerators inline); an exception or a timeout fails the step.
        IEnumerator Guard(IEnumerator inner, string what, float timeout)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            float t0 = Time.realtimeSinceStartup;
            while (stack.Count > 0)
            {
                if (Time.realtimeSinceStartup - t0 > timeout)
                {
                    Check(false, "step timed out after " + timeout.ToString("0", Inv) + " s");
                    yield break;
                }
                var e = stack.Peek();
                object current;
                try
                {
                    if (!e.MoveNext()) { stack.Pop(); continue; }
                    current = e.Current;
                }
                catch (Exception ex)
                {
                    Check(false, "EXCEPTION in test code: " + ex.GetType().Name + ": " + ex.Message + " " + Trim(ex.StackTrace, 400));
                    yield break;
                }
                if (current is IEnumerator nested && !(current is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        bool Check(bool ok, string what)
        {
            Detail((ok ? "ok   " : "FAIL ") + what);
            if (!ok && cur != null) cur.pass = false;
            return ok;
        }

        void Info(string what) => Detail("info " + what);

        void Detail(string line)
        {
            if (cur != null) cur.details += (cur.details.Length > 0 ? "\n" : "") + line;
            Note("  " + line);
        }

        void Metric(string key, string value)
        {
            report.metrics.Add(key + " = " + value);
            Info(key + " = " + value);
        }

        void Note(string line)
        {
            Debug.Log("AutoPlaytest: " + line);
            log.AppendLine(DateTime.Now.ToString("HH:mm:ss", Inv) + "  " + line);
        }

        static string F(float v, string fmt = "0.0") => v.ToString(fmt, Inv);

        // ================================================================== 01 menu
        IEnumerator StepMenu()
        {
            if (PlayerIntro.Playing) PlayerIntro.Skip();
            var ui = UIModule.Controller;
            Check(ui != null, "UI module installed");
            if (ui != null && ui.Intro.Active)
            {
                yield return WaitUntil(() => !ui.Intro.Active, 8f);
                if (ui.Intro.Active) ui.Intro.Skip();
            }
            yield return Wait(2.5f);
            var gm = GameManager.I;
            Check(gm != null && !gm.InGame, "not in a game yet");
            Check(UIState.MainMenuOpen, "main menu open");
            var crew = GameObject.Find("MenuCrew");
            Check(crew != null, "bean-diver crew on the dock (MenuCrew" + (crew != null ? ", " + crew.transform.childCount + " children)" : ")"));
            Check(DifficultySettings.Selectable.Length == 3, "exactly 3 selectable difficulties (" + DifficultySettings.Selectable.Length + ")");
            Info("settings at start: quality " + Settings.Data.quality + ", fps cap " + Settings.Data.fpsLimit + ", vsync " + Settings.Data.vsync + ", language " + Settings.Data.language);
            yield return Shot("01_menu");
            yield return MeasureUncapped("menu", 8f);
        }

        // ================================================================== 02 new game
        IEnumerator StepNewGame()
        {
            var gm = GameManager.I;
            gm.NewGame(Difficulty.OpenWater);
            yield return Frames(3);
            yield return Wait(1.5f);
            pc = Refs.Player as PlayerController;
            Check(pc != null, "player controller registered");
            if (pc == null) yield break;
            cam = pc.View;
            lamp = cam != null ? cam.GetComponentInChildren<PlayerFlashlight>(true) : null;
            if (lamp == null) lamp = pc.GetComponentInChildren<PlayerFlashlight>(true);
            Check(gm.InGame && !UIState.MainMenuOpen, "in game, menu closed");
            Check(S.difficulty == Difficulty.OpenWater, "difficulty Normal (" + S.difficulty + ")");
            Check(S.day == 1, "day 1");
            Info("clock " + gm.ClockString + ", money " + S.money + " $, followers " + S.followers);
            Vector3 bed = WorldInfo.BedPosition != Vector3.zero ? WorldInfo.BedPosition : WorldLayout.Bed;
            float dBed = FlatDist(pc.Root.position, bed);
            Check(dBed < 6f, "woke up next to the bed in the hut (" + F(dBed) + " m from the bed)");
            Check(!pc.IsInWater, "on dry ground");
            Check(Hands.Active(Hand.Right) == HoldItem.None && Hands.Active(Hand.Left) == HoldItem.None, "both hands empty at the start (camera is a hotbar item)");
            Check(Hands.SlotItem(0) == HoldItem.Camera && Hands.SlotItem(1) == HoldItem.FishingRod && Hands.SlotItem(2) == HoldItem.Stick
                  && Hands.SlotItem(3) == HoldItem.Flashlight && Hands.SlotHand(3) == Hand.Left && Hands.SlotHand(0) == Hand.Right,
                  "default 8-slot hotbar: [camera, rod, stick] main hand, [lamp] offhand on key 4");
            Check(Backpack.Has(ItemIds.Stick) && Backpack.Has(ItemIds.BottleEmpty), "starter backpack: stick + empty bottle (" + Backpack.UsedSlots + " slots used)");
            Check(Survival.Hunger >= 99f && Survival.Thirst >= 99f, "survival meters full at the start (hunger " + F(Survival.Hunger, "0") + ", thirst " + F(Survival.Thirst, "0") + ")");
            foreach (var id in new[] { "rod_t0", "cam_t0", "lamp_t1", "tank_t0", "fins_t0", "boat_t0" })
                Check(S.ownedItems.Contains(id), "starter item " + id);
            Check(Mathf.Abs(pc.Gear.airSeconds - Progression.BreathHoldSeconds) < 0.5f, "no tank yet: breath-hold " + F(pc.Gear.airSeconds) + " s");
            yield return Shot("02_wake_hut");

            // #10: the hut roof must stop a jump
            Vector3 head = pc.Root.position + Vector3.up * (PlayerController.Height - 0.1f);
            if (Physics.Raycast(head, Vector3.up, out RaycastHit roof, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                float maxTop = pc.Root.position.y + PlayerController.Height;
                GameInput.SimTap(GameAction.Jump);
                float t = 0f;
                while (t < 1.3f)
                {
                    yield return null;
                    t += Time.unscaledDeltaTime;
                    maxTop = Mathf.Max(maxTop, pc.Root.position.y + PlayerController.Height);
                }
                Check(maxTop <= roof.point.y + 0.06f, "jumping in the hut: head top " + F(maxTop, "0.00") + " m stays under the roof collider at " + F(roof.point.y, "0.00") + " m (" + roof.collider.name + ")");
            }
            else Info("no roof collider found above the spawn point (hut roof check skipped)");

            // #10: the tire swing must be solid
            var swings = Object.FindObjectsByType<PropSwing>(FindObjectsSortMode.None);
            if (swings.Length == 0) Info("no tire swing found");
            foreach (var sw in swings)
            {
                bool solid = false;
                foreach (var c in sw.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) solid = true;
                if (!solid && sw.transform.parent != null)
                    foreach (var c in sw.transform.parent.GetComponentsInChildren<Collider>(true)) if (!c.isTrigger) solid = true;
                Check(solid, "tire swing '" + sw.name + "' has a solid collider");
            }
        }

        // ================================================================== 03 phone
        IEnumerator StepPhone()
        {
            int q0 = S.questIndex;
            Info("quest before: step " + q0 + " '" + TutorialQuests.Current.title + "'");
            GameInput.SimTap(GameAction.Phone);
            yield return WaitUntil(() => UIState.PhoneOpen, 2f);
            Check(UIState.PhoneOpen, "Tab opens the phone");
            yield return WaitUntil(() => PhoneDisplay.Raised, 2.5f);
            var phone = UIModule.Controller != null ? UIModule.Controller.Phone : null;
            Check(PhoneDisplay.Raised, "old 3D phone raised in the hand (In3D " + (phone != null && phone.In3D) + ")");
            yield return Wait(0.8f);
            yield return Shot("03_phone_home");
            UIModule.DebugOpenPhone("tasks");
            yield return Wait(1.2f);
            Check(phone != null && phone.Current == phone.Tasks, "Aufgaben app open");
            var qi = TutorialQuests.Current;
            Check(qi.active && !string.IsNullOrEmpty(qi.title), "current task shown: '" + qi.title + "' – " + qi.detail);
            yield return Shot("03b_phone_tasks");
            UIModule.DebugOpenPhone("gallery");
            yield return Wait(0.8f);
            Check(phone != null && phone.Current == phone.Gallery, "gallery app exists and opens");
            GameInput.SimTap(GameAction.Phone);
            yield return WaitUntil(() => !UIState.PhoneOpen, 2f);
            Check(!UIState.PhoneOpen, "Tab closes the phone");
            yield return Wait(0.6f);
            Check(S.questIndex >= 1, "tutorial advanced past 'open the phone' (step " + S.questIndex + ": '" + TutorialQuests.Current.title + "')");
        }

        // ================================================================== 04 hotbar
        IEnumerator StepHotbar()
        {
            Hands.StowAll();
            yield return Wait(0.5f);
            GameInput.SimTap(GameAction.RightSlot1);
            yield return Wait(0.9f);
            HeldModels(out bool rod, out bool camM, out bool lampM);
            Check(Hands.Active(Hand.Right) == HoldItem.Camera, "key 1: camera in the right hand");
            Check(camM && !rod && !lampM, "only the camera model visible (camera " + camM + ", rod " + rod + ", lamp " + lampM + ")");

            GameInput.SimTap(GameAction.LeftSlot1);
            yield return Wait(0.9f);
            HeldModels(out rod, out camM, out lampM);
            Check(Hands.Active(Hand.Left) == HoldItem.Flashlight && Hands.Active(Hand.Right) == HoldItem.Camera, "key 4: lamp in the left hand, camera stays in the right");
            Check(camM && lampM && !rod, "camera + lamp models visible, no rod (camera " + camM + ", lamp " + lampM + ", rod " + rod + ")");
            yield return Shot("04_hotbar_camera_lamp");

            GameInput.SimTap(GameAction.RightSlot2);
            yield return Wait(1.0f);
            HeldModels(out rod, out camM, out lampM);
            Check(Hands.Active(Hand.Right) == HoldItem.FishingRod, "key 2: fishing rod");
            Check(Hands.Active(Hand.Left) == HoldItem.None, "rod is two-handed: the lamp was put away");
            Check(rod && !camM && !lampM, "bug #6 fixed: only the rod is visible (rod " + rod + ", camera " + camM + ", lamp " + lampM + ")");
            yield return Shot("04b_hotbar_rod");

            GameInput.SimTap(GameAction.LeftSlot1);
            yield return Wait(1.0f);
            HeldModels(out rod, out camM, out lampM);
            Check(Hands.Active(Hand.Left) == HoldItem.Flashlight && Hands.Active(Hand.Right) == HoldItem.None, "selecting the lamp stows the two-handed rod");
            Check(lampM && !rod, "lamp visible, rod gone (lamp " + lampM + ", rod " + rod + ")");

            GameInput.SimTap(GameAction.LeftSlot1);
            yield return Wait(0.6f);
            Check(Hands.Active(Hand.Left) == HoldItem.None, "pressing the same key again empties the hand");
            yield return Wait(0.4f);
            HeldModels(out rod, out camM, out lampM);
            Check(!rod && !camM && !lampM, "empty hands: no item models");
        }

        // ================================================================== 05 fishing
        IEnumerator StepFishing()
        {
            Vector3 end = DockEnd();
            yield return Stand(SnapDown(end + new Vector3(-2.5f, 0.4f, 0f)), 180f);
            Check(!pc.IsInWater, "standing on the dock");
            if (!Hands.IsHeld(HoldItem.FishingRod))
            {
                GameInput.SimTap(GameAction.RightSlot2);
                yield return Wait(0.9f);
            }
            Check(Hands.IsHeld(HoldItem.FishingRod) && Hands.Active(Hand.Left) == HoldItem.None, "rod in both hands");
            yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Ready, 3f);
            Check(FishingRodTool.State == FishingRodTool.RodState.Ready, "rod ready (" + FishingRodTool.State + ")");
            if (FishingRodTool.State != FishingRodTool.RodState.Ready) yield break;

            long money0 = S.money;
            int inv0 = FishMarket.InventoryCount;
            Transform rig = cam.transform.Find("RodViewRoot/FishingRod");
            Quaternion rest = rig != null ? rig.localRotation : Quaternion.identity;
            int casts = 0, caught = 0, misses = 0, notWater = 0;
            float maxSwing = 0f, t0 = Time.realtimeSinceStartup, biteSum = 0f;
            var fishLine = new StringBuilder();
            long valueSum = 0;

            while (FishMarket.InventoryCount < inv0 + 5 && Time.realtimeSinceStartup - t0 < 380f)
            {
                if (FishingRodTool.State != FishingRodTool.RodState.Ready)
                {
                    yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Ready, 6f);
                    if (FishingRodTool.State != FishingRodTool.RodState.Ready)
                    {
                        Check(false, "rod stuck in state " + FishingRodTool.State);
                        break;
                    }
                }
                float tc = Time.realtimeSinceStartup;
                GameInput.SimPress(GameAction.Use);
                float ct = 0f;
                while (ct < 0.8f)
                {
                    yield return null;
                    ct += Time.unscaledDeltaTime;
                    if (rig != null) maxSwing = Mathf.Max(maxSwing, Quaternion.Angle(rest, rig.localRotation));
                }
                GameInput.SimRelease(GameAction.Use);
                casts++;
                yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Waiting || FishingRodTool.State == FishingRodTool.RodState.Bite
                                             || FishingRodTool.State == FishingRodTool.RodState.Ready, 6f);
                if (FishingRodTool.State == FishingRodTool.RodState.Ready)
                {
                    notWater++;
                    GameInput.SimLook(new Vector2(40f, 0f));   // the cast hit no water: turn a bit
                    yield return Wait(0.3f);
                    if (notWater > 6) { Check(false, "every cast missed the water"); break; }
                    continue;
                }
                if (casts == 1) yield return Shot("05_fishing_wait");
                float tw = Time.realtimeSinceStartup;
                yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Bite || FishingRodTool.State == FishingRodTool.RodState.Ready, 50f);
                if (FishingRodTool.State != FishingRodTool.RodState.Bite)
                {
                    misses++;
                    Info("no bite after " + F(Time.realtimeSinceStartup - tw) + " s (state " + FishingRodTool.State + ")");
                    if (FishingRodTool.State == FishingRodTool.RodState.Waiting) GameInput.SimTap(GameAction.Use);
                    continue;
                }
                biteSum += Time.realtimeSinceStartup - tw;
                int before = FishMarket.InventoryCount;
                GameInput.SimTap(GameAction.Use);   // strike
                yield return WaitUntil(() => FishMarket.InventoryCount > before, 8f);
                if (FishMarket.InventoryCount > before)
                {
                    caught++;
                    var f = S.fishInventory[S.fishInventory.Count - 1];
                    valueSum += f.value;
                    fishLine.Append(f.kindId).Append(' ').Append(F(f.weightKg, "0.00")).Append(" kg = ").Append(f.value).Append(" $ (")
                            .Append(F(Time.realtimeSinceStartup - tc, "0")).Append(" s); ");
                    if (caught == 1)
                    {
                        yield return Wait(0.25f);
                        yield return Shot("05b_fishing_catch");
                    }
                }
                else
                {
                    misses++;
                    Info("strike did not land a fish (state " + FishingRodTool.State + ")");
                }
                yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Ready, 5f);
            }

            float total = Time.realtimeSinceStartup - t0;
            Check(FishMarket.InventoryCount >= inv0 + 5, "5 fish in the cooler box (" + (FishMarket.InventoryCount - inv0) + " caught in " + F(total, "0") + " s, " + casts + " casts, " + misses + " misses)");
            Check(S.money == money0, "caught fish are NOT sold automatically (money still " + S.money + " $)");
            Check(rig != null && maxSwing > 20f, "hold-to-cast swing animation: the rod swings back " + F(maxSwing, "0") + "° while charging");
            Metric("fishing.catches", fishLine.ToString());
            Metric("fishing.value5", valueSum + " $ for " + caught + " fish (" + (caught > 0 ? F(valueSum / (float)caught) : "0") + " $ avg)");
            Metric("fishing.secondsPerFish", caught > 0 ? F(total / caught, "0") : "-");
            if (caught > 0) { fishSecondsPerFish = total / caught; fishAvgValue = valueSum / (float)caught; }
            Metric("fishing.avgBiteWait", caught > 0 ? F(biteSum / caught, "0.0") + " s" : "-");
            float tankPrice = Shop.Price(GearDatabase.Get("tank_t1"), false);
            Metric("fishing.fishForTank", caught > 0 && valueSum > 0 ? F(tankPrice / (valueSum / (float)caught), "0.0") + " fish ≈ " + F(tankPrice / (valueSum / (float)caught) * total / caught / 60f, "0.0") + " min for the first tank (" + tankPrice + " $)" : "-");
            Info("tutorial step " + S.questIndex + ": '" + TutorialQuests.Current.title + "'");
        }

        // ================================================================== 06 market
        IEnumerator StepMarket()
        {
            int inv = FishMarket.InventoryCount;
            long value = FishMarket.InventoryValue, money0 = S.money;
            Check(inv >= 1, "fish to sell: " + inv + " worth " + value + " $");
            UIModule.DebugOpenPhone("market");
            yield return Wait(1.2f);
            yield return Shot("06_market_app");
            int n = FishMarket.ListAll();
            Check(n == inv && FishMarket.InventoryCount == 0 && FishMarket.ListingCount == n, "listed all " + n + " fish (cooler " + FishMarket.InventoryCount + ", listings " + FishMarket.ListingCount + ")");
            yield return Wait(1.0f);
            yield return Shot("06b_market_listed");
            UIModule.DebugOpenPhone("close");
            float t0 = Time.realtimeSinceStartup, first = -1f;
            int left0 = FishMarket.ListingCount;
            while (FishMarket.ListingCount > 0 && Time.realtimeSinceStartup - t0 < 95f)
            {
                if (first < 0f && FishMarket.ListingCount < left0) first = Time.realtimeSinceStartup - t0;
                yield return null;
            }
            float all = Time.realtimeSinceStartup - t0;
            Check(FishMarket.ListingCount == 0 && all <= 75f, "every listing sold – first after " + F(first, "0") + " s, all after " + F(all, "0") + " s");
            Check(S.money - money0 == value, "money +" + (S.money - money0) + " $ (expected +" + value + " $)");
            Metric("market.sellSeconds", "first " + F(first, "0") + " s, all " + F(all, "0") + " s");
            Info("tutorial step " + S.questIndex + ": '" + TutorialQuests.Current.title + "'");
        }

        // ================================================================== 07 shop + helicopter
        IEnumerator StepShop()
        {
            var gm = GameManager.I;
            var tank = GearDatabase.Get("tank_t1");
            long price = Shop.Price(tank, false);
            if (S.money < price)
            {
                long add = price - S.money;
                gm.AddMoney(add);
                Info("test top-up +" + add + " $ (the 5 tutorial fish do not pay for the " + price + " $ tank on their own – keep fishing)");
            }
            // realistic first-tank timeline without the test top-up: a human fishes ~1.3× slower than the bot and needs
            // ~4 real minutes for walking, two market rounds and the shop; the tutorial tank flies with the express timing
            if (fishSecondsPerFish > 0f && fishAvgValue > 0f)
            {
                float fishNeeded = Mathf.Ceil(price / fishAvgValue);
                float realSec = fishNeeded * fishSecondsPerFish * 1.3f + 240f;
                float order = GameManager.MorningTime + realSec / GameManager.DayLengthSeconds;
                float arrive = order + Shop.ExpressHours / 24f;
                Metric("tutorial.firstTank", F(fishNeeded, "0") + " fish ≈ " + F(realSec / 60f, "0.0") + " real min → ordered ~" + GameManager.Clock(order) + ", crate ~" + GameManager.Clock(arrive));
                Check(arrive < 18f / 24f, "realistic tutorial: the first tank arrives before 18:00 (~" + GameManager.Clock(arrive) + ", day length " + F(GameManager.DayLengthSeconds / 60f, "0") + " min) – first dive in daylight");
            }
            if (S.timeOfDay > 0.78f) S.timeOfDay = 0.45f;
            float now = S.timeOfDay;
            int day = S.day;
            long money0 = S.money;
            bool tutorialTank = TutorialQuests.TutorialRunning;
            UIModule.DebugOpenPhone("shop");
            yield return Wait(1.2f);
            yield return Shot("07_shop_app");
            bool ok = Shop.Buy(tank, false);
            Check(ok, "bought " + tank.id + " for " + price + " $ at " + GameManager.Clock(now));
            var d = Shop.FindDelivery("tank_t1");
            Check(d != null, "delivery created");
            if (d == null) yield break;
            float hours = ((d.arrivalDay - day) + (d.arrivalTime - now)) * 24f;
            Check(d.arrivalDay == day, "delivery arrives the SAME day at " + GameManager.Clock(d.arrivalTime) + " (" + F(hours) + " in-game hours later)");
            if (tutorialTank)
                Check(Mathf.Abs(hours - Shop.ExpressHours) < 0.1f, "the tutorial tank flies with the express timing (" + F(hours) + " h) at the standard price");
            else
                Check(hours >= 1.9f && hours <= 3.1f, "delivery takes 2–3 in-game hours");
            Check(money0 - S.money == price, "paid " + (money0 - S.money) + " $");
            Metric("shop.deliveryHours", F(hours) + " h in-game ≈ " + F(hours / 24f * GameManager.DayLengthSeconds / 60f) + " real minutes");
            UIModule.DebugOpenPhone("close");

            // wait at the helipad, fast-forward the clock to the arrival time
            Vector3 pad = WorldInfo.HelipadPosition != Vector3.zero ? WorldInfo.HelipadPosition : WorldLayout.Helipad;
            Vector3 standAt = SnapDown(pad + new Vector3(9f, 3f, 9f));
            yield return Stand(standAt, YawTo(standAt, pad));
            S.timeOfDay = d.arrivalTime + 0.0015f;
            float tw = Time.realtimeSinceStartup;
            HeliCrate crate = null;
            bool shotHeli = false;
            while (Time.realtimeSinceStartup - tw < 120f)
            {
                crate = FindCrate("tank_t1", true);
                if (crate != null) break;
                var chopper = Object.FindFirstObjectByType<HeliChopper>();
                if (!shotHeli && Time.realtimeSinceStartup - tw > 14f && chopper != null)
                {
                    shotHeli = true;
                    // aim at the chopper itself (a fixed point 22 m above the pad showed only sky and palm leaves)
                    yield return AimAt(chopper.transform.position);
                    yield return Shot("07b_helicopter");
                }
                yield return Wait(0.5f);
            }
            Check(crate != null, "helicopter dropped the crate on the helipad (" + F(Time.realtimeSinceStartup - tw, "0") + " s after the arrival time)");
            if (crate == null) yield break;
            Vector3 cp = crate.transform.position;
            Vector3 dir = Flat(pc.Root.position - cp);
            Vector3 feet = SnapDown(cp + dir * 1.9f + Vector3.up * 2f);
            yield return Stand(feet, YawTo(feet, cp));
            yield return AimAt(cp + Vector3.up * 0.35f);
            Info("prompt at the crate: '" + InteractionPrompt.Text + "'");
            yield return Shot("07c_crate");
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.7f);
            if (!S.ownedItems.Contains("tank_t1"))
            {
                Check(false, "E at the crate did not open it (interactor) – opened directly");
                if (crate != null) crate.Interact();
                yield return Wait(0.3f);
            }
            else Check(true, "E opens the crate");
            Check(S.ownedItems.Contains("tank_t1"), "tank_t1 owned");
            Check(Gear.IsEquipped("tank_t1"), "tank_t1 equipped right away");
            Check(Mathf.Abs(pc.Gear.airSeconds - 90f) < 0.5f, "first tank = " + F(pc.Gear.airSeconds) + " s of air (target ~90 s), max depth " + F(pc.Gear.maxDepth, "0") + " m");
            Info("tutorial step " + S.questIndex + ": '" + TutorialQuests.Current.title + "'");
        }

        // ================================================================== 08 inventory
        IEnumerator StepInventory()
        {
            GameInput.SimTap(GameAction.Inventory);
            yield return WaitUntil(() => UIState.InventoryOpen, 1.5f);
            Check(UIState.InventoryOpen, "I opens the inventory");
            yield return Wait(0.7f);
            yield return Shot("08_inventory");
            HoldItem r0 = Hands.Active(Hand.Right);
            GameInput.SimTap(GameAction.RightSlot1);
            yield return Wait(0.3f);
            Check(Hands.Active(Hand.Right) == r0, "hotbar keys are ignored while the inventory is open");
            Check(Gear.Equip("tank_t0"), "Gear.Equip(tank_t0)");
            Check(Mathf.Abs(pc.Gear.airSeconds - Progression.BreathHoldSeconds) < 0.5f, "snorkel equipped → " + F(pc.Gear.airSeconds) + " s");
            Check(Gear.Equip("tank_t1"), "Gear.Equip(tank_t1)");
            Check(Mathf.Abs(pc.Gear.airSeconds - 90f) < 0.5f, "tank equipped → " + F(pc.Gear.airSeconds) + " s");
            Check(!Gear.Equip("cam_t3"), "an item that is not owned cannot be equipped");
            Hands.Assign(4, ItemIds.Camera, Hand.Left);
            Check(Hands.SlotItem(4) == HoldItem.Camera && Hands.SlotHand(4) == Hand.Left, "camera put on hotbar slot 5 for the offhand");
            Hands.ResetToDefaults();
            Check(Hands.SlotItem(0) == HoldItem.Camera && Hands.SlotItem(3) == HoldItem.Flashlight && Hands.SlotItem(4) == HoldItem.None, "hotbar reset to defaults");
            GameInput.SimTap(GameAction.Inventory);
            yield return WaitUntil(() => !UIState.InventoryOpen, 1.5f);
            Check(!UIState.InventoryOpen, "I closes the inventory");
        }

        // ================================================================== 09 boat
        IEnumerator StepBoat()
        {
            var boat = Refs.Boat as BoatController;
            Check(boat != null, "boat exists");
            if (boat == null) yield break;
            if (!boat.Anchored) boat.ToggleAnchor();
            Transform seat = boat.transform.Find("DriverSeat");
            Check(seat != null, "driver seat found");
            if (seat == null) yield break;
            var helms = boat.GetComponentsInChildren<BoatHelmInteractable>(true);
            Check(helms.Length > 0, helms.Length + " helm interactables");

            // far away: on the dock
            Vector3 dockFeet = SnapDown(DockEnd() + new Vector3(-1.5f, 0.4f, 0f));
            yield return Stand(dockFeet, YawTo(dockFeet, seat.position));
            float dist = FlatDist(pc.Root.position, seat.position);
            bool anyCan = false;
            foreach (var h in helms) if (h.CanInteract()) anyCan = true;
            Check(!anyCan, "the wheel refuses from the dock (" + F(dist) + " m from the seat)");
            yield return AimAt(seat.position + Vector3.up * 0.6f);
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.6f);
            Check(!boat.PlayerDriving && !pc.IsSeated, "E from the dock does not start the boat");
            yield return Shot("09_boat_from_dock");

            // board and stand next to the seat
            if (!boat.PlayerAboard) boat.BoardFromDock();
            yield return Wait(0.9f);
            Check(boat.PlayerAboard, "boarded from the dock");
            Vector3 sl = boat.transform.InverseTransformPoint(seat.position);
            Vector3 feet = boat.transform.TransformPoint(new Vector3(0f, sl.y + 0.45f, sl.z - 1.0f));
            pc.TeleportTo(feet, boat.transform.eulerAngles.y);
            yield return Wait(0.9f);
            anyCan = false;
            foreach (var h in helms) if (h.CanInteract()) anyCan = true;
            Check(anyCan, "the wheel is usable standing next to the seat (" + F(FlatDist(pc.Root.position, seat.position)) + " m)");
            Hands.Select(0);
            yield return AimAt(seat.position + boat.transform.up * 0.5f);
            Info("prompt at the seat: '" + InteractionPrompt.Text + "'");
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.7f);
            if (!boat.PlayerDriving)
            {
                Check(false, "E at the seat did not start driving (interactor) – used the helm directly");
                foreach (var h in helms) if (h.CanInteract()) { h.Interact(); break; }
                yield return Wait(0.5f);
            }
            Check(boat.PlayerDriving && pc.IsSeated, "sitting at the wheel");
            Check(Hands.Active(Hand.Right) == HoldItem.None && Hands.Active(Hand.Left) == HoldItem.None, "both hands on the wheel (items stowed)");
            yield return Shot("09b_boat_helm");

            if (boat.Anchored)
            {
                GameInput.SimTap(GameAction.Anchor);
                yield return Wait(0.6f);
            }
            Check(!boat.Anchored, "Q raises the anchor");
            Vector3 p0 = boat.transform.position;
            GameInput.SimPress(GameAction.Forward);
            yield return Wait(5f);
            GameInput.SimRelease(GameAction.Forward);
            float moved = FlatDist(boat.transform.position, p0);
            Check(moved > 4f, "W drives the boat (" + F(moved) + " m in 5 s)");
            yield return Shot("09c_boat_driving");
            for (int i = 0; i < 8 && boat.PlayerDriving; i++)
            {
                yield return Wait(1.2f);
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.4f);
            }
            Check(!boat.PlayerDriving && !pc.IsSeated, "E gets up from the wheel");
            // back to the mooring
            Vector3 spawn = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawn : WorldLayout.BoatSpawn;
            boat.PlaceAt(spawn, WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawnYaw : WorldLayout.BoatSpawnYaw);
            if (!boat.Anchored) boat.ToggleAnchor();
            yield return Wait(0.5f);
        }

        // ================================================================== 10 dock ladder
        IEnumerator StepLadder()
        {
            Vector3 top = PropInfo.DockLadderTop, bottom = PropInfo.DockLadderBottom;
            Check(top != Vector3.zero && bottom != Vector3.zero, "props published the dock ladder");
            if (top == Vector3.zero || bottom == Vector3.zero) yield break;
            Hands.StowAll();
            Vector3 sea = Flat(bottom - top);
            Vector3 feet = new Vector3(bottom.x, Waves.SeaLevel - PlayerController.EyeHeight + 0.12f, bottom.z) + sea * 0.9f;
            pc.enabled = true;
            pc.TeleportTo(feet, YawTo(feet, top));
            yield return WaitUntil(() => pc.CanLadderHint, 1.5f);
            Check(pc.IsInWater, "swimming next to the dock ladder");
            Check(pc.CanLadderHint, "ladder hint shown ('" + InteractionPrompt.Text + "')");
            yield return Shot("10_ladder_from_water");
            GameInput.SimTap(GameAction.Jump);
            // L.13: the generic climb – Space grabs the ladder; W / Space HELD climbs (0.65 m/s, eased per rung, mantle).
            // No auto-climb any more (GDD §19): one tap only grabs it.
            yield return Wait(1.3f);
            bool held = false;
            if (pc.OnGenericLadder)
            {
                float y0 = pc.LadderRootY;
                yield return Wait(0.8f);
                Check(pc.OnGenericLadder && pc.LadderRootY - y0 < 0.05f, "Space grabbed the dock ladder and waits for input (rung " + F(pc.LadderRootY, "0.00") + " m, moved " + F(pc.LadderRootY - y0, "0.00") + " m without input)");
                GameInput.SimPress(GameAction.Jump);   // holding Space climbs
                held = true;
                yield return Wait(0.8f);
                yield return Shot("10a_ladder_hands");
            }
            else Info("dock ladder climbed by the scripted fallback (not in PropInfo.Ladders)");
            yield return WaitUntil(() => !pc.IsBusy && !pc.IsInWater && pc.Root.position.y > Waves.SeaLevel + 0.6f, 12f);
            if (held) GameInput.SimRelease(GameAction.Jump);
            float dTop = FlatDist(pc.Root.position, top);
            Check(!pc.IsInWater && pc.Root.position.y > Waves.SeaLevel + 0.6f && dTop < 1.8f,
                  "Space climbs the ladder onto the dock (feet y " + F(pc.Root.position.y, "0.00") + ", " + F(dTop) + " m from the top)");
            yield return Shot("10b_ladder_on_dock");
        }

        // ================================================================== 11 dive
        IEnumerator StepDive()
        {
            SetTime(0.45f);
            Hands.StowAll();
            GameInput.SimTap(GameAction.RightSlot1);
            yield return Wait(0.5f);
            Check(Hands.Active(Hand.Right) == HoldItem.Camera, "camera taken (key 1)");
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -7.5f);
            Info("dive spot " + V(floor) + " (floor " + F(-floor.y) + " m)");
            Vector3 feet = new Vector3(floor.x, Waves.SeaLevel - PlayerController.EyeHeight + 0.1f, floor.z);
            pc.enabled = true;
            pc.TeleportTo(feet, YawTo(feet, floor + Flat(site.center - floor) * 10f));
            pc.RefillAir();
            yield return Wait(1.2f);
            Check(pc.IsInWater && !pc.IsUnderwater, "swimming at the surface");
            yield return Shot("11_surface");
            float lumaSurface = lastLuma;

            GameInput.SimPress(GameAction.Down);
            yield return WaitUntil(() => pc.Depth >= 2.5f, 4f);
            GameInput.SimRelease(GameAction.Down);
            yield return Wait(1.0f);
            Check(pc.IsUnderwater, "Ctrl dives under (depth " + F(pc.Depth) + " m)");
            yield return Shot("11b_entry_3m");
            float lumaEntry = lastLuma;
            float vis = FXVisibility.Meters, scan = FilmShotEvaluator.ScanRange();
            Check(lumaEntry >= 0.15f, "near water bright on entry: mean screen brightness " + F(lumaEntry, "0.00") + " (surface " + F(lumaSurface, "0.00") + ")");
            Check(vis >= 10f && vis <= 30f, "visibility in the shallows " + F(vis, "0") + " m (target 15–20 m)");
            Check(scan <= FilmShotEvaluator.MaxScanRange + 0.01f, "camera scan range " + F(scan) + " m");
            Metric("water.entry", "depth " + F(pc.Depth) + " m, visibility " + F(vis, "0") + " m, clear " + F(FXVisibility.ClearMeters, "0") + " m, luma " + F(lumaEntry, "0.00"));

            GameInput.SimPress(GameAction.Down);
            yield return WaitUntil(() => pc.Depth >= 6f, 4f);
            GameInput.SimRelease(GameAction.Down);
            yield return Wait(0.5f);
            float cap = pc.Gear.airSeconds;
            float a0 = pc.AirSecondsLeft, d0 = pc.Depth;
            yield return Wait(8f);
            float a1 = pc.AirSecondsLeft, d1 = pc.Depth;
            float rate = (a0 - a1) / 8f;
            float expect = Progression.AirRate((d0 + d1) * 0.5f);
            Check(Mathf.Abs(cap - 90f) < 1f, "tank capacity " + F(cap) + " s");
            Check(rate > expect * 0.8f && rate < expect * 1.35f, "air drains " + F(rate, "0.00") + " s/s at " + F((d0 + d1) * 0.5f) + " m (expected " + F(expect, "0.00") + ")");
            Metric("air.budget", "full tank at " + F((d0 + d1) * 0.5f, "0") + " m ≈ " + F(cap / Mathf.Max(0.01f, rate), "0") + " s");
            vis = FXVisibility.Meters;
            Metric("water.6m", "visibility " + F(vis, "0") + " m");
            yield return Shot("11c_reef_6m");
        }

        // ================================================================== 12 camera
        IEnumerator StepCamera()
        {
            var hc = Refs.HandCamera as FilmHandCamera;
            Check(hc != null, "hand camera exists");
            if (hc == null) yield break;
            if (!pc.IsUnderwater)
            {
                var site = DiveSites.Get("house_reef");
                Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -7.5f);
                yield return DiveTo(KeepInWater(floor + Vector3.up * 3f, 2.5f, 2f), floor + Vector3.up * 3f + Vector3.forward * 10f);
            }
            if (!Hands.IsHeld(HoldItem.Camera) && Hands.Find(HoldItem.Camera, out Hand ch, out int cs)) Hands.Select(cs);
            Check(Hands.IsHeld(HoldItem.Camera), "camera in hand");
            pc.RefillAir();
            pc.enabled = false;
            Vector3 eye = cam.transform.position;
            Vector3 fwd = Flat(cam.transform.forward);
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            int clips0 = Footage.DiveCount;

            // ---- round 3 (a): entering the water does NOT start recording – only the player's own click (LMB) does
            yield return Wait(1.5f);
            Check(!hc.IsRecording, "no auto-record under water (GDD §19) – " + hc.DebugSummary());
            GameInput.SimTap(GameAction.Use);
            yield return WaitUntil(() => hc.IsRecording, 3f);
            Check(hc.IsRecording && !hc.AutoRecording, "LMB starts recording (" + hc.DebugSummary() + ")");

            // ---- (b) smart tape: nothing in frame → the tape does not run
            var spawner = FaunaModule.Spawner;
            if (spawner != null) spawner.DespawnAll();
            Pose(eye, eye + Vector3.up * 12f + fwd * 2f);
            yield return Wait(0.6f);
            float tape0 = hc.TapeSecondsLeft;
            yield return Wait(2f);
            float tape1 = hc.TapeSecondsLeft;
            if (hc.DetectedCount == 0) Check(Mathf.Abs(tape1 - tape0) < 0.1f, "smart tape: no animal in frame → tape stays (" + F(tape0) + " → " + F(tape1) + " s)");
            else Info("smart tape check skipped: " + hc.DetectedCount + " wild animals swam into the frame");

            // ---- (c) every animal in frame is detected automatically: 5 individuals → ≥ 5 clips, no aiming
            Pose(eye, eye + fwd * 10f);
            string[] crowdIds = { "blue_tang", "blue_tang", "clownfish", "lionfish", "moon_jelly", "cleaner_wrasse" };
            var crowd = new List<ICreature>();
            for (int i = 0; i < crowdIds.Length; i++)
            {
                float x = (i - (crowdIds.Length - 1) * 0.5f) * 0.55f;
                Vector3 p = KeepInWater(eye + fwd * (3.2f + (i % 2) * 0.8f) + right * x + Vector3.up * ((i % 3) - 1) * 0.25f, 1f, 1.2f);
                var c = SpawnAt(crowdIds[i], p, SizeTier.Large);
                if (c != null) crowd.Add(c);
            }
            Check(crowd.Count >= 5, crowd.Count + " animals spawned in front of the lens");
            int maxDetected = 0;
            float tt = 0f;
            bool shot = false;
            while (tt < 3.2f)
            {
                Pose(eye, eye + fwd * 10f);
                maxDetected = Mathf.Max(maxDetected, hc.DetectedCount);
                if (!shot && tt > 1.6f) { shot = true; yield return Shot("12_auto_detect_brackets"); }
                yield return null;
                tt += Time.deltaTime;
            }
            int newClips = Footage.DiveCount - clips0;
            Check(maxDetected >= 4, "detected automatically: up to " + maxDetected + " animals in frame at once (brackets " + ViewfinderInfo.BracketCount + ")");
            Check(newClips >= 5, newClips + " clips without aiming (≥ 5 expected) – " + hc.DebugSummary());
            int crowdMax = 0;
            for (int i = 0; i < S.diveFootage.Count; i++) crowdMax = Mathf.Max(crowdMax, S.diveFootage[i].crowd);
            Check(crowdMax >= 2, "crowd bonus data: up to " + crowdMax + " animals in one clip");
            Check(!Journal.IsDiscovered("blue_tang") && !Journal.IsDiscovered("lionfish"), "filming alone does not fill the Sealog (it needs a photo)");
            // L.2 sightings: counted per individual and depth (flushed in batches)
            yield return WaitUntil(() => { var be = Journal.Find("blue_tang"); return be != null && be.sightings > 0; }, 12f);
            var tang = Journal.Find("blue_tang");
            Check(tang != null && tang.sightings > 0, "sightings counted: blue tang " + (tang != null ? tang.sightings + "× at " + F(tang.minDepthSeen, "0") + "–" + F(tang.maxDepthSeen, "0") + " m" : "none"));
            foreach (var c in crowd) if (c != null && c.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(c);

            // ---- (d) a far photo does not register
            // a fist-sized fish (lionfish ≤ 38 cm): at 12 m it fills far less than the 10 % Sealog minimum. A big octopus
            // (arms spread up to ~1.5 m) may legitimately count from 12 m, so it is not used here.
            var far = SpawnAt("lionfish", KeepInWater(eye + fwd * 12f, 1.2f, 1.5f), SizeTier.Normal);
            if (far != null)
            {
                yield return PhotoOf(far, 12f, null);
                Check(!Journal.IsDiscovered("lionfish"), "a photo from 12 m does not count for the Sealog (" + photoNote + ")");
                if (far.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(far);
            }

            // ---- (e) Sealog = photo: zoom in on an animal (aim) and take a quick photo – 3 species for the tutorial
            int photos0 = Gallery.Count;
            foreach (var id in new[] { "green_turtle", "parrotfish", "octopus" })
            {
                eye = cam.transform.position;
                var c = SpawnAt(id, KeepInWater(eye + fwd * 3.5f + Vector3.up * 0.3f, 1.2f, 1.5f), SizeTier.Large);
                if (c == null) { Check(false, "could not spawn " + id); continue; }
                yield return PhotoOf(c, FilmDistance(c), id == "parrotfish" ? "12_sealog_photo_parrotfish" : null);
                var je = Journal.Find(id);
                Check(Journal.IsDiscovered(id) && je != null && je.photos >= 1, id + " photographed → Sealog entry (" + photoNote + ")");
                if (c.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(c);
            }
            var lastPhoto = Gallery.Count > 0 ? Gallery.Photos[Gallery.Count - 1] : null;
            Check(Gallery.Count > photos0 && lastPhoto != null && Gallery.Exists(lastPhoto), "photos in the gallery (" + Gallery.Count + ", last '" + (lastPhoto != null ? lastPhoto.file + "' species " + lastPhoto.speciesId : "-") + ")");
            Info("clips this dive: " + Footage.DiveCount + " (+" + (Footage.DiveCount - clips0) + ")");
            yield return Wait(0.6f);
            Check(S.questIndex >= 7, "tutorial: 3 species photographed for the Sealog (step " + S.questIndex + ": '" + TutorialQuests.Current.title + "')");
            // the Sealog screenshot is taken on the boat (step 13): under water the phone stays dry
            yield return Frames(2);
            pc.enabled = true;
        }

        // ================================================================== 13 back on the boat
        IEnumerator StepBoatReturn()
        {
            var boat = Refs.Boat as BoatController;
            Check(boat != null, "boat exists");
            if (boat == null) yield break;
            pc.enabled = true;
            if (!pc.IsInWater)
            {
                var site = DiveSites.Get("house_reef");
                Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -7.5f);
                pc.TeleportTo(new Vector3(floor.x, -4f, floor.z), 0f);
                yield return Wait(0.8f);
            }
            int dive = Footage.DiveCount;
            Check(dive > 0, dive + " clips from this dive (not yet safe)");
            yield return Wait(4f);   // breathe from the tank for a moment (the camera test froze the controller)
            float airBefore = pc.Air01;
            // the boat comes over
            Vector3 p = pc.Root.position;
            Vector3 bp = FindWater(p + new Vector3(8f, 0f, 0f), 2.5f);
            boat.PlaceAt(bp, 90f);
            yield return Wait(1.0f);
            Vector3 stern = boat.transform.TransformPoint(new Vector3(0.6f, 0f, -5.6f));
            Vector3 feet = new Vector3(stern.x, Waves.SeaLevel - PlayerController.EyeHeight + 0.1f, stern.z);
            pc.TeleportTo(feet, boat.transform.eulerAngles.y);
            yield return Wait(0.8f);
            Check(pc.IsInWater, "at the surface behind the boat");
            var ladder = boat.GetComponentInChildren<BoatLadderInteractable>(true);
            Check(ladder != null && ladder.CanInteract(), "boat ladder usable from the water");
            if (ladder != null)
            {
                var col = ladder.GetComponent<Collider>();
                yield return AimAt(col != null ? col.bounds.center : ladder.transform.position);
                Info("prompt at the boat ladder: '" + InteractionPrompt.Text + "'");
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.8f);
                if (!boat.PlayerAboard)
                {
                    Info("E did not hit the boat ladder from here – climbing via the ladder interactable");
                    ladder.Interact();
                    yield return Wait(0.6f);
                }
            }
            Check(boat.PlayerAboard && !pc.IsInWater, "climbed aboard");
            Check(Footage.DiveCount == 0 && Footage.BankedCount >= dive, "footage banked (banked " + Footage.BankedCount + ")");
            Check(airBefore < 0.98f, "air was used on the dive (" + F(airBefore * 100f, "0") + " %)");
            // E.4 (Q27): on the boat the tank refills only with the crew's boat compressor (600 $, 250 followers)
            bool hadCompressor = BoatAirStation.Owned;
            if (!hadCompressor)
            {
                float a0 = pc.Air01;
                yield return Wait(2f);
                Check(!pc.RefillingAir && pc.Air01 <= a0 + 0.001f && !pc.AtAirStation, "E.4: no refill on deck without the boat compressor (air " + F(pc.Air01 * 100f, "0") + " %)");
                S.ownedItems.Add("boat_compressor");
                GameEvents.RaiseGearChanged();
                yield return Frames(3);
                Check(BoatAirStation.Owned, "boat compressor owned (crew equipment)");
            }
            yield return WaitUntil(() => pc.RefillingAir || pc.Air01 >= 0.999f, 2f);
            Check(pc.RefillingAir || pc.Air01 >= 0.999f, "with the compressor the tank refills on deck");
            float tr = Time.realtimeSinceStartup;
            yield return WaitUntil(() => pc.Air01 >= 0.999f, 9f);
            Check(pc.Air01 >= 0.999f, "tank full after " + F(Time.realtimeSinceStartup - tr) + " s on deck (E.4: 6 s from empty)");
            yield return Shot("13_boat_deck");
            // L.2 Sealog with the three photographed species (counters, depth chips) – the phone works on deck
            UIModule.DebugOpenPhone("sealog");
            yield return Wait(1.2f);
            Check(UIState.PhoneOpen, "Sealog app opens on deck");
            yield return Shot("12b_sealog");
            UIModule.DebugOpenPhone("close");
            yield return Frames(2);
            if (!hadCompressor)
            {
                // keep the tutorial economy unchanged for the next steps
                S.ownedItems.Remove("boat_compressor");
                GameEvents.RaiseGearChanged();
            }
        }

        // ================================================================== 14 upload + sleep
        IEnumerator StepUploadSleep()
        {
            var gm = GameManager.I;
            Check(Footage.BankedCount > 0, Footage.BankedCount + " banked clips");
            UIModule.DebugOpenPhone("fintube");
            yield return Wait(1.2f);
            yield return Shot("14_fintube");
            var pv = Progression.Preview(TitleStyle.Honest);
            Info("FinTube preview: " + pv.viewsMin + "–" + pv.viewsMax + " views, +" + pv.followers + " followers, +" + pv.money + " $");
            var up = Progression.CreateUpload(TitleStyle.Honest);
            Check(up != null && S.pendingUploads.Count == 1 && Footage.BankedCount == 0, "uploaded '" + (up != null ? up.title : "-") + "' (" + (up != null ? up.clips.Count : 0) + " clips)");
            UIModule.DebugOpenPhone("close");
            yield return Wait(0.5f);
            Info("tutorial step " + S.questIndex + ": '" + TutorialQuests.Current.title + "'");

            Vector3 spawn = PlayerController.ResolveSpawn(out float yaw);
            yield return Stand(spawn, yaw);
            PropBed bed = null;
            float best = float.MaxValue;
            foreach (var b in Object.FindObjectsByType<PropBed>(FindObjectsSortMode.None))
            {
                float dd = (b.transform.position - pc.Root.position).sqrMagnitude;
                if (dd < best) { best = dd; bed = b; }
            }
            Check(bed != null, "bed found");
            int day0 = S.day;
            long f0 = S.followers, m0 = S.money;
            if (bed != null)
            {
                var bc = bed.GetComponentInChildren<Collider>();
                yield return AimAt(bc != null ? bc.bounds.center : bed.transform.position + Vector3.up * 0.4f);
                Info("prompt at the bed: '" + InteractionPrompt.Text + "'");
                GameInput.SimTap(GameAction.Interact);
                yield return WaitUntil(() => S.day > day0, 7f);
                if (S.day <= day0) Info("E did not reach the bed from here – using it directly");
                if (S.day <= day0 && bed.CanInteract()) bed.Interact();
            }
            if (S.day <= day0) gm.SleepToMorning();
            yield return WaitUntil(() => S.day > day0, 8f);
            Check(S.day == day0 + 1, "slept until day " + S.day + " at " + gm.ClockString);
            Check(S.videos.Count == 1 && S.pendingUploads.Count == 0, "the upload resolved overnight");
            if (S.videos.Count > 0)
            {
                var v = S.videos[S.videos.Count - 1];
                Check(v.views > 0 && S.followers > f0, "video: " + v.views + " views, +" + v.followersGained + " followers, +" + v.money + " $ (now " + S.followers + " followers, " + S.money + " $)");
                Metric("upload.first", v.views + " views → +" + v.followersGained + " followers, +" + v.money + " $ (" + v.clipCount + " clips)");
                MetricEarlyEconomy(v.money, S.followers);
            }
            yield return Wait(2.5f);
            Check(S.questIndex >= TutorialQuests.Finished, "tutorial finished (step " + S.questIndex + ")");
            Check(FlatDist(pc.Root.position, spawn) < 4f, "woke up in the hut");
            yield return Shot("14b_morning");
        }

        /// K.1 check of the second player message: real minutes to the first 100 $ from videos and to the first camera
        /// upgrade. Projection from the measured first video + the measured bot fishing rate: one upload per in-game day
        /// (Progression.ExpectedUploadMoney/Followers) + 6 real minutes of shore fishing per day; one day = 40 real min.
        void MetricEarlyEconomy(long firstVideoMoney, long followersNow)
        {
            var cam1 = GearDatabase.Get("cam_t1");
            if (cam1 == null) return;
            float dayMin = GameManager.DayLengthSeconds / 60f;
            float fishPerDay = fishSecondsPerFish > 0f ? 6f * 60f / fishSecondsPerFish : 0f;
            float fishMoneyPerDay = fishPerDay * fishAvgValue;
            long videoMoney = firstVideoMoney, f = followersNow, money = videoMoney;   // VideoRecord.money includes the starter bonus
            int day100 = videoMoney >= 100 ? 1 : -1, dayCam = -1;
            for (int d = 2; d <= 30 && (day100 < 0 || dayCam < 0); d++)
            {
                long m = Progression.ExpectedUploadMoney(f);
                videoMoney += m;
                money += m + (long)fishMoneyPerDay;
                f += Progression.ExpectedUploadFollowers(f);
                if (day100 < 0 && videoMoney >= 100) day100 = d;
                if (dayCam < 0 && f >= cam1.followersNeeded && money >= cam1.price) dayCam = d;
            }
            Metric("economy.first100FromVideos", day100 > 0 ? "day " + day100 + " (≈ " + F(day100 * dayMin, "0") + " real min; first video " + firstVideoMoney + " $ incl. " + Progression.StarterBonus + " $ starter bonus)" : "> 30 days");
            Metric("economy.firstCameraUpgrade", dayCam > 0 ? "day " + dayCam + " (≈ " + F(dayCam * dayMin / 60f, "0.0") + " real h) for " + cam1.price + " $ / " + cam1.followersNeeded + " F – projection: 1 upload/day + 6 min fishing/day (" + F(fishMoneyPerDay, "0") + " $)" : "> 30 days");
        }

        // ================================================================== 15 collectible
        IEnumerator StepCollectible()
        {
            // L.6 hides many pages behind climbs, in dark corners or inside buildings: take the nearest island shell /
            // page that has a free standing spot with a clear line of sight (an easy one, like a player would)
            var cands = new List<PropPickup>();
            foreach (var p in Object.FindObjectsByType<PropPickup>(FindObjectsSortMode.None))
            {
                if (p == null || !p.Available()) continue;
                if (p.kind != CollectibleKind.GoldenShell && p.kind != CollectibleKind.LogbookPage) continue;
                Vector3 pp = p.transform.position;
                if (FlatDist(pp, WorldLayout.IslandCenter) > 230f || pp.y < Waves.SeaLevel + 0.2f) continue;
                cands.Add(p);
            }
            Vector3 from = pc.Root.position;
            cands.Sort((x, y) => (x.transform.position - from).sqrMagnitude.CompareTo((y.transform.position - from).sqrMagnitude));
            PropPickup pick = null;
            Vector3 target = Vector3.zero, feet = Vector3.zero;
            for (int ci = 0; ci < cands.Count && pick == null; ci++)
            {
                var p = cands[ci];
                Vector3 t = p.trigger != null ? p.trigger.bounds.center : p.transform.position;
                for (int k = 0; k < 24; k++)
                {
                    float a = k * 30f * Mathf.Deg2Rad;
                    float r = k < 12 ? 1.4f : 1.9f;
                    Vector3 c = t + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                    Vector3 f = SnapDown(c + Vector3.up * 2f, 2f, 6f);
                    if (f.y < Waves.SeaLevel + 0.1f || Mathf.Abs(f.y - t.y) > 1.6f) continue;
                    if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 eye = f + Vector3.up * PlayerController.EyeHeight;
                    Vector3 to = t - eye;
                    if (Physics.Raycast(eye, to.normalized, to.magnitude - 0.15f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    pick = p; target = t; feet = f;
                    break;
                }
            }
            Check(pick != null, "a reachable golden shell / logbook page on the island (" + cands.Count + " on the island)");
            if (pick == null) yield break;
            yield return Stand(feet, YawTo(feet, target));
            yield return AimAt(target);
            Info(pick.kind + " '" + pick.id + "' prompt: '" + InteractionPrompt.Text + "'");
            yield return Shot("15_collectible");
            int before = Collectibles.CountAll;
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.6f);
            if (!Collectibles.IsCollected(pick.id))
            {
                Check(false, "E did not pick it up (interactor) – collected directly");
                pick.Interact();
                yield return Wait(0.3f);
            }
            else Check(true, "E picks it up");
            Check(Collectibles.IsCollected(pick.id) && Collectibles.CountAll == before + 1,
                  "collected " + pick.id + " (" + Collectibles.Count(pick.kind) + "/" + Collectibles.Total(pick.kind) + " of this kind, " + Collectibles.TotalAll + " collectibles in total)");
        }

        // ================================================================== 16 settings
        IEnumerator StepSettings()
        {
            var d = Settings.Data;
            UIModule.DebugOpenPhone("settings");
            yield return Wait(1.0f);
            yield return Shot("16_settings_app");
            UIModule.DebugOpenPhone("close");

            d.master = 0.42f; d.music = 0.3f; d.sfx = 0.55f; d.voice = 0.66f;
            Settings.Apply();
            Check(Mathf.Abs(AudioListener.volume - 0.42f) < 0.01f, "master volume → AudioListener " + F(AudioListener.volume, "0.00"));
            Check(Mathf.Abs(Settings.MusicVolume - 0.3f) < 0.01f && Mathf.Abs(Settings.SfxVolume - 0.55f) < 0.01f && Mathf.Abs(Settings.VoiceVolume - 0.66f) < 0.01f, "music / sfx / voice volumes stored");

            d.fpsLimit = 30; d.vsync = false;
            Settings.Apply();
            Check(Application.targetFrameRate == 30 && QualitySettings.vSyncCount == 0, "FPS cap 30 (targetFrameRate " + Application.targetFrameRate + ", vSync " + QualitySettings.vSyncCount + ")");
            d.fpsLimit = 0;
            Settings.Apply();
            Check(Application.targetFrameRate == -1, "FPS unlimited");

            d.quality = GraphicsQuality.Low;
            Settings.Apply();
            yield return Wait(1.0f);
            Check(FXQuality.Tier == GraphicsQuality.Low, "graphics Low applied by FX (tier " + FXQuality.Tier + ", grass " + F(FXQuality.GrassDensity, "0.0") + ", caustics " + FXQuality.Caustics + ")");
            d.quality = GraphicsQuality.Medium;
            Settings.Apply();
            yield return Wait(0.5f);
            Check(FXQuality.Tier == GraphicsQuality.Medium, "graphics Medium applied");
            d.quality = GraphicsQuality.High;
            Settings.Apply();
            yield return Wait(0.5f);
            Check(FXQuality.Tier == GraphicsQuality.High, "graphics High applied");

            d.voiceMode = VoiceMode.Off; d.micMuted = false;
            Check(!Settings.MicLive, "voice off → mic not live");
            d.voiceMode = VoiceMode.OpenMic;
            Check(Settings.MicLive, "open mic → live");
            d.micMuted = true;
            Check(!Settings.MicLive, "mic mute → not live");
            d.micMuted = false;
            d.voiceMode = VoiceMode.PushToTalk;
            Check(!Settings.MicLive, "push-to-talk without the key → not live");
            GameInput.SimPress(GameAction.PushToTalk);
            yield return Frames(2);
            Check(Settings.MicLive, "push-to-talk with V held → live");
            GameInput.SimRelease(GameAction.PushToTalk);

            var orig = GameInput.GetBinding(GameAction.Interact);
            GameInput.SetBinding(GameAction.Interact, InputBinding.FromKey(UnityEngine.InputSystem.Key.G));
            Check(GameInput.GetBinding(GameAction.Interact).key == UnityEngine.InputSystem.Key.G && GameInput.BindingLabel(GameAction.Interact) == "G",
                  "Interact rebound to G (label '" + GameInput.BindingLabel(GameAction.Interact) + "')");
            GameInput.SetBinding(GameAction.Photo, InputBinding.FromMouse(2));
            Settings.Load();   // read it back from settings.json
            Check(GameInput.GetBinding(GameAction.Interact).key == UnityEngine.InputSystem.Key.G && GameInput.GetBinding(GameAction.Photo).mouseButton == 2,
                  "bindings persisted in settings.json (Photo = '" + GameInput.BindingLabel(GameAction.Photo) + "')");
            RestoreSettings();
            Check(GameInput.GetBinding(GameAction.Interact).Equals(orig), "player's own settings restored (Interact = '" + GameInput.BindingLabel(GameAction.Interact) + "')");
        }

        // ================================================================== 17 fps
        IEnumerator StepFps()
        {
            var d = Settings.Data;
            GraphicsQuality q0 = d.quality;
            Hands.StowAll();
            // island: at the land end of the dock, looking at the hut / jungle
            Vector3 start = WorldLayout.DockStart;
            Vector3 feet = SnapDown(new Vector3(start.x + 4f, 6f, start.z - 3f));
            yield return Stand(feet, YawTo(feet, WorldLayout.Hut));
            yield return AimAt(new Vector3(WorldLayout.Hut.x, feet.y + 4f, WorldLayout.Hut.z));
            SetTime(0.45f);
            foreach (var q in new[] { GraphicsQuality.High, GraphicsQuality.Low })
            {
                d.quality = q;
                Settings.Apply();
                yield return Wait(1.5f);
                yield return MeasureUncapped("island", 10f);
                yield return Shot("17_island_" + q.ToString().ToLowerInvariant());
            }
            // under water at the house reef
            var site = DiveSites.Get("house_reef");
            Vector3 anchor = PropInfo.AnemoneSpots.Count > 0 ? PropInfo.AnemoneSpots[0] : FindSeafloor(site.center, site.radius * 0.8f, -10.5f, -5.5f);
            Vector3 eye = KeepInWater(anchor + new Vector3(4f, 2.5f, 2f), 1.4f, 1.6f);
            yield return DiveTo(eye, anchor + Vector3.up * 0.6f);
            foreach (var q in new[] { GraphicsQuality.High, GraphicsQuality.Low })
            {
                d.quality = q;
                Settings.Apply();
                yield return Wait(1.5f);
                yield return MeasureUncapped("underwater", 10f);
                yield return Shot("17b_underwater_" + q.ToString().ToLowerInvariant());
            }
            d.quality = q0;
            Settings.Apply();
            pc.enabled = true;
            yield return Wait(0.5f);
            Check(report.fps.Count >= 5, report.fps.Count + " FPS measurements");
        }

        /// Measures FPS with the frame cap and vSync off (headroom), then restores the player's cap.
        IEnumerator MeasureUncapped(string scene, float seconds)
        {
            var d = Settings.Data;
            int f0 = d.fpsLimit;
            bool v0 = d.vsync;
            d.fpsLimit = 0;
            d.vsync = false;
            Settings.Apply();
            yield return Wait(1.0f);
            var dts = new List<float>(2048);
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt;
                dts.Add(dt);
            }
            d.fpsLimit = f0;
            d.vsync = v0;
            Settings.Apply();
            if (dts.Count == 0) yield break;
            float worst = 0f;
            for (int i = 0; i < dts.Count; i++) worst = Mathf.Max(worst, dts[i]);
            var sorted = new List<float>(dts);
            sorted.Sort();
            float p99 = sorted[Mathf.Clamp(Mathf.CeilToInt(sorted.Count * 0.99f) - 1, 0, sorted.Count - 1)];
            var r = new FpsResult
            {
                scene = scene,
                quality = FXQuality.Tier.ToString(),
                avg = dts.Count / Mathf.Max(0.001f, t),
                min = 1f / Mathf.Max(0.0001f, worst),
                low1 = 1f / Mathf.Max(0.0001f, p99),
                frames = dts.Count
            };
            report.fps.Add(r);
            Info("FPS " + scene + " (" + r.quality + ", uncapped): avg " + F(r.avg, "0") + ", 1% low " + F(r.low1, "0") + ", min " + F(r.min, "0") + " over " + r.frames + " frames");
        }

        // ================================================================== 18 save / load
        IEnumerator StepSaveLoad()
        {
            var gm = GameManager.I;
            long money = S.money, followers = S.followers;
            int day = S.day, discovered = Journal.DiscoveredCount, owned = S.ownedItems.Count, videos = S.videos.Count, collected = S.collected.Count, fishSold = S.fishSold;
            int photos = Gallery.Count;
            int seed = S.saveSeed;
            bool saved = false, loaded = false;
            // round 3 step 6b: saves are slots now – the test world is saved into slot 1 of a SANDBOX folder
            // (SaveSlots.RootOverride) and continued from there; the player's own slots are never touched
            string realSlots = RealSlotsStamp();
            EnterSlotSandbox();
            try
            {
                SaveSlots.SetActive(1);
                GameManager.SavingDisabled = false;
                gm.Save();
                string slotFile = SaveSlots.FileOf(1);
                saved = File.Exists(slotFile) && File.GetLastWriteTime(slotFile) > DateTime.Now.AddSeconds(-10);
            }
            finally { GameManager.SavingDisabled = true; }
            Check(saved, "game saved (sandbox slot 1)");
            loaded = gm.LoadGame();   // „Fortsetzen“ = the last used slot
            Check(SaveSlots.Active == 1 && SaveSlots.ContinueSlot == 1, "continue loads the last used slot (" + SaveSlots.Active + ")");
            LeaveSlotSandbox();       // back to the unsaved test world (its gallery = the photos taken so far)
            RestoreSave();
            Check(loaded, "continue: save loaded");
            Check(RealSlotsStamp() == realSlots, "the player's own save slots are untouched");
            yield return Wait(1.5f);
            Check(S.money == money && S.followers == followers && S.day == day, "money " + S.money + " $, followers " + S.followers + ", day " + S.day + " restored");
            Check(Journal.DiscoveredCount == discovered && S.ownedItems.Count == owned && S.videos.Count == videos, "journal (" + Journal.DiscoveredCount + "), items (" + S.ownedItems.Count + "), videos (" + S.videos.Count + ") restored");
            Check(S.collected.Count == collected && S.fishSold == fishSold && Gallery.Count == photos && S.saveSeed == seed, "collectibles, fish sold, gallery entries and save seed restored");
            Check(Hands.Active(Hand.Right) == HoldItem.None && Hands.Active(Hand.Left) == HoldItem.None, "hands empty after loading");
            Check(Mathf.Abs(S.timeOfDay - GameManager.MorningTime) < 0.01f && FlatDist(pc.Root.position, PlayerController.ResolveSpawn(out _)) < 4f, "continues in the morning in the hut");
            Check(File.Exists(SavePath) == saveExisted, "the player's own (old single) save file is untouched");
        }

        // ================================================================== 19 / 28 menu
        IEnumerator StepReturnToMenu()
        {
            var gm = GameManager.I;
            gm.ReturnToMenu();
            yield return Wait(3f);
            Check(UIState.MainMenuOpen && !gm.InGame, "main menu open again");
            Check(GameObject.Find("MenuCrew") != null, "crew on the dock behind the menu");
            yield return Shot(report.steps.Count < 20 ? "19_menu_again" : "28_menu_end");
        }

        // ================================================================== 20 late game
        IEnumerator StepLateNewGame()
        {
            var gm = GameManager.I;
            gm.NewGame(Difficulty.OpenWater);
            yield return Frames(3);
            yield return Wait(1.2f);
            pc = Refs.Player as PlayerController;
            cam = pc != null ? pc.View : null;
            gm.SetQuestIndex(TutorialQuests.Finished);
            milestoneEvents = 0;
            siteEvents = 0;
            sponsorEvents = 0;
            Check(gm.InGame && S.followers == 0 && S.unlockedSites.Count == 1, "fresh game: only the house reef unlocked");
            foreach (var site in DiveSites.All)
                if (site.prevSiteId != null || site.IsStorySite) Check(!S.unlockedSites.Contains(site.id), site.id + " still locked (" + site.followersNeeded + " followers + " + F(site.prevCompletionNeeded * 100f, "0") + " % of " + site.prevSiteId + ")");
        }

        // ================================================================== 21-25 dive sites
        /// The five progression sites (Sealog/follower unlocks). Story sites (the Fog Isle, K.8 – unlocked by Toni's map)
        /// are appended after them in DiveSites.All and are not part of this tour.
        static int ProgressionSites
        {
            get
            {
                int n = 0;
                for (int i = 0; i < DiveSites.All.Count; i++) if (!DiveSites.All[i].IsStorySite) n++;
                return n;
            }
        }

        static readonly string[][] SitePicks =
        {
            new[] { "green_turtle", "parrotfish" },
            new[] { "leopard_shark", "cuttlefish" },
            new[] { "grouper", "barracuda" },
            new[] { "sunfish", "manta" },
            new[] { "anglerfish", "vampire_squid" },
        };
        static readonly SizeTier[][] SiteTiers =
        {
            new[] { SizeTier.Large, SizeTier.Large },
            new[] { SizeTier.Normal, SizeTier.Large },
            new[] { SizeTier.Normal, SizeTier.Normal },
            new[] { SizeTier.Normal, SizeTier.Tiny },
            new[] { SizeTier.Huge, SizeTier.Large },
        };
        static readonly float[] FloorMin = { -11f, -20f, -36f, -48f, -96f };
        static readonly float[] FloorMax = { -6f, -12f, -22f, -28f, -82f };

        IEnumerator StepSite(int index)
        {
            var gm = GameManager.I;
            var site = DiveSites.All[index];
            SetTime(0.45f);
            pc.enabled = true;
            Check(S.unlockedSites.Contains(site.id), site.Name + " unlocked (" + S.followers + " followers)");

            // the tank for this site (followers gate + price)
            var tank = GearDatabase.Get("tank_t" + (index + 1));
            if (tank != null && !S.ownedItems.Contains(tank.id))
            {
                long price = Shop.Price(tank, false);
                if (S.money < price) gm.AddMoney(price - S.money + 500);
                Check(Shop.CanBuy(tank, false, out string why), "can buy " + tank.id + " (" + price + " $, needs " + tank.followersNeeded + " followers)" + (why != null ? " – " + why : ""));
                if (Shop.Buy(tank, false))
                {
                    var del = Shop.FindDelivery(tank.id);
                    if (del != null) Shop.Receive(del);
                }
                Check(S.ownedItems.Contains(tank.id) && Gear.IsEquipped(tank.id), tank.id + " owned + equipped (" + F(pc.Gear.airSeconds, "0") + " s, " + F(pc.Gear.maxDepth, "0") + " m)");
            }

            // boat to the site
            var boat = Refs.Boat as BoatController;
            Vector3 bp = FindWater(site.center, 4f);
            if (boat != null)
            {
                float yaw = YawTo(WorldLayout.IslandCenter, bp);
                boat.PlaceAt(bp, yaw);
                if (!boat.Anchored) boat.ToggleAnchor();
                yield return Wait(0.5f);
                pc.TeleportTo(boat.RescuePoint, yaw);
                yield return Wait(1.5f);
                Check(boat.PlayerAboard, "on the boat at the site");
                var at = WorldInfo.SiteAt(bp);
                Check(at != null && at.id == site.id, "boat is inside the dive site (" + (at != null ? at.id : "none") + ")");
            }

            // mini island
            Vector3 island = MiniIsland(site.id);
            if (island != Vector3.zero)
            {
                if (boat != null)
                {
                    yield return AimAt(new Vector3(island.x, Waves.SeaLevel + 6f, island.z));
                    yield return Shot("2" + (index + 1) + "_" + site.id + "_boat");
                }
                Vector3 stand = IslandStand(island);
                Check(stand != Vector3.zero, "mini island next to the site (" + V(island) + ")");
                if (stand != Vector3.zero)
                {
                    yield return Stand(stand, YawTo(stand, island));
                    Vector3 look = PropInfo.MiniLandmarks.TryGetValue(site.id, out var lm) ? lm : island;
                    if (FlatDist(look, pc.Root.position) < 3f) look = bp;
                    yield return AimAt(new Vector3(look.x, Mathf.Max(look.y, Waves.SeaLevel) + 2f, look.z));
                    Check(!pc.IsInWater, "standing on the mini island (ground " + F(pc.Root.position.y) + " m)");
                    yield return Shot("2" + (index + 1) + "b_" + site.id + "_island");
                }
            }
            else if (boat != null) yield return Shot("2" + (index + 1) + "_" + site.id + "_boat");

            // dive
            Vector3 center = site.id == "wreck" && PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : site.center;
            float rad = site.id == "wreck" ? 45f : site.radius * 0.8f;
            Vector3 floor = FindSeafloor(center, rad, FloorMin[index], FloorMax[index]);
            Vector3 eye = KeepInWater(floor + Vector3.up * 3.5f, 2.5f, 2f);
            if (!IsClear(eye)) eye = KeepInWater(floor + Vector3.up * 7f, 4f, 2f);
            if (floor.y < FloorMin[index] - 3f) eye = KeepInWater(new Vector3(floor.x, FloorMax[index], floor.z), 4f, 2f);   // no floor in the band: open water
            Info("dive spot " + V(eye) + " above floor " + F(floor.y) + " m");
            Vector3 dir = Flat(site.center - floor);
            if (dir.sqrMagnitude < 0.5f) dir = Vector3.forward;
            if (!Hands.IsHeld(HoldItem.Camera)) { if (Hands.Find(HoldItem.Camera, out Hand ch, out int cs)) Hands.Select(cs); }
            yield return DiveTo(eye, eye + dir * 10f + Vector3.down * 1.5f);
            if (index >= 2 && lamp != null) lamp.SetOn(true);
            Check(Hands.IsHeld(HoldItem.Camera), "camera in hand" + (index >= 2 ? " + lamp " + (pc.FlashlightOn ? "on" : "OFF") : ""));
            float vis = FXVisibility.Meters;
            Metric("site." + site.id, "eye depth " + F(Waves.SeaLevel - eye.y, "0") + " m, visibility " + F(vis, "0") + " m, scan range " + F(FilmShotEvaluator.ScanRange(), "0.0") + " m");
            yield return Wait(2.5f);
            int natural = 0, all = 0;
            // the spawner rolls species at random (open-water species share every site): give the site's own animals
            // up to 6 s more to show up before calling the site empty
            for (int round = 0; round < 4; round++)
            {
                natural = 0; all = 0;
                var reg = CreatureRegistry.Active;
                for (int i = 0; i < reg.Count; i++)
                {
                    var c = reg[i];
                    if (c == null || (c is Object o && o == null) || !c.IsAlive || c.IsHallucination || c.Species == null) continue;
                    if ((c.Position - eye).sqrMagnitude > 90f * 90f) continue;
                    all++;
                    if (c.Species.siteId == site.id && !mySpawns.Contains(c)) natural++;
                }
                if (natural > 0) break;
                yield return Wait(2f);
            }
            if (site.id == "kelp_forest")
            {
                int kelp = 0, kelpNear = 0, kelpOn = 0;
                foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                {
                    if (smr == null || smr.gameObject.name != "KelpCluster") continue;
                    kelp++;
                    if (smr.enabled) kelpOn++;
                    if (FlatDist(smr.transform.position, eye) < 45f) kelpNear++;
                }
                Check(kelp > 0 && kelpNear > 0, "kelp: " + kelp + " clusters active (" + kelpOn + " rendered), " + kelpNear + " within 45 m of the dive spot");
            }
            var sp = FaunaModule.Spawner;
            Check(natural > 0, "site fauna spawns by itself (" + natural + " of " + all + " animals nearby belong to this site; spawner site " + (sp != null && sp.CurrentSite != null ? sp.CurrentSite.id : "none") + ")");
            yield return Shot("2" + (index + 1) + "c_" + site.id + "_dive");

            // film two species with the real camera (aim + record)
            Vector3 fwd = Flat(cam.transform.forward);
            var picks = SitePicks[index];
            for (int k = 0; k < picks.Length; k++)
            {
                string id = picks[k];
                Vector3 e2 = cam.transform.position;
                var c = SpawnAt(id, OpenWaterSpot(e2, KeepInWater(e2 + fwd * 5f + Vector3.up * 0.3f, 1.5f, 1.5f)), SiteTiers[index][k]);
                if (c == null) { Check(false, "could not spawn " + id); continue; }
                int clipsBefore = Footage.DiveCount;
                yield return FollowFor(c, 5f, FilmDistance(c), 0f, true, true, k == 0 ? "2" + (index + 1) + "d_" + site.id + "_filming" : null);
                yield return StopFilming();
                // K.2: filming makes clips; the Sealog entry needs a photo (LMB while aiming)
                bool clipped = Footage.DiveCount > clipsBefore;
                // big animals: ≤ 4.5 m (at 7 m in 12 m deep-water visibility the manta photo did not register); one
                // retry like a player would take a second shot. The frame belongs to the test animal: passing schools
                // (open-water species are no longer site-gated) took the Sealog subject away from the barracuda.
                ClearAround(c, cam.transform.position, 9f);
                if (c.IsAlive && !Journal.IsDiscovered(id)) yield return PhotoOf(c, Mathf.Min(FilmDistance(c), 4.5f), null);
                if (c.IsAlive && !Journal.IsDiscovered(id)) { ClearAround(c, cam.transform.position, 9f); yield return Wait(0.7f); yield return PhotoOf(c, Mathf.Min(FilmDistance(c), 3.5f), null); }
                // a fast swimmer (barracuda) can leave the follow frame; the aimed photo pass records a clip of it too
                // (auto-record while aiming) – that is filming with the real camera as well
                if (!clipped && Footage.DiveCount > clipsBefore) Info(id + ": the clip came from the aimed photo pass, not the 5 s follow");
                clipped = clipped || Footage.DiveCount > clipsBefore;
                Check(clipped && Journal.IsDiscovered(id), "filmed (" + (Footage.DiveCount - clipsBefore) + " clip(s)) + photographed into the Sealog: " + id + " (" + F(c.SizeCm, "0") + " cm, " + photoNote + ", hints " + filmHints + ")");
                // the next species gets the frame to itself (the scan-priority case is covered in step 12)
                if (c.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(c);
            }
            if (lamp != null) lamp.SetOn(false);

            // complete the journal page (the remaining species via the journal API) → sponsor
            foreach (var s in SpeciesDatabase.ByPage(site.id))
            {
                if (Journal.IsDiscovered(s.id)) continue;
                float cm = Progression.RollSizeCm(s, SizeTier.Normal, rng);
                Journal.RegisterFilmed(s, cm, SizeTier.Normal, s.baseValue);
            }
            yield return Frames(2);
            Check(Journal.SiteCompletion(site.id) >= 0.999f && S.sponsorSites.Contains(site.id), "journal page complete → sponsor deal");
            pc.enabled = true;
            pc.RefillAir();

            // followers for the next site
            if (index + 1 < ProgressionSites)
            {
                var next = DiveSites.All[index + 1];
                int ev0 = milestoneEvents;
                yield return GrantFollowers(next.followersNeeded);
                Check(S.unlockedSites.Contains(next.id), "next site " + next.id + " unlocked at " + S.followers + " followers (" + (milestoneEvents - ev0) + " milestones on the way)");
            }
        }

        IEnumerator GrantFollowers(long target)
        {
            var gm = GameManager.I;
            foreach (var m in Progression.Milestones)
            {
                if (m.followers > target || S.followers >= m.followers) continue;
                gm.AddFollowers(m.followers - S.followers);
                yield return null;
                Check(S.reachedMilestones.Contains(m.id), "milestone " + m.id + " fired");
            }
            if (S.followers < target) gm.AddFollowers(target - S.followers);
            yield return Wait(0.3f);
        }

        // ================================================================== 26 finale
        IEnumerator StepFinale()
        {
            pc.enabled = true;
            Vector3 spawn = PlayerController.ResolveSpawn(out float yaw);
            yield return Stand(spawn, yaw);
            yield return GrantFollowers(Progression.FinaleFollowers);
            Check(S.finaleReached, "finale reached at " + S.followers + " followers");
            var ui = UIModule.Controller;
            yield return WaitUntil(() => ui != null && ui.Finale.IsOpen, 6f);
            Check(ui != null && ui.Finale.IsOpen, "finale page opens");
            yield return Wait(1.0f);
            yield return Shot("26_finale");
            Check(S.reachedMilestones.Count == Progression.Milestones.Count, "all " + S.reachedMilestones.Count + "/" + Progression.Milestones.Count + " milestones reached (" + milestoneEvents + " events this game)");
            Check(S.unlockedSites.Count == ProgressionSites, "all " + S.unlockedSites.Count + " progression dive sites unlocked (the Fog Isle needs Toni's map)");
            Check(S.sponsorSites.Count == ProgressionSites, "all " + S.sponsorSites.Count + " dive-site journal pages complete");
            if (ui != null && ui.Finale.IsOpen) ui.Finale.Close();
            yield return Wait(0.6f);
            Check(!UIState.PauseOpen, "keep playing after the finale");
        }

        // ================================================================== 27 buy everything
        IEnumerator StepBuyAll()
        {
            var gm = GameManager.I;
            pc.enabled = true;
            SetTime(0.4f);
            long need = 0;
            foreach (var g in GearDatabase.All) if (!g.comingSoon && Shop.IsListed(g) && !S.ownedItems.Contains(g.id)) need += Shop.Price(g, false);
            if (S.money < need) gm.AddMoney(need - S.money + 1000);
            int bought = 0;
            float latest = 0f;
            bool sameDay = true;
            var sb = new StringBuilder();
            foreach (var g in GearDatabase.All)
            {
                if (g.comingSoon || !Shop.IsListed(g) || S.ownedItems.Contains(g.id) || Shop.IsOrdered(g)) continue;
                bool ok = Shop.Buy(g, false);
                if (!ok) { Check(false, "could not buy " + g.id); continue; }
                bought++;
                var d = Shop.FindDelivery(g.id);
                if (d != null)
                {
                    if (d.arrivalDay != S.day) sameDay = false;
                    latest = Mathf.Max(latest, d.arrivalTime);
                    float h = (d.arrivalTime - S.timeOfDay) * 24f;
                    if (bought == 1) Check(h >= 1.9f && h <= 3.1f, "standard delivery (" + g.id + ") takes 2–3 in-game hours (" + F(h) + " h)");
                }
                sb.Append(g.id).Append(' ');
            }
            Check(bought > 0, "ordered " + bought + " items: " + sb);
            Check(sameDay, "all arrive the same day (latest " + GameManager.Clock(latest) + ")");
            int comingSoon = 0;
            foreach (var g in GearDatabase.All) if (g.comingSoon) comingSoon++;
            Info(comingSoon + " items are 'coming soon' (submarines) and cannot be bought");

            Vector3 pad = WorldInfo.HelipadPosition != Vector3.zero ? WorldInfo.HelipadPosition : WorldLayout.Helipad;
            Vector3 standAt = SnapDown(pad + new Vector3(9f, 3f, 9f));
            yield return Stand(standAt, YawTo(standAt, pad));
            S.timeOfDay = latest + 0.0015f;
            float tw = Time.realtimeSinceStartup;
            int opened = 0;
            bool shot = false;
            while (Time.realtimeSinceStartup - tw < 150f && S.deliveries.Count > 0)
            {
                foreach (var c in Object.FindObjectsByType<HeliCrate>(FindObjectsSortMode.None))
                {
                    if (c == null || !c.CanInteract()) continue;
                    if (!shot)
                    {
                        shot = true;
                        yield return AimAt(c.transform.position + Vector3.up * 0.4f);
                        yield return Shot("27_crates");
                    }
                    c.Interact();
                    opened++;
                }
                yield return Wait(0.5f);
            }
            Check(opened > 0, opened + " crates delivered by helicopter and opened in " + F(Time.realtimeSinceStartup - tw, "0") + " s");
            int direct = 0;
            for (int i = S.deliveries.Count - 1; i >= 0; i--)
            {
                Shop.Receive(S.deliveries[i]);
                direct++;
            }
            if (direct > 0) Info(direct + " deliveries still in the air after 150 s were received directly (one flight carries up to 4 crates)");
            int missing = 0;
            foreach (var g in GearDatabase.All) if (!g.comingSoon && Shop.IsListed(g) && !S.ownedItems.Contains(g.id)) missing++;
            Check(missing == 0, "every buyable item owned (" + missing + " missing)");
            foreach (var id in new[] { "tank_t5", "fins_t3", "lamp_t3", "cam_t4", "boat_t2", "rod_t2" }) Gear.Equip(id);
            Check(Mathf.Abs(pc.Gear.airSeconds - 220f) < 1f && Mathf.Abs(pc.Gear.maxDepth - 100f) < 0.5f && pc.Gear.swimSpeed > 2f,
                  "best gear equipped: " + F(pc.Gear.airSeconds, "0") + " s air, " + F(pc.Gear.maxDepth, "0") + " m, swim x" + F(pc.Gear.swimSpeed) + ", camera x" + F(pc.Gear.cameraQuality));
            Metric("economy.allGearPrice", need + " $");
        }

        // ================================================================== 27b core round-2 contracts
        /// Exercises the core facades of Design_Runde2 J.3/J.3b directly (no UI): the other modules' integration steps
        /// build on these rules.
        IEnumerator StepCoreContracts()
        {
            var gm = GameManager.I;
            var s = S;

            // (e) every slider value is repaired: NaN / ∞ / 1e9 / −5 → default or clamped
            var d = new SettingsData { master = float.NaN, music = 1e9f, sfx = -5f, voice = float.PositiveInfinity, mouseSensitivity = float.NaN, fov = 1e9f, voiceGateDb = float.NaN };
            Settings.Validate(d);
            Check(d.master == 1f && d.music == 1f && d.sfx == 0f && d.voice == 1f && d.mouseSensitivity == 1f && d.fov == Settings.MaxFov && d.voiceGateDb == Settings.DefaultVoiceGateDb,
                  "Settings.Validate: NaN/∞/1e9/−5 → master " + d.master + ", music " + d.music + ", sfx " + d.sfx + ", voice " + d.voice + ", sens " + d.mouseSensitivity + ", fov " + d.fov);
            Check(!string.IsNullOrEmpty(Settings.PlayerKey), "player key exists (F.3)");

            // K.1 economy
            Check(Mathf.Abs(Progression.StarterBoost(0) - 2.5f) < 0.001f && Mathf.Abs(Progression.StarterBoost(5000) - 1f) < 0.001f,
                  "starter boost ×" + F(Progression.StarterBoost(0)) + " at 0 → ×" + F(Progression.StarterBoost(5000)) + " at 5,000 followers");
            Check(GearDatabase.Get("tank_t1").price == 100 && Progression.MonetizationFollowers == 0, "first tank 100 $, FinTube pays from video 1");
            Metric("economy.expectedUpload", "1k: " + Progression.ExpectedUploadMoney(1000) + " $ / " + Progression.ExpectedUploadFollowers(1000) + " F; 100k: "
                   + Progression.ExpectedUploadMoney(100000) + " $ / " + Progression.ExpectedUploadFollowers(100000) + " F");
            Check(Progression.RankIndex(0) == 0 && Progression.RankIndex(1000000) == Progression.Ranks.Length - 1, "ranks Plankton → " + Progression.RankName(Progression.Ranks.Length - 1));

            // L.1 / L.2 data + Sealog photo and sightings (K.2)
            int[] pageCounts = new int[SealogPages.All.Length];
            var pageList = new List<SpeciesDef>();
            for (int i = 0; i < SealogPages.All.Length; i++) { SpeciesDatabase.ByPage(SealogPages.All[i].id, pageList); pageCounts[i] = pageList.Count; }
            // round 3 (fauna step): island animals are decoration (no page), + volcano page (7) + curiosities (10) + 8 new
            Check(SpeciesDatabase.Count == 70 && pageCounts[0] == 18, "Sealog 70 entries, pages " + string.Join("/", pageCounts));
            var wrasse = SpeciesDatabase.Get("cleaner_wrasse");
            var e0 = Journal.Find(wrasse.id);
            int ph0 = e0 != null ? e0.photos : 0, si0 = e0 != null ? e0.sightings : 0;
            Journal.RegisterPhoto(wrasse, 10f, SizeTier.Normal, 0.8f, "autoplaytest_photo", 6f, Zones.Bit(Zones.HouseReef));
            Journal.AddSightings(wrasse.id, 3, 4f, 9f, Zones.Bit(Zones.Lagoon));
            var e = Journal.Find(wrasse.id);
            Check(e != null && e.discovered && e.photos == ph0 + 1 && e.sightings == si0 + 4 && e.minDepthSeen <= 4.01f && e.maxDepthSeen >= 8.99f
                  && (e.zoneMask & Zones.Bit(Zones.Lagoon)) != 0 && e.bestPhotoId == "autoplaytest_photo",
                  "Sealog photo + sightings: photos " + (e != null ? e.photos : -1) + ", sightings " + (e != null ? e.sightings : -1) + ", depth " + (e != null ? F(e.minDepthSeen, "0") + "–" + F(e.maxDepthSeen, "0") : "-"));
            Check(wrasse.DepthWeight(8f, false) > 0.99f && wrasse.DepthWeight(1f, false) < 0.4f && wrasse.DepthWeight(60f, false) == 0f, "depth band weights 1.0 at pref, 0.35 at the edge, 0 outside");

            // K.7 sponsor deals: max 2, no topic overlap, cancel → sulk
            s.sponsor.offers.Clear();
            s.sponsor.active.Clear();
            var a = new DealState { dealId = "t_shark", sponsorId = "t_s1", tags = new[] { "shark" }, reward = 100, days = 2 };
            var b = new DealState { dealId = "t_noshark", sponsorId = "t_s2", tags = new[] { "turtle", "shark" }, reward = 500, days = 2 };
            var c = new DealState { dealId = "t_ray", sponsorId = "t_s3", tags = new[] { "ray" }, reward = 80, days = 2 };
            Deals.AddOffer(a); Deals.AddOffer(b); Deals.AddOffer(c);
            bool acceptA = Deals.Accept(a.id);
            Deals.CanAccept(b, out string why, out var conflict, out string tag);
            bool acceptB = Deals.Accept(b.id);
            bool acceptC = Deals.Accept(c.id);
            Check(acceptA && !acceptB && acceptC && conflict == a && tag == "shark", "deals: shark accepted, overlapping offer refused (" + why + "), ray accepted");
            long m0 = s.money;
            Deals.Complete(c.id);
            Check(s.money == m0 + 80, "deal done pays +80 $");
            Check(Deals.Cancel(a.id) && Deals.IsSulking("t_s1") && Deals.Active.Count == 0, "cancel → sponsor sulks " + Deals.SulkDays + " days");
            s.sponsor.offers.Clear();

            // K.8 Fog Isle stays locked until Toni's map
            var fog = DiveSites.Get("fog_isle");
            Check(fog != null && !Progression.IsSiteUnlocked(fog), "Fog Isle locked without the map");
            StoryFlags.Set(StoryKeys.FogMap);
            Check(Progression.IsSiteUnlocked(fog) && Progression.IsGateOpen(SpeciesDatabase.Get("coelacanth")), "fog_map → Fog Isle unlocked, its species gate open");

            // L.4 title rules
            Check(Progression.SanitizeTitle(new string('x', 80) + "\n\t").Length == Progression.MaxTitleLength, "titles cut to " + Progression.MaxTitleLength + " characters");

            // E food + buffs
            Pantry.Add("krill_bar", 3);
            bool ate = Pantry.TryEat("krill_bar");
            Check(ate && Pantry.Count("krill_bar") == 2 && Buffs.Multiplier(BuffType.SwimSpeed) > 1.19f, "pantry: ate a krill bar → swim ×" + F(Buffs.Multiplier(BuffType.SwimSpeed)));
            Buffs.Clear();

            // K.3 finds
            var finds = new List<FindSpawn>();
            Finds.Today("wreck", finds);
            Check(finds.Count == 7, "wreck finds today: " + finds.Count + " (5 small + 2 heavy)");
            if (finds.Count > 0)
            {
                bool took = Finds.Take(finds[0].id);
                long sold = Finds.SellAll();
                Check(took && sold == finds[0].value && Finds.IsTaken(finds[0].id), "find " + (finds[0].def != null ? finds[0].def.id : "?") + " dug up and sold for " + sold + " $");
            }

            // K.3 challenges + L.7 quest limit
            Check(Challenges.Today.Count == ChallengeDatabase.PerDay, "today's photo challenges: " + string.Join(", ", Challenges.Today));
            Quests.Start("t_q1"); Quests.Start("t_q2"); Quests.Start("t_q3");
            Check(!Quests.Start("t_q4") && Quests.Start(Quests.ToniChain), "max 3 side quests (+ Toni's chain)");
            // free the three slots again (step 39 starts a real side quest)
            Quests.Drop("t_q1"); Quests.Drop("t_q2"); Quests.Drop("t_q3");   // (Toni's chain is a story chain: it stays)
            yield return null;
            gm.Save();
        }

        // ================================================================== filming helpers
        float FilmDistance(ICreature c) => Mathf.Clamp(c.SizeCm / 100f * 2.2f, 1.4f, 7f);

        /// Moves a spawn point up (in 0.75 m steps, ≤ 6 m) until it is free of geometry and visible from the eye –
        /// at the wreck the test animals were spawned inside the hull and could never be photographed.
        static Vector3 OpenWaterSpot(Vector3 eye, Vector3 p)
        {
            for (int i = 0; i < 9; i++)
            {
                Vector3 q = p + Vector3.up * (0.75f * i);
                if (q.y > Waves.SeaLevel - 1.2f) break;
                Vector3 to = q - eye;
                bool blocked = Physics.CheckSphere(q, 1.0f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                               || Physics.Raycast(eye, to.normalized, Mathf.Max(0f, to.magnitude - 1.0f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (!blocked) return q;
            }
            return p;
        }

        /// Keeps the (frozen) player's camera at `dist` from the animal (fixed approach direction, yaw offset in
        /// degrees), with Aim held / recording as requested, for `seconds`. Results in film* fields.
        IEnumerator FollowFor(ICreature c, float seconds, float dist, float yawOff, bool aim, bool record, string shotName = null)
        {
            var hc = Refs.HandCamera as FilmHandCamera;
            filmTargeted = false;
            filmRegistered = false;
            filmLost = false;
            filmClips = 0;
            filmRegisterTime = -1f;
            filmLastHint = FilmHint.None;
            filmHints = "";
            if (hc == null || c == null || cam == null) yield break;
            string id = c.Species != null ? c.Species.id : "";
            bool disc0 = Journal.IsDiscovered(id);
            int clips0 = Footage.DiveCount;
            Vector3 approach = Flat(c.Position - cam.transform.position);
            // K.2: LMB while aiming takes a photo – toggle the recording first, then raise the viewfinder
            if (hc.IsRecording != record)
            {
                GameInput.SimRelease(GameAction.Aim);
                yield return null;
                if (record) GameInput.SimTap(GameAction.Use);
                else hc.StopRecording();
                yield return null;
                yield return null;
            }
            if (aim) GameInput.SimPress(GameAction.Aim);
            else GameInput.SimRelease(GameAction.Aim);
            float t = 0f;
            bool shot = false;
            var seen = new HashSet<FilmHint>();
            while (t < seconds)
            {
                if (!c.IsAlive) { filmLost = true; break; }
                PoseOn(c, approach, dist, yawOff);
                if (ReferenceEquals(hc.CurrentCreature, c)) filmTargeted = true;
                filmLastHint = hc.Hint;
                if (hc.Hint != FilmHint.None && seen.Add(hc.Hint)) filmHints += (filmHints.Length > 0 ? "/" : "") + hc.Hint;
                if (!disc0 && filmRegisterTime < 0f && Journal.IsDiscovered(id)) filmRegisterTime = t;
                if (shotName != null && !shot && t > seconds * 0.55f)
                {
                    shot = true;
                    yield return Shot(shotName);
                }
                yield return null;
                t += Time.deltaTime;
            }
            filmClips = Footage.DiveCount - clips0;
            filmRegistered = !disc0 && Journal.IsDiscovered(id);
            if (filmLost) filmHints += (filmHints.Length > 0 ? "/" : "") + "LOST";
        }

        IEnumerator StopFilming()
        {
            var hc = Refs.HandCamera as FilmHandCamera;
            GameInput.SimRelease(GameAction.Aim);
            // API stop: a simulated LMB under water would suppress the K.2 auto-record for the rest of the dive
            if (hc != null && hc.IsRecording) hc.StopRecording();
            yield return null;
            yield return null;
            yield return null;
        }

        void PoseOn(ICreature c, Vector3 approach, float dist, float yawOff)
        {
            Vector3 center = c.WorldBounds.center;
            Vector3 eye = KeepInWater(center - approach * dist, 0.6f, 0.7f);
            Vector3 d = center - eye;
            if (d.sqrMagnitude < 1e-4f) d = approach;
            Quaternion r = Quaternion.LookRotation(d.normalized, Vector3.up) * Quaternion.Euler(0f, yawOff, 0f);
            cam.transform.SetPositionAndRotation(eye, r);
        }

        /// Despawns every other animal within r of p (test frames: the Sealog subject must be the test animal).
        void ClearAround(ICreature keep, Vector3 p, float r)
        {
            var sp = Refs.Spawner;
            if (sp == null) return;
            var reg = CreatureRegistry.Active;
            var drop = new List<ICreature>();
            for (int i = 0; i < reg.Count; i++)
            {
                var c = reg[i];
                if (c == null || (c is Object o && o == null) || ReferenceEquals(c, keep) || !c.IsAlive) continue;
                if ((c.Position - p).sqrMagnitude < r * r) drop.Add(c);
            }
            for (int i = 0; i < drop.Count; i++) sp.Despawn(drop[i]);
        }

        ICreature SpawnAt(string id, Vector3 pos, SizeTier tier)
        {
            var spawner = Refs.Spawner;
            var s = SpeciesDatabase.Get(id);
            if (spawner == null || s == null) return null;
            try
            {
                float cm = Progression.RollSizeCm(s, tier, rng);
                var c = spawner.Spawn(s, pos, cm, false);
                if (c != null) mySpawns.Add(c);
                return c;
            }
            catch (Exception e)
            {
                Check(false, "spawning " + id + " threw " + e.Message);
                return null;
            }
        }

        void ClearCrowd()
        {
            var sp = FaunaModule.Spawner;
            if (sp != null && sp.ActiveCount > 110) sp.DespawnAll();
        }

        /// Which item models are visible under the player camera right now.
        void HeldModels(out bool rod, out bool camModel, out bool lampModel)
        {
            rod = camModel = lampModel = false;
            if (cam == null) return;
            foreach (var t in cam.GetComponentsInChildren<Transform>(false))
            {
                string n = t.name;
                if (n == "RodViewRoot" || n == "FallbackRod") rod = true;
                else if (n.StartsWith("HandCamera_T", StringComparison.Ordinal)) camModel = true;
                else if (n == "Lamp" && t.childCount > 0 && t.parent != null && t.parent.name == "Geo") lampModel = true;
            }
        }

        // ------------------------------------------------------------------ player / camera helpers
        void SetTime(float t)
        {
            var gm = GameManager.I;
            if (gm != null) gm.State.timeOfDay = t;
        }

        IEnumerator Stand(Vector3 feet, float yaw)
        {
            if (pc == null) yield break;
            pc.enabled = true;
            pc.TeleportTo(feet, yaw);
            yield return Wait(0.7f);
        }

        /// Teleports the diver so the eye is at the point, lets the controller settle into dive mode, then freezes
        /// it and aims the camera (same as AutoShot).
        IEnumerator DiveTo(Vector3 eye, Vector3 lookAt)
        {
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
            if (pc == null) yield break;
            pc.enabled = true;
            Vector3 d = lookAt - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pc.TeleportTo(eye - Vector3.up * PlayerController.EyeHeight, yaw);
            pc.RefillAir();
            yield return Wait(0.6f);
            pc.RefillAir();
            pc.enabled = false;
            Pose(eye, lookAt);
            yield return Wait(1.0f);
        }

        void Pose(Vector3 eye, Vector3 lookAt)
        {
            if (cam == null) return;
            Vector3 d = lookAt - eye;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d.normalized, Vector3.up));
        }

        /// Turns the (enabled) player's view toward a point through the simulated mouse look.
        IEnumerator AimAt(Vector3 target)
        {
            if (pc == null || cam == null) yield break;
            pc.enabled = true;
            for (int i = 0; i < 45; i++)
            {
                Transform ct = cam.transform;
                Vector3 d = target - ct.position;
                if (d.sqrMagnitude < 1e-4f) yield break;
                d.Normalize();
                Vector3 f = ct.forward;
                float yawT = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, yawC = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                float pitchT = -Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)) * Mathf.Rad2Deg, pitchC = -Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg;
                float dy = Mathf.DeltaAngle(yawC, yawT), dp = pitchT - pitchC;
                if (Mathf.Abs(dy) < 1.2f && Mathf.Abs(dp) < 1.2f) break;
                float scale = Mathf.Clamp(cam.fieldOfView / PlayerController.DefaultFov, 0.15f, 1.25f);
                GameInput.SimLook(new Vector2(dy, -dp) / scale);
                yield return null;
                yield return null;
            }
            yield return Wait(0.25f);
        }

        Vector3 DockEnd()
        {
            Vector3 e = WorldInfo.DockEndPosition;
            if (e == Vector3.zero) e = WorldLayout.DockEnd + Vector3.up * PropInfo.DockDeckY;
            return e;
        }

        static Vector3 SnapDown(Vector3 p, float from = 3f, float range = 14f)
        {
            if (Physics.Raycast(p + Vector3.up * from, Vector3.down, out RaycastHit hit, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;
            return new Vector3(p.x, Mathf.Max(WorldInfo.GroundHeight(p), Waves.SeaLevel) + 0.05f, p.z);
        }

        static float YawTo(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        static float FlatDist(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static string V(Vector3 v) => "(" + F(v.x, "0") + ", " + F(v.y, "0.0") + ", " + F(v.z, "0") + ")";

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        static Vector3 Ground(Vector3 p) => new Vector3(p.x, WorldInfo.GroundHeight(p), p.z);

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

        /// Open water at least `depth` deep near a point (spiral search).
        static Vector3 FindWater(Vector3 center, float depth)
        {
            for (float r = 0f; r <= 200f; r += 5f)
            {
                int steps = r < 1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r / 3f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    var p = new Vector3(center.x + Mathf.Cos(a) * r, Waves.SeaLevel, center.z + Mathf.Sin(a) * r);
                    if (WorldInfo.GroundHeight(p) < Waves.SeaLevel - depth) return p;
                }
            }
            return new Vector3(center.x, Waves.SeaLevel, center.z);
        }

        static Vector3 MiniIsland(string siteId)
        {
            switch (siteId)
            {
                case "kelp_forest": return WorldLayout.KelpIsland;
                case "wreck": return WorldLayout.WreckIsland;
                case "blue_drop": return WorldLayout.BlueDropIsland;
                case "deep_trench": return WorldLayout.TrenchIsland;
                default: return Vector3.zero;
            }
        }

        /// Dry, free standing spot on a mini island (rings around its center).
        static Vector3 IslandStand(Vector3 c)
        {
            for (float r = 6f; r <= WorldLayout.MiniIslandRadius; r += 4f)
            {
                for (int k = 0; k < 12; k++)
                {
                    float a = k * 30f * Mathf.Deg2Rad;
                    Vector3 p = new Vector3(c.x + Mathf.Cos(a) * r, 0f, c.z + Mathf.Sin(a) * r);
                    float g = WorldInfo.GroundHeight(p);
                    if (g < Waves.SeaLevel + 0.6f || g > Waves.SeaLevel + 35f) continue;
                    Vector3 f = SnapDown(new Vector3(p.x, g + 1f, p.z), 6f, 12f);
                    if (f.y < Waves.SeaLevel + 0.5f) continue;
                    if (Physics.CheckCapsule(f + Vector3.up * 0.45f, f + Vector3.up * 1.5f, 0.32f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    return f;
                }
            }
            return Vector3.zero;
        }

        static bool IsClear(Vector3 p) =>
            !Physics.CheckSphere(p, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && p.y > WorldInfo.GroundHeight(p) + 1.5f;

        static Vector3 KeepInWater(Vector3 p, float aboveFloor, float belowSurface)
        {
            float g = WorldInfo.GroundHeight(p);
            float top = Waves.SeaLevel - belowSurface;
            p.y = Mathf.Max(p.y, g + aboveFloor);
            if (p.y > top) p.y = top;
            return p;
        }

        static HeliCrate FindCrate(string itemId, bool interactable)
        {
            foreach (var c in Object.FindObjectsByType<HeliCrate>(FindObjectsSortMode.None))
            {
                if (c == null || c.Delivery == null || c.Delivery.itemId != itemId) continue;
                if (interactable && !c.CanInteract()) continue;
                return c;
            }
            return null;
        }

        // ------------------------------------------------------------------ capture / timing
        IEnumerator Shot(string name)
        {
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                lastLuma = Luma(tex);
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                report.screenshots++;
                if (cur != null) cur.screenshots += (cur.screenshots.Length > 0 ? ", " : "") + name + ".png";
                Note("captured " + name + ".png (mean brightness " + F(lastLuma, "0.00") + ")");
            }
            catch (Exception e)
            {
                Note("capture " + name + " failed: " + e.Message);
            }
            finally
            {
                if (tex != null) Destroy(tex);
            }
        }

        static float Luma(Texture2D t)
        {
            if (t == null) return -1f;
            var px = t.GetPixels32();
            double sum = 0;
            int n = 0;
            for (int i = 0; i < px.Length; i += 17)
            {
                var c = px[i];
                sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                n++;
            }
            return n > 0 ? (float)(sum / n / 255.0) : -1f;
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

        static IEnumerator WaitUntil(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        // ------------------------------------------------------------------ the player's files
        void BackupFiles()
        {
            try
            {
                settingsExisted = File.Exists(SettingsPath);
                settingsBackup = settingsExisted ? File.ReadAllText(SettingsPath) : null;
                saveExisted = File.Exists(SavePath);
                saveBackup = saveExisted ? File.ReadAllText(SavePath) : null;
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytest: backup failed: " + e.Message); }
        }

        void RestoreSettings()
        {
            try
            {
                if (settingsExisted) File.WriteAllText(SettingsPath, settingsBackup);
                else if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytest: could not restore settings: " + e.Message); }
            Settings.Load();
            Settings.Apply();
        }

        void RestoreSave()
        {
            try
            {
                if (saveExisted) File.WriteAllText(SavePath, saveBackup);
                else if (File.Exists(SavePath)) File.Delete(SavePath);
                if (File.Exists(SaveSidecar)) File.Delete(SaveSidecar);
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytest: could not restore the save: " + e.Message); }
        }

        void DeleteTestPhotos()
        {
            int n = 0;
            foreach (var f in testPhotos)
            {
                try
                {
                    string p = Path.IsPathRooted(f) ? f : Path.Combine(Gallery.Folder, f);
                    if (File.Exists(p)) { File.Delete(p); n++; }
                }
                catch (Exception) { }
            }
            Note("deleted " + n + " test photos from the gallery folder");
        }

        // ------------------------------------------------------------------ report
        void WriteReport()
        {
            report.passed = 0;
            report.failed = 0;
            report.skipped = 0;
            foreach (var s in report.steps)
            {
                if (s.skipped) report.skipped++;
                else if (s.pass) report.passed++;
                else report.failed++;
            }
            report.loggedErrors = errorCount;
            report.loggedExceptions = exceptionCount;
            report.totalSeconds = Time.realtimeSinceStartup - startTime;
            try
            {
                File.WriteAllText(Path.Combine(outDir, "playtest.json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(outDir, "playtest_report.md"), Markdown());
                File.WriteAllText(Path.Combine(outDir, "playtest_log.txt"), log.ToString());
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytest: could not write the report: " + e.Message); }
        }

        string Markdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SEALEBRITIES – automated single-player playtest");
            sb.AppendLine();
            sb.AppendLine("- Started: " + report.started + (report.finished != null ? " · finished: " + report.finished : " · (running)"));
            sb.AppendLine("- Build: " + report.version + " · " + report.resolution + " · " + report.device);
            sb.AppendLine("- Boot: " + F(report.bootSeconds) + " s" + (report.bootErrors ? " (with errors)" : ""));
            sb.AppendLine("- Result: **" + report.passed + " passed, " + report.failed + " failed, " + report.skipped + " skipped** · " +
                          report.loggedErrors + " logged errors (" + report.loggedExceptions + " exceptions) · " + report.screenshots + " screenshots · " + F(report.totalSeconds / 60f) + " min");
            sb.AppendLine();
            sb.AppendLine("| Step | Result | Time | Errors | Screenshots |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var s in report.steps)
                sb.AppendLine("| " + s.name + " | " + (s.skipped ? "SKIPPED" : s.pass ? "PASS" : "**FAIL**") + " | " + F(s.seconds, "0") + " s | " + s.errors + " | " + s.screenshots + " |");
            sb.AppendLine();
            if (report.fps.Count > 0)
            {
                sb.AppendLine("## FPS (frame cap and vSync off)");
                sb.AppendLine();
                sb.AppendLine("| Scene | Quality | Avg | 1% low | Min | Frames |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var f in report.fps)
                    sb.AppendLine("| " + f.scene + " | " + f.quality + " | " + F(f.avg, "0") + " | " + F(f.low1, "0") + " | " + F(f.min, "0") + " | " + f.frames + " |");
                sb.AppendLine();
            }
            if (report.metrics.Count > 0)
            {
                sb.AppendLine("## Measurements");
                sb.AppendLine();
                foreach (var m in report.metrics) sb.AppendLine("- " + m);
                sb.AppendLine();
            }
            sb.AppendLine("## Step details");
            foreach (var s in report.steps)
            {
                sb.AppendLine();
                sb.AppendLine("### " + s.name + " – " + (s.skipped ? "SKIPPED" : s.pass ? "PASS" : "FAIL"));
                sb.AppendLine("```");
                sb.AppendLine(s.details);
                sb.AppendLine("```");
            }
            if (report.errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Logged errors / exceptions (unique, first 80)");
                sb.AppendLine("```");
                foreach (var e in report.errors) sb.AppendLine(e);
                sb.AppendLine("```");
            }
            return sb.ToString();
        }

        void Finish(int code)
        {
            GameInput.SimClear();
            LeaveSlotSandbox();   // round 3 step 6b: never leave the slot sandbox switched on
            RestoreSettings();
            RestoreSave();
            DeleteTestPhotos();
            DeleteTestThumbs();
            report.finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            Note("done: " + report.passed + " passed, " + report.failed + " failed, " + report.skipped + " skipped, " + errorCount + " logged errors");
            WriteReport();
            if (pc != null) pc.enabled = true;
            Application.Quit(code);
        }
    }
}
