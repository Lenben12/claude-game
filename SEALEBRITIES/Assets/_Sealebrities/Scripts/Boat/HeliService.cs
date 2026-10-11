using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop delivery service. Driven purely by the shared GameState.deliveries, idempotently, so it works the
    /// same on the host and on co-op clients that only receive state snapshots:
    ///  • a due delivery (Shop.IsDue) that is not dropped and has no crate yet → this machine flies its own (visual)
    ///    helicopter in and drops the crate on the helipad (standard: same day 2–3 in-game hours after ordering);
    ///  • a dropped delivery without a crate (loaded save, joined later, dropped by the host's helicopter) → a landed crate;
    ///  • a crate whose delivery left the state (opened by someone, on any machine) → the crate disappears.
    /// Snapshots replace the Delivery objects, so deliveries are matched by content (item, arrival day and time).
    /// Opening a crate goes through Shop.Receive (forwarded to the host on clients).
    public class HeliService : MonoBehaviour
    {
        static readonly Vector2[] Slots =
        {
            new Vector2(0f, 0f), new Vector2(1.6f, 0.5f), new Vector2(-1.5f, 0.7f), new Vector2(0.4f, -1.6f),
            new Vector2(-1.2f, -1.4f), new Vector2(1.7f, -1.3f), new Vector2(-0.2f, 1.9f), new Vector2(2.4f, 1.6f),
        };

        struct Opened
        {
            public string itemId;
            public int day;
            public float time;
            public float until;
        }

        readonly List<HeliCrate> crates = new List<HeliCrate>();
        readonly List<Delivery> due = new List<Delivery>();
        readonly List<Opened> opened = new List<Opened>();
        HeliChopper active;
        float poll;
        Vector3 pad;

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
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
            Net.StateSynced -= OnStateSynced;
        }

        void OnStateSynced() => poll = 0f;

        void Update()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || gm.State == null) return;
            poll -= Time.deltaTime;
            if (poll > 0f) return;
            poll = 0.5f;

            Reconcile(gm.State);
            if (active != null) return;

            due.Clear();
            var list = gm.State.deliveries;
            for (int i = 0; i < list.Count && due.Count < 4; i++)
            {
                var d = list[i];
                if (d != null && !d.dropped && Shop.IsDue(d) && FindCrate(d) == null && !WasOpened(d)) due.Add(d);
            }
            if (due.Count == 0) return;
            active = HeliChopper.Spawn(this, transform, due);
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
            }
            // dropped deliveries without a crate: restore them on the pad
            var list = s.deliveries;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                if (d == null || !d.dropped || FindCrate(d) != null || WasOpened(d)) continue;
                int slot = FreeSlot();
                HeliCrate.CreateLanded(this, d, slot, SlotPosition(slot));
            }
        }

        /// The rare "gust of wind" drop onto the water – deterministic per delivery and save, so every co-op machine
        /// agrees. Never on day 1, never for the tutorial tank (whenever it arrives) and never for express orders.
        public static bool DriftsAway(Delivery d)
        {
            if (d == null || d.express || d.arrivalDay <= 1 || d.itemId == "tank_t1") return false;
            return Hash(d) % 1000u < 50u;
        }

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
                o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (3f + (slot % 3) * 0.6f);
            }
            var p = new Vector3(pad.x + o.x, ProbeY, pad.z + o.y);
            p.y = BoatSurface.Below(p, ProbeY + 200f, out _);
            return p;
        }

        /// Probe start height: above everything near the pad (terrain peaks at ~60 m) but below the cruising chopper.
        float ProbeY => Mathf.Max(pad.y + 30f, 66f);

        public float PadSurfaceY() => BoatSurface.Below(new Vector3(pad.x, ProbeY, pad.z), ProbeY + 200f, out _);

        /// Water point near the pad for the rare "gust of wind" drop.
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

        // ------------------------------------------------------------------ lifecycle
        void ClearAll()
        {
            if (active != null) Destroy(active.gameObject);
            active = null;
            for (int i = crates.Count - 1; i >= 0; i--)
                if (crates[i] != null) Destroy(crates[i].gameObject);
            crates.Clear();
            opened.Clear();
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
                int slot = FreeSlot();
                HeliCrate.CreateLanded(this, d, slot, SlotPosition(slot));
            }
        }
    }
}
