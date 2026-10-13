using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sealebrities
{
    /// Round-3 step 6 (UI / phone / boat) – the Chopper-Shop catalogue (GDD §19 „jedes Item mit eigenem Bild“, §20 Q32
    /// „Katalog mit großen Bildern“): category chips, cards with a big unique 3D picture, stat bars, price, ETA and the
    /// order button, the detail page (bigger picture, normal / express), Back(), deal of the day, the old buy path.
    /// Screenshots 80_shop_*; the phone screen itself is also saved as 80_shop_*_screen.png (540 × 1080) when the
    /// 3D phone is used, and all pictures on one sheet as 80_shop_icons_sheet.png.
    public sealed partial class AutoPlaytest
    {
        IEnumerator StepR3UIShop()
        {
            var gm = GameManager.I;
            Check(gm != null && gm.InGame, "in a game");
            if (gm == null || !gm.InGame) yield break;
            var ui = UIModule.Controller;
            var phone = ui != null ? ui.Phone : null;
            var app = phone != null ? phone.Shop : null;
            Check(app != null, "the Chopper-Shop app exists");
            if (app == null) yield break;
            if (pc != null) pc.enabled = true;
            if (UIState.PhoneOpen) UIModule.DebugOpenPhone("close");
            if (UIState.InventoryOpen) UIModule.DebugOpenPhone("inventory:close");
            yield return Frames(2);

            long money0 = S.money;
            string deal0 = S.dealOfDayItem;
            if (S.money < 60000) S.money = 60000;   // most cards affordable for the shots (restored at the end)

            // ---- open: the page builds at once, pictures follow lazily (visible first, ≤ 2 per frame)
            int rc0 = UIItemIcons3D.RenderCount;
            bool opened = UIModule.DebugOpenPhone("shop");
            Check(opened && UIState.PhoneOpen && phone.Current == app, "the phone opens on the Chopper-Shop");
            if (!opened) { S.money = money0; yield break; }
            yield return null;
            int firstFrame = UIItemIcons3D.RenderCount - rc0;
            Check(firstFrame <= 2, "lazy pictures: " + firstFrame + " rendered in the first frame (≤ 2)");
            yield return R3UIShopWaitIcons(app, 4f);
            var b0 = app.LastBuild;
            Check(!b0.detail && b0.categories >= 6 && b0.cards > 0, "catalogue page: " + b0.categories + " category chips, " + b0.cards + " cards, " + b0.icons + " pictures");
            Check(app.PendingVisibleIcons == 0, "every visible picture is rendered after a short wait (" + app.PendingVisibleIcons + " visible + " + (app.PendingIcons - app.PendingVisibleIcons) + " further down still show the glyph)");
            yield return Shot("80_shop_catalogue");
            R3UIShopSaveScreen("80_shop_catalogue_screen");

            // ---- every category
            var cats = new List<ShopCategory>(app.VisibleCategories);
            var entries = new List<UIAppShop.Entry>();
            string[] expected = { "Diving", "Camera", "Boat", "Fishing", "Weapons", "Snacks" };
            var missingCats = new List<string>();
            foreach (var name in expected)
                if (!cats.Exists(c => c.ToString() == name)) missingCats.Add(name);
            Check(missingCats.Count == 0, "categories Tauchen · Kamera · Boot · Angeln · Speere · Snacks visible" + (missingCats.Count > 0 ? " – MISSING " + string.Join(", ", missingCats) : ""));
            for (int i = 0; i < cats.Count; i++)
            {
                app.DebugSelectCategory(i);
                yield return Frames(2);
                yield return R3UIShopWaitIcons(app, 4f);
                UIAppShop.Catalogue(cats[i], entries);
                var b = app.LastBuild;
                string cn = cats[i].ToString();
                Check(app.CategoryIndex == i && !b.detail && b.cards == entries.Count && b.cards > 0,
                      cn + ": " + b.cards + "/" + entries.Count + " cards, " + b.statBars + " stat bars, " + b.features + " feature chips, " + b.prices + " prices, " + b.etas + " ETAs, "
                      + b.buyButtons + " order buttons, " + b.statusPills + " states, " + b.locks + " locks");
                Check(b.prices + b.statusPills >= b.cards && b.etas == b.prices && b.buyButtons + b.locks == b.prices,
                      cn + ": every card shows its price + ETA + order button / lock, or its state");
                Check(b.statBars + b.features >= b.cards, cn + ": every card shows stat bars or a feature chip");
                Check(app.PendingVisibleIcons == 0, cn + ": every visible picture rendered (" + app.PendingVisibleIcons + " pending, " + app.PendingIcons + " incl. off-screen)");
                yield return Shot("80_shop_cat_" + cn.ToLowerInvariant());
            }

            // ---- all pictures: rendered, unique (pixel hash), not empty, not cut off by the frame
            var keys = new List<string>();
            UIAppShop.CatalogueKeys(keys);
            if (!keys.Contains("radio_battery")) keys.Add("radio_battery");   // story part, listed only during Toni's quest
            Check(keys.Count >= 30, keys.Count + " items sold (≥ 30 expected: 23 gear + 3 spears + 7 snacks)");
            var hashes = new Dictionary<uint, string>();
            var missing = new List<string>();
            var dupes = new List<string>();
            var empty = new List<string>();
            var cut = new List<string>();
            float sumMs = 0f, maxMs = 0f;
            int fresh = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                string k = keys[i];
                bool wasCached = UIItemIcons3D.Peek(k) != null;
                var t = UIItemIcons3D.Get(k);
                if (!wasCached && t != null)
                {
                    fresh++;
                    sumMs += UIItemIcons3D.LastRenderMs;
                    maxMs = Mathf.Max(maxMs, UIItemIcons3D.LastRenderMs);
                }
                if (t == null) { missing.Add(k); continue; }
                uint h = UIItemIcons3D.PixelHash(t);
                if (hashes.TryGetValue(h, out string other)) dupes.Add(k + " = " + other);
                else hashes[h] = k;
                float cov = UIItemIcons3D.Coverage(t);
                if (cov < 0.015f || cov > 0.9f) empty.Add(k + " " + F(cov, "0.00"));
                float edge = UIItemIcons3D.EdgeCoverage(t);
                if (edge > 0.05f) cut.Add(k + " " + F(edge, "0.00"));
                if ((i & 3) == 3) yield return null;
            }
            Check(missing.Count == 0, "every sold item has a 3D picture (" + (keys.Count - missing.Count) + "/" + keys.Count + ")" + (missing.Count > 0 ? " – MISSING " + string.Join(", ", missing) : ""));
            Check(dupes.Count == 0, "no two pictures are pixel-identical (" + hashes.Count + " distinct)" + (dupes.Count > 0 ? " – SAME: " + string.Join("; ", dupes) : ""));
            Check(empty.Count == 0, "every picture shows its model (1.5–90 % of the pixels covered)" + (empty.Count > 0 ? " – " + string.Join(", ", empty) : ""));
            Check(cut.Count == 0, "no model is cut off by the picture frame" + (cut.Count > 0 ? " – " + string.Join(", ", cut) : ""));
            if (fresh > 0) Metric("shop.iconRenderMs", "avg " + F(sumMs / fresh, "0.0") + " ms, max " + F(maxMs, "0.0") + " ms (" + fresh + " rendered in this step)");
            Texture2D sheet = null;
            try
            {
                sheet = UIItemIcons3D.ContactSheet(keys, 6);
                if (sheet != null)
                {
                    File.WriteAllBytes(Path.Combine(outDir, "80_shop_icons_sheet.png"), sheet.EncodeToPNG());
                    Info("saved 80_shop_icons_sheet.png (" + keys.Count + " pictures in reading order: " + string.Join(", ", keys) + ")");
                }
            }
            catch (Exception e) { Info("icon sheet failed: " + e.Message); }
            finally { if (sheet != null) Object.Destroy(sheet); }

            // ---- a gear category: stat bars + price + ETA on the cards
            int diving = cats.IndexOf(ShopCategory.Diving);
            if (diving < 0) diving = 0;
            app.DebugSelectCategory(diving);
            yield return Frames(2);
            var bd = app.LastBuild;
            Check(bd.statBars >= 2 && bd.prices > 0 && bd.etas > 0 && bd.buyButtons + bd.locks > 0, "Tauchen cards carry stat bars (" + bd.statBars + "), prices (" + bd.prices + "), ETAs (" + bd.etas + ") and order buttons / locks");
            string etaStd = UIAppShop.EtaText(false), etaExp = UIAppShop.EtaText(true);
            Check(!string.IsNullOrEmpty(etaStd) && !string.IsNullOrEmpty(etaExp) && etaStd != etaExp,
                  "ETA at " + GameManager.Clock(S.timeOfDay) + ": normal '" + etaStd + "' (" + UIAppShop.RelText(false) + "), express '" + etaExp + "' (" + UIAppShop.RelText(true) + ")");

            // ---- deal of the day: gold ribbon, −20 %, struck-through old price
            UIAppShop.Catalogue(ShopCategory.Diving, entries);
            string dealId = null;
            foreach (var e in entries)
                if (e.gear != null && !e.gear.comingSoon && !Shop.IsOwned(e.gear) && !Shop.IsOrdered(e.gear) && e.gear.price > 0 && e.gear.id != "uw_radio" && e.gear.id != "rod_t1") { dealId = e.gear.id; break; }
            if (dealId != null)
            {
                S.dealOfDayItem = dealId;
                app.DebugSelectCategory(diving);
                yield return Frames(3);
                var dg = GearDatabase.Get(dealId);
                long want = (long)Mathf.Round(dg.price * (1f - Shop.DealOfDayDiscount));
                Check(app.LastBuild.deals >= 1 && Shop.Price(dg, false) == want, "deal of the day on " + dealId + ": ribbon shown, " + dg.price + " $ → " + Shop.Price(dg, false) + " $");
                yield return Shot("80_shop_deal");
            }
            else Info("deal check skipped (everything in Tauchen is owned)");

            // ---- detail page: bigger picture, all stats, normal + express with ETA, order; Back() → catalogue
            string detailKey = dealId ?? "tank_t2";
            app.DebugOpenDetail(detailKey);
            yield return Frames(2);
            yield return R3UIShopWaitIcons(app, 3f);
            var dd = app.LastBuild;
            Check(app.DetailKey == detailKey && dd.detail && dd.icons >= 1 && dd.statBars >= 2 && dd.prices >= 1,
                  "detail page of " + detailKey + ": big picture, " + dd.statBars + " stat bars, " + dd.prices + " price");
            Check(dd.options >= 2 && dd.etas >= 2 && dd.buyButtons + dd.locks + dd.statusPills >= 1, "detail: normal + express delivery with ETA (" + dd.options + " options) and the order button / lock / state");
            Check(phone.Current == app && app.HeaderTitle == GearDatabase.Get(detailKey).Name, "the phone header shows the item name ('" + app.HeaderTitle + "')");
            yield return Shot("80_shop_detail");
            R3UIShopSaveScreen("80_shop_detail_screen");
            app.DebugSetExpress(true);
            yield return Frames(3);
            Check(app.Express && app.LastBuild.detail && app.DetailKey == detailKey, "express selected on the detail page (price +" + Mathf.RoundToInt(Shop.ExpressSurcharge * 100f) + " %)");
            yield return Shot("80_shop_detail_express");
            app.DebugSetExpress(false);
            yield return Frames(2);
            phone.Back();
            yield return Frames(3);
            Check(app.DetailKey == null && phone.Current == app && UIState.PhoneOpen && !app.LastBuild.detail && app.LastBuild.cards > 0, "Back() returns from the detail page to the catalogue (phone stays in the shop)");

            // ---- spear gun + snack details
            var gun = ItemDatabase.Get(ItemIds.Speargun);
            if (gun != null)
            {
                app.DebugOpenDetail(ItemDatabase.DeliveryId(gun));
                yield return Frames(2);
                yield return R3UIShopWaitIcons(app, 3f);
                Check(app.LastBuild.detail && app.LastBuild.statBars >= 3, "speargun detail: power / reach / durability bars (" + app.LastBuild.statBars + ")");
                yield return Shot("80_shop_detail_speargun");
            }
            var bar = FoodDatabase.Get("krill_bar");
            if (bar != null)
            {
                app.DebugOpenDetail(FoodDatabase.DeliveryId(bar));
                yield return Frames(2);
                yield return R3UIShopWaitIcons(app, 3f);
                Check(app.LastBuild.detail && app.LastBuild.options >= 1 && app.LastBuild.etas >= 1, "snack detail: hunger bar, buff chip, express-included delivery with ETA");
                yield return Shot("80_shop_detail_snack");
            }
            phone.Back();
            yield return Frames(3);

            // ---- the order button runs the old buy path (cheapest snack pack), then the test order is undone
            var chips = FoodDatabase.Get("seaweed_chips");
            if (chips != null && Shop.CanBuyFood(chips, out _))
            {
                string id = FoodDatabase.DeliveryId(chips);
                long m0 = S.money;
                int n0 = S.deliveries.Count;
                bool ok = app.DebugBuy(id);
                Check(ok && S.money == m0 - chips.price && S.deliveries.Count == n0 + 1 && Shop.IsOrdered(id),
                      "order button → Shop.BuyFood: −" + chips.price + " $, delivery '" + id + "' (" + (ok ? "ok" : "refused") + ")");
                yield return Wait(0.6f);
                Check(app.LastBuild.cards > 0, "the catalogue rebuilt after the order (tracker + 'Unterwegs')");
                yield return Shot("80_shop_tracker");
                var d = Shop.FindDelivery(id);
                if (d != null && !d.dropped) S.deliveries.Remove(d);
                S.money = m0;
            }
            else Info("snack order skipped (seaweed chips cannot be ordered right now)");

            UIModule.DebugOpenPhone("close");
            S.dealOfDayItem = deal0;
            S.money = money0;
            GameEvents.RaiseMoneyChanged(S.money);
            yield return Frames(2);
        }

        /// Waits until the shop page shows every picture (or the timeout).
        static IEnumerator R3UIShopWaitIcons(UIAppShop app, float timeout)
        {
            float t = 0f;
            while (app != null && app.PendingVisibleIcons > 0 && t < timeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        /// Saves the phone's 540 × 1080 screen texture (only the 3D phone renders into it).
        void R3UIShopSaveScreen(string name)
        {
            var ui = UIModule.Controller;
            var rt = PhoneDisplay.Texture;
            if (ui == null || ui.Phone == null || !ui.Phone.In3D || rt == null)
            {
                Info(name + ": phone screen dump skipped (flat 2D phone)");
                return;
            }
            var prev = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
                tex.Apply(false, false);
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                Info("saved " + name + ".png (phone screen " + rt.width + "×" + rt.height + ")");
            }
            catch (Exception e) { Info(name + ": phone screen dump failed: " + e.Message); }
            finally
            {
                RenderTexture.active = prev;
                if (tex != null) Object.Destroy(tex);
            }
        }
    }
}
