using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Q52 golden camera: the reward for a 100 % Sealog (all 55 species).
    ///  • The hand camera itself turns golden (FilmHandCamera rebuilds its model with FilmRigBuilder gold = true).
    ///  • A trophy for the hut shelf: the PROPS module places it – one call `FilmTrophy.Mount(shelfAnchor)` – and the
    ///    mount shows the golden camcorder on its little wooden plinth only once the Sealog is complete (also for co-op
    ///    clients after the host snapshot). Colours #E8C65A with emission 0.2 (Q52), world layer, casts shadows.
    ///  • The achievement "golden_camera" is unlocked by core (Progression.CheckUnlocks).
    public static class FilmTrophy
    {
        /// Q52 trophy colour.
        public static readonly Color Gold = new Color(0.910f, 0.776f, 0.353f);   // #E8C65A
        public const float Emission = 0.2f;

        /// Raised once per session when the Sealog becomes 100 % (or a loaded save already is).
        public static event Action Earned;

        static bool lastEarned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Earned = null; lastEarned = false; }

        /// The Sealog is 100 % (every species discovered).
        public static bool IsEarned
        {
            get
            {
                var gm = GameManager.I;
                return gm != null && gm.InGame && gm.State != null && Journal.IsComplete;
            }
        }

        /// Re-evaluates the state (hand camera / mounts call it on Sealog changes) and raises Earned on the transition.
        public static bool Refresh()
        {
            bool now = IsEarned;
            if (now && !lastEarned) Earned?.Invoke();
            lastEarned = now;
            return now;
        }

        internal static void ResetSession() => lastEarned = false;

        /// Places the trophy on a shelf anchor (props). The trophy's base sits at the anchor's origin, its lens points
        /// along the anchor's +Z. Returns the mount (hidden until the Sealog is complete). Safe to call more than once.
        public static FilmTrophyMount Mount(Transform anchor)
        {
            if (anchor == null) return null;
            var existing = anchor.GetComponentInChildren<FilmTrophyMount>(true);
            if (existing != null) return existing;
            var go = new GameObject("GoldenCameraMount");
            go.transform.SetParent(anchor, false);
            return go.AddComponent<FilmTrophyMount>();
        }

        /// Builds the trophy model (≈ 0.24 m tall: golden camcorder on a wooden plinth with a plaque) at the origin of
        /// 'parent'. Static, world layer, merged into one mesh per material. The returned object owns its meshes.
        public static GameObject Build(Transform parent)
        {
            var root = new GameObject("GoldenCameraTrophy");
            if (parent != null) root.transform.SetParent(parent, false);
            var src = new GameObject("Parts").transform;
            src.SetParent(root.transform, false);

            var gold = FilmMaterials.TrophyGold(Gold, Emission);
            var deep = FilmMaterials.TrophyGold(FilmMaterials.DeepGold, Emission * 0.6f);
            var pale = FilmMaterials.TrophyGold(FilmMaterials.PaleGold, Emission);
            var glass = LowPoly.Mat(LowPoly.Ink);
            var wood = LowPoly.Mat(LowPoly.WoodDark);
            var woodLight = LowPoly.Mat(LowPoly.Wood);
            Vector3 alongZ = new Vector3(90f, 0f, 0f);

            // plinth with a brass plaque
            P(src, FilmMesh.RoundedBox(new Vector3(0.22f, 0.05f, 0.15f), 0.008f), wood, new Vector3(0f, 0.025f, 0f), Vector3.zero);
            P(src, FilmMesh.RoundedBox(new Vector3(0.19f, 0.016f, 0.125f), 0.005f), woodLight, new Vector3(0f, 0.058f, 0f), Vector3.zero);
            P(src, FilmMesh.RoundedBox(new Vector3(0.11f, 0.026f, 0.004f), 0.0015f), pale, new Vector3(0f, 0.026f, 0.0765f), Vector3.zero);
            // camcorder body (T2 shape), lens forward (+Z)
            float y = 0.066f + 0.052f;
            P(src, FilmMesh.RoundedBox(new Vector3(0.1f, 0.1f, 0.19f), 0.02f), gold, new Vector3(0f, y, 0f), Vector3.zero);
            P(src, FilmMesh.RoundedBox(new Vector3(0.103f, 0.056f, 0.14f), 0.012f), deep, new Vector3(0f, y - 0.014f, 0.005f), Vector3.zero);
            P(src, FilmMesh.RoundedCylinder(0.043f, 0.035f, 24, 0.006f), deep, new Vector3(0f, y + 0.005f, 0.11f), alongZ);
            P(src, FilmMesh.RoundedCylinder(0.037f, 0.026f, 24, 0.004f), pale, new Vector3(0f, y + 0.005f, 0.13f), alongZ);
            P(src, FilmMesh.Cylinder(0.031f, 0.006f, 24), glass, new Vector3(0f, y + 0.005f, 0.142f), alongZ);
            // handle on top, flip-out screen, record button
            P(src, FilmMesh.RoundedBox(new Vector3(0.026f, 0.02f, 0.14f), 0.009f), gold, new Vector3(0f, y + 0.082f, -0.005f), Vector3.zero);
            P(src, FilmMesh.RoundedBox(new Vector3(0.018f, 0.034f, 0.018f), 0.006f), gold, new Vector3(0f, y + 0.062f, 0.052f), Vector3.zero);
            P(src, FilmMesh.RoundedBox(new Vector3(0.018f, 0.034f, 0.018f), 0.006f), gold, new Vector3(0f, y + 0.062f, -0.062f), Vector3.zero);
            P(src, FilmMesh.RoundedBox(new Vector3(0.008f, 0.06f, 0.09f), 0.003f), deep, new Vector3(-0.056f, y + 0.012f, -0.04f), new Vector3(0f, -12f, 0f));
            P(src, FilmMesh.RoundedCylinder(0.009f, 0.007f, 16, 0.002f), pale, new Vector3(0.03f, y - 0.025f, -0.098f), alongZ);
            // a little star on top
            P(src, FilmMesh.Sphere(0.012f, 10), pale, new Vector3(0f, y + 0.103f, -0.005f), Vector3.zero);

            var meshes = FilmMesh.CombineStaticMany(root.transform, src);
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].shadowCastingMode = ShadowCastingMode.On;
                renderers[i].reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                renderers[i].motionVectorGenerationMode = MotionVectorGenerationMode.Camera;
                renderers[i].gameObject.layer = 0;
            }
            root.AddComponent<FilmOwnedMeshes>().meshes.AddRange(meshes);
            return root;
        }

        static void P(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 euler)
        {
            var go = new GameObject("p");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }

    /// Destroys meshes created for one object together with it.
    public sealed class FilmOwnedMeshes : MonoBehaviour
    {
        public readonly List<Mesh> meshes = new List<Mesh>();

        void OnDestroy()
        {
            for (int i = 0; i < meshes.Count; i++) if (meshes[i] != null) Destroy(meshes[i]);
            meshes.Clear();
        }
    }

    /// Shows the golden camera trophy on its shelf anchor while the Sealog is 100 % (built lazily on first show).
    /// Listens to the Sealog events – no per-frame work.
    public sealed class FilmTrophyMount : MonoBehaviour
    {
        GameObject trophy;

        void OnEnable()
        {
            GameEvents.SpeciesDiscovered += OnDiscovered;
            GameEvents.GameStarted += Refresh;
            GameEvents.ReturnedToMenu += Refresh;
            GameEvents.DayStarted += OnDay;
            Net.StateSynced += Refresh;
            Refresh();
        }

        void OnDisable()
        {
            GameEvents.SpeciesDiscovered -= OnDiscovered;
            GameEvents.GameStarted -= Refresh;
            GameEvents.ReturnedToMenu -= Refresh;
            GameEvents.DayStarted -= OnDay;
            Net.StateSynced -= Refresh;
        }

        void OnDiscovered(SpeciesDef s, float cm, SizeTier tier) => Refresh();
        void OnDay(int day) => Refresh();

        void Refresh()
        {
            bool want = FilmTrophy.Refresh();
            if (want && trophy == null) trophy = FilmTrophy.Build(transform);
            if (trophy != null && trophy.activeSelf != want) trophy.SetActive(want);
        }
    }
}
