using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// One living (or hallucinated) animal. Kinematic steering (no Rigidbody, no colliders): the behavior writes a
    /// desired velocity; this component adds depth-band, terrain, obstacle and leash steering, integrates the motion,
    /// orients the body, animates the rig and keeps WorldBounds / Activity01 up to date.
    /// Ticked centrally by FaunaSpawner (lower rate when far away). Registers in CreatureRegistry while active.
    public sealed partial class FaunaCreature : MonoBehaviour, ICreature
    {
        // ------------------------------------------------------------------ ICreature
        public SpeciesDef Species => species;
        public float SizeCm => sizeCm;
        public SizeTier Tier => tier;
        public Transform Transform => tr;
        public Vector3 Position => pos;
        public Bounds WorldBounds => bounds;
        public float Activity01 => activity;
        public bool IsHallucination => hallucination;
        public bool IsAlive => alive && !pendingDespawn && this != null && gameObject.activeInHierarchy;

        // ------------------------------------------------------------------ identity
        internal SpeciesDef species;
        internal float sizeCm;
        internal SizeTier tier;
        internal bool hallucination;
        internal bool alive;
        internal bool initialized;

        // ------------------------------------------------------------------ body facts
        internal float lengthM;
        internal float halfHeight;
        internal float clearance;        // preferred height of the body center above the seafloor
        internal float hardClearance;    // absolute minimum
        internal float cruise;           // m/s
        internal float maxPitch, maxBank, turnRate;
        internal bool uprightOnly;
        internal bool swimsForward;

        // ------------------------------------------------------------------ kinematics
        internal Vector3 pos;
        internal Vector3 velocity;
        internal float groundY, surfaceY;
        internal Vector3 leashCenter;
        internal float leashRadius;
        internal Vector3 home;

        // ------------------------------------------------------------------ behavior outputs (reset every tick)
        internal Vector3 desiredVel;
        internal bool hasFaceDir;
        internal Vector3 faceDir;
        internal bool relaxBand;
        internal bool freePitch;
        internal float accelMul, turnMul;
        internal float activityTarget;
        internal float extraRoll;        // persistent: sunfish basking etc.
        internal float bellPulse;        // persistent: jelly pulse 0..1
        internal float cloak;            // persistent: vampire squid flare 0..1
        internal float lureMul = 1f;     // persistent: lure brightness multiplier / round-2 action channel (1 rest … 3 full)

        // ------------------------------------------------------------------ round 2: locomotion modes, directors
        /// Swim (fish …), Ground (crabs, starfish, walking cuttlefish, land animals) or Fly (birds, bats).
        internal FaunaMode mode;
        /// Reset every tick: the swimmer may leave the water (dolphin jumps, whale breach, flying-fish glides).
        internal bool allowAir;
        /// Persistent: flyer sits on perchPos (feet), ground animal clings to a wall / trunk at heightOverride.
        internal bool perched;
        internal Vector3 perchPos;
        internal bool hasHeightOverride;
        internal float heightOverride;
        /// Ground mode: float at the surface when the water is deeper than the legs (pig "Truffle" swims).
        internal bool floatsInWater;
        /// Spawned by LandFauna (island / shore animals): not part of the underwater zone population.
        internal bool landSpawned;
        /// Optional scripted control (animal events, bow riding, mischief): returns true when it steered this tick.
        internal IFaunaDirector director;
        /// Height of solid props (dock, rocks, hut floor) above the terrain at the current spot (ground mode).
        float propLift;
        int propCountdown;
        /// Persistent (set by the behavior): skip the obstacle probes (wall climbers move along walls on purpose).
        internal bool ignoreObstacles;
        /// Reset every tick: explicit body rotation (geckos on walls, perched birds facing out).
        internal bool hasRotOverride;
        internal Quaternion rotOverride;
        /// Land animals: home spot data handed out by LandFauna (perch, wall, pen …).
        internal FaunaLandSpot landSpot;

        // ------------------------------------------------------------------ bookkeeping (spawner)
        internal FaunaSpawner spawner;
        internal FaunaSchool school;
        internal FaunaBehavior behavior;
        internal int listIndex = -1;
        internal int slot;
        internal float accumDt;
        internal float distToPlayer = 999f;
        internal float distXZ = 999f;
        /// Distance to the LOCAL camera (rendering culling, lure light). Equals distToPlayer in single player;
        /// in co-op distToPlayer is the distance to the nearest diver.
        internal float distLocal = 999f;
        internal bool pendingDespawn;
        /// Co-op replication id (1..65535) of a real creature, 0 for hallucinations. Assigned by the spawner on every
        /// (re)activation, so a pooled object that becomes a new animal also gets a new id.
        internal int netId;
        public int NetId => netId;

        // ------------------------------------------------------------------ visuals
        internal Transform tr;
        internal CreatureRig rig;
        internal FaunaSwimAnimator anim;
        Renderer[] renderers;
        bool[] rendererDefault;
        ShadowCastingMode[] shadowDefault;
        internal bool culled;
        float cullDistance;
        /// Round 3: the model has see-through materials (jellies) – hidden from above the water (they would be drawn on
        /// top of the sea surface, which is rendered before them).
        bool transparentModel;
        float activity = 0.2f;
        Bounds bounds;
        int boundsCountdown;
        float bank;
        float yawRate;
        float lastYaw;

        // obstacle probing
        Vector3 avoidVec;
        float avoidTimer;
        int rayCountdown;

        // hallucination ghost materials
        List<Material> ghostMats;
        List<Color> ghostBase;
        List<Color> ghostEmission;
        float ghostAlpha = 1f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        // ================================================================== lifecycle

        /// One-time setup right after the instance was cloned from the species template (object is inactive).
        internal void Setup(SpeciesDef s)
        {
            tr = transform;
            species = s;
            rig = GetComponent<CreatureRig>();
            if (rig == null) rig = gameObject.AddComponent<CreatureRig>();
            renderers = rig.renderers != null && rig.renderers.Length > 0 ? rig.renderers : GetComponentsInChildren<Renderer>(true);
            rendererDefault = new bool[renderers.Length];
            shadowDefault = new ShadowCastingMode[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                rendererDefault[i] = r != null && r.enabled;
                shadowDefault[i] = r != null ? r.shadowCastingMode : ShadowCastingMode.Off;
            }
            anim = new FaunaSwimAnimator();
            anim.Setup(rig, s);
            transparentModel = HasTransparentMaterial(renderers);
        }

        /// True when any renderer of the model uses a transparent material (render queue ≥ 2950).
        internal static bool HasTransparentMaterial(Renderer[] rs)
        {
            if (rs == null) return false;
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int k = 0; k < mats.Length; k++) if (mats[k] != null && mats[k].renderQueue >= 2950) return true;
            }
            return false;
        }

        /// (Re)activates the creature from the pool. The GameObject is switched on at the end.
        internal void Activate(FaunaSpawner owner, SpeciesDef s, float cm, bool halluc, Vector3 p, FaunaSchool sc,
                               Vector3 leashC, float leashR)
        {
            spawner = owner;
            species = s;
            sizeCm = Mathf.Max(0.5f, cm);
            tier = Progression.ClassifySize(s, sizeCm);
            hallucination = halluc;
            school = sc;
            leashCenter = leashC;
            leashRadius = leashR;
            pendingDespawn = false;
            accumDt = 0f;
            culled = false;
            avoidTimer = 0f;
            rayCountdown = Random.Range(0, 4);
            boundsCountdown = 0;
            extraRoll = 0f;
            bellPulse = 0f;
            cloak = 0f;
            lureMul = 1f;
            bank = 0f;
            yawRate = 0f;
            allowAir = false;
            perched = false;
            hasHeightOverride = false;
            landSpawned = false;
            director = null;
            ignoreObstacles = false;
            propLift = 0f;
            propCountdown = 0;
            ResetCombat();

            ComputeBodyFacts();

            pos = p;
            home = p;
            float yaw = Random.Range(0f, 360f);
            tr.SetPositionAndRotation(p, Quaternion.Euler(0f, yaw, 0f));
            tr.localScale = Vector3.one * lengthM;
            lastYaw = yaw;
            velocity = tr.forward * cruise * 0.5f;
            groundY = WorldInfo.GroundHeight(p);
            surfaceY = Waves.HeightAt(p.x, p.z);

            // shadows: only big animals keep the model's shadow setting (small fish never cast: cheap with 160 animals)
            bool bigEnough = !halluc && lengthM >= 0.8f;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                r.enabled = rendererDefault[i];
                r.shadowCastingMode = bigEnough ? shadowDefault[i] : ShadowCastingMode.Off;
            }
            if (anim != null) anim.SetLureEnabled(true);

            if (behavior == null || behavior.IsHallucination != halluc) behavior = FaunaBehavior.Create(s, halluc);
            behavior.Bind(this);

            activity = 0.2f;
            alive = true;
            initialized = true;
            behavior.Reset();

            if (halluc) MakeGhost();

            if (!gameObject.activeSelf) gameObject.SetActive(true);
            else CreatureRegistry.Register(this);
            UpdateBounds();
        }

        void ComputeBodyFacts()
        {
            var s = species;
            lengthM = sizeCm / 100f;
            float h = Mathf.Max(0.04f, s.bodyHeight);
            switch (s.plan)
            {
                case BodyPlan.Jelly: halfHeight = lengthM * 0.45f + lengthM * 0.6f; break;     // bell + hanging tentacles
                case BodyPlan.Ray: halfHeight = Mathf.Max(0.04f, h * lengthM * 0.5f); break;
                case BodyPlan.Seahorse: halfHeight = lengthM * 0.5f; break;
                case BodyPlan.Octopus: halfHeight = lengthM * 0.35f; break;
                default: halfHeight = h * lengthM * 0.5f; break;
            }
            bool bottom = s.behavior == BehaviorType.BottomDweller || s.plan == BodyPlan.Eel || s.plan == BodyPlan.Octopus;
            clearance = (bottom ? 0.12f : 0.6f) + halfHeight;
            hardClearance = halfHeight + (bottom ? 0.03f : 0.15f);

            float minSpeed;
            switch (s.plan)
            {
                case BodyPlan.Jelly: minSpeed = 0.05f; break;
                case BodyPlan.Seahorse: minSpeed = 0.03f; break;
                case BodyPlan.VampireSquid: minSpeed = 0.08f; break;
                default: minSpeed = 0.14f; break;
            }
            cruise = Mathf.Clamp(s.speed * lengthM, minSpeed, 1.8f);

            uprightOnly = s.plan == BodyPlan.Jelly || s.plan == BodyPlan.Seahorse;
            swimsForward = !uprightOnly && s.plan != BodyPlan.Octopus && s.plan != BodyPlan.Cuttlefish && s.plan != BodyPlan.VampireSquid;
            switch (s.plan)
            {
                case BodyPlan.Shark: maxPitch = 25f; maxBank = 16f; break;
                case BodyPlan.Ray: maxPitch = 18f; maxBank = 28f; break;
                case BodyPlan.SeaLion: maxPitch = 50f; maxBank = 40f; break;
                case BodyPlan.Turtle: maxPitch = 30f; maxBank = 18f; break;
                case BodyPlan.Sunfish: maxPitch = 18f; maxBank = 10f; break;
                case BodyPlan.Eel: maxPitch = 35f; maxBank = 6f; break;
                case BodyPlan.Dolphin: maxPitch = 45f; maxBank = 30f; break;
                case BodyPlan.Whale: maxPitch = 22f; maxBank = 8f; break;
                case BodyPlan.Octopus: case BodyPlan.Squid: case BodyPlan.Cuttlefish: case BodyPlan.VampireSquid:
                    maxPitch = 25f; maxBank = 8f; break;
                default: maxPitch = 35f; maxBank = 12f; break;
            }
            turnRate = Mathf.Clamp(220f / (0.6f + lengthM), 18f, 300f);

            float glowBoost = s.glows ? 1.6f : 1f;
            cullDistance = Mathf.Clamp(lengthM * 140f * glowBoost, 32f, 165f);

            // ---- round 2 locomotion
            mode = FaunaModes.Of(s);
            floatsInWater = s.land && s.plan == BodyPlan.Quadruped;
            switch (mode)
            {
                case FaunaMode.Ground:
                    halfHeight = Mathf.Max(0.02f, h * lengthM * 0.5f);
                    clearance = halfHeight;
                    hardClearance = halfHeight * 0.9f;
                    cruise = Mathf.Clamp(s.speed * lengthM, 0.015f, 1.6f);
                    uprightOnly = false;
                    swimsForward = false;
                    maxPitch = 30f; maxBank = 8f;
                    turnRate = Mathf.Clamp(260f / (0.5f + lengthM), 40f, 360f);
                    if (s.land) cullDistance = Mathf.Clamp(lengthM * 90f, 45f, 90f);
                    break;
                case FaunaMode.Fly:
                    halfHeight = Mathf.Max(0.03f, h * lengthM * 0.5f);
                    clearance = halfHeight + 0.3f;
                    hardClearance = halfHeight;
                    cruise = Mathf.Clamp(s.speed * lengthM * 4f, 1.6f, 7f);   // birds fly a lot faster than they swim
                    uprightOnly = false;
                    swimsForward = true;
                    maxPitch = 35f; maxBank = 38f;
                    turnRate = 140f;
                    cullDistance = 150f;
                    break;
            }
        }

        void OnEnable()
        {
            if (initialized && alive) CreatureRegistry.Register(this);
        }

        void OnDisable()
        {
            CreatureRegistry.Unregister(this);
        }

        void OnDestroy()
        {
            CreatureRegistry.Unregister(this);
            alive = false;
            DestroyGhostMaterials();
            if (spawner != null) spawner.NotifyDestroyed(this);
        }

        /// Called by the spawner when the creature goes back to the pool / gets destroyed.
        internal void Deactivate()
        {
            alive = false;
            if (school != null) { school.Remove(this); school = null; }
            CreatureRegistry.Unregister(this);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        /// Behaviors call this to remove themselves (handled safely after the update loop).
        internal void RequestDespawn() { pendingDespawn = true; }

        /// LandFauna: hands the animal its home spot and restarts the behavior there.
        internal void SetLandSpot(FaunaLandSpot spot)
        {
            landSpot = spot;
            landSpawned = true;
            home = spot.pos;
            leashCenter = spot.pos;
            if (behavior != null) behavior.Reset();
        }

        // ================================================================== update

        internal void Tick(float dt, bool nearLod)
        {
            if (!alive || tr == null || dt <= 0f) return;

            groundY = WorldInfo.GroundHeight(pos);
            surfaceY = Waves.HeightAt(pos.x, pos.z);

            desiredVel = Vector3.zero;
            hasFaceDir = false;
            relaxBand = false;
            freePitch = false;
            allowAir = false;
            hasRotOverride = false;
            hasHeightOverride = false;
            accelMul = 1f;
            turnMul = 1f;
            activityTarget = 0.2f;

            bool directed = director != null && director.Steer(this, dt);
            if (!directed && !TickHitFlee(dt)) behavior.Tick(dt);
            if (pendingDespawn) return;

            if (mode == FaunaMode.Ground) { TickGround(dt, nearLod); Finish(dt, nearLod); return; }
            if (mode == FaunaMode.Fly) { TickFly(dt, nearLod); Finish(dt, nearLod); return; }

            Vector3 desired = desiredVel;
            Steer(ref desired, dt, nearLod);

            float accel = Mathf.Max(0.4f, cruise * 1.6f) * accelMul;
            velocity = Vector3.MoveTowards(velocity, desired, accel * dt);

            // streamlined swimmers move along their heading (smooth arcs instead of sideways sliding)
            if (swimsForward && !hasFaceDir)
            {
                float sp = velocity.magnitude;
                if (sp > cruise * 0.3f) velocity = Vector3.Lerp(velocity, tr.forward * sp, Mathf.Clamp01(dt * 3f));
            }

            // ---- integrate with hard limits (never on land, never above surface-1, never in the ground)
            Vector3 np = pos + velocity * dt;
            float gN = WorldInfo.GroundHeight(np);
            if (gN > FaunaNav.LandGuardY)
            {
                np.x = pos.x; np.z = pos.z;
                velocity.x *= -0.3f; velocity.z *= -0.3f;
                gN = groundY;
            }
            float top = surfaceY - (hallucination ? 0.7f : 1f) - halfHeight;
            float bottom = gN + hardClearance;
            if (allowAir)
            {
                // jumps / breaches / glides: free above the surface, ballistic once out of the water
                if (np.y > surfaceY) velocity.y -= 9.81f * dt * (species.plan == BodyPlan.Fish ? 0.35f : 1f);
            }
            else if (np.y > top)
            {
                // back under after an air moment: sink smoothly instead of snapping
                if (np.y > top + 0.4f) np.y = Mathf.MoveTowards(pos.y, top, Mathf.Max(2f, -velocity.y) * dt);
                else np.y = top;
                if (velocity.y > 0f) velocity.y = 0f;
            }
            if (np.y < bottom) { np.y = bottom; if (velocity.y < 0f) velocity.y = 0f; }
            pos = np;
            tr.position = pos;
            Finish(dt, nearLod);
        }

        /// Shared tail of every tick: rotation, activity, culling, animation, bounds.
        void Finish(float dt, bool nearLod)
        {
            UpdateRotation(dt);

            // ---- activity: behaviors set targets; raw speed adds a little
            float spd = velocity.magnitude;
            float speedAct = 0.2f + 0.3f * Mathf.Clamp01(spd / Mathf.Max(0.05f, cruise) - 1f);
            float at = Mathf.Max(activityTarget, speedAct);
            activity = Mathf.MoveTowards(activity, at, dt * (at > activity ? 2.5f : 0.6f));

            // ---- distance culling (renderers + lure light). Seen from above the water (dock, beach, boat) animals are
            // hidden by the nearly opaque sea: cull them beyond 35 m, and deeper than 5 m beyond the 12 m scan range.
            bool above = !FXVisibility.Underwater;
            // island / shore animals and flyers are seen through the air; breaching whales and jumping dolphins too
            bool inAir = (mode != FaunaMode.Swim && pos.y > surfaceY - 0.3f) || (allowAir && pos.y > surfaceY - 1.5f);
            float lim = inAir ? (above ? cullDistance : Mathf.Min(cullDistance, 25f)) : (above ? Mathf.Min(cullDistance, 35f) : cullDistance);
            bool shouldCull = culled ? distLocal > lim - 5f : distLocal > lim + 5f;
            if (above && !inAir && pos.y < surfaceY - 5f && distLocal > FilmShotEvaluator.MaxScanRange) shouldCull = true;
            // round 3: see-through animals (jellies) would shine through the sea surface – hide them from above
            if (above && transparentModel && pos.y < surfaceY - 0.5f) shouldCull = true;
            if (hallucination) shouldCull = false;
            if (shouldCull != culled) SetCulled(shouldCull);
            if (anim != null)
            {
                anim.SetLureEnabled(!culled && distLocal < 50f);
                // the swim cycle only runs while a camera actually sees the animal (frustum, far clip, photo camera)
                if (!culled && AnyRendererVisible())
                    anim.Animate(dt, spd / Mathf.Max(0.05f, cruise), lengthM, activity, yawRate, bellPulse, cloak,
                                 lureMul * (hallucination ? ghostAlpha : 1f), FaunaContext.Time + slot * 1.7f);
            }

            if (--boundsCountdown <= 0)
            {
                boundsCountdown = nearLod ? 4 : 1;
                UpdateBounds();
            }
        }

        // ================================================================== round 2: ground and flight

        /// Walkers (crabs, starfish, the flamboyant cuttlefish, island animals): follow the terrain and solid props,
        /// stay inside their depth / height band (beach crabs on the sand, reef crawlers under water), pigs swim.
        void TickGround(float dt, bool nearLod)
        {
            Vector3 desired = desiredVel;
            desired.y = 0f;
            Vector3 mv = velocity.sqrMagnitude > 1e-4f ? velocity : tr.forward;
            mv.y = 0f;
            float hspd = Mathf.Sqrt(velocity.x * velocity.x + velocity.z * velocity.z);
            if (!ignoreObstacles && distLocal < 70f && --rayCountdown <= 0)
            {
                rayCountdown = nearLod ? 4 : 2;
                ProbeObstacles(mv, hspd);
            }
            if (avoidTimer > 0f)
            {
                avoidTimer -= dt;
                desired += new Vector3(avoidVec.x, 0f, avoidVec.z);
            }
            float accel = Mathf.Max(0.3f, cruise * 3f) * accelMul;
            Vector3 hv = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), desired, accel * dt);
            Vector3 np = pos + hv * dt;
            float g = WorldInfo.GroundHeight(np);
            if (!GroundOk(g))
            {
                np.x = pos.x; np.z = pos.z;
                hv *= -0.3f;
                g = groundY;
            }
            if (--propCountdown <= 0)
            {
                propCountdown = nearLod ? 5 : 2;
                propLift = PropLift(np, g);
            }
            float floor = g + propLift;
            // a wall of props in front (hut, big rock): stop and let the obstacle probe turn us
            if (!hasHeightOverride && floor - (pos.y - halfHeight) > Mathf.Max(0.5f, lengthM * 1.5f))
            {
                np.x = pos.x; np.z = pos.z;
                hv *= -0.2f;
                floor = groundY + propLift;
            }
            float targetY = floor + halfHeight;
            if (hasHeightOverride) targetY = heightOverride;
            else if (floatsInWater && floor < surfaceY - halfHeight * 1.6f) targetY = surfaceY - halfHeight * 0.45f;   // swimming
            float ny = Mathf.Lerp(pos.y, targetY, Mathf.Clamp01(dt * 14f));
            np.y = ny;
            velocity = new Vector3(hv.x, (ny - pos.y) / Mathf.Max(dt, 1e-4f) * 0.25f, hv.z);
            pos = np;
            tr.position = pos;
        }

        /// Ground heights this walker may stand on: its depth band (positive = below sea level) with a small margin.
        internal bool GroundOk(float groundY)
        {
            var s = species;
            float depth = Waves.SeaLevel - groundY;
            return depth <= s.maxDepth + 0.6f && depth >= s.minDepth - 0.4f;
        }

        float PropLift(Vector3 at, float g)
        {
            float start = Mathf.Max(g, pos.y - halfHeight) + 0.6f;
            if (Physics.Raycast(new Vector3(at.x, start, at.z), Vector3.down, out RaycastHit hit, 1.4f, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore)
                && !FaunaContext.IsPlayerCollider(hit.collider) && hit.point.y > g + 0.04f)
                return hit.point.y - g;
            return 0f;
        }

        /// Birds and bats: free flight above ground and water; perched birds sit on perchPos.
        void TickFly(float dt, bool nearLod)
        {
            if (perched)
            {
                Vector3 target = perchPos + Vector3.up * (halfHeight * 0.9f);
                Vector3 np0 = Vector3.Lerp(pos, target, Mathf.Clamp01(dt * 8f));
                velocity = (np0 - pos) / Mathf.Max(dt, 1e-4f) * 0.2f;
                pos = np0;
                tr.position = pos;
                return;
            }
            Vector3 desired = desiredVel;
            Vector3 mv = velocity.sqrMagnitude > 1e-4f ? velocity : tr.forward;
            float spd = velocity.magnitude;
            if (!ignoreObstacles && distLocal < 90f && --rayCountdown <= 0)
            {
                rayCountdown = nearLod ? 4 : 2;
                ProbeObstacles(mv, spd);
            }
            if (avoidTimer > 0f)
            {
                avoidTimer -= dt;
                desired += avoidVec;
            }
            float accel = Mathf.Max(1.5f, cruise * 1.2f) * accelMul;
            velocity = Vector3.MoveTowards(velocity, desired, accel * dt);
            if (!hasFaceDir)
            {
                float sp = velocity.magnitude;
                if (sp > cruise * 0.3f) velocity = Vector3.Lerp(velocity, tr.forward * sp, Mathf.Clamp01(dt * 2f));
            }
            Vector3 np = pos + velocity * dt;
            float g = WorldInfo.GroundHeight(np);
            float floor = Mathf.Max(g, surfaceY) + halfHeight + 0.2f;
            if (np.y < floor) { np.y = floor; if (velocity.y < 0f) velocity.y = 0f; }
            if (np.y > 140f) { np.y = 140f; if (velocity.y > 0f) velocity.y = 0f; }
            pos = np;
            tr.position = pos;
        }

        void Steer(ref Vector3 desired, float dt, bool nearLod)
        {
            var s = species;
            float top, bottom;
            if (hallucination)
            {
                top = surfaceY - 0.8f - halfHeight;
                bottom = groundY + clearance;
            }
            else if (relaxBand)
            {
                top = surfaceY - 1f - halfHeight;
                bottom = groundY + hardClearance + 0.05f;
            }
            else
            {
                top = FaunaNav.BandTop(s, halfHeight, surfaceY);
                bottom = FaunaNav.BandBottom(s, groundY, clearance);
            }

            float vLimit = cruise * 1.5f + 0.3f;
            if (bottom > top)
            {
                // the seafloor here is too shallow for this species: head for deeper water
                Vector3 dh = FaunaNav.Downhill(pos);
                if (dh == Vector3.zero) dh = FaunaNav.FlatDir(leashCenter - pos);
                desired += dh * cruise;
                float targetY = Mathf.Max(groundY + clearance, Mathf.Min(-s.minDepth, surfaceY - 1f - halfHeight));
                desired.y += Mathf.Clamp((targetY - pos.y) * 1.2f, -vLimit, vLimit);
            }
            else if (pos.y > top)
            {
                desired.y = Mathf.Min(desired.y, 0f) - Mathf.Min((pos.y - top) * 1.2f, vLimit);
            }
            else if (pos.y < bottom)
            {
                desired.y = Mathf.Max(desired.y, 0f) + Mathf.Min((bottom - pos.y) * 1.2f, vLimit);
            }

            // ---- leash: stay in the dive site / around the spawn point
            if (leashRadius > 0f && !relaxBand && !hallucination)
            {
                float d = FaunaNav.DistXZ(pos, leashCenter);
                if (d > leashRadius)
                {
                    Vector3 to = FaunaNav.FlatDir(leashCenter - pos);
                    desired += to * (Mathf.Max(cruise, 0.2f) * 1.5f * Mathf.Clamp01((d - leashRadius) / 20f));
                }
            }

            // ---- terrain lookahead (cheap height samples)
            Vector3 mv = velocity.sqrMagnitude > 0.0004f ? velocity : tr.forward;
            Vector3 fwd = FaunaNav.FlatDir(mv);
            float spd = velocity.magnitude;
            if (fwd != Vector3.zero)
            {
                float look = Mathf.Clamp(lengthM * 1.2f + spd * 1.5f, 1.2f, 14f);
                Vector3 ahead = pos + fwd * look;
                float gA = WorldInfo.GroundHeight(ahead);
                if (gA > FaunaNav.LandGuardY - 0.4f)
                {
                    // coast ahead: turn toward deeper water
                    Vector3 dh = FaunaNav.Downhill(ahead);
                    if (dh == Vector3.zero) dh = -fwd;
                    desired += (dh * 1.5f - fwd * 0.6f) * Mathf.Max(cruise, 0.2f);
                }
                else if (gA + clearance > pos.y - 0.1f)
                {
                    float need = gA + clearance - pos.y;
                    if (gA + clearance < top) desired.y += Mathf.Min(need * 2f, cruise * 2f + 0.5f);   // climb over the slope
                    else
                    {
                        Vector3 dh = FaunaNav.Downhill(ahead);
                        desired += (dh - fwd * 0.5f) * Mathf.Max(cruise, 0.2f);
                    }
                }
            }

            // ---- props (rocks, wreck, kelp) via short raycasts forward and down; staggered
            if (distToPlayer < 90f && --rayCountdown <= 0)
            {
                rayCountdown = nearLod ? 4 : 2;
                ProbeObstacles(mv, spd);
            }
            if (avoidTimer > 0f)
            {
                avoidTimer -= dt;
                desired += avoidVec;
            }
        }

        void ProbeObstacles(Vector3 moveDir, float spd)
        {
            if (moveDir.sqrMagnitude < 1e-6f) return;
            Vector3 dir = moveDir.normalized;
            float look = Mathf.Clamp(lengthM * 0.8f + spd * 1.2f + 0.5f, 0.8f, 10f);
            Vector3 origin = pos + dir * (lengthM * 0.3f);
            if (Physics.Raycast(origin, dir, out RaycastHit hit, look, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore)
                && !FaunaContext.IsPlayerCollider(hit.collider))
            {
                Vector3 n = hit.normal;
                Vector3 slide = Vector3.ProjectOnPlane(dir, n);
                float urgency = 1.3f - hit.distance / look;
                avoidVec = (n * 1.3f + slide) * (Mathf.Max(cruise, 0.25f) * urgency);
                avoidTimer = 0.5f;
                return;
            }
            // something solid right below (wreck deck, rock)?
            float down = halfHeight + 0.5f;
            if (Physics.Raycast(pos, Vector3.down, out hit, down, FaunaContext.ObstacleMask, QueryTriggerInteraction.Ignore)
                && !FaunaContext.IsPlayerCollider(hit.collider) && hit.point.y > groundY + 0.2f)
            {
                avoidVec = Vector3.up * (Mathf.Max(cruise, 0.2f) * (1f - hit.distance / down + 0.2f));
                avoidTimer = 0.4f;
            }
        }

        void UpdateRotation(float dt)
        {
            Vector3 dir;
            if (hasFaceDir && faceDir.sqrMagnitude > 1e-6f) dir = faceDir;
            else if (velocity.sqrMagnitude > 0.0009f) dir = velocity;
            else dir = tr.forward;

            Vector3 curF = tr.forward;
            float curYaw = Mathf.Atan2(curF.x, curF.z) * Mathf.Rad2Deg;
            Quaternion target;
            if (hasRotOverride)
            {
                target = rotOverride;
            }
            else if (freePitch && !uprightOnly)
            {
                Vector3 up = tr.up;
                if (Mathf.Abs(Vector3.Dot(dir.normalized, up)) > 0.98f) up = Vector3.up;
                target = Quaternion.LookRotation(dir, up);
            }
            else
            {
                float h = Mathf.Sqrt(dir.x * dir.x + dir.z * dir.z);
                float yaw = h > 1e-4f ? Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg : curYaw;
                float pitch = uprightOnly ? 0f : Mathf.Clamp(Mathf.Atan2(dir.y, Mathf.Max(h, 1e-4f)) * Mathf.Rad2Deg, -maxPitch, maxPitch);
                float yawDelta = Mathf.DeltaAngle(curYaw, yaw);
                float bankTarget = uprightOnly ? 0f : Mathf.Clamp(-yawDelta * 0.35f, -maxBank, maxBank);
                bank = Mathf.MoveTowards(bank, bankTarget + extraRoll, 70f * dt);
                target = Quaternion.Euler(-pitch, yaw, bank);
            }
            // slow hoverers turn lazily (no jitter from tiny velocity changes); explicit facing turns at full rate
            float spdFactor = hasFaceDir || freePitch || hasRotOverride ? 1f : Mathf.Clamp(velocity.magnitude / Mathf.Max(0.05f, cruise * 0.5f), 0.2f, 1f);
            tr.rotation = Quaternion.RotateTowards(tr.rotation, target, turnRate * turnMul * spdFactor * dt);

            Vector3 nf = tr.forward;
            float newYaw = Mathf.Atan2(nf.x, nf.z) * Mathf.Rad2Deg;
            float rate = Mathf.DeltaAngle(lastYaw, newYaw) / Mathf.Max(dt, 1e-4f);
            yawRate = Mathf.Lerp(yawRate, rate, Mathf.Clamp01(dt * 6f));
            lastYaw = newYaw;
        }

        bool AnyRendererVisible()
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r != null && r.enabled && r.isVisible) return true;
            }
            return false;
        }

        void SetCulled(bool c)
        {
            culled = c;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = !c && rendererDefault[i];
            if (anim != null) anim.SetLureEnabled(!c && distLocal < 50f);
        }

        void UpdateBounds()
        {
            if (!culled)
            {
                bool any = false;
                Bounds b = default;
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r == null || !r.enabled) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (any && b.size.sqrMagnitude > 1e-8f) { bounds = b; return; }
            }
            float l = Mathf.Max(0.05f, lengthM);
            float w = Mathf.Max(species.bodyWidth, species.bodyHeight) * l;
            bounds = new Bounds(pos, new Vector3(Mathf.Max(l, w), Mathf.Max(halfHeight * 2f, 0.05f), Mathf.Max(l, w)));
        }

        // ================================================================== helpers for behaviors

        internal Vector3 HeadPos => pos + tr.forward * (lengthM * 0.45f);

        /// Desired velocity toward a point with arrival slowdown.
        internal Vector3 Seek(Vector3 target, float speed, float slowRadius = 0f)
        {
            Vector3 d = target - pos;
            float m = d.magnitude;
            if (m < 1e-4f) return Vector3.zero;
            if (slowRadius <= 0f) slowRadius = Mathf.Max(0.6f, lengthM * 1.5f);
            float s = m < slowRadius ? speed * (m / slowRadius) : speed;
            return d * (s / m);
        }

        /// Desired velocity away from the player (with a bit of sideways/vertical spice).
        internal Vector3 FleeFromPlayer(float speed, float spice)
        {
            Vector3 away = pos - FaunaContext.PlayerPos;
            if (away.sqrMagnitude < 1e-4f) away = -tr.forward;
            away.Normalize();
            away += new Vector3(Mathf.Sin(FaunaContext.Time * 1.3f + slot), Mathf.Sin(FaunaContext.Time * 0.9f + slot * 2f) * 0.5f, Mathf.Cos(FaunaContext.Time * 1.1f + slot)) * spice;
            return away.normalized * speed;
        }

        internal Vector3 ToPlayer => FaunaContext.PlayerPos - pos;

        internal bool PickWanderTarget(float minR, float maxR, float bottomBias, out Vector3 target)
        {
            return FaunaNav.TryPickPoint(species, pos, minR, maxR, leashCenter, leashRadius, clearance, halfHeight, bottomBias, out target);
        }

        // ================================================================== hallucination ghosting

        void MakeGhost()
        {
            ghostMats = new List<Material>();
            ghostBase = new List<Color>();
            ghostEmission = new List<Color>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var shared = r.sharedMaterials;
                var copies = new Material[shared.Length];
                for (int j = 0; j < shared.Length; j++)
                {
                    var src = shared[j];
                    if (src == null) continue;
                    var m = new Material(src) { name = src.name + "_Ghost" };
                    MaterialLibrary.MakeTransparent(m, false);
                    Color c = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
                    Color e = m.HasProperty(EmissionColorId) ? m.GetColor(EmissionColorId) : Color.black;
                    ghostMats.Add(m);
                    ghostBase.Add(c);
                    ghostEmission.Add(e);
                    copies[j] = m;
                }
                r.sharedMaterials = copies;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            SetGhostAlpha(0f);
        }

        internal void SetGhostAlpha(float a)
        {
            ghostAlpha = Mathf.Clamp01(a);
            if (ghostMats == null) return;
            for (int i = 0; i < ghostMats.Count; i++)
            {
                var m = ghostMats[i];
                if (m == null) continue;
                Color c = ghostBase[i];
                c.a *= ghostAlpha;
                m.SetColor(BaseColorId, c);
                Color e = ghostEmission[i];
                if (e.maxColorComponent > 0f) m.SetColor(EmissionColorId, e * ghostAlpha);
            }
        }

        void DestroyGhostMaterials()
        {
            if (ghostMats == null) return;
            for (int i = 0; i < ghostMats.Count; i++)
                if (ghostMats[i] != null) Destroy(ghostMats[i]);
            ghostMats = null;
        }
    }
}
