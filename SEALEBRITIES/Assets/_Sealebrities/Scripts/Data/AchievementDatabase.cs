using System.Collections.Generic;

namespace Sealebrities
{
    /// One achievement (F.6). Data only this round (Steam later). Unlocked for everyone present (Achievements.Unlock).
    public class AchievementDef
    {
        public string id, nameDe, nameEn, descDe, descEn;
        public string Name => Loc.Tr(nameDe, nameEn);
        public string Description => Loc.Tr(descDe, descEn);
    }

    public static class AchievementDatabase
    {
        public static readonly List<AchievementDef> All = new List<AchievementDef>
        {
            A("wet_feet", "Nasse Füße", "Wet Feet", "Erster Tauchgang unter 2 m", "First dive below 2 m"),
            A("first_fish", "Petri Heil", "Gone Fishin'", "Der erste Fisch", "Your first fish"),
            A("boot", "Stiefelkönig", "Boot Scootin'", "Einen alten Stiefel geangelt", "Caught an old boot"),
            A("first_upload", "Content Creator", "Content Creator", "Das erste Video hochgeladen", "Uploaded your first video"),
            A("f100", "Kleiner Fisch", "Small Fry", "100 Follower", "100 followers"),
            A("f1k", "Krill-Star", "Krill Star", "1.000 Follower", "1,000 followers"),
            A("f10k", "Riffhai", "Reef Shark", "10.000 Follower", "10,000 followers"),
            A("f100k", "Sealebrity", "Sealebrity", "100.000 Follower", "100,000 followers"),
            A("f1m", "Wal-Ikone", "Whale of a Time", "1.000.000 Follower", "1,000,000 followers"),
            A("gerald_selfie", "Gerald & ich", "Gerald & Me", "Ein Selfie mit Gerald", "A selfie with Gerald"),
            A("lifeguard", "Rettungsschwimmer", "Lifeguard", "Einen Freund an die Oberfläche geschleppt", "Towed a friend to the surface"),
            A("live_fail", "Live-Panne", "Live Fail", "Im Livestream ohnmächtig geworden", "Fainted during a stream"),
            A("hai_five", "Hai-Five", "Hai-Five", "„Stups den Hai an!“ geschafft", "Completed “Poke the shark!”"),
            A("legendary", "Legendär!", "Legendary!", "Ein Tier der Stufe Legendär gefilmt", "Filmed a Legendary-tier animal"),
            A("golden_camera", "Goldene Kamera", "Golden Camera", "Sealog zu 100 % gefüllt", "Filled the Sealog to 100 %"),
            A("shells_all", "Muschelmillionär", "Shell Shocked", "Alle 40 goldenen Muscheln", "All 40 golden shells"),
            A("pages_all", "Gus' Biograf", "Gus's Biographer", "Alle 24 Logbuch-Seiten", "All 24 logbook pages"),
            A("roof_delivery", "Lieferung aufs Dach", "Roof Delivery", "Ein Paket landete auf dem Hüttendach", "A crate landed on the hut roof"),
            A("light_fingers", "Lampenfieber", "Light Fingers", "Eine Krake hat deine Lampe geklaut", "An octopus stole your lamp"),
            A("coconut10", "Kokosnuss-Junkie", "Coconut Crazy", "10 Kokosnüsse getrunken", "Drank 10 coconuts"),
            A("grill_master", "Grillmeister", "Grill Master", "20 Fische gegrillt", "Grilled 20 fish"),
            A("tuna", "Thunfisch-Tycoon", "Tuna Tycoon", "Einen Thunfisch geangelt", "Caught a tuna"),
            A("captain_poke", "Kapitän Pieks", "Captain Poke", "Den Marlin geangelt", "Caught the marlin"),
            A("snap", "Schnur gerissen", "Snap!", "5 Fische durch gerissene Schnur verloren", "Lost 5 fish to snapped lines"),
            A("clickbait10", "Clickbait-Baron", "Clickbait Baron", "10 Clickbait-Titel", "10 clickbait titles"),
            A("honest10", "Ehrliche Haut", "Honest Fin", "10 ehrliche Titel", "10 honest titles"),
            A("night_owl", "Nachtschwärmer", "Night Owl", "Zwischen 00:00 und 04:00 getaucht", "Dived between 00:00 and 04:00"),
            A("rock_bottom", "Tiefer geht's nicht", "Rock Bottom", "95 m erreicht", "Reached 95 m"),
            A("big_bertha", "Big Bertha!", "Big Bertha!", "Den Buckelwal gefilmt", "Filmed the humpback whale"),
            A("bow_riders", "Bugwelle", "Bow Riders", "Delfine an deiner Bugwelle", "Dolphins rode your bow wave"),
            A("bottomed_out", "Bottom erreicht", "Bottomed Out", "Die Bottom-Brüder entlarvt", "Exposed the Bottom brothers"),
            A("horst_overboard", "Horst fällt raus", "Horst Overboard", "Horst aus der Hängematte fallen sehen", "Saw Horst fall out of his hammock"),
            A("full_crew", "Volle Crew", "Full Crew", "Mit 4 Tauchern gespielt", "Played with 4 divers"),
            A("live_from_the_deep", "Live aus der Tiefe", "Live from the Deep", "Das Finale geschafft", "Finished the finale"),
        };

        static AchievementDef A(string id, string de, string en, string dde, string den) =>
            new AchievementDef { id = id, nameDe = de, nameEn = en, descDe = dde, descEn = den };

        static Dictionary<string, AchievementDef> byId;

        public static AchievementDef Get(string id)
        {
            if (id == null) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, AchievementDef>();
                foreach (var a in All) byId[a.id] = a;
            }
            return byId.TryGetValue(id, out var d) ? d : null;
        }
    }
}
