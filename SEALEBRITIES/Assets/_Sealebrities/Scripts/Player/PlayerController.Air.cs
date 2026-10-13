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
        /// The faint fade is running (IsFainting = fainting || unconscious).
        bool fainting;

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
                if (IsTowing) rate *= NetRescue.TowerAirMultiplier;   // F.1: towing a friend costs air
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
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Luft knapp", "Low air"), Loc.Tr("Auftauchen!", "Surface!"));
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
                    GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Flasche leer", "Tank empty"), Loc.Tr("Hütte · Steg · Boot", "Hut · Dock · Boat"));
                }
            }

            // E.4: a tank refills only at an air station – hut zone, dock, the boat with the compressor, the landmark
            // islands' old stations; a snorkeler (no tank) simply breathes again anywhere
            if (!isInWater)
            {
                outOfWaterTimer += dt;
                if (outOfWaterTimer >= RefillDelay && airLeft < airCapacity)
                {
                    float secondsToFull = RefillSeconds;
                    if (gear.hasTank && !InRefillZone(out secondsToFull))
                    {
                        refilling = false;
                        if (!noStationWarned && Air01 < 0.95f)
                        {
                            noStationWarned = true;
                            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Keine Luftstation", "No air station"), Loc.Tr("Hütte · Steg · Boot", "Hut · Dock · Boat"));
                        }
                    }
                    else
                    {
                        if (!refilling) { refilling = true; refillFrom = Air01; }
                        airLeft = Mathf.Min(airCapacity, airLeft + airCapacity / Mathf.Max(0.5f, secondsToFull) * dt);
                        if (airLeft >= airCapacity)
                        {
                            refilling = false;
                            lowAirWarned = false;
                            emptyWarned = false;
                            if (gear.hasTank && refillFrom < 0.7f) GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Flasche voll", "Tank full"));
                        }
                    }
                }
                else refilling = false;
            }
            else
            {
                outOfWaterTimer = 0f;
                refilling = false;
                noStationWarned = false;   // one warning per dive
            }

            // out of air below the surface: faint (or the rescue drone on the relaxed difficulty)
            if (isUnderwater)
            {
                if (!diff.canFaint)
                {
                    if (Air01 <= 0.08f) StartDroneRescue();
                }
                else if (airLeft <= 0f)
                {
                    // F.1: in a session a friend can tow you up before the faint
                    if (CanGoUnconscious()) StartUnconscious();
                    else StartFaint();
                }
            }
        }

        bool noStationWarned;

        /// E.4 refill zones for a tank: hut zone (≤ 18 m from the hut), the dock (deck or ≤ 4 m from the dock line),
        /// the boat deck with the crew's compressor (6 s to full), next to an old air station (≤ 3 m, 4 s).
        bool InRefillZone(out float secondsToFull)
        {
            secondsToFull = RefillSeconds;
            Vector3 p = transform.position;
            Vector3 hut = WorldLayout.Hut;
            float hx = p.x - hut.x, hz = p.z - hut.z;
            if (hx * hx + hz * hz <= 18f * 18f) return true;
            Vector2 a = new Vector2(WorldLayout.DockStart.x, WorldLayout.DockStart.z), b = new Vector2(WorldLayout.DockEnd.x, WorldLayout.DockEnd.z);
            Vector2 q = new Vector2(p.x, p.z), d = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, d) / Mathf.Max(1e-4f, d.sqrMagnitude));
            if ((q - (a + d * t)).sqrMagnitude <= 4f * 4f) return true;
            if (BoatAirStation.LocalCanRefill)
            {
                secondsToFull = BoatAirStation.SecondsToFull;
                return true;
            }
            var stations = PropInfo.AirStations;
            for (int i = 0; i < stations.Count; i++)
            {
                if ((stations[i] - p).sqrMagnitude <= 3f * 3f)
                {
                    secondsToFull = 4f;
                    return true;
                }
            }
            return false;
        }

        /// The local tank can refill where the player stands right now (E.4) – for the HUD / tests.
        public bool AtAirStation => !gear.hasTank || InRefillZone(out _);

        void OnBumped(float airLost)
        {
            if (!initialized || menuMode || IsFainting || rescueActive) return;
            // E.2 shark gummies: predator bumps cost less air
            airLeft = Mathf.Max(0f, airLeft - Mathf.Max(0f, airLost) * Buffs.Multiplier(BuffType.BumpResist));
            trauma = Mathf.Min(1f, trauma + 0.65f);
            if (isInWater && !seated) vel += Random.onUnitSphere * 1.4f;
        }

        // ------------------------------------------------------------------ faint / rescue
        void StartFaint()
        {
            if (fainting) return;
            fainting = true;
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
                        GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Ohnmächtig!", "Blacked out!"), Loc.Tr("Zurück am Boot", "Back at the boat"));
                    }
                }
                finally
                {
                    fainting = false;
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
            GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Rettungsdrohne kommt", "Rescue drone coming"), Loc.Tr("Luft fast leer", "Air almost out"));
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
                        GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Zurück an Bord", "Back aboard"), Loc.Tr("Drohne", "Drone"));
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
