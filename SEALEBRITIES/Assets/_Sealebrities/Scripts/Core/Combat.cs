using System;
using UnityEngine;

namespace Sealebrities
{
    /// One hit by the local player's weapon (stick, spear, speargun) – slapstick, never lethal.
    public struct HitInfo
    {
        public Vector3 point;
        /// Unit direction from the attacker to the target (knockback direction).
        public Vector3 direction;
        /// "Scare" damage 0..100 (the target decides what it means: flee, stun, back off).
        public float damage;
        /// Knockback impulse in m/s and stun seconds.
        public float knockback, stun;
        public string weaponId;
        public bool underwater;
        /// Net id of the attacker (0 offline / host).
        public int attackerId;
    }

    /// Anything the player's weapons can hit: hermits (npc module), sharks and monsters (fauna module), test dummies.
    /// Put it on the object or a parent of its collider. OnHit runs on the ATTACKER's machine – a co-op guest hitting a
    /// host-owned creature proxy must forward the hit itself (suggested: NetCommand.Custom with Combat.HitKey,
    /// registered through CoreCommands.RegisterCustom by the owning module).
    public interface IHittable
    {
        bool CanBeHit { get; }
        void OnHit(in HitInfo hit);
    }

    /// Round 3 weapons + fights (GDD §19 „Waffen gegen Haie und Monster“, „Stock … Slapstick, nicht tödlich“).
    ///  * The LOCAL player's health ("Puste", 0..100, regenerates 6/s after 3 s without a hit) – a "scare" model:
    ///    sharks / monsters / angry hermits call HurtLocal; at 0 the diver is knocked back hard (under water it also
    ///    costs air through GameEvents.PlayerBumped) and gets 40 back. Never death.
    ///  * Blocking: a held weapon + the Aim button (RMB) reduces incoming damage by its defense.
    ///  * Strike: the player's tools call it on LMB – sphere cast along the view, the first IHittable is hit;
    ///    durability −1 per hit (a spear breaks at 0), damage × (0.6 + 0.8 × sharpness).
    ///  * Sharpen at the grinder (needs StoryKeys.GrindLearned from the hermits + 1 whetstone): +0.25 sharpness.
    ///  * Livestream hook: every hit / hurt raises GameEvents.Fight (viewers love fights – the stream service turns it
    ///    into a hype spike and a laughing chat).
    public static class Combat
    {
        public const float MaxHealth = 100f;
        public const float RegenDelay = 3f, RegenPerSecond = 6f;
        public const float KnockedOutRefill = 40f;
        /// Sharpening: whetstones per use and the sharpness gained.
        public const int SharpenStones = 1;
        public const float SharpenStep = 0.25f;
        /// NetCommand.Custom key suggested for modules that forward a guest's hit on a host-owned target.
        public const string HitKey = "Hit";

        /// Round 3 (fauna step): animals have no colliders (kinematic steering) – the fauna module answers this query
        /// instead: the nearest hittable animal along the strike (origin, unit dir, reach, sweep radius). Returns the
        /// target, the distance along the ray and the hit point. Compared with the collider hits; the closer one wins.
        public delegate bool SoftTargetQuery(Vector3 origin, Vector3 dir, float reach, float radius, Transform ignoreRoot,
                                             out IHittable target, out float distance, out Vector3 point);
        public static SoftTargetQuery SoftTargets;
        /// Set by an IHittable inside OnHit when the hit was REFUSED (a protected animal – Q18): no durability loss and no
        /// fight hype for it (the target raises its own reaction, e.g. haters). Reset by Strike.
        public static bool HitRefused;

        static float health = MaxHealth;
        static float lastHurt = -100f;

        /// Local player's health ("Puste") 0..100.
        public static float Health => health;
        public static float Health01 => Mathf.Clamp01(health / MaxHealth);
        /// Time.time of the last hit taken (HUD flash).
        public static float LastHurtTime => lastHurt;
        /// Set every frame by the player's tools: a weapon is held and the block button is down.
        public static bool IsBlocking;
        /// Defense of the held weapon while blocking (0..1).
        public static float BlockDefense;

        /// The local player hit something (hit, target).
        public static event Action<HitInfo, IHittable> HitLanded;
        /// The local player got hurt (damage after blocking, source position, source id).
        public static event Action<float, Vector3, string> LocalHurt;
        /// The local player's health hit 0 (slapstick knock-back).
        public static event Action LocalKnockedOut;

        static readonly RaycastHit[] Hits = new RaycastHit[16];

        /// GameManager.Update.
        public static void Tick(float dt)
        {
            if (health >= MaxHealth || dt <= 0f) return;
            if (Time.time - lastHurt < RegenDelay) return;
            health = Mathf.Min(MaxHealth, health + RegenPerSecond * dt);
        }

        public static void Reset()
        {
            health = MaxHealth;
            lastHurt = -100f;
            IsBlocking = false;
            BlockDefense = 0f;
        }

        // ------------------------------------------------------------------ taking damage
        /// A shark, monster or hermit hurts the LOCAL player (fauna / npc modules call this on the victim's machine).
        /// amount 0..100, from = attacker position, source = species / npc id (for the stream chat). Returns the damage
        /// taken after blocking.
        public static float HurtLocal(float amount, Vector3 from, string source)
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || !(amount > 0f)) return 0f;
            var p = Refs.Player;
            if (p == null || (p is UnityEngine.Object o && o == null) || p.Root == null || p.IsFainting) return 0f;
            float taken = amount * (IsBlocking ? Mathf.Clamp01(1f - BlockDefense) : 1f);
            if (Buffs.Has(BuffType.BumpResist)) taken *= Buffs.Multiplier(BuffType.BumpResist);
            health = Mathf.Max(0f, health - taken);
            lastHurt = Time.time;
            Vector3 away = p.Root.position - from;
            away.y = p.IsInWater ? away.y * 0.5f : 0.35f;
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -p.Root.forward;
            var pc = p as PlayerController;
            bool ko = health <= 0f;
            if (pc != null) pc.Knockback(away * (ko ? 6f : 2.5f + taken * 0.05f), ko ? 0.8f : 0.35f + taken * 0.004f);
            LocalHurt?.Invoke(taken, from, source);
            GameEvents.RaiseFight(ko ? "knockout" : IsBlocking ? "block" : "hurt", Mathf.Clamp01(taken / 30f));
            if (ko)
            {
                health = KnockedOutRefill;
                // under water a knock-out costs air (the existing bump path: air loss, shake, livestream spike)
                if (p.IsInWater) GameEvents.RaisePlayerBumped(Mathf.Max(3f, p.AirSecondsLeft * 0.3f));
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Autsch!", "Ouch!"), Loc.Tr("Abstand halten", "Keep your distance"));
                LocalKnockedOut?.Invoke();
            }
            return taken;
        }

        // ------------------------------------------------------------------ dealing damage
        /// Effective damage of a weapon stack (sharpness, medium).
        public static float Damage(ItemDef w, InvStack st, bool underwater)
        {
            if (w == null) return 0f;
            float sharp = w.Sharpenable ? (st != null && st.sharpness >= 0f ? st.sharpness : w.sharpness) : 0.5f;
            float dmg = w.damage * (0.6f + 0.8f * Mathf.Clamp01(sharp));
            bool home = underwater ? w.underwater : w.onLand;
            if (!home || (underwater && w.hold == HoldItem.Stick)) dmg *= w.offMediumFactor;
            return dmg;
        }

        /// The local player's swing / thrust / shot with a weapon stack: a sphere cast from origin along dir up to the
        /// weapon's reach. The first IHittable that can be hit takes the hit (durability −1, may break). False = miss.
        public static bool Strike(Vector3 origin, Vector3 dir, ItemDef weapon, InvStack stack, bool underwater, Transform ignoreRoot, out HitInfo hit)
        {
            hit = default;
            if (weapon == null || !weapon.IsWeapon || dir.sqrMagnitude < 1e-6f) return false;
            dir.Normalize();
            float reach = Mathf.Max(0.5f, weapon.reach);
            float radius = weapon.hold == HoldItem.SpearGun ? 0.15f : 0.35f;
            int n = Physics.SphereCastNonAlloc(origin, radius, dir, Hits, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            IHittable best = null;
            float bestD = float.MaxValue;
            Vector3 bestP = origin + dir * reach;
            for (int i = 0; i < n; i++)
            {
                var c = Hits[i].collider;
                if (c == null) continue;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                var h = c.GetComponentInParent<IHittable>();
                if (h == null || !h.CanBeHit) continue;
                float d = Hits[i].distance;
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                    bestP = Hits[i].point == Vector3.zero ? c.bounds.center : Hits[i].point;
                }
            }
            for (int i = 0; i < n; i++) Hits[i] = default;
            // animals (no colliders): the fauna module's sweep
            var soft = SoftTargets;
            if (soft != null)
            {
                try
                {
                    if (soft(origin, dir, reach, radius, ignoreRoot, out var sh, out float sd, out Vector3 sp) && sh != null && sh.CanBeHit && sd < bestD)
                    {
                        best = sh;
                        bestD = sd;
                        bestP = sp;
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
            }
            if (best == null) return false;
            hit = new HitInfo
            {
                point = bestP,
                direction = dir,
                damage = Damage(weapon, stack, underwater),
                knockback = weapon.knockback * (underwater ? 0.6f : 1f),
                stun = weapon.stun,
                weaponId = weapon.id,
                underwater = underwater,
                attackerId = Net.IsOnline ? Net.LocalId : 0,
            };
            HitRefused = false;
            try { best.OnHit(hit); }
            catch (Exception e) { Debug.LogException(e); }
            bool refused = HitRefused;
            HitRefused = false;
            if (!refused) Wear(weapon, stack);
            HitLanded?.Invoke(hit, best);
            if (!refused) GameEvents.RaiseFight("hit", Mathf.Clamp01(hit.damage / 30f));
            return true;
        }

        /// Durability −1; a broken weapon leaves the backpack with a toast.
        static void Wear(ItemDef weapon, InvStack stack)
        {
            if (weapon == null || stack == null || !weapon.HasDurability) return;
            stack.durability = Mathf.Max(0f, stack.durability - 1f);
            if (stack.durability > 0f) { Backpack.RaiseChanged(); return; }
            int idx = Backpack.IndexOf(stack);
            if (idx >= 0) Backpack.RemoveAt(idx, 1);
            GameEvents.RaiseNotify(NotifyKind.Warning, weapon.Name, Loc.Tr("zerbrochen", "broke"));
        }

        // ------------------------------------------------------------------ grinder
        /// The hermits taught the technique (npc module sets StoryKeys.GrindLearned).
        public static bool GrindLearned => StoryFlags.Has(StoryKeys.GrindLearned);

        /// The weapon stack the grinder would sharpen: the held spear, else the bluntest spear in the backpack.
        public static InvStack SharpenTarget()
        {
            string held = Hands.ActiveId(Hand.Right);
            var hd = ItemDatabase.Get(held);
            if (hd != null && hd.Sharpenable)
            {
                var st = Backpack.First(held);
                if (st != null && st.sharpness < 0.999f) return st;
            }
            InvStack best = null;
            var l = Backpack.Stacks;
            for (int i = 0; i < l.Count; i++)
            {
                var st = l[i];
                if (st == null || st.count <= 0) continue;
                var d = st.Def;
                if (d == null || !d.Sharpenable || st.sharpness >= 0.999f) continue;
                if (best == null || st.sharpness < best.sharpness) best = st;
            }
            return best;
        }

        /// Why the grinder cannot be used right now (null = it can).
        public static string SharpenBlocker()
        {
            if (!GrindLearned) return Loc.Tr("Schleifen lernst du bei den Einsiedlern", "The hermits know how to grind");
            if (SharpenTarget() == null) return Loc.Tr("Kein stumpfer Speer", "No blunt spear");
            if (!Backpack.Has(ItemIds.Stone, SharpenStones)) return Loc.Tr("Schleifstein fehlt", "Need a whetstone");
            return null;
        }

        /// Sharpens the target spear by one step (+0.25), costs one whetstone.
        public static bool Sharpen()
        {
            if (SharpenBlocker() != null) return false;
            var st = SharpenTarget();
            var d = st != null ? st.Def : null;
            if (d == null) return false;
            Backpack.Remove(ItemIds.Stone, SharpenStones);
            st.sharpness = Mathf.Min(1f, Mathf.Max(0f, st.sharpness) + SharpenStep);
            Backpack.RaiseChanged();
            GameEvents.RaiseNotify(NotifyKind.Info, d.Name, Loc.Tr("Schärfe ", "Sharpness ") + Mathf.RoundToInt(st.sharpness * 100f) + " %");
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HitLanded = null;
            LocalHurt = null;
            LocalKnockedOut = null;
            SoftTargets = null;
            HitRefused = false;
            health = MaxHealth;
            lastHurt = -100f;
            IsBlocking = false;
            BlockDefense = 0f;
        }
    }
}
