using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// One spoken line (≤ 90 characters per language, humour from the situation – L.7 respect rules).
    public sealed class LineDef
    {
        public string id, speaker, de, en;
        /// "clue" for secret / treasure clues (L.6), "sign" for the hermits' lines (described gestures – they never
        /// speak words, GDD §20 Q5).
        public string tag;
        public string Text => Loc.Tr(de, en);
    }

    /// Every NPC / radio line of the game (A, B, K.5, K.8, L.6, L.7, L.12 + round 3). Lines are addressed by id and
    /// resolved on every machine in its own language (Dialogue.Resolve → Resolve). Conversations are id prefixes:
    /// Seq("horst.b1") returns horst.b1.1, horst.b1.2 … in order.
    /// Tokens: {0} {1} … = arguments; {K:Action} = "[key]" of a GameAction; {S:Item} = "[key]" of the hotbar slot that
    /// holds a HoldItem (Camera / FishingRod / Flashlight) – both follow the player's key bindings.
    ///
    /// Round 3 story (GDD §20 Q9 – Uncle Gus' inheritance, coherent from the first minute):
    ///  • 1987 Gus (once the cook of the MS Clickbait, then a TV maker) shot „Live aus der Tiefe“ for Kanal Tiefsee: an
    ///    underwater studio on chains above the trench. Toni recorded the sound, Wanda flew the tapes. During the live
    ///    broadcast – the Night of the Eye – the chains broke, the studio sank, the sea shone from below and a giant
    ///    eye looked into the camera. Nobody believed Gus, the show was cancelled, Toni drifted away.
    ///  • The dropouts in the jungle (the hermits) watched that night from their lookout rock. Since then they revere
    ///    the sea, keep a vow of silence and talk only in signs – and distrust strangers with cameras.
    ///  • Now Gus has retired and left the player the island, the hut, the boat and his channel: grow it to a million
    ///    followers, then go live from the deep and finish the documentary. Horst (his old harbourmaster friend) helps.
    public static class DialogueDatabase
    {
        static readonly Dictionary<string, LineDef> lines = new Dictionary<string, LineDef>(1200);
        static readonly Dictionary<string, List<string>> seqs = new Dictionary<string, List<string>>(320);
        static readonly List<string> empty = new List<string>();
        static bool built;

        public static int Count { get { Build(); return lines.Count; } }

        public static LineDef Get(string id)
        {
            Build();
            return id != null && lines.TryGetValue(id, out var l) ? l : null;
        }

        public static bool Has(string id) => Get(id) != null;

        /// Ordered line ids of a conversation prefix ("horst.b1" → horst.b1.1 …). Empty list when unknown.
        public static List<string> Seq(string prefix)
        {
            Build();
            return prefix != null && seqs.TryGetValue(prefix, out var l) ? l : empty;
        }

        public static string SpeakerOf(string id) { var l = Get(id); return l != null ? l.speaker : null; }

        /// Every line with a tag (designer read).
        public static void ByTag(string tag, List<LineDef> into)
        {
            Build();
            into.Clear();
            foreach (var kv in lines) if (kv.Value.tag == tag) into.Add(kv.Value);
        }

        /// Every line (tests: story coherence checks).
        public static void AllLines(List<LineDef> into)
        {
            Build();
            into.Clear();
            foreach (var kv in lines) into.Add(kv.Value);
        }

        // ---------------------------------------------------------------- resolve
        static readonly StringBuilder sb = new StringBuilder(256);

        /// Dialogue.Resolve: line id + args → text in the current language (tokens replaced). Unknown ids are returned as-is.
        public static string Resolve(string id, string[] args)
        {
            var l = Get(id);
            if (l == null) return id;
            string text = l.Text;
            if (text.IndexOf('{') >= 0) text = Expand(text, args);
            return text;
        }

        /// Replaces {K:…} / {S:…} key tokens in any text (quest lines, hints).
        public static string ExpandTokens(string text) => string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 ? text : Expand(text, null);

        static string Expand(string text, string[] args)
        {
            sb.Clear();
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '{')
                {
                    int close = text.IndexOf('}', i + 1);
                    if (close > i)
                    {
                        string tok = text.Substring(i + 1, close - i - 1);
                        sb.Append(Token(tok, args));
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static string Token(string tok, string[] args)
        {
            if (tok.Length > 2 && tok[1] == ':')
            {
                string name = tok.Substring(2);
                if (tok[0] == 'K' && Enum.TryParse(name, out GameAction a)) return "[" + GameInput.BindingLabel(a) + "]";
                if (tok[0] == 'S' && Enum.TryParse(name, out HoldItem item)) return SlotKey(item);
                return "";
            }
            if (int.TryParse(tok, out int n)) return args != null && n >= 0 && n < args.Length ? args[n] ?? "" : "";
            return "{" + tok + "}";
        }

        static string SlotKey(HoldItem item)
        {
            if (!Hands.Find(item, out int slot)) return "[" + GameInput.BindingLabel(GameAction.Inventory) + "]";
            return "[" + GameInput.BindingLabel(GameInput.SlotAction(slot)) + "]";
        }

        /// Seconds a line stays up (≈ 3–4 s for a normal line, longer lines a bit more).
        public static float SecondsFor(string text) => Mathf.Clamp(1.3f + (text != null ? text.Length : 20) * 0.058f, 2.6f, 6.5f);

        // ---------------------------------------------------------------- data
        static void L(string id, string speaker, string de, string en, string tag = null)
        {
            var l = new LineDef { id = id, speaker = speaker, de = de, en = en, tag = tag };
            lines[id] = l;
            int dot = id.LastIndexOf('.');
            if (dot <= 0) return;
            string prefix = id.Substring(0, dot);
            if (!seqs.TryGetValue(prefix, out var list)) { list = new List<string>(6); seqs[prefix] = list; }
            list.Add(id);
        }

        /// Several lines of one speaker: prefix.1 … prefix.n from (de, en) pairs.
        static void S(string prefix, string speaker, string tag, params string[] deEn)
        {
            for (int i = 0; i + 1 < deEn.Length; i += 2) L(prefix + "." + (i / 2 + 1), speaker, deEn[i], deEn[i + 1], tag);
        }

        const string Clue = "clue", Sign = "sign";
        const string Horst = NpcDatabase.Horst, Gus = NpcDatabase.Gus, Wanda = NpcDatabase.Wanda, Fin = NpcDatabase.Fin, Chad = NpcDatabase.Chad, Brad = NpcDatabase.Brad,
            Toni = NpcDatabase.Toni,
            HL = NpcDatabase.HLeader, HS = NpcDatabase.HSmith, HH = NpcDatabase.HHunter, HC = NpcDatabase.HCook, HO = NpcDatabase.HLookout, HG = NpcDatabase.HGardener;

        static void Build()
        {
            if (built) return;
            built = true;
            Prologue();
            HorstLines();
            GusLines();
            WandaLines();
            FinLines();
            RivalLines();
            HermitLines();
            ToniLines();
            ClueLines();
            ReactionLines();
            SideQuestLines();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { }

        // ================================================================ K.5 prologue
        static void Prologue()
        {
            S("pro.p1", Wanda, null,
                "Endstation Rostbucht! Gepäck? Ach, du hast keins. Sehr modern.", "Last stop, Rust Cove! Luggage? Oh, you don't have any. Very modern.",
                "Ich bin Wanda. Was du im Shop bestellst, bring ich. Meistens heil.", "I'm Wanda. Whatever you order in the shop, I deliver. Mostly in one piece.");
            S("pro.p2", Horst, null,
                "Moin! Du bist Gus' Erbe! Willkommen auf Rostbucht!", "Ahoy! You're Gus's heir! Welcome to Rust Cove!",
                "Die Insel gehört jetzt dir. Hütte, Boot – und sein alter Kanal.", "The island is yours now. Hut, boat – and his old channel.",
                "Gus hat dir einen Brief dagelassen. Ich hab ihn NICHT gelesen. Zweimal.", "Gus left you a letter. I did NOT read it. Twice.");
            S("pro.p3", Horst, null,
                "Eine Million! Das schaffen wir. Ich hab schon 12 Follower. Drei davon sind echt.", "A million! We can do it. I've got 12 followers already. Three are real.",
                "Hier sind deine Aufgaben. Hauptziel oben, Kleinkram darunter.", "Here are your tasks. Main goal on top, small stuff below.",
                "Erst mal zur Hütte. Folg mir!", "First, to the hut. Follow me!");
            S("pro.p3hut", Horst, null,
                "Eine Million! Das schaffen wir. Ich hab schon 12 Follower. Drei davon sind echt.", "A million! We can do it. I've got 12 followers already. Three are real.",
                "Hier sind deine Aufgaben. Hauptziel oben, Kleinkram darunter.", "Here are your tasks. Main goal on top, small stuff below.",
                "Und das ist deine Hütte. Gemütlich, oder? Fast.", "And this is your hut. Cozy, right? Almost.");
            S("pro.bye", Wanda, null,
                "Ich flieg zurück. Bestell was, dann komm ich wieder!", "I'm heading back. Order something and I'll be back!");
            L("pro.letter.title", Gus, "Ein Brief von Onkel Gus", "A letter from Uncle Gus");
            L("pro.letter.body", Gus,
                "Kind, die Insel gehört jetzt dir – Hütte, Boot und mein Kanal. 1987 ist mein Fernsehstudio mitten in der Live-Sendung im Graben versunken, " +
                "und was ich da unten gesehen hab, glaubt mir keiner. Bring den Kanal auf eine Million Follower, dann geh live in die Tiefe und dreh die Doku zu Ende. " +
                "Horst hilft dir. Glaub ihm die Hälfte. – Gus",
                "Kid, the island is yours now – hut, boat and my channel. In 1987 my TV studio sank into the trench in the middle of a live show, " +
                "and nobody believes what I saw down there. Get the channel to a million followers, then go live from the deep and finish the documentary. " +
                "Horst will help. Believe half of it. – Gus");
        }

        // ================================================================ A Horst (round 3: he lives in his beach hammock)
        static void HorstLines()
        {
            S("horst.b0", Horst, null,
                "Gus sagt, du willst Fische berühmt machen. Ich sage: ENGAGEMENT!", "Gus says you want to make fish famous. I say: ENGAGEMENT!",
                "Erst mal Handy auf – hier läuft alles übers Handy. {K:Phone}", "First, open your phone – everything runs through it. {K:Phone}");
            S("horst.b1", Horst, null,
                "Kein Geld, keine Luft. Keine Luft, kein Tauchen. Also: Angeln!", "No money, no air. No air, no diving. So: fishing!",
                "Angel nehmen {S:FishingRod}. {K:Use} halten, loslassen – zack, Wurf.", "Take the rod {S:FishingRod}. Hold {K:Use}, let go – whoosh, cast.",
                "Taucht der Schwimmer ab: {K:Use}! Dann einholen – nicht zu stramm.", "Bobber goes under: {K:Use}! Then reel in – not too tight.",
                "Fünf Fische. Ich zähl mit. Vom Strand aus. Der Steg gehört dir.", "Five fish. I'm counting. From the beach. The dock is all yours.");
            L("horst.react.catch", Horst, "Ein Fisch! Der hat bestimmt drei Follower.", "A fish! He's got at least three followers.");
            L("horst.react.snap", Horst, "Zu stramm! Die Schnur ist auch nur ein Mensch.", "Too tight! The line's only human.");
            L("horst.react.boot", Horst, "Ein Stiefel! Vintage. Verkauft sich trotzdem.", "A boot! Vintage. Still sells.");
            S("horst.b2", Horst, null,
                "Wunderbar. Die liegen jetzt in deiner Kühlbox und denken über ihr Leben nach.", "Wonderful. They're in your cooler now, reflecting on their lives.",
                "Handy → Fischmarkt → Anbieten. Die Leute kaufen alles. Wirklich alles.", "Phone → Fish Market → List. People buy anything. Anything.");
            S("horst.b3", Horst, null,
                "Ka-tsching! Weiter angeln, bis du 100 $ hast.", "Ka-ching! Keep fishing until you've got $100.",
                "Dann im Chopper Shop die Pressluftflasche. Der Heli liefert. Meistens aufs Dach.", "Then buy the air tank in the Chopper Shop. The chopper delivers. Usually onto the roof.");
            S("horst.b4", Horst, null,
                "Hörst du das? Nein? Ich auch nicht. Kommt aber gleich!", "Hear that? No? Me neither. It's coming though!",
                "Paket am Landeplatz öffnen. Nicht schütteln.", "Open the crate on the helipad. Don't shake it.");
            S("horst.b5", Horst, null,
                "Jetzt das Wichtigste: CONTENT!", "Now the most important thing: CONTENT!",
                "Kamera nehmen {S:Camera}. Unter Wasser: {K:Use} – Aufnahme läuft. Alles, was schwimmt, zählt.", "Take the camera {S:Camera}. Under water: {K:Use} – recording. Everything that swims counts.",
                "Fürs Sealog: {K:Aim} halten, nah ran, Klick – Foto!", "For the Sealog: hold {K:Aim}, get close, click – photo!",
                "Drei Arten fotografieren, gleich vorm Steg. Und: Luft im Blick!", "Photograph three species, right off the dock. And: watch your air!");
            S("horst.b6", Horst, null,
                "Das war … WUNDERSCHÖN. Ich hab geweint. Innerlich.", "That was… BEAUTIFUL. I cried. On the inside.",
                "Handy → FinTube → Hochladen. Ein Video pro Tag – das liebt der Algorithmus.", "Phone → FinTube → Upload. One video a day – the algorithm loves that.",
                "Denk dir einen Titel aus – und ein Vorschaubild mit vielen Tieren bringt Klicks.", "Make up a title – and a thumbnail with lots of animals gets clicks.",
                "Glaub mir, ich hab Ahnung.", "Trust me, I know things.");
            S("horst.b7", Horst, null,
                "Die Aufrufe kommen über Nacht. Ins Bett darfst du ab 18 Uhr.", "Views come in overnight. Bedtime is from 6 pm.",
                "Bis dahin: Fisch aufs Lagerfeuer, Meerwasser in den Filter, Insel erkunden.", "Until then: fish on the campfire, seawater into the filter, explore the island.",
                "Ich lieg in meiner Hängematte am Strand. Professionell.", "I'll be in my hammock on the beach. Professionally.");
            S("horst.b8", Horst, null,
                "Schau dir das an! Follower! Echte! Vermutlich!", "Look at that! Followers! Real ones! Probably!",
                "FinTube zahlt sogar schon ein bisschen. Reich sind wir nächste Woche.", "FinTube even pays a little already. We'll be rich next week.",
                "Hier, mein Köderhut. Hat Glück gebracht. Mir nicht, aber anderen.", "Here, my lure hat. It brought luck. Not to me, but to others.",
                "Bei 1.000 Followern gehen wir LIVE – da spenden Leute echtes Geld!", "At 1,000 followers we go LIVE – people donate real money!");
            S("horst.b9", Horst, null,
                "TAUSEND! Weißt du, was das heißt? KRILL.tv!", "A THOUSAND! You know what that means? KRILL.tv!",
                "Und sie spenden Geld! Für Mut. Für Haie. Für Kämpfe.", "And they donate money! For courage. For sharks. For fights.",
                "Stream starten auf dem Boot: Handy → KRILL.tv.", "Start the stream on the boat: phone → KRILL.tv.",
                "Ohnmacht = Stream weg, Follower weg. Kein Druck! Wasserdruck. Los!", "Faint = stream gone, followers gone. No pressure! Water pressure. Go!");
            // idle gags (A.1) – round 3: in and around the hammock, never on the dock
            L("horst.gag.ow", Horst, "Au.", "Ow.");
            L("horst.gag.sandwich", Horst, "GÜNTHER! Das war mein Fischbrötchen!", "GÜNTHER! That was my fish roll!");
            L("horst.gag.whistle", Horst, "Hey! Hier wird nicht gedrängelt! Ich bin der Hafenmeister! Im Ruhestand!", "Hey! No pushing! I'm the harbourmaster! Retired!");
            L("horst.gag.selfie", Horst, "Hm. Gutes Licht. Ich seh aus wie ein Profi. Du auch. Fast.", "Hm. Good light. I look like a pro. You too. Almost.");
            L("horst.gag.awake", Horst, "Bin wach! Algorithmus!", "Awake! Algorithm!");
            L("horst.gag.whoa", Horst, "Huuuuch –", "Whoaaaa –");
            L("horst.gag.planned", Horst, "Geplant.", "Planned.");
            // small talk (rotating, with real tips)
            S("horst.talk", Horst, null,
                "Ich lieg meistens in meiner Hängematte südlich vom Steg. Ruf einfach. Oder bring Fischbrötchen.", "I'm mostly in my hammock south of the dock. Just shout. Or bring fish rolls.",
                "Gus hat dir nicht nur die Hütte vererbt. Auch den Kanal. Eine Million – dann geht's live in die Tiefe.", "Gus didn't just leave you the hut. The channel too. A million – then we go live from the deep.",
                "Je tiefer, desto seltener die Tiere. Und desto teurer die Flasche.", "The deeper you go, the rarer the animals. And the pricier the tank.",
                "Hunger? Roher Fisch aufs Lagerfeuer. Durst? Meerwasser in den Filter in der Hütte.", "Hungry? Raw fish on the campfire. Thirsty? Seawater into the filter in the hut.",
                "Der Algorithmus mag Haie. Und Kämpfe. Hauptsache, keiner wird nass. Also du schon.", "The algorithm loves sharks. And fights. As long as nobody gets wet. Well, you do.",
                "Viele Tiere im Bild bringen mehr Aufrufe. Hab ich gelesen. Auf einem Keks.", "Lots of animals in shot means more views. I read that. On a cookie.",
                "Hast du Günther gesehen? Er schuldet mir ein Brötchen. Mindestens.", "Have you seen Günther? He owes me a roll. At least.",
                "Selfie-Tipp: {K:Selfie} drücken, Tier im Hintergrund – Reaktionen bringen Aufrufe!", "Selfie tip: press {K:Selfie}, animal in the background – reactions get views!",
                "Halt {K:Gestures} gedrückt, dann kannst du mit Leuten mehr machen als reden. Tipps, Geschenke, Interviews.", "Hold {K:Gestures} and you can do more with people than talk. Tips, gifts, interviews.",
                "Abends grillen, morgens filmen. So läuft das im Showbusiness.", "Grill at night, film in the morning. That's showbiz.");
            S("horst.rumour", Horst, Clue,
                "Im Westen steigt manchmal Rauch aus dem Dschungel. Da wohnt keiner. Offiziell.", "Sometimes smoke rises from the jungle in the west. Nobody lives there. Officially.",
                "Gus meinte mal: Wer den Wildpfad bei der Wasserfall-Grotte findet, sollte freundlich grüßen. Mit den Händen.", "Gus once said: whoever finds the game trail by the waterfall grotto should greet politely. With their hands.");
            L("horst.full", Horst, "Du hast schon drei Aufträge offen. Erst mal die erledigen, Profi!", "You've got three jobs running already. Finish those first, pro!");
            L("horst.r2after", Horst, "Gerald ist echt! Ich hab ihn mal umarmt. Er war dagegen.", "Gerald is real! I hugged him once. He objected.");
            S("horst.toni", Horst, null,
                "Horst hier! Da liegt einer am Strand! Mit Bart! Lebt er? Er summt.", "Horst here! There's someone on the beach! With a beard! Is he alive? He's humming.",
                "Treibholzstrand, im Nordosten! Ich bring Kekse. Und Sonnencreme.", "Driftwood Beach, north-east! I'll bring cookies. And sunscreen.");
            S("horst.camp", Horst, null,
                "Horst hier! Du warst bei den Einsiedlern im Westen? Die reden nicht. Seit der Augennacht 1987.", "Horst here! You found the hermits in the west? They don't talk. Not since the Night of the Eye, 1987.",
                "Nur mit Händen. Halt {K:Gestures} gedrückt und such ein Zeichen aus. „Das Meer ehren“ mögen sie immer.", "Only with their hands. Hold {K:Gestures} and pick a sign. They always like 'Honour the sea'.",
                "Falsche Zeichen machen sie grantig. Und grantig heißt: Stock. Geschenke helfen. Viel Glück!", "Wrong signs make them grumpy. And grumpy means: stick. Gifts help. Good luck!");
            S("horst.tied", Horst, null,
                "Horst hier. Ich hab gehört, du warst … verschnürt? Kopf hoch.", "Horst here. I heard you got… tied up? Chin up.",
                "Mir haben sie mal die Hängematte zugeknotet. Mit mir drin. Drei Tage.", "Once they knotted my hammock shut. With me inside. Three days.");
            S("horst.respect", Horst, null,
                "Du hast einen Einsiedler umgebonkt?! Respekt. Die mögen Stärke.", "You bonked a hermit?! Respect. They like strength.",
                "Und Geschenke. Am liebsten beides. Nicht gleichzeitig.", "And gifts. Ideally both. Not at the same time.");
            L("horst.later", Horst, "Kein Problem. Ich bin hier. Ich bin immer hier. In der Hängematte.", "No problem. I'm here. I'm always here. In the hammock.");
            L("horst.accept", Horst, "Abgemacht! Ich notier's. Im Kopf.", "Deal! I'll note it down. In my head.");
            S("horst.lantern", Horst, Clue,
                "Heute ist Lichterabend. Ab 19 Uhr kommen die Einsiedler aus dem Dschungel zum alten Steg am Südstrand.", "It's Lantern Evening. From 7 pm the hermits come out of the jungle to the old jetty on the south beach.",
                "Sie setzen Laternen aufs Wasser. Fürs Meer. Leise gucken – nicht stören!", "They set lanterns on the water. For the sea. Watch quietly – don't disturb!");
        }

        // ================================================================ B.1 / B.2 Uncle Gus
        static void GusLines()
        {
            S("gus.m0", Gus, null,
                "Gus hier, Funkstation Ruhestand! Bist du gut gelandet?", "Gus here, retirement radio! Landed safely?",
                "Wanda fliegt wie ich koche – mit Gefühl und Feuerlöscher.", "Wanda flies like I cook – with feeling and a fire extinguisher.",
                "Ich meld mich, wenn der Kanal wächst. Und Kind: Das Meer da draußen ist voller Geschichten.", "I'll call when the channel grows. And kid: the sea out there is full of stories.");
            S("gus.m100", Gus, null,
                "Gus hier! Hundert Follower?", "Gus here! A hundred followers?",
                "In deinem Alter hatte ich hundert Seepocken. Am Hintern. Weiter so!", "At your age I had a hundred barnacles. On my backside. Keep going!");
            S("gus.m1000", Gus, null,
                "Tausend! Horst hat mich dreimal angerufen.", "A thousand! Horst called me three times.",
                "Er sagt auch, er war mal Delfin. Glaub ihm nur das Erste.", "Also says he used to be a dolphin. Only believe the first part.");
            S("gus.m2500", Gus, null,
                "Zweitausendfünfhundert! Die ersten Leute fragen nach meiner alten Doku.", "Twenty-five hundred! The first people are asking about my old documentary.",
                "Ich sag nur: Füll das Sealog. Der Rest kommt von allein.", "I'll just say: fill the Sealog. The rest comes on its own.");
            S("gus.m5000", Gus, null,
                "Fünftausend – jetzt kommen Sponsoren.", "Five thousand – sponsors incoming.",
                "Tipp vom Profi: Nimm das Geld, nicht den lebenslangen Mayo-Vorrat.", "Pro tip: take the money, not the lifetime supply of mayo.");
            S("gus.m10000", Gus, null,
                "Zehntausend! Der ganze Hafen redet von dir.", "Ten thousand! The whole harbour's talking about you.",
                "Na gut, Horst redet. Aber laut.", "Fine, Horst is talking. But loudly.");
            S("gus.m25000", Gus, null,
                "Fünfundzwanzigtausend. Die Bottom-Brüder sind wieder in der Gegend.", "Twenty-five thousand. The Bottom brothers are back around.",
                "Die klauen Ideen wie Möwen Pommes. Pass auf deine Motive auf.", "They steal ideas like gulls steal chips. Watch your subjects.");
            S("gus.m50000", Gus, null,
                "Fünfzigtausend! Ein Doku-Sender hat angerufen.", "Fifty thousand! A documentary channel called.",
                "Ich hab gesagt, du bist beschäftigt. Wichtig wirken, Kind!", "I said you're busy. Look important, kid!");
            S("gus.m100000", Gus, Clue,
                "Hunderttausend. Sealebrity!", "A hundred thousand. Sealebrity!",
                "Ich hab mir einen alten Fernseher gekauft. Baujahr 1987. Er zeigt nur Rauschen.", "I bought an old TV. Made in 1987. It only shows static.",
                "Komisch: Drüben auf der Graben-Insel steht auch einer. Ganz allein am Strand. Und nachts flimmert er.", "Funny: there's one on the trench island too. All alone on the beach. And at night it flickers.");
            S("gus.m250000", Gus, null,
                "Viertelmillion! Ich schau dich jetzt auf zwei Fernsehern gleichzeitig.", "Quarter of a million! I watch you on two TVs at once now.",
                "Auf dem einen bist du sehr grün. Auf dem anderen sehr blau. Beides steht dir.", "On one you're very green. On the other very blue. Both suit you.");
            S("gus.m500000", Gus, null,
                "Halbe Million. Weißt du, warum ich dir die Insel vererbt hab?", "Half a million. Know why I left you the island?",
                "Weil du zu Ende bringst, was ich 1987 nicht geschafft hab.", "Because you'll finish what I couldn't in 1987.");
            S("gus.m1000000", Gus, null,
                "Eine Million … bei Neptuns Socken.", "A million… by Neptune's socks.",
                "Da unten wartet etwas auf dich. Seit 1987. Etwas Großes.", "Something's waiting for you down there. Since 1987. Something big.",
                "Geh live. Dreh die Doku zu Ende. Ich schau zu.", "Go live. Finish the documentary. I'm watching.");
            // round 3 (Q42): a new dive site is announced only once it is reached – Gus calls on the unlock
            S("gus.site.kelp_forest", Gus, null,
                "Gus hier! Der Kelpwald ist offen? Meterhohe Algen, Schatten zwischen den Stämmen.", "Gus here! The kelp forest is open? Towering algae, shadows between the stalks.",
                "Da bin ich 1989 gekentert. Mit Stil. Erzähl ich dir morgen.", "I capsized there in 1989. Stylishly. I'll tell you tomorrow.");
            S("gus.site.wreck", Gus, null,
                "Das Wrack ist frei! Die MS Clickbait … da war ich mal Smutje. Lange Geschichte. Kurze Hose.", "The wreck is open! The MS Clickbait… I was her cook once. Long story. Short trousers.",
                "Und die Strömung am Vulkan im Osten hat nachgelassen. Drüben leben Viecher aus der Urzeit.", "And the current at the volcano in the east has calmed. Animals from prehistoric times live over there.",
                "Im Schlund am Vulkan soll was Riesiges wohnen. Horst nennt es Big Mo. Horst nennt alles irgendwie.", "Something huge is said to live in the volcano's sinkhole. Horst calls it Big Mo. Horst names everything.");
            S("gus.site.blue_drop", Gus, null,
                "Die Blauwasser-Kante! Eine Wand ins endlose Blau. Nimm genug Luft mit.", "The blue-water drop-off! A wall into endless blue. Take plenty of air.",
                "Damals hing unsere Funkboje da. Piept die noch?", "Our radio buoy hung there back then. Is it still beeping?");
            S("gus.site.deep_trench", Gus, null,
                "Kind … der Graben ist offen. Da ist mein Studio versunken.", "Kid… the trench is open. That's where my studio sank.",
                "Ich hör Signale von dort unten. Piepen. Wie ein alter Fernseher.", "I'm hearing signals from down there. Beeping. Like an old telly.");
            // missions
            S("gus.M1.start", Gus, null,
                "Kind, ich hab ein Geständnis.", "Kid, I've got a confession.",
                "1989 bin ich im Kelpwald gekentert. Mit Stil.", "In 1989 I capsized in the kelp forest. Stylishly.",
                "Dabei ging meine goldene Glücksflosse verloren.", "Lost my golden lucky fin.",
                "Seitdem fang ich nur noch Erkältungen.", "Since then I only catch colds.",
                "Bei der kleinen Insel am Kelpwald. Zwischen den Felsen. Bring sie heim!", "Near the little island by the kelp. Between the rocks. Bring her home!");
            S("gus.M1.done", Gus, null,
                "DA ist sie! Ich hör sie glänzen.", "THERE she is! I can hear her shine.",
                "Behalt sie. Steht dir besser. Mir stand sie nie.", "Keep her. Suits you better. Never suited me.");
            S("gus.M2.start", Gus, null,
                "Die MS Clickbait war mein Schiff. Ich war Smutje.", "The MS Clickbait was my ship. I was the cook.",
                "Der Käpt'n wollte einen Rekord: schnellste Fahrt mit dem meisten Käse.", "The captain wanted a record: fastest run with the most cheese.",
                "Es lief … nicht gut.", "It went… not well.",
                "In der Kombüse liegt mein altes Funkgerät. Wenn's noch geht, hör ich dich unter Wasser!", "My old radio's in the galley. If it still works, I can hear you under water!");
            S("gus.M2.done", Gus, null,
                "Es piept! Es lebt! Wie ich.", "It beeps! It lives! Like me.",
                "Den Unterwasser-Funk im Shop gibt's jetzt zum halben Preis.", "The underwater radio in the shop is half price now.",
                "Dann quatschen wir beim Tauchen.", "Then we can chat while you dive.");
            S("gus.hammock", Gus, Clue,
                "Ach, die Wrack-Insel! Da hab ich früher Mittagsschlaf gemacht.", "Ah, the wreck island! I used to nap there.",
                "Mein Plätzchen … mit nassem Eingang. Südost. Mehr sag ich nicht.", "My nap spot… with a wet entrance. South-east. That's all I'm saying.");
            S("gus.M3.start", Gus, null,
                "Der Leuchtturm ist seit 1987 dunkel. In der Augennacht ist die Linse zersprungen.", "The lighthouse has been dark since 1987. The lens cracked on the Night of the Eye.",
                "Drei Scherben hab ich damals versteckt. Fragt nicht.", "I hid three shards back then. Don't ask.",
                "Ganz oben am Leuchtturm, in der Grotte, beim Funkturm.", "Top of the lighthouse, in the grotto, at the radio tower.",
                "Bau sie ein. Nachts zeigt das Licht dir was.", "Fit them back. At night the light will show you something.");
            S("gus.M3.done", Gus, null,
                "Siehst du, wohin der Strahl zeigt? Zum Graben.", "See where the beam points? The trench.",
                "Da unten liegt mein Studio. Und noch was, das nicht dahin gehört.", "My studio lies down there. And something else that doesn't belong.");
            S("gus.M4.start", Gus, null,
                "Das Piepen kommt aus dem Graben. Zweiundneunzig Meter.", "The beeping comes from the trench. Ninety-two metres.",
                "Unsere Sendeantenne. Vom alten Fernsehstudio.", "Our transmitter mast. From the old TV studio.",
                "Film sie. Und … wenn du was Großes siehst: nicht winken.", "Film it. And… if you see something big: don't wave.");
            S("gus.M4.done", Gus, null,
                "Ein AUGE? So groß wie ein Bus? Das ist es. Das hab ich 1987 gesehen.", "An EYE? Big as a bus? That's it. That's what I saw in 1987.",
                "Kind, das ist dein Finale. Aber erst bei einer Million. Die verdient man sich.", "Kid, that's your finale. But only at a million. You earn that one.");
            S("gus.shard", Gus, null, "Eine Scherbe! Vorsicht, die schneidet. Wie mein Humor.", "A shard! Careful, it's sharp. Like my humour.");
        }

        // ================================================================ K.5 / L.12 Wanda
        static void WandaLines()
        {
            S("wanda.met", Wanda, null,
                "Ah, Gus' Erbe! Ich bin Wanda, ich fliege hier alles rum.", "Ah, Gus's heir! I'm Wanda, I fly everything around here.",
                "Shop bestellen, Heli kommt. Meistens an die richtige Stelle.", "Order in the shop, the chopper comes. Usually to the right spot.");
            S("wanda.story", Wanda, null,
                "Gus hat mich damals für die Doku gebucht. Ich hab die Bänder geflogen.", "Gus booked me for the documentary back then. I flew the tapes.",
                "Ich hab das Studio als Letzte gesehen. Von oben.", "I was the last to see the studio. From above.",
                "Es hat … geblinkt. Und dann war das Meer hell. Von unten.", "It… blinked. And then the sea was bright. From below.");
            S("wanda.pad", Wanda, null,
                "Ware ist da! Und ich bin auch noch dran. Alles da.", "Delivery's here! And so am I. All present.",
                "Unterschrift? Ach, egal. Vertrauen!", "Signature? Oh, never mind. Trust!",
                "Nächstes Mal bring ich Snacks. Für mich.", "Next time I'll bring snacks. For me.",
                "Günther wollte mitfliegen. Wieder. Er hat kein Ticket.", "Günther tried to fly along. Again. He has no ticket.",
                "Landeplatz getroffen! Ich hab's mir aufgeschrieben.", "Hit the pad! I wrote it down.",
                "Der Rotor macht ein neues Geräusch. Klingt fröhlich.", "The rotor makes a new noise. Sounds cheerful.");
            L("wanda.mishap.water", Wanda, "Kleine Böe. Dein Paket badet.", "Small gust. Your parcel is taking a bath.");
            L("wanda.mishap.roof", Wanda, "Ich dachte, das Dach ist der Landeplatz. Sieht von oben gleich aus.", "Thought the roof was the pad. Looks the same from up here.");
            L("wanda.mishap.palm", Wanda, "Die Palme wollte es mehr als du.", "The palm wanted it more than you.");
            L("wanda.mishap.wrong", Wanda, "Ups. Falsche Insel? Nein, richtige Insel, falsche Ecke. Folg dem Rauch!", "Oops. Wrong island? No – right island, wrong corner. Follow the smoke!");
            L("wanda.mishap.village", Wanda, "Dein Paket liegt am Südstrand. Frag nicht. Ich frag auch nicht.", "Your parcel is on the south beach. Don't ask. I don't either.");
            L("wanda.mishap.goat", Wanda, "Da sitzt eine Ziege drauf. Das ist nicht mein Problem.", "A goat is sitting on it. Not my problem.");
            L("wanda.mishap.boat", Wanda, "Direkt aufs Boot! Ich bin ein Genie. Oder es war Zufall.", "Straight onto the boat! I'm a genius. Or it was luck.");
            S("wanda.bubble", Wanda, null,
                "Heute Rückenwind! Lieferung vielleicht sogar pünktlich.", "Tailwind today! Delivery might even be on time.",
                "Frisch getankt. Der Heli, nicht ich.", "Freshly fuelled. The chopper, not me.",
                "Ich hab heute nur zweimal falsch abgebogen. Rekord!", "Only took two wrong turns today. Record!",
                "Snacks gehen immer. Sag ich als Pilotin.", "Snacks always work. Pilot's advice.",
                "Bestell was Schweres, dann flieg ich langsamer. Romantisch.", "Order something heavy, I'll fly slower. Romantic.",
                "Im Westen raucht's im Dschungel. Ich flieg da nicht tief. Die winken mit Stöcken.", "There's smoke in the western jungle. I don't fly low there. They wave sticks.",
                "Heute keine Böen. Glaub ich. Ich hab nicht nachgesehen.", "No gusts today. I think. I didn't check.",
                "Express? Ich kann auch schnell. Und laut.", "Express? I can do fast. And loud.");
            S("wanda.talk", Wanda, null,
                "Mein Hangar ist auch mein Wohnzimmer. Und meine Werkstatt. Und meine Hängematte.", "My hangar is also my living room. And workshop. And hammock.",
                "Von oben sieht die Insel aus wie ein Pfannkuchen mit Palmen.", "From above, the island looks like a pancake with palm trees.",
                "Falls mal was schiefgeht: Folg dem Rauch. Da ist dann dein Paket.", "If anything goes wrong: follow the smoke. That's where your parcel is.",
                "Von oben sieht man nachts den Vulkan im Osten glühen. Romantisch. Und heiß.", "From above you see the volcano in the east glow at night. Romantic. And hot.");
        }

        // ================================================================ B.4 / K.3 Dr. Fin
        static void FinLines()
        {
            S("fin.first", Fin, null,
                "Hallo! Dr. Fin, Meeresbiologie. Ich hab Ihre Videos gesehen.", "Hello! Dr. Fin, marine biology. I've seen your videos.",
                "Wissenschaftlich gesehen … sehr wackelig.", "Scientifically speaking… very shaky.",
                "Aber Sie finden Tiere, die ich seit Jahren suche!", "But you find animals I've been looking for for years!",
                "Ich bezahle für Daten. Fotos, Fische, Messungen. Alles in der Dr.-Fin-App.", "I pay for data. Photos, fish, measurements. All in the Dr. Fin app.",
                "Ach, und Ihre Angel … darüber reden wir noch.", "Oh, and your rod… we'll talk about that.");
            S("fin.talk", Fin, null,
                "Hm? Wussten Sie, dass jede Art ihre Lieblingstiefe hat? Steht im Sealog.", "Hm? Did you know every species has a favourite depth? It's in the Sealog.",
                "Ein Blaupunktrochen von oben – wissenschaftlich gesehen sein bester Winkel.", "A bluespotted ray from above – scientifically, his best angle.",
                "Nachts steigen die Laternenfische auf. Größte Wanderung der Welt. Jeden Tag!", "At night the lanternfish rise. Biggest migration on Earth. Every day!",
                "Ihre Tauchdaten sind faszinierend. Ihr Zeltplatz-Geschmack nicht. Hm?", "Your dive data is fascinating. Your camping taste isn't. Hm?",
                "Speerfischen nur bei Speisefischen, bitte. Schildkröten sind tabu. Gerald ist tabu.", "Spearfishing only for food fish, please. Turtles are off limits. Gerald is off limits.");
            L("fin.done", Fin, "Hervorragende Daten! Honorar ist überwiesen. Hm!", "Excellent data! Fee transferred. Hm!");
            L("fin.r1.done", Fin, "Drei Brassen! Die Carbon-Angel gibt's jetzt zum halben Preis.", "Three bream! The carbon rod is half price for you now.");
            L("fin.r4.done", Fin, "Ein Schwertfisch! Für die Wissenschaft! Hier – meine Forschungsrute.", "A swordfish! For science! Here – my research rod.");
            L("fin.new", Fin, "Neue Forschungsaufträge sind in der App. Hm? Hm!", "New research jobs are in the app. Hm? Hm!");
        }

        // ================================================================ B.3 Chad & Brad (+ round 3 sabotage, Q43)
        static void RivalLines()
        {
            L("rival.r1.1", Chad, "Yo! Chad Bottomley. Bottom Feeders. Zwei Millionen Abos. Und du so?", "Yo! Chad Bottomley. Bottom Feeders. Two million subs. You?");
            L("rival.r1.2", Brad, "Brad. Der Hübschere.", "Brad. The handsome one.");
            L("rival.r1.3", Chad, "Süße Insel. Süßes Boot. Ist das … Rost?", "Cute island. Cute boat. Is that… rust?");
            L("rival.r1.4", Brad, "Follower-Duell. Jede Woche. Verlierer muss …", "Follower duel. Every week. Loser has to…");
            L("rival.r1.5", Chad, "… verlieren. Genau. Und deine Motive? Die nehmen wir uns auch. Clout Nine, ablegen!", "…lose. Exactly. And your subjects? We'll take those too. Clout Nine, cast off!");
            L("rival.r1.6", Brad, "Bye-bye, Plankton!", "Bye-bye, plankton!");
            L("rival.r2.1", Brad, "Breaking News: deine Schildkröte ist CGI.", "Breaking news: your turtle is CGI.");
            L("rival.r2.2", Chad, "Viel zu gut animiert. Niemand ist so entspannt.", "Way too well animated. Nobody is that relaxed.");
            L("rival.r3.1", Chad, "Ein bisschen Köder hat noch keinem geschadet.", "A little chum never hurt anyone.");
            L("rival.r3.2", Brad, "Außer den Fischen. Und den Tauchern. Und dem Gesetz.", "Except the fish. And the divers. And the law.");
            L("rival.r3x.1", Chad, "Das war … ein Sozialexperiment!", "That was… a social experiment!");
            L("rival.r3x.2", Brad, "Wir löschen alles. Sofort. Gas!", "We delete everything. Now. Floor it!");
            L("rival.r4.1", Brad, "Äh. Hallo. Rein hypothetisch …", "Uh. Hi. Purely hypothetically…");
            L("rival.r4.2", Chad, "… wenn zwei sehr gutaussehende Brüder feststecken würden …", "…if two very handsome brothers were stuck…");
            L("rival.r4.3", Brad, "… auf 85 Metern …", "…at 85 metres…");
            L("rival.r4.4", Chad, "… mit wenig Luft. Und viel Reue.", "…low on air. High on regret.");
            L("rival.r4x.1", Chad, "Okay. Du bist cool. Sag's keinem.", "Okay. You're cool. Don't tell anyone.");
            L("rival.r4x.2", Brad, "Wir sehen uns beim Finale. Als Freunde. Bro.", "See you at the finale. As friends. Bro.");
            L("rival.visit.1", Chad, "Yo, Plankton! Wir waren nur kurz in der Gegend. Mit unserer Yacht.", "Yo, plankton! Just passing by. In our yacht.");
            L("rival.visit.2", Brad, "Neues Video: „Wir tauchen in Gold“. Drei Millionen Aufrufe. Kein Druck.", "New video: 'Diving in gold'. Three million views. No pressure.");
            L("rival.visit.3", Chad, "Dein Boot hat einen neuen Rostfleck. Respekt. Organisch.", "Your boat has a new rust spot. Respect. Organic.");
            L("rival.visit.4", Brad, "Wir haben einen Whirlpool. Auf dem Boot. Hast du einen Whirlpool?", "We've got a hot tub. On the boat. Do you have a hot tub?");
            L("rival.visit.5", Chad, "Wir sind dann mal weg. Ruhm wartet nicht.", "We're off. Fame doesn't wait.");
            L("rival.duel.lost", Chad, "Duell verloren, Plankton. #BottomFeeders", "Duel lost, plankton. #BottomFeeders");
            L("rival.duel.won", Brad, "Du hast das Duell gewonnen?! Das ist … ein Bug.", "You won the duel?! That's… a bug.");
            L("rival.b1.start.1", Brad, "Bro. Unser goldener Selfie-Stick …", "Bro. Our golden selfie stick…");
            L("rival.b1.start.2", Chad, "… ist ins Hausriff gefallen. Sechs Meter. Wir tauchen nicht. Frisur.", "…fell into the house reef. Six metres. We don't dive. Hair.");
            L("rival.b1.start.3", Brad, "Bringst du ihn zurück? Wir würden dich sogar erwähnen. Vielleicht.", "Bring it back? We might even mention you. Maybe.");
            L("rival.b1.choice", Chad, "Und? Gibst du ihn zurück?", "So? You giving it back?");
            L("rival.b1.give.1", Brad, "Du bist ja … nett? Komisch. Danke, Bro.", "You're… nice? Weird. Thanks, bro.");
            L("rival.b1.give.2", Chad, "Hier, für deine Rostlaube. Nicht ausgeben. Doch, ausgeben.", "Here, for your rust bucket. Don't spend it. No, spend it.");
            L("rival.b1.keep.1", Chad, "Er behält ihn?! Bro.", "He's keeping it?! Bro.");
            L("rival.b1.keep.2", Brad, "BRO.", "BRO.");
            // round 3 sabotage (Q43): stolen subjects, stream raids, the weekly duel
            S("rival.steal", Chad, null,
                "Yo Plankton! Unser neues Video ist online. Dein Motiv – nur in GOLD. Kam uns bekannt vor.", "Yo plankton! Our new video is up. Your subject – just in GOLD. Felt familiar.",
                "Wir nennen das Inspiration. Film's besser, wenn du kannst. Heute noch.", "We call it inspiration. Film it better if you can. Today.");
            L("rival.stealwon.1", Brad, "Dein Video ist besser als unseres?! Wir löschen unseres. BRO.", "Your video is better than ours?! We're deleting ours. BRO.");
            S("rival.raid", Chad, null,
                "RAID! Bottom Feeders im Haus! Alle mal „langweilig“ in den Chat!", "RAID! Bottom Feeders in the house! Everybody type 'boring'!",
                "Zeig was Spektakuläres, Plankton. Oder geh unter.", "Show us something spectacular, plankton. Or sink.");
            L("rival.raidlost.1", Brad, "Okay … das war spannend. Wir gucken nicht weiter. Vielleicht noch kurz.", "Okay… that was exciting. We're not watching. Maybe a bit.");
            L("rival.duelweek.1", Chad, "Montag ist Duell-Tag, Plankton. Wer diese Woche mehr Follower holt, gewinnt.", "Monday is duel day, plankton. Whoever gains more followers this week wins.");
        }

        // ================================================================ round 3: the hermits (GDD §19/§20 Q2–Q8)
        // They never speak words: every line describes what they show with their hands (tag "sign").
        static void HermitLines()
        {
            // ---- general reactions (any hermit – speakerOverride)
            L("hm.greet", HL, "*mustert dich von oben bis unten und verschränkt die Arme*", "*looks you up and down and folds their arms*", Sign);
            L("hm.shoo.1", HL, "*wedelt dich mit beiden Händen weg*", "*shoos you away with both hands*", Sign);
            L("hm.shoo.2", HL, "*zeigt auf den Wildpfad: raus hier!*", "*points at the game trail: out!*", Sign);
            L("hm.ignore", HO, "*schaut weiter durchs Fernrohr und tut, als wärst du Luft*", "*keeps looking through the spyglass as if you were air*", Sign);
            L("hm.warn", HH, "*hebt den Stock und schüttelt den Kopf – keinen Schritt weiter*", "*raises the stick and shakes the head – not one step further*", Sign);
            L("hm.attack", HH, "*stürmt mit erhobenem Stock auf dich zu*", "*charges at you, stick raised*", Sign);
            L("hm.win", HH, "*sitzt im Sand, reibt sich den Kopf – und nickt dir anerkennend zu*", "*sits in the sand rubbing the head – and nods at you with respect*", Sign);
            L("hm.tied", HH, "*verschnürt dich mit Fischernetz und klopft sich zufrieden die Hände ab*", "*ties you up with fishing net and dusts off the hands, satisfied*", Sign);
            L("hm.freed", HH, "*lacht lautlos und klopft dir auf die Schulter*", "*laughs silently and pats your shoulder*", Sign);
            L("hm.sign.ok", HL, "*nickt langsam*", "*nods slowly*", Sign);
            L("hm.sign.hello", HL, "*hebt kurz das Kinn*", "*gives a short lift of the chin*", Sign);
            L("hm.sign.sea", HL, "*legt die Hand aufs Herz und verbeugt sich Richtung Meer*", "*puts a hand on the heart and bows toward the sea*", Sign);
            L("hm.sign.peace", HL, "*atmet tief durch und lässt die Schultern sinken*", "*takes a deep breath and lowers the shoulders*", Sign);
            L("hm.sign.wrong", HL, "*runzelt die Stirn und dreht dir den Rücken zu*", "*frowns and turns their back on you*", Sign);
            L("hm.sign.confused", HL, "*legt den Kopf schief*", "*tilts the head*", Sign);
            L("hm.thanks", HL, "*tippt sich an die Stirn und lächelt*", "*taps the forehead and smiles*", Sign);
            L("hm.film.no", HL, "*hält dir die Hand vor die Linse und schüttelt den Kopf*", "*puts a hand over your lens and shakes the head*", Sign);
            L("hm.film.yes", HL, "*posiert stolz – mit Blick aufs Meer*", "*poses proudly – gazing at the sea*", Sign);
            L("hm.learn.no", HL, "*schüttelt den Kopf: noch nicht*", "*shakes the head: not yet*", Sign);
            L("hm.learn.none", HL, "*zuckt mit den Schultern und zeigt auf die anderen*", "*shrugs and points at the others*", Sign);
            L("hm.learn.known", HL, "*zeigt auf dich und nickt: kannst du schon*", "*points at you and nods: you already know it*", Sign);
            L("hm.trade.no", HL, "*zuckt mit den Schultern und dreht sich weg*", "*shrugs and turns away*", Sign);
            L("hm.trade.ok", HL, "*schlägt ein – Handel!*", "*shakes on it – deal!*", Sign);
            L("hm.hint.no", HL, "*legt den Finger auf die Lippen*", "*puts a finger to the lips*", Sign);
            L("hm.food.cook", HC, "*drückt dir eine dampfende Schale in die Hand*", "*presses a steaming bowl into your hands*", Sign);
            L("hm.food.other", HL, "*zeigt mit dem Daumen zum Koch am Feuer*", "*thumbs toward the cook by the fire*", Sign);
            L("hm.food.done", HC, "*klopft auf den leeren Topf: morgen wieder*", "*taps the empty pot: tomorrow again*", Sign);
            L("hm.needfish", HC, "*hebt zwei Finger und macht einen Fisch: die Makrelen fehlen noch – in deiner Kühlbox*", "*holds up two fingers and mimes a fish: the mackerel are still missing – in your cooler*", Sign);
            L("hm.gift.like", HL, "*strahlt, drückt das Geschenk an die Brust und verbeugt sich*", "*beams, hugs the gift and bows*", Sign);
            L("hm.gift.ok", HL, "*dreht es in den Händen und nickt höflich*", "*turns it over in the hands and nods politely*", Sign);
            L("hm.gift.bad", HL, "*hält es mit spitzen Fingern weit von sich*", "*holds it at arm's length with pinched fingers*", Sign);
            L("hm.gift.many", HL, "*hebt beide Hände: genug für heute*", "*raises both hands: enough for today*", Sign);
            L("hm.ritual", HL, "*setzt eine Laterne aufs Wasser und verbeugt sich*", "*sets a lantern on the water and bows*", Sign);
            // ---- teaching (Q8)
            S("hm.teach.h_smith", HS, Sign,
                "*legt einen Speer an den Schleifstein und zeigt dir den Winkel. Langsam. Sehr langsam.*", "*puts a spear to the grindstone and shows you the angle. Slowly. Very slowly.*",
                "*klopft auf deinen Rucksack, dann Richtung deiner Hütte: dort weiterschleifen*", "*taps your backpack, then points toward your hut: grind on there*");
            S("hm.teach.h_hunter", HH, Sign,
                "*flicht vor deinen Augen eine Reuse aus Treibholz und Netz*", "*weaves a fish trap from driftwood and net right in front of you*",
                "*zeigt aufs Wasser, dann auf die Sonne: jeden Morgen nachsehen*", "*points at the water, then at the sun: check it every morning*");
            S("hm.teach.h_cook", HC, Sign,
                "*zeigt: Fisch, Kokosnuss, Feuer – und reibt sich den Bauch*", "*shows: fish, coconut, fire – and rubs the belly*",
                "*schiebt dir den Topf rüber: jetzt du*", "*pushes the pot over to you: your turn*");
            S("hm.teach.h_gardener", HG, Sign,
                "*zeigt dir, an welchen Bäumen die reifen Früchte hängen, und klopft zweimal an den Stamm*", "*shows you which trees have ripe fruit and knocks twice on the trunk*",
                "*zeigt auf die Sonne: jeden Morgen etwas für dich*", "*points at the sun: something for you every morning*");
            // ---- the leader's story (first friendly talk) – the Night of the Eye, the vow of silence
            S("hm.story", HL, Sign,
                "*zeigt aufs Meer, dann auf ihren Mund – und legt den Finger auf die Lippen*", "*points at the sea, then at her mouth – and puts a finger to her lips*",
                "*formt mit beiden Händen ein riesiges Auge, darunter ein Licht, das von unten kommt*", "*forms a giant eye with both hands, a light rising beneath it*",
                "*zeigt auf die Kerben in ihrem Stab: 1987*", "*points at the notches in her staff: 1987*",
                "(Seit der Augennacht schweigen sie. Aus Respekt vor dem Meer.)", "(Since the Night of the Eye they keep silent. Out of respect for the sea.)");
            S("hm.accept", HL, Sign,
                "*legt dir eine Muschelkette um und zeigt auf das ganze Lager*", "*puts a shell necklace around your neck and gestures at the whole camp*",
                "(Ab jetzt gehörst du ein bisschen dazu.)", "(From now on you belong a little.)");
            // ---- small talk per hermit (rotating; friendly or calm)
            S("hm.talk.h_leader", HL, Sign,
                "*klopft auf das Gezeitenbrett: heute Nacht Flut*", "*taps the tide board: high tide tonight*",
                "*zeigt aufs Meer und legt die Hand aufs Herz*", "*points at the sea and puts a hand on her heart*",
                "*zeigt Richtung Graben und schließt die Augen*", "*points toward the trench and closes her eyes*");
            S("hm.talk.h_smith", HS, Sign,
                "*fährt mit dem Daumen über eine Klinge und grinst*", "*runs a thumb along a blade and grins*",
                "*hält einen Schleifstein hoch und reibt Daumen und Finger: Tausch?*", "*holds up a whetstone and rubs thumb and fingers: trade?*");
            S("hm.talk.h_hunter", HH, Sign,
                "*zeigt auf deine Schuhe, dann auf seine nackten Füße – und lacht lautlos*", "*points at your shoes, then at his bare feet – and laughs silently*",
                "*macht mit der Hand einen Fisch, der ins Netz schwimmt*", "*makes a fish with his hand swimming into a net*");
            S("hm.talk.h_cook", HC, Sign,
                "*rührt im Topf und riecht daran: perfekt*", "*stirs the pot and sniffs: perfect*",
                "*zeigt auf deinen Bauch und dann auf den Topf*", "*points at your belly, then at the pot*");
            S("hm.talk.h_lookout", HO, Sign,
                "*reicht dir kurz das Fernrohr – Richtung Vulkan*", "*hands you the spyglass for a moment – toward the volcano*",
                "*zählt mit den Fingern die Boote am Horizont: eins. Eine sehr große Yacht.*", "*counts the boats on the horizon on her fingers: one. A very big yacht.*");
            S("hm.talk.h_gardener", HG, Sign,
                "*hält dir eine Mango unter die Nase*", "*holds a mango under your nose*",
                "*zeigt auf die Ziegen und tippt sich an die Stirn: verrückt*", "*points at the goats and taps the forehead: crazy*");
        }

        // ================================================================ K.8 Toni
        static void ToniLines()
        {
            S("toni.t1", Toni, null,
                "Ist das … Festland? Nein? Gut. Festland ist laut.", "Is this… the mainland? No? Good. The mainland is loud.",
                "Ich bin Toni. Ton-Mann. 1987, „Live aus der Tiefe“. Mit Gus.", "I'm Tony. Sound guy. 1987, 'Live from the Deep'. With Gus.",
                "In der Augennacht bin ich auf einem Floß abgetrieben. Seitdem nehm ich singende Fische auf.", "On the Night of the Eye I drifted off on a raft. Since then I record singing fish.",
                "Ich hab Durst. Und Hunger. Und ein Floß ohne Floß.", "I'm thirsty. And hungry. And my raft is mostly not a raft.");
            S("toni.t2", Toni, null,
                "Eine Kokosnuss und ein gegrillter Fisch. Dann bin ich wieder ein Mensch.", "A coconut and a grilled fish. Then I'm human again.",
                "Kokosnüsse liegen unter den Palmen. Fisch kommt vom Grill an der Hütte.", "Coconuts lie under the palms. Fish comes off the grill by the hut.");
            S("toni.t2done", Toni, null,
                "Ahh. Kokosnuss. Mein alter Freund.", "Ahh. Coconut. My old friend.",
                "Mein Tonbandgerät ist beim Kentern abgesoffen. Dreißig Meter raus, flach.", "My tape recorder sank when I capsized. Thirty metres out, shallow.",
                "Bringst du's mir? Da sind 37 Jahre Wal drauf.", "Will you bring it? It has 37 years of whale on it.");
            L("toni.t2sushi", Toni, "Roh? Sushi! Auch gut. Danke!", "Raw? Sushi! Works too. Thanks!");
            L("toni.t2need", Toni, "Noch eine Kokosnuss und ein Grillfisch, dann geht's mir besser.", "Still need a coconut and a grilled fish, then I'll feel better.");
            S("toni.t3done", Toni, null,
                "Mein Gerät! Es tropft. Es lebt.", "My recorder! It's dripping. It lives.",
                "Jetzt fehlt nur ein Funkgerät, dann find ich meine Insel wieder.", "Now I just need a radio, then I can find my island again.",
                "Drei Teile: eine Röhre vom Funkturm, eine Kupferspule aus einem Bootswrack in der Lagune …", "Three parts: a valve from the radio tower, a copper coil from a rowboat wreck in the lagoon…",
                "… und eine Funkbatterie. Die verkauft Wanda. Chopper Shop, unter „Teile“.", "…and a radio battery. Wanda sells those. Chopper Shop, under 'Parts'.");
            L("toni.t4need", Toni, "Röhre, Spule, Batterie. In der Reihenfolge ist es egal. Fast.", "Valve, coil, battery. Order doesn't matter. Almost.");
            S("toni.t4done", Toni, null,
                "Hör mal: Auf meiner Insel singen die Fische.", "Listen: on my island the fish sing.",
                "Da gibt's Tiere, die gibt's sonst nirgends. Hier, meine Karte.", "There are animals there that exist nowhere else. Here, my map.",
                "Folg den gelben Bojen durch den Nebel.", "Follow the yellow buoys through the fog.",
                "Und das Auge … das Studio hat nicht geblinkt. Das Auge hat geblinkt.", "And the eye… the studio didn't blink. The eye blinked.");
            S("toni.talk", Toni, null,
                "Mmm-hmm. Hörst du das? Nein? Ich auch nicht. Schön, oder?", "Mmm-hmm. Hear that? No? Me neither. Lovely, isn't it?",
                "Gus hat damals gesagt: „Toni, das wird groß.“ Er meinte die Doku. Es war das Auge.", "Gus said back then: 'Tony, this'll be big.' He meant the show. It was the eye.",
                "Horst hat mir seine zweite Hängematte geliehen. Sie hat ein Loch. Ich hab jetzt Aussicht.", "Horst lent me his second hammock. It has a hole. Now I've got a view.",
                "Die Leute im Dschungel kenn ich noch von damals. Sie haben aufgehört zu reden. Ich hab aufgehört zuzuhören.", "I know the jungle folk from back then. They stopped talking. I stopped listening.");
            L("toni.clue.buoy", Toni, "In der Blauwasser-Wand piept was. Unser alter Studio-Sender. Achtzehn Meter, im Spalt.", "Something beeps in the blue-water wall. Our old studio transmitter. Eighteen metres, in a crack.", Clue);
            L("toni.clue.archive", Toni, "In meiner Nebelgrotte liegt mein Tonarchiv. Tief, dunkel, trocken. Fast trocken.", "My sound archive is in the Fog Grotto. Deep, dark, dry. Mostly dry.", Clue);
        }

        // ================================================================ round 3: clues (map pieces, treasure, wrecks, chests, memes, Big Mo)
        // Ids "hint.<key>.<speaker>" – NpcClues picks the first one an NPC knows whose target is still missing.
        static void ClueLines()
        {
            // ---- the old L.6 secrets (re-homed: the village is gone)
            L("horst.clue.top", Horst, "Gus sagte immer: Die beste Aussicht hat die Möwe. Ganz oben am Leuchtturm.", "Gus always said: the seagull has the best view. Right at the top of the lighthouse.", Clue);
            L("horst.clue.cellar", Horst, "Gus hat nie verraten, was unter seinem Teppich liegt. Im Leuchtturm.", "Gus never told what's under his rug. In the lighthouse.", Clue);
            L("horst.clue.kelp", Horst, "Auf der Kelp-Insel haben Gus und ich mal was in einen Baum geritzt. Ganz hinten, nur vom Wasser aus.", "On the kelp island Gus and I once carved something into a tree. Far side, only from the water.", Clue);
            L("horst.clue.ravine", Horst, "Gus hat sein Sparschwein in der Schlucht versteckt. Ganz unten, hinter einem Stein.", "Gus hid his piggy bank in the ravine. Right at the bottom, behind a rock.", Clue);
            L("hm.clue.falls", HG, "*zeigt auf den Wasserfall, hält sich die Hand ans Ohr und macht „hohl“ mit den Händen*", "*points at the waterfall, cups an ear and makes 'hollow' with the hands*", Clue);
            L("hm.clue.loft", HL, "*öffnet die Truhe im Unterstand einen Spalt und malt ein „G“ in die Luft*", "*opens the chest in the shelter a crack and draws a 'G' in the air*", Clue);
            // ---- map pieces (8) + the secret X (step 3: medium difficulty, away from the main paths)
            L("hint.map_p0.horst", Horst, "Unter der Badeinsel glitzert was. Dreieinhalb Meter tief. Ich tauch da nicht. Ich bin Hafenmeister.", "Something glitters under the swim raft. Three and a half metres down. I don't dive. I'm a harbourmaster.", Clue);
            L("hint.map_p1.horst", Horst, "Am Treibholzstrand steckt ein Stockkreuz im Sand. Mit Kieselpfeil. Sieht nach Schatz aus. Oder nach Kunst.", "On Driftwood Beach there's a stick cross in the sand. With a pebble arrow. Looks like treasure. Or art.", Clue);
            L("hint.map_p1.h_hunter", HH, "*malt ein Kreuz in den Sand, zeigt nach Nordosten und macht Grabbewegungen*", "*draws a cross in the sand, points north-east and mimes digging*", Clue);
            L("hint.map_p2.horst", Horst, "Hinter dem Wasserfall ist eine Nische. Gus hat da früher geduscht. Mit Kleidung.", "There's a niche behind the waterfall. Gus used to shower there. Fully dressed.", Clue);
            L("hint.map_p2.h_gardener", HG, "*hält die Hände wie einen Wasserfall vors Gesicht und schlüpft seitlich dahinter*", "*holds the hands like a waterfall in front of the face and slips sideways behind it*", Clue);
            L("hint.map_p3.horst", Horst, "Südwestlich der Gezeitentümpel liegt ein Kieselpfeil im Sand. Den hab ich nicht gelegt. Ich leg nie was.", "South-west of the tide pools there's a pebble arrow in the sand. I didn't lay it. I never lay anything.", Clue);
            L("hint.map_p4.horst", Horst, "In der Wasserfall-Grotte gibt's eine Kammer ganz hinten. Bück dich, sonst Beule.", "The waterfall grotto has a chamber right at the back. Duck, or bump.", Clue);
            L("hint.map_p4.h_gardener", HG, "*bückt sich tief, tastet sich durch einen Spalt und zeigt dann ein Stück Papier*", "*ducks low, feels through a crack and then shows a piece of paper*", Clue);
            L("hint.map_p5.horst", Horst, "Am Südstrand, westlich vom alten Steg: Stockkreuz im Sand. Graben macht glücklich. Sagt mein Rücken nicht.", "South beach, west of the old jetty: a stick cross in the sand. Digging makes you happy. My back disagrees.", Clue);
            L("hint.map_p5.h_hunter", HH, "*zeigt Richtung Südstrand, dann auf den alten Steg und gräbt mit beiden Händen*", "*points toward the south beach, then at the old jetty and digs with both hands*", Clue);
            L("hint.map_p6.horst", Horst, "Die Leute im Westen haben angeblich ein Stück Karte. Auf einer Kiste. Bei einer Kokosnuss mit Schwimmweste.", "The folk in the west supposedly have a map piece. On a crate. Next to a coconut in a life vest.", Clue);
            L("hint.map_p6.h_smith", HS, "*deutet auf die Kiste unter der Kokosnuss-Puppe und tippt auf ein Stück Papier*", "*points at the crate under the coconut puppet and taps a piece of paper*", Clue);
            L("hint.map_p7.horst", Horst, "Unten an der Leuchtturm-Strickleiter, am Nordstrand: Stockkreuz. Oben ist es schöner, unten sandiger.", "At the foot of the lighthouse rope ladder, north beach: a stick cross. Nicer at the top, sandier at the bottom.", Clue);
            L("hint.treasure.horst", Horst, "Mit allen acht Stücken zeigt die Karte eine Bucht unter den Klippen beim alten Funkturm. Nur schwimmend!", "With all eight pieces the map shows a cove under the cliffs by the old radio tower. Swim only!", Clue);
            L("hint.treasure.h_lookout", HO, "*hält das Fernrohr nach Südwesten, zu den Klippen unter dem Funkturm, und macht Schwimmbewegungen*", "*points the spyglass south-west at the cliffs below the radio tower and mimes swimming*", Clue);
            L("hint.maps.horst", Horst, "Gus hat seine Schatzkarte in acht Stücke gerissen. Damit ich sie nicht finde. Hat geklappt.", "Gus tore his treasure map into eight pieces. So I wouldn't find it. It worked.", Clue);
            // ---- wrecks (step 4: sailboat, seaplane, trawler, pirates)
            L("hint.wreck_sailboat.wanda", Wanda, "Von oben seh ich nördlich der Insel einen Mast im Wasser. Ein Segelboot. Schläft da unten.", "From above I see a mast in the water north of the island. A sailboat. Sleeping down there.", Clue);
            L("hint.wreck_seaplane.wanda", Wanda, "Zwischen Insel und Wrack liegt ein Wasserflugzeug im Meer. Nicht meins. Diesmal.", "Between the island and the wreck there's a seaplane in the sea. Not mine. This time.", Clue);
            L("hint.wreck_trawler.fin", Fin, "Südwestlich liegt ein gesunkener Fischkutter. Künstliche Riffe sind faszinierend. Hm!", "A sunken trawler lies to the south-west. Artificial reefs are fascinating. Hm!", Clue);
            L("hint.wreck_trawler.h_lookout", HO, "*zeigt nach Südwesten und macht mit den Händen ein Steuerrad, das versinkt*", "*points south-west and mimes a ship's wheel sinking*", Clue);
            L("hint.wreck_pirates.h_lookout", HO, "*malt mit dem Finger einen Dreispitz in die Luft und zeigt nach Nordosten aufs Meer*", "*draws a tricorn in the air and points north-east out to sea*", Clue);
            L("hint.wreck_pirates.wanda", Wanda, "Nordöstlich, Richtung Graben, liegt ein alter Segler mit Totenkopf-Flagge. Unter Wasser. Gruselig.", "North-east, toward the trench, lies an old sailing ship with a skull flag. Under water. Spooky.", Clue);
            // ---- treasure chests
            L("hint.chest_lagoon.horst", Horst, "Neben der Badeinsel, ein Stück weiter draußen: eine Truhe. Der erste Schatz ist immer der schönste.", "Next to the swim raft, a bit further out: a chest. The first treasure is always the best.", Clue);
            L("hint.chest_lagoon.h_cook", HC, "*zeigt auf die Badeinsel, dann malt er eine Kiste in die Luft und klappt den Deckel auf*", "*points at the swim raft, draws a box in the air and flips the lid open*", Clue);
            L("hint.chest_reef.horst", Horst, "Am Hausriff liegen zwei alte Truhen. Mit dem Stock kriegt man sie schneller auf. Ich mach das mit Charme.", "There are two old chests at the house reef. A stick opens them faster. I use charm.", Clue);
            L("hint.chest_kelp.fin", Fin, "Im Kelpwald hab ich zwei Kisten gesehen. Zwischen den Stämmen. Nicht meine Abteilung. Hm.", "I saw two crates in the kelp forest. Between the stalks. Not my department. Hm.", Clue);
            // ---- curiosities / memes
            L("hint.meme_shades.horst", Horst, "Am Hausriff sitzt ein Barsch mit Sonnenbrille. Der hat meinen Style geklaut.", "At the house reef there's a grouper wearing sunglasses. He stole my style.", Clue);
            L("hint.meme_cart.horst", Horst, "Irgendwer hat einen Einkaufswagen ins Hausriff geschoben. Ich war's nicht. Glaub ich.", "Somebody pushed a shopping cart into the house reef. Wasn't me. I think.", Clue);
            L("hint.meme_crabrave.toni", Toni, "Bei der Badeinsel wummert unter Wasser Musik. Krabben? Mit Bass?", "Near the swim raft music thumps under water. Crabs? With bass?", Clue);
            L("hint.meme_dance.toni", Toni, "Im Kelpwald leuchtet nachts eine Discokugel. Ich hab sie gehört. Discokugeln klingen gelb.", "In the kelp forest a disco ball glows at night. I heard it. Disco balls sound yellow.", Clue);
            L("hint.meme_gnome.horst", Horst, "Ein Gartenzwerg mit Taucherbrille im Kelpwald. Der ist mir mal abgehauen.", "A garden gnome in scuba goggles in the kelp forest. He ran away from me once.", Clue);
            L("hint.meme_throne.fin", Fin, "Am Wrack sitzt ein Einsiedlerkrebs auf einem Thron. Wissenschaftlich unerklärlich. Hm.", "At the wreck a hermit crab sits on a throne. Scientifically inexplicable. Hm.", Clue);
            L("hint.meme_gamingpc.toni", Toni, "Beim abgestürzten Flugzeug summt ein Lüfter. Unter Wasser. Mit bunten Lichtern.", "By the crashed plane a fan hums. Under water. With colourful lights.", Clue);
            L("hint.meme_stonks.toni", Toni, "Beim Fischkutter piept was im Takt. Hoch, höher, am höchsten. Wie Börsenkurse.", "By the trawler something beeps in rhythm. Up, higher, highest. Like stock prices.", Clue);
            L("hint.meme_ducks.wanda", Wanda, "Ich hab im offenen Meer Quietscheentchen gesehen. Eine ganze Flotte. Östlich der Insel.", "I saw rubber ducks in the open sea. A whole fleet. East of the island.", Clue);
            L("hint.meme_dino.h_lookout", HO, "*macht mit den Händen ein kleines Dino-Maul, quietscht lautlos und zeigt auf den Schlund*", "*makes a tiny dino jaw with her hands, squeaks silently and points at the sinkhole*", Clue);
            // ---- the volcano + Big Mo (step 4)
            L("hint.big_mo.horst", Horst, "Am Vulkan gibt's einen Schlund. Da wohnt Big Mo. Ich hab ihm den Namen gegeben. Er weiß es nicht.", "There's a sinkhole at the volcano. Big Mo lives there. I named him. He doesn't know.", Clue);
            L("hint.big_mo.fin", Fin, "Am Vulkan soll ein Mosasaurier leben. Ausgestorben seit 66 Millionen Jahren. Hm? HM!", "A mosasaur supposedly lives at the volcano. Extinct for 66 million years. Hm? HM!", Clue);
            L("hint.big_mo.h_lookout", HO, "*öffnet die Arme riesig wie ein Maul, klappt sie zu und zeigt Richtung Vulkan – tief nach unten*", "*opens the arms huge like a jaw, snaps them shut and points toward the volcano – deep down*", Clue);
            L("hint.fossils.fin", Fin, "Am Vulkan leben echte lebende Fossilien. Perlboote! Pfeilschwanzkrebse! Ich brauch Fotos!", "Real living fossils live at the volcano. Nautiluses! Horseshoe crabs! I need photos!", Clue);
            // ---- the hermits' story bits
            L("hint.tide_page.h_leader", HL, "*klopft auf das Gezeitenbrett – darunter liegt eine vergilbte Seite*", "*taps the tide board – a yellowed page lies beneath it*", Clue);
            L("hint.eye.h_leader", HL, "*formt mit den Händen ein riesiges Auge und zeigt Richtung Graben*", "*forms a giant eye with her hands and points toward the trench*", Clue);
            // ---- nothing left
            L("hint.none.horst", Horst, "Tipps? Hab ich gerade keine. Außer: Sonnencreme.", "Tips? None right now. Except: sunscreen.");
            L("hint.none.wanda", Wanda, "Von oben seh ich gerade nix Neues. Nur dich. Winkst du? Nicht winken.", "Nothing new from up here. Just you. Are you waving? Don't wave.");
            L("hint.none.fin", Fin, "Wissenschaftlich gesehen: Sie haben alles gefunden. Unwahrscheinlich, aber hm.", "Scientifically speaking: you found everything. Unlikely, but hm.");
            L("hint.none.toni", Toni, "Mmm. Gerade ist es still. Das Meer denkt nach.", "Mmm. It's quiet right now. The sea is thinking.");
            L("hint.none.rival", Chad, "Tipps? Für DICH? Bro.", "Tips? For YOU? Bro.");
        }

        // ================================================================ round 3: the wheel's actions at NPCs who speak
        static void ReactionLines()
        {
            // generic (speakerOverride = the NPC)
            L("gen.sea", Horst, "Das Meer? Groß. Nass. Voller Content.", "The sea? Big. Wet. Full of content.");
            L("gen.peace", Horst, "Alles gut. Wir sind cool.", "All good. We're cool.");
            L("gen.food", Horst, "Hunger? Roher Fisch aufs Lagerfeuer – oder Snacks per Heli.", "Hungry? Raw fish on the campfire – or snacks by chopper.");
            L("gen.trade", Horst, "Tauschen? Ich hab nix. Außer Meinungen. Die sind gratis.", "Trade? I've got nothing. Except opinions. Those are free.");
            L("gen.trade.camp", Horst, "Tauschen? Da musst du zu den Einsiedlern. Die nehmen Schätze vom Meeresgrund.", "Trade? Go to the hermits. They take treasure from the seabed.");
            L("gen.thanks", Horst, "Gern geschehen! Wofür auch immer.", "You're welcome! For whatever it was.");
            L("gen.gift.like", Horst, "Oh! Danke! Genau mein Ding.", "Oh! Thanks! Just my thing.");
            L("gen.gift.ok", Horst, "Danke … glaub ich.", "Thanks… I think.");
            L("gen.learn", Horst, "Als Nächstes? {0}.", "What next? {0}.");
            L("gen.learn.none", Horst, "Als Nächstes? Filmen, hochladen, schlafen. Der Kreislauf des Ruhms.", "What next? Film, upload, sleep. The circle of fame.");
            L("gen.film", Horst, "*lächelt in die Kamera* Hallo Internet!", "*smiles into the camera* Hello internet!");
            // interviews to camera (Film)
            L("horst.film", Horst, "Hallo Internet. Ich bin Horst. Hafenmeister. Ruhestand. Hängematte. Abonnieren!", "Hello internet. I'm Horst. Harbourmaster. Retired. Hammock. Subscribe!");
            L("wanda.film", Wanda, "Wanda hier, Chopper-Lieferdienst. Pünktlich wie die Flut. Die kommt auch, wann sie will.", "Wanda here, chopper delivery. Punctual as the tide. That also comes when it likes.");
            L("fin.film", Fin, "Dr. Fin. Meeresbiologie. Bitte nicht wackeln. Hm. Doch, wackeln. Ist authentisch.", "Dr. Fin. Marine biology. Please don't shake. Hm. Do shake. It's authentic.");
            L("toni.film", Toni, "Mmm-hmm. Das hier ist das Meer. Hör mal. … Hast du's gehört? Ich auch nicht.", "Mmm-hmm. This is the sea. Listen. … Did you hear it? Me neither.");
            L("chad.film", Chad, "Yo! Ihr seht gerade die Bottom Feeders. Gratis. Gern geschehen.", "Yo! You're watching the Bottom Feeders. For free. You're welcome.");
            L("brad.film", Brad, "Brad. Von links. Das ist meine gute Seite. Beide sind meine gute Seite.", "Brad. From the left. That's my good side. Both are my good side.");
            // gifts they love
            L("horst.gift.like", Horst, "Ein Fischbrötchen! Du bist mein Lieblings-Erbe.", "A fish roll! You're my favourite heir.");
            L("wanda.gift.like", Wanda, "Snacks! Endlich versteht mich jemand.", "Snacks! Finally someone understands me.");
            L("fin.gift.like", Fin, "Für die Wissenschaft? Ich nehme es. Für die Wissenschaft.", "For science? I'll take it. For science.");
            L("toni.gift.like", Toni, "Kokosnuss … mein alter Freund. Du kennst mich.", "Coconut… my old friend. You know me.");
            L("chad.gift.like", Chad, "Für uns? … Danke. Sag's keinem.", "For us? … Thanks. Don't tell anyone.");
            // Horst's apology / sea / food answers
            L("horst.sea", Horst, "Das Meer? Mein Arbeitsplatz. Theoretisch. Praktisch ist es die Hängematte.", "The sea? My workplace. In theory. In practice it's the hammock.");
            L("horst.food", Horst, "Fischbrötchen! … Hast du eins? Nein? Dann roher Fisch aufs Lagerfeuer.", "Fish rolls! … Got one? No? Then raw fish on the campfire.");
            L("toni.sea", Toni, "Das Meer singt. Man muss nur leise genug sein.", "The sea sings. You just have to be quiet enough.");
            L("fin.sea", Fin, "Das Meer bedeckt 71 Prozent der Erde. Und 100 Prozent meiner Gedanken. Hm.", "The sea covers 71 percent of the Earth. And 100 percent of my thoughts. Hm.");
            L("wanda.sea", Wanda, "Von oben ist das Meer blau. Von unten auch. Hab ich gehört.", "From above the sea is blue. From below too. So I've heard.");
            L("rival.sea", Brad, "Das Meer? Unser Hintergrund. Für Selfies.", "The sea? Our background. For selfies.");
        }

        // ================================================================ L.7 side quests (round 3: relocated + new funny filming jobs)
        static void SideQuestLines()
        {
            // ---- Horst
            S("sq.H1.start", Horst, null,
                "Günther klaut. Jeden Mittag. Ich brauch Beweise.", "Günther steals. Every noon. I need evidence.",
                "Ein Foto von ihm mit Beute, zwischen 12 und 14 Uhr. Er hängt meistens bei mir rum. Wegen der Brötchen.", "A photo of him with loot, between 12 and 2. He mostly hangs around me. Because of the rolls.");
            S("sq.H1.end", Horst, null, "Da! Erwischt! Ich häng's an die Palme. Als Fahndungsplakat.", "There! Caught him! I'll hang it on the palm. As a wanted poster.");
            S("sq.H2.start", Horst, null,
                "Meine Sonnenbrille ist ins Hafenbecken gefallen. Beim Fallen. Aus der Hängematte.", "My sunglasses fell into the harbour. While I fell. Out of the hammock.",
                "Drei Meter tief, zwischen den Pfählen vom Steg.", "Three metres down, between the dock posts.");
            S("sq.H2.end", Horst, null, "Meine Brille! Ich seh wieder cool aus. Hab ich vorher auch, aber jetzt sieht man's.", "My shades! I look cool again. I did before, but now you can see it.");
            S("sq.H3.start", Horst, null,
                "Influencer-Workshop! Lektion eins: ein Selfie mit dem Coach.", "Influencer workshop! Lesson one: a selfie with the coach.",
                "Lektion zwei: ein Video mit ehrlichem Titel. Ehrlich ist das neue Clickbait.", "Lesson two: a video with an honest title. Honest is the new clickbait.");
            S("sq.H3.end", Horst, null, "Zertifiziert! Von mir. Das zählt. Irgendwo.", "Certified! By me. That counts. Somewhere.");
            S("sq.T3.start", Horst, null,
                "Der große Fang: ein Fisch über drei Kilo. Vom Boot aus. Wie früher.", "The big catch: a fish over three kilos. From the boat. Like the old days.",
                "Mit der Bambusrute wird das nix. Carbon, mindestens.", "Not with the bamboo rod. Carbon, at least.");
            S("sq.T3.end", Horst, null, "Das ist ein Fisch! Fast so groß wie meine Geschichten.", "That's a fish! Almost as big as my stories.");
            S("sq.L1.start", Horst, null,
                "Ich wollte Luftaufnahmen machen. Mit einem Drachen. Für den Kanal.", "I wanted aerial shots. With a kite. For the channel.",
                "Jetzt hängt der Drachen oben am Leuchtturm, am Geländer. Holst du ihn? Ich hab Höhenangst. Und Treppenangst.", "Now the kite hangs at the top of the lighthouse, on the railing. Will you get it? I'm scared of heights. And stairs.");
            S("sq.L1.end", Horst, null, "MEIN DRACHEN! Nächstes Mal binde ich die Kamera an Günther.", "MY KITE! Next time I'll tie the camera to Günther.");
            S("sq.L3.start", Horst, null,
                "Ich hab eine Schatzkarte gemalt! Mit Buntstift! Für meinen Notgroschen.", "I drew a treasure map! In crayon! For my emergency savings.",
                "Ich hab vergessen, wo ich ihn vergraben hab. Das X ist da, wo das X ist. Folg den Pfeilen.", "I forgot where I buried them. The X is where the X is. Follow the arrows.");
            S("sq.L3.end", Horst, null, "Mein Notgroschen! Der Hälfte gehört dir. Die andere Hälfte auch, ich schulde dir eh was.", "My savings! Half is yours. The other half too, I owe you anyway.");
            S("sq.M2.start", Horst, null,
                "Gus hatte ein Paddel. Legendär. Damit ist er 1989 im Kelpwald gekentert.", "Gus had a paddle. Legendary. He capsized with it in the kelp forest in 1989.",
                "Er hat's in der Höhle an der versteckten Bucht gelassen. Bring's mir – ich häng's über die Hängematte.", "He left it in the cave by the hidden cove. Bring it to me – I'll hang it over the hammock.");
            S("sq.M2.end", Horst, null, "Gus' Paddel! Dafür kriegst du eine Flagge. Für deine … Rostlaube.", "Gus's paddle! You get a flag for that. For your… rust bucket.");
            S("sq.H4.start", Horst, null,
                "Neue Idee: ASMR! Leute schauen anderen beim Schlafen zu. Für Geld.", "New idea: ASMR! People watch others sleep. For money.",
                "Film mich zehn Sekunden, wenn ich in der Hängematte schnarche. Mittags oder nachts. Nicht wecken!", "Film me for ten seconds while I snore in the hammock. Noon or night. Don't wake me!");
            S("sq.H4.end", Horst, null, "Wie war ich? Hab ich elegant geschnarcht? Ich hab elegant geschnarcht.", "How was I? Did I snore elegantly? I snored elegantly.");
            S("sq.H5.start", Horst, null,
                "Im Meer liegen komische Sachen rum. Ein Barsch mit Sonnenbrille! Ein Einkaufswagen!", "There's weird stuff in the sea. A grouper in sunglasses! A shopping cart!",
                "Fotografier mir zwei davon. Fürs Sealog. Memes sind die Zukunft.", "Photograph two of them for me. For the Sealog. Memes are the future.");
            S("sq.H5.end", Horst, null, "Hahaha! Der Barsch! Ich lach seit drei Tagen. Innerlich.", "Hahaha! The grouper! I've been laughing for three days. On the inside.");
            // ---- Wanda
            S("sq.W1.start", Wanda, null,
                "Mir sind drei Fässer vom Haken gerutscht. Die treiben jetzt vorm Landeplatz.", "Three drums slipped off my hook. They're drifting off the helipad now.",
                "Bringst du sie zum Hangar? Sind leicht. Fast.", "Can you bring them to the hangar? They're light. Almost.");
            S("sq.W1.end", Wanda, null, "Alle drei! Dafür flieg ich dir zweimal Express. Gratis. Pssst.", "All three! I'll fly you express twice for that. Free. Shh.");
            S("sq.W2.start", Wanda, null,
                "Ich mach einen Prospekt: „Rostbucht – Insel der Lieferungen“.", "I'm making a brochure: 'Rust Cove – island of deliveries'.",
                "Ich brauch Fotos vom Wasser aus: Leuchtturm, Hütte und mein Landeplatz. Den treff ich von oben nie.", "I need photos from the water: lighthouse, hut and my helipad. I never hit it from above.");
            S("sq.W2.end", Wanda, null, "Wunderschön! Ich setz mein Gesicht drauf. Ganz groß.", "Gorgeous! I'll put my face on it. Really big.");
            S("sq.L2.start", Wanda, null,
                "Ich flieg jeden Tag übers Meer und hab noch nie einen Hai gesehen. Nur Schatten.", "I fly over the sea every day and I've never seen a shark. Only shadows.",
                "Lad ein Video mit Hai hoch. Ich guck's im Hangar. Mit Popcorn.", "Upload a video with a shark. I'll watch it in the hangar. With popcorn.");
            S("sq.L2.end", Wanda, null, "EIN HAI! Hier, meine Krone. Aus Bordkarten gefaltet. Für Mutige.", "A SHARK! Here, my crown. Folded from boarding passes. For the brave.");
            S("sq.W3.start", Wanda, null,
                "Ich brauch ein Showreel! Film mich, wenn der Heli eine Lieferung bringt. Sechs Sekunden.", "I need a showreel! Film me when the chopper brings a delivery. Six seconds.",
                "Bestell was, dann komm ich. Mit Stil. Meistens.", "Order something, then I'll come. With style. Mostly.");
            S("sq.W3.end", Wanda, null, "Wie sah ich aus? Wie eine Möwe mit Schluckauf? Perfekt!", "How did I look? Like a seagull with hiccups? Perfect!");
            // ---- Dr. Fin (she comes by boat on visit days – her jobs finish on the spot, no walk back)
            S("sq.T1.start", Fin, null,
                "Am Hausriff hängt ein altes Geisternetz. Da verfangen sich die Fische. Inakzeptabel.", "There's an old ghost net on the house reef. Fish get tangled in it. Unacceptable.",
                "Drei Stellen. {K:Interact} halten, bis es ab ist. Fünf Sekunden pro Stelle.", "Three spots. Hold {K:Interact} until it's off. Five seconds each.");
            S("sq.T1.end", Fin, null, "Netz entfernt! Die Population erholt sich. Hm. Schön.", "Net removed! The population will recover. Hm. Nice.");
            S("sq.T2.start", Fin, null,
                "Wie tief lebt der Zackenbarsch wirklich? Meine Hypothese: unter 25 Metern.", "How deep does the grouper really live? My hypothesis: below 25 metres.",
                "Ein Foto als Beleg, bitte. Am Wrack finden Sie ihn.", "A photo as proof, please. You'll find him at the wreck.");
            S("sq.T2.end", Fin, null, "Bestätigt! Ich trage Ihnen drei Tiefen ins Sealog ein. Hm!", "Confirmed! I'll mark three depths in your Sealog. Hm!");
            S("sq.J1.start", Fin, null,
                "Der Walgesang an der Blauwasser-Kante! Dafür würde ich töten. Wissenschaftlich gesprochen.", "The whale song at the blue-water drop! I'd kill for it. Scientifically speaking.",
                "Filmen Sie Big Bertha, wenn sie singt.", "Film Big Bertha while she sings.");
            S("sq.J1.end", Fin, null, "Diese Frequenzen! Ich mach daraus einen Klingelton. Für die Wissenschaft.", "Those frequencies! I'll make a ringtone of it. For science.");
            S("sq.F1.start", Fin, null,
                "Für die Wissenschaft: ein Selfie mit Gerald, der Schildkröte. Er soll lächeln.", "For science: a selfie with Gerald the turtle. He should smile.",
                "{K:Selfie} – Selfie-Ansicht. Gerald im Bild. Bitte nicht umarmen.", "{K:Selfie} – selfie view. Gerald in the shot. Please don't hug him.");
            S("sq.F1.end", Fin, null, "Er lächelt! Wissenschaftlich gesehen … ist das unmöglich. Hm!", "He's smiling! Scientifically speaking… that's impossible. Hm!");
            // ---- Toni
            S("sq.N1.start", Toni, null,
                "Ich mach ein Album: „Stimmen der Lagune“.", "I'm making an album: 'Voices of the Lagoon'.",
                "Film klickende Delfine, einen bellenden Seelöwen und den singenden Wal.", "Film clicking dolphins, a barking sea lion and the singing whale.");
            S("sq.N1.end", Toni, null, "Mmm-hmm. Das ist Musik. Das ist ein Hit.", "Mmm-hmm. That's music. That's a hit.");
            S("sq.N2.start", Toni, null,
                "Ein Band fehlt noch. Das letzte von 1987. Aus der Augennacht.", "One tape is still missing. The last one from 1987. From the Night of the Eye.",
                "Es liegt am Antennenmast im Graben. Zweiundneunzig Meter. Bring es mir.", "It's at the antenna mast in the trench. Ninety-two metres. Bring it to me.");
            S("sq.N2.end", Toni, null,
                "Das letzte Band. Hörst du? Da … atmet etwas.", "The last tape. Hear that? Something… breathes.",
                "Wenn du live gehst – da unten – dann hört es dich.", "When you go live – down there – it will hear you.");
            S("sq.J2.start", Toni, null,
                "Die Knallkrebse am Hausriff spielen nachts ein Konzert. Beste Band der Insel.", "The snapping shrimp at the house reef play a concert at night. Best band on the island.",
                "Nimm zwanzig Sekunden auf. Unter Wasser, nach 21 Uhr.", "Record twenty seconds. Under water, after 9 pm.");
            S("sq.J2.end", Toni, null, "Knister, knaster! Das wird mein Schlagzeug.", "Crackle, crackle! That'll be my drums.");
            // ---- the hermits (described signs; offered when the camp was found and the giver is friendly)
            S("sq.P1.start", HL, Sign,
                "*zeigt aufs Gezeitenbrett – drei Löcher sind leer*", "*points at the tide board – three holes are empty*",
                "*zeigt zum Strand, zu den Tümpeln, ins flache Wasser*", "*points to the beach, the tide pools, the shallow water*");
            S("sq.P1.end", HL, Sign,
                "*steckt die Pflöcke ins Brett – an einer Stelle fehlt eine Kerbe: die Augennacht*", "*puts the pegs into the board – one notch is missing: the Night of the Eye*");
            S("sq.P3.start", HL, Sign,
                "*hält eine Laterne hoch und zeigt Richtung Südstrand, zum alten Steg*", "*holds up a lantern and points toward the south beach, at the old jetty*",
                "*macht mit den Fingern ein Viereck vors Auge: vom Wasser aus*", "*makes a frame with her fingers: from the water*");
            S("sq.P3.end", HL, Sign, "*betrachtet das Bild lange und legt die Hand aufs Herz*", "*looks at the picture for a long time and puts a hand on her heart*");
            S("sq.P2.start", HG, Sign,
                "*zeigt auf die Ziegen, dann macht er Yoga – auf einem Bein*", "*points at the goats, then does yoga – on one leg*",
                "*macht ein Kamera-Viereck: acht Sekunden, mit Ziegen*", "*makes a camera frame: eight seconds, with goats*");
            S("sq.P2.end", HG, Sign, "*setzt dir seinen Strohhut auf und kichert lautlos*", "*puts his straw hat on your head and giggles silently*");
            S("sq.M1.start", HS, Sign,
                "*klopft auf die wackelige Schleifbank und hebt fünf Finger*", "*taps the wobbly grinding bench and holds up five fingers*",
                "*zeigt zu den Stränden und macht Holz-Sammel-Bewegungen*", "*points to the beaches and mimes gathering wood*");
            S("sq.M1.end", HS, Sign, "*baut die Bank neu und winkt dich heran: Zeit für eine Lektion*", "*rebuilds the bench and waves you over: time for a lesson*");
            S("sq.M3.start", HH, Sign,
                "*paddelt in der Luft, zeigt auf zwei Kanus und dann auf die Sonne: morgen, zehn Uhr*", "*paddles in the air, points at two canoes, then at the sun: tomorrow, ten o'clock*",
                "*macht ein Kamera-Viereck: filmen!*", "*makes a camera frame: film it!*");
            S("sq.M3.end", HH, Sign, "*reckt die Faust in die Luft und schenkt dir einen geflickten Wimpel*", "*pumps a fist and gives you a patched pennant*");
            S("sq.A1.start", HC, Sign,
                "*zeigt in den leeren Topf, hebt drei Finger und malt Kokosnüsse in die Luft*", "*points into the empty pot, holds up three fingers and draws coconuts in the air*",
                "*dann zwei Finger: Fische – aus deiner Kühlbox*", "*then two fingers: fish – from your cooler*");
            S("sq.A1.end", HC, Sign, "*rührt, schnuppert, strahlt – und schreibt dir das Rezept in den Sand*", "*stirs, sniffs, beams – and writes the recipe in the sand for you*");
            S("sq.A2.start", HG, Sign,
                "*zeigt auf die Lagune, taucht mit der Hand ab und pflückt sechs unsichtbare Trauben*", "*points at the lagoon, dives with his hand and picks six invisible grapes*");
            S("sq.A2.end", HG, Sign, "*knackt eine Seetraube und nickt zufrieden*", "*pops a sea grape and nods, satisfied*");
            S("sq.C1.start", HC, Sign,
                "*zeigt auf Kokos, die Kokosnuss in der Schwimmweste, und macht ein Kamera-Viereck*", "*points at Kokos, the coconut in the life vest, and makes a camera frame*",
                "*hebt sechs Finger und legt den Finger auf die Lippen: er ist schüchtern*", "*holds up six fingers and puts a finger to the lips: he's shy*");
            S("sq.C1.end", HC, Sign, "*stellt Kokos stolz zurück auf die Kiste und klatscht lautlos*", "*proudly puts Kokos back on his crate and claps silently*");
            S("sq.C2.start", HL, Sign,
                "*zeigt auf den Schrein, dann auf die sinkende Sonne*", "*points at the shrine, then at the setting sun*",
                "*macht ein Kamera-Viereck und legt dann die Hand aufs Herz: mit Respekt*", "*makes a camera frame, then puts a hand on her heart: with respect*");
            S("sq.C2.end", HL, Sign, "*verbeugt sich vor dir – zum ersten Mal*", "*bows to you – for the first time*");
            S("sq.S1.start", HO, Sign,
                "*zeigt Richtung Vulkan, öffnet die Arme riesig wie ein Maul*", "*points toward the volcano, opens her arms huge like a jaw*",
                "*macht ein Kamera-Viereck und zeigt auf den Schrein: als Beweis*", "*makes a camera frame and points at the shrine: as proof*");
            S("sq.S1.end", HO, Sign, "*klebt ein Foto von Big Mo an den Schrein und salutiert*", "*sticks a picture of Big Mo to the shrine and salutes*");
            // ---- generic giver responses
            L("sq.later.v", HL, "*nickt: später*", "*nods: later*", Sign);
            L("sq.accept.v", HL, "*schlägt dir auf die Schulter*", "*slaps your shoulder*", Sign);
            L("sq.later.n", Wanda, "Kein Problem. Das Meer wartet auch.", "No problem. The sea waits too.");
            L("sq.accept.n", Wanda, "Super! Bis gleich!", "Great! See you soon!");
        }
    }
}
