using UnityEngine;
using UnityEngine.Rendering;

namespace Sealebrities
{
    /// The old, scratched 3D phone that the diver raises in the left hand while UIState.PhoneOpen (art bible §1.13).
    /// The UI renders the phone screen into PhoneDisplay.Texture; this shows it on the screen quad (Unlit) and maps
    /// mouse positions onto it (PhoneDisplay.ScreenToUV). Posed every frame by PlayerHands.
    public sealed class PlayerPhone
    {
        public const float Width = 0.075f, Height = 0.155f, Depth = 0.012f;
        public const float ScreenW = 0.064f, ScreenH = 0.128f;   // exactly 1:2 like the 540 x 1080 texture
        const float ScreenY = 0.002f;

        public Transform Root { get; private set; }
        Transform screenT;
        Material screenMat, crackMat, bodyMat;
        Texture boundTex;
        Camera cam;
        bool raised;
        static Texture2D scratchTex, crackTex;
        static int baseMapId = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { scratchTex = null; crackTex = null; baseMapId = -1; }

        public PlayerPhone(Transform parent, Camera camera)
        {
            cam = camera;
            if (baseMapId < 0) baseMapId = Shader.PropertyToID("_BaseMap");
            Root = new GameObject("Phone").transform;
            Root.SetParent(parent, false);

            if (scratchTex == null) scratchTex = BuildScratchTexture();
            if (crackTex == null) crackTex = BuildCrackTexture();

            bodyMat = MaterialLibrary.Textured(scratchTex, Color.white, 0.22f);
            bodyMat.name = "SB_PhoneBody";
            bodyMat.renderQueue = PlayerHands.ViewModelQueue;
            Material bezel = PlayerHands.ViewModelMat(LowPoly.Mat(LowPoly.Hex("#15171F")));
            Material button = PlayerHands.ViewModelMat(LowPoly.Mat(LowPoly.Hex("#2A2D3A")));

            var body = DiverMeshes.Part(Root, "Body", DiverMeshes.RoundedSlab("phone", Width, Height, Depth, 0.008f, 3), bezel, Vector3.zero, Vector3.one, default, false);
            body.sharedMaterials = new[] { bezel, bodyMat };
            body.receiveShadows = false;

            screenMat = new Material(MaterialLibrary.UnlitShader) { name = "SB_PhoneScreen", renderQueue = PlayerHands.ViewModelQueue };
            screenMat.SetColor("_BaseColor", new Color(0.12f, 0.14f, 0.18f, 1f));
            var scr = DiverMeshes.Part(Root, "Screen", DiverMeshes.Quad("phone_screen", ScreenW, ScreenH), screenMat,
                                       new Vector3(0f, ScreenY, -Depth * 0.5f - 0.0004f), Vector3.one, default, false);
            scr.receiveShadows = false;
            screenT = scr.transform;

            crackMat = new Material(MaterialLibrary.UnlitShader) { name = "SB_PhoneCrack" };
            crackMat.SetTexture(baseMapId, crackTex);
            crackMat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.55f));
            MaterialLibrary.MakeTransparent(crackMat, false);
            crackMat.renderQueue = PlayerHands.ViewModelQueue + 1;
            var crack = DiverMeshes.Part(Root, "Crack", DiverMeshes.Quad("phone_crack", ScreenW, ScreenH), crackMat,
                                         new Vector3(0f, ScreenY, -Depth * 0.5f - 0.0009f), Vector3.one, default, false);
            crack.receiveShadows = false;

            // speaker slot and a round home button (it is an old phone)
            DiverMeshes.Part(Root, "Speaker", DiverMeshes.Capsule(0.0018f, 0.014f, 6), button, new Vector3(0.0088f, Height * 0.5f - 0.0075f, -Depth * 0.5f - 0.0003f), Vector3.one, new Vector3(0f, 0f, 90f), false);
            DiverMeshes.Part(Root, "Home", DiverMeshes.Torus(0.0045f, 0.0011f, 12, 4), button, new Vector3(0f, -Height * 0.5f + 0.0068f, -Depth * 0.5f - 0.0002f), Vector3.one, new Vector3(90f, 0f, 0f), false);
            // camera bump on the back
            DiverMeshes.Part(Root, "CamBump", DiverMeshes.Capsule(0.0055f, 0.004f, 10), bezel, new Vector3(-0.024f, Height * 0.5f - 0.016f, Depth * 0.5f), Vector3.one, new Vector3(90f, 0f, 0f), false);

            Root.gameObject.SetActive(false);
            uvFunc = ScreenToUV;
            PhoneDisplay.ScreenToUV = uvFunc;
        }

        System.Func<Vector2, Vector2?> uvFunc;

        public void SetVisible(bool v)
        {
            if (Root != null && Root.gameObject.activeSelf != v) Root.gameObject.SetActive(v);
            if (!v) SetRaised(false);
        }

        public void SetRaised(bool r)
        {
            raised = r;
            PhoneDisplay.Raised = r;
        }

        /// Keeps the screen material bound to the UI's render texture.
        public void RefreshScreen()
        {
            Texture tex = PhoneDisplay.Texture;
            if (tex == boundTex) return;
            boundTex = tex;
            if (tex != null)
            {
                screenMat.SetTexture(baseMapId, tex);
                // a little below 1 so the light UI is not pushed into bloom / clipped by the filmic grading
                screenMat.SetColor("_BaseColor", new Color(0.84f, 0.84f, 0.84f, 1f));
            }
            else
            {
                screenMat.SetTexture(baseMapId, null);
                screenMat.SetColor("_BaseColor", new Color(0.12f, 0.14f, 0.18f, 1f));
            }
        }

        /// Screen pixel → phone screen UV (origin bottom-left), null when outside or not raised. Allocation free.
        Vector2? ScreenToUV(Vector2 screen)
        {
            if (!raised || cam == null || screenT == null || !screenT.gameObject.activeInHierarchy) return null;
            Ray r = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
            Vector3 o = screenT.InverseTransformPoint(r.origin);
            Vector3 d = screenT.InverseTransformDirection(r.direction);
            if (Mathf.Abs(d.z) < 1e-6f) return null;
            float t = -o.z / d.z;
            if (t < 0f) return null;
            Vector3 p = o + d * t;
            float u = p.x / ScreenW + 0.5f, v = p.y / ScreenH + 0.5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return null;
            return new Vector2(u, v);
        }

        public void Destroy()
        {
            if (uvFunc != null && PhoneDisplay.ScreenToUV == uvFunc) PhoneDisplay.ScreenToUV = null;
            PhoneDisplay.Raised = false;
            if (screenMat != null) Object.Destroy(screenMat);
            if (crackMat != null) Object.Destroy(crackMat);
            if (bodyMat != null) Object.Destroy(bodyMat);
        }

        // ------------------------------------------------------------------ textures (built once, deterministic)
        static Texture2D BuildScratchTexture()
        {
            const int w = 128, h = 256;
            var px = new Color32[w * h];
            Color32 baseC = LowPoly.Hex("#3A3F4C"), scratch = LowPoly.Hex("#6A7080"), dark = LowPoly.Hex("#2F333E");
            var rng = new System.Random(4711);
            for (int i = 0; i < px.Length; i++)
            {
                // faint mottling of worn plastic
                int n = rng.Next(100);
                px[i] = n < 6 ? dark : baseC;
            }
            for (int s = 0; s < 46; s++)
            {
                float x = rng.Next(w), y = rng.Next(h);
                float ang = (float)rng.NextDouble() * Mathf.PI;
                int len = 6 + rng.Next(s < 6 ? 60 : 24);
                float dx = Mathf.Cos(ang), dy = Mathf.Sin(ang);
                for (int k = 0; k < len; k++)
                {
                    int px0 = Mathf.RoundToInt(x + dx * k), py0 = Mathf.RoundToInt(y + dy * k);
                    if (px0 < 0 || py0 < 0 || px0 >= w || py0 >= h) break;
                    px[py0 * w + px0] = scratch;
                    // curve a little
                    ang += ((float)rng.NextDouble() - 0.5f) * 0.12f;
                    dx = Mathf.Cos(ang); dy = Mathf.Sin(ang);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "SB_PhoneScratches", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        /// White crack lines radiating from a point near the top-left corner on a transparent background.
        static Texture2D BuildCrackTexture()
        {
            const int w = 128, h = 256;
            var px = new Color32[w * h];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            var rng = new System.Random(1312);
            Vector2 origin = new Vector2(16f, h - 22f);
            for (int b = 0; b < 7; b++)
            {
                Vector2 p = origin;
                float ang = Mathf.Lerp(-1.75f, 0.35f, b / 6f) + ((float)rng.NextDouble() - 0.5f) * 0.3f;
                int len = 12 + rng.Next(b % 2 == 0 ? 46 : 22);
                for (int k = 0; k < len; k++)
                {
                    p += new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    ang += ((float)rng.NextDouble() - 0.5f) * 0.5f;
                    int x = Mathf.RoundToInt(p.x), y = Mathf.RoundToInt(p.y);
                    if (x < 0 || y < 0 || x >= w || y >= h) break;
                    byte a = (byte)Mathf.Clamp(230 - k * 3, 70, 230);
                    px[y * w + x] = new Color32(255, 255, 255, a);
                    if (k < 8 && x + 1 < w) px[y * w + x + 1] = new Color32(255, 255, 255, (byte)(a / 2));
                }
            }
            // small impact star
            for (int dy = -2; dy <= 2; dy++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = (int)origin.x + dx, y = (int)origin.y + dy;
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 2) px[y * w + x] = new Color32(255, 255, 255, 200);
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "SB_PhoneCrack", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
