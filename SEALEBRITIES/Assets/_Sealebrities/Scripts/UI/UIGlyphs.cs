using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Vector icons drawn with Painter2D. Every function draws into rect r (y down) with color c.
    /// Item* functions are multi-colored (hotbar / inventory) and use c.a as an overall opacity.
    public static class UIGlyphs
    {
        static Vector2 P(Rect r, float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);
        static float S(Rect r) => Mathf.Min(r.width, r.height);

        static void Circle(Painter2D p, Vector2 c, float rad)
        {
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        static void Ring(Painter2D p, Vector2 c, float rad, float width)
        {
            p.lineWidth = width;
            p.BeginPath();
            p.Arc(c, rad, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Stroke();
        }

        static void Poly(Painter2D p, Rect r, params float[] xy)
        {
            p.BeginPath();
            p.MoveTo(P(r, xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(r, xy[i], xy[i + 1]));
            p.ClosePath();
            p.Fill();
        }

        static void Stroke(Painter2D p, Rect r, float width, params float[] xy)
        {
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            p.MoveTo(P(r, xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(r, xy[i], xy[i + 1]));
            p.Stroke();
        }

        static void RoundRect(Painter2D p, Rect r, float x0, float y0, float x1, float y1, float radius01)
        {
            var a = P(r, x0, y0); var b = P(r, x1, y1);
            float rad = Mathf.Min(S(r) * radius01, Mathf.Abs(b.x - a.x) * 0.5f, Mathf.Abs(b.y - a.y) * 0.5f);
            p.MoveTo(new Vector2(a.x + rad, a.y));
            p.ArcTo(new Vector2(b.x, a.y), new Vector2(b.x, b.y), rad);
            p.ArcTo(new Vector2(b.x, b.y), new Vector2(a.x, b.y), rad);
            p.ArcTo(new Vector2(a.x, b.y), new Vector2(a.x, a.y), rad);
            p.ArcTo(new Vector2(a.x, a.y), new Vector2(b.x, a.y), rad);
            p.ClosePath();
        }

        static void FillRoundRect(Painter2D p, Rect r, float x0, float y0, float x1, float y1, float radius01)
        {
            p.BeginPath();
            RoundRect(p, r, x0, y0, x1, y1, radius01);
            p.Fill();
        }

        static Color Al(Color c, float a) { c.a *= a; return c; }

        // ------------------------------------------------------------------ status / HUD
        public static void Sun(Painter2D p, Rect r, Color c)
        {
            var ctr = r.center; float s = S(r);
            p.fillColor = c;
            Circle(p, ctr, s * 0.22f);
            p.strokeColor = c;
            p.lineWidth = s * 0.08f;
            p.lineCap = LineCap.Round;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI * 0.25f;
                var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                p.BeginPath();
                p.MoveTo(ctr + d * s * 0.33f);
                p.LineTo(ctr + d * s * 0.45f);
                p.Stroke();
            }
        }

        public static void Moon(Painter2D p, Rect r, Color c)
        {
            var ctr = r.center; float R = S(r) * 0.4f;
            var top = ctr + new Vector2(Mathf.Cos(-70f * Mathf.Deg2Rad), Mathf.Sin(-70f * Mathf.Deg2Rad)) * R;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(top);
            p.Arc(ctr, R, Angle.Degrees(-70f), Angle.Degrees(70f), ArcDirection.CounterClockwise);
            p.QuadraticCurveTo(ctr + new Vector2(-0.55f * R, 0f), top);
            p.ClosePath();
            p.Fill();
        }

        public static void Check(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.13f, 0.2f, 0.52f, 0.42f, 0.74f, 0.8f, 0.28f);
        }

        public static void Cross(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.12f, 0.25f, 0.25f, 0.75f, 0.75f);
            Stroke(p, r, S(r) * 0.12f, 0.75f, 0.25f, 0.25f, 0.75f);
        }

        public static void ChevronLeft(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.12f, 0.62f, 0.2f, 0.32f, 0.5f, 0.62f, 0.8f);
        }

        public static void ChevronRight(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.12f, 0.38f, 0.2f, 0.68f, 0.5f, 0.38f, 0.8f);
        }

        public static void Lock(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.2f, 0.44f, 0.8f, 0.9f, 0.08f);
            p.Fill();
            p.strokeColor = c;
            p.lineWidth = s * 0.1f;
            p.lineCap = LineCap.Butt;
            p.BeginPath();
            p.MoveTo(P(r, 0.32f, 0.46f));
            p.LineTo(P(r, 0.32f, 0.32f));
            p.Arc(P(r, 0.5f, 0.32f), s * 0.18f, Angle.Degrees(180f), Angle.Degrees(360f));
            p.LineTo(P(r, 0.68f, 0.46f));
            p.Stroke();
        }

        public static void Person(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.5f, 0.32f), s * 0.18f);
            p.BeginPath();
            p.MoveTo(P(r, 0.14f, 0.92f));
            p.Arc(P(r, 0.5f, 0.92f), s * 0.36f, Angle.Degrees(180f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        /// Two people (co-op crew).
        public static void Crew(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = Al(c, 0.75f);
            Circle(p, P(r, 0.68f, 0.34f), s * 0.14f);
            p.BeginPath();
            p.MoveTo(P(r, 0.44f, 0.86f));
            p.Arc(P(r, 0.68f, 0.86f), s * 0.25f, Angle.Degrees(180f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
            p.fillColor = c;
            Circle(p, P(r, 0.36f, 0.36f), s * 0.16f);
            p.BeginPath();
            p.MoveTo(P(r, 0.06f, 0.92f));
            p.Arc(P(r, 0.36f, 0.92f), s * 0.3f, Angle.Degrees(180f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill();
        }

        public static void Star(Painter2D p, Rect r, Color c)
        {
            var ctr = r.center; float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = -Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float rad = (i % 2 == 0 ? 0.47f : 0.2f) * s;
                var v = ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
        }

        public static void Camera(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.08f, 0.28f, 0.66f, 0.74f, 0.08f);
            p.Fill();
            Poly(p, r, 0.7f, 0.42f, 0.94f, 0.28f, 0.94f, 0.74f, 0.7f, 0.6f);
        }

        public static void Eye(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.05f, 0.5f));
            p.QuadraticCurveTo(P(r, 0.5f, 0.05f), P(r, 0.95f, 0.5f));
            p.QuadraticCurveTo(P(r, 0.5f, 0.95f), P(r, 0.05f, 0.5f));
            p.ClosePath();
            p.MoveTo(P(r, 0.5f, 0.5f) + new Vector2(s * 0.15f, 0f));
            p.Arc(P(r, 0.5f, 0.5f), s * 0.15f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        public static void Arrow(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            Poly(p, r, 0.5f, 0.05f, 0.88f, 0.92f, 0.5f, 0.7f, 0.12f, 0.92f);
        }

        public static void Boat(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.04f));
            p.QuadraticCurveTo(P(r, 0.82f, 0.25f), P(r, 0.78f, 0.55f));
            p.LineTo(P(r, 0.74f, 0.96f));
            p.LineTo(P(r, 0.26f, 0.96f));
            p.LineTo(P(r, 0.22f, 0.55f));
            p.QuadraticCurveTo(P(r, 0.18f, 0.25f), P(r, 0.5f, 0.04f));
            p.ClosePath();
            p.Fill();
        }

        /// Side view of a small boat (shop / inventory).
        public static void BoatSide(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.04f, 0.5f));
            p.LineTo(P(r, 0.96f, 0.5f));
            p.LineTo(P(r, 0.8f, 0.78f));
            p.LineTo(P(r, 0.18f, 0.78f));
            p.ClosePath();
            p.Fill();
            Poly(p, r, 0.42f, 0.5f, 0.42f, 0.24f, 0.66f, 0.24f, 0.74f, 0.5f);
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.06f, 0.06f, 0.9f, 0.3f, 0.86f, 0.5f, 0.9f, 0.7f, 0.86f, 0.94f, 0.9f);
        }

        public static void House(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            Poly(p, r, 0.5f, 0.1f, 0.92f, 0.48f, 0.8f, 0.48f, 0.8f, 0.9f, 0.2f, 0.9f, 0.2f, 0.48f, 0.08f, 0.48f);
        }

        public static void Plus(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.14f, 0.5f, 0.2f, 0.5f, 0.8f);
            Stroke(p, r, S(r) * 0.14f, 0.2f, 0.5f, 0.8f, 0.5f);
        }

        public static void Minus(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.14f, 0.2f, 0.5f, 0.8f, 0.5f);
        }

        public static void Warning(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.06f));
            p.LineTo(P(r, 0.96f, 0.9f));
            p.LineTo(P(r, 0.04f, 0.9f));
            p.ClosePath();
            // exclamation mark as a hole
            p.MoveTo(P(r, 0.45f, 0.36f)); p.LineTo(P(r, 0.55f, 0.36f)); p.LineTo(P(r, 0.53f, 0.64f)); p.LineTo(P(r, 0.47f, 0.64f)); p.ClosePath();
            p.MoveTo(P(r, 0.5f, 0.76f) + new Vector2(s * 0.055f, 0f));
            p.Arc(P(r, 0.5f, 0.76f), s * 0.055f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        public static void Coin(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            var ctr = r.center;
            p.fillColor = c;
            p.BeginPath();
            p.Arc(ctr, s * 0.44f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.MoveTo(ctr + new Vector2(s * 0.31f, 0f));
            p.Arc(ctr, s * 0.31f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
            FillRoundRect(p, r, 0.44f, 0.28f, 0.56f, 0.72f, 0.06f);
        }

        /// Three rising bubbles (air).
        public static void Bubbles(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Ring(p, P(r, 0.38f, 0.7f), s * 0.2f, s * 0.08f);
            Ring(p, P(r, 0.68f, 0.42f), s * 0.13f, s * 0.07f);
            Ring(p, P(r, 0.42f, 0.2f), s * 0.08f, s * 0.06f);
        }

        /// Down arrow with water lines (depth).
        public static void Depth(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.1f, 0.5f, 0.12f, 0.5f, 0.72f);
            Stroke(p, r, s * 0.1f, 0.28f, 0.52f, 0.5f, 0.76f, 0.72f, 0.52f);
            Stroke(p, r, s * 0.07f, 0.12f, 0.9f, 0.3f, 0.86f, 0.5f, 0.9f, 0.7f, 0.86f, 0.88f, 0.9f);
        }

        public static void Clock(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Ring(p, r.center, s * 0.4f, s * 0.09f);
            Stroke(p, r, s * 0.09f, 0.5f, 0.28f, 0.5f, 0.52f, 0.66f, 0.62f);
        }

        public static void Bell(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.16f, 0.74f));
            p.BezierCurveTo(P(r, 0.28f, 0.6f), P(r, 0.2f, 0.18f), P(r, 0.5f, 0.16f));
            p.BezierCurveTo(P(r, 0.8f, 0.18f), P(r, 0.72f, 0.6f), P(r, 0.84f, 0.74f));
            p.ClosePath();
            p.Fill();
            Circle(p, P(r, 0.5f, 0.84f), s * 0.08f);
        }

        // ------------------------------------------------------------------ app icons
        public static void Book(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.47f, 0.24f));
            p.QuadraticCurveTo(P(r, 0.3f, 0.14f), P(r, 0.08f, 0.2f));
            p.LineTo(P(r, 0.08f, 0.8f));
            p.QuadraticCurveTo(P(r, 0.3f, 0.74f), P(r, 0.47f, 0.84f));
            p.ClosePath();
            p.Fill();
            p.BeginPath();
            p.MoveTo(P(r, 0.53f, 0.24f));
            p.QuadraticCurveTo(P(r, 0.7f, 0.14f), P(r, 0.92f, 0.2f));
            p.LineTo(P(r, 0.92f, 0.8f));
            p.QuadraticCurveTo(P(r, 0.7f, 0.74f), P(r, 0.53f, 0.84f));
            p.ClosePath();
            p.Fill();
        }

        public static void Heli(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.22f, 0.5f));
            p.BezierCurveTo(P(r, 0.22f, 0.3f), P(r, 0.62f, 0.3f), P(r, 0.64f, 0.5f));
            p.BezierCurveTo(P(r, 0.62f, 0.7f), P(r, 0.22f, 0.7f), P(r, 0.22f, 0.5f));
            p.ClosePath();
            p.Fill();
            Poly(p, r, 0.6f, 0.44f, 0.92f, 0.46f, 0.92f, 0.52f, 0.6f, 0.56f);
            Poly(p, r, 0.86f, 0.34f, 0.93f, 0.34f, 0.93f, 0.52f, 0.86f, 0.52f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.06f, 0.08f, 0.24f, 0.78f, 0.24f);
            Stroke(p, r, s * 0.06f, 0.43f, 0.24f, 0.43f, 0.34f);
            Stroke(p, r, s * 0.05f, 0.18f, 0.8f, 0.66f, 0.8f);
            Stroke(p, r, s * 0.05f, 0.32f, 0.66f, 0.3f, 0.8f);
            Stroke(p, r, s * 0.05f, 0.52f, 0.66f, 0.54f, 0.8f);
        }

        public static void Play(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.06f, 0.18f, 0.94f, 0.82f, 0.18f);
            p.MoveTo(P(r, 0.42f, 0.34f)); p.LineTo(P(r, 0.66f, 0.5f)); p.LineTo(P(r, 0.42f, 0.66f)); p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        public static void Broadcast(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            var ctr = r.center;
            p.fillColor = c;
            Circle(p, ctr, s * 0.1f);
            p.strokeColor = c;
            p.lineWidth = s * 0.08f;
            p.lineCap = LineCap.Round;
            for (int k = 1; k <= 2; k++)
            {
                float rad = s * (0.12f + 0.15f * k);
                p.BeginPath(); p.Arc(ctr, rad, Angle.Degrees(-40f), Angle.Degrees(40f)); p.Stroke();
                p.BeginPath(); p.Arc(ctr, rad, Angle.Degrees(140f), Angle.Degrees(220f)); p.Stroke();
            }
        }

        public static void Chart(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.09f, 0.1f, 0.78f, 0.36f, 0.52f, 0.54f, 0.64f, 0.86f, 0.28f);
            p.fillColor = c;
            Poly(p, r, 0.92f, 0.18f, 0.92f, 0.44f, 0.68f, 0.24f);
            Stroke(p, r, s * 0.05f, 0.08f, 0.9f, 0.92f, 0.9f);
        }

        public static void Pin(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            var ctr = P(r, 0.5f, 0.4f);
            float R = s * 0.3f;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.94f));
            p.LineTo(ctr + new Vector2(Mathf.Cos(150f * Mathf.Deg2Rad), Mathf.Sin(150f * Mathf.Deg2Rad)) * R);
            p.Arc(ctr, R, Angle.Degrees(150f), Angle.Degrees(390f));
            p.ClosePath();
            p.MoveTo(ctr + new Vector2(R * 0.42f, 0f));
            p.Arc(ctr, R * 0.42f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        public static void Bubble(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.08f, 0.14f, 0.92f, 0.7f, 0.16f);
            p.Fill();
            Poly(p, r, 0.24f, 0.62f, 0.44f, 0.66f, 0.2f, 0.9f);
        }

        public static void Gear(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            var ctr = r.center;
            p.fillColor = c;
            p.BeginPath();
            const int teeth = 8;
            for (int i = 0; i < teeth * 4; i++)
            {
                float a = (i / (float)(teeth * 4)) * Mathf.PI * 2f;
                int phase = i % 4;
                float rad = (phase == 1 || phase == 2 ? 0.46f : 0.34f) * s;
                var v = ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.MoveTo(ctr + new Vector2(s * 0.14f, 0f));
            p.Arc(ctr, s * 0.14f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        public static void Fish(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.08f, 0.5f));
            p.BezierCurveTo(P(r, 0.25f, 0.18f), P(r, 0.6f, 0.2f), P(r, 0.72f, 0.5f));
            p.BezierCurveTo(P(r, 0.6f, 0.8f), P(r, 0.25f, 0.82f), P(r, 0.08f, 0.5f));
            p.ClosePath();
            p.MoveTo(P(r, 0.66f, 0.5f)); p.LineTo(P(r, 0.95f, 0.28f)); p.LineTo(P(r, 0.88f, 0.5f)); p.LineTo(P(r, 0.95f, 0.72f)); p.ClosePath();
            p.Fill();
            p.fillColor = new Color(0f, 0f, 0f, 0.55f * c.a);
            Circle(p, P(r, 0.24f, 0.45f), s * 0.045f);
        }

        /// Checklist (Aufgaben app).
        public static void Tasks(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.1f, 0.12f, 0.26f, 0.2f, 0.34f, 0.32f, 0.2f);
            Stroke(p, r, s * 0.1f, 0.12f, 0.56f, 0.2f, 0.64f, 0.32f, 0.5f);
            Stroke(p, r, s * 0.1f, 0.44f, 0.28f, 0.88f, 0.28f);
            Stroke(p, r, s * 0.1f, 0.44f, 0.58f, 0.88f, 0.58f);
            Stroke(p, r, s * 0.1f, 0.44f, 0.84f, 0.74f, 0.84f);
            p.fillColor = c;
            Circle(p, P(r, 0.21f, 0.84f), s * 0.06f);
        }

        /// Fish with a price tag (Fischmarkt app).
        public static void Market(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            Fish(p, new Rect(r.x, r.y + r.height * 0.14f, r.width * 0.86f, r.height * 0.62f), c);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.56f, 0.66f));
            p.LineTo(P(r, 0.78f, 0.6f));
            p.LineTo(P(r, 0.96f, 0.78f));
            p.LineTo(P(r, 0.8f, 0.96f));
            p.LineTo(P(r, 0.62f, 0.88f));
            p.ClosePath();
            p.MoveTo(P(r, 0.7f, 0.72f) + new Vector2(s * 0.035f, 0f));
            p.Arc(P(r, 0.7f, 0.72f), s * 0.035f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        /// Photo frame with mountain and sun (Galerie app).
        public static void Image(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.08f, 0.16f, 0.92f, 0.84f, 0.12f);
            RoundRect(p, r, 0.16f, 0.24f, 0.84f, 0.76f, 0.06f);
            p.Fill(FillRule.OddEven);
            Poly(p, r, 0.18f, 0.74f, 0.4f, 0.46f, 0.56f, 0.64f, 0.66f, 0.54f, 0.82f, 0.74f);
            Circle(p, P(r, 0.66f, 0.38f), s * 0.07f);
        }

        public static void Speaker(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.1f, 0.38f, 0.28f, 0.38f, 0.5f, 0.18f, 0.5f, 0.82f, 0.28f, 0.62f, 0.1f, 0.62f);
            p.strokeColor = c;
            p.lineWidth = s * 0.08f;
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.Arc(P(r, 0.5f, 0.5f), s * 0.2f, Angle.Degrees(-45f), Angle.Degrees(45f)); p.Stroke();
            p.BeginPath(); p.Arc(P(r, 0.5f, 0.5f), s * 0.36f, Angle.Degrees(-45f), Angle.Degrees(45f)); p.Stroke();
        }

        public static void Mic(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.36f, 0.08f, 0.64f, 0.6f, 0.14f);
            p.strokeColor = c;
            p.lineWidth = s * 0.08f;
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.Arc(P(r, 0.5f, 0.44f), s * 0.24f, Angle.Degrees(0f), Angle.Degrees(180f)); p.Stroke();
            Stroke(p, r, s * 0.08f, 0.5f, 0.68f, 0.5f, 0.86f);
            Stroke(p, r, s * 0.08f, 0.34f, 0.88f, 0.66f, 0.88f);
        }

        public static void MicOff(Painter2D p, Rect r, Color c)
        {
            Mic(p, r, Al(c, 0.6f));
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.1f, 0.14f, 0.12f, 0.86f, 0.88f);
        }

        public static void Trash(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.22f, 0.3f, 0.78f, 0.3f, 0.72f, 0.9f, 0.28f, 0.9f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.08f, 0.14f, 0.2f, 0.86f, 0.2f);
            Stroke(p, r, s * 0.08f, 0.4f, 0.2f, 0.42f, 0.1f, 0.58f, 0.1f, 0.6f, 0.2f);
        }

        /// Backpack (inventory).
        public static void Bag(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.18f, 0.24f, 0.82f, 0.92f, 0.22f);
            p.strokeColor = c;
            p.lineWidth = s * 0.08f;
            p.BeginPath(); p.Arc(P(r, 0.5f, 0.26f), s * 0.16f, Angle.Degrees(180f), Angle.Degrees(360f)); p.Stroke();
            p.fillColor = Al(Color.white, 0.45f * c.a);
            FillRoundRect(p, r, 0.3f, 0.56f, 0.7f, 0.76f, 0.06f);
        }

        /// Flag on a pole (waypoint).
        public static void Flag(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.08f, 0.24f, 0.08f, 0.24f, 0.94f);
            p.fillColor = c;
            Poly(p, r, 0.28f, 0.1f, 0.84f, 0.26f, 0.28f, 0.46f);
        }

        public static void Keyboard(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.06f, 0.24f, 0.94f, 0.78f, 0.1f);
            for (int row = 0; row < 2; row++)
                for (int k = 0; k < 5; k++)
                {
                    float x = 0.14f + k * 0.15f, y = 0.32f + row * 0.15f;
                    RoundRect(p, r, x, y, x + 0.1f, y + 0.1f, 0.02f);
                }
            RoundRect(p, r, 0.26f, 0.62f, 0.74f, 0.7f, 0.02f);
            p.Fill(FillRule.OddEven);
            _ = s;
        }

        public static void Monitor(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.06f, 0.14f, 0.94f, 0.7f, 0.08f);
            RoundRect(p, r, 0.13f, 0.21f, 0.87f, 0.63f, 0.03f);
            p.Fill(FillRule.OddEven);
            Poly(p, r, 0.42f, 0.7f, 0.58f, 0.7f, 0.62f, 0.86f, 0.38f, 0.86f);
            FillRoundRect(p, r, 0.28f, 0.84f, 0.72f, 0.92f, 0.04f);
        }

        /// Gamepad-free "game" icon: a die (general settings).
        public static void Sliders(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.08f, 0.12f, 0.28f, 0.88f, 0.28f);
            Stroke(p, r, s * 0.08f, 0.12f, 0.5f, 0.88f, 0.5f);
            Stroke(p, r, s * 0.08f, 0.12f, 0.72f, 0.88f, 0.72f);
            p.fillColor = c;
            Circle(p, P(r, 0.66f, 0.28f), s * 0.1f);
            Circle(p, P(r, 0.32f, 0.5f), s * 0.1f);
            Circle(p, P(r, 0.56f, 0.72f), s * 0.1f);
        }

        public static void Globe(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Ring(p, r.center, s * 0.4f, s * 0.08f);
            p.lineWidth = s * 0.07f;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.1f));
            p.BezierCurveTo(P(r, 0.24f, 0.3f), P(r, 0.24f, 0.7f), P(r, 0.5f, 0.9f));
            p.BezierCurveTo(P(r, 0.76f, 0.7f), P(r, 0.76f, 0.3f), P(r, 0.5f, 0.1f));
            p.Stroke();
            Stroke(p, r, s * 0.07f, 0.12f, 0.5f, 0.88f, 0.5f);
        }

        public static void Upload(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.11f, 0.5f, 0.66f, 0.5f, 0.16f);
            Stroke(p, r, s * 0.11f, 0.28f, 0.38f, 0.5f, 0.16f, 0.72f, 0.38f);
            Stroke(p, r, s * 0.11f, 0.16f, 0.66f, 0.16f, 0.86f, 0.84f, 0.86f, 0.84f, 0.66f);
        }

        public static void Tag(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.12f, 0.16f));
            p.LineTo(P(r, 0.52f, 0.12f));
            p.LineTo(P(r, 0.9f, 0.52f));
            p.LineTo(P(r, 0.52f, 0.9f));
            p.LineTo(P(r, 0.12f, 0.52f));
            p.ClosePath();
            p.MoveTo(P(r, 0.32f, 0.32f) + new Vector2(s * 0.07f, 0f));
            p.Arc(P(r, 0.32f, 0.32f), s * 0.07f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
            p.Fill(FillRule.OddEven);
        }

        public static void Shell(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.86f));
            p.LineTo(P(r, 0.1f, 0.42f));
            p.BezierCurveTo(P(r, 0.14f, 0.1f), P(r, 0.86f, 0.1f), P(r, 0.9f, 0.42f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Al(Color.white, 0.6f * c.a);
            for (int i = 0; i < 4; i++)
            {
                float x = 0.24f + i * 0.17f;
                Stroke(p, r, s * 0.05f, 0.5f, 0.8f, x, 0.26f);
            }
        }

        public static void Bottle(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.3f, 0.36f, 0.7f, 0.92f, 0.12f);
            p.Fill();
            FillRoundRect(p, r, 0.42f, 0.1f, 0.58f, 0.4f, 0.04f);
            p.fillColor = Al(Color.white, 0.7f * c.a);
            FillRoundRect(p, r, 0.38f, 0.5f, 0.62f, 0.78f, 0.03f);
        }

        public static void Page(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.2f, 0.08f, 0.62f, 0.08f, 0.82f, 0.28f, 0.82f, 0.92f, 0.2f, 0.92f);
            p.strokeColor = Al(Color.white, 0.7f * c.a);
            Stroke(p, r, s * 0.05f, 0.32f, 0.42f, 0.7f, 0.42f);
            Stroke(p, r, s * 0.05f, 0.32f, 0.56f, 0.7f, 0.56f);
            Stroke(p, r, s * 0.05f, 0.32f, 0.7f, 0.58f, 0.7f);
        }

        public static void Chest(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.1f, 0.44f, 0.9f, 0.88f, 0.05f);
            p.BeginPath();
            p.MoveTo(P(r, 0.1f, 0.42f));
            p.BezierCurveTo(P(r, 0.1f, 0.14f), P(r, 0.9f, 0.14f), P(r, 0.9f, 0.42f));
            p.ClosePath();
            p.Fill();
            p.fillColor = Al(Color.white, 0.7f * c.a);
            FillRoundRect(p, r, 0.44f, 0.38f, 0.56f, 0.56f, 0.03f);
            _ = s;
        }

        public static void Question(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            p.lineWidth = s * 0.12f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.Arc(P(r, 0.5f, 0.34f), s * 0.2f, Angle.Degrees(180f), Angle.Degrees(400f));
            p.LineTo(P(r, 0.5f, 0.64f));
            p.Stroke();
            p.fillColor = c;
            Circle(p, P(r, 0.5f, 0.84f), s * 0.07f);
        }

        public static void Hand(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            // mitten: palm + finger block + thumb
            FillRoundRect(p, r, 0.3f, 0.12f, 0.78f, 0.8f, 0.24f);
            p.BeginPath();
            p.MoveTo(P(r, 0.32f, 0.5f));
            p.BezierCurveTo(P(r, 0.12f, 0.38f), P(r, 0.08f, 0.5f), P(r, 0.32f, 0.72f));
            p.ClosePath();
            p.Fill();
            FillRoundRect(p, r, 0.34f, 0.76f, 0.74f, 0.94f, 0.04f);
            _ = s;
        }

        public static void Mask(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            RoundRect(p, r, 0.08f, 0.28f, 0.92f, 0.72f, 0.2f);
            RoundRect(p, r, 0.16f, 0.36f, 0.46f, 0.64f, 0.1f);
            RoundRect(p, r, 0.54f, 0.36f, 0.84f, 0.64f, 0.1f);
            p.Fill(FillRule.OddEven);
            p.strokeColor = c;
            Stroke(p, r, s * 0.07f, 0.5f, 0.28f, 0.5f, 0.12f, 0.86f, 0.12f);
        }

        public static void SharkFin(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.22f, 0.72f));
            p.BezierCurveTo(P(r, 0.42f, 0.5f), P(r, 0.5f, 0.24f), P(r, 0.52f, 0.08f));
            p.BezierCurveTo(P(r, 0.66f, 0.3f), P(r, 0.74f, 0.52f), P(r, 0.82f, 0.72f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = c;
            Stroke(p, r, s * 0.08f, 0.06f, 0.86f, 0.28f, 0.78f, 0.5f, 0.86f, 0.72f, 0.78f, 0.94f, 0.86f);
        }

        public static void Wave(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.09f, 0.06f, 0.4f, 0.28f, 0.3f, 0.5f, 0.4f, 0.72f, 0.3f, 0.94f, 0.4f);
            Stroke(p, r, s * 0.09f, 0.06f, 0.66f, 0.28f, 0.56f, 0.5f, 0.66f, 0.72f, 0.56f, 0.94f, 0.66f);
        }

        public static void Sub(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.08f, 0.4f, 0.84f, 0.78f, 0.4f);
            FillRoundRect(p, r, 0.4f, 0.24f, 0.6f, 0.44f, 0.04f);
            Poly(p, r, 0.82f, 0.5f, 0.96f, 0.4f, 0.96f, 0.78f, 0.82f, 0.68f);
            p.fillColor = Al(Color.white, 0.7f * c.a);
            Circle(p, P(r, 0.32f, 0.59f), s * 0.07f);
            Circle(p, P(r, 0.56f, 0.59f), s * 0.07f);
        }

        public static void Tank(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            FillRoundRect(p, r, 0.3f, 0.22f, 0.7f, 0.94f, 0.2f);
            FillRoundRect(p, r, 0.42f, 0.08f, 0.58f, 0.24f, 0.03f);
            FillRoundRect(p, r, 0.34f, 0.06f, 0.66f, 0.12f, 0.03f);
            p.fillColor = Al(Color.white, 0.55f * c.a);
            FillRoundRect(p, r, 0.3f, 0.4f, 0.7f, 0.48f, 0.0f);
        }

        public static void Fins(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.12f, 0.18f));
            p.LineTo(P(r, 0.34f, 0.14f));
            p.LineTo(P(r, 0.46f, 0.9f));
            p.LineTo(P(r, 0.08f, 0.92f));
            p.ClosePath();
            p.Fill();
            p.BeginPath();
            p.MoveTo(P(r, 0.6f, 0.12f));
            p.LineTo(P(r, 0.82f, 0.16f));
            p.LineTo(P(r, 0.94f, 0.9f));
            p.LineTo(P(r, 0.56f, 0.88f));
            p.ClosePath();
            p.Fill();
        }

        public static void Lamp(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            FillRoundRect(p, r, 0.1f, 0.4f, 0.56f, 0.62f, 0.06f);
            Poly(p, r, 0.54f, 0.34f, 0.72f, 0.28f, 0.72f, 0.74f, 0.54f, 0.68f);
            p.fillColor = Al(c, 0.35f);
            Poly(p, r, 0.74f, 0.32f, 0.98f, 0.14f, 0.98f, 0.86f, 0.74f, 0.7f);
        }

        public static void Rod(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.08f, 0.14f, 0.9f, 0.86f, 0.1f);
            p.lineWidth = s * 0.03f;
            p.BeginPath();
            p.MoveTo(P(r, 0.86f, 0.1f));
            p.BezierCurveTo(P(r, 0.92f, 0.4f), P(r, 0.9f, 0.6f), P(r, 0.88f, 0.76f));
            p.Stroke();
            p.fillColor = c;
            Circle(p, P(r, 0.3f, 0.66f), s * 0.11f);
        }

        // ------------------------------------------------------------------ multi-colored item icons (hotbar)
        static readonly Color ItemOutline = new Color(0.08f, 0.09f, 0.16f, 0.85f);

        /// Low-poly camcorder: yellow body, dark lens and handle (art bible §1.13 colors).
        public static void ItemCamera(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            Color body = Al(UIPalette.Sun, a), dark = Al(UIPalette.Rgb(0x2E3140), a), lens = Al(UIPalette.Rgb(0x6FB9E8), a), ol = Al(ItemOutline, a);
            float ow = s * 0.06f;
            // outline pass
            p.strokeColor = ol; p.lineWidth = ow; p.lineJoin = LineJoin.Round;
            p.BeginPath(); RoundRect(p, r, 0.12f, 0.36f, 0.7f, 0.8f, 0.08f); p.Stroke();
            p.BeginPath(); RoundRect(p, r, 0.68f, 0.44f, 0.9f, 0.72f, 0.06f); p.Stroke();
            p.BeginPath(); RoundRect(p, r, 0.26f, 0.2f, 0.56f, 0.32f, 0.04f); p.Stroke();
            // fill pass
            p.fillColor = dark;
            FillRoundRect(p, r, 0.26f, 0.2f, 0.56f, 0.32f, 0.04f);
            FillRoundRect(p, r, 0.3f, 0.28f, 0.36f, 0.4f, 0.0f);
            FillRoundRect(p, r, 0.46f, 0.28f, 0.52f, 0.4f, 0.0f);
            p.fillColor = body;
            FillRoundRect(p, r, 0.12f, 0.36f, 0.7f, 0.8f, 0.08f);
            p.fillColor = dark;
            FillRoundRect(p, r, 0.68f, 0.44f, 0.9f, 0.72f, 0.06f);
            p.fillColor = lens;
            Circle(p, P(r, 0.85f, 0.58f), s * 0.06f);
            p.fillColor = Al(UIPalette.Live, a);
            Circle(p, P(r, 0.22f, 0.46f), s * 0.035f);
            p.fillColor = Al(UIPalette.Darken(UIPalette.Sun, 0.18f), a);
            FillRoundRect(p, r, 0.12f, 0.68f, 0.7f, 0.8f, 0.04f);
        }

        /// Duct-taped flashlight: yellow body, dark head, bright lens.
        public static void ItemFlashlight(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            Color body = Al(UIPalette.Sun, a), dark = Al(UIPalette.Rgb(0x2E3140), a), glass = Al(UIPalette.Rgb(0xFFF1C9), a), tape = Al(UIPalette.Rgb(0x8E949C), a), ol = Al(ItemOutline, a);
            // rotate the lamp 35° (pointing up-right) by drawing in a local frame
            Vector2 ctr = r.center;
            float ang = -35f * Mathf.Deg2Rad, cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
            Vector2 L(float x, float y)
            {
                float lx = (x - 0.5f) * s, ly = (y - 0.5f) * s;
                return ctr + new Vector2(lx * cs - ly * sn, lx * sn + ly * cs);
            }
            void Quad(float x0, float y0, float x1, float y1, Color col, bool outline)
            {
                p.BeginPath();
                p.MoveTo(L(x0, y0)); p.LineTo(L(x1, y0)); p.LineTo(L(x1, y1)); p.LineTo(L(x0, y1)); p.ClosePath();
                if (outline) { p.strokeColor = ol; p.lineWidth = s * 0.06f; p.lineJoin = LineJoin.Round; p.Stroke(); }
                else { p.fillColor = col; p.Fill(); }
            }
            Quad(0.08f, 0.4f, 0.66f, 0.6f, body, true);
            Quad(0.62f, 0.33f, 0.88f, 0.67f, dark, true);
            Quad(0.08f, 0.4f, 0.66f, 0.6f, body, false);
            Quad(0.3f, 0.39f, 0.4f, 0.61f, tape, false);
            Quad(0.62f, 0.33f, 0.88f, 0.67f, dark, false);
            Quad(0.84f, 0.37f, 0.92f, 0.63f, glass, false);
        }

        /// Fishing rod: dark rod with a blue band, grey reel, white line.
        public static void ItemRod(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            Color rod = Al(UIPalette.Rgb(0x2E2A40), a), band = Al(UIPalette.Rgb(0x3D7BC4), a), reel = Al(UIPalette.Rgb(0x8E949C), a), line = Al(Color.white, 0.9f * a), ol = Al(ItemOutline, a);
            p.lineCap = LineCap.Round;
            // outline
            p.strokeColor = ol;
            p.lineWidth = s * 0.13f;
            p.BeginPath(); p.MoveTo(P(r, 0.14f, 0.88f)); p.LineTo(P(r, 0.88f, 0.1f)); p.Stroke();
            p.fillColor = ol;
            Circle(p, P(r, 0.32f, 0.7f), s * 0.13f);
            // rod
            p.strokeColor = rod;
            p.lineWidth = s * 0.075f;
            p.BeginPath(); p.MoveTo(P(r, 0.14f, 0.88f)); p.LineTo(P(r, 0.88f, 0.1f)); p.Stroke();
            p.strokeColor = band;
            p.lineWidth = s * 0.085f;
            p.BeginPath(); p.MoveTo(P(r, 0.42f, 0.585f)); p.LineTo(P(r, 0.48f, 0.52f)); p.Stroke();
            // line
            p.strokeColor = line;
            p.lineWidth = s * 0.025f;
            p.BeginPath();
            p.MoveTo(P(r, 0.88f, 0.1f));
            p.BezierCurveTo(P(r, 0.93f, 0.36f), P(r, 0.93f, 0.6f), P(r, 0.9f, 0.8f));
            p.Stroke();
            // reel
            p.fillColor = reel;
            Circle(p, P(r, 0.32f, 0.7f), s * 0.095f);
            p.fillColor = rod;
            Circle(p, P(r, 0.32f, 0.7f), s * 0.035f);
        }

        /// Icon for a hotbar item (null for None).
        public static UIGlyph.DrawFn ForItem(HoldItem i)
        {
            switch (i)
            {
                case HoldItem.Camera: return ItemCamera;
                case HoldItem.Flashlight: return ItemFlashlight;
                case HoldItem.FishingRod: return ItemRod;
                default: return null;
            }
        }

        /// Single-color icon of a gear slot (shop / inventory).
        public static UIGlyph.DrawFn ForSlot(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Tank: return Tank;
                case GearSlot.Fins: return Fins;
                case GearSlot.Lamp: return Lamp;
                case GearSlot.Camera: return Camera;
                case GearSlot.Boat: return BoatSide;
                case GearSlot.Submarine: return Sub;
                default: return Rod;
            }
        }

        public static UIGlyph.DrawFn ForCollectible(CollectibleKind k)
        {
            switch (k)
            {
                case CollectibleKind.LogbookPage: return Page;
                case CollectibleKind.GoldenShell: return Shell;
                case CollectibleKind.BottleMessage: return Bottle;
                case CollectibleKind.Treasure: return Chest;
                default: return Question;
            }
        }

        public static UIGlyph.DrawFn ForDifficulty(Difficulty d)
        {
            switch (DifficultySettings.Normalize(d))
            {
                case Difficulty.Snorkeler: return Sun;
                case Difficulty.OpenWater: return Mask;
                default: return SharkFin;
            }
        }
    }
}
