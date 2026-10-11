using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    public class DiveSite
    {
        public string id;
        public string nameDe, nameEn;
        public Vector3 center;            // y is ignored
        public float radius;
        public float minDepth, maxDepth;  // seafloor depth range of this site
        public string prevSiteId;         // journal page that must be filled first
        public float prevCompletionNeeded;
        public long followersNeeded;
        public Color mapColor;
        public string descDe, descEn;
        /// Round 2b (K.8): a site with a flag unlocks when that story flag is set (no Sealog/follower rule) – fog_isle.
        public string unlockFlag;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);
        /// True for sites that unlock through the story instead of the Sealog/follower progression.
        public bool IsStorySite => !string.IsNullOrEmpty(unlockFlag);
    }

    public static class DiveSites
    {
        public static readonly List<DiveSite> All = new List<DiveSite>
        {
            // L.5: the house reef moves with the 0.62 island (dock end → centre 64 m, 2.0–11.2 m deep)
            new DiveSite { id = "house_reef", nameDe = "Hausriff", nameEn = "House Reef", center = new Vector3(188f, 0f, 38f), radius = 105f,
                minDepth = 3f, maxDepth = 12f, prevSiteId = null, prevCompletionNeeded = 0f, followersNeeded = 0,
                mapColor = new Color(0.18f, 0.77f, 0.75f),
                descDe = "Flaches, buntes Riff direkt vor dem Steg. Gerald wohnt hier.", descEn = "Shallow, colorful reef right off the dock. Gerald lives here." },
            new DiveSite { id = "kelp_forest", nameDe = "Kelpwald", nameEn = "Kelp Forest", center = new Vector3(-340f, 0f, 300f), radius = 130f,
                minDepth = 10f, maxDepth = 25f, prevSiteId = "house_reef", prevCompletionNeeded = 0.6f, followersNeeded = 300,
                mapColor = new Color(0.45f, 0.62f, 0.2f),
                descDe = "Meterhohe Algenwälder mit Schatten zwischen den Stämmen.", descEn = "Towering algae forests with shadows between the stalks." },
            new DiveSite { id = "wreck", nameDe = "Wrack „MS Clickbait“", nameEn = "Wreck “MS Clickbait”", center = new Vector3(460f, 0f, -380f), radius = 110f,
                minDepth = 22f, maxDepth = 40f, prevSiteId = "kelp_forest", prevCompletionNeeded = 0.6f, followersNeeded = 2500,
                mapColor = new Color(0.71f, 0.33f, 0.18f),
                descDe = "Ein gesunkener Frachter. Muränen, Barrakudas – und Hammerhaie.", descEn = "A sunken freighter. Morays, barracudas – and hammerheads." },
            new DiveSite { id = "blue_drop", nameDe = "Blauwasser-Kante", nameEn = "Blue Water Drop-off", center = new Vector3(-620f, 0f, -540f), radius = 170f,
                minDepth = 30f, maxDepth = 75f, prevSiteId = "wreck", prevCompletionNeeded = 0.6f, followersNeeded = 15000,
                mapColor = new Color(0.12f, 0.35f, 0.7f),
                descDe = "Eine Steilwand ins endlose Blau. Mantas, Mondfische, Walhaie.", descEn = "A wall dropping into endless blue. Mantas, sunfish, whale sharks." },
            new DiveSite { id = "deep_trench", nameDe = "Tiefseegraben", nameEn = "Deep Trench", center = new Vector3(760f, 0f, 660f), radius = 190f,
                minDepth = 80f, maxDepth = 160f, prevSiteId = "blue_drop", prevCompletionNeeded = 0.6f, followersNeeded = 100000,
                mapColor = new Color(0.25f, 0.1f, 0.35f),
                descDe = "Kein Sonnenlicht mehr. Hier leben Dinge, die kaum jemand gefilmt hat.", descEn = "No more sunlight. Things live here that almost nobody has filmed." },
            // K.8: Toni's secret island – unlocked only by his map (StoryKeys.FogMap), behind a fog bank (WorldLayout.FogBankRadius)
            new DiveSite { id = "fog_isle", nameDe = "Nebelinsel", nameEn = "Fog Isle", center = new Vector3(-60f, 0f, 640f), radius = 85f,
                minDepth = 4f, maxDepth = 28f, prevSiteId = null, prevCompletionNeeded = 0f, followersNeeded = 0, unlockFlag = StoryKeys.FogMap,
                mapColor = new Color(0.949f, 0.549f, 0.745f),   // #F28CBE
                descDe = "Tonis Insel. Hier singen die Fische – und manche gibt's nur hier.", descEn = "Tony's island. The fish sing here – and some live nowhere else." },
        };

        public static DiveSite Get(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < All.Count; i++) if (All[i].id == id) return All[i];
            return null;
        }
    }
}
