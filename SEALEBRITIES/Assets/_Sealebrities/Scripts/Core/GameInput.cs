using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Sealebrities
{
    /// Rebindable game actions (Settings → Steuerung). Order is stable: append new values at the end.
    /// Round 2: StreamDare (T, accept a livestream dare – C.5), Selfie (H, selfie view – K.4).
    /// Crouch on land (L.6) uses Down (no own action).
    /// Round 3: ONE hotbar of 8 slots – RightSlot1..3 are slots 1–3, LeftSlot1..3 slots 4–6 (names kept for the
    /// stored bindings), Slot7 / Slot8 were appended. Use GameInput.SlotAction(i) / SlotPressed(i).
    public enum GameAction
    {
        Forward, Back, Left, Right, Up, Down, Sprint, Jump, Interact, LightToggle, Phone, Journal, Inventory, Pause,
        RightSlot1, RightSlot2, RightSlot3, LeftSlot1, LeftSlot2, LeftSlot3, Anchor, PushToTalk, Use, Aim, Photo,
        StreamDare, Selfie,
        // round 3
        Slot7, Slot8,
        /// Round 3 (GDD §20 Q5): hold for the gesture wheel – the hermits' sign language and the action wheel at every NPC.
        Gestures,
        /// Round 3 UI step (GDD §19 „Während der Fahrt Zugriff auf die Karte“): the treasure-map overlay (default M) –
        /// works while driving the boat and on the passenger bench (does not stop steering).
        Map
    }

    /// A key OR a mouse button. key != Key.None → keyboard binding; otherwise mouseButton
    /// (-1 none, 0 left, 1 right, 2 middle, 3 back, 4 forward). Build it with FromKey / FromMouse / None.
    [Serializable]
    public struct InputBinding : IEquatable<InputBinding>
    {
        public Key key;
        public int mouseButton;

        public InputBinding(Key key) { this.key = key; mouseButton = -1; }

        public static InputBinding FromKey(Key k) => new InputBinding(k);
        public static InputBinding FromMouse(int button) => new InputBinding { key = Key.None, mouseButton = button };
        public static InputBinding None => new InputBinding { key = Key.None, mouseButton = -1 };

        public bool IsKey => key != Key.None;
        public bool IsMouse => key == Key.None && mouseButton >= 0;
        public bool IsNone => key == Key.None && mouseButton < 0;

        public bool Equals(InputBinding o) => key == o.key && (key != Key.None || mouseButton == o.mouseButton);
        public override bool Equals(object obj) => obj is InputBinding b && Equals(b);
        public override int GetHashCode() => key != Key.None ? (int)key : 100000 + mouseButton;
    }

    /// The only place that reads devices (Input System package). Gameplay code must use these members.
    /// Default controls: WASD move, mouse look, Space up/jump, LeftCtrl down, LeftShift sprint, E interact,
    /// F lamp on/off, Tab phone, J journal, I backpack, Esc pause, 1–8 hotbar slots (round 3: one row),
    /// Q anchor, V push-to-talk, LMB use (record / cast), RMB aim, P photo, mouse wheel zoom.
    /// Bindings are stored in Settings.Data.bindings (only changed actions).
    public static class GameInput
    {
        static Keyboard K => Keyboard.current;
        static Mouse M => Mouse.current;

        public static float LookSensitivity = 0.08f;

        public static readonly int ActionCount = Enum.GetValues(typeof(GameAction)).Length;

        static readonly InputBinding[] Defaults = BuildDefaults();
        static readonly InputBinding[] bindings = (InputBinding[])Defaults.Clone();
        static readonly string[] labelCache = new string[ActionCount];
        static Language labelLanguage;
        static int captureIgnoreFrame = -1;

        /// Raised after SetBinding / ResetBindings / ApplyBindings (UI refreshes key hints).
        public static event Action BindingsChanged;

        static InputBinding[] BuildDefaults()
        {
            var d = new InputBinding[Enum.GetValues(typeof(GameAction)).Length];
            d[(int)GameAction.Forward] = InputBinding.FromKey(Key.W);
            d[(int)GameAction.Back] = InputBinding.FromKey(Key.S);
            d[(int)GameAction.Left] = InputBinding.FromKey(Key.A);
            d[(int)GameAction.Right] = InputBinding.FromKey(Key.D);
            d[(int)GameAction.Up] = InputBinding.FromKey(Key.Space);
            d[(int)GameAction.Down] = InputBinding.FromKey(Key.LeftCtrl);
            d[(int)GameAction.Sprint] = InputBinding.FromKey(Key.LeftShift);
            d[(int)GameAction.Jump] = InputBinding.FromKey(Key.Space);
            d[(int)GameAction.Interact] = InputBinding.FromKey(Key.E);
            d[(int)GameAction.LightToggle] = InputBinding.FromKey(Key.F);
            d[(int)GameAction.Phone] = InputBinding.FromKey(Key.Tab);
            d[(int)GameAction.Journal] = InputBinding.FromKey(Key.J);
            d[(int)GameAction.Inventory] = InputBinding.FromKey(Key.I);
            d[(int)GameAction.Pause] = InputBinding.FromKey(Key.Escape);
            d[(int)GameAction.RightSlot1] = InputBinding.FromKey(Key.Digit1);
            d[(int)GameAction.RightSlot2] = InputBinding.FromKey(Key.Digit2);
            d[(int)GameAction.RightSlot3] = InputBinding.FromKey(Key.Digit3);
            d[(int)GameAction.LeftSlot1] = InputBinding.FromKey(Key.Digit4);
            d[(int)GameAction.LeftSlot2] = InputBinding.FromKey(Key.Digit5);
            d[(int)GameAction.LeftSlot3] = InputBinding.FromKey(Key.Digit6);
            d[(int)GameAction.Anchor] = InputBinding.FromKey(Key.Q);
            d[(int)GameAction.PushToTalk] = InputBinding.FromKey(Key.V);
            d[(int)GameAction.Use] = InputBinding.FromMouse(0);
            d[(int)GameAction.Aim] = InputBinding.FromMouse(1);
            d[(int)GameAction.Photo] = InputBinding.FromKey(Key.P);
            d[(int)GameAction.StreamDare] = InputBinding.FromKey(Key.T);
            d[(int)GameAction.Selfie] = InputBinding.FromKey(Key.H);
            d[(int)GameAction.Slot7] = InputBinding.FromKey(Key.Digit7);
            d[(int)GameAction.Slot8] = InputBinding.FromKey(Key.Digit8);
            d[(int)GameAction.Gestures] = InputBinding.FromKey(Key.G);
            d[(int)GameAction.Map] = InputBinding.FromKey(Key.M);
            return d;
        }

        /// Round 3: the action of hotbar slot 0..7 (keys 1–8 by default).
        public static GameAction SlotAction(int slot)
        {
            if (slot <= 2) return (GameAction)((int)GameAction.RightSlot1 + Mathf.Max(0, slot));
            if (slot <= 5) return (GameAction)((int)GameAction.LeftSlot1 + slot - 3);
            return slot == 6 ? GameAction.Slot7 : GameAction.Slot8;
        }

        // ------------------------------------------------------------------ raw device access (allocation free)
        static KeyControl KeyCtl(Key k)
        {
            var kb = K;
            if (kb == null || k == Key.None) return null;
            try { return kb[k]; }
            catch (Exception) { return null; }   // a key that does not exist on this keyboard layout
        }

        static ButtonControl MouseCtl(int b)
        {
            var m = M;
            if (m == null) return null;
            switch (b)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.backButton;
                case 4: return m.forwardButton;
                default: return null;
            }
        }

        static ButtonControl Ctl(in InputBinding b) => b.key != Key.None ? KeyCtl(b.key) : MouseCtl(b.mouseButton);

        static bool HeldB(in InputBinding b) { var c = Ctl(b); return c != null && c.isPressed; }
        static bool DownB(in InputBinding b) { var c = Ctl(b); return c != null && c.wasPressedThisFrame; }
        static bool UpB(in InputBinding b) { var c = Ctl(b); return c != null && c.wasReleasedThisFrame; }

        static bool Held(Key k) { var c = KeyCtl(k); return c != null && c.isPressed; }
        static bool Down(Key k) { var c = KeyCtl(k); return c != null && c.wasPressedThisFrame; }
        static bool Up(Key k) { var c = KeyCtl(k); return c != null && c.wasReleasedThisFrame; }

        // ------------------------------------------------------------------ actions
        /// While a text field has focus (UIState.TextInput, L.4) every action except Pause reads as not pressed.
        /// Round 3: while the map overlay frees the cursor, the mouse actions (Use / Aim) belong to the map.
        static bool Blocked(GameAction a) => (UIState.TextInput && a != GameAction.Pause)
                                             || (UIState.MapOverlayOpen && (a == GameAction.Use || a == GameAction.Aim));

        public static bool Held(GameAction a) => !Blocked(a) && (HeldB(bindings[(int)a]) || (simActive && SimHeld((int)a)));
        public static bool Down(GameAction a) => !Blocked(a) && (DownB(bindings[(int)a]) || (simActive && simDown[(int)a] == Time.frameCount));
        public static bool Up(GameAction a) => !Blocked(a) && (UpB(bindings[(int)a]) || (simActive && simUp[(int)a] == Time.frameCount));

        // ------------------------------------------------------------------ simulated input (automated tests only)
        // AutoPlaytest drives the real gameplay code through these: a simulated action is OR-ed with the devices.
        // Calls made during frame N take effect in frame N+1 (coroutines resume after every Update of frame N).
        static readonly int[] simFrom = NewFrames(), simUntil = NewFrames(), simDown = NewFrames(), simUp = NewFrames();
        static bool simActive;
        static Vector2 simLook;
        static int simLookFrame = -1;

        static int[] NewFrames()
        {
            var a = new int[Enum.GetValues(typeof(GameAction)).Length];
            for (int i = 0; i < a.Length; i++) a[i] = -1;
            return a;
        }

        static bool SimHeld(int i) { int f = Time.frameCount; return simFrom[i] >= 0 && f >= simFrom[i] && f <= simUntil[i]; }

        /// Test only: holds an action from the next frame on (Down fires once in that frame) until SimRelease.
        public static void SimPress(GameAction a)
        {
            int i = (int)a, f = Time.frameCount + 1;
            simActive = true;
            simFrom[i] = f;
            simUntil[i] = int.MaxValue;
            simDown[i] = f;
        }

        /// Test only: releases a simulated action (Up fires in the next frame).
        public static void SimRelease(GameAction a)
        {
            int i = (int)a;
            if (simFrom[i] < 0) return;
            simUntil[i] = Time.frameCount;
            simUp[i] = Time.frameCount + 1;
        }

        /// Test only: one press of one frame (Down + Held in the next frame, Up in the one after).
        public static void SimTap(GameAction a)
        {
            int i = (int)a, f = Time.frameCount + 1;
            simActive = true;
            simFrom[i] = f;
            simUntil[i] = f;
            simDown[i] = f;
            simUp[i] = f + 1;
        }

        /// Test only: adds a look delta (degrees, after sensitivity) for the next frame.
        public static void SimLook(Vector2 degrees)
        {
            simActive = true;
            simLook = degrees;
            simLookFrame = Time.frameCount + 1;
        }

        /// Test only: drops every simulated action.
        public static void SimClear()
        {
            for (int i = 0; i < simFrom.Length; i++) { simFrom[i] = -1; simUntil[i] = -1; simDown[i] = -1; simUp[i] = -1; }
            simLookFrame = -1;
            simScrollFrame = -1;
            simActive = false;
        }

        /// Hotbar key of slot 0..Hands.SlotCount-1 pressed this frame.
        public static bool SlotPressed(int slot)
        {
            if (slot < 0 || slot >= Hands.SlotCount) return false;
            return Down(SlotAction(slot));
        }

        /// LEGACY (3 + 3 hotbar keys, used by the selfie expressions 1–6): right i = key i+1, left i = key i+4.
        public static bool SlotPressed(Hand hand, int slot)
        {
            if (slot < 0 || slot > 2) return false;
            return SlotPressed(hand == Hand.Right ? slot : 3 + slot);
        }

        public static Vector2 Move
        {
            get
            {
                float x = (Held(GameAction.Right) ? 1f : 0f) - (Held(GameAction.Left) ? 1f : 0f);
                float y = (Held(GameAction.Forward) ? 1f : 0f) - (Held(GameAction.Back) ? 1f : 0f);
                var v = new Vector2(x, y);
                return v.sqrMagnitude > 1f ? v.normalized : v;
            }
        }

        /// Mouse delta already multiplied by sensitivity (degrees-ish per frame).
        public static Vector2 Look
        {
            get
            {
                Vector2 sim = simActive && simLookFrame == Time.frameCount ? simLook : Vector2.zero;
                var m = M;
                if (m == null) return sim;
                var d = Settings.Data;
                Vector2 v = m.delta.ReadValue() * (LookSensitivity * d.mouseSensitivity);
                if (d.invertY) v.y = -v.y;
                return v + sim;
            }
        }

        public static bool Ascend => Held(GameAction.Up);
        public static bool Descend => Held(GameAction.Down);
        public static bool Sprint => Held(GameAction.Sprint);
        public static bool JumpPressed => Down(GameAction.Jump);
        public static bool InteractPressed => Down(GameAction.Interact);
        /// Lamp on/off (only meaningful while the flashlight is held, see Hands).
        public static bool FlashlightPressed => Down(GameAction.LightToggle);
        public static bool PhonePressed => Down(GameAction.Phone);
        public static bool JournalPressed => Down(GameAction.Journal);
        public static bool InventoryPressed => Down(GameAction.Inventory);
        /// Escape always pauses / closes (it cannot be bound to anything else) plus the Pause binding.
        public static bool PausePressed => Down(Key.Escape) || Down(GameAction.Pause);
        public static bool AnchorPressed => Down(GameAction.Anchor);
        /// Round 3: the treasure-map overlay key (default M).
        public static bool MapPressed => Down(GameAction.Map);
        public static bool PhotoPressed => Down(GameAction.Photo);
        public static bool PushToTalkHeld => Held(GameAction.PushToTalk);

        public static bool AimHeld => Held(GameAction.Aim);
        public static bool AimPressed => Down(GameAction.Aim);
        /// "Use" pressed: start/stop recording with the camera, cast/strike with the rod.
        public static bool RecordPressed => Down(GameAction.Use);
        public static bool UseHeld => Held(GameAction.Use);
        public static bool UsePressed => Down(GameAction.Use);
        public static bool UseReleased => Up(GameAction.Use);
        public static bool PrimaryHeld => Held(GameAction.Use);
        public static bool PrimaryPressed => Down(GameAction.Use);
        public static bool PrimaryReleased => Up(GameAction.Use);

        /// -1, 0 or +1 per frame.
        public static float ZoomStep
        {
            get
            {
                var m = M;
                if (m == null) return 0f;
                float y = m.scroll.ReadValue().y;
                return y > 0.01f ? 1f : y < -0.01f ? -1f : 0f;
            }
        }

        /// Q17 hotbar wheel: +1 = next slot (wheel down), −1 = previous slot (wheel up), 0 = none. 0 while a text field
        /// or a modal UI has focus. The camera's aim zoom keeps using ZoomStep (Hands.HandleInput skips the wheel while aiming).
        public static int HotbarScroll
        {
            get
            {
                if (UIState.TextInput || UIState.BlocksGameplay || UIState.MapOverlayOpen) return 0;
                int sim = simActive && simScrollFrame == Time.frameCount ? simScroll : 0;
                if (sim != 0) return sim;
                var m = M;
                if (m == null) return 0;
                float y = m.scroll.ReadValue().y;
                return y < -0.01f ? 1 : y > 0.01f ? -1 : 0;
            }
        }

        /// Shift+wheel cycles the left hand (Q17) – raw Shift keys, independent of the sprint binding.
        public static bool HotbarScrollLeftHand => Held(Key.LeftShift) || Held(Key.RightShift);

        static int simScroll, simScrollFrame = -1;

        /// Test only: one hotbar wheel step (+1 next / −1 previous) in the next frame.
        public static void SimScroll(int dir)
        {
            simActive = true;
            simScroll = dir > 0 ? 1 : dir < 0 ? -1 : 0;
            simScrollFrame = Time.frameCount + 1;
        }

        /// Selfie view toggle (K.4, default H).
        public static bool SelfiePressed => Down(GameAction.Selfie);
        /// Accept a livestream dare (C.5, default T).
        public static bool StreamDarePressed => Down(GameAction.StreamDare);

        public static bool KeyHeld(Key k) => Held(k);
        public static bool KeyDown(Key k) => Down(k);
        public static bool KeyUp(Key k) => Up(k);

        /// Raw: any keyboard key or the left mouse button (title screens, skipping the intro).
        public static bool AnyKeyDown
        {
            get
            {
                var kb = K;
                if (kb != null && kb.anyKey.wasPressedThisFrame) return true;
                var m = M;
                return m != null && m.leftButton.wasPressedThisFrame;
            }
        }

        // ------------------------------------------------------------------ bindings
        public static InputBinding GetBinding(GameAction a) => bindings[(int)a];
        public static InputBinding GetDefaultBinding(GameAction a) => Defaults[(int)a];
        public static bool IsDefault(GameAction a) => bindings[(int)a].Equals(Defaults[(int)a]);

        /// Rebinds an action and saves the settings. Duplicates are allowed (see FindConflict for a UI warning).
        public static void SetBinding(GameAction a, InputBinding b)
        {
            if ((int)a < 0 || (int)a >= ActionCount) return;
            if (b.key == Key.Escape) return;   // reserved for pause / cancel
            bindings[(int)a] = b;
            WriteToSettings();
            Settings.Save();
            InvalidateLabels();
            BindingsChanged?.Invoke();
        }

        public static void ResetBindings()
        {
            Array.Copy(Defaults, bindings, bindings.Length);
            WriteToSettings();
            Settings.Save();
            InvalidateLabels();
            BindingsChanged?.Invoke();
        }

        /// Another action already using binding b (Up/Jump sharing Space is intentional and ignored).
        public static bool FindConflict(GameAction a, InputBinding b, out GameAction other)
        {
            for (int i = 0; i < ActionCount; i++)
            {
                var o = (GameAction)i;
                if (o == a || !bindings[i].Equals(b)) continue;
                if ((a == GameAction.Up && o == GameAction.Jump) || (a == GameAction.Jump && o == GameAction.Up)) continue;
                other = o;
                return true;
            }
            other = a;
            return false;
        }

        /// Loads stored overrides (called by Settings.Load). Unknown or invalid entries fall back to the default.
        public static void ApplyBindings(List<BindingEntry> list)
        {
            Array.Copy(Defaults, bindings, bindings.Length);
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    if (e == null || string.IsNullOrEmpty(e.action)) continue;
                    if (!Enum.TryParse(e.action, out GameAction a) || (int)a < 0 || (int)a >= ActionCount) continue;
                    Key k = Key.None;
                    if (!string.IsNullOrEmpty(e.key) && e.key != "None" && (!Enum.TryParse(e.key, out k) || !Enum.IsDefined(typeof(Key), k))) continue;
                    if (k == Key.Escape) continue;
                    var b = k != Key.None ? InputBinding.FromKey(k) : (e.mouseButton >= 0 && e.mouseButton <= 4 ? InputBinding.FromMouse(e.mouseButton) : InputBinding.None);
                    bindings[(int)a] = b;
                }
            }
            InvalidateLabels();
            BindingsChanged?.Invoke();
        }

        static void WriteToSettings()
        {
            var list = Settings.Data.bindings;
            list.Clear();
            for (int i = 0; i < ActionCount; i++)
            {
                if (bindings[i].Equals(Defaults[i])) continue;
                var b = bindings[i];
                list.Add(new BindingEntry { action = ((GameAction)i).ToString(), key = b.key.ToString(), mouseButton = b.key == Key.None ? b.mouseButton : -1 });
            }
        }

        /// Call when the "press a key" prompt opens: the click/key that opened it is ignored this frame.
        public static void BeginCapture() => captureIgnoreFrame = Time.frameCount;

        /// Poll while waiting for a new binding. Returns true once something was pressed:
        /// b is the pressed key / mouse button, or b.IsNone when the player cancelled with Escape.
        public static bool TryCaptureBinding(out InputBinding b)
        {
            b = InputBinding.None;
            if (Time.frameCount == captureIgnoreFrame) return false;
            var kb = K;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) return true;   // cancelled
                if (kb.anyKey.wasPressedThisFrame)
                {
                    var keys = kb.allKeys;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        var kc = keys[i];
                        if (kc != null && kc.wasPressedThisFrame && kc.keyCode != Key.None)
                        {
                            b = InputBinding.FromKey(kc.keyCode);
                            return true;
                        }
                    }
                }
            }
            for (int i = 0; i <= 4; i++)
            {
                var c = MouseCtl(i);
                if (c != null && c.wasPressedThisFrame)
                {
                    b = InputBinding.FromMouse(i);
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ labels
        /// Short display text of an action's binding ("E", "Leertaste", "LMT" …). Cached; safe to call every frame.
        public static string BindingLabel(GameAction a)
        {
            if ((int)a < 0 || (int)a >= ActionCount) return "";
            if (labelLanguage != Loc.Current) InvalidateLabels();
            var s = labelCache[(int)a];
            if (s == null) labelCache[(int)a] = s = Label(bindings[(int)a]);
            return s;
        }

        /// Incremented whenever the bindings change (cheap cache key for texts that contain key labels).
        public static int BindingsVersion { get; private set; }

        static void InvalidateLabels()
        {
            Array.Clear(labelCache, 0, labelCache.Length);
            labelLanguage = Loc.Current;
            BindingsVersion++;
        }

        /// Display text of any binding (allocates; prefer BindingLabel(GameAction) for HUD hints).
        public static string Label(InputBinding b)
        {
            if (b.IsNone) return "–";
            if (b.IsMouse)
            {
                switch (b.mouseButton)
                {
                    case 0: return Loc.Tr("LMT", "LMB");
                    case 1: return Loc.Tr("RMT", "RMB");
                    case 2: return Loc.Tr("MMT", "MMB");
                    case 3: return Loc.Tr("Maus 4", "Mouse 4");
                    default: return Loc.Tr("Maus 5", "Mouse 5");
                }
            }
            switch (b.key)
            {
                case Key.Space: return Loc.Tr("Leertaste", "Space");
                case Key.LeftShift: case Key.RightShift: return "Shift";
                case Key.LeftCtrl: case Key.RightCtrl: return Loc.Tr("Strg", "Ctrl");
                case Key.LeftAlt: case Key.RightAlt: return "Alt";
                case Key.Escape: return "Esc";
                case Key.Tab: return "Tab";
                case Key.Enter: case Key.NumpadEnter: return "Enter";
                case Key.Backspace: return Loc.Tr("Rück", "Backspace");
                case Key.CapsLock: return Loc.Tr("Feststell", "Caps");
                case Key.UpArrow: return Loc.Tr("Pfeil hoch", "Up");
                case Key.DownArrow: return Loc.Tr("Pfeil runter", "Down");
                case Key.LeftArrow: return Loc.Tr("Pfeil links", "Left");
                case Key.RightArrow: return Loc.Tr("Pfeil rechts", "Right");
                case Key.Digit0: return "0";
                case Key.Digit1: return "1";
                case Key.Digit2: return "2";
                case Key.Digit3: return "3";
                case Key.Digit4: return "4";
                case Key.Digit5: return "5";
                case Key.Digit6: return "6";
                case Key.Digit7: return "7";
                case Key.Digit8: return "8";
                case Key.Digit9: return "9";
            }
            var c = KeyCtl(b.key);
            string n = c != null ? c.displayName : null;
            if (string.IsNullOrEmpty(n)) n = b.key.ToString();
            return n.Length == 1 ? n.ToUpperInvariant() : n;
        }

        /// Localized name of an action for the controls screen.
        public static string ActionName(GameAction a)
        {
            switch (a)
            {
                case GameAction.Forward: return Loc.Tr("Vorwärts", "Forward");
                case GameAction.Back: return Loc.Tr("Rückwärts", "Back");
                case GameAction.Left: return Loc.Tr("Links", "Left");
                case GameAction.Right: return Loc.Tr("Rechts", "Right");
                case GameAction.Up: return Loc.Tr("Auftauchen", "Swim up");
                case GameAction.Down: return Loc.Tr("Abtauchen / Ducken", "Swim down / Crouch");
                case GameAction.Sprint: return Loc.Tr("Sprinten", "Sprint");
                case GameAction.Jump: return Loc.Tr("Springen", "Jump");
                case GameAction.Interact: return Loc.Tr("Benutzen", "Interact");
                case GameAction.LightToggle: return Loc.Tr("Lampe an/aus", "Lamp on/off");
                case GameAction.Phone: return Loc.Tr("Handy", "Phone");
                case GameAction.Journal: return "Sealog";
                case GameAction.Inventory: return Loc.Tr("Rucksack", "Backpack");
                case GameAction.Pause: return Loc.Tr("Pause", "Pause");
                case GameAction.RightSlot1: return Loc.Tr("Leiste 1", "Hotbar 1");
                case GameAction.RightSlot2: return Loc.Tr("Leiste 2", "Hotbar 2");
                case GameAction.RightSlot3: return Loc.Tr("Leiste 3", "Hotbar 3");
                case GameAction.LeftSlot1: return Loc.Tr("Leiste 4", "Hotbar 4");
                case GameAction.LeftSlot2: return Loc.Tr("Leiste 5", "Hotbar 5");
                case GameAction.LeftSlot3: return Loc.Tr("Leiste 6", "Hotbar 6");
                case GameAction.Slot7: return Loc.Tr("Leiste 7", "Hotbar 7");
                case GameAction.Slot8: return Loc.Tr("Leiste 8", "Hotbar 8");
                case GameAction.Gestures: return Loc.Tr("Gesten-Rad (halten)", "Gesture wheel (hold)");
                case GameAction.Map: return Loc.Tr("Karte (auch beim Fahren)", "Map (also while driving)");
                case GameAction.Anchor: return Loc.Tr("Anker", "Anchor");
                case GameAction.PushToTalk: return Loc.Tr("Sprechen (Push-to-Talk)", "Push to talk");
                case GameAction.Use: return Loc.Tr("Benutzen (Filmen, Auswerfen, Schlag)", "Use (record, cast, strike)");
                case GameAction.Aim: return Loc.Tr("Zielen", "Aim");
                case GameAction.StreamDare: return Loc.Tr("Mutprobe annehmen", "Accept dare");
                case GameAction.Selfie: return Loc.Tr("Selfie-Ansicht", "Selfie view");
                default: return Loc.Tr("Foto", "Photo");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Array.Copy(Defaults, bindings, bindings.Length);
            Array.Clear(labelCache, 0, labelCache.Length);
            captureIgnoreFrame = -1;
            BindingsChanged = null;
            SimClear();
        }
    }
}
