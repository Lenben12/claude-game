using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// One backpack slot (round 3). Stackable items share a stack; weapons / tools with durability are one per stack.
    [Serializable]
    public class InvStack
    {
        public string id;
        public int count;
        /// Uses left (−1 = no durability).
        public float durability = -1f;
        /// 0..1 (−1 = cannot be sharpened).
        public float sharpness = -1f;

        public ItemDef Def => ItemDatabase.Get(id);
    }

    /// Seawater bottles in the hut's water filter (per player). readyAt = absolute in-game time (day + timeOfDay).
    [Serializable] public class FilterJob { public int count; public float readyAt; }

    /// A campfire at a fixed spot that somebody lit (shared). until = absolute in-game time (day + timeOfDay).
    [Serializable] public class CampfireState { public string id; public float until; }

    /// Result of using a backpack item (UI feedback / tests).
    public enum UseResult { Nothing, Eaten, Drunk, Filled, NotEdible, NeedsCooking, NeedsFilter, NotHere, Held, Opened, Full }

    /// The LOCAL player's backpack (GDD §19 „Inventar als Rucksack“): everything picked up really goes in here –
    /// collectibles (the finder's own copy), map pieces, food, coconuts, raw fish, stones, driftwood, spears, bottles,
    /// crate items. Per player (GameState.backpack, never synced; co-op guests keep it in the host's PlayerRecord and in
    /// coop_player.json). Crew gear (tanks, fins, cameras, lamps, rods) stays in GameState.ownedItems and is shown by the
    /// UI next to the stacks. 48 slots (GDD §20 Q27 „Rucksack unbegrenzt“: practically never full – the screen shows
    /// 4 rows and scrolls); stack sizes from ItemDatabase.
    public static class Backpack
    {
        public const int Capacity = 48;

        public static event Action Changed;

        /// Props hook: spawn a dropped item in front of the player (id, count, durability, sharpness). Without it a drop
        /// simply discards the item.
        public static Action<InvStack, Vector3> DropHandler;

        static GameState S => GameManager.I != null ? GameManager.I.State : null;
        static float lastFullToast = -100f;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static List<InvStack> Stacks
        {
            get
            {
                var s = S;
                if (s == null) return Empty;
                if (s.backpack == null) s.backpack = new List<InvStack>();
                return s.backpack;
            }
        }

        static readonly List<InvStack> Empty = new List<InvStack>();

        /// The collectible pouch (collectibles + map pieces): no slot limit, shown in the backpack's „Sammlung“ tab.
        public static List<InvStack> Pouch
        {
            get
            {
                var s = S;
                if (s == null) return Empty;
                if (s.pouch == null) s.pouch = new List<InvStack>();
                return s.pouch;
            }
        }

        /// Collectibles and map pieces live in the pouch, everything else in the 24 backpack slots.
        /// Round 3: treasure valuables (ItemCategory.Valuable) ride in the pouch too – chest loot never fails on a full bag.
        public static bool IsPouchItem(ItemDef d) => d != null && (d.category == ItemCategory.Collectible || d.category == ItemCategory.MapPiece || d.category == ItemCategory.Valuable);

        static List<InvStack> ListFor(ItemDef d) => IsPouchItem(d) ? Pouch : Stacks;

        public static void RaiseChanged()
        {
            Changed?.Invoke();
            Hands.ValidateHeld();
        }

        // ------------------------------------------------------------------ queries
        public static int Count(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            var l = ListFor(ItemDatabase.Get(id));
            int n = 0;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].id == id) n += Mathf.Max(0, l[i].count);
            return n;
        }

        public static bool Has(string id, int n = 1) => Count(id) >= n;

        /// First stack of an item (null = none). Weapons: the stack with the most uses left.
        public static InvStack First(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var l = ListFor(ItemDatabase.Get(id));
            InvStack best = null;
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i];
                if (st == null || st.id != id || st.count <= 0) continue;
                if (best == null || st.durability > best.durability) best = st;
            }
            return best;
        }

        public static int IndexOf(InvStack st)
        {
            var l = Stacks;
            for (int i = 0; i < l.Count; i++) if (ReferenceEquals(l[i], st)) return i;
            return -1;
        }

        public static int UsedSlots
        {
            get
            {
                var l = Stacks;
                int n = 0;
                for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].count > 0) n++;
                return n;
            }
        }

        public static int FreeSlots => Mathf.Max(0, Capacity - UsedSlots);

        /// How many of an item fit in right now (existing stacks + free slots).
        public static int SpaceFor(string id)
        {
            var d = ItemDatabase.Get(id);
            if (d == null) return 0;
            if (IsPouchItem(d)) return 9999;
            int max = Mathf.Max(1, d.maxStack);
            int room = FreeSlots * max;
            if (max > 1)
            {
                var l = Stacks;
                for (int i = 0; i < l.Count; i++)
                    if (l[i] != null && l[i].id == id) room += Mathf.Max(0, max - l[i].count);
            }
            return room;
        }

        // ------------------------------------------------------------------ mutations (local player only)
        /// Adds items (stacks first, then free slots). Returns how many went in; the rest is refused with a
        /// „Rucksack voll“ toast (notify = false: no pickup toast, e.g. starter items / migration).
        public static int Add(string id, int n = 1, bool notify = true)
        {
            var d = ItemDatabase.Get(id);
            if (d == null || n <= 0 || S == null || d.isGear) return 0;
            bool pouch = IsPouchItem(d);
            var l = ListFor(d);
            int max = Mathf.Max(1, d.maxStack);
            int left = n;
            if (max > 1)
            {
                for (int i = 0; i < l.Count && left > 0; i++)
                {
                    var st = l[i];
                    if (st == null || st.id != id || st.count >= max) continue;
                    int add = Mathf.Min(left, max - st.count);
                    st.count += add;
                    left -= add;
                }
            }
            if (pouch && left > 0)
            {
                // the pouch has no slot limit: one stack per item (capped at maxStack)
                l.Add(new InvStack { id = id, count = Mathf.Min(left, max) });
                left = 0;
            }
            while (left > 0 && UsedSlots < Capacity)
            {
                int add = Mathf.Min(left, max);
                var st = new InvStack
                {
                    id = id, count = add,
                    durability = d.HasDurability ? d.durability : -1f,
                    sharpness = d.Sharpenable ? d.sharpness : -1f,
                };
                // reuse an emptied slot so the grid order stays stable
                int empty = -1;
                for (int i = 0; i < l.Count; i++) if (l[i] == null || l[i].count <= 0) { empty = i; break; }
                if (empty >= 0) l[empty] = st; else l.Add(st);
                left -= add;
            }
            int added = n - left;
            if (added > 0)
            {
                if (notify) GameEvents.RaiseNotify(NotifyKind.Collectible, d.Name, "+" + added.ToString(Inv));
                RaiseChanged();
            }
            if (left > 0 && notify) ToastFull();
            return added;
        }

        /// Adds a stack as it is (durability / sharpness kept – picking up a dropped spear). Returns false when full.
        public static bool AddStack(InvStack src, bool notify = true)
        {
            if (src == null || src.count <= 0) return false;
            var d = ItemDatabase.Get(src.id);
            if (d == null) return false;
            if (d.maxStack > 1) return Add(src.id, src.count, notify) == src.count;
            if (UsedSlots >= Capacity) { if (notify) ToastFull(); return false; }
            var st = new InvStack { id = src.id, count = 1, durability = src.durability, sharpness = src.sharpness };
            var l = Stacks;
            int empty = -1;
            for (int i = 0; i < l.Count; i++) if (l[i] == null || l[i].count <= 0) { empty = i; break; }
            if (empty >= 0) l[empty] = st; else l.Add(st);
            if (notify) GameEvents.RaiseNotify(NotifyKind.Collectible, d.Name, "+1");
            RaiseChanged();
            return true;
        }

        static void ToastFull()
        {
            if (Time.unscaledTime - lastFullToast < 2f) return;
            lastFullToast = Time.unscaledTime;
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Rucksack voll", "Backpack full"), Capacity + "/" + Capacity);
        }

        /// Removes n of an item (from the smallest stacks first). False (nothing removed) when there are not enough.
        public static bool Remove(string id, int n = 1)
        {
            if (n <= 0) return true;
            if (Count(id) < n) return false;
            var l = ListFor(ItemDatabase.Get(id));
            int left = n;
            for (int pass = 0; pass < 2 && left > 0; pass++)
            {
                for (int i = l.Count - 1; i >= 0 && left > 0; i--)
                {
                    var st = l[i];
                    if (st == null || st.id != id || st.count <= 0) continue;
                    int take = Mathf.Min(left, st.count);
                    st.count -= take;
                    left -= take;
                }
            }
            Compact();
            RaiseChanged();
            return true;
        }

        /// Removes n items of one slot.
        public static bool RemoveAt(int index, int n = 1)
        {
            var l = Stacks;
            if (index < 0 || index >= l.Count || l[index] == null || l[index].count < n || n <= 0) return false;
            l[index].count -= n;
            Compact();
            RaiseChanged();
            return true;
        }

        /// Swaps two grid slots (drag & drop in the backpack). Indices beyond the list grow it with empty slots.
        public static void Move(int from, int to)
        {
            var l = Stacks;
            if (from < 0 || to < 0 || from >= Capacity || to >= Capacity || from == to) return;
            while (l.Count <= Mathf.Max(from, to)) l.Add(null);
            var a = l[from];
            l[from] = l[to];
            l[to] = a;
            TrimTail();
            RaiseChanged();
        }

        /// Empty stacks become null placeholders (grid positions stay); trailing nulls are trimmed.
        static void Compact()
        {
            var l = Stacks;
            for (int i = 0; i < l.Count; i++) if (l[i] != null && l[i].count <= 0) l[i] = null;
            TrimTail();
            var p = Pouch;
            for (int i = p.Count - 1; i >= 0; i--) if (p[i] == null || p[i].count <= 0) p.RemoveAt(i);
        }

        static void TrimTail()
        {
            var l = Stacks;
            while (l.Count > 0 && (l[l.Count - 1] == null || l[l.Count - 1].count <= 0)) l.RemoveAt(l.Count - 1);
        }

        /// Throws a slot's items (all = whole stack, else one) on the ground in front of the player.
        public static bool Drop(int index, bool all)
        {
            var l = Stacks;
            if (index < 0 || index >= l.Count || l[index] == null || l[index].count <= 0) return false;
            var st = l[index];
            int n = all ? st.count : 1;
            var dropped = new InvStack { id = st.id, count = n, durability = st.durability, sharpness = st.sharpness };
            st.count -= n;
            Compact();
            RaiseChanged();
            var p = Refs.Player;
            if (DropHandler != null && p != null && !(p is UnityEngine.Object o && o == null) && p.Root != null)
            {
                Vector3 fwd = p.View != null ? p.View.transform.forward : p.Root.forward;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                try { DropHandler(dropped, p.Root.position + fwd * 1.1f + Vector3.up * 0.9f); }
                catch (Exception e) { Debug.LogException(e); }
            }
            return true;
        }

        // ------------------------------------------------------------------ use
        /// Uses one item of a slot: eat / drink, fill a bottle in the water, open the treasure map … (see UseResult).
        public static UseResult Use(int index)
        {
            var l = Stacks;
            if (index < 0 || index >= l.Count || l[index] == null || l[index].count <= 0) return UseResult.Nothing;
            return UseId(l[index].id);
        }

        /// Uses one item by id (the held item on LMB, the backpack's „Benutzen“).
        public static UseResult UseId(string id)
        {
            var d = ItemDatabase.Get(id);
            if (d == null || Count(id) <= 0) return UseResult.Nothing;
            switch (d.category)
            {
                case ItemCategory.Food:
                case ItemCategory.Drink:
                {
                    if (!Survival.Consume(d)) return UseResult.Nothing;
                    Remove(id, 1);
                    if (!string.IsNullOrEmpty(d.usedTo)) Add(d.usedTo, 1, false);
                    return d.category == ItemCategory.Drink ? UseResult.Drunk : UseResult.Eaten;
                }
                case ItemCategory.Raw:
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Erst braten", "Cook it first"), Loc.Tr("Lagerfeuer · Grill", "Campfire · grill"));
                    return UseResult.NeedsCooking;
            }
            switch (id)
            {
                case ItemIds.BottleEmpty:
                    return Survival.FillBottle() ? UseResult.Filled : UseResult.NotHere;
                case ItemIds.BottleSea:
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Zu salzig!", "Too salty!"), Loc.Tr("Wasserfilter in der Hütte", "Water filter in the hut"));
                    return UseResult.NeedsFilter;
                case ItemIds.TreasureMap:
                case ItemIds.MapPiece:
                    MapPieces.OpenMap();
                    return UseResult.Opened;
            }
            return UseResult.Nothing;
        }

        /// Starter items of a new player (new game, first co-op join, old saves): the stick and an empty bottle.
        public static void GiveStarterItems(GameState s)
        {
            if (s == null) return;
            if (s.backpack == null) s.backpack = new List<InvStack>();
            if (!HasIn(s, ItemIds.Stick)) s.backpack.Add(new InvStack { id = ItemIds.Stick, count = 1 });
            if (!HasIn(s, ItemIds.BottleEmpty) && !HasIn(s, ItemIds.BottleFresh) && !HasIn(s, ItemIds.BottleSea))
                s.backpack.Add(new InvStack { id = ItemIds.BottleEmpty, count = 1 });
        }

        static bool HasIn(GameState s, string id)
        {
            for (int i = 0; i < s.backpack.Count; i++) if (s.backpack[i] != null && s.backpack[i].id == id && s.backpack[i].count > 0) return true;
            return false;
        }

        /// Validates a loaded / received backpack (unknown ids, NaN, over-full stacks).
        public static void Normalize(List<InvStack> l)
        {
            if (l == null) return;
            for (int i = l.Count - 1; i >= 0; i--)
            {
                var st = l[i];
                if (st == null) continue;
                var d = ItemDatabase.Get(st.id);
                if (d == null || d.isGear || st.count <= 0) { l[i] = null; continue; }
                st.count = Mathf.Clamp(st.count, 1, Mathf.Max(1, d.maxStack));
                if (float.IsNaN(st.durability) || float.IsInfinity(st.durability)) st.durability = d.HasDurability ? d.durability : -1f;
                if (float.IsNaN(st.sharpness) || float.IsInfinity(st.sharpness)) st.sharpness = d.Sharpenable ? d.sharpness : -1f;
                if (d.HasDurability) st.durability = Mathf.Clamp(st.durability, 0f, d.durability);
                if (d.Sharpenable) st.sharpness = Mathf.Clamp01(st.sharpness);
            }
            while (l.Count > Capacity) l.RemoveAt(l.Count - 1);
            while (l.Count > 0 && l[l.Count - 1] == null) l.RemoveAt(l.Count - 1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Changed = null; DropHandler = null; lastFullToast = -100f; }
    }

    /// Round 3 world pickups (stones, driftwood, fruit, raw fish at the fish rack …): SHARED like the palms – one
    /// player takes it, it is gone for everybody until the next in-game day (GameState.dailyUsed "loot:&lt;id&gt;",
    /// host authority through CoreCommands.LootKey). The item itself goes into the taker's backpack.
    public static class Loot
    {
        static GameState S => GameManager.I != null && GameManager.I.InGame ? GameManager.I.State : null;

        public static string Key(string lootId) => "loot:" + lootId;

        public static bool Available(string lootId)
        {
            var s = S;
            return s != null && !string.IsNullOrEmpty(lootId) && !s.dailyUsed.Contains(Key(lootId));
        }

        /// The local player takes a world pickup: shared "taken today" + the item into the own backpack. False when it
        /// is gone already or the backpack is full.
        public static bool TryTake(string lootId, string itemId, int count = 1)
        {
            var s = S;
            if (s == null || !Available(lootId) || ItemDatabase.Get(itemId) == null) return false;
            if (Backpack.SpaceFor(itemId) < count)
            {
                Backpack.Add(itemId, count);   // shows „Rucksack voll“
                return false;
            }
            if (Net.IsClient) Net.Command(NetCommand.Custom, CoreCommands.LootKey, lootId);
            s.dailyUsed.Add(Key(lootId));
            Backpack.Add(itemId, count);
            return true;
        }

        /// Host side of CoreCommands.LootKey.
        public static bool HostTake(string lootId)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(lootId) || lootId.Length > 64 || s.dailyUsed.Contains(Key(lootId))) return false;
            s.dailyUsed.Add(Key(lootId));
            return true;
        }
    }
}
