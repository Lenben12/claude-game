using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Base class of the ClamOS phone apps. Apps rebuild their page from game state whenever the phone marks them dirty.
    /// Layout units are phone-screen pixels (540 × 1080, see PhoneKit for the type scale).
    public abstract class UIPhoneApp
    {
        protected UIPhone Phone;

        internal void Attach(UIPhone phone) { Phone = phone; }

        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract Color Color { get; }
        public abstract UIGlyph.DrawFn Icon { get; }
        /// Number on the red badge (0 = none, -1 = "!" dot).
        public virtual int Badge => 0;
        public virtual bool ShowLock => false;
        /// Header title (detail pages may override).
        public virtual string HeaderTitle => Title;
        /// Called when the app is opened from the home screen or directly (J key).
        public virtual void OnOpen() { }
        /// Called when the app page is left (home, other app, phone closed).
        public virtual void OnClose() { }
        /// Internal back navigation; return true when handled (otherwise the phone goes home).
        public virtual bool Back() => false;
        /// False: the page is not rebuilt when unrelated game state changes (settings – keeps text focus).
        public virtual bool LiveUpdates => true;
        public abstract void Build(VisualElement body);
        public virtual void Tick(float dt) { }
    }

    /// Type scale and builders for phone pages (light theme, big readable text – the 3D phone is ~55 % of the
    /// screen height, so 1 phone pixel ≈ 0.55 screen pixels).
    public static class PhoneKit
    {
        public const float Tiny = 18f, Small = 21f, Body = 25f, Big = 30f, Title = 36f, Huge = 56f;
        public static readonly Color Bg = UIPalette.Rgb(0xF4F1EA);

        public static VisualElement Card(float pad = 18f)
        {
            var c = UIKit.Card(Color.white, 22f, pad);
            c.style.marginBottom = 12f;
            UIKit.Lift(c, 0.8f);
            return c;
        }

        public static Label T(string text, float size = Body, Color? color = null, bool bold = false)
        {
            var l = UIKit.Text(text, size, color ?? UIPalette.Ink, bold);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static VisualElement Section(string text, UIGlyph.DrawFn icon = null)
        {
            var row = UIKit.Row();
            row.style.marginTop = 16f;
            row.style.marginBottom = 10f;
            if (icon != null)
            {
                var g = new UIGlyph(icon, UIPalette.InkSoft, 26f);
                g.style.marginRight = 8f;
                row.Add(g);
            }
            row.Add(UIKit.Text(text, Small, UIPalette.InkSoft, true));
            return row;
        }

        public static Button Button(string text, Action onClick, UIBtnKind kind = UIBtnKind.Primary, float size = Body)
        {
            var b = UIKit.Btn(text, onClick, kind, size);
            UIKit.Pad(b, 16f, 20f, 16f, 20f);
            UIKit.Radius(b, 18f);
            return b;
        }

        /// Icon button with a short label (e.g. "Alle verkaufen").
        public static Button IconButton(UIGlyph.DrawFn icon, string text, Action onClick, UIBtnKind kind = UIBtnKind.Primary, float size = Body)
        {
            var b = Button("", onClick, kind, size);
            b.style.flexDirection = FlexDirection.Row;
            b.style.justifyContent = Justify.Center;
            b.style.alignItems = Align.Center;
            var skin = b.userData as UIBtnSkin;
            var fg = skin != null ? skin.fg : Color.white;
            var g = new UIGlyph(icon, fg, size * 1.2f);
            b.Add(g);
            if (!string.IsNullOrEmpty(text))
            {
                var l = UIKit.Text(text, size, fg, true);
                l.style.marginLeft = 10f;
                l.pickingMode = PickingMode.Ignore;
                b.Add(l);
            }
            return b;
        }

        public static VisualElement Empty(UIGlyph.DrawFn icon, string text)
        {
            var c = Card(26f);
            c.style.alignItems = Align.Center;
            c.Add(new UIGlyph(icon, UIPalette.Silhouette, 72f));
            var t = T(text, Body, UIPalette.InkSoft);
            t.style.unityTextAlign = TextAnchor.MiddleCenter;
            t.style.marginTop = 10f;
            c.Add(t);
            return c;
        }

        /// White rounded stat with an icon (two or three side by side).
        public static VisualElement Stat(UIGlyph.DrawFn icon, Color iconColor, string value, Color? valueColor = null)
        {
            var s = UIWidgets.IconStat(icon, iconColor, value, valueColor ?? UIPalette.Ink, Big);
            UIKit.Lift(s, 0.8f);
            return s;
        }

        public static VisualElement Bar(float v01, Color color, float height = 14f)
        {
            var b = new UIBar(height, color);
            b.SetValue(v01);
            return b;
        }
    }

    /// The ClamOS smartphone. The screen (540 × 1080) is rendered into PhoneDisplay.Texture (own PanelSettings with a
    /// target texture) so the Player module can show it on the old scratched 3D phone in the hand; mouse input is
    /// mapped through PhoneDisplay.ScreenToUV. When the 3D phone is not available (or the player chose "flat"), the
    /// same screen element is shown as a 2D overlay instead.
    public class UIPhone
    {
        public const int W = PhoneDisplay.Width, H = PhoneDisplay.Height;

        readonly UIController ui;
        readonly VisualElement overlay, backdrop, frame, frameScreen, closeHint;
        readonly VisualElement screen, content, statusBar;
        readonly Label statusTime, statusFollowers;
        readonly List<UIPhoneApp> apps = new List<UIPhoneApp>();
        UIPhoneApp current;
        ScrollView scroll;
        Label lockClock;
        bool dirty, pendingKeep;
        float anim = 1f, openTime;
        int clockKey = -1;
        long followerCache = -1;

        // render texture panel
        RenderTexture rt;
        PanelSettings rtSettings;
        UIDocument rtDoc;
        VisualElement rtRoot;
        bool in3D;

        public readonly UIAppSealog Sealog;
        public readonly UIAppTasks Tasks;
        public readonly UIAppMarket Market;
        public readonly UIAppGallery Gallery;
        public readonly UIAppShop Shop;
        public readonly UIAppFinTube FinTube;
        public readonly UIAppKrill Krill;
        public readonly UIAppFollowFish FollowFish;
        public readonly UIAppMap Map;
        public readonly UIAppMessages Messages;
        public readonly UIAppSettings Settings;

        static Texture2D wallpaper;

        public UIPhone(UIController ui, VisualElement layer, PanelSettings mainSettings)
        {
            this.ui = ui;

            // ---------------- the screen (moves between the render-texture panel and the 2D overlay)
            screen = new VisualElement { name = "sb-phone-screen" };
            screen.style.position = Position.Absolute;
            screen.style.left = 0; screen.style.top = 0;
            screen.style.width = W; screen.style.height = H;
            screen.style.overflow = Overflow.Hidden;
            screen.style.backgroundColor = PhoneKit.Bg;
            if (wallpaper == null) wallpaper = UIKit.VGradient(UIPalette.Sand, UIPalette.Rgb(0x8FD3E0));

            statusBar = UIKit.Row();
            statusBar.style.height = 58f;
            statusBar.style.flexShrink = 0;
            UIKit.Pad(statusBar, 8f, 30f, 0f, 32f);
            statusTime = UIKit.Text("", 26f, UIPalette.Ink, true);
            statusBar.Add(statusTime);
            statusBar.Add(UIKit.Flex());
            var fGlyph = new UIGlyph(UIGlyphs.Person, UIPalette.Ink, 22f);
            fGlyph.style.marginRight = 5f;
            statusBar.Add(fGlyph);
            statusFollowers = UIKit.Text("", 22f, UIPalette.Ink, true);
            statusFollowers.style.marginRight = 14f;
            statusBar.Add(statusFollowers);
            var bat = UIKit.Box();
            UIKit.Size(bat, 36f, 18f);
            UIKit.Border(bat, 2f, UIPalette.A(UIPalette.Ink, 0.8f));
            UIKit.Radius(bat, 4f);
            UIKit.Pad(bat, 2f);
            var batFill = UIKit.Box();
            batFill.style.width = Length.Percent(38f);
            batFill.style.backgroundColor = UIPalette.Warn;
            UIKit.Radius(batFill, 2f);
            bat.Add(batFill);
            statusBar.Add(bat);
            UIKit.IgnorePicking(statusBar);
            screen.Add(statusBar);

            content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.overflow = Overflow.Hidden;
            screen.Add(content);

            var homeBar = new VisualElement();
            homeBar.style.height = 44f;
            homeBar.style.flexShrink = 0;
            UIKit.Center(homeBar);
            var pill = UIKit.Box();
            pill.pickingMode = PickingMode.Ignore;
            UIKit.Size(pill, 170f, 8f);
            UIKit.Radius(pill, 4f);
            pill.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.55f);
            homeBar.Add(pill);
            homeBar.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); GoHome(); });
            screen.Add(homeBar);
            screen.style.flexDirection = FlexDirection.Column;

            // ---------------- 2D overlay (fallback)
            overlay = new VisualElement { name = "sb-phone" };
            UIKit.FillParent(overlay);
            overlay.pickingMode = PickingMode.Ignore;
            layer.Add(overlay);

            backdrop = new VisualElement();
            UIKit.FillParent(backdrop);
            UIKit.Image(backdrop, UIKit.HGradient(new Color(0.06f, 0.08f, 0.16f, 0.0f), new Color(0.06f, 0.08f, 0.16f, 0.45f)), false);
            backdrop.RegisterCallback<ClickEvent>(e => { if (e.target == backdrop) Close(); });
            overlay.Add(backdrop);

            frame = new VisualElement { name = "sb-phone-frame" };
            frame.style.position = Position.Absolute;
            frame.style.right = Length.Percent(9f);
            frame.style.top = Length.Percent(50f);
            frame.style.width = W + 44f;
            frame.style.height = H + 44f;
            frame.style.marginTop = -(H + 44f) * 0.5f;
            frame.style.backgroundColor = UIPalette.Rgb(0x3A3F4C);
            UIKit.Radius(frame, 64f);
            UIKit.Pad(frame, 22f);
            UIKit.Border(frame, 3f, UIPalette.Rgb(0x6A7080));
            frame.style.transformOrigin = new TransformOrigin(Length.Percent(100f), Length.Percent(50f), 0f);
            frame.style.scale = new Scale(new Vector2(0.76f, 0.76f));
            overlay.Add(frame);
            frameScreen = new VisualElement();
            frameScreen.style.flexGrow = 1;
            frameScreen.style.overflow = Overflow.Hidden;
            frameScreen.style.backgroundColor = UIPalette.Rgb(0x15171F);
            UIKit.Radius(frameScreen, 44f);
            frame.Add(frameScreen);

            // small "Tab ✕" hint while the 3D phone is up (no clickable backdrop in that mode)
            closeHint = UIKit.Row();
            UIKit.Abs(closeHint, 0f, null, 0f, 40f);
            closeHint.style.justifyContent = Justify.Center;
            closeHint.Add(UIKit.HudKeyCap(GameInput.BindingLabel(GameAction.Phone), 30f));
            var ch = new UIHudGlyph(UIGlyphs.Cross, Color.white, 22f);
            ch.style.marginLeft = 8f;
            closeHint.Add(ch);
            UIKit.IgnorePicking(closeHint);
            closeHint.style.display = DisplayStyle.None;
            layer.Add(closeHint);

            overlay.style.display = DisplayStyle.None;

            Sealog = Register(new UIAppSealog());
            Tasks = Register(new UIAppTasks());
            Market = Register(new UIAppMarket());
            FinTube = Register(new UIAppFinTube());
            Shop = Register(new UIAppShop());
            Map = Register(new UIAppMap());
            Gallery = Register(new UIAppGallery());
            Messages = Register(new UIAppMessages());
            FollowFish = Register(new UIAppFollowFish());
            Krill = Register(new UIAppKrill());
            Settings = Register(new UIAppSettings());

            CreateRenderTarget(mainSettings);
            Place(rtRoot != null && !UIPrefs.PhoneFlat);
            BuildLockScreen();
        }

        T Register<T>(T app) where T : UIPhoneApp
        {
            app.Attach(this);
            apps.Add(app);
            return app;
        }

        public UIController UI => ui;
        public bool IsOpen => UIState.PhoneOpen;
        public UIPhoneApp Current => current;
        public IReadOnlyList<UIPhoneApp> Apps => apps;
        public bool In3D => in3D;

        public static bool CanOpen =>
            GameManager.I != null && GameManager.I.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen && !UIState.PauseOpen && !UIState.InventoryOpen;

        // ------------------------------------------------------------------ render texture panel
        void CreateRenderTarget(PanelSettings mainSettings)
        {
            try
            {
                rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "SB_PhoneScreen", antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                rt.Create();

                rtSettings = ScriptableObject.CreateInstance<PanelSettings>();
                rtSettings.name = "SB_PhonePanelSettings";
                if (mainSettings != null && mainSettings.themeStyleSheet != null) rtSettings.themeStyleSheet = mainSettings.themeStyleSheet;
                rtSettings.targetTexture = rt;
                rtSettings.scaleMode = PanelScaleMode.ConstantPixelSize;
                rtSettings.scale = 1f;
                rtSettings.clearColor = true;
                rtSettings.colorClearValue = PhoneKit.Bg;
                rtSettings.sortingOrder = -10;
                rtSettings.SetScreenToPanelSpaceFunction(ScreenToPhonePanel);

                var go = new GameObject("SB_PhoneUI");
                go.SetActive(false);
                // NOT under the main UI object: a UIDocument below another UIDocument becomes a nested child document
                // (drawn into the parent's screen panel at its top-left, ignoring our render-texture PanelSettings)
                go.transform.SetParent(ui.transform.parent, false);
                rtDoc = go.AddComponent<UIDocument>();
                rtDoc.panelSettings = rtSettings;
                go.SetActive(true);
                rtRoot = rtDoc.rootVisualElement;
                if (rtRoot == null) throw new Exception("no root visual element");
                rtRoot.style.width = W;
                rtRoot.style.height = H;
                UIFonts.Apply(rtRoot);
                PhoneDisplay.Texture = rt;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sealebrities UI: phone render texture unavailable, using the flat phone. " + e.Message);
                rtRoot = null;
                PhoneDisplay.Texture = rt;
            }
        }

        static readonly Vector2 Invalid = new Vector2(float.NaN, float.NaN);

        /// Screen position (top-left origin, as UI Toolkit passes it) → phone panel pixels.
        Vector2 ScreenToPhonePanel(Vector2 screenPos)
        {
            if (!in3D || !UIState.PhoneOpen) return Invalid;
            var f = PhoneDisplay.ScreenToUV;
            if (f == null) return Invalid;
            Vector2? uv;
            try { uv = f(new Vector2(screenPos.x, Screen.height - screenPos.y)); }
            catch (Exception) { return Invalid; }
            if (!uv.HasValue) return Invalid;
            var v = uv.Value;
            if (v.x < 0f || v.x > 1f || v.y < 0f || v.y > 1f) return Invalid;
            return new Vector2(v.x * W, (1f - v.y) * H);
        }

        /// Moves the screen into the render-texture panel (3D) or into the 2D overlay frame.
        void Place(bool to3D)
        {
            if (to3D && rtRoot == null) to3D = false;
            var target = to3D ? rtRoot : frameScreen;
            if (screen.parent != target)
            {
                screen.RemoveFromHierarchy();
                target.Add(screen);
            }
            in3D = to3D;
        }

        void Dispose()
        {
            if (rtDoc != null) UnityEngine.Object.Destroy(rtDoc.gameObject);
            if (rt != null) { if (ReferenceEquals(PhoneDisplay.Texture, rt)) PhoneDisplay.Texture = null; rt.Release(); UnityEngine.Object.Destroy(rt); }
            if (rtSettings != null) UnityEngine.Object.Destroy(rtSettings);
        }

        public void OnDestroy() => Dispose();

        // ------------------------------------------------------------------ open / close / navigation
        public void Open(UIPhoneApp app = null)
        {
            if (!CanOpen) return;
            bool wasOpen = UIState.PhoneOpen;
            UIState.PhoneOpen = true;
            if (!wasOpen)
            {
                anim = 0f;
                openTime = 0f;
                clockKey = -1;
                followerCache = -1;
                UpdateMode(true);
                GameEvents.RaisePhoneOpened();
            }
            if (current != null && current != app) current.OnClose();
            current = app;
            if (app != null) app.OnOpen();
            Rebuild(false);
        }

        public void Close()
        {
            if (!UIState.PhoneOpen && overlay.style.display.value == DisplayStyle.None) return;
            UIState.PhoneOpen = false;
            if (current != null) current.OnClose();
            overlay.style.display = DisplayStyle.None;
            closeHint.style.display = DisplayStyle.None;
            Settings.View?.CancelCapture();
            var focused = screen.panel != null ? screen.panel.focusController.focusedElement as VisualElement : null;
            if (focused != null) focused.Blur();
            if (rtRoot != null && !UIPrefs.PhoneFlat) Place(true);
            BuildLockScreen();
        }

        public void Toggle()
        {
            if (UIState.PhoneOpen) Close(); else Open();
        }

        /// J key: open the Sealog directly (or close it when it is already showing).
        public void ToggleApp(UIPhoneApp app)
        {
            if (UIState.PhoneOpen && current == app) { Close(); return; }
            Open(app);
        }

        /// Opens an app page (deferred to the next frame, safe to call from click handlers).
        public void OpenApp(UIPhoneApp app)
        {
            if (current != null && current != app) current.OnClose();
            current = app;
            if (app != null) app.OnOpen();
            Refresh(false);
        }

        public void GoHome()
        {
            if (current != null) current.OnClose();
            current = null;
            Refresh(false);
        }

        public void Back()
        {
            if (current == null) { Close(); return; }
            if (!current.Back())
            {
                current.OnClose();
                current = null;
            }
            Refresh(false);
        }

        /// Game state changed: rebuild the visible page next frame, keeping the scroll position.
        public void MarkDirty()
        {
            if (current != null && !current.LiveUpdates) return;
            Refresh(true);
        }

        /// Requests a rebuild of the visible page on the next frame (never rebuilds inside an event callback).
        public void Refresh(bool keepScroll = false)
        {
            pendingKeep = dirty ? pendingKeep && keepScroll : keepScroll;
            dirty = true;
        }

        /// Rebuild the visible page now (keepScroll keeps the scroll position, e.g. after buying something).
        public void Rebuild(bool keepScroll)
        {
            dirty = false;
            if (!UIState.PhoneOpen) return;
            Vector2 saved = keepScroll && scroll != null ? scroll.scrollOffset : Vector2.zero;
            content.Clear();
            scroll = null;
            if (current == null) BuildHome();
            else BuildApp(current);
            if (scroll != null && saved.y > 0.5f)
            {
                var sv = scroll;
                EventCallback<GeometryChangedEvent> cb = null;
                cb = _ =>
                {
                    sv.scrollOffset = saved;
                    sv.contentContainer.UnregisterCallback(cb);
                };
                sv.contentContainer.RegisterCallback(cb);
            }
            UpdateStatus(true);
        }

        // ------------------------------------------------------------------ lock screen (closed phone, shown on the 3D model)
        void BuildLockScreen()
        {
            content.Clear();
            scroll = null;
            screen.style.backgroundImage = Background.FromTexture2D(wallpaper);
            screen.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            var page = UIKit.Col();
            page.style.flexGrow = 1;
            page.style.alignItems = Align.Center;
            page.style.paddingTop = 120f;
            var gm = GameManager.I;
            var clock = UIKit.Text(gm != null ? gm.ClockString : "--:--", 120f, UIPalette.Ink, true);
            page.Add(clock);
            lockClock = clock;
            if (gm != null && gm.InGame)
            {
                var day = UIKit.Text(Loc.Tr("Tag ", "Day ") + gm.State.day, PhoneKit.Big, UIPalette.InkSoft, true);
                page.Add(day);
            }
            page.Add(UIKit.Flex());
            var brand = UIKit.Text("ClamOS", PhoneKit.Small, UIPalette.A(UIPalette.Ink, 0.4f), true);
            brand.style.letterSpacing = 6f;
            brand.style.marginBottom = 30f;
            page.Add(brand);
            UIKit.IgnorePicking(page);
            content.Add(page);
            UpdateStatus(true);
        }

        // ------------------------------------------------------------------ home screen
        void BuildHome()
        {
            screen.style.backgroundImage = Background.FromTexture2D(wallpaper);
            screen.style.backgroundSize = new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            var page = UIKit.Col();
            page.style.flexGrow = 1;
            UIKit.Pad(page, 6f, 26f, 10f, 26f);
            content.Add(page);

            var gm = GameManager.I;
            var s = gm != null ? gm.State : null;

            var clock = UIKit.Text(gm != null ? gm.ClockString : "--:--", 92f, UIPalette.Ink, true);
            clock.style.unityTextAlign = TextAnchor.MiddleCenter;
            clock.style.marginTop = 6f;
            page.Add(clock);
            if (s != null)
            {
                var day = UIKit.Text(Loc.Tr("Tag ", "Day ") + s.day, PhoneKit.Body, UIPalette.InkSoft, true);
                day.style.unityTextAlign = TextAnchor.MiddleCenter;
                page.Add(day);

                var w = UIKit.Row();
                w.style.marginTop = 18f;
                w.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, UIKit.Compact(s.money) + " $"));
                w.Add(UIKit.HSpacer(10f));
                w.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Compact(s.followers)));
                page.Add(w);
            }

            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            grid.style.marginTop = 28f;
            page.Add(grid);
            foreach (var app in apps) grid.Add(AppTile(app));

            page.Add(UIKit.Flex());
        }

        VisualElement AppTile(UIPhoneApp app)
        {
            var tile = UIKit.Col();
            tile.style.width = Length.Percent(33.33f);
            tile.style.alignItems = Align.Center;
            tile.style.marginBottom = 22f;

            var icon = UIWidgets.IconTile(app.Icon, app.Color, 112f);
            UIKit.Radius(icon, 26f);
            icon.style.overflow = Overflow.Visible;
            int badge = app.Badge;
            if (badge != 0)
            {
                var b = UIKit.BadgeLabel(badge, 34f);
                b.style.right = -8f; b.style.top = -8f;
                icon.Add(b);
            }
            if (app.ShowLock)
            {
                var lockBg = UIKit.Box();
                lockBg.pickingMode = PickingMode.Ignore;
                lockBg.style.position = Position.Absolute;
                lockBg.style.right = -6f; lockBg.style.bottom = -6f;
                UIKit.Size(lockBg, 38f, 38f);
                UIKit.Radius(lockBg, 19f);
                lockBg.style.backgroundColor = Color.white;
                UIKit.Center(lockBg);
                lockBg.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 24f));
                icon.Add(lockBg);
            }
            tile.Add(icon);
            var label = UIKit.Text(app.Title, 22f, UIPalette.Ink, true);
            label.style.marginTop = 8f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            tile.Add(label);

            UIKit.IgnorePicking(tile);
            tile.pickingMode = PickingMode.Position;
            tile.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); OpenApp(app); });
            tile.RegisterCallback<PointerEnterEvent>(_ => icon.style.scale = new Scale(new Vector2(1.06f, 1.06f)));
            tile.RegisterCallback<PointerLeaveEvent>(_ => icon.style.scale = new Scale(Vector2.one));
            UIKit.Transition(icon, 0.12f, "scale");
            return tile;
        }

        // ------------------------------------------------------------------ app page
        void BuildApp(UIPhoneApp app)
        {
            screen.style.backgroundImage = StyleKeyword.None;
            var header = UIKit.Row();
            header.style.height = 84f;
            header.style.flexShrink = 0;
            UIKit.Pad(header, 0f, 22f, 0f, 16f);
            content.Add(header);

            header.Add(UIWidgets.RoundIconButton(UIGlyphs.ChevronLeft, Back, 58f));
            var tileIcon = UIWidgets.IconTile(app.Icon, app.Color, 52f);
            tileIcon.style.marginLeft = 12f;
            tileIcon.style.marginRight = 12f;
            header.Add(tileIcon);
            var title = UIKit.Text(app.HeaderTitle, PhoneKit.Big, UIPalette.Ink, true);
            title.style.flexShrink = 1;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            header.Add(title);

            scroll = UIKit.Scroll();
            scroll.style.flexShrink = 1;
            UIKit.Pad(scroll.contentContainer, 8f, 22f, 40f, 22f);
            content.Add(scroll);
            try
            {
                app.Build(scroll.contentContainer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                scroll.contentContainer.Add(UIWidgets.Notice(Loc.Tr("Diese App ist abgestürzt. Versuch es gleich nochmal.", "This app crashed. Please try again."), UIPalette.Coral, UIGlyphs.Warning, PhoneKit.Body));
            }
        }

        // ------------------------------------------------------------------ per frame
        void UpdateStatus(bool force)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            int key = gm.State.day * 2000 + Mathf.FloorToInt(Mathf.Repeat(gm.State.timeOfDay, 1f) * 1440f);
            if (force || key != clockKey)
            {
                clockKey = key;
                statusTime.text = gm.ClockString;
                if (!UIState.PhoneOpen && lockClock != null && lockClock.panel != null) lockClock.text = statusTime.text;
            }
            long f = gm.State.followers;
            if (force || f != followerCache)
            {
                followerCache = f;
                statusFollowers.text = UIKit.Compact(f);
            }
        }

        /// Decides between the 3D phone (render texture) and the 2D overlay.
        void UpdateMode(bool force)
        {
            bool open = UIState.PhoneOpen;
            bool can3D = rtRoot != null && !UIPrefs.PhoneFlat && PhoneDisplay.ScreenToUV != null && PhoneDisplay.Texture != null;
            // give the Player module a moment to raise the model; without it fall back to the flat phone
            bool want3D = can3D && (PhoneDisplay.Raised || openTime < 0.9f);
            if (!open) want3D = rtRoot != null && !UIPrefs.PhoneFlat;
            if (force || want3D != in3D) Place(want3D);
            bool showOverlay = open && !in3D;
            UIKit.Show(overlay, showOverlay);
            UIKit.Show(closeHint, open && in3D);
        }

        public void Tick(float dt)
        {
            if (!UIState.PhoneOpen)
            {
                if (UIState.MainMenuOpen) return;
                // keep the lock-screen clock current for the 3D model
                if (in3D) UpdateStatus(false);
                return;
            }
            if (!CanOpen && !UIState.PhoneOpen) return;
            if (GameManager.I == null || !GameManager.I.InGame || UIState.MainMenuOpen || UIState.LoadingOpen || UIState.PauseOpen) { Close(); return; }
            openTime += dt;
            UpdateMode(false);
            if (anim < 1f && !in3D)
            {
                anim = Mathf.Min(1f, anim + dt / 0.25f);
                float e = 1f - (1f - anim) * (1f - anim) * (1f - anim);
                frame.style.translate = new Translate(0f, 80f * (1f - e));
                frame.style.opacity = e;
                backdrop.style.opacity = e;
            }
            else if (in3D && anim < 1f)
            {
                anim = 1f;
                frame.style.translate = new Translate(0f, 0f);
                frame.style.opacity = 1f;
                backdrop.style.opacity = 1f;
            }
            UpdateStatus(false);
            if (dirty) Rebuild(pendingKeep);
            if (current != null) current.Tick(dt);
        }

        /// Language changed: rebuild the visible page.
        public void RefreshTexts()
        {
            Settings.View?.Refresh();
            if (UIState.PhoneOpen) Refresh(true);
            else BuildLockScreen();
        }
    }
}
