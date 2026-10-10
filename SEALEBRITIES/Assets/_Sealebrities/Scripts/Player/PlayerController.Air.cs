using UnityEngine;

namespace Sealebrities
{
    /// Air supply, depth limits, predator bumps, fainting and the Snorkeler rescue drone.
    public partial class PlayerController
    {
        GearStats gear = DefaultGear();
        DifficultySettings diff = DifficultySettings.For(Difficulty.OpenWater);
        float airCapacity = 45f, airLeft = 45f;
        float outOfWaterTimer, notUnderTimer;
        bool tooDeepWarned, lowAirWarned, rescueActive, sprintingUnderwater;

        // fade safety net: if the UI's fader never calls back, run the action ourselves
        System.Action pendingFade;
        float pendingFadeDeadline;

        static GearStats DefaultGear() => new GearStats
        {
            airSeconds = 45f, maxDepth = 6f, swimSpeed = 1f, cameraQuality = 0.6f, tapeSeconds = 90f, maxZoom = 1.5f, boatSpeed = 1f
        };

        public void RefillAir()
        {
            airLeft = airCapacity;
            lowAirWarned = false;
        }

        void UpdateAir(float dt)
        {
            if (IsFainting || rescueActive) return;

            if (isUnderwater)
            {
                notUnderTimer = 0f;
                float rate = 1f + depth / 20f;
                if (sprintingUnderwater) rate *= 1.4f;
                if (depth > gear.maxDepth)
                {
                    rate *= 3f;
                    if (!tooDeepWarned)
                    {
                        tooDeepWarned = true;
                        GameEvents.RaiseBanner(Loc.Tr("Zu tief für deine Ausrüstung!", "Too deep for your gear!"), new Color(1f, 0.35f, 0.2f));
                        trauma = Mathf.Max(trauma, 0.25f);
                    }
                }
                airLeft = Mathf.Max(0f, airLeft - rate * dt);
                if (!lowAirWarned && airLeft > 0f && Air01 < 0.2f)
                {
                    lowAirWarned = true;
                    GameEvents.RaiseToast(Loc.Tr("Luft wird knapp – tauch auf!", "Air is running low – surface!"));
                }
            }
            else
            {
                notUnderTimer += dt;
                if (notUnderTimer > 3f) tooDeepWarned = false;     // a new dive may warn again
                // Snorkel set (no tank): you breathe through the snorkel at the surface, the breath-hold recovers.
                if (!gear.hasTank && isInWater && notUnderTimer > 0.8f)
                    airLeft = Mathf.Min(airCapacity, airLeft + airCapacity * 0.3f * dt);
                if (Air01 > 0.5f) lowAirWarned = false;
            }

            // tanks are only refilled on land / on the boat
            if (!isInWater)
            {
                outOfWaterTimer += dt;
                if (outOfWaterTimer >= 2f && airLeft < airCapacity) RefillAir();
            }
            else outOfWaterTimer = 0f;

            if (isInWater)
            {
                if (!diff.canFaint)
                {
                    if (Air01 <= 0.08f) StartDroneRescue();
                }
                else if (airLeft <= 0f) StartFaint();
            }
        }

        void OnBumped(float airLost)
        {
            if (!initialized || menuMode || IsFainting || rescueActive) return;
            airLeft = Mathf.Max(0f, airLeft - Mathf.Max(0f, airLost));
            trauma = Mathf.Min(1f, trauma + 0.65f);
            if (isInWater && !seated) vel += Random.onUnitSphere * 1.4f;
        }

        // ------------------------------------------------------------------ faint / rescue
        void StartFaint()
        {
            if (IsFainting) return;
            IsFainting = true;
            sprintingUnderwater = false;
            RunFade(() =>
            {
                try
                {
                    var gm = GameManager.I;
                    if (gm != null) gm.HandleFaint();     // loses the dive footage; permadeath returns to the menu
                    if (gm != null && gm.InGame && !menuMode)
                    {
                        GetRescuePoint(out Vector3 p, out float y);
                        pitch = 0f;
                        TeleportTo(p, y);
                        RefillAir();
                        GameEvents.RaiseToast(Loc.Tr("Ohnmächtig! Das Boot hat dich rausgezogen.", "You blacked out! The boat pulled you out."));
                    }
                }
                finally
                {
                    IsFainting = false;
                    faintAnim = 0f;
                    trauma = 0f;
                }
            }, 2.5f);
        }

        /// Snorkeler difficulty: nobody faints, a drone carries the diver back at 8 % air.
        void StartDroneRescue()
        {
            if (rescueActive) return;
            rescueActive = true;
            sprintingUnderwater = false;
            GameEvents.RaiseToast(Loc.Tr("Luft fast leer – die Rettungsdrohne holt dich!", "Almost out of air – the rescue drone is coming!"));
            RunFade(() =>
            {
                try
                {
                    var gm = GameManager.I;
                    if (gm != null && gm.InGame && !menuMode)
                    {
                        GetRescuePoint(out Vector3 p, out float y);
                        pitch = 0f;
                        TeleportTo(p, y);
                        RefillAir();
                        GameEvents.RaiseToast(Loc.Tr("Die Rettungsdrohne hat dich aufs Boot gebracht.", "The rescue drone carried you back to the boat."));
                    }
                }
                finally
                {
                    rescueActive = false;
                }
            }, 2f);
        }

        void GetRescuePoint(out Vector3 p, out float yawDeg)
        {
            var boat = Refs.Boat;
            if (Alive(boat))
            {
                p = boat.RescuePoint;
                Transform r = boat.Root;
                yawDeg = r != null ? r.eulerAngles.y : yaw;
                return;
            }
            p = WorldInfo.DockEndPosition;
            if (p == Vector3.zero)
            {
                p = WorldLayout.DockEnd;
                p.y = Mathf.Max(WorldInfo.GroundHeight(p), Waves.SeaLevel + 1.2f);
            }
            Vector3 toIsland = WorldLayout.IslandCenter - p;
            yawDeg = Mathf.Atan2(toIsland.x, toIsland.z) * Mathf.Rad2Deg;
        }

        void RunFade(System.Action atBlack, float duration)
        {
            bool done = false;
            System.Action once = null;
            once = () =>
            {
                if (done) return;
                done = true;
                if (ReferenceEquals(pendingFade, once)) pendingFade = null;
                atBlack();
            };
            pendingFade = once;
            pendingFadeDeadline = Time.unscaledTime + duration + 5f;
            Fader.FadeThrough(once, duration);
        }

        void CheckPendingFade()
        {
            if (pendingFade == null || Time.unscaledTime < pendingFadeDeadline) return;
            var a = pendingFade;
            pendingFade = null;
            a();
        }
    }
}
