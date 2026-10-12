using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „Durch feste Objekte darf man nirgends laufen“): cheap static colliders for solid props that had
    /// none – corals and reef rocks, small rocks, palm trunks along their lean, buoys, wreck parts, sandbars. Only
    /// primitives or small convex rock hulls; nothing visual changes, every collider is derived from the same seeded data
    /// as the mesh (identical on every co-op machine).
    ///
    /// Boat rule: the player's boat (Default layer, keel ≈ 0.7 m + waves) must never get stuck on a coral head in the
    /// lagoon / boat lanes. A water prop whose collider top lies shallower than <see cref="BoatDraft"/> below sea level goes
    /// on <see cref="BoatPassLayer"/>: the diver (layer 2, CharacterController) still collides with it, Default-layer
    /// bodies (the boat, NPC boats) pass. Deeper colliders stay on Default (the hull never reaches them).
    public static class PropSolid
    {
        /// Unnamed project layer: solid for the player, ignored by the Default layer (boats).
        public const int BoatPassLayer = 28;
        /// Collider tops shallower than this below sea level are boat-pass (m).
        public const float BoatDraft = 1.6f;
        /// Colliders added by these helpers since the world was built (playtest log).
        public static int Count;

        const int PerHolder = 48;
        static Transform holderParent;
        static GameObject deep, shallow;
        static int deepN, shallowN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Count = 0;
            holderParent = null;
            deep = null;
            shallow = null;
            deepN = shallowN = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitLayers() => EnsureLayers();

        /// The boat (Default) ignores the boat-pass layer (set once; the setter resets trigger states, so never per shape).
        public static void EnsureLayers()
        {
            if (!Physics.GetIgnoreLayerCollision(0, BoatPassLayer)) Physics.IgnoreLayerCollision(0, BoatPassLayer, true);
        }

        /// Layer of a water prop whose collider top is at topY.
        public static int WaterLayer(float topY) => topY > Waves.SeaLevel - BoatDraft ? BoatPassLayer : 0;

        // ------------------------------------------------------------------ shared holders (many shapes, few objects)
        /// A shared, unrotated holder at the world origin under `parent` (one per layer, ≤ 48 shapes each).
        static GameObject Holder(Transform parent, bool boatPass)
        {
            if (parent != holderParent) { holderParent = parent; deep = null; shallow = null; deepN = shallowN = 0; }
            if (boatPass)
            {
                if (shallow == null || shallowN >= PerHolder)
                {
                    EnsureLayers();
                    shallow = PropUtil.WorldRoot("SolidBoatPass", parent).gameObject;
                    shallow.layer = BoatPassLayer;
                    shallowN = 0;
                }
                shallowN++;
                return shallow;
            }
            if (deep == null || deepN >= PerHolder)
            {
                deep = PropUtil.WorldRoot("Solid", parent).gameObject;
                deepN = 0;
            }
            deepN++;
            return deep;
        }

        /// Sphere (world centre) on a shared holder.
        public static void Sphere(Transform parent, Vector3 center, float r, bool water)
        {
            if (parent == null || !(r > 0.01f)) return;
            var h = Holder(parent, water && WaterLayer(center.y + r) == BoatPassLayer);
            var c = h.AddComponent<SphereCollider>();
            c.radius = r;
            c.center = center - h.transform.position;
            Count++;
        }

        /// Upright capsule from `bottom` (lowest point) with total height h, on a shared holder.
        public static void Upright(Transform parent, Vector3 bottom, float r, float height, bool water)
        {
            if (parent == null || !(r > 0.01f)) return;
            height = Mathf.Max(height, 2f * r);
            var h = Holder(parent, water && WaterLayer(bottom.y + height) == BoatPassLayer);
            var c = h.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = r;
            c.height = height;
            c.center = bottom - h.transform.position + Vector3.up * (height * 0.5f);
            Count++;
        }

        /// Axis-aligned box (world centre) on a shared holder.
        public static void AABox(Transform parent, Vector3 center, Vector3 size, bool water)
        {
            if (parent == null) return;
            var h = Holder(parent, water && WaterLayer(center.y + size.y * 0.5f) == BoatPassLayer);
            var c = h.AddComponent<BoxCollider>();
            c.size = size;
            c.center = center - h.transform.position;
            Count++;
        }

        /// A rounded lump standing on `basePos`: horizontal radius r, height h above the base. Flat lumps (h ≤ 2r) are a
        /// sphere whose top touches h (the rest is buried), tall ones an upright capsule – divers slide along, never snag.
        public static void Dome(Transform parent, Vector3 basePos, float r, float h, bool water)
        {
            if (!(h > 0.05f) || !(r > 0.05f)) return;
            if (h <= 2f * r) Sphere(parent, basePos + Vector3.up * (h - r), r, water);
            else Upright(parent, basePos - Vector3.up * 0.05f, r, h + 0.05f, water);
        }

        // ------------------------------------------------------------------ own objects (rotated shapes)
        static GameObject Obj(Transform parent, string name, Vector3 pos, Quaternion rot, int layer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            if (layer != 0) go.layer = layer;
            if (layer == BoatPassLayer) EnsureLayers();
            Count++;
            return go;
        }

        /// Capsule from world point a to b (radius r).
        public static CapsuleCollider Segment(Transform parent, Vector3 a, Vector3 b, float r, string name, int layer = 0)
        {
            if (parent == null) return null;
            Vector3 d = b - a;
            float len = d.magnitude;
            Quaternion q = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            var c = Obj(parent, name, (a + b) * 0.5f, q, layer).AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = r;
            c.height = len + 2f * r;
            return c;
        }

        /// Rotated box (world centre).
        public static BoxCollider Box(Transform parent, Vector3 center, Vector3 size, Quaternion rot, string name, int layer = 0)
        {
            if (parent == null) return null;
            var c = Obj(parent, name, center, rot, layer).AddComponent<BoxCollider>();
            c.size = size;
            return c;
        }

        /// Convex hull of the same seeded rock PropRocks.Build / BuildSplit draws with these arguments.
        public static MeshCollider Rock(Transform parent, Vector3 pos, Vector3 scale, Quaternion rot, int seed, int detail, float rough, bool water, string name = "RockCol")
        {
            if (parent == null) return null;
            float top = pos.y + Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            var go = Obj(parent, name, pos, rot, water ? WaterLayer(top) : 0);
            var mc = go.AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = PropRocks.ColliderMesh(scale, seed, detail, rough);
            return mc;
        }

        /// Collider for a mesh that already exists (e.g. a boulder's render mesh), convex.
        public static MeshCollider ConvexMesh(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, int layer, string name)
        {
            if (parent == null || mesh == null) return null;
            var mc = Obj(parent, name, pos, rot, layer).AddComponent<MeshCollider>();
            mc.convex = true;
            mc.sharedMesh = mesh;
            return mc;
        }

        // ------------------------------------------------------------------ local-space helpers (moving models)
        /// Capsule between two LOCAL points of `parent` (follows the parent when it moves).
        public static CapsuleCollider LocalSegment(Transform parent, Vector3 a, Vector3 b, float r, string name)
        {
            if (parent == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Vector3 d = b - a;
            float len = d.magnitude;
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, d / len) : Quaternion.identity;
            var c = go.AddComponent<CapsuleCollider>();
            c.direction = 1;
            c.radius = r;
            c.height = len + 2f * r;
            Count++;
            return c;
        }

        /// Box in LOCAL space of `parent`.
        public static BoxCollider LocalBox(Transform parent, Vector3 center, Vector3 size, Quaternion localRot, string name)
        {
            if (parent == null) return null;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = localRot;
            var c = go.AddComponent<BoxCollider>();
            c.size = size;
            Count++;
            return c;
        }

        /// Makes a floating (script-animated) prop root a kinematic body so its colliders move cheaply with it.
        public static void Kinematic(GameObject go)
        {
            if (go == null || go.GetComponent<Rigidbody>() != null) return;
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        // ------------------------------------------------------------------ prop recipes
        /// Corals by shape: a rounded lump for brains / branching / staghorn / pillars / sponges (k = how much of the
        /// footprint radius is solid – bushy tips may be brushed), skipped below 0.5 m footprint or 0.3 m height.
        public static void CoralLump(Transform cols, Vector3 basePos, float r, float h, float k, bool water = true)
        {
            if (cols == null || 2f * r < 0.5f && h < 0.5f || h < 0.3f) return;
            Dome(cols, basePos, Mathf.Max(0.12f, r * k), h, water);
        }

        /// Table coral: plate radius r at height h – a squat box under the plate (nobody fits under it anyway).
        public static void CoralTable(Transform cols, Vector3 basePos, float r, float h, bool water = true)
        {
            if (cols == null || 2f * r < 0.5f || h < 0.25f) return;
            float w = r * 1.25f;
            AABox(cols, basePos + Vector3.up * (h * 0.5f), new Vector3(w, h, w), water);
        }

        /// Big sea fan: a thin plate in the fan's plane (local XY of rot), only for fans ≥ 1.1 m.
        public static void CoralFan(Transform cols, Vector3 basePos, Quaternion rot, float halfWidth, float height, bool water = true)
        {
            if (cols == null || height < 1.1f) return;
            Vector3 c = basePos + rot * new Vector3(0f, height * 0.55f, 0f);
            Box(cols, c, new Vector3(halfWidth * 1.5f, height * 0.8f, 0.12f), rot, "SeaFan", water ? WaterLayer(basePos.y + height) : 0);
        }

        /// Palm trunk of PropPlants.Palm(seed) placed with TRS(basePos, yaw, scale): two capsules ALONG the leaning
        /// trunk curve (base → ~2.9 m → ~5.5 m), radius = trunk radius + 0.05.
        public static void PalmTrunk(Transform cols, int seed, Vector3 basePos, float yaw, float scale, string name = "PalmTrunk")
        {
            if (cols == null) return;
            // same draws as PropPlants.Palm: H, lean, wig
            var rng = new System.Random(seed);
            float H = PropUtil.Range(rng, 6f, 11f);
            float lean = PropUtil.Range(rng, 0.18f, 0.35f) * H;
            float wig = PropUtil.Range(rng, -0.07f, 0.07f) * H;
            Vector3 Curve(float t) => new Vector3(lean * (2f * t - t * t) - 0.06f * lean * Mathf.Sin(t * Mathf.PI * 2f), H * t, wig * Mathf.Sin(t * Mathf.PI));
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 W(float t) => basePos + q * (Curve(t) * scale);
            float hs = Mathf.Max(1f, H * scale);
            float t1 = Mathf.Min(1f, 2.9f / hs), t2 = Mathf.Min(1f, 5.5f / hs);
            float r0 = 0.24f * scale + 0.05f, r1 = Mathf.Lerp(0.24f, 0.15f, t1) * scale + 0.05f;
            Segment(cols, W(0f) + Vector3.up * r0, W(t1), r0, name);
            if (t2 > t1 + 0.02f) Segment(cols, W(t1), W(t2), r1, name);
        }

        /// PropBeach.RowingBoat's hull (local: 3.6 m along z, keel at y 0, gunwale ≈ 0.6 m) as one box.
        public static void RowingBoat(Transform cols, Matrix4x4 trs, bool water)
        {
            if (cols == null) return;
            Vector3 c = trs.MultiplyPoint3x4(new Vector3(0f, 0.3f, 0f));
            float s = trs.lossyScale.x;
            Quaternion rot = trs.rotation;
            Box(cols, c, new Vector3(1.25f, 0.6f, 3.5f) * s, rot, "RowingBoatCol", water ? WaterLayer(c.y + 0.4f * s) : 0);
        }

        /// The convex hull of PropKit.Sandbar(center, len, wid, yaw, seed) – the same seeded rim, so one can stand on it.
        public static void Sandbar(Transform cols, Vector3 center, float len, float wid, float yaw, int seed, int layer)
        {
            if (cols == null) return;
            const int A = 12;
            var rng = new System.Random(seed);
            Quaternion r = Quaternion.Euler(0f, yaw, 0f);
            var v = new Vector3[2 * A + 1];
            v[2 * A] = Vector3.up * 0.18f;
            for (int i = 0; i < A; i++)
            {
                float a = i * Mathf.PI * 2f / A;
                float j = PropUtil.Range(rng, 0.85f, 1.1f);
                v[i] = r * new Vector3(Mathf.Cos(a) * len * 0.5f * j, 0.08f, Mathf.Sin(a) * wid * 0.5f * j);
                v[A + i] = r * new Vector3(Mathf.Cos(a) * len * 0.75f * j, -1.2f, Mathf.Sin(a) * wid * 0.85f * j);
            }
            var t = new int[(A + A * 2 + A) * 3];
            int k = 0;
            for (int i = 0; i < A; i++)
            {
                int n = (i + 1) % A;
                t[k++] = 2 * A; t[k++] = n; t[k++] = i;                 // top fan
                t[k++] = i; t[k++] = n; t[k++] = A + n;                 // side
                t[k++] = i; t[k++] = A + n; t[k++] = A + i;
                t[k++] = A + i; t[k++] = A + n; t[k++] = 2 * A;         // closing (hull only uses the points)
            }
            var m = new Mesh { name = "SandbarCol" };
            m.vertices = v;
            m.triangles = t;
            m.RecalculateBounds();
            ConvexMesh(cols, m, center, Quaternion.identity, layer, "SandbarCol");
        }
    }
}
