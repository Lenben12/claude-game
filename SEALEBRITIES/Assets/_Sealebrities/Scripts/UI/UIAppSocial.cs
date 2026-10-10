using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// KRILL.tv livestream app (locked below 1,000 followers, "coming in the next update" above).
    public class UIAppKrill : UIPhoneApp
    {
        public override string Id => "krill";
        public override string Title => "KRILL.tv";
        public override Color Color => UIPalette.Violet;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Broadcast;

        public override bool ShowLock
        {
            get
            {
                var s = UIKit.State;
                return s == null || s.followers < Progression.LivestreamFollowers;
            }
        }

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;
            bool unlocked = s.followers >= Progression.LivestreamFollowers;

            var hero = UIKit.Col();
            hero.style.alignItems = Align.Center;
            hero.style.marginTop = 30f;
            var circle = UIKit.Box();
            UIKit.Size(circle, 120f, 120f);
            UIKit.Radius(circle, 60f);
            circle.style.backgroundColor = UIPalette.A(UIPalette.Violet, 0.18f);
            UIKit.Border(circle, 2f, UIPalette.A(UIPalette.Violet, 0.6f));
            circle.style.alignItems = Align.Center;
            circle.style.justifyContent = Justify.Center;
            circle.Add(new UIGlyph(unlocked ? UIGlyphs.Broadcast : UIGlyphs.Lock, UIPalette.Violet, 64f));
            hero.Add(circle);

            var live = UIKit.Pill("● LIVE", UIPalette.Live, Color.white, 12f);
            live.style.alignSelf = Align.Center;
            live.style.marginTop = 14f;
            hero.Add(live);

            var title = UIKit.Text(unlocked ? Loc.Tr("Kommt im nächsten Update", "Coming in the next update")
                                            : Loc.Tr("Livestream ab 1.000 Followern", "Livestream from 1,000 followers"), 22f, UIPalette.Text, true);
            title.style.marginTop = 14f;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.whiteSpace = WhiteSpace.Normal;
            hero.Add(title);
            body.Add(hero);

            var text = UIKit.Para(unlocked
                ? Loc.Tr("Du hast es geschafft! Bald streamst du live aus dem Wasser: Zuschauer schreiben im Chat, spenden Krill-Coins und wünschen sich Tiere. Mit Relay-Boje sogar aus der Tiefe.",
                         "You made it! Soon you'll stream live from the water: viewers chat, donate krill coins and request animals. With a relay buoy even from the deep.")
                : Loc.Tr("Sammle mehr Follower mit FinTube-Videos. Ab 1.000 Followern schaltest du KRILL.tv frei – live aus dem Ozean!",
                         "Grow your followers with FinTube videos. At 1,000 followers you unlock KRILL.tv – live from the ocean!"), 14f, UIPalette.TextDim);
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.marginTop = 10f;
            body.Add(text);

            if (!unlocked)
            {
                var bar = new UIBar(10f, UIPalette.Violet);
                bar.style.marginTop = 22f;
                bar.SetValue(s.followers / (float)Progression.LivestreamFollowers);
                body.Add(bar);
                var row = UIKit.Row();
                row.style.marginTop = 6f;
                row.Add(UIKit.Text(Loc.Number(s.followers) + " / " + Loc.Number(Progression.LivestreamFollowers), 13f, UIPalette.Text, true));
                row.Add(UIKit.Flex());
                row.Add(UIKit.Text(Loc.Tr("noch ", "") + Loc.Number(Progression.LivestreamFollowers - s.followers) + Loc.Tr("", " to go"), 13f, UIPalette.TextDim));
                body.Add(row);
            }
        }
    }

    /// FollowFish: follower count, growth chart, totals and the milestone list.
    public class UIAppFollowFish : UIPhoneApp
    {
        public override string Id => "followfish";
        public override string Title => "FollowFish";
        public override Color Color => UIPalette.Sky;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Chart;

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;

            body.Add(UIKit.Caption(Loc.Tr("Follower", "Followers"), UIPalette.TextFaint, 12f));
            var big = UIKit.Text(Loc.Number(s.followers), 46f, UIPalette.Text, true);
            body.Add(big);
            long startOfDay = s.followerHistory.Count > 0 ? s.followerHistory[s.followerHistory.Count - 1] : 0;
            long today = s.followers - startOfDay;
            var delta = UIKit.Text(UIKit.Signed(today) + Loc.Tr(" heute", " today"), 14f, today >= 0 ? UIPalette.Money : UIPalette.Coral, true);
            body.Add(delta);

            // chart
            var data = new List<long>(s.followerHistory);
            data.Add(s.followers);
            var chartCard = UIKit.Card(new Color(1f, 1f, 1f, 0.05f), 16f, 12f);
            chartCard.style.marginTop = 12f;
            long max = 10;
            foreach (var v in data) if (v > max) max = v;
            var top = UIKit.Row();
            top.Add(UIKit.Text(Loc.Tr("Verlauf", "History"), 12f, UIPalette.TextDim, true));
            top.Add(UIKit.Flex());
            top.Add(UIKit.Text("max " + Loc.Number(max), 11f, UIPalette.TextFaint));
            chartCard.Add(top);
            var chart = new UIFollowerChart(data, max);
            chart.style.height = 170f;
            chart.style.marginTop = 6f;
            chartCard.Add(chart);
            var axis = UIKit.Row();
            axis.style.marginTop = 4f;
            axis.Add(UIKit.Text(Loc.Tr("Tag 1", "Day 1"), 11f, UIPalette.TextFaint));
            axis.Add(UIKit.Flex());
            axis.Add(UIKit.Text(Loc.Tr("Jetzt", "Now"), 11f, UIPalette.TextFaint));
            chartCard.Add(axis);
            body.Add(chartCard);

            var tiles = UIKit.Row();
            tiles.style.marginTop = 10f;
            tiles.Add(UIWidgets.StatTile(Loc.Number(s.totalViews), Loc.Tr("Aufrufe gesamt", "Total views"), UIPalette.Text));
            tiles.Add(UIKit.HSpacer(6f));
            tiles.Add(UIWidgets.StatTile(s.videos.Count.ToString(), Loc.Tr("Videos", "Videos"), UIPalette.Live));
            tiles.Add(UIKit.HSpacer(6f));
            tiles.Add(UIWidgets.StatTile(Journal.DiscoveredCount + "/" + SpeciesDatabase.All.Count, Loc.Tr("Arten", "Species"), UIPalette.Lagoon));
            body.Add(tiles);

            body.Add(UIWidgets.SectionTitle(Loc.Tr("Meilensteine · Ziel 1.000.000", "Milestones · goal 1,000,000")));
            var next = Progression.NextMilestone(s.followers);
            long prev = 0;
            foreach (var m in Progression.Milestones)
            {
                bool reached = s.followers >= m.followers || s.reachedMilestones.Contains(m.id);
                bool isNext = m == next;
                body.Add(MilestoneRow(m, reached, isNext, s.followers, prev));
                if (reached) prev = m.followers;
            }
        }

        static VisualElement MilestoneRow(Milestone m, bool reached, bool isNext, long followers, long prev)
        {
            var row = UIKit.Row();
            row.style.alignItems = Align.FlexStart;
            UIKit.Pad(row, 10f, 12f, 10f, 10f);
            UIKit.Radius(row, 12f);
            row.style.marginBottom = 5f;
            row.style.backgroundColor = isNext ? UIPalette.A(UIPalette.Sun, 0.12f) : new Color(1f, 1f, 1f, reached ? 0.05f : 0.02f);
            if (isNext) UIKit.Border(row, 1f, UIPalette.A(UIPalette.Sun, 0.6f));

            var icon = UIKit.Box();
            UIKit.Size(icon, 24f, 24f);
            UIKit.Radius(icon, 12f);
            icon.style.alignItems = Align.Center; icon.style.justifyContent = Justify.Center;
            if (reached)
            {
                icon.style.backgroundColor = UIPalette.Money;
                icon.Add(new UIGlyph(UIGlyphs.Check, UIPalette.Deep, 16f));
            }
            else
            {
                UIKit.Border(icon, 2f, isNext ? UIPalette.Sun : UIPalette.TextFaint);
                if (isNext) icon.Add(new UIGlyph(UIGlyphs.Star, UIPalette.Sun, 13f));
            }
            row.Add(icon);

            var col = UIKit.Col();
            col.style.marginLeft = 10f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text(m.Title, 14.5f, reached ? UIPalette.Text : isNext ? UIPalette.Sun : UIPalette.TextDim, true));
            col.Add(UIKit.Para(m.Description, 12f, reached || isNext ? UIPalette.TextDim : UIPalette.TextFaint));
            if (isNext)
            {
                var bar = new UIBar(6f, UIPalette.Sun);
                bar.style.marginTop = 6f;
                bar.SetValue((followers - prev) / (float)System.Math.Max(1L, m.followers - prev));
                col.Add(bar);
                var left = UIKit.Text(Loc.Tr("noch ", "") + Loc.Number(m.followers - followers) + Loc.Tr(" Follower", " followers to go"), 11f, UIPalette.Sun);
                left.style.marginTop = 3f;
                col.Add(left);
            }
            row.Add(col);
            return row;
        }
    }

    /// Line chart of the follower history (Painter2D).
    public class UIFollowerChart : VisualElement
    {
        readonly List<long> data;
        readonly long max;

        public UIFollowerChart(List<long> data, long max)
        {
            this.data = data;
            this.max = System.Math.Max(1L, max);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4f || r.height < 4f) return;
            var p = ctx.painter2D;
            float left = r.x + 2f, right = r.xMax - 6f, top = r.y + 8f, bottom = r.yMax - 2f;

            p.strokeColor = UIPalette.Line;
            p.lineWidth = 1f;
            for (int i = 0; i <= 4; i++)
            {
                float y = Mathf.Lerp(bottom, top, i / 4f);
                p.BeginPath(); p.MoveTo(new Vector2(left, y)); p.LineTo(new Vector2(right, y)); p.Stroke();
            }

            int n = data.Count;
            if (n == 0) return;
            Vector2 Pt(int i)
            {
                float x = n == 1 ? right : Mathf.Lerp(left, right, i / (float)(n - 1));
                float y = Mathf.Lerp(bottom, top, Mathf.Clamp01(data[i] / (float)max));
                return new Vector2(x, y);
            }

            // area
            p.fillColor = UIPalette.A(UIPalette.Lagoon, 0.16f);
            p.BeginPath();
            p.MoveTo(new Vector2(n == 1 ? left : Pt(0).x, bottom));
            if (n == 1) p.LineTo(new Vector2(left, Pt(0).y));
            for (int i = 0; i < n; i++) p.LineTo(Pt(i));
            p.LineTo(new Vector2(Pt(n - 1).x, bottom));
            p.ClosePath();
            p.Fill();

            // line
            p.strokeColor = UIPalette.Lagoon;
            p.lineWidth = 3f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            if (n == 1) { p.MoveTo(new Vector2(left, Pt(0).y)); p.LineTo(Pt(0)); }
            else { p.MoveTo(Pt(0)); for (int i = 1; i < n; i++) p.LineTo(Pt(i)); }
            p.Stroke();

            // current value dot
            var last = Pt(n - 1);
            p.fillColor = UIPalette.Lagoon;
            p.BeginPath(); p.Arc(last, 6f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            p.fillColor = Color.white;
            p.BeginPath(); p.Arc(last, 3f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
        }
    }
}
