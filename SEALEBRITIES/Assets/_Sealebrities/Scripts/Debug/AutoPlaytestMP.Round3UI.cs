using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 6 co-op check (GDD §20 Q31 „Geteilte Koop-Pins“, §19 „Nur der Fahrer kann den Anker legen“, „zweiter
    /// Sitzplatz“): a pin set by the guest appears on the host's map and a removal by the host disappears for the guest;
    /// the guest sits on the passenger bench (the host sees the avatar on the seat, the seat counts as taken) and the
    /// guest's anchor key does nothing (only the driver anchors).
    public sealed partial class AutoPlaytestMP
    {
        IEnumerator StepRound3UI()
        {
            yield return Sync("S34a", 120f);
            if (pc == null) yield break;
            pc.enabled = true;
            yield return Stand(isHost ? HostSpot : ClientSpot, 90f);

            // ---- shared pins
            if (isHost) MapPins.Clear();
            yield return Wait(1.5f);
            if (!isHost)
            {
                var p = MapPins.Add(WorldLayout.Hut + new Vector3(30f, 0f, 12f), MapPins.IconTreasure);
                Check(p != null && MapPins.Count == 1, "guest set a treasure pin (optimistic)");
                SendVal("pin", "1");
                // final test: the host removed the pin before the guest looked (race) – the host now waits for "pinSeen"
                yield return WaitUntil(() => MapPins.Count == 1 && MapPins.All[0].id > 0, 8f);
                Check(MapPins.Count == 1 && MapPins.All[0].id > 0, "after the host's snapshot the pin has the host's id (" + (MapPins.Count > 0 ? MapPins.All[0].id : 0) + ")");
                SendVal("pinSeen", "1");
                yield return WaitVal("pinGone", 40f);
                yield return WaitUntil(() => MapPins.Count == 0, 8f);
                Check(MapPins.Count == 0, "the host removed the pin – gone for the guest too");
            }
            else
            {
                yield return WaitVal("pin", 40f);
                yield return WaitUntil(() => MapPins.Count == 1, 8f);
                Check(MapPins.Count == 1 && MapPins.All[0].icon == MapPins.IconTreasure && MapPins.All[0].owner != Net.LocalId, "the guest's pin is on the host's map (" + MapPins.Count + ")");
                yield return WaitVal("pinSeen", 20f);
                if (MapPins.Count == 1) MapPins.Remove(MapPins.All[0].id);
                SendVal("pinGone", "1");
            }
            yield return Sync("S34b", 60f);

            // ---- the passenger bench + the anchor rule
            var boat = Refs.Boat as BoatController;
            var bench = BoatBenchInteractable.Instance;
            Check(boat != null && bench != null, "boat + bench on " + Role);
            if (boat == null || bench == null) yield break;
            if (isHost)
            {
                if (!boat.AtMooring) boat.PlaceAt(boat.MooringPosition, boat.MooringYaw);
                if (!boat.Anchored) boat.ToggleAnchor();
            }
            yield return Wait(1.5f);
            if (!boat.PlayerAboard) boat.BoardFromDock();
            yield return Wait(1f);
            bool anchored0 = boat.Anchored;
            if (!isHost)
            {
                pc.TeleportTo(boat.transform.TransformPoint(boat.Parts.benchExitLocal[0]), boat.transform.eulerAngles.y);
                yield return Wait(0.6f);
                bool sat = bench.SitOn(0);
                yield return Wait(0.4f);
                Check(sat && pc.IsSeated && pc.IsPassenger, "guest sits on the passenger bench");
                GameInput.SimTap(GameAction.Anchor);
                yield return Wait(1f);
                Check(boat.Anchored == anchored0, "the guest's anchor key does nothing (not the driver)");
                SendVal("bench", "1");
                yield return WaitVal("benchSeen", 40f);
                bench.StandUp();
                yield return Wait(0.5f);
                Check(!pc.IsSeated, "guest stood up");
            }
            else
            {
                yield return WaitVal("bench", 40f);
                yield return Wait(1.5f);
                var a = PeerAvatar();
                float d = a != null ? FlatDist(a.transform.position, boat.Parts.benchSeats[0].position) : 99f;
                Check(a != null && d < 0.9f, "the host sees the guest's avatar on the bench seat (" + F(d, "0.00") + " m)");
                Check(bench.Taken(0), "the seat counts as taken on the host");
                Check(boat.Anchored == anchored0, "the anchor did not move (anchored " + boat.Anchored + ")");
                if (a != null) { yield return AimAt(a.transform.position + Vector3.up * 1.1f); yield return Shot("host_S34_guest_on_bench"); }
                SendVal("benchSeen", "1");
            }
            yield return Sync("S34c", 60f);
        }
    }
}
