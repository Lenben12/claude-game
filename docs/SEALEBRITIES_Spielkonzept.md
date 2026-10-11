# SEALEBRITIES – Live from the Deep
Spielkonzept · laufend aktualisiert · Stand 10.10.2026

Dieses Dokument sammelt alle Entscheidungen. Jede neue Antwort aus den Frage-Runden wird hier eingetragen.

---

## 1. Kurzbeschreibung
Co-op-Tauch-Vlog-Spiel (1–4 Spieler) für Steam, gebaut in Unity. Vorbild ist **Safari-TV** (Mercy Games, Steam-App 5118500, Release geplant Nov. 2026): Dort führt man mit Freunden eine Safari-Agentur, filmt Tiere und Chaos und wird damit berühmt. Bei uns ist man **Taucher auf einer Insel**, filmt das Leben unter Wasser, sammelt Follower, verdient Geld über Videos, Livestreams und Sponsoren und kauft damit bessere Ausrüstung, die per **Helikopter** geliefert wird.

**Ton:** Chaos-Comedy wie Safari-TV. Unter Wasser wird es mit der Tiefe zunehmend gruselig (Grusel-Stufe **Mittel**: Silhouetten, ein paar leuchtende Augen, jeder Schreck endet mit einem Gag).

## 2. Entscheidungsprotokoll

| Thema | Entscheidung |
|---|---|
| Name | SEALEBRITIES – Live from the Deep (kein „TV“ im Namen; Steam-Suche ohne Treffer) |
| Engine / Plattform | Unity 6 (installiert: 6000.5.9f1), URP, später Steam (Windows) |
| Spieler | 1–4, Werte passen sich der Spielerzahl an |
| Start | Ganz kurzes Angel-Tutorial am Steg, dann erste Tauchausrüstung |
| Grusel-Stufe | Mittel |
| Figuren | Bohnen-Taucher mit Glupschaugen hinter der Maske, **mehr Gesichtsausdruck** (größere Grimassen, Mund auch unter Wasser sichtbar) |
| Grafikstil 3D | **Semi-realistisch** (realistischere Tiere und Wasser, Taucher cartoonig) |
| Insel | Tag-/Nacht-Wechsel (morgens hell, abends goldener Sonnenuntergang, nachts Nachttauchgänge) |
| Welt | Insel als Zentrale, Seekarte, einzelne Tauchplätze |
| Gast-Taucher | Ab 1.000 Followern; zahlen viel, müssen beschützt werden |
| Tod von Gästen | Nur „verschollen“ (kein Blut) mit Rettungsfenster, sonst Anwaltsbrief per Heli, Lizenz-Strike, Follower-Verlust |
| Nebengeschäft | Beides: Schmuggel/Wrack-Bergung (Murk-App) und Clickbait-Taktiken mit Strafen |
| Stickstoffnarkose | Nur auf schweren Schwierigkeitsstufen (Halluzinationen ab ~30 m) |
| Tiere ins Tagebuch | Durch **Filmen** |
| Größen | Stufen **Winzig / Normal / Groß / Riesig / Legendär** plus exakte cm-Angabe |
| Belohnung | **Grundwert der Art × fester Größen-Multiplikator** (nicht Größe × Seltenheit) |
| Volles Tagebuch | Schaltet **Sponsoren-Deals** und **neue Tauchplätze** frei |
| Erster 3D-Prototyp | Insel + Boot + Tauchen + Filmen + Tagebuch (Einzelspieler zuerst) |
| Tierarten | ca. 25 im Prototyp, später 80+ |
| Fragen | Jede Runde mindestens 10 Fragen zum Anklicken |
| Perspektive | Ego-Perspektive (wie Safari-TV) |
| Schwimmen | Frei mit leichtem Treiben (WASD, Leertaste hoch, Strg/C runter) |
| Erste Pressluftflasche | ca. 5 Minuten Luft (in der Tiefe schneller leer) |
| Luft leer (Standard) | Ohnmacht, Boot zieht dich hoch, Aufnahmen des Tauchgangs weg, Rettungsgebühr |
| Anreise | Selbst Boot fahren |
| Insel | Mittelgroß mit Dschungelweg, Leuchtturm, Felsen, versteckter Bucht |
| Spieltag | ca. 40 Minuten (Tag + Nacht; Fischmarkt ~1 echte Minute, Lieferung 2–3 Spielstunden ≈ 3–5 echte Minuten) |
| Tiere | Gemischt: kleine fliehen, manche neugierig, Raubtiere rempeln (Luftverlust, Kamera wackelt) |
| Tiermodelle | Erst im Code erzeugt, später durch bessere Modelle ersetzt |
| Filmen | Kamera in der Hand: rechte Maustaste Sucher, linke Maustaste Aufnahme, Mausrad Zoom |
| Handy | **Zentrale für alles:** Tagebuch-App „Sealog“, Shop (bessere Ausrüstung = tiefer tauchen, später U-Boote), Videos, Karte, Einstellungen |
| Sprache | Deutsch + Englisch umschaltbar |
| Spielende | Erst bei **1.000.000 Followern** (danach endlos weiter) |
| Start-Balancing | Kein Livestream am Anfang: billige Kamera, Videos aufnehmen und hochladen. **Livestream ab 1.000 Followern**, Sponsoren danach, Gast-Taucher viel später |
| Livestream (ab 1.000) | Chat reagiert live, Spenden mit Mutproben („Streichel den Aal!“): mehr Risiko, mehr Geld |
| Sponsoren (ab 5.000) | Aufträge mit Bedingungen („Filme 3 Haie diese Woche“, „keine Haie im Video“) |
| Gast-Taucher (ab 25.000) | Buchungen in der TourTrap-App, Gast-Typen mit Eigenschaften (Luftverbrauch, Panik-Neigung, Wünsche) |
| Wetter | Mit Folgen: Sturmtage mit hohen Wellen, schlechter Sicht, schaukelndem Boot – gefährlich, aber spektakuläre Clips |
| Story | Leicht: Onkel Gus vererbt dir die Schrott-Insel, ein Rivalen-Kanal macht Konkurrenz, Rätsel um das versunkene TV-Studio im Abgrund |
| Individualisierung | Anzugfarben und Muster, lustige Hüte und Masken, Gesicht und Mimik |
| Musik | Wechselt je nach Ort (Insel Ukulele/Surf-Rock, Riff entspannt, Tiefe dunkel, Gefahr Spannung) |
| Endgame | Abgrund + Finale-Livestream bei 1 Million Followern |
| Preis | Noch offen |
| Veröffentlichung | Erst Early Access (vorher Steam-Seite und Demo) |

## 3. Spielablauf (ein Spieltag, ca. 40 Min.)
1. **Morgen auf der Insel:** Handy checken (Buchungen, Wetter, Sponsoren), Heli-Pakete am Landeplatz abholen.
2. **Bootsfahrt** zum Tauchplatz (Physik-Boot aus Schrott, später ausbaubar).
3. **Tauchgang (8–12 Min.):** Luft im Blick, Tiere finden und filmen, Gäste beschützen (ab 1.000 Followern).
4. **Zurück zur Insel:** Video auf dem Röhren-TV schneiden, Titel wählen (Ehrlich / Spicy / Clickbait), hochladen.
5. **Über Nacht:** Aufrufe, Follower, Geld, Kommentare.
6. **Shop:** Ausrüstung bestellen, Lieferung per Helikopter.

## 4. Follower-Fortschritt (neu gebalanced, Ziel 1.000.000)
Start bei null mit einer billigen Kamera (Handy im Gefrierbeutel): Videos aufnehmen, auf FinTube hochladen, Follower aufbauen. Kein Livestream am Anfang. Jeder Follower erhöht die Reichweite, deshalb wird es immer schneller.

| Follower | ca. wann | Freischaltung |
|---|---|---|
| 0 | Start | Billige Kamera, Videos hochladen, Hausriff, Angeln für erstes Geld |
| 100 | Tag 1–2 | FinTube zahlt Geld pro Aufruf |
| 1.000 | Tag 4 (~1,5 Std.) | **Livestream** auf KRILL.tv |
| 2.500 | – | Livestream aus dem Wasser (Relay-Boje) |
| 5.000 | – | Erste Sponsoren-Deals (Werbung am Boot) |
| 10.000 | – | Sponsoren auf Ausrüstung, Murk-App (Schmuggel) |
| 25.000 | – | Gast-Taucher (TourTrap) |
| 50.000 | – | Mini-U-Boot, Tiefseegraben |
| 100.000 | – | Rang „Sealebrity“, VIP-Gäste |
| 250.000 | – | Tiefsee-U-Boot |
| 500.000 | – | Doku-Serie |
| 1.000.000 | – | **Finale:** Livestream aus dem Abgrund, Spiel geschafft (danach endlos) |

**Formeln (Prototyp):** Aufrufe = Summe(Clip-Werte) × 45 × Reichweite × Titel-Faktor; Reichweite = 1 + (Follower/400)^0,85; neue Follower = 3 % der Aufrufe; Geld = 8 $ pro 1.000 Aufrufe (ab 100 Followern). Titel: Ehrlich ×1,0, Spicy ×1,3, Clickbait ×1,8 Aufrufe aber nur 60 % Umwandlung in Follower.

**Tauchplätze:** Neuer Platz, wenn die Tagebuch-Seite des vorherigen zu 60 % gefüllt ist und genug Follower da sind (Kelpwald 250, Wrack 1.500, Blauwasser 8.000, Tiefseegraben 50.000). Ausrüstung bestimmt die sichere Tiefe (Flaschen 15 / 25 / 40 / 60 / 100 m).

**Werbeplätze:** Bootsrumpf, Flagge, Sonnenschirm, Außenborder, Neopren-Patch, Pressluftflasche, Stream-Overlay. Deals zahlen pro Tag plus Bonus pro 1.000 Aufrufe, manche mit Bedingungen („keine Haie“, „tauch auf 30 m“).

## 5. Tagebuch (Index aller Tierarten)
- Jede Art hat eine Seite: Name, Tauchplatz, Tiefe, Seltenheit, **größte gefundene Variante (Stufe + cm)**, Grundwert, bester Clip.
- Unentdeckte Arten erscheinen als Silhouette (wie ein Pokédex), sortiert nach Tauchplatz.
- Ein Tier gilt als entdeckt, wenn es lange genug gut im Bild war (Kamera läuft, Tier groß genug im Bild, Abstand passt).
- Neuer Größenrekord: Banner „NEUER REKORD!“ (im Stream: Chat explodiert, Extra-Views).
- Volle Seite eines Tauchplatzes: Sponsoren-Deal + nächster Tauchplatz.

### Größen-System
| Stufe | Anteil beim Spawnen | Multiplikator |
|---|---|---|
| Winzig | 25 % | ×0,5 |
| Normal | 50 % | ×1,0 |
| Groß | 17 % | ×1,6 |
| Riesig | 6 % | ×2,5 |
| Legendär | 2 % | ×4,0 |

**Wert eines Clips = Grundwert der Art × Größen-Multiplikator × Clip-Qualität** (Framing, Licht, Dauer, Ruhe). Mehr Wert = mehr Aufrufe = mehr Geld und Follower.

### Die 25 Arten im Prototyp
| # | Art | Tauchplatz | Größe (cm) | Seltenheit | Grundwert |
|---|---|---|---|---|---|
| 1 | Clownfisch | Hausriff | 6–11 | häufig | 5 |
| 2 | Paletten-Doktorfisch | Hausriff | 15–30 | häufig | 8 |
| 3 | Mondqualle | Hausriff | 10–40 | häufig | 6 |
| 4 | Papageifisch | Hausriff | 30–90 | häufig | 15 |
| 5 | Kaiserfisch | Hausriff | 20–40 | selten | 20 |
| 6 | Feuerfisch | Hausriff | 20–38 | selten | 25 |
| 7 | Blaupunktrochen | Hausriff | 30–70 | selten | 30 |
| 8 | Seepferdchen | Hausriff | 5–20 | sehr selten | 50 |
| 9 | Krake | Hausriff | 30–120 | sehr selten | 70 |
| 10 | Grüne Meeresschildkröte („Gerald“) | Hausriff | 80–150 | selten | 60 |
| 11 | Garibaldi | Kelpwald | 20–35 | häufig | 12 |
| 12 | Sepia | Kelpwald | 20–50 | selten | 40 |
| 13 | Seelöwe („Sir Flapsalot“) | Kelpwald | 150–250 | selten | 80 |
| 14 | Leopardenhai | Kelpwald | 100–180 | selten | 90 |
| 15 | Riesen-Zackenbarsch | Kelpwald | 100–250 | sehr selten | 120 |
| 16 | Muräne | Wrack | 60–250 | selten | 50 |
| 17 | Zackenbarsch | Wrack | 50–250 | selten | 70 |
| 18 | Barrakuda | Wrack | 60–180 | selten | 60 |
| 19 | Hammerhai | Wrack | 200–450 | sehr selten | 250 |
| 20 | Mantarochen | Blauwasser | 300–700 | sehr selten | 300 |
| 21 | Mondfisch | Blauwasser | 100–330 | sehr selten | 200 |
| 22 | Walhai | Blauwasser | 400–1200 | episch | 600 |
| 23 | Anglerfisch („Lumen“) | Tiefe | 10–100 | sehr selten | 150 |
| 24 | Vampirtintenfisch | Tiefe | 10–30 | sehr selten | 120 |
| 25 | Riesenkalmar | Tiefe | 500–1300 | legendär | 1500 |

## 6. Schwierigkeitsstufen
| Stufe | Kern |
|---|---|
| Schnorchler | Sterben unmöglich (Rettungsdrohne), doppelte Luft, Gäste werden nur evakuiert |
| Open Water (Standard) | Ohnmacht statt Tod, Freunde beleben wieder; Gäste können verschollen gehen |
| Advanced | Echter Tod, halbe Ausrüstung bleibt unten; Strömung, Dekompression; **Narkose** |
| Technical | Gasgemische, harte Raubtiere, Charakter geht bei Tod in Rente; **Narkose** |
| Abyss | Permadeath für die ganze Agentur, keine Anzeigen-Hilfen; **Narkose** |

Spielerzahl-Anpassung (Beispiel): 1 Spieler: 2 Gäste, 2 Raubtiere, Einnahmen ×1,3, Drohnen-Buddy · 2: 4 / 3 / ×1,0 · 3: 6 / 4 / ×0,9 · 4: 8 / 5 / ×0,8.

## 7. Tauchplätze und Stimmung
| Tiefe | Ort | Stimmung |
|---|---|---|
| 0–12 m | Insel-Lagune, Hausriff | Postkarte, reine Comedy |
| 10–25 m | Kelpwald | Doku, Rot verschwindet langsam |
| 20–40 m | Wrack „MS Clickbait“ | Unbehagen, große Umrisse, Lampe wird wichtig |
| 30–70 m | Drop-off / Blauwasser | Grusel, Sonne weg, leuchtende Augen |
| 60 m+ | Tiefseegraben | Grusel+, Anglerfisch & Co. (nur U-Boot oder Profi-Gear) |
| 150 m+ | Abgrund | Biolumineszenz, versunkenes TV-Studio, Finale |

## 8. Figuren, Gäste, Kreaturen
- **Spieler:** Bohnenkörper, Riesenaugen hinter der Maske, Entenfuß-Flossen, eigene Anzugfarbe pro Spieler, alberne Hüte. Mehr Mimik.
- **Gäste:** gelbe Leih-Anzüge mit schwarzem Streifen und Namensfähnchen. Typen u. a. Influencerin, Papa mit Schwimmflügeln, Oma, Panik-Kevin, Thrill-Seeker, VIP.
- **Sealebrities:** benannte, wiederkehrende Tier-Stars mit Fans (Gerald die Schildkröte, Sir Flapsalot die Robbe).
- **Grusel-Kreaturen (Mittel):** Anglerfisch mit Glühbirnen-Köder; „Grinner“ (Aal mit Menschengebiss) nur auf höherer Grusel-Stufe.

## 9. Handy „ClamOS“ und Plattformen (alle erfunden)
Chopper Shop (Heli-Lieferung) · FinTube (Videos) · KRILL.tv (Livestream) · FollowFish (Statistik) · TourTrap (Gäste-Buchungen) · Barnacle (Sponsoren, Nachrichten) · Murk (Grey Jobs) · Seekarte · Wetter · Tagebuch.

## 10. Technik-Plan
- Unity 6 + URP, Projektordner `SEALEBRITIES/`.
- Später Co-op: Netcode for GameObjects + Steam-Transport (Steam Datagram Relay), Steam-Lobby mit Freunde-Einladung, Proximity Voice (z. B. Dissonance). Wiederbeleben und Voice-Chat ab dem ersten Co-op-Build (Lehre aus Subnautica 2).
- Unterwasser: tiefenabhängiger Nebel und Farbverlust (Rot zuerst), Lampe bringt Farben zurück, Caustics im Flachwasser.

## 11. Markt und Konkurrenz (Recherche 10.10.2026)
- Safari-TV: 70.000 Wishlists am 13.09.2026, Closed Playtest, Release Nov. 2026, 2–4 Spieler.
- Nächste Verwandte: Content Warning (filmen für Views), Murky Divers, Subnautica 2, How to Fish (Boot-Co-op, 1 Mio. in 2 Tagen), Stream Train (Livestream-Chat bestellt Stunts, ab 16.10.2026), Cave Diving With Friends (ab 05.11.2026).
- Ein Co-op-Spiel mit Tauchen + Vlogging + zahlenden Gästen wurde nicht gefunden.
- Preisrahmen für solche Spiele: 6–10 $, mit mehr Inhalt bis 15–20 $.

## 13. Fragerunde mit 40 Fragen (10.10.2026)

| # | Thema | Entscheidung |
|---|---|---|
| 1 | Tauchgänge pro Tag | Beliebig viele |
| 2 | Luft nachfüllen | Kompressor muss gekauft werden (vorher Flaschentausch per Heli) |
| 3 | Hai-Rempler | Luftverlust + Kamera wackelt |
| 4 | Kamera-Akku | Nein, nur Speicher |
| 5 | Neue Tiergruppen | Wale & Delfine, Bodentiere (Krebse, Seesterne …), Seeschlangen & Exoten, Tiefsee-Monster |
| 6 | Köder | Nur im Schmuggel-/Clickbait-Modus (verboten, Strafen) |
| 7 | Nachttiere | Ja, eigene Nacht-Arten und Nachttauchgänge |
| 8 | Legenden | Ein benanntes legendäres Tier pro Tauchplatz (z. B. „Big Bertha“) |
| 9 | Boot ausbauen | Nur im Shop kaufen |
| 10 | Boot-Schaden | Nur optisch |
| 11 | U-Boot | Frei selbst steuern |
| 12 | Weitere Fahrzeuge/Gadgets | Keine |
| 13 | Insel ausbauen | Nein |
| 14 | Haustier | Nein |
| 15 | NPCs | Onkel Gus, Rivale „Bottom Feeders“, Meeresbiologin Dr. Fin |
| 16 | Zufallsereignisse | Sturm & Unwetter, Fundstücke (Flaschenpost, Schatzkiste, verlorene Kamera) |
| 17 | Kommentare | Ja, lustige Kommentare zu dem, was im Video passiert |
| 18 | Hater | Ja, bei Clickbait und Tierstörung |
| 19 | Rivale | Wöchentliches Follower-Duell |
| 20 | Collabs | Nein. **Im Co-op teilen sich alle Spieler EINEN Kanal und bauen alles gemeinsam auf** |
| 21 | Co-op-Größe | 1–4 Spieler |
| 22 | Rollen | Keine festen Rollen: alle filmen die ganze Zeit, jeder kann alles, gemeinsamer Upload |
| 23 | Voice unter Wasser | Dumpf + Handzeichen, Funk als Upgrade |
| 24 | Teilen | Gemeinsame Kasse, jeder eigene Ausrüstung |
| 25 | Schreckmomente | **Öfter und echter** (Grusel unten deutlich stärker) |
| 26 | Mysterium | „The Big Eye“: nur Schatten und ein riesiges Auge, erst im Finale filmbar |
| 27 | Ohnmacht im Co-op | Freunde können retten; auf schweren Stufen Geist-Kamera bis Tagesende |
| 28 | Versicherung | Ja, auf schweren Stufen |
| 29 | Einnahmen | Forschungsaufträge von Dr. Fin + Fische verkaufen bleibt |
| 30 | Challenges | Keine täglichen/wöchentlichen Herausforderungen |
| 31 | Skills | Nein, nur Ausrüstung zählt |
| 32 | Steam-Erfolge | Ja, viele lustige |
| 33 | Video-Editor | Einfach: Clips wählen + Titel |
| 34 | Fotomodus | Ja |
| 35 | Replay | Echtes Replay der Aufnahmen auf dem Röhren-TV, überspringbar |
| 36 | Tagebuch | Automatischer Eintrag beim Filmen + Benachrichtigung auf dem Handy; Eintrag zeigt 3D-Bild + bestes eigenes Foto |
| 37 | Steuerung | Controller + Steam Deck |
| 38 | Weitere Sprachen | Spanisch, Portugiesisch, Französisch, Italienisch, Russisch, Polnisch |
| 39 | Demo | Erste 3 Spieltage + Hausriff |
| 40 | Trailer | Safari-TV-Stil mit Bannern |

## 14. Zweite Runde mit 40 Fragen zum Spielinhalt (10.10.2026)

| # | Thema | Entscheidung |
|---|---|---|
| 1 | Gerald | Freundschafts-Stufen: erkennt dich wieder, folgt dir, posiert |
| 2 | Flucht scheuer Fische | Mittel |
| 3 | Schwärme | Gemischt je nach Art (Clownfische zu dritt … Sardinen 100+) |
| 4 | Jagdszenen | Ja, aber **erst ab ca. Tag 3** – entspannter Einstieg |
| 5 | Tauchplätze zum Early Access | 5 wie geplant |
| 6 | Höhle | Ja, mit Grotte (Luftblase) und Verirr-Gefahr, Führungsleine |
| 7 | Veränderungen | Nur Tag/Nacht und Wetter |
| 8 | Strömung | An bestimmten Plätzen (Blauwasser-Kante, Wrack) |
| 9 | Fundorte | Wracks & Geheimnisse (U-Boot, Flugzeugwrack, TV-Studio) |
| 10 | Flaschenpost | Ab ca. Tag 3 an einem **zufälligen Ort** auffindbar; Nachricht über die verschwundene Tiefsee-Doku mit Hinweisen, wo das TV-Studio liegt und wie man es findet |
| 11 | Schreck-Auslöser | Zufällig + feste Story-Orte |
| 12 | Dunkelheit | Gefährlich: Lampe aus lockt Kreaturen an |
| 13 | Fokus | Automatisch |
| 14 | Selfie-Modus | Ja, mit Reaktions-Bonus |
| 15 | Views-Bringer | Am meisten: Gefahr und Nahbegegnungen (Leben riskieren für die Zuschauer); lustige Pannen bringen auch viel |
| 16 | Speicher Start-Kamera | 90 Sekunden |
| 17 | Upgrade-Tempo | Etwa ein Upgrade pro Spieltag |
| 18 | Gebrauchtmarkt | Nein |
| 19 | Ausrüstung verkaufen | Ja, zum halben Preis |
| 20 | Heli-Pannen | Ja, ab und zu (Wasser, Hüttendach, Palme) |
| 21 | Onkel Gus | Im Ruhestand, meldet sich per Funk |
| 22 | Rivalen „Bottom Feeders“ | Angeber-Brüder mit Luxusyacht |
| 23 | Dr. Fin | Forschungsaufträge per Handy-App |
| 24 | Ende | Finale-Livestream + **viele verschiedene Enden** je nach Verhalten, Tempo und Spielweise |
| 25 | Charakter-Editor | Kurz beim Start, später im Spind änderbar |
| 26 | Figuren-Stil | Bohnen-Körper mit Glupschaugen |
| 27 | Gesichter sichtbar | Selfie-Modus, Co-op, Spiegel |
| 28 | Kosmetik | Über Meilensteine + Tagebuch |
| 29 | An Land | Replay, Ausrüstung, Insel erkunden |
| 30 | Sammelobjekte | Ja, optional (nicht nötig für die Story), mit Steam-Erfolgen; plus **ein Secret** auf der Insel |
| 31 | Hunger | **Survival-Elemente: essen und trinken** |
| 32 | Schlafen | Erst ab einer bestimmten Uhrzeit möglich; wer zu lange nicht schläft, wird müde (schlechtere Sicht, schwere Augenlider, weniger erkunden); **im Co-op: schläft einer, schlafen alle** |
| 33 | Umweltgefahren | Strömung, Kelp-Verheddern, Wrack-Einsturz |
| 34 | Deko-Stopp | Nur auf schweren Stufen |
| 35 | Quallen | Kurzer Schmerz + lustige Reaktion, kein echter Schaden |
| 36 | Raubtiere | **Greifen ohne Warnung an** |
| 37 | HUD | Klassisches HUD |
| 38 | Entdeckungs-Sound | Jingle + Handy-Ping |
| 39 | Ladebildschirme | Echte Meeres-Fakten + Tipps |
| 40 | Meeresschutz | Leicht und lustig: Rücksicht bringt Fans, Tierstörung bringt Hater |

## 15. Neuer Grafikstil + 40 Fragen zu Design und UI (11.10.2026)

**Grafikstil geändert:** Der semi-realistische Look ist raus („zu detailliert, zu realistisch, sieht nach KI aus“). Neu: **Low Poly wie How to Fish und Safari-TV**: kantige, flach schattierte Dreiecke, einfarbige Flächen ohne Fototexturen, helle Pastellfarben, kantiges türkises Wasser, eckige Palmen mit rotbraunen Stämmen, Bohnen-Figuren mit Glupschaugen.

| # | Thema | Entscheidung |
|---|---|---|
| 1 | Menü-Hintergrund | Die Taucher-Crew sitzt am Steg |
| 2 | Logo | Wasser-/Blasen-Schrift |
| 3 | HUD-Anordnung | **Geld + Follower unten rechts**, **Tauch-Anzeige (Ort/Tiefe) oben links**, Tag/Uhrzeit oben rechts bleibt; **keine dunkelblauen Kasten-Hintergründe** mehr |
| 4 | Menü-Stimmung | Ukulele & Strand |
| 5 | HUD-Stil | Freie Schrift mit Schatten/Umriss, ohne Kästen |
| 6 | Aufgabe | Nur kurz einblenden, per Taste wieder anzeigen |
| 7 | Fadenkreuz | Kleiner Punkt immer |
| 8 | Geld/Follower | Zwei Zeilen mit Icon, Zuwachs ploppt kurz auf |
| 9 | Luft | Ring mit Restzeit, rot blinkend bei wenig Luft |
| 10 | Tauch-Info oben links | Ort + Tiefe + Max-Tiefe |
| 11 | Warnungen | Roter Bildschirmrand + Piepen |
| 12 | Clip-Zähler | Kamera-Icon mit Zahl (grün gesichert, orange ungesichert) |
| 13 | Handy-Ort | **Im 3D-Bild in der Hand**, eigene Hand sichtbar |
| 14 | Handy offen | Welt läuft weiter |
| 15 | Handy-Look | Altes verkratztes Handy, später upgradebar |
| 16 | App-Icons | Flach & bunt |
| 17 | Tagebuch | Alle Tiere als **graue Umrisse**; erst nach dem Finden farbig mit Name, größter Größe und Größen-Farbe |
| 18 | Unentdeckt | Silhouette |
| 19 | Detailseite | Rekord-Balken + Steckbrief |
| 20 | Neuer Eintrag | Handy-Popup oben + Ping |
| 21 | Hände | Neopren mit bunter Manschette |
| 22 | Lampe | Nur in der Hand, wenn eingeschaltet |
| 23 | Kamera-Modell | Kleiner als bisher |
| 24 | Sucher | Camcorder-Rahmen mit REC |
| 25 | Wasserfarbe flach | Helles Türkis |
| 26 | Sichtweite flach | Weit, ca. 40 m |
| 27 | Lichtstrahlen | Deutlich |
| 28 | Oberfläche von unten | Hell glitzernd, kantig |
| 29 | Nacht | Heller Mondlicht-Look |
| 30 | Sterne | Viele + Mond |
| 31 | Tiefsee-Leuchten | Viele bunte Partikel |
| 32 | Lampenlicht | Warmes Gelb |
| 33 | Insel | **Komplett überarbeiten:** viel mehr zu erkunden, viele Sammelobjekte und Secrets, mehr Dschungel, Leuchtturm, Bucht, schönerer Steg, viel mehr Details |
| 34 | Tauchplätze | **Auf mehrere kleine Inseln verteilt** (jede Site hat eine Mini-Insel als Landmarke) |
| 35 | Seekarte | Gezeichnete Draufsicht |
| 36 | Markierung | Bojen + Wegpunkt im HUD |
| 37 | Schrift | Verspielt rund |
| 38 | UI-Farben | Hell & sonnig (Weiß, Sand, Türkis, Koralle) |
| 39 | Namen im Co-op | Ja, mit Lautsprecher-Symbol |
| 40 | Ladebildschirm | Unterwasser-Animation mit Fakten und Tipps |

## 16. Spieltest-Feedback Testversion 2 + Nachtschicht (11.10.2026)
Feedback zur Testversion 2 (alle 22 Punkte gehen in die Nachtschicht):
- Wirtschaft: Fische bringen weniger Geld, Fische werden in der Handy-App „Fischmarkt“ verkauft, Preise höher, langsamere Wirtschaft.
- Angel: Wurf-Animation.
- Hotbar wie How to Fish: rechte Hand und linke Hand mit je 3 Slots, die Angel ist zweihändig, Inventar mit Drag & Drop.
- Kamera: nur noch Hotbar-Item, scannt nur noch, was richtig im Bild ist.
- Luft: erste Flasche ca. 90 s, Auffüllen nur an Land oder auf dem Boot.
- Lieferung am selben Tag (nach 2–3 Spielstunden).
- Boot startet nur am Steuer.
- Fehler: Steg-Leiter, Kollisionen von Dach und Reifenschaukel.
- Wasser: hell, aber begrenzte Sicht.
- Neu: Galerie.
- Nur 3 Schwierigkeiten: Entspannt, Normal, Schwer.
- Weniger Text, mehr Symbole.
- Start-Trailer, aufgeräumtes Menü.
- Einstellungen: Lautstärken, FPS, Grafikstufen, Tastenbelegung, Voice-Modus.

Zusätzlicher Auftrag für die Nacht:
- **Komplett spielbar** von Tag 1 bis 1 Mio. Follower und Finale.
- **Multiplayer schon testbar:**
  - Koop für 1–4 Spieler mit gemeinsamem Kanal.
  - Host/Join per IP, LAN-Liste, Sprachchat in der Nähe, unter Wasser gedämpft.
  - Steam-Anbindung folgt später.
- **Grafik genau wie How to Fish:** verbindlicher Stil-Leitfaden in `docs/ArtBible_HowToFish.md`.
- **Morgen:** Testbericht (`docs/Testbericht_Nacht_1.md`), Testversion 3 und **100 Fragen**.

## 12. Offene Punkte
Werden in den nächsten Frage-Runden geklärt.
