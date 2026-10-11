using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// L.12 delivery mishaps – the rules, the drop zones and Wanda's lines. Balance constants live HERE (J.5).
    ///
    /// The HOST rolls every undropped delivery once (deterministic from HeliService.Hash + the arrival time) and stores
    /// the result in Delivery.mishap / dropZone (shared state, synced to the guests by the snapshot), at most one mishap
    /// per in-game day (GameState.lastMishapDay), never on day 1, never for the tutorial tank, express halves the chance.
    /// Every machine then flies its own helicopter to the same place. Shared crate progress (goat nudges, the kid paid,
    /// the crate shaken out of the palm / pulled off the roof) travels in Delivery.nudges through the custom command
    /// "HeliCrate" (host decides, guests apply optimistically).
    public static class HeliMishaps
    {
        /// Chance (‰) that a delivery has a mishap at all (≈ 3 in 10); express orders halve it.
        public const int MishapChance = 300;
        /// Weights inside the mishap share (sum 300 ‰ of all deliveries – L.12 table).
        public const int WeightWater = 50, WeightRoof = 60, WeightPalm = 60, WeightWrong = 60, WeightVillage = 40, WeightGoat = 20, WeightBoat = 10;
        /// Goat mishap: nudges needed. Village: Delivery.nudges ≥ this = paid. Palm / roof: ≥ 1 = the crate came down.
        public const int GoatNudges = 3, PaidMarker = 3;
        /// The search beacon smokes this long (s), then blinks until the crate is opened.
        public const float SmokeSeconds = 180f;
        /// Wanda idles on the pad after a delivery (s).
        public const float PadIdleSeconds = 120f;
        public const string CommandKey = "HeliCrate";

        /// Tests: the next rolled delivery gets this mishap (also in automated runs). Automated runs roll none otherwise.
        public static DeliveryMishap? ForceNext;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { ForceNext = null; zones = null; zonesFromProps = false; propsChecked = false; }

        // ------------------------------------------------------------------ the roll (host)
        /// Decides a delivery's mishap (host). zone = drop-zone index for WrongPlace (−1 otherwise).
        public static DeliveryMishap Roll(Delivery d, GameState s, out int zone)
        {
            zone = -1;
            if (d == null || s == null) return DeliveryMishap.None;
            uint h = HeliService.Hash(d);
            unchecked
            {
                h ^= (uint)Mathf.RoundToInt(d.arrivalTime * 1440f) * 2654435761u;
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
            }
            int zc = DropZones.Count;
            if (ForceNext.HasValue)
            {
                var forced = ForceNext.Value;
                ForceNext = null;
                if (forced == DeliveryMishap.WrongPlace) zone = zc > 0 ? (int)((h >> 20) % (uint)zc) : -1;
                return forced;
            }
            if (GameManager.AutomatedRun) return DeliveryMishap.None;
            if (d.arrivalDay <= 1 || d.itemId == "tank_t1") return DeliveryMishap.None;
            if (s.lastMishapDay == d.arrivalDay) return DeliveryMishap.None;
            int chance = d.express ? MishapChance / 2 : MishapChance;
            if (h % 1000u >= (uint)chance) return DeliveryMishap.None;
            int total = WeightWater + WeightRoof + WeightPalm + WeightWrong + WeightVillage + WeightGoat + WeightBoat;
            int w = (int)((h >> 10) % (uint)total);
            DeliveryMishap m;
            if ((w -= WeightWater) < 0) m = DeliveryMishap.Water;
            else if ((w -= WeightRoof) < 0) m = DeliveryMishap.HutRoof;
            else if ((w -= WeightPalm) < 0) m = DeliveryMishap.Palm;
            else if ((w -= WeightWrong) < 0) m = DeliveryMishap.WrongPlace;
            else if ((w -= WeightVillage) < 0) m = DeliveryMishap.Village;
            else if ((w -= WeightGoat) < 0) m = DeliveryMishap.Goat;
            else m = DeliveryMishap.Boat;
            if (m == DeliveryMishap.WrongPlace) zone = zc > 0 ? (int)((h >> 20) % (uint)zc) : -1;
            if (m == DeliveryMishap.WrongPlace && zone < 0) m = DeliveryMishap.Water;
            return m;
        }

        /// The zone a WrongPlace delivery uses on this machine (stored index, else derived from the hash).
        public static int ZoneOf(Delivery d)
        {
            int zc = DropZones.Count;
            if (d == null || zc == 0) return -1;
            if (d.dropZone >= 0 && d.dropZone < zc) return d.dropZone;
            return (int)((HeliService.Hash(d) >> 20) % (uint)zc);
        }

        // ------------------------------------------------------------------ drop zones
        static List<Vector3> zones;
        static bool zonesFromProps, propsChecked;

        /// 12 reachable, flat, dry points on the main island (L.12). PropInfo.DropZones (props) when it is published,
        /// otherwise derived deterministically from the WorldLayout anchors (identical on every co-op machine).
        public static List<Vector3> DropZones
        {
            get
            {
                if (!zonesFromProps && !propsChecked && PropInfo.IslandBuilt)
                {
                    propsChecked = true;
                    TryProps();
                }
                if (zones == null)
                {
                    // the terrain must exist, otherwise every candidate fails the dry-ground test
                    if (!WorldInfo.Built && WorldInfo.HeightProvider == null) return NoZones;
                    BuildFallbackZones();
                }
                return zones;
            }
        }

        static readonly List<Vector3> NoZones = new List<Vector3>();

        static bool TryProps()
        {
            try
            {
                var f = typeof(PropInfo).GetField("DropZones", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (f == null) return false;
                var v = f.GetValue(null);
                List<Vector3> list = null;
                if (v is IList<Vector3> l && l.Count >= 4) list = new List<Vector3>(l);
                if (list == null) return false;
                zones = list;
                zonesFromProps = true;
                return true;
            }
            catch (System.Exception) { return false; }
        }

        static readonly string[] ZoneDe =
        {
            "Dorfstrand", "Gezeitentümpel", "Leuchtturm", "Wasserfall-Becken", "Funkturm", "Treibholzstrand",
            "Versteckte Bucht", "Schlucht", "Landzunge", "Wiese am Landeplatz", "Wasserfall oben", "Strand an der Hütte"
        };
        static readonly string[] ZoneEn =
        {
            "Village beach", "Tide pools", "Lighthouse", "Waterfall pool", "Radio tower", "Driftwood beach",
            "Hidden bay", "Ravine", "Headland", "Helipad meadow", "Waterfall top", "Hut beach"
        };

        public static string ZoneName(int i) =>
            i >= 0 && i < ZoneDe.Length ? Loc.Tr(ZoneDe[i], ZoneEn[i]) : Loc.Tr("Irgendwo", "Somewhere");

        static void BuildFallbackZones()
        {
            var a = new[]
            {
                WorldLayout.SecondBeach + new Vector3(-12f, 0f, 2f),
                WorldLayout.TidePools + new Vector3(-8f, 0f, 6f),
                WorldLayout.Lighthouse + new Vector3(7f, 0f, -7f),
                WorldLayout.WaterfallPool + new Vector3(8f, 0f, -4f),
                WorldLayout.RadioTowerRuins + new Vector3(7f, 0f, 6f),
                WorldLayout.DriftwoodBeach + new Vector3(-5f, 0f, -6f),
                WorldLayout.HiddenBay + new Vector3(27f, 0f, 2f),
                WorldLayout.Ravine + new Vector3(-4f, 0f, 10f),
                WorldLayout.SecondBeach + new Vector3(30f, 0f, 3f),
                WorldLayout.Helipad + new Vector3(-13f, 0f, 9f),
                WorldLayout.WaterfallTop + new Vector3(-9f, 0f, 5f),
                WorldLayout.Hut + new Vector3(15f, 0f, -9f),
            };
            zones = new List<Vector3>(a.Length);
            for (int i = 0; i < a.Length; i++)
                if (FixUp(a[i], out Vector3 p)) zones.Add(p);
        }

        /// Deterministic spiral search for a dry, flat spot ≥ 3 m from the water near a candidate.
        static bool FixUp(Vector3 c, out Vector3 p)
        {
            float sea = Waves.SeaLevel;
            for (int ring = 0; ring <= 7; ring++)
            {
                float r = ring * 2f;
                int n = ring == 0 ? 1 : 8 + ring * 2;
                for (int k = 0; k < n; k++)
                {
                    float ang = k / (float)n * Mathf.PI * 2f + ring * 0.7f;
                    Vector3 q = c + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    float g = WorldInfo.GroundHeight(q);
                    if (g < sea + 0.5f) continue;
                    bool ok = true;
                    for (int s = 0; s < 8 && ok; s++)
                    {
                        float sa = s * Mathf.PI * 0.25f;
                        float g1 = WorldInfo.GroundHeight(q + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * 1.5f);
                        float g3 = WorldInfo.GroundHeight(q + new Vector3(Mathf.Cos(sa), 0f, Mathf.Sin(sa)) * 3f);
                        if (Mathf.Abs(g1 - g) > 0.8f || g3 < sea + 0.15f) ok = false;
                    }
                    if (!ok) continue;
                    p = new Vector3(q.x, g, q.z);
                    return true;
                }
            }
            p = c;
            return false;
        }

        // ------------------------------------------------------------------ Wanda on the radio (L.12) and on the pad
        public static string RadioLine(DeliveryMishap m)
        {
            switch (m)
            {
                case DeliveryMishap.Water: return Loc.Tr("Kleine Böe. Dein Paket badet.", "Small gust. Your parcel is taking a bath.");
                case DeliveryMishap.HutRoof: return Loc.Tr("Ich dachte, das Dach ist der Landeplatz. Sieht von oben gleich aus.", "Thought the roof was the pad. Looks the same from up here.");
                case DeliveryMishap.Palm: return Loc.Tr("Die Palme wollte es mehr als du.", "The palm wanted it more than you.");
                case DeliveryMishap.WrongPlace: return Loc.Tr("Ups. Falsche Insel? Nein, richtige Insel, falsche Ecke. Folg dem Rauch!", "Oops. Wrong island? No – right island, wrong corner. Follow the smoke!");
                case DeliveryMishap.Village: return Loc.Tr("Die Kinder haben's. Zahl mit einem Selfie.", "The kids have it. Pay with a selfie.");
                case DeliveryMishap.Goat: return Loc.Tr("Da sitzt eine Ziege drauf. Das ist nicht mein Problem.", "A goat is sitting on it. Not my problem.");
                case DeliveryMishap.Boat: return Loc.Tr("Direkt aufs Boot! Ich bin ein Genie. Oder es war Zufall.", "Straight onto the boat! I'm a genius. Or it was luck.");
            }
            return null;
        }

        /// Short notification line (L.10: icon + one line + chip).
        public static string NotifyLine(DeliveryMishap m)
        {
            switch (m)
            {
                case DeliveryMishap.Water: return Loc.Tr("Paket im Wasser", "Parcel in the water");
                case DeliveryMishap.HutRoof: return Loc.Tr("Paket auf dem Hüttendach", "Parcel on the hut roof");
                case DeliveryMishap.Palm: return Loc.Tr("Paket in der Palme", "Parcel stuck in a palm");
                case DeliveryMishap.WrongPlace: return Loc.Tr("Paket suchen!", "Find the parcel!");
                case DeliveryMishap.Village: return Loc.Tr("Paket im Dorf", "Parcel in the village");
                case DeliveryMishap.Goat: return Loc.Tr("Ziege auf dem Paket", "Goat on the parcel");
                case DeliveryMishap.Boat: return Loc.Tr("Paket auf dem Boot", "Parcel on the boat");
            }
            return Loc.Tr("Paket gelandet", "Parcel landed");
        }

        static readonly string[] PadDe =
        {
            "Unterschrift? Brauchen wir nicht. Vertrauen!",
            "Wenn's klappert, war's vorher schon kaputt.",
            "Tank voll, Kopf leer. Perfekter Flugtag.",
            "Gus hat mal ein Klavier bestellt. Ich such's heute noch.",
            "Die Möwen hier fliegen schlechter als ich. Knapp.",
            "Kurze Pause. Der Rotor muss sich beruhigen. Ich auch.",
        };
        static readonly string[] PadEn =
        {
            "Signature? Don't need one. Trust!",
            "If it rattles, it was broken before.",
            "Full tank, empty head. Perfect flying day.",
            "Gus once ordered a piano. Still looking for it.",
            "The gulls here fly worse than me. Barely.",
            "Short break. The rotor needs to calm down. So do I.",
        };

        public static string PadLine(int seed)
        {
            int i = (int)((uint)seed % (uint)PadDe.Length);
            return Loc.Tr(PadDe[i], PadEn[i]);
        }

        public const string WandaId = "wanda";

        /// A Wanda line as a subtitle (+ babble from the npc module's voice listener).
        public static void Say(string text, float seconds)
        {
            if (string.IsNullOrEmpty(text)) return;
            GameEvents.RaiseNpcLine(WandaId, text, seconds);
        }

        // ------------------------------------------------------------------ visuals
        /// Orange smoke column (≈ 25 m) for a crate dropped in the wrong place. World space; rate set by the crate.
        public static ParticleSystem SmokeColumn(Transform parent)
        {
            var ps = BoatParticles.Create("SearchSmoke", parent, new Vector3(0f, 0.9f, 0f), Quaternion.Euler(-90f, 0f, 0f), BoatMats.Smoke, true);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 9.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 3.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.95f, 0.5f, 0.18f, 0.85f), new Color(0.98f, 0.62f, 0.3f, 0.75f));
            main.gravityModifier = -0.02f;
            main.maxParticles = 140;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 6f;
            sh.radius = 0.12f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.75f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 4.2f));
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);   // a light breeze from the ENE bends the column
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.6f, -0.2f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.25f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            ps.Play();
            BoatParticles.SetRate(ps, 7f);
            return ps;
        }

        /// "Kapitän Meck": a sitting low-poly goat (faceted, world style), root at its seat, facing +Z.
        /// head = the head transform (chews / looks around).
        public static GameObject BuildGoat(Transform parent, out Transform head)
        {
            var root = new GameObject("CrateGoat");
            root.transform.SetParent(parent, false);
            var white = BoatMats.Pal("#EDE6DA");
            var patch = BoatMats.Pal("#8E6C50");
            var horn = BoatMats.Pal("#C9B48A");
            var dark = BoatMats.Pal("#3A3446");
            var s = new BoatMeshSet();
            // body lying on folded legs
            s[white].Ellipsoid(new Vector3(0f, 0.28f, -0.05f), new Vector3(0.24f, 0.22f, 0.42f), Quaternion.identity, 8, 5);
            s[patch].Ellipsoid(new Vector3(0.02f, 0.4f, -0.12f), new Vector3(0.16f, 0.1f, 0.22f), Quaternion.Euler(0f, 12f, 0f), 7, 4);
            for (int side = -1; side <= 1; side += 2)
            {
                s[white].Cylinder(new Vector3(side * 0.16f, 0.1f, 0.22f), new Vector3(side * 0.2f, 0.06f, 0.42f), 0.06f, 0.05f, 6);
                s[dark].Cylinder(new Vector3(side * 0.2f, 0.06f, 0.42f), new Vector3(side * 0.21f, 0.05f, 0.47f), 0.05f, 0.045f, 6);
                s[white].Cylinder(new Vector3(side * 0.18f, 0.1f, -0.3f), new Vector3(side * 0.22f, 0.06f, -0.08f), 0.07f, 0.055f, 6);
            }
            // tail
            s[white].Cylinder(new Vector3(0f, 0.38f, -0.45f), new Vector3(0f, 0.5f, -0.52f), 0.035f, 0.02f, 5);
            // neck
            s[white].Cylinder(new Vector3(0f, 0.36f, 0.26f), new Vector3(0f, 0.58f, 0.38f), 0.1f, 0.08f, 7);
            BoatOwnedMesh.Own(s.Build("GoatBody", root.transform, true));

            var hg = new GameObject("GoatHead").transform;
            hg.SetParent(root.transform, false);
            hg.localPosition = new Vector3(0f, 0.6f, 0.4f);
            var hs = new BoatMeshSet();
            hs[white].Ellipsoid(new Vector3(0f, 0.02f, 0.06f), new Vector3(0.1f, 0.11f, 0.16f), Quaternion.Euler(20f, 0f, 0f), 7, 4);
            hs[BoatMats.Pal("#D99A9A")].Ellipsoid(new Vector3(0f, -0.04f, 0.2f), new Vector3(0.06f, 0.05f, 0.04f), Quaternion.identity, 6, 3);
            hs[BoatMats.Pal("#D9D2C4")].Cylinder(new Vector3(0f, -0.07f, 0.15f), new Vector3(0f, -0.2f, 0.13f), 0.035f, 0.01f, 5);
            for (int side = -1; side <= 1; side += 2)
            {
                // horns curl back, ears stick out sideways, googly-ish eyes
                hs[horn].Cylinder(new Vector3(side * 0.05f, 0.11f, 0.02f), new Vector3(side * 0.08f, 0.22f, -0.06f), 0.026f, 0.018f, 5);
                hs[horn].Cylinder(new Vector3(side * 0.08f, 0.22f, -0.06f), new Vector3(side * 0.09f, 0.2f, -0.16f), 0.018f, 0.008f, 5);
                hs[white].Box(new Vector3(side * 0.13f, 0.06f, -0.02f), new Vector3(0.12f, 0.03f, 0.06f), Quaternion.Euler(0f, 0f, side * -18f));
                hs[BoatMats.White].Sphere(new Vector3(side * 0.075f, 0.06f, 0.1f), 0.032f, 6, 4);
                hs[dark].Sphere(new Vector3(side * 0.09f, 0.06f, 0.115f), 0.016f, 5, 3);
            }
            BoatOwnedMesh.Own(hs.Build("GoatHeadMesh", hg, true));
            head = hg;
            return root;
        }

        /// Wanda Wirbel (K.5 / L.12) in the cockpit: smooth bean, orange flight suit, brown aviator cap with goggles,
        /// turquoise scarf. Root at her seat, facing +Z (helicopter forward).
        public static GameObject BuildPilot(Transform parent, Vector3 seat)
        {
            var s = new SmoothMeshSet();
            var suit = LowPoly.Mat(LowPoly.Hex("#EE7F2D"));
            var skin = LowPoly.Mat(LowPoly.Hex("#E8C08A"));
            var cap = LowPoly.Mat(LowPoly.Hex("#6B4636"));
            var scarf = LowPoly.Mat(LowPoly.Hex("#3FB8C4"));
            var ink = LowPoly.Mat(LowPoly.Ink);
            var lens = LowPoly.Mat(LowPoly.Hex("#9FE4EC"));
            var white = LowPoly.Mat(LowPoly.White);
            var patch = LowPoly.Mat(LowPoly.Hex("#F2C94C"));
            // bean body (head = top of the bean, How to Fish style)
            s.Ellipsoid(suit, seat + new Vector3(0f, 0.22f, 0f), new Vector3(0.22f, 0.3f, 0.2f), Quaternion.identity, 16, 10);
            s.Ellipsoid(skin, seat + new Vector3(0f, 0.55f, 0.01f), new Vector3(0.17f, 0.16f, 0.16f), Quaternion.identity, 16, 10);
            s.Ellipsoid(patch, seat + new Vector3(0.12f, 0.3f, 0.15f), new Vector3(0.05f, 0.05f, 0.015f), Quaternion.Euler(0f, 35f, 0f), 10, 5);
            // aviator cap with ear flaps
            s.Ellipsoid(cap, seat + new Vector3(0f, 0.62f, -0.005f), new Vector3(0.175f, 0.11f, 0.17f), Quaternion.identity, 16, 8);
            s.Ellipsoid(cap, seat + new Vector3(-0.15f, 0.53f, 0f), new Vector3(0.035f, 0.08f, 0.06f), Quaternion.identity, 10, 6);
            s.Ellipsoid(cap, seat + new Vector3(0.15f, 0.53f, 0f), new Vector3(0.035f, 0.08f, 0.06f), Quaternion.identity, 10, 6);
            // goggles pushed up on the cap
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 g = seat + new Vector3(side * 0.06f, 0.68f, 0.12f);
                s.Tube(ink, g - new Vector3(0f, 0f, 0.012f), g + new Vector3(0f, 0.008f, 0.02f), 0.04f, 0.04f, 12, false, false);
                s.Ellipsoid(lens, g + new Vector3(0f, 0.005f, 0.018f), new Vector3(0.034f, 0.034f, 0.008f), Quaternion.Euler(-15f, 0f, 0f), 12, 5);
            }
            // googly eyes
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 e = seat + new Vector3(side * 0.05f, 0.56f, 0.15f);
                s.Ellipsoid(white, e, new Vector3(0.035f, 0.035f, 0.025f), Quaternion.identity, 10, 6);
                s.Ellipsoid(ink, e + new Vector3(side * 0.006f, 0.004f, 0.02f), new Vector3(0.016f, 0.016f, 0.01f), Quaternion.identity, 8, 4);
            }
            // scarf around the neck, one end flying back
            s.Tube(scarf, seat + new Vector3(0f, 0.43f, 0f), seat + new Vector3(0f, 0.47f, 0f), 0.17f, 0.165f, 16, false, false);
            s.Tube(scarf, seat + new Vector3(0.1f, 0.44f, -0.12f), seat + new Vector3(0.16f, 0.4f, -0.36f), 0.035f, 0.02f, 8, true, true);
            // arms to the controls
            s.Tube(suit, seat + new Vector3(-0.18f, 0.3f, 0.04f), seat + new Vector3(-0.12f, 0.16f, 0.3f), 0.045f, 0.04f, 10, true, true);
            s.Tube(suit, seat + new Vector3(0.18f, 0.3f, 0.04f), seat + new Vector3(0.12f, 0.16f, 0.3f), 0.045f, 0.04f, 10, true, true);
            s.Ellipsoid(ink, seat + new Vector3(-0.12f, 0.16f, 0.32f), new Vector3(0.045f, 0.04f, 0.05f), Quaternion.identity, 10, 5);
            s.Ellipsoid(ink, seat + new Vector3(0.12f, 0.16f, 0.32f), new Vector3(0.045f, 0.04f, 0.05f), Quaternion.identity, 10, 5);
            return s.Build("Wanda", parent);
        }
    }
}
