using System;
using UnityEngine;

namespace Sealebrities
{
    /// Who fills a quest-line entry (L.10 default order: tutorial → story mission → side quest → sponsor deal).
    /// Goal = the long-term goal after the tutorial (lowest priority, never overwritten by a chopper search / route line).
    public enum QuestSource { Tutorial, Story, Side, Deal, Other, Goal }

    /// The always-visible quest line at the top centre (L.10). Providers (npc: tutorial, Gus missions, side quests,
    /// Toni; livestream: sponsor deals) set ONE entry per source; the tracker shows the tracked task
    /// (GameState.trackedQuest = the entry id) or the first entry in source order. The UI draws Line/Progress/Target
    /// (+ NextLine as the optional small second line) and listens to Changed / Completed.
    /// Call SetEntry only when something changed – equal values raise nothing (no per-frame allocations here).
    public static class QuestTracker
    {
        const int Sources = 6;

        struct Entry
        {
            public bool used;
            public string id, line, progress;
            /// Optional how-to line under the task (tutorial detail) – shown as the second line instead of the next task.
            public string sub;
            public bool hasTarget;
            public Vector3 target;
            public NotifyKind kind;
        }

        static readonly Entry[] entries = new Entry[Sources];

        /// Shown line (≤ 42 characters), null = nothing.
        public static string Line;
        /// "2/3" or null.
        public static string Progress;
        /// Place tasks: the distance arrow points here (null = none).
        public static Vector3? Target;
        /// Glyph of the line.
        public static NotifyKind Kind = NotifyKind.Quest;
        /// Id of the shown entry.
        public static string ActiveId;
        /// The next task in 85 % size (null = none).
        public static string NextLine;

        public static event Action Changed;
        /// A task was completed (check mark 1.5 s, then the next one).
        public static event Action<string> Completed;

        /// Simple API of J.3b – an entry of source Other with id "other".
        public static void Set(string line, string progress, Vector3? target) =>
            SetEntry(QuestSource.Other, "other", line, progress, target, NotifyKind.Quest);

        /// Sets (line != null) or removes (line == null) the entry of a source. A removal with an id only removes the
        /// entry when it is still that id (a provider never clears another provider's line of the same source).
        public static void SetEntry(QuestSource source, string id, string line, string progress, Vector3? target, NotifyKind kind = NotifyKind.Quest) =>
            SetEntry(source, id, line, progress, target, kind, null);

        /// SetEntry with a how-to line under the task (shown as the second line while this entry is shown).
        public static void SetEntry(QuestSource source, string id, string line, string progress, Vector3? target, NotifyKind kind, string sub)
        {
            int i = (int)source;
            if (i < 0 || i >= Sources) return;
            ref var e = ref entries[i];
            if (string.IsNullOrEmpty(line))
            {
                if (!e.used || (id != null && e.id != id)) return;
                e = default;
                Resolve();
                return;
            }
            bool hasT = target.HasValue;
            Vector3 t = hasT ? target.Value : Vector3.zero;
            if (string.IsNullOrEmpty(sub)) sub = null;
            if (e.used && e.id == id && e.line == line && e.progress == progress && e.hasTarget == hasT && e.kind == kind && e.sub == sub
                && (!hasT || (e.target - t).sqrMagnitude < 0.01f)) return;
            e.used = true; e.id = id; e.line = line; e.progress = progress; e.hasTarget = hasT; e.target = t; e.kind = kind; e.sub = sub;
            Resolve();
        }

        /// Shows the check mark for a finished task (the provider then sets the next entry).
        public static void Complete(string id) => Completed?.Invoke(id);

        /// Pins a task (Tasks app „Verfolgen“) – shared state, see Quests.Track.
        public static void Track(string id) => Quests.Track(id);

        public static void Clear()
        {
            for (int i = 0; i < Sources; i++) entries[i] = default;
            Resolve();
        }

        /// Re-evaluates which entry is shown (call after GameState.trackedQuest changed).
        public static void Resolve()
        {
            string tracked = GameManager.I != null && GameManager.I.InGame ? GameManager.I.State.trackedQuest : null;
            int show = -1, next = -1;
            if (!string.IsNullOrEmpty(tracked))
                for (int i = 0; i < Sources; i++) if (entries[i].used && entries[i].id == tracked) { show = i; break; }
            for (int i = 0; i < Sources; i++)
            {
                if (!entries[i].used || i == show) continue;
                if (show < 0) show = i;
                else if (next < 0) next = i;
            }
            string line = null, prog = null, id = null, nextLine = null;
            Vector3? target = null;
            NotifyKind kind = NotifyKind.Quest;
            if (show >= 0)
            {
                var e = entries[show];
                line = e.line; prog = e.progress; id = e.id; kind = e.kind;
                if (e.hasTarget) target = e.target;
            }
            if (show >= 0 && entries[show].sub != null) nextLine = entries[show].sub;
            else if (next >= 0) nextLine = entries[next].line;
            bool changed = line != Line || prog != Progress || id != ActiveId || nextLine != NextLine || kind != Kind
                           || target.HasValue != Target.HasValue || (target.HasValue && (target.Value - Target.Value).sqrMagnitude > 0.01f);
            Line = line; Progress = prog; ActiveId = id; NextLine = nextLine; Kind = kind; Target = target;
            if (changed) Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            for (int i = 0; i < Sources; i++) entries[i] = default;
            Line = null; Progress = null; Target = null; Kind = NotifyKind.Quest; ActiveId = null; NextLine = null;
            Changed = null; Completed = null;
        }
    }
}
