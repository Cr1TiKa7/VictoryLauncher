# Entwurf: Releases bauen und Selbst-Update

Stand: 2026-11-26, vom Nutzer so freigegeben.

## Ziel

Der Nutzer baut auf GitHub selbst eine Version. Ein Launcher aus einem Release merkt, dass es eine neuere gibt, und
aktualisiert sich nach einem Klick.

Nicht Ziel: Teil-Updates, ein Installer, Zurückwechseln auf ältere Versionen, Vorabversionen.

## Ansätze

1. **Eigenes kleines Update über GitHub Releases (gewählt).** Keine neue Bibliothek, die ZIP bleibt wie bisher.
2. **Velopack.** Kann Teil-Updates, ändert aber Paketformat und Ordneraufbau.
3. **Nur auf neue Versionen hinweisen.** Am wenigsten Arbeit, aber der Launcher aktualisiert sich nicht selbst.

Gewählt außerdem: Das Update läuft erst nach einem Klick, nicht ohne Rückfrage.

## Version

- Ohne Angabe ist die Version `0.0.0` (Eigenbau, `app\`, Artefakte von `main`). Ein Eigenbau sucht und installiert
  keine Updates.
- Releases bekommen ihre Version beim Bauen (`-p:Version=X.Y.Z`). Erlaubt ist nur `X.Y.Z` aus Zahlen, nicht `0.0.0`.
- Sich selbst ersetzen darf nur die eigenständige Ein-Datei-EXE.
- Der Fenstertitel zeigt die Version.

## Release-Action

- **Run workflow** mit dem Feld `version`: nur auf `main`, die Version darf kein Tag sein und muss höher sein als
  alle vorhandenen. Die Action testet, baut und erstellt Tag `vX.Y.Z` und Release.
- Ein selbst gepushter Tag `vX.Y.Z` funktioniert weiterhin.
- Push auf `main` und Pull Requests bauen nur ein Artefakt mit Version `0.0.0`.
- Die ZIP eines Release heißt `VictoryLauncher-vX.Y.Z-win-x64.zip`. Nach diesem Namen sucht der Launcher.
- Die Regeln stehen in `packaging\Get-ReleaseVersion.ps1`, damit sie sich testen lassen.

## Suche nach Updates

- Beim Öffnen, im Hintergrund: `releases/latest` des Repositorys (ohne Anmeldung, ohne Entwürfe und
  Vorabversionen), höchstens 15 Sekunden.
- Angeboten wird nur eine höhere Version mit genau der erwarteten ZIP, einer Download-Adresse aus diesem
  Repository und einer SHA-256-Prüfsumme von GitHub.
- Alles andere (offline, kein Release, Fehler) ergibt höchstens eine Zeile im Fensterprotokoll, nie ein Dialogfenster.

## Installieren

- Eine Leiste zeigt die neue Version mit dem Knopf **Aktualisieren**. Er ist gesperrt, solange geprüft, gebaut,
  vorgewärmt oder gespielt wird. Der Download lässt sich abbrechen.
- Alles liegt im Ordner der EXE. Größe und SHA-256 werden vor allem anderen geprüft.
- Aus der ZIP wird nur `VictoryLauncher.exe` entpackt.
- Tausch: neue EXE daneben schreiben, laufende EXE umbenennen (`.old-…`), neue an ihren Platz. Schlägt der letzte
  Schritt fehl, kommt die alte zurück.
- Der Launcher speichert die Einstellungen, startet die neue EXE mit `--after-update <pid>` und beendet sich. Die
  neue wartet auf das Ende der alten und räumt dann die Reste weg. Lässt sie sich nicht starten, kommt die alte
  zurück.
- `settings.json` und `logs\` bleiben unberührt.

## Einstellungen schützen (beim Prüfen ergänzt)

Beim Test in der echten App standen nach einem von zehn Updates Standardwerte in `settings.json`. Der Auslöser dieses
Laufs ist nicht geklärt. Dabei fiel eine Lücke auf, die sich gezielt nachstellen ließ: Ist die Datei beim Start der
neuen Version kurz von jemand anderem geöffnet, scheiterte das Laden still, und beim Schließen wurden die
Standardwerte über die echte Datei gespeichert.

- Laden und Speichern warten bis zu 2 Sekunden auf eine belegte Datei.
- Eine vorhandene Datei, die sich nicht lesen lässt, wird nie überschrieben. Der Launcher sagt das beim Öffnen und
  baut dann nichts automatisch. Eine fehlende oder leere Datei ergibt wie bisher die Standardwerte.
- Gespeichert wird in eine neue Datei, die die alte in einem Schritt ersetzt.

## Grenzen

- Die EXE ist nicht signiert. Prüfsumme und Datei kommen aus demselben GitHub-Konto: Das erkennt beschädigte
  Downloads, schützt aber nicht, wenn jemand das Konto übernimmt.
- Der Weg über echte GitHub-Releases ist erst prüfbar, wenn es welche gibt.
