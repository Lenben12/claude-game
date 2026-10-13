using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Chopper Shop – round 3 CATALOGUE (GDD §19 „Chopper-Shop: schöner, jedes Item mit eigenem Bild“, §20 Q32
    /// „Katalog mit großen Bildern“). Wanda's header, the delivery trackers (now with the item's picture), wallet +
    /// express toggle, category chips with icons (Tauchen · Kamera · Boot · Angeln · Speere · Snacks · Teile) and a
    /// two-column catalogue: every card has a BIG unique 3D picture (UIItemIcons3D – rendered lazily, visible cards
    /// first, at most two per frame; the 2D glyph shows until then), the name, stat bars compared with what the local
    /// player has equipped (grey = what you already have, green = the gain, ghost = what you would lose), the price
    /// (deal of the day −20 % with a gold ribbon and a struck-through old price, story discounts too), the delivery ETA
    /// („heute 15:10–16:10“, express „heute 14:15“) and the order button – or its state (equipped / owned / on its way /
    /// soon / follower lock with progress). Tapping a card opens the detail page with an even bigger picture, all stats
    /// with deltas, normal vs express delivery (selectable, with ETA + price) and the order button; ‹ › flip through
    /// the category, Back() returns to the catalogue at the same scroll position.
    /// Buying uses exactly the old paths (Shop.Buy / Shop.BuyItem / Shop.BuyFood → co-op routing in Shop).
    public class UIAppShop : UIPhoneApp
    {
        static readonly ShopCategory[] Categories = { ShopCategory.Diving, ShopCategory.Camera, ShopCategory.Boat, ShopCategory.Fishing, ShopCategory.Weapons, ShopCategory.Snacks, ShopCategory.Parts };
        static readonly Color Gold = UIPalette.Rgb(0xE8C65A);
        static readonly Color BarGreen = UIPalette.Rgb(0x5CC86E);
        const float CardArt = 190f, CardIcon = 180f, DetailArt = 420f, DetailIcon = 396f;
        const int MaxIconsPerFrame = 2;

        // ================================================================== catalogue entries
        /// One sold thing: gear (GearDatabase), a backpack item (spears, speargun) or a snack pack. Cached per id.
        public sealed class Entry
        {
            public readonly GearItem gear;
            public readonly ItemDef item;
            public readonly FoodDef food;
            /// Picture / delivery key: gear id, "item:&lt;id&gt;" or "food:&lt;id&gt;".
            public readonly string key;
            public readonly ShopCategory category;

            Entry(GearItem g, ItemDef d, FoodDef f, string key, ShopCategory c)
            {
                gear = g; item = d; food = f; this.key = key; category = c;
            }

            public string Name => gear != null ? gear.Name : item != null ? item.Name : food != null ? food.Name : key;
            public string Description => gear != null ? gear.Description : item != null ? item.Description : food != null ? food.Description : "";

            static readonly Dictionary<string, Entry> Gears = new Dictionary<string, Entry>();
            static readonly Dictionary<string, Entry> Items = new Dictionary<string, Entry>();
            static readonly Dictionary<string, Entry> Foods = new Dictionary<string, Entry>();

            internal static void ClearCache() { Gears.Clear(); Items.Clear(); Foods.Clear(); }

            public static Entry Of(GearItem g)
            {
                if (g == null) return null;
                if (!Gears.TryGetValue(g.id, out var e)) { e = new Entry(g, null, null, g.id, g.Category); Gears[g.id] = e; }
                return e;
            }

            public static Entry Of(ItemDef d)
            {
                if (d == null) return null;
                if (!Items.TryGetValue(d.id, out var e)) { e = new Entry(null, d, null, ItemDatabase.DeliveryId(d), ShopCategory.Weapons); Items[d.id] = e; }
                return e;
            }

            public static Entry Of(FoodDef f)
            {
                if (f == null) return null;
                if (!Foods.TryGetValue(f.id, out var e)) { e = new Entry(null, null, f, FoodDatabase.DeliveryId(f), ShopCategory.Snacks); Foods[f.id] = e; }
                return e;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Entry.ClearCache();
            weaponItems.Clear();
            gearScratch.Clear();
            entryScratch.Clear();
            lines.Clear();
            feats.Clear();
        }

        static readonly List<ItemDef> weaponItems = new List<ItemDef>();
        static readonly List<GearItem> gearScratch = new List<GearItem>();
        static readonly List<Entry> entryScratch = new List<Entry>();
        static readonly System.Comparison<GearItem> GearOrder = (a, b) => a.slot != b.slot ? a.slot.CompareTo(b.slot) : a.tier.CompareTo(b.tier);

        static bool ShownGear(GearItem g) => (Shop.IsListed(g) || g.comingSoon) && !(g.comingSoon && g.slot == GearSlot.Submarine);

        public static bool HasItems(ShopCategory c)
        {
            if (c == ShopCategory.Weapons)
            {
                ItemDatabase.Sold(weaponItems);
                return weaponItems.Count > 0;
            }
            if (c == ShopCategory.Snacks)
            {
                var foods = FoodDatabase.All;
                for (int i = 0; i < foods.Count; i++) if (foods[i].SoldByChopper) return true;
                return false;
            }
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i].Category == c && ShownGear(all[i])) return true;
            return false;
        }

        /// Everything the shop shows in a category, in catalogue order.
        public static void Catalogue(ShopCategory c, List<Entry> into)
        {
            into.Clear();
            if (c == ShopCategory.Weapons)
            {
                ItemDatabase.Sold(weaponItems);
                for (int i = 0; i < weaponItems.Count; i++) into.Add(Entry.Of(weaponItems[i]));
                return;
            }
            if (c == ShopCategory.Snacks)
            {
                var foods = FoodDatabase.All;
                for (int i = 0; i < foods.Count; i++) if (foods[i].SoldByChopper) into.Add(Entry.Of(foods[i]));
                return;
            }
            gearScratch.Clear();
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++) if (all[i].Category == c && ShownGear(all[i])) gearScratch.Add(all[i]);
            gearScratch.Sort(GearOrder);
            for (int i = 0; i < gearScratch.Count; i++) into.Add(Entry.Of(gearScratch[i]));
            gearScratch.Clear();
        }

        /// Picture keys of every item the shop shows right now (all categories) – tests / contact sheet.
        public static void CatalogueKeys(List<string> into)
        {
            into.Clear();
            for (int c = 0; c < Categories.Length; c++)
            {
                if (!HasItems(Categories[c])) continue;
                Catalogue(Categories[c], entryScratch);
                for (int i = 0; i < entryScratch.Count; i++) into.Add(entryScratch[i].key);
            }
            entryScratch.Clear();
        }

        /// The entry of a picture / delivery key (null when unknown).
        public static Entry EntryFor(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            var f = FoodDatabase.FromDeliveryId(key);
            if (f != null) return Entry.Of(f);
            var d = ItemDatabase.FromDeliveryId(key);
            if (d != null) return Entry.Of(d);
            var g = GearDatabase.Get(key);
            return g != null ? Entry.Of(g) : null;
        }

        // ================================================================== page state
        int category;
        bool express;
        string detailKey;
        float restoreScroll = -1f;
        VisualElement viewport;
        ScrollView scrollView;
        float tickTimer;
        int etaKey = -1, pumpFrame, pending, pendingVisible;

        struct LiveEta
        {
            public Delivery delivery;
            public Label label;
            public VisualElement heli;
            public int shown;
        }

        struct EtaLabel
        {
            public Label label;
            public bool fast, relative;
        }

        readonly List<LiveEta> etas = new List<LiveEta>();
        readonly List<EtaLabel> etaLabels = new List<EtaLabel>();
        readonly List<UIItemIcon> icons = new List<UIItemIcon>();
        readonly List<Entry> page = new List<Entry>();
        readonly List<ShopCategory> visible = new List<ShopCategory>();

        /// What the last Build put on the page (AutoPlaytest checks).
        public struct BuildInfo
        {
            public bool detail;
            public int cards, categories, icons, statBars, features, prices, etas, options, buyButtons, statusPills, locks, deals;
        }

        public BuildInfo LastBuild;

        public override string Id => "shop";
        public override string Title => "Chopper Shop";
        public override Color Color => UIPalette.Coral;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Heli;

        public override string HeaderTitle
        {
            get
            {
                var e = detailKey != null ? EntryFor(detailKey) : null;
                return e != null ? e.Name : Title;
            }
        }

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

        // ------------------------------------------------------------------ debug / test API
        public IReadOnlyList<ShopCategory> VisibleCategories => visible;
        public int CategoryIndex => category;
        public string DetailKey => detailKey;
        public bool Express => express;
        /// Pictures of the current page that still show their glyph.
        public int PendingIcons => pending;
        /// Pictures inside the visible part of the page that still show their glyph (pictures further down are only
        /// rendered when they scroll near).
        public int PendingVisibleIcons => pendingVisible;

        public void DebugSelectCategory(int index)
        {
            RefreshVisible();
            category = Mathf.Clamp(index, 0, Mathf.Max(0, visible.Count - 1));
            detailKey = null;
            if (Phone != null) Phone.Refresh(false);
        }

        public void DebugOpenDetail(string key) => OpenDetail(key);

        public void DebugSetExpress(bool on)
        {
            express = on;
            if (Phone != null) Phone.Refresh(true);
        }

        /// Runs exactly what the order button runs.
        public bool DebugBuy(string key)
        {
            var e = EntryFor(key);
            return e != null && Buy(e);
        }

        // ================================================================== navigation
        public override void OnOpen()
        {
            detailKey = null;
            restoreScroll = -1f;
        }

        public override void OnClose()
        {
            detailKey = null;
            restoreScroll = -1f;
        }

        public override bool Back()
        {
            if (detailKey == null) return false;
            detailKey = null;
            return true;
        }

        void OpenDetail(string key)
        {
            if (string.IsNullOrEmpty(key) || EntryFor(key) == null) return;
            if (detailKey == null) restoreScroll = scrollView != null ? scrollView.scrollOffset.y : -1f;
            detailKey = key;
            if (Phone != null) Phone.Refresh(false);
        }

        void SelectCategory(int idx)
        {
            UIAudio.Click();
            category = idx;
            detailKey = null;
            if (Phone != null) Phone.Refresh(true);
        }

        void RefreshVisible()
        {
            visible.Clear();
            for (int i = 0; i < Categories.Length; i++) if (HasItems(Categories[i])) visible.Add(Categories[i]);
            if (visible.Count == 0) visible.Add(ShopCategory.Diving);
            category = Mathf.Clamp(category, 0, visible.Count - 1);
        }

        // ================================================================== build
        public override void Build(VisualElement body)
        {
            etas.Clear();
            etaLabels.Clear();
            icons.Clear();
            pending = 0;
            pendingVisible = 0;
            LastBuild = default;
            viewport = body.parent;
            scrollView = body.GetFirstAncestorOfType<ScrollView>();
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;
            RefreshVisible();

            if (detailKey != null)
            {
                var e = EntryFor(detailKey);
                if (e != null) BuildDetail(body, e, s);
                else detailKey = null;
            }
            if (detailKey == null)
            {
                BuildCatalogue(body, s);
                if (restoreScroll > 0f && scrollView != null)
                {
                    float y = restoreScroll;
                    var sv = scrollView;
                    EventCallback<GeometryChangedEvent> cb = null;
                    cb = _ =>
                    {
                        sv.scrollOffset = new Vector2(0f, y);
                        body.UnregisterCallback(cb);
                    };
                    body.RegisterCallback(cb);
                }
                restoreScroll = -1f;
            }
            for (int i = 0; i < icons.Count; i++) if (!icons[i].Resolved) pending++;
            pendingVisible = pending;   // unknown until the next layout pass
            etaKey = EtaKey(s);
        }

        void BuildCatalogue(VisualElement body, GameState s)
        {
            body.Add(WandaHeader(s));

            if (s.deliveries.Count > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Lieferungen", "Deliveries"), UIGlyphs.Heli));
                for (int i = 0; i < s.deliveries.Count; i++) if (s.deliveries[i] != null) body.Add(Tracker(s.deliveries[i]));
            }

            body.Add(WalletBar(s));
            body.Add(CategoryChips());

            var cat = visible[category];
            Catalogue(cat, page);
            var grid = UIKit.Row();
            grid.name = "shop-grid";
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.Stretch;
            grid.style.justifyContent = Justify.SpaceBetween;
            grid.style.marginTop = 4f;
            for (int i = 0; i < page.Count; i++) grid.Add(Card(page[i], s));
            if (page.Count == 0) body.Add(PhoneKit.Empty(CategoryGlyph(cat), Loc.Tr("Gerade nichts im Angebot.", "Nothing for sale right now.")));
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
            var portrait = new UIGlyph(WandaPortrait, Color.white, 92f);
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
            p.fillColor = UIPalette.Rgb(0xEE7F2D);
            p.BeginPath();
            p.MoveTo(P(0.14f, 1f));
            p.BezierCurveTo(P(0.14f, 0.72f), P(0.86f, 0.72f), P(0.86f, 1f));
            p.ClosePath();
            p.Fill();
            p.fillColor = UIPalette.Rgb(0x3FB8C4);
            p.BeginPath();
            p.MoveTo(P(0.32f, 0.76f)); p.LineTo(P(0.68f, 0.76f)); p.LineTo(P(0.62f, 0.86f)); p.LineTo(P(0.38f, 0.86f));
            p.ClosePath();
            p.Fill();
            Disc(P(0.5f, 0.5f), s * 0.27f, UIPalette.Rgb(0xD6A574));
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
            Disc(P(0.4f, 0.32f), s * 0.075f, UIPalette.Rgb(0x1E1B2B));
            Disc(P(0.6f, 0.32f), s * 0.075f, UIPalette.Rgb(0x1E1B2B));
            Disc(P(0.4f, 0.32f), s * 0.05f, UIPalette.Rgb(0x9FE4EC));
            Disc(P(0.6f, 0.32f), s * 0.05f, UIPalette.Rgb(0x9FE4EC));
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

        // ------------------------------------------------------------------ wallet + express toggle
        VisualElement WalletBar(GameState s)
        {
            var bar = UIKit.Row();
            bar.style.marginTop = 12f;
            bar.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, Loc.Money(s.money)));
            bar.Add(UIKit.HSpacer(10f));
            var ex = UIKit.Row();
            ex.name = "shop-express";
            ex.style.backgroundColor = express ? UIPalette.A(UIPalette.Coral, 0.16f) : Color.white;
            UIKit.Radius(ex, 16f);
            UIKit.Pad(ex, 10f, 14f, 10f, 12f);
            ex.Add(new UIGlyph(UIGlyphs.ShopBolt, express ? UIPalette.CoralInk : UIPalette.InkSoft, 30f));
            var exl = UIKit.Col();
            exl.style.marginLeft = 8f;
            exl.Add(UIKit.Text("Express", PhoneKit.Small, UIPalette.Ink, true));
            exl.Add(UIKit.Text(Mathf.RoundToInt(Shop.ExpressHours * 60f) + " min · +" + Mathf.RoundToInt(Shop.ExpressSurcharge * 100f) + " %", PhoneKit.Tiny, UIPalette.InkSoft));
            ex.Add(exl);
            var tg = new UIToggle(express, v => { express = v; if (Phone != null) Phone.Refresh(true); }, UIPalette.Coral, 36f);
            tg.style.marginLeft = 10f;
            ex.Add(tg);
            bar.Add(ex);
            return bar;
        }

        // ------------------------------------------------------------------ categories
        public static string CategoryName(ShopCategory c)
        {
            switch (c)
            {
                case ShopCategory.Diving: return Loc.Tr("Tauchen", "Diving");
                case ShopCategory.Camera: return Loc.Tr("Kamera", "Camera");
                case ShopCategory.Boat: return Loc.Tr("Boot", "Boat");
                case ShopCategory.Fishing: return Loc.Tr("Angeln", "Fishing");
                case ShopCategory.Snacks: return Loc.Tr("Snacks", "Snacks");
                case ShopCategory.Weapons: return Loc.Tr("Speere", "Spears");
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
                case ShopCategory.Weapons: return UIGlyphs.SharkFin;
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
                case ShopCategory.Weapons: return UIPalette.Rgb(0xB88A5A);
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

        VisualElement CategoryChips()
        {
            var wrap = UIKit.Row();
            wrap.name = "shop-categories";
            wrap.style.flexWrap = Wrap.Wrap;
            wrap.style.alignItems = Align.FlexStart;
            wrap.style.marginTop = 16f;
            int n = visible.Count;
            int per = n <= 6 ? Mathf.Max(1, n) : 4;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                bool sel = i == category;
                var c = visible[i];
                var chip = UIKit.Col();
                chip.name = "shop-category";
                chip.style.width = Length.Percent(100f / per);
                chip.style.alignItems = Align.Center;
                chip.style.marginBottom = 10f;
                var tile = UIWidgets.IconTile(CategoryGlyph(c), sel ? CategoryColor(c) : UIPalette.CardAlt, 66f);
                if (sel) UIKit.Border(tile, 3f, UIPalette.Ink);
                chip.Add(tile);
                var l = UIKit.Text(CategoryName(c), PhoneKit.Tiny, sel ? UIPalette.Ink : UIPalette.InkSoft, true);
                l.style.marginTop = 4f;
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                chip.Add(l);
                UIKit.IgnorePicking(chip);
                chip.pickingMode = PickingMode.Position;
                chip.RegisterCallback<ClickEvent>(_ => SelectCategory(idx));
                wrap.Add(chip);
                LastBuild.categories++;
            }
            return wrap;
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
                case DeliveryMishap.Village: return Loc.Tr("Am Südstrand gelandet", "Landed on the south beach");
                case DeliveryMishap.Goat: return Loc.Tr("Eine Ziege sitzt drauf", "A goat sits on it");
                case DeliveryMishap.Boat: return Loc.Tr("Auf deinem Boot!", "On your boat!");
                default: return Loc.Tr("Am Helipad – abholen!", "At the helipad – pick it up!");
            }
        }

        VisualElement Tracker(Delivery d)
        {
            var e = EntryFor(d.itemId);
            var card = UIKit.Col();
            card.name = "shop-tracker";
            card.style.backgroundColor = d.dropped ? UIPalette.A(d.Mishap != DeliveryMishap.None ? UIPalette.Warn : UIPalette.Sun, 0.3f) : Color.white;
            UIKit.Radius(card, 20f);
            UIKit.Pad(card, 10f, 16f, 10f, 10f);
            card.style.marginBottom = 8f;
            var row = UIKit.Row();
            var pic = UIKit.Box();
            UIKit.Size(pic, 64f, 64f);
            UIKit.Radius(pic, 16f);
            UIKit.Center(pic);
            pic.style.backgroundColor = UIPalette.A(e != null ? CategoryColor(e.category) : UIPalette.Coral, 0.18f);
            var icon = new UIItemIcon(d.itemId, e != null ? GlyphFor(e) : UIGlyphs.Heli, e != null ? GlyphColorFor(e) : UIPalette.CoralInk, 60f);
            pic.Add(icon);
            icons.Add(icon);
            row.Add(pic);
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
                var line = UIKit.Box();
                line.style.height = 34f;
                line.style.marginTop = 6f;
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

        // ================================================================== item state
        struct ItemState
        {
            public bool owned, equipped, ordered, deal, discount, comingSoon, locked, can, fast;
            public long price, oldPrice, followersNeeded;
            public string reason;
            public int stock;
            /// Gear: the local player's item in the same slot (comparison base).
            public GearItem current;
        }

        ItemState StateOf(Entry e, GameState s)
        {
            var st = new ItemState();
            if (e.gear != null)
            {
                var g = e.gear;
                st.owned = Shop.IsOwned(g);
                st.current = GearDatabase.Equipped(s, g.slot);
                st.equipped = st.current == g;
                st.ordered = Shop.IsOrdered(g);
                st.comingSoon = g.comingSoon;
                st.deal = Shop.IsDealOfTheDay(g) && !st.owned;
                st.price = Shop.Price(g, express);
                st.oldPrice = express ? (long)(g.price * (1f + Shop.ExpressSurcharge)) : g.price;
                st.discount = !st.deal && st.oldPrice > st.price;
                st.can = Shop.CanBuy(g, express, out st.reason);
                st.followersNeeded = g.followersNeeded;
                st.fast = Shop.FastDelivery(g, express);
            }
            else if (e.item != null)
            {
                var d = e.item;
                st.ordered = Shop.IsOrdered(e.key);
                st.price = Shop.ItemPrice(d, express);
                st.oldPrice = st.price;
                st.can = Shop.CanBuyItem(d, express, out st.reason);
                st.followersNeeded = d.followersNeeded;
                st.fast = express;
                st.stock = Backpack.Count(d.id);
            }
            else if (e.food != null)
            {
                var f = e.food;
                st.ordered = Shop.IsOrdered(e.key);
                st.price = f.price;
                st.oldPrice = f.price;
                st.can = Shop.CanBuyFood(f, out st.reason);
                st.followersNeeded = f.followersNeeded;
                st.fast = true;   // snack packs always fly with the express timing at the standard price (E.1)
                st.stock = Backpack.Count(f.id) + Pantry.Count(f.id);
            }
            st.locked = !st.can && st.followersNeeded > s.followers && !st.owned && !st.ordered && !st.comingSoon;
            return st;
        }

        static UIGlyph.DrawFn GlyphFor(Entry e)
        {
            if (e.gear != null) return UIGlyphs.ForSlot(e.gear.slot);
            if (e.item != null) return UIGlyphs.ForItemId(e.item.id) ?? (UIGlyph.DrawFn)UIGlyphs.ItemSpear;
            if (e.food != null) return e.food.kind == FoodKind.Drink ? UIGlyphs.Drink : UIGlyphs.Food;
            return UIGlyphs.Heli;
        }

        /// The multi-coloured item glyphs use the colour only as opacity; slot glyphs are tinted.
        static Color GlyphColorFor(Entry e)
        {
            if (e.gear != null) return UIWidgets.Ink(SlotColor(e.gear.slot));
            if (e.item != null) return Color.white;
            return UIPalette.CoralInk;
        }

        static int MaxTier(GearSlot slot)
        {
            int m = 1;
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++) if (all[i].slot == slot) m = Mathf.Max(m, all[i].tier);
            return m;
        }

        // ================================================================== catalogue card
        VisualElement Card(Entry e, GameState s)
        {
            var st = StateOf(e, s);
            var card = UIKit.Col();
            card.name = "shop-card";
            card.style.width = Length.Percent(48.6f);
            card.style.marginTop = 10f;
            card.style.marginBottom = 8f;
            card.style.backgroundColor = Color.white;
            UIKit.Radius(card, 22f);
            UIKit.Pad(card, 10f, 10f, 12f, 10f);
            UIKit.Lift(card, 0.8f);
            if (st.deal) UIKit.Border(card, 3f, Gold);
            else if (st.equipped) UIKit.Border(card, 3f, UIPalette.LeafInk);

            card.Add(Art(e, st, CardArt, CardIcon, false));
            var ribbon = Ribbon(st, false);
            if (ribbon != null) card.Add(ribbon);

            var name = PhoneKit.T(e.Name, PhoneKit.Small, st.comingSoon ? UIPalette.InkSoft : UIPalette.Ink, true);
            name.style.marginTop = 8f;
            card.Add(name);
            var tagline = UIKit.Text(ShortLine(e.Description), PhoneKit.Tiny, UIPalette.InkSoft);
            tagline.name = "shop-tagline";
            tagline.style.marginTop = 2f;
            tagline.style.whiteSpace = WhiteSpace.NoWrap;
            tagline.style.overflow = Overflow.Hidden;
            tagline.style.textOverflow = TextOverflow.Ellipsis;
            card.Add(tagline);

            CollectStats(e, s, st);
            int shown = 0;
            for (int i = 0; i < lines.Count && shown < 2; i++, shown++) card.Add(StatRow(lines[i], false));
            if (shown == 0 && feats.Count > 0) card.Add(FeatureChip(feats[0], false));
            card.Add(UIKit.Flex());
            card.Add(CardFooter(e, st, s));

            card.RegisterCallback<ClickEvent>(evt =>
            {
                if (FromButton(evt.target as VisualElement, card)) return;
                UIAudio.Click();
                OpenDetail(e.key);
            });
            LastBuild.cards++;
            return card;
        }

        /// The first sentence of a description ("Verbeult, aber dicht." – a dot inside "1.5×" is no sentence end).
        static string ShortLine(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return "";
            for (int i = 0; i < desc.Length; i++)
            {
                char c = desc[i];
                if ((c == '.' || c == '!' || c == '?') && (i == desc.Length - 1 || desc[i + 1] == ' '))
                    return desc.Substring(0, i + 1);
            }
            return desc;
        }

        static bool FromButton(VisualElement t, VisualElement stop)
        {
            for (var x = t; x != null && x != stop; x = x.parent) if (x is Button || x is UIToggle) return true;
            return false;
        }

        VisualElement Ribbon(ItemState st, bool big)
        {
            Label r = null;
            if (st.deal)
            {
                string pct = " −" + Mathf.RoundToInt(Shop.DealOfDayDiscount * 100f) + " %";
                r = UIKit.Pill(big ? Loc.Tr("Angebot des Tages", "Deal of the day") + pct : Loc.Tr("Tagesangebot", "Daily deal") + pct, Gold, UIPalette.Ink, big ? PhoneKit.Small : PhoneKit.Tiny);
                LastBuild.deals++;
            }
            else if (st.discount && st.oldPrice > 0)
                r = UIKit.Pill("−" + Mathf.RoundToInt((1f - st.price / (float)st.oldPrice) * 100f) + " %", UIPalette.Coral, Color.white, big ? PhoneKit.Small : PhoneKit.Tiny);
            if (r == null) return null;
            r.name = "shop-ribbon";
            r.pickingMode = PickingMode.Ignore;
            r.style.position = Position.Absolute;
            r.style.top = -12f;
            r.style.left = big ? 16f : 10f;
            return r;
        }

        /// The big picture: tinted card colour, soft white spotlight, the 3D picture (glyph until rendered), tier
        /// dots, pack size / stock, a status badge and the follower lock.
        VisualElement Art(Entry e, ItemState st, float h, float iconSize, bool big)
        {
            var color = CategoryColor(e.category);
            var art = UIKit.Box();
            art.name = "shop-art";
            art.style.height = h;
            art.style.flexShrink = 0;
            UIKit.Radius(art, big ? 28f : 18f);
            art.style.backgroundColor = UIPalette.A(color, st.comingSoon ? 0.08f : 0.2f);
            art.style.overflow = Overflow.Hidden;
            UIKit.Center(art);
            art.pickingMode = PickingMode.Ignore;

            var disc = UIKit.Box();
            disc.pickingMode = PickingMode.Ignore;
            float d = h * 0.96f;
            UIKit.Size(disc, d, d);
            disc.style.position = Position.Absolute;
            disc.style.left = Length.Percent(50f);
            disc.style.top = Length.Percent(50f);
            disc.style.marginLeft = -d * 0.5f;
            disc.style.marginTop = -d * 0.5f;
            UIKit.Image(disc, UIItemIcons3D.SoftDisc);
            disc.style.unityBackgroundImageTintColor = new Color(1f, 1f, 1f, 0.92f);
            art.Add(disc);

            var icon = new UIItemIcon(e.key, GlyphFor(e), GlyphColorFor(e), iconSize);
            if (st.comingSoon) icon.style.opacity = 0.4f;
            art.Add(icon);
            icons.Add(icon);
            LastBuild.icons++;

            float pad = big ? 14f : 8f;
            if (e.gear != null && MaxTier(e.gear.slot) > 1)
            {
                var dots = TierDots(e.gear.tier, MaxTier(e.gear.slot), UIWidgets.Ink(SlotColor(e.gear.slot)), big ? 14f : 10f);
                dots.style.position = Position.Absolute;
                dots.style.left = pad; dots.style.bottom = pad;
                art.Add(dots);
            }
            if (e.food != null)
            {
                var pack = UIKit.Pill("×" + Mathf.Max(1, e.food.packSize), Color.white, UIPalette.Ink, big ? PhoneKit.Small : PhoneKit.Tiny);
                pack.style.position = Position.Absolute;
                pack.style.right = pad; pack.style.bottom = pad;
                art.Add(pack);
            }
            if (st.stock > 0)
            {
                var have = UIKit.Pill((e.food != null ? Loc.Tr("Vorrat ", "Stock ") : Loc.Tr("Rucksack ", "Backpack ")) + st.stock, UIPalette.Leaf, UIPalette.Ink, big ? PhoneKit.Small : PhoneKit.Tiny);
                have.style.position = Position.Absolute;
                have.style.left = pad; have.style.top = pad + (big ? 0f : 6f);
                art.Add(have);
            }
            UIGlyph.DrawFn badge = null;
            Color badgeColor = UIPalette.LeafInk;
            if (st.equipped) { badge = UIGlyphs.Check; badgeColor = UIPalette.LeafInk; }
            else if (st.owned) { badge = UIGlyphs.Bag; badgeColor = UIPalette.TurquoiseInk; }
            else if (st.ordered) { badge = UIGlyphs.Heli; badgeColor = UIPalette.SunInk; }
            else if (st.locked) { badge = UIGlyphs.Lock; badgeColor = UIPalette.CoralInk; }
            if (badge != null)
            {
                float bs = big ? 56f : 40f;
                var b = UIKit.Box();
                b.pickingMode = PickingMode.Ignore;
                UIKit.Size(b, bs, bs);
                UIKit.Radius(b, bs * 0.5f);
                UIKit.Center(b);
                b.style.backgroundColor = Color.white;
                UIKit.Border(b, 3f, badgeColor);
                b.style.position = Position.Absolute;
                b.style.right = pad; b.style.top = pad + (big ? 0f : 6f);
                b.Add(new UIGlyph(badge, badgeColor, bs * 0.56f));
                art.Add(b);
            }
            if (st.locked)
            {
                var veil = UIKit.Box();
                veil.pickingMode = PickingMode.Ignore;
                UIKit.FillParent(veil);
                veil.style.backgroundColor = new Color(1f, 1f, 1f, 0.35f);
                art.Insert(art.IndexOf(icon) + 1, veil);
            }
            return art;
        }

        static VisualElement TierDots(int tier, int max, Color c, float size)
        {
            var row = UIKit.Row();
            row.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < Mathf.Max(1, max); i++)
            {
                var d = UIKit.Dot(i < tier ? c : UIPalette.A(UIPalette.Ink, 0.14f), size);
                d.style.marginRight = size * 0.4f;
                row.Add(d);
            }
            return row;
        }

        VisualElement CardFooter(Entry e, ItemState st, GameState s)
        {
            var col = UIKit.Col();
            if (st.equipped) { col.Add(StatusPill(UIGlyphs.Check, Loc.Tr("Ausgerüstet", "Equipped"), UIPalette.LeafInk, false)); return col; }
            if (st.owned) { col.Add(StatusPill(UIGlyphs.Bag, Loc.Tr("Im Besitz", "Owned"), UIPalette.TurquoiseInk, false)); return col; }
            if (st.ordered) { col.Add(StatusPill(UIGlyphs.Heli, Loc.Tr("Unterwegs", "On its way"), UIPalette.SunInk, false)); return col; }
            if (st.comingSoon) { col.Add(StatusPill(UIGlyphs.Clock, Loc.Tr("Bald", "Soon"), UIPalette.Violet, false)); return col; }
            col.Add(PriceRow(st, s, PhoneKit.Body));
            col.Add(EtaRow(st.fast));
            if (st.locked) col.Add(LockBox(st.followersNeeded, s.followers, false));
            else col.Add(BuyButton(e, st, false));
            return col;
        }

        VisualElement PriceRow(ItemState st, GameState s, float size)
        {
            var row = UIKit.Row();
            row.name = "shop-price";
            row.style.marginTop = 6f;
            row.style.flexWrap = Wrap.Wrap;
            row.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, size * 1.1f));
            var p = UIKit.Text(Loc.Money(st.price), size, s.money >= st.price ? UIPalette.Ink : UIPalette.CoralInk, true);
            p.style.marginLeft = 5f;
            row.Add(p);
            if (st.oldPrice > st.price)
            {
                var old = Strike(Loc.Money(st.oldPrice), Mathf.Max(PhoneKit.Tiny, size * 0.7f));
                old.style.marginLeft = 8f;
                row.Add(old);
            }
            LastBuild.prices++;
            return row;
        }

        /// Old price with a coral strike line.
        static VisualElement Strike(string text, float size)
        {
            var wrap = UIKit.Box();
            wrap.pickingMode = PickingMode.Ignore;
            wrap.Add(UIKit.Text(text, size, UIPalette.Silhouette, true));
            var line = UIKit.Box();
            line.pickingMode = PickingMode.Ignore;
            line.style.position = Position.Absolute;
            line.style.left = -2f;
            line.style.right = -2f;
            line.style.top = Length.Percent(52f);
            line.style.height = 2.5f;
            line.style.backgroundColor = UIPalette.CoralInk;
            line.style.rotate = new Rotate(Angle.Degrees(-6f));
            wrap.Add(line);
            return wrap;
        }

        VisualElement EtaRow(bool fast)
        {
            var row = UIKit.Row();
            row.name = "shop-eta";
            row.style.marginTop = 4f;
            row.Add(new UIGlyph(fast ? UIGlyphs.ShopBolt : UIGlyphs.Heli, fast ? UIPalette.CoralInk : UIPalette.InkSoft, 22f));
            var l = UIKit.Text(EtaText(fast), PhoneKit.Tiny, UIPalette.InkSoft, true);
            l.style.marginLeft = 5f;
            l.style.flexShrink = 1;
            row.Add(l);
            etaLabels.Add(new EtaLabel { label = l, fast = fast, relative = false });
            LastBuild.etas++;
            return row;
        }

        Button BuyButton(Entry e, ItemState st, bool big)
        {
            bool live = express && e.food == null;
            string text = big ? Loc.Tr("Bestellen", "Order") + " · " + Loc.Money(st.price) : Loc.Tr("Bestellen", "Order");
            var b = PhoneKit.IconButton(UIGlyphs.Heli, text, () => Buy(e), live ? UIBtnKind.Live : UIBtnKind.Primary, big ? PhoneKit.Body : PhoneKit.Small);
            b.name = "shop-buy";
            if (!big) UIKit.Pad(b, 10f, 8f, 10f, 8f);
            b.style.marginTop = big ? 16f : 8f;
            UIKit.SetEnabled(b, st.can);
            LastBuild.buyButtons++;
            return b;
        }

        /// The old buy paths, unchanged (Shop routes co-op orders through the host).
        bool Buy(Entry e)
        {
            bool ok = false;
            if (e.gear != null) ok = Shop.Buy(e.gear, express);
            else if (e.item != null) ok = Shop.BuyItem(e.item, express);
            else if (e.food != null) ok = Shop.BuyFood(e.food);
            if (ok)
            {
                UIAudio.Pop();
                if (Phone != null) Phone.Refresh(true);
            }
            else UIAudio.Nope();
            return ok;
        }

        VisualElement StatusPill(UIGlyph.DrawFn icon, string text, Color c, bool big)
        {
            var row = UIKit.Row();
            row.name = "shop-status";
            row.style.marginTop = 8f;
            row.style.justifyContent = Justify.Center;
            row.style.backgroundColor = UIPalette.A(c, 0.14f);
            UIKit.Radius(row, 999f);
            UIKit.Pad(row, big ? 12f : 8f, 14f, big ? 12f : 8f, 12f);
            row.Add(new UIGlyph(icon, c, big ? 30f : 22f));
            var l = UIKit.Text(text, big ? PhoneKit.Body : PhoneKit.Tiny, c, true);
            l.style.marginLeft = 6f;
            row.Add(l);
            LastBuild.statusPills++;
            return row;
        }

        VisualElement LockBox(long needed, long followers, bool big)
        {
            var box = UIKit.Col();
            box.name = "shop-lock";
            box.style.marginTop = 8f;
            box.style.backgroundColor = UIPalette.A(UIPalette.Coral, 0.12f);
            UIKit.Radius(box, 14f);
            UIKit.Pad(box, big ? 12f : 8f, 12f, big ? 12f : 8f, 10f);
            var row = UIKit.Row();
            row.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.CoralInk, big ? 30f : 22f));
            string t = big
                ? Loc.Tr("Ab ", "From ") + Loc.Number(needed) + Loc.Tr(" Followern", " followers")
                : Loc.Tr("ab ", "from ") + UIKit.Compact(needed);
            var l = UIKit.Text(t, big ? PhoneKit.Small : PhoneKit.Tiny, UIPalette.CoralInk, true);
            l.style.marginLeft = 6f;
            l.style.flexShrink = 1;
            row.Add(l);
            if (!big)
            {
                row.Add(UIKit.Flex());
                row.Add(new UIGlyph(UIGlyphs.Person, UIPalette.CoralInk, 20f));
            }
            box.Add(row);
            var bar = PhoneKit.Bar(needed > 0 ? Mathf.Clamp01(followers / (float)needed) : 1f, UIPalette.Coral, big ? 12f : 8f);
            bar.style.marginTop = 6f;
            box.Add(bar);
            if (big)
            {
                var prog = UIKit.Text(UIKit.Compact(followers) + " / " + UIKit.Compact(needed), PhoneKit.Tiny, UIPalette.CoralInk, true);
                prog.style.marginTop = 4f;
                prog.style.alignSelf = Align.FlexEnd;
                box.Add(prog);
            }
            LastBuild.locks++;
            return box;
        }

        // ================================================================== stats
        enum Fmt { Clock, Meters, Meters1, Mult, Percent, Plus, Number }

        struct StatLine
        {
            public UIGlyph.DrawFn glyph;
            public string label;
            public float v, cur, max;
            public Fmt fmt;
            public Color accent;
        }

        struct Feature
        {
            public UIGlyph.DrawFn glyph;
            public string text;
            public Color color;
        }

        static readonly List<StatLine> lines = new List<StatLine>(6);
        static readonly List<Feature> feats = new List<Feature>(4);

        static void AddStat(UIGlyph.DrawFn glyph, string label, float v, float cur, float max, Fmt fmt, Color accent) =>
            lines.Add(new StatLine { glyph = glyph, label = label, v = v, cur = cur, max = Mathf.Max(1e-4f, max), fmt = fmt, accent = accent });

        static void AddFeature(UIGlyph.DrawFn glyph, string text, Color color) => feats.Add(new Feature { glyph = glyph, text = text, color = color });

        static float MaxOf(GearSlot slot, System.Func<GearItem, float> f)
        {
            float m = 1e-4f;
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++) if (all[i].slot == slot) m = Mathf.Max(m, f(all[i]));
            return m;
        }

        static void CollectStats(Entry e, GameState s, ItemState st)
        {
            lines.Clear();
            feats.Clear();
            if (e.gear != null) GearStats(e.gear, s, st.current);
            else if (e.item != null) WeaponStats(e.item);
            else if (e.food != null) FoodStats(e.food);
        }

        /// Gear values; cur = the equipped item of the slot (NaN = nothing to compare with).
        static void GearStats(GearItem g, GameState s, GearItem cur)
        {
            const float NoCompare = float.NaN;
            bool cmp = cur != null && cur.slot == g.slot;
            switch (g.slot)
            {
                case GearSlot.Tank:
                {
                    float airMul = DifficultySettings.For(s.difficulty).airMultiplier;
                    AddStat(UIGlyphs.Bubbles, Loc.Tr("Luft", "Air"), g.airSeconds * airMul, cmp ? cur.airSeconds * airMul : NoCompare, MaxOf(GearSlot.Tank, x => x.airSeconds) * airMul, Fmt.Clock, UIPalette.Turquoise);
                    AddStat(UIGlyphs.Depth, Loc.Tr("Tiefe", "Depth"), g.maxDepth, cmp ? cur.maxDepth : NoCompare, MaxOf(GearSlot.Tank, x => x.maxDepth), Fmt.Meters, UIPalette.Sky);
                    break;
                }
                case GearSlot.Fins:
                    AddStat(UIGlyphs.Wave, Loc.Tr("Tempo", "Speed"), g.swimSpeed, cmp ? cur.swimSpeed : NoCompare, MaxOf(GearSlot.Fins, x => x.swimSpeed), Fmt.Mult, UIPalette.Coral);
                    break;
                case GearSlot.Lamp:
                    AddStat(UIGlyphs.Sun, Loc.Tr("Licht", "Light"), g.lampIntensity, cmp ? cur.lampIntensity : NoCompare, MaxOf(GearSlot.Lamp, x => x.lampIntensity), Fmt.Mult, UIPalette.Warn);
                    AddStat(UIGlyphs.ShopReach, Loc.Tr("Reichweite", "Range"), g.lampRange, cmp ? cur.lampRange : NoCompare, MaxOf(GearSlot.Lamp, x => x.lampRange), Fmt.Meters, UIPalette.Sun);
                    break;
                case GearSlot.Camera:
                    AddStat(UIGlyphs.Star, Loc.Tr("Bild", "Picture"), g.cameraQuality, cmp ? cur.cameraQuality : NoCompare, MaxOf(GearSlot.Camera, x => x.cameraQuality), Fmt.Mult, UIPalette.Sun);
                    AddStat(UIGlyphs.Clock, Loc.Tr("Speicher", "Storage"), g.tapeSeconds, cmp ? cur.tapeSeconds : NoCompare, MaxOf(GearSlot.Camera, x => x.tapeSeconds), Fmt.Clock, UIPalette.Coral);
                    AddStat(UIGlyphs.Plus, "Zoom", g.maxZoom, cmp ? cur.maxZoom : NoCompare, MaxOf(GearSlot.Camera, x => x.maxZoom), Fmt.Mult, UIPalette.Sky);
                    break;
                case GearSlot.CameraMod:
                {
                    if (g.zoomBonus > 1f)
                    {
                        var cam = GearDatabase.Equipped(s, GearSlot.Camera);
                        float baseZoom = cam != null ? cam.maxZoom : 1.5f;
                        float bonus = 1f;
                        var owned = s.ownedItems;
                        for (int i = 0; i < owned.Count; i++)
                        {
                            var m = GearDatabase.Get(owned[i]);
                            if (m != null && m.slot == GearSlot.CameraMod && m.zoomBonus > bonus) bonus = m.zoomBonus;
                        }
                        AddStat(UIGlyphs.Plus, "Zoom", baseZoom * Mathf.Max(bonus, g.zoomBonus), baseZoom * bonus,
                                MaxOf(GearSlot.Camera, x => x.maxZoom) * MaxOf(GearSlot.CameraMod, x => x.zoomBonus), Fmt.Mult, UIPalette.Sky);
                    }
                    if (g.nightVision) AddFeature(UIGlyphs.Moon, Loc.Tr("Nachtsicht beim Zielen", "Night vision while aiming"), UIPalette.LeafInk);
                    break;
                }
                case GearSlot.Rod:
                    AddStat(UIGlyphs.Clock, Loc.Tr("Biss-Tempo", "Bite speed"), g.fishSpeed, cmp ? cur.fishSpeed : NoCompare, MaxOf(GearSlot.Rod, x => x.fishSpeed), Fmt.Mult, UIPalette.Sky);
                    AddStat(UIGlyphs.ShopReach, Loc.Tr("Wurfweite", "Cast"), g.castMax, cmp ? cur.castMax : NoCompare, MaxOf(GearSlot.Rod, x => x.castMax), Fmt.Meters, UIPalette.Turquoise);
                    AddStat(UIGlyphs.Star, Loc.Tr("Glück", "Luck"), g.fishLuck, cmp ? cur.fishLuck : NoCompare, MaxOf(GearSlot.Rod, x => x.fishLuck), Fmt.Percent, UIPalette.Sun);
                    if (g.openSea) AddFeature(UIGlyphs.Wave, Loc.Tr("Hochsee-tauglich", "Open sea ready"), UIPalette.Sky);
                    if (g.rareBitePing) AddFeature(UIGlyphs.Sparkle, Loc.Tr("Leuchtet bei seltenen Fischen", "Glows for rare fish"), UIPalette.Sun);
                    break;
                case GearSlot.Boat:
                    AddStat(UIGlyphs.Boat, Loc.Tr("Tempo", "Speed"), g.boatSpeed, cmp ? cur.boatSpeed : NoCompare, MaxOf(GearSlot.Boat, x => x.boatSpeed), Fmt.Mult, UIPalette.Rgb(0x9A7658));
                    break;
                case GearSlot.Submarine:
                    AddStat(UIGlyphs.Depth, Loc.Tr("Tiefe", "Depth"), g.maxDepth, NoCompare, MaxOf(GearSlot.Submarine, x => x.maxDepth), Fmt.Meters, UIPalette.Sky);
                    break;
                case GearSlot.Compressor:
                    AddFeature(UIGlyphs.Compressor, Loc.Tr("Flasche an Bord füllen", "Refill tanks on board"), UIPalette.Turquoise);
                    break;
                case GearSlot.BoatWinch:
                    AddFeature(UIGlyphs.Winch, Loc.Tr("Bergung bis 40 m vom Boot", "Salvage up to 40 m from the boat"), UIPalette.Rgb(0x9A7658));
                    break;
                case GearSlot.Radio:
                    AddFeature(UIGlyphs.Radio, Loc.Tr("Funk unter Wasser", "Talk under water"), UIPalette.Rgb(0x3FA9B5));
                    break;
                default:
                    AddFeature(UIGlyphs.Gear, Loc.Tr("Ersatzteil", "Spare part"), UIPalette.Violet);
                    break;
            }
        }

        /// Spears / speargun compared with the strongest weapon in the backpack (the stick when there is none).
        static void WeaponStats(ItemDef d)
        {
            ItemDef r = null;
            if (Backpack.Count(d.id) > 0) r = d;
            else
            {
                r = ItemDatabase.Get(ItemIds.Stick);
                ItemDatabase.Sold(weaponItems);
                for (int i = 0; i < weaponItems.Count; i++)
                {
                    var w = weaponItems[i];
                    if (w != d && Backpack.Count(w.id) > 0 && (r == null || w.damage > r.damage)) r = w;
                }
            }
            ItemDatabase.Sold(weaponItems);
            float maxDamage = 1f, maxReach = 1f, maxDur = 1f;
            for (int i = 0; i < weaponItems.Count; i++)
            {
                maxDamage = Mathf.Max(maxDamage, weaponItems[i].damage);
                maxReach = Mathf.Max(maxReach, weaponItems[i].reach);
                maxDur = Mathf.Max(maxDur, weaponItems[i].durability);
            }
            bool cmp = r != null;
            AddStat(UIGlyphs.SharkFin, Loc.Tr("Wucht", "Power"), d.damage, cmp ? r.damage : float.NaN, maxDamage, Fmt.Number, UIPalette.Coral);
            AddStat(UIGlyphs.ShopReach, Loc.Tr("Reichweite", "Reach"), d.reach, cmp ? r.reach : float.NaN, maxReach, Fmt.Meters1, UIPalette.Sky);
            AddStat(UIGlyphs.ShopShield, Loc.Tr("Haltbarkeit", "Durability"), d.durability, cmp && r.durability > 0 ? r.durability : float.NaN, maxDur, Fmt.Number, UIPalette.LeafInk);
            if (d.twoHanded) AddFeature(UIGlyphs.Hand, Loc.Tr("Beidhändig", "Two-handed"), UIPalette.Violet);
            if (!d.onLand) AddFeature(UIGlyphs.Bubbles, Loc.Tr("Nur unter Wasser", "Under water only"), UIPalette.Sky);
            if (d.Sharpenable) AddFeature(UIGlyphs.Sparkle, Loc.Tr("Schleifbar", "Can be sharpened"), UIPalette.Sun);
        }

        static void FoodStats(FoodDef f)
        {
            if (f.hunger > 0f) AddStat(UIGlyphs.Hunger, Loc.Tr("Hunger", "Hunger"), f.hunger, float.NaN, 60f, Fmt.Plus, UIPalette.Sun);
            if (f.thirst > 0f) AddStat(UIGlyphs.Thirst, Loc.Tr("Durst", "Thirst"), f.thirst, float.NaN, 60f, Fmt.Plus, UIPalette.Sky);
            if (f.value > 0f) AddFeature(BuffGlyph(f.buff), BuffText(f), UIPalette.Turquoise);
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

        static string FormatValue(Fmt f, float v)
        {
            switch (f)
            {
                case Fmt.Clock: return UIKit.ClockShort(Mathf.RoundToInt(v)) + " min";
                case Fmt.Meters: return Loc.Meters(v);
                case Fmt.Meters1: return v.ToString("0.#", Loc.Culture) + " m";
                case Fmt.Mult: return UIKit.Mult(v);
                case Fmt.Percent: return Mathf.RoundToInt(v * 100f) + " %";
                case Fmt.Plus: return "+" + Mathf.RoundToInt(v);
                default: return Mathf.RoundToInt(v).ToString(Loc.Culture);
            }
        }

        static string FormatDelta(Fmt f, float d)
        {
            string sign = d > 0f ? "+" : "−";
            float a = Mathf.Abs(d);
            switch (f)
            {
                case Fmt.Clock: return sign + UIKit.ClockShort(Mathf.RoundToInt(a));
                case Fmt.Meters: return sign + Loc.Meters(a);
                case Fmt.Meters1: return sign + a.ToString("0.#", Loc.Culture) + " m";
                case Fmt.Mult: return sign + a.ToString("0.##", Loc.Culture) + "×";
                case Fmt.Percent: return sign + Mathf.RoundToInt(a * 100f) + " %";
                default: return sign + Mathf.RoundToInt(a);
            }
        }

        /// One stat: icon + bar (+ value). Compared bars: grey = what you have, green = the gain; worse = a faint
        /// ghost up to your current value behind the grey bar. Uncompared bars use the stat's accent colour.
        VisualElement StatRow(StatLine l, bool big)
        {
            bool cmp = !float.IsNaN(l.cur);
            float v01 = Mathf.Clamp01(l.v / l.max), c01 = cmp ? Mathf.Clamp01(l.cur / l.max) : 0f;
            float delta = cmp ? l.v - l.cur : 0f;
            float eps = Mathf.Max(1e-4f, l.max * 0.002f);
            int sign = !cmp ? 0 : delta > eps ? 1 : delta < -eps ? -1 : 0;
            var bar = Bar(v01, c01, cmp, sign, l.accent, big ? 14f : 10f);
            string value = FormatValue(l.fmt, l.v);
            Color valueColor = !cmp ? UIPalette.Ink : sign > 0 ? UIPalette.LeafInk : UIPalette.InkSoft;
            LastBuild.statBars++;
            if (!big)
            {
                var row = UIKit.Row();
                row.name = "shop-statbar";
                row.style.marginTop = 6f;
                row.Add(new UIGlyph(l.glyph, sign > 0 ? UIPalette.LeafInk : UIPalette.InkSoft, 22f));
                bar.style.flexGrow = 1;
                bar.style.flexShrink = 1;
                bar.style.minWidth = 30f;
                bar.style.marginLeft = 6f;
                bar.style.marginRight = 6f;
                row.Add(bar);
                var vl = UIKit.Text(value, PhoneKit.Tiny, valueColor, true);
                vl.style.flexShrink = 0;
                row.Add(vl);
                return row;
            }
            var col = UIKit.Col();
            col.name = "shop-statbar";
            col.style.marginTop = 12f;
            var top = UIKit.Row();
            top.Add(new UIGlyph(l.glyph, UIWidgets.Ink(l.accent), 30f));
            var lab = UIKit.Text(l.label, PhoneKit.Small, UIPalette.InkSoft, true);
            lab.style.marginLeft = 10f;
            lab.style.flexShrink = 1;
            top.Add(lab);
            top.Add(UIKit.Flex());
            if (cmp)
            {
                var pill = UIKit.Pill(sign == 0 ? "=" : FormatDelta(l.fmt, delta), sign > 0 ? UIPalette.A(UIPalette.Leaf, 0.4f) : UIPalette.A(UIPalette.Ink, 0.08f),
                                      sign > 0 ? UIPalette.LeafInk : UIPalette.InkSoft, PhoneKit.Tiny);
                pill.style.marginRight = 10f;
                top.Add(pill);
            }
            top.Add(UIKit.Text(value, PhoneKit.Body, valueColor, true));
            col.Add(top);
            bar.style.marginTop = 6f;
            col.Add(bar);
            return col;
        }

        static VisualElement Bar(float v01, float c01, bool cmp, int sign, Color accent, float h)
        {
            var track = UIKit.Box();
            track.pickingMode = PickingMode.Ignore;
            track.style.height = h;
            track.style.flexShrink = 0;
            track.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.07f);
            track.style.overflow = Overflow.Hidden;
            UIKit.Radius(track, h * 0.5f);
            if (!cmp) track.Add(Fill(v01, accent, h));
            else if (sign > 0) { track.Add(Fill(v01, BarGreen, h)); track.Add(Fill(c01, UIPalette.Silhouette, h)); }
            else if (sign == 0) track.Add(Fill(v01, UIPalette.Silhouette, h));
            else { track.Add(Fill(c01, UIPalette.A(UIPalette.Ink, 0.13f), h)); track.Add(Fill(v01, UIPalette.Silhouette, h)); }
            return track;
        }

        static VisualElement Fill(float w01, Color c, float h)
        {
            var f = UIKit.Box();
            f.pickingMode = PickingMode.Ignore;
            f.style.position = Position.Absolute;
            f.style.left = 0f;
            f.style.top = 0f;
            f.style.bottom = 0f;
            f.style.width = Length.Percent(w01 <= 0f ? 0f : Mathf.Max(w01 * 100f, 4f));
            f.style.backgroundColor = c;
            UIKit.Radius(f, h * 0.5f);
            return f;
        }

        VisualElement FeatureChip(Feature f, bool big)
        {
            var row = UIKit.Row();
            row.name = "shop-feature";
            row.style.marginTop = big ? 12f : 6f;
            row.style.alignSelf = Align.FlexStart;
            row.style.backgroundColor = UIPalette.A(f.color, 0.16f);
            UIKit.Radius(row, 999f);
            UIKit.Pad(row, big ? 8f : 5f, 12f, big ? 8f : 5f, 8f);
            Color ink = UIWidgets.Ink(f.color);
            row.Add(new UIGlyph(f.glyph, ink, big ? 28f : 20f));
            var l = PhoneKit.T(f.text, big ? PhoneKit.Small : PhoneKit.Tiny, ink, true);
            l.style.marginLeft = 6f;
            l.style.flexShrink = 1;
            row.Add(l);
            LastBuild.features++;
            return row;
        }

        // ================================================================== ETA (the Shop delivery timing rules)
        static string DayWord(bool tomorrow) => tomorrow ? Loc.Tr("morgen ", "tomorrow ") : Loc.Tr("heute ", "today ");

        /// "heute 14:15" (express: Shop.ExpressHours) or "heute 15:10–16:10" (standard: 2–3 h, after 20:00 → "morgen
        /// 07:12", small hours → the first morning flight) – the same rules as Shop.ArrivalFor, without its dice.
        public static string EtaText(bool fast)
        {
            var s = UIKit.State;
            if (s == null) return "";
            float now = Mathf.Repeat(s.timeOfDay, 1f);
            if (fast)
            {
                float t = now + Shop.ExpressHours / 24f;
                return DayWord(t >= 1f) + GameManager.Clock(t);
            }
            if (now > Shop.LastStandardOrder) return DayWord(true) + GameManager.Clock(Shop.MorningArrival);
            float a = Mathf.Max(now + Shop.StandardHoursMin / 24f, Shop.MorningArrival);
            float b = Mathf.Max(now + Shop.StandardHoursMax / 24f, Shop.MorningArrival);
            if (b - a < 1f / 1440f) return DayWord(false) + GameManager.Clock(a);
            return DayWord(false) + GameManager.Clock(a) + "–" + GameManager.Clock(b);
        }

        /// "in 45 min" / "in 2–3 h" / "morgen früh".
        public static string RelText(bool fast)
        {
            var s = UIKit.State;
            if (s == null) return "";
            if (fast) return Loc.Tr("in ", "in ") + Mathf.RoundToInt(Shop.ExpressHours * 60f) + " min";
            float now = Mathf.Repeat(s.timeOfDay, 1f);
            if (now > Shop.LastStandardOrder) return Loc.Tr("morgen früh", "tomorrow morning");
            float a = (Mathf.Max(now + Shop.StandardHoursMin / 24f, Shop.MorningArrival) - now) * 24f;
            float b = (Mathf.Max(now + Shop.StandardHoursMax / 24f, Shop.MorningArrival) - now) * 24f;
            return "in " + a.ToString("0.#", Loc.Culture) + "–" + b.ToString("0.#", Loc.Culture) + " h";
        }

        static int EtaKey(GameState s) => s == null ? -1 : s.day * 288 + Mathf.FloorToInt(Mathf.Repeat(s.timeOfDay, 1f) * 288f);

        // ================================================================== detail page
        void BuildDetail(VisualElement body, Entry e, GameState s)
        {
            LastBuild.detail = true;
            var st = StateOf(e, s);
            Catalogue(e.category, page);
            int idx = page.IndexOf(e);

            var top = UIKit.Box();
            top.style.marginTop = 12f;
            var art = Art(e, st, DetailArt, DetailIcon, true);
            top.Add(art);
            var ribbon = Ribbon(st, true);
            if (ribbon != null) top.Add(ribbon);
            if (idx >= 0 && page.Count > 1)
            {
                int n = page.Count;
                string prev = page[(idx - 1 + n) % n].key, next = page[(idx + 1) % n].key;
                top.Add(FlipButton(UIGlyphs.ChevronLeft, prev, true));
                top.Add(FlipButton(UIGlyphs.ChevronRight, next, false));
            }
            body.Add(top);
            if (idx >= 0 && page.Count > 1)
            {
                // carousel dots: where this item sits in its category
                var dots = UIKit.Row();
                dots.pickingMode = PickingMode.Ignore;
                dots.style.justifyContent = Justify.Center;
                dots.style.marginTop = 10f;
                for (int i = 0; i < page.Count; i++)
                {
                    var d = UIKit.Dot(i == idx ? UIPalette.Ink : UIPalette.A(UIPalette.Ink, 0.18f), i == idx ? 12f : 10f);
                    d.style.marginLeft = 4f;
                    d.style.marginRight = 4f;
                    dots.Add(d);
                }
                body.Add(dots);
            }

            var title = PhoneKit.T(e.Name, PhoneKit.Title, UIPalette.Ink, true);
            title.style.marginTop = 14f;
            body.Add(title);
            var meta = UIKit.Row();
            meta.style.marginTop = 6f;
            meta.style.flexWrap = Wrap.Wrap;
            var catPill = UIKit.Row();
            catPill.style.backgroundColor = UIPalette.A(CategoryColor(e.category), 0.2f);
            UIKit.Radius(catPill, 999f);
            UIKit.Pad(catPill, 5f, 12f, 5f, 8f);
            catPill.Add(new UIGlyph(CategoryGlyph(e.category), UIWidgets.Ink(CategoryColor(e.category)), 22f));
            var cl = UIKit.Text(CategoryName(e.category), PhoneKit.Tiny, UIWidgets.Ink(CategoryColor(e.category)), true);
            cl.style.marginLeft = 6f;
            catPill.Add(cl);
            meta.Add(catPill);
            if (e.gear != null && e.gear.tier > 0)
            {
                var tier = UIKit.Pill(Loc.Tr("Stufe ", "Tier ") + e.gear.tier, UIPalette.A(UIPalette.Ink, 0.07f), UIPalette.InkSoft, PhoneKit.Tiny);
                tier.style.marginLeft = 8f;
                meta.Add(tier);
            }
            body.Add(meta);
            var desc = PhoneKit.T(e.Description, PhoneKit.Small, UIPalette.InkSoft);
            desc.style.marginTop = 8f;
            body.Add(desc);

            // what it is compared with
            string baseKey = null, baseName = null;
            UIGlyph.DrawFn baseGlyph = null;
            if (e.gear != null && st.current != null && !st.equipped) { baseKey = st.current.id; baseName = st.current.Name; baseGlyph = UIGlyphs.ForSlot(st.current.slot); }
            else if (e.item != null && Backpack.Count(e.item.id) == 0)
            {
                ItemDef r = ItemDatabase.Get(ItemIds.Stick);
                ItemDatabase.Sold(weaponItems);
                for (int i = 0; i < weaponItems.Count; i++)
                {
                    var w = weaponItems[i];
                    if (w != e.item && Backpack.Count(w.id) > 0 && (r == null || w.damage > r.damage)) r = w;
                }
                if (r != null) { baseKey = ItemDatabase.DeliveryId(r); baseName = r.Name; baseGlyph = UIGlyphs.ForItemId(r.id) ?? (UIGlyph.DrawFn)UIGlyphs.ItemSpear; }
            }

            CollectStats(e, s, st);
            if (lines.Count + feats.Count > 0)
            {
                var card = PhoneKit.Card(18f);
                card.name = "shop-stats";
                card.style.marginTop = 14f;
                if (baseKey != null)
                {
                    var cmpRow = UIKit.Row();
                    cmpRow.style.marginBottom = 2f;
                    var bpic = UIKit.Box();
                    UIKit.Size(bpic, 52f, 52f);
                    UIKit.Radius(bpic, 14f);
                    UIKit.Center(bpic);
                    bpic.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.06f);
                    var bicon = new UIItemIcon(baseKey, baseGlyph, e.item != null ? Color.white : UIPalette.InkSoft, 50f);
                    bpic.Add(bicon);
                    icons.Add(bicon);
                    cmpRow.Add(bpic);
                    var bc = UIKit.Col();
                    bc.style.marginLeft = 10f;
                    bc.style.flexShrink = 1;
                    bc.Add(UIKit.Text(Loc.Tr("Du hast (Vergleich):", "You have (compared):"), PhoneKit.Tiny, UIPalette.InkSoft, true));
                    bc.Add(PhoneKit.T(baseName, PhoneKit.Small, UIPalette.Ink, true));
                    cmpRow.Add(bc);
                    card.Add(cmpRow);
                }
                for (int i = 0; i < lines.Count; i++) card.Add(StatRow(lines[i], true));
                for (int i = 0; i < feats.Count; i++) card.Add(FeatureChip(feats[i], true));
                body.Add(card);
            }

            body.Add(OrderBlock(e, st, s));
        }

        VisualElement FlipButton(UIGlyph.DrawFn glyph, string key, bool left)
        {
            const float size = 60f;
            var b = UIKit.Box();
            b.name = "shop-flip";
            UIKit.Size(b, size, size);
            UIKit.Radius(b, size * 0.5f);
            UIKit.Center(b);
            b.style.position = Position.Absolute;
            b.style.top = Length.Percent(50f);
            b.style.marginTop = -size * 0.5f;
            if (left) b.style.left = 12f; else b.style.right = 12f;
            b.Add(new UIGlyph(glyph, UIPalette.Ink, size * 0.5f));
            UIKit.Clickable(b, () => OpenDetail(key), UIPalette.A(Color.white, 0.85f), Color.white);
            return b;
        }

        VisualElement OrderBlock(Entry e, ItemState st, GameState s)
        {
            var card = PhoneKit.Card(18f);
            card.name = "shop-order";
            if (st.equipped) { card.Add(StatusPill(UIGlyphs.Check, Loc.Tr("Ausgerüstet", "Equipped"), UIPalette.LeafInk, true)); return card; }
            if (st.owned)
            {
                card.Add(StatusPill(UIGlyphs.Bag, Loc.Tr("Im Besitz", "Owned"), UIPalette.TurquoiseInk, true));
                if (CanEquip(e.gear))
                {
                    var g = e.gear;
                    var eq = PhoneKit.IconButton(UIGlyphs.Check, Loc.Tr("Ausrüsten", "Equip"), () =>
                    {
                        if (Gear.Equip(g.id)) { UIAudio.Pop(); if (Phone != null) Phone.Refresh(true); }
                        else UIAudio.Nope();
                    }, UIBtnKind.Accent, PhoneKit.Body);
                    eq.name = "shop-equip";
                    eq.style.marginTop = 12f;
                    card.Add(eq);
                }
                return card;
            }
            if (st.ordered)
            {
                card.Add(StatusPill(UIGlyphs.Heli, Loc.Tr("Unterwegs", "On its way"), UIPalette.SunInk, true));
                var d = Shop.FindDelivery(e.key);
                if (d != null)
                {
                    string when = d.dropped ? MishapLine(d.Mishap) : Loc.Tr("Ankunft ", "Arrives ") + DayWord(d.arrivalDay > s.day) + UIKit.TimeOfDay(d.arrivalTime);
                    var l = PhoneKit.T(when, PhoneKit.Small, UIPalette.InkSoft, true);
                    l.style.marginTop = 8f;
                    l.style.alignSelf = Align.Center;
                    card.Add(l);
                }
                return card;
            }
            if (st.comingSoon) { card.Add(StatusPill(UIGlyphs.Clock, Loc.Tr("Bald", "Soon"), UIPalette.Violet, true)); return card; }

            card.Add(PriceRow(st, s, PhoneKit.Big));
            if (e.food != null) card.Add(DeliveryOption(e, true, s, false));
            else
            {
                card.Add(DeliveryOption(e, false, s, true));
                card.Add(DeliveryOption(e, true, s, true));
            }
            if (st.locked) card.Add(LockBox(st.followersNeeded, s.followers, true));
            else
            {
                var b = BuyButton(e, st, true);
                card.Add(b);
                if (!st.can && !string.IsNullOrEmpty(st.reason))
                {
                    var w = PhoneKit.T(st.reason, PhoneKit.Small, UIPalette.CoralInk, true);
                    w.style.marginTop = 6f;
                    w.style.alignSelf = Align.Center;
                    card.Add(w);
                }
            }
            return card;
        }

        static bool CanEquip(GearItem g)
        {
            if (g == null) return false;
            switch (g.slot)
            {
                case GearSlot.Tank: case GearSlot.Fins: case GearSlot.Lamp: case GearSlot.Camera: case GearSlot.Rod: case GearSlot.Radio: return true;
                default: return false;
            }
        }

        /// A delivery choice (normal / express) with its ETA and price; tapping it switches the express mode.
        VisualElement DeliveryOption(Entry e, bool isExpress, GameState s, bool selectable)
        {
            bool sel = !selectable || express == isExpress;
            bool fast = isExpress || (e.gear != null && Shop.FastDelivery(e.gear, false));
            long price = e.gear != null ? Shop.Price(e.gear, isExpress) : e.item != null ? Shop.ItemPrice(e.item, isExpress) : e.food != null ? e.food.price : 0;
            Color accent = isExpress ? UIPalette.Coral : UIPalette.Turquoise, ink = isExpress ? UIPalette.CoralInk : UIPalette.TurquoiseInk;
            var row = UIKit.Row();
            row.name = "shop-option";
            row.style.marginTop = 10f;
            UIKit.Radius(row, 18f);
            UIKit.Pad(row, 12f, 14f, 12f, 12f);
            row.style.backgroundColor = sel ? UIPalette.A(accent, 0.14f) : UIPalette.A(UIPalette.Ink, 0.04f);
            UIKit.Border(row, 3f, sel ? ink : new Color(0f, 0f, 0f, 0f));
            if (selectable)
            {
                var dot = UIKit.Box();
                UIKit.Size(dot, 26f, 26f);
                UIKit.Radius(dot, 13f);
                UIKit.Border(dot, 3f, sel ? ink : UIPalette.A(UIPalette.Ink, 0.25f));
                UIKit.Center(dot);
                if (sel) dot.Add(UIKit.Dot(ink, 12f));
                dot.style.marginRight = 10f;
                row.Add(dot);
            }
            row.Add(new UIGlyph(isExpress ? UIGlyphs.ShopBolt : UIGlyphs.Heli, sel ? ink : UIPalette.InkSoft, 34f));
            var col = UIKit.Col();
            col.style.marginLeft = 10f;
            col.style.flexShrink = 1;
            col.style.flexGrow = 1;
            string title = e.food != null ? Loc.Tr("Express inklusive", "Express included") : isExpress ? "Express" : Loc.Tr("Normal", "Standard");
            col.Add(UIKit.Text(title, PhoneKit.Small, UIPalette.Ink, true));
            var eta = PhoneKit.T(EtaText(fast) + " · " + RelText(fast), PhoneKit.Tiny, UIPalette.InkSoft, true);
            eta.name = "shop-eta";
            col.Add(eta);
            etaLabels.Add(new EtaLabel { label = eta, fast = fast, relative = true });
            row.Add(col);
            var pl = UIKit.Text(Loc.Money(price), PhoneKit.Small, sel ? UIPalette.Ink : UIPalette.InkSoft, true);
            pl.style.marginLeft = 8f;
            row.Add(pl);
            if (selectable)
            {
                UIKit.IgnorePicking(row);
                row.pickingMode = PickingMode.Position;
                row.RegisterCallback<ClickEvent>(_ =>
                {
                    if (express == isExpress) return;
                    UIAudio.Click();
                    express = isExpress;
                    if (Phone != null) Phone.Refresh(true);
                });
            }
            LastBuild.etas++;
            LastBuild.options++;
            return row;
        }

        // ================================================================== per frame
        public override void Tick(float dt)
        {
            PumpIcons();
            tickTimer -= dt;
            if (tickTimer > 0f) return;
            tickTimer = 0.5f;
            for (int i = 0; i < etas.Count; i++)
            {
                var e = etas[i];
                UpdateEta(ref e);
                etas[i] = e;
            }
            var s = UIKit.State;
            if (s == null || etaLabels.Count == 0) return;
            int key = EtaKey(s);
            if (key == etaKey) return;
            etaKey = key;
            for (int i = 0; i < etaLabels.Count; i++)
            {
                var l = etaLabels[i];
                if (l.label == null) continue;
                l.label.text = l.relative ? EtaText(l.fast) + " · " + RelText(l.fast) : EtaText(l.fast);
            }
        }

        /// Lazy pictures: only pictures that are (about to be) visible are rendered – the visible ones first (≤ 2 per
        /// frame, ≤ ~12 ms), then, when nothing visible is missing, one picture within half a screen above / a screen
        /// below the view every 4th frame, so scrolling rarely shows a glyph. Off-page pictures are never rendered.
        void PumpIcons()
        {
            if (pending <= 0) { pendingVisible = 0; return; }
            pumpFrame++;
            Rect view = viewport != null && viewport.panel != null ? viewport.worldBound : new Rect(0f, 0f, 0f, 0f);
            bool viewOk = view.width > 1f && view.height > 1f && !float.IsNaN(view.height);
            Rect near = viewOk ? new Rect(view.x, view.y - view.height * 0.5f, view.width, view.height * 2f) : view;
            float t0 = Time.realtimeSinceStartup;
            int rendered = 0, unresolved = 0, unresolvedVisible = 0;
            UIItemIcon soon = null;
            for (int i = 0; i < icons.Count; i++)
            {
                var ic = icons[i];
                if (ic.Resolved || ic.TryResolve(false)) continue;
                bool laidOut = viewOk && ic.panel != null;
                Rect wb = laidOut ? ic.worldBound : view;
                bool seen = laidOut && view.Overlaps(wb);
                if (seen && rendered < MaxIconsPerFrame && Time.realtimeSinceStartup - t0 < 0.012f)
                {
                    rendered++;
                    if (ic.TryResolve(true)) continue;
                }
                unresolved++;
                if (seen) unresolvedVisible++;
                else if (soon == null && laidOut && near.Overlaps(wb)) soon = ic;
            }
            if (rendered == 0 && soon != null && (pumpFrame & 3) == 0 && soon.TryResolve(true)) unresolved--;
            pending = unresolved;
            pendingVisible = viewOk ? unresolvedVisible : unresolved;   // before the first layout pass nothing is known
        }
    }
}
