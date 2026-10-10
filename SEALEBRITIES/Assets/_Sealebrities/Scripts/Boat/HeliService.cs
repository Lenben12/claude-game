using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Chopper Shop delivery service: polls the due deliveries, sends a helicopter that drops the crates on the helipad
    /// and restores waiting crates when a save is loaded.
    public class HeliService : MonoBehaviour
    {
        static readonly Vector2[] Slots =
        {
            new Vector2(0f, 0f), new Vector2(1.6f, 0.5f), new Vector2(-1.5f, 0.7f), new Vector2(0.4f, -1.6f),
            new Vector2(-1.2f, -1.4f), new Vector2(1.7f, -1.3f), new Vector2(-0.2f, 1.9f), new Vector2(2.4f, 1.6f),
        };

        readonly List<HeliCrate> crates = new List<HeliCrate>();
        readonly List<Delivery> due = new List<Delivery>();
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
        }

        void OnDestroy()
        {
            GameEvents.GameStarted -= OnGameStarted;
            GameEvents.ReturnedToMenu -= OnReturnedToMenu;
        }

        void Update()
        {
            if (GameManager.I == null || !GameManager.I.InGame) return;
            if (active != null) return;
            poll -= Time.deltaTime;
            if (poll > 0f) return;
            poll = 0.5f;

            due.Clear();
            var list = GameManager.I.State.deliveries;
            for (int i = 0; i < list.Count && due.Count < 4; i++)
            {
                var d = list[i];
                if (d != null && !d.dropped && Shop.IsDue(d) && !IsCarried(d)) due.Add(d);
            }
            if (due.Count == 0) return;
            active = HeliChopper.Spawn(this, transform, due);
        }

        bool IsCarried(Delivery d)
        {
            for (int i = 0; i < crates.Count; i++) if (crates[i] != null && crates[i].Delivery == d) return true;
            return false;
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
        }

        void OnReturnedToMenu() => ClearAll();

        void OnGameStarted()
        {
            ClearAll();
            poll = 2f;
            var list = GameManager.I.State.deliveries;
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
