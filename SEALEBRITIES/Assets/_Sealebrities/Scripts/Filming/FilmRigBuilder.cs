using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Builds the procedural hand camera models, one per camera gear tier:
    /// T0 phone in a freezer bag, T1 action cam on a stick, T2 underwater camcorder,
    /// T3 pro camera in a housing with two strobe arms, T4 cinema rig.
    /// Model frame: +Z = lens direction (same as the view), +Y up, the screen faces -Z (toward the player's eye).
    /// Static parts are merged into one mesh per material; animated parts stay separate.
    public static class FilmRigBuilder
    {
        sealed class Ctx
        {
            public Transform root, stat, dyn;
            public FilmCameraRig rig;
        }

        // shared palette; FilmMaterials.Surface maps these to flat, matte toy tones (low-poly look)
        static readonly Color Neoprene = new Color(0.055f, 0.06f, 0.075f);
        static readonly Color SuitAccent = new Color(0.1f, 0.72f, 0.82f);
        static readonly Color GlovePad = new Color(0.17f, 0.17f, 0.19f);
        static readonly Color LensGlass = new Color(0.02f, 0.025f, 0.035f);
        static readonly Color Chrome = new Color(0.78f, 0.79f, 0.8f);
        static readonly Color Rubber = new Color(0.07f, 0.07f, 0.075f);
        static readonly Color AnodBlack = new Color(0.11f, 0.115f, 0.125f);
        static readonly Vector3 AlongZ = new Vector3(90f, 0f, 0f);   // rotates a +Y primitive to point along +Z

        public static FilmCameraRig Build(int tier)
        {
            tier = Mathf.Clamp(tier, 0, 4);
            var rig = new FilmCameraRig { tier = tier };
            var rootGo = new GameObject("HandCamera_T" + tier);
            var c = new Ctx { root = rootGo.transform, rig = rig };
            c.stat = Node(c.root, "Static", Vector3.zero, Vector3.zero);
            c.dyn = Node(c.root, "Dynamic", Vector3.zero, Vector3.zero);
            rig.root = rootGo;

            switch (tier)
            {
                case 0: BuildPhone(c); break;
                case 1: BuildActionCam(c); break;
                case 2: BuildCamcorder(c); break;
                case 3: BuildProHousing(c); break;
                default: BuildCinemaRig(c); break;
            }

            rig.owned.AddRange(FilmMesh.CombineStatic(c.stat, c.root));
            if (rig.zoomPart != null) rig.zoomPartBase = rig.zoomPart.localPosition;
            return rig;
        }

        public static void ConfigureRenderer(MeshRenderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            r.allowOcclusionWhenDynamic = false;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        }

        // ================================================================== helpers
        static Transform Node(Transform parent, string name, Vector3 pos, Vector3 euler)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = new GameObject(name);
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            ConfigureRenderer(r);
            return t;
        }

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 euler)
            => Part(parent, name, mesh, mat, pos, Quaternion.Euler(euler), Vector3.one);

        static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos)
            => Part(parent, name, mesh, mat, pos, Quaternion.identity, Vector3.one);

        static Mesh RB(float x, float y, float z, float r) => FilmMesh.RoundedBox(new Vector3(x, y, z), r);

        /// Cylinder (rod) between two points.
        static Transform Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material mat, int seg = 12)
        {
            Vector3 d = b - a;
            float len = d.magnitude;
            if (len < 0.0001f) return null;
            return Part(parent, name, FilmMesh.Cylinder(radius, len, seg), mat, (a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len), Vector3.one);
        }

        static Transform Ball(Transform parent, string name, Vector3 p, float radius, Material mat)
            => Part(parent, name, FilmMesh.Sphere(radius, 10), mat, p);

        /// Device screen (state material, REC dot and storage bar) on the dynamic layer plus a static glossy bezel.
        static void Screen(Ctx c, Vector3 pos, Vector3 euler, float w, float h)
        {
            Quaternion rot = Quaternion.Euler(euler);
            Part(c.stat, "Bezel", RB(w + 0.007f, h + 0.007f, 0.002f, 0.0009f), FilmMaterials.Surface(new Color(0.015f, 0.015f, 0.02f), 0.92f),
                 pos + rot * new Vector3(0f, 0f, 0.0012f), rot, Vector3.one);

            var anchor = Node(c.dyn, "Screen", pos, euler);
            var scr = Part(anchor, "Display", FilmMesh.Quad(w, h), FilmMaterials.Screen(FilmMaterials.ScreenStandby), Vector3.zero);
            c.rig.screen = scr.GetComponent<MeshRenderer>();

            // blinking "REC" group: red dot + three tiny emissive blocks standing in for the letters
            float dotR = Mathf.Max(0.0022f, h * 0.075f);
            var rec = Node(anchor, "Rec", new Vector3(-w * 0.5f + dotR * 2.2f, h * 0.5f - dotR * 2.2f, 0f), Vector3.zero);
            var recMat = FilmMaterials.RecDot;
            Part(rec, "RecDot", FilmMesh.Sphere(dotR, 10), recMat, new Vector3(0f, 0f, -0.0011f), Quaternion.identity, new Vector3(1f, 1f, 0.35f));
            for (int i = 0; i < 3; i++)
                Part(rec, "RecLetter", FilmMesh.Box(new Vector3(dotR * 1.1f, dotR * 1.6f, 0.0004f)), recMat,
                     new Vector3(dotR * (2.4f + i * 1.7f), 0f, -0.0009f));
            c.rig.recGroup = rec.gameObject;

            float barW = w * 0.8f, barH = Mathf.Max(0.0012f, h * 0.05f);
            var pivot = Node(anchor, "TapePivot", new Vector3(-barW * 0.5f, -h * 0.5f + barH * 2.2f, -0.0008f), Vector3.zero);
            var bar = Part(pivot, "TapeBar", FilmMesh.Quad(barW, barH), FilmMaterials.Tape(0), new Vector3(barW * 0.5f, 0f, 0f));
            c.rig.tapePivot = pivot;
            c.rig.tapeBar = bar.GetComponent<MeshRenderer>();
        }

        static void Tally(Ctx c, Vector3 pos, float radius)
        {
            var t = Ball(c.dyn, "Tally", pos, radius, FilmMaterials.RecDot);
            c.rig.tally = t.GetComponent<MeshRenderer>();
        }

        /// Gloved right hand gripping a vertical handle centered at 'grip'. The forearm leaves the view toward the lower right.
        static void Hand(Ctx c, Vector3 grip, Vector3 euler, float size)
        {
            var h = Node(c.stat, "Hand", grip, euler);
            h.localScale = new Vector3(size, size, size);
            var glove = FilmMaterials.Surface(Neoprene, 0.32f);
            var pad = FilmMaterials.Surface(GlovePad, 0.2f);
            var accent = FilmMaterials.Surface(SuitAccent, 0.45f);

            // back of the hand on the right side of the handle
            Part(h, "HandBack", RB(0.036f, 0.09f, 0.078f, 0.016f), glove, new Vector3(0.03f, -0.004f, 0.006f), new Vector3(0f, 0f, -5f));
            // four fingers wrapped around the far side of the handle (chunky, slightly cartoonish)
            for (int i = 0; i < 4; i++)
            {
                float y = 0.031f - i * 0.0205f;
                float len = i == 3 ? 0.058f : 0.068f;
                Part(h, "Finger", RB(len, 0.0195f, 0.027f, 0.0095f), glove, new Vector3(0.003f, y, 0.027f), new Vector3(0f, -8f, 0f));
                Part(h, "FingerTip", RB(0.021f, 0.0185f, 0.03f, 0.009f), pad, new Vector3(-0.026f + (i == 3 ? 0.006f : 0f), y, 0.012f), new Vector3(0f, 25f, 0f));
            }
            // thumb on the near side, pointing up-left
            Part(h, "Thumb", RB(0.023f, 0.062f, 0.025f, 0.0112f), glove, new Vector3(0.006f, 0.018f, -0.03f), new Vector3(0f, 0f, 24f));
            Part(h, "ThumbTip", RB(0.021f, 0.022f, 0.023f, 0.0104f), pad, new Vector3(-0.007f, 0.046f, -0.031f), new Vector3(0f, 0f, 24f));

            // wrist cuff + forearm toward the lower right / back
            Vector3 wrist = new Vector3(0.042f, -0.052f, -0.012f);
            Vector3 dir = new Vector3(0.42f, -0.62f, -0.66f).normalized;
            Quaternion along = Quaternion.FromToRotation(Vector3.up, dir);
            Part(h, "Cuff", FilmMesh.Frustum(0.037f, 0.034f, 0.03f, 16, true, false), accent, wrist + dir * 0.012f, along, Vector3.one);
            Part(h, "Forearm", FilmMesh.Frustum(0.038f, 0.044f, 0.36f, 16, true, false), glove, wrist + dir * 0.2f, along, Vector3.one);
            Part(h, "SleeveStripe", FilmMesh.Frustum(0.0445f, 0.0455f, 0.018f, 16, false, false), accent, wrist + dir * 0.13f, along, Vector3.one);
        }

        // ================================================================== T0: phone in a freezer bag
        static void BuildPhone(Ctx c)
        {
            var r = c.rig;
            r.hipPos = new Vector3(0.17f, -0.155f, 0.40f); r.hipRot = Quaternion.Euler(4f, -10f, -6f);
            r.aimPos = new Vector3(0.085f, -0.105f, 0.34f); r.aimRot = Quaternion.Euler(2f, -4f, 0f);

            var body = FilmMaterials.Surface(new Color(0.1f, 0.1f, 0.115f), 0.78f, 0.25f);
            var frame = FilmMaterials.Surface(new Color(0.58f, 0.6f, 0.64f), 0.72f, 0.85f);
            var glass = FilmMaterials.Surface(LensGlass, 0.96f, 0.3f);
            var chrome = FilmMaterials.Surface(Chrome, 0.8f, 0.9f);

            // landscape phone
            Part(c.stat, "PhoneBody", RB(0.150f, 0.072f, 0.0085f, 0.0041f), body, Vector3.zero);
            Part(c.stat, "PhoneFrame", RB(0.1512f, 0.0732f, 0.0042f, 0.002f), frame, Vector3.zero);
            // camera module on the back (+Z), on the side away from the hand
            Part(c.stat, "CamModule", RB(0.036f, 0.036f, 0.0034f, 0.0075f), body, new Vector3(-0.052f, 0.014f, 0.0055f));
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = new Vector3(-0.06f + i * 0.016f, 0.022f - i * 0.016f, 0.0076f);
                Part(c.stat, "LensRing", FilmMesh.Cylinder(0.0078f, 0.0024f, 20), chrome, p, AlongZ);
                Part(c.stat, "Lens", FilmMesh.Cylinder(0.0062f, 0.0028f, 20), glass, p + new Vector3(0f, 0f, 0.0004f), AlongZ);
            }
            Part(c.stat, "Flash", FilmMesh.Cylinder(0.0035f, 0.002f, 12), FilmMaterials.Surface(new Color(0.95f, 0.9f, 0.75f), 0.6f), new Vector3(-0.044f, 0.024f, 0.0074f), AlongZ);
            // side buttons
            Part(c.stat, "Button", RB(0.022f, 0.0035f, 0.003f, 0.0012f), frame, new Vector3(-0.03f, 0.0372f, 0f));

            Screen(c, new Vector3(0f, 0f, -0.0047f), Vector3.zero, 0.138f, 0.063f);

            // freezer bag: two crumpled transparent layers, zip strip, slider, a little trapped water
            var bag = FilmMaterials.Glass(new Color(0.9f, 0.95f, 1f, 0.16f));
            var bag2 = FilmMaterials.Glass(new Color(0.88f, 0.93f, 1f, 0.1f));
            Part(c.stat, "Bag", RB(0.186f, 0.112f, 0.03f, 0.012f), bag, new Vector3(-0.004f, 0.012f, 0f), new Vector3(0f, 0f, 1.2f));
            Part(c.stat, "BagWrinkle", RB(0.18f, 0.104f, 0.026f, 0.011f), bag2, new Vector3(-0.003f, 0.009f, 0.0005f), new Vector3(2.5f, -2f, -2.2f));
            Part(c.stat, "Zip", RB(0.19f, 0.0075f, 0.031f, 0.003f), FilmMaterials.Surface(new Color(0.18f, 0.42f, 0.95f), 0.55f), new Vector3(-0.004f, 0.068f, 0f), new Vector3(0f, 0f, 1.2f));
            Part(c.stat, "ZipSlider", RB(0.012f, 0.013f, 0.035f, 0.003f), FilmMaterials.Surface(new Color(0.92f, 0.2f, 0.14f), 0.5f), new Vector3(0.07f, 0.069f, 0f), new Vector3(0f, 0f, 1.2f));
            var water = FilmMaterials.Glass(new Color(0.75f, 0.88f, 1f, 0.35f));
            Part(c.stat, "Drop", FilmMesh.Sphere(0.0034f, 8), water, new Vector3(-0.07f, -0.036f, -0.0085f));
            Part(c.stat, "Drop", FilmMesh.Sphere(0.0026f, 8), water, new Vector3(-0.061f, -0.039f, -0.0083f));
            Part(c.stat, "Drop", FilmMesh.Sphere(0.003f, 8), water, new Vector3(0.024f, -0.04f, 0.0095f));

            Hand(c, new Vector3(0.083f, -0.006f, 0.006f), Vector3.zero, 0.92f);
        }

        // ================================================================== T1: action cam on a stick
        static void BuildActionCam(Ctx c)
        {
            var r = c.rig;
            r.hipPos = new Vector3(0.16f, -0.08f, 0.44f); r.hipRot = Quaternion.Euler(2f, -12f, -4f);
            r.aimPos = new Vector3(0.075f, -0.078f, 0.39f); r.aimRot = Quaternion.Euler(0f, -4f, 0f);

            var body = FilmMaterials.Surface(new Color(0.08f, 0.08f, 0.088f), 0.35f, 0.1f);
            var glass = FilmMaterials.Surface(LensGlass, 0.96f, 0.3f);
            var chrome = FilmMaterials.Surface(Chrome, 0.8f, 0.9f);
            var alu = FilmMaterials.Surface(new Color(0.56f, 0.58f, 0.62f), 0.55f, 0.85f);
            var plastic = FilmMaterials.Surface(new Color(0.16f, 0.16f, 0.18f), 0.4f);

            // camera
            Part(c.stat, "CamBody", RB(0.062f, 0.044f, 0.034f, 0.006f), body, Vector3.zero);
            Part(c.stat, "LensRing", FilmMesh.Cylinder(0.0128f, 0.006f, 20), chrome, new Vector3(0.014f, 0.004f, 0.019f), AlongZ);
            Part(c.stat, "Lens", FilmMesh.Cylinder(0.0105f, 0.01f, 20), glass, new Vector3(0.014f, 0.004f, 0.022f), AlongZ);
            Part(c.stat, "Button", FilmMesh.Cylinder(0.004f, 0.004f, 12), FilmMaterials.Surface(new Color(0.85f, 0.15f, 0.12f), 0.5f), new Vector3(-0.016f, 0.0235f, 0f), Vector3.zero);
            Screen(c, new Vector3(0f, 0f, -0.0175f), Vector3.zero, 0.044f, 0.031f);
            Tally(c, new Vector3(-0.022f, 0.016f, 0.0172f), 0.0026f);

            // clear dive housing
            var housing = FilmMaterials.Glass(new Color(0.85f, 0.92f, 1f, 0.17f));
            Part(c.stat, "Housing", RB(0.077f, 0.059f, 0.052f, 0.008f), housing, new Vector3(0f, -0.002f, 0.002f));
            Part(c.stat, "PortGlass", FilmMesh.Cylinder(0.016f, 0.005f, 20), FilmMaterials.Glass(new Color(0.1f, 0.15f, 0.2f, 0.45f)), new Vector3(0.014f, 0.004f, 0.0305f), AlongZ);
            Part(c.stat, "Latch", RB(0.032f, 0.011f, 0.022f, 0.004f), plastic, new Vector3(0f, 0.031f, -0.004f));
            Part(c.stat, "Mount", RB(0.024f, 0.02f, 0.02f, 0.003f), plastic, new Vector3(0f, -0.04f, 0f));
            Part(c.stat, "ThumbScrew", FilmMesh.Cylinder(0.0065f, 0.034f, 12), FilmMaterials.Surface(new Color(1f, 0.45f, 0.1f), 0.45f), new Vector3(0f, -0.043f, 0f), new Vector3(0f, 0f, 90f));

            // telescopic stick down to the hand
            Vector3 top = new Vector3(0f, -0.05f, 0f), bottom = new Vector3(0.012f, -0.31f, -0.042f);
            Rod(c.stat, "Stick1", top, Vector3.Lerp(top, bottom, 0.38f), 0.0072f, alu);
            Rod(c.stat, "Stick2", Vector3.Lerp(top, bottom, 0.36f), Vector3.Lerp(top, bottom, 0.7f), 0.0088f, alu);
            Rod(c.stat, "Stick3", Vector3.Lerp(top, bottom, 0.68f), bottom, 0.0104f, alu);
            Rod(c.stat, "Collar", Vector3.Lerp(top, bottom, 0.355f), Vector3.Lerp(top, bottom, 0.385f), 0.0098f, plastic);
            Rod(c.stat, "Collar", Vector3.Lerp(top, bottom, 0.675f), Vector3.Lerp(top, bottom, 0.705f), 0.0114f, plastic);
            // bright yellow floating grip (it floats if you drop it!)
            Vector3 g0 = Vector3.Lerp(top, bottom, 0.74f), g1 = Vector3.Lerp(top, bottom, 1.04f);
            Rod(c.stat, "FloatGrip", g0, g1, 0.0175f, FilmMaterials.Surface(new Color(1f, 0.8f, 0.1f), 0.15f), 16);
            Rod(c.stat, "Strap", g1, g1 + new Vector3(0.02f, -0.035f, -0.01f), 0.0035f, plastic, 8);

            Vector3 dir = (top - bottom).normalized;
            Vector3 gripCenter = Vector3.Lerp(g0, g1, 0.55f);
            Hand(c, gripCenter, Quaternion.FromToRotation(Vector3.up, dir).eulerAngles, 0.95f);
        }

        // ================================================================== T2: underwater camcorder
        static void BuildCamcorder(Ctx c)
        {
            var r = c.rig;
            r.hipPos = new Vector3(0.19f, -0.17f, 0.47f); r.hipRot = Quaternion.Euler(3f, -10f, -5f);
            r.aimPos = new Vector3(0.1f, -0.13f, 0.42f); r.aimRot = Quaternion.Euler(1f, -5f, 0f);

            var yellow = FilmMaterials.Surface(new Color(1f, 0.74f, 0.08f), 0.55f);
            var rubber = FilmMaterials.Surface(Rubber, 0.25f);
            var glass = FilmMaterials.Surface(LensGlass, 0.96f, 0.3f);
            var chrome = FilmMaterials.Surface(Chrome, 0.8f, 0.9f);

            Part(c.stat, "Body", RB(0.085f, 0.085f, 0.17f, 0.016f), yellow, Vector3.zero);
            Part(c.stat, "RubberBand", RB(0.0875f, 0.048f, 0.124f, 0.01f), rubber, new Vector3(0f, -0.012f, 0.004f));
            Part(c.stat, "PortRing", FilmMesh.Cylinder(0.037f, 0.03f, 24), rubber, new Vector3(0f, 0.004f, 0.094f), AlongZ);

            // lens barrel extends with zoom
            var barrel = Part(c.dyn, "ZoomBarrel", FilmMesh.Cylinder(0.031f, 0.028f, 24), rubber, new Vector3(0f, 0.004f, 0.106f), AlongZ);
            Part(barrel, "BarrelRing", FilmMesh.Cylinder(0.0315f, 0.004f, 24), chrome, new Vector3(0f, 0.01f, 0f), Vector3.zero);
            Part(barrel, "PortGlass", FilmMesh.Cylinder(0.027f, 0.004f, 24), glass, new Vector3(0f, 0.0145f, 0f), Vector3.zero);
            r.zoomPart = barrel;
            r.zoomTravel = 0.012f;

            // top carry handle
            Part(c.stat, "Handle", RB(0.022f, 0.018f, 0.118f, 0.008f), rubber, new Vector3(0f, 0.07f, -0.004f));
            Part(c.stat, "HandlePost", RB(0.016f, 0.03f, 0.016f, 0.005f), rubber, new Vector3(0f, 0.054f, 0.046f));
            Part(c.stat, "HandlePost", RB(0.016f, 0.03f, 0.016f, 0.005f), rubber, new Vector3(0f, 0.054f, -0.054f));
            Part(c.stat, "ZoomRocker", RB(0.012f, 0.008f, 0.026f, 0.003f), chrome, new Vector3(0.01f, 0.081f, 0.03f));
            Part(c.stat, "RecButton", FilmMesh.Cylinder(0.0085f, 0.006f, 16), FilmMaterials.Surface(new Color(0.85f, 0.12f, 0.1f), 0.5f), new Vector3(0.026f, -0.022f, -0.087f), AlongZ);
            Part(c.stat, "Badge", RB(0.03f, 0.008f, 0.002f, 0.001f), chrome, new Vector3(-0.02f, 0.026f, 0.0855f));

            // flip-out screen on the left, facing the player
            Part(c.stat, "Hinge", FilmMesh.Cylinder(0.005f, 0.052f, 12), chrome, new Vector3(-0.047f, 0.012f, -0.044f), Vector3.zero);
            Part(c.stat, "ScreenPanel", RB(0.08f, 0.054f, 0.009f, 0.003f), rubber, new Vector3(-0.088f, 0.012f, -0.046f), new Vector3(0f, -4f, 0f));
            Screen(c, new Vector3(-0.088f, 0.012f, -0.0512f), new Vector3(0f, -4f, 0f), 0.068f, 0.044f);
            Tally(c, new Vector3(-0.022f, 0.032f, 0.084f), 0.0034f);

            // hand strap on the right side
            Part(c.stat, "HandStrap", RB(0.014f, 0.062f, 0.12f, 0.006f), rubber, new Vector3(0.05f, -0.004f, -0.006f));
            Hand(c, new Vector3(0.064f, -0.012f, -0.012f), Vector3.zero, 1f);
        }

        // ================================================================== T3: pro camera in a housing with strobe arms
        static void BuildProHousing(Ctx c)
        {
            var r = c.rig;
            r.hipPos = new Vector3(0.22f, -0.225f, 0.56f); r.hipRot = Quaternion.Euler(4f, -9f, -4f);
            r.aimPos = new Vector3(0.1f, -0.205f, 0.5f); r.aimRot = Quaternion.Euler(2f, -4f, 0f);

            var housing = FilmMaterials.Surface(new Color(0.16f, 0.19f, 0.24f), 0.6f, 0.75f);
            var rubber = FilmMaterials.Surface(Rubber, 0.25f);
            var anod = FilmMaterials.Surface(AnodBlack, 0.55f, 0.7f);
            var chrome = FilmMaterials.Surface(Chrome, 0.8f, 0.9f);
            var orange = FilmMaterials.Surface(new Color(1f, 0.45f, 0.1f), 0.5f, 0.3f);

            Part(c.stat, "Housing", RB(0.2f, 0.145f, 0.115f, 0.02f), housing, Vector3.zero);
            Part(c.stat, "AccentBand", RB(0.202f, 0.007f, 0.117f, 0.0032f), orange, new Vector3(0f, -0.045f, 0f));
            Part(c.stat, "PortBase", FilmMesh.Cylinder(0.074f, 0.022f, 28), rubber, new Vector3(0f, 0f, 0.067f), AlongZ);

            // dome port with the lens behind it (lens moves with zoom)
            Part(c.stat, "Dome", FilmMesh.Sphere(0.07f, 22, true), FilmMaterials.Glass(new Color(0.75f, 0.85f, 0.95f, 0.2f)), new Vector3(0f, 0f, 0.077f), AlongZ);
            var lens = Part(c.dyn, "Lens", FilmMesh.Cylinder(0.036f, 0.05f, 24), anod, new Vector3(0f, 0f, 0.078f), AlongZ);
            Part(lens, "LensGlass", FilmMesh.Cylinder(0.03f, 0.004f, 24), FilmMaterials.Surface(LensGlass, 0.97f, 0.3f), new Vector3(0f, 0.026f, 0f), Vector3.zero);
            r.zoomPart = lens;
            r.zoomTravel = 0.014f;

            // controls on top/back
            Part(c.stat, "Knob", FilmMesh.Cylinder(0.01f, 0.014f, 14), chrome, new Vector3(-0.06f, 0.079f, -0.02f), Vector3.zero);
            Part(c.stat, "Knob", FilmMesh.Cylinder(0.01f, 0.014f, 14), chrome, new Vector3(0.06f, 0.079f, -0.02f), Vector3.zero);
            Part(c.stat, "Dial", FilmMesh.Cylinder(0.015f, 0.01f, 18), anod, new Vector3(0.045f, 0.077f, 0.025f), Vector3.zero);
            Part(c.stat, "VacuumLED", FilmMesh.Sphere(0.003f, 8), FilmMaterials.Glow(new Color(0.2f, 1f, 0.4f), 2.5f), new Vector3(-0.078f, 0.05f, -0.058f));
            Part(c.stat, "Lever", RB(0.012f, 0.03f, 0.01f, 0.004f), chrome, new Vector3(0.088f, 0.02f, -0.06f), new Vector3(0f, 0f, -20f));
            Screen(c, new Vector3(-0.012f, 0.012f, -0.0588f), Vector3.zero, 0.11f, 0.07f);
            Tally(c, new Vector3(0.078f, 0.058f, 0.058f), 0.004f);

            // handles + articulated strobe arms
            for (int s = -1; s <= 1; s += 2)
            {
                Part(c.stat, "HandleBar", RB(0.06f, 0.02f, 0.03f, 0.006f), anod, new Vector3(s * 0.12f, -0.066f, 0f));
                Part(c.stat, "Handle", RB(0.032f, 0.125f, 0.04f, 0.014f), rubber, new Vector3(s * 0.152f, -0.03f, 0f));
                Part(c.stat, "HandleCap", FilmMesh.Cylinder(0.013f, 0.012f, 14), chrome, new Vector3(s * 0.152f, 0.038f, 0f), Vector3.zero);

                Vector3 j0 = new Vector3(s * 0.152f, 0.05f, 0f);
                Vector3 j1 = new Vector3(s * 0.21f, 0.105f, 0.02f);
                Vector3 j2 = new Vector3(s * 0.245f, 0.12f, 0.075f);
                Ball(c.stat, "Joint", j0, 0.0145f, anod);
                Rod(c.stat, "Arm", j0, j1, 0.0105f, anod);
                Ball(c.stat, "Joint", j1, 0.0155f, anod);
                Rod(c.stat, "Arm", j1, j2, 0.0105f, anod);
                Ball(c.stat, "Joint", j2, 0.0145f, anod);
                Part(c.stat, "StrobeHead", FilmMesh.Cylinder(0.031f, 0.055f, 22), FilmMaterials.Surface(new Color(0.13f, 0.13f, 0.14f), 0.45f, 0.4f), j2 + new Vector3(0f, 0f, 0.035f), AlongZ);
                Part(c.stat, "StrobeRing", FilmMesh.Cylinder(0.0325f, 0.008f, 22), orange, j2 + new Vector3(0f, 0f, 0.058f), AlongZ);
                var glow = Part(c.dyn, "StrobeDiffuser", FilmMesh.Cylinder(0.028f, 0.004f, 22), FilmMaterials.LampDiffuser(false), j2 + new Vector3(0f, 0f, 0.0635f), AlongZ);
                r.lampGlows.Add(glow.GetComponent<MeshRenderer>());
            }

            Hand(c, new Vector3(0.152f, -0.03f, 0f), Vector3.zero, 1.05f);
        }

        // ================================================================== T4: cinema rig
        static void BuildCinemaRig(Ctx c)
        {
            var r = c.rig;
            r.hipPos = new Vector3(0.25f, -0.275f, 0.64f); r.hipRot = Quaternion.Euler(4f, -8f, -4f);
            r.aimPos = new Vector3(0.115f, -0.25f, 0.58f); r.aimRot = Quaternion.Euler(2f, -4f, 0f);

            var red = FilmMaterials.Surface(new Color(0.55f, 0.07f, 0.06f), 0.55f, 0.65f);
            var black = FilmMaterials.Surface(new Color(0.05f, 0.05f, 0.055f), 0.35f, 0.2f);
            var rubber = FilmMaterials.Surface(Rubber, 0.25f);
            var anod = FilmMaterials.Surface(AnodBlack, 0.55f, 0.7f);
            var chrome = FilmMaterials.Surface(Chrome, 0.8f, 0.9f);
            var foam = FilmMaterials.Surface(new Color(1f, 0.8f, 0.1f), 0.18f);
            var white = FilmMaterials.Surface(new Color(0.92f, 0.92f, 0.9f), 0.5f);

            Part(c.stat, "Housing", RB(0.24f, 0.2f, 0.26f, 0.03f), red, Vector3.zero);
            Part(c.stat, "BackPlate", RB(0.22f, 0.18f, 0.02f, 0.008f), black, new Vector3(0f, 0f, -0.134f));
            Part(c.stat, "Stripe", RB(0.242f, 0.012f, 0.18f, 0.005f), white, new Vector3(0f, -0.06f, 0.02f));
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.1f, y = (i < 2 ? -1f : 1f) * 0.08f;
                Part(c.stat, "Latch", RB(0.018f, 0.02f, 0.03f, 0.005f), chrome, new Vector3(x, y, -0.128f));
            }

            Part(c.stat, "PortRing", FilmMesh.Cylinder(0.096f, 0.03f, 32), black, new Vector3(0f, -0.005f, 0.144f), AlongZ);
            var barrel = Part(c.dyn, "LensBarrel", FilmMesh.Cylinder(0.085f, 0.075f, 32), rubber, new Vector3(0f, -0.005f, 0.18f), AlongZ);
            Part(barrel, "FocusRing", FilmMesh.Cylinder(0.0875f, 0.016f, 32), chrome, new Vector3(0f, 0.012f, 0f), Vector3.zero);
            Part(barrel, "PortGlass", FilmMesh.Cylinder(0.078f, 0.004f, 32), FilmMaterials.Glass(new Color(0.1f, 0.15f, 0.22f, 0.5f)), new Vector3(0f, 0.0385f, 0f), Vector3.zero);
            r.zoomPart = barrel;
            r.zoomTravel = 0.016f;
            // square matte box (open funnel)
            Part(c.stat, "MatteBox", FilmMesh.Frustum(0.105f, 0.155f, 0.09f, 4, false, true), FilmMaterials.SurfaceTwoSided(new Color(0.04f, 0.04f, 0.045f), 0.2f), new Vector3(0f, -0.005f, 0.27f), AlongZ);

            // top handle
            Part(c.stat, "TopHandle", RB(0.03f, 0.026f, 0.2f, 0.01f), black, new Vector3(0f, 0.152f, 0f));
            Part(c.stat, "TopPost", RB(0.022f, 0.045f, 0.022f, 0.006f), anod, new Vector3(0f, 0.118f, 0.08f));
            Part(c.stat, "TopPost", RB(0.022f, 0.045f, 0.022f, 0.006f), anod, new Vector3(0f, 0.118f, -0.08f));

            // side handles + LED panels on arms with yellow float collars
            for (int s = -1; s <= 1; s += 2)
            {
                Part(c.stat, "HandleBar", RB(0.07f, 0.024f, 0.035f, 0.007f), anod, new Vector3(s * 0.15f, -0.08f, 0f));
                Part(c.stat, "Handle", RB(0.035f, 0.135f, 0.044f, 0.0155f), rubber, new Vector3(s * 0.19f, -0.045f, 0f));
                Part(c.stat, "HandleCap", FilmMesh.Cylinder(0.014f, 0.012f, 14), chrome, new Vector3(s * 0.19f, 0.028f, 0f), Vector3.zero);

                Vector3 j0 = new Vector3(s * 0.19f, 0.042f, 0f);
                Vector3 j1 = new Vector3(s * 0.25f, 0.11f, 0.035f);
                Vector3 j2 = new Vector3(s * 0.275f, 0.135f, 0.07f);
                Ball(c.stat, "Joint", j0, 0.016f, anod);
                Rod(c.stat, "Arm", j0, j1, 0.012f, anod);
                Rod(c.stat, "Float", Vector3.Lerp(j0, j1, 0.25f), Vector3.Lerp(j0, j1, 0.75f), 0.026f, foam, 16);
                Ball(c.stat, "Joint", j1, 0.017f, anod);
                Rod(c.stat, "Arm", j1, j2, 0.012f, anod);
                Ball(c.stat, "Joint", j2, 0.016f, anod);
                Vector3 panel = j2 + new Vector3(s * 0.005f, 0.02f, 0.03f);
                Part(c.stat, "LEDPanel", RB(0.12f, 0.066f, 0.03f, 0.008f), FilmMaterials.Surface(new Color(0.18f, 0.18f, 0.2f), 0.45f, 0.5f), panel);
                var glow = Part(c.dyn, "LEDDiffuser", FilmMesh.Box(new Vector3(0.106f, 0.052f, 0.004f)), FilmMaterials.LampDiffuser(false), panel + new Vector3(0f, 0f, 0.0155f));
                r.lampGlows.Add(glow.GetComponent<MeshRenderer>());
            }

            // monitor on an arm, right of center, with sun hood
            Vector3 mon = new Vector3(0.07f, 0.205f, -0.1f);
            Rod(c.stat, "MonitorArm", new Vector3(0f, 0.165f, -0.06f), mon + new Vector3(-0.03f, -0.04f, 0.02f), 0.009f, anod);
            Ball(c.stat, "Joint", mon + new Vector3(-0.03f, -0.04f, 0.02f), 0.013f, anod);
            Part(c.stat, "Monitor", RB(0.17f, 0.105f, 0.03f, 0.008f), black, mon);
            Part(c.stat, "HoodTop", RB(0.172f, 0.004f, 0.045f, 0.0015f), black, mon + new Vector3(0f, 0.054f, -0.03f));
            Part(c.stat, "HoodSide", RB(0.004f, 0.107f, 0.045f, 0.0015f), black, mon + new Vector3(-0.086f, 0f, -0.03f));
            Part(c.stat, "HoodSide", RB(0.004f, 0.107f, 0.045f, 0.0015f), black, mon + new Vector3(0.086f, 0f, -0.03f));
            Screen(c, mon + new Vector3(0f, 0f, -0.0165f), Vector3.zero, 0.15f, 0.088f);
            Tally(c, new Vector3(-0.09f, 0.075f, 0.131f), 0.005f);
            Part(c.stat, "RecButton", FilmMesh.Cylinder(0.011f, 0.008f, 16), FilmMaterials.Surface(new Color(0.9f, 0.1f, 0.08f), 0.5f), new Vector3(0.07f, -0.05f, -0.146f), AlongZ);

            Hand(c, new Vector3(0.19f, -0.045f, 0f), Vector3.zero, 1.1f);
        }
    }
}
