using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 „Einrichten“ (GDD §19): compact furnishing screen opened at the catalogue on the hut desk
    /// (PropHutCatalog → HutDecor.OpenUI). Tabs Wände / Boden / Teppich / Vorhänge / Deko as swatches and item cards;
    /// a click applies the choice live through HutDecor (host-routed in co-op). Blocks gameplay (UIState.DecorOpen),
    /// Esc / E / ✕ close it, mouse only.
    public static class UIHutDecor
    {
        enum Tab { Walls, Floor, Rug, Curtains, Decor }

        static VisualElement root, card, tabRow, body;
        static Label footer, moneyLabel;
        static Tab tab;
        static bool dirty;
        static float anim = 1f, openedAt, pollTimer;
        static int lastSig;

        public static bool IsOpen => UIState.DecorOpen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            root = null; card = null; tabRow = null; body = null; footer = null; moneyLabel = null;
            tab = Tab.Walls; dirty = false; anim = 1f; openedAt = 0f; pollTimer = 0f; lastSig = 0;
        }

        /// UIController: builds the (hidden) screen in the given layer and registers the open hook.
        public static void Init(VisualElement layer)
        {
            root = new VisualElement { name = "sb-hut-decor" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0.08f, 0.1f, 0.18f, 0.3f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            layer.Add(root);
            root.RegisterCallback<ClickEvent>(e => { if (e.target == root) Close(); });
            card = UIWidgets.ModalCard(940f);
            UIKit.Radius(card, 24f);
            root.Add(card);
            HutDecor.OpenHandler = Open;
        }

        public static void Open()
        {
            if (root == null || UIState.DecorOpen) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.LoadingOpen || UIState.PauseOpen || UIState.PhoneOpen
                || UIState.InventoryOpen || UIState.CharacterOpen) return;
            UIState.DecorOpen = true;
            root.style.display = DisplayStyle.Flex;
            anim = 0f;
            openedAt = Time.unscaledTime;
            Rebuild();
            UIAudio.Pop();
        }

        public static void Close()
        {
            if (root == null) { UIState.DecorOpen = false; return; }
            if (!UIState.DecorOpen && root.style.display.value == DisplayStyle.None) return;
            UIState.DecorOpen = false;
            root.style.display = DisplayStyle.None;
        }

        /// Test hook: opens a tab directly.
        public static void DebugOpen(int tabIndex)
        {
            tab = (Tab)Mathf.Clamp(tabIndex, 0, 4);
            if (!IsOpen) Open(); else dirty = true;
        }

        public static void Tick(float dt)
        {
            if (!UIState.DecorOpen || root == null) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.PauseOpen || UIState.LoadingOpen) { Close(); return; }
            if (anim < 1f)
            {
                anim = Mathf.Min(1f, anim + dt / 0.18f);
                float e = 1f - (1f - anim) * (1f - anim);
                card.style.opacity = e;
                card.style.translate = new Translate(0f, 24f * (1f - e));
            }
            pollTimer -= dt;
            if (pollTimer <= 0f)
            {
                pollTimer = 0.25f;
                int sig = Signature(gm.State);
                if (sig != lastSig) dirty = true;
            }
            if (dirty) Rebuild();
        }

        /// UIController input while open: Esc or E closes (E after a short moment – the E that opened it must not).
        /// Returns true while the screen consumes the input (no phone / backpack / pause behind it).
        public static bool HandleInput()
        {
            if (!UIState.DecorOpen) return false;
            if (GameInput.PausePressed || (Time.unscaledTime - openedAt > 0.35f && GameInput.InteractPressed)) Close();
            return true;
        }

        static int Signature(GameState s)
        {
            unchecked
            {
                int h = HutDecor.Signature(s);
                if (s == null) return h;
                h = h * 31 + s.money.GetHashCode();
                h = h * 31 + (s.hut != null && s.hut.owned != null ? s.hut.owned.Count : 0);
                h = h * 31 + (s.collected != null ? s.collected.Count : 0);
                h = h * 31 + (s.achievements != null ? s.achievements.Count : 0);
                return h;
            }
        }

        // ------------------------------------------------------------------ building
        static void Rebuild()
        {
            dirty = false;
            var gm = GameManager.I;
            lastSig = Signature(gm != null ? gm.State : null);
            card.Clear();

            var head = UIKit.Row();
            head.style.marginBottom = 12f;
            var tile = UIWidgets.IconTile(UIGlyphs.DecorPaint, UIPalette.Coral, 46f);
            tile.style.marginRight = 14f;
            head.Add(tile);
            head.Add(UIKit.Text(Loc.Tr("Einrichten", "Furnish"), 30f, UIPalette.Ink, true));
            head.Add(UIKit.Flex());
            var money = UIKit.Row();
            money.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 24f));
            moneyLabel = UIKit.Text(gm != null ? Loc.Money(gm.State.money) : "", 20f, UIPalette.LeafInk, true);
            moneyLabel.style.marginLeft = 6f;
            moneyLabel.style.marginRight = 16f;
            money.Add(moneyLabel);
            head.Add(money);
            head.Add(UIWidgets.RoundIconButton(UIGlyphs.Cross, Close, 40f));
            card.Add(head);

            tabRow = UIKit.Row();
            tabRow.style.marginBottom = 14f;
            card.Add(tabRow);
            AddTab(Tab.Walls, UIGlyphs.DecorPaint, Loc.Tr("Wände", "Walls"));
            AddTab(Tab.Floor, UIGlyphs.DecorFloor, Loc.Tr("Boden", "Floor"));
            AddTab(Tab.Rug, UIGlyphs.DecorRug, Loc.Tr("Teppich", "Rug"));
            AddTab(Tab.Curtains, UIGlyphs.DecorCurtain, Loc.Tr("Vorhänge", "Curtains"));
            AddTab(Tab.Decor, UIGlyphs.Sparkle, Loc.Tr("Deko", "Decor"));

            var scroll = UIKit.Scroll();
            scroll.style.height = 420f;
            scroll.style.flexGrow = 0;
            body = UIKit.Row();
            body.style.flexWrap = Wrap.Wrap;
            body.style.alignItems = Align.FlexStart;
            scroll.Add(body);
            card.Add(scroll);

            footer = UIKit.Text(Hint(), 16f, UIPalette.InkSoft);
            footer.style.marginTop = 10f;
            footer.style.whiteSpace = WhiteSpace.Normal;
            card.Add(footer);

            switch (tab)
            {
                case Tab.Walls: Swatches(HutDecor.Walls, HutDecor.Wall, 3, -1, i => HutDecor.SetWall(i)); break;
                case Tab.Floor: Swatches(HutDecor.Floors, HutDecor.Floor, 3, -1, i => HutDecor.SetFloor(i)); break;
                case Tab.Rug: Swatches(HutDecor.Rugs, HutDecor.Rug, 2, HutDecor.RugNone, i => HutDecor.SetRug(i)); break;
                case Tab.Curtains: Swatches(HutDecor.Curtains, HutDecor.CurtainChoice, 1, HutDecor.CurtainsNone, i => HutDecor.SetCurtains(i)); break;
                default: DecorCards(); break;
            }
        }

        static string Hint() =>
            Loc.Tr("Klicken = übernehmen (sofort in der Hütte) · ", "Click = apply (right away in the hut) · ") + GameInput.BindingLabel(GameAction.Pause) + Loc.Tr(" schließt", " closes");

        static void AddTab(Tab t, UIGlyph.DrawFn icon, string text)
        {
            bool sel = t == tab;
            var b = UIKit.Row();
            UIKit.Pad(b, 8f, 14f, 8f, 12f);
            UIKit.Radius(b, 14f);
            b.style.marginRight = 8f;
            b.Add(new UIGlyph(icon, sel ? Color.white : UIPalette.InkSoft, 22f));
            var l = UIKit.Text(text, 17f, sel ? Color.white : UIPalette.InkSoft, true);
            l.style.marginLeft = 6f;
            b.Add(l);
            UIKit.IgnorePicking(b);
            b.pickingMode = PickingMode.Position;
            UIKit.Clickable(b, () => { tab = t; dirty = true; },
                sel ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.06f), sel ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.12f));
            tabRow.Add(b);
        }

        /// colors: 1 = flat, 2 = pattern (rug), 3 = three tones (boards). none = index drawn as "nothing".
        static void Swatches(HutSwatch[] list, int selected, int colors, int none, System.Func<int, bool> apply)
        {
            for (int i = 0; i < list.Length; i++)
            {
                int idx = i;
                var sw = list[i];
                bool sel = i == selected;
                var cell = UIKit.Col();
                cell.style.alignItems = Align.Center;
                cell.style.width = 132f;
                cell.style.marginRight = 14f;
                cell.style.marginBottom = 14f;
                UIKit.Pad(cell, 8f);
                UIKit.Radius(cell, 18f);
                var chip = UIKit.Box();
                UIKit.Size(chip, 96f, 96f);
                UIKit.Radius(chip, 16f);
                chip.style.overflow = Overflow.Hidden;
                UIKit.Border(chip, sel ? 4f : 2f, sel ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.15f));
                if (i == none)
                {
                    chip.style.backgroundColor = UIPalette.CardAlt;
                    UIKit.Center(chip);
                    chip.Add(new UIGlyph(UIGlyphs.Cross, UIPalette.InkSoft, 44f));
                }
                else if (colors == 3)
                {
                    chip.style.flexDirection = FlexDirection.Row;
                    Color[] tones = { sw.a, sw.b, sw.c, sw.a };
                    for (int k = 0; k < tones.Length; k++)
                    {
                        var plank = UIKit.Box();
                        plank.style.flexGrow = 1;
                        plank.style.backgroundColor = tones[k];
                        if (k > 0) { plank.style.borderLeftWidth = 2f; plank.style.borderLeftColor = UIPalette.A(UIPalette.Ink, 0.25f); }
                        chip.Add(plank);
                    }
                }
                else if (colors == 2)
                {
                    chip.style.backgroundColor = sw.a;
                    UIKit.Center(chip);
                    var inner = UIKit.Box();
                    UIKit.Size(inner, 56f, 56f);
                    UIKit.Radius(inner, i == 1 ? 28f : 6f);
                    inner.style.backgroundColor = sw.b;
                    if (sw.c != sw.b)
                    {
                        UIKit.Center(inner);
                        var dot = UIKit.Box();
                        UIKit.Size(dot, 24f, 24f);
                        UIKit.Radius(dot, i == 1 ? 12f : 3f);
                        dot.style.backgroundColor = sw.c;
                        inner.Add(dot);
                    }
                    chip.Add(inner);
                }
                else chip.style.backgroundColor = sw.a;
                if (sel)
                {
                    var ok = UIKit.Box();
                    UIKit.Size(ok, 28f, 28f);
                    UIKit.Radius(ok, 14f);
                    ok.style.backgroundColor = UIPalette.Coral;
                    ok.style.position = Position.Absolute;
                    ok.style.right = 4f; ok.style.top = 4f;
                    UIKit.Center(ok);
                    ok.Add(new UIGlyph(UIGlyphs.Check, Color.white, 18f));
                    chip.Add(ok);
                }
                cell.Add(chip);
                var name = UIKit.Text(sw.Name, 15f, sel ? UIPalette.Ink : UIPalette.InkSoft, sel);
                name.style.marginTop = 6f;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.whiteSpace = WhiteSpace.Normal;
                cell.Add(name);
                UIKit.IgnorePicking(cell);
                cell.pickingMode = PickingMode.Position;
                UIKit.Clickable(cell, () =>
                {
                    if (apply(idx)) { footer.text = sw.Name + " ✓"; dirty = true; }
                    else UIAudio.Nope();
                }, Color.clear, UIPalette.A(UIPalette.Ink, 0.06f));
                body.Add(cell);
            }
        }

        static void DecorCards()
        {
            var items = HutDecor.Items;
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                bool placed = HutDecor.IsPlaced(it.id);
                bool avail = HutDecor.IsAvailable(it.id);
                bool forSale = it.unlock == HutDecorUnlock.Buy && !avail;
                bool locked = !avail && !forSale;
                var cell = UIKit.Col();
                cell.style.alignItems = Align.Center;
                cell.style.width = 158f;
                cell.style.height = 186f;
                cell.style.marginRight = 12f;
                cell.style.marginBottom = 12f;
                UIKit.Pad(cell, 10f);
                UIKit.Radius(cell, 18f);
                UIKit.Border(cell, placed ? 3f : 2f, placed ? UIPalette.Leaf : UIPalette.A(UIPalette.Ink, 0.1f));
                var disc = UIKit.Box();
                UIKit.Size(disc, 84f, 84f);
                UIKit.Radius(disc, 42f);
                UIKit.Center(disc);
                disc.style.backgroundColor = locked ? UIPalette.A(UIPalette.Ink, 0.08f) : UIPalette.A(it.tint, 0.25f);
                var g = new UIGlyph(ItemGlyph(it.id), locked ? UIPalette.Silhouette : UIPalette.Darken(it.tint, 0.25f), 62f);
                disc.Add(g);
                if (locked)
                {
                    var lk = new UIGlyph(UIGlyphs.Lock, UIPalette.InkSoft, 26f);
                    lk.style.position = Position.Absolute;
                    lk.style.right = -2f; lk.style.bottom = -2f;
                    disc.Add(lk);
                }
                cell.Add(disc);
                var name = UIKit.Text(it.Name, 15f, UIPalette.Ink, true);
                name.style.marginTop = 6f;
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.whiteSpace = WhiteSpace.Normal;
                name.style.height = 40f;
                cell.Add(name);
                Label chipL;
                if (placed) chipL = UIKit.Pill("✓ " + Loc.Tr("Aufgestellt", "Placed"), UIPalette.Leaf, UIPalette.Ink, 13f);
                else if (avail) chipL = UIKit.Pill(it.unlock == HutDecorUnlock.Free ? Loc.Tr("Gratis", "Free") : Loc.Tr("Aufstellen", "Place"), UIPalette.A(UIPalette.Turquoise, 0.25f), UIPalette.TurquoiseInk, 13f);
                else if (forSale) chipL = UIKit.Pill(Loc.Money(it.price), GameManager.I != null && GameManager.I.State.money >= it.price ? UIPalette.Sun : UIPalette.A(UIPalette.Danger, 0.2f), UIPalette.Ink, 13f);
                else chipL = UIKit.Pill(HutDecor.LockHint(it), UIPalette.A(UIPalette.Ink, 0.1f), UIPalette.InkSoft, 12f);
                chipL.style.marginTop = 4f;
                cell.Add(chipL);
                UIKit.IgnorePicking(cell);
                cell.pickingMode = PickingMode.Position;
                string id = it.id;
                UIKit.Clickable(cell, () => OnItem(id), placed ? UIPalette.A(UIPalette.Leaf, 0.12f) : Color.clear, UIPalette.A(UIPalette.Ink, 0.06f));
                cell.RegisterCallback<PointerEnterEvent>(_ => { if (footer != null) footer.text = ItemHint(id); });
                body.Add(cell);
            }
        }

        static string ItemHint(string id)
        {
            var it = HutDecor.Item(id);
            if (it == null) return Hint();
            if (HutDecor.IsPlaced(id)) return it.Name + Loc.Tr(" – klicken zum Wegräumen", " – click to put away");
            if (HutDecor.IsAvailable(id)) return it.Name + Loc.Tr(" – klicken zum Aufstellen", " – click to place");
            if (it.unlock == HutDecorUnlock.Buy) return it.Name + " – " + Loc.Money(it.price) + Loc.Tr(" aus der Crew-Kasse", " from the crew money");
            return it.Name + Loc.Tr(" – freischalten: ", " – unlock: ") + HutDecor.LockHint(it);
        }

        static void OnItem(string id)
        {
            var it = HutDecor.Item(id);
            if (it == null) return;
            if (HutDecor.IsPlaced(id) || HutDecor.IsAvailable(id))
            {
                if (HutDecor.Toggle(id)) { dirty = true; footer.text = ItemHint(id); }
                else UIAudio.Nope();
                return;
            }
            if (it.unlock == HutDecorUnlock.Buy)
            {
                var gm = GameManager.I;
                if (gm == null || gm.State.money < it.price) { UIAudio.Nope(); footer.text = Loc.Tr("Zu wenig Geld: ", "Not enough money: ") + Loc.Money(it.price); return; }
                UIDialog.Show(ItemGlyph(id), it.tint, it.Name, Loc.Money(it.price) + Loc.Tr(" aus der Crew-Kasse", " from the crew money"),
                    Loc.Tr("Abbrechen", "Cancel"), Loc.Tr("Kaufen", "Buy"), () => { if (HutDecor.Buy(id)) UIAudio.Ping(); dirty = true; });
                return;
            }
            UIAudio.Nope();
            footer.text = ItemHint(id);
        }

        public static UIGlyph.DrawFn ItemGlyph(string id)
        {
            switch (id)
            {
                case "palm": return UIGlyphs.DecorPalm;
                case "lights": return UIGlyphs.DecorLights;
                case "surfboard": return UIGlyphs.DecorSurf;
                case "records": return UIGlyphs.DecorRecord;
                case "hangchair": return UIGlyphs.DecorChair;
                case "marlin": return UIGlyphs.DecorMarlin;
                case "aquarium": return UIGlyphs.DecorAquarium;
                case "shells": return UIGlyphs.Shell;
                case "logframe": return UIGlyphs.DecorFrame;
                case "bottles": return UIGlyphs.Bottle;
                case "finbutton": return UIGlyphs.DecorPlaque;
            }
            return UIGlyphs.Star;
        }
    }

    /// Glyphs of the furnishing screen.
    public static partial class UIGlyphs
    {
        public static void DecorPaint(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            FillRoundRect(p, r, 0.14f, 0.14f, 0.78f, 0.38f, 0.08f);
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.07f, 0.78f, 0.26f, 0.88f, 0.26f, 0.88f, 0.52f, 0.5f, 0.56f, 0.5f, 0.66f);
            FillRoundRect(p, r, 0.42f, 0.64f, 0.58f, 0.92f, 0.05f);
        }

        public static void DecorFloor(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            FillRoundRect(p, r, 0.1f, 0.16f, 0.54f, 0.34f, 0.03f); FillRoundRect(p, r, 0.58f, 0.16f, 0.9f, 0.34f, 0.03f);
            FillRoundRect(p, r, 0.1f, 0.41f, 0.3f, 0.59f, 0.03f); FillRoundRect(p, r, 0.34f, 0.41f, 0.9f, 0.59f, 0.03f);
            FillRoundRect(p, r, 0.1f, 0.66f, 0.68f, 0.84f, 0.03f); FillRoundRect(p, r, 0.72f, 0.66f, 0.9f, 0.84f, 0.03f);
        }

        public static void DecorRug(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            FillRoundRect(p, r, 0.16f, 0.24f, 0.84f, 0.76f, 0.05f);
            p.fillColor = Al(Color.white, 0.55f * c.a);
            for (int i = 0; i < 3; i++) FillRoundRect(p, r, 0.3f + i * 0.16f, 0.24f, 0.36f + i * 0.16f, 0.76f, 0f);
            p.strokeColor = c;
            for (int i = 0; i < 5; i++)
            {
                float y = 0.28f + i * 0.11f;
                Stroke(p, r, S(r) * 0.03f, 0.08f, y, 0.16f, y);
                Stroke(p, r, S(r) * 0.03f, 0.84f, y, 0.92f, y);
            }
        }

        public static void DecorCurtain(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.06f, 0.08f, 0.14f, 0.92f, 0.14f);
            p.fillColor = c;
            Poly(p, r, 0.12f, 0.18f, 0.44f, 0.18f, 0.3f, 0.55f, 0.36f, 0.9f, 0.12f, 0.9f);
            Poly(p, r, 0.88f, 0.18f, 0.56f, 0.18f, 0.7f, 0.55f, 0.64f, 0.9f, 0.88f, 0.9f);
        }

        public static void DecorPalm(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            Poly(p, r, 0.32f, 0.72f, 0.68f, 0.72f, 0.62f, 0.94f, 0.38f, 0.94f);
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.06f, 0.5f, 0.72f, 0.52f, 0.5f, 0.5f, 0.32f);
            Poly(p, r, 0.5f, 0.3f, 0.12f, 0.36f, 0.3f, 0.24f);
            Poly(p, r, 0.5f, 0.3f, 0.88f, 0.36f, 0.7f, 0.24f);
            Poly(p, r, 0.5f, 0.3f, 0.22f, 0.12f, 0.42f, 0.14f);
            Poly(p, r, 0.5f, 0.3f, 0.78f, 0.12f, 0.58f, 0.14f);
            Poly(p, r, 0.5f, 0.3f, 0.2f, 0.56f, 0.32f, 0.38f);
            Poly(p, r, 0.5f, 0.3f, 0.8f, 0.56f, 0.68f, 0.38f);
        }

        public static void DecorLights(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            p.lineWidth = S(r) * 0.04f;
            p.BeginPath();
            p.MoveTo(P(r, 0.06f, 0.2f));
            p.BezierCurveTo(P(r, 0.25f, 0.55f), P(r, 0.75f, 0.55f), P(r, 0.94f, 0.2f));
            p.Stroke();
            p.fillColor = c;
            for (int i = 0; i < 5; i++)
            {
                float t = (i + 1) / 6f;
                float x = 0.06f + 0.88f * t;
                float y = 0.2f + 0.27f * Mathf.Sin(t * Mathf.PI) + 0.12f;
                Circle(p, P(r, x, y), S(r) * 0.065f);
            }
        }

        public static void DecorSurf(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.82f, 0.1f));
            p.BezierCurveTo(P(r, 0.98f, 0.3f), P(r, 0.4f, 0.95f), P(r, 0.2f, 0.92f));
            p.BezierCurveTo(P(r, 0.02f, 0.88f), P(r, 0.55f, 0.1f), P(r, 0.82f, 0.1f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Al(Color.white, 0.7f * c.a);
            Stroke(p, r, S(r) * 0.03f, 0.78f, 0.18f, 0.24f, 0.86f);
        }

        public static void DecorRecord(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            Circle(p, r.center, S(r) * 0.4f);
            p.fillColor = Al(Color.white, 0.75f * c.a);
            Circle(p, r.center, S(r) * 0.13f);
            p.fillColor = c;
            Circle(p, r.center, S(r) * 0.035f);
            p.strokeColor = Al(Color.white, 0.35f * c.a);
            Ring(p, r.center, S(r) * 0.28f, S(r) * 0.015f);
        }

        public static void DecorChair(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.04f, 0.5f, 0.04f, 0.5f, 0.2f);
            p.lineWidth = S(r) * 0.07f;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.2f));
            p.BezierCurveTo(P(r, 0.05f, 0.3f), P(r, 0.05f, 0.85f), P(r, 0.5f, 0.88f));
            p.BezierCurveTo(P(r, 0.95f, 0.85f), P(r, 0.95f, 0.3f), P(r, 0.5f, 0.2f));
            p.Stroke();
            p.fillColor = c;
            FillRoundRect(p, r, 0.24f, 0.62f, 0.76f, 0.74f, 0.06f);
        }

        public static void DecorMarlin(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.72f, 0.5f));
            p.BezierCurveTo(P(r, 0.6f, 0.36f), P(r, 0.32f, 0.38f), P(r, 0.22f, 0.5f));
            p.BezierCurveTo(P(r, 0.32f, 0.62f), P(r, 0.6f, 0.64f), P(r, 0.72f, 0.5f));
            p.ClosePath();
            p.Fill();
            Poly(p, r, 0.6f, 0.4f, 0.32f, 0.42f, 0.42f, 0.18f);
            Poly(p, r, 0.24f, 0.5f, 0.06f, 0.3f, 0.12f, 0.5f, 0.06f, 0.7f);
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.03f, 0.7f, 0.49f, 0.96f, 0.46f);
        }

        public static void DecorAquarium(Painter2D p, Rect r, Color c)
        {
            p.fillColor = Al(c, 0.45f);
            FillRoundRect(p, r, 0.12f, 0.28f, 0.88f, 0.78f, 0.04f);
            p.strokeColor = c;
            p.lineWidth = S(r) * 0.05f;
            p.BeginPath();
            RoundRect(p, r, 0.12f, 0.2f, 0.88f, 0.8f, 0.04f);
            p.Stroke();
            p.fillColor = c;
            Poly(p, r, 0.42f, 0.5f, 0.58f, 0.42f, 0.66f, 0.5f, 0.58f, 0.58f);
            Poly(p, r, 0.42f, 0.5f, 0.34f, 0.43f, 0.34f, 0.57f);
            FillRoundRect(p, r, 0.08f, 0.8f, 0.92f, 0.9f, 0.02f);
        }

        public static void DecorFrame(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            p.lineWidth = S(r) * 0.07f;
            p.BeginPath();
            RoundRect(p, r, 0.16f, 0.16f, 0.84f, 0.84f, 0.02f);
            p.Stroke();
            for (int i = 0; i < 4; i++) Stroke(p, r, S(r) * 0.04f, 0.3f, 0.32f + i * 0.12f, i == 3 ? 0.55f : 0.7f, 0.32f + i * 0.12f);
        }

        public static void DecorPlaque(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            FillRoundRect(p, r, 0.2f, 0.1f, 0.8f, 0.9f, 0.08f);
            p.fillColor = Al(Color.white, 0.8f * c.a);
            Poly(p, r, 0.4f, 0.32f, 0.66f, 0.5f, 0.4f, 0.68f);
        }
    }
}
