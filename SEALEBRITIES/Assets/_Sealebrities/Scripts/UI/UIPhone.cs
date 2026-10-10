using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Base class of the ClamOS phone apps. Apps rebuild their page from game state whenever the phone marks them dirty.
    public abstract class UIPhoneApp
    {
        protected UIPhone Phone;

        internal void Attach(UIPhone phone) { Phone = phone; }

        public abstract string Id { get; }
        public abstract string Title { get; }
        public abstract Color Color { get; }
        public abstract UIGlyph.DrawFn Icon { get; }
        public virtual int Badge => 0;
        public virtual bool ShowLock => false;
        /// Header title (detail pages may override).
        public virtual string HeaderTitle => Title;
        /// Called when the app is opened from the home screen or directly (J key).
        public virtual void OnOpen() { }
        /// Internal back navigation; return true when handled (otherwise the phone goes home).
        public virtual bool Back() => false;
        public abstract void Build(VisualElement body);
        public virtual void Tick(float dt) { }
    }

    /// The ClamOS smartphone: portrait frame, status bar, home grid and app pages.
    public class UIPhone
    {
        readonly UIController ui;
        readonly VisualElement root, backdrop, frame, screen, content;
        readonly Label statusTime, statusFollowers;
        readonly List<UIPhoneApp> apps = new List<UIPhoneApp>();
        UIPhoneApp current;
        ScrollView scroll;
        bool dirty, pendingKeep;
        float anim = 1f;
        int clockKey = -1;
        long followerCache = -1;

        public readonly UIAppSealog Sealog;
        public readonly UIAppShop Shop;
        public readonly UIAppFinTube FinTube;
        public readonly UIAppKrill Krill;
        public readonly UIAppFollowFish FollowFish;
        public readonly UIAppMap Map;
        public readonly UIAppMessages Messages;
        public readonly UIAppSettings Settings;

        static Texture2D wallpaper;

        public UIPhone(UIController ui, VisualElement layer)
        {
            this.ui = ui;
            root = new VisualElement { name = "sb-phone" };
            UIKit.FillParent(root);
            root.pickingMode = PickingMode.Ignore;
            layer.Add(root);

            backdrop = new VisualElement();
            UIKit.FillParent(backdrop);
            UIKit.Image(backdrop, UIKit.HGradient(new Color(0f, 0f, 0f, 0.05f), new Color(0f, 0f, 0f, 0.55f)), false);
            backdrop.RegisterCallback<ClickEvent>(_ => Close());
            root.Add(backdrop);

            var wrap = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(wrap);
            wrap.style.alignItems = Align.FlexEnd;
            wrap.style.justifyContent = Justify.Center;
            wrap.style.paddingRight = Length.Percent(11f);
            root.Add(wrap);

            frame = new VisualElement { name = "sb-phone-frame" };
            frame.style.width = 440f;
            frame.style.height = 860f;
            frame.style.maxHeight = Length.Percent(95f);
            frame.style.flexShrink = 0;
            frame.style.backgroundColor = UIPalette.Rgb(0x05080D);
            UIKit.Radius(frame, 56f);
            UIKit.Pad(frame, 12f);
            UIKit.Border(frame, 2f, new Color(0.55f, 0.65f, 0.75f, 0.35f));
            wrap.Add(frame);

            screen = new VisualElement();
            screen.style.flexGrow = 1;
            UIKit.Radius(screen, 44f);
            screen.style.overflow = Overflow.Hidden;
            if (wallpaper == null) wallpaper = UIKit.VGradient(UIPalette.Rgb(0x050F1B), UIPalette.Rgb(0x123F5E));
            UIKit.Image(screen, wallpaper, false);
            screen.style.backgroundColor = UIPalette.Navy;
            frame.Add(screen);

            // status bar
            var status = UIKit.Row();
            status.style.height = 44f;
            status.style.flexShrink = 0;
            UIKit.Pad(status, 6f, 28f, 0f, 30f);
            statusTime = UIKit.Text("", 15f, UIPalette.Text, true);
            status.Add(statusTime);
            status.Add(UIKit.Flex());
            var island = UIKit.Box();
            UIKit.Abs(island, null, 10f, null, null);
            island.style.left = Length.Percent(50f);
            island.style.marginLeft = -58f;
            UIKit.Size(island, 116f, 30f);
            UIKit.Radius(island, 15f);
            island.style.backgroundColor = Color.black;
            status.Add(island);
            var fGlyph = new UIGlyph(UIGlyphs.Person, UIPalette.Text, 13f);
            fGlyph.style.marginRight = 4f;
            status.Add(fGlyph);
            statusFollowers = UIKit.Text("", 13f, UIPalette.Text, true);
            statusFollowers.style.marginRight = 10f;
            status.Add(statusFollowers);
            var bat = UIKit.Box();
            UIKit.Size(bat, 24f, 12f);
            UIKit.Border(bat, 1.5f, UIPalette.A(UIPalette.Text, 0.8f));
            UIKit.Radius(bat, 3f);
            UIKit.Pad(bat, 1.5f);
            var batFill = UIKit.Box();
            batFill.style.flexGrow = 1;
            batFill.style.backgroundColor = UIPalette.Money;
            UIKit.Radius(batFill, 1.5f);
            bat.Add(batFill);
            status.Add(bat);
            UIKit.IgnorePicking(status);
            screen.Add(status);

            content = new VisualElement();
            content.style.flexGrow = 1;
            content.style.flexShrink = 1;
            content.style.overflow = Overflow.Hidden;
            screen.Add(content);

            // home indicator
            var homeBar = new VisualElement();
            homeBar.style.height = 26f;
            homeBar.style.flexShrink = 0;
            homeBar.style.alignItems = Align.Center;
            homeBar.style.justifyContent = Justify.Center;
            var pill = UIKit.Box();
            pill.pickingMode = PickingMode.Ignore;
            UIKit.Size(pill, 140f, 5f);
            UIKit.Radius(pill, 3f);
            pill.style.backgroundColor = UIPalette.A(UIPalette.Text, 0.7f);
            homeBar.Add(pill);
            homeBar.RegisterCallback<ClickEvent>(_ => GoHome());
            screen.Add(homeBar);

            Sealog = Register(new UIAppSealog());
            Shop = Register(new UIAppShop());
            FinTube = Register(new UIAppFinTube());
            Krill = Register(new UIAppKrill());
            FollowFish = Register(new UIAppFollowFish());
            Map = Register(new UIAppMap());
            Messages = Register(new UIAppMessages());
            Settings = Register(new UIAppSettings());

            root.style.display = DisplayStyle.None;
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

        public static bool CanOpen =>
            GameManager.I != null && GameManager.I.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen && !UIState.PauseOpen;

        // ------------------------------------------------------------------ open / close / navigation
        public void Open(UIPhoneApp app = null)
        {
            if (!CanOpen) return;
            bool wasOpen = UIState.PhoneOpen;
            UIState.PhoneOpen = true;
            root.style.display = DisplayStyle.Flex;
            if (!wasOpen)
            {
                anim = 0f;
                clockKey = -1;
                followerCache = -1;
                GameEvents.RaisePhoneOpened();
            }
            current = app;
            if (app != null) app.OnOpen();
            Rebuild(false);
        }

        public void Close()
        {
            if (!UIState.PhoneOpen && root.style.display.value == DisplayStyle.None) return;
            UIState.PhoneOpen = false;
            root.style.display = DisplayStyle.None;
            var focused = root.panel != null ? root.panel.focusController.focusedElement as VisualElement : null;
            if (focused != null) focused.Blur();
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
            current = app;
            if (app != null) app.OnOpen();
            Refresh(false);
        }

        public void GoHome()
        {
            current = null;
            Refresh(false);
        }

        public void Back()
        {
            if (current == null) { Close(); return; }
            if (!current.Back()) current = null;
            Refresh(false);
        }

        /// Game state changed: rebuild the visible page next frame, keeping the scroll position.
        public void MarkDirty() { Refresh(true); }

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

        // ------------------------------------------------------------------ home screen
        void BuildHome()
        {
            var page = UIKit.Col();
            page.style.flexGrow = 1;
            UIKit.Pad(page, 8f, 20f, 10f, 20f);
            content.Add(page);

            var gm = GameManager.I;
            var s = gm != null ? gm.State : null;

            var clock = UIKit.Text(gm != null ? gm.ClockString : "--:--", 66f, UIPalette.Text, false);
            clock.style.unityTextAlign = TextAnchor.MiddleCenter;
            clock.style.marginTop = 10f;
            UIKit.Shadow(clock, 10f, 0.4f);
            page.Add(clock);
            var date = UIKit.Text((s != null ? Loc.Tr("Tag ", "Day ") + s.day + " · " : "") + Loc.Tr("Deine Insel", "Your island"), 15f, UIPalette.TextDim, true);
            date.style.unityTextAlign = TextAnchor.MiddleCenter;
            page.Add(date);

            // "today" widget
            if (s != null)
            {
                var w = UIKit.Card(new Color(1f, 1f, 1f, 0.07f), 22f, 14f);
                w.style.marginTop = 22f;
                var wr = UIKit.Row();
                wr.Add(UIWidgets.StatTile(Loc.Money(s.money), Loc.Tr("Geld", "Money"), UIPalette.Money));
                wr.Add(UIKit.HSpacer(8f));
                wr.Add(UIWidgets.StatTile(Loc.Number(s.followers), Loc.Tr("Follower", "Followers"), UIPalette.Lagoon));
                w.Add(wr);
                string line = TodayLine(s);
                if (line != null)
                {
                    var l = UIKit.Para(line, 13f, UIPalette.Sun);
                    l.style.marginTop = 10f;
                    w.Add(l);
                }
                page.Add(w);
            }

            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            grid.style.marginTop = 26f;
            page.Add(grid);
            foreach (var app in apps) grid.Add(AppTile(app));

            page.Add(UIKit.Flex());
            var brand = UIKit.Caption("ClamOS", UIPalette.A(UIPalette.Text, 0.35f), 12f);
            brand.style.letterSpacing = 6f;
            brand.style.alignSelf = Align.Center;
            page.Add(brand);
        }

        static string TodayLine(GameState s)
        {
            int clips = s.bankedFootage.Count;
            if (clips > 0) return Loc.Tr($"{clips} Clips bereit für FinTube", $"{clips} clips ready for FinTube");
            foreach (var d in s.deliveries)
                if (d.dropped) return Loc.Tr("Eine Lieferung wartet am Helipad!", "A delivery is waiting at the helipad!");
            if (s.pendingUploads.Count > 0) return Loc.Tr("Video-Ergebnisse kommen morgen früh", "Video results arrive tomorrow morning");
            return null;
        }

        VisualElement AppTile(UIPhoneApp app)
        {
            var tile = UIKit.Col();
            tile.style.width = Length.Percent(25f);
            tile.style.alignItems = Align.Center;
            tile.style.marginBottom = 18f;

            var icon = UIKit.Box();
            UIKit.Size(icon, 70f, 70f);
            UIKit.Radius(icon, 19f);
            icon.style.backgroundColor = app.Color;
            icon.style.alignItems = Align.Center;
            icon.style.justifyContent = Justify.Center;
            icon.style.overflow = Overflow.Visible;
            var shine = UIKit.Box();
            shine.pickingMode = PickingMode.Ignore;
            UIKit.Abs(shine, 0f, 0f, 0f, null);
            shine.style.height = 35f;
            shine.style.borderTopLeftRadius = 19f; shine.style.borderTopRightRadius = 19f;
            shine.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            icon.Add(shine);
            icon.Add(new UIGlyph(app.Icon, UIPalette.OnColor(app.Color) == UIPalette.Deep ? UIPalette.Deep : Color.white, 40f));
            int badge = app.Badge;
            if (badge > 0)
            {
                var b = UIKit.Text(badge > 99 ? "99+" : badge.ToString(), 12f, Color.white, true);
                b.pickingMode = PickingMode.Ignore;
                b.style.position = Position.Absolute;
                b.style.right = -6f; b.style.top = -6f;
                b.style.minWidth = 22f; b.style.height = 22f;
                b.style.backgroundColor = UIPalette.Live;
                UIKit.Radius(b, 11f);
                UIKit.Pad(b, 0f, 6f, 0f, 6f);
                b.style.unityTextAlign = TextAnchor.MiddleCenter;
                UIKit.Border(b, 2f, UIPalette.Deep);
                icon.Add(b);
            }
            if (app.ShowLock)
            {
                var lockBg = UIKit.Box();
                lockBg.pickingMode = PickingMode.Ignore;
                lockBg.style.position = Position.Absolute;
                lockBg.style.right = -6f; lockBg.style.bottom = -6f;
                UIKit.Size(lockBg, 24f, 24f);
                UIKit.Radius(lockBg, 12f);
                lockBg.style.backgroundColor = UIPalette.Deep;
                lockBg.style.alignItems = Align.Center;
                lockBg.style.justifyContent = Justify.Center;
                lockBg.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.Sun, 15f));
                icon.Add(lockBg);
            }
            tile.Add(icon);
            var label = UIKit.Text(app.Title, 12f, UIPalette.Text, true);
            label.style.marginTop = 6f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            UIKit.Shadow(label, 3f, 0.6f);
            tile.Add(label);

            UIKit.IgnorePicking(icon);
            label.pickingMode = PickingMode.Ignore;
            tile.RegisterCallback<ClickEvent>(_ => OpenApp(app));
            tile.RegisterCallback<PointerEnterEvent>(_ => icon.style.scale = new Scale(new Vector2(1.06f, 1.06f)));
            tile.RegisterCallback<PointerLeaveEvent>(_ => icon.style.scale = new Scale(Vector2.one));
            UIKit.Transition(icon, 0.12f, "scale");
            return tile;
        }

        // ------------------------------------------------------------------ app page
        void BuildApp(UIPhoneApp app)
        {
            var header = UIKit.Row();
            header.style.height = 52f;
            header.style.flexShrink = 0;
            UIKit.Pad(header, 0f, 18f, 0f, 12f);
            header.style.borderBottomWidth = 1f;
            header.style.borderBottomColor = UIPalette.Line;
            content.Add(header);

            var back = UIKit.Box();
            UIKit.Size(back, 36f, 36f);
            UIKit.Radius(back, 18f);
            back.style.alignItems = Align.Center;
            back.style.justifyContent = Justify.Center;
            back.Add(new UIGlyph(UIGlyphs.ChevronLeft, UIPalette.Text, 20f));
            UIKit.Clickable(back, Back, new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.18f));
            header.Add(back);

            var dot = UIKit.Dot(app.Color, 10f);
            dot.style.marginLeft = 12f;
            dot.style.marginRight = 8f;
            header.Add(dot);
            var title = UIKit.Text(app.HeaderTitle, 19f, UIPalette.Text, true);
            title.style.flexShrink = 1;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            header.Add(title);

            scroll = UIKit.Scroll();
            scroll.style.flexShrink = 1;
            UIKit.Pad(scroll.contentContainer, 14f, 16f, 24f, 16f);
            content.Add(scroll);
            try
            {
                app.Build(scroll.contentContainer);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                scroll.contentContainer.Add(UIWidgets.Notice(Loc.Tr("Diese App ist abgestürzt. Versuch es gleich nochmal.", "This app crashed. Please try again."), UIPalette.Coral, UIGlyphs.Warning));
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
            }
            long f = gm.State.followers;
            if (force || f != followerCache)
            {
                followerCache = f;
                statusFollowers.text = Loc.Number(f);
            }
        }

        public void Tick(float dt)
        {
            if (!UIState.PhoneOpen) return;
            if (!CanOpen) { Close(); return; }
            if (anim < 1f)
            {
                anim = Mathf.Min(1f, anim + dt / 0.25f);
                float e = 1f - (1f - anim) * (1f - anim) * (1f - anim);
                frame.style.translate = new Translate(0f, 70f * (1f - e));
                frame.style.opacity = e;
                backdrop.style.opacity = e;
            }
            UpdateStatus(false);
            if (dirty) Rebuild(pendingKeep);
            if (current != null) current.Tick(dt);
        }
    }
}
