using UnityEngine;

namespace Sealebrities
{
    /// Colours of the bean divers (art bible §1.12).
    public static class DiverStyle
    {
        public static readonly Color Neoprene = LowPoly.Hex("#343849");
        public static readonly Color Boots = LowPoly.Hex("#2B2533");
        public static readonly Color EyeWhite = LowPoly.Hex("#F4EFE6");
        public static readonly Color Pupil = LowPoly.Hex("#1E1B2B");
        public static readonly Color Mouth = LowPoly.Hex("#3A1E2E");
        public static readonly Color Tank = LowPoly.Hex("#C9CED6");
        public static readonly Color TankDark = LowPoly.Hex("#2E3140");
        public static readonly Color MaskFrame = LowPoly.Hex("#1E1B2B");
        public static readonly Color MaskGlass = new Color(0.62f, 0.89f, 0.93f, 0.28f);

        /// Skin tones; the skin is chosen by DiverLook.face / 5 (mouth variant = face % 5).
        public static readonly Color[] Skins = { LowPoly.Hex("#E8C08A"), LowPoly.Hex("#D6A574"), LowPoly.Hex("#A8724A"), LowPoly.Hex("#6B4636") };
        /// Default suit colours of player 1–4 (orange, turquoise, pink, sun yellow).
        public static readonly Color[] PlayerColors = { LowPoly.Hex("#EE7F2D"), LowPoly.Hex("#3FB8C4"), LowPoly.Hex("#E0607E"), LowPoly.Hex("#F2C94C") };
        public const int MouthVariants = 5;
        /// 0 none, 1 bucket hat, 2 viking helmet, 3 shark fin, 4 rubber duck, 5 captain's cap.
        public const int HatCount = 6;

        /// Localized hat name for a character screen.
        public static string HatName(int hat)
        {
            switch (((hat % HatCount) + HatCount) % HatCount)
            {
                case 1: return Loc.Tr("Fischerhut", "Bucket hat");
                case 2: return Loc.Tr("Wikingerhelm", "Viking helmet");
                case 3: return Loc.Tr("Haiflosse", "Shark fin");
                case 4: return Loc.Tr("Quietscheente", "Rubber duck");
                case 5: return Loc.Tr("Kapitänsmütze", "Captain's cap");
                default: return Loc.Tr("Ohne", "None");
            }
        }

        /// A DiverLook in one of the four player colours (co-op slot 0–3).
        public static DiverLook ForPlayer(int index, int hat, int face)
        {
            Color suit = PlayerColors[((index % 4) + 4) % 4];
            return new DiverLook { suit = suit, cuff = Color.Lerp(suit, Color.white, 0.55f), hat = hat, face = face };
        }
    }

    /// Builds the low-poly bean diver (How to Fish proportions: 1.85 m tall bean, no neck, googly eyes, thin tube
    /// limbs, fins) and installs DiverModels.Build. The root sits at the feet, +Z forward; it carries a DiverPuppet.
    public static class DiverBuilder
    {
        // ------------------------------------------------------------------ dimensions (character space, metres)
        public const float PivotY = 0.95f;          // swim pivot (body centre)
        public const float HeadY = 1.47f;           // head pivot (bottom of the head part of the bean)
        public const float ShoulderY = 1.35f, ShoulderX = 0.245f;
        public const float HipY = 0.78f, HipX = 0.125f;
        public const float UpperArm = 0.33f, ForeArm = 0.30f;
        public const float Thigh = 0.36f, Shin = 0.36f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (DiverModels.Build == null) DiverModels.Build = Build;
        }

        /// Assigns DiverModels.Build (called by PlayerFactory as well).
        public static void Install() => DiverModels.Build = Build;

        static Material M(Color c) => LowPoly.Mat(c);

        // ------------------------------------------------------------------ build
        public static GameObject Build(DiverLook look)
        {
            if (look.suit.a <= 0f) look.suit = DiverStyle.PlayerColors[0];
            look.suit.a = 1f;
            if (look.cuff.a <= 0f) look.cuff = Color.Lerp(look.suit, Color.white, 0.55f);
            look.cuff.a = 1f;
            int face = Mathf.Max(0, look.face);
            int hat = ((look.hat % DiverStyle.HatCount) + DiverStyle.HatCount) % DiverStyle.HatCount;
            Color skin = DiverStyle.Skins[(face / DiverStyle.MouthVariants) % DiverStyle.Skins.Length];

            var root = new GameObject("BeanDiver");
            var rt = root.transform;
            var p = root.AddComponent<DiverPuppet>();
            p.look = look;
            p.hat = hat;

            Material neo = M(DiverStyle.Neoprene), suit = M(look.suit), cuff = M(look.cuff), skinM = M(skin);
            Material boots = M(DiverStyle.Boots), white = M(DiverStyle.EyeWhite), ink = M(DiverStyle.Pupil);

            // ---- body (bean from the hips to the collar) with the suit panel on the front and back
            p.pivot = DiverMeshes.Node(rt, "Body", new Vector3(0f, PivotY, 0f));
            Mesh body = DiverMeshes.Cached("bean_body", BuildBodyMesh);
            var bodyR = DiverMeshes.Part(p.pivot, "Bean", body, neo, new Vector3(0f, -PivotY, 0f), Vector3.one);
            bodyR.sharedMaterials = new[] { neo, suit, cuff };
            // harness strap + tank on the back
            DiverMeshes.Part(p.pivot, "Harness", DiverMeshes.Torus(0.256f, 0.016f, 18, 5), M(DiverStyle.TankDark), new Vector3(0f, 1.24f - PivotY, 0f), Vector3.one);
            DiverMeshes.Part(p.pivot, "Tank", DiverMeshes.Capsule(0.105f, 0.56f), M(DiverStyle.Tank), new Vector3(0f, 0.93f - PivotY, -0.345f), Vector3.one);
            DiverMeshes.Part(p.pivot, "TankBand", DiverMeshes.Torus(0.108f, 0.012f, 14, 4), suit, new Vector3(0f, 1.32f - PivotY, -0.345f), Vector3.one);
            DiverMeshes.Part(p.pivot, "Valve", DiverMeshes.Capsule(0.035f, 0.1f), M(DiverStyle.TankDark), new Vector3(0f, 1.47f - PivotY, -0.345f), Vector3.one, default, false);

            // ---- head (top 0.38 m of the bean)
            p.head = DiverMeshes.Node(p.pivot, "Head", new Vector3(0f, HeadY - PivotY, 0f));
            DiverMeshes.Part(p.head, "HeadBean", DiverMeshes.Cached("bean_head", BuildHeadMesh), skinM, new Vector3(0f, -HeadY, 0f), Vector3.one);
            DiverMeshes.Part(p.pivot, "Collar", DiverMeshes.Torus(0.229f, 0.022f, 18, 6), cuff, new Vector3(0f, HeadY - PivotY + 0.005f, 0f), Vector3.one);
            BuildFace(p, face, white, ink);
            BuildMask(p, look, hat);
            BuildHat(p, hat);

            // ---- arms
            p.shoulderL = DiverMeshes.Node(p.pivot, "ShoulderL", new Vector3(-ShoulderX, ShoulderY - PivotY, 0f));
            p.shoulderR = DiverMeshes.Node(p.pivot, "ShoulderR", new Vector3(ShoulderX, ShoulderY - PivotY, 0f));
            BuildArm(p.shoulderL, out p.elbowL, out p.handL, neo, cuff);
            BuildArm(p.shoulderR, out p.elbowR, out p.handR, neo, cuff);

            // ---- legs
            p.hipL = DiverMeshes.Node(p.pivot, "HipL", new Vector3(-HipX, HipY - PivotY, 0f));
            p.hipR = DiverMeshes.Node(p.pivot, "HipR", new Vector3(HipX, HipY - PivotY, 0f));
            BuildLeg(p.hipL, out p.kneeL, neo, suit, boots);
            BuildLeg(p.hipR, out p.kneeR, neo, suit, boots);

            p.InitRig();
            return root;
        }

        static Mesh BuildBodyMesh()
        {
            var prof = new[]
            {
                new Vector2(0.000f, 0.660f), new Vector2(0.120f, 0.672f), new Vector2(0.200f, 0.702f), new Vector2(0.248f, 0.755f),
                new Vector2(0.267f, 0.825f), new Vector2(0.270f, 0.900f), new Vector2(0.268f, 1.000f), new Vector2(0.258f, 1.150f),
                new Vector2(0.245f, 1.300f), new Vector2(0.234f, 1.420f), new Vector2(0.229f, 1.480f), new Vector2(0.150f, 1.490f),
                new Vector2(0.000f, 1.495f),
            };
            // submeshes: 0 neoprene (hips + side panels), 1 suit colour (front/back panel), 2 cuff colour (collar band)
            return DiverMeshes.LatheY(prof, 12, 3, (i, j) =>
            {
                if (i < 5) return 0;
                if (i >= 9) return 2;
                return (j == 0 || j == 5 || j == 6 || j == 11) ? 0 : 1;
            });
        }

        static Mesh BuildHeadMesh()
        {
            var prof = new[]
            {
                new Vector2(0.000f, 1.452f), new Vector2(0.226f, 1.468f), new Vector2(0.226f, 1.520f), new Vector2(0.222f, 1.600f),
                new Vector2(0.212f, 1.680f), new Vector2(0.190f, 1.750f), new Vector2(0.150f, 1.805f), new Vector2(0.090f, 1.838f),
                new Vector2(0.000f, 1.850f),
            };
            return DiverMeshes.LatheY(prof, 14);
        }

        /// Surface radius of the head at an absolute height (for placing face parts).
        static float HeadRadius(float y)
        {
            if (y < 1.52f) return 0.226f;
            if (y < 1.60f) return Mathf.Lerp(0.226f, 0.222f, (y - 1.52f) / 0.08f);
            if (y < 1.68f) return Mathf.Lerp(0.222f, 0.212f, (y - 1.60f) / 0.08f);
            if (y < 1.75f) return Mathf.Lerp(0.212f, 0.190f, (y - 1.68f) / 0.07f);
            return Mathf.Lerp(0.190f, 0.150f, Mathf.Clamp01((y - 1.75f) / 0.055f));
        }

        static void BuildFace(DiverPuppet p, int face, Material white, Material ink)
        {
            const float eyeY = 1.62f, eyeX = 0.075f, eyeR = 0.055f, pupilR = 0.028f;
            float rs = HeadRadius(eyeY);
            float zs = Mathf.Sqrt(Mathf.Max(0.0001f, rs * rs - eyeX * eyeX));
            float ez = zs + 0.03f - eyeR;
            Mesh sphere = DiverMeshes.Sphere(6, 12);
            for (int s = -1; s <= 1; s += 2)
            {
                var eye = DiverMeshes.Node(p.head, s < 0 ? "EyeL" : "EyeR", new Vector3(s * eyeX, eyeY - HeadY, ez));
                DiverMeshes.Part(eye, "White", sphere, white, Vector3.zero, Vector3.one * (eyeR * 2f), default, false);
                var pivot = DiverMeshes.Node(eye, "PupilPivot", Vector3.zero, s < 0 ? new Vector3(-7f, -9f, 0f) : new Vector3(5f, 13f, 0f));
                DiverMeshes.Part(pivot, "Pupil", sphere, ink, new Vector3(0f, 0f, eyeR - pupilR * 0.35f), new Vector3(pupilR * 2f, pupilR * 2f, pupilR * 0.8f), default, false);
                if (s < 0) p.pupilL = pivot; else p.pupilR = pivot;
            }

            // mouth (faces +Z after the 180° turn of the pivot; the meshes face -Z)
            const float mouthY = 1.522f;
            float mz = HeadRadius(mouthY) - 0.001f;
            p.mouth = DiverMeshes.Node(p.head, "Mouth", new Vector3(0f, mouthY - HeadY, mz), new Vector3(-8f, 180f, 0f));
            Mesh mm;
            switch (face % DiverStyle.MouthVariants)
            {
                case 1: mm = DiverMeshes.Plate("mouth_open", HalfDisc(0.034f, 0.026f), 0.008f); break;
                case 2: mm = DiverMeshes.Plate("mouth_o", Ellipse(0.015f, 0.018f, 10), 0.008f); break;
                case 3: mm = DiverMeshes.Plate("mouth_flat", Ellipse(0.03f, 0.006f, 10), 0.008f); break;
                case 4: mm = DiverMeshes.Crescent("mouth_grin", 0.1f, 0.026f, 0.005f, 0.004f, 10); break;
                default: mm = DiverMeshes.Crescent("mouth_smile", 0.074f, 0.022f, 0.008f, 0.004f, 8); break;
            }
            DiverMeshes.Part(p.mouth, "MouthShape", mm, M(DiverStyle.Mouth), Vector3.zero, Vector3.one, default, false);
        }

        static Vector2[] HalfDisc(float halfW, float depth)
        {
            const int n = 9;
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = Mathf.PI * i / n;
                pts[i] = new Vector2(Mathf.Cos(a) * halfW, -Mathf.Sin(a) * depth);
            }
            return pts;
        }

        static Vector2[] Ellipse(float rx, float ry, int n)
        {
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.PI * 2f * i / n;
                pts[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            }
            return pts;
        }

        static void BuildMask(DiverPuppet p, DiverLook look, int hat)
        {
            p.maskPivot = DiverMeshes.Node(p.head, "MaskPivot", new Vector3(0f, 1.62f - HeadY, 0f));
            Material frame = M(DiverStyle.MaskFrame);
            // strap around the head
            // the strap's front half runs inside the head / eyes, only the sides and the back show
            DiverMeshes.Part(p.maskPivot, "Strap", DiverMeshes.Torus(0.226f, 0.011f, 18, 4), frame, new Vector3(0f, 0.01f, -0.03f), new Vector3(1f, 1f, 0.92f), default, false);
            // frame + skirt + glass in front of the eyes
            var front = DiverMeshes.Node(p.maskPivot, "Front", new Vector3(0f, 0f, 0.247f));
            DiverMeshes.Part(front, "Frame", DiverMeshes.Torus(0.13f, 0.017f, 18, 5), frame, Vector3.zero, new Vector3(1f, 1f, 0.55f), new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(front, "Skirt", DiverMeshes.Taper(0.5f, 0.45f, 16, false), frame, Vector3.zero, new Vector3(0.27f, 0.075f, 0.145f), new Vector3(-90f, 0f, 0f), false);
            var glass = DiverMeshes.Part(front, "Glass", DiverMeshes.Plate("mask_glass", Ellipse(0.128f, 0.068f, 16), 0.002f),
                                         MaterialLibrary.Transparent(DiverStyle.MaskGlass, 0.9f, true), new Vector3(0f, 0f, 0.002f), Vector3.one, default, false);
            glass.receiveShadows = false;
            // snorkel on the left side
            Material snork = M(look.cuff);
            DiverMeshes.Part(p.maskPivot, "Snorkel", DiverMeshes.Capsule(0.016f, 0.36f, 8), snork, new Vector3(-0.235f, -0.1f, 0.06f), Vector3.one, new Vector3(0f, 0f, -6f), false);
            DiverMeshes.Part(p.maskPivot, "SnorkelTip", DiverMeshes.Capsule(0.02f, 0.06f, 8), M(DiverStyle.MaskFrame), new Vector3(-0.27f, 0.25f, 0.06f), Vector3.one, new Vector3(0f, 0f, -6f), false);
        }

        static void BuildHat(DiverPuppet p, int hat)
        {
            if (hat == 0) return;
            var h = DiverMeshes.Node(p.head, "Hat", new Vector3(0f, 1.72f - HeadY, 0f));
            p.hatT = h;
            switch (hat)
            {
                case 1:
                {
                    Material m = M(LowPoly.Hex("#C9B48A"));
                    var prof = new[]
                    {
                        new Vector2(0.17f, 0.0f), new Vector2(0.31f, -0.045f), new Vector2(0.335f, -0.04f), new Vector2(0.25f, 0.015f),
                        new Vector2(0.215f, 0.03f), new Vector2(0.205f, 0.15f), new Vector2(0.17f, 0.19f), new Vector2(0.0f, 0.2f),
                    };
                    DiverMeshes.Part(h, "Bucket", DiverMeshes.Cached("hat_bucket", () => DiverMeshes.LatheY(prof, 16)), m, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Band", DiverMeshes.Torus(0.212f, 0.014f, 16, 4), M(LowPoly.Hex("#7A4448")), new Vector3(0f, 0.045f, 0f), Vector3.one, default, false);
                    break;
                }
                case 2:
                {
                    Material metal = M(LowPoly.Hex("#9A9AA6")), horn = M(LowPoly.Hex("#EDE6DA"));
                    var prof = new[] { new Vector2(0.235f, -0.02f), new Vector2(0.236f, 0.04f), new Vector2(0.21f, 0.11f), new Vector2(0.15f, 0.17f), new Vector2(0.07f, 0.2f), new Vector2(0f, 0.205f) };
                    DiverMeshes.Part(h, "Helmet", DiverMeshes.Cached("hat_viking", () => DiverMeshes.LatheY(prof, 16)), metal, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Rim", DiverMeshes.Torus(0.238f, 0.02f, 16, 5), M(LowPoly.Hex("#6E6F7E")), new Vector3(0f, 0.0f, 0f), Vector3.one, default, false);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var hornRoot = DiverMeshes.Node(h, "Horn", new Vector3(s * 0.2f, 0.06f, 0f), new Vector3(0f, 0f, -s * 62f));
                        DiverMeshes.Part(hornRoot, "H1", DiverMeshes.Taper(0.05f, 0.032f, 8), horn, Vector3.zero, new Vector3(1f, 0.13f, 1f));
                        var h2 = DiverMeshes.Node(hornRoot, "H2", new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0f, s * 38f));
                        DiverMeshes.Part(h2, "H2m", DiverMeshes.Cached("horn_tip", () => DiverMeshes.LatheY(new[] { new Vector2(0.034f, 0f), new Vector2(0.02f, 0.07f), new Vector2(0f, 0.13f) }, 8)), horn, Vector3.zero, Vector3.one);
                    }
                    break;
                }
                case 3:
                {
                    Material fin = M(LowPoly.Hex("#5E7A96"));
                    var outline = new[] { new Vector2(-0.16f, 0f), new Vector2(0.13f, 0f), new Vector2(0.02f, 0.1f), new Vector2(-0.09f, 0.3f), new Vector2(-0.12f, 0.12f) };
                    DiverMeshes.Part(h, "Fin", DiverMeshes.Plate("hat_fin", outline, 0.045f), fin, new Vector3(0f, 0.07f, 0f), Vector3.one, new Vector3(0f, -90f, 0f));
                    DiverMeshes.Part(h, "Cap", DiverMeshes.Cached("hat_fincap", () => DiverMeshes.LatheY(new[] { new Vector2(0.2f, 0f), new Vector2(0.17f, 0.07f), new Vector2(0.09f, 0.12f), new Vector2(0f, 0.13f) }, 14)), fin, Vector3.zero, Vector3.one);
                    break;
                }
                case 4:
                {
                    Material duck = M(LowPoly.Hex("#F2C94C")), beak = M(LowPoly.Hex("#F28C4A"));
                    Mesh sph = DiverMeshes.Sphere(6, 12);
                    DiverMeshes.Part(h, "DuckBody", sph, duck, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.26f, 0.19f, 0.32f));
                    DiverMeshes.Part(h, "DuckHead", sph, duck, new Vector3(0f, 0.27f, 0.08f), new Vector3(0.17f, 0.17f, 0.17f));
                    DiverMeshes.Part(h, "Beak", sph, beak, new Vector3(0f, 0.255f, 0.17f), new Vector3(0.09f, 0.035f, 0.08f), default, false);
                    DiverMeshes.Part(h, "Tail", sph, duck, new Vector3(0f, 0.2f, -0.16f), new Vector3(0.08f, 0.1f, 0.09f), new Vector3(-30f, 0f, 0f), false);
                    for (int s = -1; s <= 1; s += 2)
                        DiverMeshes.Part(h, "DuckEye", sph, M(DiverStyle.Pupil), new Vector3(s * 0.06f, 0.295f, 0.14f), Vector3.one * 0.025f, default, false);
                    break;
                }
                default:
                {
                    Material white = M(LowPoly.Hex("#EDE6DA")), dark = M(LowPoly.Hex("#1E1B2B"));
                    var prof = new[] { new Vector2(0.205f, -0.01f), new Vector2(0.21f, 0.09f), new Vector2(0.245f, 0.15f), new Vector2(0.235f, 0.17f), new Vector2(0f, 0.175f) };
                    DiverMeshes.Part(h, "Crown", DiverMeshes.Cached("hat_captain", () => DiverMeshes.LatheY(prof, 16)), white, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Band", DiverMeshes.Torus(0.209f, 0.022f, 16, 4), dark, new Vector3(0f, 0.025f, 0f), Vector3.one, default, false);
                    DiverMeshes.Part(h, "Visor", DiverMeshes.Plate("cap_visor", HalfDisc(0.15f, 0.11f), 0.012f), dark, new Vector3(0f, 0.0f, 0.2f), Vector3.one, new Vector3(-75f, 0f, 0f), false);
                    DiverMeshes.Part(h, "Badge", DiverMeshes.Sphere(5, 8), M(LowPoly.Hex("#F2C94C")), new Vector3(0f, 0.06f, 0.215f), new Vector3(0.05f, 0.04f, 0.015f), default, false);
                    break;
                }
            }
        }

        static void BuildArm(Transform shoulder, out Transform elbow, out Transform hand, Material neo, Material cuff)
        {
            DiverMeshes.Limb(shoulder, "UpperArm", 0.062f, UpperArm, neo);
            elbow = DiverMeshes.Node(shoulder, "Elbow", new Vector3(0f, -UpperArm, 0f));
            DiverMeshes.Limb(elbow, "ForeArm", 0.056f, ForeArm, neo);
            DiverMeshes.Part(elbow, "Cuff", DiverMeshes.Torus(0.056f, 0.014f, 12, 4), cuff, new Vector3(0f, -ForeArm + 0.02f, 0f), Vector3.one, default, false);
            hand = DiverMeshes.Node(elbow, "Hand", new Vector3(0f, -ForeArm - 0.05f, 0f));
            DiverMeshes.Part(hand, "Mitten", DiverMeshes.Sphere(6, 10), neo, new Vector3(0f, -0.02f, 0.005f), new Vector3(0.12f, 0.15f, 0.095f));
            DiverMeshes.Part(hand, "Thumb", DiverMeshes.Capsule(0.022f, 0.07f, 8), neo, new Vector3(0f, -0.01f, 0.04f), Vector3.one, new Vector3(150f, 0f, 0f), false);
        }

        static void BuildLeg(Transform hip, out Transform knee, Material neo, Material suit, Material boots)
        {
            DiverMeshes.Limb(hip, "Thigh", 0.11f, Thigh, neo);
            knee = DiverMeshes.Node(hip, "Knee", new Vector3(0f, -Thigh, 0f));
            DiverMeshes.Limb(knee, "Shin", 0.1f, Shin - 0.04f, neo);
            DiverMeshes.Part(knee, "Stripe", DiverMeshes.Torus(0.101f, 0.016f, 14, 4), suit, new Vector3(0f, -0.07f, 0f), Vector3.one, default, false);
            var foot = DiverMeshes.Node(knee, "Foot", new Vector3(0f, -Shin + 0.02f, 0f));
            DiverMeshes.Part(foot, "Pocket", DiverMeshes.Capsule(0.085f, 0.24f, 10), boots, new Vector3(0f, -0.005f, -0.06f), Vector3.one, new Vector3(90f, 0f, 0f));
            var fin = new[] { new Vector2(-0.07f, -0.06f), new Vector2(0.07f, -0.06f), new Vector2(0.115f, 0.36f), new Vector2(0.03f, 0.33f), new Vector2(0f, 0.36f), new Vector2(-0.03f, 0.33f), new Vector2(-0.115f, 0.36f) };
            DiverMeshes.Part(foot, "Fin", DiverMeshes.Plate("fin_blade", fin, 0.022f), boots, new Vector3(0f, -0.04f, 0.02f), Vector3.one, new Vector3(90f, 0f, 0f));
        }
    }
}
