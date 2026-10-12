using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// "Galerie": the local photo gallery (Core Gallery, PNGs in persistentDataPath/gallery). Thumbnail grid (newest
    /// first, loaded a few per frame), full view with species/site/day and delete. Textures are freed when the app closes.
    public class UIAppGallery : UIPhoneApp
    {
        struct Pending
        {
            public PhotoEntry entry;
            public VisualElement target;
        }

        const int PageSize = 24;

        readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        readonly List<Pending> pending = new List<Pending>();
        PhotoEntry detail;
        int shown = PageSize;
        bool confirmDelete;

        public override string Id => "gallery";
        public override string Title => Loc.Tr("Galerie", "Gallery");
        public override Color Color => UIPalette.Rgb(0xB59AD9);
        public override UIGlyph.DrawFn Icon => UIGlyphs.Image;

        public override string HeaderTitle => detail != null ? Loc.Tr("Foto", "Photo") : Title;

        public override void OnOpen()
        {
            detail = null;
            shown = PageSize;
            confirmDelete = false;
        }

        public override void OnClose()
        {
            pending.Clear();
            foreach (var kv in cache) if (kv.Value != null) Object.Destroy(kv.Value);
            cache.Clear();
            detail = null;
        }

        public override bool Back()
        {
            if (detail != null) { detail = null; confirmDelete = false; return true; }
            return false;
        }

        /// A new photo arrived (Gallery.Added).
        public void OnPhotoAdded() { Phone?.MarkDirty(); }

        public override void Build(VisualElement body)
        {
            pending.Clear();
            if (detail != null && !global::Sealebrities.Gallery.Photos.Contains(detail)) detail = null;
            if (detail != null) { BuildDetail(body, detail); return; }

            var photos = global::Sealebrities.Gallery.Photos;
            var head = UIKit.Row();
            head.style.marginBottom = 12f;
            head.Add(PhoneKit.Stat(UIGlyphs.Image, UIPalette.Rgb(0x8E6CD9), photos.Count.ToString()));
            head.Add(UIKit.HSpacer(10f));
            // round 3: the photos live inside the game – „Alle exportieren“ copies them to the PC's Pictures folder
            if (photos.Count > 0)
            {
                var all = PhoneKit.IconButton(UIGlyphs.Upload, Loc.Tr("Alle exportieren", "Export all"), () =>
                {
                    int n = global::Sealebrities.Gallery.ExportAll();
                    ExportToast(n, null);
                }, UIBtnKind.Secondary, PhoneKit.Small);
                head.Add(all);
            }
            body.Add(head);

            if (photos.Count == 0)
            {
                // the combo the tutorial teaches (Aim + Use); the Photo key works on its own as well
                body.Add(PhoneKit.Empty(UIGlyphs.Camera, Loc.Tr("Noch keine Fotos. ", "No photos yet. ") +
                                                         GameInput.BindingLabel(GameAction.Aim) + " + " + GameInput.BindingLabel(GameAction.Use) + " · " +
                                                         Loc.Tr("Foto = Eintrag (oder ", "photo = entry (or ") + GameInput.BindingLabel(GameAction.Photo) + ")"));
                return;
            }

            var grid = UIKit.Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.justifyContent = Justify.SpaceBetween;
            body.Add(grid);
            int count = 0;
            for (int i = photos.Count - 1; i >= 0 && count < shown; i--, count++)
            {
                var p = photos[i];
                if (p == null) continue;
                grid.Add(Thumb(p));
            }
            if (photos.Count > shown)
            {
                var more = PhoneKit.Button(Loc.Tr("Mehr laden", "Load more"), () => { shown += PageSize; Phone.Refresh(true); }, UIBtnKind.Secondary, PhoneKit.Body);
                more.style.marginTop = 8f;
                body.Add(more);
            }
        }

        VisualElement Thumb(PhotoEntry p)
        {
            var tile = UIKit.Col();
            tile.style.width = Length.Percent(48.5f);
            tile.style.marginBottom = 14f;
            var img = UIKit.Box();
            img.style.height = 136f;
            UIKit.Radius(img, 18f);
            img.style.backgroundColor = UIPalette.CardAlt;
            UIKit.Center(img);
            img.Add(new UIGlyph(UIGlyphs.Image, UIPalette.Silhouette, 40f));
            tile.Add(img);
            var sp = SpeciesDatabase.Get(p.speciesId);
            string label = sp != null && Journal.IsDiscovered(sp.id) ? sp.Name : Loc.Tr("Tag ", "Day ") + p.day;
            var l = UIKit.Text(label, PhoneKit.Tiny, UIPalette.InkSoft, true);
            l.style.marginTop = 4f;
            l.style.overflow = Overflow.Hidden;
            l.style.textOverflow = TextOverflow.Ellipsis;
            l.style.whiteSpace = WhiteSpace.NoWrap;
            tile.Add(l);
            if (cache.TryGetValue(p.file ?? "", out var tex) && tex != null) SetImage(img, tex);
            else pending.Add(new Pending { entry = p, target = img });
            UIKit.IgnorePicking(tile);
            img.pickingMode = PickingMode.Position;
            img.RegisterCallback<ClickEvent>(_ => { UIAudio.Click(); detail = p; confirmDelete = false; Phone.Refresh(false); });
            return tile;
        }

        static void SetImage(VisualElement img, Texture2D tex)
        {
            img.Clear();
            UIKit.ImageCover(img, tex);
        }

        Texture2D Get(PhotoEntry p)
        {
            if (p == null || string.IsNullOrEmpty(p.file)) return null;
            if (cache.TryGetValue(p.file, out var t) && t != null) return t;
            t = global::Sealebrities.Gallery.Load(p);
            cache[p.file] = t;
            return t;
        }

        void BuildDetail(VisualElement body, PhotoEntry p)
        {
            var tex = Get(p);
            var img = UIKit.Box();
            img.style.height = 280f;
            UIKit.Radius(img, 22f);
            img.style.backgroundColor = UIPalette.CardAlt;
            if (tex != null) UIKit.ImageCover(img, tex);
            else
            {
                UIKit.Center(img);
                img.Add(new UIGlyph(UIGlyphs.Warning, UIPalette.Silhouette, 60f));
            }
            body.Add(img);

            var info = PhoneKit.Card();
            info.style.marginTop = 14f;
            var sp = SpeciesDatabase.Get(p.speciesId);
            if (sp != null)
            {
                bool disc = Journal.IsDiscovered(sp.id);
                var r = UIKit.Row();
                r.Add(UIWidgets.SpeciesIcon(sp, disc, 110f, 66f));
                var n = UIKit.Text(disc ? sp.Name : "???", PhoneKit.Big, UIPalette.Ink, true);
                n.style.marginLeft = 10f;
                n.style.flexShrink = 1;
                r.Add(n);
                info.Add(r);
            }
            var site = DiveSites.Get(p.siteId);
            var meta = UIKit.Row();
            meta.style.marginTop = 8f;
            meta.Add(new UIGlyph(UIGlyphs.Clock, UIPalette.InkSoft, 26f));
            var t = UIKit.Text(Loc.Tr("Tag ", "Day ") + p.day + " · " + UIKit.TimeOfDay(p.time), PhoneKit.Small, UIPalette.InkSoft, true);
            t.style.marginLeft = 8f;
            meta.Add(t);
            if (site != null)
            {
                var pin = new UIGlyph(UIGlyphs.Pin, site.mapColor, 26f);
                pin.style.marginLeft = 16f;
                meta.Add(pin);
                var sn = UIKit.Text(site.Name, PhoneKit.Small, UIPalette.InkSoft, true);
                sn.style.marginLeft = 6f;
                sn.style.flexShrink = 1;
                meta.Add(sn);
            }
            info.Add(meta);
            body.Add(info);

            if (!confirmDelete)
            {
                // round 3: EXPORT copies the picture to Pictures/SEALEBRITIES (the game keeps its own copy)
                var exp = PhoneKit.IconButton(UIGlyphs.Upload, Loc.Tr("Exportieren", "Export"), () =>
                {
                    string path = global::Sealebrities.Gallery.Export(p);
                    ExportToast(path != null ? 1 : 0, path);
                }, UIBtnKind.Primary, PhoneKit.Body);
                exp.style.marginTop = 6f;
                body.Add(exp);
                var del = PhoneKit.IconButton(UIGlyphs.Trash, Loc.Tr("Löschen", "Delete"), () => { confirmDelete = true; Phone.Refresh(true); }, UIBtnKind.Danger, PhoneKit.Body);
                del.style.marginTop = 6f;
                body.Add(del);
            }
            else
            {
                var row = UIKit.Row();
                row.style.marginTop = 6f;
                var no = PhoneKit.Button(Loc.Tr("Behalten", "Keep"), () => { confirmDelete = false; Phone.Refresh(true); }, UIBtnKind.Secondary, PhoneKit.Body);
                no.style.flexGrow = 1;
                no.style.flexBasis = 0;
                row.Add(no);
                var yes = PhoneKit.IconButton(UIGlyphs.Trash, Loc.Tr("Weg damit", "Delete"), () =>
                {
                    if (p.file != null && cache.TryGetValue(p.file, out var tx))
                    {
                        if (tx != null) Object.Destroy(tx);
                        cache.Remove(p.file);
                    }
                    global::Sealebrities.Gallery.Delete(p);
                    detail = null;
                    confirmDelete = false;
                    Phone.Refresh(false);
                }, UIBtnKind.Primary, PhoneKit.Body);
                yes.style.flexGrow = 1;
                yes.style.flexBasis = 0;
                yes.style.marginLeft = 10f;
                row.Add(yes);
                body.Add(row);
            }
        }

        static void ExportToast(int n, string path)
        {
            if (n <= 0)
            {
                UIAudio.Nope();
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Export fehlgeschlagen", "Export failed"));
                return;
            }
            UIAudio.Pop();
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Exportiert: Bilder/SEALEBRITIES", "Exported: Pictures/SEALEBRITIES"), n > 1 ? n.ToString() : null);
        }

        public override void Tick(float dt)
        {
            // load a few thumbnails per frame to keep the phone responsive
            int budget = 3;
            while (budget-- > 0 && pending.Count > 0)
            {
                var p = pending[0];
                pending.RemoveAt(0);
                if (p.target == null || p.target.panel == null) continue;
                var tex = Get(p.entry);
                if (tex != null) SetImage(p.target, tex);
            }
        }
    }
}
