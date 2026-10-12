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

    /// Loading screen (GDD 15.40; round 3: GDD §19 „Ladebildschirm viel besser gestalten“, §20 Q35 „animierte
    /// Unterwasser-Szene mit Tipps und Fakten“). Full-screen behind the UI: the live 3D reef of UILoadingScene (the
    /// player's own diver swimming laps among a fish school, Gerald the turtle, a manta, light shafts, bubbles, corals and
    /// kelp) shown through its RenderTexture; without 3D (no GPU, build failure) the flat animated gradient with light
    /// bands, bubbles and a crossing fish. On top: the water logo, a rotating TIP card (bottom left) and a rotating
    /// OCEAN FACT card (bottom right), each with a round icon, and the progress bar with a little fish riding along.
    /// The 3D scene exists only while the screen is visible (built on Show, disposed when the fade-out ends).
    public class UILoadingScreen
    {
        // ------------------------------------------------------------------ content
        /// ~30 short, real ocean facts (DE / EN), each with an icon.
        static readonly string[,] Facts =
        {
            { "Ein Oktopus hat drei Herzen und blaues Blut.", "An octopus has three hearts and blue blood." },
            { "Der Blauwal ist das größte Tier, das je gelebt hat.", "The blue whale is the largest animal that ever lived." },
            { "Bei Seepferdchen bekommen die Männchen die Babys.", "In seahorses, the males give birth to the babies." },
            { "Meeresschildkröten legen ihre Eier oft an dem Strand, an dem sie selbst geschlüpft sind.", "Sea turtles often lay their eggs on the very beach where they hatched." },
            { "Der Ozean bedeckt rund 71 % der Erdoberfläche.", "The ocean covers about 71 % of Earth's surface." },
            { "Mehr als die Hälfte unseres Sauerstoffs stammt aus dem Meer.", "More than half of the oxygen we breathe comes from the ocean." },
            { "Der Marianengraben ist fast 11 km tief.", "The Mariana Trench is almost 11 km deep." },
            { "Mehr als 80 % des Ozeans sind noch unerforscht.", "More than 80 % of the ocean is still unexplored." },
            { "Grönlandhaie können über 250 Jahre alt werden.", "Greenland sharks can live for more than 250 years." },
            { "Clownfische schlüpfen alle als Männchen.", "All clownfish hatch as males." },
            { "Rotes Licht verschwindet unter Wasser zuerst – darum ist dort alles so blau.", "Red light fades first underwater – that's why everything looks so blue." },
            { "Viele Tiefseetiere leuchten selbst. Das nennt man Biolumineszenz.", "Many deep-sea animals make their own light. That's bioluminescence." },
            { "Korallen sind Tiere – keine Pflanzen.", "Corals are animals, not plants." },
            { "Das Great Barrier Reef ist das größte Bauwerk aus Lebewesen.", "The Great Barrier Reef is the largest structure built by living things." },
            { "Delfine haben Namen: Jeder hat seinen eigenen Pfiff.", "Dolphins have names: each one has its own signature whistle." },
            { "Quallen gibt es schon seit über 500 Millionen Jahren.", "Jellyfish have been around for more than 500 million years." },
            { "Quallen haben weder Herz noch Gehirn.", "Jellyfish have neither a heart nor a brain." },
            { "Seesterne können verlorene Arme nachwachsen lassen.", "Starfish can regrow lost arms." },
            { "Krabben laufen meistens seitwärts.", "Crabs mostly walk sideways." },
            { "Mantarochen haben das größte Gehirn aller Fische.", "Manta rays have the largest brain of any fish." },
            { "Manche Papageifische schlafen nachts in einem Schlafsack aus Schleim.", "Some parrotfish sleep in a sleeping bag of mucus at night." },
            { "Pottwale können über eine Stunde lang tauchen.", "Sperm whales can dive for more than an hour." },
            { "Schall ist unter Wasser rund viermal so schnell wie in der Luft.", "Sound travels about four times faster in water than in air." },
            { "Ein Knallkrebs schnappt mit seiner Schere lauter als ein Pistolenschuss.", "A pistol shrimp's claw snaps louder than a gunshot." },
            { "Haie gab es schon vor den Bäumen.", "Sharks existed before trees did." },
            { "Mondfische können über 2 Tonnen wiegen.", "Ocean sunfish can weigh more than 2 tonnes." },
            { "Meerwasser enthält im Schnitt etwa 3,5 % Salz.", "Seawater contains about 3.5 % salt on average." },
            { "Seeotter halten sich beim Schlafen an den Pfoten, damit sie nicht abtreiben.", "Sea otters hold paws while sleeping so they don't drift apart." },
            { "Vom Meeresgrund aus gemessen ist der Mauna Kea über 10.000 m hoch.", "Measured from the seafloor, Mauna Kea is over 10,000 m tall." },
        };

        static readonly UIGlyph.DrawFn[] FactIcons =
        {
            UIGlyphs.LoadingOctopus, UIGlyphs.Whale, UIGlyphs.Seahorse, UIGlyphs.Turtle, UIGlyphs.Globe, UIGlyphs.Bubbles,
            UIGlyphs.Depth, UIGlyphs.Microscope, UIGlyphs.SharkFin, UIGlyphs.Fish, UIGlyphs.Sun, UIGlyphs.Sparkle,
            UIGlyphs.LoadingCoral, UIGlyphs.LoadingCoral, UIGlyphs.LoadingDolphin, UIGlyphs.LoadingJelly, UIGlyphs.LoadingJelly,
            UIGlyphs.LoadingStarfish, UIGlyphs.LoadingCrab, UIGlyphs.LoadingRay, UIGlyphs.Fish, UIGlyphs.Whale, UIGlyphs.Speaker,
            UIGlyphs.Shrimp, UIGlyphs.SharkFin, UIGlyphs.Fish, UIGlyphs.Shell, UIGlyphs.Heart, UIGlyphs.ArrowUp,
        };

        /// Game tips (adapted to the 8-slot hotbar and survival), each with an icon.
        static readonly string[,] Tips =
        {
            { "Größere Tiere bringen mehr Aufrufe: Ein legendäres Exemplar zählt ×4.", "Bigger animals bring more views: a legendary specimen counts ×4." },
            { "Clips sind erst sicher, wenn du wieder an Land oder auf dem Boot bist.", "Clips are only safe once you're back on land or on the boat." },
            { "Gefangene Fische landen in der Kühlbox – verkaufen kannst du sie in der Fischmarkt-App.", "Caught fish go into the cooler box – sell them in the Fish Market app." },
            { "Leiste {R1}–{S8}. Im Rucksack ({INV}) wählst du Haupt- und Zweithand. Angel und Harpune brauchen beide Hände.", "Hotbar {R1}–{S8}. In the backpack ({INV}) you pick main hand and offhand. Rod and speargun need both hands." },
            { "Hunger und Durst: Fisch am Lagerfeuer braten, Meerwasser im Wasserfilter der Hütte trinkbar machen.", "Hunger and thirst: cook fish at a campfire, make seawater drinkable at the hut's water filter." },
            { "Kartenstücke liegen überall auf der Insel – manche sind vergraben. Mit dem Stock gräbst du schneller.", "Map pieces lie all over the island – some are buried. A stick digs faster." },
            { "Luft füllst du nur an Land oder auf dem Boot wieder auf.", "You only refill your air on land or on the boat." },
            { "Gary liefert noch am selben Tag – 2 bis 3 Stunden nach der Bestellung.", "Gary delivers the same day – 2 to 3 hours after you order." },
            { "Im Rucksack ({INV}) ziehst du alles auf die Leiste oder direkt in die Hände.", "In the backpack ({INV}) you drag anything onto the hotbar or straight into your hands." },
            { "Schlaf in deiner Hütte: Am Morgen kommen die Ergebnisse deiner Videos.", "Sleep in your hut: your video results arrive in the morning." },
        };

        static readonly UIGlyph.DrawFn[] TipIcons =
        {
            UIGlyphs.Eye, UIGlyphs.Camera, UIGlyphs.Market, UIGlyphs.Bag, UIGlyphs.Food,
            UIGlyphs.Pin, UIGlyphs.Tank, UIGlyphs.Heli, UIGlyphs.Hand, UIGlyphs.Bed,
        };

        public static int FactCount => Facts.GetLength(0);
        public static int TipCount => Tips.GetLength(0);

        const float CardSeconds = 9f, CardFade = 0.5f, FadeOutSeconds = 0.45f;

        // ------------------------------------------------------------------ elements
        readonly VisualElement root, sceneImage, logoHost, trackFill;
        readonly VisualElement[] logoBubbles = new VisualElement[6];
        readonly UIHudGlyph rider;
        readonly Label status, percent;
        readonly Card tipCard, factCard;
        readonly FlatBackdrop flat;

        UILoadingScene scene;
        bool sceneFailed;
        int tipIndex, factIndex, lastPercent = -1;
        float tipT, factT, time, target, shown, lastFill = -1f;
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

            flat = new FlatBackdrop(root);

            // the 3D reef (RenderTexture, cropped to the screen aspect)
            sceneImage = new VisualElement { name = "sb-loading-scene", pickingMode = PickingMode.Ignore };
            UIKit.FillParent(sceneImage);
            root.Add(sceneImage);

            // soft scrims so the logo and the bottom row read on any frame
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(top, 0f, 0f, 0f, null);
            top.style.height = 260f;
            UIKit.Image(top, UIKit.VGradient(new Color(0.03f, 0.16f, 0.27f, 0f), new Color(0.03f, 0.16f, 0.27f, 0.5f)), false);
            root.Add(top);
            var bottomScrim = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(bottomScrim, 0f, null, 0f, 0f);
            bottomScrim.style.height = 380f;
            UIKit.Image(bottomScrim, UIKit.VGradient(new Color(0.02f, 0.1f, 0.19f, 0.8f), new Color(0.02f, 0.1f, 0.19f, 0f)), false);
            root.Add(bottomScrim);

            // logo (gently bobbing, bubbles rising from the letters)
            var logoWrap = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Abs(logoWrap, 0f, 30f, 0f, null);
            logoWrap.style.alignItems = Align.Center;
            root.Add(logoWrap);
            logoHost = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.Size(logoHost, 980f, 132f);
            logoHost.Add(UILogo.Build(104f, 980f, 132f, TextAnchor.MiddleCenter));
            for (int i = 0; i < logoBubbles.Length; i++)
            {
                float size = 9f + (i * 5) % 9;
                var b = new VisualElement { pickingMode = PickingMode.Ignore };
                b.style.position = Position.Absolute;
                UIKit.Size(b, size, size);
                UIKit.Radius(b, size * 0.5f);
                UIKit.Border(b, 2f, new Color(1f, 1f, 1f, 0.75f));
                b.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
                b.style.left = 150f + i * 128f + (i * 37) % 40;
                logoHost.Add(b);
                logoBubbles[i] = b;
            }
            logoWrap.Add(logoHost);

            // bottom row: TIP card · progress · FACT card
            var row = UIKit.Row();
            row.pickingMode = PickingMode.Ignore;
            UIKit.Abs(row, 56f, null, 56f, 40f);
            row.style.alignItems = Align.FlexEnd;
            row.style.justifyContent = Justify.SpaceBetween;
            root.Add(row);

            tipCard = new Card(UIPalette.Coral, UIPalette.Sun, false);
            row.Add(tipCard.Root);

            var mid = UIKit.Col();
            mid.pickingMode = PickingMode.Ignore;
            mid.style.width = 600f;
            mid.style.flexShrink = 1;
            mid.style.minWidth = 320f;
            mid.style.marginLeft = 36f;
            mid.style.marginRight = 36f;
            mid.style.paddingBottom = 10f;
            mid.style.alignItems = Align.Center;
            row.Add(mid);
            status = UIKit.HudText("", 22f, new Color(1f, 1f, 1f, 0.95f), true);
            status.style.whiteSpace = WhiteSpace.NoWrap;
            status.style.overflow = Overflow.Hidden;
            status.style.textOverflow = TextOverflow.Ellipsis;
            status.style.maxWidth = Length.Percent(100f);
            status.style.unityTextAlign = TextAnchor.MiddleCenter;
            mid.Add(status);

            var barRow = UIKit.Row();
            barRow.pickingMode = PickingMode.Ignore;
            barRow.style.width = Length.Percent(100f);
            barRow.style.marginTop = 16f;
            mid.Add(barRow);
            var trackHost = new VisualElement { pickingMode = PickingMode.Ignore };
            trackHost.style.flexGrow = 1;
            trackHost.style.height = 18f;
            barRow.Add(trackHost);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            UIKit.FillParent(track);
            UIKit.Radius(track, 9f);
            UIKit.Border(track, 2f, new Color(1f, 1f, 1f, 0.7f));
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.16f);
            track.style.overflow = Overflow.Hidden;
            trackHost.Add(track);
            trackFill = new VisualElement { pickingMode = PickingMode.Ignore };
            trackFill.style.position = Position.Absolute;
            trackFill.style.left = 0; trackFill.style.top = 0; trackFill.style.bottom = 0;
            trackFill.style.width = Length.Percent(0f);
            UIKit.Radius(trackFill, 7f);
            UIKit.Image(trackFill, UIKit.HGradient(UIPalette.Rgb(0x7FE0E8), Color.white), false);
            track.Add(trackFill);
            rider = new UIHudGlyph(UIGlyphs.Fish, UIPalette.Coral, 40f);
            rider.style.position = Position.Absolute;
            rider.style.top = -11f;
            rider.style.marginLeft = -24f;
            rider.style.scale = new Scale(new Vector2(-1f, 1f));   // the glyph faces left – swim right
            trackHost.Add(rider);
            percent = UIKit.HudText("0 %", 22f, Color.white, true);
            percent.style.width = 76f;
            percent.style.marginLeft = 14f;
            percent.style.unityTextAlign = TextAnchor.MiddleRight;
            barRow.Add(percent);

            factCard = new Card(UIPalette.Rgb(0x2FA9B4), UIPalette.Rgb(0x9BEAF0), true);
            row.Add(factCard.Root);

            UIKit.IgnorePicking(root);
            root.pickingMode = PickingMode.Position;   // block clicks to the menu underneath
            tipIndex = Random.Range(0, TipCount);
            factIndex = Random.Range(0, FactCount);
            RefreshTexts();
            root.style.display = DisplayStyle.None;
        }

        // ------------------------------------------------------------------ API
        public bool Visible => root.style.display.value == DisplayStyle.Flex && fadeOut < 0f;

        /// The live 3D scene (null while hidden or when 3D is unavailable) – tests / AutoShot.
        public UILoadingScene Scene => scene;
        public string TipText => tipCard.Text;
        public string FactText => factCard.Text;
        /// Displayed (smoothed) progress 0..1.
        public float ShownProgress => shown;

        public void Show(float progress01, string text)
        {
            if (float.IsNaN(progress01)) progress01 = 0f;
            progress01 = Mathf.Clamp01(progress01);
            bool wasHidden = root.style.display.value == DisplayStyle.None;
            fadeOut = -1f;
            root.style.display = DisplayStyle.Flex;
            root.style.opacity = 1f;
            if (wasHidden)
            {
                // a fresh loading run: new cards, bar from where it is, a new attempt at the 3D scene
                sceneFailed = false;
                shown = progress01;
                tipIndex = (tipIndex + 1) % TipCount;
                factIndex = (factIndex + 1) % FactCount;
                tipT = CardFade;
                factT = CardSeconds * 0.5f;
                RefreshTexts();
            }
            if (progress01 < target - 0.25f) shown = progress01;   // a new load started while visible
            target = progress01;
            if (!ReferenceEquals(text, lastText)) { lastText = text; status.text = text ?? ""; }
            EnsureScene();
        }

        public void Hide()
        {
            if (root.style.display.value == DisplayStyle.None) return;
            if (fadeOut < 0f) fadeOut = 0f;
        }

        /// Debug / AutoShot: switch both cards to the next tip / fact now.
        public void DebugNextCards()
        {
            tipT = CardSeconds;
            factT = CardSeconds;
        }

        /// Key placeholders in tips → the player's current bindings (only when a tip is shown).
        static string WithKeys(string s)
        {
            if (s == null || s.IndexOf('{') < 0) return s;
            return s.Replace("{R1}", GameInput.BindingLabel(GameAction.RightSlot1))
                    .Replace("{R3}", GameInput.BindingLabel(GameAction.RightSlot3))
                    .Replace("{L1}", GameInput.BindingLabel(GameAction.LeftSlot1))
                    .Replace("{L3}", GameInput.BindingLabel(GameAction.LeftSlot3))
                    .Replace("{S8}", GameInput.BindingLabel(GameAction.Slot8))
                    .Replace("{INV}", GameInput.BindingLabel(GameAction.Inventory));
        }

        public void RefreshTexts()
        {
            int t = ((tipIndex % TipCount) + TipCount) % TipCount;
            tipCard.Set(Loc.Tr("TIPP", "TIP"), WithKeys(Loc.Tr(Tips[t, 0], Tips[t, 1])), TipIcons[t % TipIcons.Length]);
            int f = ((factIndex % FactCount) + FactCount) % FactCount;
            factCard.Set(Loc.Tr("WUSSTEST DU?", "DID YOU KNOW?"), Loc.Tr(Facts[f, 0], Facts[f, 1]), FactIcons[f % FactIcons.Length]);
        }

        // ------------------------------------------------------------------ 3D scene lifetime
        void EnsureScene()
        {
            if (scene != null && scene.Alive) return;
            if (scene != null) ReleaseScene();
            if (sceneFailed || fadeOut >= 0f) return;
            scene = UILoadingScene.Create();
            if (scene == null || scene.Texture == null)
            {
                sceneFailed = true;
                if (scene != null) { scene.Dispose(); scene = null; }
                return;
            }
            sceneImage.style.backgroundImage = Background.FromRenderTexture(scene.Texture);
            sceneImage.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            sceneImage.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            sceneImage.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            sceneImage.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
        }

        void ReleaseScene()
        {
            sceneImage.style.backgroundImage = StyleKeyword.None;
            if (scene != null)
            {
                scene.Dispose();
                scene = null;
            }
        }

        // ------------------------------------------------------------------ frame
        public void Tick(float dt)
        {
            if (root.style.display.value == DisplayStyle.None) return;
            if (float.IsNaN(dt) || dt < 0f) dt = 0f;
            time += dt;
            if (fadeOut >= 0f)
            {
                fadeOut += dt;
                float a = 1f - Mathf.Clamp01(fadeOut / FadeOutSeconds);
                root.style.opacity = a;
                if (a <= 0f)
                {
                    root.style.display = DisplayStyle.None;
                    fadeOut = -1f;
                    ReleaseScene();   // nothing of the reef survives the loading screen
                    return;
                }
            }

            if (scene != null)
            {
                if (!scene.Alive) ReleaseScene();
                else scene.Tick(dt);
            }
            if (scene == null) EnsureScene();
            flat.SetVisible(scene == null);
            if (scene == null) flat.Tick(dt, time);

            // progress: ease toward the reported value, the fish rides the front of the fill
            shown = shown > target ? target : Mathf.Lerp(shown, target, 1f - Mathf.Exp(-5f * dt));
            if (Mathf.Abs(shown - lastFill) > 0.0005f)
            {
                lastFill = shown;
                trackFill.style.width = Length.Percent(shown * 100f);
                rider.style.left = Length.Percent(shown * 100f);
            }
            rider.style.translate = new Translate(0f, Mathf.Sin(time * 4.2f) * 2.5f);
            rider.style.rotate = new Rotate(Angle.Degrees(Mathf.Sin(time * 4.2f + 0.8f) * 7f));
            int pc = Mathf.Clamp(Mathf.RoundToInt(shown * 100f), 0, 100);
            if (pc != lastPercent) { lastPercent = pc; percent.text = UIFormat.Percent(pc / 100f); }

            // rotating cards (staggered by half a period)
            tipT += dt;
            if (tipT >= CardSeconds)
            {
                tipT = Mathf.Repeat(tipT - CardSeconds, CardSeconds);
                tipIndex = (tipIndex + 1) % TipCount;
                RefreshTexts();
            }
            factT += dt;
            if (factT >= CardSeconds)
            {
                factT = Mathf.Repeat(factT - CardSeconds, CardSeconds);
                factIndex = (factIndex + 1) % FactCount;
                RefreshTexts();
            }
            tipCard.Animate(Envelope(tipT), time);
            factCard.Animate(Envelope(factT), time + 1.3f);

            // logo: gentle bob, bubbles rising out of the letters
            logoHost.style.translate = new Translate(0f, Mathf.Sin(time * 0.9f) * 4f);
            for (int i = 0; i < logoBubbles.Length; i++)
            {
                float ph = Mathf.Repeat(time / 3.4f + i * 0.17f + (i & 1) * 0.43f, 1f);
                var b = logoBubbles[i];
                b.style.top = 104f - ph * 128f;
                b.style.translate = new Translate(Mathf.Sin(time * 2.1f + i * 1.3f) * 5f, 0f);
                b.style.opacity = Mathf.Clamp01(ph / 0.15f) * Mathf.Clamp01((1f - ph) / 0.35f);
            }
        }

        static float Envelope(float t) => Mathf.Clamp01(Mathf.Min(t / CardFade, (CardSeconds - t) / CardFade));

        // ------------------------------------------------------------------ pieces
        /// Tip / fact card: round icon disc (white glyph) + caption + up to ~3 lines of outlined text. No panel.
        sealed class Card
        {
            public readonly VisualElement Root;
            readonly VisualElement disc;
            readonly UIGlyph icon;
            readonly Label caption, body;

            public Card(Color discColor, Color captionColor, bool mirrored)
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore };
                Root.style.flexDirection = mirrored ? FlexDirection.RowReverse : FlexDirection.Row;
                Root.style.alignItems = Align.Center;
                Root.style.flexGrow = 1;
                Root.style.flexShrink = 1;
                Root.style.flexBasis = 0f;
                Root.style.maxWidth = 600f;
                Root.style.minWidth = 0f;
                Root.style.minHeight = 96f;

                disc = new VisualElement { pickingMode = PickingMode.Ignore };
                UIKit.Size(disc, 80f, 80f);
                UIKit.Radius(disc, 40f);
                UIKit.Border(disc, 3f, new Color(1f, 1f, 1f, 0.92f));
                disc.style.backgroundColor = discColor;
                disc.style.alignItems = Align.Center;
                disc.style.justifyContent = Justify.Center;
                icon = new UIGlyph(UIGlyphs.LoadingBulb, Color.white, 48f);
                disc.Add(icon);
                Root.Add(disc);

                var col = UIKit.Col();
                col.pickingMode = PickingMode.Ignore;
                col.style.flexGrow = 1;
                col.style.flexShrink = 1;
                col.style.minWidth = 0f;
                col.style.alignItems = mirrored ? Align.FlexEnd : Align.FlexStart;
                if (mirrored) col.style.marginRight = 18f;
                else col.style.marginLeft = 18f;
                caption = UIKit.HudText("", 17f, captionColor, true);
                caption.style.letterSpacing = 1.6f;
                col.Add(caption);
                body = UIKit.HudText("", 22f, Color.white, false);
                body.style.whiteSpace = WhiteSpace.Normal;
                body.style.marginTop = 3f;
                body.style.unityTextAlign = mirrored ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
                col.Add(body);
                Root.Add(col);
            }

            public string Text => body.text;

            public void Set(string cap, string text, UIGlyph.DrawFn draw)
            {
                caption.text = cap ?? "";
                body.text = text ?? "";
                if (draw != null) icon.SetDraw(draw);
            }

            /// a = 0..1 card visibility (fade + small rise); the icon wobbles a little.
            public void Animate(float a, float t)
            {
                Root.style.opacity = a;
                Root.style.translate = new Translate(0f, (1f - a) * 12f);
                icon.style.rotate = new Rotate(Angle.Degrees(Mathf.Sin(t * 1.5f) * 6f));
                float s = 1f + (1f - a) * 0.15f;
                disc.style.scale = new Scale(new Vector2(s, s));
            }
        }

        /// The flat fallback background (no 3D): slanted light bands, rising bubbles, a fish crossing every ~4 s.
        sealed class FlatBackdrop
        {
            readonly VisualElement layer;
            readonly VisualElement fish;
            readonly VisualElement[] bubbles = new VisualElement[18];
            readonly float[] seed = new float[18];
            readonly VisualElement[] bands = new VisualElement[4];
            bool visible = true;

            public FlatBackdrop(VisualElement parent)
            {
                layer = new VisualElement { pickingMode = PickingMode.Ignore };
                UIKit.FillParent(layer);
                parent.Add(layer);

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
                    layer.Add(b);
                    bands[i] = b;
                }

                fish = new UIGlyph(UIGlyphs.Fish, new Color(0.06f, 0.24f, 0.36f, 0.45f), 120f);
                fish.style.position = Position.Absolute;
                fish.style.top = Length.Percent(45f);
                layer.Add(fish);

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
                    seed[i] = i * 0.618f % 1f;
                    layer.Add(b);
                    bubbles[i] = b;
                }
            }

            public void SetVisible(bool on)
            {
                if (on == visible) return;
                visible = on;
                layer.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            }

            public void Tick(float dt, float time)
            {
                for (int i = 0; i < bubbles.Length; i++)
                {
                    float speed = 0.06f + seed[i] * 0.08f;
                    float y = Mathf.Repeat(seed[i] + time * speed, 1f);
                    bubbles[i].style.top = Length.Percent(105f - y * 115f);
                    bubbles[i].style.translate = new Translate(Mathf.Sin(time * (0.8f + seed[i]) + i) * 14f, 0f);
                }
                for (int i = 0; i < bands.Length; i++)
                    bands[i].style.translate = new Translate(Mathf.Sin(time * 0.6f + i * 1.7f) * 40f, 0f);

                const float cross = 4f;
                float f = Mathf.Repeat(time, cross) / cross;
                bool leftToRight = Mathf.FloorToInt(time / cross) % 2 == 0;
                float x = leftToRight ? Mathf.Lerp(-10f, 105f, f) : Mathf.Lerp(105f, -10f, f);
                fish.style.left = Length.Percent(x);
                fish.style.top = Length.Percent(42f + Mathf.Sin(time * 1.7f) * 3f + (leftToRight ? 0f : 10f));
                // the glyph faces left (head on the left) – mirror it when swimming right
                fish.style.scale = new Scale(new Vector2(leftToRight ? -1f : 1f, 1f));
            }
        }
    }
}
