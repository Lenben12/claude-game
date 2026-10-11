using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Nachrichten (player feedback z: "clearer, less text"): every chat is an icon avatar + short lines (one fact per
    /// bubble, each with a small icon) – Onkel Gus, Wanda (Chopper Shop), FinTube, KRILL.tv, Barnacle deals, the
    /// page sponsors and a mysterious sender. Details live in the apps; the messages only point there.
    public class UIAppMessages : UIPhoneApp
    {
        struct Line
        {
            public UIGlyph.DrawFn icon;
            public string text;
        }

        class UIChat
        {
            public string id, name;
            public UIGlyph.DrawFn icon;
            public Color color;
            public readonly List<Line> lines = new List<Line>();
            public string stamp;
            public string Sub => lines.Count > 0 ? lines[lines.Count - 1].text : "";
            public void Add(UIGlyph.DrawFn icon, string text) => lines.Add(new Line { icon = icon, text = text });
        }

        static readonly string[,] Sponsors =
        {
            { "house_reef", "ReefSnacks" },
            { "kelp_forest", "Kelpy Cola" },
            { "wreck", "Rusty's Salvage" },
            { "blue_drop", "DeepBlue Energy" },
            { "deep_trench", "Abyssal Insurance" },
            { "fog_isle", "Foghorn Radio" },
        };

        readonly HashSet<string> seen = new HashSet<string>();
        string openChat;
        int lastBadge;

        public override string Id => "messages";
        public override string Title => Loc.Tr("Nachrichten", "Messages");
        public override Color Color => UIPalette.Rgb(0x4FC3F7);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Bubble;

        public override string HeaderTitle
        {
            get
            {
                if (openChat == null) return Title;
                foreach (var c in Chats()) if (c.id == openChat) return c.name;
                return Title;
            }
        }

        public override int Badge
        {
            get
            {
                if (UIKit.State == null) return 0;
                int n = 0;
                foreach (var c in Chats()) if (!seen.Contains(Key(c))) n++;
                return n;
            }
        }

        /// Number of unread chats (polled by the controller a few times per second to raise a push notification).
        public int UnreadCount() => Badge;

        public void ResetSession() { seen.Clear(); openChat = null; lastBadge = 0; }

        /// Returns the newest unread chat (for the push notification), null when none.
        public bool TryNewestUnread(out string name, out string line)
        {
            name = null; line = null;
            if (UIKit.State == null) return false;
            var chats = Chats();
            for (int i = chats.Count - 1; i >= 0; i--)
            {
                if (seen.Contains(Key(chats[i]))) continue;
                name = chats[i].name;
                line = chats[i].Sub;
                return true;
            }
            return false;
        }

        public int LastNotifiedBadge { get => lastBadge; set => lastBadge = value; }

        static string Key(UIChat c) => c.id + "#" + c.lines.Count;

        public override void OnOpen() { openChat = null; }

        public override bool Back()
        {
            if (openChat != null) { openChat = null; return true; }
            return false;
        }

        List<UIChat> Chats()
        {
            var list = new List<UIChat>();
            var s = UIKit.State;
            if (s == null) return list;

            var gus = new UIChat { id = "gus", name = Loc.Tr("Onkel Gus", "Uncle Gus"), icon = UIGlyphs.Radio, color = UIPalette.Rgb(0x3FA9B5), stamp = Loc.Tr("Tag 1", "Day 1") };
            gus.Add(UIGlyphs.House, Loc.Tr("Die Insel gehört jetzt dir!", "The island is yours now!"));
            gus.Add(UIGlyphs.Camera, Loc.Tr("Unter Wasser filmt die Kamera von selbst.", "Under water the camera films by itself."));
            gus.Add(UIGlyphs.Book, Loc.Tr("Fürs Sealog: ranzoomen + Foto.", "For the Sealog: zoom in + photo."));
            gus.Add(UIGlyphs.Warning, Loc.Tr("Clips sind erst an Land sicher.", "Clips are only safe on land."));
            if (Journal.DiscoveredCount >= 5)
                gus.Add(UIGlyphs.Star, Loc.Tr($"Schon {Journal.DiscoveredCount} Arten im Sealog!", $"{Journal.DiscoveredCount} species in the Sealog already!"));
            list.Add(gus);

            var wanda = new UIChat { id = "wanda", name = "Wanda", icon = UIGlyphs.Heli, color = UIPalette.Rgb(0xE0607E), stamp = "Chopper Shop" };
            wanda.Add(UIGlyphs.Heli, Loc.Tr("Bestell im Chopper Shop – ich fliege.", "Order in the Chopper Shop – I fly."));
            wanda.Add(UIGlyphs.Clock, Loc.Tr("Normal 2–3 Std. · Express 45 Min.", "Standard 2–3 h · express 45 min."));
            int open = s.deliveries.Count;
            if (open > 0) wanda.Add(UIGlyphs.Bag, Loc.Tr($"{open}× unterwegs zu dir.", $"{open}× on the way to you."));
            for (int i = 0; i < s.deliveries.Count; i++)
            {
                var d = s.deliveries[i];
                if (d != null && d.dropped && d.Mishap != DeliveryMishap.None)
                {
                    wanda.Add(UIGlyphs.Smoke, Loc.Tr("Ups. Paket suchen – folg dem Rauch!", "Oops. Find the parcel – follow the smoke!"));
                    break;
                }
            }
            list.Add(wanda);

            var ft = new UIChat { id = "fintube", name = "FinTube", icon = UIGlyphs.Play, color = UIPalette.Danger, stamp = Loc.Tr("Kanal", "Channel") };
            ft.Add(UIGlyphs.Coin, Loc.Tr("Geld ab dem 1. Video!", "Money from video 1!"));
            float boost = Progression.StarterBoost(s.followers);
            if (boost > Progression.StarterBoostShowAbove)
                ft.Add(UIGlyphs.Sparkle, Loc.Tr("Starthilfe ", "Starter boost ") + UIKit.Mult(Mathf.Round(boost * 10f) / 10f) + Loc.Tr(" bis 5.000 Follower", " until 5,000 followers"));
            ft.Add(UIGlyphs.Pencil, Loc.Tr("1 Video pro Tag. Titel selbst wählen!", "1 video per day. Pick your own title!"));
            list.Add(ft);

            if (s.followers >= LiveState.UnlockFollowers)
            {
                var kt = new UIChat { id = "krill", name = "KRILL.tv", icon = UIGlyphs.Broadcast, color = UIPalette.Rgb(0x8E5CC2), stamp = "1.000+" };
                kt.Add(UIGlyphs.Broadcast, Loc.Tr("Du kannst jetzt live gehen!", "You can go live now!"));
                kt.Add(UIGlyphs.BoatSide, Loc.Tr("Auf dem Boot: Handy → KRILL.tv", "On the boat: phone → KRILL.tv"));
                list.Add(kt);
            }

            if (s.followers >= Progression.LocalSponsorFollowers)
            {
                var bn = new UIChat { id = "barnacle", name = "Barnacle", icon = UIGlyphs.Handshake, color = UIPalette.Rgb(0x3FA9A0), stamp = "Deals" };
                bn.Add(UIGlyphs.Handshake, Loc.Tr("Sponsoren schicken dir Deals.", "Sponsors send you deals."));
                bn.Add(UIGlyphs.Tag, Loc.Tr("Max. 2 Deals – nie dasselbe Thema.", "Max 2 deals – never the same topic."));
                int offers = Deals.Offers.Count;
                if (offers > 0) bn.Add(UIGlyphs.Bell, Loc.Tr($"{offers} neue Angebote", $"{offers} new offers"));
                list.Add(bn);
            }

            for (int i = 0; i < s.sponsorSites.Count; i++)
            {
                string siteId = s.sponsorSites[i];
                string brand = "Sponsor";
                for (int k = 0; k < Sponsors.GetLength(0); k++) if (Sponsors[k, 0] == siteId) { brand = Sponsors[k, 1]; break; }
                var site = DiveSites.Get(siteId);
                long perDay = Progression.SponsorPerPage * (i + 1);
                var sp = new UIChat { id = "sponsor_" + siteId, name = brand, icon = UIGlyphs.Coin, color = UIPalette.Turquoise, stamp = Loc.Tr("Sponsor", "Sponsor") };
                sp.Add(UIGlyphs.Book, (site != null ? site.Name : siteId) + Loc.Tr(" komplett!", " complete!"));
                sp.Add(UIGlyphs.Coin, s.followers >= Progression.SponsorFollowers
                    ? "+" + Loc.Money(perDay) + Loc.Tr(" jeden Morgen", " every morning")
                    : Loc.Tr($"Ab {Loc.Number(Progression.SponsorFollowers)} Followern", $"From {Loc.Number(Progression.SponsorFollowers)} followers"));
                list.Add(sp);
            }

            if (s.followers >= 10000)
            {
                var murk = new UIChat { id = "murk", name = "???", icon = UIGlyphs.Eye, color = UIPalette.Rgb(0x3A2A55), stamp = "03:33" };
                murk.Add(UIGlyphs.Eye, Loc.Tr("Wir sehen deine Videos.", "We watch your videos."));
                murk.Add(UIGlyphs.Depth, Loc.Tr("Geh tiefer.", "Go deeper."));
                murk.Add(UIGlyphs.Question, Loc.Tr("Im Graben wartet etwas. – M.", "Something waits in the trench. – M."));
                list.Add(murk);
            }
            return list;
        }

        public override void Build(VisualElement body)
        {
            var chats = Chats();
            if (openChat != null)
            {
                foreach (var c in chats)
                    if (c.id == openChat) { seen.Add(Key(c)); BuildThread(body, c); return; }
                openChat = null;
            }
            foreach (var c in chats) body.Add(ChatRow(c));
        }

        VisualElement ChatRow(UIChat c)
        {
            bool unread = !seen.Contains(Key(c));
            var row = UIKit.Row();
            UIKit.Pad(row, 14f, 16f, 14f, 14f);
            UIKit.Radius(row, 22f);
            row.style.marginBottom = 8f;
            UIKit.Clickable(row, () => { openChat = c.id; Phone.Refresh(false); }, Color.white, UIPalette.CardAlt);
            row.Add(Avatar(c, 68f));
            var col = UIKit.Col();
            col.style.marginLeft = 14f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            var top = UIKit.Row();
            top.Add(UIKit.Text(c.name, PhoneKit.Body, UIPalette.Ink, true));
            top.Add(UIKit.Flex());
            top.Add(UIKit.Text(c.stamp, PhoneKit.Tiny, UIPalette.Silhouette));
            col.Add(top);
            var sub = UIKit.Text(c.Sub, PhoneKit.Small, unread ? UIPalette.Ink : UIPalette.InkSoft, unread);
            sub.style.overflow = Overflow.Hidden;
            sub.style.textOverflow = TextOverflow.Ellipsis;
            sub.style.whiteSpace = WhiteSpace.NoWrap;
            sub.style.marginTop = 2f;
            col.Add(sub);
            row.Add(col);
            if (unread)
            {
                var dot = UIKit.Dot(UIPalette.Badge, 18f);
                dot.style.marginLeft = 10f;
                row.Add(dot);
            }
            UIKit.IgnorePicking(row);
            row.pickingMode = PickingMode.Position;
            return row;
        }

        static VisualElement Avatar(UIChat c, float size)
        {
            var a = UIKit.Box();
            UIKit.Size(a, size, size);
            UIKit.Radius(a, size * 0.5f);
            a.style.backgroundColor = c.color;
            UIKit.Center(a);
            a.Add(new UIGlyph(c.icon, UIPalette.OnColor(c.color) == UIPalette.Ink ? UIPalette.Ink : Color.white, size * 0.56f));
            return a;
        }

        static void BuildThread(VisualElement body, UIChat c)
        {
            var head = UIKit.Col();
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 18f;
            head.Add(Avatar(c, 104f));
            var n = UIKit.Text(c.name, PhoneKit.Big, UIPalette.Ink, true);
            n.style.marginTop = 8f;
            head.Add(n);
            body.Add(head);
            foreach (var line in c.lines)
            {
                var bubble = UIKit.Row();
                bubble.style.backgroundColor = Color.white;
                bubble.style.borderTopLeftRadius = 8f; bubble.style.borderTopRightRadius = 26f;
                bubble.style.borderBottomLeftRadius = 26f; bubble.style.borderBottomRightRadius = 26f;
                UIKit.Pad(bubble, 14f, 20f, 14f, 14f);
                bubble.style.marginBottom = 10f;
                bubble.style.maxWidth = Length.Percent(92f);
                bubble.style.alignSelf = Align.FlexStart;
                UIKit.Lift(bubble, 0.6f);
                bubble.Add(new UIGlyph(line.icon ?? UIGlyphs.Bubble, UIWidgets.Ink(c.color), 30f));
                var t = PhoneKit.T(line.text, PhoneKit.Body, UIPalette.Ink, true);
                t.style.marginLeft = 10f;
                t.style.flexShrink = 1;
                bubble.Add(t);
                body.Add(bubble);
            }
        }
    }
}
