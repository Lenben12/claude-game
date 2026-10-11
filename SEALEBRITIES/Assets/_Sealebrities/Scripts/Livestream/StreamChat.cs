using UnityEngine;

namespace Sealebrities
{
    /// Chat topics (C.6). The host decides WHAT is said (line index + arguments); every machine localizes it.
    public enum ChatCat : byte
    {
        Hello, Generic, Species, Big, Record, New, Legendary, NoCamera, Danger, Bump, LowAir, Boring, DareOffer, DareDone,
        DareFail, Donation, Gerald, Hater, Lost, NoSignal, Deep, Night, Selfie, Coop
    }

    /// One chat line template: {s} = species name, {p} = a crew member's name. No emoji (Fredoka has none).
    public struct ChatLine
    {
        public ChatCat cat;
        public ChatMood mood;
        public string de, en;
        public bool NeedsSpecies => de.IndexOf("{s}", System.StringComparison.Ordinal) >= 0;
        public bool NeedsPlayer => de.IndexOf("{p}", System.StringComparison.Ordinal) >= 0;
    }

    /// What the host emits: a line + its arguments (sent as StreamChat 92, localized on each machine).
    public struct ChatEmit
    {
        public int line;
        public int user;
        public long donation;
        /// Species index (StreamEye.IndexOf) for {s}, -1 = none.
        public int species;
        /// Net id for {p}, -1 = none.
        public int player;
    }

    /// The fictional KRILL.tv chat (C.6): usernames, the line table and the host-side director that picks lines from what
    /// the cameras see (species, size, danger, fails, air, boredom, dares, donations, Gerald, haters).
    public static class StreamChat
    {
        public static readonly string[] Users =
        {
            "krabbenkalle", "SeegurkenSusi", "xX_Hai_Xx", "Opa_Plankton", "bubbleboi", "TintenTina", "MuschelMax", "algen_alex",
            "FlossenFred", "QuallenQueen", "gerald_fanclub", "DeepDiveDieter", "KorallenKai", "nemo_not_found", "SardinenSandra",
            "PlanktonPaul", "MantaMia", "tauchtante", "RiffRalf", "KiemenKarl", "wellenwilli", "Lotte_Lagune", "salzwasserSam", "BlubbBernd"
        };

        static ChatLine L(ChatCat c, ChatMood m, string de, string en) => new ChatLine { cat = c, mood = m, de = de, en = en };

        public static readonly ChatLine[] Lines =
        {
            // hello
            L(ChatCat.Hello, ChatMood.Hype, "Hallo aus dem Binnenland!", "Hello from inland!"),
            L(ChatCat.Hello, ChatMood.Hype, "erster!!", "first!!"),
            L(ChatCat.Hello, ChatMood.Hype, "LIVE!!! endlich", "LIVE!!! finally"),
            L(ChatCat.Hello, ChatMood.Neutral, "Grüße an Horst", "say hi to Horst"),
            L(ChatCat.Hello, ChatMood.Neutral, "ist Gerald heute dabei?", "is Gerald on today?"),
            // generic
            L(ChatCat.Generic, ChatMood.Neutral, "wie tief bist du?", "how deep are you?"),
            L(ChatCat.Generic, ChatMood.Neutral, "welche Kamera ist das?", "what camera is that?"),
            L(ChatCat.Generic, ChatMood.Neutral, "schönes Wasser heute", "water looks nice today"),
            L(ChatCat.Generic, ChatMood.Neutral, "Gus schaut bestimmt zu", "Gus is watching for sure"),
            L(ChatCat.Generic, ChatMood.Laugh, "mein Goldfisch schaut mit", "my goldfish is watching too"),
            L(ChatCat.Generic, ChatMood.Laugh, "ich sollte eigentlich lernen", "I should be studying"),
            L(ChatCat.Generic, ChatMood.Hype, "<3 <3 <3", "<3 <3 <3"),
            L(ChatCat.Generic, ChatMood.Laugh, "Grüße aus der Badewanne", "greetings from my bathtub"),
            L(ChatCat.Generic, ChatMood.Neutral, "das Wasser ist so blau", "the water is so blue"),
            // species on camera
            L(ChatCat.Species, ChatMood.Hype, "{s}!!", "{s}!!"),
            L(ChatCat.Species, ChatMood.Neutral, "{s}?! Echt jetzt?", "{s}?! for real?"),
            L(ChatCat.Species, ChatMood.Hype, "{s} sieht heute gut aus", "{s} looking good today"),
            L(ChatCat.Species, ChatMood.Neutral, "Zoom auf {s}!", "zoom in on the {s}!"),
            L(ChatCat.Species, ChatMood.Laugh, "{s} hat mehr Follower als ich", "{s} has more followers than me"),
            L(ChatCat.Species, ChatMood.Hype, "hallo {s}", "hi {s}"),
            // big
            L(ChatCat.Big, ChatMood.Hype, "DER IST RIESIG", "HE'S HUGE"),
            L(ChatCat.Big, ChatMood.Hype, "Maßband raus!", "get the tape measure!"),
            L(ChatCat.Big, ChatMood.Hype, "{s} XXL", "{s} XXL"),
            L(ChatCat.Big, ChatMood.Laugh, "das ist kein Fisch, das ist ein Bus", "that's not a fish, that's a bus"),
            // record
            L(ChatCat.Record, ChatMood.Hype, "REKORD!!!", "RECORD!!!"),
            L(ChatCat.Record, ChatMood.Hype, "neuer Rekord, ich hab's gesehen!", "new record, I saw it!"),
            L(ChatCat.Record, ChatMood.Hype, "{s} in XXL – Rekord!", "{s} XXL – record!"),
            // new species
            L(ChatCat.New, ChatMood.Hype, "NEU IM SEALOG!", "NEW IN THE SEALOG!"),
            L(ChatCat.New, ChatMood.Hype, "noch nie gesehen!", "never seen that before!"),
            L(ChatCat.New, ChatMood.Neutral, "Dr. Fin, schau her!", "Dr. Fin, look!"),
            L(ChatCat.New, ChatMood.Hype, "was ist DAS denn?", "what is THAT?"),
            // legendary
            L(ChatCat.Legendary, ChatMood.Hype, "LEGENDÄR!!!", "LEGENDARY!!!"),
            L(ChatCat.Legendary, ChatMood.Hype, "Clip it! CLIP IT!", "clip it! CLIP IT!"),
            L(ChatCat.Legendary, ChatMood.Laugh, "mein Bildschirm ist zu klein dafür", "my screen is too small for this"),
            // camera not in hand
            L(ChatCat.NoCamera, ChatMood.Neutral, "Kamera weg?", "camera gone?"),
            L(ChatCat.NoCamera, ChatMood.Laugh, "wir sehen nur Blasen", "all we see is bubbles"),
            L(ChatCat.NoCamera, ChatMood.Neutral, "hallo?? Bild??", "hello?? picture??"),
            L(ChatCat.NoCamera, ChatMood.Laugh, "ist das dein Daumen?", "is that your thumb?"),
            // danger close
            L(ChatCat.Danger, ChatMood.Scared, "hinter dir!!!", "behind you!!!"),
            L(ChatCat.Danger, ChatMood.Scared, "NICHT ANFASSEN", "DON'T TOUCH IT"),
            L(ChatCat.Danger, ChatMood.Scared, "ich kann nicht hinsehen", "I can't watch"),
            L(ChatCat.Danger, ChatMood.Scared, "{s}!! weg da!", "{s}!! get away!"),
            // bumped / fail
            L(ChatCat.Bump, ChatMood.Laugh, "AUA", "OUCH"),
            L(ChatCat.Bump, ChatMood.Laugh, "hahaha", "hahaha"),
            L(ChatCat.Bump, ChatMood.Laugh, "Clip it!", "clip it!"),
            L(ChatCat.Bump, ChatMood.Laugh, "{s} so: nö", "{s} said no"),
            L(ChatCat.Bump, ChatMood.Laugh, "Abzug in der B-Note", "minus points for style"),
            // low air
            L(ChatCat.LowAir, ChatMood.Scared, "Luft??", "air??"),
            L(ChatCat.LowAir, ChatMood.Scared, "tauch auf!!", "go up!!"),
            L(ChatCat.LowAir, ChatMood.Scared, "das wird knapp", "this is gonna be close"),
            L(ChatCat.LowAir, ChatMood.Scared, "ich halte für dich die Luft an", "holding my breath for you"),
            // boring
            L(ChatCat.Boring, ChatMood.Neutral, "…", "…"),
            L(ChatCat.Boring, ChatMood.Laugh, "Sand. Spannend.", "sand. riveting."),
            L(ChatCat.Boring, ChatMood.Neutral, "mehr Fische bitte", "more fish pls"),
            L(ChatCat.Boring, ChatMood.Laugh, "schlaf ich?", "am I asleep?"),
            L(ChatCat.Boring, ChatMood.Laugh, "Wasser. Viel Wasser.", "water. lots of water."),
            // dares
            L(ChatCat.DareOffer, ChatMood.Hype, "Mutprobe! Mutprobe!", "dare! dare!"),
            L(ChatCat.DareOffer, ChatMood.Hype, "trau dich!", "do it!"),
            L(ChatCat.DareOffer, ChatMood.Hype, "mach es, mach es!", "do it, do it!"),
            L(ChatCat.DareOffer, ChatMood.Neutral, "ich spende, wenn du's machst", "I'll tip if you do it"),
            L(ChatCat.DareDone, ChatMood.Hype, "GESCHAFFT!!!", "YOU DID IT!!!"),
            L(ChatCat.DareDone, ChatMood.Hype, "LEGENDE", "LEGEND"),
            L(ChatCat.DareDone, ChatMood.Hype, "Respekt!", "respect!"),
            L(ChatCat.DareDone, ChatMood.Hype, "das war irre", "that was insane"),
            L(ChatCat.DareFail, ChatMood.Laugh, "buuuh", "booo"),
            L(ChatCat.DareFail, ChatMood.Laugh, "knapp daneben", "so close"),
            L(ChatCat.DareFail, ChatMood.Neutral, "nächstes Mal!", "next time!"),
            // donations (the bubble shows the amount)
            L(ChatCat.Donation, ChatMood.Donation, "Mehr Hai!", "more shark!"),
            L(ChatCat.Donation, ChatMood.Donation, "für die Luft-Rechnung", "for the air bill"),
            L(ChatCat.Donation, ChatMood.Donation, "kauf Gerald einen Hut", "buy Gerald a hat"),
            L(ChatCat.Donation, ChatMood.Donation, "für Horsts Fischbrötchen", "for Horst's fish rolls"),
            L(ChatCat.Donation, ChatMood.Donation, "weiter so!", "keep it up!"),
            L(ChatCat.Donation, ChatMood.Donation, "für neue Flossen", "for new fins"),
            L(ChatCat.Donation, ChatMood.Donation, "mein Taschengeld, nicht verraten", "my allowance, don't tell"),
            // Gerald
            L(ChatCat.Gerald, ChatMood.Hype, "GERALD!!!", "GERALD!!!"),
            L(ChatCat.Gerald, ChatMood.Hype, "König Gerald", "King Gerald"),
            L(ChatCat.Gerald, ChatMood.Hype, "wir lieben dich Gerald", "we love you Gerald"),
            L(ChatCat.Gerald, ChatMood.Laugh, "Gerald ist KEIN CGI", "Gerald is NOT CGI"),
            // haters (clickbait history / poking animals, GDD 14.40)
            L(ChatCat.Hater, ChatMood.Hater, "Clickbait-Kanal", "clickbait channel"),
            L(ChatCat.Hater, ChatMood.Hater, "lass die Tiere in Ruhe", "leave the animals alone"),
            L(ChatCat.Hater, ChatMood.Hater, "früher war mehr Hai", "there used to be more shark"),
            L(ChatCat.Hater, ChatMood.Hater, "die Bottom-Brüder sind besser", "the Bottom brothers are better"),
            // stream lost
            L(ChatCat.Lost, ChatMood.System, "F", "F"),
            L(ChatCat.Lost, ChatMood.System, "F", "F"),
            L(ChatCat.Lost, ChatMood.System, "F im Chat", "F in chat"),
            L(ChatCat.Lost, ChatMood.System, "RIP Stream", "RIP stream"),
            // no signal
            L(ChatCat.NoSignal, ChatMood.Scared, "Bild hängt …", "stream's frozen …"),
            L(ChatCat.NoSignal, ChatMood.Scared, "Lag??", "lag??"),
            L(ChatCat.NoSignal, ChatMood.Scared, "Kein Signal?!", "no signal?!"),
            // deep / night / selfie / co-op
            L(ChatCat.Deep, ChatMood.Scared, "wie tief ist das??", "how deep is that??"),
            L(ChatCat.Deep, ChatMood.Scared, "so dunkel hier unten", "so dark down here"),
            L(ChatCat.Night, ChatMood.Scared, "nachts ist das Meer gruselig", "the sea is creepy at night"),
            L(ChatCat.Night, ChatMood.Neutral, "Lampe an!", "lamp on!"),
            L(ChatCat.Selfie, ChatMood.Hype, "Selfie!!", "selfie!!"),
            L(ChatCat.Selfie, ChatMood.Hype, "du siehst gut aus", "you look good"),
            L(ChatCat.Selfie, ChatMood.Laugh, "Grimasse!", "make a face!"),
            L(ChatCat.Coop, ChatMood.Hype, "Grüße an {p}!", "hi {p}!"),
            L(ChatCat.Coop, ChatMood.Neutral, "{p} ist mein Lieblingstaucher", "{p} is my favourite diver"),
        };

        /// Builds the localized bubble for an emitted line (allocates one string – event driven, never per frame).
        public static ChatMessage Format(in ChatEmit e)
        {
            var m = new ChatMessage();
            m.user = e.user >= 0 && e.user < Users.Length ? Users[e.user] : "krill";
            if (e.line < 0 || e.line >= Lines.Length) { m.text = ""; return m; }
            var l = Lines[e.line];
            string t = Loc.Tr(l.de, l.en);
            if (t.IndexOf('{') >= 0)
            {
                var sp = StreamEye.SpeciesAt(e.species);
                t = t.Replace("{s}", sp != null ? sp.Name : Loc.Tr("Fisch", "fish"));
                var p = e.player >= 0 ? Net.Find(e.player) : null;
                t = t.Replace("{p}", p != null && !string.IsNullOrEmpty(p.name) ? p.name : Loc.Tr("Taucher", "diver"));
            }
            m.text = t;
            m.mood = l.mood;
            m.donation = e.donation;
            return m;
        }
    }

    /// Host side of the chat: rate from hype and viewers, reactions to events first, otherwise lines that fit what the
    /// cameras see. Lines never repeat within 60 s. Allocation free.
    public sealed class ChatDirector
    {
        public struct Context
        {
            public float hype;
            public int viewers;
            public bool anyCamera;
            /// Species index of the best animal on any camera (-1 none).
            public int species;
            public bool bored, lowAir, night, deep, selfie, noSignal, hater, coop;
            public int coopPlayer;
        }

        struct Intent { public ChatCat cat; public int species; public int prio; public float until; }

        readonly float[] lastUsed = new float[StreamChat.Lines.Length];
        readonly Intent[] queue = new Intent[8];
        int queued;
        int lastUser = -1;
        float timer;
        System.Random rng = new System.Random(1);

        public void Reset(int seed)
        {
            rng = new System.Random(seed);
            for (int i = 0; i < lastUsed.Length; i++) lastUsed[i] = -1000f;
            queued = 0;
            lastUser = -1;
            timer = 1.2f;
        }

        /// A reaction to an event. Urgent ones are said within ~0.4 s (the chat "explodes").
        public void Push(ChatCat cat, int species, int prio, float now, bool urgent)
        {
            if (queued >= queue.Length)
            {
                // drop the weakest
                int weakest = 0;
                for (int i = 1; i < queued; i++) if (queue[i].prio < queue[weakest].prio) weakest = i;
                if (queue[weakest].prio > prio) return;
                queue[weakest] = queue[--queued];
            }
            queue[queued++] = new Intent { cat = cat, species = species, prio = prio, until = now + 6f };
            if (urgent) timer = Mathf.Min(timer, 0.25f + (float)rng.NextDouble() * 0.35f);
        }

        /// True when a line should be said now (e = what).
        public bool Tick(float dt, float now, in Context ctx, out ChatEmit e)
        {
            e = default;
            timer -= dt;
            if (timer > 0f) return false;
            timer = StreamRules.ChatInterval(ctx.hype, ctx.viewers) * (0.8f + (float)rng.NextDouble() * 0.4f);

            // 1) pending reactions, highest priority first
            int bestI = -1;
            for (int i = queued - 1; i >= 0; i--)
            {
                if (queue[i].until < now)
                {
                    queue[i] = queue[--queued];
                    if (bestI == queued) bestI = i;   // the moved entry was the best so far
                    continue;
                }
                if (bestI < 0 || queue[i].prio > queue[bestI].prio) bestI = i;
            }
            if (bestI >= 0)
            {
                var it = queue[bestI];
                queue[bestI] = queue[--queued];
                // big moments get a burst: say it and keep the timer short
                if (it.prio >= 3) timer = Mathf.Min(timer, 0.5f + (float)rng.NextDouble() * 0.4f);
                return Emit(it.cat, it.species, ctx, now, out e);
            }

            // 2) what the cameras show
            ChatCat cat;
            float r = (float)rng.NextDouble();
            if (ctx.noSignal) cat = ChatCat.NoSignal;
            else if (!ctx.anyCamera) cat = r < 0.6f ? ChatCat.NoCamera : ChatCat.Generic;
            else if (ctx.lowAir && r < 0.35f) cat = ChatCat.LowAir;
            else if (ctx.hater && r < 0.08f) cat = ChatCat.Hater;
            else if (ctx.coop && r > 0.94f) cat = ChatCat.Coop;
            else if (ctx.selfie && r < 0.25f) cat = ChatCat.Selfie;
            else if (ctx.species >= 0 && r < 0.62f) cat = ChatCat.Species;
            else if (ctx.bored) cat = r < 0.7f ? ChatCat.Boring : ChatCat.Generic;
            else if (ctx.deep && r < 0.75f) cat = ChatCat.Deep;
            else if (ctx.night && r < 0.75f) cat = ChatCat.Night;
            else cat = ChatCat.Generic;
            return Emit(cat, ctx.species, ctx, now, out e);
        }

        /// A donation bubble (amount already decided by the host's money model).
        public ChatEmit Donation(long amount, float now)
        {
            var ctx = default(Context);
            ctx.species = -1;
            Emit(ChatCat.Donation, -1, ctx, now, out var e);
            e.donation = amount;
            return e;
        }

        /// Picks a line of a category right now (used for the "F" wall and dare reactions).
        public bool Say(ChatCat cat, int species, in Context ctx, float now, out ChatEmit e) => Emit(cat, species, ctx, now, out e);

        bool Emit(ChatCat cat, int species, in Context ctx, float now, out ChatEmit e)
        {
            e = default;
            e.species = species;
            e.player = ctx.coop ? ctx.coopPlayer : -1;
            int pick = PickLine(cat, species >= 0, ctx.coop && ctx.coopPlayer >= 0, now, true);
            if (pick < 0) pick = PickLine(cat, species >= 0, ctx.coop && ctx.coopPlayer >= 0, now, false);
            if (pick < 0) return false;
            lastUsed[pick] = now;
            e.line = pick;
            e.user = PickUser(cat);
            return true;
        }

        int PickLine(ChatCat cat, bool hasSpecies, bool hasPlayer, float now, bool respectRepeat)
        {
            var lines = StreamChat.Lines;
            int count = 0;
            for (int i = 0; i < lines.Length; i++)
                if (Usable(lines[i], cat, hasSpecies, hasPlayer) && (!respectRepeat || now - lastUsed[i] >= StreamRules.ChatNoRepeatSeconds)) count++;
            if (count == 0) return -1;
            int target = rng.Next(count);
            if (!respectRepeat)
            {
                // least recently used instead of random when everything was said recently
                int lru = -1;
                for (int i = 0; i < lines.Length; i++)
                    if (Usable(lines[i], cat, hasSpecies, hasPlayer) && (lru < 0 || lastUsed[i] < lastUsed[lru])) lru = i;
                return lru;
            }
            for (int i = 0; i < lines.Length; i++)
            {
                if (!Usable(lines[i], cat, hasSpecies, hasPlayer) || now - lastUsed[i] < StreamRules.ChatNoRepeatSeconds) continue;
                if (target-- == 0) return i;
            }
            return -1;
        }

        static bool Usable(in ChatLine l, ChatCat cat, bool hasSpecies, bool hasPlayer) =>
            l.cat == cat && (!l.NeedsSpecies || hasSpecies) && (!l.NeedsPlayer || hasPlayer);

        int PickUser(ChatCat cat)
        {
            int n = StreamChat.Users.Length;
            if (cat == ChatCat.Gerald && rng.NextDouble() < 0.5) return 10;   // gerald_fanclub
            int u = rng.Next(n);
            if (u == lastUser) u = (u + 1 + rng.Next(n - 1)) % n;
            lastUser = u;
            return u;
        }
    }
}
