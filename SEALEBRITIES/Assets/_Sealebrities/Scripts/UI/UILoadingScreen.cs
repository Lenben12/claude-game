using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// Boot loading screen: title, "Live from the Deep" pill, progress bar, status text, rotating tips and rising bubbles.
    public class UILoadingScreen
    {
        static readonly string[,] Tips =
        {
            { "Größere Tiere bringen mehr Aufrufe: Ein legendäres Exemplar zählt ×4.", "Bigger animals bring more views: a legendary specimen counts ×4." },
            { "Clips sind erst sicher, wenn du wieder an Land oder auf dem Boot bist.", "Clips are only safe once you're back on land or on the boat." },
            { "Im Sealog (J) siehst du, welche Tiere dir noch fehlen – und dein größtes Exemplar jeder Art.", "The Sealog (J) shows which animals you're missing – and your biggest specimen of each species." },
            { "Fülle eine Tagebuch-Seite zu 60 %, um den nächsten Tauchplatz freizuschalten.", "Fill a journal page to 60 % to unlock the next dive site." },
            { "Clickbait-Titel bringen mehr Aufrufe, aber deutlich weniger echte Follower.", "Clickbait titles bring more views, but far fewer real followers." },
            { "Ab 100 Followern zahlt FinTube dir Geld pro Aufruf.", "From 100 followers on, FinTube pays you per view." },
            { "Die Taschenlampe (F) bringt in der Tiefe die Farben zurück – und sorgt für bessere Clips.", "The flashlight (F) brings colors back in the deep – and makes better clips." },
            { "Tauch nicht tiefer, als deine Flasche erlaubt. Die Tiefenanzeige warnt dich.", "Don't dive deeper than your tank allows. The depth gauge warns you." },
            { "Pilot Gary liefert Bestellungen am nächsten Morgen zum Helipad. Express kostet 50 % extra.", "Pilot Gary delivers orders to the helipad the next morning. Express costs 50 % extra." },
            { "Schlaf in deiner Hütte: Am nächsten Morgen kommen die Ergebnisse deiner Videos.", "Sleep in your hut: your video results arrive the next morning." },
            { "Ruhige Kamera, gutes Licht und ein Tier in Action ergeben die wertvollsten Clips.", "A steady camera, good light and an animal in action make the most valuable clips." },
            { "Raubtiere rempeln dich an und kosten dich Luft. Halte Abstand!", "Predators bump into you and cost you air. Keep your distance!" },
            { "Ab 1.000 Followern schaltest du KRILL.tv frei.", "At 1,000 followers you unlock KRILL.tv." },
        };

        readonly VisualElement root;
        readonly UIBar bar;
        readonly Label status, percent, tip, tipCaption;
        readonly VisualElement[] bubbles = new VisualElement[16];
        readonly float[] bubbleSeed = new float[16];
        int tipIndex;
        float tipTimer, time;
        float fadeOut = -1f;
        int lastPercent = -1;
        string lastText;

        public UILoadingScreen(VisualElement layer)
        {
            root = new VisualElement { name = "sb-loading" };
            UIKit.FillParent(root);
            UIKit.Image(root, UIKit.VGradient(UIPalette.Rgb(0x040A12), UIPalette.Rgb(0x0E2B40)), false);
            root.style.backgroundColor = UIPalette.Deep;
            root.style.alignItems = Align.Center;
            root.style.justifyContent = Justify.Center;
            layer.Add(root);

            var bubbleLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(bubbleLayer);
            bubbleLayer.style.overflow = Overflow.Hidden;
            root.Add(bubbleLayer);
            for (int i = 0; i < bubbles.Length; i++)
            {
                float size = 6f + (i * 37 % 19);
                var b = new VisualElement { pickingMode = PickingMode.Ignore };
                b.style.position = Position.Absolute;
                UIKit.Size(b, size, size);
                UIKit.Radius(b, size * 0.5f);
                UIKit.Border(b, 1.5f, new Color(0.7f, 0.9f, 1f, 0.22f));
                b.style.backgroundColor = new Color(0.7f, 0.9f, 1f, 0.04f);
                b.style.left = Length.Percent((i * 61 % 100));
                bubbleSeed[i] = i * 0.618f % 1f;
                bubbleLayer.Add(b);
                bubbles[i] = b;
            }

            var center = UIKit.Col();
            center.style.alignItems = Align.Center;
            root.Add(center);

            var title = UIKit.Text("SEALEBRITIES", 120f, UIPalette.Text, true);
            title.style.letterSpacing = 10f;
            UIKit.Shadow(title, 18f, 0.6f);
            center.Add(title);

            var pill = UIKit.Row();
            pill.style.backgroundColor = UIPalette.Live;
            UIKit.Radius(pill, 999f);
            UIKit.Pad(pill, 7f, 18f, 7f, 16f);
            pill.style.marginTop = 4f;
            var dot = UIKit.Dot(Color.white, 10f);
            dot.style.marginRight = 10f;
            pill.Add(dot);
            var pillText = UIKit.Text("LIVE FROM THE DEEP", 20f, Color.white, true);
            pillText.style.letterSpacing = 4f;
            pill.Add(pillText);
            center.Add(pill);

            center.Add(UIKit.Spacer(80f));

            var barBox = UIKit.Col();
            barBox.style.width = 760f;
            center.Add(barBox);
            bar = new UIBar(10f, UIPalette.Lagoon, new Color(1f, 1f, 1f, 0.08f));
            barBox.Add(bar);
            var row = UIKit.Row();
            row.style.marginTop = 12f;
            status = UIKit.Text("", 16f, UIPalette.TextDim);
            row.Add(status);
            row.Add(UIKit.Flex());
            percent = UIKit.Text("0 %", 16f, UIPalette.Text, true);
            row.Add(percent);
            barBox.Add(row);

            var tipBox = UIKit.Col();
            tipBox.style.position = Position.Absolute;
            tipBox.style.bottom = 70f;
            tipBox.style.left = 0; tipBox.style.right = 0;
            tipBox.style.alignItems = Align.Center;
            root.Add(tipBox);
            tipCaption = UIKit.Caption("Tipp", UIPalette.Sun, 13f);
            tipCaption.style.marginBottom = 8f;
            tipBox.Add(tipCaption);
            tip = UIKit.Text("", 19f, UIPalette.Text);
            tip.style.maxWidth = 1100f;
            tip.style.whiteSpace = WhiteSpace.Normal;
            tip.style.unityTextAlign = TextAnchor.MiddleCenter;
            tipBox.Add(tip);

            UIKit.IgnorePicking(root);
            root.pickingMode = PickingMode.Position;   // block clicks to the menu underneath
            tipIndex = Random.Range(0, Tips.GetLength(0));
            RefreshTexts();
            root.style.display = DisplayStyle.None;
        }

        public bool Visible => root.style.display.value == DisplayStyle.Flex && fadeOut < 0f;

        public void Show(float progress01, string text)
        {
            fadeOut = -1f;
            root.style.display = DisplayStyle.Flex;
            root.style.opacity = 1f;
            bar.SetValue(progress01);
            int pc = Mathf.RoundToInt(Mathf.Clamp01(progress01) * 100f);
            if (pc != lastPercent) { lastPercent = pc; percent.text = pc + " %"; }
            if (!ReferenceEquals(text, lastText)) { lastText = text; status.text = text ?? ""; }
        }

        public void Hide()
        {
            if (root.style.display.value == DisplayStyle.None) return;
            fadeOut = 0f;
        }

        public void RefreshTexts()
        {
            tipCaption.text = Loc.Tr("TIPP", "TIP");
            tip.text = Loc.Tr(Tips[tipIndex, 0], Tips[tipIndex, 1]);
        }

        public void Tick(float dt)
        {
            if (root.style.display.value == DisplayStyle.None) return;
            time += dt;
            if (fadeOut >= 0f)
            {
                fadeOut += dt;
                float a = 1f - Mathf.Clamp01(fadeOut / 0.45f);
                root.style.opacity = a;
                if (a <= 0f) { root.style.display = DisplayStyle.None; fadeOut = -1f; }
            }

            tipTimer += dt;
            if (tipTimer > 5.5f)
            {
                tipTimer = 0f;
                tipIndex = (tipIndex + 1) % Tips.GetLength(0);
                RefreshTexts();
            }
            float tipAlpha = Mathf.Clamp01(Mathf.Min(tipTimer / 0.4f, (5.5f - tipTimer) / 0.4f));
            tip.style.opacity = tipAlpha;

            for (int i = 0; i < bubbles.Length; i++)
            {
                float speed = 0.05f + bubbleSeed[i] * 0.07f;
                float y = Mathf.Repeat(bubbleSeed[i] + time * speed, 1f);
                bubbles[i].style.top = Length.Percent(105f - y * 115f);
                bubbles[i].style.translate = new Translate(Mathf.Sin(time * (0.8f + bubbleSeed[i]) + i) * 14f, 0f);
            }
        }
    }
}
