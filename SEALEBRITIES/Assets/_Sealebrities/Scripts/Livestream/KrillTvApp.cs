using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// The KRILL.tv phone app (C.7): big „Live gehen“ button (or the reason why not), the running stream with live
    /// numbers and „Stream beenden“, the last result, short how-to rows and the last streams. Replaces the round-1
    /// placeholder in the phone while the UI module has no C.7 app of its own (LivestreamModule injects it).
    public sealed class KrillTvApp : UIPhoneApp
    {
        public override string Id => "krill";
        public override string Title => "KRILL.tv";
        public override Color Color => KrillGlyph.Purple;
        public override UIGlyph.DrawFn Icon => KrillGlyph.Draw;
        public override bool ShowLock => !KrillTv.Unlocked;

        /// A dot while today's stream is still possible (nudge), nothing otherwise.
        public override int Badge => KrillTv.Unlocked && !LiveState.IsLive && !KrillTv.StreamedToday && UIKit.InGame ? -1 : 0;

        // live labels (updated in place)
        Label liveTime, liveViewers, liveMoney, livePending, liveHype;
        UIBar liveBar;
        VisualElement noSignal, dareRow;
        Label dareLabel;
        bool builtLive, builtCan;
        string builtReason;
        float pollTimer;
        int shownViewers = -1, shownSeconds = -1;
        long shownMoney = -1, shownPending = -1;
        int shownHype = -1, shownDare = -2;

        public override void OnOpen() { pollTimer = 0f; }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            liveTime = liveViewers = liveMoney = livePending = liveHype = dareLabel = null;
            liveBar = null; noSignal = null; dareRow = null;
            shownViewers = -1; shownSeconds = -1; shownMoney = -1; shownPending = -1; shownHype = -1; shownDare = -2;
            if (s == null) return;
            builtLive = LiveState.IsLive;
            builtCan = KrillTv.CanGoLive(out builtReason);

            body.Add(Hero());
            if (!KrillTv.Unlocked)
            {
                BuildLocked(body, s.followers);
                body.Add(Tips());
                return;
            }
            if (builtLive) BuildLive(body);
            else
            {
                BuildGoLive(body);
                var sum = KrillTv.LastSummary;
                if (sum != null) body.Add(SummaryCard(sum));
            }
            body.Add(Tips());
            BuildHistory(body, s);
        }

        // ---------------------------------------------------------------- sections
        static VisualElement Hero()
        {
            var hero = UIKit.Row();
            hero.style.backgroundColor = KrillGlyph.Purple;
            UIKit.Radius(hero, 26f);
            UIKit.Pad(hero, 18f, 20f, 18f, 18f);
            hero.style.marginBottom = 14f;
            hero.style.borderBottomWidth = 5f;
            hero.style.borderBottomColor = UIPalette.Darken(KrillGlyph.Purple, 0.25f);
            var icon = UIWidgets.IconTile(KrillGlyph.Draw, UIPalette.Darken(KrillGlyph.Purple, 0.18f), 92f);
            hero.Add(icon);
            var col = UIKit.Col();
            col.style.marginLeft = 16f;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text("KRILL.tv", PhoneKit.Title, Color.white, true));
            var tag = PhoneKit.T(Loc.Tr("Live aus der Tiefe. Fast.", "Live from the deep. Almost."), PhoneKit.Small, new Color(1f, 1f, 1f, 0.85f));
            col.Add(tag);
            hero.Add(col);
            return hero;
        }

        static void BuildLocked(VisualElement body, long followers)
        {
            var c = PhoneKit.Card(20f);
            var row = UIKit.Row();
            row.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 34f));
            var t = PhoneKit.T(Loc.Tr("Livestream ab 1.000 Followern", "Livestream from 1,000 followers"), PhoneKit.Body, UIPalette.Ink, true);
            t.style.marginLeft = 10f;
            t.style.flexShrink = 1;
            row.Add(t);
            c.Add(row);
            var bar = PhoneKit.Bar(followers / (float)StreamRules.UnlockFollowers, KrillGlyph.Purple, 16f);
            bar.style.marginTop = 14f;
            c.Add(bar);
            var n = UIKit.Row();
            n.style.marginTop = 8f;
            n.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 26f));
            n.Add(UIKit.Text(" " + Loc.Number(followers) + " / " + Loc.Number(StreamRules.UnlockFollowers), PhoneKit.Body, UIPalette.Ink, true));
            c.Add(n);
            body.Add(c);
        }

        void BuildGoLive(VisualElement body)
        {
            var c = PhoneKit.Card(20f);
            var btn = PhoneKit.Button("● " + Loc.Tr("Live gehen", "Go live"), OnGoLive, UIBtnKind.Live, PhoneKit.Big);
            UIKit.Pad(btn, 22f, 20f, 22f, 20f);
            UIKit.Radius(btn, 22f);
            c.Add(btn);
            if (!builtCan)
            {
                UIKit.SetEnabled(btn, false);
                var why = UIKit.Row();
                why.style.marginTop = 12f;
                why.style.justifyContent = Justify.Center;
                why.Add(new UIGlyph(KrillTv.StreamedToday ? UIGlyphs.Moon : UIGlyphs.Boat, UIPalette.InkSoft, 26f));
                var l = PhoneKit.T(builtReason ?? "", PhoneKit.Body, UIPalette.InkSoft, true);
                l.style.marginLeft = 8f;
                why.Add(l);
                c.Add(why);
            }
            var hint = PhoneKit.T(Loc.Tr($"Ca. +{Loc.Money(KrillTv.ExpectedMoney())} in 10 Min. · Follower gibt's am Ende",
                                         $"About +{Loc.Money(KrillTv.ExpectedMoney())} in 10 min · followers at the end"),
                                  PhoneKit.Small, UIPalette.InkSoft);
            hint.style.marginTop = 12f;
            hint.style.unityTextAlign = TextAnchor.MiddleCenter;
            c.Add(hint);
            // without the boat compressor a stream at a dive site has one tank (~2 min) of its 10 minutes
            var gm = GameManager.I;
            if (gm != null && gm.InGame && !gm.Gear.hasBoatCompressor)
            {
                var tank = UIKit.Row();
                tank.style.marginTop = 10f;
                tank.style.justifyContent = Justify.Center;
                tank.Add(new UIGlyph(UIGlyphs.Tank, UIPalette.InkSoft, 22f));
                var tl = PhoneKit.T(Loc.Tr("Ohne Boot-Kompressor: nur 1 Flasche (Nachfüllen am Steg)", "No boat compressor: 1 tank (refill at the dock)"),
                                    PhoneKit.Small, UIPalette.InkSoft, true);
                tl.style.marginLeft = 6f;
                tl.style.whiteSpace = WhiteSpace.Normal;
                tl.style.flexShrink = 1;
                tank.Add(tl);
                c.Add(tank);
            }
            body.Add(c);
        }

        void OnGoLive()
        {
            if (KrillTv.RequestStart()) Phone.Close();
            else Phone.Refresh(true);
        }

        void BuildLive(VisualElement body)
        {
            var c = PhoneKit.Card(20f);
            var head = UIKit.Row();
            head.Add(UIKit.Pill("● LIVE", UIPalette.Live, Color.white, PhoneKit.Small));
            head.Add(UIKit.Flex());
            head.Add(new UIGlyph(UIGlyphs.Clock, UIPalette.InkSoft, 26f));
            liveTime = UIKit.Text("", PhoneKit.Body, UIPalette.Ink, true);
            liveTime.style.marginLeft = 6f;
            head.Add(liveTime);
            c.Add(head);

            liveBar = new UIBar(12f, KrillGlyph.Purple);
            liveBar.style.marginTop = 12f;
            c.Add(liveBar);

            var r1 = UIKit.Row();
            r1.style.marginTop = 14f;
            liveViewers = StatBox(r1, UIGlyphs.Eye, KrillGlyph.Purple);
            r1.Add(UIKit.HSpacer(8f));
            liveMoney = StatBox(r1, UIGlyphs.Coin, UIPalette.Sun);
            c.Add(r1);
            var r2 = UIKit.Row();
            r2.style.marginTop = 8f;
            livePending = StatBox(r2, UIGlyphs.Person, UIPalette.Turquoise);
            r2.Add(UIKit.HSpacer(8f));
            liveHype = StatBox(r2, UIGlyphs.Star, UIPalette.Coral);
            c.Add(r2);

            noSignal = UIWidgets.Notice(Loc.Tr("Kein Signal! Zurück zum Boot.", "No signal! Back to the boat."), UIPalette.Coral, UIGlyphs.Warning, PhoneKit.Small);
            noSignal.style.marginTop = 10f;
            c.Add(noSignal);

            dareRow = UIKit.Row();
            dareRow.style.marginTop = 10f;
            dareRow.Add(new UIGlyph(UIGlyphs.Bubble, UIPalette.Violet, 26f));
            dareLabel = PhoneKit.T("", PhoneKit.Small, UIPalette.Ink, true);
            dareLabel.style.marginLeft = 8f;
            dareLabel.style.flexShrink = 1;
            dareRow.Add(dareLabel);
            c.Add(dareRow);

            var end = PhoneKit.Button(Loc.Tr("Stream beenden", "End stream"), () => { KrillTv.RequestEnd(); Phone.Refresh(true); }, UIBtnKind.Danger, PhoneKit.Body);
            end.style.marginTop = 16f;
            c.Add(end);
            var note = PhoneKit.T(Loc.Tr("Follower gibt's nur bei normalem Ende – Ohnmacht kostet!", "Followers only on a normal end – fainting costs!"),
                                  PhoneKit.Tiny, UIPalette.InkSoft);
            note.style.marginTop = 8f;
            note.style.unityTextAlign = TextAnchor.MiddleCenter;
            c.Add(note);
            body.Add(c);
            UpdateLive(true);
        }

        static Label StatBox(VisualElement row, UIGlyph.DrawFn icon, Color iconColor)
        {
            var box = UIKit.Row();
            box.style.flexGrow = 1;
            box.style.flexBasis = 0;
            box.style.backgroundColor = UIPalette.CardAlt;
            UIKit.Radius(box, 16f);
            UIKit.Pad(box, 10f, 12f, 10f, 12f);
            box.Add(new UIGlyph(icon, iconColor, 30f));
            var l = UIKit.Text("", PhoneKit.Big, UIPalette.Ink, true);
            l.style.marginLeft = 8f;
            l.style.flexShrink = 1;
            box.Add(l);
            row.Add(box);
            return l;
        }

        static VisualElement SummaryCard(StreamSummary sum)
        {
            var c = PhoneKit.Card(18f);
            var head = UIKit.Row();
            head.Add(new UIGlyph(sum.fainted ? UIGlyphs.Warning : UIGlyphs.Check, sum.fainted ? UIPalette.CoralInk : UIPalette.LeafInk, 30f));
            var t = UIKit.Text(sum.fainted ? Loc.Tr("Stream abgebrochen", "Stream lost") : Loc.Tr("Letzter Stream", "Last stream"),
                               PhoneKit.Body, sum.fainted ? UIPalette.CoralInk : UIPalette.Ink, true);
            t.style.marginLeft = 8f;
            head.Add(t);
            c.Add(head);
            var row = UIKit.Row();
            row.style.marginTop = 10f;
            row.style.flexWrap = Wrap.Wrap;
            Chip(row, UIGlyphs.Clock, UIPalette.InkSoft, UIKit.ClockShort(Mathf.RoundToInt(sum.seconds)), UIPalette.Ink);
            Chip(row, UIGlyphs.Eye, KrillGlyph.Purple, Loc.Number(sum.peakViewers), UIPalette.Ink);
            Chip(row, UIGlyphs.Coin, UIPalette.Sun, "+" + Loc.Money(sum.money), UIPalette.Ink);
            Chip(row, UIGlyphs.Person, UIPalette.Turquoise, UIKit.Signed(sum.followersDelta), sum.followersDelta >= 0 ? UIPalette.LeafInk : UIPalette.CoralInk);
            c.Add(row);
            string best = sum.BestLine();
            if (!string.IsNullOrEmpty(best))
            {
                var b = PhoneKit.T(best, PhoneKit.Small, UIPalette.InkSoft, true);
                b.style.marginTop = 6f;
                c.Add(b);
            }
            return c;
        }

        static void Chip(VisualElement row, UIGlyph.DrawFn icon, Color ic, string text, Color tc)
        {
            var r = UIKit.Row();
            r.style.marginRight = 14f;
            r.style.marginTop = 4f;
            r.Add(new UIGlyph(icon, ic, 24f));
            var l = UIKit.Text(text, PhoneKit.Body, tc, true);
            l.style.marginLeft = 4f;
            r.Add(l);
            row.Add(r);
        }

        static VisualElement Tips()
        {
            var c = PhoneKit.Card(18f);
            c.Add(PhoneKit.T(Loc.Tr("So geht's", "How it works"), PhoneKit.Body, UIPalette.Ink, true));
            Tip(c, UIGlyphs.Camera, UIPalette.TurquoiseInk, Loc.Tr("Kamera in der Hand: Jedes Tier im Bild bringt Zuschauer.", "Camera in hand: every animal on screen brings viewers."));
            Tip(c, UIGlyphs.Star, UIPalette.CoralInk,
                Loc.Tr($"Mutproben mit [{GameInput.BindingLabel(GameAction.StreamDare)}] annehmen – riskant, aber viel Geld.",
                       $"Accept dares with [{GameInput.BindingLabel(GameAction.StreamDare)}] – risky, but big money."));
            Tip(c, UIGlyphs.Boat, UIPalette.Sky, Loc.Tr("Signal nur in Bootsnähe – sonst laufen die Zuschauer weg.", "Signal only near the boat – or the viewers leave."));
            Tip(c, UIGlyphs.Warning, UIPalette.Danger, Loc.Tr("Ohnmacht = Stream weg, Follower weg.", "Faint = stream gone, followers gone."));
            return c;
        }

        static void Tip(VisualElement card, UIGlyph.DrawFn icon, Color color, string text)
        {
            var r = UIKit.Row();
            r.style.marginTop = 10f;
            r.style.alignItems = Align.FlexStart;
            r.Add(new UIGlyph(icon, color, 28f));
            var l = PhoneKit.T(text, PhoneKit.Small, UIPalette.Ink);
            l.style.marginLeft = 10f;
            l.style.flexShrink = 1;
            r.Add(l);
            card.Add(r);
        }

        static void BuildHistory(VisualElement body, GameState s)
        {
            if (s.streams == null || s.streams.Count == 0) return;
            body.Add(PhoneKit.Section(Loc.Tr("Letzte Streams", "Recent streams"), UIGlyphs.Broadcast));
            int shown = 0;
            for (int i = s.streams.Count - 1; i >= 0 && shown < 5; i--, shown++)
            {
                var r = s.streams[i];
                if (r == null) continue;
                var row = UIKit.Row();
                row.style.backgroundColor = Color.white;
                UIKit.Radius(row, 18f);
                UIKit.Pad(row, 12f, 16f, 12f, 16f);
                row.style.marginBottom = 8f;
                var left = UIKit.Col();
                left.Add(UIKit.Text(Loc.Tr("Tag ", "Day ") + r.day, PhoneKit.Body, UIPalette.Ink, true));
                left.Add(UIKit.Text(UIKit.ClockShort(Mathf.RoundToInt(r.seconds)) + " · " + Loc.Number(r.peakViewers) + Loc.Tr(" max", " peak"), PhoneKit.Tiny, UIPalette.InkSoft));
                row.Add(left);
                row.Add(UIKit.Flex());
                var right = UIKit.Col();
                right.style.alignItems = Align.FlexEnd;
                right.Add(UIKit.Text("+" + Loc.Money(r.money), PhoneKit.Body, UIPalette.SunInk, true));
                string f = r.fainted ? Loc.Tr("Abgebrochen ", "Lost ") + UIKit.Signed(r.followers) : UIKit.Signed(r.followers) + Loc.Tr(" Follower", " followers");
                right.Add(UIKit.Text(f, PhoneKit.Tiny, r.followers >= 0 ? UIPalette.LeafInk : UIPalette.CoralInk, true));
                row.Add(right);
                body.Add(row);
            }
        }

        // ---------------------------------------------------------------- live updates
        public override void Tick(float dt)
        {
            if (LiveState.IsLive != builtLive) { Phone.Refresh(true); return; }
            if (builtLive) { UpdateLive(false); return; }
            pollTimer -= dt;
            if (pollTimer > 0f) return;
            pollTimer = 0.5f;
            bool can = KrillTv.CanGoLive(out string reason);
            if (can != builtCan || reason != builtReason) Phone.Refresh(true);
        }

        void UpdateLive(bool force)
        {
            if (liveTime == null) return;
            int sec = Mathf.FloorToInt(LiveState.Seconds);
            if (force || sec != shownSeconds)
            {
                shownSeconds = sec;
                liveTime.text = UIKit.ClockShort(sec) + " / " + UIKit.ClockShort(Mathf.RoundToInt(LiveState.MaxSeconds));
            }
            liveBar.SetValue(LiveState.Seconds / Mathf.Max(1f, LiveState.MaxSeconds));
            if (force || LiveState.Viewers != shownViewers)
            {
                shownViewers = LiveState.Viewers;
                liveViewers.text = UIKit.Compact(shownViewers);
            }
            if (force || LiveState.MoneyThisStream != shownMoney)
            {
                shownMoney = LiveState.MoneyThisStream;
                liveMoney.text = "+" + UIKit.Compact(shownMoney) + " $";
            }
            if (force || LiveState.PendingFollowers != shownPending)
            {
                shownPending = LiveState.PendingFollowers;
                livePending.text = "+" + UIKit.Compact(shownPending);
            }
            int hype = Mathf.RoundToInt(LiveState.Hype * 10f);
            if (force || hype != shownHype)
            {
                shownHype = hype;
                liveHype.text = "×" + (hype / 10f).ToString("0.0", Loc.Culture);
            }
            UIKit.Show(noSignal, LiveState.NoSignal);
            var d = LiveState.Dare;
            int dareKey = d == null ? -1 : KrillTv.DareSerial * 8 + (int)d.state;
            if (force || dareKey != shownDare)
            {
                shownDare = dareKey;
                var def = StreamDares.At(KrillTv.DareIndex);
                UIKit.Show(dareRow, d != null && def != null);
                if (d != null && def != null)
                {
                    string state = d.state == DareState.Offered ? Loc.Tr("Mutprobe: ", "Dare: ")
                        : d.state == DareState.Running ? Loc.Tr("Läuft: ", "Running: ")
                        : d.state == DareState.Done ? Loc.Tr("Geschafft: ", "Done: ") : Loc.Tr("Vorbei: ", "Over: ");
                    dareLabel.text = state + StreamDares.Text(def, d.targetPlayerId);
                }
            }
        }
    }
}
