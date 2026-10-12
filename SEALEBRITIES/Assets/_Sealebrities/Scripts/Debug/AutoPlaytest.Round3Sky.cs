using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-3 step 3 – sky + water (GDD §19 "realistischere Wolken", the water hides what is under it): the cloud
    /// layer exists with the cloud count of the weather, drifts with the wind, turns golden at sunset and grey + denser on
    /// a showery day; the sea ramp / view opacity rules (lagoon readable near the viewer, deep or far water opaque), the
    /// live ocean follows the eye height, and the above-water cull distances are on above and off under the surface.
    /// Runs inside RunRound3Steps (-r3only).
    public sealed partial class AutoPlaytest
    {
        const int R3TestCullLayer = 4;   // Unity's built-in, unused "Water" layer: only registered during this check

        static float R3Warmth(Color c) => c.r - c.b;

        IEnumerator StepR3SkyWater()
        {
            Weather.TestOverride = null;
            var dn = WorldBuilder.DayNight;
            var clouds = dn != null ? dn.Clouds : null;
            var ocean = WorldBuilder.Ocean;
            Check(clouds != null, "cloud layer built (WorldDayNight.Clouds)");
            Check(ocean != null, "ocean built");
            if (pc == null || cam == null || clouds == null) yield break;
            try
            {
                // ---- clouds on a fair day at noon, seen from the dock end
                pc.enabled = true;
                Vector3 dock = DockEnd();
                yield return Stand(SnapDown(dock + Vector3.up * 1f), 90f);
                pc.enabled = false;
                Vector3 eye = dock + Vector3.up * 1.65f;
                Pose(eye, eye + new Vector3(10f, 2.6f, 3f));
                Weather.TestOverride = WeatherKind.Fair;
                SetTime(0.5f);
                yield return Frames(3);
                clouds.Settle();
                yield return Wait(1f);
                int wantFair = WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Fair));
                int fairDrawn = clouds.VisibleCount;
                Check(clouds.WantedCount == wantFair && fairDrawn >= Mathf.Max(1, wantFair - 3) && fairDrawn <= wantFair,
                      "fair day: " + fairDrawn + " clouds drawn of " + clouds.WantedCount + " wanted (expected " + wantFair + ", cover " + F(clouds.Cover, "0.00") + ")");
                Check(WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Sunny)) < wantFair
                      && WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Showers)) > WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Cloudy)) - 1,
                      "cloud count follows the weather: sunny " + WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Sunny)) + ", fair " + wantFair
                      + ", cloudy " + WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Cloudy)) + ", showers " + WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Showers)));

                // ---- drift: a drawn cloud moves with the wind over 3 s (the camera stands still)
                int probe = -1;
                Vector3 p0 = Vector3.zero;
                for (int i = 0; i < WorldClouds.MaxClouds && probe < 0; i++) if (clouds.CloudInfo(i, out p0)) probe = i;
                Vector2 d0 = clouds.Drift;
                yield return Wait(3f);
                Vector3 p1 = Vector3.zero;
                bool still = probe >= 0 && clouds.CloudInfo(probe, out p1);
                float moved = still ? new Vector2(p1.x - p0.x, p1.z - p0.z).magnitude : 0f;
                Vector2 dd = clouds.Drift - d0;
                Check(probe >= 0 && moved > 1f && moved < 80f,
                      "clouds drift: cloud #" + probe + " moved " + F(moved) + " m in 3 s (wind " + F(clouds.WindSpeed) + " m/s, drift Δ " + F(dd.magnitude) + " m)");
                yield return Shot("62_clouds_noon");

                // ---- colour: white at noon, golden / coral at sunset
                Color noonTop = clouds.TopColor, noonBase = clouds.BottomColor;
                SetTime(0.742f);
                yield return Wait(1f);
                Color sunTop = clouds.TopColor, sunBase = clouds.BottomColor;
                Check(R3Warmth(sunTop) > R3Warmth(noonTop) + 0.08f && R3Warmth(noonTop) < 0.12f,
                      "cloud tops warm up at sunset: noon ≈ #" + ColorUtility.ToHtmlStringRGB(SkyGrade.Display(noonTop)) + ", sunset ≈ #"
                      + ColorUtility.ToHtmlStringRGB(SkyGrade.Display(sunTop)) + " (bases #" + ColorUtility.ToHtmlStringRGB(SkyGrade.Display(noonBase))
                      + " → #" + ColorUtility.ToHtmlStringRGB(SkyGrade.Display(sunBase)) + ")");
                Color dayBase = SkyGrade.Display(noonBase);
                Check(dayBase.r >= 0.8f && dayBase.g >= 0.85f && dayBase.b >= 0.9f, "day bases never darker than ~#DCE5F1 (on screen ≈ #" + ColorUtility.ToHtmlStringRGB(dayBase) + ")");
                Pose(eye, eye + new Vector3(10f, 2f, 3f));
                yield return Wait(1f);
                yield return Shot("62_clouds_sunset");

                // ---- a showery day: more clouds, greyer
                Weather.TestOverride = WeatherKind.Showers;
                SetTime(0.5f);
                yield return Frames(3);
                clouds.Settle();
                yield return Wait(1f);
                int wantRain = WorldClouds.CountFor(Weather.Cloudiness(WeatherKind.Showers));
                Check(clouds.WantedCount == wantRain && clouds.VisibleCount > fairDrawn && clouds.Grey > 0.8f,
                      "showers: " + clouds.VisibleCount + " clouds drawn of " + wantRain + " (fair " + fairDrawn + "), grey " + F(clouds.Grey, "0.00"));
                Color rainBase = SkyGrade.Display(clouds.BottomColor);
                Check(rainBase.r + rainBase.g + rainBase.b < dayBase.r + dayBase.g + dayBase.b - 0.15f,
                      "rain cloud bases are darker grey (≈ #" + ColorUtility.ToHtmlStringRGB(rainBase) + ")");
                yield return Shot("62_clouds_overcast");
                Weather.TestOverride = null;
                yield return Frames(3);
                clouds.Settle();

                // ---- water: straight-down ramp (shallow see-through, deep opaque) and the view rule
                float a1 = WorldWaterMap.RampAlpha(1f), a35 = WorldWaterMap.RampAlpha(3.5f), a6 = WorldWaterMap.RampAlpha(6f), a10 = WorldWaterMap.RampAlpha(10f);
                Check(a1 < a35 && a35 < a10 && a1 <= 0.7f && a6 >= 0.95f && a10 >= 0.97f,
                      "ramp alpha: 1 m " + F(a1, "0.00") + " < 3.5 m " + F(a35, "0.00") + " < 10 m " + F(a10, "0.000") + " (6 m " + F(a6, "0.00") + ")");
                float hDock = 3f;   // standing on the dock deck (1.3 m) / the boat deck: the eye is ~3 m above the sea
                Info("this run's dock eye: " + F(eye.y - Waves.SeaLevel) + " m above the sea");
                float nearDock = WorldOcean.ViewOpacity(2f, 3f, hDock), midDock = WorldOcean.ViewOpacity(2f, 15f, hDock), farDock = WorldOcean.ViewOpacity(2f, 40f, hDock);
                float deepNear = WorldOcean.ViewOpacity(15f, 5f, hDock);
                float beachNear = WorldOcean.ViewOpacity(0.5f, 3f, 1.8f);
                Check(nearDock < 0.85f && farDock >= 0.97f && nearDock < midDock && midDock <= farDock && deepNear >= 0.99f && beachNear < 0.75f,
                      "view opacity (eye " + F(hDock) + " m): 2 m lagoon at 3 m " + F(nearDock, "0.00") + ", 15 m " + F(midDock, "0.00") + ", 40 m " + F(farDock, "0.000")
                      + "; 15 m deep at 5 m " + F(deepNear, "0.000") + "; beach 0.5 m at 3 m (eye 1.8) " + F(beachNear, "0.00"));
                Info("from the air (eye 120 m): 2 m lagoon 150 m away " + F(WorldOcean.ViewOpacity(2f, 150f, 120f), "0.00") + " (the lagoon floor may still show from above)");

                // the live ocean follows the camera's eye height above the sea (refreshed on height change / 2.5 m moves)
                if (ocean != null)
                {
                    float want = Mathf.Clamp(cam.transform.position.y - Waves.SeaLevel, WorldOcean.MinEye, 5000f);
                    Check(!ocean.CameraUnderwater && Mathf.Abs(ocean.ViewEye - want) <= Mathf.Max(0.3f, want * 0.12f) + 0.05f,
                          "ocean view opacity uses the eye height: " + F(ocean.ViewEye, "0.00") + " m (camera " + F(want, "0.00") + " m)");
                }

                // ---- above-water cull distances: a registered layer is culled at ~22 m above water, not under it
                FXSurfaceCull.Register(R3TestCullLayer);
                yield return Frames(3);
                float[] dist = cam.layerCullDistances;
                Check(FXSurfaceCull.Active && FXSurfaceCull.CameraAboveWater && Mathf.Abs(dist[R3TestCullLayer] - FXSurfaceCull.AboveWaterMeters) < 0.01f && dist[0] == 0f,
                      "above water: layer " + R3TestCullLayer + " culled at " + F(dist[R3TestCullLayer]) + " m (Default layer untouched: " + F(dist[0]) + ")");
                Vector3 uwEye = new Vector3(dock.x + 10f, Waves.SeaLevel - 1.5f, dock.z + 6f);
                yield return DiveTo(uwEye, uwEye + new Vector3(6f, -0.6f, 0f));
                yield return Frames(3);
                dist = cam.layerCullDistances;
                Check(!FXSurfaceCull.Active && !FXSurfaceCull.CameraAboveWater && dist[R3TestCullLayer] == 0f && (ocean == null || ocean.CameraUnderwater),
                      "under water: cull distances restored (layer " + R3TestCullLayer + ": " + F(dist[R3TestCullLayer]) + "), the ocean shows its underside");
                FXSurfaceCull.Unregister(R3TestCullLayer);
                yield return Frames(2);
            }
            finally
            {
                Weather.TestOverride = null;
                FXSurfaceCull.Unregister(R3TestCullLayer);
            }

            // back on dry land at the dock
            pc.enabled = true;
            yield return Stand(SnapDown(DockEnd() + Vector3.up * 1f), 90f);
            clouds.Settle();
        }
    }
}
