using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Inventory / equip screen (GameAction.Inventory, default I). Blocks gameplay while open (UIState.InventoryOpen),
    /// the world keeps running.
    ///  - hands: the two hotbars (left hand | right hand, 3 slots each); drag items between slots, drag a slot onto the
    ///    item shelf to empty it (Hands.Assign)
    ///  - item shelf: camera, lamp, fishing rod – drag onto a slot, or click to put it into the first free slot
    ///  - gear: owned items per gear slot; click to equip (Gear.Equip, per player)
    public class UIInventory
    {
        static readonly GearSlot[] GearSlots = { GearSlot.Camera, GearSlot.Lamp, GearSlot.Rod, GearSlot.Tank, GearSlot.Fins, GearSlot.Boat, GearSlot.CameraMod, GearSlot.Radio };
        static readonly HoldItem[] ShelfItems = { HoldItem.Camera, HoldItem.Flashlight, HoldItem.FishingRod };
        const float SlotSize = 84f;

        class HandSlot
        {
            public Hand hand;
            public int index;
            public VisualElement box;
            public UIGlyph icon;
            public Label key;
        }

        readonly VisualElement root, card, ghost;
        VisualElement shelf, gearHost;
        readonly UIGlyph ghostIcon;
        Label footer;
        readonly List<HandSlot> slots = new List<HandSlot>();
        readonly List<GearItem> tmpItems = new List<GearItem>();
        bool dirty;
        float anim = 1f;

        // drag state
        HoldItem dragItem;
        bool dragFromSlot, dragging;
        Hand dragHand;
        int dragIndex = -1, dragPointer = -1;
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

            card = UIWidgets.ModalCard(1400f);
            card.style.backgroundColor = UIPalette.A(UIPalette.Card, 0.94f);
            UIKit.Radius(card, 24f);
            root.Add(card);
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
            GameEvents.GearChanged += MarkDirty;
            GameInput.BindingsChanged += MarkDirty;
        }

        /// Language changed: rebuild the static texts.
        public void RefreshTexts()
        {
            card.Clear();
            slots.Clear();
            BuildFrame();
            if (UIState.InventoryOpen) Rebuild();
        }

        void BuildFrame()
        {
            card.Add(UIWidgets.Header(UIGlyphs.Bag, UIPalette.Coral, Loc.Tr("Inventar", "Inventory"), Close, 34f));

            var cols = UIKit.Row();
            cols.style.alignItems = Align.FlexStart;
            card.Add(cols);

            // ---- left: hands + item shelf
            var left = UIKit.Col();
            left.style.width = 640f;
            left.style.flexShrink = 0;
            cols.Add(left);
            left.Add(SectionLabel(UIGlyphs.Hand, Loc.Tr("Hände", "Hands")));
            var hands = UIKit.Row();
            hands.style.alignItems = Align.FlexStart;
            hands.style.backgroundColor = new Color(0.10f, 0.13f, 0.22f, 0.82f);
            UIKit.Radius(hands, 20f);
            UIKit.Pad(hands, 16f, 18f, 14f, 18f);
            hands.Add(HandGroup(Hand.Left));
            hands.Add(UIKit.HSpacer(36f));
            hands.Add(HandGroup(Hand.Right));
            left.Add(hands);

            left.Add(SectionLabel(UIGlyphs.Bag, Loc.Tr("Gegenstände", "Items")));
            shelf = UIKit.Row();
            shelf.style.backgroundColor = Color.white;
            UIKit.Radius(shelf, 20f);
            UIKit.Pad(shelf, 14f);
            left.Add(shelf);
            var hint = UIKit.Row();
            hint.style.marginTop = 10f;
            hint.Add(new UIGlyph(UIGlyphs.Hand, UIPalette.Silhouette, 22f));
            var hintText = UIKit.Text(Loc.Tr("Ziehen & ablegen", "Drag & drop"), 16f, UIPalette.InkSoft);
            hintText.style.marginLeft = 8f;
            hint.Add(hintText);
            left.Add(hint);

            // ---- right: gear
            var right = UIKit.Col();
            right.style.marginLeft = 30f;
            right.style.flexGrow = 1;
            right.style.flexShrink = 1;
            cols.Add(right);
            right.Add(SectionLabel(UIGlyphs.Gear, Loc.Tr("Ausrüstung", "Gear")));
            gearHost = UIKit.Col();
            right.Add(gearHost);

            footer = UIKit.Text("", 17f, UIPalette.InkSoft);
            footer.style.marginTop = 14f;
            footer.style.minHeight = 24f;
            footer.style.whiteSpace = WhiteSpace.Normal;
            card.Add(footer);
        }

        public bool IsOpen => UIState.InventoryOpen;

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

        VisualElement HandGroup(Hand hand)
        {
            var g = UIKit.Col();
            g.style.alignItems = Align.Center;
            var top = UIKit.Row();
            var hi = new UIGlyph(UIGlyphs.Hand, new Color(1f, 1f, 1f, 0.8f), 26f);
            if (hand == Hand.Left) hi.style.scale = new Scale(new Vector2(-1f, 1f));
            top.Add(hi);
            var name = UIKit.Text(hand == Hand.Left ? Loc.Tr("Links", "Left") : Loc.Tr("Rechts", "Right"), 17f, Color.white, true);
            name.style.marginLeft = 6f;
            top.Add(name);
            top.style.marginBottom = 8f;
            g.Add(top);
            var row = UIKit.Row();
            for (int i = 0; i < Hands.SlotsPerHand; i++)
            {
                var s = new HandSlot { hand = hand, index = i };
                s.box = UIKit.Box();
                UIKit.Size(s.box, SlotSize, SlotSize);
                UIKit.Radius(s.box, 12f);
                UIKit.Center(s.box);
                if (i > 0) s.box.style.marginLeft = 10f;
                s.box.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
                UIKit.Border(s.box, 2f, new Color(1f, 1f, 1f, 0.45f));
                s.icon = new UIGlyph(null, Color.white, 64f);
                s.box.Add(s.icon);
                s.key = UIKit.Text("", 13f, Color.white, true);
                UIKit.Outline(s.key, 1.2f);
                s.key.style.position = Position.Absolute;
                s.key.style.left = 7f; s.key.style.top = 4f;
                s.key.pickingMode = PickingMode.Ignore;
                s.box.Add(s.key);
                var cs = s;
                s.box.RegisterCallback<PointerDownEvent>(e => BeginDrag(e, Hands.SlotItem(cs.hand, cs.index), true, cs.hand, cs.index, cs.box));
                RegisterDragEvents(s.box);
                s.box.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    var it = Hands.SlotItem(cs.hand, cs.index);
                    footer.text = it == HoldItem.None ? Loc.Tr("Leerer Platz", "Empty slot") : ItemInfo(it);
                    if (!dragging) cs.box.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
                });
                s.box.RegisterCallback<PointerLeaveEvent>(_ => cs.box.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f));
                row.Add(s.box);
                slots.Add(s);
            }
            g.Add(row);
            return g;
        }

        static string ItemInfo(HoldItem it)
        {
            var g = Gear.Equipped(Hands.GearSlotOf(it));
            string n = Hands.DisplayName(it);
            if (g != null) n += " – " + g.Name;
            if (Hands.IsTwoHanded(it)) n += Loc.Tr(" (beidhändig)", " (two-handed)");
            if (!Hands.Owns(it)) n += Loc.Tr(" · noch nicht gekauft", " · not owned yet");
            return n;
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
            footer.text = "";
            EndDrag(false, Vector2.zero);
            Rebuild();
            UIAudio.Pop();
        }

        public void Close()
        {
            if (!UIState.InventoryOpen && root.style.display.value == DisplayStyle.None) return;
            EndDrag(false, Vector2.zero);
            UIState.InventoryOpen = false;
            root.style.display = DisplayStyle.None;
        }

        public void Toggle()
        {
            if (UIState.InventoryOpen) Close(); else Open();
        }

        void MarkDirty() { dirty = true; }

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
            if (dirty && !dragging) Rebuild();
        }

        public void Rebuild()
        {
            dirty = false;
            if (!UIState.InventoryOpen) return;
            // hand slots
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                var item = Hands.SlotItem(s.hand, s.index);
                s.icon.SetDraw(UIGlyphs.ForItem(item));
                s.icon.SetColor(new Color(1f, 1f, 1f, item == HoldItem.None || Hands.Owns(item) ? 1f : 0.35f));
                s.key.text = GameInput.BindingLabel((GameAction)((s.hand == Hand.Right ? (int)GameAction.RightSlot1 : (int)GameAction.LeftSlot1) + s.index));
                bool active = Hands.ActiveSlot(s.hand) == s.index;
                UIKit.Border(s.box, active ? 3f : 2f, active ? Color.white : new Color(1f, 1f, 1f, 0.45f));
            }

            // shelf
            shelf.Clear();
            for (int i = 0; i < ShelfItems.Length; i++)
            {
                var item = ShelfItems[i];
                bool owned = Hands.Owns(item);
                bool onBar = Hands.Find(item, out _, out _);
                var tile = UIKit.Col();
                tile.style.alignItems = Align.Center;
                tile.style.width = 150f;
                var box = UIKit.Box();
                UIKit.Size(box, SlotSize + 8f, SlotSize + 8f);
                UIKit.Radius(box, 16f);
                UIKit.Center(box);
                box.style.backgroundColor = new Color(0.10f, 0.13f, 0.22f, onBar ? 0.5f : 0.82f);
                UIKit.Border(box, 2f, onBar ? UIPalette.A(UIPalette.Leaf, 0.9f) : new Color(1f, 1f, 1f, 0f));
                box.Add(new UIGlyph(UIGlyphs.ForItem(item), new Color(1f, 1f, 1f, owned ? 1f : 0.35f), 66f));
                if (!owned)
                {
                    var lockG = new UIGlyph(UIGlyphs.Lock, UIPalette.Sun, 22f);
                    lockG.style.position = Position.Absolute;
                    lockG.style.right = 6f; lockG.style.bottom = 6f;
                    box.Add(lockG);
                }
                else if (onBar)
                {
                    var ok = new UIGlyph(UIGlyphs.Check, UIPalette.Leaf, 22f);
                    ok.style.position = Position.Absolute;
                    ok.style.right = 6f; ok.style.bottom = 6f;
                    box.Add(ok);
                }
                tile.Add(box);
                var name = UIKit.Text(Hands.DisplayName(item), 17f, UIPalette.Ink, true);
                name.style.marginTop = 6f;
                tile.Add(name);
                UIKit.IgnorePicking(tile);
                box.pickingMode = PickingMode.Position;
                var it = item;
                box.RegisterCallback<PointerDownEvent>(e => BeginDrag(e, it, false, Hand.Right, -1, box));
                RegisterDragEvents(box);
                box.RegisterCallback<PointerEnterEvent>(_ => footer.text = ItemInfo(it));
                shelf.Add(tile);
            }

            // gear grid
            gearHost.Clear();
            var gm = GameManager.I;
            var st = gm != null ? gm.State : null;
            for (int k = 0; k < GearSlots.Length; k++)
            {
                var slot = GearSlots[k];
                GearDatabase.OwnedInSlot(st, slot, tmpItems);
                if (tmpItems.Count == 0 && (slot == GearSlot.CameraMod || slot == GearSlot.Radio)) continue;   // round-2 slots only once owned
                var row = UIKit.Row();
                row.style.marginBottom = 10f;
                row.style.flexWrap = Wrap.Wrap;
                var head = UIWidgets.IconTile(UIGlyphs.ForSlot(slot), SlotColor(slot), 52f);
                head.style.marginRight = 14f;
                row.Add(head);
                var equipped = Gear.Equipped(slot);
                if (tmpItems.Count == 0)
                {
                    row.Add(UIKit.Text(Loc.Tr("Nichts", "None"), 17f, UIPalette.Silhouette));
                }
                for (int i = 0; i < tmpItems.Count; i++)
                {
                    var g = tmpItems[i];
                    bool eq = equipped != null && equipped.id == g.id;
                    row.Add(GearTile(g, eq, SlotColor(slot)));
                }
                gearHost.Add(row);
            }
        }

        static Color SlotColor(GearSlot s)
        {
            switch (s)
            {
                case GearSlot.Camera: return UIPalette.Sun;
                case GearSlot.Lamp: return UIPalette.Warn;
                case GearSlot.Rod: return UIPalette.Sky;
                case GearSlot.Tank: return UIPalette.Turquoise;
                case GearSlot.Fins: return UIPalette.Coral;
                case GearSlot.Boat: return UIPalette.Rgb(0x9A7658);
                default: return UIPalette.Violet;
            }
        }

        VisualElement GearTile(GearItem g, bool equipped, Color color)
        {
            var tile = UIKit.Box();
            UIKit.Size(tile, SlotSize, SlotSize);
            UIKit.Radius(tile, 14f);
            UIKit.Center(tile);
            tile.style.marginRight = 8f;
            UIKit.Border(tile, equipped ? 3f : 2f, equipped ? UIPalette.Leaf : UIPalette.A(UIPalette.Ink, 0.10f));
            tile.Add(new UIGlyph(UIGlyphs.ForSlot(g.slot), UIWidgets.Ink(color), 46f));
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

        // ------------------------------------------------------------------ drag & drop
        void RegisterDragEvents(VisualElement e)
        {
            e.RegisterCallback<PointerMoveEvent>(OnDragMove);
            e.RegisterCallback<PointerUpEvent>(OnDragUp);
            e.RegisterCallback<PointerCaptureOutEvent>(_ => { if (dragPointer >= 0) EndDrag(false, Vector2.zero); });
        }

        void BeginDrag(PointerDownEvent e, HoldItem item, bool fromSlot, Hand hand, int index, VisualElement source)
        {
            if (e.button != 0) return;
            dragItem = item;
            dragFromSlot = fromSlot;
            dragHand = hand;
            dragIndex = index;
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
                if (dragItem == HoldItem.None || (pos - dragStart).sqrMagnitude < 36f) return;
                dragging = true;
                ghostIcon.SetDraw(UIGlyphs.ForItem(dragItem));
                ghost.style.display = DisplayStyle.Flex;
            }
            Vector2 local = root.WorldToLocal(pos);
            ghost.style.left = local.x;
            ghost.style.top = local.y;
            for (int i = 0; i < slots.Count; i++)
            {
                bool over = slots[i].box.worldBound.Contains(pos);
                slots[i].box.style.backgroundColor = over ? UIPalette.A(UIPalette.Turquoise, 0.45f) : new Color(1f, 1f, 1f, 0.08f);
            }
        }

        void OnDragUp(PointerUpEvent e)
        {
            if (dragPointer != e.pointerId) return;
            var src = dragSource;
            var item = dragItem;
            bool fromSlot = dragFromSlot, was = dragging;
            Hand hand = dragHand;
            int index = dragIndex;
            Vector2 pos = e.position;
            EndDrag(false, Vector2.zero);
            if (src != null && src.HasPointerCapture(e.pointerId)) src.ReleasePointer(e.pointerId);
            if (was) Drop(item, fromSlot, hand, index, pos);
            else if (!fromSlot) QuickAssign(item);
        }

        /// Resets the drag state (drop handling is done by OnDragUp).
        void EndDrag(bool unused, Vector2 unusedPos)
        {
            dragPointer = -1;
            dragSource = null;
            dragging = false;
            dragItem = HoldItem.None;
            ghost.style.display = DisplayStyle.None;
            for (int i = 0; i < slots.Count; i++) slots[i].box.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
        }

        void Drop(HoldItem item, bool fromSlot, Hand srcHand, int srcIndex, Vector2 pos)
        {
            if (item == HoldItem.None) return;
            HandSlot target = null;
            for (int i = 0; i < slots.Count; i++) if (slots[i].box.worldBound.Contains(pos)) { target = slots[i]; break; }
            if (target != null)
            {
                if (!(fromSlot && target.hand == srcHand && target.index == srcIndex)) Hands.Assign(target.hand, target.index, item);
                UIAudio.Click();
            }
            else if (fromSlot && (shelf.worldBound.Contains(pos) || !card.worldBound.Contains(pos)))
            {
                // dragged back onto the shelf (or out of the window): empty the slot
                Hands.Assign(srcHand, srcIndex, HoldItem.None);
                UIAudio.Click();
            }
            dirty = true;
        }

        /// Click on a shelf item without dragging: put it into the first free slot (right hand first).
        void QuickAssign(HoldItem item)
        {
            if (item == HoldItem.None || Hands.Find(item, out _, out _)) return;
            for (int h = 0; h < 2; h++)
            {
                var hand = h == 0 ? Hand.Right : Hand.Left;
                for (int i = 0; i < Hands.SlotsPerHand; i++)
                    if (Hands.SlotItem(hand, i) == HoldItem.None) { Hands.Assign(hand, i, item); UIAudio.Click(); return; }
            }
        }
    }
}
