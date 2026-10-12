using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Round 3 (GDD §19/§20 Q42): the next dive site's requirement, small at the top centre under the quest line –
    /// lock + „Wrack + Vulkan“ · person „1,2k/2,5k“ · book „45/60 % Kelpwald“ (a met part turns green with a tick).
    /// Nothing is announced before it is reached: the „Neuer Tauchplatz“ banner comes from Progression.CheckUnlocks.
    /// Rebuilt twice a second only when a number changed (no per-frame allocations).
    public class UISiteGoal
    {
        readonly VisualElement row, fPart, pPart;
        readonly UIHudGlyph fGlyph, pGlyph;
        readonly Label name, fText, pText;
        bool shown;
        float timer;
        string siteId;
        long fHave = -1, fNeed = -1;
        int pHave = -1, pNeed = -1;
        Language lang;

        public UISiteGoal(VisualElement parent)
        {
            row = UIKit.Row();
            row.pickingMode = PickingMode.Ignore;
            row.style.marginTop = 4f;
            row.style.justifyContent = Justify.Center;
            row.style.alignItems = Align.Center;
            // round 3 UI polish: a faint pill keeps it readable over the bright sky; one line, max 620 px – the site name
            // gives way first (never runs into the push card top-right)
            row.style.maxWidth = 620f;
            row.style.backgroundColor = new Color(0.07f, 0.09f, 0.14f, 0.28f);
            UIKit.Radius(row, 14f);
            UIKit.Pad(row, 3f, 12f, 3f, 10f);
            var lockG = new UIHudGlyph(UIGlyphs.Lock, UIPalette.Sun, 18f);
            lockG.style.marginRight = 5f;
            row.Add(lockG);
            name = UIKit.HudText("", 17f, Color.white);
            name.style.flexShrink = 1;
            name.style.whiteSpace = WhiteSpace.NoWrap;
            name.style.overflow = Overflow.Hidden;
            name.style.textOverflow = TextOverflow.Ellipsis;
            row.Add(name);
            fPart = UIKit.Row();
            fPart.style.marginLeft = 10f;
            fPart.style.alignItems = Align.Center;
            fPart.style.flexShrink = 0;
            fGlyph = new UIHudGlyph(UIGlyphs.Person, Color.white, 17f);
            fGlyph.style.marginRight = 3f;
            fPart.Add(fGlyph);
            fText = UIKit.HudText("", 17f, Color.white);
            fPart.Add(fText);
            row.Add(fPart);
            pPart = UIKit.Row();
            pPart.style.marginLeft = 10f;
            pPart.style.alignItems = Align.Center;
            pPart.style.flexShrink = 0;
            pGlyph = new UIHudGlyph(UIGlyphs.Book, Color.white, 17f);
            pGlyph.style.marginRight = 3f;
            pPart.Add(pGlyph);
            pText = UIKit.HudText("", 17f, Color.white);
            pPart.Add(pText);
            row.Add(pPart);
            UIKit.IgnorePicking(row);
            row.style.display = DisplayStyle.None;
            parent.Add(row);
        }

        public void Tick(float dt)
        {
            var gm = GameManager.I;
            bool can = gm != null && gm.InGame && !UIState.MainMenuOpen && Settings.Data.questDisplay != 2
                       && gm.State.questIndex >= TutorialQuests.Finished;
            timer -= dt;
            if (!can) { Show(false); return; }
            if (timer > 0f) return;
            timer = 0.5f;
            var site = SiteGoal.Next();
            if (site == null) { Show(false); return; }
            SiteGoal.Parts(site, out long fh, out long fn, out float ph, out float pn, out string page);
            int phI = Mathf.RoundToInt(ph * 100f), pnI = Mathf.RoundToInt(pn * 100f);
            if (site.id != siteId || fh != fHave || fn != fNeed || phI != pHave || pnI != pNeed || lang != Loc.Current)
            {
                siteId = site.id; fHave = fh; fNeed = fn; pHave = phI; pNeed = pnI; lang = Loc.Current;
                name.text = SiteGoal.ShortName(site) + (SiteGoal.OpensVolcano(site) ? Loc.Tr(" + Vulkaninsel", " + Volcano Isle") : "");
                bool fOk = fh >= fn;
                UIKit.Show(fPart, fn > 0);
                fText.text = fOk ? UIKit.Compact(fn) : UIKit.Compact(fh) + "/" + UIKit.Compact(fn);
                fText.style.color = fOk ? UIPalette.Leaf : Color.white;
                fGlyph.SetDraw(fOk ? (UIGlyph.DrawFn)UIGlyphs.Check : UIGlyphs.Person);
                fGlyph.SetColor(fOk ? UIPalette.Leaf : Color.white);
                bool pOk = phI >= pnI;
                UIKit.Show(pPart, pnI > 0);
                pText.text = (pOk ? pnI + " %" : phI + "/" + pnI + " %") + " " + page;
                pText.style.color = pOk ? UIPalette.Leaf : Color.white;
                pGlyph.SetDraw(pOk ? (UIGlyph.DrawFn)UIGlyphs.Check : UIGlyphs.Book);
                pGlyph.SetColor(pOk ? UIPalette.Leaf : Color.white);
            }
            Show(true);
        }

        void Show(bool on)
        {
            if (on == shown) return;
            shown = on;
            row.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// Test hook: the row is visible and its texts.
        public bool Visible => shown;
        public string Text => shown ? name.text + " | " + fText.text + " | " + pText.text : null;
    }
}
