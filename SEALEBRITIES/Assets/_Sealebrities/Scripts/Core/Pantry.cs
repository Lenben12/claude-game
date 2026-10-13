using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Shared food (E.1): snack packs from the chopper / Ada's kiosk go into the pantry (hut fridge, boat cooler box);
    /// the grill takes the CHEAPEST fish from the shared cooler (Q73 "Bonus statt Strafe": never the catch that pays the tank); each palm gives one coconut per in-game day.
    /// Eating applies the buff to the LOCAL player only (Buffs). Shared mutations follow the authority rule: clients
    /// forward CoreCommands.EatKey / GrillKey / CoconutKey and apply optimistically.
    public static class Pantry
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();

        public static int Count(string foodId)
        {
            var s = S;
            if (s == null || foodId == null) return 0;
            for (int i = 0; i < s.pantry.Count; i++) if (s.pantry[i] != null && s.pantry[i].foodId == foodId) return s.pantry[i].count;
            return 0;
        }

        public static int TotalCount
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                int n = 0;
                for (int i = 0; i < s.pantry.Count; i++) if (s.pantry[i] != null) n += Mathf.Max(0, s.pantry[i].count);
                return n;
            }
        }

        /// Adds food to the shared pantry (heli crate, kiosk). SHARED STATE: clients forward EatKey "add".
        public static void Add(string foodId, int n)
        {
            if (FoodDatabase.Get(foodId) == null || n <= 0 || S == null) return;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.EatKey, "add", foodId, n.ToString(Inv));
            AddLocal(foodId, n);
        }

        /// Applies an add without routing (host side of commands, optimistic copies such as Shop.Receive on a client).
        public static void AddLocal(string foodId, int n)
        {
            var s = S;
            if (s == null || n <= 0 || FoodDatabase.Get(foodId) == null) return;
            n = Mathf.Min(n, 99);
            for (int i = 0; i < s.pantry.Count; i++)
            {
                var p = s.pantry[i];
                if (p == null || p.foodId != foodId) continue;
                p.count = Mathf.Min(999, p.count + n);
                Changed?.Invoke();
                return;
            }
            s.pantry.Add(new PantryItem { foodId = foodId, count = n });
            Changed?.Invoke();
        }

        /// Takes one item out of the pantry (no routing). Returns false when none is left.
        internal static bool RemoveLocal(string foodId)
        {
            var s = S;
            if (s == null) return false;
            for (int i = 0; i < s.pantry.Count; i++)
            {
                var p = s.pantry[i];
                if (p == null || p.foodId != foodId || p.count <= 0) continue;
                p.count--;
                if (p.count <= 0) s.pantry.RemoveAt(i);
                Changed?.Invoke();
                return true;
            }
            return false;
        }

        /// The local player eats one item from the pantry (fridge / boat cooler): buff for this player.
        public static bool TryEat(string foodId)
        {
            var def = FoodDatabase.Get(foodId);
            if (def == null || Count(foodId) <= 0) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.EatKey, "eat", foodId);
            if (!RemoveLocal(foodId)) return false;
            Buffs.Apply(def);
            Survival.AddFromFood(def);   // round 3: food also fills hunger / thirst
            return true;
        }

        /// Ada's kiosk (L.8): buys one village dish into the pantry. SHARED STATE (money + pantry): one atomic host
        /// command (CoreCommands.KioskKey) – the host checks the money and adds the dish together, so a quick double press
        /// with too little money can never get a free dish. Clients apply it optimistically (money drops at once).
        public static bool BuyAtKiosk(string foodId)
        {
            var def = FoodDatabase.Get(foodId);
            var gm = GameManager.I;
            var s = S;
            if (def == null || gm == null || s == null || def.source != "kiosk") return false;
            if (!string.IsNullOrEmpty(def.unlockQuest) && !Quests.IsDone(def.unlockQuest)) return false;
            if (s.money < def.price)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Zu wenig Geld", "Not enough money"), Loc.Money(def.price));
                return false;
            }
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CoreCommands.KioskKey, foodId);
                s.money -= def.price;
                GameEvents.RaiseMoneyChanged(s.money);
                AddLocal(foodId, 1);
                return true;
            }
            return HostBuyAtKiosk(foodId);
        }

        /// Host side of the kiosk (own purchase or CoreCommands.KioskKey): spend + add in one step. No routing.
        public static bool HostBuyAtKiosk(string foodId)
        {
            var def = FoodDatabase.Get(foodId);
            var gm = GameManager.I;
            if (def == null || gm == null || S == null || def.source != "kiosk" || !Net.HasAuthority) return false;
            if (!string.IsNullOrEmpty(def.unlockQuest) && !Quests.IsDone(def.unlockQuest)) return false;
            if (!gm.TrySpend(def.price)) return false;
            AddLocal(foodId, 1);
            return true;
        }

        // ---------------------------------------------------------------- grill (E.1)
        /// The fish the grill would take (null = none): the CHEAPEST non-junk fish in the shared cooler – the grill must
        /// never silently eat the best catch. The big-fish bonus stays reachable: the cheapest fish ≥ BigFishKg is taken
        /// instead when it is worth at most 1.5× the cheapest fish.
        public static FishItem BestGrillFish()
        {
            var s = S;
            if (s == null) return null;
            FishItem cheapest = null, cheapestBig = null;
            for (int i = 0; i < s.fishInventory.Count; i++)
            {
                var f = s.fishInventory[i];
                if (f == null) continue;
                var k = FishMarket.Kind(f.kindId);
                if (k == null || k.junk) continue;
                if (cheapest == null || f.value < cheapest.value) cheapest = f;
                if (f.weightKg >= FoodDatabase.BigFishKg && (cheapestBig == null || f.value < cheapestBig.value)) cheapestBig = f;
            }
            if (cheapest != null && cheapestBig != null && cheapestBig.value <= cheapest.value * 1.5f) return cheapestBig;
            return cheapest;
        }

        /// "E – {Fisch} grillen": takes BestGrillFish from the shared cooler. eat = true gives this player the grilled-fish
        /// buff (+5 % for fish ≥ 3 kg) and counts it; eat = false packs it to go (Toni's quest) – no buff. The 6 s
        /// sizzle is the props module's job – call this when it ends.
        public static bool TryGrill(out FishItem fish, bool eat = true)
        {
            fish = BestGrillFish();
            var def = FoodDatabase.Get(FoodDatabase.GrilledFish);
            if (fish == null || def == null)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Kühlbox ist leer", "Cooler is empty"), Loc.Tr("Erst angeln", "Go fishing"));
                return false;
            }
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.GrillKey, fish.uid.ToString(Inv));
            HostGrill(fish.uid);
            if (!eat) return true;
            Buffs.Apply(def, fish.weightKg >= FoodDatabase.BigFishKg ? FoodDatabase.BigFishBonus : 0f);
            Survival.AddFromFood(def);
            var rec = PlayerRecords.Local;
            if (rec != null) { rec.grilled++; Achievements.OnLocalCounter(rec); }
            return true;
        }

        /// Removes the grilled fish from the shared cooler and remembers the grill day (cozy bonus). No routing.
        public static void HostGrill(int uid)
        {
            var s = S;
            if (s == null) return;
            for (int i = 0; i < s.fishInventory.Count; i++)
            {
                if (s.fishInventory[i] == null || s.fishInventory[i].uid != uid) continue;
                s.fishInventory.RemoveAt(i);
                break;
            }
            s.lastGrillDay = s.day;
            FishMarket.RaiseChanged();
        }

        /// Round 3 (backpack „Kühlbox“ tab): takes one fish out of the SHARED cooler as a raw fish into the local backpack
        /// (to cook it at a campfire / the grill). Clients forward CoreCommands.TakeFishKey (uid) and apply optimistically.
        public static bool TakeFishToBackpack(FishItem f)
        {
            var s = S;
            if (s == null || f == null || !s.fishInventory.Contains(f)) return false;
            if (Backpack.SpaceFor(ItemIds.RawFish) <= 0) { Backpack.Add(ItemIds.RawFish, 1); return false; }
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.TakeFishKey, f.uid.ToString(Inv));
            HostTakeFish(f.uid);
            Backpack.Add(ItemIds.RawFish, 1);
            return true;
        }

        /// Removes a fish from the shared cooler (no grill day). No routing.
        public static bool HostTakeFish(int uid)
        {
            var s = S;
            if (s == null) return false;
            for (int i = 0; i < s.fishInventory.Count; i++)
            {
                if (s.fishInventory[i] == null || s.fishInventory[i].uid != uid) continue;
                s.fishInventory.RemoveAt(i);
                FishMarket.RaiseChanged();
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- coconut palms (E.1)
        public static string PalmKey(string palmId) => "palm:" + palmId;

        /// True when this palm still has today's coconut.
        public static bool PalmHasCoconut(string palmId)
        {
            var s = S;
            return s != null && !string.IsNullOrEmpty(palmId) && !s.dailyUsed.Contains(PalmKey(palmId));
        }

        /// "E – Palme schütteln": uses today's coconut of this palm (shared). Returns false when it was already taken.
        public static bool TryShakePalm(string palmId)
        {
            var s = S;
            if (s == null || !PalmHasCoconut(palmId)) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.CoconutKey, palmId);
            s.dailyUsed.Add(PalmKey(palmId));
            return true;
        }

        /// "E – Trinken": the local player drinks a coconut (drink buff + thirst, coconut counter).
        public static void DrinkCoconut()
        {
            Survival.Consume(ItemDatabase.Get(FoodDatabase.Coconut));
        }

        /// Round 3 fridge / kiosk counter: takes every pantry item into the local backpack (as much as fits). SHARED
        /// pantry: clients forward EatKey "eat" per item (the host removes it). Returns how many were taken.
        public static int TakeAllToBackpack()
        {
            var s = S;
            if (s == null) return 0;
            int taken = 0;
            for (int i = s.pantry.Count - 1; i >= 0; i--)
            {
                var p = s.pantry[i];
                if (p == null || p.count <= 0 || FoodDatabase.Get(p.foodId) == null) continue;
                int n = Mathf.Min(p.count, Backpack.SpaceFor(p.foodId));
                for (int k = 0; k < n; k++)
                {
                    string id = p.foodId;
                    if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.EatKey, "eat", id);
                    if (!RemoveLocal(id)) break;
                    Backpack.Add(id, 1, false);
                    taken++;
                }
            }
            return taken;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; }
    }
}
