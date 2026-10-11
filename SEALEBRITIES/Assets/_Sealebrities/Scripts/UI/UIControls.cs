using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Flat progress bar (track + fill). SetValue is allocation free and skips unchanged values.
    public class UIBar : VisualElement
    {
        public readonly VisualElement Fill;
        float value = -1f;
        Color fillColor;

        public UIBar(float height, Color fill, Color? track = null, float radius = -1f)
        {
            style.height = height;
            style.flexShrink = 0;
            style.backgroundColor = track ?? UIPalette.A(UIPalette.Ink, 0.10f);
            style.overflow = Overflow.Hidden;
            UIKit.Radius(this, radius < 0f ? height * 0.5f : radius);
            pickingMode = PickingMode.Ignore;

            Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            Fill.style.position = Position.Absolute;
            Fill.style.left = 0; Fill.style.top = 0; Fill.style.bottom = 0;
            Fill.style.width = Length.Percent(0f);
            UIKit.Radius(Fill, radius < 0f ? height * 0.5f : radius);
            Add(Fill);
            SetColor(fill);
            SetValue(0f);
        }

        public float Value => value;

        public void SetValue(float v01)
        {
            v01 = Mathf.Clamp01(v01);
            if (Mathf.Abs(v01 - value) < 0.0005f) return;
            value = v01;
            Fill.style.width = Length.Percent(v01 * 100f);
        }

        public void SetColor(Color c)
        {
            if (c == fillColor) return;
            fillColor = c;
            Fill.style.backgroundColor = c;
        }

        /// Adds a thin vertical marker (e.g. the 60 % unlock threshold).
        public VisualElement AddMarker(float at01, Color c)
        {
            var m = new VisualElement { pickingMode = PickingMode.Ignore };
            m.style.position = Position.Absolute;
            m.style.top = 0; m.style.bottom = 0; m.style.width = 3;
            m.style.left = Length.Percent(Mathf.Clamp01(at01) * 100f);
            m.style.backgroundColor = c;
            Add(m);
            return m;
        }
    }

    /// Custom horizontal slider (theme independent): track, fill and a round knob, dragged with pointer capture.
    /// onChanged fires while dragging, onReleased once when the pointer is let go (save settings there).
    public class UISlider : VisualElement
    {
        readonly VisualElement track, fill, knob;
        readonly float min, max;
        float value;
        readonly Action<float> onChanged;
        readonly Action<float> onReleased;

        public UISlider(float min, float max, float value, Action<float> onChanged, Color accent, Action<float> onReleased = null, float height = 32f)
        {
            this.min = min; this.max = max; this.onChanged = onChanged; this.onReleased = onReleased;
            style.height = height;
            style.flexGrow = 1;
            style.justifyContent = Justify.Center;
            focusable = false;

            float th = Mathf.Max(6f, height * 0.25f);
            track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = th;
            track.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.12f);
            UIKit.Radius(track, th * 0.5f);
            Add(track);

            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.backgroundColor = accent;
            UIKit.Radius(fill, th * 0.5f);
            track.Add(fill);

            float k = Mathf.Round(height * 0.7f);
            knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.style.position = Position.Absolute;
            knob.style.width = k; knob.style.height = k;
            knob.style.top = (height - k) * 0.5f;
            knob.style.marginLeft = -k * 0.5f;
            knob.style.backgroundColor = Color.white;
            UIKit.Radius(knob, k * 0.5f);
            UIKit.Border(knob, Mathf.Max(2f, k * 0.14f), accent);
            Add(knob);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
            RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            SetValue(value, false);
        }

        public float Value => value;

        public void SetValue(float v, bool notify)
        {
            value = Mathf.Clamp(v, min, max);
            float f = Mathf.InverseLerp(min, max, value);
            fill.style.width = Length.Percent(f * 100f);
            knob.style.left = Length.Percent(f * 100f);
            if (notify) onChanged?.Invoke(value);
        }

        void FromPointer(Vector3 local)
        {
            float w = layout.width;
            if (w <= 1f) return;
            SetValue(Mathf.Lerp(min, max, Mathf.Clamp01(local.x / w)), true);
        }

        void OnDown(PointerDownEvent e)
        {
            this.CapturePointer(e.pointerId);
            FromPointer(e.localPosition);
            e.StopPropagation();
        }

        void OnMove(PointerMoveEvent e)
        {
            if (this.HasPointerCapture(e.pointerId)) FromPointer(e.localPosition);
        }

        void OnUp(PointerUpEvent e)
        {
            if (!this.HasPointerCapture(e.pointerId)) return;
            this.ReleasePointer(e.pointerId);
        }

        void OnCaptureOut(PointerCaptureOutEvent e) { onReleased?.Invoke(value); }
    }

    /// Segmented selector (e.g. Deutsch | English, quality levels). Light pill track, colored selection.
    public class UISegmented : VisualElement
    {
        readonly Button[] buttons;
        readonly Color accent;
        int selected;
        readonly Action<int> onSelect;

        public UISegmented(string[] labels, int selected, Action<int> onSelect, Color accent, float fontSize = 15f)
        {
            this.accent = accent; this.onSelect = onSelect; this.selected = selected;
            style.flexDirection = FlexDirection.Row;
            style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.07f);
            UIKit.Radius(this, 14);
            UIKit.Pad(this, 4);
            buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn(labels[i], () => Select(idx, true), UIBtnKind.Ghost, fontSize);
                b.style.flexGrow = 1;
                b.style.flexBasis = 0;
                UIKit.Pad(b, fontSize * 0.5f, 8f, fontSize * 0.5f, 8f);
                UIKit.Radius(b, 11);
                if (i > 0) b.style.marginLeft = 4;
                buttons[i] = b;
                Add(b);
            }
            Refresh();
        }

        public int Selected => selected;

        public void Select(int i, bool notify)
        {
            selected = Mathf.Clamp(i, 0, buttons.Length - 1);
            Refresh();
            if (notify) onSelect?.Invoke(selected);
        }

        void Refresh()
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (i == selected) UIKit.SetColors(buttons[i], accent, UIPalette.OnColor(accent));
                else UIKit.SetColors(buttons[i], new Color(1f, 1f, 1f, 0f), UIPalette.InkSoft);
            }
        }
    }

    /// On/off switch (pill with a sliding knob).
    public class UIToggle : VisualElement
    {
        readonly VisualElement knob;
        readonly Color accent;
        readonly Action<bool> onChanged;
        bool on;

        public UIToggle(bool value, Action<bool> onChanged, Color accent, float height = 34f)
        {
            this.accent = accent; this.onChanged = onChanged; on = value;
            UIKit.Size(this, height * 1.8f, height);
            UIKit.Radius(this, height * 0.5f);
            focusable = false;
            knob = new VisualElement { pickingMode = PickingMode.Ignore };
            float k = height - 8f;
            knob.style.position = Position.Absolute;
            knob.style.top = 4f;
            knob.style.width = k; knob.style.height = k;
            UIKit.Radius(knob, k * 0.5f);
            knob.style.backgroundColor = Color.white;
            Add(knob);
            UIKit.Transition(knob, 0.12f, "left");
            RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); Set(!on, true); });
            Set(on, false);
        }

        public bool Value => on;

        public void Set(bool v, bool notify)
        {
            on = v;
            style.backgroundColor = on ? accent : UIPalette.A(UIPalette.Ink, 0.18f);
            float h = resolvedStyle.height > 1f ? resolvedStyle.height : style.height.value.value;
            knob.style.left = on ? h * 1.8f - (h - 8f) - 4f : 4f;
            if (notify) onChanged?.Invoke(on);
        }
    }

    /// Vector glyph drawn with Painter2D (icons that do not depend on font coverage).
    public class UIGlyph : VisualElement
    {
        public delegate void DrawFn(Painter2D p, Rect r, Color c);

        DrawFn draw;
        Color color;

        public UIGlyph(DrawFn draw, Color color, float size)
        {
            this.draw = draw; this.color = color;
            style.width = size; style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
        }

        public Color Color => color;
        public DrawFn Draw => draw;

        public void SetColor(Color c)
        {
            if (c == color) return;
            color = c;
            MarkDirtyRepaint();
        }

        public void SetDraw(DrawFn d)
        {
            if (d == draw) return;
            draw = d;
            MarkDirtyRepaint();
        }

        void OnGenerate(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (draw == null || r.width < 1f || r.height < 1f) return;
            draw(ctx.painter2D, r, color);
        }
    }

    /// A single-color glyph with a dark outline + soft shadow underneath (HUD icons over the 3D world).
    public class UIHudGlyph : VisualElement
    {
        readonly UIGlyph shadow, main;

        public UIHudGlyph(UIGlyph.DrawFn draw, Color color, float size)
        {
            UIKit.Size(this, size, size);
            pickingMode = PickingMode.Ignore;
            shadow = new UIGlyph(draw, UIPalette.Outline, size);
            shadow.style.position = Position.Absolute;
            shadow.style.left = 0; shadow.style.top = 1.5f;
            shadow.style.scale = new Scale(new Vector2(1.12f, 1.12f));
            Add(shadow);
            main = new UIGlyph(draw, color, size);
            main.style.position = Position.Absolute;
            main.style.left = 0; main.style.top = 0;
            Add(main);
        }

        public void SetColor(Color c) => main.SetColor(c);

        public void SetDraw(UIGlyph.DrawFn d)
        {
            shadow.SetDraw(d);
            main.SetDraw(d);
        }
    }

    /// Circular progress ring (air gauge). Repaints only when the value changes noticeably.
    public class UIRing : VisualElement
    {
        float value = 1f;
        Color fill, track;
        readonly float thickness;
        bool outline;

        public UIRing(float size, float thickness, Color fill, Color track, bool outline = false)
        {
            this.thickness = thickness; this.fill = fill; this.track = track; this.outline = outline;
            style.width = size; style.height = size;
            style.flexShrink = 0;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
        }

        public void Set(float v01, Color c)
        {
            v01 = Mathf.Clamp01(v01);
            if (Mathf.Abs(v01 - value) < 0.004f && c == fill) return;
            value = v01; fill = c;
            MarkDirtyRepaint();
        }

        void OnGenerate(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 2f) return;
            var p = ctx.painter2D;
            var c = r.center;
            float rad = Mathf.Min(r.width, r.height) * 0.5f - thickness * 0.5f - 2f;
            if (outline)
            {
                p.lineWidth = thickness + 3f;
                p.strokeColor = UIPalette.A(UIPalette.Outline, 0.45f);
                p.BeginPath();
                p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
                p.Stroke();
            }
            p.lineWidth = thickness;
            p.strokeColor = track;
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            if (value > 0.003f)
            {
                p.strokeColor = fill;
                p.lineCap = LineCap.Round;
                p.BeginPath();
                p.Arc(c, rad, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * value));
                p.Stroke();
            }
        }
    }
}
