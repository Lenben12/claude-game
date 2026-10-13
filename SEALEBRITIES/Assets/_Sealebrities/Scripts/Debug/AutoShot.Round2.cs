using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Round-2 screenshots (Design_Runde2 J.6 + the third player message): character screen, crew close-up, running
    /// avatar, first-person ladder, selfie at the reef, tide pools, dolphins, Waterfall Grotto, hidden bay, village
    /// Altriff, Wanda's hangar, quest line + notifications, wreck interior and the Fog Isle. Files: r2_*.png.
    public sealed partial class AutoShot
    {
        readonly List<GameObject> r2Temp = new List<GameObject>();

        IEnumerator ShotsRound2()
        {
            yield return Guard(ShotCharacterScreen(), "r2_16_character");
            yield return Guard(ShotCrewCloseup(), "r2_19_crew_closeup");
            yield return Guard(ShotRunAvatar(), "r2_22_run_avatar");
            yield return Guard(ShotLadderFP(), "r2_21_ladder_fp");
            yield return Guard(ShotSelfieReef(), "r2_24_selfie_reef");
            yield return Guard(ShotTidepools(), "r2_17_tidepools");
            yield return Guard(ShotDolphins(), "r2_18_dolphins");
            yield return Guard(ShotCave(), "r2_13_cave");
            yield return Guard(ShotBay(), "r2_14_bay");
            yield return Guard(ShotVillage(), "r2_23_village");
            yield return Guard(ShotHangar(), "r2_27_hangar");
            yield return Guard(ShotQuestLine(), "r2_25_quest_line");
            yield return Guard(ShotWreckInterior(), "r2_28_wreck_interior");
            yield return Guard(ShotFogIsle(), "r2_26_fog_isle");
            yield return Guard(ShotLagoonPatch(), "20_lagoon_patch");
            ClearTemp();
            if (pc != null) pc.enabled = true;
        }

        void ClearTemp()
        {
            for (int i = 0; i < r2Temp.Count; i++) if (r2Temp[i] != null) Destroy(r2Temp[i]);
            r2Temp.Clear();
        }

        DiverPuppet BuildDiver(DiverLook look, Vector3 feet, float yaw)
        {
            var build = DiverModels.Build;
            if (build == null) { Note("WARNING: no DiverModels.Build"); return null; }
            var go = build(look);
            if (go == null) return null;
            go.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            if (Refs.WorldRoot != null) go.transform.SetParent(Refs.WorldRoot, true);
            r2Temp.Add(go);
            return go.GetComponent<DiverPuppet>();
        }

        static DiverLook Look(Color suit, int hat, int pattern, int eyes, int brows, int skin, int mouth)
        {
            return new DiverLook
            {
                suit = suit, cuff = new Color(0f, 0f, 0f, 0f), hat = hat, pattern = pattern, eyes = eyes, brows = brows, skin = skin, mouth = mouth,
                snorkel = true, lookVersion = DiverLook.CurrentVersion, tankTier = 2, finTier = 1
            };
        }

        // r2_16 – the character screen (1c) with the live 3D preview: party hat, then the captain's cap
        IEnumerator ShotCharacterScreen()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            if (!UIModule.DebugOpenCharacter(8)) { Note("WARNING: character screen did not open"); yield break; }
            yield return Wait(2.2f);
            yield return Capture("r2_16_character");
            UIModule.DebugOpenCharacter(13);
            yield return Wait(1.4f);
            yield return Capture("r2_16b_character_straw_hat");
            UIModule.DebugOpenCharacter(-2);
            yield return Wait(0.6f);
        }

        // r2_19 – four bean divers on the dock at 2 m: rounder bodies, smooth shading, faces with lids and mouths
        IEnumerator ShotCrewCloseup()
        {
            SetTime(0.48f);
            yield return EnsureOnLand();
            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Vector3 deck = new Vector3(end.x - 4f, end.y - 0.05f, end.z);
            Vector3 camPos = deck + new Vector3(0f, 1.45f, -2.3f);
            var looks = new[]
            {
                Look(DiverStyle.PlayerColors[0], 1, 1, 0, 1, 0, 0),
                Look(DiverStyle.PlayerColors[1], 4, 2, 1, 3, 2, 4),
                Look(DiverStyle.PlayerColors[2], 8, 5, 4, 0, 3, 1),
                Look(DiverStyle.PlayerColors[3], 13, 3, 3, 2, 5, 2),
            };
            int[] expr = { (int)SelfieExpression.Grin, (int)SelfieExpression.Wow, (int)SelfieExpression.Laugh, (int)SelfieExpression.Wink };
            for (int i = 0; i < looks.Length; i++)
            {
                Vector3 feet = deck + new Vector3((i - 1.5f) * 0.82f, 0f, 0f);
                float yaw = Mathf.Atan2(camPos.x - feet.x, camPos.z - feet.z) * Mathf.Rad2Deg;
                var p = BuildDiver(looks[i], feet, yaw);
                if (p == null) continue;
                p.SetMotion(MotionState.Idle, 0f, i * 0.25f, i == 1 ? HoldItem.Camera : HoldItem.None, i == 2 ? HoldItem.Flashlight : HoldItem.None, false);
                p.SetExpression(expr[i]);
                p.Phase = i * 1.3f;
            }
            FreeCamera(camPos, deck + Vector3.up * 1.25f);
            yield return Wait(2f);
            yield return Capture("r2_19_crew_closeup");
            ClearTemp();
        }

        // r2_22 – a co-op avatar sprinting along the dock, side view (motion set L.13)
        IEnumerator ShotRunAvatar()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 start = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            start = new Vector3(start.x - 18f, start.y - 0.05f, start.z);
            var p = BuildDiver(Look(DiverStyle.PlayerColors[1], 5, 7, 0, 1, 1, 4), start, 90f);
            if (p == null) yield break;
            FreeCamera(start + new Vector3(0f, 1.1f, -3.4f), start + Vector3.up * 1f);
            float t = 0f, phase = 0f;
            bool shot = false;
            while (t < 1.6f)
            {
                float dt = Time.deltaTime;
                t += dt;
                phase = Mathf.Repeat(phase + dt * 2.1f, 1f);
                Vector3 pos = start + Vector3.right * (t * 6f);
                p.transform.position = pos;
                p.SetMotion(MotionState.Run, 6f, phase, HoldItem.Camera, HoldItem.None, false);
                Pose(pos + new Vector3(0f, 1.1f, -3.4f), pos + Vector3.up * 1f);
                if (!shot && t > 1.0f)
                {
                    shot = true;
                    yield return Capture("r2_22_run_avatar");
                }
                yield return null;
            }
            ClearTemp();
        }

        // 20 – a lagoon patch reef (L.3): 2 m deep, looking at a lagoon_patch_N spot from ~6 m
        IEnumerator ShotLagoonPatch()
        {
            SetTime(0.5f);
            Vector3 patch = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 14 && !found; i++)
                if (PropInfo.Spot("lagoon_patch_" + i, out patch)) found = true;
            if (!found) { Note("WARNING: no lagoon_patch_N spot – 20_lagoon_patch skipped"); yield break; }
            // from the island side (the beach is ~60 m inland of the patch), slightly above the patch top
            Vector3 toIsland = new Vector3(WorldLayout.IslandCenter.x - patch.x, 0f, WorldLayout.IslandCenter.z - patch.z);
            if (toIsland.sqrMagnitude < 1e-3f) toIsland = Vector3.forward;
            toIsland.Normalize();
            Vector3 eye = new Vector3(patch.x, 0f, patch.z) + toIsland * 6f;
            eye.y = Mathf.Max(PropGround.H(eye.x, eye.z) + 1.2f, Waves.SeaLevel - 2f);
            eye = KeepInWater(eye, 1.0f, 1.2f);
            yield return Dive(eye, patch + Vector3.up * 0.5f);
            ClearCrowd();
            yield return Wait(1.0f);
            yield return Capture("20_lagoon_patch");
        }

        // r2_21 – first person mid-climb on a ladder: both mittens on the rungs (W held – there is no auto-climb)
        IEnumerator ShotLadderFP()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            int best = -1;
            float bestLen = float.MaxValue;
            for (int i = 0; i < PropInfo.Ladders.Count; i++)
            {
                var l = PropInfo.Ladders[i];
                float len = l.top.y - l.bottom.y;
                if (l.bottom.y < Waves.SeaLevel + 0.3f || len < 2.5f || len > 14f) continue;
                if (len < bestLen) { bestLen = len; best = i; }
            }
            if (best < 0 || pc == null) { Note("WARNING: no land ladder for r2_21"); yield break; }
            var lad = PropInfo.Ladders[best];
            Vector3 o = lad.outward;
            float face = Mathf.Atan2(-o.x, -o.z) * Mathf.Rad2Deg;
            Hands.StowAll();
            pc.enabled = true;
            pc.TeleportTo(new Vector3(lad.bottom.x, lad.bottom.y + 0.05f, lad.bottom.z) + o * 0.85f, face);
            yield return Wait(0.6f);
            if (!pc.TryClimbLadder()) { Note("WARNING: could not grab the ladder"); yield break; }
            GameInput.SimPress(GameAction.Forward);
            // both mittens must stay in frame through the climb cycle (5 sampled phases)
            var seen = new bool[5];
            int sampled = 0, ok = 0;
            float tc = 0f, climbFor = Mathf.Min(1.6f, bestLen / 0.65f * 0.45f);
            while (tc < climbFor)
            {
                tc += Time.unscaledDeltaTime;
                if (pc.OnGenericLadder && !pc.LadderMantling)
                {
                    int b = Mathf.Clamp((int)(pc.LadderCycle01 * 5f), 0, 4);
                    if (!seen[b]) { seen[b] = true; sampled++; if (LadderHandCheck.BothInView(cam, out _, out _)) ok++; }
                }
                yield return null;
            }
            GameInput.SimRelease(GameAction.Forward);
            Note("ladder hands in frame at " + ok + "/" + sampled + " sampled climb phases" + (ok < sampled ? " – WARNING: a mitten left the frame" : ""));
            yield return Wait(0.25f);
            Note("ladder " + best + " (" + bestLen.ToString("0.0") + " m), on ladder " + pc.OnGenericLadder + ", root " + pc.LadderRootY.ToString("0.00"));
            yield return Capture("r2_21_ladder_fp");
            GameInput.SimPress(GameAction.Back);
            float t = 0f;
            while (pc.OnGenericLadder && t < 10f) { t += Time.unscaledDeltaTime; yield return null; }
            GameInput.SimRelease(GameAction.Back);
        }

        // r2_24 – the selfie view at the house reef: own diver + fish in the background
        IEnumerator ShotSelfieReef()
        {
            SetTime(0.5f);
            if (pc == null) yield break;
            var site = DiveSites.Get("house_reef");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.6f, -9f, -5f);
            pc.enabled = true;
            pc.TeleportTo(new Vector3(floor.x, Mathf.Min(floor.y + 2.2f, Waves.SeaLevel - 3.2f), floor.z), 40f);
            pc.RefillAir();
            yield return Wait(1.2f);
            Hold(HoldItem.Camera);
            if (!pc.TryEnterSelfie()) { Note("WARNING: selfie view did not open"); yield break; }
            yield return Wait(0.6f);
            Vector3 head = pc.Root.position + Vector3.up * PlayerController.EyeHeight;
            Vector3 back = Flat(head - cam.transform.position);
            Vector3 side = Vector3.Cross(Vector3.up, back).normalized;
            Spawn("parrotfish", KeepInWater(head + back * 2.2f + side * 0.7f, 0.8f, 1f), SizeTier.Large);
            Spawn("blue_tang", KeepInWater(head + back * 2.6f - side * 0.8f + Vector3.up * 0.4f, 0.8f, 1f), SizeTier.Large);
            Spawn("clownfish", KeepInWater(head + back * 1.8f - side * 0.3f - Vector3.up * 0.4f, 0.8f, 1f), SizeTier.Large);
            GameInput.SimTap(GameAction.RightSlot1);   // grin
            yield return Wait(1.6f);
            yield return Capture("r2_24_selfie_reef");
            pc.ExitSelfie();
            Hands.StowAll();
            yield return Frames(2);
        }

        // r2_17 – tide pools with crabs and sea stars
        IEnumerator ShotTidepools()
        {
            SetTime(0.45f);
            yield return EnsureOnLand();
            Vector3 c = PropInfo.TidePoolsCenter != Vector3.zero ? PropInfo.TidePoolsCenter : Ground(WorldLayout.TidePools);
            float g = WorldInfo.GroundHeight(c);
            Vector3 eye = new Vector3(c.x + 2.2f, Mathf.Max(g, Waves.SeaLevel) + 1.5f, c.z - 2.0f);
            FreeCamera(eye, new Vector3(c.x, g, c.z));
            Spawn("hermit_crab", new Vector3(c.x + 0.4f, g + 0.05f, c.z + 0.2f), SizeTier.Large);
            Spawn("sally_crab", new Vector3(c.x - 0.6f, g + 0.05f, c.z - 0.3f), SizeTier.Large);
            Spawn("blue_starfish", new Vector3(c.x + 0.1f, g + 0.03f, c.z - 0.7f), SizeTier.Large);
            yield return Wait(1.6f);
            yield return Capture("r2_17_tidepools");
        }

        // r2_18 – a dolphin pod just under the surface in open water
        IEnumerator ShotDolphins()
        {
            SetTime(0.5f);
            var site = DiveSites.Get("blue_drop");
            Vector3 c = site != null ? site.center : new Vector3(-700f, 0f, -560f);
            Vector3 eye = KeepInWater(new Vector3(c.x + 60f, Waves.SeaLevel - 2.2f, c.z + 40f), 4f, 1.6f);
            Vector3 look = eye + Flat(new Vector3(1f, 0f, 0.3f)) * 10f;
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("dolphin", eye + f * 7f + r * 1.5f, SizeTier.Large);
            Spawn("dolphin", eye + f * 9f - r * 1.2f + Vector3.down * 0.6f, SizeTier.Normal);
            Spawn("dolphin", eye + f * 12f + r * 0.4f + Vector3.down * 1.2f, SizeTier.Large);
            yield return Wait(1.4f);
            yield return Capture("r2_18_dolphins");
        }

        // r2_13 – the Waterfall Grotto (cave mouth in the waterfall cliff)
        IEnumerator ShotCave()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 mouth = PropInfo.CaveMouth != Vector3.zero ? PropInfo.CaveMouth : Ground(WorldLayout.CaveEntrance);
            Vector3 chamber = PropInfo.CaveChamber != Vector3.zero ? PropInfo.CaveChamber : mouth + Vector3.forward * 6f;
            Vector3 dir = Flat(chamber - mouth);
            Vector3 eye = mouth - dir * 2.5f + Vector3.up * 1.6f;
            FreeCamera(eye, chamber + Vector3.up * 1f);
            yield return Wait(2.5f);
            yield return Capture("r2_13_cave");
        }

        // r2_14 – the hidden bay from above
        IEnumerator ShotBay()
        {
            SetTime(0.55f);
            yield return EnsureOnLand();
            Vector3 b = WorldLayout.HiddenBay;
            float g = Mathf.Max(WorldInfo.GroundHeight(b), Waves.SeaLevel);
            FreeCamera(new Vector3(b.x + 26f, g + 17f, b.z - 22f), new Vector3(b.x, g, b.z));
            yield return Wait(3f);
            yield return Capture("r2_14_bay");
        }

        // r2_23 – the village Altriff at golden hour
        IEnumerator ShotVillage()
        {
            SetTime(0.7f);
            yield return EnsureOnLand();
            Vector3 v = WorldLayout.Village;
            float g = Mathf.Max(WorldInfo.GroundHeight(v), Waves.SeaLevel);
            FreeCamera(new Vector3(v.x + 22f, g + 13f, v.z - 30f), new Vector3(v.x, g + 2f, v.z));
            yield return Wait(6.5f);
            yield return Capture("r2_23_village");
            Pose(new Vector3(v.x - 6f, g + 1.7f, v.z - 14f), new Vector3(v.x + 4f, g + 1.6f, v.z + 2f));
            yield return Wait(1.5f);
            yield return Capture("r2_23b_village_street");
        }

        // r2_27 – Wanda's Chopper Shop hangar on the helipad plateau
        IEnumerator ShotHangar()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 h = WorldLayout.HangarSpot;
            if (PropInfo.Spot("hangar", out Vector3 hs)) h = hs;
            float g = Mathf.Max(WorldInfo.GroundHeight(h), Waves.SeaLevel);
            // from the pad side: the open front with the „CHOPPER SHOP“ sign and the „WANDAS WIRBEL“ board (the
            // fixed +x/+z offset showed the back wall)
            Vector3 fwd = Quaternion.Euler(0f, PropInfo.SpotYaw("hangar"), 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd).normalized;
            Vector3 eye = h + fwd * 9f + side * 3.5f;
            eye.y = g + 3.2f;
            FreeCamera(eye, new Vector3(h.x, g + 2f, h.z));
            yield return Wait(2f);
            yield return Capture("r2_27_hangar");
        }

        // r2_25 – the quest line top centre + two short notifications
        IEnumerator ShotQuestLine()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            yield return Stand(dock + new Vector3(-10f, 0.05f, 0f), 90f);
            QuestTracker.SetEntry(QuestSource.Other, "autoshot", Loc.Tr("3 Arten fürs Sealog", "3 species for the Sealog"), "1/3", DiveSites.Get("house_reef").center, NotifyKind.Sealog);
            QuestTracker.Track("autoshot");
            GameEvents.RaiseNotify(NotifyKind.Money, Loc.Tr("FinTube zahlt", "FinTube pays"), "+86 $");
            GameEvents.RaiseNotify(NotifyKind.Sealog, Loc.Tr("Neu im Sealog", "New in the Sealog"), Loc.Tr("Papageifisch", "Parrotfish"));
            yield return Wait(0.9f);
            yield return Capture("r2_25_quest_line");
            QuestTracker.SetEntry(QuestSource.Other, "autoshot", null, null, null);
        }

        // r2_28 – inside the MS Clickbait with the lamp
        IEnumerator ShotWreckInterior()
        {
            SetTime(0.55f);
            var b = PropInfo.WreckInterior;
            if (b.size.sqrMagnitude < 1f) { Note("WARNING: no wreck interior published"); yield break; }
            Vector3 c0 = PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : b.center;
            // first above the wreck (site decor activates near the camera), then inside
            yield return Dive(KeepInWater(c0 + Vector3.up * 14f, 3f, 2f), c0);
            Vector3 eye = b.center + Vector3.up * 0.3f;
            Vector3 look = eye + new Vector3(b.extents.x > b.extents.z ? 1f : 0f, -0.1f, b.extents.x > b.extents.z ? 0f : 1f) * 6f;
            yield return Dive(eye, look);
            Hold(HoldItem.Camera);
            if (lamp != null) lamp.SetOn(true);
            yield return Wait(1.5f);
            yield return Capture("r2_28_wreck_interior");
            if (lamp != null) lamp.SetOn(false);
            Hands.StowAll();
        }

        // r2_26 – the buoy lane through the fog toward the Fog Isle (with Toni's map)
        IEnumerator ShotFogIsle()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            StoryFlags.Set(StoryKeys.FogMap);
            Vector3 lane = WorldLayout.FogLaneEnd;
            Vector3 isle = WorldLayout.FogIsland;
            FreeCamera(new Vector3(lane.x + 6f, Waves.SeaLevel + 4.5f, lane.z), new Vector3(isle.x, Waves.SeaLevel + 6f, isle.z));
            yield return Wait(6.5f);
            yield return Capture("r2_26_fog_isle");
        }
    }
    /// Debug: are both first-person mittens (the "Mitten" renderers of PlayerHands) inside the viewport?
    internal static class LadderHandCheck
    {
        public static bool BothInView(Camera cam, out int mittens, out int inView)
        {
            mittens = 0;
            inView = 0;
            if (cam == null) return false;
            var hands = Object.FindFirstObjectByType<PlayerHands>();
            if (hands == null) return false;
            var rs = hands.GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null || r.name != "Mitten" || !r.enabled) continue;
                mittens++;
                Vector3 v = cam.WorldToViewportPoint(r.bounds.center);
                if (v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f) inView++;
            }
            return mittens >= 2 && inView >= 2;
        }
    }}
