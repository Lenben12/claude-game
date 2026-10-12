using System;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// What a weapon hit on an animal means (round 3, GDD §20 Q18/Q45).
    public enum FaunaHitKind : byte { None, Catch, Defend, Foul, Shoo }

    /// Round 3 (Testversion 5) – weapons against animals:
    ///  * Q18 spear fishing: a spear / speargun hit on a FOOD fish (SpeciesDef.food: sardine, tuna, grouper, lionfish,
    ///    mackerel, snapper, …) catches it – it vanishes in a puff of scales and raw fish lands in the backpack. Blood in
    ///    the water draws sharks (FaunaThreat.AddBlood).
    ///  * Q18 protected animals (everything that is neither food nor hostile: turtles, rays, dolphins, whales, Gerald,
    ///    seahorses, reef fish …): the hit is REFUSED (Combat.HitRefused – no durability loss, no fight hype), a toast
    ///    warns and the livestream gets haters (StreamService.ReportFoul).
    ///  * Q17/Q45 hostile animals (sharks that hunt, barracuda, morays, the monster): the hit repels them – knock-back,
    ///    flight, a stalking shark gives up (FaunaThreat) and the stream celebrates.
    ///  * the stick on a food fish / an island animal: it just runs away.
    /// Animals have no colliders: Combat.SoftTargets = Query sweeps the registered creatures (host animals, co-op
    /// proxies). Co-op guests hit proxies; NetCreatureProxy.OnHit → ProxyHit applies the local part and forwards the
    /// hit to the host (NetCommand.Custom Combat.HitKey: netId, kind, dir, damage, knockback).
    public static class FaunaCombat
    {
        /// Counters for tests / the playtest log.
        public static int Catches, Fouls, Defends, Shoos;
        /// Raised on the attacker's machine for every hit on an animal (species, kind).
        public static event Action<SpeciesDef, FaunaHitKind> AnimalHit;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly RaycastHit[] Los = new RaycastHit[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Catches = 0; Fouls = 0; Defends = 0; Shoos = 0; AnimalHit = null; }

        // ================================================================== rules
        public static FaunaHitKind Classify(SpeciesDef s, string weaponId, bool underwater)
        {
            if (s == null || s.meme) return FaunaHitKind.None;
            if (s.Hostile) return FaunaHitKind.Defend;
            bool spear = weaponId != ItemIds.Stick;
            if (s.food) return spear && underwater ? FaunaHitKind.Catch : FaunaHitKind.Shoo;
            if (s.deco) return FaunaHitKind.Shoo;
            return FaunaHitKind.Foul;
        }

        /// 2 = a star of the channel (Gerald & co., turtles, rays, dolphins, whales, seahorses), 1 = any other protected animal.
        public static int FoulStrength(SpeciesDef s)
        {
            if (s == null) return 1;
            if (s.unique) return 2;
            switch (s.plan)
            {
                case BodyPlan.Turtle: case BodyPlan.Ray: case BodyPlan.Dolphin: case BodyPlan.Whale: case BodyPlan.SeaLion: case BodyPlan.Seahorse:
                    return 2;
            }
            return s.id == "whale_shark" || s.id == "coelacanth" ? 2 : 1;
        }

        /// Raw fish a caught food fish gives (a big tuna feeds more).
        public static int RawFishFor(SpeciesDef s, float sizeCm) => Mathf.Clamp(Mathf.RoundToInt(sizeCm / 45f), 1, 3);

        // ================================================================== soft target sweep (Combat.SoftTargets)
        /// The nearest hittable animal along the strike. Allocation free. Rocks / wreck walls in between block it.
        public static bool Query(Vector3 origin, Vector3 dir, float reach, float radius, Transform ignoreRoot,
                                 out IHittable target, out float distance, out Vector3 point)
        {
            target = null;
            distance = float.MaxValue;
            point = origin;
            var list = CreatureRegistry.Active;
            float best = float.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || !(c is IHittable h)) continue;
                if (c is UnityEngine.Object uo && uo == null) continue;
                if (!h.CanBeHit) continue;
                Bounds b = c.WorldBounds;
                Vector3 to = b.center - origin;
                if (to.sqrMagnitude > (reach + 8f) * (reach + 8f)) continue;
                Vector3 e = b.extents;
                float r = Mathf.Clamp(Mathf.Max(e.x, Mathf.Max(e.y, e.z)) * 0.75f, 0.12f, 6f);
                float t = Vector3.Dot(to, dir);
                if (t < -r || t > reach + r) continue;
                Vector3 perp = to - dir * t;
                if (perp.sqrMagnitude > (r + radius) * (r + radius)) continue;
                float d = Mathf.Clamp(t - r * 0.6f, 0f, reach);
                if (d >= best) continue;
                Vector3 p = origin + dir * d;
                if (Blocked(origin, dir, d, ignoreRoot, c.Transform)) continue;
                best = d;
                target = h;
                point = p;
            }
            distance = best;
            return target != null;
        }

        static bool Blocked(Vector3 origin, Vector3 dir, float dist, Transform ignoreRoot, Transform self)
        {
            if (dist < 0.3f) return false;
            int n = Physics.RaycastNonAlloc(origin, dir, Los, dist, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore);
            bool blocked = false;
            for (int i = 0; i < n && !blocked; i++)
            {
                var col = Los[i].collider;
                if (col == null) continue;
                var ct = col.transform;
                if (ignoreRoot != null && ct.IsChildOf(ignoreRoot)) continue;
                if (self != null && ct.IsChildOf(self)) continue;
                blocked = true;
            }
            for (int i = 0; i < n; i++) Los[i] = default;
            return blocked;
        }

        // ================================================================== local attacker (host / offline)
        /// The local player's weapon hit a real (host-simulated) animal.
        internal static void LocalHitOnCreature(FaunaCreature c, in HitInfo hit)
        {
            if (c == null || c.species == null) return;
            var kind = Classify(c.species, hit.weaponId, hit.underwater);
            LocalAttackerPart(c.species, c.sizeCm, c.pos, kind);
            ApplyOnCreature(c, kind, hit.direction, hit.damage, hit.knockback, true, hit.attackerId);
        }

        /// A guest's weapon hit a proxy of the host's animal: local part now, the rest on the host.
        internal static void ProxyHit(NetCreatureProxy px, in HitInfo hit)
        {
            if (px == null || px.Species == null) return;
            var s = px.Species;
            var kind = Classify(s, hit.weaponId, hit.underwater);
            LocalAttackerPart(s, px.SizeCm, px.Position, kind);
            if (kind == FaunaHitKind.Catch) px.MarkCaught();
            Vector3 d = hit.direction;
            Net.Command(NetCommand.Custom, Combat.HitKey, px.NetId.ToString(Inv), ((int)kind).ToString(Inv),
                d.x.ToString("0.###", Inv), d.y.ToString("0.###", Inv), d.z.ToString("0.###", Inv),
                hit.damage.ToString("0.#", Inv), hit.knockback.ToString("0.##", Inv));
        }

        /// What the attacker's own machine does (backpack, toasts, refusal flag, scattering fish).
        static void LocalAttackerPart(SpeciesDef s, float sizeCm, Vector3 at, FaunaHitKind kind)
        {
            switch (kind)
            {
                case FaunaHitKind.Catch:
                    {
                        Catches++;
                        int n = RawFishFor(s, sizeCm);
                        int added = Backpack.Add(ItemIds.RawFish, n, false);
                        if (added > 0) GameEvents.RaiseNotify(NotifyKind.Collectible, Loc.Tr("Gefangen: ", "Caught: ") + s.Name, Loc.Tr("Roher Fisch +", "Raw fish +") + added.ToString(Inv));
                        else GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Rucksack voll", "Backpack full"), s.Name);
                        FaunaFx.Scales(at, Mathf.Max(0.3f, sizeCm * 0.02f));
                        FXSfx.Play(FXSound.Catch, 0.7f);
                        break;
                    }
                case FaunaHitKind.Foul:
                    Fouls++;
                    Combat.HitRefused = true;
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Geschützt! Finger weg", "Protected! Hands off"), s.Name);
                    FXSfx.Play(FXSound.Error, 0.6f);
                    break;
                case FaunaHitKind.Shoo:
                    Shoos++;
                    Combat.HitRefused = true;   // a shove, not a fight: no wear, no hype
                    break;
                case FaunaHitKind.Defend:
                    Defends++;
                    break;
            }
            // the reef reacts: small fish around the hit dart away
            FaunaReact.Startle(at, kind == FaunaHitKind.Defend ? 9f : 6f, 2.5f);
            try { AnimalHit?.Invoke(s, kind); }
            catch (Exception e) { Debug.LogException(e); }
        }

        // ================================================================== host side
        /// The animal's reaction (host / offline only – the animal lives here).
        internal static void ApplyOnCreature(FaunaCreature c, FaunaHitKind kind, Vector3 dir, float damage, float knockback, bool attackerLocal, int attackerId)
        {
            if (c == null || !c.alive) return;
            var s = c.species;
            switch (kind)
            {
                case FaunaHitKind.Catch:
                    if (!attackerLocal) FaunaFx.Scales(c.pos, Mathf.Max(0.3f, c.lengthM * 2f));
                    FaunaThreat.AddBlood(c.pos, 0.35f);
                    // a school mate caught: the rest of the school scatters
                    if (c.school != null) c.school.scare = 3f;
                    c.RequestDespawn();
                    break;
                case FaunaHitKind.Defend:
                    c.StartHitFlee(dir, s != null && s.monster ? 3f : Mathf.Lerp(4f, 8f, Mathf.Clamp01(damage / 40f)), Mathf.Max(1.5f, knockback));
                    c.scare += Mathf.Max(5f, damage);
                    FaunaThreat.OnHostileHit(c, damage, attackerLocal, attackerId);
                    break;
                case FaunaHitKind.Foul:
                    c.StartHitFlee(dir, 3f, 1.2f);
                    if (c.behavior != null) c.behavior.Poke(c.pos - dir);
                    var ss = StreamService.Instance;
                    if (ss != null) ss.ReportFoul(FoulStrength(s));
                    break;
                case FaunaHitKind.Shoo:
                    c.StartHitFlee(dir, 2.5f, 1f);
                    if (c.behavior != null) c.behavior.Poke(c.pos - dir);
                    break;
            }
        }

        /// NetCommand.Custom Combat.HitKey from a guest: netId, kind, dir x/y/z, damage, knockback.
        internal static bool HostHitCommand(int fromId, string[] args)
        {
            if (args == null || args.Length < 3) return false;
            var spawner = FaunaModule.Spawner;
            if (spawner == null) return false;
            if (!int.TryParse(args[1], NumberStyles.Integer, Inv, out int netId)) return false;
            int.TryParse(args[2], NumberStyles.Integer, Inv, out int kindI);
            Vector3 dir = new Vector3(F(args, 3), F(args, 4), F(args, 5));
            float damage = Mathf.Clamp(F(args, 6), 0f, 100f);
            float knock = Mathf.Clamp(F(args, 7), 0f, 12f);
            var list = spawner.ActiveCreatures;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c.netId != netId || !c.alive || c.pendingDespawn) continue;
                // the host re-derives the kind from its own species (the guest only proposes it)
                var kind = (FaunaHitKind)Mathf.Clamp(kindI, 0, (int)FaunaHitKind.Shoo);
                if (kind == FaunaHitKind.Catch && (c.species == null || !c.species.food)) kind = FaunaHitKind.Shoo;
                if (kind == FaunaHitKind.Defend && (c.species == null || !c.species.Hostile)) kind = FaunaHitKind.Foul;
                if (dir.sqrMagnitude < 1e-4f) dir = c.pos - FaunaContext.PlayerPos;
                ApplyOnCreature(c, kind, dir.normalized, damage, knock, false, fromId);
                return true;
            }
            return false;
        }

        static float F(string[] a, int i) => a != null && i < a.Length && float.TryParse(a[i], NumberStyles.Float, Inv, out float v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : 0f;
    }
}
