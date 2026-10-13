using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// FinTube Studio (L.4, Q31, Q50 + round 3 GDD §19 „FinTube-Studio: mehr Funktionen“, §20 Q33 „Clips auswählen &
    /// sortieren + Statistiken & Kommentare“). Channel header (rank badge, progress, upload streak, followers today) and
    /// three tabs:
    ///  * VIDEO – pick the clips of the next video and put them in ORDER (▲ ▼, ✕ = keep it banked for later, + = back in;
    ///    the first clip is the INTRO: title suggestions, the big collage tile and the video's top species follow it –
    ///    Progression.CreateUpload(…, clipOrder) / NetCommand.Upload 6th argument), title style, YOUR OWN TITLE, the
    ///    collage thumbnail (tap a tile to swap the animal), estimate, upload, processing + the video list.
    ///  * STATISTIK – numbers (total / average views, followers today, best day), views per day (bars from the
    ///    VideoRecords), follower growth per day (GameState.followerHistory) and the three best videos.
    ///  * KOMMENTARE – the top comments under every video, made up deterministically from the video (intro species,
    ///    other animals, title style, views compared with the channel, day) – no save data needed.
    public class UIAppFinTube : UIPhoneApp
    {
        public enum StudioTab { Video, Stats, Comments }

        StudioTab tab = StudioTab.Video;
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

        // ---- Q33 clip selection + order (UI state; the host rebuilds the same video from the order on upload)
        /// Best clip per species of today's video + the banked footage (Progression.VideoPool).
        readonly List<FootageClip> pool = new List<FootageClip>();
        /// Species of the next video in the player's order (first = intro).
        readonly List<string> order = new List<string>();
        /// Banked species the player took out of this video (stay banked for a later one) – forgotten every new day.
        readonly List<string> excluded = new List<string>();
        /// Species already in today's uploaded video (one video per day – they cannot be taken out again).
        readonly List<string> todayIds = new List<string>();
        /// The video as it would be uploaded now (Progression.OrderClips of `order`).
        List<FootageClip> videoClips = new List<FootageClip>();
        int excludedDay = -1;
        string orderKey;
        bool canUpload;
        VisualElement lastBody;

        // ---- comments
        readonly List<StudioCommentLine> commentBuf = new List<StudioCommentLine>(3);

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

        // ---- test hooks (AutoPlaytest / AutoShot)
        /// The page built last (the phone's scroll content) – tests look for named elements in it.
        public VisualElement DebugRoot => lastBody;
        public StudioTab CurrentTab => tab;
        /// Species of the next video in order (first = intro).
        public IReadOnlyList<string> ClipOrder => order;
        /// Species the player took out of the next video.
        public IReadOnlyList<string> ClipsLeftOut => excluded;

        public void DebugTab(StudioTab t)
        {
            tab = t;
            Phone?.Refresh(false);
        }

        /// Moves a species one place up (dir −1) or down (+1) in the video. False when it cannot move.
        public bool DebugMove(string speciesId, int dir)
        {
            int i = order.IndexOf(speciesId);
            if (i < 0) return false;
            return Move(i, dir);
        }

        /// Takes a banked species out of the video (in = false) or puts it back (in = true).
        public bool DebugSelect(string speciesId, bool include) => include ? Include(speciesId) : Exclude(speciesId);

        /// Presses „Hochladen“. True when the upload was created.
        public bool DebugUpload()
        {
            justUploaded = false;
            OnUpload();
            return justUploaded;
        }

        public override void OnOpen()
        {
            justUploaded = false;
            var s = UIKit.State;
            if (s != null && s.bankedFootage.Count > 0) tab = StudioTab.Video;
        }

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
            lastBody = body;
            var gm = GameManager.I;
            if (gm == null) return;
            var s = gm.State;

            BuildChannel(body, s);
            BuildTabs(body);
            switch (tab)
            {
                case StudioTab.Stats: BuildStats(body, s); break;
                case StudioTab.Comments: BuildComments(body, s); break;
                default: BuildVideoTab(body, s); break;
            }
        }

        // ------------------------------------------------------------------ channel header (Q50 rank, streak)
        void BuildChannel(VisualElement body, GameState s)
        {
            int rank = Progression.RankIndex(s.followers);
            var card = PhoneKit.Card(16f);
            card.name = "studio-channel";
            var row = UIKit.Row();
            row.Add(new UIRankBadge(rank, 76f));
            var col = UIKit.Col();
            col.style.marginLeft = 14f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.Add(UIKit.Text(Settings.DisplayName, PhoneKit.Big, UIPalette.Ink, true));
            col.Add(UIKit.Text(Progression.RankName(rank), PhoneKit.Small, UIWidgets.Ink(UIGlyphs.RankColor(rank)), true));
            row.Add(col);
            // upload streak (days in a row with a video) – the flame
            int streak = UploadStreak(s);
            var fl = UIKit.Col();
            fl.name = "studio-streak";
            fl.style.alignItems = Align.Center;
            fl.style.marginLeft = 8f;
            fl.style.opacity = streak > 0 ? 1f : 0.35f;
            fl.Add(new UIGlyph(UIGlyphs.CookFlame, streak > 0 ? UIPalette.Rgb(0xEE7F2D) : UIPalette.Silhouette, 40f));
            fl.Add(UIKit.Text(streak.ToString(Loc.Culture), PhoneKit.Small, UIPalette.Ink, true));
            row.Add(fl);
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
                nx.Add(UIKit.Flex());
                long todayGain = FollowersToday(s);
                nx.Add(new UIGlyph(UIGlyphs.Person, UIPalette.Turquoise, 20f));
                nx.Add(UIKit.Text(" " + UIKit.Signed(todayGain) + " " + Loc.Tr("heute", "today"), PhoneKit.Tiny, todayGain >= 0 ? UIPalette.TurquoiseInk : UIPalette.CoralInk, true));
                card.Add(nx);
            }
            var stats = UIKit.Row();
            stats.style.marginTop = 12f;
            stats.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Compact(s.followers)));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(s.totalViews)));
            stats.Add(UIKit.HSpacer(8f));
            stats.Add(PhoneKit.Stat(UIGlyphs.Play, UIPalette.Danger, s.videos.Count.ToString(Loc.Culture)));
            card.Add(stats);
            body.Add(card);
        }

        static long FollowersToday(GameState s)
        {
            int n = s.followerHistory.Count;
            return n > 0 ? s.followers - s.followerHistory[n - 1] : 0;
        }

        static bool UploadedOn(GameState s, int day)
        {
            for (int i = 0; i < s.videos.Count; i++) if (s.videos[i] != null && s.videos[i].day == day) return true;
            for (int i = 0; i < s.pendingUploads.Count; i++) if (s.pendingUploads[i] != null && s.pendingUploads[i].dayUploaded == day) return true;
            return false;
        }

        /// Days in a row with an upload, ending today (or yesterday while today's video is still open).
        public static int UploadStreak(GameState s)
        {
            if (s == null) return 0;
            int d = s.day;
            if (!UploadedOn(s, d)) d--;
            int n = 0;
            while (d >= 1 && n < 9999 && UploadedOn(s, d)) { n++; d--; }
            return n;
        }

        // ------------------------------------------------------------------ tabs
        void BuildTabs(VisualElement body)
        {
            var row = UIKit.Row();
            row.name = "studio-tabs";
            row.style.marginBottom = 10f;
            row.Add(TabButton(StudioTab.Video, UIGlyphs.Upload, Loc.Tr("Video", "Video"), "studio-tab-video"));
            row.Add(TabButton(StudioTab.Stats, UIGlyphs.Chart, Loc.Tr("Statistik", "Stats"), "studio-tab-stats"));
            row.Add(TabButton(StudioTab.Comments, UIGlyphs.StudioComment, Loc.Tr("Kommentare", "Comments"), "studio-tab-comments"));
            body.Add(row);
        }

        VisualElement TabButton(StudioTab t, UIGlyph.DrawFn icon, string text, string name)
        {
            bool sel = tab == t;
            var b = PhoneKit.IconButton(icon, text, () => { if (tab == t) return; tab = t; Phone.Refresh(false); },
                sel ? UIBtnKind.Live : UIBtnKind.Secondary, 18f);
            b.name = name;
            b.style.flexGrow = 1;
            b.style.flexBasis = 0;
            UIKit.Pad(b, 12f, 6f, 12f, 6f);
            if (t != StudioTab.Video) b.style.marginLeft = 6f;
            return b;
        }

        // ================================================================== VIDEO tab
        void BuildVideoTab(VisualElement body, GameState s)
        {
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

        // ------------------------------------------------------------------ clip selection + order
        FootageClip PoolClip(string id)
        {
            for (int i = 0; i < pool.Count; i++) if (pool[i].speciesId == id) return pool[i];
            return null;
        }

        static bool HasBanked(GameState s, string id)
        {
            for (int i = 0; i < s.bankedFootage.Count; i++) if (s.bankedFootage[i] != null && s.bankedFootage[i].speciesId == id) return true;
            return false;
        }

        /// Brings the order in line with the pool: gone species leave, today's clips are always in, new species join at
        /// the end (best first) until the video is full – the rest waits under „Nicht im Video“.
        void Reconcile(GameState s)
        {
            Progression.VideoPool(s, pool);
            todayIds.Clear();
            var today = Progression.TodaysUpload(s);
            if (today != null && today.clips != null)
                for (int i = 0; i < today.clips.Count; i++)
                {
                    var c = today.clips[i];
                    if (c != null && !string.IsNullOrEmpty(c.speciesId) && !todayIds.Contains(c.speciesId)) todayIds.Add(c.speciesId);
                }
            if (excludedDay != s.day) { excluded.Clear(); excludedDay = s.day; }
            for (int i = order.Count - 1; i >= 0; i--) if (PoolClip(order[i]) == null) order.RemoveAt(i);
            for (int i = excluded.Count - 1; i >= 0; i--) if (PoolClip(excluded[i]) == null || todayIds.Contains(excluded[i])) excluded.RemoveAt(i);
            bool anyToday = false;
            for (int i = 0; i < todayIds.Count; i++) if (order.Contains(todayIds[i])) { anyToday = true; break; }
            int insertAt = 0;
            for (int i = 0; i < todayIds.Count; i++)
            {
                if (order.Contains(todayIds[i])) continue;
                if (!anyToday) order.Insert(Mathf.Min(insertAt++, order.Count), todayIds[i]);
                else order.Add(todayIds[i]);
            }
            for (int i = 0; i < pool.Count; i++)
            {
                string id = pool[i].speciesId;
                if (order.Contains(id) || excluded.Contains(id)) continue;
                if (order.Count < Progression.MaxVideoClips) order.Add(id);
                else excluded.Add(id);
            }
            videoClips = Progression.OrderClips(s, order) ?? new List<FootageClip>();
            canUpload = false;
            for (int i = 0; i < videoClips.Count && !canUpload; i++) if (HasBanked(s, videoClips[i].speciesId)) canUpload = true;
        }

        bool Move(int i, int dir)
        {
            int j = i + dir;
            if (i < 0 || i >= order.Count || j < 0 || j >= order.Count) { UIAudio.Nope(); return false; }
            string t = order[i];
            order[i] = order[j];
            order[j] = t;
            Phone?.Refresh(true);
            return true;
        }

        bool Exclude(string id)
        {
            if (string.IsNullOrEmpty(id) || todayIds.Contains(id) || !order.Contains(id)) { UIAudio.Nope(); return false; }
            order.Remove(id);
            if (!excluded.Contains(id)) excluded.Add(id);
            Phone?.Refresh(true);
            return true;
        }

        bool Include(string id)
        {
            if (string.IsNullOrEmpty(id) || order.Contains(id) || order.Count >= Progression.MaxVideoClips || PoolClip(id) == null) { UIAudio.Nope(); return false; }
            excluded.Remove(id);
            order.Add(id);
            Phone?.Refresh(true);
            return true;
        }

        void SortBestFirst()
        {
            order.Sort((a, b) =>
            {
                var ca = PoolClip(a); var cb = PoolClip(b);
                return (cb != null ? cb.Value : 0f).CompareTo(ca != null ? ca.Value : 0f);
            });
            Phone?.Refresh(true);
        }

        static string KeyOf(List<FootageClip> list)
        {
            if (list == null || list.Count == 0) return "";
            var sb = new System.Text.StringBuilder(list.Count * 12);
            for (int i = 0; i < list.Count; i++) sb.Append(list[i].speciesId).Append(',');
            return sb.ToString();
        }

        void BuildUploadSection(VisualElement body, GameState s)
        {
            body.Add(PhoneKit.Section(Loc.Tr("Nächstes Video", "Next video"), UIGlyphs.Upload));
            Reconcile(s);
            var today = Progression.TodaysUpload(s);
            if (s.bankedFootage.Count == 0)
            {
                body.Add(PhoneKit.Empty(UIGlyphs.Camera, Loc.Tr("Keine gesicherten Clips. Tauchen, filmen, heil zurückkommen!", "No saved clips. Dive, film, come back safely!")));
                return;
            }

            // the collage animals follow the clip order (intro = the big tile); reset when the video changes
            string key = KeyOf(videoClips);
            if (key != orderKey)
            {
                orderKey = key;
                thumbCount = Mathf.Min(UIThumbnailComposer.MaxTiles, videoClips.Count);
                for (int i = 0; i < thumbCount; i++) thumbIds[i] = videoClips[i].speciesId;
            }

            // the daily core action first: estimate + a compact upload button (one tap uploads the chosen clips with the
            // defaults – first suggestion, auto collage)
            FillTiles(s);
            float q = UIThumbnailComposer.Quality(tiles, thumbCount);
            var est = Progression.Preview(style, videoClips);
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
            upTop.name = "studio-upload-quick";
            UIKit.SetEnabled(upTop, canUpload);
            topRow.Add(upTop);
            // final test: the numbers shrank behind the button („~3 +1“ – IconStat has basis 0 / grow 1 for even grids):
            // here they take their natural width, the button wraps to the next line when it does not fit
            foreach (var child in topRow.Children())
            {
                if (child.style.backgroundColor.value != Color.white) continue;   // only the three IconStats (white pills)
                child.style.flexShrink = 0;
                child.style.flexBasis = StyleKeyword.Auto;
                child.style.flexGrow = 0;
            }
            upTop.style.flexShrink = 0;
            body.Add(topRow);

            BuildClipList(body, s);

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

            // own title + suggestion chips (they follow the intro clip)
            Progression.SuggestTitles(videoClips, style, suggestions);
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
            collage.name = "studio-collage";
            body.Add(collage);
            var qrow = UIKit.Row();
            qrow.style.marginTop = 6f;
            qrow.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 22f));
            qrow.Add(UIKit.Text(" +" + Mathf.RoundToInt(Progression.ThumbnailBonus * q * 100f) + " % " + Loc.Tr("Aufrufe", "views"), PhoneKit.Tiny, UIPalette.LeafInk, true));
            qrow.Add(UIKit.Flex());
            qrow.Add(UIKit.Text(Loc.Tr("Bild antippen = Tier tauschen", "Tap a tile = swap animal"), PhoneKit.Tiny, UIPalette.InkSoft));
            body.Add(qrow);

            // second upload button at the end (after choosing clips / title / collage)
            if (!canUpload)
                body.Add(UIWidgets.Notice(Loc.Tr("Wähle mindestens einen neuen Clip.", "Pick at least one new clip."), UIPalette.Warn, UIGlyphs.Camera, PhoneKit.Small));
            var up = PhoneKit.IconButton(UIGlyphs.Upload,
                today != null ? Loc.Tr("Zum heutigen Video", "Add to today's video") : Loc.Tr("Hochladen", "Upload"),
                OnUpload, UIBtnKind.Live, PhoneKit.Big);
            up.name = "studio-upload";
            up.style.marginTop = 14f;
            UIKit.SetEnabled(up, canUpload);
            body.Add(up);
        }

        // the clip list: the video in order (▲ ▼ ✕) and the banked clips left out (+)
        void BuildClipList(VisualElement body, GameState s)
        {
            var head = UIKit.Row();
            head.style.marginTop = 10f;
            head.style.marginBottom = 8f;
            head.Add(new UIGlyph(UIGlyphs.Sort, UIPalette.InkSoft, 26f));
            var ht = UIKit.Text(" " + Loc.Tr("Clips · Reihenfolge", "Clips · order"), PhoneKit.Small, UIPalette.InkSoft, true);
            head.Add(ht);
            head.Add(UIKit.Text("  " + videoClips.Count + "/" + Progression.MaxVideoClips, PhoneKit.Tiny, UIPalette.Silhouette, true));
            head.Add(UIKit.Flex());
            if (videoClips.Count > 1)
            {
                var best = PhoneKit.IconButton(UIGlyphs.Star, Loc.Tr("Beste zuerst", "Best first"), SortBestFirst, UIBtnKind.Secondary, PhoneKit.Tiny);
                best.name = "studio-sort-best";
                UIKit.Pad(best, 8f, 12f, 8f, 12f);
                head.Add(best);
            }
            body.Add(head);

            var list = UIKit.Col();
            list.name = "studio-clips";
            for (int i = 0; i < videoClips.Count; i++) list.Add(ClipRow(s, videoClips[i], i, videoClips.Count));
            body.Add(list);

            // banked clips that are not in this video (they stay banked for a later one)
            bool any = false;
            for (int i = 0; i < pool.Count; i++)
            {
                var c = pool[i];
                if (order.Contains(c.speciesId)) continue;
                if (!any)
                {
                    any = true;
                    var oh = UIKit.Row();
                    oh.style.marginTop = 6f;
                    oh.style.marginBottom = 6f;
                    oh.Add(new UIGlyph(UIGlyphs.Clock, UIPalette.Silhouette, 22f));
                    oh.Add(UIKit.Text(" " + Loc.Tr("Nicht im Video – bleibt gespeichert", "Not in the video – stays saved"), PhoneKit.Tiny, UIPalette.InkSoft, true));
                    body.Add(oh);
                }
                body.Add(OutRow(c));
            }
        }

        VisualElement ClipRow(GameState s, FootageClip c, int pos, int count)
        {
            var sp = c.Species;
            bool intro = pos == 0;
            bool finale = count >= 3 && pos == count - 1;
            bool inToday = todayIds.Contains(c.speciesId);
            int oi = order.IndexOf(c.speciesId);
            var row = UIKit.Row();
            row.name = "studio-clip-" + c.speciesId;
            row.AddToClassList("studio-clip");
            row.style.marginBottom = 6f;
            UIKit.Radius(row, 18f);
            UIKit.Pad(row, 8f, 8f, 8f, 8f);
            row.style.backgroundColor = intro ? UIPalette.A(UIPalette.Danger, 0.1f) : Color.white;
            if (intro) UIKit.Border(row, 2f, UIPalette.A(UIPalette.Danger, 0.5f));

            var num = UIKit.Box();
            UIKit.Size(num, 38f, 38f);
            UIKit.Radius(num, 19f);
            UIKit.Center(num);
            num.style.backgroundColor = intro ? UIPalette.Danger : UIPalette.A(UIPalette.Ink, 0.08f);
            num.Add(UIKit.Text((pos + 1).ToString(Loc.Culture), PhoneKit.Small, intro ? Color.white : UIPalette.Ink, true));
            row.Add(num);

            if (sp != null)
            {
                var icon = UIWidgets.SpeciesIcon(sp, true, 84f, 48f);
                icon.style.marginLeft = 8f;
                row.Add(icon);
            }

            var col = UIKit.Col();
            col.style.marginLeft = 8f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.style.overflow = Overflow.Hidden;
            var name = UIKit.Text(sp != null ? sp.Name : c.speciesId, PhoneKit.Tiny, UIPalette.Ink, true);
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            col.Add(name);
            var line = UIKit.Row();
            line.style.flexWrap = Wrap.Wrap;
            line.Add(new UIGlyph(UIGlyphs.Star, UIPalette.Sun, 18f));
            line.Add(UIKit.Text(Mathf.RoundToInt(c.Value).ToString(Loc.Culture) + " ", 16f, UIPalette.Ink, true));
            if (intro) line.Add(UIKit.Pill("INTRO", UIPalette.Danger, Color.white, 13f));
            else if (finale) line.Add(UIKit.Pill("FINALE", UIPalette.Violet, Color.white, 13f));
            if (inToday) line.Add(UIKit.Pill(Loc.Tr("DRIN", "IN"), UIPalette.Sky, Color.white, 13f));
            if (c.isNewSpecies) line.Add(UIKit.Pill(Loc.Tr("NEU", "NEW"), UIPalette.Turquoise, Color.white, 13f));
            if (c.isRecord) line.Add(UIKit.Pill(Loc.Tr("REKORD", "RECORD"), UIPalette.Sun, UIPalette.Ink, 13f));
            if (c.selfie) line.Add(UIKit.Pill("SELFIE", UIPalette.Coral, Color.white, 13f));
            if (!string.IsNullOrEmpty(c.eventId)) line.Add(UIKit.Pill("EVENT", UIPalette.Violet, Color.white, 13f));
            col.Add(line);
            row.Add(col);

            int idx = oi;
            row.Add(SmallButton(UIGlyphs.StudioUp, idx > 0, () => Move(idx, -1), "up"));
            row.Add(SmallButton(UIGlyphs.StudioDown, idx >= 0 && idx < order.Count - 1, () => Move(idx, 1), "down"));
            string id = c.speciesId;
            if (inToday)
            {
                // already in today's uploaded video – it stays
                var lk = UIKit.Box();
                UIKit.Size(lk, 44f, 44f);
                UIKit.Center(lk);
                lk.style.marginLeft = 4f;
                lk.Add(new UIGlyph(UIGlyphs.Lock, UIPalette.Silhouette, 22f));
                row.Add(lk);
            }
            else row.Add(SmallButton(UIGlyphs.Cross, true, () => Exclude(id), "out"));
            return row;
        }

        VisualElement OutRow(FootageClip c)
        {
            var sp = c.Species;
            var row = UIKit.Row();
            row.name = "studio-out-" + c.speciesId;
            row.AddToClassList("studio-clip-out");
            row.style.marginBottom = 6f;
            UIKit.Radius(row, 18f);
            UIKit.Pad(row, 6f, 8f, 6f, 8f);
            row.style.backgroundColor = UIPalette.A(Color.white, 0.6f);
            UIKit.Border(row, 2f, UIPalette.A(UIPalette.Ink, 0.08f));
            if (sp != null)
            {
                var icon = UIWidgets.SpeciesIcon(sp, true, 70f, 40f);
                icon.style.opacity = 0.6f;
                row.Add(icon);
            }
            var col = UIKit.Col();
            col.style.marginLeft = 8f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            col.style.overflow = Overflow.Hidden;
            var name = UIKit.Text(sp != null ? sp.Name : c.speciesId, PhoneKit.Tiny, UIPalette.InkSoft, true);
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            col.Add(name);
            var line = UIKit.Row();
            line.Add(new UIGlyph(UIGlyphs.Star, UIPalette.Sun, 16f));
            line.Add(UIKit.Text(Mathf.RoundToInt(c.Value).ToString(Loc.Culture), 15f, UIPalette.InkSoft, true));
            col.Add(line);
            row.Add(col);
            string id = c.speciesId;
            row.Add(SmallButton(UIGlyphs.Plus, order.Count < Progression.MaxVideoClips, () => Include(id), "in"));
            return row;
        }

        static VisualElement SmallButton(UIGlyph.DrawFn icon, bool enabled, System.Action onClick, string what)
        {
            if (!enabled)
            {
                var off = UIKit.Box();
                UIKit.Size(off, 44f, 44f);
                UIKit.Center(off);
                off.style.marginLeft = 4f;
                off.Add(new UIGlyph(icon, UIPalette.A(UIPalette.Ink, 0.18f), 22f));
                off.pickingMode = PickingMode.Ignore;
                return off;
            }
            var b = UIWidgets.RoundIconButton(icon, () => onClick(), 44f);
            b.name = "studio-btn-" + what;
            b.style.marginLeft = 4f;
            return b;
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
            var clips = videoClips;
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
            Reconcile(s);
            if (!canUpload || videoClips.Count == 0) { UIAudio.Nope(); Phone?.Refresh(true); return; }
            string key = KeyOf(videoClips);
            if (thumbCount == 0 || key != orderKey)
            {
                orderKey = key;
                thumbCount = Mathf.Min(UIThumbnailComposer.MaxTiles, videoClips.Count);
                for (int i = 0; i < thumbCount; i++) thumbIds[i] = videoClips[i].speciesId;
            }
            FillTiles(s);
            float q = UIThumbnailComposer.Quality(tiles, thumbCount);
            string path = UIThumbnailComposer.Save(s.day, tiles, thumbCount);
            var ids = new string[thumbCount];
            for (int i = 0; i < thumbCount; i++) ids[i] = thumbIds[i];
            var clipOrder = new string[videoClips.Count];
            for (int i = 0; i < clipOrder.Length; i++) clipOrder[i] = videoClips[i].speciesId;
            Progression.SuggestTitles(videoClips, style, suggestions);
            string title = Progression.SanitizeTitle(titleText);
            bool custom = customTitle && title.Length > 0;
            if (title.Length == 0 && suggestions.Count > 0) title = suggestions[0];
            var up = Progression.CreateUpload(style, title, ids, path, custom, q, clipOrder);
            if (up != null)
            {
                justUploaded = true;
                titleText = "";
                customTitle = false;
                orderKey = null;
                order.Clear();   // rebuilt from today's video (same order) + what stays banked
                UIAudio.Pop();
            }
            Phone?.Refresh(false);
        }

        static void BuildPending(VisualElement body, GameState s)
        {
            if (s.pendingUploads.Count == 0) return;
            body.Add(PhoneKit.Section(Loc.Tr("In Bearbeitung", "Processing"), UIGlyphs.Clock));
            foreach (var p in s.pendingUploads)
            {
                if (p == null) continue;
                var row = PhoneKit.Card(14f);
                row.style.backgroundColor = UIPalette.A(UIPalette.Sky, 0.14f);
                row.Add(PhoneKit.T(p.title, PhoneKit.Small, UIPalette.Ink, true));
                var l = UIKit.Row();
                l.style.marginTop = 6f;
                l.style.flexWrap = Wrap.Wrap;
                l.Add(new UIGlyph(UIGlyphs.Moon, UIPalette.InkSoft, 22f));
                l.Add(UIKit.Text(" " + (p.clips != null ? p.clips.Count : 0) + " Clips  ", PhoneKit.Tiny, UIPalette.InkSoft, true));
                // the order as uploaded: intro first
                if (p.clips != null)
                    for (int i = 0; i < p.clips.Count && i < 6; i++)
                    {
                        var sp = p.clips[i] != null ? p.clips[i].Species : null;
                        if (sp == null) continue;
                        var ic = UIWidgets.SpeciesIcon(sp, true, 44f, 26f);
                        ic.style.marginRight = 2f;
                        l.Add(ic);
                    }
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
            for (int i = s.videos.Count - 1; i >= 0 && shown < 20; i--, shown++)
            {
                var v = s.videos[i];
                if (v == null) continue;
                var row = UIKit.Row();
                row.style.alignItems = Align.FlexStart;
                row.style.paddingTop = 10f; row.style.paddingBottom = 10f;
                row.style.borderBottomWidth = 1f;
                row.style.borderBottomColor = UIPalette.Line;
                row.Add(VideoThumb(v, 160f, 90f));
                var col = UIKit.Col();
                col.style.marginLeft = 12f;
                col.style.flexShrink = 1;
                col.style.flexGrow = 1;
                col.Add(PhoneKit.T(v.title, PhoneKit.Tiny, UIPalette.Ink, true));
                col.Add(VideoNumbers(v));
                row.Add(col);
                body.Add(row);
            }
        }

        static VisualElement VideoNumbers(VideoRecord v)
        {
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
            return line;
        }

        readonly UIThumbnailComposer.Tile[] tmpTiles = new UIThumbnailComposer.Tile[UIThumbnailComposer.MaxTiles];

        /// The video's saved collage (this machine) or the collage rebuilt from its species.
        VisualElement VideoThumb(VideoRecord v, float w, float h)
        {
            Texture2D png = null;
            if (!string.IsNullOrEmpty(v.thumbPath) && !historyCache.TryGetValue(v.thumbPath, out png) && historyCache.Count < 24)
            {
                png = UIThumbnailComposer.Load(v.thumbPath);
                historyCache[v.thumbPath] = png;
            }
            if (png != null)
            {
                var thumb = UIKit.Box();
                UIKit.Size(thumb, w, h);
                UIKit.Radius(thumb, 12f);
                UIKit.ImageCover(thumb, png);
                thumb.style.flexShrink = 0;
                return thumb;
            }
            int n = 0;
            if (v.thumbSpecies != null)
                for (int k = 0; k < v.thumbSpecies.Length && n < tmpTiles.Length; k++)
                {
                    var sp = SpeciesDatabase.Get(v.thumbSpecies[k]);
                    if (sp != null) tmpTiles[n++] = new UIThumbnailComposer.Tile { species = sp };
                }
            if (n == 0)
            {
                var top = SpeciesDatabase.Get(v.topSpeciesId);
                if (top != null) tmpTiles[n++] = new UIThumbnailComposer.Tile { species = top };
            }
            var e = UIThumbnailComposer.Build(tmpTiles, n, (TitleStyle)Mathf.Clamp(v.titleStyle, 0, 2), w, h, null);
            e.style.flexShrink = 0;
            for (int k = 0; k < tmpTiles.Length; k++) tmpTiles[k] = default;
            return e;
        }

        // ================================================================== STATISTIK tab
        const int ChartDays = 14;

        void BuildStats(VisualElement body, GameState s)
        {
            int n = s.videos.Count;
            long best = 0;
            for (int i = 0; i < n; i++) if (s.videos[i] != null && s.videos[i].views > best) best = s.videos[i].views;
            long avg = n > 0 ? s.totalViews / n : 0;

            var r1 = UIKit.Row();
            r1.name = "studio-numbers";
            r1.Add(PhoneKit.Stat(UIGlyphs.Eye, UIPalette.InkSoft, UIKit.Compact(s.totalViews)));
            r1.Add(UIKit.HSpacer(8f));
            r1.Add(LabeledStat("Ø", UIKit.Compact(avg)));
            body.Add(r1);
            var r2 = UIKit.Row();
            r2.style.marginTop = 8f;
            long gain = FollowersToday(s);
            r2.Add(PhoneKit.Stat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Signed(gain), gain >= 0 ? UIPalette.TurquoiseInk : UIPalette.CoralInk));
            r2.Add(UIKit.HSpacer(8f));
            r2.Add(PhoneKit.Stat(UIGlyphs.Trophy, UIPalette.SunInk, UIKit.Compact(best)));
            body.Add(r2);

            // views per day (the video's upload day; today's open video = hollow bar)
            int last = Mathf.Max(1, s.day), first = Mathf.Max(1, last - ChartDays + 1);
            var views = new long[last - first + 1];
            for (int i = 0; i < n; i++)
            {
                var v = s.videos[i];
                if (v == null || v.day < first || v.day > last) continue;
                views[v.day - first] += v.views;
            }
            int pending = -1;
            for (int i = 0; i < s.pendingUploads.Count; i++)
            {
                var p = s.pendingUploads[i];
                if (p != null && p.dayUploaded >= first && p.dayUploaded <= last && views[p.dayUploaded - first] == 0) pending = p.dayUploaded - first;
            }
            body.Add(PhoneKit.Section(Loc.Tr("Aufrufe pro Tag", "Views per day"), UIGlyphs.Eye));
            body.Add(ChartCard("studio-chart-views", views, UIPalette.Rgb(0xFF8A8A), UIPalette.Danger, pending,
                Loc.Tr("Tag ", "Day ") + first, last == s.day ? Loc.Tr("Heute", "Today") : Loc.Tr("Tag ", "Day ") + last));

            // follower growth per day (followerHistory = followers at the start of each day, + now)
            var hist = s.followerHistory;
            int hc = hist.Count;
            int gains = Mathf.Min(ChartDays, Mathf.Max(1, hc));
            var growth = new long[gains];
            long week = 0;
            for (int k = 0; k < gains; k++)
            {
                int i1 = hc - gains + k + 1;   // the end of that day (start of the next, or now)
                int i0 = i1 - 1;
                long a = i0 >= 0 && i0 < hc ? hist[i0] : 0;
                long b = i1 < hc ? hist[i1] : s.followers;
                growth[k] = b - a;
                if (k >= gains - 7) week += growth[k];
            }
            int firstGrowthDay = Mathf.Max(1, s.day - gains + 1);
            var gh = PhoneKit.Section(Loc.Tr("Follower-Wachstum", "Follower growth"), UIGlyphs.Person);
            gh.Add(UIKit.Flex());
            gh.Add(UIKit.Text(UIKit.Signed(week) + " · 7 " + Loc.Tr("Tage", "days"), PhoneKit.Tiny, week >= 0 ? UIPalette.TurquoiseInk : UIPalette.CoralInk, true));
            body.Add(gh);
            body.Add(ChartCard("studio-chart-followers", growth, UIPalette.A(UIPalette.Turquoise, 0.75f), UIPalette.TurquoiseInk, -1,
                Loc.Tr("Tag ", "Day ") + firstGrowthDay, Loc.Tr("Heute", "Today")));

            // the three best videos
            body.Add(PhoneKit.Section(Loc.Tr("Beste Videos", "Best videos"), UIGlyphs.Trophy));
            if (n == 0)
            {
                body.Add(PhoneKit.Empty(UIGlyphs.Play, Loc.Tr("Noch keine Videos.", "No videos yet.")));
                return;
            }
            var top = new List<VideoRecord>(n);
            for (int i = 0; i < n; i++) if (s.videos[i] != null) top.Add(s.videos[i]);
            top.Sort((a, b) => b.views.CompareTo(a.views));
            Color[] medal = { UIPalette.Sun, UIPalette.Rgb(0xC5CCD4), UIPalette.Rgb(0xD9965B) };
            for (int i = 0; i < top.Count && i < 3; i++)
            {
                var v = top[i];
                var row = PhoneKit.Card(12f);
                row.name = "studio-best-" + i;
                row.AddToClassList("studio-best");
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                var m = UIKit.Box();
                UIKit.Size(m, 42f, 42f);
                UIKit.Radius(m, 21f);
                UIKit.Center(m);
                m.style.backgroundColor = medal[i];
                m.style.flexShrink = 0;
                m.Add(UIKit.Text((i + 1).ToString(Loc.Culture), PhoneKit.Small, UIPalette.Ink, true));
                row.Add(m);
                var th = VideoThumb(v, 128f, 72f);
                th.style.marginLeft = 10f;
                row.Add(th);
                var col = UIKit.Col();
                col.style.marginLeft = 10f;
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                var t = PhoneKit.T(v.title, PhoneKit.Tiny, UIPalette.Ink, true);
                t.style.maxHeight = 50f;
                t.style.overflow = Overflow.Hidden;
                col.Add(t);
                col.Add(VideoNumbers(v));
                row.Add(col);
                body.Add(row);
            }
        }

        static VisualElement LabeledStat(string label, string value)
        {
            var row = UIKit.Row();
            row.style.flexGrow = 1;
            row.style.flexBasis = 0;
            row.style.backgroundColor = Color.white;
            UIKit.Radius(row, 16f);
            UIKit.Pad(row, PhoneKit.Big * 0.45f, PhoneKit.Big * 0.55f, PhoneKit.Big * 0.45f, PhoneKit.Big * 0.45f);
            UIKit.Lift(row, 0.8f);
            var l = UIKit.Text(label, PhoneKit.Big, UIPalette.InkSoft, true);
            l.style.marginRight = 10f;
            row.Add(l);
            row.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 26f));
            var v = UIKit.Text(" " + value, PhoneKit.Big, UIPalette.Ink, true);
            v.style.flexShrink = 1;
            row.Add(v);
            return row;
        }

        static VisualElement ChartCard(string name, long[] values, Color color, Color accent, int pending, string leftLabel, string rightLabel)
        {
            var card = PhoneKit.Card(14f);
            long max = 0;
            for (int i = 0; i < values.Length; i++) if (values[i] > max) max = values[i];
            var top = UIKit.Row();
            top.Add(UIKit.Text(UIKit.Compact(max), PhoneKit.Tiny, UIPalette.Silhouette, true));
            card.Add(top);
            var chart = new UIStudioChart(values, color, accent, pending) { name = name };
            chart.style.height = 170f;
            chart.style.marginTop = 4f;
            card.Add(chart);
            var axis = UIKit.Row();
            axis.style.marginTop = 6f;
            axis.Add(UIKit.Text(leftLabel, PhoneKit.Tiny, UIPalette.InkSoft));
            axis.Add(UIKit.Flex());
            axis.Add(UIKit.Text(rightLabel, PhoneKit.Tiny, UIPalette.InkSoft));
            card.Add(axis);
            return card;
        }

        // ================================================================== KOMMENTARE tab
        const int CommentVideos = 8;

        void BuildComments(VisualElement body, GameState s)
        {
            if (s.pendingUploads.Count > 0)
                body.Add(UIWidgets.Notice(Loc.Tr("Kommentare zum neuen Video kommen morgen früh.", "Comments on the new video arrive tomorrow morning."),
                    UIPalette.Sky, UIGlyphs.Moon, PhoneKit.Small));
            if (s.videos.Count == 0)
            {
                body.Add(PhoneKit.Empty(UIGlyphs.StudioComment, Loc.Tr("Noch keine Kommentare – lade dein erstes Video hoch!", "No comments yet – upload your first video!")));
                return;
            }
            long avg = s.videos.Count > 0 ? System.Math.Max(1L, s.totalViews / s.videos.Count) : 1L;
            int shown = 0;
            for (int i = s.videos.Count - 1; i >= 0 && shown < CommentVideos; i--)
            {
                var v = s.videos[i];
                if (v == null) continue;
                shown++;
                var card = PhoneKit.Card(14f);
                card.name = "studio-comments-" + i;
                var head = UIKit.Row();
                head.style.alignItems = Align.FlexStart;
                head.Add(VideoThumb(v, 112f, 63f));
                var hc = UIKit.Col();
                hc.style.marginLeft = 10f;
                hc.style.flexGrow = 1;
                hc.style.flexShrink = 1;
                var t = PhoneKit.T(v.title, PhoneKit.Tiny, UIPalette.Ink, true);
                t.style.maxHeight = 46f;
                t.style.overflow = Overflow.Hidden;
                hc.Add(t);
                var nums = UIKit.Row();
                nums.style.marginTop = 4f;
                nums.Add(new UIGlyph(UIGlyphs.Eye, UIPalette.InkSoft, 18f));
                nums.Add(UIKit.Text(" " + UIKit.Compact(v.views) + "  ", 16f, UIPalette.InkSoft, true));
                nums.Add(new UIGlyph(UIGlyphs.StudioComment, UIPalette.InkSoft, 18f));
                nums.Add(UIKit.Text(" " + UIKit.Compact(StudioComments.Count(v)), 16f, UIPalette.InkSoft, true));
                hc.Add(nums);
                head.Add(hc);
                card.Add(head);

                StudioComments.Make(v, s, i, avg, commentBuf);
                for (int k = 0; k < commentBuf.Count; k++) card.Add(CommentRow(commentBuf[k], k == 0));
                body.Add(card);
            }
        }

        static readonly Color[] AvatarColors =
        {
            UIPalette.Rgb(0x3FC1C9), UIPalette.Rgb(0xFF7A5C), UIPalette.Rgb(0x8E6CD9), UIPalette.Rgb(0x4FA3E0),
            UIPalette.Rgb(0x2F9A4E), UIPalette.Rgb(0xE8833A), UIPalette.Rgb(0xD9686E), UIPalette.Rgb(0xB8861B),
        };

        static VisualElement CommentRow(StudioCommentLine c, bool pinned)
        {
            var row = UIKit.Row();
            row.AddToClassList("studio-comment");
            row.style.alignItems = Align.FlexStart;
            row.style.marginTop = 10f;
            var av = UIKit.Box();
            UIKit.Size(av, 40f, 40f);
            UIKit.Radius(av, 20f);
            UIKit.Center(av);
            av.style.flexShrink = 0;
            av.style.backgroundColor = AvatarColors[Mathf.Abs(c.color) % AvatarColors.Length];
            string initial = string.IsNullOrEmpty(c.user) ? "?" : char.ToUpperInvariant(c.user[0]).ToString();
            av.Add(UIKit.Text(initial, PhoneKit.Small, Color.white, true));
            row.Add(av);
            var col = UIKit.Col();
            col.style.marginLeft = 10f;
            col.style.flexGrow = 1;
            col.style.flexShrink = 1;
            var who = UIKit.Row();
            who.Add(UIKit.Text("@" + c.user, 16f, UIPalette.InkSoft, true));
            if (pinned)
            {
                var pin = new UIGlyph(UIGlyphs.Pin, UIPalette.Danger, 16f);
                pin.style.marginLeft = 6f;
                who.Add(pin);
            }
            col.Add(who);
            col.Add(PhoneKit.T(c.text, PhoneKit.Small, UIPalette.Ink));
            row.Add(col);
            var likes = UIKit.Col();
            likes.style.alignItems = Align.Center;
            likes.style.marginLeft = 6f;
            likes.Add(new UIGlyph(UIGlyphs.Heart, UIPalette.Danger, 20f));
            likes.Add(UIKit.Text(UIKit.Compact(c.likes), 15f, UIPalette.InkSoft, true));
            row.Add(likes);
            return row;
        }
    }

    /// One made-up comment under a video.
    public struct StudioCommentLine
    {
        public string user, text;
        public long likes;
        public int color;
    }

    /// FinTube Studio comments (GDD §20 Q33): the top 3 comments of a video, made up DETERMINISTICALLY from the video
    /// (intro species, a second animal, sharks / Gerald, title style, views compared with the channel average, the
    /// day, the number of clips) with a seeded random – the same on every machine and after loading, no save data.
    public static class StudioComments
    {
        enum Cat : byte { Generic, Species, Second, Shark, Gerald, Clickbait, Honest, Spicy, Viral, Underrated, Many, First, Day }

        struct Tpl
        {
            public Cat cat;
            public string de, en;
        }

        static Tpl T(Cat c, string de, string en) => new Tpl { cat = c, de = de, en = en };

        // {s} = intro species, {s2} = another animal of the video, {d} = day, {n} = clips. No emoji (the font has none).
        static readonly Tpl[] Lines =
        {
            T(Cat.Generic, "Bester Kanal auf ganz FinTube!", "Best channel on all of FinTube!"),
            T(Cat.Generic, "Ich schau das zum Einschlafen. Im positiven Sinne.", "I watch this to fall asleep. In a good way."),
            T(Cat.Generic, "Wer schaut das auch heimlich im Unterricht?", "Who else is secretly watching this in class?"),
            T(Cat.Generic, "Das Wasser sieht so gut aus, ich brauch Urlaub", "The water looks so good, I need a holiday"),
            T(Cat.Generic, "Mehr davon!!!", "More of this!!!"),
            T(Cat.Generic, "Grüße an Horst", "Say hi to Horst"),
            T(Cat.Generic, "Ich hab jetzt Salzwasser im Bildschirm", "There's salt water in my screen now"),
            T(Cat.Generic, "Mein Goldfisch hat mitgeschaut und ist jetzt neidisch", "My goldfish watched too and is jealous now"),
            T(Cat.Species, "Das Intro mit {s} – Gänsehaut!", "That intro with the {s} – chills!"),
            T(Cat.Species, "{s} ist ab heute mein Lieblingstier", "The {s} is my favourite animal now"),
            T(Cat.Species, "Wer ist auch nur wegen {s} hier?", "Who else is only here for the {s}?"),
            T(Cat.Species, "{s} hat mehr Charisma als mein Chef", "The {s} has more charisma than my boss"),
            T(Cat.Species, "{s} schaut direkt in die Kamera. Ikone.", "The {s} looked right into the camera. Icon."),
            T(Cat.Species, "Ich wusste nicht, dass {s} so aussieht", "I didn't know a {s} looks like that"),
            T(Cat.Second, "{s2} hat mich komplett fertig gemacht", "The {s2} absolutely destroyed me"),
            T(Cat.Second, "Gerechtigkeit für {s2}! Viel zu kurz im Bild.", "Justice for the {s2}! Way too short on screen."),
            T(Cat.Shark, "Ich hätte mir in den Neoprenanzug gemacht", "I would have ruined my wetsuit"),
            T(Cat.Shark, "Der Hai war netter als meine Nachbarn", "The shark was nicer than my neighbours"),
            T(Cat.Gerald, "GERALD!!! <3", "GERALD!!! <3"),
            T(Cat.Gerald, "Gerald-Fanclub meldet sich zum Dienst", "Gerald fan club reporting for duty"),
            T(Cat.Clickbait, "Wo ist der Angriff? Ich warte immer noch.", "Where's the attack? Still waiting."),
            T(Cat.Clickbait, "Clickbait, aber ich hab trotzdem geklickt. Respekt.", "Clickbait, but I clicked anyway. Respect."),
            T(Cat.Clickbait, "Der Titel lügt, das Video nicht", "The title lies, the video doesn't"),
            T(Cat.Honest, "Endlich mal ein ehrlicher Titel", "Finally an honest title"),
            T(Cat.Honest, "Kein Clickbait, nur Fische. So muss das.", "No clickbait, just fish. That's the way."),
            T(Cat.Spicy, "WOW ist noch untertrieben", "WOW is an understatement"),
            T(Cat.Spicy, "SO nah dran, ich hab meinen Bildschirm abgewischt", "SO close, I wiped my screen"),
            T(Cat.Viral, "Hat mir meine Oma geschickt. Meine OMA.", "My grandma sent me this. My GRANDMA."),
            T(Cat.Viral, "Von der Startseite hierher, keine Reue", "Came from the front page, no regrets"),
            T(Cat.Viral, "Das geht viral, ich sag's euch", "This is going viral, mark my words"),
            T(Cat.Underrated, "Warum hat das so wenig Aufrufe?!", "Why does this have so few views?!"),
            T(Cat.Underrated, "Unterschätztes Video. Teilt das!", "Underrated video. Share it!"),
            T(Cat.Many, "{n} Tierarten in einem Video?! Wahnsinn", "{n} species in one video?! Insane"),
            T(Cat.First, "Ich war beim allerersten Video dabei!", "I was here for the very first video!"),
            T(Cat.First, "Erstes Video und schon so gut?", "First video and already this good?"),
            T(Cat.Day, "Tag {d} und es wird einfach nicht langweilig", "Day {d} and it never gets boring"),
            T(Cat.Day, "Seit Tag 1 dabei. Stolz auf dich!", "Here since day 1. Proud of you!"),
        };

        /// Shown comment count of a video (a share of its views).
        public static long Count(VideoRecord v) => v == null ? 0 : System.Math.Max(3L, v.views / 180);

        static int Fnv(string s)
        {
            unchecked
            {
                int h = (int)2166136261;
                if (s != null) for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619;
                return h;
            }
        }

        /// Fills `into` with the top 3 comments of video `index` (cleared first). avgViews = the channel's average.
        public static void Make(VideoRecord v, GameState s, int index, long avgViews, List<StudioCommentLine> into)
        {
            into.Clear();
            if (v == null) return;
            int seed;
            unchecked { seed = (s != null ? s.saveSeed : 0) * 486187739 + v.day * 7919 + Fnv(v.title) + (int)(v.views % 100003) * 31; }
            var rng = new System.Random(seed);

            // what the video is about
            var top = SpeciesDatabase.Get(v.topSpeciesId);
            SpeciesDef second = null;
            bool shark = top != null && top.plan == BodyPlan.Shark, gerald = v.topSpeciesId == "green_turtle";
            if (v.thumbSpecies != null)
                for (int i = 0; i < v.thumbSpecies.Length; i++)
                {
                    var sp = SpeciesDatabase.Get(v.thumbSpecies[i]);
                    if (sp == null) continue;
                    if (sp.plan == BodyPlan.Shark) shark = true;
                    if (sp.id == "green_turtle") gerald = true;
                    if (second == null && sp != top) second = sp;
                }
            float ratio = avgViews > 0 ? v.views / (float)avgViews : 1f;
            var style = (TitleStyle)Mathf.Clamp(v.titleStyle, 0, 2);

            // the first comment is about THIS video (the intro, a special animal, the title, the views), the others mix
            var specific = new List<int>(8);
            var all = new List<int>(Lines.Length);
            for (int i = 0; i < Lines.Length; i++)
            {
                var c = Lines[i].cat;
                bool ok;
                switch (c)
                {
                    case Cat.Species: ok = top != null; break;
                    case Cat.Second: ok = second != null; break;
                    case Cat.Shark: ok = shark; break;
                    case Cat.Gerald: ok = gerald; break;
                    case Cat.Clickbait: ok = style == TitleStyle.Clickbait; break;
                    case Cat.Honest: ok = style == TitleStyle.Honest; break;
                    case Cat.Spicy: ok = style == TitleStyle.Spicy; break;
                    case Cat.Viral: ok = ratio >= 1.3f; break;
                    case Cat.Underrated: ok = ratio <= 0.7f; break;
                    case Cat.Many: ok = v.clipCount >= 6; break;
                    case Cat.First: ok = index == 0; break;
                    case Cat.Day: ok = v.day >= 5; break;
                    default: ok = true; break;
                }
                if (!ok) continue;
                all.Add(i);
                if (c != Cat.Generic && c != Cat.Day) specific.Add(i);
            }

            var users = StreamChat.Users;
            long baseLikes = System.Math.Max(2L, v.views / 220);
            int used0 = -1, used1 = -1;
            int u0 = -1, u1 = -1;
            for (int k = 0; k < 3; k++)
            {
                List<int> from = k == 0 && specific.Count > 0 ? specific : all;
                int pick = -1;
                for (int tries = 0; tries < 12 && from.Count > 0; tries++)
                {
                    int cand = from[rng.Next(from.Count)];
                    if (cand == used0 || cand == used1) continue;
                    pick = cand;
                    break;
                }
                if (pick < 0) continue;
                if (k == 0) used0 = pick; else used1 = pick;
                int u = rng.Next(users.Length);
                for (int tries = 0; tries < 8 && (u == u0 || u == u1); tries++) u = rng.Next(users.Length);
                if (k == 0) u0 = u; else u1 = u;
                float f = k == 0 ? 1f : k == 1 ? 0.45f : 0.18f;
                long likes = System.Math.Max(1L, (long)(baseLikes * f * (0.8 + rng.NextDouble() * 0.4)));
                into.Add(new StudioCommentLine { user = users[u], text = Fill(Lines[pick], top, second, v), likes = likes, color = u });
            }
        }

        static string Fill(Tpl t, SpeciesDef top, SpeciesDef second, VideoRecord v)
        {
            string s = Loc.Tr(t.de, t.en);
            if (s.IndexOf('{') < 0) return s;
            return s.Replace("{s2}", second != null ? second.Name : "?")
                    .Replace("{s}", top != null ? top.Name : "?")
                    .Replace("{d}", v.day.ToString(Loc.Culture))
                    .Replace("{n}", v.clipCount.ToString(Loc.Culture));
        }
    }

    /// Small bar chart of the FinTube Studio statistics (Painter2D): one bar per day, the best day in the accent
    /// colour, negative days (followers lost) in coral below the zero line, and an optional hollow bar for today's
    /// video that is still processing.
    public class UIStudioChart : VisualElement
    {
        readonly long[] values;
        readonly Color color, accent;
        readonly int pending;

        public UIStudioChart(long[] values, Color color, Color accent, int pendingIndex = -1)
        {
            this.values = values ?? new long[0];
            this.color = color;
            this.accent = accent;
            pending = pendingIndex;
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public int Count => values.Length;

        static void Bar(Painter2D p, float x0, float y0, float x1, float y1, float rad)
        {
            rad = Mathf.Min(rad, (x1 - x0) * 0.5f, (y1 - y0) * 0.5f);
            p.BeginPath();
            p.MoveTo(new Vector2(x0, y1));
            p.LineTo(new Vector2(x0, y0 + rad));
            p.ArcTo(new Vector2(x0, y0), new Vector2(x0 + rad, y0), rad);
            p.LineTo(new Vector2(x1 - rad, y0));
            p.ArcTo(new Vector2(x1, y0), new Vector2(x1, y0 + rad), rad);
            p.LineTo(new Vector2(x1, y1));
            p.ClosePath();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 8f || r.height < 8f) return;
            var p = ctx.painter2D;
            float left = r.x + 2f, right = r.xMax - 2f, top = r.y + 4f, bottom = r.yMax - 2f;
            long max = 0, min = 0;
            int best = -1;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] > max) { max = values[i]; best = i; }
                if (values[i] < min) min = values[i];
            }
            float range = Mathf.Max(1f, max - min);
            float zeroY = Mathf.Lerp(bottom, top, (0f - min) / range);

            p.strokeColor = UIPalette.Line;
            p.lineWidth = 2f;
            for (int i = 1; i <= 3; i++)
            {
                float y = Mathf.Lerp(zeroY, top, i / 3f);
                p.BeginPath(); p.MoveTo(new Vector2(left, y)); p.LineTo(new Vector2(right, y)); p.Stroke();
            }
            p.strokeColor = UIPalette.A(UIPalette.Ink, 0.25f);
            p.BeginPath(); p.MoveTo(new Vector2(left, zeroY)); p.LineTo(new Vector2(right, zeroY)); p.Stroke();

            int n = values.Length;
            if (n == 0) return;
            float slot = (right - left) / n;
            float bw = Mathf.Max(3f, slot * 0.62f);
            for (int i = 0; i < n; i++)
            {
                float cx = left + slot * (i + 0.5f);
                long v = values[i];
                if (i == pending && v == 0)
                {
                    float py = Mathf.Lerp(zeroY, top, 0.35f);
                    p.strokeColor = UIPalette.A(accent, 0.7f);
                    p.lineWidth = 3f;
                    Bar(p, cx - bw * 0.5f, py, cx + bw * 0.5f, zeroY - 1f, Mathf.Min(bw * 0.3f, 6f));
                    p.Stroke();
                    continue;
                }
                if (v == 0)
                {
                    p.fillColor = UIPalette.A(color, 0.35f);
                    p.BeginPath(); p.Arc(new Vector2(cx, zeroY - 3f), 3f, Angle.Degrees(0f), Angle.Degrees(360f)); p.Fill();
                    continue;
                }
                float y = Mathf.Lerp(bottom, top, (v - min) / range);
                float y0 = Mathf.Min(y, zeroY), y1 = Mathf.Max(y, zeroY);
                if (y1 - y0 < 3f) { if (v > 0) y0 = y1 - 3f; else y1 = y0 + 3f; }
                p.fillColor = v < 0 ? UIPalette.Coral : (i == best ? accent : color);
                Bar(p, cx - bw * 0.5f, y0, cx + bw * 0.5f, y1, Mathf.Min(bw * 0.3f, 6f));
                p.Fill();
            }
        }
    }
}
