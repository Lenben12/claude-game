using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// Shared per-species render data (mesh, materials, bone layout, rig roles).
    internal sealed class CreatureMeshKit
    {
        public string Id;
        public CreatureMeshBake Bake;
        public Mesh Mesh;
        public Material[] Materials;
        public int[] SubmeshSlots;
        public CreatureMeshBoneSpec[] Bones;
        public int[] Spine, Pectorals, Flippers, Tentacles;
        public int TailFin = -1, Bell = -1, Lure = -1;
        public Vector3 LurePos;
        public Color LureColor;
        public Bounds LocalBounds;
        public bool CastShadows;
        public readonly List<UnityEngine.Object> Owned = new List<UnityEngine.Object>();

        public bool Valid
        {
            get
            {
                if (Mesh == null || Materials == null) return false;
                for (int i = 0; i < Materials.Length; i++) if (Materials[i] == null) return false;
                return true;
            }
        }
    }

    /// Procedural creature models for all species (see CreatureMesh* builders).
    /// Model convention: body length exactly 1 m along +Z (head at +Z), centered at the origin, +Y up;
    /// seahorse 1 m tall, jelly 1 m bell diameter, rays 1 m wingspan. One SkinnedMeshRenderer per creature.
    /// Rig: spine[] is a parent chain head -> tail with identity rest rotations (rotate around local Y, or X for
    /// pitch swimmers); tailFin / pectoralFins / flippers are single bones; tentacles[] are chain roots where each
    /// bone has exactly one bone child; cephalopods: spine = { head, mantle } and bell = mantle (vampire squid: no bell).
    public static class CreatureFactory
    {
        static readonly Dictionary<string, CreatureMeshKit> Kits = new Dictionary<string, CreatureMeshKit>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Kits.Clear(); }

        /// Returns a new active GameObject with a filled CreatureRig (no colliders). Meshes, materials and textures
        /// are shared between all builds of the same species.
        public static GameObject Build(SpeciesDef s)
        {
            if (s == null) return null;
            var kit = Kit(s);
            var root = new GameObject("Creature_" + s.id);
            var rig = root.AddComponent<CreatureRig>();
            var rt = root.transform;

            var bones = new Transform[kit.Bones.Length];
            for (int i = 0; i < kit.Bones.Length; i++)
            {
                var spec = kit.Bones[i];
                var t = new GameObject(spec.Name).transform;
                Transform parent = spec.Parent >= 0 ? bones[spec.Parent] : rt;
                Vector3 parentPos = spec.Parent >= 0 ? kit.Bones[spec.Parent].Pos : Vector3.zero;
                t.SetParent(parent, false);
                t.localPosition = spec.Pos - parentPos;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                bones[i] = t;
            }

            var model = new GameObject("Model");
            model.transform.SetParent(rt, false);
            var smr = model.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = kit.Mesh;
            smr.sharedMaterials = kit.Materials;
            smr.bones = bones;
            smr.rootBone = rt;
            smr.localBounds = kit.LocalBounds;
            smr.updateWhenOffscreen = false;
            smr.quality = SkinQuality.Auto;
            smr.skinnedMotionVectors = false;
            smr.shadowCastingMode = kit.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            smr.receiveShadows = true;

            if (kit.Lure >= 0)
            {
                var lg = new GameObject("LureLight");
                lg.transform.SetParent(bones[kit.Lure], false);
                lg.transform.localPosition = kit.LurePos - kit.Bones[kit.Lure].Pos;
                var light = lg.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = kit.LureColor;
                light.range = 4f;
                light.intensity = 2.5f;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.Auto;
                rig.lureLight = light;
            }

            rig.plan = s.plan;
            rig.spine = Map(kit.Spine, bones);
            rig.tailFin = kit.TailFin >= 0 ? bones[kit.TailFin] : null;
            rig.pectoralFins = Map(kit.Pectorals, bones);
            rig.flippers = Map(kit.Flippers, bones);
            rig.tentacles = Map(kit.Tentacles, bones);
            rig.bell = kit.Bell >= 0 ? bones[kit.Bell] : null;
            rig.renderers = new Renderer[] { smr };
            rig.bodyLength = 1f;
            return root;
        }

        static Transform[] Map(int[] idx, Transform[] bones)
        {
            var r = new Transform[idx.Length];
            for (int i = 0; i < idx.Length; i++) r[i] = bones[idx[i]];
            return r;
        }

        // ------------------------------------------------------------------------------------------ kits

        internal static CreatureMeshKit Kit(SpeciesDef s)
        {
            if (Kits.TryGetValue(s.id, out var kit) && kit != null && kit.Valid) return kit;
            CreatureMeshBake bake;
            try
            {
                bake = CreatureMeshBakery.Get(s);
                if (!bake.HasPixels) bake = CreatureMeshBake.Make(s);   // pixels were released after an earlier kit
            }
            catch (Exception)
            {
                CreatureMeshBakery.Forget(s.id);
                throw;
            }
            kit = MakeKit(s, bake);
            bake.ReleasePixels();
            Kits[s.id] = kit;
            return kit;
        }

        static CreatureMeshKit MakeKit(SpeciesDef s, CreatureMeshBake bake)
        {
            var ctx = bake.Ctx;
            var b = ctx.B;
            var kit = new CreatureMeshKit
            {
                Id = s.id,
                Bake = bake,
                Bones = ctx.Bones.ToArray(),
                Spine = ctx.Spine.ToArray(),
                Pectorals = ctx.Pectorals.ToArray(),
                Flippers = ctx.Flippers.ToArray(),
                Tentacles = ctx.Tentacles.ToArray(),
                TailFin = ctx.TailFin,
                Bell = ctx.Bell,
                Lure = ctx.Lure,
                LurePos = ctx.LurePos,
                LureColor = ctx.LureColor,
                CastShadows = ctx.CastShadows,
            };

            var mesh = new Mesh { name = "SB_Creature_" + s.id };
            if (b.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(b.Verts);
            mesh.SetUVs(0, b.Uvs);
            mesh.normals = b.ComputeNormals();
            var used = new List<int>();
            for (int i = 0; i < CreatureMeshSlot.Count; i++) if (b.Tris[i].Count > 0) used.Add(i);
            mesh.subMeshCount = used.Count;
            for (int i = 0; i < used.Count; i++) mesh.SetTriangles(b.Tris[used[i]], i, false);
            var weights = new BoneWeight[b.Count];
            for (int i = 0; i < weights.Length; i++) weights[i] = Sorted(b.Weights[i]);
            mesh.boneWeights = weights;
            var bind = new Matrix4x4[kit.Bones.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-kit.Bones[i].Pos);
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            kit.Mesh = mesh;
            kit.SubmeshSlots = used.ToArray();
            kit.Owned.Add(mesh);
            kit.Materials = CreatureMeshMaterials.For(bake, kit.SubmeshSlots, kit.Owned);

            // generous bounds in root space (animation bends the body, wings flap, tentacles sway)
            Bounds mb = b.ComputeBounds();
            Vector3 size = mb.size;
            float len = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            Vector3 pad = new Vector3(0.14f * len, 0.08f * len, 0.05f * len);
            if (s.plan == BodyPlan.Ray) pad.y += 0.35f;
            if (s.plan == BodyPlan.Jelly || s.plan == BodyPlan.Octopus || s.plan == BodyPlan.VampireSquid || s.plan == BodyPlan.Squid) pad += Vector3.one * 0.12f * len;
            if (s.plan == BodyPlan.Turtle || s.plan == BodyPlan.SeaLion) pad.y += 0.15f;
            kit.LocalBounds = new Bounds(mb.center, size + pad * 2f);
            return kit;
        }

        static BoneWeight Sorted(BoneWeight w)
        {
            // Unity expects weights in descending order (lower skin quality settings use the first entries)
            var idx = new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
            var wt = new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
            for (int i = 0; i < 4; i++)
                for (int j = i + 1; j < 4; j++)
                    if (wt[j] > wt[i]) { float t = wt[i]; wt[i] = wt[j]; wt[j] = t; int k = idx[i]; idx[i] = idx[j]; idx[j] = k; }
            float sum = wt[0] + wt[1] + wt[2] + wt[3];
            if (sum <= 1e-6f) { wt[0] = 1f; sum = 1f; }
            return new BoneWeight
            {
                boneIndex0 = idx[0], weight0 = wt[0] / sum,
                boneIndex1 = wt[1] > 0f ? idx[1] : 0, weight1 = wt[1] / sum,
                boneIndex2 = wt[2] > 0f ? idx[2] : 0, weight2 = wt[2] / sum,
                boneIndex3 = wt[3] > 0f ? idx[3] : 0, weight3 = wt[3] / sum,
            };
        }
    }
}
