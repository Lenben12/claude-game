using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Builds one template model per species lazily (CreatureFactory.Build, kept under an inactive pool root)
    /// and hands out pooled clones (deactivated / reactivated per species).
    public sealed class FaunaPool
    {
        readonly Transform templateRoot;
        readonly Transform liveRoot;
        readonly Dictionary<SpeciesDef, GameObject> templates = new Dictionary<SpeciesDef, GameObject>();
        readonly Dictionary<SpeciesDef, Stack<FaunaCreature>> free = new Dictionary<SpeciesDef, Stack<FaunaCreature>>();

        public Transform LiveRoot => liveRoot;

        public FaunaPool(Transform parent)
        {
            var t = new GameObject("FaunaTemplates");
            t.SetActive(false);
            t.transform.SetParent(parent, false);
            templateRoot = t.transform;

            var l = new GameObject("FaunaLive");
            l.transform.SetParent(parent, false);
            liveRoot = l.transform;
        }

        public bool HasTemplate(SpeciesDef s) => s != null && templates.TryGetValue(s, out var t) && t != null;

        public GameObject Template(SpeciesDef s)
        {
            if (templates.TryGetValue(s, out var tpl) && tpl != null) return tpl;

            GameObject go = null;
            try { go = CreatureFactory.Build(s); }
            catch (Exception e) { Debug.LogWarning("Sealebrities/Fauna: CreatureFactory.Build failed for " + s.id + ": " + e.Message); }
            if (go == null) go = FaunaFallbackModel.Build(s);

            go.name = "Tpl_" + s.id;
            Transform tt = go.transform;
            tt.SetParent(templateRoot, false);
            tt.localPosition = Vector3.zero;
            tt.localRotation = Quaternion.identity;
            tt.localScale = Vector3.one;
            StripPhysics(go);

            var rig = go.GetComponent<CreatureRig>();
            if (rig == null) rig = go.AddComponent<CreatureRig>();
            if (rig.spine == null) rig.spine = new Transform[0];
            if (rig.pectoralFins == null) rig.pectoralFins = new Transform[0];
            if (rig.flippers == null) rig.flippers = new Transform[0];
            if (rig.tentacles == null) rig.tentacles = new Transform[0];
            if (rig.renderers == null || rig.renderers.Length == 0) rig.renderers = go.GetComponentsInChildren<Renderer>(true);

            templates[s] = go;
            return go;
        }

        static void StripPhysics(GameObject go)
        {
            var bodies = go.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < bodies.Length; i++) Object.DestroyImmediate(bodies[i]);
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) Object.DestroyImmediate(cols[i]);
        }

        /// Returns an inactive creature for the species: pooled when available, otherwise a fresh clone.
        /// Hallucinations always get a fresh clone (their materials are swapped for fading copies).
        public FaunaCreature Take(SpeciesDef s, bool fresh)
        {
            if (!fresh && free.TryGetValue(s, out var st))
            {
                while (st.Count > 0)
                {
                    var pooled = st.Pop();
                    if (pooled != null) return pooled;
                }
            }

            var tpl = Template(s);
            var inst = Object.Instantiate(tpl, templateRoot);   // inactive in hierarchy: no Awake yet
            inst.name = fresh ? s.id + "_hallucination" : s.id;
            inst.SetActive(false);
            inst.transform.SetParent(liveRoot, false);
            var c = inst.AddComponent<FaunaCreature>();
            c.Setup(s);
            return c;
        }

        /// A fresh, inactive copy of the species model WITHOUT creature logic (co-op proxies of the host's animals).
        /// Shares the lazily built template with the real creatures, so a prewarmed site costs nothing extra.
        public GameObject CloneModel(SpeciesDef s, Transform parent)
        {
            if (s == null) return null;
            var tpl = Template(s);
            var inst = Object.Instantiate(tpl, templateRoot);   // inactive in hierarchy: no Awake yet
            inst.name = s.id + "_net";
            inst.SetActive(false);
            inst.transform.SetParent(parent != null ? parent : liveRoot, false);
            return inst;
        }

        public void Return(FaunaCreature c)
        {
            if (c == null || c.species == null) return;
            if (!free.TryGetValue(c.species, out var st))
            {
                st = new Stack<FaunaCreature>(16);
                free[c.species] = st;
            }
            st.Push(c);
        }

        public int FreeCount(SpeciesDef s) => s != null && free.TryGetValue(s, out var st) ? st.Count : 0;
    }

    /// Emergency stand-in model used only if the visual factory fails for a species.
    static class FaunaFallbackModel
    {
        public static GameObject Build(SpeciesDef s)
        {
            var root = new GameObject(s.id);
            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            var col = body.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            body.transform.SetParent(root.transform, false);
            float w = Mathf.Clamp(s.bodyWidth, 0.05f, 1.6f);
            float h = Mathf.Clamp(s.bodyHeight, 0.05f, 1f);
            body.transform.localScale = new Vector3(w, h, s.plan == BodyPlan.Ray ? 0.7f : 1f);
            var r = body.GetComponent<Renderer>();
            r.sharedMaterial = LowPoly.Mat(s.colorA);

            var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tail.name = "Tail";
            col = tail.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            var tailPivot = new GameObject("TailPivot").transform;
            tailPivot.SetParent(root.transform, false);
            tailPivot.localPosition = new Vector3(0f, 0f, -0.45f);
            tail.transform.SetParent(tailPivot, false);
            tail.transform.localPosition = new Vector3(0f, 0f, -0.08f);
            tail.transform.localScale = new Vector3(0.02f, h * 0.8f, 0.16f);
            var tr = tail.GetComponent<Renderer>();
            tr.sharedMaterial = LowPoly.Mat(s.colorC);

            var rig = root.AddComponent<CreatureRig>();
            rig.plan = s.plan;
            rig.tailFin = tailPivot;
            rig.renderers = new[] { r, tr };
            rig.bodyLength = 1f;
            return root;
        }
    }
}
