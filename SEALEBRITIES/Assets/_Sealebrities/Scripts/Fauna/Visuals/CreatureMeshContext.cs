using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    internal enum CreatureMeshEyeStyle { Friendly, Fish, Shark, Squid, Octopus, Cuttle, Turtle, Mammal, Vampire, Angler }

    /// Procedural limb animation family of a body plan (FaunaGaitAnimator; carried by CreatureRigExtra).
    public enum CreatureGait : byte { None, Crab, Star, Bird, Bat, Lizard, Quadruped, Primate, Marine, Jaw }

    /// How the skin texture is mapped: Loft = u along the body (0 head .. 1 tail), v around (0 belly, 0.5 back);
    /// Disc = top-down projection (rays): v 0..0.48 belly, 0.52..1 back; Bell = jelly bell (solid translucent color).
    internal enum CreatureMeshLayout { Loft, Disc, Bell, Custom }

    internal sealed class CreatureMeshBoneSpec
    {
        public string Name;
        public int Parent;      // -1 = creature root
        public Vector3 Pos;     // rest position in root space (rest rotation is always identity)
    }

    /// Cartoon "goggle" eye of one style: low-poly sclera ball + black pupil lens (+ white glint for friendly faces).
    internal struct CreatureMeshEyeLook
    {
        public float Scale;       // radius multiplier on the builder's anatomical eye size
        public float Pupil;       // pupil radius / eye radius
        public float PupilW, PupilH;
        public float Glint;       // white catch-light size / eye radius (0 = none)
        public Color Sclera, Iris;
    }

    internal static class CreatureMeshEyes
    {
        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

        /// Friendly species get big white toy eyes with a glint; deep-sea / predatory styles stay small and creepy.
        public static CreatureMeshEyeLook Look(CreatureMeshEyeStyle style)
        {
            switch (style)
            {
                case CreatureMeshEyeStyle.Friendly: return new CreatureMeshEyeLook { Scale = 1.4f, Pupil = 0.6f, PupilW = 1f, PupilH = 1f, Glint = 0.17f, Sclera = Hex(0xFAFAF4), Iris = Hex(0x1C1E26) };
                case CreatureMeshEyeStyle.Turtle: return new CreatureMeshEyeLook { Scale = 1.3f, Pupil = 0.62f, PupilW = 1f, PupilH = 1f, Glint = 0.17f, Sclera = Hex(0xFAFAF4), Iris = Hex(0x1C1E26) };
                case CreatureMeshEyeStyle.Mammal: return new CreatureMeshEyeLook { Scale = 1.3f, Pupil = 0.72f, PupilW = 1f, PupilH = 1f, Glint = 0.24f, Sclera = Hex(0x3A2A22), Iris = Hex(0x141418) };
                case CreatureMeshEyeStyle.Octopus: return new CreatureMeshEyeLook { Scale = 1.2f, Pupil = 0.62f, PupilW = 1f, PupilH = 0.42f, Glint = 0.17f, Sclera = Hex(0xF0CF5A), Iris = Hex(0x1C1E26) };
                case CreatureMeshEyeStyle.Cuttle: return new CreatureMeshEyeLook { Scale = 1.15f, Pupil = 0.64f, PupilW = 1f, PupilH = 0.38f, Glint = 0.17f, Sclera = Hex(0xE6D79A), Iris = Hex(0x1C1E26) };
                case CreatureMeshEyeStyle.Fish: return new CreatureMeshEyeLook { Scale = 1.05f, Pupil = 0.5f, PupilW = 1f, PupilH = 1f, Glint = 0f, Sclera = Hex(0xEBDB8C), Iris = Hex(0x15161B) };
                case CreatureMeshEyeStyle.Shark: return new CreatureMeshEyeLook { Scale = 1f, Pupil = 0.62f, PupilW = 1f, PupilH = 1f, Glint = 0f, Sclera = Hex(0x3C424C), Iris = Hex(0x111216) };
                case CreatureMeshEyeStyle.Squid: return new CreatureMeshEyeLook { Scale = 1f, Pupil = 0.66f, PupilW = 1f, PupilH = 1f, Glint = 0f, Sclera = Hex(0xEFE6D2), Iris = Hex(0x101116) };
                case CreatureMeshEyeStyle.Vampire: return new CreatureMeshEyeLook { Scale = 1f, Pupil = 0.55f, PupilW = 1f, PupilH = 1f, Glint = 0f, Sclera = Hex(0xA3162A), Iris = Hex(0x0E0E12) };
                default: return new CreatureMeshEyeLook { Scale = 0.85f, Pupil = 0.32f, PupilW = 1f, PupilH = 1f, Glint = 0f, Sclera = Hex(0xCCD5D2), Iris = Hex(0x23262B) };   // Angler
            }
        }
    }

    /// Solid slot colors (shared by the materials and the managed icon fallback).
    internal static class CreatureMeshColors
    {
        public static readonly Color Mouth = new Color(0.24f, 0.11f, 0.13f, 1f);
        public static readonly Color Tooth = new Color(0.97f, 0.96f, 0.92f, 1f);

        public static Color Glow(CreatureMeshContext ctx)
        {
            var s = ctx.S;
            Color c = s.plan == BodyPlan.Anglerfish ? ctx.LureColor : s.colorC;
            c.a = 1f;
            return c;
        }

        /// Jelly bell: transparent pastel.
        public static Color Bell(SpeciesDef s)
        {
            Color c = Color.Lerp(s.colorA, Color.white, 0.15f);
            c.a = 0.42f;
            return c;
        }

        /// Jelly gonads (turtles use the shell palette texture instead).
        public static Color Shell(SpeciesDef s)
        {
            Color c = s.colorB;
            c.a = 0.82f;
            return c;
        }

        /// Jelly tentacles / oral arms.
        public static Color Extra(SpeciesDef s)
        {
            Color c = Color.Lerp(s.colorC, s.colorA, 0.35f);
            c.a = 0.5f;
            return c;
        }
    }

    /// Everything one body-plan builder produces: geometry, bones, rig roles and painting hints.
    internal sealed class CreatureMeshContext
    {
        public readonly SpeciesDef S;
        public readonly CreatureMeshBuilder B = new CreatureMeshBuilder();
        public readonly List<CreatureMeshBoneSpec> Bones = new List<CreatureMeshBoneSpec>();
        public readonly List<int> Spine = new List<int>();
        public readonly List<int> Pectorals = new List<int>();
        public readonly List<int> Flippers = new List<int>();
        public readonly List<int> Tentacles = new List<int>();
        public int TailFin = -1, Bell = -1, Lure = -1;
        public Vector3 LurePos;
        public Color LureColor = new Color(1f, 0.86f, 0.42f);

        public CreatureMeshEyeStyle EyeStyle = CreatureMeshEyeStyle.Fish;
        public CreatureMeshLayout Layout = CreatureMeshLayout.Loft;
        /// Slots whose triangles take one flat color each (centroid uv): faceted mottling for octopus, grouper, moray ...
        public readonly bool[] FacetUv = new bool[CreatureMeshSlot.Count];
        /// Disc layout (rays): front z, disc length and width, outline (|x| -> z of the leading / trailing edge).
        public float DiscFront = 0.5f, DiscLength = 1f, DiscWidth = 1f;
        public Func<float, float> DiscLeadZ, DiscTrailZ;
        /// Painting hints in skin u (along the body); negative = none.
        public float GillU = -1f, EyeU = -1f;
        /// Matte low-poly materials; wet mammals get a touch more sheen.
        public float SkinSmoothness = 0.1f;
        public bool CastShadows = true;

        // ---- round 2: shading softness and the extra rig roles of the new body plans (CreatureRigExtra)
        /// Normal smoothing per slot (0 faceted, 1 smooth; see CreatureMeshFlat.From). Fish and invertebrates stay
        /// faceted (art bible §2.5); eyes are always smooth (round googly look); mammals, birds and land animals are
        /// smooth "characters" (L.13 / G.1 "smooth back").
        public readonly float[] Smooth = new float[CreatureMeshSlot.Count];
        public float Crease = 50f;
        /// Leg hip bones; LegKnees[i] = the knee bone of leg i (child of the hip) or −1.
        public readonly List<int> Legs = new List<int>();
        public readonly List<int> LegKnees = new List<int>();
        /// Wing shoulder bones; WingTips[i] = wrist/elbow bone (child) or −1.
        public readonly List<int> Wings = new List<int>();
        public readonly List<int> WingTips = new List<int>();
        /// Crab claw arm bones; ClawFingers[i] = the movable finger (dactyl) bone or −1.
        public readonly List<int> Claws = new List<int>();
        public readonly List<int> ClawFingers = new List<int>();
        public readonly List<int> EyeStalks = new List<int>();
        public readonly List<int> TailChain = new List<int>();
        public readonly List<int> Ears = new List<int>();
        public int Jaw = -1, Head = -1, BodyBone = -1;
        public CreatureGait Gait = CreatureGait.None;
        public bool HasExtraRig => Legs.Count > 0 || Wings.Count > 0 || Claws.Count > 0 || EyeStalks.Count > 0 || TailChain.Count > 0
                                   || Ears.Count > 0 || Jaw >= 0 || Head >= 0 || BodyBone >= 0 || Gait != CreatureGait.None;

        public CreatureMeshContext(SpeciesDef s)
        {
            S = s;
            Smooth[CreatureMeshSlot.Eye] = 1f;
            Smooth[CreatureMeshSlot.Pupil] = 1f;
            Smooth[CreatureMeshSlot.Teeth] = 0.6f;
        }

        /// Smooth "character" shading for the body and limb slots (mammals, birds, land animals).
        public void SmoothBody(float skin = 1f, float fin = 1f)
        {
            Smooth[CreatureMeshSlot.Skin] = skin;
            Smooth[CreatureMeshSlot.Fin] = fin;
            Smooth[CreatureMeshSlot.Shell] = Math.Max(Smooth[CreatureMeshSlot.Shell], skin * 0.6f);
            Smooth[CreatureMeshSlot.Mouth] = 1f;
            Crease = 72f;                       // 6–8-sided limbs and rounded bodies blend into one soft surface
            if (skin >= 0.9f) SmoothTexture = true;
        }

        /// The skin texture is sampled bilinearly (soft colour borders on smooth "character" bodies) instead of the
        /// point-filtered colour blocks of the faceted fish.
        public bool SmoothTexture;

        public bool Friendly => !S.IsDangerous;

        /// Mottled species: body (and fins) are painted per facet instead of per texel.
        public void Facet(bool skin, bool fins)
        {
            FacetUv[CreatureMeshSlot.Skin] = skin;
            FacetUv[CreatureMeshSlot.Fin] = fins;
        }

        public int Bone(string name, int parent, Vector3 pos)
        {
            Bones.Add(new CreatureMeshBoneSpec { Name = name, Parent = parent, Pos = pos });
            return Bones.Count - 1;
        }

        /// Spine bones along a sweep center (parent chain head -> tail) registered as rig spine.
        public CreatureMeshChain SpineAlong(CreatureMeshSweep body, float[] ts, int parent, string prefix = "Spine")
        {
            var ids = new int[ts.Length];
            var piv = new Vector3[ts.Length];
            int p = parent;
            for (int i = 0; i < ts.Length; i++)
            {
                piv[i] = body.Center(ts[i]);
                ids[i] = Bone(prefix + i, p, piv[i]);
                Spine.Add(ids[i]);
                p = ids[i];
            }
            return new CreatureMeshChain(ids, piv, body.Center(1f));
        }

        /// The bone (among the chain) with the biggest weight at a point.
        public static int DominantBone(CreatureMeshChain chain, Vector3 p)
        {
            BoneWeight w = chain.Weigh(p);
            return w.weight1 > w.weight0 ? w.boneIndex1 : w.boneIndex0;
        }
    }
}
