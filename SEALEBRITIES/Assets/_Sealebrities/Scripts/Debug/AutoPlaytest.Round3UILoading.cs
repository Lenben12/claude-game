using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 UI – the new loading screen (GDD §19 „Ladebildschirm viel besser gestalten“, §20 Q35 „animierte
    /// Unterwasser-Szene mit Tipps und Fakten“): shown in game, the 3D reef (UILoadingScene) is built, its camera renders
    /// into the RenderTexture and the picture animates (the player's own diver swims, fish school + big animals, bubbles),
    /// it keeps animating with time scale 0 (unscaled time), the world's sun / fog are untouched outside the scene camera,
    /// tip + fact cards rotate; after HideLoading everything is torn down (no scene objects, no RenderTexture, no render
    /// callbacks). Screenshot 81_loading_screen.
    public sealed partial class AutoPlaytest
    {
        IEnumerator StepR3UILoading()
        {
            var ctrl = UIModule.Controller;
            if (!Check(ctrl != null && ctrl.Loading != null, "UI controller + loading screen exist")) yield break;
            var ls = ctrl.Loading;
            Check(UILoadingScene.LiveCount == 0 && UILoadingScene.LiveTextures == 0 && !UILoadingScene.Hooked,
                  "before: no loading scene alive (scenes " + UILoadingScene.LiveCount + ", textures " + UILoadingScene.LiveTextures + ")");
            Check(UILoadingScreen.FactCount >= 25 && UILoadingScreen.TipCount >= 10,
                  "content: " + UILoadingScreen.FactCount + " ocean facts, " + UILoadingScreen.TipCount + " tips");

            Light worldSun = RenderSettings.sun;
            bool sunOn = worldSun != null && worldSun.enabled;
            float timeScale = Time.timeScale;

            UIModule.ShowLoading(0.4f, Loc.Tr("Testlauf: Ladebildschirm …", "Test run: loading screen …"));
            yield return Wait(1.5f);
            var scene = UILoadingScene.Current;
            if (!Check(scene != null && scene.Alive && ls.Scene == scene, "3D loading scene built and owned by the loading screen"))
            {
                UIModule.HideLoading();
                yield return Wait(1f);
                yield break;
            }
            RenderTexture rt = scene.Texture;
            var sceneCam = scene.Camera;
            Check(sceneCam != null && sceneCam.enabled && rt != null && rt.IsCreated() && sceneCam.targetTexture == rt,
                  "scene camera renders into its RenderTexture (" + (rt != null ? rt.width + "×" + rt.height + ", MSAA " + rt.antiAliasing : "none") + ")");
            Check(UILoadingScene.LiveCount == 1 && UILoadingScene.LiveTextures == 1 && UILoadingScene.Hooked,
                  "exactly one scene / one RenderTexture alive while shown");
            Check(scene.RenderedFrames >= 5, "scene camera rendered " + scene.RenderedFrames + " frames in 1.5 s");
            Check(scene.HasDiver, "the player's own diver is in the scene");
            Color want = Settings.Look.suit, got = scene.DiverSuit;
            Check(Mathf.Abs(want.r - got.r) + Mathf.Abs(want.g - got.g) + Mathf.Abs(want.b - got.b) < 0.03f,
                  "diver wears the saved suit colour (#" + ColorUtility.ToHtmlStringRGB(got) + ")");
            Check(scene.FishCount >= 8 && scene.BigAnimalCount >= 1, "fish: " + scene.FishCount + " (school + parrotfish), big animals: " + scene.BigAnimalCount);
            Check(scene.BubbleCount > 0, "bubbles rising: " + scene.BubbleCount);
            Check(scene.KelpCount >= 8 && scene.CoralCount >= 20, "seabed dressing: " + scene.KelpCount + " kelp blades, " + scene.CoralCount + " coral colonies");

            // animation: the diver moves and the picture changes between two moments
            Vector3 d0 = scene.DiverPosition;
            int f0 = scene.RenderedFrames;
            float t0 = scene.SceneTime;
            var a = R3LoadSample(rt, out Color meanA, out float spreadA);
            yield return Wait(0.6f);
            if (!scene.Alive) { Check(false, "loading scene stayed alive while shown"); yield break; }
            Vector3 d1 = scene.DiverPosition;
            var b = R3LoadSample(rt, out Color meanB, out float spreadB);
            float moved = (d1 - d0).magnitude;
            Check(moved > 0.1f, "diver swims: " + F(moved, "0.00") + " m in " + F(scene.SceneTime - t0, "0.00") + " s");
            Check(scene.RenderedFrames > f0, "camera keeps rendering (" + (scene.RenderedFrames - f0) + " frames)");
            float diff = R3LoadDiff(a, b);
            Check(diff > 0.002f, "the picture animates (mean pixel change " + F(diff, "0.0000") + ")");
            Check(spreadA > 0.03f && meanA.b > meanA.r, "the RenderTexture shows an underwater scene (mean #" + ColorUtility.ToHtmlStringRGB(meanA)
                  + ", luminance spread " + F(spreadA, "0.000") + ")");

            // independence from the world: outside the scene camera's render the world's sun is untouched
            Check(!scene.Overriding && scene.LightsHeldOff == 0 && (worldSun == null || worldSun.enabled == sunOn),
                  "world lights / fog untouched outside the loading camera (sun: " + (worldSun != null ? worldSun.name : "none") + ")");

            // unscaled time: keeps animating while the game is paused
            Time.timeScale = 0f;
            try
            {
                Vector3 p0 = scene.DiverPosition;
                yield return Wait(0.5f);
                Check(scene.Alive && (scene.DiverPosition - p0).magnitude > 0.05f, "animates with time scale 0 (unscaled dt)");
            }
            finally { Time.timeScale = timeScale > 0f ? timeScale : 1f; }

            // UI: texts present, cards rotate
            Check(!string.IsNullOrEmpty(ls.TipText) && !string.IsNullOrEmpty(ls.FactText) && ls.Visible && UIState.LoadingOpen,
                  "tip + fact shown: \"" + ls.TipText + "\" / \"" + ls.FactText + "\"");
            yield return Shot("81_loading_screen");
            Check(lastLuma > 0.1f, "loading screenshot is not dark (mean brightness " + F(lastLuma, "0.00") + ")");
            string tip0 = ls.TipText, fact0 = ls.FactText;
            ls.DebugNextCards();
            yield return Frames(3);
            Check(ls.TipText != tip0 && ls.FactText != fact0, "tip and fact rotate");

            // tear-down
            UIModule.HideLoading();
            yield return Wait(1f);
            Check(UILoadingScene.Current == null && ls.Scene == null && !ls.Visible && !UIState.LoadingOpen, "hidden: scene released by the loading screen");
            Check(UILoadingScene.LiveCount == 0 && UILoadingScene.LiveTextures == 0 && !UILoadingScene.Hooked,
                  "hidden: no scene, no RenderTexture, no render callbacks left");
            Check(rt == null && sceneCam == null && GameObject.Find("SB_LoadingScene") == null, "hidden: RenderTexture, camera and scene objects destroyed");
            Check(worldSun == null || worldSun.enabled == sunOn, "hidden: world sun still in its own state");

            // a few quick show / hide cycles never leak
            for (int i = 0; i < 2; i++)
            {
                UIModule.ShowLoading(0.7f, Loc.Tr("Gleich geht's los …", "Almost there …"));
                yield return Frames(4);
                UIModule.HideLoading();
                yield return Wait(0.8f);
            }
            Check(UILoadingScene.LiveCount == 0 && UILoadingScene.LiveTextures == 0 && !UILoadingScene.Hooked,
                  "after quick show / hide cycles: nothing leaked (scenes " + UILoadingScene.LiveCount + ", textures " + UILoadingScene.LiveTextures + ")");
        }

        /// Downsamples a RenderTexture to 64×36 and returns the pixels (+ mean colour and luminance spread).
        static Color32[] R3LoadSample(RenderTexture src, out Color mean, out float spread)
        {
            mean = Color.black;
            spread = 0f;
            if (src == null || !src.IsCreated()) return null;
            const int W = 64, H = 36;
            var small = RenderTexture.GetTemporary(W, H, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                Graphics.Blit(src, small);
                RenderTexture.active = small;
                tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0, false);
                tex.Apply(false);
                var px = tex.GetPixels32();
                double r = 0, g = 0, bl = 0, l = 0, l2 = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    var c = px[i];
                    r += c.r; g += c.g; bl += c.b;
                    double lum = (0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b) / 255.0;
                    l += lum; l2 += lum * lum;
                }
                int n = Mathf.Max(1, px.Length);
                mean = new Color((float)(r / n / 255.0), (float)(g / n / 255.0), (float)(bl / n / 255.0), 1f);
                double m = l / n;
                spread = (float)System.Math.Sqrt(System.Math.Max(0.0, l2 / n - m * m));
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(small);
                if (tex != null) Destroy(tex);
            }
        }

        static float R3LoadDiff(Color32[] a, Color32[] b)
        {
            if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0f;
            double s = 0;
            for (int i = 0; i < a.Length; i++)
                s += System.Math.Abs(a[i].r - b[i].r) + System.Math.Abs(a[i].g - b[i].g) + System.Math.Abs(a[i].b - b[i].b);
            return (float)(s / (a.Length * 3.0 * 255.0));
        }
    }
}
