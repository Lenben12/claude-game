using System;
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
        /// Hats of the character screen (CosmeticsDatabase.HatCount): 0 none … 19 sun visor.
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
            return new DiverLook { suit = suit, cuff = Color.Lerp(suit, Color.white, 0.55f), hat = hat, face = face, snorkel = true,
                                   hair = (index * 3 + 1) % (CosmeticsDatabase.HairCount - 1), hairColor = (index * 2 + 1) % CosmeticsDatabase.HairColors.Length };
        }
    }

    /// Builds the diver character and installs DiverModels.Build. The root sits at the feet, +Z forward; it carries a
    /// DiverPuppet. Round 3 (GDD §19 „Figuren schöner und realistischer – weder klotzig noch zu kugelig“): How-to-Fish
    /// bean-human proportions instead of the round bean – 1.85 m, a tall egg head on a neck, a wetsuit torso with
    /// defined shoulders (pelvis + chest on a spine joint so the upper body can twist and lean), tapered arms and legs
    /// with elbows / knees, mitten gloves, a readable face (eyes, lids, brows, nose, ears, mouth), hair that shows under
    /// every hat, beards and accessories; body type and height options. Smooth shading everywhere, LOD0 ≤ 9,000
    /// triangles incl. the biggest loadout + LOD1/LOD2 proxies (DiverLod). Meshes / pattern textures are cached.
    /// Hierarchy: root (DiverPuppet) → Rig (height scale) → Body (pelvis pivot) → {HipL/R → Knee → Foot, Chest → {Neck,
    /// Head → face / hair / mask / hat, ShoulderL/R → Elbow → Hand, gear}}.
    public static class DiverBuilder
    {
        // ------------------------------------------------------------------ dimensions (character space, metres, height scale 1)
        public const float PivotY = 0.98f;          // pelvis centre (swim pivot)
        public const float ChestY = 1.10f;          // spine joint at the waist
        public const float HeadY = 1.53f;           // head pivot (top of the neck)
        public const float CrownY = 1.866f;
        public const float ShoulderY = 1.40f, ShoulderX = 0.215f;
        public const float HipY = 0.92f, HipX = 0.10f;
        public const float UpperArm = 0.29f, ForeArm = 0.26f;
        public const float Thigh = 0.42f, Shin = 0.42f;
        public const float EyeY = 1.715f, EyeX = 0.047f, EyeR = 0.036f, PupilR = 0.018f;
        public const float HatY = 1.775f, MouthY = 1.598f, NoseY = 1.655f;
        /// Torso depth / width (the lathe is scaled by this in z; per body type in Shape).
        public const float TorsoDepth = 0.70f;
        /// Distance of the back surface from the pelvis axis (lying down: the pelvis pivot rests this high above the bed).
        public const float BackDepth = 0.12f;

        public const float NeopreneSmoothness = 0.18f, SkinSmoothness = 0.12f, HairSmoothness = 0.16f;

        /// Width profile (radius, height) of the pelvis part (crotch → waist) and the chest part (waist → collar). They
        /// overlap at the waist (the weight belt hides the seam); scaled per body type by ShapeScale.
        static readonly Vector2[] PelvisProfile =
        {
            new Vector2(0.000f, 0.795f), new Vector2(0.078f, 0.800f), new Vector2(0.132f, 0.815f), new Vector2(0.160f, 0.850f),
            new Vector2(0.172f, 0.900f), new Vector2(0.170f, 0.955f), new Vector2(0.160f, 1.010f), new Vector2(0.150f, 1.065f),
            new Vector2(0.140f, 1.110f), new Vector2(0.095f, 1.140f), new Vector2(0.000f, 1.150f),
        };

        static readonly Vector2[] ChestProfile =
        {
            new Vector2(0.000f, 1.000f), new Vector2(0.100f, 1.008f), new Vector2(0.146f, 1.030f), new Vector2(0.157f, 1.075f),
            new Vector2(0.163f, 1.140f), new Vector2(0.178f, 1.220f), new Vector2(0.193f, 1.300f), new Vector2(0.203f, 1.365f),
            new Vector2(0.203f, 1.415f), new Vector2(0.182f, 1.452f), new Vector2(0.132f, 1.477f), new Vector2(0.075f, 1.492f),
            new Vector2(0.000f, 1.500f),
        };

        /// A tall egg head (How to Fish): chin 1.528 → crown 1.866, widest (r 0.125) at the temples.
        static readonly Vector2[] HeadProfile =
        {
            new Vector2(0.000f, 1.528f), new Vector2(0.040f, 1.531f), new Vector2(0.075f, 1.545f), new Vector2(0.100f, 1.570f),
            new Vector2(0.114f, 1.605f), new Vector2(0.121f, 1.645f), new Vector2(0.1245f, 1.690f), new Vector2(0.124f, 1.735f),
            new Vector2(0.117f, 1.780f), new Vector2(0.102f, 1.818f), new Vector2(0.076f, 1.846f), new Vector2(0.040f, 1.862f),
            new Vector2(0.000f, 1.866f),
        };

        /// Body type: width factors at hips / waist / chest / shoulders, torso depth and limb thickness.
        public struct Shape
        {
            public float hip, waist, chest, shoulder, depth, limb;
        }

        public static Shape ShapeOf(int build)
        {
            switch (build)
            {
                case 1: return new Shape { hip = 0.93f, waist = 0.88f, chest = 0.9f, shoulder = 0.92f, depth = 0.68f, limb = 0.88f };   // slim
                case 2: return new Shape { hip = 1.08f, waist = 1.17f, chest = 1.1f, shoulder = 1.05f, depth = 0.8f, limb = 1.12f };    // sturdy
                case 3: return new Shape { hip = 0.98f, waist = 0.97f, chest = 1.09f, shoulder = 1.15f, depth = 0.72f, limb = 1.08f };  // broad
                default: return new Shape { hip = 1f, waist = 1f, chest = 1f, shoulder = 1f, depth = TorsoDepth, limb = 1f };
            }
        }

        static float ShapeScale(float y, Shape s)
        {
            if (y <= 0.88f) return s.hip;
            if (y <= 1.06f) return Mathf.Lerp(s.hip, s.waist, (y - 0.88f) / 0.18f);
            if (y <= 1.25f) return Mathf.Lerp(s.waist, s.chest, (y - 1.06f) / 0.19f);
            if (y <= 1.39f) return Mathf.Lerp(s.chest, s.shoulder, (y - 1.25f) / 0.14f);
            return Mathf.Lerp(s.shoulder, 1f, Mathf.Clamp01((y - 1.39f) / 0.11f));
        }

        static float ProfileR(Vector2[] prof, float y)
        {
            if (y <= prof[0].y) return prof[0].x;
            for (int i = 0; i < prof.Length - 1; i++)
            {
                Vector2 a = prof[i], b = prof[i + 1];
                if (y <= b.y) return Mathf.Lerp(a.x, b.x, (y - a.y) / Mathf.Max(1e-4f, b.y - a.y));
            }
            return prof[prof.Length - 1].x;
        }

        /// Half width of the torso (x) at an absolute height for a body type – DiverGear places straps / tanks with it.
        public static float TorsoRX(float y, int build = 0)
        {
            var s = ShapeOf(build);
            float r = y < 1.05f ? ProfileR(PelvisProfile, y) : Mathf.Max(ProfileR(ChestProfile, y), y < 1.11f ? ProfileR(PelvisProfile, y) : 0f);
            return r * ShapeScale(y, s);
        }

        /// Half depth of the torso (z) at an absolute height.
        public static float TorsoRZ(float y, int build = 0) => TorsoRX(y, build) * ShapeOf(build).depth;

        /// Legacy name used by older code: the side radius of the torso.
        public static float BodyRadiusAt(float y) => TorsoRX(y);

        /// Surface radius of the head at an absolute height (for face parts, hair, hats).
        public static float HeadRadius(float y) => ProfileR(HeadProfile, y);

        static Vector2[] Scaled(Vector2[] prof, Shape s)
        {
            var o = new Vector2[prof.Length];
            for (int i = 0; i < prof.Length; i++) o[i] = new Vector2(prof[i].x * ShapeScale(prof[i].y, s), prof[i].y);
            return o;
        }

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
        static Material HairMat(Color c) => MaterialLibrary.Opaque(c, HairSmoothness, 0f);

        /// Hats that cover the top of the head (spikes / curls on top / the high bun hide under them).
        public static bool HatCoversTop(int hat) => hat != 0 && hat != 11 && hat != 12 && hat != 14 && hat != 17 && hat != 19;

        static float HairThickness(int hair)
        {
            switch (hair)
            {
                case 2: case 7: return 0.016f;
                case 1: return 0.009f;
                case 5: case 6: return 0.012f;
                case CosmeticsDatabase.HairBald: return 0f;
                default: return 0.011f;
            }
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
            Color hairC = CosmeticsDatabase.HairColors[Mathf.Clamp(look.hairColor, 0, CosmeticsDatabase.HairColors.Length - 1)];
            Shape shape = ShapeOf(look.build);

            var root = new GameObject("BeanDiver");
            var rt = root.transform;
            var p = root.AddComponent<DiverPuppet>();
            p.look = look;
            p.hat = hat;
            p.eyeStyle = Mathf.Clamp(look.eyes, 0, CosmeticsDatabase.EyesCount - 1);
            p.browStyle = Mathf.Clamp(look.brows, 0, CosmeticsDatabase.BrowsCount - 1);
            p.baseMouth = Mathf.Clamp(look.mouth, 0, CosmeticsDatabase.MouthCount - 1);
            p.heightScale = look.HeightScale;

            Material neo = Neo(DiverStyle.Neoprene), suit = Neo(look.suit), cuff = Neo(cuffC), skinM = SkinMat(skin);
            Material panel = PatternMaterial(Mathf.Clamp(look.pattern, 0, CosmeticsDatabase.PatternCount - 1), look.suit, cuffC);
            Material boots = Neo(DiverStyle.Boots), white = M(DiverStyle.EyeWhite), ink = M(DiverStyle.Pupil);
            Material hairM = HairMat(hairC);

            // ---- rig (height option) + pelvis
            p.rig = DiverMeshes.Node(rt, "Rig", Vector3.zero);
            p.rig.localScale = Vector3.one * p.heightScale;
            p.pivot = DiverMeshes.Node(p.rig, "Body", new Vector3(0f, PivotY, 0f));
            var pelvis = DiverMeshes.Part(p.pivot, "Pelvis", DiverMeshes.Cached("torso_pelvis_b" + look.build, () => BuildPelvisMesh(shape)), neo,
                                          new Vector3(0f, -PivotY, 0f), new Vector3(1f, 1f, shape.depth));
            _ = pelvis;
            // weight belt over the waist seam + buckle
            float beltY = 1.058f, beltR = TorsoRX(beltY, look.build) + 0.006f;
            DiverMeshes.Part(p.pivot, "Belt", DiverMeshes.Torus(beltR, 0.017f, 14, 3), M(LowPoly.Hex("#2B2533")), new Vector3(0f, beltY - PivotY, 0f), new Vector3(1f, 1.15f, shape.depth), default, false);
            DiverMeshes.Part(p.pivot, "Buckle", DiverMeshes.Sphere(3, 6), MaterialLibrary.Opaque(LowPoly.Hex("#B8BEC6"), 0.5f, 0.4f),
                             new Vector3(0f, beltY - PivotY, beltR * shape.depth + 0.012f), new Vector3(0.05f, 0.036f, 0.016f), default, false);

            // ---- chest (spine joint at the waist): suit colour panel, side stripes, collar
            p.chest = DiverMeshes.Node(p.pivot, "Chest", new Vector3(0f, ChestY - PivotY, 0f));
            var chestR = DiverMeshes.Part(p.chest, "Torso", DiverMeshes.Cached("torso_chest_b" + look.build, () => BuildChestMesh(shape)), neo,
                                          new Vector3(0f, -ChestY, 0f), new Vector3(1f, 1f, shape.depth));
            chestR.sharedMaterials = new[] { neo, panel, cuff };
            // a back zip (cuff colour) – tidy suit detail
            DiverMeshes.Part(p.chest, "Zip", DiverMeshes.SmoothCapsule(0.006f, 0.30f, 6, 1, 1), cuff,
                             new Vector3(0f, 1.13f - ChestY, -TorsoRZ(1.28f, look.build) - 0.002f), Vector3.one, new Vector3(-6f, 0f, 0f), false);
            // neck (skin) + wetsuit collar
            DiverMeshes.Part(p.chest, "Neck", DiverMeshes.SmoothCapsule(0.05f, 0.16f, 12, 2, 1), skinM, new Vector3(0f, 1.435f - ChestY, -0.004f), Vector3.one, default, false);
            DiverMeshes.Part(p.chest, "Collar", DiverMeshes.Torus(0.064f, 0.017f, 12, 3), cuff, new Vector3(0f, 1.497f - ChestY, -0.004f), Vector3.one, default, false);
            // visible gear (Q51): harness, tank(s) by tier, strap lamp / camera, dive scooter – DiverGear
            DiverGear.BuildBack(p, look, suit);
            BuildSponsorPatch(p, look);

            // ---- head
            p.head = DiverMeshes.Node(p.chest, "Head", new Vector3(0f, HeadY - ChestY, 0f));
            DiverMeshes.Part(p.head, "HeadBean", DiverMeshes.Cached("head_r3", () => DiverMeshes.LatheY(DiverMeshes.Resample(HeadProfile, 16), 18)), skinM, new Vector3(0f, -HeadY, 0f), Vector3.one);
            BuildEars(p, skinM);
            BuildFace(p, look, skin, hairC, white, ink);
            BuildNose(p, look.nose, skin);
            BuildHair(p, look.hair, hat, hairM);
            BuildBeard(p, look.beard, skin, hairC);
            BuildMask(p, look);
            BuildHat(p, hat, HairThickness(look.hair));
            BuildAccessory(p, look, skin);

            // ---- arms
            float sx = ShoulderX * shape.shoulder;
            p.shoulderL = DiverMeshes.Node(p.chest, "ShoulderL", new Vector3(-sx, ShoulderY - ChestY, 0f));
            p.shoulderR = DiverMeshes.Node(p.chest, "ShoulderR", new Vector3(sx, ShoulderY - ChestY, 0f));
            BuildArm(p.shoulderL, out p.elbowL, out p.handL, neo, cuff, suit, shape.limb);
            BuildArm(p.shoulderR, out p.elbowR, out p.handR, neo, cuff, suit, shape.limb);
            if (look.accessory == 5) BuildWatch(p.elbowL, shape.limb);

            // ---- legs
            float hx = HipX * Mathf.Lerp(1f, shape.hip, 0.7f);
            p.hipL = DiverMeshes.Node(p.pivot, "HipL", new Vector3(-hx, HipY - PivotY, 0f));
            p.hipR = DiverMeshes.Node(p.pivot, "HipR", new Vector3(hx, HipY - PivotY, 0f));
            BuildLeg(p.hipL, out p.kneeL, out p.footL, neo, panel, boots, look, true, shape.limb);
            BuildLeg(p.hipR, out p.kneeR, out p.footR, neo, panel, boots, look, false, shape.limb);

            p.InitRig();
            // L.13: LOD1 (~12 m) / LOD2 (~35 m) proxies on the same bones – a crowd of avatars / NPCs stays cheap
            DiverLod.Build(root);
            return root;
        }

        static Mesh BuildPelvisMesh(Shape s) => DiverMeshes.LatheY(DiverMeshes.Resample(Scaled(PelvisProfile, s), 12), 18);

        static Mesh BuildChestMesh(Shape s)
        {
            // submeshes: 0 neoprene (lower back, side stripes), 1 suit colour / pattern (chest + back panel), 2 cuff (collar band)
            const int segs = 18;
            var prof = DiverMeshes.Resample(Scaled(ChestProfile, s), 16);
            return DiverMeshes.LatheY(prof, segs, 3, (i, j) =>
            {
                float y = (prof[i].y + prof[Mathf.Min(prof.Length - 1, i + 1)].y) * 0.5f;
                if (y < 1.085f) return 0;
                if (y >= 1.47f) return 2;
                float a = (j + 0.5f) / segs * Mathf.PI * 2f;
                return Mathf.Abs(Mathf.Cos(a)) > 0.95f ? 0 : 1;
            });
        }

        /// K.7 / Q90: the sponsor's logo patch on the left chest (look.sponsor = DiverSponsor.CurrentIndex, 0 = none).
        static void BuildSponsorPatch(DiverPuppet p, DiverLook look)
        {
            var sp = DiverSponsor.Def(look.sponsor);
            if (sp == null) return;
            const float y = 1.31f;
            float rx = TorsoRX(y, look.build), rz = TorsoRZ(y, look.build);
            // a point on the elliptical cross-section, left side toward the front; normal of the ellipse there
            float ang = 145f * Mathf.Deg2Rad;   // from +X toward +Z: left-front
            Vector3 pt = new Vector3(Mathf.Cos(ang) * rx, 0f, Mathf.Sin(ang) * rz);
            Vector3 n = new Vector3(pt.x / (rx * rx), 0f, pt.z / (rz * rz)).normalized;
            Vector3 pos = pt + n * 0.005f + new Vector3(0f, y - ChestY, 0f);
            Quaternion rot = Quaternion.LookRotation(-n, Vector3.up) * Quaternion.Euler(8f, 0f, 0f);
            var r = DiverMeshes.Part(p.chest, "SponsorPatch", DiverMeshes.Quad("sponsor_patch", 0.07f, 0.07f), DiverSponsor.Material(sp), pos, Vector3.one, default, false);
            r.transform.localRotation = rot;
        }

        // ------------------------------------------------------------------ head surface helpers
        /// A point on the head surface (head-pivot space) at an azimuth (0 = front, + = toward the diver's left… the
        /// model's +X) and absolute height, pushed out along the surface normal by `out`.
        static Vector3 HeadPoint(float azDeg, float y, float outward = 0f)
        {
            EnsureHeadArc();
            float s = ArcAtY(y);
            HeadAt(s, out Vector2 pt, out Vector2 n);
            float a = azDeg * Mathf.Deg2Rad;
            float rr = pt.x + n.x * outward;
            return new Vector3(Mathf.Sin(a) * rr, pt.y + n.y * outward - HeadY, Mathf.Cos(a) * rr);
        }

        static Vector3 HeadNormal(float azDeg, float y)
        {
            EnsureHeadArc();
            HeadAt(ArcAtY(y), out _, out Vector2 n);
            float a = azDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * n.x, n.y, Mathf.Cos(a) * n.x).normalized;
        }

        static Vector2[] hp;
        static float[] hpArc;
        static float hpLen;

        static void EnsureHeadArc()
        {
            if (hp != null) return;
            hp = DiverMeshes.Resample(HeadProfile, 40);
            hpArc = new float[hp.Length];
            for (int i = 1; i < hp.Length; i++) hpArc[i] = hpArc[i - 1] + Vector2.Distance(hp[i], hp[i - 1]);
            hpLen = hpArc[hp.Length - 1];
        }

        static float ArcAtY(float y)
        {
            if (y <= hp[0].y) return 0f;
            for (int i = 1; i < hp.Length; i++)
            {
                if (hp[i].y >= y)
                {
                    float t = (y - hp[i - 1].y) / Mathf.Max(1e-5f, hp[i].y - hp[i - 1].y);
                    return Mathf.Lerp(hpArc[i - 1], hpArc[i], t);
                }
            }
            return hpLen;
        }

        static void HeadAt(float s, out Vector2 pt, out Vector2 n)
        {
            s = Mathf.Clamp(s, 0f, hpLen);
            int i = 1;
            while (i < hp.Length - 1 && hpArc[i] < s) i++;
            float t = (s - hpArc[i - 1]) / Mathf.Max(1e-6f, hpArc[i] - hpArc[i - 1]);
            pt = Vector2.Lerp(hp[i - 1], hp[i], t);
            Vector2 tg = hp[i] - hp[i - 1];
            n = new Vector2(tg.y, -tg.x);
            if (n.sqrMagnitude < 1e-12f) n = Vector2.up;
            n.Normalize();
            if (s >= hpLen - 1e-4f) n = Vector2.up;
        }

        /// A shell over the head surface (hair cap, beard): columns over the azimuth range, rows from the lower edge
        /// (height low(az)) to the upper edge (height high(az); ≥ CrownY = the crown pole). Thickness tapers to `edge`
        /// at the lower rim. Smooth analytic normals. Head-pivot space.
        static Mesh Shell(int cols, float azMin, float azMax, bool wrap, Func<float, float> low, Func<float, float> high, float thick, float edge, int rows = 7)
        {
            EnsureHeadArc();
            int cc = wrap ? cols : cols + 1;
            var v = new List<Vector3>((rows + 1) * cc);
            var nr = new List<Vector3>(v.Capacity);
            for (int i = 0; i <= rows; i++)
            {
                float t = i / (float)rows;
                for (int j = 0; j < cc; j++)
                {
                    float az = Mathf.Lerp(azMin, azMax, j / (float)cols);
                    float s0 = ArcAtY(low(az)), s1 = high(az) >= CrownY ? hpLen : ArcAtY(high(az));
                    float s = Mathf.Lerp(s0, s1, t);
                    HeadAt(s, out Vector2 pt, out Vector2 n);
                    float th = thick * Mathf.Lerp(edge, 1f, Mathf.SmoothStep(0f, 1f, t / 0.25f));
                    if (!wrap) th *= Mathf.Lerp(edge, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Min(j, cols - j) / 2f));
                    float a = az * Mathf.Deg2Rad;
                    float rr = pt.x + n.x * th;
                    v.Add(new Vector3(Mathf.Sin(a) * rr, pt.y + n.y * th - HeadY, Mathf.Cos(a) * rr));
                    nr.Add(new Vector3(Mathf.Sin(a) * n.x, n.y, Mathf.Cos(a) * n.x).normalized);
                }
            }
            var tris = new List<int>(rows * cols * 6);
            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    int j1 = wrap ? (j + 1) % cols : j + 1;
                    int a = i * cc + j, b = i * cc + j1, c = (i + 1) * cc + j, d = (i + 1) * cc + j1;
                    AddTri(tris, v, nr, a, b, c);
                    AddTri(tris, v, nr, b, d, c);
                }
            }
            var m = new Mesh();
            m.SetVertices(v);
            m.SetNormals(nr);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static void AddTri(List<int> t, List<Vector3> v, List<Vector3> n, int a, int b, int c)
        {
            Vector3 f = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
            if (f.sqrMagnitude < 1e-14f) return;   // degenerate (crown pole)
            if (Vector3.Dot(f, n[a] + n[b] + n[c]) < 0f) { t.Add(a); t.Add(c); t.Add(b); }
            else { t.Add(a); t.Add(b); t.Add(c); }
        }

        static float Hairline(float az, float front, float side, float back)
        {
            float c = Mathf.Cos(az * Mathf.Deg2Rad);
            return c >= 0f ? Mathf.Lerp(side, front, Mathf.Pow(c, 1.6f)) : Mathf.Lerp(side, back, -c);
        }

        // ------------------------------------------------------------------ ears, face, nose
        static void BuildEars(DiverPuppet p, Material skinM)
        {
            Mesh ear = DiverMeshes.Sphere(4, 6);
            for (int s = -1; s <= 1; s += 2)
            {
                float y = 1.685f;
                float r = HeadRadius(y);
                var e = DiverMeshes.Part(p.head, s < 0 ? "EarL" : "EarR", ear, skinM, new Vector3(s * (r - 0.004f), y - HeadY, -0.008f),
                                         new Vector3(0.024f, 0.05f, 0.036f), new Vector3(0f, s * 12f, 0f), false);
                if (s < 0) p.earL = e.transform; else p.earR = e.transform;
            }
        }

        static void BuildFace(DiverPuppet p, DiverLook look, Color skin, Color hairC, Material white, Material ink)
        {
            float rs = HeadRadius(EyeY);
            float zs = Mathf.Sqrt(Mathf.Max(0.0001f, rs * rs - EyeX * EyeX));
            float ez = zs + 0.018f - EyeR;
            Mesh eyeMesh = DiverMeshes.Sphere(6, 10);
            Mesh pupilMesh = DiverMeshes.Sphere(4, 8);
            Mesh lidMesh = DiverMeshes.Cached("eyelid_cap_r3c", () => DiverMeshes.LatheY(HemisphereProfile(4), 8));
            Material lidM = SkinMat(Color.Lerp(skin, Color.black, 0.06f));
            Material shine = MaterialLibrary.Unlit(new Color(1f, 1f, 1f, 1f));
            bool bald = look.hair == CosmeticsDatabase.HairBald;
            Color browC = bald ? Color.Lerp(skin, DiverStyle.Pupil, 0.7f) : Color.Lerp(hairC, DiverStyle.Pupil, 0.25f);
            Material browM = M(browC);
            for (int s = -1; s <= 1; s += 2)
            {
                bool leftEye = s < 0;
                var eye = DiverMeshes.Node(p.head, leftEye ? "EyeL" : "EyeR", new Vector3(s * EyeX, EyeY - HeadY, ez));
                DiverMeshes.Part(eye, "White", eyeMesh, white, Vector3.zero, Vector3.one * (EyeR * 2f), default, false);
                // pupils: googly (slightly outward), cross-eyed (toward the nose)
                Vector3 pupilEuler = leftEye ? new Vector3(-6f, -8f, 0f) : new Vector3(4f, 11f, 0f);
                if (p.eyeStyle == 2) pupilEuler = new Vector3(4f, -s * 22f, 0f);
                if (p.eyeStyle == 5) pupilEuler = new Vector3(-2f, -s * 3f, 0f);
                var pivot = DiverMeshes.Node(eye, "PupilPivot", Vector3.zero, pupilEuler);
                switch (p.eyeStyle)
                {
                    case 3:   // stars
                        DiverMeshes.Part(pivot, "Star", DiverMeshes.Plate("eye_star_r3", StarOutline(0.021f, 0.009f), 0.004f),
                            M(LowPoly.Hex("#F2C94C")), new Vector3(0f, 0f, EyeR - 0.001f), Vector3.one, new Vector3(0f, 180f, 0f), false);
                        break;
                    case 4:   // hearts
                        DiverMeshes.Part(pivot, "Heart", DiverMeshes.Plate("eye_heart_r3", HeartOutline(0.018f), 0.004f),
                            M(LowPoly.Hex("#E0607E")), new Vector3(0f, 0f, EyeR - 0.001f), Vector3.one, new Vector3(0f, 180f, 0f), false);
                        break;
                    default:
                        DiverMeshes.Part(pivot, "Pupil", pupilMesh, ink, new Vector3(0f, 0f, EyeR - PupilR * 0.35f), new Vector3(PupilR * 2f, PupilR * 2f, PupilR * 0.8f), default, false);
                        // white highlight dot (upper outer side of the pupil)
                        DiverMeshes.Part(pivot, "Shine", DiverMeshes.Sphere(3, 4), shine, new Vector3(s * -0.0055f, 0.0065f, EyeR + 0.0004f), Vector3.one * 0.0145f, default, false);
                        break;
                }
                if (leftEye) { p.pupilL = pivot; p.eyeL = eye; } else { p.pupilR = pivot; p.eyeR = eye; }

                // upper eyelid: a skin-coloured half sphere just outside the eye white, rotated over the eye to blink
                var lid = DiverMeshes.Node(eye, "Lid", Vector3.zero, new Vector3(-55f, 0f, 0f));
                DiverMeshes.Part(lid, "LidCap", lidMesh, lidM, Vector3.zero, Vector3.one * (EyeR * 2.12f), default, false);
                if (p.eyeStyle == 5)
                {
                    // lashes ride on the lid rim (they blink with it)
                    for (int k = 0; k < 3; k++)
                    {
                        float a = (-35f + k * 35f) * Mathf.Deg2Rad;
                        var lash = DiverMeshes.Node(lid, "Lash", new Vector3(Mathf.Sin(a) * EyeR * 1.04f, 0f, Mathf.Cos(a) * EyeR * 1.04f), new Vector3(-60f, a * Mathf.Rad2Deg + s * 8f * (k - 1), 0f));
                        DiverMeshes.Part(lash, "M", DiverMeshes.SmoothCapsule(0.0022f, 0.014f, 4, 1, 1), ink, Vector3.zero, Vector3.one, default, false);
                    }
                }
                if (leftEye) p.lidL = lid; else p.lidR = lid;

                // brows: natural, bushy, grumpy (inner end down), surprised (high arch), thin, none
                if (p.browStyle != 5)
                {
                    float by = EyeY + 0.05f + (p.browStyle == 3 ? 0.012f : 0f);
                    float br = HeadRadius(by);
                    float bx = EyeX + 0.002f;
                    float bz = Mathf.Sqrt(Mathf.Max(0.0001f, br * br - bx * bx)) + 0.003f;
                    float tilt = p.browStyle == 2 ? s * 18f : p.browStyle == 3 ? -s * 10f : -s * 4f;
                    // tangent to the head: yaw toward the side, pitched up with the narrowing forehead
                    var brow = DiverMeshes.Node(p.head, leftEye ? "BrowL" : "BrowR", new Vector3(s * bx, by - HeadY, bz),
                        new Vector3(-18f, s * Mathf.Asin(Mathf.Clamp01(bx / Mathf.Max(0.05f, br))) * Mathf.Rad2Deg, tilt));
                    float rad = p.browStyle == 1 ? 0.0105f : p.browStyle == 4 ? 0.0045f : 0.0072f;
                    float len = p.browStyle == 1 ? 0.052f : 0.046f;
                    DiverMeshes.Part(brow, "Brow", DiverMeshes.SmoothCapsule(rad, len, 6, 2, 1), browM, new Vector3(-len * 0.5f, 0f, 0f), Vector3.one, new Vector3(0f, 0f, -90f), false);
                    if (leftEye) p.browL = brow; else p.browR = brow;
                }

                // cheek blush (shown while happy)
                float cy = 1.632f;
                float cr = HeadRadius(cy);
                float cx = 0.07f;
                float cz = Mathf.Sqrt(Mathf.Max(0.0001f, cr * cr - cx * cx)) + 0.0015f;
                var blush = DiverMeshes.Node(p.head, leftEye ? "BlushL" : "BlushR", new Vector3(s * cx, cy - HeadY, cz), new Vector3(0f, 180f + s * Mathf.Asin(Mathf.Clamp01(cx / cr)) * Mathf.Rad2Deg, 0f));
                var br2 = DiverMeshes.Part(blush, "Disc", DiverMeshes.Plate("blush_r3", Ellipse(0.02f, 0.013f, 14), 0.0015f),
                    MaterialLibrary.Transparent(DiverStyle.Blush, 0.1f, false), Vector3.zero, Vector3.one, default, false);
                br2.receiveShadows = false;
                blush.gameObject.SetActive(false);
                if (leftEye) p.blushL = blush; else p.blushR = blush;
            }

            // mouth (faces +Z after the 180° turn of the pivot; the meshes face -Z): 8 shapes, one shown at a time; a
            // full / captain's beard / stubble covers the chin, so the mouth sits on top of it
            bool beardCovers = look.beard == 3 || look.beard == 4 || look.beard == 5;
            float mz = HeadRadius(MouthY) - 0.001f + (beardCovers ? (look.beard == 5 ? 0.004f : 0.019f) : 0f);
            p.mouth = DiverMeshes.Node(p.head, "Mouth", new Vector3(0f, MouthY - HeadY, mz), new Vector3(-8f, 180f, 0f));
            p.mouth.localScale = Vector3.one * 0.6f;
            p.mouthBaseScale = 0.6f;
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

        /// Noses: 0 button, 1 round (How to Fish), 2 long, 3 pointy, 4 snub. Skin a touch warmer.
        static void BuildNose(DiverPuppet p, int nose, Color skin)
        {
            Material m = SkinMat(Color.Lerp(skin, LowPoly.Hex("#E0607E"), 0.1f));
            float y = NoseY;
            float z = HeadRadius(y);
            var n = DiverMeshes.Node(p.head, "Nose", new Vector3(0f, y - HeadY, z - 0.004f));
            Mesh sph = DiverMeshes.Sphere(5, 8);
            switch (nose)
            {
                case 1:   // big round bulb
                    DiverMeshes.Part(n, "Bulb", sph, m, new Vector3(0f, -0.006f, 0.016f), new Vector3(0.05f, 0.044f, 0.046f), default, false);
                    DiverMeshes.Part(n, "Bridge", sph, m, new Vector3(0f, 0.018f, 0.006f), new Vector3(0.026f, 0.04f, 0.026f), default, false);
                    break;
                case 2:   // long, pointing a little down
                    DiverMeshes.Part(n, "Bridge", DiverMeshes.SmoothCapsule(0.0135f, 0.062f, 8, 2, 1), m, new Vector3(0f, 0.022f, 0f), Vector3.one, new Vector3(108f, 0f, 0f), false);
                    DiverMeshes.Part(n, "Tip", sph, m, new Vector3(0f, -0.006f, 0.05f), new Vector3(0.03f, 0.026f, 0.028f), default, false);
                    break;
                case 3:   // pointy
                    DiverMeshes.Part(n, "Cone", DiverMeshes.Cached("nose_pointy", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.019f, 0.004f), new Vector2(0.017f, 0.02f), new Vector2(0.009f, 0.038f), new Vector2(0f, 0.05f) }, 8)),
                        m, new Vector3(0f, 0.002f, 0f), Vector3.one, new Vector3(80f, 0f, 0f), false);
                    break;
                case 4:   // snub (tipped up)
                    DiverMeshes.Part(n, "Tip", sph, m, new Vector3(0f, -0.002f, 0.011f), new Vector3(0.036f, 0.028f, 0.03f), new Vector3(-18f, 0f, 0f), false);
                    DiverMeshes.Part(n, "Bridge", sph, m, new Vector3(0f, 0.014f, 0.004f), new Vector3(0.02f, 0.03f, 0.02f), default, false);
                    break;
                default:  // button
                    DiverMeshes.Part(n, "Tip", sph, m, new Vector3(0f, -0.002f, 0.01f), new Vector3(0.034f, 0.03f, 0.032f), default, false);
                    DiverMeshes.Part(n, "Bridge", sph, m, new Vector3(0f, 0.016f, 0.003f), new Vector3(0.02f, 0.032f, 0.02f), default, false);
                    break;
            }
            p.noseT = n;
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

        // ------------------------------------------------------------------ hair (shows under every hat)
        /// 0 short, 1 spiky, 2 curls, 3 ponytail, 4 bun, 5 long, 6 side part, 7 shaggy, 8 bald. A shell over the head
        /// from the hairline (front high, sides above the ears, back at the nape) to the crown plus style parts. Parts on
        /// the top of the head hide under hats that cover it (the cap itself stays inside the hat).
        static void BuildHair(DiverPuppet p, int hair, int hat, Material m)
        {
            if (hair == CosmeticsDatabase.HairBald) return;
            bool covered = HatCoversTop(hat);
            float front, side, back;
            switch (hair)
            {
                case 2: front = 1.79f; side = 1.70f; back = 1.63f; break;
                case 5: front = 1.80f; side = 1.685f; back = 1.60f; break;
                case 6: front = 1.785f; side = 1.712f; back = 1.645f; break;
                case 7: front = 1.775f; side = 1.69f; back = 1.62f; break;
                case 3: case 4: front = 1.80f; side = 1.712f; back = 1.66f; break;
                default: front = 1.80f; side = 1.715f; back = 1.645f; break;
            }
            float th = HairThickness(hair);
            var h = DiverMeshes.Node(p.head, "Hair", Vector3.zero);
            string key = "hair_cap_" + hair;
            float f = front, sd = side, bk = back;
            DiverMeshes.Part(h, "Cap", DiverMeshes.Cached(key, () => Shell(22, -180f, 180f, true, az => Hairline(az, f, sd, bk), az => CrownY + 1f, th, 0.45f, 7)), m, Vector3.zero, Vector3.one);
            Mesh sph = DiverMeshes.Sphere(4, 6);
            switch (hair)
            {
                case 0:   // short: a fringe + sideburns
                    Tuft(h, sph, m, 0f, front + 0.004f, th + 0.004f, new Vector3(0.11f, 0.028f, 0.03f), -28f);
                    for (int s = -1; s <= 1; s += 2) Tuft(h, sph, m, s * 78f, 1.69f, 0.003f, new Vector3(0.012f, 0.042f, 0.02f), 0f);
                    break;
                case 1:   // spiky: cones on the top and front
                    for (int s = -1; s <= 1; s += 2) Tuft(h, sph, m, s * 78f, 1.695f, 0.003f, new Vector3(0.012f, 0.04f, 0.02f), 0f);
                    if (!covered)
                    {
                        float[] az = { 0f, -32f, 32f, 0f, -70f, 70f, 180f, -125f, 125f };
                        float[] yy = { 1.815f, 1.83f, 1.83f, 1.86f, 1.8f, 1.8f, 1.82f, 1.81f, 1.81f };
                        Mesh cone = DiverMeshes.Cached("hair_spike", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.024f, 0.002f), new Vector2(0.015f, 0.03f), new Vector2(0f, 0.058f) }, 6));
                        for (int i = 0; i < az.Length; i++)
                        {
                            Vector3 pos = HeadPoint(az[i], yy[i], th * 0.5f);
                            Vector3 n = (HeadNormal(az[i], yy[i]) + Vector3.up * 0.4f - Mathf.Cos(az[i] * Mathf.Deg2Rad) * Vector3.forward * 0.3f).normalized;
                            var r = DiverMeshes.Part(h, "Spike", cone, m, pos, Vector3.one, default, false);
                            r.transform.localRotation = Quaternion.FromToRotation(Vector3.up, n);
                        }
                    }
                    break;
                case 2:   // curls: little balls along the hairline (+ on top without a hat)
                {
                    Mesh curl = DiverMeshes.Sphere(3, 6);
                    for (int i = 0; i < 12; i++)
                    {
                        float az = -150f + i * 27.3f;
                        if (Mathf.Abs(Mathf.DeltaAngle(az, 0f)) < 35f) continue;
                        float y = Hairline(az, front, side, back) + 0.012f;
                        var r = DiverMeshes.Part(h, "Curl", curl, m, HeadPoint(az, y, th), Vector3.one * 0.042f, default, false);
                        _ = r;
                    }
                    for (int i = -1; i <= 1; i++) DiverMeshes.Part(h, "Curl", curl, m, HeadPoint(i * 22f, front + 0.006f, th), Vector3.one * 0.04f, default, false);
                    if (!covered)
                        for (int i = 0; i < 6; i++) DiverMeshes.Part(h, "Curl", curl, m, HeadPoint(i * 60f + 15f, 1.835f, th * 0.8f), Vector3.one * 0.044f, default, false);
                    break;
                }
                case 3:   // ponytail with a band
                {
                    Tuft(h, sph, m, 0f, front + 0.004f, th + 0.003f, new Vector3(0.1f, 0.022f, 0.026f), -28f);
                    Vector3 at = HeadPoint(180f, 1.70f, th + 0.006f);
                    var tail = DiverMeshes.Node(h, "Tail", at, new Vector3(-155f, 0f, 0f));
                    DiverMeshes.Part(tail, "Band", DiverMeshes.Torus(0.017f, 0.006f, 10, 3), M(LowPoly.Hex("#E0607E")), new Vector3(0f, 0.012f, 0f), Vector3.one, default, false);
                    DiverMeshes.Part(tail, "Lock", DiverMeshes.TaperCapsule(0.012f, 0.028f, 0.2f, 8, 2), m, new Vector3(0f, 0.015f, 0f), Vector3.one, default, false);
                    break;
                }
                case 4:   // bun: high on the back of the head, low at the nape under a hat
                {
                    Tuft(h, sph, m, 0f, front + 0.004f, th + 0.003f, new Vector3(0.1f, 0.022f, 0.026f), -28f);
                    Vector3 at = covered ? HeadPoint(180f, 1.675f, th + 0.026f) : HeadPoint(180f, 1.81f, th + 0.03f);
                    DiverMeshes.Part(h, "Bun", DiverMeshes.Sphere(6, 10), m, at, new Vector3(0.068f, 0.064f, 0.062f), default, false);
                    break;
                }
                case 5:   // long: a back curtain + side locks over the ears
                    DiverMeshes.Part(h, "Back", DiverMeshes.Sphere(6, 12), m, new Vector3(0f, 1.6f - HeadY, -0.072f), new Vector3(0.235f, 0.27f, 0.12f), default, false);
                    for (int s = -1; s <= 1; s += 2)
                        DiverMeshes.Part(h, "Lock", DiverMeshes.Sphere(5, 10), m, new Vector3(s * 0.108f, 1.64f - HeadY, -0.006f), new Vector3(0.042f, 0.17f, 0.075f), new Vector3(0f, 0f, s * 4f), false);
                    Tuft(h, sph, m, -12f, front + 0.003f, th + 0.003f, new Vector3(0.12f, 0.024f, 0.028f), -30f);
                    break;
                case 6:   // side part: a swoop over the forehead
                    Tuft(h, DiverMeshes.Sphere(6, 10), m, -18f, front + 0.008f, th + 0.008f, new Vector3(0.115f, 0.03f, 0.045f), -22f, 14f);
                    for (int s = -1; s <= 1; s += 2) Tuft(h, sph, m, s * 78f, 1.695f, 0.003f, new Vector3(0.012f, 0.04f, 0.02f), 0f);
                    break;
                case 7:   // shaggy: tufts all around the hairline
                    for (int i = 0; i < 11; i++)
                    {
                        float az = -160f + i * 32f;
                        float y = Hairline(az, front, side, back) + 0.006f;
                        Tuft(h, sph, m, az, y, th, new Vector3(0.05f, 0.03f, 0.03f), -10f, (i % 3 - 1) * 14f);
                    }
                    if (!covered)
                        for (int i = 0; i < 4; i++) Tuft(h, sph, m, i * 90f + 30f, 1.84f, th, new Vector3(0.06f, 0.03f, 0.05f), 0f, i * 20f);
                    break;
            }
        }

        /// A hair tuft (ellipsoid) on the head surface at an azimuth / height, rotated with the surface.
        static void Tuft(Transform parent, Mesh m, Material mat, float azDeg, float y, float outward, Vector3 size, float pitch, float roll = 0f)
        {
            Vector3 pos = HeadPoint(azDeg, y, outward);
            var r = DiverMeshes.Part(parent, "Tuft", m, mat, pos, size, default, false);
            r.transform.localRotation = Quaternion.Euler(0f, azDeg, 0f) * Quaternion.Euler(pitch, 0f, roll);
        }

        // ------------------------------------------------------------------ beards
        /// 1 moustache, 2 goatee, 3 full beard, 4 captain's beard (chin curtain), 5 stubble.
        static void BuildBeard(DiverPuppet p, int beard, Color skin, Color hairC)
        {
            if (beard <= 0) return;
            var b = DiverMeshes.Node(p.head, "Beard", Vector3.zero);
            Material m = HairMat(beard == 5 ? Color.Lerp(skin, hairC, 0.42f) : hairC);
            Mesh sph = DiverMeshes.Sphere(5, 8);
            bool moustache = beard == 1 || beard == 2 || beard == 3;
            if (beard == 3 || beard == 4 || beard == 5)
            {
                float th = beard == 5 ? 0.0035f : beard == 4 ? 0.016f : 0.02f;
                float topFront = beard == 4 ? 1.565f : beard == 5 ? 1.615f : 1.618f;
                float topSide = beard == 5 ? 1.655f : 1.665f;
                float tf = topFront, ts = topSide;
                DiverMeshes.Part(b, "Jaw", DiverMeshes.Cached("beard_jaw_" + beard, () => Shell(14, -105f, 105f, false,
                    az => 1.531f, az => Mathf.Lerp(ts, tf, Mathf.Pow(Mathf.Max(0f, Mathf.Cos(az * Mathf.Deg2Rad)), 2f)), th, 0.5f, 5)), m, Vector3.zero, Vector3.one);
                if (beard != 5)
                    DiverMeshes.Part(b, "Chin", sph, m, new Vector3(0f, 1.546f - HeadY, 0.058f), beard == 4 ? new Vector3(0.12f, 0.085f, 0.1f) : new Vector3(0.13f, 0.09f, 0.1f), new Vector3(-12f, 0f, 0f), false);
            }
            if (beard == 2)
                DiverMeshes.Part(b, "Goatee", sph, m, new Vector3(0f, 1.562f - HeadY, HeadRadius(1.562f) - 0.004f), new Vector3(0.045f, 0.05f, 0.032f), new Vector3(-10f, 0f, 0f), false);
            if (moustache)
            {
                float y = 1.622f;
                float z = HeadRadius(y) + (beard == 3 ? 0.012f : 0.003f);
                for (int s = -1; s <= 1; s += 2)
                    DiverMeshes.Part(b, "Stache", sph, m, new Vector3(s * 0.021f, y - HeadY, z), new Vector3(0.042f, 0.017f, 0.022f), new Vector3(0f, s * 12f, s * -14f), false);
            }
        }

        // ------------------------------------------------------------------ mask + snorkel
        static void BuildMask(DiverPuppet p, DiverLook look)
        {
            p.maskPivot = DiverMeshes.Node(p.head, "MaskPivot", new Vector3(0f, EyeY - HeadY, 0f));
            Color fc = look.ResolvedMask;
            bool gold = (fc - CosmeticsDatabase.MaskColors[6]).maxColorComponent < 0.02f && (CosmeticsDatabase.MaskColors[6] - fc).maxColorComponent < 0.02f;
            Material frame = MaterialLibrary.Opaque(fc, gold ? 0.6f : 0.3f, gold ? 0.4f : 0f);
            float th = HairThickness(look.hair);
            // the strap: an ellipse whose front runs inside the face (behind the eyes), sides and back over the hair
            var strap = DiverMeshes.Part(p.maskPivot, "Strap", DiverMeshes.Torus(0.128f + th, 0.008f, 16, 3), frame, new Vector3(0f, 0.012f, -0.022f), new Vector3(1f, 1f, 0.9f), default, false);
            p.maskStrap = strap.transform;
            // frame + skirt + glass in front of the eyes, a nose pocket below
            var front = DiverMeshes.Node(p.maskPivot, "Front", new Vector3(0f, 0f, 0.152f));
            DiverMeshes.Part(front, "Frame", DiverMeshes.Torus(0.088f, 0.011f, 16, 3), frame, Vector3.zero, new Vector3(1f, 1f, 0.58f), new Vector3(90f, 0f, 0f), false);
            DiverMeshes.Part(front, "Skirt", DiverMeshes.Taper(0.5f, 0.45f, 16, false), frame, Vector3.zero, new Vector3(0.176f, 0.042f, 0.102f), new Vector3(-90f, 0f, 0f), false);
            var glass = DiverMeshes.Part(front, "Glass", DiverMeshes.Plate("mask_glass_r3", Ellipse(0.085f, 0.048f, 20), 0.002f),
                                         MaterialLibrary.Transparent(DiverStyle.MaskGlass, 0.6f, true), new Vector3(0f, 0f, 0.002f), Vector3.one, default, false);
            glass.receiveShadows = false;
            DiverMeshes.Part(front, "Pocket", DiverMeshes.Sphere(5, 8), frame, new Vector3(0f, -0.05f, -0.012f), new Vector3(0.046f, 0.04f, 0.04f), default, false);
            // snorkel on the left side (1c: on / off; legacy looks always had it)
            if (look.snorkel || look.lookVersion < 1)
            {
                Material snork = Neo(look.ResolvedCuff);
                float sx = -(0.128f + th + 0.012f);
                DiverMeshes.Part(p.maskPivot, "Snorkel", DiverMeshes.SmoothCapsule(0.012f, 0.27f, 8, 2, 1), snork, new Vector3(sx, -0.085f, 0.045f), Vector3.one, new Vector3(0f, 0f, -6f), false);
                DiverMeshes.Part(p.maskPivot, "SnorkelTip", DiverMeshes.SmoothCapsule(0.015f, 0.045f, 8, 2, 1), frame,
                                 new Vector3(sx + Mathf.Sin(6f * Mathf.Deg2Rad) * 0.245f, -0.085f + Mathf.Cos(6f * Mathf.Deg2Rad) * 0.245f, 0.045f), Vector3.one, new Vector3(0f, 0f, -6f), false);
            }
        }

        // ------------------------------------------------------------------ hats (CosmeticsDatabase order 0 … 19)
        /// The hats are modelled for a head of radius ~0.205 (round-2 units) and scaled onto the round-3 head (+ hair).
        static void BuildHat(DiverPuppet p, int hat, float hairThick)
        {
            if (hat == 0) return;
            float hs = (HeadRadius(HatY) + hairThick + 0.004f) / 0.205f;
            var h = DiverMeshes.Node(p.head, "Hat", new Vector3(0f, HatY - HeadY, 0f));
            h.localScale = Vector3.one * hs;
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
                    DiverMeshes.Part(h, "Bucket", DiverMeshes.Cached("hat_bucket_r3b", () => DiverMeshes.LatheY(DiverMeshes.Resample(prof, 11), 20)), m, Vector3.zero, Vector3.one);
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
                    DiverMeshes.Part(h, "Nest", DiverMeshes.Torus(0.18f, 0.05f, 16, 5), straw, new Vector3(0f, 0.04f, 0f), new Vector3(1f, 0.8f, 1f));
                    for (int i = 0; i < 6; i++)
                        DiverMeshes.Part(h, "Twig", DiverMeshes.SmoothCapsule(0.006f, 0.12f, 4, 2, 1), straw, new Vector3(Mathf.Cos(i * 1.1f) * 0.2f, 0.05f, Mathf.Sin(i * 1.1f) * 0.2f), Vector3.one, new Vector3(70f, i * 60f, 0f), false);
                    var gull = DiverMeshes.Node(h, "Gull", new Vector3(0f, 0.11f, 0f));
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
                case 14:   // paper crown
                {
                    var paper = MaterialLibrary.Opaque(LowPoly.Hex("#F4EFE6"), 0.05f, 0f);
                    var pr = DiverMeshes.Part(h, "Paper", DiverMeshes.Cached("hat_paper_crown", PaperCrown), paper, Vector3.zero, Vector3.one);
                    pr.sharedMaterial = paper;
                    DiverMeshes.Part(h, "Line", DiverMeshes.Torus(0.214f, 0.004f, 16, 3), M(LowPoly.Hex("#C9B48A")), new Vector3(0f, 0.03f, 0f), Vector3.one, default, false);
                    break;
                }
                case 15:   // beanie with a folded cuff and a bobble
                {
                    Material knit = MaterialLibrary.Opaque(LowPoly.Hex("#D9603B"), 0.08f, 0f), cream = MaterialLibrary.Opaque(LowPoly.Hex("#EDE6DA"), 0.08f, 0f);
                    DiverMeshes.Part(h, "Dome", DiverMeshes.Cached("hat_beanie", () => DiverMeshes.LatheY(DiverMeshes.Resample(new[] { new Vector2(0.208f, -0.03f), new Vector2(0.21f, 0.06f), new Vector2(0.19f, 0.14f), new Vector2(0.13f, 0.2f), new Vector2(0.06f, 0.225f), new Vector2(0f, 0.23f) }, 10), 20)), knit, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Cuff", DiverMeshes.Cached("hat_beanie_cuff", () => DiverMeshes.LatheY(new[] { new Vector2(0.2f, -0.045f), new Vector2(0.222f, -0.04f), new Vector2(0.226f, 0.02f), new Vector2(0.214f, 0.035f) }, 20)), cream, Vector3.zero, Vector3.one, default, false);
                    DiverMeshes.Part(h, "Bobble", DiverMeshes.Sphere(6, 10), cream, new Vector3(0f, 0.25f, 0f), Vector3.one * 0.09f, default, false);
                    break;
                }
                case 16:   // baseball cap
                {
                    Material crown = MaterialLibrary.Opaque(LowPoly.Hex("#E8A23A"), 0.15f, 0f), dark = M(LowPoly.Hex("#2E3140"));
                    DiverMeshes.Part(h, "Crown", DiverMeshes.Cached("hat_basecap", () => DiverMeshes.LatheY(DiverMeshes.Resample(new[] { new Vector2(0.212f, -0.02f), new Vector2(0.212f, 0.05f), new Vector2(0.19f, 0.13f), new Vector2(0.12f, 0.18f), new Vector2(0f, 0.195f) }, 9), 20)), crown, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Visor", DiverMeshes.Plate("basecap_visor", HalfDisc(0.17f, 0.17f, 14), 0.014f), dark, new Vector3(0f, -0.01f, 0.19f), Vector3.one, new Vector3(-82f, 0f, 0f), false);
                    DiverMeshes.Part(h, "Button", DiverMeshes.Sphere(3, 6), dark, new Vector3(0f, 0.195f, 0f), Vector3.one * 0.03f, default, false);
                    DiverMeshes.Part(h, "Logo", DiverMeshes.Sphere(4, 6), M(LowPoly.Hex("#EDE6DA")), new Vector3(0f, 0.08f, 0.2f), new Vector3(0.07f, 0.05f, 0.012f), new Vector3(-14f, 0f, 0f), false);
                    break;
                }
                case 17:   // headband (terry cloth, the hair top stays free)
                {
                    DiverMeshes.Part(h, "Band", DiverMeshes.Cached("hat_headband", () => DiverMeshes.LatheY(new[] { new Vector2(0.203f, -0.005f), new Vector2(0.215f, 0.0f), new Vector2(0.218f, 0.035f), new Vector2(0.21f, 0.055f), new Vector2(0.196f, 0.058f) }, 20)),
                        MaterialLibrary.Opaque(LowPoly.Hex("#F4EFE6"), 0.05f, 0f), new Vector3(0f, 0.02f, 0f), Vector3.one, default, false);
                    DiverMeshes.Part(h, "Stripe", DiverMeshes.Torus(0.218f, 0.008f, 20, 3), M(LowPoly.Hex("#E0607E")), new Vector3(0f, 0.045f, 0f), Vector3.one, default, false);
                    break;
                }
                case 18:   // head scarf with the knot at the back
                {
                    Material cloth = MaterialLibrary.Opaque(LowPoly.Hex("#E0607E"), 0.1f, 0f);
                    DiverMeshes.Part(h, "Scarf", DiverMeshes.Cached("hat_scarf", () => DiverMeshes.LatheY(DiverMeshes.Resample(new[] { new Vector2(0.209f, -0.035f), new Vector2(0.212f, 0.05f), new Vector2(0.18f, 0.13f), new Vector2(0.1f, 0.18f), new Vector2(0f, 0.19f) }, 9), 20)), cloth, Vector3.zero, Vector3.one);
                    DiverMeshes.Part(h, "Knot", DiverMeshes.Sphere(5, 8), cloth, new Vector3(0f, 0.0f, -0.215f), new Vector3(0.07f, 0.06f, 0.06f), default, false);
                    for (int s = -1; s <= 1; s += 2)
                        DiverMeshes.Part(h, "Tail", DiverMeshes.Plate("scarf_tail", new[] { new Vector2(-0.03f, 0f), new Vector2(0.03f, 0f), new Vector2(0.02f, -0.15f), new Vector2(-0.025f, -0.13f) }, 0.01f),
                            cloth, new Vector3(s * 0.025f, -0.01f, -0.23f), Vector3.one, new Vector3(-20f, 180f, s * 14f), false);
                    break;
                }
                default:   // 19 sun visor (no crown – the hair shows on top)
                {
                    Material band = MaterialLibrary.Opaque(LowPoly.Hex("#F2C94C"), 0.2f, 0f);
                    DiverMeshes.Part(h, "Band", DiverMeshes.Cached("hat_visor_band", () => DiverMeshes.LatheY(new[] { new Vector2(0.206f, -0.005f), new Vector2(0.216f, 0.0f), new Vector2(0.216f, 0.045f), new Vector2(0.206f, 0.05f) }, 20)), band, Vector3.zero, Vector3.one, default, false);
                    DiverMeshes.Part(h, "Visor", DiverMeshes.Plate("visor_shield", HalfDisc(0.2f, 0.19f, 16), 0.012f), MaterialLibrary.Transparent(new Color(0.24f, 0.72f, 0.77f, 0.75f), 0.6f, true),
                        new Vector3(0f, 0.01f, 0.19f), Vector3.one, new Vector3(-80f, 0f, 0f), false);
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

        // ------------------------------------------------------------------ accessories
        /// 1 shell necklace, 2 neckerchief, 3 earrings, 4 flower behind the ear, 5 dive watch (BuildWatch, on the left
        /// forearm), 6 freckles, 7 a plaster on the cheek.
        static void BuildAccessory(DiverPuppet p, DiverLook look, Color skin)
        {
            switch (look.accessory)
            {
                case 1:
                {
                    // a cord ring around the neck that drapes down onto the chest (an ellipse tilted forward)
                    float depth = (TorsoRZ(1.41f, look.build) + 0.009f - 0.02f) / 0.906f;   // ÷ cos 25°
                    var n = DiverMeshes.Node(p.chest, "Necklace", new Vector3(0f, 1.47f - ChestY, 0.02f), new Vector3(-25f, 0f, 0f));
                    float zs = depth / 0.088f;
                    DiverMeshes.Part(n, "Cord", DiverMeshes.Torus(0.088f, 0.0035f, 18, 3), M(LowPoly.Hex("#8E6C50")), Vector3.zero, new Vector3(1f, 1f, zs), default, false);
                    string[] cols = { "#F4EFE6", "#F29BBE", "#F2C94C" };
                    for (int i = 0; i < 3; i++)
                    {
                        float a = (-20f + i * 20f) * Mathf.Deg2Rad;
                        DiverMeshes.Part(n, "Shell", DiverMeshes.Cached("acc_shell", () => DiverMeshes.LatheY(new[] { new Vector2(0f, 0f), new Vector2(0.016f, 0.004f), new Vector2(0.012f, 0.014f), new Vector2(0f, 0.024f) }, 6)),
                            M(LowPoly.Hex(cols[i])), new Vector3(Mathf.Sin(a) * 0.088f, -0.006f, Mathf.Cos(a) * depth + 0.006f), Vector3.one, new Vector3(100f, a * Mathf.Rad2Deg, 0f), false);
                    }
                    break;
                }
                case 2:
                {
                    // a fat knotted neckerchief roll + the triangle lying on the upper chest
                    Material cloth = MaterialLibrary.Opaque(LowPoly.Hex("#C94F3D"), 0.1f, 0f);
                    float fz = TorsoRZ(1.43f, look.build);
                    DiverMeshes.Part(p.chest, "Scarf", DiverMeshes.Torus(0.078f, 0.022f, 16, 4), cloth, new Vector3(0f, 1.48f - ChestY, 0.012f), new Vector3(1f, 1f, 1.5f), new Vector3(-12f, 0f, 0f), false);
                    DiverMeshes.Part(p.chest, "ScarfTip", DiverMeshes.Plate("acc_scarf_tip", new[] { new Vector2(-0.07f, 0f), new Vector2(0.07f, 0f), new Vector2(0f, -0.1f) }, 0.012f),
                        cloth, new Vector3(0f, 1.455f - ChestY, fz + 0.01f), Vector3.one, new Vector3(-16f, 0f, 0f), false);
                    break;
                }
                case 3:
                    if (p.earL != null && p.earR != null)
                    {
                        Material gold = MaterialLibrary.Opaque(LowPoly.Hex("#E8C65A"), 0.6f, 0.45f);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            float r = HeadRadius(1.66f);
                            DiverMeshes.Part(p.head, "Earring", DiverMeshes.Torus(0.011f, 0.0022f, 10, 3), gold, new Vector3(s * (r + 0.004f), 1.645f - HeadY, -0.006f), Vector3.one, new Vector3(0f, 0f, 90f), false);
                        }
                    }
                    break;
                case 4:
                {
                    var f = DiverMeshes.Node(p.head, "Flower", HeadPoint(-82f, 1.725f, 0.012f), new Vector3(0f, -82f, 0f));
                    Material petal = M(LowPoly.Hex("#E86FA0")), heart = M(LowPoly.Hex("#F2C94C"));
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 72f * Mathf.Deg2Rad;
                        DiverMeshes.Part(f, "Petal", DiverMeshes.Sphere(4, 6), petal, new Vector3(Mathf.Cos(a) * 0.018f, Mathf.Sin(a) * 0.018f, 0f), new Vector3(0.026f, 0.026f, 0.008f), default, false);
                    }
                    DiverMeshes.Part(f, "Heart", DiverMeshes.Sphere(3, 6), heart, new Vector3(0f, 0f, 0.004f), Vector3.one * 0.012f, default, false);
                    break;
                }
                case 6:
                {
                    Material dot = M(Color.Lerp(skin, LowPoly.Hex("#8E4A30"), 0.45f));
                    Mesh sph = DiverMeshes.Sphere(3, 4);
                    for (int s = -1; s <= 1; s += 2)
                        for (int i = 0; i < 4; i++)
                        {
                            float az = s * (22f + (i % 2) * 9f + i * 3f);
                            float y = 1.648f + (i / 2) * 0.012f - (i % 2) * 0.004f;
                            DiverMeshes.Part(p.head, "Freckle", sph, dot, HeadPoint(az, y, 0.0005f), Vector3.one * 0.0065f, default, false);
                        }
                    break;
                }
                case 7:
                {
                    Material tape = MaterialLibrary.Opaque(LowPoly.Hex("#E9C9A0"), 0.1f, 0f);
                    var pl = DiverMeshes.Node(p.head, "Plaster", HeadPoint(28f, 1.64f, 0.002f), new Vector3(0f, 28f, 0f));
                    for (int k = 0; k < 2; k++)
                        DiverMeshes.Part(pl, "Strip", DiverMeshes.RoundedSlab("acc_plaster", 0.045f, 0.013f, 0.003f, 0.005f, 2), tape, Vector3.zero, Vector3.one, new Vector3(0f, 0f, k == 0 ? 35f : -35f), false)
                            .sharedMaterials = new[] { tape, tape };
                    break;
                }
            }
        }

        /// Accessory 5: a chunky dive watch on the left forearm, just above the cuff.
        static void BuildWatch(Transform elbow, float limb)
        {
            var w = DiverMeshes.Node(elbow, "Watch", new Vector3(0f, -ForeArm + 0.06f, 0f));
            float r = 0.04f * limb;
            DiverMeshes.Part(w, "Strap", DiverMeshes.Torus(r + 0.003f, 0.007f, 14, 3), M(LowPoly.Hex("#2E3140")), Vector3.zero, Vector3.one, default, false);
            var face = DiverMeshes.Part(w, "Face", DiverMeshes.RoundedSlab("acc_watch", 0.034f, 0.034f, 0.012f, 0.012f, 3), M(LowPoly.Hex("#F2C94C")),
                                        new Vector3(0f, 0f, r + 0.006f), Vector3.one, new Vector3(0f, 180f, 0f), false);
            face.sharedMaterials = new[] { M(LowPoly.Hex("#1E1B2B")), M(LowPoly.Hex("#F2C94C")) };
        }

        // ------------------------------------------------------------------ limbs
        static void BuildArm(Transform shoulder, out Transform elbow, out Transform hand, Material neo, Material cuff, Material suit, float limb)
        {
            // shoulder cap in the suit colour: defined shoulders that read from afar
            DiverMeshes.Part(shoulder, "ShoulderCap", DiverMeshes.Sphere(4, 8), suit, new Vector3(0f, -0.01f, 0f), Vector3.one * (0.13f * limb), default, false);
            DiverMeshes.TaperLimb(shoulder, "UpperArm", 0.05f * limb, 0.044f * limb, UpperArm, neo);
            elbow = DiverMeshes.Node(shoulder, "Elbow", new Vector3(0f, -UpperArm, 0f));
            // ball joint: bends never show a gap
            DiverMeshes.Part(elbow, "Joint", DiverMeshes.Sphere(3, 6), neo, Vector3.zero, Vector3.one * (0.046f * limb * 2f), default, false);
            DiverMeshes.TaperLimb(elbow, "ForeArm", 0.044f * limb, 0.037f * limb, ForeArm, neo, 8);
            DiverMeshes.Part(elbow, "Cuff", DiverMeshes.Torus(0.039f * limb, 0.01f, 10, 3), cuff, new Vector3(0f, -ForeArm + 0.02f, 0f), Vector3.one, default, false);
            hand = DiverMeshes.Node(elbow, "Hand", new Vector3(0f, -ForeArm - 0.045f, 0f));
            // neoprene gloves everywhere (Q1/Q2): a mitten with a thumb, a bit lighter than the suit, never skin-coloured
            Material glove = Neo(DiverGear.Glove);
            DiverMeshes.Part(hand, "Mitten", DiverMeshes.Sphere(6, 10), glove, new Vector3(0f, -0.012f, 0.004f), new Vector3(0.082f, 0.11f, 0.058f));
            DiverMeshes.Part(hand, "Thumb", DiverMeshes.SmoothCapsule(0.016f, 0.055f, 8, 2, 1), glove, new Vector3(0f, 0.004f, 0.026f), Vector3.one, new Vector3(150f, 0f, 0f), false);
        }

        static void BuildLeg(Transform hip, out Transform knee, out Transform foot, Material neo, Material stripe, Material boots, DiverLook look, bool left, float limb)
        {
            DiverMeshes.TaperLimb(hip, "Thigh", 0.078f * limb, 0.06f * limb, Thigh, neo);
            DiverMeshes.Part(hip, "Stripe", DiverMeshes.Torus(0.071f * limb, 0.012f, 10, 3), stripe, new Vector3(0f, -0.2f, 0f), Vector3.one, default, false);
            knee = DiverMeshes.Node(hip, "Knee", new Vector3(0f, -Thigh, 0f));
            DiverMeshes.Part(knee, "Joint", DiverMeshes.Sphere(3, 6), neo, Vector3.zero, Vector3.one * (0.06f * limb * 2f), default, false);
            DiverMeshes.TaperLimb(knee, "Shin", 0.058f * limb, 0.046f * limb, Shin - 0.03f, neo);
            foot = DiverMeshes.Node(knee, "Foot", new Vector3(0f, -Shin, 0f));
            DiverMeshes.Part(foot, "Pocket", DiverMeshes.SmoothCapsule(0.05f, 0.22f, 8, 2, 1), boots, new Vector3(0f, -0.035f, -0.065f), Vector3.one, new Vector3(90f, 0f, 0f));
            // fins by tier (Q51) or the colour picked on the character screen – DiverGear
            DiverGear.BuildFin(foot, look, boots, left);
        }

        // ------------------------------------------------------------------ suit patterns (12 options)
        static readonly Dictionary<string, Material> patternMats = new Dictionary<string, Material>();

        /// The material of the suit panel: plain suit colour (pattern 0) or a small procedural pattern texture in suit +
        /// accent colour (stripes, dots, shark teeth, herringbone, waves, gold, lighthouse, camo, zigzag, stars,
        /// two-tone). Cached per combination.
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
            if (pattern == 8) return Color.Lerp(baseC, LowPoly.Hex("#2B2533"), 0.45f);
            // the cuff colour when it contrasts enough, else a light / dark version of the suit
            float lb = baseC.r * 0.3f + baseC.g * 0.59f + baseC.b * 0.11f;
            float lc = cuff.r * 0.3f + cuff.g * 0.59f + cuff.b * 0.11f;
            if (Mathf.Abs(lb - lc) > 0.18f) return cuff;
            return lb > 0.55f ? Color.Lerp(baseC, Color.black, 0.38f) : Color.Lerp(baseC, Color.white, 0.6f);
        }

        /// 256 × 160 texture over the chest UVs (u = angle around the torso ≈ 1.0 m, v = profile arc ≈ 0.75 m).
        static Texture2D PatternTexture(int pattern, Color baseC, Color accent)
        {
            const int W = 256, H = 160;
            const float Lu = 1.0f, Lv = 0.75f;
            var px = new Color32[W * H];
            float pix = Lu / W;   // metres per pixel (horizontal)
            for (int y = 0; y < H; y++)
            {
                float Y = (y + 0.5f) / H * Lv;
                for (int x = 0; x < W; x++)
                {
                    float X = (x + 0.5f) / W * Lu;
                    float cover = Coverage(pattern, X, Y, pix, Lv);
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
        static float Coverage(int pattern, float X, float Y, float pix, float Lv)
        {
            switch (pattern)
            {
                case 1:   // stripes: 3.5 cm bands every 10 cm
                {
                    float f = Mathf.Repeat(Y, 0.10f) - 0.05f;
                    return Edge(Mathf.Abs(f) - 0.0175f, pix);
                }
                case 2:   // dots: 2 cm dots on a staggered 8 cm grid
                {
                    float row = Mathf.Floor(Y / 0.08f);
                    float ox = (row % 2f) * 0.04f;
                    float dx = Mathf.Repeat(X + ox, 0.08f) - 0.04f, dy = Mathf.Repeat(Y, 0.08f) - 0.04f;
                    return Edge(Mathf.Sqrt(dx * dx + dy * dy) - 0.02f, pix);
                }
                case 3:   // shark teeth: a band of white teeth across the chest
                {
                    float top = Lv * 0.62f;
                    const float len = 0.07f;
                    if (Y > top + 0.03f || Y < top - len - 0.01f) return 0f;
                    float u = Mathf.Repeat(X, 0.07f) / 0.07f;
                    float toothTop = top, toothTip = top - len * (1f - Mathf.Abs(u * 2f - 1f));
                    float band = Edge(Mathf.Abs(Y - (top + 0.01f)) - 0.01f, pix);
                    float tooth = Y <= toothTop && Y >= toothTip ? 1f : Edge(Mathf.Min(Mathf.Abs(Y - toothTop), Mathf.Abs(Y - toothTip)), pix) * 0.5f;
                    return Mathf.Max(band, tooth);
                }
                case 4:   // herringbone: chevron lines
                {
                    float u = Mathf.Repeat(X, 0.08f) / 0.08f;
                    float f = Mathf.Repeat(Y + Mathf.Abs(u * 2f - 1f) * 0.04f, 0.075f) - 0.0375f;
                    return Edge(Mathf.Abs(f) - 0.009f, pix);
                }
                case 5:   // waves
                {
                    float f = Mathf.Repeat(Y + Mathf.Sin(X / 0.18f * Mathf.PI * 2f) * 0.018f, 0.1f) - 0.05f;
                    return Edge(Mathf.Abs(f) - 0.011f, pix);
                }
                case 6:   // gold: soft diagonal sheen bands
                {
                    float f = Mathf.Repeat(X * 0.7f + Y, 0.26f) - 0.13f;
                    return 0.35f * Edge(Mathf.Abs(f) - 0.025f, pix * 6f);
                }
                case 7:   // lighthouse: broad red bands
                {
                    float f = Mathf.Repeat(Y, 0.2f) - 0.1f;
                    return Edge(Mathf.Abs(f) - 0.05f, pix);
                }
                case 8:   // camo: soft blobs from a few overlapping sines (seamless in X: whole periods over 1 m)
                {
                    float n = Mathf.Sin(X * Mathf.PI * 2f * 3f + Y * 9f) + Mathf.Sin(X * Mathf.PI * 2f * 5f - Y * 13f + 1.3f) * 0.7f + Mathf.Sin(Y * 21f + X * Mathf.PI * 2f * 2f) * 0.5f;
                    return Edge(-n + 0.35f, pix * 4f);
                }
                case 9:   // zigzag band across the chest
                {
                    float u = Mathf.Repeat(X, 0.1f) / 0.1f;
                    float zig = Lv * 0.55f + (Mathf.Abs(u * 2f - 1f) - 0.5f) * 0.06f;
                    return Edge(Mathf.Abs(Y - zig) - 0.018f, pix);
                }
                case 10:  // little stars on a 10 cm grid
                {
                    float row = Mathf.Floor(Y / 0.1f);
                    float dx = Mathf.Repeat(X + (row % 2f) * 0.05f, 0.1f) - 0.05f, dy = Mathf.Repeat(Y, 0.1f) - 0.05f;
                    float ang = Mathf.Atan2(dy, dx);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float starR = 0.012f + 0.009f * Mathf.Cos(ang * 5f + Mathf.PI * 0.5f);
                    return Edge(r - starR, pix);
                }
                case 11:  // two-tone: the upper chest in the accent colour
                    return Edge(Lv * 0.6f - Y, pix);
                default: return 0f;
            }
        }
    }
}
