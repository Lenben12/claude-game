using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// „Charakter“ screen (1c): left the live, rotatable 3D preview of your own bean diver on a warm stage, right a light
    /// card with four icon tabs (Anzug · Hut · Gesicht · Maske & Flossen), big swatches and item cards (locked = grey +
    /// lock + one-line hint), the name field and Zufall / Zurücksetzen / Fertig. Every change shows at once on the diver.
    /// Opened through CharacterScreen.Open(ctx) from the first start, the main menu, the co-op lobby, the pause menu and
    /// the hut locker. Keyboard: Tab / Shift+Tab tabs, arrows + Enter pick, A/D rotate, W/S zoom, Esc cancel.
    public class UICharacterScreen
    {
        // round 3 (GDD §19 „mehr Optionen“): + Körper (build, height, skin, hair, hair colour, beard); nose in Gesicht,
        // accessories under the hats
        enum TabId { Suit, Body, Face, Hat, Gear }
        const int TabCount = 5;

        class Entry
        {
            public VisualElement el;
            public Action pick;
            public bool locked;
        }

        class Group
        {
            public readonly List<Entry> entries = new List<Entry>();
        }

        readonly UIController ui;
        readonly VisualElement layer, root, previewPanel, previewImage, card, tabsRow, optionsHost, footerRow, rankHost;
        readonly Label titleLabel, lockLine, emptyLabel;
        ScrollView optionsScroll;
        TextField nameField;
        ICharacterPreview preview;
        CharacterScreenContext context;
        Action onClosed;
        TabId tab;
        DiverLook look, original;
        bool cuffAuto, originalCuffAuto;
        string playerName = "", originalName = "";
        bool dirty, open;
        float anim = 1f, prevTimeScale = 1f;
        bool changedTimeScale;
        int zoomLevel;   // 0 body, 1 head, 2 face
        readonly List<Group> groups = new List<Group>();
        int curGroup, curEntry;
        bool dragging;
        float dragX;
        readonly List<CosmeticDef> tmpDefs = new List<CosmeticDef>();
        static readonly System.Random Rng = new System.Random();

        const float CardW = 0.44f;

        public UICharacterScreen(UIController ui, VisualElement layer)
        {
            this.ui = ui;
            this.layer = layer;
            root = new VisualElement { name = "sb-character" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0f, 0f, 0f, 0.45f);
            root.style.display = DisplayStyle.None;
            layer.Add(root);

            // ---------------- preview (left 56 %)
            previewPanel = new VisualElement { name = "sb-character-preview" };
            previewPanel.style.position = Position.Absolute;
            previewPanel.style.left = 36f; previewPanel.style.top = 36f; previewPanel.style.bottom = 36f;
            previewPanel.style.width = Length.Percent((1f - CardW) * 100f - 4f);
            UIKit.Radius(previewPanel, 28f);
            previewPanel.style.overflow = Overflow.Hidden;
            UIKit.Image(previewPanel, UIKit.VGradient(UIPalette.Rgb(0xE98F6A), UIPalette.Rgb(0xF2C48A)), false);
            root.Add(previewPanel);
            previewImage = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(previewImage);
            previewPanel.Add(previewImage);
            emptyLabel = UIKit.HudText("", 22f);
            emptyLabel.style.position = Position.Absolute;
            emptyLabel.style.top = Length.Percent(45f);
            emptyLabel.style.alignSelf = Align.Center;
            previewPanel.Add(emptyLabel);
            previewPanel.RegisterCallback<PointerDownEvent>(OnPreviewDown);
            previewPanel.RegisterCallback<PointerMoveEvent>(OnPreviewMove);
            previewPanel.RegisterCallback<PointerUpEvent>(OnPreviewUp);
            previewPanel.RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
            previewPanel.RegisterCallback<WheelEvent>(OnWheel);

            rankHost = UIKit.Row();
            UIKit.Abs(rankHost, 26f, 24f, null, null);
            rankHost.pickingMode = PickingMode.Ignore;
            previewPanel.Add(rankHost);

            // bottom-left: rotate buttons + focus chips
            var controls = UIKit.Row();
            UIKit.Abs(controls, 22f, null, 22f, 22f);
            previewPanel.Add(controls);
            controls.Add(RoundButton(UIGlyphs.RotateLeft, () => Rotate(-45f)));
            var rb = RoundButton(UIGlyphs.RotateRight, () => Rotate(45f));
            rb.style.marginLeft = 10f;
            controls.Add(rb);
            controls.Add(UIKit.Flex());
            controls.Add(FocusChip(Loc.Tr("Körper", "Body"), PreviewFocus.Body));
            controls.Add(FocusChip(Loc.Tr("Kopf", "Head"), PreviewFocus.Head));
            controls.Add(FocusChip(Loc.Tr("Gesicht", "Face"), PreviewFocus.Face));
            controls.Add(FocusChip(Loc.Tr("Füße", "Feet"), PreviewFocus.Feet));

            // ---------------- options card (right 44 %)
            card = UIKit.Col();
            card.style.position = Position.Absolute;
            card.style.right = 36f; card.style.top = 36f; card.style.bottom = 36f;
            card.style.width = Length.Percent(CardW * 100f - 2f);
            card.style.backgroundColor = UIPalette.Rgb(0xF4EFE6);
            UIKit.Radius(card, 24f);
            UIKit.Pad(card, 26f, 28f, 22f, 28f);
            UIKit.Lift(card, 1.4f);
            root.Add(card);

            var head = UIKit.Row();
            head.style.flexShrink = 0;
            titleLabel = UIKit.Text("", 36f, UIPalette.Ink, true);
            titleLabel.style.flexShrink = 1;
            head.Add(titleLabel);
            head.Add(UIKit.Flex());
            nameField = UITextInput.Field("", Settings.MaxNameLength, 22f, Loc.Tr("Dein Name", "Your name"));
            nameField.style.width = 280f;
            nameField.RegisterValueChangedCallback(e => { playerName = e.newValue ?? ""; });
            UITextInput.Track(nameField);
            var nameIcon = new UIGlyph(UIGlyphs.Pencil, UIPalette.InkSoft, 26f);
            nameIcon.style.marginRight = 8f;
            head.Add(nameIcon);
            head.Add(nameField);
            card.Add(head);

            tabsRow = UIKit.Row();
            tabsRow.style.marginTop = 18f;
            tabsRow.style.flexShrink = 0;
            card.Add(tabsRow);

            optionsHost = UIKit.Col();
            optionsHost.style.flexGrow = 1;
            optionsHost.style.flexShrink = 1;
            optionsHost.style.marginTop = 10f;
            optionsHost.style.overflow = Overflow.Hidden;
            card.Add(optionsHost);

            lockLine = UIKit.Text("", 17f, UIPalette.CoralInk, true);
            lockLine.style.marginTop = 6f;
            lockLine.style.minHeight = 22f;
            lockLine.style.flexShrink = 0;
            card.Add(lockLine);

            footerRow = UIKit.Row();
            footerRow.style.marginTop = 10f;
            footerRow.style.flexShrink = 0;
            card.Add(footerRow);
        }

        public bool IsOpen => open;

        // ------------------------------------------------------------------ open / close
        public void Open(CharacterScreenContext ctx, Action closed = null)
        {
            if (open) { onClosed = closed ?? onClosed; return; }
            open = true;
            context = ctx;
            onClosed = closed;
            UIState.CharacterOpen = true;
            ui.BringLayerToFront(layer);
            look = Settings.Look.Normalized();
            cuffAuto = Settings.Data.cuffColor.a <= 0f;
            playerName = Settings.Data.playerName ?? "";
            if (ctx == CharacterScreenContext.FirstStart)
            {
                // Q42/1c: the first look is a random suit colour + a random free hat (whatever is on screen is saved)
                var suits = CosmeticsDatabase.SuitColors;
                look.suit = suits[Rng.Next(suits.Length)];
                look.hat = Rng.Next(0, 6);
                look.hair = Rng.Next(0, CosmeticsDatabase.HairCount - 1);
                look.hairColor = Rng.Next(0, 8);
            }
            original = look;
            originalCuffAuto = cuffAuto;
            originalName = playerName;
            nameField.SetValueWithoutNotify(playerName);
            tab = TabId.Suit;
            curGroup = 0; curEntry = 0;
            zoomLevel = 0;
            lockLine.text = "";
            titleLabel.text = ctx == CharacterScreenContext.FirstStart ? Loc.Tr("Wie siehst du aus?", "What do you look like?") : Loc.Tr("Charakter", "Character");

            if ((ctx == CharacterScreenContext.Locker || ctx == CharacterScreenContext.Pause) && !Net.IsOnline && Time.timeScale > 0f)
            {
                prevTimeScale = Time.timeScale;
                Time.timeScale = 0f;
                changedTimeScale = true;
            }
            else changedTimeScale = false;

            CreatePreview();
            root.style.display = DisplayStyle.Flex;
            anim = 0f;
            Rebuild();
            UIAudio.Pop();
        }

        void CreatePreview()
        {
            DisposePreview();
            preview = UICharacterPreviews.Create(UICharacterPreviews.ScreenSize, 0);
            if (preview != null && preview.Texture != null)
            {
                previewImage.style.backgroundImage = Background.FromRenderTexture(preview.Texture);
                previewImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                previewImage.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                previewImage.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
                preview.SetLook(PreviewLook());
                preview.Focus(PreviewFocus.Body);
                emptyLabel.text = "";
            }
            else
            {
                previewImage.style.backgroundImage = StyleKeyword.None;
                emptyLabel.text = Loc.Tr("Vorschau nicht verfügbar", "Preview not available");
            }
        }

        void DisposePreview()
        {
            if (preview == null) return;
            try { preview.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            preview = null;
            previewImage.style.backgroundImage = StyleKeyword.None;
        }

        DiverLook PreviewLook()
        {
            var l = look;
            if (cuffAuto) l.cuff = new Color(0f, 0f, 0f, 0f);
            l.cuff = l.ResolvedCuff;
            l.sitting = false;
            return l;
        }

        bool Changed()
        {
            return cuffAuto != originalCuffAuto || !string.Equals((playerName ?? "").Trim(), (originalName ?? "").Trim(), StringComparison.Ordinal)
                   || !SameLook(look, original);
        }

        static bool SameLook(DiverLook a, DiverLook b) =>
            a.suit == b.suit && a.cuff == b.cuff && a.hat == b.hat && a.skin == b.skin && a.mouth == b.mouth && a.eyes == b.eyes
            && a.brows == b.brows && a.pattern == b.pattern && a.mask == b.mask && a.fins == b.fins && a.snorkel == b.snorkel
            && a.build == b.build && a.height == b.height && a.hair == b.hair && a.hairColor == b.hairColor && a.beard == b.beard
            && a.nose == b.nose && a.accessory == b.accessory;

        /// Esc / close button: asks before throwing changes away.
        public void RequestCancel()
        {
            if (!open) return;
            if (context == CharacterScreenContext.FirstStart) { Save(); return; }
            if (!Changed()) { Close(false); return; }
            UIDialog.Show(UIGlyphs.Person, UIPalette.Coral, Loc.Tr("Änderungen verwerfen?", "Discard changes?"), null,
                Loc.Tr("Weiter anpassen", "Keep editing"), Loc.Tr("Verwerfen", "Discard"), () => Close(false), null, true);
        }

        void Save()
        {
            if (!open) return;
            UITextInput.Blur();
            var l = look;
            Settings.SetLook(l, cuffAuto);
            string n = (playerName ?? "").Trim();
            if (n.Length > Settings.MaxNameLength) n = n.Substring(0, Settings.MaxNameLength);
            Settings.Data.playerName = n;
            Settings.ApplyAndSave();
            // in a session the Net module owns the name (-netname) and broadcasts the new look on LookSaved
            CharacterScreen.RaiseLookSaved();
            Close(true);
        }

        public void Close(bool saved)
        {
            if (!open) return;
            open = false;
            UITextInput.Blur();
            UIDialog.Close();
            DisposePreview();
            root.style.display = DisplayStyle.None;
            UIState.CharacterOpen = false;
            if (changedTimeScale)
            {
                changedTimeScale = false;
                if (!UIState.PauseOpen) Time.timeScale = prevTimeScale > 0f ? prevTimeScale : 1f;
            }
            var cb = onClosed;
            onClosed = null;
            CharacterScreen.RaiseClosed();
            try { cb?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
            _ = saved;
        }

        // ------------------------------------------------------------------ frame
        public void Tick(float dt)
        {
            if (!open) return;
            if (dirty) Rebuild();
            if (anim < 1f)
            {
                anim = Mathf.Min(1f, anim + dt / 0.25f);
                float e = 1f - (1f - anim) * (1f - anim);
                root.style.opacity = e;
                card.style.translate = new Translate(60f * (1f - e), 0f);
            }
            if (preview != null) UICharacterPreviews.Tick(preview, dt);
        }

        /// Keyboard / controller (UIController routes input here while the screen is open).
        public void HandleInput(float dt)
        {
            if (!open || UITextInput.Active) return;
            if (GameInput.PausePressed) { RequestCancel(); return; }
            bool shift = GameInput.KeyHeld(Key.LeftShift) || GameInput.KeyHeld(Key.RightShift);
            if (GameInput.KeyDown(Key.Tab)) { SetTab((TabId)(((int)tab + (shift ? TabCount - 1 : 1)) % TabCount)); return; }
            if (GameInput.KeyDown(Key.LeftArrow)) MoveCursor(0, -1);
            else if (GameInput.KeyDown(Key.RightArrow)) MoveCursor(0, 1);
            else if (GameInput.KeyDown(Key.UpArrow)) MoveCursor(-1, 0);
            else if (GameInput.KeyDown(Key.DownArrow)) MoveCursor(1, 0);
            else if (GameInput.KeyDown(Key.Enter) || GameInput.KeyDown(Key.NumpadEnter)) PickCursor();
            // A/D rotate (120°/s), W/S zoom between the focus presets
            float turn = (GameInput.Held(GameAction.Right) ? 1f : 0f) - (GameInput.Held(GameAction.Left) ? 1f : 0f);
            if (turn != 0f && preview != null) preview.AddYaw(turn * 120f * dt);
            if (GameInput.Down(GameAction.Forward)) Zoom(1);
            else if (GameInput.Down(GameAction.Back)) Zoom(-1);
        }

        void Rotate(float deg) { if (preview != null) preview.AddYaw(deg); }

        void Zoom(int dir)
        {
            int z = Mathf.Clamp(zoomLevel + dir, 0, 2);
            if (z == zoomLevel) return;
            zoomLevel = z;
            if (preview != null) preview.Focus(z == 0 ? PreviewFocus.Body : z == 1 ? PreviewFocus.Head : PreviewFocus.Face);
        }

        void Focus(PreviewFocus f)
        {
            zoomLevel = f == PreviewFocus.Face ? 2 : f == PreviewFocus.Head ? 1 : 0;
            if (preview != null) preview.Focus(f);
        }

        void OnPreviewDown(PointerDownEvent e)
        {
            if (e.button != 0 || e.target != previewPanel) return;   // buttons/chips on the stage keep their clicks
            dragging = true;
            dragX = e.position.x;
            previewPanel.CapturePointer(e.pointerId);
        }

        void OnPreviewMove(PointerMoveEvent e)
        {
            if (!dragging || !previewPanel.HasPointerCapture(e.pointerId)) return;
            float dx = e.position.x - dragX;
            dragX = e.position.x;
            if (float.IsNaN(dx) || float.IsInfinity(dx)) return;
            if (preview != null) preview.AddYaw(dx * 0.35f);
        }

        void OnPreviewUp(PointerUpEvent e)
        {
            if (previewPanel.HasPointerCapture(e.pointerId)) previewPanel.ReleasePointer(e.pointerId);
            dragging = false;
        }

        void OnWheel(WheelEvent e)
        {
            if (e.delta.y < -0.01f) Zoom(1);
            else if (e.delta.y > 0.01f) Zoom(-1);
            e.StopPropagation();
        }

        // ------------------------------------------------------------------ changes
        void Apply(PreviewFocus focus)
        {
            lockLine.text = "";
            if (preview != null)
            {
                preview.SetLook(PreviewLook());
                preview.PlayReaction();
                Focus(focus);
            }
            UIAudio.Pop();
            dirty = true;
        }

        /// Debug/AutoShot: put a hat on the preview (not saved) and switch to the hat tab.
        public void DebugSetHat(int hat)
        {
            if (!open) return;
            look.hat = Mathf.Clamp(hat, 0, CosmeticsDatabase.HatCount - 1);
            tab = TabId.Hat;
            Apply(PreviewFocus.Head);
        }

        void Locked(CosmeticDef d)
        {
            UIAudio.Nope();
            lockLine.text = Loc.Tr("Noch gesperrt: ", "Still locked: ") + (d != null ? d.UnlockHint : "");
        }

        void Randomize()
        {
            var suits = CosmeticsDatabase.SuitColors;
            look.suit = suits[Rng.Next(suits.Length)];
            cuffAuto = Rng.NextDouble() < 0.6;
            if (!cuffAuto) look.cuff = CosmeticsDatabase.CuffColors[1 + Rng.Next(CosmeticsDatabase.CuffColors.Length - 1)];
            look.pattern = RandomUnlocked(CosmeticsDatabase.Pattern, CosmeticsDatabase.PatternCount);
            look.hat = RandomUnlocked(CosmeticsDatabase.Hat, CosmeticsDatabase.HatCount);
            look.skin = Rng.Next(CosmeticsDatabase.SkinTones.Length);
            look.mouth = Rng.Next(CosmeticsDatabase.MouthCount);
            look.eyes = RandomUnlocked(CosmeticsDatabase.Eyes, CosmeticsDatabase.EyesCount);
            look.brows = Rng.Next(CosmeticsDatabase.BrowsCount);
            int mask = RandomUnlocked(CosmeticsDatabase.Mask, CosmeticsDatabase.MaskColors.Length);
            look.mask = mask == 0 ? new Color(0f, 0f, 0f, 0f) : CosmeticsDatabase.MaskColors[mask];
            int fins = RandomUnlocked(CosmeticsDatabase.Fins, CosmeticsDatabase.FinColors.Length);
            look.fins = fins == 0 ? new Color(0f, 0f, 0f, 0f) : CosmeticsDatabase.FinColors[fins];
            look.snorkel = Rng.NextDouble() < 0.7;
            look.face = look.skin * 5 + look.mouth;
            // round 3
            look.build = Rng.Next(CosmeticsDatabase.BuildCount);
            look.height = Rng.Next(5) - 2;
            look.hair = Rng.Next(CosmeticsDatabase.HairCount);
            look.hairColor = Rng.Next(CosmeticsDatabase.HairColors.Length);
            look.beard = Rng.NextDouble() < 0.35 ? 1 + Rng.Next(CosmeticsDatabase.BeardCount - 1) : 0;
            look.nose = Rng.Next(CosmeticsDatabase.NoseCount);
            look.accessory = Rng.NextDouble() < 0.5 ? Rng.Next(CosmeticsDatabase.AccessoryCount) : 0;
            Apply(PreviewFocus.Body);
        }

        int RandomUnlocked(string kind, int count)
        {
            int n = 0;
            for (int i = 0; i < count; i++) if (Cosmetics.IsUnlocked(kind, i)) n++;
            if (n == 0) return 0;
            int pick = Rng.Next(n);
            for (int i = 0; i < count; i++)
            {
                if (!Cosmetics.IsUnlocked(kind, i)) continue;
                if (pick-- == 0) return i;
            }
            return 0;
        }

        void ResetLook()
        {
            look = original;
            cuffAuto = originalCuffAuto;
            playerName = originalName;
            nameField.SetValueWithoutNotify(playerName);
            Apply(PreviewFocus.Body);
        }

        // ------------------------------------------------------------------ building
        void SetTab(TabId t)
        {
            if (t == tab) return;
            tab = t;
            curGroup = 0; curEntry = 0;
            lockLine.text = "";
            UIAudio.Click();
            Focus(t == TabId.Hat ? PreviewFocus.Head : t == TabId.Face ? PreviewFocus.Face : t == TabId.Gear ? PreviewFocus.Feet : PreviewFocus.Body);
            // (Body tab: the whole diver – build / height read best from afar)
            dirty = true;
        }

        void Rebuild()
        {
            dirty = false;
            BuildRank();
            BuildTabs();
            Vector2 scroll = optionsScroll != null ? optionsScroll.scrollOffset : Vector2.zero;
            optionsHost.Clear();
            groups.Clear();
            optionsScroll = UIKit.Scroll();
            optionsScroll.style.flexGrow = 1;
            optionsScroll.style.flexShrink = 1;
            optionsHost.Add(optionsScroll);
            var body = optionsScroll.contentContainer;
            UIKit.Pad(body, 2f, 4f, 12f, 2f);
            switch (tab)
            {
                case TabId.Suit: BuildSuit(body); break;
                case TabId.Body: BuildBody(body); break;
                case TabId.Hat: BuildHat(body); break;
                case TabId.Face: BuildFace(body); break;
                default: BuildGear(body); break;
            }
            if (scroll.y > 0.5f)
            {
                var sv = optionsScroll;
                EventCallback<GeometryChangedEvent> cb = null;
                cb = _ => { sv.scrollOffset = scroll; sv.contentContainer.UnregisterCallback(cb); };
                sv.contentContainer.RegisterCallback(cb);
            }
            ClampCursor();
            ShowCursor();
            BuildFooter();
        }

        void BuildRank()
        {
            rankHost.Clear();
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return;
            int r = Progression.RankIndex(gm.State.followers);
            rankHost.Add(new UIRankBadge(r, 48f));
            var l = UIKit.HudText(Progression.RankName(r), 24f);
            l.style.marginLeft = 10f;
            rankHost.Add(l);
        }

        void BuildTabs()
        {
            tabsRow.Clear();
            AddTab(TabId.Suit, UIGlyphs.Suit, Loc.Tr("Anzug", "Suit"));
            AddTab(TabId.Body, UIGlyphs.Person, Loc.Tr("Körper", "Body"));
            AddTab(TabId.Face, UIGlyphs.Face, Loc.Tr("Gesicht", "Face"));
            AddTab(TabId.Hat, UIGlyphs.Hat, Loc.Tr("Hut & Extras", "Hat & extras"));
            AddTab(TabId.Gear, UIGlyphs.Mask, Loc.Tr("Maske & Flossen", "Mask & fins"));
        }

        void AddTab(TabId t, UIGlyph.DrawFn icon, string label)
        {
            bool sel = t == tab;
            var b = UIKit.Col();
            b.style.alignItems = Align.Center;
            b.style.flexGrow = 1;
            b.style.flexBasis = 0;
            UIKit.Radius(b, 18f);
            UIKit.Pad(b, 10f, 4f, 8f, 4f);
            if (t != TabId.Suit) b.style.marginLeft = 8f;
            Color fg = sel ? Color.white : UIPalette.InkSoft;
            b.Add(new UIGlyph(icon, fg, 40f));
            var l = UIKit.Text(label, 18f, fg, true);
            l.style.marginTop = 4f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.Add(l);
            UIKit.IgnorePicking(b);
            b.pickingMode = PickingMode.Position;
            UIKit.Clickable(b, () => SetTab(t), sel ? UIPalette.Coral : Color.white, sel ? UIPalette.Coral : UIPalette.CardAlt);
            tabsRow.Add(b);
        }

        void BuildFooter()
        {
            footerRow.Clear();
            var rnd = IconBtn(UIGlyphs.Dice, Loc.Tr("Zufall", "Random"), Randomize, UIBtnKind.Secondary);
            footerRow.Add(rnd);
            var rst = IconBtn(UIGlyphs.Undo, Loc.Tr("Zurücksetzen", "Reset"), ResetLook, UIBtnKind.Secondary);
            rst.style.marginLeft = 10f;
            footerRow.Add(rst);
            footerRow.Add(UIKit.Flex());
            if (context != CharacterScreenContext.FirstStart)
            {
                var cancel = UIWidgets.RoundIconButton(UIGlyphs.Cross, RequestCancel, 52f);
                cancel.style.marginRight = 10f;
                footerRow.Add(cancel);
            }
            var done = IconBtn(UIGlyphs.Check, context == CharacterScreenContext.FirstStart ? Loc.Tr("Los geht's!", "Let's go!") : Loc.Tr("Fertig", "Done"), Save, UIBtnKind.Primary);
            done.style.minWidth = 190f;
            footerRow.Add(done);
        }

        static Button IconBtn(UIGlyph.DrawFn icon, string text, Action onClick, UIBtnKind kind)
        {
            var b = UIKit.Btn("", onClick, kind, 20f);
            b.style.flexDirection = FlexDirection.Row;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            UIKit.Pad(b, 12f, 18f, 12f, 14f);
            var skin = b.userData as UIBtnSkin;
            Color fg = skin != null ? skin.fg : Color.white;
            b.Add(new UIGlyph(icon, fg, 28f));
            var l = UIKit.Text(text, 20f, fg, true);
            l.style.marginLeft = 8f;
            l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        static VisualElement RoundButton(UIGlyph.DrawFn icon, Action onClick)
        {
            var b = UIKit.Box();
            UIKit.Size(b, 62f, 62f);
            UIKit.Radius(b, 31f);
            UIKit.Center(b);
            b.Add(new UIGlyph(icon, UIPalette.Ink, 34f));
            UIKit.Clickable(b, onClick, new Color(1f, 1f, 1f, 0.85f), Color.white);
            return b;
        }

        VisualElement FocusChip(string text, PreviewFocus f)
        {
            var l = UIKit.Text(text, 17f, UIPalette.Ink, true);
            UIKit.Pad(l, 9f, 16f, 9f, 16f);
            UIKit.Radius(l, 999f);
            l.style.marginLeft = 8f;
            l.pickingMode = PickingMode.Position;
            UIKit.Clickable(l, () => Focus(f), new Color(1f, 1f, 1f, 0.8f), Color.white);
            return l;
        }

        static VisualElement Section(VisualElement body, UIGlyph.DrawFn icon, string text)
        {
            var row = UIKit.Row();
            row.style.marginTop = 14f;
            row.style.marginBottom = 10f;
            if (icon != null)
            {
                var g = new UIGlyph(icon, UIPalette.InkSoft, 24f);
                g.style.marginRight = 8f;
                row.Add(g);
            }
            row.Add(UIKit.Text(text, 18f, UIPalette.InkSoft, true));
            body.Add(row);
            var wrap = UIKit.Row();
            wrap.style.flexWrap = Wrap.Wrap;
            wrap.style.alignItems = Align.FlexStart;
            body.Add(wrap);
            return wrap;
        }

        Group NewGroup() { var g = new Group(); groups.Add(g); return g; }

        // ---- swatches (64 px circles, selected = 4 px ink ring + check)
        void Swatch(VisualElement wrap, Group g, Color c, bool selected, bool locked, string badge, Action pick, CosmeticDef def)
        {
            var b = UIKit.Box();
            UIKit.Size(b, 64f, 64f);
            UIKit.Radius(b, 32f);
            b.style.marginRight = 14f; b.style.marginBottom = 12f;
            b.style.backgroundColor = c;
            UIKit.Border(b, selected ? 4f : 2f, selected ? UIPalette.Ink : UIPalette.A(UIPalette.Ink, 0.15f));
            UIKit.Center(b);
            if (selected) b.Add(new UIGlyph(UIGlyphs.Check, UIPalette.OnColor(c), 30f));
            else if (!string.IsNullOrEmpty(badge)) b.Add(UIKit.Text(badge, 18f, UIPalette.OnColor(c), true));
            if (locked)
            {
                b.style.opacity = 0.4f;
                b.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.Ink, 26f));
            }
            UIKit.IgnorePicking(b);
            b.pickingMode = PickingMode.Position;
            Action act = locked ? (Action)(() => Locked(def)) : pick;
            b.RegisterCallback<ClickEvent>(_ => act());
            b.RegisterCallback<PointerEnterEvent>(_ => b.style.scale = new Scale(new Vector2(1.08f, 1.08f)));
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.scale = new Scale(Vector2.one));
            UIKit.Transition(b, 0.1f, "scale");
            wrap.Add(b);
            g.entries.Add(new Entry { el = b, pick = act, locked = locked });
        }

        // ---- item cards (132 × 132: icon + name; locked = 40 % alpha + lock + 1-line hint)
        void ItemCard(VisualElement wrap, Group g, UIGlyph.DrawFn icon, string name, bool selected, CosmeticDef def, Action pick)
        {
            bool locked = def != null && !Cosmetics.IsUnlocked(def.id);
            var c = UIKit.Col();
            UIKit.Size(c, 132f, 132f);
            c.style.alignItems = Align.Center;
            c.style.justifyContent = Justify.Center;
            UIKit.Radius(c, 20f);
            c.style.marginRight = 12f; c.style.marginBottom = 12f;
            UIKit.Pad(c, 8f, 6f, 8f, 6f);
            UIKit.Border(c, selected ? 4f : 2f, selected ? UIPalette.Ink : UIPalette.A(UIPalette.Ink, 0.08f));
            c.style.backgroundColor = Color.white;
            var gl = new UIGlyph(icon, Color.white, 70f);
            c.Add(gl);
            var n = UIKit.Text(name, 15f, UIPalette.Ink, true);
            n.style.marginTop = 4f;
            n.style.unityTextAlign = TextAnchor.MiddleCenter;
            n.style.whiteSpace = WhiteSpace.NoWrap;
            n.style.overflow = Overflow.Hidden;
            n.style.textOverflow = TextOverflow.Ellipsis;
            n.style.maxWidth = 118f;
            c.Add(n);
            if (locked)
            {
                gl.style.opacity = 0.4f;
                n.style.opacity = 0.5f;
                var lk = new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 26f);
                lk.style.position = Position.Absolute;
                lk.style.right = 8f; lk.style.top = 8f;
                c.Add(lk);
                var hint = UIKit.Text(def.UnlockHint, 12f, UIPalette.InkSoft);
                hint.style.whiteSpace = WhiteSpace.NoWrap;
                hint.style.overflow = Overflow.Hidden;
                hint.style.textOverflow = TextOverflow.Ellipsis;
                hint.style.maxWidth = 118f;
                hint.style.unityTextAlign = TextAnchor.MiddleCenter;
                c.Add(hint);
            }
            if (selected)
            {
                var ck = UIKit.Box();
                UIKit.Size(ck, 28f, 28f);
                UIKit.Radius(ck, 14f);
                ck.style.backgroundColor = UIPalette.Ink;
                ck.style.position = Position.Absolute;
                ck.style.left = 8f; ck.style.top = 8f;
                UIKit.Center(ck);
                ck.Add(new UIGlyph(UIGlyphs.Check, Color.white, 18f));
                c.Add(ck);
            }
            UIKit.IgnorePicking(c);
            c.pickingMode = PickingMode.Position;
            Action act = locked ? (Action)(() => Locked(def)) : pick;
            UIKit.Clickable(c, act, Color.white, UIPalette.Raised2);
            wrap.Add(c);
            g.entries.Add(new Entry { el = c, pick = act, locked = locked });
        }

        void BuildSuit(VisualElement body)
        {
            var g = NewGroup();
            var w = Section(body, UIGlyphs.Suit, Loc.Tr("Farbe", "Colour"));
            var suits = CosmeticsDatabase.SuitColors;
            for (int i = 0; i < suits.Length; i++)
            {
                var c = suits[i];
                Swatch(w, g, c, Near(look.suit, c), false, null, () => { look.suit = c; Apply(PreviewFocus.Body); }, null);
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Hand, Loc.Tr("Bündchen", "Cuffs"));
            var auto = UIPalette.Lighten(look.suit, 0.55f);
            Swatch(w, g, auto, cuffAuto, false, "A", () => { cuffAuto = true; Apply(PreviewFocus.Body); }, null);
            var cuffs = CosmeticsDatabase.CuffColors;
            for (int i = 1; i < cuffs.Length; i++)
            {
                var c = cuffs[i];
                Swatch(w, g, c, !cuffAuto && Near(look.cuff, c), false, null, () => { cuffAuto = false; look.cuff = c; Apply(PreviewFocus.Body); }, null);
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Sparkle, Loc.Tr("Muster", "Pattern"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Pattern, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                Color suit = look.suit;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Pattern(p, r, idx, suit), d.Name, look.pattern == idx, d,
                    () => { look.pattern = idx; Apply(PreviewFocus.Body); });
            }
        }

        void BuildHat(VisualElement body)
        {
            var g = NewGroup();
            var w = Section(body, UIGlyphs.Hat, Loc.Tr("Hut", "Hat"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Hat, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Hat(p, r, idx), d.Name, look.hat == idx, d,
                    () => { look.hat = idx; Apply(PreviewFocus.Head); });
            }

            // round 3: accessories (necklace, neckerchief, earrings, flower, dive watch, freckles, plaster)
            g = NewGroup();
            w = Section(body, UIGlyphs.Sparkle, Loc.Tr("Extras", "Extras"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Accessory, tmpDefs);
            Color skinA = SkinNow();
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Accessory(p, r, idx, skinA), d.Name, look.accessory == idx, d,
                    () => { look.accessory = idx; Apply(idx == 5 ? PreviewFocus.Body : PreviewFocus.Head); });
            }
        }

        Color SkinNow()
        {
            var skins = CosmeticsDatabase.SkinTones;
            return skins[Mathf.Clamp(look.skin, 0, skins.Length - 1)];
        }

        Color HairNow()
        {
            var hc = CosmeticsDatabase.HairColors;
            return hc[Mathf.Clamp(look.hairColor, 0, hc.Length - 1)];
        }

        /// Round 3 „Körper“: build, height, skin, hair style + colour, beard.
        void BuildBody(VisualElement body)
        {
            Color suit = look.suit;
            var g = NewGroup();
            var w = Section(body, UIGlyphs.Person, Loc.Tr("Statur", "Build"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.BodyBuild, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Build(p, r, idx, suit), d.Name, look.build == idx, d,
                    () => { look.build = idx; Apply(PreviewFocus.Body); });
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Person, Loc.Tr("Größe", "Height"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Height, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int h = d.index - 2;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Height(p, r, h, suit), d.Name, look.height == h, d,
                    () => { look.height = h; Apply(PreviewFocus.Body); });
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Face, Loc.Tr("Haut", "Skin"));
            var skins = CosmeticsDatabase.SkinTones;
            for (int i = 0; i < skins.Length; i++)
            {
                int idx = i;
                Swatch(w, g, skins[i], look.skin == i, false, null, () => { look.skin = idx; look.face = look.skin * 5 + look.mouth; Apply(PreviewFocus.Face); }, null);
            }
            Color skin = SkinNow(), hairC = HairNow();

            g = NewGroup();
            w = Section(body, UIGlyphs.Face, Loc.Tr("Frisur", "Hair"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Hair, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Hair(p, r, idx, skin, hairC), d.Name, look.hair == idx, d,
                    () => { look.hair = idx; Apply(PreviewFocus.Head); });
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Sparkle, Loc.Tr("Haarfarbe", "Hair colour"));
            var hcs = CosmeticsDatabase.HairColors;
            for (int i = 0; i < hcs.Length; i++)
            {
                int idx = i;
                Swatch(w, g, hcs[i], look.hairColor == i, false, null, () => { look.hairColor = idx; Apply(PreviewFocus.Head); }, null);
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Face, Loc.Tr("Bart", "Beard"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Beard, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Beard(p, r, idx, skin, hairC), d.Name, look.beard == idx, d,
                    () => { look.beard = idx; Apply(PreviewFocus.Face); });
            }
        }

        void BuildFace(VisualElement body)
        {
            Color skin = SkinNow();
            var g = NewGroup();
            var w = Section(body, UIGlyphs.Eye, Loc.Tr("Augen", "Eyes"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Eyes, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Face(p, r, skin, idx, look.mouth, look.brows), d.Name, look.eyes == idx, d,
                    () => { look.eyes = idx; Apply(PreviewFocus.Face); });
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Face, Loc.Tr("Nase", "Nose"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Nose, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Nose(p, r, idx, skin), d.Name, look.nose == idx, d,
                    () => { look.nose = idx; Apply(PreviewFocus.Face); });
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Face, Loc.Tr("Mund", "Mouth"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Mouth, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Face(p, r, skin, look.eyes, idx, look.brows), d.Name, look.mouth == idx, d,
                    () => { look.mouth = idx; look.face = look.skin * 5 + look.mouth; Apply(PreviewFocus.Face); });
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Face, Loc.Tr("Brauen", "Brows"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Brows, tmpDefs);
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                ItemCard(w, g, (p, r, col) => UICosmeticIcons.Face(p, r, skin, look.eyes, look.mouth, idx), d.Name, look.brows == idx, d,
                    () => { look.brows = idx; Apply(PreviewFocus.Face); });
            }
        }

        void BuildGear(VisualElement body)
        {
            var g = NewGroup();
            var w = Section(body, UIGlyphs.Mask, Loc.Tr("Maske", "Mask"));
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Mask, tmpDefs);
            Color maskNow = look.ResolvedMask;
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                Color c = CosmeticsDatabase.MaskColors[Mathf.Clamp(idx, 0, CosmeticsDatabase.MaskColors.Length - 1)];
                bool locked = !Cosmetics.IsUnlocked(d.id);
                Swatch(w, g, c, Near(maskNow, c), locked, null,
                    () => { look.mask = idx == 0 ? new Color(0f, 0f, 0f, 0f) : c; Apply(PreviewFocus.Face); }, d);
            }

            g = NewGroup();
            w = Section(body, UIGlyphs.Bubbles, Loc.Tr("Schnorchel", "Snorkel"));
            ItemCard(w, g, (p, r, col) => UICosmeticIcons.Snorkel(p, r, true), Loc.Tr("An", "On"), look.snorkel, null, () => { look.snorkel = true; Apply(PreviewFocus.Head); });
            ItemCard(w, g, (p, r, col) => UICosmeticIcons.Snorkel(p, r, false), Loc.Tr("Aus", "Off"), !look.snorkel, null, () => { look.snorkel = false; Apply(PreviewFocus.Head); });

            g = NewGroup();
            w = Section(body, UIGlyphs.Fins, Loc.Tr("Flossen", "Fins"));
            // (round 3: 8 fin colours, 10 mask frames)
            CosmeticsDatabase.OfKind(CosmeticsDatabase.Fins, tmpDefs);
            Color finsNow = look.ResolvedFins;
            for (int i = 0; i < tmpDefs.Count; i++)
            {
                var d = tmpDefs[i];
                int idx = d.index;
                Color c = CosmeticsDatabase.FinColors[Mathf.Clamp(idx, 0, CosmeticsDatabase.FinColors.Length - 1)];
                bool locked = !Cosmetics.IsUnlocked(d.id);
                Swatch(w, g, c, Near(finsNow, c), locked, null,
                    () => { look.fins = idx == 0 ? new Color(0f, 0f, 0f, 0f) : c; Apply(PreviewFocus.Feet); }, d);
            }
        }

        static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.04f;

        // ------------------------------------------------------------------ keyboard cursor
        void ClampCursor()
        {
            if (groups.Count == 0) { curGroup = 0; curEntry = 0; return; }
            curGroup = Mathf.Clamp(curGroup, 0, groups.Count - 1);
            curEntry = Mathf.Clamp(curEntry, 0, Mathf.Max(0, groups[curGroup].entries.Count - 1));
        }

        void MoveCursor(int dGroup, int dEntry)
        {
            if (groups.Count == 0) return;
            HideCursor();
            curGroup = Mathf.Clamp(curGroup + dGroup, 0, groups.Count - 1);
            curEntry += dEntry;
            ClampCursor();
            ShowCursor();
            var e = Current();
            if (e != null && optionsScroll != null) optionsScroll.ScrollTo(e.el);
            UIAudio.Click();
        }

        Entry Current()
        {
            if (curGroup < 0 || curGroup >= groups.Count) return null;
            var g = groups[curGroup];
            return curEntry >= 0 && curEntry < g.entries.Count ? g.entries[curEntry] : null;
        }

        void ShowCursor()
        {
            var e = Current();
            if (e == null) return;
            e.el.style.translate = new Translate(0f, -3f);
            UIKit.BorderColor(e.el, UIPalette.Coral);
        }

        void HideCursor()
        {
            var e = Current();
            if (e == null) return;
            e.el.style.translate = new Translate(0f, 0f);
            dirty = true;   // restores the normal border colours on the next rebuild
        }

        void PickCursor()
        {
            var e = Current();
            e?.pick?.Invoke();
        }
    }

    /// Small vector previews of the cosmetic options (hats, patterns, faces, snorkel) for the item cards.
    public static class UICosmeticIcons
    {
        static Vector2 P(Rect r, float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
        static float S(Rect r) => Mathf.Min(r.width, r.height);
        static Color H(int hex) => UIPalette.Rgb(hex);

        static void Circle(Painter2D p, Vector2 c, float rad, Color col)
        {
            p.fillColor = col;
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        static void Poly(Painter2D p, Rect r, Color col, params float[] xy)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(P(r, xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(r, xy[i], xy[i + 1]));
            p.ClosePath();
            p.Fill();
        }

        static void Dome(Painter2D p, Rect r, Color col, float x0, float x1, float baseY, float topY)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(P(r, x0, baseY));
            p.BezierCurveTo(P(r, x0, topY), P(r, x1, topY), P(r, x1, baseY));
            p.ClosePath();
            p.Fill();
        }

        static void Brim(Painter2D p, Rect r, Color col, float x0, float x1, float y, float h)
        {
            p.fillColor = col;
            p.BeginPath();
            p.MoveTo(P(r, x0, y));
            p.QuadraticCurveTo(P(r, 0.5f, y - h), P(r, x1, y));
            p.QuadraticCurveTo(P(r, 0.5f, y + h * 0.6f), P(r, x0, y));
            p.ClosePath();
            p.Fill();
        }

        /// Hat options 0–14 (CosmeticsDatabase order).
        public static void Hat(Painter2D p, Rect r, int index)
        {
            float s = S(r);
            switch (index)
            {
                case 0:   // none: a crossed circle
                    p.strokeColor = UIPalette.Silhouette;
                    p.lineWidth = s * 0.07f;
                    p.BeginPath(); p.Arc(r.center, s * 0.32f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(r, 0.28f, 0.72f)); p.LineTo(P(r, 0.72f, 0.28f)); p.Stroke();
                    break;
                case 1: case 6: case 10:   // bucket hats
                {
                    Color c = index == 6 ? H(0xE8C65A) : H(0xC9B48A);
                    Dome(p, r, c, 0.28f, 0.72f, 0.62f, 0.18f);
                    Brim(p, r, UIPalette.Darken(c, 0.12f), 0.08f, 0.92f, 0.68f, 0.12f);
                    if (index == 10)
                    {
                        Circle(p, P(r, 0.22f, 0.86f), s * 0.05f, H(0xF2C94C));
                        Circle(p, P(r, 0.5f, 0.9f), s * 0.05f, H(0xE0607E));
                        Circle(p, P(r, 0.78f, 0.86f), s * 0.05f, H(0x3FB8C4));
                    }
                    break;
                }
                case 2:   // viking helmet
                    Dome(p, r, H(0x9AA0A8), 0.24f, 0.76f, 0.7f, 0.2f);
                    Poly(p, r, H(0xF4EFE6), 0.26f, 0.5f, 0.06f, 0.18f, 0.2f, 0.58f);
                    Poly(p, r, H(0xF4EFE6), 0.74f, 0.5f, 0.94f, 0.18f, 0.8f, 0.58f);
                    Brim(p, r, H(0x6E6F86), 0.2f, 0.8f, 0.72f, 0.05f);
                    break;
                case 3:   // shark fin
                    Poly(p, r, H(0x6D8296), 0.3f, 0.78f, 0.56f, 0.12f, 0.74f, 0.78f);
                    Brim(p, r, H(0x52677A), 0.24f, 0.8f, 0.8f, 0.04f);
                    break;
                case 4:   // rubber duck
                    Circle(p, P(r, 0.44f, 0.62f), s * 0.24f, H(0xF2C94C));
                    Circle(p, P(r, 0.6f, 0.34f), s * 0.16f, H(0xF2C94C));
                    Poly(p, r, H(0xEE7F2D), 0.72f, 0.32f, 0.92f, 0.38f, 0.72f, 0.42f);
                    Circle(p, P(r, 0.63f, 0.3f), s * 0.03f, UIPalette.Ink);
                    break;
                case 5: case 7:   // captain's cap / Gus' original cap
                {
                    Color top = index == 5 ? H(0xF4EFE6) : H(0x4F7FAE);
                    Color band = index == 5 ? H(0x1E2A44) : H(0xC94F3D);
                    Dome(p, r, top, 0.2f, 0.8f, 0.56f, 0.24f);
                    p.fillColor = band;
                    p.BeginPath();
                    p.MoveTo(P(r, 0.22f, 0.54f)); p.LineTo(P(r, 0.78f, 0.54f)); p.LineTo(P(r, 0.76f, 0.68f)); p.LineTo(P(r, 0.24f, 0.68f));
                    p.ClosePath(); p.Fill();
                    Brim(p, r, H(0x1E1B2B), 0.3f, 0.86f, 0.72f, 0.06f);
                    Circle(p, P(r, 0.5f, 0.46f), s * 0.05f, H(0xE8C65A));
                    break;
                }
                case 8:   // party hat
                    Poly(p, r, H(0xF29BBE), 0.28f, 0.82f, 0.5f, 0.1f, 0.72f, 0.82f);
                    Circle(p, P(r, 0.5f, 0.1f), s * 0.07f, H(0xF2C94C));
                    Circle(p, P(r, 0.44f, 0.5f), s * 0.04f, H(0x3FB8C4));
                    Circle(p, P(r, 0.56f, 0.66f), s * 0.04f, H(0xF4EFE6));
                    break;
                case 9:   // pirate tricorn
                    Poly(p, r, H(0x1E1B2B), 0.06f, 0.5f, 0.5f, 0.66f, 0.94f, 0.5f, 0.74f, 0.32f, 0.5f, 0.24f, 0.26f, 0.32f);
                    Circle(p, P(r, 0.5f, 0.44f), s * 0.07f, H(0xF4EFE6));
                    break;
                case 11:   // seagull nest „Günther“
                    Brim(p, r, H(0x8E6C50), 0.12f, 0.88f, 0.7f, 0.18f);
                    Circle(p, P(r, 0.5f, 0.42f), s * 0.18f, H(0xF4EFE6));
                    Poly(p, r, H(0xF2C94C), 0.64f, 0.4f, 0.86f, 0.44f, 0.64f, 0.48f);
                    Circle(p, P(r, 0.54f, 0.38f), s * 0.03f, UIPalette.Ink);
                    break;
                case 12:   // crown
                    Poly(p, r, H(0xE8C65A), 0.14f, 0.74f, 0.14f, 0.3f, 0.32f, 0.5f, 0.5f, 0.2f, 0.68f, 0.5f, 0.86f, 0.3f, 0.86f, 0.74f);
                    Circle(p, P(r, 0.5f, 0.6f), s * 0.06f, H(0xE0607E));
                    break;
                case 13:   // straw hat
                    Brim(p, r, H(0xE2C890), 0.02f, 0.98f, 0.66f, 0.16f);
                    Dome(p, r, H(0xD8B977), 0.3f, 0.7f, 0.62f, 0.26f);
                    p.fillColor = H(0x6E9445);
                    p.BeginPath();
                    p.MoveTo(P(r, 0.3f, 0.56f)); p.LineTo(P(r, 0.7f, 0.56f)); p.LineTo(P(r, 0.7f, 0.62f)); p.LineTo(P(r, 0.3f, 0.62f));
                    p.ClosePath(); p.Fill();
                    break;
                case 15:   // beanie
                    Dome(p, r, H(0xD9603B), 0.2f, 0.8f, 0.7f, 0.12f);
                    p.fillColor = H(0xEDE6DA);
                    p.BeginPath();
                    p.MoveTo(P(r, 0.18f, 0.62f)); p.LineTo(P(r, 0.82f, 0.62f)); p.LineTo(P(r, 0.82f, 0.76f)); p.LineTo(P(r, 0.18f, 0.76f));
                    p.ClosePath(); p.Fill();
                    Circle(p, P(r, 0.5f, 0.18f), s * 0.09f, H(0xEDE6DA));
                    break;
                case 16:   // baseball cap
                    Dome(p, r, H(0xE8A23A), 0.2f, 0.8f, 0.64f, 0.2f);
                    Brim(p, r, H(0x2E3140), 0.42f, 0.98f, 0.66f, 0.06f);
                    Circle(p, P(r, 0.5f, 0.24f), s * 0.035f, H(0x2E3140));
                    break;
                case 17:   // headband
                    p.fillColor = H(0xF4EFE6);
                    p.BeginPath();
                    p.MoveTo(P(r, 0.14f, 0.44f)); p.QuadraticCurveTo(P(r, 0.5f, 0.32f), P(r, 0.86f, 0.44f));
                    p.LineTo(P(r, 0.86f, 0.6f)); p.QuadraticCurveTo(P(r, 0.5f, 0.48f), P(r, 0.14f, 0.6f));
                    p.ClosePath(); p.Fill();
                    p.strokeColor = H(0xE0607E);
                    p.lineWidth = s * 0.04f;
                    p.BeginPath(); p.MoveTo(P(r, 0.16f, 0.52f)); p.QuadraticCurveTo(P(r, 0.5f, 0.4f), P(r, 0.84f, 0.52f)); p.Stroke();
                    break;
                case 18:   // head scarf
                    Dome(p, r, H(0xE0607E), 0.2f, 0.8f, 0.68f, 0.18f);
                    Poly(p, r, H(0xC94F6E), 0.74f, 0.6f, 0.94f, 0.72f, 0.86f, 0.86f);
                    Poly(p, r, H(0xC94F6E), 0.74f, 0.62f, 0.9f, 0.9f, 0.78f, 0.92f);
                    break;
                case 19:   // sun visor
                    p.fillColor = H(0xF2C94C);
                    p.BeginPath();
                    p.MoveTo(P(r, 0.18f, 0.5f)); p.LineTo(P(r, 0.82f, 0.5f)); p.LineTo(P(r, 0.82f, 0.6f)); p.LineTo(P(r, 0.18f, 0.6f));
                    p.ClosePath(); p.Fill();
                    Brim(p, r, UIPalette.A(H(0x3FB8C4), 0.85f), 0.3f, 0.96f, 0.62f, 0.1f);
                    break;
                default:   // 14 paper crown
                    Poly(p, r, H(0xF4EFE6), 0.14f, 0.76f, 0.14f, 0.34f, 0.3f, 0.5f, 0.42f, 0.28f, 0.58f, 0.5f, 0.7f, 0.28f, 0.86f, 0.34f, 0.86f, 0.76f);
                    p.strokeColor = UIPalette.A(UIPalette.Ink, 0.35f);
                    p.lineWidth = s * 0.02f;
                    p.BeginPath(); p.MoveTo(P(r, 0.14f, 0.66f)); p.LineTo(P(r, 0.86f, 0.66f)); p.Stroke();
                    break;
            }
        }

        // ------------------------------------------------------------------ round 3 option icons
        /// Body silhouette in the suit colour: 0 regular, 1 slim, 2 sturdy, 3 broad shoulders.
        public static void Build(Painter2D p, Rect r, int build, Color suit)
        {
            float sh = build == 1 ? 0.15f : build == 2 ? 0.2f : build == 3 ? 0.25f : 0.18f;
            float waist = build == 1 ? 0.1f : build == 2 ? 0.2f : build == 3 ? 0.13f : 0.14f;
            Figure(p, r, 0.5f, 1f, sh, waist, suit);
        }

        /// Height: a figure scaled −2 … +2 next to a little ruler.
        public static void Height(Painter2D p, Rect r, int height, Color suit)
        {
            float k = 0.78f + (height + 2) * 0.06f;
            Figure(p, r, 0.46f, k, 0.17f, 0.13f, suit);
            p.strokeColor = UIPalette.A(UIPalette.Ink, 0.45f);
            p.lineWidth = S(r) * 0.025f;
            p.BeginPath(); p.MoveTo(P(r, 0.84f, 0.92f)); p.LineTo(P(r, 0.84f, 0.92f - 0.84f * k)); p.Stroke();
            for (int i = 0; i < 4; i++)
            {
                float y = 0.92f - 0.84f * k * i / 3f;
                p.BeginPath(); p.MoveTo(P(r, 0.8f, y)); p.LineTo(P(r, 0.88f, y)); p.Stroke();
            }
        }

        /// A little standing diver: egg head, shoulders, torso, legs (feet at 92 % of the box).
        static void Figure(Painter2D p, Rect r, float cx, float scale, float shoulder, float waist, Color suit)
        {
            float H0 = 0.84f * scale, foot = 0.92f;
            float top = foot - H0;
            Color skin = H(0xD6A574), dark = H(0x343849);
            float y(float f) => top + H0 * f;
            // legs
            p.fillColor = dark;
            p.BeginPath();
            p.MoveTo(P(r, cx - waist * 0.9f * scale, y(0.5f))); p.LineTo(P(r, cx - 0.02f, y(0.5f)));
            p.LineTo(P(r, cx - 0.03f, y(1f))); p.LineTo(P(r, cx - waist * 0.8f * scale, y(1f)));
            p.ClosePath(); p.Fill();
            p.BeginPath();
            p.MoveTo(P(r, cx + 0.02f, y(0.5f))); p.LineTo(P(r, cx + waist * 0.9f * scale, y(0.5f)));
            p.LineTo(P(r, cx + waist * 0.8f * scale, y(1f))); p.LineTo(P(r, cx + 0.03f, y(1f)));
            p.ClosePath(); p.Fill();
            // torso: shoulders → waist
            p.fillColor = suit;
            p.BeginPath();
            p.MoveTo(P(r, cx - shoulder * scale, y(0.25f)));
            p.LineTo(P(r, cx + shoulder * scale, y(0.25f)));
            p.LineTo(P(r, cx + waist * scale, y(0.55f)));
            p.LineTo(P(r, cx - waist * scale, y(0.55f)));
            p.ClosePath(); p.Fill();
            // arms
            p.strokeColor = dark;
            p.lineWidth = S(r) * 0.05f * scale;
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(P(r, cx - shoulder * scale, y(0.27f))); p.LineTo(P(r, cx - shoulder * scale - 0.03f, y(0.56f))); p.Stroke();
            p.BeginPath(); p.MoveTo(P(r, cx + shoulder * scale, y(0.27f))); p.LineTo(P(r, cx + shoulder * scale + 0.03f, y(0.56f))); p.Stroke();
            // head (egg) + neck
            Circle(p, P(r, cx, y(0.12f)), S(r) * 0.085f * scale, skin);
            Circle(p, P(r, cx, y(0.07f)), S(r) * 0.075f * scale, skin);
        }

        /// A face (skin) with the hair style drawn on top in the hair colour.
        public static void Hair(Painter2D p, Rect r, int hair, Color skin, Color hairC)
        {
            float s = S(r);
            Vector2 c = P(r, 0.5f, 0.56f);
            float hr = s * 0.3f;
            // behind the head: long hair / ponytail / bun
            if (hair == 5) { p.fillColor = hairC; p.BeginPath(); p.MoveTo(c + new Vector2(-hr * 1.1f, -hr * 0.2f)); p.LineTo(c + new Vector2(hr * 1.1f, -hr * 0.2f)); p.LineTo(c + new Vector2(hr * 1.05f, hr * 1.25f)); p.LineTo(c + new Vector2(-hr * 1.05f, hr * 1.25f)); p.ClosePath(); p.Fill(); }
            if (hair == 3) Circle(p, c + new Vector2(hr * 1.05f, hr * 0.15f), hr * 0.32f, hairC);
            if (hair == 4) Circle(p, c + new Vector2(0f, -hr * 1.15f), hr * 0.38f, hairC);
            // head
            p.fillColor = skin;
            p.BeginPath();
            p.Arc(c, hr, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath(); p.Fill();
            if (hair == CosmeticsDatabase.HairBald)
            {
                Circle(p, c + new Vector2(-hr * 0.35f, -hr * 0.55f), hr * 0.16f, UIPalette.A(Color.white, 0.5f));
            }
            else
            {
                // the cap: the top half of the head
                p.fillColor = hairC;
                p.BeginPath();
                float hairline = hair == 2 || hair == 7 ? 0.0f : -0.12f;
                p.Arc(c, hr * 1.06f, Angle.Degrees(180f), Angle.Degrees(360f));
                p.LineTo(c + new Vector2(hr * 1.06f, hr * hairline));
                p.QuadraticCurveTo(c + new Vector2(0f, -hr * 0.5f), c + new Vector2(-hr * 1.06f, hr * hairline));
                p.ClosePath(); p.Fill();
                switch (hair)
                {
                    case 1:   // spikes
                        for (int i = 0; i < 5; i++)
                        {
                            float a = (200f + i * 35f) * Mathf.Deg2Rad;
                            Vector2 b = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * hr * 0.95f;
                            Vector2 tip = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * hr * 1.45f;
                            Vector2 sideV = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * hr * 0.18f;
                            p.BeginPath(); p.MoveTo(b - sideV); p.LineTo(tip); p.LineTo(b + sideV); p.ClosePath(); p.Fill();
                        }
                        break;
                    case 2:   // curls
                    case 7:   // shaggy
                        for (int i = 0; i < 7; i++)
                        {
                            float a = (180f + i * 30f) * Mathf.Deg2Rad;
                            Circle(p, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * hr * 1.02f, hr * (hair == 2 ? 0.24f : 0.2f), hairC);
                        }
                        break;
                    case 6:   // side part swoop
                        p.BeginPath();
                        p.MoveTo(c + new Vector2(-hr * 0.9f, -hr * 0.4f));
                        p.QuadraticCurveTo(c + new Vector2(-hr * 0.1f, -hr * 0.05f), c + new Vector2(hr * 0.8f, -hr * 0.45f));
                        p.LineTo(c + new Vector2(hr * 0.3f, -hr * 0.9f));
                        p.ClosePath(); p.Fill();
                        break;
                    case 5:   // long: side locks
                        p.BeginPath(); p.MoveTo(c + new Vector2(-hr * 1.06f, -hr * 0.1f)); p.LineTo(c + new Vector2(-hr * 0.8f, -hr * 0.1f)); p.LineTo(c + new Vector2(-hr * 0.85f, hr * 0.9f)); p.LineTo(c + new Vector2(-hr * 1.05f, hr * 0.9f)); p.ClosePath(); p.Fill();
                        p.BeginPath(); p.MoveTo(c + new Vector2(hr * 1.06f, -hr * 0.1f)); p.LineTo(c + new Vector2(hr * 0.8f, -hr * 0.1f)); p.LineTo(c + new Vector2(hr * 0.85f, hr * 0.9f)); p.LineTo(c + new Vector2(hr * 1.05f, hr * 0.9f)); p.ClosePath(); p.Fill();
                        break;
                }
            }
            FaceDots(p, c, hr);
        }

        static void FaceDots(Painter2D p, Vector2 c, float hr)
        {
            Color ink = H(0x1E1B2B);
            for (int s = -1; s <= 1; s += 2)
            {
                Circle(p, c + new Vector2(s * hr * 0.35f, hr * 0.12f), hr * 0.17f, Color.white);
                Circle(p, c + new Vector2(s * hr * 0.35f, hr * 0.15f), hr * 0.08f, ink);
            }
            p.strokeColor = ink;
            p.lineWidth = hr * 0.08f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(c + new Vector2(0f, hr * 0.42f), hr * 0.22f, Angle.Degrees(25f), Angle.Degrees(155f));
            p.Stroke();
        }

        /// Face with a beard: 0 none, 1 moustache, 2 goatee, 3 full, 4 captain's (chin curtain), 5 stubble.
        public static void Beard(Painter2D p, Rect r, int beard, Color skin, Color hairC)
        {
            float s = S(r);
            Vector2 c = P(r, 0.5f, 0.5f);
            float hr = s * 0.34f;
            Circle(p, c, hr, skin);
            Color bc = beard == 5 ? Color.Lerp(skin, hairC, 0.45f) : hairC;
            if (beard == 3 || beard == 4 || beard == 5)
            {
                p.fillColor = bc;
                p.BeginPath();
                float top = beard == 4 ? 0.48f : 0.12f;
                p.MoveTo(c + new Vector2(-hr * 0.98f, hr * 0.05f));
                p.QuadraticCurveTo(c + new Vector2(-hr * 0.9f, hr * 1.25f), c + new Vector2(0f, hr * 1.22f));
                p.QuadraticCurveTo(c + new Vector2(hr * 0.9f, hr * 1.25f), c + new Vector2(hr * 0.98f, hr * 0.05f));
                p.LineTo(c + new Vector2(hr * 0.7f, hr * 0.1f));
                p.QuadraticCurveTo(c + new Vector2(0f, hr * (top + 0.4f)), c + new Vector2(-hr * 0.7f, hr * 0.1f));
                p.ClosePath(); p.Fill();
            }
            if (beard == 2) Circle(p, c + new Vector2(0f, hr * 0.78f), hr * 0.2f, bc);
            if (beard == 1 || beard == 2 || beard == 3)
            {
                p.fillColor = bc;
                p.BeginPath();
                p.MoveTo(c + new Vector2(-hr * 0.42f, hr * 0.42f));
                p.QuadraticCurveTo(c + new Vector2(0f, hr * 0.22f), c + new Vector2(hr * 0.42f, hr * 0.42f));
                p.QuadraticCurveTo(c + new Vector2(0f, hr * 0.36f), c + new Vector2(-hr * 0.42f, hr * 0.42f));
                p.ClosePath(); p.Fill();
            }
            Color ink = H(0x1E1B2B);
            for (int i = -1; i <= 1; i += 2)
            {
                Circle(p, c + new Vector2(i * hr * 0.35f, -hr * 0.2f), hr * 0.17f, Color.white);
                Circle(p, c + new Vector2(i * hr * 0.35f, -hr * 0.17f), hr * 0.08f, ink);
            }
            p.strokeColor = ink;
            p.lineWidth = hr * 0.07f;
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(c + new Vector2(-hr * 0.2f, hr * 0.58f)); p.LineTo(c + new Vector2(hr * 0.2f, hr * 0.58f)); p.Stroke();
        }

        /// Face in profile-ish front view with the nose shape: 0 button, 1 round, 2 long, 3 pointy, 4 snub.
        public static void Nose(Painter2D p, Rect r, int nose, Color skin)
        {
            float s = S(r);
            Vector2 c = P(r, 0.5f, 0.5f);
            float hr = s * 0.36f;
            Circle(p, c, hr, skin);
            Color nc = UIPalette.Darken(skin, 0.12f);
            Vector2 n = c + new Vector2(0f, hr * 0.12f);
            switch (nose)
            {
                case 1: Circle(p, n + new Vector2(0f, hr * 0.06f), hr * 0.26f, nc); break;
                case 2:
                    p.fillColor = nc;
                    p.BeginPath(); p.MoveTo(n + new Vector2(-hr * 0.08f, -hr * 0.3f)); p.LineTo(n + new Vector2(hr * 0.08f, -hr * 0.3f)); p.LineTo(n + new Vector2(hr * 0.12f, hr * 0.35f)); p.LineTo(n + new Vector2(-hr * 0.12f, hr * 0.35f)); p.ClosePath(); p.Fill();
                    Circle(p, n + new Vector2(0f, hr * 0.35f), hr * 0.13f, nc);
                    break;
                case 3:
                    p.fillColor = nc;
                    p.BeginPath(); p.MoveTo(n + new Vector2(0f, -hr * 0.25f)); p.LineTo(n + new Vector2(hr * 0.16f, hr * 0.2f)); p.LineTo(n + new Vector2(-hr * 0.16f, hr * 0.2f)); p.ClosePath(); p.Fill();
                    break;
                case 4:
                    Circle(p, n + new Vector2(0f, hr * 0.02f), hr * 0.15f, nc);
                    Circle(p, n + new Vector2(-hr * 0.06f, hr * 0.07f), hr * 0.04f, UIPalette.Darken(skin, 0.4f));
                    Circle(p, n + new Vector2(hr * 0.06f, hr * 0.07f), hr * 0.04f, UIPalette.Darken(skin, 0.4f));
                    break;
                default: Circle(p, n, hr * 0.15f, nc); break;
            }
            Color ink = H(0x1E1B2B);
            for (int i = -1; i <= 1; i += 2)
            {
                Circle(p, c + new Vector2(i * hr * 0.36f, -hr * 0.22f), hr * 0.17f, Color.white);
                Circle(p, c + new Vector2(i * hr * 0.36f, -hr * 0.19f), hr * 0.08f, ink);
            }
            p.strokeColor = ink;
            p.lineWidth = hr * 0.07f;
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.Arc(c + new Vector2(0f, hr * 0.48f), hr * 0.2f, Angle.Degrees(25f), Angle.Degrees(155f)); p.Stroke();
        }

        /// Accessories: 0 none, 1 shell necklace, 2 neckerchief, 3 earrings, 4 flower, 5 dive watch, 6 freckles, 7 plaster.
        public static void Accessory(Painter2D p, Rect r, int acc, Color skin)
        {
            float s = S(r);
            switch (acc)
            {
                case 0:
                    p.strokeColor = UIPalette.Silhouette;
                    p.lineWidth = s * 0.07f;
                    p.BeginPath(); p.Arc(r.center, s * 0.32f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(P(r, 0.28f, 0.72f)); p.LineTo(P(r, 0.72f, 0.28f)); p.Stroke();
                    break;
                case 1:
                    p.strokeColor = H(0x8E6C50);
                    p.lineWidth = s * 0.03f;
                    p.BeginPath(); p.Arc(P(r, 0.5f, 0.3f), s * 0.32f, Angle.Degrees(20f), Angle.Degrees(160f)); p.Stroke();
                    Circle(p, P(r, 0.32f, 0.52f), s * 0.07f, H(0xF4EFE6));
                    Circle(p, P(r, 0.5f, 0.6f), s * 0.08f, H(0xF29BBE));
                    Circle(p, P(r, 0.68f, 0.52f), s * 0.07f, H(0xF2C94C));
                    break;
                case 2:
                    Poly(p, r, H(0xC94F3D), 0.16f, 0.32f, 0.84f, 0.32f, 0.5f, 0.82f);
                    Circle(p, P(r, 0.5f, 0.32f), s * 0.08f, H(0xA83E30));
                    break;
                case 3:
                    for (int i = -1; i <= 1; i += 2)
                    {
                        p.strokeColor = H(0xE8C65A);
                        p.lineWidth = s * 0.05f;
                        p.BeginPath(); p.Arc(P(r, 0.5f + i * 0.2f, 0.56f), s * 0.12f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
                    }
                    break;
                case 4:
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad;
                        Circle(p, P(r, 0.5f, 0.5f) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.16f, s * 0.14f, H(0xE86FA0));
                    }
                    Circle(p, P(r, 0.5f, 0.5f), s * 0.08f, H(0xF2C94C));
                    break;
                case 5:
                    p.fillColor = H(0x2E3140);
                    p.BeginPath(); p.MoveTo(P(r, 0.38f, 0.1f)); p.LineTo(P(r, 0.62f, 0.1f)); p.LineTo(P(r, 0.62f, 0.9f)); p.LineTo(P(r, 0.38f, 0.9f)); p.ClosePath(); p.Fill();
                    Circle(p, r.center, s * 0.22f, H(0xF2C94C));
                    Circle(p, r.center, s * 0.16f, H(0x1E1B2B));
                    p.strokeColor = Color.white;
                    p.lineWidth = s * 0.03f;
                    p.BeginPath(); p.MoveTo(r.center); p.LineTo(r.center + new Vector2(0f, -s * 0.12f)); p.Stroke();
                    p.BeginPath(); p.MoveTo(r.center); p.LineTo(r.center + new Vector2(s * 0.08f, 0f)); p.Stroke();
                    break;
                case 6:
                    Circle(p, r.center, s * 0.36f, skin);
                    for (int i = 0; i < 4; i++)
                        for (int k = -1; k <= 1; k += 2)
                            Circle(p, P(r, 0.5f + k * (0.17f + (i % 2) * 0.06f), 0.52f + (i / 2) * 0.07f), s * 0.025f, UIPalette.Darken(skin, 0.35f));
                    break;
                default:
                    Circle(p, r.center, s * 0.36f, skin);
                    p.fillColor = H(0xE9C9A0);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        Vector2 c = r.center;
                        float a = k * 35f * Mathf.Deg2Rad;
                        Vector2 dx = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.22f, dy = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * s * 0.07f;
                        p.BeginPath(); p.MoveTo(c - dx - dy); p.LineTo(c + dx - dy); p.LineTo(c + dx + dy); p.LineTo(c - dx + dy); p.ClosePath(); p.Fill();
                    }
                    break;
            }
        }

        /// Suit panel patterns 0–7 in the current suit colour.
        public static void Pattern(Painter2D p, Rect r, int index, Color suit)
        {
            float s = S(r);
            var box = new Rect(r.x + s * 0.12f, r.y + s * 0.12f, s * 0.76f, s * 0.76f);
            Color baseC = index == 6 ? H(0xE8C65A) : suit;
            Color accent = UIPalette.OnColor(baseC) == UIPalette.Ink ? UIPalette.Darken(baseC, 0.35f) : UIPalette.Lighten(baseC, 0.55f);
            p.fillColor = baseC;
            p.BeginPath();
            p.MoveTo(new Vector2(box.xMin + 10f, box.yMin));
            p.ArcTo(new Vector2(box.xMax, box.yMin), new Vector2(box.xMax, box.yMax), 10f);
            p.ArcTo(new Vector2(box.xMax, box.yMax), new Vector2(box.xMin, box.yMax), 10f);
            p.ArcTo(new Vector2(box.xMin, box.yMax), new Vector2(box.xMin, box.yMin), 10f);
            p.ArcTo(new Vector2(box.xMin, box.yMin), new Vector2(box.xMax, box.yMin), 10f);
            p.ClosePath();
            p.Fill();
            p.strokeColor = accent;
            p.fillColor = accent;
            p.lineCap = LineCap.Round;
            switch (index)
            {
                case 1:   // stripes
                    p.lineWidth = s * 0.07f;
                    for (int i = 0; i < 3; i++)
                    {
                        float y = box.yMin + box.height * (0.25f + i * 0.25f);
                        p.BeginPath(); p.MoveTo(new Vector2(box.xMin + 6f, y)); p.LineTo(new Vector2(box.xMax - 6f, y)); p.Stroke();
                    }
                    break;
                case 2:   // dots
                    for (int y = 0; y < 3; y++)
                        for (int x = 0; x < 3; x++)
                            Circle(p, new Vector2(box.xMin + box.width * (0.22f + x * 0.28f), box.yMin + box.height * (0.22f + y * 0.28f)), s * 0.05f, accent);
                    break;
                case 3:   // shark teeth
                    p.BeginPath();
                    p.MoveTo(new Vector2(box.xMin, box.yMin + box.height * 0.42f));
                    for (int i = 0; i < 5; i++)
                    {
                        p.LineTo(new Vector2(box.xMin + box.width * (i + 0.5f) / 5f, box.yMin + box.height * 0.66f));
                        p.LineTo(new Vector2(box.xMin + box.width * (i + 1f) / 5f, box.yMin + box.height * 0.42f));
                    }
                    p.LineTo(new Vector2(box.xMax, box.yMin + box.height * 0.36f));
                    p.LineTo(new Vector2(box.xMin, box.yMin + box.height * 0.36f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case 4:   // herringbone
                    p.lineWidth = s * 0.04f;
                    for (int row = 0; row < 3; row++)
                    {
                        float y = box.yMin + box.height * (0.24f + row * 0.26f);
                        p.BeginPath();
                        for (int i = 0; i <= 6; i++)
                        {
                            var v = new Vector2(box.xMin + 6f + (box.width - 12f) * i / 6f, y + ((i & 1) == 0 ? -s * 0.05f : s * 0.05f));
                            if (i == 0) p.MoveTo(v); else p.LineTo(v);
                        }
                        p.Stroke();
                    }
                    break;
                case 5:   // waves
                    p.lineWidth = s * 0.05f;
                    for (int row = 0; row < 3; row++)
                    {
                        float y = box.yMin + box.height * (0.26f + row * 0.24f);
                        p.BeginPath();
                        p.MoveTo(new Vector2(box.xMin + 6f, y));
                        for (int i = 0; i < 3; i++)
                        {
                            float x0 = box.xMin + 6f + (box.width - 12f) * i / 3f;
                            float x1 = box.xMin + 6f + (box.width - 12f) * (i + 1) / 3f;
                            p.QuadraticCurveTo(new Vector2((x0 + x1) * 0.5f, y - s * 0.08f), new Vector2(x1, y));
                        }
                        p.Stroke();
                    }
                    break;
                case 6:   // gold: sparkle
                    UIGlyphs.Sparkle(p, new Rect(box.x + box.width * 0.25f, box.y + box.height * 0.25f, box.width * 0.5f, box.height * 0.5f), Color.white);
                    break;
                case 7:   // lighthouse: red/white bands
                    p.fillColor = H(0xC94F3D);
                    for (int i = 0; i < 2; i++)
                    {
                        float y0 = box.yMin + box.height * (0.18f + i * 0.4f);
                        p.BeginPath();
                        p.MoveTo(new Vector2(box.xMin, y0)); p.LineTo(new Vector2(box.xMax, y0));
                        p.LineTo(new Vector2(box.xMax, y0 + box.height * 0.2f)); p.LineTo(new Vector2(box.xMin, y0 + box.height * 0.2f));
                        p.ClosePath(); p.Fill();
                    }
                    break;
                case 8:   // camo blobs
                {
                    Color dark = UIPalette.Darken(baseC, 0.4f);
                    Circle(p, new Vector2(box.xMin + box.width * 0.3f, box.yMin + box.height * 0.3f), s * 0.13f, dark);
                    Circle(p, new Vector2(box.xMin + box.width * 0.42f, box.yMin + box.height * 0.36f), s * 0.1f, dark);
                    Circle(p, new Vector2(box.xMin + box.width * 0.7f, box.yMin + box.height * 0.62f), s * 0.14f, dark);
                    Circle(p, new Vector2(box.xMin + box.width * 0.28f, box.yMin + box.height * 0.76f), s * 0.09f, dark);
                    break;
                }
                case 9:   // zigzag band
                    p.lineWidth = s * 0.07f;
                    p.BeginPath();
                    for (int i = 0; i <= 6; i++)
                    {
                        var v = new Vector2(box.xMin + 6f + (box.width - 12f) * i / 6f, box.yMin + box.height * ((i & 1) == 0 ? 0.42f : 0.62f));
                        if (i == 0) p.MoveTo(v); else p.LineTo(v);
                    }
                    p.Stroke();
                    break;
                case 10:  // stars
                    for (int y = 0; y < 2; y++)
                        for (int x = 0; x < 2; x++)
                            UIGlyphs.Star(p, new Rect(box.xMin + box.width * (0.1f + x * 0.45f), box.yMin + box.height * (0.1f + y * 0.45f), box.width * 0.35f, box.height * 0.35f), accent);
                    break;
                case 11:  // two-tone
                    p.BeginPath();
                    p.MoveTo(new Vector2(box.xMin, box.yMin + 10f));
                    p.LineTo(new Vector2(box.xMax, box.yMin + 10f));
                    p.LineTo(new Vector2(box.xMax, box.yMin + box.height * 0.45f));
                    p.LineTo(new Vector2(box.xMin, box.yMin + box.height * 0.45f));
                    p.ClosePath(); p.Fill();
                    break;
            }
        }

        /// Bean face with eyes 0–4, mouth 0–4 and brows 0–3.
        public static void Face(Painter2D p, Rect r, Color skin, int eyes, int mouth, int brows)
        {
            float s = S(r);
            Circle(p, r.center, s * 0.44f, skin);
            Color ink = H(0x1E1B2B);
            // eyes
            for (int side = -1; side <= 1; side += 2)
            {
                Vector2 c = P(r, 0.5f + side * 0.16f, 0.42f);
                float er = s * 0.11f;
                switch (eyes)
                {
                    case 1:   // sleepy: half lids
                        Circle(p, c, er, Color.white);
                        Circle(p, c + new Vector2(0f, s * 0.02f), er * 0.45f, ink);
                        p.fillColor = UIPalette.Darken(skin, 0.08f);
                        p.BeginPath();
                        p.Arc(c, er * 1.05f, Angle.Degrees(180f), Angle.Degrees(360f));
                        p.ClosePath();
                        p.Fill();
                        break;
                    case 2:   // cross-eyed
                        Circle(p, c, er, Color.white);
                        Circle(p, c + new Vector2(-side * er * 0.45f, 0f), er * 0.45f, ink);
                        break;
                    case 3:   // stars
                        UIGlyphs.Star(p, new Rect(c.x - er * 1.2f, c.y - er * 1.2f, er * 2.4f, er * 2.4f), H(0xF2C94C));
                        break;
                    case 4:   // hearts
                        p.fillColor = H(0xE0607E);
                        p.BeginPath();
                        p.MoveTo(c + new Vector2(0f, er * 0.9f));
                        p.BezierCurveTo(c + new Vector2(-er * 1.4f, -er * 0.2f), c + new Vector2(-er * 0.6f, -er * 1.2f), c + new Vector2(0f, -er * 0.4f));
                        p.BezierCurveTo(c + new Vector2(er * 0.6f, -er * 1.2f), c + new Vector2(er * 1.4f, -er * 0.2f), c + new Vector2(0f, er * 0.9f));
                        p.ClosePath();
                        p.Fill();
                        break;
                    case 5:   // lashes
                        Circle(p, c, er, Color.white);
                        Circle(p, c + new Vector2(0f, er * 0.1f), er * 0.5f, ink);
                        p.strokeColor = ink;
                        p.lineWidth = s * 0.02f;
                        for (int k = -1; k <= 1; k++)
                        {
                            Vector2 a = c + new Vector2(k * er * 0.55f, -er * 0.85f);
                            p.BeginPath(); p.MoveTo(a); p.LineTo(a + new Vector2(k * er * 0.25f + side * er * 0.1f, -er * 0.45f)); p.Stroke();
                        }
                        break;
                    default:   // googly
                        Circle(p, c, er, Color.white);
                        Circle(p, c + new Vector2(side * er * 0.15f, er * 0.1f), er * 0.5f, ink);
                        break;
                }
                // brows (round 3: 0 natural … 4 thin, 5 none)
                if (brows != 5)
                {
                    p.strokeColor = UIPalette.Darken(skin, 0.6f);
                    p.lineWidth = brows == 4 ? s * 0.022f : s * 0.04f;
                    p.lineCap = LineCap.Round;
                    p.BeginPath();
                    float y = c.y - er * 1.5f;
                    switch (brows)
                    {
                        case 0: case 4:   // natural / thin: a soft arch
                            p.MoveTo(new Vector2(c.x - er, y + er * 0.1f));
                            p.QuadraticCurveTo(new Vector2(c.x, y - er * 0.35f), new Vector2(c.x + er, y + er * 0.1f));
                            break;
                        case 2:   // grumpy: slanted inward
                            p.MoveTo(new Vector2(c.x - side * er, y - er * 0.3f));
                            p.LineTo(new Vector2(c.x + side * er * 0.7f, y + er * 0.3f));
                            break;
                        case 3:   // surprised: high arcs
                            p.MoveTo(new Vector2(c.x - er, y - er * 0.1f));
                            p.QuadraticCurveTo(new Vector2(c.x, y - er * 0.9f), new Vector2(c.x + er, y - er * 0.1f));
                            break;
                        default:   // bushy
                            p.lineWidth = s * 0.07f;
                            p.MoveTo(new Vector2(c.x - er, y));
                            p.LineTo(new Vector2(c.x + er, y));
                            break;
                    }
                    p.Stroke();
                }
            }
            // mouth
            Vector2 m = P(r, 0.5f, 0.66f);
            p.strokeColor = ink;
            p.fillColor = ink;
            p.lineWidth = s * 0.045f;
            p.lineCap = LineCap.Round;
            switch (mouth)
            {
                case 1:   // open
                    p.BeginPath();
                    p.MoveTo(m + new Vector2(-s * 0.12f, -s * 0.02f));
                    p.QuadraticCurveTo(m + new Vector2(0f, s * 0.2f), m + new Vector2(s * 0.12f, -s * 0.02f));
                    p.ClosePath();
                    p.Fill();
                    break;
                case 2:   // "oh"
                    Circle(p, m + new Vector2(0f, s * 0.02f), s * 0.06f, ink);
                    break;
                case 3:   // flat
                    p.BeginPath(); p.MoveTo(m + new Vector2(-s * 0.1f, 0f)); p.LineTo(m + new Vector2(s * 0.1f, 0f)); p.Stroke();
                    break;
                case 4:   // grin
                    p.BeginPath();
                    p.MoveTo(m + new Vector2(-s * 0.16f, -s * 0.04f));
                    p.QuadraticCurveTo(m + new Vector2(0f, s * 0.18f), m + new Vector2(s * 0.16f, -s * 0.04f));
                    p.ClosePath();
                    p.Fill();
                    p.fillColor = Color.white;
                    p.BeginPath();
                    p.MoveTo(m + new Vector2(-s * 0.13f, -s * 0.03f));
                    p.LineTo(m + new Vector2(s * 0.13f, -s * 0.03f));
                    p.LineTo(m + new Vector2(s * 0.11f, s * 0.02f));
                    p.LineTo(m + new Vector2(-s * 0.11f, s * 0.02f));
                    p.ClosePath();
                    p.Fill();
                    break;
                default:   // smile
                    p.BeginPath();
                    p.Arc(m + new Vector2(0f, -s * 0.06f), s * 0.13f, Angle.Degrees(25f), Angle.Degrees(155f));
                    p.Stroke();
                    break;
            }
        }

        /// Mask with / without snorkel.
        public static void Snorkel(Painter2D p, Rect r, bool on)
        {
            float s = S(r);
            Color frame = H(0x1E1B2B);
            p.fillColor = frame;
            p.BeginPath();
            p.MoveTo(P(r, 0.16f, 0.4f));
            p.LineTo(P(r, 0.74f, 0.4f));
            p.LineTo(P(r, 0.74f, 0.66f));
            p.LineTo(P(r, 0.16f, 0.66f));
            p.ClosePath();
            p.Fill();
            p.fillColor = H(0x9FE4EC);
            p.BeginPath();
            p.MoveTo(P(r, 0.21f, 0.45f)); p.LineTo(P(r, 0.69f, 0.45f)); p.LineTo(P(r, 0.69f, 0.61f)); p.LineTo(P(r, 0.21f, 0.61f));
            p.ClosePath(); p.Fill();
            if (on)
            {
                p.strokeColor = H(0xEE7F2D);
                p.lineWidth = s * 0.07f;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.MoveTo(P(r, 0.8f, 0.7f));
                p.LineTo(P(r, 0.84f, 0.2f));
                p.Stroke();
            }
            else
            {
                p.strokeColor = UIPalette.Silhouette;
                p.lineWidth = s * 0.05f;
                p.BeginPath(); p.MoveTo(P(r, 0.76f, 0.22f)); p.LineTo(P(r, 0.92f, 0.38f)); p.Stroke();
                p.BeginPath(); p.MoveTo(P(r, 0.92f, 0.22f)); p.LineTo(P(r, 0.76f, 0.38f)); p.Stroke();
            }
        }
    }
}
