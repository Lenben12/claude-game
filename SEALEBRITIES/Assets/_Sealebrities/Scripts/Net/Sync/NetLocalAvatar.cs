using UnityEngine;

namespace Sealebrities
{
    /// What the co-op avatar stream needs from the LOCAL player beyond IPlayer (L.13 motion set, K.4 selfie,
    /// F.1 unconscious / tow, D.1 fishing visuals). Modules publish what they know; everything left unpublished is
    /// derived here from IPlayer, Hands, Carry, SelfieState, SleepVote, NetRescue and FishingRodTool – so remote avatars
    /// already move nicely before every module is wired.
    ///
    /// * Player module (L.13): call PublishMotion(state, speed, phase) every frame from the controller / puppet driver
    ///   (ladder, crouch, land and jump states can only come from there).
    /// * Boat module (D.1): call PublishFishing(state, bobber, tension) every frame while the rod is out
    ///   (FishingRodTool.RodState as byte; bobber = world position of the float, tension 0..1 of the reel gauge).
    /// Everything is per frame, allocation free and only read by NetAvatarSync (15–20 Hz).
    public static class NetLocalAvatar
    {
        const float PublishTimeout = 0.3f;

        // ---- published by the Player module
        static MotionState pubMotion;
        static float pubPhase, pubMotionTime = -10f;

        // ---- published by the Boat module (fishing rod)
        static byte pubRod;
        static Vector3 pubBobber;
        static bool pubBobberValid;
        static float pubTension, pubFishingTime = -10f;

        // ---- derived
        static MotionState derivedMotion;
        static float derivedPhase, lastDeriveTime = -10f;
        static Vector3 lastRootPos;
        static bool hasLastRoot;

        /// L.13: the local player's procedural motion state (phase 0..1 of the current walk / run / swim / ladder cycle).
        /// speed is accepted for symmetry with IDiverMotion.SetMotion – the avatar stream already carries the velocity.
        public static void PublishMotion(MotionState state, float speed, float phase)
        {
            pubMotion = state;
            pubPhase = float.IsNaN(phase) ? 0f : Mathf.Repeat(phase, 1f);
            pubMotionTime = Time.unscaledTime;
        }

        /// D.1: the local fishing rod (state = (byte)FishingRodTool.RodState, bobber world position, reel tension 0..1).
        public static void PublishFishing(byte rodState, Vector3 bobber, bool bobberValid, float tension01)
        {
            pubRod = rodState;
            pubBobber = bobber;
            pubBobberValid = bobberValid && !float.IsNaN(bobber.x);
            pubTension = float.IsNaN(tension01) ? 0f : Mathf.Clamp01(tension01);
            pubFishingTime = Time.unscaledTime;
        }

        /// Current motion state + cycle phase to send (published, else derived).
        public static void Resolve(IPlayer p, out MotionState state, out float phase)
        {
            float now = Time.unscaledTime;
            if (now - pubMotionTime < PublishTimeout)
            {
                // story / co-op states win over the published locomotion (the player module may not know them yet)
                state = Override(p, pubMotion);
                phase = pubPhase;
                return;
            }
            Derive(p, now);
            state = derivedMotion;
            phase = derivedPhase;
        }

        static MotionState Override(IPlayer p, MotionState s)
        {
            if (NetRescue.LocalUnconscious) return MotionState.Unconscious;
            if (NetRescue.LocalTowTarget >= 0) return MotionState.Tow;
            if (SleepVote.LocalLying) return MotionState.Lie;
            if (SelfieState.Active) return MotionState.Selfie;
            return s;
        }

        static void Derive(IPlayer p, float now)
        {
            float dt = Mathf.Clamp(now - lastDeriveTime, 0f, 0.25f);
            lastDeriveTime = now;
            if (p == null || p.Root == null) { derivedMotion = MotionState.Idle; return; }
            Vector3 root = p.Root.position;
            Vector3 v = p.Velocity;
            if (!hasLastRoot) { lastRootPos = root; hasLastRoot = true; }
            float planar = new Vector2(v.x, v.z).magnitude;
            MotionState s;
            float cycleMeters;   // distance per full motion cycle (two steps / one stroke)
            if (NetRescue.LocalUnconscious) { s = MotionState.Unconscious; cycleMeters = 0f; }
            else if (NetRescue.LocalTowTarget >= 0) { s = MotionState.Tow; cycleMeters = 1.6f; }
            else if (SleepVote.LocalLying) { s = MotionState.Lie; cycleMeters = 0f; }
            else if (SelfieState.Active) { s = MotionState.Selfie; cycleMeters = 1.1f; }
            else if (p.IsSeated) { s = MotionState.Sit; cycleMeters = 0f; }
            else if (Carry.IsCarrying) { s = MotionState.Carry; cycleMeters = p.IsInWater ? 1.6f : 1.0f; }
            else if (p.IsUnderwater) { s = MotionState.Dive; cycleMeters = 1.6f; }
            else if (p.IsInWater) { s = MotionState.Swim; cycleMeters = 1.4f; }
            else if (v.y > 1.6f && Mathf.Abs(root.y - lastRootPos.y) > 0.005f) { s = MotionState.Jump; cycleMeters = 0f; }
            else if (v.y < -2.6f) { s = MotionState.Fall; cycleMeters = 0f; }
            else if (planar > 3.3f) { s = MotionState.Run; cycleMeters = 1.7f; }
            else if (planar > 0.25f) { s = MotionState.Walk; cycleMeters = 1.1f; }
            else { s = MotionState.Idle; cycleMeters = 0f; }
            lastRootPos = root;
            derivedMotion = s;
            float speed = s == MotionState.Dive || s == MotionState.Swim || s == MotionState.Tow ? v.magnitude : planar;
            // idle / lying: a slow breathing cycle (0.25 Hz) so remote puppets stay in step anyway
            derivedPhase = Mathf.Repeat(derivedPhase + (cycleMeters > 0f ? speed * dt / cycleMeters : dt * 0.25f), 1f);
        }

        /// Fishing visuals to send: false when the rod is not out. rodState = (byte)FishingRodTool.RodState.
        public static bool Fishing(out byte rodState, out Vector3 bobber, out bool bobberValid, out float tension01)
        {
            if (Time.unscaledTime - pubFishingTime < PublishTimeout)
            {
                rodState = pubRod;
                bobber = pubBobber;
                bobberValid = pubBobberValid;
                tension01 = pubTension;
                return rodState > (byte)FishingRodTool.RodState.Ready;
            }
            // not published: the rod state + the float of the local rod tool (child "Bobber", found once and cached)
            rodState = (byte)FishingRodTool.State;
            bobber = Vector3.zero;
            bobberValid = false;
            tension01 = FishingRodTool.State == FishingRodTool.RodState.Reeling ? 0.55f : 0f;
            bool out_ = rodState > (byte)FishingRodTool.RodState.Ready;
            if (out_)
            {
                if (rodBobber == null && Time.unscaledTime >= rodLookupAt)
                {
                    rodLookupAt = Time.unscaledTime + 2f;
                    var tool = Object.FindFirstObjectByType<FishingRodTool>();
                    rodBobber = tool != null ? tool.transform.Find("Bobber") : null;
                }
                if (rodBobber != null && rodBobber.gameObject.activeInHierarchy)
                {
                    bobber = rodBobber.position;
                    bobberValid = true;
                }
            }
            return out_;
        }

        static Transform rodBobber;
        static float rodLookupAt;

        /// K.4: the selfie camera of the Player module when it is a separate camera (null = the player's View moves to
        /// the selfie position). Remote players see the diver face this camera.
        public static Transform SelfieCamera;

        /// K.4: in the selfie view the camera sits in front of the face and looks back. The avatar has to face the
        /// selfie camera (not look where the camera looks): yaw / pitch from the head toward the camera.
        public static bool SelfieFacing(IPlayer p, out float yaw, out float pitch)
        {
            yaw = 0f;
            pitch = 0f;
            if (!SelfieState.Active || p == null || p.Root == null) return false;
            Transform cam = SelfieCamera != null && SelfieCamera.gameObject.activeInHierarchy ? SelfieCamera : (p.View != null ? p.View.transform : null);
            if (cam == null) return false;
            Vector3 head = p.Root.position + Vector3.up * PlayerController.EyeHeight;
            Vector3 d = cam.position - head;
            if (d.sqrMagnitude < 0.09f)
            {
                // the player module keeps the main camera at the eye: face where it looks
                return false;
            }
            yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            pubMotion = MotionState.Idle; pubPhase = 0f; pubMotionTime = -10f;
            pubRod = 0; pubBobber = Vector3.zero; pubBobberValid = false; pubTension = 0f; pubFishingTime = -10f;
            derivedMotion = MotionState.Idle; derivedPhase = 0f; lastDeriveTime = -10f; hasLastRoot = false;
            SelfieCamera = null;
            rodBobber = null;
            rodLookupAt = 0f;
        }
    }
}
