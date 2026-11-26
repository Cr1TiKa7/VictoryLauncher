# Entwurf: Konsolenmodus Series X oder Series S

Stand: 2026-11-26, vom Nutzer so freigegeben.

## Ziel

Der Nutzer wählt im Launcher je Fork, ob sich das Spiel auf einer Series X oder einer Series S sieht.

Ausgangslage: Die Runtime kennt `run --console series-x|series-s`, Standard ist `series-x`. Der Launcher hat den
Schalter nie übergeben, bisher lief also jeder Fork als Series X.

Nicht Ziel: AMDs FidelityFX SDK herunterladen oder einrichten, weitere `VICTORY_*`-Variablen je Modus, den Modus
beim Warmup übergeben.

## Ansätze

1. **Auswahl je Fork (gewählt).** Die Autoren beschreiben Series S sehr unterschiedlich, deshalb gehört die Wahl zum
   Fork und braucht je Fork einen Hinweis.
2. **Eine Auswahl für alle.** Einfacher, aber ein Wechsel für einen Fork träfe auch die, bei denen Series S nicht geht.
3. **Nur anzeigen, dass Series X läuft.** Nichts zu schalten.

## Verhalten

- Eine Auswahl `Series X (default)` / `Series S` in der Zeile von `START GAME`. Gespeichert je Fork in
  `settings.json` (`SeriesS`, Standard `false`). Eine eigene Formularzeile wurde verworfen: im kleinsten Fenster
  (760 x 720) rutschte damit der Startknopf aus dem Bild.
- Nur bei Series S hängt der Launcher beim Spielstart `--console series-s` an. Bei Series X bleibt der Aufruf wie
  bisher (`run <exe> --execute`), damit sich dort nichts ändert.
- Bei Series S steht darunter ein Hinweis zum Fork: was seine eigenen Unterlagen sagen (Stand 2026-11-26). Mit dem
  Launcher ist Series S nicht erprobt, das steht dabei. Der Hinweis nimmt seinen Platz vom Log. Weil im kleinsten
  Fenster keiner bleibt, ist er auch der Tooltip der Auswahl.
- klbf-lab braucht für Series S laut README das FidelityFX SDK in `VICTORY_FFX_DIR`. Der Launcher entfernt alle
  `VICTORY_*`-Variablen aus der Umgebung, der Hinweis sagt das. Wählbar bleibt Series S trotzdem.
- Eigene Git-URL: wählbar, mit dem Hinweis, dass nichts geprüft ist.
- Gesperrt, solange der Launcher arbeitet oder das Spiel läuft. Das Spiel-Log nennt den Modus.
