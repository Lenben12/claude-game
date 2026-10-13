using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// K.3 underwater finds and salvage (shared state, host authority).
    /// * Props place today's finds of a site with FindsDatabase.Daily(site, day, saveSeed) (= Finds.Today) – identical
    ///   on every co-op machine – and hide the ones with IsTaken.
    /// * Small find: E „Graben“ → Take(id) → straight into the „Funde“ inventory (Fish Market app tab).
    /// * Heavy salvage: Take(id) removes it from the seafloor; the player carries it (Carry.Pick) or the boat winch hooks
    ///   it; Deliver(id) at the boat / dock ladder (or when the winch pulls it aboard) puts it into the inventory.
    /// * Sell(id) / SellAll() turn inventory into money.
    /// Clients forward CoreCommands.FindKey ("take" / "deliver" / "sell" / "sellall") and apply optimistically; the host
    /// re-derives the value from the id (FindsDatabase.TryResolve), so a guest can never send a price.
    public static class Finds
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static event Action Changed;
        public static void RaiseChanged() => Changed?.Invoke();

        /// Today's finds of a site (or FindsDatabase.LagoonSite). Fills `into`.
        public static void Today(string siteId, List<FindSpawn> into)
        {
            var s = S;
            if (s == null) { into.Clear(); return; }
            FindsDatabase.Daily(siteId, s.day, s.saveSeed, into);
        }

        static FindState State(string id, bool create)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < s.finds.Count; i++) if (s.finds[i] != null && s.finds[i].id == id) return s.finds[i];
            if (!create) return null;
            var f = new FindState { id = id };
            s.finds.Add(f);
            return f;
        }

        public static bool IsTaken(string id) { var f = State(id, false); return f != null && f.taken; }
        public static bool IsDelivered(string id) { var f = State(id, false); return f != null && f.delivered; }

        public static IReadOnlyList<FoundItem> Inventory => S != null ? (IReadOnlyList<FoundItem>)S.foundItems : Array.Empty<FoundItem>();

        public static long InventoryValue
        {
            get
            {
                var s = S;
                if (s == null) return 0;
                long v = 0;
                for (int i = 0; i < s.foundItems.Count; i++) if (s.foundItems[i] != null) v += s.foundItems[i].value;
                return v;
            }
        }

        /// Digs up / picks up a find. Small finds go to the inventory at once; heavy salvage only leaves the seafloor
        /// (carry it or winch it, then Deliver). Returns false when it was already taken or the id is unknown.
        public static bool Take(string id)
        {
            var s = S;
            if (s == null || IsTaken(id)) return false;
            if (!FindsDatabase.TryResolve(id, s.saveSeed, out var spawn)) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.FindKey, "take", id);
            State(id, true).taken = true;
            if (!spawn.heavy) AddItem(spawn, s);
            GameEvents.RaiseFindTaken(id);
            if (!spawn.heavy && spawn.def != null)
                GameEvents.RaiseNotify(NotifyKind.Collectible, spawn.def.Name, "+" + Loc.Money(spawn.value));
            Changed?.Invoke();
            return true;
        }

        /// Heavy salvage arrived at the boat / dock (or the winch pulled it aboard).
        public static bool Deliver(string id)
        {
            var s = S;
            if (s == null || IsDelivered(id)) return false;
            if (!FindsDatabase.TryResolve(id, s.saveSeed, out var spawn) || !spawn.heavy) return false;
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.FindKey, "deliver", id);
            var f = State(id, true);
            f.taken = true;
            f.delivered = true;
            AddItem(spawn, s);
            if (spawn.def != null) GameEvents.RaiseNotify(NotifyKind.Collectible, spawn.def.Name, "+" + Loc.Money(spawn.value));
            Changed?.Invoke();
            return true;
        }

        static void AddItem(FindSpawn spawn, GameState s)
        {
            for (int i = 0; i < s.foundItems.Count; i++) if (s.foundItems[i] != null && s.foundItems[i].id == spawn.id) return;
            s.foundItems.Add(new FoundItem { id = spawn.id, kindId = spawn.def != null ? spawn.def.id : "", value = spawn.value, day = s.day });
        }

        /// Sells one find from the „Funde“ tab. Returns the money (0 when unknown).
        public static long Sell(string id)
        {
            var s = S;
            var gm = GameManager.I;
            if (s == null || gm == null) return 0;
            for (int i = 0; i < s.foundItems.Count; i++)
            {
                var it = s.foundItems[i];
                if (it == null || it.id != id) continue;
                s.foundItems.RemoveAt(i);
                if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.FindKey, "sell", id);
                else gm.AddMoney(it.value);
                if (Net.IsClient) { s.money += it.value; GameEvents.RaiseMoneyChanged(s.money); }   // optimistic
                GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("Fund verkauft", "Find sold"), "+" + Loc.Money(it.value));
                Changed?.Invoke();
                return it.value;
            }
            return 0;
        }

        public static long SellAll()
        {
            var s = S;
            var gm = GameManager.I;
            if (s == null || gm == null || s.foundItems.Count == 0) return 0;
            long sum = 0;
            for (int i = 0; i < s.foundItems.Count; i++) if (s.foundItems[i] != null) sum += s.foundItems[i].value;
            s.foundItems.Clear();
            if (Net.IsClient)
            {
                Net.Command(NetCommand.Custom, CoreCommands.FindKey, "sellall", "");
                s.money += sum;
                GameEvents.RaiseMoneyChanged(s.money);
            }
            else gm.AddMoney(sum);
            if (sum > 0) GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("Funde verkauft", "Finds sold"), "+" + Loc.Money(sum));
            Changed?.Invoke();
            return sum;
        }

        /// Host side of FindKey.
        public static bool Execute(string op, string id)
        {
            switch (op)
            {
                case "take": return Take(id);
                case "deliver": return Deliver(id);
                case "sell": return Sell(id) > 0;
                case "sellall": return SellAll() > 0;
            }
            return false;
        }

        /// New day: yesterday's find states are gone (ids carry the day, so nothing leaks).
        public static void OnNewDay(GameState s)
        {
            if (s == null) return;
            s.finds.Clear();
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; }
    }
}
