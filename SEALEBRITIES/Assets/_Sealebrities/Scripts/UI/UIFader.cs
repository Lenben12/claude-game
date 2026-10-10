using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Full-screen black fade used by Fader.FadeThrough (fade in → callback at black → fade out, unscaled time).
    public class UIFader
    {
        readonly MonoBehaviour host;
        readonly VisualElement overlay;
        Coroutine running;
        Action pending;
        float alpha;

        public UIFader(MonoBehaviour host, VisualElement layer)
        {
            this.host = host;
            overlay = new VisualElement { name = "sb-fader" };
            UIKit.FillParent(overlay);
            overlay.style.backgroundColor = Color.black;
            overlay.style.opacity = 0f;
            overlay.style.display = DisplayStyle.None;
            overlay.pickingMode = PickingMode.Position;   // swallow clicks while fading
            layer.Add(overlay);
        }

        public bool IsFading => running != null;

        public void FadeThrough(Action atBlack, float duration)
        {
            // a fade that is interrupted must still run its action
            if (pending != null)
            {
                var p = pending;
                pending = null;
                Invoke(p);
            }
            if (running != null && host != null) host.StopCoroutine(running);
            running = null;
            pending = atBlack;
            if (host == null || !host.isActiveAndEnabled)
            {
                var p = pending;
                pending = null;
                Invoke(p);
                return;
            }
            running = host.StartCoroutine(Run(Mathf.Max(0.2f, duration)));
        }

        IEnumerator Run(float duration)
        {
            float fadeIn = duration * 0.4f, hold = duration * 0.2f, fadeOut = duration * 0.4f;
            overlay.style.display = DisplayStyle.Flex;
            float start = alpha, t = 0f;
            while (t < fadeIn)
            {
                t += Step();
                SetAlpha(Mathf.Lerp(start, 1f, Smooth(t / fadeIn)));
                yield return null;
            }
            SetAlpha(1f);
            var cb = pending;
            pending = null;
            Invoke(cb);
            yield return null;   // give teleports/rebuilds one frame while black
            t = 0f;
            while (t < hold) { t += Step(); yield return null; }
            t = 0f;
            while (t < fadeOut)
            {
                t += Step();
                SetAlpha(1f - Smooth(t / fadeOut));
                yield return null;
            }
            SetAlpha(0f);
            overlay.style.display = DisplayStyle.None;
            running = null;
        }

        /// Hard reset (e.g. when the controller is destroyed). Pending actions run only when runPending is true.
        public void Cancel(bool runPending)
        {
            if (running != null && host != null) host.StopCoroutine(running);
            running = null;
            var p = pending;
            pending = null;
            if (runPending) Invoke(p);
            SetAlpha(0f);
            overlay.style.display = DisplayStyle.None;
        }

        static float Step() => Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        void SetAlpha(float a)
        {
            alpha = a;
            overlay.style.opacity = a;
        }

        static void Invoke(Action a)
        {
            if (a == null) return;
            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
