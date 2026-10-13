using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// "Fischmarkt": caught fish wait in the (shared) cooler box until they are listed; listings sell by themselves
    /// within ~20–60 s (FishMarket.Tick on the host). Shows the cooler (sell one / sell all), active listings with a
    /// live countdown and the latest sales.
    public class UIAppMarket : UIPhoneApp
    {
        struct LiveRow
        {
            public FishListing listing;
            public Label time;
            public UIBar bar;
            public int shown;
        }

        readonly List<LiveRow> live = new List<LiveRow>();
        readonly Dictionary<int, FishItem> listed = new Dictionary<int, FishItem>();
        readonly HashSet<int> stillThere = new HashSet<int>();
        readonly List<int> gone = new List<int>();
        readonly List<FishItem> sold = new List<FishItem>();
        float tickTimer;

        public override string Id => "market";
        public override string Title => Loc.Tr("Fischmarkt", "Fish Market");
        public override Color Color => UIPalette.Sun;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Market;

        public override int Badge
        {
            get
            {
                var s = UIKit.State;
                return s != null ? Mathf.Min(99, s.fishInventory.Count) : 0;
            }
        }

        public void ResetSession()
        {
            listed.Clear();
            sold.Clear();
            live.Clear();
            Snapshot();
        }

        /// FishMarket.Changed: detect sold listings for the history, then rebuild.
        public void OnMarketChanged()
        {
            var s = UIKit.State;
            if (s == null) return;
            stillThere.Clear();
            for (int i = 0; i < s.fishListings.Count; i++) { var l = s.fishListings[i]; if (l?.fish != null) stillThere.Add(l.fish.uid); }
            for (int i = 0; i < s.fishInventory.Count; i++) { var f = s.fishInventory[i]; if (f != null) stillThere.Add(f.uid); }
            gone.Clear();
            foreach (var kv in listed) if (!stillThere.Contains(kv.Key)) gone.Add(kv.Key);
            for (int i = 0; i < gone.Count; i++)
            {
                sold.Insert(0, listed[gone[i]]);
                listed.Remove(gone[i]);
            }
            if (sold.Count > 12) sold.RemoveRange(12, sold.Count - 12);
            Snapshot();
            Phone?.MarkDirty();
        }

        void Snapshot()
        {
            var s = UIKit.State;
            if (s == null) return;
            for (int i = 0; i < s.fishListings.Count; i++)
            {
                var l = s.fishListings[i];
                if (l?.fish != null) listed[l.fish.uid] = l.fish;
            }
        }

        bool findsTab;

        /// Tests / AutoShot (round 3 UI step): switch between the fish tab and the „Funde“ tab.
        public void DebugFindsTab(bool on) { findsTab = on; Phone.Refresh(false); }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;
            live.Clear();

            // K.3 tabs: Fische | Funde
            var finds = Finds.Inventory;
            // round 3: the count includes the treasure from chests in the backpack
            int findCount = finds.Count + ValuableStacks(out _);
            var tabs = new UISegmented(new[] { Loc.Tr("Fische", "Fish"), Loc.Tr("Funde", "Finds") + (findCount > 0 ? " · " + findCount : "") },
                findsTab ? 1 : 0, i => { findsTab = i == 1; Phone.Refresh(false); }, UIPalette.SunInk, PhoneKit.Small);
            tabs.style.marginBottom = 12f;
            body.Add(tabs);
            if (findsTab) { BuildFinds(body); return; }

            // totals
            var stats = UIKit.Row();
            stats.Add(PhoneKit.Stat(UIGlyphs.Fish, UIPalette.Sky, s.fishInventory.Count.ToString()));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Tag, UIPalette.SunInk, s.fishListings.Count.ToString()));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, UIKit.Compact(s.fishMoney) + " $", UIPalette.LeafInk));
            body.Add(stats);

            // cooler box
            var head = PhoneKit.Section(Loc.Tr("Kühlbox", "Cooler box"), UIGlyphs.Fish);
            body.Add(head);
            if (s.fishInventory.Count == 0)
            {
                body.Add(PhoneKit.Empty(UIGlyphs.Rod, Loc.Tr("Leer – ab zum Angeln an den Steg!", "Empty – go fishing at the dock!")));
            }
            else
            {
                var all = PhoneKit.IconButton(UIGlyphs.Tag, Loc.Tr("Alle anbieten", "List all") + " · " + Loc.Money(FishMarket.InventoryValue), () =>
                {
                    int n = FishMarket.ListAll();
                    if (n > 0) UIAudio.Pop();
                    Phone.MarkDirty();
                }, UIBtnKind.Primary, PhoneKit.Body);
                all.style.marginBottom = 12f;
                body.Add(all);
                for (int i = s.fishInventory.Count - 1; i >= 0; i--)
                {
                    var f = s.fishInventory[i];
                    if (f == null) continue;
                    body.Add(FishRow(f, true));
                }
            }

            // listings
            if (s.fishListings.Count > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Im Angebot", "Listed"), UIGlyphs.Tag));
                for (int i = 0; i < s.fishListings.Count; i++)
                {
                    var l = s.fishListings[i];
                    if (l?.fish == null) continue;
                    body.Add(ListingRow(l));
                }
            }

            // history
            if (sold.Count > 0 || s.fishSold > 0)
            {
                body.Add(PhoneKit.Section(Loc.Tr("Verkauft", "Sold") + " · " + s.fishSold, UIGlyphs.Check));
                for (int i = 0; i < sold.Count; i++) body.Add(SoldRow(sold[i]));
            }
        }

        /// Valuable stacks in the pouch (count of stacks) and their total worth.
        static int ValuableStacks(out long worth)
        {
            worth = 0;
            int n = 0;
            var pouch = Backpack.Pouch;
            if (pouch == null) return 0;
            for (int i = 0; i < pouch.Count; i++)
            {
                var st = pouch[i];
                var d = st != null ? st.Def : null;
                if (d == null || d.category != ItemCategory.Valuable || st.count <= 0) continue;
                n++;
                worth += (long)d.value * st.count;
            }
            return n;
        }

        /// Underwater finds and salvage (K.3) + the treasure from chests (round 3): a summary on top (items · worth ·
        /// sell everything), then the treasure, then the salvage; the hermits' barter as a hint once the camp is known.
        void BuildFinds(VisualElement body)
        {
            var list = Finds.Inventory;
            int stacks = ValuableStacks(out long treasureWorth);
            long total = treasureWorth + Finds.InventoryValue;
            if (stacks + list.Count > 0)
            {
                var stats = UIKit.Row();
                stats.Add(PhoneKit.Stat(UIGlyphs.Chest, UIPalette.SunInk, (stacks + list.Count).ToString()));
                stats.Add(UIKit.HSpacer(8f));
                stats.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, UIKit.Compact(total) + " $", UIPalette.LeafInk));
                body.Add(stats);
                if (stacks > 0 && list.Count > 0)
                {
                    var everything = PhoneKit.IconButton(UIGlyphs.Coin, Loc.Tr("Alles verkaufen", "Sell everything") + " · " + Loc.Money(total), () =>
                    {
                        long a = SeaTreasure.SellAll(), b = Finds.SellAll();
                        if (a + b > 0) UIAudio.Pop();
                        Phone.Refresh(true);
                    }, UIBtnKind.Primary, PhoneKit.Body);
                    everything.style.marginTop = 10f;
                    body.Add(everything);
                }
            }
            bool treasure = BuildValuables(body, stacks > 0 && list.Count > 0);
            if (MapDiscovery.CampKnown && (treasure || list.Count > 0))
            {
                var hint = UIKit.Row();
                hint.style.marginTop = 4f;
                hint.style.marginBottom = 6f;
                hint.Add(new UIGlyph(UIGlyphs.Hut, UIPalette.Rgb(0xD9762B), 26f));
                var ht = PhoneKit.T(" " + Loc.Tr("Schätze auch im Lager tauschen", "Treasure also trades at the camp") + " (" + GameInput.BindingLabel(GameAction.Gestures) + ")", PhoneKit.Tiny, UIPalette.InkSoft, true);
                ht.style.flexShrink = 1;
                hint.Add(ht);
                body.Add(hint);
            }
            if (list.Count == 0)
            {
                if (!treasure) body.Add(PhoneKit.Empty(UIGlyphs.Chest, Loc.Tr("Noch keine Funde – Truhen öffnen, im Sand glitzert's!", "No finds yet – open chests, look for glints!")));
                return;
            }
            if (treasure) body.Add(PhoneKit.Section(Loc.Tr("Bergungsgut", "Salvage"), UIGlyphs.Winch));
            var all = PhoneKit.IconButton(UIGlyphs.Coin, Loc.Tr("Alle verkaufen", "Sell all") + " · " + Loc.Money(Finds.InventoryValue), () =>
            {
                if (Finds.SellAll() > 0) UIAudio.Pop();
                Phone.Refresh(true);
            }, UIBtnKind.Primary, PhoneKit.Body);
            all.style.marginBottom = 12f;
            body.Add(all);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var it = list[i];
                if (it == null) continue;
                var def = FindsDatabase.Kind(it.kindId);
                var row = UIKit.Row();
                row.style.backgroundColor = Color.white;
                UIKit.Radius(row, 20f);
                UIKit.Pad(row, 10f, 12f, 10f, 14f);
                row.style.marginBottom = 8f;
                bool heavy = def != null && def.heavy;
                row.Add(UIWidgets.IconTile(heavy ? UIGlyphs.Winch : UIGlyphs.Chest, def != null && def.rare ? UIPalette.Violet : UIPalette.Sun, 56f));
                var col = UIKit.Col();
                col.style.marginLeft = 12f;
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                col.Add(PhoneKit.T(def != null ? def.Name : it.kindId, PhoneKit.Body, UIPalette.Ink, true));
                if (def != null && def.rare) col.Add(UIKit.Text(Loc.Tr("Selten!", "Rare!"), PhoneKit.Tiny, UIPalette.Violet, true));
                row.Add(col);
                var price = UIKit.Text(Loc.Money(it.value), PhoneKit.Body, UIPalette.LeafInk, true);
                price.style.marginRight = 10f;
                row.Add(price);
                string id = it.id;
                var b = PhoneKit.IconButton(UIGlyphs.Coin, "", () =>
                {
                    if (Finds.Sell(id) > 0) UIAudio.Pop();
                    Phone.Refresh(true);
                }, UIBtnKind.Primary, PhoneKit.Body);
                UIKit.Pad(b, 12f, 16f, 12f, 16f);
                row.Add(b);
                body.Add(row);
            }
        }

        /// Round 3 (fauna step, GDD §20 Q20): treasure from sunken chests / wrecks rides in the player's backpack
        /// (ItemCategory.Valuable) – sold here, one stack or everything. Returns true when there was any.
        bool BuildValuables(VisualElement body, bool compact = false)
        {
            var pouch = Backpack.Pouch;
            bool any = false;
            for (int i = 0; i < pouch.Count; i++)
            {
                var st = pouch[i];
                var d = st != null ? st.Def : null;
                if (d != null && d.category == ItemCategory.Valuable && st.count > 0) { any = true; break; }
            }
            if (!any) return false;
            body.Add(PhoneKit.Section(Loc.Tr("Schätze im Rucksack", "Treasure in the backpack"), UIGlyphs.Chest));
            if (!compact)
            {
                var all = PhoneKit.IconButton(UIGlyphs.Coin, Loc.Tr("Alle verkaufen", "Sell all") + " · " + Loc.Money(SeaTreasure.ValuablesWorth), () =>
                {
                    if (SeaTreasure.SellAll() > 0) UIAudio.Pop();
                    Phone.Refresh(true);
                }, UIBtnKind.Primary, PhoneKit.Body);
                all.style.marginBottom = 12f;
                body.Add(all);
            }
            for (int i = 0; i < pouch.Count; i++)
            {
                var st = pouch[i];
                var d = st != null ? st.Def : null;
                if (d == null || d.category != ItemCategory.Valuable || st.count <= 0) continue;
                var row = UIKit.Row();
                row.style.backgroundColor = Color.white;
                UIKit.Radius(row, 20f);
                UIKit.Pad(row, 10f, 12f, 10f, 14f);
                row.style.marginBottom = 8f;
                var draw = UIGlyphs.ForItemId(d.id);
                row.Add(UIWidgets.IconTile(draw ?? (UIGlyph.DrawFn)UIGlyphs.Chest, UIPalette.Sun, 56f));
                var col = UIKit.Col();
                col.style.marginLeft = 12f;
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                col.Add(PhoneKit.T(d.Name + (st.count > 1 ? "  ×" + st.count : ""), PhoneKit.Body, UIPalette.Ink, true));
                row.Add(col);
                var price = UIKit.Text(Loc.Money(d.value * st.count), PhoneKit.Body, UIPalette.LeafInk, true);
                price.style.marginRight = 10f;
                row.Add(price);
                string id = d.id;
                int n = st.count;
                var b = PhoneKit.IconButton(UIGlyphs.Coin, "", () =>
                {
                    if (SeaTreasure.Sell(id, n) > 0) UIAudio.Pop();
                    Phone.Refresh(true);
                }, UIBtnKind.Primary, PhoneKit.Body);
                UIKit.Pad(b, 12f, 16f, 12f, 16f);
                row.Add(b);
                body.Add(row);
            }
            return true;
        }

        static Color FishColor(FishItem f)
        {
            var k = f.Kind;
            return k != null ? k.back : UIPalette.Sky;
        }

        VisualElement FishRow(FishItem f, bool canSell)
        {
            var row = UIKit.Row();
            row.style.backgroundColor = Color.white;
            UIKit.Radius(row, 20f);
            UIKit.Pad(row, 10f, 12f, 10f, 14f);
            row.style.marginBottom = 8f;
            var icon = new UIGlyph(UIGlyphs.Fish, FishColor(f), 60f);
            row.Add(icon);
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text(f.Name, PhoneKit.Body, UIPalette.Ink, true));
            col.Add(UIKit.Text(f.weightKg.ToString("0.00", Loc.Culture) + " kg", PhoneKit.Small, UIPalette.InkSoft));
            row.Add(col);
            var price = UIKit.Text(Loc.Money(f.value), PhoneKit.Body, UIPalette.LeafInk, true);
            price.style.marginRight = 10f;
            row.Add(price);
            if (canSell)
            {
                var b = PhoneKit.IconButton(UIGlyphs.Tag, "", () =>
                {
                    if (FishMarket.List(f)) UIAudio.Pop();
                    Phone.MarkDirty();
                }, UIBtnKind.Primary, PhoneKit.Body);
                UIKit.Pad(b, 12f, 16f, 12f, 16f);
                row.Add(b);
            }
            return row;
        }

        VisualElement ListingRow(FishListing l)
        {
            var f = l.fish;
            var row = UIKit.Col();
            row.style.backgroundColor = Color.white;
            UIKit.Radius(row, 20f);
            UIKit.Pad(row, 10f, 16f, 12f, 14f);
            row.style.marginBottom = 8f;
            var top = UIKit.Row();
            top.Add(new UIGlyph(UIGlyphs.Fish, FishColor(f), 48f));
            var n = UIKit.Text(f.Name, PhoneKit.Body, UIPalette.Ink, true);
            n.style.marginLeft = 10f;
            n.style.flexGrow = 1;
            n.style.flexShrink = 1;
            top.Add(n);
            top.Add(UIKit.Text(Loc.Money(f.value), PhoneKit.Body, UIPalette.LeafInk, true));
            row.Add(top);
            var bottom = UIKit.Row();
            bottom.style.marginTop = 8f;
            var bar = new UIBar(12f, UIPalette.Sun);
            bar.style.flexGrow = 1;
            bar.SetValue(FishMarket.SaleProgress01(l));
            bottom.Add(bar);
            var clock = new UIGlyph(UIGlyphs.Clock, UIPalette.InkSoft, 24f);
            clock.style.marginLeft = 10f;
            bottom.Add(clock);
            var t = UIKit.Text("", PhoneKit.Small, UIPalette.InkSoft, true);
            t.style.width = 70f;
            t.style.unityTextAlign = TextAnchor.MiddleRight;
            bottom.Add(t);
            row.Add(bottom);
            var lr = new LiveRow { listing = l, time = t, bar = bar, shown = -1 };
            UpdateLive(ref lr);
            live.Add(lr);
            return row;
        }

        static VisualElement SoldRow(FishItem f)
        {
            var row = UIKit.Row();
            UIKit.Pad(row, 6f, 12f, 6f, 12f);
            row.Add(new UIGlyph(UIGlyphs.Check, UIPalette.LeafInk, 24f));
            var n = UIKit.Text(f.Name, PhoneKit.Small, UIPalette.InkSoft);
            n.style.marginLeft = 10f;
            n.style.flexGrow = 1;
            row.Add(n);
            row.Add(UIKit.Text("+" + Loc.Money(f.value), PhoneKit.Small, UIPalette.LeafInk, true));
            return row;
        }

        static void UpdateLive(ref LiveRow r)
        {
            int sec = Mathf.CeilToInt(FishMarket.SecondsLeft(r.listing));
            r.bar.SetValue(FishMarket.SaleProgress01(r.listing));
            if (sec == r.shown) return;
            r.shown = sec;
            r.time.text = sec <= 0 ? "…" : sec + " s";
        }

        public override void Tick(float dt)
        {
            tickTimer -= dt;
            if (tickTimer > 0f) return;
            tickTimer = 0.2f;
            for (int i = 0; i < live.Count; i++)
            {
                var r = live[i];
                UpdateLive(ref r);
                live[i] = r;
            }
        }
    }
}
