using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Food / drink buffs (E): max 1 food + 1 drink at a time, shown as 24 px icons with the minutes left in the top-left
    /// HUD column (under the dive block, also on land). Bonus only – there is no hunger meter. Allocation free per frame:
    /// labels change only when the shown minute changes.
    public class UIFoodPanel
    {
        const int Slots = 2;

        public readonly VisualElement Root;
        readonly VisualElement[] rows = new VisualElement[Slots];
        readonly UIHudGlyph[] icons = new UIHudGlyph[Slots];
        readonly UIHudGlyph[] effects = new UIHudGlyph[Slots];
        readonly Label[] times = new Label[Slots];
        readonly int[] shownKey = new int[Slots];
        bool dirty = true;

        public UIFoodPanel()
        {
            Root = UIKit.Row();
            Root.pickingMode = PickingMode.Ignore;
            Root.style.marginTop = 10f;
            for (int i = 0; i < Slots; i++)
            {
                var r = UIKit.Row();
                r.style.marginRight = 16f;
                icons[i] = new UIHudGlyph(UIGlyphs.Food, UIPalette.Sun, 26f);
                r.Add(icons[i]);
                effects[i] = new UIHudGlyph(UIGlyphs.Bubbles, Color.white, 18f);
                effects[i].style.marginLeft = -6f;
                effects[i].style.marginTop = 12f;
                r.Add(effects[i]);
                times[i] = UIKit.HudText("", 18f);
                times[i].style.marginLeft = 4f;
                r.Add(times[i]);
                r.style.display = DisplayStyle.None;
                Root.Add(r);
                rows[i] = r;
                shownKey[i] = -1;
            }
            UIKit.IgnorePicking(Root);
            Buffs.Changed += MarkDirty;
        }

        public void Dispose() { Buffs.Changed -= MarkDirty; }

        void MarkDirty() => dirty = true;

        static UIGlyph.DrawFn EffectGlyph(BuffType t)
        {
            switch (t)
            {
                case BuffType.AirCapacity: return UIGlyphs.Bubbles;
                case BuffType.SwimSpeed: return UIGlyphs.Fins;
                case BuffType.SteadyHands: return UIGlyphs.Camera;
                case BuffType.BumpResist: return UIGlyphs.SharkFin;
                default: return UIGlyphs.Eye;
            }
        }

        static Color EffectColor(BuffType t)
        {
            switch (t)
            {
                case BuffType.AirCapacity: return UIPalette.AirFull;
                case BuffType.SwimSpeed: return UIPalette.Turquoise;
                case BuffType.SteadyHands: return UIPalette.Sun;
                case BuffType.BumpResist: return UIPalette.Coral;
                default: return UIPalette.Violet;
            }
        }

        public void Tick()
        {
            var list = Buffs.Active;
            int n = list != null ? Mathf.Min(list.Count, Slots) : 0;
            for (int i = 0; i < Slots; i++)
            {
                bool on = i < n;
                UIKit.Show(rows[i], on);
                if (!on) { shownKey[i] = -1; continue; }
                var b = list[i];
                if (dirty)
                {
                    icons[i].SetDraw(b.kind == FoodKind.Drink ? UIGlyphs.Drink : UIGlyphs.Food);
                    icons[i].SetColor(b.kind == FoodKind.Drink ? UIPalette.Rgb(0xC9A27A) : UIPalette.Sun);
                    effects[i].SetDraw(EffectGlyph(b.type));
                    effects[i].SetColor(EffectColor(b.type));
                    shownKey[i] = -1;
                }
                float sec = float.IsNaN(b.secondsLeft) ? 0f : Mathf.Max(0f, b.secondsLeft);
                int key = sec >= 60f ? 1000 + Mathf.CeilToInt(sec / 60f) : Mathf.CeilToInt(sec);
                if (key != shownKey[i])
                {
                    shownKey[i] = key;
                    times[i].text = UIFormat.ShortDuration(sec);
                    times[i].style.color = sec < 60f ? UIPalette.Warn : Color.white;
                }
            }
            dirty = false;
        }
    }
}
