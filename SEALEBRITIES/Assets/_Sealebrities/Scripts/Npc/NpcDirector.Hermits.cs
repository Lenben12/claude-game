using System;
using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „verstecktes Lager im Dschungel“, §20 Q2–Q8, Q47): the six hermits of the hidden camp.
    ///  • HOST: their day (work place → sunset greeting at the shrine → fire → hammock / tent; the lantern ritual at
    ///    the old jetty every 7th evening), the guard at the gap, moods (Hermits – gifts + the right signs raise it,
    ///    wrong signs annoy), shooing, attacks in a bad mood (slapstick, Combat.HurtLocal on the victim's machine),
    ///    lost fights (respect), the tie-up (the victim wriggles free, NpcInteract), teaching crafts, barter.
    ///  • every machine: the hit forwarding (NpcActor.OnHit), the victim side of a hit, morning gifts of the learned
    ///    crafts (fish trap, fruit harvest – each player's own backpack).
    public sealed partial class NpcDirector
    {
        sealed class HermitRt
        {
            public NpcActor a;
            public HermitDef def;
            public int index;
            public float stamina = Hermits.Stamina;
            /// Fight: the player id he chases (−1 none), until when, swing timers.
            public int fightTarget = -1;
            public float fightUntil, swingT = -1f, swingCd, warnedAt = -100f, shooAt = -100f, shooLineAt = -100f;
            public float calmUntil, dazedUntil, thanksUntil, gestureAt;
            /// Tie-up: stands next to this player (−1 none) until freed.
            public int guardOver = -1;
            public bool teaching, greeted;
            public int lastFoodDay = -1;
        }

        readonly HermitRt[] hermitRt = new HermitRt[NpcDatabase.Hermits.Length];
        int koFrame = -1, lastCraftDay = -1;

        /// Automated runs (tests, screenshots) walk through the camp a lot: a furious hermit only starts a fight on his
        /// own when a test allows it (hits by the player always start one).
        public static bool TestFightsAllowed;

        void BindHermits()
        {
            for (int i = 0; i < NpcDatabase.Hermits.Length; i++)
                hermitRt[i] = new HermitRt { a = Actor(NpcDatabase.Hermits[i]), def = HermitDatabase.Get(NpcDatabase.Hermits[i]), index = i };
        }

        void ResetHermitSession()
        {
            for (int i = 0; i < hermitRt.Length; i++)
            {
                var r = hermitRt[i];
                if (r == null) continue;
                var a = r.a; var d = r.def;
                hermitRt[i] = new HermitRt { a = a, def = d, index = i };
            }
            koFrame = -1;
            seatsReady = lanternReady = false;
            if (PlayerController.Restrained) PlayerController.Restrained = false;
        }

        HermitRt Rt(NpcActor a)
        {
            if (a == null) return null;
            for (int i = 0; i < hermitRt.Length; i++) if (hermitRt[i] != null && hermitRt[i].a == a) return hermitRt[i];
            return null;
        }

        static bool CampBuilt => QuestSpots.IsRegistered("hermit_camp");
        static Vector3 CampCenter => QuestSpots.Get("hermit_camp");
        const float CampZone = WorldLayout.HermitCampRadius + 14f;

        // ================================================================ HOST: the hermits' day
        void ThinkHermits()
        {
            if (!CampBuilt || S == null)
            {
                for (int i = 0; i < hermitRt.Length; i++) if (hermitRt[i] != null && hermitRt[i].a != null) hermitRt[i].a.SetPresent(false);
                return;
            }
            float now = Time.time;
            float h = Hour;
            var s = S;
            bool lantern = StoryRules.IsLanternEvening(s.day, s.timeOfDay);
            if (lantern && AnyoneNear(QuestSpots.Get("v_jetty"), 35f) && !StoryFlags.Has(NpcKeys.RitualSeen)) StoryFlags.Set(NpcKeys.RitualSeen);
            bool race = RaceRunning;
            Vector3 camp = CampCenter;
            for (int i = 0; i < hermitRt.Length; i++)
            {
                var rt = hermitRt[i];
                if (rt == null || rt.a == null || rt.def == null) continue;
                var a = rt.a;
                string id = rt.def.id;
                int mood = Hermits.Mood(id);
                int band = Hermits.Band(mood);
                // replicated band flags (prompts / UI on every machine)
                byte f = (byte)(a.Flags & ~(NpcActor.FlagHostile | NpcActor.FlagWary | NpcActor.FlagFriendly));
                if (rt.fightTarget >= 0) f |= NpcActor.FlagHostile;
                else if (band <= 1) f |= NpcActor.FlagWary;
                else if (band == 3) f |= NpcActor.FlagFriendly;
                a.Flags = f;

                // the canoe race (M3): the hunter and the gardener paddle
                if (race && (id == NpcDatabase.HHunter || id == NpcDatabase.HGardener)) { a.SetPresent(false); continue; }

                if (now < rt.dazedUntil) { a.SetPresent(true); a.Stop(); a.Motion = NpcMotion.Dazed; continue; }
                if (a.Motion == NpcMotion.Dazed) a.Motion = NpcMotion.Idle;
                if (rt.fightTarget >= 0 && FightTick(rt, now)) continue;
                if (rt.guardOver >= 0)
                {
                    a.SetPresent(true);
                    Vector3 v = PlayerPos(rt.guardOver);
                    if (NpcUtil.FlatDist(a.Position, v) > 2.2f) { if (!a.Moving) a.GoTo(v + (a.Position - v).normalized * 1.6f, 2.4f); }
                    else { a.Stop(); a.Motion = NpcMotion.ArmsCrossed; a.FacePoint = v; }
                    continue;
                }
                if (IsTalking(a) || a.HasOffer)
                {
                    a.Stop();
                    a.Motion = rt.teaching ? (id == NpcDatabase.HCook ? NpcMotion.Cook : NpcMotion.Carve) : NpcMotion.Idle;
                    a.FacePoint = NearestDiverPos(a.Position);
                    continue;
                }

                // ---- where he belongs right now
                string spotId; NpcMotion motion; Vector3? face = null; bool lie = false;
                if (lantern) { spotId = null; motion = NpcMotion.Idle; }
                else if (Between(h, 6.5f, 18f)) { spotId = rt.def.work; motion = rt.def.workMotion; }
                else if (Between(h, 18f, 19f)) { spotId = "hermit_spawn_" + i; motion = NpcMotion.Idle; face = QuestSpots.Get("hermit_shrine"); }
                else if (Between(h, 19f, 22.5f)) { spotId = "@fire"; motion = NpcMotion.Sit; face = FireCenter(); }
                else
                {
                    spotId = rt.def.hammock;
                    motion = NpcMotion.Lie;
                    lie = spotId != null;
                    if (!lie)
                    {
                        // sleeps in a tent: out of sight
                        if (a.Present && !AnyoneNear(a.Position, 25f)) a.SetPresent(false);
                        else if (a.Present) { string tent = "hermit_tent_" + Mathf.Min(2, i % 3); if (!a.Moving) a.GoTo(QuestSpots.Get(tent), 1.4f, () => a.SetPresent(false)); }
                        continue;
                    }
                }

                // the guard: strangers near the gap while the camp has not accepted the crew
                if (id == NpcDatabase.HHunter && !lie && Between(h, 6.5f, 19f) && band <= 1)
                {
                    var intr = NearestDiverOnLand(camp, CampZone + 6f);
                    if (intr.HasValue)
                    {
                        Vector3 g0 = QuestSpots.Get("hermit_guard_0"), g1 = QuestSpots.Get("hermit_guard_1");
                        spotId = NpcUtil.FlatDistSq(g0, intr.Value) < NpcUtil.FlatDistSq(g1, intr.Value) ? "hermit_guard_0" : "hermit_guard_1";
                        motion = NpcMotion.ArmsCrossed;
                        face = intr;
                    }
                }

                Vector3 target;
                float yaw;
                if (lantern)
                {
                    target = LanternSpot(i);
                    face = QuestSpots.Get("v_lanterns");
                    yaw = 180f;
                }
                else if (spotId == "@fire")
                {
                    target = FireSeat(i);
                    yaw = 0f;
                }
                else if (lie)
                {
                    if (!SleepSpot(spotId, out target, out yaw)) { a.SetPresent(false); continue; }
                }
                else
                {
                    target = QuestSpots.Get(spotId);
                    yaw = PropInfo.SpotYaw(spotId);
                }
                if (!a.Present || !a.Placed) { a.SetPresent(true); if (lie) a.PlaceExact(target, yaw); else a.Place(target, yaw); }
                float dist = NpcUtil.FlatDist(a.Position, target);
                if (lie)
                {
                    if (dist > 0.5f && !AnyoneNear(a.Position, 20f)) { a.PlaceExact(target, yaw); }
                    else if (dist > 0.5f) { if (!a.Moving) a.GoTo(target, 1.4f, () => a.PlaceExact(target, yaw)); continue; }
                    a.Motion = NpcMotion.Lie;
                    a.FacePoint = null;
                    continue;
                }
                if (dist > (motion == NpcMotion.Sit ? 0.15f : 1.0f))
                {
                    // far away and nobody sees it: no cross-country walk through the jungle wall
                    if (dist > 25f && !AnyoneNear(a.Position, 40f) && !AnyoneNear(target, 40f)) a.Place(target, yaw);
                    else if (!a.Moving || NpcUtil.FlatDist(a.Destination, target) > 0.5f) a.GoTo(target, motion == NpcMotion.ArmsCrossed ? 2.8f : 1.5f);
                    continue;
                }
                if (a.Moving) continue;
                a.Motion = motion;
                if (face.HasValue) a.FacePoint = face;
                else a.FacePoint = PointFromYaw(a.Position, yaw);

                ReactToCrew(rt, band, now, lantern);
            }
            if (lantern) TickRitualLines(now);
        }

        static Vector3 PointFromYaw(Vector3 p, float yaw) => p + new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad)) * 4f;

        // ---- evening seats: two per log bench around the camp fire (PropCamp.FireRing: 3 logs, r 2.6 m, 35° + 120° steps)
        readonly Vector3[] fireSeats = new Vector3[6];
        readonly Vector3[] lanternSpots = new Vector3[6];
        bool seatsReady, lanternReady;

        Vector3 FireCenter() => PropInfo.Spot("hermit_fire", out var f) ? f : QuestSpots.Get("hermit_camp");

        Vector3 FireSeat(int i)
        {
            if (!seatsReady)
            {
                seatsReady = true;
                Vector3 fire = FireCenter();
                Vector3 F = WorldFeatures.CampEntranceDir; F.y = 0f;
                if (F.sqrMagnitude < 1e-4f) F = Vector3.forward;
                F.Normalize();
                Vector3 R = Vector3.Cross(Vector3.up, F).normalized;
                for (int k = 0; k < 6; k++)
                {
                    float a = (35f + (k % 3) * 120f) * Mathf.Deg2Rad;
                    Vector3 lp = fire + (F * Mathf.Cos(a) - R * Mathf.Sin(a)) * 2.6f;
                    Vector3 toFire = fire - lp; toFire.y = 0f; toFire.Normalize();
                    Vector3 tangent = Vector3.Cross(Vector3.up, toFire);
                    Vector3 p = lp + tangent * (k < 3 ? -0.5f : 0.5f);
                    p.y = WorldInfo.GroundHeight(p);
                    fireSeats[k] = p;
                }
            }
            return fireSeats[Mathf.Clamp(i, 0, 5)];
        }

        Vector3 LanternSpot(int i)
        {
            if (!lanternReady)
            {
                lanternReady = true;
                Vector3 jetty = QuestSpots.Get("v_jetty");
                for (int k = 0; k < 6; k++) lanternSpots[k] = QuestSpots.Land(jetty + new Vector3((k - 2.5f) * 1.3f, 0f, 1.5f + (k % 2) * 0.8f), 0.3f, 6f);
            }
            return lanternSpots[Mathf.Clamp(i, 0, 5)];
        }

        /// Root point + yaw of a hermit lying in a registered hammock (the pelvis on the cloth, head at the head end).
        static bool SleepSpot(string id, out Vector3 root, out float yaw)
        {
            root = Vector3.zero; yaw = 0f;
            var spots = PropInfo.SleepSpots;
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i].id != id) continue;
                Vector3 hip = spots[i].hip != Vector3.zero ? spots[i].hip : spots[i].lie;
                root = hip;
                // the body axis runs head → feet along `yaw`; NpcActor lies with the head toward its −Z
                yaw = spots[i].yaw;
                return true;
            }
            return false;
        }

        float ritualLineT;

        void TickRitualLines(float now)
        {
            if (now < ritualLineT) return;
            ritualLineT = now + 30f;
            var lead = hermitRt[0] != null ? hermitRt[0].a : null;
            if (lead == null || !lead.Present || !AnyoneNear(lead.Position, 18f) || IsTalking(lead)) return;
            lead.PlayGesture(NpcGesture.Bow);
            Talk(lead, "hm.ritual", null, lead.Id);
        }

        /// Wary hermits shoo, furious ones warn and then attack; everyone notices a stranger once (greeting stare).
        void ReactToCrew(HermitRt rt, int band, float now, bool lantern)
        {
            var a = rt.a;
            var near = NearestDiverOnLand(a.Position, 8f);
            if (!near.HasValue) return;
            float d = NpcUtil.FlatDist(a.Position, near.Value);
            a.FacePoint = near;
            if (a.Motion == NpcMotion.Mend || a.Motion == NpcMotion.Carve) { if (d < 3f) a.Motion = NpcMotion.Idle; }
            if (!rt.greeted)
            {
                rt.greeted = true;
                var hs = Hermits.Get(rt.def.id);
                if (hs == null || !hs.met)
                {
                    var st = Hermits.GetOrAdd(rt.def.id);
                    if (st != null) { st.met = true; if (Net.IsHost) NetState.ForceSnapshot(); }
                    if (!StoryFlags.Has(NpcKeys.HermitsMet)) StoryFlags.Set(NpcKeys.HermitsMet);
                    if (!IsTalking(a)) Talk(a, rt.def.id == NpcDatabase.HLookout ? "hm.ignore" : "hm.greet", null, a.Id);
                    return;
                }
            }
            if (lantern || now < rt.calmUntil) return;
            bool inCamp = NpcUtil.FlatDist(near.Value, CampCenter) < CampZone;
            if (band == 0 && inCamp)
            {
                if (d < 6f && now - rt.warnedAt > 20f)
                {
                    rt.warnedAt = now;
                    a.PlayGesture(NpcGesture.Shoo);
                    if (!IsTalking(a)) Talk(a, "hm.warn", null, a.Id);
                }
                else if (d < 4f && now - rt.warnedAt > 2.5f && now - rt.warnedAt < 20f && (!GameManager.AutomatedRun || TestFightsAllowed))
                    StartFight(rt, DiverPlayerId(near.Value), now);
                return;
            }
            if (band == 1 && d < 3.2f && now - rt.shooAt > 4f)
            {
                rt.shooAt = now;
                a.PlayGesture(rt.def.id == NpcDatabase.HLookout ? NpcGesture.Spyglass : NpcGesture.Shoo);
                if (now - rt.shooLineAt > 25f && !IsTalking(a))
                {
                    rt.shooLineAt = now;
                    Talk(a, rt.def.id == NpcDatabase.HLookout ? "hm.ignore" : (NpcUtil.Hash01((int)now, rt.index) < 0.5f ? "hm.shoo.1" : "hm.shoo.2"), null, a.Id);
                }
                return;
            }
            // idle flavour: the lookout raises the spyglass, the leader honours the sea now and then
            if (now - rt.gestureAt > 9f && d > 2f)
            {
                rt.gestureAt = now;
                if (rt.def.id == NpcDatabase.HLookout) a.PlayGesture(NpcGesture.Spyglass);
                else if (rt.def.id == NpcDatabase.HLeader && NpcUtil.Hash01((int)now, 7) < 0.5f) a.PlayGesture(NpcGesture.HandOnHeart);
            }
        }

        /// Player id of the diver standing at p (host: own id, else the closest avatar's).
        int DiverPlayerId(Vector3 p)
        {
            if (!Net.IsOnline) return Net.LocalId;
            if (NpcUtil.FlatDistSq(NpcUtil.LocalPlayerPos, p) < 1f) return Net.LocalId;
            var all = Divers.All;
            int best = Net.LocalId;
            float bd = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                var t = all[i];
                if (t == null) continue;
                float d = NpcUtil.FlatDistSq(t.position, p);
                if (d >= bd) continue;
                var na = t.GetComponentInParent<NetAvatar>();
                bd = d;
                best = na != null ? na.PlayerId : Net.LocalId;
            }
            return best;
        }

        // ================================================================ fights (HOST) – slapstick, never lethal (Q6/Q7)
        void StartFight(HermitRt rt, int playerId, float now)
        {
            if (rt.fightTarget >= 0 || now < rt.calmUntil || now < rt.dazedUntil) return;
            rt.fightTarget = playerId;
            rt.fightUntil = now + 25f;
            rt.swingCd = 0.6f;
            rt.swingT = -1f;
            rt.a.PlayGesture(NpcGesture.Swing);
            if (!IsTalking(rt.a)) Talk(rt.a, "hm.attack", null, rt.a.Id);
            if (!StoryFlags.Has(NpcKeys.HermitTied) && !StoryFlags.Has(NpcKeys.HermitRespect))
                NotifyPlayer(playerId, Loc.Tr("Ein Einsiedler greift an!", "A hermit attacks!"), Loc.Tr("Stock: LMT · Abwehr: RMT · „Frieden“-Zeichen", "Stick: LMB · block: RMB · 'Peace' sign"));
        }

        void EndFight(HermitRt rt, float calm)
        {
            rt.fightTarget = -1;
            rt.swingT = -1f;
            rt.calmUntil = Time.time + calm;
            rt.a.Stop();
        }

        /// True while he is busy fighting (chase + swing).
        bool FightTick(HermitRt rt, float now)
        {
            var a = rt.a;
            a.SetPresent(true);
            Vector3 v = PlayerPos(rt.fightTarget);
            bool targetGone = rt.fightTarget != Net.LocalId && Net.IsOnline && Net.Find(rt.fightTarget) == null;
            float d = NpcUtil.FlatDist(a.Position, v);
            bool wet = v.y < Waves.SeaLevel - 0.3f || WorldInfo.GroundHeight(v) < Waves.SeaLevel - 0.4f;
            if (targetGone || now > rt.fightUntil || d > 14f || wet || NpcUtil.FlatDist(v, CampCenter) > CampZone + 10f)
            {
                EndFight(rt, 30f);
                return false;
            }
            a.FacePoint = v;
            if (rt.swingT >= 0f)
            {
                rt.swingT -= 0.1f;
                if (rt.swingT < 0f && d < 2.3f) HurtPlayer(rt.fightTarget, 16f + NpcUtil.Hash01((int)(now * 10f), rt.index) * 8f, a);
                return true;
            }
            if (d > 1.7f)
            {
                if (!a.Moving || NpcUtil.FlatDist(a.Destination, v) > 0.8f) a.GoTo(v + (a.Position - v).normalized * 1.3f, 3.4f);
                return true;
            }
            a.Stop();
            a.Motion = NpcMotion.Idle;
            rt.swingCd -= 0.1f;
            if (rt.swingCd <= 0f)
            {
                rt.swingCd = 1.5f;
                rt.swingT = 0.45f;   // wind-up, then the bonk
                a.PlayGesture(NpcGesture.Swing);
            }
            return true;
        }

        /// HOST: a hermit's bonk lands on a player (the victim's machine applies it – Combat.HurtLocal).
        void HurtPlayer(int playerId, float amount, NpcActor from)
        {
            if (playerId == Net.LocalId || !Net.IsOnline) { LocalHermitHurt(from.Id, amount); return; }
            NpcNet.SendEvent(NpcNet.EvHurt, from.Id, NpcNet.F(amount), playerId);
        }

        void OnLocalKnockedOut() { koFrame = Time.frameCount; }

        /// Every machine (the victim): the hit lands; a knock-out by a hermit means: tied up (Q6).
        void LocalHermitHurt(string npcId, float amount)
        {
            var a = Actor(npcId);
            if (a == null || amount <= 0f || NpcInteract.TiedUp) return;
            Combat.HurtLocal(amount, a.Position, npcId);
            if (koFrame == Time.frameCount) NpcInteract.StartTieUp(npcId);
        }

        /// The local player's weapon hit an NPC (NpcActor.OnHit on the attacker's machine) → the host.
        public static void LocalHitNpc(NpcActor a, in HitInfo hit)
        {
            if (a == null) return;
            if (!NpcDatabase.IsHermit(a.Id))
            {
                // Horst & co. only complain: no wear, no fight hype
                Combat.HitRefused = true;
                NpcNet.Request("hit", a.Id, "0");
                return;
            }
            NpcNet.Request("hit", a.Id, NpcNet.F(Mathf.Max(1f, hit.damage)));
        }

        void HostHit(int fromId, NpcActor a, float damage)
        {
            if (a == null || !a.Present) return;
            var rt = Rt(a);
            float now = Time.time;
            if (rt == null)
            {
                a.PlayGesture(NpcGesture.Flinch);
                if (!IsTalking(a)) Talk(a, a.Id == NpcDatabase.Horst ? "horst.gag.ow" : "horst.gag.ow", null, a.Id);
                return;
            }
            string id = rt.def.id;
            if (now < rt.dazedUntil) { Hermits.AddMood(id, -5); return; }   // kicking somebody who is down: no
            a.PlayGesture(NpcGesture.Flinch);
            if (rt.fightTarget < 0)
            {
                // an unprovoked hit: a friend feels betrayed, everybody fights back
                Hermits.AddMood(id, Hermits.IsFriendly(id) ? -30 : -15);
                rt.calmUntil = 0f;
                rt.stamina = Hermits.Stamina;
                StartFight(rt, fromId, now);
            }
            rt.fightUntil = now + 25f;
            rt.stamina -= Mathf.Clamp(damage, 1f, 40f);
            if (rt.stamina > 0f) return;
            // ---- he lost: respect (Q7)
            rt.stamina = Hermits.Stamina;
            rt.fightTarget = -1;
            rt.swingT = -1f;
            rt.dazedUntil = now + 8f;
            rt.calmUntil = now + 180f;
            a.Stop();
            Hermits.AddRespect(id);
            int m = Hermits.Mood(id);
            Hermits.AddMood(id, Mathf.Max(Hermits.Friendly, m + 35) - m);
            for (int i = 0; i < hermitRt.Length; i++) if (hermitRt[i] != null && hermitRt[i] != rt) Hermits.AddMood(hermitRt[i].def.id, 5);
            Talk(a, "hm.win", null, a.Id);
            GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Respekt! ", "Respect! ") + a.Def.Name + Loc.Tr(" erkennt dich an", " acknowledges you"), Loc.Tr("Laune ↑", "Mood ↑"));
            var ss = StreamService.Instance;
            if (ss != null) ss.ReportFightWon(false);
            if (!StoryFlags.Has(NpcKeys.HermitRespect)) { StoryFlags.Set(NpcKeys.HermitRespect); RadioTo(fromId, NpcDatabase.Horst, "horst.respect"); }
        }

        void HostTied(int fromId, NpcActor a)
        {
            var rt = Rt(a);
            if (rt == null) return;
            EndFight(rt, 150f);
            rt.guardOver = fromId;
            Hermits.AddMood(rt.def.id, 8);
            a.PlayGesture(NpcGesture.Laugh);
            Talk(a, "hm.tied", null, a.Id);
            if (!StoryFlags.Has(NpcKeys.HermitTied)) { StoryFlags.Set(NpcKeys.HermitTied); RadioTo(fromId, NpcDatabase.Horst, "horst.tied"); }
        }

        void HostFreed(int fromId, NpcActor a)
        {
            var rt = Rt(a);
            if (rt == null) return;
            rt.guardOver = -1;
            rt.calmUntil = Time.time + 120f;
            Hermits.AddMood(rt.def.id, 4);
            a.PlayGesture(NpcGesture.Laugh);
            Talk(a, "hm.freed", null, a.Id);
        }

        /// A radio call on one player's machine (host: local, else through the notify event).
        void RadioTo(int playerId, string caller, string prefix)
        {
            if (playerId == Net.LocalId || !Net.IsOnline) Radio(caller, prefix, "net:" + prefix + ":" + NpcUtil.Day);
            else NpcNet.SendEvent(NpcNet.EvNotify, "radio:" + prefix, caller, playerId);
        }

        void NotifyPlayer(int playerId, string line, string chip)
        {
            if (playerId == Net.LocalId || !Net.IsOnline) GameEvents.RaiseNotify(NotifyKind.Warning, line, chip);
        }

        // ================================================================ signs (the gesture wheel, Q5)
        void HostSign(int fromId, NpcActor a, int signIndex, string goalLine, bool recording)
        {
            if (a == null || !a.Present || signIndex < 0 || signIndex >= HermitDatabase.SignCount) return;
            var sign = (HermitSign)signIndex;
            a.FacePoint = PlayerPos(fromId);
            var rt = Rt(a);
            if (rt != null) HermitSignReact(fromId, rt, sign);
            else SpeakerSign(fromId, a, sign, goalLine, recording);
        }

        void Wrong(HermitRt rt, int delta)
        {
            Hermits.AddMood(rt.def.id, -Mathf.Abs(delta));
            rt.a.PlayGesture(NpcGesture.Shoo);
            Talk(rt.a, "hm.sign.wrong", null, rt.a.Id);
        }

        void HermitSignReact(int fromId, HermitRt rt, HermitSign sign)
        {
            var a = rt.a;
            string id = rt.def.id;
            int band = Hermits.Band(Hermits.Mood(id));
            bool first = Hermits.FirstSignToday(id, (int)sign);
            float now = Time.time;
            if (!StoryFlags.Has(NpcKeys.HermitsMet)) StoryFlags.Set(NpcKeys.HermitsMet);
            if (IsTalking(a) && sign != HermitSign.Peace) return;
            switch (sign)
            {
                case HermitSign.Hello:
                    if (band == 0) { Wrong(rt, 6); return; }
                    if (first) Hermits.AddMood(id, 4);
                    a.PlayGesture(NpcGesture.Nod);
                    Talk(a, "hm.sign.hello", null, id);
                    return;
                case HermitSign.Sea:
                    // they revere the sea: always the right sign
                    Hermits.AddMood(id, first ? (id == NpcDatabase.HLeader ? 14 : 10) : 2);
                    a.PlayGesture(NpcGesture.HandOnHeart);
                    Talk(a, "hm.sign.sea", null, id);
                    return;
                case HermitSign.Peace:
                    if (rt.fightTarget >= 0 || band <= 1)
                    {
                        if (rt.fightTarget >= 0) EndFight(rt, 25f);
                        Hermits.AddMood(id, first ? 12 : 3);
                        a.PlayGesture(NpcGesture.Nod);
                        Talk(a, "hm.sign.peace", null, id);
                    }
                    else { Hermits.AddMood(id, 1); Talk(a, "hm.sign.ok", null, id); }
                    return;
                case HermitSign.Food:
                    if (band == 0) { Wrong(rt, 4); return; }
                    if (id == NpcDatabase.HCook)
                    {
                        if (rt.lastFoodDay != NpcUtil.Day)
                        {
                            rt.lastFoodDay = NpcUtil.Day;
                            Hermits.AddMood(id, 3);
                            a.PlayGesture(NpcGesture.Give);
                            Talk(a, "hm.food.cook", null, id);
                            GiveTo(fromId, StoryFlags.Has(NpcKeys.RecipeCurry) ? Survival.CurryId : FoodDatabase.GrilledFish, 1);
                            rt.thanksUntil = now + 60f;
                        }
                        else Talk(a, "hm.food.done", null, id);
                        return;
                    }
                    Hermits.AddMood(id, 1);
                    Talk(a, "hm.food.other", null, id);
                    return;
                case HermitSign.Trade:
                    // the barter picker opens on the player's machine when he is calm enough – this is the "too pushy" case
                    if (band <= 1) Wrong(rt, 6);
                    else Talk(a, "hm.sign.ok", null, id);
                    return;
                case HermitSign.Learn:
                    if (string.IsNullOrEmpty(rt.def.craftFlag)) { Talk(a, "hm.learn.none", null, id); return; }
                    if (StoryFlags.Has(rt.def.craftFlag)) { Talk(a, "hm.learn.known", null, id); return; }
                    if (band == 3 || Hermits.Respect(id) >= 1) { StartTeach(fromId, rt); return; }
                    Wrong(rt, 5);
                    Talk(a, "hm.learn.no", null, id);
                    return;
                case HermitSign.Hint:
                    if (band <= 1) { Wrong(rt, 3); return; }
                    GiveHint(a);
                    rt.thanksUntil = now + 60f;
                    return;
                case HermitSign.Gift:
                    Talk(a, "hm.sign.confused", null, id);
                    return;
                case HermitSign.Thanks:
                    if (now < rt.thanksUntil) { Hermits.AddMood(id, first ? 5 : 1); rt.thanksUntil = 0f; a.PlayGesture(NpcGesture.Nod); Talk(a, "hm.thanks", null, id); }
                    else Talk(a, "hm.sign.confused", null, id);
                    return;
                case HermitSign.Film:
                    // they distrust cameras – only friends pose
                    if (band == 3) { if (first) Hermits.AddMood(id, 2); a.PlayGesture(NpcGesture.Pose); Talk(a, "hm.film.yes", null, id); }
                    else { Hermits.AddMood(id, -12); a.PlayGesture(NpcGesture.Shoo); Talk(a, "hm.film.no", null, id); }
                    return;
            }
        }

        /// Puts items into one player's backpack (host: local, else an event to that player).
        void GiveTo(int playerId, string item, int n)
        {
            if (playerId == Net.LocalId || !Net.IsOnline) LocalReceive(item, n);
            else NpcNet.SendEvent(NpcNet.EvGive, item, NpcNet.I(n), playerId);
        }

        /// Every machine: an NPC handed the local player something.
        static void LocalReceive(string item, int n)
        {
            if (string.IsNullOrEmpty(item) || n <= 0 || ItemDatabase.Get(item) == null) return;
            int added = Backpack.Add(item, n);
            var d = ItemDatabase.Get(item);
            GameEvents.RaiseNotify(NotifyKind.Info, (d != null ? d.Name : item) + (n > 1 ? " ×" + n : ""), added < n ? Loc.Tr("Rucksack voll", "Backpack full") : Loc.Tr("im Rucksack", "in the backpack"));
        }

        // ---- teaching (Q8)
        void StartTeach(int fromId, HermitRt rt)
        {
            var a = rt.a;
            rt.teaching = true;
            a.PlayGesture(NpcGesture.Nod);
            if (!string.IsNullOrEmpty(rt.def.craftSpot))
            {
                Vector3 p = QuestSpots.Get(rt.def.craftSpot);
                if (NpcUtil.FlatDist(a.Position, p) < 12f) a.Place(p, PropInfo.SpotYaw(rt.def.craftSpot));
            }
            if (!Talk(a, "hm.teach." + rt.def.id, () => FinishTeach(rt), a.Id)) FinishTeach(rt);
        }

        void FinishTeach(HermitRt rt)
        {
            rt.teaching = false;
            rt.thanksUntil = Time.time + 60f;
            if (string.IsNullOrEmpty(rt.def.craftFlag) || StoryFlags.Has(rt.def.craftFlag)) return;
            StoryFlags.Set(rt.def.craftFlag);
            Hermits.AddMood(rt.def.id, 3);
            GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Gelernt: ", "Learned: ") + rt.def.Craft, CraftChip(rt.def.craftFlag));
        }

        static string CraftChip(string flag)
        {
            if (flag == StoryKeys.GrindLearned) return Loc.Tr("Schleifgerät in der Hütte", "Grinder in the hut");
            if (flag == NpcKeys.TrapLearned) return Loc.Tr("Jeden Morgen Fisch", "Fish every morning");
            if (flag == NpcKeys.RecipeCurry) return Loc.Tr("Fisch + Kokosnuss ans Feuer", "Fish + coconut on the fire");
            if (flag == NpcKeys.HarvestLearned) return Loc.Tr("Jeden Morgen Obst", "Fruit every morning");
            return null;
        }

        /// Every machine, new day: the learned crafts fill this player's own backpack (fish trap, fruit harvest).
        void LocalMorningCrafts(int day)
        {
            if (day == lastCraftDay || !InGame) return;
            lastCraftDay = day;
            if (StoryFlags.Has(NpcKeys.TrapLearned))
            {
                int n = Backpack.Add(ItemIds.RawFish, 2, false);
                if (n > 0) GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Fischfalle", "Fish trap"), "+" + n + " " + Loc.Tr("roher Fisch", "raw fish"));
            }
            if (StoryFlags.Has(NpcKeys.HarvestLearned))
            {
                int n = Backpack.Add("mango", 1, false) + Backpack.Add("banana", 1, false);
                if (n > 0) GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Ernte", "Harvest"), "+" + n + " " + Loc.Tr("Obst", "fruit"));
            }
        }

        // ================================================================ gifts (Q4) + barter (Q47)
        void HostGift(int fromId, NpcActor a, string item)
        {
            if (a == null || !a.Present || string.IsNullOrEmpty(item)) return;
            a.FacePoint = PlayerPos(fromId);
            var rt = Rt(a);
            if (rt == null) { SpeakerGift(fromId, a, item); return; }
            string id = rt.def.id;
            bool counts = Hermits.CountGift(id);
            if (!counts) { Hermits.AddMood(id, 1); Talk(a, "hm.gift.many", null, id); return; }
            if (HermitDatabase.Likes(rt.def, item)) { Hermits.AddMood(id, 18); a.PlayGesture(NpcGesture.Bow); Talk(a, "hm.gift.like", null, id); }
            else if (HermitDatabase.Dislikes(rt.def, item)) { Hermits.AddMood(id, -6); a.PlayGesture(NpcGesture.Shoo); Talk(a, "hm.gift.bad", null, id); }
            else { Hermits.AddMood(id, 8); a.PlayGesture(NpcGesture.Nod); Talk(a, "hm.gift.ok", null, id); }
            // a gift during a fight ends it (who bonks a friend with a mango?)
            if (rt.fightTarget >= 0 && !HermitDatabase.Dislikes(rt.def, item)) EndFight(rt, 30f);
        }

        void HostTrade(int fromId, NpcActor a, int index)
        {
            var rt = Rt(a);
            if (rt == null || rt.def.trades == null || index < 0 || index >= rt.def.trades.Length) return;
            var t = rt.def.trades[index];
            string id = rt.def.id;
            if (Hermits.CountTrade(id)) Hermits.AddMood(id, 3);
            a.PlayGesture(NpcGesture.Give);
            rt.thanksUntil = Time.time + 60f;
            switch (t.special)
            {
                case "hint": GiveHint(a); return;
                case "respect":
                    for (int i = 0; i < hermitRt.Length; i++) if (hermitRt[i] != null) Hermits.AddMood(hermitRt[i].def.id, 10);
                    Talk(a, "hm.accept", null, id);
                    return;
            }
            Talk(a, "hm.trade.ok", null, id);
        }

        /// Barter offers of a hermit the local player can afford right now (UI).
        public static bool CanTrade(HermitTrade t)
        {
            if (t == null) return false;
            if (t.give == HermitDatabase.AnyValuable) return CheapestValuable() != null;
            return Backpack.Count(t.give) >= t.giveN;
        }

        /// The cheapest treasure in the local pouch / backpack (null = none).
        public static string CheapestValuable()
        {
            string best = null;
            long bv = long.MaxValue;
            var lists = new[] { Backpack.Stacks, Backpack.Pouch };
            for (int k = 0; k < lists.Length; k++)
            {
                var l = lists[k];
                if (l == null) continue;
                for (int i = 0; i < l.Count; i++)
                {
                    var st = l[i];
                    if (st == null || st.count <= 0) continue;
                    var d = st.Def;
                    if (d == null || d.category != ItemCategory.Valuable) continue;
                    if (d.value < bv) { bv = d.value; best = d.id; }
                }
            }
            return best;
        }

        /// LOCAL: executes a barter offer (own backpack) and tells the host (mood, the hermit's reaction).
        public static bool LocalTrade(NpcActor a, int index)
        {
            var def = a != null ? HermitDatabase.Get(a.Id) : null;
            if (def == null || def.trades == null || index < 0 || index >= def.trades.Length) return false;
            if (Hermits.Mood(def.id) < Hermits.Grumpy) return false;
            var t = def.trades[index];
            if (!CanTrade(t)) { GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Dafür fehlt dir etwas", "You're missing something for that")); return false; }
            if (t.get != null && Backpack.SpaceFor(t.get) < t.getN) { GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Rucksack voll", "Backpack full")); return false; }
            string give = t.give == HermitDatabase.AnyValuable ? CheapestValuable() : t.give;
            if (give == null || !Backpack.Remove(give, t.giveN)) return false;
            if (t.get != null) Backpack.Add(t.get, t.getN, false);
            if (t.get2 != null) Backpack.Add(t.get2, t.get2N, false);
            string got = t.get != null ? (ItemDatabase.Get(t.get)?.Name ?? t.get) + (t.getN > 1 ? " ×" + t.getN : "") : t.special == "hint" ? Loc.Tr("ein Hinweis", "a hint") : Loc.Tr("Respekt", "respect");
            GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Tausch: ", "Trade: ") + got, "−" + (ItemDatabase.Get(give)?.Name ?? give));
            NpcNet.Request("trade", a.Id, NpcNet.I(index));
            return true;
        }

        /// LOCAL: gives one item of the backpack (or pouch treasure) to an NPC.
        public static bool LocalGift(NpcActor a, string item)
        {
            if (a == null || string.IsNullOrEmpty(item)) return false;
            if (!Backpack.Remove(item, 1)) return false;
            NpcNet.Request("gift", a.Id, item);
            return true;
        }

        // ================================================================ talking to a hermit (E)
        bool HermitTalk(int fromId, NpcActor a)
        {
            var rt = Rt(a);
            if (rt == null) return false;
            string id = rt.def.id;
            int band = Hermits.Band(Hermits.Mood(id));
            if (rt.fightTarget >= 0 || band == 0) { a.PlayGesture(NpcGesture.Shoo); Talk(a, "hm.warn", null, id); return true; }
            if (band == 1)
            {
                a.PlayGesture(id == NpcDatabase.HLookout ? NpcGesture.Spyglass : NpcGesture.Shoo);
                Talk(a, id == NpcDatabase.HLookout ? "hm.ignore" : Rotate("hm_shoo_" + id, "hm.shoo"), null, id);
                return true;
            }
            if (id == NpcDatabase.HLeader && band == 3 && !StoryFlags.Has(NpcKeys.HermitsAccepted))
            {
                a.PlayGesture(NpcGesture.HandOnHeart);
                Talk(a, "hm.story", () =>
                {
                    StoryFlags.Set(NpcKeys.HermitsAccepted);
                    for (int i = 0; i < hermitRt.Length; i++) if (hermitRt[i] != null) Hermits.Raise(hermitRt[i].def.id, Hermits.Grumpy + 5);
                    TalkMore(a, "hm.accept");
                }, id);
                return true;
            }
            if (OfferSideQuest(a)) return true;
            a.PlayGesture(NpcGesture.Nod);
            Talk(a, Rotate(id, "hm.talk." + id), null, id);
            return true;
        }

        // ================================================================ signs at NPCs who speak (Horst, Wanda, Dr. Fin, Toni, the brothers)
        void SpeakerSign(int fromId, NpcActor a, HermitSign sign, string goalLine, bool recording)
        {
            string id = a.Id;
            bool rival = id == NpcDatabase.Chad || id == NpcDatabase.Brad;
            switch (sign)
            {
                case HermitSign.Hello: OnTalkRequest(fromId, a); return;
                case HermitSign.Sea:
                    Talk(a, DialogueDatabase.Has(id + ".sea") ? id + ".sea" : rival ? "rival.sea" : "gen.sea", null, rival ? null : id);
                    return;
                case HermitSign.Peace: Talk(a, "gen.peace", null, id); return;
                case HermitSign.Food: Talk(a, id == NpcDatabase.Horst ? "horst.food" : "gen.food", null, id); return;
                case HermitSign.Trade: Talk(a, StoryFlags.Has(StoryKeys.HermitCampFound) ? "gen.trade.camp" : "gen.trade", null, id); return;
                case HermitSign.Learn:
                    if (string.IsNullOrEmpty(goalLine)) Talk(a, "gen.learn.none", null, id);
                    else Talk(a, "gen.learn", null, id, new[] { goalLine });
                    return;
                case HermitSign.Hint: GiveHint(a); return;
                case HermitSign.Gift: Talk(a, "gen.gift.ok", null, id); return;
                case HermitSign.Thanks: Talk(a, "gen.thanks", null, id); return;
                case HermitSign.Film: Interview(fromId, a, recording); return;
            }
        }

        /// „Interview“: the NPC talks into the camera; recorded → a small follower bonus once per NPC and day.
        void Interview(int fromId, NpcActor a, bool recording)
        {
            string id = a.Id;
            a.PlayGesture(NpcGesture.Pose);
            string line = DialogueDatabase.Has(id + ".film") ? id + ".film" : "gen.film";
            Talk(a, line, null, DialogueDatabase.Has(id + ".film") ? null : id);
            if (!recording) return;
            string flag = "interview:" + id + ":" + NpcUtil.Day;
            if (StoryFlags.Has(flag)) return;
            StoryFlags.Set(flag);
            var s = S;
            long gain = s != null ? (long)Mathf.Clamp(s.followers * 0.01f, 10f, 300f) : 10;
            GameManager.I.AddFollowers(gain);
            GameEvents.RaiseNotify(NotifyKind.Followers, Loc.Tr("Interview mit ", "Interview with ") + a.Def.Name, "+" + Loc.Number(gain));
            // record jobs that want an NPC on camera (none yet) read the same flag
        }

        /// Gifts to NPCs who speak: their favourites get a happy line + a tip.
        void SpeakerGift(int fromId, NpcActor a, string item)
        {
            string id = a.Id;
            bool like = false;
            switch (id)
            {
                case NpcDatabase.Horst: like = item == "fish_sandwich" || item == FoodDatabase.GrilledFish || item == Survival.CurryId || item == FoodDatabase.GrilledMeat; break;
                case NpcDatabase.Wanda: like = item == "krill_bar" || item == "seaweed_chips" || item == "shark_gummies" || item == "glow_soda" || item == "galley_coffee"; break;
                case NpcDatabase.Fin: var d = ItemDatabase.Get(item); like = d != null && d.category == ItemCategory.Valuable; break;
                case NpcDatabase.Toni: like = item == FoodDatabase.Coconut || item == FoodDatabase.GrilledFish; break;
                case NpcDatabase.Chad: case NpcDatabase.Brad: like = item == "glow_soda"; break;
            }
            a.PlayGesture(like ? NpcGesture.TaDa : NpcGesture.Nod);
            string likeLine = (id == NpcDatabase.Brad ? NpcDatabase.Chad : id) + ".gift.like";
            if (like && DialogueDatabase.Has(likeLine)) Talk(a, likeLine, null, id == NpcDatabase.Brad ? id : null);
            else Talk(a, like ? "gen.gift.like" : "gen.gift.ok", null, id);
            if (like) TalkMoreHint(a);
        }

        void TalkMoreHint(NpcActor a)
        {
            string line = NpcClues.Pick(a.Id, true);
            if (line != null) TalkMore(a, line, DialogueDatabase.SpeakerOf(line) == a.Id ? null : a.Id);
        }

        /// A clue from this NPC (map pieces, treasure, wrecks, chests, curiosities, Big Mo, the old secrets).
        void GiveHint(NpcActor a)
        {
            string line = NpcClues.Pick(a.Id, true);
            if (line == null) line = NpcClues.NoneLine(a.Id);
            if (line == null) { Talk(a, NpcDatabase.IsHermit(a.Id) ? "hm.hint.no" : "hint.none.horst", null, a.Id); return; }
            string sp = DialogueDatabase.SpeakerOf(line);
            Talk(a, line, null, sp == a.Id ? null : a.Id);
        }

        // ================================================================ discovery + misc (every machine / host)
        void OnHermitFlag(string flag)
        {
            if (flag == StoryKeys.HermitCampFound)
            {
                Radio(NpcDatabase.Horst, "horst.camp", "horst.camp");
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Ein verstecktes Lager!", "A hidden camp!"), Loc.Tr("Gesten: ", "Signs: ") + GameInput.BindingLabel(GameAction.Gestures) + Loc.Tr(" halten", " hold"));
            }
            else if (flag == NpcKeys.HermitsAccepted)
                GameEvents.RaiseBanner(Loc.Tr("Die Einsiedler akzeptieren dich!", "The hermits accept you!"), new Color(0.56f, 0.7f, 0.42f));
        }

        // ================================================================ test hooks (AutoPlaytest / AutoPlaytestMP / AutoShot)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTestStatics() { TestFightsAllowed = false; FreezeSchedules = false; }

        /// HOST: a hermit attacks this player now (tests: the tie-up).
        public bool DebugStartFight(string hermitId, int playerId)
        {
            var rt = Rt(Actor(hermitId));
            if (rt == null) return false;
            rt.calmUntil = 0f;
            rt.dazedUntil = 0f;
            StartFight(rt, playerId, Time.time);
            return rt.fightTarget == playerId;
        }

        /// Fight / tie-up state of a hermit (tests): chasing player id (−1 none), dazed, standing guard over a tied player.
        public void DebugHermitState(string hermitId, out int fightTarget, out bool dazed, out int guardOver, out float stamina)
        {
            var rt = Rt(Actor(hermitId));
            fightTarget = rt != null ? rt.fightTarget : -1;
            dazed = rt != null && Time.time < rt.dazedUntil;
            guardOver = rt != null ? rt.guardOver : -1;
            stamina = rt != null ? rt.stamina : 0f;
        }

        /// Every machine: the morning gifts of the learned crafts for a given day (tests).
        public void DebugMorningCrafts(int day) => LocalMorningCrafts(day);

        /// HOST: the brothers copied this species today (tests of the sabotage).
        public static void DebugSteal(string species)
        {
            var s = S;
            if (s == null || string.IsNullOrEmpty(species)) return;
            StoryFlags.Set(NpcKeys.RivalStealPrefix + species + ":" + s.day.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (I != null) I.trackerDirty = true;
        }

        /// Marker of a hermit: only once the camp was found (the map must not give the camp away).
        int HermitMarker(NpcActor a)
        {
            if (!StoryFlags.Has(StoryKeys.HermitCampFound)) return 0;
            if (!Hermits.IsFriendly(a.Id)) return 0;
            var all = SideQuestDatabase.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].giver == a.Id && Available(all[i])) return 1;
            var def = HermitDatabase.Get(a.Id);
            return 0;
        }
    }
}
