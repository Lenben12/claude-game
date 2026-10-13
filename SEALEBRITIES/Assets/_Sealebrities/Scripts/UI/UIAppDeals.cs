using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// „Barnacle“ sponsor app (K.7, player message m): offers with topic icons, max 2 ACTIVE deals („Aktiv 1/2“), an
    /// overlapping topic blocks an offer with a clear hint („Überschneidung mit … Erst kündigen.“) even if it pays more,
    /// cancel button with a confirm dialog (no money, the sponsor sulks 3 days), task progress + days left.
    /// Logic lives in core Deals / the livestream module's SponsorService – the app only calls Accept / Cancel.
    public class UIAppDeals : UIPhoneApp
    {
        public override string Id => "deals";
        public override string Title => "Barnacle";
        public override Color Color => UIPalette.Rgb(0x3FA9A0);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Handshake;
        public override string HeaderTitle => "Deals";

        int seenOffers = -1;

        public override bool ShowLock
        {
            get
            {
                var s = UIKit.State;
                return s == null || s.followers < Progression.LocalSponsorFollowers;
            }
        }

        public override int Badge
        {
            get
            {
                int n = Deals.Offers.Count;
                return n > 0 && n != seenOffers ? n : 0;
            }
        }

        public override void OnOpen() { seenOffers = Deals.Offers.Count; }

        /// Topic glyph of a deal tag.
        public static UIGlyph.DrawFn TagGlyph(string tag)
        {
            switch (tag)
            {
                case "shark": return UIGlyphs.SharkFin;
                case "turtle": return UIGlyphs.Turtle;
                case "ray": return UIGlyphs.Fish;
                case "octopus": return UIGlyphs.Bubbles;
                case "school": return UIGlyphs.Fish;
                case "wreck": return UIGlyphs.BoatSide;
                case "night": return UIGlyphs.Moon;
                case "depth": return UIGlyphs.Depth;
                case "fishing": return UIGlyphs.Rod;
                case "livestream": return UIGlyphs.Broadcast;
                case "island": return UIGlyphs.Hut;
                case "coral": return UIGlyphs.Sparkle;
                case "whale": return UIGlyphs.Whale;
                default: return UIGlyphs.Tag;
            }
        }

        public static Color TagColor(string tag)
        {
            switch (tag)
            {
                case "shark": return UIPalette.Rgb(0x4F7FAE);
                case "turtle": return UIPalette.Rgb(0x6E9445);
                case "ray": return UIPalette.Rgb(0x2F6FD6);
                case "octopus": return UIPalette.Rgb(0xE0607E);
                case "school": return UIPalette.Rgb(0x3FB8C4);
                case "wreck": return UIPalette.Rgb(0x8E6C50);
                case "night": return UIPalette.Rgb(0x3A3F5C);
                case "depth": return UIPalette.Rgb(0x284F80);
                case "fishing": return UIPalette.Rgb(0x4FA3E0);
                case "livestream": return UIPalette.Rgb(0x8E5CC2);
                case "island": return UIPalette.Rgb(0xEE7F2D);
                case "coral": return UIPalette.Rgb(0xF29BBE);
                default: return UIPalette.Rgb(0x2B3440);
            }
        }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;
            seenOffers = Deals.Offers.Count;

            if (s.followers < Progression.LocalSponsorFollowers)
            {
                var hero = UIKit.Col();
                hero.style.alignItems = Align.Center;
                hero.style.marginTop = 20f;
                hero.Add(UIWidgets.IconTile(UIGlyphs.Handshake, Color, 150f));
                var t = PhoneKit.T(Loc.Tr("Sponsoren ab 250 Followern", "Sponsors from 250 followers"), PhoneKit.Big, UIPalette.Ink, true);
                t.style.marginTop = 16f;
                t.style.unityTextAlign = TextAnchor.MiddleCenter;
                hero.Add(t);
                body.Add(hero);
                var bar = PhoneKit.Bar(s.followers / (float)Progression.LocalSponsorFollowers, Color, 18f);
                bar.style.marginTop = 18f;
                body.Add(bar);
                return;
            }

            // slots „Aktiv 1/2“
            var active = Deals.Active;
            var slots = UIKit.Row();
            slots.style.marginBottom = 4f;
            slots.Add(new UIGlyph(UIGlyphs.Handshake, UIWidgets.Ink(Color), 34f));
            slots.Add(UIKit.Text(" " + Loc.Tr("Aktiv ", "Active ") + active.Count + "/" + Deals.MaxActive, PhoneKit.Big, UIPalette.Ink, true));
            slots.Add(UIKit.Flex());
            for (int i = 0; i < Deals.MaxActive; i++)
            {
                var dot = UIKit.Dot(i < active.Count ? Color : UIPalette.A(UIPalette.Ink, 0.15f), 22f);
                dot.style.marginLeft = 6f;
                slots.Add(dot);
            }
            body.Add(slots);

            if (active.Count == 0)
                body.Add(UIWidgets.Notice(Loc.Tr("Kein Deal aktiv – nimm ein Angebot an.", "No active deal – accept an offer."), Color, UIGlyphs.Handshake, PhoneKit.Small));
            for (int i = 0; i < active.Count; i++) if (active[i] != null) body.Add(ActiveCard(active[i], s));

            body.Add(PhoneKit.Section(Loc.Tr("Angebote", "Offers"), UIGlyphs.Bell));
            var offers = Deals.Offers;
            if (offers.Count == 0)
                body.Add(PhoneKit.Empty(UIGlyphs.Handshake, Loc.Tr("Gerade keine Angebote.", "No offers right now.")));
            for (int i = 0; i < offers.Count; i++) if (offers[i] != null) body.Add(OfferCard(offers[i], s));
        }

        VisualElement Head(DealState d, string sub)
        {
            var row = UIKit.Row();
            var tile = UIWidgets.IconTile(UIGlyphs.Handshake, Color, 56f);
            row.Add(tile);
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text(Deals.NameOf(d.sponsorId), PhoneKit.Small, UIWidgets.Ink(Color), true));
            col.Add(PhoneKit.T(Deals.TitleOf(d), PhoneKit.Body, UIPalette.Ink, true));
            row.Add(col);
            if (!string.IsNullOrEmpty(sub)) row.Add(UIKit.Pill(sub, UIPalette.CardAlt, UIPalette.Ink, PhoneKit.Tiny));
            return row;
        }

        static VisualElement Tags(DealState d, string highlight)
        {
            var row = UIKit.Row();
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginTop = 10f;
            if (d.tags == null) return row;
            for (int i = 0; i < d.tags.Length; i++)
            {
                string t = d.tags[i];
                bool hot = t == highlight;
                var chip = UIKit.Row();
                UIKit.Radius(chip, 999f);
                UIKit.Pad(chip, 6f, 12f, 6f, 8f);
                chip.style.marginRight = 8f;
                chip.style.marginBottom = 4f;
                Color c = TagColor(t);
                chip.style.backgroundColor = hot ? UIPalette.A(UIPalette.Coral, 0.25f) : UIPalette.A(c, 0.16f);
                if (hot) UIKit.Border(chip, 2f, UIPalette.CoralInk);
                chip.Add(new UIGlyph(TagGlyph(t), hot ? UIPalette.CoralInk : UIWidgets.Ink(c), 24f));
                chip.Add(UIKit.Text(" " + Deals.TagName(t), PhoneKit.Tiny, hot ? UIPalette.CoralInk : UIWidgets.Ink(c), true));
                row.Add(chip);
            }
            return row;
        }

        VisualElement ActiveCard(DealState d, GameState s)
        {
            int left = Mathf.Max(0, d.deadlineDay - s.day);
            var card = PhoneKit.Card(16f);
            card.style.borderLeftWidth = 8f;
            card.style.borderLeftColor = Color;
            card.Add(Head(d, left <= 1 ? Loc.Tr("Letzter Tag", "Last day") : Loc.Tr($"noch {left} Tage", $"{left} days left")));
            card.Add(Tags(d, null));
            if (d.target > 0f)
            {
                float p = Mathf.Clamp01(d.progress / d.target);
                var pr = UIKit.Row();
                pr.style.marginTop = 10f;
                var bar = PhoneKit.Bar(p, Color, 14f);
                bar.style.flexGrow = 1;
                pr.Add(bar);
                var l = UIKit.Text(Mathf.FloorToInt(Mathf.Max(0f, d.progress)) + "/" + Mathf.CeilToInt(d.target), PhoneKit.Small, UIPalette.Ink, true);
                l.style.marginLeft = 10f;
                pr.Add(l);
                card.Add(pr);
            }
            var bottom = UIKit.Row();
            bottom.style.marginTop = 12f;
            bottom.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 30f));
            bottom.Add(UIKit.Text(" " + Loc.Money(d.reward), PhoneKit.Big, UIPalette.LeafInk, true));
            bottom.Add(UIKit.Flex());
            // „Verfolgen“: pin this deal in the quest line (L.10)
            bool tracked = s.trackedQuest == d.id;
            string did = d.id;
            var pin = UIWidgets.RoundIconButton(UIGlyphs.Pin, () => { QuestTracker.Track(tracked ? null : did); Phone.Refresh(true); }, 52f,
                                               tracked ? UIWidgets.Ink(Color) : UIPalette.Silhouette);
            if (tracked) pin.style.backgroundColor = UIPalette.A(Color, 0.22f);
            pin.style.marginRight = 8f;
            bottom.Add(pin);
            var cancel = PhoneKit.IconButton(UIGlyphs.Cross, Loc.Tr("Kündigen", "Cancel deal"), () =>
            {
                string sponsor = Deals.NameOf(d.sponsorId);
                string id = d.id;
                UIDialog.Show(UIGlyphs.Handshake, Color, Loc.Tr("Deal kündigen?", "Cancel the deal?"),
                    Loc.Tr($"Kein Geld, und {sponsor} ist 3 Tage beleidigt.", $"No money, and {sponsor} sulks for 3 days."),
                    Loc.Tr("Behalten", "Keep it"), Loc.Tr("Kündigen", "Cancel deal"),
                    () => { Deals.Cancel(id); Phone.Refresh(true); }, null, true);
            }, UIBtnKind.Danger, PhoneKit.Small);
            UIKit.Pad(cancel, 10f, 14f, 10f, 14f);
            bottom.Add(cancel);
            card.Add(bottom);
            return card;
        }

        VisualElement OfferCard(DealState d, GameState s)
        {
            bool ok = Deals.CanAccept(d, out string reason, out DealState conflict, out string tag);
            int expires = Mathf.Max(0, d.expiresDay - s.day);
            var card = PhoneKit.Card(16f);
            card.Add(Head(d, Loc.Tr($"{Mathf.Max(1, d.days)} Tage", $"{Mathf.Max(1, d.days)} days")));
            card.Add(Tags(d, conflict != null ? tag : null));
            var bottom = UIKit.Row();
            bottom.style.marginTop = 12f;
            bottom.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 30f));
            bottom.Add(UIKit.Text(" " + Loc.Money(d.reward), PhoneKit.Big, UIPalette.LeafInk, true));
            bottom.Add(UIKit.Flex());
            var accept = PhoneKit.IconButton(UIGlyphs.Check, Loc.Tr("Annehmen", "Accept"), () =>
            {
                if (Deals.Accept(d.id)) UIAudio.Pop(); else UIAudio.Nope();
                Phone.Refresh(true);
            }, UIBtnKind.Accent, PhoneKit.Small);
            UIKit.Pad(accept, 10f, 16f, 10f, 14f);
            UIKit.SetEnabled(accept, ok);
            bottom.Add(accept);
            card.Add(bottom);
            if (!ok && !string.IsNullOrEmpty(reason))
            {
                var n = UIWidgets.Notice(reason, UIPalette.Coral, conflict != null ? TagGlyph(tag) : UIGlyphs.Lock, PhoneKit.Tiny);
                n.style.marginTop = 10f;
                n.style.marginBottom = 0f;
                card.Add(n);
            }
            var exp = UIKit.Row();
            exp.style.marginTop = 8f;
            exp.Add(new UIGlyph(UIGlyphs.Clock, UIPalette.InkSoft, 20f));
            exp.Add(UIKit.Text(" " + (expires <= 1 ? Loc.Tr("Läuft heute ab", "Expires today") : Loc.Tr($"Angebot gilt {expires} Tage", $"Offer valid {expires} days")), PhoneKit.Tiny, UIPalette.InkSoft, true));
            card.Add(exp);
            return card;
        }
    }
}
