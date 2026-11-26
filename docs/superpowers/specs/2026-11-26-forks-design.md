# Entwurf: mehrere VIctory-Forks

Stand: 2026-11-26

## Ziel

Der Launcher startet nicht mehr nur AnyXboxX, sondern jeden Fork der VIctory-Familie: eine fest eingebaute Auswahl
und zusätzlich eine eigene Git-URL. Wie bisher prüft er vor Warmup und Start den neuesten Commit und baut das
Release.

Nicht Ziel: Forks reparieren, ihre Startskripte nachbilden, Zusatzpakete herunterladen.

## Ansätze

1. **Ein Profil je Fork als Daten (gewählt).** URL, Build-Schritte, EXE-Pfad, Einstellungen, Hinweis. Eigene URLs
   bekommen ein allgemeines Profil. Nachteil: Einstellungen sind eine Momentaufnahme und wandern nicht mit.
2. **Startskripte der Forks ausführen oder auslesen.** Immer aktuell, aber jeder Fork macht es anders (zwei haben
   `play.ps1`, andere nur README-Text), und eine kleine Änderung bricht das Auslesen.
3. **Nur AnyXboxX-Logik mit wählbarem Ordner.** Wenig Aufwand, aber die Forks haben andere Befehle und Ordner.

## Eingebaute Forks

Alle am 2026-11-26 nur lesend geprüft. Die Build-Skripte der VIctory-Forks bilden nur Fingerabdrücke des
Shader-Übersetzers. Das ist eine Momentaufnahme: Jedes Update kann das ändern.

| Fork | Besonderheit |
|------|--------------|
| LooperSalty/AnyXboxX | Runtime in `third_party/victory`, Einstellungen aus `crates\anyxboxx\src\run\settings.rs` |
| PastiiJay/klbf-lab | Warmup über `translate --cache --pipelines` |
| TaynanGT/Vice-Tay | Warmup: `shaders`, dann `warm --used`; Einstellungen aus seinem `play.ps1` (NVIDIA) |
| avatarmanz/VIctory-7900XTX | wie Vice-Tay, auf AMD abgestimmt |
| fablinoo/kelvaroq | eigener Aufbau, zweiter Build-Schritt `python scripts/build_native.py` |
| uiliannn/VItoriaBr | Warmup: `warm <Spielordner>`; Einstellungen: sein Preset für 8 GB |
| fxceless/victory-fork | kein Warmup-Befehl |

**AnyXboxX:** Am 2026-11-26 wurde dort `tools\lancer_victory.ps1` durch `anyxboxx run` ersetzt. Der Launcher liest
seitdem die Tabellen `ALWAYS` und `TUNED` aus `settings.rs` (zum Zeitpunkt des Wechsels dieselben 41 Variablen mit
denselben Werten), nicht `QUALITY`. Kann er eine der beiden Tabellen nicht vollständig lesen, verweigert er Warmup
und Start. Build-, Warmup- und Startbefehl sind unverändert.

Einstellungen für eine RTX 3080 mit 10 GB:

- **klbf-lab:** `LATE_WAIT`, `WIDE_BACK`, `PIPELINE_WAIT_MS=24`, `RELAY_COPY_QUEUE=0`. Ohne `--console series-s`,
  weil das FSR-DLLs verlangt, die man erst herunterladen müsste. Ohne die beiden AMD-Ausnahmen.
- **VItoriaBr:** das 8-GB-Preset unverändert. Das 16-GB-Preset ist für 10 GB zu groß.
- **fxceless:** nur `TEXTURE_BACK=1` (laut README nötig, damit Boote nicht sinken).
- **kelvaroq:** die Stufe "10 GiB VRAM" aus seiner Tabelle.

## Verhalten

- **Auswahl:** Liste mit den eingebauten Forks, den eigenen URLs und "Eigene Git-URL …".
- **Eigene URL:** nur `https://`, ohne Zugangsdaten, ohne Query. Entfernen nimmt nur den Listeneintrag weg, keine
  Dateien. Aufbau: `apps\victory` im Stamm oder unter `third_party\victory`, sonst Abbruch mit Meldung. Keine
  Einstellungen.
- **Vertrauen:** Vor dem ersten Auschecken oder Bauen fragt der Launcher je URL einmal nach. AnyXboxX gilt als
  bestätigt. Ohne Bestätigung wird nichts geklont, gebaut oder ausgeführt.
- **Ordner:** Checkout `G:\victory-data\forks\<id>`, Cache `G:\victory-data\cache-<id>`, Spielstände
  `G:\victory-data\data-<id>`. AnyXboxX behält Checkout, Cache und den bisherigen Spielstandort.
- **Auschecken:** fehlt der Ordner oder ist er leer, wird geklont. Gibt es einen Checkout, muss `origin` zur URL
  passen. Sonst Abbruch. Gelöscht wird nur der eigene Teil-Ordner eines fehlgeschlagenen oder abgebrochenen Klons.
- **Beim Öffnen** wird nur der gewählte Fork geprüft, und nur wenn er bestätigt und ausgecheckt ist. Ein Wechsel
  der Auswahl startet nichts.
- **Build:** `cargo build --release --locked -p victory`. kelvaroq: `cargo build --release --locked`, danach das
  Python-Skript, aber nur nach neuen Commits oder wenn die Engine fehlt.
- **Warmup-Erkennung:** nach dem Build aus `victory.exe --help`, `warm --help` und `translate --help`. Ohne
  passenden Befehl ist der Knopf aus. AnyXboxX behält den genauen Balken, alle anderen zeigen Laufzeit und letzte
  Zeile.
- **Abbrechen:** zusätzlich beim Klonen und beim `cargo`-Build. Nicht beim Pull und nicht beim Python-Schritt (der
  hat ein temporäres Laufwerk angelegt und muss es selbst wieder entfernen).
- **Warmup-Hinweis:** je Fork. Er richtet sich nach den Git-Objekten der Quellordner, damit reine README-Änderungen
  keinen Hinweis auslösen.
- **Alte `settings.json`:** Pfade und Warmup-Stand gehen auf AnyXboxX über.

## Mitgezogen

- Projekt heißt jetzt VictoryLauncher.
- Die Programmprüfung bietet nichts an, was die Claude App oder der Launcher gestartet haben.
- GitHub Action: Tests und eigenständige EXE als ZIP; bei einem Tag `v*` entsteht ein Release. Tests, die einen
  Desktop oder GPU-Zähler brauchen, laufen dort nicht.

## Grenzen

- Ob ein Fork auf diesem PC ein Bild liefert, sagt der Launcher nicht.
- Die Forks ändern sich stündlich. Befehle, Einstellungen und Build können jederzeit brechen.
