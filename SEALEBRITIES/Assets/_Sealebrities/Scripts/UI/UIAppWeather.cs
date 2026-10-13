using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Weather app (Q36, round 3 GDD §19 „Wetter-App: zusätzlich Temperaturen“): today's weather (Weather – seeded per
    /// save and day, the same for the whole co-op crew) with the AIR temperature now, the WATER temperature at the
    /// surface / 20 m / 40 m, the next hours (06–21 h every 3 h) and tomorrow.
    public class UIAppWeather : UIPhoneApp
    {
        public override string Id => "weather";
        public override string Title => Loc.Tr("Wetter", "Weather");
        public override Color Color => UIPalette.Sky;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Weather;

        float refresh;

        static UIGlyph.DrawFn KindGlyph(WeatherKind k, bool night)
        {
            if (night && (k == WeatherKind.Sunny || k == WeatherKind.Fair)) return UIGlyphs.Moon;
            switch (k)
            {
                case WeatherKind.Sunny: return UIGlyphs.Sun;
                case WeatherKind.Fair: return UIGlyphs.Weather;
                case WeatherKind.Breezy: return UIGlyphs.Wave;
                default: return UIGlyphs.Cloud;
            }
        }

        static Color KindColor(WeatherKind k, bool night)
        {
            if (night) return UIPalette.Violet;
            switch (k)
            {
                case WeatherKind.Sunny: return UIPalette.SunInk;
                case WeatherKind.Fair: return UIPalette.Sun;
                case WeatherKind.Breezy: return UIPalette.TurquoiseInk;
                default: return UIPalette.Silhouette;
            }
        }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;
            bool night = gm.IsNight;
            var k = Weather.Today;

            var hero = UIKit.Col();
            hero.style.alignItems = Align.Center;
            hero.style.marginTop = 10f;
            hero.Add(new UIGlyph(KindGlyph(k, night), KindColor(k, night), 150f));
            var t = UIKit.Text(Weather.Format(Weather.AirTempNow), 64f, UIPalette.Ink, true);
            hero.Add(t);
            var kind = UIKit.Text(night ? Loc.Tr("Nacht · ", "Night · ") + Weather.Name(k) : Weather.Name(k), PhoneKit.Title, UIPalette.InkSoft, true);
            hero.Add(kind);
            body.Add(hero);

            // air + water temperatures
            var card = PhoneKit.Card();
            card.style.marginTop = 14f;
            card.Add(Line(UIGlyphs.Thermo, UIPalette.Coral, Loc.Tr("Luft", "Air"), Weather.Format(Weather.AirTempNow)));
            card.Add(Line(UIGlyphs.Wave, UIPalette.TurquoiseInk, Loc.Tr("Wasser", "Water"), Weather.Format(Weather.WaterTempNow)));
            card.Add(Line(UIGlyphs.Depth, UIPalette.Sky, Loc.Tr("Wasser in 20 m", "Water at 20 m"), Weather.Format(Weather.WaterTemp(20f, s.timeOfDay))));
            card.Add(Line(UIGlyphs.Depth, UIPalette.Violet, Loc.Tr("Wasser in 40 m", "Water at 40 m"), Weather.Format(Weather.WaterTemp(40f, s.timeOfDay))));
            card.Add(Line(UIGlyphs.Wave, UIPalette.InkSoft, Loc.Tr("See", "Sea"), Weather.Sea(k)));
            body.Add(card);

            // the next hours
            body.Add(PhoneKit.Section(Loc.Tr("Heute", "Today"), UIGlyphs.Clock));
            var hours = UIKit.Row();
            hours.style.justifyContent = Justify.SpaceBetween;
            for (int h = 6; h <= 21; h += 3)
            {
                float tod = h / 24f;
                var col = UIKit.Col();
                col.style.alignItems = Align.Center;
                bool past = s.timeOfDay > tod + 0.02f;
                col.style.opacity = past ? 0.45f : 1f;
                col.Add(UIKit.Text(h.ToString("00") + ":00", PhoneKit.Tiny, UIPalette.InkSoft, true));
                col.Add(new UIGlyph(KindGlyph(k, h >= 21), KindColor(k, h >= 21), 40f));
                col.Add(UIKit.Text(Weather.Format(Weather.AirTemp(s.day, tod)), PhoneKit.Small, UIPalette.Ink, true));
                hours.Add(col);
            }
            body.Add(hours);

            // tomorrow
            var tk = Weather.Tomorrow;
            body.Add(PhoneKit.Section(Loc.Tr("Morgen", "Tomorrow"), UIGlyphs.Weather));
            var tm = UIKit.Row();
            tm.Add(new UIGlyph(KindGlyph(tk, false), KindColor(tk, false), 54f));
            var tl = UIKit.Col();
            tl.style.marginLeft = 12f;
            tl.Add(UIKit.Text(Weather.Name(tk), PhoneKit.Body, UIPalette.Ink, true));
            tl.Add(UIKit.Text(Weather.Format(Weather.AirTemp(s.day + 1, 7f / 24f)) + " – " + Weather.Format(Weather.AirTemp(s.day + 1, 14f / 24f)) + " · " + Weather.Sea(tk), PhoneKit.Small, UIPalette.InkSoft, true));
            tm.Add(tl);
            body.Add(tm);
        }

        static VisualElement Line(UIGlyph.DrawFn icon, Color c, string label, string value)
        {
            var row = UIKit.Row();
            row.style.marginBottom = 8f;
            row.Add(new UIGlyph(icon, c, 30f));
            var l = UIKit.Text(label, PhoneKit.Body, UIPalette.InkSoft, true);
            l.style.marginLeft = 10f;
            l.style.flexGrow = 1;
            row.Add(l);
            row.Add(UIKit.Text(value, PhoneKit.Body, UIPalette.Ink, true));
            return row;
        }

        public override void Tick(float dt)
        {
            // the hero temperature follows the clock (once per in-game ~15 min)
            refresh += dt;
            if (refresh < 20f) return;
            refresh = 0f;
            Phone?.MarkDirty();
        }
    }
}
