using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Sealebrities
{
    /// Owns the UIDocument and all screens: routes game events, handles pause/phone/journal/inventory keys,
    /// the launch intro and the cursor.
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

        public VisualElement Root => root;

        /// Seconds until the finale page opens after the 1,000,000-follower milestone (< 0 = none pending).
        float finalePending = -1f;
        float pollTimer, messageTimer;
        int droppedCount = -1;

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

            // order = draw order (later = on top). The Net module inserts its lobby layer before "sb-layer-loading".
            var hudLayer = Layer("sb-layer-hud");
            var viewLayer = Layer("sb-layer-viewfinder");
            var notesLayer = Layer("sb-layer-notes");
            var phoneLayer = Layer("sb-layer-phone");
            var inventoryLayer = Layer("sb-layer-inventory");
            var menuLayer = Layer("sb-layer-menu");
            var pauseLayer = Layer("sb-layer-pause");
            var finaleLayer = Layer("sb-layer-finale");
            var introLayer = Layer("sb-layer-intro");
            var loadingLayer = Layer("sb-layer-loading");
            var fadeLayer = Layer("sb-layer-fade");

            Hud = new UIHud(hudLayer);
            Viewfinder = new UIViewfinder(viewLayer);
            Notes = new UINotifications(notesLayer);
            Phone = new UIPhone(this, phoneLayer, doc.panelSettings);
            Inventory = new UIInventory(inventoryLayer);
            Menu = new UIMainMenu(this, menuLayer);
            Pause = new UIPauseMenu(this, pauseLayer);
            Finale = new UIFinale(finaleLayer);
            Intro = new UIIntro(introLayer);
            Loading = new UILoadingScreen(loadingLayer);
            FaderScreen = new UIFader(this, fadeLayer);
            Fader.Handler = FaderScreen.FadeThrough;

            Subscribe();
            initialized = true;
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

        // ------------------------------------------------------------------ events
        void Subscribe()
        {
            GameEvents.MoneyChanged += OnMoney;
            GameEvents.FollowersChanged += OnFollowers;
            GameEvents.SpeciesDiscovered += OnSpeciesDiscovered;
            GameEvents.SizeRecord += OnSizeRecord;
            GameEvents.MilestoneReached += OnMilestone;
            GameEvents.SiteUnlocked += OnSiteUnlocked;
            GameEvents.Toast += OnToast;
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
            GameEvents.Toast -= OnToast;
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
        void OnToast(string text) { Notes.Toast(text); }
        void OnBanner(string text, Color c) { Notes.Banner(text, c); }
        void OnDayStarted(int day) { Phone.MarkDirty(); }
        void OnStateChanged() { Phone.MarkDirty(); }
        void OnGearItem(GearItem item) { Phone.MarkDirty(); }
        void OnUploadCreated(PendingUpload u) { Phone.MarkDirty(); }
        void OnUploadResolved(VideoRecord v) { Notes.UploadResolved(v); Phone.MarkDirty(); }
        void OnBumped(float airLost) { Hud.OnBumped(airLost); }
        void OnFainted() { Phone.Close(); Inventory.Close(); }
        void OnRecordingChanged(bool rec) { Viewfinder.OnRecordingChanged(rec); }
        void OnClipRecorded(FootageClip c) { Hud.OnClipRecorded(); Phone.MarkDirty(); }
        void OnSponsor(string siteId) { Phone.MarkDirty(); }
        void OnFishCaught(int value) { Phone.MarkDirty(); }
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

        /// Key of the last announced task: its first line without a trailing " (n/m)" counter. Progress updates of the
        /// same task refresh the "Aufgaben" app silently; only a new task pushes a card with a ping and arms the badge.
        string lastQuestKey = "";

        static string QuestKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int nl = text.IndexOf('\n');
            string first = nl >= 0 ? text.Substring(0, nl) : text;
            if (first.EndsWith(")", System.StringComparison.Ordinal))
            {
                int open = first.LastIndexOf(" (", System.StringComparison.Ordinal);
                if (open > 0 && first.IndexOf('/', open) > open) first = first.Substring(0, open);
            }
            return first;
        }

        void OnQuest(string text)
        {
            string key = QuestKey(text);
            bool isNew = key.Length > 0 && key != lastQuestKey;
            lastQuestKey = key;
            Phone.Tasks.SetQuest(text, isNew);
            Notes.UpdateQuestText(text);
            if (isNew && GameManager.I != null && GameManager.I.InGame && !UIState.MainMenuOpen) Notes.Quest(text);
        }

        // Join/leave: ONE channel only – the Net module's toasts ("… ist an Bord!", "… ist von Bord gegangen",
        // "… hat die Verbindung verloren"), which are already gated to real joins/leaves (no roster replay on a guest's
        // welcome, nothing per player when a session ends). These handlers only refresh the phone.
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
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Inventory.Close();
            Phone.Messages.ResetSession();
            Phone.Tasks.ResetSession();
            Phone.Market.ResetSession();
            Menu.Hide();
            UIState.MainMenuOpen = false;
            Time.timeScale = 1f;
            Hud.ResetForNewSession();
            UIWaypoint.Clear();
        }

        void OnReturnedToMenu()
        {
            finalePending = -1f;
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Inventory.Close();
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
            // first time after boot: the launch intro, then the menu
            if (!introPlayed && (gm == null || !gm.InGame))
            {
                introPlayed = true;
                UIState.MainMenuOpen = true;
                Menu.Hide();
                Intro.Run(() =>
                {
                    var g = GameManager.I;
                    if (g == null || !g.InGame) Menu.Show();
                });
                return;
            }
            if (Intro.Active) return;   // the menu opens when the intro ends
            Menu.Show();
        }

        // ------------------------------------------------------------------ frame
        void Update()
        {
            if (!initialized) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (languageDirty) ApplyLanguage();
            TickFinalePending(dt);
            HandleInput();
            PollState(dt);

            Loading.Tick(dt);
            Intro.Tick(dt);
            Menu.Tick(dt);
            Pause.Tick(dt);
            Finale.Tick(dt);
            Phone.Tick(dt);
            Inventory.Tick(dt);
            Viewfinder.Tick(dt);
            Hud.Tick(dt, Viewfinder.IsAiming);
            Notes.Tick(dt);
        }

        void TickFinalePending(float dt)
        {
            if (finalePending < 0f) return;
            finalePending -= dt;
            if (finalePending > 0f) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.LoadingOpen) { finalePending = -1f; return; }
            // wait for fades and the pause menu (the player may have paused in the meantime)
            if (FaderScreen.IsFading || UIState.PauseOpen) { finalePending = 0.25f; return; }
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
                var item = GearDatabase.Get(last.itemId);
                Notes.Push(UIGlyphs.Heli, UIPalette.Coral, Loc.Tr("Lieferung am Helipad!", "Delivery at the helipad!"), item != null ? item.Name : null, "drop:" + last.itemId);
                Phone.MarkDirty();
            }
            droppedCount = dropped;

            messageTimer -= 0.5f;
            if (messageTimer <= 0f)
            {
                messageTimer = 3f;
                int unread = Phone.Messages.UnreadCount();
                if (unread > Phone.Messages.LastNotifiedBadge && Phone.Messages.TryNewestUnread(out string name, out string line))
                    Notes.Push(UIGlyphs.Bubble, UIPalette.Rgb(0x4FC3F7), name, line, "msg:" + name + ":" + unread);
                Phone.Messages.LastNotifiedBadge = unread;
            }
        }

        void HandleInput()
        {
            if (UISettingsView.CaptureActive) return;   // the settings screen is waiting for a key
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            if (FaderScreen.IsFading) return;

            if (UITextInput.Active)
            {
                if (GameInput.KeyDown(UnityEngine.InputSystem.Key.Escape) || GameInput.KeyDown(UnityEngine.InputSystem.Key.Enter)) UITextInput.Blur();
                return;
            }

            if (Intro.Active)
            {
                if (GameInput.PausePressed) Intro.Skip();
                return;
            }

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
                Phone.Toggle();
            }
            else if (GameInput.JournalPressed)
            {
                if (UIState.InventoryOpen) Inventory.Close();
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
            bool free = UIState.BlocksGameplay && !Intro.Active;
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
            Hud?.Hotbar.Dispose();
            Phone?.OnDestroy();
            if (UIState.PauseOpen) Time.timeScale = 1f;
            initialized = false;
        }
    }
}
