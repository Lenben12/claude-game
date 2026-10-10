using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    internal enum CreatureMeshEyeStyle { Friendly, Fish, Shark, Squid, Octopus, Cuttle, Turtle, Mammal, Vampire, Angler }

    /// How the skin texture is mapped: Loft = u along the body (0 head .. 1 tail), v around (0 belly, 0.5 back);
    /// Disc = top-down projection (rays): v 0..0.48 belly, 0.52..1 back; Bell = jelly bell (u around, v apex->rim).
    internal enum CreatureMeshLayout { Loft, Disc, Bell }

    internal sealed class CreatureMeshBoneSpec
    {
        public string Name;
        public int Parent;      // -1 = creature root
        public Vector3 Pos;     // rest position in root space (rest rotation is always identity)
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
        /// Loft layout: u (0 head .. 1 tail) -> local body radius (perimeter / 2 pi) so patterns stay round.
        public Func<float, float> SkinRadius;
        public float SkinLength = 1f;
        /// Disc layout (rays): front z, disc length and width, outline (|x| -> z of the leading / trailing edge).
        public float DiscFront = 0.5f, DiscLength = 1f, DiscWidth = 1f;
        public Func<float, float> DiscLeadZ, DiscTrailZ;
        /// Painting hints in skin u (along the body); negative = none.
        public float GillU = -1f, EyeU = -1f, PeduncleU = -1f, HeadEndU = -1f;
        public float SkinSmoothness = 0.55f, FinSmoothness = 0.45f;
        public bool CastShadows = true;

        public CreatureMeshContext(SpeciesDef s) { S = s; }

        public bool Friendly => !S.IsDangerous;

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

        /// Sets SkinRadius from the body sweep (sampled perimeter).
        public void SkinFrom(CreatureMeshSweep body)
        {
            const int n = 33;
            var r = new float[n];
            for (int i = 0; i < n; i++) r[i] = Math.Max(0.002f, body.Perimeter(i / (float)(n - 1)) / CreatureMeshMath.TwoPi);
            Vector3 a = body.Center(0f), c = body.Center(1f);
            float len = 0f;
            Vector3 prev = a;
            for (int i = 1; i <= 32; i++) { Vector3 q = body.Center(i / 32f); len += (q - prev).magnitude; prev = q; }
            SkinLength = Math.Max(0.05f, len);
            SkinRadius = u =>
            {
                float f = CreatureMeshMath.Clamp01(u) * (n - 1);
                int i = Math.Min(n - 2, (int)f);
                return CreatureMeshMath.Lerp(r[i], r[i + 1], f - i);
            };
        }

        /// The bone (among the chain) with the biggest weight at a point.
        public static int DominantBone(CreatureMeshChain chain, Vector3 p)
        {
            BoneWeight w = chain.Weigh(p);
            return w.weight1 > w.weight0 ? w.boneIndex1 : w.boneIndex0;
        }
    }
}
