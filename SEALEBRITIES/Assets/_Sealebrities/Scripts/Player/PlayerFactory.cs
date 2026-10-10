using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Entry point of the Player module: builds the first-person diver with its camera, lamp and hands.
    /// Hierarchy:  Player (CharacterController, PlayerController, PlayerInteractor, PlayerNarcosis; layer Ignore Raycast)
    ///               Head (pitch, eye height)
    ///                 Main Camera (Camera, AudioListener, URP data) – bob/shake offsets; other modules may attach models here
    ///                   Flashlight (spot Light, PlayerFlashlight)
    ///                   ViewModel (PlayerHands)
    public static class PlayerFactory
    {
        public static GameObject Create(Transform root)
        {
            var go = new GameObject("Player");
            go.layer = PlayerController.PlayerLayer;
            if (root != null) go.transform.SetParent(root, false);
            Vector3 spawn = PlayerController.ResolveSpawn(out float spawnYaw);
            go.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, spawnYaw, 0f));

            var cc = go.AddComponent<CharacterController>();
            cc.height = PlayerController.Height;
            cc.radius = PlayerController.Radius;
            cc.center = new Vector3(0f, PlayerController.Height * 0.5f, 0f);
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.05f;
            cc.minMoveDistance = 0f;

            var head = new GameObject("Head").transform;
            head.SetParent(go.transform, false);
            head.localPosition = new Vector3(0f, PlayerController.EyeHeight, 0f);

            // ---- camera (the player owns the Main Camera)
            DisableOtherMainCameras();
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(head, false);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = PlayerController.DefaultFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 4000f;   // the World module's flat ocean ring reaches 6 km; 4 km keeps a crisp horizon
            cam.allowHDR = true;
            // Faceted low-poly edges want real geometry AA: use the pipeline's MSAA when the URP asset enables it
            // (same rule as FXPostProcess, so the two modules agree), otherwise fall back to SMAA.
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool msaa = rp != null && rp.msaaSampleCount > 1;
            cam.allowMSAA = msaa;
            camGo.AddComponent<AudioListener>();
            var urp = cam.GetUniversalAdditionalCameraData();
            if (urp != null)
            {
                urp.renderPostProcessing = true;
                urp.antialiasing = msaa ? AntialiasingMode.None : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                urp.renderShadows = true;
                // safety net: a single NaN/Inf pixel (e.g. a zero normal on a tiny procedural face) is otherwise
                // spread by the bloom into a screen-sized white blob
                urp.stopNaN = true;
            }

            // ---- dive lamp: left/below the eye, converging on the screen center ~9 m ahead. The light sits just in
            // front of the view model (the lamp head reaches ~0.65 m at the end of a swim stroke, the camera rigs stay
            // right of the cone): a spot light only lights what is in front of it, so the hand lamp and the camera model
            // are no longer blown out to white by their own light at a few centimeters.
            var lightGo = new GameObject("Flashlight");
            lightGo.transform.SetParent(camGo.transform, false);
            Vector3 lp = new Vector3(-0.13f, -0.085f, 0.72f);
            lightGo.transform.localPosition = lp;
            lightGo.transform.localRotation = Quaternion.LookRotation((new Vector3(0f, 0f, 9f) - lp).normalized, Vector3.up);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 45f;
            light.innerSpotAngle = 25f;
            light.color = new Color(1f, 0.94f, 0.84f);
            light.intensity = 6f;
            light.range = 15f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;
            var flashlight = lightGo.AddComponent<PlayerFlashlight>();

            // ---- first-person hands
            var vm = new GameObject("ViewModel");
            vm.transform.SetParent(camGo.transform, false);
            var hands = vm.AddComponent<PlayerHands>();

            // ---- controller + helpers
            var pc = go.AddComponent<PlayerController>();
            flashlight.Init(pc, light);
            hands.Init(pc);
            pc.Init(cam, head, root, flashlight, hands);
            go.AddComponent<PlayerInteractor>().Init(pc);
            go.AddComponent<PlayerNarcosis>().Init(pc);

            Refs.Player = pc;
            return go;
        }

        /// A bootstrap/loading camera may exist: switch it off so ours is Camera.main and the only listener.
        static void DisableOtherMainCameras()
        {
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c == null || !c.CompareTag("MainCamera")) continue;
                c.enabled = false;
                var listener = c.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }
    }
}
