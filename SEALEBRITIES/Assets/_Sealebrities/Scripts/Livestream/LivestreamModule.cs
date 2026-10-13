using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Entry point of the Livestream module (Design_Runde2 C + K.7):
    ///   • StreamService – KRILL.tv simulation (host/offline), camera eye, dares, chat director, faint rule, co-op mirror;
    ///   • StreamNet – net messages 90 StreamState / 91 StreamFeed / 92 StreamChat;
    ///   • SponsorService – sponsor deal offers, conditions, rewards, fail mails, the deal entry of the quest line;
    ///   • KrillTv – public facade (go-live rules, start/end/dare requests, end summary, test hooks);
    ///   • LiveOverlay + KrillTvApp + BarnacleApp – HUD and phone pages, used while the UI module ships no C.7 / K.7
    ///     screens of its own (detected by the class names UIStreamOverlay / UIAppDeals).
    /// Installs itself after the scene load (like the net-sync module); GameBootstrap may also call Install(root).
    /// Custom co-op commands: CoreCommands.StreamStartKey / StreamEndKey / DareKey and "SponsorProgress".
    public static class LivestreamModule
    {
        static GameObject host;

        public static StreamService Stream { get; private set; }
        public static SponsorService Sponsors { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { host = null; Stream = null; Sponsors = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoInstall()
        {
            try { Install(null); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public static void Install(Transform root)
        {
            if (host != null) return;
            host = new GameObject("Livestream");
            if (root != null) host.transform.SetParent(root, false);
            else UnityEngine.Object.DontDestroyOnLoad(host);

            var asm = typeof(LiveState).Assembly;
            KrillTv.BuiltInUI = asm.GetType("Sealebrities.UIStreamOverlay") == null;

            StreamSfx.Init(host);
            Stream = host.AddComponent<StreamService>();
            Sponsors = host.AddComponent<SponsorService>();
            var bridge = host.AddComponent<LivestreamUIBridge>();
            bridge.injectDeals = asm.GetType("Sealebrities.UIAppDeals") == null;

            StreamNet.Install();
            SponsorService.InstallHooks();
            CoreCommands.RegisterCustom(CoreCommands.StreamStartKey, (from, a) =>
                Stream != null && Stream.HostStart(from, Arg(a, 1), false));
            CoreCommands.RegisterCustom(CoreCommands.StreamEndKey, (from, a) =>
                Stream != null && Stream.HostEndCommand(from, Arg(a, 1)));
            CoreCommands.RegisterCustom(CoreCommands.DareKey, (from, a) =>
                Stream != null && Stream.HostDareCommand(from, Arg(a, 1), Arg(a, 2)));
        }

        static string Arg(string[] a, int i) => a != null && i < a.Length ? a[i] : null;
    }

    /// Hooks the built-in HUD and the phone apps into the UI document as soon as the UI module exists, and ticks the HUD.
    public sealed class LivestreamUIBridge : MonoBehaviour
    {
        internal bool injectDeals = true;
        LiveOverlay overlay;
        bool phoneDone, overlayDone;
        float retry;

        void Update()
        {
            if (!phoneDone || !overlayDone)
            {
                retry -= Time.unscaledDeltaTime;
                if (retry <= 0f)
                {
                    retry = 0.5f;
                    TryInstall();
                }
            }
            if (overlay != null) overlay.Tick(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        void OnDestroy()
        {
            if (overlay != null) overlay.Dispose();
            overlay = null;
        }

        void TryInstall()
        {
            var ctrl = UIModule.Controller;
            if (ctrl == null) return;
            if (!overlayDone)
            {
                overlayDone = true;
                if (KrillTv.BuiltInUI) InstallOverlay(ctrl.Root);
            }
            if (!phoneDone && ctrl.Phone != null)
            {
                phoneDone = true;
                InstallApps(ctrl.Phone);
            }
        }

        void InstallOverlay(VisualElement root)
        {
            if (root == null) return;
            try
            {
                // between the notifications and the phone: phone, inventory and menus draw on top of the HUD
                var holder = new VisualElement { name = "sb-layer-live-holder", pickingMode = PickingMode.Ignore };
                UIKit.FillParent(holder);
                var phoneLayer = root.Q("sb-layer-phone");
                int at = phoneLayer != null ? root.IndexOf(phoneLayer) : -1;
                if (at >= 0) root.Insert(at, holder);
                else root.Add(holder);
                overlay = new LiveOverlay(holder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Livestream] overlay could not be installed: " + e.Message);
                overlay = null;
            }
        }

        void InstallApps(UIPhone phone)
        {
            try
            {
                var list = phone.Apps as List<UIPhoneApp>;
                if (list == null) return;
                if (KrillTv.BuiltInUI)
                {
                    int krill = IndexOf(list, "krill");
                    var app = new KrillTvApp();
                    app.Attach(phone);
                    if (krill >= 0) list[krill] = app;
                    else list.Insert(Mathf.Max(0, list.Count - 1), app);
                }
                if (injectDeals && IndexOf(list, "deals") < 0 && IndexOf(list, "barnacle") < 0 && IndexOf(list, "sponsors") < 0)
                {
                    var deals = new BarnacleApp();
                    deals.Attach(phone);
                    int settings = IndexOf(list, "settings");
                    list.Insert(settings >= 0 ? settings : list.Count, deals);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Livestream] phone apps could not be installed: " + e.Message);
            }
        }

        static int IndexOf(List<UIPhoneApp> list, string id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].Id == id) return i;
            return -1;
        }
    }
}
