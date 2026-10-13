using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 6b screenshots (save slots + invites, GDD §19b), taken in the main menu before the tour's game
    /// starts. Three example worlds live in a SANDBOX (SaveSlots.RootOverride = persistentDataPath/autoshot_slots,
    /// deleted afterwards) – the player's own slots are never touched. Files: r3slot_*.png
    ///   r3slot_menu (main menu: Fortsetzen #2 · Tag 12, Spielstände 3/4), r3slot_cards (the 4 slot cards),
    ///   r3slot_delete (delete confirmation), r3slot_new_world (difficulty cards for empty slot 3),
    ///   r3slot_host (IP host page: slot tiles, slot 2 continues), r3slot_host_new (empty slot 3 → difficulty cards),
    ///   r3slot_steamhost (Steam host page), r3slot_invite (the invite list – every button „Einladen“),
    ///   r3slot_invite_clicked (the same list after 3 clicks on one friend: unchanged).
    public sealed partial class AutoShot
    {
        string slotShotSandbox;

        IEnumerator ShotsRound3Slots()
        {
            var ctl = UIModule.Controller;
            var gm = GameManager.I;
            if (ctl == null || gm == null || gm.InGame) yield break;
            slotShotSandbox = Path.Combine(Application.persistentDataPath, "autoshot_slots");
            try { if (Directory.Exists(slotShotSandbox)) Directory.Delete(slotShotSandbox, true); } catch (Exception) { }
            Directory.CreateDirectory(slotShotSandbox);
            SaveSlots.SetActive(0);
            SaveSlots.RootOverride = slotShotSandbox;
            SaveSlots.InvalidateAll();

            // three example worlds (slot 3 empty)
            WriteExampleSlot(1, 4, 380, 1250, Difficulty.Snorkeler, 52 * 60, 3, -2, new Color(0.95f, 0.75f, 0.45f), new Color(0.35f, 0.75f, 0.85f));
            WriteExampleSlot(2, 12, 18400, 23800, Difficulty.OpenWater, 7 * 3600 + 25 * 60, 11, 0, new Color(0.1f, 0.35f, 0.55f), new Color(0.45f, 0.9f, 0.85f));
            WriteExampleSlot(4, 31, 254000, 96500, Difficulty.Advanced, 21 * 3600 + 5 * 60, 24, -9, Color.clear, Color.clear);
            SaveSlots.SetLastUsed(2);
            SaveSlots.NotifyChanged();

            ctl.Menu.Show();
            yield return Wait(1.2f);
            yield return Capture("r3slot_menu");
            ctl.Menu.DebugOpenSlots();
            yield return Wait(1f);
            Note((ctl.Menu.DebugSlotCardCount == 4 ? "OK" : "FAIL") + ": slot cards " + ctl.Menu.DebugSlotCardCount);
            yield return Capture("r3slot_cards");
            ctl.Menu.DebugAskDelete(2);
            yield return Wait(0.6f);
            yield return Capture("r3slot_delete");
            UIDialog.Cancel();
            ctl.Menu.DebugOpenNewGame(3);
            yield return Wait(0.8f);
            yield return Capture("r3slot_new_world");
            ctl.Menu.Show();
            yield return Wait(0.4f);

            if (NetLobby.DebugOpenHostPage(false))
            {
                NetLobby.DebugSelectHostSlot(2);
                yield return Wait(1f);
                yield return Capture("r3slot_host");
                NetLobby.DebugSelectHostSlot(3);
                yield return Wait(0.6f);
                yield return Capture("r3slot_host_new");
                NetLobby.DebugSelectHostSlot(2);
                NetLobby.DebugOpenHostPage(true);
                yield return Wait(0.8f);
                yield return Capture("r3slot_steamhost");
                NetLobby.Close();
                yield return Wait(0.4f);
            }
            else Note("FAIL: lobby host page did not open");

            // the invite list (fake friends, a counting test sender – never a real Steam invite)
            int sent = 0;
            NetLobby.InviteSenderOverride = id => { sent++; return true; };
            var friends = new List<SteamLobby.FriendEntry>
            {
                new SteamLobby.FriendEntry { id = 9101, name = "Kumpel Kai", sameGame = true },
                new SteamLobby.FriendEntry { id = 9102, name = "Mia_Taucht", sameGame = true },
                new SteamLobby.FriendEntry { id = 9103, name = "Lotte", sameGame = false },
            };
            if (NetLobby.DebugShowInvitePicker(friends))
            {
                yield return Wait(0.8f);
                yield return Capture("r3slot_invite");
                var btns = NetLobby.DebugInviteButtons;
                if (btns.Count > 0)
                    for (int k = 0; k < 3; k++) (btns[0].userData as Action)?.Invoke();
                yield return Wait(0.8f);
                bool same = btns.Count > 0 && btns[0].text == NetLobby.InviteLabel && btns[0].enabledSelf;
                Note((same && sent == 3 ? "OK" : "FAIL") + ": invite button after 3 clicks '" + (btns.Count > 0 ? btns[0].text : "-") + "', invites counted " + sent);
                yield return Capture("r3slot_invite_clicked");
            }
            else Note("FAIL: invite list did not open");
        }

        /// Ends the slot shots (also after an exception inside them): sandbox off + deleted, the normal menu back.
        void EndSlotShots()
        {
            NetLobby.DebugCloseInvitePicker();
            NetLobby.InviteSenderOverride = null;
            if (NetLobby.IsOpen) NetLobby.Close();
            UIDialog.Close();
            SaveSlots.SetActive(0);
            SaveSlots.RootOverride = null;
            SaveSlots.InvalidateAll();
            try { if (slotShotSandbox != null && Directory.Exists(slotShotSandbox)) Directory.Delete(slotShotSandbox, true); } catch (Exception) { }
            var ctl = UIModule.Controller;
            if (ctl != null && GameManager.I != null && !GameManager.I.InGame) ctl.Menu.Show();
        }

        static void WriteExampleSlot(int slot, int day, long followers, long money, Difficulty d, double playSeconds, int achievements,
                                     int daysAgo, Color photoTop, Color photoBottom)
        {
            var s = new GameState { day = day, followers = followers, money = money, difficulty = d, saveSeed = 4242 + slot, playSeconds = playSeconds };
            s.lastPlayedUtc = DateTime.UtcNow.AddDays(daysAgo).AddHours(-slot).Ticks;
            for (int i = 0; i < achievements && i < AchievementDatabase.All.Count; i++) s.achievements.Add(AchievementDatabase.All[i].id);
            Directory.CreateDirectory(SaveSlots.Root);
            File.WriteAllText(SaveSlots.FileOf(slot), JsonUtility.ToJson(s));
            if (photoTop.a <= 0f) return;
            // one "photo" of that world (a sea gradient with a sun) as the card picture
            string gal = SaveSlots.GalleryOf(slot);
            Directory.CreateDirectory(gal);
            var t = new Texture2D(256, 144, TextureFormat.RGBA32, false);
            var px = new Color32[256 * 144];
            for (int y = 0; y < 144; y++)
                for (int x = 0; x < 256; x++)
                {
                    Color c = Color.Lerp(photoBottom, photoTop, y / 143f);
                    float dx = x - 190f, dy = y - 105f;
                    if (dx * dx + dy * dy < 18f * 18f) c = Color.Lerp(c, new Color(1f, 0.95f, 0.7f), 0.85f);
                    px[y * 256 + x] = c;
                }
            t.SetPixels32(px);
            t.Apply(false);
            string file = "sealog_2026101" + slot + "_120000_000_scene.png";
            File.WriteAllBytes(Path.Combine(gal, file), t.EncodeToPNG());
            Object.Destroy(t);
            var idx = new ShotGalleryIndex();
            idx.photos.Add(new PhotoEntry { file = file, speciesId = "", siteId = "", day = day, time = 0.5f });
            File.WriteAllText(Path.Combine(gal, "index.json"), JsonUtility.ToJson(idx));
        }

        [Serializable]
        class ShotGalleryIndex
        {
            public int version = 1;
            public List<PhotoEntry> photos = new List<PhotoEntry>();
        }
    }
}
