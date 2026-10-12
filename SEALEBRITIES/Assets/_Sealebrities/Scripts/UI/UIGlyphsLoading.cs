using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 loading screen icons (tips + ocean facts): light bulb, octopus, jellyfish, coral, manta, dolphin, crab,
    /// starfish. Single colour (c), drawn into rect r (y down) like every UIGlyphs function; small details (eyes) use a
    /// dark ink so they read on a white glyph.
    public static partial class UIGlyphs
    {
        static Color LoadingInk(Color c) => new Color(0x1B / 255f, 0x24 / 255f, 0x33 / 255f, 0.8f * c.a);

        /// "Tip" icon: a light bulb with three short rays.
        public static void LoadingBulb(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.5f, 0.42f), s * 0.25f);
            Poly(p, r, 0.35f, 0.52f, 0.65f, 0.52f, 0.6f, 0.69f, 0.4f, 0.69f);
            FillRoundRect(p, r, 0.38f, 0.72f, 0.62f, 0.79f, 0.03f);
            FillRoundRect(p, r, 0.42f, 0.82f, 0.58f, 0.89f, 0.03f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.065f, 0.5f, 0.03f, 0.5f, 0.09f);
            Stroke(p, r, s * 0.065f, 0.14f, 0.2f, 0.19f, 0.25f);
            Stroke(p, r, s * 0.065f, 0.86f, 0.2f, 0.81f, 0.25f);
            // filament shine
            p.strokeColor = LoadingInk(c);
            Stroke(p, r, s * 0.04f, 0.44f, 0.5f, 0.47f, 0.4f, 0.53f, 0.4f, 0.56f, 0.5f);
        }

        public static void LoadingOctopus(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.24f, 0.56f));
            p.BezierCurveTo(P(r, 0.16f, 0.06f), P(r, 0.84f, 0.06f), P(r, 0.76f, 0.56f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = c;
            p.lineWidth = s * 0.085f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(r, 0.3f, 0.52f));
            p.BezierCurveTo(P(r, 0.24f, 0.7f), P(r, 0.08f, 0.7f), P(r, 0.13f, 0.86f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(P(r, 0.43f, 0.55f));
            p.BezierCurveTo(P(r, 0.41f, 0.72f), P(r, 0.29f, 0.8f), P(r, 0.36f, 0.93f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(P(r, 0.57f, 0.55f));
            p.BezierCurveTo(P(r, 0.59f, 0.72f), P(r, 0.71f, 0.8f), P(r, 0.64f, 0.93f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(P(r, 0.7f, 0.52f));
            p.BezierCurveTo(P(r, 0.76f, 0.7f), P(r, 0.92f, 0.7f), P(r, 0.87f, 0.86f));
            p.Stroke();
            p.fillColor = LoadingInk(c);
            Circle(p, P(r, 0.41f, 0.38f), s * 0.045f);
            Circle(p, P(r, 0.59f, 0.38f), s * 0.045f);
        }

        public static void LoadingJelly(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.15f, 0.5f));
            p.BezierCurveTo(P(r, 0.15f, 0.06f), P(r, 0.85f, 0.06f), P(r, 0.85f, 0.5f));
            p.LineTo(P(r, 0.74f, 0.45f));
            p.LineTo(P(r, 0.62f, 0.5f));
            p.LineTo(P(r, 0.5f, 0.45f));
            p.LineTo(P(r, 0.38f, 0.5f));
            p.LineTo(P(r, 0.26f, 0.45f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = c;
            p.lineWidth = s * 0.055f;
            p.lineCap = LineCap.Round;
            for (int i = 0; i < 3; i++)
            {
                float x = 0.32f + i * 0.18f;
                p.BeginPath();
                p.MoveTo(P(r, x, 0.5f));
                p.BezierCurveTo(P(r, x - 0.07f, 0.62f), P(r, x + 0.07f, 0.74f), P(r, x - 0.02f, 0.92f));
                p.Stroke();
            }
        }

        /// Branching coral on a little rock.
        public static void LoadingCoral(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.12f, 0.5f, 0.88f, 0.5f, 0.6f, 0.36f, 0.42f, 0.34f, 0.2f);
            Stroke(p, r, s * 0.12f, 0.5f, 0.66f, 0.66f, 0.46f, 0.68f, 0.24f);
            Stroke(p, r, s * 0.1f, 0.37f, 0.44f, 0.2f, 0.34f, 0.18f, 0.18f);
            Stroke(p, r, s * 0.1f, 0.66f, 0.48f, 0.82f, 0.4f, 0.84f, 0.26f);
            Stroke(p, r, s * 0.1f, 0.5f, 0.6f, 0.51f, 0.32f);
            p.fillColor = c;
            FillRoundRect(p, r, 0.28f, 0.84f, 0.72f, 0.94f, 0.05f);
        }

        /// Manta ray seen from above.
        public static void LoadingRay(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.3f));
            p.BezierCurveTo(P(r, 0.62f, 0.3f), P(r, 0.8f, 0.36f), P(r, 0.97f, 0.5f));
            p.BezierCurveTo(P(r, 0.78f, 0.56f), P(r, 0.62f, 0.66f), P(r, 0.5f, 0.68f));
            p.BezierCurveTo(P(r, 0.38f, 0.66f), P(r, 0.22f, 0.56f), P(r, 0.03f, 0.5f));
            p.BezierCurveTo(P(r, 0.2f, 0.36f), P(r, 0.38f, 0.3f), P(r, 0.5f, 0.3f));
            p.ClosePath();
            p.Fill();
            Poly(p, r, 0.43f, 0.32f, 0.4f, 0.19f, 0.48f, 0.3f);
            Poly(p, r, 0.57f, 0.32f, 0.6f, 0.19f, 0.52f, 0.3f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.045f, 0.5f, 0.66f, 0.5f, 0.95f);
        }

        /// Dolphin, side view, facing left.
        public static void LoadingDolphin(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.05f, 0.57f));
            p.LineTo(P(r, 0.18f, 0.5f));
            p.BezierCurveTo(P(r, 0.3f, 0.3f), P(r, 0.6f, 0.28f), P(r, 0.78f, 0.44f));
            p.LineTo(P(r, 0.93f, 0.31f));
            p.LineTo(P(r, 0.88f, 0.5f));
            p.LineTo(P(r, 0.96f, 0.65f));
            p.LineTo(P(r, 0.78f, 0.54f));
            p.BezierCurveTo(P(r, 0.6f, 0.67f), P(r, 0.32f, 0.69f), P(r, 0.18f, 0.61f));
            p.ClosePath();
            p.Fill();
            Poly(p, r, 0.43f, 0.33f, 0.52f, 0.15f, 0.59f, 0.34f);
            Poly(p, r, 0.33f, 0.6f, 0.41f, 0.76f, 0.47f, 0.62f);
            p.fillColor = LoadingInk(c);
            Circle(p, P(r, 0.25f, 0.49f), s * 0.035f);
        }

        public static void LoadingCrab(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            // legs
            Stroke(p, r, s * 0.05f, 0.3f, 0.62f, 0.14f, 0.7f, 0.1f, 0.82f);
            Stroke(p, r, s * 0.05f, 0.33f, 0.66f, 0.22f, 0.78f, 0.2f, 0.9f);
            Stroke(p, r, s * 0.05f, 0.7f, 0.62f, 0.86f, 0.7f, 0.9f, 0.82f);
            Stroke(p, r, s * 0.05f, 0.67f, 0.66f, 0.78f, 0.78f, 0.8f, 0.9f);
            // arms
            Stroke(p, r, s * 0.065f, 0.32f, 0.5f, 0.2f, 0.4f, 0.17f, 0.32f);
            Stroke(p, r, s * 0.065f, 0.68f, 0.5f, 0.8f, 0.4f, 0.83f, 0.32f);
            // eye stalks
            Stroke(p, r, s * 0.04f, 0.43f, 0.45f, 0.41f, 0.33f);
            Stroke(p, r, s * 0.04f, 0.57f, 0.45f, 0.59f, 0.33f);
            p.fillColor = c;
            Circle(p, P(r, 0.41f, 0.31f), s * 0.045f);
            Circle(p, P(r, 0.59f, 0.31f), s * 0.045f);
            // claws (a notch makes the pincer)
            Circle(p, P(r, 0.16f, 0.25f), s * 0.1f);
            Circle(p, P(r, 0.84f, 0.25f), s * 0.1f);
            // body
            p.BeginPath();
            p.MoveTo(P(r, 0.22f, 0.6f));
            p.BezierCurveTo(P(r, 0.24f, 0.36f), P(r, 0.76f, 0.36f), P(r, 0.78f, 0.6f));
            p.BezierCurveTo(P(r, 0.7f, 0.74f), P(r, 0.3f, 0.74f), P(r, 0.22f, 0.6f));
            p.ClosePath();
            p.Fill();
            p.fillColor = LoadingInk(c);
            Poly(p, r, 0.1f, 0.16f, 0.17f, 0.25f, 0.08f, 0.24f);
            Poly(p, r, 0.9f, 0.16f, 0.83f, 0.25f, 0.92f, 0.24f);
        }

        public static void LoadingStarfish(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            Vector2 ctr = P(r, 0.5f, 0.54f);
            float outer = s * 0.42f, inner = s * 0.19f;
            p.fillColor = c;
            p.strokeColor = c;
            p.lineWidth = s * 0.09f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float a = (-90f + i * 36f) * Mathf.Deg2Rad;
                float rad = (i & 1) == 0 ? outer : inner;
                var v = ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rad;
                if (i == 0) p.MoveTo(v);
                else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
            p.Stroke();
            p.fillColor = LoadingInk(c);
            Circle(p, ctr, s * 0.035f);
            for (int i = 0; i < 5; i++)
            {
                float a = (-90f + i * 72f) * Mathf.Deg2Rad;
                Circle(p, ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s * 0.17f, s * 0.025f);
            }
        }
    }
}
