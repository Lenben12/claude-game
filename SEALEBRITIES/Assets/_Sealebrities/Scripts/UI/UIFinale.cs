using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// End-of-game page shown once when the 1,000,000-follower milestone is reached: run stats plus
    /// "keep playing" / "main menu". Uses UIState.PauseOpen while open; time only stops offline.
    public class UIFinale
    {
        readonly VisualElement root, panel;
        readonly VisualElement[] confetti = new VisualElement[26];
        readonly float[] seed = new float[26];
        bool open, leaving;
        float anim = 1f, time;

        public UIFinale(VisualElement layer)
        {
            root = new VisualElement { name = "sb-finale" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0.08f, 0.06f, 0.16f, 0.55f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            root.style.overflow = Overflow.Hidden;
            layer.Add(root);

            Color[] cols = { UIPalette.Coral, UIPalette.Sun, UIPalette.Turquoise, UIPalette.Leaf, Color.white, UIPalette.Violet };
            for (int i = 0; i < confetti.Length; i++)
            {
                var c = new VisualElement { pickingMode = PickingMode.Ignore };
                c.style.position = Position.Absolute;
                float s = 10f + (i * 7 % 9);
                UIKit.Size(c, s, s * 1.6f);
                UIKit.Radius(c, 3f);
                c.style.backgroundColor = cols[i % cols.Length];
                seed[i] = (i * 0.6180339f) % 1f;
                c.style.left = Length.Percent(seed[i] * 100f);
                root.Add(c);
                confetti[i] = c;
            }

            panel = UIWidgets.ModalCard(660f);
            panel.style.alignItems = Align.Center;
            root.Add(panel);
        }

        public bool IsOpen => open;

        public void Open()
        {
            if (open) return;
            open = true;
            leaving = false;
            UIState.PauseOpen = true;
            if (!Net.IsOnline) Time.timeScale = 0f;
            root.style.display = DisplayStyle.Flex;
            root.style.opacity = 0f;
            anim = 0f;
            Rebuild();
            UIAudio.Ping();
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            leaving = false;
            UIState.PauseOpen = false;
            Time.timeScale = 1f;
            root.style.display = DisplayStyle.None;
            panel.Clear();
        }

        /// Esc on the finale page means "keep playing".
        public void HandleEscape()
        {
            if (!leaving) Close();
        }

        public void Tick(float dt)
        {
            if (!open) return;
            time += dt;
            for (int i = 0; i < confetti.Length; i++)
            {
                float speed = 0.12f + seed[i] * 0.1f;
                float y = Mathf.Repeat(seed[i] * 1.7f + time * speed, 1f);
                confetti[i].style.top = Length.Percent(-6f + y * 112f);
                confetti[i].style.rotate = new Rotate(Angle.Degrees(time * (90f + seed[i] * 200f)));
                confetti[i].style.translate = new Translate(Mathf.Sin(time * 1.3f + i) * 24f, 0f);
            }
            if (anim >= 1f) return;
            anim = Mathf.Min(1f, anim + dt / 0.5f);
            float e = 1f - (1f - anim) * (1f - anim);
            root.style.opacity = e;
            panel.style.scale = new Scale(new Vector2(0.94f + 0.06f * e, 0.94f + 0.06f * e));
        }

        public void Rebuild()
        {
            if (!open) return;
            panel.Clear();
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;

            panel.Add(UIWidgets.IconTile(UIGlyphs.Star, UIPalette.Sun, 110f));
            var title = UIKit.Text(Loc.Tr("1.000.000 Follower!", "1,000,000 Followers!"), 46f, UIPalette.Ink, true);
            title.style.marginTop = 14f;
            panel.Add(title);
            var sub = UIKit.Para(Loc.Tr("Du bist eine echte Sealebrity. Die ganze Welt schaut dir beim Tauchen zu!",
                                        "You are a true Sealebrity. The whole world watches you dive!"), 19f, UIPalette.InkSoft);
            sub.style.marginTop = 6f;
            sub.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(sub);

            var row1 = UIKit.Row();
            row1.style.marginTop = 20f;
            row1.style.alignSelf = Align.Stretch;
            row1.Add(UIWidgets.IconStat(UIGlyphs.Sun, UIPalette.Sun, Loc.Number(s.day), UIPalette.Ink, 24f));
            row1.Add(UIKit.HSpacer(8f));
            row1.Add(UIWidgets.IconStat(UIGlyphs.Play, UIPalette.Danger, Loc.Number(s.videos.Count), UIPalette.Ink, 24f));
            row1.Add(UIKit.HSpacer(8f));
            row1.Add(UIWidgets.IconStat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(s.totalViews), UIPalette.Ink, 24f));
            panel.Add(row1);
            var row2 = UIKit.Row();
            row2.style.marginTop = 8f;
            row2.style.alignSelf = Align.Stretch;
            row2.Add(UIWidgets.IconStat(UIGlyphs.Book, UIPalette.Turquoise, Journal.DiscoveredCount + "/" + SpeciesDatabase.Count, UIPalette.Ink, 24f));
            row2.Add(UIKit.HSpacer(8f));
            row2.Add(UIWidgets.IconStat(UIGlyphs.Coin, UIPalette.Sun, UIKit.Compact(s.money) + " $", UIPalette.Ink, 24f));
            panel.Add(row2);

            // biggest record: highest size tier, then the longest animal
            JournalEntry best = null;
            SpeciesDef bestDef = null;
            for (int i = 0; i < s.journal.Count; i++)
            {
                var e = s.journal[i];
                if (e == null || !e.discovered) continue;
                var def = SpeciesDatabase.Get(e.speciesId);
                if (def == null) continue;
                if (best == null || e.recordTier > best.recordTier || (e.recordTier == best.recordTier && e.recordCm > best.recordCm))
                {
                    best = e;
                    bestDef = def;
                }
            }
            if (best != null)
            {
                var rec = UIKit.Row();
                rec.style.marginTop = 14f;
                rec.style.alignSelf = Align.Stretch;
                rec.style.backgroundColor = Color.white;
                UIKit.Radius(rec, 16f);
                UIKit.Pad(rec, 8f, 14f, 8f, 8f);
                rec.Add(UIWidgets.SpeciesIcon(bestDef, true, 110f, 66f));
                var col = UIKit.Col();
                col.style.marginLeft = 10f;
                col.Add(UIKit.Text(bestDef.Name, 21f, UIPalette.Ink, true));
                var tier = (SizeTier)best.recordTier;
                col.Add(UIKit.Text(Loc.Length(best.recordCm) + " · " + Loc.TierName(tier), 18f, UIWidgets.Ink(Progression.TierColor(tier)), true));
                rec.Add(col);
                panel.Add(rec);
            }

            var buttons = UIKit.Row();
            buttons.style.marginTop = 22f;
            buttons.style.alignSelf = Align.Stretch;
            var keep = UIKit.Btn(Loc.Tr("Weiterspielen", "Keep playing"), HandleEscape, UIBtnKind.Primary, 21f);
            keep.style.flexGrow = 1;
            keep.style.flexBasis = 0;
            buttons.Add(keep);
            var menu = UIKit.Btn(Loc.Tr("Hauptmenü", "Main menu"), OnMainMenu, UIBtnKind.Secondary, 21f);
            menu.style.flexGrow = 1;
            menu.style.flexBasis = 0;
            menu.style.marginLeft = 10f;
            buttons.Add(menu);
            panel.Add(buttons);
        }

        void OnMainMenu()
        {
            if (leaving || GameManager.I == null) return;
            leaving = true;
            Fader.FadeThrough(() =>
            {
                Close();
                if (Net.IsOnline && Net.Leave != null)
                {
                    try { Net.Leave(); } catch (System.Exception e) { Debug.LogException(e); }
                }
                if (GameManager.I != null && GameManager.I.InGame) GameManager.I.ReturnToMenu();
            }, 1.2f);
        }
    }
}
