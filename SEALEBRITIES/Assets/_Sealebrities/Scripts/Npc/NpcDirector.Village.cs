using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Island life besides the hermits (round 3: the village Altriff is gone – GDD §19 „Die Dorf-Häuser entfernen“):
    /// Toni (K.8), the hermits' lantern ritual on the water at the old jetty of the south beach (every 7th evening),
    /// the hermits' canoe race (M3) and the small personal props of the npc module.
    public sealed partial class NpcDirector
    {
        bool raceRunning;
        float raceT;
        int raceDay = -1;

        void ResetVillageSession()
        {
            raceRunning = false;
            raceT = 0f;
            raceDay = -1;
            ClearLanterns();
        }

        void HostVillageStart() { }

        // ================================================================ HOST: island life
        void ThinkVillage()
        {
            var s = S;
            if (s == null) return;
            // round 2 flag kept for old saves: the lantern ritual seen (P3 used to need it)
            if (StoryRules.IsLanternEvening(s.day, s.timeOfDay) && AnyoneNear(QuestSpots.Get("v_jetty"), 70f) && !StoryFlags.Has(NpcKeys.LanternSeen)) StoryFlags.Set(NpcKeys.LanternSeen);
            ThinkRace();
            ThinkToni();
        }

        // ================================================================ M3 the hermits' canoe race
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
                GameEvents.RaiseNotify(NotifyKind.Event, Loc.Tr("Das Kanu-Rennen!", "The canoe race!"), Loc.Tr("Alter Steg", "Old jetty"));
            }
            if (!raceRunning) return;
            raceT += 0.1f;
            Vector3 c = QuestSpots.Get("v_lanterns");
            // a loop off the south beach: 6 legs of 22 s
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
            // afterwards: 10–16 his camp on the Fog Isle, else Horst's corner on the beach (Horst lent him a hammock)
            if (toni.Present && (IsTalking(toni) || toni.HasOffer)) { toni.FacePoint = NearestDiverPos(toni.Position); return; }
            if (!Between(h, 7f, 22f)) { toni.SetPresent(false); return; }
            string spotId = Between(h, 10f, 16f) ? "fog_camp" : "toni_evening";
            Vector3 spot = QuestSpots.Get(spotId);
            if (!toni.Present || NpcUtil.FlatDist(toni.Position, spot) > 40f) { toni.SetPresent(true); toni.Place(spot, 180f); }
            if (NpcUtil.FlatDist(toni.Position, spot) > 1f) { if (!toni.Moving) toni.GoTo(spot, 1.2f); return; }
            toni.Motion = spotId == "fog_camp" ? NpcMotion.Carve : NpcMotion.Sit;
            if (AnyoneNear(toni.Position, 4f)) { toni.Motion = NpcMotion.Idle; toni.FacePoint = NearestDiverPos(toni.Position); }
            else toni.FacePoint = WorldLayout.DockEnd + new Vector3(30f, 0f, -20f);
        }

        /// Every machine: Toni washed up → Horst on the radio (K.8); the camp found → Horst explains the signs.
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
            OnHermitFlag(flag);
            trackerDirty = true;
            objectsDirty = true;
        }

        // ================================================================ every machine: lanterns on the water (the hermits' ritual)
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

        /// Places Toni's wrecked raft once the world exists (Horst's hammock + bench and the hermit camp are props of
        /// the world module since round 3).
        void PlaceDressing()
        {
            if (dressingPlaced || dressingRoot == null || !WorldInfo.Built) return;
            dressingPlaced = true;
            Material wood = NpcMesh.World(LowPoly.Hex("#8E6C50")), dark = NpcMesh.World(LowPoly.Hex("#5E4436"));
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
