using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// D.1 reel tension gauge: a thin semicircle (220 × 110 px) just above the hotbar centre – zones green / yellow / red,
    /// the needle = line tension, under it the fish-stamina bar, a fish icon wiggling toward the current run direction.
    /// Reads FishingReel (fishing+boat) and sets FishingReel.GaugeByUI so the rod drops its text fallback.
    public class UIReelGauge
    {
        readonly VisualElement root;
        readonly GaugeArc arc;
        readonly UIBar stamina;
        readonly UIHudGlyph fish;
        bool shown;
        float wiggle;

        public UIReelGauge(VisualElement layer)
        {
            root = UIKit.Col();
            UIKit.Abs(root, 0f, null, 0f, 176f);
            root.style.alignItems = Align.Center;
            root.pickingMode = PickingMode.Ignore;
            layer.Add(root);
            arc = new GaugeArc();
            UIKit.Size(arc, 220f, 116f);
            root.Add(arc);
            var row = UIKit.Row();
            row.style.marginTop = 4f;
            fish = new UIHudGlyph(UIGlyphs.Fish, Color.white, 28f);
            fish.style.marginRight = 8f;
            row.Add(fish);
            stamina = new UIBar(8f, UIPalette.Coral, new Color(1f, 1f, 1f, 0.3f));
            stamina.style.width = 150f;
            row.Add(stamina);
            root.Add(row);
            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
            FishingReel.GaugeByUI = true;
        }

        public void Dispose() { FishingReel.GaugeByUI = false; }

        public void Tick(float dt)
        {
            bool show = FishingReel.Active && UIKit.InGame && !UIState.BlocksGameplay;
            if (show != shown) { shown = show; UIKit.Show(root, show); }
            FishingReel.GaugeByUI = true;
            if (!show) return;
            arc.Set(FishingReel.Tension, FishingReel.GreenMin, FishingReel.GreenMax, FishingReel.YellowMax, FishingReel.Snap01);
            stamina.SetValue(Mathf.Clamp01(FishingReel.FishStamina01));
            wiggle += dt * (FishingReel.Running ? 14f : 5f);
            float x = FishingReel.RunDir * 10f + Mathf.Sin(wiggle) * (FishingReel.Running ? 5f : 2f);
            fish.style.translate = new Translate(x, 0f);
            fish.SetColor(FishingReel.Countering ? UIPalette.Leaf : FishingReel.Running ? UIPalette.Warn : Color.white);
        }

        /// The semicircle with the zones and the needle (Painter2D, repainted only when a value changes visibly).
        class GaugeArc : VisualElement
        {
            float tension = -1f, gMin, gMax, yMax, snap;

            public GaugeArc()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Draw;
            }

            public void Set(float t, float greenMin, float greenMax, float yellowMax, float snap01)
            {
                t = float.IsNaN(t) ? 0f : Mathf.Clamp(t, 0f, 100f);
                if (Mathf.Abs(t - tension) < 0.3f && greenMin == gMin && greenMax == gMax && yellowMax == yMax && Mathf.Abs(snap01 - snap) < 0.02f) return;
                tension = t; gMin = greenMin; gMax = greenMax; yMax = yellowMax; snap = snap01;
                MarkDirtyRepaint();
            }

            static float Ang(float pct) => 180f + Mathf.Clamp(pct, 0f, 100f) * 1.8f;   // 0 % left … 100 % right (top arc)

            void Band(Painter2D p, Vector2 c, float r, float a, float b, Color col, float width)
            {
                if (b <= a) return;
                p.strokeColor = col;
                p.lineWidth = width;
                p.lineCap = LineCap.Butt;
                p.BeginPath();
                p.Arc(c, r, Angle.Degrees(Ang(a)), Angle.Degrees(Ang(b)));
                p.Stroke();
            }

            void Draw(MeshGenerationContext ctx)
            {
                var r = contentRect;
                if (r.width < 4f) return;
                var p = ctx.painter2D;
                var c = new Vector2(r.center.x, r.yMax - 8f);
                float rad = Mathf.Min(r.width * 0.5f, r.height) - 12f;
                // outline + track
                Band(p, c, rad, 0f, 100f, UIPalette.A(UIPalette.Outline, 0.5f), 16f);
                Band(p, c, rad, 0f, gMin, new Color(1f, 1f, 1f, 0.35f), 11f);
                Band(p, c, rad, gMin, gMax, UIPalette.Leaf, 11f);
                Band(p, c, rad, gMax, yMax, UIPalette.Sun, 11f);
                Band(p, c, rad, yMax, 100f, UIPalette.Danger, 11f);
                // needle
                float a = Ang(tension) * Mathf.Deg2Rad;
                var tip = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (rad + 6f);
                Color needle = snap > 0.01f ? Color.Lerp(Color.white, UIPalette.Danger, Mathf.Clamp01(snap * 2f)) : Color.white;
                p.strokeColor = UIPalette.A(UIPalette.Outline, 0.8f);
                p.lineWidth = 7f;
                p.lineCap = LineCap.Round;
                p.BeginPath(); p.MoveTo(c); p.LineTo(tip); p.Stroke();
                p.strokeColor = needle;
                p.lineWidth = 4f;
                p.BeginPath(); p.MoveTo(c); p.LineTo(tip); p.Stroke();
                p.fillColor = needle;
                p.BeginPath(); p.Arc(c, 7f, Angle.Degrees(0f), Angle.Degrees(360f)); p.ClosePath(); p.Fill();
            }
        }
    }
}
