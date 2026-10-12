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
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Besetzt", "Occupied"), Loc.Tr("jemand steuert", "someone is driving"));
                return;
            }
            boat.EnterHelm();
        }
    }

    /// Stern dive ladder: climb back aboard from the water (with heavy salvage in your arms it is delivered on deck, K.3).
    public class BoatLadderInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "An Bord klettern", "Climb aboard");
        readonly KeyPrompt promptSalvage = new KeyPrompt(GameAction.Interact, "Fund an Bord bringen", "Bring the find aboard");

        public string GetPrompt() => boat != null && boat.CanClimbAboard() ? (Carry.IsHeavy ? promptSalvage.Text : prompt.Text) : null;

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

    /// E.1 the boat's cooler box: snacks from the shared pantry. Opens the UI food panel (FoodSpots.OpenHandler) or,
    /// without one, eats the most useful snack directly.
    public class BoatCoolerInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;
        string cache;
        int cacheCount = -1;
        Language cacheLang;
        string cacheKey;

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            int n = Pantry.TotalCount;
            string key = GameInput.BindingLabel(GameAction.Interact);
            if (cache == null || n != cacheCount || cacheLang != Loc.Current || !ReferenceEquals(key, cacheKey))
            {
                cacheCount = n;
                cacheLang = Loc.Current;
                cacheKey = key;
                cache = n > 0
                    ? key + Loc.Tr(" – Kühlbox (" + n + " Snacks)", " – Cooler (" + n + " snacks)")
                    : key + Loc.Tr(" – Kühlbox (leer)", " – Cooler (empty)");
            }
            return cache;
        }

        public bool CanInteract()
        {
            var p = Refs.Player;
            if (boat == null || p == null || (p is Object o && o == null)) return false;
            return GameManager.I != null && GameManager.I.InGame && !p.IsInWater && !p.IsSeated && boat.PlayerAboard;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            BoatSynth.PlayAt(BoatSynth.Thunk, transform.position, 0.25f, 1.6f, 10f);
            if (FoodSpots.Open(FoodSpots.Boat)) return;
            FoodSpots.EatNext();
        }
    }

    /// The whole hull (colliders): "E – An Bord gehen" when the boat lies next to a dock, the village jetty or a
    /// beach and the player stands on land within reach (L.8 second landing point – works at any jetty).
    public class BoatBoardInteractable : MonoBehaviour, IInteractable
    {
        const float Reach = 4.5f;
        public BoatController boat;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "An Bord gehen", "Board the boat");

        public string GetPrompt() => CanInteract() ? prompt.Text : null;

        public bool CanInteract()
        {
            if (boat == null || !boat.CanBoardFromDock()) return false;
            var p = Refs.Player;
            if (p == null || p.Root == null) return false;
            Vector3 feet = p.Root.position;
            if (feet.y < Waves.HeightAt(feet.x, feet.z) - 0.1f) return false;
            Vector3 lp = boat.transform.InverseTransformPoint(feet);
            float dx = Mathf.Max(0f, Mathf.Abs(lp.x) - 1.3f), dz = Mathf.Max(0f, lp.z - 3.8f) + Mathf.Max(0f, -4.4f - lp.z);
            return dx * dx + dz * dz < Reach * Reach;
        }

        public void Interact()
        {
            if (CanInteract()) boat.BoardFromDock();
        }
    }

    /// Getting off the boat anywhere it lies alongside something solid (the dock, the village jetty, a beach): while
    /// the local player stands on deck at a rail and looks over it toward land within ~6.5 m, the prompt reads
    /// "E – Aussteigen" and E puts them there. A proximity prompt like DockLadder (the interactor ray only sees the
    /// rail): written only while nobody else owns the prompt. Probes run 5× per second, never per frame.
    [DefaultExecutionOrder(76)]
    public class BoatDisembark : MonoBehaviour
    {
        BoatController boat;
        readonly KeyPrompt prompt = new KeyPrompt(GameAction.Interact, "Aussteigen", "Get off");
        string written;
        float probe;
        bool hasSpot;
        Vector3 spot;
        float spotYaw;
        static readonly float[] ProbeZ = { -2.6f, -1.2f, 0.4f, 1.8f };

        public void Init(BoatController b) { boat = b; }

        void LateUpdate()
        {
            var p = Refs.Player;
            var gm = GameManager.I;
            bool can = boat != null && gm != null && gm.InGame && p != null && !(p is Object o && o == null) && p.Root != null && p.View != null
                       && !UIState.BlocksGameplay && !p.IsInWater && !p.IsSeated && !p.IsFainting && boat.PlayerAboard && !boat.PlayerDriving;
            if (!can)
            {
                hasSpot = false;
                Release();
                return;
            }
            probe -= Time.deltaTime;
            if (probe <= 0f)
            {
                probe = 0.2f;
                hasSpot = FindSpot(p, out spot, out spotYaw);
            }
            string cur = InteractionPrompt.Text;
            if (!hasSpot || (cur != null && !ReferenceEquals(cur, written)))
            {
                Release();
                return;
            }
            if (written != null && GameInput.InteractPressed)
            {
                p.TeleportTo(spot, spotYaw);
                BoatSynth.PlayAt(BoatSynth.Thunk, spot, 0.4f, 1.2f, 20f);
                hasSpot = false;
                Release();
                return;
            }
            written = prompt.Text;
            InteractionPrompt.Text = written;
        }

        /// The player stands at a rail and looks over it: is there land (≤ 1.6 m below / ≤ 1.2 m above the deck) out there?
        bool FindSpot(IPlayer p, out Vector3 at, out float yaw)
        {
            at = Vector3.zero;
            yaw = 0f;
            Transform bt = boat.transform;
            Vector3 lp = bt.InverseTransformPoint(p.Root.position);
            Vector3 look = p.View.transform.forward;
            look.y = 0f;
            if (look.sqrMagnitude < 1e-4f) return false;
            look.Normalize();
            for (int side = -1; side <= 1; side += 2)
            {
                // near this rail and looking over it
                if (side * lp.x < 0.15f) continue;
                Vector3 outward = bt.right * side;
                outward.y = 0f;
                outward.Normalize();
                if (Vector3.Dot(look, outward) < 0.45f) continue;
                float deckY = p.Root.position.y;
                for (int zi = 0; zi < ProbeZ.Length; zi++)
                {
                    if (Mathf.Abs(ProbeZ[zi] - lp.z) > 1.6f) continue;
                    for (float d = 1.6f; d <= 6.6f; d += 0.5f)
                    {
                        Vector3 q = bt.TransformPoint(new Vector3(side * 1.1f, 0f, ProbeZ[zi])) + outward * d;
                        float y = BoatSurface.Below(new Vector3(q.x, deckY + 3f, q.z), 6f, out bool water);
                        if (water) continue;
                        if (y < deckY - 1.6f || y > deckY + 1.2f) continue;
                        if (BoatHitsBoat(q)) continue;
                        at = new Vector3(q.x, y + 0.05f, q.z);
                        yaw = Mathf.Atan2(outward.x, outward.z) * Mathf.Rad2Deg;
                        return true;
                    }
                }
            }
            return false;
        }

        bool BoatHitsBoat(Vector3 q)
        {
            Vector3 lq = boat.transform.InverseTransformPoint(q);
            return Mathf.Abs(lq.x) < 1.4f && lq.z > -4.6f && lq.z < 4.1f;
        }

        void Release()
        {
            if (written != null && ReferenceEquals(InteractionPrompt.Text, written)) InteractionPrompt.Text = null;
            written = null;
        }

        void OnDestroy() => Release();
    }

    /// Anchor winch on the foredeck. Round 3 (GDD §19 „Nur der Fahrer kann den Anker legen“): the anchor is worked from the
    /// wheel only (Q at the helm) – at the winch the prompt just says so.
    public class BoatAnchorInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;
        string cache, cacheKey;
        Language cacheLang;

        public string GetPrompt()
        {
            if (boat == null || !CanInteract()) return null;
            string q = GameInput.BindingLabel(GameAction.Anchor);
            if (cache == null || cacheLang != Loc.Current || !ReferenceEquals(q, cacheKey))
            {
                cacheLang = Loc.Current;
                cacheKey = q;
                cache = Loc.Tr("Anker: nur am Steuer (" + q + ")", "Anchor: from the wheel only (" + q + ")");
            }
            return cache;
        }

        public bool CanInteract()
        {
            var p = Refs.Player;
            if (boat == null || p == null || (p is Object o && o == null)) return false;
            return GameManager.I != null && GameManager.I.InGame && !p.IsInWater && !p.IsSeated && boat.PlayerAboard;
        }

        public void Interact()
        {
            if (boat == null || !CanInteract()) return;
            BoatController.NotDriverHint();
        }
    }

    /// Round 3 (GDD §19 „Ein zweiter Sitzplatz, z. B. eine Sitzbank vorne“, Q36 „Beifahrer: Filmen + Karte ansehen“): the
    /// passenger bench on the foredeck. „E – Hinsetzen“ takes the free seat nearest to you (two seats; a seat with another
    /// crew member on it counts as taken). Seated here the hands stay free (SeatRules: camera, phone, map), the view turns
    /// ±150°, E or Space stands you up next to the bench. Only the driver works the anchor.
    public class BoatBenchInteractable : MonoBehaviour, IInteractable
    {
        public BoatController boat;
        public BoatParts parts;
        readonly KeyPrompt sit = new KeyPrompt(GameAction.Interact, "Hinsetzen", "Sit down");
        string seatedPrompt, spE, spM;
        Language spLang;
        int seatedIndex = -1;
        float seatedAt = -10f, leftAt = -10f;

        public static BoatBenchInteractable Instance { get; private set; }

        void Awake() { Instance = this; }
        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            ReleasePrompt();
        }

        /// The local player sits on this bench (seat 0/1), −1 = not.
        public int LocalSeat => seatedIndex;

        static IPlayer Local
        {
            get
            {
                var p = Refs.Player;
                return p == null || (p is Object o && o == null) ? null : p;
            }
        }

        /// Another crew member sits on seat i (their avatar root is on the seat).
        public bool Taken(int i)
        {
            if (parts == null || parts.benchSeats == null || i < 0 || i >= parts.benchSeats.Length || parts.benchSeats[i] == null) return true;
            Vector3 s = parts.benchSeats[i].position;
            var me = Local;
            Transform mine = me != null ? me.Root : null;
            var all = Divers.All;
            for (int k = 0; k < all.Count; k++)
            {
                var t = all[k];
                if (t == null || t == mine) continue;
                Vector3 d = t.position - s;
                if (Mathf.Abs(d.y) < 1.2f && d.x * d.x + d.z * d.z < 0.4f * 0.4f) return true;
            }
            return false;
        }

        int FreeSeatNear(Vector3 from)
        {
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < 2; i++)
            {
                if (Taken(i)) continue;
                float d = (parts.benchSeats[i].position - from).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            var p = Local;
            if (FreeSeatNear(p.Root.position) < 0) return Loc.Tr("Besetzt", "Occupied");
            return sit.Text;
        }

        public bool CanInteract()
        {
            var p = Local;
            if (boat == null || parts == null || parts.benchSeats == null || p == null || p.Root == null) return false;
            if (Time.time - leftAt < 0.4f) return false;
            return GameManager.I != null && GameManager.I.InGame && !p.IsInWater && !p.IsSeated && !p.IsFainting && boat.PlayerAboard && !boat.PlayerDriving;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            var p = Local;
            int i = FreeSeatNear(p.Root.position);
            if (i < 0)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Besetzt", "Occupied"), null);
                return;
            }
            SitOn(i);
        }

        /// Puts the local player on seat i (tests call it directly). False when the seat is taken / not possible.
        public bool SitOn(int i)
        {
            var p = Local;
            if (p == null || parts == null || parts.benchSeats == null || i < 0 || i >= parts.benchSeats.Length || Taken(i) || p.IsSeated) return false;
            p.SetSeated(parts.benchSeats[i]);
            seatedIndex = i;
            seatedAt = Time.time;
            BoatSynth.PlayAt(BoatSynth.Thunk, parts.benchSeats[i].position, 0.25f, 1.4f, 12f);
            return true;
        }

        /// Stands the local player up next to the bench.
        public void StandUp()
        {
            var p = Local;
            int i = seatedIndex;
            seatedIndex = -1;
            leftAt = Time.time;
            ReleasePrompt();
            if (p == null || !p.IsSeated || parts == null || parts.benchExitLocal == null || i < 0) return;
            p.ClearSeated(boat.transform.TransformPoint(parts.benchExitLocal[Mathf.Clamp(i, 0, parts.benchExitLocal.Length - 1)]));
        }

        string SeatedPrompt()
        {
            string e = GameInput.BindingLabel(GameAction.Interact), m = GameInput.BindingLabel(GameAction.Map);
            if (seatedPrompt == null || spLang != Loc.Current || !ReferenceEquals(e, spE) || !ReferenceEquals(m, spM))
            {
                spLang = Loc.Current;
                spE = e; spM = m;
                seatedPrompt = Loc.Tr(e + " – Aufstehen · " + m + " – Karte", e + " – Get up · " + m + " – Map");
            }
            return seatedPrompt;
        }

        void ReleasePrompt()
        {
            if (seatedPrompt != null && ReferenceEquals(InteractionPrompt.Text, seatedPrompt)) InteractionPrompt.Text = null;
        }

        void Update()
        {
            if (seatedIndex < 0) return;
            var p = Local;
            bool stillHere = p != null && p.IsSeated && parts != null && parts.benchSeats != null
                             && SeatRules.LocalPassenger && p.Root != null
                             && (p.Root.position - parts.benchSeats[seatedIndex].position).sqrMagnitude < 0.05f;
            if (!stillHere)
            {
                // teleported / fainted / sat elsewhere: forget the seat
                seatedIndex = -1;
                ReleasePrompt();
                return;
            }
            bool input = GameManager.I != null && GameManager.I.InGame && !UIState.BlocksGameplay;
            if (input && Time.time - seatedAt > 0.3f && (GameInput.InteractPressed || GameInput.JumpPressed))
            {
                StandUp();
                return;
            }
            if (input && !UIState.MapOverlayOpen) InteractionPrompt.Text = SeatedPrompt();
            else ReleasePrompt();
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
