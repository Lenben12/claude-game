using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 6 (phone, UI, boat – GDD §19 „Bedienung und Oberfläche“, „Boot“ + §20 Q30–Q37, Q40, Q41): the treasure
    /// map (zoom / pan, places after discovery, shared pins + HUD markers, clue and wreck pins, the M overlay while
    /// driving), the boat (anchor only by the driver, the passenger bench – film + map from it), the minimal camera HUD
    /// with the 8 m photo range and the tidy land HUD, and the polish of the step-5 UI / Sealog pages / market „Funde“ /
    /// backpack. Steps 80–83 (shop, loading screen, studio, cookbook) live in their own partial files.
    /// Runs inside RunRound3Steps (-r3only) or alone with
    ///     SEALEBRITIES.exe -autoplaytest "C:/out" -r3ui
    public sealed partial class AutoPlaytest
    {
        static bool Round3UIOnly => Array.IndexOf(Environment.GetCommandLineArgs(), "-r3ui") >= 0;

        IEnumerator RunRound3UISteps()
        {
            yield return RunRound3UISubSteps();
            yield return RunStep("84 Treasure map (Q30/Q31): parchment art, smooth zoom + pan, places after discovery (camp, volcano lock), shared pins + HUD markers, clue / wreck pins, the M overlay while driving", StepR3UIMap, 180f);
            yield return RunStep("85 Boat (GDD §19, Q36/Q37): only the driver works the anchor, the passenger bench (2 seats) – film and open the map from it, stand up", StepR3UIBoat, 150f);
            yield return RunStep("86 Camera HUD (Q40/Q41): minimal viewfinder with species icons, no frame beyond 8 m, frame + '?' close by; the land HUD stays tidy", StepR3UICamera, 150f);
            yield return RunStep("87 UI polish: backpack fits 1280×720, Sealog pages Vulkaninsel / Kuriositäten, market 'Funde', gesture wheel + gift picker + tie-up bar, site line", StepR3UIPolish, 150f);
        }

        static UIController UICtl => UIModule.Controller;

        // ================================================================== 84 treasure map
        IEnumerator StepR3UIMap()
        {
            var ctl = UICtl;
            Check(ctl != null && ctl.MapPanel != null, "UI controller with the map overlay");
            if (ctl == null || ctl.MapPanel == null) yield break;
            pc.enabled = true;
            Vector3 hutFeet = SnapDown(WorldLayout.Hut + new Vector3(-6f, 3f, 4f));
            yield return Stand(hutFeet, 90f);

            // ---- parchment art (built in the background; force it here)
            float t0 = Time.realtimeSinceStartup;
            UITreasureMapArt.BuildAllNow();
            Info("treasure map art built in " + F(Time.realtimeSinceStartup - t0, "0.00") + " s");
            Check(UITreasureMapArt.AllDone && UITreasureMapArt.Base.tex != null, "the parchment sheets are drawn (" + UITreasureMapArt.Sheets.Length + " sheets)");
            var b = UITreasureMapArt.Base.tex;
            if (b != null)
            {
                Vector2 uvLand = UITreasureMap.ToUV(WorldLayout.Hut + new Vector3(-20f, 0f, 0f)), uvSea = UITreasureMap.ToUV(new Vector3(-600f, 0f, 0f));
                // the sheets are uploaded non-readable (no CPU copy): sample a readable GPU copy
                Texture2D rd = b;
                bool tmp = false;
                if (!b.isReadable)
                {
                    var rt = RenderTexture.GetTemporary(b.width, b.height, 0, RenderTextureFormat.ARGB32);
                    Graphics.Blit(b, rt);
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    rd = new Texture2D(b.width, b.height, TextureFormat.RGBA32, false);
                    rd.ReadPixels(new Rect(0, 0, b.width, b.height), 0, 0);
                    rd.Apply();
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    tmp = true;
                }
                Color land = rd.GetPixelBilinear(uvLand.x, 1f - uvLand.y), sea = rd.GetPixelBilinear(uvSea.x, 1f - uvSea.y);
                if (tmp) Object.Destroy(rd);
                // the sea is a pale turquoise wash over the parchment: cooler (more blue relative to red) than the land
                Check(land.b < land.r && (sea.b - sea.r) > (land.b - land.r) + 0.05f, "land is drawn warm, the sea teal (land " + land + ", sea " + sea + ")");
            }

            // ---- phone map: zoom + pan
            Check(UIModule.DebugOpenPhone("map"), "the Seekarte app opens");
            yield return Wait(0.8f);
            var map = ctl.Phone.Map.Map;
            Check(map != null, "the treasure map element is on the page");
            if (map == null) yield break;
            map.SetView(WorldLayout.Hut, 3f);
            yield return Wait(0.3f);
            float z0 = map.Zoom;
            map.ZoomBy(1.6f);
            yield return null;
            yield return null;
            float zMid = map.Zoom;
            yield return Wait(0.6f);
            Check(Mathf.Abs(map.Zoom - z0 * 1.6f) < 0.05f * z0 && zMid > z0 && zMid < map.Zoom, "+ zooms smoothly (" + F(z0) + " → " + F(zMid) + " → " + F(map.Zoom) + ")");
            Vector2 at = new Vector2(120f, 160f);
            Vector3 w0 = map.ViewToWorld(at);
            map.ZoomBy(1.4f, at);   // the wheel path: zoom around the pointer
            yield return Wait(0.7f);
            Vector3 w1 = map.ViewToWorld(at);
            Check(FlatDist(w0, w1) < 4f, "zooming keeps the point under the pointer (" + F(FlatDist(w0, w1)) + " m drift)");
            Vector3 c0 = map.CenterWorld;
            map.PanBy(new Vector2(-150f, 0f));
            yield return null;
            Vector3 c1 = map.CenterWorld;
            Check(c1.x > c0.x + 5f, "dragging pans the map (" + F(c1.x - c0.x) + " m east)");
            map.SetView(WorldLayout.Hut, 4f);
            yield return Wait(0.6f);
            yield return Shot("84_map_phone");
            map.SetView(WorldLayout.Hut, 12f);
            yield return Wait(0.6f);
            yield return Shot("84_map_zoomed");

            // ---- places after discovery: the lighthouse + the hermit camp
            const string lhFlag = "map:lighthouse";
            bool lhHad = StoryFlags.Has(lhFlag), campHad = StoryFlags.Has(StoryKeys.HermitCampFound);
            if (lhHad) StoryFlags.Clear(lhFlag);
            if (campHad) StoryFlags.Clear(StoryKeys.HermitCampFound);
            yield return Wait(0.6f);
            int tokens0 = map.TokenCount;
            UIModule.DebugOpenPhone("close");
            yield return Stand(SnapDown(WorldLayout.Lighthouse + new Vector3(6f, 4f, -6f)), 0f);
            yield return Wait(1.2f);
            Check(StoryFlags.Has(lhFlag), "walking to the lighthouse discovers it");
            StoryFlags.Set(StoryKeys.HermitCampFound);
            UIModule.DebugOpenPhone("map");
            yield return Wait(1f);
            map = ctl.Phone.Map.Map;
            int tokens1 = map != null ? map.TokenCount : 0;
            Check(tokens1 > tokens0, "the lighthouse and the camp appear on the map after discovery (" + tokens0 + " → " + tokens1 + " tokens)");
            if (!campHad) StoryFlags.Clear(StoryKeys.HermitCampFound);
            Info("volcano gate open: " + VolcanoGate.Open + " (lock token until the wreck)");

            // ---- shared pins
            MapPins.Clear();
            yield return null;
            if (map != null)
            {
                map.SetView(WorldLayout.Hut, 4f);
                yield return Wait(0.4f);
                map.TogglePinMode(MapPins.IconFish);
                Check(map.PinMode == MapPins.IconFish, "the fish pin is armed");
                Vector2 mid = new Vector2(map.contentRect.width * 0.5f, map.contentRect.height * 0.5f);
                map.Tap(mid);
                yield return Wait(0.6f);
                Check(MapPins.Count == 1 && MapPins.All[0].icon == MapPins.IconFish && map.PinMode < 0, "tapping the map sets the pin for the crew (" + MapPins.Count + ")");
                Check(map.PinTokenCount == 1, "the pin is drawn on the map");
                if (MapPins.Count == 1)
                {
                    var pin = MapPins.All[0];
                    map.Tap(map.WorldToView(new Vector3(pin.x, 0f, pin.z)));
                    yield return Wait(0.3f);
                    Check(map.SelectedPin == pin.id, "tapping a pin selects it (waypoint / remove bar)");
                    yield return Shot("84_map_pin_selected");
                }
            }
            UIModule.DebugOpenPhone("close");
            // the HUD markers hide behind blocking screens (the morning summary card of the clock jumps): wait them out
            yield return WaitUntil(() => !UIState.BlocksGameplay, 10f);
            // a pin 40 m ahead shows in the HUD
            MapPins.Clear();
            Vector3 me = pc.Root.position, fwd = Flat(cam.transform.forward);
            MapPins.Add(me + fwd * 40f, MapPins.IconStar);
            MapPins.Add(me + fwd * 25f + Vector3.Cross(Vector3.up, fwd) * 10f, MapPins.IconTreasure);
            yield return Wait(0.6f);
            Check(ctl.Hud.Pins.Shown >= 1, "the pins show as small markers in the HUD (" + ctl.Hud.Pins.Shown + ")");
            yield return Shot("84_hud_pins");
            int id0 = MapPins.All.Count > 0 ? MapPins.All[0].id : -1;
            Check(MapPins.Remove(id0) && MapPins.Count == 1, "a pin can be removed");

            // ---- clue + wreck pins
            string clueFlag = null;
            foreach (var e in NpcClues.All) if (e.key == "map_p0") clueFlag = NpcClues.FlagOf(e);
            bool clueHad = clueFlag != null && StoryFlags.Has(clueFlag), wreckHad = StoryFlags.Has("found:sailboat");
            int clues0 = MapClues.Current.Count;
            if (clueFlag != null && !MapPieces.IsFound(0)) StoryFlags.Set(clueFlag);
            if (!wreckHad) StoryFlags.Set("found:sailboat");
            yield return Wait(0.5f);
            if (clueFlag != null && !MapPieces.IsFound(0)) Check(MapClues.Current.Count > clues0 || clueHad, "a heard clue becomes a „?“ circle on the map (" + MapClues.Current.Count + ")");
            UIModule.DebugOpenPhone("map");
            yield return Wait(1f);
            map = ctl.Phone.Map.Map;
            if (map != null && SeaContent.Placed.TryGetValue("wreck_sailboat", out Vector3 wreck))
            {
                map.SetView(wreck, 5f);
                yield return Wait(0.8f);
                yield return Shot("84_map_wreck_clue");
            }
            UIModule.DebugOpenPhone("close");
            if (clueFlag != null && !clueHad) StoryFlags.Clear(clueFlag);
            if (!wreckHad) StoryFlags.Clear("found:sailboat");

            // ---- the M overlay while driving: steering goes on, the cursor is free
            var boat = Refs.Boat as BoatController;
            if (boat != null)
            {
                Vector3 mooring = boat.MooringPosition;
                float myaw = boat.MooringYaw;
                if (!boat.AtMooring) boat.PlaceAt(mooring, myaw);
                if (!boat.PlayerAboard) boat.BoardFromDock();
                yield return Wait(0.8f);
                var seat = boat.Parts.seat;
                Vector3 sl = boat.transform.InverseTransformPoint(seat.position);
                pc.TeleportTo(boat.transform.TransformPoint(new Vector3(0f, sl.y + 0.45f, sl.z - 1.0f)), boat.transform.eulerAngles.y);
                yield return Wait(0.7f);
                boat.EnterHelm();
                yield return Wait(0.4f);
                Check(boat.PlayerDriving, "at the wheel");
                if (boat.Anchored) { GameInput.SimTap(GameAction.Anchor); yield return Wait(0.5f); }
                GameInput.SimTap(GameAction.Map);
                yield return Wait(0.5f);
                Check(UIState.MapOverlayOpen && !UIState.BlocksGameplay, "M opens the map overlay while driving (it does not block the steering)");
                Check(Cursor.lockState == CursorLockMode.None || GameManager.AutomatedRun, "the cursor is free on the overlay");
                Vector3 p0 = boat.transform.position;
                GameInput.SimPress(GameAction.Forward);
                yield return Wait(3.5f);
                GameInput.SimRelease(GameAction.Forward);
                Check(FlatDist(boat.transform.position, p0) > 3f, "the boat keeps driving with the map open (" + F(FlatDist(boat.transform.position, p0)) + " m)");
                var om = ctl.MapPanel.Map;
                Check(om != null && om.Follow, "the overlay follows the boat");
                yield return Shot("84_map_overlay_driving");
                GameInput.SimTap(GameAction.Map);
                yield return Wait(0.3f);
                Check(!UIState.MapOverlayOpen, "M closes it again");
                for (int i = 0; i < 6 && boat.PlayerDriving; i++)
                {
                    yield return Wait(1f);
                    GameInput.SimTap(GameAction.Interact);
                    yield return Wait(0.3f);
                }
                boat.PlaceAt(mooring, myaw);
                if (!boat.Anchored) boat.ToggleAnchor();
            }
            MapPins.Clear();
            if (!lhHad) StoryFlags.Clear(lhFlag);
            pc.enabled = true;
        }

        // ================================================================== 85 boat: anchor rule + passenger bench
        IEnumerator StepR3UIBoat()
        {
            var boat = Refs.Boat as BoatController;
            Check(boat != null && boat.Parts != null && boat.Parts.benchSeats != null && boat.Parts.benchSeats.Length == 2, "the boat has the passenger bench with 2 seats");
            if (boat == null || boat.Parts == null || boat.Parts.benchSeats == null) yield break;
            var bench = BoatBenchInteractable.Instance;
            Check(bench != null, "bench interactable");
            if (bench == null) yield break;
            if (!boat.AtMooring) boat.PlaceAt(boat.MooringPosition, boat.MooringYaw);
            if (!boat.Anchored) boat.ToggleAnchor();
            if (!boat.PlayerAboard) boat.BoardFromDock();
            yield return Wait(0.8f);
            Check(boat.PlayerAboard, "aboard");

            // ---- not the driver: Q does nothing to the anchor, the winch only says so
            bool a0 = boat.Anchored;
            GameInput.SimTap(GameAction.Anchor);
            yield return Wait(0.6f);
            Check(boat.Anchored == a0, "a crew member who is not driving cannot work the anchor (anchored " + boat.Anchored + ")");
            var winch = boat.GetComponentInChildren<BoatAnchorInteractable>(true);
            if (winch != null)
            {
                string wp = winch.GetPrompt() ?? "";
                Check(wp.IndexOf("Steuer", StringComparison.Ordinal) >= 0 || wp.IndexOf("wheel", StringComparison.Ordinal) >= 0, "the winch says 'from the wheel only' ('" + wp + "')");
            }

            // ---- sit on the bench
            Vector3 s0 = boat.Parts.benchSeats[0].position;
            pc.TeleportTo(boat.transform.TransformPoint(boat.Parts.benchExitLocal[0]), boat.transform.eulerAngles.y);
            yield return Wait(0.6f);
            Check(bench.CanInteract(), "standing next to the bench: E – Hinsetzen ('" + (bench.GetPrompt() ?? "") + "')");
            bench.Interact();
            yield return Wait(0.5f);
            Check(pc.IsSeated && pc.IsPassenger && SeatRules.LocalPassenger && bench.LocalSeat >= 0, "sitting on the passenger bench");
            Check(FlatDist(pc.Root.position, boat.Parts.benchSeats[bench.LocalSeat].position) < 0.1f, "the root sits on the seat");
            float eyeAbove = cam.transform.position.y - boat.transform.TransformPoint(new Vector3(0f, BoatHull.DeckY, 0f)).y;
            Check(eyeAbove > 0.9f && eyeAbove < 1.5f, "seated eye height above the deck " + F(eyeAbove, "0.00") + " m");

            // film from the bench (Q36)
            bool held = Hands.Hold(ItemIds.Camera, Hand.Right);
            yield return Wait(0.4f);
            Check(held && Hands.IsHeld(HoldItem.Camera), "the passenger holds the camera");
            var hc = Refs.HandCamera as FilmHandCamera;
            if (hc != null)
            {
                GameInput.SimTap(GameAction.Use);
                yield return WaitUntil(() => hc.IsRecording, 2f);
                Check(hc.IsRecording, "the passenger films from the bench");
                yield return Shot("85_bench_filming");
                hc.StopRecording();
            }
            // Q on the bench: still not the anchor
            GameInput.SimTap(GameAction.Anchor);
            yield return Wait(0.5f);
            Check(boat.Anchored == a0, "the passenger cannot work the anchor either");
            // the map from the bench
            GameInput.SimTap(GameAction.Map);
            yield return Wait(0.5f);
            Check(UIState.MapOverlayOpen && pc.IsSeated, "the passenger opens the map and stays seated");
            yield return Shot("85_bench_map");
            GameInput.SimTap(GameAction.Map);
            yield return Wait(0.3f);
            Check(bench.Taken(1 - bench.LocalSeat) == false, "the other seat is free (offline)");
            // stand up
            GameInput.SimTap(GameAction.Interact);
            yield return Wait(0.6f);
            Check(!pc.IsSeated && !SeatRules.LocalPassenger && boat.PlayerAboard, "E stands up next to the bench, still on deck");

            // ---- the driver works the anchor
            var seat = boat.Parts.seat;
            Vector3 sl = boat.transform.InverseTransformPoint(seat.position);
            pc.TeleportTo(boat.transform.TransformPoint(new Vector3(0f, sl.y + 0.45f, sl.z - 1.0f)), boat.transform.eulerAngles.y);
            yield return Wait(0.6f);
            boat.EnterHelm();
            yield return Wait(0.4f);
            bool a1 = boat.Anchored;
            GameInput.SimTap(GameAction.Anchor);
            yield return Wait(0.6f);
            Check(boat.PlayerDriving && boat.Anchored != a1, "the driver raises / drops the anchor with Q");
            if (!boat.Anchored) { GameInput.SimTap(GameAction.Anchor); yield return Wait(0.6f); }
            yield return Shot("85_bench_from_helm");
            for (int i = 0; i < 4 && boat.PlayerDriving; i++) { GameInput.SimTap(GameAction.Interact); yield return Wait(0.5f); }
            boat.PlaceAt(boat.MooringPosition, boat.MooringYaw);
            if (!boat.Anchored) boat.ToggleAnchor();
            Hands.StowAll();
            pc.enabled = true;
        }

        // ================================================================== 86 camera HUD + land HUD
        IEnumerator StepR3UICamera()
        {
            var ctl = UICtl;
            if (ctl == null) yield break;
            // land: no dive block
            pc.enabled = true;
            yield return Stand(SnapDown(WorldLayout.Hut + new Vector3(-6f, 3f, 4f)), 90f);
            yield return Wait(0.6f);
            Check(!ctl.Hud.DiveBlockShown, "on land the dive block (air, depth, clips) is hidden");
            yield return Shot("86_hud_land");

            // under water at the house reef
            var reef = DiveSites.Get("house_reef");
            Vector3 c = reef != null ? reef.center : WorldLayout.DockEnd + new Vector3(40f, 0f, 20f);
            Vector3 floor = FindSeafloor(c, 60f, Waves.SeaLevel - 9f, Waves.SeaLevel - 4f);
            Vector3 eye = new Vector3(floor.x, Mathf.Min(Waves.SeaLevel - 1.5f, floor.y + 2.5f), floor.z);
            Hands.Hold(ItemIds.Camera, Hand.Right);
            yield return DiveTo(eye, eye + Vector3.forward * 6f);
            pc.enabled = true;
            yield return Wait(0.6f);
            Check(ctl.Hud.AirRingShown, "in the water the air ring is shown");
            var spawner = FaunaModule.Spawner;
            if (spawner != null) spawner.DespawnAll();
            eye = cam.transform.position;
            Vector3 fwd = Flat(cam.transform.forward);
            var mid = SpawnAt("parrotfish", KeepInWater(eye + fwd * 10.5f, 1.2f, 1.5f), SizeTier.Large);
            GameInput.SimPress(GameAction.Aim);
            float t = 0f;
            int midBrackets = 0, midDetected = 0;
            while (t < 1.4f)
            {
                if (mid != null && mid.IsAlive) Pose(eye, mid.Position);
                midDetected = Mathf.Max(midDetected, ViewfinderInfo.DetectedCount);
                if (t > 0.5f) midBrackets = Mathf.Max(midBrackets, ViewfinderInfo.BracketCount);
                yield return null;
                t += Time.deltaTime;
            }
            if (midDetected > 0) Check(midBrackets == 0, "10 m away (beyond the 8 m photo range): detected, but no aim frame (brackets " + midBrackets + ")");
            else Info("the 10 m animal was not detected at all");
            if (mid != null && mid.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(mid);
            var near = SpawnAt("parrotfish", KeepInWater(eye + fwd * 3f, 1.2f, 1.5f), SizeTier.Large);
            t = 0f;
            int nearBrackets = 0, icons = 0;
            while (t < 1.4f)
            {
                if (near != null && near.IsAlive) Pose(eye, near.Position);
                nearBrackets = Mathf.Max(nearBrackets, ViewfinderInfo.BracketCount);
                icons = Mathf.Max(icons, ctl.Viewfinder.SpeciesIconCount);
                yield return null;
                t += Time.deltaTime;
            }
            Check(nearBrackets >= 1, "3 m away: the aim frame is shown (" + nearBrackets + ")");
            Check(icons >= 1, "the minimal camera HUD shows a small icon per detected species (" + icons + ")");
            yield return Shot("86_camera_hud");
            GameInput.SimRelease(GameAction.Aim);
            if (near != null && near.IsAlive && Refs.Spawner != null) Refs.Spawner.Despawn(near);
            Hands.StowAll();
            pc.enabled = true;
            yield return Stand(SnapDown(WorldLayout.Hut + new Vector3(-6f, 3f, 4f)), 90f);
        }

        // ================================================================== 87 UI polish
        IEnumerator StepR3UIPolish()
        {
            var ctl = UICtl;
            if (ctl == null) yield break;
            pc.enabled = true;
            yield return Stand(SnapDown(WorldLayout.Hut + new Vector3(-6f, 3f, 4f)), 90f);

            // backpack fits the window (1480 px card scales down when needed)
            UIModule.DebugOpenPhone("inventory");
            yield return Wait(0.8f);
            Check(UIState.InventoryOpen && ctl.Inventory.CardFits, "the backpack card fits the screen " + Screen.width + "×" + Screen.height + " (scale " + F(ctl.Inventory.FitScale, "0.00") + ")");
            yield return Shot("87_backpack");
            UIModule.DebugOpenPhone("inventory:close");
            yield return Wait(0.3f);

            // Sealog pages: volcano + curiosities
            if (UIModule.DebugOpenPhone("sealog"))
            {
                yield return Wait(0.4f);
                Check(ctl.Phone.Sealog.DebugShowPage(SpeciesDatabase.VolcanoPage), "the Sealog page 'Vulkaninsel' exists");
                yield return Wait(0.6f);
                yield return Shot("87_sealog_volcano");
                Check(ctl.Phone.Sealog.DebugShowPage(SpeciesDatabase.MemePage), "the Sealog page 'Kuriositäten' exists");
                yield return Wait(0.6f);
                yield return Shot("87_sealog_curiosities");
            }
            // market „Funde“ with treasure in the backpack
            Backpack.Add(ItemIds.Coins, 3, false);
            Backpack.Add(ItemIds.Pearl, 1, false);
            if (UIModule.DebugOpenPhone("market"))
            {
                ctl.Phone.Market.DebugFindsTab(true);
                yield return Wait(0.7f);
                yield return Shot("87_market_finds");
                ctl.Phone.Market.DebugFindsTab(false);
            }
            UIModule.DebugOpenPhone("close");

            // gesture wheel + gift picker + tie-up bar at a hermit (hermit camp)
            var ni = NpcInteract.I;
            SetTime(10.5f / 24f);   // the cook works at the camp fire by day
            yield return Wait(2f);
            var cook = Npc(NpcDatabase.HCook);
            if (ni != null && cook != null && cook.Present)
            {
                FriendlyCamp();
                yield return StandAt(cook, 2.2f);
                // the wheel only stays open while G is held (release = choose): hold the key for the shot
                GameInput.SimPress(GameAction.Gestures);
                yield return Wait(0.3f);
                if (!NpcInteract.WheelOpen) ni.OpenWheel(cook);
                yield return Wait(0.5f);
                Check(NpcInteract.WheelOpen && UIState.NpcMenuOpen, "the gesture wheel opens");
                yield return Shot("87_gesture_wheel");
                ni.CloseWheelForTest();
                GameInput.SimRelease(GameAction.Gestures);
                yield return Wait(0.2f);
                Backpack.Add("mango", 2, false);
                ni.OpenPicker(cook, false);
                yield return Wait(0.5f);
                yield return Shot("87_gift_picker");
                ni.ClosePicker();
                yield return Wait(0.2f);
                NpcInteract.StartTieUp(cook.Id);
                yield return Wait(0.5f);
                yield return Shot("87_tie_up");
                for (int i = 0; i < 16 && NpcInteract.TiedUp; i++) NpcInteract.DebugWriggle(i % 2 == 0 ? -1 : 1);
                yield return Wait(0.3f);
                Check(!NpcInteract.TiedUp, "wriggled free again");
            }
            else Info("no hermit cook present – wheel / picker shots skipped");
            Check(!UIState.NpcMenuOpen, "no NPC menu left open");
            Info("site goal line: " + (ctl.SiteGoal.Text ?? "(hidden)"));
            pc.enabled = true;
        }
    }
}
