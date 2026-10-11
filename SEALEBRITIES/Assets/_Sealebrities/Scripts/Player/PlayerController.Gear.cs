using UnityEngine;

namespace Sealebrities
{
    /// Comfort and gear rules of the first-person diver (round 2):
    ///  * field of view from Settings → Grafik (Q98, 60–100°); the filming zoom divides it (FilmHandCamera takes the
    ///    current FOV as its base), the menu camera and the launch intro keep their own FOV;
    ///  * head-bob / camera-shake toggles (Q99): "Kopfwippen" off = no walk bob, no under-water sway, no wave tilt
    ///    (camera and hands); "Kamerawackeln" off = no bump shake, no landing / water-entry dip, no rung pushes.
    ///    Footage steadiness is not affected (that is the filming module's own measure);
    ///  * food buffs (E.2): AirCapacity (tank seconds), SwimSpeed, SteadyHands (bump shake −50 %), BumpResist
    ///    (predator bumps cost less air) – Buffs is per player and never synced;
    ///  * Q20: jumping or climbing into the water takes the camera into its hand when that hand is empty (the rod is
    ///    stowed first) – unless the player emptied that hand within the last 3 s.
    public partial class PlayerController
    {
        /// Q20: a hand the player emptied this recently is left alone on entering the water.
        const float AutoEquipGrace = 3f;

        float handEmptiedRight = -100f, handEmptiedLeft = -100f;

        /// Gameplay vertical field of view (Settings.Data.fov, NaN-safe, 60–100°).
        public static float GameplayFov
        {
            get
            {
                var d = Settings.Data;
                float f = d != null ? Settings.Finite(d.fov, Settings.DefaultFov) : Settings.DefaultFov;
                return Mathf.Clamp(f, Settings.MinFov, Settings.MaxFov);
            }
        }

        static bool HeadBobOn => Settings.Data == null || Settings.Data.headBob;
        static bool CameraShakeOn => Settings.Data == null || Settings.Data.cameraShake;

        /// Bump-shake factor of the steady-hands buff (galley coffee 0.4 → 0.5 = "bump shake −50 %").
        static float SteadyShakeFactor => Mathf.Clamp01(1f - Buffs.Value(BuffType.SteadyHands) * 1.25f);

        /// Swim speed factor: SwimSpeed buff × the tower's ×0.7 while towing a friend (F.1).
        float SwimFactor
        {
            get
            {
                float f = Buffs.Multiplier(BuffType.SwimSpeed);
                if (Net.IsOnline && NetRescue.LocalTowTarget >= 0) f *= NetRescue.TowerSwimMultiplier;
                return f;
            }
        }

        /// Tank seconds with the AirCapacity buff.
        float BuffedAirSeconds => Mathf.Max(1f, gear.airSeconds * Buffs.Multiplier(BuffType.AirCapacity));

        /// Hotbar keys / mouse wheel (Hands.HandleInput) + remembers when the player emptied a hand (Q20).
        void HandleHotbarInput()
        {
            // a reel fight: an accidental mouse wheel / 1-6 would stow the two-handed rod and lose the fish
            if (FishingReel.Active) return;
            HoldItem r0 = Hands.Active(Hand.Right), l0 = Hands.Active(Hand.Left);
            Hands.HandleInput();
            if (r0 != HoldItem.None && Hands.Active(Hand.Right) == HoldItem.None) handEmptiedRight = Time.time;
            if (l0 != HoldItem.None && Hands.Active(Hand.Left) == HoldItem.None) handEmptiedLeft = Time.time;
        }

        /// Q20: on entering the water the camera becomes the active item of its hand when that hand is empty.
        void AutoEquipCameraOnEntry()
        {
            if (!Hands.CanSelectNow || !Hands.Owns(HoldItem.Camera)) return;
            if (!Hands.Find(HoldItem.Camera, out Hand h, out int slot)) return;   // not on the hotbar
            if (Hands.ActiveSlot(h) == slot || Hands.Active(h) != HoldItem.None) return;
            float emptied = h == Hand.Right ? handEmptiedRight : handEmptiedLeft;
            if (Time.time - emptied < AutoEquipGrace) return;
            Hands.Select(h, slot);
        }

        /// A tiny camera dip (eating bites, small bumps). Honours the camera-shake toggle.
        public void Nudge(float dip)
        {
            if (dip <= 0f || float.IsNaN(dip)) return;
            landDip = Mathf.Max(landDip, Mathf.Min(dip, 0.05f));
        }

        void OnBuffsChanged()
        {
            if (!initialized) return;
            OnGearChanged();
        }

        /// Adds camera shake (0..1 trauma). Honours the camera-shake toggle and the steady-hands buff.
        public void AddShake(float amount)
        {
            if (amount <= 0f || float.IsNaN(amount)) return;
            trauma = Mathf.Min(1f, trauma + amount);
        }

        /// G.3 mischief that the diver feels (fauna raises the event): nibbling fish wiggle the camera, Sir Flapsalot
        /// bumps you (no air loss), the octopus steals the lamp for 6 s (PlayerHands.StealLamp – idempotent).
        void OnMischief(string kind)
        {
            if (!initialized || menuMode || string.IsNullOrEmpty(kind)) return;
            OnSelfieMischief(kind);   // K.4: the selfie face laughs for 3 s
            switch (kind)
            {
                case "fish_nibble": AddShake(0.3f); break;
                case "sealion_bump": AddShake(0.4f); break;
                case "crab_pinch": AddShake(0.35f); Nudge(0.03f); break;          // „Au! Zwick!“
                case "crown_sting": AddShake(0.45f); break;                        // the crown-of-thorns pricks the glove
                case "gerald_photobomb": AddShake(0.1f); break;                    // Gerald bumps the lens
                case "octopus_lamp": if (Hands.IsHeld(HoldItem.Flashlight)) PlayerHands.StealLamp(6f); break;
            }
        }
    }
}
