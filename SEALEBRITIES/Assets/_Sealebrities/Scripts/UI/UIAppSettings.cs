using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Phone settings: language, mouse sensitivity, difficulty info, save, save & main menu.
    public class UIAppSettings : UIPhoneApp
    {
        public override string Id => "settings";
        public override string Title => Loc.Tr("Einstellungen", "Settings");
        public override Color Color => UIPalette.Rgb(0x5B6B7F);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Gear;

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;

            body.Add(UIWidgets.SectionTitle(Loc.Tr("Sprache", "Language")));
            body.Add(UIWidgets.LanguageSelector(14f));

            body.Add(UIWidgets.SectionTitle(Loc.Tr("Steuerung", "Controls")));
            body.Add(UIWidgets.SensitivityRow());

            body.Add(UIWidgets.SectionTitle(Loc.Tr("Schwierigkeit", "Difficulty")));
            var d = s.difficulty;
            var card = UIKit.Card(UIPalette.A(UIPalette.Raised, 0.95f), 14f, 14f);
            card.style.borderLeftWidth = 4f;
            card.style.borderLeftColor = UIPalette.DifficultyColor(d);
            card.Add(UIKit.Text(Loc.DifficultyName(d), 17f, UIPalette.DifficultyColor(d), true));
            var desc = UIKit.Para(DifficultySettings.Description(d), 13f, UIPalette.TextDim);
            desc.style.marginTop = 4f;
            card.Add(desc);
            var fixedNote = UIKit.Text(Loc.Tr("Die Schwierigkeit gilt für den ganzen Spielstand.", "The difficulty applies to the whole save."), 11.5f, UIPalette.TextFaint);
            fixedNote.style.marginTop = 6f;
            card.Add(fixedNote);
            body.Add(card);

            body.Add(UIWidgets.SectionTitle(Loc.Tr("Spielstand", "Save game")));
            var save = UIKit.Btn(Loc.Tr("Jetzt speichern", "Save now"), () =>
            {
                if (GameManager.I == null) return;
                GameManager.I.Save();
                GameEvents.RaiseToast(Loc.Tr("Spiel gespeichert.", "Game saved."));
            }, UIBtnKind.Secondary, 15f);
            save.style.marginBottom = 8f;
            body.Add(save);
            var menu = UIKit.Btn(Loc.Tr("Speichern & Hauptmenü", "Save & main menu"), () =>
            {
                Fader.FadeThrough(() =>
                {
                    Phone.Close();
                    if (GameManager.I != null) GameManager.I.ReturnToMenu();
                }, 1.2f);
            }, UIBtnKind.Primary, 15f);
            body.Add(menu);

            var info = UIKit.Text("ClamOS 1.0 · SEALEBRITIES", 11f, UIPalette.TextFaint);
            info.style.alignSelf = Align.Center;
            info.style.marginTop = 24f;
            body.Add(info);
        }
    }
}
