using System.Collections.Generic;
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
        public static readonly Color Blush = new Color(0.93f, 0.45f, 0.52f, 0.35f);

        /// Skin tones (legacy list, the first four of CosmeticsDatabase.SkinTones).
        public static readonly Color[] Skins = { LowPoly.Hex("#E8C08A"), LowPoly.Hex("#D6A574"), LowPoly.Hex("#A8724A"), LowPoly.Hex("#6B4636") };
        /// Default suit colours of player 1–4 (orange, turquoise, pink, sun yellow).
        public static readonly Color[] PlayerColors = { LowPoly.Hex("#EE7F2D"), LowPoly.Hex("#3FB8C4"), LowPoly.Hex("#E0607E"), LowPoly.Hex("#F2C94C") };
        public const int MouthVariants = 5;
        /// Hats of the character screen (CosmeticsDatabase.HatCount): 0 none … 14 paper crown.
        public const int HatCount = CosmeticsDatabase.HatCount;

        /// Localized hat name for a character screen.
        public static string HatName(int hat)
        {
            var d = CosmeticsDatabase.Get(CosmeticsDatabase.Hat, ((hat % HatCount) + HatCount) % HatCount);
            return d != null ? d.Name : Loc.Tr("Ohne", "None");
        }

        /// A DiverLook in one of the four player colours (co-op slot 0–3).
        public static DiverLook ForPlayer(int index, int hat, int face)
        {
            Color suit = PlayerColors[((index % 4) + 4) % 4];
            return new DiverLook { suit = suit, cuff = Color.Lerp(suit, Color.white, 0.55f), hat = hat, face = face, snorkel = true };
        }
    }

    /// Builds the bean diver (How to Fish proportions: 1.85 m tall bean, no neck, googly eyes, thin tube limbs, fins)
    /// and installs DiverModels.Build. The root sits at the feet, +Z forward; it carries a DiverPuppet.
    /// Round 2b (L.13 "much smoother characters", 1c options; LOD0 ≤ 9,000 triangles + LOD1/LOD2 proxies, DiverLod):
    /// body 28 radial × 18 rings, head 28 × 14, smooth 12 × 6 limbs with ball joints, eyes with lids + highlight dots, 8 mouth shapes, blush; every character-screen option is
    /// drawn: 15 hats, 8 suit patterns, 5 eye styles, 4 brows, 6 skin tones, mask colour, snorkel on/off, fin colour.
    /// Neoprene smoothness 0.18 (soft sheen), skin 0.10. Meshes and pattern textures are built once and shared.
    public static class DiverBuilder
    {
        // ------------------------------------------------------------------ dimensions (character space, metres)
        public const float PivotY = 0.95f;          // swim pivot (body centre)
        public const float HeadY = 1.47f;           // head pivot (bottom of the head part of the bean)
        public const float ShoulderY = 1.35f, ShoulderX = 0.245f;
        public const float HipY = 0.78f, HipX = 0.125f;
        public const float UpperArm = 0.33f, ForeArm = 0.30f;
        public const float Thigh = 0.36f, Shin = 0.36f;
        public const float EyeY = 1.62f, EyeX = 0.075f, EyeR = 0.06f, PupilR = 0.029f;

        public const float NeopreneSmoothness = 0.18f, SkinSmoothness = 0.10f;

        /// L.13 bean body profile (radius, height) bottom → top: bottom radius 0.29 with a soft belly bulge.
        static readonly Vector2[] BodyProfile =
        {
            new Vector2(0.000f, 0.655f), new Vector2(0.125f, 0.667f), new Vector2(0.208f, 0.698f), new Vector2(0.258f, 0.752f),
            new Vector2(0.280f, 0.825f), new Vector2(0.288f, 0.905f), new Vector2(0.286f, 1.000f), new Vector2(0.274f, 1.150f),
            new Vector2(0.256f, 1.300f), new Vector2(0.238f, 1.420f), new Vector2(0.231f, 1.478f), new Vector2(0.150f, 1.490f),
            new Vector2(0.000f, 1.495f),
        };

        static readonly Vector2[] HeadProfile =
        {
            new Vector2(0.000f, 1.452f), new Vector2(0.226f, 1.468f), new Vector2(0.226f, 1.520f), new Vector2(0.222f, 1.600f),
            new Vector2(0.212f, 1.680f), new Vector2(0.190f, 1.750f), new Vector2(0.150f, 1.805f), new Vector2(0.090f, 1.838f),
            new Vector2(0.000f, 1.850f),
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            if (DiverModels.Build == null) DiverModels.Build = Build;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { patternMats.Clear(); }

        /// Assigns DiverModels.Build (called by PlayerFactory as well).
        public static void Install() => DiverModels.Build = Build;

        static Material M(Color c) => LowPoly.Mat(c);
        static Material Neo(Color c) => MaterialLibrary.Opaque(c, NeopreneSmoothness, 0f);
        static Material SkinMat(Color c) => MaterialLibrary.Opaque(c, SkinSmoothness, 0f);

        /// Body surface radius at an absolute height (side of the bean) – DiverGear places straps and tanks with it.
        public static float BodyRadiusAt(float y)
        {
            // piecewise linear over the side part of the profile (points 3 … 10)
            if (y <= BodyProfile[3].y) return BodyProfile[3].x;
            for (int i = 3; i < 10; i++)
            {
                Vector2 a = BodyProfile[i], b = BodyProfile[i + 1];
                if (y <= b.y) return Mathf.Lerp(a.x, b.x, (y - a.y) / Mathf.Max(1e-4f, b.y - a.y));
            }
            return BodyProfile[10].x;
        }

        // ------------------------------------------------------------------ build
        public static GameObject Build(DiverLook look)
        {
            look = look.Normalized();
            if (look.suit.a <= 0f || (look.suit.r + look.suit.g + look.suit.b) <= 0.0001f) look.suit = DiverStyle.PlayerColors[0];
            look.suit.a = 1f;
            Color cuffC = look.ResolvedCuff;
            look.cuff = cuffC;
            int hat = ((look.hat % DiverStyle.HatCount) + DiverStyle.HatCount) % DiverStyle.HatCount;
            var skins = CosmeticsDatabase.SkinTones;
            Color skin = skins[Mathf.Clamp(look.skin, 0, skins.Length - 1)];

            var root = new GameObject("BeanDiver");
            var rt = root.transform;
            var p = root.AddComponent<DiverPuppet>();
            p.look = look;
            p.hat = hat;
            p.eyeStyle = Mathf.Clamp(look.eyes, 0, CosmeticsDatabase.EyesCount - 1);
            p.browStyle = Mathf.Clamp(look.brows, 0, CosmeticsDatabase.BrowsCount - 1);
            p.baseMouth = Mathf.Clamp(look.mouth, 0, CosmeticsDatabase.MouthCount - 1);

            Material neo = Neo(DiverStyle.Neoprene), suit = Neo(look.suit), cuff = Neo(cuffC), skinM = SkinMat(skin);
            Material panel = PatternMaterial(Mathf.Clamp(look.pattern, 0, CosmeticsDatabase.PatternCount - 1), look.suit, cuffC);
            Material boots = Neo(DiverStyle.Boots), white = M(DiverStyle.EyeWhite), ink = M(DiverStyle.Pupil);

            // ---- body (bean from the hips to the collar) with the suit panel on the front and back
            p.pivot = DiverMeshes.Node(rt, "Body", new Vector3(0f, PivotY, 0f));
            Mesh body = DiverMeshes.Cached("bean_body_r3", BuildBodyMesh);
            var bodyR = DiverMeshes.Part(p.pivot, "Bean", body, neo, new Vector3(0f, -PivotY, 0f), Vector3.one);
            bodyR.sharedMaterials = new[] { neo, panel, cuff };
            // visible gear (Q51): harness, tank(s) by tier, strap lamp / camera, dive scooter – DiverGear
            DiverGear.BuildBack(p, look, suit);
            BuildSponsorPatch(p, look);

            // ---- head (top 0.38 m of the bean)
            p.head = DiverMeshes.Node(p.pivot, "Head", new Vector3(0f, HeadY - PivotY, 0f));
            DiverMeshes.Part(p.head, "HeadBean", DiverMeshes.Cached("bean_head_r3", BuildHeadMesh), skinM, new Vector3(0f, -HeadY, 0f), Vector3.one);
            // the collar ring hides the head/body seam (matching radius)
            DiverMeshes.Part(p.pivot, "Collar", DiverMeshes.Torus(0.229f, 0.024f, 20, 3), cuff, new Vector3(0f, HeadY - PivotY + 0.005f, 0f), Vector3.one);
            BuildFace(p, look, skin, white, ink);
            BuildMask(p, look);
            BuildHat(p, hat);

            // ---- arms
            p.shoulderL = DiverMeshes.Node(p.pivot, "ShoulderL", new Vector3(-ShoulderX, ShoulderY - PivotY, 0f));
            p.shoulderR = DiverMeshes.Node(p.pivot, "ShoulderR", new Vector3(ShoulderX, ShoulderY - PivotY, 0f));
            BuildArm(p.shoulderL, out p.elbowL, out p.handL, neo, cuff);
            BuildArm(p.shoulderR, out p.elbowR, out p.handR, neo, cuff);

            // ---- legs
            p.hipL = DiverMeshes.Node(p.pivot, "HipL", new Vector3(-HipX, HipY - PivotY, 0f));
            p.hipR = DiverMeshes.Node(p.pivot, "HipR", new Vector3(HipX, HipY - PivotY, 0f));
            BuildLeg(p.hipL, out p.kneeL, neo, panel, boots, look, true);
            BuildLeg(p.hipR, out p.kneeR, neo, panel, boots, look, false);

            p.InitRig();
            // L.13: LOD1 (~12 m) / LOD2 (~35 m) proxies on the same bones – a crowd of avatars / NPCs stays cheap
            DiverLod.Build(root);
            return root;
        }

        /// K.7 / Q90: the sponsor's logo patch on the left shoulder (look.sponsor = DiverSponsor.CurrentIndex, 0 = none).
        static void BuildSponsorPatch(DiverPuppet p, DiverLook look)
        {
            var sp = DiverSponsor.Def(look.sponsor);
            if (sp == null) return;
            const float y = 1.27f;
            // on the bean's surface, left side a bit to the front; slightly proud of it so the flat patch never sinks in
            Vector3 dir = new Vector3(-0.82f, 0f, 0.57f).normalized;
            Vector3 pos = dir * (BodyRadiusAt(y) + 0.005f) + new Vector3(0f, y - PivotY, 0f);
            // the quad faces −Z: turn −Z outward, tilt with the narrowing bean (surface normal ~10° up)
            Quaternion rot = Quaternion.LookRotation(-dir, Vector3.up) * Quaternion.Euler(10f, 0f, 0f);
            var r = DiverMeshes.Part(p.pivot, "SponsorPatch", DiverMeshes.Quad("sponsor_patch", 0.075f, 0.075f), DiverSponsor.Material(sp), pos, Vector3.one, default, false);
            r.transform.localRotation = rot;
        }

        static Mesh BuildBodyMesh()
        {
            // L.13 LOD0 budget (≤ 9,000 incl. the biggest loadout): 28 radial × 18 rings – smooth normals keep it round
            const int segs = 28;
            var prof = DiverMeshes.Resample(BodyProfile, 18);
            // submeshes: 0 neoprene (hips + side panels), 1 suit colour / pattern (front + back panel), 2 cuff (collar band)
            return DiverMeshes.LatheY(prof, segs, 3, (i, j) =>
            {
                float y = (prof[i].y + prof[Mathf.Min(prof.Length - 1, i + 1)].y) * 0.5f;
                if (y < 0.83f) return 0;
                if (y >= 1.415f) return 2;
                float a = (j + 0.5f) / segs * Mathf.PI * 2f;
                return Mathf.Abs(Mathf.Cos(a)) > 0.866f ? 0 : 1;
            });
        }

        static Mesh BuildHeadMesh() => DiverMeshes.LatheY(DiverMeshes.Resample(HeadProfile, 14), 28);

        /// Surface radius of the head at an absolute height (for placing face parts).
        static float HeadRadius(float y)
        {
            if (y < 1.52f) return 0.226f;
            if (y < 1.60f) return Mathf.Lerp(0.226f, 0.222f, (y - 1.52f) / 0.08f);
            if (y < 1.68f) return Mathf.Lerp(0.222f, 0.212f, (y - 1.60f) / 0.08f);
            if (y < 1.75f) return Mathf.Lerp(0.212f, 0.190f, (y - 1.68f) / 0.07f);
            return Mathf.Lerp(0.190f, 0.150f, Mathf.Clamp01((y - 1.75f) / 0.055f));
        }

        // ------------------------------------------------------------------ face (eyes, lids, brows, mouths, blush)
        static void BuildFace(DiverPuppet p, DiverLook look, Color skin, Material white, Material ink)
        {
            float rs = HeadRadius(EyeY);
            float zs = Mathf.Sqrt(Mathf.Max(0.0001f, rs * rs - EyeX * EyeX));
            float ez = zs + 0.03f - EyeR;
            Mesh eyeMesh = DiverMeshes.Sphere(8, 12);
            Mesh pupilMesh = DiverMeshes.Sphere(5, 8);
            Mesh lidMesh = DiverMeshes.Cached("eyelid_cap_r3", () => DiverMeshes.LatheY(HemisphereProfile(6), 12));
            Material lidM = SkinMat(Color.Lerp(skin, Color.black, 0.06f));
            Material shine = MaterialLibrary.Unlit(new Color(1f, 1f, 1f, 1f));
            for (int s = -1; s <= 1; s += 2)
            {
                bool leftEye = s < 0;
                var eye = DiverMeshes.Node(p.head, leftEye ? "EyeL" : "EyeR", new Vector3(s * EyeX, EyeY - HeadY, ez));
                DiverMeshes.Part(eye, "White", eyeMesh, white, Vector3.zero, Vector3.one * (EyeR * 2f), default, false);
                // pupils: googly (slightly outward), cross-eyed (toward the nose)
                Vector3 pupilEuler = leftEye ? new Vector3(-7f, -9f, 0f) : new Vector3(5f, 13f, 0f);
                if (p.eyeStyle == 2) pupilEuler = new Vector3(4f, -s * 22f, 0f);
                var pivot = DiverMeshes.Node(eye, "PupilPivot", Vector3.zero, pupilEuler);
                switch (p.eyeStyle)
                {
                    case 3:   // stars
                        DiverMeshes.Part(pivot, "Star", DiverMeshes.Plate("eye_star", StarOutline(0.034f, 0.015f), 0.006f),
                            M(LowPoly.Hex("#F2C94C")), new Vector3(0f, 0f, EyeR - 0.002f), Vector3.one, new Vector3(0f, 180f, 0f), false);
                        break;
                    case 4:   // hearts
                        DiverMeshes.Part(pivot, "Heart", DiverMeshes.Plate("eye_heart", HeartOutline(0.03f), 0.006f),
                            M(LowPoly.Hex("#E0607E")), new Vector3(0f, 0f, EyeR - 0.002f), Vector3.one, new Vector3(0f, 180f, 0f), false);
                        break;
                    default:
                        DiverMeshes.Part(pivot, "Pupil", pupilMesh, ink, new Vector3(0f, 0f, EyeR - PupilR * 0.35f), new Vector3(PupilR * 2f, PupilR * 2f, PupilR * 0.8f), default, false);
                        // white highlight dot (upper outer side of the pupil)
                        DiverMeshes.Part(pivot, "Shine", DiverMeshes.Sphere(3, 4), shine, new Vector3(s * -0.009f, 0.011f, EyeR + 0.0005f), Vector3.one * 0.024f, default, false);
                        break;
                }
                if (leftEye) { p.pupilL = pivot; p.eyeL = eye; } else { p.pupilR = pivot; p.eyeR = eye; }

                // upper eyelid: a skin-coloured half sphere just outside the eye white, rotated over the eye to blink
                var lid = DiverMeshes.Node(eye, "Lid", Vector3.zero, new Vector3(-55f, 0f, 0f));
                DiverMeshes.Part(lid, "LidCap", lidMesh, lidM, Vector3.zero, Vector3.one * (EyeR * 2.12f), default, false);
                if (leftEye) p.lidL = lid; else p.lidR = lid;

                // brows (1c): bushy, grumpy (inner end down), surprised (high arch)
                if (p.browStyle > 0)
                {
                    float by = EyeY + 0.083f + (p.browStyle == 3 ? 0.018f : 0f);
                    float br = HeadRadius(by);
                    float bz = Mathf.Sqrt(Mathf.Max(0.0001f, br * br - EyeX * EyeX)) + 0.004f;
                    float tilt = p.browStyle == 2 ? s * 18f : p.browStyle == 3 ? -s * 10f : 0f;
                    // tangent to the head: yaw toward the side, pitched up with the narrowing top of the head (~17°)
                    var brow = DiverMeshes.Node(p.head, leftEye ? "BrowL" : "BrowR", new Vector3(s * EyeX, by - HeadY, bz),
                        new Vector3(-17f, s * Mathf.Asin(Mathf.Clamp01(EyeX / Mathf.Max(0.05f, br))) * Mathf.Rad2Deg, tilt));
                    float rad = p.browStyle == 1 ? 0.017f : 0.011f;
                    float len = p.browStyle == 1 ? 0.085f : 0.07f;
                    DiverMeshes.Part(brow, "Brow", DiverMeshes.SmoothCapsule(rad, len, 6, 2, 1),
                        M(Color.Lerp(skin, DiverStyle.Pupil, 0.75f)), new Vector3(-len * 0.5f, 0f, 0f), Vector3.one, new Vector3(0f, 0f, -90f), false);
                    if (leftEye) p.browL = brow; else p.browR = brow;
                }

                // cheek blush (shown while happy)
                float cy = 1.545f;
                float cr = HeadRadius(cy);
                float cx = 0.135f;
                float cz = Mathf.Sqrt(Mathf.Max(0.0001f, cr * cr - cx * cx)) + 0.002f;
                var blush = DiverMeshes.Node(p.head, leftEye ? "BlushL" : "BlushR", new Vector3(s * cx, cy - HeadY, cz), new Vector3(0f, 180f + s * Mathf.Asin(Mathf.Clamp01(cx / cr)) * Mathf.Rad2Deg, 0f));
                var br2 = DiverMeshes.Part(blush, "Disc", DiverMeshes.Plate("blush", Ellipse(0.032f, 0.02f, 16), 0.002f),
                    MaterialLibrary.Transparent(DiverStyle.Blush, 0.1f, false), Vector3.zero, Vector3.one, default, false);
                br2.receiveShadows = false;
                blush.gameObject.SetActive(false);
                if (leftEye) p.blushL = blush; else p.blushR = blush;
            }

            // mouth (faces +Z after the 180° turn of the pivot; the meshes face -Z): 8 shapes, one shown at a time
            const float mouthY = 1.522f;
            float mz = HeadRadius(mouthY) - 0.001f;
            p.mouth = DiverMeshes.Node(p.head, "Mouth", new Vector3(0f, mouthY - HeadY, mz), new Vector3(-8f, 180f, 0f));
            Material mm = M(DiverStyle.Mouth);
            p.mouths = new Transform[DiverPuppet.MouthShapes];
            for (int i = 0; i < DiverPuppet.MouthShapes; i++)
            {
                var t = DiverMeshes.Node(p.mouth, "Shape" + i, Vector3.zero);
                // the kissy pucker is a torus (XZ plane) – stand it up to face forward
                DiverMeshes.Part(t, "M", MouthMesh(i), mm, Vector3.zero, Vector3.one, i == 7 ? new Vector3(90f, 0f, 0f) : default, false);
                if (i == 4)   // grin: a row of teeth
                    DiverMeshes.Part(t, "Teeth", DiverMeshes.Plate("mouth_teeth", new[] { new Vector2(-0.034f, -0.004f), new Vector2(0.034f, -0.004f), new Vector2(0.028f, -0.012f), new Vector2(-0.028f, -0.012f) }, 0.004f),
                        M(DiverStyle.EyeWhite), new Vector3(0f, 0f, -0.003f), Vector3.one, default, false);
                t.gameObject.SetActive(i == p.baseMouth);
                p.mouths[i] = t;
            }
        }

        /// Mouth shapes: 0 smile, 1 open, 2 o, 3 flat, 4 grin, 5 wow-O, 6 scared (wavy), 7 kissy.
        static Mesh MouthMesh(int i)
        {
            switch (i)
            {
                case 1: return DiverMeshes.Plate("mouth_open_r2b", HalfDisc(0.034f, 0.028f, 14), 0.008f);
                case 2: return DiverMeshes.Plate("mouth_o_r2b", Ellipse(0.015f, 0.018f, 16), 0.008f);
                case 3: return DiverMeshes.Plate("mouth_flat_r2b", Ellipse(0.03f, 0.006f, 14), 0.008f);
                case 4: return DiverMeshes.Crescent("mouth_grin_r2b", 0.1f, 0.03f, 0.004f, 0.004f, 16);
                case 5: return DiverMeshes.Plate("mouth_wow", Ellipse(0.022f, 0.032f, 20), 0.008f);
                case 6: return DiverMeshes.Plate("mouth_wavy", WavyBand(0.07f, 0.007f, 0.006f, 20), 0.008f);
                case 7: return DiverMeshes.Torus(0.011f, 0.0055f, 16, 8);
                default: return DiverMeshes.Crescent("mouth_smile_r2b", 0.074f, 0.024f, 0.008f, 0.004f, 14);
            }
        }

        static Vector2[] HemisphereProfile(int rings)
        {
            // equator (y = 0) → pole (y = 0.5): a dome on +Y, unit diameter
            var pts = new Vector2[rings + 1];
            for (int i = 0; i <= rings; i++)
            {
                float a = Mathf.PI * 0.5f * i / rings;
                pts[i] = new Vector2(i == rings ? 0f : 0.5f * Mathf.Cos(a), 0.5f * Mathf.Sin(a));
            }
            return pts;
        }

        static Vector2[] HalfDisc(float halfW, float depth, int n = 9)
        {
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

        /// A wavy band (scared mouth) as a closed outline: upper edge left → right, lower edge right → left.
        static Vector2[] WavyBand(float width, float amp, float thick, int steps)
        {
            var pts = new Vector2[(steps + 1) * 2];
            for (int i = 0; i <= steps; i++)
            {
                float u = i / (float)steps;
                float x = (u - 0.5f) * width;
                float y = Mathf.Sin(u * Mathf.PI * 4f) * amp;
                pts[i] = new Vector2(x, y + thick * 0.5f);
                pts[pts.Length - 1 - i] = new Vector2(x, y - thick * 0.5f);
            }
            return pts;
        }

        static Vector2[] StarOutline(float outer, float inner)
        {
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
                float r = (i & 1) == 0 ? outer : inner;
                pts[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
            }
            return pts;
        }

        static Vector2[] HeartOutline(float size)
        {
            const int n = 24;
            var pts = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n * Mathf.PI * 2f;
                float x = 16f * Mathf.Pow(Mathf.Sin(t), 3f);
                float y = 13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t);
                pts[i] = new Vector2(x, y + 2f) * (size / 17f);
            }
            return pts;
        }

        // ------------------------------------------------------------------ mask + snorkel
        static void BuildMask(DiverPuppet p, DiverLook look)
        {
            p.maskPivot = DiverMeshes.Node(p.head, "MaskPivot", new Vector3(0f, EyeY - HeadY, 0f));
            Color fc = look.ResolvedMask;
            bool gold = (fc - CosmeticsDatabase.MaskColors[6]).maxColorComponent < 0.02f && (CosmeticsDatabase.MaskColors[6] - fc).maxColorComponent < 0.02f;
            Material frame = MaterialLibrary.Opaque(fc, gold ? 0.6f : 0.3f, gold ? 0.4f : 0f);
            // the strap's front half runs inside the head / eyes, only the sides and the back show
            DiverMeshes.Part(p.maskPivot, "Strap", DiverMeshes.Torus(0.226f, 0.011f, 16, 3), frame, new Vector3(0f, 0.01f, -0.03f), new Vector3(1f, 1f, 0.92f), default, false);
            // frame + skirt + glass in front of the eyes
            var front = DiverMeshes.Node(p.maskPivot, "Front", new Vector3(0f, 0f, 0.247f));
            DiverMeshes.Part(front, "Frame", DiverMeshes.Torus(0.13f, 0.017f, 20, 4), frame, Vector3.zero, new Vector3(1f, 1f, 0.55f), new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(front, "Skirt", DiverMeshes.Taper(0.5f, 0.45f, 16, false), frame, Vector3.zero, new Vector3(0.27f, 0.075f, 0.145f), new Vector3(-90f, 0f, 0f), false);
            var glass = DiverMeshes.Part(front, "Glass", DiverMeshes.Plate("mask_glass_r2b", Ellipse(0.128f, 0.068f, 28), 0.002f),
                                         MaterialLibrary.Transparent(DiverStyle.MaskGlass, 0.6f, true), new Vector3(0f, 0f, 0.002f), Vector3.one, default, false);
            glass.receiveShadows = false;
            // snorkel on the left side (1c: on / off; legacy looks always had it)
            if (look.snorkel || look.lookVersion < 1)
            {
                Material snork = Neo(look.ResolvedCuff);
                DiverMeshes.Part(p.maskPivot, "Snorkel", DiverMeshes.SmoothCapsule(0.016f, 0.36f, 8, 2, 1), snork, new Vector3(-0.235f, -0.1f, 0.06f), Vector3.one, new Vector3(0f, 0f, -6f), false);
                // SmoothCapsule spans y 0 → length from its origin: tube origin + axis (0.1045, 0.9945) × 0.33 – the 6 cm cap
                // overlaps the tube's last 3 cm and sticks out 3 cm (it floated 3.5 cm beside the tube before)
                DiverMeshes.Part(p.maskPivot, "SnorkelTip", DiverMeshes.SmoothCapsule(0.02f, 0.06f, 8, 2, 1), frame, new Vector3(-0.2005f, 0.228f, 0.06f), Vector3.one, new Vector3(0f, 0f, -6f), false);
            }
        }

        // ------------------------------------------------------------------ hats (CosmeticsDatabase order 0 … 14)
        static void BuildHat(DiverPuppet p, int hat)
        {
            if (hat == 0) return;
            var h = DiverMeshes.Node(p.head, "Hat", new Vector3(0f, 1.72f - HeadY, 0f));
            p.hatT = h;
            switch (hat)
            {
                case 1: case 6: case 10:   // bucket hat / golden bucket hat / Horst's lure hat
                {
                    bool goldHat = hat == 6;
                    Material m = goldHat ? MaterialLibrary.Opaque(LowPoly.Hex("#E8C65A"), 0.55f, 0.35f) : MaterialLibrary.Opaque(LowPoly.Hex("#C9B48A"), 0.12f, 0f);
                    var prof = new[]
                    {
                        new Vector2(0.17f, 0.0f), new Vector2(0.31f, -0.045f), new Vector2(0.335f, -0.04f), new Vector2(0.25f, 0.015f),
                        new Vector2(0.215f, 0.03f), new Vector2(0.205f, 0.15f), new Vector2(0.17f, 0.19f), new Vector2(0.0f, 0.2f),
                    };
                    DiverMeshes.Part(h, "Bucket", DiverMeshes.Cached("hat_bucket_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(prof, 12), 24)), m, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Band", DiverMeshes.Torus(0.212f, 0.014f, 16, 3), M(LowPoly.Hex(goldHat ? "#B8902E" : "#7A4448")), new Vector3(0f, 0.045f, 0f), Vector3.one, default, false);
                    if (hat == 10)
                    {
                        // fishing lures hooked into the band
                        string[] cols = { "#F2C94C", "#E0607E", "#3FB8C4" };
                        for (int i = 0; i < 3; i++)
                        {
                            float a = (-35f + i * 35f) * Mathf.Deg2Rad;
                            var lure = DiverMeshes.Node(h, "Lure" + i, new Vector3(Mathf.Sin(a) * 0.222f, 0.04f, Mathf.Cos(a) * 0.222f), new Vector3(0f, a * Mathf.Rad2Deg, 0f));
                            DiverMeshes.Part(lure, "Body", DiverMeshes.SmoothCapsule(0.012f, 0.05f, 6, 2, 1), M(LowPoly.Hex(cols[i])), new Vector3(0f, -0.05f, 0.01f), Vector3.one, default, false);
                            DiverMeshes.Part(lure, "Hook", DiverMeshes.Torus(0.008f, 0.0015f, 6, 3), M(LowPoly.Hex("#B8BEC6")), new Vector3(0f, -0.058f, 0.01f), Vector3.one, new Vector3(0f, 0f, 90f), false);
                        }
                    }
                    break;
                }
                case 2:   // viking helmet
                {
                    Material metal = MaterialLibrary.Opaque(LowPoly.Hex("#9A9AA6"), 0.45f, 0.2f), horn = M(LowPoly.Hex("#EDE6DA"));
                    var prof = new[] { new Vector2(0.235f, -0.02f), new Vector2(0.236f, 0.04f), new Vector2(0.21f, 0.11f), new Vector2(0.15f, 0.17f), new Vector2(0.07f, 0.2f), new Vector2(0f, 0.205f) };
                    DiverMeshes.Part(h, "Helmet", DiverMeshes.Cached("hat_viking_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(prof, 10), 20)), metal, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Rim", DiverMeshes.Torus(0.238f, 0.02f, 20, 4), M(LowPoly.Hex("#6E6F7E")), new Vector3(0f, 0.0f, 0f), Vector3.one, default, false);
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var hornRoot = DiverMeshes.Node(h, "Horn", new Vector3(s * 0.2f, 0.06f, 0f), new Vector3(0f, 0f, -s * 62f));
                        DiverMeshes.Part(hornRoot, "H1", DiverMeshes.Taper(0.05f, 0.032f, 10), horn, Vector3.zero, new Vector3(1f, 0.13f, 1f));
                        var h2 = DiverMeshes.Node(hornRoot, "H2", new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0f, s * 38f));
                        DiverMeshes.Part(h2, "H2m", DiverMeshes.Cached("horn_tip_r3", () => DiverMeshes.LatheY(new[] { new Vector2(0.034f, 0f), new Vector2(0.026f, 0.04f), new Vector2(0.016f, 0.085f), new Vector2(0f, 0.13f) }, 10)), horn, Vector3.zero, Vector3.one);
                    }
                    break;
                }
                case 3:   // shark fin
                {
                    Material fin = MaterialLibrary.Opaque(LowPoly.Hex("#5E7A96"), 0.25f, 0f);
                    var outline = new[] { new Vector2(-0.16f, 0f), new Vector2(0.13f, 0f), new Vector2(0.05f, 0.06f), new Vector2(0.0f, 0.13f), new Vector2(-0.07f, 0.26f), new Vector2(-0.09f, 0.3f), new Vector2(-0.12f, 0.12f) };
                    DiverMeshes.Part(h, "Fin", DiverMeshes.Plate("hat_fin_r2b", outline, 0.045f), fin, new Vector3(0f, 0.07f, 0f), Vector3.one, new Vector3(0f, -90f, 0f));
                    DiverMeshes.Part(h, "Cap", DiverMeshes.Cached("hat_fincap_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(new[] { new Vector2(0.2f, 0f), new Vector2(0.17f, 0.07f), new Vector2(0.09f, 0.12f), new Vector2(0f, 0.13f) }, 8), 20)), fin, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Belly", DiverMeshes.Torus(0.198f, 0.012f, 16, 3), M(LowPoly.Hex("#EDE6DA")), new Vector3(0f, 0.005f, 0f), Vector3.one, default, false);
                    break;
                }
                case 4:   // rubber duck
                {
                    Material duck = MaterialLibrary.Opaque(LowPoly.Hex("#F2C94C"), 0.4f, 0f), beak = M(LowPoly.Hex("#F28C4A"));
                    Mesh sph = DiverMeshes.Sphere(7, 12);
                    DiverMeshes.Part(h, "DuckBody", sph, duck, new Vector3(0f, 0.12f, -0.02f), new Vector3(0.26f, 0.19f, 0.32f));
                    DiverMeshes.Part(h, "DuckHead", sph, duck, new Vector3(0f, 0.27f, 0.08f), new Vector3(0.17f, 0.17f, 0.17f));
                    DiverMeshes.Part(h, "Beak", sph, beak, new Vector3(0f, 0.255f, 0.17f), new Vector3(0.09f, 0.035f, 0.08f), default, false);
                    DiverMeshes.Part(h, "Tail", sph, duck, new Vector3(0f, 0.2f, -0.16f), new Vector3(0.08f, 0.1f, 0.09f), new Vector3(-30f, 0f, 0f), false);
                    for (int s = -1; s <= 1; s += 2)
                        DiverMeshes.Part(h, "DuckEye", DiverMeshes.Sphere(4, 6), M(DiverStyle.Pupil), new Vector3(s * 0.06f, 0.295f, 0.14f), Vector3.one * 0.025f, default, false);
                    break;
                }
                case 5: case 7:   // captain's cap / Gus' original cap
                {
                    bool gus = hat == 7;
                    Material top = M(LowPoly.Hex(gus ? "#4F7FAE" : "#EDE6DA")), band = M(LowPoly.Hex(gus ? "#C94F3D" : "#1E2A44")), dark = M(LowPoly.Hex("#1E1B2B"));
                    var prof = new[] { new Vector2(0.205f, -0.01f), new Vector2(0.21f, 0.09f), new Vector2(0.245f, 0.15f), new Vector2(0.235f, 0.17f), new Vector2(0f, 0.175f) };
                    DiverMeshes.Part(h, "Crown", DiverMeshes.Cached("hat_captain_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(prof, 10), 20)), top, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Band", DiverMeshes.Torus(0.209f, 0.022f, 16, 3), band, new Vector3(0f, 0.025f, 0f), Vector3.one, default, false);
                    DiverMeshes.Part(h, "Visor", DiverMeshes.Plate("cap_visor_r2b", HalfDisc(0.15f, 0.11f, 16), 0.012f), dark, new Vector3(0f, 0.0f, 0.2f), Vector3.one, new Vector3(-75f, 0f, 0f), false);
                    DiverMeshes.Part(h, "Badge", DiverMeshes.Sphere(4, 6), MaterialLibrary.Opaque(LowPoly.Hex("#E8C65A"), 0.5f, 0.3f), new Vector3(0f, 0.06f, 0.215f), new Vector3(0.05f, 0.04f, 0.015f), default, false);
                    if (gus)   // worn: a patch on the side
                        DiverMeshes.Part(h, "Patch", DiverMeshes.Sphere(4, 6), M(LowPoly.Hex("#C9B48A")), new Vector3(0.2f, 0.1f, 0.05f), new Vector3(0.012f, 0.05f, 0.05f), default, false);
                    break;
                }
                case 8:   // party hat
                {
                    var cone = DiverMeshes.Node(h, "Cone", new Vector3(0.02f, -0.01f, 0f), new Vector3(0f, 0f, -8f));
                    DiverMeshes.Part(cone, "Body", DiverMeshes.Cached("hat_party_r3", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.15f, 0f), new Vector2(0.11f, 0.1f), new Vector2(0.06f, 0.2f), new Vector2(0.004f, 0.3f), new Vector2(0f, 0.302f) }, 20)),
                        M(LowPoly.Hex("#F29BBE")), Vector3.zero, Vector3.one);
                    DiverMeshes.Part(cone, "Pompom", DiverMeshes.Sphere(6, 10), M(LowPoly.Hex("#F2C94C")), new Vector3(0f, 0.31f, 0f), Vector3.one * 0.06f, default, false);
                    string[] cols = { "#3FB8C4", "#EDE6DA", "#F2C94C" };
                    for (int i = 0; i < 6; i++)
                    {
                        float y = 0.05f + (i % 3) * 0.07f;
                        float a = i * 1.9f;
                        float r = Mathf.Lerp(0.14f, 0.04f, y / 0.3f) + 0.002f;
                        DiverMeshes.Part(cone, "Dot", DiverMeshes.Sphere(3, 6), M(LowPoly.Hex(cols[i % 3])), new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Vector3.one * 0.022f, default, false);
                    }
                    break;
                }
                case 9:   // pirate tricorn
                {
                    Material black = M(LowPoly.Hex("#1E1B2B"));
                    DiverMeshes.Part(h, "Crown", DiverMeshes.Cached("hat_tricorn_crown_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(new[] { new Vector2(0.21f, -0.01f), new Vector2(0.205f, 0.08f), new Vector2(0.16f, 0.14f), new Vector2(0f, 0.15f) }, 8), 20)), black, Vector3.zero, Vector3.one);
                    for (int i = 0; i < 3; i++)
                    {
                        var lobe = DiverMeshes.Node(h, "Lobe" + i, Vector3.zero, new Vector3(0f, i * 120f, 0f));
                        DiverMeshes.Part(lobe, "Brim", DiverMeshes.Plate("tricorn_lobe", new[] { new Vector2(-0.2f, 0f), new Vector2(0.2f, 0f), new Vector2(0.14f, 0.1f), new Vector2(0f, 0.13f), new Vector2(-0.14f, 0.1f) }, 0.012f),
                            black, new Vector3(0f, 0.0f, 0.2f), Vector3.one, new Vector3(-20f, 0f, 0f), false);
                    }
                    DiverMeshes.Part(h, "Skull", DiverMeshes.Sphere(4, 6), M(LowPoly.Hex("#EDE6DA")), new Vector3(0f, 0.08f, 0.205f), new Vector3(0.05f, 0.05f, 0.012f), default, false);
                    break;
                }
                case 11:   // seagull nest „Günther“
                {
                    Material straw = M(LowPoly.Hex("#8E6C50")), white = M(LowPoly.Hex("#EDE6DA")), yellow = M(LowPoly.Hex("#F2C94C"));
                    DiverMeshes.Part(h, "Nest", DiverMeshes.Torus(0.18f, 0.05f, 16, 5), straw, new Vector3(0f, 0.02f, 0f), new Vector3(1f, 0.8f, 1f));
                    for (int i = 0; i < 6; i++)
                        DiverMeshes.Part(h, "Twig", DiverMeshes.SmoothCapsule(0.006f, 0.12f, 4, 2, 1), straw, new Vector3(Mathf.Cos(i * 1.1f) * 0.2f, 0.03f, Mathf.Sin(i * 1.1f) * 0.2f), Vector3.one, new Vector3(70f, i * 60f, 0f), false);
                    var gull = DiverMeshes.Node(h, "Gull", new Vector3(0f, 0.09f, 0f));
                    Mesh sph = DiverMeshes.Sphere(6, 10);
                    DiverMeshes.Part(gull, "Body", sph, white, Vector3.zero, new Vector3(0.16f, 0.13f, 0.24f));
                    DiverMeshes.Part(gull, "Wing", sph, M(LowPoly.Hex("#A9AEB4")), new Vector3(0f, 0.03f, -0.02f), new Vector3(0.17f, 0.06f, 0.18f), default, false);
                    DiverMeshes.Part(gull, "Head", sph, white, new Vector3(0f, 0.09f, 0.1f), Vector3.one * 0.1f);
                    DiverMeshes.Part(gull, "Beak", DiverMeshes.Taper(0.012f, 0.002f, 10), yellow, new Vector3(0f, 0.085f, 0.145f), new Vector3(1f, 0.06f, 1f), new Vector3(90f, 0f, 0f), false);
                    for (int s = -1; s <= 1; s += 2)
                        DiverMeshes.Part(gull, "Eye", DiverMeshes.Sphere(3, 4), M(DiverStyle.Pupil), new Vector3(s * 0.034f, 0.105f, 0.125f), Vector3.one * 0.016f, default, false);
                    break;
                }
                case 12:   // crown
                {
                    Material gold = MaterialLibrary.Opaque(LowPoly.Hex("#E8C65A"), 0.6f, 0.45f);
                    DiverMeshes.Part(h, "Ring", DiverMeshes.Cached("hat_crown_ring_r3", () => DiverMeshes.LatheY(new[] { new Vector2(0.2f, -0.01f), new Vector2(0.215f, 0.0f), new Vector2(0.215f, 0.07f), new Vector2(0.2f, 0.075f) }, 20)), gold, Vector3.zero, Vector3.one);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * Mathf.PI * 2f / 5f;
                        DiverMeshes.Part(h, "Spike", DiverMeshes.Cached("crown_spike_r3", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.035f, 0f), new Vector2(0.02f, 0.06f), new Vector2(0f, 0.1f) }, 8)),
                            gold, new Vector3(Mathf.Sin(a) * 0.205f, 0.07f, Mathf.Cos(a) * 0.205f), Vector3.one, default, false);
                        DiverMeshes.Part(h, "Ball", DiverMeshes.Sphere(3, 6), gold, new Vector3(Mathf.Sin(a) * 0.205f, 0.175f, Mathf.Cos(a) * 0.205f), Vector3.one * 0.022f, default, false);
                    }
                    DiverMeshes.Part(h, "Gem", DiverMeshes.Sphere(4, 6), MaterialLibrary.Opaque(LowPoly.Hex("#E0607E"), 0.8f, 0f), new Vector3(0f, 0.035f, 0.218f), new Vector3(0.04f, 0.04f, 0.02f), default, false);
                    break;
                }
                case 13:   // Pearl's straw hat
                {
                    Material brim = M(LowPoly.Hex("#E2C890")), crown = M(LowPoly.Hex("#D8B977")), band = M(LowPoly.Hex("#6E9445"));
                    DiverMeshes.Part(h, "Brim", DiverMeshes.Cached("hat_straw_brim_r3", () => DiverMeshes.LatheY(new[] { new Vector2(0.2f, 0.01f), new Vector2(0.3f, -0.005f), new Vector2(0.4f, -0.035f), new Vector2(0.41f, -0.03f), new Vector2(0.3f, 0.005f), new Vector2(0.2f, 0.02f) }, 24)), brim, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Crown", DiverMeshes.Cached("hat_straw_crown_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(new[] { new Vector2(0.205f, 0f), new Vector2(0.2f, 0.1f), new Vector2(0.16f, 0.14f), new Vector2(0f, 0.15f) }, 8), 20)), crown, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Band", DiverMeshes.Torus(0.205f, 0.016f, 16, 3), band, new Vector3(0f, 0.03f, 0f), Vector3.one, default, false);
                    DiverMeshes.Part(h, "Flower", DiverMeshes.Sphere(4, 6), M(LowPoly.Hex("#E86FA0")), new Vector3(0.15f, 0.04f, 0.14f), Vector3.one * 0.045f, default, false);
                    break;
                }
                default:   // 14 paper crown
                {
                    var paper = MaterialLibrary.Opaque(LowPoly.Hex("#F4EFE6"), 0.05f, 0f);
                    var pr = DiverMeshes.Part(h, "Paper", DiverMeshes.Cached("hat_paper_crown", PaperCrown), paper, Vector3.zero, Vector3.one);
                    pr.sharedMaterial = paper;
                    DiverMeshes.Part(h, "Line", DiverMeshes.Torus(0.214f, 0.004f, 16, 3), M(LowPoly.Hex("#C9B48A")), new Vector3(0f, 0.03f, 0f), Vector3.one, default, false);
                    break;
                }
            }
        }

        /// A zig-zag paper band (double sided, smooth normals pointing outward / inward).
        static Mesh PaperCrown()
        {
            const int teeth = 7, segPerTooth = 4;
            int n = teeth * segPerTooth;
            const float r = 0.212f, baseH = 0.05f, peak = 0.13f;
            var v = new List<Vector3>();
            var nr = new List<Vector3>();
            var t = new List<int>();
            for (int side = 0; side < 2; side++)
            {
                float rr = side == 0 ? r + 0.002f : r - 0.002f;
                int start = v.Count;
                for (int i = 0; i <= n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    float u = (i % segPerTooth) / (float)segPerTooth;
                    float top = baseH + (peak - baseH) * (1f - Mathf.Abs(u * 2f - 1f));
                    var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                    v.Add(dir * rr); nr.Add(side == 0 ? dir : -dir);
                    v.Add(dir * rr + Vector3.up * top); nr.Add(side == 0 ? dir : -dir);
                }
                for (int i = 0; i < n; i++)
                {
                    int a0 = start + i * 2, b0 = a0 + 2;
                    if (side == 0) { t.Add(a0); t.Add(a0 + 1); t.Add(b0 + 1); t.Add(a0); t.Add(b0 + 1); t.Add(b0); }
                    else { t.Add(a0); t.Add(b0 + 1); t.Add(a0 + 1); t.Add(a0); t.Add(b0); t.Add(b0 + 1); }
                }
            }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(nr);
            m.SetTriangles(t, 0);
            // make every face front-facing toward its normal
            var tris = m.triangles;
            var verts = m.vertices;
            var norms = m.normals;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 f = Vector3.Cross(verts[tris[i + 1]] - verts[tris[i]], verts[tris[i + 2]] - verts[tris[i]]);
                if (Vector3.Dot(f, norms[tris[i]]) < 0f) { int tmp = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = tmp; }
            }
            m.triangles = tris;
            m.RecalculateBounds();
            return m;
        }

        // ------------------------------------------------------------------ limbs
        static void BuildArm(Transform shoulder, out Transform elbow, out Transform hand, Material neo, Material cuff)
        {
            DiverMeshes.Limb(shoulder, "UpperArm", 0.062f, UpperArm, neo);
            elbow = DiverMeshes.Node(shoulder, "Elbow", new Vector3(0f, -UpperArm, 0f));
            // ball joint (radius = limb radius × 1.08): bends never show a gap
            DiverMeshes.Part(elbow, "Joint", DiverMeshes.Sphere(4, 6), neo, Vector3.zero, Vector3.one * (0.062f * 1.08f * 2f), default, false);
            DiverMeshes.Limb(elbow, "ForeArm", 0.056f, ForeArm, neo);
            DiverMeshes.Part(elbow, "Cuff", DiverMeshes.Torus(0.056f, 0.014f, 16, 3), cuff, new Vector3(0f, -ForeArm + 0.02f, 0f), Vector3.one, default, false);
            hand = DiverMeshes.Node(elbow, "Hand", new Vector3(0f, -ForeArm - 0.05f, 0f));
            // neoprene gloves everywhere (Q1/Q2): a bit lighter than the suit, never skin-coloured
            Material glove = Neo(DiverGear.Glove);
            DiverMeshes.Part(hand, "Mitten", DiverMeshes.Sphere(7, 12), glove, new Vector3(0f, -0.02f, 0.005f), new Vector3(0.12f, 0.15f, 0.095f));
            DiverMeshes.Part(hand, "Thumb", DiverMeshes.SmoothCapsule(0.022f, 0.07f, 8, 2, 1), glove, new Vector3(0f, -0.01f, 0.04f), Vector3.one, new Vector3(150f, 0f, 0f), false);
        }

        static void BuildLeg(Transform hip, out Transform knee, Material neo, Material stripe, Material boots, DiverLook look, bool left)
        {
            DiverMeshes.Limb(hip, "Thigh", 0.11f, Thigh, neo);
            knee = DiverMeshes.Node(hip, "Knee", new Vector3(0f, -Thigh, 0f));
            DiverMeshes.Part(knee, "Joint", DiverMeshes.Sphere(4, 6), neo, Vector3.zero, Vector3.one * (0.1f * 1.08f * 2f), default, false);
            DiverMeshes.Limb(knee, "Shin", 0.1f, Shin - 0.04f, neo);
            DiverMeshes.Part(knee, "Stripe", DiverMeshes.Torus(0.101f, 0.016f, 16, 3), stripe, new Vector3(0f, -0.07f, 0f), Vector3.one, default, false);
            var foot = DiverMeshes.Node(knee, "Foot", new Vector3(0f, -Shin + 0.02f, 0f));
            DiverMeshes.Part(foot, "Pocket", DiverMeshes.SmoothCapsule(0.088f, 0.25f, 10, 3, 1), boots, new Vector3(0f, -0.005f, -0.06f), Vector3.one, new Vector3(90f, 0f, 0f));
            // fins by tier (Q51) or the colour picked on the character screen – DiverGear
            DiverGear.BuildFin(foot, look, boots, left);
        }

        // ------------------------------------------------------------------ suit patterns (1c, 8 options)
        static readonly Dictionary<string, Material> patternMats = new Dictionary<string, Material>();

        /// The material of the suit panel: plain suit colour (pattern 0) or a small procedural pattern texture in suit +
        /// accent colour (stripes, dots, shark teeth, herringbone, waves, gold, lighthouse). Cached per combination.
        public static Material PatternMaterial(int pattern, Color suit, Color accentHint)
        {
            if (pattern <= 0) return Neo(suit);
            string key = pattern + "_" + ColorUtility.ToHtmlStringRGB(suit) + "_" + ColorUtility.ToHtmlStringRGB(accentHint);
            if (patternMats.TryGetValue(key, out var m) && m != null) return m;
            Color baseC = pattern == 6 ? LowPoly.Hex("#E8C65A") : suit;
            Color accent = PatternAccent(baseC, accentHint, pattern);
            var tex = PatternTexture(pattern, baseC, accent);
            m = MaterialLibrary.Textured(tex, Color.white, pattern == 6 ? 0.5f : NeopreneSmoothness, pattern == 6 ? 0.25f : 0f);
            m.name = "SB_DiverPattern_" + pattern;
            patternMats[key] = m;
            return m;
        }

        static Color PatternAccent(Color baseC, Color cuff, int pattern)
        {
            if (pattern == 7) return LowPoly.Hex("#C94F3D");
            if (pattern == 6) return LowPoly.Hex("#FFF1B8");
            // the cuff colour when it contrasts enough, else a light / dark version of the suit
            float lb = baseC.r * 0.3f + baseC.g * 0.59f + baseC.b * 0.11f;
            float lc = cuff.r * 0.3f + cuff.g * 0.59f + cuff.b * 0.11f;
            if (Mathf.Abs(lb - lc) > 0.18f) return cuff;
            return lb > 0.55f ? Color.Lerp(baseC, Color.black, 0.38f) : Color.Lerp(baseC, Color.white, 0.6f);
        }

        /// 256 × 160 texture over the body UVs (u = angle around the bean ≈ 1.76 m, v = profile arc ≈ 1.36 m).
        static Texture2D PatternTexture(int pattern, Color baseC, Color accent)
        {
            const int W = 256, H = 160;
            const float Lu = 1.76f, Lv = 1.36f;
            var px = new Color32[W * H];
            float pix = Lu / W;   // metres per pixel (horizontal)
            for (int y = 0; y < H; y++)
            {
                float Y = (y + 0.5f) / H * Lv;
                for (int x = 0; x < W; x++)
                {
                    float X = (x + 0.5f) / W * Lu;
                    float cover = Coverage(pattern, X, Y, pix);
                    Color c = Color.Lerp(baseC, accent, cover);
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "SB_DiverPattern_" + pattern, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 2 };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        static float Edge(float d, float pix) => Mathf.Clamp01(0.5f - d / (pix * 1.5f));

        /// 0..1 accent coverage of a pattern at body-surface metres (X around, Y up along the profile).
        static float Coverage(int pattern, float X, float Y, float pix)
        {
            switch (pattern)
            {
                case 1:   // stripes: 4.5 cm bands every 12 cm
                {
                    float f = Mathf.Repeat(Y, 0.12f) - 0.06f;
                    return Edge(Mathf.Abs(f) - 0.0225f, pix);
                }
                case 2:   // dots: 2.6 cm dots on a staggered 10 cm grid
                {
                    float row = Mathf.Floor(Y / 0.1f);
                    float ox = (row % 2f) * 0.05f;
                    float dx = Mathf.Repeat(X + ox, 0.1f) - 0.05f, dy = Mathf.Repeat(Y, 0.1f) - 0.05f;
                    return Edge(Mathf.Sqrt(dx * dx + dy * dy) - 0.026f, pix);
                }
                case 3:   // shark teeth: a band of white teeth across the chest
                {
                    const float top = 0.98f, len = 0.085f;
                    if (Y > top + 0.03f || Y < top - len - 0.01f) return 0f;
                    float u = Mathf.Repeat(X, 0.09f) / 0.09f;
                    float toothTop = top, toothTip = top - len * (1f - Mathf.Abs(u * 2f - 1f));
                    float band = Edge(Mathf.Abs(Y - (top + 0.012f)) - 0.012f, pix);
                    float tooth = Y <= toothTop && Y >= toothTip ? 1f : Edge(Mathf.Min(Mathf.Abs(Y - toothTop), Mathf.Abs(Y - toothTip)), pix) * 0.5f;
                    return Mathf.Max(band, tooth);
                }
                case 4:   // herringbone: chevron lines
                {
                    float u = Mathf.Repeat(X, 0.1f) / 0.1f;
                    float f = Mathf.Repeat(Y + Mathf.Abs(u * 2f - 1f) * 0.05f, 0.09f) - 0.045f;
                    return Edge(Mathf.Abs(f) - 0.011f, pix);
                }
                case 5:   // waves
                {
                    float f = Mathf.Repeat(Y + Mathf.Sin(X / 0.22f * Mathf.PI * 2f) * 0.022f, 0.12f) - 0.06f;
                    return Edge(Mathf.Abs(f) - 0.013f, pix);
                }
                case 6:   // gold: soft diagonal sheen bands
                {
                    float f = Mathf.Repeat(X * 0.7f + Y, 0.32f) - 0.16f;
                    return 0.35f * Edge(Mathf.Abs(f) - 0.03f, pix * 6f);
                }
                case 7:   // lighthouse: broad red bands
                {
                    float f = Mathf.Repeat(Y, 0.26f) - 0.13f;
                    return Edge(Mathf.Abs(f) - 0.065f, pix);
                }
                default: return 0f;
            }
        }
    }
}
