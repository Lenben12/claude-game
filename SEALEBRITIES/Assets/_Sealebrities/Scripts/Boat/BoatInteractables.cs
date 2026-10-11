using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// "E – Steuern" style prompt with the CURRENT key binding (rebindable controls). Cached per language and binding,
    /// so GetPrompt() may be called every frame without allocating.
    public sealed class KeyPrompt
    {
        readonly GameAction action;
        readonly string de, en;
        string cache, label;
        Language lang;

        public KeyPrompt(GameAction action, string de, string en)
        {
            this.action = action;
            this.de = de;
            this.en = en;
        }

        public string Text
        {
            get
            {
                string l = GameInput.BindingLabel(action);
                if (cache == null || lang != Loc.Current || !ReferenceEquals(l, label))
                {
                    lang = Loc.Current;
                    label = l;
                    cache = l + " – " + Loc.Tr(de, en);
                }
                return cache;
            }
        }
    }

    /// Driver seat / helm console: take the wheel. Only from the deck, within ~2 m of the seat (BoatController.CanEnterHelm).
    /// Co-op: while another player drives (NetFlags.SeatTakenByOther) it shows "Besetzt" and refuses.
    public class BoatHelmInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "Steuern", "Drive");

        public string GetPrompt()
        {
            if (boat == null || boat.PlayerDriving) return null;
            if (boat.HelmOccupied) return Loc.Tr("Besetzt", "Occupied");
            return prompt.Text;
        }

        public bool CanInteract() => boat != null && (boat.CanEnterHelm() || boat.HelmOccupied);

        public void Interact()
        {
            if (boat == null) return;
            if (boat.HelmOccupied)
            {
                GameEvents.RaiseToast(Loc.Tr("Besetzt – jemand anderes steuert.", "Occupied – someone else is driving."));
                return;
            }
            boat.EnterHelm();
        }
    }

    /// Stern dive ladder: climb back aboard from the water.
    public class BoatLadderInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "An Bord klettern", "Climb aboard");

        public string GetPrompt() => boat != null && boat.CanClimbAboard() ? prompt.Text : null;

        public bool CanInteract() => boat != null && boat.CanClimbAboard();

        public void Interact()
        {
            if (boat != null) boat.ClimbAboard();
        }
    }

    /// Trigger volume over the water beside the dock (boat side): "E – board" when the boat is moored close by.
    /// It never starts driving (that only happens at the wheel). Only the player's interactor ray tests triggers,
    /// so this never blocks physics or other probes. It stays clear of the dock ladder at the very end.
    public class BoatDockBoarding : MonoBehaviour, IInteractable
    {
        const float MaxBoatDistance = 15f;

        public BoatController boat;
        Vector3 dockEnd;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "An Bord gehen", "Board the boat");

        public static BoatDockBoarding Create(Transform parent, BoatController boat)
        {
            Vector3 end = WorldInfo.DockEndPosition;
            if (end == Vector3.zero) end = WorldLayout.DockEnd + Vector3.up * PropInfo.DockDeckY;
            Vector3 spawn = WorldInfo.BoatSpawn != Vector3.zero ? WorldInfo.BoatSpawn : WorldLayout.BoatSpawn;
            float side = spawn.z >= end.z ? 1f : -1f;   // the dock runs along +X; the boat is moored on this Z side
            const float halfW = 1.2f, h = 2.7f;

            var go = new GameObject("DockBoarding");
            go.transform.SetParent(parent, false);
            go.transform.position = Vector3.zero;

            // along the dock edge facing the boat (x: end-5 .. end+2, z: edge+0.25 .. edge+2.75) – not in front of the
            // ladder at the dock end, so a swimmer looking at the ladder never hits this volume
            var a = go.AddComponent<BoxCollider>();
            a.isTrigger = true;
            a.center = new Vector3(end.x - 1.5f, end.y + 0.85f, end.z + side * (halfW + 0.25f + 1.25f));
            a.size = new Vector3(7f, h, 2.5f);

            var bd = go.AddComponent<BoatDockBoarding>();
            bd.boat = boat;
            bd.dockEnd = end;
            return bd;
        }

        public string GetPrompt() => CanInteract() ? prompt.Text : null;

        public bool CanInteract()
        {
            if (boat == null || !boat.CanBoardFromDock()) return false;
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
        readonly KeyPrompt raise = new KeyPrompt(GameAction.Interact, "Anker lichten", "Raise anchor");
        readonly KeyPrompt drop = new KeyPrompt(GameAction.Interact, "Anker werfen", "Drop anchor");

        public string GetPrompt()
        {
            if (boat == null) return null;
            return boat.Anchored ? raise.Text : drop.Text;
        }

        public bool CanInteract()
        {
            var p = Refs.Player;
            if (boat == null || p == null || (p is Object o && o == null)) return false;
            return GameManager.I != null && GameManager.I.InGame && !p.IsInWater && !p.IsSeated && boat.PlayerAboard;
        }

        public void Interact()
        {
            if (boat != null && CanInteract()) boat.ToggleAnchor();
        }
    }

    /// The ladder at the end of the dock – THE way out of the water there (feedback #4: the old ladder was decoration).
    /// A thin trigger slab at the ladder (only the interactor ray sees it); E while swimming / diving at the ladder
    /// puts the player on the dock planks. When the camera is inside the slab (pressed against the ladder) the ray
    /// cannot hit it, so a proximity fallback shows the same prompt then. Uses the props ladder
    /// (PropInfo.DockLadderTop/Bottom) and builds a simple ladder itself when the props did not. If another module
    /// already provides an interactable ladder there, none is created (exactly one working way).
    [DefaultExecutionOrder(75)]
    public class DockLadder : MonoBehaviour, IInteractable
    {
        Vector3 top, bottom;
        float exitYaw;
        string written;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "Leiter hoch", "Climb the ladder");

        public static DockLadder Create(Transform parent)
        {
            Vector3 axis = WorldLayout.DockEnd - WorldLayout.DockStart;
            axis.y = 0f;
            axis = axis.sqrMagnitude > 1e-4f ? axis.normalized : Vector3.right;

            Vector3 end = WorldInfo.DockEndPosition;
            if (end == Vector3.zero) end = WorldLayout.DockEnd + Vector3.up * PropInfo.DockDeckY;
            bool propsLadder = PropInfo.DockLadderTop != Vector3.zero;
            Vector3 top = propsLadder ? PropInfo.DockLadderTop : end - axis * 0.4f;
            Vector3 bottom = propsLadder && PropInfo.DockLadderBottom != Vector3.zero ? PropInfo.DockLadderBottom : top + axis * 0.95f;

            // stand exactly on the planks
            float deck = BoatSurface.Below(new Vector3(top.x, Mathf.Max(top.y, Waves.SeaLevel) + 3f, top.z), 8f, out bool water);
            top.y = water ? Mathf.Max(top.y, PropInfo.DockDeckY) : deck;

            // the ladder rails stand ~0.5 m beyond the top point (props: x1 + 0.1 with top at x1 − 0.4)
            Vector3 ladder = new Vector3(top.x, 0f, top.z) + axis * 0.5f;

            var go = new GameObject("DockLadderClimb");
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(new Vector3(ladder.x, 0f, ladder.z), Quaternion.LookRotation(axis, Vector3.up));
            if (!propsLadder) BuildLadderMesh(go.transform, top.y);

            // the Player module's animated climb uses the props ladder (PropInfo) – then it is THE way and we add none
            if ((propsLadder && PlayerClimbsLadders()) || ProvidedElsewhere(new Vector3(ladder.x, Waves.SeaLevel, ladder.z)))
            {
                Debug.Log("Sealebrities: the dock ladder climb is provided by another module – the boat module adds none.");
                return null;
            }
            // thin trigger slab just seaward of the rails: from 2.4 m below the surface to just above the deck
            float y0 = Waves.SeaLevel - 2.4f, y1 = top.y + 0.35f;
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.center = new Vector3(0f, (y0 + y1) * 0.5f, 0.1f);
            bc.size = new Vector3(1.6f, y1 - y0, 0.14f);

            var dl = go.AddComponent<DockLadder>();
            dl.top = top + Vector3.up * 0.05f;
            dl.bottom = bottom;
            dl.exitYaw = Mathf.Atan2(-axis.x, -axis.z) * Mathf.Rad2Deg;   // facing inland
            return dl;
        }

        /// True when the Player module climbs the dock ladder itself (animated climb, "CanLadderHint"). Checked by name
        /// so this module compiles against any version of the player controller.
        static bool PlayerClimbsLadders()
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var t = typeof(PlayerController);
            try { return t.GetProperty("CanLadderHint", F) != null || t.GetMethod("UpdateLadder", F) != null; }
            catch (System.Reflection.AmbiguousMatchException) { return true; }
        }

        /// True when an interactable of another module (e.g. a props ladder) already sits at the ladder.
        static bool ProvidedElsewhere(Vector3 p)
        {
            var hits = Physics.OverlapSphere(p, 2.5f, ~0, QueryTriggerInteraction.Collide);
            var found = new List<MonoBehaviour>();
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] == null) continue;
                found.Clear();
                hits[i].GetComponentsInParent(true, found);
                for (int k = 0; k < found.Count; k++)
                {
                    var mb = found[k];
                    if (!(mb is IInteractable)) continue;
                    if (mb is DockLadder || mb is BoatDockBoarding || mb is FishingSpot) continue;
                    string n = mb.GetType().Name;
                    if (n.IndexOf("Ladder", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Leiter", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        /// Fallback ladder when the props module built none: two metal rails into the water with rungs every 0.3 m.
        static void BuildLadderMesh(Transform t, float deckY)
        {
            var s = new BoatMeshSet();
            var m = s[BoatMats.Metal];
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 0.38f;
                m.Cylinder(new Vector3(x, -2.4f, 0f), new Vector3(x, deckY + 0.85f, 0f), 0.035f, 0.035f, 6);
                m.Cylinder(new Vector3(x, deckY + 0.85f, 0f), new Vector3(x, deckY + 0.85f, -0.38f), 0.035f, 0.035f, 6);
                m.Cylinder(new Vector3(x, deckY + 0.85f, -0.38f), new Vector3(x, deckY, -0.38f), 0.035f, 0.035f, 6);
            }
            for (float y = -2.2f; y < deckY - 0.05f; y += 0.3f)
                m.Cylinder(new Vector3(-0.38f, y, 0f), new Vector3(0.38f, y, 0f), 0.028f, 0.028f, 6);
            s.Build("DockLadderMesh", t, true);
        }

        public string GetPrompt() => CanInteract() ? prompt.Text : null;

        public bool CanInteract()
        {
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || (p is Object o && o == null)) return false;
            return p.IsInWater && !p.IsSeated && !p.IsFainting;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            var p = Refs.Player;
            BoatSynth.PlayAt(BoatSynth.Splash, new Vector3(bottom.x, Waves.SeaLevel, bottom.z), 0.4f, 1.25f, 25f);
            p.TeleportTo(top, exitYaw);   // leaving the water banks the dive footage; air refills after a short rest
            BoatParticles.Puff(top + Vector3.up * 0.3f, new Color(0.85f, 0.93f, 1f, 0.6f), 10, 0.25f);
            ReleasePrompt();
        }

        /// Proximity fallback (runs after the player's interactor this frame, so E is never handled twice).
        void LateUpdate()
        {
            var p = Refs.Player;
            bool near = false;
            if (CanInteract() && !UIState.BlocksGameplay && p.Root != null)
            {
                Vector3 lp = transform.InverseTransformPoint(p.Root.position);
                near = Mathf.Abs(lp.x) < 0.95f && lp.z > -0.45f && lp.z < 0.9f;
            }
            string cur = InteractionPrompt.Text;
            if (!near || (cur != null && !ReferenceEquals(cur, written)))
            {
                ReleasePrompt();
                return;
            }
            // here the prompt is empty or ours (the interactor clears it every frame when its ray finds nothing)
            if (written != null && GameInput.InteractPressed)
            {
                Interact();
                return;
            }
            written = prompt.Text;
            InteractionPrompt.Text = written;
        }

        void ReleasePrompt()
        {
            if (written != null && ReferenceEquals(InteractionPrompt.Text, written)) InteractionPrompt.Text = null;
            written = null;
        }

        void OnDestroy() => ReleasePrompt();
    }
}
