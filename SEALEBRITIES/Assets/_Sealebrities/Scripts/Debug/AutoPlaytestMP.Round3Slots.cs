using System;
using System.Collections;

namespace Sealebrities
{
    /// Round-3 step 6b co-op check (GDD §19b): only the creator of the world (the host) may invite – the guest's invite
    /// list never opens; only the host saves (the guest has no save file); the guest's photos of the host's world live
    /// in its own per-world folder coop/w{seed}; achievements are the world's (the guest sees the host's list).
    public sealed partial class AutoPlaytestMP
    {
        IEnumerator StepRound3Slots()
        {
            yield return Sync("S35a", 120f);
            if (isHost)
            {
                Check(NetLobby.InviteRoleAllowed, "host: may invite (creator of the world)");
                Check(Gallery.Folder.IndexOf("coop", StringComparison.OrdinalIgnoreCase) < 0, "host photos in its own world folder (" + Gallery.Folder + ")");
                SendVal("hostAch", Achievements.UnlockedCount.ToString(Inv));
            }
            else
            {
                Check(!NetLobby.InviteRoleAllowed && !NetLobby.CanInvite, "guest: no invites (only the host)");
                Check(!NetLobby.DebugTryOpenInvitePicker() && !NetLobby.DebugShowInvitePicker(null) && !NetLobby.InvitePickerOpen, "guest: the invite list never opens");
                Check(SaveSlots.ActiveFile == null, "guest has no save file (only the host saves)");
                string expected = SaveSlots.GuestFolder(S.saveSeed);
                Check(Gallery.Folder.StartsWith(expected, StringComparison.OrdinalIgnoreCase), "guest photos of this world in coop/w<seed> (" + Gallery.Folder + ")");
                yield return WaitVal("hostAch", 30f);
                yield return WaitUntil(() => Val("hostAch") == Achievements.UnlockedCount.ToString(Inv), 8f);
                Check(Val("hostAch") == Achievements.UnlockedCount.ToString(Inv), "guest sees the world's achievements (" + Achievements.UnlockedCount + " = host " + Val("hostAch") + ")");
            }
            yield return Sync("S35b", 60f);
        }
    }
}
