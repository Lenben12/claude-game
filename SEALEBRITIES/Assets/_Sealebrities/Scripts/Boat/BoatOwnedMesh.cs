using UnityEngine;

namespace Sealebrities
{
    /// Frees a runtime-built mesh together with its GameObject (helicopter / crate parts are rebuilt per flight).
    /// Only attach to objects whose mesh is not shared with anything else.
    public sealed class BoatOwnedMesh : MonoBehaviour
    {
        public Mesh mesh;

        /// Marks the MeshFilter mesh of a freshly built GameObject as owned by it. Call while the object is still active.
        public static GameObject Own(GameObject go)
        {
            if (go == null) return go;
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) go.AddComponent<BoatOwnedMesh>().mesh = mf.sharedMesh;
            return go;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            mesh = null;
        }
    }
}
