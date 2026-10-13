# SEALEBRITIES – Koop über Steam testen (Testversion 3.1)

Mit dieser Version könnt ihr zu zweit (bis zu viert) über Steam zusammen tauchen.
Das Spiel ist **nicht** auf Steam veröffentlicht. Ihr startet es einfach aus dem Ordner. Steam wird nur für die Verbindung benutzt.
Ihr braucht **keine IP-Adresse, keine Portfreigabe und keine Router-Einstellung**.

---

## 1. Das braucht ihr beide

- **Steam läuft** auf dem PC und ihr seid **eingeloggt und online** (nicht „Offline-Modus“).
- **Zwei verschiedene Steam-Konten.** Mit einem Konto geht Steam-Koop nicht.
- Ihr seid **in Steam befreundet** (die Lobby ist nur für Freunde offen).
- **Genau dieselbe Spielversion.** Am besten schickst du deinem Freund genau die Zip-Datei, die du selbst benutzt:
  `SEALEBRITIES_Testversion_3_1.zip`.
  Mit einer anderen Version meldet das Spiel „Andere Spielversion“.

---

## 2. Spiel zum Freund schicken

1. Die Datei **`SEALEBRITIES_Testversion_3_1.zip`** (liegt auf dem Desktop, etwa 41 MB) in eine Cloud hochladen,
   z. B. Google Drive, OneDrive, Dropbox oder WeTransfer, und den Link an den Freund schicken.
2. Der Freund lädt die Zip herunter und **entpackt** sie (Rechtsklick → „Alle extrahieren …“).
   Nicht direkt aus der Zip heraus starten.
3. Im entpackten Ordner auf **`SEALEBRITIES.exe`** doppelklicken.

Windows kennt das Spiel noch nicht und zeigt beim ersten Start vielleicht:
*„Der Computer wurde durch Windows geschützt“*.
Dann auf **„Weitere Informationen“** und danach auf **„Trotzdem ausführen“** klicken.

Wichtig: Im Ordner liegt die Datei **`steam_appid.txt`**. Die muss neben der `SEALEBRITIES.exe` bleiben, sonst startet Steam-Koop nicht.

---

## 3. So geht's: Host (du)

1. Steam starten, dann `SEALEBRITIES.exe` starten.
2. Hauptmenü → **Mit Freunden** → **Über Steam** → **Spiel hosten**.
3. **Spielstand fortsetzen** oder **Neues Spiel** wählen, dann **Lobby öffnen & starten**
   (bzw. „Lobby öffnen & weiterspielen“).
4. Im Spiel **Esc** drücken. Rechts neben dem Pause-Menü ist die Karte **„Steam-Koop“** → **Freunde einladen**.
   - Klappt das Steam-Overlay, öffnet sich das Einladungsfenster von Steam. Dort den Freund einladen.
   - Klappt das Overlay nicht, zeigt das Spiel selbst eine Liste deiner Steam-Freunde, die online sind.
     Beim Freund auf **Einladen** klicken.
   - Alternativ: **Shift+Tab** öffnet das Steam-Overlay (falls es aktiv ist).

## 4. So geht's: Freund

1. Steam starten und eingeloggt sein.
2. **Zuerst `SEALEBRITIES.exe` starten** und im Hauptmenü warten.
   Das ist wichtig (siehe „Spacewar“ unten): Ist das Spiel noch nicht offen, startet Steam beim Annehmen ein anderes Spiel.
3. Dann einen dieser Wege nehmen:
   - **Einladung im Steam-Chat annehmen** (Nachricht vom Host anklicken), oder
   - in der **Steam-Freundesliste** beim Host auf den kleinen Pfeil → **„Spiel beitreten“**, oder
   - im Spiel: **Mit Freunden** → **Über Steam** → unter **„Freunde, die gerade hosten“** beim Host auf **Beitreten**.
4. Das Spiel verbindet von selbst. Das kann ein paar Sekunden dauern.

Kommt die Einladung, während der Freund gerade allein spielt, zeigt das Spiel einen Hinweis.
Dann **Esc → „Speichern & Hauptmenü“**. Danach geht es automatisch los.

---

## 5. Steam zeigt „Spacewar“ – das ist normal

Solange SEALEBRITIES noch keine eigene Steam-Seite hat, benutzen wir die **Test-Nummer 480** von Valve.
Deshalb zeigt Steam bei euch beiden **„Spielt Spacewar“** an. Das ist richtig so, ihr spielt trotzdem SEALEBRITIES.

Folgen davon:
- Der Freund muss **SEALEBRITIES zuerst selbst starten** und erst dann die Einladung annehmen.
  Ist das Spiel zu, würde Steam „Spacewar“ starten (oder es fehlt).
- In der Steam-Bibliothek taucht SEALEBRITIES nicht auf. Gestartet wird immer über die `SEALEBRITIES.exe` im Ordner.

---

## 6. Wenn Steam nicht klappt: Koop per IP

Unter **Mit Freunden** gibt es weiter **„Per IP / Heimnetz“**.
- Im selben WLAN/Heimnetz: Host → „Spiel hosten“, Freund → „Beitreten“, das Spiel taucht unter „Im Heimnetz gefunden“ auf.
- Übers Internet braucht der IP-Weg eine Portfreigabe (TCP 27015) oder ein Programm wie Radmin VPN / ZeroTier.
  Details stehen in `Multiplayer_Testen.md`.

Zum Ausprobieren auf **einem** PC (zwei Fenster): `Koop-Test_2_Fenster.bat` im Spielordner doppelklicken.
Das läuft über IP, nicht über Steam (Steam-Koop braucht zwei Konten).

---

## 7. Häufige Meldungen

| Meldung | Was tun |
|---|---|
| „Steam läuft nicht oder du bist nicht angemeldet …“ (im Menü „Mit Freunden“) | Steam starten, einloggen, online gehen. Dann SEALEBRITIES neu starten. Prüfen, ob `steam_appid.txt` neben der exe liegt. |
| „Steam ist im Offline-Modus …“ | In Steam oben links *Steam → Online gehen*, dann das Spiel neu starten. |
| „Andere Spielversion“ | Beide müssen dieselbe Zip benutzen. |
| „Das ist deine eigene Lobby …“ | Du hast versucht, deinem eigenen Spiel beizutreten. Steam-Koop braucht zwei Konten. |
| „Dieses Spiel gibt es nicht mehr – der Host hat es beendet.“ | Der Host muss neu hosten und neu einladen. |
| „Steam antwortet nicht – bitte nochmal versuchen.“ | Kurz warten und nochmal beitreten. |
| „Steam-Overlay ist nicht aktiv …“ | Normal. Einfach die Freundesliste im Spiel benutzen. |

---

## 8. Bekannte Grenzen

- Höchstens **4 Spieler**.
- **Nur der Host speichert.** Der Freund spielt im Spielstand des Hosts.
- **Verlässt der Host das Spiel, endet es für alle.** Der Freund landet mit einer Meldung im Hauptmenü.
- Das **Steam-Overlay (Shift+Tab)** startet bei solchen Testversionen oft nicht. Dann die Liste im Spiel benutzen.
- Im Pause-Menü steht beim Host evtl. noch „IP … · Port …“. Bei Steam-Koop spielt das keine Rolle.
- Die Verbindung läuft über Steams Server (Relay). Der Ping kann etwas höher sein als im Heimnetz.
- Sprachchat ist eingebaut: standardmäßig **offenes Mikrofon**, auf Wunsch Push-to-Talk (Taste **V**),
  Einstellungen unter *Einstellungen → Audio*. Unter Wasser hört ihr euch nur bis 10 m (mit Unterwasser-Funk bis 200 m).
- **F3** zeigt im Spiel Netzwerk-Infos (Ping, Spieler).

---

## 9. Neu in Testversion 3.1

- Koop über Steam (Einladen, „Spiel beitreten“, Liste „Freunde, die gerade hosten“).
- Lampe, Kamera und Hände ragen nicht mehr in Wände, Steg oder Boden hinein.
- Lautstärke-Regler: keine Riesenzahlen mehr nach einem Fehlklick. Kaputte Werte werden beim Start repariert.
- **„Zur Hütte“** im Pause-Menü bringt jetzt auch das Boot zurück an den Steg
  (nicht, wenn gerade ein anderer Spieler damit fährt, an Deck steht oder in der Nähe schwimmt).
- Die Test-Cheats sind entfernt.

---

## 10. Im Koop neu seit Testversion 3.1 (Runde 2)

Das gilt über Steam genauso wie per IP. Ausführlich: `docs/Multiplayer_Testen.md`, Abschnitt 6 und 8.

- **Schlafen:** Jeder legt sich mit **E** in ein Bett, eine Hängematte oder aufs Sofa. Oben steht „Schlafen 1/2“.
  Erst bei „2/2“ geht die Nacht vorbei.
- **Retten:** Geht dem Freund unter Wasser die Luft aus, ist er 30 s bewusstlos. Mit **E** schleppst du ihn ab, er hängt
  auf Kopfhöhe hinter dir. An der Oberfläche wacht er auf und behält seine Clips.
- **Fische:** Jeder hat überall Tiere um sich, auch weit weg vom Host. Beide sehen dieselben Tiere an denselben Stellen.
- **Livestream, Nebenaufgaben, Essen, Heli-Pannen:** gemeinsam. Der Freund kann selbst live gehen, Aufgaben annehmen und
  verlorene Pakete finden. Was er isst, wirkt nur bei ihm.
- **Der Freund behält seine Sachen:** Hotbar, Ausrüstungswahl und Statistik merkt sich der Host, auch wenn er neu lädt.

Der Steam-Selbsttest (Anmeldung, Relay-Netz, private Lobby anlegen und sofort wieder verlassen, 1,2 MB über die
Steam-Verbindung) ist mit dieser Version am 11.10.2026 abends bestanden. Er lädt niemanden ein und ändert nichts an deinem Konto.
