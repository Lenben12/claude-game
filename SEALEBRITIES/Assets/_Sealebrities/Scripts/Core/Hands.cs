using System;
using UnityEngine;

namespace Sealebrities
{
    /// What a hand shows (model / animation). QuestItem (L.7, K.3) is never on the hotbar: it is what the hands hold while
    /// the local player carries a quest item / heavy salvage (Carry.Current). Round 3: Stick, Spear, SpearGun (weapons),
    /// Food (anything edible or a raw fish / bottle of water) and Tool (any other backpack item – bottle, stone, map …).
    /// APPEND new values at the end (sent as bytes in the avatar state; Hands.LastItem is the network clamp).
    public enum HoldItem { None, Camera, Flashlight, FishingRod, QuestItem, Stick, Spear, SpearGun, Food, Tool }

    /// Right = MAIN hand ("Erst-Hand"), Left = OFFHAND ("Zweit-Hand").
    public enum Hand { Right, Left }

    /// Round 3 hotbar of the LOCAL player (GDD §19 „ein großer Balken statt 3 + 3“): ONE row of 8 slots (keys 1–8 =
    /// GameAction.Slot(0..7), mouse wheel = main hand, Shift + wheel = offhand). Every slot references a backpack item id
    /// (or a gear pseudo item "Camera" / "Flashlight" / "FishingRod") and belongs to a hand: main (default) or offhand
    /// (chosen in the backpack: right-click a slot or drop an item onto the offhand). Pressing a slot's key puts that item
    /// into its hand (again = empty the hand). Two-handed items (rod, speargun) empty the other hand; selecting anything
    /// while one is held stows it. The same item can sit in several slots; a stack that runs out leaves its slot dimmed.
    /// State lives per player in GameState (hotbar / hotbarOffMask / activeMain / activeOff), never synced in co-op – the
    /// avatar bytes carry the held HoldItem only. Old 3 + 3 saves migrate in GameState.EnsureHotbar (right 1–3 → slots
    /// 1–3 main hand, left 1–3 → slots 4–6 offhand: the keys stay the same).
    public static class Hands
    {
        public const int SlotCount = 8;
        /// Legacy (3 + 3 hotbar). Only used by the save migration.
        public const int SlotsPerHand = 3;
        /// Highest HoldItem value (network clamp).
        public const HoldItem LastItem = HoldItem.Tool;

        /// Raised whenever a slot assignment, a slot's hand or a held item changes (also after a new game / load).
        public static event Action Changed;

        static float lastDeniedToast = -100f;

        // ------------------------------------------------------------------ item ids
        /// Hold item of an item id (gear pseudo items, backpack items; None for unknown / "None").
        public static HoldItem ItemOf(string id)
        {
            if (string.IsNullOrEmpty(id) || id == ItemIds.None) return HoldItem.None;
            var d = ItemDatabase.Get(id);
            return d != null ? d.hold : HoldItem.None;
        }

        /// Item id of a gear hold item (Camera / Flashlight / FishingRod), else null.
        public static string GearId(HoldItem i)
        {
            switch (i)
            {
                case HoldItem.Camera: return ItemIds.Camera;
                case HoldItem.Flashlight: return ItemIds.Flashlight;
                case HoldItem.FishingRod: return ItemIds.FishingRod;
                default: return null;
            }
        }

        /// Legacy serialized name of a gear item ("Camera", "Flashlight", "FishingRod", "None").
        public static string ItemName(HoldItem i) => GearId(i) ?? ItemIds.None;

        /// Legacy: hold item of a serialized gear name.
        public static HoldItem ParseItem(string s) => ItemOf(s);

        /// Localized item name (tooltips, hotbar label).
        public static string DisplayName(HoldItem i)
        {
            switch (i)
            {
                case HoldItem.Camera: return Loc.Tr("Kamera", "Camera");
                case HoldItem.Flashlight: return Loc.Tr("Lampe", "Lamp");
                case HoldItem.FishingRod: return Loc.Tr("Angel", "Fishing rod");
                case HoldItem.Stick: return Loc.Tr("Stock", "Stick");
                case HoldItem.Spear: return Loc.Tr("Speer", "Spear");
                case HoldItem.SpearGun: return Loc.Tr("Harpune", "Speargun");
                default: return "";
            }
        }

        public static string DisplayName(string id)
        {
            var d = ItemDatabase.Get(id);
            return d != null ? d.Name : "";
        }

        /// The gear slot that decides which model/stats a gear item uses (Camera → camera tier etc.).
        public static GearSlot GearSlotOf(HoldItem i) =>
            i == HoldItem.Flashlight ? GearSlot.Lamp : i == HoldItem.FishingRod ? GearSlot.Rod : GearSlot.Camera;

        /// Items that may sit in a hotbar slot (QuestItem never does – it comes from Carry).
        public static bool IsHotbarItem(HoldItem i) => i != HoldItem.None && i != HoldItem.QuestItem;

        public static bool IsTwoHanded(HoldItem i) => i == HoldItem.FishingRod || i == HoldItem.SpearGun;

        public static bool IsTwoHandedId(string id)
        {
            var d = ItemDatabase.Get(id);
            return d != null && (d.twoHanded || IsTwoHanded(d.hold));
        }

        // ------------------------------------------------------------------ state access
        static GameState S
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null) return null;
                var s = gm.State;
                if (s == null) return null;
                if (s.hotbar == null || s.hotbar.Length != SlotCount) s.EnsureHotbar();
                return s;
            }
        }

        static bool Seated
        {
            get
            {
                var p = Refs.Player;
                if (p == null) return false;
                if (p is UnityEngine.Object o && o == null) return false;
                // round 3: the passenger bench keeps the hands free (filming from the boat, Q36)
                return p.IsSeated && !SeatRules.LocalPassenger;
            }
        }

        static bool InWater
        {
            get
            {
                var p = Refs.Player;
                return p != null && !(p is UnityEngine.Object o && o == null) && p.IsInWater;
            }
        }

        // ------------------------------------------------------------------ queries
        /// Item id in a slot ("None" when empty or out of range).
        public static string SlotId(int slot)
        {
            var s = S;
            if (s == null || slot < 0 || slot >= SlotCount) return ItemIds.None;
            return string.IsNullOrEmpty(s.hotbar[slot]) ? ItemIds.None : s.hotbar[slot];
        }

        public static HoldItem SlotItem(int slot) => ItemOf(SlotId(slot));

        /// The hand a slot's item goes into (two-handed items always count as main hand).
        public static Hand SlotHand(int slot)
        {
            var s = S;
            if (s == null || slot < 0 || slot >= SlotCount) return Hand.Right;
            if (IsTwoHandedId(s.hotbar[slot])) return Hand.Right;
            return (s.hotbarOffMask & (1 << slot)) != 0 ? Hand.Left : Hand.Right;
        }

        /// The player has the slot's item right now (gear owned / stack not empty).
        public static bool SlotUsable(int slot) => OwnsId(SlotId(slot));

        /// Active slot of a hand, −1 = empty hand.
        public static int ActiveSlot(Hand h)
        {
            var s = S;
            if (s == null) return -1;
            int a = h == Hand.Right ? s.activeMain : s.activeOff;
            if (a < 0 || a >= SlotCount) return -1;
            return ItemOf(s.hotbar[a]) == HoldItem.None ? -1 : a;
        }

        /// Item currently held in a hand (None = empty hand).
        public static HoldItem Active(Hand h)
        {
            int a = ActiveSlot(h);
            return a < 0 ? HoldItem.None : SlotItem(a);
        }

        /// Item id currently held in a hand (null = empty hand).
        public static string ActiveId(Hand h)
        {
            int a = ActiveSlot(h);
            return a < 0 ? null : SlotId(a);
        }

        /// True when the item is held in either hand right now.
        public static bool IsHeld(HoldItem i) => i != HoldItem.None && (Active(Hand.Right) == i || Active(Hand.Left) == i);

        public static bool IsHeldId(string id) => !string.IsNullOrEmpty(id) && (ActiveId(Hand.Right) == id || ActiveId(Hand.Left) == id);

        /// Hand holding the item (false when it is not held).
        public static bool TryGetHeld(HoldItem i, out Hand hand)
        {
            hand = Hand.Right;
            if (i == HoldItem.None) return false;
            if (Active(Hand.Right) == i) return true;
            hand = Hand.Left;
            return Active(Hand.Left) == i;
        }

        /// First slot that contains the item (false when it is not on the hotbar).
        public static bool Find(HoldItem i, out int slot)
        {
            slot = -1;
            if (i == HoldItem.None || S == null) return false;
            string gear = GearId(i);
            for (int k = 0; k < SlotCount; k++)
            {
                if (gear != null ? SlotId(k) == gear : SlotItem(k) == i) { slot = k; return true; }
            }
            return false;
        }

        /// Legacy overload: slot + the hand that slot belongs to.
        public static bool Find(HoldItem i, out Hand hand, out int slot)
        {
            bool ok = Find(i, out slot);
            hand = ok ? SlotHand(slot) : Hand.Right;
            return ok;
        }

        public static bool FindId(string id, out int slot)
        {
            slot = -1;
            if (string.IsNullOrEmpty(id) || S == null) return false;
            for (int k = 0; k < SlotCount; k++) if (SlotId(k) == id) { slot = k; return true; }
            return false;
        }

        /// The local player owns at least one item of the matching gear slot (camera / lamp / rod).
        public static bool Owns(HoldItem i)
        {
            string gear = GearId(i);
            if (gear != null) return OwnsId(gear);
            return false;
        }

        /// Gear pseudo item: a matching gear item is owned. Backpack item: at least one in the backpack.
        public static bool OwnsId(string id)
        {
            var d = ItemDatabase.Get(id);
            if (d == null) return false;
            if (!d.isGear) return Backpack.Count(id) > 0;
            var gm = GameManager.I;
            if (gm == null || gm.State == null) return false;
            var owned = gm.State.ownedItems;
            for (int k = 0; k < owned.Count; k++)
            {
                var g = GearDatabase.Get(owned[k]);
                if (g != null && g.slot == d.gearSlot) return true;
            }
            return false;
        }

        /// Hotbar selection is ignored while a modal UI is open or the player is seated (boat wheel).
        public static bool CanSelectNow => !UIState.BlocksGameplay && !Seated;

        /// The MAIN hand holds a backpack item that uses LMB itself (weapon, food, bottle …): an offhand camera must not
        /// start recording on the same click (PlayerTools acts instead).
        public static bool MainHandTakesUse
        {
            get
            {
                var d = ItemDatabase.Get(ActiveId(Hand.Right));
                return d != null && !d.isGear;
            }
        }

        // ------------------------------------------------------------------ mutations (local player only)
        static void SetActive(GameState s, Hand h, int v)
        {
            if (h == Hand.Right) s.activeMain = v;
            else s.activeOff = v;
        }

        static int RawActive(GameState s, Hand h) => h == Hand.Right ? s.activeMain : s.activeOff;

        static void Denied(string de, string en)
        {
            if (Time.unscaledTime - lastDeniedToast < 2f) return;
            lastDeniedToast = Time.unscaledTime;
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr(de, en));
        }

        /// Toggles a slot: its item goes into the slot's hand (selecting the held slot again empties that hand; an empty
        /// slot empties the main hand). A two-handed item empties the other hand; anything else stows a held two-hander.
        public static void Select(int slot)
        {
            var s = S;
            if (s == null || slot < 0 || slot >= SlotCount || !CanSelectNow) return;
            string id = SlotId(slot);
            HoldItem item = ItemOf(id);
            Hand h = SlotHand(slot);
            bool changed = false;
            if (item == HoldItem.None)
            {
                if (ActiveSlot(Hand.Right) >= 0) { SetActive(s, Hand.Right, -1); changed = true; }
                if (StowTwoHanded(s)) changed = true;
            }
            else if (ActiveSlot(h) == slot)
            {
                SetActive(s, h, -1);
                changed = true;
            }
            else
            {
                if (!OwnsId(id))
                {
                    var d = ItemDatabase.Get(id);
                    if (d != null && d.isGear) Denied("Noch nicht gekauft", "Not owned yet");
                    else Denied("Nicht im Rucksack", "Not in the backpack");
                    return;
                }
                if (item == HoldItem.FishingRod && InWater)
                {
                    // the two-handed rod does not go swimming (it is stowed on entering the water as well)
                    Denied("Nicht im Wasser", "Not in the water");
                    return;
                }
                StowTwoHanded(s);
                // the same slot may not be "held" by both hands
                Hand other = h == Hand.Right ? Hand.Left : Hand.Right;
                if (RawActive(s, other) == slot) SetActive(s, other, -1);
                SetActive(s, h, slot);
                if (IsTwoHandedId(id)) SetActive(s, other, -1);
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        public static void Stow(Hand h)
        {
            var s = S;
            if (s == null || RawActive(s, h) < 0) return;
            SetActive(s, h, -1);
            Changed?.Invoke();
        }

        /// Empties both hands (boat wheel, ladder, fainting, cutscenes). Always allowed.
        public static void StowAll()
        {
            var s = S;
            if (s == null || (s.activeMain < 0 && s.activeOff < 0)) return;
            s.activeMain = -1;
            s.activeOff = -1;
            Changed?.Invoke();
        }

        /// Puts an item id into a slot (backpack drag & drop; null / "None" clears it). hand = null keeps the slot's
        /// current hand. A held slot whose item changes empties that hand.
        public static void Assign(int slot, string id, Hand? hand = null)
        {
            var s = S;
            if (s == null || slot < 0 || slot >= SlotCount) return;
            if (string.IsNullOrEmpty(id) || ItemOf(id) == HoldItem.None) id = ItemIds.None;
            bool changed = s.hotbar[slot] != id;
            if (changed)
            {
                if (s.activeMain == slot) s.activeMain = -1;
                if (s.activeOff == slot) s.activeOff = -1;
                s.hotbar[slot] = id;
            }
            if (hand.HasValue) changed |= SetSlotHandRaw(s, slot, hand.Value);
            if (changed) { Normalize(s); Changed?.Invoke(); }
        }

        /// Swaps two hotbar slots (contents + hand), keeping held items in hand.
        public static void Swap(int a, int b)
        {
            var s = S;
            if (s == null || a < 0 || b < 0 || a >= SlotCount || b >= SlotCount || a == b) return;
            string t = s.hotbar[a];
            s.hotbar[a] = s.hotbar[b];
            s.hotbar[b] = t;
            // the hand travels with the item, held items stay in their hand
            int bitA = 1 << a, bitB = 1 << b;
            bool offA = (s.hotbarOffMask & bitA) != 0, offB = (s.hotbarOffMask & bitB) != 0;
            int m = s.hotbarOffMask & ~bitA & ~bitB;
            if (offB) m |= bitA;
            if (offA) m |= bitB;
            s.hotbarOffMask = m;
            s.activeMain = s.activeMain == a ? b : s.activeMain == b ? a : s.activeMain;
            s.activeOff = s.activeOff == a ? b : s.activeOff == b ? a : s.activeOff;
            Normalize(s);
            Changed?.Invoke();
        }

        /// Sets which hand a slot's item goes into (backpack: right-click a slot / drop onto the offhand).
        public static void SetSlotHand(int slot, Hand hand)
        {
            var s = S;
            if (s == null || slot < 0 || slot >= SlotCount) return;
            if (!SetSlotHandRaw(s, slot, hand)) return;
            Normalize(s);
            Changed?.Invoke();
        }

        static bool SetSlotHandRaw(GameState s, int slot, Hand hand)
        {
            int bit = 1 << slot;
            int mask = hand == Hand.Left ? s.hotbarOffMask | bit : s.hotbarOffMask & ~bit;
            if (mask == s.hotbarOffMask) return false;
            s.hotbarOffMask = mask;
            // a held slot changes hands with it
            if (hand == Hand.Left && s.activeMain == slot) { s.activeMain = -1; s.activeOff = slot; }
            else if (hand == Hand.Right && s.activeOff == slot) { s.activeOff = -1; s.activeMain = slot; }
            return true;
        }

        /// Backpack „in die Hand“: holds an item in a hand right away. Uses a slot that already has the item (and moves
        /// it to that hand), else the first free slot (else the last slot). False when the item cannot be held.
        public static bool Hold(string id, Hand hand)
        {
            var s = S;
            if (s == null || ItemOf(id) == HoldItem.None || !OwnsId(id)) return false;
            if (IsTwoHandedId(id)) hand = Hand.Right;
            if (!FindId(id, out int slot))
            {
                slot = -1;
                for (int k = 0; k < SlotCount; k++) if (SlotId(k) == ItemIds.None) { slot = k; break; }
                if (slot < 0) slot = SlotCount - 1;
                Assign(slot, id, hand);
            }
            else SetSlotHand(slot, hand);
            if (ActiveSlot(SlotHand(slot)) != slot)
            {
                bool can = CanSelectNow;
                if (!can && UIState.InventoryOpen && !Seated)
                {
                    // the backpack is open (it blocks gameplay): apply directly
                    StowTwoHanded(s);
                    Hand other = hand == Hand.Right ? Hand.Left : Hand.Right;
                    if (RawActive(s, other) == slot) SetActive(s, other, -1);
                    SetActive(s, hand, slot);
                    if (IsTwoHandedId(id)) SetActive(s, other, -1);
                    Normalize(s);
                    Changed?.Invoke();
                }
                else if (can) Select(slot);
            }
            return ActiveSlot(hand) == slot;
        }

        /// Restores the default hotbar (camera, rod, stick on 1–3 in the main hand; lamp on 4 in the offhand) with both
        /// hands empty.
        public static void ResetToDefaults()
        {
            var s = S;
            if (s == null) return;
            s.hotbar = GameState.DefaultHotbar();
            s.hotbarOffMask = GameState.DefaultOffMask;
            s.activeMain = -1;
            s.activeOff = -1;
            Changed?.Invoke();
        }

        /// Reads the hotbar keys (1–8 by default) and the mouse wheel (wheel = main hand, Shift + wheel = offhand,
        /// cycling through that hand's slots incl. the empty hand). Call once per frame from the local player.
        /// The wheel is ignored while the camera is aimed (it zooms), in the selfie view and while carrying a quest item.
        public static void HandleInput()
        {
            if (!CanSelectNow || GameManager.I == null || !GameManager.I.InGame) return;
            if (SelfieState.Active || Carry.IsCarrying) return;
            for (int i = 0; i < SlotCount; i++)
                if (GameInput.SlotPressed(i)) Select(i);
            int scroll = GameInput.HotbarScroll;
            if (scroll != 0)
            {
                var cam = Refs.HandCamera;
                bool aiming = cam != null && !(cam is UnityEngine.Object o && o == null) && cam.IsAiming;
                if (!aiming) Cycle(GameInput.HotbarScrollLeftHand ? Hand.Left : Hand.Right, scroll);
            }
        }

        /// Moves a hand's selection one step (dir +1 = next slot, −1 = previous) through empty hand → that hand's slots,
        /// skipping empty slots, items not owned / used up and the rod while swimming.
        public static void Cycle(Hand h, int dir)
        {
            var s = S;
            if (s == null || dir == 0 || !CanSelectNow) return;
            dir = dir > 0 ? 1 : -1;
            int cur = ActiveSlot(h);
            const int positions = SlotCount + 1;   // -1 (empty) .. SlotCount-1
            for (int step = 1; step < positions; step++)
            {
                int idx = (((cur + 1 + dir * step) % positions) + positions) % positions - 1;
                if (idx < 0)
                {
                    if (cur >= 0) { SetActive(s, h, -1); Changed?.Invoke(); }
                    return;
                }
                string id = SlotId(idx);
                var item = ItemOf(id);
                if (item == HoldItem.None || SlotHand(idx) != h || !OwnsId(id)) continue;
                if (item == HoldItem.FishingRod && InWater) continue;
                Select(idx);
                return;
            }
        }

        /// A held stack ran out (eaten, broken, dropped): that hand becomes empty. Called after backpack changes.
        public static void ValidateHeld()
        {
            var gm = GameManager.I;
            if (gm == null || gm.State == null || gm.State.hotbar == null) return;
            var s = gm.State;
            bool changed = false;
            for (int k = 0; k < 2; k++)
            {
                var h = (Hand)k;
                int a = RawActive(s, h);
                if (a < 0 || a >= SlotCount) continue;
                if (!OwnsId(s.hotbar[a])) { SetActive(s, h, -1); changed = true; }
            }
            if (changed) Changed?.Invoke();
        }

        /// Called by GameManager after the GameState object was replaced (new game, load, co-op snapshot).
        public static void NotifyStateReplaced()
        {
            var s = S;
            if (s != null) Normalize(s);
            Changed?.Invoke();
        }

        /// Raises Changed (gear bought / backpack refilled: the hotbar redraws its dimmed slots).
        public static void RaiseChanged() => Changed?.Invoke();

        static bool StowTwoHanded(GameState s)
        {
            for (int k = 0; k < 2; k++)
            {
                var h = (Hand)k;
                int a = ActiveSlot(h);
                if (a >= 0 && IsTwoHandedId(s.hotbar[a]))
                {
                    SetActive(s, h, -1);
                    return true;
                }
            }
            return false;
        }

        static void Normalize(GameState s)
        {
            if (s.activeMain >= SlotCount || s.activeMain < -1) s.activeMain = -1;
            if (s.activeOff >= SlotCount || s.activeOff < -1) s.activeOff = -1;
            if (s.activeMain >= 0 && (ItemOf(s.hotbar[s.activeMain]) == HoldItem.None || SlotHandRaw(s, s.activeMain) != Hand.Right)) s.activeMain = -1;
            if (s.activeOff >= 0 && (ItemOf(s.hotbar[s.activeOff]) == HoldItem.None || SlotHandRaw(s, s.activeOff) != Hand.Left)) s.activeOff = -1;
            if (s.activeMain >= 0 && s.activeMain == s.activeOff) s.activeOff = -1;
            if (s.activeMain >= 0 && IsTwoHandedId(s.hotbar[s.activeMain])) s.activeOff = -1;
        }

        static Hand SlotHandRaw(GameState s, int slot)
        {
            if (IsTwoHandedId(s.hotbar[slot])) return Hand.Right;
            return (s.hotbarOffMask & (1 << slot)) != 0 ? Hand.Left : Hand.Right;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            lastDeniedToast = -100f;
        }
    }
}
