using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Widgets shared by the main menu, the pause menu and the phone settings app.
    public static class UIWidgets
    {
        public struct ControlEntry
        {
            public string[] keys;
            public string action;
        }

        public static UISegmented LanguageSelector(float fontSize = 15f)
        {
            int sel = Loc.Current == Language.German ? 0 : 1;
            return new UISegmented(new[] { "Deutsch", "English" }, sel,
                i => Loc.SetLanguage(i == 0 ? Language.German : Language.English), UIPalette.Lagoon, fontSize);
        }

        /// Mouse sensitivity slider writing GameManager.I.State.mouseSensitivity, with a live value label.
        public static VisualElement SensitivityRow()
        {
            var col = UIKit.Col();
            var head = UIKit.Row();
            head.Add(UIKit.Text(Loc.Tr("Mausempfindlichkeit", "Mouse sensitivity"), 16f, UIPalette.TextDim, true));
            head.Add(UIKit.Flex());
            float current = GameManager.I != null ? GameManager.I.State.mouseSensitivity : 1f;
            var value = UIKit.Text(UIKit.Mult(current), 16f, UIPalette.Text, true);
            head.Add(value);
            col.Add(head);
            var slider = new UISlider(0.2f, 3f, current, v =>
            {
                v = Mathf.Round(v * 20f) / 20f;
                if (GameManager.I != null) GameManager.I.State.mouseSensitivity = v;
                value.text = UIKit.Mult(v);
            }, UIPalette.Lagoon);
            slider.style.marginTop = 6f;
            col.Add(slider);
            return col;
        }

        public static List<ControlEntry> ControlsList()
        {
            return new List<ControlEntry>
            {
                new ControlEntry { keys = new[] { "W", "A", "S", "D" }, action = Loc.Tr("Laufen / Schwimmen", "Walk / swim") },
                new ControlEntry { keys = new[] { Loc.Tr("Maus", "Mouse") }, action = Loc.Tr("Umsehen", "Look around") },
                new ControlEntry { keys = new[] { Loc.Tr("Leertaste", "Space") }, action = Loc.Tr("Springen / Auftauchen", "Jump / swim up") },
                new ControlEntry { keys = new[] { Loc.Tr("Strg", "Ctrl"), "C" }, action = Loc.Tr("Abtauchen", "Dive down") },
                new ControlEntry { keys = new[] { "Shift" }, action = Loc.Tr("Sprinten / schneller schwimmen", "Sprint / swim faster") },
                new ControlEntry { keys = new[] { "E" }, action = Loc.Tr("Interagieren (Boot, Bett, Kisten, Angeln)", "Interact (boat, bed, crates, fishing)") },
                new ControlEntry { keys = new[] { "F" }, action = Loc.Tr("Taschenlampe", "Flashlight") },
                new ControlEntry { keys = new[] { "Tab", "P" }, action = Loc.Tr("Handy (ClamOS)", "Phone (ClamOS)") },
                new ControlEntry { keys = new[] { "J" }, action = Loc.Tr("Sealog-Tagebuch", "Sealog journal") },
                new ControlEntry { keys = new[] { "Esc" }, action = Loc.Tr("Pause / Zurück", "Pause / back") },
                new ControlEntry { keys = new[] { Loc.Tr("Rechte Maustaste", "Right mouse") }, action = Loc.Tr("Kamera zielen", "Aim camera") },
                new ControlEntry { keys = new[] { Loc.Tr("Linke Maustaste", "Left mouse") }, action = Loc.Tr("Aufnahme starten / stoppen", "Start / stop recording") },
                new ControlEntry { keys = new[] { Loc.Tr("Mausrad", "Mouse wheel") }, action = Loc.Tr("Zoom", "Zoom") },
                new ControlEntry { keys = new[] { "Q" }, action = Loc.Tr("Anker werfen (Boot)", "Drop anchor (boat)") },
            };
        }

        /// Species icon element (discovered: colored icon, undiscovered: black silhouette).
        public static VisualElement SpeciesIcon(SpeciesDef s, bool discovered, float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Size(e, w, h);
            UIKit.Image(e, UICreatureIcons.Get(s));
            e.style.unityBackgroundImageTintColor = discovered ? Color.white : new Color(0.02f, 0.04f, 0.07f, 0.92f);
            return e;
        }

        /// Colored size-tier badge ("Riesig").
        public static Label TierBadge(SizeTier t, float size = 11f)
        {
            var c = Progression.TierColor(t);
            return UIKit.Pill(Loc.TierName(t), c, UIPalette.Deep, size);
        }

        /// Value/label tile used in stats rows.
        public static VisualElement StatTile(string value, string label, Color valueColor)
        {
            var tile = UIKit.Col();
            tile.style.flexGrow = 1;
            tile.style.flexBasis = 0;
            tile.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
            UIKit.Radius(tile, 12f);
            UIKit.Pad(tile, 10f, 12f, 10f, 12f);
            var v = UIKit.Text(value, 19f, valueColor, true);
            tile.Add(v);
            var l = UIKit.Text(label, 11.5f, UIPalette.TextFaint);
            l.style.marginTop = 2f;
            tile.Add(l);
            return tile;
        }

        public static VisualElement SectionTitle(string text, Color? color = null)
        {
            var l = UIKit.Caption(text, color ?? UIPalette.TextFaint, 12f);
            l.style.marginTop = 16f;
            l.style.marginBottom = 8f;
            return l;
        }

        public static VisualElement Notice(string text, Color accent, UIGlyph.DrawFn icon = null)
        {
            var row = UIKit.Row();
            row.style.alignItems = Align.FlexStart;
            row.style.backgroundColor = UIPalette.A(accent, 0.13f);
            UIKit.Radius(row, 12f);
            UIKit.Pad(row, 10f, 12f, 10f, 12f);
            row.style.marginBottom = 8f;
            if (icon != null)
            {
                var g = new UIGlyph(icon, accent, 18f);
                g.style.marginRight = 8f;
                g.style.marginTop = 1f;
                row.Add(g);
            }
            var t = UIKit.Para(text, 13.5f, accent);
            t.style.flexShrink = 1;
            t.style.flexGrow = 1;
            row.Add(t);
            return row;
        }

        /// Key/value line ("Tiefe   1–12 m").
        public static VisualElement InfoLine(string key, string value, Color? valueColor = null)
        {
            var row = UIKit.Row();
            row.style.paddingTop = 6f; row.style.paddingBottom = 6f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = UIPalette.Line;
            var k = UIKit.Text(key, 13.5f, UIPalette.TextFaint);
            k.style.width = 130f;
            row.Add(k);
            var v = UIKit.Text(value, 14f, valueColor ?? UIPalette.Text, true);
            v.style.flexShrink = 1;
            v.style.whiteSpace = WhiteSpace.Normal;
            row.Add(v);
            return row;
        }
    }
}
