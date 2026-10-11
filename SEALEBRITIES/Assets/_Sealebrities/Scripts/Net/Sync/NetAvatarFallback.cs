using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Low-poly "bean diver" used for co-op avatars when the Player module does not publish DiverModels.Build
    /// (art bible §1.12, simplified): 1.85 m bean in a dark wetsuit with a suit-colour band and cuffs, googly eyes,
    /// a mouth that flaps while talking, tube arms/legs, fins, optional hat, and the held items of both hands
    /// (camera, lamp, two-handed rod). Swims horizontally (pivoting around the head), sits, walks.
    public sealed class NetAvatarFallback : MonoBehaviour, IDiverPuppet
    {
        static readonly Color Wetsuit = new Color(0.204f, 0.220f, 0.286f);      // #343849
        static readonly Color Skin = new Color(0.910f, 0.753f, 0.541f);         // #E8C08A
        static readonly Color Boots = new Color(0.169f, 0.145f, 0.200f);        // #2B2533
        static readonly Color EyeWhite = new Color(0.96f, 0.95f, 0.92f);
        static readonly Color Pupil = new Color(0.08f, 0.07f, 0.10f);
        static readonly Color Mouth = new Color(0.23f, 0.10f, 0.12f);
        static readonly Color Gadget = new Color(0.18f, 0.20f, 0.25f);
        static readonly Color LampYellow = new Color(0.949f, 0.788f, 0.298f);
        static readonly Color RodBrown = new Color(0.55f, 0.36f, 0.22f);

        const float Eye = 1.62f;

        Transform pivot, head, mouth, legL, legR, armL, armR, itemR, itemL, rod;
        GameObject camR, camL, lampR, lampL;
        float phase, swim01, seat01, talk;

        public Transform Head => head;

        public static GameObject Build(DiverLook look)
        {
            var root = new GameObject("FallbackDiver");
            var d = root.AddComponent<NetAvatarFallback>();
            d.Create(look);
            return root;
        }

        static Transform Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Color c, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = rot ?? Quaternion.identity;
            t.localScale = scale;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = LowPoly.Mat(c);
            r.shadowCastingMode = ShadowCastingMode.On;
            return t;
        }

        static Transform Node(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        void Create(DiverLook look)
        {
            Color suit = look.suit.a <= 0f ? new Color(0.933f, 0.498f, 0.176f) : look.suit;
            suit.a = 1f;
            Color cuff = look.cuff.a <= 0f ? Color.Lerp(suit, Color.white, 0.6f) : look.cuff;
            cuff.a = 1f;

            // everything hangs from a pivot at eye height, so the swim pose keeps the head where the camera is
            pivot = Node("Pivot", transform, new Vector3(0f, Eye, 0f));
            float y0 = -Eye;

            // bean body 0.70 .. 1.85 m (r 0.25), suit band, head front
            Part(PrimitiveType.Capsule, "Bean", pivot, new Vector3(0f, y0 + 1.275f, 0f), new Vector3(0.5f, 0.575f, 0.5f), Wetsuit);
            Part(PrimitiveType.Cylinder, "SuitBand", pivot, new Vector3(0f, y0 + 1.06f, 0f), new Vector3(0.515f, 0.15f, 0.515f), suit);
            Part(PrimitiveType.Cylinder, "Belt", pivot, new Vector3(0f, y0 + 0.86f, 0f), new Vector3(0.505f, 0.03f, 0.505f), cuff);

            head = Node("Head", pivot, new Vector3(0f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Face", head, new Vector3(0f, -0.03f, 0.16f), new Vector3(0.34f, 0.26f, 0.2f), Skin);
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = Part(PrimitiveType.Sphere, s < 0 ? "EyeL" : "EyeR", head, new Vector3(0.048f * s, 0.0f, 0.245f), Vector3.one * 0.11f, EyeWhite);
                Part(PrimitiveType.Sphere, "Pupil", eye, new Vector3(0.12f * s, -0.08f, 0.42f), Vector3.one * 0.5f, Pupil);
            }
            mouth = Part(PrimitiveType.Sphere, "Mouth", head, new Vector3(0f, -0.11f, 0.25f), new Vector3(0.06f, 0.022f, 0.02f), Mouth);
            BuildHat(look.hat, head);

            // legs (hip pivots) with fins
            legL = Node("LegL", pivot, new Vector3(-0.12f, y0 + 0.75f, 0f));
            legR = Node("LegR", pivot, new Vector3(0.12f, y0 + 0.75f, 0f));
            foreach (var leg in new[] { legL, legR })
            {
                Part(PrimitiveType.Capsule, "Leg", leg, new Vector3(0f, -0.36f, 0f), new Vector3(0.22f, 0.37f, 0.22f), Wetsuit);
                Part(PrimitiveType.Cylinder, "Stripe", leg, new Vector3(0f, -0.42f, 0f), new Vector3(0.23f, 0.05f, 0.23f), suit);
                Part(PrimitiveType.Cube, "Fin", leg, new Vector3(0f, -0.73f, 0.16f), new Vector3(0.2f, 0.03f, 0.42f), Boots);
            }

            // arms (shoulder pivots) with mitten hands, cuffs and item sockets
            armL = Node("ArmL", pivot, new Vector3(-0.27f, y0 + 1.36f, 0f));
            armR = Node("ArmR", pivot, new Vector3(0.27f, y0 + 1.36f, 0f));
            itemL = BuildArm(armL, cuff, -1f);
            itemR = BuildArm(armR, cuff, 1f);

            camR = BuildCamera(itemR);
            camL = BuildCamera(itemL);
            lampR = BuildLamp(itemR);
            lampL = BuildLamp(itemL);

            // two-handed rod held across the body
            rod = Node("Rod", pivot, new Vector3(0.12f, y0 + 1.05f, 0.32f));
            rod.localRotation = Quaternion.Euler(55f, 8f, 0f);   // pole tilted forward and up
            Part(PrimitiveType.Cylinder, "Pole", rod, new Vector3(0f, 0.8f, 0f), new Vector3(0.025f, 0.85f, 0.025f), RodBrown);
            Part(PrimitiveType.Cylinder, "Reel", rod, new Vector3(0.04f, 0.12f, 0f), new Vector3(0.07f, 0.02f, 0.07f), Gadget, Quaternion.Euler(0f, 0f, 90f));
            rod.gameObject.SetActive(false);
        }

        Transform BuildArm(Transform arm, Color cuff, float side)
        {
            Part(PrimitiveType.Capsule, "Arm", arm, new Vector3(0f, -0.33f, 0f), new Vector3(0.12f, 0.35f, 0.12f), Wetsuit);
            Part(PrimitiveType.Cylinder, "Cuff", arm, new Vector3(0f, -0.6f, 0f), new Vector3(0.135f, 0.02f, 0.135f), cuff);
            Part(PrimitiveType.Sphere, "Mitten", arm, new Vector3(0f, -0.68f, 0f), new Vector3(0.14f, 0.16f, 0.12f), Wetsuit);
            var socket = Node("Item", arm, new Vector3(0f, -0.72f, 0.02f));
            socket.localRotation = Quaternion.Euler(90f, 0f, 0f);   // items point along the arm when it is raised forward
            return socket;
        }

        static GameObject BuildCamera(Transform socket)
        {
            var c = Node("Camera", socket, new Vector3(0f, 0f, 0.06f));
            Part(PrimitiveType.Cube, "Body", c, Vector3.zero, new Vector3(0.09f, 0.075f, 0.13f), Gadget);
            Part(PrimitiveType.Cylinder, "Lens", c, new Vector3(0f, 0.005f, 0.08f), new Vector3(0.05f, 0.025f, 0.05f), new Color(0.1f, 0.1f, 0.12f), Quaternion.Euler(90f, 0f, 0f));
            Part(PrimitiveType.Sphere, "Rec", c, new Vector3(0.03f, 0.04f, 0.04f), Vector3.one * 0.015f, new Color(0.9f, 0.2f, 0.2f));
            c.gameObject.SetActive(false);
            return c.gameObject;
        }

        static GameObject BuildLamp(Transform socket)
        {
            var l = Node("Lamp", socket, new Vector3(0f, 0f, 0.05f));
            Part(PrimitiveType.Cylinder, "Body", l, Vector3.zero, new Vector3(0.045f, 0.07f, 0.045f), Gadget, Quaternion.Euler(90f, 0f, 0f));
            Part(PrimitiveType.Cylinder, "Head", l, new Vector3(0f, 0f, 0.08f), new Vector3(0.065f, 0.02f, 0.065f), LampYellow, Quaternion.Euler(90f, 0f, 0f));
            l.gameObject.SetActive(false);
            return l.gameObject;
        }

        static void BuildHat(int hat, Transform head)
        {
            switch (hat)
            {
                case 1:   // bucket hat
                    Part(PrimitiveType.Cylinder, "Brim", head, new Vector3(0f, 0.2f, 0f), new Vector3(0.62f, 0.012f, 0.62f), new Color(0.788f, 0.706f, 0.541f));
                    Part(PrimitiveType.Cylinder, "Crown", head, new Vector3(0f, 0.27f, 0f), new Vector3(0.42f, 0.07f, 0.42f), new Color(0.788f, 0.706f, 0.541f));
                    break;
                case 2:   // viking helmet
                    Part(PrimitiveType.Sphere, "Helmet", head, new Vector3(0f, 0.2f, 0f), new Vector3(0.48f, 0.3f, 0.48f), new Color(0.604f, 0.604f, 0.651f));
                    Part(PrimitiveType.Capsule, "HornL", head, new Vector3(-0.24f, 0.3f, 0f), new Vector3(0.06f, 0.11f, 0.06f), new Color(0.929f, 0.902f, 0.855f), Quaternion.Euler(0f, 0f, 40f));
                    Part(PrimitiveType.Capsule, "HornR", head, new Vector3(0.24f, 0.3f, 0f), new Vector3(0.06f, 0.11f, 0.06f), new Color(0.929f, 0.902f, 0.855f), Quaternion.Euler(0f, 0f, -40f));
                    break;
                case 3:   // shark fin
                    Part(PrimitiveType.Cube, "Fin", head, new Vector3(0f, 0.36f, -0.05f), new Vector3(0.05f, 0.26f, 0.22f), new Color(0.369f, 0.478f, 0.588f), Quaternion.Euler(-20f, 0f, 0f));
                    break;
                case 4:   // duck
                    Part(PrimitiveType.Sphere, "Duck", head, new Vector3(0f, 0.3f, 0f), new Vector3(0.2f, 0.17f, 0.24f), new Color(0.949f, 0.788f, 0.298f));
                    Part(PrimitiveType.Sphere, "DuckHead", head, new Vector3(0f, 0.42f, 0.08f), Vector3.one * 0.12f, new Color(0.949f, 0.788f, 0.298f));
                    Part(PrimitiveType.Cube, "Beak", head, new Vector3(0f, 0.41f, 0.16f), new Vector3(0.06f, 0.02f, 0.06f), new Color(0.95f, 0.55f, 0.2f));
                    break;
            }
        }

        // ------------------------------------------------------------------ IDiverPuppet
        public void SetPose(bool swimming, bool seated, float speed, HoldItem right, HoldItem left, bool lightOn)
        {
            float dt = Time.deltaTime;
            swim01 = Mathf.MoveTowards(swim01, swimming ? 1f : 0f, dt * 2.5f);
            seat01 = Mathf.MoveTowards(seat01, seated ? 1f : 0f, dt * 4f);
            phase += dt * (swimming ? 2.2f + speed * 1.2f : 1.5f + speed * 2.2f);
            if (phase > 1000f) phase -= 1000f;

            // body: upright → horizontal (head stays at the eye point); seated lowers the body to the seat eye height
            pivot.localPosition = new Vector3(0f, Mathf.Lerp(Eye, PlayerController.SeatedEyeHeight, seat01), 0f);
            pivot.localRotation = Quaternion.Euler(78f * swim01, 0f, 0f);
            head.localRotation = Quaternion.Euler(-62f * swim01, 0f, 0f);   // swimmers look ahead, not at the floor

            // legs: walk swing / flutter kick / sitting
            float walk = Mathf.Clamp01(speed / 3f) * (1f - swim01) * (1f - seat01);
            float kick = swim01 * (0.35f + Mathf.Clamp01(speed / 2f) * 0.65f);
            float s = Mathf.Sin(phase * Mathf.PI * 2f);
            float legA = s * (28f * walk + 18f * kick);
            legL.localRotation = Quaternion.Euler(-90f * seat01 + legA, 0f, 0f);
            legR.localRotation = Quaternion.Euler(-90f * seat01 - legA, 0f, 0f);

            // arms: raised forward while holding something, otherwise swing / glide along the body
            bool rodHeld = right == HoldItem.FishingRod || left == HoldItem.FishingRod;
            float swing = s * 22f * walk;
            armR.localRotation = Quaternion.Euler(ArmPitch(rodHeld || right != HoldItem.None, swing, seat01), 0f, rodHeld ? -18f : 6f);
            armL.localRotation = Quaternion.Euler(ArmPitch(rodHeld || left != HoldItem.None, -swing, seat01), 0f, rodHeld ? 18f : -6f);

            camR.SetActive(right == HoldItem.Camera);
            camL.SetActive(left == HoldItem.Camera);
            lampR.SetActive(right == HoldItem.Flashlight);
            lampL.SetActive(left == HoldItem.Flashlight);
            if (rod.gameObject.activeSelf != rodHeld) rod.gameObject.SetActive(rodHeld);

            // mouth flap
            mouth.localScale = new Vector3(0.06f, 0.022f + talk * 0.05f, 0.02f);
        }

        float ArmPitch(bool holding, float swing, float seated)
        {
            if (holding) return -72f - 70f * swim01;   // keep the item pointing ahead in the swim pose
            return Mathf.Lerp(Mathf.Lerp(swing, -20f, seated) + 8f, 0f, swim01);
        }

        public void SetTalking(float loudness01) { talk = Mathf.Lerp(talk, Mathf.Clamp01(loudness01), 0.5f); }
    }
}
