using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Small modal confirm dialog above every screen (host leaves the session F.2, discard character changes 1c, …).
    /// Light card, icon tile, one short question, two buttons. Esc = cancel, Enter = confirm.
    public static class UIDialog
    {
        static VisualElement layer, root, card;
        static Action onConfirm, onCancel;
        static float anim = 1f;

        public static bool IsOpen { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { layer = null; root = null; card = null; onConfirm = null; onCancel = null; IsOpen = false; anim = 1f; }

        internal static void Init(VisualElement dialogLayer)
        {
            layer = dialogLayer;
            root = new VisualElement { name = "sb-dialog" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0.08f, 0.06f, 0.16f, 0.5f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            layer.Add(root);
        }

        /// Shows the dialog (replaces an open one). danger = the confirm button uses the coral danger style.
        public static void Show(UIGlyph.DrawFn icon, Color color, string title, string text, string cancelLabel, string confirmLabel,
                                Action confirm, Action cancel = null, bool danger = false)
        {
            if (root == null) { confirm?.Invoke(); return; }
            var ctrl = UIModule.Controller;
            if (ctrl != null) ctrl.BringLayerToFront(layer);   // above the Net lobby and the character screen
            onConfirm = confirm;
            onCancel = cancel;
            root.Clear();
            card = UIWidgets.ModalCard(620f);
            card.style.alignItems = Align.Center;
            if (icon != null) card.Add(UIWidgets.IconTile(icon, color, 84f));
            var t = UIKit.Text(title, 32f, UIPalette.Ink, true);
            t.style.marginTop = 14f;
            t.style.whiteSpace = WhiteSpace.Normal;
            t.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(t);
            if (!string.IsNullOrEmpty(text))
            {
                var p = UIKit.Para(text, 20f, UIPalette.InkSoft);
                p.style.marginTop = 8f;
                p.style.unityTextAlign = TextAnchor.MiddleCenter;
                card.Add(p);
            }
            var row = UIKit.Row();
            row.style.marginTop = 24f;
            row.style.alignSelf = Align.Stretch;
            var c = UIKit.Btn(cancelLabel, Cancel, UIBtnKind.Secondary, 21f);
            c.style.flexGrow = 1; c.style.flexBasis = 0;
            row.Add(c);
            var ok = UIKit.Btn(confirmLabel, Confirm, danger ? UIBtnKind.Live : UIBtnKind.Primary, 21f);
            ok.style.flexGrow = 1; ok.style.flexBasis = 0;
            ok.style.marginLeft = 12f;
            row.Add(ok);
            card.Add(row);
            root.Add(card);
            root.style.display = DisplayStyle.Flex;
            IsOpen = true;
            anim = 0f;
            UIAudio.Pop();
        }

        public static void Confirm()
        {
            var a = onConfirm;
            Close();
            try { a?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void Cancel()
        {
            var a = onCancel;
            Close();
            try { a?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public static void Close()
        {
            IsOpen = false;
            onConfirm = null;
            onCancel = null;
            if (root != null) root.style.display = DisplayStyle.None;
        }

        internal static void Tick(float dt)
        {
            if (!IsOpen || anim >= 1f || card == null) return;
            anim = Mathf.Min(1f, anim + dt / 0.18f);
            float e = 1f - (1f - anim) * (1f - anim);
            root.style.opacity = e;
            card.style.scale = new Scale(new Vector2(0.94f + 0.06f * e, 0.94f + 0.06f * e));
        }

        /// UIController input: Esc cancels, Enter confirms. Returns true when the dialog consumed the frame.
        internal static bool HandleInput()
        {
            if (!IsOpen) return false;
            if (GameInput.PausePressed) Cancel();
            else if (GameInput.KeyDown(UnityEngine.InputSystem.Key.Enter) || GameInput.KeyDown(UnityEngine.InputSystem.Key.NumpadEnter)) Confirm();
            return true;
        }
    }
}
