using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Nitrogen narcosis (Advanced difficulty and above): below 30 m the diver occasionally sees oversized animals
    /// that are not really there. Hallucinations are spawned through Refs.Spawner and removed again after a while.
    [DefaultExecutionOrder(80)]
    public class PlayerNarcosis : MonoBehaviour
    {
        const float StartDepth = 30f;
        const float ClearDepth = 22f;

        struct PlayerHallucination
        {
            public ICreature creature;
            public float despawnAt;
        }

        PlayerController pc;
        readonly List<PlayerHallucination> active = new List<PlayerHallucination>(4);
        float timer, toastAt = -1f;
        bool armed;

        public void Init(PlayerController controller) { pc = controller; }

        void Update()
        {
            if (pc == null) return;
            float now = Time.time;
            var gm = GameManager.I;
            bool inGame = pc.InGameplay && gm != null && gm.InGame;
            bool narcotic = inGame && pc.Diff.narcosis && pc.IsUnderwater && pc.Depth > StartDepth && !pc.IsFainting && !pc.IsBusy;

            // remove expired hallucinations (or all of them once the diver ascends / leaves the game)
            Vector3 playerPos = pc.transform.position;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PlayerHallucination h = active[i];
                if (!PlayerController.Alive(h.creature))
                {
                    active.RemoveAt(i);
                    continue;
                }
                bool alive;
                try { alive = h.creature.IsAlive; }
                catch (System.Exception) { alive = false; }
                if (!alive)
                {
                    active.RemoveAt(i);
                    continue;
                }
                bool expire = now >= h.despawnAt || !inGame || !pc.IsUnderwater || pc.Depth < ClearDepth
                              || (h.creature.Position - playerPos).sqrMagnitude > 45f * 45f;
                if (expire)
                {
                    Despawn(h.creature);
                    active.RemoveAt(i);
                }
            }

            if (toastAt > 0f && now >= toastAt)
            {
                toastAt = -1f;
                if (narcotic) GameEvents.RaiseToast(Loc.Tr("War das echt?", "Was that real?"));
            }

            if (UIState.PauseOpen) return;
            if (!narcotic)
            {
                armed = false;
                return;
            }
            if (!armed)
            {
                armed = true;
                timer = Random.Range(20f, 40f);
            }
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Random.Range(20f, 40f);
            SpawnOne(now);
        }

        void SpawnOne(float now)
        {
            var spawner = Refs.Spawner;
            if (!PlayerController.Alive(spawner)) return;
            Camera view = pc.View;
            if (view == null) return;
            SpeciesDef s = PickSpecies();
            if (s == null) return;

            Transform ct = view.transform;
            float dist = Random.Range(8f, 14f);
            Vector3 pos = ct.position + ct.forward * dist + ct.right * Random.Range(-2.5f, 2.5f) + Vector3.up * Random.Range(-1.5f, 1.5f);
            float sizeCm = s.maxCm * 1.5f;
            float halfBody = sizeCm * 0.01f * Mathf.Max(0.15f, s.bodyHeight) * 0.5f;
            float minY = WorldInfo.GroundHeight(pos) + 1f + halfBody;
            float maxY = Waves.SeaLevel - 2f - halfBody;
            if (minY > maxY) return;     // not enough water here
            pos.y = Mathf.Clamp(pos.y, minY, maxY);

            ICreature c = null;
            try { c = spawner.Spawn(s, pos, sizeCm, true); }
            catch (System.Exception e) { Debug.LogException(e); }
            if (c == null) return;
            active.Add(new PlayerHallucination { creature = c, despawnAt = now + Random.Range(9f, 16f) });
            if (Random.value < 0.4f) toastAt = now + Random.Range(2.5f, 4.5f);
        }

        /// Random species, biased towards big, scary and glowing ones.
        static SpeciesDef PickSpecies()
        {
            IReadOnlyList<SpeciesDef> all = SpeciesDatabase.All;
            if (all == null || all.Count == 0) return null;
            float total = 0f;
            for (int i = 0; i < all.Count; i++) total += Weight(all[i]);
            float r = Random.value * total;
            for (int i = 0; i < all.Count; i++)
            {
                r -= Weight(all[i]);
                if (r <= 0f) return all[i];
            }
            return all[all.Count - 1];
        }

        static float Weight(SpeciesDef s)
        {
            float w = 1f;
            if (s.IsDangerous) w += 2f;
            if (s.maxCm > 200f) w += 1.5f;
            if (s.glows) w += 1f;
            return w;
        }

        static void Despawn(ICreature c)
        {
            var spawner = Refs.Spawner;
            if (!PlayerController.Alive(spawner) || !PlayerController.Alive(c)) return;
            try { spawner.Despawn(c); }
            catch (System.Exception e) { Debug.LogException(e); }
        }

        void OnDestroy()
        {
            for (int i = 0; i < active.Count; i++) Despawn(active[i].creature);
            active.Clear();
        }
    }
}
