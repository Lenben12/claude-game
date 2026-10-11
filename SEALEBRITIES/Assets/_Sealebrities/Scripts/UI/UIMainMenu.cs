using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Main menu over the 3D scene (the bean crew sitting on the dock at golden hour, Player module).
    /// Clean and simple (art bible §1.14): water/bubble logo top-left, plain outlined text list, no panels, no pills.
    /// Overlays: difficulty (3 icon cards) and settings (light cards).
    public class UIMainMenu
    {
        enum Page { Main, Difficulty, Settings }

        readonly UIController ui;
        readonly VisualElement root, mainPage, overlayPage, bubbleLayer, logo;
        readonly VisualElement[] bubbles = new VisualElement[7];
        readonly float[] bubbleSeed = new float[7];
        Page page = Page.Main;
        Difficulty selected = Difficulty.OpenWater;
        bool starting, dirty;
        float anim = 1f, time;
        UISettingsView settings;

        const float LogoW = 980f, LogoH = 150f;

        public UIMainMenu(UIController ui, VisualElement layer)
        {
            this.ui = ui;
            root = new VisualElement { name = "sb-menu" };
            UIKit.FillParent(root);
            root.pickingMode = PickingMode.Ignore;
            layer.Add(root);

            // very soft violet shade on the left so the white text reads on the golden-hour scene (no panel)
            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(shade, 0f, 0f, null, 0f);
            shade.style.width = Length.Percent(48f);
            UIKit.Image(shade, UIKit.HGradient(new Color(0.08f, 0.05f, 0.15f, 0.34f), new Color(0.08f, 0.05f, 0.15f, 0f)), false);
            root.Add(shade);

            logo = BuildLogo();
            root.Add(logo);

            bubbleLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(bubbleLayer, 120f, 40f, null, null);
            UIKit.Size(bubbleLayer, LogoW, 240f);
            root.Add(bubbleLayer);
            for (int i = 0; i < bubbles.Length; i++)
            {
                float size = 10f + (i * 37 % 17);
                var b = new VisualElement { pickingMode = PickingMode.Ignore };
                b.style.position = Position.Absolute;
                UIKit.Size(b, size, size);
                UIKit.Radius(b, size * 0.5f);
                UIKit.Border(b, 2.5f, new Color(1f, 1f, 1f, 0.85f));
                b.style.backgroundColor = new Color(0.62f, 0.9f, 0.93f, 0.25f);
                bubbleSeed[i] = (i * 0.618f) % 1f;
                b.style.left = 40f + bubbleSeed[i] * (LogoW - 260f);
                bubbleLayer.Add(b);
                bubbles[i] = b;
            }

            mainPage = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(mainPage);
            root.Add(mainPage);

            overlayPage = new VisualElement();
            UIKit.FillParent(overlayPage);
            overlayPage.style.backgroundColor = new Color(0.08f, 0.06f, 0.16f, 0.45f);
            overlayPage.style.alignItems = Align.Center;
            overlayPage.style.justifyContent = Justify.Center;
            overlayPage.style.display = DisplayStyle.None;
            root.Add(overlayPage);

            root.style.display = DisplayStyle.None;
        }

        VisualElement BuildLogo()
        {
            // lower part of the letters filled with turquoise water (UILogo), top-left at (120, 96)
            var host = UILogo.Build(120f, LogoW, LogoH, TextAnchor.MiddleLeft);
            UIKit.Abs(host, 120f, 96f, null, null);
            return host;
        }

        public bool Visible => root.style.display.value == DisplayStyle.Flex;

        public void Show()
        {
            UIState.MainMenuOpen = true;
            starting = false;
            page = Page.Main;
            selected = Difficulty.OpenWater;
            root.style.display = DisplayStyle.Flex;
            anim = 0f;
            Rebuild();
        }

        public void Hide()
        {
            root.style.display = DisplayStyle.None;
            starting = false;
            settings?.CancelCapture();
        }

        /// Esc inside the menu: back to the main page.
        public void Back()
        {
            if (!Visible || starting) return;
            if (page != Page.Main) { settings?.CancelCapture(); page = Page.Main; Refresh(); }
        }

        /// Rebuild on the next frame (safe inside click callbacks).
        public void Refresh() { dirty = true; }

        /// After the first-start character screen (Q42/1c): straight to the difficulty cards of a new game.
        public void ShowNewGame()
        {
            if (!Visible) Show();
            page = Page.Difficulty;
            Refresh();
        }

        public void Tick(float dt)
        {
            if (!Visible) return;
            time += dt;
            if (dirty) Rebuild();
            if (page == Page.Settings) settings?.Tick(dt);
            TickBubbles();
            if (anim >= 1f) return;
            anim = Mathf.Min(1f, anim + dt / 0.6f);
            float e = 1f - (1f - anim) * (1f - anim);
            mainPage.style.opacity = e;
            mainPage.style.translate = new Translate(-30f * (1f - e), 0f);
            logo.style.opacity = e;
        }

        void TickBubbles()
        {
            for (int i = 0; i < bubbles.Length; i++)
            {
                float speed = 0.09f + bubbleSeed[i] * 0.08f;
                float y = Mathf.Repeat(bubbleSeed[i] + time * speed, 1f);
                bubbles[i].style.top = 210f - y * 210f;
                bubbles[i].style.opacity = y < 0.15f ? y / 0.15f : y > 0.8f ? (1f - y) / 0.2f : 1f;
                bubbles[i].style.translate = new Translate(Mathf.Sin(time * (1.2f + bubbleSeed[i]) + i) * 8f, 0f);
            }
        }

        public void Rebuild()
        {
            dirty = false;
            if (!Visible) return;
            BuildMain();
            overlayPage.Clear();
            if (page == Page.Difficulty) BuildDifficulty();
            else if (page == Page.Settings) BuildSettings();
            UIKit.Show(overlayPage, page != Page.Main);
            UIKit.Show(mainPage, page == Page.Main);
            if (page != Page.Main) { anim = 1f; mainPage.style.opacity = 1f; mainPage.style.translate = new Translate(0f, 0f); }
        }

        // ------------------------------------------------------------------ main page
        void BuildMain()
        {
            mainPage.Clear();
            mainPage.style.opacity = 1f;

            var col = UIKit.Col();
            UIKit.Abs(col, 120f, 330f, null, null);
            mainPage.Add(col);

            if (GameManager.HasSave) col.Add(MenuItem(Loc.Tr("Fortsetzen", "Continue"), OnContinue, true));
            col.Add(MenuItem(Loc.Tr("Neues Spiel", "New Game"), () => { page = Page.Difficulty; Refresh(); }, !GameManager.HasSave));
            col.Add(MenuItem(Loc.Tr("Mit Freunden", "With Friends"), OnFriends, false));
            col.Add(MenuItem(Loc.Tr("Charakter", "Character"), () => ui.OpenCharacter(CharacterScreenContext.MainMenu, null), false));
            col.Add(MenuItem(Loc.Tr("Einstellungen", "Settings"), () => { page = Page.Settings; Refresh(); }, false));
            col.Add(MenuItem(Loc.Tr("Beenden", "Quit"), OnQuit, false));

            // footer: version + language quick switch
            var footer = UIKit.HudText(Loc.Tr("Testversion 4", "Test build 4") + " · " + Application.version, 14f, new Color(1f, 1f, 1f, 0.5f), false);
            UIKit.Abs(footer, 120f, null, null, 36f);
            mainPage.Add(footer);
            var lang = UIKit.Row();
            UIKit.Abs(lang, null, null, 40f, 30f);
            lang.Add(LangItem("DE", Language.German));
            lang.Add(UIKit.HudText(" · ", 22f, new Color(1f, 1f, 1f, 0.6f)));
            lang.Add(LangItem("EN", Language.English));
            mainPage.Add(lang);
        }

        static Label LangItem(string text, Language l)
        {
            bool sel = Loc.Current == l;
            var t = UIKit.HudText(text, 22f, sel ? Color.white : new Color(1f, 1f, 1f, 0.55f));
            t.pickingMode = PickingMode.Position;
            t.RegisterCallback<ClickEvent>(_ =>
            {
                if (Loc.Current == l) return;
                UIAudio.Click();
                Settings.Data.language = l;
                Settings.Apply();
                Settings.Save();
            });
            t.RegisterCallback<PointerEnterEvent>(_ => t.style.color = UIPalette.Coral);
            t.RegisterCallback<PointerLeaveEvent>(_ => t.style.color = Loc.Current == l ? Color.white : new Color(1f, 1f, 1f, 0.55f));
            return t;
        }

        /// Plain text menu entry (40 px, white, outlined); hover = coral + 8 px shift + bubble bullet.
        VisualElement MenuItem(string text, System.Action onClick, bool primary)
        {
            var row = UIKit.Row();
            row.style.marginBottom = 18f;
            row.style.height = 52f;
            var bullet = UIKit.Box();
            UIKit.Size(bullet, 16f, 16f);
            UIKit.Radius(bullet, 8f);
            UIKit.Border(bullet, 3f, UIPalette.Coral);
            bullet.style.backgroundColor = new Color(1f, 1f, 1f, 0.6f);
            bullet.style.marginRight = 12f;
            bullet.style.opacity = 0f;
            bullet.pickingMode = PickingMode.Ignore;
            row.Add(bullet);
            var l = UIKit.HudText(text, 40f, primary ? UIPalette.Sand : Color.white);
            l.pickingMode = PickingMode.Ignore;
            row.Add(l);
            row.style.translate = new Translate(-28f, 0f);
            UIKit.Transition(row, 0.12f, "translate");
            row.pickingMode = PickingMode.Position;
            Color normal = primary ? UIPalette.Sand : Color.white;
            row.RegisterCallback<PointerEnterEvent>(_ =>
            {
                l.style.color = UIPalette.Coral;
                bullet.style.opacity = 1f;
                row.style.translate = new Translate(-20f, 0f);
            });
            row.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                l.style.color = normal;
                bullet.style.opacity = 0f;
                row.style.translate = new Translate(-28f, 0f);
            });
            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (starting) return;
                UIAudio.Click();
                onClick?.Invoke();
            });
            return row;
        }

        // ------------------------------------------------------------------ difficulty page
        void BuildDifficulty()
        {
            var panel = UIKit.Col();
            panel.style.alignItems = Align.Center;
            panel.style.maxWidth = Length.Percent(96f);
            overlayPage.Add(panel);

            var h = UIKit.HudText(Loc.Tr("Wie entspannt soll's sein?", "How relaxed do you want it?"), 44f);
            h.style.marginBottom = 26f;
            panel.Add(h);

            var cards = UIKit.Row();
            cards.style.alignItems = Align.Stretch;
            cards.style.justifyContent = Justify.Center;
            panel.Add(cards);
            var sel = DifficultySettings.Selectable;
            for (int i = 0; i < sel.Length; i++) cards.Add(DifficultyCard(sel[i]));

            if (GameManager.HasSave)
            {
                var warn = UIKit.Row();
                warn.style.marginTop = 20f;
                warn.style.backgroundColor = new Color(1f, 1f, 1f, 0.92f);
                UIKit.Radius(warn, 16f);
                UIKit.Pad(warn, 10f, 18f, 10f, 14f);
                warn.Add(new UIGlyph(UIGlyphs.Warning, UIPalette.SunInk, 28f));
                var wt = UIKit.Text(Loc.Tr("Überschreibt deinen Spielstand", "Overwrites your save"), 19f, UIPalette.Ink, true);
                wt.style.marginLeft = 10f;
                warn.Add(wt);
                panel.Add(warn);
            }

            var buttons = UIKit.Row();
            buttons.style.marginTop = 26f;
            var back = UIKit.Btn(Loc.Tr("Zurück", "Back"), () => { page = Page.Main; Refresh(); }, UIBtnKind.Secondary, 22f);
            back.style.width = 220f;
            back.style.marginRight = 16f;
            buttons.Add(back);
            var start = UIKit.Btn(Loc.Tr("Los geht's!", "Let's go!"), OnStartNewGame, UIBtnKind.Primary, 24f);
            start.style.minWidth = 300f;
            buttons.Add(start);
            panel.Add(buttons);
        }

        VisualElement DifficultyCard(Difficulty d)
        {
            bool locked = !DifficultySettings.IsUnlocked(d);
            if (locked && selected == d) selected = Difficulty.OpenWater;
            bool sel = d == selected;
            Color accent = locked ? UIPalette.Silhouette : UIPalette.DifficultyColor(d);
            var ds = DifficultySettings.For(d);

            var card = UIKit.Col();
            card.style.width = 330f;
            card.style.marginLeft = 12f; card.style.marginRight = 12f;
            card.style.alignItems = Align.Center;
            UIKit.Radius(card, 28f);
            UIKit.Pad(card, 26f, 24f, 24f, 24f);
            UIKit.Border(card, sel ? 5f : 2f, sel ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.08f));
            card.style.translate = new Translate(0f, sel ? -10f : 0f);
            if (locked)
            {
                // Q79: Schwer unlocks after a finale on Normal (grey card, lock glyph, one-line hint)
                card.style.backgroundColor = UIPalette.A(UIPalette.CardAlt, 0.92f);
                card.RegisterCallback<ClickEvent>(_ => UIAudio.Nope());
                card.Add(UIWidgets.IconTile(UIGlyphs.Lock, accent, 110f));
                var ln = UIKit.Text(Loc.DifficultyName(d), 36f, UIPalette.Silhouette, true);
                ln.style.marginTop = 14f;
                card.Add(ln);
                var hint = UIKit.Para(DifficultySettings.LockedHint(d) ?? Loc.Tr("Schwer – frei nach dem Finale auf Normal", "Hard – unlocks after finishing Normal"), 18f, UIPalette.InkSoft);
                hint.style.marginTop = 16f;
                hint.style.unityTextAlign = TextAnchor.UpperCenter;
                card.Add(hint);
                UIKit.IgnorePicking(card);
                card.pickingMode = PickingMode.Position;
                return card;
            }
            UIKit.Clickable(card, () => { selected = d; Refresh(); }, sel ? Color.white : UIPalette.A(UIPalette.Card, 0.95f), Color.white);

            card.Add(UIWidgets.IconTile(UIGlyphs.ForDifficulty(d), accent, 110f));
            var name = UIKit.Text(Loc.DifficultyName(d), 36f, UIPalette.Ink, true);
            name.style.marginTop = 14f;
            card.Add(name);

            var rows = UIKit.Col();
            rows.style.marginTop = 16f;
            rows.style.alignSelf = Align.Stretch;
            rows.Add(StatLine(UIGlyphs.Bubbles, UIPalette.Turquoise, Loc.Tr("Luft", "Air"), UIKit.Mult(ds.airMultiplier)));
            rows.Add(StatLine(UIGlyphs.Coin, UIPalette.Sun, Loc.Tr("Einnahmen", "Income"), UIKit.Mult(ds.incomeMultiplier)));
            rows.Add(StatLine(ds.canFaint ? UIGlyphs.Warning : UIGlyphs.Check, ds.canFaint ? UIPalette.Coral : UIPalette.Leaf,
                              Loc.Tr("Ohnmacht", "Fainting"), ds.canFaint ? "−" + Loc.Money(ds.rescueFee) : Loc.Tr("nie", "never")));
            if (ds.narcosis) rows.Add(StatLine(UIGlyphs.Eye, UIPalette.Violet, Loc.Tr("Tiefenrausch", "Narcosis"), "30 m"));
            card.Add(rows);

            var desc = UIKit.Para(DifficultySettings.Description(d), 15f, UIPalette.InkSoft);
            desc.style.marginTop = 14f;
            desc.style.unityTextAlign = TextAnchor.UpperCenter;
            card.Add(desc);
            if (d == Difficulty.OpenWater)
            {
                var rec = UIKit.Pill(Loc.Tr("Empfohlen", "Recommended"), UIPalette.Turquoise, Color.white, 15f);
                rec.style.position = Position.Absolute;
                rec.style.top = -14f;
                rec.style.alignSelf = Align.Center;
                card.Add(rec);
            }
            UIKit.IgnorePicking(card);
            card.pickingMode = PickingMode.Position;
            return card;
        }

        static VisualElement StatLine(UIGlyph.DrawFn icon, Color iconColor, string label, string value)
        {
            var row = UIKit.Row();
            row.style.marginBottom = 8f;
            row.Add(new UIGlyph(icon, UIWidgets.Ink(iconColor), 30f));
            var l = UIKit.Text(label, 19f, UIPalette.InkSoft, true);
            l.style.marginLeft = 10f;
            row.Add(l);
            row.Add(UIKit.Flex());
            row.Add(UIKit.Text(value, 22f, UIPalette.Ink, true));
            return row;
        }

        // ------------------------------------------------------------------ settings page
        void BuildSettings()
        {
            var card = UIWidgets.ModalCard(860f);
            card.Add(UIWidgets.Header(UIGlyphs.Gear, UIPalette.Rgb(0x9AA3AE), Loc.Tr("Einstellungen", "Settings"), () => { settings?.CancelCapture(); page = Page.Main; Refresh(); }, 30f));
            if (settings == null) settings = new UISettingsView(false);
            settings.OnWatchIntro = () => { settings?.CancelCapture(); page = Page.Main; ui.WatchIntro(); };
            settings.OnOpenCharacter = () => ui.OpenCharacter(CharacterScreenContext.MainMenu, null);
            settings.Root.RemoveFromHierarchy();
            card.Add(settings.Root);
            settings.Refresh();
            overlayPage.Add(card);
        }

        // ------------------------------------------------------------------ actions
        void OnContinue()
        {
            if (starting || GameManager.I == null) return;
            starting = true;
            Fader.FadeThrough(() =>
            {
                bool ok = GameManager.I != null && GameManager.I.LoadGame();
                if (!ok)
                {
                    starting = false;
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Spielstand defekt", "Save could not load"));
                    Rebuild();
                }
            }, 1.4f);
        }

        void OnStartNewGame()
        {
            if (starting || GameManager.I == null) return;
            starting = true;
            var d = DifficultySettings.IsUnlocked(selected) ? selected : Difficulty.OpenWater;
            Fader.FadeThrough(() =>
            {
                if (GameManager.I != null) GameManager.I.NewGame(d);
            }, 1.6f);
        }

        void OnFriends()
        {
            if (Net.OpenLobby != null)
            {
                try { Net.OpenLobby(); }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                    ui.Notes.Push(UIGlyphs.Crew, UIPalette.Turquoise, Loc.Tr("Lobby-Fehler", "Lobby error"), Loc.Tr("Mit Freunden", "With friends"));
                }
            }
            else
            {
                UIAudio.Nope();
                ui.Notes.Push(UIGlyphs.Crew, UIPalette.Turquoise, Loc.Tr("Kein Mehrspieler", "No multiplayer"), Loc.Tr("in diesem Build", "in this build"), "nolobby", null, false);
            }
        }

        void OnQuit()
        {
            if (starting) return;
            Application.Quit();
        }
    }
}
