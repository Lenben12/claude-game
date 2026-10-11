using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round-2 icons (character screen, notifications, ranks, deals, food, sleep, shop …). Same drawing rules as UIGlyphs:
    /// every function draws into rect r (y down) with colour c; c.a is an overall opacity.
    public static partial class UIGlyphs
    {
        // ------------------------------------------------------------------ general
        /// Music note (music volume).
        public static void Note(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.3f, 0.76f), s * 0.14f);
            Circle(p, P(r, 0.74f, 0.66f), s * 0.14f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.08f, 0.42f, 0.76f, 0.42f, 0.18f, 0.86f, 0.1f, 0.86f, 0.66f);
        }

        /// Record dot inside a ring (auto-record).
        public static void Rec(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Ring(p, r.center, s * 0.38f, s * 0.08f);
            p.fillColor = c;
            Circle(p, r.center, s * 0.22f);
        }

        /// Smartphone.
        public static void Phone(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.28f, 0.06f, 0.72f, 0.94f, 0.12f);
            p.fillColor = Al(Color.white, 0.75f * c.a);
            FillRoundRect(p, r, 0.33f, 0.14f, 0.67f, 0.78f, 0.03f);
            p.fillColor = Al(Color.white, 0.75f * c.a);
            Circle(p, P(r, 0.5f, 0.86f), s * 0.04f);
        }

        /// Six-sided die (character screen "Zufall").
        public static void Dice(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.12f, 0.12f, 0.88f, 0.88f, 0.16f);
            p.fillColor = Al(Color.white, 0.92f * c.a);
            Circle(p, P(r, 0.32f, 0.32f), s * 0.075f);
            Circle(p, P(r, 0.68f, 0.32f), s * 0.075f);
            Circle(p, P(r, 0.5f, 0.5f), s * 0.075f);
            Circle(p, P(r, 0.32f, 0.68f), s * 0.075f);
            Circle(p, P(r, 0.68f, 0.68f), s * 0.075f);
        }

        static void CurvedArrow(Painter2D p, Rect r, Color c, bool clockwise)
        {
            float s = S(r);
            var ctr = r.center;
            float rad = s * 0.3f;
            p.strokeColor = c;
            p.lineWidth = s * 0.1f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            // 270° sweep; y points down, so increasing angles turn clockwise on screen
            float a0 = clockwise ? -150f : -30f, a1 = clockwise ? 120f : -300f;
            p.Arc(ctr, rad, Angle.Degrees(a0), Angle.Degrees(a1), clockwise ? ArcDirection.Clockwise : ArcDirection.CounterClockwise);
            p.Stroke();
            // arrow head at the end angle, pointing along the direction of travel
            float end = a1 * Mathf.Deg2Rad;
            Vector2 tip = ctr + new Vector2(Mathf.Cos(end), Mathf.Sin(end)) * rad;
            Vector2 tan = clockwise ? new Vector2(-Mathf.Sin(end), Mathf.Cos(end)) : new Vector2(Mathf.Sin(end), -Mathf.Cos(end));
            Vector2 nrm = new Vector2(Mathf.Cos(end), Mathf.Sin(end));
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(tip + tan * s * 0.16f);
            p.LineTo(tip + nrm * s * 0.14f - tan * s * 0.04f);
            p.LineTo(tip - nrm * s * 0.14f - tan * s * 0.04f);
            p.ClosePath();
            p.Fill();
        }

        public static void RotateRight(Painter2D p, Rect r, Color c) => CurvedArrow(p, r, c, true);
        public static void RotateLeft(Painter2D p, Rect r, Color c) => CurvedArrow(p, r, c, false);

        /// Undo / reset (counter-clockwise arrow).
        public static void Undo(Painter2D p, Rect r, Color c) => CurvedArrow(p, r, c, false);

        /// Up arrow (quest-line distance).
        public static void ArrowUp(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            Poly(p, r, 0.5f, 0.1f, 0.86f, 0.5f, 0.62f, 0.5f, 0.62f, 0.9f, 0.38f, 0.9f, 0.38f, 0.5f, 0.14f, 0.5f);
        }

        /// Pencil (edit the title).
        public static void Pencil(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.66f, 0.12f, 0.88f, 0.34f, 0.36f, 0.86f, 0.14f, 0.64f);
            Poly(p, r, 0.14f, 0.7f, 0.3f, 0.86f, 0.08f, 0.92f);
            p.fillColor = Al(Color.white, 0.5f * c.a);
            Poly(p, r, 0.6f, 0.18f, 0.82f, 0.4f, 0.76f, 0.46f, 0.54f, 0.24f);
            _ = s;
        }

        /// Sort lines (Sealog "nach Tiefe sortieren").
        public static void Sort(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.1f, 0.14f, 0.24f, 0.86f, 0.24f);
            Stroke(p, r, s * 0.1f, 0.14f, 0.5f, 0.66f, 0.5f);
            Stroke(p, r, s * 0.1f, 0.14f, 0.76f, 0.46f, 0.76f);
        }

        /// Four-point sparkle (new / event).
        public static void Sparkle(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.04f));
            p.QuadraticCurveTo(P(r, 0.56f, 0.44f), P(r, 0.96f, 0.5f));
            p.QuadraticCurveTo(P(r, 0.56f, 0.56f), P(r, 0.5f, 0.96f));
            p.QuadraticCurveTo(P(r, 0.44f, 0.56f), P(r, 0.04f, 0.5f));
            p.QuadraticCurveTo(P(r, 0.44f, 0.44f), P(r, 0.5f, 0.04f));
            p.ClosePath();
            p.Fill();
        }

        // ------------------------------------------------------------------ character screen
        /// Diving suit (bean body with a zip).
        public static void Suit(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.08f));
            p.BezierCurveTo(P(r, 0.8f, 0.08f), P(r, 0.84f, 0.5f), P(r, 0.84f, 0.92f));
            p.LineTo(P(r, 0.16f, 0.92f));
            p.BezierCurveTo(P(r, 0.16f, 0.5f), P(r, 0.2f, 0.08f), P(r, 0.5f, 0.08f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Al(Color.white, 0.7f * c.a);
            Stroke(p, r, s * 0.06f, 0.5f, 0.42f, 0.5f, 0.88f);
            p.fillColor = Al(Color.white, 0.7f * c.a);
            FillRoundRect(p, r, 0.3f, 0.2f, 0.7f, 0.34f, 0.2f);
        }

        /// Bucket hat.
        public static void Hat(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.28f, 0.62f));
            p.BezierCurveTo(P(r, 0.28f, 0.2f), P(r, 0.72f, 0.2f), P(r, 0.72f, 0.62f));
            p.ClosePath();
            p.Fill();
            p.BeginPath();
            p.MoveTo(P(r, 0.06f, 0.74f));
            p.QuadraticCurveTo(P(r, 0.5f, 0.52f), P(r, 0.94f, 0.74f));
            p.QuadraticCurveTo(P(r, 0.5f, 0.66f), P(r, 0.06f, 0.74f));
            p.ClosePath();
            p.Fill();
        }

        /// Smiling face (face tab, selfie).
        public static void Face(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, r.center, s * 0.44f);
            p.fillColor = Al(Color.white, 0.95f * c.a);
            Circle(p, P(r, 0.36f, 0.42f), s * 0.09f);
            Circle(p, P(r, 0.64f, 0.42f), s * 0.09f);
            p.fillColor = Al(new Color(0.1f, 0.1f, 0.15f), c.a);
            Circle(p, P(r, 0.37f, 0.43f), s * 0.045f);
            Circle(p, P(r, 0.65f, 0.43f), s * 0.045f);
            p.strokeColor = Al(Color.white, 0.95f * c.a);
            p.lineWidth = s * 0.06f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(P(r, 0.5f, 0.56f), s * 0.17f, Angle.Degrees(25f), Angle.Degrees(155f));
            p.Stroke();
        }

        /// Selfie (phone with a smiley).
        public static void Selfie(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.22f, 0.06f, 0.78f, 0.94f, 0.12f);
            p.fillColor = Al(Color.white, 0.9f * c.a);
            Circle(p, P(r, 0.5f, 0.46f), s * 0.18f);
            p.fillColor = c;
            Circle(p, P(r, 0.44f, 0.42f), s * 0.03f);
            Circle(p, P(r, 0.56f, 0.42f), s * 0.03f);
            p.strokeColor = c;
            p.lineWidth = s * 0.035f;
            p.BeginPath();
            p.Arc(P(r, 0.5f, 0.47f), s * 0.09f, Angle.Degrees(25f), Angle.Degrees(155f));
            p.Stroke();
        }

        // ------------------------------------------------------------------ notifications / phone
        public static void Trophy(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.26f, 0.12f));
            p.LineTo(P(r, 0.74f, 0.12f));
            p.BezierCurveTo(P(r, 0.74f, 0.46f), P(r, 0.62f, 0.58f), P(r, 0.5f, 0.6f));
            p.BezierCurveTo(P(r, 0.38f, 0.58f), P(r, 0.26f, 0.46f), P(r, 0.26f, 0.12f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = c;
            p.lineWidth = s * 0.07f;
            p.BeginPath();
            p.Arc(P(r, 0.26f, 0.28f), s * 0.12f, Angle.Degrees(90f), Angle.Degrees(270f));
            p.Stroke();
            p.BeginPath();
            p.Arc(P(r, 0.74f, 0.28f), s * 0.12f, Angle.Degrees(-90f), Angle.Degrees(90f));
            p.Stroke();
            FillRoundRect(p, r, 0.44f, 0.58f, 0.56f, 0.78f, 0.02f);
            FillRoundRect(p, r, 0.3f, 0.78f, 0.7f, 0.9f, 0.04f);
        }

        /// Two hands shaking (sponsor deals – "Barnacle").
        public static void Handshake(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.04f, 0.38f, 0.22f, 0.3f, 0.3f, 0.62f, 0.12f, 0.7f);
            Poly(p, r, 0.96f, 0.38f, 0.78f, 0.3f, 0.7f, 0.62f, 0.88f, 0.7f);
            p.BeginPath();
            p.MoveTo(P(r, 0.24f, 0.36f));
            p.LineTo(P(r, 0.46f, 0.3f));
            p.LineTo(P(r, 0.58f, 0.38f));
            p.LineTo(P(r, 0.76f, 0.36f));
            p.LineTo(P(r, 0.7f, 0.6f));
            p.LineTo(P(r, 0.52f, 0.78f));
            p.LineTo(P(r, 0.3f, 0.6f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Al(Color.white, 0.6f * c.a);
            Stroke(p, r, s * 0.04f, 0.44f, 0.5f, 0.56f, 0.62f);
            Stroke(p, r, s * 0.04f, 0.4f, 0.58f, 0.5f, 0.7f);
        }

        /// Grilled fish on a stick (food buff).
        public static void Food(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.07f, 0.12f, 0.9f, 0.86f, 0.16f);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.26f, 0.72f));
            p.BezierCurveTo(P(r, 0.24f, 0.46f), P(r, 0.46f, 0.26f), P(r, 0.72f, 0.24f));
            p.BezierCurveTo(P(r, 0.74f, 0.5f), P(r, 0.52f, 0.72f), P(r, 0.26f, 0.72f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Al(Color.white, 0.6f * c.a);
            Stroke(p, r, s * 0.04f, 0.4f, 0.5f, 0.5f, 0.6f);
            Stroke(p, r, s * 0.04f, 0.5f, 0.4f, 0.6f, 0.5f);
        }

        /// Coconut with a straw (drink buff).
        public static void Drink(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.46f, 0.62f), s * 0.32f);
            p.fillColor = Al(Color.white, 0.55f * c.a);
            Circle(p, P(r, 0.46f, 0.4f), s * 0.12f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.07f, 0.5f, 0.38f, 0.66f, 0.1f, 0.84f, 0.1f);
        }

        /// Envelope (letter / message).
        public static void Envelope(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.08f, 0.22f, 0.92f, 0.8f, 0.06f);
            p.strokeColor = Al(Color.white, 0.8f * c.a);
            Stroke(p, r, s * 0.06f, 0.12f, 0.28f, 0.5f, 0.56f, 0.88f, 0.28f);
        }

        /// Gift box (reward).
        public static void Gift(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.14f, 0.38f, 0.86f, 0.9f, 0.05f);
            FillRoundRect(p, r, 0.08f, 0.28f, 0.92f, 0.42f, 0.04f);
            p.fillColor = Al(Color.white, 0.75f * c.a);
            FillRoundRect(p, r, 0.45f, 0.28f, 0.55f, 0.9f, 0.01f);
            p.strokeColor = c;
            p.lineWidth = s * 0.07f;
            p.BeginPath();
            p.Arc(P(r, 0.38f, 0.2f), s * 0.09f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
            p.BeginPath();
            p.Arc(P(r, 0.62f, 0.2f), s * 0.09f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Stroke();
        }

        /// Megaphone (animal event).
        public static void Megaphone(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.12f, 0.4f, 0.32f, 0.4f, 0.8f, 0.16f, 0.8f, 0.84f, 0.32f, 0.6f, 0.12f, 0.6f);
            FillRoundRect(p, r, 0.24f, 0.58f, 0.36f, 0.84f, 0.03f);
            p.strokeColor = c;
            p.lineWidth = s * 0.06f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(P(r, 0.82f, 0.5f), s * 0.12f, Angle.Degrees(-50f), Angle.Degrees(50f));
            p.Stroke();
        }

        /// Bed (sleep counter).
        public static void Bed(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.06f, 0.5f, 0.94f, 0.72f, 0.04f);
            FillRoundRect(p, r, 0.06f, 0.26f, 0.14f, 0.86f, 0.02f);
            FillRoundRect(p, r, 0.86f, 0.5f, 0.94f, 0.86f, 0.02f);
            FillRoundRect(p, r, 0.18f, 0.38f, 0.38f, 0.5f, 0.3f);
            _ = s;
        }

        /// Radio (underwater radio, radio card).
        public static void Radio(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.1f, 0.34f, 0.9f, 0.88f, 0.1f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.06f, 0.26f, 0.34f, 0.7f, 0.08f);
            p.fillColor = Al(Color.white, 0.75f * c.a);
            Circle(p, P(r, 0.34f, 0.61f), s * 0.14f);
            FillRoundRect(p, r, 0.58f, 0.48f, 0.8f, 0.54f, 0.2f);
            FillRoundRect(p, r, 0.58f, 0.6f, 0.8f, 0.66f, 0.2f);
            FillRoundRect(p, r, 0.58f, 0.72f, 0.8f, 0.78f, 0.2f);
        }

        /// Camera lens (camera mods).
        public static void Lens(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Ring(p, r.center, s * 0.38f, s * 0.1f);
            p.fillColor = c;
            Circle(p, r.center, s * 0.2f);
            p.fillColor = Al(Color.white, 0.7f * c.a);
            Circle(p, P(r, 0.44f, 0.44f), s * 0.06f);
        }

        /// Winch drum with a hook (boat winch).
        public static void Winch(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.14f, 0.16f, 0.86f, 0.42f, 0.1f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.06f, 0.5f, 0.42f, 0.5f, 0.66f);
            p.lineWidth = s * 0.08f;
            p.BeginPath();
            p.Arc(P(r, 0.44f, 0.74f), s * 0.12f, Angle.Degrees(-20f), Angle.Degrees(200f));
            p.Stroke();
        }

        /// Compressor (tank with an air arrow).
        public static void Compressor(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.16f, 0.2f, 0.48f, 0.9f, 0.3f);
            FillRoundRect(p, r, 0.26f, 0.08f, 0.38f, 0.22f, 0.1f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.08f, 0.6f, 0.62f, 0.86f, 0.62f);
            Poly(p, r, 0.86f, 0.5f, 0.98f, 0.62f, 0.86f, 0.74f);
            Stroke(p, r, s * 0.06f, 0.6f, 0.4f, 0.76f, 0.4f);
        }

        /// Smoke column (wrong-place parcel beacon).
        public static void Smoke(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.5f, 0.78f), s * 0.16f);
            Circle(p, P(r, 0.4f, 0.56f), s * 0.14f);
            Circle(p, P(r, 0.56f, 0.36f), s * 0.13f);
            Circle(p, P(r, 0.44f, 0.16f), s * 0.1f);
        }

        /// Small hut on stilts (village / map label).
        public static void Hut(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.08f, 0.44f, 0.5f, 0.12f, 0.92f, 0.44f);
            FillRoundRect(p, r, 0.2f, 0.44f, 0.8f, 0.7f, 0.02f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.07f, 0.28f, 0.7f, 0.28f, 0.92f);
            Stroke(p, r, s * 0.07f, 0.72f, 0.7f, 0.72f, 0.92f);
        }

        /// Cloud bank (Fog Isle).
        public static void Cloud(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.32f, 0.58f), s * 0.2f);
            Circle(p, P(r, 0.54f, 0.46f), s * 0.24f);
            Circle(p, P(r, 0.74f, 0.6f), s * 0.18f);
            FillRoundRect(p, r, 0.14f, 0.58f, 0.9f, 0.78f, 0.1f);
        }

        /// Sun with an "f(x)" feel – weather app (sun + small cloud).
        public static void Weather(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            Sun(p, new Rect(r.x, r.y, r.width * 0.75f, r.height * 0.75f), c);
            p.fillColor = c;
            Circle(p, P(r, 0.6f, 0.72f), s * 0.16f);
            Circle(p, P(r, 0.76f, 0.66f), s * 0.18f);
            FillRoundRect(p, r, 0.46f, 0.72f, 0.94f, 0.88f, 0.08f);
        }

        /// Microscope (Dr. Fin).
        public static void Microscope(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.16f, 0.82f, 0.84f, 0.92f, 0.1f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.12f, 0.36f, 0.16f, 0.56f, 0.56f);
            Stroke(p, r, s * 0.07f, 0.56f, 0.56f, 0.5f, 0.66f);
            p.lineWidth = s * 0.08f;
            p.BeginPath();
            p.Arc(P(r, 0.5f, 0.66f), s * 0.24f, Angle.Degrees(-70f), Angle.Degrees(90f));
            p.Stroke();
            Stroke(p, r, s * 0.08f, 0.5f, 0.9f, 0.5f, 0.82f);
        }

        // ------------------------------------------------------------------ rank badges (Q50)
        public static void Shrimp(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            p.lineWidth = s * 0.2f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(P(r, 0.5f, 0.5f), s * 0.26f, Angle.Degrees(-160f), Angle.Degrees(80f));
            p.Stroke();
            p.fillColor = c;
            Poly(p, r, 0.48f, 0.74f, 0.34f, 0.92f, 0.62f, 0.86f);
            p.lineWidth = s * 0.03f;
            Stroke(p, r, s * 0.03f, 0.26f, 0.42f, 0.08f, 0.18f);
            Stroke(p, r, s * 0.03f, 0.28f, 0.38f, 0.16f, 0.1f);
        }

        public static void Seahorse(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.5f, 0.22f), s * 0.13f);
            Poly(p, r, 0.58f, 0.18f, 0.84f, 0.2f, 0.84f, 0.28f, 0.58f, 0.3f);
            p.strokeColor = c;
            p.lineWidth = s * 0.17f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(r, 0.46f, 0.3f));
            p.BezierCurveTo(P(r, 0.3f, 0.5f), P(r, 0.62f, 0.62f), P(r, 0.5f, 0.78f));
            p.Stroke();
            p.lineWidth = s * 0.08f;
            p.BeginPath();
            p.Arc(P(r, 0.4f, 0.82f), s * 0.1f, Angle.Degrees(0f), Angle.Degrees(270f));
            p.Stroke();
        }

        public static void Turtle(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.16f, 0.62f));
            p.BezierCurveTo(P(r, 0.18f, 0.24f), P(r, 0.74f, 0.24f), P(r, 0.76f, 0.62f));
            p.ClosePath();
            p.Fill();
            Circle(p, P(r, 0.86f, 0.52f), s * 0.09f);
            Poly(p, r, 0.22f, 0.6f, 0.34f, 0.6f, 0.26f, 0.8f, 0.14f, 0.78f);
            Poly(p, r, 0.58f, 0.6f, 0.7f, 0.6f, 0.74f, 0.8f, 0.62f, 0.8f);
            p.strokeColor = Al(Color.white, 0.55f * c.a);
            Stroke(p, r, s * 0.04f, 0.32f, 0.5f, 0.46f, 0.38f, 0.6f, 0.5f);
        }

        public static void Whale(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.08f, 0.56f));
            p.BezierCurveTo(P(r, 0.1f, 0.28f), P(r, 0.56f, 0.26f), P(r, 0.7f, 0.52f));
            p.LineTo(P(r, 0.9f, 0.36f));
            p.LineTo(P(r, 0.86f, 0.56f));
            p.LineTo(P(r, 0.94f, 0.72f));
            p.LineTo(P(r, 0.7f, 0.62f));
            p.BezierCurveTo(P(r, 0.56f, 0.8f), P(r, 0.12f, 0.8f), P(r, 0.08f, 0.56f));
            p.ClosePath();
            p.Fill();
            p.fillColor = Al(Color.white, 0.8f * c.a);
            Circle(p, P(r, 0.24f, 0.52f), s * 0.035f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.05f, 0.3f, 0.3f, 0.3f, 0.14f);
            Stroke(p, r, s * 0.05f, 0.3f, 0.2f, 0.2f, 0.1f);
            Stroke(p, r, s * 0.05f, 0.3f, 0.2f, 0.4f, 0.1f);
        }

        /// Inner glyph of a channel rank (Q50: Plankton … Wal-Ikone).
        public static UIGlyph.DrawFn ForRank(int rank)
        {
            switch (Mathf.Clamp(rank, 0, 7))
            {
                case 0: return Bubbles;
                case 1: return Shrimp;
                case 2: return Fish;
                case 3: return Seahorse;
                case 4: return Turtle;
                case 5: return Star;
                case 6: return SharkFin;
                default: return Whale;
            }
        }

        /// Badge colour of a channel rank.
        public static Color RankColor(int rank)
        {
            switch (Mathf.Clamp(rank, 0, 7))
            {
                case 0: return UIPalette.Rgb(0x8FD3E0);
                case 1: return UIPalette.Rgb(0xF28C8C);
                case 2: return UIPalette.Rgb(0xEE7F2D);
                case 3: return UIPalette.Rgb(0xF2C94C);
                case 4: return UIPalette.Rgb(0x6E9445);
                case 5: return UIPalette.Rgb(0xE8C65A);
                case 6: return UIPalette.Rgb(0x4F7FAE);
                default: return UIPalette.Rgb(0x3A3F5C);
            }
        }

        /// Round rank badge: coloured disc, white ring, white inner animal (draw into any rect).
        public static void RankBadge(Painter2D p, Rect r, Color c, int rank)
        {
            float s = S(r);
            var ctr = r.center;
            Color col = RankColor(rank);
            col.a *= c.a;
            p.fillColor = Al(Color.white, c.a);
            Circle(p, ctr, s * 0.5f);
            p.fillColor = col;
            Circle(p, ctr, s * 0.43f);
            if (rank >= 5)
            {
                p.strokeColor = Al(UIPalette.Rgb(0xF2C94C), c.a);
                Ring(p, ctr, s * 0.39f, s * 0.04f);
            }
            var inner = new Rect(ctr.x - s * 0.27f, ctr.y - s * 0.27f, s * 0.54f, s * 0.54f);
            ForRank(rank)(p, inner, Al(Color.white, c.a));
        }

        // ------------------------------------------------------------------ notification kinds (L.10)
        public static UIGlyph.DrawFn ForNotify(NotifyKind k)
        {
            switch (k)
            {
                case NotifyKind.Money: return Coin;
                case NotifyKind.Followers: return Person;
                case NotifyKind.Sealog: return Book;
                case NotifyKind.Clip: return Camera;
                case NotifyKind.Delivery: return Heli;
                case NotifyKind.Quest: return Tasks;
                case NotifyKind.Sponsor: return Handshake;
                case NotifyKind.Story: return Envelope;
                case NotifyKind.Collectible: return Shell;
                case NotifyKind.Warning: return Warning;
                case NotifyKind.Event: return Megaphone;
                case NotifyKind.Achievement: return Trophy;
                default: return Bubble;
            }
        }

        public static Color NotifyColor(NotifyKind k)
        {
            switch (k)
            {
                case NotifyKind.Money: return UIPalette.Sun;
                case NotifyKind.Followers: return UIPalette.Turquoise;
                case NotifyKind.Sealog: return UIPalette.Rgb(0x3FA9B5);
                case NotifyKind.Clip: return UIPalette.Leaf;
                case NotifyKind.Delivery: return UIPalette.Coral;
                case NotifyKind.Quest: return UIPalette.Leaf;
                case NotifyKind.Sponsor: return UIPalette.Rgb(0x3FA9A0);
                case NotifyKind.Story: return UIPalette.Rgb(0xE07B39);
                case NotifyKind.Collectible: return UIPalette.Sun;
                case NotifyKind.Warning: return UIPalette.Warn;
                case NotifyKind.Event: return UIPalette.Violet;
                case NotifyKind.Achievement: return UIPalette.Rgb(0xE8C65A);
                default: return UIPalette.Turquoise;
            }
        }
    }

    /// Rank badge element (Q50) – coloured round medal with the rank animal.
    public class UIRankBadge : VisualElement
    {
        int rank;

        public UIRankBadge(int rank, float size)
        {
            this.rank = rank;
            style.width = size; style.height = size;
            style.flexShrink = 0;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += OnGenerate;
        }

        public int Rank => rank;

        public void SetRank(int r)
        {
            if (r == rank) return;
            rank = r;
            MarkDirtyRepaint();
        }

        void OnGenerate(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 2f) return;
            UIGlyphs.RankBadge(ctx.painter2D, r, Color.white, rank);
        }
    }
}
