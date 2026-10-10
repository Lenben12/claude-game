using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// End-of-game page shown once when the 1,000,000-follower milestone is reached: run stats plus
    /// "keep playing" / "main menu". Uses UIState.PauseOpen + Time.timeScale = 0 while open (no own UIState flag).
    public class UIFinale
    {
        readonly VisualElement root, panel;
        bool open, leaving;
        float anim = 1f;

        public UIFinale(VisualElement layer)
        {
            root = new VisualElement { name = "sb-finale" };
            UIKit.FillParent(root);
            root.style.backgroundColor = UIPalette.A(UIPalette.Deep, 0.82f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            layer.Add(root);

            panel = UIKit.Card(UIPalette.A(UIPalette.Navy, 0.97f), 24f, 34f);
            panel.style.width = 600f;
            panel.style.maxWidth = Length.Percent(92f);
            UIKit.Border(panel, 2f, UIPalette.A(UIPalette.Sun, 0.55f));
            root.Add(panel);
        }

        public bool IsOpen => open;

        public void Open()
        {
            if (open) return;
            open = true;
            leaving = false;
            UIState.PauseOpen = true;
            Time.timeScale = 0f;
            root.style.display = DisplayStyle.Flex;
            root.style.opacity = 0f;
            anim = 0f;
            Rebuild();
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
            if (!open || anim >= 1f) return;
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

            panel.Add(UIKit.Caption(Loc.Tr("Finale", "Finale"), UIPalette.Sun, 13f));
            var title = UIKit.Text(Loc.Tr("1.000.000 Follower!", "1,000,000 Followers!"), 40f, UIPalette.Sun, true);
            UIKit.Shadow(title);
            panel.Add(title);
            var sub = UIKit.Para(Loc.Tr("Du bist eine echte Sealebrity. Die ganze Welt schaut dir beim Tauchen zu – Glückwunsch!",
                                        "You are a true Sealebrity. The whole world watches you dive – congratulations!"), 16f, UIPalette.Text);
            sub.style.marginTop = 6f;
            panel.Add(sub);

            var row1 = UIKit.Row();
            row1.style.marginTop = 20f;
            row1.Add(UIWidgets.StatTile(Loc.Number(s.day), Loc.Tr("Tage", "Days"), UIPalette.Lagoon));
            row1.Add(UIKit.HSpacer(8f));
            row1.Add(UIWidgets.StatTile(Loc.Number(s.videos.Count), Loc.Tr("Videos", "Videos"), UIPalette.Sky));
            row1.Add(UIKit.HSpacer(8f));
            row1.Add(UIWidgets.StatTile(Loc.Number(s.totalViews), Loc.Tr("Aufrufe gesamt", "Total views"), UIPalette.Violet));
            panel.Add(row1);

            var row2 = UIKit.Row();
            row2.style.marginTop = 8f;
            row2.Add(UIWidgets.StatTile(Journal.DiscoveredCount + " / " + SpeciesDatabase.All.Count, Loc.Tr("Arten im Tagebuch", "Species in the journal"), UIPalette.Sun));
            row2.Add(UIKit.HSpacer(8f));
            row2.Add(UIWidgets.StatTile(Loc.Money(s.money), Loc.Tr("Geld", "Money"), UIPalette.Money));
            panel.Add(row2);

            // biggest record: highest size tier, then the longest animal
            JournalEntry best = null;
            SpeciesDef bestDef = null;
            for (int i = 0; i < s.journal.Count; i++)
            {
                var e = s.journal[i];
                if (!e.discovered) continue;
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
                var tier = (SizeTier)best.recordTier;
                var rec = UIWidgets.InfoLine(Loc.Tr("Größter Rekord", "Biggest record"),
                                             bestDef.Name + " · " + Loc.Length(best.recordCm) + " · " + Loc.TierName(tier),
                                             Progression.TierColor(tier));
                rec.style.marginTop = 12f;
                panel.Add(rec);
            }

            panel.Add(UIKit.Spacer(24f));
            AddButton(Loc.Tr("Weiterspielen", "Keep playing"), UIBtnKind.Primary, HandleEscape);
            AddButton(Loc.Tr("Hauptmenü", "Main menu"), UIBtnKind.Secondary, OnMainMenu);
        }

        void AddButton(string text, UIBtnKind kind, System.Action onClick)
        {
            var b = UIKit.Btn(text, onClick, kind, 18f);
            b.style.marginBottom = 10f;
            UIKit.Pad(b, 13f, 20f, 13f, 20f);
            panel.Add(b);
        }

        void OnMainMenu()
        {
            if (leaving || GameManager.I == null) return;
            leaving = true;
            Fader.FadeThrough(() =>
            {
                Close();
                if (GameManager.I != null && GameManager.I.InGame) GameManager.I.ReturnToMenu();
            }, 1.2f);
        }
    }
}
