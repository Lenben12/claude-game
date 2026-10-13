using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Story UI contract of the npc module. The UI module may take over any part (subtitles, radio card, letter card)
    /// by setting the matching flag / handler – or simply by shipping UISubtitles / UIRadioCard / UILetterCard (detected
    /// at start-up). Until then NpcOverlay draws its own How-to-Fish style fallback (outlined text, light cards).
    public static class StoryUI
    {
        /// Set true by a UI that draws GameEvents.NpcLine subtitles itself.
        public static bool ExternalSubtitles;
        /// Set true by a UI that draws the radio card for GameEvents.RadioCall itself.
        public static bool ExternalRadioCard;
        /// UI letter card (K.5): (title, body, onClosed). When null the fallback card is shown.
        public static Action<string, string, Action> LetterHandler;

        /// Fallback letter state (read by the director: E closes it).
        public static bool LetterOpen { get; internal set; }
        internal static string letterTitle, letterBody;
        internal static Action letterClosed;
        internal static int letterVersion;

        /// The local player stands at an NPC with an open offer: "[E] accept · [Q] later" chip (fallback overlay).
        public static string OfferAccept, OfferDecline;
        /// The local player listens to a conversation: show the "E next · hold E skip" hint; SkipProgress 0..1 while held.
        public static bool ConversationHint;
        public static float SkipProgress;

        public static void ShowLetter(string title, string body, Action onClosed)
        {
            if (LetterHandler != null)
            {
                try { LetterHandler(title, body, onClosed); return; }
                catch (Exception e) { Debug.LogException(e); }
            }
            letterTitle = title;
            letterBody = body;
            letterClosed = onClosed;
            LetterOpen = true;
            letterVersion++;
        }

        /// Closes the fallback letter (E / LMB – the director calls it).
        public static void CloseLetter()
        {
            if (!LetterOpen) return;
            LetterOpen = false;
            letterVersion++;
            var cb = letterClosed;
            letterClosed = null;
            cb?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            ExternalSubtitles = false; ExternalRadioCard = false; LetterHandler = null; LetterOpen = false;
            letterTitle = null; letterBody = null; letterClosed = null; OfferAccept = null; OfferDecline = null;
            ConversationHint = false; SkipProgress = 0f;
        }
    }

    /// Fallback story overlay (UI Toolkit, own panel drawn below the main UI): subtitles bottom centre above the hotbar
    /// (speaker name in the speaker's colour, white outlined text, no box – I "subtitles"), the radio card bottom-left,
    /// Gus' letter card, the offer chip and the skip hint.
    public sealed class NpcOverlay : MonoBehaviour
    {
        UIDocument doc;
        VisualElement root, subBox, radioCard, letter, offerRow, hintRow, skipBar, skipFill;
        Label subName, subText, radioName, letterTitle, letterBody, letterHint, offerAccept, offerDecline, hintText;
        float subTimer;
        bool externalSubs, externalRadio, built;
        int letterVersionSeen = -1;
        string offerA, offerD;
        bool hintShown, rootHidden;

        static readonly Color Ink = UIPalette.Rgb(0x1E1B2B);
        static readonly Color Paper = UIPalette.Rgb(0xF6EBD2);
        static readonly Color CardBg = UIPalette.Rgb(0xF4EFE6);

        public static NpcOverlay Create(Transform parent)
        {
            var go = new GameObject("SB_NpcOverlay");
            go.transform.SetParent(parent, false);
            return go.AddComponent<NpcOverlay>();
        }

        void Start()
        {
            externalSubs = StoryUI.ExternalSubtitles || Type.GetType("Sealebrities.UISubtitles") != null;
            externalRadio = StoryUI.ExternalRadioCard || Type.GetType("Sealebrities.UIRadioCard") != null;
            try { Build(); }
            catch (Exception e) { Debug.LogWarning("[Npc] story overlay could not be built: " + e.Message); }
            GameEvents.NpcLine += OnLine;
            GameEvents.RadioCall += OnRadio;
            GameEvents.ReturnedToMenu += OnMenu;
        }

        void OnDestroy()
        {
            GameEvents.NpcLine -= OnLine;
            GameEvents.RadioCall -= OnRadio;
            GameEvents.ReturnedToMenu -= OnMenu;
        }

        static PanelSettings MakePanelSettings()
        {
            var ps = Resources.Load<PanelSettings>("SealebritiesPanelSettings");
            if (ps != null) ps = Instantiate(ps);
            else
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
            }
            if (ps.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>("SealebritiesTheme");
                if (theme != null) ps.themeStyleSheet = theme;
            }
            ps.name = "SB_NpcPanelSettings";
            ps.sortingOrder = -5;   // below the main UI panel (phone, pause and menus draw on top)
            return ps;
        }

        void Build()
        {
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = MakePanelSettings();
            root = doc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            UIFonts.Apply(root);
            UIKit.FillParent(root);

            // ---- subtitles (bottom centre, above the hotbar)
            subBox = UIKit.Col();
            UIKit.Abs(subBox, 0f, null, 0f, 168f);
            subBox.style.alignItems = Align.Center;
            subBox.pickingMode = PickingMode.Ignore;
            subName = UIKit.HudText("", 22f, Color.white, true);
            subText = UIKit.HudText("", 26f, Color.white, true);
            subText.style.whiteSpace = WhiteSpace.Normal;
            subText.style.maxWidth = 1080f;
            subText.style.unityTextAlign = TextAnchor.MiddleCenter;
            subText.style.marginTop = 2f;
            subBox.Add(subName);
            subBox.Add(subText);
            root.Add(subBox);
            UIKit.Show(subBox, false);

            // ---- offer chip + conversation hint (above the subtitles – the UI module's two-line subtitles sit at bottom
            // 244 up to ~340, the chip at 262 was written across the speaker's name)
            offerRow = UIKit.Row();
            UIKit.Abs(offerRow, 0f, null, 0f, 356f);
            offerRow.style.justifyContent = Justify.Center;
            offerRow.pickingMode = PickingMode.Ignore;
            offerAccept = UIKit.HudText("", 24f, UIPalette.Sun, true);
            offerDecline = UIKit.HudText("", 22f, Color.white, true);
            offerDecline.style.marginLeft = 28f;
            offerRow.Add(offerAccept);
            offerRow.Add(offerDecline);
            root.Add(offerRow);
            UIKit.Show(offerRow, false);

            hintRow = UIKit.Col();
            UIKit.Abs(hintRow, 0f, null, 0f, 136f);
            hintRow.style.alignItems = Align.Center;
            hintRow.pickingMode = PickingMode.Ignore;
            hintText = UIKit.HudText("", 17f, new Color(1f, 1f, 1f, 0.85f), true);
            hintRow.Add(hintText);
            skipBar = new VisualElement();
            skipBar.style.width = 120f; skipBar.style.height = 5f; skipBar.style.marginTop = 4f;
            skipBar.style.backgroundColor = new Color(1f, 1f, 1f, 0.25f);
            UIKit.Radius(skipBar, 3f);
            skipFill = new VisualElement();
            skipFill.style.height = 5f; skipFill.style.width = 0f;
            skipFill.style.backgroundColor = UIPalette.Sun;
            UIKit.Radius(skipFill, 3f);
            skipBar.Add(skipFill);
            hintRow.Add(skipBar);
            root.Add(hintRow);
            UIKit.Show(hintRow, false);

            // ---- radio card (bottom-left)
            radioCard = UIKit.Row();
            UIKit.Abs(radioCard, 36f, null, null, 150f);
            radioCard.style.backgroundColor = new Color(CardBg.r, CardBg.g, CardBg.b, 0.94f);
            UIKit.Radius(radioCard, 22f);
            UIKit.Pad(radioCard, 12f, 22f, 12f, 12f);
            radioCard.pickingMode = PickingMode.Ignore;
            var portrait = new VisualElement();
            UIKit.Size(portrait, 54f, 54f);
            UIKit.Radius(portrait, 27f);
            portrait.style.backgroundColor = UIPalette.Rgb(0x3FA9B5);
            UIKit.Center(portrait);
            portrait.Add(new UIGlyph(UIGlyphs.Broadcast, Color.white, 34f));
            radioCard.Add(portrait);
            radioName = UIKit.Text("", 22f, Ink, true);
            radioName.style.marginLeft = 12f;
            radioCard.Add(radioName);
            root.Add(radioCard);
            UIKit.Show(radioCard, false);

            // ---- letter card (centre, paper)
            letter = UIKit.Col();
            UIKit.Abs(letter, null, null, null, null);
            letter.style.left = Length.Percent(50f);
            letter.style.top = Length.Percent(50f);
            letter.style.width = 760f;
            letter.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
            letter.style.backgroundColor = Paper;
            UIKit.Radius(letter, 24f);
            UIKit.Pad(letter, 34f, 44f, 26f, 44f);
            UIKit.Border(letter, 3f, UIPalette.Rgb(0xE2CFA6));
            letter.pickingMode = PickingMode.Ignore;
            letterTitle = UIKit.Text("", 34f, UIPalette.Rgb(0x7A4448), true);
            letterBody = UIKit.Text("", 25f, Ink, false);
            letterBody.style.whiteSpace = WhiteSpace.Normal;
            letterBody.style.marginTop = 16f;
            letterBody.style.unityFontStyleAndWeight = FontStyle.Italic;
            letterHint = UIKit.Text("", 19f, UIPalette.InkSoft, true);
            letterHint.style.marginTop = 22f;
            letterHint.style.alignSelf = Align.FlexEnd;
            letter.Add(letterTitle);
            letter.Add(letterBody);
            letter.Add(letterHint);
            root.Add(letter);
            UIKit.Show(letter, false);
            built = true;
        }

        void OnMenu()
        {
            subTimer = 0f;
            if (built) { UIKit.Show(subBox, false); UIKit.Show(radioCard, false); }
            if (StoryUI.LetterOpen) StoryUI.CloseLetter();
        }

        void OnLine(string speaker, string text, float seconds)
        {
            if (!built || externalSubs || !Settings.Data.subtitles) return;
            var def = NpcDatabase.Get(speaker);
            subName.text = def != null ? def.Name : speaker;
            subName.style.color = def != null ? def.color : Color.white;
            subText.text = text;
            subTimer = seconds + 0.2f;
            UIKit.Show(subBox, true);
        }

        void OnRadio(string speaker, bool started)
        {
            if (!built || externalRadio) return;
            if (started)
            {
                var def = NpcDatabase.Get(speaker);
                radioName.text = (def != null ? def.Name : speaker) + Loc.Tr(" · Funk", " · Radio");
            }
            UIKit.Show(radioCard, started);
        }

        void Update()
        {
            if (!built) return;
            bool blocked = UIState.PhoneOpen || UIState.PauseOpen || UIState.MainMenuOpen || UIState.LoadingOpen || UIState.CharacterOpen || UIState.InventoryOpen;
            if (blocked != rootHidden)
            {
                rootHidden = blocked;
                root.style.display = blocked ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (subTimer > 0f)
            {
                subTimer -= Time.unscaledDeltaTime;
                if (subTimer <= 0f) UIKit.Show(subBox, false);
            }

            // letter
            if (letterVersionSeen != StoryUI.letterVersion)
            {
                letterVersionSeen = StoryUI.letterVersion;
                if (StoryUI.LetterOpen)
                {
                    letterTitle.text = StoryUI.letterTitle ?? "";
                    letterBody.text = StoryUI.letterBody ?? "";
                    letterHint.text = "[" + GameInput.BindingLabel(GameAction.Interact) + "] " + Loc.Tr("Weiter", "Continue");
                }
                UIKit.Show(letter, StoryUI.LetterOpen);
            }

            // offer chip
            string a = StoryUI.OfferAccept, d = StoryUI.OfferDecline;
            if (!ReferenceEquals(a, offerA) || !ReferenceEquals(d, offerD))
            {
                offerA = a; offerD = d;
                offerAccept.text = a ?? "";
                offerDecline.text = d ?? "";
                UIKit.Show(offerDecline, !string.IsNullOrEmpty(d));
                UIKit.Show(offerRow, !string.IsNullOrEmpty(a));
            }

            // conversation hint + skip bar
            bool hint = StoryUI.ConversationHint && !StoryUI.LetterOpen;
            if (hint != hintShown)
            {
                hintShown = hint;
                if (hint)
                {
                    string k = GameInput.BindingLabel(GameAction.Interact);
                    hintText.text = Loc.Tr("[" + k + "] weiter  ·  " + k + " halten: überspringen", "[" + k + "] next  ·  hold " + k + ": skip");
                }
                UIKit.Show(hintRow, hint);
            }
            if (hint) skipFill.style.width = 120f * Mathf.Clamp01(StoryUI.SkipProgress);
        }
    }
}
