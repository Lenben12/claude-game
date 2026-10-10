using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// In-game HUD: money/followers with +delta popups and milestone bar, day/clock with sun/moon, quest tracker,
    /// dive panel (air ring, depth, safe depth, clip counter) and the interaction prompt. Polls with caching so it
    /// only touches labels when a displayed value actually changes.
    public class UIHud
    {
        class UIHudPopup
        {
            public Label label;
            public float age;
            public bool active;
        }

        readonly VisualElement root;

        // top-left
        readonly Label moneyLabel, followerLabel, goalLabel, bankedLabel;
        readonly UIBar goalBar;
        readonly VisualElement moneyAnchor, followerAnchor, bankedChip;
        readonly List<UIHudPopup> popups = new List<UIHudPopup>();

        // top-right
        readonly Label clockLabel;
        readonly UIGlyph sunGlyph, moonGlyph;

        // quest
        readonly VisualElement questBox;
        readonly Label questCaption, questText;
        string quest = "";
        float questFlash;

        // dive panel
        readonly VisualElement divePanel, airAnchor, clipRow;
        readonly UIRing airRing;
        readonly Label airSeconds, airCaption, depthLabel, depthCaption, maxLabel, tooDeep, clipsLabel, siteLabel;
        float bumpFlash, clipPulse, siteTimer;
        DiveSite lastSite;
        bool lastSiteValid;

        // prompt + center dot
        readonly VisualElement promptBox, centerDot;
        readonly Label promptLabel;

        // caches
        long money = -1, followers = -1;
        int clockKey = -1, airSec = -1, depthM = -1, maxM = -1, diveClips = -1, bankedClips = -1, chipClips = -1;
        bool night, tooDeepShown, diveShown, promptShown, bankedShown, visible, dotShown;
        string promptText;
        float airRatio = -1f, panelFlash = -1f;
        bool airLow, panelBorder;

        public UIHud(VisualElement layer)
        {
            root = new VisualElement { name = "sb-hud" };
            UIKit.FillParent(root);
            layer.Add(root);

            // ---------------- top-left: money / followers / goal
            var tl = UIKit.Card(UIPalette.Panel, 18f, 0f);
            UIKit.Abs(tl, 28f, 24f, null, null);
            UIKit.Pad(tl, 14f, 18f, 14f, 16f);
            tl.style.width = 320f;
            root.Add(tl);

            var mRow = UIKit.Row();
            var coin = UIKit.Box();
            UIKit.Size(coin, 30f, 30f);
            UIKit.Radius(coin, 15f);
            coin.style.backgroundColor = UIPalette.A(UIPalette.Money, 0.18f);
            coin.style.alignItems = Align.Center;
            coin.style.justifyContent = Justify.Center;
            coin.Add(UIKit.Text("$", 17f, UIPalette.Money, true));
            mRow.Add(coin);
            moneyLabel = UIKit.Text("0 $", 26f, UIPalette.Text, true);
            moneyLabel.style.marginLeft = 12f;
            mRow.Add(moneyLabel);
            moneyAnchor = UIKit.Box();
            moneyAnchor.style.flexGrow = 1;
            moneyAnchor.style.height = 30f;
            mRow.Add(moneyAnchor);
            tl.Add(mRow);

            var fRow = UIKit.Row();
            fRow.style.marginTop = 8f;
            var fIcon = UIKit.Box();
            UIKit.Size(fIcon, 30f, 30f);
            UIKit.Radius(fIcon, 15f);
            fIcon.style.backgroundColor = UIPalette.A(UIPalette.Lagoon, 0.18f);
            fIcon.style.alignItems = Align.Center;
            fIcon.style.justifyContent = Justify.Center;
            fIcon.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Lagoon, 18f));
            fRow.Add(fIcon);
            followerLabel = UIKit.Text("0", 26f, UIPalette.Text, true);
            followerLabel.style.marginLeft = 12f;
            fRow.Add(followerLabel);
            followerAnchor = UIKit.Box();
            followerAnchor.style.flexGrow = 1;
            followerAnchor.style.height = 30f;
            fRow.Add(followerAnchor);
            tl.Add(fRow);

            goalLabel = UIKit.Caption("", UIPalette.TextFaint, 11f);
            goalLabel.style.marginTop = 12f;
            goalLabel.style.marginBottom = 6f;
            tl.Add(goalLabel);
            goalBar = new UIBar(6f, UIPalette.Lagoon);
            tl.Add(goalBar);

            bankedChip = UIKit.Row();
            bankedChip.style.marginTop = 12f;
            bankedChip.style.backgroundColor = UIPalette.A(UIPalette.Sun, 0.14f);
            UIKit.Radius(bankedChip, 10f);
            UIKit.Pad(bankedChip, 6f, 10f, 6f, 10f);
            var play = new UIGlyph(UIGlyphs.Play, UIPalette.Sun, 18f);
            play.style.marginRight = 8f;
            bankedChip.Add(play);
            bankedLabel = UIKit.Text("", 13f, UIPalette.Sun, true);
            bankedLabel.style.flexShrink = 1;
            bankedLabel.style.whiteSpace = WhiteSpace.Normal;
            bankedChip.Add(bankedLabel);
            bankedChip.style.display = DisplayStyle.None;
            tl.Add(bankedChip);

            // ---------------- top-right: day + clock
            var tr = UIKit.Row();
            UIKit.Abs(tr, null, 24f, 28f, null);
            tr.style.backgroundColor = UIPalette.Panel;
            UIKit.Radius(tr, 18f);
            UIKit.Pad(tr, 12f, 18f, 12f, 14f);
            root.Add(tr);
            var celestial = UIKit.Box();
            UIKit.Size(celestial, 30f, 30f);
            sunGlyph = new UIGlyph(UIGlyphs.Sun, UIPalette.Sun, 30f);
            moonGlyph = new UIGlyph(UIGlyphs.Moon, UIPalette.Rgb(0xCFE0FF), 30f);
            UIKit.Abs(sunGlyph, 0f, 0f, null, null);
            UIKit.Abs(moonGlyph, 0f, 0f, null, null);
            moonGlyph.style.display = DisplayStyle.None;
            celestial.Add(sunGlyph);
            celestial.Add(moonGlyph);
            tr.Add(celestial);
            clockLabel = UIKit.Text("", 22f, UIPalette.Text, true);
            clockLabel.style.marginLeft = 12f;
            tr.Add(clockLabel);

            // ---------------- top-center: quest tracker
            var questWrap = UIKit.Col();
            UIKit.Abs(questWrap, 0f, 24f, 0f, null);
            questWrap.style.alignItems = Align.Center;
            root.Add(questWrap);
            questBox = UIKit.Col();
            questBox.style.backgroundColor = UIPalette.Panel;
            UIKit.Radius(questBox, 14f);
            questBox.style.borderLeftWidth = 4f;
            questBox.style.borderLeftColor = UIPalette.Sun;
            UIKit.Pad(questBox, 10f, 20f, 11f, 16f);
            questBox.style.maxWidth = 620f;
            questBox.style.display = DisplayStyle.None;
            questWrap.Add(questBox);
            questCaption = UIKit.Caption("", UIPalette.Sun, 11f);
            questBox.Add(questCaption);
            questText = UIKit.Text("", 17f, UIPalette.Text, true);
            questText.style.whiteSpace = WhiteSpace.Normal;
            questText.style.marginTop = 3f;
            questBox.Add(questText);

            // ---------------- bottom-left: dive panel
            divePanel = UIKit.Row();
            UIKit.Abs(divePanel, 28f, null, null, 28f);
            divePanel.style.backgroundColor = UIPalette.Panel;
            UIKit.Radius(divePanel, 20f);
            UIKit.Pad(divePanel, 14f, 22f, 14f, 14f);
            UIKit.Border(divePanel, 2f, Color.clear);
            divePanel.style.alignItems = Align.Center;
            divePanel.style.display = DisplayStyle.None;
            root.Add(divePanel);

            airAnchor = UIKit.Box();
            airRing = new UIRing(104f, 9f, UIPalette.Lagoon, new Color(1f, 1f, 1f, 0.1f));
            airAnchor.Add(airRing);
            var airCenter = UIKit.Col();
            airCenter.style.alignItems = Align.Center;
            airSeconds = UIKit.Text("0", 28f, UIPalette.Text, true);
            airCenter.Add(airSeconds);
            airCaption = UIKit.Caption("", UIPalette.TextDim, 10f);
            airCenter.Add(airCaption);
            airRing.Add(airCenter);
            divePanel.Add(airAnchor);

            var info = UIKit.Col();
            info.style.marginLeft = 18f;
            info.style.minWidth = 170f;
            divePanel.Add(info);
            siteLabel = UIKit.Caption("", UIPalette.Lagoon, 11f);
            info.Add(siteLabel);
            var depthRow = UIKit.Row();
            depthRow.style.alignItems = Align.FlexEnd;
            depthLabel = UIKit.Text("0 m", 36f, UIPalette.Text, true);
            depthRow.Add(depthLabel);
            depthCaption = UIKit.Caption("", UIPalette.TextFaint, 11f);
            depthCaption.style.marginLeft = 8f;
            depthCaption.style.marginBottom = 7f;
            depthRow.Add(depthCaption);
            info.Add(depthRow);
            var maxRow = UIKit.Row();
            maxLabel = UIKit.Text("", 15f, UIPalette.TextDim, true);
            maxRow.Add(maxLabel);
            tooDeep = UIKit.Pill("", UIPalette.Live, Color.white, 11f);
            tooDeep.style.marginLeft = 8f;
            tooDeep.style.display = DisplayStyle.None;
            maxRow.Add(tooDeep);
            info.Add(maxRow);
            clipRow = UIKit.Row();
            clipRow.style.marginTop = 8f;
            var cam = new UIGlyph(UIGlyphs.Camera, UIPalette.Coral, 18f);
            cam.style.marginRight = 7f;
            clipRow.Add(cam);
            clipsLabel = UIKit.Text("", 14f, UIPalette.Text);
            clipRow.Add(clipsLabel);
            info.Add(clipRow);

            // ---------------- bottom-center: interaction prompt
            var promptWrap = UIKit.Col();
            UIKit.Abs(promptWrap, 0f, null, 0f, 150f);
            promptWrap.style.alignItems = Align.Center;
            root.Add(promptWrap);
            promptBox = UIKit.Row();
            promptBox.style.backgroundColor = UIPalette.Panel;
            UIKit.Radius(promptBox, 999f);
            UIKit.Pad(promptBox, 10f, 22f, 10f, 22f);
            UIKit.Border(promptBox, 1f, new Color(1f, 1f, 1f, 0.12f));
            promptBox.style.display = DisplayStyle.None;
            promptWrap.Add(promptBox);
            promptLabel = UIKit.Text("", 18f, UIPalette.Text, true);
            promptBox.Add(promptLabel);

            // ---------------- center dot
            centerDot = UIKit.Dot(new Color(1f, 1f, 1f, 0.75f), 5f);
            centerDot.style.position = Position.Absolute;
            centerDot.style.left = Length.Percent(50f);
            centerDot.style.top = Length.Percent(50f);
            centerDot.style.marginLeft = -2.5f;
            centerDot.style.marginTop = -2.5f;
            root.Add(centerDot);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
            RefreshTexts();
        }

        // ------------------------------------------------------------------ events
        public void RefreshTexts()
        {
            questCaption.text = Loc.Tr("Aufgabe", "Objective").ToUpperInvariant();
            airCaption.text = Loc.Tr("LUFT", "AIR");
            depthCaption.text = Loc.Tr("TIEFE", "DEPTH");
            tooDeep.text = Loc.Tr("ZU TIEF!", "TOO DEEP!");
            clockKey = -1; airSec = -1; depthM = -1; maxM = -1; diveClips = -1; bankedClips = -1; chipClips = -1; lastSiteValid = false;
            if (money >= 0) moneyLabel.text = Loc.Money(money);
            if (followers >= 0) { followerLabel.text = Loc.Number(followers); UpdateGoal(); }
        }

        public void ResetForNewSession()
        {
            money = -1; followers = -1;
            for (int i = 0; i < popups.Count; i++) { popups[i].active = false; popups[i].label.style.display = DisplayStyle.None; }
            if (GameManager.I != null)
            {
                OnMoney(GameManager.I.State.money);
                OnFollowers(GameManager.I.State.followers);
            }
            clockKey = -1;
        }

        public void OnMoney(long v)
        {
            if (money >= 0 && v != money && visible && !UIState.MainMenuOpen)
                SpawnPopup(moneyAnchor, UIKit.SignedMoney(v - money), v > money ? UIPalette.Money : UIPalette.Coral);
            money = v;
            moneyLabel.text = Loc.Money(v);
        }

        public void OnFollowers(long v)
        {
            if (followers >= 0 && v != followers && visible && !UIState.MainMenuOpen)
                SpawnPopup(followerAnchor, UIKit.Signed(v - followers), v > followers ? UIPalette.Lagoon : UIPalette.Coral);
            followers = v;
            followerLabel.text = Loc.Number(v);
            UpdateGoal();
        }

        void UpdateGoal()
        {
            var next = Progression.NextMilestone(followers);
            if (next == null)
            {
                goalLabel.text = Loc.Tr("Alle Meilensteine erreicht", "All milestones reached").ToUpperInvariant();
                goalBar.SetValue(1f);
                goalBar.SetColor(UIPalette.Sun);
                return;
            }
            long prev = 0;
            foreach (var m in Progression.Milestones) if (m.followers <= followers && m.followers > prev) prev = m.followers;
            goalLabel.text = (Loc.Tr("Nächstes Ziel · ", "Next goal · ") + next.Title).ToUpperInvariant();
            goalBar.SetValue((followers - prev) / (float)System.Math.Max(1L, next.followers - prev));
            goalBar.SetColor(UIPalette.Lagoon);
        }

        public void SetQuest(string text)
        {
            quest = text ?? "";
            questText.text = quest;
            questFlash = 1f;
            UIKit.Show(questBox, quest.Length > 0);
        }

        public void OnBumped(float airLost)
        {
            bumpFlash = 1f;
            if (diveShown && airLost > 0.05f)
                SpawnPopup(airAnchor, "−" + Mathf.CeilToInt(airLost) + Loc.Tr(" s Luft", " s air"), UIPalette.Coral);
        }

        public void OnClipRecorded() { clipPulse = 1f; }

        // ------------------------------------------------------------------ popups
        void SpawnPopup(VisualElement anchor, string text, Color color)
        {
            UIHudPopup p = null;
            for (int i = 0; i < popups.Count; i++) if (!popups[i].active) { p = popups[i]; break; }
            if (p == null)
            {
                p = new UIHudPopup { label = UIKit.Text("", 18f, Color.white, true) };
                p.label.pickingMode = PickingMode.Ignore;
                p.label.style.position = Position.Absolute;
                UIKit.Shadow(p.label, 4f, 0.7f);
                popups.Add(p);
            }
            p.label.RemoveFromHierarchy();
            p.label.style.left = 10f;
            p.label.style.top = 2f;
            if (anchor == airAnchor) { p.label.style.left = 20f; p.label.style.top = -18f; }
            anchor.Add(p.label);
            p.label.text = text;
            p.label.style.color = color;
            p.label.style.display = DisplayStyle.Flex;
            p.label.style.opacity = 1f;
            p.label.style.translate = new Translate(0f, 0f);
            p.age = 0f;
            p.active = true;
        }

        void TickPopups(float dt)
        {
            for (int i = 0; i < popups.Count; i++)
            {
                var p = popups[i];
                if (!p.active) continue;
                p.age += dt;
                float t = p.age / 1.8f;
                if (t >= 1f)
                {
                    p.active = false;
                    p.label.style.display = DisplayStyle.None;
                    continue;
                }
                float rise = 1f - (1f - t) * (1f - t);
                p.label.style.translate = new Translate(0f, -26f * rise);
                p.label.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(float dt, bool aiming)
        {
            var gm = GameManager.I;
            bool show = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen;
            if (show != visible)
            {
                visible = show;
                UIKit.Show(root, show);
                if (show) { clockKey = -1; airSec = -1; depthM = -1; maxM = -1; diveClips = -1; bankedClips = -1; chipClips = -1; }
            }
            if (!show) return;

            TickPopups(dt);
            TickClock(gm);
            TickQuest(dt);
            TickDive(dt, gm);
            TickPrompt(aiming);
            TickBanked();

            bool dot = !aiming && !UIState.BlocksGameplay;
            if (dot != dotShown) { dotShown = dot; UIKit.Show(centerDot, dot); }
        }

        void TickClock(GameManager gm)
        {
            var s = gm.State;
            int key = s.day * 2000 + Mathf.FloorToInt(Mathf.Repeat(s.timeOfDay, 1f) * 1440f);
            if (key == clockKey) return;
            clockKey = key;
            clockLabel.text = Loc.Tr("Tag ", "Day ") + s.day + " · " + gm.ClockString;
            night = gm.IsNight;
            UIKit.Show(sunGlyph, !night);
            UIKit.Show(moonGlyph, night);
        }

        void TickQuest(float dt)
        {
            if (questFlash <= 0f) return;
            questFlash = Mathf.Max(0f, questFlash - dt / 0.9f);
            float s = 1f + 0.05f * questFlash;
            questBox.style.scale = new Scale(new Vector2(s, s));
            questBox.style.backgroundColor = Color.Lerp(UIPalette.Panel, UIPalette.A(UIPalette.Darken(UIPalette.Sun, 0.55f), 0.92f), questFlash);
        }

        void TickDive(float dt, GameManager gm)
        {
            var player = Refs.Player;
            bool inWater = player != null && player.IsInWater;
            if (inWater != diveShown)
            {
                diveShown = inWater;
                UIKit.Show(divePanel, inWater);
                airSec = -1; depthM = -1; maxM = -1; diveClips = -1; airRatio = -1f; lastSiteValid = false;
            }
            if (!inWater) return;

            float air01 = Mathf.Clamp01(player.Air01);
            bool low = air01 < 0.2f;
            if (Mathf.Abs(air01 - airRatio) > 0.003f || low != airLow)
            {
                airRatio = air01;
                airLow = low;
                airRing.Set(air01, low ? UIPalette.Live : (air01 < 0.4f ? UIPalette.Sun : UIPalette.Lagoon));
            }
            int sec = Mathf.Max(0, Mathf.CeilToInt(player.AirSecondsLeft));
            if (sec != airSec)
            {
                airSec = sec;
                airSeconds.text = sec >= 60 ? UIKit.ClockShort(sec) : sec.ToString();
                airSeconds.style.color = low ? UIPalette.Live : UIPalette.Text;
            }

            // red pulse when low on air, coral flash on predator bump
            float pulse = low ? 0.25f + 0.25f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f)) : 0f;
            if (bumpFlash > 0f) bumpFlash = Mathf.Max(0f, bumpFlash - dt / 0.7f);
            float flash = Mathf.Max(pulse, bumpFlash * 0.6f);
            bool border = low || bumpFlash > 0f;
            if (Mathf.Abs(flash - panelFlash) > 0.005f || border != panelBorder)
            {
                panelFlash = flash;
                panelBorder = border;
                divePanel.style.backgroundColor = flash > 0.001f ? Color.Lerp(UIPalette.Panel, UIPalette.A(UIPalette.Live, 0.85f), flash) : UIPalette.Panel;
                UIKit.BorderColor(divePanel, border ? UIPalette.A(UIPalette.Live, 0.4f + flash) : Color.clear);
            }

            float depth = Mathf.Max(0f, player.Depth);
            int d = Mathf.RoundToInt(depth);
            if (d != depthM) { depthM = d; depthLabel.text = Loc.Meters(d); }
            int max = Mathf.RoundToInt(player.MaxSafeDepth);
            if (max != maxM) { maxM = max; maxLabel.text = "max " + Loc.Meters(max); }
            bool deep = depth > player.MaxSafeDepth;
            if (deep != tooDeepShown)
            {
                tooDeepShown = deep;
                UIKit.Show(tooDeep, deep);
                maxLabel.style.color = deep ? UIPalette.Live : UIPalette.TextDim;
                depthLabel.style.color = deep ? UIPalette.Coral : UIPalette.Text;
            }

            int dc = gm.InGame ? Footage.DiveCount : 0, bc = gm.InGame ? Footage.BankedCount : 0;
            if (dc != diveClips || bc != bankedClips)
            {
                diveClips = dc; bankedClips = bc;
                clipsLabel.text = Loc.Tr($"{dc} ungesichert · {bc} gesichert", $"{dc} unsaved · {bc} saved");
                clipsLabel.style.color = dc > 0 ? UIPalette.Sun : UIPalette.TextDim;
            }
            if (clipPulse > 0f)
            {
                clipPulse = Mathf.Max(0f, clipPulse - dt / 0.5f);
                float s = 1f + 0.18f * clipPulse;
                clipRow.style.scale = new Scale(new Vector2(s, s));
            }

            siteTimer -= dt;
            if (siteTimer <= 0f)
            {
                siteTimer = 0.5f;
                var site = player.Root != null ? WorldInfo.SiteAt(player.Root.position) : null;
                if (!lastSiteValid || site != lastSite)
                {
                    lastSiteValid = true;
                    lastSite = site;
                    siteLabel.text = (site != null ? site.Name : Loc.Tr("Offenes Meer", "Open sea")).ToUpperInvariant();
                }
            }
        }

        void TickPrompt(bool aiming)
        {
            string t = InteractionPrompt.Text;
            bool show = !string.IsNullOrEmpty(t) && !aiming && !UIState.BlocksGameplay;
            if (show != promptShown) { promptShown = show; UIKit.Show(promptBox, show); }
            if (show && !string.Equals(t, promptText))
            {
                promptText = t;
                promptLabel.text = t;
            }
        }

        void TickBanked()
        {
            int bc = Footage.BankedCount;
            bool show = bc > 0 && !diveShown;
            if (show != bankedShown) { bankedShown = show; UIKit.Show(bankedChip, show); }
            if (show && bc != chipClips)
            {
                chipClips = bc;
                bankedLabel.text = Loc.Tr($"{bc} Clips gesichert · im Handy auf FinTube hochladen (Tab)",
                                          $"{bc} clips saved · upload them on FinTube in your phone (Tab)");
            }
        }
    }
}
