using UnityEngine;

namespace Sealebrities
{
    /// Movement: walking, jumping, moving platforms, surface swimming, diving, climbing out, seats, teleports and water state.
    public partial class PlayerController
    {
        const float WalkSpeed = 4f;
        const float SprintSpeed = 6.5f;
        const float JumpHeight = 1.05f;
        const float Gravity = 20f;
        const float SurfaceSwimSpeed = 2.1f;
        const float DiveSpeed = 2.6f;
        /// Eye height above the wave surface while swimming at the surface.
        const float SurfaceEyeOffset = 0.15f;

        Vector3 vel;    // own velocity; on land while grounded it is relative to the platform below
        bool grounded, wasGrounded, jumpedThisFrame, groundHit;
        float groundGap = 10f;
        Rigidbody groundRb;

        // moving platform (boat deck)
        Transform platform;
        Rigidbody platformRb;
        Vector3 platformLocalPos, platformVel;
        float platformYaw;
        bool platformFresh;
        /// After teleports / climbing out we already move with the deck: don't convert world to deck velocity on landing.
        bool suppressLandingConvert;

        // seat
        bool seated;
        Transform seat;

        // water
        bool isInWater, isUnderwater;
        float depth, enterTimer, exitTimer, diveGrace, lastWaveH;
        bool lastWaveValid;

        // climbing out of the water
        bool vaulting;
        float vaultT;
        Vector3 vaultFrom, vaultTargetWorld, vaultTargetLocal;
        Transform vaultRef;
        bool canVaultHint;
        float vaultHintTimer;

        Vector3 YawForward()
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }

        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void UpdateMovement(bool input, float dt)
        {
            if (cc == null || !cc.enabled) return;
            switch (mode)
            {
                case PlayerMoveMode.Land: MoveLand(input, dt); break;
                case PlayerMoveMode.Surface: MoveSurface(input, dt); break;
                default: MoveDive(input, dt); break;
            }
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        // ------------------------------------------------------------------ land
        void MoveLand(bool input, float dt)
        {
            Vector3 carry = ComputePlatformCarry(dt);
            if (!cc.enabled) return;   // platform teleported us

            Vector2 mv = input ? GameInput.Move : Vector2.zero;
            bool sprint = input && GameInput.Sprint && mv.y > 0.1f;
            float speed = sprint ? SprintSpeed : WalkSpeed;
            Vector3 p = transform.position;
            // wading through shallow water slows you down
            float wade = Mathf.Clamp01((Waves.HeightAt(p.x, p.z) - p.y) / 0.6f);
            speed *= Mathf.Lerp(1f, 0.55f, wade);

            Vector3 fwd = YawForward();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 wish = (fwd * mv.y + right * mv.x) * speed;
            Vector3 planar = new Vector3(vel.x, 0f, vel.z);
            if (grounded) planar = Vector3.MoveTowards(planar, wish, 30f * dt);
            else if (mv.sqrMagnitude > 0.01f) planar = Vector3.MoveTowards(planar, wish, 6f * dt);

            jumpedThisFrame = false;
            if (grounded)
            {
                if (vel.y < 0f) vel.y = -2f;
                if (input && GameInput.JumpPressed)
                {
                    vel.y = Mathf.Sqrt(2f * Gravity * JumpHeight);
                    jumpedThisFrame = true;
                }
            }
            else vel.y = Mathf.Max(vel.y - Gravity * dt, -40f);
            vel.x = planar.x;
            vel.z = planar.z;

            float preVy = vel.y;
            CollisionFlags flags = cc.Move(vel * dt + carry);
            if ((flags & CollisionFlags.Above) != 0 && vel.y > 0f) vel.y = 0f;

            ProbeGround();
            // stick to the ground when walking down slopes and steps
            if (!grounded && wasGrounded && !jumpedThisFrame && vel.y <= 0f && groundHit && groundGap < 0.35f)
            {
                cc.Move(Vector3.down * (groundGap + 0.02f));
                grounded = true;
                vel.y = -2f;
            }
            UpdatePlatform();

            if (grounded && !wasGrounded && preVy < -4f)
            {
                landDip = Mathf.Min(0.14f, -preVy * 0.012f);
                if (preVy < -11f) trauma = Mathf.Max(trauma, 0.3f);
            }

            float hs = planar.magnitude;
            bobAmp = Mathf.Lerp(bobAmp, grounded ? Mathf.Clamp01(hs / WalkSpeed) : 0f, 1f - Mathf.Exp(-8f * dt));
            bobPhase = Mathf.Repeat(bobPhase + dt * hs * (Mathf.PI / 0.75f), Mathf.PI * 2f);
        }

        void ProbeGround()
        {
            wasGrounded = grounded;
            Vector3 p = transform.position;
            const float lift = 0.1f;
            float r = Radius * 0.9f;
            groundHit = false;
            groundGap = 10f;
            groundRb = null;
            if (Physics.SphereCast(p + Vector3.up * (Radius + lift), r, Vector3.down, out RaycastHit hit, 0.4f,
                                   Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y > 0.45f)
                {
                    groundHit = true;
                    groundGap = hit.distance - (lift + Radius - r);
                    groundRb = hit.collider.attachedRigidbody;
                }
            }
            grounded = cc.isGrounded || (groundHit && groundGap < 0.08f && vel.y <= 0.5f);
            // deck edges: the sphere probe can miss while the controller still stands on the rigidbody
            if (grounded && groundRb == null && ccGroundFrame == Time.frameCount && ccGroundRb != null) groundRb = ccGroundRb;
        }

        Rigidbody ccGroundRb;
        int ccGroundFrame = -1;

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (hit.normal.y < 0.45f || hit.point.y > transform.position.y + Radius) return;
            ccGroundRb = hit.collider != null ? hit.collider.attachedRigidbody : null;
            ccGroundFrame = Time.frameCount;
        }

        /// Tracks the rigidbody we stand on so its motion (incl. yaw) can be carried over next frame.
        void UpdatePlatform()
        {
            Vector3 p = transform.position;
            if (grounded && groundRb != null)
            {
                Transform pt = groundRb.transform;
                if (pt != platform)
                {
                    if (!wasGrounded && !suppressLandingConvert)
                    {
                        // landing from a jump: our velocity was in world space, make it relative to the deck
                        Vector3 pv = groundRb.GetPointVelocity(p);
                        vel.x -= pv.x;
                        vel.z -= pv.z;
                    }
                    platform = pt;
                    platformRb = groundRb;
                    platformFresh = true;
                }
                platformLocalPos = platform.InverseTransformPoint(p);
                platformYaw = platform.eulerAngles.y;
            }
            else if (platform != null || platformRb != null)
            {
                if (!grounded)
                {
                    // jumped / walked off a moving deck: keep its momentum
                    vel.x += platformVel.x;
                    vel.z += platformVel.z;
                }
                platform = null;
                platformRb = null;
                platformVel = Vector3.zero;
            }
            if (grounded) suppressLandingConvert = false;
        }

        /// Motion of the platform under our feet since last frame (exact transform delta, rigidbody velocity on first contact).
        Vector3 ComputePlatformCarry(float dt)
        {
            if (platform == null)
            {
                platformRb = null;
                platformVel = Vector3.zero;
                return Vector3.zero;
            }
            Vector3 p = transform.position;
            Vector3 carry;
            if (platformFresh)
            {
                platformFresh = false;
                if (platformRb != null)
                {
                    carry = platformRb.GetPointVelocity(p) * dt;
                    yaw += platformRb.angularVelocity.y * Mathf.Rad2Deg * dt;
                }
                else carry = Vector3.zero;
            }
            else
            {
                Vector3 target = platform.TransformPoint(platformLocalPos);
                carry = target - p;
                yaw += Mathf.DeltaAngle(platformYaw, platform.eulerAngles.y);
                if (carry.sqrMagnitude > 4f)
                {
                    // the boat was teleported (PlaceAt) while we stood on it: come along
                    platformVel = Vector3.zero;
                    PlaceAt(target + Vector3.up * 0.05f, yaw);
                    return Vector3.zero;
                }
            }
            yaw = Mathf.Repeat(yaw, 360f);
            platformVel = carry / dt;
            return carry;
        }

        // ------------------------------------------------------------------ surface swimming
        void MoveSurface(bool input, float dt)
        {
            Vector2 mv = input ? GameInput.Move : Vector2.zero;
            bool sprint = input && GameInput.Sprint && mv.sqrMagnitude > 0.01f;
            float speed = SurfaceSwimSpeed * Mathf.Max(0.5f, gear.swimSpeed) * (sprint ? 1.35f : 1f);
            Vector3 fwd = YawForward();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 wish = (fwd * mv.y + right * mv.x) * speed;
            float rate = wish.sqrMagnitude > 0.01f ? 2.6f : 1.1f;
            Vector3 planar = Vector3.Lerp(new Vector3(vel.x, 0f, vel.z), wish, 1f - Mathf.Exp(-rate * dt));

            // vertical: spring the head to the moving surface (with wave-velocity feed-forward so it never lags under)
            Vector3 p = transform.position;
            float h = Waves.HeightAt(p.x, p.z);
            float waveVy = lastWaveValid ? Mathf.Clamp((h - lastWaveH) / dt, -3f, 3f) : 0f;
            lastWaveH = h;
            lastWaveValid = true;
            float targetY = h + SurfaceEyeOffset - EyeHeight;
            float wantVy = waveVy + (targetY - p.y) * 4f;
            vel.y = Mathf.Lerp(vel.y, wantVy, 1f - Mathf.Exp(-5f * dt));
            vel.x = planar.x;
            vel.z = planar.z;
            if (IsFainting) vel.y = Mathf.Min(vel.y, -0.3f);

            cc.Move(vel * dt);
            ProbeGround();
            platform = null;
            platformRb = null;
            bobAmp = Mathf.Lerp(bobAmp, 0f, 1f - Mathf.Exp(-8f * dt));

            if (!input) return;
            bool lookDive = pitch > 32f && mv.y > 0.3f;
            if ((GameInput.Descend || lookDive) && !grounded)
            {
                StartDive();
                return;
            }
            if (GameInput.JumpPressed || (GameInput.Ascend && mv.y > 0.3f)) TryVault();
        }

        void StartDive()
        {
            mode = PlayerMoveMode.Dive;
            vel.y = Mathf.Min(vel.y, -1.6f);
            diveGrace = 0.55f;
            canVaultHint = false;
        }

        // ------------------------------------------------------------------ diving
        void MoveDive(bool input, float dt)
        {
            Vector2 mv = input ? GameInput.Move : Vector2.zero;
            float vert = input ? (GameInput.Ascend ? 1f : 0f) - (GameInput.Descend ? 1f : 0f) : 0f;
            bool moving = mv.sqrMagnitude > 0.01f || vert != 0f;
            sprintingUnderwater = input && GameInput.Sprint && moving;
            float speed = DiveSpeed * Mathf.Max(0.5f, gear.swimSpeed) * (sprintingUnderwater ? 1.5f : 1f);

            Vector3 wish = head.forward * mv.y + transform.right * mv.x + Vector3.up * vert;
            if (wish.sqrMagnitude > 1f) wish.Normalize();
            wish *= speed;
            if (IsFainting) wish = new Vector3(0f, -0.35f, 0f);
            float rate = moving ? 2.2f : 0.85f;     // slow decay = a little glide/drift
            vel = Vector3.Lerp(vel, wish, 1f - Mathf.Exp(-rate * dt));

            float t = Time.time;
            Vector3 drift = new Vector3(Mathf.Sin(t * 0.21f), Mathf.Sin(t * 0.37f + 1.7f) * 0.35f, Mathf.Cos(t * 0.17f)) * 0.05f;

            Vector3 eye = head.position;
            float eyeDepth = Waves.HeightAt(eye.x, eye.z) - eye.y;
            bool wantsUp = vert > 0f || (mv.y > 0.2f && pitch < -12f);
            if (diveGrace > 0f)
            {
                // duck dive: get the head under before the surface checks start
                diveGrace -= dt;
                vel.y = Mathf.Min(vel.y, -1.3f);
                drift = Vector3.zero;
            }
            else if (eyeDepth < 0.45f && !wantsUp)
            {
                // stay below the moving surface unless deliberately surfacing
                float push = (eyeDepth - 0.45f) * 3f;
                if (vel.y > push) vel.y = push;
                if (drift.y > 0f) drift.y = 0f;
            }

            cc.Move((vel + drift) * dt);
            ProbeGround();
            platform = null;
            platformRb = null;
            bobAmp = Mathf.Lerp(bobAmp, 0f, 1f - Mathf.Exp(-8f * dt));

            if (diveGrace <= 0f)
            {
                eye = head.position;
                eyeDepth = Waves.HeightAt(eye.x, eye.z) - eye.y;
                // surfaced deliberately, or pushed up by shallow ground
                if (eyeDepth < 0.02f || (wantsUp && eyeDepth < 0.35f && vel.y > 0.1f))
                {
                    mode = PlayerMoveMode.Surface;
                    lastWaveValid = false;
                    sprintingUnderwater = false;
                }
            }
        }

        // ------------------------------------------------------------------ water state
        void UpdateWaterState(float dt)
        {
            Vector3 p = transform.position;
            float h = Waves.HeightAt(p.x, p.z);
            Vector3 eye = head.position;
            float hEye = Waves.HeightAt(eye.x, eye.z);
            depth = Mathf.Max(0f, hEye - eye.y);

            bool inWater = isInWater;
            if (seated) inWater = false;
            else if (!isInWater)
            {
                if (p.y < h - 1f) inWater = true;
                else if (p.y < h - 0.5f)
                {
                    enterTimer += dt;
                    if (enterTimer >= 0.2f || !grounded) inWater = true;
                }
                else enterTimer = 0f;
            }
            else if (!vaulting)
            {
                if (grounded && p.y > h + 0.1f) inWater = false;
                else if (grounded && p.y > h - 0.3f)
                {
                    exitTimer += dt;
                    if (exitTimer >= 0.35f) inWater = false;
                }
                else exitTimer = 0f;
            }

            if (inWater != isInWater)
            {
                isInWater = inWater;
                enterTimer = 0f;
                exitTimer = 0f;
                if (inWater)
                {
                    platform = null;
                    platformRb = null;
                    lastWaveValid = false;
                    outOfWaterTimer = 0f;
                    if (GameInput.Descend && !UIState.BlocksGameplay && !grounded) StartDive();
                    else mode = PlayerMoveMode.Surface;
                }
                else
                {
                    mode = PlayerMoveMode.Land;
                    OnLeftWater();
                }
            }

            bool under = !seated && (isUnderwater ? eye.y < hEye + 0.02f : eye.y < hEye - 0.02f);
            if (under != isUnderwater)
            {
                isUnderwater = under;
                GameEvents.RaiseUnderwaterChanged(under);
            }
        }

        void OnLeftWater()
        {
            outOfWaterTimer = 0f;
            sprintingUnderwater = false;
            canVaultHint = false;
            if (vel.y > 0f) vel.y = 0f;
            var gm = GameManager.I;
            if (gm != null && gm.InGame && !menuMode) Footage.BankDive();
        }

        /// After a teleport the hysteresis timers must not delay the state: decide immediately.
        void RecheckWaterAfterTeleport()
        {
            Vector3 p = transform.position;
            float h = Waves.HeightAt(p.x, p.z);
            bool inWater = !seated && p.y < h - 0.5f;
            if (isInWater && !inWater)
            {
                isInWater = false;
                OnLeftWater();
            }
            else if (!isInWater && inWater)
            {
                isInWater = true;
                outOfWaterTimer = 0f;
            }
            if (!isInWater) mode = PlayerMoveMode.Land;
            else
            {
                Vector3 eye = head.position;
                mode = eye.y < Waves.HeightAt(eye.x, eye.z) - 0.3f ? PlayerMoveMode.Dive : PlayerMoveMode.Surface;
            }
            enterTimer = 0f;
            exitTimer = 0f;
            diveGrace = 0f;
            lastWaveValid = false;
            grounded = false;
            wasGrounded = false;
        }

        // ------------------------------------------------------------------ climbing out (boat hull, dock, rocks)
        bool CheckVault(out Vector3 target, out Collider col)
        {
            target = Vector3.zero;
            col = null;
            Vector3 p = transform.position;
            float h = Waves.HeightAt(p.x, p.z);
            Vector3 fwd = YawForward();
            int mask = Physics.DefaultRaycastLayers;

            // a wall/ledge right in front of us at or above the waterline (not a gentle beach slope)
            bool wall = false;
            for (int i = 0; i < 2; i++)
            {
                Vector3 o = new Vector3(p.x, h + (i == 0 ? 0.3f : 0.95f), p.z);
                if (Physics.SphereCast(o, 0.25f, fwd, out RaycastHit wh, Radius + 0.6f, mask, QueryTriggerInteraction.Ignore) && wh.normal.y < 0.6f)
                {
                    wall = true;
                    break;
                }
            }
            if (!wall) return false;

            // walkable top surface just behind the edge
            Vector3 origin = new Vector3(p.x, Mathf.Max(h, p.y) + 2.2f, p.z) + fwd * (Radius + 0.55f);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3.4f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (hit.normal.y < 0.7f) return false;
            float top = hit.point.y;
            if (top < h - 0.2f || top > h + 1.7f) return false;

            // room to stand there
            Vector3 tp = hit.point + Vector3.up * 0.05f;
            Vector3 a = tp + Vector3.up * (Radius + 0.04f);
            Vector3 b = tp + Vector3.up * (Height - Radius);
            if (Physics.CheckCapsule(a, b, Radius * 0.92f, mask, QueryTriggerInteraction.Ignore)) return false;

            target = tp;
            col = hit.collider;
            return true;
        }

        void TryVault()
        {
            if (!CheckVault(out Vector3 target, out Collider col)) return;
            vaulting = true;
            vaultT = 0f;
            vaultFrom = transform.position;
            vaultTargetWorld = target;
            Rigidbody rb = col.attachedRigidbody;
            vaultRef = rb != null ? rb.transform : col.transform;
            vaultTargetLocal = vaultRef.InverseTransformPoint(target);
            cc.enabled = false;
            vel = Vector3.zero;
            landDip = 0.06f;
            canVaultHint = false;
            sprintingUnderwater = false;
        }

        void UpdateVault(float dt)
        {
            vaultT += dt / 0.6f;
            Vector3 target = vaultRef != null ? vaultRef.TransformPoint(vaultTargetLocal) : vaultTargetWorld;
            float rise = Smooth01(vaultT / 0.65f);
            float across = Smooth01((vaultT - 0.35f) / 0.65f);
            transform.position = new Vector3(
                Mathf.Lerp(vaultFrom.x, target.x, across),
                Mathf.Lerp(vaultFrom.y, target.y + 0.08f, rise),
                Mathf.Lerp(vaultFrom.z, target.z, across));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (vaultT < 1f) return;

            vaulting = false;
            vaultRef = null;
            cc.enabled = true;
            vel = Vector3.zero;
            suppressLandingConvert = true;
            cc.Move(Vector3.down * 0.12f);
            ProbeGround();
            grounded = grounded || groundHit;
            UpdatePlatform();
            landDip = 0.05f;
        }

        void UpdateVaultHint(float dt)
        {
            if (mode != PlayerMoveMode.Surface || vaulting || seated || !isInWater || IsFainting || rescueActive)
            {
                canVaultHint = false;
                vaultHintTimer = 0f;
                return;
            }
            vaultHintTimer -= dt;
            if (vaultHintTimer > 0f) return;
            vaultHintTimer = 0.2f;
            canVaultHint = CheckVault(out _, out _);
        }

        // ------------------------------------------------------------------ seats (boat helm)
        public void SetSeated(Transform seatTransform)
        {
            if (seatTransform == null || !initialized) return;
            if (vaulting) { vaulting = false; vaultRef = null; }
            seat = seatTransform;
            seated = true;
            seatYaw = 0f;
            if (cc != null) cc.enabled = false;
            vel = Vector3.zero;
            platform = null;
            platformRb = null;
            grounded = false;
            canVaultHint = false;
            if (isInWater)
            {
                isInWater = false;
                OnLeftWater();
            }
            mode = PlayerMoveMode.Land;
            pitch = Mathf.Clamp(pitch, -45f, 45f);
            FollowSeat();
            lastPosValid = false;
            camRotValid = false;
        }

        public void ClearSeated(Vector3 exitPosition)
        {
            if (!initialized) return;
            Vector3 f = transform.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 1e-4f) yaw = Mathf.Repeat(Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 360f);
            seated = false;
            seat = null;
            seatYaw = 0f;
            PlaceAt(exitPosition, yaw);
        }

        void FollowSeat()
        {
            if (seat == null)
            {
                // the seat was destroyed under us
                ClearSeated(transform.position + Vector3.up * 0.3f);
                return;
            }
            transform.SetPositionAndRotation(seat.position, seat.rotation * Quaternion.Euler(0f, seatYaw, 0f));
        }

        // ------------------------------------------------------------------ teleport
        public void TeleportTo(Vector3 position, float yawDegrees)
        {
            if (!initialized) return;
            if (seated) { seated = false; seat = null; seatYaw = 0f; }
            if (vaulting) { vaulting = false; vaultRef = null; }
            PlaceAt(position + Vector3.up * 0.02f, yawDegrees);
        }

        void PlaceAt(Vector3 position, float yawDegrees)
        {
            yaw = Mathf.Repeat(yawDegrees, 360f);
            bool wasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            if (cc != null && (wasEnabled || !menuMode) && !seated) cc.enabled = true;
            Physics.SyncTransforms();
            vel = Vector3.zero;
            platform = null;
            platformRb = null;
            platformVel = Vector3.zero;
            suppressLandingConvert = true;
            lastPosValid = false;
            camRotValid = false;
            canVaultHint = false;
            RecheckWaterAfterTeleport();
        }
    }
}
