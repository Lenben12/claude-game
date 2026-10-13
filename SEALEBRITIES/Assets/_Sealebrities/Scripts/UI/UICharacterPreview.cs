using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Creates the live 3D preview of a bean diver for the character screen and the lobby thumbnail (1c): the Player
    /// module's CharacterPreviewFactory when it is installed, otherwise the small built-in stage below (same contract).
    public static class UICharacterPreviews
    {
        /// Render-texture size by quality tier (1c: High 1024², Medium 768², Low 512²).
        public static int ScreenSize
        {
            get
            {
                switch (Settings.Data.quality)
                {
                    case GraphicsQuality.Low: return 512;
                    case GraphicsQuality.Medium: return 768;
                    default: return 1024;
                }
            }
        }

        /// External (Player module) previews tick themselves; the fallback needs UICharacterPreviews.Tick.
        public static ICharacterPreview Create(int size, int slot)
        {
            var f = CharacterPreviewFactory.Create;
            if (f != null)
            {
                try
                {
                    var p = f(size);
                    if (p != null) return p;
                }
                catch (System.Exception e) { Debug.LogException(e); }
            }
            try { return new UICharacterPreviewFallback(size, slot); }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                return null;
            }
        }

        /// Per-frame update for the built-in preview (no-op for the Player module's preview).
        public static void Tick(ICharacterPreview p, float dt)
        {
            if (p is UICharacterPreviewFallback fb) fb.Tick(dt);
        }
    }

    /// Built-in preview stage (fallback for ICharacterPreview): plank disc, warm key light + teal rim light on layer
    /// GameLayers.Preview at (0, 420, 0) (+ 60 m per slot so the thumbnail and the screen never see each other), its
    /// own camera rendering into a transparent RenderTexture (the UI draws the warm gradient behind it). Created on open,
    /// destroyed on close – zero cost while closed.
    public sealed class UICharacterPreviewFallback : ICharacterPreview
    {
        static readonly Vector3 StageBase = new Vector3(0f, 420f, 0f);
        const float PitchDeg = 5f, AutoRotateAfter = 5f, AutoRotateSpeed = 10f, FocusSeconds = 0.35f;

        readonly GameObject root;
        readonly Transform pivot;
        readonly Camera cam;
        readonly RenderTexture rt;
        static readonly List<Light> maskedLights = new List<Light>();
        static readonly List<int> maskedMasks = new List<int>();
        static int alive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { maskedLights.Clear(); maskedMasks.Clear(); alive = 0; }
        readonly Vector3 stage;
        GameObject diver;
        float yaw = 20f, idle, reaction = -1f;
        float fromH, fromD, toH, toD, curH = 0.95f, curD = 4.4f, focusT = 1f;
        bool disposed;
        // render-state override while our camera renders (fog/ambient of the world must not tint the preview)
        bool prevFog;
        AmbientMode prevAmbientMode;
        Color prevAmbient;
        SphericalHarmonicsL2 prevProbe;
        bool overriding;

        public UICharacterPreviewFallback(int size, int slot)
        {
            size = Mathf.Clamp(size, 128, 2048);
            stage = StageBase + new Vector3(60f * Mathf.Clamp(slot, 0, 4), 0f, 0f);
            bool low = Settings.Data.quality == GraphicsQuality.Low;
            rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "SB_CharacterPreview", antiAliasing = low ? 1 : 4, useMipMap = false, filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();

            root = new GameObject("SB_CharacterPreviewStage");
            root.layer = GameLayers.Preview;
            root.transform.position = stage;
            pivot = new GameObject("Pivot").transform;
            pivot.gameObject.layer = GameLayers.Preview;
            pivot.SetParent(root.transform, false);

            BuildDisc();
            Light("SB_PreviewKey", Quaternion.Euler(30f, 180f + 35f, 0f), new Color(1f, 0.886f, 0.722f), 1.1f, !low);
            Light("SB_PreviewRim", Quaternion.Euler(20f, -35f, 0f), new Color(0.498f, 0.827f, 0.878f), 0.6f, false);

            var camGo = new GameObject("SB_PreviewCamera");
            camGo.layer = GameLayers.Preview;
            camGo.transform.SetParent(root.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = GameLayers.PreviewMask;
            cam.fieldOfView = 28f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.allowHDR = false;
            cam.allowMSAA = !low;
            cam.useOcclusionCulling = false;
            cam.depth = -50f;
            cam.targetTexture = rt;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = !low;
            }

            // the world's sun/moon must not light the stage (the time of day would change the look); the first live
            // stage masks them, the last one restores them (thumbnail + screen may overlap for a frame)
            if (alive++ == 0)
            {
                var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    var l = lights[i];
                    if (l == null || l.gameObject.layer == GameLayers.Preview || l.type != LightType.Directional) continue;
                    if ((l.cullingMask & GameLayers.PreviewMask) == 0) continue;
                    maskedLights.Add(l);
                    maskedMasks.Add(l.cullingMask);
                    l.cullingMask &= ~GameLayers.PreviewMask;
                }
            }
            RenderPipelineManager.beginCameraRendering += OnBegin;
            RenderPipelineManager.endCameraRendering += OnEnd;
            ApplyCamera();
        }

        public RenderTexture Texture => rt;

        void BuildDisc()
        {
            // round plank platform (dock plank palette), 7 planks clipped to a 0.9 m circle
            var wood = MaterialLibrary.Opaque(UIPalette.Rgb(0x9A7658), 0.15f);
            var woodDark = MaterialLibrary.Opaque(UIPalette.Rgb(0x86654A), 0.15f);
            const float R = 0.9f, W = 0.25f;
            for (int i = 0; i < 7; i++)
            {
                float x = (i - 3) * W;
                float half = Mathf.Sqrt(Mathf.Max(0.01f, R * R - x * x));
                var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = p.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                p.name = "Plank";
                p.layer = GameLayers.Preview;
                p.transform.SetParent(root.transform, false);
                p.transform.localPosition = new Vector3(x, -0.06f, 0f);
                p.transform.localScale = new Vector3(W - 0.02f, 0.12f, half * 2f);
                var mr = p.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = (i & 1) == 0 ? wood : woodDark;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
        }

        void Light(string name, Quaternion rot, Color c, float intensity, bool shadows)
        {
            var go = new GameObject(name);
            go.layer = GameLayers.Preview;
            go.transform.SetParent(root.transform, false);
            go.transform.rotation = rot;
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            l.cullingMask = GameLayers.PreviewMask;
        }

        void OnBegin(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || overriding) return;
            overriding = true;
            prevFog = RenderSettings.fog;
            prevAmbientMode = RenderSettings.ambientMode;
            prevAmbient = RenderSettings.ambientLight;
            prevProbe = RenderSettings.ambientProbe;
            RenderSettings.fog = false;
            var amb = new Color(0.55f, 0.52f, 0.5f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = amb;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(amb);
            RenderSettings.ambientProbe = sh;
        }

        void OnEnd(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || !overriding) return;
            overriding = false;
            RenderSettings.fog = prevFog;
            RenderSettings.ambientMode = prevAmbientMode;
            RenderSettings.ambientLight = prevAmbient;
            RenderSettings.ambientProbe = prevProbe;
        }

        public void SetLook(DiverLook look)
        {
            if (disposed) return;
            if (diver != null) Object.Destroy(diver);
            diver = null;
            var build = DiverModels.Build;
            if (build == null) return;
            look.sitting = false;
            look.sponsor = DiverSponsor.CurrentIndex;   // the channel's sponsor patch (K.7)
            try { diver = build(look); }
            catch (System.Exception e) { Debug.LogException(e); diver = null; }
            if (diver == null) return;
            diver.name = "SB_PreviewDiver";
            diver.transform.SetParent(pivot, false);
            diver.transform.localPosition = Vector3.zero;
            diver.transform.localRotation = Quaternion.identity;
            SetLayer(diver.transform, GameLayers.Preview);
            DiverLod.Force(diver, true);   // the close-up always shows the full model
            var cols = diver.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            var puppet = diver.GetComponentInChildren<IDiverPuppet>();
            if (puppet != null) puppet.SetPose(false, false, 0f, HoldItem.None, HoldItem.None, false);
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        public void AddYaw(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            yaw = Mathf.Repeat(yaw + degrees, 360f);
            idle = 0f;
        }

        public void Focus(PreviewFocus focus)
        {
            float h, d;
            switch (focus)
            {
                // round 3: the human-proportioned diver's head sits at 1.53–1.87 m (eyes 1.715 m)
                case PreviewFocus.Head: h = 1.6f; d = 2.0f; break;
                case PreviewFocus.Face: h = 1.68f; d = 1.15f; break;
                case PreviewFocus.Feet: h = 0.35f; d = 2.2f; break;
                default: h = 0.95f; d = 4.4f; break;
            }
            fromH = curH; fromD = curD; toH = h; toD = d; focusT = 0f;
        }

        public void PlayReaction() { reaction = 0f; idle = 0f; }

        public void Tick(float dt)
        {
            if (disposed) return;
            idle += dt;
            if (idle > AutoRotateAfter) yaw = Mathf.Repeat(yaw + AutoRotateSpeed * dt, 360f);
            if (focusT < 1f)
            {
                focusT = Mathf.Min(1f, focusT + dt / FocusSeconds);
                float e = 1f - (1f - focusT) * (1f - focusT);
                curH = Mathf.Lerp(fromH, toH, e);
                curD = Mathf.Lerp(fromD, toD, e);
            }
            float scale = 1f, hop = 0f;
            if (reaction >= 0f)
            {
                reaction += dt;
                float t = Mathf.Clamp01(reaction / 0.6f);
                hop = Mathf.Sin(t * Mathf.PI) * 0.12f;
                scale = 1f + Mathf.Sin(t * Mathf.PI) * 0.05f;
                if (t >= 1f) reaction = -1f;
            }
            float breath = 1f + Mathf.Sin(Time.unscaledTime * 1.6f) * 0.015f;
            pivot.localRotation = Quaternion.Euler(0f, yaw, 0f);
            pivot.localPosition = new Vector3(0f, hop, 0f);
            pivot.localScale = new Vector3(scale, scale * breath, scale);
            ApplyCamera();
        }

        void ApplyCamera()
        {
            if (cam == null) return;
            float pitch = PitchDeg * Mathf.Deg2Rad;
            Vector3 target = stage + new Vector3(0f, curH, 0f);
            Vector3 pos = target + new Vector3(0f, Mathf.Sin(pitch) * curD, Mathf.Cos(pitch) * curD);
            cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(target - pos, Vector3.up));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            RenderPipelineManager.beginCameraRendering -= OnBegin;
            RenderPipelineManager.endCameraRendering -= OnEnd;
            if (overriding)
            {
                overriding = false;
                RenderSettings.fog = prevFog;
                RenderSettings.ambientMode = prevAmbientMode;
                RenderSettings.ambientLight = prevAmbient;
                RenderSettings.ambientProbe = prevProbe;
            }
            if (--alive <= 0)
            {
                alive = 0;
                for (int i = 0; i < maskedLights.Count; i++)
                    if (maskedLights[i] != null) maskedLights[i].cullingMask = maskedMasks[i] | GameLayers.PreviewMask;
                maskedLights.Clear();
                maskedMasks.Clear();
            }
            if (cam != null) cam.targetTexture = null;
            if (root != null) Object.Destroy(root);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
        }
    }

    /// Live thumbnail of the local bean diver (lobby profile card, 1c): VisualElement that owns a small preview while it
    /// is on a panel, turns slowly and follows look changes. It steps aside (disposes its preview) while the character
    /// screen is open, so only one preview stage exists at a time. Net: `new UICharacterThumb(220f)` + a button calling
    /// CharacterScreen.Open(CharacterScreenContext.Lobby).
    public class UICharacterThumb : VisualElement
    {
        ICharacterPreview preview;
        IVisualElementScheduledItem tick;
        int lookHash;
        float last;
        readonly Label placeholder;

        public UICharacterThumb(float size = 220f)
        {
            style.width = size; style.height = size;
            style.flexShrink = 0;
            style.overflow = Overflow.Hidden;
            UIKit.Radius(this, size * 0.12f);
            UIKit.Image(this, UIKit.VGradient(UIPalette.Rgb(0xE98F6A), UIPalette.Rgb(0xF2C48A)), false);
            pickingMode = PickingMode.Ignore;
            var img = new VisualElement { name = "sb-thumb-image", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(img);
            Add(img);
            placeholder = UIKit.Text("", 14f, UIPalette.Ink, true);
            placeholder.style.position = Position.Absolute;
            placeholder.style.bottom = 8f;
            placeholder.style.alignSelf = Align.Center;
            Add(placeholder);
            RegisterCallback<AttachToPanelEvent>(_ => Start());
            RegisterCallback<DetachFromPanelEvent>(_ => Stop());
        }

        /// Everything visible (round 3: body / hair / beard / nose / accessory too) – NetLookCodec's quantized hash.
        static int Hash(DiverLook l) => NetLookCodec.Hash(l);

        void Start()
        {
            if (tick == null) tick = schedule.Execute(OnTick).Every(33);
            else tick.Resume();
            last = Time.unscaledTime;
        }

        void Stop()
        {
            tick?.Pause();
            Release();
        }

        void Release()
        {
            if (preview != null)
            {
                try { preview.Dispose(); } catch (System.Exception e) { Debug.LogException(e); }
                preview = null;
            }
            var img = this.Q("sb-thumb-image");
            if (img != null) img.style.backgroundImage = StyleKeyword.None;
        }

        void OnTick()
        {
            float now = Time.unscaledTime;
            float dt = Mathf.Clamp(now - last, 0f, 0.1f);
            last = now;
            if (CharacterScreen.IsOpen) { if (preview != null) Release(); return; }
            var look = Settings.Look;
            int h = Hash(look);
            if (preview == null)
            {
                preview = UICharacterPreviews.Create(256, 1);
                if (preview == null) { placeholder.text = Settings.DisplayName; return; }
                lookHash = h;
                preview.SetLook(look);
                preview.Focus(PreviewFocus.Body);
                var img = this.Q("sb-thumb-image");
                if (img != null && preview.Texture != null)
                {
                    img.style.backgroundImage = Background.FromRenderTexture(preview.Texture);
                    img.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
                }
                placeholder.text = "";
            }
            else if (h != lookHash)
            {
                lookHash = h;
                preview.SetLook(look);
                preview.PlayReaction();
            }
            UICharacterPreviews.Tick(preview, dt);
        }
    }
}
