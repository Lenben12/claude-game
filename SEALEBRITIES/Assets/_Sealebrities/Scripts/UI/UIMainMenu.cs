using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Main menu over the 3D island (transparent right side): continue, new game (difficulty cards), co-op teaser,
    /// language, controls and quit.
    public class UIMainMenu
    {
        enum Page { Main, Difficulty, Controls }

        readonly UIController ui;
        readonly VisualElement root;
        readonly VisualElement mainPage, overlayPage;
        Page page = Page.Main;
        Difficulty selected = Difficulty.OpenWater;
        bool starting, dirty;
        float anim = 1f;

        public UIMainMenu(UIController ui, VisualElement layer)
        {
            this.ui = ui;
            root = new VisualElement { name = "sb-menu" };
            UIKit.FillParent(root);
            root.pickingMode = PickingMode.Ignore;
            layer.Add(root);

            var shade = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(shade, 0f, 0f, null, 0f);
            shade.style.width = Length.Percent(64f);
            UIKit.Image(shade, UIKit.HGradient(UIPalette.A(UIPalette.Navy, 0.94f), UIPalette.A(UIPalette.Navy, 0f)), false);
            root.Add(shade);

            var bottomShade = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(bottomShade, 0f, null, 0f, 0f);
            bottomShade.style.height = 180f;
            UIKit.Image(bottomShade, UIKit.VGradient(UIPalette.A(UIPalette.Deep, 0.7f), UIPalette.A(UIPalette.Deep, 0f)), false);
            root.Add(bottomShade);

            mainPage = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(mainPage);
            root.Add(mainPage);

            overlayPage = new VisualElement();
            UIKit.FillParent(overlayPage);
            overlayPage.style.backgroundColor = UIPalette.A(UIPalette.Deep, 0.84f);
            overlayPage.style.alignItems = Align.Center;
            overlayPage.style.justifyContent = Justify.Center;
            overlayPage.style.display = DisplayStyle.None;
            root.Add(overlayPage);

            root.style.display = DisplayStyle.None;
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
        }

        /// Esc inside the menu: back to the main page.
        public void Back()
        {
            if (!Visible || starting) return;
            if (page != Page.Main) { page = Page.Main; Refresh(); }
        }

        /// Rebuild on the next frame (safe inside click callbacks).
        void Refresh() { dirty = true; }

        public void Tick(float dt)
        {
            if (!Visible) return;
            if (dirty) Rebuild();
            if (anim >= 1f) return;
            anim = Mathf.Min(1f, anim + dt / 0.5f);
            float e = 1f - (1f - anim) * (1f - anim);
            mainPage.style.opacity = e;
            mainPage.style.translate = new Translate(-30f * (1f - e), 0f);
        }

        public void Rebuild()
        {
            dirty = false;
            if (!Visible) return;
            BuildMain();
            overlayPage.Clear();
            if (page == Page.Difficulty) BuildDifficulty();
            else if (page == Page.Controls) BuildControls();
            UIKit.Show(overlayPage, page != Page.Main);
            if (page != Page.Main) { anim = 1f; mainPage.style.opacity = 0.25f; mainPage.style.translate = new Translate(0f, 0f); }
        }

        // ------------------------------------------------------------------ main page
        void BuildMain()
        {
            mainPage.Clear();
            mainPage.style.opacity = 1f;

            var col = UIKit.Col();
            UIKit.Abs(col, 120f, 0f, null, 0f);
            col.style.width = 560f;
            col.style.justifyContent = Justify.Center;
            mainPage.Add(col);

            var kicker = UIKit.Caption(Loc.Tr("Ein Tauch-Vlog-Abenteuer", "A diving vlog adventure"), UIPalette.Lagoon, 15f);
            kicker.style.letterSpacing = 4f;
            col.Add(kicker);

            var title = UIKit.Text("SEALEBRITIES", 92f, UIPalette.Text, true);
            title.style.letterSpacing = 6f;
            title.style.marginLeft = -4f;
            UIKit.Shadow(title, 16f, 0.5f);
            col.Add(title);

            var pill = UIKit.Row();
            pill.style.alignSelf = Align.FlexStart;
            pill.style.backgroundColor = UIPalette.Live;
            UIKit.Radius(pill, 999f);
            UIKit.Pad(pill, 6f, 16f, 6f, 14f);
            var dot = UIKit.Dot(Color.white, 9f);
            dot.style.marginRight = 9f;
            pill.Add(dot);
            var pt = UIKit.Text("LIVE FROM THE DEEP", 17f, Color.white, true);
            pt.style.letterSpacing = 3f;
            pill.Add(pt);
            col.Add(pill);

            var tagline = UIKit.Para(Loc.Tr("Tauche, filme Meerestiere und werde berühmt. Ziel: 1.000.000 Follower.",
                                            "Dive, film sea creatures and get famous. Goal: 1,000,000 followers."), 19f, UIPalette.TextDim);
            tagline.style.marginTop = 22f;
            tagline.style.marginBottom = 34f;
            col.Add(tagline);

            bool hasSave = GameManager.HasSave;
            if (hasSave)
                col.Add(MenuButton(Loc.Tr("Fortsetzen", "Continue"), Loc.Tr("Weiter mit deinem Spielstand", "Resume your save"), UIBtnKind.Primary, OnContinue));
            col.Add(MenuButton(Loc.Tr("Neues Spiel", "New Game"), Loc.Tr("Schwierigkeit wählen und loslegen", "Pick a difficulty and start"),
                               hasSave ? UIBtnKind.Secondary : UIBtnKind.Primary, () => { page = Page.Difficulty; Refresh(); }));

            // co-op teaser (disabled)
            var coop = UIKit.Col();
            coop.style.backgroundColor = new Color(1f, 1f, 1f, 0.04f);
            UIKit.Border(coop, 1f, new Color(1f, 1f, 1f, 0.07f));
            UIKit.Radius(coop, 14f);
            UIKit.Pad(coop, 12f, 20f, 12f, 20f);
            coop.style.marginBottom = 10f;
            var coopRow = UIKit.Row();
            coopRow.Add(UIKit.Text(Loc.Tr("Mit Freunden spielen", "Play with Friends"), 20f, UIPalette.TextFaint, true));
            coopRow.Add(UIKit.Flex());
            coopRow.Add(UIKit.Pill(Loc.Tr("BALD", "SOON"), UIPalette.A(UIPalette.Sun, 0.85f)));
            coop.Add(coopRow);
            var coopSub = UIKit.Para(Loc.Tr("Co-op über Steam-Lobby kommt in einem späteren Update", "Co-op via Steam lobby is coming in a later update"), 14f, UIPalette.TextFaint);
            coopSub.style.marginTop = 3f;
            coop.Add(coopSub);
            col.Add(coop);

            var langRow = UIKit.Row();
            langRow.style.marginBottom = 10f;
            var langLabel = UIKit.Text(Loc.Tr("Sprache", "Language"), 16f, UIPalette.TextDim, true);
            langLabel.style.width = 120f;
            langRow.Add(langLabel);
            var seg = UIWidgets.LanguageSelector();
            seg.style.flexGrow = 1;
            langRow.Add(seg);
            col.Add(langRow);

            var row = UIKit.Row();
            var controls = UIKit.Btn(Loc.Tr("Steuerung", "Controls"), () => { page = Page.Controls; Refresh(); }, UIBtnKind.Secondary, 17f);
            controls.style.flexGrow = 1;
            controls.style.marginRight = 10f;
            row.Add(controls);
            var quit = UIKit.Btn(Loc.Tr("Beenden", "Quit"), OnQuit, UIBtnKind.Ghost, 17f);
            quit.style.flexGrow = 1;
            row.Add(quit);
            col.Add(row);

            var footer = UIKit.Text(Loc.Tr("Prototyp · Einzelspieler · Unity 6", "Prototype · Single player · Unity 6"), 13f, UIPalette.TextFaint);
            UIKit.Abs(footer, 120f, null, null, 36f);
            mainPage.Add(footer);
        }

        VisualElement MenuButton(string title, string subtitle, UIBtnKind kind, System.Action onClick)
        {
            var b = UIKit.Btn("", onClick, kind, 22f);
            b.style.flexDirection = FlexDirection.Column;
            b.style.alignItems = Align.FlexStart;
            UIKit.Pad(b, 14f, 22f, 14f, 22f);
            UIKit.Radius(b, 14f);
            b.style.marginBottom = 10f;
            var skin = b.userData as UIBtnSkin;
            Color fg = skin != null ? skin.fg : Color.white;
            var t = UIKit.Text(title, 22f, fg, true);
            t.pickingMode = PickingMode.Ignore;
            b.Add(t);
            var s = UIKit.Text(subtitle, 14f, UIPalette.A(fg, 0.72f));
            s.pickingMode = PickingMode.Ignore;
            s.style.marginTop = 2f;
            b.Add(s);
            return b;
        }

        // ------------------------------------------------------------------ difficulty page
        void BuildDifficulty()
        {
            var panel = UIKit.Col();
            panel.style.alignItems = Align.Center;
            panel.style.maxWidth = Length.Percent(96f);
            overlayPage.Add(panel);

            panel.Add(UIKit.Caption(Loc.Tr("Neues Spiel", "New game"), UIPalette.Lagoon, 14f));
            var h = UIKit.Text(Loc.Tr("Wähle deine Schwierigkeit", "Choose your difficulty"), 40f, UIPalette.Text, true);
            h.style.marginTop = 6f;
            h.style.marginBottom = 28f;
            panel.Add(h);

            var cards = UIKit.Row();
            cards.style.alignItems = Align.Stretch;
            cards.style.flexWrap = Wrap.Wrap;
            cards.style.justifyContent = Justify.Center;
            panel.Add(cards);
            for (int i = 0; i < 5; i++) cards.Add(DifficultyCard((Difficulty)i));

            if (GameManager.HasSave)
            {
                var warn = UIKit.Row();
                warn.style.marginTop = 22f;
                warn.style.backgroundColor = UIPalette.A(UIPalette.Sun, 0.12f);
                UIKit.Radius(warn, 12f);
                UIKit.Pad(warn, 10f, 16f, 10f, 14f);
                var icon = new UIGlyph(UIGlyphs.Warning, UIPalette.Sun, 22f);
                icon.style.marginRight = 10f;
                warn.Add(icon);
                warn.Add(UIKit.Text(Loc.Tr("Achtung: Ein neues Spiel überschreibt deinen bisherigen Spielstand.",
                                           "Warning: a new game overwrites your existing save."), 16f, UIPalette.Sun, true));
                panel.Add(warn);
            }

            var buttons = UIKit.Row();
            buttons.style.marginTop = 28f;
            var back = UIKit.Btn(Loc.Tr("Zurück", "Back"), () => { page = Page.Main; Refresh(); }, UIBtnKind.Ghost, 18f);
            back.style.width = 200f;
            back.style.marginRight = 14f;
            buttons.Add(back);
            var start = UIKit.Btn(Loc.Tr("Tauchgang starten", "Start diving") + "  ·  " + Loc.DifficultyName(selected), OnStartNewGame, UIBtnKind.Primary, 18f);
            start.style.minWidth = 360f;
            buttons.Add(start);
            panel.Add(buttons);
        }

        VisualElement DifficultyCard(Difficulty d)
        {
            bool sel = d == selected;
            Color accent = UIPalette.DifficultyColor(d);
            var ds = DifficultySettings.For(d);

            var card = UIKit.Col();
            card.style.width = 292f;
            card.style.minHeight = 330f;
            card.style.marginLeft = 8f; card.style.marginRight = 8f; card.style.marginBottom = 12f;
            UIKit.Radius(card, 18f);
            UIKit.Border(card, sel ? 3f : 1f, sel ? accent : new Color(1f, 1f, 1f, 0.08f));
            UIKit.Pad(card, 0f);
            card.style.overflow = Overflow.Hidden;
            card.style.translate = new Translate(0f, sel ? -6f : 0f);
            UIKit.Clickable(card, () => { selected = d; Refresh(); }, sel ? UIPalette.Raised2 : UIPalette.PanelSolid, UIPalette.Raised2);

            var strip = new VisualElement { pickingMode = PickingMode.Ignore };
            strip.style.height = 6f;
            strip.style.backgroundColor = accent;
            card.Add(strip);

            var body = UIKit.Col();
            body.pickingMode = PickingMode.Ignore;
            UIKit.Pad(body, 18f, 20f, 20f, 20f);
            body.style.flexGrow = 1;
            card.Add(body);

            var top = UIKit.Row();
            top.pickingMode = PickingMode.Ignore;
            top.Add(UIKit.Text(Loc.DifficultyName(d), 24f, accent, true));
            top.Add(UIKit.Flex());
            if (d == Difficulty.OpenWater) top.Add(UIKit.Pill(Loc.Tr("EMPFOHLEN", "RECOMMENDED"), UIPalette.A(UIPalette.Lagoon, 0.9f)));
            body.Add(top);

            var desc = UIKit.Para(DifficultySettings.Description(d), 14.5f, UIPalette.TextDim);
            desc.style.marginTop = 10f;
            desc.style.flexGrow = 1;
            body.Add(desc);

            var chips = UIKit.Row();
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 14f;
            chips.Add(Chip(Loc.Tr("Luft ", "Air ") + UIKit.Mult(ds.airMultiplier)));
            chips.Add(Chip(Loc.Tr("Einnahmen ", "Income ") + UIKit.Mult(ds.incomeMultiplier)));
            chips.Add(Chip(Loc.Tr("Rettung ", "Rescue ") + Loc.Money(ds.rescueFee)));
            if (ds.narcosis) chips.Add(Chip(Loc.Tr("Narkose", "Narcosis"), UIPalette.Violet));
            if (!ds.canFaint) chips.Add(Chip(Loc.Tr("Kein Ohnmachtsrisiko", "Can't faint"), UIPalette.Lagoon));
            if (ds.permadeath) chips.Add(Chip("Permadeath", UIPalette.Live));
            body.Add(chips);
            UIKit.IgnorePicking(body);
            return card;
        }

        static Label Chip(string text, Color? accent = null)
        {
            var c = UIKit.Text(text, 12.5f, accent ?? UIPalette.Text, true);
            c.style.backgroundColor = accent.HasValue ? UIPalette.A(accent.Value, 0.16f) : new Color(1f, 1f, 1f, 0.07f);
            UIKit.Radius(c, 8f);
            UIKit.Pad(c, 4f, 8f, 4f, 8f);
            c.style.marginRight = 6f; c.style.marginBottom = 6f;
            return c;
        }

        // ------------------------------------------------------------------ controls page
        void BuildControls()
        {
            var panel = UIKit.Card(UIPalette.PanelSolid, 22f, 34f);
            panel.style.width = 980f;
            panel.style.maxWidth = Length.Percent(94f);
            overlayPage.Add(panel);

            panel.Add(UIKit.Caption(Loc.Tr("Hilfe", "Help"), UIPalette.Lagoon, 14f));
            var h = UIKit.Text(Loc.Tr("Steuerung", "Controls"), 36f, UIPalette.Text, true);
            h.style.marginTop = 4f; h.style.marginBottom = 20f;
            panel.Add(h);

            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            panel.Add(grid);
            foreach (var entry in UIWidgets.ControlsList())
            {
                var row = UIKit.Row();
                row.style.width = Length.Percent(50f);
                row.style.paddingTop = 7f; row.style.paddingBottom = 7f;
                row.style.paddingRight = 16f;
                var keys = UIKit.Row();
                keys.style.width = 210f;
                keys.style.flexWrap = Wrap.Wrap;
                foreach (var k in entry.keys)
                {
                    var cap = UIKit.KeyCap(k, 14f);
                    cap.style.marginRight = 5f;
                    cap.style.marginBottom = 2f;
                    keys.Add(cap);
                }
                row.Add(keys);
                var act = UIKit.Text(entry.action, 16f, UIPalette.Text);
                act.style.flexShrink = 1;
                act.style.whiteSpace = WhiteSpace.Normal;
                row.Add(act);
                grid.Add(row);
            }

            var back = UIKit.Btn(Loc.Tr("Zurück", "Back"), () => { page = Page.Main; Refresh(); }, UIBtnKind.Secondary, 17f);
            back.style.alignSelf = Align.FlexEnd;
            back.style.marginTop = 20f;
            back.style.width = 200f;
            panel.Add(back);
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
                    GameEvents.RaiseToast(Loc.Tr("Spielstand konnte nicht geladen werden.", "Could not load the save."));
                    Rebuild();
                }
            }, 1.4f);
        }

        void OnStartNewGame()
        {
            if (starting || GameManager.I == null) return;
            starting = true;
            var d = selected;
            Fader.FadeThrough(() =>
            {
                if (GameManager.I != null) GameManager.I.NewGame(d);
            }, 1.6f);
        }

        void OnQuit()
        {
            if (starting) return;
            Application.Quit();
        }
    }
}
