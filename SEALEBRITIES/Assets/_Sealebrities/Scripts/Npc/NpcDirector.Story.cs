using System;
using UnityEngine;

namespace Sealebrities
{
    /// Story part of the director: K.5 prologue, A Horst (tutorial beats, schedule, idle gags, reactions),
    /// B.1 Gus' radio calls and B.2 his missions M1–M4.
    public sealed partial class NpcDirector
    {
        // ================================================================ K.5 prologue (HOST)
        enum Pro { None, WaitStart, PadWanda, PadHorstRun, Letter, Final, Walk, HutWait, Done }
        Pro pro = Pro.None;
        float proT;
        bool proPad, letterClosedHost;
        bool prologueActive => pro != Pro.None && pro != Pro.Done;

        void ResetStorySession()
        {
            pro = Pro.None;
            proT = 0f;
            letterClosedHost = false;
            gagTimer = 15f;
            gagState = 0;
            lastGag = -1;
            lastFallDay = -1;
            beat9Chase = 0f;
            missionSeen.Clear();
        }

        void HostSessionStart()
        {
            var gm = GameManager.I;
            if (gm == null) return;
            if (gm.NeedsPrologue)
            {
                bool forced = Array.IndexOf(Environment.GetCommandLineArgs(), "-prologue") >= 0;
                if (GameManager.AutomatedRun && !forced)
                {
                    // automated test runs start in the hut as before; the prologue is skipped silently
                    StoryFlags.Set(StoryKeys.GusLetterRead);
                    StoryFlags.Set(StoryKeys.PrologueDone);
                    pro = Pro.Done;
                    Debug.Log("[Npc] prologue skipped (automated run)");
                }
                else { pro = Pro.WaitStart; proT = 0f; }
            }
            else pro = Pro.Done;
            HostVisitorStart();
            HostVillageStart();
            ReleaseOrphanedPickups();
        }

        void ClientSessionStart()
        {
            // clients remember which missions they already heard about (radio calls follow shared state changes)
            for (int i = 0; i < MissionIds.Length; i++) missionSeen[MissionIds[i]] = Missions.StateOf(MissionIds[i]);
            seenFlagsCount = S != null ? S.storyFlags.Count : 0;
        }

        void ThinkPrologue()
        {
            if (pro == Pro.None || pro == Pro.Done) return;
            var horst = Actor(NpcDatabase.Horst);
            var wanda = Actor(NpcDatabase.Wanda);
            var chopper = vehicles.Length > VChopper ? vehicles[VChopper] : null;
            proT += 0.1f;
            switch (pro)
            {
                case Pro.WaitStart:
                {
                    if (proT < 1.2f || !WorldInfo.Built || !NpcUtil.PlayerValid(out var p)) return;
                    Vector3 me = p.Root.position;
                    proPad = NpcUtil.FlatDist(me, WorldLayout.Helipad) < 35f;
                    var st = S;
                    if (!proPad && st != null && st.day <= 1 && st.questIndex == 0 && !Net.IsOnline)
                    {
                        // fallback while the player module has no chopper arrival yet: step out at the helipad (K.5)
                        Vector3 spawn = QuestSpots.Land(WorldLayout.NewGameSpawn, 0.5f, 12f);
                        p.TeleportTo(spawn + Vector3.up * 0.05f, WorldLayout.NewGameSpawnYaw);
                        proPad = true;
                    }
                    if (proPad)
                    {
                        if (chopper != null)
                        {
                            Vector3 pad = QuestSpots.Get("helipad");
                            chopper.Place(pad + new Vector3(-1.5f, 0.02f, -4.5f), 200f);
                            chopper.Present = true;
                            chopper.RotorSpeed = 700f;
                        }
                        if (wanda != null)
                        {
                            wanda.SetPresent(true);
                            wanda.Place(QuestSpots.Get("wanda_pad"), 30f);
                            wanda.Motion = NpcMotion.Idle;
                        }
                        if (horst != null)
                        {
                            horst.SetPresent(true);
                            horst.Place(QuestSpots.Get("horst_trail"), 220f);
                        }
                        StoryFlags.Set(NpcKeys.WandaMet);
                        pro = Pro.PadWanda;
                        proT = 0f;
                    }
                    else
                    {
                        if (horst != null)
                        {
                            horst.SetPresent(true);
                            horst.Place(QuestSpots.Get("horst_porch"), 250f);
                        }
                        pro = Pro.HutWait;
                        proT = 0f;
                    }
                    break;
                }
                case Pro.PadWanda:
                    if (wanda == null) { pro = Pro.PadHorstRun; break; }
                    wanda.FacePoint = NearestDiverPos(wanda.Position);
                    if (AnyoneNear(wanda.Position, 9f) || (proT > 6f && AnyoneNear(wanda.Position, 28f)))
                    {
                        wanda.PlayGesture(NpcGesture.Nod);
                        bool started = Talk(wanda, "pro.p1", () =>
                        {
                            pro = Pro.PadHorstRun;
                            proT = 0f;
                            if (horst != null)
                            {
                                horst.GoTo(QuestSpots.Get("horst_helipad"), 4.2f);
                                horst.PlayGesture(NpcGesture.Flail);
                            }
                        });
                        if (started) { pro = Pro.Final; proStage = 1; }   // parked while the lines play (the callback moves on)
                    }
                    break;
                case Pro.PadHorstRun:
                    if (horst == null || !horst.Moving || proT > 25f)
                    {
                        if (horst != null)
                        {
                            horst.Stop();
                            horst.FacePoint = NearestDiverPos(horst.Position);
                        }
                        proStage = 2;
                        pro = Pro.Final;
                        if (!Talk(horst, "pro.p2", BeginLetter)) BeginLetter();
                    }
                    break;
                case Pro.HutWait:
                    if (horst != null && AnyoneNear(horst.Position, 9f))
                    {
                        horst.FacePoint = NearestDiverPos(horst.Position);
                        horst.PlayGesture(NpcGesture.Nod);
                        proStage = 2;
                        pro = Pro.Final;
                        if (!Talk(horst, "pro.p2", BeginLetter)) BeginLetter();
                    }
                    break;
                case Pro.Letter:
                    if (letterClosedHost || proT > 45f || StoryFlags.Has(StoryKeys.GusLetterRead))
                    {
                        if (StoryUI.LetterOpen) StoryUI.CloseLetter();
                        StoryFlags.Set(StoryKeys.GusLetterRead);
                        pro = Pro.Final;
                        proStage = 3;
                        if (horst != null) horst.PlayGesture(NpcGesture.TaDa);
                        if (!Talk(horst, proPad ? "pro.p3" : "pro.p3hut", FinishPrologue)) FinishPrologue();
                    }
                    break;
                case Pro.Walk:
                    if (chopper != null && chopper.Present && proT > 2f && !chopper.Moving && chopper.Pos.y > 20f)
                    {
                        chopper.Present = false;
                        chopper.RotorSpeed = 0f;
                    }
                    if ((horst == null || !horst.Moving) && (chopper == null || !chopper.Present)) pro = Pro.Done;
                    if (proT > 60f) pro = Pro.Done;
                    break;
            }
        }

        int proStage;

        void BeginLetter()
        {
            pro = Pro.Letter;
            proT = 0f;
            letterClosedHost = false;
            ShowLetterLocal();
            NpcNet.SendEvent(NpcNet.EvLetter, null, null);
        }

        void ShowLetterLocal()
        {
            string title = DialogueDatabase.Resolve("pro.letter.title", null);
            string body = DialogueDatabase.Resolve("pro.letter.body", null);
            StoryUI.ShowLetter(title, body, () => { if (Net.HasAuthority) letterClosedHost = true; else NpcNet.Request("letter"); });
        }

        void OnLetterClosed() { letterClosedHost = true; }

        void FinishPrologue()
        {
            StoryFlags.Set(StoryKeys.PrologueDone);
            Notify(NotifyKind.Story, "Deine Ziele", "Your goals", Loc.Tr("Handy → Aufgaben", "Phone → Tasks"));
            var horst = Actor(NpcDatabase.Horst);
            var wanda = Actor(NpcDatabase.Wanda);
            var chopper = vehicles.Length > VChopper ? vehicles[VChopper] : null;
            if (horst != null) horst.GoTo(QuestSpots.Get("horst_porch"), 2.2f);
            if (proPad && wanda != null && chopper != null && chopper.Present)
            {
                Talk(wanda, "pro.bye", null);
                wanda.GoTo(chopper.Pos + new Vector3(1.5f, 0f, 1.5f), 1.6f, () =>
                {
                    wanda.SetPresent(false);
                    chopper.RotorSpeed = 1300f;
                    chopper.SailTo(chopper.Pos + new Vector3(-160f, 45f, -180f), 230f, 14f);
                    proT = 0f;
                });
            }
            pro = Pro.Walk;
            proT = 0f;
        }

        /// Hold E during the prologue / a "skip" with anchor −2: everything done at once.
        void SkipPrologue()
        {
            if (!prologueActive) return;
            var horst = Actor(NpcDatabase.Horst);
            var wanda = Actor(NpcDatabase.Wanda);
            for (int i = convs.Count - 1; i >= 0; i--)
                if (convs[i].anchor == horst || convs[i].anchor == wanda) convs.RemoveAt(i);
            if (horst != null) horst.Flags = 0;
            if (wanda != null) { wanda.Flags = 0; wanda.SetPresent(false); }
            if (StoryUI.LetterOpen) StoryUI.CloseLetter();
            StoryFlags.Set(StoryKeys.GusLetterRead);
            StoryFlags.Set(StoryKeys.PrologueDone);
            var chopper = vehicles.Length > VChopper ? vehicles[VChopper] : null;
            if (chopper != null) { chopper.Present = false; chopper.RotorSpeed = 0f; }
            if (horst != null) horst.Place(QuestSpots.Get("horst_porch"), 250f);
            pro = Pro.Done;
        }

        Vector3? NearestDiverPos(Vector3 p)
        {
            var t = Divers.Nearest(p, out float d);
            return t != null ? t.position : (Vector3?)null;
        }

        // ================================================================ A Horst (HOST)
        float gagTimer, gagT;
        int gagState, lastGag = -1, lastFallDay = -1;
        float beat9Chase;
        int beatPlaying = -1;

        /// Round 3 (GDD §20 Q10 „nicht am Steg – da nervt er“): Horst explains the dock tasks from the beach beside the
        /// dock's landward end, never on the planks.
        static readonly string[] BeatSpot = { "horst_porch", "horst_dockside", "horst_dockside", "horst_dockside", "horst_helipad", "horst_dockside", "horst_dockside", "horst_dockside", "horst_campfire" };
        float horstWakeUntil;

        bool TutorialRunning => S != null && S.questIndex < TutorialQuests.Finished;

        /// Tutorial step → Horst's beat (A.3 triggers): b5 = camera + "3 species" (steps 5 and 6 share it), b6 =
        /// "WUNDERSCHÖN" + upload how-to (step 7), b7 = bed from 18:00 (step 8). b8 plays on the first morning with
        /// views (day-2 branch), never inside the tutorial. BeatSpot stays indexed by step.
        static readonly int[] StepBeat = { 0, 1, 2, 3, 4, 5, 5, 6, 7 };
        static int BeatFor(int step) => StepBeat[Mathf.Clamp(step, 0, StepBeat.Length - 1)];

        void ThinkHorst()
        {
            var horst = Actor(NpcDatabase.Horst);
            if (horst == null) return;
            if (prologueActive) return;
            var s = S;
            if (s == null) return;
            if (gagState >= 60) { ThinkFallGag(horst); return; }

            // tutorial done → Horst's lure hat (A.3; core's cosmetic rule "tutorial" unlocks hat 10 and notifies)
            if (s.questIndex >= TutorialQuests.Finished && !StoryFlags.Has(StoryKeys.TutorialDone))
                StoryFlags.Set(StoryKeys.TutorialDone);

            float h = Hour;
            horst.SetPresent(true);
            if (!horst.Placed)
            {
                // first appearance this session (loaded game / prologue skipped): start where he belongs right now
                string first = TutorialRunning ? BeatSpot[Mathf.Clamp(s.questIndex, 0, BeatSpot.Length - 1)]
                    : Between(h, 19f, 22f) ? "horst_campfire" : "horst_bench";
                horst.Place(QuestSpots.Get(first), 90f);
            }
            if (IsTalking(horst) || horst.HasOffer)
            {
                horst.Stop();
                if (horst.Motion == NpcMotion.Lie) { horst.Place(QuestSpots.Get("horst_home"), 90f); horst.Motion = NpcMotion.Idle; }
                horst.FacePoint = NearestDiverPos(horst.Position);
                return;
            }

            // ---- tutorial: walk ahead to the place of the current task, deliver the beat
            if (TutorialRunning)
            {
                int step = Mathf.Clamp(s.questIndex, 0, BeatSpot.Length - 1);
                Vector3 spot = QuestSpots.Get(BeatSpot[step]);
                if (s.questIndex == 7 && h >= 18f) spot = QuestSpots.Get("horst_porch");
                if (NpcUtil.FlatDist(horst.Position, spot) > 1.2f)
                {
                    if (!horst.Moving || NpcUtil.FlatDist(horst.Destination, spot) > 0.5f) horst.GoTo(spot, 2.2f);
                    return;
                }
                horst.Motion = NpcMotion.Idle;
                horst.FacePoint = NearestDiverPos(horst.Position);
                int beat = BeatFor(s.questIndex);
                if (!StoryFlags.Has(NpcKeys.Beat(beat)) && beatPlaying < 0)
                {
                    if (!Settings.Data.npcTutorial) { StoryFlags.Set(NpcKeys.Beat(beat)); return; }
                    if (DiverLooksAt(horst, 7f, 40f) || AnyoneNear(horst.Position, 4.5f)) PlayBeat(horst, beat);
                }
                ThinkGags(horst);
                return;
            }

            // ---- beat 8 (first morning with a resolved video) and beat 9 (1,000 followers: comes running)
            if (!StoryFlags.Has(NpcKeys.Beat(8)) && s.videos.Count > 0 && s.day >= 2)
            {
                Vector3 bench = QuestSpots.Get("horst_bench");
                if (NpcUtil.FlatDist(horst.Position, bench) > 1.5f) { if (!horst.Moving) horst.GoTo(bench, 2.2f); }
                else if (AnyoneNear(horst.Position, 7f) && beatPlaying < 0)
                {
                    if (!Settings.Data.npcTutorial) StoryFlags.Set(NpcKeys.Beat(8));
                    else PlayBeat(horst, 8);
                }
                return;
            }
            if (!StoryFlags.Has(NpcKeys.Beat(9)) && s.followers >= Progression.LivestreamFollowers && beatPlaying < 0)
            {
                var target = NearestDiverOnLand(horst.Position, 150f);
                if (target.HasValue)
                {
                    beat9Chase -= 0.1f;
                    float d = NpcUtil.FlatDist(horst.Position, target.Value);
                    if (d < 3.5f)
                    {
                        horst.Stop();
                        horst.FacePoint = target;
                        if (Settings.Data.npcTutorial) PlayBeat(horst, 9);
                        else StoryFlags.Set(NpcKeys.Beat(9));
                    }
                    else if (beat9Chase <= 0f)
                    {
                        beat9Chase = 1.5f;
                        horst.GoTo(target.Value, 4.2f);
                        horst.PlayGesture(NpcGesture.Flail);
                    }
                    return;
                }
            }

            // ---- daily schedule (round 3, Q10): his corner on the beach – bench 07–12 and 15–19, siesta in the hammock
            //      12–15, campfire 19–22, asleep in the hammock at night. Never on the dock.
            bool awake = Time.time < horstWakeUntil;
            bool hammock = !awake && (Between(h, 12f, 15f) || !Between(h, 7f, 22f));
            if (hammock && QuestSpots.IsRegistered("horst_hammock") && SleepSpot("horst_hammock", out Vector3 lieAt, out float lieYaw))
            {
                if (horst.Motion != NpcMotion.Lie || NpcUtil.FlatDist(horst.Position, lieAt) > 0.4f)
                {
                    Vector3 home = QuestSpots.Get("horst_home");
                    if (NpcUtil.FlatDist(horst.Position, home) > 1.2f && NpcUtil.FlatDist(horst.Position, lieAt) > 1.2f)
                    {
                        if (!horst.Moving || NpcUtil.FlatDist(horst.Destination, home) > 0.5f) horst.GoTo(home, 2.0f);
                        return;
                    }
                    horst.Stop();
                    horst.PlaceExact(lieAt, lieYaw);
                    horst.Motion = NpcMotion.Lie;
                    horst.FacePoint = null;
                }
                ThinkGags(horst);
                return;
            }
            if (horst.Motion == NpcMotion.Lie) { horst.Place(QuestSpots.Get("horst_home"), 90f); horst.Motion = NpcMotion.Idle; }
            if (Between(h, 7f, 19f) || awake)
            {
                Vector3 bench = QuestSpots.Get("horst_bench");
                if (NpcUtil.FlatDist(horst.Position, bench) > 0.8f)
                {
                    if (!horst.Moving || NpcUtil.FlatDist(horst.Destination, bench) > 0.5f) horst.GoTo(bench, 2.0f);
                }
                else if (!horst.Moving)
                {
                    horst.Motion = gagState == 5 ? NpcMotion.Doze : NpcMotion.Sit;
                    horst.FacePoint = PointFromYaw(horst.Position, PropHorst.BenchYaw);
                }
            }
            else
            {
                Vector3 fire = QuestSpots.Get("horst_campfire");
                if (NpcUtil.FlatDist(horst.Position, fire) > 1f) { if (!horst.Moving) horst.GoTo(fire, 2.0f); }
                else horst.Motion = NpcMotion.Idle;
            }
            ThinkGags(horst);
        }

        /// E on Horst asleep in his hammock: he wakes up (and stays up for a minute).
        void WakeHorst(NpcActor horst)
        {
            horstWakeUntil = Time.time + 75f;
            horst.Place(QuestSpots.Get("horst_home"), 90f);
            horst.Motion = NpcMotion.Idle;
            horst.PlayGesture(NpcGesture.Flail);
        }

        void PlayBeat(NpcActor horst, int n)
        {
            beatPlaying = n;
            horst.PlayGesture(n == 9 ? NpcGesture.TaDa : NpcGesture.Nod);
            if (!Talk(horst, "horst.b" + n, () => { StoryFlags.Set(NpcKeys.Beat(n)); beatPlaying = -1; })) beatPlaying = -1;
            else
            {
                var c = ConvOf(horst);
                if (c != null) c.radius = 25f;
            }
        }

        /// A diver within r metres faces the NPC (yaw within maxAngle).
        bool DiverLooksAt(NpcActor a, float r, float maxAngle)
        {
            var all = Divers.All;
            float cos = Mathf.Cos(maxAngle * Mathf.Deg2Rad);
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null) continue;
                Vector3 d = a.Position - t.position;
                d.y = 0f;
                float m = d.magnitude;
                if (m > r || m < 0.01f) continue;
                Vector3 f = t.forward;
                f.y = 0f;
                if (f.sqrMagnitude < 0.01f) continue;
                if (Vector3.Dot(f.normalized, d / m) >= cos) return true;
            }
            return false;
        }

        Vector3? NearestDiverOnLand(Vector3 p, float r)
        {
            var all = Divers.All;
            float best = r * r;
            Vector3? res = null;
            for (int i = all.Count - 1; i >= 0; i--)
            {
                var t = all[i];
                if (t == null) continue;
                Vector3 q = t.position;
                if (q.y < Waves.SeaLevel - 0.3f || WorldInfo.GroundHeight(q) < Waves.SeaLevel - 0.6f && q.y < 1f) continue;
                float d = NpcUtil.FlatDistSq(p, q);
                if (d < best) { best = d; res = q; }
            }
            return res;
        }

        // ---- idle gags (A.1): one every 12–25 s with a player within 15 m, never the same twice in a row
        void ThinkGags(NpcActor horst)
        {
            if (gagState > 0 && gagState < 60) { RunGag(horst); return; }
            if (horst.Moving || IsTalking(horst) || horst.HasOffer || !AnyoneNear(horst.Position, 15f)) return;
            gagTimer -= 0.1f;
            if (gagTimer > 0f) return;
            // less text during the tutorial: Horst stands next to the player for minutes there – a gag every 35–60 s, and
            // none while the current beat is still waiting to be told
            bool tut = TutorialRunning;
            gagTimer = (tut ? 35f : 12f) + NpcUtil.Hash01(NpcUtil.Day, (int)(Time.time * 10f)) * (tut ? 25f : 13f);
            if (tut && S != null && !StoryFlags.Has(NpcKeys.Beat(BeatFor(S.questIndex)))) return;
            int day = NpcUtil.Day;
            // round 3: in the hammock the only gag is falling out of it (once a day at most); no dock gags any more
            if (horst.Motion == NpcMotion.Lie)
            {
                if (lastFallDay != day && NpcUtil.Hash01(day, (int)Time.time, 6) < 0.12f) StartFall(horst);
                return;
            }
            int g;
            int tries = 0;
            do { g = 1 + (int)(NpcUtil.Hash01(day, (int)(Time.time * 7f), tries) * 5f); tries++; } while (g == lastGag && tries < 6);
            lastGag = g;
            gagState = g;
            gagT = 0f;
        }

        void RunGag(NpcActor horst)
        {
            gagT += 0.1f;
            switch (gagState)
            {
                case 1: // moustache twirl → it springs back
                    if (gagT < 0.15f) horst.PlayGesture(NpcGesture.Twirl);
                    else if (gagT >= 1.6f) { horst.PlayGesture(NpcGesture.Facepalm); Talk(horst, "horst.gag.ow"); gagState = 0; }
                    break;
                case 2: // Günther steals the sandwich
                    if (gagT < 0.15f) { if (gull != null) gull.Steal(); NpcNet.SendEvent(NpcNet.EvGullSteal, null, null); }
                    else if (gagT >= 1.5f) { horst.PlayGesture(NpcGesture.Shrug); Talk(horst, "horst.gag.sandwich"); gagState = 0; }
                    break;
                case 3: // whistle at a passing gull, shakes a finger
                    if (gagT < 0.15f) horst.PlayGesture(NpcGesture.Whistle);
                    else if (gagT >= 1.4f) { horst.PlayGesture(NpcGesture.Point); Talk(horst, "horst.gag.whistle"); gagState = 0; }
                    break;
                case 4: // selfie with the player
                    if (gagT < 0.15f) { horst.FacePoint = NearestDiverPos(horst.Position); horst.PlayGesture(NpcGesture.Selfie); }
                    else if (gagT >= 2.2f) { horst.PlayGesture(NpcGesture.Nod); Talk(horst, "horst.gag.selfie"); gagState = 0; }
                    break;
                case 5: // dozes standing up, jerks awake
                    if (gagT >= 4.5f)
                    {
                        horst.Motion = horst.Motion == NpcMotion.Doze ? (Between(Hour, 7f, 19f) && !TutorialRunning ? NpcMotion.Sit : NpcMotion.Idle) : horst.Motion;
                        horst.PlayGesture(NpcGesture.Flail);
                        Talk(horst, "horst.gag.awake");
                        gagState = 0;
                    }
                    else if (horst.Motion == NpcMotion.Idle || horst.Motion == NpcMotion.Sit) horst.Motion = NpcMotion.Doze;
                    break;
                default: gagState = 0; break;
            }
        }

        // ---- gag 6 (round 3): turns over in the hammock – and falls out. „Geplant.“ (achievement)
        void StartFall(NpcActor horst)
        {
            lastFallDay = NpcUtil.Day;
            gagState = 60;
            gagT = 0f;
        }

        void ThinkFallGag(NpcActor horst)
        {
            gagT += 0.1f;
            switch (gagState)
            {
                case 60:
                    horst.PlayGesture(NpcGesture.Flail);
                    Talk(horst, "horst.gag.whoa");
                    gagState = 61;
                    gagT = 0f;
                    break;
                case 61:
                    if (gagT >= 0.8f)
                    {
                        // thud – in the sand next to the hammock
                        horst.Place(QuestSpots.Get("horst_home"), horst.Yaw);
                        horst.Motion = NpcMotion.Dazed;
                        gagState = 62;
                        gagT = 0f;
                    }
                    break;
                case 62:
                    if (gagT >= 3.2f)
                    {
                        horst.Motion = NpcMotion.Idle;
                        horst.PlayGesture(NpcGesture.TaDa);
                        Talk(horst, "horst.gag.planned");
                        horstWakeUntil = Time.time + 90f;
                        if (AnyoneNear(horst.Position, 20f))
                        {
                            Achievements.Unlock("horst_overboard");
                            StoryFlags.Set(StoryKeys.HorstOverboardSeen);
                        }
                        gagState = 0;
                    }
                    break;
            }
        }

        /// HOST: tutorial reactions to catches (A.3 beat 1 extras).
        void HorstOnCatch(FishItem f)
        {
            var horst = Actor(NpcDatabase.Horst);
            if (horst == null || !horst.Present || f == null || !AnyoneNear(horst.Position, 25f) || IsTalking(horst)) return;
            if (f.kindId == "boot" && !StoryFlags.Has(NpcKeys.HorstReactBoot)) { StoryFlags.Set(NpcKeys.HorstReactBoot); Talk(horst, "horst.react.boot"); return; }
            if (TutorialRunning && !StoryFlags.Has(NpcKeys.HorstReactCatch)) { StoryFlags.Set(NpcKeys.HorstReactCatch); horst.PlayGesture(NpcGesture.Clap); Talk(horst, "horst.react.catch"); }
        }

        /// Small talk of Horst (rotating, with real tips and the village pointer).
        string HorstSmallTalk()
        {
            var seq = DialogueDatabase.Seq("horst.talk");
            if (seq.Count == 0) return null;
            horstTalkIndex = (horstTalkIndex + 1) % seq.Count;
            return seq[horstTalkIndex];
        }

        int horstTalkIndex = -1;

        // ================================================================ B.1 Gus on the radio (LOCAL)
        static readonly string[] MissionIds = { "M1", "M2", "M3", "M4" };
        readonly System.Collections.Generic.Dictionary<string, int> missionSeen = new System.Collections.Generic.Dictionary<string, int>();
        int seenFlagsCount;

        void OnMilestone(Milestone m)
        {
            if (m == null || !InGame) return;
            string pre = "gus.m" + m.followers.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (DialogueDatabase.Seq(pre).Count > 0) Radio(NpcDatabase.Gus, pre, pre);
            // L.6: the 100k call hints at the TV set on the trench island
            if (m.followers == 100000) StoryFlags.Set(NpcKeys.CluePrefix + "trench_tv");
        }

        void OnMissionChanged(string id)
        {
            if (!Net.HasAuthority) return;   // clients diff in OnStateSynced
            MissionRadio(id, Missions.StateOf(id));
        }

        void MissionRadio(string id, int state)
        {
            if (state == Missions.Active) Radio(NpcDatabase.Gus, "gus." + id + ".start", "gus." + id + ".start");
            else if (state == Missions.Done)
            {
                Radio(NpcDatabase.Gus, "gus." + id + ".done", "gus." + id + ".done");
                if (id == "M2") { Radio(NpcDatabase.Gus, "gus.hammock", "gus.hammock"); StoryFlags.Set(NpcKeys.CluePrefix + "wreck_hammock"); }
            }
            trackerDirty = true;
        }

        void OnStateSynced()
        {
            if (Net.HasAuthority || !InGame) return;
            for (int i = 0; i < MissionIds.Length; i++)
            {
                string id = MissionIds[i];
                int st = Missions.StateOf(id);
                missionSeen.TryGetValue(id, out int old);
                if (st != old) { missionSeen[id] = st; MissionRadio(id, st); }
            }
            trackerDirty = true;
            objectsDirty = true;
        }

        /// Every machine: Gus' arrival call when the player reaches the hut after the prologue (B.1 milestone 0).
        void LocalStoryTick()
        {
            if (!StoryFlags.Has(StoryKeys.PrologueDone) || StoryFlags.Has(NpcKeys.GusCall0)) return;
            if (NpcUtil.FlatDist(NpcUtil.LocalPlayerPos, WorldLayout.Hut) > 16f) return;
            Radio(NpcDatabase.Gus, "gus.m0", "gus.m0");
            StoryFlags.Set(NpcKeys.GusCall0);
        }

        // ================================================================ B.2 Gus' missions (HOST)
        /// HOST, new day: activates the next mission (max 1 active; "+ next morning" = the first DayStarted after the trigger).
        void MissionsNewDay()
        {
            var s = S;
            if (s == null || Missions.Current != null) return;
            for (int i = 0; i < MissionIds.Length; i++)
            {
                string id = MissionIds[i];
                if (Missions.StateOf(id) != Missions.Locked) continue;
                if (!MissionTriggered(id, s)) continue;
                Missions.Set(id, Missions.Active, 0);
                Notify(NotifyKind.Story, "Gus hat einen Auftrag", "Gus has a job", MissionTitle(id));
                trackerDirty = true;
                objectsDirty = true;
                return;
            }
        }

        static bool MissionTriggered(string id, GameState s)
        {
            switch (id)
            {
                case "M1": return Progression.IsSiteUnlocked("kelp_forest");
                case "M2": return Progression.IsSiteUnlocked("wreck");
                case "M3": return Collectibles.Count(CollectibleKind.BottleMessage) > 0 || s.followers >= 10000;
                case "M4": return Progression.IsSiteUnlocked("deep_trench");
            }
            return false;
        }

        public static string MissionTitle(string id)
        {
            switch (id)
            {
                case "M1": return Loc.Tr("Die Glücksflosse", "The Lucky Fin");
                case "M2": return Loc.Tr("Funkstille", "Radio Silence");
                case "M3": return Loc.Tr("Das Leuchtfeuer", "The Beacon");
                case "M4": return Loc.Tr("Signal aus der Tiefe", "Signal from the Deep");
            }
            return id;
        }

        /// Quest-line text of the active mission (≤ 42 characters) and its target.
        string MissionLine(string id, int progress, out Vector3? target)
        {
            target = null;
            switch (id)
            {
                case "M1": target = ObjectPos("m1_fin"); return Loc.Tr("Glücksflosse beim Kelp-Inselchen", "Lucky fin near the kelp islet");
                case "M2": target = ObjectPos("m2_radio"); return Loc.Tr("Gus' Funkgerät in der Kombüse", "Gus's radio in the galley");
                case "M3":
                {
                    int n = Bits(progress & 7);
                    if (n >= 3) { target = ObjectPos("m3_lamp"); return Loc.Tr("Scherben oben am Leuchtturm einsetzen", "Fit the shards at the lighthouse top"); }
                    return Loc.Tr("Linsenscherben finden", "Find the lens shards");
                }
                case "M4": target = QuestSpots.Get("m4_mast"); return Loc.Tr("Antennenmast filmen (92 m)", "Film the antenna mast (92 m)");
            }
            return null;
        }

        static int Bits(int v)
        {
            int n = 0;
            while (v != 0) { n += v & 1; v >>= 1; }
            return n;
        }

        /// HOST: a mission object was used.
        void MissionObject(string objId)
        {
            var cur = Missions.Current;
            if (cur == null) return;
            switch (objId)
            {
                case "m1_fin": if (cur.id == "M1") CompleteMission("M1"); break;
                case "m2_radio": if (cur.id == "M2") CompleteMission("M2"); break;
                case "m3_shard1": case "m3_shard2": case "m3_shard3":
                    if (cur.id == "M3")
                    {
                        int bit = objId[objId.Length - 1] - '1';
                        int p = cur.progress | (1 << bit);
                        Missions.Set("M3", Missions.Active, p);
                        Notify(NotifyKind.Story, "Linsenscherbe", "Lens shard", Bits(p & 7) + "/3");
                        RadioAll(NpcDatabase.Gus, "gus.shard");
                    }
                    break;
                case "m3_lamp":
                    if (cur.id == "M3" && Bits(cur.progress & 7) >= 3) CompleteMission("M3");
                    break;
            }
            objectsDirty = true;
            trackerDirty = true;
        }

        void CompleteMission(string id)
        {
            Missions.Set(id, Missions.Done, 0);
            long money = 0;
            switch (id)
            {
                case "M1": money = 400; Cosmetics.Unlock("fins:4"); break;
                case "M2": money = 1000; break;
                case "M3": money = 2500; Cosmetics.Unlock("pattern:7"); StoryFlags.Set(NpcKeys.LighthouseLit); break;
                case "M4": money = 10000; StoryFlags.Set(NpcKeys.EyeSeen); break;
            }
            Pay(money);
            GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Gus' Auftrag erledigt", "Gus's job done"), "+" + Loc.Money(money));
            objectsDirty = true;
            trackerDirty = true;
        }
    }
}
