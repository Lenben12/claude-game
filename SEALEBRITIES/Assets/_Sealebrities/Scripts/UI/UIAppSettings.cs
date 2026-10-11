using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Phone settings: the shared settings screen (compact) plus save / main menu (host & offline) or
    /// leave session (co-op client).
    public class UIAppSettings : UIPhoneApp
    {
        UISettingsView view;

        public override string Id => "settings";
        public override string Title => Loc.Tr("Einstellungen", "Settings");
        public override Color Color => UIPalette.Rgb(0x9AA3AE);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Gear;

        public UISettingsView View => view;
        public override bool LiveUpdates => false;

        public override void OnClose() { view?.CancelCapture(); }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            if (view == null) view = new UISettingsView(true);
            view.Root.RemoveFromHierarchy();
            body.Add(view.Root);
            view.Refresh();

            body.Add(PhoneKit.Section(Loc.Tr("Spielstand", "Save game"), UIGlyphs.House));
            if (!Net.IsClient)
            {
                var save = PhoneKit.IconButton(UIGlyphs.Check, Loc.Tr("Speichern", "Save"), () =>
                {
                    if (GameManager.I == null) return;
                    GameManager.I.Save();
                    GameEvents.RaiseToast(Loc.Tr("Gespeichert.", "Saved."));
                }, UIBtnKind.Secondary, PhoneKit.Body);
                save.style.marginBottom = 10f;
                body.Add(save);
                var menu = PhoneKit.IconButton(UIGlyphs.House, Loc.Tr("Speichern & Hauptmenü", "Save & main menu"), () =>
                {
                    Fader.FadeThrough(() =>
                    {
                        Phone.Close();
                        if (Net.IsOnline && Net.Leave != null) Net.Leave();
                        if (GameManager.I != null && GameManager.I.InGame) GameManager.I.ReturnToMenu();
                    }, 1.2f);
                }, UIBtnKind.Primary, PhoneKit.Body);
                body.Add(menu);
            }
            else
            {
                var leave = PhoneKit.IconButton(UIGlyphs.Crew, Loc.Tr("Sitzung verlassen", "Leave session"), () =>
                {
                    Fader.FadeThrough(() =>
                    {
                        Phone.Close();
                        if (Net.Leave != null) Net.Leave();
                        if (GameManager.I != null && GameManager.I.InGame) GameManager.I.ReturnToMenu();
                    }, 1.2f);
                }, UIBtnKind.Primary, PhoneKit.Body);
                body.Add(leave);
            }

            var info = UIKit.Text("ClamOS 2.0 · SEALEBRITIES", PhoneKit.Tiny, UIPalette.Silhouette);
            info.style.alignSelf = Align.Center;
            info.style.marginTop = 24f;
            body.Add(info);
        }

        public override void Tick(float dt) { view?.Tick(dt); }
    }
}
