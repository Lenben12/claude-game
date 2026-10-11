# SEALEBRITIES – Multiplayer (Koop) testen

Koop für 1–4 Taucher. Alle spielen auf **einem gemeinsamen Kanal**: ein Konto, ein Sealog, eine Kühlbox, ein FinTube-Kanal.
Einer ist **Host** (seine Welt, sein Spielstand), die anderen sind **Gäste**. Nur der Host speichert.

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
| `-netname Name` | Name für dieses Fenster |
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

Das Spiel hat eingebauten Näherungs-Sprachchat. Du hörst andere Taucher in 2–30 m Entfernung, unter Wasser klingt es gedämpft.
Wer spricht, hat ein Lautsprecher-Symbol neben dem Namen über dem Kopf.

| Was | Standard | Wo ändern |
|---|---|---|
| Sprechen (Push-to-Talk) | **V** gedrückt halten | Einstellungen → Steuerung (frei belegbar) |
| Modus | Push-to-Talk | Einstellungen → Audio: Push-to-Talk / Offenes Mikrofon / Aus |
| Mikrofon stumm | aus | Einstellungen → Audio: Mikrofon stumm |
| Lautstärke der anderen | 100 % | Einstellungen → Audio: Sprache |

- „Aus“ schaltet auch die Stimmen der anderen stumm.
- Ohne Mikrofon funktioniert das Zuhören trotzdem.
- Eigenes Mikrofon testen: Spiel mit `-voiceloopback` starten.

---

## 6. Was geteilt wird und was jeder selbst hat

**Gemeinsam** (der Host entscheidet):
- Geld, Follower, Aufrufe, Sealog-Einträge und Rekorde.
- Gesicherte Clips (nach dem Auftauchen an Land oder auf dem Boot) und FinTube-Uploads.
- Kühlbox und Fischmarkt.
- Bestellungen und der Heli, gekaufte Ausrüstung, Sammelstücke.
- Freigeschaltete Tauchplätze, Meilensteine, Aufgaben, Tag und Uhrzeit.

**Pro Spieler:**
- Position, Luft, Hotbar und was man gerade in der Hand hat.
- Welche Ausrüstung man angelegt hat.
- Die Clips des laufenden Tauchgangs (gehen bei Ohnmacht verloren).
- Einstellungen und Fotos in der Galerie.

Weitere Regeln:
- **Boot:** Es steuert immer nur einer. Ist der Sitz belegt, steht dort „Besetzt“. Die anderen können auf dem Deck mitfahren.
- **Schlafen:** Wenn einer schläft, schlafen alle und wachen am nächsten Morgen auf.
- **Kisten vom Heli:** Wer die Kiste öffnet, legt das Teil sofort an. Besitzen tun es danach alle.

**F3** zeigt im Spiel die Netzwerk-Werte (Ping, KB/s, Spieler, Boot-Fahrer, Tiere).

---

## 7. Bekannte Grenzen

- Höchstens **4 Spieler**.
- Alle brauchen **genau dieselbe Spielversion**. Sonst meldet das Spiel „Andere Spielversion als der Host“.
- **Nur der Host speichert.** Gäste spielen im Spielstand des Hosts.
- **Verlässt der Host das Spiel, endet die Sitzung für alle.** Die Gäste landen mit einer Meldung im Hauptmenü. Es gibt keine Host-Übergabe.
- Kein eigener Server und keine automatische NAT-Durchdringung. Übers Internet braucht es Portweiterleitung oder ein VPN (Abschnitt 3).
- Die Suche „Im Heimnetz gefunden“ funktioniert nur im selben Netz (oder über Radmin VPN / ZeroTier).
- Gäste können jederzeit dazukommen und gehen (Drop-in). Ihr Taucher erscheint und verschwindet dann für alle.
- Bei hohem Ping (über ca. 150 ms) bewegen sich andere Taucher und das Boot sichtbar ruckeliger.
- Kein Text-Chat im Spiel.
- Mit zwei Fenstern auf einem PC zeigt F3 einen Ping von 30–80 ms. Das liegt an der Bildrate des Fensters ohne Fokus, nicht am Netz.
- Datenmenge: etwa 10–16 KB/s pro Gast im normalen Spiel, mit Sprachchat bis etwa 30 KB/s. Jede normale Internetleitung schafft das.
- Über das Menü („Spiel hosten“) gehostete Spiele speichert der Host ganz normal in seinen Spielstand. Nur der Schnelltest aus Abschnitt 1 wird nicht gespeichert.

---

## 8. Was der automatische Koop-Test geprüft hat

Zwei Fenster auf diesem PC, Host Anna und Gast Ben, 14 Schritte, alle bestanden:

- Verbindung über 127.0.0.1, gleiche Insel und gleicher Spielstand.
- Beide sehen den anderen Taucher mit Namen über dem Kopf.
- Ben angelt, stellt den Fisch auf den Markt, und das Geld kommt bei beiden an.
- Ben kauft eine Flasche, der Heli kommt bei beiden, Ben öffnet die Kiste, und beide besitzen sie.
- Anna fährt das Boot, Ben steht an Deck und fährt mit.
- Der Steuersitz zeigt Ben „Besetzt“. Danach fährt Ben selbst.
- Ben filmt Annas Schildkröte, und der Sealog-Eintrag erscheint bei beiden mit Meldung.
- Ben lädt das Video hoch und schläft, und beide wachen am nächsten Morgen auf.
- Sammelstücke werden in beide Richtungen übertragen.
- Sprachchat (mit Testton statt Mikrofon) kommt in beide Richtungen an.
- Ben geht und kommt wieder (Drop-in).
- Anna beendet das Spiel, und Ben landet mit der Meldung „Der Host hat das Spiel beendet.“ im Menü.

Bilder und Berichte: `Builds/Screenshots/mp/` (`mp_host_report.md`, `mp_client_report.md`).
