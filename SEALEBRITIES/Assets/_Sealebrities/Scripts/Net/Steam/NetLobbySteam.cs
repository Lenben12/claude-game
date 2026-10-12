using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Steam parts of the "Mit Freunden" screens (see NetLobby):
    ///   Home      – "Über Steam (empfohlen)": big "Spiel hosten" tile + friends who host SEALEBRITIES right now (one
    ///               click "Beitreten"); "Per IP / Heimnetz" comes second.
    ///   SteamHost – which save slot (continue a world / new world in an empty slot) + difficulty, how friends get in,
    ///               "Steam-Lobby öffnen & starten" (SteamLobby.CreateForHosting → NetSession.StartSteamHost → the game
    ///               starts).
    ///   In game   – only for the HOST (the creator of the world, GDD §19b – guests never get it): while the pause menu
    ///               is open in a Steam session a card with the big "Freunde einladen" button → the in-game list of online
    ///               friends (the Steam overlay's own dialog is the small second button).
    /// Invites are only ever sent when the player clicks. A friend can be invited AS OFTEN AS YOU LIKE: every row's
    /// button always says „Einladen“, never changes its text / colour / state and never gets disabled; each click sends
    /// one invite and shows the tiny toast „Einladung gesendet“ (GDD §19b).
    public static partial class NetLobby
    {
        static VisualElement friendGamesBox, steamCard, invitePicker;
        static bool steamConnecting, friendGamesDirty, steamCardDirty, steamInstalled;
        static float friendRefresh;
        static int steamCardCount;
        static readonly List<SteamLobby.FriendEntry> friendTmp = new List<SteamLobby.FriendEntry>();
        static readonly List<Button> inviteButtons = new List<Button>();

        /// Test hook (AutoPlaytest): replaces the real Steam invite – tests never send one.
        public static Func<ulong, bool> InviteSenderOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSteamStatics()
        {
            friendGamesBox = null; steamCard = null; invitePicker = null;
            steamConnecting = false; friendGamesDirty = false; steamCardDirty = false; steamInstalled = false;
            friendRefresh = 0f; steamCardCount = 0;
            friendTmp.Clear();
            inviteButtons.Clear();
            InviteSenderOverride = null;
        }

        /// GDD §19b: only the creator of the save – the host who loaded / started the world – may invite. Guests: false.
        public static bool InviteRoleAllowed => !Net.IsClient;

        /// The invite card / list may be used right now: the host of a running Steam session that owns the lobby.
        public static bool CanInvite => Net.IsHost && NetSession.IsSteam && SteamLobby.HasLobby && SteamLobby.IsOwner;

        static void InstallSteam()
        {
            if (steamInstalled) return;
            steamInstalled = true;
            SteamLobby.FriendGamesChanged += OnFriendGamesChanged;
        }

        static void OnFriendGamesChanged() => friendGamesDirty = true;

        // =================================================================== home: Steam section
        static void BuildSteamHomeSection()
        {
            var box = NetLobbyStyle.Col();
            box.style.backgroundColor = new Color(1f, 1f, 1f, 0.75f);
            NetLobbyStyle.Radius(box, 18f);
            NetLobbyStyle.Pad(box, 14f, 18f);
            body.Add(box);

            var head = NetLobbyStyle.Row();
            head.Add(NetLobbyStyle.Text(Loc.Tr("Über Steam", "Via Steam"), 24f, NetLobbyStyle.Ink, true));
            head.Add(NetLobbyStyle.Gap(10f, true));
            head.Add(NetLobbyStyle.Chip(Loc.Tr("empfohlen", "recommended"), NetLobbyStyle.Leaf, NetLobbyStyle.White, 15f));
            head.Add(NetLobbyStyle.Gap(12f, true));
            var why = NetLobbyStyle.Text(Loc.Tr("keine IP, kein Port – Freunde kommen per Einladung", "no IP, no port – friends join by invite"), 16f, NetLobbyStyle.Muted);
            why.style.flexShrink = 1;
            head.Add(why);
            box.Add(head);
            box.Add(NetLobbyStyle.Gap(12f));

            var row = NetLobbyStyle.Row();
            row.style.alignItems = Align.Stretch;
            row.Add(Tile("+", Loc.Tr("Spiel hosten", "Host a game"), Loc.Tr("Steam-Lobby · Freunde einladen", "Steam lobby · invite friends"),
                         NetLobbyStyle.Coral, () => Go(Page.SteamHost), 150f));
            row.Add(NetLobbyStyle.Gap(16f, true));
            var right = NetLobbyStyle.Col();
            right.style.flexGrow = 1.3f;
            right.style.flexBasis = 0;
            right.Add(NetLobbyStyle.Text(Loc.Tr("Freunde, die gerade hosten", "Friends hosting right now"), 19f, NetLobbyStyle.Ink, true));
            right.Add(NetLobbyStyle.Gap(6f));
            friendGamesBox = NetLobbyStyle.Col();
            right.Add(friendGamesBox);
            row.Add(right);
            box.Add(row);
            RebuildFriendGames();
        }

        static void RebuildFriendGames()
        {
            if (friendGamesBox == null) return;
            friendGamesBox.Clear();
            var list = SteamLobby.FriendGames;
            if (list.Count == 0)
            {
                friendGamesBox.Add(NetLobbyStyle.Para(Loc.Tr(
                    "Gerade hostet keiner deiner Steam-Freunde. Lädt dich jemand ein: Einladung in Steam annehmen (Steam-Chat oder Shift+Tab) – SEALEBRITIES muss dafür schon laufen.",
                    "None of your Steam friends is hosting right now. When someone invites you: accept the invite in Steam (Steam chat or Shift+Tab) – SEALEBRITIES has to be running already."),
                    15f, NetLobbyStyle.Muted));
                return;
            }
            for (int i = 0; i < list.Count && i < 4; i++)
            {
                var g = list[i];
                var r = NetLobbyStyle.Row();
                r.style.backgroundColor = NetLobbyStyle.White;
                NetLobbyStyle.Radius(r, 14f);
                NetLobbyStyle.Pad(r, 7f, 12f);
                r.style.marginBottom = 6f;
                r.Add(NetLobbyStyle.Dot(g.Compatible ? NetLobbyStyle.Turquoise : NetLobbyStyle.Danger, 12f));
                r.Add(NetLobbyStyle.Gap(8f, true));
                var name = NetLobbyStyle.Text(g.friendName, 19f, NetLobbyStyle.Ink, true);
                name.style.flexShrink = 1;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                r.Add(name);
                r.Add(NetLobbyStyle.Gap(8f, true));
                r.Add(NetLobbyStyle.Chip(g.players + "/" + g.maxPlayers, g.Full ? NetLobbyStyle.Danger : NetLobbyStyle.Sand, g.Full ? NetLobbyStyle.White : NetLobbyStyle.Ink, 14f));
                if (!g.Compatible)
                {
                    r.Add(NetLobbyStyle.Gap(6f, true));
                    r.Add(NetLobbyStyle.Chip(Loc.Tr("Version ", "Version ") + (string.IsNullOrEmpty(g.version) ? "?" : g.version), NetLobbyStyle.Danger, NetLobbyStyle.White, 13f));
                }
                r.Add(NetLobbyStyle.Flex());
                ulong lobbyId = g.lobby;
                var join = NetLobbyStyle.Accent(Loc.Tr("Beitreten", "Join"), () => OpenSteamJoin(lobbyId), 17f);
                NetLobbyStyle.Pad(join, 6f, 16f);
                NetLobbyStyle.Enable(join, g.Compatible && !g.Full);
                r.Add(join);
                friendGamesBox.Add(r);
            }
        }

        /// Lobby screen open on the home page: refresh the "friends hosting" list every 3 s.
        static void TickSteamHome(float dt)
        {
            if (page != Page.Home || friendGamesBox == null || !SteamService.Available) return;
            friendRefresh -= dt;
            if (friendRefresh <= 0f)
            {
                friendRefresh = 3f;
                SteamLobby.RefreshFriendGames();
            }
            if (friendGamesDirty)
            {
                friendGamesDirty = false;
                RebuildFriendGames();
            }
        }

        // =================================================================== join
        /// Joins a Steam lobby from the main menu (friend list button, accepted invite, "Spiel beitreten",
        /// +connect_lobby) and shows the connecting page.
        public static void OpenSteamJoin(ulong lobbyId)
        {
            if (lobbyId == 0) return;
            if (!open) Open();
            if (!open)
            {
                // no lobby UI (yet): join anyway – the result shows up as a notice / toast
                NetSession.JoinSteamLobby(lobbyId);
                return;
            }
            SaveProfile();
            steamConnecting = true;
            page = Page.Connecting;
            NetSession.JoinSteamLobby(lobbyId);
            Build();
        }

        // =================================================================== Steam host page
        static void BuildSteamHost()
        {
            // round 3 step 6b: which of the four worlds (save slots) – continue it, or a new world in an empty slot
            BuildHostSlotPicker();
            BuildHostDifficulty();

            var info = NetLobbyStyle.Col();
            info.style.backgroundColor = new Color(1f, 1f, 1f, 0.75f);
            NetLobbyStyle.Radius(info, 18f);
            NetLobbyStyle.Pad(info, 14f, 20f);
            body.Add(info);
            info.Add(NetLobbyStyle.Text(Loc.Tr("So kommen deine Freunde dazu", "How your friends get in"), 20f, NetLobbyStyle.Ink, true));
            AddStep(info, Loc.Tr("1.  Sie starten SEALEBRITIES auf ihrem PC – Steam muss laufen und online sein.",
                                 "1.  They start SEALEBRITIES on their PC – Steam has to be running and online."));
            AddStep(info, Loc.Tr("2.  Du drückst im Spiel Esc → „Freunde einladen“. Oder sie klicken in ihrer Steam-Freundesliste bei dir auf „Spiel beitreten“.",
                                 "2.  In the game you press Esc → “Invite friends”. Or they click “Join game” on you in their Steam friends list."));
            AddStep(info, Loc.Tr("3.  Einladung annehmen – das Spiel verbindet von selbst. Keine IP, kein Port, keine Router-Einstellung.",
                                 "3.  Accept the invite – the game connects by itself. No IP, no port, no router settings."));
            var test = NetLobbyStyle.Para(Loc.Tr(
                $"Testphase: Steam zeigt euch als „Spacewar“ (Steam-Test-App 480) – das ist normal, solange SEALEBRITIES noch nicht auf Steam ist. Alle brauchen dieselbe Spielversion ({Application.version}).",
                $"Test phase: Steam shows you as playing “Spacewar” (Steam test app 480) – that is normal while SEALEBRITIES is not on Steam yet. Everyone needs the same game version ({Application.version})."),
                14f, NetLobbyStyle.Muted);
            test.style.marginTop = 8f;
            info.Add(test);

            body.Add(NetLobbyStyle.Gap(18f));
            var row = NetLobbyStyle.Row();
            row.Add(NetLobbyStyle.Flex());
            bool creating = SteamLobby.Creating;
            string label = creating
                ? Loc.Tr("Steam-Lobby wird erstellt …", "Creating the Steam lobby …")
                : HostSlotUsed ? Loc.Tr("Lobby öffnen & weiterspielen", "Open lobby & continue") : Loc.Tr("Lobby öffnen & starten", "Open lobby & start");
            var start = NetLobbyStyle.Primary(label, OnSteamHostStart, 24f);
            start.style.minWidth = 320f;
            NetLobbyStyle.Pad(start, 14f, 30f);
            NetLobbyStyle.Enable(start, !creating);
            row.Add(start);
            body.Add(row);
        }

        static void AddStep(VisualElement parent, string text)
        {
            var p = NetLobbyStyle.Para(text, 16f, NetLobbyStyle.Muted);
            p.style.marginTop = 5f;
            parent.Add(p);
        }

        static void OnSteamHostStart()
        {
            if (SteamLobby.Creating) return;
            if (!SteamService.Available)
            {
                NetSession.ReportStatus(SteamService.UnavailableText(), true);
                return;
            }
            SaveProfile();
            // a leftover session (e.g. a cancelled join) must end BEFORE the lobby exists – ending it later would leave the lobby
            if (NetSession.Current != NetSession.Phase.Idle) NetSession.EndSession(NetEndReason.LocalLeft, null, false);
            int slot = hostSlot;
            bool cont = SaveSlots.Exists(slot);
            var diff = hostDifficulty;
            NetSession.ReportStatus(Loc.Tr("Steam-Lobby wird erstellt …", "Creating the Steam lobby …"), false);
            SteamLobby.CreateForHosting((ok, err) =>
            {
                dirty = true;
                if (!ok)
                {
                    NetSession.ReportStatus(err, true);
                    return;
                }
                var gm = GameManager.I;
                if (!open || page != Page.SteamHost || gm == null || gm.InGame)
                {
                    SteamLobby.Leave();   // the player left the page meanwhile
                    return;
                }
                if (!NetSession.StartSteamHost(out string error))
                {
                    SteamLobby.Leave();
                    NetSession.ReportStatus(error, true);
                    return;
                }
                SteamLobby.UpdatePresence();
                Close();
                StartHostedGame(slot, cont, diff);
            });
            dirty = true;   // show the "creating" state
        }

        // =================================================================== in game: invite card (pause menu, host only)
        static bool debugPicker;

        static void TickSteamCard(float dt)
        {
            var gm = GameManager.I;
            // GDD §19b: only the creator of the world (the host) can invite – guests never see the card or the list
            bool want = CanInvite && Net.IsOnline && UIState.PauseOpen && !UIState.MainMenuOpen && gm != null && gm.InGame;
            if (!want)
            {
                if (steamCard != null)
                {
                    steamCard.RemoveFromHierarchy();
                    steamCard = null;
                }
                if (invitePicker != null && !debugPicker) CloseInvitePicker();
                return;
            }
            if (steamCard != null && steamCard.panel != null && !steamCardDirty && steamCardCount == Net.PlayerCount) return;
            if (!EnsureLayer()) return;
            steamCardDirty = false;
            steamCardCount = Net.PlayerCount;
            if (steamCard != null) steamCard.RemoveFromHierarchy();
            steamCard = BuildSteamCard();
            layer.Add(steamCard);
            if (invitePicker != null) invitePicker.BringToFront();
        }

        static VisualElement BuildSteamCard()
        {
            var c = NetLobbyStyle.CardBox(20f, 18f);
            c.name = "sb-net-steamcard";
            c.style.position = Position.Absolute;
            c.style.right = 32f;
            c.style.top = 110f;
            c.style.width = 380f;
            c.style.backgroundColor = new Color(0.965f, 0.945f, 0.902f, 0.96f);
            c.pickingMode = PickingMode.Position;

            var head = NetLobbyStyle.Row();
            head.Add(NetLobbyStyle.Text(Loc.Tr("Steam-Koop", "Steam co-op"), 24f, NetLobbyStyle.Ink, true));
            head.Add(NetLobbyStyle.Flex());
            head.Add(NetLobbyStyle.Chip(Net.PlayerCount + "/" + Net.MaxPlayers, NetLobbyStyle.Sand, NetLobbyStyle.Ink, 15f));
            c.Add(head);
            var p = NetLobbyStyle.Para(Loc.Tr("Freunde starten SEALEBRITIES und nehmen deine Einladung in Steam an – oder klicken in ihrer Freundesliste bei dir auf „Spiel beitreten“.",
                                              "Friends start SEALEBRITIES and accept your invite in Steam – or click “Join game” on you in their friends list."), 15f, NetLobbyStyle.Muted);
            p.style.marginTop = 6f;
            c.Add(p);
            c.Add(NetLobbyStyle.Gap(12f));

            // the in-game list first: there every friend can be invited again and again (Steam's own dialog greys a
            // friend out after one invite)
            var invite = NetLobbyStyle.Primary(Loc.Tr("Freunde einladen", "Invite friends"), OpenInvitePicker, 24f);
            NetLobbyStyle.Pad(invite, 14f, 24f);
            c.Add(invite);
            if (SteamService.OverlayEnabled)
            {
                c.Add(NetLobbyStyle.Gap(8f));
                var overlay = NetLobbyStyle.Ghost(Loc.Tr("Steam-Fenster öffnen", "Open the Steam window"), OnSteamDialogClicked, 16f);
                overlay.style.alignSelf = Align.FlexStart;
                c.Add(overlay);
            }
            return c;
        }

        static void OnSteamDialogClicked()
        {
            if (!CanInvite) return;
            if (!SteamLobby.OpenInviteDialog()) OpenInvitePicker();
        }

        static void OpenInvitePicker()
        {
            if (!CanInvite) return;   // guests (and a host without a Steam lobby) never open the invite list
            SteamLobby.OnlineFriends(friendTmp);
            ShowInvitePicker(false);
        }

        /// Builds the invite list from friendTmp. Every row: dot, name, „im Spiel“, and the button „Einladen“ – the same
        /// text, colour and state forever; each click sends one invite (SendInvite) and a tiny toast.
        static void ShowInvitePicker(bool debug)
        {
            if (!EnsureLayer()) return;
            if (invitePicker != null) invitePicker.RemoveFromHierarchy();
            invitePicker = null;
            inviteButtons.Clear();
            debugPicker = debug;

            invitePicker = new VisualElement { name = "sb-net-invite" };
            NetLobbyStyle.Fill(invitePicker);
            invitePicker.style.backgroundColor = NetLobbyStyle.Backdrop;
            invitePicker.style.alignItems = Align.Center;
            invitePicker.style.justifyContent = Justify.Center;
            invitePicker.pickingMode = PickingMode.Position;

            var card = NetLobbyStyle.CardBox(24f, 24f);
            card.style.width = 640f;
            card.style.maxWidth = Length.Percent(92f);
            card.style.maxHeight = Length.Percent(86f);
            var head = NetLobbyStyle.Row();
            head.Add(NetLobbyStyle.Text(Loc.Tr("Freunde einladen", "Invite friends"), 30f, NetLobbyStyle.Ink, true));
            head.Add(NetLobbyStyle.Flex());
            var close = NetLobbyStyle.Btn("×", CloseInvitePicker, new Color(0.106f, 0.141f, 0.2f, 0.07f), NetLobbyStyle.Ink, 26f);
            close.style.width = 44f;
            close.style.height = 44f;
            NetLobbyStyle.Pad(close, 0f, 0f);
            NetLobbyStyle.Radius(close, 22f);
            head.Add(close);
            card.Add(head);
            var sub = NetLobbyStyle.Para(Loc.Tr("Steam-Freunde, die online sind. Sie müssen SEALEBRITIES selbst gestartet haben und nehmen die Einladung dann im Steam-Chat an. Nicht angekommen? Einfach nochmal einladen.",
                                                "Steam friends who are online. They need to have started SEALEBRITIES and then accept the invite in the Steam chat. Didn't arrive? Just invite again."), 15f, NetLobbyStyle.Muted);
            sub.style.marginTop = 4f;
            sub.style.marginBottom = 12f;
            card.Add(sub);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 460f;
            scroll.style.flexShrink = 1;
            if (friendTmp.Count == 0)
                scroll.Add(NetLobbyStyle.Para(Loc.Tr("Gerade ist keiner deiner Steam-Freunde online.", "None of your Steam friends is online right now."), 17f, NetLobbyStyle.Muted));
            for (int i = 0; i < friendTmp.Count; i++)
            {
                var f = friendTmp[i];
                var r = NetLobbyStyle.Row();
                r.style.backgroundColor = NetLobbyStyle.White;
                NetLobbyStyle.Radius(r, 14f);
                NetLobbyStyle.Pad(r, 8f, 14f);
                r.style.marginBottom = 6f;
                r.Add(NetLobbyStyle.Dot(f.sameGame ? NetLobbyStyle.Turquoise : new Color(0.106f, 0.141f, 0.2f, 0.25f), 12f));
                r.Add(NetLobbyStyle.Gap(10f, true));
                var name = NetLobbyStyle.Text(f.name, 19f, NetLobbyStyle.Ink, true);
                name.style.flexShrink = 1;
                name.style.overflow = Overflow.Hidden;
                name.style.textOverflow = TextOverflow.Ellipsis;
                r.Add(name);
                if (f.sameGame)
                {
                    r.Add(NetLobbyStyle.Gap(8f, true));
                    r.Add(NetLobbyStyle.Chip(Loc.Tr("im Spiel", "in game"), NetLobbyStyle.Turquoise, NetLobbyStyle.White, 13f));
                }
                r.Add(NetLobbyStyle.Flex());
                ulong id = f.id;
                Action send = () => SendInvite(id);
                var btn = NetLobbyStyle.Accent(InviteLabel, send, 17f);
                btn.name = "sb-invite-btn";
                btn.userData = send;   // tests click it through this (never a real Steam invite there)
                NetLobbyStyle.Pad(btn, 6f, 16f);
                r.Add(btn);
                inviteButtons.Add(btn);
                scroll.Add(r);
            }
            card.Add(scroll);
            invitePicker.Add(card);
            layer.Add(invitePicker);
        }

        /// The invite button's one and only text.
        public static string InviteLabel => Loc.Tr("Einladen", "Invite");

        /// One click = one invite. The button stays exactly as it is (GDD §19b: invite again whenever you like).
        static void SendInvite(ulong friend)
        {
            if (!InviteRoleAllowed) return;   // never from a guest
            bool ok;
            try { ok = InviteSenderOverride != null ? InviteSenderOverride(friend) : CanInvite && SteamLobby.InviteFriend(friend); }
            catch (Exception) { ok = false; }
            if (ok) GameEvents.RaiseToast(Loc.Tr("Einladung gesendet", "Invite sent"));
            else GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Einladung fehlgeschlagen", "Invite failed"));
        }

        static void CloseInvitePicker()
        {
            if (invitePicker != null) invitePicker.RemoveFromHierarchy();
            invitePicker = null;
            debugPicker = false;
            inviteButtons.Clear();
            friendTmp.Clear();
        }

        // ------------------------------------------------------------------ test hooks (AutoPlaytest / AutoShot)
        /// Shows the invite list with these (fake) friends WITHOUT Steam – refused for guests (InviteRoleAllowed).
        /// Set InviteSenderOverride first: the buttons must never send a real invite in a test.
        public static bool DebugShowInvitePicker(List<SteamLobby.FriendEntry> friends)
        {
            if (!InviteRoleAllowed) return false;
            friendTmp.Clear();
            if (friends != null) friendTmp.AddRange(friends);
            ShowInvitePicker(true);
            return invitePicker != null;
        }

        /// The real path of the „Freunde einladen“ button (refused unless CanInvite).
        public static bool DebugTryOpenInvitePicker()
        {
            OpenInvitePicker();
            return invitePicker != null;
        }

        public static bool InvitePickerOpen => invitePicker != null;
        public static VisualElement DebugInvitePickerRoot => invitePicker;
        /// The rows' „Einladen“ buttons of the open list (the click action is in Button.userData).
        public static List<Button> DebugInviteButtons => inviteButtons;
        public static void DebugCloseInvitePicker() => CloseInvitePicker();
    }
}
