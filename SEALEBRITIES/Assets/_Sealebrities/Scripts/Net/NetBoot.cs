using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Installs the co-op module (no scene object or bootstrap change needed) and handles the test command line:
    ///   -host [port]            host automatically after boot (port default 27015) and start a NEW game.
    ///                           A command line test game is NOT saved, so it never overwrites the real save –
    ///                           add -save to save it anyway.
    ///   -continue               with -host: continue the save instead (saves normally).
    ///   -join ip[:port]         connect automatically after boot (retries for 60 s while the host is still booting).
    ///   -netname Name           co-op name for this window (not written to the settings).
    ///   -netbind ip             host listens only on this address (e.g. 127.0.0.1 for a same-PC test).
    ///   -netdifficulty X        Snorkeler|OpenWater|Advanced (also Entspannt|Normal|Schwer / Relaxed|Hard).
    /// Two windows on one PC: SEALEBRITIES.exe -host -netname Anna -screen-fullscreen 0 -screen-width 1280 -screen-height 720
    ///                        SEALEBRITIES.exe -join 127.0.0.1 -netname Ben -screen-fullscreen 0 -screen-width 1280 -screen-height 720
    public static class NetBoot
    {
        public static string NameOverride { get; private set; }
        public static string BindOverride { get; private set; }
        public static bool AutoHost { get; private set; }
        public static bool AutoJoin { get; private set; }

        static int autoPort = Net.DefaultPort;
        static string joinAddress;
        static bool autoContinue, autoSave, autoDone, installed;
        static Difficulty autoDifficulty = Difficulty.OpenWater;
        static float readySince = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            NameOverride = null; BindOverride = null; AutoHost = false; AutoJoin = false;
            autoPort = Net.DefaultPort; joinAddress = null; autoContinue = false; autoSave = false; autoDone = false;
            autoDifficulty = Difficulty.OpenWater; readySince = -1f; installed = false;
        }

        /// Installs the co-op module now (idempotent). GameBootstrap calls it in Awake right after the settings are
        /// loaded; the AfterSceneLoad hook below is only the safety net for scenes without the bootstrap.
        public static void EnsureInstalled() => Install();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (installed) return;
            installed = true;
            try { ParseCommandLine(Environment.GetCommandLineArgs()); }
            catch (Exception e) { Debug.LogWarning("[Net] command line: " + e.Message); }
            NetCoreRunner.Ensure();
            NetSession.Install();
            NetCommands.Install();
            NetState.Install();
            NetLobby.Install();
            Net.OpenLobby = NetLobby.Open;
            Net.Leave = NetSession.LeaveFromUI;
            if (!string.IsNullOrWhiteSpace(NameOverride)) Net.LocalName = NameOverride;
            if (AutoHost || AutoJoin)
                Debug.Log("[Net] command line: " + (AutoHost ? "host port " + autoPort : "join " + joinAddress) +
                          (NameOverride != null ? ", name " + NameOverride : "") + (BindOverride != null ? ", bind " + BindOverride : ""));
        }

        static void ParseCommandLine(string[] args)
        {
            if (args == null) return;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i] ?? "";
                string next = i + 1 < args.Length ? args[i + 1] : null;
                bool nextIsValue = next != null && !next.StartsWith("-", StringComparison.Ordinal);
                switch (a.ToLowerInvariant())
                {
                    case "-host":
                        AutoHost = true;
                        if (nextIsValue && int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) && p > 0 && p < 65536)
                        {
                            autoPort = p;
                            i++;
                        }
                        break;
                    case "-netport":
                        if (nextIsValue && int.TryParse(next, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p2) && p2 > 0 && p2 < 65536)
                        {
                            autoPort = p2;
                            i++;
                        }
                        break;
                    case "-join":
                        if (nextIsValue)
                        {
                            AutoJoin = true;
                            joinAddress = next;
                            i++;
                        }
                        break;
                    case "-netname":
                        if (next != null)
                        {
                            NameOverride = next.Trim();
                            if (NameOverride.Length > Settings.MaxNameLength) NameOverride = NameOverride.Substring(0, Settings.MaxNameLength);
                            i++;
                        }
                        break;
                    case "-netbind":
                        if (nextIsValue)
                        {
                            BindOverride = next.Trim();
                            i++;
                        }
                        break;
                    case "-netdifficulty":
                        if (nextIsValue)
                        {
                            autoDifficulty = ParseDifficulty(next);
                            i++;
                        }
                        break;
                    case "-continue":
                        autoContinue = true;
                        break;
                    case "-save":
                        autoSave = true;
                        break;
                }
            }
            if (AutoHost && AutoJoin) AutoJoin = false;   // hosting wins
        }

        static Difficulty ParseDifficulty(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "snorkeler":
                case "entspannt":
                case "relaxed":
                case "easy":
                case "0":
                    return Difficulty.Snorkeler;
                case "advanced":
                case "schwer":
                case "hard":
                case "2":
                    return Difficulty.Advanced;
                default:
                    if (Enum.TryParse(s, true, out Difficulty d)) return DifficultySettings.Normalize(d);
                    return Difficulty.OpenWater;
            }
        }

        /// Called every frame by NetCoreRunner: runs the command line action once the main menu is up.
        internal static void Tick(float dt)
        {
            if (autoDone || (!AutoHost && !AutoJoin)) return;
            if (!BootState.Ready || UIState.LoadingOpen || !UIState.MainMenuOpen || UIModule.Controller == null)
            {
                readySince = -1f;
                return;
            }
            if (readySince < 0f) readySince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - readySince < 0.75f) return;
            autoDone = true;
            if (AutoHost) RunAutoHost();
            else RunAutoJoin();
        }

        static void RunAutoHost()
        {
            if (!NetSession.StartHost(autoPort, BindOverride, out string err))
            {
                NetLobby.ShowNotice(Loc.Tr("Hosten fehlgeschlagen", "Hosting failed"), err);
                return;
            }
            bool cont = autoContinue && GameManager.HasSave;
            if (!cont && !autoSave) NetSession.SuppressSaving();
            NetLobby.StartHostedGame(cont, autoDifficulty);
        }

        static void RunAutoJoin()
        {
            if (!NetSession.TryParseAddress(joinAddress, out string host, out int port, out bool explicitPort, out string err))
            {
                NetLobby.ShowNotice(Loc.Tr("Beitreten fehlgeschlagen", "Joining failed"), err);
                return;
            }
            NetSession.EnableAutoRetry(host, port, explicitPort, 60f);
            NetSession.JoinHostPort(host, port, explicitPort);
            NetLobby.OpenConnecting();
        }
    }
}
