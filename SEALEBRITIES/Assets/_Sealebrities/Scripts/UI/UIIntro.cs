using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Launch intro ("trailer") overlay: Safari-TV style banner captions for the Player module's in-engine intro
    /// (PlayerIntro) plus a skip hint. All PlayerIntro calls are isolated in the three methods at the bottom.
    public class UIIntro
    {
        readonly VisualElement root, banner, stripe, hint;
        readonly Label captionLabel;
        string pending, shown;
        float anim, age, outT = -1f;
        bool active;
        Action<string> captionHandler;

        public UIIntro(VisualElement layer)
        {
            root = new VisualElement { name = "sb-intro" };
            UIKit.FillParent(root);
            root.pickingMode = PickingMode.Ignore;
            layer.Add(root);

            // lower-third banner (slanted coral stripe + white caption)
            banner = UIKit.Col();
            UIKit.Abs(banner, 0f, null, null, 150f);
            banner.style.alignItems = Align.FlexStart;
            root.Add(banner);
            stripe = UIKit.Row();
            stripe.style.backgroundColor = UIPalette.A(UIPalette.Coral, 0.94f);
            stripe.style.borderBottomWidth = 6f;
            stripe.style.borderBottomColor = UIPalette.Darken(UIPalette.Coral, 0.25f);
            UIKit.Pad(stripe, 16f, 60f, 18f, 140f);
            stripe.style.borderTopRightRadius = 16f;
            stripe.style.borderBottomRightRadius = 16f;
            stripe.style.rotate = new Rotate(Angle.Degrees(-2f));
            banner.Add(stripe);
            var dot = UIKit.Dot(Color.white, 20f);
            dot.style.marginRight = 18f;
            stripe.Add(dot);
            captionLabel = UIKit.HudText("", 54f);
            captionLabel.style.whiteSpace = WhiteSpace.Normal;
            captionLabel.style.maxWidth = 1300f;
            stripe.Add(captionLabel);
            var tag = UIKit.HudText("SEALEBRITIES · LIVE FROM THE DEEP", 18f, UIPalette.Sand);
            tag.style.marginLeft = 150f;
            tag.style.marginTop = 10f;
            tag.style.letterSpacing = 3f;
            banner.Add(tag);
            banner.style.display = DisplayStyle.None;

            hint = UIKit.Row();
            UIKit.Abs(hint, null, null, 40f, 36f);
            hint.Add(UIKit.HudKeyCap(Loc.Tr("Taste", "Key"), 30f));
            var ht = UIKit.HudText(Loc.Tr("  Überspringen", "  Skip"), 20f, new Color(1f, 1f, 1f, 0.85f));
            hint.Add(ht);
            hint.style.display = DisplayStyle.None;
            root.Add(hint);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
        }

        public bool Active => active;

        /// Starts the intro (if the Player module can play it) and calls onDone when it ends or is skipped.
        public void Run(Action onDone)
        {
            active = true;
            root.style.display = DisplayStyle.Flex;
            age = 0f;
            pending = null; shown = null;
            UIKit.Show(banner, false);
            UIKit.Show(hint, false);
            if (captionHandler == null) captionHandler = OnCaption;
            Subscribe(captionHandler);
            Play(() =>
            {
                Unsubscribe(captionHandler);
                active = false;
                root.style.display = DisplayStyle.None;
                onDone?.Invoke();
            });
        }

        void OnCaption(string text) { pending = text ?? ""; }

        public void Tick(float dt)
        {
            if (!active) return;
            age += dt;
            UIKit.Show(hint, age > 1.0f && IsPlaying());

            // caption change: slide the old one out, then the new one in
            if (pending != null && !string.Equals(pending, shown))
            {
                if (shown != null && shown.Length > 0 && outT < 0f) outT = 0f;
                if (outT < 0f || outT >= 0.25f)
                {
                    shown = pending;
                    pending = null;
                    outT = -1f;
                    captionLabel.text = shown;
                    anim = 0f;
                    UIKit.Show(banner, shown.Length > 0);
                    if (shown.Length > 0) UIAudio.Pop();
                }
            }
            if (outT >= 0f)
            {
                outT += dt;
                float o = Mathf.Clamp01(outT / 0.25f);
                banner.style.translate = new Translate(-900f * o * o, 0f);
                banner.style.opacity = 1f - o;
                return;
            }
            if (anim < 1f)
            {
                anim = Mathf.Min(1f, anim + dt / 0.35f);
                float e = 1f - (1f - anim) * (1f - anim) * (1f - anim);
                banner.style.translate = new Translate(-900f * (1f - e), 0f);
                banner.style.opacity = 1f;
            }
        }

        /// Skips the running intro (any key is handled by the Player module itself; this is for Esc/menus).
        public void Skip()
        {
            if (!active) return;
            try { PlayerIntro.Skip(); } catch (Exception e) { Debug.LogException(e); }
        }

        // ------------------------------------------------------------------ PlayerIntro (Player module) – isolated
        static void Play(Action onDone)
        {
            try { PlayerIntro.Play(onDone); }
            catch (Exception e)
            {
                Debug.LogException(e);
                onDone?.Invoke();
            }
        }

        static bool IsPlaying() => PlayerIntro.Playing;

        static void Subscribe(Action<string> handler) { PlayerIntro.Caption += handler; }

        static void Unsubscribe(Action<string> handler) { PlayerIntro.Caption -= handler; }
    }
}
