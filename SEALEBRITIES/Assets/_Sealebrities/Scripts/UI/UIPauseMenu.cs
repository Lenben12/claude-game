using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Esc pause menu. Offline the game stops (Time.timeScale = 0); in co-op the world keeps running.
    /// Resume, settings, co-op crew (names, ping, speaking), save & main menu (host/offline) or leave session (client), quit.
    public class UIPauseMenu
    {
        readonly UIController ui;
        readonly VisualElement root, panel;
        bool settingsPage;
        bool leaving, dirty;
        float anim = 1f, crewTimer;
        UISettingsView settings;
        VisualElement crewHost;

        public UIPauseMenu(UIController ui, VisualElement layer)
        {
            this.ui = ui;
            root = new VisualElement { name = "sb-pause" };
            UIKit.FillParent(root);
            root.style.backgroundColor = new Color(0.08f, 0.06f, 0.16f, 0.45f);
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            root.style.display = DisplayStyle.None;
            layer.Add(root);

            panel = UIWidgets.ModalCard(540f);
            root.Add(panel);
        }

        public bool IsOpen => UIState.PauseOpen;

        /// The character screen (1c) opened from here: hide the card but stay paused; shown again when it closes.
        public void SetHidden(bool hidden)
        {
            if (!UIState.PauseOpen) return;
            root.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (!hidden) { anim = 0f; Refresh(); }
        }

        public void Open()
        {
            if (UIState.PauseOpen) return;
            UIState.PauseOpen = true;
            if (!Net.IsOnline) Time.timeScale = 0f;
            settingsPage = false;
            leaving = false;
            root.style.display = DisplayStyle.Flex;
            anim = 0f;
            Rebuild();
            UIAudio.Pop();
        }

        public void Close()
        {
            if (!UIState.PauseOpen && root.style.display.value == DisplayStyle.None) return;
            settings?.CancelCapture();
            UIState.PauseOpen = false;
            Time.timeScale = 1f;
            root.style.display = DisplayStyle.None;
            leaving = false;
        }

        public void HandleEscape()
        {
            if (leaving) return;
            if (settingsPage) { settings?.CancelCapture(); settingsPage = false; Rebuild(); }
            else Close();
        }

        /// Rebuild on the next frame (safe inside click callbacks).
        void Refresh() { dirty = true; }

        public void Tick(float dt)
        {
            if (!UIState.PauseOpen) return;
            if (Net.IsOnline && Time.timeScale == 0f) Time.timeScale = 1f;   // a session started while paused
            if (dirty) Rebuild();
            if (settingsPage) settings?.Tick(dt);
            else if (crewHost != null && Net.IsOnline)
            {
                crewTimer -= dt;
                if (crewTimer <= 0f) { crewTimer = 0.5f; BuildCrew(); }
            }
            if (anim >= 1f) return;
            anim = Mathf.Min(1f, anim + dt / 0.22f);
            float e = 1f - (1f - anim) * (1f - anim);
            root.style.opacity = e;
            panel.style.scale = new Scale(new Vector2(0.96f + 0.04f * e, 0.96f + 0.04f * e));
        }

        public void Rebuild()
        {
            dirty = false;
            if (!UIState.PauseOpen) return;
            panel.Clear();
            crewHost = null;
            panel.style.width = settingsPage ? 860f : 540f;
            if (settingsPage) BuildSettings(); else BuildMain();
        }

        void BuildMain()
        {
            panel.Add(UIWidgets.Header(UIGlyphs.Moon, UIPalette.Turquoise, Loc.Tr("Pause", "Paused"), Close, 34f));

            var gm = GameManager.I;
            if (gm != null && gm.InGame)
            {
                var s = gm.State;
                var info = UIKit.Row();
                info.Add(new UIGlyph(UIGlyphs.Sun, UIPalette.SunInk, 26f));
                var t = UIKit.Text(" " + Loc.Tr("Tag ", "Day ") + s.day + " · " + gm.ClockString + "   ", 19f, UIPalette.InkSoft, true);
                info.Add(t);
                info.Add(new UIGlyph(UIGlyphs.ForDifficulty(s.difficulty), UIWidgets.Ink(UIPalette.DifficultyColor(s.difficulty)), 26f));
                info.Add(UIKit.Text(" " + Loc.DifficultyName(s.difficulty), 19f, UIPalette.InkSoft, true));
                panel.Add(info);
                var stats = UIKit.Row();
                stats.style.marginTop = 14f;
                stats.Add(UIWidgets.IconStat(UIGlyphs.Coin, UIPalette.Sun, UIKit.Compact(s.money) + " $", UIPalette.Ink, 22f));
                stats.Add(UIKit.HSpacer(8f));
                stats.Add(UIWidgets.IconStat(UIGlyphs.Person, UIPalette.Turquoise, UIKit.Compact(s.followers), UIPalette.Ink, 22f));
                stats.Add(UIKit.HSpacer(8f));
                stats.Add(UIWidgets.IconStat(UIGlyphs.Book, UIPalette.TurquoiseInk, Journal.DiscoveredCount + "/" + SpeciesDatabase.All.Count, UIPalette.Ink, 22f));
                panel.Add(stats);
            }

            if (Net.IsOnline)
            {
                var head = UIKit.Row();
                head.style.marginTop = 18f;
                head.Add(new UIGlyph(UIGlyphs.Crew, UIPalette.InkSoft, 26f));
                var ht = UIKit.Text(" Crew · " + Net.PlayerCount + "/" + Net.MaxPlayers + (Net.IsHost ? Loc.Tr(" · du bist Host", " · you are host") : ""), 18f, UIPalette.InkSoft, true);
                head.Add(ht);
                panel.Add(head);
                crewHost = UIKit.Col();
                crewHost.style.marginTop = 6f;
                crewHost.style.backgroundColor = Color.white;
                UIKit.Radius(crewHost, 16f);
                UIKit.Pad(crewHost, 6f, 14f, 6f, 14f);
                panel.Add(crewHost);
                crewTimer = 0f;
                BuildCrew();
                if (Net.IsHost && !NetSession.IsSteam)   // Steam sessions: friends come by invite, IP/port mean nothing
                {
                    // the address friends type into "Beitreten" (LAN / VPN); port forwarding hint lives in the lobby
                    string ip = null;
                    try { ip = NetAddresses.PrimaryLan(); } catch (Exception) { ip = null; }
                    var addr = UIKit.Text("IP " + (string.IsNullOrEmpty(ip) ? "?" : ip) + " · Port " + NetSession.HostPort, 16f, UIPalette.InkSoft);
                    addr.style.marginTop = 6f;
                    panel.Add(addr);
                }
            }

            panel.Add(UIKit.Spacer(20f));
            AddButton(UIGlyphs.Play, Loc.Tr("Weiter", "Resume"), UIBtnKind.Primary, Close);
            // L.9 host safety valve: after 60 s at n−1/n the host may skip the night for an AFK friend
            if (SleepVote.ForceSleepAvailable)
                AddButton(UIGlyphs.Moon, Loc.Tr("Schlafen erzwingen", "Force sleep"), UIBtnKind.Accent, () => { Close(); SleepVote.ForceSleep(); });
            var row = UIKit.Row();
            row.style.marginBottom = 10f;
            var ch = MakeButton(UIGlyphs.Person, Loc.Tr("Charakter", "Character"), UIBtnKind.Secondary,
                () => ui.OpenCharacter(CharacterScreenContext.Pause, null));
            ch.style.flexGrow = 1; ch.style.flexBasis = 0; ch.style.marginBottom = 0f;
            row.Add(ch);
            var st = MakeButton(UIGlyphs.Gear, Loc.Tr("Einstellungen", "Settings"), UIBtnKind.Secondary, () => { settingsPage = true; Refresh(); });
            st.style.flexGrow = 1; st.style.flexBasis = 0; st.style.marginBottom = 0f; st.style.marginLeft = 10f;
            row.Add(st);
            panel.Add(row);
            // unstuck: wedged in the cave / ravine / rocks → back to the hut (per player, no co-op routing needed)
            if (CanReturnToHut()) AddButton(UIGlyphs.House, Loc.Tr("Zur Hütte", "Back to the hut"), UIBtnKind.Secondary, OnReturnToHut);
            if (Net.IsClient)
                AddButton(UIGlyphs.Crew, Loc.Tr("Sitzung verlassen", "Leave session"), UIBtnKind.Secondary, OnLeaveSession);
            else
                AddButton(UIGlyphs.House, Net.IsHost ? Loc.Tr("Speichern & Sitzung beenden", "Save & end session") : Loc.Tr("Speichern & Hauptmenü", "Save & main menu"),
                          UIBtnKind.Secondary, OnSaveAndMenu);
            AddButton(UIGlyphs.Cross, Loc.Tr("Spiel beenden", "Quit game"), UIBtnKind.Danger, OnQuit);
        }

        static bool CanReturnToHut()
        {
            var gm = GameManager.I;
            var p = Refs.Player;
            if (gm == null || !gm.InGame || p == null || (p is UnityEngine.Object o && o == null)) return false;
            return !p.IsUnderwater && !p.IsSeated && !p.IsFainting;
        }

        void OnReturnToHut()
        {
            if (!CanReturnToHut()) return;
            Close();
            Fader.FadeThrough(() =>
            {
                var p = Refs.Player;
                if (p == null || (p is UnityEngine.Object o && o == null) || p.IsSeated) return;
                Vector3 pos = PlayerController.ResolveSpawn(out float yaw);
                p.TeleportTo(pos, yaw);   // leaving the water this way banks the clips like any other exit
                // the boat comes home too (moored at the dock) – unless another co-op player still drives it, stands on
                // it or dives next to it; co-op guests ask the host, who moves the shared boat
                try { BoatHome.AfterLocalPlayerWentHome(); }
                catch (Exception e) { Debug.LogException(e); }
            });
        }

        void BuildCrew()
        {
            if (crewHost == null) return;
            crewHost.Clear();
            var players = Net.Players;
            if (players.Count == 0)
            {
                crewHost.Add(UIKit.Text(Loc.Tr("Verbinde …", "Connecting …"), 17f, UIPalette.InkSoft));
                return;
            }
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null) continue;
                var r = UIKit.Row();
                r.style.paddingTop = 6f; r.style.paddingBottom = 6f;
                if (i > 0) { r.style.borderTopWidth = 1f; r.style.borderTopColor = UIPalette.Line; }
                r.Add(UIKit.Dot(p.suit, 18f));
                var n = UIKit.Text(p.name + (p.IsLocal ? Loc.Tr(" (du)", " (you)") : ""), 19f, UIPalette.Ink, p.IsLocal);
                n.style.marginLeft = 10f;
                n.style.flexGrow = 1;
                n.style.flexShrink = 1;
                r.Add(n);
                bool talk = IsSpeaking(p.id);
                var spk = new UIGlyph(talk ? UIGlyphs.Speaker : UIGlyphs.Mic, talk ? UIPalette.LeafInk : UIPalette.Silhouette, 24f);
                spk.style.marginRight = 10f;
                if (p.IsLocal && Settings.Data.micMuted) spk.SetDraw(UIGlyphs.MicOff);
                r.Add(spk);
                if (!p.IsLocal || Net.IsClient)
                {
                    int ms = p.pingMs;
                    Color pc = ms < 80 ? UIPalette.LeafInk : ms < 180 ? UIPalette.SunInk : UIPalette.CoralInk;
                    r.Add(UIKit.Text(ms + " ms", 16f, pc, true));
                }
                crewHost.Add(r);
            }
        }

        /// Speaking state from the Net module's voice chat (the local player: transmitting right now).
        static bool IsSpeaking(int id)
        {
            try { return id == Net.LocalId ? NetVoice.LocalTransmitting : NetVoice.IsSpeaking(id); }
            catch (Exception) { return false; }
        }

        void BuildSettings()
        {
            panel.Add(UIWidgets.Header(UIGlyphs.Gear, UIPalette.Rgb(0x9AA3AE), Loc.Tr("Einstellungen", "Settings"), () => { settings?.CancelCapture(); settingsPage = false; Refresh(); }, 30f));
            if (settings == null) settings = new UISettingsView(false);
            settings.OnOpenCharacter = () => ui.OpenCharacter(CharacterScreenContext.Pause, null);
            settings.Root.RemoveFromHierarchy();
            panel.Add(settings.Root);
            settings.Refresh();
        }

        void AddButton(UIGlyph.DrawFn icon, string text, UIBtnKind kind, Action onClick)
        {
            panel.Add(MakeButton(icon, text, kind, onClick));
        }

        Button MakeButton(UIGlyph.DrawFn icon, string text, UIBtnKind kind, Action onClick)
        {
            var b = UIKit.Btn("", onClick, kind, 21f);
            b.style.flexDirection = FlexDirection.Row;
            b.style.alignItems = Align.Center;
            b.style.justifyContent = Justify.FlexStart;
            b.style.marginBottom = 10f;
            UIKit.Pad(b, 13f, 20f, 13f, 18f);
            var skin = b.userData as UIBtnSkin;
            Color fg = skin != null ? skin.fg : Color.white;
            b.Add(new UIGlyph(icon, fg, 28f));
            var l = UIKit.Text(text, 21f, fg, true);
            l.style.marginLeft = 14f;
            l.pickingMode = PickingMode.Ignore;
            b.Add(l);
            return b;
        }

        /// F.2: the host ending the session (or quitting) with guests connected asks first.
        static bool GuestsConnected => Net.IsHost && Net.PlayerCount > 1;

        void ConfirmEndSession(Action then)
        {
            if (!GuestsConnected) { then(); return; }
            UIDialog.Show(UIGlyphs.Crew, UIPalette.Coral, Loc.Tr("Alle werden getrennt. Wirklich beenden?", "Everyone will be disconnected. Really end?"), null,
                Loc.Tr("Abbrechen", "Cancel"), Loc.Tr("Beenden", "End"), then, null, true);
        }

        void OnSaveAndMenu() => ConfirmEndSession(DoSaveAndMenu);

        void DoSaveAndMenu()
        {
            if (leaving || GameManager.I == null) return;
            leaving = true;
            Fader.FadeThrough(() =>
            {
                Close();
                if (Net.IsHost && Net.Leave != null)
                {
                    try { Net.Leave(); } catch (Exception e) { Debug.LogException(e); }
                }
                if (GameManager.I != null && GameManager.I.InGame) GameManager.I.ReturnToMenu();
            }, 1.2f);
        }

        void OnLeaveSession()
        {
            if (leaving) return;
            leaving = true;
            Fader.FadeThrough(() =>
            {
                Close();
                if (Net.Leave != null)
                {
                    try { Net.Leave(); } catch (Exception e) { Debug.LogException(e); }
                }
                if (GameManager.I != null && GameManager.I.InGame) GameManager.I.ReturnToMenu();
            }, 1.2f);
        }

        void OnQuit() => ConfirmEndSession(DoQuit);

        void DoQuit()
        {
            if (leaving) return;
            if (GameManager.I != null) GameManager.I.Save();
            Application.Quit();
        }
    }
}
