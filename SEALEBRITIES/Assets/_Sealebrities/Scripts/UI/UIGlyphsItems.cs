using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 backpack item icons (multi-coloured like the hotbar items; c.a = overall opacity) + the survival icons.
    public static partial class UIGlyphs
    {
        static readonly Color ItemInk = new Color(0.08f, 0.09f, 0.16f, 0.85f);

        static void Outline(Painter2D p, Rect r, float w, Color c, params float[] xy)
        {
            p.strokeColor = c;
            Stroke(p, r, w, xy);
        }

        /// A knobbly stick (bonk!).
        public static void ItemStick(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            Outline(p, r, s * 0.15f, Al(ItemInk, a), 0.16f, 0.86f, 0.5f, 0.5f, 0.84f, 0.16f);
            p.strokeColor = Al(UIPalette.Rgb(0x9A6B43), a);
            Stroke(p, r, s * 0.09f, 0.16f, 0.86f, 0.5f, 0.5f, 0.84f, 0.16f);
            p.strokeColor = Al(UIPalette.Rgb(0x6E4A2E), a);
            Stroke(p, r, s * 0.04f, 0.56f, 0.44f, 0.7f, 0.5f);
            p.fillColor = Al(UIPalette.Rgb(0x6E4A2E), a);
            Circle(p, P(r, 0.38f, 0.64f), s * 0.05f);
        }

        /// A spear: wooden shaft, turquoise grip band, steel tip.
        public static void ItemSpear(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            Outline(p, r, s * 0.11f, Al(ItemInk, a), 0.1f, 0.9f, 0.74f, 0.26f);
            p.strokeColor = Al(UIPalette.Rgb(0xB88A5A), a);
            Stroke(p, r, s * 0.06f, 0.1f, 0.9f, 0.74f, 0.26f);
            p.strokeColor = Al(UIPalette.Turquoise, a);
            Stroke(p, r, s * 0.08f, 0.3f, 0.7f, 0.38f, 0.62f);
            p.fillColor = Al(ItemInk, a);
            Poly(p, r, 0.66f, 0.2f, 0.94f, 0.06f, 0.8f, 0.34f);
            p.fillColor = Al(UIPalette.Rgb(0xD5DBE3), a);
            Poly(p, r, 0.69f, 0.23f, 0.9f, 0.1f, 0.78f, 0.31f);
        }

        /// The speargun: dark barrel, orange grip, steel bolt.
        public static void ItemSpeargun(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            FillRoundRect(p, r, 0.06f, 0.4f, 0.86f, 0.56f, 0.06f);
            p.fillColor = Al(UIPalette.Rgb(0x2E3140), a);
            FillRoundRect(p, r, 0.09f, 0.43f, 0.83f, 0.53f, 0.04f);
            p.fillColor = Al(UIPalette.Rgb(0xEE7F2D), a);
            Poly(p, r, 0.28f, 0.52f, 0.4f, 0.52f, 0.34f, 0.78f, 0.22f, 0.78f);
            p.strokeColor = Al(UIPalette.Rgb(0xD5DBE3), a);
            Stroke(p, r, s * 0.035f, 0.12f, 0.36f, 0.95f, 0.36f);
            p.fillColor = Al(UIPalette.Rgb(0xD5DBE3), a);
            Poly(p, r, 0.92f, 0.31f, 0.99f, 0.36f, 0.92f, 0.41f);
        }

        public static void ItemStone(Painter2D p, Rect r, Color c)
        {
            float a = c.a;
            p.fillColor = Al(ItemInk, a);
            Poly(p, r, 0.12f, 0.62f, 0.3f, 0.32f, 0.62f, 0.26f, 0.88f, 0.48f, 0.84f, 0.74f, 0.4f, 0.8f);
            p.fillColor = Al(UIPalette.Rgb(0x9AA3AE), a);
            Poly(p, r, 0.17f, 0.61f, 0.32f, 0.36f, 0.6f, 0.31f, 0.83f, 0.5f, 0.8f, 0.7f, 0.41f, 0.75f);
            p.fillColor = Al(UIPalette.Rgb(0xC5CCD4), a);
            Poly(p, r, 0.32f, 0.38f, 0.58f, 0.34f, 0.5f, 0.48f);
        }

        public static void ItemWood(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            Outline(p, r, s * 0.2f, Al(ItemInk, a), 0.14f, 0.7f, 0.86f, 0.38f);
            p.strokeColor = Al(UIPalette.Rgb(0xC9B59A), a);
            Stroke(p, r, s * 0.13f, 0.14f, 0.7f, 0.86f, 0.38f);
            p.strokeColor = Al(UIPalette.Rgb(0x9C8668), a);
            Stroke(p, r, s * 0.04f, 0.5f, 0.54f, 0.62f, 0.3f);
            p.fillColor = Al(UIPalette.Rgb(0xE6D6BC), a);
            Circle(p, P(r, 0.86f, 0.38f), s * 0.06f);
        }

        /// Bottle: tint by content (empty / seawater / fresh).
        static void BottleIcon(Painter2D p, Rect r, Color c, Color water, float level)
        {
            float a = c.a;
            p.fillColor = Al(ItemInk, a);
            FillRoundRect(p, r, 0.3f, 0.3f, 0.7f, 0.9f, 0.18f);
            FillRoundRect(p, r, 0.41f, 0.1f, 0.59f, 0.34f, 0.04f);
            p.fillColor = Al(UIPalette.Rgb(0xE4F1F5), a);
            FillRoundRect(p, r, 0.34f, 0.34f, 0.66f, 0.86f, 0.16f);
            FillRoundRect(p, r, 0.44f, 0.16f, 0.56f, 0.34f, 0.02f);
            if (level > 0f)
            {
                p.fillColor = Al(water, a);
                FillRoundRect(p, r, 0.34f, 0.86f - 0.5f * level, 0.66f, 0.86f, 0.14f);
            }
            p.fillColor = Al(UIPalette.Rgb(0x2E3140), a);
            FillRoundRect(p, r, 0.42f, 0.06f, 0.58f, 0.13f, 0.02f);
        }

        public static void ItemBottleEmpty(Painter2D p, Rect r, Color c) => BottleIcon(p, r, c, Color.clear, 0f);
        public static void ItemBottleSea(Painter2D p, Rect r, Color c) => BottleIcon(p, r, c, UIPalette.Rgb(0x2F7F94), 0.85f);
        public static void ItemBottleFresh(Painter2D p, Rect r, Color c) => BottleIcon(p, r, c, UIPalette.Rgb(0x6FD3F0), 0.85f);

        static void FishIcon(Painter2D p, Rect r, Color c, Color body, Color belly, bool grilled)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            Poly(p, r, 0.06f, 0.5f, 0.28f, 0.3f, 0.66f, 0.3f, 0.82f, 0.5f, 0.66f, 0.7f, 0.28f, 0.7f);
            Poly(p, r, 0.76f, 0.5f, 0.97f, 0.3f, 0.97f, 0.7f);
            p.fillColor = Al(body, a);
            Poly(p, r, 0.1f, 0.5f, 0.3f, 0.34f, 0.64f, 0.34f, 0.78f, 0.5f, 0.64f, 0.66f, 0.3f, 0.66f);
            Poly(p, r, 0.8f, 0.5f, 0.94f, 0.36f, 0.94f, 0.64f);
            p.fillColor = Al(belly, a);
            Poly(p, r, 0.2f, 0.56f, 0.64f, 0.56f, 0.6f, 0.64f, 0.3f, 0.64f);
            p.fillColor = Al(ItemInk, a);
            Circle(p, P(r, 0.24f, 0.46f), s * 0.035f);
            if (grilled)
            {
                p.strokeColor = Al(UIPalette.Rgb(0x5A3424), a);
                for (int i = 0; i < 3; i++) Stroke(p, r, s * 0.035f, 0.36f + i * 0.1f, 0.38f, 0.32f + i * 0.1f, 0.6f);
            }
        }

        public static void ItemRawFish(Painter2D p, Rect r, Color c) => FishIcon(p, r, c, UIPalette.Rgb(0x8FB3C8), UIPalette.Rgb(0xDCE9F0), false);
        public static void ItemGrilledFish(Painter2D p, Rect r, Color c) => FishIcon(p, r, c, UIPalette.Rgb(0xC97A3A), UIPalette.Rgb(0xE8B56A), true);

        public static void ItemMeat(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            Circle(p, P(r, 0.44f, 0.46f), s * 0.3f);
            p.fillColor = Al(UIPalette.Rgb(0xD9686E), a);
            Circle(p, P(r, 0.44f, 0.46f), s * 0.26f);
            p.fillColor = Al(UIPalette.Rgb(0xF4E1D2), a);
            Circle(p, P(r, 0.52f, 0.4f), s * 0.09f);
            p.strokeColor = Al(ItemInk, a);
            Stroke(p, r, s * 0.12f, 0.62f, 0.64f, 0.84f, 0.86f);
            p.strokeColor = Al(UIPalette.Rgb(0xF4EFE6), a);
            Stroke(p, r, s * 0.07f, 0.62f, 0.64f, 0.84f, 0.86f);
        }

        public static void ItemGrilledMeat(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            Circle(p, P(r, 0.44f, 0.46f), s * 0.3f);
            p.fillColor = Al(UIPalette.Rgb(0xA8603A), a);
            Circle(p, P(r, 0.44f, 0.46f), s * 0.26f);
            p.strokeColor = Al(UIPalette.Rgb(0x5A3424), a);
            Stroke(p, r, s * 0.035f, 0.3f, 0.38f, 0.5f, 0.6f);
            Stroke(p, r, s * 0.035f, 0.4f, 0.3f, 0.6f, 0.52f);
            p.strokeColor = Al(ItemInk, a);
            Stroke(p, r, s * 0.12f, 0.62f, 0.64f, 0.84f, 0.86f);
            p.strokeColor = Al(UIPalette.Rgb(0xF4EFE6), a);
            Stroke(p, r, s * 0.07f, 0.62f, 0.64f, 0.84f, 0.86f);
        }

        public static void ItemCoconut(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            Circle(p, P(r, 0.5f, 0.56f), s * 0.34f);
            p.fillColor = Al(UIPalette.Rgb(0x7A5236), a);
            Circle(p, P(r, 0.5f, 0.56f), s * 0.3f);
            p.fillColor = Al(UIPalette.Rgb(0xF4EFE6), a);
            Circle(p, P(r, 0.5f, 0.36f), s * 0.12f);
            p.strokeColor = Al(UIPalette.Rgb(0xE0607E), a);
            Stroke(p, r, s * 0.05f, 0.52f, 0.36f, 0.66f, 0.08f);
        }

        public static void ItemMango(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            Circle(p, P(r, 0.5f, 0.56f), s * 0.33f);
            p.fillColor = Al(UIPalette.Rgb(0xF2A93B), a);
            Circle(p, P(r, 0.5f, 0.56f), s * 0.29f);
            p.fillColor = Al(UIPalette.Rgb(0xE0607E), a);
            Circle(p, P(r, 0.4f, 0.48f), s * 0.12f);
            p.fillColor = Al(UIPalette.Rgb(0x6E9445), a);
            Poly(p, r, 0.5f, 0.24f, 0.72f, 0.12f, 0.64f, 0.28f);
        }

        public static void ItemBanana(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.strokeColor = Al(ItemInk, a);
            p.lineWidth = s * 0.2f;
            p.lineCap = LineCap.Round;
            p.BeginPath(); p.MoveTo(P(r, 0.2f, 0.3f)); p.QuadraticCurveTo(P(r, 0.3f, 0.82f), P(r, 0.82f, 0.68f)); p.Stroke();
            p.strokeColor = Al(UIPalette.Rgb(0xF2D94C), a);
            p.lineWidth = s * 0.14f;
            p.BeginPath(); p.MoveTo(P(r, 0.2f, 0.3f)); p.QuadraticCurveTo(P(r, 0.3f, 0.82f), P(r, 0.82f, 0.68f)); p.Stroke();
            p.fillColor = Al(UIPalette.Rgb(0x6E4A2E), a);
            Circle(p, P(r, 0.2f, 0.28f), s * 0.05f);
        }

        /// A torn map piece with a dotted path.
        public static void ItemMapPiece(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            Poly(p, r, 0.16f, 0.2f, 0.62f, 0.14f, 0.7f, 0.3f, 0.86f, 0.36f, 0.82f, 0.86f, 0.36f, 0.82f, 0.28f, 0.66f, 0.12f, 0.6f);
            p.fillColor = Al(UIPalette.Rgb(0xE8D9B0), a);
            Poly(p, r, 0.2f, 0.24f, 0.6f, 0.19f, 0.67f, 0.34f, 0.81f, 0.4f, 0.78f, 0.81f, 0.39f, 0.78f, 0.31f, 0.63f, 0.17f, 0.57f);
            p.fillColor = Al(UIPalette.Rgb(0xC0563C), a);
            for (int i = 0; i < 4; i++) Circle(p, P(r, 0.3f + i * 0.11f, 0.38f + i * 0.08f), s * 0.025f);
        }

        /// The finished treasure map: parchment, island outline, a red X.
        public static void ItemTreasureMap(Painter2D p, Rect r, Color c)
        {
            float a = c.a, s = S(r);
            p.fillColor = Al(ItemInk, a);
            FillRoundRect(p, r, 0.08f, 0.16f, 0.92f, 0.84f, 0.06f);
            p.fillColor = Al(UIPalette.Rgb(0xEAD9A8), a);
            FillRoundRect(p, r, 0.12f, 0.2f, 0.88f, 0.8f, 0.04f);
            p.fillColor = Al(UIPalette.Rgb(0x9BCB7A), a);
            Poly(p, r, 0.24f, 0.5f, 0.36f, 0.32f, 0.6f, 0.3f, 0.74f, 0.46f, 0.66f, 0.66f, 0.38f, 0.68f);
            p.strokeColor = Al(UIPalette.Rgb(0xC0392B), a);
            Stroke(p, r, s * 0.07f, 0.48f, 0.42f, 0.62f, 0.56f);
            Stroke(p, r, s * 0.07f, 0.62f, 0.42f, 0.48f, 0.56f);
        }

        /// Generic snack / drink (packaged food).
        public static void ItemSnack(Painter2D p, Rect r, Color c)
        {
            float a = c.a;
            p.fillColor = Al(ItemInk, a);
            FillRoundRect(p, r, 0.2f, 0.16f, 0.8f, 0.86f, 0.12f);
            p.fillColor = Al(UIPalette.Coral, a);
            FillRoundRect(p, r, 0.24f, 0.2f, 0.76f, 0.82f, 0.1f);
            p.fillColor = Al(UIPalette.Rgb(0xF4EFE6), a);
            FillRoundRect(p, r, 0.32f, 0.38f, 0.68f, 0.62f, 0.06f);
            p.fillColor = Al(UIPalette.Sun, a);
            Circle(p, P(r, 0.5f, 0.5f), S(r) * 0.08f);
        }

        public static void ItemDrinkCan(Painter2D p, Rect r, Color c)
        {
            float a = c.a;
            p.fillColor = Al(ItemInk, a);
            FillRoundRect(p, r, 0.28f, 0.12f, 0.72f, 0.9f, 0.1f);
            p.fillColor = Al(UIPalette.Rgb(0x6FD3C0), a);
            FillRoundRect(p, r, 0.32f, 0.16f, 0.68f, 0.86f, 0.08f);
            p.fillColor = Al(UIPalette.Rgb(0xD5DBE3), a);
            FillRoundRect(p, r, 0.32f, 0.16f, 0.68f, 0.24f, 0.04f);
            p.fillColor = Al(Color.white, 0.7f * a);
            FillRoundRect(p, r, 0.38f, 0.36f, 0.46f, 0.74f, 0.04f);
        }

        // ------------------------------------------------------------------ survival HUD (single colour)
        /// Drumstick (hunger).
        public static void Hunger(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            Circle(p, P(r, 0.4f, 0.4f), s * 0.26f);
            p.strokeColor = c;
            Stroke(p, r, s * 0.13f, 0.5f, 0.5f, 0.74f, 0.74f);
            Circle(p, P(r, 0.8f, 0.72f), s * 0.07f);
            Circle(p, P(r, 0.72f, 0.82f), s * 0.07f);
        }

        /// Water drop (thirst).
        public static void Thirst(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.1f));
            p.BezierCurveTo(P(r, 0.62f, 0.32f), P(r, 0.82f, 0.48f), P(r, 0.8f, 0.66f));
            p.BezierCurveTo(P(r, 0.78f, 0.86f), P(r, 0.62f, 0.92f), P(r, 0.5f, 0.92f));
            p.BezierCurveTo(P(r, 0.38f, 0.92f), P(r, 0.22f, 0.86f), P(r, 0.2f, 0.66f));
            p.BezierCurveTo(P(r, 0.18f, 0.48f), P(r, 0.38f, 0.32f), P(r, 0.5f, 0.1f));
            p.ClosePath();
            p.Fill();
        }

        /// Heart ("Puste" / health after a hit).
        public static void Heart(Painter2D p, Rect r, Color c)
        {
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(P(r, 0.5f, 0.86f));
            p.BezierCurveTo(P(r, 0.1f, 0.6f), P(r, 0.06f, 0.3f), P(r, 0.28f, 0.2f));
            p.BezierCurveTo(P(r, 0.42f, 0.14f), P(r, 0.5f, 0.26f), P(r, 0.5f, 0.3f));
            p.BezierCurveTo(P(r, 0.5f, 0.26f), P(r, 0.58f, 0.14f), P(r, 0.72f, 0.2f));
            p.BezierCurveTo(P(r, 0.94f, 0.3f), P(r, 0.9f, 0.6f), P(r, 0.5f, 0.86f));
            p.ClosePath();
            p.Fill();
        }

        /// Thermometer (weather app).
        public static void Thermo(Painter2D p, Rect r, Color c)
        {
            float s = S(r);
            p.fillColor = c;
            FillRoundRect(p, r, 0.42f, 0.1f, 0.58f, 0.66f, 0.08f);
            Circle(p, P(r, 0.5f, 0.74f), s * 0.16f);
        }

        /// Icon of any item id (gear pseudo items, backpack items, foods). Null for unknown / "None".
        public static UIGlyph.DrawFn ForItemId(string id)
        {
            if (string.IsNullOrEmpty(id) || id == ItemIds.None) return null;
            switch (id)
            {
                case ItemIds.Camera: return ItemCamera;
                case ItemIds.Flashlight: return ItemFlashlight;
                case ItemIds.FishingRod: return ItemRod;
                case ItemIds.Stick: return ItemStick;
                case ItemIds.SpearT1: case ItemIds.SpearT2: return ItemSpear;
                case ItemIds.Speargun: return ItemSpeargun;
                case ItemIds.Stone: return ItemStone;
                case ItemIds.Wood: return ItemWood;
                case ItemIds.BottleEmpty: return ItemBottleEmpty;
                case ItemIds.BottleSea: return ItemBottleSea;
                case ItemIds.BottleFresh: return ItemBottleFresh;
                case ItemIds.RawFish: return ItemRawFish;
                case ItemIds.RawMeat: return ItemMeat;
                case ItemIds.MapPiece: return ItemMapPiece;
                case ItemIds.TreasureMap: return ItemTreasureMap;
                case ItemIds.Shell: return Shell;
                case ItemIds.Page: return Page;
                case ItemIds.Message: return Bottle;
                case ItemIds.Treasure: return Chest;
                case FoodDatabase.GrilledFish: return ItemGrilledFish;
                case FoodDatabase.GrilledMeat: return ItemGrilledMeat;
                case FoodDatabase.Coconut: return ItemCoconut;
                case "mango": return ItemMango;
                case "banana": return ItemBanana;
            }
            var d = ItemDatabase.Get(id);
            if (d == null) return null;
            if (d.category == ItemCategory.Drink) return ItemDrinkCan;
            if (d.category == ItemCategory.Food) return ItemSnack;
            if (d.category == ItemCategory.Valuable) return d.id == ItemIds.Coins ? (UIGlyph.DrawFn)Coin : Chest;   // round 3 treasure
            return ForItem(d.hold);
        }

        /// Single-colour items (collectibles) need a tint: the multi-colour ones ignore it.
        public static Color TintFor(string id)
        {
            switch (id)
            {
                case ItemIds.Shell: case ItemIds.Treasure: return UIPalette.Sun;
                case ItemIds.Page: return UIPalette.Rgb(0xE8D9B0);
                case ItemIds.Message: return UIPalette.Rgb(0x7FC8A9);
                case ItemIds.Gem: return UIPalette.Rgb(0x7FD3E0);
                case ItemIds.Pearl: return UIPalette.Rgb(0xF2E6F0);
                case ItemIds.FossilTooth: return UIPalette.Rgb(0xE8D9B0);
                default:
                    var d = ItemDatabase.Get(id);
                    return d != null && d.category == ItemCategory.Valuable ? UIPalette.Sun : Color.white;
            }
        }
    }
}
