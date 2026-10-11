using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// F.1 rescue: tow a fainted friend. Replication + the API the Player module drives (the player module owns the
    /// unconscious body, the countdown, the float-up, the "E – Abschleppen" prompt and the revive check).
    ///
    /// Flow (Normal/Hard, ≥ 2 players):
    ///  1. The diver runs out of air → player module: SetLocalUnconscious(true, seconds) (30 s Normal / 20 s Hard,
    ///     DifficultySettings.unconsciousSeconds). Everybody gets GameEvents.PlayerUnconscious(id) + a short notification.
    ///  2. Another diver within TowRange: FindTowable(head, TowRange, out id, out name) for the prompt, StartTow(id) on E,
    ///     StopTow() on E again. The host decides (one tower per target) and sends the tow pairs to everybody.
    ///     LocalTowTarget ≥ 0 → the tower swims ×0.7 and uses air ×1.2 (player module).
    ///  3. The towed diver's machine follows TryGetTowAnchor (the tower's avatar + 1.2 m behind it) every frame and
    ///     decides the revive: depth &lt; 0.8 m → ReportRevived(TowerOf(Net.LocalId)) (or −1 when it surfaced alone).
    ///     Everybody gets GameEvents.PlayerRevived(id, rescuerId); the rescuer gets „Gerettet!“, +1 rescue and the
    ///     achievement *Rettungsschwimmer*.
    ///  4. Countdown over → player module: SetLocalUnconscious(false, 0) and the normal faint (GameManager.HandleFaint).
    /// Messages: Unconscious 94 (any → host → all), TowState 93 (client → host request, host → all pairs).
    public static class NetRescue
    {
        public const float TowRange = 2f;
        /// The towed diver hangs this far behind the tower's head.
        public const float TowOffset = 1.2f;
        /// F.1: tower swim speed ×0.7, tower air use ×1.2 (player module applies them while LocalTowTarget ≥ 0).
        public const float TowerSwimMultiplier = 0.7f, TowerAirMultiplier = 1.2f;
        /// Revived when the head comes above this depth (m).
        public const float ReviveDepth = 0.8f;

        const byte OpStop = 0, OpStart = 1, OpPairs = 2;
        /// The host accepts a tow request up to this distance (lag tolerance on top of TowRange).
        const float HostTowTolerance = 4.5f;

        /// Raised whenever an unconscious state or a tow pair changed (UI prompts, player module).
        public static event Action Changed;

        static readonly Dictionary<int, float> unconsciousUntil = new Dictionary<int, float>(4);
        static readonly Dictionary<int, int> towerOfTarget = new Dictionary<int, int>(4);
        static readonly List<int> tmp = new List<int>(4);
        static readonly SyncWriter writer = new SyncWriter(32);
        static bool localUnconscious;
        static bool handlersOn;

        // ================================================================== queries
        public static bool LocalUnconscious => localUnconscious;

        /// True while that player is unconscious (and the countdown is not long over – lost messages heal).
        public static bool IsUnconscious(int playerId)
        {
            if (playerId == Net.LocalId) return localUnconscious;
            return unconsciousUntil.TryGetValue(playerId, out float until) && Time.unscaledTime < until + 5f;
        }

        /// Seconds of the unconscious countdown left for a player (0 when not unconscious).
        public static float SecondsLeft(int playerId) =>
            unconsciousUntil.TryGetValue(playerId, out float until) ? Mathf.Max(0f, until - Time.unscaledTime) : 0f;

        /// The player towing targetId (-1 = nobody).
        public static int TowerOf(int targetId) => towerOfTarget.TryGetValue(targetId, out int t) ? t : -1;

        /// The player towerId is towing (-1 = nobody).
        public static int TowTargetOf(int towerId)
        {
            foreach (var kv in towerOfTarget) if (kv.Value == towerId) return kv.Key;
            return -1;
        }

        /// The local player tows this player (-1 = nobody).
        public static int LocalTowTarget => Net.IsOnline ? TowTargetOf(Net.LocalId) : -1;
        /// The local player is towed by this player (-1 = nobody).
        public static int LocalTowedBy => Net.IsOnline ? TowerOf(Net.LocalId) : -1;

        /// The nearest unconscious diver nobody else tows within range of `from` (the local player's head) – for the
        /// „E – Abschleppen“ prompt. name = that player's name.
        public static bool FindTowable(Vector3 from, float range, out int targetId, out string name)
        {
            targetId = -1;
            name = null;
            var sync = NetAvatarSync.Instance;
            if (!Net.IsOnline || sync == null || localUnconscious) return false;
            float best = range * range;
            var all = sync.All;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.Visible || !IsUnconscious(a.PlayerId)) continue;
                int tower = TowerOf(a.PlayerId);
                if (tower >= 0 && tower != Net.LocalId) continue;
                float d = (a.HeadPosition - from).sqrMagnitude;
                if (d > best) continue;
                best = d;
                targetId = a.PlayerId;
                name = a.PlayerName;
            }
            return targetId >= 0;
        }

        /// Where the local (towed) diver's HEAD belongs this frame: 1.2 m behind the tower's head, facing the same way.
        /// False when nobody tows the local player or the tower's avatar is not visible.
        public static bool TryGetTowAnchor(out Vector3 headPosition, out Quaternion rotation)
        {
            headPosition = Vector3.zero;
            rotation = Quaternion.identity;
            int tower = LocalTowedBy;
            if (tower < 0 || !NetAvatarSync.TryGet(tower, out var a) || !a.Visible) return false;
            Vector3 fwd = a.LookForward;
            Vector3 v = a.Velocity;
            // swimming: trail behind the direction of travel (the tower may look around while swimming)
            if (v.sqrMagnitude > 0.09f) fwd = v;
            // behind HORIZONTALLY, the head level with the tower's: a tower swimming straight up left the friend 1.2 m
            // below its own head, so reaching the surface never got the towed head above ReviveDepth
            Vector3 flat = new Vector3(fwd.x, 0f, fwd.z);
            if (flat.sqrMagnitude < 0.01f) { flat = a.LookForward; flat.y = 0f; }
            if (flat.sqrMagnitude < 0.01f) { flat = a.transform.forward; flat.y = 0f; }
            flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward;
            headPosition = a.HeadPosition - flat * TowOffset + Vector3.down * 0.15f;
            rotation = Quaternion.LookRotation(flat, Vector3.up);
            return true;
        }

        // ================================================================== local actions (player module)
        /// The local diver became unconscious (true, countdown seconds) or is conscious again / fainted for real (false).
        public static void SetLocalUnconscious(bool unconscious, float secondsLeft)
        {
            if (localUnconscious == unconscious) return;
            localUnconscious = unconscious;
            if (unconscious) unconsciousUntil[Net.LocalId] = Time.unscaledTime + Mathf.Max(0f, secondsLeft);
            else unconsciousUntil.Remove(Net.LocalId);
            if (!Net.IsOnline)
            {
                Changed?.Invoke();
                return;
            }
            Send(unconscious, secondsLeft, -1, false);
            Apply(Net.LocalId, unconscious, secondsLeft, -1, false, true);
        }

        /// The towed diver reached the surface (decided on its own machine): conscious again.
        /// rescuerId = TowerOf(Net.LocalId) (−1 when it surfaced without help). Everybody gets
        /// GameEvents.PlayerRevived(id, rescuerId) – a timeout faint (SetLocalUnconscious(false, 0)) raises nothing.
        public static void ReportRevived(int rescuerId)
        {
            if (!localUnconscious) return;
            localUnconscious = false;
            unconsciousUntil.Remove(Net.LocalId);
            if (Net.IsOnline) Send(false, 0f, rescuerId, true);
            Apply(Net.LocalId, false, 0f, rescuerId, true, true);
        }

        /// The local diver starts towing targetId (E within TowRange). False when not possible.
        public static bool StartTow(int targetId)
        {
            if (!Net.IsOnline || targetId < 0 || targetId == Net.LocalId || localUnconscious || !IsUnconscious(targetId)) return false;
            int other = TowerOf(targetId);
            if (other >= 0 && other != Net.LocalId) return false;
            if (Net.IsHost) return HostStartTow(Net.LocalId, targetId);
            SetPair(targetId, Net.LocalId);   // optimistic – the host's pair list confirms or corrects it
            writer.Reset();
            writer.Byte(OpStart);
            writer.SByte((sbyte)Mathf.Clamp(targetId, -1, 127));
            NetBus.Send(NetSyncMsg.TowState, writer.ToArray(), NetBus.Everyone, true);
            Changed?.Invoke();
            return true;
        }

        /// The local diver lets go (E again, surfaced, fainted).
        public static void StopTow()
        {
            if (!Net.IsOnline || LocalTowTarget < 0) return;
            if (Net.IsHost) { HostStopTow(Net.LocalId); return; }
            RemovePairsOfTower(Net.LocalId);
            writer.Reset();
            writer.Byte(OpStop);
            writer.SByte(-1);
            NetBus.Send(NetSyncMsg.TowState, writer.ToArray(), NetBus.Everyone, true);
            Changed?.Invoke();
        }

        // ================================================================== install / session
        internal static void Install()
        {
            if (handlersOn) return;
            handlersOn = true;
            NetBus.Register(NetSyncMsg.Unconscious, OnUnconscious);
            NetBus.Register(NetSyncMsg.TowState, OnTow);
        }

        internal static void Uninstall()
        {
            if (!handlersOn) return;
            handlersOn = false;
            NetBus.Unregister(NetSyncMsg.Unconscious);
            NetBus.Unregister(NetSyncMsg.TowState);
        }

        internal static void OnPlayerJoined(NetPlayerInfo p)
        {
            if (!Net.IsHost || p == null || p.IsLocal) return;
            // late joiner: who is unconscious right now, and the tow pairs
            foreach (var kv in unconsciousUntil)
            {
                float left = kv.Value - Time.unscaledTime;
                if (left <= -5f) continue;
                writer.Reset();
                writer.Byte((byte)Mathf.Clamp(kv.Key, 0, 255));
                writer.Bool(true);
                writer.Float(Mathf.Max(0f, left));
                writer.SByte(-1);
                writer.Bool(false);
                NetBus.Send(NetSyncMsg.Unconscious, writer.ToArray(), p.id, true);
            }
            if (towerOfTarget.Count > 0) SendPairs(p.id);
        }

        internal static void OnPlayerLeft(NetPlayerInfo p)
        {
            if (p == null) return;
            bool changed = unconsciousUntil.Remove(p.id);
            int before = towerOfTarget.Count;
            towerOfTarget.Remove(p.id);
            RemovePairsOfTower(p.id);
            changed |= before != towerOfTarget.Count;
            if (!changed) return;
            if (Net.IsHost) SendPairs(NetBus.Everyone);
            Changed?.Invoke();
        }

        internal static void Reset()
        {
            bool any = unconsciousUntil.Count > 0 || towerOfTarget.Count > 0 || localUnconscious;
            unconsciousUntil.Clear();
            towerOfTarget.Clear();
            localUnconscious = false;
            if (any) Changed?.Invoke();
        }

        /// Host: drops pairs whose target woke up / tower became unconscious, and stale unconscious states (1 Hz).
        internal static void Tick()
        {
            if (!Net.IsOnline) return;
            float now = Time.unscaledTime;
            tmp.Clear();
            foreach (var kv in unconsciousUntil)
                if (kv.Key != Net.LocalId && now > kv.Value + 20f) tmp.Add(kv.Key);   // the "off" got lost (crash, alt-F4)
            bool changed = false;
            for (int i = 0; i < tmp.Count; i++) { unconsciousUntil.Remove(tmp[i]); changed = true; }
            if (Net.IsHost)
            {
                tmp.Clear();
                foreach (var kv in towerOfTarget)
                    if (!IsUnconscious(kv.Key) || IsUnconscious(kv.Value)) tmp.Add(kv.Key);
                for (int i = 0; i < tmp.Count; i++) towerOfTarget.Remove(tmp[i]);
                if (tmp.Count > 0) { SendPairs(NetBus.Everyone); changed = true; }
            }
            if (changed) Changed?.Invoke();
        }

        // ================================================================== messages
        static void Send(bool on, float secondsLeft, int rescuer, bool revived)
        {
            writer.Reset();
            writer.Byte((byte)Mathf.Clamp(Net.LocalId, 0, 255));
            writer.Bool(on);
            writer.Float(Mathf.Max(0f, secondsLeft));
            writer.SByte((sbyte)Mathf.Clamp(rescuer, -1, 127));
            writer.Bool(revived);
            NetBus.Send(NetSyncMsg.Unconscious, writer.ToArray(), NetBus.Everyone, true);
        }

        static void OnUnconscious(int fromId, byte[] payload)
        {
            var r = new SyncReader(payload);
            int origin = r.Byte();
            bool on = r.Bool();
            float secs = r.Float();
            int rescuer = r.SByte();
            if (r.Failed) return;
            bool revived = r.Remaining > 0 ? r.Bool() : rescuer >= 0;
            if (Net.IsHost)
            {
                if (fromId == Net.LocalId) return;
                origin = fromId;   // the host trusts its transport
                NetBus.SendExcept(NetSyncMsg.Unconscious, payload, fromId, true);
            }
            if (origin == Net.LocalId) return;
            if (on) unconsciousUntil[origin] = Time.unscaledTime + Mathf.Clamp(secs, 0f, 120f);
            else unconsciousUntil.Remove(origin);
            Apply(origin, on, secs, rescuer, revived, false);
        }

        /// Shared bookkeeping + events + notifications for a state change of `who`.
        static void Apply(int who, bool on, float secs, int rescuer, bool revived, bool local)
        {
            bool pairsChanged = false;
            if (on)
            {
                // an unconscious diver cannot tow anybody
                int before = towerOfTarget.Count;
                RemovePairsOfTower(who);
                pairsChanged = before != towerOfTarget.Count;
                GameEvents.RaisePlayerUnconscious(who);
                if (!local)
                    NetState.NotifyLocal(NotifyKind.Warning, Loc.Tr($"{NameOf(who)} ist bewusstlos!", $"{NameOf(who)} passed out!"), Loc.Tr("Abschleppen!", "Tow them!"));
            }
            else
            {
                pairsChanged = towerOfTarget.Remove(who);
                if (revived) GameEvents.RaisePlayerRevived(who, rescuer);
                if (revived && rescuer >= 0)
                {
                    if (local)
                        NetState.NotifyLocal(NotifyKind.Event, Loc.Tr($"{NameOf(rescuer)} hat dich gerettet!", $"{NameOf(rescuer)} saved you!"));
                    else if (rescuer == Net.LocalId)
                    {
                        NetState.NotifyLocal(NotifyKind.Event, Loc.Tr("Gerettet!", "Rescued!"), NameOf(who));
                        var rec = PlayerRecords.Local;
                        if (rec != null) rec.rescues++;
                        Achievements.Unlock("lifeguard");
                    }
                    else
                        NetState.NotifyLocal(NotifyKind.Event, Loc.Tr($"{NameOf(rescuer)} rettet {NameOf(who)}", $"{NameOf(rescuer)} rescued {NameOf(who)}"));
                }
            }
            if (pairsChanged && Net.IsHost) SendPairs(NetBus.Everyone);
            Changed?.Invoke();
        }

        static void OnTow(int fromId, byte[] payload)
        {
            var r = new SyncReader(payload);
            byte op = r.Byte();
            if (r.Failed) return;
            if (Net.IsHost)
            {
                if (fromId == Net.LocalId) return;
                int target = r.SByte();
                if (op == OpStart && !HostStartTow(fromId, target)) SendPairs(fromId);   // refused: correct the optimistic pair
                else if (op == OpStop) HostStopTow(fromId);
                return;
            }
            if (op != OpPairs) return;
            int n = r.Byte();
            towerOfTarget.Clear();
            for (int i = 0; i < n; i++)
            {
                int target = r.SByte();
                int tower = r.SByte();
                if (r.Failed) break;
                if (target >= 0 && tower >= 0) towerOfTarget[target] = tower;
            }
            Changed?.Invoke();
        }

        static bool HostStartTow(int tower, int target)
        {
            if (tower == target || !IsUnconscious(target) || IsUnconscious(tower)) return false;
            int other = TowerOf(target);
            if (other >= 0 && other != tower) return false;
            if (Net.Find(target) == null || Net.Find(tower) == null) return false;
            if (TryHeadOf(tower, out Vector3 a) && TryHeadOf(target, out Vector3 b) && (a - b).sqrMagnitude > HostTowTolerance * HostTowTolerance)
                return false;
            RemovePairsOfTower(tower);   // one target per tower
            SetPair(target, tower);
            SendPairs(NetBus.Everyone);
            Changed?.Invoke();
            return true;
        }

        static void HostStopTow(int tower)
        {
            int before = towerOfTarget.Count;
            RemovePairsOfTower(tower);
            if (before == towerOfTarget.Count) return;
            SendPairs(NetBus.Everyone);
            Changed?.Invoke();
        }

        static bool TryHeadOf(int id, out Vector3 head)
        {
            head = Vector3.zero;
            if (id == Net.LocalId)
            {
                var p = Refs.Player;
                if (!NetWorldSync.Alive(p) || p.Root == null) return false;
                head = p.View != null ? p.View.transform.position : p.Root.position + Vector3.up * PlayerController.EyeHeight;
                return true;
            }
            if (!NetAvatarSync.TryGet(id, out var av) || !av.Visible) return false;
            head = av.HeadPosition;
            return true;
        }

        static void SendPairs(int target)
        {
            writer.Reset();
            writer.Byte(OpPairs);
            writer.Byte((byte)Mathf.Min(towerOfTarget.Count, 8));
            int k = 0;
            foreach (var kv in towerOfTarget)
            {
                if (k++ >= 8) break;
                writer.SByte((sbyte)Mathf.Clamp(kv.Key, -1, 127));
                writer.SByte((sbyte)Mathf.Clamp(kv.Value, -1, 127));
            }
            NetBus.Send(NetSyncMsg.TowState, writer.ToArray(), target, true);
        }

        static void SetPair(int target, int tower) => towerOfTarget[target] = tower;

        static void RemovePairsOfTower(int tower)
        {
            tmp.Clear();
            foreach (var kv in towerOfTarget) if (kv.Value == tower) tmp.Add(kv.Key);
            for (int i = 0; i < tmp.Count; i++) towerOfTarget.Remove(tmp[i]);
        }

        static string NameOf(int id)
        {
            var p = Net.Find(id);
            return p != null && !string.IsNullOrWhiteSpace(p.name) ? p.name : Loc.Tr("Jemand", "Someone");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Changed = null;
            unconsciousUntil.Clear();
            towerOfTarget.Clear();
            tmp.Clear();
            localUnconscious = false;
            handlersOn = false;
        }
    }
}
