using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Tracks focused text fields so keyboard shortcuts (Tab, I, J …) are ignored while typing. While a field has focus
    /// UIState.TextInput is true (L.4: GameInput then ignores every action except Pause); Enter submits (onSubmit) and
    /// Esc leaves the field – both handled by UIController.
    public static class UITextInput
    {
        static TextField current;
        static Action currentSubmit;

        public static bool Active
        {
            get
            {
                bool a = current != null && current.panel != null;
                if (!a && UIState.TextInput) UIState.TextInput = false;   // a field vanished without a focus-out
                return a;
            }
        }

        public static void Track(TextField f, Action onSubmit = null)
        {
            if (f == null) return;
            f.RegisterCallback<FocusInEvent>(_ => { current = f; currentSubmit = onSubmit; UIState.TextInput = true; });
            f.RegisterCallback<FocusOutEvent>(_ => Release(f));
            f.RegisterCallback<DetachFromPanelEvent>(_ => Release(f));
        }

        static void Release(TextField f)
        {
            if (current != f) return;
            current = null;
            currentSubmit = null;
            UIState.TextInput = false;
        }

        /// Ends typing (Esc).
        public static void Blur()
        {
            var f = current;
            current = null;
            currentSubmit = null;
            UIState.TextInput = false;
            if (f != null) f.Blur();
        }

        /// Enter: runs the field's submit action (e.g. FinTube upload), then leaves the field.
        public static void Submit()
        {
            var a = currentSubmit;
            Blur();
            try { a?.Invoke(); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// Styled single-line text field (light card look).
        public static TextField Field(string value, int maxLength, float fontSize, string placeholder = null)
        {
            var field = new TextField { maxLength = maxLength, value = value ?? "" };
            field.style.fontSize = fontSize;
            field.style.height = fontSize * 2.1f;
            field.style.marginLeft = 0f; field.style.marginRight = 0f;
            var input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = Color.white;
                input.style.color = UIPalette.Ink;
                UIKit.Radius(input, 14f);
                UIKit.Border(input, 2f, UIPalette.A(UIPalette.Ink, 0.14f));
                UIKit.Pad(input, 4f, 14f, 4f, 14f);
            }
            if (!string.IsNullOrEmpty(placeholder))
            {
                try { field.textEdition.placeholder = placeholder; field.textEdition.hidePlaceholderOnFocus = true; }
                catch (Exception) { }
            }
            return field;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { current = null; currentSubmit = null; }
    }

    /// Per-viewer UI preferences that are not part of SettingsData (PlayerPrefs, guarded).
    public static class UIPrefs
    {
        const string PhoneFlatKey = "sb_phone_flat";

        /// True: show the phone as a flat 2D overlay instead of the 3D phone in the hand (fallback / preference).
        public static bool PhoneFlat
        {
            get { try { return PlayerPrefs.GetInt(PhoneFlatKey, 0) == 1; } catch (Exception) { return false; } }
            set { try { PlayerPrefs.SetInt(PhoneFlatKey, value ? 1 : 0); PlayerPrefs.Save(); } catch (Exception) { } }
        }
    }

    /// Settings screen (main menu, pause menu and the phone's settings app). Writes Settings.Data, then
    /// Settings.Apply() + Settings.Save(). compact = phone layout (narrow, bigger text, icon tabs).
    /// Every slider label goes through UIFormat (1e). The character look lives on the character screen (1c) – the
    /// co-op tab keeps voice chat only and links to the screen.
    public class UISettingsView
    {
        enum Tab { Audio, Video, Controls, Game, Coop }

        /// True while waiting for a key press for a rebinding (UIController ignores its own shortcuts then).
        public static bool CaptureActive { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { CaptureActive = false; captureOwner = null; }

        static UISettingsView captureOwner;

        public readonly VisualElement Root;
        readonly VisualElement tabsRow, content, footer;
        readonly bool compact;
        readonly float fs;
        Tab tab;
        bool dirty;
        GameAction captureAction;

        /// Main menu only (Q42): shows „Intro ansehen“ in the footer.
        public Action OnWatchIntro;
        /// Shows „Charakter anpassen“ on the co-op tab (null = hidden, e.g. inside the phone).
        public Action OnOpenCharacter;

        public UISettingsView(bool compact)
        {
            this.compact = compact;
            fs = compact ? 26f : 19f;
            Root = UIKit.Col();
            Root.style.flexGrow = 1;
            tabsRow = UIKit.Row();
            tabsRow.style.marginBottom = compact ? 18f : 14f;
            tabsRow.style.flexShrink = 0;
            Root.Add(tabsRow);
            content = UIKit.Col();
            content.style.flexShrink = 0;
            if (compact)
            {
                Root.Add(content);
            }
            else
            {
                var sv = UIKit.Scroll();
                sv.style.maxHeight = 560f;
                sv.contentContainer.Add(content);
                Root.Add(sv);
            }
            footer = UIKit.Row();
            footer.style.marginTop = 10f;
            footer.style.flexShrink = 0;
            Root.Add(footer);
            Rebuild();
        }

        public void Refresh() { dirty = true; }

        public void Tick(float dt)
        {
            if (CaptureActive && captureOwner == this)
            {
                if (GameInput.TryCaptureBinding(out var b))
                {
                    CaptureActive = false;
                    captureOwner = null;
                    if (!b.IsNone)
                    {
                        GameInput.SetBinding(captureAction, b);
                        UIAudio.Click();
                    }
                    else UIAudio.Nope();
                    dirty = true;
                }
            }
            if (dirty) Rebuild();
        }

        /// Cancel a pending key capture (screen closed).
        public void CancelCapture()
        {
            if (captureOwner != this) return;
            CaptureActive = false;
            captureOwner = null;
        }

        void Rebuild()
        {
            dirty = false;
            BuildTabs();
            content.Clear();
            switch (tab)
            {
                case Tab.Audio: BuildAudio(); break;
                case Tab.Video: BuildVideo(); break;
                case Tab.Controls: BuildControls(); break;
                case Tab.Game: BuildGame(); break;
                default: BuildCoop(); break;
            }
            BuildFooter();
        }

        void BuildFooter()
        {
            footer.Clear();
            bool inGame = GameManager.I != null && GameManager.I.InGame;
            if (OnWatchIntro != null && !inGame)
            {
                var b = UIKit.Btn("", () => { CancelCapture(); OnWatchIntro?.Invoke(); }, UIBtnKind.Ghost, fs * 0.85f);
                b.style.flexDirection = FlexDirection.Row;
                b.style.alignItems = Align.Center;
                UIKit.Pad(b, 8f, 14f, 8f, 10f);
                b.Add(new UIGlyph(UIGlyphs.Play, UIPalette.InkSoft, fs * 1.1f));
                var l = UIKit.Text(Loc.Tr("Intro ansehen", "Watch intro"), fs * 0.85f, UIPalette.InkSoft, true);
                l.style.marginLeft = 8f;
                l.pickingMode = PickingMode.Ignore;
                b.Add(l);
                footer.Add(b);
            }
            UIKit.Show(footer, footer.childCount > 0);
        }

        // ------------------------------------------------------------------ tabs
        void BuildTabs()
        {
            tabsRow.Clear();
            AddTab(Tab.Audio, UIGlyphs.Speaker, Loc.Tr("Audio", "Audio"));
            AddTab(Tab.Video, UIGlyphs.Monitor, Loc.Tr("Grafik", "Graphics"));
            AddTab(Tab.Controls, UIGlyphs.Keyboard, Loc.Tr("Tasten", "Keys"));
            AddTab(Tab.Game, UIGlyphs.Sliders, Loc.Tr("Spiel", "Game"));
            AddTab(Tab.Coop, UIGlyphs.Crew, Loc.Tr("Koop", "Co-op"));
        }

        void AddTab(Tab t, UIGlyph.DrawFn icon, string label)
        {
            bool sel = t == tab;
            var b = UIKit.Col();
            b.style.alignItems = Align.Center;
            b.style.flexGrow = 1;
            b.style.flexBasis = 0;
            UIKit.Radius(b, 16f);
            UIKit.Pad(b, compact ? 12f : 10f, 6f, compact ? 10f : 8f, 6f);
            if (t != Tab.Audio) b.style.marginLeft = 8f;
            Color fg = sel ? Color.white : UIPalette.InkSoft;
            b.Add(new UIGlyph(icon, fg, compact ? 40f : 30f));
            var l = UIKit.Text(label, compact ? 20f : 16f, fg, true);
            l.style.marginTop = 4f;
            b.Add(l);
            UIKit.IgnorePicking(b);
            b.pickingMode = PickingMode.Position;
            UIKit.Clickable(b, () => { if (!CaptureActive) { tab = t; dirty = true; } }, sel ? UIPalette.Turquoise : Color.white, sel ? UIPalette.Turquoise : UIPalette.CardAlt);
            tabsRow.Add(b);
        }

        // ------------------------------------------------------------------ building blocks
        VisualElement Card()
        {
            var c = UIKit.Card(Color.white, 18f, compact ? 18f : 16f);
            c.style.marginBottom = 10f;
            return c;
        }

        VisualElement LabelRow(UIGlyph.DrawFn icon, string label)
        {
            var row = UIKit.Row();
            if (icon != null)
            {
                var g = new UIGlyph(icon, UIPalette.InkSoft, fs * 1.25f);
                g.style.marginRight = fs * 0.5f;
                row.Add(g);
            }
            var l = UIKit.Text(label, fs, UIPalette.Ink, true);
            l.style.flexShrink = 1;
            row.Add(l);
            row.Add(UIKit.Flex());
            return row;
        }

        /// Volume 0..1 shown as a whole percent (UIFormat.Percent). Only finite, clamped values are written (UISlider
        /// guarantees it, the label and the stored value are clamped again); saved when the slider is released.
        VisualElement VolumeSlider(UIGlyph.DrawFn icon, string label, float value, Action<float> set, bool master, float fallback)
        {
            var c = Card();
            var head = LabelRow(icon, label);
            value = UIFormat.Clamp(value, 0f, 1f, fallback);
            var pct = UIKit.Text(UIFormat.Percent(value), fs, UIPalette.TurquoiseInk, true);
            pct.style.minWidth = fs * 3.6f;
            pct.style.unityTextAlign = TextAnchor.MiddleRight;
            head.Add(pct);
            c.Add(head);
            var s = new UISlider(0f, 1f, value, v =>
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) return;
                v = Mathf.Clamp01(v);
                set(v);
                pct.text = UIFormat.Percent(v);
                if (master) AudioListener.volume = v;
            }, UIPalette.Turquoise, _ => { Settings.Apply(); Settings.Save(); }, fs * 1.8f);
            s.style.marginTop = 6f;
            c.Add(s);
            return c;
        }

        VisualElement ToggleRow(UIGlyph.DrawFn icon, string label, bool value, Action<bool> set)
        {
            var c = Card();
            var row = LabelRow(icon, label);
            row.Add(new UIToggle(value, v => { set(v); Settings.Apply(); Settings.Save(); }, UIPalette.Turquoise, fs * 1.5f));
            c.Add(row);
            return c;
        }

        VisualElement SegmentRow(UIGlyph.DrawFn icon, string label, string[] options, int selected, Action<int> set)
        {
            var c = Card();
            c.Add(LabelRow(icon, label));
            var seg = new UISegmented(options, selected, i => { set(i); Settings.Apply(); Settings.Save(); }, UIPalette.Turquoise, fs * 0.85f);
            seg.style.marginTop = 8f;
            c.Add(seg);
            return c;
        }

        // ------------------------------------------------------------------ tabs content
        void BuildAudio()
        {
            var d = Settings.Data;
            content.Add(VolumeSlider(UIGlyphs.Speaker, Loc.Tr("Gesamt", "Master"), d.master, v => d.master = v, true, Settings.DefaultMaster));
            content.Add(VolumeSlider(UIGlyphs.Note, Loc.Tr("Musik", "Music"), d.music, v => d.music = v, false, Settings.DefaultMusic));
            content.Add(VolumeSlider(UIGlyphs.Bell, Loc.Tr("Effekte", "Effects"), d.sfx, v => d.sfx = v, false, Settings.DefaultSfx));
            content.Add(VolumeSlider(UIGlyphs.Mic, Loc.Tr("Stimmen", "Voices"), d.voice, v => d.voice = v, false, Settings.DefaultVoice));
            content.Add(ToggleRow(UIGlyphs.Bubbles, Loc.Tr("Musik beim Tauchen", "Music while diving"), d.musicWhileDiving, v => d.musicWhileDiving = v));
        }

        void BuildVideo()
        {
            var d = Settings.Data;
            content.Add(SegmentRow(UIGlyphs.Monitor, Loc.Tr("Grafik", "Graphics"),
                new[] { Loc.Tr("Niedrig", "Low"), Loc.Tr("Mittel", "Medium"), Loc.Tr("Hoch", "High") }, (int)d.quality,
                i => d.quality = (GraphicsQuality)i));
            content.Add(FovSlider());
            var opts = Settings.FpsOptions;
            var labels = new string[opts.Length];
            int sel = opts.Length - 1;
            for (int i = 0; i < opts.Length; i++)
            {
                labels[i] = opts[i] <= 0 ? "∞" : opts[i].ToString();
                if (opts[i] == d.fpsLimit) sel = i;
            }
            content.Add(SegmentRow(UIGlyphs.Clock, Loc.Tr("FPS-Limit", "FPS limit"), labels, sel, i => d.fpsLimit = Settings.FpsOptions[i]));
            content.Add(ToggleRow(UIGlyphs.Monitor, "VSync", d.vsync, v => d.vsync = v));
        }

        /// Q98 field of view 60–100° (default 70): saved on release, applied live by the Player module (Settings.Changed).
        VisualElement FovSlider()
        {
            var d = Settings.Data;
            var c = Card();
            var head = LabelRow(UIGlyphs.Eye, Loc.Tr("Sichtfeld", "Field of view"));
            float v0 = UIFormat.Clamp(d.fov, Settings.MinFov, Settings.MaxFov, Settings.DefaultFov);
            var val = UIKit.Text(UIFormat.Degrees(v0, Settings.MinFov, Settings.MaxFov), fs, UIPalette.TurquoiseInk, true);
            val.style.minWidth = fs * 3.2f;
            val.style.unityTextAlign = TextAnchor.MiddleRight;
            head.Add(val);
            c.Add(head);
            var s = new UISlider(Settings.MinFov, Settings.MaxFov, v0, v =>
            {
                if (float.IsNaN(v) || float.IsInfinity(v)) return;
                v = Mathf.Clamp(Mathf.Round(v), Settings.MinFov, Settings.MaxFov);
                d.fov = v;
                val.text = UIFormat.Degrees(v, Settings.MinFov, Settings.MaxFov);
                Settings.MarkDirty();
            }, UIPalette.Turquoise, _ => { Settings.Apply(); Settings.Save(); }, fs * 1.8f);
            s.style.marginTop = 6f;
            c.Add(s);
            return c;
        }

        void BuildControls()
        {
            var d = Settings.Data;
            var sens = Card();
            sens.Add(UIWidgets.SensitivityRow(fs));
            content.Add(sens);
            content.Add(ToggleRow(UIGlyphs.Arrow, Loc.Tr("Maus-Y umkehren", "Invert mouse Y"), d.invertY, v => d.invertY = v));

            var list = Card();
            var head = LabelRow(UIGlyphs.Keyboard, Loc.Tr("Tastenbelegung", "Key bindings"));
            var reset = UIKit.Btn(Loc.Tr("Zurücksetzen", "Reset"), () =>
            {
                if (CaptureActive) return;
                GameInput.ResetBindings();
                dirty = true;
            }, UIBtnKind.Secondary, fs * 0.75f);
            UIKit.Pad(reset, 6f, 12f, 6f, 12f);
            head.Add(reset);
            list.Add(head);
            for (int i = 0; i < GameInput.ActionCount; i++)
            {
                var a = (GameAction)i;
                if (a == GameAction.Pause) continue;   // Esc always pauses
                list.Add(BindingRow(a));
            }
            content.Add(list);
        }

        VisualElement BindingRow(GameAction a)
        {
            var row = UIKit.Row();
            row.style.paddingTop = 6f; row.style.paddingBottom = 6f;
            row.style.borderTopWidth = 1f;
            row.style.borderTopColor = UIPalette.Line;
            var name = UIKit.Text(GameInput.ActionName(a), fs * 0.85f, UIPalette.Ink);
            name.style.flexShrink = 1;
            name.style.flexGrow = 1;
            row.Add(name);
            bool capturing = CaptureActive && captureOwner == this && captureAction == a;
            if (!capturing && GameInput.FindConflict(a, GameInput.GetBinding(a), out var other))
            {
                var warn = UIKit.Text(Loc.Tr("auch: ", "also: ") + GameInput.ActionName(other), fs * 0.62f, UIPalette.CoralInk, true);
                warn.style.marginRight = 8f;
                warn.style.maxWidth = compact ? 150f : 260f;
                warn.style.whiteSpace = WhiteSpace.Normal;
                row.Add(warn);
            }
            var key = UIKit.KeyCap(capturing ? Loc.Tr("Taste drücken …", "Press a key …") : GameInput.BindingLabel(a), fs * 0.8f);
            key.style.minWidth = compact ? 120f : 110f;
            if (capturing) key.style.backgroundColor = UIPalette.Sun;
            key.pickingMode = PickingMode.Position;
            key.RegisterCallback<ClickEvent>(_ =>
            {
                if (CaptureActive) return;
                UIAudio.Click();
                captureAction = a;
                captureOwner = this;
                CaptureActive = true;
                GameInput.BeginCapture();
                dirty = true;
            });
            key.RegisterCallback<PointerEnterEvent>(_ => { if (!CaptureActive) key.style.backgroundColor = UIPalette.CardAlt; });
            key.RegisterCallback<PointerLeaveEvent>(_ => { if (!CaptureActive) key.style.backgroundColor = Color.white; });
            row.Add(key);
            return row;
        }

        void BuildGame()
        {
            var d = Settings.Data;
            var lang = Card();
            lang.Add(LabelRow(UIGlyphs.Globe, Loc.Tr("Sprache", "Language")));
            var seg = UIWidgets.LanguageSelector(fs * 0.85f);
            seg.style.marginTop = 8f;
            lang.Add(seg);
            content.Add(lang);

            // L.10 task display: Normal / Small / Off
            content.Add(SegmentRow(UIGlyphs.Tasks, Loc.Tr("Aufgaben-Anzeige", "Task display"),
                new[] { Loc.Tr("Normal", "Normal"), Loc.Tr("Klein", "Small"), Loc.Tr("Aus", "Off") }, Mathf.Clamp(d.questDisplay, 0, 2),
                i => d.questDisplay = i));
            // Q99 toggles
            content.Add(ToggleRow(UIGlyphs.Wave, Loc.Tr("Kopfwippen", "Head bob"), d.headBob, v => d.headBob = v));
            content.Add(ToggleRow(UIGlyphs.Camera, Loc.Tr("Kamerawackeln", "Camera shake"), d.cameraShake, v => d.cameraShake = v));
            // K.2 auto-record
            content.Add(ToggleRow(UIGlyphs.Rec, Loc.Tr("Unter Wasser automatisch filmen", "Auto-record under water"), d.autoRecordUnderwater, v => d.autoRecordUnderwater = v));
            content.Add(ToggleRow(UIGlyphs.Bubble, Loc.Tr("Untertitel", "Subtitles"), d.subtitles, v => d.subtitles = v));
            content.Add(ToggleRow(UIGlyphs.Person, Loc.Tr("Tutorial-Hilfe von Horst", "Tutorial help from Horst"), d.npcTutorial, v => d.npcTutorial = v));

            var phone = Card();
            phone.Add(LabelRow(UIGlyphs.Phone, Loc.Tr("Handy-Ansicht", "Phone view")));
            var pseg = new UISegmented(new[] { Loc.Tr("In der Hand (3D)", "In hand (3D)"), Loc.Tr("Flach", "Flat") }, UIPrefs.PhoneFlat ? 1 : 0,
                i => UIPrefs.PhoneFlat = i == 1, UIPalette.Turquoise, fs * 0.8f);
            pseg.style.marginTop = 8f;
            phone.Add(pseg);
            content.Add(phone);

            var gm = GameManager.I;
            if (gm != null && gm.InGame)
            {
                var diff = Card();
                var row = LabelRow(UIGlyphs.ForDifficulty(gm.State.difficulty), Loc.Tr("Schwierigkeit", "Difficulty"));
                var dn = UIKit.Pill(Loc.DifficultyName(gm.State.difficulty), UIPalette.DifficultyColor(gm.State.difficulty), null, fs * 0.75f);
                row.Add(dn);
                diff.Add(row);
                content.Add(diff);
            }
        }

        void BuildCoop()
        {
            var d = Settings.Data;
            // the look and the name live on the character screen (1c)
            if (OnOpenCharacter != null)
            {
                var cc = Card();
                var row = LabelRow(UIGlyphs.Person, Settings.DisplayName);
                var b = UIKit.Btn(Loc.Tr("Charakter", "Character"), () => { CancelCapture(); OnOpenCharacter?.Invoke(); }, UIBtnKind.Accent, fs * 0.8f);
                UIKit.Pad(b, 8f, 16f, 8f, 16f);
                row.Add(b);
                cc.Add(row);
                content.Add(cc);
            }

            // voice
            content.Add(SegmentRow(UIGlyphs.Mic, Loc.Tr("Sprachchat", "Voice chat"),
                new[] { "Push-to-Talk", Loc.Tr("Offenes Mikro", "Open mic"), Loc.Tr("Aus", "Off") }, (int)d.voiceMode, i => d.voiceMode = (VoiceMode)i));
            var ptt = Card();
            var pttRow = LabelRow(UIGlyphs.Keyboard, Loc.Tr("Sprechtaste", "Talk key"));
            pttRow.Add(UIKit.KeyCap(GameInput.BindingLabel(GameAction.PushToTalk), fs * 0.8f));
            ptt.Add(pttRow);
            content.Add(ptt);
            content.Add(ToggleRow(UIGlyphs.MicOff, Loc.Tr("Mikro stumm", "Mute mic"), d.micMuted, v => d.micMuted = v));
        }
    }
}
