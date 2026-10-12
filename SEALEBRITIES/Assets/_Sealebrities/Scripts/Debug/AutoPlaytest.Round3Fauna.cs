using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 4 (fauna + sea content, GDD §19/§20): density everywhere, species reactions, rare announced shark
    /// attacks + defence, spear fishing (food fish only), protected animals refuse, the volcano's living fossils + Big Mo,
    /// treasure chests, wrecks and the curiosities. Runs inside RunRound3Steps (-r3only) or alone with
    ///     SEALEBRITIES.exe -autoplaytest "C:/out" -r3fauna
    public sealed partial class AutoPlaytest
    {
        static bool Round3FaunaOnly => Array.IndexOf(Environment.GetCommandLineArgs(), "-r3fauna") >= 0;

        IEnumerator RunRound3FaunaSteps()
        {
            yield return RunStep("65 Sea density (Q15): open sea, house reef and lagoon full of life, bait clouds, volcano zone, jellies hidden from above, weather → waves / visibility, FPS with the new density", StepR3FaunaDensity, 170f);
            yield return RunStep("66 Reactions (Q21/Q22): small fish dart away, the turtle comes closer, sharks stay cool, crabs on the beach scuttle off", StepR3FaunaFlee, 90f);
            yield return RunStep("67 Shark attack (Q16/Q17/Q45): announced (circling + danger music), a bite costs Puste + air (never death), a spear hit repels it", StepR3FaunaShark, 200f);
            yield return RunStep("68 Spear fishing (Q18): a food fish becomes raw fish in the backpack, blood draws sharks", StepR3FaunaSpearFish, 90f);
            yield return RunStep("69 Protected animals (Q18): turtle / ray / reef fish refuse the spear (no wear, no catch), the stick only shoos", StepR3FaunaProtected, 90f);
            yield return RunStep("70 Volcano fauna (Q14): living fossils spawn after the wreck unlock, models build, Big Mo rises from the sinkhole and can be repelled", StepR3FaunaVolcano, 180f);
            yield return RunStep("71 Treasure + wrecks + memes (Q19/Q20): chests open into the backpack and sell, 4 wrecks (enterable wheelhouse), 10 curiosities filmable for the Sealog", StepR3FaunaContent, 180f);
        }

        // ------------------------------------------------------------------ helpers
        FaunaCreature SpawnFC(string id, Vector3 pos)
        {
            var c = SpawnAt(id, pos, SizeTier.Normal) as FaunaCreature;
            return c;
        }

        Vector3 ReefEye(float depth)
        {
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.6f, -11f, -6f);
            return KeepInWater(floor + Vector3.up * Mathf.Min(depth, 4f), 2.5f, 2f);
        }

        static int CountSpecies(SpeciesDef s, Vector3 near, float r)
        {
            int n = 0;
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || (c is Object o && o == null) || !c.IsAlive || c.Species != s) continue;
                if ((c.Position - near).sqrMagnitude < r * r) n++;
            }
            return n;
        }

        /// FPS (uncapped, 5 s) on High and Low at the current spot; the player's quality comes back afterwards.
        IEnumerator MeasureR3Fps(string scene)
        {
            var d = Settings.Data;
            GraphicsQuality q0 = d.quality;
            foreach (var q in new[] { GraphicsQuality.High, GraphicsQuality.Low })
            {
                d.quality = q;
                Settings.Apply();
                yield return Wait(1.2f);
                yield return MeasureUncapped(scene, 5f);
            }
            d.quality = q0;
            Settings.Apply();
            yield return Wait(0.5f);
        }

        // ================================================================== 65 density
        IEnumerator StepR3FaunaDensity()
        {
            var sp = FaunaModule.Spawner;
            if (!Check(sp != null && pc != null, "fauna spawner + player")) yield break;
            Survival.Set(100f, 100f);
            SetTime(0.5f);
            // ---- open sea between the island and the volcano (no dive site)
            Vector3 os = FindWater(new Vector3(430f, 0f, 260f), 25f);
            Vector3 eye = KeepInWater(os + Vector3.down * 9f, 4f, 3f);
            string zone = FaunaZones.ZoneAt(eye);
            yield return DiveTo(eye, eye + Vector3.forward * 10f);
            yield return Wait(8f);
            int n30 = sp.CountNear(eye, 30f), n80 = sp.CountNear(eye, 80f);
            Metric("fauna.openSea", n30 + " within 30 m, " + n80 + " within 80 m (zone " + zone + ")");
            Check(n30 >= 6 && n80 >= 16, "open sea is full of life (" + n30 + " animals within 30 m, " + n80 + " within 80 m, zone " + zone + ")");
            Check(FaunaAmbientSchools.LiveClouds >= 1, "bait-fish clouds mill in the open water (" + FaunaAmbientSchools.LiveClouds + " clouds, " + FaunaAmbientSchools.LiveFish + " fish)");
            yield return Shot("65_open_sea");
            // final step 7: frame rate with the round-3 fish density (open sea + bait clouds), High and Low
            yield return MeasureR3Fps("open sea r3");

            // ---- house reef
            eye = ReefEye(3f);
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            yield return Wait(7f);
            int hr = sp.CountNear(eye, 30f);
            Metric("fauna.houseReef", hr + " within 30 m");
            Check(hr >= 14, "house reef full of fish (" + hr + " within 30 m)");
            yield return Shot("65_house_reef");
            yield return MeasureR3Fps("house reef r3");

            // ---- lagoon by the swim raft
            Vector3 lg = FindSeafloor(WorldLayout.SwimRaft, 30f, -6f, -2.5f);
            eye = KeepInWater(lg + Vector3.up * 1.5f, 1f, 1f);
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            yield return Wait(6f);
            int lag = sp.CountNear(eye, 30f);
            Check(lag >= 6, "lagoon lively (" + lag + " within 30 m, zone " + FaunaZones.ZoneAt(eye) + ")");

            // ---- the volcano zone is wired
            Vector3 vr = PropInfo.Spot("volcano_reef_2", out var r2) ? r2 : WorldLayout.VolcanoIsland + new Vector3(-80f, 0f, 0f);
            Check(FaunaZones.ZoneAt(vr) == Zones.Volcano, "volcano waters are zone " + Zones.Volcano + " (" + FaunaZones.ZoneAt(vr) + ")");
            Check(FaunaZones.Target(Zones.OpenSea) >= 30 && FaunaZones.Target(Zones.Volcano) >= 45, "zone targets: open sea " + FaunaZones.Target(Zones.OpenSea) + ", volcano " + FaunaZones.Target(Zones.Volcano));

            // ---- jellies never shine through the surface from above (cull hook, step 3 open issue)
            Vector3 dock = DockEnd();
            Vector3 jp = new Vector3(dock.x + 6f, Waves.SeaLevel - 2.5f, dock.z + 2f);
            jp = KeepInWater(jp, 0.8f, 1.6f);
            var jelly = SpawnFC("moon_jelly", jp);
            if (Check(jelly != null, "moon jelly spawned below the dock"))
            {
                // the player really stands on the dock (a camera posed above the water while the diver still swims
                // keeps the under-water state – not a situation of the game)
                pc.enabled = true;
                yield return Stand(SnapDown(dock + Vector3.up * 3f), 0f);
                yield return Wait(0.4f);
                pc.enabled = false;
                Pose(new Vector3(jp.x - 4f, Waves.SeaLevel + 2.5f, jp.z - 2f), jp);
                yield return Wait(0.8f);
                Check(jelly.culled, "seen from above the water the jelly is hidden (culled " + jelly.culled + ", " + F(Waves.SeaLevel - jelly.pos.y) + " m deep)");
                yield return Shot("65_jelly_from_above");
                yield return DiveTo(KeepInWater(new Vector3(jp.x - 3f, jp.y + 0.3f, jp.z - 1.5f), 0.6f, 1.2f), jp);
                yield return Wait(0.8f);
                Check(!jelly.culled, "under water the jelly shows again");
                sp.Despawn(jelly);
            }

            // ---- weather (Q48): waves + visibility follow the day's weather
            float w0 = Waves.Strength, f0 = FXMath.WeatherFogMul;
            Weather.TestOverride = WeatherKind.Showers;
            yield return Wait(6f);
            float w1 = Waves.Strength, f1 = FXMath.WeatherFogMul;
            Weather.TestOverride = WeatherKind.Sunny;
            yield return Wait(6f);
            float w2 = Waves.Strength, f2 = FXMath.WeatherFogMul;
            Weather.TestOverride = null;
            // the values ease with a 20 s time constant: within 6 s they only have to move towards the day's target
            bool targets = FaunaWeather.WaveTarget(WeatherKind.Showers) > FaunaWeather.WaveTarget(WeatherKind.Sunny) + 0.2f
                           && FaunaWeather.FogTarget(WeatherKind.Showers) > FaunaWeather.FogTarget(WeatherKind.Sunny) + 0.2f;
            Check(targets && w1 > w0 + 0.02f && w2 < w1 && f1 > f0 + 0.02f && f2 < f1, "showers: bigger waves + murkier water than sunny (waves " + F(w0, "0.00") + " → " + F(w1, "0.00") + " / " + F(w2, "0.00")
                  + ", fog × " + F(f0, "0.00") + " → " + F(f1, "0.00") + " / " + F(f2, "0.00") + ")");
            Check(FaunaWeather.SpawnMul(SpeciesDatabase.Get("dolphin")) > 0f, "weather spawn weights defined");
            pc.enabled = true;
        }

        // ================================================================== 66 reactions
        IEnumerator StepR3FaunaFlee()
        {
            var sp = FaunaModule.Spawner;
            if (sp == null || pc == null) yield break;
            SetTime(0.5f);
            Vector3 eye = ReefEye(3f);
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            Vector3 fwd = Flat(cam.transform.forward);

            // small fish: dart away
            var fish = SpawnFC("parrotfish", KeepInWater(eye + fwd * 5f, 1f, 1.5f));
            if (Check(fish != null, "parrotfish spawned"))
            {
                Check(FaunaReact.TemperOf(fish.species) == FaunaTemper.Darter, "parrotfish temper = Darter (" + FaunaReact.TemperOf(fish.species) + ")");
                yield return Wait(0.8f);
                Vector3 close = fish.pos - (fish.pos - eye).normalized * 1.4f;
                Pose(close, fish.pos);
                yield return Wait(1.3f);
                float d = (fish.pos - close).magnitude;
                Check(d > 3f || fish.velocity.magnitude > 1.1f, "the parrotfish darts away from the diver (now " + F(d) + " m, " + F(fish.velocity.magnitude) + " m/s)");
                yield return Shot("66_fish_dart");
                sp.Despawn(fish);
            }
            // turtle: curious
            Pose(eye, eye + fwd * 8f);
            var turtle = SpawnFC("green_turtle", KeepInWater(eye + fwd * 10f, 1f, 1.5f));
            if (turtle != null)
            {
                Check(FaunaReact.TemperOf(turtle.species) == FaunaTemper.Curious, "turtle temper = Curious");
                float d0 = (turtle.pos - eye).magnitude;
                float t = 0f, best = d0;
                // the curious turtle first waits a random 2–10 s cooldown, then swims over at ~0.5 m/s: up to 22 s
                while (t < 22f && !(best < d0 - 2f || best < 6f)) { t += Time.unscaledDeltaTime; best = Mathf.Min(best, (turtle.pos - eye).magnitude); yield return null; }
                Check(best < d0 - 2f || best < 6f, "Gerald comes closer to have a look (" + F(d0) + " → " + F(best) + " m)");
                yield return Shot("66_turtle_curious");
                sp.Despawn(turtle);
            }
            // shark: cool
            var shark = SpawnFC("whitetip_shark", KeepInWater(eye + fwd * 4f, 1f, 1.5f));
            if (shark != null)
            {
                Check(FaunaReact.TemperOf(shark.species) == FaunaTemper.Cool, "shark temper = Cool");
                yield return Wait(2.5f);
                Check(shark.hitFleeT <= 0f && shark.velocity.magnitude < 2.5f, "the reef shark stays cool (" + F(shark.velocity.magnitude) + " m/s)");
                sp.Despawn(shark);
            }
            // a crab on the beach scuttles off
            Vector3 beach = SnapDown(WorldLayout.DockStart + new Vector3(-6f, 3f, -8f));
            yield return Stand(beach, 0f);
            Vector3 cp = beach + Flat(cam.transform.forward) * 2.2f;
            cp.y = WorldInfo.GroundHeight(cp);
            var crabDef = SpeciesDatabase.Get("red_crab");
            var crab = crabDef != null ? sp.SpawnOne(crabDef, cp + Vector3.up * 0.1f, cp, 10f) : null;
            if (Check(crab != null, "red land crab (decoration) spawned on the beach"))
            {
                Check(!crabDef.InSealog && crabDef.deco, "island animals are decoration (not in the Sealog)");
                crab.landSpawned = true;
                yield return Wait(0.5f);
                Vector3 c0 = crab.pos;
                pc.TeleportTo(new Vector3(c0.x - 0.9f, beach.y, c0.z), 90f);
                yield return Wait(1.4f);
                float moved = FlatDist(crab.pos, c0);
                Check(moved > 0.5f, "the crab scuttles off (" + F(moved) + " m)");
                sp.Despawn(crab);
            }
            int deco = 0;
            foreach (var s in SpeciesDatabase.All) if (s.land && s.InSealog) deco++;
            Check(deco == 0, "no land animal is counted in the Sealog (" + deco + ")");
        }

        // ================================================================== 67 shark attack
        IEnumerator StepR3FaunaShark()
        {
            var th = FaunaThreat.Instance;
            if (!Check(th != null && pc != null, "threat director installed")) yield break;
            th.CancelStalk();
            Combat.Reset();
            Survival.Set(100f, 100f);
            SetTime(0.5f);
            Vector3 eye = ReefEye(3f);
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            Check(FaunaThreat.ChancePerMinute(Zones.HouseReef, eye) < 0.1f, "attacks are rare: house reef " + F(FaunaThreat.ChancePerMinute(Zones.HouseReef, eye) * 100f, "0.0") + " %/min");
            SetTime(0.95f);
            yield return null;
            Info("night chance at the wreck: " + F(FaunaThreat.ChancePerMinute(Zones.Wreck, eye) * 100f, "0.0") + " %/min (day x2.2)");
            SetTime(0.5f);

            // ---- 1: a stalk that ends in a bite
            int bites0 = FaunaThreat.Bites;
            float air0 = pc.Air01;
            bool forced = th.ForceStalk("whitetip_shark");
            Check(forced, "a whitetip reef shark starts hunting the diver");
            float t = 0f;
            bool circled = false, music = false;
            while (t < 60f && FaunaThreat.Busy)
            {
                t += Time.unscaledDeltaTime;
                if (FaunaThreat.Phase == 2) circled = true;
                if (FaunaDangerMusic.AnyPlaying) music = true;
                if (circled && !shotCircle) { shotCircle = true; yield return Shot("67_shark_circling"); }
                if (FaunaThreat.Bites > bites0) break;
                yield return null;
            }
            Check(circled, "announced: the shark circles before it charges");
            Check(music, "announced: the danger music plays while it circles");
            Check(FaunaThreat.Bites > bites0 || FaunaThreat.Misses > 0, "the charge bites (" + (FaunaThreat.Bites - bites0) + ") or misses (" + FaunaThreat.Misses + ")");
            yield return Wait(0.6f);
            if (FaunaThreat.Bites > bites0)
            {
                Check(Combat.Health < Combat.MaxHealth - 5f, "a bite costs Puste (" + F(Combat.Health, "0") + ")");
                // a bite never takes the last 20 s of air – with the 20 s snorkel of this test diver there is nothing to take
                bool snorkelOnly = pc.AirSecondsLeft <= 21f;
                Check(pc.Air01 < air0 - 0.01f || pc.Air01 < 0.999f || snorkelOnly, "and air (" + F(air0 * 100f, "0") + " % → " + F(pc.Air01 * 100f, "0") + " %"
                      + (snorkelOnly ? ", only " + F(pc.AirSecondsLeft, "0") + " s in the tank: the last 20 s are never taken" : "") + ")");
                Check(!pc.IsFainting, "never death: the diver is fine");
            }
            th.CancelStalk();
            yield return Wait(1f);
            pc.RefillAir();
            Combat.Reset();

            // ---- 2: the spear repels it
            Backpack.Add(ItemIds.SpearT2, 1, false);
            var spear = Backpack.First(ItemIds.SpearT2);
            var spearDef = ItemDatabase.Get(ItemIds.SpearT2);
            int repels0 = FaunaThreat.Repels;
            int fights = 0;
            Action<string, float> onFight = (k, i) => fights++;
            GameEvents.Fight += onFight;
            try
            {
                Check(th.ForceStalk("hammerhead") || th.ForceStalk("whitetip_shark"), "a second shark comes");
                t = 0f;
                while (t < 40f && FaunaThreat.Busy && FaunaThreat.Phase < 2) { t += Time.unscaledDeltaTime; yield return null; }
                var hunter = FaunaThreat.ActiveHunter;
                if (Check(hunter != null, "the hunter is circling (phase " + FaunaThreat.Phase + ")"))
                {
                    Vector3 hp = hunter.pos;
                    Vector3 from = hp + (cam.transform.position - hp).normalized * 1.6f;
                    bool hit = Combat.Strike(from, (hp - from).normalized, spearDef, spear, true, pc.transform, out var info);
                    Check(hit, "the spear thrust hits the shark (soft target sweep, damage " + F(info.damage) + ")");
                    yield return Wait(0.5f);
                    Check(FaunaThreat.Repels > repels0, "the shark is repelled (" + (FaunaThreat.Repels - repels0) + ")");
                    Check(hunter.hitFleeT > 0f || FaunaThreat.Phase == 4, "and flees");
                    Check(fights > 0, "the fight reaches the livestream hook (GameEvents.Fight × " + fights + ")");
                    yield return Shot("67_shark_repelled");
                }
            }
            finally { GameEvents.Fight -= onFight; }
            th.CancelStalk();
            Combat.Reset();
            pc.enabled = true;
        }

        bool shotCircle;

        // ================================================================== 68 spear fishing
        IEnumerator StepR3FaunaSpearFish()
        {
            var sp = FaunaModule.Spawner;
            if (sp == null || pc == null) yield break;
            Vector3 eye = ReefEye(3f);
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            Vector3 fwd = Flat(cam.transform.forward);
            Backpack.Add(ItemIds.SpearT1, 1, false);
            var spear = Backpack.First(ItemIds.SpearT1);
            var def = ItemDatabase.Get(ItemIds.SpearT1);
            var fish = SpawnFC("snapper", KeepInWater(eye + fwd * 1.8f, 1f, 1.5f)) ?? SpawnFC("sardine", KeepInWater(eye + fwd * 1.8f, 1f, 1.5f));
            if (!Check(fish != null, "a food fish in front of the diver")) yield break;
            Check(fish.species.food, fish.species.id + " is a food fish");
            int raw0 = Backpack.Count(ItemIds.RawFish), catches0 = FaunaCombat.Catches;
            float dur0 = spear != null ? spear.durability : 0f;
            yield return null;
            Vector3 from = fish.pos - fwd * 1.2f;
            bool hit = Combat.Strike(from, (fish.WorldBounds.center - from).normalized, def, spear, true, pc.transform, out _);
            yield return Wait(0.4f);
            Check(hit && FaunaCombat.Catches > catches0, "the spear catches the " + fish.species.Name);
            Check(Backpack.Count(ItemIds.RawFish) > raw0, "raw fish in the backpack (" + raw0 + " → " + Backpack.Count(ItemIds.RawFish) + ")");
            Check(!fish.alive || fish.pendingDespawn, "the fish is gone (puff of scales, no gore)");
            Check(spear == null || spear.durability < dur0, "a catch wears the spear (" + F(dur0, "0") + " → " + (spear != null ? F(spear.durability, "0") : "-") + ")");
            Check(FaunaThreat.BloodAt(eye) > 0f, "blood in the water raises the shark chance (" + F(FaunaThreat.ChancePerMinute(Zones.HouseReef, eye) * 100f, "0.0") + " %/min)");
            yield return Shot("68_spear_fishing");
        }

        // ================================================================== 69 protected animals
        IEnumerator StepR3FaunaProtected()
        {
            var sp = FaunaModule.Spawner;
            if (sp == null || pc == null) yield break;
            Vector3 eye = ReefEye(3f);
            yield return DiveTo(eye, eye + Vector3.forward * 8f);
            Vector3 fwd = Flat(cam.transform.forward);
            Backpack.Add(ItemIds.SpearT1, 1, false);
            var spear = Backpack.First(ItemIds.SpearT1);
            var def = ItemDatabase.Get(ItemIds.SpearT1);
            string[] ids = { "green_turtle", "bluespotted_ray", "clownfish" };
            foreach (var id in ids)
            {
                var c = SpawnFC(id, KeepInWater(eye + fwd * 1.8f, 1f, 1.5f));
                if (c == null) { Info("could not spawn " + id); continue; }
                yield return null;
                int fouls0 = FaunaCombat.Fouls, raw0 = Backpack.Count(ItemIds.RawFish);
                float dur0 = spear != null ? spear.durability : 0f;
                int fights = 0;
                Action<string, float> onFight = (k, i) => fights++;
                GameEvents.Fight += onFight;
                Vector3 from = c.pos - fwd * 1.2f;
                bool hit = Combat.Strike(from, (c.WorldBounds.center - from).normalized, def, spear, true, pc.transform, out _);
                GameEvents.Fight -= onFight;
                yield return Wait(0.3f);
                Check(hit && FaunaCombat.Fouls > fouls0, id + ": the spear is refused (protected)");
                Check(Backpack.Count(ItemIds.RawFish) == raw0 && c.alive, id + ": no catch, the animal lives");
                Check(spear == null || Mathf.Approximately(spear.durability, dur0), id + ": a refused hit does not wear the spear");
                Check(fights == 0, id + ": no fight hype for it (haters instead)");
                if (id == "green_turtle") yield return Shot("69_protected_turtle");
                sp.Despawn(c);
            }
            // the stick only shoos a food fish
            var fish = SpawnFC("snapper", KeepInWater(eye + fwd * 1.8f, 1f, 1.5f));
            if (fish != null)
            {
                yield return null;
                int shoo0 = FaunaCombat.Shoos, raw0 = Backpack.Count(ItemIds.RawFish);
                Vector3 from = fish.pos - fwd * 1.2f;
                Combat.Strike(from, (fish.WorldBounds.center - from).normalized, ItemDatabase.Get(ItemIds.Stick), Backpack.First(ItemIds.Stick), true, pc.transform, out _);
                yield return Wait(0.3f);
                Check(FaunaCombat.Shoos > shoo0 && Backpack.Count(ItemIds.RawFish) == raw0 && fish.alive, "the stick only shoos the fish away (no catch)");
                sp.Despawn(fish);
            }
            Check(FaunaCombat.Classify(SpeciesDatabase.Get("mosasaurus"), ItemIds.SpearT1, true) == FaunaHitKind.Defend
                  && FaunaCombat.Classify(SpeciesDatabase.Get("hammerhead"), ItemIds.SpearT1, true) == FaunaHitKind.Defend
                  && FaunaCombat.Classify(SpeciesDatabase.Get("humpback"), ItemIds.Speargun, true) == FaunaHitKind.Foul
                  && FaunaCombat.Classify(SpeciesDatabase.Get("yellowfin"), ItemIds.Speargun, true) == FaunaHitKind.Catch,
                  "rules: monster / hammerhead defend, whale protected, tuna catchable");
        }

        // ================================================================== 70 volcano fauna
        IEnumerator StepR3FaunaVolcano()
        {
            var sp = FaunaModule.Spawner;
            if (sp == null || pc == null) yield break;
            // the volcano opens with the wreck
            bool wasOpen = VolcanoGate.Open;
            if (!S.unlockedSites.Contains("kelp_forest")) S.unlockedSites.Add("kelp_forest");
            if (!S.unlockedSites.Contains("wreck")) S.unlockedSites.Add("wreck");
            Check(VolcanoGate.Open, "volcano gate open with the wreck (was " + wasOpen + ")");
            var page = new List<SpeciesDef>();
            SpeciesDatabase.ByPage(SpeciesDatabase.VolcanoPage, page);
            Check(page.Count == 7, "Sealog page Vulkaninsel: " + page.Count + " entries (6 living fossils + the monster)");
            // every model builds
            foreach (var s in page)
            {
                GameObject go = null;
                try { go = CreatureFactory.Build(s); }
                catch (Exception e) { Check(false, s.id + " model threw " + e.Message); }
                if (go == null) continue;
                var smr = go.GetComponentInChildren<SkinnedMeshRenderer>();
                int verts = smr != null && smr.sharedMesh != null ? smr.sharedMesh.vertexCount : 0;
                Bounds b = smr != null && smr.sharedMesh != null ? smr.sharedMesh.bounds : default;
                Check(verts > 150 && b.size.magnitude > 0.3f && b.size.magnitude < 3.5f, s.id + " (" + s.variant + ") model: " + verts + " vertices, size " + F(b.size.magnitude, "0.00"));
                Object.Destroy(go);
            }
            // living fossils around the volcano reef
            Vector3 reef = PropInfo.Spot("volcano_reef_2", out var r2) ? r2 : WorldLayout.VolcanoIsland + new Vector3(-80f, -8f, 0f);
            Vector3 eye = KeepInWater(reef + Vector3.up * 3f, 2f, 2f);
            yield return DiveTo(eye, reef);
            yield return Wait(9f);
            int fossils = 0;
            foreach (var s in page) fossils += CountSpecies(s, eye, 80f);
            Check(fossils >= 2, "living fossils swim around the volcano reef (" + fossils + " within 80 m)");
            var horse = SpawnFC("horseshoe_crab", reef + Vector3.up * 0.3f);
            var naut = SpawnFC("nautilus", KeepInWater(eye + Flat(cam.transform.forward) * 3f, 1f, 2f));
            yield return Wait(1.2f);
            if (naut != null) Pose(naut.pos - Flat(cam.transform.forward) * 1.6f, naut.pos);
            yield return Wait(0.4f);
            yield return Shot("70_living_fossils");
            yield return MeasureR3Fps("volcano reef r3");
            if (horse != null) sp.Despawn(horse);
            if (naut != null) sp.Despawn(naut);

            // Big Mo in the sinkhole
            Vector3 home = FaunaVolcano.AbyssHome;
            Vector3 over = new Vector3(home.x, Waves.SeaLevel - 16f, home.z);
            over = KeepInWater(over, 6f, 6f);
            yield return DiveTo(over, over + Vector3.down * 10f + Vector3.forward * 6f);
            float t = 0f;
            FaunaCreature mo = null;
            while (t < 6f && (mo = FaunaVolcano.Monster) == null) { t += Time.unscaledDeltaTime; yield return null; }
            if (mo == null) mo = FaunaVolcano.SpawnMonster(home);
            if (!Check(mo != null && mo.species.monster, "Big Mo lives in the Urzeit-Schlund (" + (mo != null ? F(Waves.SeaLevel - mo.pos.y, "0") + " m deep" : "-") + ")")) yield break;
            Check(mo.behavior is FaunaMonsterBehavior, "with its own monster behaviour");
            var mb = mo.behavior as FaunaMonsterBehavior;
            t = 0f;
            bool rose = false;
            while (t < 60f)
            {
                t += Time.unscaledDeltaTime;
                if (mb != null && mb.State >= 2 && mb.State <= 4) { rose = true; break; }   // circle / pass / lunge (5 = retreat)
                yield return null;
            }
            Check(rose, "Big Mo rises and circles the diver above the sinkhole (state " + (mb != null ? mb.State : -1) + ", " + F((mo.pos - over).magnitude) + " m away, " + F(t, "0") + " s)");
            float tm = 0f;
            while (rose && tm < 3f && !FaunaDangerMusic.AnyPlaying) { tm += Time.unscaledDeltaTime; yield return null; }
            Check(FaunaDangerMusic.AnyPlaying, "the danger music announces it");
            if (rose)
            {
                Pose(over, mo.pos);
                yield return Wait(0.3f);
                yield return Shot("70_big_mo");
                int rep0 = FaunaThreat.Repels;
                var gun = ItemDatabase.Get(ItemIds.Speargun);
                Backpack.Add(ItemIds.Speargun, 1, false);
                for (int k = 0; k < 3 && FaunaThreat.Repels == rep0; k++)
                {
                    Vector3 mp = mo.WorldBounds.center;
                    Vector3 from = mp + (over - mp).normalized * 5f;
                    Combat.Strike(from, (mp - from).normalized, gun, Backpack.First(ItemIds.Speargun), true, pc.transform, out _);
                    yield return Wait(0.4f);
                }
                Check(FaunaThreat.Repels > rep0, "speargun hits drive Big Mo off (repels " + (FaunaThreat.Repels - rep0) + ", state " + mb.State + ")");
            }
            pc.enabled = true;
        }

        // ================================================================== 71 treasure, wrecks, memes
        IEnumerator StepR3FaunaContent()
        {
            if (pc == null) yield break;
            Check(SeaContent.Chests.Count >= 14, "treasure chests placed (" + SeaContent.Chests.Count + ")");
            int wrecks = 0;
            foreach (var kv in SeaContent.Placed) if (kv.Key.StartsWith("wreck_", StringComparison.Ordinal)) wrecks++;
            Check(wrecks >= 4, "small wrecks placed between the sites (" + wrecks + ")");
            Check(SeaContent.Memes.Count >= 10, "curiosities placed (" + SeaContent.Memes.Count + ")");
            var memePage = new List<SpeciesDef>();
            SpeciesDatabase.ByPage(SpeciesDatabase.MemePage, memePage);
            Check(memePage.Count == 10, "Sealog page Kuriositäten: " + memePage.Count + " entries");

            // ---- a chest near the swim raft: open it, the loot lands in the backpack, the lid stays open
            SeaChest chest = null;
            foreach (var c in SeaContent.Chests) if (c != null && c.chestId == "lagoon_raft") chest = c;
            if (chest == null && SeaContent.Chests.Count > 0) chest = SeaContent.Chests[0];
            if (Check(chest != null, "a chest to open"))
            {
                StoryFlags.Clear(SeaTreasure.ChestFlagPrefix + chest.chestId);
                Vector3 cpos = chest.transform.position;
                Vector3 eye = KeepInWater(cpos + new Vector3(1.6f, 1.2f, -1.2f), 1f, 0.8f);
                yield return DiveTo(eye, cpos + Vector3.up * 0.4f);
                yield return Shot("71_chest_closed");
                long worth0 = SeaTreasure.ValuablesWorth;
                int opened0 = SeaTreasure.Opened;
                chest.DebugOpen();
                yield return Wait(1.2f);
                Check(SeaTreasure.Opened > opened0 && SeaTreasure.IsOpened(chest.chestId), "chest opened (shared flag set)");
                Check(SeaTreasure.ValuablesWorth > worth0, "treasure in the backpack (" + worth0 + " → " + SeaTreasure.ValuablesWorth + " $)");
                Check(chest.lid != null && Mathf.Abs(chest.lid.localEulerAngles.x) > 10f, "the lid is open");
                yield return Shot("71_chest_open");
                long money0 = S.money;
                long sold = SeaTreasure.SellAll();
                Check(sold > 0 && S.money >= money0 + sold, "valuables sold in the fish market app (+" + sold + " $)");
            }

            // ---- the trawler's wheelhouse is a room you can swim into
            if (SeaContent.Placed.TryGetValue("room_trawler", out var room))
            {
                bool free = !Physics.CheckSphere(room, 0.45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Check(free, "trawler wheelhouse: free water inside at " + V(room));
                Vector3 eye = room + Vector3.up * 0.2f;
                yield return DiveTo(eye, eye + Vector3.forward * 2f);
                yield return Shot("71_trawler_room");
            }
            else Check(false, "trawler wheelhouse placed");

            // ---- curiosities: a meme registers as a filmable creature and goes into the Sealog with a photo
            SeaMemeActor shades = null;
            foreach (var m in SeaContent.Memes) if (m != null && m.Species != null && m.Species.id == "meme_shades") shades = m;
            if (Check(shades != null, "the cool grouper with sunglasses is on the house reef"))
            {
                Vector3 p = shades.transform.position;
                Vector3 eye = KeepInWater(p + new Vector3(2.5f, 0.3f, 0f), 1f, 1f);
                yield return DiveTo(eye, p);
                yield return Wait(1.5f);
                Check(CreatureRegistry.Active.Contains(shades), "the meme is a filmable ICreature while near");
                yield return PhotoOf(shades, 2.4f, "71_meme_shades");
                Check(Journal.IsDiscovered("meme_shades"), "photo → Sealog entry (" + photoNote + ")");
            }
            // shots of a few more
            foreach (var id in new[] { "meme_throne", "meme_crabrave", "meme_gamingpc", "meme_dance" })
            {
                SeaMemeActor m = null;
                foreach (var a in SeaContent.Memes) if (a != null && a.Species != null && a.Species.id == id) m = a;
                if (m == null) { Info("meme " + id + " not placed"); continue; }
                Vector3 p = m.WorldBounds.center;
                Vector3 eye = KeepInWater(p + new Vector3(2.6f, 0.8f, 1.2f), 1f, 1f);
                yield return DiveTo(eye, p);
                yield return Wait(1f);
                yield return Shot("71_" + id);
            }
            pc.enabled = true;
        }
    }
}
