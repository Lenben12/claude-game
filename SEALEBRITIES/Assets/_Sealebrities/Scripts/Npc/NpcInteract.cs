using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „NPCs: mehr Möglichkeiten zur Interaktion“, §20 Q4–Q6): the LOCAL interaction layer.
    ///  • Gesture wheel: hold [G] near an NPC → ten symbols around the screen centre (the cursor is free); release over a
    ///    symbol or click it. At a hermit they are signs (Hallo, Das Meer ehren, Frieden, Essen, Tausch, Zeig's mir,
    ///    Hinweis, Geschenk, Danke, Filmen – the wrong one annoys him); at everybody else they are actions (Reden, Tipp,
    ///    Geschenk, Interview …). The centre shows the name, the role and – for hermits – the mood.
    ///  • Gift / barter picker: a small list of backpack items (gift) or the hermit's offers (trade, Q47).
    ///  • Tie-up (Q6): a hermit won the stick fight – the diver is tied up; wriggle free by pressing [A] and [D] in turn.
    /// Own UI Toolkit panel above the HUD. Requests go to the host through NpcNet (co-op safe).
    public sealed class NpcInteract : MonoBehaviour
    {
        public static NpcInteract I { get; private set; }

        // ---------------------------------------------------------------- state (static: tests + director)
        public static bool WheelOpen { get; private set; }
        public static bool PickerOpen { get; private set; }
        public static bool TiedUp { get; private set; }
        public static float TieProgress { get; private set; }
        public static NpcActor Target { get; private set; }
        static string tieNpc;
        static float tieT;
        static int lastDir;
        static int hovered = -1;
        static bool pickerTrade;

        public static NpcInteract Create(Transform parent)
        {
            var go = new GameObject("SB_NpcInteract");
            go.transform.SetParent(parent, false);
            return go.AddComponent<NpcInteract>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { WheelOpen = false; PickerOpen = false; TiedUp = false; TieProgress = 0f; Target = null; tieNpc = null; hovered = -1; }

        /// Session start / back to the menu: everything closed, nobody tied up.
        public static void ResetSession()
        {
            if (I != null) { I.CloseWheel(false); I.ClosePicker(); }
            WheelOpen = false; PickerOpen = false;
            if (TiedUp) { TiedUp = false; PlayerController.Restrained = false; }
            TieProgress = 0f;
            UIState.NpcMenuOpen = false;
            if (I != null && I.built) UIKit.Show(I.tiePanel, false);
        }

        // ---------------------------------------------------------------- UI
        UIDocument doc;
        VisualElement root, wheel, center, picker, pickerList, pickerIcon, tiePanel, tieFill, moodBar, moodFill;
        Label centerName, centerRole, centerHint, moodText, pickerTitle, tieText;
        readonly VisualElement[] seg = new VisualElement[HermitDatabase.SignCount];
        readonly UIGlyph[] segGlyph = new UIGlyph[HermitDatabase.SignCount];
        readonly Label[] segLabel = new Label[HermitDatabase.SignCount];
        bool built;
        const float WheelR = 210f, SegSize = 104f;

        void Awake() { I = this; }

        void Start()
        {
            try { Build(); }
            catch (System.Exception e) { Debug.LogWarning("[Npc] interaction UI could not be built: " + e.Message); }
        }

        void OnDestroy() { if (I == this) I = null; }

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
            ps.name = "SB_NpcInteractPanel";
            ps.sortingOrder = 30;   // above the HUD: the wheel and the picker take the mouse
            return ps;
        }

        static readonly UIGlyph.DrawFn[] SignGlyphs =
        {
            UIGlyphs.Hand, UIGlyphs.Wave, UIGlyphs.Handshake, UIGlyphs.Food, UIGlyphs.Coin,
            UIGlyphs.Book, UIGlyphs.Question, UIGlyphs.Gift, UIGlyphs.Heart, UIGlyphs.Camera
        };

        static readonly Color[] SignColors =
        {
            UIPalette.Rgb(0xF2C94C), UIPalette.Rgb(0x3FB8C4), UIPalette.Rgb(0x7FC8A9), UIPalette.Rgb(0xEE7F2D), UIPalette.Rgb(0xE8C65A),
            UIPalette.Rgb(0x8E5CC2), UIPalette.Rgb(0x6E9BE0), UIPalette.Rgb(0xE0607E), UIPalette.Rgb(0xE05A6E), UIPalette.Rgb(0x4F7FAE)
        };

        void Build()
        {
            doc = gameObject.AddComponent<UIDocument>();
            doc.panelSettings = MakePanelSettings();
            root = doc.rootVisualElement;
            if (root == null) return;
            root.pickingMode = PickingMode.Ignore;
            UIFonts.Apply(root);
            UIKit.FillParent(root);

            // ---- the wheel (screen centre)
            wheel = new VisualElement { name = "sb-gesture-wheel" };
            wheel.style.position = Position.Absolute;
            wheel.style.left = Length.Percent(50f);
            wheel.style.top = Length.Percent(50f);
            wheel.style.width = 0f; wheel.style.height = 0f;
            wheel.pickingMode = PickingMode.Ignore;
            root.Add(wheel);
            // a soft disc behind everything
            var disc = new VisualElement();
            UIKit.Abs(disc, -WheelR - 70f, -WheelR - 70f, null, null);
            UIKit.Size(disc, (WheelR + 70f) * 2f, (WheelR + 70f) * 2f);
            UIKit.Radius(disc, WheelR + 70f);
            disc.style.backgroundColor = new Color(0.07f, 0.09f, 0.14f, 0.55f);
            disc.pickingMode = PickingMode.Position;
            disc.RegisterCallback<PointerEnterEvent>(e => SetHover(-1));
            wheel.Add(disc);
            for (int i = 0; i < HermitDatabase.SignCount; i++)
            {
                int k = i;
                float ang = (i / (float)HermitDatabase.SignCount) * Mathf.PI * 2f - Mathf.PI * 0.5f;
                float x = Mathf.Cos(ang) * WheelR, y = Mathf.Sin(ang) * WheelR;
                var s = UIKit.Col();
                UIKit.Abs(s, x - SegSize * 0.5f, y - SegSize * 0.5f, null, null);
                UIKit.Size(s, SegSize, SegSize);
                UIKit.Radius(s, SegSize * 0.5f);
                s.style.alignItems = Align.Center;
                s.style.justifyContent = Justify.Center;
                s.style.backgroundColor = new Color(0.96f, 0.94f, 0.9f, 0.95f);
                UIKit.Border(s, 3f, UIPalette.A(SignColors[i], 0.9f));
                s.pickingMode = PickingMode.Position;
                var g = new UIGlyph(SignGlyphs[i], UIWidgets.Ink(SignColors[i]), 46f);
                s.Add(g);
                // round 3 UI polish: one line, never wrapping out of the round segment
                var l = UIKit.Text("", 14f, UIPalette.Ink, true);
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.whiteSpace = WhiteSpace.NoWrap;
                l.style.overflow = Overflow.Hidden;
                l.style.textOverflow = TextOverflow.Ellipsis;
                l.style.maxWidth = SegSize - 14f;
                l.pickingMode = PickingMode.Ignore;
                s.Add(l);
                s.RegisterCallback<PointerEnterEvent>(e => SetHover(k));
                s.RegisterCallback<ClickEvent>(e => { SetHover(k); Choose(k); });
                seg[i] = s; segGlyph[i] = g; segLabel[i] = l;
                wheel.Add(s);
            }
            center = UIKit.Col();
            UIKit.Abs(center, -96f, -96f, null, null);
            UIKit.Size(center, 192f, 192f);
            UIKit.Radius(center, 96f);
            center.style.alignItems = Align.Center;
            center.style.justifyContent = Justify.Center;
            center.style.backgroundColor = new Color(0.98f, 0.96f, 0.92f, 0.97f);
            center.pickingMode = PickingMode.Ignore;
            centerName = UIKit.Text("", 24f, UIPalette.Ink, true);
            centerName.style.maxWidth = 168f;
            centerName.style.whiteSpace = WhiteSpace.NoWrap;
            centerName.style.overflow = Overflow.Hidden;
            centerName.style.textOverflow = TextOverflow.Ellipsis;
            centerRole = UIKit.Text("", 15f, UIPalette.InkSoft, true);
            centerRole.style.maxWidth = 168f;
            centerRole.style.whiteSpace = WhiteSpace.NoWrap;
            centerRole.style.overflow = Overflow.Hidden;
            centerRole.style.textOverflow = TextOverflow.Ellipsis;
            moodBar = new VisualElement();
            UIKit.Size(moodBar, 130f, 10f);
            UIKit.Radius(moodBar, 5f);
            moodBar.style.marginTop = 8f;
            moodBar.style.backgroundColor = UIPalette.A(UIPalette.Ink, 0.15f);
            moodFill = new VisualElement();
            moodFill.style.height = 10f;
            UIKit.Radius(moodFill, 5f);
            moodBar.Add(moodFill);
            moodText = UIKit.Text("", 14f, UIPalette.InkSoft, true);
            centerHint = UIKit.Text("", 14f, UIPalette.Ink, true);
            centerHint.style.marginTop = 6f;
            centerHint.style.unityTextAlign = TextAnchor.MiddleCenter;
            centerHint.style.whiteSpace = WhiteSpace.Normal;
            centerHint.style.maxWidth = 148f;   // stays inside the round centre (chord at the hint's height)
            center.Add(centerName); center.Add(centerRole); center.Add(moodBar); center.Add(moodText); center.Add(centerHint);
            UIKit.IgnorePicking(center);
            wheel.Add(center);
            UIKit.Show(wheel, false);

            // ---- picker (gift / barter)
            picker = UIKit.Col();
            picker.style.position = Position.Absolute;
            picker.style.left = Length.Percent(50f);
            picker.style.top = Length.Percent(50f);
            picker.style.width = 540f;
            picker.style.maxHeight = Length.Percent(80f);
            picker.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
            picker.style.backgroundColor = new Color(0.97f, 0.95f, 0.91f, 0.98f);
            UIKit.Radius(picker, 26f);
            UIKit.Pad(picker, 18f, 20f, 18f, 20f);
            UIKit.Lift(picker, 1.4f);
            // round 3 UI polish: header row (icon + title + ✕) and a scrolling list – long backpacks never run off screen
            var pHead = UIKit.Row();
            pickerIcon = UIKit.Box();
            UIKit.Size(pickerIcon, 44f, 44f);
            pHead.Add(pickerIcon);
            pickerTitle = UIKit.Text("", 24f, UIPalette.Ink, true);
            pickerTitle.style.marginLeft = 12f;
            pickerTitle.style.flexShrink = 1;
            pickerTitle.style.whiteSpace = WhiteSpace.NoWrap;
            pickerTitle.style.overflow = Overflow.Hidden;
            pickerTitle.style.textOverflow = TextOverflow.Ellipsis;
            pHead.Add(pickerTitle);
            pHead.Add(UIKit.Flex());
            pHead.Add(UIWidgets.RoundIconButton(UIGlyphs.Cross, ClosePicker, 44f));
            picker.Add(pHead);
            var pScroll = UIKit.Scroll();
            pScroll.style.marginTop = 10f;
            pScroll.style.flexShrink = 1;
            pScroll.style.maxHeight = 560f;
            pickerList = UIKit.Col();
            pScroll.Add(pickerList);
            picker.Add(pScroll);
            root.Add(picker);
            UIKit.Show(picker, false);

            // ---- tie-up (Q6)
            tiePanel = UIKit.Col();
            tiePanel.style.position = Position.Absolute;
            tiePanel.style.left = Length.Percent(50f);
            // round 3 UI polish: below the interaction prompt (62 %) and above the hotbar – nothing overlaps it
            tiePanel.style.top = Length.Percent(69f);
            tiePanel.style.translate = new Translate(Length.Percent(-50f), 0f);
            tiePanel.style.alignItems = Align.Center;
            tiePanel.style.backgroundColor = new Color(0.07f, 0.09f, 0.14f, 0.55f);
            UIKit.Radius(tiePanel, 22f);
            UIKit.Pad(tiePanel, 10f, 26f, 14f, 26f);
            tiePanel.pickingMode = PickingMode.Ignore;
            var tieTitle = UIKit.HudText(Loc.Tr("Gefesselt!", "Tied up!"), 34f, UIPalette.Sun, true);
            tiePanel.Add(tieTitle);
            tieText = UIKit.HudText("", 22f, Color.white, true);
            tiePanel.Add(tieText);
            var bar = new VisualElement();
            UIKit.Size(bar, 360f, 18f);
            UIKit.Radius(bar, 9f);
            bar.style.marginTop = 10f;
            bar.style.backgroundColor = new Color(1f, 1f, 1f, 0.25f);
            tieFill = new VisualElement();
            tieFill.style.height = 18f;
            tieFill.style.width = 0f;
            UIKit.Radius(tieFill, 9f);
            tieFill.style.backgroundColor = UIPalette.Rgb(0xC9A15A);
            bar.Add(tieFill);
            tiePanel.Add(bar);
            UIKit.IgnorePicking(tiePanel);
            root.Add(tiePanel);
            UIKit.Show(tiePanel, false);
            built = true;
        }

        // ---------------------------------------------------------------- frame
        void Update()
        {
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            if (!inGame)
            {
                if (WheelOpen) CloseWheel(false);
                if (PickerOpen) ClosePicker();
                return;
            }
            TickTieUp(Time.deltaTime);
            if (PickerOpen)
            {
                if (GameInput.PausePressed || GameInput.Down(GameAction.Gestures) || Target == null || !Target.Present || Far(Target, 7f)) ClosePicker();
                return;
            }
            if (WheelOpen)
            {
                if (Target == null || !Target.Present || Far(Target, 8f) || GameInput.PausePressed) { CloseWheel(false); return; }
                RefreshCenter();
                if (!GameInput.Held(GameAction.Gestures)) CloseWheel(true);
                return;
            }
            // opening: hold the key, not while another screen is open, not tied up, not under water
            if (TiedUp || !GameInput.Down(GameAction.Gestures)) return;
            if (UIState.BlocksGameplay || UIState.TextInput || StoryUI.LetterOpen) return;
            if (NpcUtil.LocalUnderwater) return;
            var t = FindTarget();
            if (t == null) { GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Niemand in der Nähe", "Nobody nearby"), Loc.Tr("Gesten nur bei Leuten", "Signs need someone")); return; }
            OpenWheel(t);
        }

        static bool Far(NpcActor a, float r) => a == null || NpcUtil.FlatDistSq(a.Position, NpcUtil.LocalPlayerPos) > r * r;

        /// The NPC the local player looks at within 6 m (else the nearest within 3.5 m).
        public static NpcActor FindTarget()
        {
            var dir = NpcDirector.I;
            if (dir == null || !NpcUtil.PlayerValid(out var p)) return null;
            var cam = p.View != null ? p.View : Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : p.Root.position + Vector3.up * 1.6f;
            Vector3 fwd = cam != null ? cam.transform.forward : p.Root.forward;
            NpcActor best = null, nearest = null;
            float bestDot = Mathf.Cos(35f * Mathf.Deg2Rad), nd = 3.5f * 3.5f;
            for (int i = 0; i < NpcDatabase.All.Length; i++)
            {
                var a = dir.ActorAt(i);
                if (a == null || !a.Present || !a.gameObject.activeInHierarchy || a.Motion == NpcMotion.Hidden || a.Motion == NpcMotion.Swim) continue;
                Vector3 head = a.Position + Vector3.up * (a.Def.height * 0.8f);
                Vector3 d = head - eye;
                float dist = d.magnitude;
                if (dist > 6f || dist < 0.01f) continue;
                float dot = Vector3.Dot(fwd, d / dist);
                if (dot > bestDot) { bestDot = dot; best = a; }
                float fd = NpcUtil.FlatDistSq(a.Position, p.Root.position);
                if (fd < nd) { nd = fd; nearest = a; }
            }
            return best != null ? best : nearest;
        }

        public void OpenWheel(NpcActor a)
        {
            if (a == null || !built) return;
            Target = a;
            WheelOpen = true;
            hovered = -1;
            UIState.NpcMenuOpen = true;
            bool hermit = NpcDatabase.IsHermit(a.Id);
            for (int i = 0; i < seg.Length; i++)
            {
                var sign = (HermitSign)i;
                segLabel[i].text = hermit ? HermitDatabase.SignName(sign) : HermitDatabase.ActionName(sign);
                bool enabled = Enabled(a, sign);
                seg[i].style.opacity = enabled ? 1f : 0.45f;
                seg[i].style.scale = new Scale(Vector2.one);
            }
            centerName.text = a.Def.Name;
            centerRole.text = a.Def.Role;
            UIKit.Show(moodBar, hermit);
            UIKit.Show(moodText, hermit);
            RefreshCenter();
            UIKit.Show(wheel, true);
            UIAudio.Click();
        }

        /// Gift needs something in the backpack; barter only at the hermits.
        static bool Enabled(NpcActor a, HermitSign s)
        {
            bool hermit = NpcDatabase.IsHermit(a.Id);
            if (s == HermitSign.Gift) return GiftItems(null) > 0;
            if (s == HermitSign.Trade && !hermit) return true;   // they answer where to barter
            return true;
        }

        void RefreshCenter()
        {
            var a = Target;
            if (a == null) return;
            if (NpcDatabase.IsHermit(a.Id))
            {
                int mood = Hermits.Mood(a.Id), band = Hermits.Band(mood);
                moodFill.style.width = 130f * Mathf.Clamp01((mood + 100) / 200f);
                moodFill.style.backgroundColor = Hermits.BandColor(band);
                string txt = Hermits.BandName(band);
                int resp = Hermits.Respect(a.Id);
                if (resp > 0) txt += Loc.Tr("  · Respekt ", "  · respect ") + resp;
                if (moodText.text != txt) moodText.text = txt;
            }
            string hint = hovered >= 0
                ? (NpcDatabase.IsHermit(a.Id) ? HermitDatabase.SignName((HermitSign)hovered) : HermitDatabase.ActionName((HermitSign)hovered))
                : Loc.Tr("Zeichen wählen", "Pick a sign");
            if (centerHint.text != hint) centerHint.text = hint;
        }

        void SetHover(int k)
        {
            if (hovered == k) return;
            if (hovered >= 0 && hovered < seg.Length) seg[hovered].style.scale = new Scale(Vector2.one);
            hovered = k;
            if (k >= 0 && k < seg.Length) seg[k].style.scale = new Scale(new Vector2(1.14f, 1.14f));
            RefreshCenter();
        }

        void Choose(int k)
        {
            var a = Target;
            CloseWheel(false);
            if (a == null || k < 0) return;
            Perform(a, (HermitSign)k);
        }

        /// Tests / AutoShot: closes the wheel without choosing.
        public void CloseWheelForTest() => CloseWheel(false);

        void CloseWheel(bool choose)
        {
            int k = hovered;
            WheelOpen = false;
            if (built) UIKit.Show(wheel, false);
            if (!PickerOpen) UIState.NpcMenuOpen = false;
            hovered = -1;
            if (choose && k >= 0 && Target != null) Perform(Target, (HermitSign)k);
        }

        /// The sign / action at an NPC (UI and tests): gift and trade open the picker, everything else goes to the host.
        public static void Perform(NpcActor a, HermitSign sign)
        {
            if (a == null) return;
            bool hermit = NpcDatabase.IsHermit(a.Id);
            if (sign == HermitSign.Gift)
            {
                if (I != null) I.OpenPicker(a, false);
                return;
            }
            if (sign == HermitSign.Trade && hermit && Hermits.Mood(a.Id) >= Hermits.Grumpy)
            {
                if (I != null) I.OpenPicker(a, true);
                return;
            }
            string goal = QuestTracker.Line;
            var hc = Refs.HandCamera;
            bool rec = hc != null && !(hc is Object ho && ho == null) && hc.IsRecording;
            NpcNet.Request("sign", a.Id, NpcNet.I((int)sign), goal ?? "", rec ? "1" : "0");
        }

        // ---------------------------------------------------------------- picker
        static readonly List<string> giftIds = new List<string>(24);

        /// Giftable items of the local backpack / pouch treasures (into `into`, may be null): food, raw, materials,
        /// drinks, treasure – no tools, weapons, collectibles or map pieces.
        static int GiftItems(List<string> into)
        {
            into?.Clear();
            int n = 0;
            var lists = new[] { Backpack.Stacks, Backpack.Pouch };
            for (int k = 0; k < lists.Length; k++)
            {
                var l = lists[k];
                if (l == null) continue;
                for (int i = 0; i < l.Count; i++)
                {
                    var st = l[i];
                    if (st == null || st.count <= 0) continue;
                    var d = st.Def;
                    if (d == null) continue;
                    var c = d.category;
                    if (c != ItemCategory.Food && c != ItemCategory.Drink && c != ItemCategory.Raw && c != ItemCategory.Material && c != ItemCategory.Valuable
                        && d.id != ItemIds.BottleFresh) continue;
                    if (into != null && !into.Contains(d.id)) into.Add(d.id);
                    n++;
                }
            }
            return n;
        }

        public void OpenPicker(NpcActor a, bool trade)
        {
            if (!built || a == null) return;
            Target = a;
            PickerOpen = true;
            pickerTrade = trade;
            UIState.NpcMenuOpen = true;
            RebuildPicker();
            UIKit.Show(picker, true);
        }

        void RebuildPicker()
        {
            var a = Target;
            pickerList.Clear();
            if (a == null) return;
            if (pickerIcon != null)
            {
                pickerIcon.Clear();
                pickerIcon.Add(UIWidgets.IconTile(pickerTrade ? UIGlyphs.Handshake : UIGlyphs.Gift, pickerTrade ? UIPalette.Rgb(0xE8C65A) : UIPalette.Rgb(0xE0607E), 44f));
            }
            if (pickerTrade)
            {
                pickerTitle.text = Loc.Tr("Tausch mit ", "Trade with ") + a.Def.Name;
                var def = HermitDatabase.Get(a.Id);
                if (def == null || def.trades == null) return;
                for (int i = 0; i < def.trades.Length; i++)
                {
                    int k = i;
                    var t = def.trades[i];
                    bool can = NpcDirector.CanTrade(t);
                    string give = t.give == HermitDatabase.AnyValuable ? Loc.Tr("1 Schatz", "1 treasure") : t.giveN + "× " + (ItemDatabase.Get(t.give)?.Name ?? t.give);
                    string get = t.special == "hint" ? Loc.Tr("ein Hinweis", "a hint") : t.special == "respect" ? Loc.Tr("Respekt im Lager", "respect in the camp")
                        : t.getN + "× " + (ItemDatabase.Get(t.get)?.Name ?? t.get) + (t.get2 != null ? " + " + t.get2N + "× " + (ItemDatabase.Get(t.get2)?.Name ?? t.get2) : "");
                    var row = Row(t.give == HermitDatabase.AnyValuable ? (UIGlyph.DrawFn)UIGlyphs.Chest : UIGlyphs.ForItemId(t.give), give + "  →  " + get, can, () =>
                    {
                        if (NpcDirector.LocalTrade(Target, k)) RebuildPicker();
                    });
                    pickerList.Add(row);
                }
                var likes = HermitDatabase.Get(a.Id);
                if (likes != null) pickerList.Add(UIKit.Text(Loc.Tr("Mag: ", "Likes: ") + likes.Likes, 15f, UIPalette.InkSoft, true));
                return;
            }
            pickerTitle.text = Loc.Tr("Geschenk für ", "Gift for ") + a.Def.Name;
            GiftItems(giftIds);
            if (giftIds.Count == 0) { pickerList.Add(UIKit.Text(Loc.Tr("Nichts zum Verschenken im Rucksack.", "Nothing to give in the backpack."), 17f, UIPalette.InkSoft)); return; }
            for (int i = 0; i < giftIds.Count && i < 24; i++)   // the list scrolls now (was cut after 9)
            {
                string id = giftIds[i];
                var d = ItemDatabase.Get(id);
                int cnt = Backpack.Count(id);
                var row = Row(UIGlyphs.ForItemId(id), (d != null ? d.Name : id) + (cnt > 1 ? "  ×" + cnt : ""), true, () =>
                {
                    var t = Target;
                    if (NpcDirector.LocalGift(t, id)) ClosePicker();
                });
                pickerList.Add(row);
            }
            var hd = HermitDatabase.Get(a.Id);
            if (hd != null) pickerList.Add(UIKit.Text(Loc.Tr("Mag: ", "Likes: ") + hd.Likes, 15f, UIPalette.InkSoft, true));
        }

        VisualElement Row(UIGlyph.DrawFn icon, string text, bool enabled, System.Action onClick)
        {
            var row = UIKit.Row();
            UIKit.Pad(row, 8f, 12f, 8f, 10f);
            UIKit.Radius(row, 16f);
            row.style.marginBottom = 6f;
            row.style.alignItems = Align.Center;
            if (icon != null) row.Add(new UIGlyph(icon, UIPalette.Ink, 36f));
            var l = UIKit.Text(text, 18f, enabled ? UIPalette.Ink : UIPalette.InkSoft, true);
            l.style.marginLeft = 10f;
            l.style.flexShrink = 1;
            l.style.whiteSpace = WhiteSpace.Normal;
            row.Add(l);
            UIKit.IgnorePicking(row);
            row.pickingMode = PickingMode.Position;
            if (enabled) UIKit.Clickable(row, onClick, Color.white, UIPalette.CardAlt);
            else { row.style.backgroundColor = UIPalette.A(Color.white, 0.5f); row.style.opacity = 0.6f; }
            return row;
        }

        public void ClosePicker()
        {
            PickerOpen = false;
            if (built) UIKit.Show(picker, false);
            if (!WheelOpen) UIState.NpcMenuOpen = false;
        }

        // ---------------------------------------------------------------- tie-up (Q6)
        /// A hermit knocked the local diver out: tied up until wriggled free ([A] / [D] in turn) – max 25 s.
        public static void StartTieUp(string npcId)
        {
            if (TiedUp) return;
            if (I != null) { I.CloseWheel(false); I.ClosePicker(); }
            TiedUp = true;
            TieProgress = 0f;
            tieT = 0f;
            lastDir = 0;
            tieNpc = npcId;
            PlayerController.Restrained = true;
            NpcNet.Request("tied", npcId);
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Gefesselt!", "Tied up!"), Loc.Tr("Abwechselnd ", "Alternate ") + GameInput.BindingLabel(GameAction.Left) + " / " + GameInput.BindingLabel(GameAction.Right));
        }

        static void Free()
        {
            if (!TiedUp) return;
            TiedUp = false;
            PlayerController.Restrained = false;
            TieProgress = 0f;
            NpcNet.Request("freed", tieNpc);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Befreit!", "Free!"), null);
            if (I != null && I.built) UIKit.Show(I.tiePanel, false);
        }

        string tieL, tieR, tiePre;

        void TickTieUp(float dt)
        {
            if (!TiedUp)
            {
                if (built && tiePanel.style.display != DisplayStyle.None) UIKit.Show(tiePanel, false);
                return;
            }
            tieT += dt;
            if (GameInput.Down(GameAction.Left) && lastDir != -1) { TieProgress += 0.085f; lastDir = -1; }
            if (GameInput.Down(GameAction.Right) && lastDir != 1) { TieProgress += 0.085f; lastDir = 1; }
            TieProgress = Mathf.Clamp01(TieProgress - 0.05f * dt);
            if (built)
            {
                UIKit.Show(tiePanel, !UIState.PauseOpen);
                tieFill.style.width = 360f * TieProgress;
                string l = GameInput.BindingLabel(GameAction.Left), r = GameInput.BindingLabel(GameAction.Right), pre = Loc.Tr("Abwechselnd ", "Alternate ");
                if (!ReferenceEquals(l, tieL) || !ReferenceEquals(r, tieR) || !ReferenceEquals(pre, tiePre))
                {
                    tieL = l; tieR = r; tiePre = pre;
                    tieText.text = pre + "[" + l + "] [" + r + "]" + Loc.Tr(" – zappeln!", " – wriggle!");
                }
            }
            if (TieProgress >= 1f || tieT > 25f) Free();
        }

        /// Test hook: one wriggle (as if A or D was pressed).
        public static void DebugWriggle(int dir)
        {
            if (!TiedUp) return;
            if (dir != lastDir) { TieProgress += 0.085f; lastDir = dir; }
            if (TieProgress >= 1f) Free();
        }
    }
}
