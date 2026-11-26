# VictoryLauncher

Ein Windows-Launcher für die Forks der VIctory-Familie: Fork wählen, auschecken, Release bauen, Shader vorwärmen
(wenn der Fork das kann) und das Spiel starten.

Spieldateien sind nicht enthalten, du brauchst deine eigenen, rechtmäßig erworbenen. Das Projekt gehört weder zu
Rockstar Games, Take-Two oder Microsoft noch zu den Autoren der Forks.

## Voraussetzungen

- Windows x64
- git
- Rust (rustup) samt den C++ Build Tools von Visual Studio
- nur für kelvaroq: Python 3.11 oder neuer und das Windows SDK
- zum Bauen aus dem Quellcode: das .NET 10 SDK (die fertige ZIP aus den Releases braucht kein .NET)

## Eingebaute Forks

| Fork | Warmup | Einstellungen |
|------|--------|---------------|
| [AnyXboxX](https://github.com/LooperSalty/AnyXboxX) | ja, mit Prozentanzeige | aus `crates\anyxboxx\src\run\settings.rs`, wandern mit den Updates mit |
| [klbf-lab](https://github.com/PastiiJay/klbf-lab) | ja (`translate`) | aus dem README des Forks, ohne Series-S-Profil |
| [Vice-Tay](https://github.com/TaynanGT/Vice-Tay) | ja (`shaders`, dann `warm --used`) | aus seinem `play.ps1` (NVIDIA) |
| [VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) | ja (`shaders`, dann `warm --used`) | aus seinem `play.ps1`, auf AMD abgestimmt |
| [kelvaroq](https://github.com/fablinoo/kelvaroq) | wird nach dem Build erkannt | Stufe für 10 GB |
| [VItoriaBr](https://github.com/uiliannn/VItoriaBr) | ja (`warm`) | sein Preset für 8 GB |
| [victory-fork](https://github.com/fxceless/victory-fork) | nein | `VICTORY_TEXTURE_BACK=1` |

Außer bei AnyXboxX liest der Launcher die Art des Warmups aus der Hilfe des gebauten Programms. Dazu kommt "Eigene Git-URL …" (nur `https://`, Aufbau mit `apps\victory` im Stamm oder unter `third_party\victory`,
ohne besondere Einstellungen).

Bis auf AnyXboxX sind die Einstellungen eine Momentaufnahme vom 26.11.2026, ausgelegt auf eine NVIDIA-Karte mit
10 GB, soweit der Fork das hergibt. Ob ein Fork auf deinem PC ein Bild liefert, kann der Launcher nicht sagen.

## Was der Launcher tut

- **Fremder Code:** Vor dem ersten Auschecken eines Forks fragt er einmal je Adresse nach (AnyXboxX ist vorab
  bestätigt). Danach werden neue Commits ohne weitere Rückfrage geholt, gebaut und ausgeführt. Schon das Bauen führt
  fremden Code mit deinen Rechten aus.
- **Aktuell bleiben:** beim Öffnen, vor jedem Warmup und vor jedem Spielstart neue Commits holen (nur Fast Forward)
  und das Release bauen. Eigene Änderungen im Checkout werden nicht überschrieben.
- **Spielordner:** Der Launcher schreibt nichts hinein.
- **Programmprüfung:** Vor dem Spielstart zeigt er Programme, die Videospeicher oder CPU belegen. Nichts ist vorab
  angehakt. Angehakte werden normal geschlossen; was weiterläuft, wird nur nach einer zweiten Rückfrage hart beendet.
- **Treiber-Cache:** Den Shader-Cache des Grafiktreibers verwaltet er nicht.

## Ordner

Voreingestellt:

| Was | Wo |
|-----|----|
| Spielordner | `G:\DevElop` |
| Checkout, Cache, Spielstände eines Forks | `G:\victory-data\forks\<id>`, `cache-<id>`, `data-<id>` |
| AnyXboxX | Checkout `G:\AnyXboxX`, Cache `G:\victory-data\cache-anyxboxx`, Spielstände am Standardort der Runtime |

Spiel-, Cache- und Checkout-Ordner lassen sich in der Oberfläche ändern, die Wurzel `G:\victory-data` als
`DataRoot` in `settings.json`. `settings.json` und `logs\` liegen neben der EXE.

## Bauen

```powershell
dotnet test tests/VictoryLauncher.Tests
```

```powershell
dotnet publish src/VictoryLauncher -c Release -o app
```

Einige Tests öffnen eigene Testfenster außerhalb des Bildschirms oder brauchen die Leistungszähler einer
Grafikkarte. Sie tragen die Kategorie `Desktop` und laufen auf dem Build-Server nicht
(`--filter "Category!=Desktop"`).

## Release

Die GitHub Action [release.yml](.github/workflows/release.yml) testet und baut bei jedem Push auf `main` eine
eigenständige ZIP und hängt sie als Artefakt an den Lauf. Ein Tag `v*` erzeugt daraus ein Release:

```powershell
git tag v1.0.0
```

```powershell
git push origin v1.0.0
```
