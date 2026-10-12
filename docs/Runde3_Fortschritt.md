# Runde 3 (Testversion 5) – Übergabe-Log

Jeder Agent liest dieses Log zuerst und hängt am Ende seinen eigenen Abschnitt an.

## Plan (Reihenfolge)
1. Core: Rucksack-Inventar + große Hotbar, Überleben (Hunger/Durst, Wasserfilter, Kochen), Waffen-Daten, Kartenstücke, Galerie, Wetter, Kamera-Regeln
2. Spieler + Figuren: Schlafen in Hängematte/Bett, Leiter, Kollisionen, Figuren/Hände/Gegenstände, Anpassung, Stock/Speer-Kampf
3. Welt + Karte: Insel-Umbau, Schilder, Surfbretter, Badeinsel, Dorf-Häuser weg → verstecktes Einsiedler-Lager, individuelle Inseln (Vulkan), Wolken, Haus innen
4. Tiere + Meer-Inhalte: viel mehr Fische überall, Fluchtverhalten, Urzeit-Tiere, Haie/Monster + Abwehr, Aktivitäten, Memes
5. NPCs + Story: Einsiedler (Stimmung, Zeichensprache, Angriff, Speer schärfen), Nebenaufgaben-Bugs, Story, Tauchplatz-Freischaltung
6. Handy + UI + Boot: Karte zoom/pan, Shop-Bilder, FinTube-Studio, Ladebildschirm, Wetter, Anker nur Fahrer, zweiter Sitz
6b. Spielstände + Einladen (GDD 19b): 4 Spielstand-Slots (löschen, Karte mit Spielzeit/Erfolgen je Welt), Erfolge/Fotos je Spielstand, Fotos nur im Spiel, mehrfach einladen, nur Host lädt ein
7. Integration + Build + Tests + Testversion 5 + Bericht

---

## Schritt 1 – Core-Systeme + UI (Agent „core“, Compile-Tag `r3core`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK**. Unity wurde nicht gestartet (kein Playtest gelaufen – die neuen
AutoPlaytest-Schritte 50–55 und der MP-Schritt 11h müssen in Schritt 7 laufen).

### Was neu ist (GDD §19)
1. **Rucksack** (`Core/Backpack.cs`, Katalog `Data/ItemDatabase.cs`): pro Spieler `GameState.backpack` (24 Plätze,
   Stapel mit Anzahl, Haltbarkeit, Schärfe) + `GameState.pouch` (Sammeltasche ohne Platzlimit: Muscheln, Logbuchseiten,
   Flaschenpost, Fundstücke, Kartenstücke – die Kopie des Finders; gezählt wird weiter geteilt in `Collectibles`).
   API: `Backpack.Add/Remove/Count/Has/First/SpaceFor/Move/Drop/Use/UseId/AddStack`, Event `Backpack.Changed`.
   Alles Aufgesammelte landet drin: Sammelobjekte (`PropPickup`), Kokosnüsse (Palme, Heli-Missgeschick), Kisten-Snacks
   und gekaufte Speere (der **Öffner** der Kiste bekommt sie; was nicht passt: Snacks in den Kühlschrank, Speere auf den
   Boden), Kühlschrank-Inhalt (E am Kühlschrank nimmt alles in den Rucksack), Steine/Treibholz/Obst (`PropLoot`, täglich
   geteilt über `Loot` + `CoreCommands.LootKey`), Kartenstücke, Fisch aus der Kühlbox (Rucksack-Tab „Kühlbox“ →
   „Zum Braten“ = roher Fisch, `Pantry.TakeFishToBackpack`, Host-Kommando `TakeFishKey`). Ausrüstung (Flaschen, Flossen,
   Kameras, Lampen, Angeln) bleibt Crew-Besitz (`ownedItems`) und steht im Tab „Ausrüstung“; Kamera/Lampe/Angel sind
   die Pseudo-Items `"Camera"`/`"Flashlight"`/`"FishingRod"`. Startausrüstung jedes Spielers: Stock + leere Flasche.
2. **Eine Hotbar mit 8 Plätzen** (`Core/Hands.cs` neu geschrieben, `UI/UIHotbar.cs`): Tasten 1–8 (`GameInput.SlotAction(i)`,
   die alten Aktionen RightSlot1..3/LeftSlot1..3 = Plätze 1–6, neu `Slot7`/`Slot8`), Mausrad = Haupthand, Shift+Rad =
   Zweithand. Jeder Platz gehört zur **Haupthand** oder **Zweithand** (`GameState.hotbarOffMask`; Rechtsklick im Rucksack
   schaltet um; Gegenstand auf die Hand-Felder ziehen = sofort halten). Beidhändig: Angel, Harpune. `HoldItem` erweitert
   (APPEND): `Stick, Spear, SpearGun, Food, Tool` – Avatar-Bytes ok (`Hands.LastItem`), `DiverPuppet` zeigt
   Stock/Speer/Harpune/Essen/Werkzeug an Koop-Avataren. Hand.Right = Haupthand, Hand.Left = Zweithand.
   Neue Hands-API: `SlotId/SlotItem/SlotHand/SlotUsable(int)`, `ActiveId(Hand)`, `Select(int)`, `Assign(int, id, hand?)`,
   `Swap`, `SetSlotHand`, `Hold(id, hand)`, `Find(HoldItem, out int)`, `MainHandTakesUse`. Die alten `(Hand, int)`-
   Überladungen von Select/Assign/SlotItem sind **weg** (alle Aufrufer umgestellt).
   **Save-Migration:** `GameState.CurrentVersion = 5`; alte 3+3-Leiste → rechts 1–3 = Plätze 1–3 (Haupthand), links 1–3 =
   Plätze 4–6 (Zweithand), der Stock kommt auf den ersten freien Haupthand-Platz; alte Funde → Sammeltasche. Koop: Hotbar,
   Rucksack, Hunger/Durst stehen im `PlayerRecord` (Host-Save) und in `coop_player.json`.
3. **Rucksack-Bildschirm** (`UI/UIInventory.cs` neu, Taste I): Hände, Leiste, 24er-Raster (Ziehen zum Sortieren,
   Rechtsklick = benutzen/essen, zeigen + 1–8 = auf die Leiste), rechts Tabs Gegenstand (Werte, Essen/Trinken/Benutzen,
   Haupt-/Zweithand, Wegwerfen), Ausrüstung, Kühlbox, Sammlung (+ **Schatzkarte** `UITreasureMapView`).
4. **Überleben** (`Core/Survival.cs`, HUD `UI/UISurvivalPanel.cs` = Keule + Tropfen + Herz bei Treffern): Hunger 36,
   Durst 24 Spielstunden von voll bis leer (im Wasser Hunger ×1,3; Entspannt ×0,6, Schwer ×1,25), eine Nacht −15/−20.
   Unter 25: langsamer + weniger Luft (über `Buffs.Multiplier(AirCapacity/SwimSpeed)` und das Gehen in `MoveLand`,
   min. ×0,8), nie Tod. Pro Spieler. Essen/Trinken: Rucksack/Hotbar (LMB mit Essen in der Hand), nicht unter Wasser.
   Jede `FoodDef` hat jetzt `hunger`/`thirst`; neu: `grilled_meat`, `mango`, `banana`, `water_bottle` (Heli, 3er-Pack),
   `fresh_water` (gefilterte Flasche). Rohes (`raw_fish`, `raw_meat`) muss gebraten werden.
   **Wasser:** leere Flasche im Wasser / am Steg / auf dem Boot benutzen → Meerwasser → **Wasserfilter in der Hütte**
   (`PropWaterFilter`, 45 Spielminuten, pro Spieler `GameState.filterJobs`) → Trinkwasser → leere Flasche zurück.
   **Kochen:** `PropCampfire` an festen Stellen (2 Treibholz bauen → brennt 3 Spielstunden, geteilt: `GameState.campfires`,
   `CoreCommands.CampfireKey`) und der bisherige Grill (`PropGrill`: brät zuerst Rohes aus dem Rucksack, sonst den
   billigsten Kühlbox-Fisch – das Ergebnis kommt in den **Rucksack** und wird nicht mehr sofort gegessen).
5. **Waffen + Kampf** (`Core/Combat.cs`, Spieler `Player/PlayerTools.cs`, Modelle in `PlayerHands`): Stock (Start; an Land
   Schubser mit Knockback/Stun, unter Wasser halb so stark), Holzspeer 150 $ / Stahlspeer 900 $ / Harpune 4.000 $
   (beidhändig, nur unter Wasser, 7 m) im Chopper-Shop-Tab „Speere“ (`Shop.BuyItem`, Lieferung `item:<id>`), Haltbarkeit
   (−1 pro Treffer, zerbricht), Schärfe (Schaden × (0,6 + 0,8 × Schärfe)). RMB mit Waffe = Abwehr. **Schleifgerät** in der
   Hütte (`PropGrinder`, 1 Schleifstein = +25 %) – erst nach `StoryKeys.GrindLearned` („hermit_grind“).
   API für Fauna/NPCs: `IHittable` (OnHit(HitInfo) auf dem Ziel-Objekt oder einem Parent des Colliders),
   `Combat.HurtLocal(amount, from, source)` (Puste 0–100, Abwehr zieht ab, bei 0 Knockback + unter Wasser Luftverlust,
   nie Tod), Events `Combat.HitLanded/LocalHurt/LocalKnockedOut`, `GameEvents.Fight` → Livestream-Hype (StreamService
   zählt es wie einen Rempler).
6. **Kartenstücke** (`Core/MapPieces.cs`, Props `PropMapPiece`/`PropDigSpot`/`PropTreasureDig`): 8 Stücke
   (Collectible-Art `MapPiece`, ids `map:p01…`), 4 liegen, 4 vergraben (E – Graben, mit Stock/Speer 2,5 s, mit Händen 5 s).
   Alle gefunden → Flag `treasure_map`, Banner, Wegpunkt, Karte im Rucksack („Sammlung“); am X `PropTreasureDig`
   (Flag `treasure_found`, +500 $). Platzhalter-Orte in `PropSurvival.BuildIsland`.
7. **Galerie**: Bilder bleiben im Datenordner des Spiels; die Handy-Galerie hat **Exportieren** (ein Bild) und **Alle
   exportieren** → `Bilder/SEALEBRITIES` (`Gallery.Export/ExportAll`; Tests leiten mit `Gallery.ExportFolderOverride` um).
   Der „Ordner“-Knopf ist weg.
8. **Kamera-Regeln**: keine automatische Aufnahme mehr (`FilmHandCamera.AutoRecordEnabled = false`, Einstellung entfernt),
   Aufnahme nur per LMB; die Zielrahmen (vier Ecken pro Tier) nur noch im Foto-Bereich
   (`FilmShotEvaluator.InPhotoRange`: Sealog-groß oder ≥ 75 % davon und ≤ 14 m); „?“ für unbekannte Arten bleibt.
   Kamera in der Zweithand + Waffe/Essen in der Haupthand: LMB gehört der Haupthand.
9. **Wetter** (`Core/Weather.cs`, App `UI/UIAppWeather.cs`): Wetter pro Tag aus dem Save-Seed (im Koop gleich),
   Lufttemperatur nach Uhrzeit/Wetter, Wasser 0/20/40 m, Stundenleiste, morgen. `Weather.Cloudiness/Wind01` sind für
   Wolken/Wellen nutzbar.

### Geänderte / neue Dateien
Neu: `Core/Backpack.cs` (+ `Loot`), `Core/Survival.cs`, `Core/Combat.cs`, `Core/MapPieces.cs`, `Core/Weather.cs`,
`Data/ItemDatabase.cs`, `Player/PlayerTools.cs`, `UI/UIGlyphsItems.cs`, `UI/UISurvivalPanel.cs`,
`World/Props/PropSurvival.cs`, `Debug/AutoPlaytest.Round3.cs`, `Debug/AutoPlaytestMP.Round3Core.cs`.
Neu geschrieben: `Core/Hands.cs`, `UI/UIHotbar.cs`, `UI/UIInventory.cs`, `UI/UIAppWeather.cs`.
Geändert: `Core/GameState.cs` (v5), `GameManager.cs`, `PlayerRecords.cs`, `GameInput.cs`, `Buffs.cs`, `Pantry.cs`,
`Shop.cs`, `Collectibles.cs`, `CoreCommands.cs`, `GameEvents.cs`, `StoryFlags.cs`, `Gallery.cs`, `Data/FoodDatabase.cs`,
`Data/GearDatabase.cs` (ShopCategory.Weapons), `Data/DialogueDatabase.cs`, `Player/PlayerHands.cs`, `DiverPuppet.cs`,
`PlayerController.Gear/Movement/Ladders/Selfie.cs`, `PlayerFlashlight.cs`, `PlayerFactory.cs`,
`Filming/FilmHandCamera.cs`, `FilmShotEvaluator.cs`, `TutorialQuests.cs`, `Boat/FishingSpot.cs`, `HeliCrate.cs`,
`Livestream/StreamService.cs`, `LivestreamSelfTest.cs`, `Net/NetState.cs`, `Net/Sync/NetAvatarSync.cs`, `NetCrew.cs`,
`UI/UIHud.cs`, `UIAppShop.cs`, `UIAppGallery.cs`, `UIGlyphs.cs`, `UIWidgets.cs`, `UILoadingScreen.cs`,
`UISettingsView.cs`, `UIController.cs`, `World/Props/PropInteractables.cs`, `PropInteract.cs`, `PropHut.cs`,
`IslandProps.cs`, Tests (`Debug/*`).

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3only` → Menü, frisches Spiel, Schritte **50–55** (Rucksack/Hotbar/
  Migration, Überleben/Filter/Lagerfeuer/Grill, Waffen/Abwehr/Schleifen, Kartenstücke/Schatz, Galerie-Export +
  Kamera-Regeln, Wetter + Koop-Record), Screenshots `50_*` … `55_*`. Im vollen Lauf hängen sie hinter den
  Runde-2-Schritten (Zeitbudget 22,5 min beachten – lieber `-r3only`).
- Schritt 12 prüft jetzt: keine Auto-Aufnahme, LMB startet. Alle Testschritte starten satt (`Survival.Set(100, 100)`).
- Koop: `AutoPlaytestMP` Schritt **11h** (eigene Rucksäcke + Hunger, geteiltes Lagerfeuer/Kartenstück/Tages-Fund,
  Avatar hält den Stock). Schritt 11f erwartet den Grillfisch jetzt im Rucksack.

### Offen / für die nächsten Agenten
- **Schritt 2 (Spieler):** Hände/Gegenstände schöner machen – `PlayerHands` hat die neuen Modi `Stick/Spear/Gun/Hold`
  (einfache Modelle, Schwung über `PlayerHands.Swing`), Avatare in `DiverPuppet.UpdateRound3Items`. Kampf-Feinschliff
  (Treffer-Feedback, Stun-Animation) gern dort. `PlayerController.Knockback` existiert.
- **Schritt 3 (Welt):** alle Platzierungen in `PropSurvival` sind Platzhalter: Wasserfilter + Schleifgerät in der Hütte
  (`PropSurvival.BuildHutStations`, aufgerufen aus `PropHut.BuildInterior`), 3 Lagerfeuerstellen, Treibholz/Steine/Obst,
  8 Kartenstücke, das Schatz-X. Eigene Orte: `MapPieces.UseDefaultPlacement = false` vor dem Bau setzen und
  `PropSurvival.CreatePiece/CreateCampfire/CreateLoot` aufrufen, `MapPieces.SecretSpot` (+ `SecretSpotValid`) setzen.
  Das Dorf (Kiosk) entfällt – der Kiosk-Fallback legt Gekauftes in den Rucksack.
- **Schritt 4 (Fauna):** Haie/Monster greifen über `Combat.HurtLocal` an; treffbare Tiere bekommen `IHittable`
  (Knockback/Stun/Fliehen aus `HitInfo`). Auf Koop-Gästen sind Tiere Proxys → Treffer zum Host weiterleiten (Vorschlag:
  `CoreCommands.RegisterCustom(Combat.HitKey, …)`). Speerfischen könnte `raw_fish` in den Rucksack legen.
- **Schritt 5 (NPC):** Die Einsiedler setzen `StoryKeys.GrindLearned` (Schleifen lernen); Einsiedler-Angriffe über
  `Combat.HurtLocal`, Einsiedler mit `IHittable`. Fleisch (`raw_meat`) könnte es als Tausch geben.
- **Schritt 6 (UI):** Die Wetter-App ist funktional (Feinschliff gern); der Chopper-Shop hat den Tab „Speere“ (Bilder dort);
  die Ladebildschirm-Tipps sind an die 8er-Leiste/Überleben angepasst. `UIInventory` ist groß – Feinschliff möglich.
- Bekannt/ungetestet: Unity lief nicht – Layout des Rucksack-Fensters (1480 px), Halte-Posen der neuen Gegenstände und
  die Platzhalter-Orte (Lagerfeuer am Strand, Filter an der Ostwand der Hütte) bitte per Screenshot prüfen.

---

## Schritt 2 – Spieler + Figuren (Agent „player“, Compile-Tag `r3player`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK**. Unity wurde nicht gestartet – die neuen AutoPlaytest-Schritte
**56–59**, der MP-Check in **S08** und die AutoShot-Bilder **`r3_*`** müssen in Schritt 7 laufen (Geometrie ist gerechnet,
nicht gesehen).

### Was neu ist (GDD §19)
1. **Schlafen IN Hängematte / Bett** (Bilder 4/5: der Avatar lag neben der Hängematte bzw. auf dem Boden).
   Ursache: beim Hinlegen blieb die Spieler-Wurzel stehen (nur die Kamera legte sich hin), der Koop-Avatar wurde über eine
   Näherungs-Rechnung „hingeschnappt“, und die Liege-Pose hatte die Unterarme senkrecht nach oben.
   Jetzt: `SleepSpotInfo` hat `hip` (Punkt unter dem Becken auf Matratze/Tuch), `headRise`, `feetRise`; registriert über
   **`PropBed.RegisterBed(id, pillowTop, pillowLength, feetDir, mattressTop, stand, standYaw)`** und
   **`PropBed.RegisterHammock(id, hookKopf, hookFüße, sag, stand, standYaw, pillow)`** (Hütten-Bett, Sofa-Koje, beide
   Veranda-Hängematten, alle `PropKit.HammockBed`; das alte `Register` geht weiter, die Pose wird dann geschätzt).
   Beim Hinlegen legt `PlayerController` die **Wurzel auf den Beckenpunkt** (CharacterController aus), die Kamera ist das
   Auge der Liege-Pose (`DiverPuppet.LieEye`, Blick nach oben Richtung Füße, Maus ±60°), Aufstehen (Bewegen/Springen/E)
   stellt einen an den `stand`-Punkt. Solo: E legt einen während der Schlaf-Blende hin (`GameEvents.SleepStarted`;
   `PropInfo.LocalSleepSpot` wird jetzt auch solo gesetzt); Koop: bei n/n bleibt man durch die Blende liegen. Der Avatar
   (`NetAvatar`) schnappt über `DiverPuppet.LiePose` auf den Platz und bekommt `SetLiePose` (Hängematte: Oberkörper und
   Beine folgen der Kurve, Knie leicht gebeugt, sanftes Schaukeln; Bett: flach, Kopf auf dem Kissen; Arme seitlich,
   Tank/BCD und Flossen abgelegt). `PropBed` folgt der verschobenen Wurzel (kein sofortiges „Aufstehen“ mehr).
   Test-Hook: `PlayerController.DebugLie(spotIndex)` (−1 = aufstehen), `LyingSpotIndex`.
2. **Leitern:** Der Steg-Leiter-Eintrag in `PropInfo.Ladders` hatte als Fuß `DockLadderBottom` (0,45 m seewärts der
   Holme) → man hing 0,85 m vor den Sprossen, die Hände griffen ins Leere. Jetzt Fuß = Holm-Linie (`PropDock`). Die alte
   geskriptete Steg-Kletterei (lief von selbst hoch) ist ersetzt: Leertaste/E neben der Steg-Leiter greift sie auch ohne
   hinzuschauen mit dem normalen Klettern (W/Leertaste halten = hoch, S = runter, oben 0,55 s Absteigen auf die Planken).
   Von oben einsteigen: erst über die Kante hinaus, dann absenken (schnitt vorher durch Planken/Geländer); Greifen aus dem
   Wasser gleitet je nach Abstand 0,22–0,5 s heran. Avatar: abwechselnd Hände UND Füße auf den Sprossen, Körper an der Leiter.
3. **Kollisionen (Spielerseite):** CharacterController geprüft (Radius 0,32, Skin 0,05, Stufe 0,4, Hang 50° – passen),
   `enableOverlapRecovery` an; neu `PlayerController.Collision.cs`: jedes Frame wird man aus konvexen Collidern (Boot-Rumpf,
   Kisten, Türen, Props) herausgeschoben, in die man durch Teleports/Klettern geriet oder die sich in einen hinein bewegt
   haben (ein CharacterController kollidiert nur, wenn er selbst läuft). Tauchen/Schwimmen nutzen weiter `cc.Move`.
   **Fehlende Prop-Collider für Schritt 3** (Audit, Datei:Zeile ungefähr):
   - **Korallen/Riffsteine ohne Collider:** `PropReefs.cs` `Patch()` 384–508 (Basisfels bis 3,5 m, Kolonien),
     `BommieGarden()` 511–533, lose Steine 709–718; `PropCoral.cs` `PropReef.Place` 259 (Drop-off/Wrack/Kelp) – ~400 Patches.
   - **Palmen:** Kapsel nur senkrecht am Fuß (`PropJungle.cs:170`, `PropDetails.cs:453`), der Stamm neigt sich 0,4–0,9 m →
     man läuft durch den Stamm.
   - **Kleine Felsen:** `PropJungle.BuildRocks` nur s>1,2 (Z. 505/537), `PropKit.RockCluster` nur s>0,6 (550),
     `PropKelp.cs:92` nur s>1,6 (+90 Steine 104–113), `PropDeep.Formation` nur s>1,8 (179), Basalt 277–283,
     `PropMiniIslands.cs:194` nur s>1,1, `PropPOI.cs` 118–124/286–293 (Wasserfall-Becken).
   - **Bojen nur Trigger / ohne:** Tauchplatz-Boje `UnderwaterDecor.cs:84–151`; `PropKit.MooringBuoy` 635–652.
   - **Wrack MS Clickbait:** Schornstein 171, Kran-Sockel 188 + Ausleger 193, Fockmast 179, Lüfter/Winde 217–224,
     Propeller 243–249, Rettungsboot 366, Fässer 354; `PropWreckInterior.cs` Tür 211–218; Wal-Skelett `PropDeep.cs` 212–216.
   - **Wandas Hubschrauber** (`HeliChopper.cs` ~210, `NpcVehicles.cs:292`); Yacht/Katamaran-Collider zu niedrig
     (`NpcVehicles.cs` 215/237).
   - **Dorf/Steg:** Stelzen + Steg-Pfosten `PropVillage.cs` 574/631/645, Kanus 532/292, Laternenpfosten `PropDock.cs:293`,
     Poller 115–122; Lagune: Sandbänke `PropDetails.cs:327`, Ruderboot-Wracks 342, versunkener Steg 363–365.
   - **Nur Trigger (solide aussehend):** Grill `PropBeach.cs` 405, Lagerfeuer + Schatzkiste `PropSurvival.cs` 253/351.
   - **Hütte:** Nachttisch-Kiste neben dem Bett (`PropHut.cs:514`, `Crate()` 720 ohne Collider).
   - Weitere: Baumhaus-Stämme auf Kopfhöhe `PropDetails.cs:268`, Radioturm-Gitter nur Beine `PropPOI2.cs` 474–481,
     Tonis Hütte nur Rückwand `PropIslandExtras.cs` 241–256.
4. **Figuren schöner / „realistischer“** (`DiverBuilder.cs` neu): Bohnen-MENSCH statt Kugelbohne – 1,85 m, hoher Eier-Kopf
   auf kurzem Hals mit Anzug-Kragen, Oberkörper mit Schultern (Becken + Brust auf Wirbel-Gelenk, elliptischer Querschnitt),
   konische Arme/Beine mit Ellbogen und Knien, Fäustlinge mit Daumen, lesbares Gesicht (Augen mit Lidern, Brauen in
   Haarfarbe, Nase, Ohren, Mund), Haare unter jedem Hut, Bleigurt, Rücken-Reißverschluss, BCD mit Schultergurten,
   Schulterkappen in Anzugfarbe. Glatte Normalen, LOD1/LOD2 wie bisher (`DiverLod`). Mit Hut sitzt die Maske an Land um
   den Hals (Gesicht bleibt sichtbar), ohne Hut auf der Stirn, im Wasser über den Augen. Gilt für Koop-Avatare, Menü-Crew,
   Selfie und Vorschau (NPCs nutzen den eigenen `NpcBuilder` – nicht geändert).
   Rig: Wurzel → `Rig` (Größen-Skalierung) → `Body` (Becken, 0,98 m) → Beine | `Chest` (1,10 m) → Hals/Kopf/Arme/Ausrüstung.
   `DiverPuppet`: neue Gehen/Rennen-Animation (Knie beugt in der Schwungphase, Ferse beim Rennen, Fußabrollen, Becken-Drehung
   + gegenläufige Schultern, Kopf stabilisiert, Pumparme), Schwimmen (Kraulbeinschlag mit gestreckten Flossen, Delfin beim
   Sprint, Froschbeinschlag an der Oberfläche), Sitzen an die neuen Maße angepasst (Hintern bei Wurzel + 0,36 m wie vorher).
5. **Anpassung** (Charakter-Bildschirm, neuer Tab **„Körper“**): Statur (4), Größe (5 Stufen ±8 %), 10 Hauttöne,
   9 Frisuren, 10 Haarfarben, 6 Bärte; „Gesicht“: 6 Augen (+Wimpern), 5 Nasen, 5 Münder, 6 Brauen (**0 = jetzt
   „Natürlich“**, „Keine“ = 5); „Hut & Extras“: 20 Hüte (+Mütze, Basecap, Stirnband, Kopftuch, Sonnenschild),
   8 Extras (Muschelkette, Halstuch, Ohrringe, Blume, Taucheruhr, Sommersprossen, Pflaster); 18 Anzugfarben, 12 Muster
   (+Tarnung, Zickzack, Sterne, Zweifarbig), 10 Maskenrahmen, 8 Flossenfarben. Live-3D-Vorschau, Icons für alles.
   `DiverLook` + `SettingsData` + `NetLookCodec` haben `build/height/hair/hairColor/beard/nose/accessory` (0 = Standard,
   alte Dateien bleiben gültig); **`Net.ProtocolVersion = 3`** (Look-Bytes erweitert). Die Look-Hashes (Lobby-Vorschau,
   Selfie, Menü-Crew) nutzen jetzt `NetLookCodec.Hash`.
6. **Hände + Gegenstände** (`PlayerHands`): Fäustling mit zwei Finger-Gliedern, die sich um Griffe schließen (offen für
   Essen/Handy, Faust um Stock/Speer/Lampe/Harpune/Leiter); neue Modelle: knorriger Stock (3 Segmente, Knoten, Zweig mit
   Blatt), Holzspeer (feuergehärtete Spitze + Bindung) / Stahlspeer (Stahlblatt + Kragen) je nach Item, Harpune (Rohr,
   Mündung, 2 Gummis, Griff + Abzug, geladener Pfeil), Flaschen aufrecht in der Faust. Posen: Stock ruht schräg,
   LMB = Ausholen über die Schulter + diagonaler Schlag mit Ausfallschritt; Speer tief an der Hüfte, LMB = Zurückziehen +
   0,36 m Stoß; Harpune **beidhändig** (zweite Hand am Lauf, Modus `GunSupport`), RMB hebt sie mittig auf Augenhöhe,
   Schuss = Rückstoß, Pfeil weg und schiebt sich nach 0,6 s wieder ein; Trinken = Flasche zum Mund kippen.
   Avatare: neue Stock/Speer/Harpune/Essen/Flaschen-Modelle und Halte-Posen.
7. **Ego-Bewegung** (alles × Kopfwippen-Schalter): stärkeres, leicht rollendes Wippen beim Sprinten, Neigen beim
   Seitwärtsgehen, ruhiges Atmen im Stand, +3° Sichtfeld beim Sprinten; gehaltene Gegenstände senken sich beim Sprinten.

### Geänderte / neue Dateien
Neu: `Player/PlayerController.Collision.cs`, `Debug/AutoPlaytest.Round3Player.cs`, `Debug/AutoShot.Round3Player.cs`.
Neu geschrieben: `Player/DiverBuilder.cs`, `Player/DiverPuppet.cs`. Geändert: `Player/DiverGear.cs`, `DiverMeshes.cs`
(`TaperCapsule`, `TaperLimb`), `PlayerHands.cs`, `PlayerController.cs/.Poses.cs/.Ladders.cs/.Movement.cs/.Selfie.cs`,
`PlayerFactory.cs`, `PlayerMenuCamera.cs`; `Core/Net.cs` (DiverLook, ProtocolVersion 3), `Core/Settings.cs`,
`Data/CosmeticsDatabase.cs`; `Net/Sync/NetCrew.cs` (NetLookCodec), `Net/Sync/NetAvatar.cs` (Liege-Snap);
`World/Props/PropInfo.cs` (SleepSpotInfo), `PropBehaviours.cs` (PropBed), `PropHut.cs`, `PropKit.cs`, `PropDock.cs`;
`UI/UICharacterScreen.cs` (+Icons), `UI/UICharacterPreview.cs`; `Debug/AutoPlaytest.Round3.cs`, `AutoPlaytestMP.cs`,
`AutoShot.cs`; `docs/ArtBible_HowToFish.md` (§1.12 Hinweis).

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3only` → Schritte **56** (Liegen in Hängematte + Bett: Wurzel auf dem
  Beckenpunkt, Auge über der Fläche, Blick nach oben, Avatar-Pose ohne Durchsinken, Kopf am Kissenende, Aufstehen am
  Stand-Punkt; Bilder `56_sleep_fp_*`, `56_sleep_avatar_*`), **57** (Steg-Leiter aus dem Wasser, ohne hinzusehen, kein
  Auto-Klettern, kein Teleport, oben auf den Planken; `57_dock_ladder_fp`, `57_ladder_avatar`), **58** (alle neuen Optionen
  bauen, Dreiecke ≤ 9.000 je Option, schwerste Kombination ≤ 10.500, LODs; `58_characters_lineup/_faces`, `58_run_avatar`,
  `58_character_screen`), **59** (Stock-Schlag, Holz-/Stahlspeer + Stoß, Harpune beidhändig + Zielen, Flasche + Trinken;
  `59_hands_*`).
- Koop: `AutoPlaytestMP` S08 prüft jetzt, dass der Avatar des Gasts IM Bett/der Hängematte liegt (≤ 0,3 m vom Beckenpunkt).
- `-autoshot`: neue Bilder `r3_characters_lineup/_faces`, `r3_sleep_hammock/_bed`, `r3_ladder_avatar`, `r3_hands_*`.

### Offen / für die nächsten Agenten
- **Unbedingt per Screenshot prüfen (Schritt 7):** Hut-Sitz auf dem neuen Kopf (Hüte sind auf Kopfradius + Haar skaliert),
  Masken-Halsposition, Frisuren unter Hüten, Liege-Posen, Griff der zweiten Hand an der Harpune, Größe der Ego-Gegenstände
  (Kunstbibel ≤ 240 × 260 px). Alle Maße stehen als Konstanten oben in `DiverBuilder` / `DiverPuppet` / `PlayerHands`.
- **Schritt 3 (Welt):** Collider-Liste oben. Wer Betten/Hängematten neu baut: bitte `PropBed.RegisterBed` /
  `RegisterHammock` benutzen (dann liegen Spieler + Avatar exakt drin). Das Haus-Innere darf umgebaut werden.
- **Schritt 5 (NPC):** `NpcBuilder` (Einsiedler usw.) hat eigene, ältere Proportionen – gern an die neuen Maße angleichen
  (`DiverBuilder.ShapeOf`, Kopf-/Gesichtsmaße); schlafende NPCs in Hängematten können `DiverPuppet.LiePose` nutzen.
- Avatare zeigen keinen Schlag-/Stoß-Ablauf anderer Spieler (kein Netz-Ereignis dafür) – nur die Halte-Posen.
- Bekannt: Spieler mit alten Einstellungen bekommen „Kurz“-Haare und natürliche Brauen (vorher Glatze/keine Brauen).

---

## Schritt 3 – Welt + Karte (Agent „world“ + 4 Unter-Agenten, Compile-Tags `r3world`, `r3sky`, `r3hut`, `r3col`, `r3isle`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK** (Tag `r3world`). Unity wurde nicht gestartet – alle neuen Schritte
(**60–64** im AutoPlaytest `-r3only`, MP-Check Hütte) und die AutoShot-Bilder **`r3w_*`, `r3i_*`, `r3s_*`, `r3h_*`** müssen in
Schritt 7 laufen. Geometrie ist gerechnet (Terrain-Probe `scratchpad/r3world/probe`, kompiliert die echte
`WorldHeightField`), nicht gesehen.

### Was neu ist (GDD §19 „Insel und Welt“, „Fehlerhafte Objekte“, „Kollisionen“)
0. **Verbindliche Spieler-Antworten dieser Runde (vom Koordinator):** Insel klein + dicht (2–3 min zu Fuß quer, überall
   etwas zu entdecken); das Lager gehört **exzentrischen Aussteiger-Einsiedlern** (4–6, eigene Rollen), die das Meer
   verehren und Fremden misstrauen; Horst sitzt nicht mehr am Steg (Hängematte am Strand); Geheimnisse mittelschwer
   (klettern/tauchen, Hinweise, nicht von den Hauptwegen sichtbar); Story-Kern = Onkel Gus’ Erbe; die Vulkaninsel öffnet
   mit dem Wrack, Tiere = echte lebende Fossilien + ein großes Urzeit-Monster (tiefer Bereich reserviert). Alles umgesetzt.
1. **Karte / Insel (Agent world).** Die Anker (Hütte, Steg, Heli-Pad, Leuchtturm, POIs, Tauchplätze) bleiben, damit alle
   Systeme weiterlaufen (Routen-Probe: alle Wege wie vorher, max. 3 m > 32°; Insel ~240 m breit, 4,4 ha). Neu/umgebaut:
   - **Dorf „Altriff“ entfernt** (`PropVillage.BuildHouses = false`): kein Versammlungshaus, Kiosk, Bootsschuppen, Bühne,
     Terrassenhäuser, Stelzenhäuser, Gärten, Gehege mehr. Es bleiben der **alte Holzsteg** (zweiter Bootsanleger,
     Laternen, Helm-Schatz daneben; Spots `village_jetty`/`village_mooring`/`v_jetty*`/`v_lanterns`/`v_canoe_start`),
     zwei Kanus und ein Netzgestell auf dem offenen **Südstrand** (die Terrasse ist jetzt eine flache Strandwiese).
     Steg-Pfähle haben Collider. Karten-App: „Südstrand“ statt „Altriff“.
   - **Verstecktes Einsiedler-Lager** (`World/Props/PropCamp.cs`, Terrain `WorldHeightField.CampHollow`) auf der Westseite
     zwischen Wasserfall-Grotte und versteckter Bucht: `WorldLayout.HermitCamp` (−92, 16), flacher Lagerboden
     (`WorldFeatures.CampFloorY` 17,2 m, Radius 12 m) in einer **Senke mit 3–5 m hohem, dicht bewaldetem Wall**, kein Weg,
     kein Schild. Zugang über einen **schmalen Wildpfad** vom Platz vor der Wasserfall-Grotte (`CampTrailStart →
     CampTrailBend → CampTrailGap`, im Terrain nur ein schwacher Streifen trockenes Gras, Steinmännchen + Muschel-Kette
     als Hinweis) und eine Kerbe zum **Aussichtsfelsen** über der Westküste (`CampLookout`). Hinweis von weitem: dünne
     **Rauchsäule** der Räucherkiste + zwei Laternen nachts.
     **Bewohner:** 4–6 exzentrische Aussteiger-Einsiedler (keine Filmcrew, keine Piraten, keine reale Volksgruppe, keine
     Klischees). Inhalt: **Schrein ans Meer** im Lager (Steinplatten, Treibholz-Bogen mit Muscheln + Glaskugeln, große
     Muschel, Opferschalen, kleine Lichter, Muschelkreis) und ein **Muschel-Altar am Aussichtsfelsen**, Feuerstelle (echte
     `PropCampfire` „camp_hermits“, Kochen möglich) mit 3 Baumstamm-Bänken (Sitzplätze) und Kochdreibein, 3 Zelte aus
     geflicktem Segeltuch, **Unterstand des Anführers** (Segeldach, Treibholz-„Thron“ mit Muschel-Lehne, Truhe mit
     allem, was das Meer bringt, Muschel-Mobile, Gezeitenbrett), Palmblatt-Unterstand, **zwei Hängematten**
     (`PropKit.HammockBed` „camp_hammock“, „camp_hammock_2“ → `PropBed.RegisterHammock`), Schleifbank mit
     Kurbel-Schleifstein + Speerständer, Fischtrocken-Gestell + Räucherkiste, **zwei Schrott-Skulpturen** (Steuerrad bzw.
     Rettungsring, Bojen, Flaschen-Windspiel, alter Fernseher), „**Kokos**“ (Kokosnuss-Kumpel in Rettungsweste),
     Ziegengehege + Hühnerauslauf (vom Dorf hierher: `goat_pen`/`chicken_run`), Regenfänger, Gartenbeet, Fallenschnüre
     mit Blechdosen-Klappern rund um den Rand, zwei Schlingenfallen am Wildpfad. Alles mit Collidern (Büsche/Gras
     passierbar). `StoryKeys.HermitCampFound` wird gesetzt, sobald jemand im Lager steht (`PropCampDiscovery`) – erst
     dann zeigt die Karten-App das Lager.
   - **NPC-Anker für Schritt 5** (`PropCamp.SpotIds`, alle als `QuestSpots` + `PropInfo`-Spots): `hermit_camp`,
     `hermit_gap` (Lücke im Wall), `hermit_trail_start`, `hermit_trail_bend`, `hermit_fire_0..3`, `hermit_tent_0..2`,
     `hermit_shelter`, `hermit_leanto`, `hermit_workbench`, `hermit_grind_lesson` (dort `StoryKeys.GrindLearned`),
     `hermit_drying`, `hermit_smoker`, `hermit_totem_0/1`, `hermit_guard_0/1` (innen an der Lücke – Misstrauen/Angriff),
     `hermit_goats`, `hermit_chickens`, `hermit_garden`, `hermit_rain`, `hermit_lookout`, `hermit_shrine`,
     `hermit_shrine_sea`, `hermit_spawn_0..5` und **je Rolle ein Arbeitsplatz**: `hermit_role_leader` (Thron im
     Unterstand), `hermit_role_smith` (Schleifbank), `hermit_role_hunter` (Netz-/Trockengestell, Fallen),
     `hermit_role_cook` (Feuer), `hermit_role_lookout` (Aussichtsfelsen), `hermit_role_gardener` (Garten + Ziegen).
     Dazu `PropInfo`-Spots `hermit_fire`, `camp_grinder`, `camp_lookout`, `camp_map_piece`, `campfire_camp_hermits`.
   - **Vulkaninsel** (Terrain `WorldHeightField.Volcano/VolcanoHint`, Farben `WorldTerrainData.VolcanoTint`):
     `WorldLayout.VolcanoIsland` (700, 120) östlich der Hauptinsel (vom Steg aus am Horizont, ~570 m), Küstenradius 56 m,
     Kraterrand 36 m (`VolcanoPeak`), Kraterboden 28 m (`WorldFeatures.CraterFloorY`), konkaver Kegel (oben ~46–48°,
     unten 25–30°, besteigbar), Erosionsrinnen, **Lavazunge** nach ONO (`VolcanoLavaDir`), **schwarzer Sandstrand** + 44 m
     breiter flacher schwarzer Schelf auf der Westseite (`VolcanoBeachDir`, Landepunkt `VolcanoBeach`), sonst steiler
     Abbruch auf 50–90 m. Schwarzer Sand = DeepRock-Facetten, Lava dunkel, dunkler Meeresboden, dunkelgrüne Farnhänge;
     feine Terrain-Kacheln (3,2 m). **Zone reserviert:** `Zones.Volcano = "volcano_isle"` (am Ende von `Zones.All` →
     Bit 12, alte zoneMasks gültig, Name „Vulkaninsel“), `WorldLayout.VolcanoZoneRadius` 190 m.
     **Freischaltung:** erreichbar ab der **Wrack-Freischaltung** (`World/VolcanoGate.cs`, `VolcanoGate.Open` =
     `unlockedSites` enthält „wreck“): bis dahin drückt eine starke Strömung im Ring `WorldLayout.VolcanoGateRadius`
     (235 m) das Boot zurück (`BoatController`, wie die Nebelbank) und beim Einfahren erscheint einmal die Anforderung
     (`VolcanoGate.RequirementText`, nichts wird vorher angekündigt); die Karten-App zeigt bis dahin ein Schloss.
     **Urzeit-Schlund** für das große Urzeit-Monster: `WorldLayout.VolcanoAbyss` (818, 150), Radius 48 m, Boden ~128 m
     tief, terrassierte Steilwände (Spot `volcano_abyss`). `StoryKeys.VolcanoLanded` = jemand stand auf der Insel.
     Requisiten + Spawn-Anker: Punkt 10.
   - **Offene Wiesen** (`PropJungle.Meadow`): drei Lichtungen ohne Kronendach (zwischen Hütte und Heli-Pad, nördliches
     Flachland am Leuchtturmweg, Hang südlich der Schlucht) – Gras + Blumen, damit Hütte, Heli-Pad und Leuchtturm von
     weitem lesbar sind.
   - **Felsen in natürlichen Gruppen** (`PropJungle.BuildRocks` neu): 17 Felsgruppen im Inland (an Hängen/Schultern,
     moosig, ein großer Brocken + 1–4 kleinere hangabwärts + Kiesel, ab und zu ein stehender Stein oder eine flache
     Platte) und 16 an der Küste statt einzeln verstreuter Steine.
   - **Horsts Ecke** (`World/Props/PropHorst.cs`): Hängematte zwischen zwei Palmen am Strand südlich der Hütte, ~26 m
     abseits des Stegwegs (`PropHorst.Corner` (93, −22)), registrierter Schlafplatz „horst_hammock“; seine Bank + das
     „HAFENMEISTER“-Schild stehen jetzt dort (`PropDock.Extras` → `PropHorst.BenchSpot`), Kühlbox, Thermoskanne, Angel,
     Flip-Flops. Anker: `horst_hammock` (liegen), `horst_home`, `horst_corner`, `horst_bench` (neu dort). Steg bleibt frei.
2. **Schilder** (`PropPaths`): jeder Wegweiser steht **2,2 m neben seinem Weg** (nie darauf, nie im Gebüsch –
   `SnapBesideTrail`), jeder Pfeil zeigt **entlang des Weges** (Tangente ~8 m den Weg hinunter, `TrailBearing`) statt
   schnurgerade auf einen Kontrollpunkt hinter einer Kurve; Dorf-Piktogramm → „Strand“; Piktogramme 1 cm vor dem Brett
   (kein Z-Fighting); 2,2 m Freiraum für Vegetation. Breite Text-Schilder (`PropKit.Sign`, z. B. „HOCHSPANNUNG“,
   „KANAL TIEFSEE“) setzen jeden Seitenpfosten auf **seinen** Boden (hingen am Hang in der Luft). `PropPaths.SignAudit`.
3. **Badeinsel weiter draußen:** `WorldLayout.SwimRaft` (118, 34) → **(146, 44)** = ~40 m vor dem Strand, ~3,7 m Wasser
   (Badeloch vertieft), nördlich der Bootsspur; die Leiter zeigt weiter zum Ufer.
4. **Kartenstücke, Lagerfeuer, Tagesfunde** (`PropSurvival`, `MapPieces.UseDefaultPlacement = false` in `IslandProps`):
   - 8 Stücke, mittlere Schwierigkeit, nicht von den Hauptwegen sichtbar: **liegend** (glitzern) 0 Meeresboden unter der
     Badeinsel (3,7 m tauchen), 2 Nische hinter dem Wasserfall (Engstelle), 4 Kammer der Wasserfall-Grotte,
     6 Einsiedler-Lager (auf Kokos’ Kiste); **vergraben** (Sandhügel mit Stockkreuz + **Pfeil aus Kieseln**, je ≥ 12 m
     von jedem Weg) 1 Treibholzstrand, 3 Sand südwestlich der Gezeitenbecken, 5 Südstrand westlich des alten Stegs,
     7 Nordstrand am Fuß der Leuchtturm-Strickleiter (klettern).
   - **Das Geheimnis (Schatz-X):** eine versteckte Mini-Bucht unter den Südwest-Klippen (unter der Funkturm-Ruine,
     ~(−58, −102)), nur schwimmend/mit dem Boot erreichbar (nicht auf dem Vulkan, weil der erst mit dem Wrack öffnet).
   - Lagerfeuer: Hüttenstrand, Treibholzstrand, versteckte Bucht + **Südstrand**, **Vulkanstrand**, **Einsiedler-Lager**.
   - Tagesfunde neu: Treibholz + Kokosnüsse am Südstrand, Schleifsteine an der Funkturm-Ruine, Treibholz +
     Lava-Schleifsteine am Vulkanstrand. Feuerstellen flach fest (man steigt auf die Steine), die Schatztruhe ist fest.
   - „Gus’ Schuldscheine“ (Geheimnis `village_loft`) liegen jetzt in der Truhe der Einsiedler (angespült), die Logbuchseite
     „Gezeitenkalender“ (`tide_calendar_page`) auf dem Boden des Anführer-Unterstands.
5. **Kollisionen Insel (Agent world):** Palmen bekommen Kapseln **entlang des schrägen Stamms** (`PropSolid.PalmTrunk`
   in `PropJungle.AddPalm` – gilt für alle Palmen inkl. Mini-Inseln/Vulkan), alle Felsen ab ~0,45 m (vorher > 1,2 m),
   Felsen am Wasserfall-Becken, Steg-Pfähle am Südstrand, Kanus.
6. **Wolken + Himmel (Agent „sky“, `WorldClouds.cs` neu geschrieben):** geschichtete Low-Poly-Kumuluswolken (flache
   Unterseite, 5–7 Basis-, 3–4 Mittel-, 1–3 Blumenkohl-Puffs, flach schattiert, verdeckte Flächen entfernt), 16 Slots,
   Anzahl nach Wetter (`round(lerp(4, 16, Weather.Cloudiness))`: sonnig 5 … Schauer 15, bei Bewölkung höher/tiefer/grauer),
   Drift mit `Weather.Wind01` (1,6–5,5 m/s aus Osten ±25°/Tag), Layout aus Save-Seed + Tag + Wetter, Drift auf der
   geteilten Spieluhr (Koop gleich). Farben pro Facette nach Sonnenhöhe (Mittag Oberseite #F7F9FC, Unterseite ≥ #DCE5F1,
   goldene Stunde #FFD9B8/#C9B3C8, Sonnenuntergang korall, Nacht blaugrau), eigene Luftperspektive. Am klaren Tag löst sich
   eine Wolke vor der Sonne langsam auf. ~17 Draw Calls, keine Allokationen. `WorldSkyDome`: warmes Horizont-Glühen
   Richtung Sonne (−6°…10°), 9 ferne Stratus-/Zirrus-Bänder am Horizont (Queue 1002, driften, Sonnenuntergang farbig).
   `WorldSky`: Sterne/Mond auf Queue 2440/2441 (vor den Wolken 2450 – Wolken verdecken sie jetzt). `Weather.TestOverride`
   nur für Tests.
7. **Wasser verdeckt den Grund (Agent „sky“):** Blick-Deckkraft pro Vertex (`WorldOcean.ViewOpacity`: Fresnel +
   längerer Lichtweg + streifender Winkel) über der Tiefen-Rampe (`WorldWaterMap`: 0 m 0,55 → 3,5 m 0,88 → 6 m 0,97 →
   ≥ 15 m 1,0). Beispiele (Auge 3 m): Lagune 2 m tief senkrecht ≈ 0,77, in 15 m ≈ 0,92, in 40 m ≈ 0,99; 15 m Tiefe immer
   ≥ 0,99; vom Strand (1,8 m) 0,5 m tief in 3 m: 0,68 (Sand sichtbar, türkis). Unterwasser-Ansicht unverändert.
   Neuer Haken `FX/FXGraphics.FXSurfaceCull` (Layer-Cull-Distanz 22 m, solange die Kamera über Wasser ist) – noch kein
   Layer registriert (Tiere liegen auf Default).
8. **Haus innen + Einrichten (Agent „hut“):** `PropHut.BuildInterior` neu in Zonen: Schlafecke (Bett mit Steppdecke,
   Nachttisch mit Lampe/Buch/Wecker – jetzt mit Collider, Schaffell, Pinnwand mit Gus’ Seekarte), Schreibtisch (Laptop,
   Kaktus, Gus’ Brief, **Katalog-Klemmbrett „E – Einrichten“**), langes Wandregal, Spind (nach z 2,75 – stand 0,29 m vom
   Spawn), **Werkstattecke** (Werkbank mit Schraubstock, Lochwand, Hocker, **Schleifgerät**), Sofa-Koje + Bücherregal +
   Bilder, **TV-Ecke** (Röhren-TV auf Schrank mit Videokassetten, Sessel, Beistelltisch, Stehlampe), **Küchenecke**
   (Kühlschrank mit Radio, Spüle, Abtropfgestell, Obstschale, Regale, **Wasserfilter** neben der Theke, Topfpflanze),
   Trophäenregal (goldene Kamera + Messing-Taucherhelm), präparierter Fisch, Hängepflanze, Leisten. 3 Lampen mit
   `PropNightLight`. Alle Möbel mit Collidern; Betten/Hängematten weiter exakt registriert, Spawn frei (≥ 0,6 m).
   **Einrichten:** `Core/HutDecor.cs` (Katalog + API), `World/Props/PropHutDecor.cs` (anpassbare Teile als eigene
   Renderer: Wandverkleidung, Boden, Teppiche, Vorhänge, Deko), `UI/UIHutDecor.cs` (Karte mit Tabs Wände / Boden /
   Teppich / Vorhänge / Deko, Farbfelder + Icons, Kaufbestätigung). 8 Wandfarben, 6 Böden, 6 Teppiche (inkl. keiner),
   7 Vorhänge (inkl. keine), 11 Deko-Teile: gratis Topfpalme, Lichterkette; gekauft Surfbrett an der Wand 450 $,
   Plattenspieler 600 $, Hängesessel 1.200 $, Marlin-Trophäe 1.800 $, Aquarium 3.000 $; gefunden Muschelgirlande
   (3 goldene Muscheln), Logbuch-Bilderrahmen (3 Logbuchseiten), Flaschenpost-Sammlung (1 Flaschenpost), Silberner
   Fin-Button (Erfolg „f10k“). **Save:** `GameState.hut`, `GameState.CurrentVersion = 6` (alte Saves → Standard-Hütte,
   kaputte Werte repariert). **Koop:** Gäste → `Net.Command(Custom, CoreCommands.HutKey, op, value)`, Host prüft/kauft
   (`TrySpend`) und erzwingt einen Snapshot (`NetState.ForceSnapshot`), Gäste übernehmen per Snapshot.
   `UIState.DecorOpen` (blockt Bewegung). **Surfbrett-Ständer** (`BuildSurfRack`): Bretter stehen mit dem Heck auf dem
   Boden und lehnen an der oberen Stange (lehnten vorher 13 cm vor dem Ständer in der Luft).
9. **Fehlende Collider (Agent „col“, neuer Helfer `World/Props/PropSolid.cs`):** Korallen (Lumps/Kapseln/Tisch-Boxen/
   Fächer-Platten ab 0,5 m, weiche Korallen/Anemonen/Seegras passierbar) + Riff-Grundfelsen + lose Steine (konvexe
   Hülle) in `PropReefs`/`PropCoral`; Kelp-Felsen + Steine ab 0,35 m (`PropKelp`, Kelp passierbar); Tiefsee-Formationen,
   Basalt, Wal-Skelett (`PropDeep`); Tauchplatz-Boje + Festmach-Bojen (kinematischer Rigidbody, bewegt sich mit);
   `PropKit.RockCluster` ab 0,35 m; Wrack (Schornstein, Masten, Kran, Lüfter, Winde, Reling, Propeller, Fässer,
   Rettungsboot, Schlagtür – Durchgänge offen); **Hubschrauber** (Rumpf, Motor, Heckausleger, Kufen – Rotor nicht);
   Yacht/Katamaran/Kanu (`NpcVehicles`); Laternenpfähle + Poller am Steg; Sandbänke (begehbar), Ruderboot-Wracks,
   versunkener Steg, Baumhaus-Stämme, Palmenhaine (`PropDetails`); Grill fest (Trigger bleibt), Treibholz,
   Kanister (`PropBeach`); Funkturm-Gitter bis 9 m (`PropPOI2`); Tonis Hütte (`PropIslandExtras`); Werkzeugwagen im
   Hangar. **Boot-Regel:** Collider im Wasser, deren Oberkante höher als 1,6 m unter dem Meeresspiegel liegt, kommen auf
   **Layer 28 „boat-pass“** (`Physics.IgnoreLayerCollision(0, 28)`) – der Taucher stößt an, das Boot (Default) bleibt nicht
   an Lagunen-Riffen/Bojen/Sandbänken hängen. **Surfbretter im Sand** (`PropBeach.StuckBoardCenter`): Heck 0,25–0,28 m
   unter dem tiefsten Boden darunter – schweben nie mehr am Hang.
10. **Vulkan-Requisiten + jede Insel individuell (Agent „isle“, `World/Props/PropVolcano.cs` (+ `PropVolcanoFX`),
    neu `PropVolcanoPlants.cs`, `PropMiniIslands.cs`):**
    - **Krater:** glühender Lavasee mit dunkler Kruste + 6 Krustenschollen, 7 glühende Risse, 11 zackige Randblöcke
      (ferne Batch – Silhouette vom Steg), Licht „CraterGlow“ nachts. **Lavazunge:** 4 glühende Rissbahnen bis ins Meer,
      46 Basaltbrocken, 2 Dampfquellen wo Lava auf Wasser trifft. 4 Flanken-Fumarolen mit Schwefelkruste.
    - **Rauchfahne** „VolcanoSmoke“ (~150 Partikel, 9–15 m groß, 70–90 s Lebenszeit, vorgewärmt, treibt mit dem
      Passatwind wie die Wolken, Stärke nach `Weather.Wind01`); `PropVolcanoFX` 5×/s ohne Allokationen: tags hellgrau,
      nachts dunkel mit oranger Unterglut, Lava-Puls, Dampf aus > 320 m, Lichter aus > 450 m.
    - **Schwarzer Strand:** 20 Lavabrocken, 48 Bimssteine, 6 Treibholzstämme, Wurzelstumpf, 3 Spülsäume, 7 windschiefe
      Palmen nur am Strand; tote Palme „volcano_dead_palm“; Felsküste ringsum (64 Brocken); **Fossilienwand**
      (Ammoniten, Oberschenkelknochen, Wirbel + Rippen, großer Schädel – nur Optik). **Hänge:** Bodenfarne, Baumfarne,
      Palmfarne (Cycas), Büsche, Gras, verbrannte Stämme oben, Asche + Lavabrocken am oberen Kegel, Schachtelhalme.
    - **Unter Wasser** (nur < 330 m aktiv): 6 Lavariff-/Kissenlava-Gruppen auf dem Schelf mit Seelilien, Fassschwämmen,
      Seeigeln + 48 Brocken, 4 Seelilien-Gärten am Ostabbruch, **Lavaröhren-Höhle** (5 Felsbögen die 67°-Ostwand
      hinunter von ~46 auf 59 m, ~1,6 m freier Radius).
    - **Spawn-Anker** (`PropInfo.SetSpot`): `volcano_beach`, `volcano_mooring` (Boje ~3,4 m tief), `volcano_crater`
      (Randblick), `volcano_crater_floor`, `volcano_land_0..5` (Strand, Farnhang, vor der Fossilienwand, hoher Hang),
      `volcano_reef_0..5` (Schelf 6–10 m), `volcano_deep_0..3` (46–76 m), `volcano_cave` (+ `_in`/`_out`),
      `volcano_lava_sea`, `volcano_vent_0..3`, `volcano_fossils`, `volcano_dead_palm`, dazu (world) `volcano_abyss`
      (Monster-Schlund). Land-Spots Bereich „volcano“ (Strand, Wasser, Sonnenplätze auf Brocken, Farnwiesen, Ansitze auf
      Stämmen), Habitate Zone „volcano_isle“ (Spalten, Seefächer, Sand, Überhänge in der Röhre).
    - **Inseln:** Kelp = üppige grüne Insel (Palmen, Büsche, Bananen, 8 Mangroven, Prunkwinden, Seegraswiesen 0,6–3 m);
      Wrack = rostiger Schrottstrand (Rumpfplatten, Träger, Kessel, 7 Ölfässer, Ketten, angespültes Rettungsboot
      `wreck_lifeboat`, trockenes Dünengras, 2 Palmen); Blauwasser = Basaltsäulen mit Seevogelnestern (Land-Spots Nest),
      7 windschiefe Nadelbäume, kein Palmen; Graben = weißes Mini-Atoll (Korallenschutt, eine weit überhängende Palme
      `trench_palm`, 14 weiße Sandbänke im Ring, Antenne bleibt); Nebelinsel = Baumfarne, Moospolster, tiefer
      Bodennebel, ~26 rosa/violett leuchtende Pflanzen um Tonis Lager (nachts `PropNightLight`). Alle Felsen mit
      konvexem Collider. LOD: Vulkan-Fernfelsen 1400 m, Bäume Lite bis 2400 m, Lava bis 2000 m, Details 170 m.

### Geänderte / neue Dateien
- **world:** `Core/Contracts.cs` (WorldLayout: `SwimRaft`, `HermitCamp`(+Radien), `VolcanoIsland`, `VolcanoShoreRadius`,
  `VolcanoPeak`, `VolcanoZoneRadius`, `VolcanoGateRadius`, `VolcanoAbyss`(+Radius/Tiefe); `Zones.Volcano`),
  `Core/StoryFlags.cs` (`HermitCampFound`, `VolcanoLanded`), `World/WorldFeatures.cs`, `World/WorldHeightField.cs`
  (CampHollow, CampTrail, Volcano, Abyss, Badeloch), `World/WorldTerrainData.cs` (VolcanoTint, Wildpfad-Streifen, feine
  Kacheln), **neu** `World/VolcanoGate.cs`, `Boat/BoatController.cs` (Strömungsring), `World/Props/IslandProps.cs`,
  `PropVillage.cs`, **neu** `PropCamp.cs`, **neu** `PropHorst.cs`, `PropDock.cs` (Horsts Bank), `PropPaths.cs`
  (Schilder-Audit), `PropKit.cs` (`Sign`-Pfosten), `PropSurvival.cs` (Kartenstücke, Feuer, Funde, Schatz, Spots,
  Collider), `PropJungle.cs` (Wiesen, Felsgruppen, Palmen-/Fels-Collider), `PropPOI.cs` (Beckenfelsen),
  `UI/UIAppMap.cs`, `UI/UITextures.cs`; Tests `Debug/AutoPlaytest.Round3World.cs`, `AutoShot.Round3World.cs`,
  Hooks in `AutoShot.cs` + `AutoPlaytest.Round3.cs` (Schritte 60–64), `AutoPlaytest.Round2.cs` (Dorf-Check → Steg + Lager).
- **sky:** `World/WorldClouds.cs` (neu geschrieben), `WorldSkyDome.cs`, `WorldSky.cs`, `WorldDayNight.cs`, `WorldCoast.cs`
  (WorldWaterMap), `WorldOcean.cs`, `FX/FXGraphics.cs` (FXSurfaceCull), `FX/FXDirector.cs`, `Core/Weather.cs`
  (TestOverride); Tests `AutoShot.Round3Sky.cs`, `AutoPlaytest.Round3Sky.cs`.
- **hut:** `World/Props/PropHut.cs`, **neu** `PropHutDecor.cs`, `Core/HutDecor.cs`, `UI/UIHutDecor.cs` (+ UIGlyphs-Partial),
  `Core/GameState.cs` (v6, `hut`), `Core/CoreCommands.cs` (`HutKey`), `Core/Contracts.cs` (`UIState.DecorOpen`),
  `UI/UIController.cs` (3 Hook-Zeilen); Tests `AutoPlaytest.Round3Hut.cs`, `AutoShot.Round3Hut.cs`, **neu**
  `AutoPlaytestMP.Round3Hut.cs` (+ Hook in `AutoPlaytestMP.Round3Core.cs`).
- **col:** **neu** `World/Props/PropSolid.cs`, `PropReefs.cs`, `PropCoral.cs`, `PropKelp.cs`, `PropDeep.cs`,
  `UnderwaterDecor.cs`, `PropKit.cs` (MooringBuoy, RockCluster), `PropWreck.cs`, `PropWreckInterior.cs`, `PropDock.cs`,
  `PropDetails.cs`, `PropBeach.cs`, `PropPOI2.cs`, `PropIslandExtras.cs`, `PropStructures.cs`, `PropUnder.cs`,
  `Boat/HeliChopper.cs`, `Npc/NpcVehicles.cs`; Test `AutoPlaytest.Round3Colliders.cs`.
- **isle:** `World/Props/PropVolcano.cs`, **neu** `PropVolcanoPlants.cs`, `PropMiniIslands.cs`; Tests
  `AutoShot.Round3Islands.cs`, `AutoPlaytest.Round3Islands.cs`.

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3only` → neue Schritte **60** Welt (Lagerboden flach + Senke, kein Weg/
  Schild zum Lager, alle `hermit_*`-Anker inkl. Rollen, Hängematte, Ziegen im Lager, Dorfhäuser weg/Steg da,
  Entdeckungs-Flag, Vulkan-Höhen + Schelf/Abbruch, Zone Bit 12, Gate = Wrack, Urzeit-Schlund > 110 m, Badeinsel ≥ 30 m
  vom Strand, Wegweiser-Audit, 8 Kartenstücke (4 vergraben, ≥ 12 m von Wegen), Schatz-X in der Südwest-Bucht,
  6 Lagerfeuer, neue Tagesfunde, Horsts Ecke; Bilder `60_*`), **61** Kollisionen (`61_*`), **62** Himmel + Wasser
  (`62_*`), **63** Hütte (`63_*`), **64** Inseln/Vulkan (Rauch, Dampf, Lava, Spawn-Spots mit Tiefen, Collider, Höhle frei,
  Inselidentitäten).
- Koop: `AutoPlaytestMP` S31a–c (in 11h): Gast richtet ein → Host sieht es → Host ändert Boden → Gast sieht es.
- `-autoshot`: `r3w_*`, `r3i_*`, `r3s_*`, `r3h_*` (Liste oben bzw. in den Dateien).
- Offline-Terrain-Probe: `scratchpad/r3world/probe` (`powershell -File extract.ps1`, `dotnet build`, `dotnet run --no-build
  -- <out> zoom <cx> <cz> <half> <name>`; Höhen für Punkte aus `pts.txt`).

### Offen / für die nächsten Agenten
- **Unbedingt per Screenshot prüfen (Schritt 7):** `r3w_*` (Insel aus der Luft, Südstrand ohne Dorf, Lager von oben /
  vom Wall / am Feuer / Schrein / Abenddämmerung, Wildpfad, Aussichtsfelsen, Wegweiser, Badeinsel, Vulkan vom Steg,
  Kieselpfeil am Kartenstück), `r3i_*` (Vulkan + Inseln), `r3s_*` (Wolken, Wasser), `r3h_*` (Hütte). Konkret: Lager-Wall
  dicht genug (versteckt?), Lücke frei, Zelte/Unterstände nicht im Hang; Vulkankegel besteigbar; Rauch bei 580 m lesbar;
  Lava-Helligkeit; Höhlen-Durchgang; Wolken-Helligkeit (Bloom?) und -menge am Schönwettertag; Lagunenfarbe aus der Ferne;
  Hütte: Vorhang-Falten, Proportionen von Hängesessel/Marlin/Helm, Surfbrett-Neigung. Gehzeit quer über die Insel messen
  (Spieler-Antwort: 2–3 min) – die Insel wurde nicht vergrößert (~240 m).
- **Schritt 4 (Fauna):**
  - **Vulkan-Zone verdrahten:** `FaunaZones.ZoneAt` → `Zones.Volcano` innerhalb `WorldLayout.VolcanoZoneRadius` (190 m)
    um `WorldLayout.VolcanoIsland` (vor OpenSea/DeepSea); Spawns nur wenn `VolcanoGate.Open` (Wrack freigeschaltet).
    Echte lebende Fossilien an den Ankern aus Punkt 10, **das große Urzeit-Monster im Urzeit-Schlund**
    (`WorldLayout.VolcanoAbyss`, Spot `volcano_abyss`, ~128 m). Ein eigener `DiveSite` „volcano“ wurde bewusst NICHT
    angelegt (Story-Site-/Karten-Logik nimmt an, die einzige Story-Site sei die Nebelinsel). `StoryKeys.VolcanoLanded`.
  - **Durchsichtige Tiere über Wasser:** Quallen, Geister-Materialien, Blasen (`MakeTransparent`, Queue 3000) werden NACH
    dem Ozean (2980) gezeichnet und erscheinen von Land/Boot aus auf der Wasseroberfläche. Fix: Tier-Renderer auf einen
    eigenen Layer + `FXSurfaceCull.Register(layer)` (22 m Sichtweite über Wasser) oder transparente Renderer ausblenden,
    solange die Kamera über Wasser ist und das Tier > 1 m tief schwimmt. Undurchsichtige Tiere verdeckt das Wasser jetzt.
  - **Ziegen/Hühner** leben im Einsiedler-Lager (`PropInfo` `goat_pen`/`chicken_run`, Land-Spots „goat_pen“,
    „chicken_run“, Wiese „hermit_camp“) – `FaunaAnchors` nutzt noch `WorldLayout.Village` + Offsets („village_pen“,
    „village_roof“, „village“) → umstellen; Dorfdächer gibt es nicht mehr.
  - Neue feste Collider unter Wasser (Korallen, Felsen, Wal-Skelett, Lavabögen) – Boot-Pass-Layer 28 beachten.
- **Schritt 5 (NPC + Story):**
  - **Dorf weg – diese NPCs haben ihren Platz verloren:** **Pearl, Mira, Tomas, Ada, Juno, Lio** + Hintergrund
    **v_weaver1/2, v_fisher1/2, v_teen, v_grandpa** + `npc:bg1..6:home`. Ihre Arbeits-Spots (`v_meeting`, `v_pearl`,
    `v_cards_h`, `v_calendar`, `v_eve1`, `v_boatshed`, `v_mira`, `v_kiosk`, `v_ada`, `v_stage`, `v_teen`, `v_guesthouse`,
    `v_grandpa`, `v_grandpa_carve`, `v_weaver1/2`, `v_nets`, `v_tomas`, `v_fisher1/2`, `v_lio`) registriert keine Requisite
    mehr → `QuestSpots` rechnet sie aus `WorldLayout.Village` auf den leeren Südstrand. Weiter registriert: `village`,
    `v_jetty`, `v_jetty_end`, `v_lanterns`, `v_canoe_start` (Steg bleibt), `npc:bg*:home`.
  - **Nebenaufgaben mit `needVillage`** (über `StoryKeys.VillageVisited`, NpcDirector.Quests ~Z. 219): **P1–P3 (Pearl),
    M1 (Mira), T1–T3 (Tomas), A1–A2 (Ada), J1–J2 (Juno), L1–L3 (Lio)** – 14 direkt, M2/M3 über die M1-Kette = 16 von 24.
    Dazu: P2-Eskorte zum Versammlungshaus, Laternenabend am Steg (`NpcDirector.Village`), Ada-Kiosk (Pantry fällt auf den
    Rucksack zurück), Tutorial-Ziel „Dorf Altriff besuchen“ (`TutorialQuests.VillageGoal`), `FilmChallenges` (~Z. 143,
    `WorldLayout.Village`), Landmarke „lm_village“ (NpcDirector.Quests ~Z. 1724), Heli-Missgeschick „Paket im Dorf“.
  - **Hinweise anpassen:** „pearl.clue.loft“ (Gus’ Schuldscheine) → liegen jetzt in der Truhe der Einsiedler
    (`village_loft` im Anführer-Unterstand); Logbuchseite „Gezeitenkalender“ liegt im Lager. **Kartenstücke/Schatz** sind
    jetzt mittelschwer versteckt – Hinweise per Logbuchseiten/NPC-Sätzen wären gut (Badeinsel-Grund, hinter dem Wasserfall,
    Grotten-Kammer, Lager, Treibholzstrand, Gezeitenbecken, Südstrand, Nordstrand unter der Strickleiter; Schatz in der
    versteckten Südwest-Bucht unter der Funkturm-Ruine).
  - **Einsiedler:** 4–6 Rollen, je ein Arbeitsplatz `hermit_role_*` (Anführer, Schmied/Schleifer, Jäger, Koch, Ausguck,
    Gärtner), Wachen `hermit_guard_0/1`, Schleif-Lektion an `hermit_grind_lesson` → `StoryKeys.GrindLearned`, Schreine
    `hermit_shrine(_sea)`, Hängematten „camp_hammock(_2)“ (auch Spieler-Schlafplätze), `StoryKeys.HermitCampFound`.
  - **Horst:** Zuhause jetzt `horst_hammock`/`horst_home`/`horst_bench` in Horsts Ecke am Strand – Leerlauf-/Wartepunkte
    auf dem Steg (`horst_dockend`, `horst_dockmid`, `horst_dockedge`, `NpcDirector.Story`) bitte dorthin verlegen
    (Spieler: Horst steht im Weg auf dem Steg). Der Steg bleibt frei.
  - Story-Kern Gus’ Erbe: Requisiten verweisen auf Gus (Brief, Poster, Seekarte an der Pinnwand der Hütte, Schuldscheine).
- **Schritt 6 (UI):** Karten-App: „Südstrand“ statt „Altriff“, Vulkaninsel (Schloss bis `VolcanoGate.Open`), Lager nach
  Entdeckung – beim Zoom/Pan-Umbau übernehmen; `UITextures` zeichnet die Vulkaninsel. `UIHutDecor` ggf. angleichen.
  Boot: Strömungsring am Vulkan in `BoatController` (`UpdateVolcanoGate` + FixedUpdate) – bei Boot-Umbauten erhalten.
- **Render-Queues:** Sterne/Mond 2440/2441, Wolken 2450, Horizont-Bänder 1002, Ozean 2980.
- **Performance prüfen:** ~5–7 k primitive Collider + ~1,2 k konvexe Fels-Hüllen (statisch, beim Bau verteilt), Lager-/
  Vulkan-Requisiten (gemergt, Cull/LOD), Vulkan-Rauch (≤ 220 Partikel) + Dampf, 3 Punktlichter in der Hütte.
- Kleinigkeiten: `PropVillage` behält die alten Gebäude-Builder (`BuildHouses = false`); `PropReefs.Fringing` für den
  Vulkan bewusst nicht (eigenes Lavariff); Schwimmer können den Strömungsring (nur Boot) theoretisch durchschwimmen.

---

## Schritt 4 – Tiere + Meer-Inhalte (Agent „fauna“, Compile-Tag `r3fauna`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK**. Unity wurde nicht gestartet – die neuen AutoPlaytest-Schritte
**65–71** (auch allein: `-r3fauna`), der MP-Check **S32a–d** (in 11h hinter der Hütte) und die AutoShot-Bilder **`r3f_*`**
müssen in Schritt 7 laufen. Neue Modelle (Perlboot, Pfeilschwanzkrebs, Seelilie, Mosasaurus, Kragenhai-Kragen, alle
Wracks / Truhen / Memes) sind gerechnet, nicht gesehen → **unbedingt per Screenshot prüfen**.
Koordinator-Vorgabe beachtet: „mehr Inhalt im Meer“ ist das Wichtigste der Testversion 5 (2–3 h Koop-Spaß).

### Was neu ist (GDD §19 „Meer und Tiere“ + §20 Q14–Q23, Q44, Q45, Q48)
1. **Dichte überall (Q15)** – `FaunaZones.Target`: Hausriff 80, Lagune 46, Kelp 60, Wrack 56, Blauwasser 42, Graben 22,
   Hochsee 34, Tiefsee 16, **Vulkan 52**; Extra-Schwärme Hochsee 2 / Tiefsee, Lagune, Vulkan 1; `FaunaSpawner.MaxActive` 190
   (Netz-Snapshot-Byte passt), Sicht-Band 15 (offenes Wasser mind. 14). Mehr Schwarm-Kumpels (`CreatureSwarm`: Makrele 5,
   Sardine 5, Stachelmakrele 3, Laternenfisch 3, Snapper 2, Doktorfisch 3 …). **Neu `FaunaAmbientSchools`**: 2–4 rein
   optische Köderfisch-Wolken (36–60 Fische, GPU-instanziert, je Maschine lokal, nicht filmbar) mahlen 14–26 m um die
   Kamera im offenen Wasser, weichen dem Taucher aus – das Meer zwischen den Plätzen ist nie leer. Budget: Pool, LOD
   (bestehend), Instancing, Aktivierung nur unter Wasser.
2. **Reaktionen je Art (Q21)** – `FaunaReact.TemperOf`: Darter (kleine Fische flitzen weg, Rempeln/Schnell-Schwimmen
   vergrößert den Radius), Hider (Krake, Aale, Krebse, Perlboot drücken sich weg / runter), Curious (Schildkröte, Seelöwe,
   Delfin, Sepia kommen gucken – bestehendes Verhalten), Cool (Haie, Barrakuda, Wale, Mantas, Mondfisch, Monster:
   bleiben gelassen). `FaunaBehavior.React()` in Wander/Hover; **Schreck-Stellen** (`FaunaReact.Startle`: Speertreffer,
   kreisender Hai, Monster) lassen Schwärme / Rochen / Kleinfische platzen. Inseltiere: bestehende Flucht + alle übrigen
   Krebse flüchten jetzt seitwärts (`FaunaCrabBehavior`).
3. **Inseltiere = Deko (Q22/Q23)** – `SpeciesDef.deco` (alle `land`-Arten): filmbar, reagieren, aber **kein Sealog-Eintrag**
   (`Journal.RegisterPhoto/RegisterFilmed` lehnen ab, kein „?“, nicht gezählt, Sealog-Seite „Insel“ entfernt). Neu als Deko:
   Rote Landkrabbe, Blaufußtölpel, Blauschwanz-Skink, Riffreiher (`LandFauna`-Pläne, auch am Vulkan: Meerechsen,
   Klippenkrabben, Landkrabben; Cap 52). Ziegen/Hühner/Trüffel: Pferch + Boden-Spots jetzt Tag „camp“ (Einsiedler-Lager),
   `FaunaAnchors`-Fallback ohne Dorf.
4. **Haiangriffe selten, aber spannend (Q16/Q17/Q45)** – **neu `FaunaThreat`** (Host/offline): pro Taucher ab 90 s
   Tauchzeit + ≥ 3 m, nach einem Angriff 3,5–6 min Pause; Chance/min nach Zone (Hausriff 3 %, Lagune 1,5 %, Kelp 5 %, Wrack
   8 %, Blauwasser 9 %, Tiefsee 10 %, Vulkan 7 % …) × Nacht 2,2 (Dämmerung 1,4) × Blut im Wasser (bis ×3) × Schwierigkeit ×
   Wetter. Jäger (`SpeciesDef.hunter`: Weißspitzen-Riffhai, Hammerhai, Tigerhai, Sechskiemer, Barrakuda) – vorhandener
   aus 70 m oder einer kommt außer Sicht. Ablauf: anschwimmen → **kreisen** (8–13 s, enger werdend, **Gefahren-Musik**
   `FaunaDangerMusic` = synthetisches E–F-Ostinato + Warnung „Ein Hai kreist! · Speer bereit“) → **ein Angriff** →
   Rückzug. Biss: `Combat.HurtLocal` (Puste, Knockback), Luft −12 s (nie unter 20 s Rest), 50 % 1–2 Clips des aktuellen
   Tauchgangs weg – **nie Tod**. Jeder Waffentreffer **wehrt ab** („Abgewehrt!“, Flucht, `StreamService.ReportFightWon`
   = großer Hype). Die alten Zufalls-Attacken von `FaunaPredatorBehavior` sind aus (nur noch Patrouille + Blick).
   Koop: Biss / Musik / Toasts an den Gast über **`NetSyncMsg.FaunaCombat` (103)**.
5. **Waffen gegen Tiere (Q18/Q45)** – Tiere haben keine Collider: **`Combat.SoftTargets`** (`FaunaCombat.Query`) sucht
   getroffene Tiere entlang des Stoßes (Hindernisse blockieren). `FaunaCreature` und `NetCreatureProxy` sind `IHittable`.
   Regeln (`FaunaCombat.Classify`): **Speisefisch** (`SpeciesDef.food`: Sardine, Thun, Zackenbarsch, Rotfeuerfisch,
   Fliegender Fisch, Makrele, Goldmakrele, Snapper, Stachelmakrele) + Speer/Harpune unter Wasser = **Fang** (Schuppen-
   Wölkchen, `raw_fish` ×1–3 in den Rucksack, Blut lockt Haie); **feindlich** (Jäger, Muränen, Monster) = Abwehr;
   **alles andere geschützt** (Schildkröte, Rochen, Delfin, Wal, Gerald, Seepferdchen, Rifffische …) = **verweigert**
   (`Combat.HitRefused`: kein Verschleiß, kein Kampf-Hype; Toast „Geschützt! Finger weg“, Hater im Chat + Hype −,
   `StreamService.ReportFoul`); Stock auf Fisch/Inseltier = verscheuchen. Gäste: Fang sofort lokal (Proxy versteckt),
   Treffer per `NetCommand.Custom Combat.HitKey` (netId, Art, Richtung, Schaden) an den Host.
6. **Nachts gruselig (Q44)** – Raubtier-Gewicht nachts ×1,8, Angriffe ×2,2; neue seltene Nachttiere: **Blitzlichtfisch**,
   **Rippenqualle** (leuchten), **Weißspitzen-Riffhai** (nachts ×4); Laternenfische/Perlboote/Sechskiemer steigen auf.
7. **Wetter (Q48)** – **neu `FaunaWeather`**: `Waves.Strength` (sonnig 0,8 … windig 1,35), Unterwasser-Sicht über den neuen
   Haken `FXMath.WeatherFogMul` (sonnig klarer, Schauer trüber ×1,3), Spawn-Gewichte (sonnig: Delfine, Schildkröten,
   Mantas, Walhai, Fliegende Fische oben; Schauer/Wind: mehr Quallen + Haie, Oberflächentiere weg).
8. **Vulkaninsel (Q13/Q14)** – Zone `volcano_isle` (`FaunaZones`: 190 m um die Insel + der Schlund), Spawns erst mit
   `VolcanoGate.Open` (sonst normale Hochsee). **Lebende Fossilien** (Sealog-Seite **„Vulkaninsel“**, „???“ bis offen):
   Perlboot (Spiralschale, Tentakel), Pfeilschwanzkrebs (Sand), Sechskiemer-Hai (Jäger), Kragenhai (Rüschen-Kragen),
   Seelilie (Stiel + Federarme), Schleimaal + **das Urzeit-Monster Mosasaurus „Big Mo“** (Legendary, 12–15 m, eigenes Modell
   mit Klappkiefer, 4 Paddeln, Schwanzflosse). **Neu `FaunaVolcano`**: spawnt Big Mo im Urzeit-Schlund (~100 m), sobald
   jemand < 105 m davon im Wasser ist; `FaunaMonsterBehavior`: lauert → steigt auf, wenn ein Taucher über dem Schlund ist
   → **kreist** (Musik + „Etwas Riesiges kommt …“) → beim ersten Mal nur ein naher Vorbeizug (filmbar!), danach 60 %
   **ein Biss** (55 Puste, 20 s Luft, 70 % Clip-Verlust) → zurück in die Tiefe. Treffer während der Jagd vertreiben ihn
   kurz, ein Treffer in den Angriff oder genug Schaden lassen ihn 90 s abtauchen (Flag `big_mo_repelled`, großer Hype).
9. **Schätze + Wracks (Q20)** – **neu `SeaContent`** (aus `UnderwaterDecor`, deterministisch über Gelände-Suche): **4 kleine
   Wracks** zwischen den Plätzen (Segelboot N der Insel, Wasserflugzeug Richtung Wrack, **Fischkutter mit begehbarem
   Steuerhaus** (Tür + Fenster, „versunkener Raum“), Piratenwrack Richtung Graben), jedes meldet sich beim ersten Fund
   („Entdeckt: …“, Flag `found:*`) und versteckt eine Truhe; dazu **13 Truhen** (2 Hausriff, 2 Lagune – eine an der Badeinsel
   als erster Schatz –, 2 Kelp, Wrack-Laderaum + Sand, Blauwasser, Graben, Nebelinsel, Lavaröhre, am Monster-Schlund).
   **`SeaChest`**: „E – Truhe öffnen“ 2,2 s (mit Stock/Speer „aufhebeln“ 1,2 s), Deckel geht für alle auf (Flag `chest:*`),
   **Beute in den Rucksack des Öffners**: neue **Wertsachen** (`ItemCategory.Valuable`, in der Sammeltasche ohne Platzlimit:
   Goldmünzen, Perle, Messingkompass, Fernrohr, Edelstein, Goldkelch, Seltsame Figur, Urzeit-Zahn) + Schleifsteine, Snacks,
   Glow-Soda, selten ein Speer. Verkauf im **Fischmarkt-App-Tab „Funde“** (neuer Abschnitt „Schätze im Rucksack“,
   `SeaTreasure.Sell/SellAll`), Wert im Rucksack-Detail.
10. **Memes (Q19)** – Sealog-Seite **„Kuriositäten“** (10 Einträge, „?“ bis zum Foto): Cooler Barsch mit Sonnenbrille,
    Disco-Krake (Discokugel + Licht), Stonks-Rochen (klettert Zickzack-Kurve, grüner Pfeil), Krabben-Rave (8 Krabben um
    einen Ghettoblaster), Thron am Meeresgrund (Einsiedlerkrebs-König), Gaming-PC (RGB) am Flugzeugwrack, Quietscheentchen-
    Flotte an der Oberfläche (vom Boot sichtbar), versunkener Einkaufswagen mit Fisch, Taucher-Gartenzwerg, Gummi-Dino am
    Monster-Schlund. **`SeaMemeActor`** = lokales `ICreature` (in jeder Koop-Maschine gleich, nicht treffbar): filmbar wie
    Tiere (baseValue 70–160 = Bonus-Views), Foto → Sealog. Sealog-Icons über den neuen Haken `CreatureIcons.CustomModel`.
11. **Mehr Arten** (Sealog jetzt **70**): Hausriff + Weißspitzen-Riffhai, Blitzlichtfisch; Wrack + Rotschnapper,
    Großaugen-Stachelmakrele; Hochsee + Makrele, Goldmakrele, Tigerhai, Rippenqualle; Vulkaninsel 7; Kuriositäten 10.
    Sardinen/Thun/Barrakuda/Hammerhai/Adlerrochen/Laternenfisch/Mondqualle leben auch am Vulkan.
12. **Quallen nicht durch die Oberfläche (Step-3-Punkt)** – Modelle mit transparentem Material werden von über Wasser
    ausgeblendet, sobald sie > 0,5 m tief sind (`FaunaCreature`, `NetCreatureProxy`).
13. **Tier-Events** auch in Hochsee/Tiefsee/Vulkan (Schwarm, Fressrausch, Leuchtnacht).

### Geänderte / neue Dateien
Neu: `Fauna/AI/FaunaCombat.cs`, `FaunaCreature.Combat.cs`, `FaunaReact.cs`, `FaunaThreat.cs`, `FaunaDangerMusic.cs`,
`FaunaWeather.cs`, `FaunaVolcano.cs` (+ `FaunaMonsterBehavior`), `FaunaAmbientSchools.cs`, `Fauna/Visuals/CreatureMeshFossils.cs`,
`Fauna/Content/SeaModels.cs`, `SeaMemes.cs`, `SeaTreasure.cs`, `SeaContent.cs`, Tests `Debug/AutoPlaytest.Round3Fauna.cs`,
`AutoShot.Round3Fauna.cs`, `AutoPlaytestMP.Round3Fauna.cs`.
Geändert: `Data/SpeciesDatabase.cs` (deco/food/hunter/monster/meme, 25 Sealog-Arten + 4 Deko, `Count` = Sealog),
`Data/ItemDatabase.cs` (Valuable + `value`), `Core/GameState.cs` (Sealog-Seiten, Journal ignoriert Deko), `Core/Combat.cs`
(`SoftTargets`, `HitRefused`), `Core/Backpack.cs` (Wertsachen in der Tasche), `FX/FXFrame.cs` (`WeatherFogMul`),
`Net/Sync/NetWorldWire.cs` (103), `Net/Sync/NetCreatureProxy.cs` (IHittable, Quallen-Cull), `Livestream/StreamService.cs`
(`ReportFoul`, `ReportFightWon`), `StreamChat.cs` (+8 Zeilen), `StreamEye.cs`, `Filming/FilmDetector.cs` (Deko = bekannt),
`UI/UIAppSealog.cs`, `UIAppMarket.cs`, `UIViewfinder.cs`, `UIInventory.cs`, `UIGlyphsItems.cs`, `UIAppSocial.cs`,
`UIFinale.cs`, `UIPauseMenu.cs`, `Player/PlayerNarcosis.cs`, `Npc/NpcDirector.Quests.cs` (1 Zeile),
`World/Props/UnderwaterDecor.cs` (Hook), Fauna: `FaunaSpawner`, `FaunaZones`, `FaunaCreature`, `FaunaBehavior(s*)`,
`FaunaEvents`, `FaunaAnchors`, `FaunaModule`, `FaunaGaitAnimator`, `LandFauna`, `LandAmbient`, `CreatureSwarm`,
`CreatureIcons`, `CreatureMeshCrab/Shark/Starfish/Eel/Cephalopods`; Tests `AutoPlaytest.cs` (Sealog 70, `-r3fauna`),
`AutoPlaytest.Round3.cs`, `AutoShot.cs`, `AutoPlaytestMP.Round3Core.cs`.
Kein Save-Format geändert (Truhen/Funde = Story-Flags, Memes/Arten = Journal) → keine Versionserhöhung nötig.

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3fauna` (nur diese Schritte, ≤ 16 min) oder im `-r3only`-Lauf:
  **65** Dichte (Hochsee ≥ 6/30 m + ≥ 16/80 m, Köderwolken, Hausriff ≥ 14, Lagune, Vulkan-Zone, Qualle von oben unsichtbar,
  Wetter → Wellen/Sicht), **66** Reaktionen (Papageifisch flitzt, Gerald kommt, Riffhai cool, Landkrabbe flüchtet, keine
  Landart im Sealog), **67** Haiangriff (kreist + Musik, Biss = Puste/Luft, kein Ohnmacht, Speer wehrt ab + Fight-Hook),
  **68** Speerfischen (Snapper → roher Fisch, Verschleiß, Blut), **69** Geschützt (Schildkröte/Rochen/Clownfisch verweigert
  ohne Verschleiß/Hype; Stock verscheucht), **70** Vulkan (7 Modelle bauen, Fossilien am Riff, Big Mo steigt auf, Harpune
  vertreibt ihn), **71** Truhen/Wracks/Memes (≥ 14 Truhen, 4 Wracks, Steuerhaus frei, Truhe öffnen → Rucksack → verkaufen,
  Meme-Foto → Sealog). Bilder `65_*` … `71_*`.
- Koop: `AutoPlaytestMP` **S32a–d** (Gast spießt Host-Fisch über den Proxy, Host-Hai beißt den Gast + Musik beim Gast,
  Gast-Truhe ist beim Host offen, Beute nur beim Öffner).
- `-autoshot`: `r3f_open_sea_life`, `r3f_night_reef`, `r3f_shark_circling`, `r3f_living_fossils`, `r3f_big_mo`, `r3f_chest`,
  `r3f_wreck_*` (+ `r3f_trawler_wheelhouse`), `r3f_meme_*` (10), `r3f_island_crabs`.

### Offen / für die nächsten Agenten
- **Schritt 7 – unbedingt ansehen:** Modelle (Perlboot-Spirale/Öffnung vorn, Pfeilschwanzkrebs flach mit Stachel, Seelilie
  steht auf dem Grund, Mosasaurus-Kiefer + Paddel, Kragenhai-Kragen), Wracks (liegen sie im Sand, nicht schwebend/zu tief?
  Steuerhaus betretbar?), Truhen-Lage (Hang, Korallen), Meme-Proportionen (Sonnenbrille am Kopf, Krabben-Rave, Enten auf
  den Wellen), Big-Mo-Größe/Tempo, Lautstärke der Gefahren-Musik, FPS mit 190 Tieren + Köderwolken (notfalls
  `FaunaSpawner.MaxActive`/`FaunaZones.Target`/`FaunaAmbientSchools.MaxClouds` senken). Platzierungs-Log:
  „[Fauna] sea content: … chests …“; jede Position steht in `SeaContent.Placed`.
- **Schritt 5 (NPC/Story):** Hinweise auf Wracks/Truhen/Memes (Logbuchseiten, Horst-Tipps, Einsiedler-Gerüchte über
  „Big Mo“) wären gut; Flags `found:wreck_*`, `chest:*`, `big_mo_repelled` sind nutzbar. Einsiedler-Kampf kann
  `Combat.HitRefused`/`IHittable` genauso nutzen. Rohes Fleisch weiter offen.
- **Schritt 6 (UI):** Karten-App könnte gefundene Wracks (`found:*`) als Pins zeigen; Sealog-Seiten „Vulkaninsel“ (Farbe
  orange, „???“ bis Wrack) und „Kuriositäten“ (violett) prüfen; Fischmarkt-Tab „Funde“ hat den neuen Abschnitt
  (Feinschliff gern). Chopper-Shop: Harpune/Speere jetzt wirklich nützlich (Speerfischen + Abwehr).
- Bekannt: Memes sind lokal (nicht repliziert) – bewusst, sie sind deterministisch; Big Mo nur auf dem Host simuliert
  (Gäste sehen den Proxy, Musik/Biss kommen per 103). Der alte Funde-Tab (Sand-Glitzern/Bergung) bleibt Crew-Inventar.

---

## Schritt 5 – NPCs + Einsiedler-Lager + Story + Nebenaufgaben (Agent „npc“, Compile-Tag `r3npc`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK**. Unity wurde nicht gestartet – die neuen AutoPlaytest-Schritte
**72–79** (auch allein: `-r3npc`), der MP-Check **S33a–f** (nach den Fauna-Checks S32) und die AutoShot-Bilder **`r3n_*`**
müssen in Schritt 7 laufen. Alle NPC-Körper sind neu gebaut (Spieler-Proportionen); Einsiedler-Ausstattung, Hängematten-
Posen und das Sitzen auf den Lager-Baumstämmen sind gerechnet, nicht gesehen → **unbedingt per Screenshot prüfen**.

### Was neu ist (GDD §19 „NPCs/Story/Nebenaufgaben“ + §20 Q2–Q11, Q42, Q43, Q47)
1. **Die Einsiedler (Q2/Q3)** – sechs exzentrische Aussteiger, die das Meer verehren und Fremden misstrauen (keine reale
   Volksgruppe, keine Klischees): **Ebbe** (Anführerin, Poncho, Muschelkette, Gezeitenstab), **Amboss** (Schleifer,
   Lederschürze, Schutzbrille, Wetzstein), **Harpo** (Jäger, Stirnband, Netz, Stab – bewacht die Lücke), **Topf** (Koch,
   Siebhut, Kelle), **Fernglas** (Späherin, Mütze, Rettungsweste, Fernrohr), **Wurzel** (Gärtner, Strohhut, Gießkanne).
   `NpcDatabase` Index 20–25 (`HLeader … HGardener`, `Hermits[]`, `IsHermit`), `HermitDatabase` (Start-Laune, Arbeitsplatz
   `hermit_role_*`, Lieblings-/Hass-Geschenke, Tauschangebote, Handwerk). **Tagesablauf** (`NpcDirector.Hermits`):
   6:30–18 Arbeit an der Rolle, 18–19 am Schrein, 19–22:30 **auf den drei Baumstamm-Bänken am Feuer** (je zwei pro Stamm,
   Sitzpunkte aus `PropCamp.FireRing` gerechnet), nachts Ebbe + Fernglas in `camp_hammock(_2)` (Liege-Pose über
   `PropInfo.SleepSpots`), die anderen in den Zelten (unsichtbar); der Laternenabend am Südstrand-Steg bleibt (jetzt mit den
   Einsiedlern). Neues Save-Feld `GameState.hermits` (**Version 6 → 7**, alte Saves werden normalisiert).
2. **Laune + Gesten-Rad (Q4/Q5)** – `Core/Hermits.cs`: Laune −100…100 in 4 Stufen (feindselig < −50, mürrisch < −10,
   neutral, freundlich ≥ 30), Respekt, „kennt euch“; jeden Morgen zieht die Laune ¼ zurück zur Grundlinie (Start + Respekt).
   **Gesten-Rad** (`NpcInteract`, neue Aktion `GameAction.Gestures`, Standard **G halten**, umbelegbar): 10 Zeichen
   (Hallo, Meer, Frieden, Essen, Tausch, Lernen, Hinweis, Geschenk, Danke, Filmen) mit Glyphen, Name/Rolle/Laune-Balken in
   der Mitte; loslassen oder klicken wählt. Richtige Zeichen (Meer + Frieden bei misstrauischen, Hallo bei freundlichen,
   Danke nach Geschenken …) heben die Laune, falsche nerven (Kopfschütteln, −Laune). Sie sprechen nicht unsere Sprache –
   nur Summen + Gesten; **wer nicht will, ignoriert oder scheucht weg**. Geschenke über eine Auswahl aus dem Rucksack
   (Lieblingssachen +18, ok +8, verhasst −6, ab dem 4. am Tag kaum noch). Das Rad geht auch bei Horst, Wanda, Dr. Fin,
   Toni, Chad/Brad (Reden, Tipp, Geschenk, **Interview beim Live-Filmen** = +1 % Follower, einmal pro NPC und Tag).
   **E** = Reden (mürrisch: Wegscheuchen), Prompt „E – Name   G – Zeichen“. Kein Arm-Wink-Gruß mehr bei irgendeinem NPC
   (`Wave/Greet` = Nicken).
3. **Prügelei (Q6/Q7)** – Einsiedler sind `IHittable`. **Feindselig** + jemand im Lager → Warnung, dann Angriff (Slapstick:
   Ausholen 0,45 s, `Combat.HurtLocal`). **Verloren (k. o.)** → **gefesselt** (`PlayerController.Restrained`, Minispiel
   **A/D abwechselnd zappeln**, max. 25 s), der Einsiedler bewacht dich mit verschränkten Armen und lässt dann los.
   **Gewonnen** (Stock, bis seine Puste weg ist) → er taumelt 8 s, **respektiert dich** (+Respekt, Laune mindestens
   freundlich, die anderen +5), Kampf-Hype im Stream, beim ersten Mal Funk von Horst. Unprovoziertes Schlagen = Laune
   −15/−30 + Gegenangriff; Schläge auf Horst & Co. werden verweigert (`Combat.HitRefused`). Ein Geschenk beendet den Kampf.
4. **Handwerk + Tausch (Q8/Q47)** – Amboss lehrt **Speer-Schleifen** (`StoryKeys.GrindLearned`, an `hermit_grind_lesson`),
   Harpo **Fischfallen** (`hermit_traps`: jeden Morgen +2 roher Fisch), Topf das **Kokos-Curry** (`hermit_recipe_curry`:
   roher Fisch + Kokosnuss am Feuer = Curry, `Survival.CurryId`), Wurzel die **Ernte** (`hermit_harvest`: Mango + Banane
   jeden Morgen) – über ihre Nebenaufgabe (s. 7) oder bei freundlicher Laune per „Lernen“-Zeichen. **Tauschen** (Zeichen
   „Tausch“, ab mürrisch): Schätze/Wertsachen gegen Wasser, Steine, Obst, Hinweise oder Respekt, Holz → Speer, Fisch →
   Curry/Salat (max. 2 Tausche pro Einsiedler und Tag).
5. **Story aus einem Guss (Q9)** – `DialogueDatabase` komplett überarbeitet: Brief (Onkel Gus, Insel + Hütte + Kanal,
   verlorene Tiefsee-Doku von 1987 / versunkenes TV-Studio, Horst als Nachbar), Gus’ Funk, Logbuchseiten (`PropLore` Seiten
   0/11/12/17 + Dachboden-Geheimnis: Gus’ Schuldscheine liegen jetzt in der Truhe der Einsiedler), Einsiedler (Ebbes
   Geschichte über Gus beim Annehmen), Rivalen, Toni, Nebelinsel, Finale. Keine Dorf-Zeile mehr (Test 76 prüft
   „Altriff/Perle/Lio/Gute Flut“). **Hinweise** (`NpcClues`, ~38 Einträge): Kartenteile 0–7, Schatz, Truhen, Wracks, Memes,
   Big Mo, Fossilien, Gezeitenseite, Auge – von Horst (jedes 3. Gespräch), Toni, den Einsiedlern (Zeichen „Hinweis“ /
   Tausch) und Gus. Gehörte Hinweise stehen in der Aufgaben-App.
6. **Horst (Q10)** – lebt in seiner **Strand-Hängematte** (Siesta 12–15 h + nachts liegend, `horst_hammock`), tagsüber auf
   seiner Bank in der Ecke, abends am Lagerfeuer; **kein Warte-/Leerlaufpunkt mehr auf dem Steg** (`horst_dockside` neben
   dem Steg). Ansprechen weckt ihn. Gag: fällt höchstens 1×/Tag aus der Hängematte („Geplant.“, Achievement umgetextet).
   Eigene Aufgaben H1–H5, T3, L1, L3, M2; Funk zu Lager-Fund, Fesseln, Respekt.
7. **Nebenaufgaben (Q11)** – `SideQuestDatabase` neu: die 16 Dorf-Aufgaben sind **umgezogen** (Horst, Wanda, Dr. Fin,
   Toni, Brad, Einsiedler) oder ersetzt; **neue lustige Film-Jobs** (Kamera-Symbol im Log): Horst schnarcht 10 s in der
   Hängematte (H4), Meme-Fotos (H5), Chopper-Start (W3), Selfie mit Gerald (F1), Ziegen-Yoga (P2 → Hut „Wurzels
   Strohhut“), Kokos-Porträt (C1), Kanu-Rennen (M3 → „Kanu-Wimpel“), Gezeiten-Pflöcke (P1), Laternen (P3), Ritual am
   Schrein (C2), Mosasaurus filmen (S1). Rucksack-Abgaben direkt per E beim Auftraggeber („?“ über dem Kopf). Einsiedler
   geben Aufgaben erst nach Lager-Fund + freundlicher Laune. **Aufgaben-App**: Abschnitte „Aufträge“ (erledigt/gesamt,
   Angebote), „Einsiedler“ (Laune, Respekt, gelerntes Handwerk), „Hinweise“.
8. **Rivalen-Sabotage (Q43)** – nach R1, solange keine Freunde: an ~27 % der Tage **klauen die Bottom Feeders dein letztes
   Motiv** (Warnung auf der Quest-Zeile; ein besserer Clip ≥ 0,9 holt es zurück = Follower-Bonus), **stören Streams**
   (45 % pro Live-Sendung ab 60 s: 45 s Störung, Fouls → Hype −; eine gewonnene Prügelei in der Zeit dreht es um),
   **Follower-Duell** jeden 7. Tag (bestehende Duell-Logik, neuer Funk).
9. **Tauchplätze (Q42)** – kein Platz wird vorher angekündigt (Meilenstein-Texte, Gus-Funk, Tutorial-Ziel bereinigt).
   **Neue Zeile oben mittig unter der Quest-Zeile** (`UISiteGoal`, `SiteGoal.Line`), z. B. „Wrack + Vulkaninsel: 2.500
   Follower + 60 % Sealog Kelpwald“ mit Häkchen je erfüllter Bedingung; Banner + Gus-Funk erst beim Freischalten
   (`GameEvents.SiteUnlocked`), die Vulkaninsel öffnet mit dem Wrack.
10. **NPC-Körper (Q38)** – `NpcBuilder` neu mit den Maßen der neuen Spielerfiguren (Hüftgelenk 0,92, Schulter 1,40, Ei-Kopf
    auf Hals, Augen/Brauen/Nase/Mund, Schulterkappen, verjüngte Glieder, Fäustlinge mit Daumen, Frisuren 0–8, Bärte 0–3),
    How-to-Fish-Stil; neue Posen `Dazed`, `ArmsCrossed`, Gesten `Shoo, HandOnHeart, Swing, Flinch, Bow, Spyglass, Pose`;
    Liegen auf Schlafplätzen mit Kopf-/Fußhöhe + Schaukeln, Augen zu, liegender Collider.
11. **Koop** – alles über den Host: `NpcNet.Request` (`sign`, `gift`, `trade`, `hit`, `tied`, `freed`, `handin`),
    Laune/Respekt im GameState-Snapshot (ForceSnapshot bei jeder Änderung), Band-Flags am Actor (4 Hz); Treffer gegen
    Gäste per `EvHurt`, Belohnungen in den Rucksack des Handelnden per `EvGive`; Fesseln + Zappeln laufen auf der Maschine
    des Opfers.

### Geänderte / neue Dateien
Neu: `Core/Hermits.cs`, `Core/SiteGoal.cs`, `Data/HermitDatabase.cs`, `Npc/NpcDirector.Hermits.cs`, `Npc/NpcDirector.Rivals.cs`,
`Npc/NpcClues.cs`, `Npc/NpcInteract.cs`, `UI/UISiteGoal.cs`, Tests `Debug/AutoPlaytest.Round3Npc.cs`,
`AutoPlaytestMP.Round3Npc.cs`, `AutoShot.Round3Npc.cs`.
Geändert: `Data/NpcDatabase.cs` (Dorf-NPCs `Retired`, 6 Einsiedler), `DialogueDatabase.cs` (neu geschrieben),
`SideQuestDatabase.cs` (neu geschrieben), `CosmeticsDatabase.cs` (hat13/14, boatflag1/2 umbenannt), `AchievementDatabase.cs`,
`Core/GameState.cs` (v7, `hermits`), `GameInput.cs` (`Gestures`), `Contracts.cs` (`UIState.NpcMenuOpen`), `Progression.cs`,
`Survival.cs` (Curry), `Player/PlayerController.cs` (`Restrained`), `Filming/TutorialQuests.cs` (kein Dorf-Ziel, keine
Platz-Ankündigung), `Boat/HeliMishaps.cs` (Dorf-Gewicht 0) + `HeliService.cs`/`UI/UIAppShop.cs` (Texte), `UI/UIController.cs`
(SiteGoal), `UIAppTasks.cs` (Aufträge/Einsiedler/Hinweise), `World/Props/PropInteract.cs` (Logbuch-Texte), Npc:
`NpcKeys`, `NpcTypes`, `NpcActor` (IHittable, Liegen, neue Posen), `NpcBuilder` (neu), `NpcNet`, `NpcDirector` (+ `.Village`
neu geschrieben, `.Story`, `.Visitors`, `.Quests`), `QuestSpots`, `NpcTaskLog`, `CarryModels`; Tests `AutoPlaytest.cs`
(`-r3npc`), `AutoPlaytest.Round2.cs` (Dorf-Checks → Einsiedler, Nebenaufgaben-Treiber), `AutoPlaytest.Round3.cs`,
`AutoPlaytestMP.Round3.cs` (S17 mit Amboss), `AutoPlaytestMP.Round3Core.cs`, `AutoShot.cs`.
Netz-Protokollversion unverändert (neue Request-Ops/Events laufen über die bestehenden NPC-Nachrichten).

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3npc` (nur diese Schritte) oder im `-r3only`-Lauf:
  **72** sechs Einsiedler an ihren Plätzen, Proportionen (Höhe, Kopf auf Hals), abends ≥ 5 sitzen am Feuer, Horst auf Bank /
  in der Hängematte, kein Horst-Spot auf dem Steg; **73** G halten öffnet das Rad, Meer-Zeichen + Lieblingsgeschenk heben
  die Laune, falsches Zeichen senkt sie, Geschenk-Auswahl; **74** feindseliger Einsiedler greift an → gefesselt → A/D frei;
  Stock-Sieg → Respekt + freundlich; **75** Schleifen gelernt, Schatz eintauschen, Fischfalle/Ernte am Morgen, Curry;
  **76** Story-Zeilen ohne Dorf, Hinweise für Kartenteile/Schatz/Wracks/Memes/Big Mo; **77** Platz-Zeile oben mittig, keine
  Ankündigung vorher, Banner beim Erreichen; **78** jede Nebenaufgabe bis zur Belohnung (`StepR2SideQuestsAll`, ≤ 12 min);
  **79** Rivalen klauen ein Motiv, ein besserer Clip holt es zurück. Bilder `72_*` … `79_*`.
- Koop: `AutoPlaytestMP` **S33a–f** (Gast: Zeichen + Geschenk → Host-Laune und zurück; Host-Jäger greift den Gast an →
  Gast gefesselt, zappelt frei, Host sah die Wache; Gast-Rucksack-Abgabe A1 zählt beim Host).
- `-autoshot`: `r3n_camp_day`, `r3n_camp_fire`, `r3n_camp_night`, `r3n_hermit_<rolle>` (6), `r3n_hermit_lineup`,
  `r3n_story_cast`, `r3n_horst_bench`, `r3n_horst_hammock`, `r3n_gesture_wheel`.

### Offen / für die nächsten Agenten
- **Schritt 7 – unbedingt ansehen:** neue NPC-Proportionen neben den Spielern (Kopfgröße, Hals, Hände), Einsiedler-
  Ausstattung (Siebhut, Fernrohr, Gießkanne …), Liegen in den Hängematten (Horst + Ebbe/Fernglas: Kopf am Kopfende, nicht
  im Tuch versunken), **Sitzen auf den Feuer-Baumstämmen** (Hüfte auf dem Stamm? sonst `FireSeat` in
  `NpcDirector.Hermits` anpassen), Gesten-Rad-Layout (Radius 210 px, 10 Segmente) + Geschenk-/Tausch-Auswahl,
  Fessel-Balken, Platz-Zeile oben mittig (überdeckt sie etwas?). Laune-Balance (Start −55…−15, Drift) im echten Spiel
  fühlen.
- Hinweis: `PropCamp` registriert `hermit_fire_0..3` auf 1,9 m um das Feuer (zwischen den Stämmen) – die Einsiedler
  benutzen sie nicht mehr, sie sitzen auf den Stämmen (2,6 m).
- **Schritt 6 (UI):** Gesten-Rad/Auswahl/Fessel-Balken/`UISiteGoal` sind funktional (eigenes UIDocument, Sortierung 30) –
  Feinschliff gern; Kochbuch/Rezepte könnten `hermit_recipe_curry` zeigen; Karten-App: Pins für gehörte Hinweise
  (`clue:<key>`, `NpcClues.Heard`), Lager-Bewohner. Die Einstellungen zeigen die neue Taste „Gesten-Rad (halten)“ (G).
- Test-Haken: `NpcDirector.FreezeSchedules` (AutoShot-Aufstellungen), `NpcDirector.TestFightsAllowed` (automatische Läufe
  werden sonst nie angegriffen), `NpcInteract.DebugWriggle`, `NpcDirector.DebugStartFight/DebugHermitState/
  DebugMorningCrafts/DebugSteal`.
- Bekannt: Dorf-NPC-Indizes (Pearl … `v_*`) bleiben als `NpcKind.Retired` (Netz-Indizes stabil, kein Körper);
  `StoryKeys.VillageVisited` wird nicht mehr gebraucht; Heli-Missgeschick „Dorf“ hat Gewicht 0; `raw_meat` gibt es weiter
  nicht (Tausch nutzt Fisch/Obst/Wertsachen); Interviews/Hinweise sind pro NPC und Tag begrenzt; die Menü-Musik
  „Altriff-Lied“ hängt weiter an J1 (jetzt Dr. Fins „Walgesang“).

---

## Schritt 6 – Handy + UI + Boot (Agent „ui“ + 3 Unter-Agenten, Compile-Tags `r3ui`, `r3ui_shop`, `r3ui_load`, `r3ui_studio`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK** (Tag `r3ui`). Unity wurde nicht gestartet – die neuen AutoPlaytest-Schritte
**80–87** (auch allein: `-r3ui`), der MP-Check **S34a–c** (nach S33) und die AutoShot-Bilder **`r3u_*`** müssen in Schritt 7
laufen. Spielstand-Slots/Einladen (19b) sind NICHT gemacht – das ist Schritt 6b.

### Was neu ist (GDD §19 „Bedienung und Oberfläche“, „Boot“, „Kamera“ + §20 Q30–Q37, Q40, Q41)
1. **Handy-Karte = gezeichnete Schatzkarte (Q30)** – `UI/UITreasureMap.cs` (Element), `UI/UIAppMap.cs` (App „Seekarte“),
   `UI/UIMapPanel.cs` (Overlay), `Core/MapPins.cs` (Pins + Entdeckung).
   - **Pergament-Kunst** `UITreasureMapArt`: aus dem Terrain gezeichnet, im Hintergrund (≈1–3 ms pro Frame, ab 3 s nach
     Spielstart; nie ein Ruckler – auch das Einfärben läuft zeilenweise): ein Blatt für das ganze Meer (512²) + Detail-Blätter
     (Hauptinsel 640² über ±230 m, Vulkan 320², je Landmarken-Insel 160–192²) mit weichen Rändern – Zoom bis ×16 bleibt scharf.
     Papier mit Körnung + Flecken, verbrannte Ränder, Tusche-Küstenlinie + zweite Wellenlinie, schraffierte Hangschatten,
     Strand/Grün/Fels, Vulkan aschgrau, Wasser als Türkis-Lasur (Lagune heller), gepunktete Tiefenlinien alle 20 m.
     Tinten-Ebene (`UITreasureMapInk`): Windrose mit Kompasslinien, Tauchplatz-Kreise (gestrichelt solange zu), die
     **Strömungs-Grenze um den Vulkan** (gestrichelt bis zum Wrack), Nebelwolke über der Nebelinsel bis Tonis Karte (dann die
     Bojen-Linie), gestrichelte „hier irgendwo“-Kreise für Hinweise. Deko: Wal + Haiflosse im offenen Meer.
   - **Zoom + Verschieben**: Ziehen = verschieben, Mausrad / + / − = weicher Zoom um den Zeiger (1–16), ◎ = auf mich.
     Ansicht (Zoom/Mitte) bleibt beim Neuaufbau der Seite erhalten (`UITreasureMap.PhoneView/OverlayView`).
   - **Orte erst nach Entdeckung** (`MapDiscovery`, geteilte Flags `map:<id>` über StoryFlags → Gäste über den Host):
     Hütte, Steg, Heli-Platz von Anfang an; Leuchtturm, Wasserfall, Grotte, Gezeitenbecken, Funkturm-Ruine, Südstrand,
     Treibholzstrand, versteckte Bucht, Badeinsel, Horsts Ecke erscheinen, sobald jemand dort war (Toast „Auf der Karte: …“);
     **Lager nur nach dem Besuch** (`StoryKeys.HermitCampFound`), **Vulkaninsel mit Schloss** bis `VolcanoGate.Open`.
     Ortsnamen erscheinen ab Zoom ~2,5 (sonst nur Symbole); Tauchplätze immer (Schloss solange zu).
   - Spieler-Pfeil, Boot, Koop-Taucher, Wegpunkt (Tippen auf leere Karte, rastet an Tauchplätzen ein), Aufgaben-„!“ und
     NPC-„!/?“ wie früher, **gefundene Wracks** (`found:*` → `SeaContent.Placed`), **gehörte Hinweise** (`MapClues`: `clue:*`
     aus `NpcClues` → ungefährer Ort als „?“ im gestrichelten Kreis, nie der exakte Punkt), **Schatz-X** wenn alle Kartenstücke
     da sind und der Schatz noch nicht gehoben ist.
2. **Geteilte Koop-Pins (Q31)** – `MapPins` in `GameState.mapPins` (**Version 7 → 8**, alte Saves: keine Pins). Palette
   oben links auf der Karte (Flagge, Stern, Fische, Gefahr, Schatz, Filmspot): Symbol antippen, dann Karte antippen; Pin
   antippen → Leiste mit Entfernung, „von dir/von der Crew“, Wegpunkt, Löschen. Max 24 (der älteste fällt raus).
   **Koop:** Gäste → `NetCommand.Custom CoreCommands.PinKey` (`add x z icon` / `remove id` / `clear`), optimistisch mit
   negativer Id, der Host vergibt die Id + `NetState.ForceSnapshot`; alle sehen alle Pins. **HUD**: `UIHudPins` zeigt die
   8 nächsten Pins (≤ 900 m) als kleine Symbole mit Entfernung in der Welt. `Net.ProtocolVersion = 4`.
3. **Karte beim Fahren + für den Beifahrer** – neue Aktion **`GameAction.Map` (Standard M, umbelegbar)** öffnet `UIMapPanel`
   rechts (560 px, Pergament, folgt dem Boot). **Blockiert das Spiel nicht** (`UIState.MapOverlayOpen`, nicht in
   `BlocksGameplay`): WASD lenkt weiter, nur der Mauszeiger ist frei (Maus-Blick, Mausrad-Leiste, LMB/RMB pausieren solange –
   `GameInput.Blocked`, `PlayerController.UpdateLook`). Esc/M/✕ schließen; Handy, Rucksack und Menüs schließen es; im Wasser
   bleibt es zu (wie das Handy). Die Fahr-Hilfe zeigt jetzt „M Karte“.
4. **Boot (GDD §19, Q36/Q37)**:
   - **Nur der Fahrer legt/lichtet den Anker** (Q am Steuer). Wer nicht fährt und Q drückt, bekommt „Anker nur vom Fahrer“;
     die Ankerwinde sagt nur noch „Anker: nur am Steuer (Q)“ (`BoatAnchorInteractable`, `BoatController.NotDriverHint`).
     `BoatController.ToggleAnchor()` bleibt als API (Tests, Netz).
   - **Zweiter Sitzplatz = Sitzbank vorne** (`BoatModelBuilder.BuildBench`, `BoatBenchInteractable`): Holzbank mit zwei
     Kissen an der Backbord-Bordwand des Vordecks (z 0,98–1,94), **2 Plätze**, man sitzt quer und schaut ±150° (nach vorn über
     den Bug, zur Seite aufs Meer). Steuerbord bleibt frei zum Bug; die Heli-Kiste („Boot“-Missgeschick) landet jetzt dort
     (`foredeckCrateLocal` x 0,32). „E – Hinsetzen“ nimmt den freien Platz (ein Platz mit einem Crew-Avatar ist besetzt),
     E/Leertaste steht auf. **Auf der Bank bleiben die Hände frei** (`SeatRules.RegisterPassengerSeat`,
     `SeatRules.LocalPassenger`, `PlayerController.IsPassenger`): Kamera filmen, Handy, Karte (M); Augenhöhe 1,12 m über dem
     Sitz. Am Steuer weiter alles verstaut. Boot-Upgrades bleiben reine Käufe, kein Lack/Design (Q37). Die Vulkan-Strömung aus
     Schritt 3 ist unverändert.
5. **Kameramodus minimal (Q40/Q41)** – `UIViewfinder`: oben links ● REC + Zeit und **ein kleines Symbol pro erkannter Art**
   (`ViewfinderInfo.DetectedSpecies/DetectedKnown`, unbekannte Arten grau mit „?“), unten links nur noch der Zoom; Band nur
   noch wenn es knapp wird, kein Akku, kein Datum, kein Zoom-Balken, dünnere Rahmen-Ecken. **Sealog-Foto-Reichweite 8 m**
   (`FilmShotEvaluator.PhotoRangeMax` 14 → 8: kein Zielrahmen und kein Sealog-Foto weiter weg; Abstand zur nächsten Seite
   des Tieres). Das „?“ bei unbekannten Arten bleibt.
6. **HUD an Land aufgeräumt** – der Tauchblock (Luft, Tiefe, Clips) erscheint nur noch im Wasser; auf dem Boot nur
   Platzname + Clip-Zähler; an Land nichts davon (Hunger/Durst bleiben).
7. **FinTube-Studio (Q33, Unter-Agent „studio“)** – `UI/UIAppFinTube.cs` neu: Kanal-Kopf mit Rang + Upload-Serie (Flamme) +
   „+N heute“; Tabs **Video / Statistik / Kommentare**. Video: eine Clip-Liste (je Art, beste zuerst, max 12) mit ▲ ▼ ✕,
   INTRO/FINALE-Markierung, weggelassene Clips bleiben gebankt („Beste zuerst“ setzt zurück); **die Reihenfolge zählt**
   (der erste Clip ist das Intro: Titel-Vorschläge, großes Collage-Bild, Top-Art des Videos, Kommentare). Titel + Collage wie
   bisher. Statistik: Zahlen (gesamt, Ø, heute, bestes Video), **Aufrufe pro Tag** (14 Tage, Balken), **Follower-Wachstum**
   (`followerHistory`), Top 3 Videos (`UIStudioChart`, Painter2D). Kommentare: je Video 3 Top-Kommentare (deterministisch
   aus Art/Titel/Aufrufen, ~37 Vorlagen, `StudioComments.Make`, kein Save nötig). Daten: `Progression.VideoPool/OrderClips/
   CreateUpload(…, clipOrder)` (nur die gewählten Arten werden verbraucht), `NetCommand.Upload` mit optionalem 6. Argument
   (Reihenfolge) – alte 5-Argument-Aufrufe gehen weiter.
8. **Kochbuch-App (Q34, Unter-Agent „studio“)** – `UI/UIAppCookbook.cs` (Id „cookbook“, 16. App): nur echte Rezepte –
   Gegrillter Fisch (Lagerfeuer/Einsiedler-Feuer/Grill), **Kokos-Fischcurry** (gesperrt bis Topf es lehrt,
   `hermit_recipe_curry`; Topfs Name + Lager erst nach dem Lager-Fund), Grillfisch aus der Kühlbox (Hütten-Grill),
   Gebratenes Fleisch, Trinkwasser (Filter 45 min), Kokosnuss/Mango/Banane roh. Karten mit großem Icon, Hunger/Durst-Werten,
   Zutaten-Chips (grün hab ich / rot fehlt), Status, **Wo kochen** (Symbole) und woher die Zutaten kommen; Filter
   Alles / Kann ich / Fisch / Obst / Trinken.
9. **Feinschliff Schritt-5-UI + Seiten**:
   - Gesten-Rad: Segment-Texte einzeilig (nie aus dem Kreis), Name/Rolle in der Mitte gekürzt, Hinweis schmaler.
     Geschenk-/Tausch-Auswahl: Kopf mit Symbol + ✕, Liste scrollt (max 80 % Höhe, vorher nach 9 abgeschnitten).
     Fessel-Balken auf 69 % Höhe (unter der Interaktions-Zeile bei 62 %, über der Leiste) mit dunklem Hintergrund.
     Platz-Zeile (`UISiteGoal`): leichte dunkle Pille, eine Zeile, max 620 px, der Name kürzt zuerst.
   - Sealog-Seiten **„Vulkaninsel“** (orange Kachel mit Rauch/Schloss, „Öffnet mit dem Wrack“ + die Wrack-Bedingung bzw.
     „Lebende Fossilien + ein Urzeit-Monster“) und **„Kuriositäten“** (violette Kachel, „Lustige Fundstücke · Foto =
     Eintrag“; Karten zeigen „Versteckt im Meer“ + violetten „Meme“-Chip statt „Lebt in Kurios!“).
   - Fischmarkt-Tab **„Funde“**: Zähler im Tab zählt auch die Schätze, oben Summe (Stück · Wert), „Alles verkaufen“ für
     Schätze + Bergungsgut, Hinweis „Schätze auch im Lager tauschen (G)“ sobald das Lager bekannt ist, neuer Leer-Text.
   - **Rucksack** (1480 px): skaliert sich jetzt passend in jedes Fenster (`UIInventory.FitCard`, nie größer, min. 50 %) –
     1280×720, 4:3 und kleine Fenster schneiden nichts mehr ab (Ziehen & Ablegen nutzt `worldBound`, passt).
   - **Benachrichtigungen** kürzer: Toast-Zeile max 64 Zeichen (vorher 90), Push-Karten 34/30; Stream-Raid-Text gekürzt.
10. **Ladebildschirm (Q35, Unter-Agent „load“)** – `UI/UILoadingScreen.cs` neu, `UI/UILoadingScene.cs` (+ `UILoadingSceneHost`,
    `UIGlyphsLoading.cs`): eine **animierte 3D-Unterwasser-Szene** in einem eigenen kleinen Riff weit unter der Welt
    (3200, −5600, −2600, Layer Preview 29), eigene Kamera → RenderTexture (16:9, bis 1080p, Low 720p) vollflächig hinter der
    UI. **Der eigene Taucher** (gespeicherter Look aus den Einstellungen, `DiverModels.Build`) schwimmt langsame 30-s-Runden
    mit Handkamera + Atemblasen; Doktorfisch-Schwarm (12, Low 8), 2 Papageifische, Gerald + ein Manta im Hintergrund (echte
    Fauna-Modelle), 6 Lichtstrahlen, aufsteigende Blasen, Plankton, wellige Oberfläche, Sanddünen, Korallen, Seegras,
    Kelp. Nebel/Umgebungslicht/Sonne werden nur für diese Kamera umgeschaltet (wie `FXViewModelFill`), keine Nachbearbeitung;
    läuft mit ungeskalierter Zeit (auch beim Welt-Bau), wird beim Ausblenden komplett abgebaut (Kamera, Objekte, Meshes,
    Materialien, RenderTexture). UI darüber: Logo (wippt, Blasen), unten Tipp-Karte (alle 10 Tipps mit Icon) · Fortschritt
    (Text, runder Balken mit kleinem Fisch, %) · **Meeres-Fakt-Karte** (29 echte Fakten DE/EN mit Icon), wechseln alle 9 s
    versetzt. Ohne 3D (kein Grafikgerät / Fehler) der alte flache Hintergrund.
11. **Chopper-Shop als Katalog (Q32, Unter-Agent „shop“)** – `UI/UIAppShop.cs` neu, `UI/UIItemIcons3D.cs`, `UIGlyphsShop.cs`:
    Wandas Kopf, Liefer-Tracker (mit 3D-Bild), Geld + Express-Schalter, **Kategorie-Chips** (Tauchen, Kamera, Boot, Angeln,
    Speere, Snacks, Teile), **Karten 2 pro Reihe** mit **großem eigenem 3D-Bild** (190 px), Stufen-Punkte, Bestand, Status
    (ausgerüstet / besessen / unterwegs / gesperrt), Name + Kurzzeile, bis zu 2 **Werte-Balken** im Vergleich zur
    ausgerüsteten Ausrüstung (grau = hast du, grün = Gewinn), Preis (Tagesangebot mit Band + durchgestrichenem Preis),
    **Lieferzeit** („heute 15:10–16:10“ normal / „heute 14:15“ Express) und Bestellknopf. Antippen → Detailseite (420-px-Bild,
    ‹ › durch die Kategorie, alle Balken mit Differenz, Normal/Express wählbar, „Ausrüsten“ für Besessenes; Zurück stellt die
    Scroll-Position wieder her). Kaufwege unverändert (`Shop.Buy/BuyItem/BuyFood`, Koop-Routing gleich). **3D-Bilder**
    (`UIItemIcons3D`): für JEDEN Artikel ein eigenes Low-Poly-Modell (5 Flaschen + Schnorchel, 3 Flossen + Scooter, 3 Lampen,
    Kameras über `FilmRigBuilder`, Zoom-Objektiv, Nachtsicht, Außenborder, Schlauchboot, Kompressor, Winde, 4 Angeln, Holz-/
    Stahlspeer, Harpune, Stock, Funk + Akku, alle 7 Snacks; Unbekanntes = Kiste mit Farbe), offscreen auf Layer 31 bei
    y −3000 gerendert (2×, auf 256 px, weicher Schatten, transparenter Hintergrund), pro Artikel gecacht, **nur sichtbare
    Bilder, max. 2 pro Frame**, Rückfall auf die Glyphe. Kein Bootslack/-design (Q37).
12. **Bootslack**: bewusst keiner (Q37) – nur Upgrades.

### Geänderte / neue Dateien
Neu: `Core/MapPins.cs` (MapPin, MapPins, MapDiscovery), `UI/UITreasureMap.cs` (UITreasureMapArt, UITreasureMap,
UITreasureMapInk, MapClues, UIHudPins), `UI/UIMapPanel.cs`, Tests `Debug/AutoPlaytest.Round3UI.cs`, `.Round3UISub.cs`,
`AutoPlaytestMP.Round3UI.cs`, `AutoShot.Round3UI.cs`, `.Round3UISub.cs`.
Neu geschrieben: `UI/UIAppMap.cs`.
Geändert: `Core/GameState.cs` (v8, `mapPins`), `Core/CoreCommands.cs` (`PinKey`), `Core/Net.cs` (Protokoll 4),
`Core/Contracts.cs` (`UIState.MapOverlayOpen/CursorFree`, `SeatRules`, `ViewfinderInfo.DetectedSpecies`), `Core/GameInput.cs`
(`GameAction.Map` = M, Maus-Aktionen gesperrt solange die Karte offen ist), `Core/Hands.cs` (Beifahrer), `UI/UIController.cs`
(Karten-Ebene, M-Taste, Cursor), `UI/UIHud.cs` (Pins, Tauchblock nur im Wasser), `UI/UIViewfinder.cs`, `UI/UIInventory.cs`,
`UI/UIAppSealog.cs`, `UI/UIAppMarket.cs`, `UI/UISiteGoal.cs`, `UI/UINotifications.cs`, `Npc/NpcInteract.cs`,
`Npc/NpcDirector.Rivals.cs` (1 Text), `Boat/BoatController.cs` (Anker, Fahr-Hilfe), `Boat/BoatInteractables.cs` (Winde,
`BoatBenchInteractable`), `Boat/BoatModelBuilder.cs` (Bank), `Player/PlayerController.cs`, `.Movement.cs` (Beifahrer-Sitz,
Blick-Sperre bei offener Karte), `Player/PlayerHands.cs`, `Filming/FilmHandCamera.cs`, `Filming/FilmShotEvaluator.cs` (8 m);
Hooks in `Debug/AutoPlaytest.cs` (`-r3ui`), `AutoPlaytest.Round3.cs`, `AutoPlaytestMP.Round3Core.cs`, `AutoShot.cs`.
Unter-Agent „studio“: `UI/UIAppFinTube.cs` (neu geschrieben), neu `UI/UIAppCookbook.cs`, `UI/UIGlyphsCook.cs`,
`Debug/AutoPlaytest.Round3UIStudio.cs`, `AutoShot.Round3UIStudio.cs`; geändert `Core/Progression.cs`, `Core/CoreCommands.cs`
(Upload-Argument 6), `UI/UIPhone.cs` (Kochbuch registriert), `UI/UIModule.cs` (Kommentar).
Unter-Agent „load“: `UI/UILoadingScreen.cs` (neu geschrieben), neu `UI/UILoadingScene.cs`, `UI/UILoadingSceneHost.cs`,
`UI/UIGlyphsLoading.cs`, `Debug/AutoPlaytest.Round3UILoading.cs`, `AutoShot.Round3UILoading.cs`.
Unter-Agent „shop“: `UI/UIAppShop.cs` (neu geschrieben), neu `UI/UIItemIcons3D.cs`, `UI/UIGlyphsShop.cs`,
`Debug/AutoPlaytest.Round3UIShop.cs`, `AutoShot.Round3UIShop.cs`.

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3ui` (nur diese Schritte) oder im `-r3only`-Lauf (hinter 79):
  **80** Chopper-Shop, **81** Ladebildschirm, **82** FinTube-Studio (Reihenfolge → Upload, Statistik, Kommentare),
  **83** Kochbuch (Curry gesperrt → gelernt, hab/fehlt, Filter), **84** Schatzkarte (Kunst gezeichnet, Land warm/Meer türkis,
  weicher Zoom + Zoom um den Zeiger + Verschieben, Leuchtturm + Lager erst nach Entdeckung, Pin setzen/auswählen/löschen,
  HUD-Pins, Hinweis-Kreis + Wrack, **M-Karte beim Fahren: Boot fährt weiter**), **85** Boot (Q eines Nicht-Fahrers ändert den
  Anker nicht, Winde-Text, Bank: sitzen, Augenhöhe, mit Kamera filmen, Q bleibt wirkungslos, Karte auf der Bank, aufstehen,
  Fahrer ankert mit Q), **86** Kamera-HUD (an Land kein Tauchblock, im Wasser Luftring, Tier in 10 m: kein Rahmen, in 3 m:
  Rahmen + Art-Symbol), **87** Feinschliff (Rucksack passt in den Bildschirm, Sealog-Seiten Vulkan/Kuriositäten,
  Markt-„Funde“, Gesten-Rad, Geschenk-Auswahl, Fessel-Balken). Bilder `80_*` … `87_*`.
- Koop: `AutoPlaytestMP` **S34a–c** (Gast setzt Pin → Host sieht ihn mit Host-Id → Host löscht → beim Gast weg; Gast sitzt
  auf der Bank → Host sieht den Avatar auf dem Sitz, Platz besetzt; Q des Gasts ändert den Anker nicht).
- `-autoshot`: `r3u_map_phone`, `r3u_map_zoom`, `r3u_map_world`, `r3u_bench`, `r3u_bench_view`, `r3u_map_overlay_bench`,
  `r3u_map_overlay_boat`, `r3u_hud_land`, `r3u_backpack`; Shop `r3u_shop_catalogue`, `r3u_shop_cat_*`, `r3u_shop_detail_*`,
  `r3u_shop_screen_*`, Kontaktbogen `r3u_shop_icons.png` + Einzelbilder `r3u_shop_icons/<key>.png`; Ladebildschirm
  `r3u_loading_start/_mid/_late/_en` + `r3u_loading_scene_rt` (die reine 3D-Szene); Studio `r3u_studio_clips/_stats/
  _comments`, `r3u_cookbook_locked`, `r3u_cookbook`. Der Test 80 schreibt zusätzlich `80_shop_icons_sheet.png` und die
  Kennzahl `shop.iconRenderMs`.

### Offen / für die nächsten Agenten
- **Schritt 6b (Spielstände):** `GameState.CurrentVersion` ist jetzt **8** (Pins) – bei einem neuen Feld weiter hochzählen;
  die Pins + Entdeckungs-Flags (`map:*`) gehören zur Welt (Spielstand), nicht zum Konto. `Net.ProtocolVersion` ist 4.
- **Schritt 7 – unbedingt ansehen:** Schatzkarten-Look (`r3u_map_*`: Papier zu dunkel/hell? Küstenlinie, Schraffur, Ränder
  der Detail-Blätter sichtbar?), Lesbarkeit der Ortsnamen (kursiv – falls die Schrift kein Kursiv hat, wird es schräg
  gerechnet), Overlay-Position rechts (kollidiert nichts mit Toasts/Push-Karten?), die **Bank** (`r3u_bench`: Höhe, Kissen,
  Lehne an der Bordwand, Avatar sitzt drauf – Hüfte auf Sitzhöhe 0,44 m?), Blick von der Bank (Augenhöhe 1,12 m),
  Kamera-HUD (`86_camera_hud`: Art-Symbole nicht zu groß?), Rucksack bei 1280×720, Gesten-Rad/Auswahl/Fessel-Balken.
- Auf echter Hardware fühlen: Zoom-Tempo (×1,3 je Rad-Raste, ×1,6 je Knopf), Ziehen im 3D-Handy (die Maus muss auf dem
  Handy-Bildschirm bleiben; verlässt sie ihn, endet das Ziehen), Mausrad im 3D-Handy (sonst + / −).
- Die Karte zeichnet die Insel aus dem Terrain beim ersten Spielstart im Hintergrund (~8 s bei 1 ms/Frame); wer die Karte in
  den ersten Sekunden öffnet, sieht kurz Papier ohne Insel (die Handy-App pumpt dann 3 ms/Frame).
- Pins anderer Spieler sind nicht namentlich markiert (nur „von der Crew“ + türkiser Punkt) – bewusst ohne Namen.
- Die Beifahrer-Bank kann nicht angeln (Angel braucht Stehen) und nicht kämpfen – nur Kamera/Handy/Karte.
- **Shop-Bilder ansehen** (Kontaktbogen): Ausschnitt/Winkel der diagonalen Angeln/Speere, Gummis der Harpune, Flossen-Paar,
  Schlauchboot, Außenborder; Kameras ohne Fäustling (sonst `HideMitten` in `UIItemIcons3D`); ob der transparente
  Hintergrund klappt oder der Weiß-Rückfall greift; Kartenhöhen, 2-zeilige Namen, Band „Tagesangebot −20 %“, Chip-Umbruch mit
  „Teile“. Liegt `shop.iconRenderMs` > ~15 ms, `MaxIconsPerFrame` auf 1 setzen (oder die Bilder auf dem Ladebildschirm
  vorrendern wie `PrewarmCreatures`).
- **Ladebildschirm ansehen** (`r3u_loading_scene_rt`, `81_loading_screen`): Nebel/Umgebungslicht/Sonne/Sand-Helligkeit,
  Lichtstrahlen, ob der eigene Taucher (Anzugfarbe, Hut) bei ~20–28 % Bildbreite erkennbar ist, Oberflächen-Band hinter dem
  Logo, Manta-Neigung (sonst `roll = -24f` in `PlaceFauna` umdrehen), erste Frames schon in Schwimmpose. Bekannt: bei
  timeScale 0 frieren Arme/Beine ein (Bahn läuft weiter); `WorldDayNight` schaltet die Himmelskuppel für die Lade-Kamera aus
  und für die Hauptkamera wieder ein (harmlos); auf Hoch ~75 MB GPU-Speicher nur während des Ladens.
- **Studio/Kochbuch ansehen**: Clip-Zeilen-Breite (lange Artnamen kürzen?), Tab „Kommentare“ bei 18 px, hohler Balken des
  laufenden Videos, Flammen-/Kommentar-Glyphen, „Ø“ in der Fredoka-Schrift, Chip-Umbruch im Kochbuch; der Startbildschirm
  hat jetzt 16 Apps (4 Reihen). Spoiler-Entscheidung: Topfs Name + das Lager-Feuer im Kochbuch erst nach dem Lager-Fund
  (`CampFound` in `UIAppCookbook`), vorher „Ein Koch im Dschungel kennt dieses Rezept …“.

---

## Schritt 6b – Spielstände + Einladen (Agent „slots“, Compile-Tag `r3slots`) – erledigt

Compile-Check am Ende: **Runtime OK / EditorAll OK**. Unity wurde nicht gestartet – die neuen AutoPlaytest-Schritte **88–89**
(auch allein: `-r3slots`, dort zusätzlich Schritt 18), der umgebaute Schritt **18** (Speichern + Fortsetzen), der MP-Check
**S35a–b** (nach S34) und die AutoShot-Bilder **`r3slot_*`** müssen in Schritt 7 laufen. Layout der Slot-Karten ist gerechnet,
nicht gesehen → **per Screenshot prüfen**.

### Was neu ist (GDD §19b)
1. **Vier Spielstände = vier Welten** (`Core/SaveSlots.cs`, neu). Dateien in `persistentDataPath`:
   `save_slot1.json … save_slot4.json` (die Welt), `slot{n}/gallery/` (Fotos dieser Welt: PNG + `index.json` +
   `film_photos.json`), `slots.json` (zuletzt gespielter Slot = „Fortsetzen“). `SaveSlots.Active` = Slot der laufenden Welt
   (0 = ungespeicherte Welt: Tests, `-host` ohne `-save`). API: `Exists/Info/FirstFree/ContinueSlot/LastUsed/Delete/
   MigrateLegacy/FileOf/FolderOf/GalleryOf/GuestFolder/WorldFolder/GalleryFolder`, `SlotInfo` (Tag, Follower, Geld,
   Schwierigkeit, Spielzeit, Erfolge x/34, zuletzt gespielt, Fotos + neuestes Foto) – gecacht bis die Datei sich ändert.
   `GameManager`: `NewGame(difficulty, slot)` (räumt Reste im Slot weg, speichert sofort), `LoadSlot(slot)`, `LoadGame()` =
   „Fortsetzen“ (letzter Slot), `Save()` schreibt nur in `SaveSlots.ActiveFile` (nie auf Gästen, nie ohne Slot),
   `DeleteSave()` (Permadeath) löscht den aktiven Slot samt Fotos. Das alte `NewGame(difficulty)` nimmt den ersten freien
   Slot (Tests/`SavingDisabled`: keinen).
2. **Migration**: beim ersten Start (`GameManager.Awake`, nie in Testläufen) wird `sealebrities_save.json` zu Slot 1
   (Slot 1 belegt → erster freier), der alte PC-weite Ordner `gallery/` wandert nach `slot1/gallery/`, die alte Datei bleibt als
   `sealebrities_save.json.migrated`. Läuft nur einmal.
3. **Hauptmenü** (`UI/UIMainMenu.cs`): „Fortsetzen“ (mit „#2 · Tag 12“) = letzter Slot; ohne Spielstand „Neues Spiel“ (erster
   freier Slot); **„Spielstände“** (mit „x/4“) öffnet die **4 Slot-Karten**: neuestes Foto der Welt als Bild (sonst
   Meer-Verlauf + Schwierigkeits-Symbol), „#n“, „Zuletzt“-Marke, Foto-Zähler, Schwierigkeit, **Tag**, Follower, Geld,
   **Spielzeit**, **Erfolge x/34**, Datum; Knöpfe **Spielen** + **Mülleimer** (→ Bestätigung „Spielstand n löschen? Die Welt
   mit ihren Erfolgen und Fotos ist dann weg.“). Leere Karte: „+“, „Leer“, **Neues Spiel** → Schwierigkeitskarten mit
   „Neue Welt · Spielstand n“ (Zurück/Esc → Karten). Defekte Datei: „Defekt“ + nur Löschen. Fußzeile „Testversion 5“.
4. **Alles je Welt, nichts kontoweit**: Erfolge = `GameState.achievements` der Welt (`Achievements.IsUnlocked`,
   `UnlockedCount`, `Total`); die PC-Liste `Settings.Data.achievements` wird nicht mehr gelesen/geschrieben. Benachrichtigung
   einmal pro Welt (gemerkt im eigenen `PlayerRecord`), beim Laden / Beitreten kein erneutes Aufpoppen
   (`Achievements.RememberSilently`). **Spielzeit** `GameState.playSeconds` (Host zählt, Pause offline nicht), **zuletzt
   gespielt** `GameState.lastPlayedUtc`. **Fotos** nur im Ordner der Welt (`Gallery.Folder` = `SaveSlots.GalleryFolder`,
   Wechsel der Welt lädt die Liste neu, `FilmPhotoBook` ebenso), nie automatisch im Bilder-Ordner – raus nur per
   „Exportieren“. Löschen eines Slots löscht seine Fotos (+ die FinTube-Collagen `thumbs/s{seed}*`). Karten-Pins, Sealog,
   Haus, Einsiedler, Story-Flags lagen schon im GameState → je Slot. Einstellungen + Charakter-Look bleiben global.
   **Koop-Gast**: Fotos der Host-Welt in `coop/w{saveSeed}/gallery`, `coop_player.json` (Hotbar/Rucksack-Ersatz) jetzt
   ebenfalls je Host-Welt in `coop/w{saveSeed}/` (vorher PC-weit); beim Beitreten wird `SaveSlots.Active = 0` (nichts der
   Host-Welt kann je in einen eigenen Slot geschrieben werden).
5. **Koop-Regeln**: nur der Host (Ersteller der Welt) lädt ein – die Steam-Einladekarte im Pausenmenü erscheint nur bei
   `NetLobby.CanInvite` (Host + Steam-Lobby-Besitzer), Gäste öffnen die Liste nie (`NetLobby.InviteRoleAllowed`),
   `SteamLobby.InviteFriend/OpenInviteDialog` nur als Besitzer, Rich-Presence „connect“ („Spiel beitreten“) nur beim Host.
   Gespeichert wird nur beim Host (in seinen Slot, mit allen Gast-`PlayerRecord`s wie bisher). Host verlässt → alle ins Menü
   + die Warnung (unverändert). **Host-Seiten** (IP + Steam): „Welche Welt hostest du?“ – 4 Slot-Kacheln (belegt: Tag,
   Follower, Spielzeit, Erfolge; leer: „+ Neue Welt“ → Schwierigkeitskarten); Start „Weiterspielen & hosten“ /
   „Neue Welt & hosten“ bzw. „Lobby öffnen & weiterspielen/starten“. `NetLobby.StartHostedGame(slot, continue, difficulty)`
   (die alte Überladung für `-host [-continue]` bleibt: Fortsetzen = letzter Slot).
6. **Einladen beliebig oft**: In der Freundesliste heißt der Knopf **immer „Einladen“** – kein „Eingeladen“, kein Ausgrauen,
   keine andere Farbe; jeder Klick schickt eine Einladung + kleiner Toast „Einladung gesendet“ (`SteamLobby` merkt sich keine
   Eingeladenen mehr, `WasInvited` ist weg). „Freunde einladen“ öffnet jetzt zuerst die eigene Liste (Steams Overlay-Dialog
   graut nach einer Einladung aus) – der Overlay-Dialog ist der kleine zweite Knopf „Steam-Fenster öffnen“.
7. **Save-Format**: `GameState.CurrentVersion = 9` (`playSeconds`, `lastPlayedUtc`); alte Saves: Spielzeit aus
   `localRecord.playMinutes`, Welt-Erfolge in den eigenen Record (kein Aufpoppen), Duplikate/leere Erfolgs-Ids entfernt.
   `Net.ProtocolVersion` unverändert 4 (nur JSON-Felder dazu).

### Geänderte / neue Dateien
Neu: `Core/SaveSlots.cs`, Tests `Debug/AutoPlaytest.Round3Slots.cs`, `AutoPlaytestMP.Round3Slots.cs`, `AutoShot.Round3Slots.cs`.
Neu geschrieben: `UI/UIMainMenu.cs`.
Geändert: `Core/GameManager.cs` (Slots, Migration, Spielzeit, Gast-Datei je Welt), `Core/GameState.cs` (v9),
`Core/Achievements.cs` (je Welt), `Core/PlayerRecords.cs` (stilles Übernehmen), `Core/Gallery.cs` (Ordner je Welt,
`ReadIndexAt`, `Reload`, Speicher-Liste je Welt in Testläufen), `Core/Settings.cs` (Kommentar), `Filming/FilmPhotoBook.cs`
(lädt je Welt neu), `Net/NetLobby.cs` (Slot-Auswahl auf den Host-Seiten, Test-Haken), `Net/Steam/NetLobbySteam.cs`
(Einladen nur Host, Knopf immer „Einladen“, Test-Haken), `Net/Steam/SteamLobby.cs` (Besitzer-Prüfung, kein Merken,
connect nur Host); Tests: `AutoPlaytest.cs` (`-r3slots`, Schritt 18 im Sandkasten, Testfotos mit vollem Pfad, Finish räumt den
Sandkasten), `AutoPlaytest.Round3.cs` (Hook), `AutoPlaytestMP.cs` (Testfotos voller Pfad, Save-Pfad), `AutoPlaytestMP.Round3Core.cs`
(Hook S35), `AutoShot.cs` (Hook nach `01_menu`).

### Testen
- `SEALEBRITIES.exe -autoplaytest "<ordner>" -r3slots` (≈ 4 min) oder im `-r3only`-Lauf (hinter 87). Alles läuft in einem
  **Sandkasten** `persistentDataPath/autotest_slots` (`SaveSlots.RootOverride`) – die echten Slots werden nie geschrieben
  (Fingerabdruck-Prüfung). **18** Speichern in Sandkasten-Slot 1 + Fortsetzen; **88** 4 leere Slots → alter v8-Einzelspielstand
  + alte Galerie → Slot 1 (Backup, nur einmal, Karte: Tag/Follower/Geld/Erfolge 2/34/Spielzeit 1 h 30/Foto), Slot 1 laden
  (v9, eigene Fotos, eigene Erfolge, kein Aufpoppen), Speichern v9, Spielzeit läuft, neue Welt in Slot 2 (keine Fotos/Erfolge
  von Welt 1, Foto nur im Slot-2-Ordner, nie im Bilder-Ordner), zurück zu Slot 1, Slot 2 löschen (mit Fotos), Menü: 4 Karten
  (1× Spielen/Löschen, 3× Neues Spiel), Löschen fragt nach (Abbrechen behält), leere Karte → Schwierigkeit für Slot 3, Host-Seiten
  4 Slot-Kacheln (belegt = fortsetzen, leer = neue Welt), Löschen bestätigt → weg; zurück in die Testwelt. Bilder
  `88_slots_menu`, `88_slots_delete_confirm`, `88_slots_new_world`, `88_host_slots`. **89** Einladeliste mit 3 Testfreunden
  (Zähl-Sender `NetLobby.InviteSenderOverride` – **nie eine echte Steam-Einladung**): alle Knöpfe „Einladen“, 4 Klicks auf denselben
  Freund → 4 Einladungen, 4 Toasts, Text/Farbe/Deckkraft/aktiv unverändert, neu geöffnet kein „Eingeladen/Erneut“; ohne
  Steam-Lobby öffnet sich die echte Liste nicht. Bilder `89_invite_list`, `89_invite_list_after_clicks`.
- Koop: `AutoPlaytestMP` **S35a–b**: Host darf einladen; Gast nicht (Liste öffnet nie), Gast hat keine Save-Datei, Gast-Fotos in
  `autotest_coop/w<seed>` (echt: `coop/w<seed>`), Gast sieht die Erfolge der Welt.
- `-autoshot`: `r3slot_menu`, `r3slot_cards`, `r3slot_delete`, `r3slot_new_world`, `r3slot_host`, `r3slot_host_new`,
  `r3slot_steamhost`, `r3slot_invite`, `r3slot_invite_clicked` (Sandkasten `autoshot_slots` mit 3 Beispielwelten, danach gelöscht).
- Von Hand: alten Spielstand haben → Spiel starten → „Fortsetzen #1 · Tag …“, „Spielstände 1/4“; Karte prüfen; zweite Welt in
  Slot 2 anlegen, Fotos machen, Slot 1 laden → Galerie zeigt nur Slot-1-Fotos; Slot löschen. Steam-Koop: Gast drückt Esc → keine
  Einladekarte; Host → „Freunde einladen“ → denselben Freund mehrmals einladen.

### Offen / für Schritt 7
- **Ansehen:** `r3slot_cards` / `88_slots_menu` (Kartenbreite 286 px × 4 – passt die Reihe in 1280×720? Text-Kürzung bei
  großen Geldbeträgen, Datum in EN), `r3slot_host*` (Kacheln + Schwierigkeitskarten auf einer Seite – zu hoch?),
  `r3slot_invite*`.
- Die Migration lief nur im Sandkasten-Test; beim ersten echten Start prüfen: `save_slot1.json` + `slot1/gallery` +
  `sealebrities_save.json.migrated` im Datenordner, Fotos in der Handy-Galerie von Slot 1.
- Der Gast-Ordner hängt am `saveSeed` des Hosts: dieselbe Host-Welt = dieselben Gast-Fotos; löscht der Host seinen Slot, bleiben
  die Gast-Fotos auf dem Gast-PC liegen (nur dort löschbar über die Galerie).
- Steam-Achievements sind weiter NICHT angebunden (Erfolge nur je Welt im Spiel).
- `coop_player.json` (alt, PC-weit) wird nicht mehr gelesen – ein Gast startet in einer ihm neuen Host-Welt mit dem
  Start-Rucksack, sofern der Host keinen Record von ihm hat (gewollt: je Welt).

---

## Schritt 7 – Integration, Build, Tests, Testversion 5 (Agent „final“, Compile-Tag `r3final`) – erledigt

Compile-Check: **Runtime OK / EditorAll OK**. Build: `Build Finished, Result: Success` (Unity 6000.5.9f1, Version 0.5.2610120452).
Alle Tests liefen nur mit Test-Schaltern aus Kopien des Builds (`Builds/_r3run_<n>`), der echte Spielstand des Spielers
wurde nie gestartet (Datenordner vorher gesichert, Prüfsummen von `settings.json` und `sealebrities_save.json` danach gleich;
die Migration in Slot 1 passiert also beim ersten eigenen Start).

### Testläufe (Ordner `Builds/Screenshots/r3_*`)
| Lauf | Build | Ergebnis |
|---|---|---|
| `r3_only1` (`-r3only`) | erster Build | 33/43 – Ausgangslage, Fehler s. unten |
| `r3_autoshot1` (1920×1080 Hoch) | b | 176 Bilder, Sichtprüfung → Leiste, Lager-Kamera, Köderwolken, Bank, Studio, Memes |
| `r3_full1` (voller Lauf, 24 min) | c | 61 ok / 5 Fehler / 20 übersprungen |
| `r3_only2` | e | 41/43 |
| `r3_mp1` (Host + Gast) | f | Host 18/22, Gast 17/22 (Auto-Aufnahme, Kisten-Snacks, Pins, Zeichen) |
| `r3_mp2` | g | **22/22 + 22/22** |
| `r3_steam_selftest` | h | bestanden (Init, Relay, private Lobby, 1,2-MB-Loopback) |
| `r3_autoshot_final` / `_final2` | h / l | je 176 Bilder, 0 Fehler (Quelle der Showcase-Bilder) |
| `r3_full_final` | h | 60 ok / 5 Fehler / 21 übersprungen (20b Zufall wie TV4, 33 Leiter → behoben, 51/61/66 Tests robuster) |
| `r3_only_final` | j | 41/43 (66 Schildkröte Zufall → Test verlängert, 84 HUD-Pins → behoben) |
| `r3_mp_final` | m | **22/22 + 22/22** |
| `r3_ui_final` (`-r3ui`, 80–87) | n (Auslieferung) | **11/11** |

FPS (1280×720, ohne Begrenzung, Hoch / Niedrig): Insel 98 / 156, Hausriff 143 / 180, offenes Meer 204 / 218, Vulkan-Riff
185 / 202, Einsiedler-Lager 111 / 145; 1920×1080 Hoch Insel ~75–80. Budgets unverändert (`FaunaSpawner.MaxActive` 190,
`FaunaAmbientSchools.MaxClouds` 4). 0 Exceptions / 0 Fehlermeldungen in allen Läufen.

### Was ich repariert habe
**Spiel:**
- **Rucksack „unbegrenzt“ (§20 Q27):** `Backpack.Capacity` 24 → **48**; das Raster zeigt 4 Reihen und scrollt (Mausrad).
- **Leiste im Rucksack-Fenster:** 8 Plätze passten nicht in die linke Spalte (Platz 8 ragte heraus) – Platzgröße 80 → 66 px.
- **Toasts** lagen bei offenem Rucksack auf der Geld-Zeile (`UINotifications`: immer 150 px über dem Rand).
- **Köderfisch-Wolken** (`FaunaAmbientSchools`): jede zweite Wolke erscheint jetzt vorn im Nebel (wächst über 2 s ein) und alle
  ziehen am Taucher vorbei – vorher entstanden sie nur seitlich/hinten und das offene Meer wirkte auf Standbildern leer.
- **Big Mo** stieg zu langsam (2,2 m/s für ~90 m in 30 s) und tauchte wieder ab, ohne je zu kreisen → 5 m/s, Timer 45 s.
- **Landkrabben** flüchten wirklich (0,3 m → ~1 m/s seitwärts, `FaunaCrabBehavior`).
- **Einsiedler-Feuer** brennt abends (18:30–23:30) und in der Dämmerung von selbst (`PropCampfire` „camp_hermits“, geteilte Uhr) –
  die Einsiedler saßen vorher um eine kalte Feuerstelle; dort kann man dann auch braten.
- **Nebelinsel-Arten** an ihrer Insel ×3 Gewicht (in einem Lauf 0 von 75 Tieren).
- **Leiter-Hände (Ego):** der Greifbereich wird an das senkrechte Sichtfeld und die Blickrichtung angepasst (die untere Hand fiel
  mit den neuen Körpern aus dem Bild, Schritt 33).
- **Harpune beim Zielen** etwas tiefer und seitlich (der Schaft füllte die Bildmitte).
- **FinTube-Studio:** die Schätzwerte („~Aufrufe +Follower +Geld“) wurden vom Hochladen-Knopf zusammengedrückt („~3 +1“) – sie
  behalten ihre Breite, der Knopf bricht um.
- **HUD-Pins** (`UIHudPins`): ein Pin tief unter dem Blick (z. B. das Meer vom Leuchtturm aus) verschwand – er sitzt jetzt am
  unteren/oberen Rand des freien Bereichs (nie über der Leiste).

**Tests (veraltete Erwartungen / Test-Fehler):** Schritt 60 (Lager-Kamera lag im Hang; Wegweiser an der Wasserfall-Grotte erlaubt,
solange keiner zum Lager zeigt), 61 (zweiter Versuch gegen Strömung), 65 (Jelly-Test vom Steg statt Kamera über dem Wasser,
Wetter-Werte gleiten mit 20 s), 67 (Schnorchel-Taucher: die letzten 20 s Luft nimmt ein Biss nie), 70 (Big-Mo-Musik), 71
(Kamera in die Hand für das Meme-Foto), 72 (Horst liegt nach dem Uhrsprung noch), 73/87 (Zeichen erst nach dem Begrüßungssatz;
das Rad bleibt nur bei gehaltenem G offen), 84 (Pergament-Textur ist nicht lesbar → GPU-Kopie; HUD-Pins nach der Morgen-Karte),
MP 06/07 (keine Auto-Aufnahme mehr → REC per LMB), MP 11g (Kisten-Snacks landen im Rucksack des Öffners), MP 11h (Pin-Wettlauf,
Zeichen nach dem Satz), 41 (8 s statt 5 s an der Nebelinsel), 51 (das Feuer brät das ERSTE Rohe im Rucksack), 61 (nächste
Annäherung statt Endpunkt – schnelle Flossen gleiten am Fels entlang), 66 (neugierige Schildkröte bis 22 s). Neu: FPS-Messungen offenes Meer / Hausriff / Vulkan / Lager in `-r3only`, AutoShot `r3f_reef_life` +
„beste Blickrichtung“ für Unterwasser-Bilder, Studio-Bild mit einer Woche Kanal-Verlauf, Meme-Bilder näher, Bank-Bild vom Bug.
`AutoPlaytest.TimeBudget`: `-r3only` 85 min, `-r3fauna/-r3npc/-r3ui` 40 min, sonst 24 min.

### Auslieferung
- `Desktop/SEALEBRITIES_Testversion_5` + `ll/Builds/Testversion_5` (ohne `*_BurstDebugInformation_DoNotShip`, mit `steam_appid.txt`,
  `Koop-Test_2_Fenster.bat/.ps1`, `Anleitung_Steam_Koop.txt` mit „Neu in Testversion 5“ + Hinweis „gleiche Version“),
  `Desktop/SEALEBRITIES_Testversion_5.zip`.
- Showcase: `ll/Builds/Screenshots/tv5_showcase/` (18 Bilder 1920×1080 + `showcase.json`).
- Bericht `docs/Testbericht_Runde_3.md`, Fragen `docs/Fragen_40_Runde3.json/.md`.

### Offen / bekannt
- **Hausriff aus der Nähe** wirkt auf Standbildern leer: 43 Tiere in 20 m (Zählung), aber kleine Fische sind im Dunst winzig und
  flüchten (Darter); Spawns erst ab 12 m. Ideen: Flucht-Radius kleiner, mehr mittelgroße Rifffische, `MinSpawnDistance` im Riff 8 m.
- Rucksack 48 Plätze ohne Scrollleiste (Mausrad); Drop auf unsichtbare (gescrollte) Plätze nicht geprüft.
- Avatare zeigen Schlag/Stoß anderer Spieler nicht; Steam-Erfolge nicht angebunden; Paket-Suche verdrängt die Tutorial-Zeile nicht.
- `-r3only`/`-r3ui`/AutoShot-Schritte sind zufallsabhängig: 20b (Wrack-Zählung), 66 (Schildkröte), 41 (Nebelinsel-Arten) –
  bei einem Fehlschlag erst wiederholen.
- Der volle Lauf überspringt die Runde-3-Schritte (Zeitlimit 24 min) – dafür `-r3only` (≈ 14 min).
- Testordner `autotest_*` im Datenordner wurden nach den Läufen entfernt; der echte Spielstand wurde nie gestartet (Migration
  in Slot 1 beim ersten eigenen Start des Spielers).
