using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// „Dr. Fin“ research app (B.4, K.3; icon microscope #3FA9B5): her open research quests (max 3) with progress and the
    /// reward, the rod chain marker, done count. Data: GameState.research (core) + ResearchDatabase (npc).
    public class UIAppFin : UIPhoneApp
    {
        const long UnlockFollowers = 300;

        public override string Id => "fin";
        public override string Title => "Dr. Fin";
        public override Color Color => UIPalette.Rgb(0x3FA9B5);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Microscope;

        public override bool ShowLock
        {
            get
            {
                var s = UIKit.State;
                return s == null || s.followers < UnlockFollowers;
            }
        }

        public override int Badge
        {
            get
            {
                var s = UIKit.State;
                if (s == null || s.followers < UnlockFollowers) return 0;
                int n = 0;
                for (int i = 0; i < s.research.Count; i++) if (s.research[i] != null && !s.research[i].done) n++;
                return n;
            }
        }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;

            var head = PhoneKit.Card(16f);
            var hr = UIKit.Row();
            hr.Add(UIWidgets.IconTile(UIGlyphs.Microscope, Color, 72f));
            var hc = UIKit.Col();
            hc.style.marginLeft = 14f;
            hc.style.flexShrink = 1;
            hc.Add(UIKit.Text("Dr. Marina Fin", PhoneKit.Big, UIPalette.Ink, true));
            hc.Add(PhoneKit.T(Loc.Tr("Ich bezahle für Daten. Fotos, Fische, Messungen.", "I pay for data. Photos, fish, measurements."), PhoneKit.Tiny, UIPalette.InkSoft));
            hr.Add(hc);
            head.Add(hr);
            body.Add(head);

            if (s.followers < UnlockFollowers)
            {
                body.Add(UIWidgets.Notice(Loc.Tr("Dr. Fin meldet sich ab 300 Followern.", "Dr. Fin gets in touch from 300 followers."), Color, UIGlyphs.Lock, PhoneKit.Small));
                var bar = PhoneKit.Bar(s.followers / (float)UnlockFollowers, Color, 16f);
                bar.style.marginTop = 6f;
                body.Add(bar);
                return;
            }

            body.Add(PhoneKit.Section(Loc.Tr("Forschungsaufträge", "Research jobs"), UIGlyphs.Microscope));
            int open = 0, done = 0;
            for (int i = 0; i < s.research.Count; i++)
            {
                var r = s.research[i];
                if (r == null) continue;
                if (r.done) { done++; continue; }
                body.Add(JobCard(r));
                open++;
            }
            if (open == 0)
                body.Add(PhoneKit.Empty(UIGlyphs.Microscope, Loc.Tr("Neue Aufträge bringt sie bei ihrem Besuch.", "She brings new jobs when she visits.")));

            var info = UIKit.Row();
            info.style.marginTop = 10f;
            info.Add(new UIGlyph(UIGlyphs.BoatSide, UIPalette.InkSoft, 26f));
            info.Add(UIKit.Text(" " + Loc.Tr("Besuch jeden 3. Tag am Steg", "Visits every 3rd day at the dock"), PhoneKit.Tiny, UIPalette.InkSoft, true));
            body.Add(info);
            if (done > 0)
            {
                var d = UIKit.Row();
                d.style.marginTop = 6f;
                d.Add(new UIGlyph(UIGlyphs.Check, UIPalette.LeafInk, 26f));
                d.Add(UIKit.Text(" " + done + Loc.Tr(" erledigt", " done"), PhoneKit.Small, UIPalette.LeafInk, true));
                body.Add(d);
            }
        }

        VisualElement JobCard(ResearchState r)
        {
            var def = ResearchDatabase.Get(r.id);
            var card = PhoneKit.Card(14f);
            card.style.borderLeftWidth = 8f;
            card.style.borderLeftColor = Color;
            var row = UIKit.Row();
            UIGlyph.DrawFn icon = UIGlyphs.Camera;
            if (def != null)
                switch (def.kind)
                {
                    case ResearchKind.Film: icon = UIGlyphs.Rec; break;
                    case ResearchKind.Catch: icon = UIGlyphs.Rod; break;
                    case ResearchKind.Samples: icon = UIGlyphs.Bottle; break;
                    case ResearchKind.Census: icon = UIGlyphs.Eye; break;
                    case ResearchKind.Listen: icon = UIGlyphs.Note; break;
                }
            row.Add(UIWidgets.IconTile(icon, Color, 52f));
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(PhoneKit.T(def != null ? def.Title : r.id, PhoneKit.Small, UIPalette.Ink, true));
            if (def != null && def.rod > 0)
                col.Add(UIKit.Text(Loc.Tr("Angel-Kette ", "Rod chain ") + def.rod + "/4", PhoneKit.Tiny, UIPalette.Sky, true));
            row.Add(col);
            if (def != null && def.money > 0) row.Add(UIKit.Text("+" + Loc.Money(def.money), PhoneKit.Small, UIPalette.LeafInk, true));
            else if (def != null && def.rod == 4) row.Add(new UIGlyph(UIGlyphs.Gift, UIPalette.SunInk, 30f));
            card.Add(row);
            int count = def != null ? Mathf.Max(1, def.count) : 1;
            if (count > 1)
            {
                var pr = UIKit.Row();
                pr.style.marginTop = 8f;
                var bar = PhoneKit.Bar(Mathf.Clamp01(r.progress / (float)count), Color, 12f);
                bar.style.flexGrow = 1;
                pr.Add(bar);
                var l = UIKit.Text(Mathf.Clamp(r.progress, 0, count) + "/" + count, PhoneKit.Tiny, UIPalette.Ink, true);
                l.style.marginLeft = 8f;
                pr.Add(l);
                card.Add(pr);
            }
            return card;
        }
    }
}
