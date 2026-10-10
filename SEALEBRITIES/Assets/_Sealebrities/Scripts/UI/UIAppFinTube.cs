using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// FinTube Studio: banked footage, title style, estimates, upload, pending uploads and the video history.
    public class UIAppFinTube : UIPhoneApp
    {
        TitleStyle style = TitleStyle.Honest;
        bool justUploaded;

        public override string Id => "fintube";
        public override string Title => "FinTube";
        public override Color Color => UIPalette.Live;
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

            // channel stats
            var stats = UIKit.Row();
            stats.Add(UIWidgets.StatTile(Loc.Number(s.followers), Loc.Tr("Follower", "Followers"), UIPalette.Lagoon));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(UIWidgets.StatTile(Loc.Number(s.totalViews), Loc.Tr("Aufrufe", "Views"), UIPalette.Text));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(UIWidgets.StatTile(s.videos.Count.ToString(), Loc.Tr("Videos", "Videos"), UIPalette.Live));
            body.Add(stats);
            body.Add(UIKit.Spacer(10f));

            if (s.followers < Progression.MonetizationFollowers)
                body.Add(UIWidgets.Notice(Loc.Tr($"Geld gibt es erst ab {Loc.Number(Progression.MonetizationFollowers)} Followern. Bis dahin bringen Videos Aufrufe und Follower.",
                                                 $"Money only from {Loc.Number(Progression.MonetizationFollowers)} followers. Until then videos bring views and followers."), UIPalette.Sun, UIGlyphs.Warning));

            int dive = Footage.DiveCount;
            if (dive > 0)
                body.Add(UIWidgets.Notice(Loc.Tr($"{dive} Clips sind noch im Wasser – erst an Land/auf dem Boot sichern.",
                                                 $"{dive} clips are still in the water – save them on land/on the boat first."), UIPalette.Coral, UIGlyphs.Camera));

            if (justUploaded)
            {
                var ok = UIKit.Card(UIPalette.A(UIPalette.Money, 0.14f), 16f, 16f);
                var r = UIKit.Row();
                var c = UIKit.Box();
                UIKit.Size(c, 34f, 34f);
                UIKit.Radius(c, 17f);
                c.style.backgroundColor = UIPalette.Money;
                c.style.alignItems = Align.Center; c.style.justifyContent = Justify.Center;
                c.Add(new UIGlyph(UIGlyphs.Check, UIPalette.Deep, 22f));
                r.Add(c);
                var t = UIKit.Col();
                t.style.marginLeft = 12f;
                t.style.flexShrink = 1;
                t.Add(UIKit.Text(Loc.Tr("Hochgeladen!", "Uploaded!"), 18f, UIPalette.Money, true));
                t.Add(UIKit.Para(Loc.Tr("Ergebnisse morgen früh. Schlaf in deiner Hütte, um vorzuspulen.", "Results tomorrow morning. Sleep in your hut to skip ahead."), 13f, UIPalette.Text));
                r.Add(t);
                ok.Add(r);
                ok.style.marginBottom = 8f;
                body.Add(ok);
            }

            BuildUploadSection(body, s);
            BuildPending(body, s);
            BuildHistory(body, s);
        }

        void BuildUploadSection(VisualElement body, GameState s)
        {
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Bereit zum Hochladen", "Ready to upload"), UIPalette.Live));
            if (s.bankedFootage.Count == 0)
            {
                var empty = UIKit.Card(new Color(1f, 1f, 1f, 0.05f), 16f, 18f);
                empty.style.alignItems = Align.Center;
                empty.Add(new UIGlyph(UIGlyphs.Camera, UIPalette.TextFaint, 40f));
                var t = UIKit.Para(Loc.Tr("Keine gesicherten Clips. Geh tauchen, filme Tiere und komm heil zurück an Land oder aufs Boot.",
                                          "No saved clips. Go diving, film animals and get back to land or the boat safely."), 13.5f, UIPalette.TextDim);
                t.style.unityTextAlign = TextAnchor.MiddleCenter;
                t.style.marginTop = 8f;
                empty.Add(t);
                body.Add(empty);
                return;
            }

            var clips = Progression.SelectBestClips(s.bankedFootage);
            var info = UIKit.Text(Loc.Tr($"{s.bankedFootage.Count} Clips gesichert · die besten pro Art kommen ins Video (max. 12)",
                                         $"{s.bankedFootage.Count} clips saved · the best per species go into the video (max 12)"), 12f, UIPalette.TextFaint);
            info.style.whiteSpace = WhiteSpace.Normal;
            info.style.marginBottom = 6f;
            body.Add(info);
            float total = 0f;
            foreach (var c in clips) { body.Add(ClipRow(c)); total += c.Value; }

            // title style
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Titel-Stil", "Title style")));
            var seg = new UISegmented(new[] { Loc.TitleStyleName(TitleStyle.Honest), Loc.TitleStyleName(TitleStyle.Spicy), Loc.TitleStyleName(TitleStyle.Clickbait) },
                (int)style, i => { style = (TitleStyle)i; Phone.Refresh(true); }, UIPalette.Live, 13f);
            body.Add(seg);
            float vm = Progression.TitleViewMultiplier(style), cm = Progression.TitleConversionMultiplier(style);
            var eff = UIKit.Text(Loc.Tr("Aufrufe ", "Views ") + UIKit.Mult(vm) + "  ·  " + Loc.Tr("Follower-Rate ", "Follower rate ") + UIKit.Mult(cm), 12f, UIPalette.TextDim);
            eff.style.marginTop = 6f;
            body.Add(eff);

            // thumbnail + title preview
            var preview = UIKit.Row();
            preview.style.marginTop = 10f;
            preview.style.alignItems = Align.FlexStart;
            var thumb = UIKit.Box();
            UIKit.Size(thumb, 120f, 68f);
            UIKit.Radius(thumb, 10f);
            UIKit.Image(thumb, UIKit.VGradient(UIPalette.Rgb(0x082033), UIPalette.Rgb(0x1E6A8A)), false);
            thumb.style.alignItems = Align.Center; thumb.style.justifyContent = Justify.Center;
            if (clips.Count > 0 && clips[0].Species != null) thumb.Add(UIWidgets.SpeciesIcon(clips[0].Species, true, 100f, 58f));
            var dur = UIKit.Text(UIKit.Clock(SumSeconds(clips)), 10f, Color.white, true);
            dur.style.position = Position.Absolute;
            dur.style.right = 5f; dur.style.bottom = 4f;
            dur.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            UIKit.Pad(dur, 1f, 4f, 1f, 4f);
            UIKit.Radius(dur, 3f);
            thumb.Add(dur);
            preview.Add(thumb);
            var title = UIKit.Para(Progression.SuggestTitle(clips, style), 14f, UIPalette.Text);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginLeft = 10f;
            title.style.flexShrink = 1;
            preview.Add(title);
            body.Add(preview);

            // estimate
            var est = Progression.Preview(style);
            var estRow = UIKit.Row();
            estRow.style.marginTop = 12f;
            estRow.style.alignItems = Align.Stretch;
            estRow.Add(UIWidgets.StatTile(Loc.Number(est.viewsMin) + "–" + Loc.Number(est.viewsMax), Loc.Tr("Aufrufe (geschätzt)", "Views (estimate)"), UIPalette.Text));
            estRow.Add(UIKit.HSpacer(6f));
            estRow.Add(UIWidgets.StatTile(UIKit.Signed(est.followers), Loc.Tr("Follower", "Followers"), UIPalette.Lagoon));
            estRow.Add(UIKit.HSpacer(6f));
            estRow.Add(s.followers >= Progression.MonetizationFollowers
                ? UIWidgets.StatTile(UIKit.SignedMoney(est.money), Loc.Tr("Geld", "Money"), UIPalette.Money)
                : UIWidgets.StatTile("—", Loc.Tr("Geld ab 100 F.", "Money from 100 f."), UIPalette.TextFaint));
            body.Add(estRow);

            var up = UIKit.Btn(Loc.Tr("Hochladen", "Upload"), OnUpload, UIBtnKind.Live, 17f);
            up.style.marginTop = 12f;
            body.Add(up);
            var sub = UIKit.Text(Loc.Tr("Ergebnisse morgen früh", "Results tomorrow morning"), 12f, UIPalette.TextFaint);
            sub.style.alignSelf = Align.Center;
            sub.style.marginTop = 6f;
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
                GameEvents.RaiseToast(Loc.Tr("Video hochgeladen – Ergebnisse morgen früh.", "Video uploaded – results tomorrow morning."));
            }
            Phone.Refresh(false);
        }

        static VisualElement ClipRow(FootageClip c)
        {
            var sp = c.Species;
            var row = UIKit.Row();
            row.style.backgroundColor = new Color(1f, 1f, 1f, 0.05f);
            UIKit.Radius(row, 12f);
            UIKit.Pad(row, 6f, 12f, 6f, 6f);
            row.style.marginBottom = 5f;
            if (sp != null) row.Add(UIWidgets.SpeciesIcon(sp, true, 58f, 40f));
            var col = UIKit.Col();
            col.style.marginLeft = 8f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text(sp != null ? sp.Name : c.speciesId, 14f, UIPalette.Text, true));
            var tags = UIKit.Row();
            tags.style.marginTop = 3f;
            tags.style.flexWrap = Wrap.Wrap;
            var tier = UIWidgets.TierBadge(c.Tier, 10f);
            tier.style.marginRight = 5f;
            tags.Add(tier);
            tags.Add(UIKit.Text(Loc.Length(c.sizeCm), 12f, UIPalette.TextDim, true));
            if (c.isNewSpecies) { var p = UIKit.Pill(Loc.Tr("NEU", "NEW"), UIPalette.Lagoon, UIPalette.Deep, 9f); p.style.marginLeft = 5f; tags.Add(p); }
            if (c.isRecord) { var p = UIKit.Pill(Loc.Tr("REKORD", "RECORD"), UIPalette.Sun, UIPalette.Deep, 9f); p.style.marginLeft = 5f; tags.Add(p); }
            col.Add(tags);
            row.Add(col);
            var val = UIKit.Col();
            val.style.alignItems = Align.FlexEnd;
            val.Add(UIKit.Text(Mathf.RoundToInt(c.Value).ToString(Loc.Culture), 16f, UIPalette.Sun, true));
            val.Add(UIKit.Text(Loc.Tr("Wert", "Value"), 10f, UIPalette.TextFaint));
            row.Add(val);
            return row;
        }

        static void BuildPending(VisualElement body, GameState s)
        {
            if (s.pendingUploads.Count == 0) return;
            body.Add(UIWidgets.SectionTitle(Loc.Tr("In Bearbeitung", "Processing")));
            foreach (var p in s.pendingUploads)
            {
                var row = UIKit.Row();
                row.style.backgroundColor = UIPalette.A(UIPalette.Sky, 0.1f);
                UIKit.Radius(row, 12f);
                UIKit.Pad(row, 10f, 12f, 10f, 12f);
                row.style.marginBottom = 5f;
                var col = UIKit.Col();
                col.style.flexShrink = 1;
                col.Add(UIKit.Para(p.title, 13.5f, UIPalette.Text));
                col.Add(UIKit.Text(Loc.Tr($"Tag {p.dayUploaded} · {p.clips.Count} Clips · Ergebnisse morgen früh", $"Day {p.dayUploaded} · {p.clips.Count} clips · results tomorrow morning"), 11.5f, UIPalette.Sky));
                row.Add(col);
                body.Add(row);
            }
        }

        static void BuildHistory(VisualElement body, GameState s)
        {
            body.Add(UIWidgets.SectionTitle(Loc.Tr("Deine Videos", "Your videos")));
            if (s.videos.Count == 0)
            {
                body.Add(UIKit.Text(Loc.Tr("Noch keine Videos veröffentlicht.", "No videos published yet."), 13f, UIPalette.TextFaint));
                return;
            }
            int shown = 0;
            for (int i = s.videos.Count - 1; i >= 0 && shown < 25; i--, shown++)
            {
                var v = s.videos[i];
                var row = UIKit.Row();
                row.style.alignItems = Align.FlexStart;
                row.style.paddingTop = 8f; row.style.paddingBottom = 8f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = UIPalette.Line;
                var thumb = UIKit.Box();
                UIKit.Size(thumb, 74f, 42f);
                UIKit.Radius(thumb, 7f);
                thumb.style.backgroundColor = UIPalette.Raised2;
                var top = SpeciesDatabase.Get(v.topSpeciesId);
                if (top != null)
                {
                    thumb.style.alignItems = Align.Center; thumb.style.justifyContent = Justify.Center;
                    thumb.Add(UIWidgets.SpeciesIcon(top, true, 64f, 36f));
                }
                row.Add(thumb);
                var col = UIKit.Col();
                col.style.marginLeft = 10f;
                col.style.flexShrink = 1;
                col.style.flexGrow = 1;
                var t = UIKit.Para(v.title, 13f, UIPalette.Text);
                t.style.unityFontStyleAndWeight = FontStyle.Bold;
                col.Add(t);
                var line = UIKit.Text(Loc.Tr("Tag ", "Day ") + v.day + " · " + Loc.Number(v.views) + Loc.Tr(" Aufrufe", " views"), 11.5f, UIPalette.TextDim);
                line.style.marginTop = 2f;
                col.Add(line);
                var line2 = UIKit.Row();
                line2.style.marginTop = 2f;
                line2.Add(UIKit.Text(UIKit.Signed(v.followersGained) + Loc.Tr(" Follower", " followers"), 11.5f, UIPalette.Lagoon, true));
                var m = UIKit.Text(v.money > 0 ? UIKit.SignedMoney(v.money) : Loc.Tr("kein Geld", "no money"), 11.5f, v.money > 0 ? UIPalette.Money : UIPalette.TextFaint, true);
                m.style.marginLeft = 10f;
                line2.Add(m);
                col.Add(line2);
                row.Add(col);
                body.Add(row);
            }
        }
    }
}
