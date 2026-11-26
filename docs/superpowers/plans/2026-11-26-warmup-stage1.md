# Warmup Stufe 1: Fortschritt für alle Forks, Implementierungsplan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Jeder Fork mit Warmup zeigt Prozent oder einen Zähler, soweit seine Ausgabe das hergibt, und kelvaroq bekommt einen Warmup.

**Architecture:** Pro Warmup-Schritt liest ein `WarmOutput` die Ausgabezeilen und liefert Stand und Urteil. `WarmPlan` wählt den passenden aus, `WarmView` macht daraus Text und Balken, `MainWindow` zeigt das nur noch an. `ProcessRunner` bekommt eine Option, bei der stdin offen bleibt und Abbrechen es schließt.

**Tech Stack:** .NET 10, WPF, xUnit 2.9

**Spec:** `docs/superpowers/specs/2026-11-26-warmup-design.md` (nur Stufe 1; Stufe 2 bekommt einen eigenen Plan)

## Global Constraints

- Tests vor dem Code, jeder neue Test wird zuerst rot gesehen.
- Über Erfolg entscheidet der Exit-Code. Ausnahmen: AnyXboxX verlangt zusätzlich seine Ergebniszeile, kelvaroq darf nicht unfertig sein.
- Unter 100 % bleibt der Prozentwert bei höchstens 99.
- Das Format von `settings.json` ändert sich nicht. `LastWarmTotal` ist die Gesamtzahl des Schritts, der selbst keine nennt.
- Zeilen, die nicht passen oder unlesbare Zahlen enthalten, werden übergangen. `Feed` wirft nie: Es läuft auf Threadpool-Threads, eine Ausnahme dort beendet den Launcher.
- Oberfläche und README auf Englisch, Zahlen mit englischen Trennzeichen.
- Beispielzeilen in Tests nennen Fork und Commit ihrer Quelle.
- Nichts wird in den Spielordner geschrieben. Kein Fork außer AnyXboxX wird gebaut oder gestartet.
- Commits nur, wenn der Auftraggeber den Modus „mit Commits“ gewählt hat. Nie Push, Tag oder Release.
- Commit-Meldungen enden mit `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Eine Zeile mit einer zu großen Zahl: wird übergangen, kein Absturz (Tests in Task 1 bis 3).
2. Ein gespeichertes `LastWarmTotal` von 0 oder weniger (von Hand geänderte Datei): Zähler statt Division durch 0 (Task 2).
3. Ein Pfad mit Leerzeichen und weiteren Doppelpunkten vor klbf-labs Fortschritt: wird trotzdem gelesen (Task 3).
4. Log-Zeilen, die dieselben Wörter enthalten wie Fortschrittszeilen: werden nicht als Fortschritt gelesen (Task 3).
5. Ein Kind mit offener stdin, das von selbst endet: Der Exit-Code kommt normal zurück, nichts hängt (Task 5).

## Dateien

| Datei in `src\VictoryLauncher.Core\` | Aufgabe |
|---|---|
| `WarmOutput.cs` (neu) | `WarmStatus`, `WarmVerdict`, Basisklasse `WarmOutput` |
| `WarmProgress.cs` | liest AnyXboxX, DexNom, VItoriaBr |
| `ViceTayOutput.cs` (neu) | `ShaderCacheOutput`, `KnownPipelinesOutput` |
| `ShaderPackOutput.cs` (neu) | klbf-lab |
| `PrecompileOutput.cs` (neu) | kelvaroq |
| `WarmPlan.cs` | neue Art, Erkennung, Auswahl des Lesers, Abbruchfrist |
| `ProcessRunner.cs` | Option mit offener stdin |
| `WarmView.cs` (neu) | Statustext und Balken |

Dazu `src\VictoryLauncher\MainWindow.xaml.cs`, `README.md` und in `tests\VictoryLauncher.Tests\`: `ProcessAndProgressTests.cs`, `WarmPlanTests.cs`, neu `WarmOutputTests.cs`.

---

### Task 0: Die Tastenbelegung vom Warmup trennen

Die Tastenbelegung ist noch nicht committet, und Task 6 und 7 ändern mit `MainWindow.xaml.cs` und `README.md` dieselben Dateien.

- [ ] **Step 1: Ausgangsstand prüfen.** `dotnet test tests/VictoryLauncher.Tests` → 298 bestanden. `git status --short` zeigt nur die Tastenbelegung und die Warmup-Dokumente.
- [ ] **Step 2a (Modus mit Commits):** Nur die Tastenbelegung committen: `README.md`, `docs/screenshot.png`, `Forks.cs`, `KeyBindings.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`, `ControlsDialog.xaml`, `ControlsDialog.xaml.cs`, `KeyBindingsTests.cs`, die beiden `controls`-Dokumente. Meldung: `Show the key bindings of the chosen fork`. Danach Spec und diesen Plan als eigener Commit `Plan the warmup progress`.
- [ ] **Step 2b (Modus ohne Commits):** Nichts committen. Das weicht vom Design ab („bleibt davon getrennt“): Tastenbelegung und Warmup liegen dann in denselben Dateien und lassen sich später nur noch gemeinsam sauber committen. Diesen Modus nur auf ausdrücklichen Wunsch nehmen.

---

### Task 1: Gemeinsame Form und AnyXboxX, DexNom, VItoriaBr

**Files:**
- Create: `src/VictoryLauncher.Core/WarmOutput.cs`
- Modify: `src/VictoryLauncher.Core/WarmProgress.cs`
- Test: `tests/VictoryLauncher.Tests/WarmOutputTests.cs`; die vorhandenen `WarmProgressTests` bleiben unverändert grün

**Interfaces:**
- Produces:
  - `record WarmStatus(int Count, int? Total, double? Percent, string Unit, bool Estimated = false)` mit `string Describe()`
  - `record WarmVerdict(bool Complete, string? Summary = null, int? RememberTotal = null)`
  - `abstract class WarmOutput`: `void Feed(string line)`, `WarmStatus? Status`, `WarmVerdict Verdict()` (nur nach Exit-Code 0 gefragt), `virtual string? FailureHint => null`. Abgeleitete Klassen füllen `protected abstract void Read(string line)`, `WarmStatus? Current()`, `WarmVerdict Judge()`. Die Basis hält das Lock und bietet `protected static bool TryNumber(Match match, string group, out int number)`.
  - `sealed class WarmProgress(int total, bool estimated = false, bool strict = true) : WarmOutput`, die bisherigen Eigenschaften `Total`, `Count`, `Percent`, `ResultLine`, `ResultTotal` bleiben. `strict` (AnyXboxX): `Status` von Anfang an, fertig nur mit Ergebniszeile. Sonst: `Status` erst nach der ersten verstandenen Zeile, immer fertig. `Summary` = `ResultLine`, `RememberTotal` = `ResultTotal`.
- `Describe()`: ohne `Total` `"{Count} {Unit}"`, sonst `"{Count} of {Total} {Unit} · {Percent:0}%"`; bei `Estimated` steht statt der Zahl `about {Total} (estimated)`.

- [ ] **Step 1: Failing tests schreiben**

```csharp
public class WarmReaderTests
{
    // DexNom 64bd58f, apps/victory/src/translate.rs
    private const string DexNomResult =
        "warm         25700 translated, 5 refused; 2700 compute pipelines, 0 refused; 53 omitted by ray-tracing mode; 398 s";
    // VItoriaBr 1361e19, apps/victory/src/warm.rs
    private const string VitoriaResult =
        "containers   25889 distinct: 25685 translated now, 0 already cached, 204 do not translate (142.0 s)";

    [Fact]
    public void The_result_line_with_the_extra_field_counts()
    {
        var reader = new WarmProgress(25758, estimated: true, strict: false);
        reader.Feed(DexNomResult);
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, DexNomResult, 25705), reader.Verdict());
    }

    [Fact]
    public void A_warmup_whose_lines_mean_nothing_here_is_still_done()
    {
        var reader = new WarmProgress(25758, estimated: true, strict: false);
        reader.Feed(VitoriaResult);
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true), reader.Verdict());
    }

    [Fact]
    public void The_strict_reader_shows_from_the_start_and_wants_its_result_line()
    {
        var reader = new WarmProgress(25758);
        Assert.Equal(0, reader.Status!.Count);
        Assert.Equal(new WarmVerdict(false), reader.Verdict());
    }

    [Fact]
    public void An_estimate_is_called_one()
    {
        var reader = new WarmProgress(25758, estimated: true);
        reader.Feed("    63 s  2000 shaders");
        Assert.Equal("2,000 of about 25,758 (estimated) shaders · 8%", reader.Status!.Describe());
    }

    [Theory]
    [InlineData("    63 s  99999999999999999999 shaders")]
    [InlineData("warm         99999999999999999999 translated, 5 refused; 1 compute pipelines, 0 refused; 4 s")]
    public void A_number_too_large_to_read_is_ignored(string line)
    {
        var reader = new WarmProgress(10);
        reader.Feed(line);
        Assert.Equal(0, reader.Count);
        Assert.Null(reader.ResultLine);
    }
}
```

- [ ] **Step 2: Rot sehen.** `dotnet test tests/VictoryLauncher.Tests` → Übersetzungsfehler, `WarmVerdict` und die neuen Parameter fehlen.
- [ ] **Step 3: Umsetzen.** `WarmOutput.cs` anlegen, `WarmProgress` davon ableiten. Die Ergebnis-Regex lässt `; N omitted by ray-tracing mode` als optionales Feld zu. `int.Parse` wird zu `TryNumber`.
- [ ] **Step 4: Grün sehen.** Alle Tests bestehen, auch die alten `WarmProgressTests`.
- [ ] **Step 5: Commit** (nur mit Commits): `Read warmup progress through one shape, DexNom included`

---

### Task 2: Vice-Tay und VIctory-7900XTX

**Files:**
- Create: `src/VictoryLauncher.Core/ViceTayOutput.cs`
- Test: `tests/VictoryLauncher.Tests/WarmOutputTests.cs`

**Interfaces:**
- Consumes: `WarmOutput`, `WarmStatus`, `WarmVerdict`
- Produces: `sealed class ShaderCacheOutput(int? lastTotal) : WarmOutput` (Einheit `shaders`), `sealed class KnownPipelinesOutput : WarmOutput` (Einheit `pipelines`)

- [ ] **Step 1: Failing tests schreiben**

```csharp
// Vice-Tay 79d78bc, apps/victory/src/translate.rs
public class ShaderCacheOutputTests
{
    private const string Counting = "1000 shaders in the cache, 14 s";
    private const string Done = "25685 shaders in the cache, 204 do not translate, 142 s";

    [Fact]
    public void The_first_time_it_only_counts()
    {
        var reader = new ShaderCacheOutput(null);
        Assert.Null(reader.Status);
        reader.Feed(Counting);
        Assert.Equal("1,000 shaders", reader.Status!.Describe());
        Assert.Null(reader.Status!.Percent);
    }

    [Fact]
    public void The_end_line_is_not_a_count_and_gives_the_total_to_remember()
    {
        var reader = new ShaderCacheOutput(null);
        reader.Feed(Done);
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, "25685 shaders in the cache, 204 do not translate", 25889), reader.Verdict());
    }

    [Fact]
    public void Later_it_shows_a_percentage_of_the_last_total()
    {
        var reader = new ShaderCacheOutput(2000);
        reader.Feed(Counting);
        Assert.Equal(50, reader.Status!.Percent);
        reader.Feed("3000 shaders in the cache, 40 s");
        Assert.Equal(99, reader.Status!.Percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_last_total_that_makes_no_sense_is_left_out(int total)
    {
        var reader = new ShaderCacheOutput(total);
        reader.Feed(Counting);
        Assert.Null(reader.Status!.Percent);
    }

    [Fact]
    public void A_number_too_large_to_read_is_ignored()
    {
        var reader = new ShaderCacheOutput(null);
        reader.Feed("99999999999999999999 shaders in the cache, 1 s");
        Assert.Null(reader.Status);
    }
}

// Vice-Tay 79d78bc, apps/victory/src/warm.rs (the same in VIctory-7900XTX 2de9980)
public class KnownPipelinesOutputTests
{
    private const string NothingMade =
        "0 made in 0 s (0 have no host pipeline, 0 ended their process); ms each: median 0.0, p90 0.0, p99 0.0, most 0, sum 0 s";

    private static string Header(int known, int before, int toMake) =>
        $"{known} pipelines known, {before} made before (0123456789abcdef), 0 bad; {toMake} to make, 8 processes";

    [Fact]
    public void Shows_the_share_of_what_is_to_make()
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed(Header(84000, 0, 2000));
        Assert.Equal("0 of 2,000 pipelines · 0%", reader.Status!.Describe());
        reader.Feed("1000 of 2000, 35 s");
        Assert.Equal("1,000 of 2,000 pipelines · 50%", reader.Status!.Describe());
        // the last count can reach the total before the end line is there
        reader.Feed("2000 of 2000, 70 s");
        Assert.Equal(99, reader.Status!.Percent);
    }

    [Fact]
    public void The_end_line_sums_up()
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed(Header(84000, 0, 2000));
        reader.Feed("1999 made in 71 s (3 have no host pipeline, 1 ended their process); ms each: median 12.0, " +
                    "p90 40.1, p99 300.5, most 2100, sum 190 s");
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, "1996 pipelines built, 3 have no host pipeline, 1 ended their process"),
            reader.Verdict());
    }

    [Theory]
    [InlineData(0, 0, "No pipelines recorded yet, so there was nothing to build.")]
    [InlineData(84000, 83990, "All 83990 recorded pipelines were built before, nothing new to build.")]
    [InlineData(500, 0, "None of the 500 recorded pipelines had to be built.")]
    public void Says_why_there_was_nothing_to_build(int known, int before, string summary)
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed(Header(known, before, 0));
        reader.Feed(NothingMade);
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true, summary), reader.Verdict());
    }

    [Fact]
    public void A_number_too_large_to_read_is_ignored()
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed("99999999999999999999 of 2000, 35 s");
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true), reader.Verdict());
    }
}
```

`All … built before` gilt bei `before > 0` und nichts zu bauen. Der dritte Fall kommt bei `warm --used` vor.

- [ ] **Step 2: Rot sehen** → die beiden Klassen fehlen.
- [ ] **Step 3: Beide Klassen umsetzen.** Alle Regexe sind am Zeilenanfang und Zeilenende verankert, nur die Endzeile von `warm` darf nach `ms each:` beliebig weitergehen.
- [ ] **Step 4: Grün sehen.**
- [ ] **Step 5: Commit** (nur mit Commits): `Read the warmup progress of Vice-Tay and VIctory-7900XTX`

---

### Task 3: klbf-lab und kelvaroq

**Files:**
- Create: `src/VictoryLauncher.Core/ShaderPackOutput.cs`, `src/VictoryLauncher.Core/PrecompileOutput.cs`
- Test: `tests/VictoryLauncher.Tests/WarmOutputTests.cs`

**Interfaces:**
- Produces: `sealed class ShaderPackOutput : WarmOutput` mit `FailureHint` = `"Start the warmup again: it then skips the pipeline the driver died on."`; `sealed class PrecompileOutput : WarmOutput`. Beide Einheit `pipelines`.

- [ ] **Step 1: Failing tests schreiben**

```csharp
// klbf-lab 0c529ca, apps/victory/src/prebuild.rs
public class ShaderPackOutputTests
{
    private const string Start =
        "pipelines    52000 in the pack, 1000 already pre-built, 51000 to make on 8 threads";

    [Fact]
    public void Reads_a_report_behind_a_path_with_a_drive_letter()
    {
        var reader = new ShaderPackOutput();
        reader.Feed(Start);
        Assert.Equal("0 of 51,000 pipelines · 0%", reader.Status!.Describe());
        reader.Feed(@"G:\DevElop\shaders.rpf: 600/51000 pipelines, 590 made, 10 found in the library, 61 s");
        Assert.Equal("600 of 51,000 pipelines · 1%", reader.Status!.Describe());
    }

    [Fact]
    public void More_than_the_total_stays_at_the_total_and_99_percent()
    {
        var reader = new ShaderPackOutput();
        reader.Feed(@"D:\my games: old\shaders.rpf: 103/100 pipelines, 100 made, 3 found in the library, 5 s");
        Assert.Equal(100, reader.Status!.Count);
        Assert.Equal(99, reader.Status!.Percent);
    }

    [Fact]
    public void The_end_line_sums_up()
    {
        var reader = new ShaderPackOutput();
        reader.Feed(Start);
        reader.Feed("pipelines    52000 in the pack, 3600.4 s: 50000 made, 1000 found in the library, 1000 already pre-built");
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, "50000 pipelines built, 1000 found in the library, 1000 already pre-built"),
            reader.Verdict());
    }

    [Fact]
    public void Nothing_to_make_shows_no_percentage()
    {
        var reader = new ShaderPackOutput();
        reader.Feed("pipelines    52000 in the pack, 52000 already pre-built, 0 to make on 8 threads");
        Assert.Null(reader.Status);
        Assert.True(reader.Verdict().Complete);
    }

    [Theory]
    [InlineData("2026-11-26T10:00:00Z  INFO victory_gpu::pipeline: host pipeline library saved bytes=5 made=3")]
    [InlineData("pipelines    no shader pack among the files: nothing to pre-build")]
    [InlineData("x: 99999999999999999999/100 pipelines, 1 made, 0 found in the library, 5 s")]
    public void Other_lines_are_not_progress(string line)
    {
        var reader = new ShaderPackOutput();
        reader.Feed(line);
        Assert.Null(reader.Status);
    }

    [Fact]
    public void A_failed_run_says_to_start_again() =>
        Assert.Equal("Start the warmup again: it then skips the pipeline the driver died on.",
            new ShaderPackOutput().FailureHint);
}

// kelvaroq 1270c42, native/patches/0059-preload.patch
public class PrecompileOutputTests
{
    private static PrecompileOutput Fed(params string[] lines)
    {
        var reader = new PrecompileOutput();
        foreach (var line in lines)
            reader.Feed(line);
        return reader;
    }

    [Fact]
    public void Shows_done_of_total() =>
        Assert.Equal("2 of 3 pipelines · 67%", Fed("precompile 0 3 0", "precompile 2 3 0").Status!.Describe());

    [Fact]
    public void Almost_done_is_not_100_percent() =>
        Assert.Equal(99, Fed("precompile 999 1000 0").Status!.Percent);

    [Fact]
    public void Nothing_to_do_is_done()
    {
        var reader = Fed("precompile 0 0 0");
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true, "Nothing to build: kelvaroq only knows pipelines that a game run has used."),
            reader.Verdict());
    }

    [Fact]
    public void Stopping_early_is_unfinished() =>
        Assert.Equal(new WarmVerdict(false, "Warmup incomplete: 0 of 3 pipelines. Start it again."),
            Fed("precompile 0 3 0").Verdict());

    [Fact]
    public void No_report_at_all_is_unfinished() =>
        Assert.Equal(new WarmVerdict(false, "Warmup incomplete: the program reported no progress. Start it again."),
            Fed().Verdict());

    [Theory]
    [InlineData("precompile 3 3 0", "3 pipelines built")]
    [InlineData("precompile 3 3 1", "2 pipelines built, 1 failed and will be tried again next time")]
    public void The_end_sums_up(string line, string summary) =>
        Assert.Equal(new WarmVerdict(true, summary), Fed(line).Verdict());

    [Theory]
    [InlineData("precompile-check 3")]
    [InlineData("WARN precompile 1 2 3 could not be saved")]
    [InlineData("precompile 99999999999999999999 3 0")]
    public void Other_lines_are_not_progress(string line) => Assert.Null(Fed(line).Status);
}
```

- [ ] **Step 2: Rot sehen.**
- [ ] **Step 3: Beide Klassen umsetzen.** klbf-labs Fortschritts-Regex ist nur hinten verankert (der Pfad davor ist beliebig). Bei kelvaroq gilt die letzte Zeile.
- [ ] **Step 4: Grün sehen.**
- [ ] **Step 5: Commit** (nur mit Commits): `Read the warmup progress of klbf-lab and kelvaroq`

---

### Task 4: `WarmPlan` kennt kelvaroq und wählt den Leser

**Files:**
- Modify: `src/VictoryLauncher.Core/WarmPlan.cs`
- Test: `tests/VictoryLauncher.Tests/WarmPlanTests.cs`

**Interfaces:**
- Consumes: alle Leser aus Task 1 bis 3
- Produces:
  - `WarmKind.Precompile`
  - `Detect(string help, string warmHelp, string translateHelp, string precompileHelp = "")`
  - `static WarmOutput Reader(WarmKind kind, IReadOnlyList<string> step, int? lastTotal)`: `GameFiles` → `WarmProgress` streng, `GameFolder` → `WarmProgress` nicht streng, beide mit `lastTotal ?? EstimatedTotal` und `estimated` bei `null` oder `<= 0`; `ShadersThenPipelines` → bei `step[0] == "shaders"` `ShaderCacheOutput`, sonst `KnownPipelinesOutput`; `ShaderPack`, `Precompile` → ihr Leser; `None` → `ArgumentOutOfRangeException`
  - `static TimeSpan? CancelGrace(WarmKind kind)`: 120 s bei `Precompile`, sonst `null`
- Texte für `About`:
  - `ShadersThenPipelines`: `The warmup translates the shaders. Its second step builds pipelines from a record of earlier game runs, which this launcher does not switch on yet.`
  - `Precompile`: `The warmup builds ahead the pipelines that your earlier game runs used. That helps mainly after an update, not in places you have not seen yet. Play once first.`

- [ ] **Step 1: Failing tests schreiben.** Bei `The_other_kinds_get_no_folders` kommt `[InlineData(WarmKind.Precompile)]` dazu.

```csharp
private const string PrecompileWithCheck =
    "Usage: victory.exe precompile [OPTIONS]\n\nOptions:\n      --cache <CACHE>  Cache root\n      --check          Only count";

[Fact]
public void A_precompile_with_check_comes_before_everything_else() =>
    Assert.Equal(WarmKind.Precompile, WarmPlan.Detect(Help("translate", "precompile", "warm", "run"),
        WarmFiles, TranslateWithCache, PrecompileWithCheck));

[Fact]
public void A_precompile_without_check_is_not_taken() =>
    Assert.Equal(WarmKind.ShaderPack, WarmPlan.Detect(Help("translate", "precompile", "run"), "",
        TranslateWithCache, "Usage: victory.exe precompile"));

[Fact]
public void The_precompile_warmup_is_one_command() =>
    Assert.Equal([["precompile"]], WarmPlan.Steps(WarmKind.Precompile, @"G:\game"));

[Theory]
[InlineData(WarmKind.GameFiles, "warm", typeof(WarmProgress))]
[InlineData(WarmKind.GameFolder, "warm", typeof(WarmProgress))]
[InlineData(WarmKind.ShadersThenPipelines, "shaders", typeof(ShaderCacheOutput))]
[InlineData(WarmKind.ShadersThenPipelines, "warm", typeof(KnownPipelinesOutput))]
[InlineData(WarmKind.ShaderPack, "translate", typeof(ShaderPackOutput))]
[InlineData(WarmKind.Precompile, "precompile", typeof(PrecompileOutput))]
public void Each_step_gets_its_reader(WarmKind kind, string command, Type reader) =>
    Assert.IsType(reader, WarmPlan.Reader(kind, [command], null));

[Fact]
public void Only_the_game_files_reader_wants_a_result_line()
{
    Assert.False(WarmPlan.Reader(WarmKind.GameFiles, ["warm"], null).Verdict().Complete);
    Assert.True(WarmPlan.Reader(WarmKind.GameFolder, ["warm"], null).Verdict().Complete);
}

[Theory]
[InlineData(null, WarmProgress.EstimatedTotal, true)]
[InlineData(0, WarmProgress.EstimatedTotal, true)]
[InlineData(100, 100, false)]
public void The_shader_count_uses_the_last_total_or_the_estimate(int? last, int total, bool estimated)
{
    var status = WarmPlan.Reader(WarmKind.GameFiles, ["warm"], last).Status!;
    Assert.Equal(total, status.Total);
    Assert.Equal(estimated, status.Estimated);
}

[Theory]
[InlineData(WarmKind.GameFiles, null)]
[InlineData(WarmKind.GameFolder, null)]
[InlineData(WarmKind.ShadersThenPipelines, null)]
[InlineData(WarmKind.ShaderPack, null)]
[InlineData(WarmKind.Precompile, 120)]
public void Only_precompile_is_asked_to_stop_itself(WarmKind kind, int? seconds) =>
    Assert.Equal(seconds, (int?)WarmPlan.CancelGrace(kind)?.TotalSeconds);

[Fact]
public void The_texts_say_what_the_warmup_cannot_do()
{
    Assert.Contains("does not switch on yet", WarmPlan.About(WarmKind.ShadersThenPipelines));
    Assert.Contains("Play once first", WarmPlan.About(WarmKind.Precompile));
}
```

- [ ] **Step 2: Rot sehen.**
- [ ] **Step 3: Umsetzen.** `Detect` prüft zuerst, ob `precompile` in der Befehlsliste steht und `CliHelp.HasOption(precompileHelp, "--check")` gilt.
- [ ] **Step 4: Grün sehen.**
- [ ] **Step 5: Commit** (nur mit Commits): `Know the precompile warmup and pick a reader for each step`

---

### Task 5: `ProcessRunner` kann ein Kind bitten, selbst aufzuhören

**Files:**
- Modify: `src/VictoryLauncher.Core/ProcessRunner.cs`
- Test: `tests/VictoryLauncher.Tests/ProcessAndProgressTests.cs` (Klasse `ProcessRunnerTests`)

**Interfaces:**
- Produces: `RunAsync(ProcessStartInfo start, Action<string> onLine, CancellationToken cancel, TimeSpan? closeInputGrace = null)`. Mit Frist: stdin wird umgeleitet und offen gehalten. Abbrechen schließt sie und wartet bis zur Frist, erst dann wird der Prozessbaum beendet. In beiden Fällen kommt `OperationCanceledException`, und alle Zeilen, die das Kind noch geschrieben hat, sind bei `onLine` angekommen, wenn es selbst geendet hat. Ohne Frist ändert sich nichts.

- [ ] **Step 1: Failing tests schreiben**

```csharp
private static ProcessStartInfo PowerShell(string script)
{
    var psi = new ProcessStartInfo("powershell.exe");
    foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
        psi.ArgumentList.Add(argument);
    return psi;
}

private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

[Fact]
public async Task The_input_stays_open_while_the_child_works()
{
    var lines = new ConcurrentQueue<string>();
    var code = await ProcessRunner.RunAsync(PowerShell(
        "$r = [Console]::OpenStandardInput().ReadAsync((New-Object byte[] 1), 0, 1); " +
        "Start-Sleep -Milliseconds 1500; \"ended=$($r.IsCompleted)\"; exit 0"),
        lines.Enqueue, CancellationToken.None, Grace).WaitAsync(TimeSpan.FromSeconds(60));
    Assert.Equal(0, code);
    Assert.Contains("ended=False", lines);
}

[Fact]
public async Task Cancel_closes_the_input_and_lets_the_child_finish()
{
    var lines = new ConcurrentQueue<string>();
    using var cts = new CancellationTokenSource();
    var run = ProcessRunner.RunAsync(PowerShell(
        "'ready'; $null = [Console]::In.ReadToEnd(); Start-Sleep -Milliseconds 500; 'saved'"),
        // not Cancel() here: what waits for it would go on inside this callback, which is still reading the output
        line => { lines.Enqueue(line); if (line == "ready") cts.CancelAfter(1); }, cts.Token, Grace);

    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(60)));
    Assert.Contains("saved", lines);
}

[Fact]
public async Task A_child_that_ignores_the_closed_input_is_killed_after_the_grace()
{
    using var cts = new CancellationTokenSource(500);
    var watch = Stopwatch.StartNew();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(
        Cmd("ping -n 30 127.0.0.1"), _ => { }, cts.Token, TimeSpan.FromSeconds(1)));
    Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(15));
}

[Fact]
public async Task A_child_with_an_open_input_that_ends_by_itself_returns_its_code() =>
    Assert.Equal(7, await ProcessRunner.RunAsync(Cmd("exit 7"), _ => { }, CancellationToken.None, Grace)
        .WaitAsync(TimeSpan.FromSeconds(60)));
```

- [ ] **Step 2: Rot sehen** → die Überladung mit vier Argumenten fehlt. Die beiden PowerShell-Skripte liefen am 2026-11-26 in einem Wegwerf-Versuch mit Windows PowerShell durch, bei offener und bei geschlossener stdin. Hängt ein Test trotzdem, zuerst das Skript allein prüfen.
- [ ] **Step 3: Umsetzen.** Beim Schließen eine `IOException` abfangen (das Kind kann gerade geendet haben). Das Warten auf die Frist ist ein `await` mit eigenem `CancellationTokenSource(grace)`, kein blockierendes Warten, weil Abbrechen aus dem Oberflächen-Thread kommt. Läuft die Frist ab, folgt der vorhandene Weg mit `Kill(entireProcessTree: true)`.
- [ ] **Step 4: Grün sehen**, auch `Cancel_kills_the_whole_tree_quickly`.
- [ ] **Step 5: Commit** (nur mit Commits): `Let a child stop by itself when its input closes`

---

### Task 6: Anzeige im Fenster

**Files:**
- Create: `src/VictoryLauncher.Core/WarmView.cs`
- Modify: `src/VictoryLauncher/MainWindow.xaml.cs` (`Cancel_Click`, `WarmKindOf`, `Warm`, `Refresh`, die Warmup-Felder)
- Test: `tests/VictoryLauncher.Tests/WarmOutputTests.cs`

**Interfaces:**
- Consumes: `WarmPlan.Reader`, `WarmPlan.CancelGrace`, `RunAsync` mit Frist
- Produces: `record WarmView(string Text, double? Percent)` mit `static WarmView Of(WarmStatus? status, IReadOnlyList<string> stepNames, int step, string clock, string lastLine)`; `step` zählt ab 0. `Percent == null` heißt Balken ohne Ende.

- [ ] **Step 1: Failing tests schreiben**

```csharp
public class WarmViewTests
{
    [Fact]
    public void Without_a_status_it_shows_the_step_and_the_last_line() =>
        Assert.Equal(new WarmView("Warmup translate · 1:05\ncache  x", null),
            WarmView.Of(null, ["translate"], 0, "1:05", "cache  x"));

    [Fact]
    public void With_several_steps_it_says_which_one() =>
        Assert.Equal(new WarmView("Warmup step 2 of 2 (warm) · 0:03\n", null),
            WarmView.Of(null, ["shaders", "warm"], 1, "0:03", ""));

    [Fact]
    public void A_long_last_line_is_cut() =>
        Assert.Equal("Warmup warm · 0:01\n" + new string('x', 110) + " …",
            WarmView.Of(null, ["warm"], 0, "0:01", new string('x', 200)).Text);

    [Fact]
    public void A_status_moves_the_bar() =>
        Assert.Equal(new WarmView("Step 2 of 2 · 1,000 of 2,000 pipelines · 50% · 5:12", 50),
            WarmView.Of(new WarmStatus(1000, 2000, 50, "pipelines"), ["shaders", "warm"], 1, "5:12", "x"));

    [Fact]
    public void A_count_without_a_total_leaves_the_bar_running() =>
        Assert.Equal(new WarmView("3,000 shaders · 1:23", null),
            WarmView.Of(new WarmStatus(3000, null, null, "shaders"), ["warm"], 0, "1:23", "x"));

    [Fact]
    public void AnyXboxX_reads_as_before() =>
        Assert.Equal("2,000 of about 25,758 (estimated) shaders · 8% · 1:03",
            WarmView.Of(new WarmStatus(2000, 25758, 7.76, "shaders", true), ["warm"], 0, "1:03", "").Text);
}
```

- [ ] **Step 2: Rot sehen.**
- [ ] **Step 3: `WarmView` umsetzen**, Tests grün.
- [ ] **Step 4: `MainWindow` anschließen.**
  - Felder: der laufende `WarmOutput?`, die Schrittnamen, der Index des Schritts und die letzte Zeile. `_warm` und `_warmStep` in der bisherigen Form entfallen.
  - `WarmKindOf` holt auch `precompile --help`, wenn die Befehlsliste `precompile` nennt, und gibt es an `Detect` weiter.
  - `Warm`: je Schritt ein neuer Leser aus `WarmPlan.Reader(kind, steps[i], State.LastWarmTotal)`, die letzte Zeile wird geleert, und `RunAsync` bekommt `WarmPlan.CancelGrace(kind)`.
  - Exit-Code ungleich 0: `Warmup failed at {Schritt} (exit code {code}). {FailureHint} Details in logs\warm.log.` (ohne Hinweis fällt er samt Leerzeichen weg).
  - Exit-Code 0, aber `Complete == false`: Hat das Urteil eine `Summary` (kelvaroq), steht dort `{Summary} Details in logs\warm.log.`. Hat es keine (AnyXboxX ohne Ergebniszeile), bleibt die bisherige Meldung `Warmup failed at {Schritt} (exit code 0). Details in logs\warm.log.`.
  - Weder bei einem Fehler noch bei einem unfertigen Lauf wird etwas als erledigt gespeichert, und der Balken bleibt bei `Status?.Percent ?? 0`.
  - Erfolg: Text wie bisher mit `Summary` des letzten Schritts, ersatzweise seiner letzten Zeile. `LastWarmTotal` bekommt das erste `RememberTotal` aller Schritte, sonst `null`.
  - `Refresh`: Text und Balken kommen aus `WarmView.Of`; `WarmBar.IsIndeterminate = view.Percent is null`.
  - `Cancel_Click`: Läuft ein Warmup mit Frist, heißt der Text `Cancelling … the fork finishes the running pipelines and saves.`
- [ ] **Step 5: Prüfen.** `dotnet test tests/VictoryLauncher.Tests` ganz grün, `dotnet build -c Release` mit 0 Warnungen.
- [ ] **Step 6: Commit** (nur mit Commits): `Show the warmup progress of every fork`

---

### Task 7: README und Prüfung in der echten App

**Files:**
- Modify: `README.md`

- [ ] **Step 1: README.** Warmup-Spalte:

| Fork | Text |
|---|---|
| AnyXboxX | `yes, with a percentage` |
| klbf-lab | ``yes (`translate`), with a percentage`` |
| Vice-Tay, VIctory-7900XTX | ``yes (`shaders`: a count, from the second time a percentage; the pipeline step has nothing to build yet)`` |
| kelvaroq | ``yes (`precompile`): the pipelines of earlier game runs, with a percentage`` |
| VItoriaBr | ``yes (`warm`), without a percentage`` |
| victory-fork | `no` |
| VIctory (DexNom) | ``yes (`warm`), with a percentage`` |

Unter „What the launcher does“ kommt vor „Driver cache“:
`- **Warmup progress:** It is read from the lines the fork prints. For all forks except AnyXboxX it was written from their sources and has not been tried against the running program. If the lines do not match, you see the last line of the output instead (kelvaroq reports the warmup as incomplete then).`

An „Driver cache“ wird angehängt:
`What the pipeline step of Vice-Tay and VIctory-7900XTX builds is only kept there.`

- [ ] **Step 2: Echte App mit AnyXboxX.** Testkopie mit `dotnet publish src/VictoryLauncher -c Release -o <Ordner im Scratchpad>` (sie sucht nie nach Updates). Vorher prüfen, dass kein `victory.exe` und kein anderer Launcher läuft. Läuft einer, wird nachgefragt und nichts beendet. Die Testkopie aktualisiert und baut dabei `G:\AnyXboxX` und schreibt in `G:\victory-data\cache-anyxboxx`, wie der installierte Launcher.
  Erwartet: Text `… of … shaders · N% · Zeit` mit laufendem Balken ab Beginn, am Ende `Warmup finished after …: warm …` und Balken voll; `logs\warm.log` enthält die Ergebniszeile; danach ist der Warmup-Hinweis weg.
- [ ] **Step 3: Abbrechen** im selben Aufbau: Warmup starten, CANCEL. Erwartet: `Warmup cancelled. The pipeline library was not saved, please repeat the warmup later.` und kein `victory.exe` mehr.
- [ ] **Step 4: Schlussprüfung.** Ganze Testsuite und Release-Build, `git status` und `git diff --stat` ansehen, nur die eigene Testkopie schließen.
- [ ] **Step 5: Commit** (nur mit Commits): `Describe the warmup progress`

Im Bericht steht ausdrücklich, was ungeprüft bleibt: die Anzeige für klbf-lab, Vice-Tay, VIctory-7900XTX, kelvaroq, VItoriaBr und DexNom an laufenden Programmen.
