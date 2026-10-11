using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// "Fish everywhere" (L.1, K.3) without hundreds of simulated animals: every real member of a big school (sardine,
    /// lanternfish – and blue tang / barracuda while a Big School event runs nearby) brings a few visual buddies that
    /// swim in formation around it. Buddies are GPU-instanced static meshes in 4 pre-baked swim poses (CPU skinned
    /// once from the species kit), drawn with Graphics.RenderMeshInstanced – no GameObjects, no simulation, no net
    /// traffic. Works on co-op clients too (it follows whatever ICreature is in the CreatureRegistry, proxies
    /// included). Only real animals are filmable; buddies are scenery. No per-frame allocations.
    public sealed class CreatureSwarm : MonoBehaviour
    {
        public const float MaxDistance = 45f;
        public const int Poses = 4;
        const int Batch = 1023;

        sealed class Kind
        {
            public SpeciesDef s;
            public Mesh[] poses;
            public Material[] mats;
            public int subs;
            public readonly Matrix4x4[][] m = new Matrix4x4[Poses][];
            public readonly int[] n = new int[Poses];
            public bool failed;
        }

        readonly Dictionary<SpeciesDef, Kind> kinds = new Dictionary<SpeciesDef, Kind>();
        readonly List<Kind> used = new List<Kind>(8);
        RenderParams rp;
        bool rpInit;

        /// Buddies per real animal (0 = none). Big-school species always; event schools only while their event runs.
        public static int BuddiesFor(SpeciesDef s, Vector3 at)
        {
            if (s == null) return 0;
            switch (s.id)
            {
                case "sardine": return 4;
                case "lanternfish": return 2;
                // playtest round 2 („überall Fische“): the small reef schoolers always bring a couple of buddies
                case "blue_tang":
                case "barracuda":
                    if (FaunaEventInfo.IsActive && FaunaEventInfo.ActiveId == "big_school" && (at - FaunaEventInfo.Center).sqrMagnitude < 60f * 60f) return s.id == "blue_tang" ? 4 : 1;
                    return s.id == "blue_tang" ? 2 : 0;
                case "cleaner_wrasse": return FaunaEventInfo.IsActive && FaunaEventInfo.ActiveId == "cleaning_station" ? 3 : 2;
                default: return 0;
            }
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            var p = Refs.Player;
            if (p != null && p.View != null) cam = p.View;
            if (cam == null) return;
            Vector3 cp = cam.transform.position;
            Vector3 cf = cam.transform.forward;
            float time = Time.time;
            used.Clear();

            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c.IsHallucination || !c.IsAlive) continue;
                var s = c.Species;
                Vector3 pos = c.Position;
                int nb = BuddiesFor(s, pos);
                if (nb <= 0) continue;
                Vector3 to = pos - cp;
                float d2 = to.sqrMagnitude;
                if (d2 > MaxDistance * MaxDistance) continue;
                if (d2 > 64f && Vector3.Dot(to, cf) < 0f) continue;   // behind the camera
                var kind = KindOf(s);
                if (kind == null) continue;
                if (!used.Contains(kind)) { used.Add(kind); for (int k = 0; k < Poses; k++) kind.n[k] = 0; }

                var t = c.Transform;
                if (t == null) continue;
                Quaternion rot = t.rotation;
                float scale = t.lossyScale.x;
                int seed = t.GetHashCode();
                float spread = Mathf.Max(0.25f, scale * 2.2f);
                for (int b = 0; b < nb; b++)
                {
                    float h1 = Hash(seed, b, 1), h2 = Hash(seed, b, 2), h3 = Hash(seed, b, 3), h4 = Hash(seed, b, 4);
                    Vector3 o = new Vector3(h1 * 2f - 1f, (h2 * 2f - 1f) * 0.45f, h3 * 2f - 1f) * spread;
                    o += new Vector3(Mathf.Sin(time * 0.7f + h4 * 9f), Mathf.Sin(time * 0.5f + h1 * 7f) * 0.5f, Mathf.Cos(time * 0.6f + h2 * 8f)) * (spread * 0.12f);
                    Quaternion r = rot * Quaternion.Euler(0f, (h4 - 0.5f) * 22f, 0f);
                    float sc = scale * (0.85f + 0.25f * h2);
                    int pose = ((int)((time * 2.2f + h3) * Poses)) & (Poses - 1);
                    int k = kind.n[pose];
                    if (k >= Batch) continue;
                    if (kind.m[pose] == null) kind.m[pose] = new Matrix4x4[Batch];
                    kind.m[pose][k] = Matrix4x4.TRS(pos + rot * o, r, new Vector3(sc, sc, sc));
                    kind.n[pose] = k + 1;
                }
            }
            if (used.Count == 0) return;
            if (!rpInit)
            {
                rpInit = true;
                rp = new RenderParams(used[0].mats[0]) { layer = 0, shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = new Bounds(Vector3.zero, Vector3.one * 100000f) };
            }
            for (int u = 0; u < used.Count; u++)
            {
                var kind = used[u];
                for (int pose = 0; pose < Poses; pose++)
                {
                    int count = kind.n[pose];
                    if (count == 0) continue;
                    var mesh = kind.poses[pose];
                    for (int sub = 0; sub < kind.subs; sub++)
                    {
                        var mat = kind.mats[sub];
                        if (mat == null || !mat.enableInstancing) continue;
                        rp.material = mat;
                        Graphics.RenderMeshInstanced(rp, mesh, sub, kind.m[pose], count);
                    }
                }
            }
        }


        static float Hash(int a, int b, int c)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u ^ (uint)b * 0x85EBCA77u ^ (uint)c * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        Kind KindOf(SpeciesDef s)
        {
            if (kinds.TryGetValue(s, out var k)) return k.failed ? null : k;
            k = new Kind { s = s };
            kinds[s] = k;
            try
            {
                var kit = CreatureFactory.Kit(s);
                k.poses = new Mesh[Poses];
                for (int p = 0; p < Poses; p++) k.poses[p] = CreatureMeshPose.Bake(kit, p / (float)Poses, "SB_Swarm_" + s.id + "_" + p);
                k.mats = kit.Materials;
                k.subs = Mathf.Min(kit.Materials.Length, k.poses[0].subMeshCount);
                for (int i = 0; i < k.mats.Length; i++) if (k.mats[i] != null && k.mats[i].renderQueue < 3000) k.mats[i].enableInstancing = true;
            }
            catch (System.Exception e)
            {
                k.failed = true;
                Debug.LogWarning("[Fauna] swarm buddies for " + s.id + " unavailable: " + e.Message);
                return null;
            }
            return k;
        }

        void OnDestroy()
        {
            foreach (var kv in kinds)
                if (kv.Value.poses != null)
                    for (int i = 0; i < kv.Value.poses.Length; i++) if (kv.Value.poses[i] != null) Destroy(kv.Value.poses[i]);
            kinds.Clear();
        }
    }
}
