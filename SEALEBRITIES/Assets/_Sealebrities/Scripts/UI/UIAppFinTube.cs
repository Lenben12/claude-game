using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// FinTube Studio (L.4, Q31, Q50): channel header with the rank badge, the video's clips, title style, YOUR OWN TITLE
    /// (text field, max 60, 3 suggestion chips – tap fills the field, Enter uploads), the collage thumbnail with up to 5
    /// animals of the video (tap a tile to swap the animal), estimate, upload, pending uploads and the video list
    /// (collage + title).
    public class UIAppFinTube : UIPhoneApp
    {
        TitleStyle style = TitleStyle.Spicy;   // the balanced middle (the economy simulation assumes it)
        bool justUploaded;
        string titleText = "";
        bool customTitle;
        readonly List<string> suggestions = new List<string>(3);
        readonly string[] thumbIds = new string[UIThumbnailComposer.MaxTiles];
        int thumbCount;
        readonly UIThumbnailComposer.Tile[] tiles = new UIThumbnailComposer.Tile[UIThumbnailComposer.MaxTiles];
        readonly Dictionary<string, Texture2D> photoCache = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, Texture2D> historyCache = new Dictionary<string, Texture2D>();
        List<FootageClip> clips;
        string clipsKey;

        public override string Id => "fintube";
        public override string Title => "FinTube";
        public override Color Color => UIPalette.Danger;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Play;
        /// No rebuild while the title field has focus (the text and the caret stay).
        public override bool LiveUpdates => !UITextInput.Active;

        public override int Badge
        {
            get
            {
                var s = UIKit.State;
                return s != null && s.bankedFootage.Count > 0 ? Mathf.Min(12, CountSpecies(s.bankedFootage)) : 0;
            }
        }

        static int CountSpecies(List<FootageClip> list)
        {
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                bool seen = false;
                for (int j = 0; j < i; j++) if (list[j].speciesId == list[i].speciesId) { seen = true; break; }
                if (!seen) n++;
            }
            return n;
        }

        public override string HeaderTitle => "FinTube Studio";

        public override void OnOpen() { justUploaded = false; }

        public override void OnClose()
        {
            foreach (var kv in photoCache) if (kv.Value != null) Object.Destroy(kv.Value);
            photoCache.Clear();
            foreach (var kv in historyCache) if (kv.Value != null) Object.Destroy(kv.Value);
            historyCache.Clear();
            for (int i = 0; i < tiles.Length; i++) tiles[i] = default;
        }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;

            BuildChannel(body, s);

            int dive = Footage.DiveCount;
            if (dive > 0)
                body.Add(UIWidgets.Notice(Loc.Tr($"{dive} Clips noch im Wasser – erst an Land/aufs Boot!", $"{dive} clips still in the water – get to land/the boat first!"),
                                          UIPalette.Warn, UIGlyphs.Camera, PhoneKit.Small));

            if (justUploaded)
            {
                var ok = PhoneKit.Card();
                ok.style.backgroundColor = UIPalette.A(UIPalette.Leaf, 0.3f);
                var r = UIKit.Row();
                var c = UIKit.Box();
                UIKit.Size(c, 56f, 56f);
                UIKit.Radius(c, 28f);
                c.style.backgroundColor = UIPalette.LeafInk;
                UIKit.Center(c);
                c.Add(new UIGlyph(UIGlyphs.Check, Color.white, 36f));
                r.Add(c);
                var t = UIKit.Col();
                t.style.marginLeft = 14f;
                t.style.flexShrink = 1;
                t.Add(UIKit.Text(Loc.Tr("Hochgeladen!", "Uploaded!"), PhoneKit.Big, UIPalette.LeafInk, true));
                var tr = UIKit.Row();
                tr.Add(new UIGlyph(UIGlyphs.Moon, UIPalette.Ink, 22f));
                tr.Add(UIKit.Text(" " + Loc.Tr("Ergebnisse morgen früh", "Results tomorrow morning"), PhoneKit.Small, UIPalette.Ink, true));
                t.Add(tr);
                r.Add(t);
                ok.Add(r);
                body.Add(ok);
            }

            BuildUploadSection(body, s);
            BuildPending(body, s);
            BuildHistory(body, s);
        }

        // ------------------------------------------------------------------ channel header (Q50 rank)
        void BuildChannel(VisualElement body, GameState s)
        {
            int rank = Progression.RankIndex(s.followers);
            var card = PhoneKit.Card(16f);
            var row = UIKit.Row();
            row.Add(new UIRankBadge(rank, 76f));
            var col = UIKit.Col();
            col.style.marginLeft = 14f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text(Settings.DisplayName, PhoneKit.Big, UIPalette.Ink, true));
            col.Add(UIKit.Text(Progression.RankName(rank), PhoneKit.Small, UIWidgets.Ink(UIGlyphs.RankColor(rank)), true));
            row.Add(col);
            card.Add(row);
            if (rank + 1 < Progression.Ranks.Length)
            {
                long a = Progression.Ranks[rank].followers, b = Progression.Ranks[rank + 1].followers;
                var bar = PhoneKit.Bar((s.followers - a) / (float)System.Math.Max(1L, b - a), UIGlyphs.RankColor(rank + 1), 12f);
                bar.style.marginTop = 12f;
                card.Add(bar);
                var nx = UIKit.Row();
                nx.style.marginTop = 6f;
                nx.Add(new UIRankBadge(rank + 1, 26f));
                nx.Add(UIKit.Text(" " + Progression.RankName(rank + 1) + " · " + UIKit.Compact(b), PhoneKit.Tiny, UIPalette.InkSoft, true));
                card.Add(nx);
            }
            var stats = UIKit.Row();
            stats.style.marginTop = 12f;
            stats.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Compact(s.followers)));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(s.totalViews)));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Play, UIPalette.Danger, s.videos.Count.ToString()));
            card.Add(stats);
            body.Add(card);
        }

        // ------------------------------------------------------------------ upload
        List<FootageClip> CurrentClips(GameState s)
        {
            var today = Progression.TodaysUpload(s);
            if (today != null && today.clips != null && today.clips.Count > 0)
            {
                var merged = new List<FootageClip>(s.bankedFootage);
                merged.AddRange(today.clips);
                return Progression.SelectBestClips(merged);
            }
            return Progression.SelectBestClips(s.bankedFootage);
        }

        void BuildUploadSection(VisualElement body, GameState s)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Bereit zum Hochladen", "Ready to upload"), UIGlyphs.Upload));
            if (s.bankedFootage.Count == 0)
            {
                body.Add(PhoneKit.Empty(UIGlyphs.Camera, Loc.Tr("Keine gesicherten Clips. Tauchen, filmen, heil zurückkommen!", "No saved clips. Dive, film, come back safely!")));
                return;
            }

            var today = Progression.TodaysUpload(s);
            clips = CurrentClips(s);
            // the collage animals default to the best clips; reset when the video's clips change
            string key = ClipsKey(clips);
            if (key != clipsKey)
            {
                clipsKey = key;
                thumbCount = Mathf.Min(UIThumbnailComposer.MaxTiles, clips.Count);
                for (int i = 0; i < thumbCount; i++) thumbIds[i] = clips[i].speciesId;
            }

            // the daily core action first: estimate + a compact upload button right under the header (one tap uploads
            // with the defaults – first suggestion, auto collage); title, style and collage stay open below
            FillTiles(s);
            float q = UIThumbnailComposer.Quality(tiles, thumbCount);
            var est = Progression.Preview(style);
            float thumbF = 1f + Progression.ThumbnailBonus * Mathf.Max(0f, q - (today != null ? today.thumbQuality : 0f));
            var topRow = UIKit.Row();
            topRow.style.alignItems = Align.Center;
            topRow.style.flexWrap = Wrap.Wrap;
            topRow.style.marginBottom = 8f;
            topRow.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, "~" + UIKit.Compact((long)((est.viewsMin + est.viewsMax) / 2 * thumbF))));
            topRow.Add(UIKit.HSpacer(8f));
            topRow.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Signed((long)(est.followers * thumbF)), UIPalette.TurquoiseInk));
            topRow.Add(UIKit.HSpacer(8f));
            topRow.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, UIKit.SignedMoney((long)(est.money * thumbF)), UIPalette.LeafInk));
            topRow.Add(UIKit.Flex());
            var upTop = PhoneKit.IconButton(UIGlyphs.Upload,
                today != null ? Loc.Tr("Dazu", "Add") : Loc.Tr("Hochladen", "Upload"),
                OnUpload, UIBtnKind.Live, PhoneKit.Small);
            topRow.Add(upTop);
            body.Add(topRow);

            // clips: compact chips (icon + value)
            var clipWrap = UIKit.Row();
            clipWrap.style.flexWrap = Wrap.Wrap;
            for (int i = 0; i < clips.Count; i++) clipWrap.Add(ClipChip(clips[i]));
            body.Add(clipWrap);

            // title style
            body.Add(PhoneKit.Section(Loc.Tr("Titel-Stil", "Title style"), UIGlyphs.Star));
            var seg = new UISegmented(new[] { Loc.TitleStyleName(TitleStyle.Honest), Loc.TitleStyleName(TitleStyle.Spicy), Loc.TitleStyleName(TitleStyle.Clickbait) },
                (int)style, i => { style = (TitleStyle)i; if (!customTitle) titleText = ""; Phone.Refresh(true); }, UIPalette.Danger, PhoneKit.Small);
            body.Add(seg);
            var eff = UIKit.Row();
            eff.style.marginTop = 8f;
            eff.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 24f));
            eff.Add(UIKit.Text(" " + UIKit.Mult(Progression.TitleViewMultiplier(style)) + "   ", PhoneKit.Small, UIPalette.Ink, true));
            eff.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 24f));
            eff.Add(UIKit.Text(" " + UIKit.Mult(Progression.TitleConversionMultiplier(style)), PhoneKit.Small, UIPalette.Ink, true));
            body.Add(eff);

            // own title + suggestion chips
            Progression.SuggestTitles(clips, style, suggestions);
            body.Add(PhoneKit.Section(Loc.Tr("Dein Titel", "Your title"), UIGlyphs.Pencil));
            var field = UITextInput.Field(titleText, Progression.MaxTitleLength, PhoneKit.Small, suggestions.Count > 0 ? suggestions[0] : "");
            var count = UIKit.Text((titleText ?? "").Length + " / " + Progression.MaxTitleLength, PhoneKit.Tiny, UIPalette.InkSoft);
            field.RegisterValueChangedCallback(e =>
            {
                titleText = e.newValue ?? "";   // raw while typing (spaces stay); sanitized on upload
                string clean = Progression.SanitizeTitle(titleText);
                customTitle = clean.Length > 0 && !suggestions.Contains(clean);
                count.text = titleText.Length + " / " + Progression.MaxTitleLength;
            });
            UITextInput.Track(field, OnUpload);
            body.Add(field);
            count.style.alignSelf = Align.FlexEnd;
            count.style.marginTop = 2f;
            body.Add(count);
            var chips = UIKit.Col();
            chips.style.marginTop = 4f;
            for (int i = 0; i < suggestions.Count; i++)
            {
                string sug = suggestions[i];
                var chip = UIKit.Row();
                UIKit.Radius(chip, 18f);
                UIKit.Pad(chip, 10f, 14f, 10f, 12f);
                chip.style.marginBottom = 6f;
                bool sel = string.Equals(sug, titleText);
                UIKit.Border(chip, 2f, sel ? UIPalette.Danger : UIPalette.A(UIPalette.Ink, 0.08f));
                chip.Add(new UIGlyph(UIGlyphs.Sparkle, UIPalette.Danger, 22f));
                var l = PhoneKit.T(sug, PhoneKit.Tiny, UIPalette.Ink, true);
                l.style.marginLeft = 8f;
                l.style.flexShrink = 1;
                chip.Add(l);
                UIKit.IgnorePicking(chip);
                chip.pickingMode = PickingMode.Position;
                UIKit.Clickable(chip, () => { titleText = sug; customTitle = false; Phone.Refresh(true); }, sel ? UIPalette.A(UIPalette.Danger, 0.12f) : Color.white, UIPalette.CardAlt);
                chips.Add(chip);
            }
            body.Add(chips);

            // collage thumbnail
            body.Add(PhoneKit.Section(Loc.Tr("Vorschaubild", "Thumbnail"), UIGlyphs.Image));
            var collage = UIThumbnailComposer.Build(tiles, thumbCount, style, 496f, 279f, SwapTile);
            body.Add(collage);
            var qrow = UIKit.Row();
            qrow.style.marginTop = 6f;
            qrow.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 22f));
            qrow.Add(UIKit.Text(" +" + Mathf.RoundToInt(Progression.ThumbnailBonus * q * 100f) + " % " + Loc.Tr("Aufrufe", "views"), PhoneKit.Tiny, UIPalette.LeafInk, true));
            qrow.Add(UIKit.Flex());
            qrow.Add(UIKit.Text(Loc.Tr("Bild antippen = Tier tauschen", "Tap a tile = swap animal"), PhoneKit.Tiny, UIPalette.InkSoft));
            body.Add(qrow);

            // second upload button at the end (after choosing title / collage)
            var up = PhoneKit.IconButton(UIGlyphs.Upload,
                today != null ? Loc.Tr("Zum heutigen Video", "Add to today's video") : Loc.Tr("Hochladen", "Upload"),
                OnUpload, UIBtnKind.Live, PhoneKit.Big);
            up.style.marginTop = 14f;
            body.Add(up);
        }

        static string ClipsKey(List<FootageClip> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new System.Text.StringBuilder(list.Count * 10);
            for (int i = 0; i < list.Count; i++) sb.Append(list[i].speciesId).Append(',');
            return sb.ToString();
        }

        void FillTiles(GameState s)
        {
            for (int i = 0; i < tiles.Length; i++) tiles[i] = default;
            for (int i = 0; i < thumbCount; i++)
            {
                var sp = SpeciesDatabase.Get(thumbIds[i]);
                var pe = UIThumbnailComposer.TodaysPhoto(sp, s.day);
                Texture2D tex = null;
                if (pe != null && !photoCache.TryGetValue(pe.file, out tex))
                {
                    tex = global::Sealebrities.Gallery.Load(pe);
                    photoCache[pe.file] = tex;
                }
                tiles[i] = new UIThumbnailComposer.Tile { species = sp, photo = tex, quality = UIThumbnailComposer.TileQuality(sp, tex != null ? pe : null) };
            }
        }

        /// Tap on a tile: the next animal of the video that is not on the collage yet.
        void SwapTile(int index)
        {
            if (clips == null || clips.Count <= thumbCount || index < 0 || index >= thumbCount) { UIAudio.Nope(); return; }
            int start = 0;
            for (int i = 0; i < clips.Count; i++) if (clips[i].speciesId == thumbIds[index]) { start = i; break; }
            for (int k = 1; k <= clips.Count; k++)
            {
                string id = clips[(start + k) % clips.Count].speciesId;
                bool used = false;
                for (int j = 0; j < thumbCount; j++) if (thumbIds[j] == id) { used = true; break; }
                if (used) continue;
                thumbIds[index] = id;
                Phone.Refresh(true);
                return;
            }
        }

        void OnUpload()
        {
            var gm = GameManager.I;
            if (gm == null || gm.State.bankedFootage.Count == 0) return;
            var s = gm.State;
            if (clips == null || clips.Count == 0) clips = CurrentClips(s);
            if (thumbCount == 0) { thumbCount = Mathf.Min(UIThumbnailComposer.MaxTiles, clips.Count); for (int i = 0; i < thumbCount; i++) thumbIds[i] = clips[i].speciesId; }
            FillTiles(s);
            float q = UIThumbnailComposer.Quality(tiles, thumbCount);
            string path = UIThumbnailComposer.Save(s.day, tiles, thumbCount);
            var ids = new string[thumbCount];
            for (int i = 0; i < thumbCount; i++) ids[i] = thumbIds[i];
            string title = Progression.SanitizeTitle(titleText);
            bool custom = customTitle && title.Length > 0;
            if (title.Length == 0 && suggestions.Count > 0) title = suggestions[0];
            var up = Progression.CreateUpload(style, title, ids, path, custom, q);
            if (up != null)
            {
                justUploaded = true;
                titleText = "";
                customTitle = false;
                clipsKey = null;
                UIAudio.Pop();
            }
            Phone.Refresh(false);
        }

        static VisualElement ClipChip(FootageClip c)
        {
            var sp = c.Species;
            var chip = UIKit.Col();
            chip.style.width = Length.Percent(31.5f);
            chip.style.marginRight = Length.Percent(1.5f);
            chip.style.marginBottom = 8f;
            chip.style.alignItems = Align.Center;
            chip.style.backgroundColor = Color.white;
            UIKit.Radius(chip, 18f);
            UIKit.Pad(chip, 8f, 6f, 8f, 6f);
            if (sp != null) chip.Add(UIWidgets.SpeciesIcon(sp, true, 120f, 64f));
            var row = UIKit.Row();
            row.Add(new UIGlyph(UIGlyphs.Star, UIPalette.Sun, 20f));
            row.Add(UIKit.Text(Mathf.RoundToInt(c.Value).ToString(Loc.Culture), PhoneKit.Tiny, UIPalette.Ink, true));
            if (c.crowd > 0) row.Add(UIKit.Text("  ×" + (c.crowd + 1), PhoneKit.Tiny, UIPalette.TurquoiseInk, true));
            chip.Add(row);
            var tags = UIKit.Row();
            if (c.isNewSpecies) tags.Add(UIKit.Pill(Loc.Tr("NEU", "NEW"), UIPalette.Turquoise, Color.white, 15f));
            if (c.isRecord) tags.Add(UIKit.Pill(Loc.Tr("REKORD", "RECORD"), UIPalette.Sun, UIPalette.Ink, 15f));
            if (c.selfie) tags.Add(UIKit.Pill("SELFIE", UIPalette.Coral, Color.white, 15f));
            if (!string.IsNullOrEmpty(c.eventId)) tags.Add(UIKit.Pill("EVENT", UIPalette.Violet, Color.white, 15f));
            if (tags.childCount > 0) chip.Add(tags);
            return chip;
        }

        static void BuildPending(VisualElement body, GameState s)
        {
            if (s.pendingUploads.Count == 0) return;
            body.Add(PhoneKit.Section(Loc.Tr("In Bearbeitung", "Processing"), UIGlyphs.Clock));
            foreach (var p in s.pendingUploads)
            {
                var row = PhoneKit.Card(14f);
                row.style.backgroundColor = UIPalette.A(UIPalette.Sky, 0.14f);
                row.Add(PhoneKit.T(p.title, PhoneKit.Small, UIPalette.Ink, true));
                var l = UIKit.Row();
                l.style.marginTop = 6f;
                l.Add(new UIGlyph(UIGlyphs.Moon, UIPalette.InkSoft, 22f));
                l.Add(UIKit.Text(" " + p.clips.Count + " Clips", PhoneKit.Tiny, UIPalette.InkSoft, true));
                row.Add(l);
                body.Add(row);
            }
        }

        void BuildHistory(VisualElement body, GameState s)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Deine Videos", "Your videos"), UIGlyphs.Play));
            if (s.videos.Count == 0)
            {
                body.Add(PhoneKit.T(Loc.Tr("Noch keine Videos.", "No videos yet."), PhoneKit.Small, UIPalette.InkSoft));
                return;
            }
            int shown = 0;
            var tmp = new UIThumbnailComposer.Tile[UIThumbnailComposer.MaxTiles];
            for (int i = s.videos.Count - 1; i >= 0 && shown < 20; i--, shown++)
            {
                var v = s.videos[i];
                var row = UIKit.Row();
                row.style.alignItems = Align.FlexStart;
                row.style.paddingTop = 10f; row.style.paddingBottom = 10f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = UIPalette.Line;

                VisualElement thumb;
                Texture2D png = null;
                if (!string.IsNullOrEmpty(v.thumbPath) && !historyCache.TryGetValue(v.thumbPath, out png) && historyCache.Count < 24)
                {
                    png = UIThumbnailComposer.Load(v.thumbPath);
                    historyCache[v.thumbPath] = png;
                }
                if (png != null)
                {
                    thumb = UIKit.Box();
                    UIKit.Size(thumb, 160f, 90f);
                    UIKit.Radius(thumb, 12f);
                    UIKit.ImageCover(thumb, png);
                }
                else
                {
                    int n = 0;
                    if (v.thumbSpecies != null)
                        for (int k = 0; k < v.thumbSpecies.Length && n < tmp.Length; k++)
                        {
                            var sp = SpeciesDatabase.Get(v.thumbSpecies[k]);
                            if (sp != null) tmp[n++] = new UIThumbnailComposer.Tile { species = sp };
                        }
                    if (n == 0)
                    {
                        var top = SpeciesDatabase.Get(v.topSpeciesId);
                        if (top != null) tmp[n++] = new UIThumbnailComposer.Tile { species = top };
                    }
                    thumb = UIThumbnailComposer.Build(tmp, n, (TitleStyle)Mathf.Clamp(v.titleStyle, 0, 2), 160f, 90f, null);
                }
                row.Add(thumb);
                var col = UIKit.Col();
                col.style.marginLeft = 12f;
                col.style.flexShrink = 1;
                col.style.flexGrow = 1;
                col.Add(PhoneKit.T(v.title, PhoneKit.Tiny, UIPalette.Ink, true));
                var line = UIKit.Row();
                line.style.marginTop = 4f;
                line.style.flexWrap = Wrap.Wrap;
                line.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 20f));
                line.Add(UIKit.Text(" " + UIKit.Compact(v.views) + "  ", PhoneKit.Tiny, UIPalette.InkSoft, true));
                line.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 20f));
                line.Add(UIKit.Text(" " + UIKit.Signed(v.followersGained) + "  ", PhoneKit.Tiny, UIPalette.TurquoiseInk, true));
                if (v.money > 0)
                {
                    line.Add(new UIGlyph(UIGlyphs.Coin, UIPalette.Sun, 20f));
                    line.Add(UIKit.Text(" " + UIKit.SignedMoney(v.money), PhoneKit.Tiny, UIPalette.LeafInk, true));
                }
                col.Add(line);
                row.Add(col);
                body.Add(row);
            }
        }
    }
}
