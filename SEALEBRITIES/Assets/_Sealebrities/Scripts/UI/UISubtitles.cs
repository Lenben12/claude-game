using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// NPC / radio subtitles (section I "subtitles", A.2, B.1): bottom centre above the hotbar, max 2 lines, 26 px, the
    /// speaker's name in the speaker's colour, white text with an ink outline, no box. The same look is used for every
    /// NPC line (shared dialogue style: the npc module only raises GameEvents.NpcLine / Dialogue.Say). The npc module's
    /// fallback overlay steps aside because this class exists (StoryUI detects "Sealebrities.UISubtitles").
    public class UISubtitles
    {
        readonly VisualElement root, lineWrap;
        readonly Label speaker, text;
        float lineLeft, lineTotal;

        public UISubtitles(VisualElement layer)
        {
            root = new VisualElement { name = "sb-subtitles", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(root);
            layer.Add(root);

            lineWrap = UIKit.Col();
            // above the hip-recording line „● REC · 3“ (bottom 214, UIViewfinder) and the control hints – at 196 the
            // subtitle and the REC line were drawn on top of each other (playtest round 2, wreck dive)
            UIKit.Abs(lineWrap, 0f, null, 0f, 244f);
            lineWrap.style.alignItems = Align.Center;
            root.Add(lineWrap);
            speaker = UIKit.HudText("", 22f, UIPalette.Sun);
            lineWrap.Add(speaker);
            text = UIKit.HudText("", 26f);
            text.style.whiteSpace = WhiteSpace.Normal;
            text.style.maxWidth = 1100f;
            text.style.unityTextAlign = TextAnchor.MiddleCenter;
            text.style.marginTop = 2f;
            lineWrap.Add(text);
            lineWrap.style.display = DisplayStyle.None;

            UIKit.IgnorePicking(root);
            GameEvents.NpcLine += OnLine;
        }

        public void Dispose() { GameEvents.NpcLine -= OnLine; }

        /// Name of a speaker id ("horst", "gus" …) – the npc module's DisplayName wins.
        public static string SpeakerName(string id)
        {
            var npc = NpcRegistry.Find(id);
            if (npc != null && !(npc is Object o && o == null) && !string.IsNullOrEmpty(npc.DisplayName)) return npc.DisplayName;
            switch (id)
            {
                case "horst": return "Horst";
                case "gus": return Loc.Tr("Onkel Gus", "Uncle Gus");
                case "fin": return "Dr. Fin";
                case "chad": return "Chad";
                case "brad": return "Brad";
                case "wanda": return "Wanda";
                case "pearl": return Loc.Tr("Oma Perle", "Grandma Pearl");
                case "toni": return Loc.Tr("Toni", "Tony");
                case "ada": return Loc.Tr("Tante Ada", "Auntie Ada");
                case "mira": return "Mira";
                case "tomas": return "Tomas";
                case "juno": return "Juno";
                case "lio": return "Lio";
                default: return string.IsNullOrEmpty(id) ? "" : char.ToUpperInvariant(id[0]) + id.Substring(1);
            }
        }

        /// Subtitle colour of a speaker (section I).
        public static Color SpeakerColor(string id)
        {
            switch (id)
            {
                case "horst": return UIPalette.Rgb(0xE07B39);
                case "gus": return UIPalette.Rgb(0x3FA9B5);
                case "fin": return UIPalette.Rgb(0x8E5CC2);
                case "chad": case "brad": return UIPalette.Rgb(0xE8C65A);
                case "wanda": return UIPalette.Rgb(0xE0607E);
                case "pearl": return UIPalette.Rgb(0x6E9445);
                case "toni": return UIPalette.Rgb(0x4F7FAE);
                default: return UIPalette.Rgb(0xC9B48A);
            }
        }

        void OnLine(string speakerId, string line, float seconds)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (!Settings.Data.subtitles) return;
            speaker.text = SpeakerName(speakerId);
            speaker.style.color = SpeakerColor(speakerId);
            text.text = line;
            lineTotal = Mathf.Clamp(float.IsNaN(seconds) ? 3.5f : seconds, 1.5f, 12f) + 0.4f;
            lineLeft = lineTotal;
            lineWrap.style.display = DisplayStyle.Flex;
        }

        public void Clear()
        {
            lineLeft = 0f;
            lineWrap.style.display = DisplayStyle.None;
        }

        bool atTop;

        public void Tick(float dt)
        {
            bool show = UIKit.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen && !UIState.CharacterOpen && !UIState.PauseOpen;
            UIKit.Show(root, show);
            // while the raised phone fills the screen centre the subtitles move to the top (the quest line is
            // hidden then) – at the bottom they were written across the phone screen
            bool top = UIState.PhoneOpen || UIState.InventoryOpen;
            if (top != atTop)
            {
                atTop = top;
                if (top) { lineWrap.style.bottom = StyleKeyword.Auto; lineWrap.style.top = 30f; }
                else { lineWrap.style.top = StyleKeyword.Auto; lineWrap.style.bottom = 244f; }
            }
            if (lineLeft <= 0f) return;
            lineLeft -= dt;
            float a = Mathf.Clamp01(lineLeft / 0.35f) * Mathf.Clamp01((lineTotal - lineLeft) / 0.15f);
            lineWrap.style.opacity = a;
            if (lineLeft <= 0f) lineWrap.style.display = DisplayStyle.None;
        }
    }

    /// Radio card (B.1): bottom-left, the speaker's round portrait + „Onkel Gus · Funk“, shown while a radio call runs
    /// (GameEvents.RadioCall). It counts as the one notification card (Q37) – other cards wait meanwhile.
    public class UIRadioCard
    {
        readonly VisualElement root, card, portrait;
        readonly Label nameLabel;
        readonly UIGlyph wave;
        float age = -1f;
        string speakerId;
        const float MaxSeconds = 40f;   // safety: a call that never ends

        public UIRadioCard(VisualElement layer)
        {
            root = new VisualElement { name = "sb-radio", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(root);
            layer.Add(root);
            card = UIKit.Row();
            UIKit.Abs(card, 32f, null, null, 210f);
            card.style.backgroundColor = new Color(1f, 1f, 1f, 0.94f);
            UIKit.Radius(card, 22f);
            UIKit.Pad(card, 10f, 18f, 10f, 10f);
            UIKit.Lift(card, 1.2f);
            portrait = UIKit.Box();
            UIKit.Size(portrait, 58f, 58f);
            UIKit.Radius(portrait, 29f);
            UIKit.Center(portrait);
            portrait.Add(new UIGlyph(UIGlyphs.Person, Color.white, 40f));
            card.Add(portrait);
            var col = UIKit.Col();
            col.style.marginLeft = 12f;
            nameLabel = UIKit.Text("", 21f, UIPalette.Ink, true);
            col.Add(nameLabel);
            var subRow = UIKit.Row();
            wave = new UIGlyph(UIGlyphs.Radio, UIPalette.InkSoft, 20f);
            subRow.Add(wave);
            subRow.Add(UIKit.Text(" " + Loc.Tr("Funk", "Radio"), 17f, UIPalette.InkSoft, true));
            col.Add(subRow);
            card.Add(col);
            card.style.display = DisplayStyle.None;
            root.Add(card);
            UIKit.IgnorePicking(root);
            GameEvents.RadioCall += OnRadio;
        }

        public void Dispose() { GameEvents.RadioCall -= OnRadio; }

        public bool Active => age >= 0f;

        void OnRadio(string id, bool started)
        {
            if (started)
            {
                speakerId = id;
                nameLabel.text = UISubtitles.SpeakerName(id);
                portrait.style.backgroundColor = UISubtitles.SpeakerColor(id);
                age = 0f;
                card.style.display = DisplayStyle.Flex;
                UIAudio.Ping();
            }
            else if (id == speakerId || string.IsNullOrEmpty(id)) Clear();
        }

        public void Clear()
        {
            age = -1f;
            card.style.display = DisplayStyle.None;
        }

        public void Tick(float dt, UINotifications notes)
        {
            bool show = UIKit.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen && !UIState.CharacterOpen;
            UIKit.Show(root, show);
            if (age >= 0f)
            {
                age += dt;
                wave.style.opacity = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(age * 6f));
                if (age > MaxSeconds) Clear();
            }
            if (notes != null) notes.HoldCards = age >= 0f;
        }
    }
}
