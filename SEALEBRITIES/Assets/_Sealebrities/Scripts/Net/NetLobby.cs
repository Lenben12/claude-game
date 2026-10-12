using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// "Mit Freunden" screens (Net.OpenLobby): the profile card (1c: live 3D thumbnail of your bean diver, your name and
    /// „Charakter anpassen“ → the character screen), then the clear first choice "Über Steam (empfohlen)" when Steam
    /// runs (host a Steam lobby + friends who host right now, NetLobbySteam.cs) and "Per IP / Heimnetz" second: Spiel
    /// hosten (round 3 step 6b: pick WHICH of the four save slots to host – a used slot continues that world, an empty
    /// one starts a new world there with 3 difficulty cards; port, own addresses + internet hint), Beitreten (IP[:port] +
    /// LAN list), connecting state, notices (session ended, errors).
    /// In game: the Steam invite card while the pause menu is open in a Steam session ("Freunde einladen" – only the
    /// HOST, the creator of the world, can invite), the (disabled) co-op card and the F3 diagnostics overlay. Built into
    /// the UI module's document as its own layer.
    public static partial class NetLobby
    {
        enum Page { Home, Host, Join, Connecting, SteamHost }

        // 1c profile card: live thumbnail (Player module's CharacterPreview) – only while the home page is shown
        const int ThumbTextureSize = 256;
        const float ThumbSize = 220f;
        static ICharacterPreview lobbyPreview;
        static bool characterFromLobby;
        static float characterOpenedAt;

        static readonly Color[] Swatches =
        {
            NetLobbyStyle.Hex(0xEE7F2D), NetLobbyStyle.Hex(0x3FB8C4), NetLobbyStyle.Hex(0xE0607E), NetLobbyStyle.Hex(0xF2C94C),
            NetLobbyStyle.Hex(0x7BC96F), NetLobbyStyle.Hex(0x8C7BD9), NetLobbyStyle.Hex(0x4A7FD6), NetLobbyStyle.Hex(0xF2F2EE)
        };

        const string LastJoinKey = "SB_LastJoinAddress";

        static VisualElement layer, lobby, body, lanBox, notice, coopCard;
        static Label statusLabel, connectingLabel, overlay;
        static NetLanListener lan;
        static Page page;
        static bool open, dirty, installed, profileDirty;
        /// The save slot the host pages host (1..4): used = continue that world, empty = a new world there.
        static int hostSlot = 1;
        static Difficulty hostDifficulty = Difficulty.OpenWater;
        static string portText = "27015", joinText = "";
        static float dotsTimer, coopRefresh, overlayRefresh;
        static int dots;

        public static bool IsOpen => open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            layer = null; lobby = null; body = null; lanBox = null; notice = null; coopCard = null;
            statusLabel = null; connectingLabel = null; overlay = null; lan = null;
            page = Page.Home; open = false; dirty = false; installed = false; profileDirty = false;
            hostSlot = 1; hostDifficulty = Difficulty.OpenWater; portText = "27015"; joinText = "";
            dotsTimer = 0f; coopRefresh = 0f; overlayRefresh = 0f; dots = 0;
            lobbyPreview = null; characterFromLobby = false; characterOpenedAt = 0f;
        }

        internal static void Install()
        {
            if (installed) return;
            installed = true;
            GameEvents.LanguageChanged += OnLanguageChanged;
            NetSession.StatusChanged += OnStatusChanged;
            CharacterScreen.Closed += OnCharacterClosed;
            InstallSteam();
        }

        // =================================================================== 1c character screen from the lobby
        static void OpenCharacter()
        {
            if (CharacterScreen.OpenHandler == null) return;
            SaveProfile();
            // one preview stage at a time: the character screen builds its own
            DisposeLobbyPreview();
            characterFromLobby = true;
            characterOpenedAt = Time.unscaledTime;
            if (lobby != null) lobby.style.display = DisplayStyle.None;
            try { CharacterScreen.Open(CharacterScreenContext.Lobby); }
            catch (Exception e)
            {
                Debug.LogException(e);
                RestoreAfterCharacter();
            }
        }

        static void OnCharacterClosed()
        {
            if (characterFromLobby) RestoreAfterCharacter();
        }

        static void RestoreAfterCharacter()
        {
            characterFromLobby = false;
            if (!open) return;
            if (lobby != null) lobby.style.display = DisplayStyle.Flex;
            dirty = true;   // new name / look on the profile card
        }

        static bool EnsureLobbyPreview()
        {
            if (lobbyPreview != null) return lobbyPreview.Texture != null;
            var factory = CharacterPreviewFactory.Create;
            if (factory == null) return false;
            try
            {
                lobbyPreview = factory(ThumbTextureSize);
                if (lobbyPreview == null) return false;
                lobbyPreview.SetLook(Settings.Look);
                lobbyPreview.Focus(PreviewFocus.Body);
                return lobbyPreview.Texture != null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] lobby character preview failed: " + e.Message);
                DisposeLobbyPreview();
                return false;
            }
        }

        static void DisposeLobbyPreview()
        {
            var p = lobbyPreview;
            lobbyPreview = null;
            if (p == null) return;
            try { p.Dispose(); }
            catch (Exception e) { Debug.LogWarning("[Net] lobby character preview dispose: " + e.Message); }
        }

        static void OnLanguageChanged()
        {
            dirty = true;
            coopRefresh = 0f;
            steamCardDirty = true;
        }

        // =================================================================== layer
        static bool EnsureLayer()
        {
            if (layer != null && layer.panel != null) return true;
            var ctrl = UIModule.Controller;
            if (ctrl == null || ctrl.Root == null) return false;
            var root = ctrl.Root;
            layer = new VisualElement { name = "sb-layer-net", pickingMode = PickingMode.Ignore };
            NetLobbyStyle.Fill(layer);
            NetLobbyStyle.ApplyFont(layer);
            // above menu and pause, below the loading screen and the fader
            int index = -1;
            for (int i = 0; i < root.childCount; i++)
            {
                string n = root[i].name;
                if (n == "sb-layer-loading" || n == "sb-layer-fade") { index = i; break; }
            }
            if (index >= 0) root.Insert(index, layer);
            else root.Add(layer);
            return true;
        }

        // =================================================================== lobby
        /// Net.OpenLobby – called by the main menu ("Mit Freunden").
        public static void Open()
        {
            var gm = GameManager.I;
            if (gm != null && gm.InGame) return;   // only from the main menu
            if (!EnsureLayer())
            {
                Debug.LogWarning("[Net] lobby: the UI is not installed yet.");
                return;
            }
            HideNotice();
            open = true;
            page = NetSession.IsConnecting || NetSession.IsRetrying ? Page.Connecting : Page.Home;
            steamConnecting = page == Page.Connecting && NetSession.IsSteam;
            friendRefresh = 0f;
            if (page == Page.Home) NetSession.ClearStatus();
            portText = NetSession.RequestedPort.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(joinText))
            {
                try { joinText = PlayerPrefs.GetString(LastJoinKey, ""); } catch (Exception) { joinText = ""; }
            }
            hostSlot = DefaultHostSlot();
            StartLan();
            Build();
        }

        /// The slot played last, else the first empty one (a damaged slot is never preselected).
        static int DefaultHostSlot()
        {
            int s = SaveSlots.ContinueSlot;
            if (SaveSlots.Valid(s) && !SaveSlots.Info(s).broken) return s;
            int free = SaveSlots.FirstFree();
            if (free > 0) return free;
            for (int i = 1; i <= SaveSlots.Count; i++) if (!SaveSlots.Info(i).broken) return i;
            return 1;
        }

        /// The selected host slot holds a world (→ continue it) – else a new world starts in it.
        static bool HostSlotUsed => SaveSlots.Exists(hostSlot);

        /// Host pages: „Welche Welt?“ – the four save slots as small tiles (used: day, followers, play time,
        /// achievements; empty: „+ Neue Welt“). The selected one is hosted.
        static void BuildHostSlotPicker()
        {
            if (!SaveSlots.Valid(hostSlot) || SaveSlots.Info(hostSlot).broken) hostSlot = DefaultHostSlot();
            body.Add(NetLobbyStyle.Text(Loc.Tr("Welche Welt hostest du?", "Which world do you host?"), 20f, NetLobbyStyle.Ink, true));
            body.Add(NetLobbyStyle.Gap(8f));
            var row = NetLobbyStyle.Row();
            row.style.alignItems = Align.Stretch;
            for (int i = 1; i <= SaveSlots.Count; i++)
            {
                if (i > 1) row.Add(NetLobbyStyle.Gap(10f, true));
                row.Add(HostSlotTile(i));
            }
            body.Add(row);
            body.Add(NetLobbyStyle.Gap(16f));
        }

        static VisualElement HostSlotTile(int slot)
        {
            var info = SaveSlots.Info(slot);
            bool sel = slot == hostSlot;
            var t = NetLobbyStyle.Col();
            t.name = "sb-host-slot";
            t.style.flexGrow = 1;
            t.style.flexBasis = 0;
            t.style.minHeight = 100f;
            NetLobbyStyle.Radius(t, 16f);
            NetLobbyStyle.Pad(t, 10f, 12f);
            NetLobbyStyle.Border(t, sel ? 3f : 0f, sel ? NetLobbyStyle.TurquoiseDark : Color.clear);
            Color bg = sel ? NetLobbyStyle.Lighten(NetLobbyStyle.Turquoise, 0.72f) : new Color(1f, 1f, 1f, 0.75f);
            NetLobbyStyle.Clickable(t, () =>
            {
                if (SaveSlots.Info(slot).broken) return;
                hostSlot = slot;
                dirty = true;
            }, bg, sel ? bg : NetLobbyStyle.White);

            var top = NetLobbyStyle.Row();
            top.pickingMode = PickingMode.Ignore;
            top.Add(NetLobbyStyle.Chip("#" + slot, sel ? NetLobbyStyle.TurquoiseDark : NetLobbyStyle.Ink, NetLobbyStyle.White, 14f));
            top.Add(NetLobbyStyle.Flex());
            if (info.used && !info.broken) top.Add(NetLobbyStyle.Text(Loc.DifficultyName(info.difficulty), 14f, NetLobbyStyle.Muted, true));
            t.Add(top);
            t.Add(NetLobbyStyle.Gap(6f));
            if (info.broken)
            {
                t.Add(NetLobbyStyle.Text(Loc.Tr("Defekt", "Damaged"), 20f, NetLobbyStyle.Danger, true));
            }
            else if (info.used)
            {
                t.Add(NetLobbyStyle.Text(Loc.Tr("Tag ", "Day ") + info.day, 22f, NetLobbyStyle.Ink, true));
                t.Add(NetLobbyStyle.Text(Loc.Number(info.followers) + " Follower", 15f, NetLobbyStyle.Muted));
                t.Add(NetLobbyStyle.Text(SaveSlots.FormatPlayTime(info.playSeconds) + " · " + info.achievements + "/" + Achievements.Total + " " + Loc.Tr("Erfolge", "achievements"), 14f, NetLobbyStyle.Muted));
            }
            else
            {
                t.Add(NetLobbyStyle.Text("+ " + Loc.Tr("Neue Welt", "New world"), 20f, NetLobbyStyle.TurquoiseDark, true));
                t.Add(NetLobbyStyle.Text(Loc.Tr("leer", "empty"), 15f, NetLobbyStyle.Muted));
            }
            return t;
        }

        /// Opens the lobby directly on the "connecting" page (command line -join).
        public static void OpenConnecting()
        {
            Open();
            if (!open) return;
            steamConnecting = false;
            page = Page.Connecting;
            Build();
        }

        public static void Close()
        {
            if (!open && lobby == null) return;
            open = false;
            characterFromLobby = false;
            DisposeLobbyPreview();
            if (lobby != null) lobby.RemoveFromHierarchy();
            lobby = null;
            body = null;
            lanBox = null;
            friendGamesBox = null;
            statusLabel = null;
            connectingLabel = null;
            StopLan();
            SaveProfile();
        }

        static void StartLan()
        {
            if (lan != null) return;
            lan = new NetLanListener();
            lan.Start();
        }

        static void StopLan()
        {
            lan?.Dispose();
            lan = null;
        }

        static void SaveProfile()
        {
            if (!profileDirty) return;
            profileDirty = false;
            Settings.Save();
        }

        static void Back()
        {
            switch (page)
            {
                case Page.Home: Close(); break;
                case Page.Connecting:
                    NetSession.CancelJoin();
                    page = steamConnecting ? Page.Home : Page.Join;
                    steamConnecting = false;
                    Build();
                    break;
                default:
                    page = Page.Home;
                    NetSession.ClearStatus();
                    Build();
                    break;
            }
        }

        static void Go(Page p)
        {
            page = p;
            NetSession.ClearStatus();
            Build();
        }

        static void Build()
        {
            dirty = false;
            if (!open || !EnsureLayer()) return;
            if (lobby != null) lobby.RemoveFromHierarchy();
            if (page != Page.Home) DisposeLobbyPreview();   // the thumbnail lives on the home page only

            lobby = new VisualElement { name = "sb-net-lobby" };
            NetLobbyStyle.Fill(lobby);
            lobby.style.backgroundColor = NetLobbyStyle.Backdrop;
            lobby.style.alignItems = Align.Center;
            lobby.style.justifyContent = Justify.Center;
            lobby.pickingMode = PickingMode.Position;

            var card = NetLobbyStyle.CardBox(26f, 30f);
            card.style.width = 960f;
            card.style.maxWidth = Length.Percent(94f);
            card.style.maxHeight = Length.Percent(94f);
            card.style.overflow = Overflow.Hidden;
            lobby.Add(card);

            // header
            var head = NetLobbyStyle.Row();
            if (page == Page.Host || page == Page.Join || page == Page.SteamHost)
            {
                var back = NetLobbyStyle.Ghost(Loc.Tr("Zurück", "Back"), Back, 17f);
                back.style.marginRight = 16f;
                head.Add(back);
            }
            string title;
            switch (page)
            {
                case Page.Host: title = Loc.Tr("Hosten per IP / Heimnetz", "Host by IP / home network"); break;
                case Page.Join: title = Loc.Tr("Beitreten per IP / Heimnetz", "Join by IP / home network"); break;
                case Page.SteamHost: title = Loc.Tr("Über Steam hosten", "Host via Steam"); break;
                case Page.Connecting: title = Loc.Tr("Verbinden", "Connecting"); break;
                default: title = Loc.Tr("Mit Freunden tauchen", "Dive with friends"); break;
            }
            head.Add(NetLobbyStyle.Text(title, 38f, NetLobbyStyle.Ink, true));
            head.Add(NetLobbyStyle.Flex());
            var close = NetLobbyStyle.Btn("×", page == Page.Connecting ? (Action)Back : Close, new Color(0.106f, 0.141f, 0.2f, 0.07f), NetLobbyStyle.Ink, 26f);
            close.style.width = 48f;
            close.style.height = 48f;
            NetLobbyStyle.Pad(close, 0f, 0f);
            NetLobbyStyle.Radius(close, 24f);
            head.Add(close);
            card.Add(head);

            if (page == Page.Home)
            {
                var sub = NetLobbyStyle.Text(Loc.Tr("Ein Kanal · eine Kasse · bis zu 4 Taucher", "One channel · one wallet · up to 4 divers"), 18f, NetLobbyStyle.Muted);
                sub.style.marginTop = 4f;
                card.Add(sub);
            }
            card.Add(NetLobbyStyle.Gap(20f));

            body = NetLobbyStyle.Col();
            body.style.flexShrink = 1;
            card.Add(body);
            friendGamesBox = null;
            switch (page)
            {
                case Page.Host: BuildHost(); break;
                case Page.Join: BuildJoin(); break;
                case Page.SteamHost: BuildSteamHost(); break;
                case Page.Connecting: BuildConnecting(); break;
                default: BuildHome(); break;
            }

            statusLabel = NetLobbyStyle.Para("", 18f, NetLobbyStyle.Muted);
            statusLabel.style.marginTop = 14f;
            card.Add(statusLabel);
            UpdateStatus();

            if (characterFromLobby) lobby.style.display = DisplayStyle.None;
            layer.Add(lobby);
        }

        // ------------------------------------------------------------------ home: profile + choice
        /// The name shown on the profile card: the chosen name, else the Steam persona name, else "Taucher".
        static string ProfileName()
        {
            string shownName = Settings.Data.playerName;
            if (string.IsNullOrWhiteSpace(shownName) && !string.IsNullOrWhiteSpace(SteamService.PersonaName))
            {
                shownName = SteamService.PersonaName.Trim();
                if (shownName.Length > Settings.MaxNameLength) shownName = shownName.Substring(0, Settings.MaxNameLength);
            }
            if (!string.IsNullOrWhiteSpace(NetBoot.NameOverride)) shownName = NetBoot.NameOverride;
            return string.IsNullOrWhiteSpace(shownName) ? Loc.Tr("Taucher", "Diver") : shownName.Trim();
        }

        /// 1c: profile card – live thumbnail of the bean diver (220 × 220), name, „Charakter anpassen“. The look and the
        /// name are edited on the character screen; this card only shows them.
        static void BuildProfileCard()
        {
            var prof = NetLobbyStyle.Row();
            prof.style.alignItems = Align.Center;
            prof.style.backgroundColor = new Color(1f, 1f, 1f, 0.75f);
            NetLobbyStyle.Radius(prof, 18f);
            NetLobbyStyle.Pad(prof, 12f, 14f);
            body.Add(prof);

            var frame = new VisualElement { name = "sb-net-thumb" };
            frame.style.width = ThumbSize;
            frame.style.height = ThumbSize;
            frame.style.flexShrink = 0;
            frame.style.overflow = Overflow.Hidden;
            NetLobbyStyle.Radius(frame, 18f);
            // the preview stage's warm gradient (#F2C48A → #E98F6A) as the frame colour while it renders
            NetLobbyStyle.Clickable(frame, OpenCharacter, NetLobbyStyle.Hex(0xF2C48A), NetLobbyStyle.Hex(0xF5D3A5));
            if (EnsureLobbyPreview())
            {
                var img = new Image { image = lobbyPreview.Texture, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
                img.style.flexGrow = 1;
                frame.Add(img);
            }
            else frame.Add(BeanBadge(Settings.Look));
            prof.Add(frame);
            prof.Add(NetLobbyStyle.Gap(22f, true));

            var col = NetLobbyStyle.Col();
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(NetLobbyStyle.Text(Loc.Tr("Dein Taucher", "Your diver"), 17f, NetLobbyStyle.Muted, true));
            var nameLabel = NetLobbyStyle.Text(ProfileName(), 34f, NetLobbyStyle.Ink, true);
            nameLabel.style.overflow = Overflow.Hidden;
            nameLabel.style.textOverflow = TextOverflow.Ellipsis;
            col.Add(nameLabel);
            if (!string.IsNullOrWhiteSpace(NetBoot.NameOverride))
                col.Add(NetLobbyStyle.Text("(-netname " + NetBoot.NameOverride + ")", 15f, NetLobbyStyle.Muted));
            var hint = NetLobbyStyle.Para(Loc.Tr("Anzug, Hut, Gesicht – deine Freunde sehen dich genau so.",
                                                 "Suit, hat, face – your friends see you exactly like this."), 16f, NetLobbyStyle.Muted);
            hint.style.marginTop = 4f;
            col.Add(hint);
            col.Add(NetLobbyStyle.Gap(14f));
            var btn = NetLobbyStyle.Accent(Loc.Tr("Charakter anpassen", "Customise character"), OpenCharacter, 21f);
            btn.style.alignSelf = Align.FlexStart;
            NetLobbyStyle.Pad(btn, 12f, 26f);
            col.Add(btn);
            prof.Add(col);
        }

        /// Flat stand-in while no 3D preview exists: a How-to-Fish bean (suit body, face, hat brim) in UI shapes.
        static VisualElement BeanBadge(DiverLook look)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.style.flexGrow = 1;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.FlexEnd;
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.style.width = 74f;
            face.style.height = 52f;
            face.style.marginBottom = -6f;
            NetLobbyStyle.Radius(face, 26f);
            var skins = CosmeticsDatabase.SkinTones;
            face.style.backgroundColor = skins != null && skins.Length > 0 ? skins[Mathf.Clamp(look.skin, 0, skins.Length - 1)] : NetLobbyStyle.Hex(0xE8C08A);
            var bean = new VisualElement { pickingMode = PickingMode.Ignore };
            bean.style.width = 120f;
            bean.style.height = 150f;
            bean.style.borderTopLeftRadius = 60f;
            bean.style.borderTopRightRadius = 60f;
            bean.style.borderBottomLeftRadius = 30f;
            bean.style.borderBottomRightRadius = 30f;
            bean.style.backgroundColor = NetLobbyStyle.Hex(0x343849);
            var band = new VisualElement { pickingMode = PickingMode.Ignore };
            band.style.marginTop = 62f;
            band.style.height = 30f;
            band.style.backgroundColor = new Color(look.suit.r, look.suit.g, look.suit.b, 1f);
            bean.Add(band);
            box.Add(face);
            box.Add(bean);
            face.BringToFront();
            return box;
        }

        static void BuildHome()
        {
            if (CharacterScreen.OpenHandler != null)
            {
                BuildProfileCard();
                BuildChoices();
                return;
            }
            // no character screen installed (UI module older than round 2): the quick look editor of round 1
            BuildQuickLook();
            BuildChoices();
        }

        static void BuildQuickLook()
        {
            var prof = NetLobbyStyle.Col();
            prof.style.backgroundColor = new Color(1f, 1f, 1f, 0.75f);
            NetLobbyStyle.Radius(prof, 18f);
            NetLobbyStyle.Pad(prof, 16f, 20f);
            body.Add(prof);

            var nameRow = NetLobbyStyle.Row();
            var nl = NetLobbyStyle.Text(Loc.Tr("Name", "Name"), 19f, NetLobbyStyle.Muted, true);
            nl.style.width = 90f;
            nameRow.Add(nl);
            // no name chosen yet: the Steam name is the default (editable; only saved when the player changes it)
            string shownName = Settings.Data.playerName;
            if (string.IsNullOrWhiteSpace(shownName) && !string.IsNullOrWhiteSpace(SteamService.PersonaName))
            {
                shownName = SteamService.PersonaName.Trim();
                if (shownName.Length > Settings.MaxNameLength) shownName = shownName.Substring(0, Settings.MaxNameLength);
            }
            var nameField = NetLobbyStyle.Field(shownName, Settings.MaxNameLength, 300f);
            nameField.RegisterValueChangedCallback(e =>
            {
                string v = e.newValue ?? "";
                if (v.Length > Settings.MaxNameLength) v = v.Substring(0, Settings.MaxNameLength);
                Settings.Data.playerName = v.Trim();
                Settings.MarkDirty();
                profileDirty = true;
                Net.LocalName = NetSession.LocalName();
            });
            nameRow.Add(nameField);
            if (!string.IsNullOrWhiteSpace(NetBoot.NameOverride))
            {
                var ov = NetLobbyStyle.Text("(-netname " + NetBoot.NameOverride + ")", 15f, NetLobbyStyle.Muted);
                ov.style.marginLeft = 10f;
                nameRow.Add(ov);
            }
            nameRow.Add(NetLobbyStyle.Flex());
            for (int i = 0; i < Swatches.Length; i++)
            {
                var c = Swatches[i];
                bool sel = ColorClose(Settings.Data.suitColor, c);
                var sw = new VisualElement();
                sw.style.width = 34f;
                sw.style.height = 34f;
                sw.style.marginLeft = 6f;
                NetLobbyStyle.Radius(sw, 17f);
                NetLobbyStyle.Border(sw, sel ? 4f : 2f, sel ? NetLobbyStyle.Ink : new Color(1f, 1f, 1f, 0.9f));
                NetLobbyStyle.Clickable(sw, () =>
                {
                    Settings.Data.suitColor = c;
                    Settings.MarkDirty();
                    profileDirty = true;
                    dirty = true;
                }, c, NetLobbyStyle.Lighten(c, 0.2f));
                nameRow.Add(sw);
            }
            prof.Add(nameRow);

            var hatRow = NetLobbyStyle.Row();
            hatRow.style.marginTop = 12f;
            var hl = NetLobbyStyle.Text(Loc.Tr("Hut", "Hat"), 19f, NetLobbyStyle.Muted, true);
            hl.style.width = 90f;
            hatRow.Add(hl);
            string[] hats =
            {
                Loc.Tr("Ohne", "None"), Loc.Tr("Fischerhut", "Bucket hat"), Loc.Tr("Wikinger", "Viking"),
                Loc.Tr("Haiflosse", "Shark fin"), Loc.Tr("Ente", "Duck")
            };
            for (int i = 0; i < hats.Length; i++)
            {
                int h = i;
                bool sel = Settings.Data.hat == i;
                var chip = NetLobbyStyle.Text(hats[i], 17f, sel ? NetLobbyStyle.White : NetLobbyStyle.Ink, true);
                NetLobbyStyle.Radius(chip, 999f);
                NetLobbyStyle.Pad(chip, 6f, 14f);
                chip.style.marginRight = 8f;
                NetLobbyStyle.Clickable(chip, () =>
                {
                    Settings.Data.hat = h;
                    Settings.MarkDirty();
                    profileDirty = true;
                    dirty = true;
                }, sel ? NetLobbyStyle.TurquoiseDark : new Color(0.106f, 0.141f, 0.2f, 0.07f),
                   sel ? NetLobbyStyle.TurquoiseDark : new Color(0.106f, 0.141f, 0.2f, 0.14f));
                hatRow.Add(chip);
            }
            prof.Add(hatRow);
        }

        /// Steam first (when it runs), IP / home network second.
        static void BuildChoices()
        {
            body.Add(NetLobbyStyle.Gap(20f));
            if (SteamService.Available)
            {
                // 1st choice: Steam (no IP, no port forwarding, invite from the friends list)
                BuildSteamHomeSection();
                body.Add(NetLobbyStyle.Gap(16f));
                BuildIpSection(true);
                return;
            }
            var why = NetLobbyStyle.Para(Loc.Tr("Koop über Steam ist gerade nicht möglich: ", "Co-op via Steam is not possible right now: ") + SteamService.UnavailableText(),
                                         16f, NetLobbyStyle.Muted);
            why.style.marginBottom = 12f;
            body.Add(why);
            BuildIpSection(false);
        }

        /// "Per IP / Heimnetz": big tiles when Steam is unavailable, a compact second row below the Steam section otherwise.
        static void BuildIpSection(bool compact)
        {
            if (compact)
            {
                var head = NetLobbyStyle.Row();
                head.Add(NetLobbyStyle.Text(Loc.Tr("Per IP / Heimnetz", "By IP / home network"), 20f, NetLobbyStyle.Ink, true));
                head.Add(NetLobbyStyle.Gap(10f, true));
                head.Add(NetLobbyStyle.Text(Loc.Tr("gleiches WLAN, VPN oder ohne Steam", "same Wi-Fi, VPN or without Steam"), 16f, NetLobbyStyle.Muted));
                body.Add(head);
                body.Add(NetLobbyStyle.Gap(8f));
                var row = NetLobbyStyle.Row();
                row.style.alignItems = Align.Stretch;
                row.Add(SmallTile("+", Loc.Tr("Hosten per IP", "Host by IP"), Loc.Tr("Port + Adresse teilen", "Share port + address"),
                                  NetLobbyStyle.TurquoiseDark, () => Go(Page.Host)));
                row.Add(NetLobbyStyle.Gap(14f, true));
                row.Add(SmallTile("»", Loc.Tr("Beitreten per IP", "Join by IP"), Loc.Tr("IP eintippen oder Heimnetz-Liste", "Type the IP or pick from the LAN list"),
                                  NetLobbyStyle.Turquoise, () => Go(Page.Join)));
                body.Add(row);
                return;
            }
            var tiles = NetLobbyStyle.Row();
            tiles.style.alignItems = Align.Stretch;
            tiles.Add(Tile("+", Loc.Tr("Spiel hosten", "Host a game"), Loc.Tr("Deine Insel – Freunde kommen dazu", "Your island – friends drop in"),
                           NetLobbyStyle.Coral, () => Go(Page.Host)));
            tiles.Add(NetLobbyStyle.Gap(18f, true));
            tiles.Add(Tile("»", Loc.Tr("Beitreten", "Join"), Loc.Tr("Per IP oder im Heimnetz", "By IP or on your home network"),
                           NetLobbyStyle.Turquoise, () => Go(Page.Join)));
            body.Add(tiles);
        }

        /// Compact one-line tile (glyph circle + title + subtitle).
        static VisualElement SmallTile(string glyph, string title, string sub, Color bg, Action onClick)
        {
            var t = NetLobbyStyle.Row();
            t.style.flexGrow = 1;
            t.style.flexBasis = 0;
            t.style.minHeight = 76f;
            NetLobbyStyle.Pad(t, 10f, 16f);
            NetLobbyStyle.Radius(t, 18f);
            NetLobbyStyle.Clickable(t, onClick, bg, NetLobbyStyle.Lighten(bg, 0.12f));
            var circle = new VisualElement { pickingMode = PickingMode.Ignore };
            circle.style.width = 44f;
            circle.style.height = 44f;
            circle.style.flexShrink = 0;
            NetLobbyStyle.Radius(circle, 22f);
            circle.style.backgroundColor = new Color(1f, 1f, 1f, 0.28f);
            circle.style.alignItems = Align.Center;
            circle.style.justifyContent = Justify.Center;
            var g = NetLobbyStyle.Text(glyph, 28f, NetLobbyStyle.White, true);
            g.style.unityTextAlign = TextAnchor.MiddleCenter;
            g.style.marginTop = -3f;
            circle.Add(g);
            t.Add(circle);
            t.Add(NetLobbyStyle.Gap(12f, true));
            var col = NetLobbyStyle.Col();
            col.pickingMode = PickingMode.Ignore;
            col.style.flexShrink = 1;
            col.Add(NetLobbyStyle.Text(title, 22f, NetLobbyStyle.White, true));
            var st = NetLobbyStyle.Para(sub, 15f, new Color(1f, 1f, 1f, 0.92f));
            st.style.marginTop = 1f;
            col.Add(st);
            t.Add(col);
            return t;
        }

        static VisualElement Tile(string glyph, string title, string sub, Color bg, Action onClick, float height = 190f)
        {
            var t = NetLobbyStyle.Col();
            t.style.flexGrow = 1;
            t.style.flexBasis = 0;
            t.style.height = height;
            t.style.justifyContent = Justify.Center;
            t.style.alignItems = Align.Center;
            NetLobbyStyle.Radius(t, 24f);
            NetLobbyStyle.Clickable(t, onClick, bg, NetLobbyStyle.Lighten(bg, 0.12f));

            var circle = new VisualElement { pickingMode = PickingMode.Ignore };
            circle.style.width = 64f;
            circle.style.height = 64f;
            NetLobbyStyle.Radius(circle, 32f);
            circle.style.backgroundColor = new Color(1f, 1f, 1f, 0.28f);
            circle.style.alignItems = Align.Center;
            circle.style.justifyContent = Justify.Center;
            var g = NetLobbyStyle.Text(glyph, 40f, NetLobbyStyle.White, true);
            g.style.unityTextAlign = TextAnchor.MiddleCenter;
            g.style.marginTop = -4f;
            circle.Add(g);
            t.Add(circle);

            var tt = NetLobbyStyle.Text(title, 30f, NetLobbyStyle.White, true);
            tt.style.marginTop = 10f;
            t.Add(tt);
            var st = NetLobbyStyle.Text(sub, 17f, new Color(1f, 1f, 1f, 0.92f));
            st.style.marginTop = 2f;
            t.Add(st);
            return t;
        }

        // ------------------------------------------------------------------ host page
        static void BuildHost()
        {
            BuildHostSlotPicker();
            BuildHostDifficulty();

            var info = NetLobbyStyle.Col();
            info.style.backgroundColor = new Color(1f, 1f, 1f, 0.75f);
            NetLobbyStyle.Radius(info, 18f);
            NetLobbyStyle.Pad(info, 14f, 20f);
            body.Add(info);

            var portRow = NetLobbyStyle.Row();
            var pl = NetLobbyStyle.Text("Port", 19f, NetLobbyStyle.Muted, true);
            pl.style.width = 70f;
            portRow.Add(pl);
            var pf = NetLobbyStyle.Field(portText, 5, 130f);
            pf.RegisterValueChangedCallback(e => portText = e.newValue);
            portRow.Add(pf);
            portRow.Add(NetLobbyStyle.Gap(22f, true));
            portRow.Add(NetLobbyStyle.Text(Loc.Tr("Deine Adresse:", "Your address:"), 19f, NetLobbyStyle.Muted, true));
            portRow.Add(NetLobbyStyle.Gap(8f, true));
            var addrs = NetAddresses.LocalIPv4();
            var chips = NetLobbyStyle.Row();
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.flexShrink = 1;
            if (addrs.Count == 0) chips.Add(NetLobbyStyle.Chip(Loc.Tr("keine Netzwerkverbindung", "no network"), NetLobbyStyle.Danger, NetLobbyStyle.White));
            for (int i = 0; i < addrs.Count && i < 4; i++)
            {
                var a = addrs[i];
                var chip = NetLobbyStyle.Chip(a.vpn ? a.address + "  VPN" : a.address, a.vpn ? NetLobbyStyle.Sun : NetLobbyStyle.Turquoise,
                                              a.vpn ? NetLobbyStyle.Ink : NetLobbyStyle.White, 17f);
                chip.style.marginRight = 6f;
                chip.style.marginBottom = 4f;
                chip.tooltip = a.adapter;
                chips.Add(chip);
            }
            portRow.Add(chips);
            info.Add(portRow);

            var lanHint = NetLobbyStyle.Para(Loc.Tr("Gleiches WLAN: Freunde öffnen „Beitreten“ – dein Spiel steht in der Liste.",
                                                    "Same Wi-Fi: friends open “Join” – your game shows up in the list."), 16f, NetLobbyStyle.Muted);
            lanHint.style.marginTop = 10f;
            info.Add(lanHint);
            var netHint = NetLobbyStyle.Para(Loc.Tr("Internet: TCP-Port im Router an diesen PC weiterleiten und die öffentliche IP teilen – oder alle nutzen Radmin VPN / ZeroTier und teilen die VPN-Adresse.",
                                                    "Internet: forward the TCP port in your router to this PC and share your public IP – or everyone uses Radmin VPN / ZeroTier and shares the VPN address."), 16f, NetLobbyStyle.Muted);
            netHint.style.marginTop = 4f;
            info.Add(netHint);
            var fwHint = NetLobbyStyle.Para(Loc.Tr("Fragt Windows nach der Firewall: „Zugriff zulassen“.", "If Windows asks about the firewall: “Allow access”."), 16f, NetLobbyStyle.Muted);
            fwHint.style.marginTop = 4f;
            info.Add(fwHint);

            body.Add(NetLobbyStyle.Gap(18f));
            var row = NetLobbyStyle.Row();
            row.Add(NetLobbyStyle.Flex());
            var start = NetLobbyStyle.Primary(HostSlotUsed ? Loc.Tr("Weiterspielen & hosten", "Continue & host") : Loc.Tr("Neue Welt & hosten", "New world & host"), OnHostStart, 24f);
            start.style.minWidth = 300f;
            NetLobbyStyle.Pad(start, 14f, 30f);
            row.Add(start);
            body.Add(row);
        }

        /// An empty host slot: the 3 difficulty cards of the new world.
        static void BuildHostDifficulty()
        {
            if (HostSlotUsed) return;
            var cards = NetLobbyStyle.Row();
            cards.style.alignItems = Align.Stretch;
            var sel = DifficultySettings.Selectable;
            for (int i = 0; i < sel.Length; i++)
            {
                if (i > 0) cards.Add(NetLobbyStyle.Gap(14f, true));
                cards.Add(DifficultyCard(sel[i]));
            }
            body.Add(cards);
            body.Add(NetLobbyStyle.Gap(16f));
        }

        static VisualElement DifficultyCard(Difficulty d)
        {
            bool sel = DifficultySettings.Normalize(hostDifficulty) == d;
            Color accent = d == Difficulty.Snorkeler ? NetLobbyStyle.Turquoise : d == Difficulty.OpenWater ? NetLobbyStyle.Sun : NetLobbyStyle.Coral;
            var c = NetLobbyStyle.Col();
            c.style.flexGrow = 1;
            c.style.flexBasis = 0;
            c.style.minHeight = 150f;
            c.style.overflow = Overflow.Hidden;
            NetLobbyStyle.Radius(c, 18f);
            NetLobbyStyle.Border(c, sel ? 3f : 0f, sel ? NetLobbyStyle.TurquoiseDark : Color.clear);
            c.style.translate = new Translate(0f, sel ? -4f : 0f);
            NetLobbyStyle.Clickable(c, () => { hostDifficulty = d; dirty = true; }, NetLobbyStyle.White, NetLobbyStyle.Lighten(accent, 0.85f));

            var strip = new VisualElement { pickingMode = PickingMode.Ignore };
            strip.style.height = 8f;
            strip.style.backgroundColor = accent;
            c.Add(strip);
            var inner = NetLobbyStyle.Col();
            inner.pickingMode = PickingMode.Ignore;
            NetLobbyStyle.Pad(inner, 12f, 16f);
            inner.Add(NetLobbyStyle.Text(Loc.DifficultyName(d), 26f, NetLobbyStyle.Ink, true));
            var desc = NetLobbyStyle.Para(DifficultySettings.Description(d), 15f, NetLobbyStyle.Muted);
            desc.style.marginTop = 6f;
            inner.Add(desc);
            c.Add(inner);
            return c;
        }

        static void OnHostStart()
        {
            if (!int.TryParse((portText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) || port < 1 || port > 65535)
            {
                NetSession.ReportStatus(Loc.Tr("Port ungültig (1–65535).", "Invalid port (1–65535)."), true);
                return;
            }
            SaveProfile();
            if (!NetSession.StartHost(port, NetBoot.BindOverride, out string err))
            {
                NetSession.ReportStatus(err, true);
                return;
            }
            int slot = hostSlot;
            bool cont = SaveSlots.Exists(slot);
            var diff = hostDifficulty;
            Close();
            StartHostedGame(slot, cont, diff);
        }

        /// Host (-host command line): continue the last played slot, or a new world in the first free slot (no slot
        /// when saving is suppressed).
        public static void StartHostedGame(bool continueSave, Difficulty difficulty) =>
            StartHostedGame(continueSave ? SaveSlots.ContinueSlot : 0, continueSave, difficulty);

        /// Host: enters the world after StartHost / StartSteamHost – continues save slot `slot` or starts a new world in
        /// it (slot 0 + new = GameManager picks: first free slot, none when saving is off). Only the host saves – into
        /// this slot, with every guest's PlayerRecord.
        public static void StartHostedGame(int slot, bool continueSave, Difficulty difficulty)
        {
            Fader.FadeThrough(() =>
            {
                var gm = GameManager.I;
                if (gm == null) return;
                bool ok = true;
                if (continueSave) ok = gm.LoadSlot(slot);
                else if (SaveSlots.Valid(slot)) gm.NewGame(difficulty, slot);
                else gm.NewGame(difficulty);
                if (!ok)
                {
                    NetSession.EndSession(NetEndReason.LocalLeft, null, false);
                    ShowNotice(Loc.Tr("Hosten fehlgeschlagen", "Hosting failed"), Loc.Tr("Der Spielstand konnte nicht geladen werden.", "Could not load the save."));
                }
            }, 1.6f);
        }

        // ------------------------------------------------------------------ join page
        static void BuildJoin()
        {
            var row = NetLobbyStyle.Row();
            var field = NetLobbyStyle.Field(joinText, 64, 520f, 22f);
            field.RegisterValueChangedCallback(e => joinText = e.newValue);
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) OnJoinTyped();
            });
            row.Add(field);
            row.Add(NetLobbyStyle.Gap(12f, true));
            var go = NetLobbyStyle.Accent(Loc.Tr("Verbinden", "Connect"), OnJoinTyped, 22f);
            NetLobbyStyle.Pad(go, 12f, 26f);
            row.Add(go);
            body.Add(row);
            var ex = NetLobbyStyle.Text(Loc.Tr("z. B. 192.168.1.20  oder  26.10.0.5:27015", "e.g. 192.168.1.20  or  26.10.0.5:27015"), 16f, NetLobbyStyle.Muted);
            ex.style.marginTop = 6f;
            body.Add(ex);

            body.Add(NetLobbyStyle.Gap(22f));
            body.Add(NetLobbyStyle.Text(Loc.Tr("Im Heimnetz gefunden", "Found on your network"), 22f, NetLobbyStyle.Ink, true));
            body.Add(NetLobbyStyle.Gap(8f));
            lanBox = NetLobbyStyle.Col();
            body.Add(lanBox);
            RebuildLan();
        }

        static void RebuildLan()
        {
            if (lanBox == null) return;
            lanBox.Clear();
            if (lan == null || !lan.Available)
            {
                lanBox.Add(NetLobbyStyle.Para(Loc.Tr("LAN-Suche nicht verfügbar – IP oben eintippen.", "LAN search unavailable – type the IP above."), 17f, NetLobbyStyle.Muted));
                return;
            }
            if (lan.Games.Count == 0)
            {
                lanBox.Add(NetLobbyStyle.Para(Loc.Tr("Suche läuft … Der Host muss „Spiel hosten“ gestartet haben.", "Searching … the host must have started “Host a game”."), 17f, NetLobbyStyle.Muted));
                return;
            }
            for (int i = 0; i < lan.Games.Count && i < 6; i++)
            {
                var g = lan.Games[i];
                var r = NetLobbyStyle.Row();
                r.style.backgroundColor = NetLobbyStyle.White;
                NetLobbyStyle.Radius(r, 16f);
                NetLobbyStyle.Pad(r, 10f, 16f);
                r.style.marginBottom = 8f;
                r.Add(NetLobbyStyle.Dot(g.Compatible ? NetLobbyStyle.Turquoise : NetLobbyStyle.Danger, 12f));
                r.Add(NetLobbyStyle.Gap(10f, true));
                r.Add(NetLobbyStyle.Text(g.name, 22f, NetLobbyStyle.Ink, true));
                r.Add(NetLobbyStyle.Gap(10f, true));
                r.Add(NetLobbyStyle.Chip(g.players + "/" + g.maxPlayers, g.Full ? NetLobbyStyle.Danger : NetLobbyStyle.Sand, g.Full ? NetLobbyStyle.White : NetLobbyStyle.Ink));
                r.Add(NetLobbyStyle.Gap(8f, true));
                r.Add(NetLobbyStyle.Text(g.address + (g.port != Net.DefaultPort ? ":" + g.port : ""), 16f, NetLobbyStyle.Muted));
                if (!g.Compatible)
                {
                    r.Add(NetLobbyStyle.Gap(8f, true));
                    r.Add(NetLobbyStyle.Chip(Loc.Tr("Version ", "Version ") + g.version, NetLobbyStyle.Danger, NetLobbyStyle.White, 14f));
                }
                r.Add(NetLobbyStyle.Flex());
                string host = g.address;
                int port = g.port;
                var join = NetLobbyStyle.Accent(Loc.Tr("Beitreten", "Join"), () => StartJoin(host, port), 19f);
                NetLobbyStyle.Enable(join, g.Compatible && !g.Full);
                r.Add(join);
                lanBox.Add(r);
            }
        }

        static void OnJoinTyped()
        {
            if (!NetSession.TryParseAddress(joinText, out string host, out int port, out bool explicitPort, out string err))
            {
                NetSession.ReportStatus(err, true);
                return;
            }
            try { PlayerPrefs.SetString(LastJoinKey, (joinText ?? "").Trim()); PlayerPrefs.Save(); } catch (Exception) { }
            StartJoin(host, port, explicitPort);
        }

        static void StartJoin(string host, int port, bool explicitPort = true)
        {
            SaveProfile();
            steamConnecting = false;
            page = Page.Connecting;
            NetSession.JoinHostPort(host, port, explicitPort);
            Build();
        }

        // ------------------------------------------------------------------ connecting page
        static void BuildConnecting()
        {
            var col = NetLobbyStyle.Col();
            col.style.alignItems = Align.Center;
            NetLobbyStyle.Pad(col, 30f, 0f);
            connectingLabel = NetLobbyStyle.Text(ConnectingText(), 30f, NetLobbyStyle.Ink, true);
            col.Add(connectingLabel);
            col.Add(NetLobbyStyle.Gap(26f));
            var cancel = NetLobbyStyle.Ghost(Loc.Tr("Abbrechen", "Cancel"), Back, 20f);
            NetLobbyStyle.Pad(cancel, 10f, 34f);
            col.Add(cancel);
            body.Add(col);
        }

        static string ConnectingText()
        {
            if (steamConnecting || NetSession.IsSteam) return Loc.Tr("Verbinde über Steam", "Connecting via Steam") + " " + new string('.', 1 + dots % 3);
            string target = string.IsNullOrEmpty(NetSession.JoinTarget) ? "" : " " + NetSession.JoinTarget;
            return Loc.Tr("Verbinde mit", "Connecting to") + target + " " + new string('.', 1 + dots % 3);
        }

        // ------------------------------------------------------------------ status
        static void OnStatusChanged()
        {
            if (!open) return;
            if (page == Page.Connecting && NetSession.Current == NetSession.Phase.Idle && !NetSession.IsRetrying)
            {
                page = steamConnecting ? Page.Home : Page.Join;
                steamConnecting = false;
                dirty = true;
            }
            UpdateStatus();
        }

        static void UpdateStatus()
        {
            if (statusLabel == null) return;
            statusLabel.text = NetSession.Status ?? "";
            statusLabel.style.color = NetSession.StatusIsError ? NetLobbyStyle.Danger : NetLobbyStyle.Muted;
            statusLabel.style.display = string.IsNullOrEmpty(NetSession.Status) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // =================================================================== notice
        public static void ShowNotice(string title, string text)
        {
            if (!EnsureLayer())
            {
                GameEvents.RaiseToast(text);
                return;
            }
            HideNotice();
            notice = new VisualElement { name = "sb-net-notice" };
            NetLobbyStyle.Fill(notice);
            notice.style.backgroundColor = NetLobbyStyle.Backdrop;
            notice.style.alignItems = Align.Center;
            notice.style.justifyContent = Justify.Center;
            notice.pickingMode = PickingMode.Position;
            var c = NetLobbyStyle.CardBox(24f, 28f);
            c.style.width = 640f;
            c.style.maxWidth = Length.Percent(92f);
            c.Add(NetLobbyStyle.Text(title, 32f, NetLobbyStyle.Ink, true));
            var t = NetLobbyStyle.Para(text, 20f, NetLobbyStyle.Muted);
            t.style.marginTop = 10f;
            c.Add(t);
            c.Add(NetLobbyStyle.Gap(22f));
            var ok = NetLobbyStyle.Primary("OK", HideNotice, 22f);
            ok.style.alignSelf = Align.FlexEnd;
            ok.style.minWidth = 140f;
            c.Add(ok);
            notice.Add(c);
            layer.Add(notice);
        }

        static void HideNotice()
        {
            if (notice != null) notice.RemoveFromHierarchy();
            notice = null;
        }

        // =================================================================== frame
        internal static void Tick(float dt)
        {
            TickOverlay(dt);
            TickCoopCard(dt);
            TickSteamCard(dt);

            if (characterFromLobby)
            {
                // the character screen owns Esc while it is open; heal a screen that never opened / never said "closed"
                if (!UIState.CharacterOpen && Time.unscaledTime - characterOpenedAt > 0.6f) RestoreAfterCharacter();
                return;
            }

            if (GameInput.PausePressed)
            {
                if (notice != null) HideNotice();
                else if (invitePicker != null) CloseInvitePicker();
                else if (open) Back();
            }

            if (!open) return;
            var gm = GameManager.I;
            if ((gm != null && gm.InGame) || !UIState.MainMenuOpen)
            {
                Close();
                return;
            }
            if (lan != null && lan.Tick() && page == Page.Join) RebuildLan();
            if (dirty) Build();
            TickSteamHome(dt);
            if (page == Page.Connecting && connectingLabel != null)
            {
                dotsTimer += dt;
                if (dotsTimer >= 0.4f)
                {
                    dotsTimer = 0f;
                    dots++;
                    connectingLabel.text = ConnectingText();
                }
            }
        }

        // ------------------------------------------------------------------ in-game co-op card (pause menu)
        /// Off: the UI's pause menu shows the crew (names, ping, speaking), the host's IP and the leave / end button
        /// itself (UIPauseMenu). Two crew lists on top of each other were confusing. Set to true to bring the card back.
        const bool ShowCoopCard = false;

        static void TickCoopCard(float dt)
        {
            var gm = GameManager.I;
            bool want = ShowCoopCard && Net.IsOnline && UIState.PauseOpen && !UIState.MainMenuOpen && gm != null && gm.InGame;
            if (!want)
            {
                if (coopCard != null)
                {
                    coopCard.RemoveFromHierarchy();
                    coopCard = null;
                }
                coopRefresh = 0f;
                return;
            }
            coopRefresh -= dt;
            if (coopCard != null && coopRefresh > 0f) return;
            coopRefresh = 1f;
            if (!EnsureLayer()) return;
            if (coopCard != null) coopCard.RemoveFromHierarchy();
            coopCard = BuildCoopCard();
            layer.Add(coopCard);
        }

        static VisualElement BuildCoopCard()
        {
            var c = NetLobbyStyle.CardBox(20f, 16f);
            c.style.position = Position.Absolute;
            c.style.right = 32f;
            c.style.top = 110f;
            c.style.width = 380f;
            c.style.backgroundColor = new Color(0.965f, 0.945f, 0.902f, 0.94f);
            c.pickingMode = PickingMode.Position;

            c.Add(NetLobbyStyle.Text(Net.IsHost ? Loc.Tr("Koop · du bist Host", "Co-op · you are host") : Loc.Tr("Koop · Gast", "Co-op · guest"), 22f, NetLobbyStyle.Ink, true));
            c.Add(NetLobbyStyle.Gap(8f));
            for (int i = 0; i < Net.Players.Count; i++)
            {
                var p = Net.Players[i];
                var r = NetLobbyStyle.Row();
                r.style.marginBottom = 4f;
                r.Add(NetLobbyStyle.Dot(p.suit, 14f));
                r.Add(NetLobbyStyle.Gap(8f, true));
                r.Add(NetLobbyStyle.Text(p.name + (p.IsLocal ? Loc.Tr(" (du)", " (you)") : ""), 19f, NetLobbyStyle.Ink, p.IsLocal));
                r.Add(NetLobbyStyle.Flex());
                if (!p.IsLocal || Net.IsClient)
                {
                    int ms = p.IsLocal ? NetDiagnostics.PingMs : p.pingMs;
                    Color pc = ms < 80 ? NetLobbyStyle.Leaf : ms < 180 ? NetLobbyStyle.Sun : NetLobbyStyle.Danger;
                    r.Add(NetLobbyStyle.Chip(ms + " ms", pc, ms < 180 && ms >= 80 ? NetLobbyStyle.Ink : NetLobbyStyle.White, 14f));
                }
                c.Add(r);
            }
            if (Net.IsHost)
            {
                var addrs = NetAddresses.LocalIPv4();
                string a = addrs.Count > 0 ? addrs[0].address : "?";
                for (int i = 1; i < addrs.Count && i < 3; i++) a += ", " + addrs[i].address;
                var t = NetLobbyStyle.Para(Loc.Tr("IP: ", "IP: ") + a + " · Port " + NetSession.HostPort, 16f, NetLobbyStyle.Muted);
                t.style.marginTop = 6f;
                c.Add(t);
            }
            c.Add(NetLobbyStyle.Gap(10f));
            var leave = NetLobbyStyle.Btn(Net.IsHost ? Loc.Tr("Sitzung beenden", "End session") : Loc.Tr("Sitzung verlassen", "Leave session"),
                                          NetSession.LeaveFromUI, new Color(0.898f, 0.282f, 0.302f, 0.12f), NetLobbyStyle.Danger, 17f);
            leave.style.alignSelf = Align.FlexStart;
            c.Add(leave);
            return c;
        }

        // ------------------------------------------------------------------ F3 diagnostics
        static void TickOverlay(float dt)
        {
            if (GameInput.KeyDown(Key.F3)) NetDiagnostics.OverlayVisible = !NetDiagnostics.OverlayVisible;
            if (!NetDiagnostics.OverlayVisible)
            {
                if (overlay != null && overlay.style.display != DisplayStyle.None) overlay.style.display = DisplayStyle.None;
                return;
            }
            if (!EnsureLayer()) return;
            if (overlay == null || overlay.panel == null)
            {
                overlay = new Label { name = "sb-net-overlay", pickingMode = PickingMode.Ignore, enableRichText = false };
                overlay.style.position = Position.Absolute;
                overlay.style.left = 32f;
                overlay.style.top = 260f;
                overlay.style.fontSize = 15f;
                overlay.style.color = Color.white;
                overlay.style.backgroundColor = new Color(0.08f, 0.1f, 0.16f, 0.55f);
                NetLobbyStyle.Radius(overlay, 10f);
                NetLobbyStyle.Pad(overlay, 8f, 12f);
                layer.Add(overlay);
            }
            if (overlay.style.display == DisplayStyle.None) overlay.style.display = DisplayStyle.Flex;
            overlayRefresh -= dt;
            if (overlayRefresh > 0f) return;
            overlayRefresh = 0.5f;
            overlay.BringToFront();
            // transport/session numbers + replication (avatars, boat driver, creature proxies, microphone)
            overlay.text = NetDiagnostics.Summary() + "\n" + NetWorldSync.Summary();
        }

        static bool ColorClose(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.06f;

        // ------------------------------------------------------------------ test hooks (AutoPlaytest / AutoShot)
        /// Opens the lobby on the IP host page (steam = the Steam host page) – main menu only.
        public static bool DebugOpenHostPage(bool steam)
        {
            if (!open) Open();
            if (!open) return false;
            page = steam ? Page.SteamHost : Page.Host;
            Build();
            return true;
        }

        /// Selects a save slot on the host pages (as a click on its tile).
        public static void DebugSelectHostSlot(int slot)
        {
            hostSlot = slot;
            if (open) Build();
        }

        public static int DebugHostSlot => hostSlot;
        public static bool DebugHostContinues => HostSlotUsed;

        /// Slot tiles on the open host page.
        public static int DebugHostSlotTiles => lobby != null ? lobby.Query<VisualElement>("sb-host-slot").ToList().Count : 0;
    }
}
