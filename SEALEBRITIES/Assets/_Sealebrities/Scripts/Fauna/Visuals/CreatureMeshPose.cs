using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// CPU-skinned static swim poses of a species kit (the swarm buddies of CreatureSwarm). The pose follows the same
    /// travelling sine wave as FaunaSwimAnimator (spine yaw, tail fin) at a cruising amplitude; phase01 picks the moment
    /// of the cycle. Pure math on the managed bake + one Mesh per pose (main thread, built once per species).
    internal static class CreatureMeshPose
    {
        public static Mesh Bake(CreatureMeshKit kit, float phase01, string name)
        {
            var flat = kit.Bake.Flat;
            var bones = kit.Bones;
            int nb = bones.Length;
            // local yaw per bone (degrees): spine wave + tail fin
            var localYaw = new float[nb];
            int ns = kit.Spine.Length;
            const float amp = 13f, k = 2.4f;
            float phase = phase01 * Mathf.PI * 2f;
            float prev = 0f;
            for (int i = 0; i < ns; i++)
            {
                float t = ns > 1 ? i / (float)(ns - 1) : 1f;
                float w = Mathf.Lerp(0.08f, 1f, t * t);
                float a = amp * w * Mathf.Sin(phase - k * t);
                localYaw[kit.Spine[i]] = a - prev;   // nested chain: local = world − parent
                prev = a;
            }
            if (kit.TailFin >= 0) localYaw[kit.TailFin] += amp * 0.9f * Mathf.Sin(phase - k - 0.5f);

            // world rotation (yaw only, rest rotations are identity) and position of every bone
            var worldYaw = new float[nb];
            var worldPos = new Vector3[nb];
            for (int b = 0; b < nb; b++)
            {
                var spec = bones[b];
                if (spec.Parent < 0)
                {
                    worldYaw[b] = localYaw[b];
                    worldPos[b] = spec.Pos;
                }
                else
                {
                    int p = spec.Parent;   // parents always come first in the bone list
                    worldYaw[b] = worldYaw[p] + localYaw[b];
                    worldPos[b] = worldPos[p] + Yaw(spec.Pos - bones[p].Pos, worldYaw[p]);
                }
            }

            int n = flat.Count;
            var verts = new Vector3[n];
            var norms = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var bw = flat.Weights[i];
                Vector3 v = flat.Verts[i], nn = flat.Normals[i];
                Vector3 pv = Vector3.zero, pn = Vector3.zero;
                Add(ref pv, ref pn, v, nn, bw.boneIndex0, bw.weight0, bones, worldYaw, worldPos);
                Add(ref pv, ref pn, v, nn, bw.boneIndex1, bw.weight1, bones, worldYaw, worldPos);
                Add(ref pv, ref pn, v, nn, bw.boneIndex2, bw.weight2, bones, worldYaw, worldPos);
                Add(ref pv, ref pn, v, nn, bw.boneIndex3, bw.weight3, bones, worldYaw, worldPos);
                verts[i] = pv;
                norms[i] = pn.sqrMagnitude > 1e-8f ? pn.normalized : nn;
            }

            var mesh = new Mesh { name = name };
            if (n > 65000) mesh.indexFormat = IndexFormat32();
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = flat.Uvs;
            var slots = kit.SubmeshSlots;
            mesh.subMeshCount = slots.Length;
            for (int i = 0; i < slots.Length; i++) mesh.SetTriangles(flat.Tris[slots[i]], i, false);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        static UnityEngine.Rendering.IndexFormat IndexFormat32() => UnityEngine.Rendering.IndexFormat.UInt32;

        static void Add(ref Vector3 pv, ref Vector3 pn, Vector3 v, Vector3 n, int bone, float w, CreatureMeshBoneSpec[] bones, float[] yaw, Vector3[] pos)
        {
            if (w <= 0f || bone < 0 || bone >= bones.Length) return;
            pv += (pos[bone] + Yaw(v - bones[bone].Pos, yaw[bone])) * w;
            pn += Yaw(n, yaw[bone]) * w;
        }

        static Vector3 Yaw(Vector3 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector3(v.x * c + v.z * s, v.y, -v.x * s + v.z * c);
        }
    }
}
