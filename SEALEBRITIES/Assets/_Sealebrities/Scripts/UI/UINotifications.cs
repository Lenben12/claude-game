using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Notifications (art bible §1.14, Q37, L.10) – "one icon + one short line + an optional value chip", never sentences:
    ///  - toasts: outlined white lines above the money/followers block (no boxes), max 2 on screen, queue max 6 (the
    ///    oldest is dropped), identical toasts merge into one line with „×3“;
    ///  - cards (max 1 on screen, the rest waits): push card top-right (icon tile + line + chip), celebration ribbon
    ///    (milestones, new sites, ranks – the second line on a light band), achievement card, morning card.
    ///    While a radio call runs (UISubtitles) cards wait (the radio card is the one card).
    public class UINotifications
    {
        class ToastItem
        {
            public VisualElement el;
            public UIHudGlyph icon;
            public Label line, chip;
            public NotifyKind kind;
            public string text, chipText, key;
            public int count;
            public float age;
        }

        struct PendingToast
        {
            public NotifyKind kind;
            public string text, chip, key;
            public int count;
        }

        enum CardType { Push, Ribbon, Summary, Achievement }

        class Card
        {
            public CardType type;
            public UIGlyph.DrawFn icon;
            public Color color;
            public string title, sub, key;
            public Texture2D image;
            public bool ping;
            public SummaryData summary;
        }

        class SummaryData
        {
            public int day, videos;
            public long views, followers, money;
            public string topSpecies;
        }

        struct RecentKey
        {
            public string key;
            public float time;
        }

        const float ToastLife = 3.6f, ToastFade = 0.35f;
        const int MaxToastsShown = 2, MaxToastQueue = 6;
        const float PushIn = 0.3f, PushHold = 3.2f, PushOut = 0.3f;
        const float RibbonIn = 0.35f, RibbonHold = 3.2f, RibbonOut = 0.45f;
        const float SummaryLife = 8f;
        const int MaxCardQueue = 6;

        readonly VisualElement root, toastStack;
        readonly List<ToastItem> toasts = new List<ToastItem>();
        readonly List<PendingToast> toastQueue = new List<PendingToast>();
        readonly List<ToastItem> toastPool = new List<ToastItem>();
        readonly List<RecentKey> recent = new List<RecentKey>();

        // cards
        readonly List<Card> cards = new List<Card>();
        Card current;
        float cardAge;
        readonly VisualElement pushWrap, pushIconHost, pushImage;
        readonly Label pushTitle, pushChip;
        readonly VisualElement ribbonWrap, ribbon, ribbonIcon, ribbonBand;
        readonly Label ribbonTitle, ribbonSub;
        readonly VisualElement summaryWrap;
        VisualElement summaryCard;
        SummaryData pendingSummary;
        float summaryDelay;

        /// True while a radio call shows its card (set by UISubtitles) – other cards wait.
        public bool HoldCards;

        public UINotifications(VisualElement layer)
        {
            root = new VisualElement { name = "sb-notes" };
            UIKit.FillParent(root);
            layer.Add(root);

            // toasts above the money block (bottom-right), newest at the bottom
            toastStack = UIKit.Col();
            UIKit.Abs(toastStack, null, null, 32f, 150f);
            toastStack.style.width = 560f;
            toastStack.style.alignItems = Align.FlexEnd;
            root.Add(toastStack);

            // push card (top-right, under the clock – the top centre belongs to the quest line)
            pushWrap = UIKit.Row();
            UIKit.Abs(pushWrap, null, 78f, 28f, null);
            pushWrap.style.minHeight = 64f;
            pushWrap.style.maxWidth = 600f;
            pushWrap.style.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
            UIKit.Radius(pushWrap, 20f);
            UIKit.Pad(pushWrap, 10f, 16f, 10f, 10f);
            UIKit.Lift(pushWrap, 1.2f);
            root.Add(pushWrap);
            pushIconHost = UIKit.Box();
            UIKit.Size(pushIconHost, 44f, 44f);
            pushWrap.Add(pushIconHost);
            pushTitle = UIKit.Text("", 21f, UIPalette.Ink, true);
            pushTitle.style.marginLeft = 12f;
            // the short line stays readable; a long chip (species names …) gives way first – before, „Neu im
            // Sealog!“ was cut to „Neu im Se…“ next to „Gefleckter Adlerroche“ (playtest round 2)
            pushTitle.style.flexShrink = 0;
            pushTitle.style.maxWidth = 300f;
            pushTitle.style.whiteSpace = WhiteSpace.NoWrap;
            pushTitle.style.overflow = Overflow.Hidden;
            pushTitle.style.textOverflow = TextOverflow.Ellipsis;
            pushWrap.Add(pushTitle);
            pushChip = UIKit.Pill("", UIPalette.CardAlt, UIPalette.Ink, 17f);
            pushChip.style.marginLeft = 10f;
            pushChip.style.maxWidth = 240f;
            pushChip.style.flexShrink = 1;
            pushChip.style.minWidth = 40f;
            pushChip.style.overflow = Overflow.Hidden;
            pushChip.style.textOverflow = TextOverflow.Ellipsis;
            pushChip.style.whiteSpace = WhiteSpace.NoWrap;
            pushWrap.Add(pushChip);
            pushImage = UIKit.Box();
            UIKit.Size(pushImage, 72f, 48f);
            pushImage.style.marginLeft = 8f;
            pushWrap.Add(pushImage);
            pushWrap.style.display = DisplayStyle.None;

            // celebration ribbon (upper third) – title on the coloured ribbon, second line on a light band (Q38)
            ribbonWrap = UIKit.Col();
            UIKit.Abs(ribbonWrap, 0f, null, 0f, null);
            ribbonWrap.style.top = Length.Percent(17f);
            ribbonWrap.style.alignItems = Align.Center;
            root.Add(ribbonWrap);
            ribbonIcon = UIKit.Box();
            UIKit.Size(ribbonIcon, 170f, 110f);
            ribbonIcon.style.marginBottom = -18f;
            ribbonWrap.Add(ribbonIcon);
            ribbon = UIKit.Col();
            ribbon.style.alignItems = Align.Center;
            UIKit.Pad(ribbon, 12f, 46f, 14f, 46f);
            ribbon.style.maxWidth = 1300f;
            ribbon.style.rotate = new Rotate(Angle.Degrees(-2.5f));
            UIKit.Radius(ribbon, 10f);
            ribbonWrap.Add(ribbon);
            ribbonTitle = UIKit.HudText("", 46f);
            ribbonTitle.style.whiteSpace = WhiteSpace.Normal;
            ribbonTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            ribbon.Add(ribbonTitle);
            ribbonBand = UIKit.Row();
            ribbonBand.style.marginTop = 8f;
            ribbonBand.style.backgroundColor = new Color(0xF4 / 255f, 0xEF / 255f, 0xE6 / 255f, 0.88f);
            UIKit.Radius(ribbonBand, 10f);
            UIKit.Pad(ribbonBand, 6f, 18f, 6f, 18f);
            ribbonBand.style.maxWidth = 1100f;
            ribbonSub = UIKit.Text("", 24f, UIPalette.Rgb(0x1E1B2B), true);
            ribbonSub.style.whiteSpace = WhiteSpace.Normal;
            ribbonSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            ribbonBand.Add(ribbonSub);
            ribbonWrap.Add(ribbonBand);
            ribbonWrap.style.display = DisplayStyle.None;

            summaryWrap = UIKit.Col();
            UIKit.Abs(summaryWrap, 0f, null, 0f, 230f);
            summaryWrap.style.alignItems = Align.Center;
            root.Add(summaryWrap);

            UIKit.IgnorePicking(root);
        }

        bool Seen(string key, float window)
        {
            if (string.IsNullOrEmpty(key)) return false;
            float now = Time.unscaledTime;
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                if (now - recent[i].time > window) { recent.RemoveAt(i); continue; }
                if (recent[i].key == key) return true;
            }
            recent.Add(new RecentKey { key = key, time = now });
            return false;
        }

        // ------------------------------------------------------------------ toasts (L.10)
        /// GameEvents.Notify → icon + one short line + optional chip. The UI listens to Notify only (never Toast too).
        public void Notify(NotifyKind kind, string line, string chip)
        {
            if (string.IsNullOrEmpty(line)) return;
            // the host runs a guest's command: that guest's own lines (logbook page, warnings, hints, clips, delivery)
            // are not the host's business; money / quest lines name who did it (forwarding is unaffected)
            if (NetState.HostRunningGuestCommand)
            {
                switch (kind)
                {
                    case NotifyKind.Collectible: case NotifyKind.Delivery: case NotifyKind.Warning: case NotifyKind.Info: case NotifyKind.Clip:
                        return;
                    case NotifyKind.Money: case NotifyKind.Quest:
                    {
                        var p = Net.Find(NetCommands.ScopeFrom);
                        if (p != null && !p.IsLocal && !string.IsNullOrEmpty(p.name)) line = p.name + ": " + line;
                        break;
                    }
                }
            }
            line = Short(line, 90);   // L.10 lines are short; legacy sentences may wrap to a second line instead of being cut
            string key = (int)kind + "|" + line + "|" + (chip ?? "");
            // identical toast on screen → "×n"
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                if (t.key != key) continue;
                t.count++;
                t.age = Mathf.Min(t.age, 0.3f);
                SetToastText(t);
                t.el.style.scale = new Scale(new Vector2(1.08f, 1.08f));
                return;
            }
            for (int i = 0; i < toastQueue.Count; i++)
            {
                if (toastQueue[i].key != key) continue;
                var q = toastQueue[i];
                q.count++;
                toastQueue[i] = q;
                return;
            }
            if (toastQueue.Count >= MaxToastQueue) toastQueue.RemoveAt(0);   // drop the oldest
            toastQueue.Add(new PendingToast { kind = kind, text = line, chip = chip, key = key, count = 1 });
        }

        /// Legacy plain text toast (= Info).
        public void Toast(string text) => Notify(NotifyKind.Info, text, null);

        static string Short(string s, int max)
        {
            if (s == null) return "";
            int nl = s.IndexOf('\n');
            if (nl >= 0) s = s.Substring(0, nl);
            s = s.Trim();
            return s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…";
        }

        ToastItem MakeToast()
        {
            if (toastPool.Count > 0)
            {
                var t = toastPool[toastPool.Count - 1];
                toastPool.RemoveAt(toastPool.Count - 1);
                return t;
            }
            var n = new ToastItem { el = UIKit.Row() };
            n.el.pickingMode = PickingMode.Ignore;
            n.el.style.justifyContent = Justify.FlexEnd;
            n.el.style.marginTop = 6f;
            n.icon = new UIHudGlyph(UIGlyphs.Bubble, UIPalette.Turquoise, 26f);
            n.icon.style.marginRight = 10f;
            n.el.Add(n.icon);
            n.line = UIKit.HudText("", 23f);
            n.line.style.whiteSpace = WhiteSpace.Normal;
            n.line.style.maxWidth = 470f;
            n.line.style.unityTextAlign = TextAnchor.MiddleRight;
            n.line.style.flexShrink = 1;
            n.line.style.overflow = Overflow.Hidden;
            n.line.style.textOverflow = TextOverflow.Ellipsis;
            n.el.Add(n.line);
            n.chip = UIKit.HudText("", 23f, UIPalette.Sun);
            n.chip.style.marginLeft = 10f;
            n.chip.style.whiteSpace = WhiteSpace.NoWrap;
            // a long chip (site names …) ran across the held camera model – keep toasts compact (L.10)
            n.chip.style.maxWidth = 220f;
            n.chip.style.overflow = Overflow.Hidden;
            n.chip.style.textOverflow = TextOverflow.Ellipsis;
            n.el.Add(n.chip);
            UIKit.Transition(n.el, 0.15f, "scale");
            return n;
        }

        void SetToastText(ToastItem t)
        {
            t.line.text = t.text;
            string c = t.chipText ?? "";
            if (t.count > 1) c = c.Length > 0 ? c + "  ×" + t.count : "×" + t.count;
            t.chip.text = c;
            UIKit.Show(t.chip, c.Length > 0);
        }

        void TickToasts(float dt)
        {
            // start queued toasts while fewer than 2 are on screen
            while (toastQueue.Count > 0 && toasts.Count < MaxToastsShown)
            {
                var q = toastQueue[0];
                toastQueue.RemoveAt(0);
                var t = MakeToast();
                t.kind = q.kind; t.text = q.text; t.chipText = q.chip; t.key = q.key; t.count = q.count; t.age = 0f;
                t.icon.SetDraw(UIGlyphs.ForNotify(q.kind));
                t.icon.SetColor(UIGlyphs.NotifyColor(q.kind));
                t.chip.style.color = ChipColor(q.kind, q.chip);
                SetToastText(t);
                t.el.style.opacity = 0f;
                toastStack.Add(t.el);
                toasts.Add(t);
            }
            // a backlog moves the visible ones along faster
            float speed = toastQueue.Count > 0 ? 1.8f : 1f;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                t.age += dt * speed;
                if (t.age >= ToastLife)
                {
                    t.el.RemoveFromHierarchy();
                    toasts.RemoveAt(i);
                    toastPool.Add(t);
                    continue;
                }
                float a = Mathf.Min(1f, t.age / 0.22f);
                float outA = Mathf.Clamp01((ToastLife - t.age) / ToastFade);
                t.el.style.opacity = Mathf.Min(a, outA);
                float e = 1f - (1f - a) * (1f - a);
                t.el.style.translate = new Translate(30f * (1f - e), 0f);
                if (t.age > 0.2f) t.el.style.scale = new Scale(Vector2.one);
            }
        }

        static Color ChipColor(NotifyKind k, string chip)
        {
            if (!string.IsNullOrEmpty(chip) && (chip[0] == '−' || chip[0] == '-')) return UIPalette.Coral;
            switch (k)
            {
                case NotifyKind.Money: return UIPalette.Leaf;
                case NotifyKind.Followers: return UIPalette.Turquoise;
                case NotifyKind.Warning: return UIPalette.Warn;
                default: return UIPalette.Sun;
            }
        }

        // ------------------------------------------------------------------ cards (max 1 on screen)
        /// Push card: icon tile + one line + optional chip (+ image). key deduplicates within 6 s.
        public void Push(UIGlyph.DrawFn icon, Color color, string title, string sub, string key = null, Texture2D image = null, bool ping = true)
        {
            if (string.IsNullOrEmpty(title)) return;
            if (Seen("p:" + (key ?? title + sub), 6f)) return;
            if (LowPriority(key))
            {
                // only the newest chat card waits
                for (int i = cards.Count - 1; i >= 0; i--) if (LowPriority(cards[i].key)) cards.RemoveAt(i);
            }
            Enqueue(new Card { type = CardType.Push, icon = icon, color = color, title = Short(title, 40), sub = sub != null ? Short(sub, 32) : null, key = key, image = image, ping = ping });
        }

        static bool LowPriority(string key) => key != null && key.StartsWith("msg:", System.StringComparison.Ordinal);

        void Enqueue(Card c)
        {
            if (cards.Count >= MaxCardQueue)
            {
                int drop = -1;
                for (int i = 0; i < cards.Count; i++) if (LowPriority(cards[i].key) || cards[i].type == CardType.Push) { drop = i; break; }
                if (drop < 0) drop = 0;
                cards.RemoveAt(drop);
            }
            cards.Add(c);
        }

        /// Quest cards are gone (L.10: the quest line is always on screen). Kept for older callers.
        public void Quest(string text) { }
        public void UpdateQuestText(string text) { }

        public void Banner(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int nl = text.IndexOf('\n');
            string title = nl >= 0 ? text.Substring(0, nl) : text;
            string sub = nl >= 0 ? text.Substring(nl + 1) : null;
            if (Seen("r:" + text, 8f)) return;
            Enqueue(new Card { type = CardType.Ribbon, title = title, sub = sub, color = color, key = text });
        }

        public void SpeciesDiscovered(SpeciesDef s, float cm, SizeTier tier)
        {
            if (s == null) return;
            Push(UIGlyphs.Book, UIPalette.Turquoise, Loc.Tr("Neu im Sealog!", "New in the Sealog!"), s.Name, "disc_" + s.id, UICreatureIcons.Get(s));
        }

        public void SizeRecord(SpeciesDef s, float cm, SizeTier tier)
        {
            if (s == null) return;
            Push(UIGlyphs.Star, UIPalette.Sun, Loc.Tr("Rekord!", "Record!"), s.Name + " · " + Loc.Length(cm),
                 "rec_" + s.id + "_" + Mathf.RoundToInt(cm), UICreatureIcons.Get(s));
        }

        public void Milestone(Milestone m)
        {
            if (m == null) return;
            // same text as the banner Progression raises, so the duplicate is dropped; a rank milestone shows the rank
            // (title + colour) – UIController skips the separate "Neuer Rang" ribbon for it
            int r = m.RankIndex;
            Banner(m.BannerText, r > 0 ? UIGlyphs.RankColor(r) : UIPalette.Sun);
        }

        public void SiteUnlocked(DiveSite site)
        {
            if (site == null || (site.prevSiteId == null && !site.IsStorySite)) return;
            Banner(Loc.Tr("Neuer Tauchplatz", "New dive site") + "\n" + site.Name, new Color(0.18f, 0.77f, 0.75f));
        }

        /// Q50 rank-up banner „Neuer Rang: {rank}!“.
        public void RankUp(int rank)
        {
            Banner(Loc.Tr("Neuer Rang: ", "New rank: ") + Progression.RankName(rank) + "!", UIGlyphs.RankColor(rank));
        }

        /// F.6 achievement card (trophy + name).
        public void Achievement(AchievementDef a)
        {
            if (a == null) return;
            if (Seen("a:" + a.id, 30f)) return;
            Enqueue(new Card { type = CardType.Achievement, icon = UIGlyphs.Trophy, color = UIPalette.Rgb(0xE8C65A),
                title = Loc.Tr("Erfolg", "Achievement"), sub = Loc.Tr(a.nameDe, a.nameEn), key = "ach:" + a.id, ping = true });
        }

        void TickCards(float dt)
        {
            if (current == null)
            {
                if (cards.Count == 0 || HoldCards || UIState.CharacterOpen) return;
                // the big centre cards (ribbon, morning summary) wait until the phone is put away – they were drawn
                // across the phone screen (playtest round 2); the small push cards top-right may still come
                if ((UIState.PhoneOpen || UIState.InventoryOpen) && cards[0].type != CardType.Push && cards[0].type != CardType.Achievement) return;
                current = cards[0];
                cards.RemoveAt(0);
                cardAge = 0f;
                ShowCard(current);
            }
            cardAge += dt;
            if (cards.Count > 1) cardAge += dt * 0.6f;   // backlog: move faster
            switch (current.type)
            {
                case CardType.Ribbon: TickRibbon(); break;
                case CardType.Summary: TickSummary(); break;
                default: TickPush(); break;
            }
        }

        void ShowCard(Card c)
        {
            switch (c.type)
            {
                case CardType.Ribbon:
                {
                    ribbonTitle.text = c.title;
                    bool hasSub = !string.IsNullOrEmpty(c.sub);
                    ribbonSub.text = hasSub ? c.sub : "";
                    UIKit.Show(ribbonBand, hasSub);
                    UIKit.Image(ribbonIcon, c.image);
                    UIKit.Show(ribbonIcon, c.image != null);
                    var col = c.color;
                    float lum = col.r * 0.299f + col.g * 0.587f + col.b * 0.114f;
                    if (lum > 0.62f) col = UIPalette.Darken(col, 0.22f);
                    col.a = 0.92f;
                    ribbon.style.backgroundColor = col;
                    ribbon.style.borderBottomWidth = 5f;
                    ribbon.style.borderBottomColor = UIPalette.Darken(col, 0.3f);
                    ribbonWrap.style.display = DisplayStyle.Flex;
                    UIAudio.Pop();
                    break;
                }
                case CardType.Summary:
                    summaryCard = BuildSummary(c.summary);
                    summaryWrap.Add(summaryCard);
                    UIAudio.Ping();
                    break;
                default:
                {
                    pushIconHost.Clear();
                    pushIconHost.Add(UIWidgets.IconTile(c.icon ?? UIGlyphs.Bell, c.color, 44f));
                    pushTitle.text = c.title;
                    bool hasSub = !string.IsNullOrEmpty(c.sub);
                    pushChip.text = hasSub ? c.sub : "";
                    pushChip.style.backgroundColor = UIPalette.A(c.color, 0.22f);
                    pushChip.style.color = UIWidgets.Ink(c.color);
                    UIKit.Show(pushChip, hasSub);
                    UIKit.Image(pushImage, c.image);
                    UIKit.Show(pushImage, c.image != null);
                    pushWrap.style.display = DisplayStyle.Flex;
                    if (c.ping) UIAudio.Ping();
                    break;
                }
            }
        }

        void EndCard()
        {
            pushWrap.style.display = DisplayStyle.None;
            ribbonWrap.style.display = DisplayStyle.None;
            if (summaryCard != null) { summaryCard.RemoveFromHierarchy(); summaryCard = null; }
            current = null;
        }

        void TickPush()
        {
            float hold = current.type == CardType.Achievement ? PushHold + 1f : PushHold;
            float total = PushIn + hold + PushOut;
            if (cardAge >= total) { EndCard(); return; }
            float inT = Mathf.Clamp01(cardAge / PushIn);
            float outT = Mathf.Clamp01((total - cardAge) / PushOut);
            float e = 1f - (1f - inT) * (1f - inT) * (1f - inT);
            pushWrap.style.translate = new Translate(140f * (1f - e) + 140f * (1f - outT), 0f);
            pushWrap.style.opacity = Mathf.Min(inT * 1.5f, 1f) * Mathf.Min(1f, outT * 1.6f);
        }

        void TickRibbon()
        {
            float hold = RibbonHold + (string.IsNullOrEmpty(current.sub) ? 0f : 0.8f);
            float total = RibbonIn + hold + RibbonOut;
            if (cardAge >= total) { EndCard(); return; }
            float inT = Mathf.Clamp01(cardAge / RibbonIn);
            float outT = Mathf.Clamp01((total - cardAge) / RibbonOut);
            float e = 1f - (1f - inT) * (1f - inT) * (1f - inT);
            ribbonWrap.style.opacity = Mathf.Min(inT, outT);
            ribbon.style.translate = new Translate(-260f * (1f - e) + 260f * (1f - outT), 0f);
            float s = Mathf.Lerp(0.9f, 1f, e);
            ribbonWrap.style.scale = new Scale(new Vector2(s, s));
        }

        // ------------------------------------------------------------------ morning card
        public void UploadResolved(VideoRecord v)
        {
            if (v == null) return;
            var s = UIKit.State;
            if (pendingSummary == null)
            {
                pendingSummary = new SummaryData { day = s != null ? s.day : v.day + 1 };
                summaryDelay = 1.6f;   // let the wake-up fade finish
            }
            pendingSummary.videos++;
            pendingSummary.views += v.views;
            pendingSummary.followers += v.followersGained;
            pendingSummary.money += v.money;
            var top = SpeciesDatabase.Get(v.topSpeciesId);
            if (top != null && pendingSummary.topSpecies == null) pendingSummary.topSpecies = top.Name;
        }

        void TickPendingSummary(float dt)
        {
            if (pendingSummary == null) return;
            summaryDelay -= dt;
            if (summaryDelay > 0f || UIState.MainMenuOpen) return;
            // the morning card goes first
            cards.Insert(0, new Card { type = CardType.Summary, summary = pendingSummary, key = "summary" });
            pendingSummary = null;
        }

        void TickSummary()
        {
            if (summaryCard == null || cardAge >= SummaryLife || UIState.MainMenuOpen) { EndCard(); return; }
            float a = Mathf.Min(Mathf.Clamp01(cardAge / 0.35f), Mathf.Clamp01((SummaryLife - cardAge) / 0.5f));
            summaryCard.style.opacity = a;
            summaryCard.style.translate = new Translate(0f, 24f * (1f - Mathf.Clamp01(cardAge / 0.35f)));
        }

        /// L.10: three icons with numbers (views · +followers · +$) + the best clip's species name.
        static VisualElement BuildSummary(SummaryData it)
        {
            var card = UIKit.Card(new Color(1f, 1f, 1f, 0.95f), 24f, 20f);
            card.style.width = 600f;
            card.style.opacity = 0f;
            UIKit.Lift(card, 1.5f);

            var head = UIKit.Row();
            head.Add(UIWidgets.IconTile(UIGlyphs.Sun, UIPalette.Sun, 48f));
            var htxt = UIKit.Col();
            htxt.style.marginLeft = 14f;
            htxt.style.flexShrink = 1;
            htxt.Add(UIKit.Text(Loc.Tr("Guten Morgen · Tag ", "Good morning · Day ") + it.day, 26f, UIPalette.Ink, true));
            if (!string.IsNullOrEmpty(it.topSpecies))
            {
                var r = UIKit.Row();
                r.Add(new UIGlyph(UIGlyphs.Star, UIPalette.SunInk, 20f));
                r.Add(UIKit.Text(" " + it.topSpecies + (it.videos > 1 ? "  · " + it.videos + " Videos" : ""), 18f, UIPalette.InkSoft, true));
                htxt.Add(r);
            }
            head.Add(htxt);
            card.Add(head);

            var stats = UIKit.Row();
            stats.style.marginTop = 14f;
            stats.style.alignItems = Align.Stretch;
            stats.style.backgroundColor = UIPalette.CardAlt;
            UIKit.Radius(stats, 16f);
            UIKit.Pad(stats, 6f);
            stats.Add(UIWidgets.IconStat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(it.views), UIPalette.Ink, 24f));
            stats.Add(UIKit.HSpacer(6f));
            stats.Add(UIWidgets.IconStat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Signed(it.followers), UIPalette.TurquoiseInk, 24f));
            stats.Add(UIKit.HSpacer(6f));
            stats.Add(UIWidgets.IconStat(UIGlyphs.Coin, UIPalette.Sun, UIKit.SignedMoney(it.money), UIPalette.LeafInk, 24f));
            card.Add(stats);
            UIKit.IgnorePicking(card);
            return card;
        }

        public void Clear()
        {
            for (int i = 0; i < toasts.Count; i++) { toasts[i].el.RemoveFromHierarchy(); toastPool.Add(toasts[i]); }
            toasts.Clear();
            toastQueue.Clear();
            cards.Clear();
            EndCard();
        }

        public void Tick(float dt)
        {
            bool show = !UIState.LoadingOpen && !UIState.CharacterOpen;
            UIKit.Show(root, show);
            if (!show) return;
            // the inventory card covers the lower right: lift the toasts a bit
            toastStack.style.bottom = UIState.InventoryOpen ? 60f : 150f;
            TickToasts(dt);
            TickPendingSummary(dt);
            TickCards(dt);
        }
    }
}
