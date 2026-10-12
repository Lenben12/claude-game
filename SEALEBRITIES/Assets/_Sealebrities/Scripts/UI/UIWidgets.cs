using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Widgets shared by the menus, the phone apps and the inventory (light card theme).
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
            return new UISegmented(new[] { "Deutsch", "English" }, sel, i =>
            {
                Settings.Data.language = i == 0 ? Language.German : Language.English;
                Settings.Apply();
                Settings.Save();
            }, UIPalette.Turquoise, fontSize);
        }

        /// Mouse sensitivity slider writing Settings.Data.mouseSensitivity, with a live value label.
        public static VisualElement SensitivityRow(float fontSize = 16f)
        {
            var col = UIKit.Col();
            var head = UIKit.Row();
            head.Add(UIKit.Text(Loc.Tr("Mausempfindlichkeit", "Mouse sensitivity"), fontSize, UIPalette.InkSoft, true));
            head.Add(UIKit.Flex());
            const float Min = 0.2f, Max = 3f;
            float current = UIFormat.Clamp(Settings.Data.mouseSensitivity, Min, Max, Settings.DefaultSensitivity);
            var value = UIKit.Text(UIFormat.Multiplier(Mathf.Round(current * 20f) / 20f, Min, Max), fontSize, UIPalette.Ink, true);
            head.Add(value);
            col.Add(head);
            var slider = new UISlider(Min, Max, current, v =>
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) return;
                v = Mathf.Clamp(Mathf.Round(v * 20f) / 20f, Min, Max);
                Settings.Data.mouseSensitivity = v;
                Settings.MarkDirty();
                value.text = UIFormat.Multiplier(v, Min, Max);
            }, UIPalette.Turquoise, _ => Settings.Save(), fontSize * 2f);
            slider.style.marginTop = 6f;
            col.Add(slider);
            return col;
        }

        /// Short controls overview built from the current key bindings.
        public static List<ControlEntry> ControlsList()
        {
            string L(GameAction a) => GameInput.BindingLabel(a);
            return new List<ControlEntry>
            {
                new ControlEntry { keys = new[] { L(GameAction.Forward), L(GameAction.Left), L(GameAction.Back), L(GameAction.Right) }, action = Loc.Tr("Laufen / Schwimmen", "Walk / swim") },
                new ControlEntry { keys = new[] { L(GameAction.Jump) }, action = Loc.Tr("Springen / Auftauchen", "Jump / swim up") },
                new ControlEntry { keys = new[] { L(GameAction.Down) }, action = Loc.Tr("Abtauchen / Ducken (halten)", "Dive down / crouch (hold)") },
                new ControlEntry { keys = new[] { L(GameAction.Forward), L(GameAction.Back) }, action = Loc.Tr("Leiter hoch / runter · E loslassen", "Ladder up / down · E let go") },
                new ControlEntry { keys = new[] { L(GameAction.Sprint) }, action = Loc.Tr("Sprinten", "Sprint") },
                new ControlEntry { keys = new[] { L(GameAction.Interact) }, action = Loc.Tr("Benutzen", "Interact") },
                new ControlEntry { keys = new[] { L(GameInput.SlotAction(0)) + "–" + L(GameInput.SlotAction(Hands.SlotCount - 1)) }, action = Loc.Tr("Leiste (Mausrad: Haupthand)", "Hotbar (wheel: main hand)") },
                new ControlEntry { keys = new[] { L(GameAction.Use) }, action = Loc.Tr("Benutzen: filmen, auswerfen, schlagen, essen", "Use: record, cast, strike, eat") },
                new ControlEntry { keys = new[] { L(GameAction.Aim) }, action = Loc.Tr("Zielen / mit Waffe abwehren", "Aim / block with a weapon") },
                new ControlEntry { keys = new[] { L(GameAction.Photo) }, action = Loc.Tr("Foto fürs Sealog", "Photo for the Sealog") },
                new ControlEntry { keys = new[] { L(GameAction.Selfie) }, action = Loc.Tr("Selfie-Ansicht", "Selfie view") },
                new ControlEntry { keys = new[] { L(GameAction.LightToggle) }, action = Loc.Tr("Lampe an/aus", "Lamp on/off") },
                new ControlEntry { keys = new[] { L(GameAction.Phone) }, action = Loc.Tr("Handy", "Phone") },
                new ControlEntry { keys = new[] { L(GameAction.Inventory) }, action = Loc.Tr("Rucksack", "Backpack") },
                new ControlEntry { keys = new[] { L(GameAction.Journal) }, action = "Sealog" },
                new ControlEntry { keys = new[] { L(GameAction.Anchor) }, action = Loc.Tr("Anker", "Anchor") },
                new ControlEntry { keys = new[] { L(GameAction.PushToTalk) }, action = Loc.Tr("Sprechen", "Talk") },
            };
        }

        /// Species icon element (discovered: colored icon, undiscovered: flat grey silhouette #8C96A3).
        public static VisualElement SpeciesIcon(SpeciesDef s, bool discovered, float w, float h)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Size(e, w, h);
            if (discovered)
            {
                UIKit.Image(e, UICreatureIcons.Get(s));
                return e;
            }
            var sil = UICreatureIcons.Silhouette(s);
            if (sil != null) UIKit.Image(e, sil);
            else
            {
                // texture not readable: darken the colored icon instead
                UIKit.Image(e, UICreatureIcons.Get(s));
                e.style.unityBackgroundImageTintColor = new Color(0.30f, 0.33f, 0.38f, 0.6f);
            }
            return e;
        }

        /// Colored size-tier badge ("Riesig").
        public static Label TierBadge(SizeTier t, float size = 11f)
        {
            var c = Progression.TierColor(t);
            if (t == SizeTier.Normal) c = UIPalette.CardAlt;
            return UIKit.Pill(Loc.TierName(t), c, UIPalette.Ink, size);
        }

        /// Value/label tile used in stats rows (white tile on a light card).
        public static VisualElement StatTile(string value, string label, Color valueColor, float size = 19f)
        {
            var tile = UIKit.Col();
            tile.style.flexGrow = 1;
            tile.style.flexBasis = 0;
            tile.style.backgroundColor = Color.white;
            UIKit.Radius(tile, 14f);
            UIKit.Pad(tile, size * 0.5f, size * 0.6f, size * 0.5f, size * 0.6f);
            var v = UIKit.Text(value, size, valueColor, true);
            tile.Add(v);
            var l = UIKit.Text(label, size * 0.62f, UIPalette.InkSoft);
            l.style.marginTop = 2f;
            l.style.whiteSpace = WhiteSpace.Normal;
            tile.Add(l);
            return tile;
        }

        /// Icon + big value (no label) – "less text, more icons".
        public static VisualElement IconStat(UIGlyph.DrawFn icon, Color iconColor, string value, Color valueColor, float size = 24f)
        {
            var row = UIKit.Row();
            row.style.flexGrow = 1;
            row.style.flexBasis = 0;
            row.style.backgroundColor = Color.white;
            UIKit.Radius(row, 16f);
            UIKit.Pad(row, size * 0.45f, size * 0.55f, size * 0.45f, size * 0.45f);
            var g = new UIGlyph(icon, iconColor, size * 1.15f);
            g.style.marginRight = size * 0.35f;
            row.Add(g);
            var v = UIKit.Text(value, size, valueColor, true);
            v.style.flexShrink = 1;
            row.Add(v);
            return row;
        }

        public static VisualElement SectionTitle(string text, Color? color = null, float size = 12f)
        {
            var l = UIKit.Caption(text, color ?? UIPalette.InkSoft, size);
            l.style.marginTop = size * 1.4f;
            l.style.marginBottom = size * 0.7f;
            return l;
        }

        public static VisualElement Notice(string text, Color accent, UIGlyph.DrawFn icon = null, float size = 13.5f)
        {
            var row = UIKit.Row();
            row.style.alignItems = Align.FlexStart;
            row.style.backgroundColor = UIPalette.A(accent, 0.16f);
            UIKit.Radius(row, 14f);
            UIKit.Pad(row, size * 0.75f, size * 0.9f, size * 0.75f, size * 0.9f);
            row.style.marginBottom = 8f;
            Color ink = Ink(accent);
            if (icon != null)
            {
                var g = new UIGlyph(icon, ink, size * 1.35f);
                g.style.marginRight = size * 0.6f;
                row.Add(g);
            }
            var t = UIKit.Para(text, size, ink);
            t.style.flexShrink = 1;
            t.style.flexGrow = 1;
            row.Add(t);
            return row;
        }

        /// Readable text version of an accent color on light cards.
        public static Color Ink(Color accent)
        {
            float lum = accent.r * 0.299f + accent.g * 0.587f + accent.b * 0.114f;
            return lum > 0.5f ? UIPalette.Darken(accent, 0.45f) : accent;
        }

        /// Key/value line ("Tiefe   1–12 m").
        public static VisualElement InfoLine(string key, string value, Color? valueColor = null, float size = 14f)
        {
            var row = UIKit.Row();
            row.style.paddingTop = size * 0.45f; row.style.paddingBottom = size * 0.45f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = UIPalette.Line;
            var k = UIKit.Text(key, size * 0.95f, UIPalette.InkSoft);
            k.style.width = size * 9.5f;
            row.Add(k);
            var v = UIKit.Text(value, size, valueColor ?? UIPalette.Ink, true);
            v.style.flexShrink = 1;
            v.style.whiteSpace = WhiteSpace.Normal;
            row.Add(v);
            return row;
        }

        /// Colored rounded-square app/category icon with a white glyph.
        public static VisualElement IconTile(UIGlyph.DrawFn icon, Color color, float size)
        {
            var tile = UIKit.Box();
            UIKit.Size(tile, size, size);
            UIKit.Radius(tile, size * 0.24f);
            tile.style.backgroundColor = color;
            UIKit.Center(tile);
            tile.style.borderBottomWidth = Mathf.Max(2f, size * 0.05f);
            tile.style.borderBottomColor = UIPalette.Darken(color, 0.18f);
            tile.Add(new UIGlyph(icon, UIPalette.OnColor(color) == UIPalette.Ink ? UIPalette.Ink : Color.white, size * 0.58f));
            tile.pickingMode = PickingMode.Ignore;
            return tile;
        }

        /// Light modal card used by menu overlays (settings, difficulty, inventory).
        public static VisualElement ModalCard(float width)
        {
            var card = UIKit.Card(UIPalette.A(UIPalette.Card, 0.97f), 28f, 30f);
            card.style.width = width;
            card.style.maxWidth = Length.Percent(94f);
            card.style.maxHeight = Length.Percent(92f);
            UIKit.Lift(card, 1.4f);
            return card;
        }

        /// Big header row: icon tile + title (+ optional close button on the right).
        public static VisualElement Header(UIGlyph.DrawFn icon, Color color, string title, System.Action onClose, float size = 34f)
        {
            var row = UIKit.Row();
            row.style.marginBottom = size * 0.5f;
            if (icon != null)
            {
                var t = IconTile(icon, color, size * 1.4f);
                t.style.marginRight = size * 0.45f;
                row.Add(t);
            }
            var l = UIKit.Text(title, size, UIPalette.Ink, true);
            l.style.flexShrink = 1;
            row.Add(l);
            row.Add(UIKit.Flex());
            if (onClose != null) row.Add(RoundIconButton(UIGlyphs.Cross, onClose, size * 1.2f));
            return row;
        }

        /// Round light button with a glyph (close, back, delete …).
        public static VisualElement RoundIconButton(UIGlyph.DrawFn icon, System.Action onClick, float size, Color? iconColor = null)
        {
            var b = UIKit.Box();
            UIKit.Size(b, size, size);
            UIKit.Radius(b, size * 0.5f);
            UIKit.Center(b);
            b.Add(new UIGlyph(icon, iconColor ?? UIPalette.Ink, size * 0.5f));
            UIKit.Clickable(b, onClick, UIPalette.A(UIPalette.Ink, 0.07f), UIPalette.A(UIPalette.Ink, 0.15f));
            return b;
        }
    }
}
