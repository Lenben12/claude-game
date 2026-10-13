using UnityEngine;

namespace Sealebrities
{
    /// G.3 mischief – occasional and funny, never damage. Runs on every machine for its LOCAL player (co-op clients
    /// check the host's animals through their proxies): at most one mischief per player per 3 real minutes, a 25 %
    /// roll each time a condition becomes true. Raises GameEvents.Mischief(kind) – the player module reacts (lamp gone
    /// for 6 s, camera wiggle, hat floating off, the filming module credits Gerald) – plus a short notification and a
    /// small effect. On the host / offline the animal itself plays along (directors).
    ///   octopus_lamp     octopus within 2 m, lamp on      → it grabs the lamp, ink puff, swims off with a light
    ///   fish_nibble      school within 1.5 m, still 3 s   → "Hehe, that tickles!", tiny bubbles
    ///   sealion_bump     sea lion within 4 m              → playful bump (no air loss), steals the hat for 5 s
    ///   gerald_photobomb Gerald within 6 m while filming  → he swims right in front of the lens for 2 s
    ///   parrot_poop      parrotfish within 3 m            → a white sand puff
    ///   crab_pinch       standing on a hermit / ghost crab → "Ow! Pinch!", the crab runs off
    ///   capuchin_hat     the capuchin within 3 m on land  → he snatches the hat for 5 s (L.11)
    /// Not rate-limited: touching a crown-of-thorns starfish (crown_sting, a short pain gag, GDD 14.35).
    public sealed class FaunaMischief : MonoBehaviour
    {
        public const float Cooldown = 180f;
        public const float Chance = 0.25f;

        enum K { OctoLamp, Nibble, SeaLion, Gerald, Parrot, Pinch, Monkey, Count }

        readonly bool[] was = new bool[(int)K.Count];
        float lastAt = -999f, stingAt = -999f;
        float stillTime;
        Vector3 lastPos;
        float scanTimer;

        static readonly string[] Ids = { "octopus_lamp", "fish_nibble", "sealion_bump", "gerald_photobomb", "parrot_poop", "crab_pinch", "capuchin_hat" };

        public static float LastMischiefTime { get; private set; } = -999f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { LastMischiefTime = -999f; }

        void Update()
        {
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || p.Root == null || UIState.BlocksGameplay) return;
            float dt = Time.deltaTime;
            Vector3 root = p.Root.position;
            Vector3 head = p.View != null ? p.View.transform.position : root + Vector3.up * 1.6f;
            if ((root - lastPos).sqrMagnitude < 0.0004f && p.Velocity.sqrMagnitude < 0.01f) stillTime += dt; else stillTime = 0f;
            lastPos = root;
            scanTimer -= dt;
            if (scanTimer > 0f) return;
            scanTimer = 0.2f;

            bool under = p.IsUnderwater;
            bool recording = Refs.HandCamera != null && Refs.HandCamera.IsRecording;
            ICreature octo = null, school = null, seaLion = null, gerald = null, parrot = null, crab = null, monkey = null, crown = null;
            float dOcto = 2f, dSchool = 1.5f, dLion = 4f, dGerald = 6f, dParrot = 3f, dCrab = 0.55f, dMonkey = 3f, dCrown = 0.7f;
            var list = CreatureRegistry.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null || c.IsHallucination || !c.IsAlive) continue;
                var s = c.Species;
                if (s == null) continue;
                Vector3 cp = c.Position;
                float dh = (cp - head).magnitude;
                switch (s.id)
                {
                    case "octopus": if (under && dh < dOcto) { dOcto = dh; octo = c; } break;
                    case "sea_lion": if (under && dh < dLion) { dLion = dh; seaLion = c; } break;
                    case "green_turtle": if (under && dh < dGerald) { dGerald = dh; gerald = c; } break;
                    case "parrotfish": if (under && dh < dParrot) { dParrot = dh; parrot = c; } break;
                    case "crown_star": if (under && dh < dCrown + c.SizeCm * 0.005f) { dCrown = dh; crown = c; } break;
                    case "capuchin": if (!under) { float dm = (cp - root).magnitude; if (dm < dMonkey) { dMonkey = dm; monkey = c; } } break;
                    case "hermit_crab":
                    case "ghost_crab":
                        {
                            float dx = cp.x - root.x, dz = cp.z - root.z;
                            float dxz = Mathf.Sqrt(dx * dx + dz * dz);
                            if (dxz < dCrab && Mathf.Abs(cp.y - root.y) < 0.6f) { dCrab = dxz; crab = c; }
                            break;
                        }
                    default:
                        if (under && s.behavior == BehaviorType.Schooling && dh < dSchool) { dSchool = dh; school = c; }
                        break;
                }
            }

            // a gag, not a mischief: touching the crown-of-thorns hurts a little (no damage)
            if (crown != null && Time.time - stingAt > 30f)
            {
                stingAt = Time.time;
                GameEvents.RaiseMischief("crown_sting");
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Autsch – Stacheln!", "Ouch – spines!"), null);
            }

            Check(K.OctoLamp, octo != null && p.FlashlightOn, octo);
            Check(K.Nibble, school != null && stillTime > 3f, school);
            Check(K.SeaLion, seaLion != null, seaLion);
            Check(K.Gerald, gerald != null && recording, gerald);
            Check(K.Parrot, parrot != null, parrot);
            Check(K.Pinch, crab != null, crab);
            Check(K.Monkey, monkey != null, monkey);
        }

        void Check(K k, bool now, ICreature c)
        {
            int i = (int)k;
            bool rising = now && !was[i];
            was[i] = now;
            if (!rising || c == null) return;
            if (Time.time - lastAt < Cooldown) return;
            if (Random.value >= Chance) return;
            Trigger(k, c);
        }

        void Trigger(K k, ICreature c)
        {
            lastAt = Time.time;
            LastMischiefTime = lastAt;
            string id = Ids[(int)k];
            GameEvents.RaiseMischief(id);
            var p = Refs.Player;
            Vector3 head = p != null && p.View != null ? p.View.transform.position : c.Position;
            var fc = c as FaunaCreature;
            bool own = fc != null && Net.HasAuthority;
            switch (k)
            {
                case K.OctoLamp:
                    FaunaFx.Ink(c.Position, 0.8f);
                    LampLight.Attach(c.Transform, 6f);
                    if (own) fc.director = new SwimOff(head, 3.5f, 6f, true);
                    Note(Loc.Tr("Krake klaut deine Lampe!", "Octopus stole your lamp!"));
                    break;
                case K.Nibble:
                    FaunaBubbles.Poof(head + (p != null && p.View != null ? p.View.transform.forward : Vector3.forward) * 0.6f - Vector3.up * 0.3f, 0.15f, 10);
                    Note(Loc.Tr("Hihi, das kitzelt!", "Hehe, that tickles!"));
                    break;
                case K.SeaLion:
                    if (own) fc.director = new Bump(head, 1.6f);
                    Note(Loc.Tr("Sir Flapsalot klaut den Hut!", "Sir Flapsalot nabs your hat!"));
                    break;
                case K.Gerald:
                    if (own) fc.director = new Photobomb(2f);
                    Note(Loc.Tr("Fotobombe von Gerald!", "Gerald photobombs!"));
                    break;
                case K.Parrot:
                    FaunaFx.Sand(c.Position - c.Transform.forward * (c.SizeCm * 0.005f), 0.5f);
                    Note(Loc.Tr("… Papageifisch-Sand", "… parrotfish sand"));
                    break;
                case K.Pinch:
                    if (own && fc.behavior != null) fc.behavior.Poke(p != null ? p.Root.position : head);
                    Note(Loc.Tr("Au! Zwick!", "Ow! Pinch!"));
                    break;
                case K.Monkey:
                    if (own) fc.director = new SwimOff(p != null ? p.Root.position : head, 7f, 5f, false);
                    Note(Loc.Tr("Klappe klaut deinen Hut!", "Clapper stole your hat!"));
                    break;
            }
        }

        static void Note(string line) => GameEvents.RaiseNotify(NotifyKind.Event, line, null);

        // ================================================================== directors (host / offline only)

        /// Runs (swims) a few metres away from the player and stays away for a while (octopus with the lamp, the
        /// capuchin with the hat), then hands back to the behavior.
        sealed class SwimOff : IFaunaDirector
        {
            readonly Vector3 from;
            readonly float dist;
            float left;
            readonly bool action;
            Vector3 target;
            bool init;
            public SwimOff(Vector3 from, float dist, float seconds, bool inkAction) { this.from = from; this.dist = dist; left = seconds; action = inkAction; }
            public bool Steer(FaunaCreature c, float dt)
            {
                left -= dt;
                if (left <= 0f) { c.director = null; return false; }
                if (!init)
                {
                    init = true;
                    Vector3 away = c.pos - from;
                    if (c.mode != FaunaMode.Swim) away.y = 0f;
                    target = c.pos + (away.sqrMagnitude > 1e-4f ? away.normalized : c.Transform.forward) * dist;
                }
                Vector3 to = target - c.pos;
                if (c.mode != FaunaMode.Swim) to.y = 0f;
                float d = to.magnitude;
                c.relaxBand = true;
                c.desiredVel = d > 0.3f ? to / d * Mathf.Max(1.2f, c.cruise * 2.5f) : Vector3.zero;
                c.accelMul = 4f;
                c.activityTarget = 0.9f;
                if (c.mode != FaunaMode.Swim) c.lureMul = 3f;   // capuchin claps happily
                return true;
            }
        }

        /// Sea lion: a playful dart at the diver and away again (no air loss).
        sealed class Bump : IFaunaDirector
        {
            readonly Vector3 at;
            float t;
            readonly float dur;
            public Bump(Vector3 at, float seconds) { this.at = at; dur = seconds; }
            public bool Steer(FaunaCreature c, float dt)
            {
                t += dt;
                if (t > dur) { c.director = null; return false; }
                Vector3 target = t < dur * 0.45f ? FaunaContext.PlayerPos : at + (c.pos - at).normalized * 5f;
                c.relaxBand = true;
                c.desiredVel = c.Seek(target, 4f, 0.3f);
                c.accelMul = 5f;
                c.turnMul = 3f;
                c.activityTarget = 1f;
                return true;
            }
        }

        /// Gerald swims right in front of the lens for 2 s.
        sealed class Photobomb : IFaunaDirector
        {
            float t;
            readonly float dur;
            public Photobomb(float seconds) { dur = seconds + 2f; }
            public bool Steer(FaunaCreature c, float dt)
            {
                t += dt;
                if (t > dur) { c.director = null; return false; }
                Vector3 lens = FaunaContext.CamPos + FaunaContext.CamForward * (0.9f + c.lengthM * 0.6f);
                c.relaxBand = true;
                c.desiredVel = c.Seek(lens, Mathf.Max(1.2f, c.cruise * 3f), 0.8f);
                c.hasFaceDir = (lens - c.pos).sqrMagnitude < 1.5f;
                c.faceDir = FaunaContext.CamPos - c.pos;
                c.accelMul = 3f;
                c.activityTarget = 0.9f;
                return true;
            }
        }

        /// A small warm "lamp" light carried by the octopus for a few seconds.
        sealed class LampLight : MonoBehaviour
        {
            float left;

            public static void Attach(Transform t, float seconds)
            {
                if (t == null) return;
                var go = new GameObject("StolenLamp");
                go.transform.SetParent(t, false);
                go.transform.localPosition = new Vector3(0f, -0.2f, 0.2f);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.85f, 0.63f);
                l.range = 4f;
                l.intensity = 2.2f;
                l.shadows = LightShadows.None;
                go.AddComponent<LampLight>().left = seconds;
            }

            void Update()
            {
                left -= Time.deltaTime;
                if (left <= 0f || transform.parent == null || !transform.parent.gameObject.activeInHierarchy) Destroy(gameObject);
            }
        }
    }
}
