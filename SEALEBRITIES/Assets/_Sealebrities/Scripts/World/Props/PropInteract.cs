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
        static readonly Dictionary<string, string> Plain = new Dictionary<string, string>();
        static readonly Dictionary<string, string[]> Steps = new Dictionary<string, string[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Cache.Clear(); Plain.Clear(); Steps.Clear(); label = null; }

        static void Check()
        {
            string l = GameInput.BindingLabel(GameAction.Interact);
            if (string.IsNullOrEmpty(l)) l = "E";
            if (l != label || lang != Loc.Current)
            {
                Cache.Clear();
                Plain.Clear();
                Steps.Clear();
                label = l;
                lang = Loc.Current;
            }
        }

        public static string Interact(string de, string en)
        {
            Check();
            if (!Cache.TryGetValue(de, out var s))
            {
                s = label + " – " + Loc.Tr(de, en);
                Cache[de] = s;
            }
            return s;
        }

        /// A localized hint without a key ("Schlafen ab 18:00"), cached.
        public static string Text(string de, string en)
        {
            Check();
            if (!Plain.TryGetValue(de, out var s))
            {
                s = Loc.Tr(de, en);
                Plain[de] = s;
            }
            return s;
        }

        /// "Graben … 40 %" while a timed action runs (11 cached steps per verb).
        public static string Progress(string de, string en, float t01)
        {
            Check();
            if (!Steps.TryGetValue(de, out var arr))
            {
                arr = new string[11];
                string verb = Loc.Tr(de, en);
                for (int i = 0; i <= 10; i++) arr[i] = verb + " … " + (i * 10) + " %";
                Steps[de] = arr;
            }
            return arr[Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(t01) * 10f), 0, 10)];
        }
    }

    /// A ladder the player climbs with the interact key (short fade + teleport between its two standing points) – the
    /// fallback until the Player module's generic ladder climb (L.13) reads PropInfo.Ladders and sets
    /// PropInfo.LaddersHandledByPlayer. Besides the interaction ray (trigger collider on this object) the prompt is also
    /// offered by proximity – while swimming next to a ladder in the water (waterBottom), and optionally while standing
    /// at the top / bottom stand – so it works even when another collider is hit first by the ray.
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
        /// Rope ladders read "Strickleiter" in the prompt.
        public bool rope;

        float lastUse = -10f;
        string written;

        IPlayer P => Refs.Player;

        /// Builds a climbable ladder: a trigger box along the rails (foot → railTop) + PropLadder + the LadderInfo entry.
        /// foot = foot of the rails (ground / seafloor), railTop = top of the rails (≈ platform height + 1 m),
        /// topStand = standing point on the platform, outward = away from the wall.
        public static PropLadder Create(Transform parent, string name, Vector3 foot, float railTopY, Vector3 topStand, Vector3 outward, byte kind,
            bool waterBottom = false, Vector3? bottomStandOverride = null)
        {
            outward.y = 0f;
            outward = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.forward;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            float h = Mathf.Max(0.6f, railTopY - foot.y);
            go.transform.SetPositionAndRotation(new Vector3(foot.x, foot.y + h * 0.5f, foot.z) + outward * 0.18f, Quaternion.LookRotation(outward));
            var bc = go.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(0.9f, h, 0.7f);
            var l = go.AddComponent<PropLadder>();
            float yawIn = Mathf.Atan2(-outward.x, -outward.z) * Mathf.Rad2Deg;
            l.bottomStand = bottomStandOverride ?? (foot + outward * 0.85f + Vector3.up * 0.05f);
            l.bottomYaw = yawIn;
            l.topStand = topStand + Vector3.up * 0.05f;
            l.topYaw = yawIn + 180f;
            l.foot = foot;
            l.waterBottom = waterBottom;
            l.proximityTop = !waterBottom;
            l.proximityBottom = !waterBottom;
            l.rope = kind == LadderInfo.Rope;
            // owner: the generic climb (L.13) skips the ladder while its object is off (cellar ladder behind the trapdoor)
            PropInfo.AddLadder(foot, topStand, outward, kind, go.transform);
            return l;
        }

        bool PlayerAtTop()
        {
            var p = P;
            return p != null && p.Root != null && p.Root.position.y > (bottomStand.y + topStand.y) * 0.5f;
        }

        public string GetPrompt()
        {
            if (!CanInteract()) return null;
            if (PlayerAtTop()) return rope ? PropPrompt.Interact("Strickleiter runter", "Climb down the rope ladder") : PropPrompt.Interact("Runterklettern", "Climb down");
            if (waterBottom) return PropPrompt.Interact("Rausklettern", "Climb out");
            return rope ? PropPrompt.Interact("Strickleiter hoch", "Climb the rope ladder") : PropPrompt.Interact("Hochklettern", "Climb up");
        }

        public bool CanInteract()
        {
            if (PropInfo.LaddersHandledByPlayer) return false;
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
            if (!PropInfo.LaddersHandledByPlayer && p != null && p.Root != null && GameManager.I != null && GameManager.I.InGame && !UIState.BlocksGameplay && !p.IsSeated && !p.IsFainting)
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

    /// Story texts of the collectibles (H.2 / L.6: Onkel Gus' logbook in 4 chapters, 13 secrets, 14 treasures,
    /// 3 messages in a bottle). Ids stay stable across versions (saved collected lists).
    public static class PropLore
    {
        public const int PageCount = 24;
        public const int ChapterCount = 4;

        static readonly string[] PageDe =
        {
            // ---- chapter 1 „Rostbucht“
            "Tag 1 auf Rostbucht. Hütte für drei Kokosnüsse gekauft – von den Leuten im Dschungel. Zwei waren hohl. Sie lachen heute noch. Lautlos.",
            "Mein erstes Video: 4 Aufrufe. Drei davon war ich. Der vierte war Horst.",
            "Gerald die Schildkröte hat mir heute zugewinkt. Glaube ich. Horst sagt, das war ein Krampf.",
            "Die Bottom-Brüder parken schon wieder ihre Yacht über meinem Riff. Ich hab Fischbrötchen in ihren Whirlpool geworfen.",
            "Im Wrack singt nachts irgendwas. Ich war damals der Smutje. Ich singe nicht. Also wer?",
            "Mein Versteck für die guten Bänder: hinter dem Vorhang aus Wurzeln, wo das Wasser rauscht. Bücken nicht vergessen!",
            // ---- chapter 2 „Live aus der Tiefe“
            "Neuer Plan: eine echte Tiefsee-Doku. Live! Titel: „Live aus der Tiefe“. Kanal Tiefsee hat Ja gesagt!",
            "Sie bauen ein Unterwasser-Studio. Es hängt an Ketten über dem Graben. Der Techniker sagt, das hält. Bestimmt.",
            "Basislager neben der Schlucht aufgebaut. Toni, unser Tonmann, nimmt alles auf. Sogar mein Schnarchen.",
            "Probelauf. Ganz unten im Graben hat mich etwas angeschaut. Ein Auge. Nur ein riesiges Auge.",
            "Wanda hat die Bänder abgeholt. Sie fliegt wie eine Möwe mit Schluckauf.",
            "Morgen ist die Live-Sendung. Die Dschungelleute klopfen auf ihr Gezeitenbrett und schütteln den Kopf: schlechte Nacht. Ich sage: Quote!",
            // ---- chapter 3 „Die Augennacht“
            "Live-Sendung: erst Rauschen, dann Stille. Die Ketten sind weg. Das Studio auch. Das Meer war hell. Von unten. Im Dschungel spricht seitdem keiner mehr.",
            "Keiner glaubt mir das mit dem Auge. Kanal Tiefsee hat die Doku gestrichen. Und Toni ist verschwunden.",
            "Blitz im Funkturm. Oder etwas anderes. Seitdem knistert das alte Funkgerät da oben. Ich frage lieber nicht.",
            "Ich habe die Bänder versteckt. Wer sie findet, erzählt die Geschichte zu Ende.",
            "Die Leuchtturm-Linse ist gesprungen. Drei Scherben: ganz oben, in der Grotte, beim Funkturm. Fragt nicht.",
            "Notgroschen ins Sparschwein, Sparschwein in die Schlucht. Da sucht keiner. Außer Horst. Horst sucht überall.",
            // ---- chapter 4 „Das Auge wartet“
            "Kapitänskajüte der MS Clickbait. Käpt'n Kurt hatte einen Dreispitz. Und keinen Humor.",
            "Den Kajütenschlüssel gab der Käpt'n immer den Muränen im Laderaum. Die rücken ihn nur im Dunkeln raus.",
            "Auf der Kelp-Insel haben Horst und ich unsere Namen in einen Baum geritzt. 1989. Er hat sich verschrieben.",
            "Von der Blauwasser-Insel sieht man nachts ein Blinken im Meer. Die Funkboje vom Studio? Piept die noch?",
            "Auf der Graben-Insel steht ein Fernseher am Strand. Nachts zeigt er Rauschen. Ich hab ihn nie eingesteckt.",
            "Wenn eine Million Leute zuschauen, zeigt es sich vielleicht. Das Auge. Bring's zu Ende, Kind. – Gus",
        };

        static readonly string[] PageEn =
        {
            "Day 1 on Rust Cove. Bought the hut for three coconuts – from the jungle folk. Two were hollow. They are still laughing. Silently.",
            "My first video: 4 views. Three of them were me. The fourth was Horst.",
            "Gerald the turtle waved at me today. I think. Horst says it was a cramp.",
            "The Bottom brothers parked their yacht over my reef again. I threw fish rolls into their hot tub.",
            "Something sings in the wreck at night. I was the ship's cook back then. I don't sing. So who?",
            "My hiding place for the good tapes: behind the curtain of roots, where the water roars. Remember to duck!",
            "New plan: a real deep-sea documentary. Live! Title: 'Live from the Deep'. Deep Sea TV said yes!",
            "They're building an underwater studio. It hangs on chains above the trench. The technician says it'll hold. Surely.",
            "Set up base camp by the ravine. Tony, our sound guy, records everything. Even my snoring.",
            "Test run. Deep down in the trench something looked at me. An eye. Just one giant eye.",
            "Wanda picked up the tapes. She flies like a seagull with hiccups.",
            "Tomorrow is the live broadcast. The jungle folk tap their tide board and shake their heads: bad night. I say: ratings!",
            "Live broadcast: first static, then silence. The chains are gone. So is the studio. The sea was bright. From below. Nobody in the jungle has spoken since.",
            "Nobody believes me about the eye. Deep Sea TV cancelled the documentary. And Tony vanished.",
            "Lightning hit the radio tower. Or something else. Since then the old radio up there crackles. I'd rather not ask.",
            "I hid the tapes. Whoever finds them gets to finish the story.",
            "The lighthouse lens cracked. Three shards: at the very top, in the grotto, at the radio tower. Don't ask.",
            "Emergency cash into the piggy bank, piggy bank into the ravine. Nobody looks there. Except Horst. Horst looks everywhere.",
            "Captain's cabin of the MS Clickbait. Captain Kurt had a tricorn. And no sense of humour.",
            "The captain always gave the cabin key to the morays in the hold. They only hand it over in the dark.",
            "On the kelp island Horst and I carved our names into a tree. 1989. He misspelled his.",
            "From the blue-water island you see a blinking in the sea at night. The studio's radio buoy? Is it still beeping?",
            "On the trench island there's a TV on the beach. At night it shows static. I never plugged it in.",
            "If a million people are watching, it might show itself. The eye. Finish it, kid. – Gus",
        };

        static readonly string[] ChapterDe = { "Rostbucht", "Live aus der Tiefe", "Die Augennacht", "Das Auge wartet" };
        static readonly string[] ChapterEn = { "Rust Cove", "Live from the Deep", "The Night of the Eye", "The Eye Is Waiting" };

        public static string PageId(int index) => Collectibles.Id(CollectibleKind.LogbookPage, "p" + (index + 1).ToString("00"));
        public static string Page(int index) => Loc.Tr(PageDe[Mathf.Clamp(index, 0, PageCount - 1)], PageEn[Mathf.Clamp(index, 0, PageCount - 1)]);
        /// Chapter (0..3) of a page (6 pages each) – the phone's „Gus' Logbuch“ shows them in 4 chapters.
        public static int Chapter(int pageIndex) => Mathf.Clamp(pageIndex / 6, 0, ChapterCount - 1);
        public static string ChapterTitle(int chapter) => Loc.Tr(ChapterDe[Mathf.Clamp(chapter, 0, ChapterCount - 1)], ChapterEn[Mathf.Clamp(chapter, 0, ChapterCount - 1)]);

        // ---------------------------------------------------------------- messages in a bottle (day 3 / 6 / 10)
        public const string BottleKey = "gus";
        public static readonly string[] BottleKeys = { "gus", "gus2", "gus3" };
        public static readonly int[] BottleDays = { 3, 6, 10 };

        static readonly string[] BottleDe =
        {
            "Flaschenpost: „Das TV-Studio ist im Graben verschwunden. Gus' Filmrolle liegt beim alten Funkturm. – T.“",
            "Flaschenpost: „Das Studio hat in der letzten Nacht nicht geblinkt. Etwas anderes hat geblinkt. – T.“",
            "Flaschenpost: „Folgt dem Strahl des Leuchtturms. Zweiundneunzig Meter. Da steht die alte Antenne. – T.“",
        };

        static readonly string[] BottleEn =
        {
            "Message in a bottle: 'The TV studio vanished in the trench. Gus's film reel is at the old radio tower. – T.'",
            "Message in a bottle: 'On the last night the studio didn't blink. Something else did. – T.'",
            "Message in a bottle: 'Follow the lighthouse beam. Ninety-two metres. The old antenna stands there. – T.'",
        };

        public static string BottleText => Loc.Tr(BottleDe[0], BottleEn[0]);
        public static string BottleId(int i) => Collectibles.Id(CollectibleKind.BottleMessage, BottleKeys[Mathf.Clamp(i, 0, BottleKeys.Length - 1)]);

        public struct Named { public string key, nameDe, nameEn, textDe, textEn; }

        /// L.6: 13 secrets (the keys of the first four and wreck_cabin are referenced by saves and cosmetics rules).
        public static readonly Named[] Secrets =
        {
            new Named { key = "cave_grotto", nameDe = "Gus' geheime Filmecke", nameEn = "Gus's secret film corner",
                textDe = "Hier hat Onkel Gus seine Doku geschnitten. Die alte Kamera läuft noch!", textEn = "This is where Uncle Gus edited his documentary. The old camera still runs!" },
            new Named { key = "waterfall", nameDe = "Versteck hinter dem Wasserfall", nameEn = "Hideout behind the waterfall",
                textDe = "Gus' Notvorrat: drei Dosen Kokoskekse und ein Zettel: „Nicht für Gerald!“", textEn = "Gus's emergency stash: three tins of coconut cookies and a note: \"Not for Gerald!\"" },
            new Named { key = "lighthouse_top", nameDe = "Ganz oben", nameEn = "At the very top",
                textDe = "In die Lüftungshaube geritzt: „G + Gerald, für immer“.", textEn = "Carved into the vent cap: \"G + Gerald, forever\"." },
            new Named { key = "radio_tower", nameDe = "Das alte Funkgerät", nameEn = "The old radio",
                textDe = "Es knistert: „…hier Gus … das Studio … tief unten im Graben … die Rolle … hier am Turm …“", textEn = "It crackles: \"...this is Gus ... the studio ... deep down in the trench ... the reel ... here at the tower ...\"" },
            new Named { key = "ravine_piggy", nameDe = "Gus' Sparschwein", nameEn = "Gus's piggy bank",
                textDe = "Ein rosa Sparschwein mit 150 $ und einem Zettel: „Für schlechte Tage. Oder Fischbrötchen.“", textEn = "A pink piggy bank with $150 and a note: \"For bad days. Or fish rolls.\"" },
            new Named { key = "wreck_cabin", nameDe = "Die Kapitänskajüte", nameEn = "The captain's cabin",
                textDe = "Der Piraten-Dreispitz von Käpt'n Kurt. Riecht nach Käse.", textEn = "Captain Kurt's pirate tricorn. Smells of cheese." },
            new Named { key = "kelp_carving", nameDe = "„G + H 1989“", nameEn = "\"G + H 1989\"",
                textDe = "In die Rinde geritzt: „G + H 1989“. Das H steht auf dem Kopf.", textEn = "Carved into the bark: \"G + H 1989\". The H is upside down." },
            new Named { key = "wreck_hammock", nameDe = "Gus' Hängematte", nameEn = "Gus's hammock",
                textDe = "Eine Hängematte in einer Meereshöhle. Auf dem Kissen: „Mittagsschlaf – nicht stören“.", textEn = "A hammock in a sea cave. The pillow says: \"Nap time – do not disturb\"." },
            new Named { key = "blue_buoy", nameDe = "Die Studio-Funkboje", nameEn = "The studio radio buoy",
                textDe = "Eine alte Funkboje mit „KANAL TIEFSEE“-Aufkleber. Sie piept noch – Richtung Graben.", textEn = "An old radio buoy with a \"DEEP SEA TV\" sticker. It still beeps – toward the trench." },
            new Named { key = "trench_tv", nameDe = "Der Fernseher am Strand", nameEn = "The TV on the beach",
                textDe = "Ein alter Fernseher unter Palmwedeln. Nachts zeigt er Rauschen – und ganz kurz ein Auge.", textEn = "An old TV under palm fronds. At night it shows static – and, for a moment, an eye." },
            new Named { key = "lighthouse_cellar", nameDe = "Gus' Keller", nameEn = "Gus's cellar",
                textDe = "Sendepläne von 1987, eine Seekarte mit einem Kreuz im Graben und ein Zettel: „Folge dem Strahl.“", textEn = "Broadcast plans from 1987, a chart with a cross in the trench and a note: \"Follow the beam.\"" },
            new Named { key = "village_loft", nameDe = "Gus' Schuldscheine", nameEn = "Gus's IOUs",
                textDe = "Eine Zigarrenkiste voller Schuldscheine von Gus an Horst. Summe: 412 Fischbrötchen. Angespült in der Truhe der Einsiedler.", textEn = "A cigar box full of IOUs from Gus to Horst. Total: 412 fish rolls. Washed up in the hermits' chest." },
            new Named { key = "fog_archive", nameDe = "Tonis Tonarchiv", nameEn = "Tony's sound archive",
                textDe = "Kassetten in einer Luftblase: „Singende Fische, Vol. 1–40“ – und eine: „Das Auge. NICHT abspielen.“", textEn = "Tapes in an air pocket: \"Singing Fish, Vol. 1–40\" – and one: \"The Eye. DO NOT PLAY.\"" },
        };

        /// H.2: 14 treasures.
        public static readonly Named[] Treasures =
        {
            new Named { key = "bay_chest", nameDe = "Piratenkiste", nameEn = "Pirate chest" },
            new Named { key = "bird_nest", nameDe = "Goldenes Ei", nameEn = "Golden egg" },
            new Named { key = "diving_helmet", nameDe = "Alter Taucherhelm", nameEn = "Old diving helmet" },
            new Named { key = "bridge_coins", nameDe = "Goldmünzen", nameEn = "Gold coins" },
            new Named { key = "wreck_compass", nameDe = "Kompass der MS Clickbait", nameEn = "MS Clickbait compass" },
            new Named { key = "film_reel", nameDe = "Gus' verlorene Filmrolle", nameEn = "Gus's lost film reel" },
            new Named { key = "hold_gold", nameDe = "Goldbarren aus dem Laderaum", nameEn = "Gold bar from the hold" },
            new Named { key = "hold_cheese", nameDe = "Goldener Käse-Pokal", nameEn = "Golden cheese trophy" },
            new Named { key = "hold_safe", nameDe = "Tresor des Käpt'ns", nameEn = "The captain's safe" },
            new Named { key = "kelp_chest", nameDe = "Gus' festgeklemmte Seekiste", nameEn = "Gus's stuck sea chest" },
            new Named { key = "drop_ledge", nameDe = "Messing-Fernrohr", nameEn = "Brass spyglass" },
            new Named { key = "trench_camera", nameDe = "Alte TV-Kamera", nameEn = "Old TV camera" },
            new Named { key = "bay_cave", nameDe = "Schmuggler-Laterne", nameEn = "Smuggler's lantern" },
            new Named { key = "cellar_mic", nameDe = "Goldenes Mikrofon", nameEn = "Golden microphone" },
        };

        public static int SecretIndex(string key)
        {
            for (int i = 0; i < Secrets.Length; i++) if (Secrets[i].key == key) return i;
            return -1;
        }

        public static int TreasureIndex(string key)
        {
            for (int i = 0; i < Treasures.Length; i++) if (Treasures[i].key == key) return i;
            return -1;
        }

        /// Localized story text of a collectible id (pages, secrets, bottles) or null. For the phone UI.
        public static string TextFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < PageCount; i++) if (id == PageId(i)) return Page(i);
            for (int i = 0; i < BottleKeys.Length; i++) if (id == BottleId(i)) return Loc.Tr(BottleDe[i], BottleEn[i]);
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
    /// with a subtle sparkle (within 12 m, Q62); hidden once its id is in the shared collected list (re-checked when
    /// the state changes – co-op clients get the list from the host). Secrets (L.6) have no sparkle, only a faint glint
    /// within 2.5 m, and keep their object (only the glint stops).
    public class PropPickup : MonoBehaviour, IInteractable
    {
        public const float SparkleRange = 12f, SecretGlintRange = 2.5f;

        public string id;
        public CollectibleKind kind;
        public int pageIndex = -1;
        public GameObject model;
        public bool keepModel;
        /// Not available before this in-game day (messages in a bottle: 3 / 6 / 10).
        public int requireDay;
        /// Extra condition (story gate: a story flag that must be set, e.g. the wreck cabin key). Null = none.
        public string requireFlag;
        /// Money paid with the pickup (Gus' piggy bank: 150 $).
        public long money;
        public ParticleSystem sparkle;
        public Collider trigger;

        bool shown = true, sparkleOn;

        public bool requireDay3 { get => requireDay >= 3; set => requireDay = value ? 3 : 0; }

        public bool Available()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) return requireDay <= 1 && string.IsNullOrEmpty(requireFlag);
            if (Collectibles.IsCollected(id)) return false;
            if (!string.IsNullOrEmpty(requireFlag) && !StoryFlags.Has(requireFlag)) return false;
            return requireDay <= 1 || gm.State.day >= requireDay;
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
            // round 3: the finder's own copy goes into the backpack's collection pouch (secrets stay where they are)
            string item = ItemDatabase.ForCollectible(kind);
            if (item != null) Backpack.Add(item, 1, false);
            if (money > 0 && GameManager.I != null)
            {
                GameManager.I.AddMoney(money);
                GameEvents.RaiseNotify(NotifyKind.Money, PropLore.NameFor(id, kind), "+" + Loc.Money(money));
            }
            string text = kind == CollectibleKind.LogbookPage && pageIndex >= 0 ? PropLore.Page(pageIndex) : PropLore.TextFor(id);
            if (!string.IsNullOrEmpty(text))
            {
                string title = kind == CollectibleKind.LogbookPage ? Loc.Tr("Onkel Gus' Logbuch", "Uncle Gus's logbook")
                    : kind == CollectibleKind.BottleMessage ? Collectibles.KindName(kind) : PropLore.NameFor(id, kind);
                GameEvents.RaiseBanner(title + "\n" + text, new Color(0.96f, 0.91f, 0.78f));
            }
            Refresh(PropCam.Valid, PropCam.Position);
        }

        public void Refresh(bool camValid, Vector3 cam)
        {
            bool avail = Available();
            bool gateOpen = string.IsNullOrEmpty(requireFlag) || StoryFlags.Has(requireFlag);
            float d2 = camValid ? (cam - transform.position).sqrMagnitude : 0f;
            // the small models are only drawn within 140 m (distance activation); story-gated ones only when open;
            // secrets keep their object after discovery
            bool show = gateOpen && (avail || keepModel) && d2 < 140f * 140f;
            if (show != shown)
            {
                shown = show;
                if (model != null) model.SetActive(show);
            }
            if (trigger != null && trigger.enabled != avail) trigger.enabled = avail;
            float range = kind == CollectibleKind.Secret ? SecretGlintRange : SparkleRange;
            bool spark = avail && camValid && d2 < range * range;
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

    /// Keeps all collectibles in sync with the shared state (periodic + event driven) and places the messages in a
    /// bottle at beach spots chosen by GameState.saveSeed (identical on all co-op machines).
    public sealed class PropCollectibleManager : MonoBehaviour
    {
        public readonly List<PropPickup> pickups = new List<PropPickup>();
        /// The three messages in a bottle (day 3 / 6 / 10).
        public readonly List<PropPickup> bottles = new List<PropPickup>();
        public PropPickup bottle { get => bottles.Count > 0 ? bottles[0] : null; set { if (value != null && !bottles.Contains(value)) bottles.Add(value); } }
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
            GameEvents.StoryFlagSet += OnFlag;
        }

        void OnDisable()
        {
            Collectibles.Found -= OnFound;
            Net.StateSynced -= RefreshAll;
            GameEvents.GameStarted -= RefreshAll;
            GameEvents.DayStarted -= OnDay;
            GameEvents.ReturnedToMenu -= RefreshAll;
            GameEvents.StoryFlagSet -= OnFlag;
        }

        void OnFound(string id, CollectibleKind kind) => RefreshAll();
        void OnDay(int day) => RefreshAll();
        void OnFlag(string flag) => RefreshAll();

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            RefreshAll();
        }

        public void RefreshAll()
        {
            PlaceBottles();
            bool valid = PropCam.Valid;
            Vector3 cam = PropCam.Position;
            for (int i = 0; i < pickups.Count; i++)
                if (pickups[i] != null) pickups[i].Refresh(valid, cam);
        }

        void PlaceBottles()
        {
            if (bottles.Count == 0 || bottleSpots.Count == 0 || GameManager.I == null) return;
            var st = GameManager.I.State;
            if (st == null || st.saveSeed == bottleSeed) return;
            bottleSeed = st.saveSeed;
            var r = st.SeededRandom(7331);
            int first = r.Next(bottleSpots.Count);
            for (int i = 0; i < bottles.Count; i++)
            {
                if (bottles[i] == null) continue;
                // three different beach spots, spread around the island
                int k = (first + i * Mathf.Max(1, bottleSpots.Count / 3) + (i > 0 ? r.Next(3) : 0)) % bottleSpots.Count;
                float yaw = (float)r.NextDouble() * 360f;
                bottles[i].transform.SetPositionAndRotation(bottleSpots[k], Quaternion.Euler(0f, yaw, 0f));
            }
        }
    }
}
