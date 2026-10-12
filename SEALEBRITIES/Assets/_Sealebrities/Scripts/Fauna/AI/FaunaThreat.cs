using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §20 Q16/Q17/Q44/Q45) – shark attacks: RARE but exciting, always announced.
    /// HOST / offline: every few seconds each diver (≥ 3 m deep, ≥ 90 s into the dive, not in a cooldown of 3.5–6 min
    /// after the last attack on them) rolls a small chance per minute that depends on the zone (house reef 3 % …
    /// open / deep sea 9–10 %), the time of day (night × 2.2), blood in the water (spear fishing, up to × 3), the
    /// difficulty's predator aggression, the weather and the co-op player count. On a hit a hunter (SpeciesDef.hunter:
    /// whitetip, hammerhead, tiger, sixgill, barracuda – whatever lives there and is unlocked) already nearby is taken,
    /// else one arrives from out of view. A director then steers it: approach → CIRCLE the diver (8–13 s, closing in,
    /// the danger music starts and a warning pops up) → one CHARGE → retreat. A charge that connects is a BITE
    /// (Combat.HurtLocal on the victim's machine: knock-back + "Puste", air loss and maybe 1–2 clips of the current
    /// dive – never death). Any weapon hit (FaunaCombat) REPELS it: it flees, the attacker gets "Abgewehrt!" and the
    /// livestream a big hype spike (Q45). One stalk at a time in the whole session.
    /// Co-op: the host simulates; bites, music cues and toasts reach the guests as NetSyncMsg.FaunaCombat (103).
    /// Also ticks the weather influence (FaunaWeather) and owns the hit command (Combat.HitKey) + soft targets.
    public sealed class FaunaThreat : MonoBehaviour
    {
        public const float FirstAttackAfter = 90f;
        public const float RollEvery = 4f;
        public const float CooldownMin = 210f, CooldownMax = 360f;
        public const float SpawnMin = 28f, SpawnMax = 38f;
        /// Shark bite: damage ("Puste" 0..100), air seconds lost, chance to lose 1–2 clips of the current dive.
        public const float BiteDamage = 38f, BiteAir = 12f, BiteClipChance = 0.5f;

        const byte SubBite = 1, SubCueOn = 2, SubCueOff = 3, SubRepelled = 4, SubCircling = 5;

        public static FaunaThreat Instance { get; private set; }
        /// Counters for tests / logs.
        public static int Stalks, Bites, Repels, Misses;
        /// Blood in the water (spear fishing): level 0..1.5 at a spot, decays over ~2 minutes.
        static float bloodLevel;
        static Vector3 bloodPos;

        sealed class DiverState { public int id; public float diveTime, cooldownUntil, roll; }
        readonly List<DiverState> states = new List<DiverState>(4);
        StalkDirector stalk;
        FaunaDangerMusic music;
        readonly SyncWriter w = new SyncWriter(48);
        readonly List<SpeciesDef> pick = new List<SpeciesDef>(8);
        readonly List<float> pickW = new List<float>(8);
        System.Random rng = new System.Random(4711);
        bool handlersOn;

        public static bool Busy => Instance != null && Instance.stalk != null;
        public static FaunaCreature ActiveHunter => Instance != null && Instance.stalk != null ? Instance.stalk.c : null;
        /// Current stalk phase for tests (0 none, 1 approach, 2 circle, 3 charge, 4 retreat).
        public static int Phase => Instance != null && Instance.stalk != null ? (int)Instance.stalk.ph + 1 : 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; Stalks = 0; Bites = 0; Repels = 0; Misses = 0; bloodLevel = 0f; }

        void Awake()
        {
            Instance = this;
            music = gameObject.AddComponent<FaunaDangerMusic>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnEnable()
        {
            NetBus.Register(NetSyncMsg.FaunaCombat, OnMsg);
            handlersOn = true;
            CoreCommands.RegisterCustom(Combat.HitKey, FaunaCombat.HostHitCommand);
            Combat.SoftTargets = FaunaCombat.Query;
            GameEvents.GameStarted += OnReset;
            GameEvents.ReturnedToMenu += OnReset;
        }

        void OnDisable()
        {
            if (handlersOn) { NetBus.Unregister(NetSyncMsg.FaunaCombat); handlersOn = false; }
            if (Combat.SoftTargets == (Combat.SoftTargetQuery)FaunaCombat.Query) Combat.SoftTargets = null;
            GameEvents.GameStarted -= OnReset;
            GameEvents.ReturnedToMenu -= OnReset;
        }

        void OnReset()
        {
            EndStalk();
            states.Clear();
            bloodLevel = 0f;
            if (music != null) music.Stop(true);
        }

        // ================================================================== public API
        /// Spear fishing spills blood: sharks get curious (higher attack chance near this spot for ~2 minutes).
        public static void AddBlood(Vector3 p, float amount)
        {
            if (bloodLevel <= 0.05f || (p - bloodPos).sqrMagnitude > 60f * 60f) bloodPos = p;
            else bloodPos = Vector3.Lerp(bloodPos, p, 0.3f);
            bloodLevel = Mathf.Min(1.5f, bloodLevel + Mathf.Max(0f, amount));
        }

        public static float BloodAt(Vector3 p) => bloodLevel > 0f && (p - bloodPos).sqrMagnitude < 70f * 70f ? bloodLevel : 0f;

        /// A weapon hit a hostile animal (host side, FaunaCombat).
        internal static void OnHostileHit(FaunaCreature c, float damage, bool attackerLocal, int attackerId)
        {
            var inst = Instance;
            if (c == null) return;
            bool won = false;
            if (inst != null && inst.stalk != null && ReferenceEquals(inst.stalk.c, c)) { inst.stalk.Repel(); won = true; }
            if (c.behavior is FaunaMonsterBehavior mb) won |= mb.OnWeaponHit(damage);
            if (!won) return;
            Repels++;
            var ss = StreamService.Instance;
            if (ss != null) ss.ReportFightWon(c.species != null && c.species.monster);
            if (attackerLocal) RepelledToast(c.species);
            else if (inst != null) inst.SendSub(attackerId, SubRepelled, c.species);
        }

        /// A hunter / the monster bit a diver (host side). Applied on the victim's machine.
        internal static void Bite(int victimNetId, bool victimLocal, Vector3 from, SpeciesDef s, float damage, float air, float clipChance)
        {
            Bites++;
            FaunaReact.Startle(from, 10f, 3f);
            if (victimLocal) { ApplyBiteLocal(from, s, damage, air, clipChance); return; }
            var inst = Instance;
            if (inst == null) return;
            var w = inst.w;
            w.Reset();
            w.Byte(SubBite);
            w.Float(damage);
            w.Vec3(from);
            w.Float(air * FaunaContext.BumpAirMul);
            w.Float(clipChance);
            w.UShort((ushort)Mathf.Max(0, StreamEye.IndexOf(s)));
            NetBus.Send(NetSyncMsg.FaunaCombat, w.ToArray(), victimNetId, true);
        }

        /// The danger music for one diver (on / off) + the circling warning.
        internal static void Cue(int victimNetId, bool victimLocal, bool on, SpeciesDef s)
        {
            var inst = Instance;
            if (inst == null) return;
            if (victimLocal)
            {
                if (on) { inst.music.Play(); CirclingToast(s); }
                else inst.music.Stop(false);
                return;
            }
            // the host's own player nearby hears it too
            if (FaunaContext.DiverCount > 0 && on)
            {
                Vector3 me = FaunaContext.DiverList[0].head;
                if (TryVictim(victimNetId, out var vd) && (vd.head - me).sqrMagnitude < 30f * 30f && FaunaContext.DiverList[0].underwater) inst.music.Play();
            }
            if (!on) inst.music.Stop(false);
            inst.SendSub(victimNetId, on ? SubCueOn : SubCueOff, s);
            if (on) inst.SendSub(victimNetId, SubCircling, s);
        }

        /// Tests: starts a stalk on the local diver right now (optionally with a given hunter species).
        public bool ForceStalk(string speciesId = null)
        {
            if (stalk != null || !FaunaContext.HasPlayer || FaunaContext.DiverCount == 0) return false;
            ref FaunaDiver d = ref FaunaContext.DiverList[0];
            return TryStart(0, FaunaZones.ZoneAt(d.head), speciesId);
        }

        /// Tests (host, co-op): starts a stalk on the diver with this net id (a guest).
        public bool ForceStalkOn(int netId, string speciesId = null)
        {
            if (stalk != null || !Net.HasAuthority) return false;
            for (int k = 0; k < FaunaContext.DiverCount; k++)
                if (FaunaContext.DiverList[k].netId == netId)
                    return TryStart(k, FaunaZones.ZoneAt(FaunaContext.DiverList[k].head), speciesId);
            return false;
        }

        /// Tests / debugging: ends a running stalk.
        public void CancelStalk() => EndStalk();

        // ================================================================== frame
        void Update()
        {
            FaunaWeather.Tick(Time.deltaTime);
            float dt = Time.deltaTime;
            if (bloodLevel > 0f) bloodLevel = Mathf.Max(0f, bloodLevel - dt / 90f);

            var gm = GameManager.I;
            if (gm == null || !gm.InGame || !Net.HasAuthority || NetFlags.CreaturesRemote)
            {
                if (stalk != null) EndStalk();
                return;
            }
            if (UIState.PauseOpen && !Net.IsOnline) return;
            var spawner = FaunaModule.Spawner;
            if (spawner == null || !FaunaContext.HasPlayer) return;

            if (stalk != null)
            {
                if (!stalk.Valid) EndStalk();
                return;
            }

            int n = FaunaContext.DiverCount;
            for (int k = 0; k < n; k++)
            {
                ref FaunaDiver d = ref FaunaContext.DiverList[k];
                var st = State(d.netId);
                float depth = Waves.SeaLevel - d.head.y;
                if (!d.underwater || depth < 3f) { if (!d.inWater) st.diveTime = 0f; continue; }
                st.diveTime += dt;
                if (st.diveTime < FirstAttackAfter || Time.time < st.cooldownUntil) continue;
                if (FaunaContext.Time < (d.local ? FaunaContext.CalmUntil : d.calmUntil)) continue;
                st.roll -= dt;
                if (st.roll > 0f) continue;
                st.roll = RollEvery;
                string zone = FaunaZones.ZoneAt(d.head);
                float perMinute = ChancePerMinute(zone, d.head);
                if (rng.NextDouble() >= perMinute * RollEvery / 60f) continue;
                if (TryStart(k, zone, null))
                {
                    st.cooldownUntil = Time.time + Mathf.Lerp(CooldownMin, CooldownMax, (float)rng.NextDouble());
                    break;
                }
            }
        }

        DiverState State(int id)
        {
            for (int i = 0; i < states.Count; i++) if (states[i].id == id) return states[i];
            var s = new DiverState { id = id, roll = RollEvery };
            states.Add(s);
            return s;
        }

        /// Attack chance per minute of dive time at a spot (Q16 "selten").
        public static float ChancePerMinute(string zone, Vector3 at)
        {
            float b;
            switch (zone)
            {
                case Zones.HouseReef: b = 0.03f; break;
                case Zones.Lagoon: b = 0.015f; break;
                case Zones.Kelp: b = 0.05f; break;
                case Zones.Wreck: b = 0.08f; break;
                case Zones.BlueDrop: b = 0.09f; break;
                case Zones.Trench: b = 0.06f; break;
                case Zones.FogIsle: b = 0.03f; break;
                case Zones.Volcano: b = 0.07f; break;
                case Zones.DeepSea: b = 0.1f; break;
                default: b = 0.09f; break;   // open sea
            }
            var now = FaunaContext.DayNow;
            if (now == DayMask.Night) b *= 2.2f;
            else if (now == DayMask.Dusk) b *= 1.4f;
            b *= 1f + 1.4f * BloodAt(at);
            b *= Mathf.Clamp(FaunaContext.Aggression, 0.25f, 2f);
            b *= FaunaWeather.PredatorMul;
            return b;
        }

        // ================================================================== start / end
        bool TryStart(int k, string zone, string forced)
        {
            var spawner = FaunaModule.Spawner;
            if (spawner == null || k < 0 || k >= FaunaContext.DiverCount) return false;
            FaunaDiver d = FaunaContext.DiverList[k];
            FaunaCreature hunter = null;
            SpeciesDef forcedDef = forced != null ? SpeciesDatabase.Get(forced) : null;

            // a hunter already around?
            var list = spawner.ActiveCreatures;
            float best = 70f * 70f;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || !c.alive || c.pendingDespawn || c.hallucination || c.director != null || c.species == null) continue;
                if (forcedDef != null ? c.species != forcedDef : !c.species.hunter) continue;
                if (c.species.monster) continue;
                float d2 = (c.pos - d.head).sqrMagnitude;
                if (d2 < best) { best = d2; hunter = c; }
            }
            if (hunter == null)
            {
                var s = forcedDef ?? PickHunter(zone);
                if (s == null) return false;
                Vector3 p = OffstagePoint(d, s);
                hunter = spawner.SpawnOne(s, p, p, 120f);
                if (hunter == null) return false;
            }
            stalk = new StalkDirector(this, hunter, d.netId, d.local);
            hunter.director = stalk;
            Stalks++;
            Debug.Log("[Fauna] stalk: " + hunter.species.id + " hunts diver " + (d.local ? "local" : d.netId.ToString()) + " in " + zone);
            return true;
        }

        SpeciesDef PickHunter(string zone)
        {
            pick.Clear();
            pickW.Clear();
            bool night = FaunaContext.NightNow;
            void Add(string id, float wgt)
            {
                var s = SpeciesDatabase.Get(id);
                if (s == null || GameManager.I == null || !Progression.IsGateOpen(s)) return;
                pick.Add(s);
                pickW.Add(wgt);
            }
            switch (zone)
            {
                case Zones.HouseReef: case Zones.Lagoon: case Zones.FogIsle:
                    Add("whitetip_shark", 1f);
                    if (night) Add("tiger_shark", 0.25f);
                    break;
                case Zones.Kelp:
                    Add("whitetip_shark", 1f); Add("barracuda", 0.6f);
                    break;
                case Zones.Wreck:
                    Add("hammerhead", 1f); Add("barracuda", 0.8f); Add("whitetip_shark", 0.6f);
                    break;
                case Zones.BlueDrop:
                    Add("hammerhead", 1f); Add("tiger_shark", 0.7f);
                    break;
                case Zones.Trench: case Zones.DeepSea:
                    Add("tiger_shark", 1f); Add("hammerhead", 0.5f);
                    break;
                case Zones.Volcano:
                    Add("sixgill_shark", 1f); Add("tiger_shark", 0.6f);
                    break;
                default:
                    Add("tiger_shark", 1f); Add("hammerhead", 0.7f); Add("barracuda", 0.5f);
                    break;
            }
            // nothing unlocked there yet: the reef shark is always allowed (house reef species)
            if (pick.Count == 0) Add("whitetip_shark", 1f);
            if (pick.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < pickW.Count; i++) total += pickW[i];
            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < pick.Count; i++) { r -= pickW[i]; if (r <= 0f) return pick[i]; }
            return pick[pick.Count - 1];
        }

        Vector3 OffstagePoint(in FaunaDiver d, SpeciesDef s)
        {
            Vector3 fwd = FaunaNav.FlatDir(d.forward);
            if (fwd == Vector3.zero) fwd = Vector3.forward;
            for (int i = 0; i < 8; i++)
            {
                float ang = Mathf.Lerp(110f, 250f, (float)rng.NextDouble());
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * fwd;
                Vector3 p = d.head + dir * Mathf.Lerp(SpawnMin, SpawnMax, (float)rng.NextDouble());
                p.y = d.head.y + Mathf.Lerp(-3f, 2f, (float)rng.NextDouble());
                if (WorldInfo.GroundHeight(p) > FaunaNav.LandGuardY - 1f) continue;
                if (FaunaNav.ClampIntoWater(ref p, 1.2f, 0.5f)) return p;
            }
            Vector3 q = d.head - fwd * SpawnMin;
            FaunaNav.ClampIntoWater(ref q, 1.2f, 0.5f);
            return q;
        }

        void EndStalk()
        {
            if (stalk == null) return;
            var s = stalk;
            stalk = null;
            s.Finish();
        }

        internal static bool TryVictim(int netId, out FaunaDiver d)
        {
            for (int i = 0; i < FaunaContext.DiverCount; i++)
            {
                if (FaunaContext.DiverList[i].netId != netId) continue;
                d = FaunaContext.DiverList[i];
                return true;
            }
            d = default;
            return false;
        }

        // ================================================================== net (host → one guest)
        void SendSub(int target, byte sub, SpeciesDef s)
        {
            if (!Net.IsHost) return;
            w.Reset();
            w.Byte(sub);
            w.UShort((ushort)Mathf.Max(0, StreamEye.IndexOf(s)));
            NetBus.Send(NetSyncMsg.FaunaCombat, w.ToArray(), target, true);
        }

        void OnMsg(int fromId, byte[] payload)
        {
            if (Net.IsHost) return;   // only the host's fauna attacks
            var r = new SyncReader(payload);
            byte sub = r.Byte();
            if (r.Failed) return;
            switch (sub)
            {
                case SubBite:
                    {
                        float dmg = r.Float();
                        Vector3 from = r.Vec3();
                        float air = r.Float();
                        float clip = r.Float();
                        var s = StreamEye.SpeciesAt(r.UShort());
                        if (r.Failed) return;
                        ApplyBiteLocal(from, s, Mathf.Clamp(dmg, 0f, 100f), Mathf.Clamp(air, 0f, 60f), Mathf.Clamp01(clip));
                        break;
                    }
                case SubCueOn: music.Play(); break;
                case SubCueOff: music.Stop(false); break;
                case SubRepelled: RepelledToast(StreamEye.SpeciesAt(r.UShort())); break;
                case SubCircling: CirclingToast(StreamEye.SpeciesAt(r.UShort())); break;
            }
        }

        // ================================================================== on the victim's machine
        static void ApplyBiteLocal(Vector3 from, SpeciesDef s, float damage, float air, float clipChance)
        {
            var p = Refs.Player;
            if (p == null || (p is Object o && o == null) || p.IsFainting) return;
            float taken = Combat.HurtLocal(damage, from, s != null ? s.id : "shark");
            if (taken <= 0f) return;
            // Q17: air loss, but a bite never takes the last 20 s of air (no death by shark)
            float loss = Mathf.Min(air * Mathf.Clamp01(taken / Mathf.Max(1f, damage) + 0.25f), 60f, Mathf.Max(0f, p.AirSecondsLeft - 20f));
            air = loss;
            if (p.IsUnderwater && loss > 0.5f) GameEvents.RaisePlayerBumped(loss);
            if (Random.value < clipChance) LoseClips(Random.value < 0.35f ? 2 : 1);
            var inst = Instance;
            if (inst != null && inst.music != null) inst.music.Hit();
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Biss! ", "Bitten! ") + (s != null ? s.Name : ""), Loc.Tr("Luft −", "Air −") + Mathf.RoundToInt(air) + " s");
        }

        /// The camera got knocked: the newest clips of this dive are lost (never all of them).
        static void LoseClips(int n)
        {
            var gm = GameManager.I;
            if (gm == null || gm.State == null) return;
            var list = gm.State.diveFootage;
            if (list == null || list.Count == 0) return;
            n = Mathf.Min(n, Mathf.Max(1, list.Count - 1));
            int lost = 0;
            for (int i = 0; i < n && list.Count > 0; i++) { list.RemoveAt(list.Count - 1); lost++; }
            if (lost > 0) GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Kamera gerempelt", "Camera knocked"), Loc.Tr("Clips −", "Clips −") + lost);
        }

        static void RepelledToast(SpeciesDef s) =>
            GameEvents.RaiseNotify(NotifyKind.Event, Loc.Tr("Abgewehrt!", "Repelled!"), s != null ? s.Name : null);

        static void CirclingToast(SpeciesDef s)
        {
            bool monster = s != null && s.monster;
            GameEvents.RaiseNotify(NotifyKind.Warning, monster ? Loc.Tr("Etwas Riesiges kommt …", "Something huge is coming …") : Loc.Tr("Ein Hai kreist!", "A shark is circling!"),
                Loc.Tr("Speer bereit · RMB blocken", "Spear ready · RMB blocks"));
        }

        // ================================================================== the stalk
        /// approach → circle (announced) → one charge → retreat. Repel() ends it early (weapon hit).
        sealed class StalkDirector : IFaunaDirector
        {
            public enum Ph { Approach, Circle, Charge, Retreat }
            readonly FaunaThreat owner;
            public readonly FaunaCreature c;
            readonly int victimId;
            readonly bool victimLocal;
            public Ph ph;
            float t, orbit, orbitDir, radius, startle, life;
            bool cueOn, done, repelled;

            public StalkDirector(FaunaThreat owner, FaunaCreature c, int victimId, bool victimLocal)
            {
                this.owner = owner;
                this.c = c;
                this.victimId = victimId;
                this.victimLocal = victimLocal;
                ph = Ph.Approach;
                t = 25f;
                orbitDir = Random.value < 0.5f ? -1f : 1f;
            }

            public bool Valid => !done && c != null && c.alive && !c.pendingDespawn && ReferenceEquals(c.director, this) && life < 90f;

            public void Repel()
            {
                if (ph == Ph.Retreat && repelled) return;
                repelled = true;
                ph = Ph.Retreat;
                t = 7f;
                CueOff();
            }

            void CueOff()
            {
                if (!cueOn) return;
                cueOn = false;
                Cue(victimId, victimLocal, false, c != null ? c.species : null);
            }

            public void Finish()
            {
                done = true;
                CueOff();
                if (c != null && ReferenceEquals(c.director, this)) c.director = null;
            }

            public bool Steer(FaunaCreature cr, float dt)
            {
                life += dt;
                t -= dt;
                if (!TryVictim(victimId, out var v) || !v.underwater)
                {
                    if (ph != Ph.Retreat) { ph = Ph.Retreat; t = 5f; CueOff(); }
                }
                Vector3 head = ph != Ph.Retreat || v.head != Vector3.zero ? v.head : cr.pos;
                cr.relaxBand = true;
                switch (ph)
                {
                    case Ph.Approach:
                        {
                            Vector3 off = cr.pos - head;
                            off.y = 0f;
                            if (off.sqrMagnitude < 1e-3f) off = Vector3.forward;
                            Vector3 tgt = head + off.normalized * 10f;
                            tgt.y = head.y - 0.5f;
                            cr.desiredVel = cr.Seek(tgt, Mathf.Max(cr.cruise * 1.8f, 1.5f), 3f);
                            cr.activityTarget = 0.5f;
                            if ((cr.pos - head).sqrMagnitude < 13f * 13f)
                            {
                                ph = Ph.Circle;
                                t = Random.Range(8f, 13f);
                                radius = 9f;
                                orbit = Mathf.Atan2(off.x, off.z);
                                cueOn = true;
                                Cue(victimId, victimLocal, true, cr.species);
                            }
                            else if (t <= 0f) { ph = Ph.Retreat; t = 4f; }
                            break;
                        }
                    case Ph.Circle:
                        {
                            radius = Mathf.MoveTowards(radius, 4.5f, dt * 0.45f);
                            orbit += dt * orbitDir * Mathf.Max(0.5f, cr.cruise * 1.3f) / radius;
                            Vector3 tgt = head + new Vector3(Mathf.Sin(orbit), 0f, Mathf.Cos(orbit)) * radius;
                            tgt.y = head.y - 0.6f + Mathf.Sin(orbit * 1.5f) * 0.8f;
                            Vector3 tangent = new Vector3(Mathf.Cos(orbit), 0f, -Mathf.Sin(orbit)) * orbitDir;
                            cr.desiredVel = cr.Seek(tgt, Mathf.Max(cr.cruise * 1.6f, 1.2f), 2f) + tangent * Mathf.Max(cr.cruise, 0.6f);
                            cr.hasFaceDir = true;
                            cr.faceDir = Vector3.Lerp(tangent, (head - cr.pos).normalized, 0.25f);
                            cr.accelMul = 2f;
                            cr.turnMul = 2f;
                            cr.activityTarget = 0.65f;
                            startle -= dt;
                            if (startle <= 0f) { startle = 1f; FaunaReact.Startle(head, 9f, 1.5f); }
                            if (t <= 0f) { ph = Ph.Charge; t = 3.5f; }
                            break;
                        }
                    case Ph.Charge:
                        {
                            Vector3 to = head - cr.HeadPos;
                            float hd = to.magnitude;
                            float sp = Mathf.Clamp(cr.cruise * 3.5f, 4f, 7f);
                            cr.desiredVel = hd > 1e-3f ? to * (sp / hd) : cr.Transform.forward * sp;
                            cr.accelMul = 3.5f;
                            cr.turnMul = 2.8f;
                            cr.activityTarget = 1f;
                            if (hd < 1.5f + cr.lengthM * 0.08f)
                            {
                                float dmg = BiteDamage * (cr.species != null && cr.species.id == "barracuda" ? 0.45f : 1f);
                                Bite(victimId, victimLocal, cr.HeadPos, cr.species, dmg, BiteAir, BiteClipChance);
                                ph = Ph.Retreat;
                                t = 6f;
                                CueOff();
                            }
                            else if (t <= 0f)
                            {
                                Misses++;
                                ph = Ph.Retreat;
                                t = 5f;
                                CueOff();
                            }
                            break;
                        }
                    case Ph.Retreat:
                        {
                            Vector3 away = cr.pos - head;
                            away.y = Mathf.Min(away.y, 0f) - 0.3f;
                            if (away.sqrMagnitude < 1e-3f) away = -cr.Transform.forward;
                            cr.desiredVel = away.normalized * Mathf.Max(cr.cruise * (repelled ? 3f : 2f), repelled ? 3f : 1.8f);
                            cr.accelMul = 2.5f;
                            cr.activityTarget = repelled ? 1f : 0.6f;
                            if (t <= 0f)
                            {
                                done = true;
                                cr.director = null;
                            }
                            break;
                        }
                }
                return true;
            }
        }
    }
}
