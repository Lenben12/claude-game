using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 6b (GDD §19b) – save slots + invites. Runs at the end of RunRound3Steps (-r3only) or alone with
    ///     SEALEBRITIES.exe -autoplaytest "C:/out" -r3slots
    /// 88: four slots in a SANDBOX folder (SaveSlots.RootOverride = persistentDataPath/autotest_slots – the player's own
    ///     slots are never read for writing): migration of an old single save (+ its gallery) into slot 1, load it, a new
    ///     world in slot 2, achievements / photos / play time per world, Fortsetzen = last used, delete (also through the
    ///     menu's confirmation), the 4 slot cards in the menu, the slot tiles on the host pages. Screenshots 88_*.
    /// 89: the invite list – the button always says „Einladen“, never changes or disables, can be clicked again and
    ///     again (a test sender counts the invites – no real Steam invite is ever sent); the real list stays closed
    ///     without a Steam lobby. Screenshot 89_invite_list.
    public sealed partial class AutoPlaytest
    {
        static bool Round3SlotsOnly => Array.IndexOf(Environment.GetCommandLineArgs(), "-r3slots") >= 0;

        IEnumerator RunRound3SlotSteps()
        {
            yield return RunStep("88 Save slots (GDD §19b): 4 worlds, old single save → slot 1, new world in an empty slot, achievements / photos / play time per world, Fortsetzen = last used, delete with confirmation, slot cards + host slot picker", StepR3Slots, 180f);
            yield return RunStep("89 Invites (GDD §19b): the friend list's button always says 'Einladen', stays clickable and looks the same after any number of clicks; only the host can invite", StepR3Invites, 60f);
        }

        // ------------------------------------------------------------------ sandbox
        string slotSandbox;

        /// All slot files / slot folders / slots.json of the test live in persistentDataPath/autotest_slots.
        void EnterSlotSandbox()
        {
            slotSandbox = Path.Combine(Application.persistentDataPath, "autotest_slots");
            try { if (Directory.Exists(slotSandbox)) Directory.Delete(slotSandbox, true); } catch (Exception) { }
            try { Directory.CreateDirectory(slotSandbox); } catch (Exception) { }
            SaveSlots.SetActive(0);
            SaveSlots.RootOverride = slotSandbox;
            SaveSlots.InvalidateAll();
        }

        /// Back to the normal (unsaved) test world; deletes the sandbox. Safe to call any time (Finish calls it).
        void LeaveSlotSandbox()
        {
            if (string.IsNullOrEmpty(SaveSlots.RootOverride)) return;
            SaveSlots.SetActive(0);
            SaveSlots.RootOverride = null;
            SaveSlots.InvalidateAll();
            GameManager.SavingDisabled = true;
            try { if (slotSandbox != null && Directory.Exists(slotSandbox)) Directory.Delete(slotSandbox, true); } catch (Exception) { }
        }

        /// Fingerprint of the player's REAL slot files (write time + size) – must not change during a test.
        static string RealSlotsStamp()
        {
            var sb = new StringBuilder();
            string root = Application.persistentDataPath;
            for (int i = 1; i <= SaveSlots.Count; i++)
            {
                string f = Path.Combine(root, "save_slot" + i + ".json");
                try
                {
                    var fi = new FileInfo(f);
                    sb.Append(fi.Exists ? fi.LastWriteTimeUtc.Ticks + ":" + fi.Length : "-").Append('|');
                }
                catch (Exception) { sb.Append("?|"); }
            }
            string meta = Path.Combine(root, "slots.json");
            try { sb.Append(File.Exists(meta) ? File.ReadAllText(meta) : "-"); } catch (Exception) { sb.Append('?'); }
            return sb.ToString();
        }

        [Serializable]
        class TestGalleryIndex
        {
            public int version = 1;
            public List<PhotoEntry> photos = new List<PhotoEntry>();
        }

        static byte[] TestPng(Color a, Color b)
        {
            var t = new Texture2D(64, 36, TextureFormat.RGBA32, false);
            for (int y = 0; y < 36; y++)
                for (int x = 0; x < 64; x++)
                    t.SetPixel(x, y, Color.Lerp(a, b, y / 35f));
            t.Apply(false);
            byte[] png = t.EncodeToPNG();
            Object.Destroy(t);
            return png;
        }

        int r3AchPops;
        void OnR3AchPop(AchievementDef a) => r3AchPops++;

        static int CountNamed(VisualElement root, string name) => root != null ? root.Query<VisualElement>(name).ToList().Count : 0;

        // ================================================================== 88 save slots
        IEnumerator StepR3Slots()
        {
            var gm = GameManager.I;
            var ui = UIModule.Controller;
            Check(gm != null && ui != null, "game + UI");
            if (gm == null || ui == null) yield break;
            string worldJson = JsonUtility.ToJson(S);   // the running test world – restored at the end
            int seed0 = S.saveSeed;
            string realBefore = RealSlotsStamp();
            GameEvents.AchievementUnlocked += OnR3AchPop;
            EnterSlotSandbox();
            try
            {
                // ---- a: four empty slots
                Check(!SaveSlots.AnyUsed && SaveSlots.FirstFree() == 1 && SaveSlots.ContinueSlot == 0 && SaveSlots.Count == 4,
                      "sandbox: four empty slots (first free " + SaveSlots.FirstFree() + ")");

                // ---- b: an old single save (v8 + the old per-PC gallery) becomes slot 1
                var legacy = new GameState { day = 7, money = 4321, followers = 1234, saveSeed = 777001, difficulty = Difficulty.Snorkeler };
                legacy.version = 8;
                legacy.prologueDone = true;                         // a running old game: no prologue / tutorial
                legacy.questIndex = TutorialQuests.Finished;
                legacy.unlockedSites.Add("house_reef");
                legacy.achievements.Add("first_fish");
                legacy.achievements.Add("f100");
                legacy.localRecord = new PlayerRecord { key = "autotest", name = "Test", playMinutes = 90f };
                legacy.localRecord.achievements.Add("first_fish");
                File.WriteAllText(SaveSlots.LegacyPath, JsonUtility.ToJson(legacy));
                string oldGallery = Path.Combine(SaveSlots.Root, "gallery");
                Directory.CreateDirectory(oldGallery);
                const string oldPhoto = "sealog_20260101_120000_000_scene.png";
                File.WriteAllBytes(Path.Combine(oldGallery, oldPhoto), TestPng(new Color(0.1f, 0.4f, 0.6f), new Color(0.5f, 0.9f, 0.9f)));
                var idx = new TestGalleryIndex();
                idx.photos.Add(new PhotoEntry { file = oldPhoto, speciesId = "", siteId = "", day = 6, time = 0.5f });
                File.WriteAllText(Path.Combine(oldGallery, "index.json"), JsonUtility.ToJson(idx));

                int moved = SaveSlots.MigrateLegacy();
                Check(moved == 1 && SaveSlots.Exists(1) && !File.Exists(SaveSlots.LegacyPath) && File.Exists(SaveSlots.LegacyPath + ".migrated"),
                      "the old single save is now slot 1 (backup sealebrities_save.json.migrated kept)");
                Check(File.Exists(Path.Combine(SaveSlots.GalleryOf(1), oldPhoto)) && !Directory.Exists(oldGallery), "the old gallery moved into slot1/gallery");
                var i1 = SaveSlots.Info(1);
                Check(i1.used && !i1.broken && i1.day == 7 && i1.followers == 1234 && i1.money == 4321 && i1.difficulty == Difficulty.Snorkeler,
                      "slot 1 card: day " + i1.day + ", " + i1.followers + " followers, " + i1.money + " $, " + i1.difficulty);
                Check(i1.achievements == 2 && Math.Abs(i1.playSeconds - 5400.0) < 1.0 && i1.photos == 1 && i1.latestPhoto != null,
                      "slot 1 card: achievements " + i1.achievements + "/" + Achievements.Total + ", play time " + SaveSlots.FormatPlayTime(i1.playSeconds) + ", photos " + i1.photos);
                Check(SaveSlots.LastUsed == 1 && SaveSlots.ContinueSlot == 1 && SaveSlots.FirstFree() == 2, "Fortsetzen = slot 1, first free = 2");
                Check(SaveSlots.MigrateLegacy() == 0, "the migration runs only once");

                // ---- c: load slot 1
                int pops0 = r3AchPops;
                bool ok = gm.LoadSlot(1);
                yield return Frames(3);
                yield return Wait(1.2f);
                Check(ok && SaveSlots.Active == 1 && S.day == 7 && S.followers == 1234 && S.version == GameState.CurrentVersion,
                      "slot 1 loaded (day " + S.day + ", save version " + S.version + ")");
                Check(Math.Abs(S.playSeconds - 5400.0) < 60.0, "play time migrated from the host's record (" + SaveSlots.FormatPlayTime(S.playSeconds) + ")");
                Check(Gallery.Folder.StartsWith(SaveSlots.FolderOf(1), StringComparison.OrdinalIgnoreCase) && Gallery.Count == 1,
                      "slot 1 shows its own photo (" + Gallery.Count + ") from " + Gallery.Folder);
                Check(Achievements.IsUnlocked("first_fish") && Achievements.IsUnlocked("f100") && !Achievements.IsUnlocked("first_upload") && Achievements.UnlockedCount == 2,
                      "achievements of world 1: " + Achievements.UnlockedCount + "/" + Achievements.Total);
                Check(r3AchPops == pops0, "loading a world pops no achievement again");

                // ---- d: save writes v9 into slot 1
                GameManager.SavingDisabled = false;
                gm.Save();
                var j1 = JsonUtility.FromJson<GameState>(File.ReadAllText(SaveSlots.FileOf(1)));
                Check(j1 != null && j1.version == GameState.CurrentVersion && j1.lastPlayedUtc > 0 && j1.playSeconds >= 5400.0,
                      "slot 1 saved as v" + (j1 != null ? j1.version : 0) + " with play time + last played");

                // ---- e: the play time counts
                double p0 = S.playSeconds;
                yield return Wait(1.5f);
                Check(S.playSeconds > p0 + 0.8, "the world's play time runs (+" + F((float)(S.playSeconds - p0), "0.0") + " s)");

                // ---- f: a new world in empty slot 2
                gm.NewGame(Difficulty.OpenWater, 2);
                yield return Frames(3);
                yield return Wait(1.2f);
                gm.SetQuestIndex(TutorialQuests.Finished);
                pc = Refs.Player as PlayerController;
                cam = pc != null ? pc.View : null;
                Check(SaveSlots.Active == 2 && SaveSlots.Exists(2) && SaveSlots.LastUsed == 2 && S.day == 1,
                      "new world in empty slot 2 (saved at once, Fortsetzen = 2)");
                Check(Gallery.Count == 0 && Gallery.Folder.StartsWith(SaveSlots.FolderOf(2), StringComparison.OrdinalIgnoreCase), "world 2 has no photos of world 1");
                Check(!Achievements.IsUnlocked("first_fish") && Achievements.UnlockedCount == 0, "achievements are per world – none in the new world");
                Check(S.playSeconds < 30.0, "world 2's play time starts at 0 (" + F((float)S.playSeconds, "0.0") + " s)");
                int pops1 = r3AchPops;
                Achievements.Unlock("first_upload");
                Check(Achievements.IsUnlocked("first_upload") && r3AchPops == pops1 + 1, "'first_upload' unlocked in world 2 (one notification)");
                var tex = new Texture2D(48, 27, TextureFormat.RGBA32, false);
                var px = new Color32[48 * 27];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(240, 140, 90, 255);
                tex.SetPixels32(px);
                tex.Apply(false);
                var e2 = Gallery.Add(tex, null, null);
                Object.Destroy(tex);
                Check(e2 != null && File.Exists(Path.Combine(SaveSlots.GalleryOf(2), e2.file)), "the photo is stored in slot 2's own folder");
                bool inPictures = false;
                if (e2 != null)
                {
                    try { inPictures = File.Exists(Path.Combine(Gallery.ExportFolder, e2.file)); } catch (Exception) { }
                    Check(!File.Exists(Path.Combine(SaveSlots.GalleryOf(1), e2.file)), "…not in world 1");
                }
                Check(!inPictures, "…and never by itself in the PC's Pictures folder (only 'Exportieren' copies it out)");
                gm.Save();
                var i2 = SaveSlots.Info(2);
                var i1b = SaveSlots.Info(1);
                Check(i2.achievements == 1 && i2.photos == 1, "slot 2 card: 1 achievement, 1 photo");
                Check(i1b.achievements == 2 && i1b.photos == 1, "slot 1 card unchanged: 2 achievements, 1 photo");

                // ---- g: back to world 1: its own achievements + photos only
                ok = gm.LoadSlot(1);
                yield return Frames(3);
                yield return Wait(1f);
                Check(ok && SaveSlots.Active == 1 && !Achievements.IsUnlocked("first_upload") && Gallery.Count == 1 && SaveSlots.LastUsed == 1,
                      "world 1 again: no 'first_upload', its 1 photo, Fortsetzen = 1");

                // ---- h: delete slot 2 (file + photos)
                bool del = SaveSlots.Delete(2);
                Check(del && !SaveSlots.Exists(2) && !Directory.Exists(SaveSlots.FolderOf(2)) && !SaveSlots.Info(2).used, "slot 2 deleted together with its photos");
                Check(SaveSlots.Exists(1) && SaveSlots.Info(1).photos == 1, "slot 1 untouched by the delete");

                // ---- i: the menu: 4 cards, delete asks first, new world in an empty slot
                gm.ReturnToMenu();   // saves world 1 into sandbox slot 1
                yield return Wait(2.5f);
                Check(UIState.MainMenuOpen && !gm.InGame, "main menu");
                ui.Menu.DebugOpenSlots();
                yield return Wait(0.8f);
                var mroot = ui.Menu.DebugRoot;
                Check(ui.Menu.DebugOnSlotsPage && ui.Menu.DebugSlotCardCount == 4 && CountNamed(mroot, "sb-slot-card") == 4,
                      "4 slot cards (" + CountNamed(mroot, "sb-slot-card") + ")");
                Check(CountNamed(mroot, "sb-slot-play") == 1 && CountNamed(mroot, "sb-slot-new") == 3 && CountNamed(mroot, "sb-slot-delete") == 1,
                      "1 used (Spielen + Löschen), 3 empty (Neues Spiel)");
                yield return Shot("88_slots_menu");
                ui.Menu.DebugAskDelete(1);
                yield return Wait(0.5f);
                Check(UIDialog.IsOpen, "Löschen asks for a confirmation first");
                yield return Shot("88_slots_delete_confirm");
                UIDialog.Cancel();
                yield return Wait(0.3f);
                Check(SaveSlots.Exists(1), "Abbrechen keeps the world");
                ui.Menu.DebugOpenNewGame(3);
                yield return Wait(0.6f);
                Check(ui.Menu.DebugOnNewGamePage && ui.Menu.DebugNewGameSlot == 3, "an empty card opens the difficulty cards for slot 3");
                yield return Shot("88_slots_new_world");
                ui.Menu.Back();
                yield return Wait(0.4f);
                Check(ui.Menu.DebugOnSlotsPage, "Esc/Back returns to the slot cards");

                // ---- j: the host pages pick the slot to host
                if (NetLobby.DebugOpenHostPage(false))
                {
                    yield return Wait(0.6f);
                    Check(NetLobby.DebugHostSlotTiles == 4 && NetLobby.DebugHostSlot == 1 && NetLobby.DebugHostContinues,
                          "IP host page: 4 slot tiles, slot 1 (used) preselected = continue");
                    yield return Shot("88_host_slots");
                    NetLobby.DebugSelectHostSlot(2);
                    yield return Wait(0.3f);
                    Check(NetLobby.DebugHostSlot == 2 && !NetLobby.DebugHostContinues, "an empty slot = a new world there");
                    NetLobby.DebugOpenHostPage(true);
                    yield return Wait(0.5f);
                    Check(NetLobby.DebugHostSlotTiles == 4, "Steam host page: 4 slot tiles");
                    NetLobby.Close();
                    yield return Wait(0.3f);
                }
                else Check(false, "lobby host page could not open");

                // ---- k: delete through the confirmation
                ui.Menu.DebugOpenSlots();
                ui.Menu.DebugAskDelete(1);
                yield return Wait(0.3f);
                UIDialog.Confirm();
                yield return Wait(0.6f);
                Check(!SaveSlots.Exists(1) && !Directory.Exists(SaveSlots.FolderOf(1)) && !SaveSlots.AnyUsed, "Löschen confirmed: slot 1 and its photos are gone");
                Check(CountNamed(ui.Menu.DebugRoot, "sb-slot-new") == 4, "the cards show four empty slots");
            }
            finally
            {
                GameEvents.AchievementUnlocked -= OnR3AchPop;
                LeaveSlotSandbox();
            }
            Check(RealSlotsStamp() == realBefore, "the player's own save slots were never written");

            // back into the test world
            if (UIState.MainMenuOpen) ui.Menu.Show();
            bool back = gm.LoadFromJson(worldJson);
            yield return Frames(3);
            yield return Wait(1.2f);
            pc = Refs.Player as PlayerController;
            cam = pc != null ? pc.View : null;
            Check(back && gm.InGame && S.saveSeed == seed0 && SaveSlots.Active == 0, "back in the (unsaved) test world");
        }

        // ================================================================== 89 invites
        IEnumerator StepR3Invites()
        {
            int sent = 0, toasts = 0;
            ulong lastId = 0;
            string sentText = Loc.Tr("Einladung gesendet", "Invite sent");
            Action<NotifyKind, string, string> onNote = (k, line, chip) => { if (line == sentText) toasts++; };
            NetLobby.InviteSenderOverride = id => { sent++; lastId = id; return true; };   // never a real Steam invite
            GameEvents.Notify += onNote;
            try
            {
                Check(NetLobby.InviteRoleAllowed, "offline / host: the world's creator may invite");
                Check(!NetLobby.CanInvite && !NetLobby.DebugTryOpenInvitePicker(), "no Steam lobby: the real invite list stays closed (nothing can be sent)");
                var friends = new List<SteamLobby.FriendEntry>
                {
                    new SteamLobby.FriendEntry { id = 9001, name = "Testfreund A", sameGame = true },
                    new SteamLobby.FriendEntry { id = 9002, name = "Testfreund B", sameGame = false },
                    new SteamLobby.FriendEntry { id = 9003, name = "Testfreund C", sameGame = true },
                };
                Check(NetLobby.DebugShowInvitePicker(friends), "invite list open (3 test friends)");
                yield return Wait(0.6f);
                var btns = NetLobby.DebugInviteButtons;
                bool allInvite = btns.Count == 3;
                for (int i = 0; i < btns.Count; i++) allInvite &= btns[i] != null && btns[i].text == NetLobby.InviteLabel && btns[i].enabledSelf;
                Check(allInvite, "every row: '" + NetLobby.InviteLabel + "', clickable (" + btns.Count + " buttons)");
                yield return Shot("89_invite_list");

                if (btns.Count > 0)
                {
                    var b = btns[0];
                    string text0 = b.text;
                    Color bg0 = b.resolvedStyle.backgroundColor;
                    float op0 = b.resolvedStyle.opacity;
                    for (int k = 0; k < 4; k++)
                    {
                        var click = b.userData as Action;
                        click?.Invoke();
                        yield return Wait(0.35f);
                        bool same = b.text == text0 && b.enabledSelf && b.enabledInHierarchy && b.panel != null
                                    && Mathf.Abs(b.resolvedStyle.opacity - op0) < 0.01f && ColorNear(b.resolvedStyle.backgroundColor, bg0);
                        Check(same, "click " + (k + 1) + ": still '" + b.text + "', clickable, same look");
                    }
                    Check(sent == 4 && lastId == 9001, "4 invites to the same friend (" + sent + ")");
                    Check(toasts == 4, "a small toast 'Einladung gesendet' each time (" + toasts + ")");
                    yield return Shot("89_invite_list_after_clicks");
                }

                // a re-opened list: still no 'invited' state anywhere
                NetLobby.DebugCloseInvitePicker();
                NetLobby.DebugShowInvitePicker(friends);
                yield return Wait(0.3f);
                bool clean = true;
                var root = NetLobby.DebugInvitePickerRoot;
                if (root != null)
                    root.Query<TextElement>().ForEach(t =>
                    {
                        string s = t.text ?? "";
                        if (s.IndexOf("Eingeladen", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("Erneut", StringComparison.OrdinalIgnoreCase) >= 0
                            || s.IndexOf("Invited", StringComparison.OrdinalIgnoreCase) >= 0 || s.IndexOf("again", StringComparison.OrdinalIgnoreCase) >= 0 && t is Button)
                            clean = false;
                    });
                var btns2 = NetLobby.DebugInviteButtons;
                for (int i = 0; i < btns2.Count; i++) clean &= btns2[i].text == NetLobby.InviteLabel && btns2[i].enabledSelf;
                Check(root != null && clean, "re-opened: every button still '" + NetLobby.InviteLabel + "' (no 'Eingeladen' / 'Erneut einladen')");
            }
            finally
            {
                NetLobby.DebugCloseInvitePicker();
                NetLobby.InviteSenderOverride = null;
                GameEvents.Notify -= onNote;
            }
        }

        static bool ColorNear(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a) < 0.02f;
    }
}
