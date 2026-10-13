using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Cookbook app (round 3, GDD §20 Q34 „Neue App: Kochbuch“). Every dish the game REALLY makes, read from the live
    /// data – Survival.CookOne (campfire / hermits' fire / hut grill: the first raw item of the backpack → its cookTo;
    /// raw fish + a coconut → the hermits' curry once Topf taught it), PropGrill (without raw food it grills the
    /// cheapest cooler fish), the hut's water filter (seawater → fresh water, 45 min) and what you eat as it is
    /// (coconut, mango, banana). Per recipe: big icon, hunger / thirst points (FoodDef via Survival.ValuesOf) + buff,
    /// ingredients with have / need (green check / red cross), where to cook (icons), where the ingredients come from
    /// and the hermits' trades. Filter row: Alles · Kann ich · Fisch · Obst · Trinken.
    /// Spoiler rule: hermit names, their fire and their trades only show once the camp was found.
    public class UIAppCookbook : UIPhoneApp
    {
        public enum Filter { All, CanCook, Fish, Fruit, Drink }

        [System.Flags]
        public enum Place { None = 0, Campfire = 1, Grill = 2, HermitFire = 4, WaterFilter = 8, AsIs = 16 }

        public sealed class Ingredient
        {
            public string itemId;
            public int count = 1;
        }

        public sealed class Recipe
        {
            /// Stable id (tests, element names "cook-&lt;id&gt;").
            public string id;
            /// Backpack item id of the result (foods share their FoodDatabase id).
            public string resultId;
            public Ingredient[] ingredients;
            public Place places;
            public Filter category = Filter.All;
            /// Also listed under „Trinken“.
            public bool drink;
            /// Nothing to cook: eat / drink it as it is.
            public bool asIs;
            /// Story flag that unlocks it (null = always) and the hermit who teaches it.
            public string lockFlag, teacher;
        }

        /// Pseudo ingredient: a fish from the shared cooler (the grill takes it itself).
        public const string CoolerFish = "*cooler_fish";

        static Ingredient I(string id, int n = 1) => new Ingredient { itemId = id, count = n };

        /// The dishes the game can make (no invented recipes – see the class comment).
        public static readonly Recipe[] Recipes =
        {
            new Recipe { id = "grilled_fish", resultId = FoodDatabase.GrilledFish, ingredients = new[] { I(ItemIds.RawFish) },
                places = Place.Campfire | Place.Grill | Place.HermitFire, category = Filter.Fish },
            new Recipe { id = "curry", resultId = Survival.CurryId, ingredients = new[] { I(ItemIds.RawFish), I(FoodDatabase.Coconut) },
                places = Place.Campfire | Place.Grill | Place.HermitFire, category = Filter.Fish, lockFlag = NpcKeys.RecipeCurry, teacher = NpcDatabase.HCook },
            new Recipe { id = "grill_cooler", resultId = FoodDatabase.GrilledFish, ingredients = new[] { I(CoolerFish) },
                places = Place.Grill, category = Filter.Fish },
            new Recipe { id = "grilled_meat", resultId = FoodDatabase.GrilledMeat, ingredients = new[] { I(ItemIds.RawMeat) },
                places = Place.Campfire | Place.Grill | Place.HermitFire, category = Filter.All },
            new Recipe { id = "fresh_water", resultId = ItemIds.BottleFresh, ingredients = new[] { I(ItemIds.BottleSea) },
                places = Place.WaterFilter, category = Filter.Drink, drink = true },
            new Recipe { id = "coconut", resultId = FoodDatabase.Coconut, ingredients = new[] { I(FoodDatabase.Coconut) },
                places = Place.AsIs, category = Filter.Fruit, drink = true, asIs = true },
            new Recipe { id = "mango", resultId = "mango", ingredients = new[] { I("mango") },
                places = Place.AsIs, category = Filter.Fruit, asIs = true },
            new Recipe { id = "banana", resultId = "banana", ingredients = new[] { I("banana") },
                places = Place.AsIs, category = Filter.Fruit, asIs = true },
        };

        static readonly Color AppColor = UIPalette.Rgb(0xE8833A);
        static readonly Color HungerColor = UIPalette.Rgb(0xE8833A);

        Filter filter = Filter.All;
        VisualElement lastBody;
        bool hooked;
        System.Action onChanged;
        System.Action<string> onFlag;
        float refresh;

        public override string Id => "cookbook";
        public override string Title => Loc.Tr("Kochbuch", "Cookbook");
        public override Color Color => AppColor;
        public override UIGlyph.DrawFn Icon => UIGlyphs.CookBook;

        // ---- queries (also used by the tests)
        public static Recipe Find(string id)
        {
            for (int i = 0; i < Recipes.Length; i++) if (Recipes[i].id == id) return Recipes[i];
            return null;
        }

        public static bool IsLocked(Recipe r) => r != null && !string.IsNullOrEmpty(r.lockFlag) && !StoryFlags.Has(r.lockFlag);

        /// Fish in the shared cooler the grill would take (no junk).
        public static int CoolerFishCount()
        {
            var s = UIKit.State;
            if (s == null) return 0;
            int n = 0;
            for (int i = 0; i < s.fishInventory.Count; i++)
            {
                var f = s.fishInventory[i];
                if (f == null) continue;
                var k = FishMarket.Kind(f.kindId);
                if (k != null && !k.junk) n++;
            }
            return n;
        }

        public static int Have(string itemId) => itemId == CoolerFish ? CoolerFishCount() : Backpack.Count(itemId);

        public static int HaveCount(Recipe r, int ingredient) =>
            r != null && ingredient >= 0 && ingredient < r.ingredients.Length ? Have(r.ingredients[ingredient].itemId) : 0;

        /// Every ingredient is there (the recipe may still be locked).
        public static bool HasIngredients(Recipe r)
        {
            if (r == null) return false;
            for (int i = 0; i < r.ingredients.Length; i++) if (Have(r.ingredients[i].itemId) < r.ingredients[i].count) return false;
            return true;
        }

        /// Unlocked and every ingredient there.
        public static bool CanMake(Recipe r) => !IsLocked(r) && HasIngredients(r);

        static bool CampFound => StoryFlags.Has(StoryKeys.HermitCampFound);

        public static bool Matches(Recipe r, Filter f)
        {
            switch (f)
            {
                case Filter.CanCook: return CanMake(r);
                case Filter.Fish: return r.category == Filter.Fish;
                case Filter.Fruit: return r.category == Filter.Fruit;
                case Filter.Drink: return r.drink;
                default: return true;
            }
        }

        // ---- test hooks
        public VisualElement DebugRoot => lastBody;
        public Filter CurrentFilter => filter;

        public void DebugFilter(Filter f)
        {
            filter = f;
            Phone?.Refresh(false);
        }

        // ------------------------------------------------------------------ live refresh while open
        public override void OnOpen()
        {
            if (hooked) return;
            hooked = true;
            if (onChanged == null) onChanged = OnDataChanged;
            if (onFlag == null) onFlag = OnFlag;
            Backpack.Changed += onChanged;
            Pantry.Changed += onChanged;
            FishMarket.Changed += onChanged;
            GameEvents.StoryFlagSet += onFlag;
        }

        public override void OnClose()
        {
            if (!hooked) return;
            hooked = false;
            Backpack.Changed -= onChanged;
            Pantry.Changed -= onChanged;
            FishMarket.Changed -= onChanged;
            GameEvents.StoryFlagSet -= onFlag;
        }

        void OnFlag(string flag) => OnDataChanged();

        void OnDataChanged()
        {
            if (Phone != null && UIState.PhoneOpen && Phone.Current == this) Phone.MarkDirty();
        }

        public override void Tick(float dt)
        {
            // hunger / thirst at the top and the filter timer follow the clock
            refresh += dt;
            if (refresh < 15f) return;
            refresh = 0f;
            Phone?.MarkDirty();
        }

        // ------------------------------------------------------------------ page
        public override void Build(VisualElement body)
        {
            lastBody = body;
            var gm = GameManager.I;
            if (gm == null) return;

            var meters = UIKit.Row();
            meters.name = "cook-meters";
            meters.Add(Meter(UIGlyphs.Hunger, HungerColor, Survival.Hunger01));
            meters.Add(UIKit.HSpacer(8f));
            meters.Add(Meter(UIGlyphs.Thirst, UIPalette.Sky, Survival.Thirst01));
            body.Add(meters);

            var fr = UIKit.Row();
            fr.name = "cook-filters";
            fr.style.flexWrap = Wrap.Wrap;
            fr.style.marginTop = 12f;
            FilterChip(fr, Filter.All, UIGlyphs.CookBook, "Alles", "All");
            FilterChip(fr, Filter.CanCook, UIGlyphs.Check, "Kann ich", "Can do");
            FilterChip(fr, Filter.Fish, UIGlyphs.Fish, "Fisch", "Fish");
            FilterChip(fr, Filter.Fruit, UIGlyphs.CookTree, "Obst", "Fruit");
            FilterChip(fr, Filter.Drink, UIGlyphs.Thirst, "Trinken", "Drinks");
            body.Add(fr);

            int ready = 0;
            for (int i = 0; i < Recipes.Length; i++) if (CanMake(Recipes[i])) ready++;
            var cnt = UIKit.Row();
            cnt.style.marginTop = 2f;
            cnt.style.marginBottom = 8f;
            cnt.Add(new UIGlyph(UIGlyphs.CookPot, UIPalette.InkSoft, 24f));
            cnt.Add(UIKit.Text(" " + ready + " / " + Recipes.Length + " " + Loc.Tr("jetzt möglich", "possible now"), PhoneKit.Tiny, UIPalette.InkSoft, true));
            body.Add(cnt);

            int shown = 0;
            for (int i = 0; i < Recipes.Length; i++)
            {
                var r = Recipes[i];
                if (!Matches(r, filter)) continue;
                body.Add(Card(r));
                shown++;
            }
            if (shown == 0)
                body.Add(PhoneKit.Empty(UIGlyphs.CookPot, Loc.Tr("Gerade nichts – sammle Zutaten!", "Nothing right now – gather ingredients!")));
        }

        static VisualElement Meter(UIGlyph.DrawFn icon, Color c, float v01)
        {
            var row = UIKit.Row();
            row.style.flexGrow = 1;
            row.style.flexBasis = 0;
            row.style.backgroundColor = Color.white;
            UIKit.Radius(row, 16f);
            UIKit.Pad(row, 10f, 12f, 10f, 10f);
            UIKit.Lift(row, 0.8f);
            row.Add(new UIGlyph(icon, c, 32f));
            var bar = PhoneKit.Bar(v01, v01 < Survival.Low / Survival.Max ? UIPalette.Coral : c, 12f);
            bar.style.flexGrow = 1;
            bar.style.marginLeft = 8f;
            bar.style.marginRight = 8f;
            row.Add(bar);
            row.Add(UIKit.Text(Mathf.RoundToInt(v01 * 100f) + " %", PhoneKit.Tiny, UIPalette.Ink, true));
            return row;
        }

        void FilterChip(VisualElement row, Filter f, UIGlyph.DrawFn icon, string de, string en)
        {
            bool sel = filter == f;
            var chip = UIKit.Row();
            chip.name = "cook-filter-" + (int)f;
            UIKit.Radius(chip, 20f);
            UIKit.Pad(chip, 8f, 14f, 8f, 12f);
            chip.style.marginRight = 8f;
            chip.style.marginBottom = 8f;
            if (!sel) UIKit.Border(chip, 2f, UIPalette.A(UIPalette.Ink, 0.08f));
            var fg = sel ? Color.white : UIPalette.Ink;
            chip.Add(new UIGlyph(icon, fg, 24f));
            var l = UIKit.Text(Loc.Tr(de, en), PhoneKit.Small, fg, true);
            l.style.marginLeft = 6f;
            chip.Add(l);
            UIKit.IgnorePicking(chip);
            chip.pickingMode = PickingMode.Position;
            UIKit.Clickable(chip, () => { if (filter == f) return; filter = f; Phone.Refresh(false); },
                sel ? AppColor : Color.white, sel ? UIPalette.Lighten(AppColor, 0.1f) : UIPalette.CardAlt);
            row.Add(chip);
        }

        // ------------------------------------------------------------------ one recipe card
        static UIGlyph.DrawFn IconFor(string id)
        {
            if (id == Survival.CurryId) return UIGlyphs.CookCurry;
            if (id == CoolerFish) return UIGlyphs.ItemRawFish;
            return UIGlyphs.ForItemId(id) ?? UIGlyphs.ItemSnack;
        }

        static string ItemName(string id)
        {
            if (id == CoolerFish) return Loc.Tr("Fisch (Kühlbox)", "Fish (cooler)");
            var d = ItemDatabase.Get(id);
            return d != null ? d.Name : id;
        }

        VisualElement Card(Recipe r)
        {
            bool locked = IsLocked(r);
            bool ok = !locked && HasIngredients(r);
            var card = PhoneKit.Card(16f);
            card.name = "cook-" + r.id;
            card.AddToClassList("cook-recipe");
            card.AddToClassList(locked ? "cook-locked" : ok ? "cook-ready" : "cook-missing");
            if (locked) card.style.backgroundColor = UIPalette.Rgb(0xEEF0F2);

            // ---- result: big icon, name, hunger / thirst, buff, status
            var top = UIKit.Row();
            top.style.alignItems = Align.Center;
            var tile = UIKit.Box();
            UIKit.Size(tile, 92f, 92f);
            UIKit.Radius(tile, 24f);
            UIKit.Center(tile);
            tile.style.flexShrink = 0;
            tile.style.overflow = Overflow.Visible;
            tile.style.backgroundColor = locked ? UIPalette.A(UIPalette.Ink, 0.07f) : UIPalette.A(AppColor, 0.16f);
            tile.Add(new UIGlyph(IconFor(r.resultId), locked ? new Color(1f, 1f, 1f, 0.4f) : Color.white, 76f));
            if (locked)
            {
                var lb = UIKit.Box();
                lb.name = "cook-" + r.id + "-lock";
                lb.pickingMode = PickingMode.Ignore;
                lb.style.position = Position.Absolute;
                lb.style.right = -6f; lb.style.bottom = -6f;
                UIKit.Size(lb, 40f, 40f);
                UIKit.Radius(lb, 20f);
                lb.style.backgroundColor = Color.white;
                UIKit.Center(lb);
                lb.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.SunInk, 24f));
                tile.Add(lb);
            }
            top.Add(tile);

            var col = UIKit.Col();
            col.style.marginLeft = 14f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(PhoneKit.T(ItemName(r.resultId), PhoneKit.Big, locked ? UIPalette.InkSoft : UIPalette.Ink, true));
            var vals = UIKit.Row();
            vals.style.flexWrap = Wrap.Wrap;
            vals.style.marginTop = 4f;
            var item = ItemDatabase.Get(r.resultId);
            Survival.ValuesOf(item, out float h, out float t);
            if (h > 0f) vals.Add(ValuePill(UIGlyphs.Hunger, HungerColor, "+" + Mathf.RoundToInt(h)));
            if (t > 0f) vals.Add(ValuePill(UIGlyphs.Thirst, UIPalette.Sky, "+" + Mathf.RoundToInt(t)));
            var food = item != null ? item.Food : null;
            if (food != null && food.value > 0f) vals.Add(BuffPill(food));
            col.Add(vals);
            top.Add(col);
            top.Add(StatusBadge(locked, ok, r.id));
            card.Add(top);

            // ---- ingredients (have / need)
            var ings = UIKit.Row();
            ings.style.flexWrap = Wrap.Wrap;
            ings.style.alignItems = Align.Center;
            ings.style.marginTop = 12f;
            for (int i = 0; i < r.ingredients.Length; i++)
            {
                if (i > 0)
                {
                    var plus = new UIGlyph(UIGlyphs.Plus, UIPalette.InkSoft, 20f);
                    plus.style.marginRight = 8f;
                    plus.style.marginBottom = 8f;
                    ings.Add(plus);
                }
                ings.Add(IngredientChip(r, i));
            }
            card.Add(ings);

            // ---- where
            var pl = UIKit.Row();
            pl.name = "cook-" + r.id + "-where";
            pl.style.flexWrap = Wrap.Wrap;
            pl.style.marginTop = 4f;
            AddPlace(pl, r, Place.Campfire, UIGlyphs.CookCampfire, UIPalette.Rgb(0xEE7F2D), "Lagerfeuer", "Campfire");
            AddPlace(pl, r, Place.Grill, UIGlyphs.CookGrill, UIPalette.CoralInk, "Grill", "Grill");
            if (CampFound) AddPlace(pl, r, Place.HermitFire, UIGlyphs.CookHermitFire, UIPalette.Rgb(0x8C6A3E), "Einsiedler", "Hermits");
            AddPlace(pl, r, Place.WaterFilter, UIGlyphs.CookFilter, UIPalette.Sky, "Filter", "Filter");
            AddPlace(pl, r, Place.AsIs, UIGlyphs.Hand, UIPalette.LeafInk, r.drink ? "Trinken" : "Essen", r.drink ? "Drink" : "Eat");
            card.Add(pl);
            if ((r.places & (Place.Campfire | Place.HermitFire)) != 0)
            {
                int wood = Backpack.Count(ItemIds.Wood);
                card.Add(Note(UIGlyphs.ItemWood, Color.white, Loc.Tr($"Feuer: {Survival.CampfireWood} Treibholz ({wood} dabei) · Grill: immer an",
                    $"Fire: {Survival.CampfireWood} driftwood ({wood} with you) · grill: always lit"),
                    wood >= Survival.CampfireWood ? UIPalette.LeafInk : UIPalette.InkSoft));
            }

            AddNotes(card, r, locked);
            return card;
        }

        static VisualElement ValuePill(UIGlyph.DrawFn icon, Color c, string text)
        {
            var p = UIKit.Row();
            UIKit.Radius(p, 14f);
            UIKit.Pad(p, 4f, 10f, 4f, 6f);
            p.style.marginRight = 6f;
            p.style.marginBottom = 4f;
            p.style.backgroundColor = UIPalette.A(c, 0.16f);
            p.Add(new UIGlyph(icon, c, 22f));
            p.Add(UIKit.Text(" " + text, PhoneKit.Small, UIWidgets.Ink(c), true));
            return p;
        }

        static VisualElement BuffPill(FoodDef f)
        {
            UIGlyph.DrawFn g;
            bool minus = false;
            switch (f.buff)
            {
                case BuffType.AirCapacity: g = UIGlyphs.Tank; break;
                case BuffType.SwimSpeed: g = UIGlyphs.Fins; break;
                case BuffType.SteadyHands: g = UIGlyphs.Camera; minus = true; break;
                case BuffType.BumpResist: g = UIGlyphs.SharkFin; minus = true; break;
                default: g = UIGlyphs.Eye; break;
            }
            string text = (minus ? "−" : "+") + Mathf.RoundToInt(f.value * 100f) + " % · " + Mathf.RoundToInt(f.minutes) + " min";
            return ValuePill(g, UIPalette.TurquoiseInk, text);
        }

        static VisualElement StatusBadge(bool locked, bool ok, string id)
        {
            var b = UIKit.Box();
            b.name = "cook-" + id + "-status";
            UIKit.Size(b, 44f, 44f);
            UIKit.Radius(b, 22f);
            UIKit.Center(b);
            b.style.flexShrink = 0;
            b.style.marginLeft = 8f;
            if (locked)
            {
                b.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.1f);
                b.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.InkSoft, 24f));
            }
            else if (ok)
            {
                b.style.backgroundColor = UIPalette.LeafInk;
                b.Add(new UIGlyph(UIGlyphs.Check, Color.white, 26f));
            }
            else
            {
                b.style.backgroundColor = UIPalette.Coral;
                b.Add(new UIGlyph(UIGlyphs.Cross, Color.white, 22f));
            }
            return b;
        }

        static VisualElement IngredientChip(Recipe r, int i)
        {
            var g = r.ingredients[i];
            int have = Have(g.itemId);
            bool enough = have >= g.count;
            var chip = UIKit.Row();
            chip.name = "cook-" + r.id + "-ing-" + i;
            chip.AddToClassList(enough ? "cook-have" : "cook-missing");
            UIKit.Radius(chip, 16f);
            UIKit.Pad(chip, 6f, 10f, 6f, 8f);
            chip.style.marginRight = 8f;
            chip.style.marginBottom = 8f;
            chip.style.backgroundColor = enough ? UIPalette.A(UIPalette.Leaf, 0.24f) : UIPalette.A(UIPalette.Coral, 0.16f);
            chip.Add(new UIGlyph(IconFor(g.itemId), Color.white, 40f));
            var c = UIKit.Col();
            c.style.marginLeft = 6f;
            c.Add(UIKit.Text(r.asIs ? Loc.Tr("Im Rucksack", "In backpack") : ItemName(g.itemId), 16f, UIPalette.InkSoft, true));
            string n = r.asIs ? have.ToString(Loc.Culture) : have.ToString(Loc.Culture) + " / " + g.count.ToString(Loc.Culture);
            c.Add(UIKit.Text(n, PhoneKit.Small, enough ? UIPalette.LeafInk : UIPalette.CoralInk, true));
            chip.Add(c);
            var st = new UIGlyph(enough ? UIGlyphs.Check : UIGlyphs.Cross, enough ? UIPalette.LeafInk : UIPalette.CoralInk, 22f);
            st.style.marginLeft = 8f;
            chip.Add(st);
            return chip;
        }

        static void AddPlace(VisualElement row, Recipe r, Place p, UIGlyph.DrawFn icon, Color c, string de, string en)
        {
            if ((r.places & p) == 0) return;
            var col = UIKit.Col();
            col.name = "cook-" + r.id + "-place-" + (int)p;
            col.style.alignItems = Align.Center;
            col.style.width = 92f;
            col.style.marginBottom = 4f;
            var sq = UIKit.Box();
            UIKit.Size(sq, 54f, 54f);
            UIKit.Radius(sq, 16f);
            UIKit.Center(sq);
            sq.style.backgroundColor = UIPalette.A(c, 0.14f);
            sq.Add(new UIGlyph(icon, c, 36f));
            col.Add(sq);
            var l = UIKit.Text(Loc.Tr(de, en), 16f, UIPalette.InkSoft, true);
            l.style.marginTop = 2f;
            l.style.unityTextAlign = TextAnchor.MiddleCenter;
            col.Add(l);
            row.Add(col);
        }

        static VisualElement Note(UIGlyph.DrawFn icon, Color iconColor, string text, Color? textColor = null)
        {
            var row = UIKit.Row();
            row.AddToClassList("cook-note");
            row.style.alignItems = Align.Center;
            row.style.marginTop = 6f;
            var g = new UIGlyph(icon, iconColor, 26f);
            g.style.marginRight = 8f;
            row.Add(g);
            var t = PhoneKit.T(text, 17f, textColor ?? UIPalette.InkSoft, true);
            t.style.flexShrink = 1;
            row.Add(t);
            return row;
        }

        /// Where the ingredients come from, the hermit who teaches it, trades and how the cooking really works.
        static void AddNotes(VisualElement card, Recipe r, bool locked)
        {
            bool camp = CampFound;
            switch (r.id)
            {
                case "grilled_fish":
                {
                    int raw = Backpack.Count(ItemIds.RawFish), cool = CoolerFishCount();
                    if (raw == 0 && cool > 0)
                        card.Add(Note(UIGlyphs.ItemRawFish, Color.white, Loc.Tr($"{cool} Fisch(e) in der Kühlbox → Rucksack → Kühlbox → „Zum Braten“",
                            $"{cool} fish in the cooler → backpack → cooler → “To cook”"), UIPalette.TurquoiseInk));
                    else card.Add(Note(UIGlyphs.Rod, UIPalette.InkSoft, Loc.Tr("Angeln → Kühlbox → Rucksack „Zum Braten“", "Fish → cooler → backpack “To cook”")));
                    if (StoryFlags.Has(NpcKeys.TrapLearned))
                        card.Add(Note(UIGlyphs.Fish, UIPalette.TurquoiseInk, Loc.Tr("Fischfalle: jeden Morgen +2", "Fish trap: +2 every morning")));
                    if (StoryFlags.Has(NpcKeys.RecipeCurry))
                        card.Add(Note(UIGlyphs.CookCurry, Color.white, Loc.Tr("Mit Kokosnuss im Rucksack wird daraus Curry", "With a coconut in the backpack it becomes curry")));
                    break;
                }
                case "grill_cooler":
                    card.Add(Note(UIGlyphs.CookGrill, UIPalette.CoralInk, Loc.Tr("Der Grill nimmt den billigsten Fisch – nur wenn nichts Rohes im Rucksack ist",
                        "The grill takes the cheapest fish – only with nothing raw in the backpack")));
                    break;
                case "curry":
                {
                    var who = NpcDatabase.Get(r.teacher);
                    if (locked)
                    {
                        var n = camp && who != null
                            ? Note(UIGlyphs.Person, UIPalette.SunInk, Loc.Tr($"{who.Name}, der Einsiedler-Koch, bringt es dir bei", $"{who.Name}, the hermits' cook, teaches it"), UIPalette.SunInk)
                            : Note(UIGlyphs.Question, UIPalette.SunInk, Loc.Tr("Ein Koch im Dschungel kennt dieses Rezept …", "A cook in the jungle knows this recipe…"), UIPalette.SunInk);
                        n.name = "cook-curry-teacher";
                        card.Add(n);
                    }
                    else if (camp && who != null)
                        card.Add(Note(UIGlyphs.Person, UIPalette.LeafInk, Loc.Tr($"Gelernt bei {who.Name}", $"Learned from {who.Name}")));
                    card.Add(Note(UIGlyphs.CookPalm, UIPalette.LeafInk, Loc.Tr("Kokosnuss: Palme schütteln", "Coconut: shake a palm")));
                    break;
                }
                case "grilled_meat":
                    if (!camp) card.Add(Note(UIGlyphs.Question, UIPalette.InkSoft, Loc.Tr("Rohes Fleisch? Im Dschungel weiß jemand Rat …", "Raw meat? Someone in the jungle can help…")));
                    break;
                case "fresh_water":
                {
                    int empty = Backpack.Count(ItemIds.BottleEmpty);
                    card.Add(Note(UIGlyphs.ItemBottleEmpty, Color.white, Loc.Tr($"Leere Flasche im Meer füllen ({empty} leer)", $"Fill an empty bottle in the sea ({empty} empty)")));
                    int waiting = Survival.FilterCount, readyN = Survival.FilterReadyCount;
                    string filt = Loc.Tr($"Wasserfilter in der Hütte: {Mathf.RoundToInt(Survival.FilterMinutes)} min", $"Water filter in the hut: {Mathf.RoundToInt(Survival.FilterMinutes)} min");
                    if (waiting > 0) filt += Loc.Tr($" · {readyN} fertig, {waiting - readyN} filtert", $" · {readyN} ready, {waiting - readyN} filtering");
                    card.Add(Note(UIGlyphs.Clock, UIPalette.Sky, filt));
                    break;
                }
                case "coconut":
                    card.Add(Note(UIGlyphs.CookPalm, UIPalette.LeafInk, Loc.Tr("Palme schütteln – eine pro Palme und Tag", "Shake a palm – one per palm and day")));
                    break;
                case "mango":
                case "banana":
                    card.Add(Note(UIGlyphs.CookTree, UIPalette.LeafInk, Loc.Tr("Auf der Insel sammeln (wächst jeden Tag nach)", "Gather it on the island (grows back daily)")));
                    if (StoryFlags.Has(NpcKeys.HarvestLearned))
                        card.Add(Note(UIGlyphs.CookTree, UIPalette.LeafInk, Loc.Tr("Ernte: jeden Morgen +1", "Harvest: +1 every morning")));
                    break;
            }
            if (!camp) return;
            // the hermits' barter (Q47) that gives the dish or its raw ingredient
            AddTrades(card, r.resultId);
            for (int i = 0; i < r.ingredients.Length; i++)
                if (r.ingredients[i].itemId == ItemIds.RawMeat) AddTrades(card, ItemIds.RawMeat);
        }

        static void AddTrades(VisualElement card, string itemId)
        {
            var all = HermitDatabase.All;
            for (int h = 0; h < all.Length; h++)
            {
                var d = all[h];
                if (d.trades == null) continue;
                for (int k = 0; k < d.trades.Length; k++)
                {
                    var tr = d.trades[k];
                    if (tr == null || (tr.get != itemId && tr.get2 != itemId)) continue;
                    var npc = NpcDatabase.Get(d.id);
                    int n = tr.get == itemId ? tr.getN : tr.get2N;
                    var row = UIKit.Row();
                    row.AddToClassList("cook-trade");
                    row.style.alignItems = Align.Center;
                    row.style.marginTop = 6f;
                    row.Add(new UIGlyph(UIGlyphs.Handshake, UIPalette.Violet, 24f));
                    row.Add(UIKit.Text(" " + (npc != null ? npc.Name : "?") + ":  " + tr.giveN + "×", 17f, UIPalette.InkSoft, true));
                    bool valuable = tr.give == HermitDatabase.AnyValuable;
                    var gi = new UIGlyph(valuable ? UIGlyphs.Chest : IconFor(tr.give), valuable ? UIPalette.Sun : Color.white, 26f);
                    gi.style.marginLeft = 4f;
                    row.Add(gi);
                    if (valuable) row.Add(UIKit.Text(" " + Loc.Tr("Schatz", "treasure"), 17f, UIPalette.InkSoft, true));
                    row.Add(UIKit.Text("  →  " + n + "×", 17f, UIPalette.InkSoft, true));
                    var ri = new UIGlyph(IconFor(itemId), Color.white, 26f);
                    ri.style.marginLeft = 4f;
                    row.Add(ri);
                    card.Add(row);
                }
            }
        }
    }
}
