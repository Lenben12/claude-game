using UnityEngine;

namespace Sealebrities
{
    /// Player answer (round 3): the volcano island becomes reachable together with the WRECK site (mid game, the same
    /// requirement as the wreck: Progression unlocks "wreck"). Until then a strong current ring
    /// (WorldLayout.VolcanoGateRadius around WorldLayout.VolcanoIsland) pushes boats back out (BoatController) and the
    /// requirement is shown as a short notice – nothing is announced before. The fauna step gates the volcano zone's
    /// spawns with Open as well. Shared state (unlockedSites) – identical on every co-op machine.
    public static class VolcanoGate
    {
        /// The dive site whose unlock opens the volcano.
        public const string RequiredSite = "wreck";

        /// True once the wreck site is unlocked (or there is no game, e.g. the menu).
        public static bool Open
        {
            get
            {
                var gm = GameManager.I;
                if (gm == null || gm.State == null) return true;
                return gm.State.unlockedSites.Contains(RequiredSite);
            }
        }

        /// Horizontal distance from the volcano island's centre.
        public static float Distance(Vector3 p)
        {
            float dx = p.x - WorldLayout.VolcanoIsland.x, dz = p.z - WorldLayout.VolcanoIsland.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool InsideRing(Vector3 p) => Distance(p) < WorldLayout.VolcanoGateRadius;

        /// The requirement line shown at the current ring (top centre notice): the wreck site's own requirement.
        public static string RequirementText()
        {
            var site = DiveSites.Get(RequiredSite);
            string reason = null;
            if (site != null && !Progression.SiteRequirementsMet(site, out reason)) { }
            string head = Loc.Tr("Vulkaninsel: erst mit dem Wrack erreichbar", "Volcano Isle: reachable with the wreck");
            return string.IsNullOrEmpty(reason) ? head : head + " · " + reason;
        }
    }
}
