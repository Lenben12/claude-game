using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 – world: the island layout and its anchors, the village houses gone (south beach jetty stays), the
    /// hidden hermit camp (flat hollow, no trail / signpost, NPC anchors, discovery flag), the volcano island terrain +
    /// zone id, the swim raft out in the sea, the signpost audit, the map pieces / campfires / daily finds and the
    /// treasure X. Runs inside RunRound3Steps (-r3only). Screenshots 60_*.
    public sealed partial class AutoPlaytest
    {
        IEnumerator StepR3World()
        {
            // ---- anchors + terrain
            Vector3 camp = WorldLayout.HermitCamp;
            float cg = WorldInfo.GroundHeight(camp);
            float mn = 999f, mx = -999f;
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI / 8f, r = 2f + (i % 4) * 2f;
                float h = WorldInfo.GroundHeight(camp + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                mn = Mathf.Min(mn, h); mx = Mathf.Max(mx, h);
            }
            Check(Mathf.Abs(cg - WorldFeatures.CampFloorY) < 1.5f && mx - mn < 1.3f, "hermit camp floor flat at " + F(cg) + " m (spread " + F(mx - mn, "0.00") + " m within 8 m)");
            float rim = 0f;
            for (int i = 0; i < 12; i++)
            {
                float a = i * Mathf.PI / 6f;
                rim += WorldInfo.GroundHeight(camp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (WorldLayout.HermitCampRadius + 7f));
            }
            rim /= 12f;
            Check(rim > cg + 1.5f, "the camp lies in a hollow: rim " + F(rim) + " m vs floor " + F(cg) + " m");
            float trail = PropPaths.DistanceToPath(camp.x, camp.z);
            Check(trail > 18f, "no marked trail leads to the camp (nearest trail " + F(trail, "0") + " m)");
            // the waterfall-pool post stands ~40 m from the camp (the wild trail starts at the grotto): allowed, as long as
            // nothing stands right at the camp and no arrow points at it
            int nearSigns = 0, pointing = 0;
            Vector2 camp2 = new Vector2(camp.x, camp.z);
            foreach (var s in PropPaths.SignAudit)
            {
                Vector2 at2 = new Vector2(s.at.x, s.at.z);
                float dist = Vector2.Distance(at2, camp2);
                if (dist < 30f) nearSigns++;
                if (dist < 60f && s.toward != null)
                    foreach (var tw in s.toward)
                        if (Vector2.Angle(tw - at2, camp2 - at2) < 15f) pointing++;
            }
            Check(nearSigns == 0 && pointing == 0, "no signpost at the hidden camp (" + nearSigns + " within 30 m, " + pointing + " arrows pointing at it)");

            // ---- the camp props + NPC anchors
            int spots = 0;
            string miss = "";
            foreach (var id in PropCamp.SpotIds)
            {
                if (QuestSpots.IsRegistered(id)) spots++;
                else miss += id + " ";
            }
            Check(spots == PropCamp.SpotIds.Length, spots + "/" + PropCamp.SpotIds.Length + " hermit NPC anchors registered" + (miss.Length > 0 ? " (missing " + miss + ")" : ""));
            Check(GameObject.Find("HermitCamp") != null && GameObject.Find("HammockBed_camp_hammock") != null, "camp built with a registered hammock");
            Check(PropInfo.Spot("goat_pen", out Vector3 pen) && Vector3.Distance(new Vector3(pen.x, 0f, pen.z), new Vector3(camp.x, 0f, camp.z)) < 16f, "the goats live in the camp now (" + V(pen) + ")");
            Check(PropInfo.Spot("village_loft", out Vector3 loft) && Vector3.Distance(loft, camp) < 20f, "Gus' IOUs secret moved into the hermits' chest of sea finds");
            Check(GameObject.Find("VillageMesh") == null && GameObject.Find("SouthBeachMesh") != null && PropInfo.Spot("village_jetty", out _), "village houses removed, the south beach jetty stays");

            // ---- discovery flag: stand in the camp
            if (pc != null)
            {
                pc.enabled = true;
                Hands.StowAll();
                Vector3 feet = SnapDown(camp + new Vector3(3f, 3f, -2f));
                yield return Stand(feet, YawTo(feet, camp));
                yield return Wait(1.3f);
                Check(StoryFlags.Has(StoryKeys.HermitCampFound), "walking into the camp sets " + StoryKeys.HermitCampFound + " (the map shows it now)");
                pc.enabled = false;
                SetTime(0.42f);
                // WorldLayout.HermitCamp lies at y 0 – the shot is posed above the camp floor (it was inside the hill)
                Vector3 campFloor = new Vector3(camp.x, cg, camp.z);
                Pose(campFloor + WorldFeatures.CampEntranceDir * 9f + new Vector3(0f, 6.5f, 0f) + Vector3.Cross(Vector3.up, WorldFeatures.CampEntranceDir) * 5f, campFloor + Vector3.up * 0.5f);
                yield return Wait(2f);
                yield return Shot("60_hermit_camp");
                // the game trail from the grotto clearing (a faint strip, cairns)
                Vector3 ts = WorldFeatures.CampTrailStart;
                Pose(ts + Vector3.up * 1.7f - WorldFeatures.CampEntranceDir * -2f, WorldFeatures.CampTrailGap + Vector3.up * 1.2f);
                yield return Wait(1.5f);
                yield return Shot("60_hermit_trail");
                pc.enabled = true;
            }

            // ---- volcano island + zone
            Vector3 vol = WorldLayout.VolcanoIsland;
            float crater = WorldInfo.GroundHeight(vol);
            float rimH = WorldInfo.GroundHeight(vol + new Vector3(WorldFeatures.CraterRimRadius, 0f, 0f));
            Vector3 beach = WorldFeatures.VolcanoBeach;
            float bh = WorldInfo.GroundHeight(beach);
            Check(Mathf.Abs(crater - WorldFeatures.CraterFloorY) < 2.5f && rimH > WorldLayout.VolcanoPeak - 4f && bh > 0.2f && bh < 3f,
                  "volcano: crater floor " + F(crater) + " m, rim " + F(rimH) + " m, black beach " + F(bh) + " m");
            float east = WorldInfo.SeafloorDepth(vol + new Vector3(WorldLayout.VolcanoShoreRadius + 40f, 0f, 0f));
            float shelf = WorldInfo.SeafloorDepth(beach + WorldFeatures.VolcanoBeachDir * 30f);
            Check(east > 30f && shelf > 2f && shelf < 12f, "volcano waters: shallow black-sand shelf " + F(shelf) + " m off the beach, steep east drop " + F(east) + " m");
            Check(Zones.Index(Zones.Volcano) == 12 && Zones.Name(Zones.Volcano).Length > 0, "zone id '" + Zones.Volcano + "' reserved (bit 12, appended)");

            // ---- swim raft far out
            bool raft = PropInfo.Spot("swim_raft", out Vector3 rp);
            float raftShore = 999f;
            for (float d = 0f; d < 80f; d += 1f)
            {
                Vector3 q = rp - new Vector3(d, 0f, 0f);
                if (WorldInfo.GroundHeight(q) > 0f) { raftShore = d; break; }
            }
            Check(raft && raftShore > 30f && WorldInfo.SeafloorDepth(rp) > 2.7f, "swim raft out in the sea: " + F(raftShore, "0") + " m to the beach, " + F(WorldInfo.SeafloorDepth(rp)) + " m of water under it");

            // ---- signposts
            int signs = PropPaths.SignAudit.Count, offTrail = 0, wet = 0, village = 0, solid = 0;
            foreach (var s in PropPaths.SignAudit)
            {
                float dp = PropPaths.DistanceToPath(s.at.x, s.at.z);
                if (dp < 1.2f || dp > 3.6f) offTrail++;
                if (s.at.y < 0.4f) wet++;
                if (s.icons != null) foreach (var ic in s.icons) if (ic == PropIcon.Village) village++;
                if (Physics.CheckCapsule(s.at + Vector3.up * 0.5f, s.at + Vector3.up * 1.6f, 0.12f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) solid++;
            }
            Check(signs >= 10 && offTrail == 0 && wet == 0 && village == 0 && solid == signs,
                  signs + " signposts: " + offTrail + " not beside their trail, " + wet + " in the wet, " + village + " pointing to the old village, " + solid + " solid");
            if (signs > 0 && pc != null)
            {
                var s0 = PropPaths.SignAudit[0];
                Vector3 to = new Vector3(s0.toward[0].x, s0.at.y, s0.toward[0].y);
                pc.enabled = false;
                SetTime(0.45f);
                Pose(s0.at + (s0.at - to).normalized * 3.2f + Vector3.up * 1.7f, s0.at + Vector3.up * 1.5f);
                yield return Wait(1.2f);
                yield return Shot("60_signpost_hut");
                pc.enabled = true;
            }

            // ---- map pieces, treasure, campfires, daily finds
            int valid = 0, buried = 0, far = 0;
            for (int i = 0; i < MapPieces.Count; i++)
            {
                var sp = MapPieces.Spots[i];
                if (!sp.valid) continue;
                valid++;
                if (sp.buried) buried++;
                float nearest = 999f;
                for (int k = 0; k < MapPieces.Count; k++) if (k != i && MapPieces.Spots[k].valid) nearest = Mathf.Min(nearest, Vector3.Distance(sp.position, MapPieces.Spots[k].position));
                if (nearest > 20f) far++;
            }
            Check(!MapPieces.UseDefaultPlacement && valid == MapPieces.Count && buried == 4 && far >= 6, "map pieces placed by the world: " + valid + " (" + buried + " buried), " + far + " are ≥ 20 m from every other");
            float xPath = PropPaths.DistanceToPath(MapPieces.SecretSpot.x, MapPieces.SecretSpot.z);
            Check(MapPieces.SecretSpotValid && Vector3.Distance(new Vector3(MapPieces.SecretSpot.x, 0f, MapPieces.SecretSpot.z), new Vector3(-58f, 0f, -102.5f)) < 12f && xPath > 10f,
                  "the treasure X lies in the hidden south-west cove " + V(MapPieces.SecretSpot) + ", " + F(xPath, "0") + " m from the nearest trail");
            int hiddenBuried = 0;
            for (int i = 0; i < MapPieces.Count; i++)
                if (MapPieces.Spots[i].valid && MapPieces.Spots[i].buried && PropPaths.DistanceToPath(MapPieces.Spots[i].position.x, MapPieces.Spots[i].position.z) >= 11.5f) hiddenBuried++;
            Check(hiddenBuried >= 3, hiddenBuried + "/4 buried pieces lie ≥ 12 m away from the trails (not visible from the main paths)");
            // the volcano opens with the wreck: the gate follows the wreck unlock, the abyss for the monster exists
            Check(VolcanoGate.Open == GameManager.I.State.unlockedSites.Contains("wreck") && WorldInfo.SeafloorDepth(WorldLayout.VolcanoAbyss) > 110f && PropInfo.Spot("volcano_abyss", out _),
                  "volcano gate = wreck unlock (" + VolcanoGate.Open + "), Urzeit-Schlund " + F(WorldInfo.SeafloorDepth(WorldLayout.VolcanoAbyss), "0") + " m deep");
            Check(QuestSpots.IsRegistered("horst_hammock") && QuestSpots.IsRegistered("horst_home") && Vector3.Distance(QuestSpots.Get("horst_bench"), WorldLayout.DockStart) > 15f,
                  "Horst lives in his beach corner (hammock + bench " + F(Vector3.Distance(QuestSpots.Get("horst_bench"), WorldLayout.DockStart), "0") + " m from the dock)");
            int fires = 0;
            foreach (var id in new[] { "camp_hut_beach", "camp_driftwood", "camp_bay", "camp_south_beach", "camp_volcano", "camp_hermits" })
                if (PropInfo.Spot("campfire_" + id, out _)) fires++;
            Check(fires == 6, fires + "/6 campfire spots (hut beach, driftwood beach, hidden bay, south beach, volcano, hermit camp)");
            int loot = 0;
            foreach (var l in Object.FindObjectsByType<PropLoot>(FindObjectsSortMode.None))
                if (l != null && l.lootId != null && (l.lootId.StartsWith("wood_south") || l.lootId.StartsWith("stone_volcano") || l.lootId.StartsWith("coconut_south"))) loot++;
            Check(loot >= 6, loot + " new daily finds on the south beach and the volcano");
            if (pc != null) pc.enabled = true;
        }
    }
}
