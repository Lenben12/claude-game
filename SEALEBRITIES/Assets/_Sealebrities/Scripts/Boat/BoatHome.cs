using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// "Zur Hütte" (pause menu) brings the boat back to its mooring at the dock as well (feedback Testversion 3, f):
    /// the player is at the hut and the boat lies at the end of the dock instead of drifting at the last dive site.
    /// Offline and on the co-op host the boat is moved at once (BoatController.ReturnToMooring: stopped, anchored).
    /// A co-op client sends NetCommand.Custom "BoatHome" – only the host moves the shared boat (it simulates the parked
    /// boat and streams the new pose; the guests' anchor follows the stream's anchor bit silently).
    /// Never while ANOTHER player drives it, stands on its deck or still dives right next to it (they would be stranded
    /// at sea): then only the local player goes home and gets a short toast.
    public static class BoatHome
    {
        /// NetCommand.Custom key (first argument), handled by the host through CoreCommands.RegisterCustom.
        public const string CommandKey = "BoatHome";
        /// Another player in the water this close to the boat (m, horizontal) still needs it.
        const float DiverRange = 60f;
        /// Avatars without a state for this long are leftovers of a lost connection.
        const float AvatarStaleSeconds = 6f;

        /// Called once by BoatModule.Install.
        public static void Install()
        {
            CoreCommands.RegisterCustom(CommandKey, OnCommand);
        }

        static BoatController Boat
        {
            get
            {
                var b = Refs.Boat as BoatController;
                return b != null ? b : null;   // Unity null check (destroyed boat)
            }
        }

        /// The local player was just teleported to the hut ("Zur Hütte"): bring the boat home too, if nobody needs it.
        public static void AfterLocalPlayerWentHome()
        {
            var gm = GameManager.I;
            var bc = Boat;
            if (gm == null || !gm.InGame || bc == null || bc.AtMooring) return;

            string blocker = Blocker(bc, Net.LocalId);
            if (blocker != null)
            {
                // L.10: one short line + the reason as the chip
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Das Boot bleibt draußen", "The boat stays out"), blocker);
                return;
            }
            if (Net.HasAuthority)
            {
                if (!bc.ReturnToMooring()) return;
                GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Boot liegt wieder am Steg", "Boat is back at the dock"));
                return;
            }
            // guest: the host checks again and may refuse (race / handover) – confirm only what really happened
            Net.Command(NetCommand.Custom, CommandKey);
            GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Boot wird gerufen …", "Calling the boat …"));
            bc.StartCoroutine(ConfirmHome(bc));
        }

        /// Guest: "back at the dock" once the streamed boat really lies at its mooring (within 3 s), else "stays out".
        static IEnumerator ConfirmHome(BoatController bc)
        {
            float t = 0f;
            while (t < 3f)
            {
                if (bc == null) yield break;
                if (bc.AtMooring)
                {
                    GameEvents.RaiseNotify(NotifyKind.Info, Loc.Tr("Boot liegt wieder am Steg", "Boat is back at the dock"));
                    yield break;
                }
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            if (bc != null && !bc.AtMooring)
                GameEvents.RaiseNotify(NotifyKind.Warning, Loc.Tr("Das Boot bleibt draußen", "The boat stays out"));
        }

        /// Host: a guest went home with "Zur Hütte" and asks for the boat (fromId = that guest).
        static bool OnCommand(int fromId, string[] args)
        {
            if (!Net.HasAuthority) return false;
            var bc = Boat;
            if (bc == null) return false;
            if (bc.AtMooring) return true;
            // conditions changed since the guest's own check (someone took the wheel / stepped aboard): it stays
            if (Blocker(bc, fromId) != null) return false;
            return bc.ReturnToMooring();
        }

        /// Localized reason why the boat must stay where it is, or null when it may go home.
        /// requesterId = the player who went home (already at the hut, never counted).
        static string Blocker(BoatController bc, int requesterId)
        {
            // someone at the wheel (the requester cannot be: "Zur Hütte" is not offered while seated)
            int driver = NetFlags.BoatDriverId;
            if (driver >= 0 && driver != requesterId) return Driving(NameOf(driver));
            if (Net.IsHost && bc.IsRemote) return Driving(Loc.Tr("Jemand", "Someone"));   // a guest still simulates it

            // this machine's own player, when somebody else asked (host deciding a guest's request)
            if (Net.LocalId != requesterId)
            {
                if (bc.PlayerDriving) return Driving(Net.LocalName);
                var p = Refs.Player;
                if (p != null && !(p is Object o && o == null) && p.Root != null)
                {
                    if (bc.PlayerAboard) return Aboard(Net.LocalName);
                    if ((p.IsInWater || p.IsUnderwater) && Near(bc, p.Root.position)) return Diving(Net.LocalName);
                }
            }

            // everybody else (co-op avatars)
            if (Net.IsOnline)
            {
                NetAvatarSync sync = NetAvatarSync.Instance;
                if (sync != null)
                {
                    var all = sync.All;
                    for (int i = 0; i < all.Count; i++)
                    {
                        var a = all[i];
                        if (a == null || a.PlayerId == requesterId || a.PlayerId == Net.LocalId) continue;
                        if (Time.unscaledTime - a.LastStateTime > AvatarStaleSeconds) continue;
                        Vector3 feet = a.transform.position;
                        if (a.OnBoat || bc.IsOnDeck(feet)) return Aboard(NameOf(a.PlayerId, a.PlayerName));
                        if ((a.InWater || a.Underwater) && Near(bc, feet)) return Diving(NameOf(a.PlayerId, a.PlayerName));
                    }
                }
            }
            return null;
        }

        static bool Near(BoatController bc, Vector3 p)
        {
            Vector3 d = p - bc.Root.position;
            d.y = 0f;
            return d.sqrMagnitude < DiverRange * DiverRange;
        }

        static string NameOf(int id, string fallback = null)
        {
            var info = Net.Find(id);
            if (info != null && !string.IsNullOrWhiteSpace(info.name)) return info.name;
            if (!string.IsNullOrWhiteSpace(fallback)) return fallback;
            return Loc.Tr("Jemand", "Someone");
        }

        static string Driving(string who) => Loc.Tr(who + " steuert", who + " is steering");

        static string Aboard(string who) => Loc.Tr(who + " ist an Bord", who + " is aboard");

        static string Diving(string who) => Loc.Tr(who + " taucht am Boot", who + " dives at the boat");
    }
}
