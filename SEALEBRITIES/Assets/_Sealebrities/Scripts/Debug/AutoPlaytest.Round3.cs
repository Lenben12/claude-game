using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 steps (Testversion 5, GDD §19). They run after the round-2 steps of the full run, or alone with
    ///     SEALEBRITIES.exe -autoplaytest "C:/out" -r3only
    /// (menu → fresh late game → these steps → menu). Core step: backpack + one hotbar, survival, weapons, map pieces,
    /// gallery export, camera rules, weather. Later round-3 agents append their own steps to RunRound3Steps.
    public sealed partial class AutoPlaytest
    {
        static bool Round3Only => Array.IndexOf(Environment.GetCommandLineArgs(), "-r3only") >= 0;

        IEnumerator RunRound3Steps()
        {
            yield return RunStep("50 Backpack + one hotbar (GDD §19): 8 slots, main hand / offhand, stacks, two-handed speargun, drop + pick up, save migration, backpack screen", StepR3Backpack, 120f);
            yield return RunStep("51 Survival: hunger / thirst sink with the clock, low = slower + less air, eat, seawater → filter → drink, campfire + grill cook raw food", StepR3Survival, 150f);
            yield return RunStep("52 Weapons: stick bonk on land, spear thrust under water (durability), block, hurt never deadly, grinder after the hermits' lesson, fight → stream", StepR3Weapons, 150f);
            yield return RunStep("53 Map pieces: 8 placed (lying + buried), dig with the stick, the complete map opens + marks the X, treasure dug up", StepR3MapPieces, 150f);
            yield return RunStep("54 Gallery export + camera rules: no auto-record, LMB records, aim brackets only within photo range, '?' for new species", StepR3GalleryCamera, 150f);
            yield return RunStep("55 Weather app temperatures + per-player backpack in the co-op record + avatar hold bytes", StepR3Weather, 90f);
            // round 3 step 2 (player + characters): AutoPlaytest.Round3Player.cs
            yield return RunStep("56 Sleeping IN the hammock and the bed: root on the pelvis point, first-person eye, the avatar's pose, getting up", StepR3SleepPose, 90f);
            yield return RunStep("57 Dock ladder climbed by the player: Space grabs, no auto-climb, W climbs smoothly, steps off at the top; avatar on the ladder", StepR3DockLadder, 90f);
            yield return RunStep("58 Characters: every round-3 option builds, triangle budget + LOD, line-up / faces / run shots, character screen", StepR3Characters, 120f);
            yield return RunStep("59 Hands + held items: stick swing, wooden / steel spear thrust, two-handed speargun aim, bottle + drinking", StepR3HeldItems, 90f);
            // round 3 step 3 (world): AutoPlaytest.Round3World / Round3Colliders / Round3Sky / Round3Hut
            yield return RunStep("60 World: island layout + anchors, hermit camp hidden (no sign / trail), volcano island zone, swim raft out in the sea, signposts point along their trails, map pieces + campfires + loot placed", StepR3World, 120f);
            yield return RunStep("61 Collisions: reef rocks, palm trunks, rocks, buoys, wreck parts and the helicopter are solid (bushes / grass / kelp passable)", StepR3Colliders, 120f);
            yield return RunStep("62 Sky + water: layered clouds drift and colour at sunset, deep water hides the seabed from above, the lagoon stays turquoise", StepR3SkyWater, 90f);
            yield return RunStep("63 Hut furnishing: interior built, Einrichten choices change the hut, persist in the save and reach co-op guests", StepR3HutDecor, 120f);
            yield return RunStep("64 Islands: every landmark island has its own look, the volcano island (smoke, crater glow, black sand, ferns, spawn anchors for the living fossils) is built and solid", StepR3Islands, 90f);
            // round 3 step 4 (fauna + sea content): AutoPlaytest.Round3Fauna.cs (alone: -r3fauna)
            yield return RunRound3FaunaSteps();
            // round 3 step 5 (NPCs, hermit camp, story, side quests): AutoPlaytest.Round3Npc.cs (alone: -r3npc)
            yield return RunRound3NpcSteps();
            // round 3 step 6 (phone, UI, boat: map, shop, studio, cookbook, loading screen, camera HUD, bench): AutoPlaytest.Round3UI*.cs (alone: -r3ui)
            yield return RunRound3UISteps();
            // round 3 step 6b (save slots + invites, GDD §19b): AutoPlaytest.Round3Slots.cs (alone: -r3slots)
            yield return RunRound3SlotSteps();
        }

        /// A test target for the weapons (counts hits, keeps the last hit).
        public sealed class R3HitDummy : MonoBehaviour, IHittable
        {
            public int hits;
            public HitInfo last;
            public bool CanBeHit => isActiveAndEnabled;
            public void OnHit(in HitInfo hit) { hits++; last = hit; }
        }

        R3HitDummy MakeDummy(Vector3 center)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "R3HitDummy";
            go.transform.position = center;
            go.transform.localScale = new Vector3(0.8f, 1.4f, 0.8f);
            return go.AddComponent<R3HitDummy>();
        }

        /// A standing spot next to a prop (free capsule, ground within 1.5 m of the prop's base).
        Vector3 SpotNear(Vector3 target, float dist)
        {
            for (int k = 0; k < 16; k++)
            {
                float a = k * 22.5f * Mathf.Deg2Rad;
                Vector3 f = SnapDown(target + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * dist + Vector3.up * 2f, 2.5f, 6f);
                if (Mathf.Abs(f.y - target.y) > 1.6f) continue;
                if (Physics.CheckCapsule(f + Vector3.up * 0.4f, f + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                return f;
            }
            return SnapDown(target + new Vector3(dist, 2f, 0f));
        }

        /// Starts a timed prop action (PropHold) and waits until it finished (the player stands next to it).
        IEnumerator HoldAt(PropHold prop, float maxSeconds)
        {
            if (prop == null) yield break;
            Vector3 p = prop.transform.position;
            Vector3 feet = SpotNear(new Vector3(p.x, Mathf.Min(p.y, WorldInfo.GroundHeight(p) + 0.5f), p.z), 1.4f);
            yield return Stand(feet, YawTo(feet, p));
            yield return AimAt(p + Vector3.up * 0.3f);
            prop.Interact();
            yield return Wait(0.2f);
            float t = 0f;
            while (prop.Running && t < maxSeconds) { t += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(0.2f);
        }

        // ================================================================== 50 backpack + hotbar
        IEnumerator StepR3Backpack()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            Vector3 hut = WorldInfo.PlayerSpawn != Vector3.zero ? WorldInfo.PlayerSpawn : WorldLayout.PlayerSpawn;
            yield return Stand(SnapDown(hut + Vector3.up * 2f), 90f);
            Hands.StowAll();
            Check(Hands.SlotCount == 8 && GameInput.SlotAction(7) == GameAction.Slot8, "one hotbar row of 8 slots (keys " + GameInput.BindingLabel(GameInput.SlotAction(0)) + "–" + GameInput.BindingLabel(GameInput.SlotAction(7)) + ")");
            Check(Backpack.Has(ItemIds.Stick), "the stick is in the backpack from the start");

            // stacks
            int stone0 = Backpack.Count(ItemIds.Stone);
            Backpack.Add(ItemIds.Stone, 3);
            Backpack.Add(ItemIds.Wood, 2);
            Backpack.Add(FoodDatabase.Coconut, 2);
            Check(Backpack.Count(ItemIds.Stone) == stone0 + 3 && Backpack.Count(ItemIds.Wood) >= 2 && Backpack.Count(FoodDatabase.Coconut) >= 2,
                  "picked-up things are in the backpack: stones " + Backpack.Count(ItemIds.Stone) + ", wood " + Backpack.Count(ItemIds.Wood) + ", coconuts " + Backpack.Count(FoodDatabase.Coconut) + " (" + Backpack.UsedSlots + "/" + Backpack.Capacity + " slots)");
            int room = Backpack.SpaceFor(ItemIds.Stone);
            int added = Backpack.Add(ItemIds.Stone, room + 5, false);
            Check(added == room && Backpack.UsedSlots <= Backpack.Capacity, "a full backpack refuses the rest (" + added + " of " + (room + 5) + " stones fit, " + Backpack.UsedSlots + "/" + Backpack.Capacity + ")");
            Backpack.Remove(ItemIds.Stone, added);

            // main hand: key 3 = stick
            GameInput.SimTap(GameInput.SlotAction(2));
            yield return Wait(0.6f);
            Check(Hands.Active(Hand.Right) == HoldItem.Stick, "key 3: the stick in the main hand (" + Hands.Active(Hand.Right) + ")");
            // offhand: coconut on slot 6, marked for the offhand
            Hands.Assign(5, FoodDatabase.Coconut, Hand.Left);
            GameInput.SimTap(GameInput.SlotAction(5));
            yield return Wait(0.6f);
            Check(Hands.Active(Hand.Left) == HoldItem.Food && Hands.Active(Hand.Right) == HoldItem.Stick, "key 6: coconut in the offhand, the stick stays in the main hand");
            yield return Shot("50_hands_stick_coconut");
            // eat from the hand: LMB acts with the offhand only when the main hand is empty → empty it first
            Hands.Stow(Hand.Right);
            Survival.Set(60f, 40f);
            int coco0 = Backpack.Count(FoodDatabase.Coconut);
            yield return Wait(0.7f);
            GameInput.SimTap(GameAction.Use);
            yield return Wait(0.8f);
            Check(Backpack.Count(FoodDatabase.Coconut) == coco0 - 1 && Survival.Thirst > 70f, "LMB drinks the held coconut (left " + Backpack.Count(FoodDatabase.Coconut) + ", thirst " + F(Survival.Thirst, "0") + ")");
            // right-click logic: the same slot into the main hand
            Hands.SetSlotHand(5, Hand.Right);
            Check(Hands.SlotHand(5) == Hand.Right, "a slot can be switched between offhand and main hand");

            // two-handed speargun
            Backpack.Add(ItemIds.Speargun, 1, false);
            Hands.Assign(6, ItemIds.Speargun);
            GameInput.SimTap(GameInput.SlotAction(3));   // lamp in the offhand first
            yield return Wait(0.5f);
            GameInput.SimTap(GameInput.SlotAction(6));
            yield return Wait(0.6f);
            Check(Hands.Active(Hand.Right) == HoldItem.SpearGun && Hands.Active(Hand.Left) == HoldItem.None, "the speargun is two-handed: the lamp was put away");
            Hands.Swap(0, 6);
            Check(Hands.SlotItem(0) == HoldItem.SpearGun && Hands.SlotItem(6) == HoldItem.Camera, "two hotbar slots swapped");
            Hands.Swap(0, 6);
            Hands.StowAll();
            // the used-up stack: dimmed slot, the hand empties
            Backpack.Remove(ItemIds.Speargun, 1);
            Check(!Hands.SlotUsable(6), "a stack that ran out leaves its slot dimmed");

            // drop + pick up again
            var st = Backpack.First(ItemIds.Wood);
            int wood0 = Backpack.Count(ItemIds.Wood);
            Check(st != null && Backpack.Drop(Backpack.IndexOf(st), false), "dropped one driftwood");
            yield return Wait(0.8f);
            PropLoot dropped = null;
            foreach (var l in Object.FindObjectsByType<PropLoot>(FindObjectsSortMode.None)) if (l != null && l.dropped != null && l.itemId == ItemIds.Wood) dropped = l;
            Check(dropped != null && Backpack.Count(ItemIds.Wood) == wood0 - 1, "the driftwood lies in front of the player (" + (dropped != null ? V(dropped.transform.position) : "none") + ")");
            if (dropped != null)
            {
                dropped.Interact();
                yield return Wait(0.3f);
                Check(Backpack.Count(ItemIds.Wood) == wood0, "picked it up again (" + Backpack.Count(ItemIds.Wood) + ")");
            }

            // the backpack screen
            UIModule.DebugOpenPhone("inventory");
            yield return Wait(0.9f);
            Check(UIState.InventoryOpen, "I opens the backpack");
            yield return Shot("50_backpack_screen");
            UIModule.DebugOpenPhone("inventory:close");
            yield return Wait(0.3f);
            Hands.ResetToDefaults();

            // save migration: a round-2 save (v4) with the 3 + 3 hotbar and collected things
            string old = "{\"version\":4,\"saveSeed\":77,\"day\":3,\"hotbarRight\":[\"Camera\",\"None\",\"FishingRod\"],\"hotbarLeft\":[\"Flashlight\",\"None\",\"None\"],"
                         + "\"collected\":[\"shell:s01\",\"shell:s02\",\"log:p01\",\"treasure:bay_chest\"],\"ownedItems\":[\"cam_t0\",\"lamp_t1\",\"rod_t0\"]}";
            GameState mig = null;
            try { mig = JsonUtility.FromJson<GameState>(old); mig.EnsureValid(); }
            catch (Exception e) { Check(false, "migration threw " + e.Message); }
            if (mig != null)
            {
                Check(mig.version == GameState.CurrentVersion && mig.hotbar != null && mig.hotbar.Length == 8, "v4 save migrated to v" + mig.version + " with an 8-slot hotbar");
                Check(mig.hotbar[0] == ItemIds.Camera && mig.hotbar[2] == ItemIds.FishingRod && mig.hotbar[3] == ItemIds.Flashlight && (mig.hotbarOffMask & (1 << 3)) != 0,
                      "old keys kept: 1 camera, 3 rod, 4 lamp (offhand) – " + string.Join(",", mig.hotbar));
                bool stickOnBar = Array.IndexOf(mig.hotbar, ItemIds.Stick) >= 0;
                int shells = 0, pages = 0;
                foreach (var p in mig.pouch) { if (p == null) continue; if (p.id == ItemIds.Shell) shells = p.count; if (p.id == ItemIds.Page) pages = p.count; }
                bool stickInBag = false;
                foreach (var b in mig.backpack) if (b != null && b.id == ItemIds.Stick) stickInBag = true;
                Check(stickOnBar && stickInBag && mig.backpackInit, "the stick was added (hotbar + backpack)");
                Check(shells == 2 && pages == 1, "old finds are in the collection pouch (shells " + shells + ", pages " + pages + ")");
            }
        }

        // ================================================================== 51 survival
        IEnumerator StepR3Survival()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            // ---- the meters sink with the in-game clock
            Survival.Set(100f, 100f);
            float hours = 6f;
            float dt = hours / 24f * GameManager.DayLengthSeconds;
            Survival.Tick(dt);   // as if 6 in-game hours passed on land
            float rate = S.difficulty == Difficulty.Snorkeler ? 0.6f : S.difficulty == Difficulty.OpenWater ? 1f : 1.25f;
            float expH = 100f - hours * 100f / Survival.HungerHours * rate, expT = 100f - hours * 100f / Survival.ThirstHours * rate;
            Check(Mathf.Abs(Survival.Hunger - expH) < 1.5f && Mathf.Abs(Survival.Thirst - expT) < 1.5f,
                  "6 in-game hours: hunger " + F(Survival.Hunger, "0") + " (≈" + F(expH, "0") + "), thirst " + F(Survival.Thirst, "0") + " (≈" + F(expT, "0") + ")");
            // ---- low = slower + less air, never 0
            Survival.Set(4f, 3f);
            yield return Frames(2);
            Check(Survival.SpeedFactor < 0.9f && Survival.SpeedFactor >= Survival.MinFactor && Survival.AirFactor < 0.95f && Survival.AirFactor >= Survival.MinFactor,
                  "starving + parched: speed ×" + F(Survival.SpeedFactor, "0.00") + ", air ×" + F(Survival.AirFactor, "0.00") + " (gentle, never below ×" + F(Survival.MinFactor, "0.0") + ")");
            Check(Buffs.Multiplier(BuffType.AirCapacity) < 0.97f && Buffs.Multiplier(BuffType.SwimSpeed) < 0.97f, "the penalty reaches the player's air capacity and swim speed");
            Check(!pc.IsFainting, "nobody faints from hunger");
            yield return Shot("51_hud_hungry_thirsty");
            // ---- eat a snack
            Backpack.Add("krill_bar", 1, false);
            float h0 = Survival.Hunger;
            var r = Backpack.UseId("krill_bar");
            Check(r == UseResult.Eaten && Survival.Hunger > h0 + 25f, "ate a krill bar: hunger " + F(h0, "0") + " → " + F(Survival.Hunger, "0"));
            // raw food is refused
            Backpack.Add(ItemIds.RawFish, 1, false);
            Check(Backpack.UseId(ItemIds.RawFish) == UseResult.NeedsCooking && Backpack.Count(ItemIds.RawFish) >= 1, "raw fish must be cooked first");

            // ---- seawater: fill a bottle at the dock end
            if (!Backpack.Has(ItemIds.BottleEmpty)) Backpack.Add(ItemIds.BottleEmpty, 1, false);
            Vector3 dock = DockEnd();
            Vector3 dockFeet = SnapDown(dock + new Vector3(-0.5f, 0.4f, 0f));
            yield return Stand(dockFeet, 90f);
            yield return AimAt(dockFeet + new Vector3(4f, -1.5f, 0f));
            int sea0 = Backpack.Count(ItemIds.BottleSea);
            Check(Survival.NearSeaWater(), "at the dock end the sea is in reach");
            Check(Backpack.UseId(ItemIds.BottleEmpty) == UseResult.Filled && Backpack.Count(ItemIds.BottleSea) == sea0 + 1, "empty bottle filled with seawater");
            Check(Backpack.UseId(ItemIds.BottleSea) == UseResult.NeedsFilter, "seawater is too salty to drink");

            // ---- the hut's water filter
            var filter = Object.FindObjectsByType<PropWaterFilter>(FindObjectsSortMode.None);
            Check(filter.Length > 0, filter.Length + " water filter in the hut");
            if (filter.Length > 0)
            {
                yield return HoldAt(filter[0], 4f);
                Check(Survival.FilterCount >= 1 && Backpack.Count(ItemIds.BottleSea) == 0, "seawater put into the filter (" + Survival.FilterCount + " bottle, " + F(Survival.FilterSecondsLeft, "0") + " s left)");
                yield return Shot("51_water_filter");
                S.timeOfDay = Mathf.Min(0.97f, S.timeOfDay + (Survival.FilterMinutes + 5f) / 1440f);
                yield return Frames(2);
                Check(Survival.FilterReadyCount >= 1, "after " + Survival.FilterMinutes + " in-game minutes the water is ready");
                int fresh0 = Backpack.Count(ItemIds.BottleFresh);
                yield return HoldAt(filter[0], 4f);
                Check(Backpack.Count(ItemIds.BottleFresh) == fresh0 + 1, "fresh water taken out (" + Backpack.Count(ItemIds.BottleFresh) + ")");
                Survival.Set(50f, 20f);
                int empty0 = Backpack.Count(ItemIds.BottleEmpty);
                Check(Backpack.UseId(ItemIds.BottleFresh) == UseResult.Drunk && Survival.Thirst > 60f && Backpack.Count(ItemIds.BottleEmpty) == empty0 + 1,
                      "drank the fresh water: thirst " + F(Survival.Thirst, "0") + ", the empty bottle is back");
            }

            // ---- a campfire spot: build it with driftwood, cook raw fish
            var fires = Object.FindObjectsByType<PropCampfire>(FindObjectsSortMode.None);
            Check(fires.Length >= 3, fires.Length + " campfire spots");
            PropCampfire fire = null;
            float best = float.MaxValue;
            foreach (var f in fires) { float d = FlatDist(f.transform.position, WorldLayout.Hut); if (d < best) { best = d; fire = f; } }
            if (fire != null)
            {
                Backpack.Add(ItemIds.Wood, Survival.CampfireWood, false);
                int wood0 = Backpack.Count(ItemIds.Wood);
                yield return HoldAt(fire, 4f);
                Check(Survival.CampfireLit(fire.id) && Backpack.Count(ItemIds.Wood) == wood0 - Survival.CampfireWood,
                      "campfire '" + fire.id + "' built with " + Survival.CampfireWood + " driftwood, burns " + F(Survival.CampfireHoursLeft(fire.id), "0.0") + " h");
                // the fire cooks the FIRST raw item of the backpack – in the late-game run that may be meat or a second fish stack
                int raw0 = Survival.RawCount, cooked0 = Backpack.Count(FoodDatabase.GrilledFish) + Backpack.Count("grilled_meat");
                yield return HoldAt(fire, 7f);
                int cooked1 = Backpack.Count(FoodDatabase.GrilledFish) + Backpack.Count("grilled_meat");
                Check(Survival.RawCount == raw0 - 1 && cooked1 == cooked0 + 1, "raw food cooked at the campfire → grilled food in the backpack (raw " + raw0 + " → " + Survival.RawCount + ", grilled " + cooked0 + " → " + cooked1 + ")");
                yield return Shot("51_campfire");
            }
            // ---- the grill at the hut cooks raw meat
            PropGrill grill = null;
            best = float.MaxValue;
            foreach (var g in Object.FindObjectsByType<PropGrill>(FindObjectsSortMode.None)) { float d = FlatDist(g.transform.position, WorldLayout.Hut); if (d < best) { best = d; grill = g; } }
            if (grill != null)
            {
                Backpack.Add(ItemIds.RawMeat, 1, false);
                int meat0 = Backpack.Count(FoodDatabase.GrilledMeat);
                yield return HoldAt(grill, 8f);
                Check(Backpack.Count(FoodDatabase.GrilledMeat) == meat0 + 1, "raw meat cooked on the grill (" + Backpack.Count(FoodDatabase.GrilledMeat) + ")");
                float hh = Survival.Hunger;
                Backpack.UseId(FoodDatabase.GrilledMeat);
                Check(Survival.Hunger > hh + 30f, "roast meat fills the stomach (" + F(hh, "0") + " → " + F(Survival.Hunger, "0") + ")");
            }
            else Info("no grill found");
            Survival.Set(100f, 100f);
        }

        // ================================================================== 52 weapons
        IEnumerator StepR3Weapons()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            int fights = 0;
            Action<string, float> onFight = (k, i) => fights++;
            GameEvents.Fight += onFight;
            R3HitDummy dummy = null;
            try
            {
                // ---- the stick on land
                Vector3 hut = WorldInfo.PlayerSpawn != Vector3.zero ? WorldInfo.PlayerSpawn : WorldLayout.PlayerSpawn;
                Vector3 beach = SnapDown(WorldLayout.DockStart + new Vector3(-6f, 3f, -8f));
                yield return Stand(beach, 0f);
                Vector3 fwd = Flat(cam.transform.forward);
                dummy = MakeDummy(pc.Root.position + fwd * 1.5f + Vector3.up * 0.9f);
                Hands.Hold(ItemIds.Stick, Hand.Right);
                yield return Wait(0.4f);
                yield return AimAt(dummy.transform.position);
                GameInput.SimTap(GameAction.Use);
                yield return Wait(0.5f);
                Check(dummy.hits == 1 && dummy.last.knockback > 0f && dummy.last.stun > 0f && dummy.last.weaponId == ItemIds.Stick,
                      "stick bonk on land hits the dummy (hits " + dummy.hits + ", knockback " + F(dummy.last.knockback) + ", stun " + F(dummy.last.stun) + " s, damage " + F(dummy.last.damage) + ")");
                yield return Shot("52_stick_bonk");
                // ---- block + hurt (never deadly)
                Combat.Reset();
                GameInput.SimPress(GameAction.Aim);
                yield return Wait(0.3f);
                Check(Combat.IsBlocking, "RMB with a weapon = block");
                float taken = Combat.HurtLocal(20f, dummy.transform.position, "test_shark");
                GameInput.SimRelease(GameAction.Aim);
                Check(taken < 20f && taken > 0f, "blocked: 20 → " + F(taken) + " damage (defense " + F(ItemDatabase.Get(ItemIds.Stick).defense * 100f, "0") + " %)");
                yield return Wait(0.3f);
                Combat.HurtLocal(500f, dummy.transform.position, "test_monster");
                yield return Wait(0.5f);
                Check(Combat.Health > 0f && !pc.IsFainting, "a huge hit only knocks the diver back (health " + F(Combat.Health, "0") + ", never deadly)");
                Object.Destroy(dummy.gameObject);
                dummy = null;

                // ---- a spear under water
                var site = DiveSites.Get("house_reef");
                Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -7.5f);
                Vector3 eye = KeepInWater(floor + Vector3.up * 3f, 2.5f, 2f);
                yield return DiveTo(eye, eye + Vector3.forward * 10f);
                Backpack.Add(ItemIds.SpearT1, 1, false);
                var spear = Backpack.First(ItemIds.SpearT1);
                float dur0 = spear != null ? spear.durability : 0f;
                Hands.Hold(ItemIds.SpearT1, Hand.Right);
                yield return Wait(0.4f);
                eye = cam.transform.position;
                fwd = Flat(cam.transform.forward);
                dummy = MakeDummy(KeepInWater(eye + fwd * 1.8f, 1f, 0.5f));
                Pose(eye, dummy.transform.position);
                yield return Wait(0.3f);
                GameInput.SimTap(GameAction.Use);
                yield return Wait(0.5f);
                Check(dummy.hits == 1 && dummy.last.underwater, "spear thrust under water hits (" + dummy.hits + ", damage " + F(dummy.last.damage) + ")");
                Check(spear != null && spear.durability == dur0 - 1f, "durability " + F(dur0, "0") + " → " + (spear != null ? F(spear.durability, "0") : "-"));
                yield return Shot("52_spear_underwater");
                Object.Destroy(dummy.gameObject);
                dummy = null;
                pc.enabled = true;
                Check(fights >= 2, "fights reach the livestream hook (GameEvents.Fight × " + fights + ")");

                // ---- grinder: only after the hermits' lesson
                Hands.StowAll();
                StoryFlags.Clear(StoryKeys.GrindLearned);
                Check(Combat.SharpenBlocker() != null && !Combat.GrindLearned, "grinding is locked until the hermits teach it ('" + Combat.SharpenBlocker() + "')");
                StoryFlags.Set(StoryKeys.GrindLearned);   // the npc step sets this in the hermit camp
                Backpack.Add(ItemIds.Stone, 1, false);
                float sharp0 = spear != null ? spear.sharpness : 0f;
                var grinder = Object.FindObjectsByType<PropGrinder>(FindObjectsSortMode.None);
                Check(grinder.Length > 0, grinder.Length + " grinder in the hut");
                if (grinder.Length > 0) yield return HoldAt(grinder[0], 5f);
                else Combat.Sharpen();
                Check(spear != null && spear.sharpness > sharp0 + 0.2f, "spear sharpened: " + F(sharp0 * 100f, "0") + " % → " + (spear != null ? F(spear.sharpness * 100f, "0") : "-") + " %");
            }
            finally
            {
                GameEvents.Fight -= onFight;
                if (dummy != null) Object.Destroy(dummy.gameObject);
                Combat.Reset();
            }
        }

        // ================================================================== 53 map pieces
        IEnumerator StepR3MapPieces()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            var lying = Object.FindObjectsByType<PropMapPiece>(FindObjectsSortMode.None);
            var buried = Object.FindObjectsByType<PropDigSpot>(FindObjectsSortMode.None);
            Check(lying.Length + buried.Length == MapPieces.Count && buried.Length > 0 && lying.Length > 0,
                  MapPieces.Count + " map pieces placed: " + lying.Length + " lying, " + buried.Length + " buried");
            Check(Collectibles.Total(CollectibleKind.MapPiece) == MapPieces.Count, "registered as collectibles (" + Collectibles.Total(CollectibleKind.MapPiece) + ")");
            int found0 = MapPieces.Found, bag0 = Backpack.Count(ItemIds.MapPiece);
            if (lying.Length > 0)
            {
                var piece = lying[0];
                Vector3 feet = SpotNear(piece.transform.position, 1.2f);
                yield return Stand(feet, YawTo(feet, piece.transform.position));
                yield return AimAt(piece.transform.position);
                Info("prompt at the map piece: '" + InteractionPrompt.Text + "'");
                piece.Interact();
                yield return Wait(0.4f);
                Check(MapPieces.Found == found0 + 1 && Backpack.Count(ItemIds.MapPiece) == bag0 + 1, "picked up a map piece (" + MapPieces.Found + "/" + MapPieces.Count + ", backpack " + Backpack.Count(ItemIds.MapPiece) + ")");
            }
            if (buried.Length > 0)
            {
                var spot = buried[0];
                Hands.Hold(ItemIds.Stick, Hand.Right);
                int f0 = MapPieces.Found;
                float t0 = Time.realtimeSinceStartup;
                yield return HoldAt(spot, 8f);
                Check(MapPieces.Found == f0 + 1, "dug up a buried piece with the stick in " + F(Time.realtimeSinceStartup - t0, "0.0") + " s");
                yield return Shot("53_dig_spot");
            }
            // the rest + the complete map
            for (int i = 0; i < MapPieces.Count; i++) MapPieces.Collect(i);
            yield return Wait(0.3f);
            Check(MapPieces.Complete && StoryFlags.Has(StoryKeys.TreasureMap), "all pieces → the treasure map is complete");
            UIWaypoint.Clear();
            MapPieces.OpenMap();
            yield return Wait(0.9f);
            Check(UIState.InventoryOpen, "the map opens in the backpack (collection tab)");
            yield return Shot("53_treasure_map");
            UIModule.DebugOpenPhone("inventory:close");
            Check(MapPieces.MarkSecret() && UIWaypoint.Active && FlatDist(UIWaypoint.Position, MapPieces.SecretSpot) < 1f, "the X is marked on the HUD (" + V(MapPieces.SecretSpot) + ")");
            // the treasure
            var dig = Object.FindObjectsByType<PropTreasureDig>(FindObjectsSortMode.None);
            Check(dig.Length == 1, "a treasure spot at the X");
            if (dig.Length > 0)
            {
                long m0 = S.money;
                yield return HoldAt(dig[0], 9f);
                Check(StoryFlags.Has(StoryKeys.TreasureFound) && S.money >= m0 + PropTreasureDig.Reward, "treasure dug up (+" + (S.money - m0) + " $)");
            }
            Hands.StowAll();
        }

        // ================================================================== 54 gallery export + camera rules
        IEnumerator StepR3GalleryCamera()
        {
            if (pc == null) yield break;
            pc.enabled = true;
            // ---- gallery: inside the game, export into the (test) Pictures folder
            string export = Path.Combine(outDir, "export_pictures");
            Gallery.ExportFolderOverride = export;
            try
            {
                Check(Gallery.Folder.StartsWith(Application.persistentDataPath, StringComparison.OrdinalIgnoreCase), "the photos live in the game's data folder (" + Gallery.Folder + ")");
                var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
                var px = new Color32[256];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(40, (byte)(i % 255), 200, 255);
                tex.SetPixels32(px);
                tex.Apply();
                var entry = Gallery.Add(tex, null, null);
                Object.Destroy(tex);
                Check(entry != null && Gallery.Exists(entry), "a photo was added to the gallery");
                if (entry != null)
                {
                    string path = Gallery.Export(entry);
                    Check(path != null && File.Exists(path) && path.StartsWith(export, StringComparison.OrdinalIgnoreCase), "EXPORT copied it out (" + (path ?? "-") + ")");
                    Check(Gallery.Exists(entry), "the game keeps its own copy");
                    try { if (path != null && File.Exists(path)) File.Delete(path); } catch (Exception) { }
                }
                UIModule.DebugOpenPhone("gallery");
                yield return Wait(0.8f);
                yield return Shot("54_gallery_export");
                UIModule.DebugOpenPhone("close");
            }
            finally { Gallery.ExportFolderOverride = null; }

            // ---- camera: no auto-record in the water, LMB records
            var hc = Refs.HandCamera as FilmHandCamera;
            if (hc == null) { Check(false, "no hand camera"); yield break; }
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -11f, -7.5f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 3f, 2.5f, 2f);
            Hands.StowAll();
            if (Hands.Find(HoldItem.Camera, out int cs)) Hands.Select(cs);
            yield return DiveTo(eye, eye + Vector3.forward * 10f);
            if (!Hands.IsHeld(HoldItem.Camera) && Hands.Find(HoldItem.Camera, out cs)) Hands.Select(cs);
            yield return Wait(1.6f);
            Check(Hands.IsHeld(HoldItem.Camera) && !hc.IsRecording, "camera in hand under water, nothing records by itself (" + hc.DebugSummary() + ")");
            GameInput.SimTap(GameAction.Use);
            yield return WaitUntil(() => hc.IsRecording, 2f);
            Check(hc.IsRecording && !hc.AutoRecording, "LMB records");
            hc.StopRecording();

            // ---- aim brackets only within the photo range; "?" for unknown species
            var spawner = FaunaModule.Spawner;
            if (spawner != null) spawner.DespawnAll();
            eye = cam.transform.position;
            Vector3 fwd = Flat(cam.transform.forward);
            string farId = "barracuda", nearId = "parrotfish";
            var far = SpawnAt(farId, KeepInWater(eye + fwd * 13f, 1.2f, 1.5f), SizeTier.Normal);
            GameInput.SimPress(GameAction.Aim);
            float t = 0f;
            int farDetected = 0, farBrackets = 0;
            while (t < 1.2f)
            {
                if (far != null && far.IsAlive) Pose(eye, far.Position);
                farDetected = Mathf.Max(farDetected, ViewfinderInfo.DetectedCount);
                if (t > 0.5f) farBrackets = Mathf.Max(farBrackets, ViewfinderInfo.BracketCount);
                yield return null;
                t += Time.deltaTime;
            }
            if (farDetected > 0) Check(farBrackets == 0, "an animal 13 m away is detected but gets NO aim frame (detected " + farDetected + ", brackets " + farBrackets + ")");
            else Info("the far animal was not detected at all (visibility) – no frame either");
            if (far != null && far.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(far);
            var near = SpawnAt(nearId, KeepInWater(eye + fwd * 2.4f, 1.2f, 1.5f), SizeTier.Large);
            t = 0f;
            int nearBrackets = 0;
            bool unknownMark = false;
            while (t < 1.2f)
            {
                if (near != null && near.IsAlive) Pose(eye, near.Position);
                nearBrackets = Mathf.Max(nearBrackets, ViewfinderInfo.BracketCount);
                for (int i = 0; i < ViewfinderInfo.BracketCount; i++) if (ViewfinderInfo.BracketNew[i]) unknownMark = true;
                yield return null;
                t += Time.deltaTime;
            }
            Check(nearBrackets >= 1, "a close animal gets its aim frame (" + nearBrackets + ")");
            if (!Journal.IsDiscovered(nearId)) Check(unknownMark, "an undiscovered species is marked with '?'");
            yield return Shot("54_bracket_close");
            GameInput.SimRelease(GameAction.Aim);
            if (near != null && near.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(near);
            pc.enabled = true;
        }

        // ================================================================== 55 weather + co-op data
        IEnumerator StepR3Weather()
        {
            int day = S.day;
            float morning = Weather.AirTemp(day, 5f / 24f), afternoon = Weather.AirTemp(day, 14f / 24f);
            Check(afternoon > morning + 2f && morning > 18f && afternoon < 36f, "air: " + Weather.Format(morning) + " at 05:00, " + Weather.Format(afternoon) + " at 14:00");
            float w0 = Weather.WaterTemp(0f), w40 = Weather.WaterTemp(40f), w100 = Weather.WaterTemp(100f);
            Check(w0 > 25f && w40 < w0 && w100 < w40, "water: " + Weather.Format(w0) + " surface, " + Weather.Format(w40) + " at 40 m, " + Weather.Format(w100) + " at 100 m");
            Check(Weather.KindOf(day + 3) == Weather.KindOf(day + 3) && Weather.KindOf(1) == WeatherKind.Sunny, "the forecast is seeded (same on every co-op machine); today " + Weather.Name(Weather.Today) + ", tomorrow " + Weather.Name(Weather.Tomorrow));
            UIModule.DebugOpenPhone("weather");
            yield return Wait(0.9f);
            yield return Shot("55_weather_app");
            UIModule.DebugOpenPhone("close");
            // per-player data in the host save (F.3) and the avatar bytes
            var rec = PlayerRecords.CaptureLocal();
            bool stick = false;
            if (rec != null && rec.backpack != null) foreach (var b in rec.backpack) if (b != null && b.id == ItemIds.Stick) stick = true;
            Check(rec != null && rec.backpackInit && stick && rec.hotbar != null && rec.hotbar.Length == Hands.SlotCount,
                  "the co-op record carries the hotbar, backpack and meters (hunger " + (rec != null ? F(rec.hunger, "0") : "-") + ")");
            string json = rec != null ? JsonUtility.ToJson(rec) : null;
            var back = json != null ? JsonUtility.FromJson<PlayerRecord>(json) : null;
            Check(back != null && back.backpack != null && back.backpack.Count == rec.backpack.Count, "the record survives JSON (" + (back != null && back.backpack != null ? back.backpack.Count : 0) + " stacks)");
            Check((byte)HoldItem.SpearGun <= (byte)Hands.LastItem && (byte)HoldItem.Tool == (byte)Hands.LastItem, "new held items fit the avatar state bytes (≤ " + (byte)Hands.LastItem + ")");
        }
    }
}
