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
            UIKit.Size(circle, 200f, 200f);
            UIKit.Radius(circle, 100f);
            circle.style.backgroundColor = UIPalette.A(UIPalette.Violet, 0.18f);
            UIKit.Border(circle, 4f, UIPalette.Violet);
            UIKit.Center(circle);
            circle.Add(new UIGlyph(unlocked ? UIGlyphs.Broadcast : UIGlyphs.Lock, UIPalette.Violet, 110f));
            hero.Add(circle);

            var live = UIKit.Pill("● LIVE", UIPalette.Live, Color.white, PhoneKit.Small);
            live.style.alignSelf = Align.Center;
            live.style.marginTop = 18f;
            hero.Add(live);

            var title = PhoneKit.T(unlocked ? Loc.Tr("Kommt im nächsten Update", "Coming in the next update")
                                            : Loc.Tr("Livestream ab 1.000 Followern", "Livestream from 1,000 followers"), PhoneKit.Big, UIPalette.Ink, true);
            title.style.marginTop = 16f;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            hero.Add(title);
            body.Add(hero);

            if (!unlocked)
            {
                var bar = PhoneKit.Bar(s.followers / (float)Progression.LivestreamFollowers, UIPalette.Violet, 18f);
                bar.style.marginTop = 26f;
                body.Add(bar);
                var row = UIKit.Row();
                row.style.marginTop = 8f;
                row.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 28f));
                row.Add(UIKit.Text(" " + Loc.Number(s.followers) + " / " + Loc.Number(Progression.LivestreamFollowers), PhoneKit.Body, UIPalette.Ink, true));
                body.Add(row);
            }
        }
    }

    /// FollowFish: follower count, growth chart, totals and the milestone list.
    public class UIAppFollowFish : UIPhoneApp
    {
        public override string Id => "followfish";
        public override string Title => "FollowFish";
        public override Color Color => UIPalette.Rgb(0x4F7FAE);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Chart;

        public override void Build(VisualElement body)
        {
            var s = UIKit.State;
            if (s == null) return;

            var top = UIKit.Row();
            top.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 60f));
            var big = UIKit.Text(Loc.Number(s.followers), PhoneKit.Huge, UIPalette.Ink, true);
            big.style.marginLeft = 10f;
            top.Add(big);
            body.Add(top);
            long startOfDay = s.followerHistory.Count > 0 ? s.followerHistory[s.followerHistory.Count - 1] : 0;
            long today = s.followers - startOfDay;
            var delta = UIKit.Text(UIKit.Signed(today) + Loc.Tr(" heute", " today"), PhoneKit.Body, today >= 0 ? UIPalette.LeafInk : UIPalette.CoralInk, true);
            body.Add(delta);

            var data = new List<long>(s.followerHistory);
            data.Add(s.followers);
            var chartCard = PhoneKit.Card(16f);
            chartCard.style.marginTop = 14f;
            long max = 10;
            foreach (var v in data) if (v > max) max = v;
            var chart = new UIFollowerChart(data, max);
            chart.style.height = 260f;
            chartCard.Add(chart);
            var axis = UIKit.Row();
            axis.style.marginTop = 6f;
            axis.Add(UIKit.Text(Loc.Tr("Tag 1", "Day 1"), PhoneKit.Tiny, UIPalette.InkSoft));
            axis.Add(UIKit.Flex());
            axis.Add(UIKit.Text(Loc.Tr("Jetzt", "Now"), PhoneKit.Tiny, UIPalette.InkSoft));
            chartCard.Add(axis);
            body.Add(chartCard);

            var tiles = UIKit.Row();
            tiles.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(s.totalViews)));
            tiles.Add(UIKit.HSpacer(8f));
            tiles.Add(PhoneKit.Stat(UIGlyphs.Play, UIPalette.Danger, s.videos.Count.ToString()));
            tiles.Add(UIKit.HSpacer(8f));
            tiles.Add(PhoneKit.Stat(UIGlyphs.Book, UIPalette.Turquoise, Journal.DiscoveredCount + "/" + SpeciesDatabase.All.Count));
            body.Add(tiles);

            body.Add(PhoneKit.Section(Loc.Tr("Meilensteine", "Milestones"), UIGlyphs.Star));
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
            UIKit.Pad(row, 14f, 16f, 14f, 14f);
            UIKit.Radius(row, 20f);
            row.style.marginBottom = 8f;
            row.style.backgroundColor = isNext ? UIPalette.A(UIPalette.Sun, 0.25f) : Color.white;
            if (isNext) UIKit.Border(row, 2f, UIPalette.SunInk);
            if (!reached && !isNext) row.style.opacity = 0.6f;

            var icon = UIKit.Box();
            UIKit.Size(icon, 40f, 40f);
            UIKit.Radius(icon, 20f);
            UIKit.Center(icon);
            if (reached)
            {
                icon.style.backgroundColor = UIPalette.LeafInk;
                icon.Add(new UIGlyph(UIGlyphs.Check, Color.white, 26f));
            }
            else
            {
                UIKit.Border(icon, 3f, isNext ? UIPalette.SunInk : UIPalette.Silhouette);
                if (isNext) icon.Add(new UIGlyph(UIGlyphs.Star, UIPalette.SunInk, 22f));
            }
            row.Add(icon);

            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(PhoneKit.T(m.Title, PhoneKit.Body, UIPalette.Ink, true));
            col.Add(PhoneKit.T(m.Description, PhoneKit.Tiny, UIPalette.InkSoft));
            if (isNext)
            {
                var bar = PhoneKit.Bar((followers - prev) / (float)System.Math.Max(1L, m.followers - prev), UIPalette.SunInk, 12f);
                bar.style.marginTop = 8f;
                col.Add(bar);
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
            float left = r.x + 4f, right = r.xMax - 10f, top = r.y + 10f, bottom = r.yMax - 4f;

            p.strokeColor = UIPalette.Line;
            p.lineWidth = 2f;
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

            p.fillColor = UIPalette.A(UIPalette.Turquoise, 0.22f);
            p.BeginPath();
            p.MoveTo(new Vector2(n == 1 ? left : Pt(0).x, bottom));
            if (n == 1) p.LineTo(new Vector2(left, Pt(0).y));
            for (int i = 0; i < n; i++) p.LineTo(Pt(i));
            p.LineTo(new Vector2(Pt(n - 1).x, bottom));
            p.ClosePath();
            p.Fill();

            p.strokeColor = UIPalette.TurquoiseInk;
            p.lineWidth = 5f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            if (n == 1) { p.MoveTo(new Vector2(left, Pt(0).y)); p.LineTo(Pt(0)); }
            else { p.MoveTo(Pt(0)); for (int i = 1; i < n; i++) p.LineTo(Pt(i)); }
            p.Stroke();

            var last = Pt(n - 1);
            p.fillColor = UIPalette.TurquoiseInk;
            p.BeginPath(); p.Arc(last, 10f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            p.fillColor = Color.white;
            p.BeginPath(); p.Arc(last, 5f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
        }
    }
}
