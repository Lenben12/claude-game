using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 UI screenshots (GDD §19 „FinTube-Studio und Handy“, §20 Q33/Q34) on the 3D phone. Files: r3u_*.png
    ///   r3u_studio_clips (clip list in the chosen order, intro on top, one clip left out), r3u_studio_stats (charts +
    ///   best videos), r3u_studio_comments (top comments per video), r3u_cookbook_locked (the curry before Topf's lesson,
    ///   only when the flag was not set yet), r3u_cookbook (curry learned, ingredients in the backpack).
    /// The main agent adds `yield return Guard(ShotsRound3UIStudio(), "round-3 ui studio shots");` to the tour.
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3UIStudio()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame || UIModule.Controller == null || UIModule.Controller.Phone == null) { Note("WARNING: r3u – no game / phone"); yield break; }
            var s = gm.State;
            SetTime(0.45f);
            yield return EnsureOnLand();
            if (pc != null) pc.enabled = true;
            Hands.StowAll();
            yield return Guard(ShotR3UIStudio(s), "r3u_studio");
            yield return Guard(ShotR3UICookbook(), "r3u_cookbook");
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
            if (pc != null) pc.enabled = true;
        }

        IEnumerator OpenPhoneR3u(string id)
        {
            if (!UIModule.DebugOpenPhone(id)) Note("WARNING: phone did not open (" + id + ")");
            float t = 0f;
            while (!PhoneDisplay.Raised && t < 2f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return Wait(1.2f);
        }

        IEnumerator ShotR3UIStudio(GameState s)
        {
            var app = UIModule.Controller.Phone.FinTube;
            // four fresh clips (a new video) + a few past days of channel history for the charts
            if (s.pendingUploads.Count > 0) Progression.ResolvePendingUploads();
            string[] ids = { "green_turtle", "parrotfish", "clownfish", "blue_tang" };
            var clips = new List<FootageClip>();
            for (int i = 0; i < ids.Length; i++)
                clips.Add(new FootageClip { speciesId = ids[i], sizeCm = 40f + i * 8f, tier = (int)(i == 0 ? SizeTier.Huge : SizeTier.Large), quality = 1.1f - i * 0.15f,
                    seconds = 6f, action = 0.5f, day = s.day, siteId = "house_reef", crowd = 2, depth = 6f, isNewSpecies = i == 3 });
            Footage.BankClips(clips);
            // the tour runs on day 1 – give the channel a week of history so the charts are not empty (showcase)
            if (s.day < 8)
            {
                s.day = 8;
                if (s.followerHistory.Count < 7)
                {
                    // a growing curve that ends just below today's followers (the header shows "+N heute")
                    s.followerHistory.Clear();
                    long now = Mathf.Max(80, (int)s.followers);
                    for (int d = 0; d < 7; d++) s.followerHistory.Add((long)(now * (0.2f + 0.68f * Mathf.Pow(d / 6f, 1.6f))));
                }
            }
            if (s.videos.Count < 5)
                for (int d = 5; d >= 1; d--)
                {
                    if (s.day - d < 1) continue;
                    long views = 600 + (d * 977) % 2400;
                    s.videos.Add(new VideoRecord { day = s.day - d, title = Loc.Tr("Tauchtag ", "Dive day ") + (s.day - d) + ": " + SpeciesDatabase.Get(ids[d % ids.Length]).Name,
                        views = views, followersGained = views / 55, money = views / 240, clipCount = 3 + d % 3, titleStyle = d % 3,
                        topSpeciesId = ids[d % ids.Length], thumbSpecies = new[] { ids[d % ids.Length], ids[(d + 1) % ids.Length] } });
                    s.totalViews += views;
                }
            s.videos.Sort((a, b) => a.day.CompareTo(b.day));

            yield return OpenPhoneR3u("fintube");
            app.DebugTab(UIAppFinTube.StudioTab.Video);
            yield return Wait(0.5f);
            // the new species becomes the intro, the weakest clip waits for tomorrow
            app.DebugMove("blue_tang", -1);
            app.DebugMove("blue_tang", -1);
            app.DebugMove("blue_tang", -1);
            app.DebugSelect("clownfish", false);
            yield return Wait(0.8f);
            yield return Capture("r3u_studio_clips");
            app.DebugTab(UIAppFinTube.StudioTab.Stats);
            yield return Wait(1f);
            yield return Capture("r3u_studio_stats");
            app.DebugTab(UIAppFinTube.StudioTab.Comments);
            yield return Wait(1f);
            yield return Capture("r3u_studio_comments");
            app.DebugTab(UIAppFinTube.StudioTab.Video);
            UIModule.DebugOpenPhone("close");
            yield return Frames(3);
        }

        IEnumerator ShotR3UICookbook()
        {
            var app = UIModule.Controller.Phone.Cookbook;
            StoryFlags.Set(StoryKeys.HermitCampFound);
            Backpack.Add(ItemIds.RawFish, 2, false);
            Backpack.Add(FoodDatabase.Coconut, 1, false);
            Backpack.Add("mango", 1, false);
            Backpack.Add(ItemIds.BottleSea, 1, false);
            yield return OpenPhoneR3u("cookbook");
            app.DebugFilter(UIAppCookbook.Filter.All);
            yield return Wait(0.6f);
            if (!StoryFlags.Has(NpcKeys.RecipeCurry))
            {
                yield return Capture("r3u_cookbook_locked");
                StoryFlags.Set(NpcKeys.RecipeCurry);
                yield return Wait(0.8f);
            }
            yield return Capture("r3u_cookbook");
            UIModule.DebugOpenPhone("close");
            yield return Frames(3);
        }
    }
}
