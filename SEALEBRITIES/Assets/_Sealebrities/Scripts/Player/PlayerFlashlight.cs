using UnityEngine;

namespace Sealebrities
{
    /// Dive lamp: a warm spot light that only shines while the Flashlight hotbar item is in a hand (Hands API) and
    /// switched on. Taking the lamp into a hand switches it on, F toggles it (F with the lamp not in hand takes it from
    /// the hotbar). The light sits just in front of the lamp lens of the view model and aims at the screen centre ~9 m
    /// ahead, so the hand lamp and the camera model are not blown out by their own light. Stats come from the lamp gear.
    [DefaultExecutionOrder(65)]
    public class PlayerFlashlight : MonoBehaviour
    {
        /// Unity intensity units per GearItem.lampIntensity point.
        const float IntensityPerLevel = 3.5f;
        static readonly Vector3 DefaultLeft = new Vector3(-0.13f, -0.085f, 0.72f);
        static readonly Color Warm = LowPoly.Hex("#FFD9A0");

        PlayerController pc;
        PlayerHands hands;
        Light lamp;
        bool on, hasLamp, wasHeld;
        int tier;
        float baseIntensity = IntensityPerLevel, flicker;
        bool lit;

        /// Switched on, owned, held in a hand and not put away for the phone.
        public bool On => lit;

        public void Init(PlayerController controller, Light light)
        {
            pc = controller;
            lamp = light;
            if (lamp != null)
            {
                lamp.color = Warm;
                lamp.spotAngle = 55f;
                lamp.innerSpotAngle = 32f;
            }
            Apply();
        }

        public void SetHands(PlayerHands viewHands) { hands = viewHands; }

        public void Refresh(GearStats g)
        {
            hasLamp = g.hasLamp;
            tier = hasLamp ? Mathf.Clamp(Mathf.RoundToInt(g.lampIntensity), 1, 3) : 0;
            baseIntensity = IntensityPerLevel * Mathf.Max(0.25f, g.lampIntensity);
            if (lamp != null) lamp.range = Mathf.Max(8f, g.lampRange * 1.15f);
            if (!hasLamp) on = false;
            Apply();
        }

        public void ForceOff()
        {
            on = false;
            flicker = 0f;
            Apply();
        }

        /// Switches the lamp without input (tools such as AutoShot). Turning it on also takes the lamp into a hand.
        public void SetOn(bool value)
        {
            if (value && !hasLamp) return;
            if (value && !Hands.IsHeld(HoldItem.Flashlight) && Hands.Find(HoldItem.Flashlight, out Hand h, out int slot))
                Hands.Select(h, slot);
            on = value;
            wasHeld = Hands.IsHeld(HoldItem.Flashlight);
            flicker = 0f;
            Apply();
        }

        void Update()
        {
            if (pc == null || lamp == null) return;
            if (!pc.InGameplay)
            {
                if (on) ForceOff();
                wasHeld = false;
                return;
            }

            bool held = Hands.IsHeld(HoldItem.Flashlight);
            if (held != wasHeld)
            {
                // taking the lamp into a hand switches it on, putting it away switches it off
                wasHeld = held;
                on = held && hasLamp;
                flicker = on && tier == 1 ? 0.2f : 0f;
            }
            if (!UIState.BlocksGameplay && !pc.IsFainting && !pc.IsSeated && GameInput.FlashlightPressed) Toggle(held);
            Apply();

            if (lit && flicker > 0f)
            {
                // the duct-taped lamp needs a moment to make contact
                flicker -= Time.deltaTime;
                lamp.intensity = flicker > 0f && Random.value < 0.45f ? baseIntensity * 0.12f : baseIntensity;
            }
        }

        void LateUpdate()
        {
            if (lamp == null || !lamp.enabled) return;
            // follow the lamp in the hand (the view model animates in its LateUpdate, order 60)
            Vector3 lp;
            if (hands != null && hands.TryGetLampLens(out Vector3 lens, out Hand hand))
                lp = lens + new Vector3(0f, 0.01f, 0.12f);
            else
            {
                lp = DefaultLeft;
                if (Hands.TryGetHeld(HoldItem.Flashlight, out Hand h) && h == Hand.Right) lp.x = -lp.x;
            }
            Transform t = lamp.transform;
            t.localPosition = lp;
            t.localRotation = Quaternion.LookRotation((new Vector3(0f, 0f, 9f) - lp).normalized, Vector3.up);
        }

        void Toggle(bool held)
        {
            if (!hasLamp)
            {
                GameEvents.RaiseToast(Loc.Tr("Noch keine Lampe", "No lamp yet"));
                return;
            }
            if (!held)
            {
                // F with the lamp on the hotbar but not in hand: take it (selection switches it on)
                if (Hands.Find(HoldItem.Flashlight, out Hand h, out int slot))
                {
                    Hands.Select(h, slot);
                    if (Hands.IsHeld(HoldItem.Flashlight))
                    {
                        wasHeld = true;
                        on = true;
                        flicker = tier == 1 ? 0.2f : 0f;
                    }
                }
                else GameEvents.RaiseToast(Loc.Tr("Lampe auf die Hotbar legen (", "Put the lamp on the hotbar (") + GameInput.BindingLabel(GameAction.Inventory) + ")");   // one-off toast
                return;
            }
            on = !on;
            flicker = on && tier == 1 ? 0.2f : 0f;
        }

        void Apply()
        {
            if (lamp == null) return;
            bool phoneUp = hands != null && hands.PhoneUp;
            lit = on && hasLamp && pc != null && pc.InGameplay && Hands.IsHeld(HoldItem.Flashlight) && !phoneUp && !pc.IsSeated;
            if (lamp.enabled != lit) lamp.enabled = lit;
            if (lit && flicker <= 0f) lamp.intensity = baseIntensity;
        }
    }
}
