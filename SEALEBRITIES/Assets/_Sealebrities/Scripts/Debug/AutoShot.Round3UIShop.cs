using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 6 screenshots – the Chopper-Shop catalogue (GDD §20 Q32 „Katalog mit großen Bildern“). Files: r3u_shop_*.png
    ///   r3u_shop_catalogue (3D phone in the hand) + r3u_shop_screen_catalogue (the phone screen itself, 540 × 1080)
    ///   r3u_shop_cat_&lt;category&gt; / r3u_shop_screen_cat_&lt;category&gt; (every category)
    ///   r3u_shop_detail_&lt;item&gt; / r3u_shop_screen_detail_&lt;item&gt; (tank deal, deep sea rod, pro camera, speargun, krill bar,
    ///   outboard with express selected)
    ///   r3u_shop_icons (all pictures on one sheet) + r3u_shop_icons/&lt;key&gt;.png (every picture alone, 256 px)
    public sealed partial class AutoShot
    {
        IEnumerator ShotsRound3UIShop()
        {
            yield return Guard(ShotR3UIShopPhone(), "r3u_shop");
            yield return Guard(ShotR3UIShopIcons(), "r3u_shop_icons");
            if (pc != null) pc.enabled = true;
        }

        IEnumerator ShotR3UIShopPhone()
        {
            var gm = GameManager.I;
            if (gm == null || !gm.InGame) { Note("WARNING: r3u_shop – not in a game"); yield break; }
            var s = gm.State;
            long money0 = s.money;
            string deal0 = s.dealOfDayItem;
            SetTime(0.45f);
            yield return EnsureOnLand();
            Vector3 dock = WorldInfo.DockEndPosition != Vector3.zero ? WorldInfo.DockEndPosition : WorldLayout.DockEnd + Vector3.up * 1.3f;
            Hands.StowAll();
            yield return Stand(dock + new Vector3(-6f, 0.05f, 0f), 262f);
            if (s.money < 60000) s.money = 60000;
            if (!s.ownedItems.Contains("tank_t2")) s.dealOfDayItem = "tank_t2";
            if (!UIModule.DebugOpenPhone("shop")) { Note("WARNING: r3u_shop – phone did not open"); s.money = money0; s.dealOfDayItem = deal0; yield break; }
            float t = 0f;
            while (!PhoneDisplay.Raised && t < 2f) { t += Time.unscaledDeltaTime; yield return null; }
            var app = UIModule.Controller != null && UIModule.Controller.Phone != null ? UIModule.Controller.Phone.Shop : null;
            if (app == null) { Note("WARNING: r3u_shop – no shop app"); UIModule.DebugOpenPhone("close"); s.money = money0; s.dealOfDayItem = deal0; yield break; }

            yield return R3UIShopIconsReady(app, 4f);
            yield return Wait(0.6f);
            yield return Capture("r3u_shop_catalogue");
            R3UIShopScreen("r3u_shop_screen_catalogue");

            var cats = new List<ShopCategory>(app.VisibleCategories);
            for (int i = 0; i < cats.Count; i++)
            {
                app.DebugSelectCategory(i);
                yield return Frames(2);
                yield return R3UIShopIconsReady(app, 4f);
                yield return Wait(0.3f);
                string n = cats[i].ToString().ToLowerInvariant();
                yield return Capture("r3u_shop_cat_" + n);
                R3UIShopScreen("r3u_shop_screen_cat_" + n);
            }

            string[] details = { "tank_t2", "rod_t2", "cam_t3", "item:" + ItemIds.Speargun, "food:krill_bar" };
            for (int i = 0; i < details.Length; i++)
            {
                if (UIAppShop.EntryFor(details[i]) == null) continue;
                app.DebugOpenDetail(details[i]);
                yield return Frames(2);
                yield return R3UIShopIconsReady(app, 4f);
                yield return Wait(0.3f);
                string n = R3UIShopFileName(details[i]);
                yield return Capture("r3u_shop_detail_" + n);
                R3UIShopScreen("r3u_shop_screen_detail_" + n);
            }
            if (UIAppShop.EntryFor("boat_t1") != null)
            {
                app.DebugOpenDetail("boat_t1");
                app.DebugSetExpress(true);
                yield return Frames(2);
                yield return R3UIShopIconsReady(app, 4f);
                yield return Wait(0.3f);
                yield return Capture("r3u_shop_detail_express");
                R3UIShopScreen("r3u_shop_screen_detail_express");
                app.DebugSetExpress(false);
            }
            UIModule.DebugOpenPhone("close");
            s.money = money0;
            s.dealOfDayItem = deal0;
            GameEvents.RaiseMoneyChanged(s.money);
            yield return Frames(3);
        }

        /// All pictures on one sheet + every picture as its own file (r3u_shop_icons/…).
        IEnumerator ShotR3UIShopIcons()
        {
            var keys = new List<string>();
            UIAppShop.CatalogueKeys(keys);
            if (!keys.Contains("radio_battery")) keys.Add("radio_battery");
            // the starter gear the cards compare with (not sold, still pictured)
            string[] starters = { "tank_t0", "fins_t0", "lamp_t1", "cam_t0", "rod_t0", "boat_t0", "rod_t3", "item:" + ItemIds.Stick };
            for (int i = 0; i < starters.Length; i++) if (!keys.Contains(starters[i])) keys.Add(starters[i]);
            Texture2D sheet = null;
            try
            {
                string dir = Path.Combine(outDir, "r3u_shop_icons");
                Directory.CreateDirectory(dir);
                int n = 0;
                for (int i = 0; i < keys.Count; i++)
                {
                    var tex = UIItemIcons3D.Get(keys[i]);
                    if (tex == null) { Note("WARNING: r3u_shop_icons – no picture for " + keys[i]); continue; }
                    File.WriteAllBytes(Path.Combine(dir, R3UIShopFileName(keys[i]) + ".png"), tex.EncodeToPNG());
                    n++;
                }
                sheet = UIItemIcons3D.ContactSheet(keys, 7);
                if (sheet != null)
                {
                    File.WriteAllBytes(Path.Combine(outDir, "r3u_shop_icons.png"), sheet.EncodeToPNG());
                    saved++;
                }
                Note("captured r3u_shop_icons.png (" + keys.Count + " pictures, " + n + " single files): " + string.Join(", ", keys));
            }
            catch (Exception e) { Note("ERROR: r3u_shop_icons – " + e.Message); }
            finally { if (sheet != null) Object.Destroy(sheet); }
            yield return Frames(2);
        }

        static string R3UIShopFileName(string key) => string.IsNullOrEmpty(key) ? "none" : key.Replace(':', '_');

        static IEnumerator R3UIShopIconsReady(UIAppShop app, float timeout)
        {
            float t = 0f;
            while (app != null && app.PendingVisibleIcons > 0 && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// The phone screen texture as its own file (only while the 3D phone is used).
        void R3UIShopScreen(string name)
        {
            var ui = UIModule.Controller;
            var rt = PhoneDisplay.Texture;
            if (ui == null || ui.Phone == null || !ui.Phone.In3D || rt == null) return;
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false, false);
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                saved++;
                Note("captured " + name + ".png (phone screen)");
            }
            catch (Exception e) { Note("WARNING: " + name + " – " + e.Message); }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) Object.Destroy(tex);
            }
        }
    }
}
