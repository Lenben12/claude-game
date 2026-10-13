using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 screenshots – the island map: the whole island from the air (morning + golden hour), the open south
    /// beach where the village was, the hidden hermit camp (from the rim, at the fire, at dusk with the fire glow), the
    /// game trail with its cairns, a signpost at the hut junction, the swim raft out in the sea, the volcano seen from the
    /// dock and from the lighthouse. Files: r3w_*.png.
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3World()
        {
            yield return Guard(ShotR3WIsland(), "r3w_island");
            yield return Guard(ShotR3WCamp(), "r3w_camp");
            yield return Guard(ShotR3WDetails(), "r3w_details");
            if (pc != null) pc.enabled = true;
        }

        IEnumerator ShotR3WIsland()
        {
            yield return EnsureOnLand();
            if (pc != null) pc.enabled = false;
            // the whole island from the south-east at 120 m – hut, dock and lighthouse must read at a glance
            SetTime(0.38f);
            FreeCamera(new Vector3(150f, 120f, -170f), new Vector3(0f, 5f, 10f));
            yield return Wait(2.5f);
            yield return Capture("r3w_island_air_morning");
            SetTime(0.74f);
            FreeCamera(new Vector3(-170f, 95f, -150f), new Vector3(10f, 5f, 15f));
            yield return Wait(2.5f);
            yield return Capture("r3w_island_air_west_golden");
            // the open south beach (no village houses any more), jetty and canoes
            SetTime(0.6f);
            Vector3 v = WorldLayout.Village;
            FreeCamera(new Vector3(v.x + 30f, 14f, v.z - 40f), new Vector3(v.x, 2f, v.z - 6f));
            yield return Wait(2f);
            yield return Capture("r3w_south_beach");
        }

        IEnumerator ShotR3WCamp()
        {
            Vector3 c = WorldLayout.HermitCamp;
            c.y = WorldInfo.GroundHeight(c);
            Vector3 e = WorldFeatures.CampEntranceDir;
            Vector3 side = Vector3.Cross(Vector3.up, e);
            // from above: the hollow hidden in the jungle (you should NOT see much of it from here)
            SetTime(0.45f);
            FreeCamera(c + new Vector3(30f, 45f, -40f), c);
            yield return Wait(2f);
            yield return Capture("r3w_camp_from_air");
            // from the rim at the gap: tents, fire ring, junk totems
            FreeCamera(c + e * 13f + side * 3f + Vector3.up * 4.5f, c + Vector3.up * 0.8f - e * 2f);
            yield return Wait(2f);
            yield return Capture("r3w_camp_overview");
            // at the fire, eye height, toward the leader's shelter
            FreeCamera(c + e * 3.5f - side * 2f + Vector3.up * 1.6f, c - e * 6f + side * 5f + Vector3.up * 1.2f);
            yield return Wait(1.5f);
            yield return Capture("r3w_camp_fire");
            // the shrine to the sea (shells, driftwood arch, glass floats)
            if (PropInfo.Spot("hermit_shrine", out Vector3 shrine))
            {
                FreeCamera(shrine + Vector3.up * 1.6f + side * 1.2f, shrine + (c - shrine).normalized * 1.4f + Vector3.up * 0.9f);
                yield return Wait(1.2f);
                yield return Capture("r3w_camp_shrine");
            }
            // dusk: the smoke column and the glow (the smoking rack + the hermits' lanterns are the hint from afar)
            SetTime(0.8f);
            FreeCamera(c - e * 9f + side * 4f + Vector3.up * 3f, c + Vector3.up * 1f);
            yield return Wait(2f);
            yield return Capture("r3w_camp_dusk");
            // the game trail from the grotto clearing: the cairns lead the way
            SetTime(0.5f);
            Vector3 ts = WorldFeatures.CampTrailStart;
            ts.y = WorldInfo.GroundHeight(ts);
            FreeCamera(ts + e * 2.5f + Vector3.up * 1.7f, WorldFeatures.CampTrailGap + Vector3.up * 1.2f);
            yield return Wait(1.5f);
            yield return Capture("r3w_camp_trail");
            // the sea lookout
            Vector3 lk = WorldFeatures.CampLookout;
            lk.y = WorldInfo.GroundHeight(lk);
            FreeCamera(lk + (c - lk).normalized * 3f + Vector3.up * 1.8f, lk + (lk - c).normalized * 30f);
            yield return Wait(1.5f);
            yield return Capture("r3w_camp_lookout");
        }

        IEnumerator ShotR3WDetails()
        {
            SetTime(0.47f);
            // a signpost at the hut junction, readable, standing beside its trail
            if (PropPaths.SignAudit.Count > 0)
            {
                var s = PropPaths.SignAudit[0];
                Vector3 to = new Vector3(s.toward[0].x, s.at.y, s.toward[0].y);
                Vector3 back = (s.at - to); back.y = 0f; back.Normalize();
                FreeCamera(s.at + back * 3f + Vector3.Cross(Vector3.up, back) * 1.2f + Vector3.up * 1.7f, s.at + Vector3.up * 1.55f);
                yield return Wait(1.2f);
                yield return Capture("r3w_signpost_hut");
            }
            // the swim raft out in the sea (from the beach)
            if (PropInfo.Spot("swim_raft", out Vector3 raft))
            {
                Vector3 beach = new Vector3(WorldLayout.DockStart.x - 2f, 0f, raft.z - 8f);
                beach.y = Mathf.Max(WorldInfo.GroundHeight(beach), Waves.SeaLevel) + 1.7f;
                FreeCamera(beach, raft + Vector3.up * 1f);
                yield return Wait(1.5f);
                yield return Capture("r3w_swim_raft");
            }
            // the volcano on the horizon from the dock end at golden hour (landmark + smoke)
            SetTime(0.73f);
            Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            FreeCamera(dock + Vector3.up * 1.6f, WorldLayout.VolcanoIsland + Vector3.up * 18f);
            yield return Wait(2f);
            yield return Capture("r3w_volcano_from_dock");
            // a map piece clue: the pebble arrow + the dig mound on the south beach
            if (MapPieces.Spots[5].valid)
            {
                Vector3 p = MapPieces.Spots[5].position;
                SetTime(0.5f);
                FreeCamera(p + new Vector3(3.5f, 2.6f, -3.5f), p);
                yield return Wait(1.2f);
                yield return Capture("r3w_map_piece_dig");
            }
        }
    }
}
