using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop delivery service. Driven purely by the shared GameState.deliveries, idempotently, so it works the
    /// same on the host and on co-op clients that only receive state snapshots:
    ///  • the HOST rolls each new delivery's mishap once (L.12, HeliMishaps.Roll → Delivery.mishap / dropZone);
    ///  • a due delivery (Shop.IsDue) that is not dropped and has no crate yet → this machine flies its own (visual)
    ///    helicopter (Wanda) to the drop point(s) and drops the crates;
    ///  • a dropped delivery without a crate (loaded save, joined later, dropped by the host's helicopter) → a landed
    ///    crate where its mishap put it;
    ///  • a crate whose delivery left the state (opened by someone, on any machine) → the crate disappears.
    /// Snapshots replace the Delivery objects, so deliveries are matched by content (item, arrival day and time).
    /// Opening a crate goes through Shop.Receive (forwarded to the host on clients). After the drops Wanda lands on the
    /// pad and idles for 2 minutes (one line for a player within 20 m).
    public class HeliService : MonoBehaviour
    {
        /// Crate slots on the pad (radius 6 m): a ring around the centre – the centre stays free for Wanda's landing.
        static readonly Vector2[] Slots =
        {
            new Vector2(3.6f, 0.8f), new Vector2(-3.4f, 1.4f), new Vector2(0.9f, -3.8f), new Vector2(-1.4f, 3.7f),
            new Vector2(2.8f, 3.0f), new Vector2(-3.0f, -2.5f), new Vector2(3.2f, -2.4f), new Vector2(-0.2f, -4.5f),
        };

        struct Opened
        {
            public string itemId;
            public int day;
            public float time;
            public float until;
        }

        /// Where a crate of a delivery comes down on this machine.
        public struct Placement
        {
            /// Effective mishap here (palm/roof/boat may fall back when the place does not exist right now).
            public DeliveryMishap mishap;
            /// Landing point (crown for a palm, roof spot, zone point, foredeck …).
            public Vector3 point;
            /// Palm: the trunk base (shake spot).
            public Vector3 trunk;
            /// Where the helicopter hovers to drop it (ground under the drop).
            public Vector3 hover;
            public bool onPad;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly List<HeliCrate> crates = new List<HeliCrate>();
        readonly List<Delivery> due = new List<Delivery>();
        readonly List<Opened> opened = new List<Opened>();
        readonly HashSet<Delivery> rolled = new HashSet<Delivery>();
        HeliChopper active, idle;
        float poll, radioOffAt = -1f;
        Vector3 pad;
        string searchLine;
        int searchKey = int.MinValue;
        Language searchLang;
        bool searchShown;

        /// Positions of crates the crew has to search for (wrong place, village) – the UI map can mark them.
        public static readonly List<Vector3> SearchMarkers = new List<Vector3>();
        /// A lost crate is waiting somewhere (the quest line "Paket suchen" is shown).
        public static bool SearchActive => SearchMarkers.Count > 0;
        /// Wanda's chopper stands on the pad with idling rotor (2 min after a delivery) – the npc module can put her
        /// NPC next to it ("wanda_pad"); the cockpit pilot hides while an NPC "wanda" exists.
        public static bool ChopperOnPad { get; internal set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { SearchMarkers.Clear(); ChopperOnPad = false; }

        public Vector3 Pad => pad;

        public static HeliService Create(Transform parent)
        {
            var go = new GameObject("HeliService");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<HeliService>();
            s.Init();
            return s;
        }

        void Init()
        {
            pad = WorldInfo.HelipadPosition;
            if (pad == Vector3.zero)
            {
                pad = WorldLayout.Helipad;
                pad.y = WorldInfo.GroundHeight(pad);
            }
            GameEvents.GameStarted += OnGameStarted;
            GameEvents.ReturnedToMenu += OnReturnedToMenu;
            Net.StateSynced += OnStateSynced;
            CoreCommands.RegisterCustom(HeliMishaps.CommandKey, OnCrateCommand);
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            Net.StateSynced -= OnStateSynced;
            SearchMarkers.Clear();
        }

        void OnStateSynced() => poll = 0f;

        void Update()
        {
            if (radioOffAt > 0f && Time.time >= radioOffAt)
            {
                radioOffAt = -1f;
                GameEvents.RaiseRadioCall(HeliMishaps.WandaId, false);
            }
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || gm.State == null) return;
            poll -= Time.deltaTime;
            if (poll > 0f) return;
            poll = 0.5f;

            if (Net.HasAuthority) RollPending(gm.State);
            Reconcile(gm.State);
            UpdateSearch();
            if (active != null) return;

            due.Clear();
            var list = gm.State.deliveries;
            for (int i = 0; i < list.Count && due.Count < 4; i++)
            {
                var d = list[i];
                if (d != null && !d.dropped && Shop.IsDue(d) && FindCrate(d) == null && !WasOpened(d)) due.Add(d);
            }
            if (due.Count == 0) return;
            // Wanda still idles on the pad: she takes off first, the next flight comes right after
            if (idle != null && idle.OnPad)
            {
                idle.Depart();
                return;
            }
            active = HeliChopper.Spawn(this, transform, due);
        }

        // ------------------------------------------------------------------ mishaps (host rolls, everybody places)
        void RollPending(GameState s)
        {
            var list = s.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d == null || d.dropped || rolled.Contains(d)) continue;
                rolled.Add(d);
                if (d.mishap != 0) continue;   // rolled in an earlier session
                var m = HeliMishaps.Roll(d, s, out int zone);
                if (m == DeliveryMishap.None) continue;
                d.mishap = (int)m;
                d.dropZone = m == DeliveryMishap.WrongPlace ? zone : -1;
                s.lastMishapDay = d.arrivalDay;
                Debug.Log("[Heli] delivery " + d.itemId + " (day " + d.arrivalDay + ") mishap: " + m + (zone >= 0 ? " zone " + zone : ""));
            }
        }

        /// The landing place of a delivery's crate on this machine (deterministic for everything but the boat).
        public Placement Place(Delivery d, int slot)
        {
            var p = new Placement { mishap = d != null ? d.Mishap : DeliveryMishap.None };
            Vector3 padSlot = SlotPosition(slot);
            switch (p.mishap)
            {
                case DeliveryMishap.Water:
                    if (FindDriftTarget(out Vector3 water)) { p.point = water; p.hover = pad; return p; }
                    break;
                case DeliveryMishap.HutRoof:
                    if (FindRoofSpot(out Vector3 roof)) { p.point = roof; p.hover = roof; return p; }
                    p.mishap = DeliveryMishap.WrongPlace;
                    break;
                case DeliveryMishap.Palm:
                    if (FindPalm(d, out Vector3 crown, out Vector3 trunk)) { p.point = crown; p.trunk = trunk; p.hover = crown; return p; }
                    p.mishap = DeliveryMishap.WrongPlace;
                    break;
                case DeliveryMishap.Village:
                {
                    Vector3 v = WorldLayout.Village + new Vector3(6f, 0f, -3f);
                    v.y = BoatSurface.Below(new Vector3(v.x, 80f, v.z), 120f, out bool wet);
                    if (!wet) { p.point = v; p.hover = v; return p; }
                    p.mishap = DeliveryMishap.WrongPlace;
                    break;
                }
                case DeliveryMishap.Boat:
                {
                    var boat = Refs.Boat as BoatController;
                    if (boat != null && boat.AtMooring && boat.Parts != null)
                    {
                        Vector3 b = boat.transform.TransformPoint(boat.Parts.foredeckCrateLocal);
                        p.point = b;
                        p.hover = b;
                        return p;
                    }
                    p.mishap = DeliveryMishap.Water;
                    if (FindDriftTarget(out Vector3 w2)) { p.point = w2; p.hover = pad; return p; }
                    break;
                }
                case DeliveryMishap.Goat:
                case DeliveryMishap.None:
                    break;
            }
            if (p.mishap == DeliveryMishap.WrongPlace)
            {
                int z = HeliMishaps.ZoneOf(d);
                var zones = HeliMishaps.DropZones;
                if (z >= 0 && z < zones.Count)
                {
                    Vector3 zp = zones[z];
                    zp.y = BoatSurface.Below(new Vector3(zp.x, zp.y + 40f, zp.z), 80f, out _);
                    p.point = zp;
                    p.hover = zp;
                    return p;
                }
            }
            if (p.mishap != DeliveryMishap.Goat) p.mishap = DeliveryMishap.None;
            p.point = padSlot;
            p.hover = pad;
            p.onPad = true;
            return p;
        }

        /// The rare "gust of wind" drop onto the water (L.12 Water mishap) – decided by the host's roll.
        public static bool DriftsAway(Delivery d) => d != null && d.Mishap == DeliveryMishap.Water;

        /// Stable hash of a delivery (FNV-1a over the item id, arrival day and the save seed).
        public static uint Hash(Delivery d)
        {
            unchecked
            {
                uint h = 2166136261u;
                string id = d != null ? d.itemId : null;
                if (id != null) for (int i = 0; i < id.Length; i++) { h ^= id[i]; h *= 16777619u; }
                int day = d != null ? d.arrivalDay : 0;
                h ^= (uint)day; h *= 16777619u;
                var gm = GameManager.I;
                int seed = gm != null && gm.State != null ? gm.State.saveSeed : 0;
                h ^= (uint)seed; h *= 16777619u;
                return h;
            }
        }

        /// FNV-1a of a string (deterministic on every machine).
        public static uint HashString(string s)
        {
            unchecked
            {
                uint h = 2166136261u;
                if (s != null) for (int i = 0; i < s.Length; i++) { h ^= s[i]; h *= 16777619u; }
                return h;
            }
        }

        // ------------------------------------------------------------------ shared crate progress (goat, kid, palm, roof)
        /// Raises Delivery.nudges (never lowers it). Clients forward "HeliCrate" to the host and apply optimistically.
        public void SetNudges(Delivery d, int value)
        {
            if (d == null) return;
            var cur = Resolve(d) ?? d;
            if (value <= cur.nudges) return;
            if (Net.IsClient)
                Net.Command(NetCommand.Custom, HeliMishaps.CommandKey, d.itemId, d.arrivalDay.ToString(Inv), d.arrivalTime.ToString("R", Inv), value.ToString(Inv));
            cur.nudges = value;
            d.nudges = value;
        }

        /// Host side of "HeliCrate": item id, arrival day, arrival time, nudges.
        static bool OnCrateCommand(int fromId, string[] args)
        {
            var gm = GameManager.I;
            if (gm == null || gm.State == null || args == null || args.Length < 5) return false;
            if (!int.TryParse(args[2], NumberStyles.Integer, Inv, out int day)) return false;
            if (!float.TryParse(args[3], NumberStyles.Float, Inv, out float time) || float.IsNaN(time)) return false;
            if (!int.TryParse(args[4], NumberStyles.Integer, Inv, out int n)) return false;
            var probe = new Delivery { itemId = args[1], arrivalDay = day, arrivalTime = time };
            var list = gm.State.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                if (!Same(list[i], probe)) continue;
                list[i].nudges = Mathf.Max(list[i].nudges, Mathf.Clamp(n, 0, 9));
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ matching (snapshots replace the objects)
        public static bool Same(Delivery a, Delivery b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.itemId == b.itemId && a.arrivalDay == b.arrivalDay && Mathf.Abs(a.arrivalTime - b.arrivalTime) < 0.002f;
        }

        /// The current instance of a delivery in the shared state, null when it is gone (received).
        public Delivery Resolve(Delivery d)
        {
            var gm = GameManager.I;
            if (d == null || gm == null || gm.State == null) return null;
            var list = gm.State.deliveries;
            for (int i = 0; i < list.Count; i++) if (Same(list[i], d)) return list[i];
            return null;
        }

        HeliCrate FindCrate(Delivery d)
        {
            for (int i = 0; i < crates.Count; i++)
                if (crates[i] != null && Same(crates[i].Delivery, d)) return crates[i];
            return null;
        }

        /// The local player opened this crate a moment ago (a slightly older host snapshot may still list it).
        public bool WasOpened(Delivery d)
        {
            if (d == null) return false;
            float now = Time.time;
            for (int i = opened.Count - 1; i >= 0; i--)
            {
                var o = opened[i];
                if (now > o.until) { opened.RemoveAt(i); continue; }
                if (o.itemId == d.itemId && o.day == d.arrivalDay && Mathf.Abs(o.time - d.arrivalTime) < 0.002f) return true;
            }
            return false;
        }

        public void MarkOpened(Delivery d)
        {
            if (d == null) return;
            opened.Add(new Opened { itemId = d.itemId, day = d.arrivalDay, time = d.arrivalTime, until = Time.time + 12f });
        }

        void Reconcile(GameState s)
        {
            // crates whose delivery is gone (received on this or another machine)
            for (int i = crates.Count - 1; i >= 0; i--)
            {
                var c = crates[i];
                if (c == null) { crates.RemoveAt(i); continue; }
                var cur = Resolve(c.Delivery);
                if (cur == null || WasOpened(cur))
                {
                    crates.RemoveAt(i);
                    c.Vanish();
                    continue;
                }
                c.Rebind(cur);
                // a guest dropped with a stale mishap and the host decided differently: move the crate once
                if (c.IsDown && cur.dropped && c.RawMishap != cur.Mishap)
                {
                    crates.RemoveAt(i);
                    c.Vanish();
                }
            }
            // dropped deliveries without a crate: restore them where their mishap put them
            var list = s.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d == null || !d.dropped || FindCrate(d) != null || WasOpened(d)) continue;
                CreateLandedFor(d);
            }
        }

        void CreateLandedFor(Delivery d)
        {
            int slot = FreeSlot();
            var pl = Place(d, slot);
            Vector3 at = pl.point;
            bool tangled = false;
            switch (pl.mishap)
            {
                case DeliveryMishap.Palm:
                    if (d.nudges >= 1) at = GroundBeside(pl.trunk, pl.point, 1.6f);
                    else tangled = true;
                    break;
                case DeliveryMishap.HutRoof:
                    if (d.nudges >= 1) at = GroundBeside(WorldLayout.Hut, pl.point, 6f);
                    break;
            }
            var c = HeliCrate.CreateLanded(this, d, pl.onPad ? slot : -1, at);
            c.SetLandedMishap(pl.mishap, d.Mishap, pl.trunk, tangled, pl.mishap == DeliveryMishap.Water);
        }

        static Vector3 GroundBeside(Vector3 around, Vector3 from, float dist)
        {
            Vector3 dir = from - around;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.right;
            Vector3 p = around + dir * dist;
            p.y = BoatSurface.Below(new Vector3(p.x, around.y + 30f, p.z), 60f, out _);
            return p;
        }

        // ------------------------------------------------------------------ search (wrong place / village)
        void UpdateSearch()
        {
            SearchMarkers.Clear();
            HeliCrate best = null;
            float bestD = float.MaxValue;
            var p = Refs.Player;
            Vector3 me = p != null && !(p is Object o && o == null) && p.Root != null ? p.Root.position : pad;
            for (int i = 0; i < crates.Count; i++)
            {
                var c = crates[i];
                if (c == null || !c.IsDown || (c.Mishap != DeliveryMishap.WrongPlace && c.Mishap != DeliveryMishap.Village)) continue;
                Vector3 cp = c.transform.position;
                SearchMarkers.Add(cp);
                float d = (cp - me).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
            if (best != null)
            {
                int key = best.Mishap == DeliveryMishap.Village ? -2 : HeliMishaps.ZoneOf(best.Delivery);
                if (searchLine == null || key != searchKey || searchLang != Loc.Current)
                {
                    searchKey = key;
                    searchLang = Loc.Current;
                    searchLine = key == -2 ? Loc.Tr("Paket im Dorf holen", "Get the parcel in the village")
                                           : Loc.Tr("Paket suchen: ", "Find the parcel: ") + HeliMishaps.ZoneName(key);
                }
                QuestTracker.SetEntry(QuestSource.Other, "heli_search", searchLine, null, best.transform.position, NotifyKind.Delivery);
                searchShown = true;
            }
            else if (searchShown)
            {
                searchShown = false;
                QuestTracker.SetEntry(QuestSource.Other, "heli_search", null, null, null);
            }
        }

        /// Wanda on the radio about a mishap (radio card + subtitle, ~5 s). The npc module voices her calls itself
        /// (dialogue lines "wanda.mishap.*" on GameEvents.DeliveryMishapHappened) – then this fallback stays silent.
        public void RadioMishap(DeliveryMishap m)
        {
            if (Dialogue.Resolve != null) return;
            string line = HeliMishaps.RadioLine(m);
            if (line == null) return;
            GameEvents.RaiseRadioCall(HeliMishaps.WandaId, true);
            HeliMishaps.Say(line, 4.5f);
            radioOffAt = Time.time + 4.8f;
        }

        // ------------------------------------------------------------------ chopper bookkeeping
        /// The flight dropped all its cargo – a new one may start; it now lands (idle) or flies home.
        public void CargoDone(HeliChopper c, bool landing)
        {
            if (active == c) active = null;
            if (landing) idle = c;
        }

        public void ChopperGone(HeliChopper c)
        {
            if (active == c) active = null;
            if (idle == c) idle = null;
        }

        // ------------------------------------------------------------------ crates
        public void Register(HeliCrate c) { if (!crates.Contains(c)) crates.Add(c); }
        public void Unregister(HeliCrate c) { crates.Remove(c); }

        public int FreeSlot()
        {
            for (int s = 0; s < 64; s++)
            {
                bool used = false;
                for (int i = 0; i < crates.Count; i++)
                    if (crates[i] != null && crates[i].Slot == s) { used = true; break; }
                if (!used) return s;
            }
            return 0;
        }

        public Vector3 SlotPosition(int slot)
        {
            Vector2 o;
            if (slot < Slots.Length) o = Slots[slot];
            else
            {
                float a = slot * 2.4f;
                o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (7f + (slot % 3) * 0.8f);   // beside the pad
            }
            var p = new Vector3(pad.x + o.x, ProbeY, pad.z + o.y);
            p.y = BoatSurface.Below(p, ProbeY + 200f, out _);
            return p;
        }

        /// Probe start height: above everything near the pad (terrain peaks at ~60 m) but below the cruising chopper.
        float ProbeY => Mathf.Max(pad.y + 30f, 66f);

        public float PadSurfaceY() => BoatSurface.Below(new Vector3(pad.x, ProbeY, pad.z), ProbeY + 200f, out _);

        /// Water point near the pad for the "gust of wind" drop.
        public bool FindDriftTarget(out Vector3 point)
        {
            float bestD = float.MaxValue;
            point = Vector3.zero;
            for (int k = 0; k < 24; k++)
            {
                float a = k / 24f * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                for (float r = 15f; r <= 160f; r += 5f)
                {
                    Vector3 p = pad + dir * r;
                    if (WorldInfo.GroundHeight(p) < Waves.SeaLevel - 1.5f)
                    {
                        if (r < bestD)
                        {
                            bestD = r;
                            point = pad + dir * (r + 6f);
                            point.y = Waves.SeaLevel;
                        }
                        break;
                    }
                }
            }
            return bestD < float.MaxValue;
        }

        /// A spot on the hut roof (2.4–9 m above the ground): the highest of a few probes around the hut centre.
        public static bool FindRoofSpot(out Vector3 spot)
        {
            Vector3 hut = WorldLayout.Hut;
            spot = Vector3.zero;
            float best = float.MinValue;
            for (int i = 0; i < 9; i++)
            {
                float ox = (i % 3 - 1) * 1.6f, oz = (i / 3 - 1) * 1.6f;
                Vector3 q = new Vector3(hut.x + ox, 0f, hut.z + oz);
                float g = WorldInfo.GroundHeight(q);
                float y = BoatSurface.Below(new Vector3(q.x, g + 20f, q.z), 25f, out bool water);
                if (water || y < g + 2.4f || y > g + 9f) continue;
                if (y > best) { best = y; spot = new Vector3(q.x, y, q.z); }
            }
            return best > float.MinValue;
        }

        static readonly Collider[] Overlap = new Collider[512];

        /// A palm near the pad (trunk capsules of the props module: upright, ~3.4 m, r ≤ 0.3 m) and an estimate of its
        /// crown (≈ 7.5 m up, leaning toward lower ground). Deterministic: sorted by distance, picked by the hash.
        public bool FindPalm(Delivery d, out Vector3 crown, out Vector3 trunk)
        {
            crown = trunk = Vector3.zero;
            int n = Physics.OverlapSphereNonAlloc(pad, 55f, Overlap, ~0, QueryTriggerInteraction.Ignore);
            Vector3 b0 = Vector3.zero, b1 = Vector3.zero, b2 = Vector3.zero;
            float d0 = float.MaxValue, d1 = float.MaxValue, d2 = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var cap = Overlap[i] as CapsuleCollider;
                Overlap[i] = null;
                if (cap == null || cap.direction != 1 || Mathf.Abs(cap.height - 3.4f) > 0.06f || cap.radius > 0.31f) continue;
                Vector3 c = cap.transform.TransformPoint(cap.center);
                Vector3 baseP = c - Vector3.up * (cap.height * 0.5f);
                float dist = new Vector2(baseP.x - pad.x, baseP.z - pad.z).magnitude;
                if (dist < 12f) continue;
                float key = Mathf.Abs(dist - 24f) + baseP.x * 1e-4f;
                if (key < d0) { d2 = d1; b2 = b1; d1 = d0; b1 = b0; d0 = key; b0 = baseP; }
                else if (key < d1) { d2 = d1; b2 = b1; d1 = key; b1 = baseP; }
                else if (key < d2) { d2 = key; b2 = baseP; }
            }
            if (d0 == float.MaxValue) return false;
            uint pick = (Hash(d) >> 7) % 3u;
            trunk = pick == 2 && d2 < float.MaxValue ? b2 : pick >= 1 && d1 < float.MaxValue ? b1 : b0;
            // coastal palms lean toward the sea: toward the lowest ground around
            Vector3 lean = Vector3.zero;
            float low = float.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                float a = k * Mathf.PI * 0.25f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float g = WorldInfo.GroundHeight(trunk + dir * 6f);
                if (g < low) { low = g; lean = dir; }
            }
            crown = trunk + lean * 2f + Vector3.up * 7.2f;
            return true;
        }

        // ------------------------------------------------------------------ lifecycle
        void ClearAll()
        {
            if (active != null) Destroy(active.gameObject);
            if (idle != null && idle != active) Destroy(idle.gameObject);
            active = null;
            idle = null;
            for (int i = crates.Count - 1; i >= 0; i--)
                if (crates[i] != null) Destroy(crates[i].gameObject);
            crates.Clear();
            opened.Clear();
            rolled.Clear();
            SearchMarkers.Clear();
            if (searchShown) { searchShown = false; QuestTracker.SetEntry(QuestSource.Other, "heli_search", null, null, null); }
            if (radioOffAt > 0f) { radioOffAt = -1f; GameEvents.RaiseRadioCall(HeliMishaps.WandaId, false); }
        }

        void OnReturnedToMenu() => ClearAll();

        void OnGameStarted()
        {
            ClearAll();
            poll = 2f;
            var gm = GameManager.I;
            if (gm == null || gm.State == null) return;
            var list = gm.State.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d == null || !d.dropped) continue;
                CreateLandedFor(d);
            }
        }
    }
}
