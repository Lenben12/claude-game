using UnityEngine;

namespace Sealebrities
{
    /// Lives on the root of the loading screen's 3D scene (UILoadingScene). When Unity destroys that root from outside
    /// (scene unload, application quit, play-mode exit) it hands the clean-up back to the scene: render callbacks,
    /// switched-off lights, the RenderTexture and every runtime mesh / material / texture are released – nothing leaks.
    public sealed class UILoadingSceneHost : MonoBehaviour
    {
        internal UILoadingScene Owner;

        void OnDestroy()
        {
            var o = Owner;
            Owner = null;
            if (o != null) o.Dispose();
        }
    }
}
