# Warmup: Fortschritt für alle Forks und Pipelines im Voraus

Stand 2026-11-26, freigegeben.

## Absicht

Beim Warmup ist beides gewünscht: sehen, wie weit er ist, und weniger Ruckler im Spiel. Heute zeigt der Launcher nur
bei AnyXboxX Prozent, bei allen anderen läuft ein Balken ohne Ende. kelvaroq hat einen Warmup, den der Launcher nicht
kennt. Bei Vice-Tay und VIctory-7900XTX meldet der zweite Schritt `0 pipelines known` und tut nichts.

## Was schon da ist

Laut Quelltext speichert jeder Fork gebaute Host-Pipelines in einer eigenen Bibliothek (`host-pipelines.bin`) und baut
die früher benutzten beim Laden. Auf der Platte geprüft ist das nur bei AnyXboxX (450 MB). Ein Warmup hilft deshalb
nur bei Pipelines, die noch nie gebraucht wurden, und nach Fork- oder Treiber-Updates.

`warm --used`, wie der Launcher es heute bei Vice-Tay aufruft, filtert die aufgezeichneten Pipelines. Aufgezeichnet
wird nur mit `VICTORY_PIPELINE_BOOK=1`, und das setzt weder der Launcher noch das `play.ps1` des Forks. Daher
`0 pipelines known`.

## Ziele

1. Jeder Fork mit Warmup zeigt Prozent oder wenigstens einen Zähler, soweit seine Ausgabe das hergibt.
2. kelvaroq bekommt einen Warmup.
3. Vice-Tay und VIctory-7900XTX können Pipelines für noch nicht besuchte Orte im Voraus bauen, wenn ein gemessener
   Testlauf zeigt, dass sich das lohnt.

## Nicht-Ziele

- Den Komplettbau von klbf-lab in andere Forks übertragen.
- Den Treiber-Cache verwalten, verschieben oder messen. Das bleibt Handarbeit.
- kelvaroqs `precompile` automatisch vor dem Spielstart ausführen. Sein eigenes Startprogramm tut das, hier bleibt es
  beim Knopf WARM UP. Kann später dazukommen.
- Ein Schalter für die Aufzeichnung.
- Prozent für VItoriaBr (die Idee, Cache-Dateien zu zählen, ist ungeprüft) und ein Warmup für victory-fork.

## Darf nicht passieren

- Ein Warmup, der funktioniert, wird als fehlgeschlagen gemeldet.
- Ein Warmup, der nichts oder nur einen Teil gebaut hat, wird als vollständig gemeldet.
- Der Launcher schreibt in den Spielordner oder legt seine Fork-Caches woanders als im gewählten Cache-Ordner an
  (Standard auf G:).

## Randbedingungen

- Warmup und Spiel bekommen dieselben `VICTORY_*`-Variablen.
- Oberfläche und README auf Englisch.
- Die Zeilenformate unten stammen aus dem Quelltext: klbf-lab `0c529ca`, Vice-Tay `79d78bc` (`warm.rs` ist in
  VIctory-7900XTX `2de9980` identisch), DexNom `64bd58f`, VItoriaBr `1361e19`, kelvaroq `1270c42` (Patches 0050 und
  0059). Keines wurde bisher an einem laufenden Programm gesehen. Ändert ein Fork seine Ausgabe, fällt die Anzeige
  auf Schrittname und letzte Zeile zurück.

## Wege

| Weg | Inhalt | Abwägung |
|-----|--------|----------|
| A | Nur Fortschritt, Einstellungen wie bisher | Keine Abweichung von den Autoren. Gegen Ruckler kommt nur kelvaroqs `precompile` dazu, und das hilft nach Updates, nicht an neuen Orten. |
| **B (gewählt)** | A, danach Aufzeichnung und voller `warm` für Vice-Tay und VIctory-7900XTX | Der einzige neu gefundene Weg zu noch nicht besuchten Orten. Weicht von `play.ps1` ab, Dauer und Größe sind unbekannt, das Ergebnis liegt nur im Treiber-Cache. |
| C | Den Komplettbau von klbf-lab in andere Forks übertragen | Der Launcher müsste fremden Code verändern, das bricht bei Updates. |

Gewählt ist B, weil Fortschritt und weniger Ruckler beide zählen.

## Stufe 1: Fortschritt

### Regeln für alle Arten

- **Über Erfolg entscheidet der Exit-Code.** Die Auswertung der Zeilen liefert Prozent, Zähler und Zusammenfassung.
  Ausnahmen: AnyXboxX verlangt wie bisher zusätzlich seine Ergebniszeile, und kelvaroq hat die Regel unten.
- Solange keine passende Zeile da war, sieht es aus wie heute: Balken ohne Ende, Schrittname und letzte Zeile.
- Jeder Schritt wird für sich ausgewertet. Bei mehreren Schritten steht „Step 1 of 2“ vor dem Stand.
- Ein Lauf, bei dem es nichts zu bauen gab (Vice-Tay ohne Aufzeichnung, kelvaroq mit `total` = 0), gilt als
  erledigt. Der Beschreibungstext, der immer zu sehen ist, sagt, dass erst eine Spielrunde Arbeit bringt.
- Unter 100 % bleibt der Balken bei höchstens 99 %.
- `LastWarmTotal` bleibt die Gesamtzahl des Schritts, der selbst keine nennt: der Shader-Lauf von AnyXboxX, DexNom
  und Schritt 1 von Vice-Tay. Das Format von `settings.json` ändert sich nicht.

### Pro Fork

**AnyXboxX:** unverändert.
Nachtrag: Mit der zusammengeführten Basis von AnyXboxX (04a08f5) gilt das nicht mehr. AnyXboxX hat jetzt dieselben
zwei Schritte und Leser wie Vice-Tay, und der Shader-Schritt bekommt alle Archive und Programme des Spiels.

**DexNom und VItoriaBr** (beide Art `GameFolder`):

- DexNom schreibt je Shader eine Zeile mit `: shader translated `, alle 2000 `<Sekunden> s  <N> shaders` und am Ende
  `warm         {T} translated, {R} refused; {P} compute pipelines, {Q} refused; {O} omitted by ray-tracing mode; {S} s`.
  Prozent gegen `LastWarmTotal`, beim ersten Mal gegen die Schätzung wie bei AnyXboxX (als geschätzt beschriftet).
- VItoriaBr schreibt nichts davon. Seine letzte Zeile beginnt mit `containers`. Mit Exit-Code 0 ist das ein Erfolg.

**Vice-Tay und VIctory-7900XTX:**

- `shaders` schreibt alle 1000 `N shaders in the cache, S s` und am Ende
  `N shaders in the cache, M do not translate, S s`. Beim ersten Mal Zähler, danach Prozent gegen `LastWarmTotal`
  (N + M aus der Endzeile). Beide Zeilen beginnen gleich und dürfen nicht verwechselt werden.
- `warm` schreibt zuerst
  `K pipelines known, D made before (<fingerprint>), B bad; T to make, J processes`, dann alle 1000 `C of T, S s`
  und am Ende
  `C made in S s (R have no host pipeline, X ended their process); ms each: …`. Prozent = C / T, bei T = 0 ohne
  Division.
- Zusammenfassung: bei K = 0, dass nichts aufgezeichnet ist. Bei T = 0 und D > 0, dass alle D schon früher gebaut
  wurden. Sonst gebaute und abgebrochene.
- In Stufe 1 bleibt der Aufruf `warm --used`. Der Beschreibungstext der Art sagt dann ehrlich, dass der zweite Schritt
  eine Aufzeichnung braucht, die der Launcher noch nicht einschaltet.

**klbf-lab:**

- Vor dem Bauen: `pipelines    N in the pack, A already pre-built, T to make on J threads`.
- Fortschritt höchstens alle 200 Pipelines oder 60 Sekunden:
  `<Pfad>: F/T pipelines, M made, L found in the library, S s`. Der Pfad enthält selbst einen Doppelpunkt. F kann T um
  die nach einem Absturz verdächtigen Pipelines übersteigen, daher die Grenze bei 99 %.
- Ende: `pipelines    N in the pack, S s: M made, L found in the library, A already pre-built`.
- Bei Exit-Code ungleich 0 sagt der Status zusätzlich: Warmup noch einmal starten, er überspringt dann die Pipeline,
  an der der Treiber gestorben ist (laut Modulkommentar).

**kelvaroq** (neue Art `Precompile`):

- Erkennung: Die Hilfe nennt den Befehl `precompile`, und dessen Hilfe die Option `--check`. Geprüft wird das vor
  `warm` und `translate`.
- Ein Schritt: `precompile`, mit derselben Startinformation wie das Spiel (Arbeitsordner neben der EXE,
  `VICTORY_GAME_DIR`). `--check` braucht der Launcher nicht: Ohne Arbeit schreibt `precompile` `precompile 0 0 0`.
- Ausgabe: `precompile <done> <total> <failed>`.
- **stdin:** Der Befehl liest stdin und hört auf, sobald es endet. Ohne eigene Leitung ist nach dem Quelltext zu
  erwarten, dass er `precompile 0 N 0` und Exit-Code 0 meldet, ohne etwas zu bauen. `ProcessRunner` bekommt deshalb eine Option, die nur dieser Schritt nutzt:
  stdin umleiten und offen halten.
- **Abbrechen:** stdin schließen und warten. Der Fork baut die laufenden Pipelines fertig und speichert. Erst nach
  120 Sekunden wird der Prozessbaum beendet. Danach gilt der Lauf als abgebrochen.
- **Unfertig:** Exit-Code 0, aber ohne Abbruch am Ende `done < total` oder gar keine `precompile`-Zeile: Der Status
  sagt „unvollständig, noch einmal starten“, und der Warmup gilt nicht als erledigt.
- `total` = 0: Es gibt nichts zu bauen. Der Status erklärt, dass kelvaroq nur Pipelines kennt, die eine Spielrunde
  benutzt hat.
- `failed` > 0 bei `done` = `total`: fertig, die Fehlschläge werden genannt.
- Beschreibungstext: baut die Pipelines früherer Spielrunden im Voraus, hilft vor allem nach einem Update, nicht an
  neuen Orten, erst einmal spielen.

### README

Warmup-Spalte der Tabelle anpassen und bei „Driver cache“ ergänzen, dass das Ergebnis von `warm` bei Vice-Tay und
VIctory-7900XTX nur im Treiber-Cache liegt.

## Stufe 2: Pipelines im Voraus (Vice-Tay, VIctory-7900XTX)

### Änderung

- Beide Forks bekommen `VICTORY_PIPELINE_BOOK=1` in ihre Einstellungen. Das Spiel hält dann **jede erzeugte**
  Pipeline in `known-pipelines.bin` und `known-shaders.bin` im Cache-Ordner fest. Beim `warm` selbst wirkt die
  Variable nach dem Quelltext nicht. Der Warmup-Hinweis erscheint dadurch einmal neu.
- Schritt 2 wird `warm` statt `warm --used`. `warm` baut die schon benutzten Pipelines zuerst.
- Neues Häkchen **Rebuild all pipelines**, nur bei dieser Art sichtbar, nicht gespeichert. Es hängt `--again` an
  `warm`. Grund: `warm-done.txt` merkt sich je Übersetzer- und Treiberstand, was gebaut wurde, aber nicht, ob der
  Treiber-Cache das noch hat. Wird der Cache geleert, überspringt `warm` sonst alles. Der Launcher versucht nicht, das
  selbst zu erkennen.
- Abbrechen beendet wie bisher den Prozessbaum. `warm` schreibt jede fertige Pipeline sofort in die Liste, der
  nächste Lauf macht dort weiter, und der Status sagt das.
- Beschreibungstext: erst einmal spielen, dann Warmup. Das Ergebnis liegt im Cache des Grafiktreibers.

### Bedingungen vor der Übernahme

Stufe 2 wird zunächst nur in einer lokalen Testkopie gebaut. Committet wird sie erst, wenn diese Schritte der Reihe
nach durch sind:

1. **Treiber-Cache (von Hand):** nach einem Neustart, vor allen GPU-Programmen, `D:\NVidiaCache\move-dxcache.ps1`
   ausführen und in der NVIDIA-Systemsteuerung die Größe des Shader-Caches prüfen. Dass eine kleine Grenze vorgebaute
   Einträge verdrängt, ist eine ungeprüfte Sorge.
2. **Läuft der Fork überhaupt?** Vice-Tay wurde auf der RTX 3080 noch nie mit Bild gesehen. Ohne brauchbares Bild
   ist hier Schluss. VIctory-7900XTX ist auf AMD abgestimmt und auf NVIDIA ungemessen.
3. **Aufzeichnen:** eine Spielrunde mit der Testkopie. Dafür muss dem Fork im Launcher einmal vertraut werden
   (fremder Code). Festgehalten werden die Zahl der aufgezeichneten Pipelines und die Größe
   der beiden Dateien.
4. **Messen:** Die beiden Läufe startet Claude mit denselben Variablen wie der Launcher, nach einem Ja. Erst
   `warm --used` (die benutzten, damit sie die Messung nicht verfälschen), dann `warm --limit 3000`.
   Gemessen werden beim zweiten Lauf Dauer, Zuwachs von `DXCache` und die Einträge in `warm-bad.txt`. Wächst der
   Cache nicht mit, spricht das für eine Grenze. Daraus wird auf alle Pipelines hochgerechnet.
5. **Entscheidung:** anhand dieser Zahlen, durch den Auftraggeber. Bei Nein wird Stufe 2 verworfen, Stufe 1 bleibt.

Bei Ja gilt die Änderung für beide Forks, gemessen ist dann nur Vice-Tay. Ob es weniger ruckelt, kann der Launcher
nicht messen. Das zeigt sich nur beim Spielen.

## Prüfen

- Tests vor dem Code. Die Beispielzeilen werden aus den Formatzeilen des Quelltexts gebildet, mit Fork und Commit im
  Test.
- Pflichtfälle: die VItoriaBr-Schlusszeile mit Exit-Code 0 ist ein Erfolg; die DexNom-Schlusszeile mit dem
  zusätzlichen Feld wird erkannt; die beiden ähnlichen `shaders`-Zeilen; `warm` mit K = 0 und mit T = 0; klbf-lab mit
  Pfad samt Laufwerk und F > T; kelvaroq mit `0 0 0`, mit `done < total` ohne Abbruch und ohne jede Zeile.
- `ProcessRunner` mit echtem Kindprozess: stdin bleibt offen; Abbrechen schließt es, das Kind beendet sich selbst;
  ein Kind, das nicht reagiert, wird nach der Frist beendet.
- In der echten App zunächst nur AnyXboxX (Verhalten unverändert).
- **Offen bleibt:** Die Auswertung für die anderen Forks ist erst bestätigt, wenn ein Fork gebaut und sein Warmup
  gelaufen ist. Das heißt Downloads und fremder Code, dafür wird je Fork vorher gefragt. Bis dahin sagt die
  Release-Notiz oder der Bericht, welche Forks ungeprüft sind.

## Dem Umsetzer überlassen

Namen und Aufteilung der Klassen für die Zeilenauswertung, die genauen englischen Texte, die Platzierung des Häkchens.

## Ablauf

Stufe 1 und Stufe 2 bekommen je einen eigenen Plan und eigene Commits. Die noch nicht committete Tastenbelegung bleibt
davon getrennt. Commit, Push und Release gibt es nur auf Ansage.
