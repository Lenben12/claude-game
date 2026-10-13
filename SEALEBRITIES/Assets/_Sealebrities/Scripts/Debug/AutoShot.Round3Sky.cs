using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 screenshots – clouds (noon / sunset / overcast + the sun-side horizon glow), water opacity from
    /// the boat, the dock and the beach. Files: r3s_*.png. The weather is forced through Weather.TestOverride (Fair for
    /// the normal sky, Showers for the overcast one) and reset afterwards.
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3Sky()
        {
            var dn = WorldBuilder.DayNight;
            var clouds = dn != null ? dn.Clouds : null;
            if (clouds == null) Note("WARNING: no cloud layer (WorldDayNight.Clouds)");
            yield return Guard(ShotR3sClouds(clouds), "r3s_clouds");
            Weather.TestOverride = null;
            yield return Frames(2);
            if (clouds != null) clouds.Settle();
            yield return Guard(ShotR3sWater(), "r3s_water");
            if (pc != null) pc.enabled = true;
        }

        Vector3 R3sDockEye()
        {
            Vector3 end = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * PropInfo.DockDeckY;
            return end + Vector3.up * 1.6f;
        }

        static Vector3 R3sSunFlat(float timeOfDay)
        {
            WorldDayNight.SunPosition(timeOfDay, out float e, out float az);
            return Flat(WorldDayNight.Direction(e, az));
        }

        void R3sNoteClouds(WorldClouds c, string label)
        {
            if (c == null) return;
            Color top = SkyGrade.Display(c.TopColor), bot = SkyGrade.Display(c.BottomColor);
            Note("clouds " + label + ": weather " + Weather.Today + ", cover " + c.Cover.ToString("0.00") + ", grey " + c.Grey.ToString("0.00")
                 + ", drawn " + c.VisibleCount + " / wanted " + c.WantedCount + ", wind " + c.WindSpeed.ToString("0.0") + " m/s"
                 + ", lit top ≈ #" + ColorUtility.ToHtmlStringRGB(top) + ", base ≈ #" + ColorUtility.ToHtmlStringRGB(bot));
        }

        void R3sNoteWater(Vector3 eye, Vector3 lookAt, string label)
        {
            float h = Mathf.Max(eye.y - Waves.SeaLevel, WorldOcean.MinEye);
            Vector3 d = lookAt - eye;
            float t = d.y < -0.01f ? (Waves.SeaLevel - eye.y) / d.y : 30f;
            Vector3 hit = eye + d * Mathf.Clamp(t, 0f, 200f);
            float floor = Waves.SeaLevel - WorldInfo.GroundHeight(hit);
            float dist = new Vector2(hit.x - eye.x, hit.z - eye.z).magnitude;
            Note("water " + label + ": eye " + h.ToString("0.0") + " m above the sea, view centre " + dist.ToString("0") + " m away over "
                 + floor.ToString("0.0") + " m of water → opacity " + WorldOcean.ViewOpacity(floor, dist, h).ToString("0.00")
                 + " (2 m lagoon: 3 m " + WorldOcean.ViewOpacity(2f, 3f, h).ToString("0.00") + ", 10 m " + WorldOcean.ViewOpacity(2f, 10f, h).ToString("0.00")
                 + ", 30 m " + WorldOcean.ViewOpacity(2f, 30f, h).ToString("0.00") + "; 12 m deep at 10 m " + WorldOcean.ViewOpacity(12f, 10f, h).ToString("0.000") + ")");
        }

        IEnumerator ShotR3sClouds(WorldClouds clouds)
        {
            // noon, a fair day: from the beach north of the dock, looking out to sea and ~15° up
            Weather.TestOverride = WeatherKind.Fair;
            SetTime(0.5f);
            yield return EnsureOnLand();
            yield return Frames(2);
            if (clouds != null) clouds.Settle();
            Vector3 beach = FindBeach(new Vector3(104f, 0f, 36f));
            Vector3 eye = new Vector3(beach.x, Mathf.Max(beach.y, Waves.SeaLevel) + 1.7f, beach.z);
            Vector3 seaward = Flat(eye - WorldLayout.IslandCenter);
            Vector3 up15 = eye + seaward * 10f + Vector3.up * 2.7f;
            FreeCamera(eye, up15);
            yield return Wait(4f);
            R3sNoteClouds(clouds, "noon");
            yield return MeasureFps("r3s_clouds_noon");
            yield return Capture("r3s_clouds_noon");

            // sunset (sun ≈ 3.5°): from the dock end, away from the sun over the sea – peach-lit tops, lilac bases
            SetTime(0.742f);
            Vector3 dockEye = R3sDockEye();
            Vector3 toSun = R3sSunFlat(0.742f);
            FreeCamera(dockEye, dockEye - toSun * 10f + Vector3.up * 1.9f);
            yield return Wait(5f);
            R3sNoteClouds(clouds, "sunset");
            yield return Capture("r3s_clouds_sunset");
            // toward the sun: the warm horizon glow, the far stratus bands, the sun disc kept clear
            Pose(dockEye, dockEye + toSun * 10f + Vector3.up * 1.1f);
            yield return Wait(2f);
            yield return Capture("r3s_sunset_glow");

            // overcast (showers forced): more, bigger, greyer clouds at noon, same view as the noon shot
            Weather.TestOverride = WeatherKind.Showers;
            SetTime(0.5f);
            yield return Frames(2);
            if (clouds != null) clouds.Settle();
            FreeCamera(eye, up15);
            yield return Wait(4f);
            R3sNoteClouds(clouds, "overcast");
            yield return Capture("r3s_clouds_overcast");
            Weather.TestOverride = null;
        }

        IEnumerator ShotR3sWater()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();

            // dock end: down into the lagoon beside the dock and out over the sea – the floor shows near, not far
            Vector3 eye = R3sDockEye();
            Vector3 look = eye + new Vector3(9f, -3.4f, 4f);
            FreeCamera(eye, look);
            yield return Wait(3f);
            R3sNoteWater(eye, look, "from the dock");
            yield return Capture("r3s_water_from_dock");

            // boat: out over deep water east of the house reef, looking back across the reef toward the island
            var bc = Refs.Boat as BoatController;
            Vector3 boatPos = new Vector3(252f, 0f, 30f);
            if (bc != null)
            {
                bc.PlaceAt(boatPos, 270f);
                yield return Wait(1f);
                boatPos = bc.transform.position;
            }
            else Note("WARNING: no boat – the boat view is taken from a free camera");
            Vector3 beye = new Vector3(boatPos.x, Waves.SeaLevel + 2.7f, boatPos.z);
            Vector3 blook = new Vector3(boatPos.x - 45f, Waves.SeaLevel - 6f, boatPos.z - 3f);
            FreeCamera(beye, blook);
            yield return Wait(3f);
            Note("boat at " + boatPos.x.ToString("0") + "/" + boatPos.z.ToString("0") + " over " + (Waves.SeaLevel - WorldInfo.GroundHeight(boatPos)).ToString("0.0") + " m of water");
            R3sNoteWater(beye, blook, "from the boat");
            yield return Capture("r3s_water_from_boat");
            if (bc != null) bc.ReturnToMooring();

            // beach: standing at the waterline, the shallow turquoise lagoon (sand shows through near the feet)
            Vector3 b = FindBeach(WorldLayout.Isl3(160f, 0f, -55f));
            Vector3 leye = new Vector3(b.x, Mathf.Max(b.y, Waves.SeaLevel) + 1.7f, b.z);
            Vector3 sea = Flat(leye - WorldLayout.IslandCenter);
            Vector3 llook = leye + sea * 8f + Vector3.down * 2.6f;
            FreeCamera(leye, llook);
            yield return Wait(3f);
            R3sNoteWater(leye, llook, "from the beach");
            yield return Capture("r3s_lagoon_beach");
        }
    }
}
