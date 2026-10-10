using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Transforms of one procedural first-person arm (built for the left side, mirrored for the right).
    public class PlayerHandRig
    {
        public Transform arm, geo, hand, thumb1, thumb2, torch;
        public readonly Transform[] knuckle = new Transform[4];
        public readonly Transform[] middle = new Transform[4];
        public Renderer lens;
        public Vector3 pos;
        public Quaternion rot = Quaternion.identity;
        public bool poseValid;
    }

    /// Procedural first-person hands: dark neoprene gloves and sleeves with a skin-colored wrist gap.
    /// Idle sway, look lag, walking swing and breaststroke swimming. The left hand carries the dive lamp
    /// (Goodman-style strap on the back of the hand) once a lamp is owned. The right hand rests out of view so the
    /// filming module's camera model has the lower right area; it only joins the strokes while not aiming/recording.
    [DefaultExecutionOrder(60)]
    public class PlayerHands : MonoBehaviour
    {
        PlayerController pc;
        PlayerHandRig left, right;
        Mesh mSphere, mCapsule, mTaper, mCylinder, mFlare;
        Texture2D neopreneTex;
        Material neoprene, accent, skin, strap, tape, lensOff, lensOn, torchYellow, torchBlack, torchOrange;
        int torchTier = -1;
        bool lensLit, built;
        float time, strokePhase, strokeW, rightW;
        Vector2 sway;

        // breaststroke key poses for the LEFT hand in camera space (right hand = mirrored)
        static readonly Vector3 K0p = new Vector3(-0.085f, -0.19f, 0.25f);   // tucked in front of the chest
        static readonly Vector3 K1p = new Vector3(-0.05f, -0.12f, 0.50f);    // reach forward, palms down
        static readonly Vector3 K2p = new Vector3(-0.32f, -0.17f, 0.37f);    // sweep outward and back
        Quaternion k0q, k1q, k2q;

        public void Init(PlayerController controller)
        {
            pc = controller;
            if (!built) Build();
        }

        public void SetVisible(bool v)
        {
            if (gameObject.activeSelf != v) gameObject.SetActive(v);
            if (v && built)
            {
                left.poseValid = false;
                right.poseValid = false;
            }
        }

        // ------------------------------------------------------------------ build
        void Build()
        {
            built = true;
            k0q = Quaternion.Euler(-18f, 22f, 40f);
            k1q = Quaternion.Euler(-6f, 4f, -12f);
            k2q = Quaternion.Euler(-2f, -26f, -62f);

            mSphere = PlayerMeshes.Sphere();
            mCapsule = PlayerMeshes.Capsule();
            mTaper = PlayerMeshes.Taper();
            mCylinder = PlayerMeshes.Cylinder();
            mFlare = PlayerMeshes.Flare();
            neopreneTex = PlayerMeshes.NeopreneTexture(64);

            neoprene = MaterialLibrary.Textured(neopreneTex, new Color(0.13f, 0.14f, 0.16f), 0.32f);
            neoprene.name = "SB_PlayerNeoprene";
            accent = MaterialLibrary.Opaque(new Color(0.05f, 0.62f, 0.66f), 0.4f);
            skin = MaterialLibrary.Opaque(new Color(0.88f, 0.64f, 0.5f), 0.42f);
            strap = MaterialLibrary.Opaque(new Color(0.05f, 0.05f, 0.055f), 0.2f);
            tape = MaterialLibrary.Opaque(new Color(0.62f, 0.63f, 0.64f), 0.28f);
            torchYellow = MaterialLibrary.Opaque(new Color(1f, 0.76f, 0.08f), 0.55f);
            torchBlack = MaterialLibrary.Opaque(new Color(0.07f, 0.07f, 0.08f), 0.6f, 0.35f);
            torchOrange = MaterialLibrary.Opaque(new Color(1f, 0.45f, 0.08f), 0.5f);
            lensOff = MaterialLibrary.Opaque(new Color(0.72f, 0.77f, 0.82f), 0.95f);
            lensOn = MaterialLibrary.Emissive(new Color(1f, 0.97f, 0.9f), new Color(1f, 0.92f, 0.75f), 3f);

            left = BuildArm("LeftArm", false);
            right = BuildArm("RightArm", true);
        }

        PlayerHandRig BuildArm(string name, bool mirror)
        {
            var rig = new PlayerHandRig();
            rig.arm = Node(transform, name, Vector3.zero);
            rig.geo = Node(rig.arm, "Geo", Vector3.zero);
            if (mirror) rig.geo.localScale = new Vector3(-1f, 1f, 1f);

            // sleeve (neoprene suit) from the elbow to just before the wrist, teal stripe, bare wrist gap
            Part(rig.geo, "Sleeve", mTaper, neoprene, new Vector3(0f, -0.004f, -0.385f), new Vector3(0.088f, 0.078f, 0.34f));
            Part(rig.geo, "Stripe", mCylinder, accent, new Vector3(0f, -0.004f, -0.1f), new Vector3(0.09f, 0.08f, 0.016f));
            Part(rig.geo, "Wrist", mCapsule, skin, new Vector3(0f, -0.003f, -0.06f), new Vector3(0.062f, 0.054f, 0.08f));

            // glove (palm down, fingers along +Z, thumb on +X for the left hand)
            rig.hand = Node(rig.geo, "Hand", Vector3.zero);
            Part(rig.hand, "GloveCuff", mTaper, neoprene, new Vector3(0f, 0f, -0.014f), new Vector3(0.074f, 0.062f, 0.06f));
            Part(rig.hand, "Palm", mSphere, neoprene, new Vector3(0f, 0.002f, 0.068f), new Vector3(0.09f, 0.036f, 0.112f));

            float[] xs = { -0.031f, -0.0105f, 0.0105f, 0.031f };    // pinky .. index
            float[] zs = { 0.106f, 0.114f, 0.116f, 0.112f };
            float[] len1 = { 0.030f, 0.038f, 0.041f, 0.038f };
            float[] len2 = { 0.024f, 0.030f, 0.032f, 0.029f };
            float[] w = { 0.0165f, 0.0185f, 0.019f, 0.0185f };
            for (int i = 0; i < 4; i++)
            {
                Transform k = Node(rig.hand, "Finger" + i, new Vector3(xs[i], 0.002f, zs[i]));
                Part(k, "Proximal", mCapsule, neoprene, new Vector3(0f, 0f, -0.006f), new Vector3(w[i], w[i] * 0.88f, len1[i] + 0.012f));
                Transform m = Node(k, "Joint", new Vector3(0f, 0f, len1[i]));
                Part(m, "Distal", mCapsule, neoprene, new Vector3(0f, 0f, -0.005f), new Vector3(w[i] * 0.94f, w[i] * 0.84f, len2[i] + 0.008f));
                rig.knuckle[i] = k;
                rig.middle[i] = m;
            }

            rig.thumb1 = Node(rig.hand, "Thumb", new Vector3(0.042f, -0.01f, 0.04f));
            rig.thumb1.localRotation = Quaternion.Euler(12f, 38f, -35f);
            Part(rig.thumb1, "Proximal", mCapsule, neoprene, new Vector3(0f, 0f, -0.006f), new Vector3(0.022f, 0.019f, 0.045f));
            rig.thumb2 = Node(rig.thumb1, "Joint", new Vector3(0f, 0f, 0.036f));
            Part(rig.thumb2, "Distal", mCapsule, neoprene, new Vector3(0f, 0f, -0.004f), new Vector3(0.02f, 0.017f, 0.034f));
            return rig;
        }

        /// Dive lamp strapped to the back of the left glove; the model changes with the lamp tier.
        void RebuildTorch(int tier)
        {
            torchTier = tier;
            lensLit = false;
            if (left.torch != null)
            {
                Destroy(left.torch.gameObject);
                left.torch = null;
                left.lens = null;
            }
            if (tier <= 0) return;

            Transform t = Node(left.hand, "Lamp", new Vector3(0f, 0.03f, 0.02f));
            left.torch = t;
            float bodyR = tier == 1 ? 0.042f : tier == 2 ? 0.05f : 0.054f;
            float len = tier == 1 ? 0.12f : tier == 2 ? 0.11f : 0.13f;
            float headR = tier == 1 ? 0.056f : tier == 2 ? 0.078f : 0.084f;
            const float y = 0.008f;
            const float z0 = -0.02f;

            Part(t, "Strap", mCylinder, strap, new Vector3(0f, -0.03f, 0.035f), new Vector3(0.1f, 0.054f, 0.024f));
            Part(t, "Body", mCylinder, tier == 1 ? torchYellow : torchBlack, new Vector3(0f, y, z0), new Vector3(bodyR, bodyR, len));
            Part(t, "Head", mFlare, tier == 1 ? torchBlack : tier == 3 ? torchOrange : torchBlack, new Vector3(0f, y, z0 + len), new Vector3(headR, headR, 0.045f));
            Transform lens = Part(t, "Lens", mCylinder, lensOff, new Vector3(0f, y, z0 + len + 0.042f), new Vector3(headR * 0.84f, headR * 0.84f, 0.005f));
            left.lens = lens.GetComponent<Renderer>();

            if (tier == 1)
            {
                // the famous duct tape
                Part(t, "Tape", mCylinder, tape, new Vector3(0f, y, 0.02f), new Vector3(bodyR * 1.1f, bodyR * 1.1f, 0.03f));
                Part(t, "Tape2", mCylinder, tape, new Vector3(0f, y, 0.075f), new Vector3(bodyR * 1.08f, bodyR * 1.08f, 0.014f), new Vector3(0f, 0f, 0f));
            }
            else if (tier == 2)
            {
                Part(t, "Switch", mCapsule, torchOrange, new Vector3(0f, y + bodyR * 0.48f, 0.01f), new Vector3(0.012f, 0.01f, 0.03f));
            }
            else
            {
                Part(t, "Ring", mCylinder, torchOrange, new Vector3(0f, y, z0 + len - 0.014f), new Vector3(bodyR * 1.07f, bodyR * 1.07f, 0.012f));
                Part(t, "Switch", mCapsule, torchOrange, new Vector3(0f, y + bodyR * 0.48f, 0.01f), new Vector3(0.012f, 0.01f, 0.03f));
            }
        }

        static Transform Node(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default)
        {
            var go = new GameObject(name);
            Transform t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return t;
        }

        // ------------------------------------------------------------------ animation
        void LateUpdate()
        {
            if (!built || pc == null || !pc.InGameplay) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            dt = Mathf.Min(dt, 0.1f);
            time += dt;

            GearStats g = pc.Gear;
            int tier = g.hasLamp ? Mathf.Clamp(Mathf.RoundToInt(g.lampIntensity), 1, 3) : 0;
            if (tier != torchTier) RebuildTorch(tier);
            bool lit = pc.FlashlightOn;
            if (left.lens != null && lit != lensLit)
            {
                left.lens.sharedMaterial = lit ? lensOn : lensOff;
                lensLit = lit;
            }

            bool seated = pc.IsSeated;
            bool inWater = pc.IsInWater && !seated;
            float swim01 = inWater ? Mathf.Clamp01(pc.MoveSpeed / 2.2f) : 0f;
            float targetW = inWater ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(swim01 * 1.8f - 0.1f)) : 0f;
            if (pc.IsFainting) targetW = 0f;
            strokeW = Mathf.Lerp(strokeW, targetW, 1f - Mathf.Exp(-3f * dt));
            if (inWater) strokePhase = Mathf.Repeat(strokePhase + dt * Mathf.Lerp(0.4f, 0.95f, swim01), 1000f);
            rightW = Mathf.Lerp(rightW, HandCameraBusy() ? 0f : strokeW, 1f - Mathf.Exp(-6f * dt));

            Vector2 swayTarget = Vector2.ClampMagnitude(pc.LookDelta * 0.0022f, 0.03f);
            sway = Vector2.Lerp(sway, swayTarget, 1f - Mathf.Exp(-9f * dt));

            SampleStroke(strokePhase, out Vector3 sp, out Quaternion sq);
            bool holdsTorch = left.torch != null;
            PoseArm(left, false, holdsTorch ? strokeW * 0.8f : strokeW, holdsTorch, inWater, seated, sp, sq, dt);
            PoseArm(right, true, rightW, false, inWater, seated, sp, sq, dt);
        }

        void SampleStroke(float phase, out Vector3 p, out Quaternion q)
        {
            float f = phase - Mathf.Floor(phase);
            if (f < 0.3f)
            {
                float a = Mathf.SmoothStep(0f, 1f, f / 0.3f);
                p = Vector3.Lerp(K0p, K1p, a);
                q = Quaternion.Slerp(k0q, k1q, a);
            }
            else if (f < 0.7f)
            {
                float a = Mathf.SmoothStep(0f, 1f, (f - 0.3f) / 0.4f);
                p = Vector3.Lerp(K1p, K2p, a);
                q = Quaternion.Slerp(k1q, k2q, a);
            }
            else
            {
                float a = Mathf.SmoothStep(0f, 1f, (f - 0.7f) / 0.3f);
                p = Vector3.Lerp(K2p, K0p, a);
                q = Quaternion.Slerp(k2q, k0q, a);
            }
        }

        void PoseArm(PlayerHandRig rig, bool mirror, float w, bool torch, bool inWater, bool seated, Vector3 sp, Quaternion sq, float dt)
        {
            Vector3 restP, restE;
            if (seated)
            {
                restP = torch ? new Vector3(-0.17f, -0.25f, 0.31f) : new Vector3(-0.2f, -0.36f, 0.27f);
                restE = new Vector3(-6f, 8f, -8f);
            }
            else if (torch)
            {
                restP = new Vector3(-0.155f, -0.17f, 0.34f);
                restE = new Vector3(-5f, 6f, -4f);
            }
            else if (inWater && !mirror)
            {
                restP = new Vector3(-0.17f, -0.215f, 0.33f);
                restE = new Vector3(-4f, 12f, -28f);
            }
            else
            {
                // lowered out of view
                restP = new Vector3(-0.2f, -0.37f, 0.26f);
                restE = new Vector3(-12f, 10f, -12f);
            }

            Vector3 p = Vector3.Lerp(restP, sp, w);
            Quaternion q = Quaternion.Slerp(Quaternion.Euler(restE), sq, w);

            // breathing / idle sway
            float side = mirror ? 1.27f : 1f;
            p.x += Mathf.Sin(time * 1.1f * side) * 0.004f;
            p.y += Mathf.Sin(time * 1.6f * side + 0.7f) * 0.005f;
            if (inWater)
            {
                // gentle sculling while hovering
                float scull = (1f - Mathf.Clamp01(w / 0.35f)) * (torch ? 0.35f : 1f);
                p.x += Mathf.Sin(time * 2.1f) * 0.012f * scull;
                p.z += Mathf.Cos(time * 2.1f) * 0.008f * scull;
                q *= Quaternion.Euler(0f, 0f, Mathf.Sin(time * 2.1f) * 12f * scull);
            }
            if (pc.Mode == PlayerMoveMode.Land && pc.Grounded && !seated)
            {
                float ph = pc.BobPhase + (mirror ? Mathf.PI : 0f);
                float b = pc.BobAmount;
                p.x += Mathf.Sin(ph) * 0.006f * b;
                p.y -= Mathf.Abs(Mathf.Sin(ph)) * 0.012f * b;
                p.z += Mathf.Cos(ph) * 0.01f * b;
            }
            p.y -= pc.FaintAmount * 0.3f;

            if (mirror)
            {
                p.x = -p.x;
                q = new Quaternion(q.x, -q.y, -q.z, q.w);   // reflect across the YZ plane
            }
            // look lag (same direction for both hands)
            p.x -= sway.x;
            p.y -= sway.y;

            float k = 1f - Mathf.Exp(-14f * dt);
            if (!rig.poseValid)
            {
                rig.pos = p;
                rig.rot = q;
                rig.poseValid = true;
            }
            else
            {
                rig.pos = Vector3.Lerp(rig.pos, p, k);
                rig.rot = Quaternion.Slerp(rig.rot, q, k);
            }
            rig.arm.localPosition = rig.pos;
            rig.arm.localRotation = rig.rot;

            float curl = torch ? 42f : Mathf.Lerp(inWater ? 14f : 32f, 6f, w);
            for (int i = 0; i < 4; i++)
            {
                rig.knuckle[i].localRotation = Quaternion.Euler(curl + i * 2.5f, (i - 1.5f) * 4f * (1f - w * 0.5f), 0f);
                rig.middle[i].localRotation = Quaternion.Euler(curl * 1.15f, 0f, 0f);
            }
            rig.thumb2.localRotation = Quaternion.Euler(curl * 0.6f, 0f, 0f);
        }

        /// The filming module's camera model brings its own gloved right hand (always on screen while playing), so the
        /// right view-hand only joins the strokes when no hand camera exists. Otherwise two right hands would show.
        static bool HandCameraBusy()
        {
            var hc = Refs.HandCamera;
            return PlayerController.Alive(hc);
        }

        void OnDestroy()
        {
            if (mSphere != null) Destroy(mSphere);
            if (mCapsule != null) Destroy(mCapsule);
            if (mTaper != null) Destroy(mTaper);
            if (mCylinder != null) Destroy(mCylinder);
            if (mFlare != null) Destroy(mFlare);
            if (neoprene != null) Destroy(neoprene);
            if (neopreneTex != null) Destroy(neopreneTex);
        }
    }
}
