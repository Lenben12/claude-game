#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX || !UNITY_5_3_OR_NEWER) && !DISABLESTEAMWORKS
#define SB_STEAM
#endif

using System;
using UnityEngine;
#if SB_STEAM
using Steamworks;
#endif

namespace Sealebrities
{
    /// Steamworks lifetime for co-op over Steam. Until SEALEBRITIES has its own Steam app id the game runs as Valve's
    /// test app 480 ("Spacewar"): nothing has to be published, Steam shows the player as "playing Spacewar".
    ///   Init  – GameBootstrap.Awake, before the co-op module installs (NetBoot). SteamAPI.InitEx WITHOUT
    ///           RestartAppIfNecessary, so the game keeps starting straight from the .exe / the editor. Needs the Steam
    ///           client running + logged in and steam_appid.txt (480) next to the exe / in the project root
    ///           (SealebritiesSetup writes both). Fails quietly: Available stays false, co-op per IP / LAN keeps working.
    ///           Then SteamNetworkingUtils.InitRelayNetworkAccess (warms up the relay network for the first P2P connect).
    ///   Frame – SteamAPI.RunCallbacks every frame (SteamServiceRunner, execution order -950: before the co-op pump at -800).
    ///   Quit  – co-op goodbye + transport flush (NetSession.OnQuit), lobby left + rich presence cleared, SteamAPI.Shutdown.
    /// Skipped for the automated test runs (-autoplaytest, -autoplaytest-mp, -autoshot set GameManager.AutomatedRun)
    /// and with -nosteam. -steamselftest initializes Steam on its own (Debug/SteamSelfTest).
    /// Privacy: the persona name and Steam IDs are never written to the log or to test reports.
    public static class SteamService
    {
        public const uint TestAppId = 480;

        static bool initialized, attempted;
        static string personaName = "";

        /// SteamAPI is initialized in this process (Steam client running).
        public static bool Initialized => initialized;
        /// Short init result for diagnostics: "OK", "NoSteamClient", "VersionMismatch", "FailedGeneric",
        /// "DllNotFound", "disabled", "unsupported" … ("" before Init ran).
        public static string InitResult { get; private set; } = "";
        /// Steam's own (English) explanation when Init failed; contains no personal data.
        public static string InitMessage { get; private set; } = "";
        /// Real seconds Init took.
        public static float InitSeconds { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            initialized = false; attempted = false; personaName = "";
            InitResult = ""; InitMessage = ""; InitSeconds = 0f;
        }

        /// Steam can be used for co-op right now: initialized AND logged on (lobbies and the relay need Steam's servers).
        public static bool Available
        {
            get
            {
#if SB_STEAM
                if (!initialized) return false;
                try { return SteamUser.BLoggedOn(); }
                catch (Exception) { return false; }
#else
                return false;
#endif
            }
        }

        /// Steam runs, but the user is offline / not logged on (Steam offline mode).
        public static bool OfflineMode => initialized && !Available;

        /// The player's Steam name ("" when Steam is not running). Only used as the default co-op name – never logged.
        public static string PersonaName => personaName ?? "";

        /// The Steam overlay hooked into the game (Shift+Tab works). Usually false in the editor.
        public static bool OverlayEnabled
        {
            get
            {
#if SB_STEAM
                if (!initialized) return false;
                try { return SteamUtils.IsOverlayEnabled(); }
                catch (Exception) { return false; }
#else
                return false;
#endif
            }
        }

        public static uint AppId
        {
            get
            {
#if SB_STEAM
                if (!initialized) return 0;
                try { return SteamUtils.GetAppID().m_AppId; }
                catch (Exception) { return 0; }
#else
                return 0;
#endif
            }
        }

        /// Own Steam id (0 when unavailable). Never log it.
        public static ulong LocalSteamId
        {
            get
            {
#if SB_STEAM
                if (!initialized) return 0;
                try { return SteamUser.GetSteamID().m_SteamID; }
                catch (Exception) { return 0; }
#else
                return 0;
#endif
            }
        }

        /// Localized one-line reason why Steam co-op is not offered (lobby screen).
        public static string UnavailableText()
        {
            if (initialized && !Available)
                return Loc.Tr("Steam ist im Offline-Modus – bitte in Steam online gehen.", "Steam is in offline mode – please go online in Steam.");
            switch (InitResult)
            {
                case "disabled": return Loc.Tr("Steam ist für diesen Start abgeschaltet (-nosteam / Testlauf).", "Steam is switched off for this launch (-nosteam / test run).");
                case "VersionMismatch": return Loc.Tr("Der Steam-Client ist veraltet – bitte Steam aktualisieren.", "The Steam client is out of date – please update Steam.");
                case "DllNotFound": return Loc.Tr("Steam-Bibliothek (steam_api64.dll) fehlt.", "Steam library (steam_api64.dll) is missing.");
                case "unsupported": return Loc.Tr("Steam wird auf dieser Plattform nicht unterstützt.", "Steam is not supported on this platform.");
                default: return Loc.Tr("Steam läuft nicht oder du bist nicht angemeldet – Steam starten und das Spiel neu öffnen.", "Steam is not running or you are not logged in – start Steam and restart the game.");
            }
        }

        static bool HasArg(string flag)
        {
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                    if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }
            return false;
        }

        /// Initializes Steamworks once per run (idempotent). forAutomatedRun: also for automated test runs (only the
        /// Steam self test passes true).
        public static void Init() => Init(false);

        public static void Init(bool forAutomatedRun)
        {
            if (attempted) return;
            if (!forAutomatedRun && (GameManager.AutomatedRun || HasArg("-nosteam")))
            {
                attempted = true;
                InitResult = "disabled";
                Debug.Log("[Steam] skipped for this launch (automated test run / -nosteam) – co-op per IP / LAN only.");
                return;
            }
            attempted = true;
#if SB_STEAM
            float t0 = Time.realtimeSinceStartup;
            try
            {
                if (!Packsize.Test()) Debug.LogWarning("[Steam] Steamworks.NET packsize test failed – wrong Steamworks.NET build for this platform.");
                var r = SteamAPI.InitEx(out string msg);
                InitMessage = msg ?? "";
                InitResult = ShortResult(r);
                if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
                {
                    Debug.Log("[Steam] not available (" + InitResult + (string.IsNullOrEmpty(InitMessage) ? "" : ": " + InitMessage) +
                              ") – co-op per IP / LAN still works.");
                    return;
                }
            }
            catch (DllNotFoundException)
            {
                InitResult = "DllNotFound";
                Debug.LogWarning("[Steam] steam_api64.dll not found – co-op per IP / LAN only.");
                return;
            }
            catch (Exception e)
            {
                InitResult = "Exception";
                InitMessage = e.GetType().Name;
                Debug.LogWarning("[Steam] init failed (" + e.GetType().Name + ") – co-op per IP / LAN only.");
                return;
            }
            finally
            {
                InitSeconds = Time.realtimeSinceStartup - t0;
            }

            initialized = true;
            CallbackDispatcher.ExceptionHandler = OnCallbackException;
            try { personaName = SteamFriends.GetPersonaName() ?? ""; } catch (Exception) { personaName = ""; }
            try { SteamNetworkingUtils.InitRelayNetworkAccess(); }
            catch (Exception e) { Debug.LogWarning("[Steam] relay network init failed: " + e.Message); }
            SteamServiceRunner.Ensure();
            try { SteamLobby.Install(); }
            catch (Exception e) { Debug.LogException(e); }
            bool loggedOn = Available;
            Debug.Log("[Steam] initialized (app " + AppId + (loggedOn ? ", online" : ", OFFLINE – Steam co-op needs Steam online") +
                      ", " + InitSeconds.ToString("0.00") + " s).");
#else
            InitResult = "unsupported";
#endif
        }

#if SB_STEAM
        static string ShortResult(ESteamAPIInitResult r)
        {
            switch (r)
            {
                case ESteamAPIInitResult.k_ESteamAPIInitResult_OK: return "OK";
                case ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient: return "NoSteamClient";
                case ESteamAPIInitResult.k_ESteamAPIInitResult_VersionMismatch: return "VersionMismatch";
                case ESteamAPIInitResult.k_ESteamAPIInitResult_FailedGeneric: return "FailedGeneric";
                default: return r.ToString();
            }
        }

        static void OnCallbackException(Exception e) => Debug.LogException(e);
#endif

        /// Called every frame by SteamServiceRunner (before the co-op pump).
        internal static void RunCallbacks()
        {
#if SB_STEAM
            if (!initialized) return;
            SteamAPI.RunCallbacks();
#endif
        }

        /// Application quit / runner destroyed: say goodbye on the co-op connection while Steam is still up, leave the
        /// lobby, clear the rich presence, shut Steamworks down. Safe to call twice.
        internal static void Shutdown()
        {
            if (!initialized) return;
            try { NetSession.OnQuit(); } catch (Exception e) { Debug.LogException(e); }
            try { SteamLobby.Shutdown(); } catch (Exception e) { Debug.LogException(e); }
            initialized = false;
#if SB_STEAM
            try { SteamAPI.Shutdown(); }
            catch (Exception e) { Debug.LogWarning("[Steam] shutdown: " + e.Message); }
#endif
            Debug.Log("[Steam] shut down.");
        }
    }
}
