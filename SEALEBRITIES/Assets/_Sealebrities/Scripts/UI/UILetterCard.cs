using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Gus' letter in the prologue (K.5): a paper card in the centre (warm paper, slightly tilted, handwriting-like
    /// Fredoka), signature bottom-right, „E – schließen“. Does not pause the game; E (Interact) or Esc closes it.
    /// npc module: StoryUI.LetterHandler is set to Show(title, body, onClosed) by the UI controller (texts localized; a
    /// last body line starting with „–“ becomes the signature) – or ShowGusLetter(onClosed).
    public static class UILetterCard
    {
        static VisualElement root, paper;
        static Label header, body, signature, closeKey;
        static Action onClosed;
        static float anim = 1f, age;

        public static bool IsOpen { get; private set; }
        /// Time.unscaledTime of the last Close (the E that closes the card must not reopen the letter prop behind it).
        public static float ClosedAt { get; private set; } = -100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { root = null; paper = null; header = null; body = null; signature = null; closeKey = null; onClosed = null; IsOpen = false; anim = 1f; age = 0f; ClosedAt = -100f; }

        internal static void Init(VisualElement layer)
        {
            root = new VisualElement { name = "sb-letter" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0.08f, 0.06f, 0.16f, 0.35f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.pickingMode = PickingMode.Ignore;
            layer.Add(root);

            paper = UIKit.Col();
            paper.style.width = 760f;
            paper.style.maxWidth = Length.Percent(90f);
            paper.style.backgroundColor = UIPalette.Rgb(0xF6ECD6);
            UIKit.Radius(paper, 10f);
            UIKit.Pad(paper, 44f, 52f, 34f, 52f);
            paper.style.borderBottomWidth = 6f;
            paper.style.borderBottomColor = UIPalette.Rgb(0xD9C7A2);
            paper.style.rotate = new Rotate(Angle.Degrees(-1.5f));
            root.Add(paper);

            var head = UIKit.Row();
            head.Add(new UIGlyph(UIGlyphs.Envelope, UIPalette.Rgb(0xB8861B), 34f));
            var ht = header = UIKit.Text(Loc.Tr("Ein Brief von Onkel Gus", "A letter from Uncle Gus"), 22f, UIPalette.Rgb(0x8E6C50), true);
            ht.style.marginLeft = 12f;
            head.Add(ht);
            paper.Add(head);

            body = UIKit.Text("", 27f, UIPalette.Rgb(0x2F2A3A));
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginTop = 18f;
            body.style.unityFontStyleAndWeight = FontStyle.Normal;
            body.style.letterSpacing = 0.3f;
            paper.Add(body);
            signature = UIKit.Text("", 30f, UIPalette.Rgb(0x2F2A3A), true);
            signature.style.alignSelf = Align.FlexEnd;
            signature.style.marginTop = 16f;
            signature.style.rotate = new Rotate(Angle.Degrees(-4f));
            paper.Add(signature);

            var hint = UIKit.Row();
            hint.style.alignSelf = Align.Center;
            hint.style.marginTop = 20f;
            closeKey = UIKit.KeyCap("E", 18f);
            hint.Add(closeKey);
            var hl = UIKit.Text(" " + Loc.Tr("schließen", "close"), 18f, UIPalette.InkSoft, true);
            hint.Add(hl);
            paper.Add(hint);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
        }

        /// StoryUI.LetterHandler contract: (title, body, onClosed). A last body line starting with „–“ / "-" becomes the
        /// signature; an empty title keeps „Ein Brief von Onkel Gus“.
        public static void Show(string title, string text, Action closed)
        {
            string sign = "";
            text = text ?? "";
            int nl = text.LastIndexOf('\n');
            string last = (nl >= 0 ? text.Substring(nl + 1) : "").Trim();
            if (last.Length > 0 && (last[0] == '–' || last[0] == '-')) { sign = last; text = text.Substring(0, nl).TrimEnd(); }
            ShowLetter(title, text, sign, closed);
        }

        /// Shows a letter (texts already localized).
        public static void ShowLetter(string title, string text, string sign, Action closed = null)
        {
            if (root == null) { closed?.Invoke(); return; }
            header.text = string.IsNullOrEmpty(title) ? Loc.Tr("Ein Brief von Onkel Gus", "A letter from Uncle Gus") : title;
            body.text = text ?? "";
            signature.text = sign ?? "";
            closeKey.text = GameInput.BindingLabel(GameAction.Interact);
            onClosed = closed;
            IsOpen = true;
            anim = 0f;
            age = 0f;
            root.style.display = DisplayStyle.Flex;
            UIAudio.Pop();
        }

        /// The K.5 letter text of Gus (DE/EN).
        public static void ShowGusLetter(Action closed = null)
        {
            ShowLetter(null, Loc.Tr(
                "Kind, die Insel gehört dir – Hütte, Boot, Kanal. 1987 verschwand mein Studio im Meer. Bring den Kanal auf eine Million. Horst hilft. Glaub ihm die Hälfte.",
                "Kid, the island is yours – hut, boat, channel. In 1987 my studio vanished into the sea. Get the channel to a million. Horst will help. Believe half of it."),
                "– Gus", closed);
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedAt = Time.unscaledTime;
            if (root != null) root.style.display = DisplayStyle.None;
            var cb = onClosed;
            onClosed = null;
            try { cb?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        internal static void Tick(float dt)
        {
            if (!IsOpen) return;
            age += dt;
            if (anim < 1f)
            {
                anim = Mathf.Min(1f, anim + dt / 0.3f);
                float e = 1f - (1f - anim) * (1f - anim) * (1f - anim);
                root.style.opacity = e;
                paper.style.translate = new Translate(0f, 60f * (1f - e));
            }
        }

        /// UIController input while the letter is open: E or Esc closes it (after a short moment so the E that opened it
        /// does not close it). Returns true while the letter consumes the input.
        internal static bool HandleInput()
        {
            if (!IsOpen) return false;
            if (age > 0.4f && (GameInput.InteractPressed || GameInput.PausePressed)) Close();
            return true;
        }
    }
}
