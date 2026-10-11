#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX || UNITY_STANDALONE_OSX || STEAMWORKS_WIN || STEAMWORKS_LIN_OSX || !UNITY_5_3_OR_NEWER) && !DISABLESTEAMWORKS
#define SB_STEAM
#endif

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
#if SB_STEAM
using Steamworks;
#endif

namespace Sealebrities
{
    /// Steam lobby + rich presence around a Steam co-op session (the game data itself runs over SteamNetTransport).
    ///   Host:  CreateForHosting → FriendsOnly lobby (max 4) with lobby data sb_game / sb_proto / sb_ver / sb_host /
    ///          sb_count → NetSession.StartSteamHost. Rich presence: "status" (Taucht mit Freunden 2/4),
    ///          "connect" = "+connect_lobby <lobby id>" and the player group, so friends can click "Spiel beitreten" in
    ///          their Steam friends list. "Freunde einladen" opens Steam's invite dialog (only when the player clicks it);
    ///          without the overlay the lobby screen lists online friends to invite directly.
    ///   Join:  GameLobbyJoinRequested_t (accepted invite / "join game" on a friend in a lobby),
    ///          GameRichPresenceJoinRequested_t ("join game" via rich presence) and "+connect_lobby <id>" on the command
    ///          line → JoinLobby → lobby data checked (SEALEBRITIES? same protocol + version?) → P2P connect to the lobby
    ///          owner → the normal Hello / Welcome handshake. Requests that arrive in a running single-player game wait
    ///          until the player is back in the main menu.
    ///   Leave: NetSession ending / failing / quitting leaves the lobby and clears the rich presence.
    /// The friend list on the lobby screen shows friends who host a SEALEBRITIES lobby right now (one click to join).
    /// Test app 480: a friend must START SEALEBRITIES first – Steam would launch "Spacewar" for a closed game.
    /// Privacy: lobby / Steam ids and persona names never go into the log.
    public static class SteamLobby
    {
        public const string GameTag = "SEALEBRITIES";
        public const string KeyGame = "sb_game", KeyProto = "sb_proto", KeyVersion = "sb_ver", KeyHost = "sb_host", KeyCount = "sb_count";
        public const string ConnectToken = "+connect_lobby";
        const float CreateTimeout = 25f;

        /// A friend's SEALEBRITIES lobby (lobby screen list).
        public sealed class FriendGame
        {
            public ulong lobby;
            public string friendName;
            public int players;
            public int maxPlayers;
            public int protocol;
            public string version;
            public float seen;
            public bool Compatible => protocol == Net.ProtocolVersion && version == Application.version;
            public bool Full => players >= maxPlayers;
        }

        /// An online Steam friend (invite list when the Steam overlay is not available).
        public sealed class FriendEntry
        {
            public ulong id;
            public string name;
            /// Playing the same Steam app right now (480 / SEALEBRITIES).
            public bool sameGame;
        }

        public static readonly List<FriendGame> FriendGames = new List<FriendGame>();
        /// Raised when FriendGames changed (lobby screen rebuilds its list).
        public static event Action FriendGamesChanged;

        /// Lobby this PC is in (host or guest), 0 = none.
        public static ulong CurrentLobby { get; private set; }
        public static bool IsOwner { get; private set; }
        public static bool HasLobby => CurrentLobby != 0;
        /// CreateForHosting is waiting for Steam.
        public static bool Creating { get; private set; }
        /// A join request (invite / command line) waits for the main menu.
        public static bool JoinPending => pendingJoin != 0;

        static bool installed, presenceSet;
        static ulong pendingJoin;
        static float pendingReadySince = -1f, createStarted;
        static int joinToken;
        static Action<bool, string> createDone;
        static Action<bool, ulong, string> joinDone;
        static readonly Dictionary<ulong, string> friendLobbies = new Dictionary<ulong, string>();
        static readonly Dictionary<ulong, float> friendLobbySeen = new Dictionary<ulong, float>();
        static readonly HashSet<ulong> invited = new HashSet<ulong>();
        static readonly List<ulong> tmpIds = new List<ulong>();

#if SB_STEAM
        static Callback<GameLobbyJoinRequested_t> cbLobbyJoin;
        static Callback<GameRichPresenceJoinRequested_t> cbPresenceJoin;
        static Callback<LobbyDataUpdate_t> cbLobbyData;
        static CallResult<LobbyCreated_t> crCreate;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            FriendGames.Clear(); FriendGamesChanged = null;
            CurrentLobby = 0; IsOwner = false; Creating = false;
            installed = false; presenceSet = false; pendingJoin = 0; pendingReadySince = -1f; createStarted = 0f; joinToken = 0;
            createDone = null; joinDone = null;
            friendLobbies.Clear(); friendLobbySeen.Clear(); invited.Clear(); tmpIds.Clear();
#if SB_STEAM
            cbLobbyJoin = null; cbPresenceJoin = null; cbLobbyData = null; crCreate = null;
#endif
        }

        /// Called by SteamService.Init once Steamworks runs.
        internal static void Install()
        {
            if (installed) return;
            installed = true;
#if SB_STEAM
            cbLobbyJoin = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            cbPresenceJoin = Callback<GameRichPresenceJoinRequested_t>.Create(OnPresenceJoinRequested);
            cbLobbyData = Callback<LobbyDataUpdate_t>.Create(OnLobbyDataUpdate);
            crCreate = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
#endif
            NetSession.Started += OnSessionStarted;
            NetSession.Ended += OnSessionEnded;
            Net.PlayerJoined += OnPlayerCountChanged;
            Net.PlayerLeft += OnPlayerCountChanged;
            ParseLaunchCommandLine();
        }

        /// Application quit (SteamService.Shutdown): leave the lobby, clear the rich presence, drop the callbacks.
        internal static void Shutdown()
        {
            Leave();
            pendingJoin = 0;
            if (!installed) return;
            installed = false;
            NetSession.Started -= OnSessionStarted;
            NetSession.Ended -= OnSessionEnded;
            Net.PlayerJoined -= OnPlayerCountChanged;
            Net.PlayerLeft -= OnPlayerCountChanged;
#if SB_STEAM
            try
            {
                cbLobbyJoin?.Dispose(); cbPresenceJoin?.Dispose(); cbLobbyData?.Dispose(); crCreate?.Dispose();
            }
            catch (Exception) { }
            cbLobbyJoin = null; cbPresenceJoin = null; cbLobbyData = null; crCreate = null;
#endif
        }

        // =================================================================== host
        /// Creates the FriendsOnly lobby for hosting. done(true, null) once the lobby exists (lobby data set);
        /// done(false, localized error) otherwise. Call NetSession.StartSteamHost after success.
        public static void CreateForHosting(Action<bool, string> done)
        {
            if (Creating) return;
            if (!SteamService.Available)
            {
                done?.Invoke(false, SteamService.UnavailableText());
                return;
            }
#if SB_STEAM
            Leave();
            SteamAPICall_t call;
            try { call = SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, Net.MaxPlayers); }
            catch (Exception) { call = SteamAPICall_t.Invalid; }
            if (call == SteamAPICall_t.Invalid)
            {
                done?.Invoke(false, CreateErrorText("call"));
                return;
            }
            Creating = true;
            createStarted = Time.realtimeSinceStartup;
            createDone = done;
            crCreate.Set(call);
            Debug.Log("[Steam] creating a friends-only lobby …");
#else
            done?.Invoke(false, SteamService.UnavailableText());
#endif
        }

        static string CreateErrorText(string code) =>
            Loc.Tr("Steam-Lobby konnte nicht erstellt werden (" + code + "). Ist Steam online? Sonst „Per IP / Heimnetz“ nutzen.",
                   "Could not create the Steam lobby (" + code + "). Is Steam online? Otherwise use “By IP / home network”.");

#if SB_STEAM
        static void OnLobbyCreated(LobbyCreated_t r, bool ioFailure)
        {
            var done = createDone;
            createDone = null;
            bool wanted = Creating;
            Creating = false;
            bool ok = !ioFailure && r.m_eResult == EResult.k_EResultOK && r.m_ulSteamIDLobby != 0;
            if (!wanted)
            {
                // timed out / cancelled meanwhile
                if (ok) SafeLeaveLobby(r.m_ulSteamIDLobby);
                return;
            }
            if (!ok)
            {
                string code = ioFailure ? "io" : r.m_eResult.ToString().Replace("k_EResult", "");
                Debug.LogWarning("[Steam] lobby creation failed (" + code + ")");
                done?.Invoke(false, CreateErrorText(code));
                return;
            }
            CurrentLobby = r.m_ulSteamIDLobby;
            IsOwner = true;
            var id = new CSteamID(CurrentLobby);
            try
            {
                SteamMatchmaking.SetLobbyData(id, KeyGame, GameTag);
                SteamMatchmaking.SetLobbyData(id, KeyProto, Net.ProtocolVersion.ToString(CultureInfo.InvariantCulture));
                SteamMatchmaking.SetLobbyData(id, KeyVersion, Application.version);
                SteamMatchmaking.SetLobbyData(id, KeyHost, SteamService.LocalSteamId.ToString(CultureInfo.InvariantCulture));
                SteamMatchmaking.SetLobbyData(id, KeyCount, "1");
                SteamMatchmaking.SetLobbyJoinable(id, true);
            }
            catch (Exception e) { Debug.LogWarning("[Steam] lobby data: " + e.Message); }
            Debug.Log("[Steam] lobby created (friends only, max " + Net.MaxPlayers + ")");
            done?.Invoke(true, null);
        }
#endif

        /// "Freunde einladen": Steam's invite dialog for the current lobby (only ever called from a player's click).
        /// Returns false when there is no lobby; OverlayEnabled tells whether the dialog can actually appear.
        public static bool OpenInviteDialog()
        {
#if SB_STEAM
            if (!SteamService.Initialized || CurrentLobby == 0) return false;
            try
            {
                SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(CurrentLobby));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Steam] invite dialog: " + e.Message);
                return false;
            }
#else
            return false;
#endif
        }

        /// Sends one Steam lobby invite to a friend (only ever called from a player's click in the invite list).
        public static bool InviteFriend(ulong friend)
        {
#if SB_STEAM
            if (!SteamService.Initialized || CurrentLobby == 0 || friend == 0) return false;
            try
            {
                bool ok = SteamMatchmaking.InviteUserToLobby(new CSteamID(CurrentLobby), new CSteamID(friend));
                if (ok) invited.Add(friend);
                return ok;
            }
            catch (Exception) { return false; }
#else
            return false;
#endif
        }

        public static bool WasInvited(ulong friend) => invited.Contains(friend);

        /// Online friends for the in-game invite list: same game first, then by name. Allocates – call on demand only.
        public static void OnlineFriends(List<FriendEntry> into, int max = 40)
        {
            into.Clear();
#if SB_STEAM
            if (!SteamService.Available) return;
            try
            {
                uint app = SteamService.AppId;
                int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < n && i < 1000; i++)
                {
                    var f = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    var st = SteamFriends.GetFriendPersonaState(f);
                    if (st == EPersonaState.k_EPersonaStateOffline) continue;
                    bool same = SteamFriends.GetFriendGamePlayed(f, out FriendGameInfo_t gi) && gi.m_gameID.AppID().m_AppId == app;
                    into.Add(new FriendEntry { id = f.m_SteamID, name = SteamFriends.GetFriendPersonaName(f) ?? "?", sameGame = same });
                }
            }
            catch (Exception e) { Debug.LogWarning("[Steam] friend list: " + e.Message); }
            into.Sort((a, b) => a.sameGame != b.sameGame ? (a.sameGame ? -1 : 1) : string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            if (into.Count > max) into.RemoveRange(max, into.Count - max);
#endif
        }

        // =================================================================== presence
        static void OnSessionStarted()
        {
            if (NetSession.IsSteam) UpdatePresence();
        }

        static void OnSessionEnded(string notice)
        {
            Leave();
        }

        static void OnPlayerCountChanged(NetPlayerInfo p)
        {
            if (NetSession.IsSteam) UpdatePresence();
        }

        /// Rich presence + lobby player count for the running Steam session.
        public static void UpdatePresence()
        {
#if SB_STEAM
            if (!SteamService.Initialized || CurrentLobby == 0) return;
            int n = Mathf.Clamp(Net.IsOnline ? Net.PlayerCount : 1, 1, Net.MaxPlayers);
            string count = n.ToString(CultureInfo.InvariantCulture);
            try
            {
                if (IsOwner) SteamMatchmaking.SetLobbyData(new CSteamID(CurrentLobby), KeyCount, count);
                string lobby = CurrentLobby.ToString(CultureInfo.InvariantCulture);
                SteamFriends.SetRichPresence("status", Loc.Tr($"Taucht mit Freunden ({n}/{Net.MaxPlayers})", $"Diving with friends ({n}/{Net.MaxPlayers})"));
                SteamFriends.SetRichPresence("connect", ConnectToken + " " + lobby);
                SteamFriends.SetRichPresence("steam_player_group", lobby);
                SteamFriends.SetRichPresence("steam_player_group_size", count);
                presenceSet = true;
            }
            catch (Exception e) { Debug.LogWarning("[Steam] rich presence: " + e.Message); }
#endif
        }

        static void ClearPresence()
        {
            if (!presenceSet) return;
            presenceSet = false;
#if SB_STEAM
            if (!SteamService.Initialized) return;
            try { SteamFriends.ClearRichPresence(); } catch (Exception) { }
#endif
        }

        /// Leaves the lobby (if any), cancels a pending lobby join / creation and clears the rich presence.
        public static void Leave()
        {
            joinToken++;
            joinDone = null;
            if (Creating)
            {
                Creating = false;
                createDone = null;   // a late LobbyCreated_t leaves that lobby again
            }
            if (CurrentLobby != 0)
            {
                SafeLeaveLobby(CurrentLobby);
                Debug.Log("[Steam] left the lobby");
            }
            CurrentLobby = 0;
            IsOwner = false;
            invited.Clear();
            ClearPresence();
        }

        static void SafeLeaveLobby(ulong lobby)
        {
#if SB_STEAM
            if (lobby == 0 || !SteamService.Initialized) return;
            try { SteamMatchmaking.LeaveLobby(new CSteamID(lobby)); } catch (Exception) { }
#endif
        }

        // =================================================================== join
        /// Enters a lobby and reports the host to connect to: done(true, hostSteamId, null) or done(false, 0, error).
        /// Used by NetSession.JoinSteamLobby; a newer Join / Leave cancels an older one (its lobby is left again).
        public static void Join(ulong lobbyId, Action<bool, ulong, string> done)
        {
            if (!SteamService.Available)
            {
                done?.Invoke(false, 0, SteamService.UnavailableText());
                return;
            }
#if SB_STEAM
            Leave();
            int token = ++joinToken;
            joinDone = done;
            SteamAPICall_t call;
            try { call = SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)); }
            catch (Exception) { call = SteamAPICall_t.Invalid; }
            if (call == SteamAPICall_t.Invalid)
            {
                joinDone = null;
                done?.Invoke(false, 0, Loc.Tr("Steam konnte der Lobby nicht beitreten.", "Steam could not join the lobby."));
                return;
            }
            // one CallResult per request: the dispatcher keeps it alive until the result arrived
            var cr = CallResult<LobbyEnter_t>.Create();
            cr.Set(call, (r, io) => OnLobbyEntered(token, r, io));
            Debug.Log("[Steam] joining a lobby …");
#else
            done?.Invoke(false, 0, SteamService.UnavailableText());
#endif
        }

#if SB_STEAM
        static void OnLobbyEntered(int token, LobbyEnter_t r, bool io)
        {
            ulong lobby = r.m_ulSteamIDLobby;
            var resp = (EChatRoomEnterResponse)r.m_EChatRoomEnterResponse;
            bool entered = !io && resp == EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess && lobby != 0;
            if (token != joinToken || joinDone == null)
            {
                if (entered) SafeLeaveLobby(lobby);   // cancelled meanwhile
                return;
            }
            var done = joinDone;
            joinDone = null;
            if (!entered)
            {
                Debug.LogWarning("[Steam] could not enter the lobby (" + (io ? "io" : resp.ToString().Replace("k_EChatRoomEnterResponse", "")) + ")");
                done(false, 0, io ? Loc.Tr("Steam antwortet nicht – bitte nochmal versuchen.", "Steam does not answer – please try again.") : EnterErrorText(resp));
                return;
            }
            CurrentLobby = lobby;
            IsOwner = false;
            var id = new CSteamID(lobby);
            string game = SteamMatchmaking.GetLobbyData(id, KeyGame);
            if (game != GameTag)
            {
                Leave();
                done(false, 0, Loc.Tr("Diese Steam-Lobby gehört nicht zu SEALEBRITIES.", "This Steam lobby does not belong to SEALEBRITIES."));
                return;
            }
            int.TryParse(SteamMatchmaking.GetLobbyData(id, KeyProto), NumberStyles.Integer, CultureInfo.InvariantCulture, out int proto);
            string ver = SteamMatchmaking.GetLobbyData(id, KeyVersion);
            if (proto != Net.ProtocolVersion || ver != Application.version)
            {
                Leave();
                Debug.LogWarning("[Steam] lobby has another game version (" + ver + " / protocol " + proto + ")");
                done(false, 0, NetSession.ReasonText(NetEndReason.VersionMismatch, string.IsNullOrEmpty(ver) ? "?" : ver));
                return;
            }
            // the real host wrote sb_host when it created the lobby; Steam may hand the lobby OWNERSHIP to a guest when
            // the host briefly drops – that guest has no listen socket. sb_host first, the owner only as a fallback.
            ulong host = 0;
            ulong.TryParse(SteamMatchmaking.GetLobbyData(id, KeyHost), NumberStyles.None, CultureInfo.InvariantCulture, out host);
            if (host == 0)
            {
                try { host = SteamMatchmaking.GetLobbyOwner(id).m_SteamID; } catch (Exception) { host = 0; }
            }
            else if (!IsLobbyMember(id, host))
            {
                Leave();
                done(false, 0, Loc.Tr("Der Host ist nicht mehr in diesem Spiel.", "The host is no longer in this game."));
                return;
            }
            if (host == SteamService.LocalSteamId)
            {
                Leave();
                done(false, 0, Loc.Tr("Das ist deine eigene Lobby – Steam-Koop braucht zwei verschiedene Steam-Konten (auf einem PC: „Per IP / Heimnetz“ mit 127.0.0.1).",
                                      "That is your own lobby – Steam co-op needs two different Steam accounts (on one PC: “By IP / home network” with 127.0.0.1)."));
                return;
            }
            if (host == 0)
            {
                Leave();
                done(false, 0, Loc.Tr("Der Host ist nicht mehr in diesem Spiel.", "The host is no longer in this game."));
                return;
            }
            int members = 0;
            try { members = SteamMatchmaking.GetNumLobbyMembers(id); } catch (Exception) { }
            Debug.Log("[Steam] entered a SEALEBRITIES lobby (" + members + " members) – connecting to the host");
            done(true, host, null);
        }

        /// True when the Steam id is a member of the lobby (no ids are logged).
        public static bool IsLobbyMember(CSteamID lobby, ulong steamId)
        {
            if (steamId == 0) return false;
            try
            {
                int n = SteamMatchmaking.GetNumLobbyMembers(lobby);
                for (int i = 0; i < n; i++)
                    if (SteamMatchmaking.GetLobbyMemberByIndex(lobby, i).m_SteamID == steamId) return true;
            }
            catch (Exception) { }
            return false;
        }

        static string EnterErrorText(EChatRoomEnterResponse r)
        {
            switch (r)
            {
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseDoesntExist:
                    return Loc.Tr("Dieses Spiel gibt es nicht mehr – der Host hat es beendet.", "This game no longer exists – the host ended it.");
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseFull:
                    return NetSession.ReasonText(NetEndReason.Full, null);
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseNotAllowed:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseBanned:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseMemberBlockedYou:
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseYouBlockedMember:
                    return Loc.Tr("Steam erlaubt den Beitritt zu diesem Spiel nicht.", "Steam does not allow joining this game.");
                case EChatRoomEnterResponse.k_EChatRoomEnterResponseLimited:
                    return Loc.Tr("Eingeschränktes Steam-Konto – Lobbys sind gesperrt.", "Limited Steam account – lobbies are blocked.");
                default:
                    return Loc.Tr("Beitritt über Steam fehlgeschlagen – bitte nochmal versuchen.", "Joining via Steam failed – please try again.");
            }
        }

        // ------------------------------------------------------------------ join requests from Steam
        static void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t r) => RequestJoin(r.m_steamIDLobby.m_SteamID, "invite");

        static void OnPresenceJoinRequested(GameRichPresenceJoinRequested_t r)
        {
            if (TryParseConnect(r.m_rgchConnect, out ulong lobby)) RequestJoin(lobby, "friends list");
        }
#endif

        /// "+connect_lobby 1234" anywhere in the text (rich presence connect string, launch command line).
        public static bool TryParseConnect(string text, out ulong lobby)
        {
            lobby = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int i = text.IndexOf(ConnectToken, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            i += ConnectToken.Length;
            while (i < text.Length && (text[i] == ' ' || text[i] == '=' || text[i] == '"')) i++;
            int start = i;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') i++;
            return i > start && ulong.TryParse(text.Substring(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out lobby) && lobby != 0;
        }

        static void ParseLaunchCommandLine()
        {
            ulong lobby = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length && lobby == 0; i++)
                {
                    if (!string.Equals(args[i], ConnectToken, StringComparison.OrdinalIgnoreCase)) continue;
                    if (i + 1 < args.Length) ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out lobby);
                }
            }
            catch (Exception) { lobby = 0; }
#if SB_STEAM
            if (lobby == 0 && SteamService.Initialized)
            {
                try
                {
                    if (SteamApps.GetLaunchCommandLine(out string cmd, 1024) > 0) TryParseConnect(cmd, out lobby);
                }
                catch (Exception) { lobby = 0; }
            }
#endif
            if (lobby != 0) RequestJoin(lobby, "command line");
        }

        /// A join request from Steam: runs as soon as the main menu is up (immediately when it already is).
        static void RequestJoin(ulong lobby, string via)
        {
            if (lobby == 0) return;
            Debug.Log("[Steam] join request (" + via + ")");
            if (Net.IsOnline)
            {
                if (lobby == CurrentLobby)
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Schon in diesem Spiel", "Already in this game"));
                else
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Erst Sitzung verlassen", "Leave the session first"), Loc.Tr("Esc", "Esc"));
                return;
            }
            pendingJoin = lobby;
            pendingReadySince = -1f;
            var gm = GameManager.I;
            if (gm != null && gm.InGame)
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Einladung angenommen", "Invite accepted"), Loc.Tr("Esc → Hauptmenü", "Esc → Main menu"));
        }

        // =================================================================== frame
        /// Every frame (SteamServiceRunner): lobby creation timeout and pending join requests.
        internal static void Tick(float dt)
        {
            if (Creating && Time.realtimeSinceStartup - createStarted > CreateTimeout)
            {
                var done = createDone;
                createDone = null;
                Creating = false;
                Debug.LogWarning("[Steam] lobby creation timed out");
                done?.Invoke(false, CreateErrorText("timeout"));
            }

            if (pendingJoin == 0) return;
            var gm = GameManager.I;
            bool ready = BootState.Ready && !UIState.LoadingOpen && UIState.MainMenuOpen && UIModule.Controller != null
                         && (gm == null || !gm.InGame) && !Net.IsOnline;
            if (!ready)
            {
                pendingReadySince = -1f;
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (pendingReadySince < 0f) { pendingReadySince = now; return; }
            if (now - pendingReadySince < 0.75f) return;
            ulong lobby = pendingJoin;
            pendingJoin = 0;
            pendingReadySince = -1f;
            NetLobby.OpenSteamJoin(lobby);
        }

        // =================================================================== friends hosting right now
        /// Looks for friends in a SEALEBRITIES lobby (same Steam app, lobby data checked). Lobby screen: every few seconds.
        public static void RefreshFriendGames()
        {
#if SB_STEAM
            if (!SteamService.Available) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                uint app = SteamService.AppId;
                int n = SteamFriends.GetFriendCount(EFriendFlags.k_EFriendFlagImmediate);
                for (int i = 0; i < n && i < 1000; i++)
                {
                    var f = SteamFriends.GetFriendByIndex(i, EFriendFlags.k_EFriendFlagImmediate);
                    if (!SteamFriends.GetFriendGamePlayed(f, out FriendGameInfo_t gi)) continue;
                    if (gi.m_gameID.AppID().m_AppId != app || !gi.m_steamIDLobby.IsValid()) continue;
                    ulong lobby = gi.m_steamIDLobby.m_SteamID;
                    if (lobby == CurrentLobby) continue;
                    friendLobbies[lobby] = SteamFriends.GetFriendPersonaName(f) ?? "?";
                    friendLobbySeen[lobby] = now;
                    SteamMatchmaking.RequestLobbyData(gi.m_steamIDLobby);
                }
            }
            catch (Exception e) { Debug.LogWarning("[Steam] friend games: " + e.Message); }

            // friends who stopped hosting
            bool changed = false;
            tmpIds.Clear();
            foreach (var kv in friendLobbySeen) if (now - kv.Value > 12f) tmpIds.Add(kv.Key);
            for (int i = 0; i < tmpIds.Count; i++)
            {
                friendLobbySeen.Remove(tmpIds[i]);
                friendLobbies.Remove(tmpIds[i]);
                for (int k = FriendGames.Count - 1; k >= 0; k--)
                    if (FriendGames[k].lobby == tmpIds[i]) { FriendGames.RemoveAt(k); changed = true; }
            }
            tmpIds.Clear();
            if (changed) FriendGamesChanged?.Invoke();
#endif
        }

#if SB_STEAM
        static void OnLobbyDataUpdate(LobbyDataUpdate_t u)
        {
            if (u.m_ulSteamIDLobby != u.m_ulSteamIDMember) return;   // member data, not the lobby's
            ulong lobby = u.m_ulSteamIDLobby;
            if (!friendLobbies.TryGetValue(lobby, out string friendName)) return;
            int idx = -1;
            for (int i = 0; i < FriendGames.Count; i++) if (FriendGames[i].lobby == lobby) { idx = i; break; }
            var id = new CSteamID(lobby);
            bool ours = u.m_bSuccess != 0 && SteamMatchmaking.GetLobbyData(id, KeyGame) == GameTag;
            if (!ours)
            {
                if (idx >= 0) { FriendGames.RemoveAt(idx); FriendGamesChanged?.Invoke(); }
                return;
            }
            var g = idx >= 0 ? FriendGames[idx] : new FriendGame { lobby = lobby };
            g.friendName = friendName;
            int.TryParse(SteamMatchmaking.GetLobbyData(id, KeyCount), NumberStyles.Integer, CultureInfo.InvariantCulture, out g.players);
            int.TryParse(SteamMatchmaking.GetLobbyData(id, KeyProto), NumberStyles.Integer, CultureInfo.InvariantCulture, out g.protocol);
            g.version = SteamMatchmaking.GetLobbyData(id, KeyVersion) ?? "";
            int limit = 0;
            try { limit = SteamMatchmaking.GetLobbyMemberLimit(id); } catch (Exception) { }
            g.maxPlayers = limit > 0 ? Mathf.Min(limit, Net.MaxPlayers) : Net.MaxPlayers;
            if (g.players < 1) g.players = 1;
            g.seen = Time.realtimeSinceStartup;
            if (idx < 0) FriendGames.Add(g);
            FriendGamesChanged?.Invoke();
        }
#endif
    }
}
