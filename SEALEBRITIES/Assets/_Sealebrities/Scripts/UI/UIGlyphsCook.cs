using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 UI (GDD §19 „FinTube-Studio und Handy: mehr Funktionen“, §20 Q33/Q34): icons of the cookbook app (where
    /// you cook: campfire, the hut grill, the hermits' fire, the water filter; palm / fruit tree sources; the curry bowl)
    /// and of the FinTube Studio (comments, move up / down, the upload-streak flame). Single-colour icons use c; the
    /// curry bowl is multi-coloured like the other Item* icons (c.a = overall opacity).
    public static partial class UIGlyphs
    {
        /// Closed ellipse path (4 cubic segments) – filled / stroked by the caller.
        static void EllipsePath(Painter2D p, Vector2 c, float rx, float ry)
        {
            const float k = 0.5523f;
            p.BeginPath();
            p.MoveTo(new Vector2(c.x + rx, c.y));
            p.BezierCurveTo(new Vector2(c.x + rx, c.y + ry * k), new Vector2(c.x + rx * k, c.y + ry), new Vector2(c.x, c.y + ry));
            p.BezierCurveTo(new Vector2(c.x - rx * k, c.y + ry), new Vector2(c.x - rx, c.y + ry * k), new Vector2(c.x - rx, c.y));
            p.BezierCurveTo(new Vector2(c.x - rx, c.y - ry * k), new Vector2(c.x - rx * k, c.y - ry), new Vector2(c.x, c.y - ry));
            p.BezierCurveTo(new Vector2(c.x + rx * k, c.y - ry), new Vector2(c.x + rx, c.y - ry * k), new Vector2(c.x + rx, c.y));
            p.ClosePath();
        }

        static void SteamLine(Painter2D p, Rect r, float x, float y0, float y1, float width)
        {
            p.lineWidth = width;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(r, x, y0));
            float m = (y0 + y1) * 0.5f;
            p.BezierCurveTo(P(r, x - 0.06f, (y0 + m) * 0.5f), P(r, x + 0.06f, (m + y1) * 0.5f), P(r, x, y1));
            p.Stroke();
        }

        // ------------------------------------------------------------------ cookbook (single colour)
        /// The cookbook app: a book with a spoon and a fork on the cover.
        public static void CookBook(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            p.lineWidth = s * 0.075f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            RoundRect(p, r, 0.18f, 0.1f, 0.84f, 0.9f, 0.09f);
            p.Stroke();
            Stroke(p, r, s * 0.075f, 0.31f, 0.12f, 0.31f, 0.88f);
            // spoon
            p.fillColor = c;
            EllipsePath(p, P(r, 0.5f, 0.34f), s * 0.075f, s * 0.1f);
            p.Fill();
            Stroke(p, r, s * 0.055f, 0.5f, 0.42f, 0.5f, 0.76f);
            // fork
            Stroke(p, r, s * 0.04f, 0.63f, 0.22f, 0.63f, 0.38f);
            Stroke(p, r, s * 0.04f, 0.69f, 0.22f, 0.69f, 0.38f);
            Stroke(p, r, s * 0.04f, 0.75f, 0.22f, 0.75f, 0.38f);
            Stroke(p, r, s * 0.05f, 0.63f, 0.38f, 0.69f, 0.44f, 0.75f, 0.38f);
            Stroke(p, r, s * 0.055f, 0.69f, 0.44f, 0.69f, 0.76f);
        }

        /// A cooking pot with steam.
        public static void CookPot(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.2f, 0.48f, 0.8f, 0.88f, 0.12f);
            FillRoundRect(p, r, 0.13f, 0.42f, 0.87f, 0.52f, 0.04f);
            FillRoundRect(p, r, 0.05f, 0.5f, 0.17f, 0.58f, 0.03f);
            FillRoundRect(p, r, 0.83f, 0.5f, 0.95f, 0.58f, 0.03f);
            p.strokeColor = c;
            SteamLine(p, r, 0.34f, 0.35f, 0.1f, s * 0.055f);
            SteamLine(p, r, 0.5f, 0.35f, 0.06f, s * 0.055f);
            SteamLine(p, r, 0.66f, 0.35f, 0.1f, s * 0.055f);
        }

        /// One flame (campfire, upload streak).
        public static void CookFlame(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.52f, 0.05f));
            p.BezierCurveTo(P(r, 0.64f, 0.26f), P(r, 0.84f, 0.4f), P(r, 0.82f, 0.63f));
            p.BezierCurveTo(P(r, 0.8f, 0.85f), P(r, 0.65f, 0.95f), P(r, 0.5f, 0.95f));
            p.BezierCurveTo(P(r, 0.34f, 0.95f), P(r, 0.18f, 0.84f), P(r, 0.18f, 0.64f));
            p.BezierCurveTo(P(r, 0.18f, 0.48f), P(r, 0.28f, 0.38f), P(r, 0.35f, 0.28f));
            p.BezierCurveTo(P(r, 0.38f, 0.4f), P(r, 0.42f, 0.46f), P(r, 0.47f, 0.47f));
            p.BezierCurveTo(P(r, 0.45f, 0.32f), P(r, 0.47f, 0.18f), P(r, 0.52f, 0.05f));
            p.ClosePath();
            p.Fill();
        }

        /// A campfire: flame over two crossed logs.
        public static void CookCampfire(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            CookFlame(p, new Rect(r.x + r.width * 0.23f, r.y + r.height * 0.02f, r.width * 0.54f, r.height * 0.68f), c);
            p.strokeColor = c;
            Stroke(p, r, s * 0.1f, 0.12f, 0.92f, 0.88f, 0.74f);
            Stroke(p, r, s * 0.1f, 0.12f, 0.74f, 0.88f, 0.92f);
        }

        /// The hut grill: grate over a bowl on legs, heat waves above.
        public static void CookGrill(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.07f, 0.1f, 0.44f, 0.9f, 0.44f);
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.16f, 0.5f));
            p.LineTo(P(r, 0.84f, 0.5f));
            p.BezierCurveTo(P(r, 0.84f, 0.68f), P(r, 0.68f, 0.76f), P(r, 0.5f, 0.76f));
            p.BezierCurveTo(P(r, 0.32f, 0.76f), P(r, 0.16f, 0.68f), P(r, 0.16f, 0.5f));
            p.ClosePath();
            p.Fill();
            Stroke(p, r, s * 0.065f, 0.34f, 0.72f, 0.22f, 0.94f);
            Stroke(p, r, s * 0.065f, 0.66f, 0.72f, 0.78f, 0.94f);
            SteamLine(p, r, 0.34f, 0.36f, 0.1f, s * 0.05f);
            SteamLine(p, r, 0.5f, 0.36f, 0.06f, s * 0.05f);
            SteamLine(p, r, 0.66f, 0.36f, 0.1f, s * 0.05f);
        }

        /// The water filter: a funnel over a jug of clean water.
        public static void CookFilter(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Poly(p, r, 0.16f, 0.08f, 0.84f, 0.08f, 0.58f, 0.3f, 0.42f, 0.3f);
            FillRoundRect(p, r, 0.46f, 0.28f, 0.54f, 0.4f, 0.02f);
            p.strokeColor = c;
            p.lineWidth = s * 0.07f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            RoundRect(p, r, 0.22f, 0.4f, 0.78f, 0.93f, 0.12f);
            p.Stroke();
            FillRoundRect(p, r, 0.29f, 0.64f, 0.71f, 0.87f, 0.07f);
        }

        /// The hermits' fire: a tent and a flame.
        public static void CookHermitFire(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.075f, 0.05f, 0.88f, 0.33f, 0.18f, 0.61f, 0.88f, 0.05f, 0.88f);
            Stroke(p, r, s * 0.06f, 0.33f, 0.5f, 0.25f, 0.88f);
            Stroke(p, r, s * 0.06f, 0.33f, 0.5f, 0.41f, 0.88f);
            CookFlame(p, new Rect(r.x + r.width * 0.6f, r.y + r.height * 0.4f, r.width * 0.38f, r.height * 0.5f), c);
        }

        /// A palm with coconuts (coconut source).
        public static void CookPalm(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            p.lineWidth = s * 0.09f;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(r, 0.46f, 0.95f));
            p.QuadraticCurveTo(P(r, 0.42f, 0.6f), P(r, 0.54f, 0.32f));
            p.Stroke();
            p.fillColor = c;
            Leaf(p, r, 0.54f, 0.3f, 0.1f, 0.42f, 0.26f, 0.16f, 0.3f, 0.4f);
            Leaf(p, r, 0.54f, 0.3f, 0.96f, 0.4f, 0.8f, 0.14f, 0.76f, 0.38f);
            Leaf(p, r, 0.54f, 0.3f, 0.3f, 0.06f, 0.36f, 0.12f, 0.46f, 0.24f);
            Leaf(p, r, 0.54f, 0.3f, 0.82f, 0.06f, 0.66f, 0.08f, 0.62f, 0.24f);
            Circle(p, P(r, 0.5f, 0.38f), s * 0.06f);
            Circle(p, P(r, 0.61f, 0.4f), s * 0.06f);
        }

        /// A leaf from a base point to a tip, bulging through two control points (one per side).
        static void Leaf(Painter2D p, Rect r, float bx, float by, float tx, float ty, float c1x, float c1y, float c2x, float c2y)
        {
            p.BeginPath();
            p.MoveTo(P(r, bx, by));
            p.QuadraticCurveTo(P(r, c1x, c1y), P(r, tx, ty));
            p.QuadraticCurveTo(P(r, c2x, c2y), P(r, bx, by));
            p.ClosePath();
            p.Fill();
        }

        /// A fruit tree (mango / banana source).
        public static void CookTree(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.44f, 0.56f, 0.56f, 0.94f, 0.03f);
            Circle(p, P(r, 0.5f, 0.34f), s * 0.27f);
            Circle(p, P(r, 0.3f, 0.5f), s * 0.17f);
            Circle(p, P(r, 0.7f, 0.5f), s * 0.17f);
        }

        // ------------------------------------------------------------------ the curry bowl (multi-colour item icon)
        /// The hermits' coconut fish curry in a bowl.
        public static void CookCurry(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            // steam
            p.strokeColor = Al(UIPalette.Silhouette, a * 0.8f);
            SteamLine(p, r, 0.38f, 0.34f, 0.08f, s * 0.045f);
            SteamLine(p, r, 0.6f, 0.34f, 0.1f, s * 0.045f);
            // bowl
            p.fillColor = Al(ItemInk, a);
            p.BeginPath();
            p.MoveTo(P(r, 0.05f, 0.47f));
            p.LineTo(P(r, 0.95f, 0.47f));
            p.BezierCurveTo(P(r, 0.95f, 0.76f), P(r, 0.75f, 0.92f), P(r, 0.5f, 0.92f));
            p.BezierCurveTo(P(r, 0.25f, 0.92f), P(r, 0.05f, 0.76f), P(r, 0.05f, 0.47f));
            p.ClosePath();
            p.Fill();
            p.fillColor = Al(UIPalette.Rgb(0xE9EEF2), a);
            p.BeginPath();
            p.MoveTo(P(r, 0.1f, 0.5f));
            p.LineTo(P(r, 0.9f, 0.5f));
            p.BezierCurveTo(P(r, 0.9f, 0.73f), P(r, 0.72f, 0.87f), P(r, 0.5f, 0.87f));
            p.BezierCurveTo(P(r, 0.28f, 0.87f), P(r, 0.1f, 0.73f), P(r, 0.1f, 0.5f));
            p.ClosePath();
            p.Fill();
            p.strokeColor = Al(UIPalette.Rgb(0x3FA9C4), a);
            Stroke(p, r, s * 0.04f, 0.2f, 0.7f, 0.5f, 0.75f, 0.8f, 0.7f);
            // curry surface
            p.fillColor = Al(ItemInk, a);
            EllipsePath(p, P(r, 0.5f, 0.47f), r.width * 0.43f, r.height * 0.12f);
            p.Fill();
            p.fillColor = Al(UIPalette.Rgb(0xF2A93B), a);
            EllipsePath(p, P(r, 0.5f, 0.47f), r.width * 0.39f, r.height * 0.09f);
            p.Fill();
            // fish pieces, coconut cream, a green leaf
            p.fillColor = Al(UIPalette.Rgb(0xF4E1D2), a);
            Circle(p, P(r, 0.36f, 0.47f), s * 0.05f);
            Circle(p, P(r, 0.62f, 0.49f), s * 0.045f);
            p.fillColor = Al(Color.white, a * 0.85f);
            Circle(p, P(r, 0.5f, 0.44f), s * 0.03f);
            p.fillColor = Al(UIPalette.Rgb(0x6E9445), a);
            Poly(p, r, 0.47f, 0.48f, 0.6f, 0.4f, 0.55f, 0.5f);
        }

        // ------------------------------------------------------------------ FinTube Studio (single colour)
        /// A speech bubble with three dots (comments).
        public static void StudioComment(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            p.lineWidth = s * 0.075f;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            RoundRect(p, r, 0.09f, 0.14f, 0.91f, 0.7f, 0.16f);
            p.Stroke();
            Stroke(p, r, s * 0.075f, 0.3f, 0.7f, 0.24f, 0.9f, 0.48f, 0.7f);
            p.fillColor = c;
            Circle(p, P(r, 0.32f, 0.42f), s * 0.055f);
            Circle(p, P(r, 0.5f, 0.42f), s * 0.055f);
            Circle(p, P(r, 0.68f, 0.42f), s * 0.055f);
        }

        public static void StudioUp(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.13f, 0.22f, 0.66f, 0.5f, 0.36f, 0.78f, 0.66f);
        }

        public static void StudioDown(Painter2D p, Rect r, Color c)
        {
            p.strokeColor = c;
            Stroke(p, r, S(r) * 0.13f, 0.22f, 0.36f, 0.5f, 0.66f, 0.78f, 0.36f);
        }
    }
}
