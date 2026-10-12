using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 survival (GDD §19 „Hunger und Durst als echte Bedürfnisse“) – gentle but noticeable, never deadly.
    ///  * Hunger and thirst of the LOCAL player (0..100, GameState.hunger / thirst, per player – co-op guests keep them
    ///    in the host's PlayerRecord). They sink with the in-game clock: hunger 100 → 0 in 36 in-game hours, thirst in
    ///    24 (swimming / diving ×1.3, Relaxed ×0.6, Hard ×1.25); a night's sleep costs 15 / 20.
    ///  * Below 25 the diver is slower and has less air (Buffs.Multiplier carries SpeedFactor / AirFactor: down to
    ///    ×0.8 at 0). One toast per crossing (25, 10). No death, no damage.
    ///  * Food and drink: Consume (backpack „Essen / Trinken“, LMB with food in hand). Raw fish / meat must be cooked
    ///    first (CookOne at a lit campfire or the grill), seawater must be filtered (FilterPut / FilterTake at the hut's
    ///    water filter, 45 in-game minutes). An empty bottle is filled in the sea (FillBottle).
    ///  * Campfires (shared, host authority): fixed spots (props), „Lagerfeuer bauen“ costs 2 driftwood and burns for 3
    ///    in-game hours (TryLightCampfire → CoreCommands.CampfireKey).
    public static class Survival
    {
        public const float Max = 100f;
        /// In-game hours from full to empty.
        public const float HungerHours = 36f, ThirstHours = 24f;
        /// Below this the penalty starts (and the HUD icon turns orange).
        public const float Low = 25f, VeryLow = 10f;
        /// Slowest / least air at 0.
        public const float MinFactor = 0.8f;
        public const float InWaterRate = 1.3f;
        public const float SleepHunger = 15f, SleepThirst = 20f;
        /// Water filter: in-game minutes per batch.
        public const float FilterMinutes = 45f;
        /// Campfire: driftwood needed, in-game hours it burns.
        public const int CampfireWood = 2;
        public const float CampfireHours = 3f;
        /// Seconds of cooking per item at a campfire / grill (the props' timed action).
        public const float CookSeconds = 4f;

        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// Raised when a shown value (whole points) or a threshold changes.
        public static event Action Changed;

        static int shownHunger = -1, shownThirst = -1, factorStep = -1;
        static bool warnedHunger, warnedThirst, warnedHungerBad, warnedThirstBad;
        static bool hooked;

        public static float Hunger => S != null ? S.hunger : Max;
        public static float Thirst => S != null ? S.thirst : Max;
        public static float Hunger01 => Mathf.Clamp01(Hunger / Max);
        public static float Thirst01 => Mathf.Clamp01(Thirst / Max);
        public static bool Hungry => Hunger < Low;
        public static bool Thirsty => Thirst < Low;

        /// Absolute in-game time (day + time of day) – timers of the filter and the campfires.
        public static float Now
        {
            get
            {
                var gm = GameManager.I;
                return gm == null || gm.State == null ? 0f : gm.State.day + gm.State.timeOfDay;
            }
        }

        static float Penalty(float v) => v >= Low ? 1f : Mathf.Lerp(MinFactor, 1f, Mathf.Clamp01(v / Low));

        /// Walk / swim speed factor (1 = fine, 0.8 = starving and parched).
        public static float SpeedFactor
        {
            get
            {
                var s = S;
                if (s == null) return 1f;
                return Mathf.Max(MinFactor, Penalty(s.hunger) * Penalty(s.thirst));
            }
        }

        /// Air capacity factor (thirst weighs more under water).
        public static float AirFactor
        {
            get
            {
                var s = S;
                if (s == null) return 1f;
                return Mathf.Max(MinFactor, Mathf.Min(Penalty(s.thirst), Mathf.Lerp(1f, Penalty(s.hunger), 0.5f)));
            }
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            GameEvents.SleepStarted += OnSleep;
            GameEvents.GameStarted += OnGameStarted;
        }

        static void OnGameStarted()
        {
            shownHunger = shownThirst = factorStep = -1;
            var s = S;
            warnedHunger = s != null && s.hunger < Low;
            warnedThirst = s != null && s.thirst < Low;
            warnedHungerBad = s != null && s.hunger < VeryLow;
            warnedThirstBad = s != null && s.thirst < VeryLow;
            Changed?.Invoke();
        }

        /// GameManager.Update (scaled time, paused with the game offline).
        public static void Tick(float dt)
        {
            Hook();
            var s = S;
            if (s == null || dt <= 0f || float.IsNaN(dt)) return;
            float hours = dt / GameManager.DayLengthSeconds * 24f;
            float rate = DifficultyRate(s.difficulty);
            var p = Refs.Player;
            bool water = p != null && !(p is UnityEngine.Object o && o == null) && p.IsInWater;
            float hungerLoss = hours * Max / HungerHours * rate * (water ? InWaterRate : 1f);
            float thirstLoss = hours * Max / ThirstHours * rate;
            if (Net.IsOnline && (p == null || UIState.MainMenuOpen)) return;
            s.hunger = Mathf.Clamp(s.hunger - hungerLoss, 0f, Max);
            s.thirst = Mathf.Clamp(s.thirst - thirstLoss, 0f, Max);
            Refresh(s);
        }

        static float DifficultyRate(Difficulty d)
        {
            switch (DifficultySettings.Normalize(d))
            {
                case Difficulty.Snorkeler: return 0.6f;
                case Difficulty.OpenWater: return 1f;
                default: return 1.25f;
            }
        }

        /// Toasts on the thresholds, Changed for the HUD and the penalty step for the player's stats.
        static void Refresh(GameState s)
        {
            int h = Mathf.CeilToInt(s.hunger), t = Mathf.CeilToInt(s.thirst);
            bool changed = h != shownHunger || t != shownThirst;
            shownHunger = h;
            shownThirst = t;
            CheckWarn(s.hunger, ref warnedHunger, ref warnedHungerBad, true);
            CheckWarn(s.thirst, ref warnedThirst, ref warnedThirstBad, false);
            int step = Mathf.RoundToInt(SpeedFactor * 50f) * 100 + Mathf.RoundToInt(AirFactor * 50f);
            if (step != factorStep)
            {
                bool first = factorStep < 0;
                factorStep = step;
                if (!first) Buffs.RaiseChanged();   // the player re-reads air capacity / speed
            }
            if (changed) Changed?.Invoke();
        }

        static void CheckWarn(float v, ref bool warned, ref bool warnedBad, bool hunger)
        {
            if (v >= Low + 5f) { warned = false; warnedBad = false; return; }
            if (!warned && v < Low)
            {
                warned = true;
                GameEvents.RaiseNotify(NotifyKind.Warning, hunger ? Loc.Tr("Hunger", "Hungry") : Loc.Tr("Durst", "Thirsty"),
                    hunger ? Loc.Tr("Etwas essen", "Eat something") : Loc.Tr("Etwas trinken", "Drink something"));
            }
            if (!warnedBad && v < VeryLow)
            {
                warnedBad = true;
                GameEvents.RaiseNotify(NotifyKind.Warning, hunger ? Loc.Tr("Magen knurrt!", "Stomach growls!") : Loc.Tr("Ganz trocken!", "Parched!"),
                    Loc.Tr("Langsamer · weniger Luft", "Slower · less air"));
            }
        }

        static void OnSleep() => ApplyNight();

        /// A night's sleep costs some hunger / thirst (never below 15 from it). Host / offline: GameEvents.SleepStarted;
        /// co-op guests: the Net module's sleep event.
        public static void ApplyNight()
        {
            var s = S;
            if (s == null) return;
            s.hunger = Mathf.Max(Mathf.Min(s.hunger, 15f), s.hunger - SleepHunger);
            s.thirst = Mathf.Max(Mathf.Min(s.thirst, 15f), s.thirst - SleepThirst);
            Refresh(s);
        }

        /// Sets both meters (tests, debug, a new co-op guest).
        public static void Set(float hunger, float thirst)
        {
            var s = S;
            if (s == null) return;
            s.hunger = Mathf.Clamp(float.IsNaN(hunger) ? Max : hunger, 0f, Max);
            s.thirst = Mathf.Clamp(float.IsNaN(thirst) ? Max : thirst, 0f, Max);
            Refresh(s);
        }

        // ------------------------------------------------------------------ eating / drinking
        /// Hunger / thirst points an item restores.
        public static void ValuesOf(ItemDef d, out float hunger, out float thirst)
        {
            hunger = d != null ? d.hunger : 0f;
            thirst = d != null ? d.thirst : 0f;
            var f = d != null ? d.Food : null;
            if (f != null) { hunger += f.hunger; thirst += f.thirst; }
        }

        /// Hunger / thirst of a food eaten somewhere else (fridge / boat cooler list, the grill's eat-now path) – the buff
        /// is applied by the caller.
        public static void AddFromFood(FoodDef f)
        {
            var s = S;
            if (s == null || f == null) return;
            s.hunger = Mathf.Clamp(s.hunger + Mathf.Max(0f, f.hunger), 0f, Max);
            s.thirst = Mathf.Clamp(s.thirst + Mathf.Max(0f, f.thirst), 0f, Max);
            Refresh(s);
        }

        /// The local diver's head is under water (no eating or drinking down there).
        public static bool LocalUnderwater
        {
            get
            {
                var p = Refs.Player;
                return p != null && !(p is UnityEngine.Object o && o == null) && p.IsUnderwater;
            }
        }

        /// The local player eats / drinks one item (the caller removes it from the backpack). Food buffs still apply.
        /// False for raw food (cook it first), things that are not edible and under water.
        public static bool Consume(ItemDef d)
        {
            var s = S;
            if (s == null || d == null || !d.Edible) return false;
            if (LocalUnderwater)
            {
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Nicht unter Wasser", "Not under water"), Loc.Tr("Erst auftauchen", "Surface first"));
                return false;
            }
            ValuesOf(d, out float h, out float t);
            s.hunger = Mathf.Clamp(s.hunger + h, 0f, Max);
            s.thirst = Mathf.Clamp(s.thirst + t, 0f, Max);
            var f = d.Food;
            if (f != null)
            {
                Buffs.Apply(f);   // raises FoodEaten (first-person eating) – also for buff-less food
                if (f.id == FoodDatabase.Coconut)
                {
                    var rec = PlayerRecords.Local;
                    if (rec != null) { rec.coconuts++; Achievements.OnLocalCounter(rec); }
                }
            }
            string chip = (h > 0f ? "+" + Mathf.RoundToInt(h).ToString(Inv) + " " + Loc.Tr("Hunger", "food") : "")
                          + (h > 0f && t > 0f ? " · " : "") + (t > 0f ? "+" + Mathf.RoundToInt(t).ToString(Inv) + " " + Loc.Tr("Durst", "water") : "");
            GameEvents.RaiseNotify(NotifyKind.Info, d.Name, chip.Length > 0 ? chip : null);
            Refresh(s);
            return true;
        }

        // ------------------------------------------------------------------ water
        /// True when the local player can fill a bottle right here: in the water, on the boat, or standing at the
        /// shore / on the dock with the sea right in front.
        public static bool NearSeaWater()
        {
            var p = Refs.Player;
            if (p == null || (p is UnityEngine.Object o && o == null) || p.Root == null) return false;
            if (p.IsInWater) return true;
            var b = Refs.Boat;
            if (b != null && !(b is UnityEngine.Object bo && bo == null) && b.PlayerAboard) return true;
            Vector3 pos = p.Root.position;
            if (pos.y - Waves.SeaLevel > 3.2f) return false;
            Vector3 fwd = p.View != null ? p.View.transform.forward : p.Root.forward;
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            for (int i = 1; i <= 3; i++)
            {
                Vector3 q = pos + fwd * (0.9f * i);
                if (WorldInfo.GroundHeight(q) < Waves.SeaLevel - 0.25f) return true;
            }
            return false;
        }

        /// Uses one empty bottle: seawater in. Toast when no sea is near.
        public static bool FillBottle()
        {
            if (!Backpack.Has(ItemIds.BottleEmpty)) return false;
            if (!NearSeaWater())
            {
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Kein Meer hier", "No sea here"), Loc.Tr("Im Wasser benutzen", "Use it in the water"));
                return false;
            }
            Backpack.Remove(ItemIds.BottleEmpty, 1);
            Backpack.Add(ItemIds.BottleSea, 1, false);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Meerwasser abgefüllt", "Seawater bottled"), Loc.Tr("Filtern in der Hütte", "Filter it in the hut"));
            return true;
        }

        /// Bottles waiting in the filter (ready or not).
        public static int FilterCount
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                int n = 0;
                for (int i = 0; i < s.filterJobs.Count; i++) if (s.filterJobs[i] != null) n += s.filterJobs[i].count;
                return n;
            }
        }

        public static int FilterReadyCount
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                float now = Now;
                int n = 0;
                for (int i = 0; i < s.filterJobs.Count; i++) if (s.filterJobs[i] != null && s.filterJobs[i].readyAt <= now) n += s.filterJobs[i].count;
                return n;
            }
        }

        /// Real seconds until the next batch is ready (0 = something is ready or nothing waits).
        public static float FilterSecondsLeft
        {
            get
            {
                var s = S;
                if (s == null || s.filterJobs.Count == 0) return 0f;
                float now = Now, next = float.MaxValue;
                for (int i = 0; i < s.filterJobs.Count; i++)
                {
                    var j = s.filterJobs[i];
                    if (j == null) continue;
                    if (j.readyAt <= now) return 0f;
                    next = Mathf.Min(next, j.readyAt);
                }
                return next == float.MaxValue ? 0f : (next - now) * GameManager.DayLengthSeconds;
            }
        }

        /// Puts every seawater bottle of the backpack into the filter. Returns how many.
        public static int FilterPut()
        {
            var s = S;
            int n = Backpack.Count(ItemIds.BottleSea);
            if (s == null || n <= 0) return 0;
            Backpack.Remove(ItemIds.BottleSea, n);
            s.filterJobs.Add(new FilterJob { count = n, readyAt = Now + FilterMinutes / 1440f });
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Wasser filtert …", "Filtering water…"), Mathf.RoundToInt(FilterMinutes) + " min");
            return n;
        }

        /// Takes the ready bottles out of the filter (fresh water into the backpack). Returns how many.
        public static int FilterTake()
        {
            var s = S;
            if (s == null) return 0;
            float now = Now;
            int taken = 0;
            for (int i = s.filterJobs.Count - 1; i >= 0; i--)
            {
                var j = s.filterJobs[i];
                if (j == null || j.readyAt > now) continue;
                int room = Backpack.SpaceFor(ItemIds.BottleFresh);
                int n = Mathf.Min(room, j.count);
                if (n <= 0) break;
                Backpack.Add(ItemIds.BottleFresh, n, false);
                taken += n;
                j.count -= n;
                if (j.count <= 0) s.filterJobs.RemoveAt(i);
            }
            if (taken > 0) GameEvents.RaiseNotify(NotifyKind.Collectible, Loc.Tr("Trinkwasser", "Fresh water"), "+" + taken.ToString(Inv));
            return taken;
        }

        /// Loading a save rewinds the clock to the morning: everything in the filter is done by then.
        public static void FinishFilterAfterLoad(GameState s)
        {
            if (s == null || s.filterJobs == null) return;
            for (int i = 0; i < s.filterJobs.Count; i++) if (s.filterJobs[i] != null) s.filterJobs[i].readyAt = 0f;
        }

        // ------------------------------------------------------------------ cooking
        /// First raw item in the backpack (null = nothing to cook).
        public static string FirstRaw()
        {
            var l = Backpack.Stacks;
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i];
                if (st == null || st.count <= 0) continue;
                var d = st.Def;
                if (d != null && d.category == ItemCategory.Raw && !string.IsNullOrEmpty(d.cookTo)) return d.id;
            }
            return null;
        }

        public static int RawCount
        {
            get
            {
                int n = 0;
                var l = Backpack.Stacks;
                for (int i = 0; i < l.Count; i++)
                {
                    var st = l[i];
                    if (st == null || st.count <= 0) continue;
                    var d = st.Def;
                    if (d != null && d.category == ItemCategory.Raw) n += st.count;
                }
                return n;
            }
        }

        /// The hermits' recipe (NpcKeys.RecipeCurry): raw fish + coconut → this food.
        public const string CurryId = "coconut_curry";

        /// Cooks one raw item of the backpack (campfire / grill finished its sizzle). The cooked item goes into the
        /// backpack; the shared "grilled today" day is set (cozy bonus E.2).
        public static bool CookOne()
        {
            string raw = FirstRaw();
            var d = ItemDatabase.Get(raw);
            if (d == null) return false;
            // round 3 (GDD §20 Q8): the hermits' cook taught the curry – raw fish + a coconut become a coconut fish curry
            if (raw == ItemIds.RawFish && StoryFlags.Has(NpcKeys.RecipeCurry) && Backpack.Has(FoodDatabase.Coconut) && ItemDatabase.Get(CurryId) != null)
            {
                Backpack.Remove(raw, 1);
                Backpack.Remove(FoodDatabase.Coconut, 1);
                Backpack.Add(CurryId, 1);
                MarkGrilledToday();
                return true;
            }
            Backpack.Remove(raw, 1);
            Backpack.Add(d.cookTo, 1);
            MarkGrilledToday();
            return true;
        }

        /// Shared lastGrillDay (cozy bonus) – clients forward CoreCommands.GrillKey with uid −1 (no fish removed).
        public static void MarkGrilledToday()
        {
            var s = S;
            if (s == null) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.GrillKey, "-1");
            s.lastGrillDay = s.day;
            var rec = PlayerRecords.Local;
            if (rec != null) { rec.grilled++; Achievements.OnLocalCounter(rec); }
        }

        // ------------------------------------------------------------------ campfires (shared)
        public static CampfireState Campfire(string id)
        {
            var s = GameManager.I != null ? GameManager.I.State : null;
            if (s == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s.campfires.Count; i++) if (s.campfires[i] != null && s.campfires[i].id == id) return s.campfires[i];
            return null;
        }

        public static bool CampfireLit(string id)
        {
            var c = Campfire(id);
            return c != null && c.until > Now;
        }

        /// In-game hours left (0 = out).
        public static float CampfireHoursLeft(string id)
        {
            var c = Campfire(id);
            return c == null ? 0f : Mathf.Max(0f, (c.until - Now) * 24f);
        }

        /// „Lagerfeuer bauen“: 2 driftwood from the local backpack, the fire is shared. False (toast) without wood.
        public static bool TryLightCampfire(string id)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return false;
            if (CampfireLit(id)) return true;
            if (!Backpack.Has(ItemIds.Wood, CampfireWood))
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Zu wenig Treibholz", "Not enough driftwood"), Backpack.Count(ItemIds.Wood) + "/" + CampfireWood);
                return false;
            }
            Backpack.Remove(ItemIds.Wood, CampfireWood);
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.CampfireKey, id);
            HostLight(id);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Lagerfeuer brennt", "Campfire lit"), Mathf.RoundToInt(CampfireHours) + " h");
            return true;
        }

        /// Host side (and optimistic copy on the client): the fire burns for CampfireHours from now.
        public static bool HostLight(string id)
        {
            var s = GameManager.I != null ? GameManager.I.State : null;
            if (s == null || string.IsNullOrEmpty(id) || id.Length > 64) return false;
            var c = Campfire(id);
            if (c == null) s.campfires.Add(c = new CampfireState { id = id });
            c.until = Now + CampfireHours / 24f;
            // old fires are forgotten (the list stays small)
            float now = Now;
            for (int i = s.campfires.Count - 1; i >= 0; i--) if (s.campfires[i] != null && s.campfires[i].until < now - 1f) s.campfires.RemoveAt(i);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            hooked = false;
            shownHunger = shownThirst = factorStep = -1;
            warnedHunger = warnedThirst = warnedHungerBad = warnedThirstBad = false;
        }
    }
}
