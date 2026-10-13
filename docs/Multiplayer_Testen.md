# SEALEBRITIES – Multiplayer (Koop) testen

Koop für 1–4 Taucher. Alle spielen auf **einem gemeinsamen Kanal**: ein Konto, ein Sealog, eine Kühlbox, ein FinTube-Kanal.
Einer ist **Host** (seine Welt, sein Spielstand), die anderen sind **Gäste**. Nur der Host speichert –
aber er speichert auch für seine Gäste mit (Hotbar, Ausrüstungswahl, Statistiken, Erfolge, siehe Abschnitt 6).

---

## 0. Mit einem Freund auf einem anderen PC – über Steam (empfohlen, ohne Veröffentlichung)

Das Spiel muss dafür **nicht** auf Steam veröffentlicht sein. Es benutzt Valves Test-App 480.
Ihr braucht keine IP-Adresse, keinen Port und keine Router-Einstellung. Beide müssen in Steam befreundet sein.

1. **Build-Ordner zippen:** den ganzen Ordner `Builds/SEALEBRITIES` (mit `SEALEBRITIES.exe` und `steam_appid.txt`) als Zip packen
   und dem Freund schicken (z. B. über Google Drive, OneDrive oder WeTransfer).
2. **Freund entpackt** die Zip (Rechtsklick → „Alle extrahieren …“) – nicht direkt aus der Zip starten.
   `steam_appid.txt` muss neben der `SEALEBRITIES.exe` liegen bleiben.
3. **Beide starten Steam** (eingeloggt, online) und dann `SEALEBRITIES.exe`. Der Freund wartet im Hauptmenü.
4. **Host:** Hauptmenü → **Mit Freunden** → bei **Über Steam** auf **Spiel hosten** → **Lobby öffnen & starten**
   (Seite „Über Steam hosten“).
5. **Host im Spiel:** **Esc** → Karte „Steam-Koop“ → **Freunde einladen** (Steam-Einladungsfenster; ohne Steam-Overlay erscheint
   eine Liste im Spiel).
6. **Freund nimmt die Einladung in Steam an** (Steam-Chat, Shift+Tab oder in der Freundesliste „Spiel beitreten“). Das Spiel
   verbindet von selbst („Verbinde über Steam …“).

Wichtig: Steam zeigt bei euch beiden **„Spielt Spacewar“** – das ist die Test-App 480 und völlig normal.
Beide brauchen **genau dieselbe Spielversion** (sonst: „Dein Freund hat eine andere Spielversion.“).
Klappt Steam nicht: Abschnitt 2 / 3 (IP, Heimnetz, Radmin VPN / ZeroTier). Ausführlich: `docs/Steam_Koop_Testen.md`.

---

## 1. Schnelltest: zwei Fenster auf einem PC

Am schnellsten geht es mit dem Starter im Ordner `Builds`:

1. Doppelklick auf **`Builds/Koop-Test_2_Fenster.bat`**.
2. Es öffnen sich zwei Fenster: **Anna** (Host) und **Ben** (Gast).
3. Ben verbindet sich automatisch, sobald Anna im Spiel ist (das dauert bis zu einer Minute).
4. Mit der Maus ins jeweilige Fenster klicken, um es zu steuern.

Optional für mehr Gäste: `Koop-Test_2_Fenster.bat "" 3` öffnet drei Gäste (Ben, Cleo, Dana).

Wichtig:
- Dieses Testspiel wird **nicht gespeichert**. Dein echter Spielstand bleibt unberührt.
- Beide Fenster teilen sich eine Grafikkarte, und Windows bevorzugt das Fenster mit Fokus.
  Im automatischen Test lief das aktive Fenster mit 60 FPS, das andere nur mit etwa 15–30 FPS.
  Mit zwei PCs gibt es das nicht. Für den Test hilft *Einstellungen → Grafik: Niedrig*.
- Das Fenster ohne Fokus läuft weiter, reagiert aber nicht auf Tastatur und Maus.
- Ist Port 27015 auf deinem PC belegt, weicht der Host von selbst auf **Port 27115** aus. Das ist zum Beispiel bei installiertem iTunes / „Apple Devices“ der Fall (dessen Dienst sitzt auf 127.0.0.1:27015).
  Gäste auf demselben PC finden ihn trotzdem automatisch, wenn sie `127.0.0.1` ohne Port eintippen.
  Der Host sieht dann „Host läuft auf Port 27115“.

Von Hand geht es so (in der Eingabeaufforderung im Ordner `Builds/SEALEBRITIES`):

```
SEALEBRITIES.exe -host -netbind 127.0.0.1 -netname Anna -screen-fullscreen 0 -screen-width 1280 -screen-height 720
SEALEBRITIES.exe -join 127.0.0.1 -netname Ben -screen-fullscreen 0 -screen-width 1280 -screen-height 720
```

| Schalter | Wirkung |
|---|---|
| `-host [Port]` | Hostet nach dem Start automatisch und startet ein neues Testspiel (Standard-Port 27015) |
| `-continue` | Mit `-host`: deinen Spielstand fortsetzen statt neu (wird dann normal gespeichert) |
| `-save` | Mit `-host`: das neue Testspiel trotzdem speichern |
| `-join IP[:Port]` | Verbindet automatisch (versucht es 60 s lang erneut, falls der Host noch lädt) |
| `-netname Name` | Name für dieses Fenster (bekommt damit auch einen eigenen Spieler-Schlüssel, siehe Abschnitt 6) |
| `-playerkey X` | Eigener Spieler-Schlüssel für dieses Fenster (Test: Gast-Daten beim Host wiederfinden) |
| `-netbind 127.0.0.1` | Host lauscht nur auf diesem PC, dann kommt keine Firewall-Abfrage |
| `-netdifficulty Normal` | Schwierigkeit für `-host` (Entspannt / Normal / Schwer) |
| `-voiceloopback` | Du hörst dein eigenes Mikrofon (Sprachtest, geht auch allein) |

Automatischer Koop-Test (für Entwickler): `-autoplaytest-mp host <Ordner>` und `-autoplaytest-mp client <Ordner>`.
Die Berichte landen als `mp_host_report.md` und `mp_client_report.md` im Ordner.

---

## 2. Im gleichen Heimnetz (LAN / WLAN)

**Host:**
1. Hauptmenü → **Mit Freunden** → **Spiel hosten**.
2. „Neues Spiel“ oder „Fortsetzen“ wählen.
3. Beim ersten Mal fragt Windows nach der Firewall (siehe Abschnitt 4) → **Zugriff zulassen**.
4. Im Spiel zeigt eine Meldung die Adresse, z. B. „Freunde verbinden sich mit 192.168.1.20“. Die IP steht auch im Pause-Menü.

**Gäste:**
1. Hauptmenü → **Mit Freunden** → **Beitreten**.
2. Unter **„Im Heimnetz gefunden“** erscheint das Spiel des Hosts → **Beitreten**.
3. Falls die Liste leer bleibt: die IP des Hosts oben eintippen (z. B. `192.168.1.20`) → **Verbinden**.

Die Suche im Heimnetz läuft über UDP-Port 27016, das Spiel selbst über TCP-Port 27015.

---

## 3. Über das Internet

Es gibt keinen eigenen Server. Die Gäste verbinden sich direkt mit dem PC des Hosts. Dafür gibt es zwei Wege.

### Weg A: Portweiterleitung am Router (Host)

1. Im Router (z. B. FritzBox: *Internet → Freigaben → Portfreigaben*) eine Freigabe für den PC des Hosts anlegen:
   **TCP-Port 27015** → an die lokale IP des Host-PCs (z. B. 192.168.1.20).
   Meldet das Spiel beim Hosten einen anderen Port (z. B. „Host läuft auf Port 27115“), diesen Port freigeben und den Gästen `IP:27115` geben.
2. Die öffentliche IP herausfinden (z. B. im Router oder über eine „Wie ist meine IP“-Seite).
3. Die Gäste tippen diese IP unter **Mit Freunden → Beitreten** ein, z. B. `85.123.45.67`.
   Bei einem anderen Port: `85.123.45.67:27020`.

Klappt das nicht, hat der Anschluss oft keine eigene öffentliche IPv4-Adresse (DS-Lite / CGNAT, häufig bei Kabel- und Glasfaseranschlüssen). Dann Weg B nehmen.

### Weg B: Virtuelles LAN mit Radmin VPN oder ZeroTier (am einfachsten)

1. **Alle** installieren dasselbe Programm: Radmin VPN oder ZeroTier.
2. Der Host erstellt ein Netzwerk (Radmin: *Netzwerk → Netzwerk erstellen*, Name und Passwort). Die anderen treten mit Name und Passwort bei.
   Bei ZeroTier: der Host legt ein Netzwerk an, die anderen treten mit der Netzwerk-ID bei, und der Host bestätigt sie.
3. Der Host startet **Spiel hosten**.
4. Die Gäste tippen die **VPN-IP des Hosts** ein (Radmin zeigt sie neben dem Namen, z. B. `26.10.0.5`; ZeroTier z. B. `10.147.17.3`).
   Oft taucht das Spiel auch direkt unter „Im Heimnetz gefunden“ auf.

Windows stuft VPN-Netze oft als **öffentlich** ein. Dann bei der Firewall-Abfrage auch „Öffentliche Netzwerke“ erlauben.

---

## 4. Windows-Firewall: „Zugriff zulassen“

Beim ersten Hosten fragt Windows: *„Die Windows Defender Firewall hat einige Features dieser App blockiert.“*

- Haken bei **„Private Netzwerke“** setzen (Heimnetz).
- Für Radmin VPN / ZeroTier auch **„Öffentliche Netzwerke“** anhaken.
- Dann **Zugriff zulassen** klicken.

Falls du aus Versehen „Abbrechen“ geklickt hast:
*Windows-Sicherheit → Firewall- & Netzwerkschutz → „Zugriff von Apps durch die Firewall zulassen“ → Einstellungen ändern → SEALEBRITIES* bei Privat (und ggf. Öffentlich) anhaken.

Beim Test mit zwei Fenstern auf einem PC (`-netbind 127.0.0.1`) kommt keine Abfrage.

---

## 5. Sprachchat

Das Spiel hat eingebauten Näherungs-Sprachchat. Wer spricht, hat ein Lautsprecher-Symbol neben dem Namen über dem Kopf.

| Situation | Reichweite | Klang |
|---|---|---|
| Beide über Wasser | 2–30 m | klar, kommt aus der Richtung des Tauchers |
| Einer (oder beide) unter Wasser | **2–10 m** | gedämpft |
| Mit dem **Unterwasser-Funk** (jeder Taucher hat ihn angelegt, wer über Wasser ist, steht auf dem Boot) | **bis 200 m** | Funk: dünner Klang, leises Rauschen und Knacken |

| Was | Standard | Wo ändern |
|---|---|---|
| Modus | **Offenes Mikrofon** (mit Rauschsperre −45 dB, 0,3 s Nachlauf) | Einstellungen → Audio: Offenes Mikrofon / Push-to-Talk / Aus |
| Push-to-Talk-Taste | **V** gedrückt halten | Einstellungen → Steuerung (frei belegbar) |
| Mikrofon stumm | aus | Einstellungen → Audio: Mikrofon stumm |
| Lautstärke der anderen | 100 % | Einstellungen → Audio: Sprache |

- „Aus“ schaltet auch die Stimmen der anderen stumm.
- Ohne Mikrofon funktioniert das Zuhören trotzdem.
- Eigenes Mikrofon testen: Spiel mit `-voiceloopback` starten.
- **F3** zeigt pro Mitspieler, wie seine Stimme gerade ankommt (Luft / Wasser / Funk).

---

## 6. Was geteilt wird und was jeder selbst hat

**Gemeinsam** (der Host entscheidet):
- Geld, Follower, Aufrufe, Sealog-Einträge, Rekorde, Sichtungen und Tiefen.
- Gesicherte Clips (nach dem Auftauchen an Land oder auf dem Boot) und FinTube-Uploads (mit eigenem Titel).
- Kühlbox, Fischmarkt, Vorratskammer (Snacks), Funde und Bergungsgut.
- Bestellungen und der Heli (auch Pannen: liegt die Kiste falsch, liegt sie bei allen an derselben Stelle), gekaufte Ausrüstung, Sammelstücke.
- Freigeschaltete Tauchplätze (auch die Nebelinsel), Meilensteine, Aufgaben, Nebenaufgaben, Story (Toni, Dorf Altriff), Tag und Uhrzeit.
- Sponsor-Deals (höchstens 2), Foto-Challenges des Tages, Tier-Events (Fressrausch, Putzerstation …).
- Der Livestream auf KRILL.tv: alle sehen dieselben Zuschauer, Chat und Mutproben; jede Kamera zählt mit.
- Story-Figuren (Horst, Wanda, Dorfbewohner …): der Host steuert sie, alle sehen sie am selben Ort.
  Ihre Sätze hört jeder in 25 m Umkreis, Funksprüche (Gus) alle – unter Wasser nur mit Unterwasser-Funk, sonst nach dem Auftauchen.
- Erfolge: werden für alle freigeschaltet, die gerade dabei sind.

**Pro Spieler** (der Host merkt es sich für jeden Gast unter einem zufälligen Spieler-Schlüssel):
- Position, Luft, Hotbar und was man gerade in der Hand hat (auch getragene Quest-Gegenstände).
- Welche Ausrüstung man angelegt hat, eigene Statistiken und Erfolge.
- Aussehen (Charakter-Bildschirm) – alle sehen deinen Taucher genau so.
- Die Clips des laufenden Tauchgangs (gehen bei Ohnmacht verloren), Essens-Buffs, Einstellungen und Fotos in der Galerie.

**Gäste behalten alles:** Kommt ein Gast später wieder (auch nachdem der Host neu geladen hat), bekommt er seine Hotbar,
Ausrüstungswahl, Statistiken und Erfolge vom Host zurück. Der Host speichert das, wenn ein Gast geht, und spätestens jede Minute.

Weitere Regeln:
- **Boot:** Es steuert immer nur einer. Ist der Sitz belegt, steht dort „Besetzt“. Die anderen können auf dem Deck mitfahren.
- **Schlafen:** **Alle** müssen sich hinlegen (Bett, Hängematten, Koje). Oben steht „Schlafen 1/2“, bei „2/2“ geht die Nacht vorbei.
  Wer abwesend ist, bekommt eine Meldung „Anna will schlafen“. Nach 60 s bei „1/2“ kann der Host im Pausemenü „Schlafen erzwingen“.
- **Retten:** Geht einem Taucher (Normal/Schwer) unter Wasser die Luft aus, wird er bewusstlos (30 s / 20 s).
  Ein Freund in 2 m Nähe kann ihn mit **E** abschleppen. Der Bewusstlose hängt dann auf Kopfhöhe hinter dem Retter
  (bleibt er an Korallen oder Felsen hängen, zieht ihn das Abschleppen frei). Erreicht der Retter die Oberfläche,
  ist der Freund gerettet und behält seine Clips.
- **Gefahr:** Mehr Taucher = etwas mehr Raubtiere und Quallen (2 Spieler ×1,25, 3 ×1,5, 4 ×1,75).
- **Kisten vom Heli:** Wer die Kiste öffnet, legt das Teil sofort an. Besitzen tun es danach alle.
- **Host beendet:** Sind Gäste da, fragt das Spiel „Alle werden getrennt. Wirklich beenden?“. Die Gäste sehen danach
  „Der Host hat die Sitzung beendet. Dein Fortschritt ist beim Host gespeichert.“
- **Menü-Crew:** Im Hauptmenü stehen danach die Taucher deiner Freunde aus der letzten Sitzung neben dir (nur das Aussehen).

**F3** zeigt im Spiel die Netzwerk-Werte (Ping, KB/s, Spieler, Boot-Fahrer, Tiere, Schlafen, Livestream, Tier-Event, Abschleppen).

---

## 7. Bekannte Grenzen

- Höchstens **4 Spieler**.
- Alle brauchen **genau dieselbe Spielversion**. Sonst meldet das Spiel „Andere Spielversion als der Host“.
- **Nur der Host speichert.** Gäste spielen im Spielstand des Hosts; ihre persönlichen Daten liegen dort mit drin.
- **Verlässt der Host das Spiel, endet die Sitzung für alle.** Die Gäste landen mit einer Meldung im Hauptmenü. Es gibt keine Host-Übergabe.
- Kein eigener Server und keine automatische NAT-Durchdringung. Übers Internet braucht es Portweiterleitung oder ein VPN (Abschnitt 3).
- Die Suche „Im Heimnetz gefunden“ funktioniert nur im selben Netz (oder über Radmin VPN / ZeroTier).
- Gäste können jederzeit dazukommen und gehen (Drop-in). Ihr Taucher erscheint und verschwindet dann für alle.
- Bei hohem Ping (über ca. 150 ms) bewegen sich andere Taucher und das Boot sichtbar ruckeliger.
- Kein Text-Chat im Spiel.
- Mit zwei Fenstern auf einem PC zeigt F3 einen Ping von 30–80 ms. Das liegt an der Bildrate des Fensters ohne Fokus, nicht am Netz.
- Datenmenge: etwa 10–17 KB/s pro Gast im normalen Spiel, Spitzen bis etwa 45 KB/s (Sprachchat, Livestream, viele Tiere
  um den Gast). Jede normale Internetleitung schafft das.
  Tiere werden nur übertragen, wenn sie sich bewegen (und weiter entfernte seltener) – die „Fische überall“-Welt kostet daher kaum mehr.
- Über das Menü („Spiel hosten“) gehostete Spiele speichert der Host ganz normal in seinen Spielstand. Nur der Schnelltest aus Abschnitt 1 wird nicht gespeichert.

---

## 8. Was der automatische Koop-Test geprüft hat

Stand 11.10.2026 abends (Runde 2, mit allen Neuerungen aus deiner dritten Nachricht).
Zwei Fenster auf diesem PC, Host Anna und Gast Ben, 21 Schritte, ohne einen einzigen Fehler im Log.
Im letzten Lauf bestand Ben alle 21 Schritte, Anna 20. Der eine rote Punkt war ein Zeitproblem des Tests:
Bens Kamera hatte die Livestream-Mutprobe schon selbst erfüllt, bevor der Test nachsah. Die Belohnung kam an,
der Test ist angepasst.

**Grundlagen**
- Verbindung über 127.0.0.1, gleiche Insel und gleicher Spielstand. Beide sehen den anderen Taucher mit Namen.
- Ben angelt, stellt den Fisch auf den Markt, das Geld kommt bei beiden an.
- Ben kauft eine Flasche, der Heli kommt bei beiden, Ben öffnet die Kiste, beide besitzen sie.
- Anna fährt das Boot, Ben steht im Steuerhaus und fährt mit. Der Sitz zeigt Ben „Besetzt“, danach fährt Ben selbst.
- Ben filmt Annas Schildkröte, der Sealog-Eintrag erscheint bei beiden.
- Ben lädt ein Video mit **eigenem Titel** hoch, bei Anna steht genau dieser Titel.
- Sammelstücke und Sprachchat in beide Richtungen, ein Erfolg für beide, neues Aussehen kommt beim anderen an.

**Neu in Runde 2**
- **Schlafen:** Ben legt sich mit E ins Bett, oben steht „Schlafen 1/2“, die Nacht vergeht nicht. Ben steht auf, die Anzeige
  verschwindet. Anna legt sich mit E in die Hängematte, Ben ins Bett: „2/2 – Gute Nacht!“, beide wachen am Morgen auf.
  Beide sehen den anderen liegen.
- **Retten:** Ben wird unter Wasser bewusstlos. Anna schleppt ihn mit E nach oben, Ben wacht mit 30 % Luft auf und behält seine Clips.
- **Fische überall, für beide gleich:** Ben taucht allein, über 400 m von Anna entfernt. Annas Spiel füllt das Meer um Ben
  (rund 65 Tiere in 80 m). Ben sieht genau dieselben Tiere an denselben Stellen (12 von 12 geprüft, höchstens 0,2 m daneben).
  Schwimmt Anna dazu, sehen beide dieselben Tiere. Alle Tiere sind in ihrer Tiefe, keine Art eines gesperrten Riffs.
- **Inseltiere:** Am Ziegengehege sieht Ben dieselben Ziegen und Geckos wie Anna.
- **Neue Arten:** Adlerrochen, Einsiedlerkrebs und die Arten der Nebelinsel kommen bei Ben mit dem richtigen Modell an.
- **Livestream:** Ben geht auf dem Boot live. Beide sehen dieselben Zuschauer und den Chat. Ben bekommt eine Mutprobe,
  nimmt sie mit T an, die Belohnung landet in der gemeinsamen Kasse. Ben beendet den Stream, beide sehen dieselbe Abschlusskarte.
- **Dorf und Nebenaufgaben:** Ben spricht Mira an, beide hören ihre Sätze. Ben nimmt „Treibholz“ an, die Aufgabe steht bei
  beiden oben in der Mitte (0/5). Ben sammelt ein Stück (1/5), Anna eins (2/5), bei beiden zählt beides.
- **Essen:** Anna legt Snacks in den Kühlschrank, Ben isst einen. Nur Ben bekommt den Buff, bei beiden ist einer weniger da.
  Ben grillt am Lagerfeuer einen Fisch aus der gemeinsamen Kühlbox.
- **Heli-Panne:** Ben bestellt Krill-Riegel, der Heli wirft das Paket weit daneben ab. Bei beiden liegt es an derselben Stelle,
  mit Rauch und „Paket suchen: …“ oben. Ben findet und öffnet es, die Snacks sind bei beiden.
- **Gast-Daten:** Ben geht und kommt wieder und hat seine Hotbar zurück. Danach geht Ben, Anna speichert, geht ins Hauptmenü
  und lädt den Spielstand neu. Ben kommt wieder und hat seine eigene Hotbar und Statistik zurück.
- Anna beendet das Spiel, Ben landet mit der Meldung „Der Host hat die Sitzung beendet. …“ im Menü.

**Dabei gefunden und behoben**
- Abschleppen: Der Bewusstlose hing 1,2 m *unter* dem Retter, wenn der senkrecht auftauchte, und wachte an der Oberfläche
  nicht auf. Er hängt jetzt auf Kopfhöhe hinter dem Retter. Bleibt er an Korallen hängen, wird er freigezogen.
- Boot: Bei niedriger Bildrate rutschte ein Mitfahrer im Steuerhaus beim Beschleunigen nach hinten und auf die Bank.
- Das Angebot „E – Annehmen · Q – Später“ stand über dem Namen in den Untertiteln, jetzt steht es darüber.
- Während eines Livestreams lief der Zeitbalken durch die Aufgabenzeile, die Zeile rutscht jetzt tiefer.
- Beim Schlafen kamen zwei Meldungen gleichzeitig („Ben will schlafen“ und „Crew will schlafen“), jetzt nur noch eine.

**Bildrate:** Im letzten Lauf liefen beide Fenster mit 60 FPS (Grafik „Niedrig“, 1024×576, 60 FPS ist die Obergrenze).
In den ersten beiden Läufen lief auf demselben PC zusätzlich die Vorschau-Version des Spiels. Mit drei Spielfenstern auf
einer Grafikeinheit kamen da nur 7–15 FPS heraus.

Der automatische Test schreibt weder deinen Spielstand noch `settings.json` (auch keine Erfolge oder Aussehen).

Bilder und Berichte: `Builds/Screenshots/mp_r2/` (`mp_host_report.md`, `mp_client_report.md`).
