using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Sealebrities
{
    /// Owns the UIDocument and all screens: routes game events, handles pause/phone/journal keys and the cursor.
    public class UIController : MonoBehaviour
    {
        UIDocument doc;
        VisualElement root;
        bool initialized;
        bool languageDirty;

        public UIFader FaderScreen { get; private set; }
        public UILoadingScreen Loading { get; private set; }
        public UIMainMenu Menu { get; private set; }
        public UIPauseMenu Pause { get; private set; }
        public UIHud Hud { get; private set; }
        public UINotifications Notes { get; private set; }
        public UIViewfinder Viewfinder { get; private set; }
        public UIPhone Phone { get; private set; }
        public UIFinale Finale { get; private set; }

        public VisualElement Root => root;

        /// Seconds until the finale page opens after the 1,000,000-follower milestone (< 0 = none pending).
        float finalePending = -1f;

        public void Init(UIDocument document)
        {
            if (initialized) return;
            doc = document;
            root = doc.rootVisualElement;
            root.style.flexGrow = 1;
            root.pickingMode = PickingMode.Ignore;
            ApplyFallbackFont();

            VisualElement Layer(string name)
            {
                var l = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
                UIKit.FillParent(l);
                root.Add(l);
                return l;
            }

            var hudLayer = Layer("sb-layer-hud");
            var viewLayer = Layer("sb-layer-viewfinder");
            var notesLayer = Layer("sb-layer-notes");
            var phoneLayer = Layer("sb-layer-phone");
            var menuLayer = Layer("sb-layer-menu");
            var pauseLayer = Layer("sb-layer-pause");
            var finaleLayer = Layer("sb-layer-finale");
            var loadingLayer = Layer("sb-layer-loading");
            var fadeLayer = Layer("sb-layer-fade");

            Hud = new UIHud(hudLayer);
            Viewfinder = new UIViewfinder(viewLayer);
            Notes = new UINotifications(notesLayer);
            Phone = new UIPhone(this, phoneLayer);
            Menu = new UIMainMenu(this, menuLayer);
            Pause = new UIPauseMenu(this, pauseLayer);
            Finale = new UIFinale(finaleLayer);
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
        void OnFainted() { Phone.Close(); }
        void OnRecordingChanged(bool rec) { Viewfinder.OnRecordingChanged(rec); }
        void OnClipRecorded(FootageClip c) { Hud.OnClipRecorded(); Phone.MarkDirty(); }
        void OnQuest(string text) { Hud.SetQuest(text); }
        void OnSponsor(string siteId) { Phone.MarkDirty(); }

        void OnLanguageChanged()
        {
            // rebuild next frame: the language buttons that raised this event are part of the trees being rebuilt
            languageDirty = true;
        }

        void OnGameStarted()
        {
            finalePending = -1f;
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Phone.Messages.ResetSession();
            Menu.Hide();
            UIState.MainMenuOpen = false;
            Time.timeScale = 1f;
            Hud.ResetForNewSession();
        }

        void OnReturnedToMenu()
        {
            finalePending = -1f;
            Finale.Close();
            Pause.Close();
            Phone.Close();
            Time.timeScale = 1f;
            Hud.SetQuest("");
            ShowMainMenu();
        }

        void ApplyLanguage()
        {
            languageDirty = false;
            Loading.RefreshTexts();
            Hud.RefreshTexts();
            Viewfinder.RefreshTexts();
            Menu.Rebuild();
            Pause.Rebuild();
            Finale.Rebuild();
            Phone.Refresh(true);
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

            Loading.Tick(dt);
            Menu.Tick(dt);
            Pause.Tick(dt);
            Finale.Tick(dt);
            Phone.Tick(dt);
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
            Finale.Open();
        }

        void HandleInput()
        {
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            if (FaderScreen.IsFading) return;

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
                if (UIState.PhoneOpen) { Phone.Close(); return; }
                if (UIState.PauseOpen) { Pause.HandleEscape(); return; }
                if (inGame) Pause.Open();
                return;
            }
            if (!inGame || UIState.PauseOpen) return;
            if (GameInput.PhonePressed) Phone.Toggle();
            else if (GameInput.JournalPressed) Phone.ToggleApp(Phone.Sealog);
        }

        void LateUpdate()
        {
            if (!initialized) return;
            if (UIState.BlocksGameplay)
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
            if (UIState.PauseOpen) Time.timeScale = 1f;
            initialized = false;
        }
    }
}
