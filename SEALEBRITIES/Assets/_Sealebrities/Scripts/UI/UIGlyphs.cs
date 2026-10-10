using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Vector icons drawn with Painter2D. Every function draws into rect r (y down) with color c.
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
            float rad = S(r) * radius01;
            p.MoveTo(new Vector2(a.x + rad, a.y));
            p.ArcTo(new Vector2(b.x, a.y), new Vector2(b.x, b.y), rad);
            p.ArcTo(new Vector2(b.x, b.y), new Vector2(a.x, b.y), rad);
            p.ArcTo(new Vector2(a.x, b.y), new Vector2(a.x, a.y), rad);
            p.ArcTo(new Vector2(a.x, a.y), new Vector2(b.x, a.y), rad);
            p.ClosePath();
        }

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
            // cabin
            p.BeginPath();
            p.MoveTo(P(r, 0.22f, 0.5f));
            p.BezierCurveTo(P(r, 0.22f, 0.3f), P(r, 0.62f, 0.3f), P(r, 0.64f, 0.5f));
            p.BezierCurveTo(P(r, 0.62f, 0.7f), P(r, 0.22f, 0.7f), P(r, 0.22f, 0.5f));
            p.ClosePath();
            p.Fill();
            // tail boom
            Poly(p, r, 0.6f, 0.44f, 0.92f, 0.46f, 0.92f, 0.52f, 0.6f, 0.56f);
            Poly(p, r, 0.86f, 0.34f, 0.93f, 0.34f, 0.93f, 0.52f, 0.86f, 0.52f);
            p.strokeColor = c;
            // rotor + mast
            Stroke(p, r, s * 0.06f, 0.08f, 0.24f, 0.78f, 0.24f);
            Stroke(p, r, s * 0.06f, 0.43f, 0.24f, 0.43f, 0.34f);
            // skids
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
            p.fillColor = new Color(0f, 0f, 0f, 0.55f);
            Circle(p, P(r, 0.24f, 0.45f), s * 0.045f);
        }
    }
}
