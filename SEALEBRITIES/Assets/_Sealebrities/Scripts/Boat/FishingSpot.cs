using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Rod rack at the end of the dock (tutorial spot): a resting rod, a bucket and a tackle box. E puts the fishing
    /// rod into your hands (selects it on the hotbar, adding it to a free right-hand slot if needed). Fishing itself is
    /// done with the hotbar rod (FishingRodTool) anywhere at the water. The bucket shows up to five fish of the shared
    /// cooler box (FishMarket inventory) – nothing is sold here.
    public class FishingSpot : MonoBehaviour, IInteractable
    {
        const int MaxBucketFish = 5;

        Transform idleRod, bucketFishRoot;
        readonly List<GameObject> bucketFish = new List<GameObject>();
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "Angel nehmen", "Take the rod");
        int shownCount = -1;
        float poll;

        // ------------------------------------------------------------------ creation
        public static FishingSpot Create(Transform parent)
        {
            Vector3 spot = WorldInfo.FishingSpot;
            if (spot == Vector3.zero) spot = WorldLayout.FishingSpot;
            Vector3 dockEnd = WorldInfo.DockEndPosition;
            if (dockEnd == Vector3.zero) dockEnd = WorldLayout.DockEnd;

            Vector3 facing = spot - dockEnd;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.25f) facing = dockEnd - WorldLayout.DockStart;
            facing.y = 0f;
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.right;
            facing.Normalize();

            // find the dock planks under the spot (fall back toward the dock end, then to a fixed height)
            Vector3 pos = spot;
            bool found = false;
            for (int i = 0; i <= 4 && !found; i++)
            {
                Vector3 c = Vector3.Lerp(spot, dockEnd, i * 0.25f);
                float y = BoatSurface.Below(new Vector3(c.x, Mathf.Max(spot.y, 0f) + 6f, c.z), 12f, out bool water);
                if (!water)
                {
                    pos = new Vector3(c.x, y, c.z);
                    found = true;
                }
            }
            if (!found) pos.y = Mathf.Max(spot.y, 1.0f);

            var go = new GameObject("FishingSpot");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing, Vector3.up));
            var fs = go.AddComponent<FishingSpot>();
            fs.Init();
            return fs;
        }

        void Init()
        {
            FishingVisuals.BuildSpot(transform, out idleRod, out bucketFishRoot);
            var bc = gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.45f, -0.15f);
            bc.size = new Vector3(1.1f, 0.9f, 0.75f);
            GameEvents.GameStarted += OnChanged;
            FishMarket.Changed += OnChanged;
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnChanged;
            FishMarket.Changed -= OnChanged;
        }

        void OnChanged() => poll = 0f;

        // ------------------------------------------------------------------ IInteractable
        public string GetPrompt() => CanInteract() ? prompt.Text : null;

        public bool CanInteract()
        {
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || (p is Object o && o == null)) return false;
            if (p.IsInWater || p.IsUnderwater || p.IsSeated || p.IsFainting) return false;
            return !Hands.IsHeld(HoldItem.FishingRod) && Hands.Owns(HoldItem.FishingRod) && Hands.CanSelectNow;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            TakeRod();
        }

        /// Puts the rod into the player's hands via the hotbar (two-handed: the other hand is emptied by Hands).
        public static void TakeRod()
        {
            if (Hands.Find(HoldItem.FishingRod, out Hand hand, out int slot))
            {
                if (Hands.ActiveSlot(hand) != slot) Hands.Select(hand, slot);
                return;
            }
            int free = -1;
            for (int i = 0; i < Hands.SlotsPerHand; i++)
                if (Hands.SlotItem(Hand.Right, i) == HoldItem.None) { free = i; break; }
            if (free < 0) free = Hands.SlotsPerHand - 1;
            Hands.Assign(Hand.Right, free, HoldItem.FishingRod);
            Hands.Select(Hand.Right, free);
        }

        // ------------------------------------------------------------------ visuals
        void Update()
        {
            // the resting rod "is" the player's rod while it is in their hands
            bool rodOut = FishingRodTool.ViewModelVisible;
            if (idleRod != null && idleRod.gameObject.activeSelf == rodOut) idleRod.gameObject.SetActive(!rodOut);

            poll -= Time.deltaTime;
            if (poll > 0f) return;
            poll = 1f;
            int want = Mathf.Min(FishMarket.InventoryCount, MaxBucketFish);
            if (want != shownCount) RefreshBucket(want);
        }

        void RefreshBucket(int want)
        {
            shownCount = want;
            for (int i = 0; i < bucketFish.Count; i++) if (bucketFish[i] != null) Destroy(bucketFish[i]);
            bucketFish.Clear();
            var gm = GameManager.I;
            var inv = gm != null ? gm.State.fishInventory : null;
            for (int i = 0; i < want; i++)
            {
                FishKind kind = inv != null && i < inv.Count && inv[i] != null ? inv[i].Kind : null;
                if (kind != null && kind.junk) kind = FishMarket.Kind("mackerel");
                float len = kind != null ? Mathf.Clamp(kind.length * 0.6f, 0.16f, 0.32f) : 0.24f;
                var mats = kind != null ? FishingVisuals.KindMats(kind) : BoatMats.FishMats(i % 3);
                var f = FishingVisuals.BuildFish(bucketFishRoot, mats, len, kind != null ? kind.heightMul : 0.8f, kind != null ? kind.widthMul : 0.85f, false);
                float a = (i * 72f + 15f) * Mathf.Deg2Rad;
                f.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.07f, 0.3f + (i % 2) * 0.03f, Mathf.Sin(a) * 0.07f);
                f.transform.localRotation = Quaternion.Euler(-72f - (i % 3) * 5f, i * 72f, 0f);
                bucketFish.Add(f);
            }
        }
    }
}
