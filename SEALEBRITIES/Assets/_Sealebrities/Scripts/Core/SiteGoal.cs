using UnityEngine;

namespace Sealebrities
{
    /// Round 3 (GDD §19 „Neuer Tauchplatz wird nicht angekündigt, bevor die Anforderungen erfüllt sind“, §20 Q42): the
    /// next dive site that is still locked and what it needs – shown small at the top centre (UISiteGoal, e.g.
    /// „Wrack + Vulkaninsel: 2.500 Follower · 60 % Kelpwald“). The banner only comes when it is reached
    /// (Progression.CheckUnlocks). The volcano island opens together with the wreck (VolcanoGate), so the wreck's line
    /// names both. The story site (Fog Isle) stays a secret. Pure read of the shared state – every machine agrees.
    public static class SiteGoal
    {
        /// The next locked, non-story dive site (null = all open).
        public static DiveSite Next()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return null;
            var s = gm.State;
            var sites = DiveSites.All;
            for (int i = 0; i < sites.Count; i++)
                if (!sites[i].IsStorySite && !s.unlockedSites.Contains(sites[i].id)) return sites[i];
            return null;
        }

        /// The volcano island opens with this site (the wreck).
        public static bool OpensVolcano(DiveSite site) => site != null && site.id == "wreck" && !VolcanoGate.Open;

        /// Requirement parts of a locked site: followers (have / need) and the previous Sealog page (have / need, 0..1).
        public static void Parts(DiveSite site, out long followersHave, out long followersNeed, out float pageHave, out float pageNeed, out string pageName)
        {
            var gm = GameManager.I;
            followersHave = gm != null ? gm.State.followers : 0;
            followersNeed = site != null ? site.followersNeeded : 0;
            pageHave = 0f; pageNeed = 0f; pageName = null;
            if (site == null || string.IsNullOrEmpty(site.prevSiteId)) return;
            pageNeed = site.prevCompletionNeeded;
            pageHave = Journal.SiteCompletion(site.prevSiteId);
            var prev = DiveSites.Get(site.prevSiteId);
            int pi = SealogPages.IndexOf(site.prevSiteId);
            pageName = pi >= 0 ? SealogPages.All[pi].Name : prev != null ? prev.Name : site.prevSiteId;
        }

        /// Short name for the chip („Wrack“ instead of „Wrack „MS Clickbait““).
        public static string ShortName(DiveSite site)
        {
            if (site == null) return "";
            switch (site.id)
            {
                case "kelp_forest": return Loc.Tr("Kelpwald", "Kelp Forest");
                case "wreck": return Loc.Tr("Wrack", "Wreck");
                case "blue_drop": return Loc.Tr("Blauwasser", "Blue Water");
                case "deep_trench": return Loc.Tr("Tiefseegraben", "Deep Trench");
                default: return site.Name;
            }
        }

        /// One plain line („Wrack + Vulkaninsel: 2.500 Follower + 60 % Sealog Kelpwald“) – tests, notifications.
        public static string Line(DiveSite site)
        {
            if (site == null) return null;
            Parts(site, out _, out long fNeed, out _, out float pNeed, out string page);
            string head = ShortName(site) + (OpensVolcano(site) ? Loc.Tr(" + Vulkaninsel", " + Volcano Isle") : "") + ": ";
            string f = fNeed > 0 ? Loc.Number(fNeed) + Loc.Tr(" Follower", " followers") : null;
            string p = pNeed > 0f ? Loc.Percent(pNeed) + Loc.Tr(" Sealog ", " Sealog ") + page : null;
            return head + (f != null && p != null ? f + " + " + p : f ?? p ?? "");
        }
    }
}
