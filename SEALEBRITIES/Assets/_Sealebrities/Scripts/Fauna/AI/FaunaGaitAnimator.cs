using UnityEngine;

namespace Sealebrities
{
    /// Procedural limb animation of the round-2 body plans (CreatureRigExtra): walk cycles for crabs (scuttle), lizards
    /// (sprawled trot), quadrupeds and the capuchin (diagonal trot + body bob), birds and bats (flap, glide, fold the
    /// wings when perched, tuck the legs in flight), crab claws and fingers, eye stalks, head look-around, tail wag,
    /// ear flicks, the goblin shark's shooting jaw, the gulper's ballooning mouth and the hermit crab hiding in its shell.
    /// Driven only by values that also reach co-op proxies: speed / cruise, activity and the per-plan action channel
    /// (lureMul: action01 = (lureMul − 1) / 2; birds: 0 perched, 0.5 glide, 1 flap; crabs: claws up / hide in the shell;
    /// goblin / gulper: jaw out / mouth open; goat: grazing; capuchin: clapping).
    /// All axes are precomputed in each bone's parent space (works for any rest orientation). No allocations per frame.
    public sealed class FaunaGaitAnimator
    {
        CreatureGait gait;
        Transform root;
        float seed;

        // legs
        Transform[] legs, knees;
        Quaternion[] legRest, kneeRest;
        Vector3[] legSwingAxis, legLiftAxis, legYawAxis, kneeAxis;
        float[] legSide, legPhase;
        bool[] legFront;

        // wings
        Transform[] wings, tips;
        Quaternion[] wingRest, tipRest;
        Vector3[] wingRestScale;
        Vector3[] wingFlapAxis, wingYawAxis, tipFlapAxis;
        float[] wingSide;

        // claws
        Transform[] claws, fingers;
        Quaternion[] clawRest, fingerRest;
        Vector3[] clawAxis, fingerAxis;

        Transform[] stalks; Quaternion[] stalkRest; Vector3[] stalkAxis;
        Transform[] tail; Quaternion[] tailRest; Vector3[] tailYaw, tailPitch;
        Transform[] ears; Quaternion[] earRest; Vector3[] earAxis;

        Transform head; Quaternion headRest; Vector3 headYaw, headPitch; Vector3 headPosRest;
        Transform body; Vector3 bodyPosRest; Vector3 bodyRestScale; Vector3 bodyUp, bodyBack;
        Transform jaw; Quaternion jawRest; Vector3 jawPosRest, jawFwd, jawOpenAxis, jawScaleRest;

        float walkPhase, flapPhase, idleT;
        float foldBlend = 1f, flapAmp, gaitAmp;
        float wagTimer, wagT, earTimer, earT, earIdx;

        public bool Valid => gait != CreatureGait.None;

        public void Setup(CreatureRigExtra ex, SpeciesDef s, Transform rootT)
        {
            root = rootT;
            gait = ex != null ? ex.gait : CreatureGait.None;
            IsGulper = s != null && s.variant == "gulper";
            if (ex == null) return;
            seed = Random.Range(0f, 100f);
            walkPhase = Random.Range(0f, Mathf.PI * 2f);
            flapPhase = Random.Range(0f, Mathf.PI * 2f);

            // ---- legs
            int n = ex.legs != null ? ex.legs.Length : 0;
            legs = new Transform[n]; knees = new Transform[n];
            legRest = new Quaternion[n]; kneeRest = new Quaternion[n];
            legSwingAxis = new Vector3[n]; legLiftAxis = new Vector3[n]; legYawAxis = new Vector3[n]; kneeAxis = new Vector3[n];
            legSide = new float[n]; legPhase = new float[n]; legFront = new bool[n];
            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (ex.legs[i] == null) continue;
                float z = root.InverseTransformPoint(ex.legs[i].position).z;
                zMin = Mathf.Min(zMin, z); zMax = Mathf.Max(zMax, z);
            }
            float zMid = (zMin + zMax) * 0.5f;
            int[] sideCount = new int[2];
            for (int i = 0; i < n; i++)
            {
                var h = ex.legs[i];
                legs[i] = h;
                knees[i] = ex.knees != null && i < ex.knees.Length ? ex.knees[i] : null;
                if (h == null) continue;
                Vector3 lp = root.InverseTransformPoint(h.position);
                legSide[i] = lp.x >= 0f ? 1f : -1f;
                legFront[i] = lp.z >= zMid;
                legRest[i] = h.localRotation;
                legSwingAxis[i] = ParentDir(h, Vector3.right);
                legLiftAxis[i] = ParentDir(h, Vector3.forward);
                legYawAxis[i] = ParentDir(h, Vector3.up);
                if (knees[i] != null)
                {
                    kneeRest[i] = knees[i].localRotation;
                    kneeAxis[i] = gait == CreatureGait.Crab ? ParentDir(knees[i], Vector3.forward) : ParentDir(knees[i], Vector3.right);
                }
                int sideIdx = legSide[i] > 0f ? 0 : 1;
                int order = sideCount[sideIdx]++;
                switch (gait)
                {
                    case CreatureGait.Crab:
                        // alternating tetrapod: neighbours on one side and the opposite leg move in anti-phase
                        legPhase[i] = ((order + sideIdx) & 1) == 0 ? 0f : Mathf.PI;
                        break;
                    case CreatureGait.Bird:
                    case CreatureGait.Bat:
                        legPhase[i] = sideIdx == 0 ? 0f : Mathf.PI;
                        break;
                    default:
                        // diagonal trot: right-front with left-hind
                        legPhase[i] = (legFront[i] == (sideIdx == 0)) ? 0f : Mathf.PI;
                        break;
                }
            }

            // ---- wings
            int w = ex.wings != null ? ex.wings.Length : 0;
            wings = new Transform[w]; tips = new Transform[w];
            wingRest = new Quaternion[w]; tipRest = new Quaternion[w]; wingRestScale = new Vector3[w];
            wingFlapAxis = new Vector3[w]; wingYawAxis = new Vector3[w]; tipFlapAxis = new Vector3[w]; wingSide = new float[w];
            for (int i = 0; i < w; i++)
            {
                wings[i] = ex.wings[i];
                tips[i] = ex.wingTips != null && i < ex.wingTips.Length ? ex.wingTips[i] : null;
                if (wings[i] == null) continue;
                wingRest[i] = wings[i].localRotation;
                wingRestScale[i] = wings[i].localScale;
                wingSide[i] = root.InverseTransformPoint(wings[i].position).x >= 0f ? 1f : -1f;
                wingFlapAxis[i] = ParentDir(wings[i], Vector3.forward);
                wingYawAxis[i] = ParentDir(wings[i], Vector3.up);
                if (tips[i] != null)
                {
                    tipRest[i] = tips[i].localRotation;
                    tipFlapAxis[i] = ParentDir(tips[i], Vector3.forward);
                }
            }

            // ---- claws
            int c = ex.claws != null ? ex.claws.Length : 0;
            claws = new Transform[c]; fingers = new Transform[c];
            clawRest = new Quaternion[c]; fingerRest = new Quaternion[c]; clawAxis = new Vector3[c]; fingerAxis = new Vector3[c];
            for (int i = 0; i < c; i++)
            {
                claws[i] = ex.claws[i];
                fingers[i] = ex.fingers != null && i < ex.fingers.Length ? ex.fingers[i] : null;
                if (claws[i] != null) { clawRest[i] = claws[i].localRotation; clawAxis[i] = ParentDir(claws[i], Vector3.right); }
                if (fingers[i] != null) { fingerRest[i] = fingers[i].localRotation; fingerAxis[i] = ParentDir(fingers[i], Vector3.right); }
            }

            stalks = Copy(ex.eyeStalks, out stalkRest, out stalkAxis, Vector3.right);
            tail = Copy(ex.tail, out tailRest, out tailYaw, Vector3.up);
            tailPitch = new Vector3[tail.Length];
            for (int i = 0; i < tail.Length; i++) if (tail[i] != null) tailPitch[i] = ParentDir(tail[i], Vector3.right);
            ears = Copy(ex.ears, out earRest, out earAxis, Vector3.forward);

            head = ex.head;
            if (head != null)
            {
                headRest = head.localRotation;
                headPosRest = head.localPosition;
                headYaw = ParentDir(head, Vector3.up);
                headPitch = ParentDir(head, Vector3.right);
            }
            body = ex.body;
            if (body != null)
            {
                bodyPosRest = body.localPosition;
                bodyRestScale = body.localScale;
                Transform p = body.parent;
                bodyUp = p != null ? p.InverseTransformDirection(root.up) : Vector3.up;
                bodyBack = p != null ? p.InverseTransformDirection(-root.forward) : Vector3.back;
            }
            jaw = ex.jaw;
            if (jaw != null)
            {
                jawRest = jaw.localRotation;
                jawPosRest = jaw.localPosition;
                jawScaleRest = jaw.localScale;
                Transform p = jaw.parent;
                jawFwd = p != null ? p.InverseTransformDirection(root.forward) : Vector3.forward;
                jawOpenAxis = ParentDir(jaw, Vector3.right);
            }
        }

        Transform[] Copy(Transform[] src, out Quaternion[] rest, out Vector3[] axis, Vector3 rigAxis)
        {
            int n = src != null ? src.Length : 0;
            var r = new Transform[n];
            rest = new Quaternion[n];
            axis = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                r[i] = src[i];
                if (r[i] == null) continue;
                rest[i] = r[i].localRotation;
                axis[i] = ParentDir(r[i], rigAxis);
            }
            return r;
        }

        Vector3 ParentDir(Transform bone, Vector3 rigDir)
        {
            Vector3 w = root.TransformDirection(rigDir);
            Transform p = bone.parent;
            Vector3 d = p != null ? p.InverseTransformDirection(w) : w;
            return d.sqrMagnitude > 1e-10f ? d.normalized : rigDir;
        }

        /// speed01 = speed / cruise; action01 = plan-specific action channel 0..1.
        public void Animate(float dt, float speed01, float activity, float action01, float time)
        {
            if (gait == CreatureGait.None || dt <= 0f) return;
            speed01 = Mathf.Clamp(speed01, 0f, 3f);
            action01 = Mathf.Clamp01(action01);
            idleT += dt;
            bool flyer = gait == CreatureGait.Bird || gait == CreatureGait.Bat;

            // ---- walk cycle
            float baseFreq;
            float swingAmp, liftAmp;
            switch (gait)
            {
                case CreatureGait.Crab: baseFreq = 3.2f; swingAmp = 16f; liftAmp = 20f; break;
                case CreatureGait.Lizard: baseFreq = 2.6f; swingAmp = 30f; liftAmp = 12f; break;
                case CreatureGait.Quadruped: baseFreq = 1.7f; swingAmp = 24f; liftAmp = 0f; break;
                case CreatureGait.Primate: baseFreq = 2f; swingAmp = 26f; liftAmp = 0f; break;
                case CreatureGait.Bird: baseFreq = 2.8f; swingAmp = 26f; liftAmp = 0f; break;
                default: baseFreq = 2f; swingAmp = 18f; liftAmp = 0f; break;
            }
            float walking = flyer ? (1f - Mathf.Clamp01(action01 * 3f)) : 1f;   // flyers walk only when perched
            float target = Mathf.Clamp01(speed01 * 1.2f) * walking;
            gaitAmp = Mathf.MoveTowards(gaitAmp, target, dt * 4f);
            walkPhase += dt * baseFreq * Mathf.Clamp(speed01, 0.35f, 2.2f) * Mathf.PI * 2f * (gaitAmp > 0.02f ? 1f : 0.2f);
            if (walkPhase > 1000f) walkPhase -= Mathf.PI * 300f;

            float hide = gait == CreatureGait.Crab && body != null && head != null ? action01 : 0f;   // hermit crab: action = hide in the shell
            for (int i = 0; i < legs.Length; i++)
            {
                var h = legs[i];
                if (h == null) continue;
                float ph = walkPhase + legPhase[i];
                float sw = Mathf.Sin(ph);
                float up = Mathf.Max(0f, Mathf.Cos(ph));   // lift during the forward swing
                Quaternion q;
                if (gait == CreatureGait.Crab)
                {
                    // scuttle: legs sweep around the vertical axis and lift their tips
                    float yawA = swingAmp * gaitAmp * sw;
                    float lift = liftAmp * gaitAmp * up + 6f * Mathf.Sin(idleT * 0.7f + i);   // idle twitch
                    lift += hide * 55f;   // fold up when hiding
                    q = Quaternion.AngleAxis(yawA, legYawAxis[i]) * Quaternion.AngleAxis(legSide[i] * lift, legLiftAxis[i]);
                }
                else if (flyer)
                {
                    // perched / walking: steps; flying: legs tucked back
                    float tuck = (1f - walking) * 75f;
                    q = Quaternion.AngleAxis(swingAmp * gaitAmp * sw + tuck, legSwingAxis[i]);
                }
                else
                {
                    float a = swingAmp * gaitAmp * sw;
                    q = Quaternion.AngleAxis(a, legSwingAxis[i]);
                    if (gait == CreatureGait.Lizard) q = Quaternion.AngleAxis(legSide[i] * liftAmp * gaitAmp * up, legLiftAxis[i]) * q;
                }
                h.localRotation = q * legRest[i];
                var k = knees[i];
                if (k != null)
                {
                    float bend;
                    if (gait == CreatureGait.Crab) bend = -legSide[i] * (gaitAmp * up * 18f + hide * 40f);
                    else if (gait == CreatureGait.Lizard) bend = 0f;
                    else bend = gaitAmp * up * 38f;   // the foot folds back while the leg swings forward
                    k.localRotation = Quaternion.AngleAxis(bend, kneeAxis[i]) * kneeRest[i];
                }
            }

            // ---- body bob (and the hermit crab retreating into its shell)
            if (body != null)
            {
                float bob = 0f;
                if (gait == CreatureGait.Quadruped || gait == CreatureGait.Primate) bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.025f * gaitAmp;
                else if (gait == CreatureGait.Crab) bob = Mathf.Sin(walkPhase * 2f) * 0.012f * gaitAmp;
                else if (flyer) bob = Mathf.Abs(Mathf.Sin(walkPhase)) * 0.02f * gaitAmp;
                body.localPosition = bodyPosRest + bodyUp * bob + bodyBack * (hide * 0.16f) - bodyUp * (hide * 0.05f);
                body.localScale = hide > 0.001f ? bodyRestScale * Mathf.Lerp(1f, 0.55f, hide) : bodyRestScale;
            }

            // ---- wings
            if (wings.Length > 0)
            {
                // action 0 = folded (perched), 0.5 = glide, 1 = flap
                float fold = 1f - Mathf.Clamp01(action01 * 3f);
                foldBlend = Mathf.MoveTowards(foldBlend, fold, dt * 3f);
                float flapT = Mathf.Clamp01((action01 - 0.5f) * 2f);
                flapAmp = Mathf.MoveTowards(flapAmp, flapT, dt * 2.5f);
                float freq = gait == CreatureGait.Bat ? 4.2f : 3f;
                flapPhase += dt * Mathf.PI * 2f * Mathf.Lerp(0.6f, freq, flapAmp);
                if (flapPhase > 1000f) flapPhase -= Mathf.PI * 300f;
                float amp = Mathf.Lerp(6f, gait == CreatureGait.Bat ? 52f : 42f, flapAmp) * (1f - foldBlend);
                float dihedral = Mathf.Lerp(6f, 2f, flapAmp) * (1f - foldBlend);
                for (int i = 0; i < wings.Length; i++)
                {
                    var wg = wings[i];
                    if (wg == null) continue;
                    float s = wingSide[i];
                    float flap = s * (amp * Mathf.Sin(flapPhase) + dihedral);
                    // folded: shrink the span (the tip follows), swing the wing back, then roll it onto the flank
                    float yaw = s * 80f * foldBlend;
                    float roll = s * 62f * foldBlend;
                    wg.localRotation = Quaternion.AngleAxis(roll, wingFlapAxis[i]) * Quaternion.AngleAxis(yaw, wingYawAxis[i]) * Quaternion.AngleAxis(flap, wingFlapAxis[i]) * wingRest[i];
                    Vector3 sc = wingRestScale[i];
                    wg.localScale = foldBlend > 0.001f ? new Vector3(sc.x * Mathf.Lerp(1f, 0.34f, foldBlend), sc.y, sc.z * Mathf.Lerp(1f, 0.8f, foldBlend)) : sc;
                    var tp = tips[i];
                    if (tp != null)
                    {
                        float lag = s * amp * 0.55f * Mathf.Sin(flapPhase - 0.7f);
                        tp.localRotation = Quaternion.AngleAxis(lag, tipFlapAxis[i]) * tipRest[i];
                    }
                }
            }

            // ---- claws: slow idle sway, raised and snapping when the action channel is up (threat display)
            for (int i = 0; i < claws.Length; i++)
            {
                if (claws[i] != null)
                {
                    float raise = -(6f * Mathf.Sin(idleT * 0.9f + i * 1.7f + seed) + 30f * action01 * (1f - hide)) + hide * 30f;
                    claws[i].localRotation = Quaternion.AngleAxis(raise, clawAxis[i]) * clawRest[i];
                }
                if (fingers[i] != null)
                {
                    float open = 8f + 8f * Mathf.Sin(idleT * 1.3f + i * 2.1f + seed);
                    if (action01 > 0.2f) open = 25f * Mathf.Abs(Mathf.Sin(idleT * 9f + i));   // snap snap
                    fingers[i].localRotation = Quaternion.AngleAxis(-open, fingerAxis[i]) * fingerRest[i];
                }
            }
            for (int i = 0; i < stalks.Length; i++)
                if (stalks[i] != null)
                    stalks[i].localRotation = Quaternion.AngleAxis(8f * Mathf.Sin(idleT * 1.1f + i * 0.8f + seed) - hide * 60f, stalkAxis[i]) * stalkRest[i];

            // ---- head: look-around, grazing (goat action), walking bob (birds), clapping capuchin nods
            if (head != null && gait != CreatureGait.Crab)
            {
                float yaw = (Mathf.PerlinNoise(idleT * 0.25f, seed) - 0.5f) * 60f * (1f - gaitAmp * 0.6f);
                float pitch = (Mathf.PerlinNoise(seed, idleT * 0.2f) - 0.5f) * 20f;
                if (gait == CreatureGait.Quadruped) pitch += action01 * 45f;                       // grazing
                if (gait == CreatureGait.Bird) pitch += Mathf.Sin(walkPhase * 2f) * 8f * gaitAmp;  // pigeon-ish bob
                if (gait == CreatureGait.Primate && action01 > 0.2f) pitch += Mathf.Sin(idleT * 8f) * 10f;
                if (gait == CreatureGait.Marine) { yaw *= 0.15f; pitch *= 0.2f; }
                head.localRotation = Quaternion.AngleAxis(yaw, headYaw) * Quaternion.AngleAxis(pitch, headPitch) * headRest;
            }
            else if (head != null && gait == CreatureGait.Crab)
            {
                // hermit crab shell wobbles with the steps
                head.localRotation = Quaternion.AngleAxis(Mathf.Sin(walkPhase) * 4f * gaitAmp, headYaw) * headRest;
            }

            // ---- tail: wag bursts (goat), sway (capuchin), feathers spread (birds)
            if (tail.Length > 0)
            {
                wagTimer -= dt;
                if (wagTimer <= 0f) { wagTimer = Random.Range(3f, 9f); wagT = 1.2f; }
                if (wagT > 0f) wagT -= dt;
                float wag = wagT > 0f ? Mathf.Sin(idleT * 18f) * 22f * Mathf.Clamp01(wagT) : 0f;
                for (int i = 0; i < tail.Length; i++)
                {
                    if (tail[i] == null) continue;
                    float sway = 0f, lift = 0f;
                    if (gait == CreatureGait.Primate) sway = 14f * Mathf.Sin(idleT * 0.8f - i * 0.6f + seed);
                    else if (gait == CreatureGait.Quadruped) sway = wag;
                    else if (flyer) lift = -action01 * 10f + Mathf.Sin(idleT * 2f) * 3f;
                    tail[i].localRotation = Quaternion.AngleAxis(sway, tailYaw[i]) * Quaternion.AngleAxis(lift, tailPitch[i]) * tailRest[i];
                }
            }
            if (ears.Length > 0)
            {
                earTimer -= dt;
                if (earTimer <= 0f) { earTimer = Random.Range(2f, 6f); earT = 0.35f; earIdx = Random.value < 0.5f ? 0 : 1; }
                if (earT > 0f) earT -= dt;
                for (int i = 0; i < ears.Length; i++)
                {
                    if (ears[i] == null) continue;
                    float flick = earT > 0f && (int)earIdx == i ? Mathf.Sin(earT / 0.35f * Mathf.PI) * 25f : 0f;
                    ears[i].localRotation = Quaternion.AngleAxis(flick, earAxis[i]) * earRest[i];
                }
            }

            // ---- jaw: goblin shark shoots it forward, the gulper balloons its mouth pouch
            if (jaw != null)
            {
                if (IsGulper)
                {
                    float m = 1f + 2f * action01 + 0.05f * Mathf.Sin(idleT * 1.5f);
                    jaw.localScale = jawScaleRest * m;
                    jaw.localRotation = Quaternion.AngleAxis(10f * action01, jawOpenAxis) * jawRest;
                }
                else
                {
                    jaw.localPosition = jawPosRest + jawFwd * (0.09f * action01);
                    jaw.localRotation = Quaternion.AngleAxis(28f * action01 + 3f * Mathf.Sin(idleT * 0.8f), jawOpenAxis) * jawRest;
                }
            }
        }

        /// Gulper eels balloon their pouch (set once from the species in Setup through the caller).
        public bool IsGulper;


    }
}
