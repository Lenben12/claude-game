using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Sealebrities
{
    /// Owns the UIDocument and all screens: routes game events, handles pause/phone/journal/inventory keys,
    /// the first-start intro + character screen and the cursor.
    public class UIController : MonoBehaviour
    {
        UIDocument doc;
        VisualElement root;
        bool initialized;
        bool languageDirty;
        bool introPlayed;

        public UIFader FaderScreen { get; private set; }
        public UILoadingScreen Loading { get; private set; }
        public UIMainMenu Menu { get; private set; }
        public UIPauseMenu Pause { get; private set; }
        public UIHud Hud { get; private set; }
        public UINotifications Notes { get; private set; }
        public UIViewfinder Viewfinder { get; private set; }
        public UIPhone Phone { get; private set; }
        public UIInventory Inventory { get; private set; }
        public UIFinale Finale { get; private set; }
        public UIIntro Intro { get; private set; }
        /// 1c character screen (CharacterScreen.Open routes here).
        public UICharacterScreen Character { get; private set; }
        /// L.10 quest line at the top centre.
        public UIQuestLine QuestLine { get; private set; }
        public UISleepCounter SleepCounter { get; private set; }
        public UISubtitles Subtitles { get; private set; }
        public UIRadioCard Radio { get; private set; }
        public UIReelGauge Reel { get; private set; }

        public VisualElement Root => root;

        /// Seconds until the finale page opens after the 1,000,000-follower milestone (< 0 = none pending).
        float finalePending = -1f;
        float pollTimer, messageTimer;
        int droppedCount = -1;
        float dryHintCooldown;

        VisualElement characterLayer, dialogLayer;

        public void Init(UIDocument document)
        {
            if (initialized) return;
            doc = document;
            root = doc.rootVisualElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            if (!UIFonts.Apply(root)) ApplyFallbackFont();
            UIAudio.Init(gameObject);

            VisualElement Layer(string name)
            {
                var l = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                UIKit.FillParent(l);
                root.Add(l);
                return l;
            }

            // order = draw order (later = on top). The Net module inserts its lobby layer before "sb-layer-loading";
            // the character screen and dialogs move themselves in front of it when they open (BringLayerToFront).
            var hudLayer = Layer("sb-layer-hud");
            var viewLayer = Layer("sb-layer-viewfinder");
            var questLayer = Layer("sb-layer-quest");
            var subLayer = Layer("sb-layer-subtitles");
            var notesLayer = Layer("sb-layer-notes");
            var letterLayer = Layer("sb-layer-letter");
            var phoneLayer = Layer("sb-layer-phone");
            var inventoryLayer = Layer("sb-layer-inventory");
            var menuLayer = Layer("sb-layer-menu");
            var pauseLayer = Layer("sb-layer-pause");
            var finaleLayer = Layer("sb-layer-finale");
            var introLayer = Layer("sb-layer-intro");
            characterLayer = Layer("sb-layer-character");
            dialogLayer = Layer("sb-layer-dialog");
            var loadingLayer = Layer("sb-layer-loading");
            var fadeLayer = Layer("sb-layer-fade");

            Hud = new UIHud(hudLayer);
            Reel = new UIReelGauge(hudLayer);
            Viewfinder = new UIViewfinder(viewLayer);
            QuestLine = new UIQuestLine(questLayer);
            SleepCounter = new UISleepCounter(QuestLine.Below);
            Subtitles = new UISubtitles(subLayer);
            Radio = new UIRadioCard(subLayer);
            Notes = new UINotifications(notesLayer);
            UILetterCard.Init(letterLayer);
            Phone = new UIPhone(this, phoneLayer, doc.panelSettings);
            Inventory = new UIInventory(inventoryLayer);
            Menu = new UIMainMenu(this, menuLayer);
            Pause = new UIPauseMenu(this, pauseLayer);
            Finale = new UIFinale(finaleLayer);
            Intro = new UIIntro(introLayer);
            Character = new UICharacterScreen(this, characterLayer);
            UIDialog.Init(dialogLayer);
            Loading = new UILoadingScreen(loadingLayer);
            FaderScreen = new UIFader(this, fadeLayer);
            Fader.Handler = FaderScreen.FadeThrough;
            CharacterScreen.OpenHandler = ctx => OpenCharacter(ctx, null);
            PropHooks.OpenLetter = OpenGusLetterProp;   // the letter prop in the hut: paper card instead of a text banner
            HookStoryUI();

            Subscribe();
            initialized = true;
        }

        /// PropReadable (Gus' letter in the hut). The card does not block the interactor: the E that closes it would hit
        /// the prop again – ignore the prop for half a second after a close.
        static void OpenGusLetterProp()
        {
            if (UILetterCard.IsOpen || Time.unscaledTime - UILetterCard.ClosedAt < 0.5f) return;
            UILetterCard.ShowGusLetter();
        }

        void ApplyFallbackFont()
        {
            var ps = doc.panelSettings;
            if (ps != null && ps.themeStyleSheet != null) return;
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) root.style.unityFontDefinition = FontDefinition.FromFont(font);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("Sealebrities UI: no theme and no builtin font available. " + e.Message);
            }
        }

        /// Takes over the npc module's story UI (subtitles, radio card, Gus' letter): its StoryUI flags/handler are set by
        /// reflection so this module never depends on the npc module's files at compile time.
        void HookStoryUI()
        {
            try
            {
                var t = typeof(UIController).Assembly.GetType("Sealebrities.StoryUI");
                if (t == null) return;
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static;
                t.GetField("ExternalSubtitles", flags)?.SetValue(null, true);
                t.GetField("ExternalRadioCard", flags)?.SetValue(null, true);
                var f = t.GetField("LetterHandler", flags);
                if (f != null && f.FieldType == typeof(System.Action<string, string, System.Action>))
                    f.SetValue(null, (System.Action<string, string, System.Action>)UILetterCard.Show);
            }
            catch (System.Exception e) { Debug.LogWarning("Sealebrities UI: story UI hook failed: " + e.Message); }
        }

        /// Moves a layer right in front of the loading screen (above the Net module's lobby layer, menu and pause).
        public void BringLayerToFront(VisualElement layer)
        {
            if (layer == null || root == null) return;
            layer.RemoveFromHierarchy();
            int index = -1;
            for (int i = 0; i < root.childCount; i++)
            {
                string n = root[i].name;
                if (n == "sb-layer-loading" || n == "sb-layer-fade") { index = i; break; }
            }
            if (index < 0) root.Add(layer);
            else root.Insert(index, layer);
            // dialogs always stay above the character screen
            if (layer != dialogLayer && dialogLayer != null) BringDialogAbove(layer);
        }

        void BringDialogAbove(VisualElement below)
        {
            if (root.IndexOf(below) < 0) return;
            dialogLayer.RemoveFromHierarchy();
            int idx = root.IndexOf(below);
            root.Insert(Mathf.Min(idx + 1, root.childCount), dialogLayer);
        }

        // ------------------------------------------------------------------ character screen (1c)
        /// Opens the character screen for a context; the pause menu steps aside while it is open.
        public void OpenCharacter(CharacterScreenContext ctx, System.Action onClosed)
        {
            if (Character == null) return;
            if (Character.IsOpen) return;
            bool fromPause = UIState.PauseOpen;
            if (ctx == CharacterScreenContext.Locker || ctx == CharacterScreenContext.Pause)
            {
                Phone.Close();
                Inventory.Close();
            }
            if (fromPause) Pause.SetHidden(true);
            Character.Open(ctx, () =>
            {
                if (fromPause) Pause.SetHidden(false);
                Menu.Refresh();
                onClosed?.Invoke();
            });
        }

        // ------------------------------------------------------------------ events
        void Subscribe()
        {
            GameEvents.MoneyChanged += OnMoney;
            GameEvents.FollowersChanged += OnFollowers;
            GameEvents.SpeciesDiscovered += OnSpeciesDiscovered;
            GameEvents.SizeRecord += OnSizeRecord;
            GameEvents.MilestoneReached += OnMilestone;
            GameEvents.SiteUnlocked += OnSiteUnlocked;
            GameEvents.Notify += OnNotify;   // L.10: Notify only – never Toast too (Toast mirrors every notification)
            GameEvents.Banner += OnBanner;
            GameEvents.DayStarted += OnDayStarted;
            GameEvents.GearChanged += OnStateChanged;
            GameEvents.LanguageChanged += OnLanguageChanged;
            GameEvents.DeliveryOrdered += OnGearItem;
            GameEvents.DeliveryReceived += OnGearItem;
            GameEvents.UploadCreated += OnUploadCreated;
            GameEvents.UploadResolved += OnUploadResolved;
            GameEvents.PlayerBumped += OnBumped;
            GameEvents.PlayerFainted += OnFainted;
            GameEvents.RecordingChanged += OnRecordingChanged;
            GameEvents.ClipRecorded += OnClipRecorded;
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            GameEvents.QuestChanged += OnQuest;
            GameEvents.SponsorUnlocked += OnSponsor;
            GameEvents.FishCaught += OnFishCaught;
            GameEvents.RankChanged += OnRankChanged;
            GameEvents.AchievementUnlocked += OnAchievement;
            GameEvents.DealChanged += OnIdChanged;
            GameEvents.SideQuestChanged += OnIdChanged;
            GameEvents.MissionChanged += OnIdChanged;
            GameEvents.ResearchChanged += OnIdChanged;
            GameEvents.ChallengeCompleted += OnIdChanged;
            GameEvents.FindTaken += OnIdChanged;
            GameEvents.StoryFlagSet += OnIdChanged;
            GameEvents.SealogPhoto += OnSealogPhoto;
            GameEvents.Sighted += OnSighted;
            GameEvents.DeliveryMishapHappened += OnMishap;
            Pantry.Changed += OnStateChanged;
            Deals.Changed += OnStateChanged;
            FishMarket.Changed += OnMarketChanged;
            Gallery.Added += OnPhotoAdded;
            Gallery.Removed += OnPhotoAdded;
            Net.PlayerJoined += OnPlayerJoined;
            Net.PlayerLeft += OnPlayerLeft;
            Net.SessionEnded += OnSessionEnded;
            Net.StateSynced += OnStateSynced;
        }

        void Unsubscribe()
        {
            GameEvents.MoneyChanged -= OnMoney;
            GameEvents.FollowersChanged -= OnFollowers;
            GameEvents.SpeciesDiscovered -= OnSpeciesDiscovered;
            GameEvents.SizeRecord -= OnSizeRecord;
            GameEvents.MilestoneReached -= OnMilestone;
            GameEvents.SiteUnlocked -= OnSiteUnlocked;
            GameEvents.Notify -= OnNotify;
            GameEvents.Banner -= OnBanner;
            GameEvents.DayStarted -= OnDayStarted;
            GameEvents.GearChanged -= OnStateChanged;
            GameEvents.LanguageChanged -= OnLanguageChanged;
            GameEvents.DeliveryOrdered -= OnGearItem;
            GameEvents.DeliveryReceived -= OnGearItem;
            GameEvents.UploadCreated -= OnUploadCreated;
            GameEvents.UploadResolved -= OnUploadResolved;
            GameEvents.PlayerBumped -= OnBumped;
            GameEvents.PlayerFainted -= OnFainted;
            GameEvents.RecordingChanged -= OnRecordingChanged;
            GameEvents.ClipRecorded -= OnClipRecorded;
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            GameEvents.QuestChanged -= OnQuest;
            GameEvents.SponsorUnlocked -= OnSponsor;
            GameEvents.FishCaught -= OnFishCaught;
            GameEvents.RankChanged -= OnRankChanged;
            GameEvents.AchievementUnlocked -= OnAchievement;
            GameEvents.DealChanged -= OnIdChanged;
            GameEvents.SideQuestChanged -= OnIdChanged;
            GameEvents.MissionChanged -= OnIdChanged;
            GameEvents.ResearchChanged -= OnIdChanged;
            GameEvents.ChallengeCompleted -= OnIdChanged;
            GameEvents.FindTaken -= OnIdChanged;
            GameEvents.StoryFlagSet -= OnIdChanged;
            GameEvents.SealogPhoto -= OnSealogPhoto;
            GameEvents.Sighted -= OnSighted;
            GameEvents.DeliveryMishapHappened -= OnMishap;
            Pantry.Changed -= OnStateChanged;
            Deals.Changed -= OnStateChanged;
            FishMarket.Changed -= OnMarketChanged;
            Gallery.Added -= OnPhotoAdded;
            Gallery.Removed -= OnPhotoAdded;
            Net.PlayerJoined -= OnPlayerJoined;
            Net.PlayerLeft -= OnPlayerLeft;
            Net.SessionEnded -= OnSessionEnded;
            Net.StateSynced -= OnStateSynced;
        }

        void OnMoney(long v) { Hud.OnMoney(v); Phone.MarkDirty(); }
        void OnFollowers(long v) { Hud.OnFollowers(v); Phone.MarkDirty(); }
        void OnSpeciesDiscovered(SpeciesDef s, float cm, SizeTier t) { Notes.SpeciesDiscovered(s, cm, t); Phone.MarkDirty(); }
        void OnSizeRecord(SpeciesDef s, float cm, SizeTier t) { Notes.SizeRecord(s, cm, t); Phone.MarkDirty(); }
        void OnMilestone(Milestone m)
        {
            Notes.Milestone(m);
            Phone.MarkDirty();
            // the finale milestone fires inside the wake-up fade (StartNewDay): open the end page a bit later
            if (m != null && m.followers >= Progression.FinaleFollowers && GameManager.I != null && GameManager.I.InGame)
                finalePending = 1.8f;
        }
        void OnSiteUnlocked(DiveSite s) { Notes.SiteUnlocked(s); Phone.MarkDirty(); }
        void OnNotify(NotifyKind kind, string line, string chip) { Notes.Notify(kind, line, chip); }
        void OnBanner(string text, Color c) { Notes.Banner(text, c); }
        void OnDayStarted(int day) { Phone.MarkDirty(); }
        void OnStateChanged() { Phone.MarkDirty(); }
        void OnIdChanged(string id) { Phone.MarkDirty(); }
        void OnGearItem(GearItem item) { Phone.MarkDirty(); }
        void OnUploadCreated(PendingUpload u) { Phone.MarkDirty(); }
        void OnUploadResolved(VideoRecord v) { Notes.UploadResolved(v); Phone.MarkDirty(); }
        void OnBumped(float airLost) { Hud.OnBumped(airLost); }
        void OnFainted() { Phone.Close(); Inventory.Close(); UILetterCard.Close(); }
        void OnRecordingChanged(bool rec) { Viewfinder.OnRecordingChanged(rec); }
        void OnClipRecorded(FootageClip c) { Hud.OnClipRecorded(); Phone.MarkDirty(); }
        void OnSponsor(string siteId) { Phone.MarkDirty(); }
        void OnFishCaught(int value) { Phone.MarkDirty(); }
        void OnRankChanged(int rank)
        {
            // every rank threshold is also a milestone: its ribbon already names the rank (UINotifications.Milestone) –
            // no second "Neuer Rang" ribbon right behind it
            if (rank > 0 && UIKit.InGame && !RankIsMilestone(rank)) Notes.RankUp(rank);
            Hud.OnRankChanged();
            Phone.MarkDirty();
        }

        static bool RankIsMilestone(int rank)
        {
            var ranks = Progression.Ranks;
            if (rank < 0 || rank >= ranks.Length) return false;
            long f = ranks[rank].followers;
            var ms = Progression.Milestones;
            for (int i = 0; i < ms.Count; i++) if (ms[i] != null && ms[i].followers == f) return true;
            return false;
        }
        void OnAchievement(AchievementDef a) { Notes.Achievement(a); }
        void OnSealogPhoto(PhotoEntry p, SpeciesDef s) { Phone.MarkDirty(); }
        void OnSighted(SpeciesDef s, int n) { if (UIState.PhoneOpen) Phone.MarkDirty(); }
        void OnMishap(string itemId, DeliveryMishap m) { Phone.MarkDirty(); }
        void OnMarketChanged() { Phone.Market.OnMarketChanged(); }
        void OnPhotoAdded(PhotoEntry e) { Phone.Gallery.OnPhotoAdded(); }
        /// Co-op client: a host snapshot (incl. the 10 s keyframe) only rebuilds the open phone page when the shared state
        /// actually changed (no hitch / lost hover state every 10 s).
        int lastSyncedHash;
        bool syncedHashValid;
        void OnStateSynced()
        {
            var gm = GameManager.I;
            if (gm == null || gm.State == null) { Phone.MarkDirty(); return; }
            int h = NetState.StateHash(gm.State);
            if (syncedHashValid && h == lastSyncedHash) return;
            lastSyncedHash = h;
            syncedHashValid = true;
            Phone.MarkDirty();
        }

        /// Legacy task text (tutorial: "Titel\nDetail (2/5)"): the Tasks app and the quest line (until the provider
        /// feeds QuestTracker). Progress pops the quest line; there are no task cards any more (L.10).
        void OnQuest(string text)
        {
            Phone.Tasks.SetQuest(text, true);
            QuestLine.SetLegacy(text);
        }

        // Join/leave: ONE channel only – the Net module's notifications, which are already gated to real joins/leaves.
        // These handlers only refresh the phone.
        void OnPlayerJoined(NetPlayerInfo p)
        {
            if (p == null || p.IsLocal) return;
            Phone.MarkDirty();
        }

        void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null || p.IsLocal) return;
            Phone.MarkDirty();
        }

        void OnSessionEnded(string reason)
        {
            // with a reason the lobby shows it (NetLobby.ShowNotice); without one the player left on purpose
            Phone.MarkDirty();
        }

        void OnLanguageChanged()
        {
            // rebuild next frame: the language buttons that raised this event are part of the trees being rebuilt
            languageDirty = true;
        }

        void OnGameStarted()
        {
            finalePending = -1f;
            droppedCount = -1;
            if (Character.IsOpen) Character.Close(false);
            UIDialog.Close();
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Inventory.Close();
            UILetterCard.Close();
            Phone.Messages.ResetSession();
            Phone.Tasks.ResetSession();
            Phone.Market.ResetSession();
            QuestLine.ResetSession();
            Subtitles.Clear();
            Radio.Clear();
            Notes.Clear();
            Menu.Hide();
            UIState.MainMenuOpen = false;
            Time.timeScale = 1f;
            Hud.ResetForNewSession();
            UIWaypoint.Clear();
        }

        void OnReturnedToMenu()
        {
            finalePending = -1f;
            if (Character.IsOpen) Character.Close(false);
            UIDialog.Close();
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Inventory.Close();
            UILetterCard.Close();
            Subtitles.Clear();
            Radio.Clear();
            Time.timeScale = 1f;
            UIWaypoint.Clear();
            ShowMainMenu();
        }

        void ApplyLanguage()
        {
            languageDirty = false;
            Loading.RefreshTexts();
            Hud.RefreshTexts();
            Viewfinder.RefreshTexts();
            Inventory.RefreshTexts();
            QuestLine.RefreshTexts();
            Menu.Rebuild();
            Pause.Rebuild();
            Finale.Rebuild();
            Phone.RefreshTexts();
        }

        // ------------------------------------------------------------------ API used by UIModule
        public void ShowLoading(float progress, string text)
        {
            UIState.LoadingOpen = true;
            Loading.Show(progress, text);
        }

        public void HideLoading()
        {
            UIState.LoadingOpen = false;
            Loading.Hide();
        }

        public void ShowMainMenu()
        {
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Inventory.Close();
            var gm = GameManager.I;
            // Q42: the launch intro plays only on the very first start, followed by the character screen (1c); later
            // starts go straight to the menu (the intro stays reachable via Einstellungen → „Intro ansehen“)
            if (!introPlayed && (gm == null || !gm.InGame))
            {
                introPlayed = true;
                if (!Settings.Data.introSeen)
                {
                    UIState.MainMenuOpen = true;
                    Menu.Hide();
                    Intro.Run(OnFirstIntroDone);
                    return;
                }
            }
            if (Intro.Active) return;   // the menu opens when the intro ends
            Menu.Show();
        }

        void OnFirstIntroDone()
        {
            var g = GameManager.I;
            if (!GameManager.AutomatedRun)
            {
                Settings.Data.introSeen = true;
                Settings.Save();
            }
            if (g != null && g.InGame) return;
            Menu.Show();
            // automated runs never stop at the first-start character screen
            if (GameManager.AutomatedRun) return;
            OpenCharacter(CharacterScreenContext.FirstStart, () =>
            {
                var gm = GameManager.I;
                if (gm == null || !gm.InGame) Menu.ShowNewGame();
            });
        }

        /// Settings → „Intro ansehen“ (main menu only).
        public void WatchIntro()
        {
            var gm = GameManager.I;
            if (gm != null && gm.InGame) return;
            if (Intro.Active) return;
            Menu.Hide();
            UIState.MainMenuOpen = true;
            Intro.Run(() =>
            {
                var g = GameManager.I;
                if (g == null || !g.InGame) Menu.Show();
            });
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (!initialized) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (languageDirty) ApplyLanguage();
            TickFinalePending(dt);
            HandleInput(dt);
            PollState(dt);
            TickDryPhone(dt);

            bool aiming = Viewfinder.IsAiming;
            Loading.Tick(dt);
            Intro.Tick(dt);
            Menu.Tick(dt);
            Pause.Tick(dt);
            Finale.Tick(dt);
            Phone.Tick(dt);
            Inventory.Tick(dt);
            Viewfinder.Tick(dt);
            Hud.Tick(dt, aiming);
            QuestLine.Tick(dt, aiming);
            SleepCounter.Tick(Notes);
            Subtitles.Tick(dt);
            Radio.Tick(dt, Notes);
            Reel.Tick(dt);
            Notes.Tick(dt);
            Character.Tick(dt);
            UILetterCard.Tick(dt);
            UIDialog.Tick(dt);
        }

        void TickFinalePending(float dt)
        {
            if (finalePending < 0f) return;
            finalePending -= dt;
            if (finalePending > 0f) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.LoadingOpen) { finalePending = -1f; return; }
            // wait for fades, the pause menu and the character screen (the player may have opened them meanwhile)
            if (FaderScreen.IsFading || UIState.PauseOpen || UIState.CharacterOpen) { finalePending = 0.25f; return; }
            finalePending = -1f;
            Phone.Close();
            Inventory.Close();
            Finale.Open();
        }

        /// Light polling for things without an event: crates landing on the helipad, new chat messages.
        void PollState(float dt)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) { droppedCount = -1; return; }
            pollTimer -= dt;
            if (pollTimer > 0f) return;
            pollTimer = 0.5f;

            var s = gm.State;
            int dropped = 0;
            Delivery last = null;
            for (int i = 0; i < s.deliveries.Count; i++)
                if (s.deliveries[i] != null && s.deliveries[i].dropped) { dropped++; last = s.deliveries[i]; }
            if (droppedCount >= 0 && dropped > droppedCount && last != null)
            {
                bool lost = last.Mishap == DeliveryMishap.WrongPlace || last.Mishap == DeliveryMishap.Village || last.Mishap == DeliveryMishap.Water
                            || last.Mishap == DeliveryMishap.HutRoof || last.Mishap == DeliveryMishap.Palm || last.Mishap == DeliveryMishap.Goat
                            || last.Mishap == DeliveryMishap.Boat;
                Notes.Push(lost ? UIGlyphs.Smoke : UIGlyphs.Heli, lost ? UIPalette.Warn : UIPalette.Coral,
                    lost ? Loc.Tr("Paket suchen!", "Find the parcel!") : Loc.Tr("Lieferung da!", "Delivery here!"), Shop.DeliveryName(last), "drop:" + last.itemId);
                Phone.MarkDirty();
            }
            droppedCount = dropped;

            messageTimer -= 0.5f;
            if (messageTimer <= 0f)
            {
                messageTimer = 3f;
                int unread = Phone.Messages.UnreadCount();
                if (unread > Phone.Messages.LastNotifiedBadge && Phone.Messages.TryNewestUnread(out string name, out string line))
                    Notes.Push(UIGlyphs.Envelope, UIPalette.Rgb(0x4FC3F7), name, Loc.Tr("Neue Nachricht", "New message"), "msg:" + name + ":" + unread);
                Phone.Messages.LastNotifiedBadge = unread;
            }
        }

        static bool PlayerSwimming()
        {
            var p = Refs.Player;
            if (p is Object po && po == null) return false;
            if (p == null) return false;
            var b = Refs.Boat;
            if (b is Object bo && bo == null) b = null;
            if (b != null && b.PlayerAboard) return false;
            return p.IsInWater || p.IsUnderwater;
        }

        /// Q35: the phone stays dry – it cannot be opened (and closes) while swimming or diving.
        void TickDryPhone(float dt)
        {
            if (dryHintCooldown > 0f) dryHintCooldown -= dt;
            if (UIState.PhoneOpen && PlayerSwimming())
            {
                Phone.Close();
                DryHint();
            }
        }

        void DryHint()
        {
            UIAudio.Nope();
            if (dryHintCooldown > 0f) return;
            dryHintCooldown = 4f;
            Notes.Notify(NotifyKind.Warning, Loc.Tr("Handy bleibt trocken", "Phone stays dry"), Loc.Tr("an Land / Boot", "land / boat"));
        }

        void HandleInput(float dt)
        {
            if (UISettingsView.CaptureActive) return;   // the settings screen is waiting for a key
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            if (FaderScreen.IsFading) return;

            if (UITextInput.Active)
            {
                if (GameInput.KeyDown(UnityEngine.InputSystem.Key.Escape)) UITextInput.Blur();
                else if (GameInput.KeyDown(UnityEngine.InputSystem.Key.Enter) || GameInput.KeyDown(UnityEngine.InputSystem.Key.NumpadEnter)) UITextInput.Submit();
                return;
            }

            if (UIDialog.HandleInput()) return;

            if (Intro.Active)
            {
                if (GameInput.PausePressed) Intro.Skip();
                return;
            }

            if (Character.IsOpen)
            {
                Character.HandleInput(dt);
                return;
            }

            if (UILetterCard.HandleInput()) return;

            // finale page: Esc = keep playing, nothing else reaches pause/phone while it is open
            if (Finale.IsOpen)
            {
                if (GameInput.PausePressed) Finale.HandleEscape();
                return;
            }

            if (GameInput.PausePressed)
            {
                if (UIState.LoadingOpen) return;
                if (UIState.MainMenuOpen) { Menu.Back(); return; }
                if (UIState.InventoryOpen) { Inventory.Close(); return; }
                if (UIState.PhoneOpen) { Phone.Close(); return; }
                if (UIState.PauseOpen) { Pause.HandleEscape(); return; }
                if (inGame) Pause.Open();
                return;
            }
            if (!inGame || UIState.PauseOpen) return;
            // Alt+Tab / Alt+I / Alt+J while switching windows (2-window co-op test) must not toggle phone or inventory
            if ((!Application.isFocused && !GameManager.AutomatedRun)
                || GameInput.KeyHeld(UnityEngine.InputSystem.Key.LeftAlt) || GameInput.KeyHeld(UnityEngine.InputSystem.Key.RightAlt)) return;
            if (GameInput.InventoryPressed)
            {
                if (UIState.PhoneOpen) Phone.Close();
                Inventory.Toggle();
            }
            else if (GameInput.PhonePressed)
            {
                if (UIState.InventoryOpen) Inventory.Close();
                if (!UIState.PhoneOpen && PlayerSwimming()) { DryHint(); return; }
                Phone.Toggle();
            }
            else if (GameInput.JournalPressed)
            {
                if (UIState.InventoryOpen) Inventory.Close();
                if (!UIState.PhoneOpen && PlayerSwimming()) { DryHint(); return; }
                Phone.ToggleApp(Phone.Sealog);
            }
        }

        /// Offline: alt-tabbing out of a running game opens the pause menu (the clock and the air stop). Online the world
        /// runs on anyway; automated test runs rely on unfocused windows running normally.
        void OnApplicationFocus(bool focus)
        {
            if (focus || !initialized || GameManager.AutomatedRun || Net.IsOnline) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.LoadingOpen || UIState.BlocksGameplay) return;
            if (Intro.Active || Finale.IsOpen || FaderScreen.IsFading) return;
            Pause.Open();
        }

        void LateUpdate()
        {
            if (!initialized) return;
            bool free = (UIState.BlocksGameplay || UIDialog.IsOpen) && !Intro.Active;
            if (free)
            {
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
            }
            else
            {
                if (Cursor.lockState != CursorLockMode.Locked) Cursor.lockState = CursorLockMode.Locked;
                if (Cursor.visible) Cursor.visible = false;
            }
        }

        void OnDestroy()
        {
            if (!initialized) return;
            Unsubscribe();
            if (FaderScreen != null)
            {
                if (Fader.Handler == (System.Action<System.Action, float>)FaderScreen.FadeThrough) Fader.Handler = null;
                FaderScreen.Cancel(false);
            }
            if (CharacterScreen.OpenHandler != null) CharacterScreen.OpenHandler = null;
            if (PropHooks.OpenLetter == (System.Action)OpenGusLetterProp) PropHooks.OpenLetter = null;
            if (Character != null && Character.IsOpen) Character.Close(false);
            Hud?.Hotbar.Dispose();
            Hud?.Food.Dispose();
            QuestLine?.Dispose();
            Subtitles?.Dispose();
            Radio?.Dispose();
            Reel?.Dispose();
            Phone?.OnDestroy();
            if (UIState.PauseOpen) Time.timeScale = 1f;
            initialized = false;
        }
    }
}
