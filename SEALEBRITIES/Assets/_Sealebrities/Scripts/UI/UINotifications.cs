using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Toast stack (right), big center banners (queued, deduplicated) and the morning video summary card.
    public class UINotifications
    {
        class UIToastItem
        {
            public VisualElement el;
            public float age;
        }

        class UIBannerItem
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

        const float ToastLife = 4f;
        const float BannerIn = 0.3f, BannerHold = 3.2f, BannerOut = 0.45f;
        const float SummaryLife = 9f;

        readonly VisualElement root, toastStack, bannerEl, bannerIcon, summaryWrap;
        readonly Label bannerTitle, bannerSub;
        readonly List<UIToastItem> toasts = new List<UIToastItem>();
        readonly Queue<UIBannerItem> banners = new Queue<UIBannerItem>();
        readonly List<UIRecentKey> recent = new List<UIRecentKey>();
        UIBannerItem currentBanner;
        float bannerAge;
        bool toastsShifted;

        readonly Queue<UISummaryItem> summaries = new Queue<UISummaryItem>();
        UISummaryItem pendingSummary;
        UISummaryItem currentSummary;
        VisualElement summaryCard;
        float summaryAge, summaryDelay;

        public UINotifications(VisualElement layer)
        {
            root = new VisualElement { name = "sb-notes" };
            UIKit.FillParent(root);
            layer.Add(root);

            toastStack = UIKit.Col();
            UIKit.Abs(toastStack, null, 140f, 28f, null);
            toastStack.style.width = 360f;
            toastStack.style.alignItems = Align.FlexEnd;
            root.Add(toastStack);

            var bannerWrap = UIKit.Col();
            UIKit.Abs(bannerWrap, 0f, null, 0f, null);
            bannerWrap.style.top = Length.Percent(19f);
            bannerWrap.style.alignItems = Align.Center;
            root.Add(bannerWrap);
            bannerEl = UIKit.Col();
            bannerEl.style.alignItems = Align.Center;
            bannerEl.style.backgroundColor = UIPalette.A(UIPalette.Deep, 0.72f);
            UIKit.Radius(bannerEl, 26f);
            UIKit.Pad(bannerEl, 18f, 40f, 20f, 40f);
            bannerEl.style.maxWidth = 1200f;
            bannerEl.style.borderBottomWidth = 3f;
            bannerEl.style.display = DisplayStyle.None;
            bannerWrap.Add(bannerEl);
            bannerIcon = UIKit.Box();
            UIKit.Size(bannerIcon, 150f, 90f);
            bannerIcon.style.marginBottom = 6f;
            bannerEl.Add(bannerIcon);
            bannerTitle = UIKit.Text("", 40f, UIPalette.Sun, true);
            bannerTitle.style.whiteSpace = WhiteSpace.Normal;
            bannerTitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            UIKit.Shadow(bannerTitle, 10f, 0.7f);
            bannerEl.Add(bannerTitle);
            bannerSub = UIKit.Text("", 20f, UIPalette.Text);
            bannerSub.style.whiteSpace = WhiteSpace.Normal;
            bannerSub.style.unityTextAlign = TextAnchor.MiddleCenter;
            bannerSub.style.marginTop = 6f;
            bannerEl.Add(bannerSub);

            summaryWrap = UIKit.Col();
            UIKit.Abs(summaryWrap, 0f, null, 0f, 210f);
            summaryWrap.style.alignItems = Align.Center;
            root.Add(summaryWrap);

            UIKit.IgnorePicking(root);
        }

        // ------------------------------------------------------------------ toasts
        public void Toast(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var row = UIKit.Row();
            row.pickingMode = PickingMode.Ignore;
            row.style.backgroundColor = UIPalette.Panel;
            UIKit.Radius(row, 12f);
            UIKit.Pad(row, 10f, 16f, 10f, 12f);
            row.style.marginBottom = 8f;
            row.style.maxWidth = 360f;
            var bar = UIKit.Box();
            bar.pickingMode = PickingMode.Ignore;
            bar.style.width = 4f;
            bar.style.alignSelf = Align.Stretch;
            bar.style.backgroundColor = UIPalette.Lagoon;
            UIKit.Radius(bar, 2f);
            bar.style.marginRight = 12f;
            row.Add(bar);
            var l = UIKit.Text(text, 15f, UIPalette.Text);
            l.pickingMode = PickingMode.Ignore;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.flexShrink = 1;
            row.Add(l);
            row.style.opacity = 0f;
            toastStack.Insert(0, row);
            toasts.Add(new UIToastItem { el = row, age = 0f });
            // keep the stack short: age out the oldest
            int alive = 0;
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                alive++;
                if (alive > 5 && toasts[i].age < ToastLife - 0.4f) toasts[i].age = ToastLife - 0.4f;
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
                t.el.style.translate = new Translate(40f * (1f - e), 0f);
            }
        }

        // ------------------------------------------------------------------ banners
        public void Banner(string text, Color color)
        {
            if (string.IsNullOrEmpty(text)) return;
            int nl = text.IndexOf('\n');
            string title = nl >= 0 ? text.Substring(0, nl) : text;
            string sub = nl >= 0 ? text.Substring(nl + 1) : null;
            Enqueue(new UIBannerItem { title = title, sub = sub, color = color, key = text });
        }

        public void SpeciesDiscovered(SpeciesDef s, float cm, SizeTier tier)
        {
            if (s == null) return;
            Enqueue(new UIBannerItem
            {
                title = Loc.Tr("NEU ENTDECKT: ", "NEW DISCOVERY: ") + s.Name + " · " + Loc.Length(cm) + " (" + Loc.TierName(tier) + ")",
                sub = Loc.Tr("Neuer Eintrag im Sealog-Tagebuch (J)", "New entry in the Sealog journal (J)"),
                color = Progression.TierColor(tier),
                icon = UICreatureIcons.Get(s),
                key = "disc_" + s.id,
            });
        }

        public void SizeRecord(SpeciesDef s, float cm, SizeTier tier)
        {
            if (s == null) return;
            Enqueue(new UIBannerItem
            {
                title = Loc.Tr("NEUER REKORD!", "NEW RECORD!"),
                sub = s.Name + " · " + Loc.Length(cm) + " (" + Loc.TierName(tier) + ")  ·  " + Loc.Tr("Größen-Bonus ", "Size bonus ") + UIKit.Mult(Progression.SizeMultiplier(tier)),
                color = Progression.TierColor(tier),
                icon = UICreatureIcons.Get(s),
                key = "rec_" + s.id + "_" + Mathf.RoundToInt(cm),
            });
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

        void Enqueue(UIBannerItem item)
        {
            float now = Time.unscaledTime;
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                if (now - recent[i].time > 8f) { recent.RemoveAt(i); continue; }
                if (recent[i].key == item.key) return;
            }
            recent.Add(new UIRecentKey { key = item.key, time = now });
            if (banners.Count > 6) return;   // never build up an endless backlog
            banners.Enqueue(item);
        }

        void TickBanner(float dt)
        {
            if (currentBanner == null)
            {
                if (banners.Count == 0) return;
                currentBanner = banners.Dequeue();
                bannerAge = 0f;
                bannerTitle.text = currentBanner.title;
                bannerTitle.style.color = currentBanner.color;
                bool hasSub = !string.IsNullOrEmpty(currentBanner.sub);
                bannerSub.text = hasSub ? currentBanner.sub : "";
                UIKit.Show(bannerSub, hasSub);
                UIKit.Image(bannerIcon, currentBanner.icon);
                UIKit.Show(bannerIcon, currentBanner.icon != null);
                bannerEl.style.borderBottomColor = UIPalette.A(currentBanner.color, 0.9f);
                bannerEl.style.display = DisplayStyle.Flex;
            }
            bannerAge += dt;
            float hold = BannerHold + (string.IsNullOrEmpty(currentBanner.sub) ? 0f : 0.8f);
            float total = BannerIn + hold + BannerOut;
            // speed up when a backlog exists
            if (banners.Count > 1) bannerAge += dt * 0.6f;
            if (bannerAge >= total)
            {
                bannerEl.style.display = DisplayStyle.None;
                currentBanner = null;
                return;
            }
            float inT = Mathf.Clamp01(bannerAge / BannerIn);
            float outT = Mathf.Clamp01((total - bannerAge) / BannerOut);
            float e = 1f - (1f - inT) * (1f - inT) * (1f - inT);
            float s = Mathf.Lerp(0.82f, 1f, e) * Mathf.Lerp(1.04f, 1f, outT);
            bannerEl.style.opacity = Mathf.Min(inT, outT);
            bannerEl.style.scale = new Scale(new Vector2(s, s));
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
            var card = UIKit.Card(UIPalette.A(UIPalette.Navy, 0.94f), 22f, 22f);
            card.style.width = 600f;
            card.style.borderTopWidth = 4f;
            card.style.borderTopColor = UIPalette.Live;
            card.style.opacity = 0f;

            var head = UIKit.Row();
            var logo = UIKit.Box();
            UIKit.Size(logo, 38f, 38f);
            UIKit.Radius(logo, 10f);
            logo.style.backgroundColor = UIPalette.Live;
            logo.style.alignItems = Align.Center;
            logo.style.justifyContent = Justify.Center;
            logo.Add(new UIGlyph(UIGlyphs.Play, Color.white, 26f));
            head.Add(logo);
            var htxt = UIKit.Col();
            htxt.style.marginLeft = 12f;
            htxt.Add(UIKit.Caption(Loc.Tr("Guten Morgen · Tag ", "Good morning · Day ") + it.day, UIPalette.Sun, 12f));
            htxt.Add(UIKit.Text(it.videos > 1
                ? Loc.Tr($"{it.videos} Videos sind online!", $"{it.videos} videos are online!")
                : Loc.Tr("Dein Video ist online!", "Your video is online!"), 24f, UIPalette.Text, true));
            head.Add(htxt);
            card.Add(head);

            if (!string.IsNullOrEmpty(it.title))
            {
                var t = UIKit.Para("„" + it.title + "“", 15f, UIPalette.TextDim);
                t.style.marginTop = 10f;
                card.Add(t);
            }

            var stats = UIKit.Row();
            stats.style.marginTop = 14f;
            stats.style.alignItems = Align.Stretch;
            stats.Add(UIWidgets.StatTile(Loc.Number(it.views), Loc.Tr("Aufrufe", "Views"), UIPalette.Text));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(UIWidgets.StatTile(UIKit.Signed(it.followers), Loc.Tr("neue Follower", "new followers"), UIPalette.Lagoon));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(it.money > 0 || it.monetized
                ? UIWidgets.StatTile(UIKit.SignedMoney(it.money), Loc.Tr("Werbeeinnahmen", "Ad revenue"), UIPalette.Money)
                : UIWidgets.StatTile("—", Loc.Tr("Geld erst ab 100 Followern", "Money from 100 followers"), UIPalette.TextFaint));
            card.Add(stats);
            UIKit.IgnorePicking(card);
            return card;
        }

        public void Clear()
        {
            for (int i = 0; i < toasts.Count; i++) toasts[i].el.RemoveFromHierarchy();
            toasts.Clear();
            banners.Clear();
            currentBanner = null;
            bannerEl.style.display = DisplayStyle.None;
        }

        public void Tick(float dt)
        {
            bool show = !UIState.LoadingOpen;
            UIKit.Show(root, show);
            if (!show) return;
            // keep toasts readable next to the phone instead of hidden behind it
            bool phone = UIState.PhoneOpen;
            if (phone != toastsShifted)
            {
                toastsShifted = phone;
                toastStack.style.right = phone ? Length.Percent(36f) : new Length(28f);
            }
            TickToasts(dt);
            TickBanner(dt);
            TickSummary(dt);
        }
    }
}
