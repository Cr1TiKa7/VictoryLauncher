# AnyXboxX Launcher: design

Approved by the user on 2026-11-26. This describes the first, AnyXboxX only version. For what changed since, see
[2026-11-26-forks-design.md](2026-11-26-forks-design.md).

## Intent
Run AnyXboxX without the command line: choose the game folder, warm the shader cache while seeing progress, start the game. The launcher keeps AnyXboxX on the newest commit and builds the release itself.

## Approaches
- **C#/WPF on .NET 10 (chosen).** The user writes C# and can maintain it. Long runs do not block the window.
- PowerShell window: no build step, but status and cancel get awkward.
- Web UI: needs a local server, too much for this.

## Behaviour
- **Place:** own folder `G:\AnyXboxX-Launcher`, outside the AnyXboxX checkout, so a pull there stays clean.
- **Update:** checked when the window opens and again before warmup and before start. With new commits: `git pull --ff-only`, then `cargo build --release -p victory`. Automatic, without asking (the user saw the risk of running unreviewed third party code and approved). Local changes are never overwritten. If the check or update fails (offline, diverged, build error) the launcher says so and an existing `victory.exe` stays usable. `cargo build` runs on every check (it does nothing when the exe is current), so no "already built" state can go stale. Untracked files in the checkout (logs, scripts) do not count as local changes.
- **Settings:** the tuned `VICTORY_*` table is read from `tools\lancer_victory.ps1` at run time, plus `VICTORY_DISCORD=0` and `VICTORY_OCEAN_READBACK=1` as that script sets them. For the 10 GB card `VICTORY_CARD_BUFFERS_MB=1024` is set on top. If the table cannot be read, warmup and start are refused with a message (never run with silently missing settings).
- **Paths:** game folder (must contain `game_scarlett_master.exe`), cache folder (default `G:\victory-data\cache-anyxboxx`, passed as `VICTORY_CACHE_DIR`), AnyXboxX folder (default `G:\AnyXboxX`). Stored in `settings.json` next to the launcher.
- **Warmup:** same inputs as before: every `.rpf` of the game folder plus the game exe, `XboxBvhBuilder.dll` and `amd_fidelityfx_upscaler_xbox.dll` where present. Shows a counter, elapsed time, a percentage and the final result line. Can be cancelled.
- **Percentage:** `victory warm` prints a count only every 2000 shaders and no total. The total is taken from the last finished warmup, the first time from an estimate of 25758, and is labelled as an estimate then. Between two reports the launcher counts `shader translated` log lines only if they add information; otherwise the bar moves in steps of 2000.
- **After updates:** the launcher recommends a new warmup when what the caches can depend on differs from the last finished warmup: git's tree of `third_party/victory` or the `VICTORY_*` variables it sets (which include game and cache folder). Commits elsewhere in the repository do not count. It does not force it. (Changed on 2026-11-26: first it compared the commit, which also fired for commits that only touched other folders. Uncommitted edits in `third_party/victory` are not seen.)
- **Start:** runs `victory.exe run <game exe> --execute`, shows that it runs, no second instance, buttons stay locked while it runs.
- **Logs:** `logs\update.log`, `logs\warm.log`, `logs\game.log` next to the launcher. The window shows the last lines.
- **NVIDIA DXCache** is not managed by the launcher.

## Goals and non-goals
- Goals: the four things above, usable by double click.
- Non-goals: editing tuning values in the UI, installing Rust or git, managing the driver cache, supporting other runtimes (Flamingo, DexNom), a self update of the launcher.
- Anti-goals: writing anything into the game folder; starting with an outdated or half built exe without saying so.

## Left to the builder
Window layout details, code structure, tests.

## Addition: program check before the start
Approved by the user on 2026-11-26, including the hard stop after a second question.

- **When:** after "SPIEL STARTEN", once update and build are done. A window opens only when there is something to report. A failing check never blocks the start.
- **Listed:** programs of the own session, grouped by executable file, with at least 60 MB video memory (from 400 MB "hoch") or at least 2 % CPU over a 3 s sample. Private memory counts only when less than 8 GB RAM are free, then from 500 MB on. The Windows per process video memory counters are only good for ranking.
- **Never listed:** anything under the Windows folder, programs whose file cannot be read (elevated or protected), other sessions, Windows shell processes, Defender, WebView2 (it belongs to other programs), the Claude app, the launcher, `victory`, `git`, `cargo`, `rustc`.
- **Choice:** nothing is preselected. Buttons: close the selected ones and start, start as is, cancel.
- **Closing:** first like a click on the X of every window of the program, then up to 8 s of waiting. What still runs is named in a second question with a warning about unsaved data: force close and start, leave running and start, or cancel (default: leave running). Only the processes that were listed are touched, checked by process id and start time, never a process tree.
