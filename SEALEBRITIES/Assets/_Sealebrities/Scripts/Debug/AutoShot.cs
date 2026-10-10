using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// Automated screenshot tour for build verification. Does nothing unless the game is started with
    ///     SEALEBRITIES.exe -autoshot "C:/some/output/folder"
    /// It disables saving, waits for the boot, starts an Open Water game with good test gear and captures
    /// 01_menu.png … 12_night.png (menu, island, hut/dock/boat, Sealog, reef, kelp, wreck, drop-off, trench, night),
    /// writes autoshot_log.txt and quits. Every shot is guarded: a failing shot is logged and the tour continues.
    public sealed class AutoShot : MonoBehaviour
    {
        const float BootTimeout = 900f;

        string outDir;
        readonly StringBuilder log = new StringBuilder();
        readonly System.Random rng = new System.Random(4711);
        PlayerController pc;
        PlayerFlashlight lamp;
        Camera cam;
        int saved;

        // ------------------------------------------------------------------ activation (command line only)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Activate()
        {
            string dir = ParseOutputDir(Environment.GetCommandLineArgs());
            if (dir == null) return;
            GameManager.SavingDisabled = true;   // never touch the player's save
            var go = new GameObject("SB_AutoShot");
            DontDestroyOnLoad(go);
            go.AddComponent<AutoShot>().outDir = dir;
        }

        static string ParseOutputDir(string[] args)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], "-autoshot", StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]) && !args[i + 1].StartsWith("-"))
                    return args[i + 1].Trim().Trim('"');
                Debug.LogError("AutoShot: -autoshot needs an output folder, e.g. -autoshot C:/shots");
                return null;
            }
            return null;
        }

        void Start() { StartCoroutine(Run()); }

        // ------------------------------------------------------------------ the tour
        IEnumerator Run()
        {
            try { Directory.CreateDirectory(outDir); }
            catch (Exception e)
            {
                Debug.LogError("AutoShot: cannot create output folder " + outDir + ": " + e.Message);
                Application.Quit(2);
                yield break;
            }
            Note("AutoShot started, output " + outDir);

            float waited = 0f;
            while (!BootState.Ready)
            {
                waited += Time.unscaledDeltaTime;
                if (waited > BootTimeout)
                {
                    Note("ERROR: boot did not finish within " + BootTimeout + " s");
                    Finish(3);
                    yield break;
                }
                yield return null;
            }
            Note("boot ready after " + BootState.BootSeconds.ToString("0.0") + " s" + (BootState.HadErrors ? " (boot had errors)" : ""));

            // 01 – main menu over the island
            yield return Wait(3f);
            yield return Capture("01_menu");

            // start a game with test gear and a partly filled journal
            yield return Guard(StartGame(), "start game");

            yield return Guard(ShotIslandAir(), "02_island_air");
            yield return Guard(ShotHutDock(), "03_hut_dock_boat");
            yield return Guard(ShotPhone(), "04/05 phone");
            yield return Guard(ShotReef(), "06/07 reef");
            yield return Guard(ShotKelp(), "08_kelp");
            yield return Guard(ShotWreck(), "09_wreck");
            yield return Guard(ShotBlueDrop(), "10_blue_drop");
            yield return Guard(ShotTrench(), "11_trench_night");
            yield return Guard(ShotNightIsland(), "12_night");

            Finish(saved >= 12 ? 0 : 1);
        }

        IEnumerator StartGame()
        {
            var gm = GameManager.I;
            if (gm == null) throw new InvalidOperationException("no GameManager");
            gm.NewGame(Difficulty.OpenWater);
            yield return Frames(3);

            var s = gm.State;
            foreach (var id in new[] { "tank_t5", "lamp_t3", "cam_t2", "fins_t1", "boat_t1" })
                if (GearDatabase.Get(id) != null && !s.ownedItems.Contains(id)) s.ownedItems.Add(id);
            GameEvents.RaiseGearChanged();

            // a little progress so the HUD and the Sealog have something to show (no milestone banners)
            s.money = 1840;
            s.followers = 612;
            s.reachedMilestones.Add("m100");
            if (!s.unlockedSites.Contains("kelp_forest")) s.unlockedSites.Add("kelp_forest");
            GameEvents.RaiseMoneyChanged(s.money);
            GameEvents.RaiseFollowersChanged(s.followers);
            FillJournal();

            pc = Refs.Player as PlayerController;
            if (pc == null) throw new InvalidOperationException("no PlayerController registered");
            cam = pc.View;
            lamp = cam != null ? cam.GetComponentInChildren<PlayerFlashlight>(true) : null;
            pc.RefillAir();
            yield return Wait(1.5f);
        }

        void FillJournal()
        {
            var tiers = new[] { SizeTier.Legendary, SizeTier.Huge, SizeTier.Large, SizeTier.Normal, SizeTier.Large, SizeTier.Tiny, SizeTier.Huge, SizeTier.Normal };
            int k = 0;
            foreach (var id in new[] { "clownfish", "blue_tang", "moon_jelly", "parrotfish", "lionfish", "octopus", "green_turtle", "garibaldi" })
            {
                var sp = SpeciesDatabase.Get(id);
                if (sp == null) continue;
                var e = Journal.Get(id);
                var t = tiers[k++ % tiers.Length];
                e.discovered = true;
                e.firstDay = 1;
                e.recordCm = Progression.RollSizeCm(sp, t, rng);
                e.recordTier = (int)Progression.ClassifySize(sp, e.recordCm);
                e.timesFilmed = 1 + k;
                e.bestClipValue = sp.baseValue * Progression.SizeMultiplier((SizeTier)e.recordTier);
            }
        }

        // 02 – island overview from the air at golden hour
        IEnumerator ShotIslandAir()
        {
            SetTime(0.72f);
            yield return EnsureOnLand();
            FreeCamera(new Vector3(285f, 125f, -255f), new Vector3(10f, 2f, 25f));
            yield return Wait(6.5f);   // sky ambient refreshes every 6 s
            yield return Capture("02_island_air");
        }

        // 03 – hut, dock and boat at noon, seen from the beach
        IEnumerator ShotHutDock()
        {
            SetTime(0.5f);
            yield return EnsureOnLand();
            Vector3 beach = FindBeach(new Vector3(160f, 0f, -55f));
            Vector3 eye = new Vector3(beach.x, Mathf.Max(beach.y, Waves.SeaLevel) + 1.7f, beach.z);
            FreeCamera(eye, new Vector3(165f, 2.5f, 12f));
            yield return Wait(6.5f);
            yield return Capture("03_hut_dock_boat");
        }

        // 04/05 – the phone: Sealog index and a species page with its biggest specimen
        IEnumerator ShotPhone()
        {
            Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            yield return Stand(dock + new Vector3(-6f, 0.05f, 0f), 262f);
            if (!UIModule.DebugOpenPhone("sealog")) Note("WARNING: phone did not open");
            yield return Wait(1.5f);
            yield return Capture("04_sealog");
            UIModule.DebugOpenPhone("sealog:green_turtle");
            yield return Wait(1.2f);
            yield return Capture("05_sealog_record");
            UIModule.DebugOpenPhone("close");
            yield return Frames(3);
        }

        // 06/07 – house reef (clownfish, green turtle, octopus, blue tang, lionfish)
        IEnumerator ShotReef()
        {
            SetTime(0.5f);
            var site = DiveSites.Get("house_reef");
            Vector3 anchor;
            if (PropInfo.AnemoneSpots.Count > 0) anchor = PropInfo.AnemoneSpots[0];
            else anchor = FindSeafloor(site.center, site.radius * 0.8f, -10.5f, -5.5f);

            Vector3 dir = Flat(new Vector3(-1f, 0f, -0.4f));
            Vector3 eye = anchor - dir * 5.5f + Vector3.up * 2.2f;
            eye = KeepInWater(eye, 1.4f, 1.6f);
            Vector3 look = anchor + Vector3.up * 0.6f;
            yield return Dive(eye, look);
            ClearCrowd();

            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("clownfish", anchor + Vector3.up * 0.45f + r * 0.3f, SizeTier.Large);
            Spawn("clownfish", anchor + Vector3.up * 0.55f - r * 0.4f, SizeTier.Normal);
            Spawn("octopus", anchor - r * 1.6f + f * 0.5f + Vector3.up * 0.2f, SizeTier.Large);
            Spawn("lionfish", anchor + r * 1.9f + Vector3.up * 0.9f, SizeTier.Huge);
            Spawn("blue_tang", eye + f * 7f + r * 2.5f + Vector3.up * 0.8f, SizeTier.Large);
            Spawn("green_turtle", eye + f * 9f - r * 3f + Vector3.up * 1.2f, SizeTier.Huge);
            yield return Wait(1.2f);
            yield return Capture("06_reef");

            // second angle: wider, looking along the reef toward the turtle
            Vector3 eye2 = KeepInWater(eye - f * 3f + r * 4f + Vector3.up * 0.8f, 1.2f, 1.6f);
            Pose(eye2, eye + f * 8f - r * 2f);
            Spawn("green_turtle", eye2 + (eye + f * 8f - eye2).normalized * 6f, SizeTier.Legendary);
            yield return Wait(1.2f);
            yield return Capture("07_reef_wide");
        }

        // 08 – kelp forest with sea lion and leopard shark
        IEnumerator ShotKelp()
        {
            SetTime(0.45f);
            var site = DiveSites.Get("kelp_forest");
            Vector3 floor = FindSeafloor(site.center, site.radius * 0.7f, -20f, -12f);
            Vector3 eye = KeepInWater(floor + Vector3.up * 4f, 2.5f, 2f);
            Vector3 look = eye + Flat(new Vector3(0.6f, 0f, -1f)) * 10f + Vector3.down * 1.5f;
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("sea_lion", eye + f * 5f + r * 1.2f + Vector3.up * 0.5f, SizeTier.Large);
            Spawn("leopard_shark", eye + f * 8f - r * 2f + Vector3.down * 2.5f, SizeTier.Huge);
            Spawn("garibaldi", eye + f * 4f - r * 1.5f + Vector3.down * 1f, SizeTier.Large);
            yield return Wait(1.2f);
            yield return Capture("08_kelp");
        }

        // 09 – wreck with moray, hammerhead and barracuda
        IEnumerator ShotWreck()
        {
            SetTime(0.55f);
            var site = DiveSites.Get("wreck");
            // first swim above the wreck so its decor and colliders are active (sites activate near the camera)
            Vector3 c0 = PropInfo.WreckCenter != Vector3.zero ? PropInfo.WreckCenter : Ground(site.center);
            yield return Dive(KeepInWater(c0 + Vector3.up * 16f, 3f, 2f), c0);

            Vector3 eye = Vector3.zero, look = Vector3.zero, moray = Vector3.zero;
            bool framed = false;

            // preferred: a hull breach in the side of the ship (moray home), seen from open water
            int best = -1;
            for (int i = 0; i < PropInfo.WreckHoles.Count && i < PropInfo.WreckHoleNormals.Count; i++)
            {
                if (Mathf.Abs(PropInfo.WreckHoleNormals[i].y) > 0.6f) continue;           // skip hatches/deck openings
                if (best < 0 || PropInfo.WreckHoles[i].y > PropInfo.WreckHoles[best].y) best = i;   // highest: best light
            }
            if (best >= 0)
            {
                Vector3 hole = PropInfo.WreckHoles[best];
                Vector3 n = Flat(PropInfo.WreckHoleNormals[best]);
                for (float dist = 8f; dist <= 20f && !framed; dist += 3f)
                {
                    Vector3 e = KeepInWater(hole + n * dist + Vector3.up * 2.5f, 3f, 2f);
                    if (IsClear(e)) { eye = e; framed = true; }
                }
                moray = hole + n * 0.6f;
                look = hole + Vector3.up * 1.2f;
            }
            if (!framed)
            {
                // fallback: circle the wreck center for an open spot that looks straight at the hull
                Vector3 c = c0;
                look = c + Vector3.up * 3f;
                for (int k = 0; k < 16 && !framed; k++)
                {
                    float a = k * Mathf.PI / 8f;
                    Vector3 e = KeepInWater(c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 24f + Vector3.up * 6f, 3f, 2f);
                    if (!IsClear(e)) continue;
                    if (Physics.Raycast(e, (look - e).normalized, out RaycastHit hit, 30f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                        && !IsTerrain(hit.collider))
                    {
                        eye = e;
                        moray = hit.point + hit.normal * 0.6f;
                        framed = true;
                    }
                }
                if (!framed)
                {
                    eye = KeepInWater(c + new Vector3(-20f, 7f, -12f), 3f, 2f);
                    moray = c + new Vector3(-6f, 1.5f, -4f);
                }
            }
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("moray", moray, SizeTier.Huge);
            Spawn("barracuda", eye + f * 5f - r * 2f + Vector3.up * 0.6f, SizeTier.Large);
            Spawn("hammerhead", eye + f * 10f + r * 3f + Vector3.up * 1.5f, SizeTier.Large);
            Spawn("grouper", eye + f * 6f + r * 1.5f - Vector3.up * 1f, SizeTier.Huge);
            yield return Wait(1.3f);
            yield return Capture("09_wreck");
        }

        // 10 – blue water drop-off with manta and whale shark
        IEnumerator ShotBlueDrop()
        {
            SetTime(0.5f);
            var site = DiveSites.Get("blue_drop");
            Vector3 c = Ground(site.center);
            Vector3 eye = new Vector3(c.x, -20f, c.z);
            eye = KeepInWater(eye, 4f, 2f);
            Vector3 look = eye + Flat(new Vector3(-1f, 0f, -0.3f)) * 12f + Vector3.down * 2f;
            yield return Dive(eye, look);
            ClearCrowd();
            Vector3 f = (look - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("manta", eye + f * 7f + r * 2f + Vector3.down * 1f, SizeTier.Huge);
            Spawn("whale_shark", eye + f * 20f - r * 5f + Vector3.down * 4f, SizeTier.Large);
            yield return Wait(1.3f);
            yield return Capture("10_blue_drop");
        }

        // 11 – deep trench at night with the flashlight on. Taken on the canyon rim (~85-95 m) so the best test tank
        // (Trimix, 100 m) is not "too deep" – the canyon floor at ~150 m needs the submarine.
        IEnumerator ShotTrench()
        {
            SetTime(0.95f);
            var site = DiveSites.Get("deep_trench");
            Vector3 rim = FindSeafloor(site.center, site.radius, -96f, -82f);
            Vector3 eye = KeepInWater(rim + Vector3.up * 3.5f, 2.5f, 3f);
            Vector3 toCenter = Flat(site.center - rim);
            Vector3 target = eye + toCenter * 8f + Vector3.down * 2.5f;
            yield return Dive(eye, target);
            if (lamp != null) lamp.SetOn(true);
            else Note("WARNING: no flashlight component found");
            ClearCrowd();
            Vector3 f = (target - eye).normalized, r = Vector3.Cross(Vector3.up, f).normalized;
            Spawn("anglerfish", eye + f * 3.2f + r * 0.6f, SizeTier.Huge);
            Spawn("vampire_squid", eye + f * 5f - r * 1.6f + Vector3.up * 0.8f, SizeTier.Large);
            yield return Wait(6.5f);
            yield return Capture("11_trench_night");
            if (lamp != null) lamp.SetOn(false);
        }

        // 12 – the island at night (hut lamp, dock lantern, torches, campfire, lighthouse beams)
        IEnumerator ShotNightIsland()
        {
            SetTime(0.95f);
            yield return EnsureOnLand();
            FreeCamera(new Vector3(240f, 16f, -45f), new Vector3(105f, 6f, 35f));
            yield return Wait(3f);
            yield return Capture("12_night");
        }

        // ------------------------------------------------------------------ player / camera helpers
        void SetTime(float t)
        {
            var gm = GameManager.I;
            if (gm != null) gm.State.timeOfDay = t;
        }

        /// Puts the (enabled) player back on land at the hut so the water state is "dry" before a free-camera shot.
        IEnumerator EnsureOnLand()
        {
            if (pc == null) yield break;
            if (pc.enabled && !pc.IsInWater && !pc.IsUnderwater) yield break;
            pc.enabled = true;
            Vector3 p = PlayerController.ResolveSpawn(out float yaw);
            pc.TeleportTo(p, yaw);
            yield return Frames(10);
        }

        /// The game window has keyboard focus during the tour: a stray Tab/P typed elsewhere opens the phone (and hides
        /// the camera model). Every non-phone shot starts with the phone closed.
        static void CloseStrayPhone()
        {
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
        }

        /// Free camera (player frozen where he stands): only for above-water views.
        void FreeCamera(Vector3 eye, Vector3 lookAt)
        {
            CloseStrayPhone();
            if (pc == null || cam == null) return;
            pc.enabled = false;
            Pose(eye, lookAt);
        }

        void Pose(Vector3 eye, Vector3 lookAt)
        {
            if (cam == null) return;
            Vector3 d = lookAt - eye;
            if (d.sqrMagnitude < 1e-4f) d = Vector3.forward;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(d.normalized, Vector3.up));
        }

        /// Player standing (controller active) at a position, looking along the yaw.
        IEnumerator Stand(Vector3 feet, float yaw)
        {
            if (pc == null) yield break;
            pc.enabled = true;
            pc.TeleportTo(feet, yaw);
            pc.RefillAir();
            yield return Wait(0.8f);
        }

        /// Teleports the diver so the eye is at the given point, lets the controller settle into dive mode
        /// (underwater state, depth), then freezes it and aims the camera.
        IEnumerator Dive(Vector3 eye, Vector3 lookAt)
        {
            CloseStrayPhone();
            if (pc == null) yield break;
            if (lamp != null) lamp.SetOn(false);
            pc.enabled = true;
            Vector3 d = lookAt - eye;
            float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            pc.TeleportTo(eye - Vector3.up * PlayerController.EyeHeight, yaw);
            pc.RefillAir();
            yield return Wait(0.6f);
            pc.RefillAir();
            pc.enabled = false;
            Pose(eye, lookAt);
            yield return Wait(1.2f);   // site decor activation, fog blend, fauna near the camera
        }

        void ClearCrowd()
        {
            var sp = FaunaModule.Spawner;
            if (sp != null && sp.ActiveCount > 110) sp.DespawnAll();
        }

        void Spawn(string id, Vector3 pos, SizeTier tier)
        {
            var spawner = Refs.Spawner;
            var s = SpeciesDatabase.Get(id);
            if (spawner == null || s == null) { Note("WARNING: cannot spawn " + id); return; }
            try
            {
                float cm = Progression.RollSizeCm(s, tier, rng);
                var c = spawner.Spawn(s, pos, cm, false);
                if (c == null) Note("WARNING: spawner refused " + id);
            }
            catch (Exception e)
            {
                Note("WARNING: spawning " + id + " threw " + e.Message);
            }
        }

        // ------------------------------------------------------------------ terrain helpers
        static Vector3 Ground(Vector3 p) => new Vector3(p.x, WorldInfo.GroundHeight(p), p.z);

        /// True for the ground: the low-poly mesh terrain chunks ("Chunk_x_z" under the "Terrain" root, see
        /// WorldTerrain.CreateChunk) or a legacy Unity TerrainCollider.
        static bool IsTerrain(Collider c)
        {
            if (c == null) return false;
            if (c is TerrainCollider) return true;
            if (!(c is MeshCollider)) return false;
            var t = c.transform;
            return t.name.StartsWith("Chunk_", StringComparison.Ordinal) && t.parent != null && t.parent.name == "Terrain";
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        /// Spiral search around a center for a seafloor point whose height lies in [minY, maxY].
        static Vector3 FindSeafloor(Vector3 center, float radius, float minY, float maxY)
        {
            for (float r = 0f; r <= radius; r += 6f)
            {
                int steps = r < 1f ? 1 : Mathf.Max(8, Mathf.RoundToInt(r / 4f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps + r * 0.13f;
                    var p = new Vector3(center.x + Mathf.Cos(a) * r, 0f, center.z + Mathf.Sin(a) * r);
                    float g = WorldInfo.GroundHeight(p);
                    if (g >= minY && g <= maxY) return new Vector3(p.x, g, p.z);
                }
            }
            return Ground(center);
        }

        /// Walks from a start point toward (or away from) the island center until it stands on dry beach.
        static Vector3 FindBeach(Vector3 start)
        {
            Vector3 p = start;
            Vector3 toCenter = Flat(-start);
            for (int i = 0; i < 40; i++)
            {
                float g = WorldInfo.GroundHeight(p);
                if (g >= 0.3f && g <= 3f) return new Vector3(p.x, g, p.z);
                p += (g < 0.3f ? toCenter : -toCenter) * 3f;
            }
            return Ground(start);
        }

        /// True when no collider (hull, rock, terrain) is within 1.5 m of the point.
        static bool IsClear(Vector3 p) =>
            !Physics.CheckSphere(p, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && p.y > WorldInfo.GroundHeight(p) + 1.5f;

        /// Keeps an underwater eye point at least `aboveFloor` above the seafloor and `belowSurface` under the surface.
        static Vector3 KeepInWater(Vector3 p, float aboveFloor, float belowSurface)
        {
            float g = WorldInfo.GroundHeight(p);
            float top = Waves.SeaLevel - belowSurface;
            p.y = Mathf.Max(p.y, g + aboveFloor);
            if (p.y > top) p.y = top;
            return p;
        }

        // ------------------------------------------------------------------ capture / timing
        IEnumerator Capture(string name)
        {
            yield return Frames(2);
            string path = Path.Combine(outDir, name + ".png");
            try
            {
                ScreenCapture.CaptureScreenshot(path);
                saved++;
                Note("captured " + name + ".png");
            }
            catch (Exception e)
            {
                Note("ERROR: capture " + name + " failed: " + e.Message);
            }
            yield return Frames(3);   // the file is written at the end of the capture frame
        }

        static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        /// Runs a shot coroutine; an exception is logged and the tour moves on.
        IEnumerator Guard(IEnumerator inner, string what)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(inner);
            while (stack.Count > 0)
            {
                var e = stack.Peek();
                object current;
                try
                {
                    if (!e.MoveNext()) { stack.Pop(); continue; }
                    current = e.Current;
                }
                catch (Exception ex)
                {
                    Note("ERROR in " + what + ": " + ex);
                    yield break;
                }
                if (current is IEnumerator nested && !(current is CustomYieldInstruction)) { stack.Push(nested); continue; }
                yield return current;
            }
        }

        void Note(string line)
        {
            Debug.Log("AutoShot: " + line);
            log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
        }

        void Finish(int code)
        {
            Note("done, " + saved + " screenshots");
            try { File.WriteAllText(Path.Combine(outDir, "autoshot_log.txt"), log.ToString()); }
            catch (Exception e) { Debug.LogWarning("AutoShot: could not write log: " + e.Message); }
            if (pc != null) pc.enabled = true;
            Application.Quit(code);
        }
    }
}
