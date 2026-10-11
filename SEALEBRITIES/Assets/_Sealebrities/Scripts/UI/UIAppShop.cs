using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Chopper Shop: icon category tabs, item cards with stat bars, standard (same day, +2–3 h) and express (+45 min)
    /// orders, and the current deliveries with a live ETA.
    public class UIAppShop : UIPhoneApp
    {
        static readonly GearSlot[] Slots = { GearSlot.Tank, GearSlot.Camera, GearSlot.Rod, GearSlot.Fins, GearSlot.Lamp, GearSlot.Boat, GearSlot.Submarine };
        int category;
        readonly System.Collections.Generic.List<GearSlot> visibleSlots = new System.Collections.Generic.List<GearSlot>();

        static bool HasBuyable(GearSlot slot)
        {
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++) if (all[i].slot == slot && !all[i].comingSoon) return true;
            return false;
        }

        struct LiveEta
        {
            public Delivery delivery;
            public Label label;
            public int shown;
        }

        readonly List<LiveEta> etas = new List<LiveEta>();
        readonly List<GearItem> items = new List<GearItem>();
        float tickTimer;

        public override string Id => "shop";
        public override string Title => "Chopper Shop";
        public override Color Color => UIPalette.Coral;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Heli;

        public override int Badge
        {
            get
            {
                var s = UIKit.State;
                if (s == null) return 0;
                int n = 0;
                for (int i = 0; i < s.deliveries.Count; i++) if (s.deliveries[i] != null && s.deliveries[i].dropped) n++;
                return n;
            }
        }

        static string SlotName(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Tank: return Loc.Tr("Luft", "Air");
                case GearSlot.Fins: return Loc.Tr("Flossen", "Fins");
                case GearSlot.Lamp: return Loc.Tr("Licht", "Light");
                case GearSlot.Camera: return Loc.Tr("Kamera", "Camera");
                case GearSlot.Boat: return Loc.Tr("Boot", "Boat");
                case GearSlot.Rod: return Loc.Tr("Angel", "Rod");
                default: return Loc.Tr("U-Boot", "Sub");
            }
        }

        public static Color SlotColor(GearSlot s)
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

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;
            etas.Clear();

            var bal = UIKit.Row();
            bal.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, Loc.Money(s.money)));
            bal.Add(UIKit.HSpacer(10f));
            bal.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Compact(s.followers)));
            body.Add(bal);

            if (s.deliveries.Count > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Lieferungen", "Deliveries"), UIGlyphs.Heli));
                for (int i = 0; i < s.deliveries.Count; i++) if (s.deliveries[i] != null) body.Add(DeliveryRow(s.deliveries[i]));
            }

            // category tabs (a slot whose items are all "coming soon" – the submarines – is not shown)
            visibleSlots.Clear();
            for (int i = 0; i < Slots.Length; i++) if (HasBuyable(Slots[i])) visibleSlots.Add(Slots[i]);
            if (visibleSlots.Count == 0) visibleSlots.Add(GearSlot.Tank);
            category = Mathf.Clamp(category, 0, visibleSlots.Count - 1);
            var tabs = UIKit.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginTop = 16f;
            tabs.style.marginBottom = 4f;
            for (int i = 0; i < visibleSlots.Count; i++)
            {
                int idx = i;
                bool sel = i == category;
                var sl = visibleSlots[i];
                var t = UIKit.Col();
                t.style.alignItems = Align.Center;
                t.style.width = Length.Percent(25f);
                t.style.marginBottom = 10f;
                var tile = UIWidgets.IconTile(UIGlyphs.ForSlot(sl), sel ? SlotColor(sl) : UIPalette.CardAlt, 72f);
                if (sel) UIKit.Border(tile, 3f, UIPalette.Ink);
                t.Add(tile);
                var l = UIKit.Text(SlotName(sl), PhoneKit.Tiny, sel ? UIPalette.Ink : UIPalette.InkSoft, true);
                l.style.marginTop = 4f;
                t.Add(l);
                UIKit.IgnorePicking(t);
                t.pickingMode = PickingMode.Position;
                t.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); category = idx; Phone.Refresh(false); });
                tabs.Add(t);
            }
            body.Add(tabs);

            var slot = visibleSlots[category];
            items.Clear();
            foreach (var g in GearDatabase.All) if (g.slot == slot) items.Add(g);
            items.Sort((a, b) => a.tier.CompareTo(b.tier));
            var equipped = GearDatabase.Equipped(s, slot);
            for (int i = 0; i < items.Count; i++) body.Add(ItemCard(items[i], s, equipped == items[i]));
        }

        VisualElement DeliveryRow(Delivery d)
        {
            var item = GearDatabase.Get(d.itemId);
            var row = UIKit.Row();
            row.style.backgroundColor = d.dropped ? UIPalette.A(UIPalette.Sun, 0.35f) : Color.white;
            UIKit.Radius(row, 20f);
            UIKit.Pad(row, 12f, 16f, 12f, 12f);
            row.style.marginBottom = 8f;
            row.Add(UIWidgets.IconTile(item != null ? UIGlyphs.ForSlot(item.slot) : UIGlyphs.Heli, item != null ? SlotColor(item.slot) : UIPalette.Coral, 52f));
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexShrink = 1;
            col.style.flexGrow = 1;
            col.Add(PhoneKit.T(item != null ? item.Name : d.itemId, PhoneKit.Body, UIPalette.Ink, true));
            var eta = UIKit.Text("", PhoneKit.Small, d.dropped ? UIPalette.SunInk : UIPalette.InkSoft, true);
            col.Add(eta);
            row.Add(col);
            if (d.express) row.Add(UIKit.Pill("EXPRESS", UIPalette.Coral, Color.white, PhoneKit.Tiny));
            var le = new LiveEta { delivery = d, label = eta, shown = -2 };
            UpdateEta(ref le);
            etas.Add(le);
            return row;
        }

        static void UpdateEta(ref LiveEta e)
        {
            var d = e.delivery;
            int key;
            if (d.dropped) key = -1;
            else if (Shop.IsDue(d)) key = 0;
            else key = Mathf.CeilToInt(Shop.SecondsUntil(d) / 10f);
            if (key == e.shown) return;
            e.shown = key;
            if (key == -1) e.label.text = Loc.Tr("Liegt am Helipad – abholen!", "At the helipad – pick it up!");
            else if (key == 0) e.label.text = Loc.Tr("Heli im Anflug …", "Chopper inbound …");
            else
            {
                var gm = GameManager.I;
                string day = gm != null && d.arrivalDay > gm.State.day ? Loc.Tr("morgen ", "tomorrow ") : "";
                int secs = key * 10;
                e.label.text = day + UIKit.TimeOfDay(d.arrivalTime) + " · " + Loc.Tr("in ", "in ") + UIKit.ClockShort(secs) + " min";
            }
        }

        VisualElement ItemCard(GearItem item, GameState s, bool isEquipped)
        {
            bool owned = Shop.IsOwned(item), ordered = Shop.IsOrdered(item);
            var card = PhoneKit.Card();
            if (isEquipped) UIKit.Border(card, 3f, UIPalette.LeafInk);

            var head = UIKit.Row();
            var tile = UIWidgets.IconTile(UIGlyphs.ForSlot(item.slot), item.comingSoon ? UIPalette.CardAlt : SlotColor(item.slot), 68f);
            head.Add(tile);
            var nc = UIKit.Col();
            nc.style.marginLeft = 12f;
            nc.style.flexShrink = 1;
            nc.style.flexGrow = 1;
            var name = PhoneKit.T(item.Name, PhoneKit.Body, item.comingSoon ? UIPalette.InkSoft : UIPalette.Ink, true);
            nc.Add(name);
            var desc = PhoneKit.T(item.Description, PhoneKit.Tiny, UIPalette.InkSoft);
            desc.style.marginTop = 2f;
            nc.Add(desc);
            head.Add(nc);
            card.Add(head);

            var status = UIKit.Row();
            status.style.marginTop = 8f;
            if (isEquipped) status.Add(StatusPill(UIGlyphs.Check, Loc.Tr("Ausgerüstet", "Equipped"), UIPalette.LeafInk));
            else if (owned) status.Add(StatusPill(UIGlyphs.Bag, Loc.Tr("Im Inventar", "In inventory"), UIPalette.TurquoiseInk));
            else if (ordered) status.Add(StatusPill(UIGlyphs.Heli, Loc.Tr("Unterwegs", "On its way"), UIPalette.SunInk));
            else if (item.comingSoon) status.Add(StatusPill(UIGlyphs.Clock, Loc.Tr("Bald", "Soon"), UIPalette.Violet));
            if (status.childCount > 0) card.Add(status);

            // stat bars
            var stats = UIKit.Col();
            stats.style.marginTop = 10f;
            AddStats(stats, item, s);
            card.Add(stats);

            if (owned || ordered || item.comingSoon) return card;

            var priceRow = UIKit.Row();
            priceRow.style.marginTop = 12f;
            priceRow.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 32f));
            var price = UIKit.Text(Loc.Money(item.price), PhoneKit.Big, s.money >= item.price ? UIPalette.Ink : UIPalette.CoralInk, true);
            price.style.marginLeft = 8f;
            priceRow.Add(price);
            priceRow.Add(UIKit.Flex());
            if (item.followersNeeded > 0)
            {
                bool met = s.followers >= item.followersNeeded;
                priceRow.Add(new UIGlyph(UIGlyphs.Person, met ? UIPalette.Turquoise : UIPalette.CoralInk, 26f));
                var fl = UIKit.Text(UIKit.Compact(item.followersNeeded), PhoneKit.Small, met ? UIPalette.InkSoft : UIPalette.CoralInk, true);
                fl.style.marginLeft = 4f;
                priceRow.Add(fl);
            }
            card.Add(priceRow);

            bool canNormal = Shop.CanBuy(item, false, out string reason);
            bool canExpress = Shop.CanBuy(item, true, out string reasonExpress);
            var buttons = UIKit.Row();
            buttons.style.marginTop = 12f;
            var buy = BuyButton(UIGlyphs.Heli, Loc.Tr("Bestellen", "Order"), StandardEta(s), () => { if (Shop.Buy(item, false)) { UIAudio.Pop(); Phone.Refresh(true); } }, UIBtnKind.Primary);
            buy.style.flexGrow = 1;
            buy.style.flexBasis = 0;
            UIKit.SetEnabled(buy, canNormal);
            buttons.Add(buy);
            var express = BuyButton(UIGlyphs.Clock, Loc.Money(Shop.Price(item, true)), "+45 min", () => { if (Shop.Buy(item, true)) { UIAudio.Pop(); Phone.Refresh(true); } }, UIBtnKind.Secondary);
            express.style.flexGrow = 1;
            express.style.flexBasis = 0;
            express.style.marginLeft = 10f;
            UIKit.SetEnabled(express, canExpress);
            buttons.Add(express);
            card.Add(buttons);
            if (!canNormal && !string.IsNullOrEmpty(reason))
            {
                var w = PhoneKit.T(reason, PhoneKit.Small, UIPalette.CoralInk, true);
                w.style.marginTop = 8f;
                card.Add(w);
            }
            return card;
        }

        static string StandardEta(GameState s)
        {
            if (s.timeOfDay > Shop.LastStandardOrder) return Loc.Tr("morgen ", "tomorrow ") + UIKit.TimeOfDay(Shop.MorningArrival);
            return "+2–3 h";
        }

        static Button BuyButton(UIGlyph.DrawFn icon, string text, string sub, System.Action onClick, UIBtnKind kind)
        {
            var b = PhoneKit.Button("", onClick, kind, PhoneKit.Body);
            b.style.flexDirection = FlexDirection.Row;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.Center;
            UIKit.Pad(b, 12f, 12f, 12f, 12f);
            var skin = b.userData as UIBtnSkin;
            Color fg = skin != null ? skin.fg : Color.white;
            b.Add(new UIGlyph(icon, fg, 34f));
            var col = UIKit.Col();
            col.style.marginLeft = 8f;
            col.pickingMode = PickingMode.Ignore;
            var t = UIKit.Text(text, PhoneKit.Small, fg, true);
            t.pickingMode = PickingMode.Ignore;
            col.Add(t);
            var st = UIKit.Text(sub, PhoneKit.Tiny, UIPalette.A(fg, 0.8f), false);
            st.pickingMode = PickingMode.Ignore;
            col.Add(st);
            b.Add(col);
            return b;
        }

        static VisualElement StatusPill(UIGlyph.DrawFn icon, string text, Color c)
        {
            var row = UIKit.Row();
            row.style.backgroundColor = UIPalette.A(c, 0.14f);
            UIKit.Radius(row, 999f);
            UIKit.Pad(row, 6f, 14f, 6f, 10f);
            row.Add(new UIGlyph(icon, c, 22f));
            var l = UIKit.Text(text, PhoneKit.Tiny, c, true);
            l.style.marginLeft = 6f;
            row.Add(l);
            return row;
        }

        // ------------------------------------------------------------------ stat bars
        static float MaxOf(GearSlot slot, System.Func<GearItem, float> f)
        {
            float m = 0.0001f;
            foreach (var g in GearDatabase.All) if (g.slot == slot) m = Mathf.Max(m, f(g));
            return m;
        }

        static void AddStats(VisualElement host, GearItem g, GameState s)
        {
            float airMul = DifficultySettings.For(s.difficulty).airMultiplier;
            switch (g.slot)
            {
                case GearSlot.Tank:
                    host.Add(StatBar(UIGlyphs.Bubbles, g.airSeconds / MaxOf(g.slot, x => x.airSeconds), UIKit.ClockShort(Mathf.RoundToInt(g.airSeconds * airMul)) + " min"));
                    host.Add(StatBar(UIGlyphs.Depth, g.maxDepth / MaxOf(g.slot, x => x.maxDepth), Loc.Meters(g.maxDepth)));
                    break;
                case GearSlot.Submarine:
                    host.Add(StatBar(UIGlyphs.Depth, g.maxDepth / MaxOf(g.slot, x => x.maxDepth), Loc.Meters(g.maxDepth)));
                    break;
                case GearSlot.Fins:
                    host.Add(StatBar(UIGlyphs.Wave, g.swimSpeed / MaxOf(g.slot, x => x.swimSpeed), UIKit.Mult(g.swimSpeed)));
                    break;
                case GearSlot.Lamp:
                    host.Add(StatBar(UIGlyphs.Sun, g.lampIntensity / MaxOf(g.slot, x => x.lampIntensity), UIKit.Mult(g.lampIntensity)));
                    host.Add(StatBar(UIGlyphs.Eye, g.lampRange / MaxOf(g.slot, x => x.lampRange), Loc.Meters(g.lampRange)));
                    break;
                case GearSlot.Camera:
                    host.Add(StatBar(UIGlyphs.Star, g.cameraQuality / MaxOf(g.slot, x => x.cameraQuality), UIKit.Mult(g.cameraQuality)));
                    host.Add(StatBar(UIGlyphs.Clock, g.tapeSeconds / MaxOf(g.slot, x => x.tapeSeconds), UIKit.ClockShort(Mathf.RoundToInt(g.tapeSeconds)) + " min"));
                    host.Add(StatBar(UIGlyphs.Plus, g.maxZoom / MaxOf(g.slot, x => x.maxZoom), UIKit.Mult(g.maxZoom)));
                    break;
                case GearSlot.Rod:
                    host.Add(StatBar(UIGlyphs.Clock, g.fishSpeed / MaxOf(g.slot, x => x.fishSpeed), UIKit.Mult(g.fishSpeed)));
                    host.Add(StatBar(UIGlyphs.Star, (g.fishLuck + 0.15f) / (MaxOf(g.slot, x => x.fishLuck) + 0.15f), "+" + Mathf.RoundToInt(g.fishLuck * 100f) + " %"));
                    break;
                case GearSlot.Boat:
                    host.Add(StatBar(UIGlyphs.Boat, g.boatSpeed / MaxOf(g.slot, x => x.boatSpeed), UIKit.Mult(g.boatSpeed)));
                    break;
            }
        }

        static VisualElement StatBar(UIGlyph.DrawFn icon, float v01, string value)
        {
            var row = UIKit.Row();
            row.style.marginTop = 6f;
            row.Add(new UIGlyph(icon, UIPalette.InkSoft, 26f));
            var bar = PhoneKit.Bar(Mathf.Clamp01(v01), UIPalette.Turquoise, 12f);
            bar.style.flexGrow = 1;
            bar.style.marginLeft = 10f;
            bar.style.marginRight = 10f;
            row.Add(bar);
            var l = UIKit.Text(value, PhoneKit.Small, UIPalette.Ink, true);
            l.style.width = 110f;
            l.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(l);
            return row;
        }

        public override void Tick(float dt)
        {
            tickTimer -= dt;
            if (tickTimer > 0f) return;
            tickTimer = 0.5f;
            for (int i = 0; i < etas.Count; i++)
            {
                var e = etas[i];
                UpdateEta(ref e);
                etas[i] = e;
            }
        }
    }
}
