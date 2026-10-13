using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round-3 UI step (GDD §19 „FinTube-Studio und Handy: mehr Funktionen“, §20 Q33 + Q34):
    ///   StepR3UIStudio   – FinTube Studio: pick + order the clips (the order reaches the upload: intro = top species,
    ///                      big collage tile; left-out clips stay banked), the co-op route NetCommand.Upload with the
    ///                      optional 6th argument (clip order) and the old 5-argument layout, statistics charts, best
    ///                      videos, the made-up comments. Shots 82_studio_clips / 82_studio_stats / 82_studio_comments.
    ///   StepR3UICookbook – the Kochbuch app: the hermits' curry locked until Topf taught it, have / missing counts from
    ///                      the backpack, the „Kann ich“ filter, where to cook, the book agrees with Survival.CookOne.
    ///                      Shots 83_cookbook_locked / 83_cookbook.
    /// The main agent wires them into the run list, e.g.
    ///     yield return RunStep("82 FinTube Studio (Q33): clips picked + ordered, the order reaches the upload, stats, comments", StepR3UIStudio, 120f);
    ///     yield return RunStep("83 Cookbook app (Q34): recipes from the real data, curry locked → learned, have / missing", StepR3UICookbook, 90f);
    public sealed partial class AutoPlaytest
    {
        static readonly string[] StudioTestSpecies = { "parrotfish", "green_turtle", "clownfish", "blue_tang" };

        static FootageClip R3uClip(GameState s, string id, float quality, SizeTier tier) => new FootageClip
        {
            speciesId = id, sizeCm = 40f, tier = (int)tier, quality = quality, seconds = 6f, action = 0.4f,
            day = s.day, siteId = "house_reef", crowd = 1, depth = 6f
        };

        static int R3uBankedOf(GameState s, string id)
        {
            int n = 0;
            for (int i = 0; i < s.bankedFootage.Count; i++) if (s.bankedFootage[i] != null && s.bankedFootage[i].speciesId == id) n++;
            return n;
        }

        static string R3uIds(List<FootageClip> l)
        {
            if (l == null) return "-";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < l.Count; i++) sb.Append(i > 0 ? "," : "").Append(l[i] != null ? l[i].speciesId : "null");
            return sb.ToString();
        }

        static string R3uIds(IReadOnlyList<string> l)
        {
            if (l == null) return "-";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < l.Count; i++) sb.Append(i > 0 ? "," : "").Append(l[i]);
            return sb.ToString();
        }

        // ================================================================== 82 FinTube Studio
        IEnumerator StepR3UIStudio()
        {
            var gm = GameManager.I;
            var s = gm != null ? gm.State : null;
            if (s == null || UIModule.Controller == null || UIModule.Controller.Phone == null) { Check(false, "no game / no phone"); yield break; }
            var app = UIModule.Controller.Phone.FinTube;
            // a fresh video: earlier steps' uploads are resolved (like the next morning), earlier banked clips put aside
            if (s.pendingUploads.Count > 0) Progression.ResolvePendingUploads();
            var keep = new List<FootageClip>(s.bankedFootage);
            s.bankedFootage.Clear();

            // three clips with different values (best first = the default order)
            var clips = new List<FootageClip>
            {
                R3uClip(s, StudioTestSpecies[0], 0.9f, SizeTier.Large),
                R3uClip(s, StudioTestSpecies[1], 1.2f, SizeTier.Huge),
                R3uClip(s, StudioTestSpecies[2], 0.5f, SizeTier.Normal),
            };
            Footage.BankClips(clips);
            var byValue = new List<FootageClip>(clips);
            byValue.Sort((a, b) => b.Value.CompareTo(a.Value));
            string best = byValue[0].speciesId, mid = byValue[1].speciesId, low = byValue[2].speciesId;
            Check(s.bankedFootage.Count == 3, "3 test clips banked (" + R3uIds(s.bankedFootage) + ")");

            UIModule.DebugOpenPhone("fintube");
            yield return Wait(1.2f);
            Check(UIState.PhoneOpen && UIModule.Controller.Phone.Current == app, "FinTube Studio open");
            if (app.CurrentTab != UIAppFinTube.StudioTab.Video) { app.DebugTab(UIAppFinTube.StudioTab.Video); yield return Wait(0.5f); }
            Check(app.ClipOrder.Count == 3 && app.ClipOrder[0] == best && app.ClipOrder[1] == mid && app.ClipOrder[2] == low,
                "default order = best clip first (" + R3uIds(app.ClipOrder) + ")");
            var root = app.DebugRoot;
            Check(root != null && root.Query(className: "studio-clip").ToList().Count == 3, "3 clip rows with ▲ ▼ ✕");
            Check(root != null && root.Q("studio-tabs") != null && root.Q("studio-streak") != null, "tabs + upload-streak flame in the channel header");

            // reorder: the weakest clip becomes the intro, the middle one is left out for later
            Check(app.DebugMove(low, -1) && app.DebugMove(low, -1), "▲ ▲ moves " + low + " to the top");
            Check(app.DebugSelect(mid, false), "✕ takes " + mid + " out of the video");
            yield return Wait(0.6f);
            root = app.DebugRoot;
            Check(app.ClipOrder.Count == 2 && app.ClipOrder[0] == low && app.ClipOrder[1] == best, "new order: " + R3uIds(app.ClipOrder) + " (intro " + low + ")");
            Check(root != null && root.Q("studio-clip-" + low) != null && root.Q("studio-out-" + mid) != null, "intro row + the left-out clip under „Nicht im Video“");
            Check(root != null && root.Q("studio-collage") != null, "collage thumbnail still there");
            Check(!app.DebugMove(low, -1), "the intro cannot move further up");
            yield return Shot("82_studio_clips");

            // upload: the order reaches the video
            int v0 = s.videos.Count;
            Check(app.DebugUpload(), "Hochladen pressed");
            var up = Progression.TodaysUpload(s);
            Check(up != null && up.clips != null && up.clips.Count == 2 && up.clips[0].speciesId == low && up.clips[1].speciesId == best,
                "the upload carries the order: " + (up != null ? R3uIds(up.clips) : "-"));
            Check(up != null && up.thumbSpecies != null && up.thumbSpecies.Length > 0 && up.thumbSpecies[0] == low, "the intro is the big collage tile (" + (up != null && up.thumbSpecies != null ? string.Join(",", up.thumbSpecies) : "-") + ")");
            Check(R3uBankedOf(s, mid) == 1 && R3uBankedOf(s, low) == 0 && R3uBankedOf(s, best) == 0, "the left-out clip stays banked, the used ones are gone (" + R3uIds(s.bankedFootage) + ")");
            yield return Wait(0.5f);

            // co-op route: a guest's NetCommand.Upload with the 6th argument (clip order) – a new intro joins today's video
            string extra = StudioTestSpecies[3];
            Footage.BankClips(new List<FootageClip> { R3uClip(s, extra, 0.8f, SizeTier.Large) });
            bool okCmd = CoreCommands.Execute(Net.LocalId, NetCommand.Upload, new[] { "0", "Gast-Titel", "", "0", "0.5", extra + "," + low + "," + best });
            up = Progression.TodaysUpload(s);
            Check(okCmd && up != null && up.clips.Count == 3 && up.clips[0].speciesId == extra && up.clips[1].speciesId == low && up.clips[2].speciesId == best,
                "host applied the guest's order (6th argument): " + (up != null ? R3uIds(up.clips) : "-"));
            Check(R3uBankedOf(s, mid) == 1 && R3uBankedOf(s, extra) == 0, "the guest's order only used its own species (" + R3uIds(s.bankedFootage) + ")");
            // the old 5-argument layout (older clients) still works: every banked clip, best first
            bool okOld = CoreCommands.Execute(Net.LocalId, NetCommand.Upload, new[] { "1", "Alter Client", "", "1", "0.4" });
            up = Progression.TodaysUpload(s);
            bool hasMid = false;
            if (up != null) for (int i = 0; i < up.clips.Count; i++) if (up.clips[i].speciesId == mid) hasMid = true;
            Check(okOld && hasMid && s.bankedFootage.Count == 0, "old 5-argument upload: the rest joins, bank empty (" + (up != null ? R3uIds(up.clips) : "-") + ")");

            // overnight: the video's top species is the intro of the stored order
            var orderNow = up != null ? up.clips[0].speciesId : null;
            Progression.ResolvePendingUploads();
            Check(s.videos.Count == v0 + 1 && s.videos[s.videos.Count - 1].topSpeciesId == orderNow && s.videos[s.videos.Count - 1].clipCount == 4,
                "resolved: video top species = intro " + orderNow + " (" + (s.videos.Count > 0 ? s.videos[s.videos.Count - 1].topSpeciesId : "-") + ")");

            // a few past days of channel history so the charts have something to draw (test game only)
            if (s.videos.Count < 4)
            {
                for (int d = 3; d >= 1; d--)
                {
                    if (s.day - d < 1) continue;
                    long views = 800 + d * 650;
                    s.videos.Insert(0, new VideoRecord { day = s.day - d, title = "Testvideo Tag " + (s.day - d), views = views, followersGained = views / 60, money = views / 250,
                        clipCount = 3, topSpeciesId = StudioTestSpecies[d % StudioTestSpecies.Length], thumbSpecies = new[] { StudioTestSpecies[d % StudioTestSpecies.Length], "clownfish" } });
                    s.totalViews += views;
                }
                s.videos.Sort((a, b) => a.day.CompareTo(b.day));
                Info("added past test videos for the charts (" + s.videos.Count + " videos)");
            }
            Check(UIAppFinTube.UploadStreak(s) >= 1, "upload streak " + UIAppFinTube.UploadStreak(s) + " day(s)");

            // statistics
            app.DebugTab(UIAppFinTube.StudioTab.Stats);
            yield return Wait(0.8f);
            root = app.DebugRoot;
            var vc = root != null ? root.Q<UIStudioChart>("studio-chart-views") : null;
            var fc = root != null ? root.Q<UIStudioChart>("studio-chart-followers") : null;
            Check(app.CurrentTab == UIAppFinTube.StudioTab.Stats && vc != null && vc.Count >= 1 && fc != null && fc.Count >= 1,
                "stats: views-per-day chart (" + (vc != null ? vc.Count : 0) + " days) + follower growth (" + (fc != null ? fc.Count : 0) + " days)");
            Check(root != null && root.Q("studio-numbers") != null && root.Query(className: "studio-best").ToList().Count >= 1, "stats: numbers + best videos (" + (root != null ? root.Query(className: "studio-best").ToList().Count : 0) + ")");
            yield return Shot("82_studio_stats");

            // comments
            app.DebugTab(UIAppFinTube.StudioTab.Comments);
            yield return Wait(0.8f);
            root = app.DebugRoot;
            var rows = root != null ? root.Query(className: "studio-comment").ToList() : new List<VisualElement>();
            Check(app.CurrentTab == UIAppFinTube.StudioTab.Comments && rows.Count >= 3, rows.Count + " comment rows");
            var a1 = new List<StudioCommentLine>();
            var a2 = new List<StudioCommentLine>();
            var last = s.videos[s.videos.Count - 1];
            StudioComments.Make(last, s, s.videos.Count - 1, 1000, a1);
            StudioComments.Make(last, s, s.videos.Count - 1, 1000, a2);
            bool same = a1.Count == 3 && a2.Count == 3;
            for (int i = 0; same && i < 3; i++) same = a1[i].text == a2[i].text && a1[i].user == a2[i].user && a1[i].likes == a2[i].likes && !string.IsNullOrEmpty(a1[i].text);
            Check(same, "comments are deterministic: \"" + (a1.Count > 0 ? a1[0].text : "-") + "\" (@" + (a1.Count > 0 ? a1[0].user : "-") + ")");
            yield return Shot("82_studio_comments");

            app.DebugTab(UIAppFinTube.StudioTab.Video);
            UIModule.DebugOpenPhone("close");
            yield return Frames(2);
            s.bankedFootage.AddRange(keep);
        }

        // ================================================================== 83 Cookbook
        IEnumerator StepR3UICookbook()
        {
            var gm = GameManager.I;
            var s = gm != null ? gm.State : null;
            if (s == null || UIModule.Controller == null || UIModule.Controller.Phone == null) { Check(false, "no game / no phone"); yield break; }
            var app = UIModule.Controller.Phone.Cookbook;
            Check(app != null && app.Id == "cookbook", "Kochbuch app registered");
            bool hadCurry = StoryFlags.Has(NpcKeys.RecipeCurry), hadCamp = StoryFlags.Has(StoryKeys.HermitCampFound);
            if (hadCurry) StoryFlags.Clear(NpcKeys.RecipeCurry);
            StoryFlags.Set(StoryKeys.HermitCampFound);   // the teacher's name + the hermits' fire show once the camp is known
            int fish0 = Backpack.Count(ItemIds.RawFish), coco0 = Backpack.Count(FoodDatabase.Coconut), curry0 = Backpack.Count(Survival.CurryId);
            int addFish = Backpack.Add(ItemIds.RawFish, 2, false);
            int addCoco = Backpack.Add(FoodDatabase.Coconut, 1, false);
            Check(Backpack.Count(ItemIds.RawFish) >= 1 && Backpack.Count(FoodDatabase.Coconut) >= 1, "raw fish ×" + Backpack.Count(ItemIds.RawFish) + " + coconut ×" + Backpack.Count(FoodDatabase.Coconut) + " in the backpack");

            UIModule.DebugOpenPhone("cookbook");
            yield return Wait(1.2f);
            Check(UIState.PhoneOpen && UIModule.Controller.Phone.Current == app, "Kochbuch open");
            if (app.CurrentFilter != UIAppCookbook.Filter.All) { app.DebugFilter(UIAppCookbook.Filter.All); yield return Wait(0.5f); }
            var root = app.DebugRoot;
            int cards = root != null ? root.Query(className: "cook-recipe").ToList().Count : 0;
            Check(cards == UIAppCookbook.Recipes.Length, cards + " recipe cards (grilled fish, curry, cooler fish on the grill, roast meat, water, coconut, mango, banana)");
            var curryR = UIAppCookbook.Find("curry");
            var curry = root != null ? root.Q("cook-curry") : null;
            Check(curry != null && curry.ClassListContains("cook-locked") && UIAppCookbook.IsLocked(curryR), "the hermits' curry is locked before Topf teaches it");
            Check(root != null && root.Q("cook-curry-teacher") != null && root.Q("cook-curry-lock") != null, "lock badge + who teaches it");
            // have / missing counts from the backpack
            Check(UIAppCookbook.HaveCount(curryR, 0) == Backpack.Count(ItemIds.RawFish) && UIAppCookbook.HaveCount(curryR, 1) == Backpack.Count(FoodDatabase.Coconut),
                "curry counts: fish " + UIAppCookbook.HaveCount(curryR, 0) + ", coconut " + UIAppCookbook.HaveCount(curryR, 1));
            var fishChip = root != null ? root.Q("cook-grilled_fish-ing-0") : null;
            Check(fishChip != null && fishChip.ClassListContains("cook-have"), "grilled fish: raw fish chip green");
            int meat = Backpack.Count(ItemIds.RawMeat);
            var meatChip = root != null ? root.Q("cook-grilled_meat-ing-0") : null;
            Check(meatChip != null && meatChip.ClassListContains(meat > 0 ? "cook-have" : "cook-missing"), "roast meat: raw meat " + meat + " → " + (meat > 0 ? "green" : "red missing"));
            Check(root != null && root.Q("cook-grilled_fish-place-1") != null && root.Q("cook-grilled_fish-place-2") != null && root.Q("cook-grilled_fish-place-4") != null
                  && root.Q("cook-fresh_water-place-8") != null, "where: campfire / grill / hermits' fire, water filter");
            yield return Shot("83_cookbook_locked");

            // Topf teaches it → unlocked (the app listens to the story flag)
            StoryFlags.Set(NpcKeys.RecipeCurry);
            yield return Wait(0.6f);
            root = app.DebugRoot;
            curry = root != null ? root.Q("cook-curry") : null;
            Check(curry != null && !curry.ClassListContains("cook-locked") && curry.ClassListContains("cook-ready") && !UIAppCookbook.IsLocked(curryR), "curry unlocked and ready after the lesson");

            app.DebugFilter(UIAppCookbook.Filter.CanCook);
            yield return Wait(0.5f);
            root = app.DebugRoot;
            Check(root != null && root.Q("cook-curry") != null && root.Q("cook-grilled_fish") != null && (meat > 0 || root.Q("cook-grilled_meat") == null),
                "„Kann ich“ shows only what is possible now (" + (root != null ? root.Query(className: "cook-recipe").ToList().Count : 0) + ")");
            app.DebugFilter(UIAppCookbook.Filter.All);
            yield return Wait(0.5f);
            yield return Shot("83_cookbook");

            // the book tells the truth: raw fish + coconut on the fire → curry (Survival.CookOne)
            if (Survival.FirstRaw() == ItemIds.RawFish)
            {
                int c0 = Backpack.Count(Survival.CurryId), co = Backpack.Count(FoodDatabase.Coconut);
                Survival.CookOne();
                Check(Backpack.Count(Survival.CurryId) == c0 + 1 && Backpack.Count(FoodDatabase.Coconut) == co - 1, "CookOne made the curry the book promised");
                yield return Wait(0.5f);
                Check(UIAppCookbook.HaveCount(curryR, 1) == Backpack.Count(FoodDatabase.Coconut), "counts follow the backpack (coconut " + UIAppCookbook.HaveCount(curryR, 1) + ")");
            }
            else Info("first raw item is " + Survival.FirstRaw() + " – CookOne check skipped");

            UIModule.DebugOpenPhone("close");
            yield return Frames(2);
            // restore the backpack + flags
            int extraCurry = Backpack.Count(Survival.CurryId) - curry0;
            if (extraCurry > 0) Backpack.Remove(Survival.CurryId, extraCurry);
            int fishNow = Backpack.Count(ItemIds.RawFish);
            if (fishNow > fish0) Backpack.Remove(ItemIds.RawFish, Mathf.Min(addFish, fishNow - fish0));
            int cocoNow = Backpack.Count(FoodDatabase.Coconut);
            if (cocoNow > coco0) Backpack.Remove(FoodDatabase.Coconut, Mathf.Min(addCoco, cocoNow - coco0));
            if (!hadCurry) StoryFlags.Clear(NpcKeys.RecipeCurry);
            if (!hadCamp) StoryFlags.Clear(StoryKeys.HermitCampFound);
        }
    }
}
