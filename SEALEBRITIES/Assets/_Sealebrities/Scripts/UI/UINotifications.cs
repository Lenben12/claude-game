using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Notifications (art bible §1.14):
    ///  - toasts: short outlined white lines stacked above the money/followers block (no boxes)
    ///  - push banners: phone-style white card sliding in at the top (app icon + one line), 3.5 s, ping
    ///  - celebration ribbons: Safari-TV style slanted banner for milestones, new sites, new gear
    ///  - morning card: results of yesterday's uploads
    public class UINotifications
    {
        class UIToastItem
        {
            public VisualElement el;
            public float age;
        }

        class UIPushItem
        {
            public UIGlyph.DrawFn icon;
            public Color color;
            public string title, sub, key;
            public Texture2D image;
            public bool ping;
        }

        class UIRibbonItem
        {
            public string title, sub, key;
            public Color color;
            public Texture2D icon;
        }

        class UISummaryItem
        {
            public int day, videos;
            public long views, followers, money;
            public string title;
            public bool monetized;
        }

        struct UIRecentKey
        {
            public string key;
            public float time;
        }

        const float ToastLife = 4.2f;
        const float PushIn = 0.35f, PushHold = 3.5f, PushOut = 0.35f;
        const float RibbonIn = 0.35f, RibbonHold = 3.4f, RibbonOut = 0.45f;
        const float SummaryLife = 9f;

        readonly VisualElement root, toastStack;
        readonly List<UIToastItem> toasts = new List<UIToastItem>();
        readonly List<UIRecentKey> recent = new List<UIRecentKey>();

        // push banner
        readonly VisualElement pushWrap, pushCard, pushIconHost, pushImage;
        readonly Label pushTitle, pushSub;
        readonly List<UIPushItem> pushes = new List<UIPushItem>();   // FIFO (index 0 = next)
        const int MaxQueuedPushes = 6;
        string questText = "";
        UIPushItem currentPush;
        float pushAge;

        // ribbon
        readonly VisualElement ribbonWrap, ribbon, ribbonIcon;
        readonly Label ribbonTitle, ribbonSub;
        readonly Queue<UIRibbonItem> ribbons = new Queue<UIRibbonItem>();
        UIRibbonItem currentRibbon;
        float ribbonAge;

        // morning summary
        readonly VisualElement summaryWrap;
        readonly Queue<UISummaryItem> summaries = new Queue<UISummaryItem>();
        UISummaryItem pendingSummary, currentSummary;
        VisualElement summaryCard;
        float summaryAge, summaryDelay;

        public UINotifications(VisualElement layer)
        {
            root = new VisualElement { name = "sb-notes" };
            UIKit.FillParent(root);
            layer.Add(root);

            // toasts above the money block (bottom-right)
            toastStack = UIKit.Col();
            UIKit.Abs(toastStack, null, null, 32f, 150f);
            toastStack.style.width = 520f;
            toastStack.style.alignItems = Align.FlexEnd;
            toastStack.style.flexDirection = FlexDirection.ColumnReverse;
            root.Add(toastStack);

            // push banner (top centre)
            pushWrap = UIKit.Col();
            UIKit.Abs(pushWrap, 0f, 18f, 0f, null);
            pushWrap.style.alignItems = Align.Center;
            root.Add(pushWrap);
            var pushHolder = UIKit.Box();
            pushHolder.style.width = 480f;
            pushWrap.Add(pushHolder);
            var pushShadow = UIKit.Box();
            UIKit.Abs(pushShadow, 4f, 7f, 4f, -6f);
            UIKit.Radius(pushShadow, 22f);
            pushShadow.style.backgroundColor = new Color(0f, 0f, 0f, 0.16f);
            pushHolder.Add(pushShadow);
            pushCard = UIKit.Row();
            pushCard.style.minHeight = 76f;
            pushCard.style.backgroundColor = new Color(1f, 1f, 1f, 0.95f);
            UIKit.Radius(pushCard, 20f);
            UIKit.Pad(pushCard, 12f, 16f, 12f, 14f);
            pushHolder.Add(pushCard);
            pushIconHost = UIKit.Box();
            UIKit.Size(pushIconHost, 44f, 44f);
            pushCard.Add(pushIconHost);
            var pushText = UIKit.Col();
            pushText.style.marginLeft = 12f;
            pushText.style.flexGrow = 1;
            pushText.style.flexShrink = 1;
            pushTitle = UIKit.Text("", 20f, UIPalette.Ink, true);
            pushTitle.style.whiteSpace = WhiteSpace.Normal;
            pushText.Add(pushTitle);
            pushSub = UIKit.Text("", 16f, UIPalette.InkSoft);
            pushSub.style.whiteSpace = WhiteSpace.Normal;
            pushSub.style.marginTop = 1f;
            pushText.Add(pushSub);
            pushCard.Add(pushText);
            pushImage = UIKit.Box();
            UIKit.Size(pushImage, 72f, 50f);
            pushImage.style.marginLeft = 8f;
            pushCard.Add(pushImage);
            pushWrap.style.display = DisplayStyle.None;

            // celebration ribbon (upper third)
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
            ribbonSub = UIKit.HudText("", 22f, UIPalette.Sand, false);
            ribbonSub.style.whiteSpace = WhiteSpace.Normal;
            ribbonSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            ribbonSub.style.marginTop = 2f;
            ribbonSub.style.maxWidth = 1100f;
            ribbonWrap.Add(ribbonSub);
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
            recent.Add(new UIRecentKey { key = key, time = now });
            return false;
        }

        // ------------------------------------------------------------------ toasts
        public void Toast(string text)
        {
            if (string.IsNullOrEmpty(text) || Seen("t:" + text, 2.5f)) return;
            var row = UIKit.Row();
            row.pickingMode = PickingMode.Ignore;
            row.style.justifyContent = Justify.FlexEnd;
            row.style.marginTop = 6f;
            var dot = new UIHudGlyph(UIGlyphs.Bubble, UIPalette.Turquoise, 18f);
            dot.style.marginRight = 8f;
            dot.style.alignSelf = Align.FlexStart;
            dot.style.marginTop = 3f;
            row.Add(dot);
            var l = UIKit.HudText(text, 21f);
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1;
            l.style.maxWidth = 480f;
            l.style.unityTextAlign = TextAnchor.UpperRight;
            row.Add(l);
            row.style.opacity = 0f;
            toastStack.Add(row);
            toasts.Add(new UIToastItem { el = row, age = 0f });
            // keep the stack short: age out the oldest
            int alive = 0;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                alive++;
                if (alive > 4 && toasts[i].age < ToastLife - 0.4f) toasts[i].age = ToastLife - 0.4f;
            }
        }

        void TickToasts(float dt)
        {
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                t.age += dt;
                if (t.age >= ToastLife)
                {
                    t.el.RemoveFromHierarchy();
                    toasts.RemoveAt(i);
                    continue;
                }
                float a = Mathf.Min(1f, t.age / 0.25f);
                float outA = Mathf.Clamp01((ToastLife - t.age) / 0.4f);
                t.el.style.opacity = Mathf.Min(a, outA);
                float e = 1f - (1f - a) * (1f - a);
                t.el.style.translate = new Translate(30f * (1f - e), 0f);
            }
        }

        // ------------------------------------------------------------------ push banners
        /// Phone-style notification (app icon + title + one line). key deduplicates within 6 s.
        public void Push(UIGlyph.DrawFn icon, Color color, string title, string sub, string key = null, Texture2D image = null, bool ping = true)
        {
            if (string.IsNullOrEmpty(title)) return;
            bool quest = IsQuestKey(key);
            if (quest)
            {
                // coalesce: a newer task replaces a queued (now stale) one
                for (int i = pushes.Count - 1; i >= 0; i--) if (IsQuestKey(pushes[i].key)) pushes.RemoveAt(i);
            }
            if (pushes.Count >= MaxQueuedPushes)
            {
                // full: drop the oldest low-priority card (task / chat) – never the new important one
                int drop = -1;
                for (int i = 0; i < pushes.Count; i++) if (LowPriority(pushes[i].key)) { drop = i; break; }
                if (drop < 0)
                {
                    if (LowPriority(key)) return;
                    drop = 0;
                }
                pushes.RemoveAt(drop);
            }
            if (Seen("p:" + (key ?? title + sub), 6f)) return;
            pushes.Add(new UIPushItem { icon = icon, color = color, title = title, sub = sub, key = key, image = image, ping = ping });
        }

        static bool IsQuestKey(string key) => key != null && key.StartsWith("quest:", System.StringComparison.Ordinal);
        static bool LowPriority(string key) => key != null && (key.StartsWith("quest:", System.StringComparison.Ordinal) || key.StartsWith("msg:", System.StringComparison.Ordinal));

        public void Quest(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            questText = text;
            Push(UIGlyphs.Tasks, UIPalette.Leaf, Loc.Tr("Neue Aufgabe", "New task"), text, "quest:" + text);
        }

        /// The current task text (progress counters change without a new card): a queued task card shows the newest text.
        public void UpdateQuestText(string text) => questText = text ?? "";

        void TickPush(float dt)
        {
            if (currentPush == null)
            {
                if (pushes.Count == 0) return;
                currentPush = pushes[0];
                pushes.RemoveAt(0);
                if (IsQuestKey(currentPush.key))
                {
                    // the task was finished/replaced meanwhile → drop; same task with newer counters → newest text
                    if (string.IsNullOrEmpty(questText)) { currentPush = null; return; }
                    currentPush.sub = questText;
                }
                pushAge = 0f;
                pushIconHost.Clear();
                pushIconHost.Add(UIWidgets.IconTile(currentPush.icon ?? UIGlyphs.Bell, currentPush.color, 44f));
                pushTitle.text = currentPush.title;
                bool hasSub = !string.IsNullOrEmpty(currentPush.sub);
                pushSub.text = hasSub ? currentPush.sub : "";
                UIKit.Show(pushSub, hasSub);
                UIKit.Image(pushImage, currentPush.image);
                UIKit.Show(pushImage, currentPush.image != null);
                pushWrap.style.display = DisplayStyle.Flex;
                if (currentPush.ping) UIAudio.Ping();
            }
            pushAge += dt;
            if (pushes.Count > 1) pushAge += dt * 0.7f;   // backlog: move faster
            float total = PushIn + PushHold + PushOut;
            if (pushAge >= total)
            {
                pushWrap.style.display = DisplayStyle.None;
                currentPush = null;
                return;
            }
            float inT = Mathf.Clamp01(pushAge / PushIn);
            float outT = Mathf.Clamp01((total - pushAge) / PushOut);
            float e = 1f - (1f - inT) * (1f - inT) * (1f - inT);
            float o = outT * outT;
            pushWrap.style.translate = new Translate(0f, -110f * (1f - e) - 110f * (1f - o));
            pushWrap.style.opacity = Mathf.Min(inT * 1.5f, 1f) * Mathf.Min(1f, outT * 1.6f);
        }

        // ------------------------------------------------------------------ celebration ribbons
        public void Banner(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int nl = text.IndexOf('\n');
            string title = nl >= 0 ? text.Substring(0, nl) : text;
            string sub = nl >= 0 ? text.Substring(nl + 1) : null;
            EnqueueRibbon(new UIRibbonItem { title = title, sub = sub, color = color, key = text });
        }

        public void SpeciesDiscovered(SpeciesDef s, float cm, SizeTier tier)
        {
            if (s == null) return;
            Push(UIGlyphs.Book, UIPalette.Turquoise, Loc.Tr("Neu im Sealog: ", "New in the Sealog: ") + s.Name,
                 Loc.Length(cm) + " · " + Loc.TierName(tier), "disc_" + s.id, UICreatureIcons.Get(s));
        }

        public void SizeRecord(SpeciesDef s, float cm, SizeTier tier)
        {
            if (s == null) return;
            Push(UIGlyphs.Star, UIPalette.Sun, Loc.Tr("Neuer Rekord!", "New record!"),
                 s.Name + " · " + Loc.Length(cm) + " · " + Loc.TierName(tier) + " " + UIKit.Mult(Progression.SizeMultiplier(tier)),
                 "rec_" + s.id + "_" + Mathf.RoundToInt(cm), UICreatureIcons.Get(s));
        }

        public void Milestone(Milestone m)
        {
            if (m == null) return;
            // same text as the banner Progression raises, so the duplicate is dropped
            Banner(m.Title + "\n" + m.Description, UIPalette.Sun);
        }

        public void SiteUnlocked(DiveSite site)
        {
            if (site == null || site.prevSiteId == null) return;
            Banner(Loc.Tr("Neuer Tauchplatz: ", "New dive site: ") + site.Name, new Color(0.18f, 0.77f, 0.75f));
        }

        void EnqueueRibbon(UIRibbonItem item)
        {
            if (Seen("r:" + item.key, 8f)) return;
            if (ribbons.Count > 5) return;   // never build up an endless backlog
            ribbons.Enqueue(item);
        }

        void TickRibbon(float dt)
        {
            if (currentRibbon == null)
            {
                if (ribbons.Count == 0) return;
                currentRibbon = ribbons.Dequeue();
                ribbonAge = 0f;
                ribbonTitle.text = currentRibbon.title;
                bool hasSub = !string.IsNullOrEmpty(currentRibbon.sub);
                ribbonSub.text = hasSub ? currentRibbon.sub : "";
                UIKit.Show(ribbonSub, hasSub);
                UIKit.Image(ribbonIcon, currentRibbon.icon);
                UIKit.Show(ribbonIcon, currentRibbon.icon != null);
                var c = currentRibbon.color;
                // keep the ribbon saturated and dark enough for white text
                float lum = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                if (lum > 0.62f) c = UIPalette.Darken(c, 0.22f);
                c.a = 0.92f;
                ribbon.style.backgroundColor = c;
                ribbon.style.borderBottomWidth = 5f;
                ribbon.style.borderBottomColor = UIPalette.Darken(c, 0.3f);
                ribbonWrap.style.display = DisplayStyle.Flex;
                UIAudio.Pop();
            }
            ribbonAge += dt;
            float hold = RibbonHold + (string.IsNullOrEmpty(currentRibbon.sub) ? 0f : 0.8f);
            float total = RibbonIn + hold + RibbonOut;
            if (ribbons.Count > 1) ribbonAge += dt * 0.6f;
            if (ribbonAge >= total)
            {
                ribbonWrap.style.display = DisplayStyle.None;
                currentRibbon = null;
                return;
            }
            float inT = Mathf.Clamp01(ribbonAge / RibbonIn);
            float outT = Mathf.Clamp01((total - ribbonAge) / RibbonOut);
            float e = 1f - (1f - inT) * (1f - inT) * (1f - inT);
            ribbonWrap.style.opacity = Mathf.Min(inT, outT);
            ribbon.style.translate = new Translate(-260f * (1f - e) + 260f * (1f - outT), 0f);
            float s = Mathf.Lerp(0.9f, 1f, e);
            ribbonWrap.style.scale = new Scale(new Vector2(s, s));
        }

        // ------------------------------------------------------------------ morning summary
        public void UploadResolved(VideoRecord v)
        {
            if (v == null) return;
            var s = UIKit.State;
            bool monetized = s != null && s.followers >= Progression.MonetizationFollowers;
            if (pendingSummary == null)
            {
                pendingSummary = new UISummaryItem { day = s != null ? s.day : v.day + 1, title = v.title, monetized = monetized };
                summaryDelay = 1.6f;   // let the wake-up fade finish
            }
            pendingSummary.videos++;
            pendingSummary.views += v.views;
            pendingSummary.followers += v.followersGained;
            pendingSummary.money += v.money;
            pendingSummary.monetized |= v.money > 0;
            if (pendingSummary.videos > 1) pendingSummary.title = null;
        }

        void TickSummary(float dt)
        {
            if (pendingSummary != null)
            {
                summaryDelay -= dt;
                if (summaryDelay <= 0f) { summaries.Enqueue(pendingSummary); pendingSummary = null; }
            }
            if (currentSummary == null)
            {
                if (summaries.Count == 0 || UIState.MainMenuOpen) return;
                currentSummary = summaries.Dequeue();
                summaryAge = 0f;
                summaryCard = BuildSummary(currentSummary);
                summaryWrap.Add(summaryCard);
                UIAudio.Ping();
            }
            summaryAge += dt;
            if (summaryAge >= SummaryLife || UIState.MainMenuOpen)
            {
                summaryCard.RemoveFromHierarchy();
                summaryCard = null;
                currentSummary = null;
                return;
            }
            float a = Mathf.Min(Mathf.Clamp01(summaryAge / 0.35f), Mathf.Clamp01((SummaryLife - summaryAge) / 0.5f));
            summaryCard.style.opacity = a;
            summaryCard.style.translate = new Translate(0f, 24f * (1f - Mathf.Clamp01(summaryAge / 0.35f)));
        }

        VisualElement BuildSummary(UISummaryItem it)
        {
            var card = UIKit.Card(new Color(1f, 1f, 1f, 0.95f), 24f, 22f);
            card.style.width = 620f;
            card.style.opacity = 0f;
            UIKit.Lift(card, 1.5f);

            var head = UIKit.Row();
            head.Add(UIWidgets.IconTile(UIGlyphs.Play, UIPalette.Danger, 48f));
            var htxt = UIKit.Col();
            htxt.style.marginLeft = 14f;
            htxt.style.flexShrink = 1;
            htxt.Add(UIKit.Text((Loc.Tr("Guten Morgen · Tag ", "Good morning · Day ") + it.day).ToUpperInvariant(), 14f, UIPalette.SunInk, true));
            htxt.Add(UIKit.Text(it.videos > 1
                ? Loc.Tr($"{it.videos} Videos sind online!", $"{it.videos} videos are online!")
                : Loc.Tr("Dein Video ist online!", "Your video is online!"), 26f, UIPalette.Ink, true));
            head.Add(htxt);
            card.Add(head);

            if (!string.IsNullOrEmpty(it.title))
            {
                var t = UIKit.Para("„" + it.title + "“", 16f, UIPalette.InkSoft);
                t.style.marginTop = 10f;
                card.Add(t);
            }

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
            stats.Add(it.money > 0 || it.monetized
                ? UIWidgets.IconStat(UIGlyphs.Coin, UIPalette.Sun, UIKit.SignedMoney(it.money), UIPalette.LeafInk, 24f)
                : UIWidgets.IconStat(UIGlyphs.Lock, UIPalette.Silhouette, Loc.Tr("ab 100", "from 100"), UIPalette.InkSoft, 24f));
            card.Add(stats);
            UIKit.IgnorePicking(card);
            return card;
        }

        public void Clear()
        {
            for (int i = 0; i < toasts.Count; i++) toasts[i].el.RemoveFromHierarchy();
            toasts.Clear();
            ribbons.Clear();
            pushes.Clear();
            currentRibbon = null;
            currentPush = null;
            ribbonWrap.style.display = DisplayStyle.None;
            pushWrap.style.display = DisplayStyle.None;
        }

        public void Tick(float dt)
        {
            bool show = !UIState.LoadingOpen;
            UIKit.Show(root, show);
            if (!show) return;
            // the inventory card covers the lower right: lift the toasts a bit
            toastStack.style.bottom = UIState.InventoryOpen ? 60f : 150f;
            TickToasts(dt);
            TickPush(dt);
            TickRibbon(dt);
            TickSummary(dt);
        }
    }
}
