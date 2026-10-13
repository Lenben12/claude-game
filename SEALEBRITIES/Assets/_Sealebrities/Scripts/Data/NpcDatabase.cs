using UnityEngine;

namespace Sealebrities
{
    public enum NpcKind
    {
        /// Main story cast (Horst, Wanda, Dr. Fin, the Bottom brothers, Toni).
        Story,
        /// Altriff villagers who gave quests (Pearl, Mira, Tomas, Ada, Juno, Lio) – unused since round 3 (Retired).
        Villager,
        /// Altriff background residents – unused since round 3 (Retired).
        Background,
        /// Only heard on the radio (Uncle Gus).
        Radio,
        /// Round 3: the eccentric dropout hermits of the hidden jungle camp (moods, sign language, crafts, barter).
        Hermit,
        /// Round 3: the village Altriff was removed (GDD §19) – its people keep their network index but get no body.
        Retired
    }

    /// One story NPC (A, B, K.5, K.8, L.7, L.8). Looks are built by NpcBuilder from the id; voices by NpcBabble.
    public sealed class NpcDef
    {
        public string id;
        public string nameDe, nameEn;
        /// Short role shown under the name on the subtitle / radio card.
        public string roleDe, roleEn;
        public NpcKind kind;
        /// Subtitle colour of the speaker name (I "subtitles").
        public Color color;
        /// Babble voice (Q60): base pitch in Hz, syllables per second, vowel set (0 bubbly o/u/a, 1 bright a/e/i,
        /// 2 mixed, 3 soft hum-heavy), radio = band-pass + crackle (Gus).
        public float pitch, rate;
        public int vowels;
        public bool radio;
        /// Body height in metres (bean incl. head, without hat).
        public float height = 1.75f;

        public string Name => Loc.Tr(nameDe, nameEn);
        public string Role => Loc.Tr(roleDe, roleEn);
    }

    /// All NPCs. The array index is the network index of the actor (NpcNet) – APPEND new NPCs at the end.
    public static class NpcDatabase
    {
        public const string Horst = "horst", Gus = "gus", Wanda = "wanda", Fin = "fin", Chad = "chad", Brad = "brad",
            Pearl = "pearl", Mira = "mira", Tomas = "tomas", Ada = "ada", Juno = "juno", Lio = "lio", Toni = "toni",
            Weaver1 = "v_weaver1", Weaver2 = "v_weaver2", Fisher1 = "v_fisher1", Fisher2 = "v_fisher2", Teen = "v_teen", Grandpa = "v_grandpa",
            /// Pseudo speaker: the hermits together (the hum at the lantern ritual).
            Crowd = "crowd",
            /// Round 3 (GDD §19/§20 Q2/Q3): the six hermits of the hidden jungle camp – nicknames Horst gave them (they do
            /// not speak; since the Night of the Eye they keep a vow of silence and talk in signs).
            HLeader = "h_leader", HSmith = "h_smith", HHunter = "h_hunter", HCook = "h_cook", HLookout = "h_lookout", HGardener = "h_gardener";

        /// The hermits in role order (leader, smith, hunter, cook, lookout, gardener).
        public static readonly string[] Hermits = { HLeader, HSmith, HHunter, HCook, HLookout, HGardener };

        public static bool IsHermit(string id)
        {
            if (id == null || id.Length < 3 || id[0] != 'h' || id[1] != '_') return false;
            for (int i = 0; i < Hermits.Length; i++) if (Hermits[i] == id) return true;
            return false;
        }

        /// Role index 0..5 of a hermit (−1 for everybody else).
        public static int HermitIndex(string id)
        {
            for (int i = 0; i < Hermits.Length; i++) if (Hermits[i] == id) return i;
            return -1;
        }

        static Color C(string hex) => LowPoly.Hex(hex);

        public static readonly NpcDef[] All =
        {
            new NpcDef { id = Horst, nameDe = "Horst", nameEn = "Horst", roleDe = "Hafenmeister", roleEn = "Harbourmaster", kind = NpcKind.Story,
                color = C("#E07B39"), pitch = 140f, rate = 11f, vowels = 0, height = 1.70f },
            new NpcDef { id = Gus, nameDe = "Onkel Gus", nameEn = "Uncle Gus", roleDe = "Funk", roleEn = "Radio", kind = NpcKind.Radio,
                color = C("#3FA9B5"), pitch = 95f, rate = 8f, vowels = 2, radio = true },
            new NpcDef { id = Wanda, nameDe = "Wanda", nameEn = "Wanda", roleDe = "Pilotin", roleEn = "Pilot", kind = NpcKind.Story,
                color = C("#E0607E"), pitch = 205f, rate = 12f, vowels = 1, height = 1.78f },
            new NpcDef { id = Fin, nameDe = "Dr. Fin", nameEn = "Dr. Fin", roleDe = "Meeresbiologin", roleEn = "Marine biologist", kind = NpcKind.Story,
                color = C("#8E5CC2"), pitch = 230f, rate = 13f, vowels = 1, height = 1.76f },
            new NpcDef { id = Chad, nameDe = "Chad", nameEn = "Chad", roleDe = "Bottom Feeders", roleEn = "Bottom Feeders", kind = NpcKind.Story,
                color = C("#E8C65A"), pitch = 120f, rate = 12f, vowels = 2, height = 1.86f },
            new NpcDef { id = Brad, nameDe = "Brad", nameEn = "Brad", roleDe = "Bottom Feeders", roleEn = "Bottom Feeders", kind = NpcKind.Story,
                color = C("#E8C65A"), pitch = 165f, rate = 12f, vowels = 1, height = 1.86f },
            new NpcDef { id = Pearl, nameDe = "Großmutter Perle", nameEn = "Grandma Pearl", roleDe = "Altriff", roleEn = "Oldreef", kind = NpcKind.Retired,
                color = C("#6E9445"), pitch = 175f, rate = 7f, vowels = 3, height = 1.58f },
            new NpcDef { id = Mira, nameDe = "Mira", nameEn = "Mira", roleDe = "Bootsbauerin", roleEn = "Boat builder", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 195f, rate = 12f, vowels = 2, height = 1.80f },
            new NpcDef { id = Tomas, nameDe = "Tomas", nameEn = "Tomas", roleDe = "Fischer", roleEn = "Fisherman", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 110f, rate = 8f, vowels = 0, height = 1.82f },
            new NpcDef { id = Ada, nameDe = "Tante Ada", nameEn = "Auntie Ada", roleDe = "Kiosk", roleEn = "Kiosk", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 210f, rate = 11f, vowels = 1, height = 1.66f },
            new NpcDef { id = Juno, nameDe = "Juno", nameEn = "Juno", roleDe = "Musikerin", roleEn = "Musician", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 220f, rate = 12f, vowels = 1, height = 1.74f },
            new NpcDef { id = Lio, nameDe = "Lio", nameEn = "Lio", roleDe = "Abonnent Nr. 1", roleEn = "Subscriber #1", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 300f, rate = 14f, vowels = 1, height = 1.18f },
            new NpcDef { id = Toni, nameDe = "Toni", nameEn = "Tony", roleDe = "Tonmann", roleEn = "Sound guy", kind = NpcKind.Story,
                color = C("#4F7FAE"), pitch = 120f, rate = 9f, vowels = 3, height = 1.72f },
            new NpcDef { id = Weaver1, nameDe = "Noa", nameEn = "Noa", roleDe = "Weberin", roleEn = "Weaver", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 190f, rate = 10f, vowels = 2, height = 1.70f },
            new NpcDef { id = Weaver2, nameDe = "Ilse", nameEn = "Ilse", roleDe = "Weberin", roleEn = "Weaver", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 205f, rate = 9f, vowels = 1, height = 1.62f },
            new NpcDef { id = Fisher1, nameDe = "Kai", nameEn = "Kai", roleDe = "Fischer", roleEn = "Fisher", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 125f, rate = 10f, vowels = 0, height = 1.80f },
            new NpcDef { id = Fisher2, nameDe = "Rosa", nameEn = "Rosa", roleDe = "Fischerin", roleEn = "Fisher", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 200f, rate = 11f, vowels = 2, height = 1.68f },
            new NpcDef { id = Teen, nameDe = "Matti", nameEn = "Matti", roleDe = "Ukulele", roleEn = "Ukulele", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 160f, rate = 12f, vowels = 2, height = 1.70f },
            new NpcDef { id = Grandpa, nameDe = "Opa Jules", nameEn = "Grandpa Jules", roleDe = "Hängematte", roleEn = "Hammock", kind = NpcKind.Retired,
                color = C("#C9B48A"), pitch = 105f, rate = 6f, vowels = 3, height = 1.66f },
            new NpcDef { id = Crowd, nameDe = "Einsiedler", nameEn = "Hermits", roleDe = "Lager", roleEn = "Camp", kind = NpcKind.Radio,
                color = C("#8FB36A"), pitch = 150f, rate = 4f, vowels = 3 },
            // ---- round 3: the hermits (APPENDED – network indices 20 … 25). Low, slow hum voices (they never speak words).
            new NpcDef { id = HLeader, nameDe = "Ebbe", nameEn = "Ebb", roleDe = "Anführerin", roleEn = "Leader", kind = NpcKind.Hermit,
                color = C("#8FB36A"), pitch = 165f, rate = 3.5f, vowels = 3, height = 1.76f },
            new NpcDef { id = HSmith, nameDe = "Amboss", nameEn = "Anvil", roleDe = "Schleifer", roleEn = "Grinder", kind = NpcKind.Hermit,
                color = C("#C98A4B"), pitch = 95f, rate = 3.5f, vowels = 3, height = 1.84f },
            new NpcDef { id = HHunter, nameDe = "Harpo", nameEn = "Harpo", roleDe = "Jäger", roleEn = "Hunter", kind = NpcKind.Hermit,
                color = C("#C0674F"), pitch = 120f, rate = 3.5f, vowels = 3, height = 1.80f },
            new NpcDef { id = HCook, nameDe = "Topf", nameEn = "Pot", roleDe = "Koch", roleEn = "Cook", kind = NpcKind.Hermit,
                color = C("#E0A040"), pitch = 130f, rate = 4f, vowels = 3, height = 1.68f },
            new NpcDef { id = HLookout, nameDe = "Fernglas", nameEn = "Spyglass", roleDe = "Späherin", roleEn = "Lookout", kind = NpcKind.Hermit,
                color = C("#4F9FC4"), pitch = 200f, rate = 4f, vowels = 3, height = 1.72f },
            new NpcDef { id = HGardener, nameDe = "Wurzel", nameEn = "Root", roleDe = "Gärtner", roleEn = "Gardener", kind = NpcKind.Hermit,
                color = C("#7FA243"), pitch = 110f, rate = 3.5f, vowels = 3, height = 1.66f },
        };

        public static NpcDef Get(string id)
        {
            if (id == null) return null;
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
            return null;
        }

        public static int IndexOf(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < All.Length; i++) if (All[i].id == id) return i;
            return -1;
        }

        /// Localized display name ("Horst", "Onkel Gus" …); unknown ids are returned unchanged.
        public static string NameOf(string id)
        {
            var d = Get(id);
            return d != null ? d.Name : id;
        }

        public static Color ColorOf(string id)
        {
            var d = Get(id);
            return d != null ? d.color : LowPoly.Hex("#C9B48A");
        }

        /// True for every NPC with a body in the world (not Gus' radio voice, not the crowd, not the retired villagers).
        public static bool HasActor(NpcDef d) => d != null && d.kind != NpcKind.Radio && d.kind != NpcKind.Retired;
    }
}
