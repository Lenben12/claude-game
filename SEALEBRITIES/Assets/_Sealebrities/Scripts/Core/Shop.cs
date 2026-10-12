using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop: buying creates a Delivery; the helicopter (Boat module) drops it on the helipad; picking up the crate calls Receive.
    /// Standard delivery arrives the SAME day 2–3 in-game hours later (≈ 3.3–5 real minutes at 40 min/day); orders placed
    /// after 20:00 come the next morning at 07:12 (orders in the small hours: 07:12 the same day). Express arrives after
    /// 45 in-game minutes, day or night. The tutorial tank (tank_t1 while the tutorial runs) always uses the express
    /// timing at the standard price (usually ordered ~13:30–15:00 → arrives ~14:15–15:45, first dive in daylight).
    /// Owned items are shared by the whole co-op crew (one wallet); each player equips their own choice (Gear.Equip).
    ///
    /// Round 2: snack packs (E.1 – FoodDef, delivery id "food:&lt;id&gt;", always the express timing at the standard
    /// price, crate → shared pantry), "Angebot des Tages" (L.12 – one listed item −20 % per day, rolled by the host),
    /// story discounts (rod_t1 −50 % after Dr. Fin R1, uw_radio −50 % after Gus M2), reward-only / story-gated items.
    public static class Shop
    {
        public const float ExpressSurcharge = 0.5f;
        public const float ExpressHours = 0.75f;
        public const float StandardHoursMin = 2f, StandardHoursMax = 3f;
        /// Standard orders placed after this time (20:00) arrive the next morning.
        public const float LastStandardOrder = 20f / 24f;
        /// Morning arrival for late orders (07:12).
        public const float MorningArrival = 0.30f;
        /// L.12 "Angebot des Tages": −20 %.
        public const float DealOfDayDiscount = 0.2f;

        static readonly System.Random rng = new System.Random();

        static GameState S => GameManager.I.State;

        public static bool IsOwned(GearItem item) => item != null && S.ownedItems.Contains(item.id);

        public static bool IsOrdered(GearItem item) => item != null && IsOrdered(item.id);

        public static bool IsOrdered(string itemId)
        {
            if (itemId == null) return false;
            var d = S.deliveries;
            for (int i = 0; i < d.Count; i++) if (d[i] != null && d[i].itemId == itemId) return true;
            return false;
        }

        /// Listed in the shop at all (not a reward-only item; story flag set when one is required).
        public static bool IsListed(GearItem item) => GearDatabase.IsListed(item);

        /// Today's "Angebot des Tages" item id (null = none).
        public static string DealOfTheDay => GameManager.I != null ? S.dealOfDayItem : null;

        public static bool IsDealOfTheDay(GearItem item) => item != null && item.id == DealOfTheDay;

        /// Price before the express surcharge: story discounts and the deal of the day applied.
        public static long BasePrice(GearItem item)
        {
            if (item == null) return 0;
            float p = item.price;
            if (item.id == "rod_t1" && StoryFlags.Has(StoryKeys.RodT1Discount)) p *= 0.5f;
            if (item.id == "uw_radio" && (StoryFlags.Has(StoryKeys.RadioDiscount) || Missions.IsDone("M2"))) p *= 0.5f;
            if (IsDealOfTheDay(item)) p *= 1f - DealOfDayDiscount;
            return (long)Mathf.Round(p);
        }

        public static long Price(GearItem item, bool express) => express ? (long)(BasePrice(item) * (1f + ExpressSurcharge)) : BasePrice(item);

        public static bool CanBuy(GearItem item, bool express, out string reason)
        {
            reason = null;
            if (item == null) { reason = "?"; return false; }
            if (item.comingSoon) { reason = Loc.Tr("Bald verfügbar", "Coming soon"); return false; }
            if (!IsListed(item)) { reason = Loc.Tr("Nicht im Shop", "Not sold in the shop"); return false; }
            if (IsOwned(item)) { reason = Loc.Tr("Besitzt du schon", "Already owned"); return false; }
            if (IsOrdered(item)) { reason = Loc.Tr("Unterwegs", "On its way"); return false; }
            if (S.followers < item.followersNeeded) { reason = Loc.Tr($"Ab {Loc.Number(item.followersNeeded)} Followern", $"From {Loc.Number(item.followersNeeded)} followers"); return false; }
            if (S.money < Price(item, express)) { reason = Loc.Tr("Zu wenig Geld", "Not enough money"); return false; }
            return true;
        }

        /// The tutorial tank always flies with the express timing (standard price): the first dive must happen in
        /// daylight and the tutorial can never force a sleep, however late the player orders it.
        public static bool FastDelivery(GearItem item, bool express) =>
            express || (item != null && item.id == "tank_t1" && TutorialQuests.TutorialRunning);

        /// Arrival (day, time of day) for an order placed now.
        public static void ArrivalFor(bool express, out int day, out float time)
        {
            float now = S.timeOfDay;
            day = S.day;
            if (express)
            {
                time = now + ExpressHours / 24f;
            }
            else
            {
                if (now > LastStandardOrder)
                {
                    day += 1;
                    time = MorningArrival;
                    return;
                }
                float h;
                lock (rng) h = Mathf.Lerp(StandardHoursMin, StandardHoursMax, (float)rng.NextDouble());
                time = now + h / 24f;
                if (time < MorningArrival) time = MorningArrival;   // ordered in the small hours → first flight of the day
            }
            if (time >= 1f)
            {
                day += 1;
                time -= 1f;
            }
        }

        /// SHARED STATE: on co-op clients this sends NetCommand.Buy (itemId, express) to the host.
        public static bool Buy(GearItem item, bool express)
        {
            if (GameManager.I == null) return false;
            if (!CanBuy(item, express, out var reason)) { GameEvents.RaiseNotify(NotifyKind.Warning, reason); return false; }
            if (Net.IsClient)
            {
                // the host answers with one toast ("ordered – chopper arrives at 19:30" or why it was refused)
                Net.Command(NetCommand.Buy, item.id, express ? "1" : "0");
                // optimistic: the order and the price show right away (the shop cannot be ordered twice while waiting);
                // the host's next snapshot replaces both (NetState keeps this Delivery object and copies the host's
                // arrival time into it, or removes it when the host refused the order)
                ArrivalFor(FastDelivery(item, express), out int cday, out float ctime);
                S.deliveries.Add(new Delivery { itemId = item.id, express = express, arrivalDay = cday, arrivalTime = ctime });
                S.money = System.Math.Max(0, S.money - Price(item, express));
                GameEvents.RaiseMoneyChanged(S.money);
                GameEvents.RaiseDeliveryOrdered(item);
                // the confirmation in this player's own language (the host only forwards a refusal)
                OrderedToast(item.Name, cday, ctime);
                return true;
            }
            if (!GameManager.I.TrySpend(Price(item, express))) return false;
            ArrivalFor(FastDelivery(item, express), out int day, out float time);
            var d = new Delivery { itemId = item.id, express = express, arrivalDay = day, arrivalTime = time };
            S.deliveries.Add(d);
            GameEvents.RaiseDeliveryOrdered(item);
            // a guest's order (executing on the host): the guest already showed its own localized confirmation
            if (!(Net.IsHost && NetCommands.ScopeFrom > 0)) OrderedToast(item.Name, day, time);
            GameManager.I.Save();
            return true;
        }

        // ---------------------------------------------------------------- snacks (E.1)
        public static bool CanBuyFood(FoodDef f, out string reason)
        {
            reason = null;
            if (f == null || !f.SoldByChopper) { reason = Loc.Tr("Nicht im Shop", "Not sold in the shop"); return false; }
            if (IsOrdered(FoodDatabase.DeliveryId(f))) { reason = Loc.Tr("Unterwegs", "On its way"); return false; }
            if (S.followers < f.followersNeeded) { reason = Loc.Tr($"Ab {Loc.Number(f.followersNeeded)} Followern", $"From {Loc.Number(f.followersNeeded)} followers"); return false; }
            if (S.money < f.price) { reason = Loc.Tr("Zu wenig Geld", "Not enough money"); return false; }
            return true;
        }

        /// Orders a pack of snacks (packSize items) – always the express timing at the standard price (E.1).
        /// SHARED STATE: clients send NetCommand.Buy ("food:&lt;id&gt;", express).
        public static bool BuyFood(FoodDef f, bool express = true)
        {
            if (GameManager.I == null) return false;
            if (!CanBuyFood(f, out var reason)) { GameEvents.RaiseNotify(NotifyKind.Warning, reason); return false; }
            string id = FoodDatabase.DeliveryId(f);
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Buy, id, "1");
                ArrivalFor(true, out int cday, out float ctime);
                S.deliveries.Add(new Delivery { itemId = id, express = true, arrivalDay = cday, arrivalTime = ctime });
                S.money = System.Math.Max(0, S.money - f.price);
                GameEvents.RaiseMoneyChanged(S.money);
                OrderedToast(f.Name, cday, ctime);
                return true;
            }
            if (!GameManager.I.TrySpend(f.price)) return false;
            ArrivalFor(true, out int day, out float time);
            S.deliveries.Add(new Delivery { itemId = id, express = true, arrivalDay = day, arrivalTime = time });
            if (!(Net.IsHost && NetCommands.ScopeFrom > 0)) OrderedToast(f.Name, day, time);
            GameManager.I.Save();
            return true;
        }

        // ---------------------------------------------------------------- backpack items (round 3: spears, speargun)
        public static bool CanBuyItem(ItemDef d, bool express, out string reason)
        {
            reason = null;
            if (d == null || !d.sold || d.price <= 0) { reason = Loc.Tr("Nicht im Shop", "Not sold in the shop"); return false; }
            if (IsOrdered(ItemDatabase.DeliveryId(d))) { reason = Loc.Tr("Unterwegs", "On its way"); return false; }
            if (S.followers < d.followersNeeded) { reason = Loc.Tr($"Ab {Loc.Number(d.followersNeeded)} Followern", $"From {Loc.Number(d.followersNeeded)} followers"); return false; }
            if (S.money < ItemPrice(d, express)) { reason = Loc.Tr("Zu wenig Geld", "Not enough money"); return false; }
            return true;
        }

        public static long ItemPrice(ItemDef d, bool express) => d == null ? 0 : express ? (long)(d.price * (1f + ExpressSurcharge)) : d.price;

        /// Orders a backpack item (spear, speargun). The crate goes into the OPENER's backpack.
        /// SHARED STATE: clients send NetCommand.Buy ("item:&lt;id&gt;", express).
        public static bool BuyItem(ItemDef d, bool express)
        {
            if (GameManager.I == null) return false;
            if (!CanBuyItem(d, express, out var reason)) { GameEvents.RaiseNotify(NotifyKind.Warning, reason); return false; }
            string id = ItemDatabase.DeliveryId(d);
            long price = ItemPrice(d, express);
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Buy, id, express ? "1" : "0");
                ArrivalFor(express, out int cday, out float ctime);
                S.deliveries.Add(new Delivery { itemId = id, express = express, arrivalDay = cday, arrivalTime = ctime });
                S.money = System.Math.Max(0, S.money - price);
                GameEvents.RaiseMoneyChanged(S.money);
                OrderedToast(d.Name, cday, ctime);
                return true;
            }
            if (!GameManager.I.TrySpend(price)) return false;
            ArrivalFor(express, out int day, out float time);
            S.deliveries.Add(new Delivery { itemId = id, express = express, arrivalDay = day, arrivalTime = time });
            if (!(Net.IsHost && NetCommands.ScopeFrom > 0)) OrderedToast(d.Name, day, time);
            GameManager.I.Save();
            return true;
        }

        static void OrderedToast(string name, int day, float time)
        {
            string clock = GameManager.Clock(time);
            if (day > S.day)
                GameEvents.RaiseNotify(NotifyKind.Delivery, Loc.Tr($"{name} bestellt", $"{name} ordered"), Loc.Tr($"morgen {clock}", $"tomorrow {clock}"));
            else
                GameEvents.RaiseNotify(NotifyKind.Delivery, Loc.Tr($"{name} bestellt", $"{name} ordered"), clock);
        }

        /// Display name of a delivery (gear or snack pack).
        public static string DeliveryName(Delivery d)
        {
            if (d == null) return "";
            var f = FoodDatabase.FromDeliveryId(d.itemId);
            if (f != null) return f.Name;
            var it = ItemDatabase.FromDeliveryId(d.itemId);
            if (it != null) return it.Name;
            var g = GearDatabase.Get(d.itemId);
            return g != null ? g.Name : d.itemId;
        }

        /// True when the delivery's arrival time has passed.
        public static bool IsDue(Delivery d) => S.day > d.arrivalDay || (S.day == d.arrivalDay && S.timeOfDay >= d.arrivalTime);

        /// Real seconds until a delivery is due (0 when due).
        public static float SecondsUntil(Delivery d)
        {
            if (d == null || IsDue(d)) return 0f;
            float dt = (d.arrivalDay - S.day) + (d.arrivalTime - S.timeOfDay);
            return Mathf.Max(0f, dt * GameManager.DayLengthSeconds);
        }

        /// First delivery of an item (prefers a crate already on the helipad).
        public static Delivery FindDelivery(string itemId)
        {
            Delivery any = null;
            var list = S.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d == null || d.itemId != itemId) continue;
                if (d.dropped) return d;
                if (any == null) any = d;
            }
            return any;
        }

        /// The delivery of an item that arrives at exactly this day / time (same rule as HeliService.Same) – the host
        /// resolves a guest's OpenCrate with it, so opening crate B never removes crate A of the same snack pack.
        public static Delivery FindDelivery(string itemId, int day, float time)
        {
            var list = S.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d != null && d.itemId == itemId && d.arrivalDay == day && Mathf.Abs(d.arrivalTime - time) < 0.002f) return d;
            }
            return null;
        }

        /// Called when the LOCAL player picks up the crate. The opener switches to the new item right away
        /// (Gear.ClearChoice); other players keep their own equipment choice. Round 3: snack packs and backpack items
        /// (spears) go into the OPENER's backpack (what does not fit: snacks into the shared fridge, items on the ground).
        /// SHARED STATE: on co-op clients this sends NetCommand.OpenCrate (itemId) to the host.
        public static void Receive(Delivery d)
        {
            if (d == null || GameManager.I == null) return;
            if (Net.IsClient)   // applied optimistically below; day + time identify the crate among equal orders
                Net.Command(NetCommand.OpenCrate, d.itemId, d.arrivalDay.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            d.arrivalTime.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            var item = GearDatabase.Get(d.itemId);
            if (item != null) Gear.ClearChoice(item.slot);
            ToOpenersBackpack(d);
            Apply(d, item);
        }

        /// Host side of a client's NetCommand.OpenCrate (does not touch the host player's equipment choice or backpack).
        public static void ReceiveForRemote(Delivery d)
        {
            if (d == null || GameManager.I == null || !Net.HasAuthority) return;
            Apply(d, GearDatabase.Get(d.itemId));
        }

        /// Round 3: the crate's snacks / items go into the local opener's backpack.
        static void ToOpenersBackpack(Delivery d)
        {
            var food = FoodDatabase.FromDeliveryId(d.itemId);
            if (food != null)
            {
                int n = Mathf.Max(1, food.packSize);
                int added = Backpack.Add(food.id, n, false);
                if (added < n) Pantry.Add(food.id, n - added);   // the rest waits in the fridge
                GameEvents.RaiseNotify(NotifyKind.Delivery, food.Name, "+" + n + (added < n ? Loc.Tr(" · Rest im Kühlschrank", " · rest in the fridge") : Loc.Tr(" im Rucksack", " in the backpack")));
                return;
            }
            var it = ItemDatabase.FromDeliveryId(d.itemId);
            if (it == null) return;
            int count = Mathf.Max(1, it.packSize);
            for (int i = 0; i < count; i++)
            {
                if (Backpack.Add(it.id, 1, false) > 0) continue;
                // backpack full: the item lands in front of the player
                Backpack.DropHandler?.Invoke(new InvStack { id = it.id, count = 1, durability = it.HasDurability ? it.durability : -1f, sharpness = it.Sharpenable ? it.sharpness : -1f },
                    Refs.Player != null && Refs.Player.Root != null ? Refs.Player.Root.position + Vector3.up : Vector3.zero);
            }
            GameEvents.RaiseNotify(NotifyKind.Delivery, it.Name, Loc.Tr("im Rucksack", "in the backpack"));
            GameEvents.RaiseBanner(Loc.Tr("Neu im Rucksack: ", "New in the backpack: ") + it.Name, new Color(1f, 0.55f, 0.1f));
        }

        static void Apply(Delivery d, GearItem item)
        {
            S.deliveries.Remove(d);
            if (FoodDatabase.FromDeliveryId(d.itemId) != null || ItemDatabase.FromDeliveryId(d.itemId) != null)
            {
                GameManager.I.Save();
                return;
            }
            if (item == null) return;
            if (!S.ownedItems.Contains(item.id)) S.ownedItems.Add(item.id);
            GameEvents.RaiseDeliveryReceived(item);
            GameEvents.RaiseGearChanged();
            GameEvents.RaiseBanner(Loc.Tr("Neu ausgerüstet: ", "New gear: ") + item.Name, new Color(1f, 0.55f, 0.1f));
            GameManager.I.Save();
        }

        // ---------------------------------------------------------------- deal of the day (L.12)
        static readonly System.Collections.Generic.List<GearItem> dealCandidates = new System.Collections.Generic.List<GearItem>();

        /// Host, new day / new game: picks one listed, not owned item the channel can already buy (seeded by save + day).
        public static void RollDealOfTheDay(GameState s)
        {
            if (s == null || !Net.HasAuthority) return;
            dealCandidates.Clear();
            var all = GearDatabase.All;
            for (int i = 0; i < all.Count; i++)
            {
                var g = all[i];
                if (g.comingSoon || !GearDatabase.IsListed(g) || g.price <= 0 || s.ownedItems.Contains(g.id)) continue;
                if (g.followersNeeded > s.followers) continue;
                if (g.id == "tank_t1" && s.day <= 1) continue;   // the tutorial tank keeps its 100 $
                dealCandidates.Add(g);
            }
            s.dealOfDayItem = dealCandidates.Count > 0 ? dealCandidates[s.SeededRandom(2000 + s.day).Next(dealCandidates.Count)].id : null;
            dealCandidates.Clear();
        }
    }
}
