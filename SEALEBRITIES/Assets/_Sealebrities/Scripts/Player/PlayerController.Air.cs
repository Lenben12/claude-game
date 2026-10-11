using UnityEngine;

namespace Sealebrities
{
    /// Air supply (core balance: breath-hold 20 s without a tank, tanks 90 s and up), depth limits, predator bumps,
    /// fainting and the rescue drone of the relaxed difficulty.
    /// Air is only used below the surface; consumption = Progression.AirRate(depth) (×1.4 sprinting, ×3 too deep).
    /// A tank is never refilled in the water: rest on land or on the boat and it fills up within ~5 s
    /// (RefillingAir). Without a tank the diver holds the breath and simply breathes again at the surface.
    public partial class PlayerController
    {
        const float RefillSeconds = 5f;      // empty → full while resting out of the water
        const float RefillDelay = 0.6f;      // seconds out of the water before refilling starts
        const float BreathRecoverSeconds = 6f;

        GearStats gear = DefaultGear();
        DifficultySettings diff = DifficultySettings.For(Difficulty.OpenWater);
        float airCapacity = Progression.BreathHoldSeconds, airLeft = Progression.BreathHoldSeconds;
        float outOfWaterTimer, notUnderTimer, refillFrom;
        bool tooDeepWarned, lowAirWarned, emptyWarned, rescueActive, sprintingUnderwater, refilling;

        // fade safety net: if the UI's fader never calls back, run the action ourselves
        System.Action pendingFade;
        float pendingFadeDeadline;

        static GearStats DefaultGear() => new GearStats
        {
            airSeconds = Progression.BreathHoldSeconds, maxDepth = 6f, swimSpeed = 1f, cameraQuality = 0.6f, tapeSeconds = 90f, maxZoom = 1.5f, boatSpeed = 1f,
            fishSpeed = 1f
        };

        public void RefillAir()
        {
            airLeft = airCapacity;
            lowAirWarned = false;
            emptyWarned = false;
            refilling = false;
        }

        void UpdateAir(float dt)
        {
            if (IsFainting || rescueActive) return;

            if (isUnderwater)
            {
                notUnderTimer = 0f;
                float rate = Progression.AirRate(depth);
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
                if (!lowAirWarned && airLeft > 0f && Air01 < 0.25f)
                {
                    lowAirWarned = true;
                    GameEvents.RaiseToast(Loc.Tr("Luft knapp – auftauchen!", "Low air – surface!"));
                }
            }
            else
            {
                notUnderTimer += dt;
                if (notUnderTimer > 3f) tooDeepWarned = false;     // a new dive may warn again
                // no tank: at the surface you simply breathe again (snorkel); a tank only refills out of the water
                if (!gear.hasTank && isInWater && notUnderTimer > 0.8f)
                    airLeft = Mathf.Min(airCapacity, airLeft + airCapacity / BreathRecoverSeconds * dt);
                if (Air01 > 0.5f) lowAirWarned = false;
                if (gear.hasTank && isInWater && airLeft <= 0.05f && !emptyWarned)
                {
                    emptyWarned = true;
                    GameEvents.RaiseToast(Loc.Tr("Flasche leer – an Land oder aufs Boot!", "Tank empty – back to land or the boat!"));
                }
            }

            // resting on land / on the boat deck refills the tank gradually
            if (!isInWater)
            {
                outOfWaterTimer += dt;
                if (outOfWaterTimer >= RefillDelay && airLeft < airCapacity)
                {
                    if (!refilling) { refilling = true; refillFrom = Air01; }
                    airLeft = Mathf.Min(airCapacity, airLeft + airCapacity / RefillSeconds * dt);
                    if (airLeft >= airCapacity)
                    {
                        refilling = false;
                        lowAirWarned = false;
                        emptyWarned = false;
                        if (gear.hasTank && refillFrom < 0.7f) GameEvents.RaiseToast(Loc.Tr("Flasche voll", "Tank full"));
                    }
                }
                else refilling = false;
            }
            else
            {
                outOfWaterTimer = 0f;
                refilling = false;
            }

            // out of air below the surface: faint (or the rescue drone on the relaxed difficulty)
            if (isUnderwater)
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
            Hands.StowAll();
            RunFade(() =>
            {
                try
                {
                    var gm = GameManager.I;
                    if (gm != null) gm.HandleFaint();     // loses the dive footage (rescue fee via the host in co-op)
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

        /// Relaxed difficulty: nobody faints, a drone carries the diver back at 8 % air.
        void StartDroneRescue()
        {
            if (rescueActive) return;
            rescueActive = true;
            sprintingUnderwater = false;
            Hands.StowAll();
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
                        GameEvents.RaiseToast(Loc.Tr("Die Drohne hat dich aufs Boot gebracht.", "The drone carried you back to the boat."));
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
