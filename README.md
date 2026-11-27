# VictoryLauncher

A Windows launcher for the forks of the VIctory family: pick a fork, check it out, build the release, warm up the
shaders (if the fork can do that) and start the game.

Game files are not included, you need your own, lawfully obtained ones. This project is not affiliated with Rockstar
Games, Take-Two or Microsoft, nor with the authors of the forks.

![The VictoryLauncher window: fork selection, folders, warmup and the "Start game" button](docs/screenshot.png)

## Requirements

- Windows x64
- git
- Rust (rustup) with the C++ Build Tools of Visual Studio
- for kelvaroq only: Python 3.11 or newer and the Windows SDK
- to build from source: the .NET 10 SDK, and PowerShell 7 for the tests (the ready made ZIP from the releases needs
  neither)

## Built in forks

| Fork | Warmup | Settings |
|------|--------|----------|
| [AnyXboxX](https://github.com/LooperSalty/AnyXboxX) | yes (`shaders` with every archive and binary of the game: a count, from the second time a percentage; the pipeline step has nothing to build yet) | from `crates\anyxboxx\src\run\settings.rs`, they follow the updates; on a Series X also the output size [chosen in the window](#output-size-anyxboxx) |
| [klbf-lab](https://github.com/PastiiJay/klbf-lab) | yes (`translate`), with a percentage | from the README of the fork, without its Series S setup |
| [Vice-Tay](https://github.com/TaynanGT/Vice-Tay) | yes (`shaders`: a count, from the second time a percentage; the pipeline step has nothing to build yet) | from its `play.ps1` (NVIDIA) |
| [VIctory-7900XTX](https://github.com/avatarmanz/VIctory-7900XTX) | yes (`shaders`: a count, from the second time a percentage; the pipeline step has nothing to build yet) | from its `play.ps1`, tuned for AMD |
| [kelvaroq](https://github.com/fablinoo/kelvaroq) | yes (`precompile`): the pipelines of earlier game runs, with a percentage | the tier for 10 GB |
| [VItoriaBr](https://github.com/uiliannn/VItoriaBr) | yes (`warm`), without a percentage | its preset for 8 GB |
| [victory-fork](https://github.com/fxceless/victory-fork) | no | `VICTORY_TEXTURE_BACK=1` |
| [VIctory (DexNom)](https://github.com/DexNom/VIctory) | yes (`warm`), with a percentage | its experimental NVIDIA mode (ray tracing and FSR off), developed for AMD |
| [VIctory-NVIDIA](https://github.com/trustno1-coder/VIctory-NVIDIA) | yes (`shaders`: a count, from the second time a percentage; the pipeline step has nothing to build yet) | from its `play.rs` (NVIDIA), 2560x1440, without DLSS and frame generation |

The launcher reads the kind of warmup from the help of the built program. There is also
"Own Git URL …" (`https://` only, a layout with `apps\victory` in the root or below `third_party\victory`, no special
settings).

With AnyXboxX the worker pools depend on the processor, as with `anyxboxx run`: PCs with many logical CPUs get larger
ones (at the moment from 16, with more landing threads from 24). The sizes and both limits come from `settings.rs` and
follow the updates. The rule that picks between them is written into the launcher. If it cannot read these tables
completely, or finds one it does not know, it says so and starts neither the warmup nor the game. As with
`anyxboxx run`, the environment variable `ANYXBOXX_LOGICAL_CPUS` (a positive whole number, set before the launcher
starts) takes the place of the number of logical CPUs.

Apart from AnyXboxX, the settings are a snapshot from 2026-11-26 (VIctory-NVIDIA: 2026-11-27), meant for an NVIDIA
card with 10 GB, as far as the fork allows it. The output sizes offered for AnyXboxX and their default do not follow
the updates either: they are taken from its own launcher on 2026-11-27. The launcher cannot tell whether a fork shows
a picture on your PC.

## What the launcher does

- **Third party code:** Before it checks out a fork for the first time, it asks once per address (AnyXboxX is
  confirmed in advance). After that, new commits are fetched, built and run without asking again. Building alone
  already runs third party code with your permissions.
- **Staying up to date:** When it opens, before every warmup and before every game start it fetches new commits
  (fast forward only) and builds the release. Your own changes in the checkout are not overwritten.
- **Game folder:** The launcher writes nothing into it.
- **Program check:** Before the game starts, it lists programs that use video memory or CPU. Nothing is ticked in
  advance. Ticked programs are closed normally; whatever keeps running is only force closed after a second question.
- **Warmup progress:** It is read from the lines the fork prints. In the running launcher it has only been tried with
  AnyXboxX (37d4686), whose pipeline step had nothing to build there. For the other forks it was written from their
  sources, for Vice-Tay also checked against saved logs. If the lines do not match, you see the last line of the output
  instead (kelvaroq reports the warmup as incomplete then).
- **Driver cache:** It does not manage the shader cache of the graphics driver. What the pipeline step of AnyXboxX,
  Vice-Tay and VIctory-7900XTX builds is only kept there.

## Output size (AnyXboxX)

With AnyXboxX a second list next to **Start game** sets the size the game is asked to put out: 1280x720, **1920x1080**
(the default, as in the launcher of AnyXboxX), 2560x1440, or the title's own, which is 3840x2160 with the settings a
Series X wrote and is what the launcher did before there was this list. A smaller size looks softer and should run
faster; the author of AnyXboxX reports about 20 fps at 3840x2160 against 24 to 30 at 1920x1080 on an RTX 4070 SUPER,
which this launcher has not measured. AnyXboxX changes two places in the title's code for it when the game starts and
leaves them alone if their bytes are not the expected ones. Changing the size asks for no new warmup. The game has not
been started with this list yet.

## Console: Series X or Series S

The list next to **Start game** sets which console the game sees, for each fork on its own. **Series X** is what every
built in fork does by itself and the default; the launcher then names no console. With **Series S** it adds
`--console series-s` to the start of the game. The warmup is not changed. Two things are only set for a Series X: the
2560x1440 setting of VIctory-NVIDIA, whose program does not start as a Series S with it, and the output size of
AnyXboxX, whose own launcher has no Series S.

With Series S the launcher shows below what is known about the fork, taken from its own files on 2026-11-26
(VIctory-NVIDIA: 2026-11-27; in a small window the note may only be visible as the tooltip of the list). The authors
disagree: some describe it as lighter and faster, one fork's table shows it slower than Series X with 1080p output,
and a tester of another got a black picture. klbf-lab would need AMD's FidelityFX SDK for it, which the launcher does
not pass on to the game. The game has not been started as a Series S with this launcher.

## Keyboard controls

**CONTROLS** next to **Start game** shows which key stands in for which input of the controller with the chosen fork.
The keys only count while the game window has the focus. With every built in fork except AnyXboxX and VIctory-NVIDIA,
no controller may be connected either.

All built in forks except AnyXboxX use the same keys, by the letter on the key. AnyXboxX goes by the place of a key on
the keyboard, so the launcher names its keys as your keyboard layout has them. On a German keyboard, for example, LT is
the key labelled `Y` with AnyXboxX and the key labelled `Z` with the others.

AnyXboxX also has these, and takes keyboard and mouse while a controller is connected:

| Input of the controller | In addition to its letter keys |
|-------------------------|--------------------------------|
| Right stick | mouse movement |
| LT (aim; in a vehicle brake and reverse) | Left Shift or the right mouse button |
| RT (fire; in a vehicle accelerate) | Space or the left mouse button |

The mouse counts once you have clicked into the game's picture or pressed F9. That click does not fire. Pressing F9
again or leaving the window gives the mouse back. F11 or Alt+Enter switches between full screen and a window, as with
Vice-Tay.

The tables are written into the launcher (a snapshot from 2026-11-26, AnyXboxX and VIctory-NVIDIA from 2026-11-27): a
fork that changes its keys is shown wrong until the launcher follows. For an own Git URL the usual table is shown, not
checked for that fork.

## Updates of the launcher

Only the EXE from a release updates itself. A launcher you built yourself shows `(local build)` in the window title and
does not even look for updates.

- When it opens, the launcher asks GitHub in the background for the latest release of this repository (without signing
  in). If it is newer than the launcher itself, a bar with an **Update** button appears. When you are offline or
  something fails, there is at most one line in the log.
- Only a click downloads the ZIP, checks its size and SHA-256, replaces the EXE and restarts the launcher.
  `settings.json` and `logs\` are kept. The button is disabled while the launcher checks, builds or warms up, and while
  the game runs.
- The folder of the EXE has to be writable.
- The EXE is not signed. The checksum comes from the same GitHub account as the ZIP: it detects damaged downloads, but
  it does not protect you if someone takes over the account.

If you have been building the launcher yourself, unpack the ZIP of a release once and copy your `settings.json` next
to it.

## Folders

Defaults:

| What | Where |
|------|-------|
| Game folder | `G:\DevElop` |
| Checkout, cache and save games of a fork | `G:\victory-data\forks\<id>`, `cache-<id>`, `data-<id>` |
| AnyXboxX | checkout `G:\AnyXboxX`, cache `G:\victory-data\cache-anyxboxx`, save games in the default location of the runtime |

You can change the game, cache and checkout folders in the window, and the root `G:\victory-data` as `DataRoot` in
`settings.json`. `settings.json` and `logs\` are next to the EXE. If an existing `settings.json` cannot be read, the
launcher says so, uses the default values and does not overwrite the file.

## Building

```powershell
dotnet test tests/VictoryLauncher.Tests
```

```powershell
dotnet publish src/VictoryLauncher -c Release -o app
```

Some tests open test windows of their own outside the screen or need the performance counters of a graphics card. They
have the category `Desktop` and do not run on the build server (`--filter "Category!=Desktop"`).

## Release

You build a new version on GitHub: **Actions → Release → Run workflow**, branch `main`, with a `version` such as
`1.0.0`. The action checks the number, runs the tests, builds the self-contained ZIP and creates the tag `v1.0.0` with
its release. The number has to consist of three numbers and be higher than all earlier ones. Older launchers then offer
it as an update.

Pushing a tag yourself still works:

```powershell
git tag v1.0.0
```

```powershell
git push origin v1.0.0
```

The release notes list the titles of the commits since the release before that changed something in `src`, so those
titles are written for the people who use the launcher. Commits that only touch docs, tests or the build are left out,
and merge commits too. The notes can still be edited on GitHub afterwards. They come from
[Get-ReleaseNotes.ps1](packaging/Get-ReleaseNotes.ps1), which you can run yourself to see them before a release:

```powershell
./packaging/Get-ReleaseNotes.ps1 -Tag v1.1.0 -Repository Cr1TiKa7/VictoryLauncher
```

Every push to `main` and every pull request also runs the tests and builds a ZIP (version `0.0.0`) that is attached to
the run as an artifact, without creating a release. The rules for the version number are in
[Get-ReleaseVersion.ps1](packaging/Get-ReleaseVersion.ps1). The tests of both scripts need PowerShell 7.
