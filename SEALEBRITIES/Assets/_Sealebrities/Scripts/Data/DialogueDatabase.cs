using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Sealebrities
{
    /// One spoken line (≤ 90 characters per language, humour from the situation – L.7 / L.8 respect rules).
    public sealed class LineDef
    {
        public string id, speaker, de, en;
        /// "village" for every line spoken in / about Altriff (designer read, L.8), "clue" for secret clues (L.6).
        public string tag;
        public string Text => Loc.Tr(de, en);
    }

    /// Every NPC / radio line of the game (A, B, K.5, K.8, L.6, L.7, L.8, L.12). Lines are addressed by id and resolved
    /// on every machine in its own language (Dialogue.Resolve → Resolve). Conversations are id prefixes: Seq("horst.b1")
    /// returns horst.b1.1, horst.b1.2 … in order.
    /// Tokens: {0} {1} … = arguments; {K:Action} = "[key]" of a GameAction; {S:Item} = "[key]" of the hotbar slot that
    /// holds a HoldItem (Camera / FishingRod / Flashlight) – both follow the player's key bindings.
    public static class DialogueDatabase
    {
        static readonly Dictionary<string, LineDef> lines = new Dictionary<string, LineDef>(900);
        static readonly Dictionary<string, List<string>> seqs = new Dictionary<string, List<string>>(256);
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

        /// Every line with a tag (designer read of the village lines, L.8).
        public static void ByTag(string tag, List<LineDef> into)
        {
            Build();
            into.Clear();
            foreach (var kv in lines) if (kv.Value.tag == tag) into.Add(kv.Value);
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
            if (!Hands.Find(item, out Hand hand, out int slot)) return "[" + GameInput.BindingLabel(GameAction.Inventory) + "]";
            var a = (GameAction)((hand == Hand.Right ? (int)GameAction.RightSlot1 : (int)GameAction.LeftSlot1) + slot);
            return "[" + GameInput.BindingLabel(a) + "]";
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

        const string V = "village", Clue = "clue";
        const string Horst = NpcDatabase.Horst, Gus = NpcDatabase.Gus, Wanda = NpcDatabase.Wanda, Fin = NpcDatabase.Fin, Chad = NpcDatabase.Chad, Brad = NpcDatabase.Brad,
            Pearl = NpcDatabase.Pearl, Mira = NpcDatabase.Mira, Tomas = NpcDatabase.Tomas, Ada = NpcDatabase.Ada, Juno = NpcDatabase.Juno, Lio = NpcDatabase.Lio,
            Toni = NpcDatabase.Toni;

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
            VillageLines();
            ToniLines();
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
                "Kind, die Insel gehört dir – Hütte, Boot, Kanal. 1987 verschwand mein Studio im Meer. Bring den Kanal auf eine Million. Horst hilft. Glaub ihm die Hälfte. – Gus",
                "Kid, the island is yours – hut, boat, channel. In 1987 my studio vanished into the sea. Get the channel to a million. Horst will help. Believe half of it. – Gus");
        }

        // ================================================================ A Horst
        static void HorstLines()
        {
            S("horst.b0", Horst, null,
                "Gus sagt, du willst Fische berühmt machen. Ich sage: ENGAGEMENT!", "Gus says you want to make fish famous. I say: ENGAGEMENT!",
                "Erst mal Handy auf – hier läuft alles übers Handy. {K:Phone}", "First, open your phone – everything runs through it. {K:Phone}");
            S("horst.b1", Horst, null,
                "Kein Geld, keine Luft. Keine Luft, kein Tauchen. Also: Angeln!", "No money, no air. No air, no diving. So: fishing!",
                "Angel nehmen {S:FishingRod}. {K:Use} halten, loslassen – zack, Wurf.", "Take the rod {S:FishingRod}. Hold {K:Use}, let go – whoosh, cast.",
                "Taucht der Schwimmer ab: {K:Use}! Dann einholen – nicht zu stramm.", "Bobber goes under: {K:Use}! Then reel in – not too tight.",
                "Fünf Fische. Ich zähl mit. Ich zähl immer mit.", "Five fish. I'm counting. I'm always counting.");
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
                "Kamera auf {S:Camera}. Unter Wasser filmt sie von selbst – alles, was schwimmt, zählt.", "Camera's on {S:Camera}. Under water it films by itself – everything that swims counts.",
                "Fürs Sealog: {K:Aim} halten, ranzoomen, Klick – Foto!", "For the Sealog: hold {K:Aim}, zoom in, click – photo!",
                "Drei Arten fotografieren, gleich vorm Steg. Und: Luft im Blick!", "Photograph three species, right off the dock. And: watch your air!");
            S("horst.b6", Horst, null,
                "Das war … WUNDERSCHÖN. Ich hab geweint. Innerlich.", "That was… BEAUTIFUL. I cried. On the inside.",
                "Handy → FinTube → Hochladen. Ein Video pro Tag – das liebt der Algorithmus.", "Phone → FinTube → Upload. One video a day – the algorithm loves that.",
                "Denk dir einen Titel aus – und ein Vorschaubild mit vielen Tieren bringt Klicks.", "Make up a title – and a thumbnail with lots of animals gets clicks.",
                "Glaub mir, ich hab Ahnung.", "Trust me, I know things.");
            S("horst.b7", Horst, null,
                "Die Aufrufe kommen über Nacht. Ins Bett darfst du ab 18 Uhr.", "Views come in overnight. Bedtime is from 6 pm.",
                "Bis dahin: Grill am Strand, Kokosnüsse, Insel erkunden.", "Until then: grill on the beach, coconuts, explore the island.",
                "Ich … beobachte Möwen. Professionell.", "I'll… observe seagulls. Professionally.");
            S("horst.b8", Horst, null,
                "Schau dir das an! Follower! Echte! Vermutlich!", "Look at that! Followers! Real ones! Probably!",
                "FinTube zahlt sogar schon ein bisschen. Reich sind wir nächste Woche.", "FinTube even pays a little already. We'll be rich next week.",
                "Hier, mein Köderhut. Hat Glück gebracht. Mir nicht, aber anderen.", "Here, my lure hat. It brought luck. Not to me, but to others.",
                "Bei 1.000 Followern gehen wir LIVE – da spenden Leute echtes Geld!", "At 1,000 followers we go LIVE – people donate real money!");
            S("horst.b9", Horst, null,
                "TAUSEND! Weißt du, was das heißt? KRILL.tv!", "A THOUSAND! You know what that means? KRILL.tv!",
                "Und sie spenden Geld! Für Mut. Für Haie. Für Unfälle.", "And they donate money! For courage. For sharks. For accidents.",
                "Stream starten auf dem Boot: Handy → KRILL.tv.", "Start the stream on the boat: phone → KRILL.tv.",
                "Ohnmacht = Stream weg, Follower weg. Kein Druck! Wasserdruck. Los!", "Faint = stream gone, followers gone. No pressure! Water pressure. Go!");
            // idle gags (A.1)
            L("horst.gag.ow", Horst, "Au.", "Ow.");
            L("horst.gag.sandwich", Horst, "GÜNTHER! Das war mein Fischbrötchen!", "GÜNTHER! That was my fish roll!");
            L("horst.gag.whistle", Horst, "Hey! Hier wird nicht gedrängelt! Ich bin der Hafenmeister!", "Hey! No pushing in my harbour! I'm the harbourmaster!");
            L("horst.gag.selfie", Horst, "Hm. Gutes Licht. Ich seh aus wie ein Profi. Du auch. Fast.", "Hm. Good light. I look like a pro. You too. Almost.");
            L("horst.gag.awake", Horst, "Bin wach! Algorithmus!", "Awake! Algorithm!");
            L("horst.gag.whoa", Horst, "Huuuuch –", "Whoaaaa –");
            L("horst.gag.planned", Horst, "Geplant.", "Planned.");
            // small talk (rotating, with real tips)
            S("horst.talk", Horst, null,
                "Ich sitz meistens am Steg. Ruf einfach. Oder nicht.", "I'm usually on the dock. Just shout. Or don't.",
                "Je tiefer, desto seltener die Tiere. Und desto teurer die Flasche.", "The deeper you go, the rarer the animals. And the pricier the tank.",
                "Im Sealog steht, in welcher Tiefe jeder Fisch wohnt. Sehr praktisch.", "The Sealog says at which depth each fish lives. Very handy.",
                "Der Algorithmus mag Haie. Und Katzen. Wir haben keine Katzen.", "The algorithm loves sharks. And cats. We don't have cats.",
                "Viele Tiere im Bild bringen mehr Aufrufe. Hab ich gelesen. Auf einem Keks.", "Lots of animals in shot means more views. I read that. On a cookie.",
                "Perle schlägt mich jeden Abend beim Kartenspiel. Ich lass sie gewinnen. Seit 30 Jahren.", "Pearl beats me at cards every night. I let her win. For 30 years now.",
                "Das Dorf Altriff liegt im Süden am Strand. Sag „Gute Flut!“ – das sagt man da so.", "The village of Oldreef is on the south beach. Say 'Good tide!' – that's how they greet.",
                "Hast du Günther gesehen? Er schuldet mir ein Brötchen. Mindestens.", "Have you seen Günther? He owes me a roll. At least.",
                "Selfie-Tipp: {K:Selfie} drücken, Tier im Hintergrund – Reaktionen bringen Aufrufe!", "Selfie tip: press {K:Selfie}, animal in the background – reactions get views!",
                "Abends grillen, morgens filmen. So läuft das im Showbusiness.", "Grill at night, film in the morning. That's showbiz.");
            L("horst.full", Horst, "Du hast schon drei Aufträge offen. Erst mal die erledigen, Profi!", "You've got three jobs running already. Finish those first, pro!");
            L("horst.r2after", Horst, "Gerald ist echt! Ich hab ihn mal umarmt. Er war dagegen.", "Gerald is real! I hugged him once. He objected.");
            S("horst.toni", Horst, null,
                "Horst hier! Da liegt einer am Strand! Mit Bart! Lebt er? Er summt.", "Horst here! There's someone on the beach! With a beard! Is he alive? He's humming.",
                "Treibholzstrand, im Nordosten! Ich bring Kekse. Und Sonnencreme.", "Driftwood Beach, north-east! I'll bring cookies. And sunscreen.");
            L("horst.later", Horst, "Kein Problem. Ich bin hier. Ich bin immer hier.", "No problem. I'm here. I'm always here.");
            L("horst.accept", Horst, "Abgemacht! Ich notier's. Im Kopf.", "Deal! I'll note it down. In my head.");
            L("horst.cards", Horst, "Kartenabend? Bei Perle? … Na gut. Aber diesmal gewinn ich.", "Card night? At Pearl's? … Fine. But this time I win.");
            L("horst.cardsearly", Horst, "Kartenabend? Erst ab 18 Uhr. Vorher hab ich … Termine. Mit Möwen.", "Card night? Not before 6 pm. I've got… appointments. With seagulls.");
        }

        // ================================================================ B.1 / B.2 Uncle Gus
        static void GusLines()
        {
            S("gus.m0", Gus, null,
                "Gus hier! Bist du gut gelandet?", "Gus here! Landed safely?",
                "Wanda fliegt wie ich koche – mit Gefühl und Feuerlöscher.", "Wanda flies like I cook – with feeling and a fire extinguisher.");
            S("gus.m100", Gus, null,
                "Gus hier, Funkstation Ruhestand! Hundert Follower?", "Gus here, retirement radio! A hundred followers?",
                "In deinem Alter hatte ich hundert Seepocken. Am Hintern. Weiter so!", "At your age I had a hundred barnacles. On my backside. Keep going!");
            S("gus.m1000", Gus, null,
                "Tausend! Horst hat mich dreimal angerufen.", "A thousand! Horst called me three times.",
                "Er sagt auch, er war mal Delfin. Glaub ihm nur das Erste.", "Also says he used to be a dolphin. Only believe the first part.");
            S("gus.m2500", Gus, null,
                "Zweitausendfünfhundert! Das Wrack da draußen, die MS Clickbait …", "Twenty-five hundred! That wreck out there, the MS Clickbait…",
                "… da war ich mal Smutje. Lange Geschichte. Kurze Hose. Schau mal rein.", "…I was her cook. Long story. Short trousers. Have a look inside.");
            S("gus.m5000", Gus, null,
                "Fünftausend – jetzt kommen Sponsoren.", "Five thousand – sponsors incoming.",
                "Tipp vom Profi: Nimm das Geld, nicht den lebenslangen Mayo-Vorrat.", "Pro tip: take the money, not the lifetime supply of mayo.");
            S("gus.m10000", Gus, null,
                "Zehntausend! Der ganze Hafen redet von dir.", "Ten thousand! The whole harbour's talking about you.",
                "Na gut, Horst redet. Aber laut.", "Fine, Horst is talking. But loudly.");
            S("gus.m25000", Gus, null,
                "Fünfundzwanzigtausend. Die Bottom-Brüder sind wieder in der Gegend.", "Twenty-five thousand. The Bottom brothers are back around.",
                "Wenn die Yacht anlegt: Fenster zu, Ohren zu.", "When that yacht docks: shut the windows, shut your ears.");
            S("gus.m50000", Gus, null,
                "Fünfzigtausend! Ein Doku-Sender hat angerufen.", "Fifty thousand! A documentary channel called.",
                "Ich hab gesagt, du bist beschäftigt. Wichtig wirken, Kind!", "I said you're busy. Look important, kid!");
            S("gus.m100000", Gus, Clue,
                "Hunderttausend. Sealebrity!", "A hundred thousand. Sealebrity!",
                "Und … ich hör Signale aus dem Graben. Piepen. Wie ein alter Fernseher.", "And… I'm hearing signals from the trench. Beeping. Like an old telly.",
                "Nachts flimmert drüben auf der Graben-Insel was. Komisch, oder?", "At night something flickers on the trench island. Odd, eh?");
            S("gus.m250000", Gus, null,
                "Viertelmillion! Ich hab mir extra einen Fernseher gekauft.", "Quarter of a million! Bought a TV just to watch you.",
                "Baujahr 1987. Du bist sehr grün.", "Made in 1987. You look very green.");
            S("gus.m500000", Gus, null,
                "Halbe Million. Weißt du, warum ich dir die Insel vererbt hab?", "Half a million. Know why I left you the island?",
                "Ich auch nicht mehr. Beste Idee meines Lebens.", "Neither do I anymore. Best idea I ever had.");
            S("gus.m1000000", Gus, null,
                "Eine Million … bei Neptuns Socken.", "A million… by Neptune's socks.",
                "Da unten wartet etwas auf dich. Etwas Großes.", "Something's waiting for you down there. Something big.",
                "Geh live. Ich schau zu.", "Go live. I'm watching.");
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
                "Der Leuchtturm ist seit Jahren dunkel. Die Linse ist zersprungen.", "The lighthouse has been dark for years. The lens is cracked.",
                "Drei Scherben hab ich damals versteckt. Fragt nicht.", "I hid three shards back then. Don't ask.",
                "Ganz oben am Leuchtturm, in der Grotte, beim Funkturm.", "Top of the lighthouse, in the grotto, at the radio tower.",
                "Bau sie ein. Nachts zeigt das Licht dir was.", "Fit them back. At night the light will show you something.");
            S("gus.M3.done", Gus, null,
                "Siehst du, wohin der Strahl zeigt? Zum Graben.", "See where the beam points? The trench.",
                "Da unten liegt was, das nicht dahin gehört.", "Something's down there that doesn't belong.");
            S("gus.M4.start", Gus, null,
                "Das Piepen kommt aus dem Graben. Zweiundneunzig Meter.", "The beeping comes from the trench. Ninety-two metres.",
                "Eine Sendeantenne. Vom alten Fernsehstudio.", "A transmitter mast. From the old TV studio.",
                "Film sie. Und … wenn du was Großes siehst: nicht winken.", "Film it. And… if you see something big: don't wave.");
            S("gus.M4.done", Gus, null,
                "Ein AUGE? So groß wie ein Bus?", "An EYE? Big as a bus?",
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
                "Gus hat mich damals für die Doku gebucht.", "Gus booked me for the documentary back then.",
                "Ich hab das Studio als Letzte gesehen. Von oben.", "I was the last to see the studio. From above.",
                "Es hat … geblinkt.", "It… blinked.");
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
            L("wanda.mishap.village", Wanda, "Die Kinder haben's. Zahl mit einem Selfie.", "The kids have it. Pay with a selfie.", V);
            L("wanda.mishap.goat", Wanda, "Da sitzt eine Ziege drauf. Das ist nicht mein Problem.", "A goat is sitting on it. Not my problem.");
            L("wanda.mishap.boat", Wanda, "Direkt aufs Boot! Ich bin ein Genie. Oder es war Zufall.", "Straight onto the boat! I'm a genius. Or it was luck.");
            S("wanda.bubble", Wanda, null,
                "Heute Rückenwind! Lieferung vielleicht sogar pünktlich.", "Tailwind today! Delivery might even be on time.",
                "Frisch getankt. Der Heli, nicht ich.", "Freshly fuelled. The chopper, not me.",
                "Ich hab heute nur zweimal falsch abgebogen. Rekord!", "Only took two wrong turns today. Record!",
                "Snacks gehen immer. Sag ich als Pilotin.", "Snacks always work. Pilot's advice.",
                "Bestell was Schweres, dann flieg ich langsamer. Romantisch.", "Order something heavy, I'll fly slower. Romantic.",
                "Die Ziege vom Dorf schaut mich komisch an.", "The village goat keeps looking at me funny.",
                "Heute keine Böen. Glaub ich. Ich hab nicht nachgesehen.", "No gusts today. I think. I didn't check.",
                "Express? Ich kann auch schnell. Und laut.", "Express? I can do fast. And loud.");
            S("wanda.talk", Wanda, null,
                "Mein Hangar ist auch mein Wohnzimmer. Und meine Werkstatt. Und meine Hängematte.", "My hangar is also my living room. And workshop. And hammock.",
                "Von oben sieht die Insel aus wie ein Pfannkuchen mit Palmen.", "From above, the island looks like a pancake with palm trees.",
                "Falls mal was schiefgeht: Folg dem Rauch. Da ist dann dein Paket.", "If anything goes wrong: follow the smoke. That's where your parcel is.");
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
                "Bitte nichts anfassen. Außer die Messstäbe. Die dürfen Sie.", "Please don't touch anything. Except the measuring stakes. Those you may.");
            L("fin.done", Fin, "Hervorragende Daten! Honorar ist überwiesen. Hm!", "Excellent data! Fee transferred. Hm!");
            L("fin.r1.done", Fin, "Drei Brassen! Die Carbon-Angel gibt's jetzt zum halben Preis.", "Three bream! The carbon rod is half price for you now.");
            L("fin.r4.done", Fin, "Ein Schwertfisch! Für die Wissenschaft! Hier – meine Forschungsrute.", "A swordfish! For science! Here – my research rod.");
            L("fin.new", Fin, "Neue Forschungsaufträge sind in der App. Hm? Hm!", "New research jobs are in the app. Hm? Hm!");
        }

        // ================================================================ B.3 Chad & Brad
        static void RivalLines()
        {
            L("rival.r1.1", Chad, "Yo! Chad Bottomley. Bottom Feeders. Zwei Millionen Abos. Und du so?", "Yo! Chad Bottomley. Bottom Feeders. Two million subs. You?");
            L("rival.r1.2", Brad, "Brad. Der Hübschere.", "Brad. The handsome one.");
            L("rival.r1.3", Chad, "Süße Insel. Süßes Boot. Ist das … Rost?", "Cute island. Cute boat. Is that… rust?");
            L("rival.r1.4", Brad, "Follower-Duell. Jede Woche. Verlierer muss …", "Follower duel. Every week. Loser has to…");
            L("rival.r1.5", Chad, "… verlieren. Genau. Clout Nine, ablegen!", "…lose. Exactly. Clout Nine, cast off!");
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
            L("rival.b1.start.1", Brad, "Bro. Unser goldener Selfie-Stick …", "Bro. Our golden selfie stick…");
            L("rival.b1.start.2", Chad, "… ist ins Hausriff gefallen. Sechs Meter. Wir tauchen nicht. Frisur.", "…fell into the house reef. Six metres. We don't dive. Hair.");
            L("rival.b1.start.3", Brad, "Bringst du ihn zurück? Wir würden dich sogar erwähnen. Vielleicht.", "Bring it back? We might even mention you. Maybe.");
            L("rival.b1.choice", Chad, "Und? Gibst du ihn zurück?", "So? You giving it back?");
            L("rival.b1.give.1", Brad, "Du bist ja … nett? Komisch. Danke, Bro.", "You're… nice? Weird. Thanks, bro.");
            L("rival.b1.give.2", Chad, "Hier, für deine Rostlaube. Nicht ausgeben. Doch, ausgeben.", "Here, for your rust bucket. Don't spend it. No, spend it.");
            L("rival.b1.keep.1", Chad, "Er behält ihn?! Bro.", "He's keeping it?! Bro.");
            L("rival.b1.keep.2", Brad, "BRO.", "BRO.");
        }

        // ================================================================ L.8 village Altriff
        static void VillageLines()
        {
            S("pearl.first", Pearl, V,
                "Gute Flut! Du bist also Gus' Erbe.", "Good tide! So you're Gus's heir.",
                "Gus hat uns die Hütte für drei Kokosnüsse abgekauft. Zwei waren hohl.", "Gus bought the hut from us for three coconuts. Two were hollow.",
                "In der Nacht, als sein Studio verschwand, war das Meer hell. Von unten.", "The night his studio vanished, the sea was bright. From below.",
                "Unser Kalender hat die Nacht markiert. Wir nennen sie die Augennacht.", "Our calendar marked that night. We call it the Night of the Eye.");
            S("pearl.afterP1", Pearl, V,
                "Die Pflöcke zählen die Fluten. In der Augennacht zählten wir eine zu viel.", "The pegs count the tides. On the Night of the Eye we counted one too many.",
                "Als ob das Meer einmal tief Luft geholt hätte.", "As if the sea took one deep breath.");
            S("pearl.afterP3", Pearl, V,
                "Damals trieben auch Laternen. Und unter ihnen leuchtete etwas zurück.", "Lanterns floated that night too. And something below shone back.",
                "Wir glauben: Das Licht kommt wieder, wenn sehr viele zuschauen.", "We believe the light returns when very many are watching.",
                "Eine Million, sagt Lio. Er hat nachgerechnet. Zweimal.", "A million, Lio says. He did the maths. Twice.");
            S("pearl.talk", Pearl, V,
                "Gute Flut! Setz dich, wenn du magst. Der Stuhl beißt nicht. Der Hund schon.", "Good tide! Sit if you like. The chair doesn't bite. The dog does.",
                "Horst schuldet mir 412 Fischbrötchen. Ich führe Buch.", "Horst owes me 412 fish rolls. I keep a ledger.",
                "Der Kalender sagt: Am nächsten Laternenabend wird das Wasser ruhig.", "The calendar says the water will be calm on the next Lantern Evening.",
                "Wir haben euren Kanal abonniert. Alle. Auch der Hund.", "We subscribed to your channel. All of us. The dog too.");
            L("pearl.clue.top", Pearl, "Gus sagte immer: Die beste Aussicht hat die Möwe.", "Gus always said: the seagull has the best view.", Clue);
            L("pearl.clue.kelp", Pearl, "Auf der Kelp-Insel hat Gus mal was in einen Baum geritzt. Ganz hinten, nur vom Wasser aus.", "On the kelp island Gus once carved something into a tree. Far side, only from the water.", Clue);
            L("pearl.clue.cellar", Pearl, "Gus hat nie verraten, was unter seinem Teppich liegt. Im Leuchtturm.", "Gus never told what's under his rug. In the lighthouse.", Clue);
            L("pearl.clue.loft", Pearl, "Beim Kartenspiel hat Gus immer seine Schuldscheine versteckt. Oben im Bootsschuppen.", "At cards Gus always hid his IOUs. Up in the boat shed.", Clue);
            S("mira.talk", Mira, V,
                "Ist das ein Boot oder ein Hilferuf?", "Is that a boat or a cry for help?",
                "Wir bauen gerade zwei Kanus. Eins schwimmt schon. Das andere übt noch.", "We're building two canoes. One floats already. The other is practising.",
                "Holz aus dem Meer ist das beste Holz. Hat schon Geschichten.", "Wood from the sea is the best wood. It already has stories.");
            S("tomas.talk", Tomas, V,
                "Gute Flut. Clownfische bleiben flach. Unter zwölf Metern suchst du die umsonst.", "Good tide. Clownfish stay shallow. Below twelve metres you look in vain.",
                "Zackenbarsche wohnen tief. Ab zwanzig Metern, am Wrack.", "Groupers live deep. From twenty metres, at the wreck.",
                "Kapitän Meck hat schon wieder ein Segel gefressen. Das dritte diese Woche.", "Captain Bleat ate another sail. Third one this week.",
                "Nachts kommen die Kraken raus. Mit Lampe siehst du mehr. Sie dich auch.", "At night the octopuses come out. With a lamp you see more. So do they.");
            S("ada.talk", Ada, V,
                "Gute Flut, Schätzchen! Hunger? Meine Soße ist nur ein bisschen scharf.", "Good tide, dear! Hungry? My sauce is only a little spicy.",
                "Ingwer-Limo gegen wackelige Hände. Für die Kamera, verstehst du?", "Ginger fizz for shaky hands. For the camera, you know?",
                "Horst isst hier jeden Tag. Er bezahlt nie. Er singt dafür. Schlecht.", "Horst eats here every day. Never pays. He sings instead. Badly.");
            L("ada.kiosk", Ada, "Such dir was aus! Alles frisch. Fast alles.", "Pick something! Everything's fresh. Almost everything.", V);
            L("ada.needfish", Ada, "Und die zwei Makrelen? Leg sie in deine Kühlbox, dann hol ich sie mir.", "And the two mackerel? Put them in your cooler and I'll fetch them.", V);
            S("juno.talk", Juno, V,
                "Mir fehlt schon wieder eine Marimba-Taste. Das A. Wer klaut ein A?", "My marimba is missing a key again. The A. Who steals an A?",
                "Am Laternenabend spiel ich. Komm vorbei, das Wasser leuchtet dann.", "I play on Lantern Evening. Come by, the water glows.",
                "Unter Wasser knistert das Riff. Das sind Knallkrebse. Beste Band der Insel.", "Under water the reef crackles. That's snapping shrimp. Best band on the island.");
            L("juno.clue.falls", Juno, "Hinter dem Wasserfall klingt's hohl. Musikerinnen hören so was.", "Behind the waterfall it sounds hollow. Musicians hear these things.", Clue);
            S("lio.talk", Lio, V,
                "Bist du berühmt? Wie berühmt? Berühmter als Horst?", "Are you famous? How famous? More famous than Horst?",
                "Ich bin Abonnent Nummer eins! Ich hab's mir auf den Arm geschrieben.", "I'm subscriber number one! I wrote it on my arm.",
                "Kann ich ein Selfie? Noch eins? Noch eins?", "Can I have a selfie? Another one? Another one?",
                "Wenn ich groß bin, werd ich Delfin. Oder Pilot. Oder beides.", "When I grow up I'll be a dolphin. Or a pilot. Or both.");
            L("lio.clue.ravine", Lio, "In der Schlucht hab ich was Rosa gesehen! Ganz unten! Hinter einem Stein!", "I saw something pink in the ravine! Right at the bottom! Behind a rock!", Clue);
            L("lio.crate", Lio, "Paket? Nur gegen Selfie! Das ist Zoll.", "Parcel? Only for a selfie! That's customs.", V);
            L("lio.cratedone", Lio, "Perfekt! Ich seh voll gut aus. Paket gehört dir!", "Perfect! I look great. The parcel is yours!", V);
            L("v.greet.1", NpcDatabase.Weaver1, "Gute Flut!", "Good tide!", V);
            L("v.greet.2", NpcDatabase.Weaver2, "Gute Flut! Pass auf die Matten auf, die sind noch nass.", "Good tide! Mind the mats, they're still wet.", V);
            L("v.greet.3", NpcDatabase.Fisher1, "Gute Flut. Heute beißen sie. Nur nicht bei mir.", "Good tide. They're biting today. Just not for me.", V);
            L("v.greet.4", NpcDatabase.Fisher2, "Gute Flut! Die Netze sind geflickt. Die Fische wissen's noch nicht.", "Good tide! Nets are mended. The fish don't know yet.", V);
            L("v.greet.5", NpcDatabase.Teen, "Gute Flut! Ich lern grad das Laternenlied. Zweiter Akkord fehlt noch.", "Good tide! Learning the lantern song. Still missing the second chord.", V);
            L("v.greet.6", NpcDatabase.Grandpa, "Mmh … Gute Flut … sag Perle, ich bin gleich wach.", "Mmh… good tide… tell Pearl I'm nearly awake.", V);
            S("village.lantern", Juno, V,
                "Laternenabend! Lass eine los und wünsch dir was Ruhiges.", "Lantern Evening! Let one go and wish for something calm.",
                "Und filmen ist erlaubt. Die Laternen sind sehr eitel.", "And filming is allowed. The lanterns are very vain.");
        }

        // ================================================================ K.8 Toni
        static void ToniLines()
        {
            S("toni.t1", Toni, null,
                "Ist das … Festland? Nein? Gut. Festland ist laut.", "Is this… the mainland? No? Good. The mainland is loud.",
                "Ich bin Toni. Ton-Mann. 1987, „Live aus der Tiefe“.", "I'm Tony. Sound guy. 1987, 'Live from the Deep'.",
                "Ich hab den letzten Drehtag verschlafen. Seitdem nehm ich singende Fische auf.", "I overslept the last day of shooting. Since then I record singing fish.",
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
                "Die Altriffer haben mir ein Zimmer gegeben. Mit Hängematte. Ich bin gerührt.", "The Oldreefers gave me a room. With a hammock. I'm touched.");
            L("toni.clue.buoy", Toni, "In der Blauwasser-Wand piept was. Unser alter Studio-Sender. Achtzehn Meter, im Spalt.", "Something beeps in the blue-water wall. Our old studio transmitter. Eighteen metres, in a crack.", Clue);
            L("toni.clue.archive", Toni, "In meiner Nebelgrotte liegt mein Tonarchiv. Tief, dunkel, trocken. Fast trocken.", "My sound archive is in the Fog Grotto. Deep, dark, dry. Mostly dry.", Clue);
        }

        // ================================================================ L.7 side quests (start 2–4 lines, end 1–2)
        static void SideQuestLines()
        {
            // Horst
            S("sq.H1.start", Horst, null,
                "Günther klaut. Jeden Mittag. Ich brauch Beweise.", "Günther steals. Every noon. I need evidence.",
                "Ein Foto von ihm, mit Essen im Schnabel. Am Steg, zwischen 12 und 14 Uhr.", "A photo of him with food in his beak. At the dock, between 12 and 2.");
            S("sq.H1.end", Horst, null, "Da! Erwischt! Ich häng's in den Hafen. Als Fahndungsplakat.", "There! Caught him! I'll hang it in the harbour. As a wanted poster.");
            S("sq.H2.start", Horst, null,
                "Meine Sonnenbrille ist ins Hafenbecken gefallen.", "My sunglasses fell into the harbour.",
                "Sie war teuer. Na ja – gefunden. Drei Meter tief, zwischen den Pfählen.", "They were expensive. Well, found. Three metres down, between the posts.");
            S("sq.H2.end", Horst, null, "Meine Brille! Ich seh wieder cool aus. Hab ich vorher auch, aber jetzt sieht man's.", "My shades! I look cool again. I did before, but now you can see it.");
            S("sq.H3.start", Horst, null,
                "Influencer-Workshop! Lektion eins: ein Selfie mit dem Coach.", "Influencer workshop! Lesson one: a selfie with the coach.",
                "Lektion zwei: ein Video mit ehrlichem Titel. Ehrlich ist das neue Clickbait.", "Lesson two: a video with an honest title. Honest is the new clickbait.");
            S("sq.H3.end", Horst, null, "Zertifiziert! Von mir. Das zählt. Irgendwo.", "Certified! By me. That counts. Somewhere.");
            // Wanda
            S("sq.W1.start", Wanda, null,
                "Mir sind drei Fässer vom Haken gerutscht. Die treiben jetzt in der Lagune.", "Three drums slipped off my hook. They're drifting in the lagoon now.",
                "Bringst du sie zum Hangar? Sind leicht. Fast.", "Can you bring them to the hangar? They're light. Almost.");
            S("sq.W1.end", Wanda, null, "Alle drei! Dafür flieg ich dir zweimal Express. Gratis. Pssst.", "All three! I'll fly you express twice for that. Free. Shh.");
            S("sq.W2.start", Wanda, null,
                "Ich mach einen Prospekt: „Rostbucht – Insel der Lieferungen“.", "I'm making a brochure: 'Rust Cove – island of deliveries'.",
                "Ich brauch Fotos vom Wasser aus: Leuchtturm, Hütte und das Dorf.", "I need photos from the water: lighthouse, hut and the village.");
            S("sq.W2.end", Wanda, null, "Wunderschön! Ich setz mein Gesicht drauf. Ganz groß.", "Gorgeous! I'll put my face on it. Really big.");
            // Pearl
            S("sq.P1.start", Pearl, V,
                "Unser Gezeitenkalender hat drei Pflöcke verloren. Geschnitzt, so groß wie ein Arm.", "Our tide calendar lost three pegs. Carved, as long as an arm.",
                "Einer am Strand, einer bei den Gezeitentümpeln, einer in der Lagune, zwei Meter tief.", "One on the beach, one at the tide pools, one in the lagoon, two metres down.");
            S("sq.P1.end", Pearl, V, "Jetzt zählt der Kalender wieder richtig. Setz dich – ich erzähl dir was.", "Now the calendar counts right again. Sit down – I'll tell you something.");
            S("sq.P2.start", Pearl, V,
                "Heute ist Kartenabend. Aber mein Gegner drückt sich.", "It's card night. But my opponent is hiding.",
                "Bring mir Horst. Ab 18 Uhr, zum Versammlungshaus.", "Bring me Horst. From 6 pm, to the meeting house.");
            S("sq.P2.end", Pearl, V, "Gewonnen. Schon wieder. Hier, mein alter Strohhut. Der hat Glück.", "Won. Again. Here, my old straw hat. It's lucky.");
            S("sq.P3.start", Pearl, V,
                "Heute ist Laternenabend. Lass eine Laterne los …", "It's Lantern Evening. Let a lantern go…",
                "… und film sie vom Wasser aus. Fünf Lichter, mindestens.", "…and film them from the water. Five lights, at least.");
            S("sq.P3.end", Pearl, V, "So sah es 1987 aus. Nur dass damals etwas zurückgeleuchtet hat.", "That's how it looked in 1987. Except back then, something shone back.");
            // Mira
            S("sq.M1.start", Mira, V,
                "Ich brauch Treibholz für das neue Kanu. Fünf gute Stücke.", "I need driftwood for the new canoe. Five good pieces.",
                "Liegt an den Stränden rum. Das krumme ist das beste.", "It's lying around the beaches. The crooked ones are the best.");
            S("sq.M1.end", Mira, V, "Perfekt krumm! Daraus wird ein Bug. Oder ein Stuhl. Mal sehen.", "Perfectly crooked! That'll be a bow. Or a chair. We'll see.");
            S("sq.M2.start", Mira, V,
                "Gus hatte ein Paddel. Legendär. Er hat's in der Bucht-Höhle versteckt.", "Gus had a paddle. Legendary. He hid it in the cove cave.",
                "Bring's in den Bootsschuppen. Es soll ins neue Kanu.", "Bring it to the boat shed. It belongs in the new canoe.");
            S("sq.M2.end", Mira, V, "Gus' Paddel! Dafür bekommst du eine Flagge. Für deine … Rostlaube.", "Gus's paddle! You get a flag for that. For your… rust bucket.");
            S("sq.M3.start", Mira, V,
                "Das Kanu ist fertig! Morgen um 10 Uhr: das erste Paddeln.", "The canoe is done! Tomorrow at 10: First Paddle.",
                "Das ganze Dorf paddelt einmal um die Lagune. Filmst du's?", "The whole village paddles once round the lagoon. Will you film it?");
            S("sq.M3.end", Mira, V, "Hast du's? Wir sind berühmt! Hier, unsere Flagge. Für dein Boot.", "Got it? We're famous! Here, our flag. For your boat.");
            // Tomas
            S("sq.T1.start", Tomas, V,
                "Am Hausriff hängt ein altes Geisternetz. Da verfangen sich die Fische.", "There's an old ghost net on the house reef. Fish get tangled in it.",
                "Drei Stellen. {K:Interact} halten, bis es ab ist. Fünf Sekunden pro Stelle.", "Three spots. Hold {K:Interact} until it's off. Five seconds each.");
            S("sq.T1.end", Tomas, V, "Siehst du? Jetzt kommen sie zurück. Fische merken sich so was.", "See? Now they're coming back. Fish remember things like that.");
            S("sq.T2.start", Tomas, V,
                "Wie tief wohnt eigentlich der Zackenbarsch? Ich wette: unter 25 Metern.", "How deep does the grouper actually live? I bet: below 25 metres.",
                "Bring mir ein Foto als Beweis. Am Wrack findest du ihn.", "Bring me a photo as proof. You'll find him at the wreck.");
            S("sq.T2.end", Tomas, V, "Wusst ich's doch. Hier – ich trag dir drei Tiefen ins Sealog ein.", "Knew it. Here – I'll mark three depths in your Sealog.");
            S("sq.T3.start", Tomas, V,
                "Der große Fang: Ein Fisch über drei Kilo. Vom Boot aus.", "The big catch: a fish over three kilos. From the boat.",
                "Mit der Bambusrute wird das nix. Carbon, mindestens.", "Not with the bamboo rod. Carbon, at least.");
            S("sq.T3.end", Tomas, V, "Das ist ein Fisch! Fast so groß wie meine Geschichten.", "That's a fish! Almost as big as my stories.");
            // Ada
            S("sq.A1.start", Ada, V,
                "Ich koch Kokos-Fischcurry! Mir fehlen drei Kokosnüsse und zwei Makrelen.", "I'm cooking coconut fish curry! I'm missing three coconuts and two mackerel.",
                "Die Makrelen nehm ich aus deiner Kühlbox. Die Nüsse bringst du.", "I'll take the mackerel from your cooler. You bring the coconuts.");
            S("sq.A1.end", Ada, V, "Riecht das gut! Ab heute steht's auf der Karte. Nur ein bisschen scharf.", "Smells great! It's on the menu from today. Only a little spicy.");
            S("sq.A2.start", Ada, V,
                "Seetrauben! Die wachsen im Seegras der Lagune, ein bis drei Meter tief.", "Sea grapes! They grow in the lagoon sea grass, one to three metres deep.",
                "Pflück mir sechs. Nicht naschen.", "Pick me six. No snacking.");
            S("sq.A2.end", Ada, V, "Knackig! Seetrauben-Salat ab sofort im Kiosk.", "Crunchy! Sea grape salad at the kiosk from now on.");
            // Juno
            S("sq.J1.start", Juno, V,
                "Ich will ein Lied über den Walgesang schreiben.", "I want to write a song about the whale song.",
                "Filmst du Big Bertha, wenn sie singt? An der Blauwasser-Kante.", "Will you film Big Bertha while she sings? At the blue-water drop.");
            S("sq.J1.end", Juno, V, "Das ist es! Das Altriff-Lied. Hör mal, im Menü. Ab jetzt.", "That's it! The Oldreef song. Listen, in the menu. From now on.");
            S("sq.J2.start", Juno, V,
                "Die Knallkrebse am Hausriff spielen nachts ein Konzert.", "The snapping shrimp at the house reef play a concert at night.",
                "Nimm zwanzig Sekunden auf. Unter Wasser, nach 21 Uhr.", "Record twenty seconds. Under water, after 9 pm.");
            S("sq.J2.end", Juno, V, "Knister, knaster! Das wird mein Schlagzeug.", "Crackle, crackle! That'll be my drums.");
            // Lio
            S("sq.L1.start", Lio, V,
                "Mein Drachen! Er hängt oben am Leuchtturm, am Geländer!", "My kite! It's stuck at the top of the lighthouse, on the railing!",
                "Holst du ihn? Bitte, bitte, bitte?", "Will you get it? Please, please, please?");
            S("sq.L1.end", Lio, V, "MEIN DRACHEN! Selfie! Jetzt! Danke!", "MY KITE! Selfie! Now! Thanks!");
            S("sq.L2.start", Lio, V,
                "Ich will einen echten Hai sehen! In einem Video! Von dir!", "I want to see a real shark! In a video! By you!",
                "Lad eins hoch, mit Hai. Bitte.", "Upload one with a shark. Please.");
            S("sq.L2.end", Lio, V, "EIN HAI! Hier, meine Krone. Aus Papier. Selbst gebastelt.", "A SHARK! Here, my crown. Paper. Made it myself.");
            S("sq.L3.start", Lio, V,
                "Ich hab eine Schatzkarte gemalt! Mit Buntstift! Und sie stimmt!", "I drew a treasure map! In crayon! And it's real!",
                "Das X ist da, wo das X ist. Folg den Pfeilen.", "The X is where the X is. Follow the arrows.");
            S("sq.L3.end", Lio, V, "Echt?! Ein echter Schatz! Ich bin ein Genie!", "Really?! A real treasure! I'm a genius!");
            // Toni
            S("sq.N1.start", Toni, null,
                "Ich mach ein Album: „Stimmen der Lagune“.", "I'm making an album: 'Voices of the Lagoon'.",
                "Film klickende Delfine, einen bellenden Seelöwen und den singenden Wal.", "Film clicking dolphins, a barking sea lion and the singing whale.");
            S("sq.N1.end", Toni, null, "Mmm-hmm. Das ist Musik. Das ist ein Hit.", "Mmm-hmm. That's music. That's a hit.");
            S("sq.N2.start", Toni, null,
                "Ein Band fehlt noch. Das letzte von 1987.", "One tape is still missing. The last one from 1987.",
                "Es liegt am Antennenmast im Graben. Zweiundneunzig Meter. Bring es mir.", "It's at the antenna mast in the trench. Ninety-two metres. Bring it to me.");
            S("sq.N2.end", Toni, null,
                "Das letzte Band. Hörst du? Da … atmet etwas.", "The last tape. Hear that? Something… breathes.",
                "Wenn du live gehst – da unten – dann hört es dich.", "When you go live – down there – it will hear you.");
            // Chad & Brad B1 (rival.b1.* above)
            // generic giver responses
            L("sq.later.v", NpcDatabase.Pearl, "Kein Problem. Das Meer wartet auch.", "No problem. The sea waits too.", V);
            L("sq.accept.v", NpcDatabase.Pearl, "Gute Flut für dich!", "Good tide to you!", V);
        }
    }
}
