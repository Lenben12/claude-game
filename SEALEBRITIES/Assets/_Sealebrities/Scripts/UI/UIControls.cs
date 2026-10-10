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
            style.backgroundColor = track ?? new Color(1f, 1f, 1f, 0.1f);
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
            m.style.top = 0; m.style.bottom = 0; m.style.width = 2;
            m.style.left = Length.Percent(Mathf.Clamp01(at01) * 100f);
            m.style.backgroundColor = c;
            Add(m);
            return m;
        }
    }

    /// Custom horizontal slider (theme independent): track, fill and a round knob, dragged with pointer capture.
    public class UISlider : VisualElement
    {
        readonly VisualElement track, fill, knob;
        readonly float min, max;
        float value;
        readonly Action<float> onChanged;

        public UISlider(float min, float max, float value, Action<float> onChanged, Color accent)
        {
            this.min = min; this.max = max; this.onChanged = onChanged;
            style.height = 28;
            style.flexGrow = 1;
            style.justifyContent = Justify.Center;
            focusable = false;

            track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 6;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            UIKit.Radius(track, 3);
            Add(track);

            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.position = Position.Absolute;
            fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.backgroundColor = accent;
            UIKit.Radius(fill, 3);
            track.Add(fill);

            knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.style.position = Position.Absolute;
            knob.style.width = 20; knob.style.height = 20;
            knob.style.top = 4;
            knob.style.marginLeft = -10;
            knob.style.backgroundColor = Color.white;
            UIKit.Radius(knob, 10);
            UIKit.Border(knob, 3f, accent);
            Add(knob);

            RegisterCallback<PointerDownEvent>(OnDown);
            RegisterCallback<PointerMoveEvent>(OnMove);
            RegisterCallback<PointerUpEvent>(OnUp);
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
            if (this.HasPointerCapture(e.pointerId)) this.ReleasePointer(e.pointerId);
        }
    }

    /// Segmented selector (e.g. Deutsch | English, title styles).
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
            style.backgroundColor = new Color(0f, 0f, 0f, 0.3f);
            UIKit.Radius(this, 12);
            UIKit.Pad(this, 4);
            buttons = new Button[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                var b = UIKit.Btn(labels[i], () => Select(idx, true), UIBtnKind.Ghost, fontSize);
                b.style.flexGrow = 1;
                b.style.flexBasis = 0;
                UIKit.Pad(b, 8f, 10f, 8f, 10f);
                UIKit.Radius(b, 9);
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
                else UIKit.SetColors(buttons[i], new Color(1f, 1f, 1f, 0f), UIPalette.TextDim);
            }
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

    /// Circular progress ring (air gauge). Repaints only when the value changes noticeably.
    public class UIRing : VisualElement
    {
        float value = 1f;
        Color fill, track;
        readonly float thickness;

        public UIRing(float size, float thickness, Color fill, Color track)
        {
            this.thickness = thickness; this.fill = fill; this.track = track;
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
            float rad = Mathf.Min(r.width, r.height) * 0.5f - thickness * 0.5f - 1f;
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
