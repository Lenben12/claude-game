using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Nachrichten: story chats derived from the game state (Uncle Gus, Pilot Gary, FinTube, KRILL.tv, sponsors, Murk).
    public class UIAppMessages : UIPhoneApp
    {
        class UIChat
        {
            public string id, name, initials, sub;
            public Color color;
            public List<string> lines = new List<string>();
            public string stamp;
        }

        static readonly string[,] Sponsors =
        {
            { "house_reef", "ReefSnacks", "Algen-Chips mit Meersalz", "Seaweed chips with sea salt" },
            { "kelp_forest", "Kelpy Cola", "Die grünste Limo der Welt", "The greenest soda in the world" },
            { "wreck", "Rusty's Salvage", "Bergungsdienst seit 1962", "Salvage since 1962" },
            { "blue_drop", "DeepBlue Energy", "Energy-Drink für Tiefgänger", "Energy drink for deep thinkers" },
            { "deep_trench", "Abyssal Insurance", "Versichert auch Seeungeheuer", "We even insure sea monsters" },
        };

        readonly HashSet<string> seen = new HashSet<string>();
        string openChat;

        public override string Id => "messages";
        public override string Title => Loc.Tr("Nachrichten", "Messages");
        public override Color Color => UIPalette.Money;
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

        public void ResetSession() { seen.Clear(); openChat = null; }

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

            var gus = new UIChat { id = "gus", name = Loc.Tr("Onkel Gus", "Uncle Gus"), initials = "G", color = UIPalette.Sun, stamp = Loc.Tr("Tag 1", "Day 1") };
            gus.lines.Add(Loc.Tr("Hallo! Die Insel gehört jetzt dir – Hütte, Steg, die alte Rostlaube und alles, was drumherum im Wasser schwimmt.",
                                 "Hi! The island is yours now – the hut, the dock, the old Rust Bucket and everything swimming around it."));
            gus.lines.Add(Loc.Tr("Ich bin zu alt zum Tauchen. Aber du hast ja dein Handy im Gefrierbeutel … Film einfach alles, was unter Wasser lebt!",
                                 "I'm too old to dive. But you've got your phone in a freezer bag … just film everything that lives underwater!"));
            gus.lines.Add(Loc.Tr("Je größer das Viech, desto mehr Leute gucken zu. Und denk dran: Clips sind erst sicher, wenn du wieder aus dem Wasser bist.",
                                 "The bigger the critter, the more people watch. And remember: clips are only safe once you're out of the water."));
            gus.lines.Add(Loc.Tr("Fürs erste Geld: Angel am Steg ein paar Fische. Damit bestellst du bei Gary eine Pressluftflasche.",
                                 "For your first money: catch a few fish at the dock. Then order an air tank from Gary."));
            if (Journal.DiscoveredCount >= 5)
                gus.lines.Add(Loc.Tr("Ich hab dein Sealog gesehen – schon " + Journal.DiscoveredCount + " Arten! Weiter so, Kleines.",
                                     "I saw your Sealog – already " + Journal.DiscoveredCount + " species! Keep it up, kiddo."));
            gus.sub = gus.lines[gus.lines.Count - 1];
            list.Add(gus);

            var gary = new UIChat { id = "gary", name = Loc.Tr("Pilot Gary", "Pilot Gary"), initials = "PG", color = UIPalette.Coral, stamp = Loc.Tr("Tag 1", "Day 1") };
            gary.lines.Add(Loc.Tr("Gary hier, Chopper-Lieferdienst! Bestell im Chopper Shop, ich bring's am nächsten Morgen zum Helipad.",
                                  "Gary here, chopper delivery! Order in the Chopper Shop and I'll drop it at the helipad the next morning."));
            gary.lines.Add(Loc.Tr("Eilig? Express kostet 50 % extra, dann bin ich in ein paar Minuten da. Die Kiste musst du selbst abholen.",
                                  "In a hurry? Express costs 50 % extra and I'll be there in a few minutes. You pick up the crate yourself."));
            int open = s.deliveries.Count;
            if (open > 0)
                gary.lines.Add(Loc.Tr(open == 1 ? "Ich hab 1 Lieferung für dich auf dem Plan." : $"Ich hab {open} Lieferungen für dich auf dem Plan.",
                                      open == 1 ? "I've got 1 delivery for you on my schedule." : $"I've got {open} deliveries for you on my schedule."));
            gary.sub = gary.lines[gary.lines.Count - 1];
            list.Add(gary);

            if (s.followers >= Progression.MonetizationFollowers)
            {
                var ft = new UIChat { id = "fintube", name = "FinTube Team", initials = "FT", color = UIPalette.Live, stamp = "100+" };
                ft.lines.Add(Loc.Tr($"Glückwunsch zu {Loc.Number(Progression.MonetizationFollowers)} Followern! Ab sofort verdienst du Geld mit jedem Aufruf: {Progression.MoneyPerThousandViews:0} $ pro 1.000 Aufrufe.",
                                    $"Congrats on {Loc.Number(Progression.MonetizationFollowers)} followers! From now on you earn money with every view: ${Progression.MoneyPerThousandViews:0} per 1,000 views."));
                ft.lines.Add(Loc.Tr("Tipp: Neue Arten und große Exemplare bringen die meisten Aufrufe. Lade dieselbe Art nicht jeden Tag hoch.",
                                    "Tip: new species and big specimens bring the most views. Don't upload the same species every day."));
                ft.sub = ft.lines[ft.lines.Count - 1];
                list.Add(ft);
            }

            if (s.followers >= Progression.LivestreamFollowers)
            {
                var kt = new UIChat { id = "krill", name = "KRILL.tv", initials = "K", color = UIPalette.Violet, stamp = "1.000+" };
                kt.lines.Add(Loc.Tr("Du bist jetzt Partner auf KRILL.tv! Livestreams aus dem Ozean kommen im nächsten Update.",
                                    "You're now a KRILL.tv partner! Livestreams from the ocean are coming in the next update."));
                kt.sub = kt.lines[0];
                list.Add(kt);
            }

            for (int i = 0; i < s.sponsorSites.Count; i++)
            {
                string siteId = s.sponsorSites[i];
                int row = -1;
                for (int k = 0; k < Sponsors.GetLength(0); k++) if (Sponsors[k, 0] == siteId) { row = k; break; }
                var site = DiveSites.Get(siteId);
                string brand = row >= 0 ? Sponsors[row, 1] : "Sponsor";
                long perDay = 150 * (i + 1);
                var sp = new UIChat { id = "sponsor_" + siteId, name = brand, initials = brand.Substring(0, 1), color = UIPalette.Lagoon, stamp = Loc.Tr("Sponsor", "Sponsor") };
                if (row >= 0) sp.lines.Add(Loc.Tr($"Hallo von {brand} – {Sponsors[row, 2]}!", $"Hello from {brand} – {Sponsors[row, 3]}!"));
                sp.lines.Add(Loc.Tr($"Wir haben gesehen, dass du die komplette Tagebuch-Seite „{(site != null ? site.Name : siteId)}“ gefilmt hast. Wir möchten dich sponsern: {Loc.Money(perDay)} pro Tag!",
                                    $"We saw you filmed the complete “{(site != null ? site.Name : siteId)}” journal page. We'd like to sponsor you: {Loc.Money(perDay)} per day!"));
                sp.lines.Add(s.followers >= Progression.SponsorFollowers
                    ? Loc.Tr("Deal aktiv – die Zahlung kommt jeden Morgen.", "Deal active – payment arrives every morning.")
                    : Loc.Tr($"Der Deal startet, sobald du {Loc.Number(Progression.SponsorFollowers)} Follower hast.", $"The deal starts as soon as you have {Loc.Number(Progression.SponsorFollowers)} followers."));
                sp.sub = sp.lines[sp.lines.Count - 1];
                list.Add(sp);
            }

            if (s.followers >= 10000)
            {
                var murk = new UIChat { id = "murk", name = "???", initials = "M", color = UIPalette.Rgb(0x3A2A55), stamp = "03:33" };
                murk.lines.Add(Loc.Tr("Wir sehen deine Videos.", "We watch your videos."));
                murk.lines.Add(Loc.Tr("Du filmst nur die Oberfläche. Tiefer. Geh tiefer.", "You only film the surface. Deeper. Go deeper."));
                murk.lines.Add(Loc.Tr("Im Graben wartet etwas, das noch nie jemand gefilmt hat. – M.", "In the trench something waits that nobody has ever filmed. – M."));
                murk.sub = murk.lines[murk.lines.Count - 1];
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
            UIKit.Pad(row, 10f, 10f, 10f, 10f);
            UIKit.Radius(row, 14f);
            row.style.marginBottom = 4f;
            UIKit.Clickable(row, () => { openChat = c.id; Phone.Refresh(false); }, new Color(1f, 1f, 1f, unread ? 0.07f : 0.03f), new Color(1f, 1f, 1f, 0.12f));
            row.Add(Avatar(c, 46f));
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            var top = UIKit.Row();
            top.Add(UIKit.Text(c.name, 15f, UIPalette.Text, true));
            top.Add(UIKit.Flex());
            top.Add(UIKit.Text(c.stamp, 11f, UIPalette.TextFaint));
            col.Add(top);
            var sub = UIKit.Text(c.sub, 12.5f, unread ? UIPalette.Text : UIPalette.TextDim);
            sub.style.overflow = Overflow.Hidden;
            sub.style.textOverflow = TextOverflow.Ellipsis;
            sub.style.whiteSpace = WhiteSpace.NoWrap;
            sub.style.marginTop = 2f;
            col.Add(sub);
            row.Add(col);
            if (unread)
            {
                var dot = UIKit.Dot(UIPalette.Sky, 10f);
                dot.style.marginLeft = 8f;
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
            a.style.alignItems = Align.Center;
            a.style.justifyContent = Justify.Center;
            a.Add(UIKit.Text(c.initials, size * 0.36f, UIPalette.OnColor(c.color), true));
            return a;
        }

        static void BuildThread(VisualElement body, UIChat c)
        {
            var head = UIKit.Col();
            head.style.alignItems = Align.Center;
            head.style.marginBottom = 14f;
            head.Add(Avatar(c, 64f));
            var n = UIKit.Text(c.name, 17f, UIPalette.Text, true);
            n.style.marginTop = 6f;
            head.Add(n);
            head.Add(UIKit.Text(c.stamp, 11f, UIPalette.TextFaint));
            body.Add(head);
            foreach (var line in c.lines)
            {
                var bubble = UIKit.Para(line, 14f, UIPalette.Text);
                bubble.style.backgroundColor = UIPalette.Raised2;
                bubble.style.borderTopLeftRadius = 6f; bubble.style.borderTopRightRadius = 16f;
                bubble.style.borderBottomLeftRadius = 16f; bubble.style.borderBottomRightRadius = 16f;
                UIKit.Pad(bubble, 10f, 14f, 10f, 14f);
                bubble.style.marginBottom = 6f;
                bubble.style.maxWidth = Length.Percent(88f);
                bubble.style.alignSelf = Align.FlexStart;
                body.Add(bubble);
            }
        }
    }
}
