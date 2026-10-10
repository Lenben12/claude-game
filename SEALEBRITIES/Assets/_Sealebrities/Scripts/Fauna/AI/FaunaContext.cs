using UnityEngine;

namespace Sealebrities
{
    /// Per-frame snapshot of the player/camera facts shared by every creature. Filled once per frame by the
    /// FaunaSpawner so hundreds of creatures don't each poke the player interface.
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
        public static float Time;

        /// Layers the forward/down obstacle probes test against (everything except Ignore Raycast and Water).
        public const int ObstacleMask = ~((1 << 2) | (1 << 4));

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

        /// Predators may attack (player present, under water, not in a calm period).
        public static bool PlayerAttackable => HasPlayer && PlayerUnderwater && Time >= CalmUntil;

        public static bool IsPlayerCollider(Collider c)
        {
            if (c == null || PlayerRoot == null) return false;
            return c.transform.IsChildOf(PlayerRoot);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Clear(); CalmUntil = 0f; Aggression = 1f; }
    }
}
