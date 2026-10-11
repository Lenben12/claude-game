using UnityEngine;
using UnityEngine.UIElements;

namespace Sealebrities
{
    /// The SEALEBRITIES water logo: white Fredoka letters whose lower part is filled with turquoise water,
    /// dark outline, soft shadow (art bible §1.14).
    public static class UILogo
    {
        public static VisualElement Build(float fontSize, float width, float height, TextAnchor align)
        {
            var host = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Size(host, width, height);
            host.Add(Label(Color.white, fontSize, width, height, align));
            var clip = new VisualElement { pickingMode = PickingMode.Ignore };
            clip.style.position = Position.Absolute;
            clip.style.left = 0; clip.style.right = 0;
            clip.style.top = height * 0.58f;
            clip.style.bottom = 0;
            clip.style.overflow = Overflow.Hidden;
            var teal = Label(UIPalette.Turquoise, fontSize, width, height, align);
            teal.style.top = -height * 0.58f;
            clip.Add(teal);
            host.Add(clip);
            return host;
        }

        static Label Label(Color c, float size, float w, float h, TextAnchor align)
        {
            var l = UIKit.Text("SEALEBRITIES", size, c, true);
            l.style.position = Position.Absolute;
            l.style.left = 0; l.style.top = 0;
            l.style.width = w; l.style.height = h;
            l.style.unityTextAlign = align;
            l.style.letterSpacing = size * 0.02f;
            l.style.unityTextOutlineWidth = Mathf.Max(2f, size * 0.025f);
            l.style.unityTextOutlineColor = new Color(0x1B / 255f, 0x24 / 255f, 0x33 / 255f, 0.6f);
            l.style.textShadow = new TextShadow { offset = new Vector2(0f, size * 0.04f), blurRadius = 8f, color = new Color(0f, 0f, 0f, 0.3f) };
            l.pickingMode = PickingMode.Ignore;
            return l;
        }
    }

    /// Loading screen (GDD 15.40, art bible §1.14): underwater gradient, slanted light bands, rising bubbles,
    /// a low-poly fish crossing every few seconds, the water logo, a rounded progress bar and real ocean facts + tips.
    public class UILoadingScreen
    {
        static readonly string[,] Facts =
        {
            { "Der Ozean bedeckt rund 71 % der Erdoberfläche.", "The ocean covers about 71 % of Earth's surface." },
            { "Mehr als die Hälfte unseres Sauerstoffs stammt aus dem Meer – vor allem von winzigem Plankton.", "More than half of the oxygen we breathe comes from the ocean – mostly from tiny plankton." },
            { "Der Marianengraben ist an seiner tiefsten Stelle fast 11.000 Meter tief.", "The Mariana Trench is almost 11,000 meters deep at its deepest point." },
            { "Der Blauwal ist das größte Tier, das je gelebt hat. Allein sein Herz wiegt rund 180 kg.", "The blue whale is the largest animal that ever lived. Its heart alone weighs about 180 kg." },
            { "Oktopusse haben drei Herzen und blaues Blut.", "Octopuses have three hearts and blue blood." },
            { "Bei Seepferdchen bekommen die Männchen die Babys.", "In seahorses, the males carry and give birth to the babies." },
            { "Grönlandhaie können mehrere Jahrhunderte alt werden.", "Greenland sharks can live for several centuries." },
            { "Mehr als 80 % des Ozeans sind noch unerforscht.", "More than 80 % of the ocean is still unexplored." },
            { "Sonnenlicht reicht nur etwa 200 Meter tief – darunter beginnt die Dämmerzone.", "Sunlight only reaches about 200 meters deep – below that begins the twilight zone." },
            { "Viele Tiefseetiere erzeugen ihr eigenes Licht. Das nennt man Biolumineszenz.", "Many deep-sea animals make their own light. That's called bioluminescence." },
            { "Meeresschildkröten kehren oft an den Strand zurück, an dem sie geschlüpft sind.", "Sea turtles often return to the very beach where they hatched." },
            { "Mondfische können über 2 Tonnen wiegen – die schwersten Knochenfische der Welt.", "Ocean sunfish can weigh over 2 tonnes – the heaviest bony fish in the world." },
            { "Rotes Licht verschwindet unter Wasser zuerst – deshalb wirkt in der Tiefe alles so blau.", "Red light disappears first underwater – that's why everything looks so blue at depth." },
            { "Walhaie sind die größten Fische der Welt und fressen vor allem Plankton.", "Whale sharks are the largest fish in the world and mostly eat plankton." },
            { "Clownfische schlüpfen alle als Männchen – der größte wird zum Weibchen.", "All clownfish hatch as males – the largest one becomes the female." },
            { "Das Great Barrier Reef ist so groß, dass man es aus dem All sehen kann.", "The Great Barrier Reef is so big it can be seen from space." },
        };

        static readonly string[,] Tips =
        {
            { "Größere Tiere bringen mehr Aufrufe: Ein legendäres Exemplar zählt ×4.", "Bigger animals bring more views: a legendary specimen counts ×4." },
            { "Clips sind erst sicher, wenn du wieder an Land oder auf dem Boot bist.", "Clips are only safe once you're back on land or on the boat." },
            { "Gefangene Fische landen in der Kühlbox – verkaufen kannst du sie in der Fischmarkt-App.", "Caught fish go into the cooler box – sell them in the Fish Market app." },
            { "Rechte Hand {R1}–{R3}, linke Hand {L1}–{L3}. Die Angel braucht beide Hände.", "Right hand {R1}–{R3}, left hand {L1}–{L3}. The fishing rod needs both hands." },
            { "Luft füllst du nur an Land oder auf dem Boot wieder auf.", "You only refill your air on land or on the boat." },
            { "Gary liefert noch am selben Tag – 2 bis 3 Stunden nach der Bestellung.", "Gary delivers the same day – 2 to 3 hours after you order." },
            { "Im Inventar ({INV}) ziehst du Kamera, Lampe und Angel in deine Hände.", "In the inventory ({INV}) you drag the camera, lamp and rod into your hands." },
            { "Schlaf in deiner Hütte: Am Morgen kommen die Ergebnisse deiner Videos.", "Sleep in your hut: your video results arrive in the morning." },
        };

        readonly VisualElement root, fish;
        readonly UIBar bar;
        readonly Label status, fact, factCaption;
        readonly VisualElement[] bubbles = new VisualElement[18];
        readonly float[] bubbleSeed = new float[18];
        readonly VisualElement[] bands = new VisualElement[4];
        int factIndex, shownCount;
        bool showingTip;
        float factTimer, time, fishTimer;
        float fadeOut = -1f;
        string lastText;

        public UILoadingScreen(VisualElement layer)
        {
            root = new VisualElement { name = "sb-loading" };
            UIKit.FillParent(root);
            UIKit.Image(root, UIKit.VGradient(UIPalette.Rgb(0x1A5A8C), UIPalette.Rgb(0x5ECAD9)), false);
            root.style.backgroundColor = UIPalette.Rgb(0x2D90BC);
            root.style.overflow = Overflow.Hidden;
            layer.Add(root);

            // slanted light bands from the surface
            var bandTex = UIKit.VGradient(new Color(1f, 1f, 1f, 0f), new Color(0.87f, 0.98f, 1f, 0.16f));
            for (int i = 0; i < bands.Length; i++)
            {
                var b = new VisualElement { pickingMode = PickingMode.Ignore };
                b.style.position = Position.Absolute;
                b.style.top = Length.Percent(-20f);
                b.style.height = Length.Percent(130f);
                b.style.width = 110f + i * 45f;
                b.style.left = Length.Percent(12f + i * 22f);
                b.style.rotate = new Rotate(Angle.Degrees(18f));
                UIKit.Image(b, bandTex, false);
                root.Add(b);
                bands[i] = b;
            }

            fish = new UIGlyph(UIGlyphs.Fish, new Color(0.06f, 0.24f, 0.36f, 0.45f), 120f);
            fish.style.position = Position.Absolute;
            fish.style.top = Length.Percent(60f);
            root.Add(fish);

            var bubbleLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(bubbleLayer);
            root.Add(bubbleLayer);
            for (int i = 0; i < bubbles.Length; i++)
            {
                float size = 8f + (i * 37 % 22);
                var b = new VisualElement { pickingMode = PickingMode.Ignore };
                b.style.position = Position.Absolute;
                UIKit.Size(b, size, size);
                UIKit.Radius(b, size * 0.5f);
                UIKit.Border(b, 2f, new Color(1f, 1f, 1f, 0.55f));
                b.style.backgroundColor = new Color(1f, 1f, 1f, 0.08f);
                b.style.left = Length.Percent(i * 61 % 100);
                bubbleSeed[i] = i * 0.618f % 1f;
                bubbleLayer.Add(b);
                bubbles[i] = b;
            }

            var center = UIKit.Col();
            UIKit.FillParent(center);
            center.style.alignItems = Align.Center;
            center.style.justifyContent = Justify.Center;
            center.pickingMode = PickingMode.Ignore;
            root.Add(center);
            center.Add(UILogo.Build(118f, 1000f, 150f, TextAnchor.MiddleCenter));

            var barBox = UIKit.Col();
            barBox.style.width = 480f;
            barBox.style.marginTop = 60f;
            barBox.style.alignItems = Align.Center;
            center.Add(barBox);
            bar = new UIBar(10f, Color.white, new Color(1f, 1f, 1f, 0.25f));
            bar.style.width = 480f;
            barBox.Add(bar);
            status = UIKit.HudText("", 18f, new Color(1f, 1f, 1f, 0.85f), false);
            status.style.marginTop = 12f;
            barBox.Add(status);

            var factBox = UIKit.Col();
            factBox.style.position = Position.Absolute;
            factBox.style.bottom = 70f;
            factBox.style.left = 0; factBox.style.right = 0;
            factBox.style.alignItems = Align.Center;
            root.Add(factBox);
            factCaption = UIKit.HudText("", 18f, UIPalette.Sand);
            factCaption.style.marginBottom = 8f;
            factBox.Add(factCaption);
            fact = UIKit.HudText("", 26f);
            fact.style.maxWidth = 1100f;
            fact.style.whiteSpace = WhiteSpace.Normal;
            fact.style.unityTextAlign = TextAnchor.MiddleCenter;
            factBox.Add(fact);

            UIKit.IgnorePicking(root);
            root.pickingMode = PickingMode.Position;   // block clicks to the menu underneath
            factIndex = Random.Range(0, Facts.GetLength(0));
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
            if (!ReferenceEquals(text, lastText)) { lastText = text; status.text = text ?? ""; }
        }

        public void Hide()
        {
            if (root.style.display.value == DisplayStyle.None) return;
            fadeOut = 0f;
        }

        /// Key placeholders in tips → the player's current bindings (only when a tip is shown).
        static string WithKeys(string s)
        {
            if (s == null || s.IndexOf('{') < 0) return s;
            return s.Replace("{R1}", GameInput.BindingLabel(GameAction.RightSlot1))
                    .Replace("{R3}", GameInput.BindingLabel(GameAction.RightSlot3))
                    .Replace("{L1}", GameInput.BindingLabel(GameAction.LeftSlot1))
                    .Replace("{L3}", GameInput.BindingLabel(GameAction.LeftSlot3))
                    .Replace("{INV}", GameInput.BindingLabel(GameAction.Inventory));
        }

        public void RefreshTexts()
        {
            if (showingTip)
            {
                int i = factIndex % Tips.GetLength(0);
                factCaption.text = Loc.Tr("TIPP", "TIP");
                fact.text = WithKeys(Loc.Tr(Tips[i, 0], Tips[i, 1]));
            }
            else
            {
                int i = factIndex % Facts.GetLength(0);
                factCaption.text = Loc.Tr("WUSSTEST DU?", "DID YOU KNOW?");
                fact.text = Loc.Tr(Facts[i, 0], Facts[i, 1]);
            }
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

            factTimer += dt;
            if (factTimer > 6f)
            {
                factTimer = 0f;
                shownCount++;
                showingTip = shownCount % 3 == 2;   // every third card is a game tip
                factIndex++;
                RefreshTexts();
            }
            fact.style.opacity = Mathf.Clamp01(Mathf.Min(factTimer / 0.4f, (6f - factTimer) / 0.4f));
            factCaption.style.opacity = fact.style.opacity;

            for (int i = 0; i < bubbles.Length; i++)
            {
                float speed = 0.06f + bubbleSeed[i] * 0.08f;
                float y = Mathf.Repeat(bubbleSeed[i] + time * speed, 1f);
                bubbles[i].style.top = Length.Percent(105f - y * 115f);
                bubbles[i].style.translate = new Translate(Mathf.Sin(time * (0.8f + bubbleSeed[i]) + i) * 14f, 0f);
            }
            for (int i = 0; i < bands.Length; i++)
                bands[i].style.translate = new Translate(Mathf.Sin(time * 0.6f + i * 1.7f) * 40f, 0f);

            // a fish crossing every ~4 s, alternating direction
            fishTimer += dt;
            const float cross = 4f;
            float f = Mathf.Repeat(fishTimer, cross) / cross;
            bool leftToRight = Mathf.FloorToInt(fishTimer / cross) % 2 == 0;
            float x = leftToRight ? Mathf.Lerp(-10f, 105f, f) : Mathf.Lerp(105f, -10f, f);
            fish.style.left = Length.Percent(x);
            fish.style.top = Length.Percent(56f + Mathf.Sin(fishTimer * 1.7f) * 3f + (leftToRight ? 0f : 12f));
            // the glyph faces right (head on the left, tail right) – mirror it when swimming right
            fish.style.scale = new Scale(new Vector2(leftToRight ? -1f : 1f, 1f));
        }
    }
}
