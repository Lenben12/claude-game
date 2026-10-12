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

## 17. 100 Fragen nach der Nachtschicht (11.10.2026)
Antworten des Spielers auf `docs/Fragen_100_Nacht1.md`:

| # | Thema | Antwort |
|---|---|---|
| 1 | Nächste Grafik-Runde | **Hände und Gegenstände** zuerst (kleiner, heller, wie How to Fish) |
| 2 | Hände | Überall Neopren-Handschuhe |
| 3 | Körniges Wandrauschen | **Effekt (SSAO) ganz ausschalten** |
| 4 | Meerfarbe | **Breite türkise Lagune** um die Insel, erst weiter draußen blau |
| 5 | Insel | Mehr Leben: Vögel, Krabben, wehende Palmen |
| 6 | Orientierung | Wegweiser + Seekarte |
| 7 | Schnellreise | Nein, aber Abkürzungen |
| 8 | Schlucht | Leitern an beiden Enden, unten etwas Verstecktes |
| 9 | Eintauchen | Kurzer Platscher + Blasen |
| 10 | Tiefsee | **Etwas Restlicht überall** (Boden und Umrisse ohne Lampe erkennbar) |
| 11 | Meeresboden | Felsen, Spalten, Sandrippen, feinere Dreiecke |
| 12 | Geräusche | Atem, Blasen, Tierlaute (Atem schneller bei wenig Luft) |
| 13 | Tierfarben in der Tiefe | Leicht leuchtende Farben |
| 14 | Tiermenge | So lassen |
| 15 | Neue Tiergruppen | **Alle** (Krebse/Seesterne, Wale/Delfine, Tiefsee-Monster) |
| 16 | Tier-Unfug | Ab und zu, lustig |
| 17 | Hotbar wechseln | Zahlen + Mausrad |
| 18 | Leere Hände | So lassen (an Land unsichtbar) |
| 19 | Controller | Vor dem Early Access |
| 20 | Auto-Kamera | Ja, beim Sprung ins Wasser automatisch |
| 21 | Startgeld / Einstieg | Umsetzen (ca. 10 Minuten bis zur ersten Flasche). **Neu: Ein lustig aussehender Einführungs-NPC** (ein Mann) redet am Anfang kurz mit dir, führt dich schnell ins Spiel ein und gibt die Aufgaben. Danach kommt das Angeln. Er erklärt auch, dass man Geld verdient, wenn man live geht und überträgt, was man macht. Das Ganze soll richtig lustig sein. |
| 22 | Einholen | Geschicklichkeitsspiel mit Spannungsleiste |
| 23 | Angelplätze | Vom Boot auf offener See (größere, wertvollere Fische) |
| 24 | Bessere Ruten | **Alle 3:** weiter werfen + Seltenes, schneller, Angel-Aufträge von Dr. Fin |
| 25 | Schnorcheln | Erlaubt (20 s, an der Oberfläche schnell wieder voll) |
| 26 | Zu tief | Luft schneller leer + Warnung (wie jetzt) |
| 27 | Kompressor | Hütte füllt gratis, das Boot erst nach Kauf eines Boot-Kompressors |
| 28 | Flaschen | Nur Vorteile |
| 29 | Scan-Regeln | Passt so |
| 30 | Sucher-Hinweise | Symbol + 1–2 Wörter |
| 31 | Fotos | Foto als Vorschaubild fürs FinTube-Video (mehr Aufrufe) |
| 32 | Kamera-Upgrades | Mehr Zoom + Nachtsicht |
| 33 | Handy-Größe | Groß wie jetzt |
| 34 | Handy-Upgrade | Neue Apps + neuer Look |
| 35 | Handy nass | Nur an Land und auf dem Boot |
| 36 | Nächste App | Wetter-App |
| 37 | Meldungen | Max. 1 Karte + 2 Texte gleichzeitig |
| 38 | Banner-Zeile | Auf hellem Band |
| 39 | Hotbar-Anzeige | **Immer sichtbar** (wie How to Fish) |
| 40 | Richtung zum Boot | **Tauchcomputer am Arm** (Richtung + Tiefe) |
| 41 | Menü-Licht | Wärmer, tiefe Sonne (orange-golden) |
| 42 | Intro | Nur beim ersten Start, danach im Menü abrufbar |
| 43 | Intro unter Wasser | Gerald + bunter Fischschwarm |
| 44 | Menü-Crew | Du + deine zuletzt gespielten Koop-Freunde |
| 45 | Erstes Geld | Etwas Geld schon ab Video 1 |
| 46 | Videos pro Tag | Ein Video pro Tag |
| 47 | Preise | Passt, das Spiel soll lang sein (ca. 35–45 h) |
| 48 | Koop-Tempo | Koop leicht schneller (4 Spieler ca. 1,3×) |
| 49 | Freischalten | Passt (Kelp 300, Wrack 2.500, Blauwasser 15.000, Graben 100.000 + 60 % Sealog) |
| 50 | Ränge | Kanal-Ränge mit Abzeichen |
| 51 | Ausrüstung an der Figur | Sichtbar |
| 52 | Sealog 100 % | Goldene Kamera als Trophäe in der Hütte + Steam-Erfolg |
| 53 | Nächster Platz-Ausbau | Wrack „MS Clickbait“ (Innenräume, Fundstücke, Grusel-Ecken) |
| 54 | Mini-Inseln | Geheimnis + Rastplatz (Luft auffüllen) |
| 55 | Abgrund | Nur mit dem U-Boot |
| 56 | Koop-Stellen | Optionale Stellen für zwei, allein gibt es einen Umweg |
| 57 | Story-Menge | **Beides kombinieren:** Funk je Meilenstein (kurz, witzig) **und** Story-Missionen von Gus |
| 58 | Rivalen | Erster Besuch bei 1.000 Followern, dann passiert noch nicht viel. **Später im Spiel kommen sie öfter wieder und bringen eine neue Story-Linie mit.** |
| 59 | Besuch auf der Insel | Ja, ab und zu legen Figuren am Steg an |
| 60 | Stimmen | Brabbel-Stimmen mit Untertiteln |
| 61 | Menge Sammelstücke | Etwa doppelt so viele (auch unter Wasser und auf den Mini-Inseln) |
| 62 | Sammelstücke finden | Glitzern aus der Nähe + Zähler im Handy |
| 63 | Belohnung | Story + Kosmetik je Set (alle Muscheln = goldener Hut, alle Logbuch-Seiten = ganze Gus-Geschichte) |
| 64 | TV-Studio-Rätsel | Mittel, mit Hinweisen in den Logbuch-Seiten |
| 65 | Koop-Rettung | Bewusstlosen Freund zur Oberfläche schleppen, er behält die Clips |
| 66 | Host verlässt | Warnung „Alle werden getrennt“ mit Bestätigung |
| 67 | Gast-Stand | **Gäste behalten alles**, wenn der Spielstand wieder geladen wird (Host-Save speichert die Daten pro Gast) |
| 68 | Mehr Spieler | **Mehr Gefahren** (mehr Raubtiere, zusammen wird es härter) |
| 69 | Voice-Start | Offenes Mikrofon als Standard |
| 70 | Hörweite unter Wasser | Nur nah, ca. 10 m; das Funk-Upgrade hebt das auf |
| 71 | Funk-Upgrade | Ab dem Wrack, ca. 2.500 $ |
| 72 | Text-Chat | **Nein** |
| 73 | Hunger | Sanft: Bonus statt Strafe |
| 74 | Essen | **Beides:** Fische grillen + Kokosnüsse **und** Snacks per Heli |
| 75 | Schlafen | Ab 18 Uhr |
| 76 | Tageslänge | 40 Minuten |
| 77 | Finale | Geskriptetes Spektakel (Big Eye, Live-Chat, Gags, ca. 10 Min.) |
| 78 | Enden | 3 Enden zum Early-Access-Start |
| 79 | Nach dem Finale | **Beides:** Legenden + Rekordjagd **und** härterer Durchgang. **„Schwer“ ist erst freigeschaltet, wenn man „Normal“ durchgespielt hat** – dann das ganze Spiel noch einmal, viel, viel anspruchsvoller. Am Anfang nur „Entspannt“ und „Normal“ wählbar. |
| 80 | Abspann | Dein Best-of (beste Clips und Fotos) als Abspann |
| 81 | Livestream | Live tauchen vom Boot, Chat reagiert sofort, Spenden live |
| 82 | Live-Chat | Klein am Bildrand, kurze Sprechblasen |
| 83 | Mutproben | **Echt riskant** (tiefer als erlaubt, Hai anstupsen – viel Geld) |
| 84 | Ohnmacht im Stream | **Stream bricht ab, Follower gehen verloren** |
| 85 | Gast-Taucher | Nach Livestream + Wetter |
| 86 | Gast-KI | Folgen dir grob, machen Quatsch |
| 87 | Gast-Clips | Ja, wackelige Gast-Clips |
| 88 | Gast-Bewertung | Sterne + lustiger Kommentar |
| 89 | Sponsor-Geld | Grundgeld pro Seite + Aufträge |
| 90 | Sponsor-Logos | Fantasie-Logos sichtbar an Boot und Anzug |
| 91 | Regelbruch | Kein Geld + Meckermail, Deal endet |
| 92 | Anzahl Sponsoren | Bis zu 3, Angebote per Nachricht |
| 93 | Steam-Seite | Nach dem Grafik-Feinschliff |
| 94 | Steam-Erfolge im Koop | **Für alle Anwesenden** |
| 95 | Preis Early Access | **9,99 €** |
| 96 | Erste Steam-Funktion | Freunde einladen |
| 97 | Grafikstufe beim Start | Mittel bei Grafik im Prozessor |
| 98 | Sichtfeld | Regler 60–100° |
| 99 | Kopfwippen/Wackeln | Abschaltbar per Schalter |
| 100 | Test-Abkürzungen | **Sofort entfernen** (Strg+Umschalt+F12 / F5–F10) |

## 18. Feedback nach dem Spielen von Testversion 3 (11.10.2026)
- **Insel kleiner:** ca. 70–75 % der jetzigen Größe, kürzere Wege, alle Orte bleiben erhalten.
- **Koop mit einem Freund an einem anderen PC**, ohne das Spiel auf Steam zu veröffentlichen:
  - Steam-Test mit der Test-ID 480, Einladen über die Freundesliste.
  - Als Ausweichweg IP oder VPN.
- **Charakter-Bildschirm übersichtlicher:** Man sieht seinen Taucher live in 3D, während man Farben, Hut, Gesicht usw. ändert.
- **Bug:** Taschenlampe über dem Steg, der Steg ist durch die Lampe sichtbar. Gegenstände in der Hand dürfen nie in die Welt eintauchen.
- **Bug:** Beim Ändern der Lautstärke steht manchmal eine Milliardenzahl. Alle Regler werden begrenzt und sauber formatiert.
- **„Zur Hütte“ teleportieren** bringt auch das Boot zurück an den Steg.

**Zweite Feedback-Nachricht (11.10.2026):**
- **Geld kommt viel zu langsam:** Der Einstieg soll spürbar schneller Geld bringen.
- **Filmen neu (ersetzt die strengen Scan-Regeln aus Frage 29):**
  - Unter Wasser kannst du einfach die ganze Zeit filmen, ohne auf einen Fisch zu fokussieren.
  - Alle Tiere im Bild werden automatisch erkannt. Größe und Art bestimmen, wie viel das Video bringt.
  - **Für das Tagebuch (Sealog)** zoomst du auf ein Tier und machst ein kurzes **Foto**.
- **Unter Wasser soll es mehr zu tun geben.** Im Moment ist dort zu wenig los.
- **Selfie-Ansicht:** Mit der Taste **H** kannst du dich selbst anschauen.
- **Mehr Story-NPCs:**
  - Sie erklären, warum du auf der Insel bist und was du dort alles tun musst.
  - Vor dem eigentlichen Spielstart redest du mit einem NPC, der dir die Aufgaben gibt.
- **Entspannte Hintergrundmusik.**
- **Sponsoren (ersetzt Frage 92 „bis zu 3“):**
  - Sponsoren geben Aufgaben. Du kannst **maximal 2 Deals gleichzeitig** annehmen.
  - Die Deals dürfen sich **nicht überschneiden**: Hat ein Deal mit Haien zu tun, kannst du keinen zweiten Hai-Deal annehmen, auch wenn er mehr zahlt. Dafür musst du den ersten zuerst kündigen.
- **Gestrandeter NPC (später im Spiel, nicht von Anfang an):**
  - Er wird irgendwann an einem Strand angeschwemmt. Du musst ihm helfen.
  - Zur Belohnung gibt er dir eine **Karte zu einer geheimen Insel**. Dort gibt es seltene Fischarten, die es sonst nirgends gibt.

**Dritte Nachricht, nach dem Koop-Test mit einem Freund (11.10.2026):**
- **Fische komplett Open World:**
  - Tiere können überall im Meer zufällig erscheinen. **Die Tiefe bestimmt, welche Arten** man trifft.
  - An jedem Riff spawnen nur dessen Arten, freigeschaltet je nach Spielfortschritt. Die Tiefe filtert trotzdem mit.
  - Überall Fische.
- **Sealog:** zeigt, wie oft man ein Tier gefunden hat und in welcher Tiefe es lebt.
- **Korallenriffe** überall viel schöner gestalten.
- **FinTube:**
  - Den Videotitel selbst eintippen, dazu Vorschläge.
  - Das Vorschaubild zeigt **mehrere Tiere** als Collage.
- **Insel noch kleiner** (ca. 60–65 % der Breite von Testversion 3, ersetzt den 0,75-Plan) und **viel detaillierter**.
- **Geheimnisse viel besser verstecken.** Bisher findet man sie einfach beim Herumlaufen.
- **Mehr Nebenaufgaben** und NPCs, die sie vergeben.
- **Animationen:** coolere Lauf-Animationen und eine echte **Leiter-Kletter-Animation**.
- **Schlafen im Koop (ersetzt „einer schläft = alle schlafen“):**
  - Alle Spieler müssen am Bett sein, Anzeige **„1/2 · 2/2“** wie in Minecraft.
- **Aufgaben** immer **oben in der Mitte** anzeigen, klein und aufgeräumt (ersetzt „nur kurz“).
- **Tiere auf der Insel:** Vögel, Krabben, Echsen, evtl. Ziegen, Schweine oder Affen.
- **Nachrichten:** viel übersichtlicher, Symbol und eine kurze Zeile.
- **Dorf einer seit Generationen ansässigen Inselgemeinschaft:**
  - Der Spieler sprach von einer „alten Indianerbande“.
  - Umsetzung **respektvoll** als erfundene Inselkultur mit eigenen Bräuchen, Pfahlhäusern, Booten und Handwerk.
  - Ohne Klischees wie Federschmuck, Kriegsbemalung oder nachgeäffte Sprache.
  - Die Bewohner vergeben Nebenaufgaben und geben Story-Hinweise.
- **Chopper-Shop schöner gestalten.**
- **Pannen:** Der Heli wirft das Paket mal an der falschen Stelle ab, dann muss man es auf der Insel suchen.
- **Figuren viel schöner und weicher**, nicht so klotzig. Auch Hände, Kamera und alle Gegenstände schöner.
- **Danach** wieder neue Fragen stellen.

## 19. Feedback zur Vorschau von Testversion 4 (11.10.2026, für Testversion 5)
Der Spieler hat die Vorschau mit einem Freund im Koop getestet. Umsetzung als Nachtschicht nach Testversion 4, mit **einzelnen Agenten nacheinander** (kein Workflow). Ziel: fertige Testversion 5 bis zum nächsten Morgen (10 Uhr oder später) und danach **40 Fragen**.

**Bugs und Korrekturen**
- **Schlafen:**
  - Die Figur liegt wirklich in der Hängematte oder im Bett, nicht schwebend daneben oder auf dem Boden (Screenshots `images/4.png`, `images/5.png`).
  - Schlafen geht im Bett oder in der Hängematte.
- **Kollisionen:** Durch feste Objekte darf man nirgends laufen.
- **Leiter:** Die Kletteranimation ist noch verbuggt. Am Steg klettert man selbst hoch.
- **Fehlerhafte Objekte:** schwebende Surfbretter, falsche bzw. verbuggte Schilder. Die Badeinsel weiter ins Meer setzen.
- **Neuer Tauchplatz:** wird nicht angekündigt, bevor die Anforderungen erfüllt sind. Die Anforderungen stehen oben in der Mitte.
- **Nebenaufgaben** sind teilweise stark verbuggt.
- **Kamera:**
  - Beim Betreten des Wassers startet **keine** automatische Aufnahme. Aufnahme nur selbst per Linksklick.
  - Den Viereck-Rahmen beim Zielen (Rechtsklick, Sealog) erst zeigen, wenn man nah genug am Tier ist. Keine Tiere von zu weit weg.
  - Das Fragezeichen bei unentdeckten Arten ist gut und bleibt.
- **Boot:**
  - Nur der Fahrer kann den Anker legen.
  - Ein zweiter Sitzplatz, z. B. eine Sitzbank vorne.
  - Während der Fahrt hat man Zugriff auf die Karte und die Markierungen.

**Meer und Tiere**
- **Viel mehr Fische und Inhalte im Meer:** Es soll nie leer sein, überall Tiere. Manche Gebiete erreicht man erst ab einer bestimmten Stufe.
- **Mehr Ideen**, was man im Meer machen kann, dazu ein paar **Memes**.
- **Tiere reagieren:** Sie fliehen, wenn man sich nähert, im Meer und auf der Insel.
- **Jede Insel sieht individuell aus**, z. B. eine Vulkaninsel nur mit Urzeit-Tieren.
- **Waffen gegen Haie und Monster:** Speere und andere Unterwasser-Waffen (kommt bei den Live-Zuschauern gut an).
  - Bessere Speere kann man kaufen.
  - Mit einem Schleifgerät und Steinen kann man sie selbst schärfen. Die Technik lernt man im Einsiedler-Lager.

**Überleben**
- **Hunger und Durst als echte Bedürfnisse:**
  - Meerwasser zu Hause filtern.
  - Essen am Lagerfeuer oder auf dem Grill braten.

**Insel und Welt**
- Die Karte insgesamt umgestalten: schöner, „realistischer“, übersichtlich, aber mit Geheimnissen zum Suchen.
- Realistischere Wolken.
- Das Haus innen schöner, und man kann es individueller einrichten.
- **Die Dorf-Häuser entfernen.**
- **Stattdessen ein verstecktes Lager im Dschungel auf der anderen Inselseite**, das man am Anfang gar nicht kennt:
  - ein paar Zelte, Einsiedler, die sich dort vor langer Zeit niedergelassen haben
  - Der Spieler nannte es „Indianerdorf“.
  - Umsetzung als Lager von Gestrandeten bzw. Einsiedlern **ohne Bezug zu einer realen Volksgruppe**, keine Klischees.
  - Stimmungen: friedlich oder aggressiv. Nur in guter Stimmung kann man mit ihnen reden.
  - Verständigung per **Zeichensprache**, weil sie die eigene Sprache nicht sprechen.
  - Sie können angreifen. Man wehrt sich mit einem **Stock** (Slapstick, nicht tödlich).
  - Man kann nicht jeden ansprechen. Kein komischer Arm-Gruß.
- **Kartenstücke** überall auf der Insel, teils vergraben. Erst mit allen Teilen öffnet sich die Karte zu einem Geheimnis.

**Figuren und Gegenstände**
- Figuren schöner und **„realistischer“**: weder klotzig noch zu kugelig. Unsere eigene Figur auch.
- Mehr Optionen beim Anpassen.
- Hände und alle Gegenstände in der Hand sehen besser aus.

**Bedienung und Oberfläche**
- **Inventar als Rucksack:**
  - Aufgesammeltes landet wirklich im Rucksack, den man öffnen kann.
  - Die Leiste wird **ein großer Balken statt 3 + 3**.
  - Im Rucksack wählt man, was in Erst- und was in Zweithand ist.
- **Karte im Handy:** zoomen und verschieben.
- **Chopper-Shop:** schöner, jedes Item mit eigenem Bild.
- **FinTube-Studio und Handy:** mehr Funktionen.
- **Wetter-App:** zusätzlich Temperaturen.
- **Galerie:** Bilder nur im Spiel speichern, mit Export auf den PC.
- **Ladebildschirm** viel besser gestalten.
- **Kameramodus** übersichtlicher.
- **NPCs:** mehr Möglichkeiten zur Interaktion.
- **Story:** muss mehr Sinn ergeben.
- **Fischmarkt:** bleibt so.

## 19b. Nachtrag Koop und Spielstände (12.10.2026, für Testversion 5)
- **Einladen:** Man kann eine Person **mehrmals einladen**, wenn sie die erste Einladung nicht angenommen hat. Der Knopf heißt dabei **immer gleich „Einladen“**, also kein „Erneut einladen“ und keine andere Anzeige. Er bleibt einfach jederzeit klickbar.
- **4 Spielstände (Slots):**
  - Jeder kann gelöscht werden.
  - Jede Karte zeigt z. B. die **Spielzeit in dieser Welt**, die **Erfolge in dieser Welt**, Tag, Follower und Geld.
  - Alles hängt am jeweiligen Spielstand und ist nicht kontoweit. Erfolge, Fortschritt und Fotos sind je Welt getrennt.
- **Koop:**
  - Nur der Ersteller des Spielstands (Host) kann einladen.
  - Gespeichert wird nur beim Host.
  - Verlässt der Host, fliegen alle aus der Welt.
- **Bilder:**
  - Sie werden **nicht** in der PC-Galerie bzw. im Bilder-Ordner gespeichert, sondern nur im Spiel, und zwar je Spielstand.
  - Export auf den PC nur auf ausdrücklichen Knopfdruck.

## 20. 50 Zwischenfragen in der Nacht (12.10.2026, für Testversion 5)
| # | Thema | Antwort |
|---|---|---|
| 1 | Inselgröße | Klein und dicht: ca. 2–3 Minuten quer drüber, überall etwas zu entdecken |
| 2 | Lager-Bewohner | **Aussteiger-Einsiedler**, die das Meer verehren und Fremden misstrauen |
| 3 | Anzahl | 4–6 mit eigenen Rollen (Anführer, Schmied, Jäger, Koch, Späher …) |
| 4 | Laune verbessern | Geschenke + richtige Gesten |
| 5 | Zeichensprache | Gesten-Rad mit Symbolen (falsche Gesten verärgern) |
| 6 | Niederlage gegen Einsiedler | **Sie fesseln dich kurz**, du befreist dich per Minispiel |
| 7 | Sieg mit dem Stock | **Er respektiert dich** (Stärke bringt Respekt und bessere Laune) |
| 8 | Was sie geben | Handwerk (Speere schärfen, Fischfallen, Rezepte) |
| 9 | Story-Kern | Onkel Gus' Erbe (Insel, Hütte, Kanal + Rätsel der verschollenen Doku) |
| 10 | Horst | Taucht immer wieder auf (Tipps, Kommentare, eigene Nebenaufgaben), **aber nicht am Steg, sondern in einer Hängematte am Strand** (am Steg nervt er) |
| 11 | Nebenaufgaben | **Lustige Filmaufträge** |
| 12 | Geheimnisse | Mittel mit Hinweisen (klettern/tauchen, Tipps aus Logbuch und von NPCs) |
| 13 | Vulkaninsel | Erreichbar ab der Wrack-Freischaltung |
| 14 | Urzeit-Tiere | Mischung: echte lebende Fossilien + ein großes Urzeit-Monster |
| 15 | Fischmenge | Viele, überall Leben, auch im offenen Meer |
| 16 | Angriffe | Selten, aber spannend, angekündigt durch Musik oder Kreisen |
| 17 | Hai-Treffer | Luft- und Clip-Verlust, kein Tod |
| 18 | Speerfischen | Nur Speisefische, geschützte Tiere nie |
| 19 | Memes | Lustige Fundstücke + Tier-Memes |
| 20 | Unterwasser zusätzlich | Schätze & Wracks erkunden |
| 21 | Flucht | Je nach Art (kleine Fische schnell weg, Schildkröten neugierig, Haie cool) |
| 22 | Inseltiere | Vögel, Krabben, Echsen |
| 23 | Landtiere im Sealog | **Nein, nur Deko** |
| 24 | Hunger/Durst | **Schneller** sinken (mehr Survival-Gefühl) |
| 25 | Wasser filtern | Ca. 45 Spielminuten |
| 26 | Lagerfeuer | Feste Plätze + Treibholz |
| 27 | Rucksack | **Unbegrenzt** |
| 28 | Hotbar | 8 Plätze |
| 29 | Haltbarkeit | Ja, langsam (schärfen/reparieren am Schleifgerät) |
| 30 | Handy-Karte | Gezeichnete Schatzkarte, zoom- und verschiebbar, Orte erst nach Entdeckung |
| 31 | Markierungen | **Geteilte Koop-Pins** |
| 32 | Chopper-Shop | Katalog mit großen Bildern |
| 33 | FinTube-Studio | Clips auswählen & sortieren + Statistiken & Kommentare |
| 34 | Neue App | Kochbuch |
| 35 | Ladebildschirm | Animierte Unterwasser-Szene mit Tipps und Fakten |
| 36 | Beifahrer | Filmen + Karte ansehen |
| 37 | Boot | **Nur Upgrades**, kein eigenes Design |
| 38 | Figuren | How-to-Fish-Stil, natürlicher (Low-Poly-Menschen mit echten Proportionen) |
| 39 | Weltstil | Low-Poly, aber detaillierter |
| 40 | Kameramodus | Minimal: REC, Zoom + kleine Symbole erkannter Tiere |
| 41 | Sealog-Foto-Reichweite | Bis ca. 8 m |
| 42 | Tauchplätze | Anforderung oben in der Mitte, Banner erst beim Erreichen |
| 43 | Rivalen | Gegenspieler mit Sabotage (Motive klauen, Streams stören, Follower-Duelle) |
| 44 | Nachttauchen | Gruselig, mehr Raubtiere, seltene Nachttiere |
| 45 | Kampf im Livestream | **Jeder Kampf bringt Hype** |
| 46 | Haus einrichten | **Feste Varianten wählen** (Wandfarbe, Boden, Teppich …) |
| 47 | Funde verkaufen | Ja: Fischmarkt + Tausch bei den Einsiedlern |
| 48 | Wetter | Beeinflusst Sicht, Wellen und welche Tiere erscheinen |
| 49 | Wichtigstes für Testversion 5 | **Mehr Inhalt im Meer** |
| 50 | Spielzeit einer Testversion | 2–3 Stunden |

## 12. Offene Punkte
Werden in den nächsten Frage-Runden geklärt.
