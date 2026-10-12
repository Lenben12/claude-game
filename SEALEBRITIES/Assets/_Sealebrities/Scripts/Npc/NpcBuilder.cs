using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Rig of a built NPC (bones are plain transforms; NpcActor animates them procedurally – no Animator).
    /// Root at the feet, +Z forward.
    public sealed class NpcBody : MonoBehaviour
    {
        public Transform pivot, head, shoulderL, shoulderR, elbowL, elbowR, handL, handR, hipL, hipR, kneeL, kneeR;
        public Transform pupilL, pupilR, lidL, lidR, mouth, mouthOpen, moustache, hat, browL, browR, propR, propL, extra;
        public float hipY, height, upperArm, foreArm, thigh, shin, shoulderY;
        /// Base local rotations of the head and pivot (lean), restored every frame before the procedural offsets.
        public Quaternion pivotBase = Quaternion.identity;
        public Vector3 pivotBasePos;
        public Renderer[] renderers;
        public float eyeR;
    }

    /// Builds the NPCs in the round-3 character style (GDD §19 „Figuren schöner, realistischer – weder klotzig noch zu
    /// kugelig“, §20 Q38 „How-to-Fish-Stil, natürlicher“): the same natural proportions as the new player characters
    /// (DiverBuilder) – a tall egg head on a short neck, shoulders, a waist, tapered arms and legs with elbows and knees,
    /// mittens with a thumb, a readable face (eyes with lids, brows, nose, ears, mouth) – dressed in everyday clothes
    /// (shirts, shorts, trousers, aprons, sailcloth). Smooth shading, LOD1/LOD2 like the divers (DiverLod).
    /// Respect rules: varied skin tones, no costumes or symbols of real peoples – the hermits are eccentric dropouts
    /// in patched sailcloth, nets, a colander for a hat.
    /// All heights below are in the player's character space (1.866 m crown) and scaled per NPC.
    public static class NpcBuilder
    {
        const int Segs = 24;

        // ---- the player character's dimensions (DiverBuilder) – character space
        const float HipJoint = 0.92f, NeckTop = 1.53f, ShoulderH = 1.40f, ShoulderX = 0.215f, HipX = 0.10f;
        const float UpperArm = 0.29f, ForeArm = 0.26f, Thigh = 0.42f, Shin = 0.42f, Crown = 1.866f;
        const float EyeY = 1.712f, EyeX = 0.05f, EyeR = 0.035f, NoseY = 1.655f, MouthY = 1.6f, BrowY = 1.762f;
        const float WaistLine = 1.06f, Depth = 0.72f;

        /// Torso profile (radius, height) crotch → collar, the pelvis and chest of the divers merged.
        static readonly Vector2[] TorsoProfile =
        {
            new Vector2(0.000f, 0.800f), new Vector2(0.082f, 0.805f), new Vector2(0.136f, 0.822f), new Vector2(0.164f, 0.858f),
            new Vector2(0.175f, 0.905f), new Vector2(0.171f, 0.960f), new Vector2(0.161f, 1.020f), new Vector2(0.154f, 1.075f),
            new Vector2(0.160f, 1.140f), new Vector2(0.178f, 1.220f), new Vector2(0.193f, 1.300f), new Vector2(0.203f, 1.362f),
            new Vector2(0.202f, 1.412f), new Vector2(0.183f, 1.450f), new Vector2(0.135f, 1.476f), new Vector2(0.075f, 1.492f),
            new Vector2(0.000f, 1.500f),
        };

        /// The tall egg head (chin 1.528 → crown 1.866, widest r 0.125 at the temples).
        static readonly Vector2[] HeadProfile =
        {
            new Vector2(0.000f, 1.528f), new Vector2(0.040f, 1.531f), new Vector2(0.075f, 1.545f), new Vector2(0.100f, 1.570f),
            new Vector2(0.114f, 1.605f), new Vector2(0.121f, 1.645f), new Vector2(0.1245f, 1.690f), new Vector2(0.124f, 1.735f),
            new Vector2(0.117f, 1.780f), new Vector2(0.102f, 1.818f), new Vector2(0.076f, 1.846f), new Vector2(0.040f, 1.862f),
            new Vector2(0.000f, 1.866f),
        };

        struct Look
        {
            public float height, legF;
            /// Body type: width factors at hips / waist / chest / shoulders, limb thickness, belly bulge (m).
            public float hip, waist, chest, shoulder, limb, belly;
            public Color skin, top, bottom, sleeve, legs, feet, sock, sock2;
            public int sleeves;     // 0 none, 1 short, 2 long
            public int lower;       // 0 shorts, 1 long trousers, 2 long skirt, 3 one-piece (bottom = top colour)
            public int shoes;       // 0 bare, 1 sandals, 2 shoes, 3 boots, 4 socks + sandals
            public float eyeScale;
            public bool crossEyed;
            public Color hair;
            public int hairStyle;   // 0 none, 1 short, 2 bun, 3 man bun, 4 shoulder length, 5 white ring, 6 ponytail, 7 braid, 8 wild long
            public int beard;       // 0 none, 1 full, 2 stubble, 3 long white (Toni)
            public Color nose;
            public float noseR;
            public float lean;
            public int mouth;       // 0 smile, 1 grin, 2 flat, 3 open
            public int brows;       // 0 none, 1 bushy, 2 thin
            public Color browColor;
        }

        static Color H(string s) => LowPoly.Hex(s);
        static Material M(Color c, float s = 0.12f) => NpcMesh.Mat(c, s);

        static readonly Color Ink = H("#1E1B2B"), EyeWhite = H("#F4EFE6"), MouthC = H("#3A1E2E"), Gold = H("#E8C65A");

        // per build (static scratch – Build runs on the main thread, one NPC at a time)
        static float S, legDrop;
        static Look cur;

        // ------------------------------------------------------------------ public
        public static GameObject Build(string id)
        {
            var look = LookFor(id);
            cur = look;
            legDrop = (1f - look.legF) * HipJoint;
            S = look.height / (Crown - legDrop);
            var root = new GameObject("NPC_" + id);
            var body = root.AddComponent<NpcBody>();
            BuildBody(root.transform, body, look, id);
            Accessories(id, body, look);
            // L.13: LOD1 / LOD2 proxies on the same bones – before the renderer list
            DiverLod.Build(root);
            body.renderers = root.GetComponentsInChildren<Renderer>(true);
            return root;
        }

        /// Character-space height → NPC height above the feet.
        static float Y(float y) => y <= HipJoint ? y * S * (1f - legDrop / HipJoint) : (y - legDrop) * S;
        /// Height relative to the pivot (the hip joints).
        static float P(float y) => Y(y) - Y(HipJoint);
        /// Height relative to the head node (top of the neck).
        static float Hd(float y) => (y - NeckTop) * S;

        static float HeadR(float y)
        {
            var p = HeadProfile;
            if (y <= p[0].y) return p[0].x * S;
            for (int i = 0; i < p.Length - 1; i++)
                if (y <= p[i + 1].y) return Mathf.Lerp(p[i].x, p[i + 1].x, (y - p[i].y) / Mathf.Max(1e-4f, p[i + 1].y - p[i].y)) * S;
            return 0f;
        }

        /// Point on the head surface (head-node space) at character height y and sideways offset x (metres, scaled).
        static Vector3 HeadPt(float x, float y, float outward = 0f)
        {
            float r = HeadR(y);
            float z = Mathf.Sqrt(Mathf.Max(0.0001f, r * r - x * x));
            return new Vector3(x, Hd(y), z + outward);
        }

        static float ShapeScale(float y, in Look l)
        {
            if (y <= 0.88f) return l.hip;
            if (y <= 1.06f) return Mathf.Lerp(l.hip, l.waist, (y - 0.88f) / 0.18f);
            if (y <= 1.25f) return Mathf.Lerp(l.waist, l.chest, (y - 1.06f) / 0.19f);
            if (y <= 1.39f) return Mathf.Lerp(l.chest, l.shoulder, (y - 1.25f) / 0.14f);
            return Mathf.Lerp(l.shoulder, 1f, Mathf.Clamp01((y - 1.39f) / 0.11f));
        }

        /// Half width of the torso at character height y (scaled) – accessories sit on it.
        static float TorsoR(float y)
        {
            var p = TorsoProfile;
            float r = 0f;
            if (y <= p[0].y) r = p[0].x;
            else
            {
                r = p[p.Length - 1].x;
                for (int i = 0; i < p.Length - 1; i++)
                    if (y <= p[i + 1].y) { r = Mathf.Lerp(p[i].x, p[i + 1].x, (y - p[i].y) / Mathf.Max(1e-4f, p[i + 1].y - p[i].y)); break; }
            }
            return r * ShapeScale(y, cur) * S;
        }

        /// Front surface depth (z) of the torso at character height y and sideways x (incl. the belly).
        static float TorsoZ(float y, float x = 0f)
        {
            float rx = TorsoR(y), rz = rx * Depth;
            float t = rx > 1e-4f ? Mathf.Clamp01(Mathf.Abs(x) / rx) : 0f;
            float z = rz * Mathf.Sqrt(Mathf.Max(0f, 1f - t * t));
            float fy = 1f - Mathf.Clamp01(Mathf.Abs(y - 1.0f) / 0.24f);
            return z + cur.belly * fy * fy * (3f - 2f * fy) * S;
        }

        // ------------------------------------------------------------------ looks
        static Look Base(float height)
        {
            return new Look
            {
                height = height, legF = 1f, hip = 1f, waist = 1f, chest = 1f, shoulder = 1f, limb = 1f, belly = 0f,
                skin = H("#E8C08A"), top = H("#EDE6DA"), bottom = H("#4F7FAE"), sleeve = H("#EDE6DA"), legs = H("#E8C08A"), feet = H("#5E4436"),
                sock = H("#EDE6DA"), sock2 = H("#EDE6DA"), sleeves = 1, lower = 0, shoes = 2, eyeScale = 1f, hair = H("#3A2C2A"), hairStyle = 0, beard = 0,
                nose = H("#E8C08A"), noseR = 0.022f, lean = 0f, mouth = 0, brows = 2, browColor = H("#3A2C2A")
            };
        }

        static Look LookFor(string id)
        {
            var d = NpcDatabase.Get(id);
            float h = d != null ? d.height : 1.75f;
            var l = Base(h);
            switch (id)
            {
                case NpcDatabase.Horst:
                    // stocky, round belly, short legs – the harbourmaster in his pineapple shirt
                    l.legF = 0.86f; l.hip = 1.18f; l.waist = 1.38f; l.chest = 1.2f; l.shoulder = 1.02f; l.limb = 1.1f; l.belly = 0.07f;
                    l.skin = H("#E9A27A"); l.top = H("#A8478F"); l.sleeve = H("#A8478F"); l.bottom = H("#B5A07A");
                    l.legs = H("#E9A27A"); l.sock = H("#EDE6DA"); l.sock2 = l.sock; l.feet = H("#6B4636"); l.shoes = 4; l.sleeves = 1; l.lower = 0;
                    l.eyeScale = 1.15f; l.crossEyed = true; l.nose = H("#D9534F"); l.noseR = 0.03f; l.mouth = 1; l.brows = 1; l.browColor = H("#E07B39");
                    l.hair = H("#E07B39"); l.hairStyle = 5;
                    break;
                case NpcDatabase.Wanda:
                    l.skin = H("#D6A574"); l.top = H("#EE7F2D"); l.sleeve = H("#EE7F2D"); l.bottom = H("#EE7F2D"); l.legs = H("#EE7F2D");
                    l.lower = 3; l.sleeves = 2; l.shoes = 3; l.feet = H("#5E4436"); l.mouth = 1; l.hair = H("#7A4448"); l.hairStyle = 4; l.browColor = H("#7A4448");
                    l.hip = 1.02f; l.waist = 0.92f; l.chest = 0.95f; l.shoulder = 0.95f; l.limb = 0.92f;
                    break;
                case NpcDatabase.Fin:
                    l.skin = H("#A8724A"); l.top = H("#343849"); l.sleeve = H("#EDE6DA"); l.bottom = H("#343849"); l.legs = H("#343849");
                    l.lower = 3; l.sleeves = 2; l.shoes = 2; l.feet = H("#2B2533"); l.hair = H("#2E2430"); l.hairStyle = 2; l.mouth = 0; l.browColor = H("#2E2430");
                    l.hip = 0.98f; l.waist = 0.9f; l.chest = 0.92f; l.shoulder = 0.92f; l.limb = 0.9f;
                    break;
                case NpcDatabase.Chad:
                case NpcDatabase.Brad:
                    l.skin = H("#E8935A"); l.top = H("#F4EFE6"); l.sleeve = H("#F4EFE6"); l.bottom = H("#F4EFE6"); l.legs = H("#F4EFE6");
                    l.lower = 1; l.sleeves = 1; l.shoes = 2; l.feet = H("#F4EFE6"); l.mouth = 1;
                    l.hip = 0.98f; l.waist = 0.95f; l.chest = 1.12f; l.shoulder = 1.16f; l.limb = 1.08f;
                    l.hair = H("#5E4436"); l.hairStyle = id == NpcDatabase.Brad ? 3 : 1; l.browColor = H("#5E4436");
                    break;
                case NpcDatabase.Toni:
                    l.skin = H("#E8C08A"); l.top = H("#3FA9A0"); l.sleeve = H("#3FA9A0"); l.bottom = H("#B5A07A"); l.legs = H("#E8C08A");
                    l.lower = 0; l.sleeves = 1; l.shoes = 0; l.feet = H("#E8C08A"); l.hair = H("#F4EFE6"); l.hairStyle = 5; l.nose = H("#D9534F"); l.noseR = 0.026f;
                    l.waist = 1.12f; l.belly = 0.035f; l.mouth = 0; l.brows = 1; l.browColor = H("#F4EFE6"); l.beard = 3;
                    break;
                // ---- the hermits (round 3)
                case NpcDatabase.HLeader:
                    l.skin = H("#C99A6E"); l.top = H("#D9CBA8"); l.sleeve = H("#D9CBA8"); l.bottom = H("#6E7F5E"); l.legs = H("#C99A6E");
                    l.lower = 2; l.sleeves = 2; l.shoes = 0; l.feet = H("#C99A6E"); l.hair = H("#CFCAC2"); l.hairStyle = 7; l.browColor = H("#CFCAC2");
                    l.mouth = 2; l.lean = 3f; l.hip = 1.0f; l.waist = 0.92f; l.chest = 0.95f; l.shoulder = 0.94f; l.limb = 0.92f;
                    break;
                case NpcDatabase.HSmith:
                    l.skin = H("#B07A52"); l.top = H("#8A8C96"); l.sleeve = H("#8A8C96"); l.bottom = H("#4A4E5C"); l.legs = H("#4A4E5C");
                    l.lower = 1; l.sleeves = 0; l.shoes = 3; l.feet = H("#4A3428"); l.hair = H("#8E3B2A"); l.hairStyle = 0; l.beard = 1;
                    l.brows = 1; l.browColor = H("#8E3B2A"); l.mouth = 2; l.hip = 1.06f; l.waist = 1.12f; l.chest = 1.2f; l.shoulder = 1.22f; l.limb = 1.22f; l.belly = 0.02f;
                    break;
                case NpcDatabase.HHunter:
                    l.skin = H("#D6A574"); l.top = H("#9C8F6E"); l.sleeve = H("#9C8F6E"); l.bottom = H("#6B5A44"); l.legs = H("#D6A574");
                    l.lower = 0; l.sleeves = 0; l.shoes = 0; l.feet = H("#D6A574"); l.hair = H("#3A2C2A"); l.hairStyle = 8; l.beard = 2;
                    l.browColor = H("#3A2C2A"); l.brows = 1; l.mouth = 2; l.hip = 0.95f; l.waist = 0.86f; l.chest = 0.98f; l.shoulder = 1.02f; l.limb = 0.95f;
                    break;
                case NpcDatabase.HCook:
                    l.skin = H("#E8C08A"); l.top = H("#E07B39"); l.sleeve = H("#E07B39"); l.bottom = H("#5E5A4E"); l.legs = H("#E8C08A");
                    l.lower = 0; l.sleeves = 1; l.shoes = 1; l.feet = H("#8E6C50"); l.hair = H("#3A2C2A"); l.hairStyle = 1; l.beard = 2;
                    l.mouth = 1; l.hip = 1.15f; l.waist = 1.35f; l.chest = 1.18f; l.shoulder = 1.04f; l.limb = 1.08f; l.belly = 0.06f; l.legF = 0.92f;
                    break;
                case NpcDatabase.HLookout:
                    l.skin = H("#8E5E3E"); l.top = H("#3F6E8C"); l.sleeve = H("#3F6E8C"); l.bottom = H("#C9B48A"); l.legs = H("#C9B48A");
                    l.lower = 1; l.sleeves = 2; l.shoes = 4; l.sock = H("#F2C94C"); l.sock2 = H("#3FB8C4"); l.feet = H("#5E4436");
                    l.hair = H("#2E2430"); l.hairStyle = 4; l.browColor = H("#2E2430"); l.mouth = 0;
                    l.hip = 0.96f; l.waist = 0.86f; l.chest = 0.9f; l.shoulder = 0.9f; l.limb = 0.88f;
                    break;
                case NpcDatabase.HGardener:
                    l.skin = H("#D6A574"); l.top = H("#6E9445"); l.sleeve = H("#6E9445"); l.bottom = H("#7A6A4A"); l.legs = H("#7A6A4A");
                    l.lower = 1; l.sleeves = 1; l.shoes = 1; l.feet = H("#8E6C50"); l.hair = H("#B9B2A8"); l.hairStyle = 6; l.beard = 2;
                    l.browColor = H("#B9B2A8"); l.brows = 1; l.mouth = 0; l.lean = 5f; l.waist = 1.05f; l.belly = 0.015f; l.limb = 0.95f;
                    break;
            }
            if (l.noseR > 0f && l.nose == default) l.nose = l.skin;
            return l;
        }

        // ------------------------------------------------------------------ body
        static void BuildBody(Transform root, NpcBody b, Look l, string id)
        {
            float hipY = Y(HipJoint);
            b.hipY = hipY;
            b.height = l.height;
            Material skin = M(l.skin, 0.1f), top = M(l.top, 0.14f), bottom = M(l.bottom, 0.14f);

            b.pivot = NpcMesh.Node(root, "Pivot", new Vector3(0f, hipY, 0f), new Vector3(l.lean, 0f, 0f));
            b.pivotBase = b.pivot.localRotation;
            b.pivotBasePos = b.pivot.localPosition;

            // ---- torso: one lathe crotch → collar (submeshes 0 top, 1 bottom), scaled in z (depth), belly pushed forward
            int lower = l.lower;
            var prof = new Vector2[TorsoProfile.Length];
            for (int i = 0; i < prof.Length; i++)
            {
                float y = TorsoProfile[i].y;
                prof[i] = new Vector2(TorsoProfile[i].x * ShapeScale(y, l) * S, P(y));
            }
            var smooth = NpcMesh.Smooth(prof, 2);
            float waistP = P(WaistLine);
            float belly = l.belly * S;
            var torsoMesh = NpcMesh.Cached("r3torso_" + id, () =>
            {
                var m = NpcMesh.Lathe(smooth, Segs, 2, (i, j) =>
                {
                    float y = (smooth[i].y + smooth[Mathf.Min(smooth.Length - 1, i + 1)].y) * 0.5f;
                    return lower != 3 && y < waistP ? 1 : 0;
                });
                return Bulge(m, belly / Depth, P(1.0f), 0.24f * S);
            });
            var torso = NpcMesh.Part(b.pivot, "Torso", torsoMesh, top, Vector3.zero, new Vector3(1f, 1f, Depth));
            torso.sharedMaterials = new[] { top, lower == 3 ? top : bottom };
            // waistband over the seam
            if (lower != 3)
                NpcMesh.Part(b.pivot, "Waistband", NpcMesh.Torus(TorsoR(WaistLine) * 1.01f, 0.014f * S, Segs, 6), bottom, new Vector3(0f, waistP, 0f), new Vector3(1f, 1f, Depth * 1.02f), default, false);

            // ---- neck + collar
            NpcMesh.Part(b.pivot, "Neck", NpcMesh.Capsule(0.052f * S, 0.15f * S, 14), skin, new Vector3(0f, P(1.42f), -0.004f), Vector3.one, default, false);
            NpcMesh.Part(b.pivot, "Collar", NpcMesh.Torus(0.066f * S, 0.016f * S, 16, 6), top, new Vector3(0f, P(1.488f), -0.004f), Vector3.one, default, false);

            // ---- head
            b.head = NpcMesh.Node(b.pivot, "Head", new Vector3(0f, P(NeckTop), 0f), new Vector3(-l.lean * 0.7f, 0f, 0f));
            var hp = new Vector2[HeadProfile.Length];
            for (int i = 0; i < hp.Length; i++) hp[i] = new Vector2(HeadProfile[i].x * S, Hd(HeadProfile[i].y));
            var headSmooth = NpcMesh.Smooth(hp, 2);
            NpcMesh.Part(b.head, "HeadBean", NpcMesh.Cached("r3head_" + Mathf.RoundToInt(S * 1000f), () => NpcMesh.Lathe(headSmooth, Segs)), skin, Vector3.zero, Vector3.one);
            Ears(b, skin);
            Face(b, l);
            Hair(b, l);
            Beard(b, l);

            // ---- arms
            b.shoulderY = P(ShoulderH) + hipY;
            b.upperArm = UpperArm * S;
            b.foreArm = ForeArm * S;
            float sx = ShoulderX * S * l.shoulder;
            b.shoulderL = NpcMesh.Node(b.pivot, "ShoulderL", new Vector3(-sx, P(ShoulderH), 0f));
            b.shoulderR = NpcMesh.Node(b.pivot, "ShoulderR", new Vector3(sx, P(ShoulderH), 0f));
            Material upper = l.sleeves >= 1 ? M(l.sleeve, 0.14f) : skin;
            Material fore = l.sleeves >= 2 ? M(l.sleeve, 0.14f) : skin;
            Arm(b.shoulderL, out b.elbowL, out b.handL, l, upper, fore, skin, top);
            Arm(b.shoulderR, out b.elbowR, out b.handR, l, upper, fore, skin, top);

            // ---- legs
            float hx = HipX * S * Mathf.Lerp(1f, l.hip, 0.7f);
            b.thigh = Thigh * S * l.legF;
            b.shin = Shin * S * l.legF;
            b.hipL = NpcMesh.Node(b.pivot, "HipL", new Vector3(-hx, 0f, 0f));
            b.hipR = NpcMesh.Node(b.pivot, "HipR", new Vector3(hx, 0f, 0f));
            Material thighM = lower == 1 || lower == 3 ? M(l.legs, 0.14f) : skin;
            Material shinM = lower == 1 || lower == 3 ? M(l.legs, 0.14f) : l.shoes == 4 ? M(l.sock, 0.1f) : skin;
            Material shinM2 = lower == 1 || lower == 3 ? shinM : l.shoes == 4 ? M(l.sock2, 0.1f) : skin;
            Leg(b.hipL, out b.kneeL, l, thighM, shinM, bottom, -1);
            Leg(b.hipR, out b.kneeR, l, thighM, shinM2, bottom, 1);
            if (lower == 2)
                NpcMesh.Part(b.pivot, "Skirt", NpcMesh.Cached("r3skirt_" + id, () => SkirtMesh(l)), bottom, Vector3.zero, new Vector3(1f, 1f, 0.92f));
        }

        /// Pushes the lower front of the torso forward (belly) with a smooth falloff (mesh space; z is scaled by Depth).
        static Mesh Bulge(Mesh m, float amount, float centerY, float halfHeight)
        {
            if (amount <= 0.0005f) return m;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                float fy = 1f - Mathf.Clamp01(Mathf.Abs(v[i].y - centerY) / halfHeight);
                fy = fy * fy * (3f - 2f * fy);
                float fz = Mathf.Clamp01(v[i].z / (0.12f * S));
                v[i].z += amount * fy * fz;
            }
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh SkirtMesh(Look l)
        {
            float r = TorsoR(0.92f) * 1.04f;
            float top = P(1.0f), down = P(0.2f);
            var prof = NpcMesh.Smooth(new[] { new Vector2(r * 1.35f, down), new Vector2(r * 1.22f, Mathf.Lerp(down, top, 0.4f)), new Vector2(r * 1.06f, Mathf.Lerp(down, top, 0.8f)), new Vector2(r * 0.98f, top) }, 3);
            return NpcMesh.Lathe(prof, Segs);
        }

        static void Arm(Transform shoulder, out Transform elbow, out Transform hand, Look l, Material upper, Material fore, Material skin, Material top)
        {
            float k = l.limb * S;
            // shoulder cap in the shirt colour (defined shoulders – the player characters have them too)
            NpcMesh.Part(shoulder, "ShoulderCap", NpcMesh.Sphere(8, 12), l.sleeves >= 1 ? upper : top, new Vector3(0f, -0.01f * S, 0f), Vector3.one * (0.125f * k), default, false);
            NpcMesh.Part(shoulder, "UpperArm", NpcMesh.Limb(0.05f * k, 0.044f * k, UpperArm * S, 12), upper, Vector3.zero, Vector3.one);
            if (l.sleeves == 1) NpcMesh.Part(shoulder, "Sleeve", NpcMesh.Torus(0.052f * k, 0.012f * S, 12, 5), upper, new Vector3(0f, -UpperArm * S * 0.55f, 0f), Vector3.one, default, false);
            elbow = NpcMesh.Node(shoulder, "Elbow", new Vector3(0f, -UpperArm * S, 0f));
            NpcMesh.Part(elbow, "Ball", NpcMesh.Sphere(6, 10), fore, Vector3.zero, Vector3.one * (0.092f * k), default, false);
            NpcMesh.Part(elbow, "ForeArm", NpcMesh.Limb(0.044f * k, 0.036f * k, ForeArm * S, 12), fore, Vector3.zero, Vector3.one);
            if (l.sleeves == 2) NpcMesh.Part(elbow, "Cuff", NpcMesh.Torus(0.04f * k, 0.01f * S, 12, 4), fore, new Vector3(0f, -ForeArm * S + 0.02f * S, 0f), Vector3.one, default, false);
            hand = NpcMesh.Node(elbow, "Hand", new Vector3(0f, -ForeArm * S - 0.045f * S, 0f));
            NpcMesh.Part(hand, "Mitten", NpcMesh.Sphere(8, 12), skin, new Vector3(0f, -0.012f * S, 0.004f * S), new Vector3(0.08f, 0.106f, 0.056f) * (S * Mathf.Lerp(1f, l.limb, 0.5f)));
            NpcMesh.Part(hand, "Thumb", NpcMesh.Capsule(0.015f * S, 0.052f * S, 8), skin, new Vector3(0f, 0.004f * S, 0.025f * S), Vector3.one, new Vector3(150f, 0f, 0f), false);
        }

        static void Leg(Transform hip, out Transform knee, Look l, Material thigh, Material shin, Material bottom, int side)
        {
            float k = l.limb * S;
            float tl = Thigh * S * l.legF, sl = Shin * S * l.legF;
            NpcMesh.Part(hip, "Thigh", NpcMesh.Limb(0.078f * k, 0.06f * k, tl, 12), thigh, Vector3.zero, Vector3.one);
            if (l.lower == 0)   // shorts: a wider cloth sleeve over the upper thigh
                NpcMesh.Part(hip, "ShortsLeg", NpcMesh.Limb(0.092f * k, 0.082f * k, tl * 0.42f, 12), bottom, new Vector3(0f, 0.01f * S, 0f), Vector3.one);
            knee = NpcMesh.Node(hip, "Knee", new Vector3(0f, -tl, 0f));
            NpcMesh.Part(knee, "Ball", NpcMesh.Sphere(6, 10), shin, Vector3.zero, Vector3.one * (0.118f * k), default, false);
            NpcMesh.Part(knee, "Shin", NpcMesh.Limb(0.058f * k, 0.046f * k, sl - 0.03f * S, 12), shin, Vector3.zero, Vector3.one);
            var foot = NpcMesh.Node(knee, "Foot", new Vector3(0f, -sl + 0.045f * S, 0.03f * S));
            float r = 0.05f * k;
            Material skinM = M(l.skin, 0.1f);
            switch (l.shoes)
            {
                case 0:
                    NpcMesh.Part(foot, "Foot", NpcMesh.Sphere(8, 12), skinM, new Vector3(0f, -0.02f * S, 0.035f * S), new Vector3(r * 1.9f, r * 1.0f, r * 3.6f));
                    break;
                case 1:
                case 4:
                    NpcMesh.Part(foot, "Foot", NpcMesh.Sphere(8, 12), l.shoes == 4 ? M(side < 0 ? l.sock : l.sock2, 0.1f) : skinM, new Vector3(0f, -0.016f * S, 0.035f * S), new Vector3(r * 1.9f, r * 1.0f, r * 3.6f));
                    NpcMesh.Part(foot, "Sole", NpcMesh.RoundBox(r * 2.1f, 0.022f * S, r * 3.9f, 0.6f, 5), M(l.feet, 0.1f), new Vector3(0f, -0.045f * S, 0.035f * S), Vector3.one);
                    NpcMesh.Part(foot, "Strap", NpcMesh.Torus(r * 0.85f, 0.009f * S, 12, 4), M(l.feet, 0.1f), new Vector3(0f, -0.02f * S, 0.06f * S), new Vector3(1.1f, 1f, 0.7f), default, false);
                    break;
                case 3:
                    NpcMesh.Part(foot, "Boot", NpcMesh.RoundBox(r * 2.3f, 0.2f * S, r * 3.6f, 0.5f, 5), M(l.feet, 0.2f), new Vector3(0f, 0.035f * S, 0.025f * S), Vector3.one);
                    break;
                default:
                    NpcMesh.Part(foot, "Shoe", NpcMesh.RoundBox(r * 2.2f, 0.085f * S, r * 3.9f, 0.55f, 5), M(l.feet, 0.15f), new Vector3(0f, -0.022f * S, 0.04f * S), Vector3.one);
                    break;
            }
        }

        // ------------------------------------------------------------------ face
        static void Ears(NpcBody b, Material skin)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float y = 1.69f;
                Vector3 p = new Vector3(s * (HeadR(y) - 0.006f * S), Hd(y), -0.005f * S);
                NpcMesh.Part(b.head, s < 0 ? "EarL" : "EarR", NpcMesh.Sphere(6, 10), skin, p, new Vector3(0.022f, 0.05f, 0.036f) * S, default, false);
            }
        }

        static void Face(NpcBody b, Look l)
        {
            float eyeR = EyeR * S * l.eyeScale;
            b.eyeR = eyeR;
            Mesh sph = NpcMesh.Sphere(10, 16);
            Material white = M(EyeWhite, 0.35f), ink = M(Ink, 0.4f), hl = M(Color.white, 0.6f);
            for (int s = -1; s <= 1; s += 2)
            {
                // the eye node is squashed vertically to blink (cartoon blink, NpcActor)
                Vector3 c = HeadPt(s * EyeX * S, EyeY, -eyeR * 0.45f);
                var eye = NpcMesh.Node(b.head, s < 0 ? "EyeL" : "EyeR", c);
                NpcMesh.Part(eye, "White", sph, white, Vector3.zero, Vector3.one * eyeR * 2f, default, false);
                float yaw = l.crossEyed ? -s * 14f : s * 3f, pitch = l.crossEyed ? 6f : -4f;
                var pivot = NpcMesh.Node(eye, "PupilPivot", Vector3.zero, new Vector3(pitch, yaw, 0f));
                float pr = eyeR * 0.52f;
                NpcMesh.Part(pivot, "Pupil", NpcMesh.Sphere(8, 12), ink, new Vector3(0f, 0f, eyeR - pr * 0.35f), new Vector3(pr * 2f, pr * 2f, pr * 0.8f), default, false);
                NpcMesh.Part(pivot, "Shine", NpcMesh.Sphere(6, 8), hl, new Vector3(pr * 0.35f, pr * 0.4f, eyeR + 0.001f), Vector3.one * eyeR * 0.34f, default, false);
                if (s < 0) { b.pupilL = pivot; b.lidL = eye; } else { b.pupilR = pivot; b.lidR = eye; }
                if (l.brows > 0)
                {
                    Vector3 bp = HeadPt(s * EyeX * S, BrowY * (l.eyeScale > 1.05f ? 1.004f : 1f), -0.002f * S);
                    var brow = NpcMesh.Node(b.head, s < 0 ? "BrowL" : "BrowR", bp, new Vector3(-12f, s * -14f, s * -6f));
                    float w = (l.brows == 1 ? 0.07f : 0.055f) * S, th = (l.brows == 1 ? 0.016f : 0.008f) * S;
                    NpcMesh.Part(brow, "Brow", NpcMesh.Arc("r3brow" + l.brows + "_" + Mathf.RoundToInt(S * 1000f), w, th, -0.006f * S, 0.009f * S), M(l.browColor, 0.1f),
                        Vector3.zero, Vector3.one, new Vector3(0f, 180f, 0f), false);
                    if (s < 0) b.browL = brow; else b.browR = brow;
                }
            }
            // nose
            if (l.noseR > 0f)
                NpcMesh.Part(b.head, "Nose", NpcMesh.Sphere(8, 12), M(l.nose, 0.16f), HeadPt(0f, NoseY, l.noseR * S * 0.3f), new Vector3(1.6f, 2f, 1.9f) * (l.noseR * S));
            // mouth: closed shape + open shape (toggled by the talk animation)
            b.mouth = NpcMesh.Node(b.head, "Mouth", HeadPt(0f, MouthY, -0.002f * S), new Vector3(-12f, 180f, 0f));
            Material mm = M(MouthC, 0.3f);
            float ms = S;
            Mesh shape;
            switch (l.mouth)
            {
                case 1: shape = NpcMesh.Arc("r3grin_" + Mathf.RoundToInt(ms * 1000f), 0.055f * ms, 0.018f * ms, 0.013f * ms, 0.006f * ms); break;
                case 2: shape = NpcMesh.Arc("r3flat_" + Mathf.RoundToInt(ms * 1000f), 0.04f * ms, 0.008f * ms, 0.002f * ms, 0.006f * ms); break;
                case 3: shape = NpcMesh.Plate("r3mouth_o_" + Mathf.RoundToInt(ms * 1000f), NpcMesh.Ellipse(0.014f * ms, 0.017f * ms, 14), 0.006f * ms); break;
                default: shape = NpcMesh.Arc("r3smile_" + Mathf.RoundToInt(ms * 1000f), 0.046f * ms, 0.011f * ms, 0.011f * ms, 0.006f * ms); break;
            }
            NpcMesh.Part(b.mouth, "Closed", shape, mm, Vector3.zero, Vector3.one, default, false);
            b.mouthOpen = NpcMesh.Node(b.mouth, "Open", Vector3.zero);
            NpcMesh.Part(b.mouthOpen, "OpenShape", NpcMesh.Plate("r3mouth_open_" + Mathf.RoundToInt(ms * 1000f), NpcMesh.Ellipse(0.02f * ms, 0.015f * ms, 14), 0.006f * ms), mm, new Vector3(0f, -0.004f * ms, -0.001f), Vector3.one, default, false);
            NpcMesh.Part(b.mouthOpen, "Tongue", NpcMesh.Plate("r3tongue_" + Mathf.RoundToInt(ms * 1000f), NpcMesh.Ellipse(0.011f * ms, 0.006f * ms, 10), 0.004f * ms), M(H("#D9534F"), 0.3f), new Vector3(0f, -0.01f * ms, -0.003f), Vector3.one, default, false);
            b.mouthOpen.localScale = new Vector3(1f, 0.01f, 1f);
        }

        /// A cap of hair over the top/back of the head (dome scaled to the egg head).
        static void HairCap(NpcBody b, Material hm, float fromY, float extraBack = 0f, float tilt = -14f)
        {
            float r = HeadR(1.69f) * 1.075f;
            float h = (Crown - fromY) * S * 1.12f;
            NpcMesh.Part(b.head, "Hair", NpcMesh.Dome(10, 24), hm, new Vector3(0f, Hd(fromY), -0.012f * S - extraBack), new Vector3(r * 2f, h * 2f, r * 2.02f), new Vector3(tilt, 0f, 0f));
        }

        static void Hair(NpcBody b, Look l)
        {
            if (l.hairStyle == 0) return;
            Material hm = M(l.hair, 0.18f);
            float back = HeadR(1.65f);
            switch (l.hairStyle)
            {
                case 1: // short
                    HairCap(b, hm, 1.72f);
                    break;
                case 2: // bun
                    HairCap(b, hm, 1.72f);
                    NpcMesh.Part(b.head, "Bun", NpcMesh.Sphere(10, 14), hm, new Vector3(0f, Hd(1.82f), -back * 0.85f), Vector3.one * 0.085f * S);
                    break;
                case 3: // man bun on top
                    HairCap(b, hm, 1.73f, 0f, -8f);
                    NpcMesh.Part(b.head, "Bun", NpcMesh.Sphere(10, 14), hm, new Vector3(0f, Hd(1.875f), -0.03f * S), Vector3.one * 0.07f * S);
                    break;
                case 4: // shoulder length
                    HairCap(b, hm, 1.7f);
                    NpcMesh.Part(b.head, "Back", NpcMesh.Capsule(0.105f * S, 0.27f * S, 16), hm, new Vector3(0f, Hd(1.5f), -back * 0.55f), new Vector3(1.12f, 1f, 0.6f));
                    break;
                case 5: // ring around a bald top
                    NpcMesh.Part(b.head, "Ring", NpcMesh.Torus(HeadR(1.7f) * 0.98f, 0.024f * S, 20, 6), hm, new Vector3(0f, Hd(1.7f), -0.02f * S), new Vector3(1f, 1f, 1f), new Vector3(-16f, 0f, 0f));
                    break;
                case 6: // ponytail
                    HairCap(b, hm, 1.71f);
                    NpcMesh.Part(b.head, "Tie", NpcMesh.Sphere(8, 10), hm, new Vector3(0f, Hd(1.66f), -back - 0.01f * S), Vector3.one * 0.05f * S);
                    NpcMesh.Part(b.head, "Tail", NpcMesh.Capsule(0.028f * S, 0.24f * S, 10), hm, new Vector3(0f, Hd(1.66f), -back - 0.03f * S), Vector3.one, new Vector3(165f, 0f, 0f));
                    break;
                case 7: // long braid down the back (the leader)
                    HairCap(b, hm, 1.7f);
                    var braid = new[] { new Vector3(0f, Hd(1.66f), -back), new Vector3(0f, Hd(1.52f), -back - 0.02f * S), new Vector3(0.01f * S, Hd(1.36f), -0.16f * S), new Vector3(0.02f * S, Hd(1.18f), -0.17f * S) };
                    NpcMesh.Part(b.head, "Braid", NpcMesh.Sweep("r3braid_" + Mathf.RoundToInt(S * 1000f), braid, 0.032f * S, 0.02f * S, 8), hm, Vector3.zero, Vector3.one, default, false);
                    break;
                case 8: // wild long hair (the hunter)
                    HairCap(b, hm, 1.69f, 0.004f * S, -10f);
                    NpcMesh.Part(b.head, "Back", NpcMesh.Capsule(0.12f * S, 0.3f * S, 14), hm, new Vector3(0f, Hd(1.48f), -back * 0.6f), new Vector3(1.2f, 1f, 0.65f));
                    for (int s = -1; s <= 1; s += 2)
                        NpcMesh.Part(b.head, "Strand", NpcMesh.Capsule(0.03f * S, 0.22f * S, 8), hm, new Vector3(s * HeadR(1.62f) * 0.95f, Hd(1.5f), -0.02f * S), Vector3.one, new Vector3(0f, 0f, s * 6f), false);
                    break;
            }
        }

        static void Beard(NpcBody b, Look l)
        {
            if (l.beard == 0) return;
            Color bc = l.beard == 3 ? H("#F4EFE6") : l.beard == 2 ? Color.Lerp(l.skin, l.hair, 0.45f) : l.hair;
            Material m = M(bc, 0.14f);
            switch (l.beard)
            {
                case 1: // full beard
                    NpcMesh.Part(b.head, "Beard", NpcMesh.Sphere(10, 16), m, HeadPt(0f, 1.575f, -0.028f * S), new Vector3(0.2f, 0.17f, 0.16f) * S);
                    NpcMesh.Part(b.head, "Moustache", NpcMesh.Capsule(0.014f * S, 0.075f * S, 8), m, HeadPt(-0.0375f * S, MouthY + 0.017f, 0.004f * S), Vector3.one, new Vector3(0f, 0f, -90f), false);
                    break;
                case 2: // stubble
                    NpcMesh.Part(b.head, "Stubble", NpcMesh.Dome(8, 18), m, new Vector3(0f, Hd(1.6f), 0.02f * S), new Vector3(0.2f, 0.12f, 0.2f) * S, new Vector3(150f, 0f, 0f), false);
                    break;
                case 3: // Toni's long white beard down to the belly
                {
                    var beard = NpcMesh.Node(b.head, "Beard", HeadPt(0f, 1.6f, -0.03f * S));
                    b.moustache = beard;
                    NpcMesh.Part(beard, "Beard", NpcMesh.Capsule(0.085f * S, 0.36f * S, 16), m, new Vector3(0f, -0.33f * S, 0.03f * S), new Vector3(1.1f, 1f, 0.6f), default);
                    NpcMesh.Part(beard, "Moustache", NpcMesh.Capsule(0.02f * S, 0.12f * S, 10), m, new Vector3(-0.06f * S, 0.02f * S, 0.035f * S), Vector3.one, new Vector3(0f, 0f, -90f), false);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------ accessories per character
        static void Accessories(string id, NpcBody b, Look l)
        {
            switch (id)
            {
                case NpcDatabase.Horst: Horst(b, l); break;
                case NpcDatabase.Wanda: Wanda(b, l); break;
                case NpcDatabase.Fin: Fin(b, l); break;
                case NpcDatabase.Chad: Bottom(b, l, true); break;
                case NpcDatabase.Brad: Bottom(b, l, false); break;
                case NpcDatabase.Toni: Toni(b, l); break;
                case NpcDatabase.HLeader: HermitLeader(b, l); break;
                case NpcDatabase.HSmith: HermitSmith(b, l); break;
                case NpcDatabase.HHunter: HermitHunter(b, l); break;
                case NpcDatabase.HCook: HermitCook(b, l); break;
                case NpcDatabase.HLookout: HermitLookout(b, l); break;
                case NpcDatabase.HGardener: HermitGardener(b, l); break;
            }
        }

        /// A flat decal on the front (or back) of the torso at character height y, sideways x.
        static Transform OnChest(NpcBody b, string name, float x, float y, bool back = false)
        {
            float z = TorsoZ(y, x) + 0.003f * S;
            float yaw = Mathf.Atan2(x, Mathf.Max(0.01f, z)) * Mathf.Rad2Deg;
            return NpcMesh.Node(b.pivot, name, new Vector3(back ? -x : x, P(y), back ? -z : z), new Vector3(0f, back ? -yaw : 180f + yaw, 0f));
        }

        static void Glasses(NpcBody b, Color frame, Color lens, bool pushedUp, float scale)
        {
            var g = NpcMesh.Node(b.head, "Glasses", Vector3.zero);
            float y = pushedUp ? 1.79f : EyeY;
            float r = b.eyeR * 1.2f * scale;
            Material fm = M(frame, 0.45f), lm = NpcMesh.Glass(new Color(lens.r, lens.g, lens.b, 0.62f));
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 c = HeadPt(s * EyeX * S, y, pushedUp ? 0.006f * S : b.eyeR * 0.75f);
                NpcMesh.Part(g, "Rim", NpcMesh.Torus(r, 0.005f * S, 18, 5), fm, c, Vector3.one, new Vector3(90f + (pushedUp ? -35f : 0f), 0f, 0f), false);
                NpcMesh.Part(g, "Lens", NpcMesh.Plate("r3lens", NpcMesh.Ellipse(1f, 1f, 18), 0.02f), lm, c, new Vector3(r, r, 0.3f), new Vector3(pushedUp ? -35f : 0f, 0f, 0f), false);
                float hr = HeadR(y);
                var arm = new[] { c + new Vector3(s * r, 0f, 0f), new Vector3(s * hr * 0.92f, Hd(y) + 0.004f * S, c.z * 0.4f), new Vector3(s * hr * 0.99f, Hd(y) - 0.008f * S, -hr * 0.12f) };
                NpcMesh.Part(g, "Arm", NpcMesh.Sweep("r3glarm_" + s + "_" + Mathf.RoundToInt(S * 1000f) + (pushedUp ? "u" : "d") + Mathf.RoundToInt(scale * 100f), arm, 0.004f * S, 0.004f * S, 6), fm, Vector3.zero, Vector3.one, default, false);
            }
            Vector3 bl = HeadPt(-EyeX * S + r, y, pushedUp ? 0.006f * S : b.eyeR * 0.75f), br = HeadPt(EyeX * S - r, y, pushedUp ? 0.006f * S : b.eyeR * 0.75f);
            NpcMesh.Part(g, "Bridge", NpcMesh.Capsule(0.004f * S, Mathf.Max(0.01f, br.x - bl.x + 0.006f * S), 6), fm, bl + new Vector3(-0.003f * S, 0.005f * S, 0.004f * S), Vector3.one, new Vector3(0f, 0f, -90f), false);
        }

        /// A brimmed hat (bucket / sun hat) on the egg head.
        static Transform BrimHat(NpcBody b, string key, Color c, float brim, float crownH, float atY, float tilt = -6f)
        {
            float hr = HeadR(atY) * 1.04f;
            var hat = NpcMesh.Node(b.head, "Hat", new Vector3(0f, Hd(atY), -0.006f * S), new Vector3(tilt, 0f, 0f));
            b.hat = hat;
            float ch = crownH * S;
            var prof = NpcMesh.Smooth(new[] { new Vector2(hr * brim, -0.02f * S), new Vector2(hr * (brim - 0.12f), 0f), new Vector2(hr * 1.02f, 0.015f * S), new Vector2(hr * 0.98f, ch * 0.75f), new Vector2(hr * 0.7f, ch * 0.97f), new Vector2(0f, ch) }, 2);
            NpcMesh.Part(hat, "Crown", NpcMesh.Cached("r3hat_" + key + "_" + Mathf.RoundToInt(S * 1000f), () => NpcMesh.Lathe(prof, Segs)), M(c, 0.1f), Vector3.zero, Vector3.one);
            return hat;
        }

        // ---- Horst: pineapple shirt, walrus moustache, bucket hat with lures, sunglasses pushed up, whistle, fish roll
        static void Horst(NpcBody b, Look l)
        {
            Material pine = M(H("#F2C94C"), 0.14f), leaf = M(H("#6E9445"), 0.14f);
            float[,] spots = { { 0.08f, 1.30f }, { -0.1f, 1.2f }, { 0.04f, 1.1f }, { -0.06f, 1.37f }, { 0.12f, 1.17f } };
            for (int i = 0; i < spots.GetLength(0); i++)
                for (int side = 0; side < 2; side++)
                {
                    var p = OnChest(b, "Pineapple", spots[i, 0] * S, spots[i, 1], side == 1);
                    NpcMesh.Part(p, "Fruit", NpcMesh.Plate("r3pine_body", NpcMesh.Ellipse(0.016f, 0.024f, 10), 0.004f), pine, Vector3.zero, Vector3.one * S, default, false);
                    NpcMesh.Part(p, "Crown", NpcMesh.Plate("r3pine_crown", new[] { new Vector2(-0.013f, 0f), new Vector2(0.013f, 0f), new Vector2(0.007f, 0.018f), new Vector2(0f, 0.027f), new Vector2(-0.007f, 0.018f) }, 0.004f),
                        leaf, new Vector3(0f, 0.021f * S, 0f), Vector3.one * S, default, false);
                }
            // walrus moustache: 3 tufts per side
            var mo = NpcMesh.Node(b.head, "Moustache", HeadPt(0f, MouthY + 0.03f, 0.008f * S));
            b.moustache = mo;
            Material mm = M(H("#E07B39"), 0.2f);
            for (int s = -1; s <= 1; s += 2)
                for (int t = 0; t < 3; t++)
                {
                    float k = S;
                    var path = new[]
                    {
                        new Vector3(0.006f * s, 0.003f, 0f) * k, new Vector3(0.045f * s, -0.006f - t * 0.007f, -0.012f) * k,
                        new Vector3(0.09f * s, -0.024f - t * 0.012f, -0.03f) * k, new Vector3(0.13f * s, -0.048f - t * 0.018f, -0.045f) * k
                    };
                    NpcMesh.Part(mo, "Tuft", NpcMesh.Sweep("r3moust_" + s + "_" + t + "_" + Mathf.RoundToInt(S * 1000f), path, (0.018f - t * 0.0025f) * S, 0.008f * S, 8), mm, new Vector3(0f, -t * 0.004f * S, -t * 0.004f * S), Vector3.one, default, false);
                }
            Glasses(b, H("#1E1B2B"), H("#3FA9B5"), true, 1.0f);
            // bucket hat with three lures
            var hat = BrimHat(b, "horst", H("#C9B48A"), 1.55f, 0.13f, 1.775f, -6f);
            NpcMesh.Part(hat, "Band", NpcMesh.Torus(HeadR(1.775f) * 1.06f, 0.008f * S, Segs, 5), M(H("#7A4448"), 0.1f), new Vector3(0f, 0.03f * S, 0f), Vector3.one, default, false);
            Color[] lures = { H("#F2C94C"), H("#E0607E"), H("#3FB8C4") };
            for (int i = 0; i < 3; i++)
            {
                float a = (200f + i * 55f) * Mathf.Deg2Rad;
                float rr = HeadR(1.775f) * 1.45f;
                var lp = NpcMesh.Node(hat, "Lure", new Vector3(Mathf.Cos(a) * rr, -0.02f * S, Mathf.Sin(a) * rr));
                NpcMesh.Part(lp, "Line", NpcMesh.Cylinder(0.002f * S, 0.04f * S, 6), M(H("#EDE6DA"), 0.3f), new Vector3(0f, -0.04f * S, 0f), Vector3.one, default, false);
                NpcMesh.Part(lp, "Bait", NpcMesh.Sphere(6, 8), M(lures[i], 0.4f), new Vector3(0f, -0.052f * S, 0f), new Vector3(0.014f, 0.026f, 0.014f) * S, default, false);
            }
            // whistle on an orange lanyard
            NpcMesh.Part(b.pivot, "Lanyard", NpcMesh.Torus(0.075f * S, 0.005f * S, 18, 4), M(H("#EE7F2D"), 0.2f), new Vector3(0f, P(1.47f), 0.01f * S), new Vector3(1f, 1f, 1.2f), new Vector3(30f, 0f, 0f), false);
            var w = OnChest(b, "Whistle", 0f, 1.33f);
            NpcMesh.Part(w, "Body", NpcMesh.Capsule(0.009f * S, 0.034f * S, 8), M(H("#C9CED6"), 0.6f), new Vector3(0f, 0f, -0.008f * S), Vector3.one, new Vector3(90f, 0f, 0f), false);
            // fish roll in the right hand
            var sw = NpcMesh.Node(b.handR, "Sandwich", new Vector3(0f, -0.035f * S, 0.035f * S), new Vector3(0f, 90f, 0f));
            b.propR = sw;
            NpcMesh.Part(sw, "Bun", NpcMesh.Capsule(0.02f * S, 0.09f * S, 10), M(H("#D9A35E"), 0.15f), new Vector3(0f, 0f, -0.045f * S), Vector3.one, new Vector3(90f, 0f, 0f));
            NpcMesh.Part(sw, "Fish", NpcMesh.Capsule(0.011f * S, 0.11f * S, 8), M(H("#9FB4C4"), 0.3f), new Vector3(0f, 0.007f * S, -0.055f * S), Vector3.one, new Vector3(90f, 0f, 0f), false);
        }

        // ---- Wanda: aviator cap + goggles, scarf, patches on the flight suit, belt
        static void Wanda(NpcBody b, Look l)
        {
            var cap = NpcMesh.Node(b.head, "Cap", new Vector3(0f, Hd(1.7f), -0.006f * S));
            b.hat = cap;
            float hr = HeadR(1.69f) * 1.08f;
            NpcMesh.Part(cap, "Leather", NpcMesh.Dome(10, Segs), M(H("#6B4636"), 0.25f), Vector3.zero, new Vector3(hr * 2f, (Crown - 1.7f) * S * 2.25f, hr * 2.02f), new Vector3(-10f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(cap, "Flap", NpcMesh.RoundBox(0.016f * S, 0.085f * S, 0.06f * S, 0.8f, 5), M(H("#6B4636"), 0.25f), new Vector3(s * hr * 0.97f, -0.05f * S, -0.012f * S), Vector3.one, new Vector3(0f, 0f, s * 6f));
            var gog = NpcMesh.Node(cap, "Goggles", new Vector3(0f, 0.075f * S, hr * 0.78f), new Vector3(-30f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                NpcMesh.Part(gog, "Cup", NpcMesh.Cylinder(0.026f * S, 0.02f * S, 14), M(H("#C9CED6"), 0.6f), new Vector3(s * 0.036f * S, 0f, 0f), Vector3.one, new Vector3(90f, 0f, 0f), false);
                NpcMesh.Part(gog, "Lens", NpcMesh.Cylinder(0.021f * S, 0.003f * S, 14), NpcMesh.Glass(new Color(0.62f, 0.89f, 0.93f, 0.6f)), new Vector3(s * 0.036f * S, 0f, 0.021f * S), Vector3.one, new Vector3(90f, 0f, 0f), false);
            }
            NpcMesh.Part(b.pivot, "Scarf", NpcMesh.Torus(0.07f * S, 0.024f * S, 18, 6), M(H("#3FB8C4"), 0.15f), new Vector3(0f, P(1.47f), 0f), Vector3.one, default, false);
            var se = OnChest(b, "ScarfEnd", 0.05f * S, 1.36f);
            NpcMesh.Part(se, "Cloth", NpcMesh.RoundBox(0.045f * S, 0.13f * S, 0.016f * S, 0.6f, 5), M(H("#3FB8C4"), 0.15f), new Vector3(0f, -0.02f * S, -0.01f * S), Vector3.one, new Vector3(-6f, 0f, 10f), false);
            Patch(b, -0.08f * S, 1.3f, H("#F2C94C"), 0.022f);
            Patch(b, 0.09f * S, 1.18f, H("#3FB8C4"), 0.02f);
            NpcMesh.Part(b.pivot, "Belt", NpcMesh.Torus(TorsoR(1.06f) * 1.02f, 0.014f * S, Segs, 5), M(H("#5E4436"), 0.2f), new Vector3(0f, P(1.06f), 0f), new Vector3(1f, 1f, Depth * 1.03f), default, false);
        }

        static void Patch(NpcBody b, float x, float y, Color c, float r)
        {
            var p = OnChest(b, "Patch", x, y);
            NpcMesh.Part(p, "Cloth", NpcMesh.Plate("r3patch", NpcMesh.Ellipse(1f, 1f, 14), 0.1f), M(c, 0.15f), Vector3.zero, new Vector3(r * S, r * S, 0.03f * S), default, false);
        }

        // ---- Dr. Fin: lab coat over the wetsuit, huge round glasses, pencil, clipboard
        static void Fin(NpcBody b, Look l)
        {
            var coat = NpcMesh.Smooth(new[]
            {
                new Vector2(TorsoR(0.86f) * 1.14f, P(0.55f)), new Vector2(TorsoR(0.9f) * 1.12f, P(0.86f)), new Vector2(TorsoR(1.06f) * 1.1f, P(1.06f)),
                new Vector2(TorsoR(1.25f) * 1.09f, P(1.25f)), new Vector2(TorsoR(1.38f) * 1.08f, P(1.38f)), new Vector2(0.075f * S, P(1.49f))
            }, 3);
            var coatMesh = NpcMesh.Cached("r3fin_coat_" + Mathf.RoundToInt(S * 1000f), () => NpcMesh.Lathe(coat, Segs, 2, (i, j) =>
            {
                float ang = (j + 0.5f) / Segs * 360f;
                return Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 22f ? 1 : 0;
            }));
            var cr = NpcMesh.Part(b.pivot, "Coat", coatMesh, M(H("#EDE6DA"), 0.15f), Vector3.zero, new Vector3(1f, 1f, Depth * 1.05f));
            cr.sharedMaterials = new[] { M(H("#EDE6DA"), 0.15f), M(H("#343849"), 0.18f) };
            Glasses(b, H("#1E1B2B"), H("#9FE4EC"), false, 1.3f);
            NpcMesh.Part(b.head, "Pencil", NpcMesh.Capsule(0.005f * S, 0.09f * S, 6), M(H("#F2C94C"), 0.2f), new Vector3(HeadR(1.68f) * 1.02f, Hd(1.7f), -0.012f * S), Vector3.one, new Vector3(-70f, 0f, 20f), false);
            var cb = NpcMesh.Node(b.handL, "Clipboard", new Vector3(0f, -0.04f * S, 0.045f * S), new Vector3(-70f, 0f, 0f));
            b.propL = cb;
            NpcMesh.Part(cb, "Board", NpcMesh.RoundBox(0.15f * S, 0.2f * S, 0.011f * S, 0.3f, 5), M(H("#3FA9B5"), 0.3f), Vector3.zero, Vector3.one);
            NpcMesh.Part(cb, "Paper", NpcMesh.RoundBox(0.13f * S, 0.16f * S, 0.003f * S, 0.1f, 4), M(H("#F4EFE6"), 0.1f), new Vector3(0f, -0.01f * S, 0.007f * S), Vector3.one, default, false);
        }

        // ---- Chad & Brad: gold chain, mirrored glasses, epaulettes; Chad a backwards cap; selfie stick
        static void Bottom(NpcBody b, Look l, bool chad)
        {
            NpcMesh.Part(b.pivot, "Chain", NpcMesh.Torus(0.08f * S, 0.007f * S, 18, 5), M(Gold, 0.8f), new Vector3(0f, P(1.46f), 0.02f * S), new Vector3(1f, 1f, 1.25f), new Vector3(32f, 0f, 0f), false);
            Glasses(b, Gold, H("#9FB4D8"), false, 1.0f);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(b.pivot, "Epaulette", NpcMesh.RoundBox(0.07f * S, 0.016f * S, 0.055f * S, 0.6f, 4), M(Gold, 0.6f), new Vector3(s * ShoulderX * S * l.shoulder * 0.95f, P(ShoulderH) + 0.05f * S, 0f), Vector3.one, default, false);
            NpcMesh.Part(b.pivot, "Belt", NpcMesh.Torus(TorsoR(1.06f) * 1.02f, 0.012f * S, Segs, 5), M(Gold, 0.6f), new Vector3(0f, P(1.06f), 0f), new Vector3(1f, 1f, Depth * 1.03f), default, false);
            if (chad) Cap(b, H("#F4EFE6"), true);
            var stick = NpcMesh.Node(b.handR, "SelfieStick", new Vector3(0f, -0.035f * S, 0.02f * S), new Vector3(-20f, 0f, 0f));
            b.propR = stick;
            NpcMesh.Part(stick, "Pole", NpcMesh.Cylinder(0.006f * S, 0.45f * S, 8), M(H("#2E3140"), 0.5f), new Vector3(0f, -0.06f * S, 0f), Vector3.one, new Vector3(-90f, 0f, 0f), false);
            NpcMesh.Part(stick, "Phone", NpcMesh.RoundBox(0.05f * S, 0.09f * S, 0.008f * S, 0.4f, 4), M(H("#1E1B2B"), 0.6f), new Vector3(0f, -0.06f * S, 0.47f * S), Vector3.one, new Vector3(-90f, 0f, 0f), false);
        }

        static void Cap(NpcBody b, Color c, bool backwards)
        {
            var cap = NpcMesh.Node(b.head, "Cap", new Vector3(0f, Hd(1.73f), -0.004f * S), new Vector3(0f, backwards ? 180f : 0f, 0f));
            b.hat = cap;
            float hr = HeadR(1.72f) * 1.07f;
            NpcMesh.Part(cap, "Crown", NpcMesh.Dome(10, Segs), M(c, 0.15f), Vector3.zero, new Vector3(hr * 2f, (Crown - 1.73f) * S * 2.3f, hr * 2.02f));
            NpcMesh.Part(cap, "Visor", NpcMesh.Plate("r3visor_" + Mathf.RoundToInt(S * 1000f), new[] { new Vector2(-0.075f * S, 0f), new Vector2(0.075f * S, 0f), new Vector2(0.06f * S, 0.07f * S), new Vector2(0f, 0.09f * S), new Vector2(-0.06f * S, 0.07f * S) }, 0.008f * S),
                M(c * 0.9f, 0.15f), new Vector3(0f, 0.006f * S, hr * 0.92f), Vector3.one, new Vector3(80f, 0f, 0f), false);
        }

        // ---- Toni: coconut-shell headphones, patches, a cassette recorder on a strap (beard: Look.beard 3)
        static void Toni(NpcBody b, Look l)
        {
            float hr = HeadR(1.7f);
            NpcMesh.Part(b.head, "Band", NpcMesh.Torus(hr * 1.04f, 0.008f * S, Segs, 5), M(H("#8E6C50"), 0.2f), new Vector3(0f, Hd(1.7f), 0f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(b.head, "Shell", NpcMesh.Dome(8, 14), M(H("#6B4A36"), 0.2f), new Vector3(s * hr * 1.0f, Hd(1.69f), 0f), Vector3.one * 0.085f * S, new Vector3(0f, 0f, -s * 90f));
            Patch(b, -0.07f * S, 1.24f, H("#F2C94C"), 0.026f);
            Patch(b, 0.1f * S, 1.12f, H("#E0607E"), 0.022f);
            var strap = new[] { new Vector3(-0.07f * S, P(1.47f), 0f), new Vector3(0.03f * S, P(1.25f), TorsoZ(1.25f, 0.03f * S) + 0.012f * S), new Vector3(0.16f * S, P(1.04f), 0.04f * S) };
            NpcMesh.Part(b.pivot, "Strap", NpcMesh.Sweep("r3toni_strap_" + Mathf.RoundToInt(S * 1000f), strap, 0.006f * S, 0.006f * S, 5), M(H("#5E4436"), 0.2f), Vector3.zero, Vector3.one, default, false);
            var rec = NpcMesh.Node(b.pivot, "Recorder", new Vector3(TorsoR(1.0f) + 0.03f * S, P(1.0f), 0.04f * S), new Vector3(0f, 80f, 0f));
            b.extra = rec;
            NpcMesh.Part(rec, "Box", NpcMesh.RoundBox(0.13f * S, 0.08f * S, 0.04f * S, 0.35f, 5), M(H("#2E3140"), 0.35f), Vector3.zero, Vector3.one);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(rec, "Reel", NpcMesh.Cylinder(0.015f * S, 0.004f * S, 12), M(H("#F4EFE6"), 0.3f), new Vector3(s * 0.03f * S, 0.006f * S, 0.021f * S), Vector3.one, new Vector3(90f, 0f, 0f), false);
        }

        // ================================================================== the hermits (round 3)
        /// Patched sailcloth poncho over the shoulders (an open shell down to the hips).
        static void Poncho(NpcBody b, string key, Color c, Color patch, float down)
        {
            var prof = NpcMesh.Smooth(new[]
            {
                new Vector2(TorsoR(down) * 1.32f, P(down)), new Vector2(TorsoR(1.15f) * 1.22f, P(1.15f)), new Vector2(TorsoR(1.3f) * 1.2f, P(1.3f)),
                new Vector2(ShoulderX * S * cur.shoulder * 1.38f, P(1.42f)), new Vector2(0.1f * S, P(1.5f)), new Vector2(0.075f * S, P(1.5f))
            }, 3);
            var mesh = NpcMesh.Cached("r3poncho_" + key, () => NpcMesh.Lathe(prof, Segs, 2, (i, j) =>
            {
                float ang = (j + 0.5f) / Segs * 360f;
                return Mathf.Abs(Mathf.DeltaAngle(ang, 300f)) < 18f || Mathf.Abs(Mathf.DeltaAngle(ang, 140f)) < 14f ? 1 : 0;
            }));
            var r = NpcMesh.Part(b.pivot, "Poncho", mesh, M(c, 0.1f), Vector3.zero, new Vector3(1f, 1f, Depth * 1.1f));
            r.sharedMaterials = new[] { M(c, 0.1f), M(patch, 0.1f) };
        }

        static void ShellNecklace(NpcBody b)
        {
            Material cord = M(H("#8E6C50"), 0.2f), shell = M(H("#F4EFE6"), 0.4f), shell2 = M(H("#E8B4A0"), 0.4f);
            NpcMesh.Part(b.pivot, "Cord", NpcMesh.Torus(0.085f * S, 0.004f * S, 18, 4), cord, new Vector3(0f, P(1.455f), 0.02f * S), new Vector3(1f, 1f, 1.3f), new Vector3(34f, 0f, 0f), false);
            for (int i = 0; i < 5; i++)
            {
                float x = (i - 2) * 0.026f * S;
                float y = 1.405f - Mathf.Abs(i - 2) * 0.012f;
                var p = OnChest(b, "Shell", x, y);
                NpcMesh.Part(p, "Body", NpcMesh.Sphere(6, 8), i % 2 == 0 ? shell : shell2, Vector3.zero, new Vector3(0.018f, 0.022f, 0.01f) * S, default, false);
            }
        }

        /// A long stick / staff held upright in a hand.
        static Transform Staff(Transform hand, string key, float len, Color c)
        {
            var st = NpcMesh.Node(hand, "Staff", new Vector3(0f, -0.02f * S, 0.015f * S), new Vector3(-12f, 0f, 0f));
            NpcMesh.Part(st, "Pole", NpcMesh.Capsule(0.014f * S, len * S, 8), M(c, 0.15f), new Vector3(0f, -len * S * 0.45f, 0f), Vector3.one, default, false);
            return st;
        }

        // ---- the leader „Ebbe“: grey braid, patched sailcloth poncho, shell necklace, tide staff with a glass float
        static void HermitLeader(NpcBody b, Look l)
        {
            Poncho(b, "leader", H("#D9CBA8"), H("#B8865A"), 1.0f);
            ShellNecklace(b);
            var st = Staff(b.handR, "leader", 1.55f, H("#8E6C50"));
            b.propR = st;
            NpcMesh.Part(st, "Float", NpcMesh.Sphere(10, 14), NpcMesh.Glass(new Color(0.45f, 0.78f, 0.7f, 0.75f)), new Vector3(0f, 0.86f * S, 0f), Vector3.one * 0.075f * S);
            NpcMesh.Part(st, "Net", NpcMesh.Torus(0.038f * S, 0.004f * S, 12, 4), M(H("#C9B48A"), 0.1f), new Vector3(0f, 0.86f * S, 0f), Vector3.one, default, false);
            for (int i = 0; i < 4; i++)
                NpcMesh.Part(st, "Notch", NpcMesh.Torus(0.016f * S, 0.003f * S, 8, 3), M(H("#5E4436"), 0.1f), new Vector3(0f, (0.5f - i * 0.05f) * S, 0f), Vector3.one, default, false);
        }

        // ---- the smith „Amboss“: bald, red beard, leather apron, goggles on the forehead, boots
        static void HermitSmith(NpcBody b, Look l)
        {
            var ap = OnChest(b, "Apron", 0f, 1.12f);
            NpcMesh.Part(ap, "Leather", NpcMesh.RoundBox(TorsoR(1.15f) * 1.5f, 0.52f * S, 0.014f * S, 0.25f, 5), M(H("#7A4E2E"), 0.25f), new Vector3(0f, -0.07f * S, -0.015f * S), Vector3.one, new Vector3(-4f, 0f, 0f));
            NpcMesh.Part(b.pivot, "ApronStrap", NpcMesh.Torus(0.078f * S, 0.007f * S, 16, 4), M(H("#5E3A22"), 0.2f), new Vector3(0f, P(1.47f), 0f), Vector3.one, default, false);
            var gog = NpcMesh.Node(b.head, "Goggles", HeadPt(0f, 1.79f, 0.004f * S), new Vector3(-40f, 0f, 0f));
            NpcMesh.Part(gog, "Strap", NpcMesh.Torus(HeadR(1.79f) * 1.02f, 0.006f * S, 18, 4), M(H("#2E3140"), 0.3f), new Vector3(0f, 0f, -HeadR(1.79f)), Vector3.one, new Vector3(90f, 0f, 0f), false);
            for (int s = -1; s <= 1; s += 2)
            {
                NpcMesh.Part(gog, "Cup", NpcMesh.Cylinder(0.022f * S, 0.016f * S, 12), M(H("#5E5A4E"), 0.5f), new Vector3(s * 0.032f * S, 0f, 0f), Vector3.one, new Vector3(90f, 0f, 0f), false);
                NpcMesh.Part(gog, "Lens", NpcMesh.Cylinder(0.018f * S, 0.003f * S, 12), NpcMesh.Glass(new Color(0.3f, 0.36f, 0.3f, 0.7f)), new Vector3(s * 0.032f * S, 0f, 0.017f * S), Vector3.one, new Vector3(90f, 0f, 0f), false);
            }
            // a whetstone in the left hand
            var ws = NpcMesh.Node(b.handL, "Whetstone", new Vector3(0f, -0.035f * S, 0.03f * S));
            b.propL = ws;
            NpcMesh.Part(ws, "Stone", NpcMesh.RoundBox(0.09f * S, 0.03f * S, 0.04f * S, 0.4f, 4), M(H("#8A8C96"), 0.2f), Vector3.zero, Vector3.one);
        }

        // ---- the hunter „Harpo“: wild hair + headband, net over the shoulder, a long stick (he guards the gap)
        static void HermitHunter(NpcBody b, Look l)
        {
            NpcMesh.Part(b.head, "Headband", NpcMesh.Torus(HeadR(1.74f) * 1.04f, 0.011f * S, Segs, 5), M(H("#4F7FAE"), 0.15f), new Vector3(0f, Hd(1.74f), -0.004f * S), Vector3.one, new Vector3(-8f, 0f, 0f), false);
            float sy = 1.0f;
            var net = new[]
            {
                new Vector3(-0.13f * S, P(1.45f), 0.06f * S), new Vector3(-0.03f * S, P(1.3f), TorsoZ(1.3f, -0.03f * S) + 0.012f * S),
                new Vector3(0.1f * S, P(1.15f), TorsoZ(1.15f, 0.1f * S) + 0.01f * S), new Vector3(TorsoR(sy) + 0.01f * S, P(sy), 0.02f * S),
                new Vector3(0.1f * S, P(1.18f), -TorsoZ(1.18f, 0.1f * S) - 0.012f * S), new Vector3(-0.1f * S, P(1.42f), -0.08f * S)
            };
            NpcMesh.Part(b.pivot, "Net", NpcMesh.Sweep("r3hunter_net_" + Mathf.RoundToInt(S * 1000f), net, 0.028f * S, 0.02f * S, 8), M(H("#6E9445"), 0.1f), Vector3.zero, Vector3.one, default, false);
            for (int i = 1; i < net.Length - 1; i++)
                NpcMesh.Part(b.pivot, "Float", NpcMesh.Sphere(6, 8), M(H("#EE7F2D"), 0.3f), net[i] + new Vector3(0f, -0.02f * S, 0f), Vector3.one * 0.04f * S, default, false);
            var st = Staff(b.handR, "hunter", 1.5f, H("#9C7B55"));
            b.propR = st;
            NpcMesh.Part(st, "Tip", NpcMesh.Cone(0.016f * S, 0.07f * S, 6), M(H("#C9B48A"), 0.2f), new Vector3(0f, 0.78f * S, 0f), Vector3.one, default, false);
        }

        // ---- the cook „Topf“: a colander for a hat, stained apron, a ladle
        static void HermitCook(NpcBody b, Look l)
        {
            var hat = NpcMesh.Node(b.head, "Colander", new Vector3(0f, Hd(1.76f), -0.004f * S), new Vector3(-6f, 0f, 0f));
            b.hat = hat;
            float hr = HeadR(1.76f) * 1.12f;
            Material metal = M(H("#C9CED6"), 0.6f);
            NpcMesh.Part(hat, "Bowl", NpcMesh.Dome(8, Segs), metal, Vector3.zero, new Vector3(hr * 2f, 0.2f * S, hr * 2f));
            NpcMesh.Part(hat, "Rim", NpcMesh.Torus(hr, 0.007f * S, Segs, 4), metal, Vector3.zero, Vector3.one, default, false);
            Material hole = M(H("#2E3140"), 0.2f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * 36f * Mathf.Deg2Rad;
                float rr = hr * 0.62f;
                NpcMesh.Part(hat, "Hole", NpcMesh.Sphere(4, 6), hole, new Vector3(Mathf.Cos(a) * rr, 0.07f * S, Mathf.Sin(a) * rr), Vector3.one * 0.012f * S, default, false);
            }
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(hat, "Handle", NpcMesh.Torus(0.018f * S, 0.005f * S, 8, 4), metal, new Vector3(s * hr * 1.02f, 0.01f * S, 0f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            var ap = OnChest(b, "Apron", 0f, 1.08f);
            NpcMesh.Part(ap, "Cloth", NpcMesh.RoundBox(TorsoR(1.1f) * 1.45f, 0.42f * S, 0.012f * S, 0.3f, 5), M(H("#EDE6DA"), 0.12f), new Vector3(0f, -0.06f * S, -0.012f * S), Vector3.one, new Vector3(-6f, 0f, 0f));
            NpcMesh.Part(ap, "Stain", NpcMesh.Plate("r3stain", NpcMesh.Ellipse(1f, 0.7f, 10), 0.1f), M(H("#C98A4B"), 0.1f), new Vector3(0.03f * S, -0.12f * S, -0.02f * S), new Vector3(0.025f, 0.025f, 0.02f) * S, default, false);
            var ladle = NpcMesh.Node(b.handR, "Ladle", new Vector3(0f, -0.035f * S, 0.02f * S), new Vector3(70f, 0f, 0f));
            b.propR = ladle;
            NpcMesh.Part(ladle, "Handle", NpcMesh.Cylinder(0.005f * S, 0.2f * S, 6), metal, Vector3.zero, Vector3.one, default, false);
            NpcMesh.Part(ladle, "Bowl", NpcMesh.Dome(6, 12), metal, new Vector3(0f, 0.21f * S, 0.012f * S), Vector3.one * 0.055f * S, new Vector3(180f, 0f, 0f), false);
        }

        // ---- the lookout „Fernglas“: striped beanie, life vest, brass spyglass, mismatched socks
        static void HermitLookout(NpcBody b, Look l)
        {
            var hat = NpcMesh.Node(b.head, "Beanie", new Vector3(0f, Hd(1.72f), -0.008f * S), new Vector3(-10f, 0f, 0f));
            b.hat = hat;
            float hr = HeadR(1.71f) * 1.08f;
            NpcMesh.Part(hat, "Knit", NpcMesh.Dome(10, Segs), M(H("#C94F3D"), 0.1f), Vector3.zero, new Vector3(hr * 2f, (Crown - 1.72f) * S * 2.6f, hr * 2.02f));
            for (int i = 0; i < 2; i++)
                NpcMesh.Part(hat, "Stripe", NpcMesh.Torus(hr * (0.98f - i * 0.12f), 0.01f * S, Segs, 4), M(H("#F4EFE6"), 0.1f), new Vector3(0f, (0.03f + i * 0.05f) * S, 0f), Vector3.one, default, false);
            NpcMesh.Part(hat, "Pompom", NpcMesh.Sphere(8, 10), M(H("#F4EFE6"), 0.1f), new Vector3(0f, (Crown - 1.72f) * S * 1.3f, 0f), Vector3.one * 0.05f * S);
            // life vest: a thick orange shell around the chest
            var vest = NpcMesh.Smooth(new[]
            {
                new Vector2(TorsoR(1.08f) * 1.18f, P(1.08f)), new Vector2(TorsoR(1.2f) * 1.22f, P(1.2f)), new Vector2(TorsoR(1.33f) * 1.18f, P(1.33f)),
                new Vector2(ShoulderX * S * cur.shoulder * 0.95f, P(1.43f)), new Vector2(0.09f * S, P(1.47f))
            }, 3);
            var vm = NpcMesh.Cached("r3lookout_vest", () => NpcMesh.Lathe(vest, Segs, 2, (i, j) =>
            {
                float ang = (j + 0.5f) / Segs * 360f;
                return Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 12f ? 1 : 0;
            }));
            var vr = NpcMesh.Part(b.pivot, "LifeVest", vm, M(H("#EE7F2D"), 0.2f), Vector3.zero, new Vector3(1f, 1f, Depth * 1.2f));
            vr.sharedMaterials = new[] { M(H("#EE7F2D"), 0.2f), M(H("#F2C94C"), 0.2f) };
            var sg = NpcMesh.Node(b.handR, "Spyglass", new Vector3(0f, -0.035f * S, 0.03f * S), new Vector3(-80f, 0f, 0f));
            b.propR = sg;
            Material brass = M(H("#C9A13E"), 0.55f);
            NpcMesh.Part(sg, "Tube", NpcMesh.Cylinder(0.018f * S, 0.16f * S, 10, 0.022f * S), brass, Vector3.zero, Vector3.one, default, false);
            NpcMesh.Part(sg, "Tube2", NpcMesh.Cylinder(0.014f * S, 0.12f * S, 10), M(H("#8E6C50"), 0.3f), new Vector3(0f, 0.16f * S, 0f), Vector3.one, default, false);
        }

        // ---- the gardener „Wurzel“: straw hat with flowers, ponytail, gloves, a watering can
        static void HermitGardener(NpcBody b, Look l)
        {
            var hat = BrimHat(b, "gardener", H("#C9B48A"), 1.8f, 0.11f, 1.77f, -4f);
            NpcMesh.Part(hat, "Band", NpcMesh.Torus(HeadR(1.77f) * 1.05f, 0.008f * S, Segs, 5), M(H("#3FA9A0"), 0.12f), new Vector3(0f, 0.03f * S, 0f), Vector3.one, default, false);
            Color[] fl = { H("#E0607E"), H("#F2C94C"), H("#F4EFE6") };
            for (int i = 0; i < 3; i++)
            {
                float a = (60f + i * 22f) * Mathf.Deg2Rad;
                float rr = HeadR(1.77f) * 1.07f;
                NpcMesh.Part(hat, "Flower", NpcMesh.Sphere(6, 8), M(fl[i], 0.2f), new Vector3(Mathf.Cos(a) * rr, 0.035f * S, Mathf.Sin(a) * rr), Vector3.one * 0.03f * S, default, false);
            }
            Material glove = M(H("#6E9445"), 0.15f);
            for (int s = -1; s <= 1; s += 2)
            {
                var hand = s < 0 ? b.handL : b.handR;
                NpcMesh.Part(hand, "Glove", NpcMesh.Sphere(8, 10), glove, new Vector3(0f, -0.01f * S, 0.004f * S), new Vector3(0.088f, 0.115f, 0.064f) * S);
            }
            var can = NpcMesh.Node(b.handL, "WateringCan", new Vector3(0f, -0.06f * S, 0.03f * S));
            b.propL = can;
            Material tin = M(H("#8A9AA6"), 0.45f);
            NpcMesh.Part(can, "Body", NpcMesh.Cylinder(0.05f * S, 0.12f * S, 12), tin, new Vector3(0f, -0.13f * S, 0f), Vector3.one, default, false);
            NpcMesh.Part(can, "Spout", NpcMesh.Capsule(0.008f * S, 0.12f * S, 6), tin, new Vector3(0f, -0.1f * S, 0.04f * S), Vector3.one, new Vector3(50f, 0f, 0f), false);
        }
    }
}
