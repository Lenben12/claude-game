using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// State of a remote co-op diver as the fauna AI sees it. Implemented by the Net module's avatars and registered
    /// with FaunaContext.RegisterRemote for the avatar root that sits in Divers.All.
    public interface IFaunaDiver
    {
        /// Net player id (used to route a predator bump to that player's machine).
        int NetId { get; }
        /// Camera / head position of the diver.
        Vector3 HeadPosition { get; }
        Vector3 LookForward { get; }
        Vector3 Velocity { get; }
        bool Underwater { get; }
        bool InWater { get; }
        bool LightOn { get; }
        /// Predators leave this diver alone until this Time.time (after fainting / right after joining).
        float CalmUntil { get; }
    }

    /// One diver in the per-frame snapshot (index 0 = the local player).
    public struct FaunaDiver
    {
        public Transform root;
        public Camera cam;
        public Vector3 rootPos, head, forward, right, velocity;
        public float speed, calmUntil;
        public bool underwater, inWater, flashlight, local;
        public int netId;
    }

    /// Per-frame snapshot of the player/camera facts shared by every creature. Filled once per frame by the
    /// FaunaSpawner so hundreds of creatures don't each poke the player interface.
    /// Co-op: the snapshot also lists every remote diver (Divers.All). The spawner points the "Player*" fields at the
    /// diver nearest to the creature it is about to tick (SetFocus), so the behaviors react to whoever is closest.
    /// With a single diver (offline) the focus never moves and everything behaves exactly like single player.
    public static class FaunaContext
    {
        public static bool HasPlayer;
        /// Head / camera position (used for approach, charge and bump distances).
        public static Vector3 PlayerPos;
        public static Vector3 PlayerRootPos;
        public static Vector3 PlayerVelocity;
        public static float PlayerSpeed;
        public static bool PlayerUnderwater;
        public static bool PlayerInWater;
        public static bool PlayerFlashlight;
        public static Transform PlayerRoot;
        public static Camera Cam;
        public static Vector3 CamPos;
        public static Vector3 CamForward = Vector3.forward;
        public static Vector3 CamRight = Vector3.right;
        public static float Aggression = 1f;
        /// Predators/ambushers do not attack before this time (e.g. right after the player fainted).
        public static float CalmUntil;
        /// Co-op: start-of-game calm period that also protects remote divers (the local faint calm does not).
        public static float SessionCalmUntil;
        public static float Time;

        // ------------------------------------------------------------------ co-op diver list
        public const int MaxDivers = 8;
        /// Every diver this frame: index 0 = local player, then remote divers (only while Net.IsOnline).
        public static readonly FaunaDiver[] DiverList = new FaunaDiver[MaxDivers];
        public static int DiverCount;
        /// Index into DiverList the Player* fields currently describe.
        public static int Focus;
        /// The focused diver is the local player (bumps raise GameEvents.PlayerBumped directly).
        public static bool FocusLocal = true;
        /// Net id of the focused diver.
        public static int FocusNetId;
        static float focusCalmUntil;

        /// Set by the Net module: a predator bumped a remote diver (netId, air seconds lost).
        public static Action<int, float> RemoteBump;

        static readonly Dictionary<Transform, IFaunaDiver> remote = new Dictionary<Transform, IFaunaDiver>();

        /// Layers the forward/down obstacle probes test against (everything except Ignore Raycast and Water).
        public const int ObstacleMask = ~((1 << 2) | (1 << 4));

        /// Registers the state source of a remote diver whose root is (or will be) in Divers.All.
        public static void RegisterRemote(Transform root, IFaunaDiver diver)
        {
            if (root == null || diver == null) return;
            remote[root] = diver;
        }

        public static void UnregisterRemote(Transform root)
        {
            if (ReferenceEquals(root, null)) return;
            remote.Remove(root);
        }

        public static void Capture()
        {
            Time = UnityEngine.Time.time;
            var gm = GameManager.I;
            Aggression = gm != null ? gm.Difficulty.predatorAggression : 1f;

            var pl = Refs.Player;
            if (pl == null) { Clear(); return; }
            Transform root = pl.Root;
            if (root == null) { Clear(); return; }

            HasPlayer = true;
            PlayerRoot = root;
            PlayerRootPos = root.position;
            Camera cam = pl.View;
            if (cam != null)
            {
                Cam = cam;
                Transform ct = cam.transform;
                CamPos = ct.position;
                CamForward = ct.forward;
                CamRight = ct.right;
            }
            else
            {
                Cam = null;
                CamPos = PlayerRootPos + Vector3.up * 1.6f;
                CamForward = root.forward;
                CamRight = root.right;
            }
            PlayerPos = CamPos;
            PlayerVelocity = pl.Velocity;
            PlayerSpeed = PlayerVelocity.magnitude;
            PlayerUnderwater = pl.IsUnderwater;
            PlayerInWater = pl.IsInWater;
            PlayerFlashlight = pl.FlashlightOn;

            // ---- diver list: the local player first (focus 0 == the values above)
            Focus = 0;
            FocusLocal = true;
            FocusNetId = Net.LocalId;
            focusCalmUntil = 0f;
            ref FaunaDiver l = ref DiverList[0];
            l.root = root;
            l.cam = Cam;
            l.rootPos = PlayerRootPos;
            l.head = PlayerPos;
            l.forward = CamForward;
            l.right = CamRight;
            l.velocity = PlayerVelocity;
            l.speed = PlayerSpeed;
            l.underwater = PlayerUnderwater;
            l.inWater = PlayerInWater;
            l.flashlight = PlayerFlashlight;
            l.local = true;
            l.netId = Net.LocalId;
            l.calmUntil = 0f;
            DiverCount = 1;
            if (Net.IsOnline) CaptureRemote(root);
        }

        static void CaptureRemote(Transform localRoot)
        {
            var all = Divers.All;
            for (int i = 0; i < all.Count && DiverCount < MaxDivers; i++)
            {
                Transform t = all[i];
                if (t == null || t == localRoot || !t.gameObject.activeInHierarchy) continue;
                ref FaunaDiver d = ref DiverList[DiverCount];
                d.root = t;
                d.cam = null;
                d.local = false;
                d.rootPos = t.position;
                Vector3 fwd;
                if (remote.TryGetValue(t, out var src) && src != null && !(src is UnityEngine.Object uo && uo == null))
                {
                    d.head = src.HeadPosition;
                    fwd = src.LookForward;
                    d.velocity = src.Velocity;
                    d.underwater = src.Underwater;
                    d.inWater = src.InWater;
                    d.flashlight = src.LightOn;
                    d.netId = src.NetId;
                    d.calmUntil = src.CalmUntil;
                }
                else
                {
                    // a diver transform without a registered state source: derive what we can from the transform
                    d.head = d.rootPos + Vector3.up * 1.6f;
                    fwd = t.forward;
                    d.velocity = Vector3.zero;
                    d.underwater = Waves.IsUnderwater(d.head);
                    d.inWater = Waves.DepthOf(d.rootPos + Vector3.up * 0.9f) > 0f;
                    d.flashlight = false;
                    d.netId = -1;
                    d.calmUntil = 0f;
                }
                if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                fwd.Normalize();
                d.forward = fwd;
                Vector3 r = Vector3.Cross(Vector3.up, fwd);
                d.right = r.sqrMagnitude > 1e-6f ? r.normalized : Vector3.right;
                d.speed = d.velocity.magnitude;
                DiverCount++;
            }
        }

        /// Points the Player*/Cam* fields at DiverList[i] (no-op when already focused).
        public static void SetFocus(int i)
        {
            if (i == Focus || i < 0 || i >= DiverCount) return;
            Focus = i;
            ref FaunaDiver d = ref DiverList[i];
            FocusLocal = d.local;
            FocusNetId = d.netId;
            focusCalmUntil = d.calmUntil;
            PlayerRoot = d.root;
            PlayerRootPos = d.rootPos;
            PlayerPos = d.head;
            CamPos = d.head;
            Cam = d.cam;
            CamForward = d.forward;
            CamRight = d.right;
            PlayerVelocity = d.velocity;
            PlayerSpeed = d.speed;
            PlayerUnderwater = d.underwater;
            PlayerInWater = d.inWater;
            PlayerFlashlight = d.flashlight;
        }

        /// Index of the diver whose head is nearest to p (0 when there is only the local player).
        public static int NearestDiver(Vector3 p, out float dist)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < DiverCount; i++)
            {
                Vector3 h = DiverList[i].head;
                float dx = p.x - h.x, dy = p.y - h.y, dz = p.z - h.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < bd) { bd = d2; best = i; }
            }
            dist = DiverCount > 0 ? Mathf.Sqrt(bd) : 0f;
            return best;
        }

        static void Clear()
        {
            HasPlayer = false;
            PlayerRoot = null;
            Cam = null;
            PlayerUnderwater = false;
            PlayerInWater = false;
            PlayerSpeed = 0f;
            PlayerVelocity = Vector3.zero;
            DiverCount = 0;
            Focus = 0;
            FocusLocal = true;
            focusCalmUntil = 0f;
        }

        /// True when the point is roughly inside the player's camera view.
        public static bool InView(Vector3 p)
        {
            if (Cam == null)
            {
                if (!HasPlayer) return false;
                Vector3 d = p - CamPos;
                return Vector3.Dot(d, CamForward) > d.magnitude * 0.5f;
            }
            Vector3 v = Cam.WorldToViewportPoint(p);
            return v.z > 0f && v.x > -0.08f && v.x < 1.08f && v.y > -0.08f && v.y < 1.08f;
        }

        /// Predators may attack (player present, under water, not in a calm period). Remote divers have their own
        /// calm period (fainting / joining) instead of the local player's.
        public static bool PlayerAttackable =>
            HasPlayer && PlayerUnderwater && (FocusLocal ? Time >= CalmUntil : Time >= focusCalmUntil && Time >= SessionCalmUntil);

        public static bool IsPlayerCollider(Collider c)
        {
            if (c == null || PlayerRoot == null) return false;
            return c.transform.IsChildOf(PlayerRoot);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Clear();
            CalmUntil = 0f;
            SessionCalmUntil = 0f;
            Aggression = 1f;
            RemoteBump = null;
            remote.Clear();
            Array.Clear(DiverList, 0, DiverList.Length);
        }
    }
}
