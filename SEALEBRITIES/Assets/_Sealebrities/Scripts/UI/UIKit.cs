using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Colors of the SEALEBRITIES interface (art bible §1.14): bright and sunny – white, sand, turquoise, coral – on
    /// light frosted cards. The HUD never uses boxes: white text/icons with a thin dark outline.
    /// The legacy names (Panel, Raised, Text …) are kept for older call sites and now describe the LIGHT theme
    /// (dark ink text on light cards).
    public static class UIPalette
    {
        // ---- HUD palette (floating text over the 3D world)
        public static readonly Color White = Color.white;
        public static readonly Color Sand = Rgb(0xF6E7C8);
        public static readonly Color Turquoise = Rgb(0x3FC1C9);
        public static readonly Color Coral = Rgb(0xFF7A5C);
        public static readonly Color Sun = Rgb(0xF2C94C);
        public static readonly Color Leaf = Rgb(0x9BE564);
        public static readonly Color Warn = Rgb(0xFFB347);
        public static readonly Color Danger = Rgb(0xFF5A5A);
        public static readonly Color AirFull = Rgb(0x6FE3D3);
        public static readonly Color DepthSub = Rgb(0xCFE8F0);
        /// Text outline color of every HUD label (rgba(20,24,40,0.75)).
        public static readonly Color Outline = new Color(20f / 255f, 24f / 255f, 40f / 255f, 0.75f);

        // ---- light surfaces (cards, phone, menus)
        public static readonly Color Ink = Rgb(0x1B2433);
        public static readonly Color InkSoft = Rgb(0x5A6478);
        public static readonly Color Silhouette = Rgb(0x8C96A3);
        public static readonly Color Card = Rgb(0xF6F1E6);
        public static readonly Color CardAlt = Rgb(0xE9EEF2);
        public static readonly Color Badge = Rgb(0xFF4D4D);
        /// Darker accent variants that stay readable as text on light cards.
        public static readonly Color TurquoiseInk = Rgb(0x1C8E96);
        public static readonly Color SunInk = Rgb(0xB8861B);
        public static readonly Color LeafInk = Rgb(0x2F9A4E);
        public static readonly Color CoralInk = Rgb(0xE0553A);

        // ---- legacy names (light theme)
        public static readonly Color Navy = Rgb(0x0D1B2A);
        public static readonly Color Panel = new Color(Card.r, Card.g, Card.b, 0.95f);
        public static readonly Color PanelSolid = Card;
        public static readonly Color Deep = Ink;
        public static readonly Color Raised = Color.white;
        public static readonly Color Raised2 = Rgb(0xEEF1F4);
        public static readonly Color Lagoon = Turquoise;
        public static readonly Color Live = Rgb(0xFF4D4D);
        public static readonly Color Money = LeafInk;
        public static readonly Color Sky = Rgb(0x4FA3E0);
        public static readonly Color Violet = Rgb(0x8E6CD9);
        public static readonly Color Text = Ink;
        public static readonly Color TextDim = InkSoft;
        public static readonly Color TextFaint = Silhouette;
        public static readonly Color Line = new Color(Ink.r, Ink.g, Ink.b, 0.10f);
        public static readonly Color Shadow = new Color(0f, 0f, 0f, 0.25f);

        public static Color Rgb(int hex) => new Color(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1f);
        public static Color A(Color c, float a) { c.a = a; return c; }
        public static Color Lighten(Color c, float t) { var r = Color.Lerp(c, Color.white, t); r.a = c.a; return r; }
        public static Color Darken(Color c, float t) { var r = Color.Lerp(c, Color.black, t); r.a = c.a; return r; }

        /// Readable text color on top of a colored background.
        public static Color OnColor(Color bg) => (bg.r * 0.299f + bg.g * 0.587f + bg.b * 0.114f) > 0.66f ? Ink : Color.white;

        public static Color RarityColor(Rarity r)
        {
            switch (r)
            {
                case Rarity.Common: return Silhouette;
                case Rarity.Uncommon: return Turquoise;
                case Rarity.Rare: return Sky;
                case Rarity.Epic: return Violet;
                default: return Sun;
            }
        }

        public static Color DifficultyColor(Difficulty d)
        {
            switch (DifficultySettings.Normalize(d))
            {
                case Difficulty.Snorkeler: return Turquoise;
                case Difficulty.OpenWater: return Sun;
                default: return Coral;
            }
        }
    }

    public enum UIBtnKind { Primary, Secondary, Ghost, Accent, Danger, Live }

    /// Colors of a button, kept in Button.userData so hover callbacks can read them.
    public class UIBtnSkin
    {
        public Color bg, fg, hover, border;
    }

    /// Fredoka (Resources/Fonts/Fredoka-Variable.ttf) for every panel. Built once; falls back to the theme font.
    public static class UIFonts
    {
        static bool loaded;
        static bool available;
        static FontDefinition definition;

        const string PrewarmGlyphs = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]_abcdefghijklmnopqrstuvwxyzÄÖÜäöüß€·–—…„“”°×−";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { loaded = false; available = false; definition = default; }

        public static bool Available { get { Load(); return available; } }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            Font font = null;
            try { font = Resources.Load<Font>("Fonts/Fredoka-Variable"); }
            catch (Exception e) { Debug.LogWarning("Sealebrities UI: Fredoka could not be loaded. " + e.Message); }
            if (font == null)
            {
                Debug.LogWarning("Sealebrities UI: Resources/Fonts/Fredoka-Variable not found – using the default font.");
                return;
            }
            try
            {
                var asset = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font, 90, 9,
                    UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                    UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
                if (asset != null)
                {
                    asset.name = "SB_Fredoka_SDF";
                    // rasterise the common glyphs now (boot, behind the loading screen) instead of on first use in a
                    // toast or phone app (a few dozen new 90 pt glyphs can take several ms mid-game)
                    try { asset.TryAddCharacters(PrewarmGlyphs); }
                    catch (Exception e) { Debug.LogWarning("Sealebrities UI: glyph prewarm failed: " + e.Message); }
                    definition = FontDefinition.FromSDFFont(asset);
                    available = true;
                    return;
                }
            }
            catch (Exception e) { Debug.LogWarning("Sealebrities UI: SDF font asset failed, using the plain font. " + e.Message); }
            definition = FontDefinition.FromFont(font);
            available = true;
        }

        /// Sets Fredoka on a panel root (inherited by every child). Returns false when the font is missing.
        public static bool Apply(VisualElement root)
        {
            Load();
            if (!available || root == null) return false;
            root.style.unityFontDefinition = definition;
            return true;
        }
    }

    /// Element builders with inline styles (no USS files needed).
    public static class UIKit
    {
        // ------------------------------------------------------------------ basic elements
        public static VisualElement Box(string name = null)
        {
            var e = new VisualElement();
            if (name != null) e.name = name;
            return e;
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

        public static Label Text(string text, float size = 16f, Color? color = null, bool bold = false)
        {
            var l = new Label(text ?? "");
            l.style.fontSize = size;
            l.style.color = color ?? UIPalette.Text;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.style.paddingLeft = 0; l.style.paddingRight = 0; l.style.paddingTop = 0; l.style.paddingBottom = 0;
            l.style.marginLeft = 0; l.style.marginRight = 0; l.style.marginTop = 0; l.style.marginBottom = 0;
            return l;
        }

        /// Wrapping paragraph text.
        public static Label Para(string text, float size = 14f, Color? color = null)
        {
            var l = Text(text, size, color ?? UIPalette.TextDim);
            l.style.whiteSpace = WhiteSpace.Normal;
            return l;
        }

        /// Small uppercase caption with letter spacing.
        public static Label Caption(string text, Color? color = null, float size = 11f)
        {
            var l = Text(text != null ? text.ToUpperInvariant() : "", size, color ?? UIPalette.TextFaint, true);
            l.style.letterSpacing = 1.2f;
            return l;
        }

        public static void Shadow(Label l, float blur = 6f, float alpha = 0.65f)
        {
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = blur, color = new Color(0f, 0f, 0f, alpha) };
        }

        /// HUD look: thin dark outline + soft drop shadow (art bible §1.14). Works on any label.
        public static void Outline(Label l, float width = 1.5f, float shadowAlpha = 0.35f)
        {
            l.style.unityTextOutlineWidth = width;
            l.style.unityTextOutlineColor = UIPalette.Outline;
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, 2f), blurRadius = 4f, color = new Color(0f, 0f, 0f, shadowAlpha) };
        }

        /// Floating HUD label: white (or colored) Fredoka text with outline and shadow, no background.
        public static Label HudText(string text, float size, Color? color = null, bool bold = true)
        {
            var l = Text(text, size, color ?? Color.white, bold);
            Outline(l, size >= 40f ? 2.2f : size >= 26f ? 1.8f : 1.5f);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static VisualElement Spacer(float h)
        {
            var e = new VisualElement();
            e.style.height = h;
            e.style.flexShrink = 0;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public static VisualElement HSpacer(float w)
        {
            var e = new VisualElement();
            e.style.width = w;
            e.style.flexShrink = 0;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public static VisualElement Flex()
        {
            var e = new VisualElement();
            e.style.flexGrow = 1;
            e.pickingMode = PickingMode.Ignore;
            return e;
        }

        public static VisualElement Divider(float margin = 10f)
        {
            var e = new VisualElement();
            e.style.height = 1;
            e.style.backgroundColor = UIPalette.Line;
            e.style.marginTop = margin; e.style.marginBottom = margin;
            e.style.flexShrink = 0;
            return e;
        }

        // ------------------------------------------------------------------ style helpers
        public static void Pad(VisualElement e, float all) => Pad(e, all, all, all, all);
        public static void Pad(VisualElement e, float vertical, float horizontal) => Pad(e, vertical, horizontal, vertical, horizontal);
        public static void Pad(VisualElement e, float t, float r, float b, float l)
        {
            e.style.paddingTop = t; e.style.paddingRight = r; e.style.paddingBottom = b; e.style.paddingLeft = l;
        }

        public static void Margin(VisualElement e, float all) => Margin(e, all, all, all, all);
        public static void Margin(VisualElement e, float t, float r, float b, float l)
        {
            e.style.marginTop = t; e.style.marginRight = r; e.style.marginBottom = b; e.style.marginLeft = l;
        }

        public static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r; e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r; e.style.borderBottomRightRadius = r;
        }

        public static void Border(VisualElement e, float w, Color c)
        {
            e.style.borderTopWidth = w; e.style.borderRightWidth = w; e.style.borderBottomWidth = w; e.style.borderLeftWidth = w;
            BorderColor(e, c);
        }

        public static void BorderColor(VisualElement e, Color c)
        {
            e.style.borderTopColor = c; e.style.borderRightColor = c; e.style.borderBottomColor = c; e.style.borderLeftColor = c;
        }

        public static void Abs(VisualElement e, float? left, float? top, float? right, float? bottom)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        public static void FillParent(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.right = 0; e.style.bottom = 0;
        }

        public static void Size(VisualElement e, float w, float h)
        {
            e.style.width = w; e.style.height = h;
            e.style.flexShrink = 0;
        }

        public static void Center(VisualElement e)
        {
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
        }

        public static void Show(VisualElement e, bool show)
        {
            if (e == null) return;
            var d = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (e.style.display.value != d || e.style.display.keyword != StyleKeyword.Undefined) e.style.display = d;
        }

        public static bool IsShown(VisualElement e) => e != null && e.style.display.value != DisplayStyle.None;

        /// Background image. contain = keep aspect inside the box (icons); otherwise stretch to fill (gradients, map).
        public static void Image(VisualElement e, Texture2D tex, bool contain = true)
        {
            if (tex == null) { e.style.backgroundImage = StyleKeyword.None; return; }
            e.style.backgroundImage = Background.FromTexture2D(tex);
            e.style.backgroundSize = contain
                ? new BackgroundSize(BackgroundSizeType.Contain)
                : new BackgroundSize(Length.Percent(100f), Length.Percent(100f));
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
        }

        /// Background image that covers the box (cropping, keeps aspect) – photos and thumbnails.
        public static void ImageCover(VisualElement e, Texture2D tex)
        {
            if (tex == null) { e.style.backgroundImage = StyleKeyword.None; return; }
            e.style.backgroundImage = Background.FromTexture2D(tex);
            e.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            e.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            e.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            e.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        }

        public static void IgnorePicking(VisualElement root)
        {
            root.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < root.hierarchy.childCount; i++) IgnorePicking(root.hierarchy[i]);
        }

        public static void Transition(VisualElement e, float seconds, params string[] properties)
        {
            var props = new List<StylePropertyName>();
            var durs = new List<TimeValue>();
            foreach (var p in properties) { props.Add(new StylePropertyName(p)); durs.Add(new TimeValue(seconds, TimeUnit.Second)); }
            e.style.transitionProperty = props;
            e.style.transitionDuration = durs;
        }

        /// Soft drop shadow for light cards (a blurred dark copy is not available in UI Toolkit, so a
        /// slightly darker bottom border fakes the lift).
        public static void Lift(VisualElement e, float strength = 1f)
        {
            e.style.borderBottomWidth = Mathf.Max(e.style.borderBottomWidth.value, 3f * strength);
            e.style.borderBottomColor = new Color(0.10f, 0.12f, 0.20f, 0.12f * strength);
        }

        // ------------------------------------------------------------------ composite elements
        /// Light frosted card (default) – pass a color for tinted cards.
        public static VisualElement Card(Color? bg = null, float radius = 16f, float padding = 14f)
        {
            var e = new VisualElement();
            e.style.backgroundColor = bg ?? UIPalette.Panel;
            Radius(e, radius);
            Pad(e, padding);
            return e;
        }

        public static Label Pill(string text, Color bg, Color? fg = null, float size = 11f)
        {
            var l = Text(text, size, fg ?? UIPalette.OnColor(bg), true);
            l.style.backgroundColor = bg;
            Radius(l, 999f);
            Pad(l, 3f, 9f, 3f, 9f);
            l.style.flexShrink = 0;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            return l;
        }

        /// Round dot (status light, rarity dot …).
        public static VisualElement Dot(Color c, float size = 8f)
        {
            var e = new VisualElement();
            Size(e, size, size);
            Radius(e, size * 0.5f);
            e.style.backgroundColor = c;
            return e;
        }

        /// Red notification badge with a number (or "!" for 0 / a dot for -1).
        public static Label BadgeLabel(int count, float size = 30f)
        {
            string t = count < 0 ? "" : count == 0 ? "!" : count > 99 ? "99+" : count.ToString();
            var b = Text(t, size * 0.55f, Color.white, true);
            b.pickingMode = PickingMode.Ignore;
            b.style.position = Position.Absolute;
            b.style.minWidth = size; b.style.height = size;
            b.style.backgroundColor = UIPalette.Badge;
            Radius(b, size * 0.5f);
            Pad(b, 0f, size * 0.2f, 0f, size * 0.2f);
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            Border(b, Mathf.Max(2f, size * 0.08f), Color.white);
            return b;
        }

        public static Button Btn(string text, Action onClick, UIBtnKind kind = UIBtnKind.Primary, float size = 17f)
        {
            var b = new Button();
            b.text = text;
            b.focusable = false;
            if (onClick != null) b.clicked += onClick;
            b.style.fontSize = size;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.unityTextAlign = TextAnchor.MiddleCenter;
            b.style.whiteSpace = WhiteSpace.Normal;
            Border(b, 0f, Color.clear);
            Radius(b, 14f);
            Pad(b, 11f, 20f, 11f, 20f);
            Margin(b, 0f);
            var skin = new UIBtnSkin();
            b.userData = skin;
            SetKind(b, kind);
            b.RegisterCallback<PointerEnterEvent>(OnBtnEnter);
            b.RegisterCallback<PointerLeaveEvent>(OnBtnLeave);
            b.RegisterCallback<PointerDownEvent>(OnBtnDown, TrickleDown.TrickleDown);
            b.RegisterCallback<PointerUpEvent>(OnBtnUp);
            b.RegisterCallback<ClickEvent>(OnBtnClick);
            Transition(b, 0.12f, "background-color", "scale");
            return b;
        }

        public static void SetKind(Button b, UIBtnKind kind)
        {
            switch (kind)
            {
                case UIBtnKind.Primary: SetColors(b, UIPalette.Coral, Color.white); break;
                case UIBtnKind.Secondary: SetColors(b, Color.white, UIPalette.Ink, UIPalette.A(UIPalette.Ink, 0.12f)); break;
                case UIBtnKind.Ghost: SetColors(b, new Color(1f, 1f, 1f, 0f), UIPalette.InkSoft); break;
                case UIBtnKind.Accent: SetColors(b, UIPalette.Turquoise, Color.white); break;
                case UIBtnKind.Danger: SetColors(b, Color.white, UIPalette.CoralInk, UIPalette.A(UIPalette.Coral, 0.45f)); break;
                default: SetColors(b, UIPalette.Live, Color.white); break;
            }
        }

        public static void SetColors(Button b, Color bg, Color fg) => SetColors(b, bg, fg, Color.clear);

        public static void SetColors(Button b, Color bg, Color fg, Color border)
        {
            var skin = b.userData as UIBtnSkin;
            if (skin == null) { skin = new UIBtnSkin(); b.userData = skin; }
            skin.bg = bg; skin.fg = fg; skin.border = border;
            skin.hover = bg.a < 0.2f ? UIPalette.A(UIPalette.Ink, 0.06f) : (bg.r + bg.g + bg.b > 2.7f ? UIPalette.Raised2 : UIPalette.Lighten(bg, 0.14f));
            b.style.backgroundColor = bg;
            b.style.color = fg;
            if (border.a > 0f) Border(b, 2f, border);
            else Border(b, 0f, Color.clear);
        }

        public static void SetEnabled(Button b, bool enabled)
        {
            b.SetEnabled(enabled);
            b.style.opacity = enabled ? 1f : 0.45f;
            if (!enabled && b.userData is UIBtnSkin s) b.style.backgroundColor = s.bg;
        }

        static void OnBtnEnter(PointerEnterEvent e)
        {
            if (e.currentTarget is Button b && b.enabledInHierarchy && b.userData is UIBtnSkin s) b.style.backgroundColor = s.hover;
        }

        static void OnBtnLeave(PointerLeaveEvent e)
        {
            if (e.currentTarget is Button b && b.userData is UIBtnSkin s)
            {
                b.style.backgroundColor = s.bg;
                b.style.scale = new Scale(Vector2.one);
            }
        }

        static void OnBtnDown(PointerDownEvent e)
        {
            if (e.currentTarget is Button b && b.enabledInHierarchy) b.style.scale = new Scale(new Vector2(0.97f, 0.97f));
        }

        static void OnBtnUp(PointerUpEvent e)
        {
            if (e.currentTarget is Button b) b.style.scale = new Scale(Vector2.one);
        }

        static void OnBtnClick(ClickEvent e) { UIAudio.Click(); }

        /// A clickable card-like element (not a Button, so it can hold arbitrary children).
        public static VisualElement Clickable(VisualElement e, Action onClick, Color normal, Color hover)
        {
            e.style.backgroundColor = normal;
            e.RegisterCallback<PointerEnterEvent>(_ => e.style.backgroundColor = hover);
            e.RegisterCallback<PointerLeaveEvent>(_ => e.style.backgroundColor = normal);
            e.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); onClick?.Invoke(); });
            return e;
        }

        public static ScrollView Scroll()
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.mouseWheelScrollSize = 80f;
            sv.focusable = false;
            sv.style.flexGrow = 1;
            // explicit layout of the internals so scrolling also works without a theme style sheet
            var inner = sv.Q(className: ScrollView.contentAndVerticalScrollUssClassName);
            if (inner != null)
            {
                inner.style.flexDirection = FlexDirection.Row;
                inner.style.flexGrow = 1;
                inner.style.flexShrink = 1;
            }
            sv.contentViewport.style.flexGrow = 1;
            sv.contentViewport.style.flexShrink = 1;
            sv.contentViewport.style.overflow = Overflow.Hidden;
            sv.contentContainer.style.flexShrink = 0;
            return sv;
        }

        /// Keyboard key cap ("E", "Tab", …) – light cap with a dark border (cards / menus).
        public static Label KeyCap(string key, float size = 14f)
        {
            var l = Text(key, size, UIPalette.Ink, true);
            l.style.backgroundColor = Color.white;
            Border(l, 2f, UIPalette.A(UIPalette.Ink, 0.25f));
            l.style.borderBottomWidth = 4f;
            Radius(l, 7f);
            Pad(l, 3f, 9f, 2f, 9f);
            l.style.minWidth = 30f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.flexShrink = 0;
            return l;
        }

        /// HUD key cap: transparent, 2 px white outline, radius 6, outlined white text (art bible §1.14).
        public static Label HudKeyCap(string key, float size = 32f)
        {
            var l = HudText(key, size * 0.56f, Color.white, true);
            l.style.minWidth = size; l.style.height = size;
            Border(l, 2f, Color.white);
            Radius(l, 6f);
            Pad(l, 0f, 7f, 0f, 7f);
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            l.style.backgroundColor = new Color(0.06f, 0.08f, 0.14f, 0.18f);
            l.style.flexShrink = 0;
            return l;
        }

        static readonly Dictionary<(Color, Color, bool), Texture2D> Gradients = new Dictionary<(Color, Color, bool), Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Gradients.Clear(); }

        /// Horizontal gradient texture (left → right) used for menu backdrops. Cached per color pair.
        public static Texture2D HGradient(Color left, Color right)
        {
            if (Gradients.TryGetValue((left, right, true), out var cached) && cached != null) return cached;
            const int width = 256;
            var t = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SB_UI_HGradient" };
            for (int x = 0; x < width; x++)
            {
                float f = x / (float)(width - 1);
                t.SetPixel(x, 0, Color.Lerp(left, right, f * f * (3f - 2f * f)));
            }
            t.Apply(false, true);
            Gradients[(left, right, true)] = t;
            return t;
        }

        /// Vertical gradient texture (bottom → top). Cached per color pair.
        public static Texture2D VGradient(Color bottom, Color top)
        {
            if (Gradients.TryGetValue((bottom, top, false), out var cached) && cached != null) return cached;
            const int height = 256;
            var t = new Texture2D(1, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "SB_UI_VGradient" };
            for (int y = 0; y < height; y++)
            {
                float f = y / (float)(height - 1);
                t.SetPixel(0, y, Color.Lerp(bottom, top, f));
            }
            t.Apply(false, true);
            Gradients[(bottom, top, false)] = t;
            return t;
        }

        // ------------------------------------------------------------------ formatting
        /// "×1,6" with the current culture.
        public static string Mult(float m) => "×" + m.ToString("0.##", Loc.Culture);

        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        /// "5:42" (minutes without leading zero).
        public static string ClockShort(int seconds)
        {
            int s = Mathf.Max(0, seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public static string TimeOfDay(float t01)
        {
            float h = Mathf.Repeat(t01, 1f) * 24f;
            int hh = Mathf.FloorToInt(h) % 24;
            int mm = Mathf.FloorToInt((h - Mathf.Floor(h)) * 60f);
            return hh.ToString("00") + ":" + mm.ToString("00");
        }

        public static string Distance(float meters)
        {
            if (meters >= 1000f) return (meters / 1000f).ToString("0.0", Loc.Culture) + " km";
            return Loc.Meters(meters);
        }

        public static string DepthRange(float a, float b) => a.ToString("0", Loc.Culture) + "–" + b.ToString("0", Loc.Culture) + " m";

        public static string Signed(long v) => (v >= 0 ? "+" : "−") + Loc.Number(Math.Abs(v));
        public static string SignedMoney(long v) => (v >= 0 ? "+" : "−") + Loc.Money(Math.Abs(v));

        /// Compact number for tight spots: 950 · 12,4k · 1,2 Mio.
        public static string Compact(long n)
        {
            long a = Math.Abs(n);
            string s;
            if (a >= 1000000) s = (a / 1000000f).ToString(a >= 10000000 ? "0" : "0.#", Loc.Culture) + Loc.Tr(" Mio.", "M");
            else if (a >= 10000) s = (a / 1000f).ToString(a >= 100000 ? "0" : "0.#", Loc.Culture) + "k";
            else s = Loc.Number(a);
            return n < 0 ? "−" + s : s;
        }

        public static bool InGame => GameManager.I != null && GameManager.I.InGame;
        public static GameState State => GameManager.I != null ? GameManager.I.State : null;
    }
}
