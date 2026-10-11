using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Looks 3 m ahead from the screen center for IInteractable components, publishes InteractionPrompt.Text
    /// and calls Interact() on E. Inactive while seated (the boat handles its own exit prompt).
    [DefaultExecutionOrder(70)]
    public class PlayerInteractor : MonoBehaviour
    {
        const float Range = 3f;

        PlayerController pc;
        readonly RaycastHit[] hits = new RaycastHit[16];
        readonly List<MonoBehaviour> behaviours = new List<MonoBehaviour>(16);
        readonly Dictionary<Collider, MonoBehaviour> found = new Dictionary<Collider, MonoBehaviour>(64);
        readonly HashSet<Collider> none = new HashSet<Collider>();
        float cacheAge;
        string written;
        bool loggedError;

        public void Init(PlayerController controller) { pc = controller; }

        void Update()
        {
            if (pc == null) return;
            Camera view = pc.View;
            bool active = pc.InGameplay && !UIState.BlocksGameplay && !pc.IsSeated && !pc.IsBusy && view != null;
            if (!active)
            {
                Release();
                return;
            }

            // components can be added/removed at runtime: forget the collider lookup every couple of seconds
            cacheAge += Time.unscaledDeltaTime;
            if (cacheAge > 2f)
            {
                cacheAge = 0f;
                found.Clear();
                none.Clear();
            }

            IInteractable target = FindTarget(view.transform);
            string prompt = null;
            bool can = false;
            if (target != null)
            {
                try
                {
                    can = target.CanInteract();
                    if (can) prompt = target.GetPrompt();
                }
                catch (System.Exception e)
                {
                    LogOnce(e);
                    can = false;
                    prompt = null;
                }
                if (string.IsNullOrEmpty(prompt)) prompt = null;
            }
            if (prompt == null && !can)
            {
                if (pc.CanLadderHint) prompt = ClimbPrompt(true);
                else if (pc.CanVaultHint) prompt = ClimbPrompt(false);
            }

            InteractionPrompt.Text = prompt;
            written = prompt;

            if (GameInput.InteractPressed)
            {
                if (can)
                {
                    try { target.Interact(); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                else if (pc.CanLadderHint) pc.TryLadder();
                else if (pc.CanVaultHint) pc.TryVault();
            }
        }

        // cached "Key – Verb" prompts (rebuilt only when the binding label or the language changes)
        string ladderPrompt, vaultPrompt, promptKey;
        Language promptLang;

        string ClimbPrompt(bool ladder)
        {
            string key = GameInput.BindingLabel(GameAction.Jump);
            if (!ReferenceEquals(key, promptKey) || promptLang != Loc.Current)
            {
                promptKey = key;
                promptLang = Loc.Current;
                ladderPrompt = key + Loc.Tr(" – Leiter hoch", " – Climb ladder");
                vaultPrompt = key + Loc.Tr(" – Rausklettern", " – Climb out");
            }
            return ladder ? ladderPrompt : vaultPrompt;
        }

        void OnDisable() { Release(); }

        /// Clears the prompt only if it is still the one we wrote (other modules may own it, e.g. while seated).
        void Release()
        {
            if (written == null) return;
            if (ReferenceEquals(InteractionPrompt.Text, written)) InteractionPrompt.Text = null;
            written = null;
        }

        IInteractable FindTarget(Transform view)
        {
            int n = Physics.RaycastNonAlloc(new Ray(view.position, view.forward), hits, Range,
                                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
            // sort by distance (insertion sort, n is tiny)
            for (int i = 1; i < n; i++)
            {
                RaycastHit h = hits[i];
                int j = i - 1;
                while (j >= 0 && hits[j].distance > h.distance)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = h;
            }
            for (int i = 0; i < n; i++)
            {
                Collider col = hits[i].collider;
                if (col == null) continue;
                MonoBehaviour mb = Resolve(col);
                if (mb != null) return (IInteractable)mb;
                if (!col.isTrigger) return null;    // solid geometry blocks the view; plain trigger volumes don't
            }
            return null;
        }

        MonoBehaviour Resolve(Collider col)
        {
            if (none.Contains(col)) return null;
            if (found.TryGetValue(col, out MonoBehaviour cached))
            {
                if (cached != null) return cached.isActiveAndEnabled ? cached : null;
                found.Remove(col);
            }

            behaviours.Clear();
            col.GetComponentsInParent(false, behaviours);
            MonoBehaviour result = null;
            bool anyCandidate = false;
            for (int i = 0; i < behaviours.Count; i++)
            {
                MonoBehaviour b = behaviours[i];
                if (b == null || !(b is IInteractable)) continue;
                anyCandidate = true;
                if (b.isActiveAndEnabled) { result = b; break; }
            }
            behaviours.Clear();
            if (result != null) found[col] = result;
            else if (!anyCandidate) none.Add(col);
            return result;
        }

        void LogOnce(System.Exception e)
        {
            if (loggedError) return;
            loggedError = true;
            Debug.LogException(e);
        }
    }
}
