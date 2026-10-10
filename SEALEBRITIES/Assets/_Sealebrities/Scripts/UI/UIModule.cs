using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Entry point of the UI module (UI Toolkit, built entirely in code). Called by GameBootstrap:
    /// Install → ShowLoading/HideLoading → ShowMainMenu.
    public static class UIModule
    {
        static UIController controller;
        static bool hasPendingLoading;
        static float pendingProgress;
        static string pendingText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            controller = null;
            hasPendingLoading = false;
            pendingProgress = 0f;
            pendingText = null;
        }

        public static UIController Controller => controller;

        public static void Install(Transform root)
        {
            if (controller != null) return;
            var go = new GameObject("SB_UI");
            go.SetActive(false);
            if (root != null) go.transform.SetParent(root, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = CreatePanelSettings();
            doc.sortingOrder = 100;
            var ctrl = go.AddComponent<UIController>();
            go.SetActive(true);
            if (doc.rootVisualElement == null)
            {
                Debug.LogError("Sealebrities UI: UIDocument has no root visual element (missing PanelSettings?).");
                return;
            }
            ctrl.Init(doc);
            controller = ctrl;

            if (hasPendingLoading || UIState.LoadingOpen)
                ctrl.ShowLoading(pendingProgress, pendingText ?? Loc.Tr("Lade …", "Loading …"));
            hasPendingLoading = false;
        }

        public static void ShowLoading(float progress01, string text)
        {
            UIState.LoadingOpen = true;
            if (controller == null)
            {
                hasPendingLoading = true;
                pendingProgress = progress01;
                pendingText = text;
                return;
            }
            controller.ShowLoading(progress01, text);
        }

        public static void HideLoading()
        {
            UIState.LoadingOpen = false;
            hasPendingLoading = false;
            if (controller != null) controller.HideLoading();
        }

        public static void ShowMainMenu()
        {
            UIState.MainMenuOpen = true;
            if (controller != null) controller.ShowMainMenu();
        }

        /// Debug/AutoShot helper: opens the phone on an app by id ("sealog", "shop", "fintube", "krill", "followfish",
        /// "map", "messages", "settings"). "sealog:&lt;speciesId&gt;" opens that species' journal page, null or "" the home
        /// screen, "close" closes the phone. Returns false when the phone cannot open (no game, menu/pause/loading open).
        public static bool DebugOpenPhone(string app)
        {
            if (controller == null || controller.Phone == null) return false;
            var phone = controller.Phone;
            if (app == "close")
            {
                phone.Close();
                return true;
            }

            string id = app, detail = null;
            if (!string.IsNullOrEmpty(app))
            {
                int colon = app.IndexOf(':');
                if (colon >= 0)
                {
                    id = app.Substring(0, colon);
                    detail = app.Substring(colon + 1);
                }
            }

            UIPhoneApp target = null;
            if (!string.IsNullOrEmpty(id))
            {
                UIPhoneApp[] apps = { phone.Sealog, phone.Shop, phone.FinTube, phone.Krill, phone.FollowFish, phone.Map, phone.Messages, phone.Settings };
                for (int i = 0; i < apps.Length; i++)
                    if (apps[i] != null && apps[i].Id == id) { target = apps[i]; break; }
                if (target == null) Debug.LogWarning("Sealebrities UI: unknown phone app '" + id + "'.");
            }

            phone.Open(target);
            if (!UIState.PhoneOpen) return false;
            if (target == phone.Sealog && !string.IsNullOrEmpty(detail))
            {
                var s = SpeciesDatabase.Get(detail);
                if (s != null) phone.Sealog.ShowSpecies(s);
            }
            return true;
        }

        static PanelSettings CreatePanelSettings()
        {
            var ps = Resources.Load<PanelSettings>("SealebritiesPanelSettings");
            if (ps != null)
            {
                if (ps.themeStyleSheet != null) return ps;
                // never modify the asset itself (would persist in the editor): work on a runtime copy
                ps = Object.Instantiate(ps);
            }
            else
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                ps.match = 0.5f;
            }
            ps.name = "SB_RuntimePanelSettings";
            if (ps.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>("SealebritiesTheme");
                if (theme != null) ps.themeStyleSheet = theme;
            }
            return ps;
        }
    }
}
