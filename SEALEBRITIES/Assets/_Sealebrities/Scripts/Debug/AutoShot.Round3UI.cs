using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 6 screenshots (phone, UI, boat). Files: r3u_*.png
    ///   r3u_map_phone (treasure map, island), r3u_map_zoom (zoomed in on the hut), r3u_map_world (whole sheet),
    ///   r3u_map_overlay_boat (the M overlay at the wheel), r3u_bench (the passenger bench from the deck),
    ///   r3u_bench_view (sitting on the bench, camera in hand), r3u_hud_land (tidy land HUD), r3u_backpack (fit)
    /// The shop / loading / studio / cookbook shots come from their own partial files (ShotsRound3UISub).
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3UI()
        {
            yield return Guard(ShotR3UIMap(), "r3u_map");
            yield return Guard(ShotR3UIBoat(), "r3u_boat");
            yield return Guard(ShotR3UIHud(), "r3u_hud");
            yield return Guard(ShotsRound3UISub(), "r3u_sub");
            if (UIState.MapOverlayOpen && UIModule.Controller != null) UIModule.Controller.MapPanel.Close();
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
            if (pc != null) pc.enabled = true;
        }

        IEnumerator ShotR3UIMap()
        {
            var ctl = UIModule.Controller;
            if (ctl == null) yield break;
            SetTime(0.45f);
            yield return EnsureOnLand();
            pc.enabled = true;
            UITreasureMapArt.BuildAllNow();
            // a couple of pins so the shot shows them
            MapPins.Clear();
            MapPins.Add(WorldLayout.SwimRaft + new Vector3(10f, 0f, 6f), MapPins.IconFish);
            MapPins.Add(WorldLayout.HiddenBay, MapPins.IconTreasure);
            MapPins.Add(WorldLayout.Lighthouse + new Vector3(30f, 0f, 20f), MapPins.IconCamera);
            if (!UIModule.DebugOpenPhone("map")) yield break;
            yield return Wait(1f);
            var map = ctl.Phone.Map.Map;
            if (map == null) yield break;
            map.SetView(new Vector3(20f, 0f, 0f), 4.2f);
            yield return Wait(1f);
            yield return Capture("r3u_map_phone");
            map.SetView(WorldLayout.Hut, 12f);
            yield return Wait(1f);
            yield return Capture("r3u_map_zoom");
            map.SetView(Vector3.zero, 1f);
            yield return Wait(1f);
            yield return Capture("r3u_map_world");
            UIModule.DebugOpenPhone("close");
        }

        IEnumerator ShotR3UIBoat()
        {
            var ctl = UIModule.Controller;
            var boat = Refs.Boat as BoatController;
            if (ctl == null || boat == null || boat.Parts == null) yield break;
            SetTime(0.45f);
            if (!boat.AtMooring) boat.PlaceAt(boat.MooringPosition, boat.MooringYaw);
            yield return EnsureOnLand();
            pc.enabled = true;
            boat.BoardFromDock();
            yield return Wait(1f);
            // the bench seen from the bow (the old eye behind the wheelhouse corner sat inside the cabin wall)
            Vector3 b = boat.transform.TransformPoint(new Vector3(0.75f, BoatHull.DeckY + 2.2f, 2.9f));
            Vector3 look = boat.transform.TransformPoint(new Vector3(BoatModelBuilder.BenchX, BoatHull.DeckY + 0.5f, 1.45f));
            FreeCamera(b, look);
            yield return Wait(0.8f);
            yield return Capture("r3u_bench");
            pc.enabled = true;
            var bench = BoatBenchInteractable.Instance;
            if (bench != null)
            {
                pc.TeleportTo(boat.transform.TransformPoint(boat.Parts.benchExitLocal[0]), boat.transform.eulerAngles.y);
                yield return Wait(0.5f);
                if (bench.SitOn(0))
                {
                    Hands.Hold(ItemIds.Camera, Hand.Right);
                    yield return Wait(1f);
                    yield return Capture("r3u_bench_view");
                    UIModule.Controller.MapPanel.Open();
                    yield return Wait(1f);
                    yield return Capture("r3u_map_overlay_bench");
                    UIModule.Controller.MapPanel.Close();
                    bench.StandUp();
                    Hands.StowAll();
                }
            }
            // the overlay at the wheel
            var seat = boat.Parts.seat;
            Vector3 sl = boat.transform.InverseTransformPoint(seat.position);
            pc.TeleportTo(boat.transform.TransformPoint(new Vector3(0f, sl.y + 0.45f, sl.z - 1.0f)), boat.transform.eulerAngles.y);
            yield return Wait(0.6f);
            boat.EnterHelm();
            yield return Wait(0.6f);
            ctl.MapPanel.Open();
            yield return Wait(1.2f);
            yield return Capture("r3u_map_overlay_boat");
            ctl.MapPanel.Close();
            boat.ExitHelm();
            yield return Wait(0.4f);
            MapPins.Clear();
        }

        IEnumerator ShotR3UIHud()
        {
            var ctl = UIModule.Controller;
            if (ctl == null) yield break;
            SetTime(0.45f);
            pc.enabled = true;
            yield return Stand(PlayerController.ResolveSpawn(out float yaw), yaw);
            yield return Wait(1f);
            yield return Capture("r3u_hud_land");
            UIModule.DebugOpenPhone("inventory");
            yield return Wait(1f);
            yield return Capture("r3u_backpack");
            UIModule.DebugOpenPhone("inventory:close");
        }
    }
}
