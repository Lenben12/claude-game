using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// KRILL.tv HUD (C.6 / C.7) in the How-to-Fish style – tiny white outlined elements over the world:
    /// * top centre: red „LIVE“ pill + eye + viewer count, below the thin data-plan bar (10 min) and „Kein Signal!“;
    /// * right edge (bottom 55 %): chat bubbles, max 4, 6 s each, newest at the bottom, donations in gold;
    /// * left edge: the dare card (chat user, dare, reward, timer, [T] annehmen, progress, result);
    /// * above the money line: the gold „+x $ live“ ticker;
    /// * centre: the end card („Stream beendet“ … / „STREAM ABGEBROCHEN“ −n Follower).
    /// Built into its own layer under the phone/menus of the UI document. Used while the UI module has no
    /// UIStreamOverlay of its own (KrillTv.BuiltInUI). Allocation free per frame (texts change only with their values).
    public sealed class LiveOverlay
    {
        const float Margin = 32f;
        const int Pool = 6;

        readonly VisualElement layer, top, pillRow, signalRow, chatCol, dareCard, endCard;
        readonly Label pill, viewersLabel, timeLabel, tickerLabel;
        readonly UIBar dataBar;
        readonly UIHudGlyph eyeGlyph;

        // dare card parts
        readonly VisualElement dareRiskBar, dareResultRow, darePromptRow, dareWaitRow;
        readonly Label dareUser, dareText, dareReward, dareTimer, dareRisk, dareResult, dareWait;
        readonly UIGlyph dareResultGlyph;
        readonly UIBar dareBar;
        readonly Label dareKey;

        // end card parts
        readonly Label endTitle, endSub, endTime, endPeak, endMoney, endFollowers, endBest;
        readonly VisualElement endStats;

        struct Bubble
        {
            public VisualElement root, chip;
            public Label user, text, chipLabel;
            public float age;
            public bool active;
            public int order;
        }

        readonly Bubble[] bubbles = new Bubble[Pool];
        int bubbleOrder;

        int shownViewers = -1, shownSeconds = -1, shownDareSecond = -1, dareBuiltSerial = -1, dareBuiltState = -1;
        Language dareLang;
        bool shownNoSignal, visible;
        float pillPop, tickerAge = 99f, tickerSum, endAge = 99f, blink;
        long tickerShown = -1;

        static readonly Color BubbleBg = new Color(1f, 1f, 1f, 0.85f);
        static readonly Color BubbleLine = new Color(UIPalette.Ink.r, UIPalette.Ink.g, UIPalette.Ink.b, 0.75f);
        static readonly Color Gold = UIPalette.Rgb(0xF2C94C);

        public LiveOverlay(VisualElement parent)
        {
            layer = new VisualElement { name = "sb-layer-live", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(layer);
            parent.Add(layer);

            // ---------------- top centre: LIVE pill + viewers + data plan
            top = UIKit.Col();
            UIKit.Abs(top, 0f, 16f, 0f, null);
            top.style.alignItems = Align.Center;
            layer.Add(top);
            pillRow = UIKit.Row();
            top.Add(pillRow);
            pill = UIKit.Pill("● LIVE", UIPalette.Live, Color.white, 20f);
            UIKit.Pad(pill, 3f, 14f, 3f, 14f);
            pill.style.borderBottomWidth = 3f;
            pill.style.borderBottomColor = UIPalette.Darken(UIPalette.Live, 0.25f);
            pillRow.Add(pill);
            eyeGlyph = new UIHudGlyph(UIGlyphs.Eye, Color.white, 26f);
            eyeGlyph.style.marginLeft = 12f;
            eyeGlyph.style.marginRight = 6f;
            pillRow.Add(eyeGlyph);
            viewersLabel = UIKit.HudText("0", 28f);
            pillRow.Add(viewersLabel);

            var barRow = UIKit.Row();
            barRow.style.marginTop = 6f;
            top.Add(barRow);
            dataBar = new UIBar(6f, Color.white, new Color(1f, 1f, 1f, 0.25f));
            dataBar.style.width = 220f;
            barRow.Add(dataBar);
            timeLabel = UIKit.HudText("10:00", 16f, new Color(1f, 1f, 1f, 0.9f));
            timeLabel.style.marginLeft = 8f;
            barRow.Add(timeLabel);

            signalRow = UIKit.Row();
            signalRow.style.marginTop = 4f;
            signalRow.Add(new UIHudGlyph(UIGlyphs.Warning, UIPalette.Danger, 20f));
            var noSig = UIKit.HudText(Loc.Tr("Kein Signal!", "No signal!"), 20f, UIPalette.Danger);
            noSig.style.marginLeft = 6f;
            noSig.name = "nosignal";
            signalRow.Add(noSig);
            UIKit.Show(signalRow, false);
            top.Add(signalRow);

            // ---------------- money ticker (above the money line, bottom-right)
            tickerLabel = UIKit.HudText("", 24f, Gold);
            UIKit.Abs(tickerLabel, null, null, Margin, 128f);
            tickerLabel.style.opacity = 0f;
            layer.Add(tickerLabel);

            // ---------------- chat column (right edge, bottom 55 %)
            chatCol = UIKit.Col();
            UIKit.Abs(chatCol, null, null, Margin, 172f);
            chatCol.style.width = 380f;
            chatCol.style.maxHeight = Length.Percent(55f);
            chatCol.style.justifyContent = Justify.FlexEnd;
            chatCol.style.alignItems = Align.FlexEnd;
            chatCol.style.overflow = Overflow.Hidden;
            chatCol.pickingMode = PickingMode.Ignore;
            layer.Add(chatCol);
            for (int i = 0; i < Pool; i++) bubbles[i] = MakeBubble();

            // ---------------- dare card (left edge)
            dareCard = UIKit.Col();
            UIKit.Abs(dareCard, Margin, null, null, null);
            dareCard.style.top = Length.Percent(36f);
            dareCard.style.width = 360f;
            dareCard.style.backgroundColor = new Color(1f, 1f, 1f, 0.92f);
            UIKit.Radius(dareCard, 20f);
            UIKit.Pad(dareCard, 14f, 18f, 14f, 22f);
            dareCard.style.overflow = Overflow.Hidden;
            UIKit.Lift(dareCard, 1.2f);
            dareRiskBar = UIKit.Box();
            UIKit.Abs(dareRiskBar, 0f, 0f, null, 0f);
            dareRiskBar.style.width = 8f;
            dareCard.Add(dareRiskBar);

            var head = UIKit.Row();
            head.Add(new UIGlyph(UIGlyphs.Bubble, UIPalette.Violet, 20f));
            dareUser = UIKit.Text("", 16f, UIPalette.InkSoft, true);
            dareUser.style.marginLeft = 6f;
            dareUser.style.flexShrink = 1;
            dareUser.style.overflow = Overflow.Hidden;
            dareUser.style.textOverflow = TextOverflow.Ellipsis;
            head.Add(dareUser);
            head.Add(UIKit.Flex());
            dareRisk = UIKit.Pill("", UIPalette.Sun, UIPalette.Ink, 14f);
            head.Add(dareRisk);
            dareCard.Add(head);

            dareText = UIKit.Text("", 24f, UIPalette.Ink, true);
            dareText.style.whiteSpace = WhiteSpace.Normal;
            dareText.style.marginTop = 6f;
            dareCard.Add(dareText);

            var rewardRow = UIKit.Row();
            rewardRow.style.marginTop = 8f;
            rewardRow.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 24f));
            dareReward = UIKit.Text("", 22f, UIPalette.SunInk, true);
            dareReward.style.marginLeft = 6f;
            rewardRow.Add(dareReward);
            rewardRow.Add(UIKit.Flex());
            rewardRow.Add(new UIGlyph(UIGlyphs.Clock, UIPalette.InkSoft, 20f));
            dareTimer = UIKit.Text("", 20f, UIPalette.Ink, true);
            dareTimer.style.marginLeft = 4f;
            rewardRow.Add(dareTimer);
            dareCard.Add(rewardRow);

            dareBar = new UIBar(8f, UIPalette.Violet);
            dareBar.style.marginTop = 10f;
            dareCard.Add(dareBar);

            darePromptRow = UIKit.Row();
            darePromptRow.style.marginTop = 10f;
            dareKey = UIKit.KeyCap(GameInput.BindingLabel(GameAction.StreamDare), 18f);
            darePromptRow.Add(dareKey);
            var acc = UIKit.Text(Loc.Tr("annehmen", "accept"), 20f, UIPalette.Ink, true);
            acc.name = "accept";
            acc.style.marginLeft = 8f;
            darePromptRow.Add(acc);
            dareCard.Add(darePromptRow);

            dareWaitRow = UIKit.Row();
            dareWaitRow.style.marginTop = 10f;
            dareWait = UIKit.Text("", 18f, UIPalette.InkSoft, false);
            dareWait.style.whiteSpace = WhiteSpace.Normal;
            dareWaitRow.Add(dareWait);
            dareCard.Add(dareWaitRow);

            dareResultRow = UIKit.Row();
            dareResultRow.style.marginTop = 10f;
            dareResultGlyph = new UIGlyph(UIGlyphs.Check, UIPalette.LeafInk, 30f);
            dareResultRow.Add(dareResultGlyph);
            dareResult = UIKit.Text("", 22f, UIPalette.LeafInk, true);
            dareResult.style.marginLeft = 8f;
            dareResultRow.Add(dareResult);
            dareCard.Add(dareResultRow);
            UIKit.IgnorePicking(dareCard);
            UIKit.Show(dareCard, false);
            layer.Add(dareCard);

            // ---------------- end card (centre)
            endCard = UIKit.Col();
            UIKit.Abs(endCard, 0f, null, 0f, null);
            endCard.style.top = Length.Percent(20f);
            endCard.style.alignItems = Align.Center;
            layer.Add(endCard);
            var card = UIKit.Card(new Color(UIPalette.Card.r, UIPalette.Card.g, UIPalette.Card.b, 0.96f), 24f, 22f);
            card.style.width = 520f;
            card.style.alignItems = Align.Center;
            UIKit.Lift(card, 1.4f);
            endCard.Add(card);
            var titleRow = UIKit.Row();
            var tile = UIWidgets.IconTile(KrillGlyph.Draw, KrillGlyph.Purple, 46f);
            tile.style.marginRight = 12f;
            titleRow.Add(tile);
            endTitle = UIKit.Text("", 32f, UIPalette.Ink, true);
            titleRow.Add(endTitle);
            card.Add(titleRow);
            endSub = UIKit.Text("", 26f, UIPalette.CoralInk, true);
            endSub.style.marginTop = 6f;
            card.Add(endSub);
            endStats = UIKit.Row();
            endStats.style.marginTop = 14f;
            endStats.style.flexWrap = Wrap.Wrap;
            endStats.style.justifyContent = Justify.Center;
            card.Add(endStats);
            endTime = Stat(endStats, UIGlyphs.Clock, UIPalette.InkSoft);
            endPeak = Stat(endStats, UIGlyphs.Eye, UIPalette.Violet);
            endMoney = Stat(endStats, UIGlyphs.Coin, UIPalette.Sun);
            endFollowers = Stat(endStats, UIGlyphs.Person, UIPalette.Turquoise);
            endBest = UIKit.Text("", 20f, UIPalette.InkSoft, true);
            endBest.style.marginTop = 10f;
            endBest.style.whiteSpace = WhiteSpace.Normal;
            endBest.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(endBest);
            UIKit.IgnorePicking(endCard);
            UIKit.Show(endCard, false);

            UIKit.Show(top, false);
            UIKit.IgnorePicking(layer);   // a HUD never takes clicks (menus and the 2D phone sit above it anyway)
            UIKit.Show(layer, false);
            LiveState.Chat += OnChat;
            KrillTv.Started += OnStarted;
            KrillTv.Ended += OnEnded;
            KrillTv.MoneyTicked += OnMoney;
            GameEvents.LanguageChanged += OnLanguage;
            GameInput.BindingsChanged += OnBindings;
        }

        public void Dispose()
        {
            LiveState.Chat -= OnChat;
            KrillTv.Started -= OnStarted;
            KrillTv.Ended -= OnEnded;
            KrillTv.MoneyTicked -= OnMoney;
            GameEvents.LanguageChanged -= OnLanguage;
            GameInput.BindingsChanged -= OnBindings;
            layer.RemoveFromHierarchy();
        }

        static Label Stat(VisualElement row, UIGlyph.DrawFn icon, Color c)
        {
            var r = UIKit.Row();
            r.style.marginLeft = 10f;
            r.style.marginRight = 10f;
            r.style.marginTop = 4f;
            r.Add(new UIGlyph(icon, c, 26f));
            var l = UIKit.Text("", 24f, UIPalette.Ink, true);
            l.style.marginLeft = 6f;
            r.Add(l);
            row.Add(r);
            return l;
        }

        Bubble MakeBubble()
        {
            var b = new Bubble();
            b.root = UIKit.Col();
            b.root.style.backgroundColor = BubbleBg;
            UIKit.Border(b.root, 2f, BubbleLine);
            UIKit.Radius(b.root, 16f);
            UIKit.Pad(b.root, 7f, 14f, 8f, 14f);
            b.root.style.marginTop = 8f;
            b.root.style.maxWidth = 360f;
            b.root.style.alignSelf = Align.FlexEnd;
            UIKit.Transition(b.root, 0.25f, "translate", "opacity");
            var head = UIKit.Row();
            b.user = UIKit.Text("", 16f, UIPalette.TurquoiseInk, true);
            head.Add(b.user);
            b.chip = UIKit.Row();
            b.chip.style.marginLeft = 8f;
            b.chip.style.backgroundColor = Gold;
            UIKit.Radius(b.chip, 999f);
            UIKit.Pad(b.chip, 1f, 8f, 1f, 6f);
            b.chip.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.SunInk, 16f));
            b.chipLabel = UIKit.Text("", 15f, UIPalette.Ink, true);
            b.chipLabel.style.marginLeft = 3f;
            b.chip.Add(b.chipLabel);
            head.Add(b.chip);
            b.root.Add(head);
            b.text = UIKit.Text("", 18f, UIPalette.Ink, false);
            b.text.style.whiteSpace = WhiteSpace.Normal;
            b.root.Add(b.text);
            UIKit.IgnorePicking(b.root);
            return b;
        }

        static Color MoodColor(ChatMood m)
        {
            switch (m)
            {
                case ChatMood.Hype: return UIPalette.CoralInk;
                case ChatMood.Laugh: return UIPalette.SunInk;
                case ChatMood.Scared: return UIPalette.Violet;
                case ChatMood.Donation: return UIPalette.SunInk;
                case ChatMood.Hater: return UIPalette.Silhouette;
                case ChatMood.System: return UIPalette.Ink;
                default: return UIPalette.TurquoiseInk;
            }
        }

        // ================================================================ events
        void OnChat(ChatMessage m)
        {
            if (string.IsNullOrEmpty(m.text)) return;
            // reuse the oldest bubble when all are busy; at most 4 visible
            int free = -1, oldest = -1, active = 0;
            for (int i = 0; i < Pool; i++)
            {
                if (!bubbles[i].active) { if (free < 0) free = i; continue; }
                active++;
                if (oldest < 0 || bubbles[i].order < bubbles[oldest].order) oldest = i;
            }
            if (active >= StreamRules.ChatVisible && oldest >= 0) Retire(oldest);
            if (free < 0) free = oldest;
            if (free < 0) return;

            ref var b = ref bubbles[free];
            b.active = true;
            b.age = 0f;
            b.order = ++bubbleOrder;
            b.user.text = m.user;
            b.user.style.color = MoodColor(m.mood);
            b.text.text = m.text;
            bool donation = m.donation > 0;
            UIKit.Show(b.chip, donation);
            if (donation) b.chipLabel.text = "+" + Loc.Money(m.donation);
            b.root.style.backgroundColor = donation ? UIPalette.A(UIPalette.Lighten(Gold, 0.55f), 0.95f) : BubbleBg;
            UIKit.BorderColor(b.root, donation ? UIPalette.SunInk : BubbleLine);
            b.root.style.opacity = 0f;
            b.root.style.translate = new Translate(40f, 0f);
            chatCol.Add(b.root);   // newest at the bottom
        }

        void Retire(int i)
        {
            ref var b = ref bubbles[i];
            b.active = false;
            b.root.RemoveFromHierarchy();
        }

        void OnStarted()
        {
            pillPop = 1f;
            endAge = 99f;
            UIKit.Show(endCard, false);
            for (int i = 0; i < Pool; i++) if (bubbles[i].active) Retire(i);
            tickerSum = 0f;
            tickerAge = 99f;
            shownViewers = -1;
            shownSeconds = -1;
        }

        void OnEnded(StreamSummary s)
        {
            if (s == null) return;
            endAge = 0f;
            if (s.fainted)
            {
                endTitle.text = Loc.Tr("STREAM ABGEBROCHEN", "STREAM LOST");
                endTitle.style.color = UIPalette.CoralInk;
                endSub.text = "−" + Loc.Number(System.Math.Abs(s.followersDelta)) + Loc.Tr(" Follower", " followers");
                UIKit.Show(endSub, true);
            }
            else
            {
                endTitle.text = Loc.Tr("Stream beendet", "Stream ended");
                endTitle.style.color = UIPalette.Ink;
                UIKit.Show(endSub, false);
            }
            endTime.text = UIKit.ClockShort(Mathf.RoundToInt(s.seconds));
            endPeak.text = Loc.Number(s.peakViewers);
            endMoney.text = "+" + Loc.Money(s.money);
            endFollowers.text = (s.followersDelta >= 0 ? "+" : "−") + Loc.Number(System.Math.Abs(s.followersDelta));
            endFollowers.style.color = s.followersDelta >= 0 ? UIPalette.LeafInk : UIPalette.CoralInk;
            string best = s.BestLine();
            endBest.text = best;
            UIKit.Show(endBest, !string.IsNullOrEmpty(best));
            endCard.style.opacity = 0f;
            endCard.style.scale = new Scale(new Vector2(0.9f, 0.9f));
        }

        void OnMoney(long v)
        {
            if (tickerAge > 1.6f) tickerSum = 0f;
            tickerSum += v;
            tickerAge = 0f;
        }

        void OnLanguage()
        {
            dareBuiltSerial = -1;
            var noSig = signalRow.Q<Label>("nosignal");
            if (noSig != null) noSig.text = Loc.Tr("Kein Signal!", "No signal!");
            var acc = darePromptRow.Q<Label>("accept");
            if (acc != null) acc.text = Loc.Tr("annehmen", "accept");
            shownSeconds = -1;
        }

        void OnBindings() => dareKey.text = GameInput.BindingLabel(GameAction.StreamDare);

        // ================================================================ per frame
        public void Tick(float dt)
        {
            var gm = GameManager.I;
            bool inGame = gm != null && gm.InGame;
            bool blocked = !inGame || UIState.MainMenuOpen || UIState.LoadingOpen || UIState.PauseOpen || UIState.CharacterOpen;
            bool live = LiveState.IsLive && inGame;
            bool show = !blocked && (live || endAge < 8f);
            if (show != visible)
            {
                visible = show;
                UIKit.Show(layer, show);
            }
            bool sideHidden = UIState.PhoneOpen || UIState.InventoryOpen;
            // the end card waits while the phone / inventory is open (it was drawn on top of the phone screen)
            if (!sideHidden || endAge >= 8f) endAge += dt;
            if (!show) return;
            UIKit.Show(top, live);
            UIKit.Show(chatCol, !sideHidden);
            if (live) TickTop(dt);
            TickBubbles(dt);
            TickTicker(dt);
            TickDare(sideHidden || !live);
            TickEnd();
        }

        void TickTop(float dt)
        {
            int v = LiveState.Viewers;
            if (v != shownViewers)
            {
                shownViewers = v;
                viewersLabel.text = Loc.Number(v);
            }
            float max = Mathf.Max(1f, LiveState.MaxSeconds);
            float left = Mathf.Max(0f, max - LiveState.Seconds);
            dataBar.SetValue(left / max);
            dataBar.SetColor(left < 30f ? UIPalette.Danger : left < 120f ? UIPalette.Warn : Color.white);
            int sec = Mathf.CeilToInt(left);
            if (sec != shownSeconds)
            {
                shownSeconds = sec;
                timeLabel.text = UIKit.ClockShort(sec);
            }
            bool noSignal = LiveState.NoSignal;
            if (noSignal != shownNoSignal)
            {
                shownNoSignal = noSignal;
                UIKit.Show(signalRow, noSignal);
            }
            if (noSignal)
            {
                blink += dt;
                signalRow.style.opacity = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(blink * 5f));
            }
            if (pillPop > 0f)
            {
                pillPop = Mathf.Max(0f, pillPop - dt * 2.2f);
                float sc = 1f + 0.6f * pillPop * pillPop;
                pill.style.scale = new Scale(new Vector2(sc, sc));
            }
        }

        void TickBubbles(float dt)
        {
            for (int i = 0; i < Pool; i++)
            {
                ref var b = ref bubbles[i];
                if (!b.active) continue;
                b.age += dt;
                if (b.age < 0.05f) continue;
                if (b.age < 0.3f)
                {
                    b.root.style.opacity = 1f;
                    b.root.style.translate = new Translate(0f, 0f);
                }
                else if (b.age > StreamRules.ChatSeconds)
                {
                    float f = 1f - (b.age - StreamRules.ChatSeconds) / 0.4f;
                    if (f <= 0f) { Retire(i); continue; }
                    b.root.style.opacity = f;
                }
            }
        }

        void TickTicker(float dt)
        {
            tickerAge += dt;
            if (tickerAge > 2.6f)
            {
                if (tickerShown != -2) { tickerLabel.style.opacity = 0f; tickerShown = -2; }
                return;
            }
            long sum = (long)tickerSum;
            if (sum != tickerShown)
            {
                tickerShown = sum;
                tickerLabel.text = "+" + Loc.Money(sum) + " live";
            }
            float pop = Mathf.Clamp01(1f - tickerAge / 0.25f);
            float sc = 1f + 0.2f * pop;
            tickerLabel.style.scale = new Scale(new Vector2(sc, sc));
            tickerLabel.style.opacity = tickerAge > 2.1f ? Mathf.Clamp01((2.6f - tickerAge) / 0.5f) : 1f;
        }

        void TickDare(bool hidden)
        {
            var d = LiveState.Dare;
            var def = StreamDares.At(KrillTv.DareIndex);
            bool show = !hidden && d != null && def != null;
            UIKit.Show(dareCard, show);
            if (!show) return;

            int state = (int)d.state;
            if (KrillTv.DareSerial != dareBuiltSerial || state != dareBuiltState || dareLang != Loc.Current) BuildDare(d, def);

            int s = Mathf.CeilToInt(Mathf.Max(0f, d.secondsLeft));
            if (s != shownDareSecond)
            {
                shownDareSecond = s;
                dareTimer.text = UIKit.ClockShort(s);
            }
            if (d.state == DareState.Offered)
                dareBar.SetValue(d.secondsLeft / StreamRules.DareAcceptSeconds);
            else if (d.state == DareState.Running)
                dareBar.SetValue(KrillTv.LocalIsDareTarget && KrillTv.DareProgress01 >= 0f ? KrillTv.DareProgress01 : d.secondsLeft / Mathf.Max(1f, def.seconds));
        }

        void BuildDare(DareInfo d, DareDef def)
        {
            dareBuiltSerial = KrillTv.DareSerial;
            dareBuiltState = (int)d.state;
            dareLang = Loc.Current;
            shownDareSecond = -1;
            string user = KrillTv.DareUser >= 0 && KrillTv.DareUser < StreamChat.Users.Length ? StreamChat.Users[KrillTv.DareUser] : "krill";
            dareUser.text = user + Loc.Tr(" · Mutprobe", " · dare");
            dareText.text = StreamDares.Text(def, d.targetPlayerId);
            dareReward.text = "+" + Loc.Money(d.reward);
            Color rc = StreamDares.RiskColor(def.risk);
            dareRiskBar.style.backgroundColor = rc;
            dareRisk.text = StreamDares.RiskName(def.risk);
            dareRisk.style.backgroundColor = rc;
            dareRisk.style.color = UIPalette.OnColor(rc);

            bool offered = d.state == DareState.Offered, running = d.state == DareState.Running;
            bool result = !offered && !running;
            bool mine = d.targetPlayerId == Net.LocalId;
            UIKit.Show(darePromptRow, offered && mine);
            UIKit.Show(dareWaitRow, (offered && !mine) || (running && !mine));
            if (offered && !mine) dareWait.text = TargetName(d.targetPlayerId) + Loc.Tr(" entscheidet …", " is deciding …");
            else if (running && !mine) dareWait.text = TargetName(d.targetPlayerId) + Loc.Tr(" ist dran!", " is on it!");
            UIKit.Show(dareBar, offered || running);
            dareBar.SetColor(offered ? UIPalette.Violet : UIPalette.Leaf);
            dareText.style.opacity = result ? 0.55f : 1f;
            UIKit.Show(dareResultRow, result);
            if (result)
            {
                if (d.state == DareState.Done)
                {
                    dareResultGlyph.SetDraw(UIGlyphs.Check);
                    dareResultGlyph.SetColor(UIPalette.LeafInk);
                    dareResult.text = Loc.Tr("Geschafft! +", "Done! +") + Loc.Money(d.reward);
                    dareResult.style.color = UIPalette.LeafInk;
                }
                else
                {
                    dareResultGlyph.SetDraw(UIGlyphs.Cross);
                    dareResultGlyph.SetColor(UIPalette.CoralInk);
                    dareResult.text = d.state == DareState.Expired ? Loc.Tr("Abgelaufen", "Expired") : Loc.Tr("Nicht geschafft", "Failed");
                    dareResult.style.color = UIPalette.CoralInk;
                }
            }
        }

        static string TargetName(int id)
        {
            var p = Net.Find(id);
            return p != null && !string.IsNullOrEmpty(p.name) ? p.name : Loc.Tr("Taucher", "Diver");
        }

        void TickEnd()
        {
            bool show = endAge < 8f && !UIState.PhoneOpen && !UIState.InventoryOpen;
            UIKit.Show(endCard, show);
            if (!show) return;
            float a = endAge < 0.3f ? endAge / 0.3f : endAge > 7.4f ? Mathf.Clamp01((8f - endAge) / 0.6f) : 1f;
            endCard.style.opacity = a;
            float sc = endAge < 0.3f ? Mathf.Lerp(0.9f, 1f, endAge / 0.3f) : 1f;
            endCard.style.scale = new Scale(new Vector2(sc, sc));
        }
    }

    /// The KRILL.tv app icon: a pink krill on purple (C.1).
    public static class KrillGlyph
    {
        public static readonly Color Purple = UIPalette.Rgb(0x8E5CC2);
        public static readonly Color Pink = UIPalette.Rgb(0xF28CBE);
        static readonly Color PinkDark = UIPalette.Rgb(0xD9608F);

        static Vector2 P(Rect r, float x, float y) => new Vector2(r.x + r.width * x, r.y + r.height * y);

        /// White (tile) colour draws the pink krill; any other colour draws a single-colour silhouette.
        public static void Draw(Painter2D p, Rect r, Color c)
        {
            bool tint = c.r > 0.95f && c.g > 0.95f && c.b > 0.95f;
            Color body = tint ? Pink : c;
            Color dark = tint ? PinkDark : c;
            float s = Mathf.Min(r.width, r.height);
            body.a *= c.a; dark.a *= c.a;

            // curled body: thick round stroke along an arc
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            p.strokeColor = body;
            p.lineWidth = s * 0.2f;
            p.BeginPath();
            p.Arc(P(r, 0.5f, 0.42f), s * 0.25f, Angle.Degrees(200f), Angle.Degrees(20f), ArcDirection.CounterClockwise);
            p.Stroke();
            // segment lines
            p.strokeColor = dark;
            p.lineWidth = s * 0.035f;
            for (int i = 0; i < 4; i++)
            {
                float ang = (60f + i * 30f) * Mathf.Deg2Rad;
                Vector2 c0 = P(r, 0.5f, 0.42f);
                Vector2 a = c0 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * s * 0.17f;
                Vector2 b = c0 + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * s * 0.33f;
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
            }
            // tail fan (left end of the curl)
            p.fillColor = body;
            p.BeginPath();
            p.MoveTo(P(r, 0.24f, 0.36f));
            p.LineTo(P(r, 0.06f, 0.24f));
            p.LineTo(P(r, 0.1f, 0.44f));
            p.LineTo(P(r, 0.04f, 0.56f));
            p.ClosePath();
            p.Fill();
            // head
            p.BeginPath();
            p.Arc(P(r, 0.74f, 0.36f), s * 0.14f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
            // legs
            p.strokeColor = dark;
            p.lineWidth = s * 0.04f;
            for (int i = 0; i < 3; i++)
            {
                Vector2 a = P(r, 0.42f + i * 0.1f, 0.68f);
                Vector2 b = P(r, 0.38f + i * 0.1f, 0.84f);
                p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke();
            }
            // antennae
            p.strokeColor = body;
            p.lineWidth = s * 0.035f;
            p.BeginPath(); p.MoveTo(P(r, 0.82f, 0.28f)); p.QuadraticCurveTo(P(r, 0.95f, 0.1f), P(r, 0.98f, 0.04f)); p.Stroke();
            p.BeginPath(); p.MoveTo(P(r, 0.85f, 0.32f)); p.QuadraticCurveTo(P(r, 1.0f, 0.22f), P(r, 1.02f, 0.18f)); p.Stroke();
            // eye
            p.fillColor = tint ? UIPalette.Ink : UIPalette.A(UIPalette.Ink, 0.7f * c.a);
            p.BeginPath(); p.Arc(P(r, 0.79f, 0.32f), s * 0.05f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
            p.fillColor = new Color(1f, 1f, 1f, c.a);
            p.BeginPath(); p.Arc(P(r, 0.805f, 0.305f), s * 0.018f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
        }
    }
}
