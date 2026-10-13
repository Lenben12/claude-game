using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// „Barnacle“ – the sponsor app (K.7, header „Deals“): active deals in 2 slots (progress, days left, reward,
    /// „Verfolgen“, „Kündigen“ with the sulk warning) and the open offers with „Annehmen“ – disabled with the overlap
    /// reason when a topic is taken. Added to the phone by LivestreamModule while the UI module has no UIAppDeals.
    public sealed class BarnacleApp : UIPhoneApp
    {
        public static readonly Color Teal = UIPalette.Rgb(0x3FA9A0);

        public override string Id => "deals";
        public override string Title => "Barnacle";
        public override string HeaderTitle => Loc.Tr("Deals", "Deals");
        public override Color Color => Teal;
        public override UIGlyph.DrawFn Icon => HandshakeGlyph.Draw;
        public override bool ShowLock => UIKit.State == null || UIKit.State.followers < Progression.LocalSponsorFollowers;
        public override int Badge => UIKit.InGame ? Deals.Offers.Count : 0;

        /// Deal whose „Kündigen“ asks for confirmation right now.
        string confirmCancel;
        int builtKey;
        float pollTimer;

        public override void OnOpen() { confirmCancel = null; pollTimer = 0f; }

        /// Deals change without a phone refresh (offers in the morning, progress from the host) – poll a cheap key.
        public override void Tick(float dt)
        {
            pollTimer -= dt;
            if (pollTimer > 0f) return;
            pollTimer = 0.5f;
            if (StateKey() != builtKey) Phone.Refresh(true);
        }

        static int StateKey()
        {
            unchecked
            {
                int h = 17;
                var o = Deals.Offers;
                var a = Deals.Active;
                h = h * 31 + o.Count;
                h = h * 31 + a.Count;
                for (int i = 0; i < o.Count; i++) if (o[i] != null) h = h * 31 + (o[i].id != null ? o[i].id.GetHashCode() : 0);
                for (int i = 0; i < a.Count; i++)
                    if (a[i] != null) h = h * 31 + (a[i].id != null ? a[i].id.GetHashCode() : 0) + Mathf.FloorToInt(a[i].progress * 10f);
                var s = UIKit.State;
                if (s != null) h = h * 31 + (s.trackedQuest != null ? s.trackedQuest.GetHashCode() : 0) + s.day;
                return h;
            }
        }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            builtKey = StateKey();
            if (s == null) return;
            if (s.followers < Progression.LocalSponsorFollowers)
            {
                var c = PhoneKit.Card(20f);
                c.Add(PhoneKit.T(Loc.Tr("Sponsoren melden sich ab 250 Followern.", "Sponsors get in touch from 250 followers."), PhoneKit.Body, UIPalette.Ink, true));
                var bar = PhoneKit.Bar(s.followers / (float)Progression.LocalSponsorFollowers, Teal, 16f);
                bar.style.marginTop = 12f;
                c.Add(bar);
                body.Add(c);
                return;
            }

            var active = Deals.Active;
            var slots = UIKit.Row();
            slots.style.marginBottom = 6f;
            slots.Add(new UIGlyph(HandshakeGlyph.Draw, UIPalette.TurquoiseInk, 30f));
            var sl = UIKit.Text(Loc.Tr("Aktiv ", "Active ") + active.Count + "/" + Deals.MaxActive, PhoneKit.Big, UIPalette.Ink, true);
            sl.style.marginLeft = 8f;
            slots.Add(sl);
            body.Add(slots);

            for (int i = 0; i < active.Count; i++) if (active[i] != null) body.Add(DealCard(active[i], true));
            if (active.Count == 0)
                body.Add(PhoneKit.Empty(HandshakeGlyph.Draw, Loc.Tr("Noch kein Deal. Nimm ein Angebot an!", "No deal yet. Accept an offer!")));

            body.Add(PhoneKit.Section(Loc.Tr("Angebote", "Offers"), UIGlyphs.Bell));
            var offers = Deals.Offers;
            for (int i = 0; i < offers.Count; i++) if (offers[i] != null) body.Add(DealCard(offers[i], false));
            if (offers.Count == 0)
                body.Add(PhoneKit.Empty(UIGlyphs.Clock, Loc.Tr("Gerade keine Angebote – morgen früh kommt Neues.", "No offers right now – new ones arrive in the morning.")));

            var info = UIWidgets.Notice(Loc.Tr($"Max. 2 Deals gleichzeitig, ohne gleiches Thema. Volle Sealog-Seiten zahlen extra ({Loc.Money(Progression.SponsorPerPage)} pro Seite und Tag ab 5.000 Followern).",
                                              $"Max 2 deals at once, no shared topic. Full Sealog pages pay extra ({Loc.Money(Progression.SponsorPerPage)} per page and day from 5,000 followers)."),
                                       Teal, UIGlyphs.Book, PhoneKit.Tiny);
            info.style.marginTop = 10f;
            body.Add(info);
        }

        VisualElement DealCard(DealState d, bool active)
        {
            var def = SponsorDatabase.Get(d.dealId);
            var sp = SponsorDatabase.GetSponsor(d.sponsorId);
            var c = PhoneKit.Card(18f);
            if (active) { UIKit.Border(c, 2f, UIPalette.A(Teal, 0.6f)); c.style.borderBottomWidth = 4f; }

            var head = UIKit.Row();
            head.Add(Logo(sp));
            var who = UIKit.Col();
            who.style.marginLeft = 10f;
            who.style.flexShrink = 1;
            who.style.flexGrow = 1;
            who.Add(UIKit.Text(sp != null ? sp.Name : d.sponsorId, PhoneKit.Small, UIPalette.InkSoft, true));
            var title = PhoneKit.T(def != null ? def.Title : d.dealId, PhoneKit.Body, UIPalette.Ink, true);
            who.Add(title);
            head.Add(who);
            var reward = UIKit.Col();
            reward.style.alignItems = Align.FlexEnd;
            reward.Add(UIKit.Text("+" + Loc.Money(d.reward), PhoneKit.Body, UIPalette.SunInk, true));
            int days = active ? SponsorService.DaysLeft(d) : d.days;
            string daysText = active
                ? (days == 1 ? Loc.Tr("Läuft noch 1 Tag", "1 day left") : Loc.Tr($"Läuft noch {days} Tage", $"{days} days left"))
                : (days == 1 ? Loc.Tr("1 Tag Zeit", "1 day to do it") : Loc.Tr($"{days} Tage Zeit", $"{days} days to do it"));
            reward.Add(UIKit.Text(daysText, PhoneKit.Tiny, UIPalette.InkSoft, true));
            head.Add(reward);
            c.Add(head);

            if (def != null)
            {
                var desc = PhoneKit.T(def.Description, PhoneKit.Small, UIPalette.InkSoft);
                desc.style.marginTop = 8f;
                c.Add(desc);
            }

            var tags = UIKit.Row();
            tags.style.marginTop = 8f;
            tags.style.flexWrap = Wrap.Wrap;
            if (d.tags != null)
                for (int i = 0; i < d.tags.Length; i++)
                {
                    var p = UIKit.Pill(Deals.TagName(d.tags[i]), UIPalette.A(Teal, 0.18f), UIPalette.TurquoiseInk, PhoneKit.Tiny);
                    p.style.marginRight = 6f;
                    tags.Add(p);
                }
            c.Add(tags);

            if (active)
            {
                if (def != null)
                {
                    float p01 = def.condition == DealCondition.OrderAndFilm
                        ? Mathf.Floor(d.progress) / 2f
                        : d.progress / Mathf.Max(0.01f, def.target);
                    var prow = UIKit.Row();
                    prow.style.marginTop = 10f;
                    var bar = PhoneKit.Bar(p01, Teal, 14f);
                    bar.style.flexGrow = 1;
                    prow.Add(bar);
                    var pt = UIKit.Text(SponsorService.ProgressText(def, d), PhoneKit.Small, UIPalette.Ink, true);
                    pt.style.marginLeft = 10f;
                    prow.Add(pt);
                    c.Add(prow);
                }
                if (confirmCancel == d.id)
                {
                    string sponsor = sp != null ? sp.Name : d.sponsorId;
                    var warn = UIWidgets.Notice(Loc.Tr($"Deal kündigen? Kein Geld, und {sponsor} ist 3 Tage beleidigt.",
                                                       $"Cancel the deal? No money, and {sponsor} sulks for 3 days."), UIPalette.Coral, UIGlyphs.Warning, PhoneKit.Small);
                    warn.style.marginTop = 10f;
                    c.Add(warn);
                    var row = UIKit.Row();
                    var yes = PhoneKit.Button(Loc.Tr("Kündigen", "Cancel deal"), () => { Deals.Cancel(d.id); confirmCancel = null; Phone.Refresh(true); }, UIBtnKind.Danger, PhoneKit.Small);
                    yes.style.flexGrow = 1;
                    row.Add(yes);
                    row.Add(UIKit.HSpacer(8f));
                    var no = PhoneKit.Button(Loc.Tr("Behalten", "Keep it"), () => { confirmCancel = null; Phone.Refresh(true); }, UIBtnKind.Secondary, PhoneKit.Small);
                    no.style.flexGrow = 1;
                    row.Add(no);
                    c.Add(row);
                }
                else
                {
                    var row = UIKit.Row();
                    row.style.marginTop = 12f;
                    bool tracked = UIKit.State != null && UIKit.State.trackedQuest == d.id;
                    var track = PhoneKit.IconButton(UIGlyphs.Pin, tracked ? Loc.Tr("Verfolgt", "Tracked") : Loc.Tr("Verfolgen", "Track"),
                        () => { QuestTracker.Track(tracked ? null : d.id); Phone.Refresh(true); }, tracked ? UIBtnKind.Accent : UIBtnKind.Secondary, PhoneKit.Small);
                    track.style.flexGrow = 1;
                    row.Add(track);
                    row.Add(UIKit.HSpacer(8f));
                    var cancel = PhoneKit.Button(Loc.Tr("Kündigen", "Cancel deal"), () => { confirmCancel = d.id; Phone.Refresh(true); }, UIBtnKind.Danger, PhoneKit.Small);
                    row.Add(cancel);
                    c.Add(row);
                }
            }
            else
            {
                bool can = Deals.CanAccept(d, out string reason, out _, out _);
                var s = UIKit.State;
                int expires = s != null ? Mathf.Max(1, d.expiresDay - s.day) : 1;
                var exp = UIKit.Text(expires == 1 ? Loc.Tr("Angebot gilt bis morgen früh", "Offer ends tomorrow morning")
                                                  : Loc.Tr($"Angebot gilt noch {expires} Tage", $"Offer valid for {expires} more days"),
                                     PhoneKit.Tiny, UIPalette.InkSoft, true);
                exp.style.marginTop = 8f;
                c.Add(exp);
                var accept = PhoneKit.Button(Loc.Tr("Annehmen", "Accept"), () => { Deals.Accept(d.id); Phone.Refresh(true); }, UIBtnKind.Accent, PhoneKit.Body);
                accept.style.marginTop = 10f;
                c.Add(accept);
                if (!can)
                {
                    UIKit.SetEnabled(accept, false);
                    var why = PhoneKit.T(reason ?? "", PhoneKit.Small, UIPalette.CoralInk, true);
                    why.style.marginTop = 6f;
                    c.Add(why);
                }
            }
            return c;
        }

        /// Fantasy logo (Q90): coloured disc with the sponsor's letters.
        static VisualElement Logo(SponsorDef sp)
        {
            var logo = UIKit.Box();
            UIKit.Size(logo, 58f, 58f);
            UIKit.Radius(logo, 29f);
            Color c = sp != null ? sp.color : Teal;
            logo.style.backgroundColor = c;
            UIKit.Border(logo, 3f, Color.white);
            UIKit.Center(logo);
            logo.Add(UIKit.Text(sp != null ? sp.logo : "?", 22f, UIPalette.OnColor(c), true));
            return logo;
        }
    }

    /// Barnacle app icon: two hands shaking.
    public static class HandshakeGlyph
    {
        static Vector2 P(Rect r, float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);

        public static void Draw(Painter2D p, Rect r, Color c)
        {
            float s = Mathf.Min(r.width, r.height);
            p.fillColor = c;
            p.strokeColor = c;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            // left cuff
            p.BeginPath();
            p.MoveTo(P(r, 0.02f, 0.38f)); p.LineTo(P(r, 0.2f, 0.3f)); p.LineTo(P(r, 0.3f, 0.6f)); p.LineTo(P(r, 0.12f, 0.68f));
            p.ClosePath(); p.Fill();
            // right cuff
            p.BeginPath();
            p.MoveTo(P(r, 0.98f, 0.38f)); p.LineTo(P(r, 0.8f, 0.3f)); p.LineTo(P(r, 0.7f, 0.6f)); p.LineTo(P(r, 0.88f, 0.68f));
            p.ClosePath(); p.Fill();
            // clasped hands
            p.lineWidth = s * 0.16f;
            p.BeginPath();
            p.MoveTo(P(r, 0.26f, 0.44f)); p.LineTo(P(r, 0.44f, 0.36f)); p.LineTo(P(r, 0.7f, 0.44f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(P(r, 0.3f, 0.56f)); p.LineTo(P(r, 0.48f, 0.68f)); p.LineTo(P(r, 0.72f, 0.54f));
            p.Stroke();
            // fingers
            p.lineWidth = s * 0.07f;
            for (int i = 0; i < 3; i++)
            {
                float x = 0.46f + i * 0.08f;
                p.BeginPath(); p.MoveTo(P(r, x, 0.5f)); p.LineTo(P(r, x + 0.03f, 0.72f)); p.Stroke();
            }
        }
    }
}
