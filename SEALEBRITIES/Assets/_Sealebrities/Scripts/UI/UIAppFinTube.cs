using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// FinTube Studio: banked footage (best clip per species), title style, estimate, upload, pending uploads and history.
    public class UIAppFinTube : UIPhoneApp
    {
        TitleStyle style = TitleStyle.Spicy;   // the balanced middle (the economy simulation assumes it)
        bool justUploaded;

        public override string Id => "fintube";
        public override string Title => "FinTube";
        public override Color Color => UIPalette.Danger;
        public override UIGlyph.DrawFn Icon => UIGlyphs.Play;

        public override int Badge
        {
            get
            {
                var s = UIKit.State;
                return s != null && s.bankedFootage.Count > 0 ? Mathf.Min(12, CountSpecies(s.bankedFootage)) : 0;
            }
        }

        static int CountSpecies(List<FootageClip> clips)
        {
            int n = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                bool seen = false;
                for (int j = 0; j < i; j++) if (clips[j].speciesId == clips[i].speciesId) { seen = true; break; }
                if (!seen) n++;
            }
            return n;
        }

        public override string HeaderTitle => "FinTube Studio";

        public override void OnOpen() { justUploaded = false; }

        public override void Build(VisualElement body)
        {
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;

            var stats = UIKit.Row();
            stats.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Compact(s.followers)));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(s.totalViews)));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Play, UIPalette.Danger, s.videos.Count.ToString()));
            body.Add(stats);
            body.Add(UIKit.Spacer(12f));

            if (s.followers < Progression.MonetizationFollowers)
                body.Add(UIWidgets.Notice(Loc.Tr($"Geld ab {Loc.Number(Progression.MonetizationFollowers)} Followern", $"Money from {Loc.Number(Progression.MonetizationFollowers)} followers"),
                                          UIPalette.Sun, UIGlyphs.Coin, PhoneKit.Small));

            int dive = Footage.DiveCount;
            if (dive > 0)
                body.Add(UIWidgets.Notice(dive == 1
                                              ? Loc.Tr("1 Clip noch im Wasser – erst an Land/aufs Boot!", "1 clip still in the water – get to land/the boat first!")
                                              : Loc.Tr($"{dive} Clips noch im Wasser – erst an Land/aufs Boot!", $"{dive} clips still in the water – get to land/the boat first!"),
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
                t.Add(PhoneKit.T(Loc.Tr("Ergebnisse morgen früh – schlaf in der Hütte.", "Results tomorrow morning – sleep in the hut."), PhoneKit.Small, UIPalette.Ink));
                r.Add(t);
                ok.Add(r);
                body.Add(ok);
            }

            BuildUploadSection(body, s);
            BuildPending(body, s);
            BuildHistory(body, s);
        }

        void BuildUploadSection(VisualElement body, GameState s)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Bereit zum Hochladen", "Ready to upload"), UIGlyphs.Upload));
            if (s.bankedFootage.Count == 0)
            {
                body.Add(PhoneKit.Empty(UIGlyphs.Camera, Loc.Tr("Keine gesicherten Clips. Tauchen, filmen, heil zurückkommen!", "No saved clips. Dive, film, come back safely!")));
                return;
            }

            // one video per day: new clips are added to today's video (the preview shows the merged video)
            var today = Progression.TodaysUpload(s);
            List<FootageClip> clips;
            if (today != null && today.clips != null && today.clips.Count > 0)
            {
                var merged = new List<FootageClip>(s.bankedFootage);
                merged.AddRange(today.clips);
                clips = Progression.SelectBestClips(merged);
            }
            else clips = Progression.SelectBestClips(s.bankedFootage);
            for (int i = 0; i < clips.Count; i++) body.Add(ClipRow(clips[i]));

            body.Add(PhoneKit.Section(Loc.Tr("Titel-Stil", "Title style"), UIGlyphs.Star));
            var seg = new UISegmented(new[] { Loc.TitleStyleName(TitleStyle.Honest), Loc.TitleStyleName(TitleStyle.Spicy), Loc.TitleStyleName(TitleStyle.Clickbait) },
                (int)style, i => { style = (TitleStyle)i; Phone.Refresh(true); }, UIPalette.Danger, PhoneKit.Small);
            body.Add(seg);
            var eff = UIKit.Row();
            eff.style.marginTop = 8f;
            eff.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 24f));
            eff.Add(UIKit.Text(" " + UIKit.Mult(Progression.TitleViewMultiplier(style)) + "   ", PhoneKit.Small, UIPalette.Ink, true));
            eff.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 24f));
            eff.Add(UIKit.Text(" " + UIKit.Mult(Progression.TitleConversionMultiplier(style)), PhoneKit.Small, UIPalette.Ink, true));
            body.Add(eff);

            // thumbnail + title preview
            var preview = PhoneKit.Card(14f);
            preview.style.marginTop = 12f;
            var pr = UIKit.Row();
            pr.style.alignItems = Align.FlexStart;
            var thumb = UIKit.Box();
            UIKit.Size(thumb, 176f, 100f);
            UIKit.Radius(thumb, 14f);
            UIKit.Image(thumb, UIKit.VGradient(UIPalette.Rgb(0x2478AA), UIPalette.Rgb(0x5ECAD9)), false);
            UIKit.Center(thumb);
            if (clips.Count > 0 && clips[0].Species != null) thumb.Add(UIWidgets.SpeciesIcon(clips[0].Species, true, 150f, 86f));
            var dur = UIKit.Text(UIKit.Clock(SumSeconds(clips)), PhoneKit.Tiny, Color.white, true);
            dur.style.position = Position.Absolute;
            dur.style.right = 6f; dur.style.bottom = 5f;
            dur.style.backgroundColor = new Color(0f, 0f, 0f, 0.65f);
            UIKit.Pad(dur, 1f, 6f, 1f, 6f);
            UIKit.Radius(dur, 5f);
            thumb.Add(dur);
            pr.Add(thumb);
            var title = PhoneKit.T(Progression.SuggestTitle(clips, style), PhoneKit.Small, UIPalette.Ink, true);
            title.style.marginLeft = 12f;
            title.style.flexShrink = 1;
            pr.Add(title);
            preview.Add(pr);
            body.Add(preview);

            // estimate
            var est = Progression.Preview(style);
            var estRow = UIKit.Row();
            estRow.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, "~" + UIKit.Compact((est.viewsMin + est.viewsMax) / 2)));
            estRow.Add(UIKit.HSpacer(8f));
            estRow.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Signed(est.followers), UIPalette.TurquoiseInk));
            body.Add(estRow);
            if (s.followers >= Progression.MonetizationFollowers)
            {
                var mr = UIKit.Row();
                mr.style.marginTop = 8f;
                mr.Add(PhoneKit.Stat(UIGlyphs.Coin, UIPalette.Sun, UIKit.SignedMoney(est.money), UIPalette.LeafInk));
                body.Add(mr);
            }

            var up = PhoneKit.IconButton(UIGlyphs.Upload,
                today != null ? Loc.Tr("Zum heutigen Video hinzufügen", "Add to today's video") : Loc.Tr("Hochladen", "Upload"),
                OnUpload, UIBtnKind.Live, PhoneKit.Big);
            up.style.marginTop = 14f;
            body.Add(up);
            var sub = UIKit.Row();
            sub.style.alignSelf = Align.Center;
            sub.style.marginTop = 8f;
            sub.Add(new UIGlyph(UIGlyphs.Moon, UIPalette.InkSoft, 22f));
            sub.Add(UIKit.Text(" " + Loc.Tr("Ergebnisse morgen früh", "Results tomorrow morning"), PhoneKit.Tiny, UIPalette.InkSoft));
            body.Add(sub);
        }

        static float SumSeconds(List<FootageClip> clips)
        {
            float t = 0f;
            foreach (var c in clips) t += Mathf.Max(0f, c.seconds);
            return t;
        }

        void OnUpload()
        {
            if (GameManager.I == null) return;
            var up = Progression.CreateUpload(style);
            if (up != null)
            {
                justUploaded = true;
                UIAudio.Pop();
            }
            Phone.Refresh(false);
        }

        static VisualElement ClipRow(FootageClip c)
        {
            var sp = c.Species;
            var row = UIKit.Row();
            row.style.backgroundColor = Color.white;
            UIKit.Radius(row, 18f);
            UIKit.Pad(row, 8f, 14f, 8f, 8f);
            row.style.marginBottom = 8f;
            if (sp != null) row.Add(UIWidgets.SpeciesIcon(sp, true, 96f, 60f));
            var col = UIKit.Col();
            col.style.marginLeft = 10f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(PhoneKit.T(sp != null ? sp.Name : c.speciesId, PhoneKit.Small, UIPalette.Ink, true));
            var tags = UIKit.Row();
            tags.style.marginTop = 4f;
            tags.style.flexWrap = Wrap.Wrap;
            var tier = UIWidgets.TierBadge(c.Tier, PhoneKit.Tiny);
            tier.style.marginRight = 6f;
            tags.Add(tier);
            if (c.isNewSpecies) { var p = UIKit.Pill(Loc.Tr("NEU", "NEW"), UIPalette.Turquoise, Color.white, PhoneKit.Tiny); p.style.marginRight = 6f; tags.Add(p); }
            if (c.isRecord) tags.Add(UIKit.Pill(Loc.Tr("REKORD", "RECORD"), UIPalette.Sun, UIPalette.Ink, PhoneKit.Tiny));
            col.Add(tags);
            row.Add(col);
            var val = UIKit.Row();
            val.Add(new UIGlyph(UIGlyphs.Star, UIPalette.Sun, 24f));
            val.Add(UIKit.Text(Mathf.RoundToInt(c.Value).ToString(Loc.Culture), PhoneKit.Body, UIPalette.Ink, true));
            row.Add(val);
            return row;
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

        static void BuildHistory(VisualElement body, GameState s)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Deine Videos", "Your videos"), UIGlyphs.Play));
            if (s.videos.Count == 0)
            {
                body.Add(PhoneKit.T(Loc.Tr("Noch keine Videos.", "No videos yet."), PhoneKit.Small, UIPalette.InkSoft));
                return;
            }
            int shown = 0;
            for (int i = s.videos.Count - 1; i >= 0 && shown < 20; i--, shown++)
            {
                var v = s.videos[i];
                var row = UIKit.Row();
                row.style.alignItems = Align.FlexStart;
                row.style.paddingTop = 10f; row.style.paddingBottom = 10f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = UIPalette.Line;
                var thumb = UIKit.Box();
                UIKit.Size(thumb, 120f, 68f);
                UIKit.Radius(thumb, 10f);
                UIKit.Image(thumb, UIKit.VGradient(UIPalette.Rgb(0x2478AA), UIPalette.Rgb(0x5ECAD9)), false);
                var top = SpeciesDatabase.Get(v.topSpeciesId);
                if (top != null)
                {
                    UIKit.Center(thumb);
                    thumb.Add(UIWidgets.SpeciesIcon(top, true, 104f, 60f));
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
