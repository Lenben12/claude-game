using System;
using UnityEngine;

namespace Sealebrities
{
    /// Items that can sit in a hotbar slot and be held in a hand. QuestItem (L.7, K.3) is never on the hotbar: it is
    /// what the hands hold while the local player carries a quest item / heavy salvage (Carry.Current – the model is
    /// chosen by the carried item id). APPEND new values at the end (sent as bytes in the avatar state).
    public enum HoldItem { None, Camera, Flashlight, FishingRod, QuestItem }

    public enum Hand { Right, Left }

    /// How-to-Fish style hotbar of the LOCAL player: right hand and left hand (offhand) with 3 slots each and one
    /// active slot per hand (-1 = empty hand). An item lives in at most one slot. The fishing rod is two-handed:
    /// selecting it empties the other hand, and selecting anything while the rod is out stows the rod.
    /// Keys: GameAction.RightSlot1..3 (1/2/3) and LeftSlot1..3 (4/5/6) – call HandleInput once per frame.
    /// State lives per player in GameState (hotbarRight/hotbarLeft/activeRight/activeLeft, never synced in co-op).
    public static class Hands
    {
        public const int SlotsPerHand = 3;

        /// Raised whenever a slot assignment or an active slot changes (also after a new game / load).
        public static event Action Changed;

        static readonly HoldItem[] right = new HoldItem[SlotsPerHand];
        static readonly HoldItem[] left = new HoldItem[SlotsPerHand];
        static GameState cachedState;
        static string[] cachedRight, cachedLeft;
        static float lastDeniedToast = -100f;

        const string NameNone = "None", NameCamera = "Camera", NameFlashlight = "Flashlight", NameRod = "FishingRod";

        /// Serialized name of an item (GameState.hotbarRight/Left entries).
        public static string ItemName(HoldItem i)
        {
            switch (i)
            {
                case HoldItem.Camera: return NameCamera;
                case HoldItem.Flashlight: return NameFlashlight;
                case HoldItem.FishingRod: return NameRod;
                default: return NameNone;
            }
        }

        public static HoldItem ParseItem(string s)
        {
            switch (s)
            {
                case NameCamera: return HoldItem.Camera;
                case NameFlashlight: return HoldItem.Flashlight;
                case NameRod: return HoldItem.FishingRod;
                default: return HoldItem.None;
            }
        }

        /// Localized item name for tooltips.
        public static string DisplayName(HoldItem i)
        {
            switch (i)
            {
                case HoldItem.Camera: return Loc.Tr("Kamera", "Camera");
                case HoldItem.Flashlight: return Loc.Tr("Lampe", "Lamp");
                case HoldItem.FishingRod: return Loc.Tr("Angel", "Fishing rod");
                default: return "";
            }
        }

        /// The gear slot that decides which model/stats an item uses (Camera → camera tier etc.).
        public static GearSlot GearSlotOf(HoldItem i) =>
            i == HoldItem.Flashlight ? GearSlot.Lamp : i == HoldItem.FishingRod ? GearSlot.Rod : GearSlot.Camera;

        /// Items that may sit in a hotbar slot (QuestItem never does – it comes from Carry).
        public static bool IsHotbarItem(HoldItem i) => i == HoldItem.Camera || i == HoldItem.Flashlight || i == HoldItem.FishingRod;

        // ------------------------------------------------------------------ state access
        static GameState Sync()
        {
            var gm = GameManager.I;
            if (gm == null) return null;
            var s = gm.State;
            if (s == null) return null;
            if (!ReferenceEquals(s, cachedState) || !ReferenceEquals(s.hotbarRight, cachedRight) || !ReferenceEquals(s.hotbarLeft, cachedLeft))
            {
                s.EnsureHotbar();
                for (int i = 0; i < SlotsPerHand; i++)
                {
                    right[i] = ParseItem(s.hotbarRight[i]);
                    left[i] = ParseItem(s.hotbarLeft[i]);
                }
                cachedState = s;
                cachedRight = s.hotbarRight;
                cachedLeft = s.hotbarLeft;
            }
            return s;
        }

        static HoldItem[] Arr(Hand h) => h == Hand.Right ? right : left;
        static Hand Other(Hand h) => h == Hand.Right ? Hand.Left : Hand.Right;
        static int RawActive(GameState s, Hand h) => h == Hand.Right ? s.activeRight : s.activeLeft;

        static void SetRawActive(GameState s, Hand h, int v)
        {
            if (h == Hand.Right) s.activeRight = v;
            else s.activeLeft = v;
        }

        static void WriteSlot(GameState s, Hand h, int slot, HoldItem item)
        {
            Arr(h)[slot] = item;
            (h == Hand.Right ? s.hotbarRight : s.hotbarLeft)[slot] = ItemName(item);
        }

        static int IndexOf(Hand h, HoldItem item)
        {
            if (item == HoldItem.None) return -1;
            var a = Arr(h);
            for (int i = 0; i < SlotsPerHand; i++) if (a[i] == item) return i;
            return -1;
        }

        static bool Seated
        {
            get
            {
                var p = Refs.Player;
                if (p == null) return false;
                if (p is UnityEngine.Object o && o == null) return false;
                return p.IsSeated;
            }
        }

        // ------------------------------------------------------------------ queries
        /// Active slot of a hand, -1 = empty hand.
        public static int ActiveSlot(Hand h)
        {
            var s = Sync();
            if (s == null) return -1;
            int a = RawActive(s, h);
            if (a < 0 || a >= SlotsPerHand) return -1;
            return Arr(h)[a] == HoldItem.None ? -1 : a;
        }

        /// Item currently held in a hand (None = empty hand).
        public static HoldItem Active(Hand h)
        {
            int a = ActiveSlot(h);
            return a < 0 ? HoldItem.None : Arr(h)[a];
        }

        public static HoldItem SlotItem(Hand h, int slot)
        {
            if (slot < 0 || slot >= SlotsPerHand || Sync() == null) return HoldItem.None;
            return Arr(h)[slot];
        }

        public static bool IsTwoHanded(HoldItem i) => i == HoldItem.FishingRod;

        /// True when the item is held in either hand right now.
        public static bool IsHeld(HoldItem i) => i != HoldItem.None && (Active(Hand.Right) == i || Active(Hand.Left) == i);

        /// Hand holding the item (false when it is not held).
        public static bool TryGetHeld(HoldItem i, out Hand hand)
        {
            hand = Hand.Right;
            if (i == HoldItem.None) return false;
            if (Active(Hand.Right) == i) return true;
            hand = Hand.Left;
            return Active(Hand.Left) == i;
        }

        /// Slot that contains the item (false when it is not on the hotbar).
        public static bool Find(HoldItem i, out Hand hand, out int slot)
        {
            hand = Hand.Right;
            slot = -1;
            if (Sync() == null || i == HoldItem.None) return false;
            slot = IndexOf(Hand.Right, i);
            if (slot >= 0) return true;
            hand = Hand.Left;
            slot = IndexOf(Hand.Left, i);
            return slot >= 0;
        }

        /// The local player owns at least one item of the matching gear slot (camera / lamp / rod).
        public static bool Owns(HoldItem i)
        {
            if (!IsHotbarItem(i)) return false;
            var gm = GameManager.I;
            if (gm == null || gm.State == null) return false;
            var owned = gm.State.ownedItems;
            var slot = GearSlotOf(i);
            for (int k = 0; k < owned.Count; k++)
            {
                var g = GearDatabase.Get(owned[k]);
                if (g != null && g.slot == slot) return true;
            }
            return false;
        }

        /// Hotbar selection is ignored while a modal UI is open or the player is seated (boat wheel).
        public static bool CanSelectNow => !UIState.BlocksGameplay && !Seated;

        // ------------------------------------------------------------------ mutations (local player only)
        /// Toggles a slot: selecting the active slot (or an empty slot) empties that hand. A two-handed item empties
        /// the other hand; selecting anything while the rod is active stows the rod.
        public static void Select(Hand h, int slot)
        {
            var s = Sync();
            if (s == null || slot < 0 || slot >= SlotsPerHand || !CanSelectNow) return;
            HoldItem item = Arr(h)[slot];
            int cur = ActiveSlot(h);
            bool changed = false;
            if (cur == slot)
            {
                SetRawActive(s, h, -1);
                changed = true;
            }
            else if (item == HoldItem.None)
            {
                if (cur >= 0) { SetRawActive(s, h, -1); changed = true; }
                if (StowTwoHanded(s)) changed = true;
            }
            else
            {
                if (!Owns(item))
                {
                    if (Time.unscaledTime - lastDeniedToast > 2f)
                    {
                        lastDeniedToast = Time.unscaledTime;
                        GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Noch nicht gekauft", "Not owned yet"));
                    }
                    return;
                }
                if (IsTwoHanded(item))
                {
                    // the two-handed rod does not go swimming (it is stowed on entering the water as well)
                    var p = Refs.Player;
                    if (p != null && !(p is UnityEngine.Object o && o == null) && p.IsInWater)
                    {
                        if (Time.unscaledTime - lastDeniedToast > 2f)
                        {
                            lastDeniedToast = Time.unscaledTime;
                            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Nicht im Wasser", "Not in the water"));
                        }
                        return;
                    }
                }
                StowTwoHanded(s);
                SetRawActive(s, h, slot);
                if (IsTwoHanded(item)) SetRawActive(s, Other(h), -1);
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        public static void Stow(Hand h)
        {
            var s = Sync();
            if (s == null || RawActive(s, h) < 0) return;
            SetRawActive(s, h, -1);
            Changed?.Invoke();
        }

        /// Empties both hands (boat wheel, ladder, fainting, cutscenes). Always allowed.
        public static void StowAll()
        {
            var s = Sync();
            if (s == null || (s.activeRight < 0 && s.activeLeft < 0)) return;
            s.activeRight = -1;
            s.activeLeft = -1;
            Changed?.Invoke();
        }

        /// Puts an item into a slot (inventory drag & drop; None clears the slot). The item leaves any other slot;
        /// when it came from another slot, the displaced item swaps into that slot. Held items stay in hand where
        /// they moved within the same hand, otherwise that hand is emptied.
        public static void Assign(Hand h, int slot, HoldItem item)
        {
            var s = Sync();
            if (s == null || slot < 0 || slot >= SlotsPerHand) return;
            if (item != HoldItem.None && !IsHotbarItem(item)) return;
            HoldItem prevR = Active(Hand.Right), prevL = Active(Hand.Left);
            HoldItem displaced = Arr(h)[slot];
            if (displaced == item) return;

            bool fromSlot = false;
            Hand srcHand = Hand.Right;
            int srcSlot = -1;
            if (item != HoldItem.None)
            {
                fromSlot = Find(item, out srcHand, out srcSlot);
                if (fromSlot) WriteSlot(s, srcHand, srcSlot, HoldItem.None);
            }
            WriteSlot(s, h, slot, item);
            if (fromSlot && displaced != HoldItem.None) WriteSlot(s, srcHand, srcSlot, displaced);

            SetRawActive(s, Hand.Right, IndexOf(Hand.Right, prevR));
            SetRawActive(s, Hand.Left, IndexOf(Hand.Left, prevL));
            Normalize(s);
            Changed?.Invoke();
        }

        /// Restores the default hotbar (right: camera, rod; left: lamp) with both hands empty.
        public static void ResetToDefaults()
        {
            var s = Sync();
            if (s == null) return;
            s.hotbarRight = new[] { NameCamera, NameRod, NameNone };
            s.hotbarLeft = new[] { NameFlashlight, NameNone, NameNone };
            s.activeRight = -1;
            s.activeLeft = -1;
            Sync();
            Changed?.Invoke();
        }

        /// Reads the hotbar keys (1–6 by default) and the mouse wheel (Q17: wheel = right hand, Shift+wheel = left hand,
        /// cycling through the slots incl. the empty hand). Call once per frame from the local player.
        /// The wheel is ignored while the camera is aimed (it zooms), in the selfie view (arm length / hotbar keys pick
        /// expressions) and while carrying a quest item.
        public static void HandleInput()
        {
            if (!CanSelectNow || GameManager.I == null || !GameManager.I.InGame) return;
            if (SelfieState.Active || Carry.IsCarrying) return;
            for (int i = 0; i < SlotsPerHand; i++)
            {
                if (GameInput.SlotPressed(Hand.Right, i)) Select(Hand.Right, i);
                if (GameInput.SlotPressed(Hand.Left, i)) Select(Hand.Left, i);
            }
            int scroll = GameInput.HotbarScroll;
            if (scroll != 0)
            {
                var cam = Refs.HandCamera;
                bool aiming = cam != null && !(cam is UnityEngine.Object o && o == null) && cam.IsAiming;
                if (!aiming) Cycle(GameInput.HotbarScrollLeftHand ? Hand.Left : Hand.Right, scroll);
            }
        }

        /// Moves a hand's selection one step (dir +1 = next slot, −1 = previous) through empty hand → slot 1 → 2 → 3,
        /// skipping empty slots, items not owned yet and the rod while swimming.
        public static void Cycle(Hand h, int dir)
        {
            var s = Sync();
            if (s == null || dir == 0 || !CanSelectNow) return;
            dir = dir > 0 ? 1 : -1;
            int cur = ActiveSlot(h);
            const int positions = SlotsPerHand + 1;   // -1 (empty) .. SlotsPerHand-1
            for (int step = 1; step < positions; step++)
            {
                int idx = (((cur + 1 + dir * step) % positions) + positions) % positions - 1;
                if (idx < 0)
                {
                    if (cur >= 0) { SetRawActive(s, h, -1); Changed?.Invoke(); }
                    return;
                }
                var item = Arr(h)[idx];
                if (item == HoldItem.None || !Owns(item)) continue;
                if (IsTwoHanded(item))
                {
                    var p = Refs.Player;
                    if (p != null && !(p is UnityEngine.Object po && po == null) && p.IsInWater) continue;
                }
                Select(h, idx);
                return;
            }
        }

        /// Called by GameManager after the GameState object was replaced (new game, load, co-op snapshot).
        public static void NotifyStateReplaced()
        {
            cachedState = null;
            Sync();
            Changed?.Invoke();
        }

        static bool StowTwoHanded(GameState s)
        {
            for (int k = 0; k < 2; k++)
            {
                var h = (Hand)k;
                int a = ActiveSlot(h);
                if (a >= 0 && IsTwoHanded(Arr(h)[a]))
                {
                    SetRawActive(s, h, -1);
                    return true;
                }
            }
            return false;
        }

        static void Normalize(GameState s)
        {
            for (int k = 0; k < 2; k++)
            {
                var h = (Hand)k;
                int a = RawActive(s, h);
                if (a >= SlotsPerHand || (a >= 0 && (Arr(h)[a] == HoldItem.None || !Owns(Arr(h)[a])))) SetRawActive(s, h, -1);
            }
            if (IsTwoHanded(Active(Hand.Right))) SetRawActive(s, Hand.Left, -1);
            else if (IsTwoHanded(Active(Hand.Left))) SetRawActive(s, Hand.Right, -1);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            cachedState = null;
            cachedRight = null;
            cachedLeft = null;
            lastDeniedToast = -100f;
            Array.Clear(right, 0, right.Length);
            Array.Clear(left, 0, left.Length);
        }
    }
}
