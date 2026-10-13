using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Quest objects in the world (L.7 side quests, K.8 Toni's chain, B.2 Gus missions). The props module places them
    /// deterministically and publishes their positions; the npc module decides when they are wanted and what using
    /// one does.
    ///
    /// Ids (stable):
    ///  quest:&lt;QUEST&gt;:&lt;name&gt;    side quests – H2 sunglasses, W1 drum_0..2, P1 peg_0..2, M1 driftwood_0..4, M2 paddle, T1 net_0..2,
    ///                            A2 grape_0..5, L1 kite, N2 tape, B1 stick, L3 map_treasure
    ///  toni:&lt;step&gt;:&lt;name&gt;      Toni (K.8) – T3 recorder, T4 valve, T4 coil
    ///  mission:&lt;M&gt;:&lt;name&gt;      Gus missions – M1 lucky_fin, M2 radio, M3 lens_0..2, M3 lamp (install point)
    ///  stake:&lt;site&gt;:&lt;i&gt;        Dr. Fin's research stakes (K.3) – 3 per site, „Probe nehmen“ (2 s)
    ///  carry:coconut / carry:grilled_fish   – the grill / coconut ask WantsCarry before eating
    ///
    /// Default rules while the npc module has not set IsWanted: quest:Q:* while Quests.IsActive(Q); mission:M:* while
    /// Missions.StateOf(M) == Active; toni:* while StoryFlags.Has(toni_arrived) and the "toni" chain runs; stake:* always.
    /// Used fires on the LOCAL machine (npc: Quests.AddProgress / Carry.Pick / Missions.Set …); objects the local
    /// player already used stay hidden on this machine until IsWanted says otherwise.
    public static class PropQuest
    {
        /// npc: is the object with this id wanted right now (visible + usable)? Null → default rules above.
        public static Func<string, bool> IsWanted;
        /// npc: should the grill / coconut be taken along instead of eaten? (ids "grilled_fish", "coconut").
        public static Func<string, bool> CarryWanted;
        /// Fired when the local player used a quest object (id, world position).
        public static event Action<string, Vector3> Used;
        /// Positions of every quest object (for map markers / QuestTracker targets).
        public static readonly Dictionary<string, Vector3> Positions = new Dictionary<string, Vector3>();

        static readonly HashSet<string> usedLocal = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { IsWanted = null; CarryWanted = null; Used = null; Positions.Clear(); usedLocal.Clear(); parsed.Clear(); }

        public static bool Wanted(string id)
        {
            if (string.IsNullOrEmpty(id) || GameManager.I == null || !GameManager.I.InGame) return false;
            if (IsWanted != null) return IsWanted(id);
            if (usedLocal.Contains(id)) return false;
            return DefaultWanted(id);
        }

        public static bool WantsCarry(string item)
        {
            if (GameManager.I == null || !GameManager.I.InGame || Carry.IsCarrying) return false;
            if (CarryWanted != null) return CarryWanted(item);
            // default: no – the npc module hands out Toni's "q:grilled_fish:0" itself when a grilled fish is eaten
            // (FoodEaten) and spawns its own "q:coconut:n" pickups at the palms (QuestSpots coconut:n)
            return false;
        }

        struct Parsed { public string kind, key; }
        static readonly Dictionary<string, Parsed> parsed = new Dictionary<string, Parsed>();

        static bool DefaultWanted(string id)
        {
            if (!parsed.TryGetValue(id, out var pr))
            {
                int a = id.IndexOf(':');
                int b = a >= 0 ? id.IndexOf(':', a + 1) : -1;
                pr = new Parsed
                {
                    kind = a >= 0 ? id.Substring(0, a) : "",
                    key = a < 0 ? "" : (b > a ? id.Substring(a + 1, b - a - 1) : id.Substring(a + 1)),
                };
                parsed[id] = pr;
            }
            string kind = pr.kind, key = pr.key;
            switch (kind)
            {
                case "quest": return Quests.IsActive(key);
                case "mission": return Missions.StateOf(key) == Missions.Active;
                case "toni": return StoryFlags.Has(StoryKeys.ToniArrived) && Quests.IsActive(Quests.ToniChain);
                case "stake": return true;
            }
            return false;
        }

        public static void RaiseUsed(string id, Vector3 pos)
        {
            if (string.IsNullOrEmpty(id)) return;
            Used?.Invoke(id, pos);
        }

        internal static void MarkUsed(string id) { if (!string.IsNullOrEmpty(id)) usedLocal.Add(id); }

        /// Clears the local "used" memory of an id (npc: the quest restarted).
        public static void Forget(string id) { if (id != null) usedLocal.Remove(id); }
    }

    /// One quest object: a small model + trigger. „E – {verb}“ (optionally timed: hold the spot for `seconds`, e.g.
    /// cutting the ghost net 5 s, taking a water sample 2 s). carryItem picks it up into both hands (Carry.Pick).
    public class PropQuestItem : MonoBehaviour, IInteractable
    {
        public string id;
        public string verbDe = "Aufheben", verbEn = "Pick up";
        public float seconds;
        /// Carry item id ("salv"-style ids are heavy) – null: no carrying, only the Used event.
        public string carryItem;
        public GameObject model;
        public Collider trigger;
        public ParticleSystem glint;
        /// Keep the model visible when not wanted (stakes, the lighthouse lamp …).
        public bool alwaysVisible;
        public float glintRange = 6f;

        float progress = -1f, next;
        bool visible = true, glinting;

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            if (progress >= 0f) return PropPrompt.Progress(verbDe, verbEn, progress / Mathf.Max(0.01f, seconds));
            return PropPrompt.Interact(verbDe, verbEn);
        }

        public bool CanInteract() => PropQuest.Wanted(id) && (carryItem == null || !Carry.IsCarrying || progress >= 0f);

        public void Interact()
        {
            if (!CanInteract() || progress >= 0f) return;
            if (seconds > 0.05f) { progress = 0f; return; }
            Use();
        }

        void Use()
        {
            if (carryItem != null && !Carry.Pick(carryItem))
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Hände sind voll", "Hands are full"));
                return;
            }
            PropQuest.MarkUsed(id);
            PropQuest.RaiseUsed(id, transform.position);
            Refresh();
        }

        void Update()
        {
            if (progress >= 0f)
            {
                var p = Refs.Player;
                if (p == null || p.Root == null || (p.Root.position - transform.position).sqrMagnitude > 4f * 4f || !PropQuest.Wanted(id)) progress = -1f;
                else
                {
                    progress += Time.deltaTime;
                    if (progress >= seconds) { progress = -1f; Use(); }
                }
            }
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            Refresh();
        }

        void Refresh()
        {
            bool wanted = PropQuest.Wanted(id);
            bool show = wanted || alwaysVisible;
            if (show != visible)
            {
                visible = show;
                if (model != null) model.SetActive(show);
            }
            if (trigger != null && trigger.enabled != wanted) trigger.enabled = wanted;
            bool g = wanted && PropCam.Valid && (PropCam.Position - transform.position).sqrMagnitude < glintRange * glintRange;
            if (g != glinting)
            {
                glinting = g;
                if (glint != null)
                {
                    if (g) glint.Play(true);
                    else glint.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }

        /// Builds a quest object: root with a trigger sphere, a model holder and a glint; registers the position.
        public static PropQuestItem Create(Transform parent, string id, Vector3 pos, float yaw, float triggerRadius = 0.6f)
        {
            var go = new GameObject("Quest_" + id.Replace(':', '_'));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var q = go.AddComponent<PropQuestItem>();
            q.id = id;
            var m = new GameObject("Model");
            m.transform.SetParent(go.transform, false);
            q.model = m;
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = triggerRadius;
            sc.center = Vector3.up * 0.2f;
            q.trigger = sc;
            q.glint = PropFX.Sparkle(go.transform, pos + Vector3.up * 0.3f, 0.25f);
            PropQuest.Positions[id] = pos;
            return q;
        }
    }
}
