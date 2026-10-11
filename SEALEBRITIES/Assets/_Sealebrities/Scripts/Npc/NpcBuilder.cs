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

    /// Builds the How-to-Fish style bean NPCs (L.13: rounder, smoother – 32 radial segments, smooth normals, ball
    /// joints; the world stays faceted). Respect rules of L.8 apply to every villager: everyday clothes, varied skin
    /// tones from the shared palette, no costumes or symbols.
    public static class NpcBuilder
    {
        const int Segs = 32;

        struct Look
        {
            public float height, legLen, bottomR, waistR, chestR, shoulderR, neckR, headR, armR, legR, belly;
            public Color skin, top, bottom, sleeve, legs, feet, sock;
            public int sleeves;     // 0 tank, 1 short, 2 long
            public int lower;       // 0 shorts, 1 long trousers, 2 skirt, 3 one-piece (bottom = top colour)
            public int shoes;       // 0 bare, 1 sandals, 2 shoes, 3 boots, 4 socks + sandals
            public float eyeScale;
            public bool crossEyed;
            public Color hair;
            public int hairStyle;   // 0 none, 1 short, 2 bun, 3 man bun, 4 shoulder length, 5 white ring
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

        // ------------------------------------------------------------------ public
        public static GameObject Build(string id)
        {
            var look = LookFor(id);
            var root = new GameObject("NPC_" + id);
            var body = root.AddComponent<NpcBody>();
            BuildBean(root.transform, body, look, id);
            Accessories(id, body, look);
            // L.13: LOD1 / LOD2 proxies on the same bones (up to 18 NPCs, 12 in Altriff) – before the renderer list
            DiverLod.Build(root);
            body.renderers = root.GetComponentsInChildren<Renderer>(true);
            return root;
        }

        // ------------------------------------------------------------------ looks
        static Look Base(float height)
        {
            return new Look
            {
                height = height, legLen = 0.72f * height / 1.85f, bottomR = 0.27f, waistR = 0.27f, chestR = 0.255f, shoulderR = 0.235f,
                neckR = 0.225f, headR = 0.226f, armR = 0.058f, legR = 0.1f, belly = 0.01f,
                skin = H("#E8C08A"), top = H("#EDE6DA"), bottom = H("#4F7FAE"), sleeve = H("#EDE6DA"), legs = H("#E8C08A"), feet = H("#5E4436"),
                sock = H("#EDE6DA"), sleeves = 1, lower = 0, shoes = 2, eyeScale = 1f, hair = H("#3A2C2A"), hairStyle = 0,
                nose = H("#E8C08A"), noseR = 0.032f, lean = 0f, mouth = 0, brows = 2, browColor = H("#3A2C2A")
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
                    l.legLen = 0.55f; l.bottomR = 0.42f; l.waistR = 0.41f; l.chestR = 0.33f; l.shoulderR = 0.22f; l.neckR = 0.165f; l.headR = 0.17f;
                    l.belly = 0.06f; l.skin = H("#E9A27A"); l.top = H("#A8478F"); l.sleeve = H("#A8478F"); l.bottom = H("#B5A07A");
                    l.legs = H("#E9A27A"); l.sock = H("#EDE6DA"); l.feet = H("#6B4636"); l.shoes = 4; l.sleeves = 1; l.lower = 0;
                    l.eyeScale = 1.3f; l.crossEyed = true; l.nose = H("#D9534F"); l.noseR = 0.05f; l.mouth = 1; l.brows = 1; l.browColor = H("#E07B39");
                    break;
                case NpcDatabase.Wanda:
                    l.skin = H("#D6A574"); l.top = H("#EE7F2D"); l.sleeve = H("#EE7F2D"); l.bottom = H("#EE7F2D"); l.legs = H("#EE7F2D");
                    l.lower = 3; l.sleeves = 2; l.shoes = 3; l.feet = H("#5E4436"); l.mouth = 1; l.hair = H("#7A4448"); l.hairStyle = 4; l.browColor = H("#7A4448");
                    break;
                case NpcDatabase.Fin:
                    l.skin = H("#A8724A"); l.top = H("#343849"); l.sleeve = H("#EDE6DA"); l.bottom = H("#343849"); l.legs = H("#343849");
                    l.lower = 3; l.sleeves = 2; l.shoes = 2; l.feet = H("#2B2533"); l.hair = H("#2E2430"); l.hairStyle = 2; l.mouth = 0; l.browColor = H("#2E2430");
                    break;
                case NpcDatabase.Chad:
                case NpcDatabase.Brad:
                    l.skin = H("#E8935A"); l.top = H("#F4EFE6"); l.sleeve = H("#F4EFE6"); l.bottom = H("#F4EFE6"); l.legs = H("#F4EFE6");
                    l.lower = 1; l.sleeves = 1; l.shoes = 2; l.feet = H("#F4EFE6"); l.mouth = 1; l.chestR = 0.27f; l.shoulderR = 0.25f;
                    l.hair = H("#5E4436"); l.hairStyle = id == NpcDatabase.Brad ? 3 : 0; l.browColor = H("#5E4436");
                    break;
                case NpcDatabase.Pearl:
                    l.skin = H("#6B4636"); l.top = H("#EDE6DA"); l.sleeve = H("#EDE6DA"); l.bottom = H("#2F8F6E"); l.legs = H("#6B4636");
                    l.lower = 2; l.sleeves = 2; l.shoes = 1; l.feet = H("#8E6C50"); l.hair = H("#F4EFE6"); l.hairStyle = 2; l.lean = 7f;
                    l.bottomR = 0.29f; l.waistR = 0.285f; l.mouth = 0; l.browColor = H("#F4EFE6");
                    break;
                case NpcDatabase.Mira:
                    l.skin = H("#D6A574"); l.top = H("#C9B48A"); l.sleeve = H("#C9B48A"); l.bottom = H("#4F7FAE"); l.legs = H("#4F7FAE");
                    l.lower = 1; l.sleeves = 1; l.shoes = 2; l.feet = H("#5E4436"); l.hair = H("#3A2C2A"); l.hairStyle = 1; l.mouth = 1;
                    break;
                case NpcDatabase.Tomas:
                    l.skin = H("#A8724A"); l.top = H("#4F7FAE"); l.sleeve = H("#4F7FAE"); l.bottom = H("#3A3F5C"); l.legs = H("#3A3F5C");
                    l.lower = 1; l.sleeves = 2; l.shoes = 3; l.feet = H("#F2C94C"); l.hair = H("#8A8C96"); l.hairStyle = 1; l.mouth = 2;
                    l.browColor = H("#8A8C96"); l.brows = 1;
                    break;
                case NpcDatabase.Ada:
                    l.skin = H("#E8C08A"); l.top = H("#F4EFE6"); l.sleeve = H("#F4EFE6"); l.bottom = H("#8E5CC2"); l.legs = H("#E8C08A");
                    l.lower = 2; l.sleeves = 1; l.shoes = 1; l.feet = H("#8E6C50"); l.hair = H("#5E4436"); l.hairStyle = 2; l.bottomR = 0.3f; l.waistR = 0.3f;
                    l.belly = 0.03f; l.mouth = 1;
                    break;
                case NpcDatabase.Juno:
                    l.skin = H("#F0D2B4"); l.top = H("#F2C94C"); l.sleeve = H("#F2C94C"); l.bottom = H("#2F8F6E"); l.legs = H("#F0D2B4");
                    l.lower = 0; l.sleeves = 0; l.shoes = 1; l.feet = H("#8E6C50"); l.hair = H("#C94F3D"); l.hairStyle = 4; l.browColor = H("#C94F3D"); l.mouth = 0;
                    break;
                case NpcDatabase.Lio:
                    l.legLen = 0.42f; l.bottomR = 0.2f; l.waistR = 0.2f; l.chestR = 0.19f; l.shoulderR = 0.18f; l.neckR = 0.19f; l.headR = 0.2f; l.armR = 0.045f; l.legR = 0.075f;
                    l.skin = H("#4A2F28"); l.top = H("#E0607E"); l.sleeve = H("#E0607E"); l.bottom = H("#3A3F5C"); l.legs = H("#4A2F28");
                    l.lower = 0; l.sleeves = 1; l.shoes = 0; l.feet = H("#4A2F28"); l.hair = H("#1E1B2B"); l.hairStyle = 1; l.eyeScale = 1.12f; l.mouth = 3;
                    l.browColor = H("#1E1B2B");
                    break;
                case NpcDatabase.Toni:
                    l.skin = H("#E8C08A"); l.top = H("#3FA9A0"); l.sleeve = H("#3FA9A0"); l.bottom = H("#B5A07A"); l.legs = H("#E8C08A");
                    l.lower = 0; l.sleeves = 1; l.shoes = 0; l.feet = H("#E8C08A"); l.hair = H("#F4EFE6"); l.hairStyle = 5; l.nose = H("#D9534F"); l.noseR = 0.04f;
                    l.bottomR = 0.3f; l.waistR = 0.3f; l.belly = 0.03f; l.mouth = 0; l.brows = 1; l.browColor = H("#F4EFE6");
                    break;
                case NpcDatabase.Weaver1:
                    l.skin = H("#A8724A"); l.top = H("#EDE6DA"); l.sleeve = H("#EDE6DA"); l.bottom = H("#3FA9A0"); l.lower = 2; l.legs = H("#A8724A"); l.shoes = 1;
                    l.feet = H("#8E6C50"); l.hair = H("#2E2430"); l.hairStyle = 4;
                    break;
                case NpcDatabase.Weaver2:
                    l.skin = H("#F0D2B4"); l.top = H("#EE7F2D"); l.sleeve = H("#EE7F2D"); l.bottom = H("#C9B48A"); l.lower = 2; l.legs = H("#F0D2B4"); l.shoes = 1;
                    l.feet = H("#8E6C50"); l.hair = H("#B9B2A8"); l.hairStyle = 2; l.lean = 4f;
                    break;
                case NpcDatabase.Fisher1:
                    l.skin = H("#6B4636"); l.top = H("#C94F3D"); l.sleeve = H("#C94F3D"); l.bottom = H("#3A3F5C"); l.lower = 1; l.legs = H("#3A3F5C"); l.shoes = 3;
                    l.feet = H("#2F8F6E"); l.hair = H("#1E1B2B"); l.hairStyle = 1;
                    break;
                case NpcDatabase.Fisher2:
                    l.skin = H("#D6A574"); l.top = H("#4F7FAE"); l.sleeve = H("#4F7FAE"); l.bottom = H("#B5A07A"); l.lower = 0; l.legs = H("#D6A574"); l.shoes = 1;
                    l.feet = H("#8E6C50"); l.hair = H("#5E4436"); l.hairStyle = 4;
                    break;
                case NpcDatabase.Teen:
                    l.skin = H("#E8C08A"); l.top = H("#3FB8C4"); l.sleeve = H("#3FB8C4"); l.bottom = H("#3A3F5C"); l.lower = 0; l.sleeves = 0; l.legs = H("#E8C08A"); l.shoes = 2;
                    l.feet = H("#EDE6DA"); l.hair = H("#7A4448"); l.hairStyle = 1; l.mouth = 1;
                    break;
                case NpcDatabase.Grandpa:
                    l.skin = H("#D6A574"); l.top = H("#EDE6DA"); l.sleeve = H("#EDE6DA"); l.bottom = H("#8E6C50"); l.lower = 1; l.legs = H("#8E6C50"); l.shoes = 1;
                    l.feet = H("#5E4436"); l.hair = H("#F4EFE6"); l.hairStyle = 5; l.belly = 0.02f; l.mouth = 2; l.brows = 1; l.browColor = H("#F4EFE6");
                    break;
            }
            if (l.noseR > 0f && l.nose == default) l.nose = l.skin;
            return l;
        }

        // ------------------------------------------------------------------ bean
        static void BuildBean(Transform root, NpcBody b, Look l, string id)
        {
            float hipY = l.legLen;
            float neckY = l.height - l.headR * 1.75f;
            b.hipY = hipY;
            b.height = l.height;
            Material skin = M(l.skin, 0.1f), top = M(l.top, 0.14f), bottom = M(l.bottom, 0.14f);

            b.pivot = NpcMesh.Node(root, "Pivot", new Vector3(0f, hipY, 0f), new Vector3(l.lean, 0f, 0f));
            b.pivotBase = b.pivot.localRotation;
            b.pivotBasePos = b.pivot.localPosition;

            // ---- torso lathe (hip → collar), submeshes 0 top, 1 bottom, 2 skin (belly band)
            float span = neckY - hipY;
            float waistY = hipY + span * (l.lower == 3 ? 0.0f : 0.3f);
            var ctrl = new[]
            {
                new Vector2(0f, hipY - 0.06f),
                new Vector2(l.bottomR * 0.62f, hipY - 0.045f),
                new Vector2(l.bottomR * 0.93f, hipY + span * 0.06f),
                new Vector2(l.bottomR, hipY + span * 0.22f),
                new Vector2(l.waistR, hipY + span * 0.42f),
                new Vector2(Mathf.Lerp(l.waistR, l.chestR, 0.6f), hipY + span * 0.62f),
                new Vector2(l.chestR, hipY + span * 0.8f),
                new Vector2(l.shoulderR, hipY + span * 0.93f),
                new Vector2(l.neckR * 1.02f, neckY),
            };
            var prof = NpcMesh.Smooth(ctrl, 3);
            int lower = l.lower;
            bool belly = id == NpcDatabase.Horst;
            float bellyY0 = hipY + span * 0.27f, bellyY1 = hipY + span * 0.36f;
            string key = "torso_" + id;
            var torsoMesh = NpcMesh.Cached(key, () =>
            {
                var m = NpcMesh.Lathe(prof, Segs, 3, (i, j) =>
                {
                    float y = (prof[i].y + prof[Mathf.Min(prof.Length - 1, i + 1)].y) * 0.5f;
                    float ang = (j + 0.5f) / Segs * 360f;
                    bool front = Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 50f;
                    if (belly && front && y > bellyY0 && y < bellyY1) return 2;
                    if (lower != 3 && y < waistY) return 1;
                    return 0;
                });
                return BulgeFront(m, l.belly, hipY + span * 0.35f, span * 0.35f);
            });
            var torso = NpcMesh.Part(b.pivot, "Torso", torsoMesh, top, new Vector3(0f, -hipY, 0f), Vector3.one);
            torso.sharedMaterials = new[] { top, lower == 3 ? top : bottom, skin };

            // collar ring hides the head seam
            NpcMesh.Part(b.pivot, "Collar", NpcMesh.Torus(l.neckR * 1.0f, 0.02f, Segs, 8), top, new Vector3(0f, neckY - hipY - 0.005f, 0f), Vector3.one, default, false);

            // ---- head (collar → crown)
            b.head = NpcMesh.Node(b.pivot, "Head", new Vector3(0f, neckY - hipY, 0f), new Vector3(-l.lean * 0.7f, 0f, 0f));
            float hr = l.headR, nr = l.neckR;
            var headCtrl = new[]
            {
                new Vector2(0f, -0.01f), new Vector2(nr * 0.98f, 0f), new Vector2(Mathf.Lerp(nr, hr, 0.6f), hr * 0.35f),
                new Vector2(hr, hr * 0.8f), new Vector2(hr * 0.97f, hr * 1.15f), new Vector2(hr * 0.85f, hr * 1.45f),
                new Vector2(hr * 0.6f, hr * 1.66f), new Vector2(hr * 0.3f, hr * 1.76f), new Vector2(0f, hr * 1.78f),
            };
            var headProf = NpcMesh.Smooth(headCtrl, 3);
            NpcMesh.Part(b.head, "HeadBean", NpcMesh.Cached("head_" + id, () => NpcMesh.Lathe(headProf, Segs)), skin, Vector3.zero, Vector3.one);
            Face(b, l, hr);
            Hair(b, l, hr);

            // ---- arms
            float shoulderY = hipY + span * 0.86f;
            b.shoulderY = shoulderY;
            float armLen = Mathf.Max(0.45f, (shoulderY - hipY) * 0.95f + 0.12f);
            if (id == NpcDatabase.Lio) armLen = 0.46f;
            b.upperArm = armLen * 0.5f;
            b.foreArm = armLen * 0.46f;
            float sx = Mathf.Lerp(l.chestR, l.shoulderR, 0.5f) + l.armR * 0.6f;
            b.shoulderL = NpcMesh.Node(b.pivot, "ShoulderL", new Vector3(-sx, shoulderY - hipY, 0f));
            b.shoulderR = NpcMesh.Node(b.pivot, "ShoulderR", new Vector3(sx, shoulderY - hipY, 0f));
            Material upper = l.sleeves >= 1 ? M(l.sleeve, 0.14f) : skin;
            Material fore = l.sleeves >= 2 ? M(l.sleeve, 0.14f) : skin;
            Arm(b.shoulderL, out b.elbowL, out b.handL, l, upper, fore, skin, b.upperArm, b.foreArm);
            Arm(b.shoulderR, out b.elbowR, out b.handR, l, upper, fore, skin, b.upperArm, b.foreArm);

            // ---- legs
            float hx = l.bottomR * 0.45f;
            b.thigh = hipY * 0.5f;
            b.shin = hipY * 0.5f;
            b.hipL = NpcMesh.Node(b.pivot, "HipL", new Vector3(-hx, 0.02f, 0f));
            b.hipR = NpcMesh.Node(b.pivot, "HipR", new Vector3(hx, 0.02f, 0f));
            Material thighM = lower == 0 ? skin : lower == 2 ? skin : M(l.legs, 0.14f);
            if (lower == 1 || lower == 3) thighM = M(l.legs, 0.14f);
            Material shinM = l.shoes == 4 ? M(l.sock, 0.1f) : (lower == 1 || lower == 3) ? M(l.legs, 0.14f) : skin;
            Leg(b.hipL, out b.kneeL, l, thighM, shinM, b.thigh, b.shin, -1);
            Leg(b.hipR, out b.kneeR, l, thighM, shinM, b.thigh, b.shin, 1);
            // shorts / skirt over the thigh tops
            if (lower == 0)
                NpcMesh.Part(b.pivot, "Shorts", NpcMesh.Cached("shorts_" + id, () => ShortsMesh(l)), bottom, Vector3.zero, Vector3.one);
            else if (lower == 2)
                NpcMesh.Part(b.pivot, "Skirt", NpcMesh.Cached("skirt_" + id, () => SkirtMesh(l)), bottom, Vector3.zero, Vector3.one);
        }

        /// Pushes the lower front of the torso forward (belly) with a smooth falloff.
        static Mesh BulgeFront(Mesh m, float amount, float centerY, float halfHeight)
        {
            if (amount <= 0.001f) return m;
            var v = m.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                float fy = 1f - Mathf.Clamp01(Mathf.Abs(v[i].y - centerY) / halfHeight);
                fy = fy * fy * (3f - 2f * fy);
                float fz = Mathf.Clamp01(v[i].z / 0.25f);
                v[i].z += amount * fy * fz;
            }
            m.vertices = v;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh ShortsMesh(Look l)
        {
            // a soft skirt-like band around both thigh tops
            float r = l.bottomR * 0.98f;
            var prof = NpcMesh.Smooth(new[] { new Vector2(r * 0.86f, -0.2f), new Vector2(r * 0.95f, -0.12f), new Vector2(r, -0.02f), new Vector2(r * 0.9f, 0.04f) }, 3);
            return NpcMesh.Lathe(prof, Segs);
        }

        static Mesh SkirtMesh(Look l)
        {
            float r = l.bottomR;
            float down = l.legLen * 0.72f;
            var prof = NpcMesh.Smooth(new[] { new Vector2(r * 1.12f, -down), new Vector2(r * 1.06f, -down * 0.6f), new Vector2(r * 1.0f, -down * 0.2f), new Vector2(r * 0.92f, 0.05f) }, 3);
            return NpcMesh.Lathe(prof, Segs);
        }

        static void Arm(Transform shoulder, out Transform elbow, out Transform hand, Look l, Material upper, Material fore, Material skin, float ua, float fa)
        {
            float r = l.armR;
            NpcMesh.Part(shoulder, "Joint", NpcMesh.Sphere(10, 16), upper, Vector3.zero, Vector3.one * r * 2.3f, default, false);
            NpcMesh.Part(shoulder, "UpperArm", NpcMesh.Limb(r * 1.08f, r, ua, 16), upper, Vector3.zero, Vector3.one);
            elbow = NpcMesh.Node(shoulder, "Elbow", new Vector3(0f, -ua, 0f));
            NpcMesh.Part(elbow, "Ball", NpcMesh.Sphere(10, 16), fore, Vector3.zero, Vector3.one * r * 2.1f, default, false);
            NpcMesh.Part(elbow, "ForeArm", NpcMesh.Limb(r, r * 0.9f, fa, 16), fore, Vector3.zero, Vector3.one);
            if (l.sleeves == 1) NpcMesh.Part(shoulder, "Sleeve", NpcMesh.Torus(r * 1.15f, r * 0.32f, 16, 6), upper, new Vector3(0f, -ua * 0.55f, 0f), Vector3.one, default, false);
            hand = NpcMesh.Node(elbow, "Hand", new Vector3(0f, -fa - r * 0.6f, 0f));
            NpcMesh.Part(hand, "Mitten", NpcMesh.Sphere(12, 18), skin, new Vector3(0f, -0.035f, 0.005f), new Vector3(r * 1.9f, r * 2.4f, r * 1.5f));
            NpcMesh.Part(hand, "Thumb", NpcMesh.Capsule(r * 0.38f, r * 1.2f, 10), skin, new Vector3(0f, -0.01f, r * 0.7f), Vector3.one, new Vector3(140f, 0f, 0f), false);
        }

        static void Leg(Transform hip, out Transform knee, Look l, Material thigh, Material shin, float tl, float sl, int side)
        {
            float r = l.legR;
            NpcMesh.Part(hip, "Thigh", NpcMesh.Limb(r * 1.05f, r * 0.95f, tl, 16), thigh, Vector3.zero, Vector3.one);
            knee = NpcMesh.Node(hip, "Knee", new Vector3(0f, -tl, 0f));
            NpcMesh.Part(knee, "Ball", NpcMesh.Sphere(10, 16), shin, Vector3.zero, Vector3.one * r * 2.05f, default, false);
            NpcMesh.Part(knee, "Shin", NpcMesh.Limb(r * 0.95f, r * 0.82f, sl - 0.06f, 16), shin, Vector3.zero, Vector3.one);
            var foot = NpcMesh.Node(knee, "Foot", new Vector3(0f, -sl + 0.05f, 0.04f));
            switch (l.shoes)
            {
                case 0:
                    NpcMesh.Part(foot, "Foot", NpcMesh.Sphere(10, 16), M(l.skin, 0.1f), new Vector3(0f, -0.02f, 0.03f), new Vector3(r * 1.7f, r * 0.9f, r * 2.8f));
                    break;
                case 1:
                case 4:
                    NpcMesh.Part(foot, "Foot", NpcMesh.Sphere(10, 16), M(l.shoes == 4 ? l.sock : l.skin, 0.1f), new Vector3(0f, -0.015f, 0.03f), new Vector3(r * 1.7f, r * 0.9f, r * 2.8f));
                    NpcMesh.Part(foot, "Sole", NpcMesh.RoundBox(r * 1.9f, 0.025f, r * 3.1f, 0.6f, 6), M(l.feet, 0.1f), new Vector3(0f, -0.045f, 0.03f), Vector3.one);
                    NpcMesh.Part(foot, "Strap", NpcMesh.Torus(r * 0.75f, 0.012f, 16, 5), M(l.feet, 0.1f), new Vector3(0f, -0.02f, 0.06f), new Vector3(1.1f, 1f, 0.7f), new Vector3(0f, 0f, 0f), false);
                    break;
                case 3:
                    NpcMesh.Part(foot, "Boot", NpcMesh.RoundBox(r * 2.1f, 0.2f, r * 2.9f, 0.55f, 6), M(l.feet, 0.2f), new Vector3(0f, 0.04f, 0.02f), Vector3.one);
                    break;
                default:
                    NpcMesh.Part(foot, "Shoe", NpcMesh.RoundBox(r * 2.0f, 0.09f, r * 3.1f, 0.6f, 6), M(l.feet, 0.15f), new Vector3(0f, -0.02f, 0.04f), Vector3.one);
                    break;
            }
        }

        // ------------------------------------------------------------------ face
        static void Face(NpcBody b, Look l, float hr)
        {
            float eyeR = 0.055f * (hr / 0.226f) * l.eyeScale;
            if (b.height < 1.3f) eyeR = 0.05f * l.eyeScale;
            b.eyeR = eyeR;
            float eyeY = hr * 0.98f, eyeX = hr * 0.36f;
            float zs = Mathf.Sqrt(Mathf.Max(0.0001f, hr * hr - eyeX * eyeX)) * 0.97f;
            Mesh sph = NpcMesh.Sphere(12, 20);
            Material white = M(EyeWhite, 0.35f), ink = M(Ink, 0.4f), hl = M(Color.white, 0.6f);
            for (int s = -1; s <= 1; s += 2)
            {
                // the eye node is squashed vertically to blink (cartoon blink, NpcActor)
                var eye = NpcMesh.Node(b.head, s < 0 ? "EyeL" : "EyeR", new Vector3(s * eyeX, eyeY, zs + eyeR * 0.35f));
                NpcMesh.Part(eye, "White", sph, white, Vector3.zero, Vector3.one * eyeR * 2f, default, false);
                float yaw = l.crossEyed ? -s * 14f : s * 3f, pitch = l.crossEyed ? 6f : -4f;
                var pivot = NpcMesh.Node(eye, "PupilPivot", Vector3.zero, new Vector3(pitch, yaw, 0f));
                float pr = eyeR * 0.5f;
                NpcMesh.Part(pivot, "Pupil", NpcMesh.Sphere(8, 16), ink, new Vector3(0f, 0f, eyeR - pr * 0.35f), new Vector3(pr * 2f, pr * 2f, pr * 0.8f), default, false);
                NpcMesh.Part(pivot, "Shine", NpcMesh.Sphere(6, 10), hl, new Vector3(pr * 0.35f, pr * 0.4f, eyeR + 0.002f), Vector3.one * eyeR * 0.36f, default, false);
                if (s < 0) { b.pupilL = pivot; b.lidL = eye; } else { b.pupilR = pivot; b.lidR = eye; }
                if (l.brows > 0)
                {
                    var brow = NpcMesh.Node(b.head, s < 0 ? "BrowL" : "BrowR", new Vector3(s * eyeX, eyeY + eyeR * 1.35f, zs + eyeR * 0.15f), new Vector3(0f, s * -12f, s * -6f));
                    float w = l.brows == 1 ? eyeR * 2.2f : eyeR * 1.7f, th = l.brows == 1 ? 0.026f : 0.012f;
                    NpcMesh.Part(brow, "Brow", NpcMesh.Arc("brow" + l.brows + "_" + Mathf.RoundToInt(eyeR * 1000f), w, th, -0.01f, 0.012f), M(l.browColor, 0.1f),
                        Vector3.zero, Vector3.one, new Vector3(0f, 180f, 0f), false);
                    if (s < 0) b.browL = brow; else b.browR = brow;
                }
            }
            // nose
            if (l.noseR > 0f)
                NpcMesh.Part(b.head, "Nose", NpcMesh.Sphere(10, 16), M(l.nose, 0.18f), new Vector3(0f, eyeY - eyeR * 1.0f, zs + l.noseR * 0.7f), Vector3.one * l.noseR * 2f);
            // mouth: closed shape + open shape (toggled by the talk animation)
            float mouthY = eyeY - eyeR * 2.0f - 0.02f;
            float mz = Mathf.Sqrt(Mathf.Max(0.0001f, hr * hr - 0.0004f)) * 0.995f;
            b.mouth = NpcMesh.Node(b.head, "Mouth", new Vector3(0f, mouthY, mz), new Vector3(-10f, 180f, 0f));
            Material mm = M(MouthC, 0.3f);
            Mesh shape;
            switch (l.mouth)
            {
                case 1: shape = NpcMesh.Arc("grin", 0.09f, 0.03f, 0.022f, 0.008f); break;
                case 2: shape = NpcMesh.Arc("flat", 0.06f, 0.012f, 0.004f, 0.008f); break;
                case 3: shape = NpcMesh.Plate("mouth_o", NpcMesh.Ellipse(0.022f, 0.026f, 14), 0.008f); break;
                default: shape = NpcMesh.Arc("smile", 0.075f, 0.016f, 0.018f, 0.008f); break;
            }
            NpcMesh.Part(b.mouth, "Closed", shape, mm, Vector3.zero, Vector3.one, default, false);
            b.mouthOpen = NpcMesh.Node(b.mouth, "Open", Vector3.zero);
            NpcMesh.Part(b.mouthOpen, "OpenShape", NpcMesh.Plate("mouth_open", NpcMesh.Ellipse(0.032f, 0.024f, 16), 0.008f), mm, new Vector3(0f, -0.006f, -0.001f), Vector3.one, default, false);
            NpcMesh.Part(b.mouthOpen, "Tongue", NpcMesh.Plate("tongue", NpcMesh.Ellipse(0.018f, 0.009f, 12), 0.006f), M(H("#D9534F"), 0.3f), new Vector3(0f, -0.016f, -0.004f), Vector3.one, default, false);
            b.mouthOpen.localScale = new Vector3(1f, 0.01f, 1f);
        }

        static void Hair(NpcBody b, Look l, float hr)
        {
            if (l.hairStyle == 0) return;
            Material hm = M(l.hair, 0.18f);
            switch (l.hairStyle)
            {
                case 1: // short cap of hair on the back/top
                    NpcMesh.Part(b.head, "Hair", NpcMesh.Dome(10, 28), hm, new Vector3(0f, hr * 1.02f, -hr * 0.08f), new Vector3(hr * 2.12f, hr * 1.55f, hr * 2.15f), new Vector3(-14f, 0f, 0f));
                    break;
                case 2: // bun
                    NpcMesh.Part(b.head, "Hair", NpcMesh.Dome(10, 28), hm, new Vector3(0f, hr * 1.05f, -hr * 0.1f), new Vector3(hr * 2.1f, hr * 1.45f, hr * 2.12f), new Vector3(-18f, 0f, 0f));
                    NpcMesh.Part(b.head, "Bun", NpcMesh.Sphere(10, 18), hm, new Vector3(0f, hr * 1.62f, -hr * 0.55f), Vector3.one * hr * 0.75f);
                    break;
                case 3: // man bun on top
                    NpcMesh.Part(b.head, "Hair", NpcMesh.Dome(10, 28), hm, new Vector3(0f, hr * 1.08f, -hr * 0.05f), new Vector3(hr * 2.08f, hr * 1.4f, hr * 2.1f), new Vector3(-10f, 0f, 0f));
                    NpcMesh.Part(b.head, "Bun", NpcMesh.Sphere(10, 18), hm, new Vector3(0f, hr * 1.85f, -hr * 0.2f), Vector3.one * hr * 0.62f);
                    break;
                case 4: // shoulder length
                    NpcMesh.Part(b.head, "Hair", NpcMesh.Dome(10, 28), hm, new Vector3(0f, hr * 1.0f, -hr * 0.1f), new Vector3(hr * 2.18f, hr * 1.6f, hr * 2.2f), new Vector3(-12f, 0f, 0f));
                    NpcMesh.Part(b.head, "Back", NpcMesh.Capsule(hr * 0.95f, hr * 1.4f, 24), hm, new Vector3(0f, hr * 0.15f, -hr * 0.42f), new Vector3(1.05f, 1f, 0.55f));
                    break;
                case 5: // white ring around a bald top
                    NpcMesh.Part(b.head, "Ring", NpcMesh.Torus(hr * 0.93f, hr * 0.17f, 28, 8), hm, new Vector3(0f, hr * 1.05f, -hr * 0.12f), new Vector3(1f, 1f, 1f), new Vector3(-12f, 0f, 0f));
                    break;
            }
        }

        // ------------------------------------------------------------------ accessories per character
        static void Accessories(string id, NpcBody b, Look l)
        {
            float hr = l.headR;
            switch (id)
            {
                case NpcDatabase.Horst: Horst(b, l, hr); break;
                case NpcDatabase.Wanda: Wanda(b, l, hr); break;
                case NpcDatabase.Fin: Fin(b, l, hr); break;
                case NpcDatabase.Chad: Bottom(b, l, hr, true); break;
                case NpcDatabase.Brad: Bottom(b, l, hr, false); break;
                case NpcDatabase.Pearl: Pearl(b, l, hr); break;
                case NpcDatabase.Mira: Mira(b, l, hr); break;
                case NpcDatabase.Tomas: Tomas(b, l, hr); break;
                case NpcDatabase.Ada: Ada(b, l, hr); break;
                case NpcDatabase.Juno: Juno(b, l, hr); break;
                case NpcDatabase.Lio: Lio(b, l, hr); break;
                case NpcDatabase.Toni: Toni(b, l, hr); break;
                case NpcDatabase.Weaver1: SunHat(b, hr, H("#C9B48A"), 1.25f); break;
                case NpcDatabase.Weaver2: Glasses(b, l, hr, H("#8E6C50"), H("#CFE8F0"), false, 0.8f); break;
                case NpcDatabase.Fisher1: Cap(b, hr, H("#3FA9A0"), false); break;
                case NpcDatabase.Fisher2: Apron(b, l, H("#3FA9A0"), false); break;
                case NpcDatabase.Teen: Ukulele(b); break;
                case NpcDatabase.Grandpa: SunHat(b, hr, H("#C9B48A"), 1.1f); break;
            }
        }

        static float FaceZ(float hr, float x, float y)
        {
            // approximate head surface (sphere of radius hr centred at y = hr * 0.85)
            float dy = y - hr * 0.85f;
            return Mathf.Sqrt(Mathf.Max(0.0001f, hr * hr - x * x - dy * dy * 0.7f));
        }

        static void Horst(NpcBody b, Look l, float hr)
        {
            // pineapple blocks on the Hawaiian shirt (front + back) – flat yellow quads with a green crown
            Material pine = M(H("#F2C94C"), 0.14f), leaf = M(H("#6E9445"), 0.14f);
            float y0 = b.shoulderY - b.hipY;
            Vector3[] spots = { new Vector3(0.12f, y0 - 0.25f, 0f), new Vector3(-0.16f, y0 - 0.42f, 0f), new Vector3(0.05f, y0 - 0.55f, 0f), new Vector3(-0.08f, y0 - 0.15f, 0f) };
            for (int i = 0; i < spots.Length; i++)
            {
                float y = spots[i].y, x = spots[i].x;
                float r = TorsoRadiusAt(l, b, y + b.hipY);
                float z = Mathf.Sqrt(Mathf.Max(0.0001f, r * r - x * x)) + 0.004f;
                for (int side = -1; side <= 1; side += 2)
                {
                    float zz = side > 0 ? z + (i == 2 ? l.belly * 0.5f : 0f) : -z;
                    var p = NpcMesh.Node(b.pivot, "Pineapple", new Vector3(x * side, y, zz), new Vector3(0f, side > 0 ? 180f + Mathf.Atan2(x, z) * Mathf.Rad2Deg : -Mathf.Atan2(x, z) * Mathf.Rad2Deg, 0f));
                    NpcMesh.Part(p, "Fruit", NpcMesh.Plate("pine_body", NpcMesh.Ellipse(0.028f, 0.04f, 10), 0.006f), pine, Vector3.zero, Vector3.one, default, false);
                    NpcMesh.Part(p, "Crown", NpcMesh.Plate("pine_crown", new[] { new Vector2(-0.022f, 0f), new Vector2(0.022f, 0f), new Vector2(0.012f, 0.03f), new Vector2(0f, 0.045f), new Vector2(-0.012f, 0.03f) }, 0.006f),
                        leaf, new Vector3(0f, 0.036f, 0f), Vector3.one, default, false);
                }
            }
            // giant walrus moustache: 3 tufts per side, 0.46 m wide
            var mo = NpcMesh.Node(b.head, "Moustache", new Vector3(0f, hr * 0.98f - b.eyeR * 1.7f, FaceZ(hr, 0f, hr * 0.7f) + 0.03f));
            b.moustache = mo;
            Material mm = M(H("#E07B39"), 0.2f);
            for (int s = -1; s <= 1; s += 2)
                for (int t = 0; t < 3; t++)
                {
                    float ang = (12f + t * 14f) * s;
                    var path = new[]
                    {
                        new Vector3(0.01f * s, 0.005f, 0f), new Vector3(0.08f * s, -0.01f - t * 0.012f, -0.02f),
                        new Vector3(0.16f * s, -0.04f - t * 0.02f, -0.05f), new Vector3(0.23f * s, -0.08f - t * 0.03f, -0.07f)
                    };
                    NpcMesh.Part(mo, "Tuft", NpcMesh.Sweep("moust_" + s + "_" + t, path, 0.03f - t * 0.004f, 0.014f, 10), mm, new Vector3(0f, -t * 0.006f, -t * 0.006f), Vector3.one, new Vector3(0f, 0f, ang * 0.1f), false);
                }
            // sunglasses pushed up on the forehead
            Glasses(b, l, hr, H("#1E1B2B"), H("#3FA9B5"), true, 1.05f);
            // bucket hat with three lures
            var hat = NpcMesh.Node(b.head, "Hat", new Vector3(0f, hr * 1.42f, -0.01f), new Vector3(-6f, 0f, 0f));
            b.hat = hat;
            var prof = NpcMesh.Smooth(new[] { new Vector2(hr * 1.0f, 0f), new Vector2(hr * 1.75f, -0.05f), new Vector2(hr * 1.85f, -0.045f), new Vector2(hr * 1.35f, 0.01f), new Vector2(hr * 1.12f, 0.05f), new Vector2(hr * 1.06f, 0.16f), new Vector2(hr * 0.85f, 0.21f), new Vector2(0f, 0.22f) }, 2);
            NpcMesh.Part(hat, "Bucket", NpcMesh.Cached("horst_hat", () => NpcMesh.Lathe(prof, Segs)), M(H("#C9B48A"), 0.1f), Vector3.zero, Vector3.one);
            NpcMesh.Part(hat, "Band", NpcMesh.Torus(hr * 1.09f, 0.014f, Segs, 6), M(H("#7A4448"), 0.1f), new Vector3(0f, 0.05f, 0f), Vector3.one, default, false);
            Color[] lures = { H("#F2C94C"), H("#E0607E"), H("#3FB8C4") };
            for (int i = 0; i < 3; i++)
            {
                float a = (200f + i * 55f) * Mathf.Deg2Rad;
                var lp = NpcMesh.Node(hat, "Lure", new Vector3(Mathf.Cos(a) * hr * 1.7f, -0.05f, Mathf.Sin(a) * hr * 1.7f));
                NpcMesh.Part(lp, "Line", NpcMesh.Cylinder(0.003f, 0.06f, 6), M(H("#EDE6DA"), 0.3f), new Vector3(0f, -0.06f, 0f), Vector3.one, default, false);
                NpcMesh.Part(lp, "Bait", NpcMesh.Sphere(8, 12), M(lures[i], 0.4f), new Vector3(0f, -0.08f, 0f), new Vector3(0.022f, 0.04f, 0.022f), default, false);
            }
            // whistle on an orange lanyard
            float chestY = b.shoulderY - b.hipY - 0.05f;
            NpcMesh.Part(b.pivot, "Lanyard", NpcMesh.Torus(l.neckR * 1.12f, 0.008f, Segs, 5), M(H("#EE7F2D"), 0.2f), new Vector3(0f, chestY + 0.03f, 0.015f), new Vector3(1f, 1f, 1.1f), new Vector3(28f, 0f, 0f), false);
            float wz = TorsoRadiusAt(l, b, b.shoulderY - 0.18f) + 0.02f;
            NpcMesh.Part(b.pivot, "Whistle", NpcMesh.Capsule(0.014f, 0.05f, 10), M(H("#C9CED6"), 0.6f), new Vector3(0f, chestY - 0.2f, wz), Vector3.one, new Vector3(90f, 0f, 0f), false);
            // fish sandwich in the right hand
            var sw = NpcMesh.Node(b.handR, "Sandwich", new Vector3(0f, -0.05f, 0.05f), new Vector3(0f, 90f, 0f));
            b.propR = sw;
            NpcMesh.Part(sw, "Bun", NpcMesh.Capsule(0.03f, 0.13f, 12), M(H("#D9A35E"), 0.15f), new Vector3(0f, 0f, -0.065f), Vector3.one, new Vector3(90f, 0f, 0f));
            NpcMesh.Part(sw, "Fish", NpcMesh.Capsule(0.017f, 0.16f, 10), M(H("#9FB4C4"), 0.3f), new Vector3(0f, 0.01f, -0.08f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            NpcMesh.Part(sw, "Lettuce", NpcMesh.Torus(0.03f, 0.008f, 12, 5), M(H("#7FA243"), 0.15f), new Vector3(0f, 0.005f, 0f), new Vector3(1f, 1f, 2.1f), default, false);
        }

        static float TorsoRadiusAt(Look l, NpcBody b, float y)
        {
            float span = b.height - l.headR * 1.75f - b.hipY;
            float t = Mathf.Clamp01((y - b.hipY) / Mathf.Max(0.01f, span));
            if (t < 0.22f) return Mathf.Lerp(l.bottomR * 0.93f, l.bottomR, t / 0.22f);
            if (t < 0.42f) return Mathf.Lerp(l.bottomR, l.waistR, (t - 0.22f) / 0.2f);
            if (t < 0.8f) return Mathf.Lerp(l.waistR, l.chestR, (t - 0.42f) / 0.38f);
            return Mathf.Lerp(l.chestR, l.shoulderR, (t - 0.8f) / 0.2f);
        }

        static void Glasses(NpcBody b, Look l, float hr, Color frame, Color lens, bool pushedUp, float scale)
        {
            var g = NpcMesh.Node(b.head, "Glasses", Vector3.zero);
            float eyeY = hr * 0.98f, eyeX = hr * 0.36f;
            float y = pushedUp ? hr * 1.36f : eyeY;
            float r = b.eyeR * 1.15f * scale;
            float z = FaceZ(hr, eyeX, y) + (pushedUp ? 0.012f : b.eyeR * 0.9f);
            Material fm = M(frame, 0.45f), lm = NpcMesh.Glass(new Color(lens.r, lens.g, lens.b, 0.62f));
            for (int s = -1; s <= 1; s += 2)
            {
                NpcMesh.Part(g, "Rim", NpcMesh.Torus(r, 0.0075f, 22, 6), fm, new Vector3(s * eyeX, y, z), Vector3.one, new Vector3(90f + (pushedUp ? -35f : 0f), 0f, 0f), false);
                NpcMesh.Part(g, "Lens", NpcMesh.Plate("lens", NpcMesh.Ellipse(1f, 1f, 22), 0.02f), lm, new Vector3(s * eyeX, y, z), new Vector3(r, r, 0.3f), new Vector3(pushedUp ? -35f : 0f, 0f, 0f), false);
                // arm toward the ear
                var arm = new[] { new Vector3(s * (eyeX + r), y, z), new Vector3(s * hr * 0.92f, y + 0.005f, z * 0.45f), new Vector3(s * hr * 0.98f, y - 0.01f, -hr * 0.1f) };
                NpcMesh.Part(g, "Arm", NpcMesh.Sweep("glarm_" + s + "_" + Mathf.RoundToInt(hr * 1000f) + (pushedUp ? "u" : "d"), arm, 0.006f, 0.006f, 6), fm, Vector3.zero, Vector3.one, default, false);
            }
            NpcMesh.Part(g, "Bridge", NpcMesh.Capsule(0.006f, eyeX * 2f - r * 2f + 0.01f, 6), fm, new Vector3(-(eyeX - r) - 0.005f, y + 0.008f, z), Vector3.one, new Vector3(0f, 0f, -90f), false);
        }

        static void Wanda(NpcBody b, Look l, float hr)
        {
            // aviator cap + goggles on the cap, scarf, patches on the flight suit
            var cap = NpcMesh.Node(b.head, "Cap", new Vector3(0f, hr * 0.95f, -0.01f));
            b.hat = cap;
            NpcMesh.Part(cap, "Leather", NpcMesh.Dome(10, Segs), M(H("#6B4636"), 0.25f), Vector3.zero, new Vector3(hr * 2.14f, hr * 1.7f, hr * 2.16f));
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(cap, "Flap", NpcMesh.RoundBox(0.02f, 0.13f, 0.09f, 0.8f, 6), M(H("#6B4636"), 0.25f), new Vector3(s * hr * 1.0f, -0.07f, -0.01f), Vector3.one, new Vector3(0f, 0f, s * 6f));
            var gog = NpcMesh.Node(cap, "Goggles", new Vector3(0f, hr * 0.55f, hr * 0.62f), new Vector3(-30f, 0f, 0f));
            for (int s = -1; s <= 1; s += 2)
            {
                NpcMesh.Part(gog, "Cup", NpcMesh.Cylinder(0.04f, 0.03f, 18), M(H("#C9CED6"), 0.6f), new Vector3(s * 0.055f, 0f, 0f), Vector3.one, new Vector3(90f, 0f, 0f), false);
                NpcMesh.Part(gog, "Lens", NpcMesh.Cylinder(0.032f, 0.004f, 18), NpcMesh.Glass(new Color(0.62f, 0.89f, 0.93f, 0.6f)), new Vector3(s * 0.055f, 0f, 0.031f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            }
            NpcMesh.Part(b.pivot, "Scarf", NpcMesh.Torus(l.neckR * 1.08f, 0.04f, Segs, 8), M(H("#3FB8C4"), 0.15f), new Vector3(0f, b.height - hr * 1.75f - b.hipY - 0.01f, 0f), Vector3.one, default, false);
            NpcMesh.Part(b.pivot, "ScarfEnd", NpcMesh.RoundBox(0.07f, 0.2f, 0.025f, 0.6f, 6), M(H("#3FB8C4"), 0.15f), new Vector3(0.09f, b.shoulderY - b.hipY - 0.14f, l.chestR + 0.03f), Vector3.one, new Vector3(-8f, 0f, 12f), false);
            Patch(b, l, new Vector3(-0.12f, b.shoulderY - b.hipY - 0.12f, 0f), H("#F2C94C"), 0.035f);
            Patch(b, l, new Vector3(0.13f, b.shoulderY - b.hipY - 0.3f, 0f), H("#3FB8C4"), 0.03f);
            NpcMesh.Part(b.pivot, "Belt", NpcMesh.Torus(l.waistR * 1.0f, 0.025f, Segs, 6), M(H("#5E4436"), 0.2f), new Vector3(0f, (b.height - hr * 1.75f - b.hipY) * 0.3f, 0f), Vector3.one, default, false);
        }

        static void Patch(NpcBody b, Look l, Vector3 at, Color c, float r)
        {
            float rad = TorsoRadiusAt(l, b, at.y + b.hipY);
            float z = Mathf.Sqrt(Mathf.Max(0.0001f, rad * rad - at.x * at.x)) + 0.003f;
            NpcMesh.Part(b.pivot, "Patch", NpcMesh.Plate("patch", NpcMesh.Ellipse(1f, 1f, 16), 0.1f), M(c, 0.15f), new Vector3(at.x, at.y, z), new Vector3(r, r, 0.05f),
                new Vector3(0f, 180f + Mathf.Atan2(at.x, z) * Mathf.Rad2Deg, 0f), false);
        }

        static void Fin(NpcBody b, Look l, float hr)
        {
            // lab coat over the wetsuit: an open shell around the torso (front gap shows the suit)
            float span = b.height - hr * 1.75f - b.hipY;
            var coat = NpcMesh.Smooth(new[]
            {
                new Vector2(l.bottomR * 1.12f, -0.38f), new Vector2(l.bottomR * 1.07f, 0f), new Vector2(l.waistR * 1.05f, span * 0.42f),
                new Vector2(l.chestR * 1.06f, span * 0.8f), new Vector2(l.shoulderR * 1.06f, span * 0.93f), new Vector2(l.neckR * 1.08f, span + 0.005f)
            }, 3);
            var coatMesh = NpcMesh.Cached("fin_coat", () => NpcMesh.Lathe(coat, Segs, 2, (i, j) =>
            {
                float ang = (j + 0.5f) / Segs * 360f;
                return Mathf.Abs(Mathf.DeltaAngle(ang, 90f)) < 22f ? 1 : 0;
            }));
            var cr = NpcMesh.Part(b.pivot, "Coat", coatMesh, M(H("#EDE6DA"), 0.15f), Vector3.zero, Vector3.one);
            cr.sharedMaterials = new[] { M(H("#EDE6DA"), 0.15f), M(H("#343849"), 0.18f) };
            // huge round glasses
            Glasses(b, l, hr, H("#1E1B2B"), H("#9FE4EC"), false, 1.35f);
            // pencil behind the right ear
            NpcMesh.Part(b.head, "Pencil", NpcMesh.Capsule(0.008f, 0.14f, 8), M(H("#F2C94C"), 0.2f), new Vector3(hr * 0.98f, hr * 1.05f, -0.02f), Vector3.one, new Vector3(-70f, 0f, 20f), false);
            // waterproof clipboard in the left hand
            var cb = NpcMesh.Node(b.handL, "Clipboard", new Vector3(0f, -0.06f, 0.07f), new Vector3(-70f, 0f, 0f));
            b.propL = cb;
            NpcMesh.Part(cb, "Board", NpcMesh.RoundBox(0.22f, 0.3f, 0.016f, 0.3f, 6), M(H("#3FA9B5"), 0.3f), Vector3.zero, Vector3.one);
            NpcMesh.Part(cb, "Paper", NpcMesh.RoundBox(0.19f, 0.24f, 0.004f, 0.1f, 4), M(H("#F4EFE6"), 0.1f), new Vector3(0f, -0.015f, 0.01f), Vector3.one, default, false);
            NpcMesh.Part(cb, "Clip", NpcMesh.RoundBox(0.08f, 0.03f, 0.02f, 0.5f, 4), M(H("#C9CED6"), 0.6f), new Vector3(0f, 0.14f, 0.01f), Vector3.one, default, false);
        }

        static void Bottom(NpcBody b, Look l, float hr, bool chad)
        {
            // gold chain, mirrored sunglasses, captain epaulettes; Chad: backwards cap. Both carry a selfie stick.
            NpcMesh.Part(b.pivot, "Chain", NpcMesh.Torus(l.neckR * 1.15f, 0.012f, Segs, 6), M(Gold, 0.8f), new Vector3(0f, b.shoulderY - b.hipY - 0.02f, 0.03f), new Vector3(1f, 1f, 1.15f), new Vector3(30f, 0f, 0f), false);
            Glasses(b, l, hr, Gold, H("#9FB4D8"), false, 1.0f);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(b.shoulderL.parent, "Epaulette", NpcMesh.RoundBox(0.1f, 0.025f, 0.08f, 0.6f, 5), M(Gold, 0.6f), new Vector3(s * (l.shoulderR + 0.02f), b.shoulderY - b.hipY + 0.05f, 0f), Vector3.one, default, false);
            NpcMesh.Part(b.pivot, "Belt", NpcMesh.Torus(l.waistR * 1.0f, 0.02f, Segs, 6), M(Gold, 0.6f), new Vector3(0f, (b.height - hr * 1.75f - b.hipY) * 0.3f, 0f), Vector3.one, default, false);
            if (chad) Cap(b, hr, H("#F4EFE6"), true);
            var stick = NpcMesh.Node(b.handR, "SelfieStick", new Vector3(0f, -0.05f, 0.03f), new Vector3(-20f, 0f, 0f));
            b.propR = stick;
            NpcMesh.Part(stick, "Pole", NpcMesh.Cylinder(0.01f, 0.7f, 10), M(H("#2E3140"), 0.5f), new Vector3(0f, -0.1f, 0f), Vector3.one, new Vector3(-90f, 0f, 0f), false);
            NpcMesh.Part(stick, "Phone", NpcMesh.RoundBox(0.075f, 0.14f, 0.012f, 0.4f, 5), M(H("#1E1B2B"), 0.6f), new Vector3(0f, -0.1f, 0.72f), Vector3.one, new Vector3(-90f, 0f, 0f), false);
        }

        static void Cap(NpcBody b, float hr, Color c, bool backwards)
        {
            var cap = NpcMesh.Node(b.head, "Cap", new Vector3(0f, hr * 1.12f, -0.005f), new Vector3(0f, backwards ? 180f : 0f, 0f));
            b.hat = cap;
            NpcMesh.Part(cap, "Crown", NpcMesh.Dome(10, Segs), M(c, 0.15f), Vector3.zero, new Vector3(hr * 2.1f, hr * 1.35f, hr * 2.12f));
            NpcMesh.Part(cap, "Visor", NpcMesh.Plate("visor", new[] { new Vector2(-0.11f, 0f), new Vector2(0.11f, 0f), new Vector2(0.09f, 0.1f), new Vector2(0f, 0.13f), new Vector2(-0.09f, 0.1f) }, 0.012f),
                M(c * 0.9f, 0.15f), new Vector3(0f, 0.01f, hr * 0.95f), Vector3.one, new Vector3(80f, 0f, 0f), false);
        }

        static void SunHat(NpcBody b, float hr, Color c, float wide)
        {
            var hat = NpcMesh.Node(b.head, "SunHat", new Vector3(0f, hr * 1.28f, -0.01f), new Vector3(-5f, 0f, 0f));
            b.hat = hat;
            var prof = NpcMesh.Smooth(new[] { new Vector2(hr * 1.9f * wide, -0.03f), new Vector2(hr * 1.6f * wide, 0f), new Vector2(hr * 1.02f, 0.03f), new Vector2(hr * 0.95f, 0.13f), new Vector2(hr * 0.5f, 0.17f), new Vector2(0f, 0.175f) }, 2);
            NpcMesh.Part(hat, "Straw", NpcMesh.Cached("sunhat_" + Mathf.RoundToInt(hr * 1000f) + "_" + Mathf.RoundToInt(wide * 100f), () => NpcMesh.Lathe(prof, Segs)), M(c, 0.1f), Vector3.zero, Vector3.one);
            NpcMesh.Part(hat, "Band", NpcMesh.Torus(hr * 1.0f, 0.012f, Segs, 5), M(H("#3FA9A0"), 0.12f), new Vector3(0f, 0.05f, 0f), Vector3.one, default, false);
        }

        static void Apron(NpcBody b, Look l, Color c, bool flowers)
        {
            float span = b.height - l.headR * 1.75f - b.hipY;
            float y = span * 0.15f;
            float r = TorsoRadiusAt(l, b, b.hipY + y + 0.15f);
            var ap = NpcMesh.Node(b.pivot, "Apron", new Vector3(0f, y, r * 0.86f));
            NpcMesh.Part(ap, "Cloth", NpcMesh.RoundBox(r * 1.35f, span * 0.62f, 0.02f, 0.35f, 6), M(c, 0.12f), new Vector3(0f, span * 0.12f, 0.035f), Vector3.one, new Vector3(-6f, 0f, 0f));
            if (flowers)
            {
                Material f1 = M(H("#F2C94C"), 0.12f), f2 = M(H("#F4EFE6"), 0.12f);
                for (int i = 0; i < 6; i++)
                {
                    float fx = ((i % 3) - 1) * r * 0.38f, fy = span * (0.02f + (i / 3) * 0.2f);
                    NpcMesh.Part(ap, "Flower", NpcMesh.Plate("flower", new[] { new Vector2(0f, 0.02f), new Vector2(0.006f, 0.006f), new Vector2(0.02f, 0f), new Vector2(0.006f, -0.006f), new Vector2(0f, -0.02f), new Vector2(-0.006f, -0.006f), new Vector2(-0.02f, 0f), new Vector2(-0.006f, 0.006f) }, 0.004f),
                        i % 2 == 0 ? f1 : f2, new Vector3(fx, fy, 0.05f), Vector3.one, default, false);
                }
            }
            // straps over the shoulders
            NpcMesh.Part(b.pivot, "Strap", NpcMesh.Torus(l.neckR * 1.1f, 0.01f, Segs, 5), M(c, 0.12f), new Vector3(0f, b.shoulderY - b.hipY + 0.02f, 0f), Vector3.one, default, false);
        }

        static void Pearl(NpcBody b, Look l, float hr)
        {
            // woven shawl, reading glasses on a cord, walking stick
            NpcMesh.Part(b.pivot, "Shawl", NpcMesh.Torus(l.shoulderR * 0.98f, 0.055f, Segs, 8), M(H("#3FA9A0"), 0.12f), new Vector3(0f, b.shoulderY - b.hipY - 0.01f, 0f), new Vector3(1.05f, 0.8f, 1.05f), default, false);
            NpcMesh.Part(b.pivot, "ShawlPattern", NpcMesh.Torus(l.shoulderR * 1.03f, 0.018f, Segs, 6), M(H("#F2C94C"), 0.12f), new Vector3(0f, b.shoulderY - b.hipY - 0.04f, 0f), Vector3.one, default, false);
            float chestY = b.shoulderY - b.hipY - 0.12f;
            float z = TorsoRadiusAt(l, b, b.shoulderY - 0.12f) + 0.012f;
            var cord = new[] { new Vector3(-l.neckR, chestY + 0.14f, 0.02f), new Vector3(-0.05f, chestY, z), new Vector3(0.05f, chestY, z), new Vector3(l.neckR, chestY + 0.14f, 0.02f) };
            NpcMesh.Part(b.pivot, "Cord", NpcMesh.Sweep("pearl_cord", cord, 0.004f, 0.004f, 5), M(H("#8E6C50"), 0.2f), Vector3.zero, Vector3.one, default, false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(b.pivot, "Reader", NpcMesh.Torus(0.028f, 0.005f, 16, 5), M(H("#8E6C50"), 0.3f), new Vector3(s * 0.035f, chestY - 0.01f, z + 0.004f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            var stick = NpcMesh.Node(b.handR, "Stick", new Vector3(0f, -0.03f, 0.02f));
            b.propR = stick;
            NpcMesh.Part(stick, "Cane", NpcMesh.Cylinder(0.014f, b.hipY + 0.25f, 10), M(H("#8E6C50"), 0.2f), new Vector3(0f, -b.hipY - 0.12f, 0f), Vector3.one, default, false);
            NpcMesh.Part(stick, "Knob", NpcMesh.Sphere(8, 12), M(H("#5E4436"), 0.2f), new Vector3(0f, 0.12f, 0f), Vector3.one * 0.045f, default, false);
        }

        static void Mira(NpcBody b, Look l, float hr)
        {
            Apron(b, l, H("#8E6C50"), false);
            NpcMesh.Part(b.head, "Pencil", NpcMesh.Capsule(0.008f, 0.14f, 8), M(H("#F2C94C"), 0.2f), new Vector3(hr * 0.98f, hr * 1.05f, -0.02f), Vector3.one, new Vector3(-70f, 0f, 20f), false);
            var ham = NpcMesh.Node(b.handR, "Mallet", new Vector3(0f, -0.06f, 0.02f), new Vector3(90f, 0f, 0f));
            b.propR = ham;
            NpcMesh.Part(ham, "Handle", NpcMesh.Cylinder(0.012f, 0.26f, 8), M(H("#C9B08E"), 0.2f), new Vector3(0f, -0.04f, 0f), Vector3.one, default, false);
            NpcMesh.Part(ham, "Head", NpcMesh.RoundBox(0.12f, 0.06f, 0.06f, 0.4f, 5), M(H("#8E6C50"), 0.2f), new Vector3(0f, 0.22f, 0f), Vector3.one, default, false);
        }

        static void Tomas(NpcBody b, Look l, float hr)
        {
            // net over the shoulder: a draped tube band with knots, stubble shadow on the chin
            var net = new[]
            {
                new Vector3(-0.2f, b.shoulderY - b.hipY + 0.05f, 0.12f), new Vector3(-0.05f, b.shoulderY - b.hipY - 0.2f, l.chestR + 0.03f),
                new Vector3(0.18f, b.shoulderY - b.hipY - 0.4f, l.waistR * 0.8f), new Vector3(0.28f, b.shoulderY - b.hipY - 0.55f, 0.05f),
                new Vector3(0.15f, b.shoulderY - b.hipY - 0.35f, -l.chestR - 0.03f), new Vector3(-0.15f, b.shoulderY - b.hipY - 0.05f, -0.15f)
            };
            NpcMesh.Part(b.pivot, "Net", NpcMesh.Sweep("tomas_net", net, 0.045f, 0.03f, 8), M(H("#6E9445"), 0.1f), Vector3.zero, Vector3.one, default, false);
            for (int i = 1; i < net.Length - 1; i++)
                NpcMesh.Part(b.pivot, "Float", NpcMesh.Sphere(8, 12), M(H("#EE7F2D"), 0.3f), net[i] + new Vector3(0f, -0.03f, 0f), Vector3.one * 0.06f, default, false);
            NpcMesh.Part(b.head, "Stubble", NpcMesh.Dome(8, 24), M(Color.Lerp(l.skin, H("#3A2C2A"), 0.35f), 0.08f), new Vector3(0f, hr * 0.55f, hr * 0.32f), new Vector3(hr * 1.4f, hr * 0.9f, hr * 1.25f), new Vector3(150f, 0f, 0f), false);
            SunHat(b, hr, H("#B5A07A"), 0.9f);
        }

        static void Ada(NpcBody b, Look l, float hr)
        {
            Apron(b, l, H("#EE7F2D"), true);
            var ladle = NpcMesh.Node(b.handR, "Ladle", new Vector3(0f, -0.05f, 0.03f), new Vector3(70f, 0f, 0f));
            b.propR = ladle;
            NpcMesh.Part(ladle, "Handle", NpcMesh.Cylinder(0.008f, 0.3f, 8), M(H("#C9CED6"), 0.6f), Vector3.zero, Vector3.one, default, false);
            NpcMesh.Part(ladle, "Bowl", NpcMesh.Dome(6, 14), M(H("#C9CED6"), 0.6f), new Vector3(0f, 0.31f, 0.02f), Vector3.one * 0.08f, new Vector3(180f, 0f, 0f), false);
            NpcMesh.Part(b.head, "Scarf", NpcMesh.Dome(10, Segs), M(H("#3FA9A0"), 0.12f), new Vector3(0f, hr * 1.05f, -hr * 0.12f), new Vector3(hr * 2.18f, hr * 1.45f, hr * 2.2f), new Vector3(-22f, 0f, 0f));
        }

        static void Juno(NpcBody b, Look l, float hr)
        {
            // headphones around the neck, freckles, marimba mallets in the pocket
            NpcMesh.Part(b.pivot, "PhonesBand", NpcMesh.Torus(l.neckR * 1.15f, 0.012f, Segs, 6), M(H("#1E1B2B"), 0.4f), new Vector3(0f, b.height - hr * 1.75f - b.hipY + 0.01f, 0f), Vector3.one, default, false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(b.pivot, "Cup", NpcMesh.Cylinder(0.045f, 0.035f, 18), M(H("#3FB8C4"), 0.3f), new Vector3(s * l.neckR * 1.05f, b.height - hr * 1.75f - b.hipY, 0.04f), Vector3.one, new Vector3(0f, 0f, s * 90f), false);
            Material fr = M(Color.Lerp(l.skin, H("#C94F3D"), 0.45f), 0.1f);
            float eyeY = hr * 0.98f;
            for (int i = 0; i < 8; i++)
            {
                float x = ((i % 4) - 1.5f) * 0.025f + (i < 4 ? -0.07f : 0.07f) * 0.5f;
                x += i < 4 ? -0.03f : 0.03f;
                float y = eyeY - b.eyeR * 1.6f - (i % 2) * 0.012f;
                NpcMesh.Part(b.head, "Freckle", NpcMesh.Sphere(4, 6), fr, new Vector3(x, y, FaceZ(hr, x, y) + 0.001f), Vector3.one * 0.008f, default, false);
            }
            for (int s = 0; s < 2; s++)
            {
                var mal = NpcMesh.Node(b.pivot, "Mallet", new Vector3(0.14f + s * 0.03f, 0.02f, l.bottomR * 0.85f), new Vector3(-8f, 0f, 8f));
                NpcMesh.Part(mal, "Stick", NpcMesh.Cylinder(0.006f, 0.3f, 6), M(H("#C9B08E"), 0.2f), Vector3.zero, Vector3.one, default, false);
                NpcMesh.Part(mal, "Head", NpcMesh.Sphere(8, 12), M(H("#E0607E"), 0.25f), new Vector3(0f, 0.3f, 0f), Vector3.one * 0.04f, default, false);
            }
        }

        static void Lio(NpcBody b, Look l, float hr)
        {
            // oversized FinTube T-shirt (play logo), swim goggles on the forehead
            Material white = M(H("#F4EFE6"), 0.12f);
            float y = b.shoulderY - b.hipY - 0.16f;
            float z = TorsoRadiusAt(l, b, b.shoulderY - 0.16f) + 0.004f;
            NpcMesh.Part(b.pivot, "Logo", NpcMesh.Plate("lio_logo", NpcMesh.Ellipse(0.055f, 0.04f, 18), 0.006f), white, new Vector3(0f, y, z), Vector3.one, default, false);
            NpcMesh.Part(b.pivot, "Play", NpcMesh.Plate("lio_play", new[] { new Vector2(-0.015f, -0.02f), new Vector2(0.025f, 0f), new Vector2(-0.015f, 0.02f) }, 0.006f), M(H("#E0607E"), 0.12f), new Vector3(0.003f, y, z + 0.004f), Vector3.one, new Vector3(0f, 180f, 0f), false);
            var gog = NpcMesh.Node(b.head, "Goggles", new Vector3(0f, hr * 1.38f, 0f), new Vector3(-25f, 0f, 0f));
            NpcMesh.Part(gog, "Strap", NpcMesh.Torus(hr * 0.97f, 0.01f, Segs, 5), M(H("#3FB8C4"), 0.3f), Vector3.zero, Vector3.one, default, false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(gog, "Lens", NpcMesh.Sphere(8, 14), NpcMesh.Glass(new Color(0.25f, 0.72f, 0.77f, 0.7f)), new Vector3(s * 0.05f, 0f, hr * 0.95f), new Vector3(0.05f, 0.04f, 0.03f), default, false);
        }

        static void Toni(NpcBody b, Look l, float hr)
        {
            // white beard down to the belly
            var beard = NpcMesh.Node(b.head, "Beard", new Vector3(0f, hr * 0.5f, FaceZ(hr, 0f, hr * 0.5f) - 0.04f));
            b.moustache = beard;
            NpcMesh.Part(beard, "Beard", NpcMesh.Capsule(0.14f, 0.5f, 24), M(H("#F4EFE6"), 0.15f), new Vector3(0f, -0.45f, 0.02f), new Vector3(1.1f, 1f, 0.6f), default);
            NpcMesh.Part(beard, "Moustache", NpcMesh.Capsule(0.035f, 0.2f, 12), M(H("#F4EFE6"), 0.15f), new Vector3(-0.1f, 0.03f, 0.05f), Vector3.one, new Vector3(0f, 0f, -90f), false);
            // coconut-shell headphones
            NpcMesh.Part(b.head, "Band", NpcMesh.Torus(hr * 1.02f, 0.012f, Segs, 6), M(H("#8E6C50"), 0.2f), new Vector3(0f, hr * 0.95f, 0f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(b.head, "Shell", NpcMesh.Dome(8, 18), M(H("#6B4A36"), 0.2f), new Vector3(s * hr * 1.0f, hr * 0.95f, 0f), Vector3.one * 0.13f, new Vector3(0f, 0f, -s * 90f));
            // patches on the shirt
            Patch(b, l, new Vector3(-0.1f, b.shoulderY - b.hipY - 0.2f, 0f), H("#F2C94C"), 0.04f);
            Patch(b, l, new Vector3(0.14f, b.shoulderY - b.hipY - 0.38f, 0f), H("#E0607E"), 0.035f);
            // cassette recorder on a strap
            var strap = new[] { new Vector3(-l.neckR, b.shoulderY - b.hipY + 0.05f, 0f), new Vector3(0.05f, b.shoulderY - b.hipY - 0.3f, l.chestR + 0.02f), new Vector3(0.24f, b.shoulderY - b.hipY - 0.48f, 0.06f) };
            NpcMesh.Part(b.pivot, "Strap", NpcMesh.Sweep("toni_strap", strap, 0.01f, 0.01f, 5), M(H("#5E4436"), 0.2f), Vector3.zero, Vector3.one, default, false);
            var rec = NpcMesh.Node(b.pivot, "Recorder", new Vector3(l.waistR + 0.04f, b.shoulderY - b.hipY - 0.5f, 0.06f), new Vector3(0f, 80f, 0f));
            b.extra = rec;
            NpcMesh.Part(rec, "Box", NpcMesh.RoundBox(0.2f, 0.12f, 0.06f, 0.35f, 6), M(H("#2E3140"), 0.35f), Vector3.zero, Vector3.one);
            for (int s = -1; s <= 1; s += 2)
                NpcMesh.Part(rec, "Reel", NpcMesh.Cylinder(0.022f, 0.006f, 14), M(H("#F4EFE6"), 0.3f), new Vector3(s * 0.045f, 0.01f, 0.031f), Vector3.one, new Vector3(90f, 0f, 0f), false);
        }

        static void Ukulele(NpcBody b)
        {
            var u = NpcMesh.Node(b.handL, "Ukulele", new Vector3(0.05f, -0.05f, 0.12f), new Vector3(-60f, 20f, 70f));
            b.propL = u;
            NpcMesh.Part(u, "Body", NpcMesh.Sphere(10, 18), M(H("#C9B08E"), 0.3f), Vector3.zero, new Vector3(0.17f, 0.22f, 0.06f));
            NpcMesh.Part(u, "Hole", NpcMesh.Cylinder(0.025f, 0.005f, 12), M(H("#3A2C2A"), 0.2f), new Vector3(0f, 0.02f, 0.03f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            NpcMesh.Part(u, "Neck", NpcMesh.RoundBox(0.035f, 0.26f, 0.025f, 0.4f, 4), M(H("#5E4436"), 0.3f), new Vector3(0f, 0.22f, 0f), Vector3.one, default, false);
        }
    }
}
