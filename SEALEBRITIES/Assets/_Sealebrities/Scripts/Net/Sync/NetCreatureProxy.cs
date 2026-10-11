using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Co-op client stand-in for one of the host's animals: same model as the real creature (Fauna template clone),
    /// pose interpolated from the host's 10 Hz snapshots, procedural swim animation (FaunaSwimAnimator), distance
    /// culling and bounds like FaunaCreature. Registered in CreatureRegistry while active, so the hand camera can film
    /// it and the journal gets the host's real species, size and tier.
    public sealed class NetCreatureProxy : MonoBehaviour, ICreature
    {
        struct Snap
        {
            public Vector3 pos;
            public Quaternion rot;
            public float act, anim;
        }

        // ------------------------------------------------------------------ ICreature
        public SpeciesDef Species => species;
        public float SizeCm => sizeCm;
        public SizeTier Tier => tier;
        public Transform Transform => tr;
        public Vector3 Position => pos;
        public Bounds WorldBounds => bounds;
        public float Activity01 => activity;
        public bool IsHallucination => false;
        public bool IsAlive => alive && this != null && gameObject.activeInHierarchy;

        public int NetId => netId;
        /// Time.unscaledTime of the last spawn/snapshot that mentioned this animal.
        public float LastHeard => lastHeard;

        SpeciesDef species;
        float sizeCm;
        SizeTier tier;
        int netId;
        bool alive;
        float lastHeard;

        Transform tr;
        CreatureRig rig;
        FaunaSwimAnimator anim;
        Renderer[] renderers;
        bool[] rendererDefault;
        ShadowCastingMode[] shadowDefault;
        bool culled;
        Bounds bounds;
        int boundsCountdown;

        readonly SyncSnapBuffer<Snap> buffer = new SyncSnapBuffer<Snap>(6);
        Vector3 pos, lastPos;
        float lengthM, cruise, cullDistance, halfHeight;
        float activity = 0.2f, animValue, speed, yawRate, lastYaw, seed;
        bool hasLast;

        /// One-time setup on a freshly cloned (inactive) model.
        public void Setup(SpeciesDef s)
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
            seed = Random.Range(0f, 50f);
        }

        public void Activate(int id, float cm, SizeTier t, float ts, Vector3 p, Quaternion r, float act, float an)
        {
            netId = id;
            sizeCm = Mathf.Max(0.5f, cm);
            tier = t;
            lengthM = sizeCm / 100f;
            ComputeBodyFacts();
            buffer.Clear();
            Push(ts, p, r, act, an);
            pos = p;
            lastPos = p;
            hasLast = false;
            activity = act;
            animValue = an;
            speed = 0f;
            yawRate = 0f;
            culled = false;
            boundsCountdown = 0;
            tr.SetPositionAndRotation(p, r);
            tr.localScale = Vector3.one * lengthM;
            lastYaw = r.eulerAngles.y;

            bool bigEnough = lengthM >= 0.8f;
            for (int i = 0; i < renderers.Length; i++)
            {
                var rr = renderers[i];
                if (rr == null) continue;
                rr.enabled = rendererDefault[i];
                rr.shadowCastingMode = bigEnough ? shadowDefault[i] : ShadowCastingMode.Off;
            }
            if (anim != null) anim.SetLureEnabled(true);
            alive = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            CreatureRegistry.Register(this);
            UpdateBounds();
        }

        void ComputeBodyFacts()
        {
            var s = species;
            float h = Mathf.Max(0.04f, s.bodyHeight);
            halfHeight = h * lengthM * 0.5f;
            float minSpeed;
            switch (s.plan)
            {
                case BodyPlan.Jelly: minSpeed = 0.05f; break;
                case BodyPlan.Seahorse: minSpeed = 0.03f; break;
                case BodyPlan.VampireSquid: minSpeed = 0.08f; break;
                default: minSpeed = 0.14f; break;
            }
            cruise = Mathf.Clamp(s.speed * lengthM, minSpeed, 1.8f);
            float glowBoost = s.glows ? 1.6f : 1f;
            cullDistance = Mathf.Clamp(lengthM * 140f * glowBoost, 32f, 165f);
        }

        public void Deactivate()
        {
            alive = false;
            CreatureRegistry.Unregister(this);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        void OnDisable() { CreatureRegistry.Unregister(this); }

        void OnDestroy()
        {
            alive = false;
            CreatureRegistry.Unregister(this);
        }

        public void Push(float ts, Vector3 p, Quaternion r, float act, float an)
        {
            lastHeard = Time.unscaledTime;
            buffer.Add(ts, new Snap { pos = p, rot = r, act = act, anim = an });
        }

        /// renderTime = host clock time to display; camPos = local camera (culling).
        public void Tick(float dt, float renderTime, Vector3 camPos, float time)
        {
            if (!alive || dt <= 0f) return;
            if (!buffer.Bracket(renderTime, out int ia, out int ib, out float f, out float over)) return;
            ref Snap a = ref buffer.At(ia);
            ref Snap b = ref buffer.At(ib);
            Vector3 p = Vector3.LerpUnclamped(a.pos, b.pos, f);
            Quaternion r = Quaternion.Slerp(a.rot, b.rot, f);
            activity = Mathf.Lerp(a.act, b.act, f);
            animValue = Mathf.Lerp(a.anim, b.anim, f);
            if (over > 0f && buffer.Count >= 2)
            {
                // late snapshot: keep gliding with the last known velocity for a moment
                int n = buffer.Count - 1;
                float dtS = buffer.TimeAt(n) - buffer.TimeAt(n - 1);
                if (dtS > 1e-3f) p += (buffer.At(n).pos - buffer.At(n - 1).pos) * (Mathf.Min(over, 0.3f) / dtS);
            }
            pos = p;
            tr.SetPositionAndRotation(p, r);

            // speed / turn rate for the swim cycle
            if (hasLast)
            {
                float inst = (p - lastPos).magnitude / dt;
                speed = Mathf.Lerp(speed, inst, Mathf.Clamp01(dt * 6f));
                float yaw = r.eulerAngles.y;
                float rate = Mathf.DeltaAngle(lastYaw, yaw) / dt;
                yawRate = Mathf.Lerp(yawRate, rate, Mathf.Clamp01(dt * 6f));
                lastYaw = yaw;
            }
            lastPos = p;
            hasLast = true;

            float dCam = Vector3.Distance(p, camPos);
            // seen from above the water the sea hides them: cull beyond 35 m, deeper than 5 m beyond the scan range
            bool above = !FXVisibility.Underwater;
            float lim = above ? Mathf.Min(cullDistance, 35f) : cullDistance;
            bool shouldCull = culled ? dCam > lim - 5f : dCam > lim + 5f;
            if (above && p.y < Waves.HeightAt(p.x, p.z) - 5f && dCam > FilmShotEvaluator.MaxScanRange) shouldCull = true;
            if (shouldCull != culled) SetCulled(shouldCull, dCam);
            if (anim != null)
            {
                anim.SetLureEnabled(!culled && dCam < 50f);
                if (!culled && AnyRendererVisible())
                {
                    float bell = species.plan == BodyPlan.Jelly ? animValue : 0f;
                    float cloak = species.plan == BodyPlan.VampireSquid ? animValue : 0f;
                    float lure = species.plan == BodyPlan.Jelly || species.plan == BodyPlan.VampireSquid ? 1f : animValue * 3f;
                    anim.Animate(dt, speed / Mathf.Max(0.05f, cruise), lengthM, activity, yawRate, bell, cloak, lure, time + seed);
                }
            }

            if (--boundsCountdown <= 0)
            {
                boundsCountdown = 3;
                UpdateBounds();
            }
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

        void SetCulled(bool c, float dCam)
        {
            culled = c;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = !c && rendererDefault[i];
            if (anim != null) anim.SetLureEnabled(!c && dCam < 50f);
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

        /// Encodes the animation channel the proxy needs from a real creature (bell pulse / cloak / lure).
        public static float AnimChannel(FaunaCreature c)
        {
            var s = c.species;
            if (s == null) return 0f;
            if (s.plan == BodyPlan.Jelly) return c.bellPulse;
            if (s.plan == BodyPlan.VampireSquid) return c.cloak;
            return Mathf.Clamp01(c.lureMul / 3f);
        }
    }
}
