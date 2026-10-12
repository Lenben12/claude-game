using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// One running food buff (E). secondsLeft counts REAL seconds.
    public struct ActiveBuff
    {
        public BuffType type;
        public FoodKind kind;
        public float value;
        public float secondsLeft;
        public string foodId;
    }

    /// Food buffs of the LOCAL player (E): max 1 food + 1 drink buff at a time (a new one of the same kind replaces the
    /// old one). Never synced, lost on returning to the menu. GameManager ticks them (paused with the game offline).
    /// Readers: Player (air capacity, swim speed, shake), Filming (steady hands), Fauna/Player (bump resist),
    /// FX (night eyes); UI draws the 24 px icons left of the air ring.
    public static class Buffs
    {
        static readonly List<ActiveBuff> active = new List<ActiveBuff>(2);

        public static IReadOnlyList<ActiveBuff> Active => active;
        public static event Action Changed;

        /// Starts a food/drink buff. bonus adds to the food's value (grilled fish ≥ 3 kg: +0.05).
        public static void Apply(FoodDef f, float bonus = 0f)
        {
            if (f == null) return;
            // round 3: plain food (water, fruit snacks) has no buff – it only fills the survival meters
            if (!(f.value + bonus > 0f)) { GameEvents.RaiseFoodEaten(f); return; }
            for (int i = active.Count - 1; i >= 0; i--) if (active[i].kind == f.kind) active.RemoveAt(i);
            active.Add(new ActiveBuff
            {
                type = f.buff, kind = f.kind, value = Mathf.Max(0f, f.value + bonus),
                secondsLeft = Mathf.Max(1f, f.minutes * 60f), foodId = f.id
            });
            Raise();
            GameEvents.RaiseFoodEaten(f);
        }

        /// Current buff value of a type (0 = none).
        public static float Value(BuffType t)
        {
            float v = 0f;
            for (int i = 0; i < active.Count; i++) if (active[i].type == t && active[i].value > v) v = active[i].value;
            return v;
        }

        /// Multiplier for a buff type (1 = no buff):
        /// AirCapacity / SwimSpeed / NightEyes → 1 + value; SteadyHands → 1 − value (camera-shake factor);
        /// BumpResist → 1 − value (factor on the air lost to a predator bump).
        /// Round 3: AirCapacity and SwimSpeed also carry the survival penalty (hungry / thirsty: up to −20 %, never 0).
        public static float Multiplier(BuffType t)
        {
            float v = Value(t);
            switch (t)
            {
                case BuffType.SteadyHands:
                case BuffType.BumpResist: return Mathf.Clamp01(1f - v);
                case BuffType.AirCapacity: return (1f + v) * Survival.AirFactor;
                case BuffType.SwimSpeed: return (1f + v) * Survival.SpeedFactor;
                default: return 1f + v;
            }
        }

        /// Re-raises Changed (the player re-reads its air capacity / speed) – used by Survival when its penalty step changes.
        public static void RaiseChanged() => Raise();

        public static bool Has(BuffType t) => Value(t) > 0f;

        public static void Tick(float dt)
        {
            if (active.Count == 0 || dt <= 0f) return;
            bool changed = false;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var b = active[i];
                b.secondsLeft -= dt;
                if (b.secondsLeft <= 0f) { active.RemoveAt(i); changed = true; }
                else active[i] = b;
            }
            if (changed) Raise();
        }

        public static void Clear()
        {
            if (active.Count == 0) return;
            active.Clear();
            Raise();
        }

        static void Raise()
        {
            Changed?.Invoke();
            GameEvents.RaiseBuffsChanged();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active.Clear(); Changed = null; }
    }
}
