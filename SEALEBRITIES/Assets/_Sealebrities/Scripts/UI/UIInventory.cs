using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 BACKPACK screen (GDD §19 „Inventar als Rucksack“, GameAction.Inventory – default I). Blocks gameplay while
    /// open (UIState.InventoryOpen), the world keeps running.
    ///  left:  HANDS (main hand / offhand – drop an item here to hold it), the ONE hotbar row (8 slots: drag items in,
    ///         drag slots to swap, right-click a slot = main hand ↔ offhand, drag a slot out = empty it) and the
    ///         24-slot backpack grid (drag to sort; hover + 1–8 puts the item on that hotbar slot)
    ///  right: tabs – Gegenstand (selected item: stats, Essen / Trinken / Benutzen, in die Haupt-/Zweithand,
    ///         Wegwerfen), Ausrüstung (camera / lamp / rod to drag + every owned gear item to equip), Kühlbox (the shared
    ///         fish cooler – take a fish out raw to cook it), Sammlung (collectibles, map pieces, the treasure map)
    ///  footer: hunger / thirst and a hint line.
    public class UIInventory
    {
        static readonly GearSlot[] GearSlots = { GearSlot.Camera, GearSlot.Lamp, GearSlot.Rod, GearSlot.Tank, GearSlot.Fins, GearSlot.Boat, GearSlot.CameraMod, GearSlot.Radio };
        static readonly string[] HoldGear = { ItemIds.Camera, ItemIds.Flashlight, ItemIds.FishingRod };
        // 8 hotbar slots must fit the left column (6 × (90 + 8) + 24 = 612 px): 8 × 66 + 7 × 8 + 20 = 604 px
        const float GridSlot = 90f, BarSlot = 66f, HandSlot = 100f, Gap = 8f;
        const int GridCols = 6;

        enum Tab { Item, Gear, Cooler, Collection }
        enum DragKind { None, Grid, Bar, Gear }

        readonly VisualElement root, card, ghost;
        readonly UIGlyph ghostIcon;
        VisualElement handsRow, barRow, gridHost, tabRow, panel;
        Label capacityLabel, footer, needsLabel;
        readonly VisualElement[] handBoxes = new VisualElement[2];
        readonly List<VisualElement> barBoxes = new List<VisualElement>(Hands.SlotCount);
        readonly List<VisualElement> gridBoxes = new List<VisualElement>(Backpack.Capacity);
        readonly List<GearItem> tmpItems = new List<GearItem>();
        bool dirty;
        float anim = 1f, needsTimer;
        Tab tab = Tab.Item;
        int selected = -1;
        string selectedId;
        bool showMap;
        int hoverGrid = -1;
        string hoverId;

        // drag state
        DragKind dragKind;
        int dragIndex = -1, dragPointer = -1;
        string dragId;
        bool dragging;
        Vector2 dragStart;
        VisualElement dragSource;

        public UIInventory(VisualElement layer)
        {
            root = new VisualElement { name = "sb-inventory" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0.08f, 0.1f, 0.18f, 0.35f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            layer.Add(root);

            card = UIWidgets.ModalCard(1480f);
            card.style.backgroundColor = UIPalette.A(UIPalette.Card, 0.95f);
            UIKit.Radius(card, 24f);
            // round 3 UI step: the 1480 px card scales down to fit any window (1280×720, 4:3, small windowed modes)
            // instead of being cut – no max-width squeeze (the 6-column grid and the hands keep their size)
            card.style.maxWidth = StyleKeyword.None;
            card.style.maxHeight = StyleKeyword.None;
            card.style.flexShrink = 0;
            card.style.transformOrigin = new TransformOrigin(Length.Percent(50f), Length.Percent(50f), 0f);
            root.Add(card);
            root.RegisterCallback<GeometryChangedEvent>(_ => FitCard());
            card.RegisterCallback<GeometryChangedEvent>(_ => FitCard());
            BuildFrame();

            ghost = UIKit.Box();
            ghost.style.position = Position.Absolute;
            UIKit.Size(ghost, 72f, 72f);
            ghost.style.marginLeft = -36f; ghost.style.marginTop = -36f;
            ghost.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            UIKit.Radius(ghost, 14f);
            UIKit.Border(ghost, 2f, UIPalette.Turquoise);
            UIKit.Center(ghost);
            ghostIcon = new UIGlyph(null, Color.white, 60f);
            ghost.Add(ghostIcon);
            ghost.pickingMode = PickingMode.Ignore;
            ghostIcon.pickingMode = PickingMode.Ignore;
            ghost.style.display = DisplayStyle.None;
            root.Add(ghost);

            root.RegisterCallback<ClickEvent>(e => { if (e.target == root) Close(); });

            Hands.Changed += MarkDirty;
            Backpack.Changed += MarkDirty;
            GameEvents.GearChanged += MarkDirty;
            GameInput.BindingsChanged += MarkDirty;
            FishMarket.Changed += MarkDirty;
            MapPieces.Changed += MarkDirty;
            MapPieces.OpenMapHandler = OpenMap;
        }

        float fitScale = 1f;

        /// Scales the card down (never up) so it always fits the panel with a small margin.
        void FitCard()
        {
            float rw = root.layout.width, rh = root.layout.height;
            float cw = card.layout.width, ch = card.layout.height;
            if (!(rw > 10f) || !(rh > 10f) || !(cw > 10f) || !(ch > 10f)) return;
            float s = Mathf.Min(1f, (rw - 32f) / cw, (rh - 32f) / ch);
            s = Mathf.Max(0.5f, s);
            if (Mathf.Abs(s - fitScale) < 0.003f) return;
            fitScale = s;
            card.style.scale = new Scale(new Vector2(s, s));
        }

        /// Tests: the card's fit-to-screen scale (1 = full size).
        public float FitScale => fitScale;

        /// Tests: the (scaled) card lies completely inside the screen panel.
        public bool CardFits
        {
            get
            {
                var r = root.worldBound;
                var c = card.worldBound;
                return c.width > 10f && c.xMin >= r.xMin - 2f && c.yMin >= r.yMin - 2f && c.xMax <= r.xMax + 2f && c.yMax <= r.yMax + 2f;
            }
        }

        /// Language changed: rebuild the static texts.
        public void RefreshTexts()
        {
            card.Clear();
            BuildFrame();
            if (UIState.InventoryOpen) Rebuild();
        }

        public bool IsOpen => UIState.InventoryOpen;

        // ------------------------------------------------------------------ frame
        void BuildFrame()
        {
            var head = UIKit.Row();
            head.style.marginBottom = 14f;
            var tile = UIWidgets.IconTile(UIGlyphs.Bag, UIPalette.Coral, 48f);
            tile.style.marginRight = 14f;
            head.Add(tile);
            head.Add(UIKit.Text(Loc.Tr("Rucksack", "Backpack"), 32f, UIPalette.Ink, true));
            capacityLabel = UIKit.Text("", 18f, UIPalette.InkSoft, true);
            capacityLabel.style.marginLeft = 14f;
            head.Add(capacityLabel);
            head.Add(UIKit.Flex());
            head.Add(UIWidgets.RoundIconButton(UIGlyphs.Cross, Close, 40f));
            card.Add(head);

            var cols = UIKit.Row();
            cols.style.alignItems = Align.FlexStart;
            card.Add(cols);

            // ---- left: hands, hotbar, grid
            var left = UIKit.Col();
            left.style.width = GridCols * (GridSlot + Gap) + 24f;
            left.style.flexShrink = 0;
            cols.Add(left);

            var top = UIKit.Row();
            top.style.alignItems = Align.FlexEnd;
            left.Add(top);
            var handsCol = UIKit.Col();
            handsCol.Add(SectionLabel(UIGlyphs.Hand, Loc.Tr("Hände", "Hands")));
            handsRow = UIKit.Row();
            handsCol.Add(handsRow);
            top.Add(handsCol);
            for (int h = 0; h < 2; h++)
            {
                var hand = h == 0 ? Hand.Right : Hand.Left;
                var box = UIKit.Box();
                UIKit.Size(box, HandSlot, HandSlot);
                UIKit.Radius(box, 18f);
                UIKit.Center(box);
                box.style.marginRight = 12f;
                box.style.backgroundColor = new Color(0.10f, 0.13f, 0.22f, 0.86f);
                UIKit.Border(box, 3f, hand == Hand.Right ? Color.white : UIPalette.Turquoise);
                handBoxes[h] = box;
                var c = hand;
                box.RegisterCallback<ClickEvent>(_ => { Hands.Stow(c); UIAudio.Click(); });
                box.RegisterCallback<PointerEnterEvent>(_ => footer.text = c == Hand.Right ? Loc.Tr("Haupthand – Gegenstand hierher ziehen", "Main hand – drag an item here") : Loc.Tr("Zweithand – z. B. die Lampe", "Offhand – e.g. the lamp"));
                handsRow.Add(box);
            }
            var nameCol = UIKit.Col();
            nameCol.style.marginLeft = 4f;
            nameCol.style.marginBottom = 8f;
            needsLabel = UIKit.Text("", 17f, UIPalette.InkSoft, true);
            nameCol.Add(needsLabel);
            top.Add(nameCol);

            left.Add(SectionLabel(UIGlyphs.Keyboard, Loc.Tr("Leiste", "Hotbar")));
            barRow = UIKit.Row();
            barRow.style.backgroundColor = new Color(0.10f, 0.13f, 0.22f, 0.86f);
            UIKit.Radius(barRow, 18f);
            UIKit.Pad(barRow, 10f, 10f, 10f, 10f);
            left.Add(barRow);

            left.Add(SectionLabel(UIGlyphs.Bag, Loc.Tr("Rucksack", "Backpack")));
            gridHost = UIKit.Row();
            gridHost.style.flexWrap = Wrap.Wrap;
            gridHost.style.width = GridCols * (GridSlot + Gap);
            // 48 slots (Q27 „unbegrenzt“): 4 rows visible, the mouse wheel scrolls to the rest
            var gridScroll = UIKit.Scroll();
            gridScroll.style.flexGrow = 0;
            gridScroll.style.height = 4f * (GridSlot + Gap);
            gridScroll.style.width = GridCols * (GridSlot + Gap);
            gridScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            gridScroll.Add(gridHost);
            left.Add(gridScroll);

            // ---- right: tabs + panel
            var right = UIKit.Col();
            right.style.marginLeft = 26f;
            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            cols.Add(right);
            tabRow = UIKit.Row();
            tabRow.style.marginBottom = 10f;
            right.Add(tabRow);
            var scroll = UIKit.Scroll();
            scroll.style.height = 600f;
            scroll.style.flexGrow = 0;
            panel = UIKit.Col();
            scroll.Add(panel);
            right.Add(scroll);

            footer = UIKit.Text("", 17f, UIPalette.InkSoft);
            footer.style.marginTop = 12f;
            footer.style.minHeight = 24f;
            footer.style.whiteSpace = WhiteSpace.Normal;
            card.Add(footer);
        }

        static VisualElement SectionLabel(UIGlyph.DrawFn icon, string text)
        {
            var row = UIKit.Row();
            row.style.marginTop = 10f;
            row.style.marginBottom = 8f;
            row.Add(new UIGlyph(icon, UIPalette.InkSoft, 22f));
            var l = UIKit.Text(text, 20f, UIPalette.InkSoft, true);
            l.style.marginLeft = 8f;
            row.Add(l);
            return row;
        }

        // ------------------------------------------------------------------ open / close
        public void Open()
        {
            if (UIState.InventoryOpen) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.LoadingOpen || UIState.PauseOpen || UIState.PhoneOpen) return;
            UIState.InventoryOpen = true;
            root.style.display = DisplayStyle.Flex;
            anim = 0f;
            footer.text = HintLine();
            EndDrag();
            Rebuild();
            UIAudio.Pop();
        }

        public void Close()
        {
            if (!UIState.InventoryOpen && root.style.display.value == DisplayStyle.None) return;
            EndDrag();
            UIState.InventoryOpen = false;
            showMap = false;
            root.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (UIState.InventoryOpen) Close(); else Open();
        }

        /// MapPieces.OpenMapHandler: the backpack opens on the collection tab with the treasure map.
        void OpenMap()
        {
            tab = Tab.Collection;
            showMap = true;
            if (!UIState.InventoryOpen) Open();
            else dirty = true;
        }

        void MarkDirty() { dirty = true; }

        static string HintLine() =>
            Loc.Tr("Ziehen & ablegen · Rechtsklick auf die Leiste: Haupt-/Zweithand · Zeigen + ", "Drag & drop · right-click the hotbar: main hand / offhand · hover + ")
            + GameInput.BindingLabel(GameInput.SlotAction(0)) + "–" + GameInput.BindingLabel(GameInput.SlotAction(Hands.SlotCount - 1))
            + Loc.Tr(": auf die Leiste", ": to the hotbar");

        public void Tick(float dt)
        {
            if (!UIState.InventoryOpen) return;
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIState.MainMenuOpen || UIState.PauseOpen) { Close(); return; }
            if (anim < 1f)
            {
                anim = Mathf.Min(1f, anim + dt / 0.2f);
                float e = 1f - (1f - anim) * (1f - anim);
                card.style.opacity = e;
                card.style.translate = new Translate(0f, 30f * (1f - e));
            }
            // hover a backpack item (or a gear tile) + a hotbar key: put it on that slot
            if (!dragging && !string.IsNullOrEmpty(hoverId) && Hands.ItemOf(hoverId) != HoldItem.None)
            {
                for (int i = 0; i < Hands.SlotCount; i++)
                {
                    if (!GameInput.SlotPressed(i)) continue;
                    Hands.Assign(i, hoverId);
                    UIAudio.Click();
                    footer.text = Hands.DisplayName(hoverId) + " → " + GameInput.BindingLabel(GameInput.SlotAction(i));
                    break;
                }
            }
            needsTimer -= dt;
            if (needsTimer <= 0f) { needsTimer = 1f; RefreshNeeds(); }
            if (dirty && !dragging) Rebuild();
        }

        void RefreshNeeds()
        {
            needsLabel.text = Loc.Tr("Hunger ", "Food ") + Mathf.CeilToInt(Survival.Hunger) + " %   ·   " + Loc.Tr("Durst ", "Water ") + Mathf.CeilToInt(Survival.Thirst) + " %";
            needsLabel.style.color = Survival.Hungry || Survival.Thirsty ? UIPalette.CoralInk : UIPalette.InkSoft;
        }

        // ------------------------------------------------------------------ rebuild
        public void Rebuild()
        {
            dirty = false;
            if (!UIState.InventoryOpen) return;
            capacityLabel.text = Backpack.UsedSlots + "/" + Backpack.Capacity;
            RefreshNeeds();
            BuildHands();
            BuildBar();
            BuildGrid();
            BuildTabs();
            BuildPanel();
        }

        void BuildHands()
        {
            for (int h = 0; h < 2; h++)
            {
                var box = handBoxes[h];
                box.Clear();
                var hand = h == 0 ? Hand.Right : Hand.Left;
                string id = Hands.ActiveId(hand);
                var draw = UIGlyphs.ForItemId(id);
                if (draw != null) box.Add(new UIGlyph(draw, UIGlyphs.TintFor(id), 72f));
                else
                {
                    var g = new UIGlyph(UIGlyphs.Hand, new Color(1f, 1f, 1f, 0.45f), 44f);
                    if (hand == Hand.Left) g.style.scale = new Scale(new Vector2(-1f, 1f));
                    box.Add(g);
                }
                var l = UIKit.Text(hand == Hand.Right ? Loc.Tr("Haupt", "Main") : Loc.Tr("Zweit", "Off"), 13f, Color.white, true);
                UIKit.Outline(l, 1.2f);
                l.style.position = Position.Absolute;
                l.style.left = 8f; l.style.top = 4f;
                l.pickingMode = PickingMode.Ignore;
                box.Add(l);
            }
        }

        void BuildBar()
        {
            barRow.Clear();
            barBoxes.Clear();
            int main = Hands.ActiveSlot(Hand.Right), off = Hands.ActiveSlot(Hand.Left);
            for (int i = 0; i < Hands.SlotCount; i++)
            {
                string id = Hands.SlotId(i);
                bool isOff = Hands.SlotHand(i) == Hand.Left;
                var box = SlotBox(id, BarSlot, !Hands.SlotUsable(i) && id != ItemIds.None, true);
                if (i > 0) box.style.marginLeft = Gap;
                Color border = i == main ? Color.white : i == off ? UIPalette.Turquoise : isOff ? UIPalette.A(UIPalette.Turquoise, 0.55f) : new Color(1f, 1f, 1f, 0.45f);
                UIKit.Border(box, i == main || i == off ? 3f : 2f, border);
                var key = UIKit.Text(GameInput.BindingLabel(GameInput.SlotAction(i)), 13f, Color.white, true);
                UIKit.Outline(key, 1.2f);
                key.style.position = Position.Absolute;
                key.style.left = 7f; key.style.top = 4f;
                key.pickingMode = PickingMode.Ignore;
                box.Add(key);
                if (isOff)
                {
                    var m = new UIGlyph(UIGlyphs.Hand, UIPalette.Turquoise, 18f);
                    m.style.position = Position.Absolute;
                    m.style.left = 5f; m.style.bottom = 5f;
                    m.style.scale = new Scale(new Vector2(-1f, 1f));
                    box.Add(m);
                }
                int idx = i;
                box.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button == 1)
                    {
                        // right-click: this slot's item goes into the other hand
                        Hands.SetSlotHand(idx, Hands.SlotHand(idx) == Hand.Left ? Hand.Right : Hand.Left);
                        UIAudio.Click();
                        e.StopPropagation();
                        return;
                    }
                    BeginDrag(e, DragKind.Bar, idx, Hands.SlotId(idx), box);
                });
                RegisterDragEvents(box);
                box.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    string sid = Hands.SlotId(idx);
                    footer.text = sid == ItemIds.None ? Loc.Tr("Leerer Platz", "Empty slot")
                        : Hands.DisplayName(sid) + (Hands.SlotHand(idx) == Hand.Left ? Loc.Tr(" · Zweithand", " · offhand") : Loc.Tr(" · Haupthand", " · main hand"))
                          + (Hands.IsTwoHandedId(sid) ? Loc.Tr(" (beidhändig)", " (two-handed)") : "");
                });
                barRow.Add(box);
                barBoxes.Add(box);
            }
        }

        void BuildGrid()
        {
            gridHost.Clear();
            gridBoxes.Clear();
            var l = Backpack.Stacks;
            for (int i = 0; i < Backpack.Capacity; i++)
            {
                var st = i < l.Count ? l[i] : null;
                bool has = st != null && st.count > 0 && st.Def != null;
                string id = has ? st.id : null;
                var box = SlotBox(id, GridSlot, false, false);
                box.style.marginRight = Gap;
                box.style.marginBottom = Gap;
                if (i == selected && has) UIKit.Border(box, 3f, UIPalette.Coral);
                if (has)
                {
                    var d = st.Def;
                    if (st.count > 1) box.Add(Corner(st.count.ToString(), false));
                    if (d.HasDurability) box.Add(DurBar(st.durability / Mathf.Max(1f, d.durability)));
                    if (Hands.FindId(id, out int bar)) box.Add(Corner(GameInput.BindingLabel(GameInput.SlotAction(bar)), true));
                }
                int idx = i;
                box.RegisterCallback<PointerDownEvent>(e =>
                {
                    var cur = Backpack.Stacks;
                    var s = idx < cur.Count ? cur[idx] : null;
                    if (s == null || s.count <= 0) return;
                    if (e.button == 1)
                    {
                        // right-click: use / eat / drink right away
                        var r = Backpack.Use(idx);
                        Feedback(r, s.Def);
                        e.StopPropagation();
                        return;
                    }
                    BeginDrag(e, DragKind.Grid, idx, s.id, box);
                });
                RegisterDragEvents(box);
                box.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    hoverGrid = idx;
                    var cur = Backpack.Stacks;
                    var s = idx < cur.Count ? cur[idx] : null;
                    hoverId = s != null && s.count > 0 ? s.id : null;
                    footer.text = s != null && s.count > 0 && s.Def != null ? s.Def.Name + " – " + s.Def.Description : HintLine();
                });
                box.RegisterCallback<PointerLeaveEvent>(_ => { if (hoverGrid == idx) { hoverGrid = -1; hoverId = null; } });
                gridHost.Add(box);
                gridBoxes.Add(box);
            }
        }

        /// A dark rounded slot with the item icon (dimmed = not usable right now).
        static VisualElement SlotBox(string id, float size, bool dim, bool dark)
        {
            var box = UIKit.Box();
            UIKit.Size(box, size, size);
            UIKit.Radius(box, 14f);
            UIKit.Center(box);
            box.style.backgroundColor = dark ? new Color(1f, 1f, 1f, 0.08f) : new Color(0.10f, 0.13f, 0.22f, 0.82f);
            UIKit.Border(box, 2f, new Color(1f, 1f, 1f, dark ? 0.45f : 0.12f));
            var draw = UIGlyphs.ForItemId(id);
            if (draw != null)
            {
                Color tint = UIGlyphs.TintFor(id);
                tint.a = dim ? 0.35f : 1f;
                box.Add(new UIGlyph(draw, tint, size * 0.72f));
            }
            return box;
        }

        static Label Corner(string text, bool topRight)
        {
            var l = UIKit.Text(text, 15f, Color.white, true);
            UIKit.Outline(l, 1.2f);
            l.style.position = Position.Absolute;
            if (topRight) { l.style.right = 7f; l.style.top = 3f; l.style.color = UIPalette.Sun; }
            else { l.style.right = 7f; l.style.bottom = 3f; }
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        static VisualElement DurBar(float v01)
        {
            v01 = Mathf.Clamp01(float.IsNaN(v01) ? 0f : v01);
            var track = UIKit.Box();
            track.style.position = Position.Absolute;
            track.style.left = 10f; track.style.right = 10f; track.style.bottom = 6f;
            track.style.height = 5f;
            UIKit.Radius(track, 2.5f);
            track.style.backgroundColor = new Color(0f, 0f, 0f, 0.4f);
            var fill = UIKit.Box();
            fill.style.height = 5f;
            fill.style.width = Length.Percent(v01 * 100f);
            UIKit.Radius(fill, 2.5f);
            fill.style.backgroundColor = v01 < 0.25f ? UIPalette.Danger : v01 < 0.5f ? UIPalette.Warn : UIPalette.Leaf;
            track.Add(fill);
            track.pickingMode = PickingMode.Ignore;
            fill.pickingMode = PickingMode.Ignore;
            return track;
        }

        // ------------------------------------------------------------------ tabs
        void BuildTabs()
        {
            tabRow.Clear();
            AddTab(Tab.Item, UIGlyphs.Bag, Loc.Tr("Gegenstand", "Item"));
            AddTab(Tab.Gear, UIGlyphs.Gear, Loc.Tr("Ausrüstung", "Gear"));
            AddTab(Tab.Cooler, UIGlyphs.Fish, Loc.Tr("Kühlbox", "Cooler") + " " + FishMarket.InventoryCount);
            AddTab(Tab.Collection, UIGlyphs.Shell, Loc.Tr("Sammlung", "Collection"));
        }

        void AddTab(Tab t, UIGlyph.DrawFn icon, string text)
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
            UIKit.Clickable(b, () => { tab = t; if (t != Tab.Collection) showMap = false; dirty = true; },
                sel ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.06f), sel ? UIPalette.Coral : UIPalette.A(UIPalette.Ink, 0.12f));
            tabRow.Add(b);
        }

        void BuildPanel()
        {
            panel.Clear();
            switch (tab)
            {
                case Tab.Item: BuildItemPanel(); break;
                case Tab.Gear: BuildGearPanel(); break;
                case Tab.Cooler: BuildCoolerPanel(); break;
                default: BuildCollectionPanel(); break;
            }
        }

        // ---- item details
        void BuildItemPanel()
        {
            var l = Backpack.Stacks;
            InvStack st = selected >= 0 && selected < l.Count ? l[selected] : null;
            if (st != null && (st.count <= 0 || st.Def == null)) st = null;
            string id = st != null ? st.id : selectedId;
            var d = ItemDatabase.Get(id);
            if (d == null)
            {
                panel.Add(UIWidgets.Notice(Loc.Tr("Gegenstand anklicken für Infos. Rechtsklick = benutzen / essen.", "Click an item for details. Right-click = use / eat."), UIPalette.Sky, UIGlyphs.Bag, 16f));
                return;
            }
            var head = UIKit.Row();
            var big = UIKit.Box();
            UIKit.Size(big, 120f, 120f);
            UIKit.Radius(big, 22f);
            UIKit.Center(big);
            big.style.backgroundColor = new Color(0.10f, 0.13f, 0.22f, 0.9f);
            var draw = UIGlyphs.ForItemId(id);
            if (draw != null) big.Add(new UIGlyph(draw, UIGlyphs.TintFor(id), 96f));
            head.Add(big);
            var info = UIKit.Col();
            info.style.marginLeft = 16f;
            info.style.flexShrink = 1;
            var title = UIKit.Text(d.Name + (st != null && st.count > 1 ? "  ×" + st.count : ""), 26f, UIPalette.Ink, true);
            info.Add(title);
            var desc = UIKit.Text(d.Description, 17f, UIPalette.InkSoft);
            desc.style.whiteSpace = WhiteSpace.Normal;
            desc.style.marginTop = 4f;
            info.Add(desc);
            head.Add(info);
            panel.Add(head);

            // stats
            var stats = UIKit.Col();
            stats.style.marginTop = 14f;
            Survival.ValuesOf(d, out float hu, out float th);
            if (hu > 0f) stats.Add(StatLine(UIGlyphs.Hunger, UIPalette.Sun, "+" + Mathf.RoundToInt(hu) + Loc.Tr(" Hunger", " food")));
            if (th > 0f) stats.Add(StatLine(UIGlyphs.Thirst, UIPalette.Sky, "+" + Mathf.RoundToInt(th) + Loc.Tr(" Durst", " water")));
            var f = d.Food;
            if (f != null && f.value > 0f) stats.Add(StatLine(UIGlyphs.Sparkle, UIPalette.Violet, FoodSpots.BuffChip(f)));
            if (d.category == ItemCategory.Raw) stats.Add(StatLine(UIGlyphs.Smoke, UIPalette.Warn, Loc.Tr("Roh – am Lagerfeuer oder Grill braten", "Raw – cook it at a campfire or grill")));
            if (d.value > 0) stats.Add(StatLine(UIGlyphs.Coin, UIPalette.Sun, Loc.Tr("Wert ", "Value ") + Loc.Money(d.value) + Loc.Tr(" · Fischmarkt-App → Funde", " · fish market app → finds")));
            if (d.IsWeapon)
            {
                bool under = Refs.Player != null && Refs.Player.IsUnderwater;
                stats.Add(StatLine(UIGlyphs.SharkFin, UIPalette.Coral, Loc.Tr("Wirkung ", "Power ") + Mathf.RoundToInt(Combat.Damage(d, st, under))
                    + (d.underwater && !d.onLand ? Loc.Tr(" · nur unter Wasser", " · under water only") : d.onLand && d.hold == HoldItem.Stick ? Loc.Tr(" · an Land", " · on land") : "")));
                stats.Add(StatLine(UIGlyphs.Hand, UIPalette.Turquoise, Loc.Tr("Abwehr ", "Block ") + Mathf.RoundToInt(d.defense * 100f) + " %  (" + GameInput.BindingLabel(GameAction.Aim) + ")"));
                if (d.HasDurability && st != null)
                    stats.Add(StatLine(UIGlyphs.Gear, UIPalette.Leaf, Loc.Tr("Haltbarkeit ", "Durability ") + Mathf.CeilToInt(st.durability) + "/" + d.durability));
                if (d.Sharpenable && st != null)
                    stats.Add(StatLine(UIGlyphs.Sparkle, UIPalette.Sun, Loc.Tr("Schärfe ", "Sharpness ") + Mathf.RoundToInt(Mathf.Max(0f, st.sharpness) * 100f) + " %"
                        + (Combat.GrindLearned ? Loc.Tr(" · Schleifgerät in der Hütte", " · grinder in the hut") : Loc.Tr(" · Schleifen lernen?", " · learn to grind?"))));
                if (d.twoHanded) stats.Add(StatLine(UIGlyphs.Hand, UIPalette.InkSoft, Loc.Tr("Beidhändig", "Two-handed")));
            }
            panel.Add(stats);

            // actions
            var acts = UIKit.Row();
            acts.style.flexWrap = Wrap.Wrap;
            acts.style.marginTop = 16f;
            string use = UseLabel(d);
            if (use != null && st != null)
            {
                int idx = selected;
                acts.Add(ActionBtn(use, () => Feedback(Backpack.Use(idx), d), UIBtnKind.Primary));
            }
            if (d.hold != HoldItem.None && (st != null || d.isGear))
            {
                acts.Add(ActionBtn(Loc.Tr("In die Haupthand", "Main hand"), () => { if (Hands.Hold(id, Hand.Right)) UIAudio.Click(); }, UIBtnKind.Secondary));
                if (!d.twoHanded) acts.Add(ActionBtn(Loc.Tr("In die Zweithand", "Offhand"), () => { if (Hands.Hold(id, Hand.Left)) UIAudio.Click(); }, UIBtnKind.Secondary));
            }
            if (st != null && !d.isGear)
            {
                int idx = selected;
                acts.Add(ActionBtn(Loc.Tr("Wegwerfen", "Drop"), () => { Backpack.Drop(idx, false); UIAudio.Click(); }, UIBtnKind.Danger));
                if (st.count > 1) acts.Add(ActionBtn(Loc.Tr("Alle wegwerfen", "Drop all"), () => { Backpack.Drop(idx, true); UIAudio.Click(); }, UIBtnKind.Ghost));
            }
            panel.Add(acts);
        }

        static string UseLabel(ItemDef d)
        {
            switch (d.category)
            {
                case ItemCategory.Food: return Loc.Tr("Essen", "Eat");
                case ItemCategory.Drink: return Loc.Tr("Trinken", "Drink");
                case ItemCategory.MapPiece: return Loc.Tr("Karte ansehen", "View map");
            }
            if (d.id == ItemIds.BottleEmpty) return Loc.Tr("Meerwasser abfüllen", "Fill with seawater");
            return null;
        }

        void Feedback(UseResult r, ItemDef d)
        {
            switch (r)
            {
                case UseResult.Eaten: case UseResult.Drunk: UIAudio.Pop(); break;
                case UseResult.Filled: UIAudio.Click(); break;
                case UseResult.NotHere: case UseResult.NeedsCooking: case UseResult.NeedsFilter: UIAudio.Nope(); break;
            }
            dirty = true;
        }

        static VisualElement StatLine(UIGlyph.DrawFn icon, Color color, string text)
        {
            var row = UIKit.Row();
            row.style.marginBottom = 6f;
            row.Add(new UIGlyph(icon, color, 24f));
            var l = UIKit.Text(text, 17f, UIPalette.Ink, true);
            l.style.marginLeft = 8f;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1;
            row.Add(l);
            return row;
        }

        static Button ActionBtn(string text, System.Action onClick, UIBtnKind kind)
        {
            var b = UIKit.Btn(text, onClick, kind, 16f);
            b.style.marginRight = 8f;
            b.style.marginBottom = 8f;
            return b;
        }

        // ---- gear
        void BuildGearPanel()
        {
            // the three hand-held gear items (drag onto the hotbar / a hand, click = details)
            panel.Add(SectionLabel(UIGlyphs.Hand, Loc.Tr("In die Hand", "To hold")));
            var shelf = UIKit.Row();
            for (int i = 0; i < HoldGear.Length; i++)
            {
                string id = HoldGear[i];
                bool owned = Hands.OwnsId(id);
                var box = SlotBox(id, BarSlot + 6f, !owned, false);
                box.style.marginRight = 10f;
                bool onBar = Hands.FindId(id, out int bar);
                if (onBar) box.Add(Corner(GameInput.BindingLabel(GameInput.SlotAction(bar)), true));
                if (!owned)
                {
                    var lockG = new UIGlyph(UIGlyphs.Lock, UIPalette.Sun, 22f);
                    lockG.style.position = Position.Absolute;
                    lockG.style.right = 6f; lockG.style.bottom = 6f;
                    box.Add(lockG);
                }
                string gid = id;
                box.RegisterCallback<PointerDownEvent>(e => { if (e.button == 0) BeginDrag(e, DragKind.Gear, -1, gid, box); });
                RegisterDragEvents(box);
                box.RegisterCallback<PointerEnterEvent>(_ => { hoverId = gid; footer.text = ItemDatabase.Get(gid).Name + " – " + ItemDatabase.Get(gid).Description; });
                box.RegisterCallback<PointerLeaveEvent>(_ => { if (hoverId == gid) hoverId = null; });
                shelf.Add(box);
            }
            panel.Add(shelf);

            panel.Add(SectionLabel(UIGlyphs.Gear, Loc.Tr("Ausrüstung", "Gear")));
            var gm = GameManager.I;
            var st = gm != null ? gm.State : null;
            for (int k = 0; k < GearSlots.Length; k++)
            {
                var slot = GearSlots[k];
                GearDatabase.OwnedInSlot(st, slot, tmpItems);
                if (tmpItems.Count == 0 && (slot == GearSlot.CameraMod || slot == GearSlot.Radio)) continue;
                var row = UIKit.Row();
                row.style.marginBottom = 10f;
                row.style.flexWrap = Wrap.Wrap;
                var head = UIWidgets.IconTile(UIGlyphs.ForSlot(slot), UIAppShop.SlotColor(slot), 52f);
                head.style.marginRight = 14f;
                row.Add(head);
                var equipped = Gear.Equipped(slot);
                if (tmpItems.Count == 0) row.Add(UIKit.Text(Loc.Tr("Nichts", "None"), 17f, UIPalette.Silhouette));
                for (int i = 0; i < tmpItems.Count; i++)
                {
                    var g = tmpItems[i];
                    bool eq = equipped != null && equipped.id == g.id;
                    row.Add(GearTile(g, eq, UIAppShop.SlotColor(slot)));
                }
                panel.Add(row);
            }
        }

        VisualElement GearTile(GearItem g, bool equipped, Color color)
        {
            var tile = UIKit.Box();
            UIKit.Size(tile, 78f, 78f);
            UIKit.Radius(tile, 14f);
            UIKit.Center(tile);
            tile.style.marginRight = 8f;
            UIKit.Border(tile, equipped ? 3f : 2f, equipped ? UIPalette.Leaf : UIPalette.A(UIPalette.Ink, 0.10f));
            tile.Add(new UIGlyph(UIGlyphs.ForSlot(g.slot), UIWidgets.Ink(color), 44f));
            var tier = UIKit.Text(g.tier <= 0 ? "" : TierRoman(g.tier), 15f, UIPalette.InkSoft, true);
            tier.style.position = Position.Absolute;
            tier.style.left = 7f; tier.style.top = 3f;
            tile.Add(tier);
            if (equipped)
            {
                var ok = UIKit.Box();
                UIKit.Size(ok, 26f, 26f);
                UIKit.Radius(ok, 13f);
                ok.style.backgroundColor = UIPalette.LeafInk;
                ok.style.position = Position.Absolute;
                ok.style.right = -6f; ok.style.top = -6f;
                UIKit.Center(ok);
                ok.Add(new UIGlyph(UIGlyphs.Check, Color.white, 18f));
                tile.Add(ok);
            }
            UIKit.IgnorePicking(tile);
            tile.pickingMode = PickingMode.Position;
            UIKit.Clickable(tile, () =>
            {
                if (Gear.Equip(g.id)) footer.text = Loc.Tr("Ausgerüstet: ", "Equipped: ") + g.Name;
            }, equipped ? UIPalette.A(UIPalette.Leaf, 0.22f) : Color.white, UIPalette.CardAlt);
            tile.RegisterCallback<PointerEnterEvent>(_ => footer.text = g.Name + " – " + g.Description);
            return tile;
        }

        static string TierRoman(int t)
        {
            switch (t) { case 1: return "I"; case 2: return "II"; case 3: return "III"; case 4: return "IV"; case 5: return "V"; default: return t.ToString(); }
        }

        // ---- cooler (shared fish)
        void BuildCoolerPanel()
        {
            var gm = GameManager.I;
            var list = gm != null ? gm.State.fishInventory : null;
            if (list == null || list.Count == 0)
            {
                panel.Add(UIWidgets.Notice(Loc.Tr("Kühlbox leer – angeln gehen!", "Cooler empty – go fishing!"), UIPalette.Sky, UIGlyphs.Fish, 16f));
                return;
            }
            panel.Add(UIWidgets.Notice(Loc.Tr("Gefangene Fische verkauft das Handy (Fischmarkt). Braten: als rohen Fisch in den Rucksack nehmen.",
                "The phone sells caught fish (fish market). To cook one, take it into the backpack as raw fish."), UIPalette.Sky, UIGlyphs.Market, 15f));
            int shown = 0;
            for (int i = list.Count - 1; i >= 0 && shown < 40; i--, shown++)
            {
                var f = list[i];
                if (f == null) continue;
                var row = UIKit.Row();
                row.style.marginTop = 8f;
                row.style.backgroundColor = Color.white;
                UIKit.Radius(row, 14f);
                UIKit.Pad(row, 8f, 12f, 8f, 12f);
                row.Add(new UIGlyph(UIGlyphs.ItemRawFish, Color.white, 40f));
                var name = UIKit.Text(f.Name, 17f, UIPalette.Ink, true);
                name.style.marginLeft = 10f;
                name.style.flexGrow = 1;
                name.style.flexShrink = 1;
                row.Add(name);
                row.Add(UIKit.Text(f.weightKg.ToString("0.0", Loc.Culture) + " kg · " + Loc.Money(f.value), 16f, UIPalette.InkSoft, true));
                var fish = f;
                var take = UIKit.Btn(Loc.Tr("Zum Braten", "To cook"), () => { if (Pantry.TakeFishToBackpack(fish)) UIAudio.Click(); else UIAudio.Nope(); }, UIBtnKind.Secondary, 15f);
                take.style.marginLeft = 10f;
                row.Add(take);
                panel.Add(row);
            }
        }

        // ---- collection + treasure map
        void BuildCollectionPanel()
        {
            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            AddCollectible(grid, ItemIds.Shell, CollectibleKind.GoldenShell);
            AddCollectible(grid, ItemIds.Page, CollectibleKind.LogbookPage);
            AddCollectible(grid, ItemIds.Message, CollectibleKind.BottleMessage);
            AddCollectible(grid, ItemIds.Treasure, CollectibleKind.Treasure);
            AddCollectible(grid, ItemIds.MapPiece, CollectibleKind.MapPiece);
            panel.Add(grid);

            panel.Add(SectionLabel(UIGlyphs.Flag, Loc.Tr("Schatzkarte", "Treasure map") + "  " + MapPieces.Found + "/" + MapPieces.Count));
            var map = new UITreasureMapView();
            map.style.height = 300f;
            map.style.marginBottom = 10f;
            panel.Add(map);
            var acts = UIKit.Row();
            if (MapPieces.Complete)
            {
                acts.Add(ActionBtn(Loc.Tr("X markieren", "Mark the X"), () => { if (MapPieces.MarkSecret()) { UIAudio.Pop(); footer.text = Loc.Tr("Ziel gesetzt – folge der Fahne!", "Waypoint set – follow the flag!"); } }, UIBtnKind.Primary));
                if (StoryFlags.Has(StoryKeys.TreasureFound)) acts.Add(StatLine(UIGlyphs.Check, UIPalette.Leaf, Loc.Tr("Schatz gefunden!", "Treasure found!")));
            }
            else acts.Add(StatLine(UIGlyphs.Question, UIPalette.InkSoft, Loc.Tr("Kartenstücke liegen überall auf der Insel – manche sind vergraben.", "Map pieces lie all over the island – some are buried.")));
            panel.Add(acts);
            if (showMap) footer.text = MapPieces.Complete ? Loc.Tr("Die Karte ist komplett!", "The map is complete!") : Loc.Tr("Noch ", "Still ") + (MapPieces.Count - MapPieces.Found) + Loc.Tr(" Stücke fehlen", " pieces missing");
        }

        void AddCollectible(VisualElement grid, string id, CollectibleKind kind)
        {
            var tile = UIKit.Col();
            tile.style.alignItems = Align.Center;
            tile.style.width = 108f;
            tile.style.marginBottom = 10f;
            int mine = Backpack.Count(id);
            var box = SlotBox(mine > 0 ? id : null, 84f, false, false);
            if (mine <= 0) box.Add(new UIGlyph(UIGlyphs.ForItemId(id), new Color(1f, 1f, 1f, 0.25f), 60f));
            if (mine > 0) box.Add(Corner(mine.ToString(), false));
            tile.Add(box);
            int have = Collectibles.Count(kind), total = Collectibles.Total(kind);
            var l = UIKit.Text(have + (total > 0 ? "/" + total : ""), 16f, UIPalette.Ink, true);
            l.style.marginTop = 4f;
            tile.Add(l);
            var d = ItemDatabase.Get(id);
            tile.RegisterCallback<PointerEnterEvent>(_ => footer.text = (d != null ? d.Name : "") + Loc.Tr(" – du: ", " – you: ") + mine + Loc.Tr(" · Kanal: ", " · channel: ") + have);
            grid.Add(tile);
        }

        // ------------------------------------------------------------------ drag & drop
        void RegisterDragEvents(VisualElement e)
        {
            e.RegisterCallback<PointerMoveEvent>(OnDragMove);
            e.RegisterCallback<PointerUpEvent>(OnDragUp);
            e.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragPointer >= 0) EndDrag(); });
        }

        void BeginDrag(PointerDownEvent e, DragKind kind, int index, string id, VisualElement source)
        {
            if (e.button != 0) return;
            dragKind = kind;
            dragIndex = index;
            dragId = id;
            dragPointer = e.pointerId;
            dragStart = e.position;
            dragSource = source;
            dragging = false;
            source.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnDragMove(PointerMoveEvent e)
        {
            if (dragPointer != e.pointerId || dragSource == null) return;
            Vector2 pos = e.position;
            if (!dragging)
            {
                if (string.IsNullOrEmpty(dragId) || dragId == ItemIds.None || (pos - dragStart).sqrMagnitude < 36f) return;
                dragging = true;
                ghostIcon.SetDraw(UIGlyphs.ForItemId(dragId));
                ghostIcon.SetColor(UIGlyphs.TintFor(dragId));
                ghost.style.display = DisplayStyle.Flex;
            }
            Vector2 local = root.WorldToLocal(pos);
            ghost.style.left = local.x;
            ghost.style.top = local.y;
            for (int i = 0; i < barBoxes.Count; i++)
                barBoxes[i].style.backgroundColor = barBoxes[i].worldBound.Contains(pos) ? UIPalette.A(UIPalette.Turquoise, 0.45f) : new Color(1f, 1f, 1f, 0.08f);
            for (int h = 0; h < 2; h++)
                handBoxes[h].style.backgroundColor = handBoxes[h].worldBound.Contains(pos) ? UIPalette.A(UIPalette.Turquoise, 0.55f) : new Color(0.10f, 0.13f, 0.22f, 0.86f);
        }

        void OnDragUp(PointerUpEvent e)
        {
            if (dragPointer != e.pointerId) return;
            var src = dragSource;
            var kind = dragKind;
            int index = dragIndex;
            string id = dragId;
            bool was = dragging;
            Vector2 pos = e.position;
            EndDrag();
            if (src != null && src.HasPointerCapture(e.pointerId)) src.ReleasePointer(e.pointerId);
            if (was) Drop(kind, index, id, pos);
            else Click(kind, index, id);
            dirty = true;
        }

        void EndDrag()
        {
            dragPointer = -1;
            dragSource = null;
            dragging = false;
            dragKind = DragKind.None;
            dragId = null;
            if (ghost != null) ghost.style.display = DisplayStyle.None;
            for (int i = 0; i < barBoxes.Count; i++) barBoxes[i].style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
            for (int h = 0; h < 2; h++) if (handBoxes[h] != null) handBoxes[h].style.backgroundColor = new Color(0.10f, 0.13f, 0.22f, 0.86f);
        }

        void Click(DragKind kind, int index, string id)
        {
            switch (kind)
            {
                case DragKind.Grid:
                    selected = index;
                    selectedId = id;
                    tab = Tab.Item;
                    UIAudio.Click();
                    break;
                case DragKind.Bar:
                case DragKind.Gear:
                    if (string.IsNullOrEmpty(id) || id == ItemIds.None) return;
                    selected = -1;
                    var st = Backpack.First(id);
                    if (st != null) selected = Backpack.IndexOf(st);
                    selectedId = id;
                    tab = Tab.Item;
                    UIAudio.Click();
                    break;
            }
        }

        void Drop(DragKind kind, int index, string id, Vector2 pos)
        {
            if (string.IsNullOrEmpty(id) || id == ItemIds.None) return;
            // onto a hand: hold it
            for (int h = 0; h < 2; h++)
            {
                if (!handBoxes[h].worldBound.Contains(pos)) continue;
                if (Hands.ItemOf(id) == HoldItem.None) { UIAudio.Nope(); return; }
                if (Hands.Hold(id, h == 0 ? Hand.Right : Hand.Left)) UIAudio.Click();
                else UIAudio.Nope();
                return;
            }
            // onto a hotbar slot
            for (int i = 0; i < barBoxes.Count; i++)
            {
                if (!barBoxes[i].worldBound.Contains(pos)) continue;
                if (kind == DragKind.Bar) { if (index != i) Hands.Swap(index, i); }
                else if (Hands.ItemOf(id) != HoldItem.None) Hands.Assign(i, id);
                else { UIAudio.Nope(); return; }
                UIAudio.Click();
                return;
            }
            // onto a backpack slot
            for (int i = 0; i < gridBoxes.Count; i++)
            {
                if (!gridBoxes[i].worldBound.Contains(pos)) continue;
                if (kind == DragKind.Grid) { if (index != i) { Backpack.Move(index, i); if (selected == index) selected = i; } }
                else if (kind == DragKind.Bar) Hands.Assign(index, ItemIds.None);
                UIAudio.Click();
                return;
            }
            // dragged a hotbar slot out of the window: empty it
            if (kind == DragKind.Bar && (!card.worldBound.Contains(pos) || gridHost.worldBound.Contains(pos)))
            {
                Hands.Assign(index, ItemIds.None);
                UIAudio.Click();
            }
        }
    }

    /// The treasure map (round 3): a parchment with the island, cut into MapPieces.Count tiles – found pieces show the
    /// map, missing ones stay blank with a "?"; the complete map shows the red X (MapPieces.SecretSpot).
    public class UITreasureMapView : VisualElement
    {
        const int Cols = 4, Rows = 2;
        static readonly Color Paper = new Color(0.92f, 0.85f, 0.66f, 1f), PaperDark = new Color(0.78f, 0.69f, 0.5f, 1f);
        static readonly Color Sea = new Color(0.55f, 0.72f, 0.74f, 1f), Land = new Color(0.6f, 0.75f, 0.45f, 1f), Ink = new Color(0.3f, 0.22f, 0.14f, 1f);

        public UITreasureMapView()
        {
            pickingMode = PickingMode.Ignore;
            style.flexShrink = 0;
            UIKit.Radius(this, 14f);
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 10f || r.height < 10f) return;
            var p = ctx.painter2D;
            // parchment
            p.fillColor = PaperDark;
            p.BeginPath(); p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax)); p.ClosePath(); p.Fill();
            var inner = new Rect(r.x + 6f, r.y + 6f, r.width - 12f, r.height - 12f);
            float cw = inner.width / Cols, ch = inner.height / Rows;
            Vector2 c = inner.center;
            float rad = Mathf.Min(inner.width, inner.height) * 0.36f;
            for (int i = 0; i < Cols * Rows; i++)
            {
                int cx = i % Cols, cy = i / Cols;
                var cell = new Rect(inner.x + cx * cw, inner.y + cy * ch, cw - 2f, ch - 2f);
                bool found = MapPieces.IsFound(i);
                p.fillColor = found ? Sea : Paper;
                Rect(p, cell);
                if (!found)
                {
                    p.strokeColor = PaperDark;
                    p.lineWidth = 3f;
                    p.BeginPath(); p.Arc(cell.center + new Vector2(0f, -6f), 10f, Angle.Degrees(200f), Angle.Degrees(400f)); p.Stroke();
                    p.fillColor = PaperDark;
                    p.BeginPath(); p.Arc(cell.center + new Vector2(0f, 14f), 3f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
                    continue;
                }
                // the island blob clipped to this cell: sample points of an irregular circle
                p.fillColor = Land;
                p.BeginPath();
                bool first = true;
                for (int k = 0; k <= 36; k++)
                {
                    float a = k / 36f * Mathf.PI * 2f;
                    float rr = rad * (1f + 0.12f * Mathf.Sin(a * 3f + 0.7f) + 0.06f * Mathf.Sin(a * 7f));
                    Vector2 q = c + new Vector2(Mathf.Cos(a) * rr * 1.5f, Mathf.Sin(a) * rr);
                    q.x = Mathf.Clamp(q.x, cell.xMin, cell.xMax);
                    q.y = Mathf.Clamp(q.y, cell.yMin, cell.yMax);
                    if (first) { p.MoveTo(q); first = false; } else p.LineTo(q);
                }
                p.ClosePath();
                p.Fill();
            }
            // dotted path + the X once complete
            if (MapPieces.Complete)
            {
                Vector3 s = MapPieces.SecretSpot;
                float nx = Mathf.Clamp(s.x / (WorldLayout.IslandRadius * 1.3f), -1f, 1f), nz = Mathf.Clamp(s.z / (WorldLayout.IslandRadius * 1.3f), -1f, 1f);
                Vector2 x = c + new Vector2(nx * rad * 1.5f, -nz * rad);
                p.strokeColor = new Color(0.75f, 0.22f, 0.17f, 1f);
                p.lineWidth = 6f;
                p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(x + new Vector2(-12f, -12f)); p.LineTo(x + new Vector2(12f, 12f)); p.Stroke();
                p.BeginPath(); p.MoveTo(x + new Vector2(12f, -12f)); p.LineTo(x + new Vector2(-12f, 12f)); p.Stroke();
                p.strokeColor = Ink;
                p.lineWidth = 3f;
                Vector2 hut = c + new Vector2(Mathf.Clamp(WorldLayout.Hut.x / (WorldLayout.IslandRadius * 1.3f), -1f, 1f) * rad * 1.5f, -Mathf.Clamp(WorldLayout.Hut.z / (WorldLayout.IslandRadius * 1.3f), -1f, 1f) * rad);
                for (int k = 1; k < 10; k++)
                {
                    Vector2 q = Vector2.Lerp(hut, x, k / 10f) + new Vector2(0f, Mathf.Sin(k * 1.3f) * 8f);
                    p.BeginPath(); p.Arc(q, 2.2f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
                }
            }
        }

        static void Rect(Painter2D p, Rect r)
        {
            p.BeginPath();
            p.MoveTo(new Vector2(r.xMin, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMin)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.LineTo(new Vector2(r.xMin, r.yMax));
            p.ClosePath();
            p.Fill();
        }
    }
}
