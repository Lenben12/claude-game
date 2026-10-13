using UnityEngine;

namespace Sealebrities
{
    /// Entry point of the npc+story module (Design_Runde2 A, B, K.5, K.8, L.7, L.8, L.12 lines).
    /// GameBootstrap may call NpcModule.Install(root) in its boot order; until it does, the module installs itself
    /// right after the boot (it waits for Refs.WorldRoot and BootState.Ready). Install is idempotent.
    public static class NpcModule
    {
        static NpcDirector director;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { director = null; }

        public static NpcDirector Director => director;

        public static void Install(Transform root)
        {
            if (director != null) return;
            NpcNet.Register();
            Dialogue.Resolve = DialogueDatabase.Resolve;
            // the phone's Tasks app reads our titles and the secret clues through its hooks
            UITaskInfo.SideQuestTitle = SideQuestDatabase.TitleOf;
            UITaskInfo.MissionTitle = NpcDirector.MissionTitle;
            UITaskInfo.SecretClue = StoryLog.SecretClueByIndex;
            director = NpcDirector.Create(root != null ? root : Refs.WorldRoot);
            Debug.Log("[Npc] module installed");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            if (director != null) return;
            var go = new GameObject("SB_NpcInstaller");
            go.AddComponent<NpcAutoInstaller>();
        }
    }

    /// Waits for the boot (world root + main menu) and installs the npc module once.
    sealed class NpcAutoInstaller : MonoBehaviour
    {
        float waited;

        void Update()
        {
            waited += Time.unscaledDeltaTime;
            if (NpcModule.Director != null) { Destroy(gameObject); return; }
            if (Refs.WorldRoot == null) return;
            if (!BootState.Ready && waited < 120f) return;
            NpcModule.Install(Refs.WorldRoot);
            Destroy(gameObject);
        }
    }
}
