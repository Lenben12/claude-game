using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Quests part of the director: talk routing, side quests (L.7), Toni's chain (K.8), Dr. Fin's research checks (B.4,
    /// K.3), quest objects, local photo/clip/recording checks (reported to the host) and the quest-line feed (L.10).
    public sealed partial class NpcDirector
    {
        bool trackerDirty = true, objectsDirty = true;
        float trackerTimer, objectsTimer, markerTimer;
        readonly Dictionary<string, int> talkIndex = new Dictionary<string, int>();
        string deliveringCarry;

        void ResetQuestSession()
        {
            trackerDirty = true;
            objectsDirty = true;
            talkIndex.Clear();
            escortPlayer = -1;
            escortDone = false;
            recNightReef = recCanoe = recMast = listenWhale = 0f;
            holdObj = null;
            holdT = 0f;
            deliveringCarry = null;
        }

        // ================================================================ HOST think
        void HostThink()
        {
            ThinkPrologue();
            ThinkHorst();
            ThinkWanda();
            ThinkFin();
            ThinkRivals();
            ThinkVillage();
            ThinkEscort();
            TickOffers();
            markerTimer -= 0.1f;
            if (markerTimer <= 0f) { markerTimer = 0.5f; ThinkMarkers(); ThinkQuestTimers(); ThinkSoundClues(); }
        }

        /// L.6 sound clue: within 25 m of the radio tower the old radio crackles – that counts as its clue.
        void ThinkSoundClues()
        {
            const string flag = NpcKeys.CluePrefix + "radio_tower";
            if (StoryFlags.Has(flag) || Collectibles.IsCollected("secret:radio_tower")) return;
            Vector3 tower = PropInfo.RadioTowerBase != Vector3.zero ? PropInfo.RadioTowerBase : WorldLayout.RadioTowerRuins;
            if (AnyoneNear(tower, 25f)) StoryFlags.Set(flag);
        }

        void OnDayStarted(int day)
        {
            trackerDirty = true;
            objectsDirty = true;
            if (!Net.HasAuthority || !InGame) return;
            MissionsNewDay();
            RivalsNewDay();
            EnsureResearchOffers(true);
        }

        // ================================================================ talk routing (HOST)
        void OnTalkRequest(int fromId, NpcActor a)
        {
            if (a == null || !a.Present) return;
            if (IsTalking(a)) { ConvNext(a.Index, false); return; }
            if (a.Index < offers.Length && offers[a.Index].quest != null) { OnAccept(fromId, a, false); return; }
            a.FacePoint = PlayerPos(fromId);
            string carried = CarriedBy(fromId);
            string id = a.Id;

            if (id == NpcDatabase.Horst)
            {
                if (prologueActive) return;
                if (TutorialRunning)
                {
                    int beat = BeatFor(S.questIndex);
                    if (!StoryFlags.Has(NpcKeys.Beat(beat))) { PlayBeat(a, beat); return; }
                }
                if (EscortTalk(fromId, a)) return;
            }
            if (id == NpcDatabase.Toni && ToniTalk(fromId, a, carried)) return;
            if (SideQuestHandIn(fromId, a, carried)) return;
            if (OfferSideQuest(a)) return;
            if (id == NpcDatabase.Pearl && !StoryFlags.Has(NpcKeys.PearlMet))
            {
                Talk(a, "pearl.first", () => { StoryFlags.Set(NpcKeys.PearlMet); StoryFlags.Set(StoryKeys.NightOfTheEye); });
                return;
            }
            if (id == NpcDatabase.Fin && !StoryFlags.Has(NpcKeys.FinMet))
            {
                Talk(a, "fin.first", () => { StoryFlags.Set(NpcKeys.FinMet); EnsureResearchOffers(true); });
                return;
            }
            if (id == NpcDatabase.Wanda && !StoryFlags.Has(NpcKeys.WandaMet))
            {
                StoryFlags.Set(NpcKeys.WandaMet);
                Talk(a, "wanda.met");
                return;
            }
            SmallTalk(a);
        }

        /// The carried quest item of a player (host: own Carry, remote: the player's record).
        static string CarriedBy(int playerId)
        {
            if (playerId == Net.LocalId) return Carry.Current;
            var p = Net.Find(playerId);
            var rec = p != null && !string.IsNullOrEmpty(p.key) ? PlayerRecords.Find(p.key) : null;
            return rec != null ? rec.carried : null;
        }

        void SetRemoteCarry(int fromId, string item)
        {
            var p = Net.Find(fromId);
            var rec = p != null && !string.IsNullOrEmpty(p.key) ? PlayerRecords.Find(p.key) : null;
            if (rec != null) rec.carried = string.IsNullOrEmpty(item) ? null : item;
        }

        /// Takes the carried item from a player (hand-in).
        void TakeCarried(int playerId, string carryId)
        {
            if (playerId == Net.LocalId)
            {
                if (Carry.Current == carryId) { deliveringCarry = carryId; Carry.Drop(); deliveringCarry = null; }
            }
            else
            {
                SetRemoteCarry(playerId, null);
                NpcNet.SendEvent(NpcNet.EvDropCarry, carryId, null, playerId);
            }
        }

        void SmallTalk(NpcActor a)
        {
            string id = a.Id;
            // clue lines first (L.6) – once each, then the normal rotation
            string clue = ClueFor(id);
            if (clue != null) { Talk(a, clue); return; }
            string line = null;
            switch (id)
            {
                case NpcDatabase.Horst: line = HorstSmallTalk(); break;
                case NpcDatabase.Fin: line = FinSmallTalk(); break;
                case NpcDatabase.Lio:
                    if (VillageCrateWaiting()) { line = "lio.crate"; break; }
                    line = Rotate(id, "lio.talk"); break;
                case NpcDatabase.Chad: case NpcDatabase.Brad: line = Rotate(id, "rival.visit"); break;
                case NpcDatabase.Weaver1: line = "v.greet.1"; break;
                case NpcDatabase.Weaver2: line = "v.greet.2"; break;
                case NpcDatabase.Fisher1: line = "v.greet.3"; break;
                case NpcDatabase.Fisher2: line = "v.greet.4"; break;
                case NpcDatabase.Teen: line = "v.greet.5"; break;
                case NpcDatabase.Grandpa: line = "v.greet.6"; break;
                case NpcDatabase.Ada: line = Rotate(id, "ada.talk"); break;
                case NpcDatabase.Juno:
                    line = Village.IsLanternEvening ? Rotate("juno_l", "village.lantern") : Rotate(id, "juno.talk"); break;
                case NpcDatabase.Pearl:
                    if (StoryFlags.Has(NpcKeys.PearlLightStory) && NpcUtil.Hash01(NpcUtil.Day, 51) < 0.3f) { Talk(a, "pearl.afterP3"); return; }
                    line = Rotate(id, "pearl.talk"); break;
                default: line = Rotate(id, id + ".talk"); break;
            }
            if (line != null) { if (id != NpcDatabase.Grandpa && id != NpcDatabase.Ada) a.PlayGesture(NpcGesture.Greet); Talk(a, line); }
        }

        string Rotate(string key, string prefix)
        {
            var seq = DialogueDatabase.Seq(prefix);
            if (seq.Count == 0) return null;
            talkIndex.TryGetValue(key, out int i);
            talkIndex[key] = i + 1;
            return seq[i % seq.Count];
        }

        // ---- L.6 clue lines: (speaker, line id, secret key, condition)
        string ClueFor(string npc)
        {
            switch (npc)
            {
                case NpcDatabase.Pearl:
                    if (!StoryFlags.Has(NpcKeys.PearlMet)) return null;
                    if (TryClue("lighthouse_top", "pearl.clue.top")) return "pearl.clue.top";
                    if (TryClue("lighthouse_cellar", "pearl.clue.cellar")) return "pearl.clue.cellar";
                    if (Progression.IsSiteUnlocked("kelp_forest") && TryClue("kelp_carving", "pearl.clue.kelp")) return "pearl.clue.kelp";
                    if (Quests.IsDone("P2") && TryClue("village_loft", "pearl.clue.loft")) return "pearl.clue.loft";
                    return null;
                case NpcDatabase.Juno: return TryClue("waterfall", "juno.clue.falls") ? "juno.clue.falls" : null;
                case NpcDatabase.Lio: return TryClue("ravine_piggy", "lio.clue.ravine") ? "lio.clue.ravine" : null;
                case NpcDatabase.Toni:
                    if (!StoryFlags.Has(StoryKeys.FogMap)) return null;
                    if (Progression.IsSiteUnlocked("blue_drop") && TryClue("blue_buoy", "toni.clue.buoy")) return "toni.clue.buoy";
                    if (TryClue("fog_archive", "toni.clue.archive")) return "toni.clue.archive";
                    return null;
            }
            return null;
        }

        static bool TryClue(string secret, string line)
        {
            string flag = NpcKeys.CluePrefix + secret;
            if (StoryFlags.Has(flag) || Collectibles.IsCollected("secret:" + secret)) return false;
            StoryFlags.Set(flag);
            return true;
        }

        bool VillageCrateWaiting()
        {
            var s = S;
            if (s == null) return false;
            for (int i = 0; i < s.deliveries.Count; i++)
                if (s.deliveries[i] != null && s.deliveries[i].dropped && s.deliveries[i].Mishap == DeliveryMishap.Village) return true;
            return false;
        }

        // ================================================================ side quests (HOST)
        static bool Available(SideQuestDef d)
        {
            var s = S;
            if (s == null || d == null) return false;
            if (Quests.Get(d.id) != null) return false;
            if (d.needTutorial && !StoryFlags.Has(StoryKeys.TutorialDone)) return false;
            if (s.day < d.minDay || s.followers < d.minFollowers) return false;
            if (!string.IsNullOrEmpty(d.needFlag) && !StoryFlags.Has(d.needFlag)) return false;
            if (!string.IsNullOrEmpty(d.needSite) && !Progression.IsSiteUnlocked(d.needSite)) return false;
            if (!string.IsNullOrEmpty(d.needQuest) && !Quests.IsDone(d.needQuest)) return false;
            if (!string.IsNullOrEmpty(d.needItem) && !s.ownedItems.Contains(d.needItem)) return false;
            if (d.needVillage && !StoryFlags.Has(StoryKeys.VillageVisited)) return false;
            if (d.id == "P3" && !StoryRules.IsLanternDay(s.day)) return false;
            return true;
        }

        /// A side quest is on offer right now (its giver is around and shows "!") – for the Tasks app.
        public static bool IsOffered(SideQuestDef d)
        {
            if (!Available(d) || I == null) return false;
            var g = I.Actor(d.giver);
            return g != null && g.Present;
        }

        bool OfferSideQuest(NpcActor a)
        {
            var all = SideQuestDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var d = all[i];
                if (d.giver != a.Id || !Available(d)) continue;
                if (Quests.ActiveSideCount >= Quests.MaxActiveSide)
                {
                    Talk(a, "horst.full", null, a.Id);
                    return true;
                }
                bool rivals = a.Id == NpcDatabase.Brad || a.Id == NpcDatabase.Chad;
                var seq = d.id == "B1" ? "rival.b1.start" : d.StartSeq;
                if (!Talk(a, seq, null, rivals ? null : a.Id)) return true;
                var c = ConvOf(a);
                if (c != null) c.offerQuest = d.id;
                return true;
            }
            // B1 is offered by Brad after R1 (lines live in rival.b1.*)
            return false;
        }

        void OnAccept(int fromId, NpcActor a, bool decline)
        {
            if (a == null || a.Index >= offers.Length) return;
            var o = offers[a.Index];
            if (o.quest == null) return;
            CloseOffer(a);
            var d = SideQuestDatabase.Get(o.quest);
            if (o.choice)
            {
                // B1: give the golden selfie stick back (+200 $, they are nicer) or keep it (hand prop, they sulk)
                var q = Quests.Get("B1");
                if (q == null || q.done) return;
                var chad = Actor(NpcDatabase.Chad);
                if (!decline)
                {
                    Pay(200);
                    StoryFlags.Set(NpcKeys.StickReturned);
                    Talk(a, "rival.b1.give.1");
                    if (chad != null) TalkMore(a, "rival.b1.give.2");
                    Notify(NotifyKind.Quest, "Selfie-Stick zurückgegeben", "Selfie stick returned", "+" + Loc.Money(200));
                }
                else
                {
                    Cosmetics.Unlock("handprop:1");
                    StoryFlags.Set(NpcKeys.StickKept);
                    Talk(a, "rival.b1.keep.1");
                    TalkMore(a, "rival.b1.keep.2");
                }
                Quests.Complete("B1");
                trackerDirty = true;
                return;
            }
            if (decline)
            {
                Talk(a, a.Id == NpcDatabase.Horst ? "horst.later" : "sq.later.v", null, a.Id);
                return;
            }
            if (d == null) return;
            if (Quests.Start(d.id))
            {
                Talk(a, a.Id == NpcDatabase.Horst ? "horst.accept" : "sq.accept.v", null, a.Id);
                a.PlayGesture(NpcGesture.ThumbsUp);
                GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Neue Aufgabe", "New task"), d.Title);
                // the pin is per player: only the accepting player's own line follows it
                if (fromId == Net.LocalId && S != null && string.IsNullOrEmpty(S.trackedQuest)) Quests.Track(d.id);
                trackerDirty = true;
                objectsDirty = true;
            }
            else Talk(a, "horst.full", null, a.Id);
        }

        // ---- progress encoding (what the UI shows): QuestState.progress = the step's counter (items delivered, objects
        //      collected, photo targets, film targets) or seconds for Record steps. Which object was taken / picked up is
        //      kept in shared story flags: "qo:<quest>:<n>" collected/done, "qp:<quest>:<n>" picked up (carried).
        static int Delivered(QuestState q) => q.progress;
        static string ObjFlag(string quest, int n) => "qo:" + quest + ":" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static string PickFlag(string quest, int n) => "qp:" + quest + ":" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

        bool SideQuestHandIn(int fromId, NpcActor a, string carried)
        {
            var s = S;
            if (s == null) return false;
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done || q.id == Quests.ToniChain) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || d.giver != a.Id && !(d.id == "B1" && (a.Id == NpcDatabase.Chad || a.Id == NpcDatabase.Brad))) continue;
                if (q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                switch (st.kind)
                {
                    case SqStep.Talk:
                        // P2: E on Pearl starts the card night once – pressing E again while it runs must not restart it
                        if (d.id == "P2") { if (!cardsRunning) StartCardNight(a); return true; }
                        CompleteSideQuest(d, a);
                        return true;
                    case SqStep.Bring:
                        if (st.spot == "hangar" || st.spot == "v_boatshed") break;   // delivered at a drop zone, not to the giver
                        string item = CarryModels.ItemOf(carried);
                        if (item == null || !ContainsItem(st.target, item)) break;
                        if (d.id == "A1" && Delivered(q) + 1 >= st.count && !HasFish(s, "mackerel", 2))
                        {
                            Talk(a, "ada.needfish", null, a.Id);
                            return true;
                        }
                        TakeCarried(fromId, carried);
                        int delivered = Delivered(q) + 1;
                        if (delivered >= st.count)
                        {
                            if (d.id == "A1") RemoveFish(s, "mackerel", 2);
                            AdvanceSideQuest(d, q, a);
                        }
                        else
                        {
                            Quests.SetProgress(d.id, q.step, delivered);
                            a.PlayGesture(NpcGesture.ThumbsUp);
                            GameEvents.RaiseNotify(NotifyKind.Quest, d.Title, delivered + "/" + st.count);
                        }
                        trackerDirty = true;
                        objectsDirty = true;
                        return true;
                    case SqStep.Choice:
                        Talk(a, "rival.b1.choice");
                        var c = ConvOf(a);
                        if (c != null) { c.offerQuest = "B1"; c.offerChoice = true; }
                        return true;
                }
            }
            return false;
        }

        static bool ContainsItem(string csv, string item)
        {
            if (string.IsNullOrEmpty(csv) || item == null) return false;
            int start = 0;
            while (start <= csv.Length)
            {
                int comma = csv.IndexOf(',', start);
                int end = comma < 0 ? csv.Length : comma;
                if (end - start == item.Length && string.CompareOrdinal(csv, start, item, 0, item.Length) == 0) return true;
                if (comma < 0) break;
                start = comma + 1;
            }
            return false;
        }

        /// Counts the "qo:" flags of a step's n objects/targets, stores the count as progress and advances when complete.
        void CountFlagsAndAdvance(SideQuestDef d, QuestState q, SqStepDef st, int n)
        {
            int have = 0;
            for (int k = 0; k < n; k++) if (StoryFlags.Has(ObjFlag(d.id, k))) have++;
            if (have >= st.count) { AdvanceSideQuest(d, q, null); return; }
            Quests.SetProgress(d.id, q.step, have);
            GameEvents.RaiseNotify(NotifyKind.Quest, d.Title, have + "/" + st.count);
            trackerDirty = true;
        }

        static int CsvCount(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return 0;
            int n = 1;
            for (int i = 0; i < csv.Length; i++) if (csv[i] == ',') n++;
            return n;
        }

        static int IndexInCsv(string csv, string item)
        {
            if (string.IsNullOrEmpty(csv) || item == null) return -1;
            int idx = 0, start = 0;
            while (start <= csv.Length)
            {
                int comma = csv.IndexOf(',', start);
                int end = comma < 0 ? csv.Length : comma;
                if (end - start == item.Length && string.CompareOrdinal(csv, start, item, 0, item.Length) == 0) return idx;
                if (comma < 0) break;
                start = comma + 1;
                idx++;
            }
            return -1;
        }

        static bool HasFish(GameState s, string kind, int n)
        {
            int c = 0;
            for (int i = 0; i < s.fishInventory.Count; i++) if (s.fishInventory[i] != null && s.fishInventory[i].kindId == kind) c++;
            return c >= n;
        }

        static void RemoveFish(GameState s, string kind, int n)
        {
            for (int i = s.fishInventory.Count - 1; i >= 0 && n > 0; i--)
                if (s.fishInventory[i] != null && s.fishInventory[i].kindId == kind) { s.fishInventory.RemoveAt(i); n--; }
            FishMarket.RaiseChanged();
        }

        static FishItem TakeAnyFish(GameState s)
        {
            for (int i = s.fishInventory.Count - 1; i >= 0; i--)
            {
                var f = s.fishInventory[i];
                if (f == null || f.kindId == "boot") continue;
                s.fishInventory.RemoveAt(i);
                FishMarket.RaiseChanged();
                return f;
            }
            return null;
        }

        /// Step done → next step (resets progress) or the whole quest.
        void AdvanceSideQuest(SideQuestDef d, QuestState q, NpcActor giver)
        {
            int next = q.step + 1;   // captured first: SetProgress mutates q (same QuestState instance)
            if (next >= d.steps.Length) { CompleteSideQuest(d, giver); return; }
            Quests.SetProgress(d.id, next, 0);
            GameEvents.RaiseNotify(NotifyKind.Quest, d.Title, d.steps[next].Line);
            trackerDirty = true;
            objectsDirty = true;
        }

        void CompleteSideQuest(SideQuestDef d, NpcActor giver)
        {
            var q = Quests.Get(d.id);
            if (q == null || q.done) return;
            if (giver == null) giver = Actor(d.giver);
            Quests.Complete(d.id);
            long pay = QuestPay(d);
            Pay(pay);
            if (!string.IsNullOrEmpty(d.cosmetic)) Cosmetics.Unlock(d.cosmetic);
            if (!string.IsNullOrEmpty(d.flag)) StoryFlags.Set(d.flag);
            switch (d.id)
            {
                case "W1": StoryFlags.Set(NpcKeys.FreeExpress1); StoryFlags.Set(NpcKeys.FreeExpress2); break;
                case "T2": MarkDepthHints(3); break;
            }
            string extra = d.Chip;
            string chip = pay > 0 ? "+" + Loc.Money(pay) + (extra != null ? " · " + extra : "") : extra;
            GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Erledigt: ", "Done: ") + d.Title, chip);
            if (giver != null && giver.Present)
            {
                giver.PlayGesture(NpcGesture.TaDa);
                Talk(giver, d.EndSeq, null, giver.Id);
                if (d.id == "P1") TalkMore(giver, "pearl.afterP1");
                if (d.id == "P3") TalkMore(giver, "pearl.afterP3");
                if (d.id == "L1") giver.PlayGesture(NpcGesture.Selfie);
            }
            trackerDirty = true;
            objectsDirty = true;
        }

        /// Side-quest money: d.money is the floor. Late quests (past the kelp forest: wreck, blue drop, trench, Fog Isle)
        /// scale with a quarter of an expected upload so they keep pace with upload income. Early quests stay fixed – the
        /// economy sim models them as a flat per-day amount.
        static long QuestPay(SideQuestDef d)
        {
            if (d == null) return 0;
            long pay = d.money;
            if (pay <= 0) return pay;
            bool late = d.needSite == "wreck" || d.needSite == "blue_drop" || d.needSite == "deep_trench" || d.needSite == "fog_isle"
                        || d.needFlag == StoryKeys.FogMap;
            var s = S;
            if (late && s != null)
            {
                long scaled = (long)(Mathf.Round(0.25f * Progression.ExpectedUploadMoney(s.followers) / 5f) * 5f);
                if (scaled > pay) pay = scaled;
            }
            return pay;
        }

        /// T2 reward: Tomas marks the depth bands of 3 undiscovered species in the Sealog ("depthhint:<id>" flags).
        void MarkDepthHints(int n)
        {
            var all = SpeciesDatabase.All;
            for (int i = 0; i < all.Count && n > 0; i++)
            {
                var sp = all[i];
                if (sp.land || Journal.IsDiscovered(sp.id)) continue;
                string f = NpcKeys.DepthHintPrefix + sp.id;
                if (StoryFlags.Has(f)) continue;
                StoryFlags.Set(f);
                n--;
            }
        }

        /// Side quests with a time window or a place condition (HOST, every 0.5 s).
        void ThinkQuestTimers()
        {
            var s = S;
            if (s == null) return;
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind == SqStep.Wait && s.day > q.startedDay && Hour >= st.a)
                    AdvanceSideQuest(d, q, null);
                else if (st.kind == SqStep.Lantern && Village.LanternReleasedToday)
                    AdvanceSideQuest(d, q, null);
            }
        }

        // ---- P2 card night: Horst escorted to the village after 18:00
        int escortPlayer = -1;
        bool escortDone;
        float escortRepath;

        bool EscortActive => escortPlayer >= 0 || cardsRunning;

        bool EscortTalk(int fromId, NpcActor horst)
        {
            var q = Quests.Get("P2");
            if (q == null || q.done || q.step != 0) return false;
            if (Hour < 18f && Hour > 6f) { Talk(horst, "horst.cardsearly"); return true; }
            escortPlayer = fromId;
            Talk(horst, "horst.cards");
            horst.PlayGesture(NpcGesture.Shrug);
            return true;
        }

        void ThinkEscort()
        {
            var horst = Actor(NpcDatabase.Horst);
            if (horst == null) return;
            if (escortDone)
            {
                escortDone = false;
                escortPlayer = -1;
                var d = SideQuestDatabase.Get("P2");
                CompleteSideQuest(d, Actor(NpcDatabase.Pearl));
                return;
            }
            if (escortPlayer < 0 || cardsRunning) return;
            var q = Quests.Get("P2");
            if (q == null || q.done) { escortPlayer = -1; return; }
            horst.SetPresent(true);
            Vector3 meet = QuestSpots.Get("v_meeting");
            if (q.step == 0)
            {
                if (NpcUtil.FlatDist(horst.Position, meet) < 14f && Hour >= 18f)
                {
                    Quests.SetProgress("P2", 1, 0);
                    horst.GoTo(QuestSpots.Get("v_cards_h"), 2f);
                    GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Kartenabend", "Card Night"), Loc.Tr("Zu Perle", "To Pearl"));
                    trackerDirty = true;
                    return;
                }
                escortRepath -= 0.1f;
                Vector3 target = PlayerPos(escortPlayer);
                if (escortRepath <= 0f && NpcUtil.FlatDist(horst.Position, target) > 3f)
                {
                    escortRepath = 1f;
                    horst.GoTo(target - (target - horst.Position).normalized * 2f, NpcUtil.FlatDist(horst.Position, target) > 12f ? 3.6f : 2.4f);
                }
            }
        }

        void StartCardNight(NpcActor pearl)
        {
            // only reached at P2 step 1 (Horst was escorted here). After a reload the escort/session fields are gone and
            // Horst is back at the dock – place him at the card table instead of refusing (cardsRunning keeps him there).
            var horst = Actor(NpcDatabase.Horst);
            if (horst != null)
            {
                horst.SetPresent(true);
                horst.Stop();
                horst.Place(QuestSpots.Get("v_cards_h"), 270f);
            }
            cardsRunning = true;
            cardsT = 0f;
            Talk(pearl, "pearl.talk.2");
        }

        // ================================================================ Toni's chain (HOST, K.8)
        const string ToniCoconut = "qo:toni:coconut", ToniFish = "qo:toni:fish", ToniValve = "qo:toni:valve", ToniCoil = "qo:toni:coil",
            ToniRecorderPicked = "qp:toni:recorder";

        bool ToniTalk(int fromId, NpcActor toni, string carried)
        {
            if (!StoryFlags.Has(StoryKeys.ToniArrived) || StoryFlags.Has(StoryKeys.FogMap)) return false;
            var s = S;
            var q = Quests.Get(Quests.ToniChain);
            if (q == null)
            {
                Quests.Start(Quests.ToniChain);
                StoryFlags.Set(NpcKeys.ToniMet);
                toni.Motion = NpcMotion.Idle;
                Talk(toni, "toni.t1", () =>
                {
                    Quests.SetProgress(Quests.ToniChain, 1, 0);
                    StoryFlags.Set(StoryKeys.ToniT1);
                    TalkMore(toni, "toni.t2");
                    trackerDirty = true;
                    objectsDirty = true;
                });
                return true;
            }
            if (q.done) return false;
            string item = CarryModels.ItemOf(carried);
            switch (q.step)
            {
                case 0:
                    // the chain was started elsewhere (save from an older build, co-op): the first talk still counts as T1
                    StoryFlags.Set(NpcKeys.ToniMet);
                    StoryFlags.Set(StoryKeys.ToniT1);
                    Quests.SetProgress(Quests.ToniChain, 1, 0);
                    Talk(toni, "toni.t2");
                    trackerDirty = true;
                    objectsDirty = true;
                    return true;
                case 1:
                {
                    bool nut = StoryFlags.Has(ToniCoconut), fish = StoryFlags.Has(ToniFish);
                    if (item == "coconut" && !nut) { TakeCarried(fromId, carried); nut = true; StoryFlags.Set(ToniCoconut); }
                    else if (item == "grilled_fish" && !fish) { TakeCarried(fromId, carried); fish = true; StoryFlags.Set(ToniFish); }
                    else if (!fish && s != null && TakeAnyFish(s) != null) { fish = true; StoryFlags.Set(ToniFish); Talk(toni, "toni.t2sushi"); }
                    if (nut && fish)
                    {
                        Quests.SetProgress(Quests.ToniChain, 2, 0);
                        StoryFlags.Set(StoryKeys.ToniT2);
                        toni.PlayGesture(NpcGesture.TaDa);
                        TalkMore(toni, "toni.t2done");
                    }
                    else
                    {
                        Quests.SetProgress(Quests.ToniChain, 1, (nut ? 1 : 0) + (fish ? 1 : 0));
                        TalkMore(toni, "toni.t2need");
                    }
                    trackerDirty = true;
                    objectsDirty = true;
                    return true;
                }
                case 2:
                    if (item == "recorder")
                    {
                        TakeCarried(fromId, carried);
                        Quests.SetProgress(Quests.ToniChain, 3, 0);
                        StoryFlags.Set(StoryKeys.ToniT3);
                        StoryFlags.Set(StoryKeys.ToniT4Active);
                        toni.PlayGesture(NpcGesture.TaDa);
                        Talk(toni, "toni.t3done");
                        trackerDirty = true;
                        objectsDirty = true;
                    }
                    else Talk(toni, "toni.t2done.2");
                    return true;
                case 3:
                {
                    // resumable: the battery is already handed in (T4 set) but the closing talk was cut off by a
                    // reload / return to menu → replay it and finish the chain (also repairs saves broken that way)
                    if (StoryFlags.Has(StoryKeys.ToniT4))
                    {
                        Talk(toni, "toni.t4done", FinishToni);
                        return true;
                    }
                    bool battery = s != null && s.ownedItems.Contains("radio_battery");
                    if (StoryFlags.Has(ToniValve) && StoryFlags.Has(ToniCoil) && battery)
                    {
                        s.ownedItems.Remove("radio_battery");
                        GameEvents.RaiseGearChanged();
                        StoryFlags.Clear(StoryKeys.ToniT4Active);
                        StoryFlags.Set(StoryKeys.ToniT4);
                        toni.PlayGesture(NpcGesture.TaDa);
                        Talk(toni, "toni.t4done", FinishToni);
                    }
                    else Talk(toni, "toni.t4need");
                    return true;
                }
            }
            return false;
        }

        /// End of Toni's chain (T4): quest done + Fog Isle map. Idempotent – also used to resume an interrupted T4 talk.
        void FinishToni()
        {
            Quests.Complete(Quests.ToniChain);
            StoryFlags.Set(StoryKeys.FogMap);
            trackerDirty = true;
            objectsDirty = true;
        }

        // ================================================================ HOST events from players (photo / clip / rec / objects)
        static readonly HashSet<string> tagSet = new HashSet<string>();

        void HostEvent(int fromId, string kind, string a, string b, string c)
        {
            switch (kind)
            {
                case "photo": HostPhoto(fromId, a, b, NpcNet.ParseF(c)); break;
                case "clip": HostClip(fromId, a, b, NpcNet.ParseF(c)); break;
                case "rec": HostRecording(a, NpcNet.ParseF(b)); break;
                case "listen": HostListen(a, NpcNet.ParseF(b)); break;
                case "lantern": trackerDirty = true; break;
                case "lost": HostObjectLost(a); break;
            }
        }

        static void ParseTags(string csv)
        {
            tagSet.Clear();
            if (string.IsNullOrEmpty(csv)) return;
            var parts = csv.Split(',');
            for (int i = 0; i < parts.Length; i++) if (parts[i].Length > 0) tagSet.Add(parts[i]);
        }

        static int TagInt(string prefix)
        {
            foreach (var t in tagSet)
                if (t.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(t.Substring(prefix.Length), out int v)) return v;
            return 0;
        }

        void HostPhoto(int fromId, string species, string tags, float depth)
        {
            ParseTags(tags);
            var s = S;
            if (s == null) return;
            bool water = tagSet.Contains("water");
            // ---- side quests
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind == SqStep.Photo)
                {
                    if (st.target == "guenther")
                    {
                        if (tagSet.Contains("guenther") && Between(Hour, st.a, st.b)) AdvanceSideQuest(d, q, null);
                    }
                    else if (st.target == "lanterns")
                    {
                        if (TagInt("lanterns:") >= 5 && water) AdvanceSideQuest(d, q, null);
                    }
                    else if (st.count > 1)
                    {
                        if (!water) continue;
                        string[] targets = st.target.Split(',');
                        bool any = false;
                        for (int k = 0; k < targets.Length; k++)
                            if (tagSet.Contains(targets[k]) && !StoryFlags.Has(ObjFlag(d.id, k))) { StoryFlags.Set(ObjFlag(d.id, k)); any = true; }
                        if (any) CountFlagsAndAdvance(d, q, st, targets.Length);
                    }
                    else if (species == st.target && depth >= st.a) AdvanceSideQuest(d, q, null);
                }
                else if (st.kind == SqStep.Selfie && tagSet.Contains("selfie") && tagSet.Contains("npc:" + st.target)) AdvanceSideQuest(d, q, null);
            }
            // ---- Lio guards a mishap crate in the village: pay with a selfie (L.12)
            if (tagSet.Contains("selfie") && tagSet.Contains("npc:" + NpcDatabase.Lio) && VillageCrateWaiting())
            {
                StoryFlags.Set("lio_selfie:" + s.day);
                LioSelfieTaken?.Invoke();
                var lio = Actor(NpcDatabase.Lio);
                if (lio != null) { lio.PlayGesture(NpcGesture.TaDa); Talk(lio, "lio.cratedone"); }
            }
            // ---- research photos
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r.kind != ResearchKind.Photo || r.target != species || !IsResearchOpen(r.id)) continue;
                if (r.a < 0f && !tagSet.Contains("pitchdown")) continue;
                if (r.needLamp && !tagSet.Contains("lamp")) continue;
                if (r.needNight && !Between(Hour, 21f, 4f)) continue;
                if (depth < r.minDepth) continue;
                ResearchAdvance(r, 1);
            }
            // ---- rivals R3: the yacht dumping chum on camera
            if (tagSet.Contains("yacht_chum")) RivalR3Exposed();
            // ---- Gerald selfie achievement (K.4)
            if (tagSet.Contains("selfie") && species == "green_turtle") Achievements.Unlock("gerald_selfie");
        }

        /// Raised on the host when the village kids got their selfie (L.12 mishap "village") – the Boat module may
        /// release the crate (also readable as StoryFlags "lio_selfie:&lt;day&gt;").
        public static event Action LioSelfieTaken;

        void HostClip(int fromId, string species, string flags, float seconds)
        {
            ParseTags(flags);
            var s = S;
            if (s == null || string.IsNullOrEmpty(species)) return;
            int crowd = TagInt("crowd:"), tier = TagInt("tier:");
            float quality = TagInt("q:") / 100f;
            // side quests: Film steps (bit mask for several species)
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind != SqStep.Film) continue;
                int k = IndexInCsv(st.target, species);
                if (k < 0) continue;
                if (st.count <= 1) { AdvanceSideQuest(d, q, null); continue; }
                if (StoryFlags.Has(ObjFlag(d.id, k))) continue;
                StoryFlags.Set(ObjFlag(d.id, k));
                CountFlagsAndAdvance(d, q, st, CsvCount(st.target));
            }
            // research clips
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r.kind != ResearchKind.Film || r.target != species || !IsResearchOpen(r.id)) continue;
                if (crowd < r.a || tier < r.b) continue;
                if (r.boatFast && !tagSet.Contains("boatfast")) continue;
                ResearchAdvance(r, 1);
            }
            if (species == "green_turtle") RivalGeraldClip(seconds, quality);
        }

        void HostRecording(string what, float seconds)
        {
            var s = S;
            if (s == null || seconds <= 0f) return;
            if (what == "mast")
            {
                var cur = Missions.Current;
                if (cur != null && cur.id == "M4") CompleteMission("M4");
                return;
            }
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind != SqStep.Record || st.target != what) continue;
                int sec = q.progress + Mathf.RoundToInt(seconds);
                if (sec >= st.a) AdvanceSideQuest(d, q, null);
                else Quests.SetProgress(d.id, q.step, sec);
            }
        }

        void HostListen(string species, float seconds)
        {
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r.kind != ResearchKind.Listen || r.target != species || !IsResearchOpen(r.id)) continue;
                int p = ResearchProgress(r.id) + Mathf.RoundToInt(seconds);
                if (p >= r.a) CompleteResearch(r);
                else Research.SetProgress(r.id, p);
            }
        }

        void OnFishCaught(int value)
        {
            if (!Net.HasAuthority || !InGame) return;
            var s = S;
            if (s == null || s.fishInventory.Count == 0) return;
            var f = s.fishInventory[s.fishInventory.Count - 1];
            if (f == null) return;
            HorstOnCatch(f);
            bool fromBoat = BoatTelemetry.Valid && AnyoneNear(BoatTelemetry.Position, 7f) || (Refs.Boat != null && !(Refs.Boat is UnityEngine.Object o && o == null) && Refs.Boat.PlayerAboard);
            // research catches (rod chain)
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r.kind != ResearchKind.Catch || r.target != f.kindId || !IsResearchOpen(r.id)) continue;
                if (f.weightKg < r.a || (r.b >= 1f && !fromBoat)) continue;
                ResearchAdvance(r, 1);
            }
            // side quests: Catch steps
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind != SqStep.Catch) continue;
                if (!string.IsNullOrEmpty(st.target) && st.target != f.kindId) continue;
                if (f.weightKg < st.a || (st.b >= 1f && !fromBoat) || f.kindId == "boot") continue;
                AdvanceSideQuest(d, q, null);
            }
        }

        void OnUploadCreated(PendingUpload u)
        {
            if (!Net.HasAuthority || !InGame || u == null) return;
            RivalUploadCreated(u);
            var s = S;
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind != SqStep.Upload) continue;
                bool ok = false;
                if (st.target == "honest") ok = u.titleStyle == (int)TitleStyle.Honest;
                else if (st.target == "shark")
                {
                    if (u.clips != null)
                        for (int k = 0; k < u.clips.Count && !ok; k++)
                        {
                            var sp = u.clips[k] != null ? SpeciesDatabase.Get(u.clips[k].speciesId) : null;
                            ok = sp != null && sp.plan == BodyPlan.Shark;
                        }
                }
                if (ok) AdvanceSideQuest(d, q, null);
            }
        }

        void OnUploadResolved(VideoRecord v)
        {
            if (!Net.HasAuthority || !InGame) return;
            RivalUploadResolved(v);
            trackerDirty = true;
        }

        void OnSighted(SpeciesDef sp, int count)
        {
            if (!Net.HasAuthority || !InGame || sp == null || count <= 0) return;
            var all = ResearchDatabase.All;
            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r.kind != ResearchKind.Census || !IsResearchOpen(r.id) || !ContainsItem(r.target, sp.id)) continue;
                ResearchAdvance(r, Mathf.Min(count, 50));
            }
        }

        void OnSideQuestChanged(string id)
        {
            trackerDirty = true;
            objectsDirty = true;
            // a dropped side quest (Tasks app „Aufgeben“): forget its pickup / object flags, so taking it again starts
            // fresh (its Bring items respawn)
            var s = S;
            if (!Net.HasAuthority || s == null || string.IsNullOrEmpty(id) || Quests.Get(id) != null || SideQuestDatabase.Get(id) == null) return;
            string qp = "qp:" + id + ":", qo = "qo:" + id + ":";
            for (int i = s.storyFlags.Count - 1; i >= 0; i--)
            {
                if (i >= s.storyFlags.Count) continue;
                string f = s.storyFlags[i];
                if (f != null && (f.StartsWith(qp, StringComparison.Ordinal) || f.StartsWith(qo, StringComparison.Ordinal))) StoryFlags.Clear(f);
            }
        }

        // ================================================================ markers above heads (HOST)
        void ThinkMarkers()
        {
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null) continue;
                a.MarkerValue = a.Present ? MarkerFor(a) : 0;
            }
        }

        int MarkerFor(NpcActor a)
        {
            var s = S;
            if (s == null || prologueActive) return 0;
            string id = a.Id;
            // ready to hand in → "?"
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                bool mine = d.giver == id || (d.id == "B1" && (id == NpcDatabase.Brad || id == NpcDatabase.Chad));
                if (!mine) continue;
                var st = d.steps[q.step];
                if (st.kind == SqStep.Talk || st.kind == SqStep.Choice) return 2;
                if (st.kind == SqStep.Bring && AnyCarrying(st.target)) return 2;
            }
            if (id == NpcDatabase.Toni)
            {
                if (!StoryFlags.Has(StoryKeys.ToniArrived) || StoryFlags.Has(StoryKeys.FogMap)) { }
                else
                {
                    var tq = Quests.Get(Quests.ToniChain);
                    if (tq == null) return 1;
                    if (tq.step == 1 && (AnyCarrying("coconut,grilled_fish"))) return 2;
                    if (tq.step == 2 && AnyCarrying("recorder")) return 2;
                    if (tq.step == 3 && StoryFlags.Has(StoryKeys.ToniT4)) return 2;
                    if (tq.step == 3 && StoryFlags.Has(ToniValve) && StoryFlags.Has(ToniCoil) && s.ownedItems.Contains("radio_battery")) return 2;
                }
            }
            // story beats → "!"
            if (id == NpcDatabase.Horst && TutorialRunning && !StoryFlags.Has(NpcKeys.Beat(BeatFor(s.questIndex)))) return 1;
            if (id == NpcDatabase.Pearl && !StoryFlags.Has(NpcKeys.PearlMet)) return 1;
            if (id == NpcDatabase.Fin && !StoryFlags.Has(NpcKeys.FinMet)) return 1;
            if (id == NpcDatabase.Horst)
            {
                var p2 = Quests.Get("P2");
                if (p2 != null && !p2.done && p2.step == 0 && Hour >= 18f) return 1;
            }
            // a side quest to give → "!"
            var all = SideQuestDatabase.All;
            for (int i = 0; i < all.Length; i++)
                if (all[i].giver == id && Available(all[i])) return 1;
            return 0;
        }

        bool AnyCarrying(string csv)
        {
            if (ContainsItem(csv, CarryModels.ItemOf(Carry.Current))) return true;
            var players = Net.Players;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || p.IsLocal || string.IsNullOrEmpty(p.key)) continue;
                var rec = PlayerRecords.Find(p.key);
                if (rec != null && ContainsItem(csv, CarryModels.ItemOf(rec.carried))) return true;
            }
            return false;
        }

        // ================================================================ quest objects (every machine)
        enum ObjKind { Pickup, Collect, Hold, Use, Kiosk, Drop, Scenery }

        sealed class ObjDef
        {
            public string id, spot, model;
            public ObjKind kind;
            public string de, en;
            public float hold;
            public Func<bool> visible;
            public Vector3 offset;
            public float yaw;
            public bool floating;
        }

        sealed class Obj
        {
            public ObjDef def;
            public GameObject go;
            public QuestObject comp;
            public Transform glint;
            /// Id of the same object in the props module's PropQuest (null = none); when props placed it, ours stays hidden.
            public string propId;
            public bool wanted;
        }

        readonly Dictionary<string, Obj> byPropId = new Dictionary<string, Obj>(64);
        bool propHooks, propPalmsChecked, propPalms;

        /// The props module's id for one of our quest objects (PropQuest naming: quest:Q:name, toni:T:name, mission:M:name).
        static string PropIdFor(string id)
        {
            int c = id.LastIndexOf(':');
            string n = c > 0 ? id.Substring(c + 1) : "0";
            if (id.StartsWith("q:sunglasses", StringComparison.Ordinal)) return "quest:H2:sunglasses";
            if (id.StartsWith("q:drum", StringComparison.Ordinal)) return "quest:W1:drum_" + n;
            if (id.StartsWith("peg:", StringComparison.Ordinal)) return "quest:P1:peg_" + n;
            if (id.StartsWith("driftwood:", StringComparison.Ordinal)) return "quest:M1:driftwood_" + n;
            if (id.StartsWith("q:paddle", StringComparison.Ordinal)) return "quest:M2:paddle";
            if (id.StartsWith("ghostnet:", StringComparison.Ordinal)) return "quest:T1:net_" + n;
            if (id.StartsWith("seagrape:", StringComparison.Ordinal)) return "quest:A2:grape_" + n;
            if (id.StartsWith("q:kite", StringComparison.Ordinal)) return "quest:L1:kite";
            if (id.StartsWith("q:tape", StringComparison.Ordinal)) return "quest:N2:tape";
            if (id.StartsWith("q:selfie_stick", StringComparison.Ordinal)) return "quest:B1:stick";
            if (id.StartsWith("liochest", StringComparison.Ordinal)) return "quest:L3:map_treasure";
            if (id.StartsWith("q:recorder", StringComparison.Ordinal)) return "toni:T3:recorder";
            if (id == "valve") return "toni:T4:valve";
            if (id == "coil") return "toni:T4:coil";
            if (id == "m1_fin") return "mission:M1:lucky_fin";
            if (id == "m2_radio") return "mission:M2:radio";
            if (id.StartsWith("m3_shard", StringComparison.Ordinal)) return "mission:M3:lens_" + (id[id.Length - 1] - '1');
            if (id == "m3_lamp") return "mission:M3:lamp";
            if (id.StartsWith("stake:", StringComparison.Ordinal)) return id;
            return null;
        }

        /// Wires the props module's quest objects (PropQuest) to our quest state once.
        void HookPropQuest()
        {
            if (propHooks) return;
            propHooks = true;
            PropQuest.IsWanted = PropWanted;
            PropQuest.CarryWanted = CarryWanted;
            PropQuest.Used += OnPropUsed;
        }

        bool PropWanted(string propId)
        {
            if (propId != null && byPropId.TryGetValue(propId, out var o)) return o.wanted;
            return false;
        }

        /// The grill / a fallen coconut: take it along for Toni (K.8 T2) or Ada (A1) instead of eating/drinking it.
        bool CarryWanted(string item)
        {
            if (!InGame || Carry.IsCarrying) return false;
            if (item == "grilled_fish") return ToniStep(1) && !StoryFlags.Has(ToniFish);
            if (item == "coconut") return (ToniStep(1) && !StoryFlags.Has(ToniCoconut)) || SqStepIs("A1", 0);
            return false;
        }

        /// The local player used a props quest object → the same request as for our own objects.
        void OnPropUsed(string propId, Vector3 pos)
        {
            if (propId == null || !byPropId.TryGetValue(propId, out var o)) return;
            var d = o.def;
            if (d.kind == ObjKind.Pickup)
            {
                // our carry id format "q:<item>:<n>" (hand-ins read the item from it)
                string cur = Carry.Current;
                if (cur != d.id)
                {
                    if (!string.IsNullOrEmpty(cur)) { deliveringCarry = cur; Carry.Drop(); deliveringCarry = null; }
                    Carry.Pick(d.id);
                }
                NpcNet.Request("obj", d.id, "take");
                return;
            }
            if (d.id == "r4_valve" && (!NpcUtil.PlayerValid(out var p) || p.Air01 < 0.3f)) return;
            NpcNet.Request("obj", d.id, d.kind == ObjKind.Use ? "use" : "collect");
        }

        readonly List<Obj> objs = new List<Obj>(64);
        Transform objRoot;
        Obj holdObj;
        float holdT;

        void ClearObjects()
        {
            for (int i = 0; i < objs.Count; i++) if (objs[i].go != null) objs[i].go.SetActive(false);
            holdObj = null;
        }

        void EnsureObjDefs()
        {
            if (objs.Count > 0) return;
            objRoot = new GameObject("QuestObjects").transform;
            objRoot.SetParent(npcRoot, false);
            // ---- side quests (flag strings are built once here – the predicates allocate nothing)
            Pick("H2", 0, "sunglasses", "q_sunglasses", "Sonnenbrille aufheben", "Pick up the sunglasses");
            for (int i = 0; i < 3; i++) Pick("W1", i, "drum", "drum:" + i, "Fass nehmen", "Take the drum").floating = true;
            Add("drop:hangar", "hangar", "pallet", ObjKind.Drop, "Fass abladen", "Drop the drum", () => SqStepIs("W1", 0) && LocalCarrying("drum"));
            for (int i = 0; i < 3; i++) Collect("P1", i, "peg", ObjKind.Collect, "Gezeitenpflock nehmen", "Take the tide peg");
            for (int i = 0; i < 5; i++) Collect("M1", i, "driftwood", ObjKind.Collect, "Treibholz sammeln", "Collect driftwood");
            Pick("M2", 0, "paddle", "q_paddle", "Gus' Paddel nehmen", "Take Gus's paddle");
            Add("drop:boatshed", "v_boatshed", "pallet", ObjKind.Drop, "Paddel ablegen", "Put the paddle down", () => SqStepIs("M2", 0) && LocalCarrying("paddle"));
            for (int i = 0; i < 3; i++) Collect("T1", i, "ghostnet", ObjKind.Hold, "Netz lösen (halten)", "Cut the net (hold)").hold = 5f;
            for (int i = 0; i < 6; i++) Collect("A2", i, "seagrape", ObjKind.Collect, "Seetrauben pflücken", "Pick sea grapes");
            Pick("L1", 0, "kite", "q_kite", "Lios Drachen nehmen", "Take Lio's kite");
            Collect("L3", 0, "liochest", ObjKind.Collect, "Schatz öffnen", "Open the treasure").spot = "q_liochest";
            Pick("N2", 0, "tape", "q_tape", "Das letzte Band nehmen", "Take the last tape");
            Pick("B1", 0, "selfie_stick", "q_selfie_stick", "Selfie-Stick nehmen", "Take the selfie stick");
            for (int i = 0; i < 6; i++) { int k = i; Add("q:coconut:" + i, "coconut:" + i, "coconut", ObjKind.Pickup, "Kokosnuss nehmen", "Take a coconut", () => CoconutVisible(k)); }
            // ---- Toni (K.8)
            Add("q:recorder:0", "q_recorder", "recorder", ObjKind.Pickup, "Tonbandgerät bergen", "Recover the tape recorder", () => ToniStep(2) && !StoryFlags.Has(ToniRecorderPicked));
            Add("valve", "q_valve", "valve", ObjKind.Collect, "Funkröhre nehmen", "Take the radio valve", () => ToniStep(3) && !StoryFlags.Has(ToniValve));
            Add("coil", "q_coil", "coil", ObjKind.Collect, "Kupferspule nehmen", "Take the copper coil", () => ToniStep(3) && !StoryFlags.Has(ToniCoil));
            // ---- Gus' missions (B.2)
            Add("m1_fin", "m1_fin", "fin", ObjKind.Use, "Glücksflosse bergen", "Recover the lucky fin", () => MissionActive("M1"));
            Add("m2_radio", "m2_radio", "radio", ObjKind.Use, "Funkgerät bergen", "Recover the radio", () => MissionActive("M2"));
            for (int i = 0; i < 3; i++) { int k = i; Add("m3_shard" + (i + 1), "m3_shard" + (i + 1), "shard", ObjKind.Use, "Linsenscherbe nehmen", "Take the lens shard", () => MissionActive("M3") && (MissionProgress("M3") & (1 << k)) == 0); }
            Add("m3_lamp", "m3_lamp", "lamp", ObjKind.Use, "Scherben einsetzen", "Fit the shards", () => MissionActive("M3") && Bits(MissionProgress("M3") & 7) >= 3);
            // the 1987 antenna mast in the trench (M4 film target, N2 tape) – stays once found
            Add("m4_mast", "m4_mast", "mast", ObjKind.Scenery, null, null, () => Missions.StateOf("M4") != Missions.Locked || Quests.Get("N2") != null);
            // ---- Dr. Fin's research stakes (K.3)
            string[] sites = { "house_reef", "kelp_forest", "wreck", "blue_drop", "deep_trench", "fog_isle" };
            for (int si = 0; si < sites.Length; si++)
                for (int n = 0; n < 3; n++)
                {
                    string research = "fin_samples_" + sites[si];
                    string flag = "qo:stake:" + sites[si] + ":" + n;
                    Add("stake:" + sites[si] + ":" + n, "stake:" + sites[si] + ":" + n, "stake", ObjKind.Hold, "Probe nehmen", "Take a sample",
                        () => IsResearchOpen(research) && !StoryFlags.Has(flag)).hold = 2f;
                }
            // ---- rivals R4: the sub's air valve
            Add("r4_valve", "r4_sub", "valve_sub", ObjKind.Use, "Luft abgeben (30 %)", "Give air (30 %)", () => StoryFlags.Has(NpcKeys.RivalR4Started) && !StoryFlags.Has(NpcKeys.RivalR4Rescued) && !StoryFlags.Has(NpcKeys.RivalR4Ignored)).offset = new Vector3(1.3f, 0f, -0.4f);
            // ---- village: lantern stand, Ada's kiosk
            Add("lantern_stand", "v_jetty", "lantern_stand", ObjKind.Use, "Laterne loslassen", "Release a lantern", () => Village.IsLanternEvening && !Village.LanternReleasedToday).offset = new Vector3(1.2f, 0f, 0.6f);
            Add("kiosk:coconut_curry", "v_kiosk", "food:coconut_curry", ObjKind.Kiosk, null, null, () => KioskVisible("coconut_curry")).offset = new Vector3(-0.45f, 0.95f, 0f);
            Add("kiosk:sea_grape_salad", "v_kiosk", "food:sea_grape_salad", ObjKind.Kiosk, null, null, () => KioskVisible("sea_grape_salad")).offset = new Vector3(0f, 0.95f, 0f);
            Add("kiosk:ginger_fizz", "v_kiosk", "food:ginger_fizz", ObjKind.Kiosk, null, null, () => KioskVisible("ginger_fizz")).offset = new Vector3(0.45f, 0.95f, 0f);
        }

        ObjDef Add(string id, string spot, string model, ObjKind kind, string de, string en, Func<bool> visible)
        {
            var d = new ObjDef { id = id, spot = spot, model = model, kind = kind, de = de, en = en, visible = visible, yaw = NpcUtil.StableHash(id) % 360 };
            var o = new Obj { def = d, propId = PropIdFor(id) };
            objs.Add(o);
            if (o.propId != null) byPropId[o.propId] = o;
            return d;
        }

        /// World position of a quest object (the props module's when it placed one) – quest-line targets.
        Vector3 ObjectPos(string id)
        {
            var o = FindObj(id);
            if (o != null && o.propId != null && PropQuest.Positions.TryGetValue(o.propId, out var p)) return p;
            return o != null ? QuestSpots.Get(o.def.spot) + o.def.offset : QuestSpots.Get(id);
        }

        /// Quest-line target of a step spot: object spots ("q_kite" …) follow the object, the rest are QuestSpots.
        Vector3 SpotPos(string spot)
        {
            switch (spot)
            {
                case "q_sunglasses": return ObjectPos("q:sunglasses:0");
                case "q_kite": return ObjectPos("q:kite:0");
                case "q_tape": return ObjectPos("q:tape:0");
                case "q_selfie_stick": return ObjectPos("q:selfie_stick:0");
                case "q_liochest": return ObjectPos("liochest:0");
                case "q_paddle": return ObjectPos("q:paddle:0");
                case "q_recorder": return ObjectPos("q:recorder:0");
            }
            return QuestSpots.Get(spot);
        }

        /// A carried quest item of a Bring step (id "q:&lt;item&gt;:&lt;n&gt;"), visible until picked up ("qp:" flag).
        ObjDef Pick(string quest, int n, string item, string spot, string de, string en)
        {
            string flag = PickFlag(quest, n);
            return Add("q:" + item + ":" + n, spot, item, ObjKind.Pickup, de, en, () => SqStepIs(quest, 0) && !StoryFlags.Has(flag));
        }

        /// A collect / hold object of a step (id "&lt;group&gt;:&lt;n&gt;"), visible until taken ("qo:" flag).
        ObjDef Collect(string quest, int n, string group, ObjKind kind, string de, string en)
        {
            string flag = ObjFlag(quest, n);
            return Add(group + ":" + n, group + ":" + n, group, kind, de, en, () => SqStepIs(quest, 0) && !StoryFlags.Has(flag));
        }

        // ---- visibility predicates (shared state → the same on every machine)
        static bool SqStepIs(string id, int step) { var q = Quests.Get(id); return q != null && !q.done && q.step == step; }

        static bool LocalCarrying(string item) => CarryModels.ItemOf(Carry.Current) == item;

        bool CoconutVisible(int k)
        {
            if (LocalCarrying("coconut")) return false;
            bool toni = ToniStep(1) && !StoryFlags.Has(ToniCoconut);
            bool ada = SqStepIs("A1", 0);
            return (toni || ada) && (k < 3 || ada);
        }

        static bool ToniStep(int step) { var q = Quests.Get(Quests.ToniChain); return q != null && !q.done && q.step == step; }
        static bool MissionActive(string id) => Missions.StateOf(id) == Missions.Active;
        static int MissionProgress(string id) { var m = Missions.Get(id); return m != null ? m.progress : 0; }

        bool KioskVisible(string foodId)
        {
            var ada = Actor(NpcDatabase.Ada);
            if (ada == null || !ada.Present || ada.Motion == NpcMotion.Hidden) return false;
            var def = FoodDatabase.Get(foodId);
            if (def == null) return false;
            return string.IsNullOrEmpty(def.unlockQuest) || Quests.IsDone(def.unlockQuest);
        }

        void TickObjects(float dt)
        {
            EnsureObjDefs();
            HookPropQuest();
            if (!propPalmsChecked && WorldInfo.Built)
            {
                propPalmsChecked = true;
                propPalms = FindObjectsByType<PropPalm>(FindObjectsSortMode.None).Length > 0;
            }
            objectsTimer -= dt;
            if (objectsDirty || objectsTimer <= 0f)
            {
                objectsDirty = false;
                objectsTimer = 0.5f;
                for (int i = 0; i < objs.Count; i++)
                {
                    var o = objs[i];
                    bool vis = false;
                    try { vis = o.def.visible(); }
                    catch (Exception) { vis = false; }
                    o.wanted = vis;
                    // the props module placed this object itself (PropQuest) – ours stays hidden
                    if (o.propId != null && PropQuest.Positions.ContainsKey(o.propId)) vis = false;
                    // coconuts come from the real palms when props built them (PropPalm → PropCoconut „Mitnehmen“)
                    if (propPalms && o.def.model == "coconut") vis = false;
                    if (vis && o.go == null) Spawn(o);
                    if (o.go != null && o.go.activeSelf != vis) o.go.SetActive(vis);
                }
            }
            // glints: a faint sparkle within 6 m (finds style), bobbing for floating drums
            Vector3 me = NpcUtil.LocalPlayerPos;
            float t = Time.time;
            for (int i = 0; i < objs.Count; i++)
            {
                var o = objs[i];
                if (o.go == null || !o.go.activeSelf) continue;
                if (o.def.floating)
                {
                    var p = o.go.transform.position;
                    p.y = Waves.HeightAt(p.x, p.z) + 0.15f + Mathf.Sin(t * 1.3f + i) * 0.05f;
                    o.go.transform.position = p;
                }
                if (o.glint == null) continue;
                float d = (o.go.transform.position - me).sqrMagnitude;
                bool on = d < 36f && o.def.kind != ObjKind.Kiosk && o.def.kind != ObjKind.Drop;
                if (o.glint.gameObject.activeSelf != on) o.glint.gameObject.SetActive(on);
                if (on)
                {
                    float s = 0.12f + Mathf.Abs(Mathf.Sin(t * 3f + i)) * 0.12f;
                    o.glint.localScale = new Vector3(s, s, s);
                    if (cam != null) o.glint.rotation = Quaternion.LookRotation(o.glint.position - cam.transform.position);
                }
            }
            TickHold(dt);
        }

        void Spawn(Obj o)
        {
            Vector3 p = QuestSpots.Get(o.def.spot) + o.def.offset;
            GameObject go;
            if (o.def.kind == ObjKind.Scenery)
            {
                go = o.def.model == "mast" ? CarryModels.Mast() : CarryModels.Build(o.def.model);
                go.name = "StoryObj_" + o.def.id;
                go.transform.SetParent(objRoot, false);
                go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, o.def.yaw, 0f));
                NpcVehicleBuilder.AddSolid(go, new Vector3(2f, 11f, 2f), new Vector3(0f, 5.5f, 0f));
                o.go = go;
                return;
            }
            go = CarryModels.Build(o.def.model);
            go.name = "QuestObj_" + o.def.id;
            go.transform.SetParent(objRoot, false);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, o.def.yaw, 0f));
            var col = go.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = o.def.kind == ObjKind.Kiosk ? 0.3f : o.def.kind == ObjKind.Drop ? 1.2f : o.def.model == "drum" ? 0.6f : 0.55f;
            col.center = new Vector3(0f, o.def.kind == ObjKind.Drop ? 0.6f : 0.15f, 0f);
            var qo = go.AddComponent<QuestObject>();
            qo.Init(o.def.id);
            o.comp = qo;
            if (o.def.kind != ObjKind.Kiosk && o.def.kind != ObjKind.Drop)
            {
                var g = NpcMesh.Node(go.transform, "Glint", new Vector3(0f, 0.35f, 0f));
                NpcMesh.Part(g, "Star", NpcMesh.Plate("glint_star", new[] { new Vector2(0f, 1f), new Vector2(0.18f, 0.18f), new Vector2(1f, 0f), new Vector2(0.18f, -0.18f), new Vector2(0f, -1f), new Vector2(-0.18f, -0.18f), new Vector2(-1f, 0f), new Vector2(-0.18f, 0.18f) }, 0.02f),
                    NpcMesh.Glow(LowPoly.Hex("#FFF2B0"), 2.2f), Vector3.zero, Vector3.one, default, false);
                o.glint = g;
                g.gameObject.SetActive(false);
            }
            o.go = go;
        }

        Obj FindObj(string id)
        {
            for (int i = 0; i < objs.Count; i++) if (objs[i].def.id == id) return objs[i];
            return null;
        }

        /// Interaction prompt of a quest object for the local player.
        public static string ObjectPrompt(string id)
        {
            var dir = I;
            var o = dir != null ? dir.FindObj(id) : null;
            if (o == null) return null;
            var d = o.def;
            if (d.kind == ObjKind.Kiosk)
            {
                var food = FoodDatabase.Get(id.Substring(6));
                return food != null ? Prompts.Get(id, food.nameDe + " (" + Loc.Money(food.price) + ")", food.nameEn + " (" + Loc.Money(food.price) + ")") : null;
            }
            if (d.kind == ObjKind.Pickup && Carry.IsCarrying) return Prompts.Get("hands_full", "Hände voll", "Hands full");
            if (d.kind == ObjKind.Hold && dir.holdObj == o) return Prompts.Get("holding", "Halten …", "Hold …");
            return Prompts.Get("obj:" + id, d.de, d.en);
        }

        public static bool ObjectCanInteract(string id)
        {
            var dir = I;
            var o = dir != null ? dir.FindObj(id) : null;
            return o != null && o.go != null && o.go.activeSelf;
        }

        /// The local player pressed E on a quest object.
        public static void ObjectInteract(string id)
        {
            var dir = I;
            var o = dir != null ? dir.FindObj(id) : null;
            if (o == null || o.go == null || !o.go.activeSelf) return;
            dir.LocalObject(o);
        }

        void LocalObject(Obj o)
        {
            var d = o.def;
            switch (d.kind)
            {
                case ObjKind.Pickup:
                    if (Carry.IsCarrying) { Notify(NotifyKind.Warning, "Hände voll", "Hands full"); return; }
                    if (Carry.Pick(d.id))
                    {
                        if (d.model != "coconut") o.go.SetActive(false);
                        NpcNet.Request("obj", d.id, "take");
                        GameEvents.RaiseNotify(NotifyKind.Quest, CarryModels.NameOf(CarryModels.ItemOf(d.id)), Loc.Tr("getragen", "carried"));
                    }
                    break;
                case ObjKind.Collect:
                    o.go.SetActive(false);
                    NpcNet.Request("obj", d.id, "collect");
                    break;
                case ObjKind.Hold:
                    holdObj = o;
                    holdT = 0f;
                    break;
                case ObjKind.Use:
                    if (d.id == "r4_valve")
                    {
                        if (!NpcUtil.PlayerValid(out var p) || p.Air01 < 0.3f) { Notify(NotifyKind.Warning, "Zu wenig Luft (30 %)", "Not enough air (30 %)"); return; }
                    }
                    if (d.id == "lantern_stand")
                    {
                        if (Village.ReleaseLantern()) { AddReleasedLantern(); NpcNet.Request("evt", "lantern"); }
                        objectsDirty = true;
                        return;
                    }
                    o.go.SetActive(false);
                    NpcNet.Request("obj", d.id, "use");
                    break;
                case ObjKind.Kiosk:
                {
                    string food = d.id.Substring(6);
                    // BuyAtKiosk already says why it failed („Zu wenig Geld · 45 $“) – one line, not two
                    if (Pantry.BuyAtKiosk(food))
                    {
                        var f = FoodDatabase.Get(food);
                        GameEvents.RaiseNotify(NotifyKind.Info, f != null ? f.Name : food, Loc.Tr("im Kühlschrank", "in the fridge"));
                    }
                    break;
                }
                case ObjKind.Drop:
                {
                    string carried = Carry.Current;
                    if (string.IsNullOrEmpty(carried)) return;
                    deliveringCarry = carried;
                    Carry.Drop();
                    deliveringCarry = null;
                    NpcNet.Request("obj", d.id, "deliver", carried);
                    break;
                }
            }
        }

        void TickHold(float dt)
        {
            if (holdObj == null) return;
            bool ok = holdObj.go != null && holdObj.go.activeSelf && GameInput.Held(GameAction.Interact)
                      && (holdObj.go.transform.position - NpcUtil.LocalPlayerPos).sqrMagnitude < 16f;
            if (!ok) { holdObj = null; holdT = 0f; StoryUI.SkipProgress = 0f; return; }
            holdT += dt;
            StoryUI.SkipProgress = holdT / Mathf.Max(0.5f, holdObj.def.hold);
            if (holdT >= holdObj.def.hold)
            {
                var o = holdObj;
                holdObj = null;
                holdT = 0f;
                StoryUI.SkipProgress = 0f;
                o.go.SetActive(false);
                NpcNet.Request("obj", o.def.id, "collect");
            }
        }

        /// HOST: a player used a quest object.
        void HostObject(int fromId, string id, string op)
        {
            if (string.IsNullOrEmpty(id)) return;
            objectsDirty = true;
            trackerDirty = true;
            // missions
            if (id.StartsWith("m1_", StringComparison.Ordinal) || id.StartsWith("m2_", StringComparison.Ordinal) || id.StartsWith("m3_", StringComparison.Ordinal)) { MissionObject(id); return; }
            if (id == "r4_valve") { RivalR4Rescue(); return; }
            if (id.StartsWith("stake:", StringComparison.Ordinal))
            {
                int b = id.LastIndexOf(':');
                string site = id.Substring(6, b - 6);
                var def = ResearchDatabase.Get("fin_samples_" + site);
                if (def == null || !IsResearchOpen(def.id)) return;
                string flag = "qo:" + id;
                if (StoryFlags.Has(flag)) return;
                StoryFlags.Set(flag);
                int have = 0;
                for (int k = 0; k < 3; k++) if (StoryFlags.Has("qo:stake:" + site + ":" + k)) have++;
                if (have >= def.count) CompleteResearch(def);
                else { Research.SetProgress(def.id, have); GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Wasserprobe", "Water sample"), have + "/" + def.count); }
                return;
            }
            if (id == "valve" || id == "coil")
            {
                var q = Quests.Get(Quests.ToniChain);
                if (q == null || q.done || q.step != 3) return;
                StoryFlags.Set(id == "valve" ? ToniValve : ToniCoil);
                int p = (StoryFlags.Has(ToniValve) ? 1 : 0) + (StoryFlags.Has(ToniCoil) ? 1 : 0);
                Quests.SetProgress(Quests.ToniChain, 3, p);
                GameEvents.RaiseNotify(NotifyKind.Quest, id == "valve" ? Loc.Tr("Funkröhre", "Radio valve") : Loc.Tr("Kupferspule", "Copper coil"), p + "/2");
                return;
            }
            if (id == "q:recorder:0" && op == "take")
            {
                if (ToniStep(2)) StoryFlags.Set(ToniRecorderPicked);
                return;
            }
            if (id.StartsWith("drop:", StringComparison.Ordinal) && op == "deliver") { HostDropZone(id); return; }
            // side quest objects: "<group>:<n>" (collect / hold) or "q:<item>:<n>" (pickup)
            string group = id, item = null;
            int n = 0;
            if (id.StartsWith("q:", StringComparison.Ordinal)) { item = CarryModels.ItemOf(id); int c = id.LastIndexOf(':'); n = NpcNet.ParseI(id.Substring(c + 1)); }
            else { int c = id.LastIndexOf(':'); if (c > 0) { group = id.Substring(0, c); n = NpcNet.ParseI(id.Substring(c + 1)); } }
            var s = S;
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (item != null && st.kind == SqStep.Bring && ContainsItem(st.target, item) && op == "take")
                {
                    if (item == "coconut") return;   // coconuts are not tracked (any palm gives one)
                    StoryFlags.Set(PickFlag(d.id, n));
                    return;
                }
                if (item == null && (st.kind == SqStep.Collect || st.kind == SqStep.Hold) && st.target == group)
                {
                    string flag = ObjFlag(d.id, n);
                    if (StoryFlags.Has(flag)) return;
                    StoryFlags.Set(flag);
                    CountFlagsAndAdvance(d, q, st, CountOf(group));
                    return;
                }
            }
        }

        /// Number of objects of a collect group (flags 0..n−1).
        static int CountOf(string group)
        {
            switch (group)
            {
                case "peg": case "ghostnet": return 3;
                case "driftwood": return 5;
                case "seagrape": return 6;
                default: return 1;
            }
        }

        void HostDropZone(string zone)
        {
            string quest = zone == "drop:hangar" ? "W1" : zone == "drop:boatshed" ? "M2" : null;
            var q = Quests.Get(quest);
            var d = SideQuestDatabase.Get(quest);
            if (q == null || q.done || d == null || q.step >= d.steps.Length) return;
            var st = d.steps[q.step];
            int delivered = Delivered(q) + 1;
            if (delivered >= st.count) AdvanceSideQuest(d, q, null);
            else
            {
                Quests.SetProgress(d.id, q.step, delivered);
                GameEvents.RaiseNotify(NotifyKind.Quest, d.Title, delivered + "/" + st.count);
            }
        }

        /// A carried quest item was lost (fainting, dropped by another module) → it reappears at its spot.
        void HostObjectLost(string carryId)
        {
            string item = CarryModels.ItemOf(carryId);
            if (item == null) return;
            int c = carryId.LastIndexOf(':');
            int n = NpcNet.ParseI(carryId.Substring(c + 1));
            if (item == "recorder")
            {
                if (ToniStep(2)) StoryFlags.Clear(ToniRecorderPicked);
                return;
            }
            var s = S;
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind == SqStep.Bring && ContainsItem(st.target, item))
                {
                    StoryFlags.Clear(PickFlag(d.id, n));
                    objectsDirty = true;
                    return;
                }
            }
        }

        /// HOST, session start (new game / load / continue – HostSessionStart): nobody carries anything now, but the save
        /// may hold "qp:" pickup flags of items that were in someone's hands (they hide the object) and stale
        /// PlayerRecord.carried values. Releases them so the item respawns at its spot – otherwise the Bring step (and
        /// for Toni's recorder the Fog Isle) is soft-locked.
        void ReleaseOrphanedPickups()
        {
            var s = S;
            if (s == null || !Net.HasAuthority) return;
            for (int i = 0; i < s.players.Count; i++) if (s.players[i] != null) s.players[i].carried = null;
            string hostCarry = Carry.Current;
            // Toni's recorder (T3)
            if (ToniStep(2) && StoryFlags.Has(ToniRecorderPicked) && hostCarry != "q:recorder:0") StoryFlags.Clear(ToniRecorderPicked);
            // side-quest Bring steps: set pickup flags − delivered (W1 drums keep their flag after the hangar) − carried
            for (int i = 0; i < s.sideQuests.Count; i++)
            {
                var q = s.sideQuests[i];
                if (q == null || q.done || q.id == Quests.ToniChain) continue;
                var d = SideQuestDatabase.Get(q.id);
                if (d == null || q.step >= d.steps.Length) continue;
                var st = d.steps[q.step];
                if (st.kind != SqStep.Bring || string.IsNullOrEmpty(st.target) || st.target.IndexOf(',') >= 0) continue;
                string prefix = "q:" + st.target + ":";
                bool hostHolds = hostCarry != null && hostCarry.StartsWith(prefix, StringComparison.Ordinal);
                int set = 0;
                for (int n = 0; n < st.count; n++) if (StoryFlags.Has(PickFlag(d.id, n))) set++;
                int surplus = set - Delivered(q) - (hostHolds ? 1 : 0);
                for (int n = st.count - 1; n >= 0 && surplus > 0; n--)
                {
                    string f = PickFlag(d.id, n);
                    if (!StoryFlags.Has(f) || (hostHolds && hostCarry == prefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture))) continue;
                    StoryFlags.Clear(f);
                    surplus--;
                }
            }
            objectsDirty = true;
            trackerDirty = true;
        }

        // ================================================================ local evaluators (every machine)
        float recNightReef, recCanoe, recMast, listenWhale, scanTimer;
        bool whaleNear;

        void LocalTick(float dt)
        {
            LocalStoryTick();
            TickLanterns(dt);
            TickDressing();
            TickWatchers(dt);
        }

        /// A carried quest item left the hands without a hand-in (fainting, another module dropped it) → the host puts
        /// the object back at its spot.
        void OnCarryChanged(string oldId, string newId)
        {
            if (!InGame || string.IsNullOrEmpty(oldId) || oldId == deliveringCarry || !string.IsNullOrEmpty(newId)) return;
            string item = CarryModels.ItemOf(oldId);
            if (item == null || item == "coconut" || item == "grilled_fish") return;
            NpcNet.Request("evt", "lost", oldId);
            objectsDirty = true;
        }

        /// Gallery photo taken on THIS machine → what is in the frame → host.
        void OnPhoto(PhotoEntry e)
        {
            if (!InGame || e == null) return;
            if (!NpcUtil.PlayerValid(out var p)) return;
            var c = p.View != null ? p.View : cam;
            if (c == null) return;
            var sb = new System.Text.StringBuilder(96);
            void Tag(string t) { if (sb.Length > 0) sb.Append(','); sb.Append(t); }
            Vector3 cp = c.transform.position;
            bool onBoat = Refs.Boat != null && !(Refs.Boat is UnityEngine.Object bo && bo == null) && Refs.Boat.PlayerAboard;
            if (p.IsInWater || onBoat) Tag("water");
            if (SelfieState.Active) Tag("selfie");
            if (p.FlashlightOn) Tag("lamp");
            if (c.transform.forward.y < -0.7f) Tag("pitchdown");
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (a == null || !a.Present || !a.gameObject.activeSelf) continue;
                if (InView(c, a.Position + Vector3.up * (a.Def.height * 0.7f), 14f)) Tag("npc:" + a.Id);
            }
            if (gull != null && gull.Root.gameObject.activeSelf && InView(c, gull.Root.position, 30f)) Tag("guenther");
            if (InView(c, WorldLayout.Lighthouse + Vector3.up * 18f, 280f)) Tag("lm_lighthouse");
            if (InView(c, WorldLayout.Hut + Vector3.up * 3f, 220f)) Tag("lm_hut");
            if (InView(c, WorldLayout.Village + Vector3.up * 3f, 260f)) Tag("lm_village");
            int lan = LanternsInView(c);
            if (lan > 0) Tag("lanterns:" + lan);
            if (ChumActive && vehicles.Length > VYacht && vehicles[VYacht].Present && InView(c, vehicles[VYacht].Pos + Vector3.up * 2f, 180f, false)) Tag("yacht_chum");
            NpcNet.Request("evt", "photo", e.speciesId ?? "", sb.ToString(), NpcNet.F(p.Depth));
        }

        static readonly RaycastHit[] viewHits = new RaycastHit[8];

        /// In the camera frustum (5 % margin), within dist, and not hidden behind world geometry.
        static bool InView(Camera c, Vector3 p, float dist, bool occlusion = true)
        {
            Vector3 v = c.WorldToViewportPoint(p);
            if (v.z < 0.3f || v.z > dist || v.x < 0.05f || v.x > 0.95f || v.y < 0.05f || v.y > 0.95f) return false;
            if (!occlusion) return true;
            Vector3 from = c.transform.position, d = p - from;
            float len = d.magnitude;
            int n = Physics.RaycastNonAlloc(new Ray(from, d / len), viewHits, Mathf.Max(0f, len - 0.6f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var col = viewHits[i].collider;
                if (col == null || col is CharacterController) continue;
                if (col.GetComponentInParent<NpcActor>() != null) continue;
                return false;
            }
            return true;
        }

        void OnClipRecorded(FootageClip clip)
        {
            if (!InGame || clip == null || string.IsNullOrEmpty(clip.speciesId)) return;
            var sb = new System.Text.StringBuilder(48);
            sb.Append("crowd:").Append(clip.crowd).Append(",tier:").Append(clip.tier).Append(",q:").Append(Mathf.RoundToInt(clip.quality * 100f));
            if (clip.selfie) sb.Append(",selfie");
            if (BoatTelemetry.Valid && BoatTelemetry.Speed > 6f && NpcUtil.FlatDist(BoatTelemetry.Position, NpcUtil.LocalPlayerPos) < 15f) sb.Append(",boatfast");
            NpcNet.Request("evt", "clip", clip.speciesId, sb.ToString(), NpcNet.F(clip.seconds));
        }

        /// Recording conditions only this machine sees: night reef (J2), the canoe race (M3), the antenna mast (M4),
        /// staying with the singing whale (research).
        void TickWatchers(float dt)
        {
            var hc = Refs.HandCamera;
            bool rec = hc != null && !(hc is UnityEngine.Object ho && ho == null) && hc.IsRecording;
            if (!NpcUtil.PlayerValid(out var p)) return;
            Vector3 me = p.Root.position;
            var c = p.View != null ? p.View : cam;
            if (rec && c != null)
            {
                if (p.IsUnderwater && Between(Hour, 21f, 4f) && SqStepIs("J2", 0))
                {
                    var site = WorldInfo.SiteAt(me);
                    if (site != null && site.id == "house_reef") recNightReef += dt;
                }
                if (RaceRunning && SqStepIs("M3", 1))
                    for (int i = VCanoe1; i <= VCanoe2 && i < vehicles.Length; i++)
                        if (vehicles[i].Present && InView(c, vehicles[i].Pos + Vector3.up * 0.6f, 90f, false)) { recCanoe += dt * 0.5f; }
                if (MissionActive("M4") && InView(c, QuestSpots.Get("m4_mast") + Vector3.up * 5f, 22f, false)) recMast += dt;
            }
            if (recNightReef >= 5f) { NpcNet.Request("evt", "rec", "night_reef", NpcNet.F(recNightReef)); recNightReef = 0f; }
            if (recCanoe >= 4f) { NpcNet.Request("evt", "rec", "canoe", NpcNet.F(recCanoe)); recCanoe = 0f; }
            if (recMast >= 5f) { NpcNet.Request("evt", "rec", "mast", NpcNet.F(recMast)); recMast = 0f; ShowEyeShadow(); }
            // whale song: within 30 m of the humpback (scan twice a second)
            scanTimer -= dt;
            if (scanTimer <= 0f)
            {
                scanTimer = 0.5f;
                whaleNear = false;
                if (IsResearchOpen("fin_listen_whale") && p.IsUnderwater)
                {
                    var list = CreatureRegistry.Active;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var cr = list[i];
                        if (cr == null || cr.Species == null || cr.Species.id != "humpback") continue;
                        if ((cr.Position - me).sqrMagnitude < 900f) { whaleNear = true; break; }
                    }
                }
            }
            if (whaleNear) listenWhale += dt;
            if (listenWhale >= 5f) { NpcNet.Request("evt", "listen", "humpback", NpcNet.F(listenWhale)); listenWhale = 0f; }
        }

        // ---- M4: the Big Eye's shadow passes behind the mast (scripted, unfilmable)
        GameObject eyeShadow;
        float eyeT = -1f;

        void ShowEyeShadow()
        {
            Vector3 mast = QuestSpots.Get("m4_mast");
            if (eyeShadow == null)
            {
                eyeShadow = new GameObject("BigEyeShadow");
                eyeShadow.transform.SetParent(npcRoot, false);
                NpcMesh.Part(eyeShadow.transform, "Shadow", NpcMesh.Sphere(14, 24), NpcMesh.Mat(new Color(0.04f, 0.05f, 0.09f), 0.05f), Vector3.zero, new Vector3(9f, 6f, 9f), default, false);
                NpcMesh.Part(eyeShadow.transform, "Glint", NpcMesh.Sphere(10, 16), NpcMesh.Glow(LowPoly.Hex("#F2C94C"), 0.9f), new Vector3(0f, 0.5f, -4.2f), new Vector3(1.6f, 2.4f, 0.4f), default, false);
            }
            eyeShadow.SetActive(true);
            eyeShadow.transform.position = mast + new Vector3(-40f, 6f, 30f);
            eyeT = 0f;
        }

        void TickEyeShadow(float dt)
        {
            if (eyeShadow == null || eyeT < 0f) return;
            eyeT += dt;
            Vector3 mast = QuestSpots.Get("m4_mast");
            eyeShadow.transform.position = mast + new Vector3(-40f + eyeT * 8f, 6f, 30f);
            if (eyeT > 10f) { eyeShadow.SetActive(false); eyeT = -1f; }
        }

        /// K.8 T2 „Grillfisch zum Mitnehmen“ is handled by the props grill (PropQuest.CarryWanted → CarryWanted above);
        /// eating stays a normal buff.
        void OnFoodEaten(FoodDef f) { }

        // ================================================================ quest line feed (every machine, L.10)
        void TickTracker(float dt)
        {
            TickEyeShadow(dt);
            trackerTimer -= dt;
            if (!trackerDirty && trackerTimer > 0f) return;
            trackerDirty = false;
            trackerTimer = 1f;
            FeedStory();
            FeedSide();
        }

        void FeedStory()
        {
            var s = S;
            if (s == null) return;
            string line = null, prog = null, id = null;
            Vector3? target = null;
            NotifyKind kind = NotifyKind.Story;
            if (!s.prologueDone)
            {
                id = "story:prologue";
                line = Loc.Tr("Willkommen auf Rostbucht!", "Welcome to Rust Cove!");
                var h = Actor(NpcDatabase.Horst);
                if (h != null && h.Present) target = h.Position;
            }
            else if (StoryFlags.Has(StoryKeys.ToniArrived) && !StoryFlags.Has(StoryKeys.FogMap))
            {
                id = "story:toni";
                var q = Quests.Get(Quests.ToniChain);
                int step = q != null ? q.step : 0;
                var st = SideQuestDatabase.ToniChain.steps[Mathf.Clamp(step, 0, 3)];
                line = st.Line;
                if (step == 1) prog = q.progress + "/2";
                if (step == 3) prog = (q.progress + (s.ownedItems.Contains("radio_battery") ? 1 : 0)) + "/3";
                target = step == 2 ? ObjectPos("q:recorder:0") : step == 3 ? (Vector3?)null : QuestSpots.Get("driftwood");
                if (step == 2 && StoryFlags.Has(ToniRecorderPicked)) target = QuestSpots.Get("driftwood");
            }
            else if (Missions.Current != null)
            {
                var m = Missions.Current;
                id = "gus:" + m.id;
                line = MissionLine(m.id, m.progress, out target);
                if (m.id == "M3" && Bits(m.progress & 7) < 3) prog = Bits(m.progress & 7) + "/3";
            }
            else if (StoryFlags.Has(NpcKeys.RivalR4Started) && !StoryFlags.Has(NpcKeys.RivalR4Rescued) && !StoryFlags.Has(NpcKeys.RivalR4Ignored))
            {
                id = "story:r4";
                line = Loc.Tr("Den Brüdern Luft bringen (85 m)", "Bring the brothers air (85 m)");
                target = QuestSpots.Get("r4_sub");
            }
            else if (ChumActive)
            {
                id = "story:r3";
                line = Loc.Tr("Foto: Yacht beim Ködern am Wrack", "Photo: the yacht chumming at the wreck");
                target = vehicles[VYacht].Pos;
            }
            else if (StoryFlags.Has(NpcKeys.RivalR2Started) && !StoryFlags.Has(NpcKeys.RivalR2Done))
            {
                id = "story:r2";
                line = Loc.Tr("Gerald 20 s filmen + ehrlich hochladen", "Film Gerald 20 s + honest upload");
            }
            QuestTracker.SetEntry(QuestSource.Story, id, line, prog, target, kind);
        }

        void FeedSide()
        {
            var s = S;
            if (s == null) return;
            QuestState pick = null;
            if (!string.IsNullOrEmpty(s.trackedQuest))
            {
                var t = Quests.Get(s.trackedQuest);
                if (t != null && !t.done && t.id != Quests.ToniChain) pick = t;
            }
            if (pick == null)
                for (int i = 0; i < s.sideQuests.Count; i++)
                {
                    var q = s.sideQuests[i];
                    if (q != null && !q.done && q.id != Quests.ToniChain) { pick = q; break; }
                }
            if (pick == null) { QuestTracker.SetEntry(QuestSource.Side, null, null, null, null); return; }
            var d = SideQuestDatabase.Get(pick.id);
            if (d == null || pick.step >= d.steps.Length) { QuestTracker.SetEntry(QuestSource.Side, null, null, null, null); return; }
            var st = d.steps[pick.step];
            string prog = null;
            switch (st.kind)
            {
                case SqStep.Bring: if (st.count > 1) prog = Delivered(pick) + "/" + st.count; break;
                case SqStep.Collect: case SqStep.Hold: case SqStep.Film: if (st.count > 1) prog = pick.progress + "/" + st.count; break;
                case SqStep.Photo: if (st.count > 1) prog = pick.progress + "/" + st.count; break;
                case SqStep.Record: prog = pick.progress + "/" + Mathf.RoundToInt(st.a) + " s"; break;
            }
            Vector3? target = null;
            if (st.kind == SqStep.Talk || st.kind == SqStep.Choice || (st.kind == SqStep.Bring && LocalCarrying(FirstItem(st.target)) && st.spot != "hangar" && st.spot != "v_boatshed"))
            {
                var g = Actor(d.giver);
                if (g != null && g.Present) target = g.Position;
            }
            else if (st.kind == SqStep.Bring && LocalCarrying(FirstItem(st.target)) && st.spot != null) target = SpotPos(st.spot);
            else if (st.spot != null) target = SpotPos(st.spot);
            QuestTracker.SetEntry(QuestSource.Side, d.id, st.Line, prog, target, NotifyKind.Quest);
        }

        static string FirstItem(string csv)
        {
            if (string.IsNullOrEmpty(csv)) return null;
            int c = csv.IndexOf(',');
            return c < 0 ? csv : csv.Substring(0, c);
        }
    }

    /// Interaction surface of a quest object (E prompt routed to NpcDirector).
    public sealed class QuestObject : MonoBehaviour, IInteractable
    {
        string id;
        public string Id => id;
        public void Init(string objectId) { id = objectId; }
        public string GetPrompt() => NpcDirector.ObjectPrompt(id);
        public bool CanInteract() => NpcDirector.ObjectCanInteract(id);
        public void Interact() => NpcDirector.ObjectInteract(id);
    }
}
