using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Chopper Shop: gear categories, item cards with stats, normal/express orders and current deliveries.
    public class UIAppShop : UIPhoneApp
    {
        static readonly GearSlot[] Slots = { GearSlot.Tank, GearSlot.Fins, GearSlot.Lamp, GearSlot.Camera, GearSlot.Boat, GearSlot.Submarine };
        int category;

        public override string Id => "shop";
        public override string Title => Loc.Tr("Chopper Shop", "Chopper Shop");
        public override Color Color => UIPalette.Sun;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Heli;

        public override int Badge
        {
            get
            {
                var s = UIKit.State;
                if (s == null) return 0;
                int n = 0;
                foreach (var d in s.deliveries) if (d.dropped) n++;
                return n;
            }
        }

        static string SlotName(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Tank: return Loc.Tr("Flaschen", "Tanks");
                case GearSlot.Fins: return Loc.Tr("Flossen", "Fins");
                case GearSlot.Lamp: return Loc.Tr("Lampen", "Lamps");
                case GearSlot.Camera: return Loc.Tr("Kameras", "Cameras");
                case GearSlot.Boat: return Loc.Tr("Boot", "Boat");
                default: return Loc.Tr("U-Boote", "Subs");
            }
        }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;

            // balance
            var bal = UIKit.Row();
            bal.Add(UIWidgets.StatTile(Loc.Money(s.money), Loc.Tr("Guthaben", "Balance"), UIPalette.Money));
            bal.Add(UIKit.HSpacer(8f));
            bal.Add(UIWidgets.StatTile(Loc.Number(s.followers), Loc.Tr("Follower", "Followers"), UIPalette.Lagoon));
            body.Add(bal);

            // deliveries
            if (s.deliveries.Count > 0)
            {
                body.Add(UIWidgets.SectionTitle(Loc.Tr("Lieferungen", "Deliveries"), UIPalette.Sun));
                foreach (var d in s.deliveries) body.Add(DeliveryRow(d, s));
            }

            // categories
            var tabs = UIKit.Row();
            tabs.style.flexWrap = Wrap.Wrap;
            tabs.style.marginTop = 14f;
            for (int i = 0; i < Slots.Length; i++)
            {
                int idx = i;
                bool sel = i == category;
                var b = UIKit.Btn(SlotName(Slots[i]), () => { category = idx; Phone.Refresh(false); }, UIBtnKind.Ghost, 13f);
                UIKit.Pad(b, 7f, 12f, 7f, 12f);
                UIKit.Radius(b, 999f);
                b.style.marginRight = 6f; b.style.marginBottom = 6f;
                if (sel) UIKit.SetColors(b, UIPalette.Sun, UIPalette.Deep);
                tabs.Add(b);
            }
            body.Add(tabs);

            var slot = Slots[Mathf.Clamp(category, 0, Slots.Length - 1)];
            var items = new List<GearItem>();
            foreach (var g in GearDatabase.All) if (g.slot == slot) items.Add(g);
            items.Sort((a, b) => a.tier.CompareTo(b.tier));
            var equipped = GearDatabase.Equipped(s, slot);
            foreach (var item in items) body.Add(ItemCard(item, s, equipped == item));

            var note = UIKit.Para(Loc.Tr("Pilot Gary liefert am nächsten Morgen zum Helipad. Express (+50 %) kommt in wenigen Minuten. Die Kiste holst du selbst ab.",
                                         "Pilot Gary delivers to the helipad the next morning. Express (+50 %) arrives in a few minutes. Pick up the crate yourself."), 12f, UIPalette.TextFaint);
            note.style.marginTop = 8f;
            body.Add(note);
        }

        static VisualElement DeliveryRow(Delivery d, GameState s)
        {
            var item = GearDatabase.Get(d.itemId);
            var row = UIKit.Row();
            row.style.backgroundColor = UIPalette.A(UIPalette.Sun, d.dropped ? 0.18f : 0.08f);
            UIKit.Radius(row, 12f);
            UIKit.Pad(row, 10f, 12f, 10f, 10f);
            row.style.marginBottom = 6f;
            var g = new UIGlyph(UIGlyphs.Heli, UIPalette.Sun, 26f);
            g.style.marginRight = 10f;
            row.Add(g);
            var col = UIKit.Col();
            col.style.flexShrink = 1;
            col.style.flexGrow = 1;
            col.Add(UIKit.Text(item != null ? item.Name : d.itemId, 14f, UIPalette.Text, true));
            string status;
            if (d.dropped) status = Loc.Tr("Liegt am Helipad – abholen!", "Waiting at the helipad – pick it up!");
            else if (Shop.IsDue(d)) status = Loc.Tr("Heli im Anflug …", "Chopper inbound …");
            else status = Loc.Tr("Ankunft: Tag ", "Arrives: day ") + d.arrivalDay + " · " + UIKit.TimeOfDay(d.arrivalTime);
            var st = UIKit.Text(status, 12.5f, d.dropped ? UIPalette.Sun : UIPalette.TextDim);
            st.style.whiteSpace = WhiteSpace.Normal;
            col.Add(st);
            row.Add(col);
            if (d.express) row.Add(UIKit.Pill("EXPRESS", UIPalette.Coral, Color.white, 10f));
            return row;
        }

        VisualElement ItemCard(GearItem item, GameState s, bool isEquipped)
        {
            bool owned = Shop.IsOwned(item), ordered = Shop.IsOrdered(item);
            var card = UIKit.Card(UIPalette.A(UIPalette.Raised, 0.95f), 16f, 14f);
            card.style.marginTop = 8f;
            if (isEquipped) UIKit.Border(card, 1.5f, UIPalette.A(UIPalette.Money, 0.6f));

            var head = UIKit.Row();
            var name = UIKit.Text(item.Name, 16f, item.comingSoon ? UIPalette.TextDim : UIPalette.Text, true);
            name.style.flexShrink = 1;
            name.style.whiteSpace = WhiteSpace.Normal;
            head.Add(name);
            head.Add(UIKit.Flex());
            if (isEquipped) head.Add(UIKit.Pill(Loc.Tr("AUSGERÜSTET", "EQUIPPED"), UIPalette.Money, UIPalette.Deep, 10f));
            else if (owned) head.Add(UIKit.Pill(Loc.Tr("IM BESITZ", "OWNED"), UIPalette.A(UIPalette.Money, 0.3f), UIPalette.Money, 10f));
            else if (ordered) head.Add(UIKit.Pill(Loc.Tr("UNTERWEGS", "ON ITS WAY"), UIPalette.Sun, UIPalette.Deep, 10f));
            else if (item.comingSoon) head.Add(UIKit.Pill(Loc.Tr("BALD", "SOON"), UIPalette.A(UIPalette.Violet, 0.35f), UIPalette.Violet, 10f));
            card.Add(head);

            var desc = UIKit.Para(item.Description, 12.5f, UIPalette.TextDim);
            desc.style.marginTop = 4f;
            card.Add(desc);

            var stats = UIKit.Row();
            stats.style.flexWrap = Wrap.Wrap;
            stats.style.marginTop = 8f;
            foreach (var st in Stats(item, s)) stats.Add(StatChip(st));
            card.Add(stats);

            if (owned) return card;

            var priceRow = UIKit.Row();
            priceRow.style.marginTop = 8f;
            priceRow.Add(UIKit.Text(Loc.Money(item.price), 18f, UIPalette.Sun, true));
            priceRow.Add(UIKit.Flex());
            if (item.followersNeeded > 0)
            {
                bool met = s.followers >= item.followersNeeded;
                var g = new UIGlyph(UIGlyphs.Person, met ? UIPalette.TextDim : UIPalette.Coral, 13f);
                g.style.marginRight = 4f;
                priceRow.Add(g);
                priceRow.Add(UIKit.Text(Loc.Tr("ab ", "from ") + Loc.Number(item.followersNeeded), 12.5f, met ? UIPalette.TextDim : UIPalette.Coral, true));
            }
            card.Add(priceRow);

            if (item.comingSoon)
            {
                var cs = UIKit.Text(Loc.Tr("Kommt in einem späteren Update", "Coming in a later update"), 12.5f, UIPalette.Violet, true);
                cs.style.marginTop = 8f;
                card.Add(cs);
                return card;
            }
            if (ordered) return card;

            bool canNormal = Shop.CanBuy(item, false, out string reason);
            bool canExpress = Shop.CanBuy(item, true, out string reasonExpress);
            var buttons = UIKit.Row();
            buttons.style.marginTop = 10f;
            var buy = UIKit.Btn(Loc.Tr("Bestellen", "Order"), () => { if (Shop.Buy(item, false)) Phone.Refresh(true); }, UIBtnKind.Primary, 14f);
            buy.style.flexGrow = 1;
            buy.style.flexBasis = 0;
            UIKit.Pad(buy, 9f, 8f, 9f, 8f);
            UIKit.SetEnabled(buy, canNormal);
            buttons.Add(buy);
            var express = UIKit.Btn(Loc.Tr("Express +50 %", "Express +50 %") + " · " + Loc.Money(Shop.Price(item, true)), () => { if (Shop.Buy(item, true)) Phone.Refresh(true); }, UIBtnKind.Secondary, 13f);
            express.style.flexGrow = 1.4f;
            express.style.flexBasis = 0;
            express.style.marginLeft = 8f;
            UIKit.Pad(express, 9f, 8f, 9f, 8f);
            UIKit.SetEnabled(express, canExpress);
            buttons.Add(express);
            card.Add(buttons);
            if (!canNormal || !canExpress)
            {
                string why = !canNormal ? reason : reasonExpress;
                if (!string.IsNullOrEmpty(why))
                {
                    var w = UIKit.Text(why, 12f, UIPalette.Coral, true);
                    w.style.marginTop = 6f;
                    card.Add(w);
                }
            }
            return card;
        }

        struct UIShopStat
        {
            public string label, value;
        }

        static List<UIShopStat> Stats(GearItem g, GameState s)
        {
            var list = new List<UIShopStat>();
            float airMul = DifficultySettings.For(s.difficulty).airMultiplier;
            switch (g.slot)
            {
                case GearSlot.Tank:
                    list.Add(new UIShopStat { label = Loc.Tr("Luft", "Air"), value = UIKit.ClockShort(Mathf.RoundToInt(g.airSeconds * airMul)) + " min" });
                    list.Add(new UIShopStat { label = Loc.Tr("Tiefe", "Depth"), value = Loc.Tr("bis ", "to ") + Loc.Meters(g.maxDepth) });
                    break;
                case GearSlot.Submarine:
                    list.Add(new UIShopStat { label = Loc.Tr("Tiefe", "Depth"), value = Loc.Tr("bis ", "to ") + Loc.Meters(g.maxDepth) });
                    break;
                case GearSlot.Fins:
                    list.Add(new UIShopStat { label = Loc.Tr("Tempo", "Speed"), value = UIKit.Mult(g.swimSpeed) });
                    break;
                case GearSlot.Lamp:
                    list.Add(new UIShopStat { label = Loc.Tr("Licht", "Light"), value = UIKit.Mult(g.lampIntensity) });
                    list.Add(new UIShopStat { label = Loc.Tr("Reichweite", "Range"), value = Loc.Meters(g.lampRange) });
                    break;
                case GearSlot.Camera:
                    list.Add(new UIShopStat { label = Loc.Tr("Qualität", "Quality"), value = UIKit.Mult(g.cameraQuality) });
                    list.Add(new UIShopStat { label = Loc.Tr("Band", "Tape"), value = UIKit.ClockShort(Mathf.RoundToInt(g.tapeSeconds)) + " min" });
                    list.Add(new UIShopStat { label = "Zoom", value = UIKit.Mult(g.maxZoom) });
                    break;
                case GearSlot.Boat:
                    list.Add(new UIShopStat { label = Loc.Tr("Bootstempo", "Boat speed"), value = UIKit.Mult(g.boatSpeed) });
                    break;
            }
            return list;
        }

        static VisualElement StatChip(UIShopStat st)
        {
            var chip = UIKit.Row();
            chip.style.backgroundColor = new Color(1f, 1f, 1f, 0.06f);
            UIKit.Radius(chip, 8f);
            UIKit.Pad(chip, 4f, 8f, 4f, 8f);
            chip.style.marginRight = 6f; chip.style.marginBottom = 4f;
            chip.Add(UIKit.Text(st.label, 11.5f, UIPalette.TextFaint));
            var v = UIKit.Text(st.value, 12f, UIPalette.Text, true);
            v.style.marginLeft = 5f;
            chip.Add(v);
            return chip;
        }
    }
}
