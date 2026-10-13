#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX || !UNITY_5_3_OR_NEWER) && !DISABLESTEAMWORKS
#define SB_STEAM
#endif

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if SB_STEAM
using Steamworks;
#endif

namespace Sealebrities
{
    /// Steam check without a second PC. Does nothing unless the game is started with
    ///     SEALEBRITIES.exe -steamselftest "C:/some/output/folder"
    /// Steps: SteamAPI init (no RestartAppIfNecessary) → logged on? app id → relay network access → P2P listen socket →
    /// a PRIVATE lobby (k_ELobbyTypePrivate, 1 slot): LobbyCreated_t confirmed, member count + owner read, lobby data
    /// round trip, lobby left again → SteamNetTransport loopback over an in-process socket pair (1.2 MB fragmented
    /// reliable payload, an unreliable frame, a reply, clean close) → <folder>/steam_selftest.json → quit
    /// (exit code 0 = pass, 1 = fail, 2 = no output folder).
    /// Never invites anyone, never sets rich presence, never writes persona names or Steam ids. Saving is disabled.
    public sealed class SteamSelfTest : MonoBehaviour
    {
        const float Watchdog = 150f;

        [Serializable]
        public class Report
        {
            public string started;
            public string finished;
            public string gameVersion;
            public string steamworksNet;
            public bool pass;
            public float totalSeconds;
            // init
            public bool initOk;
            public string initResult;
            public string initMessage;
            public float initSeconds;
            public bool loggedOn;
            public uint appId;
            public bool overlayEnabled;
            public bool personaNameAvailable;
            // relay network
            public string relayStatus;
            public float relaySeconds;
            // P2P listen socket (the host side of SteamNetTransport)
            public bool listenSocketOk;
            // private lobby
            public bool lobbyCreateCalled;
            public bool lobbyCallbackReceived;
            public string lobbyResult;
            public float lobbyCallbackSeconds;
            public bool lobbyCreated;
            public int lobbyMemberCount;
            public bool lobbyOwnerIsSelf;
            public bool lobbyDataRoundTrip;
            public bool lobbyLeft;
            // transport loopback (in-process socket pair)
            public bool loopbackPairCreated;
            public bool loopbackNetworkLoopback;
            public bool loopbackConnected;
            public bool loopbackBigReliableOk;
            public int loopbackBigBytes;
            public float loopbackBigSeconds;
            public bool loopbackUnreliableArrived;
            public bool loopbackReplyOk;
            public bool loopbackCloseSeen;
            public bool loopbackOk;
            public List<string> errors = new List<string>();
            public List<string> notes = new List<string>();
        }

        public static bool Active { get; private set; }

        string outDir;
        readonly Report report = new Report();
        float startTime;
        bool finished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = false; }

        // ------------------------------------------------------------------ activation (command line only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Activate()
        {
            string dir = ParseOutputDir(Environment.GetCommandLineArgs());
            if (dir == null) return;
            Active = true;
            GameManager.SavingDisabled = true;     // never touch the player's save
            Settings.WritesDisabled = true;        // nor settings.json (the player's game may run at the same time)
            GameManager.AutomatedRun = true;
            Application.runInBackground = true;
            // before GameBootstrap (which skips Steam for automated runs)
            SteamService.Init(true);
            var go = new GameObject("SB_SteamSelfTest");
            DontDestroyOnLoad(go);
            go.AddComponent<SteamSelfTest>().outDir = dir;
        }

        static string ParseOutputDir(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-steamselftest", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-"))
                    return args[i + 1].Trim().Trim('"');
                Debug.LogError("SteamSelfTest: -steamselftest needs an output folder, e.g. -steamselftest C:/steamtest");
                return null;
            }
            return null;
        }

        void Start()
        {
            startTime = Time.realtimeSinceStartup;
            report.started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            report.gameVersion = Application.version;
            try { Directory.CreateDirectory(outDir); }
            catch (Exception e)
            {
                Debug.LogError("SteamSelfTest: cannot create output folder: " + e.Message);
                finished = true;
                Application.Quit(2);
                return;
            }
            StartCoroutine(Run());
        }

        void Update()
        {
            if (finished || Time.realtimeSinceStartup - startTime < Watchdog) return;
            report.errors.Add("watchdog: the self test did not finish within " + Watchdog + " s");
            StopAllCoroutines();
            Finish();
        }

        float Since(float t0) => Time.realtimeSinceStartup - t0;

        IEnumerator Run()
        {
            yield return null;
#if SB_STEAM
            report.steamworksNet = Steamworks.Version.SteamworksNETVersion;
            // ---------------------------------------------------------- 1. init
            report.initOk = SteamService.Initialized;
            report.initResult = SteamService.InitResult;
            report.initMessage = SteamService.InitMessage;
            report.initSeconds = SteamService.InitSeconds;
            if (!report.initOk)
            {
                report.errors.Add("SteamAPI init failed (" + report.initResult + ") – is Steam running and logged in? steam_appid.txt (480) next to the exe?");
                Finish();
                yield break;
            }
            try
            {
                report.loggedOn = SteamUser.BLoggedOn();
                report.appId = SteamService.AppId;
                report.personaNameAvailable = !string.IsNullOrEmpty(SteamService.PersonaName);
            }
            catch (Exception e) { report.errors.Add("steam user: " + e.GetType().Name); }
            if (!report.loggedOn) report.errors.Add("Steam is running but not logged on (offline mode) – lobbies and the relay need Steam online");
            if (report.appId != SteamService.TestAppId) report.notes.Add("app id is " + report.appId + " (expected the test app " + SteamService.TestAppId + ")");

            // ---------------------------------------------------------- 2. relay network
            float t0 = Time.realtimeSinceStartup;
            ESteamNetworkingAvailability avail = ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown;
            try { SteamNetworkingUtils.InitRelayNetworkAccess(); }
            catch (Exception e) { report.errors.Add("relay init: " + e.GetType().Name); }
            while (Since(t0) < 25f)
            {
                try { avail = SteamNetworkingUtils.GetRelayNetworkStatus(out SteamRelayNetworkStatus_t _); }
                catch (Exception) { avail = ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Unknown; }
                if (avail == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current
                    || avail == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Failed
                    || avail == ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_CannotTry) break;
                yield return null;
            }
            report.relayStatus = avail.ToString().Replace("k_ESteamNetworkingAvailability_", "");
            report.relaySeconds = Since(t0);
            if (avail != ESteamNetworkingAvailability.k_ESteamNetworkingAvailability_Current)
                report.errors.Add("relay network not ready (" + report.relayStatus + ")");
            report.overlayEnabled = SteamService.OverlayEnabled;

            // ---------------------------------------------------------- 3. P2P listen socket
            var listenTest = new SteamNetTransport();
            try
            {
                listenTest.StartSteamServer();
                report.listenSocketOk = true;
            }
            catch (Exception e) { report.errors.Add("P2P listen socket: " + e.Message); }
            finally { listenTest.Shutdown(0); }

            // ---------------------------------------------------------- 4. private lobby
            yield return PrivateLobby();

            // ---------------------------------------------------------- 5. transport loopback
            yield return Loopback();

            report.pass = report.initOk && report.loggedOn && report.listenSocketOk && report.lobbyCreated && report.lobbyMemberCount == 1
                          && report.lobbyLeft && report.loopbackOk;
#else
            report.initResult = SteamService.InitResult;
            report.errors.Add("Steamworks is not compiled in for this platform");
#endif
            Finish();
        }

#if SB_STEAM
        IEnumerator PrivateLobby()
        {
            bool got = false, io = false;
            LobbyCreated_t result = default;
            var cr = CallResult<LobbyCreated_t>.Create((r, failure) => { result = r; io = failure; got = true; });
            SteamAPICall_t call = SteamAPICall_t.Invalid;
            float t0 = Time.realtimeSinceStartup;
            try
            {
                // private, ONE slot: nobody can ever join this lobby; nobody is invited
                call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePrivate, 1);
                report.lobbyCreateCalled = call != SteamAPICall_t.Invalid;
                if (report.lobbyCreateCalled) cr.Set(call);
            }
            catch (Exception e) { report.errors.Add("CreateLobby: " + e.GetType().Name); }
            if (!report.lobbyCreateCalled)
            {
                report.errors.Add("CreateLobby returned no call handle");
                cr.Dispose();
                yield break;
            }
            while (!got && Since(t0) < 25f) yield return null;
            report.lobbyCallbackReceived = got;
            report.lobbyCallbackSeconds = Since(t0);
            if (!got)
            {
                cr.Cancel();
                cr.Dispose();
                report.errors.Add("no LobbyCreated_t callback within 25 s");
                yield break;
            }
            cr.Dispose();
            report.lobbyResult = io ? "io failure" : result.m_eResult.ToString().Replace("k_EResult", "");
            report.lobbyCreated = !io && result.m_eResult == EResult.k_EResultOK && result.m_ulSteamIDLobby != 0;
            if (!report.lobbyCreated)
            {
                report.errors.Add("private lobby not created (" + report.lobbyResult + ")");
                yield break;
            }
            var lobby = new CSteamID(result.m_ulSteamIDLobby);
            try
            {
                report.lobbyMemberCount = SteamMatchmaking.GetNumLobbyMembers(lobby);
                report.lobbyOwnerIsSelf = SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID();
                SteamMatchmaking.SetLobbyData(lobby, "sb_selftest", "1");
                report.lobbyDataRoundTrip = SteamMatchmaking.GetLobbyData(lobby, "sb_selftest") == "1";
            }
            catch (Exception e) { report.errors.Add("lobby read: " + e.GetType().Name); }
            if (report.lobbyMemberCount != 1) report.errors.Add("lobby member count " + report.lobbyMemberCount + " (expected 1)");
            yield return null;
            try
            {
                SteamMatchmaking.LeaveLobby(lobby);
                report.lobbyLeft = true;
            }
            catch (Exception e) { report.errors.Add("LeaveLobby: " + e.GetType().Name); }
        }

        IEnumerator Loopback()
        {
            HSteamNetConnection a = HSteamNetConnection.Invalid, b = HSteamNetConnection.Invalid;
            try
            {
                var id1 = new SteamNetworkingIdentity();
                id1.SetSteamID(SteamUser.GetSteamID());
                var id2 = id1;
                report.loopbackPairCreated = SteamNetworkingSockets.CreateSocketPair(out a, out b, true, ref id1, ref id2);
                report.loopbackNetworkLoopback = report.loopbackPairCreated;
                if (!report.loopbackPairCreated)
                {
                    report.notes.Add("socket pair over UDP loopback failed – trying the in-memory pair");
                    report.loopbackPairCreated = SteamNetworkingSockets.CreateSocketPair(out a, out b, false, ref id1, ref id2);
                }
            }
            catch (Exception e) { report.errors.Add("CreateSocketPair: " + e.GetType().Name); }
            if (!report.loopbackPairCreated)
            {
                report.errors.Add("could not create a Steam socket pair");
                yield break;
            }

            var host = new SteamNetTransport();
            var client = new SteamNetTransport();
            try
            {
                host.AttachForSelfTest(a, true);
                client.AttachForSelfTest(b, false);
            }
            catch (Exception e)
            {
                report.errors.Add("attach: " + e.Message);
                host.Shutdown(0);
                client.Shutdown(0);
                yield break;
            }

            var big = Pattern(1200000, 7);
            var small = Pattern(300, 11);
            var reply = Pattern(5000, 13);
            const byte TBig = 200, TSmall = 201, TReply = 202;
            float t0 = Time.realtimeSinceStartup;
            client.Send(0, TBig, 1, big, true);
            client.Send(0, TSmall, 1, small, false);
            host.Send(1, TReply, 0, reply, true);

            bool hostConnected = false, clientConnected = false, gotBig = false, gotSmall = false, gotReply = false;
            float bigAt = -1f;
            while (Since(t0) < 15f && !(gotBig && gotReply && (gotSmall || Since(t0) > 3f)))
            {
                while (host.Poll(out var e))
                {
                    if (e.kind == NetTransportEventKind.Connected) hostConnected = true;
                    else if (e.kind == NetTransportEventKind.Data && e.type == TBig)
                    {
                        gotBig = e.sender == 1 && e.connection == 1 && Same(e.payload, big);
                        bigAt = Since(t0);
                    }
                    else if (e.kind == NetTransportEventKind.Data && e.type == TSmall) gotSmall = Same(e.payload, small);
                }
                while (client.Poll(out var e))
                {
                    if (e.kind == NetTransportEventKind.Connected) clientConnected = true;
                    else if (e.kind == NetTransportEventKind.Data && e.type == TReply) gotReply = e.sender == 0 && Same(e.payload, reply);
                }
                yield return null;
            }

            // clean close: the host must see the client leave
            client.Disconnect(0, "selftest done");
            bool hostSawClose = false;
            float t1 = Time.realtimeSinceStartup;
            while (Since(t1) < 6f && !hostSawClose)
            {
                while (client.Poll(out _)) { }
                while (host.Poll(out var e)) if (e.kind == NetTransportEventKind.Disconnected && e.connection == 1) hostSawClose = true;
                yield return null;
            }
            host.Shutdown(0);
            client.Shutdown(0);

            report.loopbackConnected = hostConnected && clientConnected;
            report.loopbackBigReliableOk = gotBig;
            report.loopbackBigBytes = big.Length;
            report.loopbackBigSeconds = bigAt;
            report.loopbackUnreliableArrived = gotSmall;
            report.loopbackReplyOk = gotReply;
            report.loopbackCloseSeen = hostSawClose;
            report.loopbackOk = report.loopbackConnected && gotBig && gotReply;
            if (!gotBig) report.errors.Add("loopback: fragmented 1.2 MB reliable payload did not arrive intact");
            if (!gotReply) report.errors.Add("loopback: host → client reply did not arrive");
            if (!gotSmall) report.notes.Add("loopback: the unreliable frame did not arrive (allowed – unreliable)");
            if (!hostSawClose) report.notes.Add("loopback: the host did not see the close within 6 s");
        }

        static byte[] Pattern(int n, int seed)
        {
            var b = new byte[n];
            for (int i = 0; i < n; i++) b[i] = (byte)(i * 31 + seed + (i >> 9));
            return b;
        }

        static bool Same(byte[] x, byte[] y)
        {
            if (x == null || y == null || x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }
#endif

        void Finish()
        {
            if (finished) return;
            finished = true;
            report.finished = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            report.totalSeconds = Time.realtimeSinceStartup - startTime;
            string path = Path.Combine(outDir, "steam_selftest.json");
            try { File.WriteAllText(path, JsonUtility.ToJson(report, true)); }
            catch (Exception e) { Debug.LogError("SteamSelfTest: cannot write " + path + ": " + e.Message); }
            Debug.Log("SteamSelfTest: " + (report.pass ? "PASS" : "FAIL") + " (init " + report.initResult + ", lobby " + (report.lobbyCreated ? "ok" : "no") +
                      ", relay " + report.relayStatus + ", loopback " + (report.loopbackOk ? "ok" : "no") + ") → " + path);
            Application.Quit(report.pass ? 0 : 1);
        }
    }
}
