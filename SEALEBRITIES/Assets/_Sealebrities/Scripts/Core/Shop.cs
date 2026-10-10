namespace Sealebrities
{
    /// Chopper Shop: buying creates a Delivery; the helicopter (Boat module) drops it on the helipad; picking up the crate calls Receive.
    public static class Shop
    {
        public const float ExpressSurcharge = 0.5f;
        public const float ExpressDayFraction = 0.12f;   // ~3 real minutes with a 25-minute day

        static GameState S => GameManager.I.State;

        public static bool IsOwned(GearItem item) => S.ownedItems.Contains(item.id);
        public static bool IsOrdered(GearItem item) => S.deliveries.Exists(d => d.itemId == item.id);

        public static long Price(GearItem item, bool express) => express ? (long)(item.price * (1f + ExpressSurcharge)) : item.price;

        public static bool CanBuy(GearItem item, bool express, out string reason)
        {
            reason = null;
            if (item.comingSoon) { reason = Loc.Tr("Bald verfügbar", "Coming soon"); return false; }
            if (IsOwned(item)) { reason = Loc.Tr("Besitzt du schon", "Already owned"); return false; }
            if (IsOrdered(item)) { reason = Loc.Tr("Unterwegs", "On its way"); return false; }
            if (S.followers < item.followersNeeded) { reason = Loc.Tr($"Ab {Loc.Number(item.followersNeeded)} Followern", $"From {Loc.Number(item.followersNeeded)} followers"); return false; }
            if (S.money < Price(item, express)) { reason = Loc.Tr("Zu wenig Geld", "Not enough money"); return false; }
            return true;
        }

        public static bool Buy(GearItem item, bool express)
        {
            if (!CanBuy(item, express, out var reason)) { GameEvents.RaiseToast(reason); return false; }
            if (!GameManager.I.TrySpend(Price(item, express))) return false;
            var d = new Delivery { itemId = item.id, express = express };
            if (express)
            {
                float t = S.timeOfDay + ExpressDayFraction;
                d.arrivalDay = S.day + (t >= 1f ? 1 : 0);
                d.arrivalTime = t % 1f;
            }
            else
            {
                d.arrivalDay = S.day + 1;
                d.arrivalTime = 0.30f; // next morning ~07:12
            }
            S.deliveries.Add(d);
            GameEvents.RaiseDeliveryOrdered(item);
            GameEvents.RaiseToast(express
                ? Loc.Tr($"{item.Name} bestellt – Express-Heli ist unterwegs!", $"{item.Name} ordered – express chopper on its way!")
                : Loc.Tr($"{item.Name} bestellt – Lieferung morgen früh per Heli.", $"{item.Name} ordered – chopper delivery tomorrow morning."));
            GameManager.I.Save();
            return true;
        }

        /// True when the delivery's arrival time has passed.
        public static bool IsDue(Delivery d) => S.day > d.arrivalDay || (S.day == d.arrivalDay && S.timeOfDay >= d.arrivalTime);

        /// Called when the player picks up the crate.
        public static void Receive(Delivery d)
        {
            S.deliveries.Remove(d);
            var item = GearDatabase.Get(d.itemId);
            if (item == null) return;
            if (!S.ownedItems.Contains(item.id)) S.ownedItems.Add(item.id);
            GameEvents.RaiseDeliveryReceived(item);
            GameEvents.RaiseGearChanged();
            GameEvents.RaiseBanner(Loc.Tr("Neu ausgerüstet: ", "New gear: ") + item.Name, new UnityEngine.Color(1f, 0.55f, 0.1f));
            GameManager.I.Save();
        }
    }
}
