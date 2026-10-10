using UnityEngine;

namespace Sealebrities
{
    /// Driver seat / helm console: take the wheel.
    public class BoatHelmInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;

        public string GetPrompt()
        {
            if (boat == null || boat.PlayerDriving) return null;
            return Loc.Tr("E – Steuern", "E – Drive");
        }

        public bool CanInteract() => boat != null && boat.CanEnterHelm();

        public void Interact()
        {
            if (boat != null) boat.EnterHelm();
        }
    }

    /// Stern dive ladder: climb back aboard from the water.
    public class BoatLadderInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;

        public string GetPrompt()
        {
            if (boat == null || !boat.CanClimbAboard()) return null;
            return Loc.Tr("E – An Bord klettern", "E – Climb aboard");
        }

        public bool CanInteract() => boat != null && boat.CanClimbAboard();

        public void Interact()
        {
            if (boat != null) boat.ClimbAboard();
        }
    }

    /// Trigger volumes over the water at the dock end (boat side and end): "E – board" when the boat is moored close by.
    /// Only the player's interactor ray tests triggers, so these never block physics or other probes.
    public class BoatDockBoarding : MonoBehaviour, IInteractable
    {
        const float MaxBoatDistance = 15f;

        public BoatController boat;
        public FishingSpot fishing;
        Vector3 dockEnd;

        public static BoatDockBoarding Create(Transform parent, BoatController boat, FishingSpot fishing)
        {
            Vector3 end = WorldInfo.DockEndPosition;
            if (end == Vector3.zero) end = WorldLayout.DockEnd + Vector3.up * 1.3f;
            Vector3 spawn = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawn : WorldLayout.BoatSpawn;
            float side = spawn.z >= end.z ? 1f : -1f;   // the dock runs along +X; the boat is moored on this Z side
            const float halfW = 1.2f, h = 2.7f;

            var go = new GameObject("DockBoarding");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;

            // along the dock edge facing the boat (x: end-5 .. end, z: edge+0.25 .. edge+2.75)
            var a = go.AddComponent<BoxCollider>();
            a.isTrigger = true;
            a.center = new Vector3(end.x - 2.5f, end.y + 0.85f, end.z + side * (halfW + 0.25f + 1.25f));
            a.size = new Vector3(5f, h, 2.5f);

            // beyond the dock end (x: end+0.25 .. end+2.25, z: dock middle .. edge+2.75)
            var b = go.AddComponent<BoxCollider>();
            b.isTrigger = true;
            b.center = new Vector3(end.x + 1.25f, end.y + 0.85f, end.z + side * (halfW + 2.75f) * 0.5f);
            b.size = new Vector3(2f, h, halfW + 2.75f);

            var bd = go.AddComponent<BoatDockBoarding>();
            bd.boat = boat;
            bd.fishing = fishing;
            bd.dockEnd = end;
            return bd;
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return Loc.Tr("E – An Bord gehen", "E – Board the boat");
        }

        public bool CanInteract()
        {
            if (boat == null || !boat.CanBoardFromDock()) return false;
            if (fishing != null && fishing.SessionActive) return false;   // E reels in while fishing
            Vector3 d = boat.transform.position - dockEnd;
            d.y = 0f;
            return d.sqrMagnitude < MaxBoatDistance * MaxBoatDistance;
        }

        public void Interact()
        {
            if (CanInteract()) boat.BoardFromDock();
        }
    }

    /// Anchor winch on the foredeck: drop or raise the anchor.
    public class BoatAnchorInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;

        public string GetPrompt()
        {
            if (boat == null) return null;
            return boat.Anchored ? Loc.Tr("E – Anker lichten", "E – Raise anchor") : Loc.Tr("E – Anker werfen", "E – Drop anchor");
        }

        public bool CanInteract()
        {
            var p = Refs.Player;
            return boat != null && p != null && GameManager.I != null && GameManager.I.InGame && !p.IsInWater && !p.IsSeated;
        }

        public void Interact()
        {
            if (boat != null && CanInteract()) boat.ToggleAnchor();
        }
    }
}
