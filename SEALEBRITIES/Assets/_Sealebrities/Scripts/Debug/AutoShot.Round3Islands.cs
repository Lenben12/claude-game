using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 screenshots – the landmark islands with their own identities and the volcano island. Files: r3i_*.png.
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3Islands()
        {
            yield return Guard(ShotR3iVolcanoFromDock(), "r3i_volcano_from_dock");
            yield return Guard(ShotR3iVolcanoBeach(), "r3i_volcano_beach");
            yield return Guard(ShotR3iVolcanoCrater(), "r3i_volcano_crater");
            yield return Guard(ShotR3iVolcanoShelf(), "r3i_volcano_underwater_shelf");
            yield return Guard(ShotR3iVolcanoCave(), "r3i_volcano_cave");
            yield return Guard(ShotR3iIsle("kelp_forest", "r3i_isle_kelp", 0.45f), "r3i_isle_kelp");
            yield return Guard(ShotR3iIsle("wreck", "r3i_isle_wreck", 0.45f), "r3i_isle_wreck");
            yield return Guard(ShotR3iIsle("blue_drop", "r3i_isle_blue", 0.45f), "r3i_isle_blue");
            yield return Guard(ShotR3iIsle("deep_trench", "r3i_isle_trench", 0.47f), "r3i_isle_trench");
            yield return Guard(ShotR3iIsle("fog_isle", "r3i_isle_fog", 0.8f), "r3i_isle_fog");
            if (lamp != null) lamp.SetOn(false);
            if (pc != null) pc.enabled = true;
        }

        static Vector3 VolcanoCenter => WorldLayout.VolcanoIsland;

        // the volcano across the house reef from the dock end at golden hour – the smoke column must read
        IEnumerator ShotR3iVolcanoFromDock()
        {
            SetTime(0.74f);
            yield return EnsureOnLand();
            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Vector3 v = VolcanoCenter;
            FreeCamera(end + Vector3.up * 1.7f, new Vector3(v.x, 38f, v.z));
            yield return Wait(3f);
            yield return Capture("r3i_volcano_from_dock");
        }

        // on the black sand looking up at the cone (palms, boulders, ferns, the plume)
        IEnumerator ShotR3iVolcanoBeach()
        {
            SetTime(0.42f);
            yield return EnsureOnLand();
            Vector3 v = VolcanoCenter;
            Vector3 beach = PropInfo.Spot("volcano_beach", out var b) ? b : Ground(WorldFeatures.VolcanoBeach);
            Vector3 side = Vector3.Cross(Vector3.up, Flat(v - beach));
            Vector3 eye = beach - Flat(v - beach) * 4f + side * 6f;
            eye.y = Mathf.Max(WorldInfo.GroundHeight(eye), 0f) + 1.7f;
            FreeCamera(eye, new Vector3(v.x, 24f, v.z));
            yield return Wait(2.5f);
            yield return Capture("r3i_volcano_beach");
        }

        // above the crater at dusk: the lava lake glow, cracks, the plume rising out of it
        IEnumerator ShotR3iVolcanoCrater()
        {
            SetTime(0.8f);
            yield return EnsureOnLand();
            Vector3 v = VolcanoCenter;
            Vector3 floor = PropInfo.Spot("volcano_crater_floor", out var f) ? f : new Vector3(v.x, WorldFeatures.CraterFloorY, v.z);
            Vector3 dir = Flat(WorldFeatures.VolcanoBeachDir);
            FreeCamera(floor + dir * 22f + Vector3.up * 24f, floor + Vector3.up * 1f);
            yield return Wait(3f);
            yield return Capture("r3i_volcano_crater");
        }

        // the lava-rock reef with sea lilies on the black-sand shelf
        IEnumerator ShotR3iVolcanoShelf()
        {
            SetTime(0.5f);
            if (!PropInfo.Spot("volcano_reef_2", out var reef) && !PropInfo.Spot("volcano_reef_0", out reef)) { Note("WARNING: no volcano reef spot"); yield break; }
            Vector3 v = VolcanoCenter;
            Vector3 outward = Flat(reef - v);
            Vector3 eye = KeepInWater(reef + outward * 7f + Vector3.Cross(Vector3.up, outward) * 3f + Vector3.up * 2.5f, 1.5f, 1.2f);
            yield return Dive(eye, reef + Vector3.up * 0.6f);
            ClearCrowd();
            yield return Wait(1.2f);
            yield return Capture("r3i_volcano_underwater_shelf");
        }

        // inside the lava-tube arches on the east drop-off (flashlight on)
        IEnumerator ShotR3iVolcanoCave()
        {
            SetTime(0.5f);
            if (!PropInfo.Spot("volcano_cave", out var cave)) { Note("WARNING: no volcano_cave spot"); yield break; }
            Vector3 eye = PropInfo.Spot("volcano_cave_in", out var cin) ? cin : cave + Vector3.up * 3f;
            yield return Dive(eye, cave);
            Hold(HoldItem.Camera);
            if (lamp != null) lamp.SetOn(true);
            ClearCrowd();
            yield return Wait(2.5f);
            yield return Capture("r3i_volcano_cave");
            if (lamp != null) lamp.SetOn(false);
            Hands.StowAll();
        }

        // a landmark island from the boat's approach height: its identity (+ landmark) in frame
        IEnumerator ShotR3iIsle(string id, string file, float time)
        {
            SetTime(time);
            yield return EnsureOnLand();
            Vector3 top = Vector3.zero;
            foreach (var mi in WorldFeatures.MiniIslands) if (mi.id == id) top = Ground(mi.center);
            if (top == Vector3.zero) { Note("WARNING: no landmark island " + id); yield break; }
            Vector3 look = PropInfo.MiniLandmarks.TryGetValue(id, out var lm) ? Vector3.Lerp(top, lm, 0.5f) : top;
            Vector3 dir = Flat(look - top);
            if ((look - top).sqrMagnitude < 4f) dir = Flat(new Vector3(-0.6f, 0f, -0.8f));
            dir = Quaternion.Euler(0f, 35f, 0f) * dir;
            Vector3 eye = top + dir * 62f;
            eye.y = Mathf.Max(top.y, 2f) + 16f;
            FreeCamera(eye, look + Vector3.up * 2f);
            yield return Wait(2.5f);
            yield return Capture(file);
        }
    }
}
