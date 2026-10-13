using UnityEngine;

namespace Sealebrities
{
    /// Body state of an NPC (replicated as a byte – APPEND only).
    public enum NpcMotion : byte
    {
        Hidden, Idle, Walk, Run, Sit, Lie, Swim,
        /// Idle work loops (L.8): weaving, mending nets, cooking, carving, music, ukulele.
        Weave, Mend, Cook, Carve, Marimba, Strum,
        /// Dozing standing up (Horst gag 5) / sleeping in the hammock (grandpa).
        Doze,
        /// Playing cards at the meeting house (Pearl vs Horst).
        Cards,
        /// Paddling an outrigger canoe (M3, First Paddle).
        Paddle,
        /// Waving both arms (cheering).
        Cheer,
        /// Round 3: knocked down after a lost stick fight – sits in the sand, head wobbling (hermits).
        Dazed,
        /// Round 3: standing with folded arms (a wary hermit, the guard at the camp gap).
        ArmsCrossed
    }

    /// One-shot gestures layered over the motion (replicated as a byte + sequence counter – APPEND only).
    public enum NpcGesture : byte
    {
        None, Wave, Point, Shrug, ThumbsUp, Facepalm, TaDa, Twirl, Whistle, Selfie, Laugh, Nod, Think, Clap,
        /// "Gute Flut!" – an open hand raised (L.8 greeting).
        Greet,
        /// Horst steps backward off the dock (gag 6).
        Fall,
        /// Arms flail (running in late, panicking).
        Flail,
        /// Hands something over (quest hand-in).
        Give,
        // ---- round 3 (hermits + the gesture wheel)
        /// Shoos somebody away with both hands.
        Shoo,
        /// Hand on the heart, a small bow toward the sea.
        HandOnHeart,
        /// Stick swing (wind-up + strike) of an angry hermit.
        Swing,
        /// Hit by a stick: flinches back.
        Flinch,
        /// A deep bow (respect).
        Bow,
        /// Raises the spyglass to the eye (the lookout).
        Spyglass,
        /// Proud pose for the camera.
        Pose
    }

    /// Small helpers shared by the npc module.
    public static class NpcUtil
    {
        public static bool InGame => GameManager.I != null && GameManager.I.InGame;
        public static GameState State => GameManager.I != null ? GameManager.I.State : null;

        /// Hours 0..24 of the in-game clock.
        public static float Hour => GameManager.I != null ? Mathf.Repeat(GameManager.I.State.timeOfDay, 1f) * 24f : 12f;

        public static int Day => GameManager.I != null ? GameManager.I.State.day : 1;

        public static bool PlayerValid(out IPlayer p)
        {
            p = Refs.Player;
            if (p == null) return false;
            if (p is Object o && o == null) { p = null; return false; }
            return p.Root != null;
        }

        /// Position of the local player's feet (or Vector3.zero without a player).
        public static Vector3 LocalPlayerPos
        {
            get { return PlayerValid(out var p) ? p.Root.position : Vector3.zero; }
        }

        public static float FlatDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static float FlatDistSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        static readonly RaycastHit[] hits = new RaycastHit[12];

        /// Walkable surface height at (x, z): the highest solid collider (docks, decks, porches, boardwalks) that starts
        /// below max(terrain, sea level) + 2.4 m – so roofs and awnings are ignored – and is not an NPC or a player, else
        /// the terrain.
        public static float SurfaceY(Vector3 p)
        {
            float ground = WorldInfo.GroundHeight(p);
            float fromY = Mathf.Max(ground, Waves.SeaLevel) + 2.4f;
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, fromY, p.z), Vector3.down, hits, fromY - ground + 1f,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                var c = hits[i].collider;
                if (c == null) continue;
                if (c.GetComponentInParent<NpcActor>() != null) continue;
                if (c is CharacterController) continue;
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            if (best > float.NegativeInfinity && best > ground - 0.5f) return best;
            return ground;
        }

        /// True when the local player is under water.
        public static bool LocalUnderwater => PlayerValid(out var p) && p.IsUnderwater;

        /// Deterministic 0..1 hash (seeded per save) for stable random choices that every co-op machine agrees on.
        public static float Hash01(int a, int b = 0, int c = 0)
        {
            var s = State;
            unchecked
            {
                uint h = (uint)((s != null ? s.saveSeed : 1) * 73856093) ^ (uint)(a * 19349663) ^ (uint)(b * 83492791) ^ (uint)(c * 2654435761u);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                if (s != null) for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h;
            }
        }
    }
}
