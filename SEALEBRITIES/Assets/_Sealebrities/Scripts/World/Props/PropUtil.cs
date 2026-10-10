using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Coroutine helpers: frame-budgeted yielding and exception isolation so one failing prop group never blocks loading.
    public static class PropRun
    {
        static readonly System.Diagnostics.Stopwatch Watch = new System.Diagnostics.Stopwatch();
        static int frame = -1;
        public static float BudgetMs = 12f;

        /// True when the current frame's work budget is used up: the caller should "yield return null".
        public static bool Due()
        {
            int f = Time.frameCount;
            if (f != frame || !Watch.IsRunning)
            {
                frame = f;
                Watch.Reset();
                Watch.Start();
                return false;
            }
            return Watch.Elapsed.TotalMilliseconds > BudgetMs;
        }

        /// Runs a (possibly nested) coroutine and logs instead of propagating exceptions.
        public static IEnumerator Safe(IEnumerator inner, string label)
        {
            while (true)
            {
                object cur;
                try
                {
                    if (!inner.MoveNext()) yield break;
                    cur = inner.Current;
                }
                catch (Exception e)
                {
                    Debug.LogError("Sealebrities props: '" + label + "' failed: " + e);
                    yield break;
                }
                if (cur is IEnumerator nested) yield return Safe(nested, label);
                else yield return cur;
            }
        }
    }

    /// Ground height access with a fallback when the terrain does not exist (keeps the module usable in isolation).
    public static class PropGround
    {
        public static float H(float x, float z)
        {
            if (WorldInfo.Terrain != null) return WorldInfo.GroundHeight(new Vector3(x, 0f, z));
            float r = Mathf.Sqrt(x * x + z * z);
            if (r < WorldLayout.IslandRadius) return Mathf.Lerp(9f, 1.2f, r / WorldLayout.IslandRadius);
            var s = WorldInfo.SiteAt(new Vector3(x, 0f, z));
            if (s != null) return -(s.minDepth + s.maxDepth) * 0.5f;
            return -Mathf.Min(30f, 2f + (r - WorldLayout.IslandRadius) * 0.15f);
        }

        public static float H(Vector3 p) => H(p.x, p.z);

        public static Vector3 At(float x, float z) => new Vector3(x, H(x, z), z);

        public static Vector3 Normal(float x, float z, float e = 1f)
        {
            float hl = H(x - e, z), hr = H(x + e, z), hd = H(x, z - e), hu = H(x, z + e);
            return new Vector3(hl - hr, 2f * e, hd - hu).normalized;
        }

        /// Max absolute height difference to 4 neighbours at distance e, divided by e (rise over run).
        public static float Slope(float x, float z, float e = 1.5f)
        {
            float h = H(x, z);
            float m = Mathf.Abs(H(x - e, z) - h);
            m = Mathf.Max(m, Mathf.Abs(H(x + e, z) - h));
            m = Mathf.Max(m, Mathf.Abs(H(x, z - e) - h));
            m = Mathf.Max(m, Mathf.Abs(H(x, z + e) - h));
            return m / e;
        }

        public static void MinMax(float cx, float cz, float radius, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            for (int r = 0; r <= 3; r++)
            {
                float rr = radius * r / 3f;
                int n = r == 0 ? 1 : 8 + r * 4;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    float h = H(cx + Mathf.Cos(a) * rr, cz + Mathf.Sin(a) * rr);
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }
        }

        public static void MinMaxRect(Vector3 center, Quaternion rot, float halfX, float halfZ, float step, out float min, out float max)
        {
            min = float.MaxValue; max = float.MinValue;
            for (float x = -halfX; x <= halfX + 0.01f; x += step)
            {
                for (float z = -halfZ; z <= halfZ + 0.01f; z += step)
                {
                    Vector3 p = center + rot * new Vector3(x, 0f, z);
                    float h = H(p.x, p.z);
                    if (h < min) min = h;
                    if (h > max) max = h;
                }
            }
        }

        public static float SeaDepth(float x, float z) => Waves.SeaLevel - H(x, z);
    }

    /// Spatial hash of blocked circles (used for keeping vegetation off structures/paths and for spacing).
    public sealed class PropExclusion
    {
        readonly float cell;
        readonly Dictionary<long, List<Vector3>> cells = new Dictionary<long, List<Vector3>>();

        public PropExclusion(float cellSize = 16f) { cell = cellSize; }

        static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        public void Add(float x, float z, float r)
        {
            int x0 = Mathf.FloorToInt((x - r) / cell), x1 = Mathf.FloorToInt((x + r) / cell);
            int z0 = Mathf.FloorToInt((z - r) / cell), z1 = Mathf.FloorToInt((z + r) / cell);
            var c = new Vector3(x, z, r);
            for (int ix = x0; ix <= x1; ix++)
            {
                for (int iz = z0; iz <= z1; iz++)
                {
                    long k = Key(ix, iz);
                    if (!cells.TryGetValue(k, out var list)) { list = new List<Vector3>(8); cells[k] = list; }
                    list.Add(c);
                }
            }
        }

        public void AddLine(Vector3 a, Vector3 b, float r)
        {
            float len = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            int n = Mathf.Max(1, Mathf.CeilToInt(len / Mathf.Max(0.5f, r * 0.6f)));
            for (int i = 0; i <= n; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, i / (float)n);
                Add(p.x, p.z, r);
            }
        }

        public bool Blocked(float x, float z, float pad = 0f)
        {
            long k = Key(Mathf.FloorToInt(x / cell), Mathf.FloorToInt(z / cell));
            if (!cells.TryGetValue(k, out var list)) return false;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                float dx = c.x - x, dz = c.y - z, rr = c.z + pad;
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }
    }

    /// Collider helpers. Colliders live on their own GameObjects (no renderers), parented under a structure root.
    public static class PropCol
    {
        public static BoxCollider Box(Transform parent, Vector3 center, Vector3 size, Quaternion rot, string name = "Col")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rot);
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            return c;
        }

        public static MeshCollider Mesh(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, string name = "MeshCol")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            var c = go.AddComponent<MeshCollider>();
            c.sharedMesh = mesh;
            return c;
        }

        public static CapsuleCollider Capsule(Transform parent, Vector3 center, float radius, float height, Quaternion rot, string name = "Col")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rot);
            var c = go.AddComponent<CapsuleCollider>();
            c.radius = radius;
            c.height = height;
            c.direction = 1;
            return c;
        }

        /// Adds an upright capsule as an extra component on a shared, unrotated collider holder at the world origin.
        public static void UprightCapsule(GameObject holder, Vector3 worldBase, float radius, float height)
        {
            var c = holder.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = radius;
            c.height = height;
            c.center = worldBase - holder.transform.position + Vector3.up * (height * 0.5f);
        }
    }

    public static class PropUtil
    {
        /// Child transform that sits at the world origin with identity rotation (vertex data of combined meshes is in world space).
        public static Transform WorldRoot(string name, Transform parent)
        {
            var go = new GameObject(name);
            var t = go.transform;
            if (parent != null) t.SetParent(parent, false);
            t.position = Vector3.zero;
            t.rotation = Quaternion.identity;
            return t;
        }

        public static float Range(System.Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);
        public static float R01(System.Random r) => (float)r.NextDouble();
        public static bool Chance(System.Random r, float p) => r.NextDouble() < p;

        public static Vector2 InDisc(System.Random r, float radius)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f;
            float d = Mathf.Sqrt((float)r.NextDouble()) * radius;
            return new Vector2(Mathf.Cos(a) * d, Mathf.Sin(a) * d);
        }

        public static T Pick<T>(System.Random r, IList<T> list) => list[r.Next(list.Count)];

        public static Quaternion Yaw(float deg) => Quaternion.Euler(0f, deg, 0f);

        /// Rotation that tilts "up" partially toward a ground normal, then yaws.
        public static Quaternion OnGround(Vector3 normal, float follow, float yawDeg)
        {
            Vector3 up = Vector3.Slerp(Vector3.up, normal, follow);
            return Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yawDeg, 0f);
        }

        public static Light AddPointLight(Transform parent, Vector3 pos, Color color, float range, float intensity, string name = "Light")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.renderMode = LightRenderMode.Auto;
            return l;
        }

        /// Plain renderer object for a mesh in local space of its parent.
        public static MeshRenderer MeshObject(Transform parent, string name, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }
    }
}
