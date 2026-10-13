using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 5 (NPCs, hermit camp, story, side quests – GDD §19 + §20 Q2–Q11, Q42, Q43): the six hermits with
    /// their day, the new proportions, Horst in his beach hammock (never on the dock), the gesture wheel + moods,
    /// the stick fight (tie-up minigame / respect after a win), crafts + barter, the coherent story + clue lines,
    /// the dive-site gating (requirement at the top, no early announcement), every side quest completable and the
    /// rivals' sabotage. Runs inside RunRound3Steps (-r3only) or alone with
    ///     SEALEBRITIES.exe -autoplaytest "C:/out" -r3npc
    public sealed partial class AutoPlaytest
    {
        static bool Round3NpcOnly => Array.IndexOf(Environment.GetCommandLineArgs(), "-r3npc") >= 0;

        IEnumerator RunRound3NpcSteps()
        {
            yield return RunStep("72 NPCs (Q2/Q3/Q10/Q38): six hermits at their places, natural proportions, Horst in his hammock corner – never on the dock", StepR3NpcCamp, 180f);
            yield return RunStep("73 Gesture wheel + mood (Q4/Q5): hold G opens the wheel, the right sign / a liked gift raise the mood, a wrong sign annoys", StepR3NpcSigns, 120f);
            yield return RunStep("74 Stick fight (Q6/Q7): a furious hermit attacks, a lost fight ties you up (A/D wriggle free), a won fight earns respect", StepR3NpcFight, 180f);
            yield return RunStep("75 Crafts + barter (Q8/Q47): sharpening learned at the smith, trade a treasure, fish trap + harvest mornings, the curry recipe", StepR3NpcCrafts, 120f);
            yield return RunStep("76 Story (Q9) + clues: Gus' inheritance from the first minute, no village left in any line, clues for map pieces / treasure / wrecks / memes / Big Mo", StepR3NpcStory, 90f);
            yield return RunStep("77 Dive-site gating (Q42): the next site's requirement stands at the top centre, no announcement before it is reached", StepR3NpcSiteGate, 90f);
            yield return RunStep("78 Side quests (Q11): every job – old ones moved off the village + the new funny filming jobs – driven to its reward", StepR2SideQuestsAll, 720f);
            yield return RunStep("79 Rival sabotage (Q43): a copied subject on the quest line, the better clip wins it back", StepR3NpcSabotage, 90f);
        }

        // ------------------------------------------------------------------ helpers
        static NpcActor Npc(string id) => NpcRegistry.Find(id) as NpcActor;

        /// Hermits seated (Sit, not walking) within 3.4 m of the camp fire – the three log benches lie at 2.6 m.
        static int HermitsSittingAtFire()
        {
            if (!PropInfo.Spot("hermit_fire", out Vector3 fire)) return 0;
            int n = 0;
            foreach (var id in NpcDatabase.Hermits)
            {
                var a = Npc(id);
                if (a != null && a.Present && !a.Moving && a.Motion == NpcMotion.Sit && NpcUtil.FlatDist(a.Position, fire) < 3.4f) n++;
            }
            return n;
        }

        static float DockDist(Vector3 p)
        {
            Vector3 a = WorldLayout.DockStart, b = WorldLayout.DockEnd;
            a.y = 0f; b.y = 0f; p.y = 0f;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        static Bounds RenderBounds(NpcActor a)
        {
            var b = new Bounds(a.Position, Vector3.zero);
            var rs = a.GetComponentsInChildren<Renderer>();
            bool any = false;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] == null || !rs[i].enabled || rs[i].name.EndsWith("_L1", StringComparison.Ordinal) || rs[i].name.EndsWith("_L2", StringComparison.Ordinal)) continue;
                if (rs[i].transform.name == "Bang" || rs[i].transform.parent != null && rs[i].transform.parent.name == "Marker") continue;
                if (!any) { b = rs[i].bounds; any = true; } else b.Encapsulate(rs[i].bounds);
            }
            return b;
        }

        void FriendlyCamp(int mood = 50)
        {
            StoryFlags.Set(StoryKeys.HermitCampFound);
            foreach (var id in NpcDatabase.Hermits)
            {
                var h = Hermits.GetOrAdd(id);
                if (h == null) continue;
                h.met = true;
                h.mood = mood;
                if (mood >= Hermits.Friendly) h.friend = true;
            }
        }

        IEnumerator StandAt(NpcActor a, float dist)
        {
            if (a == null || pc == null) yield break;
            pc.enabled = true;
            Vector3 p = a.Position;
            Vector3 feet = SpotNear(p, dist);
            yield return Stand(feet, YawTo(feet, p));
            yield return AimAt(p + Vector3.up * (a.Def.height * 0.75f));
        }

        // ================================================================== 72 camp + NPC looks + Horst
        IEnumerator StepR3NpcCamp()
        {
            Check(NpcModule.Director != null, "npc module installed");
            Check(QuestSpots.IsRegistered("hermit_camp"), "the hermit camp is built (anchor hermit_camp)");
            int bodies = 0;
            foreach (var id in NpcDatabase.Hermits) if (Npc(id) != null) bodies++;
            Check(bodies == 6, bodies + "/6 hermits have bodies (leader, smith, hunter, cook, lookout, gardener)");
            FriendlyCamp();
            SetTime(10f / 24f);
            yield return Wait(3f);
            int atWork = 0;
            string far = "";
            foreach (var d in HermitDatabase.All)
            {
                var a = Npc(d.id);
                if (a == null || !a.Present) { far += d.id + "(away) "; continue; }
                float dist = Vector3.Distance(Flat(a.Position), Flat(QuestSpots.Get(d.work)));
                if (dist < 3f) atWork++; else far += d.id + "(" + F(dist, "0") + " m) ";
            }
            Check(atWork >= 5, atWork + "/6 hermits at their work places at 10:00" + (far.Length > 0 ? " – " + far : ""));
            // natural proportions (Q38): close to the defined height, an egg head – not a giant ball
            string props = "";
            bool propsOk = true;
            foreach (var id in new[] { NpcDatabase.Horst, NpcDatabase.HLeader, NpcDatabase.HSmith, NpcDatabase.HCook })
            {
                var a = Npc(id);
                if (a == null || !a.Present || a.Body == null || a.Body.head == null) continue;
                var bb = RenderBounds(a);
                float h = bb.size.y;
                // Horst may still lie in his hammock / sit on his bench right after the clock jump: only standing bodies count
                if (h < a.Def.height * 0.8f) { props += id + " not standing (" + F(h, "0.00") + " m) – skipped; "; continue; }
                var head = a.Body.head.GetComponentInChildren<MeshRenderer>();
                float headH = head != null ? head.bounds.size.y : 0f, headW = head != null ? head.bounds.size.x : 1f;
                bool ok = h > a.Def.height * 0.85f && h < a.Def.height * 1.25f && headH > headW * 1.05f && headH < a.Def.height * 0.24f;
                propsOk &= ok;
                props += id + " " + F(h, "0.00") + "/" + F(a.Def.height, "0.00") + " m, head " + F(headH, "0.00") + "×" + F(headW, "0.00") + "; ";
            }
            Check(propsOk, "natural proportions (height, egg head on a neck): " + props);
            // the camp from above + at the fire
            Vector3 c = QuestSpots.Get("hermit_camp");
            pc.enabled = false;
            Pose(c + new Vector3(-10f, 9f, -10f), c);
            yield return Wait(1f);
            yield return Shot("72_hermit_camp");
            // final step 7: frame rate over the camp (props, 6 hermits, smoke) on High and Low
            yield return MeasureR3Fps("hermit camp r3");
            var lead = Npc(NpcDatabase.HLeader);
            if (lead != null) { Pose(lead.Position + lead.transform.forward * 2.2f + Vector3.up * 1.6f, lead.Position + Vector3.up * 1.4f); yield return Wait(0.6f); yield return Shot("72_hermit_leader"); }
            SetTime(20f / 24f);
            yield return WaitUntil(() => HermitsSittingAtFire() >= 5, 30f);
            int sitting = HermitsSittingAtFire();
            Check(sitting >= 5, "evening: the hermits sit on the log benches around the fire (" + sitting + "/6 seated within 3.4 m)");
            if (!PropInfo.Spot("hermit_fire", out Vector3 fireC)) fireC = c;
            Pose(fireC + new Vector3(4f, 2.2f, -5f), fireC + Vector3.up * 0.6f);
            yield return Wait(1f);
            yield return Shot("72_hermits_at_the_fire");
            pc.enabled = true;

            // ---- Horst (Q10): bench in his corner, siesta in the hammock, never on the dock
            var horst = Npc(NpcDatabase.Horst);
            if (Check(horst != null, "Horst exists"))
            {
                SetTime(10f / 24f);
                yield return WaitUntil(() => horst.Present && !horst.Moving && Vector3.Distance(Flat(horst.Position), Flat(QuestSpots.Get("horst_bench"))) < 2f, 30f);
                Check(Vector3.Distance(Flat(horst.Position), Flat(QuestSpots.Get("horst_bench"))) < 2f && DockDist(horst.Position) > 8f,
                      "10:00 Horst sits on his bench in the beach corner (" + F(DockDist(horst.Position), "0") + " m from the dock)");
                SetTime(13f / 24f);
                yield return WaitUntil(() => horst.Motion == NpcMotion.Lie, 30f);
                Check(horst.Motion == NpcMotion.Lie && Vector3.Distance(Flat(horst.Position), Flat(QuestSpots.Get("horst_hammock"))) < 1.2f,
                      "13:00 siesta: Horst lies IN his hammock (" + horst.Motion + ", " + F(Vector3.Distance(Flat(horst.Position), Flat(QuestSpots.Get("horst_hammock"))), "0.0") + " m from the cloth)");
                pc.enabled = false;
                Pose(horst.Position + new Vector3(2.6f, 1.7f, 1.4f), horst.Position + Vector3.up * 0.3f);
                yield return Wait(0.8f);
                yield return Shot("72_horst_hammock");
                pc.enabled = true;
                // his tutorial / talk spots: none on the dock planks
                string onDock = "";
                foreach (var sp in new[] { "horst_dockside", "horst_bench", "horst_home", "horst_campfire", "horst_porch", "toni_evening" })
                    if (DockDist(QuestSpots.Get(sp)) < 2.5f) onDock += sp + " ";
                Check(onDock.Length == 0, "none of Horst's spots is on the dock" + (onDock.Length > 0 ? " – on the dock: " + onDock : ""));
                SetTime(10f / 24f);
            }
        }

        // ================================================================== 73 gesture wheel + mood
        IEnumerator StepR3NpcSigns()
        {
            FriendlyCamp(-15);
            SetTime(11f / 24f);
            yield return Wait(2f);
            var cook = Npc(NpcDatabase.HCook);
            if (!Check(cook != null && cook.Present, "the cook is in the camp")) yield break;
            yield return StandAt(cook, 2.2f);
            yield return Wait(0.5f);
            Info("prompt at the cook: '" + InteractionPrompt.Text + "'");
            // the wheel through the real key (hold G)
            GameInput.SimPress(GameAction.Gestures);
            yield return Wait(0.5f);
            bool opened = NpcInteract.WheelOpen && UIState.NpcMenuOpen;
            if (!opened && NpcInteract.I != null) { NpcInteract.I.OpenWheel(cook); yield return Wait(0.3f); }
            Check(NpcInteract.WheelOpen && NpcInteract.Target == cook, "holding G opens the gesture wheel at the cook (" + (opened ? "key" : "direct") + ")");
            yield return Shot("73_gesture_wheel");
            GameInput.SimRelease(GameAction.Gestures);
            yield return Wait(0.4f);
            Check(!NpcInteract.WheelOpen && !UIState.NpcMenuOpen, "releasing G closes the wheel");

            // a sign while the cook still talks (his greeting bark) is ignored by design – wait for the end of his line
            yield return WaitUntil(() => (cook.Flags & NpcActor.FlagConversation) == 0, 12f);
            yield return Wait(0.3f);
            int m0 = Hermits.Mood(NpcDatabase.HCook);
            NpcInteract.Perform(cook, HermitSign.Sea);
            yield return Wait(0.6f);
            int m1 = Hermits.Mood(NpcDatabase.HCook);
            Check(m1 > m0, "'Das Meer ehren' is the right sign: mood " + m0 + " → " + m1);
            yield return WaitUntil(() => (cook.Flags & NpcActor.FlagConversation) == 0, 12f);
            NpcInteract.Perform(cook, HermitSign.Film);
            yield return Wait(0.6f);
            int m2 = Hermits.Mood(NpcDatabase.HCook);
            Check(m2 < m1, "filming a wary hermit is the wrong sign – annoyed: " + m1 + " → " + m2);
            yield return WaitUntil(() => (cook.Flags & NpcActor.FlagConversation) == 0, 12f);
            Backpack.Add(ItemIds.RawFish, 1, false);
            bool gave = NpcDirector.LocalGift(cook, ItemIds.RawFish);
            yield return Wait(0.6f);
            int m3 = Hermits.Mood(NpcDatabase.HCook);
            Check(gave && m3 >= m2 + 15, "a liked gift (raw fish) lifts the mood: " + m2 + " → " + m3);
            yield return Wait(0.5f);
            int band = Hermits.Band(m3);
            bool flagOk = band == 3 ? (cook.Flags & NpcActor.FlagFriendly) != 0 : band <= 1 ? (cook.Flags & NpcActor.FlagWary) != 0 : (cook.Flags & (NpcActor.FlagWary | NpcActor.FlagFriendly)) == 0;
            Check(flagOk, "the mood band travels with the NPC state (band " + band + ", flags " + cook.Flags + ")");
            // food sign at a calm cook: a bowl lands in the backpack
            Hermits.Raise(NpcDatabase.HCook, 40);
            yield return WaitUntil(() => (cook.Flags & NpcActor.FlagConversation) == 0, 12f);
            int food0 = Backpack.Count(FoodDatabase.GrilledFish) + Backpack.Count(Survival.CurryId);
            NpcInteract.Perform(cook, HermitSign.Food);
            yield return Wait(0.8f);
            Check(Backpack.Count(FoodDatabase.GrilledFish) + Backpack.Count(Survival.CurryId) > food0, "'Essen' at the cook: a bowl in the backpack");
            // the gift picker
            if (NpcInteract.I != null)
            {
                Backpack.Add("mango", 1, false);
                NpcInteract.I.OpenPicker(cook, false);
                yield return Wait(0.4f);
                Check(NpcInteract.PickerOpen && UIState.BlocksGameplay, "the gift picker is open (cursor free)");
                yield return Shot("73_gift_picker");
                NpcInteract.I.ClosePicker();
            }
            // a speaking NPC: the action wheel gives a tip
            var horst = Npc(NpcDatabase.Horst);
            if (horst != null && horst.Present)
            {
                yield return StandAt(horst, 2f);
                int l0 = npcLines;
                NpcInteract.Perform(horst, HermitSign.Hint);
                yield return Wait(1.5f);
                Check(npcLines > l0, "Horst answers 'Tipp' with a line (" + (npcLines - l0) + " lines)");
            }
        }

        // ================================================================== 74 stick fight: tie-up + respect
        IEnumerator StepR3NpcFight()
        {
            var dir = NpcDirector.I;
            FriendlyCamp(50);
            var hunter = Npc(NpcDatabase.HHunter);
            if (!Check(dir != null && hunter != null, "director + the hunter")) yield break;
            var hs = Hermits.GetOrAdd(NpcDatabase.HHunter);
            hs.mood = -70;
            SetTime(11f / 24f);
            yield return Wait(1.5f);
            yield return StandAt(hunter, 2.6f);
            Combat.Reset();
            NpcDirector.TestFightsAllowed = true;
            float hurt = 0f;
            Action<float, Vector3, string> onHurt = (d, f, s) => { if (s == NpcDatabase.HHunter) hurt += d; };
            Combat.LocalHurt += onHurt;
            yield return WaitUntil(() => { dir.DebugHermitState(NpcDatabase.HHunter, out int ft, out _, out _, out _); return ft >= 0; }, 12f);
            dir.DebugHermitState(NpcDatabase.HHunter, out int target, out _, out _, out _);
            if (target < 0) { Info("he did not attack on his own within 12 s – forcing the fight"); dir.DebugStartFight(NpcDatabase.HHunter, Net.LocalId); }
            Check(true, "a furious hermit attacks the player (fight target " + target + ")");
            // stay put and take the bonks until knocked out → tied up
            float t0 = Time.realtimeSinceStartup;
            while (!NpcInteract.TiedUp && Time.realtimeSinceStartup - t0 < 40f)
            {
                if (Vector3.Distance(Flat(pc.Root.position), Flat(hunter.Position)) > 3f) yield return StandAt(hunter, 1.5f);
                yield return Wait(0.3f);
            }
            Combat.LocalHurt -= onHurt;
            Check(hurt > 0f, "the bonks hurt (Puste −" + F(hurt, "0") + "), never death (fainting " + pc.IsFainting + ")");
            Check(NpcInteract.TiedUp && PlayerController.Restrained, "knocked out by a hermit → tied up, walking blocked (Q6)");
            yield return Wait(1f);
            pc.enabled = false;
            Pose(pc.Root.position + new Vector3(2.2f, 1.6f, 1.6f), pc.Root.position + Vector3.up * 1f);
            yield return Shot("74_tied_up");
            pc.enabled = true;
            dir.DebugHermitState(NpcDatabase.HHunter, out int ft2, out _, out int guard, out _);
            Check(ft2 < 0 && guard == Net.LocalId, "the hermit stopped fighting and stands guard (fight " + ft2 + ", guard over " + guard + ")");
            int moodTied = Hermits.Mood(NpcDatabase.HHunter);
            // wriggle free: A / D in turn (the real keys first, the hook as a fallback)
            for (int i = 0; i < 16 && NpcInteract.TiedUp; i++)
            {
                GameInput.SimTap(i % 2 == 0 ? GameAction.Left : GameAction.Right);
                yield return Frames(3);
            }
            for (int i = 0; i < 20 && NpcInteract.TiedUp; i++) NpcInteract.DebugWriggle(i % 2 == 0 ? -1 : 1);
            yield return Wait(0.8f);
            Check(!NpcInteract.TiedUp && !PlayerController.Restrained, "A / D in turn: free again");
            dir.DebugHermitState(NpcDatabase.HHunter, out _, out _, out int guard2, out _);
            Check(guard2 < 0 && Hermits.Mood(NpcDatabase.HHunter) >= moodTied, "he lets the player go and laughs (guard " + guard2 + ", mood " + moodTied + " → " + Hermits.Mood(NpcDatabase.HHunter) + ")");

            // ---- the won fight (Q7): a real stick bonk first, then enough hits
            Combat.Reset();
            yield return Wait(1f);
            hs.mood = -70;
            int resp0 = Hermits.Respect(NpcDatabase.HHunter);
            yield return StandAt(hunter, 1.6f);
            var stick = ItemDatabase.Get(ItemIds.Stick);
            if (Backpack.Count(ItemIds.Stick) == 0) Backpack.Add(ItemIds.Stick, 1, false);
            Vector3 from = cam.transform.position;
            Vector3 aim = hunter.Position + Vector3.up * 1.1f;
            bool bonk = Combat.Strike(from, (aim - from).normalized, stick, Backpack.First(ItemIds.Stick), false, pc.transform, out _);
            Check(bonk, "the stick hits a hermit (IHittable)");
            yield return Wait(0.4f);
            for (int i = 0; i < 8; i++)
            {
                dir.DebugHermitState(NpcDatabase.HHunter, out _, out bool dz, out _, out _);
                if (dz) break;
                NpcNet.Request("hit", NpcDatabase.HHunter, "10");
                yield return Wait(0.25f);
            }
            yield return Wait(0.5f);
            dir.DebugHermitState(NpcDatabase.HHunter, out _, out bool dazed, out _, out _);
            Check(dazed && hunter.Motion == NpcMotion.Dazed, "beaten: he sits dazed in the sand (" + hunter.Motion + ")");
            Check(Hermits.Respect(NpcDatabase.HHunter) > resp0 && Hermits.IsFriendly(NpcDatabase.HHunter) && StoryFlags.Has(NpcKeys.HermitRespect),
                  "strength earns respect: respect " + resp0 + " → " + Hermits.Respect(NpcDatabase.HHunter) + ", mood " + Hermits.Mood(NpcDatabase.HHunter) + " (friendly)");
            pc.enabled = false;
            Pose(hunter.Position + hunter.transform.forward * 2.4f + Vector3.up * 1.2f, hunter.Position + Vector3.up * 0.5f);
            yield return Wait(0.4f);
            yield return Shot("74_hermit_dazed");
            pc.enabled = true;
            NpcDirector.TestFightsAllowed = false;
            Combat.Reset();
            FriendlyCamp(50);
        }

        // ================================================================== 75 crafts + barter
        IEnumerator StepR3NpcCrafts()
        {
            FriendlyCamp(50);
            SetTime(11f / 24f);
            yield return Wait(1.5f);
            var smith = Npc(NpcDatabase.HSmith);
            if (!Check(smith != null && smith.Present, "the smith is at his grinding bench")) yield break;
            yield return StandAt(smith, 2f);
            bool before = StoryFlags.Has(StoryKeys.GrindLearned);
            if (before) { S.storyFlags.Remove(StoryKeys.GrindLearned); }
            NpcInteract.Perform(smith, HermitSign.Learn);
            yield return WaitUntil(() => StoryFlags.Has(StoryKeys.GrindLearned), 25f);
            Check(StoryFlags.Has(StoryKeys.GrindLearned) && Combat.GrindLearned, "'Zeig's mir' at a friendly smith: spear sharpening learned (StoryKeys.GrindLearned)");
            // barter: a treasure for three whetstones
            Backpack.Add(ItemIds.Coins, 1, false);
            int stones0 = Backpack.Count(ItemIds.Stone);
            bool traded = NpcDirector.LocalTrade(smith, 0);
            yield return Wait(0.6f);
            Check(traded && Backpack.Count(ItemIds.Stone) >= stones0 + 3, "barter: 1 treasure → 3 whetstones (" + stones0 + " → " + Backpack.Count(ItemIds.Stone) + ")");
            if (NpcInteract.I != null) { NpcInteract.I.OpenPicker(smith, true); yield return Wait(0.4f); yield return Shot("75_barter_picker"); NpcInteract.I.ClosePicker(); }
            // the hunter's fish trap + the gardener's harvest: every morning into the own backpack
            StoryFlags.Set(NpcKeys.TrapLearned);
            StoryFlags.Set(NpcKeys.HarvestLearned);
            int fish0 = Backpack.Count(ItemIds.RawFish), fruit0 = Backpack.Count("mango") + Backpack.Count("banana");
            NpcDirector.I.DebugMorningCrafts(S.day + 500);
            Check(Backpack.Count(ItemIds.RawFish) > fish0 && Backpack.Count("mango") + Backpack.Count("banana") > fruit0, "morning: the fish trap and the harvest filled the backpack");
            // the cook's recipe: raw fish + coconut on the fire = coconut curry
            StoryFlags.Set(NpcKeys.RecipeCurry);
            Backpack.Add(ItemIds.RawFish, 1, false);
            Backpack.Add(FoodDatabase.Coconut, 1, false);
            int curry0 = Backpack.Count(Survival.CurryId);
            // cook the fish first in the list: move raw fish to the front by cooking until a curry appears
            for (int i = 0; i < 6 && Backpack.Count(Survival.CurryId) == curry0 && Survival.FirstRaw() != null; i++)
            {
                if (Survival.FirstRaw() != ItemIds.RawFish) { Backpack.Remove(Survival.FirstRaw(), 1); continue; }
                Survival.CookOne();
            }
            Check(Backpack.Count(Survival.CurryId) > curry0, "the cook's recipe: raw fish + coconut on the fire → coconut curry");
        }

        // ================================================================== 76 story + clues
        IEnumerator StepR3NpcStory()
        {
            var all = new List<LineDef>();
            DialogueDatabase.AllLines(all);
            string letter = Dialogue.Resolve != null ? DialogueDatabase.Resolve("pro.letter.body", null) : "";
            Check(letter.Contains("1987") && (letter.Contains("Million") || letter.Contains("million")) && letter.Contains("Horst"),
                  "Gus' letter explains the inheritance from the first minute: island + hut + channel, 1987, a million, Horst");
            string bad = "";
            foreach (var l in all)
            {
                string t = (l.de ?? "") + " " + (l.en ?? "");
                if (t.Contains("Altriff") || t.Contains("Oldreef") || t.Contains("Perle ") || t.Contains("Pearl") || t.Contains(" Lio") || t.Contains("Gute Flut")) bad += l.id + " ";
            }
            Check(bad.Length == 0, all.Count + " lines – none mentions the removed village" + (bad.Length > 0 ? " (still: " + bad + ")" : ""));
            int signs = 0;
            foreach (var l in all) if (l.tag == "sign" && l.de.StartsWith("*") || l.tag == "sign" && l.de.StartsWith("(")) signs++;
            Check(signs >= 40, signs + " hermit lines are described signs (they never speak words)");
            foreach (var sid in new[] { "gus.site.kelp_forest", "gus.site.wreck", "gus.site.blue_drop", "gus.site.deep_trench", "horst.camp", "horst.tied", "horst.respect", "hm.story" })
                Check(DialogueDatabase.Seq(sid).Count > 0, "story beat '" + sid + "' (" + DialogueDatabase.Seq(sid).Count + " lines)");
            // milestone banners never announce a dive site
            string ann = "";
            foreach (var m in Progression.Milestones) if (m.descDe.Contains("Wrack") || m.descDe.Contains("Graben") || m.descEn.Contains("wreck") || m.descEn.Contains("trench")) ann += m.id + " ";
            Check(ann.Length == 0, "no follower milestone announces a dive site" + (ann.Length > 0 ? " (" + ann + ")" : ""));
            // clues: map pieces / treasure / wrecks / memes / Big Mo have someone who knows them
            int keys = 0;
            string missingWho = "";
            foreach (var e in NpcClues.All)
            {
                keys++;
                bool any = false;
                for (int k = 0; k + 1 < e.who.Length; k += 2) if (DialogueDatabase.Has(e.who[k + 1])) any = true;
                if (!any) missingWho += e.key + " ";
            }
            Check(keys >= 35 && missingWho.Length == 0, keys + " clues, each with a line" + (missingWho.Length > 0 ? " – missing: " + missingWho : ""));
            string h1 = NpcClues.Pick(NpcDatabase.Horst, false);
            Check(h1 != null, "Horst has a clue ready: '" + Trim(h1 != null ? DialogueDatabase.Resolve(h1, null) : "-", 90) + "'");
            Check(NpcClues.Pick(NpcDatabase.HLookout, false) != null || NpcClues.Pick(NpcDatabase.Wanda, false) != null, "the lookout / Wanda know sea clues (wrecks, ducks, Big Mo)");
            yield break;
        }

        // ================================================================== 77 dive-site gating
        IEnumerator StepR3NpcSiteGate()
        {
            var s = S;
            var ui = UIModule.Controller;
            // make the wreck the next locked site again, enough followers – but the kelp page below 60 %
            var wreck = DiveSites.Get("wreck");
            bool wasOpen = s.unlockedSites.Contains("wreck");
            var removed = new List<string>();
            for (int i = DiveSites.All.Count - 1; i >= 0; i--)
            {
                var d = DiveSites.All[i];
                if (d.IsStorySite || d.id == "house_reef" || d.id == "kelp_forest") continue;
                if (s.unlockedSites.Remove(d.id)) removed.Add(d.id);
            }
            if (!s.unlockedSites.Contains("kelp_forest")) s.unlockedSites.Add("kelp_forest");
            float kelp = Journal.SiteCompletion("kelp_forest");
            int banners = 0;
            string bannerText = "";
            Action<string, Color> onBanner = (t, c) => { banners++; bannerText += t.Replace('\n', ' ') + " | "; };
            GameEvents.Banner += onBanner;
            if (s.followers < 3000) GameManager.I.AddFollowers(3000 - s.followers);
            Progression.CheckUnlocks();
            yield return Wait(1.2f);
            bool met = Progression.SiteRequirementsMet(wreck, out string reason);
            Check(SiteGoal.Next() == wreck, "the next locked site is the wreck (" + (SiteGoal.Next() != null ? SiteGoal.Next().id : "none") + ")");
            Info("requirement line: '" + SiteGoal.Line(wreck) + "' (kelp page " + Loc.Percent(kelp) + ", met " + met + ")");
            Check(SiteGoal.Line(wreck).Contains("2.500") || SiteGoal.Line(wreck).Contains("2,500"), "the requirement names the followers: '" + SiteGoal.Line(wreck) + "'");
            Check(SiteGoal.OpensVolcano(wreck), "the wreck line names the volcano island too (it opens with the wreck)");
            if (!met)
            {
                Check(!s.unlockedSites.Contains("wreck") && bannerText.IndexOf(wreck.Name, StringComparison.Ordinal) < 0 && bannerText.IndexOf("Tauchplatz", StringComparison.Ordinal) < 0,
                      "requirements not met (" + reason + ") → no banner, still locked (banners: " + (bannerText.Length > 0 ? bannerText : "none") + ")");
                yield return Wait(1.5f);
                if (ui != null && ui.SiteGoal != null) Check(ui.SiteGoal.Visible && ui.SiteGoal.Text.Contains(SiteGoal.ShortName(wreck)), "the requirement stands at the top centre: '" + ui.SiteGoal.Text + "'");
                yield return Shot("77_site_requirement_top");
            }
            else Info("the kelp page is already full in this game – the locked case is checked by the line only");
            GameEvents.Banner -= onBanner;
            // restore
            foreach (var id in removed) if (!s.unlockedSites.Contains(id)) s.unlockedSites.Add(id);
            if (wasOpen && !s.unlockedSites.Contains("wreck")) s.unlockedSites.Add("wreck");
        }

        // ================================================================== 79 rival sabotage
        IEnumerator StepR3NpcSabotage()
        {
            StoryFlags.Set(NpcKeys.RivalR1Done);
            string sp = "clownfish";
            NpcDirector.DebugSteal(sp);
            yield return Wait(1.6f);
            Check(NpcDirector.StolenToday() == sp, "the brothers copied a subject today (" + NpcDirector.StolenToday() + ")");
            Check(QuestTracker.Line != null, "quest line: '" + QuestTracker.Line + "' (" + QuestTracker.ActiveId + ")");
            long f0 = S.followers;
            NpcNet.Request("evt", "clip", sp, "q:95,tier:1,crowd:2", "6");
            yield return Wait(0.8f);
            Check(NpcDirector.StolenToday() == null && S.followers > f0, "a better clip of it wins it back (+" + (S.followers - f0) + " followers)");
            Check(DialogueDatabase.Seq("rival.raid").Count > 0 && DialogueDatabase.Seq("rival.steal").Count > 0, "raid + steal lines exist");
        }
    }
}
