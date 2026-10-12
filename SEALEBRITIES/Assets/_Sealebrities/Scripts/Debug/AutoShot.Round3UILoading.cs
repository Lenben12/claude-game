using System.Collections;
using System.IO;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 UI screenshots – the new loading screen (3D reef with the player's own diver + tip / fact cards).
    /// Files: r3u_loading_*.png
    ///   r3u_loading_start (8 %, first seconds) / r3u_loading_mid (52 %, after 5 s) / r3u_loading_late (91 %, next cards)
    ///   r3u_loading_en (English texts) / r3u_loading_scene_rt (the raw 3D RenderTexture without UI, for judging the scene)
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3UILoading()
        {
            var ctrl = UIModule.Controller;
            if (ctrl == null || ctrl.Loading == null) { Note("WARNING: r3u_loading – no UI controller"); yield break; }
            var ls = ctrl.Loading;
            var lang = Loc.Current;
            try
            {
                UIModule.ShowLoading(0.08f, Loc.Tr("Insel wird geformt …", "Shaping the island …"));
                yield return Wait(2.5f);
                if (ls.Scene == null) Note("WARNING: r3u_loading – no 3D scene (flat fallback shown)");
                yield return Capture("r3u_loading_start");

                UIModule.ShowLoading(0.52f, Loc.Tr("Korallen wachsen …", "Growing corals …"));
                yield return Wait(5f);
                yield return Capture("r3u_loading_mid");
                SaveLoadingRT("r3u_loading_scene_rt");

                ls.DebugNextCards();
                UIModule.ShowLoading(0.91f, Loc.Tr("Fische werden geweckt …", "Waking up the fish …"));
                yield return Wait(6f);
                yield return Capture("r3u_loading_late");

                Loc.Current = Language.English;
                ls.RefreshTexts();
                UIModule.ShowLoading(0.97f, Loc.Tr("Gleich geht's los …", "Almost there …"));
                yield return Wait(1.5f);
                yield return Capture("r3u_loading_en");
            }
            finally
            {
                Loc.Current = lang;
                ls.RefreshTexts();
                UIModule.HideLoading();
            }
            yield return Wait(1f);
            Note(UILoadingScene.LiveCount == 0 && UILoadingScene.LiveTextures == 0
                ? "r3u_loading: scene torn down after hiding"
                : "WARNING: r3u_loading – loading scene still alive after hiding (" + UILoadingScene.LiveCount + " / " + UILoadingScene.LiveTextures + ")");
        }

        /// Writes the loading scene's RenderTexture (no UI on top) as a PNG.
        void SaveLoadingRT(string name)
        {
            var scene = UILoadingScene.Current;
            var rt = scene != null ? scene.Texture : null;
            if (rt == null || !rt.IsCreated()) { Note("WARNING: " + name + " – no loading scene RenderTexture"); return; }
            var prev = RenderTexture.active;
            RenderTexture tmp = null;
            Texture2D tex = null;
            try
            {
                tmp = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rt, tmp);
                RenderTexture.active = tmp;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false);
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                saved++;
                Note("captured " + name + ".png");
            }
            catch (System.Exception e) { Note("ERROR: " + name + " failed: " + e.Message); }
            finally
            {
                RenderTexture.active = prev;
                if (tmp != null) RenderTexture.ReleaseTemporary(tmp);
                if (tex != null) Destroy(tex);
            }
        }
    }
}
