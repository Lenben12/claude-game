using System.Collections;
using UnityEngine;

namespace Sealebrities
{
    /// Round-2 co-op checks of the two-window playtest (Design_Runde2 J.6 "Co-op"): an achievement unlocked by the host
    /// reaches the client (F.6), a new look on the client rebuilds its avatar on the host (1c), the client runs out of
    /// air under water and the host tows it up (F.1 – revived with its clips, no faint), and both divers hear each
    /// other only up to 10 m under water (F.5). Both windows run the same code, synchronized through the test channel.
    public sealed partial class AutoPlaytestMP
    {
        int revivedById = -2;

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++) yield return null;
        }

        void OnR2Revived(int id, int rescuerId)
        {
            if (id == Net.LocalId) revivedById = rescuerId;
        }

        IEnumerator StepCoopRound2()
        {
            yield return Sync("S11ba", 120f);
            if (pc == null) yield break;
            pc.enabled = true;

            // ---- F.6 achievements for everyone present
            const string ach = "roof_delivery";
            if (isHost)
            {
                bool fresh = Achievements.Unlock(ach);
                Check(Achievements.IsUnlocked(ach), "host unlocked '" + ach + "'" + (fresh ? "" : " (already known)"));
                SendVal("ach", ach);
            }
            else
            {
                yield return WaitVal("ach", 20f);
                yield return WaitUntil(() => Achievements.IsUnlocked(ach), 10f);
                Check(Achievements.IsUnlocked(ach), "client received the host's achievement '" + ach + "' (F.6 – everybody present gets it)");
            }
            yield return Sync("S11bb", 40f);

            // ---- 1c look sync: the client puts on the party hat → the host sees it on the avatar
            if (!isHost)
            {
                int hat0 = Settings.Data.hat;
                Settings.Data.hat = 8;
                CharacterScreen.RaiseLookSaved();
                SendVal("hat", "8");
                yield return WaitVal("hatseen", 20f);
                Settings.Data.hat = hat0;
                CharacterScreen.RaiseLookSaved();
            }
            else
            {
                yield return WaitVal("hat", 20f);
                var info = Net.Find(peerId);
                yield return WaitUntil(() => info != null && info.look.hat == 8, 10f);
                Check(info != null && info.look.hat == 8, "the client's new look (party hat) reached the host (look sync)");
                var a = PeerAvatar();
                if (a != null)
                {
                    yield return Wait(1f);
                    yield return AimAt(a.HeadPosition);
                    yield return Shot("host_S11b_client_party_hat");
                }
                SendVal("hatseen", "1");
            }
            yield return Sync("S11bc", 40f);

            // ---- F.1 rescue tow + F.5 voice range under water
            var site = DiveSites.Get("house_reef");
            // open water with a free way up for both divers (the denser round-2 reef put the old spot under coral)
            Vector3 eye = OpenWaterEye(site.center, site.radius * 0.8f, 4.5f, new Vector3(1.3f, 0f, 0f));
            Vector3 mine = isHost ? eye + new Vector3(1.3f, 0f, 0f) : eye;
            GameEvents.PlayerRevived += OnR2Revived;
            revivedById = -2;
            pc.TeleportTo(mine - Vector3.up * PlayerController.EyeHeight, isHost ? 270f : 90f);
            pc.RefillAir();
            yield return Wait(1.8f);
            pc.RefillAir();
            Check(pc.IsUnderwater, Role + " under water at the house reef (" + F(pc.Depth) + " m)");
            yield return Sync("S11bd", 40f);
            float range = NetVoice.RangeOf(peerId);
            Check(range <= 11f || pc.Gear.hasRadio, "voice range under water: " + F(range, "0") + " m (F.5: 10 m muffled, 200 m with the radio)");
            if (!isHost)
            {
                int clips0 = Footage.DiveCount;
                bool ko = pc.DebugKnockOut();
                Check(ko && pc.IsUnconscious && NetRescue.LocalUnconscious, "client out of air → unconscious instead of a faint (F.1, " + F(pc.UnconsciousSecondsLeft, "0") + " s)");
                SendVal("ko", ko ? "1" : "0");
                yield return Wait(1f);
                yield return Shot("client_S11b_unconscious");
                var trace = new System.Text.StringBuilder();
                float tt = 0f, nextT = 0f;
                while (pc.IsUnconscious && tt < 40f)
                {
                    tt += Time.unscaledDeltaTime;
                    if (tt >= nextT)
                    {
                        nextT += 2f;
                        bool anchored = NetRescue.TryGetTowAnchor(out Vector3 ah, out _);
                        trace.Append(F(tt, "0")).Append("s ").Append(F(pc.Depth, "0.0")).Append(" m");
                        if (anchored) trace.Append(" (anchor ").Append(F(Waves.SeaLevel - ah.y, "0.0")).Append(" m, ").Append(F(Vector3.Distance(ah, cam.transform.position), "0.0")).Append(" m away)");
                        else trace.Append(" (no anchor, towed by ").Append(NetRescue.LocalTowedBy).Append(")");
                        trace.Append("; ");
                    }
                    yield return null;
                }
                Info("towed diver (depth, anchor): " + trace);
                Check(!pc.IsUnconscious && !pc.IsFainting, "client revived (" + F(pc.Depth, "0.0") + " m deep)");
                Check(revivedById == 0, "revived by the host's tow (rescuer id " + revivedById + ")");
                Check(Footage.DiveCount >= clips0, "clips kept (" + clips0 + " → " + Footage.DiveCount + ")");
                Check(pc.Air01 >= 0.25f, "air after the revive: " + F(pc.Air01 * 100f, "0") + " %");
                SendVal("revived", revivedById.ToString());
            }
            else
            {
                yield return WaitVal("ko", 20f);
                yield return WaitUntil(() => NetRescue.IsUnconscious(peerId), 10f);
                Check(NetRescue.IsUnconscious(peerId), "host sees the client unconscious");
                yield return Wait(0.6f);
                bool prompt = pc.TryGetTowPrompt(out string text);
                Info("tow prompt: '" + text + "' (" + prompt + ")");
                pc.ToggleTow();
                yield return WaitUntil(() => NetRescue.LocalTowTarget == peerId, 4f);
                if (NetRescue.LocalTowTarget != peerId) { Info("E did not grab – NetRescue.StartTow"); NetRescue.StartTow(peerId); yield return Wait(0.5f); }
                Check(NetRescue.LocalTowTarget == peerId, "host tows the client (E – Abschleppen)");
                var a = PeerAvatar();
                if (a != null) { yield return AimAt(a.HeadPosition); yield return Shot("host_S11b_towing"); }
                GameInput.SimPress(GameAction.Up);
                var trace = new System.Text.StringBuilder();
                float tt = 0f, nextT = 0f;
                while (NetRescue.IsUnconscious(peerId) && tt < 30f)
                {
                    tt += Time.unscaledDeltaTime;
                    if (tt >= nextT)
                    {
                        nextT += 2f;
                        var pa = PeerAvatar();
                        trace.Append(F(tt, "0")).Append("s host ").Append(F(pc.Depth, "0.0")).Append(" m");
                        if (pa != null) trace.Append(", guest ").Append(F(Waves.SeaLevel - pa.HeadPosition.y, "0.0")).Append(" m");
                        trace.Append(NetRescue.LocalTowTarget == peerId ? "" : " (not towing)").Append("; ");
                    }
                    yield return null;
                }
                GameInput.SimRelease(GameAction.Up);
                Info("tower (depths): " + trace);
                Check(!NetRescue.IsUnconscious(peerId), "towed to the surface → the client is awake again");
                yield return WaitVal("revived", 20f);
                Check(Val("revived") == "0", "the client reports the host as its rescuer (" + (Val("revived") ?? "-") + ")");
            }
            GameEvents.PlayerRevived -= OnR2Revived;
            yield return Sync("S11be", 60f);
            pc.enabled = true;
            yield return Stand(isHost ? HostSpot : ClientSpot, isHost ? 270f : 90f);
        }
    }
}
