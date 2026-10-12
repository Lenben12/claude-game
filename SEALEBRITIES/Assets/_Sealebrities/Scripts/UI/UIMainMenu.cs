using System;
using System.IO;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Main menu over the 3D scene (the bean crew sitting on the dock at golden hour, Player module).
    /// Clean and simple (art bible §1.14): water/bubble logo top-left, plain outlined text list, no panels, no pills.
    /// Overlays: the four SAVE SLOTS (round 3 step 6b, GDD §19b: one card per world – play time, achievements of that
    /// world, day, followers, money, last played, difficulty, newest photo; Spielen / Neues Spiel / Löschen with a
    /// confirmation), difficulty (3 icon cards, the new world goes into the chosen empty slot) and settings.
    /// „Fortsetzen“ = the slot played last.
    public class UIMainMenu
    {
        enum Page { Main, Slots, Difficulty, Settings }

        readonly UIController ui;
        readonly VisualElement root, mainPage, overlayPage, bubbleLayer, logo;
        readonly VisualElement[] bubbles = new VisualElement[7];
        readonly float[] bubbleSeed = new float[7];
        Page page = Page.Main;
        Difficulty selected = Difficulty.OpenWater;
        bool starting, dirty;
        float anim = 1f, time;
        UISettingsView settings;

        // save slots
        int newSlot;            // the empty slot a new game goes into (difficulty page)
        bool newFromSlots;      // the difficulty page came from the slot cards (Back returns there)
        int slotCards;          // cards on the last built slots page (tests)
        readonly string[] thumbPath = new string[SaveSlots.Count + 1];
        readonly Texture2D[] thumbTex = new Texture2D[SaveSlots.Count + 1];

        const float LogoW = 980f, LogoH = 150f;
        const float SlotCardW = 286f, SlotThumbH = 150f;

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
            ReleaseThumbs();
        }

        /// Esc inside the menu: back one page (difficulty → slot cards → main).
        public void Back()
        {
            if (!Visible || starting) return;
            if (page == Page.Main) return;
            settings?.CancelCapture();
            page = page == Page.Difficulty && newFromSlots ? Page.Slots : Page.Main;
            Refresh();
        }

        /// Rebuild on the next frame (safe inside click callbacks).
        public void Refresh() { dirty = true; }

        /// After the first-start character screen (Q42/1c): straight to the difficulty cards of a new game (first free
        /// slot; all four used → the slot cards).
        public void ShowNewGame()
        {
            if (!Visible) Show();
            OpenNewGame(SaveSlots.FirstFree(), false);
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
            if (page == Page.Slots) BuildSlots();
            else if (page == Page.Difficulty) BuildDifficulty();
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

            int cont = SaveSlots.ContinueSlot;
            if (cont > 0)
            {
                var ci = SaveSlots.Info(cont);
                string sub = "#" + cont + (ci.used && !ci.broken ? " · " + Loc.Tr("Tag ", "Day ") + ci.day : "");
                col.Add(MenuItem(Loc.Tr("Fortsetzen", "Continue"), OnContinue, true, sub));
            }
            else col.Add(MenuItem(Loc.Tr("Neues Spiel", "New Game"), () => OpenNewGame(SaveSlots.FirstFree(), false), true));
            col.Add(MenuItem(Loc.Tr("Spielstände", "Saves"), OpenSlots, false, SaveSlots.UsedCount + "/" + SaveSlots.Count));
            col.Add(MenuItem(Loc.Tr("Mit Freunden", "With Friends"), OnFriends, false));
            col.Add(MenuItem(Loc.Tr("Charakter", "Character"), () => ui.OpenCharacter(CharacterScreenContext.MainMenu, null), false));
            col.Add(MenuItem(Loc.Tr("Einstellungen", "Settings"), () => { page = Page.Settings; Refresh(); }, false));
            col.Add(MenuItem(Loc.Tr("Beenden", "Quit"), OnQuit, false));

            // footer: version + language quick switch
            var footer = UIKit.HudText(Loc.Tr("Testversion 5", "Test build 5") + " · " + Application.version, 14f, new Color(1f, 1f, 1f, 0.5f), false);
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

        /// Plain text menu entry (40 px, white, outlined); hover = coral + 8 px shift + bubble bullet. `sub` = a small
        /// note after the label („#2 · Tag 5“, „3/4“).
        VisualElement MenuItem(string text, Action onClick, bool primary, string sub = null)
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
            if (!string.IsNullOrEmpty(sub))
            {
                var s = UIKit.HudText(sub, 20f, new Color(1f, 1f, 1f, 0.72f), false);
                s.pickingMode = PickingMode.Ignore;
                s.style.marginLeft = 14f;
                s.style.marginTop = 8f;
                row.Add(s);
            }
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

        // ------------------------------------------------------------------ save slots page
        void OpenSlots()
        {
            page = Page.Slots;
            Refresh();
        }

        /// Difficulty cards for a new world in `slot` (must be empty – otherwise the slot cards).
        void OpenNewGame(int slot, bool fromSlots)
        {
            if (!SaveSlots.Valid(slot) || SaveSlots.Exists(slot))
            {
                newFromSlots = false;
                OpenSlots();
                return;
            }
            newSlot = slot;
            newFromSlots = fromSlots;
            page = Page.Difficulty;
            Refresh();
        }

        void BuildSlots()
        {
            var card = UIWidgets.ModalCard(SlotCardW * SaveSlots.Count + 18f * (SaveSlots.Count - 1) + 70f);
            card.Add(UIWidgets.Header(UIGlyphs.Book, UIPalette.Turquoise, Loc.Tr("Spielstände", "Saves"), () => { page = Page.Main; Refresh(); }, 32f));
            var sub = UIKit.Para(Loc.Tr("Jede Welt für sich: Erfolge, Fotos, Fortschritt.", "Every world on its own: achievements, photos, progress."), 17f, UIPalette.InkSoft);
            sub.style.marginTop = -8f;
            sub.style.marginBottom = 16f;
            card.Add(sub);

            var row = UIKit.Row();
            row.style.alignItems = Align.Stretch;
            row.style.justifyContent = Justify.Center;
            slotCards = 0;
            int last = SaveSlots.ContinueSlot;
            for (int i = 1; i <= SaveSlots.Count; i++)
            {
                if (i > 1) row.Add(UIKit.HSpacer(18f));
                row.Add(SlotCard(i, i == last));
                slotCards++;
            }
            card.Add(row);
            overlayPage.Add(card);
        }

        VisualElement SlotCard(int slot, bool lastPlayed)
        {
            var info = SaveSlots.Info(slot);
            bool used = info.used;
            var card = UIKit.Col();
            card.name = "sb-slot-card";
            card.style.width = SlotCardW;
            card.style.flexShrink = 1;
            card.style.backgroundColor = used ? Color.white : UIPalette.A(Color.white, 0.6f);
            UIKit.Radius(card, 22f);
            card.style.overflow = Overflow.Hidden;
            UIKit.Border(card, lastPlayed ? 4f : 2f, lastPlayed ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.08f));

            // ---- picture: newest photo of that world, else a sea gradient with the difficulty icon / a "+"
            var thumb = UIKit.Box();
            thumb.style.height = SlotThumbH;
            thumb.style.flexShrink = 0;
            thumb.style.alignItems = Align.Center;
            thumb.style.justifyContent = Justify.Center;
            Texture2D tex = used && !info.broken ? Thumb(slot, info.latestPhoto) : null;
            if (tex != null) UIKit.ImageCover(thumb, tex);
            else
            {
                if (used) UIKit.Image(thumb, UIKit.VGradient(UIPalette.Rgb(0x1F5F8B), UIPalette.Rgb(0x6FD3DB)), false);
                else UIKit.Image(thumb, UIKit.VGradient(UIPalette.Rgb(0xEADBBE), UIPalette.Rgb(0xF8F1E2)), false);
                if (used && !info.broken) thumb.Add(UIWidgets.IconTile(UIGlyphs.ForDifficulty(info.difficulty), UIPalette.DifficultyColor(info.difficulty), 74f));
                else if (info.broken) thumb.Add(UIWidgets.IconTile(UIGlyphs.Warning, UIPalette.Coral, 74f));
                else thumb.Add(UIWidgets.IconTile(UIGlyphs.Plus, UIPalette.Turquoise, 74f));
            }
            var num = UIKit.Pill("#" + slot, UIPalette.A(UIPalette.Ink, 0.72f), Color.white, 17f);
            num.style.position = Position.Absolute;
            num.style.left = 10f;
            num.style.top = 10f;
            thumb.Add(num);
            if (lastPlayed && used)
            {
                var lp = UIKit.Pill(Loc.Tr("Zuletzt", "Last"), UIPalette.Coral, Color.white, 15f);
                lp.style.position = Position.Absolute;
                lp.style.right = 10f;
                lp.style.top = 10f;
                thumb.Add(lp);
            }
            if (used && info.photos > 0)
            {
                var ph = UIKit.Row();
                ph.style.position = Position.Absolute;
                ph.style.right = 10f;
                ph.style.bottom = 8f;
                ph.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.62f);
                UIKit.Radius(ph, 999f);
                UIKit.Pad(ph, 2f, 9f, 2f, 7f);
                ph.Add(new UIGlyph(UIGlyphs.Image, Color.white, 18f));
                var pt = UIKit.Text(" " + info.photos, 15f, Color.white, true);
                ph.Add(pt);
                thumb.Add(ph);
            }
            card.Add(thumb);

            var body = UIKit.Col();
            UIKit.Pad(body, 12f, 14f, 14f, 14f);
            body.style.flexGrow = 1;
            card.Add(body);

            var title = UIKit.Row();
            title.Add(UIKit.Text(Loc.Tr("Spielstand ", "Save ") + slot, 19f, UIPalette.InkSoft, true));
            title.Add(UIKit.Flex());
            if (used && !info.broken)
                title.Add(UIKit.Pill(Loc.DifficultyName(info.difficulty), UIPalette.DifficultyColor(info.difficulty), null, 14f));
            body.Add(title);

            if (!used)
            {
                var empty = UIKit.Text(Loc.Tr("Leer", "Empty"), 30f, UIPalette.Silhouette, true);
                empty.style.marginTop = 6f;
                body.Add(empty);
                body.Add(UIKit.Flex());
                var create = UIKit.Btn(Loc.Tr("Neues Spiel", "New game"), () => OpenNewGame(slot, true), UIBtnKind.Accent, 20f);
                create.name = "sb-slot-new";
                create.style.marginTop = 14f;
                body.Add(create);
                return card;
            }
            if (info.broken)
            {
                var bad = UIKit.Text(Loc.Tr("Defekt", "Damaged"), 30f, UIPalette.CoralInk, true);
                bad.style.marginTop = 6f;
                body.Add(bad);
                body.Add(UIKit.Para(Loc.Tr("Zuletzt: ", "Last: ") + SaveSlots.FormatDate(info.lastPlayed), 14f, UIPalette.InkSoft));
                body.Add(UIKit.Flex());
                var row0 = UIKit.Row();
                row0.style.marginTop = 14f;
                row0.Add(UIKit.Flex());
                var del0 = UIWidgets.RoundIconButton(UIGlyphs.Trash, () => AskDelete(slot), 46f, UIPalette.CoralInk);
                del0.name = "sb-slot-delete";
                row0.Add(del0);
                body.Add(row0);
                return card;
            }

            var day = UIKit.Text(Loc.Tr("Tag ", "Day ") + info.day, 30f, UIPalette.Ink, true);
            day.style.marginTop = 2f;
            body.Add(day);

            var g1 = UIKit.Row();
            g1.style.marginTop = 8f;
            g1.Add(MiniStat(UIGlyphs.Person, UIPalette.TurquoiseInk, UIKit.Compact(info.followers)));
            g1.Add(MiniStat(UIGlyphs.Coin, UIPalette.SunInk, UIKit.Compact(info.money) + " $"));
            body.Add(g1);
            var g2 = UIKit.Row();
            g2.style.marginTop = 6f;
            g2.Add(MiniStat(UIGlyphs.Clock, UIPalette.Violet, SaveSlots.FormatPlayTime(info.playSeconds)));
            g2.Add(MiniStat(UIGlyphs.Trophy, UIPalette.SunInk, info.achievements + "/" + Achievements.Total));
            body.Add(g2);

            var when = UIKit.Text(SaveSlots.FormatDate(info.lastPlayed), 14f, UIPalette.Silhouette);
            when.style.marginTop = 8f;
            body.Add(when);
            body.Add(UIKit.Flex());

            var buttons = UIKit.Row();
            buttons.style.marginTop = 12f;
            var play = UIKit.Btn(Loc.Tr("Spielen", "Play"), () => OnLoadSlot(slot), UIBtnKind.Primary, 20f);
            play.name = "sb-slot-play";
            play.style.flexGrow = 1;
            play.style.marginRight = 10f;
            buttons.Add(play);
            var del = UIWidgets.RoundIconButton(UIGlyphs.Trash, () => AskDelete(slot), 46f, UIPalette.CoralInk);
            del.name = "sb-slot-delete";
            del.tooltip = Loc.Tr("Löschen", "Delete");
            buttons.Add(del);
            body.Add(buttons);
            return card;
        }

        static VisualElement MiniStat(UIGlyph.DrawFn icon, Color color, string value)
        {
            var r = UIKit.Row();
            r.style.flexGrow = 1;
            r.style.flexBasis = 0;
            r.Add(new UIGlyph(icon, color, 24f));
            var t = UIKit.Text(value, 17f, UIPalette.Ink, true);
            t.style.marginLeft = 6f;
            t.style.flexShrink = 1;
            t.style.overflow = Overflow.Hidden;
            t.style.textOverflow = TextOverflow.Ellipsis;
            r.Add(t);
            return r;
        }

        void AskDelete(int slot)
        {
            if (!SaveSlots.Exists(slot)) return;
            UIDialog.Show(UIGlyphs.Trash, UIPalette.Coral,
                Loc.Tr("Spielstand " + slot + " löschen?", "Delete save " + slot + "?"),
                Loc.Tr("Die Welt mit ihren Erfolgen und Fotos ist dann weg.", "The world, its achievements and photos will be gone."),
                Loc.Tr("Abbrechen", "Cancel"), Loc.Tr("Löschen", "Delete"),
                () =>
                {
                    ReleaseThumb(slot);
                    if (!SaveSlots.Delete(slot))
                        GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Nicht alles gelöscht", "Not everything deleted"));
                    Refresh();
                }, null, true);
        }

        // ---- the newest photo of a world as the card picture (loaded once, destroyed when the menu hides)
        Texture2D Thumb(int slot, string path)
        {
            if (!SaveSlots.Valid(slot)) return null;
            if (thumbPath[slot] == path) return thumbTex[slot];
            ReleaseThumb(slot);
            thumbPath[slot] = path;
            if (string.IsNullOrEmpty(path)) return null;
            Texture2D tex = null;
            try
            {
                if (File.Exists(path))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "SB_SlotThumb", wrapMode = TextureWrapMode.Clamp };
                    if (!tex.LoadImage(File.ReadAllBytes(path)))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
            }
            catch (Exception)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                tex = null;
            }
            thumbTex[slot] = tex;
            return tex;
        }

        void ReleaseThumb(int slot)
        {
            if (!SaveSlots.Valid(slot)) return;
            if (thumbTex[slot] != null) UnityEngine.Object.Destroy(thumbTex[slot]);
            thumbTex[slot] = null;
            thumbPath[slot] = null;
        }

        void ReleaseThumbs()
        {
            for (int i = 1; i <= SaveSlots.Count; i++) ReleaseThumb(i);
        }

        // ------------------------------------------------------------------ difficulty page
        void BuildDifficulty()
        {
            var panel = UIKit.Col();
            panel.style.alignItems = Align.Center;
            panel.style.maxWidth = Length.Percent(96f);
            overlayPage.Add(panel);

            if (SaveSlots.Valid(newSlot))
            {
                var chip = UIKit.Pill(Loc.Tr("Neue Welt · Spielstand ", "New world · save ") + newSlot, UIPalette.A(Color.white, 0.92f), UIPalette.Ink, 18f);
                chip.style.marginBottom = 12f;
                panel.Add(chip);
            }
            var h = UIKit.HudText(Loc.Tr("Wie entspannt soll's sein?", "How relaxed do you want it?"), 44f);
            h.style.marginBottom = 26f;
            panel.Add(h);

            var cards = UIKit.Row();
            cards.style.alignItems = Align.Stretch;
            cards.style.justifyContent = Justify.Center;
            panel.Add(cards);
            var sel = DifficultySettings.Selectable;
            for (int i = 0; i < sel.Length; i++) cards.Add(DifficultyCard(sel[i]));

            var buttons = UIKit.Row();
            buttons.style.marginTop = 26f;
            var back = UIKit.Btn(Loc.Tr("Zurück", "Back"), () => { page = newFromSlots ? Page.Slots : Page.Main; Refresh(); }, UIBtnKind.Secondary, 22f);
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
        void OnContinue() => OnLoadSlot(SaveSlots.ContinueSlot);

        void OnLoadSlot(int slot)
        {
            if (starting || GameManager.I == null || !SaveSlots.Exists(slot)) return;
            starting = true;
            Fader.FadeThrough(() =>
            {
                bool ok = GameManager.I != null && GameManager.I.LoadSlot(slot);
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
            int slot = SaveSlots.Valid(newSlot) && !SaveSlots.Exists(newSlot) ? newSlot : SaveSlots.FirstFree();
            if (slot == 0)
            {
                // all four slots used meanwhile: pick / delete one first
                UIAudio.Nope();
                OpenSlots();
                return;
            }
            starting = true;
            var d = DifficultySettings.IsUnlocked(selected) ? selected : Difficulty.OpenWater;
            Fader.FadeThrough(() =>
            {
                if (GameManager.I != null) GameManager.I.NewGame(d, slot);
            }, 1.6f);
        }

        void OnFriends()
        {
            if (Net.OpenLobby != null)
            {
                try { Net.OpenLobby(); }
                catch (Exception e)
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

        // ------------------------------------------------------------------ test hooks (AutoPlaytest / AutoShot)
        /// Slot cards built on the last slots page.
        public int DebugSlotCardCount => slotCards;
        public bool DebugOnSlotsPage => Visible && page == Page.Slots;
        public bool DebugOnNewGamePage => Visible && page == Page.Difficulty;
        public int DebugNewGameSlot => newSlot;
        public VisualElement DebugRoot => root;

        public void DebugOpenSlots()
        {
            if (!Visible) Show();
            OpenSlots();
            Rebuild();
        }

        public void DebugOpenNewGame(int slot)
        {
            if (!Visible) Show();
            OpenNewGame(slot, true);
            Rebuild();
        }

        /// Opens the delete confirmation of a slot (UIDialog – Cancel / Löschen).
        public void DebugAskDelete(int slot) => AskDelete(slot);
    }
}
