using System;
using UnityEngine;

namespace Sealebrities
{
    /// Bony fish (BodyPlan.Fish) with variants: default, tall, lionfish, barracuda, grouper, garibaldi.
    /// Low poly: 13 octagonal rings, fins of a few flat triangles, big goggle eyes for the friendly reef fish.
    internal static class CreatureMeshFish
    {
        const float Pi = CreatureMeshMath.Pi;

        enum Tail { Rounded, Emarginate, Forked, Lunate, Truncate }

        static readonly float[] BodyTs = { 0f, 0.03f, 0.075f, 0.13f, 0.2f, 0.29f, 0.39f, 0.5f, 0.61f, 0.72f, 0.82f, 0.91f, 1f };

        sealed class Spec
        {
            public float Lb = 0.8f, Nose = 0.06f;
            public CreatureMeshCurve Top, Bot, Wid;
            public float EyeT = 0.115f, EyeTheta = 0.32f, EyeR = 0.1f;   // EyeR relative to H
            public CreatureMeshJaw Jaw;                                   // null = small closed cartoon mouth
            public float D0 = 0.24f, D1 = 0.8f, DH = 0.3f; public int DSpines; public float DMembrane = 0.75f;
            public float DRake = 0.45f, DTrail = 0f; public int DCols = 5;
            public float A0 = 0.6f, A1 = 0.82f, AH = 0.26f, ARake = 0.6f, ATrail = 0f;
            public bool SecondDorsal; public float D2a, D2b, D2H;
            public float PecT = 0.25f, PecLen = 0.16f; public bool PecFan; public int PecRays;
            public float PelT = 0.31f, PelLen = 0.11f;
            public Tail Caudal = Tail.Emarginate; public float CLen = 0.2f, CSpread = 0.42f;
            public bool Brows;   // lionfish supraocular tentacles
            // round 2 extras
            public bool Wings;          // flying fish: huge pectoral "wings" spread sideways
            public int Finlets;         // tuna: little yellow finlets in front of the tail (per side of the tail stock)
            public int Photophores;     // lanternfish: glowing dots along the belly
            public bool Lobed;          // coelacanth: fleshy lobes at the fin bases + tail tuft
            public float LowerLobe = 1f;   // forked tail: lower lobe length multiplier (flying fish)
        }

        static Spec SpecFor(SpeciesDef s)
        {
            var p = new Spec();
            string v = s.variant ?? "";
            switch (v)
            {
                case "tall":
                {
                    bool tang = CreatureMeshMath.Luma(s.colorB) < CreatureMeshMath.Luma(s.colorA);
                    p.Top = new CreatureMeshCurve(0f, 0.03f, 0.05f, 0.15f, 0.12f, 0.33f, 0.22f, 0.46f, 0.36f, 0.5f, 0.5f, 0.46f, 0.66f, 0.32f, 0.8f, 0.16f, 0.9f, 0.085f, 1f, 0.085f);
                    p.Bot = new CreatureMeshCurve(0f, -0.05f, 0.06f, -0.17f, 0.14f, -0.33f, 0.28f, -0.46f, 0.42f, -0.48f, 0.56f, -0.4f, 0.7f, -0.25f, 0.82f, -0.12f, 0.9f, -0.075f, 1f, -0.075f);
                    p.Wid = new CreatureMeshCurve(0f, 0.3f, 0.1f, 0.8f, 0.25f, 1f, 0.5f, 0.85f, 0.75f, 0.45f, 1f, 0.25f);
                    p.Nose = 0.04f;
                    p.EyeT = 0.13f; p.EyeTheta = 0.55f; p.EyeR = 0.085f;
                    p.D0 = 0.2f; p.D1 = 0.88f; p.DH = tang ? 0.13f : 0.12f; p.DRake = 0.35f; p.DTrail = tang ? 0f : 0.07f; p.DCols = 6;
                    p.A0 = 0.42f; p.A1 = 0.88f; p.AH = tang ? 0.12f : 0.11f; p.ARake = 0.35f; p.ATrail = tang ? 0f : 0.06f;
                    p.Caudal = tang ? Tail.Lunate : Tail.Truncate; p.CLen = 0.2f; p.CSpread = 0.34f;
                    p.PecT = 0.27f; p.PecLen = 0.14f; p.PelT = 0.33f; p.PelLen = tang ? 0.09f : 0.15f;
                    break;
                }
                case "lionfish":
                    p.Lb = 0.78f;
                    p.Top = new CreatureMeshCurve(0f, 0.06f, 0.08f, 0.28f, 0.2f, 0.44f, 0.35f, 0.5f, 0.52f, 0.44f, 0.7f, 0.28f, 0.85f, 0.14f, 1f, 0.12f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.08f, -0.24f, 0.2f, -0.4f, 0.38f, -0.44f, 0.56f, -0.36f, 0.72f, -0.22f, 0.86f, -0.12f, 1f, -0.11f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.1f, 0.85f, 0.3f, 1f, 0.55f, 0.75f, 0.8f, 0.35f, 1f, 0.25f);
                    p.Nose = 0.06f;
                    p.EyeT = 0.12f; p.EyeTheta = 0.45f; p.EyeR = 0.09f;
                    p.D0 = 0.17f; p.D1 = 0.6f; p.DH = 0.44f; p.DSpines = 9; p.DMembrane = 0.3f; p.DRake = 0.3f;
                    p.SecondDorsal = true; p.D2a = 0.6f; p.D2b = 0.84f; p.D2H = 0.2f;
                    p.A0 = 0.62f; p.A1 = 0.84f; p.AH = 0.24f; p.ARake = 0.5f;
                    p.PecT = 0.27f; p.PecLen = 0.2f; p.PecFan = true; p.PecRays = 9;
                    p.PelT = 0.33f; p.PelLen = 0.16f;
                    p.Caudal = Tail.Rounded; p.CLen = 0.22f; p.CSpread = 0.45f;
                    p.Brows = true;
                    break;
                case "barracuda":
                    p.Lb = 0.84f;
                    p.Top = new CreatureMeshCurve(0f, 0.03f, 0.06f, 0.15f, 0.14f, 0.3f, 0.24f, 0.42f, 0.4f, 0.5f, 0.6f, 0.45f, 0.8f, 0.28f, 0.93f, 0.18f, 1f, 0.18f);
                    p.Bot = new CreatureMeshCurve(0f, -0.02f, 0.06f, -0.12f, 0.14f, -0.28f, 0.26f, -0.42f, 0.44f, -0.48f, 0.62f, -0.42f, 0.8f, -0.26f, 0.93f, -0.17f, 1f, -0.17f);
                    p.Wid = new CreatureMeshCurve(0f, 0.2f, 0.08f, 0.55f, 0.2f, 0.85f, 0.4f, 1f, 0.65f, 0.8f, 0.85f, 0.45f, 1f, 0.35f);
                    p.Nose = 0.015f;
                    p.EyeT = 0.12f; p.EyeTheta = 0.3f; p.EyeR = 0.14f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.13f, MouthFrac = 0.1f, GapeDeg = 8f, Extend = 0.022f, JawWidth = 0.9f, Teeth = 4, ToothLen = 0.013f, ToothRadius = 0.0028f, FangBoost = 0.8f, ToothInward = 0.15f };
                    p.D0 = 0.38f; p.D1 = 0.48f; p.DH = 0.5f; p.DRake = 0.7f; p.DCols = 3;
                    p.SecondDorsal = true; p.D2a = 0.69f; p.D2b = 0.77f; p.D2H = 0.42f;
                    p.A0 = 0.69f; p.A1 = 0.78f; p.AH = 0.38f; p.ARake = 0.7f;
                    p.PecT = 0.22f; p.PecLen = 0.07f; p.PelT = 0.42f; p.PelLen = 0.05f;
                    p.Caudal = Tail.Forked; p.CLen = 0.17f; p.CSpread = 0.62f;
                    break;
                case "grouper":
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.2f, 0.15f, 0.36f, 0.3f, 0.48f, 0.45f, 0.5f, 0.62f, 0.43f, 0.78f, 0.28f, 0.9f, 0.18f, 1f, 0.18f);
                    p.Bot = new CreatureMeshCurve(0f, -0.08f, 0.06f, -0.26f, 0.16f, -0.42f, 0.32f, -0.48f, 0.5f, -0.46f, 0.66f, -0.36f, 0.8f, -0.22f, 0.9f, -0.16f, 1f, -0.16f);
                    p.Wid = new CreatureMeshCurve(0f, 0.4f, 0.08f, 0.8f, 0.2f, 1f, 0.45f, 0.95f, 0.7f, 0.6f, 0.9f, 0.35f, 1f, 0.3f);
                    p.Nose = 0.05f;
                    p.EyeT = 0.1f; p.EyeTheta = 0.55f; p.EyeR = 0.075f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.15f, MouthFrac = 0.18f, GapeDeg = 13f, Extend = 0.02f, JawWidth = 1.04f, JawDepth = 1.05f };
                    p.D0 = 0.25f; p.D1 = 0.8f; p.DH = 0.22f; p.DRake = 0.35f; p.DCols = 6;
                    p.A0 = 0.6f; p.A1 = 0.8f; p.AH = 0.24f; p.ARake = 0.45f;
                    p.PecT = 0.26f; p.PecLen = 0.17f; p.PelT = 0.3f; p.PelLen = 0.12f;
                    p.Caudal = Tail.Rounded; p.CLen = 0.2f; p.CSpread = 0.36f;
                    break;
                case "garibaldi":
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.22f, 0.14f, 0.38f, 0.25f, 0.48f, 0.4f, 0.5f, 0.56f, 0.44f, 0.72f, 0.3f, 0.86f, 0.15f, 1f, 0.14f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.06f, -0.22f, 0.15f, -0.36f, 0.3f, -0.46f, 0.45f, -0.46f, 0.6f, -0.38f, 0.74f, -0.24f, 0.86f, -0.13f, 1f, -0.12f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.08f, 0.75f, 0.2f, 0.95f, 0.35f, 1f, 0.55f, 0.85f, 0.75f, 0.5f, 1f, 0.25f);
                    p.Nose = 0.06f;
                    p.EyeT = 0.12f; p.EyeTheta = 0.4f; p.EyeR = 0.085f;
                    p.D0 = 0.25f; p.D1 = 0.84f; p.DH = 0.2f; p.DRake = 0.35f; p.DCols = 6;
                    p.A0 = 0.6f; p.A1 = 0.84f; p.AH = 0.22f; p.ARake = 0.45f;
                    p.PecT = 0.26f; p.PecLen = 0.15f; p.PelT = 0.31f; p.PelLen = 0.1f;
                    p.Caudal = Tail.Lunate; p.CLen = 0.2f; p.CSpread = 0.42f;
                    break;
                case "slender":   // sardine: streamlined schooling fish
                case "cleaner":   // cleaner wrasse: slim cigar with a long low dorsal
                {
                    bool cleaner = v == "cleaner";
                    p.Lb = cleaner ? 0.82f : 0.8f;
                    p.Top = new CreatureMeshCurve(0f, 0.03f, 0.07f, 0.17f, 0.16f, 0.32f, 0.3f, 0.43f, 0.45f, 0.46f, 0.6f, 0.4f, 0.76f, 0.26f, 0.9f, 0.15f, 1f, 0.13f);
                    p.Bot = new CreatureMeshCurve(0f, -0.03f, 0.07f, -0.16f, 0.16f, -0.3f, 0.3f, -0.42f, 0.45f, -0.44f, 0.6f, -0.37f, 0.76f, -0.24f, 0.9f, -0.14f, 1f, -0.12f);
                    p.Wid = new CreatureMeshCurve(0f, 0.3f, 0.08f, 0.65f, 0.2f, 0.9f, 0.38f, 1f, 0.6f, 0.82f, 0.82f, 0.45f, 1f, 0.3f);
                    p.Nose = cleaner ? 0.035f : 0.05f;
                    p.EyeT = 0.11f; p.EyeTheta = 0.28f; p.EyeR = cleaner ? 0.13f : 0.14f;
                    if (cleaner) { p.D0 = 0.28f; p.D1 = 0.86f; p.DH = 0.14f; p.DRake = 0.25f; p.DCols = 7; p.A0 = 0.55f; p.A1 = 0.86f; p.AH = 0.12f; p.ARake = 0.25f; p.Caudal = Tail.Truncate; p.CLen = 0.17f; p.CSpread = 0.36f; }
                    else { p.D0 = 0.4f; p.D1 = 0.58f; p.DH = 0.28f; p.DRake = 0.5f; p.DCols = 4; p.A0 = 0.7f; p.A1 = 0.82f; p.AH = 0.18f; p.ARake = 0.6f; p.Caudal = Tail.Forked; p.CLen = 0.2f; p.CSpread = 0.62f; }
                    p.PecT = 0.24f; p.PecLen = 0.13f; p.PelT = 0.42f; p.PelLen = 0.08f;
                    break;
                }
                case "flying":
                    p.Lb = 0.8f;
                    p.Top = new CreatureMeshCurve(0f, 0.04f, 0.07f, 0.2f, 0.16f, 0.36f, 0.3f, 0.46f, 0.45f, 0.48f, 0.62f, 0.4f, 0.8f, 0.24f, 0.92f, 0.15f, 1f, 0.13f);
                    p.Bot = new CreatureMeshCurve(0f, -0.04f, 0.07f, -0.2f, 0.16f, -0.36f, 0.3f, -0.46f, 0.45f, -0.48f, 0.62f, -0.4f, 0.8f, -0.24f, 0.92f, -0.14f, 1f, -0.12f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.08f, 0.75f, 0.2f, 1f, 0.45f, 0.95f, 0.75f, 0.5f, 1f, 0.3f);
                    p.Nose = 0.05f; p.EyeT = 0.1f; p.EyeTheta = 0.3f; p.EyeR = 0.17f;
                    p.D0 = 0.62f; p.D1 = 0.78f; p.DH = 0.18f; p.DRake = 0.5f; p.DCols = 3;
                    p.A0 = 0.64f; p.A1 = 0.78f; p.AH = 0.16f; p.ARake = 0.5f;
                    p.Wings = true; p.PecT = 0.22f; p.PecLen = 0.46f; p.PelT = 0.48f; p.PelLen = 0.2f;
                    p.Caudal = Tail.Forked; p.CLen = 0.24f; p.CSpread = 0.7f; p.LowerLobe = 1.35f;
                    break;
                case "tuna":
                    p.Lb = 0.86f;
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.22f, 0.14f, 0.38f, 0.28f, 0.48f, 0.42f, 0.5f, 0.58f, 0.42f, 0.74f, 0.25f, 0.86f, 0.12f, 0.94f, 0.07f, 1f, 0.06f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.06f, -0.22f, 0.14f, -0.38f, 0.28f, -0.48f, 0.42f, -0.5f, 0.58f, -0.42f, 0.74f, -0.25f, 0.86f, -0.12f, 0.94f, -0.07f, 1f, -0.06f);
                    p.Wid = new CreatureMeshCurve(0f, 0.4f, 0.08f, 0.75f, 0.2f, 0.95f, 0.4f, 1f, 0.62f, 0.85f, 0.82f, 0.5f, 0.94f, 0.55f, 1f, 0.4f);
                    p.Nose = 0.05f; p.EyeT = 0.1f; p.EyeTheta = 0.32f; p.EyeR = 0.1f;
                    p.D0 = 0.3f; p.D1 = 0.42f; p.DH = 0.22f; p.DRake = 0.6f; p.DCols = 3;
                    p.SecondDorsal = true; p.D2a = 0.5f; p.D2b = 0.6f; p.D2H = 0.5f;
                    p.A0 = 0.52f; p.A1 = 0.62f; p.AH = 0.48f; p.ARake = 0.7f;
                    p.PecT = 0.24f; p.PecLen = 0.3f; p.PelT = 0.3f; p.PelLen = 0.08f;
                    p.Caudal = Tail.Lunate; p.CLen = 0.2f; p.CSpread = 0.9f;
                    p.Finlets = 6;
                    break;
                case "lantern":
                    p.Lb = 0.8f;
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.24f, 0.15f, 0.4f, 0.28f, 0.46f, 0.45f, 0.44f, 0.62f, 0.36f, 0.8f, 0.22f, 0.92f, 0.14f, 1f, 0.13f);
                    p.Bot = new CreatureMeshCurve(0f, -0.05f, 0.06f, -0.24f, 0.15f, -0.4f, 0.28f, -0.46f, 0.45f, -0.44f, 0.62f, -0.36f, 0.8f, -0.22f, 0.92f, -0.13f, 1f, -0.12f);
                    p.Wid = new CreatureMeshCurve(0f, 0.4f, 0.1f, 0.85f, 0.25f, 1f, 0.5f, 0.85f, 0.8f, 0.45f, 1f, 0.3f);
                    p.Nose = 0.06f; p.EyeT = 0.12f; p.EyeTheta = 0.2f; p.EyeR = 0.22f;
                    p.D0 = 0.38f; p.D1 = 0.56f; p.DH = 0.22f; p.DCols = 4;
                    p.A0 = 0.58f; p.A1 = 0.78f; p.AH = 0.16f;
                    p.Caudal = Tail.Forked; p.CLen = 0.2f; p.CSpread = 0.5f;
                    p.Photophores = 7;
                    break;
                case "mandarin":
                    p.Lb = 0.78f;
                    p.Top = new CreatureMeshCurve(0f, 0.08f, 0.07f, 0.28f, 0.17f, 0.42f, 0.32f, 0.46f, 0.5f, 0.4f, 0.68f, 0.3f, 0.84f, 0.2f, 1f, 0.16f);
                    p.Bot = new CreatureMeshCurve(0f, -0.1f, 0.07f, -0.3f, 0.17f, -0.4f, 0.32f, -0.42f, 0.5f, -0.38f, 0.68f, -0.28f, 0.84f, -0.18f, 1f, -0.15f);
                    p.Wid = new CreatureMeshCurve(0f, 0.55f, 0.08f, 0.9f, 0.2f, 1f, 0.45f, 0.85f, 0.75f, 0.5f, 1f, 0.35f);
                    p.Nose = 0.07f; p.EyeT = 0.13f; p.EyeTheta = 0.7f; p.EyeR = 0.13f;
                    p.D0 = 0.24f; p.D1 = 0.38f; p.DH = 0.55f; p.DSpines = 4; p.DMembrane = 0.55f; p.DRake = 0.3f;
                    p.SecondDorsal = true; p.D2a = 0.42f; p.D2b = 0.86f; p.D2H = 0.32f;
                    p.A0 = 0.5f; p.A1 = 0.86f; p.AH = 0.24f; p.ARake = 0.4f;
                    p.PecT = 0.28f; p.PecLen = 0.24f; p.PecFan = true; p.PecRays = 6;
                    p.PelT = 0.26f; p.PelLen = 0.18f;
                    p.Caudal = Tail.Rounded; p.CLen = 0.24f; p.CSpread = 0.5f;
                    break;
                case "coelacanth":
                    p.Lb = 0.78f;
                    p.Top = new CreatureMeshCurve(0f, 0.05f, 0.06f, 0.2f, 0.15f, 0.36f, 0.3f, 0.46f, 0.45f, 0.48f, 0.62f, 0.42f, 0.78f, 0.3f, 0.9f, 0.2f, 1f, 0.17f);
                    p.Bot = new CreatureMeshCurve(0f, -0.08f, 0.06f, -0.24f, 0.16f, -0.38f, 0.32f, -0.46f, 0.5f, -0.45f, 0.66f, -0.36f, 0.8f, -0.24f, 0.9f, -0.17f, 1f, -0.15f);
                    p.Wid = new CreatureMeshCurve(0f, 0.4f, 0.08f, 0.8f, 0.2f, 1f, 0.45f, 0.95f, 0.7f, 0.6f, 0.9f, 0.35f, 1f, 0.3f);
                    p.Nose = 0.05f; p.EyeT = 0.1f; p.EyeTheta = 0.42f; p.EyeR = 0.09f;
                    p.Jaw = new CreatureMeshJaw { HingeT = 0.13f, MouthFrac = 0.2f, GapeDeg = 6f, Extend = 0.006f, JawWidth = 1.02f };
                    p.D0 = 0.3f; p.D1 = 0.4f; p.DH = 0.3f; p.DSpines = 5; p.DMembrane = 0.7f; p.DRake = 0.35f;
                    p.SecondDorsal = true; p.D2a = 0.62f; p.D2b = 0.72f; p.D2H = 0.32f;
                    p.A0 = 0.64f; p.A1 = 0.74f; p.AH = 0.3f; p.ARake = 0.4f;
                    p.PecT = 0.25f; p.PecLen = 0.2f; p.PelT = 0.48f; p.PelLen = 0.16f;
                    p.Caudal = Tail.Rounded; p.CLen = 0.2f; p.CSpread = 0.55f;
                    p.Lobed = true;
                    break;
                default:
                {
                    bool banded = s.pattern == PatternType.VerticalBands;
                    p.Top = new CreatureMeshCurve(0f, 0.04f, 0.06f, 0.2f, 0.14f, 0.36f, 0.26f, 0.48f, 0.38f, 0.52f, 0.52f, 0.47f, 0.68f, 0.32f, 0.82f, 0.17f, 0.9f, 0.12f, 1f, 0.12f);
                    p.Bot = new CreatureMeshCurve(0f, -0.06f, 0.06f, -0.2f, 0.14f, -0.34f, 0.28f, -0.45f, 0.42f, -0.46f, 0.56f, -0.38f, 0.7f, -0.24f, 0.83f, -0.13f, 0.9f, -0.11f, 1f, -0.11f);
                    p.Wid = new CreatureMeshCurve(0f, 0.35f, 0.08f, 0.72f, 0.2f, 0.95f, 0.32f, 1f, 0.5f, 0.88f, 0.68f, 0.6f, 0.85f, 0.3f, 1f, 0.22f);
                    p.Caudal = banded ? Tail.Rounded : Tail.Emarginate;
                    p.DCols = 6;
                    break;
                }
            }
            return p;
        }

        public static void Build(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            var sp = SpecFor(s);
            float H = CreatureMeshMath.Clamp(s.bodyHeight, 0.06f, 1.2f);
            float W = CreatureMeshMath.Clamp(s.bodyWidth, 0.04f, 0.8f);
            float F = CreatureMeshMath.Clamp(s.finSize, 0.3f, 2.5f);
            float Lb = sp.Lb;
            var b = ctx.B;
            ctx.EyeStyle = ctx.Friendly ? CreatureMeshEyeStyle.Friendly : CreatureMeshEyeStyle.Fish;
            // blotchy species are painted per facet (low-poly mottling) instead of per texel
            if (s.pattern == PatternType.Mottled || s.pattern == PatternType.Spots) ctx.Facet(true, s.pattern == PatternType.Mottled);

            var body = CreatureMeshAnatomy.Body(0.5f, Lb, t => sp.Top[t] * H, t => sp.Bot[t] * H, t => sp.Wid[t] * W * 0.5f, sp.Nose, BodyTs, 8);
            if (sp.Jaw != null) CreatureMeshAnatomy.PrepareUpperJaw(body, sp.Jaw);

            var chain = ctx.SpineAlong(body, new[] { 0.16f, 0.32f, 0.47f, 0.61f, 0.74f, 0.87f }, -1);
            int head = ctx.Spine[0];
            int last = ctx.Spine[ctx.Spine.Count - 1];
            Vector3 ped = body.Center(1f);
            int tail = ctx.Bone("TailFin", last, ped);
            ctx.TailFin = tail;

            body.Build(b, CreatureMeshSlot.Skin, chain.ByAnchor);
            ctx.GillU = 0.2f;
            ctx.EyeU = sp.EyeT;

            if (sp.Jaw != null) CreatureMeshAnatomy.BuildJaw(ctx, body, sp.Jaw, chain.ByAnchor);
            else CreatureMeshAnatomy.SnoutMouth(ctx, body, 0.03f, 0.55f, 0.35f, CreatureMeshSkin.Fixed(head));

            // goggle eyes
            float eyeR = sp.EyeR * H;
            CreatureMeshParts.EyePair(ctx, body, sp.EyeT, Pi * 0.5f + sp.EyeTheta, eyeR, 0.3f, ctx.Friendly ? 0.38f : 0.5f, CreatureMeshSkin.Fixed(head));

            if (sp.Brows)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = body.Point(sp.EyeT + 0.005f, Pi * 0.5f + sp.EyeTheta + 0.5f);
                    p.x *= side;
                    Vector3 tip = p + new Vector3(side * 0.012f, eyeR * 1.9f, -eyeR * 0.6f);
                    CreatureMeshParts.Cone(b, CreatureMeshSlot.Fin, p, tip, eyeR * 0.32f, 3, CreatureMeshParts.FinUv(3, 0.5f, 0.1f), CreatureMeshParts.FinUv(3, 0.5f, 0.9f), CreatureMeshSkin.One(head));
                }
            }

            BuildFins(ctx, body, chain, sp, H, F, head, tail, ped);
            BuildExtras(ctx, body, chain, sp, H, F, tail, ped);
        }

        /// Round-2 details: tuna finlets, lanternfish photophores, coelacanth fin lobes and tail tuft.
        static void BuildExtras(CreatureMeshContext ctx, CreatureMeshSweep body, CreatureMeshChain chain, Spec sp, float H, float F, int tail, Vector3 ped)
        {
            var b = ctx.B;
            if (sp.Finlets > 0)
            {
                for (int k = 0; k < sp.Finlets; k++)
                {
                    float t = CreatureMeshMath.Lerp(0.66f, 0.95f, k / (float)(sp.Finlets - 1));
                    for (int side = 0; side < 2; side++)
                    {
                        bool top = side == 0;
                        float h = 0.022f * F;
                        CreatureMeshAnatomy.MedianFin(ctx, body, t - 0.012f, t + 0.012f, top, q => h * CreatureMeshMath.Sin(Pi * q), q => h * 0.6f * q, 1, 0, chain.ByAnchor, 0.003f);
                    }
                }
                // keel on the tail stock
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 p = body.Point(0.95f, Pi * 0.5f);
                    if (side < 0) p = CreatureMeshMath.MirrorX(p);
                    CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, p, Vector3.forward, Vector3.up, 0.008f, 0.004f, 0.03f, 3, 5,
                        new Vector2(0.95f, 0.5f), new Vector2(0.95f, 0.5f), chain.ByAnchor);
                }
            }
            if (sp.Photophores > 0)
            {
                // two rows of glowing dots along the belly (the Glow slot pulses in the deep, G.2)
                for (int row = 0; row < 2; row++)
                    for (int k = 0; k < sp.Photophores; k++)
                    {
                        float t = CreatureMeshMath.Lerp(0.16f, 0.82f, k / (float)(sp.Photophores - 1));
                        float th = Pi * 0.5f - (row == 0 ? 0.55f : 0.95f);
                        Vector3 p = body.Point(t, th);
                        Vector3 nrm = body.Normal(t, th);
                        float r = 0.012f * (row == 0 ? 1f : 0.8f);
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 c = side > 0 ? p : CreatureMeshMath.MirrorX(p);
                            Vector3 nn = side > 0 ? nrm : CreatureMeshMath.MirrorX(nrm);
                            CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Glow, c - nn * (r * 0.3f), nn, Vector3.forward, r, r, r * 0.7f, 3, 5,
                                Vector2.zero, Vector2.zero, chain.ByAnchor);
                        }
                    }
            }
            if (sp.Lobed)
            {
                // fleshy lobes at the bases of the paired fins, the second dorsal and the anal fin
                var lobeUv = new Vector2(0.5f, 0.3f);
                float lr = 0.045f * H / 0.3f;
                Vector3[] at =
                {
                    body.Point(sp.PecT + 0.015f, Pi * 0.5f - 0.3f), body.Point(sp.PelT, 0.4f),
                };
                for (int k = 0; k < at.Length; k++)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 c = side > 0 ? at[k] : CreatureMeshMath.MirrorX(at[k]);
                        CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, c + new Vector3(side * lr * 0.4f, -lr * 0.2f, -lr * 0.5f), Vector3.back, Vector3.up, lr * 0.7f, lr * 0.7f, lr * 1.3f, 4, 6,
                            lobeUv, lobeUv, chain.ByAnchor);
                    }
                Vector3 d2 = body.Point((sp.D2a + sp.D2b) * 0.5f, Pi), an = body.Point((sp.A0 + sp.A1) * 0.5f, 0f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, d2, Vector3.back, Vector3.up, lr * 0.55f, lr * 0.9f, lr * 1.4f, 4, 6, lobeUv, lobeUv, chain.ByAnchor);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, an, Vector3.back, Vector3.up, lr * 0.55f, lr * 0.9f, lr * 1.4f, 4, 6, lobeUv, lobeUv, chain.ByAnchor);
                // the famous tail tuft: a small extra fin at the tip of the tail
                float L = sp.CLen * (0.85f + 0.15f * F);
                Vector3 tuftBase = ped + Vector3.back * (L * 0.82f);
                CreatureMeshAnatomy.CaudalFin(ctx, tuftBase, 0.012f, q => L * 0.35f * (1f - 0.4f * (2f * q - 1f) * (2f * q - 1f)), q => (2f * q - 1f) * L * 0.18f, tail, 3, 0.005f);
                CreatureMeshParts.Ellipsoid(b, CreatureMeshSlot.Skin, ped + Vector3.back * (L * 0.45f), Vector3.back, Vector3.up, 0.012f, 0.02f, L * 0.45f, 4, 6,
                    lobeUv, lobeUv, CreatureMeshSkin.Fixed(tail));
            }
        }

        static void BuildFins(CreatureMeshContext ctx, CreatureMeshSweep body, CreatureMeshChain chain, Spec sp, float H, float F, int head, int tail, Vector3 ped)
        {
            // ---- dorsal: rounded blade of a few triangles, or a crest of separate spines (lionfish)
            if (sp.DH > 0f)
            {
                int cols = sp.DSpines > 0 ? sp.DSpines * 2 : sp.DCols;
                Func<float, float> h = q =>
                {
                    float baseH = H * F * sp.DH;
                    if (sp.DSpines > 0)
                    {
                        float env = CreatureMeshMath.Lerp(0.75f, 1f, CreatureMeshMath.Sin(Pi * Math.Min(1f, q * 1.4f)));
                        float round = CreatureMeshMath.Nose(1f - q, 0.12f) * Math.Max(0.35f, CreatureMeshMath.Nose(q, 0.04f));
                        return baseH * env * round * CreatureMeshAnatomy.Spikes(q, sp.DSpines, sp.DMembrane, 18f);
                    }
                    return baseH * CreatureMeshMath.Nose(q, 0.12f) * CreatureMeshMath.Nose(1f - q, 0.22f) * CreatureMeshMath.Lerp(0.85f, 1.05f, q);
                };
                Func<float, float> rake = q => h(q) * (sp.DRake + 0.4f * q) + sp.DTrail * CreatureMeshMath.Pow(q, 4f);
                CreatureMeshAnatomy.MedianFin(ctx, body, sp.D0, sp.D1, true, h, rake, cols, 0, chain.ByAnchor, 0.006f * F);
            }
            if (sp.SecondDorsal)
            {
                Func<float, float> h2 = q => H * F * sp.D2H * CreatureMeshMath.Nose(q, 0.12f) * CreatureMeshMath.Nose(1f - q, 0.35f) * CreatureMeshMath.Lerp(1.1f, 0.6f, q);
                Func<float, float> r2 = q => h2(q) * (0.5f + 0.5f * q) + 0.01f * q;
                CreatureMeshAnatomy.MedianFin(ctx, body, sp.D2a, sp.D2b, true, h2, r2, 3, 0, chain.ByAnchor, 0.005f * F);
            }

            // ---- anal
            {
                Func<float, float> h = q => H * F * sp.AH * CreatureMeshMath.Nose(q, 0.15f) * CreatureMeshMath.Nose(1f - q, 0.3f) * CreatureMeshMath.Lerp(1f, 0.85f, q);
                Func<float, float> rake = q => h(q) * (sp.ARake + 0.3f * q) + sp.ATrail * CreatureMeshMath.Pow(q, 4f);
                CreatureMeshAnatomy.MedianFin(ctx, body, sp.A0, sp.A1, false, h, rake, 4, 0, chain.ByAnchor, 0.005f * F);
            }

            // ---- pelvic pair (skinned to the spine): one flat triangle-ish blade each
            {
                int pb = CreatureMeshContext.DominantBone(chain, body.Center(sp.PelT));
                float len = sp.PelLen * H * F * 1.2f + 0.02f * F;
                CreatureMeshAnatomy.PairedFin(ctx,
                    q => body.Point(sp.PelT + 0.035f * q, 0.38f) + Vector3.up * 0.003f,
                    q => body.Point(sp.PelT + 0.035f * q, 0.38f) + new Vector3(0.12f, -0.5f, -0.85f) * (len * CreatureMeshMath.Lerp(1f, 0.45f, q)),
                    pb, pb, 2, 1, 2, 0.004f * F);
            }

            // ---- pectoral pair (own bones)
            {
                float t = sp.PecT;
                Vector3 pa = body.Point(t, Pi * 0.5f - 0.15f), pbp = body.Point(t + 0.03f, Pi * 0.5f - 0.5f);
                Vector3 n = body.Normal(t + 0.015f, Pi * 0.5f - 0.3f);
                Vector3 mid = (pa + pbp) * 0.5f - n * 0.003f;
                int pr = ctx.Bone("PectoralR", CreatureMeshContext.DominantBone(chain, mid), mid);
                Vector3 midL = CreatureMeshMath.MirrorX(mid);
                int pl = ctx.Bone("PectoralL", CreatureMeshContext.DominantBone(chain, mid), midL);
                ctx.Pectorals.Add(pr);
                ctx.Pectorals.Add(pl);
                if (sp.Wings)
                {
                    // flying fish: long glider wings spread sideways (slightly swept back), several rays
                    float len = sp.PecLen * F * 0.8f;
                    CreatureMeshAnatomy.PairedFin(ctx,
                        q => Vector3.Lerp(pa, pbp, q) - n * 0.003f,
                        q =>
                        {
                            Vector3 d = CreatureMeshMath.Norm(new Vector3(1f, 0.1f, -0.15f - 0.6f * q));
                            float l = len * (0.45f + 0.55f * CreatureMeshMath.Sin(Pi * (0.18f + 0.62f * q))) * CreatureMeshAnatomy.Spikes(q, 5, 0.9f, 6f);
                            return Vector3.Lerp(pa, pbp, q) + d * l;
                        },
                        pr, pl, 8, 1, 2, 0.005f, (q, r) => Vector3.up * (0.01f * r * r));
                }
                else if (sp.PecFan)
                {
                    // lionfish: huge striped fan with free ray tips, spread from down-back to back
                    float len = sp.PecLen * F;
                    CreatureMeshAnatomy.PairedFin(ctx,
                        q => Vector3.Lerp(pa, pbp, q) - n * 0.004f,
                        q =>
                        {
                            float ang = CreatureMeshMath.Lerp(0.35f, -1.25f, q);
                            Vector3 d = new Vector3(0.38f, CreatureMeshMath.Sin(ang), -CreatureMeshMath.Cos(ang));
                            float l = len * (0.75f + 0.25f * CreatureMeshMath.Sin(Pi * q)) * CreatureMeshAnatomy.Spikes(q, sp.PecRays, 0.68f, 12f);
                            return Vector3.Lerp(pa, pbp, q) + CreatureMeshMath.Norm(d) * l;
                        },
                        pr, pl, sp.PecRays * 2, 1, 2, 0.004f,
                        (q, r) => Vector3.right * (0.02f * r * r));
                }
                else
                {
                    float len = sp.PecLen * F;
                    CreatureMeshAnatomy.PairedFin(ctx,
                        q => Vector3.Lerp(pa, pbp, q) - n * 0.003f,
                        q =>
                        {
                            Vector3 d = new Vector3(0.42f, 0.12f - 0.25f * q, -0.9f);
                            float l = len * (0.62f + 0.38f * CreatureMeshMath.Sin(Pi * (0.25f + 0.6f * q)));
                            return Vector3.Lerp(pa, pbp, q) + CreatureMeshMath.Norm(d) * l;
                        },
                        pr, pl, 3, 1, 2, 0.004f,
                        (q, r) => Vector3.right * (0.012f * r * r));
                }
            }

            // ---- caudal: chunky blade, column count matched to the outline (fork / lunate notch sampled exactly)
            {
                float bh = Math.Max(0.012f, body.HalfTop(0.97f) * 1.15f);
                float L = sp.CLen * (0.85f + 0.15f * F);
                float spread = sp.CSpread * H * F;
                Func<float, float> len, lift;
                int cols;
                switch (sp.Caudal)
                {
                    case Tail.Rounded:
                        len = q => L * (1f - 0.28f * (2f * q - 1f) * (2f * q - 1f));
                        lift = q => (2f * q - 1f) * spread * 0.85f;
                        cols = 4;
                        break;
                    case Tail.Forked:
                        float lower = sp.LowerLobe;
                        len = q => L * (0.42f + 0.58f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * q - 1f), 0.9f)) * (q < 0.5f ? CreatureMeshMath.Lerp(1f, lower, CreatureMeshMath.Abs(2f * q - 1f)) : 1f);
                        lift = q => (2f * q - 1f) * spread * (0.5f + 0.5f * CreatureMeshMath.Abs(2f * q - 1f));
                        cols = 4;
                        break;
                    case Tail.Lunate:
                        len = q => L * (0.62f + 0.38f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * q - 1f), 1.3f));
                        lift = q => (2f * q - 1f) * spread;
                        cols = 4;
                        break;
                    case Tail.Truncate:
                        len = q => L * (0.9f + 0.1f * CreatureMeshMath.Abs(2f * q - 1f));
                        lift = q => (2f * q - 1f) * spread * 0.9f;
                        cols = 2;
                        break;
                    default:
                        len = q => L * (0.8f + 0.2f * CreatureMeshMath.Pow(CreatureMeshMath.Abs(2f * q - 1f), 1.5f));
                        lift = q => (2f * q - 1f) * spread * 0.9f;
                        cols = 4;
                        break;
                }
                CreatureMeshAnatomy.CaudalFin(ctx, ped, bh, len, lift, tail, cols, 0.006f * F, 0.006f);
            }
        }
    }
}
