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
        // round 2: extra rig roles (CreatureRigExtra) – indices into Bones, −1 = none
        public CreatureGait Gait;
        public bool HasExtra;
        public int[] Legs, Knees, Wings, WingTips, Claws, Fingers, EyeStalks, TailChain, Ears;
        public int Jaw = -1, Head = -1, BodyBone = -1;
        public bool LandSpecies;
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

            if (kit.HasExtra)
            {
                var ex = root.AddComponent<CreatureRigExtra>();
                ex.gait = kit.Gait;
                ex.legs = Map(kit.Legs, bones);
                ex.knees = Map(kit.Knees, bones);
                ex.wings = Map(kit.Wings, bones);
                ex.wingTips = Map(kit.WingTips, bones);
                ex.claws = Map(kit.Claws, bones);
                ex.fingers = Map(kit.Fingers, bones);
                ex.eyeStalks = Map(kit.EyeStalks, bones);
                ex.tail = Map(kit.TailChain, bones);
                ex.ears = Map(kit.Ears, bones);
                ex.jaw = kit.Jaw >= 0 ? bones[kit.Jaw] : null;
                ex.head = kit.Head >= 0 ? bones[kit.Head] : null;
                ex.body = kit.BodyBone >= 0 ? bones[kit.BodyBone] : null;
            }
            return root;
        }

        static Transform[] Map(int[] idx, Transform[] bones)
        {
            if (idx == null) return new Transform[0];
            var r = new Transform[idx.Length];
            for (int i = 0; i < idx.Length; i++) r[i] = idx[i] >= 0 && idx[i] < bones.Length ? bones[idx[i]] : null;
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
            }
            catch (Exception)
            {
                CreatureMeshBakery.Forget(s.id);
                throw;
            }
            kit = MakeKit(s, bake);
            Kits[s.id] = kit;
            return kit;
        }

        /// Low-poly kit: faceted (flat shaded, unwelded) skinned mesh built from the managed bake; the flattening
        /// happens on the bake thread (same result as LowPoly.Flatten, bone weights + bind poses preserved).
        static CreatureMeshKit MakeKit(SpeciesDef s, CreatureMeshBake bake)
        {
            var ctx = bake.Ctx;
            var b = ctx.B;
            var f = bake.Flat;
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
                Gait = ctx.Gait,
                HasExtra = ctx.HasExtraRig,
                Legs = ctx.Legs.ToArray(), Knees = ctx.LegKnees.ToArray(),
                Wings = ctx.Wings.ToArray(), WingTips = ctx.WingTips.ToArray(),
                Claws = ctx.Claws.ToArray(), Fingers = ctx.ClawFingers.ToArray(),
                EyeStalks = ctx.EyeStalks.ToArray(), TailChain = ctx.TailChain.ToArray(), Ears = ctx.Ears.ToArray(),
                Jaw = ctx.Jaw, Head = ctx.Head, BodyBone = ctx.BodyBone,
                LandSpecies = s.land,
            };

            var mesh = new Mesh { name = "SB_Creature_" + s.id };
            if (f.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = f.Verts;
            mesh.uv = f.Uvs;
            mesh.normals = f.Normals;
            var used = new List<int>();
            for (int i = 0; i < CreatureMeshSlot.Count; i++) if (f.Tris[i].Length > 0) used.Add(i);
            mesh.subMeshCount = used.Count;
            for (int i = 0; i < used.Count; i++) mesh.SetTriangles(f.Tris[used[i]], i, false);
            mesh.boneWeights = f.Weights;
            var bind = new Matrix4x4[kit.Bones.Length];
            for (int i = 0; i < bind.Length; i++) bind[i] = Matrix4x4.Translate(-kit.Bones[i].Pos);
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
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
            // wings flap / fold, legs swing, jaws shoot out, mouth pouches balloon
            if (s.plan == BodyPlan.Bird || s.plan == BodyPlan.Bat) pad += new Vector3(0.05f, 0.45f, 0.35f) * len;
            if (s.plan == BodyPlan.Crab || s.plan == BodyPlan.Lizard || s.plan == BodyPlan.Quadruped || s.plan == BodyPlan.Primate) pad += new Vector3(0.08f, 0.1f, 0.12f) * len;
            if (s.plan == BodyPlan.Dolphin || s.plan == BodyPlan.Whale) pad.y += 0.12f * len;
            if (ctx.Jaw >= 0) pad += new Vector3(0.1f, 0.15f, 0.12f) * len;
            kit.LocalBounds = new Bounds(mb.center, size + pad * 2f);
            return kit;
        }
    }
}
