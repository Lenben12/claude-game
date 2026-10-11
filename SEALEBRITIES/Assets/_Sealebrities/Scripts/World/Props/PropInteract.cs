using System.Collections.Generic;
using UnityEngine;

namespace Sealebrities
{
    /// Interaction prompts with the player's current Interact key ("E – Aufheben"), cached (no per-frame allocation).
    public static class PropPrompt
    {
        static string label;
        static Language lang;
        static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); label = null; }

        public static string Interact(string de, string en)
        {
            string l = GameInput.BindingLabel(GameAction.Interact);
            if (string.IsNullOrEmpty(l)) l = "E";
            if (l != label || lang != Loc.Current)
            {
                Cache.Clear();
                label = l;
                lang = Loc.Current;
            }
            if (!Cache.TryGetValue(de, out var s))
            {
                s = l + " – " + Loc.Tr(de, en);
                Cache[de] = s;
            }
            return s;
        }
    }

    /// A ladder the player climbs with the interact key (short fade + teleport between its two standing points).
    /// Besides the interaction ray (trigger collider on this object) the prompt is also offered by proximity – while
    /// swimming next to the dock ladder (waterBottom), and optionally while standing at the top / bottom stand – so it
    /// works even when another collider (boat boarding zone, a railing) is hit first by the ray.
    [DefaultExecutionOrder(75)]
    public class PropLadder : MonoBehaviour, IInteractable
    {
        public Vector3 bottomStand, topStand;
        public float bottomYaw, topYaw;
        public bool waterBottom;
        /// Foot of the ladder (proximity zone center for the swimming player).
        public Vector3 foot;
        public float proximity = 1.9f;
        /// Offer the prompt when standing within 1.1 m of the top / bottom standing point (on land).
        public bool proximityTop, proximityBottom;

        float lastUse = -10f;
        string written;

        IPlayer P => Refs.Player;

        bool PlayerAtTop()
        {
            var p = P;
            return p != null && p.Root != null && p.Root.position.y > (bottomStand.y + topStand.y) * 0.5f;
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            if (PlayerAtTop()) return PropPrompt.Interact("Runterklettern", "Climb down");
            return waterBottom ? PropPrompt.Interact("Rausklettern", "Climb out") : PropPrompt.Interact("Hochklettern", "Climb up");
        }

        public bool CanInteract()
        {
            var p = P;
            return p != null && p.Root != null && GameManager.I != null && GameManager.I.InGame && !p.IsSeated && !p.IsFainting
                   && Time.unscaledTime - lastUse > 1.2f;
        }

        public void Interact() => Climb();

        void Climb()
        {
            if (!CanInteract()) return;
            lastUse = Time.unscaledTime;
            bool up = !PlayerAtTop();
            Vector3 target = up ? topStand : bottomStand;
            float yaw = up ? topYaw : bottomYaw;
            Fader.FadeThrough(() =>
            {
                var p = Refs.Player;
                if (p != null) p.TeleportTo(target, yaw);
            }, 0.5f);
        }

        static bool Near(Vector3 a, Vector3 b, float r, float dy)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz < r * r && Mathf.Abs(a.y - b.y) < dy;
        }

        void Update()
        {
            if (!waterBottom && !proximityTop && !proximityBottom) return;
            var p = P;
            bool zone = false;
            if (p != null && p.Root != null && GameManager.I != null && GameManager.I.InGame && !UIState.BlocksGameplay && !p.IsSeated && !p.IsFainting)
            {
                Vector3 pos = p.Root.position;
                if (p.IsInWater)
                {
                    if (waterBottom)
                    {
                        float dx = pos.x - foot.x, dz = pos.z - foot.z;
                        zone = dx * dx + dz * dz < proximity * proximity && pos.y < topStand.y - 0.4f;
                    }
                }
                else
                {
                    zone = (proximityTop && Near(pos, topStand, 1.1f, 1.2f)) || (proximityBottom && Near(pos, bottomStand, 1.1f, 1.2f));
                }
            }
            if (!zone)
            {
                if (written != null && ReferenceEquals(InteractionPrompt.Text, written)) InteractionPrompt.Text = null;
                written = null;
                return;
            }
            string mine = GetPrompt();
            if (mine == null) return;
            string cur = InteractionPrompt.Text;
            // only when nothing else is targeted (the interactor handles E itself when its ray hits this ladder)
            if (cur == null || cur == VaultPrompt || (written != null && ReferenceEquals(cur, written)))
            {
                bool fresh = !ReferenceEquals(cur, written) || cur == null;
                InteractionPrompt.Text = mine;
                written = mine;
                if (fresh && GameInput.InteractPressed) Climb();
            }
        }

        // the player's "<Jump key> – Rausklettern" hint (same text as PlayerInteractor.ClimbPrompt, so the dock ladder
        // still takes over after Jump was rebound); rebuilt only when the binding label or the language changes
        static string vaultPrompt, vaultKey;
        static Language vaultLang;

        static string VaultPrompt
        {
            get
            {
                string key = GameInput.BindingLabel(GameAction.Jump);
                if (vaultPrompt == null || !ReferenceEquals(key, vaultKey) || vaultLang != Loc.Current)
                {
                    vaultKey = key;
                    vaultLang = Loc.Current;
                    vaultPrompt = key + Loc.Tr(" – Rausklettern", " – Climb out");
                }
                return vaultPrompt;
            }
        }

        void OnDisable()
        {
            if (written != null && ReferenceEquals(InteractionPrompt.Text, written)) InteractionPrompt.Text = null;
            written = null;
        }
    }

    /// Story texts of the collectibles (Onkel Gus' logbook, secrets, treasures, the message in a bottle).
    public static class PropLore
    {
        public const int PageCount = 15;

        static readonly string[] PageDe =
        {
            "Tag 1 auf Rust Cove. Hütte für drei Kokosnüsse gekauft. Das Dach leckt nur, wenn es regnet.",
            "Mein erstes Video: 4 Aufrufe. Drei davon war ich.",
            "Gerald die Schildkröte hat mir heute zugewinkt. Glaube ich.",
            "Die Bottom-Feeder-Brüder parken schon wieder ihre Yacht über meinem Riff.",
            "Im Wrack singt nachts irgendwas. Ich filme lieber tagsüber.",
            "Neuer Plan: eine echte Tiefsee-Doku. Titel: \"Live from the Deep\".",
            "Der Sender sagt Ja! Sie bauen ein Unterwasser-Studio für die Live-Show.",
            "Das Studio hängt an Ketten über dem Graben. Der Techniker sagt, das hält. Bestimmt.",
            "Probelauf. Ganz unten im Graben hat mich etwas angeschaut. Ein Auge. Nur ein riesiges Auge.",
            "Live-Sendung: erst Rauschen, dann Stille. Die Ketten sind weg. Das Studio auch.",
            "Keiner glaubt mir das mit dem Auge. Der Sender hat die Doku gestrichen.",
            "Ich habe die Bänder versteckt. Wer sie findet, erzählt die Geschichte zu Ende.",
            "Blitz im Funkturm. Oder etwas anderes. Ich frage lieber nicht.",
            "Ruhestand! Die Hütte gehört jetzt der neuen Crew. Seid nett zu Gerald.",
            "Wenn eine Million Leute zuschauen, zeigt es sich vielleicht. Das Auge.",
        };

        static readonly string[] PageEn =
        {
            "Day 1 on Rust Cove. Bought the hut for three coconuts. The roof only leaks when it rains.",
            "My first video: 4 views. Three of them were me.",
            "Gerald the turtle waved at me today. I think.",
            "The Bottom Feeder brothers parked their yacht over my reef again.",
            "Something sings inside the wreck at night. I prefer filming in daylight.",
            "New plan: a real deep-sea documentary. Title: \"Live from the Deep\".",
            "The network said yes! They are building an underwater studio for the live show.",
            "The studio hangs on chains above the trench. The technician says it will hold. Surely.",
            "Test run. Deep down in the trench something looked at me. An eye. Just one giant eye.",
            "Live broadcast: first static, then silence. The chains are gone. So is the studio.",
            "Nobody believes me about the eye. The network cancelled the documentary.",
            "I hid the tapes. Whoever finds them gets to finish the story.",
            "Lightning hit the radio tower. Or something else. I'd rather not ask.",
            "Retirement! The hut belongs to the new crew now. Be nice to Gerald.",
            "If a million people are watching, it might show itself. The eye.",
        };

        public static string PageId(int index) => Collectibles.Id(CollectibleKind.LogbookPage, "p" + (index + 1).ToString("00"));
        public static string Page(int index) => Loc.Tr(PageDe[Mathf.Clamp(index, 0, PageCount - 1)], PageEn[Mathf.Clamp(index, 0, PageCount - 1)]);

        public const string BottleKey = "gus";
        public static string BottleText => Loc.Tr(
            "Flaschenpost von Gus: Das TV-Studio ist irgendwo im Graben verschwunden. Meine Filmrolle hab ich aber gerettet – sie liegt beim alten Funkturm.",
            "Message from Gus: the TV studio vanished somewhere in the trench. But I saved my film reel – it's at the old radio tower.");

        public struct Named { public string key, nameDe, nameEn, textDe, textEn; }

        public static readonly Named[] Secrets =
        {
            new Named { key = "cave_grotto", nameDe = "Gus' geheime Filmecke", nameEn = "Gus' secret film corner",
                textDe = "Hier hat Onkel Gus seine Doku geschnitten. Die alte Kamera läuft noch!", textEn = "This is where Uncle Gus edited his documentary. The old camera still runs!" },
            new Named { key = "waterfall", nameDe = "Versteck hinter dem Wasserfall", nameEn = "Hideout behind the waterfall",
                textDe = "Gus' Notvorrat: drei Dosen Kokoskekse und ein Zettel: \"Nicht für Gerald!\"", textEn = "Gus' emergency stash: three tins of coconut cookies and a note: \"Not for Gerald!\"" },
            new Named { key = "lighthouse_top", nameDe = "Ganz oben", nameEn = "At the very top",
                textDe = "Ins Geländer geritzt: \"G + Gerald, für immer\".", textEn = "Carved into the railing: \"G + Gerald, forever\"." },
            new Named { key = "radio_tower", nameDe = "Das alte Funkgerät", nameEn = "The old radio",
                textDe = "Es knistert: \"...hier Gus ... das Studio ... tief unten im Graben ... die Rolle ... hier am Turm ...\"", textEn = "It crackles: \"...this is Gus ... the studio ... deep down in the trench ... the reel ... here at the tower ...\"" },
        };

        public static readonly Named[] Treasures =
        {
            new Named { key = "bay_chest", nameDe = "Piratenkiste", nameEn = "Pirate chest" },
            new Named { key = "bird_nest", nameDe = "Goldenes Ei", nameEn = "Golden egg" },
            new Named { key = "diving_helmet", nameDe = "Alter Taucherhelm", nameEn = "Old diving helmet" },
            new Named { key = "bridge_coins", nameDe = "Goldmünzen", nameEn = "Gold coins" },
            new Named { key = "wreck_compass", nameDe = "Kompass der MS Clickbait", nameEn = "MS Clickbait compass" },
            new Named { key = "film_reel", nameDe = "Gus' verlorene Filmrolle", nameEn = "Gus' lost film reel" },
        };

        /// Localized story text of a collectible id (pages, secrets, bottle) or null. For the phone UI.
        public static string TextFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < PageCount; i++) if (id == PageId(i)) return Page(i);
            if (id == Collectibles.Id(CollectibleKind.BottleMessage, BottleKey)) return BottleText;
            foreach (var s in Secrets) if (id == Collectibles.Id(CollectibleKind.Secret, s.key)) return Loc.Tr(s.textDe, s.textEn);
            return null;
        }

        /// Localized display name of a collectible id (treasures, secrets) or the kind name.
        public static string NameFor(string id, CollectibleKind kind)
        {
            if (!string.IsNullOrEmpty(id))
            {
                foreach (var s in Secrets) if (id == Collectibles.Id(CollectibleKind.Secret, s.key)) return Loc.Tr(s.nameDe, s.nameEn);
                foreach (var t in Treasures) if (id == Collectibles.Id(CollectibleKind.Treasure, t.key)) return Loc.Tr(t.nameDe, t.nameEn);
            }
            return Collectibles.KindName(kind);
        }
    }

    /// One optional collectible (logbook page, golden shell, treasure, secret, message in a bottle): "E – Aufheben"
    /// with a subtle sparkle; hidden once its id is in the shared collected list (re-checked when the state changes –
    /// co-op clients get the list from the host). Secrets keep their object and only stop sparkling.
    public class PropPickup : MonoBehaviour, IInteractable
    {
        public string id;
        public CollectibleKind kind;
        public int pageIndex = -1;
        public GameObject model;
        public bool keepModel;
        public bool requireDay3;
        public ParticleSystem sparkle;
        public Collider trigger;

        bool shown = true, sparkleOn;

        public bool Available()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return !requireDay3;
            if (Collectibles.IsCollected(id)) return false;
            return !requireDay3 || gm.State.day >= 3;
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            return kind == CollectibleKind.Secret ? PropPrompt.Interact("Untersuchen", "Inspect") : PropPrompt.Interact("Aufheben", "Pick up");
        }

        public bool CanInteract() => GameManager.I != null && GameManager.I.InGame && Available();

        public void Interact()
        {
            if (!CanInteract()) return;
            string name = kind == CollectibleKind.Treasure || kind == CollectibleKind.Secret ? PropLore.NameFor(id, kind) : null;
            if (!Collectibles.Collect(id, kind, name)) return;
            string text = kind == CollectibleKind.LogbookPage && pageIndex >= 0 ? PropLore.Page(pageIndex) : PropLore.TextFor(id);
            if (!string.IsNullOrEmpty(text))
            {
                string title = kind == CollectibleKind.LogbookPage ? Loc.Tr("Onkel Gus' Logbuch", "Uncle Gus' logbook")
                    : kind == CollectibleKind.BottleMessage ? Collectibles.KindName(kind) : PropLore.NameFor(id, kind);
                GameEvents.RaiseBanner(title + "\n" + text, new Color(0.96f, 0.91f, 0.78f));
            }
            Refresh(PropCam.Valid, PropCam.Position);
        }

        public void Refresh(bool camValid, Vector3 cam)
        {
            bool avail = Available();
            float d2 = camValid ? (cam - transform.position).sqrMagnitude : 0f;
            // the small models are only drawn within 140 m (distance activation)
            bool show = avail && d2 < 140f * 140f;
            if (show != shown)
            {
                shown = show;
                if (model != null && !keepModel) model.SetActive(show);
            }
            if (trigger != null && trigger.enabled != avail) trigger.enabled = avail;
            bool spark = avail && camValid && d2 < 30f * 30f;
            if (spark != sparkleOn)
            {
                sparkleOn = spark;
                if (sparkle != null)
                {
                    if (spark) sparkle.Play(true);
                    else sparkle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }
        }
    }

    /// Keeps all collectibles in sync with the shared state (periodic + event driven) and places the message in a
    /// bottle at a beach spot chosen by GameState.saveSeed (identical on all co-op machines).
    public sealed class PropCollectibleManager : MonoBehaviour
    {
        public readonly List<PropPickup> pickups = new List<PropPickup>();
        public PropPickup bottle;
        public readonly List<Vector3> bottleSpots = new List<Vector3>();
        int bottleSeed = int.MinValue;
        float next;

        void OnEnable()
        {
            Collectibles.Found += OnFound;
            Net.StateSynced += RefreshAll;
            GameEvents.GameStarted += RefreshAll;
            GameEvents.DayStarted += OnDay;
            GameEvents.ReturnedToMenu += RefreshAll;
        }

        void OnDisable()
        {
            Collectibles.Found -= OnFound;
            Net.StateSynced -= RefreshAll;
            GameEvents.GameStarted -= RefreshAll;
            GameEvents.DayStarted -= OnDay;
            GameEvents.ReturnedToMenu -= RefreshAll;
        }

        void OnFound(string id, CollectibleKind kind) => RefreshAll();
        void OnDay(int day) => RefreshAll();

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            RefreshAll();
        }

        public void RefreshAll()
        {
            PlaceBottle();
            bool valid = PropCam.Valid;
            Vector3 cam = PropCam.Position;
            for (int i = 0; i < pickups.Count; i++)
                if (pickups[i] != null) pickups[i].Refresh(valid, cam);
        }

        void PlaceBottle()
        {
            if (bottle == null || bottleSpots.Count == 0 || GameManager.I == null) return;
            var st = GameManager.I.State;
            if (st == null || st.saveSeed == bottleSeed) return;
            bottleSeed = st.saveSeed;
            var r = st.SeededRandom(7331);
            Vector3 spot = bottleSpots[r.Next(bottleSpots.Count)];
            float yaw = (float)r.NextDouble() * 360f;
            bottle.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, yaw, 0f));
        }
    }
}
