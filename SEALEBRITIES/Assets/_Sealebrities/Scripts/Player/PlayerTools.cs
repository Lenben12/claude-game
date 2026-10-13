using UnityEngine;

namespace Sealebrities
{
    /// Round 3: what Use (LMB) does with a held backpack item (camera, rod and lamp keep their own modules):
    ///  * Stick: a slapstick bonk on land (knockback + stun), weak under water; Spear: a thrust (made for the water);
    ///    Speargun (two-handed): a 7 m shot under water. Combat.Strike finds the first IHittable; durability, sharpness.
    ///  * Aim (RMB) with a weapon in hand = block (Combat.IsBlocking, damage × (1 − defense)).
    ///  * Food / drink: eat / drink one (hunger / thirst + buff, first-person eating animation).
    ///  * Empty bottle: fill it with seawater (in the water / at the shore); seawater: "filter it first"; map: open it.
    /// The main hand acts first; the offhand acts when the main hand is empty.
    [DefaultExecutionOrder(72)]
    public class PlayerTools : MonoBehaviour
    {
        PlayerController pc;
        PlayerHands hands;
        float cooldown;

        /// Last strike result (tests / debug): Time.time of the last swing and whether it hit.
        public static float LastSwingTime { get; private set; } = -100f;
        public static bool LastSwingHit { get; private set; }

        public void Init(PlayerController controller, PlayerHands viewHands)
        {
            pc = controller;
            hands = viewHands;
        }

        bool CanAct =>
            pc != null && pc.InGameplay && !UIState.BlocksGameplay && !pc.IsSeated && !pc.IsBusy && !pc.IsFainting
            && !SelfieState.Active && !Carry.IsCarrying && !pc.IsLyingDown;

        void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (!CanAct)
            {
                Combat.IsBlocking = false;
                return;
            }

            // which hand acts: the main hand, or the offhand when the main hand is empty
            Hand hand = Hand.Right;
            string id = Hands.ActiveId(Hand.Right);
            if (!Handles(id) && Hands.Active(Hand.Right) == HoldItem.None)
            {
                hand = Hand.Left;
                id = Hands.ActiveId(Hand.Left);
            }
            var d = ItemDatabase.Get(id);
            bool weapon = d != null && d.IsWeapon;

            // block with a held weapon (RMB) – the camera's aim uses RMB only while the camera is held
            bool block = weapon && GameInput.AimHeld && !Hands.IsHeld(HoldItem.Camera);
            Combat.IsBlocking = block;
            Combat.BlockDefense = block ? d.defense : 0f;

            if (!Handles(id) || !GameInput.UsePressed || block) return;
            // aiming the camera (RMB): LMB takes the photo – no strike / bite on the same click
            var hc = Refs.HandCamera;
            if (hc != null && !(hc is Object o && o == null) && hc.IsAiming) return;
            if (weapon) Strike(d, hand);
            else Use(d);
        }

        static bool Handles(string id)
        {
            var d = ItemDatabase.Get(id);
            return d != null && !d.isGear;
        }

        void Strike(ItemDef d, Hand hand)
        {
            if (cooldown > 0f) return;
            cooldown = Mathf.Max(0.2f, d.cooldown);
            bool under = pc.IsUnderwater;
            if (d.hold == HoldItem.SpearGun && !under)
            {
                GameEvents.RaiseNotify(NotifyKind.Info, d.Name, Loc.Tr("Nur unter Wasser", "Under water only"));
                return;
            }
            var st = Backpack.First(d.id);
            Camera view = pc.View;
            Transform vt = view != null ? view.transform : transform;
            bool thrust = d.hold != HoldItem.Stick || under;
            if (hands != null) hands.Swing(hand, thrust);
            FXSfx.Play(d.hold == HoldItem.SpearGun ? FXSound.Cast : FXSound.Equip, 0.5f);
            bool hit = Combat.Strike(vt.position, vt.forward, d, st, under, pc.transform, out HitInfo info);
            LastSwingTime = Time.time;
            LastSwingHit = hit;
            if (hit)
            {
                FXSfx.Play(FXSound.Click, 0.8f);
                pc.AddShake(0.12f);
            }
        }

        void Use(ItemDef d)
        {
            if (cooldown > 0f || d == null) return;
            cooldown = 0.6f;
            var r = Backpack.UseId(d.id);
            if (r == UseResult.Filled) FXSfx.Play(FXSound.Splash, 0.4f);
        }

        void OnDisable() { Combat.IsBlocking = false; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastSwingTime = -100f; LastSwingHit = false; }
    }
}
