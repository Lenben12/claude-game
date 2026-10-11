using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Visitors: Wanda (K.5, L.12 lines), Dr. Fin + RV Plankton + research (B.4, K.3), the Bottom brothers + Clout Nine (B.3).
    public sealed partial class NpcDirector
    {
        // ================================================================ Wanda (HOST)
        readonly HashSet<string> greetedDrops = new HashSet<string>();
        float wandaPadUntil;
        bool wandaPadSaid;

        void ResetVisitorSession()
        {
            greetedDrops.Clear();
            wandaPadUntil = 0f;
            wandaPadSaid = false;
            finState = 0;
            rivalVisit = RivalVisit.None;
            rivalT = 0f;
            r3SharksSpawned = false;
            geraldSecondsToday = 0f;
            geraldQualityOk = false;
            geraldDay = -1;
            finTalkIndex = -1;
        }

        void HostVisitorStart()
        {
            if (vehicles.Length > VCat) vehicles[VCat].Present = false;
            if (vehicles.Length > VYacht) vehicles[VYacht].Present = false;
            if (vehicles.Length > VSub) vehicles[VSub].Present = false;
        }

        void ThinkWanda()
        {
            var w = Actor(NpcDatabase.Wanda);
            if (w == null || prologueActive) return;
            var s = S;
            float h = Hour;
            // a dropped delivery she has not waited at yet → walk to the pad edge, stay 2 minutes
            string drop = NewestDrop(s);
            if (drop != null && !greetedDrops.Contains(drop) && Between(h, 6.5f, 20f))
            {
                greetedDrops.Add(drop);
                wandaPadUntil = Time.time + 120f;
                wandaPadSaid = false;
            }
            bool day = Between(h, 7f, 19f) || Time.time < wandaPadUntil;
            w.SetPresent(day);
            if (!day) return;
            if (IsTalking(w) || w.HasOffer) { w.Stop(); w.FacePoint = NearestDiverPos(w.Position); return; }

            Vector3 target = Time.time < wandaPadUntil ? QuestSpots.Get("wanda_padedge") : QuestSpots.Get("wanda_hangar");
            if (NpcUtil.FlatDist(w.Position, target) > 1f)
            {
                if (!w.Placed) w.Place(target, 0f);
                else if (!w.Moving || NpcUtil.FlatDist(w.Destination, target) > 0.5f) w.GoTo(target, 1.7f);
                return;
            }
            w.Motion = Time.time < wandaPadUntil ? NpcMotion.Idle : NpcMotion.Carve;
            w.FacePoint = NearestDiverPos(w.Position) ?? (Vector3?)WorldLayout.Helipad;

            if (Time.time < wandaPadUntil && !wandaPadSaid && AnyoneNear(w.Position, 20f))
            {
                wandaPadSaid = true;
                var seq = DialogueDatabase.Seq("wanda.pad");
                if (seq.Count > 0) Talk(w, seq[(int)(NpcUtil.Hash01(NpcUtil.Day, s != null ? s.deliveries.Count : 0, 3) * seq.Count) % seq.Count]);
                return;
            }
            if (!StoryFlags.Has(NpcKeys.WandaMet) && AnyoneNear(w.Position, 6f))
            {
                StoryFlags.Set(NpcKeys.WandaMet);
                w.PlayGesture(NpcGesture.Wave);
                Talk(w, "wanda.met");
                return;
            }
            if (!StoryFlags.Has(StoryKeys.WandaThirdDelivery) && s != null && s.ownedItems.Count >= 9 && AnyoneNear(w.Position, 8f))
            {
                StoryFlags.Set(StoryKeys.WandaThirdDelivery);
                Talk(w, "wanda.story");
            }
        }

        static string NewestDrop(GameState s)
        {
            if (s == null) return null;
            for (int i = s.deliveries.Count - 1; i >= 0; i--)
            {
                var d = s.deliveries[i];
                if (d != null && d.dropped) return d.itemId + "@" + d.arrivalDay + ":" + Mathf.RoundToInt(d.arrivalTime * 1440f);
            }
            return null;
        }

        /// The daily speech bubble of Wanda's portrait in the Chopper Shop app (L.12 – ui reads it).
        public static string WandaBubbleLine()
        {
            var seq = DialogueDatabase.Seq("wanda.bubble");
            if (seq.Count == 0) return null;
            int i = (int)(NpcUtil.Hash01(NpcUtil.Day, 77) * seq.Count) % seq.Count;
            return DialogueDatabase.Resolve(seq[i], null);
        }

        void OnMishap(string itemId, DeliveryMishap m)
        {
            if (!Net.HasAuthority || !InGame) return;
            string pre = null;
            switch (m)
            {
                case DeliveryMishap.Water: pre = "wanda.mishap.water"; break;
                case DeliveryMishap.HutRoof: pre = "wanda.mishap.roof"; break;
                case DeliveryMishap.Palm: pre = "wanda.mishap.palm"; break;
                case DeliveryMishap.WrongPlace: pre = "wanda.mishap.wrong"; break;
                case DeliveryMishap.Village: pre = "wanda.mishap.village"; break;
                case DeliveryMishap.Goat: pre = "wanda.mishap.goat"; break;
                case DeliveryMishap.Boat: pre = "wanda.mishap.boat"; break;
            }
            if (pre != null) RadioAll(NpcDatabase.Wanda, pre);
        }

        void OnDeliveryReceived(GearItem item)
        {
            if (!Net.HasAuthority || !InGame) return;
            if (!StoryFlags.Has(NpcKeys.FirstDelivery)) StoryFlags.Set(NpcKeys.FirstDelivery);
        }

        /// W1 reward: the next two express orders refund their surcharge.
        void OnDeliveryOrdered(GearItem item)
        {
            if (!Net.HasAuthority || !InGame || item == null) return;
            string flag = StoryFlags.Has(NpcKeys.FreeExpress1) ? NpcKeys.FreeExpress1 : StoryFlags.Has(NpcKeys.FreeExpress2) ? NpcKeys.FreeExpress2 : null;
            if (flag == null) return;
            var d = Shop.FindDelivery(item.id);
            if (d == null || !d.express) return;
            long refund = Shop.Price(item, true) - Shop.Price(item, false);
            if (refund <= 0) return;
            StoryFlags.Clear(flag);
            Pay(refund);
            GameEvents.RaiseNotify(NotifyKind.Delivery, Loc.Tr("Gratis-Express von Wanda", "Free express from Wanda"), "+" + Loc.Money(refund));
        }

        // ================================================================ Dr. Fin (HOST)
        int finState;          // 0 away, 1 arriving, 2 present, 3 leaving
        int finTalkIndex = -1;

        void ThinkFin()
        {
            var fin = Actor(NpcDatabase.Fin);
            var rv = vehicles.Length > VCat ? vehicles[VCat] : null;
            if (fin == null || rv == null) return;
            var s = S;
            if (s == null) return;
            float h = Hour;
            bool visitDay = s.followers >= 300 && s.day % 3 == 0;
            bool window = visitDay && h >= 8.5f && h < 13f;
            switch (finState)
            {
                case 0:
                    fin.SetPresent(false);
                    if (window)
                    {
                        rv.Place(QuestSpots.Get("sea_entry"), 270f);
                        rv.Present = true;
                        rv.SailTo(QuestSpots.Get("berth_a"), 90f, h > 8.6f ? 3f : 30f);
                        finState = 1;
                    }
                    else rv.Present = false;
                    break;
                case 1:
                    if (!rv.Moving)
                    {
                        fin.SetPresent(true);
                        fin.Place(QuestSpots.Get("fin_stand"), 180f);
                        finState = 2;
                    }
                    break;
                case 2:
                    if (!window && !IsTalking(fin))
                    {
                        fin.SetPresent(false);
                        rv.SailTo(QuestSpots.Get("sea_entry"), 90f, 30f);
                        finState = 3;
                        break;
                    }
                    if (IsTalking(fin)) { fin.FacePoint = NearestDiverPos(fin.Position); break; }
                    fin.Motion = NpcMotion.Idle;
                    fin.FacePoint = NearestDiverPos(fin.Position) ?? (Vector3?)rv.Pos;
                    if (!StoryFlags.Has(NpcKeys.FinMet) && AnyoneNear(fin.Position, 6f))
                    {
                        fin.PlayGesture(NpcGesture.Wave);
                        Talk(fin, "fin.first", () => { StoryFlags.Set(NpcKeys.FinMet); EnsureResearchOffers(true); });
                    }
                    else if (NpcUtil.Hash01((int)(Time.time / 9f), 5) < 0.02f) fin.PlayGesture(NpcGesture.Think);
                    break;
                case 3:
                    if (!rv.Moving) { rv.Present = false; finState = 0; }
                    break;
            }
        }

        string FinSmallTalk()
        {
            var seq = DialogueDatabase.Seq("fin.talk");
            if (seq.Count == 0) return null;
            finTalkIndex = (finTalkIndex + 1) % seq.Count;
            return seq[finTalkIndex];
        }

        // ---- research offers (B.4: 3 open, a new one replaces a finished one next morning, the rod chain always among them)
        /// Offered = an entry in GameState.research that is not done (UIAppFin lists exactly these).
        public static bool IsResearchOpen(string id) { var r = Research.Get(id); return r != null && !r.done; }
        /// Progress counter of a research task (photos, fish, samples, sightings, seconds).
        public static int ResearchProgress(string id) => Research.ProgressOf(id);

        /// HOST: offers a research task (creates its entry with progress 0 – shared state, authority only).
        static void OfferResearch(string id)
        {
            var s = S;
            if (s == null || !Net.HasAuthority || Research.Get(id) != null) return;
            s.research.Add(new ResearchState { id = id });
            GameEvents.RaiseResearchChanged(id);
        }

        void EnsureResearchOffers(bool announce)
        {
            if (!Net.HasAuthority || !StoryFlags.Has(NpcKeys.FinMet)) return;
            var all = ResearchDatabase.All;
            int open = 0;
            for (int i = 0; i < all.Length; i++) if (IsResearchOpen(all[i].id)) open++;
            bool added = false;
            // the rod chain: the first unfinished R step is always open
            for (int r = 1; r <= 4; r++)
            {
                var def = RodStep(r);
                if (def == null || Research.IsDone(def.id)) continue;
                if (!IsResearchOpen(def.id) && open < ResearchDatabase.MaxOpen) { OfferResearch(def.id); open++; added = true; }
                break;
            }
            // fill from the pool in a per-save deterministic order
            int n = all.Length;
            for (int k = 0; k < n && open < ResearchDatabase.MaxOpen; k++)
            {
                int best = -1;
                float bestKey = 2f;
                for (int i = 0; i < n; i++)
                {
                    var d = all[i];
                    if (d.rod > 0 || Research.IsDone(d.id) || IsResearchOpen(d.id)) continue;
                    if (!string.IsNullOrEmpty(d.needSite) && !Progression.IsSiteUnlocked(d.needSite)) continue;
                    float key = NpcUtil.Hash01(NpcUtil.StableHash(d.id), 911);
                    if (key < bestKey) { bestKey = key; best = i; }
                }
                if (best < 0) break;
                OfferResearch(all[best].id);
                open++;
                added = true;
            }
            if (added && announce) Notify(NotifyKind.Quest, "Dr. Fin: neue Forschung", "Dr. Fin: new research", Loc.Tr("App „Dr. Fin“", "'Dr. Fin' app"));
            trackerDirty = true;
        }

        static ResearchDef RodStep(int r)
        {
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++) if (all[i].rod == r) return all[i];
            return null;
        }

        /// HOST: adds research progress; completes (and pays) at the target count.
        void ResearchAdvance(ResearchDef d, int amount, int setTo = -1)
        {
            if (d == null || !IsResearchOpen(d.id)) return;
            int prog = setTo >= 0 ? setTo : ResearchProgress(d.id) + amount;
            if (prog >= d.count) { CompleteResearch(d); return; }
            Research.SetProgress(d.id, prog);
            trackerDirty = true;
        }

        void CompleteResearch(ResearchDef d)
        {
            Research.Complete(d.id);
            Pay(d.money);
            var s = S;
            switch (d.rod)
            {
                case 1: StoryFlags.Set(StoryKeys.RodT1Discount); break;
                case 4:
                    if (s != null && !s.ownedItems.Contains("rod_t3"))
                    {
                        s.ownedItems.Add("rod_t3");
                        GameEvents.RaiseGearChanged();
                    }
                    break;
            }
            string chip = d.money > 0 ? "+" + Loc.Money(d.money) : d.rod == 4 ? Loc.Tr("Forschungsrute", "Research rod") : null;
            GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Forschung erledigt", "Research done"), chip);
            var fin = Actor(NpcDatabase.Fin);
            if (fin != null && fin.Present && AnyoneNear(fin.Position, 25f))
                Talk(fin, d.rod == 1 ? "fin.r1.done" : d.rod == 4 ? "fin.r4.done" : "fin.done");
            trackerDirty = true;
        }

        // ================================================================ the Bottom brothers (HOST)
        enum RivalVisit { None, Arriving, Docked, Leaving, Wreck, WreckLeaving }
        RivalVisit rivalVisit;
        string rivalPurpose;
        float rivalT;
        bool rivalSpoke, r3SharksSpawned;
        int rivalVisitDay = -1;
        float geraldSecondsToday;
        bool geraldQualityOk;
        int geraldDay = -1;

        /// HOST, new day: weekly duel, R1–R4 triggers, extra visits.
        void RivalsNewDay()
        {
            var s = S;
            if (s == null) return;
            // weekly follower duel (B.3): every Monday (day % 7 == 1)
            if (StoryFlags.Has(NpcKeys.RivalR1Done) && s.day % 7 == 1 && s.lastDuelDay != s.day)
            {
                s.lastDuelDay = s.day;
                int c = s.followerHistory.Count;
                long now = c > 0 ? s.followerHistory[c - 1] : s.followers;
                long before = c > 7 ? s.followerHistory[c - 8] : (c > 0 ? s.followerHistory[0] : 0);
                long gain = Math.Max(0, now - before);
                // the brothers' weekly gain follows yours (they copy everything) – ×0.75 … ×1.3, deterministic per week
                float factor = 0.75f + NpcUtil.Hash01(s.day, 31) * 0.55f;
                long rivalGain = (long)(gain * factor);
                bool won = StoryFlags.Has("r2_week") || (gain > 0 && gain > rivalGain);
                StoryFlags.Clear("r2_week");
                if (won)
                {
                    s.duelWonUntilDay = s.day + 6;
                    // one ribbon with the reward in its band (host and guests alike)
                    GameEvents.RaiseBanner(Loc.Tr("Duell gewonnen!\n+5 % Aufrufe", "Duel won!\n+5 % views"), new Color(0.95f, 0.79f, 0.3f));
                    NpcNet.SendEvent(NpcNet.EvNotify, "banner:duel", null);
                }
                else GameEvents.RaiseNotify(NotifyKind.Story, DialogueDatabase.Resolve("rival.duel.lost", null));
            }
            if (s.followers >= Progression.LivestreamFollowers && !StoryFlags.Has(NpcKeys.RivalR1Done)) ScheduleRivals("R1");
            else if (s.followers >= 25000 && StoryFlags.Has(NpcKeys.RivalR1Done) && !StoryFlags.Has(NpcKeys.RivalR2Started))
            {
                StoryFlags.Set(NpcKeys.RivalR2Started);
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Bottom Feeders: „Gerald ist CGI!“", "Bottom Feeders: 'Gerald is CGI!'"));
                ScheduleRivals("R2");
            }
            else if (s.followers >= 100000 && StoryFlags.Has(NpcKeys.RivalR2Started) && !StoryFlags.Has(NpcKeys.RivalR3Started))
            {
                StoryFlags.Set(NpcKeys.RivalR3Started);
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Köder-Skandal am Wrack!", "Chum scandal at the wreck!"));
                rivalVisit = RivalVisit.None;
                StartWreckVisit();
            }
            else if (s.followers >= 500000 && StoryFlags.Has(NpcKeys.RivalR3Started) && !StoryFlags.Has(NpcKeys.RivalR4Started))
            {
                StoryFlags.Set(NpcKeys.RivalR4Started);
                StoryFlags.Set("rival_r4_day:" + s.day);
                RadioAll(NpcDatabase.Brad, "rival.r4");
                objectsDirty = true;
            }
            else if (StoryFlags.Has(NpcKeys.RivalR4Started) && !StoryFlags.Has(NpcKeys.RivalR4Rescued) && !StoryFlags.Has(NpcKeys.RivalR4Ignored) && R4StartDay(s) >= 0 && s.day > R4StartDay(s))
            {
                // ignored: Dr. Fin rescued them – they heckle in the finale
                StoryFlags.Set(NpcKeys.RivalR4Ignored);
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Dr. Fin hat die Brüder gerettet", "Dr. Fin rescued the brothers"));
                objectsDirty = true;
            }
            else if (StoryFlags.Has(NpcKeys.RivalR2Started) && rivalVisit == RivalVisit.None)
            {
                bool b1Back = Quests.IsActive("B1") && Quests.Get("B1") != null && Quests.Get("B1").step >= 1;
                if (b1Back || NpcUtil.Hash01(s.day, 41) < 0.10f) ScheduleRivals(b1Back ? "B1" : "EXTRA");
            }
        }

        static int R4StartDay(GameState s)
        {
            for (int i = 0; i < s.storyFlags.Count; i++)
            {
                string f = s.storyFlags[i];
                if (f != null && f.StartsWith("rival_r4_day:", StringComparison.Ordinal) && int.TryParse(f.Substring(13), out int d)) return d;
            }
            return -1;
        }

        void ScheduleRivals(string purpose)
        {
            rivalPurpose = purpose;
            rivalVisitDay = NpcUtil.Day;
            rivalVisit = RivalVisit.None;
            rivalSpoke = false;
        }

        void ThinkRivals()
        {
            var chad = Actor(NpcDatabase.Chad);
            var brad = Actor(NpcDatabase.Brad);
            var yacht = vehicles.Length > VYacht ? vehicles[VYacht] : null;
            if (chad == null || brad == null || yacht == null) return;
            // R4: the gold mini sub „Bottom Line“ stuck on the trench ledge at 85 m until rescued / ignored
            var sub = vehicles.Length > VSub ? vehicles[VSub] : null;
            if (sub != null)
            {
                bool r4 = StoryFlags.Has(NpcKeys.RivalR4Started) && !StoryFlags.Has(NpcKeys.RivalR4Rescued) && !StoryFlags.Has(NpcKeys.RivalR4Ignored);
                if (r4 && !sub.Present) { sub.Place(QuestSpots.Get("r4_sub"), 40f); sub.Present = true; }
                else if (!r4 && sub.Present) sub.Present = false;
            }
            float h = Hour;
            rivalT += 0.1f;
            switch (rivalVisit)
            {
                case RivalVisit.None:
                {
                    chad.SetPresent(false);
                    brad.SetPresent(false);
                    yacht.Present = false;
                    if (rivalPurpose == null || rivalVisitDay != NpcUtil.Day) break;
                    float from = rivalPurpose == "R1" ? 9f : 11f, to = rivalPurpose == "R1" ? 13f : 15f;
                    if (h >= from && h < to)
                    {
                        yacht.Place(QuestSpots.Get("sea_entry"), 270f);
                        yacht.Present = true;
                        yacht.SailTo(QuestSpots.Get("berth_b"), 90f, 32f);
                        rivalVisit = RivalVisit.Arriving;
                        rivalT = 0f;
                    }
                    break;
                }
                case RivalVisit.Arriving:
                    if (!yacht.Moving)
                    {
                        chad.SetPresent(true);
                        brad.SetPresent(true);
                        chad.Place(QuestSpots.Get("rival_stand"), 0f);
                        brad.Place(QuestSpots.Get("rival_stand2"), 0f);
                        chad.Motion = NpcMotion.Idle;
                        brad.Motion = NpcMotion.Idle;
                        rivalVisit = RivalVisit.Docked;
                        rivalT = 0f;
                    }
                    break;
                case RivalVisit.Docked:
                {
                    Vector3? look = NearestDiverPos(chad.Position);
                    chad.FacePoint = look;
                    brad.FacePoint = look;
                    if (!rivalSpoke && AnyoneNear(chad.Position, 9f) && !IsTalking(chad))
                    {
                        rivalSpoke = true;
                        chad.PlayGesture(NpcGesture.Selfie);
                        brad.PlayGesture(NpcGesture.Wave);
                        switch (rivalPurpose)
                        {
                            case "R1":
                                Talk(chad, "rival.r1", () =>
                                {
                                    StoryFlags.Set(NpcKeys.RivalR1Done);
                                    rivalT = 0f;
                                });
                                break;
                            case "R2":
                                Talk(chad, "rival.r2", () =>
                                {
                                    var horst = Actor(NpcDatabase.Horst);
                                    if (horst != null && horst.Present && AnyoneNear(horst.Position, 25f)) Talk(horst, "horst.r2after");
                                    rivalT = 0f;
                                });
                                break;
                            case "B1":
                                rivalT = 0f;
                                break;
                            default:
                                Talk(chad, NpcUtil.Hash01(NpcUtil.Day, 3) < 0.5f ? "rival.visit.1" : "rival.visit.3");
                                TalkMore(chad, NpcUtil.Hash01(NpcUtil.Day, 4) < 0.5f ? "rival.visit.2" : "rival.visit.4");
                                TalkMore(chad, "rival.visit.5");
                                break;
                        }
                    }
                    // stay ~2 minutes after the talk (offers: B1), then cast off
                    bool timeUp = rivalSpoke ? rivalT > 120f && !IsTalking(chad) && !IsTalking(brad) && !brad.HasOffer && !chad.HasOffer : rivalT > 600f || Hour > 17f;
                    if (timeUp)
                    {
                        chad.SetPresent(false);
                        brad.SetPresent(false);
                        yacht.SailTo(QuestSpots.Get("sea_entry"), 90f, 30f);
                        rivalVisit = RivalVisit.Leaving;
                        rivalPurpose = null;
                    }
                    break;
                }
                case RivalVisit.Leaving:
                    if (!yacht.Moving) { yacht.Present = false; rivalVisit = RivalVisit.None; }
                    break;
                case RivalVisit.Wreck:
                    ThinkWreckVisit(chad, brad, yacht);
                    break;
                case RivalVisit.WreckLeaving:
                    if (!yacht.Moving) { yacht.Present = false; rivalVisit = RivalVisit.None; }
                    break;
            }
        }

        // ---- R3: the Clout Nine anchors at the wreck and dumps chum
        void StartWreckVisit()
        {
            var yacht = vehicles[VYacht];
            yacht.Place(QuestSpots.Get("wreck_anchor"), 120f);
            yacht.Present = true;
            rivalVisit = RivalVisit.Wreck;
            rivalT = 0f;
            r3SharksSpawned = false;
            rivalSpoke = false;
        }

        public bool ChumActive => rivalVisit == RivalVisit.Wreck && StoryFlags.Has(NpcKeys.RivalR3Started) && !StoryFlags.Has(NpcKeys.RivalR3Done);

        void ThinkWreckVisit(NpcActor chad, NpcActor brad, NpcVehicle yacht)
        {
            Vector3 deck = yacht.Pos + Quaternion.Euler(0f, yacht.Yaw, 0f) * new Vector3(0.8f, 0f, -5f);
            chad.SetPresent(true);
            brad.SetPresent(true);
            if (NpcUtil.FlatDist(chad.Position, deck) > 2f) { chad.Place(deck, yacht.Yaw + 90f); brad.Place(deck + Quaternion.Euler(0f, yacht.Yaw, 0f) * new Vector3(-1.4f, 0f, 0.6f), yacht.Yaw + 90f); }
            chad.Motion = NpcMotion.Idle;
            brad.Motion = NpcMotion.Idle;
            if (!r3SharksSpawned && AnyoneNear(yacht.Pos, 150f) && Refs.Spawner != null)
            {
                r3SharksSpawned = true;
                var shark = SpeciesDatabase.Get("hammerhead");
                if (shark != null)
                    for (int i = 0; i < 3; i++)
                    {
                        Vector3 p = yacht.Pos + new Vector3(Mathf.Cos(i * 2.1f) * 25f, -14f - i * 3f, Mathf.Sin(i * 2.1f) * 25f);
                        try { Refs.Spawner.Spawn(shark, p, Mathf.Lerp(shark.minCm, shark.maxCm, 0.4f + i * 0.15f), false); }
                        catch (Exception e) { Debug.LogWarning("[Npc] chum sharks: " + e.Message); break; }
                    }
            }
            if (!rivalSpoke && AnyoneNear(yacht.Pos, 45f))
            {
                rivalSpoke = true;
                Talk(chad, "rival.r3", null, null, null, 60f);
            }
            if (StoryFlags.Has(NpcKeys.RivalR3Done) || Hour > 18f || rivalT > 1800f)
            {
                chad.SetPresent(false);
                brad.SetPresent(false);
                yacht.SailTo(yacht.Pos + new Vector3(300f, 0f, -200f), yacht.Yaw, 40f);
                rivalVisit = RivalVisit.WreckLeaving;
            }
        }

        /// HOST: R3 exposed by a photo of the yacht while the chum is in the water.
        void RivalR3Exposed()
        {
            if (!ChumActive) return;
            StoryFlags.Set(NpcKeys.RivalR3Done);
            StoryFlags.Set("r3_bonus");
            Achievements.Unlock("bottomed_out");
            var chad = Actor(NpcDatabase.Chad);
            if (chad != null) Talk(chad, "rival.r3x", null, null, null, 80f);
            GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Bottom Feeders entlarvt!", "Bottom Feeders exposed!"), Loc.Tr("+40 % Aufrufe", "+40 % views"));
            trackerDirty = true;
        }

        /// HOST: R4 rescue (E on the sub's valve, needs 30 % air – checked on the rescuer's machine).
        void RivalR4Rescue()
        {
            if (!StoryFlags.Has(NpcKeys.RivalR4Started) || StoryFlags.Has(NpcKeys.RivalR4Rescued) || StoryFlags.Has(NpcKeys.RivalR4Ignored)) return;
            StoryFlags.Set(NpcKeys.RivalR4Rescued);
            StoryFlags.Set(NpcKeys.RivalsFriends);
            Pay(15000);
            RadioAll(NpcDatabase.Chad, "rival.r4x");
            GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Brüder gerettet", "Brothers rescued"), "+" + Loc.Money(15000));
            objectsDirty = true;
            trackerDirty = true;
        }

        /// HOST: R2 – Gerald filmed ≥ 20 s (one clip with quality ≥ 1.0) → an honest upload the same day.
        void RivalGeraldClip(float seconds, float quality)
        {
            if (!StoryFlags.Has(NpcKeys.RivalR2Started) || StoryFlags.Has(NpcKeys.RivalR2Done)) return;
            int day = NpcUtil.Day;
            if (geraldDay != day) { geraldDay = day; geraldSecondsToday = 0f; geraldQualityOk = false; }
            geraldSecondsToday += seconds;
            if (quality >= 1f) geraldQualityOk = true;
        }

        bool R2ClipsReady => geraldDay == NpcUtil.Day && geraldSecondsToday >= 20f && geraldQualityOk;

        /// HOST: bonus followers / money on the resolved video (R2 +25 % followers, R3 +40 % views).
        void RivalUploadResolved(VideoRecord v)
        {
            var s = S;
            if (s == null || v == null) return;
            string flag = "r2_upload:" + v.day;
            if (StoryFlags.Has(flag))
            {
                StoryFlags.Clear(flag);
                long extra = Math.Max(1, v.followersGained / 4);
                GameManager.I.AddFollowers(extra);
                StoryFlags.Set(NpcKeys.RivalR2Done);
                StoryFlags.Set("r2_week");
                GameEvents.RaiseNotify(NotifyKind.Followers, Loc.Tr("Gerald ist echt!", "Gerald is real!"), "+" + Loc.Number(extra));
            }
            else if (StoryFlags.Has("r3_bonus"))
            {
                StoryFlags.Clear("r3_bonus");
                long f = Math.Max(1, (long)(v.followersGained * 0.4f)), m = (long)(v.money * 0.4f);
                GameManager.I.AddFollowers(f);
                Pay(m);
                GameEvents.RaiseNotify(NotifyKind.Followers, Loc.Tr("Skandal-Video", "Scandal video"), "+40 %");
            }
        }

        void RivalUploadCreated(PendingUpload u)
        {
            if (u == null || !StoryFlags.Has(NpcKeys.RivalR2Started) || StoryFlags.Has(NpcKeys.RivalR2Done)) return;
            if (u.titleStyle != (int)TitleStyle.Honest || !R2ClipsReady) return;
            bool gerald = false;
            if (u.clips != null) for (int i = 0; i < u.clips.Count; i++) if (u.clips[i] != null && u.clips[i].speciesId == "green_turtle") gerald = true;
            if (gerald) StoryFlags.Set("r2_upload:" + u.dayUploaded);
        }
    }
}
