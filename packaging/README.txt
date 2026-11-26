VictoryLauncher (Windows x64, self-contained)

Getting started
  Unpack the zip into a folder you can write to, then start VictoryLauncher.exe.
  settings.json and the logs folder are created next to the EXE.

.NET does not have to be installed. You still need:
  - git
  - Rust (rustup) with the C++ Build Tools of Visual Studio, so that the forks can be built
  - for kelvaroq only: Python 3.11 or newer and the Windows SDK
  - your own, lawfully obtained game files (they are not included)

Updates
  - When it opens, the launcher asks GitHub for a newer version. If there is one, the UPDATE
    button appears. Nothing is downloaded, checked or restarted before you click it.
    settings.json and logs are kept.
  - The EXE is not signed. The checksum detects damaged downloads, but it does not protect you
    if someone takes over the GitHub account.

Good to know
  - The launcher builds and runs third party code. It asks before it checks out a fork for the
    first time (AnyXboxX is confirmed in advance). After that, new commits are fetched, built
    and run without asking again: when the launcher opens, before every warmup and before every
    game start.
  - The defaults are G:\DevElop as the game folder, G:\victory-data for checkouts, caches and
    save games, and G:\AnyXboxX as the checkout of AnyXboxX. You can change the folders in the
    window, and G:\victory-data as "DataRoot" in settings.json.
    Without a drive G:, change these paths first.
  - The settings of the forks are meant for an NVIDIA card with 10 GB, as far as the fork allows
    it. The note below the fork says what it is tuned for.
  - Program check before the game starts: nothing is ticked in advance. A program is only force
    closed after a second question.
