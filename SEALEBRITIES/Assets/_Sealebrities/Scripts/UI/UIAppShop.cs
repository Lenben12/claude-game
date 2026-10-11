using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Chopper Shop, redesigned (L.12, player feedback ab): catalogue look with Wanda's header (portrait + one line per
    /// day), the delivery tracker (chopper on a dotted line, ETA „noch 1:40 Std.“, mishaps as „Paket suchen“), big
    /// category tiles (Tauchen · Kamera · Boot · Angeln · Snacks · Teile), an express toggle and item cards two per row:
    /// big item art, tier dots, stats compared with what you own (better green, worse grey), clear price, lock reason,
    /// „Angebot des Tages“ with a gold ribbon (−20 %).
    public class UIAppShop : UIPhoneApp
    {
        static readonly ShopCategory[] Categories = { ShopCategory.Diving, ShopCategory.Camera, ShopCategory.Boat, ShopCategory.Fishing, ShopCategory.Snacks, ShopCategory.Parts };
        static readonly Color Gold = UIPalette.Rgb(0xE8C65A);
        int category;
        bool express;

        struct LiveEta
        {
            public Delivery delivery;
            public Label label;
            public VisualElement heli;
            public int shown;
        }

        readonly List<LiveEta> etas = new List<LiveEta>();
        readonly List<GearItem> items = new List<GearItem>();
        readonly List<ShopCategory> visible = new List<ShopCategory>();
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

        static string CategoryName(ShopCategory c)
        {
            switch (c)
            {
                case ShopCategory.Diving: return Loc.Tr("Tauchen", "Diving");
                case ShopCategory.Camera: return Loc.Tr("Kamera", "Camera");
                case ShopCategory.Boat: return Loc.Tr("Boot", "Boat");
                case ShopCategory.Fishing: return Loc.Tr("Angeln", "Fishing");
                case ShopCategory.Snacks: return Loc.Tr("Snacks", "Snacks");
                default: return Loc.Tr("Teile", "Parts");
            }
        }

        static UIGlyph.DrawFn CategoryGlyph(ShopCategory c)
        {
            switch (c)
            {
                case ShopCategory.Diving: return UIGlyphs.Mask;
                case ShopCategory.Camera: return UIGlyphs.Camera;
                case ShopCategory.Boat: return UIGlyphs.BoatSide;
                case ShopCategory.Fishing: return UIGlyphs.Rod;
                case ShopCategory.Snacks: return UIGlyphs.Food;
                default: return UIGlyphs.Gear;
            }
        }

        static Color CategoryColor(ShopCategory c)
        {
            switch (c)
            {
                case ShopCategory.Diving: return UIPalette.Turquoise;
                case ShopCategory.Camera: return UIPalette.Sun;
                case ShopCategory.Boat: return UIPalette.Rgb(0x9A7658);
                case ShopCategory.Fishing: return UIPalette.Sky;
                case ShopCategory.Snacks: return UIPalette.Coral;
                default: return UIPalette.Violet;
            }
        }

        public static Color SlotColor(GearSlot s)
        {
            switch (s)
            {
                case GearSlot.Camera: case GearSlot.CameraMod: return UIPalette.Sun;
                case GearSlot.Lamp: return UIPalette.Warn;
                case GearSlot.Rod: return UIPalette.Sky;
                case GearSlot.Tank: case GearSlot.Compressor: return UIPalette.Turquoise;
                case GearSlot.Fins: return UIPalette.Coral;
                case GearSlot.Boat: case GearSlot.BoatWinch: return UIPalette.Rgb(0x9A7658);
                case GearSlot.Radio: return UIPalette.Rgb(0x3FA9B5);
                default: return UIPalette.Violet;
            }
        }

        static bool HasItems(ShopCategory c)
        {
            if (c == ShopCategory.Snacks)
            {
                var foods = FoodDatabase.All;
                for (int i = 0; i < foods.Count; i++) if (foods[i].SoldByChopper) return true;
                return false;
            }
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Category == c && (Shop.IsListed(all[i]) || all[i].comingSoon) && !(all[i].comingSoon && all[i].slot == GearSlot.Submarine)) return true;
            return false;
        }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;
            etas.Clear();

            body.Add(WandaHeader(s));

            if (s.deliveries.Count > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Lieferungen", "Deliveries"), UIGlyphs.Heli));
                for (int i = 0; i < s.deliveries.Count; i++) if (s.deliveries[i] != null) body.Add(Tracker(s.deliveries[i]));
            }

            // wallet + express toggle
            var bar = UIKit.Row();
            bar.style.marginTop = 12f;
            bar.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, Loc.Money(s.money)));
            bar.Add(UIKit.HSpacer(10f));
            var ex = UIKit.Row();
            ex.style.backgroundColor = Color.white;
            UIKit.Radius(ex, 16f);
            UIKit.Pad(ex, 10f, 14f, 10f, 14f);
            ex.Add(new UIGlyph(UIGlyphs.Clock, express ? UIPalette.CoralInk : UIPalette.InkSoft, 28f));
            var exl = UIKit.Col();
            exl.style.marginLeft = 8f;
            exl.Add(UIKit.Text("Express", PhoneKit.Small, UIPalette.Ink, true));
            exl.Add(UIKit.Text("+45 min · +50 %", PhoneKit.Tiny, UIPalette.InkSoft));
            ex.Add(exl);
            var tg = new UIToggle(express, v => { express = v; Phone.Refresh(true); }, UIPalette.Coral, 36f);
            tg.style.marginLeft = 10f;
            ex.Add(tg);
            bar.Add(ex);
            body.Add(bar);

            // category tiles
            visible.Clear();
            for (int i = 0; i < Categories.Length; i++) if (HasItems(Categories[i])) visible.Add(Categories[i]);
            if (visible.Count == 0) visible.Add(ShopCategory.Diving);
            category = Mathf.Clamp(category, 0, visible.Count - 1);
            var tabs = UIKit.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginTop = 16f;
            for (int i = 0; i < visible.Count; i++)
            {
                int idx = i;
                bool sel = i == category;
                var c = visible[i];
                var t = UIKit.Col();
                t.style.alignItems = Align.Center;
                t.style.width = Length.Percent(33.33f);
                t.style.marginBottom = 12f;
                var tile = UIWidgets.IconTile(CategoryGlyph(c), sel ? CategoryColor(c) : UIPalette.CardAlt, 86f);
                if (sel) UIKit.Border(tile, 3f, UIPalette.Ink);
                t.Add(tile);
                var l = UIKit.Text(CategoryName(c), PhoneKit.Small, sel ? UIPalette.Ink : UIPalette.InkSoft, true);
                l.style.marginTop = 4f;
                t.Add(l);
                UIKit.IgnorePicking(t);
                t.pickingMode = PickingMode.Position;
                t.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); category = idx; Phone.Refresh(false); });
                tabs.Add(t);
            }
            body.Add(tabs);

            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.Stretch;
            grid.style.justifyContent = Justify.SpaceBetween;
            var cat = visible[category];
            if (cat == ShopCategory.Snacks)
            {
                var foods = FoodDatabase.All;
                for (int i = 0; i < foods.Count; i++) if (foods[i].SoldByChopper) grid.Add(FoodCard(foods[i], s));
            }
            else
            {
                items.Clear();
                foreach (var g in GearDatabase.All)
                    if (g.Category == cat && (Shop.IsListed(g) || g.comingSoon) && !(g.comingSoon && g.slot == GearSlot.Submarine)) items.Add(g);
                items.Sort((a, b) => a.slot != b.slot ? a.slot.CompareTo(b.slot) : a.tier.CompareTo(b.tier));
                for (int i = 0; i < items.Count; i++) grid.Add(ItemCard(items[i], s));
            }
            body.Add(grid);
        }

        // ------------------------------------------------------------------ Wanda header
        static readonly string[] WandaDe =
        {
            "Heute Rückenwind! Lieferung vielleicht sogar pünktlich.",
            "Ich hab den Heli gewaschen. Also, es hat geregnet.",
            "Bestellen, zurücklehnen, Daumen drücken.",
            "Neuer Rekord: drei Lieferungen ohne Möwe im Rotor.",
            "Express heißt: Ich fliege schneller. Nicht genauer.",
            "Landet's in der Lagune: Es ist wasserdicht. Meistens.",
            "Horst wollte mitfliegen. Ich hab Platzmangel vorgetäuscht.",
        };
        static readonly string[] WandaEn =
        {
            "Tailwind today! Delivery might even be on time.",
            "I washed the chopper. Well, it rained.",
            "Order, lean back, cross your fingers.",
            "New record: three deliveries without a gull in the rotor.",
            "Express means I fly faster. Not more accurately.",
            "If it lands in the lagoon: it's waterproof. Mostly.",
            "Horst wanted a ride. I faked a lack of space.",
        };

        static VisualElement WandaHeader(GameState s)
        {
            var row = UIKit.Row();
            row.style.alignItems = Align.FlexEnd;
            var portrait = new UIGlyph(WandaPortrait, Color.white, 104f);
            row.Add(portrait);
            var bubble = UIKit.Col();
            bubble.style.marginLeft = 10f;
            bubble.style.flexShrink = 1;
            bubble.style.flexGrow = 1;
            bubble.style.backgroundColor = Color.white;
            UIKit.Radius(bubble, 22f);
            bubble.style.borderBottomLeftRadius = 4f;
            UIKit.Pad(bubble, 12f, 16f, 12f, 16f);
            UIKit.Lift(bubble, 0.8f);
            bubble.Add(UIKit.Text("Wanda", PhoneKit.Tiny, UIPalette.Rgb(0xE0607E), true));
            int i = Mathf.Abs(s.day) % WandaDe.Length;
            bubble.Add(PhoneKit.T(Loc.Tr(WandaDe[i], WandaEn[i]), PhoneKit.Small, UIPalette.Ink, true));
            row.Add(bubble);
            return row;
        }

        /// Wanda (L.12): bean face, aviator cap #6B4636, goggles, orange flight suit #EE7F2D, scarf #3FB8C4.
        static void WandaPortrait(Painter2D p, Rect r, Color c)
        {
            Vector2 P(float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
            float s = Mathf.Min(r.width, r.height);
            void Disc(Vector2 ctr, float rad, Color col)
            {
                p.fillColor = col;
                p.BeginPath();
                p.Arc(ctr, rad, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                p.Fill();
            }
            // suit (shoulders)
            p.fillColor = UIPalette.Rgb(0xEE7F2D);
            p.BeginPath();
            p.MoveTo(P(0.14f, 1f));
            p.BezierCurveTo(P(0.14f, 0.72f), P(0.86f, 0.72f), P(0.86f, 1f));
            p.ClosePath();
            p.Fill();
            // scarf
            p.fillColor = UIPalette.Rgb(0x3FB8C4);
            p.BeginPath();
            p.MoveTo(P(0.32f, 0.76f)); p.LineTo(P(0.68f, 0.76f)); p.LineTo(P(0.62f, 0.86f)); p.LineTo(P(0.38f, 0.86f));
            p.ClosePath();
            p.Fill();
            // head
            Disc(P(0.5f, 0.5f), s * 0.27f, UIPalette.Rgb(0xD6A574));
            // cap
            p.fillColor = UIPalette.Rgb(0x6B4636);
            p.BeginPath();
            p.MoveTo(P(0.22f, 0.46f));
            p.BezierCurveTo(P(0.22f, 0.12f), P(0.78f, 0.12f), P(0.78f, 0.46f));
            p.ClosePath();
            p.Fill();
            p.BeginPath();
            p.MoveTo(P(0.22f, 0.44f)); p.LineTo(P(0.18f, 0.66f)); p.LineTo(P(0.27f, 0.62f));
            p.ClosePath(); p.Fill();
            p.BeginPath();
            p.MoveTo(P(0.78f, 0.44f)); p.LineTo(P(0.82f, 0.66f)); p.LineTo(P(0.73f, 0.62f));
            p.ClosePath(); p.Fill();
            // goggles on the cap
            Disc(P(0.4f, 0.32f), s * 0.075f, UIPalette.Rgb(0x1E1B2B));
            Disc(P(0.6f, 0.32f), s * 0.075f, UIPalette.Rgb(0x1E1B2B));
            Disc(P(0.4f, 0.32f), s * 0.05f, UIPalette.Rgb(0x9FE4EC));
            Disc(P(0.6f, 0.32f), s * 0.05f, UIPalette.Rgb(0x9FE4EC));
            // eyes + grin
            Disc(P(0.41f, 0.52f), s * 0.05f, Color.white);
            Disc(P(0.59f, 0.52f), s * 0.05f, Color.white);
            Disc(P(0.42f, 0.53f), s * 0.025f, UIPalette.Rgb(0x1E1B2B));
            Disc(P(0.6f, 0.53f), s * 0.025f, UIPalette.Rgb(0x1E1B2B));
            p.strokeColor = UIPalette.Rgb(0x1E1B2B);
            p.lineWidth = s * 0.025f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(P(0.5f, 0.6f), s * 0.07f, Angle.Degrees(20f), Angle.Degrees(160f));
            p.Stroke();
        }

        // ------------------------------------------------------------------ delivery tracker
        static string MishapLine(DeliveryMishap m)
        {
            switch (m)
            {
                case DeliveryMishap.Water: return Loc.Tr("In der Lagune – schwimm hin!", "In the lagoon – swim there!");
                case DeliveryMishap.HutRoof: return Loc.Tr("Auf dem Hüttendach!", "On the hut roof!");
                case DeliveryMishap.Palm: return Loc.Tr("In einer Palme – schütteln!", "Up a palm – shake it!");
                case DeliveryMishap.WrongPlace: return Loc.Tr("Falsche Ecke – folge dem Rauch!", "Wrong corner – follow the smoke!");
                case DeliveryMishap.Village: return Loc.Tr("Im Dorf – Lio hat's", "In the village – Lio has it");
                case DeliveryMishap.Goat: return Loc.Tr("Eine Ziege sitzt drauf", "A goat sits on it");
                case DeliveryMishap.Boat: return Loc.Tr("Auf deinem Boot!", "On your boat!");
                default: return Loc.Tr("Am Helipad – abholen!", "At the helipad – pick it up!");
            }
        }

        VisualElement Tracker(Delivery d)
        {
            var item = GearDatabase.Get(d.itemId);
            var food = FoodDatabase.FromDeliveryId(d.itemId);
            var card = UIKit.Col();
            card.style.backgroundColor = d.dropped ? UIPalette.A(d.Mishap != DeliveryMishap.None ? UIPalette.Warn : UIPalette.Sun, 0.3f) : Color.white;
            UIKit.Radius(card, 20f);
            UIKit.Pad(card, 12f, 16f, 12f, 12f);
            card.style.marginBottom = 8f;
            var row = UIKit.Row();
            row.Add(UIWidgets.IconTile(food != null ? UIGlyphs.Food : item != null ? UIGlyphs.ForSlot(item.slot) : UIGlyphs.Heli,
                                       food != null ? UIPalette.Coral : item != null ? SlotColor(item.slot) : UIPalette.Coral, 52f));
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexShrink = 1;
            col.style.flexGrow = 1;
            col.Add(PhoneKit.T(Shop.DeliveryName(d), PhoneKit.Body, UIPalette.Ink, true));
            var eta = UIKit.Text("", PhoneKit.Small, d.dropped ? UIPalette.SunInk : UIPalette.InkSoft, true);
            col.Add(eta);
            row.Add(col);
            if (d.express) row.Add(UIKit.Pill("EXPRESS", UIPalette.Coral, Color.white, PhoneKit.Tiny));
            card.Add(row);

            VisualElement heli = null;
            if (!d.dropped)
            {
                // dotted line from the hangar to the helipad with the chopper on it
                var line = UIKit.Box();
                line.style.height = 34f;
                line.style.marginTop = 8f;
                for (int i = 0; i < 18; i++)
                {
                    var dot = UIKit.Dot(UIPalette.A(UIPalette.Ink, 0.2f), 6f);
                    dot.style.position = Position.Absolute;
                    dot.style.left = Length.Percent(i * 100f / 18f);
                    dot.style.top = 14f;
                    line.Add(dot);
                }
                var pad = new UIGlyph(UIGlyphs.Pin, UIPalette.CoralInk, 28f);
                pad.style.position = Position.Absolute;
                pad.style.right = 0f; pad.style.top = 2f;
                line.Add(pad);
                heli = new UIGlyph(UIGlyphs.Heli, UIPalette.Ink, 32f);
                heli.style.position = Position.Absolute;
                heli.style.top = 1f;
                heli.style.marginLeft = -16f;
                line.Add(heli);
                card.Add(line);
            }
            var le = new LiveEta { delivery = d, label = eta, heli = heli, shown = -2 };
            UpdateEta(ref le);
            etas.Add(le);
            return card;
        }

        static void UpdateEta(ref LiveEta e)
        {
            var d = e.delivery;
            var gm = GameManager.I;
            if (gm == null) return;
            int key;
            if (d.dropped) key = -1 - d.mishap;
            else if (Shop.IsDue(d)) key = 0;
            else key = Mathf.CeilToInt(Shop.SecondsUntil(d) / 2f);
            if (key == e.shown) return;
            e.shown = key;
            if (d.dropped) e.label.text = MishapLine(d.Mishap);
            else if (key == 0) e.label.text = Loc.Tr("Heli im Anflug …", "Chopper inbound …");
            else
            {
                float hours = Shop.SecondsUntil(d) / GameManager.DayLengthSeconds * 24f;
                int mins = Mathf.Max(1, Mathf.CeilToInt(hours * 60f));
                string hm = (mins / 60) + ":" + (mins % 60).ToString("00");
                e.label.text = Loc.Tr("noch " + hm + " Std. · ", hm + " h to go · ") + UIKit.TimeOfDay(d.arrivalTime);
            }
            if (e.heli != null)
            {
                float window = GameManager.DayLengthSeconds * (3f / 24f);
                float p = d.dropped || key == 0 ? 1f : 1f - Mathf.Clamp01(Shop.SecondsUntil(d) / window);
                e.heli.style.left = Length.Percent(Mathf.Lerp(4f, 92f, p));
            }
        }

        // ------------------------------------------------------------------ item cards (2 per row)
        static VisualElement TierDots(int tier, int max, Color c)
        {
            var row = UIKit.Row();
            for (int i = 0; i < Mathf.Max(1, max); i++)
            {
                var d = UIKit.Dot(i < tier ? c : UIPalette.A(UIPalette.Ink, 0.12f), 10f);
                d.style.marginRight = 4f;
                row.Add(d);
            }
            return row;
        }

        static int MaxTier(GearSlot slot)
        {
            int m = 1;
            foreach (var g in GearDatabase.All) if (g.slot == slot) m = Mathf.Max(m, g.tier);
            return m;
        }

        VisualElement CardShell(bool deal, bool highlight)
        {
            var card = UIKit.Col();
            card.style.width = Length.Percent(48.6f);
            card.style.marginBottom = 14f;
            card.style.backgroundColor = Color.white;
            UIKit.Radius(card, 22f);
            UIKit.Pad(card, 14f, 12f, 12f, 12f);
            UIKit.Lift(card, 0.8f);
            if (deal) UIKit.Border(card, 3f, Gold);
            else if (highlight) UIKit.Border(card, 3f, UIPalette.LeafInk);
            if (deal)
            {
                var ribbon = UIKit.Pill(Loc.Tr("Angebot des Tages −20 %", "Deal of the day −20 %"), Gold, UIPalette.Ink, PhoneKit.Tiny);
                ribbon.style.position = Position.Absolute;
                ribbon.style.top = -12f;
                ribbon.style.left = 10f;
                card.Add(ribbon);
            }
            return card;
        }

        VisualElement ItemCard(GearItem item, GameState s)
        {
            bool owned = Shop.IsOwned(item), ordered = Shop.IsOrdered(item);
            var equipped = GearDatabase.Equipped(s, item.slot);
            bool isEquipped = equipped == item;
            bool deal = Shop.IsDealOfTheDay(item) && !owned;
            var card = CardShell(deal, isEquipped);

            var art = UIKit.Box();
            art.style.height = 118f;
            art.style.marginTop = deal ? 8f : 0f;
            UIKit.Radius(art, 16f);
            art.style.backgroundColor = UIPalette.A(SlotColor(item.slot), item.comingSoon ? 0.08f : 0.18f);
            UIKit.Center(art);
            art.Add(new UIGlyph(UIGlyphs.ForSlot(item.slot), item.comingSoon ? UIPalette.Silhouette : UIWidgets.Ink(SlotColor(item.slot)), 84f));
            var dots = TierDots(item.tier, MaxTier(item.slot), UIWidgets.Ink(SlotColor(item.slot)));
            dots.style.position = Position.Absolute;
            dots.style.left = 10f; dots.style.bottom = 8f;
            art.Add(dots);
            card.Add(art);

            var name = PhoneKit.T(item.Name, PhoneKit.Small, item.comingSoon ? UIPalette.InkSoft : UIPalette.Ink, true);
            name.style.marginTop = 8f;
            card.Add(name);

            var stats = UIKit.Col();
            stats.style.marginTop = 4f;
            AddStats(stats, item, equipped, s);
            if (stats.childCount == 0)
            {
                var desc = PhoneKit.T(item.Description, PhoneKit.Tiny, UIPalette.InkSoft);
                stats.Add(desc);
            }
            card.Add(stats);
            card.Add(UIKit.Flex());

            if (isEquipped) { card.Add(StatusPill(UIGlyphs.Check, Loc.Tr("Ausgerüstet", "Equipped"), UIPalette.LeafInk)); return card; }
            if (owned) { card.Add(StatusPill(UIGlyphs.Bag, Loc.Tr("Im Inventar", "In inventory"), UIPalette.TurquoiseInk)); return card; }
            if (ordered) { card.Add(StatusPill(UIGlyphs.Heli, Loc.Tr("Unterwegs", "On its way"), UIPalette.SunInk)); return card; }
            if (item.comingSoon) { card.Add(StatusPill(UIGlyphs.Clock, Loc.Tr("Bald", "Soon"), UIPalette.Violet)); return card; }

            // price
            var pr = UIKit.Row();
            pr.style.marginTop = 8f;
            long price = Shop.Price(item, express);
            pr.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 28f));
            var pl = UIKit.Text(Loc.Money(price), PhoneKit.Body, s.money >= price ? UIPalette.Ink : UIPalette.CoralInk, true);
            pl.style.marginLeft = 4f;
            pr.Add(pl);
            if (deal)
            {
                var old = UIKit.Text(Loc.Money(express ? (long)(item.price * (1f + Shop.ExpressSurcharge)) : item.price), PhoneKit.Tiny, UIPalette.Silhouette, true);
                old.style.marginLeft = 8f;
                pr.Add(old);
            }
            card.Add(pr);

            bool can = Shop.CanBuy(item, express, out string reason);
            if (!can && item.followersNeeded > s.followers)
            {
                var lk = UIKit.Row();
                lk.style.marginTop = 6f;
                lk.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.CoralInk, 22f));
                lk.Add(UIKit.Text(" " + Loc.Tr("ab ", "from ") + UIKit.Compact(item.followersNeeded) + " F", PhoneKit.Tiny, UIPalette.CoralInk, true));
                card.Add(lk);
                return card;
            }
            var buy = PhoneKit.IconButton(UIGlyphs.Heli, Loc.Tr("Bestellen", "Order"), () =>
            {
                if (Shop.Buy(item, express)) { UIAudio.Pop(); Phone.Refresh(true); }
                else UIAudio.Nope();
            }, express ? UIBtnKind.Live : UIBtnKind.Primary, PhoneKit.Small);
            UIKit.Pad(buy, 10f, 8f, 10f, 8f);
            buy.style.marginTop = 8f;
            UIKit.SetEnabled(buy, can);
            card.Add(buy);
            if (!can && !string.IsNullOrEmpty(reason))
            {
                var w = PhoneKit.T(reason, PhoneKit.Tiny, UIPalette.CoralInk, true);
                w.style.marginTop = 4f;
                card.Add(w);
            }
            else
            {
                var etaL = UIKit.Text(express || Shop.FastDelivery(item, false) ? "+45 min" : StandardEta(s), PhoneKit.Tiny, UIPalette.InkSoft, true);
                etaL.style.alignSelf = Align.Center;
                etaL.style.marginTop = 4f;
                card.Add(etaL);
            }
            return card;
        }

        VisualElement FoodCard(FoodDef f, GameState s)
        {
            bool ordered = Shop.IsOrdered(FoodDatabase.DeliveryId(f));
            var card = CardShell(false, false);
            var art = UIKit.Box();
            art.style.height = 118f;
            UIKit.Radius(art, 16f);
            art.style.backgroundColor = UIPalette.A(UIPalette.Coral, 0.14f);
            UIKit.Center(art);
            art.Add(new UIGlyph(f.kind == FoodKind.Drink ? UIGlyphs.Drink : UIGlyphs.Food, UIPalette.CoralInk, 80f));
            var pack = UIKit.Pill("×" + Mathf.Max(1, f.packSize), Color.white, UIPalette.Ink, PhoneKit.Tiny);
            pack.style.position = Position.Absolute;
            pack.style.right = 8f; pack.style.bottom = 8f;
            art.Add(pack);
            int inPantry = Pantry.Count(f.id);
            if (inPantry > 0)
            {
                var have = UIKit.Pill(Loc.Tr("Vorrat ", "Stock ") + inPantry, UIPalette.Leaf, UIPalette.Ink, PhoneKit.Tiny);
                have.style.position = Position.Absolute;
                have.style.left = 8f; have.style.top = 8f;
                art.Add(have);
            }
            card.Add(art);
            var name = PhoneKit.T(f.Name, PhoneKit.Small, UIPalette.Ink, true);
            name.style.marginTop = 8f;
            card.Add(name);
            var eff = UIKit.Row();
            eff.style.marginTop = 4f;
            eff.Add(new UIGlyph(BuffGlyph(f.buff), UIPalette.TurquoiseInk, 22f));
            eff.Add(UIKit.Text(" " + BuffText(f), PhoneKit.Tiny, UIPalette.TurquoiseInk, true));
            card.Add(eff);
            card.Add(UIKit.Flex());
            if (ordered) { card.Add(StatusPill(UIGlyphs.Heli, Loc.Tr("Unterwegs", "On its way"), UIPalette.SunInk)); return card; }
            var pr = UIKit.Row();
            pr.style.marginTop = 8f;
            pr.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 28f));
            pr.Add(UIKit.Text(" " + Loc.Money(f.price), PhoneKit.Body, s.money >= f.price ? UIPalette.Ink : UIPalette.CoralInk, true));
            card.Add(pr);
            bool can = Shop.CanBuyFood(f, out string reason);
            if (!can && f.followersNeeded > s.followers)
            {
                var lk = UIKit.Row();
                lk.style.marginTop = 6f;
                lk.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.CoralInk, 22f));
                lk.Add(UIKit.Text(" " + Loc.Tr("ab ", "from ") + UIKit.Compact(f.followersNeeded) + " F", PhoneKit.Tiny, UIPalette.CoralInk, true));
                card.Add(lk);
                return card;
            }
            var buy = PhoneKit.IconButton(UIGlyphs.Heli, Loc.Tr("Bestellen", "Order"), () =>
            {
                if (Shop.BuyFood(f)) { UIAudio.Pop(); Phone.Refresh(true); } else UIAudio.Nope();
            }, UIBtnKind.Primary, PhoneKit.Small);
            UIKit.Pad(buy, 10f, 8f, 10f, 8f);
            buy.style.marginTop = 8f;
            UIKit.SetEnabled(buy, can);
            card.Add(buy);
            var etaL = UIKit.Text("+45 min", PhoneKit.Tiny, UIPalette.InkSoft, true);
            etaL.style.alignSelf = Align.Center;
            etaL.style.marginTop = 4f;
            card.Add(etaL);
            return card;
        }

        static UIGlyph.DrawFn BuffGlyph(BuffType t)
        {
            switch (t)
            {
                case BuffType.AirCapacity: return UIGlyphs.Bubbles;
                case BuffType.SwimSpeed: return UIGlyphs.Fins;
                case BuffType.SteadyHands: return UIGlyphs.Camera;
                case BuffType.BumpResist: return UIGlyphs.SharkFin;
                default: return UIGlyphs.Eye;
            }
        }

        static string BuffText(FoodDef f)
        {
            int pct = Mathf.RoundToInt(f.value * 100f);
            string what;
            switch (f.buff)
            {
                case BuffType.AirCapacity: what = Loc.Tr("Luft", "Air"); break;
                case BuffType.SwimSpeed: what = Loc.Tr("Tempo", "Speed"); break;
                case BuffType.SteadyHands: what = Loc.Tr("Ruhige Hand", "Steady hands"); break;
                case BuffType.BumpResist: what = Loc.Tr("Biss-Schutz", "Bite guard"); break;
                default: what = Loc.Tr("Nachtsicht", "Night eyes"); break;
            }
            return what + " +" + pct + " % · " + Mathf.RoundToInt(f.minutes) + " min";
        }

        static string StandardEta(GameState s)
        {
            if (s.timeOfDay > Shop.LastStandardOrder) return Loc.Tr("morgen ", "tomorrow ") + UIKit.TimeOfDay(Shop.MorningArrival);
            return "+2–3 h";
        }

        static VisualElement StatusPill(UIGlyph.DrawFn icon, string text, Color c)
        {
            var row = UIKit.Row();
            row.style.marginTop = 8f;
            row.style.justifyContent = Justify.Center;
            row.style.backgroundColor = UIPalette.A(c, 0.14f);
            UIKit.Radius(row, 999f);
            UIKit.Pad(row, 8f, 12f, 8f, 10f);
            row.Add(new UIGlyph(icon, c, 22f));
            var l = UIKit.Text(text, PhoneKit.Tiny, c, true);
            l.style.marginLeft = 6f;
            row.Add(l);
            return row;
        }

        // ------------------------------------------------------------------ stats compared with the owned item
        static void AddStats(VisualElement host, GearItem g, GearItem cur, GameState s)
        {
            float airMul = DifficultySettings.For(s.difficulty).airMultiplier;
            bool same = cur == g;
            switch (g.slot)
            {
                case GearSlot.Tank:
                    host.Add(Stat(UIGlyphs.Bubbles, UIKit.ClockShort(Mathf.RoundToInt(g.airSeconds * airMul)) + " min",
                        same || cur == null ? 0f : (g.airSeconds - cur.airSeconds) * airMul, "s", 0));
                    host.Add(Stat(UIGlyphs.Depth, Loc.Meters(g.maxDepth), same || cur == null ? 0f : g.maxDepth - cur.maxDepth, "m", 0));
                    break;
                case GearSlot.Submarine:
                    host.Add(Stat(UIGlyphs.Depth, Loc.Meters(g.maxDepth), 0f, "m", 0));
                    break;
                case GearSlot.Fins:
                    host.Add(Stat(UIGlyphs.Wave, UIKit.Mult(g.swimSpeed), same || cur == null ? 0f : g.swimSpeed - cur.swimSpeed, "×", 2));
                    break;
                case GearSlot.Lamp:
                    host.Add(Stat(UIGlyphs.Sun, UIKit.Mult(g.lampIntensity), same || cur == null ? 0f : g.lampIntensity - cur.lampIntensity, "×", 1));
                    host.Add(Stat(UIGlyphs.Eye, Loc.Meters(g.lampRange), same || cur == null ? 0f : g.lampRange - cur.lampRange, "m", 0));
                    break;
                case GearSlot.Camera:
                    host.Add(Stat(UIGlyphs.Star, UIKit.Mult(g.cameraQuality), same || cur == null ? 0f : g.cameraQuality - cur.cameraQuality, "×", 2));
                    host.Add(Stat(UIGlyphs.Clock, UIKit.ClockShort(Mathf.RoundToInt(g.tapeSeconds)) + " min", same || cur == null ? 0f : g.tapeSeconds - cur.tapeSeconds, "s", 0));
                    host.Add(Stat(UIGlyphs.Plus, UIKit.Mult(g.maxZoom), same || cur == null ? 0f : g.maxZoom - cur.maxZoom, "×", 1));
                    break;
                case GearSlot.Rod:
                    host.Add(Stat(UIGlyphs.Clock, UIKit.Mult(g.fishSpeed), same || cur == null ? 0f : g.fishSpeed - cur.fishSpeed, "×", 2));
                    host.Add(Stat(UIGlyphs.Arrow, Loc.Meters(g.castMax), same || cur == null ? 0f : g.castMax - cur.castMax, "m", 0));
                    break;
                case GearSlot.Boat:
                    host.Add(Stat(UIGlyphs.Boat, UIKit.Mult(g.boatSpeed), same || cur == null ? 0f : g.boatSpeed - cur.boatSpeed, "×", 2));
                    break;
            }
        }

        /// One stat line: icon + value + delta vs. the owned item (better = green „+30 s“, worse = grey).
        static VisualElement Stat(UIGlyph.DrawFn icon, string value, float delta, string unit, int decimals)
        {
            var row = UIKit.Row();
            row.style.marginTop = 4f;
            row.Add(new UIGlyph(icon, UIPalette.InkSoft, 22f));
            var v = UIKit.Text(" " + value, PhoneKit.Tiny, UIPalette.Ink, true);
            v.style.flexShrink = 1;
            row.Add(v);
            if (Mathf.Abs(delta) > 0.0001f)
            {
                string fmt = decimals <= 0 ? "0" : decimals == 1 ? "0.0" : "0.00";
                string d = (delta > 0f ? "+" : "−") + Mathf.Abs(delta).ToString(fmt, Loc.Culture) + (unit == "×" ? "×" : " " + unit);
                var dl = UIKit.Text("  " + d, PhoneKit.Tiny, delta > 0f ? UIPalette.LeafInk : UIPalette.Silhouette, true);
                row.Add(dl);
            }
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
