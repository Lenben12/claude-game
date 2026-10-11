using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Self-contained look of the co-op screens (art bible §1.14: light frosted cards, Fredoka, Ink text, turquoise /
    /// coral / sun accents, no navy panels). Kept inside the Net module so UI-module refactors cannot break it.
    static class NetLobbyStyle
    {
        public static readonly Color Ink = Hex(0x1B2433);
        public static readonly Color Muted = Hex(0x5A6478);
        public static readonly Color Card = new Color(0.965f, 0.945f, 0.902f, 0.97f);   // #F6F1E6
        public static readonly Color Sand = Hex(0xF6E7C8);
        public static readonly Color White = Color.white;
        public static readonly Color Turquoise = Hex(0x3FC1C9);
        public static readonly Color TurquoiseDark = Hex(0x2A9AA2);
        public static readonly Color Coral = Hex(0xFF7A5C);
        public static readonly Color Sun = Hex(0xF2C94C);
        public static readonly Color Leaf = Hex(0x4E9A2A);
        public static readonly Color Danger = Hex(0xE5484D);
        public static readonly Color Line = new Color(0.106f, 0.141f, 0.2f, 0.12f);
        public static readonly Color Backdrop = new Color(0.04f, 0.16f, 0.24f, 0.45f);

        static Font font;
        static bool fontTried;

        public static Color Hex(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);

        public static Color Lighten(Color c, float t)
        {
            var r = Color.Lerp(c, Color.white, t);
            r.a = c.a;
            return r;
        }

        /// Fredoka (Resources/Fonts/Fredoka-Variable) on an element; children inherit it.
        public static void ApplyFont(VisualElement e)
        {
            if (!fontTried)
            {
                fontTried = true;
                try { font = Resources.Load<Font>("Fonts/Fredoka-Variable"); } catch (Exception) { font = null; }
            }
            if (font != null) e.style.unityFontDefinition = FontDefinition.FromFont(font);
        }

        public static VisualElement Row()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            return e;
        }

        public static VisualElement Col()
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Column;
            return e;
        }

        public static VisualElement Flex()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.flexGrow = 1;
            return e;
        }

        public static VisualElement Gap(float px, bool horizontal = false)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            if (horizontal) e.style.width = px; else e.style.height = px;
            e.style.flexShrink = 0;
            return e;
        }

        public static Label Text(string text, float size, Color color, bool bold = false)
        {
            var l = new Label(text ?? "");
            l.style.fontSize = size;
            l.style.color = color;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static Label Para(string text, float size, Color color)
        {
            var l = Text(text, size, color);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        public static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        public static void Border(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = w; e.style.borderRightWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w;
            e.style.borderTopColor = c; e.style.borderRightColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c;
        }

        public static void Pad(VisualElement e, float v, float h)
        {
            e.style.paddingTop = v; e.style.paddingBottom = v; e.style.paddingLeft = h; e.style.paddingRight = h;
        }

        public static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        }

        public static void Outline(Label l, float width = 1.5f)
        {
            l.style.unityTextOutlineWidth = width;
            l.style.unityTextOutlineColor = new Color(0.078f, 0.094f, 0.157f, 0.75f);
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 4f, color = new Color(0f, 0f, 0f, 0.35f) };
        }

        public static VisualElement CardBox(float radius = 24f, float padding = 28f)
        {
            var c = Col();
            c.style.backgroundColor = Card;
            Radius(c, radius);
            Pad(c, padding, padding + 4f);
            return c;
        }

        /// Rounded button with hover/press feedback.
        public static Button Btn(string text, Action onClick, Color bg, Color fg, float size = 20f)
        {
            var b = new Button();
            b.text = text;
            b.focusable = false;
            if (onClick != null) b.clicked += onClick;
            b.style.fontSize = size;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.whiteSpace = WhiteSpace.Normal;
            b.style.color = fg;
            b.style.backgroundColor = bg;
            Border(b, 0f, Color.clear);
            Radius(b, 14f);
            Pad(b, 10f, 22f);
            b.style.marginLeft = 0; b.style.marginRight = 0; b.style.marginTop = 0; b.style.marginBottom = 0;
            Color hover = Lighten(bg, 0.14f);
            b.RegisterCallback<PointerEnterEvent>(_ => { if (b.enabledInHierarchy) b.style.backgroundColor = hover; });
            b.RegisterCallback<PointerLeaveEvent>(_ => { b.style.backgroundColor = bg; b.style.scale = new Scale(Vector2.one); });
            b.RegisterCallback<PointerDownEvent>(_ => { if (b.enabledInHierarchy) b.style.scale = new Scale(new Vector2(0.97f, 0.97f)); }, TrickleDown.TrickleDown);
            b.RegisterCallback<PointerUpEvent>(_ => b.style.scale = new Scale(Vector2.one));
            return b;
        }

        public static Button Primary(string text, Action onClick, float size = 22f) => Btn(text, onClick, Coral, White, size);
        public static Button Accent(string text, Action onClick, float size = 20f) => Btn(text, onClick, Turquoise, White, size);

        public static Button Ghost(string text, Action onClick, float size = 18f)
        {
            var b = Btn(text, onClick, new Color(0.106f, 0.141f, 0.2f, 0.06f), Ink, size);
            return b;
        }

        public static void Enable(Button b, bool on)
        {
            b.SetEnabled(on);
            b.style.opacity = on ? 1f : 0.45f;
        }

        /// A clickable box (card, tab, swatch) with hover tint.
        public static VisualElement Clickable(VisualElement e, Action onClick, Color normal, Color hover)
        {
            e.style.backgroundColor = normal;
            e.pickingMode = PickingMode.Position;
            e.RegisterCallback<PointerEnterEvent>(_ => e.style.backgroundColor = hover);
            e.RegisterCallback<PointerLeaveEvent>(_ => e.style.backgroundColor = normal);
            e.RegisterCallback<ClickEvent>(_ => onClick?.Invoke());
            return e;
        }

        /// Light text field (white, rounded, Ink text).
        public static TextField Field(string value, int maxLength, float width, float size = 20f)
        {
            var tf = new TextField { value = value ?? "" };
            tf.maxLength = maxLength;
            tf.style.width = width;
            tf.style.marginLeft = 0; tf.style.marginRight = 0; tf.style.marginTop = 0; tf.style.marginBottom = 0;
            tf.style.fontSize = size;
            tf.style.color = Ink;
            var input = tf.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = White;
                Border(input, 2f, Line);
                Radius(input, 12f);
                input.style.color = Ink;
                input.style.fontSize = size;
                input.style.minHeight = size + 22f;
                Pad(input, 6f, 12f);
            }
            return tf;
        }

        public static VisualElement Dot(Color c, float size)
        {
            var d = new VisualElement { pickingMode = PickingMode.Ignore };
            d.style.width = size; d.style.height = size; d.style.flexShrink = 0;
            Radius(d, size * 0.5f);
            d.style.backgroundColor = c;
            return d;
        }

        /// Small rounded label ("2/4", "VPN" …).
        public static Label Chip(string text, Color bg, Color fg, float size = 15f)
        {
            var l = Text(text, size, fg, true);
            l.style.backgroundColor = bg;
            Radius(l, 999f);
            Pad(l, 3f, 10f);
            l.style.flexShrink = 0;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            return l;
        }
    }
}
