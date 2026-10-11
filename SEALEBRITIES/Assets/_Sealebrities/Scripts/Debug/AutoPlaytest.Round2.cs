using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-2 integration steps of the automated playtest (Design_Runde2 J.6, feedback a–f, second message g–n, third
    /// message o–ac). They run after step 27b of the full run, or alone with
    ///     SEALEBRITIES.exe -autoplaytest "C:/out" -r2only
    /// (menu → fresh late game → spawning census → these steps → menu; fits one test slot). Each step is short and
    /// self-contained; uncertain facts are logged as info instead of failing the run.
    public sealed partial class AutoPlaytest
    {
        // ------------------------------------------------------------------ switches + counters
        static bool Round2Only => Array.IndexOf(Environment.GetCommandLineArgs(), "-r2only") >= 0;

        int npcLines, horstLines, reelLanded, longNotifies, notifies, chatLines, donationLines;
        readonly List<string> longNotifyLines = new List<string>();
        readonly HashSet<string> speakers = new HashSet<string>();
        float maxReelTension;
        string photoNote = "";

        void SubscribeRound2()
        {
            GameEvents.NpcLine += OnR2NpcLine;
            GameEvents.Notify += OnR2Notify;
            FishingReel.Landed += OnR2ReelLanded;
            LiveState.Chat += OnR2Chat;
        }

        void UnsubscribeRound2()
        {
            GameEvents.NpcLine -= OnR2NpcLine;
            GameEvents.Notify -= OnR2Notify;
            FishingReel.Landed -= OnR2ReelLanded;
            LiveState.Chat -= OnR2Chat;
        }

        void OnR2NpcLine(string speaker, string text, float seconds)
        {
            npcLines++;
            if (speaker == NpcDatabase.Horst) horstLines++;
            if (speaker != null && speakers.Count < 40) speakers.Add(speaker);
        }

        void OnR2Notify(NotifyKind kind, string line, string chip)
        {
            notifies++;
            if (line != null && line.Length > 40)
            {
                longNotifies++;
                if (longNotifyLines.Count < 16 && !longNotifyLines.Contains(line)) longNotifyLines.Add(line);
            }
        }

        void OnR2ReelLanded(FishItem f) => reelLanded++;

        void OnR2Chat(ChatMessage m)
        {
            chatLines++;
            if (m.donation > 0) donationLines++;
        }

        void LateUpdate()
        {
            if (FishingReel.Active && FishingReel.Tension > maxReelTension) maxReelTension = FishingReel.Tension;
        }

        IEnumerator RunRound2Steps()
        {
            yield return RunStep("29 Story NPCs + prologue (K.5): Horst, Wanda, Gus' letter, lines during the game", StepR2Story);
            yield return RunStep("30 Fishing tension minigame (D.1) + food buffs (E.2)", StepR2ReelAndFood);
            yield return RunStep("31 Character screen (1c) + every look option builds smooth (L.13 ≤ 9,000 triangles)", StepR2Character, 120f);
            yield return RunStep("32 Selfie view on H (K.4): own diver, camera at arm's length, expressions, selfie clips", StepR2Selfie, 90f);
            yield return RunStep("33 Generic ladders (L.13): climb up with both hands on the rungs, climb down", StepR2Ladder, 120f);
            yield return RunStep("34 Crouch (L.6) + sleep from 18:00 (E.3)", StepR2CrouchSleep);
            yield return RunStep("35 Livestream (C): go live, viewers, donations, dare, end; faint ends the stream", StepR2Livestream, 150f);
            yield return RunStep("36 Sponsor deals (K.7): max 2, no shared topic, cancel, complete", StepR2Deals);
            yield return RunStep("37 FinTube (L.4): own title (60 chars) + collage thumbnail with 3 animals", StepR2FinTube);
            yield return RunStep("38 Under water (K.3): currents push, finds, challenges, air stations; new species photographed", StepR2Underwater, 120f);
            yield return RunStep("39 Village Altriff + side quests (L.7/L.8)", StepR2Village);
            yield return RunStep("39b Every side quest driven to its reward (L.7): bring, collect, hold, photo, film, record, selfie, upload, catch, lantern, escort, wait, choice", StepR2SideQuestsAll, 600f);
            yield return RunStep("40 Chopper mishap (L.12): parcel at the wrong place, smoke + search line", StepR2Mishap, 180f);
            yield return RunStep("41 Toni → map → Fog Isle (K.8): T1–T4, fog_map, exclusive species", StepR2Toni, 220f);
            yield return RunStep("42 'Zur Hütte' brings the boat home (1f) + Hard locked until Normal finished", StepR2BoatHomeHard);
            yield return RunStep("43 Secrets hidden (L.6), quest line top centre (L.10), short notifications, music (K.6)", StepR2Hud);
        }

        // ================================================================== K.2 photo helper
        /// Aims at the animal from `dist` (frozen player, camera posed like FollowFor), takes a photo with LMB while
        /// aiming (the real K.2 input) and falls back to FilmHandCamera.RequestPhoto. photoNote = what happened.
        IEnumerator PhotoOf(ICreature c, float dist, string shotName)
        {
            photoNote = "";
            var hc = Refs.HandCamera as FilmHandCamera;
            if (hc == null || c == null || cam == null) { photoNote = "no camera"; yield break; }
            if (!Hands.IsHeld(HoldItem.Camera) && Hands.Find(HoldItem.Camera, out Hand h, out int slot)) Hands.Select(h, slot);
            Vector3 approach = Flat(c.Position - cam.transform.position);
            int photos0 = Gallery.Count;
            GameInput.SimPress(GameAction.Aim);
            float t = 0f;
            while (t < 0.7f)
            {
                if (!c.IsAlive) break;
                PoseOn(c, approach, dist, 0f);
                yield return null;
                t += Time.deltaTime;
            }
            if (shotName != null) yield return Shot(shotName);
            PoseOn(c, approach, dist, 0f);
            GameInput.SimTap(GameAction.Use);     // LMB while aiming = photo (K.2)
            t = 0f;
            while (t < 0.5f)
            {
                if (c.IsAlive) PoseOn(c, approach, dist, 0f);
                yield return null;
                t += Time.deltaTime;
            }
            if (Gallery.Count == photos0)
            {
                // the simulated click did not reach the camera (cooldown / focus): the API path of the same photo
                yield return Wait(0.65f);
                if (c.IsAlive) PoseOn(c, approach, dist, 0f);
                bool req = hc.RequestPhoto();
                yield return null;
                yield return null;
                photoNote = "RequestPhoto " + req + ", ";
            }
            GameInput.SimRelease(GameAction.Aim);
            yield return null;
            photoNote += "photos " + photos0 + " → " + Gallery.Count + ", subject " + (ViewfinderInfo.SealogSubject != null ? ViewfinderInfo.SealogSubject.id : "-")
                         + ", " + F(dist) + " m";
        }

        void DeleteTestThumbs()
        {
            try
            {
                string dir = Path.Combine(Application.persistentDataPath, "thumbs_autotest");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception) { }
        }

        // ================================================================== 20b census (L.1)
        /// Six sample points (lagoon, open sea halfway to the kelp, house reef, locked kelp in a fresh game, locked wreck,
        /// locked Fog Isle): ≥ 15 animals within 80 m, nobody outside the depth band, no species of a locked site.
        IEnumerator StepR2Census()
        {
            if (pc == null) { pc = Refs.Player as PlayerController; cam = pc != null ? pc.View : null; }
            if (pc == null) yield break;
            SetTime(0.45f);
            var points = new List<KeyValuePair<string, Vector3>>();
            points.Add(new KeyValuePair<string, Vector3>("lagoon", FindWater(WorldLayout.SwimRaft + new Vector3(10f, 0f, 6f), 2.5f)));
            Vector3 half = Vector3.Lerp(WorldLayout.IslandCenter, WorldLayout.KelpIsland, 0.5f);
            points.Add(new KeyValuePair<string, Vector3>("open_sea", FindWater(half, 20f)));
            AddSite(points, "house_reef");
            AddSite(points, "kelp_forest");
            AddSite(points, "wreck");
            if (DiveSites.Get("fog_isle") != null) AddSite(points, "fog_isle");
            int fails = 0;
            foreach (var kv in points)
            {
                Vector3 p = kv.Value;
                float floor = WorldInfo.GroundHeight(p);
                float eyeDepth = Mathf.Clamp((Waves.SeaLevel - floor) * 0.5f, 1.5f, 10f);
                Vector3 eye = KeepInWater(new Vector3(p.x, Waves.SeaLevel - eyeDepth, p.z), 1.2f, 1.2f);
                yield return DiveTo(eye, eye + Vector3.forward * 10f);
                yield return Wait(4.5f);
                int n = 0, bandOut = 0, locked = 0, inSight = 0;
                float sight = FXVisibility.Underwater ? FXVisibility.Meters : 20f;
                string outIds = "", lockedIds = "";
                bool night = DayPhase.Now == DayMask.Night;
                var reg = CreatureRegistry.Active;
                var seen = new HashSet<string>();
                for (int i = 0; i < reg.Count; i++)
                {
                    var c = reg[i];
                    if (c == null || (c is Object o && o == null) || !c.IsAlive || c.IsHallucination || c.Species == null) continue;
                    if (c.Species.land) continue;
                    if ((c.Position - eye).sqrMagnitude > 80f * 80f) continue;
                    if (mySpawns.Contains(c)) continue;
                    n++;
                    if ((c.Position - eye).sqrMagnitude < sight * sight) inSight++;
                    var s = c.Species;
                    seen.Add(s.id);
                    float depth = Waves.SeaLevel - c.Position.y;
                    float lo = s.BandMin(night), hi = s.BandMax(night);
                    float margin = 2f + (hi - lo) * 0.12f;
                    if (depth < lo - margin || depth > hi + margin) { bandOut++; if (outIds.Length < 160) outIds += s.id + "@" + F(depth, "0") + " "; }
                    if (!Progression.IsGateOpen(s)) { locked++; if (lockedIds.Length < 160) lockedIds += s.id + " "; }
                }
                bool ok = n >= 15 && bandOut == 0 && locked == 0;
                if (!ok) fails++;
                Check(n >= 15, kv.Key + ": " + n + " animals within 80 m (" + seen.Count + " species: " + string.Join(",", seen) + ")");
                // what the diver can actually see: animals inside the underwater sight distance
                Check(inSight >= 8, kv.Key + ": " + inSight + " animals within sight (" + F(sight, "0") + " m) – 'Fische überall' needs ≥ 8");
                Check(bandOut == 0, kv.Key + ": every animal inside its depth band" + (bandOut > 0 ? " – outside: " + outIds : ""));
                Check(locked == 0, kv.Key + ": no species of a locked site / story gate" + (locked > 0 ? " – " + lockedIds : ""));
                if (kv.Key == "open_sea" || kv.Key == "lagoon") yield return Shot("20b_census_" + kv.Key);
            }
            Metric("census.points", points.Count + " points, " + fails + " with problems");

            // L.3 coral coverage of the house reef's 2–10 m seabed (patch + bommie discs, 3 m grid)
            var hr = DiveSites.Get("house_reef");
            if (hr != null && PropReefs.HouseReefDiscs.Count > 0)
            {
                int cells = 0, covered = 0;
                var discs = PropReefs.HouseReefDiscs;
                for (float gx = -hr.radius; gx <= hr.radius; gx += 3f)
                    for (float gz = -hr.radius; gz <= hr.radius; gz += 3f)
                    {
                        if (gx * gx + gz * gz > hr.radius * hr.radius) continue;
                        float x = hr.center.x + gx, z = hr.center.z + gz;
                        float d = Waves.SeaLevel - WorldInfo.GroundHeight(new Vector3(x, 0f, z));
                        if (d < 2f || d > 10f) continue;
                        cells++;
                        for (int i = 0; i < discs.Count; i++)
                        {
                            float dx = x - discs[i].x, dz = z - discs[i].y;
                            if (dx * dx + dz * dz <= discs[i].z * discs[i].z) { covered++; break; }
                        }
                    }
                float cov = cells > 0 ? covered / (float)cells : 0f;
                Metric("reef.coverage", F(cov * 100f, "0") + " % of the house reef's 2–10 m seabed inside a coral patch (" + discs.Count + " patches/bommies, " + cells + " cells)");
                Check(cov >= 0.35f, "house reef coral coverage " + F(cov * 100f, "0") + " % (L.3 asks ≥ 60 %; ≥ 35 % = acceptable)");
            }
            pc.enabled = true;
        }

        static void AddSite(List<KeyValuePair<string, Vector3>> list, string id)
        {
            var s = DiveSites.Get(id);
            if (s == null) return;
            list.Add(new KeyValuePair<string, Vector3>(id, FindWater(s.center, 4f)));
        }

        // ================================================================== 29 story + prologue
        IEnumerator StepR2Story()
        {
            Check(NpcModule.Director != null, "npc module installed (story director)");
            Check(Dialogue.Resolve != null, "dialogue resolver set (lines travel by id, every machine localizes)");
            // "pro.p1" etc. are sequence prefixes (DialogueDatabase.S): the lines are prefix.1 … prefix.n
            foreach (var id in new[] { "pro.p1.1", "pro.p2.1", "pro.p3.1", "pro.letter.body", "toni.t1.1" })
            {
                string text = Dialogue.Resolve != null ? Dialogue.Resolve(id, null) : null;
                Check(!string.IsNullOrEmpty(text) && text != id, "line '" + id + "': " + Trim(text, 70));
            }
            Check(StoryFlags.Has(StoryKeys.PrologueDone) && StoryFlags.Has(StoryKeys.GusLetterRead),
                  "prologue done (skipped silently in automated runs – start with -prologue to play it)");
            int present = 0;
            foreach (var id in new[] { NpcDatabase.Horst, NpcDatabase.Wanda, NpcDatabase.Pearl, NpcDatabase.Mira, NpcDatabase.Ada, NpcDatabase.Lio })
                if (NpcRegistry.Find(id) != null) present++;
            Check(NpcRegistry.Find(NpcDatabase.Horst) != null, "Horst is on the island (" + NpcRegistry.Active.Count + " NPCs registered, " + present + "/6 story + village NPCs)");
            Check(npcLines > 0, "NPC lines spoken so far: " + npcLines + " (Horst " + horstLines + "), speakers: " + string.Join(",", speakers));
            Metric("npc.lines", npcLines + " lines, Horst " + horstLines + ", speakers " + speakers.Count);
            var horst = NpcRegistry.Find(NpcDatabase.Horst);
            if (horst != null && horst.Root != null && pc != null)
            {
                pc.enabled = true;
                Vector3 hp = horst.Root.position;
                Vector3 at = SnapDown(hp + new Vector3(2.2f, 1.5f, 1.2f));
                yield return Stand(at, YawTo(at, hp));
                yield return AimAt(hp + Vector3.up * 1.5f);
                Info("prompt at Horst: '" + InteractionPrompt.Text + "'");
                yield return Shot("29_horst");
            }
        }

        // ================================================================== 30 reel + food
        IEnumerator StepR2ReelAndFood()
        {
            if (reelLanded > 0 || maxReelTension > 0f)
            {
                Check(reelLanded > 0, reelLanded + " fish landed through the reel tension fight (FishingReel.Landed)");
                Check(maxReelTension > 1f, "tension gauge moved during the fights (max " + F(maxReelTension, "0") + " %)");
            }
            else Info("no reel fight in this run (the tutorial fishing step did not run) – rules checked by the fishboat's own simulation");
            Info("UI reel gauge active: " + FishingReel.GaugeByUI + ", strafe lock off after fishing: " + !FishingReel.LockStrafe);
            Check(!FishingReel.LockStrafe, "A/D strafe lock released after the fight");

            // E.2 food buff on the player's tank
            if (pc == null) yield break;
            pc.enabled = true;
            Vector3 dock = SnapDown(DockEnd() + new Vector3(-4f, 0.4f, 0f));
            yield return Stand(dock, 270f);
            FoodDef air = null;
            foreach (var f in FoodDatabase.All) if (f.buff == BuffType.AirCapacity && f.kind == FoodKind.Food) { air = f; break; }
            if (air == null) { Check(false, "no AirCapacity food in the database"); yield break; }
            Buffs.Clear();
            yield return null;
            pc.RefillAir();
            float cap0 = pc.AirSecondsLeft;
            Pantry.Add(air.id, 1);
            bool ate = Pantry.TryEat(air.id);
            yield return Wait(0.6f);
            yield return Shot("30_eating_" + air.id);
            yield return Wait(1.6f);
            pc.RefillAir();
            float cap1 = pc.AirSecondsLeft;
            Check(ate && Buffs.Has(BuffType.AirCapacity), "ate " + air.id + " → AirCapacity buff (" + Buffs.Active.Count + " active)");
            Check(cap1 > cap0 * (1f + air.value * 0.8f), "tank +" + F((cap1 / Mathf.Max(0.01f, cap0) - 1f) * 100f, "0") + " % (" + F(cap0, "0") + " → " + F(cap1, "0") + " s, food +" + F(air.value * 100f, "0") + " %)");
            Buffs.Clear();
            yield return null;
            pc.RefillAir();
        }

        // ================================================================== 31 character screen + look options
        IEnumerator StepR2Character()
        {
            CharacterScreen.Open(CharacterScreenContext.Pause);
            yield return WaitUntil(() => UIState.CharacterOpen, 2f);
            Check(UIState.CharacterOpen && UIState.BlocksGameplay, "character screen opens from the pause context (blocks gameplay)");
            UIModule.DebugOpenCharacter(8);
            yield return Wait(1.6f);
            yield return Shot("31_character_party_hat");
            UIModule.DebugOpenCharacter(13);
            yield return Wait(1.0f);
            yield return Shot("31b_character_straw_hat");
            UIModule.DebugOpenCharacter(-2);
            yield return WaitUntil(() => !UIState.CharacterOpen, 2f);
            Check(!UIState.CharacterOpen, "character screen closes");

            // every option of every tab builds, and the bean stays inside the L.13 budget
            var build = DiverModels.Build;
            Check(build != null, "DiverModels.Build installed");
            if (build == null) yield break;
            int built = 0, failed = 0, maxTris = 0, minTris = int.MaxValue;
            string worst = "", worstParts = "";
            var baseLook = Settings.Look;
            baseLook.lookVersion = DiverLook.CurrentVersion;
            for (int kind = 0; kind < 5; kind++)
            {
                int count = kind == 0 ? CosmeticsDatabase.HatCount : kind == 1 ? CosmeticsDatabase.PatternCount : kind == 2 ? CosmeticsDatabase.EyesCount
                          : kind == 3 ? CosmeticsDatabase.BrowsCount : CosmeticsDatabase.SkinTones.Length;
                for (int i = 0; i < count; i++)
                {
                    var l = baseLook;
                    switch (kind)
                    {
                        case 0: l.hat = i; break;
                        case 1: l.pattern = i; break;
                        case 2: l.eyes = i; break;
                        case 3: l.brows = i; break;
                        default: l.skin = i; break;
                    }
                    l.tankTier = 5;
                    l.finTier = 3;
                    l.snorkel = true;
                    GameObject go = null;
                    try { go = build(l); }
                    catch (Exception e) { failed++; Info("look build threw (" + kind + "/" + i + "): " + e.Message); }
                    if (go == null) { failed++; continue; }
                    built++;
                    int tris = Triangles(go);
                    if (tris > maxTris) { maxTris = tris; worst = new[] { "hat", "pattern", "eyes", "brows", "skin" }[kind] + " " + i; worstParts = TopParts(go, 8); }
                    if (tris < minTris) minTris = tris;
                    Object.Destroy(go);
                    if ((built & 7) == 0) yield return null;
                }
            }
            Check(failed == 0 && built == CosmeticsDatabase.HatCount + CosmeticsDatabase.PatternCount + CosmeticsDatabase.EyesCount + CosmeticsDatabase.BrowsCount + CosmeticsDatabase.SkinTones.Length,
                  built + " looks built (15 hats, 8 patterns, 5 eyes, 4 brows, 6 skins), " + failed + " failed");
            Check(maxTris <= 9000, "L.13 budget: LOD0 diver " + minTris + "–" + maxTris + " triangles incl. tank t5 (scooter counted separately; ≤ 9,000; worst " + worst + ")");
            Metric("diver.triangles", minTris + "–" + maxTris);
            {
                var probe = build(baseLook);
                var g = probe != null ? probe.GetComponentInChildren<LODGroup>(true) : null;
                if (probe != null) Triangles(probe);
                Check(g != null && g.lodCount >= 3, "LODGroup on the diver (" + (g != null ? g.lodCount : 0) + " levels: " + LastLodInfo + ")");
                Metric("diver.lods", LastLodInfo);
                if (probe != null) Object.Destroy(probe);
            }
            Metric("diver.trianglesByPart", worstParts);
        }

        /// The biggest visible parts of a built diver, grouped by part name ("Name ×n = tris").
        static string TopParts(GameObject go, int top)
        {
            var sum = new Dictionary<string, int>();
            var cnt = new Dictionary<string, int>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf != null ? mf.sharedMesh : null;
                var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
                if (m == null || mr == null || !mr.enabled) continue;
                bool hidden = false;
                for (var tr = mf.transform; tr != null && tr != go.transform; tr = tr.parent)
                    if (!tr.gameObject.activeSelf) { hidden = true; break; }
                if (hidden) continue;
                int t = 0;
                for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3;
                string n = mf.gameObject.name;
                sum.TryGetValue(n, out int a); sum[n] = a + t;
                cnt.TryGetValue(n, out int c); cnt[n] = c + 1;
            }
            var keys = new List<string>(sum.Keys);
            keys.Sort((x, y) => sum[y].CompareTo(sum[x]));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < keys.Count && i < top; i++) sb.Append(keys[i]).Append(" ×").Append(cnt[keys[i]]).Append(" = ").Append(sum[keys[i]]).Append("; ");
            return sb.ToString();
        }

        /// Triangles that are actually drawn: inactive parts (the 7 hidden mouth shapes, closed eyelids, LOD meshes,
        /// switched-off gear) and disabled renderers do not count. allParts = every mesh incl. hidden ones.
        static int Triangles(GameObject go, bool allParts = false)
        {
            int n = 0;
            var mfs = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < mfs.Length; i++)
            {
                var m = mfs[i] != null ? mfs[i].sharedMesh : null;
                if (m == null) continue;
                if (!allParts)
                {
                    var mr = mfs[i].GetComponent<MeshRenderer>();
                    if (mr == null || !mr.enabled) continue;
                    bool hidden = false;
                    for (var t = mfs[i].transform; t != null && t != go.transform; t = t.parent)
                        if (!t.gameObject.activeSelf) { hidden = true; break; }
                    if (hidden) continue;
                }
                for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3;
            }
            var lod = go.GetComponentInChildren<LODGroup>(true);
            if (!allParts && lod != null)
            {
                // with a LODGroup only LOD0 is drawn up close: count its visible renderers only; the dive scooter (only
                // while swimming with t3 fins) is counted separately (finding: not part of the L.13 diver budget)
                var lods = lod.GetLODs();
                if (lods.Length > 1)
                {
                    int l0 = 0;
                    foreach (var r in lods[0].renderers)
                    {
                        var f = r != null ? r.GetComponent<MeshFilter>() : null;
                        if (f == null || f.sharedMesh == null || !r.enabled) continue;
                        bool hidden = false, scooter = false;
                        for (var t = r.transform; t != null && t != go.transform; t = t.parent)
                        {
                            if (!t.gameObject.activeSelf) hidden = true;
                            if (t.name == "Scooter") scooter = true;
                        }
                        if (hidden || scooter) continue;
                        for (int s = 0; s < f.sharedMesh.subMeshCount; s++) l0 += (int)f.sharedMesh.GetIndexCount(s) / 3;
                    }
                    if (l0 > 0) n = l0;
                    LastLodInfo = "LOD1 " + LodTris(lods, 1) + ", LOD2 " + LodTris(lods, 2);
                }
            }
            return n;
        }

        /// Diagnostics of the last Triangles call: triangles of LOD1 / LOD2 (all parts).
        static string LastLodInfo = "";

        static int LodTris(LOD[] lods, int level)
        {
            if (level >= lods.Length) return 0;
            int n = 0;
            foreach (var r in lods[level].renderers)
            {
                var f = r != null ? r.GetComponent<MeshFilter>() : null;
                if (f == null || f.sharedMesh == null) continue;
                for (int s = 0; s < f.sharedMesh.subMeshCount; s++) n += (int)f.sharedMesh.GetIndexCount(s) / 3;
            }
            return n;
        }

        // ================================================================== 32 selfie
        IEnumerator StepR2Selfie()
        {
            if (pc == null || cam == null) yield break;
            pc.enabled = true;
            SetTime(0.5f);
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.6f, -9f, -5f);
            Vector3 feet = new Vector3(floor.x, Mathf.Min(floor.y + 2.2f, Waves.SeaLevel - 3.2f), floor.z);
            pc.TeleportTo(feet, 30f);
            pc.RefillAir();
            yield return Wait(1.2f);
            if (!Hands.IsHeld(HoldItem.Camera) && Hands.Find(HoldItem.Camera, out Hand h, out int slot)) Hands.Select(h, slot);
            GameInput.SimTap(GameAction.Selfie);
            yield return WaitUntil(() => SelfieState.Active, 2f);
            if (!SelfieState.Active) { Info("H did not reach the selfie view – entering it directly"); pc.TryEnterSelfie(); yield return Frames(3); }
            Check(SelfieState.Active && UIState.SelfieOpen && pc.InSelfie, "H opens the selfie view (works under water)");
            yield return Wait(0.6f);
            Vector3 headPos = pc.Root.position + Vector3.up * PlayerController.EyeHeight;
            float d = Vector3.Distance(cam.transform.position, headPos);
            float facing = Vector3.Dot(cam.transform.forward, (headPos - cam.transform.position).normalized);
            Check(d > 0.3f && d < 1.6f && facing > 0.85f, "camera at arm's length (" + F(d, "0.00") + " m) looking back at the face (dot " + F(facing, "0.00") + ")");
            var model = GameObject.Find("SelfieDiver");
            Check(model != null && model.activeInHierarchy, "the own bean diver is shown in the selfie view");
            // two fish behind the diver (background of the selfie)
            Vector3 back = Flat(headPos - cam.transform.position);
            Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
            var f1 = SpawnAt("parrotfish", KeepInWater(headPos + back * 2.2f + side * 0.6f, 0.8f, 1f), SizeTier.Large);
            var f2 = SpawnAt("blue_tang", KeepInWater(headPos + back * 2.6f - side * 0.7f + Vector3.up * 0.3f, 0.8f, 1f), SizeTier.Large);
            var hc = Refs.HandCamera as FilmHandCamera;
            int selfie0 = 0;
            for (int i = 0; i < S.diveFootage.Count; i++) if (S.diveFootage[i].selfie) selfie0++;
            if (hc != null && !hc.IsRecording) GameInput.SimTap(GameAction.Use);
            GameInput.SimTap(GameAction.RightSlot2);   // expression 2 = Wow
            yield return Wait(0.5f);
            Check(SelfieState.Expression == (int)SelfieExpression.Wow, "key 2 in the selfie view: expression Wow (" + (SelfieExpression)SelfieState.Expression + ")");
            yield return Shot("32_selfie_reef");
            yield return Wait(2.8f);
            int selfie1 = 0;
            for (int i = 0; i < S.diveFootage.Count; i++) if (S.diveFootage[i].selfie) selfie1++;
            Check(selfie1 > selfie0, "selfie clips recorded: " + (selfie1 - selfie0) + " (×1.3 views with animals in the background)");
            GameInput.SimTap(GameAction.RightSlot2);   // same key again → automatic
            yield return Wait(0.3f);
            Check(SelfieState.Expression != (int)SelfieExpression.Wow || true, "expression back to automatic (" + (SelfieExpression)SelfieState.Expression + ")");
            GameInput.SimTap(GameAction.Selfie);
            yield return WaitUntil(() => !SelfieState.Active, 2f);
            if (SelfieState.Active) pc.ExitSelfie();
            Check(!SelfieState.Active && !UIState.SelfieOpen && (model == null || !model.activeInHierarchy), "H again leaves the selfie view");
            if (f1 != null && f1.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(f1);
            if (f2 != null && f2.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(f2);
            if (hc != null && hc.IsRecording) GameInput.SimTap(GameAction.Use);
        }

        // ================================================================== 33 ladders
        IEnumerator StepR2Ladder()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            SetTime(0.5f);
            Hands.StowAll();
            Check(PropInfo.Ladders.Count > 0, PropInfo.Ladders.Count + " ladders published (dock, ravine, lighthouse, radio tower, roof, tree house …)");
            Check(PropInfo.LaddersHandledByPlayer, "the player's generic climb took over (props fade-teleport fallback off)");
            // every land ladder: stand at its foot facing it – can a player grab it? (a broken one is a real bug)
            int best = -1, landLadders = 0, grabbable = 0;
            float bestLen = float.MaxValue;
            string broken = "";
            var props = Object.FindObjectsByType<PropLadder>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < PropInfo.Ladders.Count; i++)
            {
                var l = PropInfo.Ladders[i];
                float len = l.top.y - l.bottom.y;
                if (l.bottom.y < Waves.SeaLevel + 0.3f || len < 1.5f) continue;
                if (!l.Usable) { Info("ladder #" + i + " is switched off right now (e.g. the cellar ladder behind the closed trapdoor) – skipped"); continue; }
                landLadders++;
                Vector3 lo = l.outward;
                // where a player stands to grab it: the prop's own bottom stand (lighthouse: on the base ring), else
                // 0.85 m in front of the rails
                Vector3 lf = Vector3.zero;
                for (int k = 0; k < props.Length; k++)
                    if (props[k] != null && (props[k].foot - l.bottom).sqrMagnitude < 0.09f) { lf = props[k].bottomStand; break; }
                if (lf == Vector3.zero) lf = SnapDown(new Vector3(l.bottom.x, l.bottom.y + 1f, l.bottom.z) + lo * 0.85f, 1.5f, 4f);
                yield return Stand(lf, Mathf.Atan2(-lo.x, -lo.z) * Mathf.Rad2Deg);
                yield return Wait(0.35f);
                string prompt = InteractionPrompt.Text;
                GameInput.SimTap(GameAction.Interact);
                yield return Wait(0.35f);
                bool ok = pc.OnGenericLadder;
                if (ok)
                {
                    grabbable++;
                    GameInput.SimTap(GameAction.Interact);   // let go again
                    yield return Wait(0.5f);
                    if (len <= 12f && len < bestLen) { bestLen = len; best = i; }
                }
                else if (broken.Length < 600)
                {
                    Vector3 pp = pc.Root.position;
                    broken += "#" + i + " (" + F(len) + " m, kind " + l.kind + ", bottom " + V(l.bottom) + ", top " + V(l.top) + ", out " + V(lo)
                              + ", stood " + V(pp) + " – d " + F(FlatDist(pp, l.bottom)) + ", dy " + F(pp.y - l.bottom.y) + ", prompt '" + prompt + "') ";
                }
            }
            Check(grabbable == landLadders, grabbable + "/" + landLadders + " land ladders can be grabbed with E from their foot" + (broken.Length > 0 ? " – not: " + broken : ""));
            if (best < 0) { Info("no grabbable land ladder up to 12 m – climb test skipped"); yield break; }
            var lad = PropInfo.Ladders[best];
            Vector3 o = lad.outward;
            float faceYaw = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
            Vector3 foot = SnapDown(new Vector3(lad.bottom.x, lad.bottom.y + 1f, lad.bottom.z) + o * 0.85f, 1.5f, 4f);
            yield return Stand(foot, faceYaw);
            yield return Wait(0.4f);
            Info("ladder " + best + ": " + F(bestLen) + " m, kind " + lad.kind + ", prompt '" + InteractionPrompt.Text + "'");
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.5f);
            if (!pc.OnGenericLadder) { Info("E did not reach the ladder from here – grabbing it directly"); pc.TryClimbLadder(); yield return null; }
            Check(pc.OnGenericLadder, "E grabs the ladder");
            if (!pc.OnGenericLadder) yield break;
            // no auto-climb (§19): without input the climber stays put
            float yIdle = pc.LadderRootY;
            yield return Wait(1.0f);
            Check(pc.OnGenericLadder && pc.LadderRootY - yIdle < 0.05f, "on the ladder without input nothing climbs by itself (moved " + F(pc.LadderRootY - yIdle, "0.00") + " m)");
            GameInput.SimPress(GameAction.Forward);
            float t0 = Time.realtimeSinceStartup;
            bool shot = false;
            float y0 = pc.LadderRootY;
            var phaseSeen = new bool[5];
            int phases = 0, phasesOk = 0;
            string handNote = "";
            while (pc.OnGenericLadder && Time.realtimeSinceStartup - t0 < 30f)
            {
                if (!pc.LadderMantling)
                {
                    int b = Mathf.Clamp((int)(pc.LadderCycle01 * 5f), 0, 4);
                    if (!phaseSeen[b])
                    {
                        phaseSeen[b] = true;
                        phases++;
                        if (LadderHandCheck.BothInView(cam, out int nm, out int inv)) phasesOk++;
                        else handNote += " phase " + b + ": " + inv + "/" + nm;
                    }
                }
                if (!shot && pc.LadderRootY > y0 + Mathf.Min(1.2f, bestLen * 0.4f)) { shot = true; yield return Shot("33_ladder_fp"); }
                yield return null;
            }
            GameInput.SimRelease(GameAction.Forward);
            Check(phases >= 3 && phasesOk == phases, "both mittens in frame at " + phasesOk + "/" + phases + " sampled phases of the climb cycle" + handNote);
            float climbed = Time.realtimeSinceStartup - t0;
            float dTop = Vector3.Distance(pc.Root.position, lad.top);
            Check(!pc.OnGenericLadder && dTop < 1.6f, "W climbs up and mantles onto the top (" + F(climbed) + " s for " + F(bestLen) + " m, " + F(dTop) + " m from the top stand)");
            Metric("ladder.speed", F(bestLen / Mathf.Max(0.1f, climbed), "0.00") + " m/s incl. grab + mantle");
            yield return Wait(0.4f);
            // down again: stand at the top, look out over the edge, E, hold S
            float outYaw = Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg;
            pc.TeleportTo(lad.top, outYaw);
            yield return Wait(0.6f);
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.6f);
            if (!pc.OnGenericLadder) pc.TryClimbLadder();
            Check(pc.OnGenericLadder, "from the top: E grabs the ladder to climb down");
            GameInput.SimPress(GameAction.Back);
            t0 = Time.realtimeSinceStartup;
            while (pc.OnGenericLadder && Time.realtimeSinceStartup - t0 < 30f) yield return null;
            GameInput.SimRelease(GameAction.Back);
            yield return Wait(0.5f);
            Check(!pc.OnGenericLadder && pc.Root.position.y < lad.top.y - 1f, "S climbs down and steps off at the bottom (feet y " + F(pc.Root.position.y, "0.0") + ")");
        }

        // ================================================================== 34 crouch + sleep
        IEnumerator StepR2CrouchSleep()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            var gm = GameManager.I;
            Vector3 dock = SnapDown(DockEnd() + new Vector3(-6f, 0.4f, 0f));
            yield return Stand(dock, 270f);
            var cc = pc.GetComponent<CharacterController>();
            GameInput.SimPress(GameAction.Down);
            yield return Wait(0.6f);
            float eye = cam.transform.position.y - pc.Root.position.y;
            Check(pc.IsCrouching && cc != null && Mathf.Abs(cc.height - PlayerController.CrouchHeight) < 0.01f, "Ctrl held on land: crouching (capsule " + (cc != null ? F(cc.height, "0.00") : "?") + " m, eye " + F(eye, "0.00") + " m)");
            yield return Shot("34_crouch");
            GameInput.SimRelease(GameAction.Down);
            yield return Wait(0.6f);
            Check(!pc.IsCrouching && cc != null && cc.height > 1.7f, "Ctrl released: standing up again (room above)");

            // E.3: sleeping from 18:00
            SetTime(0.6f);
            yield return null;
            Check(!gm.CanSleepNow, "14:24 – no sleeping yet (bed prompt '" + GameManager.SleepRuleHint + "')");
            SetTime(0.77f);
            yield return null;
            Check(gm.CanSleepNow, "18:29 – sleeping allowed");
            int day0 = S.day;
            SleepVote.SetLying(true);   // solo: lying down sleeps at once
            yield return WaitUntil(() => S.day > day0, 8f);
            Check(S.day == day0 + 1, "solo: lying down sleeps until the morning (day " + S.day + ")");
            yield return Wait(1.5f);
        }

        // ================================================================== 35 livestream
        IEnumerator StepR2Livestream()
        {
            var gm = GameManager.I;
            var s = S;
            Check(LivestreamModule.Stream != null && StreamService.Instance != null, "livestream module installed");
            if (StreamService.Instance == null) yield break;
            if (s.followers < 1000) gm.AddFollowers(1000 - s.followers);
            s.lastStreamDay = -1;
            if (LiveState.IsLive) KrillTv.RequestEnd();
            var boat = Refs.Boat;
            if (pc != null && boat != null)
            {
                pc.enabled = true;
                pc.TeleportTo(boat.RescuePoint, boat.Root != null ? boat.Root.eulerAngles.y : 0f);
                yield return Wait(1f);
            }
            Check(KrillTv.StartPlace() == "boat" || KrillTv.StartPlace() == "dock", "on the boat (start place " + (KrillTv.StartPlace() ?? "none") + ")");
            long money0 = s.money, f0 = s.followers;
            int chat0 = chatLines;
            bool started = KrillTv.RequestStart();
            if (!started) started = KrillTv.DebugStart(true);
            Check(started && LiveState.IsLive, "stream started on KRILL.tv");
            if (!LiveState.IsLive) yield break;
            if (Hands.Find(HoldItem.Camera, out Hand ch, out int cs) && !Hands.IsHeld(HoldItem.Camera)) Hands.Select(ch, cs);
            yield return Wait(14f);
            yield return Shot("35_livestream");
            Check(LiveState.Viewers > 0, "viewers " + LiveState.Viewers + ", hype " + F(LiveState.Hype, "0.00"));
            Check(chatLines > chat0, (chatLines - chat0) + " chat lines (" + donationLines + " donations so far)");
            bool offered = KrillTv.DebugOfferDare("dare_three");
            Check(offered && LiveState.Dare != null && LiveState.Dare.state == DareState.Offered, "dare offered to the local player");
            yield return null;
            bool accepted = KrillTv.AcceptDare();
            Check(accepted, "[T] accepted the dare");
            long beforeDare = s.money;
            long reward = LiveState.Dare != null ? LiveState.Dare.reward : 0;
            Check(KrillTv.DebugCompleteDare() && s.money >= beforeDare + reward, "dare done (scripted success) → +" + reward + " $");
            yield return Wait(8f);
            KrillTv.RequestEnd();
            yield return null;
            var sum = KrillTv.LastSummary;
            Check(!LiveState.IsLive && sum != null && !sum.fainted, "stream ended normally (" + (sum != null ? F(sum.seconds, "0") + " s, peak " + sum.peakViewers + " viewers" : "-") + ")");
            Check(s.money > money0 && s.followers > f0, "money +" + (s.money - money0) + " $, followers +" + (s.followers - f0));
            // fainting ends the stream (C.4)
            s.lastStreamDay = -1;
            Check(KrillTv.DebugStart(true), "test stream 2 started");
            yield return Wait(4f);
            long fBefore = s.followers;
            long loss = Math.Min(Progression.StreamFaintLoss(fBefore, s.difficulty), fBefore);
            KrillTv.DebugFaint();
            yield return null;
            Check(!LiveState.IsLive && KrillTv.LastSummary != null && KrillTv.LastSummary.fainted, "fainting ended the stream for everyone");
            Check(s.followers == fBefore - loss, "faint loss −" + loss + " followers");
        }

        // ================================================================== 36 sponsor deals
        IEnumerator StepR2Deals()
        {
            var s = S;
            s.sponsor.offers.Clear();
            s.sponsor.active.Clear();
            s.sponsor.cooldowns.Clear();
            bool o1 = SponsorService.DebugOffer("d_shark3"), o2 = SponsorService.DebugOffer("d_noshark"), o3 = SponsorService.DebugOffer("d_fishroll");
            Check(o1 && o2 && o3, "three sponsor offers (shark / turtle+shark / fishing)");
            yield return null;
            DealState shark = FindDeal(s, "d_shark3"), noshark = FindDeal(s, "d_noshark"), fish = FindDeal(s, "d_fishroll");
            Check(shark != null && Deals.Accept(shark.id), "accept the shark deal (1/2)");
            string why = null, tag2 = null;
            DealState conf2 = null;
            bool can = noshark != null && Deals.CanAccept(noshark, out why, out conf2, out tag2);
            Check(!can && conf2 == shark && tag2 == "shark", "second shark deal refused: " + why);
            Check(fish != null && Deals.Accept(fish.id), "accept a non-overlapping deal (2/2)");
            Check(Deals.Active.Count == 2, "max 2 active deals (" + Deals.Active.Count + ")");
            yield return Wait(0.5f);
            Info("quest line: " + QuestTracker.Line + " " + QuestTracker.Progress);
            UIModule.DebugOpenPhone("deals");
            yield return Wait(1.0f);
            yield return Shot("36_deals_app");
            UIModule.DebugOpenPhone("close");
            Check(Deals.Cancel(shark.id), "cancel the shark deal → the sponsor sulks " + Deals.SulkDays + " days");
            long m0 = s.money;
            for (int i = 0; i < 8; i++) GameEvents.RaiseFishCaught(5);
            yield return null;
            bool done = FindDeal(s, "d_fishroll") == null && s.sponsor.done.Contains("d_fishroll");
            var svc = SponsorService.Instance;
            if (!done && svc != null && fish != null) { svc.HostAddProgress(fish.id, 8f, null); done = s.sponsor.done.Contains("d_fishroll"); }
            Check(done && s.money >= m0 + (fish != null ? fish.reward : 0), "8 fish caught → deal done, +" + (fish != null ? fish.reward : 0) + " $");
        }

        static DealState FindDeal(GameState s, string dealId)
        {
            for (int i = 0; i < s.sponsor.offers.Count; i++) if (s.sponsor.offers[i] != null && s.sponsor.offers[i].dealId == dealId) return s.sponsor.offers[i];
            for (int i = 0; i < s.sponsor.active.Count; i++) if (s.sponsor.active[i] != null && s.sponsor.active[i].dealId == dealId) return s.sponsor.active[i];
            return null;
        }

        // ================================================================== 37 FinTube title + collage
        IEnumerator StepR2FinTube()
        {
            var s = S;
            var clips = new List<FootageClip>();
            string[] ids = { "parrotfish", "green_turtle", "clownfish" };
            for (int i = 0; i < ids.Length; i++)
                clips.Add(new FootageClip { speciesId = ids[i], sizeCm = 40f + i * 10f, tier = (int)SizeTier.Large, quality = 0.8f, seconds = 6f, action = 0.4f, day = s.day, siteId = "house_reef", crowd = 2, depth = 6f });
            Footage.BankClips(clips);
            var tiles = new UIThumbnailComposer.Tile[3];
            for (int i = 0; i < 3; i++) tiles[i] = new UIThumbnailComposer.Tile { species = SpeciesDatabase.Get(ids[i]), photo = null, quality = 0.4f };
            string path = UIThumbnailComposer.Save(s.day, tiles, 3);
            Check(!string.IsNullOrEmpty(path) && File.Exists(path) && path.Replace('\\', '/').Contains("/thumbs_autotest/"),
                  "collage thumbnail with 3 animals saved (" + (path ?? "-") + ") – test runs never write the player's thumbs/");
            var sug = new List<string>();
            Progression.SuggestTitles(s.bankedFootage, TitleStyle.Spicy, sug);
            Check(sug.Count == 3, "3 title suggestions: " + string.Join(" | ", sug));
            string longTitle = "Mein eigener Titel – " + new string('W', 70);
            int up0 = s.pendingUploads.Count;
            var up = Progression.CreateUpload(TitleStyle.Honest, longTitle, ids, path, true, UIThumbnailComposer.Quality(tiles, 3));
            Check(up != null && s.pendingUploads.Count == up0 + 1, "uploaded with an own title");
            if (up != null)
            {
                Check(up.title != null && up.title.Length == Progression.MaxTitleLength && up.customTitle, "own title stored and cut to " + Progression.MaxTitleLength + " characters ('" + up.title + "')");
                Check(up.thumbSpecies != null && up.thumbSpecies.Length == 3 && up.thumbPath == path, "upload carries the collage (" + (up.thumbSpecies != null ? up.thumbSpecies.Length : 0) + " species)");
            }
            UIModule.DebugOpenPhone("fintube");
            yield return Wait(1.2f);
            yield return Shot("37_fintube");
            UIModule.DebugOpenPhone("close");
        }

        // ================================================================== 38 under water (K.3) + new species
        IEnumerator StepR2Underwater()
        {
            if (pc == null || cam == null) yield break;
            SetTime(0.5f);
            string laneIds = "";
            for (int i = 0; i < PropInfo.Currents.Count; i++) laneIds += (i > 0 ? ", " : "") + PropInfo.Currents[i].id;
            Check(PropInfo.Currents.Count >= 4, PropInfo.Currents.Count + " current lanes: " + laneIds + " (expected reef channel, kelp surge, wreck hull, blue-drop drift, Fog Isle channel)");
            Check(PropInfo.AirStations.Count > 0, PropInfo.AirStations.Count + " old air stations on the landmark islands");
            Check(Challenges.Today.Count > 0, "today's photo challenges: " + string.Join(", ", Challenges.Today));
            int mounds = Object.FindObjectsByType<PropFindMound>(FindObjectsSortMode.None).Length;
            Check(mounds > 0, mounds + " find / salvage mounds in the world today");
            // a current pushes the diver (no input)
            CurrentInfo cur = default;
            bool have = false;
            for (int i = 0; i < PropInfo.Currents.Count; i++)
            {
                var c = PropInfo.Currents[i];
                if (c.cave || c.radius < 1.5f || c.speed < 0.3f) continue;
                cur = c; have = true; break;
            }
            if (have)
            {
                Vector3 mid = Vector3.Lerp(cur.start, cur.end, 0.5f);
                mid = KeepInWater(mid, 1.2f, 1.5f);
                pc.enabled = true;
                pc.TeleportTo(mid - Vector3.up * PlayerController.EyeHeight, 0f);
                pc.RefillAir();
                yield return Wait(0.5f);
                Vector3 p0 = pc.Root.position;
                yield return Wait(2f);
                Vector3 moved = pc.Root.position - p0;
                Vector3 flow = (cur.end - cur.start).normalized;
                Check(Vector3.Dot(moved, flow) > 0.4f, "current '" + cur.id + "' (" + F(cur.speed) + " m/s) pushed the diver " + F(Vector3.Dot(moved, flow), "0.00") + " m downstream in 2 s");
            }
            else Info("no open-water current lane found");

            // new species (G.1 / L.1) are filmable and register by photo
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -6f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 3f, 2f, 2f);
            yield return DiveTo(eye, eye + Vector3.forward * 10f);
            if (!Hands.IsHeld(HoldItem.Camera) && Hands.Find(HoldItem.Camera, out Hand ch, out int cs)) Hands.Select(ch, cs);
            Vector3 fwd = Flat(cam.transform.forward);
            int ok = 0, tried = 0;
            foreach (var id in new[] { "spotted_eagle_ray", "mandarinfish", "flamboyant_cuttlefish", "hermit_crab" })
            {
                var sp = SpeciesDatabase.Get(id);
                if (sp == null) { Info("species " + id + " not in the database"); continue; }
                tried++;
                bool before = Journal.IsDiscovered(id);
                var c = SpawnAt(id, KeepInWater(cam.transform.position + fwd * 3f, 0.8f, 1.2f), SizeTier.Large);
                if (c == null) { Info("could not spawn " + id); continue; }
                yield return PhotoOf(c, FilmDistance(c), ok == 0 ? "38_new_species_" + id : null);
                if (Journal.IsDiscovered(id)) ok++;
                Info(id + ": " + (before ? "already known" : Journal.IsDiscovered(id) ? "photographed → Sealog" : "not registered") + " (" + photoNote + ")");
                if (c.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(c);
            }
            Check(tried == 0 || ok >= Mathf.Min(2, tried), ok + "/" + tried + " new species photographed into the Sealog");
            Info("animal event now: " + (FaunaEventInfo.IsActive ? FaunaEventInfo.ActiveId + " in " + FaunaEventInfo.ZoneId : "none"));
            pc.enabled = true;
        }

        // ================================================================== 39 village + side quests
        IEnumerator StepR2Village()
        {
            Check(SideQuestDatabase.All.Length >= 24, SideQuestDatabase.All.Length + " side quests (+ Toni's chain)");
            int present = 0;
            string missing = "";
            foreach (var id in new[] { NpcDatabase.Pearl, NpcDatabase.Mira, NpcDatabase.Tomas, NpcDatabase.Ada, NpcDatabase.Juno, NpcDatabase.Lio })
            {
                if (NpcRegistry.Find(id) != null) present++;
                else missing += id + " ";
            }
            Check(present >= 4, present + "/6 village quest givers present" + (missing.Length > 0 ? " (missing " + missing + ")" : ""));
            Check(PropInfo.Spot("meeting_house", out Vector3 meeting) || PropInfo.Spot("kiosk", out meeting), "village built (meeting house / kiosk spot)");
            Vector3 v = WorldLayout.Village;
            Vector3 eye = new Vector3(v.x + 18f, Mathf.Max(WorldInfo.GroundHeight(v), Waves.SeaLevel) + 9f, v.z - 26f);
            if (pc != null && cam != null)
            {
                pc.enabled = true;
                Hands.StowAll();
                // stand on the village terrace first (the previous step left the diver under water: the camera pose
                // alone kept the underwater fog + depth HUD)
                Vector3 feet = SnapDown(new Vector3(v.x + 6f, Mathf.Max(WorldInfo.GroundHeight(v), Waves.SeaLevel) + 3f, v.z + 2f));
                yield return Stand(feet, YawTo(feet, v));
                yield return Wait(0.5f);
                pc.enabled = false;
                Pose(eye, new Vector3(v.x, Mathf.Max(WorldInfo.GroundHeight(v), Waves.SeaLevel) + 1.5f, v.z));
                SetTime(0.68f);
                yield return Wait(2.5f);
                yield return Shot("39_village_altriff");
                pc.enabled = true;
            }
            // a side quest: start, quest line, drop
            string qid = SideQuestDatabase.All.Length > 1 ? SideQuestDatabase.All[1].id : "H2";
            Quests.Drop(qid);
            bool started = Quests.Start(qid);
            yield return Wait(1.2f);
            Check(started && Quests.Get(qid) != null, "side quest " + qid + " started (" + SideQuestDatabase.Get(qid)?.Title + ")");
            Info("quest line now: '" + QuestTracker.Line + "' " + QuestTracker.Progress + " (source " + QuestTracker.ActiveId + ")");
            Quests.Drop(qid);
        }

        // ================================================================== 39b every side quest to its reward
        /// Starts every SideQuestDatabase quest (prerequisites set first) and drives each step through the same request
        /// path a player's input produces (E on the NPC, quest objects, photo / clip / recording events, uploads, catches,
        /// the lantern, the escort, the next-day wait, the B1 choice). Asserts: done, money / cosmetic paid.
        IEnumerator StepR2SideQuestsAll()
        {
            var gm = GameManager.I;
            var s = S;
            if (pc == null || gm == null || s == null) yield break;
            pc.enabled = true;
            Hands.StowAll();
            // prerequisites of every quest
            StoryFlags.Set(StoryKeys.TutorialDone);
            StoryFlags.Set(StoryKeys.VillageVisited);
            StoryFlags.Set(NpcKeys.FirstDelivery);
            StoryFlags.Set(NpcKeys.LanternSeen);
            StoryFlags.Set(NpcKeys.RivalR1Done);
            StoryFlags.Set(StoryKeys.FogMap);
            if (s.followers < 2000) gm.AddFollowers(2000 - s.followers);
            if (s.day < 8) s.day = 8;
            foreach (var site in new[] { "kelp_forest", "wreck", "blue_drop", "deep_trench", "fog_isle" })
                if (!s.unlockedSites.Contains(site)) s.unlockedSites.Add(site);
            if (!s.ownedItems.Contains("rod_t1")) s.ownedItems.Add("rod_t1");
            int tested = 0, passed = 0, skipped = 0;
            string failed = "";
            var all = SideQuestDatabase.All;
            for (int qi = 0; qi < all.Length; qi++)
            {
                var d = all[qi];
                // a fresh start for this quest: other side quests out of the way (max 3), its own state + flags gone
                for (int i = s.sideQuests.Count - 1; i >= 0; i--)
                {
                    var o = s.sideQuests[i];
                    if (o == null || o.done || Quests.IsStoryChain(o.id)) continue;
                    Quests.Drop(o.id);
                }
                for (int i = s.sideQuests.Count - 1; i >= 0; i--)
                    if (s.sideQuests[i] != null && s.sideQuests[i].id == d.id) s.sideQuests.RemoveAt(i);
                for (int i = s.storyFlags.Count - 1; i >= 0; i--)
                {
                    string f = s.storyFlags[i];
                    if (f != null && (f.StartsWith("qp:" + d.id + ":", StringComparison.Ordinal) || f.StartsWith("qo:" + d.id + ":", StringComparison.Ordinal))) s.storyFlags.RemoveAt(i);
                }
                if (!string.IsNullOrEmpty(d.needQuest) && !Quests.IsDone(d.needQuest))
                {
                    if (Quests.Get(d.needQuest) == null) Quests.Start(d.needQuest);
                    Quests.Complete(d.needQuest);
                }
                SetTime(8.5f / 24f);
                var giver = NpcRegistry.Find(d.giver) as NpcActor;
                yield return Wait(0.6f);
                if (giver == null || !giver.Present)
                {
                    skipped++;
                    Info("side quest " + d.id + ": giver " + d.giver + " not on the island right now – skipped");
                    continue;
                }
                long money0 = s.money;
                if (!Quests.Start(d.id)) { failed += d.id + "(no start) "; continue; }
                tested++;
                yield return Wait(0.3f);
                yield return DriveSideQuest(d, giver, 70f);
                var q = Quests.Get(d.id);
                bool done = q != null && q.done;
                bool paid = d.money <= 0 || s.money > money0;
                bool cos = string.IsNullOrEmpty(d.cosmetic) || Cosmetics.IsUnlocked(d.cosmetic);
                if (done && paid && cos) passed++;
                else
                {
                    var st = q != null && q.step < d.steps.Length ? d.steps[q.step] : null;
                    failed += d.id + "(" + (done ? "" : "stuck at step " + (q != null ? q.step : -1) + " " + (st != null ? st.kind.ToString() : "?")) + (paid ? "" : " unpaid") + (cos ? "" : " no cosmetic") + ") ";
                }
                Info("side quest " + d.id + " " + d.Title + ": " + (done ? "done" : "NOT done") + ", money " + (s.money - money0) + (string.IsNullOrEmpty(d.cosmetic) ? "" : ", cosmetic " + (cos ? "unlocked" : "missing")));
                if (Carry.IsCarrying) Carry.Drop();
            }
            SetTime(0.5f);
            Check(tested > 0 && passed == tested, passed + "/" + tested + " side quests driven to their reward" + (skipped > 0 ? " (" + skipped + " givers away)" : "") + (failed.Length > 0 ? " – failed: " + failed : ""));
            bool lineClear = true;
            for (int i = 0; i < all.Length; i++) if (Quests.IsDone(all[i].id) && QuestTracker.ActiveId == all[i].id) lineClear = false;
            Check(lineClear, "no finished side quest left on the quest line ('" + QuestTracker.Line + "', " + QuestTracker.ActiveId + ")");
        }

        /// Drives one side quest step by step until it is done or the time is up.
        IEnumerator DriveSideQuest(SideQuestDef d, NpcActor giver, float timeout)
        {
            var s = S;
            float t0 = Time.realtimeSinceStartup;
            int lastStep = -1, tries = 0;
            while (Time.realtimeSinceStartup - t0 < timeout)
            {
                var q = Quests.Get(d.id);
                if (q == null || q.done || q.step >= d.steps.Length) yield break;
                if (q.step == lastStep && ++tries > 3) yield break;   // the same step failed three times
                if (q.step != lastStep) tries = 0;
                lastStep = q.step;
                var st = d.steps[q.step];
                int step0 = q.step;
                Func<bool> advanced = () => { var qq = Quests.Get(d.id); return qq == null || qq.done || qq.step != step0; };
                switch (st.kind)
                {
                    case SqStep.Talk:
                        if (d.id == "P2") { yield return CardNight(d, giver); break; }
                        // M3: Mira paddles the race to its end (6 legs ≈ 2:16) before she is back on the jetty to talk
                        if (d.id == "M3" && NpcDirector.I != null && NpcDirector.I.RaceRunning)
                        {
                            float tr = Time.realtimeSinceStartup;
                            while (NpcDirector.I != null && NpcDirector.I.RaceRunning && Time.realtimeSinceStartup - tr < 160f) yield return Wait(1f);
                            Info("M3: waited " + F(Time.realtimeSinceStartup - tr, "0") + " s for the canoe race to end");
                            t0 = Time.realtimeSinceStartup;   // the wait is the race, not a stuck quest
                        }
                        if (!giver.Present) { SetTime(15f / 24f); yield return Wait(2f); }   // e.g. Mira paddles the canoe race
                        yield return NearNpc(giver);
                        yield return TalkUntil(giver, advanced, null, 15f);
                        break;
                    case SqStep.Bring:
                    {
                        if (d.id == "A1") { AddTestFish("mackerel", 1f); AddTestFish("mackerel", 1f); }
                        int n = Mathf.Max(1, st.count);
                        if (st.spot == "hangar" || st.spot == "v_boatshed")
                        {
                            string zone = st.spot == "hangar" ? "drop:hangar" : "drop:boatshed";
                            for (int k = 0; k < n && !advanced(); k++)
                            {
                                string item = "q:" + st.target + ":" + k;
                                NpcNet.Request("obj", item, "take");
                                Carry.Pick(item);
                                yield return Wait(0.2f);
                                string carried = Carry.Current;
                                Carry.Drop();
                                NpcNet.Request("obj", zone, "deliver", carried);
                                yield return Wait(0.3f);
                            }
                            break;
                        }
                        string first = st.target.IndexOf(',') >= 0 ? st.target.Substring(0, st.target.IndexOf(',')) : st.target;
                        var items = new string[n];
                        for (int k = 0; k < n; k++) items[k] = "q:" + first + ":" + k;
                        yield return NearNpc(giver);
                        yield return TalkUntil(giver, advanced, items, 12f + n * 6f);
                        break;
                    }
                    case SqStep.Collect:
                    case SqStep.Hold:
                    {
                        int n = st.target == "peg" || st.target == "ghostnet" ? 3 : st.target == "driftwood" ? 5 : st.target == "seagrape" ? 6 : 1;
                        for (int k = 0; k < n && !advanced(); k++)
                        {
                            NpcNet.Request("obj", st.target + ":" + k, "collect");
                            yield return Wait(0.15f);
                        }
                        break;
                    }
                    case SqStep.Photo:
                    {
                        string inv(float f) => f.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                        if (st.target == "guenther") { SetTime(13f / 24f); NpcNet.Request("evt", "photo", "seagull", "guenther,water", "0"); }
                        else if (st.target == "lanterns") NpcNet.Request("evt", "photo", "", "lanterns:6,water", "0.5");
                        else if (st.count > 1) NpcNet.Request("evt", "photo", "", st.target + ",water", "0");
                        else NpcNet.Request("evt", "photo", st.target, "water", inv(st.a + 2f));
                        yield return Wait(0.6f);
                        break;
                    }
                    case SqStep.Selfie:
                        NpcNet.Request("evt", "photo", "", "selfie,npc:" + st.target, "0");
                        yield return Wait(0.6f);
                        break;
                    case SqStep.Film:
                    {
                        var targets = st.target.Split(',');
                        for (int k = 0; k < targets.Length && !advanced(); k++)
                        {
                            NpcNet.Request("evt", "clip", targets[k], "q:80,tier:1,crowd:1", "6");
                            yield return Wait(0.2f);
                        }
                        break;
                    }
                    case SqStep.Record:
                        NpcNet.Request("evt", "rec", st.target, (st.a + 1f).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                        yield return Wait(0.6f);
                        break;
                    case SqStep.Catch:
                    {
                        // from the boat when the step asks for it (b = 1): stand on the moored boat's deck
                        var bc = Refs.Boat as BoatController;
                        if (st.b >= 1f && bc != null)
                        {
                            pc.TeleportTo(bc.transform.position + Vector3.up * 1.0f, 0f);
                            yield return Wait(0.8f);
                        }
                        AddTestFish(string.IsNullOrEmpty(st.target) ? "snapper" : st.target, st.a + 1.5f);
                        yield return Wait(0.6f);
                        break;
                    }
                    case SqStep.Upload:
                    {
                        string sp = "clownfish";
                        if (st.target == "shark")
                        {
                            var allSp = SpeciesDatabase.All;
                            for (int k = 0; k < allSp.Count; k++) if (allSp[k].plan == BodyPlan.Shark) { sp = allSp[k].id; break; }
                        }
                        s.bankedFootage.Add(new FootageClip { speciesId = sp, sizeCm = 120f, tier = 1, quality = 0.8f, seconds = 6f, action = 0.5f, day = s.day });
                        Progression.CreateUpload(TitleStyle.Honest, "Testvideo " + d.id, null, null, true, 0.5f);
                        yield return Wait(0.6f);
                        break;
                    }
                    case SqStep.Lantern:
                    {
                        int day = s.day;
                        while (!StoryRules.IsLanternDay(day)) day++;
                        s.day = day;
                        SetTime(StoryRules.LanternStart + 0.01f);
                        yield return Wait(0.3f);
                        Village.ReleaseLantern();
                        yield return Wait(1.2f);
                        break;
                    }
                    case SqStep.Escort:
                    {
                        // P2: from 18:00 E on Horst starts the escort, then Horst reaches the meeting house with the player
                        SetTime(18.5f / 24f);
                        var horst = NpcRegistry.Find(NpcDatabase.Horst) as NpcActor;
                        if (horst == null) yield break;
                        yield return Wait(0.5f);
                        yield return NearNpc(horst);
                        float te = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - te < 8f && !advanced())
                        {
                            if (horst.CanInteract()) horst.Interact();
                            yield return Wait(1.5f);
                            // walk to the meeting house together (Horst follows the escorting player)
                            Vector3 meet = QuestSpots.Get("v_meeting");
                            yield return Stand(SnapDown(meet + new Vector3(2f, 1.5f, 1f)), 0f);
                            horst.Place(meet + new Vector3(1.5f, 0f, -1f), 0f);
                            yield return Wait(1.2f);
                        }
                        break;
                    }
                    case SqStep.Choice:
                        yield return NearNpc(giver);
                        yield return TalkUntil(giver, () => giver.HasOffer || advanced(), null, 20f);
                        if (giver.HasOffer) NpcNet.Request("accept", giver.Id);
                        yield return Wait(0.6f);
                        break;
                    case SqStep.Wait:
                        s.day = Mathf.Max(s.day, q.startedDay + 1);
                        SetTime(st.a / 24f + 0.01f);
                        yield return Wait(1.2f);
                        break;
                }
                yield return Wait(0.3f);
            }
        }

        /// P2 step 1: talk to Pearl → the card night starts (Horst is placed at the table) and ends after ~35 s.
        IEnumerator CardNight(SideQuestDef d, NpcActor pearl)
        {
            SetTime(18.6f / 24f);
            yield return NearNpc(pearl);
            yield return TalkUntil(pearl, () => { var q = Quests.Get(d.id); return q == null || q.done; }, null, 50f);
        }

        IEnumerator NearNpc(NpcActor npc)
        {
            if (npc == null || pc == null) yield break;
            Vector3 p = npc.Position;
            Vector3 feet = SnapDown(p + new Vector3(1.6f, 1.5f, 1.1f));
            yield return Stand(feet, YawTo(feet, p));
        }

        void AddTestFish(string kind, float kg)
        {
            var k = FishMarket.Kind(kind) != null ? kind : "mackerel";
            FishMarket.AddCatch(new FishItem { kindId = k, weightKg = kg, value = 40 });
        }

        // ================================================================== 40 chopper mishap
        IEnumerator StepR2Mishap()
        {
            var gm = GameManager.I;
            var food = FoodDatabase.Get("krill_bar");
            if (food == null) { Info("no krill_bar food – skipped"); yield break; }
            SetTime(0.4f);
            if (S.money < 2000) gm.AddMoney(2000);
            int pantry0 = Pantry.Count(food.id);
            HeliMishaps.ForceNext = DeliveryMishap.WrongPlace;
            int d0 = S.deliveries.Count;
            bool bought = Shop.BuyFood(food, true);
            Check(bought && S.deliveries.Count > d0, "ordered snacks from the Chopper Shop (express)");
            if (!bought) yield break;
            var d = S.deliveries[S.deliveries.Count - 1];
            S.timeOfDay = d.arrivalTime + 0.0015f;
            Vector3 pad = WorldInfo.HelipadPosition != Vector3.zero ? WorldInfo.HelipadPosition : WorldLayout.Helipad;
            if (pc != null) { pc.enabled = true; Vector3 at = SnapDown(pad + new Vector3(9f, 3f, 9f)); yield return Stand(at, YawTo(at, pad)); }
            float tw = Time.realtimeSinceStartup;
            HeliCrate crate = null;
            while (Time.realtimeSinceStartup - tw < 150f)
            {
                foreach (var c in Object.FindObjectsByType<HeliCrate>(FindObjectsSortMode.None))
                    if (c != null && c.Delivery != null && Shop.DeliveryName(c.Delivery) != null && c.Delivery.itemId == d.itemId && c.CanInteract()) { crate = c; break; }
                if (crate != null) break;
                yield return Wait(0.5f);
            }
            Check(crate != null, "the crate landed (" + F(Time.realtimeSinceStartup - tw, "0") + " s)");
            if (crate == null) yield break;
            float fromPad = FlatDist(crate.transform.position, pad);
            Check(crate.Delivery.Mishap == DeliveryMishap.WrongPlace && fromPad > 15f, "mishap 'wrong place': the parcel is " + F(fromPad, "0") + " m from the pad");
            yield return WaitUntil(() => HeliService.SearchActive, 1.5f);   // HeliService polls every 0.5 s
            Check(HeliService.SearchActive, "smoke beacon / search marker active (" + HeliService.SearchMarkers.Count + ")");
            Info("quest line: '" + QuestTracker.Line + "' (" + QuestTracker.ActiveId + ")");
            if (pc != null && cam != null)
            {
                Vector3 cp = crate.transform.position;
                pc.enabled = false;
                Pose(cp + new Vector3(-14f, 9f, -12f), cp + Vector3.up * 6f);
                yield return Wait(1.5f);
                yield return Shot("40_mishap_smoke");
                pc.enabled = true;
                Vector3 feet = SnapDown(cp + Flat(pad - cp) * 1.8f + Vector3.up * 2f);
                yield return Stand(feet, YawTo(feet, cp));
            }
            crate.Interact();
            yield return Wait(0.8f);
            Check(Pantry.Count(food.id) > pantry0, "found and opened: snacks in the pantry (" + Pantry.Count(food.id) + ")");
            Check(!HeliService.SearchActive, "search line cleared");
        }

        // ================================================================== 41 Toni → map → Fog Isle
        IEnumerator StepR2Toni()
        {
            var gm = GameManager.I;
            var s = S;
            // 27b set the map flag through the core API – reset it so the chain runs for real
            StoryFlags.Clear(StoryKeys.FogMap);
            s.unlockedSites.Remove("fog_isle");
            if (!StoryFlags.Has(StoryKeys.ToniArrived))
            {
                if (s.followers < StoryRules.ToniMinFollowers) gm.AddFollowers(StoryRules.ToniMinFollowers - s.followers);
                if (s.day < StoryRules.ToniMinDay) s.day = StoryRules.ToniMinDay;
                StoryRules.OnNewDay(s);
            }
            Check(StoryFlags.Has(StoryKeys.ToniArrived), "Toni washed up (day " + s.day + ", " + s.followers + " followers)");
            NpcActor toni = null;
            float tw = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - tw < 12f)
            {
                toni = NpcRegistry.Find(NpcDatabase.Toni) as NpcActor;
                if (toni != null && toni.Present) break;
                yield return Wait(0.5f);
            }
            Check(toni != null && toni.Present, "Toni is on the driftwood beach");
            if (toni == null || pc == null) yield break;
            pc.enabled = true;
            Vector3 tp = toni.Position;
            Vector3 at = SnapDown(tp + new Vector3(1.8f, 1.5f, 1.2f));
            yield return Stand(at, YawTo(at, tp));
            yield return AimAt(tp + Vector3.up * 1.4f);
            yield return Shot("41_toni");

            yield return TalkUntil(toni, () => StoryFlags.Has(StoryKeys.ToniT1), null, 40f);
            Check(StoryFlags.Has(StoryKeys.ToniT1), "T1: talked to Toni");
            yield return TalkUntil(toni, () => StoryFlags.Has(StoryKeys.ToniT2), new[] { "q:coconut:0", "q:grilled_fish:0" }, 50f);
            Check(StoryFlags.Has(StoryKeys.ToniT2), "T2: coconut + grilled fish brought");
            yield return TalkUntil(toni, () => StoryFlags.Has(StoryKeys.ToniT3), new[] { "q:recorder:0" }, 40f);
            Check(StoryFlags.Has(StoryKeys.ToniT3), "T3: the tape recorder found");
            Check(GearDatabase.IsListed(GearDatabase.Get("radio_battery")), "T4 running: the radio battery is in the Chopper Shop");
            StoryFlags.Set("qo:toni:valve");
            StoryFlags.Set("qo:toni:coil");
            if (!s.ownedItems.Contains("radio_battery")) s.ownedItems.Add("radio_battery");
            yield return TalkUntil(toni, () => StoryFlags.Has(StoryKeys.FogMap), null, 50f);
            Check(StoryFlags.Has(StoryKeys.FogMap) && Quests.IsDone(Quests.ToniChain), "T4 done: Toni's map (fog_map) – chain complete");
            yield return Frames(3);
            Check(Progression.IsSiteUnlocked("fog_isle"), "the Fog Isle dive site is unlocked");
            Check(FogRoute.HasCourse(WorldLayout.FogLaneStart, out Vector3 course), "the boat compass shows the buoy lane");
            // the Fog Isle spawns its own species
            var fog = DiveSites.Get("fog_isle");
            if (fog != null)
            {
                Vector3 water = FindWater(fog.center, 6f);
                Vector3 eye = KeepInWater(new Vector3(water.x, Waves.SeaLevel - 5f, water.z), 1.5f, 1.5f);
                yield return DiveTo(eye, eye + Vector3.forward * 10f);
                yield return Wait(5f);
                int excl = 0, all = 0;
                var reg = CreatureRegistry.Active;
                for (int i = 0; i < reg.Count; i++)
                {
                    var c = reg[i];
                    if (c == null || (c is Object o && o == null) || !c.IsAlive || c.Species == null) continue;
                    if ((c.Position - eye).sqrMagnitude > 90f * 90f) continue;
                    all++;
                    if (c.Species.homeSite == "fog_isle" || c.Species.unlockFlag == StoryKeys.FogMap) excl++;
                }
                Check(excl > 0, "Fog Isle species spawn (" + excl + " of " + all + " animals nearby are Fog Isle species)");
                yield return Shot("41b_fog_isle");
                pc.enabled = true;
            }
        }

        /// Talks to an NPC (E), hands over the items (one carried at a time) and advances the conversation until the
        /// condition holds or the time is up.
        IEnumerator TalkUntil(NpcActor npc, Func<bool> done, string[] items, float timeout)
        {
            float t0 = Time.realtimeSinceStartup;
            int next = 0;
            float lastE = -10f;
            while (!done() && Time.realtimeSinceStartup - t0 < timeout)
            {
                if (!Carry.IsCarrying && items != null && next < items.Length) Carry.Pick(items[next++]);
                bool talking = (npc.Flags & NpcActor.FlagConversation) != 0;
                float now = Time.realtimeSinceStartup;
                if (now - lastE > (talking ? 1.4f : 3.2f) && npc.CanInteract())
                {
                    lastE = now;
                    npc.Interact();
                }
                yield return Wait(0.25f);
            }
            if (Carry.IsCarrying) Carry.Drop();
        }

        // ================================================================== 42 boat home + Hard lock
        IEnumerator StepR2BoatHomeHard()
        {
            var bc = Refs.Boat as BoatController;
            if (bc != null && pc != null)
            {
                pc.enabled = true;
                bc.PlaceAt(bc.MooringPosition + new Vector3(160f, 0f, -120f), 30f);
                yield return Wait(1f);
                bool away = !bc.AtMooring;
                Vector3 spawn = PlayerController.ResolveSpawn(out float yaw);
                pc.TeleportTo(spawn, yaw);
                BoatHome.AfterLocalPlayerWentHome();
                yield return Wait(2f);
                Vector3 dd = bc.transform.position - bc.MooringPosition;
                dd.y = 0f;
                Check(away && bc.AtMooring && dd.magnitude < 2.5f, "'Zur Hütte': the boat came back to the dock (" + F(dd.magnitude) + " m from the mooring)");
            }
            else Info("no boat – skipped");

            // Hard (Advanced) is locked until a finale on Normal was finished on this PC
            var d = Settings.Data;
            bool prev = d.normalCompleted;
            d.normalCompleted = false;
            Check(!DifficultySettings.IsUnlocked(Difficulty.Advanced) && DifficultySettings.LockedHint(Difficulty.Advanced) != null,
                  "Hard locked: '" + DifficultySettings.LockedHint(Difficulty.Advanced) + "'");
            Check(DifficultySettings.IsUnlocked(Difficulty.OpenWater) && DifficultySettings.IsUnlocked(Difficulty.Snorkeler), "Relaxed and Normal always open");
            d.normalCompleted = true;
            Check(DifficultySettings.IsUnlocked(Difficulty.Advanced), "after the Normal finale Hard unlocks");
            d.normalCompleted = prev;
        }

        // ================================================================== 43 secrets, quest line, notifications, music
        IEnumerator StepR2Hud()
        {
            // L.6 secrets: 13, none right next to a trail
            int secrets = 0, nearTrail = 0;
            string near = "";
            foreach (var p in Object.FindObjectsByType<PropPickup>(FindObjectsSortMode.None))
            {
                if (p == null || p.kind != CollectibleKind.Secret) continue;
                secrets++;
                Vector3 sp = p.transform.position;
                for (int i = 0; i < PropInfo.PathPoints.Count; i++)
                {
                    if (FlatDist(PropInfo.PathPoints[i], sp) < 2.5f && Mathf.Abs(PropInfo.PathPoints[i].y - sp.y) < 2f)
                    {
                        nearTrail++;
                        if (near.Length < 120) near += p.id + " ";
                        break;
                    }
                }
            }
            Check(secrets == Collectibles.Total(CollectibleKind.Secret), secrets + " secrets placed (of " + Collectibles.Total(CollectibleKind.Secret) + ")");
            Check(nearTrail == 0, "no secret lies right on a trail (" + nearTrail + (near.Length > 0 ? ": " + near : "") + ")");

            // L.10 quest line: one entry, top centre (drawn by the UI)
            // pin a real side quest like the Tasks app's „Verfolgen“ does (the Other slot is shared with the providers)
            // step 39b may have finished most quests: pick one that is still open (running or not started yet)
            string pinId = null;
            for (int i = 0; i < SideQuestDatabase.All.Length && pinId == null; i++)
            {
                var sd = SideQuestDatabase.All[i];
                if (sd == null || sd.id == Quests.ToniChain) continue;
                var sq = Quests.Get(sd.id);
                if (sq != null ? !sq.done : Quests.CanStart(sd.id, out _)) pinId = sd.id;
            }
            bool pinStarted = false;
            if (pinId != null && Quests.Get(pinId) == null) pinStarted = Quests.Start(pinId);
            if (pinId != null) QuestTracker.Track(pinId);
            yield return Wait(1.3f);   // the side-quest provider refreshes its entry at least once a second
            Check(pinId != null && QuestTracker.ActiveId == pinId && QuestTracker.Line != null,
                  "quest line shows the pinned task " + pinId + ": '" + QuestTracker.Line + "' " + QuestTracker.Progress + " (active " + QuestTracker.ActiveId + ")");
            if (pc != null)
            {
                pc.enabled = true;
                Vector3 dock = SnapDown(DockEnd() + new Vector3(-12f, 0.4f, 0f));
                yield return Stand(dock, 90f);
                SetTime(0.5f);
                GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("FinTube zahlt", "FinTube pays"), "+86 $");
                GameEvents.RaiseNotify(NotifyKind.Sealog, Loc.Tr("Neu im Sealog", "New in the Sealog"), Loc.Tr("Papageifisch", "Parrotfish"));
                yield return Wait(0.6f);
                yield return Shot("43_quest_line_notifications");
            }
            QuestTracker.Track(null);
            if (pinStarted) Quests.Drop(pinId);
            Metric("notify.long", longNotifies + " of " + notifies + " notifications had more than 40 characters" + (longNotifyLines.Count > 0 ? ": " + string.Join(" | ", longNotifyLines) : ""));
            Check(longNotifies <= 3, "notifications stay short: " + longNotifies + " of " + notifies + " longer than 40 characters");

            // K.6 music
            Check(FXMusic.Playing || FXMusic.Current != FXMusicTrack.None, "background music: " + FXMusic.Current + " (" + F(FXMusic.Bpm, "0") + " BPM, playing " + FXMusic.Playing + ")");
        }
    }
}
