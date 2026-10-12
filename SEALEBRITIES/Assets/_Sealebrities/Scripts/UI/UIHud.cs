using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// In-game HUD in the How-to-Fish style (art bible §1.14): no boxes, white Fredoka text/icons with outline + shadow.
    ///   top-left     dive block: air ring with seconds, depth, max depth, site, clip counter (green saved / orange unsaved)
    ///   top-right    sun/moon + "Tag 3 · 14:20"
    ///   bottom-right coin + money, person + followers, +delta popups
    ///   bottom-centre hotbar (left hand | right hand), centre dot crosshair, interaction prompt with key caps at 62 %
    ///   map waypoint marker with distance
    /// Polls with caching so it only touches labels when a displayed value actually changes.
    public class UIHud
    {
        class UIHudPopup
        {
            public Label label;
            public float age;
            public bool active;
        }

        class PromptSegment
        {
            public VisualElement row;
            public Label key, verb;
        }

        const float Margin = 32f;

        readonly VisualElement root;
        public readonly UIHotbar Hotbar;
        public readonly UIFoodPanel Food;
        /// Round 3: hunger / thirst meters (+ heart when hurt).
        public readonly UISurvivalPanel Needs;
        /// Round 3 (GDD §20 Q31): the shared co-op map pins as small markers in the view.
        public readonly UIHudPins Pins;
        float hotbarAlpha = 1f;
        readonly UIRankBadge rankBadge;
        readonly VisualElement camHint;
        readonly Label camAimKey, camAimLabel, camSelfieKey, camSelfieLabel;
        bool camHintShown;
        int camHintVersion = -1;

        // bottom-right
        readonly Label moneyLabel, followerLabel;
        readonly VisualElement moneyAnchor, followerAnchor;
        readonly List<UIHudPopup> popups = new List<UIHudPopup>();

        // top-right
        readonly Label clockLabel;
        readonly UIHudGlyph sunGlyph, moonGlyph, micGlyph;
        bool micShown;

        // top-left dive block
        readonly VisualElement diveBlock, airBox, clipRow, depthRow, siteRow;
        readonly UIRing airRing;
        readonly Label airSeconds, depthLabel, maxLabel, siteLabel, clipsSaved, clipsUnsaved, clipsSep;
        readonly UIHudGlyph tooDeepIcon;
        float bumpFlash, clipPulse, siteTimer, siteFlash;
        DiveSite lastSite;
        bool lastSiteValid;

        // prompt + centre dot + waypoint
        readonly VisualElement promptBox, centerDot, waypoint, hotbarWrap;
        readonly List<PromptSegment> promptSegs = new List<PromptSegment>();
        readonly Label waypointLabel;
        readonly UIHudGlyph waypointIcon;

        // caches
        long money = -1, followers = -1;
        int clockKey = -1, airSec = -1, depthM = -1, maxM = -1, diveClips = -1, bankedClips = -1, wpDist = -1;
        bool night, tooDeepShown, diveShown, promptShown, visible, dotShown, wpShown, hotbarShown = true, airBlink;
        string promptText;
        float airRatio = -1f;
        int airColorKey = -1;

        public UIHud(VisualElement layer)
        {
            root = new VisualElement { name = "sb-hud" };
            UIKit.FillParent(root);
            layer.Add(root);

            // ---------------- top-left: dive block + food buffs (E)
            var leftCol = UIKit.Col();
            UIKit.Abs(leftCol, Margin, Margin, null, null);
            root.Add(leftCol);
            diveBlock = UIKit.Col();
            leftCol.Add(diveBlock);
            Needs = new UISurvivalPanel();
            leftCol.Add(Needs.Root);
            Food = new UIFoodPanel();
            leftCol.Add(Food.Root);
            var diveTop = UIKit.Row();
            diveTop.style.alignItems = Align.Center;
            diveBlock.Add(diveTop);

            airBox = UIKit.Box();
            UIKit.Size(airBox, 76f, 76f);
            airRing = new UIRing(76f, 7f, UIPalette.AirFull, new Color(1f, 1f, 1f, 0.25f), true);
            airBox.Add(airRing);
            airSeconds = UIKit.HudText("0", 22f);
            airRing.Add(airSeconds);
            diveTop.Add(airBox);

            var info = UIKit.Col();
            info.style.marginLeft = 16f;
            diveTop.Add(info);
            depthRow = UIKit.Row();
            depthRow.style.alignItems = Align.FlexEnd;
            depthLabel = UIKit.HudText("0 m", 44f);
            depthRow.Add(depthLabel);
            tooDeepIcon = new UIHudGlyph(UIGlyphs.Warning, UIPalette.Danger, 26f);
            tooDeepIcon.style.marginLeft = 8f;
            tooDeepIcon.style.marginBottom = 10f;
            tooDeepIcon.style.display = DisplayStyle.None;
            depthRow.Add(tooDeepIcon);
            info.Add(depthRow);
            maxLabel = UIKit.HudText("", 18f, UIPalette.DepthSub);
            maxLabel.style.marginTop = -4f;
            info.Add(maxLabel);
            siteRow = UIKit.Row();
            siteRow.style.marginTop = 4f;
            var pin = new UIHudGlyph(UIGlyphs.Pin, UIPalette.Turquoise, 20f);
            pin.style.marginRight = 5f;
            siteRow.Add(pin);
            siteLabel = UIKit.HudText("", 20f);
            siteRow.Add(siteLabel);
            info.Add(siteRow);

            clipRow = UIKit.Row();
            clipRow.style.marginTop = 10f;
            var cam = new UIHudGlyph(UIGlyphs.Camera, Color.white, 24f);
            cam.style.marginRight = 8f;
            clipRow.Add(cam);
            clipsSaved = UIKit.HudText("0", 22f, UIPalette.Leaf);
            clipRow.Add(clipsSaved);
            clipsSep = UIKit.HudText(" · ", 22f, new Color(1f, 1f, 1f, 0.8f));
            clipRow.Add(clipsSep);
            clipsUnsaved = UIKit.HudText("0", 22f, UIPalette.Warn);
            clipRow.Add(clipsUnsaved);
            diveBlock.Add(clipRow);

            // ---------------- top-right: day + clock
            var tr = UIKit.Row();
            UIKit.Abs(tr, null, Margin, Margin, null);
            root.Add(tr);
            var celestial = UIKit.Box();
            UIKit.Size(celestial, 30f, 30f);
            sunGlyph = new UIHudGlyph(UIGlyphs.Sun, UIPalette.Sun, 30f);
            moonGlyph = new UIHudGlyph(UIGlyphs.Moon, UIPalette.Rgb(0xDCE6FF), 30f);
            UIKit.Abs(sunGlyph, 0f, 0f, null, null);
            UIKit.Abs(moonGlyph, 0f, 0f, null, null);
            moonGlyph.style.display = DisplayStyle.None;
            celestial.Add(sunGlyph);
            celestial.Add(moonGlyph);
            tr.Add(celestial);
            clockLabel = UIKit.HudText("", 26f);
            clockLabel.style.marginLeft = 10f;
            tr.Add(clockLabel);
            // co-op voice: lights up while the own microphone transmits (push-to-talk held / open mic speaking)
            micGlyph = new UIHudGlyph(UIGlyphs.Mic, UIPalette.Leaf, 26f);
            micGlyph.style.marginLeft = 12f;
            micGlyph.style.display = DisplayStyle.None;
            tr.Add(micGlyph);

            // ---------------- bottom-right: money / followers
            var br = UIKit.Col();
            UIKit.Abs(br, null, null, Margin, Margin);
            br.style.alignItems = Align.FlexEnd;
            root.Add(br);
            var mRow = UIKit.Row();
            moneyAnchor = UIKit.Box();
            moneyAnchor.style.position = Position.Absolute;
            moneyAnchor.style.right = 0; moneyAnchor.style.top = -22f;
            mRow.Add(moneyAnchor);
            var coin = new UIHudGlyph(UIGlyphs.Coin, UIPalette.Sun, 30f);
            coin.style.marginRight = 10f;
            mRow.Add(coin);
            moneyLabel = UIKit.HudText("0 $", 34f);
            mRow.Add(moneyLabel);
            br.Add(mRow);
            var fRow = UIKit.Row();
            fRow.style.marginTop = 8f;
            followerAnchor = UIKit.Box();
            followerAnchor.style.position = Position.Absolute;
            followerAnchor.style.right = 0; followerAnchor.style.top = -22f;
            fRow.Add(followerAnchor);
            var person = new UIHudGlyph(UIGlyphs.Person, UIPalette.Turquoise, 30f);
            person.style.marginRight = 10f;
            fRow.Add(person);
            followerLabel = UIKit.HudText("0", 34f);
            fRow.Add(followerLabel);
            // Q50 channel rank badge next to the follower line
            rankBadge = new UIRankBadge(0, 34f);
            rankBadge.style.marginLeft = 10f;
            fRow.Add(rankBadge);
            br.Add(fRow);

            // ---------------- bottom-centre: hotbar
            hotbarWrap = UIKit.Col();
            UIKit.Abs(hotbarWrap, 0f, null, 0f, 28f - 26f);
            hotbarWrap.style.alignItems = Align.Center;
            root.Add(hotbarWrap);
            // camera in hand: the two key hints of the K.2 / K.4 flow („[RMB] Foto fürs Sealog · [H] Selfie“)
            camHint = UIKit.Row();
            camHint.style.marginBottom = 8f;
            camAimKey = UIKit.HudKeyCap("", 28f);
            camHint.Add(camAimKey);
            var camAimText = UIKit.HudText(" ", 18f);
            camAimText.style.marginLeft = 6f;
            camAimText.style.marginRight = 16f;
            camAimLabel = camAimText;
            camHint.Add(camAimText);
            camSelfieKey = UIKit.HudKeyCap("", 28f);
            camHint.Add(camSelfieKey);
            camSelfieLabel = UIKit.HudText(" ", 18f);
            camSelfieLabel.style.marginLeft = 6f;
            camHint.Add(camSelfieLabel);
            camHint.style.display = DisplayStyle.None;
            hotbarWrap.Add(camHint);
            Hotbar = new UIHotbar();
            hotbarWrap.Add(Hotbar.Root);

            // ---------------- interaction prompt at 62 % screen height
            var promptWrap = UIKit.Col();
            UIKit.Abs(promptWrap, 0f, null, 0f, null);
            promptWrap.style.top = Length.Percent(62f);
            promptWrap.style.alignItems = Align.Center;
            root.Add(promptWrap);
            promptBox = UIKit.Row();
            promptBox.style.flexWrap = Wrap.Wrap;
            promptBox.style.justifyContent = Justify.Center;
            promptBox.style.maxWidth = 1100f;
            promptBox.style.display = DisplayStyle.None;
            promptWrap.Add(promptBox);

            // ---------------- centre dot (5 px white 70 % with 1 px dark ring)
            centerDot = UIKit.Dot(new Color(1f, 1f, 1f, 0.7f), 7f);
            UIKit.Border(centerDot, 1f, UIPalette.A(UIPalette.Outline, 0.6f));
            centerDot.style.position = Position.Absolute;
            centerDot.style.left = Length.Percent(50f);
            centerDot.style.top = Length.Percent(50f);
            centerDot.style.marginLeft = -3.5f;
            centerDot.style.marginTop = -3.5f;
            root.Add(centerDot);

            // ---------------- waypoint marker
            waypoint = UIKit.Col();
            waypoint.style.position = Position.Absolute;
            waypoint.style.alignItems = Align.Center;
            waypoint.style.width = 120f;
            waypoint.style.marginLeft = -60f;
            waypoint.style.marginTop = -20f;
            waypointIcon = new UIHudGlyph(UIGlyphs.Flag, UIPalette.Coral, 30f);
            waypoint.Add(waypointIcon);
            waypointLabel = UIKit.HudText("", 18f);
            waypoint.Add(waypointLabel);
            waypoint.style.display = DisplayStyle.None;
            root.Add(waypoint);
            Pins = new UIHudPins(root);

            // ---------------- F.1 unconscious: red vignette + countdown (a friend can tow you up)
            koOverlay = new VisualElement { name = "sb-ko" };
            UIKit.FillParent(koOverlay);
            koOverlay.style.backgroundImage = new StyleBackground(VignetteTexture());
            koOverlay.style.unityBackgroundImageTintColor = new Color(0.78f, 0.08f, 0.12f, 1f);
            koOverlay.style.alignItems = Align.Center;
            koOverlay.style.display = DisplayStyle.None;
            var koCol = UIKit.Col();
            koCol.style.position = Position.Absolute;
            koCol.style.top = Length.Percent(34f);
            koCol.style.left = 0f;
            koCol.style.right = 0f;
            koCol.style.alignItems = Align.Center;
            koLabel = UIKit.HudText("", 40f);
            koCol.Add(koLabel);
            koHint = UIKit.HudText("", 22f, new Color(1f, 0.86f, 0.86f, 1f));
            koHint.style.marginTop = 6f;
            koCol.Add(koHint);
            koOverlay.Add(koCol);
            root.Insert(0, koOverlay);

            UIKit.IgnorePicking(root);
            root.style.display = DisplayStyle.None;
            UIKit.Show(diveBlock, false);
            RefreshTexts();
        }

        // F.1 unconscious overlay
        readonly VisualElement koOverlay;
        readonly Label koLabel, koHint;
        int koSec = -1;
        bool koShown;
        Language koLang;
        static Texture2D vignette;

        static Texture2D VignetteTexture()
        {
            if (vignette != null) return vignette;
            const int n = 64;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.32f) / 0.68f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 235f));
                }
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "SB_KoVignette", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            vignette.SetPixels32(px);
            vignette.Apply(false, true);
            return vignette;
        }

        /// F.1: while the local diver is unconscious (co-op, Normal/Hard) – red pulsing vignette + seconds left.
        void TickUnconscious()
        {
            var pc = Refs.Player as PlayerController;
            float left = pc != null ? pc.UnconsciousSecondsLeft : 0f;
            bool on = left > 0f && pc.IsUnconscious;
            if (on != koShown)
            {
                koShown = on;
                UIKit.Show(koOverlay, on);
                koSec = -1;
            }
            if (!on) return;
            int sec = Mathf.CeilToInt(left);
            if (sec != koSec || koLang != Loc.Current)
            {
                koSec = sec;
                koLang = Loc.Current;
                koLabel.text = Loc.Tr("Bewusstlos · ", "Passed out · ") + sec + " s";
                koHint.text = Loc.Tr("Ein Freund kann dich hochziehen", "A friend can tow you up");
            }
            // heartbeat pulse
            float beat = Mathf.Pow(Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.6f)), 6f);
            koOverlay.style.opacity = 0.62f + 0.38f * beat;
        }

        // ------------------------------------------------------------------ events
        public void RefreshTexts()
        {
            clockKey = -1; airSec = -1; depthM = -1; maxM = -1; diveClips = -1; bankedClips = -1; lastSiteValid = false; wpDist = -1;
            promptText = null;
            if (money >= 0) moneyLabel.text = Loc.Money(money);
            if (followers >= 0) followerLabel.text = Loc.Number(followers);
            Hotbar.RefreshTexts();
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
            Hotbar.MarkDirty();
        }

        public void OnMoney(long v)
        {
            if (money >= 0 && v != money && visible && !UIState.MainMenuOpen)
                SpawnPopup(moneyAnchor, UIKit.SignedMoney(v - money), v > money ? UIPalette.Leaf : UIPalette.Coral);
            money = v;
            moneyLabel.text = Loc.Money(v);
        }

        public void OnFollowers(long v)
        {
            if (followers >= 0 && v != followers && visible && !UIState.MainMenuOpen)
                SpawnPopup(followerAnchor, UIKit.Signed(v - followers), v > followers ? UIPalette.Turquoise : UIPalette.Coral);
            followers = v;
            followerLabel.text = Loc.Number(v);
            rankBadge.SetRank(Progression.RankIndex(v));
        }

        /// Q50: refresh the rank badge (GameEvents.RankChanged).
        public void OnRankChanged()
        {
            if (followers >= 0) rankBadge.SetRank(Progression.RankIndex(followers));
        }

        /// Kept for older callers: quests now live in the phone app "Aufgaben" (notification on change).
        public void SetQuest(string text) { }

        /// Tests (round 3 UI step): the dive block (air / depth / clips) is on screen; the air ring part of it.
        public bool DiveBlockShown => diveShown;
        public bool AirRingShown => diveShown && UIKit.IsShown(airBox);

        public void OnBumped(float airLost)
        {
            bumpFlash = 1f;
            if (diveShown && airLost > 0.05f)
                SpawnPopup(airBox, "−" + Mathf.CeilToInt(airLost) + " s", UIPalette.Coral);
        }

        public void OnClipRecorded() { clipPulse = 1f; }

        // ------------------------------------------------------------------ popups
        void SpawnPopup(VisualElement anchor, string text, Color color)
        {
            UIHudPopup p = null;
            for (int i = 0; i < popups.Count; i++) if (!popups[i].active) { p = popups[i]; break; }
            if (p == null)
            {
                p = new UIHudPopup { label = UIKit.HudText("", 22f) };
                p.label.style.position = Position.Absolute;
                p.label.style.whiteSpace = WhiteSpace.NoWrap;
                popups.Add(p);
            }
            p.label.RemoveFromHierarchy();
            bool right = anchor != airBox;
            if (right)
            {
                p.label.style.right = 0f;
                p.label.style.left = StyleKeyword.Null;
                p.label.style.top = -8f;
            }
            else
            {
                p.label.style.right = StyleKeyword.Null;
                p.label.style.left = 84f;
                p.label.style.top = 20f;
            }
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
                float t = p.age / 1.5f;
                if (t >= 1f)
                {
                    p.active = false;
                    p.label.style.display = DisplayStyle.None;
                    continue;
                }
                float rise = 1f - (1f - t) * (1f - t);
                p.label.style.translate = new Translate(0f, -18f * rise);
                p.label.style.opacity = t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f;
            }
        }

        // ------------------------------------------------------------------ per frame
        public void Tick(float dt, bool aiming)
        {
            var gm = GameManager.I;
            bool show = gm != null && gm.InGame && !UIState.MainMenuOpen && !UIState.LoadingOpen && !UIState.CharacterOpen;
            if (show != visible)
            {
                visible = show;
                UIKit.Show(root, show);
                if (show) { clockKey = -1; airSec = -1; depthM = -1; maxM = -1; diveClips = -1; bankedClips = -1; Hotbar.MarkDirty(); }
            }
            if (!show) return;

            TickPopups(dt);
            TickUnconscious();
            TickClock(gm);
            bool mic = Net.IsOnline && NetVoice.LocalTransmitting;
            if (mic != micShown) { micShown = mic; UIKit.Show(micGlyph, mic); }
            TickDive(dt, gm);
            TickPrompt(aiming);
            TickWaypoint();
            Pins.Tick(!UIState.BlocksGameplay && !UIState.MapOverlayOpen && !aiming);

            Food.Tick();
            Needs.Tick();

            // Q39: the hotbar is always visible (empty hands, land, water, viewfinder, at the wheel) – hidden only while
            // the phone or a menu is open; idle after 4 s → 60 % alpha, full again on any change
            bool hb = !UIState.InventoryOpen && !UIState.PhoneOpen && !UIState.PauseOpen && !UIState.SelfieOpen && !SelfieState.Active;
            if (hb != hotbarShown) { hotbarShown = hb; UIKit.Show(hotbarWrap, hb); if (hb) Hotbar.MarkDirty(); }
            bool cam = hb && !aiming && Hands.Active(Hand.Right) == HoldItem.Camera;
            if (cam != camHintShown) { camHintShown = cam; UIKit.Show(camHint, cam); }
            if (cam)
            {
                int v = GameInput.BindingsVersion * 31 + (int)Loc.Current;
                if (v != camHintVersion)
                {
                    camHintVersion = v;
                    camAimKey.text = GameInput.BindingLabel(GameAction.Aim);
                    camAimLabel.text = Loc.Tr("Foto fürs Sealog", "Photo for the Sealog");
                    camSelfieKey.text = GameInput.BindingLabel(GameAction.Selfie);
                    camSelfieLabel.text = "Selfie";
                }
            }
            if (hb)
            {
                Hotbar.Tick();
                float target = Hotbar.IdleSeconds > 4f || aiming ? 0.6f : 1f;
                if (Mathf.Abs(target - hotbarAlpha) > 0.001f)
                {
                    hotbarAlpha = Mathf.MoveTowards(hotbarAlpha, target, dt * (target > hotbarAlpha ? 6f : 1.5f));
                    hotbarWrap.style.opacity = hotbarAlpha;
                }
            }

            bool dot = !aiming && !UIState.BlocksGameplay && !UIState.MapOverlayOpen;
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

        void TickDive(float dt, GameManager gm)
        {
            var player = Refs.Player;
            if (player is Object po && po == null) player = null;
            var boat = Refs.Boat;
            if (boat is Object bo && bo == null) boat = null;
            bool inWater = player != null && player.IsInWater;
            bool aboard = boat != null && boat.PlayerAboard;
            int dc = Footage.DiveCount, bc = Footage.BankedCount;
            // round 3 UI step (GDD §20 Q40 „HUD an Land aufgeräumt“): dive info + air only in the water; on the boat just
            // the site name and the clip counter; on land nothing of the dive block (hunger / thirst stay below)
            bool show = inWater || aboard;
            bool divePart = inWater;
            if (show != diveShown)
            {
                diveShown = show;
                UIKit.Show(diveBlock, show);
                airSec = -1; depthM = -1; maxM = -1; diveClips = -1; bankedClips = -1; airRatio = -1f; lastSiteValid = false;
            }
            if (!show) return;
            UIKit.Show(airBox, divePart);
            UIKit.Show(depthRow, inWater);
            UIKit.Show(maxLabel, inWater);
            // Q40: depth lives on the wrist computer – the site name shows for 4 s when a site is entered (always on the boat)
            if (siteFlash > 0f) siteFlash -= dt;
            UIKit.Show(siteRow, aboard || (inWater && siteFlash > 0f));

            if (dc != diveClips || bc != bankedClips)
            {
                diveClips = dc; bankedClips = bc;
                clipsSaved.text = bc.ToString();
                clipsUnsaved.text = dc.ToString();
                UIKit.Show(clipsUnsaved, dc > 0);
                UIKit.Show(clipsSep, dc > 0);
            }
            if (clipPulse > 0f)
            {
                clipPulse = Mathf.Max(0f, clipPulse - dt / 0.5f);
                float sc = 1f + 0.2f * clipPulse;
                clipRow.style.scale = new Scale(new Vector2(sc, sc));
            }
            if (!divePart || player == null) return;

            // air ring: turquoise → sun below 40 % → red below 20 % (blinking 2 Hz)
            float air01 = Mathf.Clamp01(player.Air01);
            int colorKey = air01 < 0.2f ? 2 : air01 < 0.4f ? 1 : 0;
            if (Mathf.Abs(air01 - airRatio) > 0.003f || colorKey != airColorKey)
            {
                airRatio = air01;
                airColorKey = colorKey;
                airRing.Set(air01, colorKey == 2 ? UIPalette.Danger : colorKey == 1 ? UIPalette.Sun : UIPalette.AirFull);
                airSeconds.style.color = colorKey == 2 ? UIPalette.Danger : Color.white;
            }
            bool blinkOff = colorKey == 2 && inWater && Mathf.Repeat(Time.unscaledTime * 2f, 1f) > 0.5f;
            if (bumpFlash > 0f) bumpFlash = Mathf.Max(0f, bumpFlash - dt / 0.6f);
            if (blinkOff != airBlink || bumpFlash > 0f)
            {
                airBlink = blinkOff;
                airRing.style.opacity = blinkOff ? 0.35f : 1f;
                float s = 1f + 0.12f * bumpFlash;
                airBox.style.scale = new Scale(new Vector2(s, s));
            }
            int sec = Mathf.Max(0, Mathf.CeilToInt(player.AirSecondsLeft));
            if (sec != airSec)
            {
                airSec = sec;
                airSeconds.text = sec >= 100 ? UIKit.ClockShort(sec) : sec.ToString();
            }

            if (inWater)
            {
                float depth = Mathf.Max(0f, player.Depth);
                int d = Mathf.RoundToInt(depth);
                if (d != depthM) { depthM = d; depthLabel.text = Loc.Meters(d); }
                int max = Mathf.RoundToInt(player.MaxSafeDepth);
                if (max != maxM) { maxM = max; maxLabel.text = "max " + Loc.Meters(max); }
                bool deep = depth > player.MaxSafeDepth;
                if (deep != tooDeepShown)
                {
                    tooDeepShown = deep;
                    UIKit.Show(tooDeepIcon, deep);
                    maxLabel.style.color = deep ? UIPalette.Danger : UIPalette.DepthSub;
                    depthLabel.style.color = deep ? UIPalette.Coral : Color.white;
                }
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
                    siteLabel.text = site != null ? site.Name : Loc.Tr("Offenes Meer", "Open sea");
                    siteFlash = 4f;
                }
            }
        }

        // ------------------------------------------------------------------ prompt ("E – Schlafen · Q – Anker")
        void TickPrompt(bool aiming)
        {
            string t = InteractionPrompt.Text;
            bool show = !string.IsNullOrEmpty(t) && !aiming && !UIState.BlocksGameplay;
            if (show != promptShown) { promptShown = show; UIKit.Show(promptBox, show); }
            if (show && !string.Equals(t, promptText))
            {
                promptText = t;
                BuildPrompt(t);
            }
        }

        void BuildPrompt(string text)
        {
            int seg = 0, start = 0;
            while (start <= text.Length)
            {
                int sep = text.IndexOf(" · ", start, System.StringComparison.Ordinal);
                int end = sep < 0 ? text.Length : sep;
                string part = text.Substring(start, end - start).Trim();
                if (part.Length > 0) SetSegment(seg++, part);
                if (sep < 0) break;
                start = sep + 3;
            }
            for (int i = 0; i < promptSegs.Count; i++) UIKit.Show(promptSegs[i].row, i < seg);
        }

        void SetSegment(int index, string part)
        {
            while (promptSegs.Count <= index)
            {
                var s = new PromptSegment { row = UIKit.Row() };
                s.row.style.marginLeft = 14f; s.row.style.marginRight = 14f; s.row.style.marginBottom = 6f;
                s.key = UIKit.HudKeyCap("", 32f);
                s.key.style.marginRight = 10f;
                s.row.Add(s.key);
                s.verb = UIKit.HudText("", 22f);
                s.row.Add(s.verb);
                UIKit.IgnorePicking(s.row);
                promptBox.Add(s.row);
                promptSegs.Add(s);
            }
            var seg = promptSegs[index];
            int dash = part.IndexOf(" – ", System.StringComparison.Ordinal);
            if (dash < 0) dash = part.IndexOf(" - ", System.StringComparison.Ordinal);
            if (dash > 0 && dash <= 14)
            {
                seg.key.text = part.Substring(0, dash).Trim();
                seg.verb.text = part.Substring(dash + 3).Trim();
                UIKit.Show(seg.key, true);
            }
            else
            {
                seg.verb.text = part;
                UIKit.Show(seg.key, false);
            }
        }

        // ------------------------------------------------------------------ waypoint
        void TickWaypoint()
        {
            bool active = UIWaypoint.Active && !UIState.BlocksGameplay;
            Camera cam = null;
            Vector3 from = Vector3.zero;
            if (active)
            {
                var p = Refs.Player;
                if (p is Object po && po == null) p = null;
                cam = p != null ? p.View : null;
                if (cam == null) cam = Camera.main;
                if (cam == null || p == null || p.Root == null) active = false;
                else from = p.Root.position;
            }
            if (active != wpShown)
            {
                wpShown = active;
                UIKit.Show(waypoint, active);
                wpDist = -1;
                if (active) waypointIcon.SetColor(UIWaypoint.Color);
            }
            if (!active) return;

            Vector3 target = UIWaypoint.Position;
            target.y = Waves.SeaLevel + 1.5f;
            Vector3 sp = cam.WorldToScreenPoint(target);
            float w = root.layout.width, h = root.layout.height;
            if (w < 2f || h < 2f || Screen.width < 2 || Screen.height < 2) return;
            float x = sp.x / Screen.width * w;
            float y = (1f - sp.y / Screen.height) * h;
            if (sp.z < 0f)
            {
                // behind the camera: mirror and push to the bottom edge
                x = w - x;
                y = h;
            }
            x = Mathf.Clamp(x, 80f, w - 80f);
            y = Mathf.Clamp(y, 90f, h - 210f);
            waypoint.style.left = x;
            waypoint.style.top = y;

            int dist = Mathf.RoundToInt(Vector2.Distance(new Vector2(from.x, from.z), new Vector2(target.x, target.z)));
            int key = dist >= 1000 ? dist / 100 : dist / 5;
            if (key != wpDist)
            {
                wpDist = key;
                waypointLabel.text = UIKit.Distance(dist >= 1000 ? key * 100f : key * 5f);
            }
            if (dist < 25) UIWaypoint.Clear();
        }
    }
}
