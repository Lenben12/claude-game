using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Weather app placeholder (Q36): today's sky from the clock, „Bald: Sturmwarnungen und Seegang.“
    public class UIAppWeather : UIPhoneApp
    {
        public override string Id => "weather";
        public override string Title => Loc.Tr("Wetter", "Weather");
        public override Color Color => UIPalette.Sky;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Weather;

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            bool night = gm != null && gm.IsNight;
            var hero = UIKit.Col();
            hero.style.alignItems = Align.Center;
            hero.style.marginTop = 20f;
            hero.Add(new UIGlyph(night ? UIGlyphs.Moon : UIGlyphs.Weather, night ? UIPalette.Violet : UIPalette.SunInk, 180f));
            var t = UIKit.Text(night ? Loc.Tr("Klare Nacht", "Clear night") : Loc.Tr("Sonnig, 28 °C", "Sunny, 28 °C"), PhoneKit.Title, UIPalette.Ink, true);
            t.style.marginTop = 10f;
            hero.Add(t);
            var w = UIKit.Row();
            w.style.marginTop = 10f;
            w.Add(new UIGlyph(UIGlyphs.Wave, UIPalette.TurquoiseInk, 30f));
            w.Add(UIKit.Text(" " + Loc.Tr("Ruhige See", "Calm sea"), PhoneKit.Body, UIPalette.InkSoft, true));
            hero.Add(w);
            body.Add(hero);
            var n = UIWidgets.Notice(Loc.Tr("Bald: Sturmwarnungen und Seegang.", "Coming soon: storm warnings and swell."), UIPalette.Sky, UIGlyphs.Clock, PhoneKit.Small);
            n.style.marginTop = 24f;
            body.Add(n);
        }
    }
}
