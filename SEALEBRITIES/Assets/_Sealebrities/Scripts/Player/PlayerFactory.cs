using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sealebrities
{
    /// Entry point of the Player module: builds the first-person diver with its camera, lamp and hands, installs the
    /// bean-diver factory (DiverModels.Build) and registers the player (Refs.Player, Divers).
    /// Hierarchy:  Player (CharacterController, PlayerController, PlayerInteractor, PlayerNarcosis; layer Ignore Raycast)
    ///               Head (pitch, eye height)
    ///                 Main Camera (Camera, AudioListener, URP data) – bob/shake offsets; other modules may attach models here
    ///                   Flashlight (spot Light, PlayerFlashlight) – lights the world
    ///                   ViewModel (PlayerHands: mitten hands, lamp, fallback rod, 3D phone)
    ///                   ViewModelCamera (URP overlay in the main camera's stack, PlayerViewModelCamera): draws every
    ///                     mesh below the Main Camera (layer 30) on top of the world, runs the post-processing
    /// Also: PlayerIntro (launch intro) and PlayerMenuCamera (crew on the dock in the main menu).
    public static class PlayerFactory
    {
        public static GameObject Create(Transform root)
        {
            DiverBuilder.Install();
            var go = new GameObject("Player");
            go.layer = PlayerController.PlayerLayer;
            if (root != null) go.transform.SetParent(root, false);
            Vector3 spawn = PlayerController.ResolveSpawn(out float spawnYaw);
            go.transform.SetPositionAndRotation(spawn, Quaternion.Euler(0f, spawnYaw, 0f));

            var cc = go.AddComponent<CharacterController>();
            cc.height = PlayerController.Height;
            cc.radius = PlayerController.Radius;
            cc.center = new Vector3(0f, PlayerController.Height * 0.5f, 0f);
            // round 3 collision audit (GDD §19 „durch feste Objekte darf man nirgends laufen“): radius 0.32 / skin 0.05
            // (≈ 15 % of the radius, Unity's recommended range) / step 0.4 / slope 50° are sound – walking through things
            // came from colliders missing on the props (listed for the world step in docs/Runde3_Fortschritt.md) and
            // from things moving INTO the diver (a CharacterController only collides while it moves itself): overlap
            // recovery on + PlayerController.Depenetrate pushes the diver out every frame (boat hulls, doors, teleports)
            cc.slopeLimit = 50f;
            cc.stepOffset = 0.4f;
            cc.skinWidth = 0.05f;
            cc.minMoveDistance = 0f;
            cc.enableOverlapRecovery = true;

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

            // ---- dive lamp (art bible §2.6: warm #FFD9A0, spot 55°, ~14 m). Only lit while the lamp is in a hand; the
            // light follows the lens of the hand lamp, a little in front of it, converging on the screen centre ~9 m
            // ahead – a spot light only lights what is in front of it, so the view model is not blown out.
            var lightGo = new GameObject("Flashlight");
            lightGo.transform.SetParent(camGo.transform, false);
            Vector3 lp = new Vector3(-0.13f, -0.085f, 0.72f);
            lightGo.transform.localPosition = lp;
            lightGo.transform.localRotation = Quaternion.LookRotation((new Vector3(0f, 0f, 9f) - lp).normalized, Vector3.up);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Spot;
            light.spotAngle = 55f;
            light.innerSpotAngle = 32f;
            light.color = LowPoly.Hex("#FFD9A0");
            light.intensity = 3.5f;
            light.range = 14f;
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

            // ---- held items on their own overlay camera: world geometry (dock, posts, rocks) never cuts them
            PlayerViewModelCamera.Attach(cam);
            go.AddComponent<PlayerInteractor>().Init(pc);
            go.AddComponent<PlayerNarcosis>().Init(pc);
            go.AddComponent<PlayerTools>().Init(pc, hands);   // round 3: stick / spears / eating / bottles on LMB

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
