using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    // =============================================================================================
    // Round-2 boat helpers: compressor (E.4), snack spot (E.1), salvage delivery + winch (K.3),
    // the route to the Fog Isle (K.8) and the boat flag (L.7 / K.7).
    // =============================================================================================

    /// E.4 air refill on the boat – only with the crew's "Boot-Kompressor" (GearSlot.Compressor, 600 $, 250 F).
    /// The hut zone and the dock refill for free (Player module); the Player module asks this class for the boat.
    public static class BoatAirStation
    {
        /// E.4: a tank fills from empty to full in 6 s on deck.
        public const float SecondsToFull = 6f;

        static BoatController Boat
        {
            get
            {
                var b = Refs.Boat as BoatController;
                return b != null ? b : null;
            }
        }

        /// The crew owns the boat compressor.
        public static bool Owned { get { var b = Boat; return b != null && b.HasCompressor; } }

        /// True when a diver whose feet are at this world position stands on the deck of a boat with the compressor.
        public static bool CanRefillAt(Vector3 feet)
        {
            var b = Boat;
            return b != null && b.HasCompressor && b.IsOnDeck(feet);
        }

        /// The local player stands on the deck and the compressor is owned (Player module: refill zone "boat").
        public static bool LocalCanRefill
        {
            get
            {
                var b = Boat;
                return b != null && b.HasCompressor && b.PlayerAboard;
            }
        }

        /// The compressor runs for the local player right now (animation + chug sound).
        internal static bool LocalRefillingHere(BoatController bc)
        {
            if (bc == null || !bc.HasCompressor || !bc.PlayerAboard) return false;
            var pc = Refs.Player as PlayerController;
            return pc != null && pc.RefillingAir && pc.Gear.hasTank;
        }
    }

    /// E.1 eat spots: the hut fridge (props) and the boat's cooler box (this module). The UI module's food panel
    /// (UIFoodPanel: icon, name, count, buff – click to eat) registers OpenHandler; without it the spot eats the
    /// most useful snack right away (FoodSpots.EatNext) so the pantry always works.
    public static class FoodSpots
    {
        public const string Boat = "boat", Fridge = "fridge";

        /// (spot id) → true when the panel opened. Set by the UI module.
        public static System.Func<string, bool> OpenHandler;

        public static bool Open(string spotId) => OpenHandler != null && OpenHandler(spotId);

        /// Eats one snack for the local player: one whose buff is not running yet first, the strongest otherwise.
        public static bool EatNext()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return false;
            var pantry = gm.State.pantry;
            FoodDef best = null;
            float bestScore = -1f;
            for (int i = 0; i < pantry.Count; i++)
            {
                var it = pantry[i];
                if (it == null || it.count <= 0) continue;
                var f = FoodDatabase.Get(it.foodId);
                if (f == null) continue;
                float score = f.value + (Buffs.Has(f.buff) ? 0f : 1f);
                if (score > bestScore) { bestScore = score; best = f; }
            }
            if (best == null)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Keine Snacks an Bord", "No snacks aboard"), Loc.Tr("Chopper Shop", "Chopper Shop"));
                return false;
            }
            if (!Pantry.TryEat(best.id)) return false;
            GameEvents.RaiseNotify(NotifyKind.Info, best.Name, BuffChip(best));
            return true;
        }

        /// Short buff chip for a notification ("Luft +10 %").
        public static string BuffChip(FoodDef f)
        {
            if (f == null) return null;
            string pct = Mathf.RoundToInt(f.value * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + " %";
            switch (f.buff)
            {
                case BuffType.AirCapacity: return Loc.Tr("Luft +", "Air +") + pct;
                case BuffType.SwimSpeed: return Loc.Tr("Schwimmen +", "Swim +") + pct;
                case BuffType.SteadyHands: return Loc.Tr("Ruhige Hände", "Steady hands");
                case BuffType.BumpResist: return Loc.Tr("Rempler −", "Bumps −") + pct;
                case BuffType.NightEyes: return Loc.Tr("Tiefensicht +", "Deep sight +") + pct;
            }
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OpenHandler = null; }
    }

    /// K.3: heavy salvage the local player carries (Carry.IsHeavy, "salv:…") is delivered as soon as they stand on the
    /// boat deck (climbing the stern ladder) or on the dock – Finds.Deliver puts it into the „Funde“ tab.
    public sealed class SalvageDelivery : MonoBehaviour
    {
        float poll;

        public static SalvageDelivery Create(Transform parent)
        {
            var go = new GameObject("SalvageDelivery");
            go.transform.SetParent(parent, false);
            return go.AddComponent<SalvageDelivery>();
        }

        /// Delivers the carried salvage (if any). Returns true when something was delivered.
        public static bool DeliverCarried(Vector3 at)
        {
            if (!Carry.IsHeavy) return false;
            string id = Carry.Current;
            bool ok = Finds.Deliver(id);
            if (!ok && !Finds.IsDelivered(id)) return false;
            Carry.Drop();
            BoatParticles.Puff(at + Vector3.up * 0.5f, new Color(0.9f, 0.95f, 1f, 0.75f), 14, 0.3f);
            BoatSynth.PlayAt(BoatSynth.Thunk, at, 0.75f, 0.85f, 30f);
            return ok;
        }

        void Update()
        {
            poll -= Time.deltaTime;
            if (poll > 0f) return;
            poll = 0.25f;
            if (!Carry.IsHeavy) return;
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || (p is Object o && o == null) || p.Root == null) return;
            if (p.IsInWater || p.IsUnderwater || p.IsFainting) return;
            var boat = Refs.Boat;
            bool aboard = boat != null && !(boat is Object bo && bo == null) && boat.PlayerAboard;
            Vector3 feet = p.Root.position;
            if (aboard || OnDock(feet)) DeliverCarried(feet);
        }

        static bool OnDock(Vector3 feet)
        {
            if (feet.y < Waves.SeaLevel + 0.4f) return false;
            Vector3 a = WorldLayout.DockStart, b = WorldLayout.DockEnd;
            Vector2 p = new Vector2(feet.x, feet.z), s = new Vector2(a.x, a.z), e = new Vector2(b.x, b.z);
            Vector2 d = e - s;
            float t = Mathf.Clamp01(Vector2.Dot(p - s, d) / Mathf.Max(1e-4f, d.sqrMagnitude));
            return (p - (s + d * t)).sqrMagnitude < 2.4f * 2.4f;
        }
    }

    /// K.3 "Bootswinde" (boat winch, crew equipment): E on a heavy salvage item anywhere ≤ 40 m from the boat hooks
    /// it (the props module's salvage interactable calls BoatWinch.Hook instead of Carry.Pick when CanHook is true);
    /// a yellow lift bag marks it, and as soon as the local player is back on deck the davit cranks it aboard and
    /// Finds.Deliver books it. Per machine: whoever hooked it winches it.
    public static class BoatWinch
    {
        public const float Range = Carry.WinchRange;

        internal static BoatWinchRunner Runner;

        static BoatController Boat
        {
            get
            {
                var b = Refs.Boat as BoatController;
                return b != null ? b : null;
            }
        }

        /// The crew owns the winch.
        public static bool Available { get { var b = Boat; return b != null && b.HasWinch; } }

        /// The winch can hook an item lying here (owned and ≤ 40 m from the boat, horizontally).
        public static bool CanHook(Vector3 itemPos)
        {
            var b = Boat;
            if (b == null || !b.HasWinch) return false;
            Vector3 d = itemPos - b.transform.position;
            d.y = 0f;
            return d.sqrMagnitude <= Range * Range;
        }

        /// Hooks a heavy find ("salv:…" id). The winch takes `visual` over (parented to its lift bag, destroyed when
        /// aboard); pass null and it builds a netted bundle itself. Returns false when it cannot hook it.
        public static bool Hook(string findId, Vector3 itemPos, GameObject visual = null)
        {
            if (string.IsNullOrEmpty(findId) || Runner == null || !CanHook(itemPos)) return false;
            if (Runner.IsHooked(findId)) return true;
            if (!Finds.IsTaken(findId) && !Finds.Take(findId)) return false;
            Runner.Add(findId, itemPos, visual);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Haken dran", "Hooked"), Loc.Tr("Winde zieht an Bord", "Winch on deck"));
            return true;
        }

        /// Items waiting for the winch (lift bags in the water).
        public static int Pending => Runner != null ? Runner.Count : 0;
    }

    internal sealed class BoatWinchRunner : MonoBehaviour
    {
        enum Phase { Waiting, Winching, Swinging }

        sealed class Item
        {
            public string id;
            public Vector3 seabed;
            public Transform bundle, bag;
            public LineRenderer bagRope;
            public Phase phase;
            public float t;
            public Vector3 swingFrom;
        }

        readonly List<Item> items = new List<Item>();
        LineRenderer cable;
        readonly Vector3[] ropePts = new Vector3[2];
        readonly Vector3[] cablePts = new Vector3[2];
        AudioSource ratchet;

        public int Count => items.Count;

        public static BoatWinchRunner Create(Transform parent)
        {
            var go = new GameObject("BoatWinch");
            go.transform.SetParent(parent, false);
            var r = go.AddComponent<BoatWinchRunner>();
            r.cable = NewRope(go.transform, "WinchCable", 0.018f, new Color(0.24f, 0.22f, 0.26f));
            r.ratchet = BoatSynth.LoopSource(go, BoatSynth.Ratchet, 2f, 30f, AudioRolloffMode.Linear);
            BoatWinch.Runner = r;
            GameEvents.GameStarted += r.Clear;
            GameEvents.ReturnedToMenu += r.Clear;
            return r;
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= Clear;
            GameEvents.ReturnedToMenu -= Clear;
            if (BoatWinch.Runner == this) BoatWinch.Runner = null;
        }

        static LineRenderer NewRope(Transform parent, string name, float width, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.numCapVertices = 1;
            lr.sharedMaterial = MaterialLibrary.Unlit(c);
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        public bool IsHooked(string id)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].id == id) return true;
            return false;
        }

        public void Add(string id, Vector3 pos, GameObject visual)
        {
            var it = new Item { id = id, seabed = pos, phase = Phase.Waiting };
            var root = new GameObject("WinchSalvage_" + id).transform;
            root.SetParent(transform, false);
            root.position = pos;
            if (visual != null)
            {
                visual.transform.SetParent(root, true);
            }
            else
            {
                // netted bundle: a dark crate-ish lump in a rope net
                var s = new BoatMeshSet();
                s[BoatMats.Pal("#5C5A69")].Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.6f, 0.5f, 0.45f), Quaternion.Euler(0f, 20f, 6f));
                s[BoatMats.Rope].Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.63f, 0.06f, 0.48f), Quaternion.Euler(0f, 20f, 6f));
                s[BoatMats.Rope].Box(new Vector3(0f, 0.25f, 0f), new Vector3(0.06f, 0.53f, 0.48f), Quaternion.Euler(0f, 20f, 6f));
                BoatOwnedMesh.Own(s.Build("Bundle", root, true));
            }
            it.bundle = root;
            // yellow lift bag at the surface above it
            var bs = new BoatMeshSet();
            bs[BoatMats.Pal("#F2C94C")].Ellipsoid(new Vector3(0f, 0.25f, 0f), new Vector3(0.28f, 0.38f, 0.28f), Quaternion.identity, 8, 5);
            bs[BoatMats.Pal("#EE7F2D")].Cylinder(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 0.7f, 0f), 0.06f, 0.04f, 6);
            var bag = BoatOwnedMesh.Own(bs.Build("LiftBag", transform, true)).transform;
            bag.position = new Vector3(pos.x, Waves.HeightAt(pos.x, pos.z) - 0.2f, pos.z);
            it.bag = bag;
            it.bagRope = NewRope(bag, "BagRope", 0.012f, new Color(0.79f, 0.71f, 0.54f));
            items.Add(it);
            BoatSynth.PlayAt(BoatSynth.Splash, bag.position, 0.4f, 1.4f, 30f);
        }

        void Clear()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].bundle != null) Destroy(items[i].bundle.gameObject);
                if (items[i].bag != null) Destroy(items[i].bag.gameObject);
            }
            items.Clear();
            if (cable != null) cable.enabled = false;
            if (ratchet != null && ratchet.isPlaying) ratchet.Stop();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || items.Count == 0)
            {
                if (cable != null && cable.enabled) cable.enabled = false;
                if (ratchet != null && ratchet.isPlaying) ratchet.Stop();
                return;
            }
            var boat = Refs.Boat as BoatController;
            bool haveBoat = boat != null && boat.Parts != null;
            Item active = null;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                // the lift bag bobs at the surface, its rope down to the bundle
                if (it.bag != null)
                {
                    Vector3 bp = it.bag.position;
                    float w = Waves.HeightAt(bp.x, bp.z);
                    if (it.phase == Phase.Waiting) bp = new Vector3(it.seabed.x, w - 0.2f + Mathf.Sin(Time.time * 1.3f + i) * 0.05f, it.seabed.z);
                    it.bag.position = bp;
                    if (it.bagRope != null && it.bundle != null)
                    {
                        ropePts[0] = bp;
                        ropePts[1] = it.bundle.position + Vector3.up * 0.5f;
                        it.bagRope.SetPositions(ropePts);
                        if (!it.bagRope.enabled) it.bagRope.enabled = it.phase == Phase.Waiting;
                    }
                }
                if (active == null && it.phase != Phase.Waiting) active = it;
            }

            // start the next one when the local player is on deck and the boat is near enough
            if (active == null && haveBoat && boat.PlayerAboard)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    Vector3 d = it.seabed - boat.transform.position;
                    d.y = 0f;
                    if (d.sqrMagnitude > 70f * 70f) continue;
                    it.phase = Phase.Winching;
                    it.t = 0f;
                    if (it.bagRope != null) it.bagRope.enabled = false;
                    active = it;
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Winde zieht …", "Winching …"));
                    break;
                }
            }

            if (active == null || !haveBoat)
            {
                if (cable != null && cable.enabled) cable.enabled = false;
                if (ratchet != null && ratchet.isPlaying) ratchet.Stop();
                return;
            }

            Vector3 pulley = boat.transform.TransformPoint(boat.Parts.davitPulleyLocal);
            Transform b = active.bundle;
            if (b == null) { Finish(active); return; }
            if (active.phase == Phase.Winching)
            {
                Vector3 p = b.position;
                // straight up first, then toward the pulley
                Vector3 to = pulley + Vector3.down * 0.55f;
                Vector3 step = to - p;
                float sp = 2.2f * dt;
                p = step.magnitude <= sp ? to : p + step.normalized * sp;
                b.position = p;
                b.rotation = Quaternion.Euler(Mathf.Sin(Time.time * 2.3f) * 8f, Time.time * 20f, Mathf.Sin(Time.time * 1.9f) * 8f);
                if (active.bag != null) active.bag.position = p + Vector3.up * 0.55f;
                if ((p - to).sqrMagnitude < 0.01f)
                {
                    active.phase = Phase.Swinging;
                    active.t = 0f;
                    active.swingFrom = p;
                    BoatSynth.PlayAt(BoatSynth.Splash, p, 0.3f, 1.5f, 25f);
                }
            }
            else
            {
                active.t += dt / 0.9f;
                Vector3 drop = boat.transform.TransformPoint(boat.Parts.winchDropLocal);
                float a = Mathf.Clamp01(active.t);
                Vector3 p = Vector3.Lerp(active.swingFrom, drop, a * a * (3f - 2f * a)) + Vector3.up * (Mathf.Sin(a * Mathf.PI) * 0.35f);
                b.position = p;
                if (active.bag != null) active.bag.position = p + Vector3.up * 0.55f;
                if (a >= 1f) { Finish(active); return; }
            }
            cablePts[0] = pulley;
            cablePts[1] = b.position + Vector3.up * 0.5f;
            cable.SetPositions(cablePts);
            if (!cable.enabled) cable.enabled = true;
            if (ratchet != null)
            {
                ratchet.transform.position = pulley;
                ratchet.volume = 0.5f * Settings.SfxVolume;
                if (!ratchet.isPlaying) ratchet.Play();
            }
        }

        void Finish(Item it)
        {
            items.Remove(it);
            Vector3 at = it.bundle != null ? it.bundle.position : transform.position;
            Finds.Deliver(it.id);
            BoatParticles.Puff(at + Vector3.up * 0.3f, new Color(0.9f, 0.95f, 1f, 0.75f), 16, 0.35f);
            BoatSynth.PlayAt(BoatSynth.Thunk, at, 0.8f, 0.8f, 30f);
            if (it.bundle != null) Destroy(it.bundle.gameObject);
            if (it.bag != null) Destroy(it.bag.gameObject);
            if (cable != null) cable.enabled = false;
            if (ratchet != null && ratchet.isPlaying) ratchet.Stop();
        }
    }

    /// K.8 "boat navigation to the secret island once the map is unlocked": the boat's dashboard compass gets a gold
    /// course marker (BoatController) and – while nothing else is tracked – the quest line shows „Zur Nebelinsel“ with
    /// a distance arrow (QuestTracker source Other) until the crew has photographed something on the Fog Isle.
    public static class FogRoute
    {
        public const string QuestId = "fog_route";
        /// No course marker any more this close to the island.
        public const float ArrivedRadius = 150f;

        /// Next waypoint of the buoy lane for a boat at this position (false = no course: map missing or arrived).
        public static bool HasCourse(Vector3 pos, out Vector3 target)
        {
            target = WorldLayout.FogIsland;
            Vector3 isle = WorldLayout.FogIsland;
            Vector2 p = new Vector2(pos.x, pos.z);
            if ((p - new Vector2(isle.x, isle.z)).sqrMagnitude < ArrivedRadius * ArrivedRadius) return false;
            Vector3 a = WorldLayout.FogLaneStart, b = WorldLayout.FogLaneEnd;
            Vector2 s = new Vector2(a.x, a.z), e = new Vector2(b.x, b.z), d = e - s;
            float len2 = Mathf.Max(1e-4f, d.sqrMagnitude);
            float t = Vector2.Dot(p - s, d) / len2;
            float lateral = (p - (s + d * Mathf.Clamp01(t))).magnitude;
            if (t < 0f || lateral > WorldLayout.FogLaneWidth * 1.5f) target = a;      // first to the lane entrance
            else if (t < 1f) target = b;                                             // follow the buoys
            else target = isle;
            return true;
        }
    }

    /// Keeps the quest-line entry „Zur Nebelinsel“ (K.8 route) – polled once per second, no per-frame work.
    internal sealed class FogRouteTracker : MonoBehaviour
    {
        float poll;
        bool shown;

        public static FogRouteTracker Create(Transform parent)
        {
            var go = new GameObject("FogRouteTracker");
            go.transform.SetParent(parent, false);
            return go.AddComponent<FogRouteTracker>();
        }

        void Update()
        {
            poll -= Time.deltaTime;
            if (poll > 0f) return;
            poll = 1f;
            // a lost delivery crate (HeliService) uses the same quest-line slot – it wins while it lasts
            if (HeliService.SearchActive) { shown = false; return; }
            var gm = GameManager.I;
            bool want = false;
            Vector3 target = WorldLayout.FogLaneStart;
            if (gm != null && gm.InGame && StoryFlags.Has(StoryKeys.FogMap) && !SeenFogIsle(gm.State))
            {
                Vector3 from = Vector3.zero;
                var p = Refs.Player;
                if (p != null && !(p is Object o && o == null) && p.Root != null) from = p.Root.position;
                var boat = Refs.Boat;
                if (boat != null && !(boat is Object bo && bo == null) && boat.PlayerAboard && boat.Root != null) from = boat.Root.position;
                want = FogRoute.HasCourse(from, out target);
            }
            if (want)
            {
                QuestTracker.SetEntry(QuestSource.Other, FogRoute.QuestId, Loc.Tr("Zur Nebelinsel", "To the Fog Isle"), null, target, NotifyKind.Story);
                shown = true;
            }
            else if (shown)
            {
                QuestTracker.SetEntry(QuestSource.Other, FogRoute.QuestId, null, null, null);
                shown = false;
            }
        }

        /// The crew already photographed a Fog Isle species (they found the way).
        static bool SeenFogIsle(GameState s)
        {
            if (s == null) return false;
            for (int i = 0; i < s.journal.Count; i++)
            {
                var e = s.journal[i];
                if (e == null || !e.discovered) continue;
                var def = SpeciesDatabase.Get(e.speciesId);
                if (def != null && def.homeSite == Zones.FogIsle) return true;
            }
            return false;
        }
    }

    /// Boat flag meshes (L.7 rewards "Wellen" / "Altriff") and the sponsor pennant (K.7) – built only when they change.
    public static class BoatFlags
    {
        static readonly string[] PennantColors = { "#E0607E", "#3FB8C4", "#F2C94C", "#8E5CC2", "#6E9445", "#EE7F2D" };

        /// Builds the flag under `parent` (local +Z = away from the pole). Returns null when there is nothing to show.
        public static GameObject Build(Transform parent, int flag, int pennant)
        {
            if (parent == null || (flag <= 0 && pennant < 0)) return null;
            var s = new BoatMeshSet();
            const float W = 0.52f, H = 0.32f;
            if (flag == 1)
            {
                // "Wellen": turquoise with two white zigzag waves
                Quad(s[BoatMats.Pal("#3FA9B5")], 0f, W, -H, 0f, 0f);
                for (int k = 0; k < 2; k++)
                {
                    float y = -0.09f - k * 0.12f;
                    for (int z = 0; z < 4; z++)
                    {
                        float z0 = 0.03f + z * 0.12f;
                        Tri(s[BoatMats.Pal("#EDE6DA")], new Vector3(0f, y, z0), new Vector3(0f, y + 0.05f, z0 + 0.06f), new Vector3(0f, y, z0 + 0.12f));
                    }
                }
            }
            else if (flag == 2)
            {
                // "Altriff": the village's woven-sail look – orange field, yellow band, teal wave triangles
                Quad(s[BoatMats.Pal("#EE7F2D")], 0f, W, -H, 0f, 0f);
                Quad(s[BoatMats.Pal("#F2C94C")], 0f, W, -0.2f, -0.12f, 0.002f);
                for (int z = 0; z < 5; z++)
                {
                    float z0 = 0.02f + z * 0.1f;
                    Tri(s[BoatMats.Pal("#3FA9A0")], new Vector3(0f, -H + 0.01f, z0), new Vector3(0f, -H + 0.09f, z0 + 0.05f), new Vector3(0f, -H + 0.01f, z0 + 0.1f));
                }
            }
            if (pennant >= 0)
            {
                // the sponsor's pennant below the flag
                float top = flag > 0 ? -H - 0.04f : 0f;
                Tri(s[BoatMats.Pal(PennantColors[pennant % PennantColors.Length])], new Vector3(0f, top, 0f), new Vector3(0f, top - 0.16f, 0f), new Vector3(0f, top - 0.08f, 0.42f));
            }
            return BoatOwnedMesh.Own(s.Build("BoatFlag", parent, false));
        }

        /// Double-sided rectangle in the pole plane (x = 0): z from z0 to z1, y from y0 to y1, pushed out by `off`.
        static void Quad(BoatMeshBuilder b, float z0, float z1, float y0, float y1, float off)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (0.002f + off);
                b.QuadOut(new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, y1, z1), new Vector3(x, y1, z0), new Vector3(side, 0f, 0f));
            }
        }

        static void Tri(BoatMeshBuilder b, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 o = new Vector3(side * 0.004f, 0f, 0f);
                b.TriOut(p0 + o, p1 + o, p2 + o, new Vector3(side, 0f, 0f));
            }
        }
    }
}
