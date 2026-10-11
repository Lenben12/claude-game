using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// The village Altriff (L.8 – an invented, long-established island community: ordinary people with their own crafts,
    /// customs and a sense of humour; no stereotypes), the Lantern Evening, the First Paddle canoe race (M3) and Toni (K.8).
    public sealed partial class NpcDirector
    {
        struct Sched
        {
            public string id, work;
            public NpcMotion motion;
            public string evening;
            public NpcMotion eveningMotion;
            public bool wander;
        }

        static readonly Sched[] VillageSchedule =
        {
            new Sched { id = NpcDatabase.Pearl, work = "v_pearl", motion = NpcMotion.Idle, evening = "v_meeting", eveningMotion = NpcMotion.Cards },
            new Sched { id = NpcDatabase.Mira, work = "v_mira", motion = NpcMotion.Carve, evening = "v_meeting", eveningMotion = NpcMotion.Idle },
            new Sched { id = NpcDatabase.Tomas, work = "v_tomas", motion = NpcMotion.Mend, evening = "v_jetty", eveningMotion = NpcMotion.Idle },
            new Sched { id = NpcDatabase.Ada, work = "v_ada", motion = NpcMotion.Cook, evening = "v_ada", eveningMotion = NpcMotion.Cook },
            new Sched { id = NpcDatabase.Juno, work = "v_stage", motion = NpcMotion.Marimba, evening = "v_stage", eveningMotion = NpcMotion.Marimba },
            new Sched { id = NpcDatabase.Lio, work = "v_lio", motion = NpcMotion.Idle, evening = "v_meeting", eveningMotion = NpcMotion.Idle, wander = true },
            new Sched { id = NpcDatabase.Weaver1, work = "v_weaver1", motion = NpcMotion.Weave, evening = "v_meeting", eveningMotion = NpcMotion.Idle },
            new Sched { id = NpcDatabase.Weaver2, work = "v_weaver2", motion = NpcMotion.Weave, evening = "v_meeting", eveningMotion = NpcMotion.Idle },
            new Sched { id = NpcDatabase.Fisher1, work = "v_fisher1", motion = NpcMotion.Mend, evening = "v_jetty", eveningMotion = NpcMotion.Idle },
            new Sched { id = NpcDatabase.Fisher2, work = "v_fisher2", motion = NpcMotion.Mend, evening = "v_jetty", eveningMotion = NpcMotion.Idle },
            new Sched { id = NpcDatabase.Teen, work = "v_teen", motion = NpcMotion.Strum, evening = "v_stage", eveningMotion = NpcMotion.Strum },
            // Grandpa carves driftwood animals next to his hammock (L.8 craft); the hammock nap is only the 13–15 h gag
            new Sched { id = NpcDatabase.Grandpa, work = "v_grandpa_carve", motion = NpcMotion.Carve, evening = "v_meeting", eveningMotion = NpcMotion.Idle },
        };

        float lioWanderT;
        bool raceRunning;
        float raceT;
        int raceDay = -1;
        bool cardsRunning;
        float cardsT;

        void ResetVillageSession()
        {
            lioWanderT = 0f;
            raceRunning = false;
            raceT = 0f;
            raceDay = -1;
            cardsRunning = false;
            ClearLanterns();
        }

        void HostVillageStart() { }

        // ================================================================ HOST: schedules
        void ThinkVillage()
        {
            var s = S;
            if (s == null) return;
            float h = Hour;
            bool lantern = StoryRules.IsLanternEvening(s.day, s.timeOfDay);
            bool night = !Between(h, 6.5f, 22f);
            if (lantern && AnyoneNear(WorldLayout.Village, 70f) && !StoryFlags.Has(NpcKeys.LanternSeen)) StoryFlags.Set(NpcKeys.LanternSeen);
            if (!StoryFlags.Has(StoryKeys.VillageVisited) && AnyoneNear(WorldLayout.Village, 30f))
            {
                StoryFlags.Set(StoryKeys.VillageVisited);
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Altriff", "Oldreef"), Loc.Tr("Gute Flut!", "Good tide!"));
            }

            for (int i = 0; i < VillageSchedule.Length; i++)
            {
                var sc = VillageSchedule[i];
                var a = Actor(sc.id);
                if (a == null) continue;
                if (sc.id == NpcDatabase.Pearl && cardsRunning) continue;
                if (raceRunning && (sc.id == NpcDatabase.Mira || sc.id == NpcDatabase.Tomas)) { a.SetPresent(false); continue; }
                if (night) { a.SetPresent(false); continue; }
                bool evening = h >= 18f || lantern;
                string spotId = evening ? sc.evening : sc.work;
                bool nap = sc.id == NpcDatabase.Grandpa && !evening && Between(h, 13f, 15f);
                if (nap) spotId = "v_grandpa";
                Vector3 spot = QuestSpots.Get(spotId) + Offset(i, evening);
                if (lantern && sc.id != NpcDatabase.Juno && sc.id != NpcDatabase.Teen && sc.id != NpcDatabase.Ada)
                    spot = QuestSpots.Get("v_jetty") + Offset(i, true);
                if (!a.Present)
                {
                    a.SetPresent(true);
                    a.Place(spot, 180f + i * 37f);
                }
                if (IsTalking(a) || a.HasOffer) { a.Stop(); a.Motion = NpcMotion.Idle; a.FacePoint = NearestDiverPos(a.Position); continue; }
                if (sc.wander && !evening)
                {
                    lioWanderT -= 0.1f;
                    if (lioWanderT <= 0f && !a.Moving)
                    {
                        lioWanderT = 4f + NpcUtil.Hash01((int)(Time.time), 17) * 5f;
                        float ang = NpcUtil.Hash01((int)(Time.time * 3f), 19) * Mathf.PI * 2f;
                        a.GoTo(spot + new Vector3(Mathf.Cos(ang) * 6f, 0f, Mathf.Sin(ang) * 4f), 2.6f);
                    }
                    if (!a.Moving) { a.Motion = NpcMotion.Idle; a.FacePoint = NearestDiverPos(a.Position); }
                    continue;
                }
                if (NpcUtil.FlatDist(a.Position, spot) > 1f)
                {
                    if (!a.Moving || NpcUtil.FlatDist(a.Destination, spot) > 0.5f) a.GoTo(spot, 1.5f);
                    continue;
                }
                NpcMotion m = nap ? NpcMotion.Lie : evening ? sc.eveningMotion : sc.motion;
                if (lantern && spotId != sc.evening) m = NpcMotion.Cheer;
                if (m == NpcMotion.Cards && !cardsRunning) m = NpcMotion.Idle;
                // look up from the work when someone comes close (greeting „Gute Flut!“ – a short wave)
                if (AnyoneNear(a.Position, 4f) && m != NpcMotion.Lie)
                {
                    if (a.Motion != NpcMotion.Idle && m != NpcMotion.Cook) a.PlayGesture(NpcGesture.Greet);
                    m = sc.id == NpcDatabase.Ada ? m : NpcMotion.Idle;
                    a.FacePoint = NearestDiverPos(a.Position);
                }
                a.Motion = m;
                a.Flags = sc.id == NpcDatabase.Ada ? (byte)(a.Flags | NpcActor.FlagKiosk) : a.Flags;
            }

            // Pearl's first story (L.8) when someone walks up to her
            var pearl = Actor(NpcDatabase.Pearl);
            if (pearl != null && pearl.Present && !StoryFlags.Has(NpcKeys.PearlMet) && !IsTalking(pearl) && AnyoneNear(pearl.Position, 6f))
            {
                pearl.PlayGesture(NpcGesture.Greet);
                Talk(pearl, "pearl.first", () => { StoryFlags.Set(NpcKeys.PearlMet); StoryFlags.Set(StoryKeys.NightOfTheEye); });
            }
            ThinkRace();
            ThinkCards();
            ThinkToni();
        }

        static Vector3 Offset(int i, bool evening)
        {
            if (!evening) return Vector3.zero;
            float a = i * 0.9f;
            return new Vector3(Mathf.Cos(a) * 2.2f, 0f, Mathf.Sin(a) * 1.6f);
        }

        // ================================================================ M3 First Paddle (canoe race)
        void ThinkRace()
        {
            var q = Quests.Get("M3");
            var c1 = vehicles.Length > VCanoe2 ? vehicles[VCanoe1] : null;
            var c2 = vehicles.Length > VCanoe2 ? vehicles[VCanoe2] : null;
            if (c1 == null) return;
            bool want = q != null && !q.done && q.step >= 1 && Hour >= 10f && Hour < 12f && raceDay != NpcUtil.Day;
            if (!raceRunning && want)
            {
                raceRunning = true;
                raceT = 0f;
                Vector3 start = QuestSpots.Get("v_canoe_start");
                c1.Place(start, 90f);
                c2.Place(start + new Vector3(0f, 0f, -4f), 90f);
                c1.Present = true;
                c2.Present = true;
                GameEvents.RaiseNotify(NotifyKind.Event, Loc.Tr("Das erste Paddeln!", "First Paddle!"), Loc.Tr("Dorfsteg", "Village jetty"));
            }
            if (!raceRunning) return;
            raceT += 0.1f;
            Vector3 c = QuestSpots.Get("v_lanterns");
            // a loop round the stilt lagoon: 6 legs of 22 s
            int leg = (int)(raceT / 22f);
            if (raceT - leg * 22f < 0.11f && leg < 6)
            {
                float a1 = leg / 6f * Mathf.PI * 2f + 0.5f, a2 = a1 + 0.05f;
                Vector3 t1 = c + new Vector3(Mathf.Cos(a1 + 1.05f) * 26f, 0f, Mathf.Sin(a1 + 1.05f) * 14f);
                Vector3 t2 = c + new Vector3(Mathf.Cos(a2 + 1.0f) * 22f, 0f, Mathf.Sin(a2 + 1.0f) * 11f);
                t1 = QuestSpots.Surface(t1, 0.6f, 14f);
                t2 = QuestSpots.Surface(t2, 0.6f, 14f);
                c1.SailTo(t1, Mathf.Atan2(t1.x - c1.Pos.x, t1.z - c1.Pos.z) * Mathf.Rad2Deg, 21.5f);
                c2.SailTo(t2, Mathf.Atan2(t2.x - c2.Pos.x, t2.z - c2.Pos.z) * Mathf.Rad2Deg, 22.5f);
            }
            if (raceT > 6 * 22f + 4f)
            {
                raceRunning = false;
                raceDay = NpcUtil.Day;
                c1.Present = false;
                c2.Present = false;
            }
        }

        public bool RaceRunning => raceRunning || (vehicles.Length > VCanoe1 && vehicles[VCanoe1].Present);

        // ================================================================ P2 card night
        void ThinkCards()
        {
            if (!cardsRunning) return;
            cardsT += 0.1f;
            var pearl = Actor(NpcDatabase.Pearl);
            var horst = Actor(NpcDatabase.Horst);
            if (pearl != null)
            {
                pearl.SetPresent(true);
                if (NpcUtil.FlatDist(pearl.Position, QuestSpots.Get("v_pearl")) > 1.2f && !pearl.Moving) pearl.Place(QuestSpots.Get("v_pearl"), 90f);
                pearl.Motion = NpcMotion.Cards;
                if (horst != null) pearl.FacePoint = horst.Position;
            }
            if (horst != null)
            {
                horst.Motion = NpcMotion.Cards;
                if (pearl != null) horst.FacePoint = pearl.Position;
            }
            if (cardsT > 35f)
            {
                cardsRunning = false;
                if (horst != null) { horst.Motion = NpcMotion.Idle; horst.PlayGesture(NpcGesture.Facepalm); }
                if (pearl != null) pearl.PlayGesture(NpcGesture.Laugh);
                escortDone = true;
            }
        }

        // ================================================================ K.8 Toni (HOST)
        void ThinkToni()
        {
            var toni = Actor(NpcDatabase.Toni);
            if (toni == null) return;
            if (!StoryFlags.Has(StoryKeys.ToniArrived)) { toni.SetPresent(false); return; }
            var q = Quests.Get(Quests.ToniChain);
            bool done = StoryFlags.Has(StoryKeys.FogMap);
            float h = Hour;
            if (!done)
            {
                Vector3 beach = QuestSpots.Get(q == null ? "toni_lie" : "driftwood");
                if (!toni.Present) { toni.SetPresent(true); toni.Place(beach, 30f); }
                if (IsTalking(toni) || toni.HasOffer) { toni.FacePoint = NearestDiverPos(toni.Position); if (toni.Motion == NpcMotion.Lie && q != null) toni.Motion = NpcMotion.Idle; return; }
                if (q == null) { toni.Motion = NpcMotion.Lie; return; }
                if (NpcUtil.FlatDist(toni.Position, beach) > 1f) { if (!toni.Moving) toni.GoTo(beach, 1.2f); return; }
                toni.Motion = q.step == 1 ? NpcMotion.Doze : NpcMotion.Idle;
                if (AnyoneNear(toni.Position, 5f)) { toni.Motion = NpcMotion.Idle; toni.FacePoint = NearestDiverPos(toni.Position); }
                return;
            }
            // afterwards: the village guesthouse, 10–16 his camp on the Fog Isle – never relocated mid-conversation
            if (toni.Present && (IsTalking(toni) || toni.HasOffer)) { toni.FacePoint = NearestDiverPos(toni.Position); return; }
            if (!Between(h, 7f, 22f)) { toni.SetPresent(false); return; }
            string spotId = Between(h, 10f, 16f) ? "fog_camp" : "v_guesthouse";
            Vector3 spot = QuestSpots.Get(spotId);
            if (!toni.Present || NpcUtil.FlatDist(toni.Position, spot) > 40f) { toni.SetPresent(true); toni.Place(spot, 180f); }
            if (NpcUtil.FlatDist(toni.Position, spot) > 1f) { if (!toni.Moving) toni.GoTo(spot, 1.2f); return; }
            toni.Motion = spotId == "fog_camp" ? NpcMotion.Carve : NpcMotion.Idle;
            if (AnyoneNear(toni.Position, 4f)) { toni.Motion = NpcMotion.Idle; toni.FacePoint = NearestDiverPos(toni.Position); }
        }

        /// Every machine: Toni washed up → Horst on the radio (K.8).
        void OnStoryFlag(string flag)
        {
            if (!InGame) return;
            if (flag == StoryKeys.ToniArrived)
            {
                Radio(NpcDatabase.Horst, "horst.toni", "horst.toni");
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Schiffbrüchiger!", "Castaway!"), Loc.Tr("Treibholzstrand", "Driftwood Beach"));
            }
            else if (flag == StoryKeys.FogMap)
                GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Neue Insel auf der Seekarte!", "New island on the sea chart!"), Loc.Tr("Nebelinsel", "Fog Isle"));
            trackerDirty = true;
            objectsDirty = true;
        }

        // ================================================================ every machine: lanterns on the water (L.8)
        readonly List<Transform> lanterns = new List<Transform>(32);
        readonly List<Vector3> lanternBase = new List<Vector3>(32);
        Transform lanternRoot;
        bool lanternsOn;

        void TickLanterns(float dt)
        {
            var s = S;
            bool on = s != null && StoryRules.IsLanternEvening(s.day, s.timeOfDay);
            if (on != lanternsOn)
            {
                lanternsOn = on;
                if (on) SpawnLanterns(); else ClearLanterns();
            }
            if (!on) return;
            float t = Time.time;
            for (int i = 0; i < lanterns.Count; i++)
            {
                var l = lanterns[i];
                if (l == null) continue;
                Vector3 b = lanternBase[i];
                b += new Vector3(Mathf.Sin(t * 0.05f + i) * 4f, 0f, Mathf.Cos(t * 0.04f + i * 1.7f) * 3f - (t % 600f) * 0.004f);
                b.y = Waves.HeightAt(b.x, b.z) + 0.02f;
                l.SetPositionAndRotation(b, Quaternion.Euler(Mathf.Sin(t + i) * 4f, i * 33f, Mathf.Cos(t * 0.8f + i) * 4f));
            }
        }

        void SpawnLanterns()
        {
            ClearLanterns();
            if (lanternRoot == null)
            {
                lanternRoot = new GameObject("Lanterns").transform;
                lanternRoot.SetParent(npcRoot, false);
            }
            Vector3 c = QuestSpots.Get("v_lanterns");
            for (int i = 0; i < 24; i++)
            {
                float a = i * 2.39996f, r = 3f + Mathf.Sqrt(i) * 4.2f;
                Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.6f);
                var go = NpcVehicleBuilder.Lantern(i);
                go.transform.SetParent(lanternRoot, false);
                go.transform.position = p;
                lanterns.Add(go.transform);
                lanternBase.Add(p);
            }
        }

        /// A lantern released from the jetty (local cosmetic) floats out to the others.
        void AddReleasedLantern()
        {
            if (!lanternsOn) return;
            Vector3 p = QuestSpots.Get("v_jetty_end");
            p = QuestSpots.Surface(p, 0.4f, 12f);
            var go = NpcVehicleBuilder.Lantern(lanterns.Count);
            go.transform.SetParent(lanternRoot, false);
            go.transform.position = p;
            lanterns.Add(go.transform);
            lanternBase.Add(p);
        }

        void ClearLanterns()
        {
            for (int i = 0; i < lanterns.Count; i++) if (lanterns[i] != null) Destroy(lanterns[i].gameObject);
            lanterns.Clear();
            lanternBase.Clear();
            lanternsOn = false;
        }

        /// Lantern positions for the P3 photo check (≥ 5 in frame).
        public int LanternsInView(Camera c)
        {
            if (c == null) return 0;
            int n = 0;
            for (int i = 0; i < lanterns.Count; i++)
            {
                var l = lanterns[i];
                if (l == null) continue;
                Vector3 v = c.WorldToViewportPoint(l.position);
                if (v.z > 0.3f && v.z < 120f && v.x > 0.02f && v.x < 0.98f && v.y > 0.02f && v.y < 0.98f) n++;
            }
            return n;
        }

        // ================================================================ set dressing of the npc module (props may replace it)
        void BuildDressing()
        {
            var root = new GameObject("NpcDressing").transform;
            root.SetParent(npcRoot, false);
            dressingRoot = root;
        }

        Transform dressingRoot;
        bool dressingPlaced;

        /// Places the small personal props once the world exists (Horst's chair + tent, the card table, Juno's marimba,
        /// grandpa's hammock, Toni's wrecked raft) unless props registered their own spot.
        void PlaceDressing()
        {
            if (dressingPlaced || dressingRoot == null || !WorldInfo.Built) return;
            dressingPlaced = true;
            Material wood = NpcMesh.World(LowPoly.Hex("#8E6C50")), dark = NpcMesh.World(LowPoly.Hex("#5E4436")), cloth = NpcMesh.World(LowPoly.Hex("#3FA9A0"));
            if (!QuestSpots.IsRegistered("horst_bench"))
            {
                var chair = NpcVehicleBuilder.HorstChair();
                chair.transform.SetParent(dressingRoot, false);
                Vector3 p = QuestSpots.Get("horst_bench");
                chair.transform.SetPositionAndRotation(p + new Vector3(0f, 0f, -0.05f), Quaternion.Euler(0f, 90f, 0f));
            }
            if (!QuestSpots.IsRegistered("horst_tent"))
            {
                var tent = NpcVehicleBuilder.HorstTent();
                tent.transform.SetParent(dressingRoot, false);
                Vector3 p = QuestSpots.Get("horst_tent") + new Vector3(0f, 0f, -2.2f);
                p.y = WorldInfo.GroundHeight(p);
                tent.transform.SetPositionAndRotation(p, Quaternion.Euler(0f, 15f, 0f));
            }
            if (!QuestSpots.IsRegistered("v_cards"))
            {
                Vector3 p = (QuestSpots.Get("v_pearl") + QuestSpots.Get("v_cards_h")) * 0.5f;
                var t = new GameObject("CardTable").transform;
                t.SetParent(dressingRoot, false);
                t.position = p;
                NpcMesh.Part(t, "Top", NpcMesh.Flat(NpcMesh.Cylinder(0.55f, 0.06f, 10), "card_top"), wood, new Vector3(0f, 0.62f, 0f));
                NpcMesh.Part(t, "Leg", NpcMesh.Flat(NpcMesh.Cylinder(0.08f, 0.62f, 6), "card_leg"), dark, Vector3.zero);
                NpcMesh.Part(t, "Cards", NpcMesh.Flat(NpcMesh.RoundBox(0.12f, 0.03f, 0.18f, 0.2f, 2), "card_deck"), NpcMesh.World(LowPoly.Hex("#F4EFE6")), new Vector3(0.1f, 0.7f, 0f));
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 sp = s < 0 ? QuestSpots.Get("v_pearl") : QuestSpots.Get("v_cards_h");
                    NpcMesh.Part(t, "Stool", NpcMesh.Flat(NpcMesh.Cylinder(0.24f, 0.42f, 8), "stool"), wood, sp - p + new Vector3(0f, -0.02f, 0f));
                }
            }
            if (!QuestSpots.IsRegistered("v_marimba"))
            {
                Vector3 p = QuestSpots.Get("v_stage");
                var t = new GameObject("Marimba").transform;
                t.SetParent(dressingRoot, false);
                t.SetPositionAndRotation(p + new Vector3(0f, 0f, 0.75f), Quaternion.identity);
                NpcMesh.Part(t, "Frame", NpcMesh.Flat(NpcMesh.RoundBox(1.3f, 0.08f, 0.45f, 0.2f, 2), "mar_frame"), dark, new Vector3(0f, 0.78f, 0f));
                string[] keys = { "#C9B08E", "#B5A07A", "#C9B08E", "#B5A07A", "#C9B08E", "#B5A07A", "#C9B08E" };
                for (int i = 0; i < keys.Length; i++)
                {
                    if (i == 5) continue;   // the missing A key (Juno's gag)
                    NpcMesh.Part(t, "Key", NpcMesh.Flat(NpcMesh.RoundBox(0.13f, 0.04f, 0.4f - i * 0.03f, 0.3f, 2), "mar_key" + i), NpcMesh.World(LowPoly.Hex(keys[i])), new Vector3(-0.54f + i * 0.18f, 0.84f, 0f));
                }
                for (int s = -1; s <= 1; s += 2)
                    NpcMesh.Part(t, "Leg", NpcMesh.Flat(NpcMesh.Cylinder(0.04f, 0.78f, 6), "mar_leg"), dark, new Vector3(s * 0.55f, 0f, 0f));
            }
            if (!QuestSpots.IsRegistered("v_hammock"))
            {
                Vector3 p = QuestSpots.Get("v_grandpa");
                var t = new GameObject("Hammock").transform;
                t.SetParent(dressingRoot, false);
                t.SetPositionAndRotation(p, Quaternion.Euler(0f, 180f, 0f));
                for (int s = -1; s <= 1; s += 2)
                    NpcMesh.Part(t, "Post", NpcMesh.Flat(NpcMesh.Cylinder(0.07f, 1.5f, 6), "ham_post"), dark, new Vector3(0f, -0.2f, s * 1.3f));
                NpcMesh.Part(t, "Cloth", NpcMesh.Flat(NpcMesh.RoundBox(0.75f, 0.1f, 2.4f, 0.6f, 3), "ham_cloth"), cloth, new Vector3(0f, 0.18f, 0f));
            }
            if (!QuestSpots.IsRegistered("toni_raft"))
            {
                Vector3 p = QuestSpots.Get("toni_raft");
                var t = new GameObject("ToniRaft").transform;
                t.SetParent(dressingRoot, false);
                p.y = Mathf.Max(WorldInfo.GroundHeight(p) + 0.1f, Waves.SeaLevel - 0.15f);
                t.SetPositionAndRotation(p, Quaternion.Euler(4f, 35f, -6f));
                for (int i = 0; i < 5; i++)
                    NpcMesh.Part(t, "Log", NpcMesh.Flat(NpcMesh.Capsule(0.13f, 2.4f - (i % 2) * 0.5f, 7), "raft_log" + (i % 2)), i % 2 == 0 ? wood : dark,
                        new Vector3(-0.56f + i * 0.28f, 0f, -1.1f), Vector3.one, new Vector3(90f, 0f, i * 7f));
                NpcMesh.Part(t, "Mast", NpcMesh.Flat(NpcMesh.Cylinder(0.05f, 1.6f, 6), "raft_mast"), dark, new Vector3(0.1f, 0f, 0.3f), Vector3.one, new Vector3(0f, 0f, 28f));
                NpcMesh.Part(t, "Sail", NpcMesh.Flat(NpcMesh.RoundBox(0.04f, 0.9f, 0.7f, 0.2f, 2), "raft_sail"), NpcMesh.World(LowPoly.Hex("#EDE6DA")), new Vector3(-0.4f, 1.1f, 0.3f), Vector3.one, new Vector3(0f, 0f, 28f));
                toniRaft = t.gameObject;
            }
        }

        GameObject toniRaft;

        void TickDressing()
        {
            if (!dressingPlaced) PlaceDressing();
            if (toniRaft != null)
            {
                bool show = StoryFlags.Has(StoryKeys.ToniArrived);
                if (toniRaft.activeSelf != show) toniRaft.SetActive(show);
            }
        }
    }
}
