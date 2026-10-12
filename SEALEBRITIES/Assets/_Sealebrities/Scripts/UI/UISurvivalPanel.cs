using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 survival HUD (GDD §19 „Hunger und Durst … kleine HUD-Symbole“): drumstick + water drop with a short
    /// meter each (white → orange below 25 → red pulsing below 10) in the top-left column, and a heart while the diver
    /// is hurt (Combat.Health < 100). Allocation free per frame: values are drawn only when the shown step changes.
    public class UISurvivalPanel
    {
        const float BarW = 64f, BarH = 7f;

        public readonly VisualElement Root;
        readonly Meter hunger, thirst, health;
        bool dirty = true, healthShown;

        sealed class Meter
        {
            public VisualElement row, fill;
            public UIHudGlyph icon;
            public int key = -1;
            public int colorKey = -1;
        }

        public UISurvivalPanel()
        {
            Root = UIKit.Row();
            Root.pickingMode = PickingMode.Ignore;
            Root.style.marginTop = 10f;
            hunger = MakeMeter(UIGlyphs.Hunger);
            thirst = MakeMeter(UIGlyphs.Thirst);
            health = MakeMeter(UIGlyphs.Heart);
            health.row.style.display = DisplayStyle.None;
            UIKit.IgnorePicking(Root);
            Survival.Changed += MarkDirty;
        }

        public void Dispose() { Survival.Changed -= MarkDirty; }

        void MarkDirty() => dirty = true;

        Meter MakeMeter(UIGlyph.DrawFn icon)
        {
            var m = new Meter();
            m.row = UIKit.Row();
            m.row.style.marginRight = 14f;
            m.row.style.alignItems = Align.Center;
            m.icon = new UIHudGlyph(icon, Color.white, 24f);
            m.row.Add(m.icon);
            var track = UIKit.Box();
            UIKit.Size(track, BarW, BarH);
            track.style.marginLeft = 6f;
            UIKit.Radius(track, BarH * 0.5f);
            track.style.backgroundColor = new Color(0.06f, 0.08f, 0.14f, 0.45f);
            UIKit.Border(track, 1f, new Color(1f, 1f, 1f, 0.35f));
            m.fill = UIKit.Box();
            m.fill.style.height = BarH - 2f;
            UIKit.Radius(m.fill, (BarH - 2f) * 0.5f);
            m.fill.style.backgroundColor = Color.white;
            track.Add(m.fill);
            m.row.Add(track);
            Root.Add(m.row);
            return m;
        }

        static void Set(Meter m, float v01, bool warnLow, float time)
        {
            v01 = float.IsNaN(v01) ? 1f : Mathf.Clamp01(v01);
            int key = Mathf.RoundToInt(v01 * 64f);
            if (key != m.key)
            {
                m.key = key;
                m.fill.style.width = Length.Percent(v01 * 100f);
            }
            int ck = v01 < Survival.VeryLow / Survival.Max ? 2 : v01 < Survival.Low / Survival.Max ? 1 : 0;
            if (!warnLow) ck = v01 < 0.35f ? 2 : v01 < 0.7f ? 1 : 0;
            if (ck != m.colorKey)
            {
                m.colorKey = ck;
                Color c = ck == 2 ? UIPalette.Danger : ck == 1 ? UIPalette.Warn : Color.white;
                m.fill.style.backgroundColor = c;
                m.icon.SetColor(ck == 0 ? Color.white : c);
            }
            // very low: the icon pulses
            m.icon.style.opacity = ck == 2 ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(time * 4f)) : 1f;
        }

        public void Tick()
        {
            float t = Time.unscaledTime;
            if (dirty || hunger.colorKey == 2 || thirst.colorKey == 2)
            {
                dirty = false;
                Set(hunger, Survival.Hunger01, true, t);
                Set(thirst, Survival.Thirst01, true, t);
            }
            bool hurt = Combat.Health < Combat.MaxHealth - 0.5f;
            if (hurt != healthShown) { healthShown = hurt; UIKit.Show(health.row, hurt); }
            if (hurt) Set(health, Combat.Health01, false, t);
        }
    }
}
