using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Procedural swim animation driven by the CreatureRig bones: traveling sine wave along the spine
    /// (yaw for fish/sharks/eels, pitch for sea lions/rays/seahorses), tail and pectoral fin flaps, ray/manta
    /// wing beats, turtle/sea-lion flipper strokes, tentacle sway with phase offsets, jelly bell pulse,
    /// vampire-squid cloak flare and anglerfish lure flicker.
    /// All swing axes are precomputed in each bone's parent space, so it works for any bone rest orientation.
    public sealed class FaunaSwimAnimator
    {
        Transform root;
        BodyPlan plan;
        string variant;
        float seed;

        // spine
        Transform[] spine;
        Quaternion[] spineRest;
        Vector3[] spineAxis;
        bool spineNested;
        bool spinePitch;

        // tail
        Transform tail;
        Quaternion tailRest;
        Vector3 tailAxis;

        // pectoral fins
        Transform[] pecs;
        Quaternion[] pecRest;
        Vector3[] pecFlapAxis, pecTuckAxis;
        float[] pecSide;

        // flippers / wings
        Transform[] flips;
        Quaternion[] flipRest;
        Vector3[] flipFlapAxis, flipFeatherAxis;
        float[] flipSide, flipLag, flipAmpScale;
        bool[] flipFront, flipVertical;

        // tentacle bones (all chains flattened)
        Transform[] tents;
        Quaternion[] tentRest;
        Vector3[] tentAxisA, tentAxisB;
        float[] tentPhase, tentWeight;
        bool[] tentRoot;

        // bell
        Transform bell;
        Vector3 bellRest;

        // lure
        Light lure;
        float lureBase;

        float phase, finPhase, flipPhase, swayPhase;
        float prevSpineAngle;

        const float TwoPi = Mathf.PI * 2f;

        public Light Lure => lure;

        public void Setup(CreatureRig rig, SpeciesDef s)
        {
            root = rig.transform;
            plan = s.plan;
            variant = s.variant ?? "";
            seed = Random.Range(0f, 100f);
            phase = Random.Range(0f, TwoPi);
            finPhase = Random.Range(0f, TwoPi);
            flipPhase = Random.Range(0f, TwoPi);
            swayPhase = Random.Range(0f, TwoPi);

            // ---- spine
            var sp = new List<Transform>();
            if (rig.spine != null)
                for (int i = 0; i < rig.spine.Length; i++)
                    if (rig.spine[i] != null && rig.spine[i] != root) sp.Add(rig.spine[i]);
            spine = sp.ToArray();
            spinePitch = plan == BodyPlan.SeaLion || plan == BodyPlan.Ray || plan == BodyPlan.Seahorse || plan == BodyPlan.Dolphin || plan == BodyPlan.Whale;
            Vector3 spineRigAxis = spinePitch ? Vector3.right : Vector3.up;
            spineRest = new Quaternion[spine.Length];
            spineAxis = new Vector3[spine.Length];
            spineNested = spine.Length > 1;
            for (int i = 0; i < spine.Length; i++)
            {
                spineRest[i] = spine[i].localRotation;
                // rays: the disc undulates (pitch) but the whip tail swings sideways (yaw)
                Vector3 ax = plan == BodyPlan.Ray && i > 0 ? Vector3.up : spineRigAxis;
                spineAxis[i] = ParentDir(spine[i], ax);
                if (i > 0 && !spine[i].IsChildOf(spine[i - 1])) spineNested = false;
            }

            // ---- tail fin
            tail = rig.tailFin != null && rig.tailFin != root ? rig.tailFin : null;
            if (tail != null)
            {
                tailRest = tail.localRotation;
                tailAxis = ParentDir(tail, spinePitch ? Vector3.right : Vector3.up);
            }

            // ---- pectoral fins
            var pl = new List<Transform>();
            if (rig.pectoralFins != null)
                for (int i = 0; i < rig.pectoralFins.Length; i++)
                    if (rig.pectoralFins[i] != null && rig.pectoralFins[i] != root) pl.Add(rig.pectoralFins[i]);
            pecs = pl.ToArray();
            pecRest = new Quaternion[pecs.Length];
            pecFlapAxis = new Vector3[pecs.Length];
            pecTuckAxis = new Vector3[pecs.Length];
            pecSide = new float[pecs.Length];
            for (int i = 0; i < pecs.Length; i++)
            {
                pecRest[i] = pecs[i].localRotation;
                pecFlapAxis[i] = ParentDir(pecs[i], Vector3.forward);
                pecTuckAxis[i] = ParentDir(pecs[i], Vector3.up);
                Vector3 lp = root.InverseTransformPoint(pecs[i].position);
                pecSide[i] = Mathf.Abs(lp.x) > 1e-4f ? Mathf.Sign(lp.x) : (i % 2 == 0 ? 1f : -1f);
            }

            // ---- flippers / wings
            var fl = new List<Transform>();
            if (rig.flippers != null)
                for (int i = 0; i < rig.flippers.Length; i++)
                    if (rig.flippers[i] != null && rig.flippers[i] != root) fl.Add(rig.flippers[i]);
            flips = fl.ToArray();
            flipRest = new Quaternion[flips.Length];
            flipFlapAxis = new Vector3[flips.Length];
            flipFeatherAxis = new Vector3[flips.Length];
            flipSide = new float[flips.Length];
            flipLag = new float[flips.Length];
            flipAmpScale = new float[flips.Length];
            flipFront = new bool[flips.Length];
            flipVertical = new bool[flips.Length];
            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < flips.Length; i++)
            {
                float z = root.InverseTransformPoint(flips[i].position).z;
                if (z < zMin) zMin = z;
                if (z > zMax) zMax = z;
            }
            float zSpan = zMax - zMin;
            float zMid = zSpan > 0.02f ? (zMin + zMax) * 0.5f : 0f;
            for (int i = 0; i < flips.Length; i++)
            {
                flipRest[i] = flips[i].localRotation;
                Vector3 lp = root.InverseTransformPoint(flips[i].position);
                bool vertical = plan == BodyPlan.Sunfish && Mathf.Abs(lp.y) > Mathf.Abs(lp.x);
                flipVertical[i] = vertical;
                flipFront[i] = lp.z >= zMid;
                // front-to-rear phase lag (undulating disc margin on rays), nested wing tips swing less on their own
                flipLag[i] = zSpan > 0.02f ? Mathf.Clamp01((zMax - lp.z) / zSpan) * 1.1f : 0f;
                bool nested = false;
                for (int j = 0; j < flips.Length; j++)
                    if (j != i && flips[i].parent == flips[j]) { nested = true; break; }
                flipAmpScale[i] = nested ? 0.6f : 1f;
                if (vertical)
                {
                    flipSide[i] = Mathf.Abs(lp.y) > 1e-4f ? Mathf.Sign(lp.y) : 1f;
                    flipFlapAxis[i] = ParentDir(flips[i], Vector3.up);
                    flipFeatherAxis[i] = ParentDir(flips[i], Vector3.forward);
                }
                else
                {
                    flipSide[i] = Mathf.Abs(lp.x) > 1e-4f ? Mathf.Sign(lp.x) : (i % 2 == 0 ? 1f : -1f);
                    flipFlapAxis[i] = ParentDir(flips[i], Vector3.forward);
                    flipFeatherAxis[i] = ParentDir(flips[i], Vector3.right);
                }
            }

            // ---- tentacles (each root may be a chain)
            var tb = new List<Transform>();
            var tr = new List<Quaternion>();
            var ta = new List<Vector3>();
            var tbx = new List<Vector3>();
            var tph = new List<float>();
            var tw = new List<float>();
            var troot = new List<bool>();
            var chain = new List<Transform>(8);
            if (rig.tentacles != null)
            {
                for (int j = 0; j < rig.tentacles.Length; j++)
                {
                    Transform t0 = rig.tentacles[j];
                    if (t0 == null || t0 == root) continue;
                    chain.Clear();
                    Transform t = t0;
                    while (t != null && chain.Count < 8)
                    {
                        chain.Add(t);
                        t = NextChainBone(t);
                    }

                    Vector3 rootLocal = root.InverseTransformPoint(t0.position);
                    Vector3 dir;
                    if (chain.Count > 1) dir = root.InverseTransformPoint(chain[chain.Count - 1].position) - rootLocal;
                    else dir = new Vector3(rootLocal.x, rootLocal.y - 0.5f * Mathf.Max(0.05f, rootLocal.magnitude + 0.1f), rootLocal.z);
                    if (dir.sqrMagnitude < 1e-8f) dir = Vector3.down;
                    dir.Normalize();
                    Vector3 radial = rootLocal - dir * Vector3.Dot(rootLocal, dir);
                    if (radial.sqrMagnitude < 1e-8f) radial = Vector3.Cross(dir, Vector3.up);
                    if (radial.sqrMagnitude < 1e-8f) radial = Vector3.Cross(dir, Vector3.right);
                    radial.Normalize();
                    Vector3 axA = Vector3.Cross(dir, radial).normalized;       // bends the tentacle outward (+) / inward (-)
                    Vector3 axB = Vector3.Cross(dir, axA).normalized;          // bends it sideways

                    for (int k = 0; k < chain.Count; k++)
                    {
                        Transform b = chain[k];
                        tb.Add(b);
                        tr.Add(b.localRotation);
                        ta.Add(ParentDir(b, axA));
                        tbx.Add(ParentDir(b, axB));
                        tph.Add(j * 0.93f + k * 0.65f);
                        tw.Add(chain.Count > 1 ? (k + 1f) / chain.Count : 1f);
                        troot.Add(k == 0);
                    }
                }
            }
            tents = tb.ToArray();
            tentRest = tr.ToArray();
            tentAxisA = ta.ToArray();
            tentAxisB = tbx.ToArray();
            tentPhase = tph.ToArray();
            tentWeight = tw.ToArray();
            tentRoot = troot.ToArray();

            // ---- bell
            bell = rig.bell != null && rig.bell != root ? rig.bell : null;
            if (bell != null) bellRest = bell.localScale;

            // ---- lure light
            lure = rig.lureLight;
            if (lure != null) lureBase = lure.intensity > 0f ? lure.intensity : 1.5f;

            // ---- round 2: legs, wings, claws, jaws (crabs, birds, land animals, goblin shark, gulper)
            var ex = rig.GetComponent<CreatureRigExtra>();
            if (ex != null)
            {
                gaitAnim = new FaunaGaitAnimator();
                gaitAnim.Setup(ex, s, root);
                if (!gaitAnim.Valid) gaitAnim = null;
            }
        }

        FaunaGaitAnimator gaitAnim;

        static Transform NextChainBone(Transform t)
        {
            int n = t.childCount;
            if (n == 0) return null;
            // prefer a pure bone (no renderer), then a child that continues the chain
            for (int i = 0; i < n; i++)
            {
                Transform c = t.GetChild(i);
                if (c.GetComponent<Renderer>() == null && c.GetComponent<Light>() == null) return c;
            }
            for (int i = 0; i < n; i++)
            {
                Transform c = t.GetChild(i);
                if (c.childCount > 0) return c;
            }
            return null;
        }

        Vector3 ParentDir(Transform bone, Vector3 rigDir)
        {
            Vector3 w = root.TransformDirection(rigDir);
            Transform p = bone.parent;
            Vector3 d = p != null ? p.InverseTransformDirection(w) : w;
            return d.sqrMagnitude > 1e-10f ? d.normalized : rigDir;
        }

        /// speed01: current speed / cruise speed. yawRate in degrees per second (positive = turning right).
        /// bellPulse 0..1 (jelly contraction), cloak 0..1 (vampire squid flare), lureMul (light multiplier, 0 = off).
        public void Animate(float dt, float speed01, float lengthM, float activity, float yawRate, float bellPulse, float cloak, float lureMul, float time)
        {
            speed01 = Mathf.Clamp(speed01, 0f, 3f);
            float sizeF = Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(0.05f, lengthM)), 0.35f, 2.6f);
            float move01 = Mathf.Clamp01(speed01 * 0.6f);
            float freq, amp, k, headW;
            bool eel = false;

            switch (plan)
            {
                case BodyPlan.Fish:
                    freq = Mathf.Lerp(0.9f, 3.2f, Mathf.Clamp01(speed01 * 0.5f)) * sizeF;
                    amp = Mathf.Lerp(5f, 18f, move01) + activity * 5f;
                    k = 2.4f; headW = 0.08f;
                    if (variant == "lionfish") amp *= 0.55f;
                    else if (variant == "grouper") { amp *= 0.8f; freq *= 0.8f; }
                    else if (variant == "barracuda") { amp *= 0.75f; k = 2.0f; }
                    break;
                case BodyPlan.Shark:
                    freq = Mathf.Lerp(0.55f, 1.8f, Mathf.Clamp01(speed01 * 0.5f)) * sizeF * 0.85f;
                    amp = Mathf.Lerp(6f, 16f, move01) + activity * 4f;
                    k = 2.0f; headW = 0.06f;
                    if (variant == "whale") { amp *= 0.8f; freq *= 0.8f; }
                    break;
                case BodyPlan.Eel:
                    freq = Mathf.Lerp(0.5f, 2.2f, Mathf.Clamp01(speed01 * 0.5f)) * sizeF * 0.75f;
                    amp = Mathf.Lerp(6f, 28f, move01) + activity * 6f;
                    k = 7.5f; headW = 0.3f; eel = true;
                    break;
                case BodyPlan.Anglerfish:
                    freq = Mathf.Lerp(0.6f, 2.4f, Mathf.Clamp01(speed01 * 0.5f)) * sizeF * 0.7f;
                    amp = Mathf.Lerp(4f, 12f, move01) + activity * 4f;
                    k = 2.4f; headW = 0.05f;
                    break;
                case BodyPlan.SeaLion:
                    freq = Mathf.Lerp(0.5f, 2.0f, Mathf.Clamp01(speed01 * 0.5f)) * sizeF * 0.9f;
                    amp = Mathf.Lerp(4f, 15f, move01) + activity * 4f;
                    k = 3.0f; headW = 0.15f;
                    break;
                case BodyPlan.Ray:
                    freq = Mathf.Lerp(0.3f, 0.9f, Mathf.Clamp01(speed01 * 0.5f)) * Mathf.Max(0.55f, sizeF * 0.8f);
                    amp = Mathf.Lerp(3f, 8f, move01);
                    k = 3.0f; headW = 0.05f;
                    break;
                case BodyPlan.Seahorse:
                    freq = 0.35f; amp = 3f + activity * 2f; k = 2f; headW = 0.3f;
                    break;
                case BodyPlan.Sunfish:
                    freq = 0.5f; amp = 2f; k = 1.5f; headW = 0.2f;
                    break;
                case BodyPlan.Turtle:
                    freq = 0.45f; amp = 3f; k = 1.5f; headW = 1f;
                    break;
                case BodyPlan.Dolphin:
                    freq = Mathf.Lerp(0.6f, 2.2f, Mathf.Clamp01(speed01 * 0.5f)) * sizeF * 0.9f;
                    amp = Mathf.Lerp(5f, 14f, move01) + activity * 4f;
                    k = 2.6f; headW = 0.06f;
                    break;
                case BodyPlan.Whale:
                    freq = Mathf.Lerp(0.18f, 0.6f, Mathf.Clamp01(speed01 * 0.5f));
                    amp = Mathf.Lerp(4f, 9f, move01) + activity * 2f;
                    k = 2.4f; headW = 0.04f;
                    break;
                case BodyPlan.Lizard:
                    // walking S-curve of the body and tail
                    freq = Mathf.Lerp(0.25f, 1.4f, Mathf.Clamp01(speed01 * 0.7f));
                    amp = Mathf.Lerp(3f, 15f, move01);
                    k = 2.2f; headW = 0.25f;
                    break;
                case BodyPlan.Crab: case BodyPlan.Starfish: case BodyPlan.Bird: case BodyPlan.Bat:
                case BodyPlan.Quadruped: case BodyPlan.Primate:
                    freq = 0.5f; amp = 0f; k = 1f; headW = 0f;
                    break;
                default:
                    freq = 0.6f; amp = 3f; k = 2f; headW = 0.3f;
                    break;
            }

            phase += dt * freq * TwoPi;
            if (phase > 1000f) phase -= TwoPi * 150f;

            // ---- spine wave
            int n = spine.Length;
            if (n > 0)
            {
                float turnBend = spinePitch ? 0f : Mathf.Clamp(yawRate * 0.06f, -14f, 14f);
                float headLook = 0f;
                if (eel) headLook = (Mathf.PerlinNoise(time * 0.45f, seed) - 0.5f) * 40f * (1f - move01);
                prevSpineAngle = 0f;
                for (int i = 0; i < n; i++)
                {
                    float t = n > 1 ? i / (float)(n - 1) : 1f;
                    float w = eel ? Mathf.Lerp(0.35f, 1f, t) : Mathf.Lerp(headW, 1f, t * t);
                    float a = amp * w * Mathf.Sin(phase - k * t) + turnBend * t;
                    if (plan == BodyPlan.Seahorse) a += 10f * t * t;   // curled tail
                    if (i == 0) a += headLook;
                    float local = spineNested ? a - prevSpineAngle : a;
                    prevSpineAngle = a;
                    spine[i].localRotation = Quaternion.AngleAxis(local, spineAxis[i]) * spineRest[i];
                }
            }

            // ---- tail fin
            if (tail != null)
            {
                float ta = amp * 0.9f * Mathf.Sin(phase - k - 0.5f);
                if (plan == BodyPlan.Ray || plan == BodyPlan.Turtle) ta *= 0.3f;
                tail.localRotation = Quaternion.AngleAxis(ta, tailAxis) * tailRest;
            }

            // ---- pectoral fins
            if (pecs.Length > 0)
            {
                float hover01 = 1f - Mathf.Clamp01(speed01);
                float fFreq = (1.6f + 1.5f * hover01) * Mathf.Max(0.5f, sizeF * 0.7f);
                float fAmp = 8f + 22f * hover01 + 10f * activity;
                if (plan == BodyPlan.Cuttlefish || plan == BodyPlan.Squid) { fFreq = (3.5f + 2f * activity) * Mathf.Clamp(sizeF, 0.3f, 1.5f); fAmp = 22f; }
                else if (plan == BodyPlan.Shark) { fFreq *= 0.4f; fAmp *= 0.25f; }
                else if (plan == BodyPlan.Dolphin || plan == BodyPlan.Whale) { fFreq *= 0.3f; fAmp *= 0.35f; }
                else if (plan == BodyPlan.Seahorse) { fFreq = 7f; fAmp = 25f; }
                finPhase += dt * fFreq * TwoPi;
                if (finPhase > 1000f) finPhase -= TwoPi * 150f;
                float tuck = -25f * Mathf.Clamp01(speed01 - 1f);
                for (int i = 0; i < pecs.Length; i++)
                {
                    float s = pecSide[i];
                    float a = s * fAmp * Mathf.Sin(finPhase + s * 0.4f + i * 0.3f);
                    pecs[i].localRotation = Quaternion.AngleAxis(a, pecFlapAxis[i]) * Quaternion.AngleAxis(s * tuck, pecTuckAxis[i]) * pecRest[i];
                }
            }

            // ---- flippers / wings
            if (flips.Length > 0)
            {
                float fFreq, fAmp;
                switch (plan)
                {
                    case BodyPlan.Ray:
                        fFreq = Mathf.Lerp(0.3f, 0.9f, Mathf.Clamp01(speed01 * 0.5f)) * Mathf.Max(0.55f, sizeF * 0.8f);
                        fAmp = (variant == "manta" ? 26f : 16f) * (0.35f + 0.65f * Mathf.Clamp01(speed01)) + activity * 6f;
                        break;
                    case BodyPlan.Turtle:
                        fFreq = Mathf.Lerp(0.22f, 0.7f, Mathf.Clamp01(speed01 * 0.6f));
                        fAmp = 32f * (0.35f + 0.65f * Mathf.Clamp01(speed01)) + activity * 6f;
                        break;
                    case BodyPlan.SeaLion:
                        fFreq = Mathf.Lerp(0.35f, 1.4f, Mathf.Clamp01(speed01 * 0.5f));
                        fAmp = 26f * (0.4f + 0.6f * Mathf.Clamp01(speed01)) + activity * 8f;
                        break;
                    case BodyPlan.Sunfish:
                        fFreq = 0.45f + 0.4f * Mathf.Clamp01(speed01);
                        fAmp = 32f;
                        break;
                    default:
                        fFreq = 0.8f + speed01;
                        fAmp = 14f;
                        break;
                }
                flipPhase += dt * fFreq * TwoPi;
                if (flipPhase > 1000f) flipPhase -= TwoPi * 150f;
                for (int i = 0; i < flips.Length; i++)
                {
                    float s = flipSide[i];
                    Quaternion q;
                    if (flipVertical[i])
                    {
                        q = Quaternion.AngleAxis(s * fAmp * Mathf.Sin(flipPhase), flipFlapAxis[i]);
                    }
                    else if (plan == BodyPlan.Turtle || plan == BodyPlan.SeaLion)
                    {
                        if (flipFront[i])
                        {
                            float stroke = s * fAmp * Mathf.Sin(flipPhase);
                            float feather = 22f * Mathf.Cos(flipPhase);
                            q = Quaternion.AngleAxis(stroke, flipFlapAxis[i]) * Quaternion.AngleAxis(feather, flipFeatherAxis[i]);
                        }
                        else if (plan == BodyPlan.SeaLion)
                        {
                            q = Quaternion.AngleAxis(amp * 1.2f * Mathf.Sin(phase - k), flipFeatherAxis[i]);
                        }
                        else
                        {
                            q = Quaternion.AngleAxis(s * fAmp * 0.3f * Mathf.Sin(flipPhase + 1.57f), flipFlapAxis[i]);
                        }
                    }
                    else
                    {
                        // ray/manta wings: traveling flap from front to back gives the "flying" look
                        q = Quaternion.AngleAxis(s * fAmp * flipAmpScale[i] * Mathf.Sin(flipPhase - flipLag[i]), flipFlapAxis[i]);
                    }
                    flips[i].localRotation = q * flipRest[i];
                }
            }

            // ---- tentacles
            if (tents.Length > 0)
            {
                float tFreq, tAmp;
                switch (plan)
                {
                    case BodyPlan.Jelly: tFreq = 0.45f; tAmp = 7f + 10f * Mathf.Clamp01(speed01); break;
                    case BodyPlan.Octopus: tFreq = 0.3f + 0.4f * activity; tAmp = 13f + 8f * Mathf.Clamp01(speed01); break;
                    case BodyPlan.Squid: tFreq = 0.7f; tAmp = 6f; break;
                    case BodyPlan.Cuttlefish: tFreq = 0.6f; tAmp = 6f; break;
                    case BodyPlan.VampireSquid: tFreq = 0.4f; tAmp = 8f; break;
                    case BodyPlan.Starfish: tFreq = 0.07f + 0.25f * Mathf.Clamp01(speed01); tAmp = 4f + 6f * Mathf.Clamp01(speed01); break;
                    default: tFreq = 0.6f; tAmp = 8f; break;
                }
                swayPhase += dt * tFreq * TwoPi;
                if (swayPhase > 1000f) swayPhase -= TwoPi * 150f;
                for (int i = 0; i < tents.Length; i++)
                {
                    float w = 0.35f + 0.65f * tentWeight[i];
                    float a1 = tAmp * w * Mathf.Sin(swayPhase + tentPhase[i]);
                    float a2 = tAmp * 0.6f * w * Mathf.Cos(swayPhase * 0.8f + tentPhase[i] * 1.3f);
                    if (cloak > 0f) a1 += cloak * (tentRoot[i] ? 70f : 12f);
                    if (bellPulse > 0f && plan == BodyPlan.Jelly) a1 += bellPulse * 10f * tentWeight[i];
                    tents[i].localRotation = Quaternion.AngleAxis(a1, tentAxisA[i]) * Quaternion.AngleAxis(a2, tentAxisB[i]) * tentRest[i];
                }
            }

            // ---- bell
            if (bell != null)
            {
                if (plan == BodyPlan.Jelly)
                {
                    float c = Mathf.Clamp01(bellPulse);
                    bell.localScale = new Vector3(bellRest.x * (1f - 0.17f * c), bellRest.y * (1f + 0.1f * c), bellRest.z * (1f - 0.17f * c));
                }
                else if (plan == BodyPlan.VampireSquid)
                {
                    // the arms do the flare; the mantle only puffs slightly
                    float c = Mathf.Clamp01(cloak);
                    float b = 1f + 0.03f * Mathf.Sin(time * 1.1f + seed);
                    bell.localScale = new Vector3(bellRest.x * (b + 0.06f * c), bellRest.y * (b - 0.04f * c), bellRest.z * b);
                }
                else
                {
                    float b = 1f + 0.035f * Mathf.Sin(time * 1.3f + seed);
                    bell.localScale = bellRest * b;
                }
            }

            // ---- lure light flicker
            if (lure != null)
            {
                float flicker = 0.78f + 0.44f * (Mathf.PerlinNoise(time * 2.3f, seed) - 0.5f);
                if (Mathf.PerlinNoise(time * 0.37f, seed + 3.1f) > 0.74f) flicker *= 0.15f;   // brief blackout
                lure.intensity = lureBase * flicker * (1f + activity * 0.6f) * lureMul;
            }

            // ---- limbs: the action channel rides on lureMul (1 = rest … 3 = full action), see FaunaGaitAnimator
            if (gaitAnim != null) gaitAnim.Animate(dt, speed01, activity, Mathf.Clamp01((lureMul - 1f) * 0.5f), time);
        }

        public void SetLureEnabled(bool on)
        {
            if (lure != null && lure.enabled != on) lure.enabled = on;
        }
    }
}
