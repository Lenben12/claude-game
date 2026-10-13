using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 – the few extra glyphs of the Chopper-Shop catalogue (express bolt, reach arrows, durability shield).
    public static partial class UIGlyphs
    {
        /// Lightning bolt: express delivery.
        public static void ShopBolt(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            Poly(p, r, 0.6f, 0.04f, 0.18f, 0.56f, 0.46f, 0.56f, 0.36f, 0.96f, 0.82f, 0.42f, 0.54f, 0.42f, 0.7f, 0.04f);
        }

        /// Double arrow ↔: reach / cast distance.
        public static void ShopReach(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.strokeColor = c;
            Stroke(p, r, s * 0.11f, 0.2f, 0.5f, 0.8f, 0.5f);
            p.fillColor = c;
            Poly(p, r, 0.04f, 0.5f, 0.3f, 0.26f, 0.3f, 0.74f);
            Poly(p, r, 0.96f, 0.5f, 0.7f, 0.26f, 0.7f, 0.74f);
        }

        /// Shield: durability.
        public static void ShopShield(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.06f));
            p.BezierCurveTo(P(r, 0.66f, 0.16f), P(r, 0.78f, 0.17f), P(r, 0.88f, 0.17f));
            p.BezierCurveTo(P(r, 0.9f, 0.56f), P(r, 0.78f, 0.8f), P(r, 0.5f, 0.96f));
            p.BezierCurveTo(P(r, 0.22f, 0.8f), P(r, 0.1f, 0.56f), P(r, 0.12f, 0.17f));
            p.BezierCurveTo(P(r, 0.22f, 0.17f), P(r, 0.34f, 0.16f), P(r, 0.5f, 0.06f));
            p.ClosePath();
            p.Fill();
        }
    }
}
