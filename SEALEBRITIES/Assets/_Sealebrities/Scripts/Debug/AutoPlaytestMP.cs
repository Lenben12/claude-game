using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Automated CO-OP playtest with two game windows on one PC. Does nothing unless the game is started with
    ///     SEALEBRITIES.exe -autoplaytest-mp host   "C:/some/output/folder" [-netbind 127.0.0.1] [-netport 27015] [-netname Anna]
    ///     SEALEBRITIES.exe -autoplaytest-mp client "C:/some/output/folder" [-netport 27015] [-netname Ben]
    /// The host opens a session on 127.0.0.1 (no firewall prompt), starts a new game and waits up to 3 min for the client;
    /// the client joins 127.0.0.1. Both sides then run the same scripted steps, synchronized through their own test
    /// messages on the co-op connection (NetBus type 200): avatars + nameplates, fishing + fish market, shop + helicopter
    /// crate, boat (driver, deck, seat), filming a host creature as a proxy, banking + upload, sleeping, collectibles,
    /// voice (synthetic tone instead of the microphone), FPS + bandwidth, disconnect, drop-in rejoin and the host quitting.
    /// Every step is timed, asserted and screenshotted; the results go to <folder>/mp_host_report.md + mp_host.json
    /// (mp_client_… on the client), written after every step. Saving is disabled on both sides; the host backs up and
    /// restores settings.json and the save.
    public sealed class AutoPlaytestMP : MonoBehaviour
    {
        const byte MsgType = 200;          // NetBus range 160+ is free
        const float BootTimeout = 900f;
        const float JoinTimeout = 180f;
        const float TimeBudget = 22f * 60f;
        const string HostIp = "127.0.0.1";

        // ------------------------------------------------------------------ report data
        [Serializable]
        public class StepResult
        {
            public string name;
            public bool pass = true;
            public bool skipped;
            public float seconds;
            public int errors;
            public float fps;
            public float kbInAvg, kbOutAvg, kbPeak;
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
            public string role;
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
            public string transport;
            public long bytesInTotal, bytesOutTotal;
            public List<StepResult> steps = new List<StepResult>();
            public List<FpsResult> fps = new List<FpsResult>();
            public List<string> metrics = new List<string>();
            public List<string> errors = new List<string>();
        }

        // ------------------------------------------------------------------ state
        bool isHost;
        string outDir;
        int port = Net.DefaultPort;
        string Role => isHost ? "host" : "client";
        readonly Report report = new Report();
        readonly StringBuilder log = new StringBuilder();
        readonly System.Random rng = new System.Random(777);
        StepResult cur;
        float startTime;
        int errorCount, exceptionCount;

        // per-step counters
        int curFrames;
        float curTime, bwTimer, bwIn, bwOut, bwPeak;
        int bwN;

        PlayerController pc;
        Camera cam;

        // co-op test channel
        readonly Dictionary<string, string> inbox = new Dictionary<string, string>();
        int peerId = -1;
        int myStep, peerStep;
        bool syncOk;

        // events seen
        int speciesEvents, leftEvents, joinedEvents, sleepEvents;
        string sessionEndReason;
        bool sessionEnded;
        readonly HashSet<string> toastsSeen = new HashSet<string>();
        readonly List<string> testPhotos = new List<string>();   // gallery PNGs taken by this run (deleted at the end)

        // backups (host only)
        bool settingsExisted, saveExisted;
        string settingsBackup, saveBackup;
        VoiceMode voiceMode0;
        bool voiceModeChanged;

        // filming results
        bool filmTargeted, filmRegistered;
        float filmRegisterTime;
        string filmHints = "";

        static GameState S => GameManager.I.State;
        static string SettingsPath => Path.Combine(Application.persistentDataPath, "settings.json");
        static string SavePath => Path.Combine(Application.persistentDataPath, "sealebrities_save.json");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ------------------------------------------------------------------ activation (command line only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Activate()
        {
            var args = Environment.GetCommandLineArgs();
            if (!ParseArgs(args, out bool host, out string dir, out int p)) return;
            GameManager.SavingDisabled = true;     // never touch the player's save (both windows)
            GameManager.AutomatedRun = true;
            Application.runInBackground = true;    // both windows keep running while the other one has the focus
            var go = new GameObject("SB_AutoPlaytestMP");
            DontDestroyOnLoad(go);
            var t = go.AddComponent<AutoPlaytestMP>();
            t.isHost = host;
            t.outDir = dir;
            t.port = p;
            if (host) t.BackupFiles();
        }

        static bool ParseArgs(string[] args, out bool host, out string dir, out int p)
        {
            host = false;
            dir = null;
            p = Net.DefaultPort;
            if (args == null) return false;
            bool found = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                if (string.Equals(a, "-netport", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length
                    && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pp) && pp > 0 && pp < 65536)
                    p = pp;
                if (!string.Equals(a, "-autoplaytest-mp", StringComparison.OrdinalIgnoreCase)) continue;
                string role = i + 1 < args.Length ? (args[i + 1] ?? "").Trim().ToLowerInvariant() : "";
                string folder = i + 2 < args.Length ? args[i + 2] : null;
                if ((role != "host" && role != "client") || string.IsNullOrWhiteSpace(folder) || folder.StartsWith("-", StringComparison.Ordinal))
                {
                    Debug.LogError("AutoPlaytestMP: usage -autoplaytest-mp host|client <output folder>");
                    return false;
                }
                host = role == "host";
                dir = folder.Trim().Trim('"');
                found = true;
            }
            return found;
        }

        void OnEnable()
        {
            Application.logMessageReceived += OnLog;
            GameEvents.SpeciesDiscovered += OnSpecies;
            GameEvents.SleepStarted += OnSleep;
            GameEvents.Toast += OnToast;
            Net.PlayerLeft += OnPlayerLeft;
            Net.PlayerJoined += OnPlayerJoined;
            Net.SessionEnded += OnSessionEnded;
            Gallery.Added += OnPhotoAdded;
        }

        void OnPhotoAdded(PhotoEntry e) { if (e != null && !string.IsNullOrEmpty(e.file)) testPhotos.Add(e.file); }

        /// Both roles: the discovery auto-photos / photos of this run never stay in the player's gallery folder.
        void DeleteTestPhotos()
        {
            int n = 0;
            for (int i = 0; i < testPhotos.Count; i++)
            {
                try
                {
                    string p = Path.Combine(Gallery.Folder, testPhotos[i]);
                    if (File.Exists(p)) { File.Delete(p); n++; }
                }
                catch (Exception) { }
            }
            testPhotos.Clear();
            Note("deleted " + n + " test photos from the gallery folder");
        }

        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            GameEvents.SpeciesDiscovered -= OnSpecies;
            GameEvents.SleepStarted -= OnSleep;
            GameEvents.Toast -= OnToast;
            Net.PlayerLeft -= OnPlayerLeft;
            Net.PlayerJoined -= OnPlayerJoined;
            Net.SessionEnded -= OnSessionEnded;
            Gallery.Added -= OnPhotoAdded;
            NetBus.Unregister(MsgType);
        }

        void OnSpecies(SpeciesDef d, float cm, SizeTier t) => speciesEvents++;
        void OnSleep() => sleepEvents++;
        void OnPlayerLeft(NetPlayerInfo p) { if (p != null && !p.IsLocal) leftEvents++; }
        void OnPlayerJoined(NetPlayerInfo p) { if (p != null && !p.IsLocal) joinedEvents++; }

        void OnSessionEnded(string reason)
        {
            sessionEnded = true;
            sessionEndReason = reason ?? "";
        }

        void OnToast(string text)
        {
            if (!string.IsNullOrEmpty(text) && toastsSeen.Count < 200) toastsSeen.Add(text);
        }

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

        // ------------------------------------------------------------------ co-op test channel
        void OnMsg(int from, byte[] payload)
        {
            if (payload == null) return;
            string s;
            try { s = Encoding.UTF8.GetString(payload); }
            catch (Exception) { return; }
            int nl = s.IndexOf('\n');
            string k = nl >= 0 ? s.Substring(0, nl) : s;
            string v = nl >= 0 ? s.Substring(nl + 1) : "";
            if (isHost && from > 0 && from != Net.LocalId) peerId = from;
            if (k == "step")
            {
                if (int.TryParse(v, NumberStyles.Integer, Inv, out int st) && st > peerStep) peerStep = st;
                return;
            }
            inbox[k] = v;
        }

        void Send(string key, string value = "")
        {
            if (!Net.IsOnline) return;
            if (isHost && (peerId < 0 || Net.Find(peerId) == null)) return;
            var b = Encoding.UTF8.GetBytes(key + "\n" + (value ?? ""));
            NetBus.Send(MsgType, b, isHost ? peerId : NetBus.Everyone, true);
        }

        void SendVal(string key, string value) => Send("v:" + key, value);
        string Val(string key) => inbox.TryGetValue("v:" + key, out var v) ? v : null;

        bool PeerGone
        {
            get
            {
                if (isHost) return peerId < 0 || Net.Find(peerId) == null;
                return !Net.IsClient || Net.Find(0) == null;
            }
        }

        bool PeerAhead => peerStep > myStep;

        /// Both sides wait for each other here (the marker is re-sent until the peer's arrives).
        IEnumerator Sync(string name, float timeout = 90f)
        {
            syncOk = false;
            string k = "at:" + name;
            float t = 0f, resend = 0f;
            while (t < timeout)
            {
                if (inbox.ContainsKey(k)) { syncOk = true; break; }
                if (PeerGone && t > 3f) break;
                if (PeerAhead) break;
                if (resend <= 0f)
                {
                    Send(k);
                    resend = 1.5f;
                }
                float dt = Time.unscaledDeltaTime;
                resend -= dt;
                t += dt;
                yield return null;
            }
            Send(k);
            if (!syncOk)
                Check(false, "peer did not reach sync point '" + name + "' (" + (PeerGone ? "peer gone" : PeerAhead ? "peer already in a later step" : "timeout " + F(timeout, "0") + " s") + ")");
        }

        IEnumerator WaitVal(string key, float timeout)
        {
            string k = "v:" + key;
            float t = 0f;
            while (!inbox.ContainsKey(k) && t < timeout)
            {
                if (PeerGone && t > 3f) break;
                if (PeerAhead) break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!inbox.ContainsKey(k)) Info("no '" + key + "' from the peer after " + F(t, "0") + " s" + (PeerGone ? " (peer gone)" : PeerAhead ? " (peer moved on)" : ""));
        }

        IEnumerator WaitPeer(Func<bool> cond, float timeout)
        {
            float t = 0f;
            while (!cond() && t < timeout)
            {
                if (PeerGone && t > 3f) yield break;
                if (PeerAhead) yield break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        NetAvatar PeerAvatar() => peerId >= 0 && NetAvatarSync.TryGet(peerId, out var a) ? a : null;

        int FindPeerId()
        {
            for (int i = 0; i < Net.Players.Count; i++)
                if (Net.Players[i] != null && !Net.Players[i].IsLocal) return Net.Players[i].id;
            return -1;
        }

        // ------------------------------------------------------------------ frame counters
        void Update()
        {
            if (cur == null) return;
            float dt = Time.unscaledDeltaTime;
            curFrames++;
            curTime += dt;
            bwTimer += dt;
            if (bwTimer >= 1f)
            {
                bwTimer = 0f;
                if (Net.IsOnline)
                {
                    float i = NetDiagnostics.BytesInPerSecond / 1024f, o = NetDiagnostics.BytesOutPerSecond / 1024f;
                    bwIn += i;
                    bwOut += o;
                    bwN++;
                    bwPeak = Mathf.Max(bwPeak, i + o);
                }
            }
        }

        // ------------------------------------------------------------------ the run
        void Start()
        {
            NetBus.Register(MsgType, OnMsg);
            StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            startTime = Time.realtimeSinceStartup;
            report.role = Role;
            report.started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            try { Directory.CreateDirectory(outDir); }
            catch (Exception e)
            {
                Debug.LogError("AutoPlaytestMP: cannot create output folder " + outDir + ": " + e.Message);
                Application.Quit(2);
                yield break;
            }
            Note("AutoPlaytestMP (" + Role + ") started, output " + outDir + ", port " + port);

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
            Note("boot ready after " + F(BootState.BootSeconds) + " s" + (BootState.HadErrors ? " (boot had errors)" : ""));

            yield return RunStep(1, "01 Session: host on 127.0.0.1, client joins, same world + state", StepSession, 320f);
            yield return RunStep(2, "02 Avatars: each side sees the other diver with nameplate", StepAvatars, 150f);
            yield return RunStep(3, "03 Fishing: client catches + lists a fish → host money rises, client HUD equal", StepFishing, 400f);
            yield return RunStep(4, "04 Shop: client buys → host delivery → chopper + crate on both → client opens → owned on both", StepShop, 330f);
            yield return RunStep(5, "05 Boat: host drives, client on deck sees it move; seat refuses the client; client drives", StepBoat, 260f);
            yield return RunStep(6, "06 Filming: client films a host creature (proxy) at the house reef → host journal + popups", StepFilming, 200f);
            yield return RunStep(7, "07 Upload: client banks footage + uploads → upload on host", StepUpload, 150f);
            yield return RunStep(8, "08 Sleep: client sleeps → both wake up next morning, video resolved on both", StepSleep, 150f);
            yield return RunStep(9, "09 Collectibles: pickups sync both ways", StepCollectibles, 200f);
            yield return RunStep(10, "10 Voice: synthetic tone both ways (frames arrive and play)", StepVoice, 150f);
            yield return RunStep(11, "11 Performance + bandwidth", StepPerf, 120f);
            yield return RunStep(12, "12 Client disconnects → avatar removed on host", StepDisconnect, 120f);
            yield return RunStep(13, "13 Drop-in rejoin → client gets the current state", StepRejoin, 200f);
            yield return RunStep(14, "14 Host quits → client back in the menu with a message", StepHostQuit, 120f, true);

            Finish(report.failed == 0 ? 0 : 1);
        }

        // ------------------------------------------------------------------ step runner
        IEnumerator RunStep(int index, string name, Func<IEnumerator> body, float timeout, bool always = false)
        {
            cur = new StepResult { name = name };
            myStep = index;
            curFrames = 0;
            curTime = 0f;
            bwTimer = 0f;
            bwIn = bwOut = bwPeak = 0f;
            bwN = 0;
            if (!always && Time.realtimeSinceStartup - startTime > TimeBudget)
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
            Send("step", index.ToString(Inv));
            int e0 = errorCount, x0 = exceptionCount;
            float t0 = Time.realtimeSinceStartup;
            Note("STEP " + name);
            GameInput.SimClear();
            yield return Guard(body(), name, timeout);
            GameInput.SimClear();
            NetVoice.TestToneHz = 0f;
            if (pc != null && !pc.enabled) pc.enabled = true;
            var hc = Refs.HandCamera as FilmHandCamera;
            if (hc != null && hc.IsRecording)
            {
                GameInput.SimTap(GameAction.Use);
                yield return null;
                yield return null;
            }
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
            if (UIState.InventoryOpen) UIModule.DebugOpenPhone("inventory:close");
            cur.seconds = Time.realtimeSinceStartup - t0;
            cur.errors = errorCount - e0;
            cur.fps = curTime > 0f ? curFrames / curTime : 0f;
            if (bwN > 0)
            {
                cur.kbInAvg = bwIn / bwN;
                cur.kbOutAvg = bwOut / bwN;
                cur.kbPeak = bwPeak;
            }
            if (exceptionCount > x0) Check(false, (exceptionCount - x0) + " exception(s) logged during this step");
            report.steps.Add(cur);
            Note((cur.pass ? "PASS " : "FAIL ") + name + " (" + F(cur.seconds) + " s, " + F(cur.fps, "0") + " fps, net in " + F(cur.kbInAvg, "0.0") + " / out " + F(cur.kbOutAvg, "0.0") + " KB/s)");
            cur = null;
            WriteReport();
        }

        IEnumerator Guard(IEnumerator inner, string what, float timeout)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            float t0 = Time.realtimeSinceStartup;
            while (stack.Count > 0)
            {
                if (Time.realtimeSinceStartup - t0 > timeout)
                {
                    Check(false, "step timed out after " + F(timeout, "0") + " s");
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
            Debug.Log("AutoPlaytestMP[" + Role + "]: " + line);
            log.AppendLine(DateTime.Now.ToString("HH:mm:ss", Inv) + "  " + line);
        }

        static string F(float v, string fmt = "0.0") => v.ToString(fmt, Inv);
        static string F(double v, string fmt = "0.0") => v.ToString(fmt, Inv);

        // ================================================================== 01 session
        IEnumerator BootToMenu()
        {
            if (PlayerIntro.Playing) PlayerIntro.Skip();
            var ui = UIModule.Controller;
            if (ui != null && ui.Intro.Active)
            {
                yield return WaitUntil(() => !ui.Intro.Active, 8f);
                if (ui.Intro.Active) ui.Intro.Skip();
            }
            yield return WaitUntil(() => UIState.MainMenuOpen && !UIState.LoadingOpen, 30f);
            yield return Wait(1.5f);
            Check(UIState.MainMenuOpen, "main menu up");
        }

        IEnumerator StepSession()
        {
            var gm = GameManager.I;
            yield return BootToMenu();
            if (isHost)
            {
                string bind = string.IsNullOrWhiteSpace(NetBoot.BindOverride) ? HostIp : NetBoot.BindOverride;
                bool ok = NetSession.StartHost(port, bind, out string err);
                Check(ok, "hosting on " + bind + ":" + NetSession.HostPort + (ok ? " (" + NetDiagnostics.Transport + ")" : " – " + err));
                if (!ok) yield break;
                if (NetSession.HostPort != port) Info("port " + port + " is taken on this PC (another program) – the host moved to " + NetSession.HostPort + "; status '" + NetSession.Status + "'");
                NetSession.SuppressSaving();
                NetLobby.StartHostedGame(false, Difficulty.OpenWater);
                yield return WaitUntil(() => gm.InGame, 20f);
                Check(gm.InGame, "host entered a new game (Normal)");
                if (!gm.InGame) yield break;
                yield return Wait(1f);
                BindPlayer();
                SetTime(0.42f);
                float tj = Time.realtimeSinceStartup;
                yield return WaitUntil(() => FindPeerId() >= 0, JoinTimeout);
                peerId = FindPeerId();
                Check(peerId >= 0, "a client joined after " + F(Time.realtimeSinceStartup - tj, "0") + " s (" + (peerId >= 0 ? "id " + peerId + " '" + Net.Find(peerId).name + "'" : "nobody") + ")");
                if (peerId < 0) yield break;
                yield return Sync("S01", 90f);
                Check(Net.IsHost && Net.Players.Count == 2, "host session: " + Net.Players.Count + " players, transport " + NetDiagnostics.Transport);
                Check(NetDiagnostics.Transport.Contains("127.0.0.1"), "listening only on 127.0.0.1 (no firewall prompt)");
                SendVal("world", WorldFingerprint());
                SendVal("state", StateLine());
                SendVal("clock", S.timeOfDay.ToString("R", Inv));
                yield return WaitVal("worldcheck", 30f);
                string wc = Val("worldcheck") ?? "-";
                Check(wc == "same", "client built the identical island (" + wc + ")");
                yield return WaitVal("statecheck", 30f);
                string sc = Val("statecheck") ?? "-";
                Check(sc == "same", "client got the host's game state (" + sc + ")");
                yield return Shot("host_S01_ingame");
            }
            else
            {
                StartJoin(JoinTimeout);
                float tj = Time.realtimeSinceStartup;
                yield return WaitUntil(() => NetSession.Current == NetSession.Phase.Joined && gm.InGame, JoinTimeout + 40f);
                bool joined = NetSession.Current == NetSession.Phase.Joined && gm.InGame && Net.IsClient;
                Check(joined, "joined '" + JoinText + "' → " + NetSession.JoinTarget + " after " + F(Time.realtimeSinceStartup - tj, "0") + " s as player " + Net.LocalId + " (status '" + NetSession.Status + "')");
                if (!joined) yield break;
                peerId = 0;
                yield return Wait(1.5f);
                BindPlayer();
                yield return Sync("S01", 90f);
                var host = Net.Find(0);
                Check(host != null && Net.Players.Count == 2, "player list: " + Net.Players.Count + " (host '" + (host != null ? host.name : "-") + "', me '" + Net.LocalName + "')");
                Check(GameManager.SavingDisabled, "the client never saves");
                yield return WaitVal("world", 30f);
                string mine = WorldFingerprint(), theirs = Val("world") ?? "";
                bool same = mine == theirs;
                Check(same, "same procedural island as the host" + (same ? "" : ": first difference at char " + FirstDiff(mine, theirs) + " (" + Trim(mine, 120) + " vs " + Trim(theirs, 120) + ")"));
                SendVal("worldcheck", same ? "same" : "different at " + FirstDiff(mine, theirs));
                yield return WaitVal("state", 30f);
                yield return Wait(0.5f);
                string sm = StateLine(), st = Val("state") ?? "";
                bool ss = sm == st;
                Check(ss, "same shared state as the host: " + sm + (ss ? "" : " vs host " + st));
                SendVal("statecheck", ss ? "same" : sm + " vs " + st);
                yield return WaitVal("clock", 10f);
                float hostClock = Val("clock") != null ? float.Parse(Val("clock"), Inv) : -1f;
                float clockDiff = Mathf.Abs(S.timeOfDay - hostClock) * 24f * 60f;
                Check(hostClock >= 0f && clockDiff < 6f, "clock follows the host (" + GameManager.Clock(S.timeOfDay) + " vs host " + GameManager.Clock(hostClock) + ", " + F(clockDiff, "0.0") + " in-game min apart)");
                yield return Shot("client_S01_ingame");
            }
        }

        /// Joins the way a player does: the address typed without a port (a host on this PC may sit on a fallback port).
        string JoinText => port == Net.DefaultPort ? HostIp : HostIp + ":" + port;

        void StartJoin(float retrySeconds)
        {
            if (!NetSession.TryParseAddress(JoinText, out string h, out int p, out bool explicitPort, out string err))
            {
                Check(false, "cannot parse '" + JoinText + "': " + err);
                return;
            }
            NetSession.EnableAutoRetry(h, p, explicitPort, retrySeconds);
            NetSession.JoinHostPort(h, p, explicitPort);
        }

        void BindPlayer()
        {
            pc = Refs.Player as PlayerController;
            cam = pc != null ? pc.View : null;
            Check(pc != null && cam != null, "local player ready");
        }

        static string StateLine()
        {
            var s = S;
            return "seed " + s.saveSeed + ", " + s.difficulty + ", day " + s.day + ", money " + s.money + ", followers " + s.followers
                   + ", items " + s.ownedItems.Count + ", sites " + s.unlockedSites.Count;
        }

        static string WorldFingerprint()
        {
            var sb = new StringBuilder(1024);
            sb.Append(V(WorldInfo.DockEndPosition)).Append(';').Append(V(WorldInfo.HelipadPosition)).Append(';')
              .Append(V(WorldInfo.BoatSpawn)).Append(';').Append(V(WorldInfo.BedPosition)).Append(';');
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                    sb.Append(WorldInfo.GroundHeight(new Vector3(x * 55f + 3f, 0f, z * 55f - 7f)).ToString("0.00", Inv)).Append(',');
            var picks = Object.FindObjectsByType<PropPickup>(FindObjectsSortMode.None);
            var ids = new List<string>();
            foreach (var p in picks)
            {
                if (p == null || p.kind == CollectibleKind.BottleMessage) continue;
                Vector3 q = p.transform.position;
                ids.Add(p.id + "@" + Mathf.RoundToInt(q.x) + "/" + Mathf.RoundToInt(q.y) + "/" + Mathf.RoundToInt(q.z));
            }
            ids.Sort(StringComparer.Ordinal);
            sb.Append(";pickups ").Append(ids.Count).Append(':');
            foreach (var s in ids) sb.Append(s).Append(',');
            return sb.ToString();
        }

        static int FirstDiff(string a, string b)
        {
            int n = Mathf.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++) if (a[i] != b[i]) return i;
            return n;
        }

        // ================================================================== 02 avatars
        Vector3 HostSpot => SnapDown(DockEnd() + new Vector3(-4f, 0.4f, 0f));
        Vector3 ClientSpot => SnapDown(DockEnd() + new Vector3(-8.5f, 0.4f, 0f));

        IEnumerator StepAvatars()
        {
            yield return Sync("S02a", 120f);
            if (pc == null) yield break;
            Vector3 me = isHost ? HostSpot : ClientSpot, other = isHost ? ClientSpot : HostSpot;
            yield return Stand(me, YawTo(me, other));
            yield return Sync("S02b", 40f);
            yield return Wait(1.5f);
            var a = PeerAvatar();
            Check(a != null && a.Visible, "the other diver's avatar is visible (" + (a != null ? "'" + a.PlayerName + "'" : "none") + ")");
            if (a == null) yield break;
            float off = FlatDist(a.transform.position, other);
            Check(off < 1.0f, "avatar stands where the other player is (" + F(off, "0.00") + " m off)");
            var info = Net.Find(peerId);
            Check(info != null && a.PlayerName == info.name, "avatar name = player name '" + (info != null ? info.name : "-") + "'");
            yield return AimAt(a.HeadPosition);
            yield return Wait(0.8f);
            var plate = a.transform.Find("Nameplate");
            Check(plate != null && plate.gameObject.activeInHierarchy, "nameplate shown above the head");
            var model = a.transform.Find("Diver");
            Check(model != null && model.gameObject.activeInHierarchy, "diver model shown (" + (model != null ? model.childCount + " parts" : "none") + ")");
            Check(Divers.All.Count == 2, "Divers.All has both divers (" + Divers.All.Count + ")");
            yield return Shot(Role + "_S02_sees_" + (isHost ? "client" : "host"));
            yield return Sync("S02c", 40f);
        }

        // ================================================================== 03 fishing + market
        IEnumerator StepFishing()
        {
            yield return Sync("S03a", 120f);
            if (pc == null) yield break;
            Vector3 end = DockEnd();
            if (isHost)
            {
                long money0 = S.money;
                Vector3 me = SnapDown(end + new Vector3(-6.5f, 0.4f, 0.8f));
                yield return Stand(me, 90f);
                float t0 = Time.realtimeSinceStartup;
                bool shot = false;
                while (Val("caught") == null && Time.realtimeSinceStartup - t0 < 170f && !PeerGone && !PeerAhead)
                {
                    if (!shot && Time.realtimeSinceStartup - t0 > 9f)
                    {
                        shot = true;
                        var a = PeerAvatar();
                        if (a != null) yield return AimAt(a.HeadPosition + Vector3.down * 0.3f);
                        Check(a != null && a.RightItem == HoldItem.FishingRod, "host sees the client holding the rod (" + (a != null ? a.RightItem + "/" + a.LeftItem : "-") + ")");
                        yield return Shot("host_S03_client_fishing");
                    }
                    yield return null;
                }
                string caught = Val("caught");
                Check(caught != null, "client reports a catch (" + (caught ?? "none") + ")");
                if (caught == null) yield break;
                string[] parts = caught.Split('|');
                int uid = parts.Length > 0 && int.TryParse(parts[0], NumberStyles.Integer, Inv, out int u) ? u : 0;
                int value = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, Inv, out int v) ? v : 0;
                yield return WaitUntil(() => HasFish(uid), 5f);
                Check(HasFish(uid) || S.fishSold > 0, "the client's fish (uid " + uid + ", " + value + " $) is in the host's shared cooler box / market (fishCaught " + S.fishCaught + ")");
                yield return WaitVal("listed", 60f);
                yield return WaitUntil(() => S.fishListings.Count > 0 || S.fishSold > 0, 5f);
                Check(S.fishListings.Count > 0 || S.fishSold > 0, "the client's listing reached the host market (" + S.fishListings.Count + " listings, inventory " + S.fishInventory.Count + ")");
                float ts = Time.realtimeSinceStartup;
                yield return WaitUntil(() => S.fishSold > 0 && S.fishListings.Count == 0, 110f);
                Check(S.fishSold > 0 && S.fishListings.Count == 0, "sold on the host after " + F(Time.realtimeSinceStartup - ts, "0") + " s");
                long gained = S.money - money0;
                Check(gained > 0 && (value <= 0 || gained == value), "host money +" + gained + " $ (fish value " + value + " $), now " + S.money + " $");
                Metric("coop.fishValue", value + " $ (co-op fish factor with 2 players: " + F(Progression.CoopFishFactor(2), "0.00") + ")");
                SendVal("sold", S.money.ToString(Inv));
                yield return Sync("S03b", 60f);
            }
            else
            {
                Vector3 me = SnapDown(end + new Vector3(-2.5f, 0.4f, 0f));
                yield return Stand(me, 180f);
                Hands.StowAll();
                yield return Wait(0.3f);
                GameInput.SimTap(GameAction.RightSlot2);
                yield return Wait(0.9f);
                Check(Hands.IsHeld(HoldItem.FishingRod), "rod in both hands (key 2)");
                yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Ready, 3f);
                int inv0 = FishMarket.InventoryCount;
                bool real = false;
                float t0 = Time.realtimeSinceStartup;
                int casts = 0, notWater = 0;
                while (FishMarket.InventoryCount <= inv0 && Time.realtimeSinceStartup - t0 < 130f)
                {
                    if (FishingRodTool.State != FishingRodTool.RodState.Ready)
                    {
                        yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Ready, 6f);
                        if (FishingRodTool.State != FishingRodTool.RodState.Ready) { Info("rod stuck in " + FishingRodTool.State); break; }
                    }
                    GameInput.SimPress(GameAction.Use);
                    yield return Wait(0.8f);
                    GameInput.SimRelease(GameAction.Use);
                    casts++;
                    yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Waiting || FishingRodTool.State == FishingRodTool.RodState.Bite
                                                 || FishingRodTool.State == FishingRodTool.RodState.Ready, 6f);
                    if (FishingRodTool.State == FishingRodTool.RodState.Ready)
                    {
                        if (++notWater > 6) break;
                        GameInput.SimLook(new Vector2(40f, 0f));
                        yield return Wait(0.3f);
                        continue;
                    }
                    yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Bite || FishingRodTool.State == FishingRodTool.RodState.Ready, 55f);
                    if (FishingRodTool.State != FishingRodTool.RodState.Bite)
                    {
                        if (FishingRodTool.State == FishingRodTool.RodState.Waiting) GameInput.SimTap(GameAction.Use);
                        continue;
                    }
                    int before = FishMarket.InventoryCount;
                    GameInput.SimTap(GameAction.Use);
                    yield return WaitUntil(() => FishMarket.InventoryCount > before, 8f);
                    if (FishMarket.InventoryCount > before) real = true;
                    yield return WaitUntil(() => FishingRodTool.State == FishingRodTool.RodState.Ready, 5f);
                }
                Check(real, "client caught a fish with the real rod (" + casts + " casts, " + F(Time.realtimeSinceStartup - t0, "0") + " s)");
                if (!real)
                {
                    FishMarket.AddCatch(FishMarket.RollCatch(rng));
                    Info("fallback: added a rolled catch through the same FishMarket.AddCatch path to test the sync");
                }
                FishItem f = S.fishInventory.Count > 0 ? S.fishInventory[S.fishInventory.Count - 1] : null;
                if (f == null) { Check(false, "no fish in the cooler box"); yield break; }
                SendVal("caught", f.uid.ToString(Inv) + "|" + f.value.ToString(Inv));
                yield return Shot("client_S03_catch");
                yield return Wait(1.5f);
                Check(HasFish(f.uid), "after the host's snapshot the fish (uid " + f.uid + ") is still in the shared cooler box");
                Hands.StowAll();
                UIModule.DebugOpenPhone("market");
                yield return Wait(1.0f);
                int n = FishMarket.ListAll();
                Check(n >= 1, "listed " + n + " fish on the market (phone app)");
                SendVal("listed", n.ToString(Inv));
                yield return Wait(1.0f);
                yield return Shot("client_S03_market");
                UIModule.DebugOpenPhone("close");
                yield return WaitVal("sold", 150f);
                string sold = Val("sold");
                if (sold == null) { Check(false, "host never reported the sale"); yield break; }
                long hostMoney = long.Parse(sold, Inv);
                yield return WaitUntil(() => S.money == hostMoney, 5f);
                Check(S.money == hostMoney, "client money " + S.money + " $ = host money " + hostMoney + " $");
                yield return Wait(0.4f);
                string hud = HudMoneyText();
                Check(hud == Loc.Money(hostMoney), "client HUD shows '" + hud + "' (expected '" + Loc.Money(hostMoney) + "')");
                Check(ToastSeen("verkauft", "sold"), "client got the 'fish sold' toast");
                yield return Shot("client_S03_money");
                yield return Sync("S03b", 60f);
            }
        }

        static bool HasFish(int uid)
        {
            var s = S;
            for (int i = 0; i < s.fishInventory.Count; i++) if (s.fishInventory[i] != null && s.fishInventory[i].uid == uid) return true;
            for (int i = 0; i < s.fishListings.Count; i++) if (s.fishListings[i] != null && s.fishListings[i].fish != null && s.fishListings[i].fish.uid == uid) return true;
            return false;
        }

        bool ToastSeen(string de, string en)
        {
            foreach (var t in toastsSeen) if (t.IndexOf(de, StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf(en, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static string HudMoneyText()
        {
            var ui = UIModule.Controller;
            if (ui == null || ui.Hud == null) return null;
            var f = typeof(UIHud).GetField("moneyLabel", BindingFlags.NonPublic | BindingFlags.Instance);
            var l = f != null ? f.GetValue(ui.Hud) as Label : null;
            return l != null ? l.text : null;
        }

        // ================================================================== 04 shop + helicopter
        IEnumerator StepShop()
        {
            yield return Sync("S04a", 120f);
            if (pc == null) yield break;
            var gm = GameManager.I;
            var tank = GearDatabase.Get("tank_t1");
            long price = Shop.Price(tank, false);
            Vector3 pad = WorldInfo.HelipadPosition != Vector3.zero ? WorldInfo.HelipadPosition : WorldLayout.Helipad;
            if (isHost)
            {
                SetTime(0.45f);
                if (S.money < price + 20)
                {
                    long add = price + 20 - S.money;
                    gm.AddMoney(add);
                    Info("test top-up +" + add + " $ so the client can afford the " + price + " $ tank");
                }
                long money1 = S.money;
                SendVal("money4", money1.ToString(Inv));
                Vector3 me = SnapDown(pad + new Vector3(9f, 3f, 9f));
                yield return Stand(me, YawTo(me, pad));
                yield return WaitPeer(() => Shop.FindDelivery("tank_t1") != null, 60f);
                var d = Shop.FindDelivery("tank_t1");
                Check(d != null, "the client's order is a delivery in the host state");
                if (d == null) yield break;
                Check(S.money == money1 - price, "shared wallet paid " + (money1 - S.money) + " $ (price " + price + " $)");
                Check(d.arrivalDay == S.day, "standard delivery the same day at " + GameManager.Clock(d.arrivalTime));
                SendVal("arrival", d.arrivalDay.ToString(Inv) + "|" + d.arrivalTime.ToString("R", Inv));
                yield return Sync("S04b", 40f);
                S.timeOfDay = d.arrivalTime + 0.0015f;   // fast-forward (clock streams to the client)
                yield return WaitChopperAndCrate(pad, "host");
                yield return Sync("S04c", 120f);
                yield return WaitPeer(() => S.ownedItems.Contains("tank_t1") && Shop.FindDelivery("tank_t1") == null, 60f);
                Check(S.ownedItems.Contains("tank_t1") && Shop.FindDelivery("tank_t1") == null, "the client opened the crate: tank_t1 owned in the host state, delivery gone");
                yield return WaitUntil(() => FindCrate("tank_t1") == null, 6f);
                Check(FindCrate("tank_t1") == null, "the host's crate disappeared");
                Info("host's own equipment choice: " + (Gear.IsEquipped("tank_t1") ? "tank_t1" : "snorkel") + " (" + F(pc.Gear.airSeconds, "0") + " s air)");
                SendVal("owned", "1");
                yield return Shot("host_S04_crate_gone");
                yield return Sync("S04d", 40f);
            }
            else
            {
                yield return WaitVal("money4", 30f);
                long m4 = Val("money4") != null ? long.Parse(Val("money4"), Inv) : 0;
                yield return WaitUntil(() => S.money == m4 && m4 > 0, 6f);
                Check(S.money >= price, "client sees the shared wallet: " + S.money + " $");
                Vector3 me = SnapDown(pad + new Vector3(-9f, 3f, 9f));
                yield return Stand(me, YawTo(me, pad));
                UIModule.DebugOpenPhone("shop");
                yield return Wait(1.2f);
                yield return Shot("client_S04_shop");
                bool ok = Shop.Buy(tank, false);
                Check(ok, "client ordered tank_t1 (" + price + " $)");
                UIModule.DebugOpenPhone("close");
                yield return WaitVal("arrival", 40f);
                string arr = Val("arrival");
                if (arr == null) { Check(false, "host did not confirm the order"); yield break; }
                float at = float.Parse(arr.Split('|')[1], Inv);
                yield return WaitUntil(() => { var dd = Shop.FindDelivery("tank_t1"); return dd != null && Mathf.Abs(dd.arrivalTime - at) < 1e-4f; }, 6f);
                var d = Shop.FindDelivery("tank_t1");
                Check(d != null && Mathf.Abs(d.arrivalTime - at) < 1e-4f, "client state has the host's delivery (arrives " + GameManager.Clock(at) + ")");
                Check(S.money == m4 - price, "client wallet after the order: " + S.money + " $");
                Check(ToastSeen("Uhr", "arrive") || ToastSeen("Lieferung", "deliver") || ToastSeen("bestellt", "ordered"), "client got the order confirmation toast");
                yield return Sync("S04b", 40f);
                yield return WaitChopperAndCrate(pad, "client");
                yield return Sync("S04c", 120f);
                var crate = FindCrate("tank_t1");
                if (crate == null) { Check(false, "no crate to open"); yield break; }
                Vector3 cp = crate.transform.position;
                Vector3 dir = Flat(pc.Root.position - cp);
                Vector3 feet = SnapDown(cp + dir * 1.9f + Vector3.up * 2f);
                yield return Stand(feet, YawTo(feet, cp));
                yield return AimAt(cp + Vector3.up * 0.35f);
                Info("prompt at the crate: '" + InteractionPrompt.Text + "'");
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.8f);
                if (!S.ownedItems.Contains("tank_t1"))
                {
                    Info("E did not open the crate from here – opened via the crate");
                    if (crate != null && crate.CanInteract()) crate.Interact();
                    yield return Wait(0.3f);
                }
                Check(S.ownedItems.Contains("tank_t1"), "client opened the crate: tank_t1 owned");
                yield return WaitVal("owned", 40f);
                yield return Wait(1.0f);
                Check(S.ownedItems.Contains("tank_t1") && Shop.FindDelivery("tank_t1") == null, "still owned after the host's snapshot, delivery gone");
                Check(Gear.IsEquipped("tank_t1") && Mathf.Abs(pc.Gear.airSeconds - 90f) < 1f, "the opener equips it: " + F(pc.Gear.airSeconds, "0") + " s air");
                yield return Shot("client_S04_opened");
                yield return Sync("S04d", 40f);
            }
        }

        IEnumerator WaitChopperAndCrate(Vector3 pad, string who)
        {
            float tw = Time.realtimeSinceStartup;
            bool sawChopper = false, shot = false;
            HeliCrate crate = null;
            while (Time.realtimeSinceStartup - tw < 150f)
            {
                crate = FindCrate("tank_t1");
                if (crate != null && crate.CanInteract()) break;
                if (Object.FindFirstObjectByType<HeliChopper>() != null) sawChopper = true;
                if (!shot && sawChopper && Time.realtimeSinceStartup - tw > 12f)
                {
                    shot = true;
                    yield return AimAt(pad + Vector3.up * 20f);
                    yield return Shot(who + "_S04_chopper");
                }
                yield return Wait(0.4f);
            }
            Check(sawChopper, who + " sees the helicopter");
            Check(crate != null, who + " sees the crate on the helipad (" + F(Time.realtimeSinceStartup - tw, "0") + " s after the arrival time)");
            if (crate != null)
            {
                yield return AimAt(crate.transform.position + Vector3.up * 0.4f);
                yield return Shot(who + "_S04_crate");
            }
        }

        // ================================================================== 05 boat
        IEnumerator StepBoat()
        {
            yield return Sync("S05a", 120f);
            if (pc == null) yield break;
            var boat = Refs.Boat as BoatController;
            Check(boat != null, "boat exists");
            if (boat == null) yield break;
            Transform seat = boat.transform.Find("DriverSeat");
            var helms = boat.GetComponentsInChildren<BoatHelmInteractable>(true);
            Vector3 mooring = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawn : WorldLayout.BoatSpawn;
            float mooringYaw = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawnYaw : WorldLayout.BoatSpawnYaw;
            if (isHost)
            {
                boat.PlaceAt(mooring, mooringYaw);
                if (!boat.Anchored) boat.ToggleAnchor();
                yield return Wait(0.6f);
                yield return Stand(SnapDown(DockEnd() + new Vector3(-1.5f, 0.4f, 0f)), 0f);
                if (!boat.PlayerAboard) boat.BoardFromDock();
                yield return Wait(0.8f);
                Vector3 sl = boat.transform.InverseTransformPoint(seat.position);
                pc.TeleportTo(boat.transform.TransformPoint(new Vector3(0f, sl.y + 0.45f, sl.z - 1.0f)), boat.transform.eulerAngles.y);
                yield return Wait(0.8f);
                yield return Sync("S05b", 60f);
                yield return AimAt(() => seat.position + boat.transform.up * 0.5f);
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.8f);
                if (!boat.PlayerDriving)
                {
                    Info("E did not reach the seat – using the helm directly");
                    foreach (var h in helms) if (h.CanInteract()) { h.Interact(); break; }
                    yield return Wait(0.5f);
                }
                Check(boat.PlayerDriving && NetFlags.BoatDriverId == Net.LocalId, "host at the wheel (driver id " + NetFlags.BoatDriverId + ")");
                yield return Sync("S05c", 40f);
                if (boat.Anchored) { GameInput.SimTap(GameAction.Anchor); yield return Wait(0.6f); }
                Check(!boat.Anchored, "anchor up");
                Vector3 p0 = boat.transform.position;
                GameInput.SimPress(GameAction.Forward);
                float t = 0f;
                float nextReport = 1f;
                while (t < 6f)
                {
                    t += Time.unscaledDeltaTime;
                    if (t >= nextReport)
                    {
                        nextReport += 1f;
                        SendVal("bpos" + Mathf.RoundToInt(nextReport - 1f), Vec(boat.transform.position));
                    }
                    yield return null;
                }
                GameInput.SimRelease(GameAction.Forward);
                float moved = FlatDist(boat.transform.position, p0);
                Check(moved > 4f, "host drove the boat " + F(moved) + " m");
                yield return Shot("host_S05_driving");
                yield return Sync("S05d", 40f);
                yield return WaitVal("seatdone", 40f);
                if (!boat.Anchored) { GameInput.SimTap(GameAction.Anchor); yield return Wait(0.6f); }
                for (int i = 0; i < 8 && boat.PlayerDriving; i++)
                {
                    yield return Wait(1.0f);
                    GameInput.SimTap(GameAction.Interact);
                    yield return Wait(0.4f);
                }
                Check(!boat.PlayerDriving && NetFlags.BoatDriverId == -1, "host got up, seat free (driver id " + NetFlags.BoatDriverId + ")");
                yield return Sync("S05e", 40f);
                // the client drives now: the host follows the client's stream
                yield return WaitPeer(() => NetFlags.BoatDriverId == peerId, 20f);
                Check(NetFlags.BoatDriverId == peerId && NetFlags.BoatRemote, "host sees the client at the wheel (driver " + NetFlags.BoatDriverId + ", remote " + NetFlags.BoatRemote + ")");
                Vector3 h0 = boat.transform.position;
                yield return WaitVal("cdrove", 40f);
                float hm = FlatDist(boat.transform.position, h0);
                Check(hm > 2.5f, "host sees the boat move while the client drives (" + F(hm) + " m)");
                var rope = boat.transform.parent != null ? boat.transform.parent.Find("AnchorRope") : null;
                var lr = rope != null ? rope.GetComponent<LineRenderer>() : null;
                float ropeLen = lr != null && lr.enabled && lr.positionCount > 1 ? Vector3.Distance(lr.GetPosition(0), lr.GetPosition(lr.positionCount - 1)) : 0f;
                Check(ropeLen < 10f, "the raised anchor comes along with the moving boat (rope " + (lr != null && lr.enabled ? F(ropeLen) + " m" : "stowed") + ", anchored " + boat.Anchored + ")");
                var a = PeerAvatar();
                Check(a != null && a.Seated, "the client's avatar sits at the wheel on the host's screen");
                BoatDiag(boat, seat, "host after getting up", a);
                if (a != null) yield return AimAt(() => a.HeadPosition);
                BoatDiag(boat, seat, "host aimed at the client driver", a);
                yield return Shot("host_S05_client_driving");
                yield return Sync("S05f", 40f);
                yield return WaitUntil(() => NetFlags.BoatDriverId == -1 && !NetFlags.BoatRemote, 8f);
                Check(NetFlags.BoatDriverId == -1 && !NetFlags.BoatRemote, "the boat is back under host simulation after the client got up");
            }
            else
            {
                yield return WaitUntil(() => FlatDist(boat.transform.position, mooring) < 3f, 8f);
                yield return Stand(SnapDown(DockEnd() + new Vector3(-3.0f, 0.4f, 0f)), 0f);
                if (!boat.PlayerAboard) boat.BoardFromDock();
                yield return Wait(0.8f);
                pc.TeleportTo(boat.RescuePoint, boat.transform.eulerAngles.y);
                yield return Wait(0.8f);
                Check(boat.PlayerAboard && !pc.IsInWater, "client boarded the boat and stands on the deck");
                yield return Sync("S05b", 60f);
                yield return WaitUntil(() => NetFlags.BoatDriverId == 0, 15f);
                Check(NetFlags.BoatDriverId == 0, "client sees the host at the wheel (driver id " + NetFlags.BoatDriverId + ")");
                Check(NetFlags.BoatRemote, "boat simulated on the host (BoatRemote on the client)");
                var a = PeerAvatar();
                if (a != null)
                {
                    // the seated flag rides on the 15 Hz avatar stream (100 ms interpolation), the driver id is a reliable message
                    yield return WaitUntil(() => a.Seated, 3f);
                    Check(a.Seated, "the host's avatar sits at the wheel");
                    yield return AimAt(() => a.HeadPosition);
                    BoatDiag(boat, seat, "client looking at the host driver", a);
                }
                yield return Sync("S05c", 40f);
                Vector3 b0 = boat.transform.position;
                Vector3 local0 = boat.transform.InverseTransformPoint(pc.Root.position);
                float maxErr = 0f;
                int compared = 0;
                float t = 0f;
                bool shot = false;
                var done = new HashSet<string>();
                var track = new StringBuilder();
                float nextSample = 0f;
                while (t < 8f)
                {
                    t += Time.unscaledDeltaTime;
                    if (t >= nextSample)
                    {
                        nextSample += 1f;
                        Vector3 lp = boat.transform.InverseTransformPoint(pc.Root.position);
                        track.Append(F(t, "0")).Append("s ").Append(V(lp)).Append(pc.IsInWater ? " water" : "").Append("; ");
                    }
                    for (int k = 1; k <= 6; k++)
                    {
                        string key = "bpos" + k;
                        if (done.Contains(key) || Val(key) == null) continue;
                        done.Add(key);
                        float err = FlatDist(ParseVec(Val(key)), boat.transform.position);
                        maxErr = Mathf.Max(maxErr, err);
                        compared++;
                    }
                    if (!shot && t > 3.5f)
                    {
                        shot = true;
                        yield return Shot("client_S05_on_deck");
                    }
                    yield return null;
                }
                float moved = FlatDist(boat.transform.position, b0);
                Vector3 local1 = boat.transform.InverseTransformPoint(pc.Root.position);
                float drift = Vector3.Distance(new Vector3(local0.x, 0f, local0.z), new Vector3(local1.x, 0f, local1.z));
                Check(moved > 4f, "client sees the boat move " + F(moved) + " m");
                Check(compared > 0 && maxErr < 3f, "client boat pose follows the host (" + compared + " samples, max " + F(maxErr, "0.00") + " m apart incl. 100 ms interpolation + message delay)");
                Check(boat.PlayerAboard && !pc.IsInWater && drift < 1.0f, "client carried by the deck (drift " + F(drift, "0.00") + " m, aboard " + boat.PlayerAboard + ")");
                Info("boat-local feet while the host drives: start " + V(local0) + "; " + track);
                yield return Sync("S05d", 40f);
                // the seat refuses the second player (same standing spot the host used to take the wheel)
                Vector3 sl = boat.transform.InverseTransformPoint(seat.position);
                pc.TeleportTo(boat.transform.TransformPoint(new Vector3(0f, sl.y + 0.45f, sl.z - 1.0f)), boat.transform.eulerAngles.y);
                yield return Wait(0.8f);
                BoatDiag(boat, seat, "client next to the occupied seat", null);
                float seatDist = FlatDist(pc.Root.position, seat.position);
                Check(seatDist < 1.5f && boat.PlayerAboard, "placed 1 m behind the seat on the still moving deck, the client lands there (" + F(seatDist, "0.00") + " m from the seat)");
                string prompt = helms.Length > 0 ? helms[0].GetPrompt() : null;
                Check(!boat.CanEnterHelm() && boat.HelmOccupied, "seat refuses: CanEnterHelm " + boat.CanEnterHelm() + ", occupied " + boat.HelmOccupied + ", prompt '" + prompt + "'");
                yield return AimAt(() => seat.position + boat.transform.up * 0.5f);
                string aimedPrompt = InteractionPrompt.Text;
                BoatDiag(boat, seat, "client aimed at the occupied seat (prompt '" + aimedPrompt + "')", null);
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.8f);
                Check(!boat.PlayerDriving && !pc.IsSeated && NetFlags.BoatDriverId == 0, "E at the occupied seat does not seat the client");
                Check(ToastSeen("Besetzt", "Occupied") && !string.IsNullOrEmpty(aimedPrompt) && aimedPrompt.Contains(Loc.Tr("Besetzt", "Occupied")),
                      "the crosshair prompt says '" + aimedPrompt + "' and E answers with the 'occupied' toast");
                yield return Shot("client_S05_seat_occupied");
                SendVal("seatdone", "1");
                yield return Sync("S05e", 40f);
                // seat free → the client drives
                yield return WaitUntil(() => NetFlags.BoatDriverId == -1, 8f);
                yield return Wait(0.6f);
                var presses = new StringBuilder();
                for (int attempt = 0; attempt < 3 && !boat.PlayerDriving; attempt++)
                {
                    yield return AimAt(() => seat.position + boat.transform.up * 0.5f);
                    presses.Append("press ").Append(attempt + 1).Append(": prompt '").Append(InteractionPrompt.Text).Append("', canEnter ").Append(boat.CanEnterHelm())
                           .Append(", driver ").Append(NetFlags.BoatDriverId).Append(", dist ").Append(F(FlatDist(pc.Root.position, seat.position), "0.00")).Append("; ");
                    GameInput.SimTap(GameAction.Interact);
                    yield return Wait(0.8f);
                }
                Info(presses.ToString());
                Check(boat.PlayerDriving, "E at the free seat (after the host got up) seats the client");
                if (!boat.PlayerDriving)
                {
                    Info("E did not reach the free seat – using the helm directly");
                    foreach (var h in helms) if (h.CanInteract()) { h.Interact(); break; }
                    yield return Wait(0.5f);
                }
                yield return WaitUntil(() => NetFlags.BoatDriverId == Net.LocalId && !NetFlags.BoatRemote, 5f);
                Check(boat.PlayerDriving && NetFlags.BoatDriverId == Net.LocalId && !NetFlags.BoatRemote, "client took the free wheel and simulates the boat now");
                if (boat.Anchored) { GameInput.SimTap(GameAction.Anchor); yield return Wait(0.6f); }
                Vector3 c0 = boat.transform.position;
                GameInput.SimPress(GameAction.Forward);
                yield return Wait(4f);
                GameInput.SimRelease(GameAction.Forward);
                float cm = FlatDist(boat.transform.position, c0);
                Check(cm > 3f, "client drove the boat " + F(cm) + " m");
                SendVal("cdrove", F(cm));
                yield return Sync("S05f", 40f);
                if (!boat.Anchored) { GameInput.SimTap(GameAction.Anchor); yield return Wait(0.6f); }
                for (int i = 0; i < 8 && boat.PlayerDriving; i++)
                {
                    yield return Wait(1.0f);
                    GameInput.SimTap(GameAction.Interact);
                    yield return Wait(0.4f);
                }
                Check(!boat.PlayerDriving, "client got up again");
            }
        }

        /// Boat-local positions of the local player, the seat, the camera direction and (optionally) the other avatar's head.
        void BoatDiag(BoatController boat, Transform seat, string what, NetAvatar a)
        {
            if (boat == null || pc == null) return;
            Transform bt = boat.transform;
            Vector3 me = bt.InverseTransformPoint(pc.Root.position);
            Vector3 st = seat != null ? bt.InverseTransformPoint(seat.position) : Vector3.zero;
            Vector3 fw = cam != null ? bt.InverseTransformDirection(cam.transform.forward) : Vector3.zero;
            string s = what + ": me " + V(me) + ", seat " + V(st) + ", flat dist " + F(FlatDist(pc.Root.position, seat != null ? seat.position : pc.Root.position), "0.00")
                       + " m, cam fwd " + V(fw) + ", aboard " + boat.PlayerAboard + ", inWater " + pc.IsInWater + ", seated " + pc.IsSeated
                       + ", driver " + NetFlags.BoatDriverId + ", remote " + NetFlags.BoatRemote;
            if (a != null) s += ", other head " + V(bt.InverseTransformPoint(a.HeadPosition)) + " (seated " + a.Seated + ", onBoat " + a.OnBoat + ")";
            Info(s);
        }

        // ================================================================== 06 filming a host creature
        IEnumerator StepFilming()
        {
            yield return Sync("S06a", 120f);
            if (pc == null) yield break;
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -7.5f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 3f, 2.5f, 2f);
            Vector3 fwd = Flat(site.center - floor);
            if (isHost)
            {
                int ev0 = speciesEvents;
                yield return WaitVal("divepos", 40f);
                string dp = Val("divepos");
                if (dp == null) { Check(false, "client did not dive"); yield break; }
                Vector3 ce = ParseVec(dp);
                yield return Wait(1.0f);
                // watch from the side
                Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 myEye = KeepInWater(ce - side * 6f + Vector3.up * 0.5f, 1.5f, 1.5f);
                yield return DiveTo(myEye, ce + fwd * 2f);
                var spawner = Refs.Spawner;
                var def = SpeciesDatabase.Get("green_turtle");
                var fs = FaunaModule.Spawner;
                if (fs != null && fs.ActiveCount > 110) fs.DespawnAll();
                ICreature turtle = null;
                if (spawner != null && def != null)
                    turtle = spawner.Spawn(def, KeepInWater(ce + fwd * 4f + Vector3.up * 0.3f, 1.5f, 1.5f), Progression.RollSizeCm(def, SizeTier.Large, rng), false);
                var fc = turtle as FaunaCreature;
                Check(turtle != null && fc != null && fc.NetId > 0, "host spawned a green turtle in front of the client (net id " + (fc != null ? fc.NetId : 0) + ")");
                if (turtle == null) yield break;
                SendVal("spawned", (fc != null ? fc.NetId : 0).ToString(Inv));
                float t0 = Time.realtimeSinceStartup;
                bool shot = false;
                while (!Journal.IsDiscovered("green_turtle") && Time.realtimeSinceStartup - t0 < 60f && !PeerGone && !PeerAhead)
                {
                    if (!shot && Time.realtimeSinceStartup - t0 > 3f)
                    {
                        shot = true;
                        Pose(myEye, Vector3.Lerp(ce, turtle.Position, 0.5f));
                        yield return Shot("host_S06_client_filming");
                    }
                    yield return null;
                }
                Check(Journal.IsDiscovered("green_turtle"), "the client's filming put the green turtle into the HOST's Sealog after " + F(Time.realtimeSinceStartup - t0, "0") + " s");
                yield return Wait(0.6f);
                Check(speciesEvents > ev0, "host raised the 'new species' popup (" + (speciesEvents - ev0) + " events)");
                yield return Shot("host_S06_popup");
                SendVal("journal", Journal.IsDiscovered("green_turtle") ? "1" : "0");
                yield return WaitVal("filmed", 30f);
                if (turtle.IsAlive && spawner != null) spawner.Despawn(turtle);
                pc.enabled = true;
                yield return Sync("S06b", 40f);
            }
            else
            {
                if (!Hands.IsHeld(HoldItem.Camera) && Hands.Find(HoldItem.Camera, out Hand ch, out int cs)) Hands.Select(ch, cs);
                yield return DiveTo(eye, eye + fwd * 10f);
                Check(Hands.IsHeld(HoldItem.Camera), "camera in hand under water");
                Check(pc.IsUnderwater, "client under water at the house reef (" + F(pc.Depth) + " m)");
                int ev0 = speciesEvents;
                SendVal("divepos", Vec(eye));
                yield return WaitVal("spawned", 30f);
                int nid = Val("spawned") != null ? int.Parse(Val("spawned"), Inv) : -1;
                NetCreatureProxy proxy = null;
                float tp = Time.realtimeSinceStartup;
                while (proxy == null && Time.realtimeSinceStartup - tp < 15f)
                {
                    var reg = CreatureRegistry.Active;
                    for (int i = 0; i < reg.Count; i++)
                    {
                        if (reg[i] is NetCreatureProxy px && px != null && px.IsAlive && px.Species != null && px.Species.id == "green_turtle"
                            && (nid <= 0 || px.NetId == nid))
                        {
                            proxy = px;
                            break;
                        }
                    }
                    if (proxy == null) yield return Wait(0.2f);
                }
                Check(proxy != null, "the host's turtle arrived as a creature proxy after " + F(Time.realtimeSinceStartup - tp, "0.0") + " s (net id " + nid + ", " + CountProxies() + " proxies around)");
                if (proxy == null) yield break;
                Check(FaunaModule.Spawner == null || FaunaModule.Spawner.ActiveCount == 0 || NetFlags.CreaturesRemote, "client spawns no real animals (CreaturesRemote " + NetFlags.CreaturesRemote + ")");
                yield return FollowFor(proxy, 4.5f, 2.6f, true, true, "client_S06_viewfinder");
                yield return StopFilming();
                Check(filmRegistered || Journal.IsDiscovered("green_turtle"), "client filmed the proxy and logged it (" + (filmRegistered ? F(filmRegisterTime) + " s" : "not registered") + ", hints " + filmHints + ")");
                Check(speciesEvents > ev0, "client raised the 'new species' popup");
                yield return Shot("client_S06_popup");
                Check(Footage.DiveCount > 0, Footage.DiveCount + " clip(s) of this dive");
                SendVal("filmed", "1");
                yield return WaitVal("journal", 40f);
                Check(Val("journal") == "1", "host confirms the Sealog entry");
                yield return Sync("S06b", 40f);
            }
        }

        static int CountProxies()
        {
            int n = 0;
            var reg = CreatureRegistry.Active;
            for (int i = 0; i < reg.Count; i++) if (reg[i] is NetCreatureProxy) n++;
            return n;
        }

        // ================================================================== 07 bank + upload
        IEnumerator StepUpload()
        {
            yield return Sync("S07a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            if (isHost)
            {
                yield return Stand(HostSpot, 270f);
                yield return WaitVal("banked", 40f);
                int n = Val("banked") != null ? int.Parse(Val("banked"), Inv) : 1;
                yield return WaitUntil(() => S.bankedFootage.Count >= n, 8f);
                Check(S.bankedFootage.Count >= n && n > 0, "client's clips banked in the host state (" + S.bankedFootage.Count + ")");
                SendVal("bankok", S.bankedFootage.Count.ToString(Inv));
                yield return WaitVal("uploaded", 40f);
                yield return WaitUntil(() => S.pendingUploads.Count >= 1, 8f);
                Check(S.pendingUploads.Count == 1 && S.bankedFootage.Count == 0, "client's FinTube upload is in the host state ('" + (S.pendingUploads.Count > 0 ? S.pendingUploads[0].title : "-") + "'), banked pool emptied");
                SendVal("upok", S.pendingUploads.Count.ToString(Inv));
                yield return Sync("S07b", 40f);
            }
            else
            {
                int clips = Footage.DiveCount;
                Check(clips > 0, clips + " clips from the dive");
                yield return Stand(ClientSpot, 90f);
                yield return Wait(0.8f);
                if (Footage.DiveCount > 0)
                {
                    Info("leaving the water by teleport did not bank – banking directly");
                    Footage.BankDive();
                }
                Check(Footage.DiveCount == 0 && Footage.BankedCount >= clips, "clips banked on land (" + Footage.BankedCount + ")");
                SendVal("banked", clips.ToString(Inv));
                yield return WaitVal("bankok", 30f);
                yield return Wait(0.6f);
                Check(Footage.BankedCount >= clips, "banked clips survive the host's snapshot (" + Footage.BankedCount + ")");
                UIModule.DebugOpenPhone("fintube");
                yield return Wait(1.2f);
                yield return Shot("client_S07_fintube");
                var up = Progression.CreateUpload(TitleStyle.Honest);
                Check(up != null, "client uploaded '" + (up != null ? up.title : "-") + "'");
                SendVal("uploaded", up != null ? up.title : "");
                UIModule.DebugOpenPhone("close");
                yield return WaitVal("upok", 30f);
                yield return Wait(0.8f);
                Check(S.pendingUploads.Count == 1 && S.bankedFootage.Count == 0, "client state after the host's snapshot: " + S.pendingUploads.Count + " pending upload, " + S.bankedFootage.Count + " banked");
                yield return Sync("S07b", 40f);
            }
        }

        // ================================================================== 08 sleep
        IEnumerator StepSleep()
        {
            yield return Sync("S08a", 120f);
            if (pc == null) yield break;
            var gm = GameManager.I;
            int day0 = S.day;
            if (isHost)
            {
                int sl0 = sleepEvents;
                yield return WaitUntil(() => S.day > day0, 40f);
                Check(S.day == day0 + 1, "the client's sleep moved the HOST to day " + S.day + " (" + gm.ClockString + ")");
                Check(sleepEvents > sl0, "host faded to sleep too");
                yield return Wait(2.5f);
                Check(S.videos.Count == 1 && S.pendingUploads.Count == 0, "the upload resolved overnight (" + S.videos.Count + " video" + (S.videos.Count > 0 ? ": " + S.videos[0].views + " views, +" + S.videos[0].followersGained + " followers" : "") + ")");
                SendVal("hostday", S.day + "|" + S.videos.Count + "|" + S.followers);
                yield return Shot("host_S08_morning");
                yield return Sync("S08b", 40f);
            }
            else
            {
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
                if (bed != null)
                {
                    var bc = bed.GetComponentInChildren<Collider>();
                    yield return AimAt(bc != null ? bc.bounds.center : bed.transform.position + Vector3.up * 0.4f);
                    Info("prompt at the bed: '" + InteractionPrompt.Text + "'");
                    GameInput.SimTap(GameAction.Interact);
                    yield return WaitUntil(() => S.day > day0, 10f);
                    if (S.day <= day0 && bed.CanInteract())
                    {
                        Info("E did not reach the bed – using it directly");
                        bed.Interact();
                    }
                }
                if (S.day <= day0) { Info("fallback: GameManager.SleepToMorning"); gm.SleepToMorning(); }
                yield return WaitUntil(() => S.day > day0, 15f);
                Check(S.day == day0 + 1 && Mathf.Abs(S.timeOfDay - GameManager.MorningTime) < 0.02f, "client woke up on day " + S.day + " at " + gm.ClockString);
                yield return WaitVal("hostday", 20f);
                yield return Wait(0.5f);
                Check(S.videos.Count == 1 && S.pendingUploads.Count == 0, "client sees the resolved video (" + S.videos.Count + " videos, followers " + S.followers + "; host: " + (Val("hostday") ?? "-") + ")");
                yield return Shot("client_S08_morning");
                yield return Sync("S08b", 40f);
            }
        }

        // ================================================================== 09 collectibles
        IEnumerator StepCollectibles()
        {
            yield return Sync("S09a", 120f);
            if (pc == null) yield break;
            if (isHost)
            {
                SetTime(0.45f);
                var list = new List<PropPickup>();
                foreach (var p in Object.FindObjectsByType<PropPickup>(FindObjectsSortMode.None))
                {
                    if (p == null || !p.Available() || p.requireDay3) continue;
                    if (p.kind != CollectibleKind.GoldenShell && p.kind != CollectibleKind.LogbookPage) continue;
                    Vector3 pp = p.transform.position;
                    if (FlatDist(pp, WorldLayout.IslandCenter) > 230f || pp.y < Waves.SeaLevel + 0.2f) continue;
                    list.Add(p);
                }
                Vector3 hut = WorldLayout.Hut;
                list.Sort((x, y) =>
                {
                    int c = FlatDist(x.transform.position, hut).CompareTo(FlatDist(y.transform.position, hut));
                    return c != 0 ? c : string.CompareOrdinal(x.id, y.id);
                });
                Check(list.Count >= 2, list.Count + " land collectibles available");
                if (list.Count < 2) yield break;
                var pa = list[0];
                var pb = list[1];
                SendVal("pick", pa.id + "|" + pb.id);
                int c0 = Collectibles.CountAll;
                yield return WaitPeer(() => Collectibles.IsCollected(pa.id), 60f);
                Check(Collectibles.IsCollected(pa.id) && Collectibles.CountAll == c0 + 1, "the client's pickup '" + pa.id + "' is collected in the host state");
                yield return WaitUntil(() => !pa.Available() && (pa.model == null || pa.keepModel || !pa.model.activeSelf), 4f);
                Check(!pa.Available() && (pa.model == null || pa.keepModel || !pa.model.activeSelf), "it vanished from the host's island");
                yield return CollectSelf(pb);
                SendVal("hostpicked", pb.id + "|" + Collectibles.CountAll);
                yield return Sync("S09b", 60f);
            }
            else
            {
                yield return WaitVal("pick", 30f);
                string pk = Val("pick");
                if (pk == null) { Check(false, "host sent no collectible ids"); yield break; }
                string[] ids = pk.Split('|');
                var pa = FindPickup(ids[0]);
                var pb = ids.Length > 1 ? FindPickup(ids[1]) : null;
                Check(pa != null && pb != null, "both collectibles exist on the client's island (" + pk + ")");
                if (pa == null) yield break;
                yield return CollectSelf(pa);
                yield return WaitVal("hostpicked", 60f);
                string hp = Val("hostpicked");
                if (hp == null || pb == null) { Check(false, "host did not pick up its collectible"); yield break; }
                yield return WaitUntil(() => Collectibles.IsCollected(pb.id), 6f);
                Check(Collectibles.IsCollected(pb.id), "the host's pickup '" + pb.id + "' arrived in the client state");
                yield return WaitUntil(() => !pb.Available() && (pb.model == null || pb.keepModel || !pb.model.activeSelf), 4f);
                Check(!pb.Available() && (pb.model == null || pb.keepModel || !pb.model.activeSelf), "it vanished from the client's island");
                int hostCount = int.Parse(hp.Split('|')[1], Inv);
                Check(Collectibles.CountAll == hostCount, "same collectible count on both (" + Collectibles.CountAll + " / host " + hostCount + ")");
                var hostInfo = Net.Find(0);
                string hn = hostInfo != null ? hostInfo.name + ":" : ":";
                Check(ToastSeen(hn, hn), "client got the toast '" + hn + " …' for the host's find");
                yield return Sync("S09b", 60f);
            }
        }

        static PropPickup FindPickup(string id)
        {
            foreach (var p in Object.FindObjectsByType<PropPickup>(FindObjectsSortMode.None))
                if (p != null && p.id == id) return p;
            return null;
        }

        IEnumerator CollectSelf(PropPickup pick)
        {
            Vector3 target = pick.trigger != null ? pick.trigger.bounds.center : pick.transform.position;
            Vector3 feet = Vector3.zero;
            for (int k = 0; k < 12 && feet == Vector3.zero; k++)
            {
                float a = k * 30f * Mathf.Deg2Rad;
                Vector3 c = target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.5f;
                Vector3 f = SnapDown(c + Vector3.up * 2f, 2f, 6f);
                if (f.y < Waves.SeaLevel + 0.1f || Mathf.Abs(f.y - target.y) > 1.6f) continue;
                if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                feet = f;
            }
            if (feet == Vector3.zero) feet = SnapDown(target + Vector3.up * 1.5f);
            yield return Stand(feet, YawTo(feet, target));
            yield return AimAt(target);
            Info(pick.kind + " '" + pick.id + "' prompt: '" + InteractionPrompt.Text + "'");
            yield return Shot(Role + "_S09_collectible");
            int before = Collectibles.CountAll;
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.6f);
            if (!Collectibles.IsCollected(pick.id))
            {
                Info("E did not pick it up from here – collected via the pickup");
                pick.Interact();
                yield return Wait(0.3f);
            }
            Check(Collectibles.IsCollected(pick.id) && Collectibles.CountAll == before + 1, Role + " collected " + pick.id + " (" + Collectibles.CountAll + " total)");
        }

        // ================================================================== 10 voice
        IEnumerator StepVoice()
        {
            yield return Sync("S10a", 120f);
            if (pc == null) yield break;
            if (Settings.Data.voiceMode == VoiceMode.Off)
            {
                voiceMode0 = Settings.Data.voiceMode;
                voiceModeChanged = true;
                Settings.Data.voiceMode = VoiceMode.PushToTalk;   // in memory only: "Off" would also mute the others
                Info("voice mode was Off – set to push-to-talk for this test (not saved)");
            }
            Vector3 me = isHost ? HostSpot : ClientSpot, other = isHost ? ClientSpot : HostSpot;
            yield return Stand(me, YawTo(me, other));
            yield return Sync("S10b", 40f);
            yield return Wait(1.0f);
            var a = PeerAvatar();
            if (a != null) yield return AimAt(a.HeadPosition);
            Info("microphone: " + (NetVoice.MicAvailable ? NetVoice.MicDevice : "none") + ", output " + AudioSettings.outputSampleRate + " Hz, driver caps " + AudioSettings.driverCapabilities);
            yield return Sync("S10c", 40f);
            // round 1: the client talks, round 2: the host talks
            for (int round = 0; round < 2; round++)
            {
                bool talk = (round == 0) != isHost;
                if (talk) yield return Talk(2.5f);
                else yield return Listen(4.5f, round == 0 ? "host_S10_client_speaking" : "client_S10_host_speaking");
                yield return Sync("S10r" + round, 30f);
            }
            if (voiceModeChanged) { Settings.Data.voiceMode = voiceMode0; voiceModeChanged = false; }
        }

        IEnumerator Talk(float seconds)
        {
            int s0 = NetVoice.FramesSent;
            bool tx = false;
            NetVoice.TestToneHz = isHost ? 523.25f : 440f;
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                if (NetVoice.LocalTransmitting) tx = true;
                yield return null;
            }
            NetVoice.TestToneHz = 0f;
            int sent = NetVoice.FramesSent - s0;
            Check(sent >= Mathf.RoundToInt(seconds * 25f * 0.8f), Role + " sent " + sent + " voice frames of a " + F(seconds) + " s test tone (25 frames/s expected)");
            Check(tx, "LocalTransmitting while the tone runs (speaking indicator for the own player)");
            yield return Wait(2.0f);
        }

        IEnumerator Listen(float seconds, string shotName)
        {
            int f0 = NetVoice.FramesReceived;
            long p0 = NetVoicePlayer.SamplesPlayed;
            bool speaking = false, icon = false, shot = false;
            float t = 0f;
            var a = PeerAvatar();
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                if (NetVoice.IsSpeaking(peerId)) speaking = true;
                if (a != null)
                {
                    var ic = a.transform.Find("Nameplate/Speaking");
                    if (ic != null && ic.gameObject.activeInHierarchy) icon = true;
                }
                if (!shot && t > 1.4f)
                {
                    shot = true;
                    yield return Shot(shotName);
                }
                yield return null;
            }
            int got = NetVoice.FramesReceived - f0;
            long played = NetVoicePlayer.SamplesPlayed - p0;
            Check(got >= 50, Role + " received " + got + " voice frames (~62 sent)");
            Check(played >= NetVoiceCodec.SampleRate, Role + " played " + F(played / (float)NetVoiceCodec.SampleRate, "0.00") + " s of the other player's voice through the 3D voice source");
            Check(speaking, "NetVoice.IsSpeaking(peer) while the tone arrives (mouth flap)");
            Check(icon, "speaker icon on the nameplate");
        }

        // ================================================================== 11 performance + bandwidth
        IEnumerator StepPerf()
        {
            yield return Sync("S11a", 120f);
            if (pc == null) yield break;
            Vector3 me = isHost ? HostSpot : ClientSpot;
            yield return Stand(me, YawTo(me, WorldLayout.Hut));
            yield return AimAt(new Vector3(WorldLayout.Hut.x, me.y + 3f, WorldLayout.Hut.z));
            yield return Sync("S11b", 40f);
            var d = Settings.Data;
            Info("settings: quality " + d.quality + ", fps cap " + d.fpsLimit + ", vsync " + d.vsync + " – both windows share one GPU");
            yield return Measure("island (both windows)", 10f);
            float kbIn = NetDiagnostics.BytesInPerSecond / 1024f, kbOut = NetDiagnostics.BytesOutPerSecond / 1024f;
            Metric("net.idleOnIsland", "in " + F(kbIn, "0.0") + " KB/s, out " + F(kbOut, "0.0") + " KB/s, ping " + NetDiagnostics.PingMs + " ms");
            Metric("net.total", "in " + F(NetDiagnostics.BytesInTotal / 1024f / 1024f, "0.00") + " MB, out " + F(NetDiagnostics.BytesOutTotal / 1024f / 1024f, "0.00") + " MB in " + F((Time.realtimeSinceStartup - startTime) / 60f) + " min, snapshots sent " + NetDiagnostics.SnapshotsSent + " / applied " + NetDiagnostics.SnapshotsApplied + ", commands sent " + NetDiagnostics.CommandsSent + " / executed " + NetDiagnostics.CommandsExecuted + ", unreliable dropped " + NetDiagnostics.UnreliableDropped);
            Metric("net.lastSnapshot", NetDiagnostics.LastSnapshotBytes + " B");
            float peak = 0f;
            foreach (var s in report.steps) peak = Mathf.Max(peak, s.kbPeak);
            Check(peak < 200f, "bandwidth peak " + F(peak, "0.0") + " KB/s (in + out) over all steps");
            Check(NetDiagnostics.PingMs < 100, "ping " + NetDiagnostics.PingMs + " ms on localhost");
            yield return Sync("S11c", 40f);
        }

        IEnumerator Measure(string scene, float seconds)
        {
            var dts = new List<float>(2048);
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt;
                dts.Add(dt);
            }
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
            bool focused = Application.isFocused;
            r.scene += focused ? ", focused window" : ", background window";
            report.fps.Add(r);
            string line = "FPS " + r.scene + " (" + r.quality + "): avg " + F(r.avg, "0") + ", 1% low " + F(r.low1, "0") + ", min " + F(r.min, "0") + " over " + r.frames + " frames";
            // two windows share one GPU here and Windows favours the focused one: only the focused window is held to the bar
            if (focused) Check(r.avg >= 20f, line);
            else Info(line + " – the unfocused window gets what the focused one leaves of the shared GPU (not the case with two PCs)");
        }

        // ================================================================== 12 disconnect
        IEnumerator StepDisconnect()
        {
            yield return Sync("S12a", 120f);
            if (isHost)
            {
                int left = peerId;
                int l0 = leftEvents;
                string pname = Net.Find(left) != null ? Net.Find(left).name : "";
                yield return WaitUntil(() => Net.Find(left) == null, 15f);
                Check(Net.Find(left) == null && Net.Players.Count == 1, "client left the player list (" + Net.Players.Count + " players)");
                yield return WaitUntil(() => !NetAvatarSync.TryGet(left, out _), 6f);
                // Destroy happens at the end of the frame: look for the object a moment later
                yield return WaitUntil(() => GameObject.Find("NetAvatar_" + left) == null, 3f);
                Check(!NetAvatarSync.TryGet(left, out _) && GameObject.Find("NetAvatar_" + left) == null, "the client's avatar was removed on the host");
                Check(Divers.All.Count == 1, "Divers.All back to the host only (" + Divers.All.Count + ")");
                Check(leftEvents > l0, "PlayerLeft raised");
                Check(ToastSeen("von Bord", "left the crew"), "host got the '" + pname + " left the crew' toast");
                Check(GameManager.I.InGame && NetSession.IsHostSide, "host keeps playing and hosting");
                yield return Wait(0.6f);
                yield return Shot("host_S12_after_leave");
            }
            else
            {
                yield return Wait(1.0f);
                Net.Leave?.Invoke();
                yield return WaitUntil(() => UIState.MainMenuOpen && !GameManager.I.InGame && !Net.IsOnline, 15f);
                Check(UIState.MainMenuOpen && !GameManager.I.InGame && !Net.IsOnline, "client left: back in the main menu, offline");
                Check(GameManager.SavingDisabled, "saving still disabled after leaving");
                yield return Wait(1.0f);
                yield return Shot("client_S12_menu");
            }
        }

        // ================================================================== 13 rejoin
        IEnumerator StepRejoin()
        {
            var gm = GameManager.I;
            if (isHost)
            {
                int old = peerId;
                peerId = -1;
                float t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => FindPeerId() >= 0, 90f);
                peerId = FindPeerId();
                Check(peerId >= 0, "the client joined again (drop-in) after " + F(Time.realtimeSinceStartup - t0, "0") + " s as id " + peerId + " (was " + old + ")");
                if (peerId < 0) yield break;
                yield return Sync("S13a", 60f);
                SendVal("state13", StateLine() + ", journal " + Journal.DiscoveredCount + ", collected " + S.collected.Count + ", videos " + S.videos.Count + ", " + gm.ClockString.Substring(0, 2));
                yield return Wait(2.5f);
                var a = PeerAvatar();
                Check(a != null && a.Visible, "the rejoined client's avatar is visible again");
                yield return Sync("S13b", 60f);
            }
            else
            {
                yield return Wait(3f);
                StartJoin(60f);
                float t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => NetSession.Current == NetSession.Phase.Joined && gm.InGame, 70f);
                bool joined = NetSession.Current == NetSession.Phase.Joined && gm.InGame && Net.IsClient;
                Check(joined, "rejoined after " + F(Time.realtimeSinceStartup - t0, "0") + " s as player " + Net.LocalId);
                if (!joined) yield break;
                peerId = 0;
                sessionEnded = false;   // the own leave in step 12 raised SessionEnded too
                sessionEndReason = null;
                BindPlayer();
                yield return Sync("S13a", 60f);
                yield return WaitVal("state13", 30f);
                yield return Wait(1.0f);
                string mine = StateLine() + ", journal " + Journal.DiscoveredCount + ", collected " + S.collected.Count + ", videos " + S.videos.Count + ", " + gm.ClockString.Substring(0, 2);
                string theirs = Val("state13") ?? "";
                Check(mine == theirs, "rejoined client has the current shared state: " + mine + (mine == theirs ? "" : " vs host " + theirs));
                Check(Journal.IsDiscovered("green_turtle") && S.ownedItems.Contains("tank_t1"), "journal entry and bought tank are still there");
                yield return Wait(1.5f);
                var a = PeerAvatar();
                Check(a != null && a.Visible, "host avatar visible after rejoining");
                if (a != null) yield return AimAt(a.HeadPosition);
                yield return Shot("client_S13_rejoined");
                yield return Sync("S13b", 60f);
            }
        }

        // ================================================================== 14 host quits
        IEnumerator StepHostQuit()
        {
            yield return Sync("S14a", 120f);
            if (isHost)
            {
                Check(true, "host quits the game now (Application.Quit → session end for the client)");
                yield return Wait(0.5f);
            }
            else
            {
                float t0 = Time.realtimeSinceStartup;
                yield return WaitUntil(() => sessionEnded && UIState.MainMenuOpen && !GameManager.I.InGame && !Net.IsOnline, 40f);
                Check(sessionEnded, "session ended after the host quit (" + F(Time.realtimeSinceStartup - t0, "0") + " s)");
                Check(UIState.MainMenuOpen && !GameManager.I.InGame && !Net.IsOnline, "client is back in the main menu, offline");
                Check(!string.IsNullOrEmpty(sessionEndReason), "with a message: '" + sessionEndReason + "' (status '" + NetSession.Status + "')");
                yield return Wait(1.2f);
                yield return Shot("client_S14_menu_notice");
            }
        }

        // ================================================================== filming helpers
        IEnumerator FollowFor(ICreature c, float seconds, float dist, bool aim, bool record, string shotName)
        {
            var hc = Refs.HandCamera as FilmHandCamera;
            filmTargeted = false;
            filmRegistered = false;
            filmRegisterTime = -1f;
            filmHints = "";
            if (hc == null || c == null || cam == null) yield break;
            string id = c.Species != null ? c.Species.id : "";
            bool disc0 = Journal.IsDiscovered(id);
            Vector3 approach = Flat(c.Position - cam.transform.position);
            if (aim) GameInput.SimPress(GameAction.Aim);
            if (hc.IsRecording != record) GameInput.SimTap(GameAction.Use);
            float t = 0f;
            bool shot = false;
            var seen = new HashSet<FilmHint>();
            while (t < seconds)
            {
                if (!c.IsAlive) { filmHints += "/LOST"; break; }
                Vector3 center = c.WorldBounds.center;
                Vector3 eye = KeepInWater(center - approach * dist, 0.6f, 0.7f);
                Vector3 d = center - eye;
                if (d.sqrMagnitude < 1e-4f) d = approach;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d.normalized, Vector3.up));
                if (ReferenceEquals(hc.CurrentCreature, c)) filmTargeted = true;
                if (hc.Hint != FilmHint.None && seen.Add(hc.Hint)) filmHints += (filmHints.Length > 0 ? "/" : "") + hc.Hint;
                if (!disc0 && filmRegisterTime < 0f && Journal.IsDiscovered(id)) filmRegisterTime = t;
                if (shotName != null && !shot && t > seconds * 0.4f)
                {
                    shot = true;
                    yield return Shot(shotName);
                }
                yield return null;
                t += Time.deltaTime;
            }
            filmRegistered = !disc0 && Journal.IsDiscovered(id);
        }

        IEnumerator StopFilming()
        {
            var hc = Refs.HandCamera as FilmHandCamera;
            if (hc != null && hc.IsRecording) GameInput.SimTap(GameAction.Use);
            GameInput.SimRelease(GameAction.Aim);
            yield return null;
            yield return null;
            yield return null;
        }

        // ------------------------------------------------------------------ player / camera helpers
        void SetTime(float t)
        {
            var gm = GameManager.I;
            if (gm != null && Net.HasAuthority) gm.State.timeOfDay = t;
        }

        IEnumerator Stand(Vector3 feet, float yaw)
        {
            if (pc == null) yield break;
            pc.enabled = true;
            pc.TeleportTo(feet, yaw);
            yield return Wait(0.7f);
        }

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

        IEnumerator AimAt(Vector3 target) => AimAt(() => target);

        /// Turns the view toward a target that may move (an avatar or seat on a moving boat): re-read every iteration.
        IEnumerator AimAt(Func<Vector3> target)
        {
            if (pc == null || cam == null) yield break;
            pc.enabled = true;
            for (int i = 0; i < 45; i++)
            {
                Transform ct = cam.transform;
                Vector3 d = target() - ct.position;
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

        static Vector3 DockEnd()
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

        static string V(Vector3 v) => "(" + F(v.x, "0.0") + "," + F(v.y, "0.0") + "," + F(v.z, "0.0") + ")";

        static string Vec(Vector3 v) => v.x.ToString("R", Inv) + "|" + v.y.ToString("R", Inv) + "|" + v.z.ToString("R", Inv);

        static Vector3 ParseVec(string s)
        {
            if (string.IsNullOrEmpty(s)) return Vector3.zero;
            var p = s.Split('|');
            if (p.Length < 3) return Vector3.zero;
            return new Vector3(float.Parse(p[0], Inv), float.Parse(p[1], Inv), float.Parse(p[2], Inv));
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

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
            return new Vector3(center.x, WorldInfo.GroundHeight(center), center.z);
        }

        static Vector3 KeepInWater(Vector3 p, float aboveFloor, float belowSurface)
        {
            float g = WorldInfo.GroundHeight(p);
            float top = Waves.SeaLevel - belowSurface;
            p.y = Mathf.Max(p.y, g + aboveFloor);
            if (p.y > top) p.y = top;
            return p;
        }

        static HeliCrate FindCrate(string itemId)
        {
            foreach (var c in Object.FindObjectsByType<HeliCrate>(FindObjectsSortMode.None))
            {
                if (c == null || c.Delivery == null || c.Delivery.itemId != itemId) continue;
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
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                report.screenshots++;
                if (cur != null) cur.screenshots += (cur.screenshots.Length > 0 ? ", " : "") + name + ".png";
                Note("captured " + name + ".png");
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

        // ------------------------------------------------------------------ the player's files (host)
        void BackupFiles()
        {
            try
            {
                settingsExisted = File.Exists(SettingsPath);
                settingsBackup = settingsExisted ? File.ReadAllText(SettingsPath) : null;
                saveExisted = File.Exists(SavePath);
                saveBackup = saveExisted ? File.ReadAllText(SavePath) : null;
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytestMP: backup failed: " + e.Message); }
        }

        void RestoreFiles()
        {
            if (!isHost) return;
            try
            {
                string nowSettings = File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath) : null;
                if (nowSettings != settingsBackup)
                {
                    Note("settings.json changed during the test – restoring the player's file");
                    if (settingsExisted) File.WriteAllText(SettingsPath, settingsBackup);
                    else if (File.Exists(SettingsPath)) File.Delete(SettingsPath);
                }
                string nowSave = File.Exists(SavePath) ? File.ReadAllText(SavePath) : null;
                if (nowSave != saveBackup)
                {
                    Note("WARNING: the save changed during the test – restoring the player's save");
                    if (saveExisted) File.WriteAllText(SavePath, saveBackup);
                    else if (File.Exists(SavePath)) File.Delete(SavePath);
                }
                else Note("the player's save and settings are untouched");
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytestMP: restore failed: " + e.Message); }
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
            report.transport = NetDiagnostics.Transport;
            report.bytesInTotal = NetDiagnostics.BytesInTotal;
            report.bytesOutTotal = NetDiagnostics.BytesOutTotal;
            try
            {
                File.WriteAllText(Path.Combine(outDir, "mp_" + Role + ".json"), JsonUtility.ToJson(report, true));
                File.WriteAllText(Path.Combine(outDir, "mp_" + Role + "_report.md"), Markdown());
                File.WriteAllText(Path.Combine(outDir, "mp_" + Role + "_log.txt"), log.ToString());
            }
            catch (Exception e) { Debug.LogWarning("AutoPlaytestMP: could not write the report: " + e.Message); }
        }

        string Markdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SEALEBRITIES – automated co-op playtest (" + Role + ")");
            sb.AppendLine();
            sb.AppendLine("- Started: " + report.started + (report.finished != null ? " · finished: " + report.finished : " · (running)"));
            sb.AppendLine("- Build: " + report.version + " · " + report.resolution + " · " + report.device);
            sb.AppendLine("- Boot: " + F(report.bootSeconds) + " s" + (report.bootErrors ? " (with errors)" : ""));
            sb.AppendLine("- Result: **" + report.passed + " passed, " + report.failed + " failed, " + report.skipped + " skipped** · " +
                          report.loggedErrors + " logged errors (" + report.loggedExceptions + " exceptions) · " + report.screenshots + " screenshots · " + F(report.totalSeconds / 60f) + " min");
            sb.AppendLine("- Network: " + report.transport + " · total in " + F(report.bytesInTotal / 1024f, "0") + " KB, out " + F(report.bytesOutTotal / 1024f, "0") + " KB");
            sb.AppendLine();
            sb.AppendLine("| Step | Result | Time | FPS | Net in/out KB/s (peak) | Errors | Screenshots |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var s in report.steps)
                sb.AppendLine("| " + s.name + " | " + (s.skipped ? "SKIPPED" : s.pass ? "PASS" : "**FAIL**") + " | " + F(s.seconds, "0") + " s | " + F(s.fps, "0") + " | "
                              + F(s.kbInAvg, "0.0") + " / " + F(s.kbOutAvg, "0.0") + " (" + F(s.kbPeak, "0.0") + ") | " + s.errors + " | " + s.screenshots + " |");
            sb.AppendLine();
            if (report.fps.Count > 0)
            {
                sb.AppendLine("## FPS");
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
            NetVoice.TestToneHz = 0f;
            if (voiceModeChanged) { Settings.Data.voiceMode = voiceMode0; voiceModeChanged = false; }
            report.finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv);
            Note("done: " + report.passed + " passed, " + report.failed + " failed, " + report.skipped + " skipped, " + errorCount + " logged errors");
            WriteReport();
            if (pc != null) pc.enabled = true;
            RestoreFiles();
            DeleteTestPhotos();
            WriteReport();
            Application.Quit(code);
        }
    }
}
