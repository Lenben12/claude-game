using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Sealebrities
{
    /// Hooks other modules may set so the props' interactables open their UI. Without a hook every interactable has a
    /// small built-in fallback (eat / buy one item, a banner) so nothing is ever a dead end.
    public static class PropHooks
    {
        /// UI (E.1 / L.8): open the food list – "fridge" (hut), "kiosk" (Ada's kiosk in Altriff).
        public static Action<string> OpenFood;
        /// UI (K.5): show Gus' letter card (paper style).
        public static Action OpenLetter;
        /// Player: look through the coin telescope at the lighthouse (eye position, view direction).
        public static Action<Vector3, Vector3> Telescope;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { OpenFood = null; OpenLetter = null; Telescope = null; }
    }

    /// Base of a timed interaction ("E – Graben" … 1.5 s): E starts it, it runs while the local player stays within
    /// range and completes after `seconds`. The prompt shows the progress ("Graben … 40 %", cached strings).
    public abstract class PropHold : MonoBehaviour, IInteractable
    {
        public float seconds = 1.5f;
        public float range = 3.4f;
        protected float progress = -1f;

        protected abstract string VerbDe { get; }
        protected abstract string VerbEn { get; }
        protected abstract bool Available();
        protected abstract void Complete();
        protected virtual void OnStart() { }
        protected virtual void OnStop() { }
        /// Optional hint shown instead of the verb when not available (null = hide the prompt).
        protected virtual string UnavailableHint => null;

        public bool Running => progress >= 0f;

        public virtual string GetPrompt()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return null;
            if (Running) return PropPrompt.Progress(VerbDe, VerbEn, progress / Mathf.Max(0.01f, seconds));
            if (!Available()) return UnavailableHint;
            return PropPrompt.Interact(VerbDe, VerbEn);
        }

        public virtual bool CanInteract() => GameManager.I != null && GameManager.I.InGame && (Running || Available() || UnavailableHint != null);

        public void Interact()
        {
            if (Running || !Available()) return;
            progress = 0f;
            OnStart();
        }

        protected virtual void Update()
        {
            if (!Running) return;
            var p = Refs.Player;
            bool ok = p != null && p.Root != null && !p.IsFainting && GameManager.I != null && GameManager.I.InGame
                      && (p.Root.position - transform.position).sqrMagnitude < range * range && Available();
            if (!ok)
            {
                progress = -1f;
                OnStop();
                return;
            }
            progress += Time.deltaTime;
            if (progress < seconds) return;
            progress = -1f;
            OnStop();
            Complete();
        }

        protected virtual void OnDisable()
        {
            if (!Running) return;
            progress = -1f;
            OnStop();
        }
    }

    /// Old air station at a rest spot (E.4 / Q54): „E – Luft auffüllen“, 4 s, free. A hissing hose while it fills.
    public class PropAirStation : PropHold
    {
        public ParticleSystem hiss;

        protected override string VerbDe => "Luft auffüllen";
        protected override string VerbEn => "Refill air";

        void Awake() { seconds = 4f; range = 3.6f; }

        protected override bool Available()
        {
            var p = Refs.Player;
            return p != null && !p.IsInWater && p.Air01 < 0.995f;
        }

        protected override string UnavailableHint
        {
            get
            {
                var p = Refs.Player;
                return p != null && !p.IsInWater && p.Air01 >= 0.995f ? PropPrompt.Text("Flasche ist voll", "Tank is full") : null;
            }
        }

        protected override void OnStart() { if (hiss != null) hiss.Play(true); }
        protected override void OnStop() { if (hiss != null) hiss.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        protected override void Complete()
        {
            var p = Refs.Player;
            if (p == null) return;
            p.RefillAir();
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Luft voll", "Air full"), "100 %");
        }
    }

    /// A grill (E.1): the hut campfire and the village grill. „E – Makrele grillen“ takes the cheapest fish from the
    /// shared cooler after a 6 s sizzle (smoke) and gives the grilled-fish buff. With Toni's quest wanting a fish to
    /// carry (PropQuest "carry:grilled_fish") the fish is taken along instead („Grillfisch zum Mitnehmen“).
    public class PropGrill : PropHold
    {
        public ParticleSystem smoke;
        public Material glow;
        public Color glowColor = new Color(2.5f, 0.75f, 0.18f);

        bool ToGo => PropQuest.WantsCarry("grilled_fish");

        // the prompt names the fish the grill will take („Makrele grillen“) – cached per fish uid / language
        int verbUid = int.MinValue;
        Language verbLang;
        string verbDe = "Fisch grillen", verbEn = "Grill a fish";

        void RefreshVerb()
        {
            var f = Pantry.BestGrillFish();
            int uid = f != null ? f.uid : int.MinValue + 1;
            if (uid == verbUid && verbLang == Loc.Current) return;
            verbUid = uid;
            verbLang = Loc.Current;
            var k = f != null ? FishMarket.Kind(f.kindId) : null;
            if (k == null) { verbDe = "Fisch grillen"; verbEn = "Grill a fish"; return; }
            verbDe = k.nameDe + " grillen";
            verbEn = "Grill " + k.nameEn;
        }

        protected override string VerbDe { get { if (ToGo) return "Grillfisch zum Mitnehmen"; RefreshVerb(); return verbDe; } }
        protected override string VerbEn { get { if (ToGo) return "Grilled fish to go"; RefreshVerb(); return verbEn; } }

        void Awake() { seconds = 6f; range = 3.6f; }

        protected override bool Available() => Pantry.BestGrillFish() != null && (!ToGo || !Carry.IsCarrying);
        protected override string UnavailableHint => Pantry.BestGrillFish() == null ? PropPrompt.Text("Kühlbox leer – erst angeln", "Cooler empty – go fishing") : null;

        protected override void OnStart()
        {
            if (smoke != null) smoke.Play(true);
            PropMats.SetEmission(glow, glowColor);
        }

        protected override void OnStop()
        {
            if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            PropMats.SetEmission(glow, Color.black);
        }

        protected override void Complete()
        {
            bool togo = ToGo;
            // packed to go for Toni: no buff / grill-master count for the cook
            if (!Pantry.TryGrill(out var fish, !togo)) return;
            if (togo && Carry.Pick("q:grilled_fish:0"))
            {
                GameEvents.RaiseNotify(NotifyKind.Quest, Loc.Tr("Grillfisch eingepackt", "Grilled fish packed"));
                PropQuest.RaiseUsed("carry:grilled_fish", transform.position);
                return;
            }
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Lecker!", "Delicious!"), fish != null ? fish.weightKg.ToString("0.0", CultureInfo.InvariantCulture) + " kg" : null);
        }
    }

    /// A coconut palm (E.1): „E – Palme schütteln“ uses today's coconut of this palm (shared, Pantry.TryShakePalm) and
    /// drops a coconut that bounces on the ground („E – Trinken“).
    public class PropPalm : MonoBehaviour, IInteractable
    {
        public string palmId;
        public Vector3 crown;
        public Transform shakeRoot;
        float shakeT = -1f, lastUse = -10f;

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return PropPrompt.Interact("Palme schütteln", "Shake the palm");
        }

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Pantry.PalmHasCoconut(palmId) && Time.unscaledTime - lastUse > 1f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (!Pantry.TryShakePalm(palmId)) return;
            shakeT = 0f;
            Vector3 p = transform.position;
            var pl = Refs.Player;
            Vector3 toPlayer = pl != null && pl.Root != null ? pl.Root.position - p : Vector3.forward;
            toPlayer.y = 0f;
            toPlayer = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : Vector3.forward;
            PropCoconut.Drop(transform.parent, crown, p + toPlayer * 1.1f);
            PropQuest.RaiseUsed("palm:" + palmId, p);
        }

        void Update()
        {
            if (shakeT < 0f || shakeRoot == null) return;
            shakeT += Time.deltaTime;
            float k = Mathf.Max(0f, 1f - shakeT / 0.9f);
            shakeRoot.localRotation = Quaternion.Euler(Mathf.Sin(shakeT * 31f) * 3f * k, 0f, Mathf.Sin(shakeT * 27f) * 3f * k);
            if (shakeT > 0.9f) { shakeT = -1f; shakeRoot.localRotation = Quaternion.identity; }
        }
    }

    /// A coconut on the ground (local – only the player who shook the palm sees it): „E – Trinken“ (drink buff) or, when
    /// Toni's quest wants one, „E – Mitnehmen“ (carry). Falls from the crown with a little bounce, vanishes after 3 min.
    public class PropCoconut : MonoBehaviour, IInteractable
    {
        static Mesh mesh;
        Vector3 from, to;
        float t, life;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { mesh = null; }

        public static void Drop(Transform parent, Vector3 crown, Vector3 groundTarget)
        {
            if (mesh == null)
            {
                var b = new PropMeshBuilder();
                b.Ico(Vector3.zero, new Vector3(0.15f, 0.17f, 0.15f), Quaternion.identity, 1);
                mesh = b.ToMesh("Coconut");
            }
            var go = new GameObject("Coconut");
            go.transform.SetParent(parent, false);
            groundTarget.y = PropGround.H(groundTarget.x, groundTarget.z) + 0.15f;
            go.transform.position = crown;
            PropUtil.MeshObject(go.transform, "Nut", mesh, PropMats.Coconut, Vector3.zero, Quaternion.identity);
            var sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 0.4f;
            var c = go.AddComponent<PropCoconut>();
            c.from = crown;
            c.to = groundTarget;
        }

        bool Carry_ => PropQuest.WantsCarry("coconut");

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return Carry_ ? PropPrompt.Interact("Kokosnuss mitnehmen", "Take the coconut") : PropPrompt.Interact("Kokosnuss trinken", "Drink the coconut");
        }

        public bool CanInteract() => t >= 1f && GameManager.I != null && GameManager.I.InGame;

        public void Interact()
        {
            if (!CanInteract()) return;
            if (Carry_)
            {
                if (!Carry.Pick("q:coconut:0")) { GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Hände sind voll", "Hands are full")); return; }
                PropQuest.RaiseUsed("carry:coconut", transform.position);
            }
            else
            {
                Pantry.DrinkCoconut();
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Kokosnuss!", "Coconut!"), Loc.Tr("Schneller schwimmen", "Swim faster"));
            }
            Destroy(gameObject);
        }

        void Update()
        {
            life += Time.deltaTime;
            if (life > 180f) { Destroy(gameObject); return; }
            if (t >= 1f) return;
            t = Mathf.Min(1f, t + Time.deltaTime / 0.75f);
            // fall with gravity, then a small bounce
            float fall = Mathf.Min(1f, t / 0.75f);
            Vector3 p = Vector3.Lerp(from, to, fall);
            p.y = Mathf.Lerp(from.y, to.y, fall * fall);
            if (t > 0.75f) p.y = to.y + Mathf.Sin((t - 0.75f) / 0.25f * Mathf.PI) * 0.25f;
            transform.SetPositionAndRotation(p, Quaternion.Euler(t * 400f, t * 170f, 0f));
        }
    }

    /// The hut fridge (E.1) / Ada's kiosk (L.8): „E – Kühlschrank“ opens the UI food list (PropHooks.OpenFood);
    /// without a UI hook the fridge eats the best snack from the pantry and the kiosk sells one dish.
    public class PropFoodSpot : MonoBehaviour, IInteractable
    {
        public string kind = "fridge";
        float lastUse = -10f;

        bool Kiosk => kind == "kiosk";

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return Kiosk ? PropPrompt.Interact("Kiosk", "Kiosk") : PropPrompt.Interact("Kühlschrank", "Fridge");
        }

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 0.5f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (PropHooks.OpenFood != null) { PropHooks.OpenFood(kind); return; }
            if (Kiosk) BuyFallback();
            else EatFallback();
        }

        static void EatFallback()
        {
            FoodDef best = null;
            foreach (var f in FoodDatabase.All)
            {
                if (Pantry.Count(f.id) <= 0) continue;
                if (best == null || f.value > best.value) best = f;
            }
            if (best == null)
            {
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Kühlschrank leer", "Fridge empty"), Loc.Tr("Snacks im Chopper Shop", "Snacks in the Chopper Shop"));
                return;
            }
            if (Pantry.TryEat(best.id)) GameEvents.RaiseNotify(NotifyKind.Info, best.Name, best.Description);
        }

        static void BuyFallback()
        {
            FoodDef pick = null;
            foreach (var f in FoodDatabase.All)
            {
                if (f.source != "kiosk") continue;
                if (!string.IsNullOrEmpty(f.unlockQuest) && !Quests.IsDone(f.unlockQuest)) continue;
                if (pick == null || f.price > pick.price) pick = f;
            }
            if (pick == null) return;
            if (Pantry.BuyAtKiosk(pick.id)) GameEvents.RaiseNotify(NotifyKind.Delivery, pick.Name, "−" + Loc.Money(pick.price));
        }
    }

    /// The hut locker (1c): „E – Spind öffnen“ opens the character screen.
    public class PropLocker : MonoBehaviour, IInteractable
    {
        public string GetPrompt() => CanInteract() ? PropPrompt.Interact("Spind öffnen", "Open locker") : null;
        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && !CharacterScreen.IsOpen;
        public void Interact() { if (CanInteract()) CharacterScreen.Open(CharacterScreenContext.Locker); }
    }

    /// A readable thing (Gus' letter, the 1987 base-camp sign, the tide calendar …): E shows its text as a banner, or
    /// the letter card when PropHooks.OpenLetter is set and this is Gus' letter.
    public class PropReadable : MonoBehaviour, IInteractable
    {
        public string verbDe = "Lesen", verbEn = "Read";
        public string titleDe, titleEn, textDe, textEn;
        public bool gusLetter;
        float lastUse = -10f;

        public string GetPrompt() => CanInteract() ? PropPrompt.Interact(verbDe, verbEn) : null;
        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 1f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (gusLetter)
            {
                StoryFlags.Set(StoryKeys.GusLetterRead);
                if (PropHooks.OpenLetter != null) { PropHooks.OpenLetter(); return; }
            }
            string t = string.IsNullOrEmpty(titleDe) ? "" : Loc.Tr(titleDe, titleEn) + "\n";
            GameEvents.RaiseBanner(t + Loc.Tr(textDe, textEn), new Color(0.96f, 0.91f, 0.78f));
        }

        public const string LetterDe = "Kind, die Insel gehört jetzt dir. Die Hütte, das Boot, mein Kanal. 1987 hab ich eine Live-Doku aus der Tiefe gedreht – dann war das Studio weg, und keiner hat mir geglaubt. Mach du sie fertig: Bring den Kanal auf eine Million, füll das Sealog, und geh live da runter. Horst hilft dir. Glaub ihm nur die Hälfte. – Gus";
        public const string LetterEn = "Kid, the island is yours now. The hut, the boat, my channel. In 1987 I shot a live documentary from the deep – then the studio was gone and nobody believed me. You finish it: get the channel to a million, fill the Sealog, and go live down there. Horst will help. Believe half of what he says. – Gus";
    }

    /// The broken coin telescope at the lighthouse (L.5 spot 6): „E – Durchschauen“ – the Player module zooms
    /// (PropHooks.Telescope); otherwise a little gag.
    public class PropTelescope : MonoBehaviour, IInteractable
    {
        public Vector3 lookDir = Vector3.forward;
        float lastUse = -10f;
        int gag;

        public string GetPrompt() => CanInteract() ? PropPrompt.Interact("Durchschauen", "Look through") : null;
        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 1.5f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (PropHooks.Telescope != null) { PropHooks.Telescope(transform.position + Vector3.up * 1.3f, lookDir); return; }
            gag = (gag + 1) % 3;
            switch (gag)
            {
                case 0: GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Klick. Nichts. Kaputt.", "Click. Nothing. Broken.")); break;
                case 1: GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Ein Auge! Ach nein – eine Möwe.", "An eye! Oh – a seagull.")); break;
                default: GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Da draußen blinkt was. Richtung Graben.", "Something blinks out there. Toward the trench.")); break;
            }
        }
    }

    /// A seat (bench, rest spot, log): „E – Hinsetzen“ seats the player (IPlayer.SetSeated); moving, jumping or E stands
    /// up. At a rest spot with an air station (refillAir) the tank fills while sitting (4 s).
    public class PropSeat : MonoBehaviour, IInteractable
    {
        public Transform seat;
        public Vector3 standPos;
        public bool refillAir;
        public bool restSpot;
        float sitT = -1f, lastUse = -10f;
        bool sitting;

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return restSpot ? PropPrompt.Interact("Ausruhen", "Take a rest") : PropPrompt.Interact("Hinsetzen", "Sit down");
        }

        public bool CanInteract()
        {
            var p = Refs.Player;
            return seat != null && p != null && !p.IsSeated && !p.IsInWater && GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 0.8f;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            var p = Refs.Player;
            lastUse = Time.unscaledTime;
            p.SetSeated(seat);
            sitting = true;
            sitT = 0f;
        }

        void Update()
        {
            if (!sitting) return;
            var p = Refs.Player;
            if (p == null || !p.IsSeated)
            {
                sitting = false;
                return;
            }
            sitT += Time.deltaTime;
            if (refillAir && sitT >= 4f && sitT - Time.deltaTime < 4f && p.Air01 < 0.995f)
            {
                p.RefillAir();
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Ausgeruht · Luft voll", "Rested · air full"), "100 %");
            }
            if (sitT < 0.4f || UIState.BlocksGameplay) return;
            if (GameInput.Move.sqrMagnitude > 0.25f || GameInput.JumpPressed || GameInput.InteractPressed)
            {
                sitting = false;
                lastUse = Time.unscaledTime;
                p.ClearSeated(standPos);
            }
        }

        void OnDisable()
        {
            if (!sitting) return;
            sitting = false;
            var p = Refs.Player;
            if (p != null && p.IsSeated) p.ClearSeated(standPos);
        }
    }

    /// The rope swing over the waterfall pool (L.5 spot 7): „E – Schwingen“ seats the player on the swinging knot; E
    /// (or the end of the second swing) lets go above the pool – splash.
    public class PropRopeSwing : MonoBehaviour, IInteractable
    {
        public Transform pivot;
        public Transform knot;
        public Vector3 releaseDir = Vector3.forward;
        public float amplitude = 48f;
        float t = -1f, lastUse = -10f;
        bool riding;

        public string GetPrompt()
        {
            if (riding) return null;
            return CanInteract() ? PropPrompt.Interact("Schwingen", "Swing") : null;
        }

        public bool CanInteract()
        {
            var p = Refs.Player;
            return !riding && knot != null && p != null && !p.IsSeated && !p.IsInWater && GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 1.5f;
        }

        public void Interact()
        {
            if (!CanInteract()) return;
            var p = Refs.Player;
            lastUse = Time.unscaledTime;
            riding = true;
            t = 0f;
            p.SetSeated(knot);
        }

        void Update()
        {
            if (pivot == null) return;
            if (!riding)
            {
                // idle: the rope hangs and moves a little in the wind
                float s = Mathf.Sin(Time.time * 0.9f) * 3f;
                pivot.localRotation = Quaternion.Euler(s, 0f, 0f);
                return;
            }
            var p = Refs.Player;
            t += Time.deltaTime;
            // start from the bank (back), swing out over the pool twice
            float ang = -amplitude * Mathf.Cos(t * 2.1f) * Mathf.Exp(-t * 0.12f);
            pivot.localRotation = Quaternion.Euler(ang, 0f, 0f);
            bool release = (t > 0.6f && GameInput.InteractPressed && ang > 5f) || t > 6.2f || p == null || !p.IsSeated;
            if (!release) return;
            riding = false;
            lastUse = Time.unscaledTime;
            if (p != null && p.IsSeated) p.ClearSeated(knot.position + Vector3.up * 0.2f + releaseDir * 0.3f);
            PropQuest.RaiseUsed("rope_swing", transform.position);
        }
    }

    /// Shows / hides a target while a story flag is set (Toni's raft from day 10, the Fog Isle buoy lane with the map).
    public class PropFlagGate : MonoBehaviour
    {
        public GameObject target;
        public string flag;
        public bool invert;
        float next;

        void OnEnable() { GameEvents.StoryFlagSet += OnFlag; Apply(); }
        void OnDisable() { GameEvents.StoryFlagSet -= OnFlag; }
        void OnFlag(string f) { if (f == flag) Apply(); }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            Apply();
        }

        void Apply()
        {
            if (target == null) return;
            bool on = GameManager.I != null && GameManager.I.InGame && StoryFlags.Has(flag);
            if (invert) on = !on;
            if (target.activeSelf != on) target.SetActive(on);
        }

        public static PropFlagGate Attach(GameObject holder, GameObject target, string flag, bool invert = false)
        {
            var g = holder.AddComponent<PropFlagGate>();
            g.target = target;
            g.flag = flag;
            g.invert = invert;
            return g;
        }
    }

    /// Lighthouse cellar (L.6 #11): „E – Teppich anheben“ rolls the rug away and reveals the trapdoor; „E – Falltür
    /// öffnen“ opens it (the cellar ladder works from then on). Local per machine (the cellar itself is just hidden).
    public class PropTrapdoor : MonoBehaviour, IInteractable
    {
        public Transform rug, hatch;
        public GameObject ladder;
        int stage;
        float anim = -1f, lastUse = -10f;
        Vector3 rugPos0;
        Quaternion hatch0;

        void Start()
        {
            if (rug != null) rugPos0 = rug.localPosition;
            if (hatch != null) hatch0 = hatch.localRotation;
            if (ladder != null) ladder.SetActive(false);
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return stage == 0 ? PropPrompt.Interact("Teppich anheben", "Lift the rug") : PropPrompt.Interact("Falltür öffnen", "Open the trapdoor");
        }

        public bool CanInteract() => stage < 2 && anim < 0f && GameManager.I != null && GameManager.I.InGame && Time.unscaledTime - lastUse > 0.5f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            anim = 0f;
            stage++;
            if (stage == 2 && ladder != null) ladder.SetActive(true);
        }

        void Update()
        {
            if (anim < 0f) return;
            anim = Mathf.Min(1f, anim + Time.deltaTime / 0.8f);
            float e = Mathf.SmoothStep(0f, 1f, anim);
            if (stage == 1 && rug != null) rug.localPosition = rugPos0 + new Vector3(0f, 0.02f, 1.1f * e);
            if (stage == 2 && hatch != null) hatch.localRotation = hatch0 * Quaternion.Euler(-100f * e, 0f, 0f);
            if (anim >= 1f) anim = -1f;
        }
    }

    /// Two-player co-op spots (F.4 / Q56): the heavy door in the wreck hold and the stuck chest at the kelp island
    /// open when two DIFFERENT players press E within 3 s. Presses travel to the host (NetCommand.Custom "PropPress");
    /// the host sets the story flag – every machine sees the open door through the shared flags.
    public static class PropCoop
    {
        public const string PressKey = "PropPress";
        public const float Window = 3f;

        struct PressInfo { public int player; public float time; }
        static readonly Dictionary<string, PressInfo> Last = new Dictionary<string, PressInfo>();
        static bool installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Last.Clear(); installed = false; }

        public static void Install()
        {
            if (installed) return;
            installed = true;
            CoreCommands.RegisterCustom(PressKey, (from, args) => HostPress(args != null && args.Length > 1 ? args[1] : null, args != null && args.Length > 2 ? args[2] : null, from));
        }

        /// Local press (E). Clients forward it; the host evaluates.
        public static void Press(string spotId, string flag)
        {
            if (string.IsNullOrEmpty(spotId) || string.IsNullOrEmpty(flag)) return;
            if (Net.IsClient) { Net.Command(NetCommand.Custom, PressKey, spotId, flag); return; }
            HostPress(spotId, flag, Net.IsOnline ? Net.LocalId : 0);
        }

        static bool HostPress(string spotId, string flag, int player)
        {
            if (string.IsNullOrEmpty(spotId) || string.IsNullOrEmpty(flag) || !Net.HasAuthority) return false;
            if (!flag.StartsWith("coop_", StringComparison.Ordinal)) return false;   // only the props' own flags
            float now = Time.unscaledTime;
            if (Last.TryGetValue(spotId, out var p) && p.player != player && now - p.time <= Window)
            {
                Last.Remove(spotId);
                StoryFlags.Set(flag);
                return true;
            }
            Last[spotId] = new PressInfo { player = player, time = now };
            return true;
        }
    }

    /// A two-player door / chest (PropCoop): „E – Zu zweit drücken“; solo shows a hint (the wreck has a vent detour).
    /// soloPresses > 0 (the kelp chest, GDD Q56 „allein gibt es einen Umweg“): alone, that many E presses in quick
    /// succession (≤ SoloWindow s apart) pry it open – „Allein hebeln 2/5“.
    public class PropCoopAction : MonoBehaviour, IInteractable
    {
        public const float SoloWindow = 3f;
        public string spotId;
        public string flag;
        public string verbDe = "Zu zweit drücken", verbEn = "Push together";
        public string soloDe = "Zu schwer für einen", soloEn = "Too heavy for one";
        public int soloPresses;
        int soloCount;
        float lastSoloPress = -100f;
        public Transform moving;
        public Vector3 openOffset;
        public Vector3 openEuler;
        public Collider blocker;
        float lastUse = -10f, open01;
        Vector3 pos0;
        Quaternion rot0;
        bool init;

        bool IsOpen => GameManager.I != null && StoryFlags.Has(flag);

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            bool coop = Net.IsOnline && Net.PlayerCount > 1;
            if (coop) return PropPrompt.Interact(verbDe, verbEn);
            if (soloPresses <= 0) return PropPrompt.Text(soloDe, soloEn);
            int n = Time.unscaledTime - lastSoloPress <= SoloWindow ? Mathf.Clamp(soloCount, 0, soloPresses) : 0;
            if (soloDeTexts == null)
            {
                // built once (no per-frame strings): index 0 = the start hint, 1..n−1 = the counter
                soloDeTexts = new string[soloPresses + 1];
                soloEnTexts = new string[soloPresses + 1];
                soloDeTexts[0] = "Allein hebeln (" + soloPresses + "× schnell)";
                soloEnTexts[0] = "Pry alone (" + soloPresses + "× quickly)";
                for (int i = 1; i <= soloPresses; i++) { soloDeTexts[i] = "Allein hebeln " + i + "/" + soloPresses; soloEnTexts[i] = "Pry alone " + i + "/" + soloPresses; }
            }
            return PropPrompt.Interact(soloDeTexts[n], soloEnTexts[n]);
        }

        string[] soloDeTexts, soloEnTexts;

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && !IsOpen && Time.unscaledTime - lastUse > 0.4f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (!(Net.IsOnline && Net.PlayerCount > 1))
            {
                if (soloPresses > 0)
                {
                    // solo detour: offline and a lone host both have the authority
                    float now = Time.unscaledTime;
                    if (now - lastSoloPress > SoloWindow) soloCount = 0;
                    lastSoloPress = now;
                    soloCount++;
                    if (soloCount >= soloPresses)
                    {
                        soloCount = 0;
                        if (Net.HasAuthority) StoryFlags.Set(flag);
                        GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Geschafft – aufgehebelt!", "Done – pried open!"));
                    }
                    return;
                }
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr(soloDe, soloEn), Loc.Tr("Zu zweit", "Two players"));
                return;
            }
            PropCoop.Press(spotId, flag);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Hau ruck!", "Heave-ho!"), Loc.Tr("Zweiter drückt mit", "Second player: push too"));
        }

        void Update()
        {
            if (moving == null) return;
            if (!init) { init = true; pos0 = moving.localPosition; rot0 = moving.localRotation; }
            float target = IsOpen ? 1f : 0f;
            if (Mathf.Approximately(open01, target)) return;
            open01 = Mathf.MoveTowards(open01, target, Time.deltaTime / 1.2f);
            float e = Mathf.SmoothStep(0f, 1f, open01);
            moving.localPosition = pos0 + openOffset * e;
            moving.localRotation = rot0 * Quaternion.Euler(openEuler * e);
            if (blocker != null) blocker.enabled = open01 < 0.5f;
        }
    }

    /// The captain's cabin door of the MS Clickbait (L.6 #6): locked until somebody found the key in the moray nest
    /// (shared flag "wreck_key"); then „E – Aufschließen“ swings it open for everyone.
    public class PropKeyDoor : MonoBehaviour, IInteractable
    {
        public const string KeyFlag = "wreck_key", OpenFlag = "wreck_cabin_open";
        public Transform leaf;
        public Collider blocker;
        float open01, lastUse = -10f;

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return StoryFlags.Has(KeyFlag) ? PropPrompt.Interact("Aufschließen", "Unlock") : PropPrompt.Text("Abgeschlossen", "Locked");
        }

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && !StoryFlags.Has(OpenFlag) && Time.unscaledTime - lastUse > 0.5f;

        public void Interact()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            if (!StoryFlags.Has(KeyFlag))
            {
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Abgeschlossen", "Locked"), Loc.Tr("Schlüssel?", "Key?"));
                return;
            }
            StoryFlags.Set(OpenFlag);
        }

        void Update()
        {
            if (leaf == null) return;
            float target = GameManager.I != null && StoryFlags.Has(OpenFlag) ? 1f : 0f;
            if (Mathf.Approximately(open01, target)) return;
            open01 = Mathf.MoveTowards(open01, target, Time.deltaTime / 1.5f);
            leaf.localRotation = Quaternion.Euler(0f, -95f * Mathf.SmoothStep(0f, 1f, open01), 0f);
            if (blocker != null) blocker.enabled = open01 < 0.4f;
        }
    }

    /// The cabin key in the moray nest (L.6 #6): the morays only let go when the lamp has been off for 5 s nearby.
    /// Taking it sets the shared flag "wreck_key".
    public class PropMorayKey : MonoBehaviour, IInteractable
    {
        public GameObject model;
        float dark;

        bool Taken => GameManager.I != null && StoryFlags.Has(PropKeyDoor.KeyFlag);

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return dark >= 5f ? PropPrompt.Interact("Schlüssel nehmen", "Take the key") : PropPrompt.Text("Die Muränen fauchen … Licht aus?", "The morays hiss… lights off?");
        }

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && !Taken;

        public void Interact()
        {
            if (!CanInteract()) return;
            if (dark < 5f)
            {
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Die Muränen schnappen!", "The morays snap!"), Loc.Tr("Lampe aus", "Lamp off"));
                return;
            }
            StoryFlags.Set(PropKeyDoor.KeyFlag);
            GameEvents.RaiseNotify(NotifyKind.Story, Loc.Tr("Kajütenschlüssel", "Cabin key"), Loc.Tr("Brücke", "Bridge"));
        }

        void Update()
        {
            if (model != null && model.activeSelf == Taken) model.SetActive(!Taken);
            var p = Refs.Player;
            if (p == null || p.Root == null) return;
            bool near = (p.Root.position - transform.position).sqrMagnitude < 6f * 6f;
            if (near && !p.FlashlightOn) dark += Time.deltaTime;
            else dark = 0f;
        }
    }
}
