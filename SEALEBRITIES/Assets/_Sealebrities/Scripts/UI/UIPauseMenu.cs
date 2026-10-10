using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Esc pause menu (Time.timeScale = 0 while open): resume, settings, save & main menu, quit.
    public class UIPauseMenu
    {
        readonly UIController ui;
        readonly VisualElement root, panel;
        bool settingsPage;
        bool leaving, dirty;
        float anim = 1f;

        public UIPauseMenu(UIController ui, VisualElement layer)
        {
            this.ui = ui;
            root = new VisualElement { name = "sb-pause" };
            UIKit.FillParent(root);
            root.style.backgroundColor = UIPalette.A(UIPalette.Deep, 0.62f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            layer.Add(root);

            panel = UIKit.Card(UIPalette.A(UIPalette.Navy, 0.96f), 24f, 32f);
            panel.style.width = 480f;
            UIKit.Border(panel, 1f, new Color(1f, 1f, 1f, 0.08f));
            root.Add(panel);
        }

        public bool IsOpen => UIState.PauseOpen;

        public void Open()
        {
            if (UIState.PauseOpen) return;
            UIState.PauseOpen = true;
            Time.timeScale = 0f;
            settingsPage = false;
            leaving = false;
            root.style.display = DisplayStyle.Flex;
            anim = 0f;
            Rebuild();
        }

        public void Close()
        {
            if (!UIState.PauseOpen && root.style.display.value == DisplayStyle.None) return;
            UIState.PauseOpen = false;
            Time.timeScale = 1f;
            root.style.display = DisplayStyle.None;
            leaving = false;
        }

        public void HandleEscape()
        {
            if (leaving) return;
            if (settingsPage) { settingsPage = false; Rebuild(); }
            else Close();
        }

        /// Rebuild on the next frame (safe inside click callbacks).
        void Refresh() { dirty = true; }

        public void Tick(float dt)
        {
            if (!UIState.PauseOpen) return;
            if (dirty) Rebuild();
            if (anim >= 1f) return;
            anim = Mathf.Min(1f, anim + dt / 0.22f);
            float e = 1f - (1f - anim) * (1f - anim);
            root.style.opacity = e;
            panel.style.scale = new Scale(new Vector2(0.96f + 0.04f * e, 0.96f + 0.04f * e));
        }

        public void Rebuild()
        {
            dirty = false;
            if (!UIState.PauseOpen) return;
            panel.Clear();
            if (settingsPage) BuildSettings(); else BuildMain();
        }

        void BuildMain()
        {
            panel.Add(UIKit.Caption(Loc.Tr("Pausiert", "Paused"), UIPalette.Lagoon, 13f));
            var title = UIKit.Text("PAUSE", 44f, UIPalette.Text, true);
            title.style.letterSpacing = 4f;
            panel.Add(title);

            var gm = GameManager.I;
            if (gm != null && gm.InGame)
            {
                var s = gm.State;
                var info = UIKit.Text(Loc.Tr("Tag ", "Day ") + s.day + " · " + gm.ClockString + "  ·  " + Loc.DifficultyName(s.difficulty), 15f, UIPalette.TextDim);
                info.style.marginTop = 2f;
                panel.Add(info);
                var stats = UIKit.Row();
                stats.style.marginTop = 16f;
                stats.Add(UIWidgets.StatTile(Loc.Money(s.money), Loc.Tr("Geld", "Money"), UIPalette.Money));
                stats.Add(UIKit.HSpacer(8f));
                stats.Add(UIWidgets.StatTile(Loc.Number(s.followers), Loc.Tr("Follower", "Followers"), UIPalette.Lagoon));
                stats.Add(UIKit.HSpacer(8f));
                stats.Add(UIWidgets.StatTile(Journal.DiscoveredCount + " / " + SpeciesDatabase.All.Count, Loc.Tr("Arten", "Species"), UIPalette.Sun));
                panel.Add(stats);
            }

            panel.Add(UIKit.Spacer(22f));
            AddButton(Loc.Tr("Weiter", "Resume"), UIBtnKind.Primary, Close);
            AddButton(Loc.Tr("Einstellungen", "Settings"), UIBtnKind.Secondary, () => { settingsPage = true; Refresh(); });
            AddButton(Loc.Tr("Speichern & Hauptmenü", "Save & main menu"), UIBtnKind.Secondary, OnSaveAndMenu);
            AddButton(Loc.Tr("Spiel beenden", "Quit game"), UIBtnKind.Danger, OnQuit);
        }

        void BuildSettings()
        {
            panel.Add(UIKit.Caption(Loc.Tr("Pause", "Pause"), UIPalette.Lagoon, 13f));
            var title = UIKit.Text(Loc.Tr("Einstellungen", "Settings"), 34f, UIPalette.Text, true);
            title.style.marginBottom = 20f;
            panel.Add(title);

            panel.Add(UIKit.Text(Loc.Tr("Sprache", "Language"), 16f, UIPalette.TextDim, true));
            var seg = UIWidgets.LanguageSelector();
            seg.style.marginTop = 6f;
            seg.style.marginBottom = 20f;
            panel.Add(seg);

            panel.Add(UIWidgets.SensitivityRow());
            panel.Add(UIKit.Spacer(26f));
            AddButton(Loc.Tr("Zurück", "Back"), UIBtnKind.Secondary, () => { settingsPage = false; Refresh(); });
        }

        void AddButton(string text, UIBtnKind kind, System.Action onClick)
        {
            var b = UIKit.Btn(text, onClick, kind, 18f);
            b.style.marginBottom = 10f;
            UIKit.Pad(b, 13f, 20f, 13f, 20f);
            panel.Add(b);
        }

        void OnSaveAndMenu()
        {
            if (leaving || GameManager.I == null) return;
            leaving = true;
            Fader.FadeThrough(() =>
            {
                Close();
                if (GameManager.I != null) GameManager.I.ReturnToMenu();
            }, 1.2f);
        }

        void OnQuit()
        {
            if (leaving) return;
            if (GameManager.I != null) GameManager.I.Save();
            Application.Quit();
        }
    }
}
