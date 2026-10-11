using UnityEngine;

namespace Sealebrities
{
    /// Visible equipment on the bean diver (Q51, Design_Runde2 H) – built by DiverBuilder for co-op avatars, the menu
    /// crew, the selfie view and the character preview:
    ///  * tank by tier: t0 none (snorkel set), t1 small dented grey with duct tape, t2 12 L steel blue, t3 twin grey,
    ///    t4 nitrox (light body, yellow + green band), t5 trimix (twin black with orange bands) – bigger tier = bigger rig;
    ///  * fins by tier: t0 squeaky yellow pool fins, t1 split teal, t2 long carbon black with rails, t3 carbon + a dive
    ///    scooter under the body (only shown while swimming); a fin colour picked on the character screen wins;
    ///  * the dive lamp on the left chest strap and the camera on a lanyard at the chest while they are not in a hand
    ///    (DiverPuppet toggles them every frame from the held items).
    /// Legacy looks (lookVersion 0: old peers, default crew looks) keep the old steel tank and dark fins.
    /// Smooth-shaded parts only (characters are soft, the world stays faceted – art bible §1.12).
    public static class DiverGear
    {
        // ------------------------------------------------------------------ palette (Q1/Q51)
        /// Neoprene gloves everywhere (first-person mittens, phone hand, avatar mittens) – Q1/Q2, lighter than the suit.
        public static readonly Color Glove = LowPoly.Hex("#4A4F66");
        public static readonly Color GlovePad = LowPoly.Hex("#3A3E52");

        static readonly Color StrapDark = LowPoly.Hex("#2E3140");
        static readonly Color TankDented = LowPoly.Hex("#9AA0A8");
        static readonly Color TankDent = LowPoly.Hex("#7E848D");
        static readonly Color DuctTape = LowPoly.Hex("#C1C7CE");
        static readonly Color TankSteel = LowPoly.Hex("#4F7FAE");
        static readonly Color TankTwin = LowPoly.Hex("#8E949C");
        static readonly Color TankNitrox = LowPoly.Hex("#DDE2E6");
        static readonly Color NitroxYellow = LowPoly.Hex("#F2C94C");
        static readonly Color NitroxGreen = LowPoly.Hex("#6E9445");
        static readonly Color TankTrimix = LowPoly.Hex("#2E3140");
        static readonly Color TrimixOrange = LowPoly.Hex("#EE7F2D");
        static readonly Color Valve = LowPoly.Hex("#3A3E52");
        static readonly Color ValveChrome = LowPoly.Hex("#B8BEC6");

        static readonly Color FinSqueaky = LowPoly.Hex("#F2C94C");
        static readonly Color FinSplit = LowPoly.Hex("#3FB8C4");
        static readonly Color FinCarbon = LowPoly.Hex("#2B2533");
        static readonly Color FinRail = LowPoly.Hex("#4A4F66");

        static readonly Color LampYellow = LowPoly.Hex("#F2C94C");
        static readonly Color LampHead = LowPoly.Hex("#2E3140");
        static readonly Color LampLens = LowPoly.Hex("#FFF1C9");
        static readonly Color CamBody = LowPoly.Hex("#F2C94C");
        static readonly Color CamDark = LowPoly.Hex("#2E3140");
        static readonly Color Cord = LowPoly.Hex("#1E1B2B");
        static readonly Color ScooterBody = LowPoly.Hex("#F2C94C");
        static readonly Color ScooterDark = LowPoly.Hex("#2E3140");
        static readonly Color ScooterStripe = LowPoly.Hex("#EE7F2D");

        static Material M(Color c) => LowPoly.Mat(c);

        /// Tank tier to draw: legacy looks (lookVersion 0, no gear info) keep the classic steel tank.
        public static int TankTierOf(DiverLook look) =>
            look.lookVersion < 1 && look.tankTier <= 0 ? 2 : Mathf.Clamp(look.tankTier, 0, 5);

        public static int FinTierOf(DiverLook look) => Mathf.Clamp(look.finTier, 0, 3);

        /// Body surface radius of the bean at an absolute height (matches DiverBuilder's body profile).
        static float BodyRadius(float y) => DiverBuilder.BodyRadiusAt(y);

        /// Distance of the tank surface behind the body axis (round 2b: the rounder bean is ~2 cm wider at the back).
        const float BackGap = 0.268f;

        static float FrontZ(float y, float x)
        {
            float r = BodyRadius(y);
            return Mathf.Sqrt(Mathf.Max(0.0001f, r * r - x * x));
        }

        // ------------------------------------------------------------------ back: harness, tank(s), strap items, scooter
        /// Harness, tank(s) by tier, the strap lamp + strap camera (toggled by DiverPuppet) and the dive scooter.
        public static void BuildBack(DiverPuppet p, DiverLook look, Material suit)
        {
            Transform pv = p.pivot;
            float py = DiverBuilder.PivotY;
            int tank = TankTierOf(look);
            Material strap = M(StrapDark);

            if (tank > 0)
            {
                // BCD straps: chest band + waist band (the bean has no shoulders – no straps over the head)
                // straps hug the (round 2b, rounder) bean: radius from DiverBuilder's body profile
                DiverMeshes.Part(pv, "Harness", DiverMeshes.Torus(BodyRadius(1.24f) + 0.004f, 0.016f, 20, 4), strap, new Vector3(0f, 1.24f - py, 0f), Vector3.one);
                DiverMeshes.Part(pv, "WaistBand", DiverMeshes.Torus(BodyRadius(0.97f) + 0.004f, 0.014f, 20, 4), strap, new Vector3(0f, 0.97f - py, 0f), new Vector3(1f, 1.2f, 1f), default, false);
                DiverMeshes.Part(pv, "Buckle", DiverMeshes.Sphere(3, 6), M(ValveChrome), new Vector3(0f, 0.97f - py, FrontZ(0.97f, 0f) + 0.012f), new Vector3(0.05f, 0.032f, 0.018f), default, false);
            }

            switch (tank)
            {
                case 1:   // scrap tank: small, dented, duct tape
                {
                    float r = 0.09f, len = 0.5f, z = -(BackGap + r);
                    Single(pv, "Tank", r, len, 0.93f - py, 0f, z, M(TankDented));
                    var dent = DiverMeshes.Part(pv, "Dent", DiverMeshes.Sphere(3, 6), M(TankDent),
                        new Vector3(r * 0.72f, 1.16f - py, z - r * 0.62f), new Vector3(0.06f, 0.085f, 0.022f), new Vector3(0f, 130f, 0f), false);
                    dent.receiveShadows = true;
                    DiverMeshes.Part(pv, "Tape", DiverMeshes.Torus(r + 0.002f, 0.009f, 16, 3), M(DuctTape), new Vector3(0f, 1.26f - py, z), new Vector3(1f, 2.2f, 1f), new Vector3(0f, 0f, 6f), false);
                    ValveOn(pv, "Valve", 0f, 0.93f + len - 0.02f - py, z, M(Valve), M(StrapDark));
                    break;
                }
                case 2:   // 12 L steel, blue, band in the suit colour
                {
                    float r = 0.105f, len = 0.56f, z = -(BackGap + r);
                    Single(pv, "Tank", r, len, 0.92f - py, 0f, z, M(TankSteel));
                    DiverMeshes.Part(pv, "TankBand", DiverMeshes.Torus(r + 0.003f, 0.012f, 16, 3), suit, new Vector3(0f, 1.32f - py, z), Vector3.one);
                    ValveOn(pv, "Valve", 0f, 0.92f + len - 0.02f - py, z, M(Valve), M(ValveChrome));
                    break;
                }
                case 3:   // twin grey with a manifold
                {
                    float r = 0.085f, len = 0.54f, z = -(BackGap + r), x = 0.092f;
                    Material body = M(TankTwin), band = M(StrapDark);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Single(pv, s < 0 ? "TankL" : "TankR", r, len, 0.93f - py, s * x, z, body);
                        DiverMeshes.Part(pv, "Band", DiverMeshes.Torus(r + 0.003f, 0.011f, 16, 3), band, new Vector3(s * x, 1.05f - py, z), Vector3.one, default, false);
                        DiverMeshes.Part(pv, "Band", DiverMeshes.Torus(r + 0.003f, 0.011f, 16, 3), band, new Vector3(s * x, 1.3f - py, z), Vector3.one, default, false);
                        ValveOn(pv, "Valve", s * x, 0.93f + len - 0.02f - py, z, M(Valve), M(ValveChrome));
                    }
                    Bar(pv, "Manifold", new Vector3(-x, 0.93f + len + 0.045f - py, z), new Vector3(x, 0.93f + len + 0.045f - py, z), 0.016f, M(ValveChrome));
                    break;
                }
                case 4:   // nitrox: light body, yellow + green band
                {
                    float r = 0.11f, len = 0.6f, z = -(BackGap + r);
                    Single(pv, "Tank", r, len, 0.9f - py, 0f, z, M(TankNitrox));
                    DiverMeshes.Part(pv, "Yellow", DiverMeshes.Taper(r + 0.004f, r + 0.004f, 16, false), M(NitroxYellow), new Vector3(0f, 1.3f - py, z), new Vector3(1f, 0.085f, 1f), default, false);
                    DiverMeshes.Part(pv, "Green", DiverMeshes.Taper(r + 0.004f, r + 0.004f, 16, false), M(NitroxGreen), new Vector3(0f, 1.27f - py, z), new Vector3(1f, 0.022f, 1f), default, false);
                    ValveOn(pv, "Valve", 0f, 0.9f + len - 0.02f - py, z, M(Valve), M(NitroxYellow));
                    break;
                }
                case 5:   // trimix: twin black, orange bands, the big rig
                {
                    float r = 0.095f, len = 0.62f, z = -(BackGap + r), x = 0.102f;
                    Material body = M(TankTrimix), orange = M(TrimixOrange);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Single(pv, s < 0 ? "TankL" : "TankR", r, len, 0.88f - py, s * x, z, body);
                        DiverMeshes.Part(pv, "Band", DiverMeshes.Torus(r + 0.003f, 0.013f, 16, 3), orange, new Vector3(s * x, 1.0f - py, z), Vector3.one, default, false);
                        DiverMeshes.Part(pv, "Band", DiverMeshes.Torus(r + 0.003f, 0.013f, 16, 3), orange, new Vector3(s * x, 1.34f - py, z), Vector3.one, default, false);
                        ValveOn(pv, "Valve", s * x, 0.88f + len - 0.02f - py, z, M(Valve), orange);
                    }
                    Bar(pv, "Manifold", new Vector3(-x, 0.88f + len + 0.045f - py, z), new Vector3(x, 0.88f + len + 0.045f - py, z), 0.017f, M(ValveChrome));
                    break;
                }
            }

            BuildStrapLamp(p, py);
            BuildStrapCamera(p, py);
            if (FinTierOf(look) >= 3) BuildScooter(p, py);
        }

        /// One upright tank: capsule from y0 (pivot space) with radius r and length len.
        static void Single(Transform pv, string name, float r, float len, float y0, float x, float z, Material m)
        {
            DiverMeshes.Part(pv, name, DiverMeshes.Capsule(r, len, 12), m, new Vector3(x, y0, z), Vector3.one);
            // a soft neck where the valve sits
            DiverMeshes.Part(pv, name + "Neck", DiverMeshes.Taper(r * 0.42f, r * 0.3f, 10), m, new Vector3(x, y0 + len - r * 0.25f, z), new Vector3(1f, 0.05f, 1f), default, false);
        }

        static void ValveOn(Transform pv, string name, float x, float y, float z, Material body, Material knob)
        {
            DiverMeshes.Part(pv, name, DiverMeshes.Capsule(0.03f, 0.085f, 8), body, new Vector3(x, y, z), Vector3.one, default, false);
            DiverMeshes.Part(pv, name + "Knob", DiverMeshes.Torus(0.024f, 0.009f, 8, 3), knob, new Vector3(x, y + 0.075f, z), Vector3.one, default, false);
            // first stage pointing to the right shoulder
            DiverMeshes.Part(pv, name + "Stage", DiverMeshes.Capsule(0.019f, 0.07f, 6), body, new Vector3(x + 0.02f, y + 0.045f, z), Vector3.one, new Vector3(0f, 0f, -90f), false);
        }

        /// Capsule between two pivot-space points.
        static MeshRenderer Bar(Transform parent, string name, Vector3 a, Vector3 b, float r, Material m)
        {
            Vector3 d = b - a;
            float len = Mathf.Max(0.001f, d.magnitude);
            Quaternion q = Quaternion.FromToRotation(Vector3.up, d / len);
            var part = DiverMeshes.Part(parent, name, DiverMeshes.Capsule(r, len + r * 2f, 6), m, a - (d / len) * r, Vector3.one, default, false);
            part.transform.localRotation = q;
            return part;
        }

        /// The dive lamp clipped to the left side of the chest strap (shown while it is not in a hand).
        static void BuildStrapLamp(DiverPuppet p, float py)
        {
            const float y = 1.29f, x = -0.14f;
            float z = FrontZ(y, x);
            var root = DiverMeshes.Node(p.pivot, "StrapLamp", new Vector3(x, y - py, z + 0.012f), new Vector3(-12f, -28f, 0f));
            DiverMeshes.Part(root, "Clip", DiverMeshes.Capsule(0.012f, 0.05f, 10), M(StrapDark), new Vector3(0f, -0.03f, -0.006f), Vector3.one, default, false);
            DiverMeshes.Part(root, "Body", DiverMeshes.Capsule(0.02f, 0.12f, 16), M(LampYellow), new Vector3(0f, -0.06f, 0.016f), Vector3.one, default, false);
            DiverMeshes.Part(root, "Head", DiverMeshes.Taper(0.021f, 0.027f, 18), M(LampHead), new Vector3(0f, 0.045f, 0.016f), new Vector3(1f, 0.035f, 1f), default, false);
            DiverMeshes.Part(root, "Lens", DiverMeshes.Sphere(6, 16), MaterialLibrary.Emissive(LampLens, LowPoly.Hex("#FFD9A0"), 0.4f), new Vector3(0f, 0.08f, 0.016f), new Vector3(0.046f, 0.012f, 0.046f), default, false);
            p.strapLamp = root;
            root.gameObject.SetActive(false);
        }

        /// The hand camera hanging from a lanyard in front of the chest (shown while it is not in a hand).
        static void BuildStrapCamera(DiverPuppet p, float py)
        {
            const float y = 1.08f, x = 0.07f;
            float z = FrontZ(y, x);
            var root = DiverMeshes.Node(p.pivot, "StrapCamera", new Vector3(x, y - py, z + 0.03f), new Vector3(8f, 0f, -4f));
            DiverMeshes.Part(root, "Body", DiverMeshes.RoundedSlab("strapcam", 0.085f, 0.064f, 0.048f, 0.012f, 3), M(CamBody), Vector3.zero, Vector3.one, default, false)
                .sharedMaterials = new[] { M(CamBody), M(CamBody) };
            DiverMeshes.Part(root, "Lens", DiverMeshes.Capsule(0.019f, 0.04f, 16), M(CamDark), new Vector3(0.012f, 0f, 0.012f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(root, "LensRing", DiverMeshes.Torus(0.019f, 0.004f, 18, 5), M(ScooterStripe), new Vector3(0.012f, 0f, 0.045f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(root, "RecDot", DiverMeshes.Sphere(5, 10), M(LowPoly.Hex("#E04848")), new Vector3(-0.028f, 0.02f, 0.025f), Vector3.one * 0.009f, default, false);
            // lanyard up to the collar (both ends)
            Vector3 top = new Vector3(x, 1.45f - py, FrontZ(1.45f, x) - 0.01f);
            Vector3 c = root.localPosition;
            var lr = Bar(p.pivot, "LanyardR", c + new Vector3(0.036f, 0.03f, -0.01f), top + new Vector3(0.05f, 0f, -0.03f), 0.0045f, M(Cord));
            var ll = Bar(p.pivot, "LanyardL", c + new Vector3(-0.036f, 0.03f, -0.01f), top + new Vector3(-0.08f, 0f, -0.02f), 0.0045f, M(Cord));
            // the lanyard belongs to the camera: parent it so it hides together
            lr.transform.SetParent(root, true);
            ll.transform.SetParent(root, true);
            p.strapCam = root;
            root.gameObject.SetActive(false);
        }

        /// t3 fins: a dive scooter under the body (in front of the chest; while swimming the bean lies on it).
        static void BuildScooter(DiverPuppet p, float py)
        {
            float z = 0.375f;
            var root = DiverMeshes.Node(p.pivot, "Scooter", new Vector3(0f, 0.8f - py, z));
            Material body = M(ScooterBody), dark = M(ScooterDark), stripe = M(ScooterStripe);
            DiverMeshes.Part(root, "Hull", DiverMeshes.Capsule(0.085f, 0.5f, 24), body, Vector3.zero, Vector3.one, default, false);
            DiverMeshes.Part(root, "Nose", DiverMeshes.Sphere(8, 20), dark, new Vector3(0f, 0.47f, 0f), new Vector3(0.12f, 0.08f, 0.12f), default, false);
            DiverMeshes.Part(root, "Stripe", DiverMeshes.Taper(0.087f, 0.087f, 24, false), stripe, new Vector3(0f, 0.3f, 0f), new Vector3(1f, 0.035f, 1f), default, false);
            DiverMeshes.Part(root, "Shroud", DiverMeshes.Torus(0.078f, 0.012f, 24, 6), dark, new Vector3(0f, -0.035f, 0f), new Vector3(1f, 2.2f, 1f), default, false);
            var prop = DiverMeshes.Node(root, "Prop", new Vector3(0f, -0.03f, 0f));
            for (int i = 0; i < 3; i++)
                DiverMeshes.Part(prop, "Blade", DiverMeshes.Sphere(5, 10), dark, Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0.035f, 0f, 0f),
                                 new Vector3(0.06f, 0.008f, 0.022f), new Vector3(0f, i * 120f, 18f), false);
            for (int s = -1; s <= 1; s += 2)
                DiverMeshes.Part(root, "Handle", DiverMeshes.Capsule(0.014f, 0.08f, 10), dark, new Vector3(s * 0.08f, 0.36f, 0f), Vector3.one, new Vector3(0f, 0f, -s * 90f), false);
            p.scooter = root;
            p.scooterProp = prop;
            root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ fins
        /// The fin blade on a foot node (replaces DiverBuilder's plain blade). left = mirrored split blades.
        public static void BuildFin(Transform foot, DiverLook look, Material boots, bool left)
        {
            var finRot = new Vector3(90f, 0f, 0f);
            var at = new Vector3(0f, -0.04f, 0.02f);
            if (look.lookVersion < 1)
            {
                // legacy look: the original dark blade
                var legacy = new[] { new Vector2(-0.07f, -0.06f), new Vector2(0.07f, -0.06f), new Vector2(0.115f, 0.36f), new Vector2(0.03f, 0.33f), new Vector2(0f, 0.36f), new Vector2(-0.03f, 0.33f), new Vector2(-0.115f, 0.36f) };
                DiverMeshes.Part(foot, "Fin", DiverMeshes.Plate("fin_blade", legacy, 0.022f), boots, at, Vector3.one, finRot);
                return;
            }
            int tier = FinTierOf(look);
            Color tierColor = tier == 0 ? FinSqueaky : tier == 1 ? FinSplit : FinCarbon;
            Material blade = M(look.fins.a > 0f ? new Color(look.fins.r, look.fins.g, look.fins.b, 1f) : tierColor);
            var fin = DiverMeshes.Node(foot, "Fin", at, finRot);
            switch (tier)
            {
                case 0:
                {
                    // short, wide, round: squeaky pool fins
                    var o = new[] { new Vector2(-0.075f, -0.05f), new Vector2(0.075f, -0.05f), new Vector2(0.105f, 0.15f), new Vector2(0.085f, 0.23f), new Vector2(0.035f, 0.27f), new Vector2(-0.035f, 0.27f), new Vector2(-0.085f, 0.23f), new Vector2(-0.105f, 0.15f) };
                    DiverMeshes.Part(fin, "Blade", DiverMeshes.Plate("fin_pool", o, 0.03f), blade, Vector3.zero, Vector3.one);
                    break;
                }
                case 1:
                {
                    // split fins: two blades with a gap in the middle
                    var l = new[] { new Vector2(-0.075f, -0.05f), new Vector2(-0.006f, -0.05f), new Vector2(-0.012f, 0.39f), new Vector2(-0.07f, 0.42f), new Vector2(-0.13f, 0.38f) };
                    var r = new[] { new Vector2(0.006f, -0.05f), new Vector2(0.075f, -0.05f), new Vector2(0.13f, 0.38f), new Vector2(0.07f, 0.42f), new Vector2(0.012f, 0.39f) };
                    DiverMeshes.Part(fin, "BladeL", DiverMeshes.Plate("fin_split_l", l, 0.022f), blade, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(fin, "BladeR", DiverMeshes.Plate("fin_split_r", r, 0.022f), blade, Vector3.zero, Vector3.one);
                    break;
                }
                default:
                {
                    // long carbon blades with lighter side rails
                    var o = new[] { new Vector2(-0.07f, -0.05f), new Vector2(0.07f, -0.05f), new Vector2(0.095f, 0.4f), new Vector2(0.06f, 0.5f), new Vector2(0f, 0.53f), new Vector2(-0.06f, 0.5f), new Vector2(-0.095f, 0.4f) };
                    DiverMeshes.Part(fin, "Blade", DiverMeshes.Plate("fin_carbon", o, 0.018f), blade, Vector3.zero, Vector3.one);
                    Material rail = M(FinRail);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        Vector3 a = new Vector3(s * 0.072f, -0.02f, 0f), b = new Vector3(s * 0.094f, 0.4f, 0f);
                        Vector3 d = b - a;
                        var part = DiverMeshes.Part(fin, "Rail", DiverMeshes.Capsule(0.011f, d.magnitude, 6), rail, a, Vector3.one, default, false);
                        part.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
                    }
                    break;
                }
            }
        }
    }
}
