using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop: buying creates a Delivery; the helicopter (Boat module) drops it on the helipad; picking up the crate calls Receive.
    /// Standard delivery arrives the SAME day 2–3 in-game hours later (≈ 3.3–5 real minutes at 40 min/day); orders placed
    /// after 20:00 come the next morning at 07:12 (orders in the small hours: 07:12 the same day). Express arrives after
    /// 45 in-game minutes, day or night. The tutorial tank (tank_t1 while the tutorial runs) always uses the express
    /// timing at the standard price (usually ordered ~13:30–15:00 → arrives ~14:15–15:45, first dive in daylight).
    /// Owned items are shared by the whole co-op crew (one wallet); each player equips their own choice (Gear.Equip).
    public static class Shop
    {
        public const float ExpressSurcharge = 0.5f;
        public const float ExpressHours = 0.75f;
        public const float StandardHoursMin = 2f, StandardHoursMax = 3f;
        /// Standard orders placed after this time (20:00) arrive the next morning.
        public const float LastStandardOrder = 20f / 24f;
        /// Morning arrival for late orders (07:12).
        public const float MorningArrival = 0.30f;

        static readonly System.Random rng = new System.Random();

        static GameState S => GameManager.I.State;

        public static bool IsOwned(GearItem item) => item != null && S.ownedItems.Contains(item.id);

        public static bool IsOrdered(GearItem item)
        {
            if (item == null) return false;
            var d = S.deliveries;
            for (int i = 0; i < d.Count; i++) if (d[i] != null && d[i].itemId == item.id) return true;
            return false;
        }

        public static long Price(GearItem item, bool express) => express ? (long)(item.price * (1f + ExpressSurcharge)) : item.price;

        public static bool CanBuy(GearItem item, bool express, out string reason)
        {
            reason = null;
            if (item == null) { reason = "?"; return false; }
            if (item.comingSoon) { reason = Loc.Tr("Bald verfügbar", "Coming soon"); return false; }
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
            if (!CanBuy(item, express, out var reason)) { GameEvents.RaiseToast(reason); return false; }
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
                OrderedToast(item, cday, ctime);
                return true;
            }
            if (!GameManager.I.TrySpend(Price(item, express))) return false;
            ArrivalFor(FastDelivery(item, express), out int day, out float time);
            var d = new Delivery { itemId = item.id, express = express, arrivalDay = day, arrivalTime = time };
            S.deliveries.Add(d);
            GameEvents.RaiseDeliveryOrdered(item);
            // a guest's order (executing on the host): the guest already showed its own localized confirmation
            if (!(Net.IsHost && NetCommands.ScopeFrom > 0)) OrderedToast(item, day, time);
            GameManager.I.Save();
            return true;
        }

        static void OrderedToast(GearItem item, int day, float time)
        {
            string clock = GameManager.Clock(time);
            if (day > S.day)
                GameEvents.RaiseToast(Loc.Tr($"{item.Name} bestellt – Heli kommt morgen um {clock}.", $"{item.Name} ordered – chopper arrives tomorrow at {clock}."));
            else
                GameEvents.RaiseToast(Loc.Tr($"{item.Name} bestellt – Heli kommt um {clock}.", $"{item.Name} ordered – chopper arrives at {clock}."));
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

        /// Called when the LOCAL player picks up the crate. The opener switches to the new item right away
        /// (Gear.ClearChoice); other players keep their own equipment choice.
        /// SHARED STATE: on co-op clients this sends NetCommand.OpenCrate (itemId) to the host.
        public static void Receive(Delivery d)
        {
            if (d == null || GameManager.I == null) return;
            if (Net.IsClient) Net.Command(NetCommand.OpenCrate, d.itemId);   // applied optimistically below
            var item = GearDatabase.Get(d.itemId);
            if (item != null) Gear.ClearChoice(item.slot);
            Apply(d, item);
        }

        /// Host side of a client's NetCommand.OpenCrate (does not touch the host player's equipment choice).
        public static void ReceiveForRemote(Delivery d)
        {
            if (d == null || GameManager.I == null || !Net.HasAuthority) return;
            Apply(d, GearDatabase.Get(d.itemId));
        }

        static void Apply(Delivery d, GearItem item)
        {
            S.deliveries.Remove(d);
            if (item == null) return;
            if (!S.ownedItems.Contains(item.id)) S.ownedItems.Add(item.id);
            GameEvents.RaiseDeliveryReceived(item);
            GameEvents.RaiseGearChanged();
            GameEvents.RaiseBanner(Loc.Tr("Neu ausgerüstet: ", "New gear: ") + item.Name, new Color(1f, 0.55f, 0.1f));
            GameManager.I.Save();
        }
    }
}
