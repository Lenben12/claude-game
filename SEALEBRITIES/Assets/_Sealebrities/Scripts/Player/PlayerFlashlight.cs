using UnityEngine;

namespace Sealebrities
{
    /// Dive lamp: a warm spot light on the camera, toggled with F. Stats come from the equipped lamp.
    [DefaultExecutionOrder(55)]
    public class PlayerFlashlight : MonoBehaviour
    {
        /// Unity intensity units per GearItem.lampIntensity point.
        const float IntensityPerLevel = 6f;

        PlayerController pc;
        Light lamp;
        bool on, hasLamp;
        int tier;
        float baseIntensity = IntensityPerLevel, flicker;

        public bool On => on && hasLamp;

        public void Init(PlayerController controller, Light light)
        {
            pc = controller;
            lamp = light;
            Apply();
        }

        public void Refresh(GearStats g)
        {
            hasLamp = g.hasLamp;
            tier = hasLamp ? Mathf.Clamp(Mathf.RoundToInt(g.lampIntensity), 1, 3) : 0;
            baseIntensity = IntensityPerLevel * Mathf.Max(0.25f, g.lampIntensity);
            if (lamp != null) lamp.range = Mathf.Max(4f, g.lampRange * 1.25f);
            if (!hasLamp) on = false;
            Apply();
        }

        public void ForceOff()
        {
            on = false;
            flicker = 0f;
            Apply();
        }

        /// Switches the lamp without input (tools such as AutoShot). Ignored while no lamp is owned.
        public void SetOn(bool value)
        {
            if (value && !hasLamp) return;
            on = value;
            flicker = 0f;
            Apply();
        }

        void Update()
        {
            if (pc == null || lamp == null) return;
            if (!pc.InGameplay)
            {
                if (on) ForceOff();
                return;
            }
            if (!UIState.BlocksGameplay && !pc.IsFainting && GameInput.FlashlightPressed) Toggle();

            if (On && flicker > 0f)
            {
                // the duct-taped lamp needs a moment to make contact
                flicker -= Time.deltaTime;
                lamp.intensity = flicker > 0f && Random.value < 0.45f ? baseIntensity * 0.12f : baseIntensity;
            }
        }

        void Toggle()
        {
            if (!hasLamp)
            {
                GameEvents.RaiseToast(Loc.Tr("Du hast noch keine Lampe – bestell eine im Chopper Shop (Handy).",
                                             "You don't have a lamp yet – order one in the Chopper Shop (phone)."));
                return;
            }
            on = !on;
            flicker = on && tier == 1 ? 0.2f : 0f;
            Apply();
        }

        void Apply()
        {
            if (lamp == null) return;
            lamp.enabled = On;
            lamp.intensity = baseIntensity;
        }
    }
}
