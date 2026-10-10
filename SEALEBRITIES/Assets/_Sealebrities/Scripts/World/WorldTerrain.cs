using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace Sealebrities
{
    /// Turns WorldTerrainData into scene objects on the main thread: one GameObject per 128 m chunk with a flat-shaded
    /// mesh (one submesh per color class, shared LowPoly materials) and a welded MeshCollider on the Default layer.
    public static class WorldTerrain
    {
        const float FrameBudgetMs = 9f;
        static ushort[] identity;
        static Material[] classMats;

        public static Material MaterialFor(TerrainClass c)
        {
            if (classMats == null || classMats.Length == 0 || classMats[0] == null)
            {
                classMats = new[]
                {
                    LowPoly.Mat(LowPoly.Sand),
                    LowPoly.Mat(Color.Lerp(LowPoly.Sand, LowPoly.WetSand, 0.4f)),
                    MaterialLibrary.Opaque(LowPoly.WetSand, 0.22f, 0f),
                    LowPoly.Mat(LowPoly.Grass),
                    LowPoly.Mat(LowPoly.GrassDark),
                    LowPoly.Mat(LowPoly.Soil),
                    LowPoly.Mat(LowPoly.Rock),
                    LowPoly.Mat(LowPoly.RockDark),
                    LowPoly.Mat(LowPoly.Seabed),
                    LowPoly.Mat(Color.Lerp(LowPoly.Seabed, LowPoly.WetSand, 0.5f)),
                    LowPoly.Mat(LowPoly.DeepRock),
                };
            }
            return classMats[(int)c];
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { classMats = null; }

        /// Builds every chunk object under a "Terrain" root, yielding whenever the frame budget is used up.
        /// progress receives 0..1.
        public static IEnumerator CreateObjects(Transform parent, WorldTerrainData data, Action<float> progress)
        {
            var root = new GameObject("Terrain").transform;
            root.SetParent(parent, false);
            root.position = Vector3.zero;

            int maxVerts = 2 * WorldTerrainData.FineCells * WorldTerrainData.FineCells * 3;
            if (identity == null || identity.Length < maxVerts)
            {
                identity = new ushort[maxVerts];
                for (int i = 0; i < maxVerts; i++) identity[i] = (ushort)i;
            }

            var watch = Stopwatch.StartNew();
            var mats = new List<Material>(11);
            int done = 0;
            foreach (var c in data.Chunks)
            {
                if (c != null)
                {
                    try { CreateChunk(root, c, mats); }
                    catch (Exception e)
                    {
                        Debug.LogError("Sealebrities: terrain chunk " + c.Cx + "/" + c.Cz + " failed.");
                        Debug.LogException(e);
                    }
                    data.ReleaseRenderData(c);
                }
                done++;
                if (watch.Elapsed.TotalMilliseconds > FrameBudgetMs)
                {
                    progress?.Invoke(done / (float)data.Chunks.Length);
                    yield return null;
                    watch.Restart();
                }
            }
            progress?.Invoke(1f);
        }

        static void CreateChunk(Transform root, WorldTerrainChunk c, List<Material> mats)
        {
            var go = new GameObject("Chunk_" + c.Cx + "_" + c.Cz);
            go.transform.SetParent(root, false);
            go.layer = 0;

            // ---- render mesh: unwelded flat-shaded triangles, already sorted by class -> one submesh per present class
            var verts = c.RenderVerts;
            int vc = verts.Length;
            var mesh = new Mesh { name = "SB_Terrain_" + c.Cx + "_" + c.Cz };
            mesh.indexFormat = vc > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(c.RenderNormals);
            int present = 0;
            for (int k = 0; k < c.ClassCount.Length; k++) if (c.ClassCount[k] > 0) present++;
            mesh.subMeshCount = present;
            mats.Clear();

            var center = new Vector3(c.X0 + WorldTerrainData.ChunkSize * 0.5f, (c.MinY + c.MaxY) * 0.5f, c.Z0 + WorldTerrainData.ChunkSize * 0.5f);
            var size = new Vector3(WorldTerrainData.ChunkSize + 2f, Mathf.Max(0.5f, c.MaxY - c.MinY) + 1f, WorldTerrainData.ChunkSize + 2f);
            var bounds = new Bounds(center, size);

            if (vc <= identity.Length)
            {
                mesh.SetIndexBufferParams(vc, IndexFormat.UInt16);
                mesh.SetIndexBufferData(identity, 0, 0, vc, MeshUpdateFlags.DontValidateIndices);
                int start = 0, sub = 0;
                for (int k = 0; k < c.ClassCount.Length; k++)
                {
                    int n = c.ClassCount[k] * 3;
                    if (n == 0) continue;
                    var d = new SubMeshDescriptor(start, n, MeshTopology.Triangles) { firstVertex = start, vertexCount = n, bounds = bounds };
                    mesh.SetSubMesh(sub++, d, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
                    mats.Add(MaterialFor((TerrainClass)k));
                    start += n;
                }
            }
            else
            {
                int start = 0, sub = 0;
                for (int k = 0; k < c.ClassCount.Length; k++)
                {
                    int n = c.ClassCount[k] * 3;
                    if (n == 0) continue;
                    var idx = new int[n];
                    for (int i = 0; i < n; i++) idx[i] = start + i;
                    mesh.SetIndices(idx, MeshTopology.Triangles, sub++, false);
                    mats.Add(MaterialFor((TerrainClass)k));
                    start += n;
                }
            }
            mesh.bounds = bounds;
            mesh.UploadMeshData(true);

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.allowOcclusionWhenDynamic = false;

            // ---- collider: the same surface, welded (fewer vertices to cook)
            int s = c.N + 1;
            var cv = new Vector3[s * s];
            for (int k = 0; k < cv.Length; k++) cv[k] = new Vector3(c.Vx[k], c.Vy[k], c.Vz[k]);
            var tris = new int[c.N * c.N * 6];
            int t = 0;
            for (int j = 0; j < c.N; j++)
                for (int i = 0; i < c.N; i++)
                {
                    int a = j * s + i, b = a + 1, cc = a + s, d = cc + 1;
                    if (c.DiagBC[j * c.N + i]) { tris[t++] = a; tris[t++] = cc; tris[t++] = b; tris[t++] = b; tris[t++] = cc; tris[t++] = d; }
                    else { tris[t++] = a; tris[t++] = cc; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = b; }
                }
            var col = new Mesh { name = "SB_TerrainCol_" + c.Cx + "_" + c.Cz };
            col.indexFormat = cv.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            col.SetVertices(cv);
            col.SetTriangles(tris, 0, true);
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = col;
        }
    }
}
