using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Tracks focused text fields so keyboard shortcuts (Tab, I, J …) are ignored while typing.
    public static class UITextInput
    {
        static TextField current;

        public static bool Active => current != null && current.panel != null;

        public static void Track(TextField f)
        {
            if (f == null) return;
            f.RegisterCallback<FocusInEvent>(_ => current = f);
            f.RegisterCallback<FocusOutEvent>(_ => { if (current == f) current = null; });
            f.RegisterCallback<DetachFromPanelEvent>(_ => { if (current == f) current = null; });
        }

        /// Ends typing (Esc).
        public static void Blur()
        {
            var f = current;
            current = null;
            if (f != null) f.Blur();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { current = null; }
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
    public class UISettingsView
    {
        enum Tab { Audio, Video, Controls, Game, Coop }

        /// True while waiting for a key press for a rebinding (UIController ignores its own shortcuts then).
        public static bool CaptureActive { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { CaptureActive = false; captureOwner = null; }

        static UISettingsView captureOwner;

        public readonly VisualElement Root;
        readonly VisualElement tabsRow, content;
        readonly bool compact;
        readonly float fs;
        Tab tab;
        bool dirty;
        GameAction captureAction;
        Label captureLabel;

        static readonly Color[] SuitColors =
        {
            UIPalette.Rgb(0xEE7F2D), UIPalette.Rgb(0x3FB8C4), UIPalette.Rgb(0xE0607E), UIPalette.Rgb(0xF2C94C),
            UIPalette.Rgb(0x8E6CD9), UIPalette.Rgb(0x6E9445), UIPalette.Rgb(0x4F7FAE), UIPalette.Rgb(0xEDE6DA),
        };

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
                sv.style.maxHeight = 600f;
                sv.contentContainer.Add(content);
                Root.Add(sv);
            }
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

        VisualElement VolumeSlider(UIGlyph.DrawFn icon, string label, float value, Action<float> set, bool master)
        {
            var c = Card();
            var head = LabelRow(icon, label);
            var pct = UIKit.Text(Mathf.RoundToInt(value * 100f) + " %", fs, UIPalette.TurquoiseInk, true);
            head.Add(pct);
            c.Add(head);
            var s = new UISlider(0f, 1f, value, v =>
            {
                set(v);
                pct.text = Mathf.RoundToInt(v * 100f) + " %";
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
            content.Add(VolumeSlider(UIGlyphs.Speaker, Loc.Tr("Gesamt", "Master"), d.master, v => d.master = v, true));
            content.Add(VolumeSlider(UIGlyphs.Wave, Loc.Tr("Musik", "Music"), d.music, v => d.music = v, false));
            content.Add(VolumeSlider(UIGlyphs.Bell, Loc.Tr("Effekte", "Effects"), d.sfx, v => d.sfx = v, false));
            content.Add(VolumeSlider(UIGlyphs.Mic, Loc.Tr("Stimmen", "Voices"), d.voice, v => d.voice = v, false));
        }

        void BuildVideo()
        {
            var d = Settings.Data;
            content.Add(SegmentRow(UIGlyphs.Monitor, Loc.Tr("Grafik", "Graphics"),
                new[] { Loc.Tr("Niedrig", "Low"), Loc.Tr("Mittel", "Medium"), Loc.Tr("Hoch", "High") }, (int)d.quality,
                i => d.quality = (GraphicsQuality)i));
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
            if (capturing)
            {
                key.style.backgroundColor = UIPalette.Sun;
                captureLabel = key;
            }
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

            var phone = Card();
            phone.Add(LabelRow(UIGlyphs.Bubble, Loc.Tr("Handy-Ansicht", "Phone view")));
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
            _ = d;
        }

        void BuildCoop()
        {
            var d = Settings.Data;
            // name
            var nameCard = Card();
            nameCard.Add(LabelRow(UIGlyphs.Person, Loc.Tr("Dein Name", "Your name")));
            var field = new TextField { maxLength = Settings.MaxNameLength, value = d.playerName ?? "" };
            field.style.marginTop = 8f;
            field.style.fontSize = fs;
            field.style.height = fs * 2f;
            var input = field.Q(className: TextField.inputUssClassName);
            if (input != null)
            {
                input.style.backgroundColor = UIPalette.CardAlt;
                input.style.color = UIPalette.Ink;
                UIKit.Radius(input, 12f);
                UIKit.Border(input, 2f, UIPalette.A(UIPalette.Ink, 0.12f));
                UIKit.Pad(input, 4f, 12f, 4f, 12f);
            }
            field.RegisterValueChangedCallback(e =>
            {
                Settings.Data.playerName = e.newValue ?? "";
                Settings.MarkDirty();
            });
            field.RegisterCallback<FocusOutEvent>(_ => { Settings.Apply(); Settings.Save(); });
            UITextInput.Track(field);
            nameCard.Add(field);
            content.Add(nameCard);

            // suit color
            var suit = Card();
            suit.Add(LabelRow(UIGlyphs.Person, Loc.Tr("Anzugfarbe", "Suit color")));
            var sw = UIKit.Row();
            sw.style.flexWrap = Wrap.Wrap;
            sw.style.marginTop = 8f;
            for (int i = 0; i < SuitColors.Length; i++)
            {
                var col = SuitColors[i];
                bool sel = ColorClose(col, d.suitColor);
                var b = UIKit.Box();
                float size = compact ? 56f : 44f;
                UIKit.Size(b, size, size);
                UIKit.Radius(b, size * 0.5f);
                b.style.marginRight = 10f; b.style.marginBottom = 8f;
                UIKit.Border(b, sel ? 4f : 2f, sel ? UIPalette.Ink : UIPalette.A(UIPalette.Ink, 0.15f));
                if (sel) { UIKit.Center(b); b.Add(new UIGlyph(UIGlyphs.Check, UIPalette.OnColor(col), size * 0.5f)); }
                UIKit.Clickable(b, () => { Settings.Data.suitColor = col; Settings.Apply(); Settings.Save(); dirty = true; }, col, UIPalette.Lighten(col, 0.15f));
                sw.Add(b);
            }
            suit.Add(sw);
            content.Add(suit);

            // hat
            content.Add(SegmentRow(UIGlyphs.Person, Loc.Tr("Hut", "Hat"),
                new[] { Loc.Tr("Keiner", "None"), Loc.Tr("Fischer", "Bucket"), Loc.Tr("Wikinger", "Viking"), Loc.Tr("Haiflosse", "Shark"), Loc.Tr("Ente", "Duck") },
                Mathf.Clamp(d.hat, 0, 4), i => d.hat = i));

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

        static bool ColorClose(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) < 0.06f;
    }
}
