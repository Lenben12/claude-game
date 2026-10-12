# Testbericht Runde 3 – Testversion 5

Stand: 12.10.2026, morgens · Version 0.5.2610120452

## Kurz gesagt

Testversion 5 ist fertig. Sie enthält eure ganze Liste zur Vorschau 4 (Spielkonzept Abschnitt 19), den Nachtrag zu
Koop und Spielständen (19b) und die 50 Antworten aus der Nacht (Abschnitt 20). Schwerpunkt: **mehr Inhalt im Meer**.

- Spiel: `Desktop\SEALEBRITIES_Testversion_5\SEALEBRITIES.exe`
- Für den Freund: `Desktop\SEALEBRITIES_Testversion_5.zip` (41 MB) – **er braucht genau diese Version**
- Bilder zum Ansehen: `ll\Builds\Screenshots\tv5_showcase\` (18 Bilder in 1920×1080 + `showcase.json` mit je einer Frage)
- Automatische Tests: Runde-3-Test 41 von 43 Schritten, Koop 22 von 22 bei Host und Gast, Steam-Selbsttest bestanden, 0 Fehlermeldungen. Die übrigen Fehlschläge waren Test-Zeitfehler oder sind behoben (Details unten).
- **Wichtig beim ersten Start:** Dein alter Spielstand wird automatisch zu **Spielstand 1** (mit allen Fotos).
  Die alte Datei bleibt als Sicherung (`sealebrities_save.json.migrated`) liegen. Im Menü steht dann „Fortsetzen #1 · Tag …“.

Ehrlich: Gespielt haben auch diesmal nur Testprogramme. Alles wurde gebaut, automatisch durchgespielt und auf Bildern
angeschaut – wie es sich anfühlt, sagt ihr uns.

## Was ist neu (nach euren Punkten)

**Bugs und Korrekturen**
- Schlafen: Die Figur liegt jetzt wirklich **in** der Hängematte bzw. im Bett (auch der Koop-Avatar). Schlafen geht im Bett und in der Hängematte.
- Kollisionen: über 4.600 neue feste Teile – Korallen, Felsen, schräge Palmenstämme, Bojen, Wrackteile, Hubschrauber, Boote. Gras, Büsche, Seegras und Kelp bleiben passierbar. Das Boot bleibt nicht an flachen Riffen hängen.
- Leiter: Am Steg kletterst du selbst (Leertaste/E greifen, W hoch, S runter), kein Auto-Klettern mehr. Beide Hände an den Sprossen.
- Surfbretter stecken im Sand, Schilder stehen neben dem Weg und zeigen den Weg entlang, die Badeinsel liegt ~40 m draußen.
- Neue Tauchplätze werden nicht mehr vorher angekündigt. Die Bedingung steht oben in der Mitte, das Banner kommt erst beim Erreichen.
- Nebenaufgaben: alle 24 neu bzw. umgezogen (das Dorf ist weg) und jede einzeln bis zur Belohnung getestet.
- Kamera: keine automatische Aufnahme mehr, nur Linksklick. Der Sealog-Rahmen erscheint erst aus der Nähe (bis ca. 8 m). Das „?“ bleibt.
- Boot: nur der Fahrer legt den Anker (Q am Steuer). **Sitzbank vorne** für zwei. Taste **M** öffnet die Karte, auch beim Fahren.

**Meer und Tiere**
- Viel mehr Fische überall, auch im offenen Meer, dazu Köderfisch-Wolken. Tiere reagieren je nach Art: kleine Fische flitzen weg, Schildkröten kommen gucken, Haie bleiben cool, Krabben am Strand flüchten.
- **Vulkaninsel** (öffnet mit dem Wrack): schwarzer Strand, Krater mit Lava, Rauchfahne, lebende Fossilien (Perlboot, Pfeilschwanzkrebs, Kragenhai, Sechskiemer, Seelilie, Schleimaal) und **Big Mo**, ein 13-m-Mosasaurus im Urzeit-Schlund.
- **Waffen:** Stock, Holz- und Stahlspeer, Harpune. Haiangriffe sind selten, kündigen sich mit Kreisen und Musik an, kosten Puste, Luft und manchmal einen Clip – nie das Leben. Ein Treffer wehrt ab und bringt Hype im Stream. Speisefische kann man fangen, geschützte Tiere nie.
- **Schätze:** 16 Truhen, 4 kleine Wracks (ein Fischkutter mit Steuerhaus zum Reinschwimmen), Beute im Rucksack, Verkauf im Fischmarkt oder Tausch im Lager.
- **10 Meme-Fundstücke** (Krabben-Rave, Disco-Krake, Stonks-Rochen, Gaming-PC, Enten-Flotte …) mit eigener Sealog-Seite.
- Nachts gruseliger: mehr Raubtiere, Blitzlichtfische, Rippenquallen. Das Wetter ändert Sicht, Wellen und Tiere.

**Überleben**
- Hunger und Durst sinken mit der Zeit. Wer hungrig ist, wird langsamer und hat weniger Luft (nie Tod).
- Meerwasser in der Flasche holen → **Wasserfilter in der Hütte** (45 Spielminuten) → trinken.
- Rohen Fisch am **Lagerfeuer** (6 Feuerstellen, Treibholz) oder am Grill braten. Neue **Kochbuch-App**.

**Insel und Welt**
- Insel neu gestaltet: Wiesen, natürliche Felsgruppen, Horsts Hängematte am Strand. Die Dorf-Häuser sind weg.
- **Verstecktes Einsiedler-Lager** im Dschungel der Westseite: 6 Aussteiger-Einsiedler (Ebbe, Amboss, Harpo, Topf, Fernglas, Wurzel) mit eigenem Tagesablauf. Verständigung über das **Gesten-Rad (G halten)**, Geschenke, Tausch, Handwerk (Speer schärfen, Fischfallen, Kokos-Curry, Ernte). Bei schlechter Laune gibt es Slapstick-Prügeleien mit dem Stock – verlierst du, wirst du kurz gefesselt (A/D zappeln), gewinnst du, respektieren sie dich.
- **8 Kartenstücke** überall, 4 davon vergraben (mit dem Stock graben). Alle zusammen zeigen den Weg zu einem Schatz.
- Neue Wolken, Wasser verdeckt den tiefen Grund, jede kleine Insel sieht anders aus.
- **Hütte innen** neu und einrichtbar: Wandfarbe, Boden, Teppich, Vorhänge und Deko (gekauft oder gefunden).

**Figuren und Gegenstände**
- Neue Figuren: Low-Poly-Menschen mit echten Proportionen, Gesicht, Hals, Fäustlingen. Viel mehr Optionen (Körper, Größe, Hautton, Frisuren, Bärte, Nasen, 20 Hüte, Extras).
- Neue Hände und Gegenstände: Stock, Speere, Harpune (beidhändig), Flaschen, Essen. Auch die NPCs haben die neuen Körper.

**Bedienung und Oberfläche**
- **Rucksack** (Taste I) mit 48 Plätzen (praktisch unbegrenzt) und eine **Leiste mit 8 Plätzen** (Tasten 1–8). Rechtsklick auf einen Platz = Haupt-/Zweithand.
- **Karte im Handy:** gezeichnete Schatzkarte zum Zoomen und Verschieben, Orte erst nach dem Besuch, **geteilte Koop-Pins**.
- **Chopper-Shop** als Katalog mit eigenem 3D-Bild für jeden Artikel. **FinTube-Studio** mit Clip-Reihenfolge, Statistik und Kommentaren. **Wetter-App** mit Temperaturen.
- **Ladebildschirm** mit Unterwasser-Szene (dein eigener Taucher), Tipps und echten Meeres-Fakten.
- Kameramodus minimal: REC, Zoom und kleine Symbole der erkannten Tiere.
- Galerie: Bilder nur im Spiel, Export auf den PC per Knopf.

**Spielstände und Koop (19b)**
- **4 Spielstände**, jeder löschbar. Jede Karte zeigt Tag, Follower, Geld, **Spielzeit** und **Erfolge dieser Welt**. Fotos und Erfolge gehören zur Welt.
- Nur der Host lädt ein und speichert. Der Knopf heißt immer **„Einladen“** und bleibt klickbar. Verlässt der Host, endet das Spiel für alle.

## Test-Ergebnisse

Rechner: AMD Ryzen 7 5700G mit Radeon-Grafik im Prozessor, 28 GB RAM. Alle Läufe: **0 Fehlermeldungen, 0 Abstürze**.

| Test | Ergebnis |
|---|---|
| Runde-3-Test (`-r3only`, Schritte 50–89: Rucksack, Überleben, Waffen, Kartenstücke, Schlafen, Leiter, Figuren, Welt, Kollisionen, Himmel, Hütte, Inseln, Tiere, Haie, Big Mo, Truhen, Memes, Einsiedler, Story, Nebenaufgaben, Karte, Boot, Shop, Studio, Kochbuch, Ladebildschirm, Spielstände, Einladen) | **41 von 43** bestanden (14 min). Rest: die neugierige Schildkröte kam im Testfenster nicht nah genug (Zufall – Test verlängert) und die Pin-Markierungen im HUD fehlten, wenn der Pin tief unter dem Blick lag (behoben; Nachtest der Schritte 80–87 auf dem ausgelieferten Build: **11 von 11**) |
| Gesamtlauf (Tag 1 bis Finale, alle Tauchplätze, Runde 2; 24-Minuten-Limit) | **60 von 65** bestanden, 21 übersprungen (die Runde-3-Schritte hinter dem Zeitlimit – sie laufen komplett im Runde-3-Test). Rest: Fisch-Zählung am noch gesperrten Wrack (Zufall, schon in Testversion 4), Leiter-Hände in der Ego-Sicht (behoben), Lagerfeuer-, Fels- und Krabben-Test im späten Spielstand (Tests robuster, Krabben schneller) |
| Koop-Test (Host + Gast auf einem PC, 22 Schritte inkl. Rucksäcke, Lagerfeuer, Hütte, Haie, Truhen, Einsiedler, Pins, Sitzbank, Spielstände, Einladen) | **22 von 22** bei Host und Gast |
| Steam-Selbsttest (Anmeldung, Relay, private Lobby, 1,2 MB über Steam-Netz) | bestanden |
| Bilder-Rundgang 1920×1080, Grafik Hoch | 176 Bilder, keine Fehler; 18 davon im Showcase |

**Bilder pro Sekunde** (ohne Begrenzung, 1280×720):

| Ort | Hoch | Niedrig |
|---|---|---|
| Insel (Steg, Blick auf die Hütte) | 98 | 156 |
| Hausriff unter Wasser | 143 | 180 |
| Offenes Meer mit neuen Schwärmen + Köderfisch-Wolken | 204 | 218 |
| Vulkan-Riff mit lebenden Fossilien | 185 | 202 |
| Einsiedler-Lager (Requisiten, 6 Einsiedler, Rauch) | 111 | 145 |

In 1920×1080 auf Hoch: Insel ~75–80 (mit 60er-Begrenzung durchgehend 60). Die neue Tiermenge kostet kaum Leistung, die Budgets bleiben.

## Bekannte Probleme

- **Nur Testprogramme haben gespielt.** Balance (Tempo von Hunger und Durst, Laune der Einsiedler, wie oft Haie angreifen, Preise) hat noch kein Mensch gefühlt.
- **Hausriff aus der Nähe:** Die vielen Fische schwimmen oft 12–25 m entfernt im Dunst, kleine Fische flüchten vor dir. Direkt vor der Maske wirkt das Riff dadurch manchmal noch leer. Im offenen Meer ist jetzt viel los (Delfine, Schwärme, Köderfisch-Wolken).
- **Rucksack:** 48 Plätze (praktisch unbegrenzt). Das Raster zeigt 4 Reihen und scrollt nur mit dem Mausrad, eine Scrollleiste gibt es noch nicht.
- **Koop:** Mitspieler sehen bei dir nur, wie du Stock oder Speer hältst, nicht den Schlag oder Stoß selbst.
- **Karte:** Wer sie in den ersten Sekunden nach dem Laden öffnet, sieht kurz Papier ohne Insel (sie wird im Hintergrund gezeichnet).
- **Paket am falschen Ort** während des Tutorials: Die Suche steht dann nur in der Aufgaben-App, oben bleibt die Tutorial-Aufgabe.
- **Steam:** Erfolge sind noch nicht an Steam angebunden (sie zählen im Spiel je Welt). Steam zeigt weiter „Spacewar“ (Test-Nummer 480).
- Fotos, die ein Koop-Gast macht, bleiben auf seinem PC (in der Galerie der Host-Welt).
- Testversion 5 spielt nicht mit Testversion 4 zusammen.

## So startet ihr

**Allein**
1. `SEALEBRITIES.exe` im Ordner `SEALEBRITIES_Testversion_5` starten.
2. Beim ersten Start wird dein alter Spielstand zu **Spielstand 1**. „Fortsetzen“ spielt ihn weiter, unter **Spielstände** siehst du alle 4 Plätze und kannst eine neue Welt anlegen.

Tasten: E benutzen · I Rucksack · 1–8 / Mausrad Leiste · Linksklick benutzen/filmen · Rechtsklick zielen/abwehren · G halten Gesten-Rad · M Karte · Tab Handy · H Selfie · Q Anker (nur Fahrer) · Esc Pause.

**Koop über Steam**
1. Die Zip an den Freund schicken. **Er braucht genau diese Testversion 5** (mit Testversion 4 geht es nicht).
2. Beide: Steam an, online, befreundet, zwei Konten. `steam_appid.txt` bleibt neben der exe.
3. Host: Mit Freunden → Über Steam → Spiel hosten → Welt wählen → Lobby öffnen → Esc → Freunde einladen.
4. Freund: erst das Spiel starten, dann die Einladung annehmen.
- Alles Weitere: `Anleitung_Steam_Koop.txt` im Spielordner. Ein PC, zwei Fenster: `Koop-Test_2_Fenster.bat`.

## 40 Fragen

`docs/Fragen_40_Runde3.md` (auch als `.json`). Die erste Antwort ist jeweils unsere Empfehlung.
