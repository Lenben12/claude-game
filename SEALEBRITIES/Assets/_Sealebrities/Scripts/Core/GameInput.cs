using UnityEngine;
using UnityEngine.InputSystem;

namespace Sealebrities
{
    /// The only place that reads devices (Input System package). Gameplay code must use these properties.
    /// Controls: WASD move, mouse look, Space up/jump, Ctrl or C down, Shift sprint, E interact, F flashlight,
    /// Tab or P phone, J journal, Esc pause, RMB aim camera, LMB record, mouse wheel zoom.
    public static class GameInput
    {
        static Keyboard K => Keyboard.current;
        static Mouse M => Mouse.current;

        public static float LookSensitivity = 0.08f;

        static bool Held(Key k) => K != null && K[k].isPressed;
        static bool Down(Key k) => K != null && K[k].wasPressedThisFrame;
        static bool Up(Key k) => K != null && K[k].wasReleasedThisFrame;

        public static Vector2 Move
        {
            get
            {
                float x = (Held(Key.D) ? 1f : 0f) - (Held(Key.A) ? 1f : 0f);
                float y = (Held(Key.W) ? 1f : 0f) - (Held(Key.S) ? 1f : 0f);
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        /// Mouse delta already multiplied by sensitivity (degrees-ish per frame).
        public static Vector2 Look
        {
            get
            {
                if (M == null) return Vector2.zero;
                float sens = LookSensitivity * (GameManager.I != null ? GameManager.I.State.mouseSensitivity : 1f);
                return M.delta.ReadValue() * sens;
            }
        }

        public static bool Ascend => Held(Key.Space);
        public static bool Descend => Held(Key.LeftCtrl) || Held(Key.C);
        public static bool Sprint => Held(Key.LeftShift);
        public static bool JumpPressed => Down(Key.Space);
        public static bool InteractPressed => Down(Key.E);
        public static bool FlashlightPressed => Down(Key.F);
        public static bool PhonePressed => Down(Key.Tab) || Down(Key.P);
        public static bool JournalPressed => Down(Key.J);
        public static bool PausePressed => Down(Key.Escape);
        public static bool AnchorPressed => Down(Key.Q);

        public static bool AimHeld => M != null && M.rightButton.isPressed;
        public static bool RecordPressed => M != null && M.leftButton.wasPressedThisFrame;
        public static bool PrimaryHeld => M != null && M.leftButton.isPressed;
        public static bool PrimaryPressed => M != null && M.leftButton.wasPressedThisFrame;
        public static bool PrimaryReleased => M != null && M.leftButton.wasReleasedThisFrame;

        /// -1, 0 or +1 per frame.
        public static float ZoomStep
        {
            get
            {
                if (M == null) return 0f;
                float y = M.scroll.ReadValue().y;
                return y > 0.01f ? 1f : y < -0.01f ? -1f : 0f;
            }
        }

        public static bool KeyHeld(Key k) => Held(k);
        public static bool KeyDown(Key k) => Down(k);
        public static bool KeyUp(Key k) => Up(k);

        public static bool AnyKeyDown => (K != null && K.anyKey.wasPressedThisFrame) || PrimaryPressed;
    }
}
