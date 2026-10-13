using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 4 screenshots (fauna + sea content): open-sea life, a shark circling, Big Mo in the sinkhole, the
    /// living fossils, a night dive, chests, the four wrecks and all ten curiosities. Files: r3f_*.png.
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3Fauna()
        {
            var gm = GameManager.I;
            if (gm != null && gm.State != null)
            {
                if (!gm.State.unlockedSites.Contains("kelp_forest")) gm.State.unlockedSites.Add("kelp_forest");
                if (!gm.State.unlockedSites.Contains("wreck")) gm.State.unlockedSites.Add("wreck");
            }
            yield return Guard(ShotR3fOpenSea(), "r3f_open_sea_life");
            yield return Guard(ShotR3fNightReef(), "r3f_night_reef");
            yield return Guard(ShotR3fShark(), "r3f_shark_circling");
            yield return Guard(ShotR3fFossils(), "r3f_living_fossils");
            yield return Guard(ShotR3fBigMo(), "r3f_big_mo");
            yield return Guard(ShotR3fChest(), "r3f_chest");
            foreach (var id in new[] { "sailboat", "seaplane", "trawler", "pirates" })
                yield return Guard(ShotR3fWreck(id), "r3f_wreck_" + id);
            foreach (var m in SeaContent.Memes)
                if (m != null && m.Species != null) yield return Guard(ShotR3fMeme(m), "r3f_" + m.Species.id);
            yield return Guard(ShotR3fBeach(), "r3f_island_crabs");
            SetTime(0.5f);
            if (lamp != null) lamp.SetOn(false);
            if (pc != null) pc.enabled = true;
        }

        // the open sea between the island and the volcano at 8 m – schools, single fish, bait clouds
        IEnumerator ShotR3fOpenSea()
        {
            SetTime(0.5f);
            Vector3 p = new Vector3(430f, 0f, 260f);
            Vector3 eye = KeepInWater(new Vector3(p.x, -8f, p.z), 4f, 3f);
            yield return Dive(eye, eye + new Vector3(1f, -0.1f, 0.3f) * 10f);
            yield return Wait(7f);
            // look where the animals are (the spawner fills a ring around the diver – a fixed heading often shows the
            // empty side; nothing is spawned for the shot)
            Pose(eye, BestLook(eye, 24f, out int n));
            yield return Wait(0.8f);
            Note("r3f_open_sea_life: " + n + " animals in the chosen view");
            yield return Capture("r3f_open_sea_life");
            // the house reef with the same rule (final step 7: the showcase needs a representative reef frame)
            var site = DiveSites.Get("house_reef");
            if (site != null)
            {
                Vector3 anchor = PropInfo.AnemoneSpots.Count > 0 ? PropInfo.AnemoneSpots[0] : site.center + Vector3.down * 6f;
                Vector3 reye = KeepInWater(anchor + new Vector3(3f, 2.6f, 2f), 1.4f, 1.6f);
                yield return Dive(reye, anchor);
                yield return Wait(8f);
                Pose(reye, BestLook(reye, 20f, out int rn));
                yield return Wait(0.8f);
                Note("r3f_reef_life: " + rn + " animals in the chosen view");
                yield return Capture("r3f_reef_life");
            }
        }

        /// The heading (16 tries) with the most live sea animals 2.5–range m away inside a ±30° cone; returns a look-at point.
        static Vector3 BestLook(Vector3 eye, float range, out int count)
        {
            int best = -1;
            float bestYaw = 0f;
            var reg = CreatureRegistry.Active;
            for (int k = 0; k < 16; k++)
            {
                float yaw = k * 22.5f;
                Vector3 f = Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, -0.12f, 1f);
                int n = 0;
                for (int i = 0; i < reg.Count; i++)
                {
                    var c = reg[i];
                    if (c == null || (c is Object o && o == null) || !c.IsAlive || c.Species == null || c.Species.land) continue;
                    Vector3 d = c.Position - eye;
                    float m = d.magnitude;
                    if (m < 2.5f || m > range) continue;
                    if (Vector3.Angle(f, d) < 30f) n++;
                }
                if (n > best) { best = n; bestYaw = yaw; }
            }
            count = Mathf.Max(0, best);
            return eye + Quaternion.Euler(0f, bestYaw, 0f) * new Vector3(0f, -0.12f, 1f) * 10f;
        }

        // night dive on the house reef with the lamp: whitetip reef sharks and flashlight fish
        IEnumerator ShotR3fNightReef()
        {
            SetTime(0.96f);
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.6f, -10f, -6f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 2.5f, 2f, 2f);
            Vector3 fwd = new Vector3(0.8f, 0f, 0.6f);
            yield return Dive(eye, eye + fwd * 8f);
            if (lamp != null) lamp.SetOn(true);
            Spawn("whitetip_shark", KeepInWater(eye + fwd * 6f, 1f, 1.5f), SizeTier.Large);
            Spawn("flashlight_fish", KeepInWater(eye + fwd * 3.5f + Vector3.up * 0.3f, 1f, 1.5f), SizeTier.Normal);
            Spawn("comb_jelly", KeepInWater(eye + fwd * 4f + Vector3.right * 1.5f + Vector3.up, 1f, 1.5f), SizeTier.Normal);
            yield return Wait(2.5f);
            yield return Capture("r3f_night_reef");
            if (lamp != null) lamp.SetOn(false);
            SetTime(0.5f);
        }

        IEnumerator ShotR3fShark()
        {
            SetTime(0.5f);
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.6f, -10f, -6f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 3f, 2f, 2f);
            yield return Dive(eye, eye + Vector3.forward * 8f);
            var th = FaunaThreat.Instance;
            if (th == null || !th.ForceStalk("whitetip_shark")) { Note("WARNING: no stalk for the shark shot"); yield break; }
            float t = 0f;
            while (t < 30f && FaunaThreat.Phase != 2) { t += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(2.5f);
            var h = FaunaThreat.ActiveHunter;
            if (h != null) Pose(eye, Vector3.Lerp(eye + Vector3.forward * 3f, h.pos, 0.85f));
            yield return Wait(0.2f);
            yield return Capture("r3f_shark_circling");
            th.CancelStalk();
        }

        IEnumerator ShotR3fFossils()
        {
            SetTime(0.5f);
            Vector3 reef = PropInfo.Spot("volcano_reef_2", out var r2) ? r2 : WorldLayout.VolcanoIsland + new Vector3(-80f, -8f, 0f);
            Vector3 outward = Flat(reef - WorldLayout.VolcanoIsland);
            Vector3 eye = KeepInWater(reef + outward * 4f + Vector3.up * 2f, 1.5f, 1.5f);
            yield return Dive(eye, reef + Vector3.up * 0.5f);
            ClearCrowd();
            var sp = FaunaModule.Spawner;
            if (sp != null)
            {
                Vector3 look = Flat(reef - eye);
                Vector3 side = Vector3.Cross(Vector3.up, look);
                sp.SpawnOne(SpeciesDatabase.Get("horseshoe_crab"), Ground(reef + side * 0.6f) + Vector3.up * 0.1f, reef, 6f);
                sp.SpawnOne(SpeciesDatabase.Get("sea_lily"), Ground(reef - side * 1.2f + look * 0.8f) + Vector3.up * 0.1f, reef, 3f);
                sp.SpawnOne(SpeciesDatabase.Get("nautilus"), KeepInWater(reef + Vector3.up * 1.6f + side * 0.4f, 1f, 1.5f), reef, 6f);
                sp.SpawnOne(SpeciesDatabase.Get("hagfish"), Ground(reef - side * 0.3f - look * 0.6f) + Vector3.up * 0.15f, reef, 6f);
            }
            yield return Wait(1.6f);
            yield return Capture("r3f_living_fossils");
        }

        IEnumerator ShotR3fBigMo()
        {
            SetTime(0.5f);
            Vector3 home = FaunaVolcano.AbyssHome;
            Vector3 eye = KeepInWater(new Vector3(home.x + 6f, -14f, home.z - 6f), 6f, 6f);
            yield return Dive(eye, eye + Vector3.down * 8f + Vector3.forward * 6f);
            var mo = FaunaVolcano.Monster ?? FaunaVolcano.SpawnMonster(home);
            if (mo == null) { Note("WARNING: Big Mo did not spawn"); yield break; }
            var mb = mo.behavior as FaunaMonsterBehavior;
            float t = 0f;
            while (t < 60f && (mb == null || mb.State < 2)) { t += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(3f);
            Pose(eye, mo.WorldBounds.center);
            yield return Wait(0.2f);
            yield return Capture("r3f_big_mo");
        }

        IEnumerator ShotR3fChest()
        {
            SetTime(0.5f);
            SeaChest chest = null;
            foreach (var c in SeaContent.Chests) if (c != null && c.chestId == "hr_coral") chest = c;
            if (chest == null && SeaContent.Chests.Count > 0) chest = SeaContent.Chests[0];
            if (chest == null) { Note("WARNING: no chest placed"); yield break; }
            Vector3 p = chest.transform.position;
            Vector3 eye = KeepInWater(p + chest.transform.forward * 2.2f + Vector3.up * 1.1f, 1f, 0.8f);
            yield return Dive(eye, p + Vector3.up * 0.3f);
            yield return Wait(1f);
            yield return Capture("r3f_chest");
        }

        IEnumerator ShotR3fWreck(string id)
        {
            SetTime(0.5f);
            if (!SeaContent.Placed.TryGetValue("wreck_" + id, out var p)) { Note("WARNING: wreck " + id + " not placed"); yield break; }
            Vector3 eye = KeepInWater(p + new Vector3(9f, 4f, -7f), 2f, 1.5f);
            yield return Dive(eye, p + Vector3.up * 1.5f);
            if (lamp != null && Waves.SeaLevel - p.y > 22f) lamp.SetOn(true);
            yield return Wait(1.5f);
            yield return Capture("r3f_wreck_" + id);
            if (id == "trawler" && SeaContent.Placed.TryGetValue("room_trawler", out var room))
            {
                yield return Dive(room + Vector3.up * 0.1f, room + Vector3.forward * 2f + Vector3.down * 0.5f);
                if (lamp != null) lamp.SetOn(true);
                yield return Wait(1f);
                yield return Capture("r3f_trawler_wheelhouse");
            }
            if (lamp != null) lamp.SetOn(false);
        }

        IEnumerator ShotR3fMeme(SeaMemeActor m)
        {
            SetTime(0.5f);
            Vector3 p = m.WorldBounds.center;
            bool surface = m.Species.id == "meme_ducks";
            Vector3 eye = surface ? new Vector3(p.x + 7f, Waves.SeaLevel + 2.2f, p.z - 5f)
                                  : KeepInWater(p + new Vector3(2.4f, 0.6f, -1.9f) * Mathf.Clamp(m.WorldBounds.size.magnitude * 0.45f, 1f, 1.8f), 1f, 1f);
            if (surface)
            {
                yield return EnsureOnLand();
                FreeCamera(eye, p);
            }
            else yield return Dive(eye, p);
            yield return Wait(2f);
            Pose(eye, m.WorldBounds.center);
            yield return Wait(0.2f);
            yield return Capture("r3f_" + m.Species.id);
        }

        IEnumerator ShotR3fBeach()
        {
            SetTime(0.42f);
            yield return EnsureOnLand();
            Vector3 beach = Ground(WorldLayout.DockStart + new Vector3(-8f, 0f, -10f));
            var sp = FaunaModule.Spawner;
            if (sp != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = Ground(beach + new Vector3(i * 0.9f - 1.2f, 0f, 2f + (i % 2) * 0.7f));
                    var crab = sp.SpawnOne(SpeciesDatabase.Get("red_crab"), c + Vector3.up * 0.1f, c, 6f);
                    if (crab != null) crab.landSpawned = true;
                }
            }
            FreeCamera(beach + new Vector3(-1.5f, 1.4f, -1.5f), beach + new Vector3(0f, 0.2f, 2.2f));
            yield return Wait(1.2f);
            yield return Capture("r3f_island_crabs");
        }
    }
}
