using UnityEngine;

namespace Sealebrities
{
    /// Publishes the exact positions of the props' geometry to the npc module's QuestSpots (NPC work / evening spots,
    /// quest-object spots, berths) and mirrors them in PropInfo.Spots. Keys are the npc module's ids ("v_kiosk",
    /// "q_kite", "stake:wreck:1", "horst_bench" …). Deterministic: called while the world builds.
    public static class PropSpots
    {
        public static void Register(string id, Vector3 world)
        {
            if (string.IsNullOrEmpty(id)) return;
            QuestSpots.Register(id, world);
            PropInfo.SetSpot(id, world, PropInfo.SpotYaw(id));
        }

        public static void Register(string id, Vector3 world, float yaw)
        {
            if (string.IsNullOrEmpty(id)) return;
            QuestSpots.Register(id, world);
            PropInfo.SetSpot(id, world, yaw);
        }

        /// Re-publishes a PropInfo spot under a QuestSpots id (when the props key differs).
        public static void Alias(string questId, string propKey, Vector3 offset = default)
        {
            if (PropInfo.Spot(propKey, out var p)) QuestSpots.Register(questId, p + offset);
        }
    }
}
