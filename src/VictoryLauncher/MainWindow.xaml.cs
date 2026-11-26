using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using VictoryLauncher.Core;
using Microsoft.Win32;

namespace VictoryLauncher;

public partial class MainWindow : Window
{
    private enum Activity { Idle, Updating, Warming, Playing }

    /// <summary>An entry of the fork list; without a fork it is the one for typing an own URL.</summary>
    private sealed record ForkChoice(string Label, ForkProfile? Fork)
    {
        public override string ToString() => Label;
    }

    private static readonly string Home = AppContext.BaseDirectory;
    private static readonly string SettingsFile = Path.Combine(Home, "settings.json");
    private static readonly TimeSpan HelpLimit = TimeSpan.FromSeconds(30);

    private readonly LogSink _log = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch _clock = new();

    private LauncherSettings _settings = LauncherSettings.Load(SettingsFile);
    private ForkProfile _fork;
    private Activity _activity = Activity.Idle;
    private CancellationTokenSource? _cancel;
    private string _head = "";
    // What the last check found, and the fork and checkout it belongs to.
    private (string ForkId, string RepoDir, string? SourceId, WarmKind Warm)? _checked;
    // The running warmup: the reader of its step, which of the steps that is, and the last line of the output.
    private WarmOutput? _warm;
    private IReadOnlyList<string> _warmSteps = [];
    private int _warmStep;
    private string _warmLine = "";
    // A cancelled warmup of this fork is given time to stop by itself.
    private bool _warmStopsItself;
    private bool _closeWhenIdle;
    // True while the launcher itself fills the fields.
    private bool _showing = true;

    public MainWindow()
    {
        InitializeComponent();
        Title = WindowName;
        _fork = _settings.Selected();
        ListForks();
        ShowFork();
        // From here on a change in the list or in a field comes from the user.
        _showing = false;

        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        // On opening only a fork that is already there and agreed to is touched.
        Loaded += async (_, _) =>
        {
            var known = _settings.LoadProblem is null;
            if (!known)
                WarnAboutSettings();
            _ = CheckForUpdate();
            // With the standard values instead of the user's, the folders may be the wrong ones.
            if (known && _settings.IsTrusted(_fork) && ForkCheckout.Exists(Setup.RepoDir))
                await Guard(async () => { await UpdateAndBuild(); });
        };
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            try
            {
                // With an update in place, Update_Click has saved already, and saving may no longer work.
                if (_successor is null)
                    SaveSettings();
                _log.Dispose();
            }
            finally
            {
                StartSuccessor();
            }
        };
        SetActivity(Activity.Idle);
    }

    private ForkSetup Setup => _settings.Setup(_fork);

    private ForkState State => _settings.State(_fork.Id);

    /// <summary>True while the list shows the entry for an own URL: then there is no fork to act on.</summary>
    private bool TypingUrl => ForkBox.SelectedItem is ForkChoice { Fork: null };

    // ---------- fork list ----------

    private void ListForks()
    {
        var showing = _showing;
        _showing = true;
        var choices = ForkCatalog.All(_settings.CustomUrls).Select(fork => new ForkChoice(fork.Name, fork)).ToList();
        choices.Add(new("Own Git URL …", null));
        ForkBox.ItemsSource = choices;
        ForkBox.SelectedItem = choices.FirstOrDefault(c => c.Fork?.Id == _fork.Id) ?? choices[0];
        _showing = showing;
    }

    private void Fork_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || ForkBox.SelectedItem is not ForkChoice choice)
            return;
        if (choice.Fork is { } fork)
        {
            _fork = fork;
            _settings = _settings with { SelectedFork = fork.Id };
            SaveSettings();
        }
        ShowFork();
        SetActivity(_activity);
        if (choice.Fork is null)
            UrlBox.Focus();
    }

    /// <summary>Fills the window for the chosen fork. Nothing is fetched or built here.</summary>
    private void ShowFork()
    {
        var showing = _showing;
        _showing = true;
        var setup = Setup;
        var typing = TypingUrl;

        UrlLabel.Visibility = UrlBox.Visibility = AddUrlButton.Visibility =
            typing ? Visibility.Visible : Visibility.Collapsed;
        RemoveForkButton.Visibility = !typing && _fork.IsCustom ? Visibility.Visible : Visibility.Hidden;
        ForkNote.Text = typing
            ? "Enter the https address of a repository with a VIctory layout, for example https://github.com/name/project"
            : string.Join("\n", new[]
            {
                _fork.Url,
                _fork.Note,
                setup.DataDir is { } data ? $"Save games: {data}" : null,
            }.Where(line => !string.IsNullOrEmpty(line)));

        GameBox.Text = _settings.GameDir;
        CacheBox.Text = typing ? "" : setup.CacheDir;
        RepoBox.Text = typing ? "" : setup.RepoDir;

        WarmBar.Value = 0;
        VersionText.Text = typing ? ""
            : ForkCheckout.Exists(setup.RepoDir) ? "not checked yet"
            : "not checked out yet";
        StatusText.Text = typing ? "Enter the address and add it." : "Ready.";
        _showing = showing;
        ShowCheckButton();
        ShowHint();
    }

    private void ShowCheckButton() =>
        CheckButton.Content = !TypingUrl && !ForkCheckout.Exists(Setup.RepoDir) ? "CHECK OUT" : "CHECK";

    private void Url_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddUrl_Click(sender, e);
    }

    private void AddUrl_Click(object sender, RoutedEventArgs e)
    {
        if (_activity != Activity.Idle)
            return;
        var (url, error) = GitUrl.Parse(UrlBox.Text);
        if (url is null)
        {
            Warn(error!);
            return;
        }
        // An address that is already in the list selects that entry, with its settings.
        var known = ForkCatalog.All(_settings.CustomUrls).FirstOrDefault(f => GitUrl.Same(f.Url, url));
        if (known is null)
        {
            _settings = _settings.AddCustom(url);
            known = ForkCatalog.Custom(url);
        }
        _fork = known;
        _settings = _settings with { SelectedFork = known.Id };
        SaveSettings();
        UrlBox.Text = "";
        ListForks();
        ShowFork();
        SetActivity(Activity.Idle);
    }

    private void RemoveFork_Click(object sender, RoutedEventArgs e)
    {
        if (_activity != Activity.Idle || !_fork.IsCustom || MessageBox.Show(this,
                $"Remove {_fork.Name} from the list?\n\nFiles stay where they are:\n{Setup.RepoDir}\n{Setup.CacheDir}",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        // The agreement goes with the entry: adding the address again asks again.
        var forks = new Dictionary<string, ForkState>(_settings.Forks);
        forks.Remove(_fork.Id);
        _settings = _settings.RemoveCustom(_fork.Url) with { Forks = forks, SelectedFork = ForkCatalog.DefaultId };
        _fork = _settings.Selected();
        SaveSettings();
        ListForks();
        ShowFork();
        SetActivity(Activity.Idle);
    }

    /// <summary>Asked once per address, before anything of it is checked out, built or run.</summary>
    private bool Trust()
    {
        if (_settings.IsTrusted(_fork))
            return true;
        var answer = MessageBox.Show(this,
            $"{_fork.Name}\n{_fork.Url}\n\n" +
            "The launcher checks out this fork, builds it and runs it. This is third party code that nobody has " +
            "reviewed. It runs with your permissions, already while it is being built. Later commits are built " +
            $"without asking again.\n\n{_fork.Note}\n\nCheckout to: {Setup.RepoDir}\n\nDo you trust this fork?",
            "Run third party code?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return false;
        _settings = _settings.With(_fork.Id, State with { TrustedUrl = _fork.Url });
        SaveSettings();
        return true;
    }

    // ---------- buttons ----------

    private async void Check_Click(object sender, RoutedEventArgs e) =>
        await Guard(async () => { await UpdateAndBuild(askOnFailure: true, onlyCheck: true); });

    private async void Warm_Click(object sender, RoutedEventArgs e) => await Guard(Warm);

    private async void Play_Click(object sender, RoutedEventArgs e) => await Guard(Play);

    // Not behind Guard: the keys are wanted while a warmup or the game runs, too.
    private void Controls_Click(object sender, RoutedEventArgs e)
    {
        if (!TypingUrl)
            new ControlsDialog(_fork) { Owner = this }.ShowDialog();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => RequestCancel();

    private void RequestCancel()
    {
        CancelButton.IsEnabled = false;
        StatusText.Text = _warmStopsItself
            ? "Cancelling … the fork finishes the running pipelines and saves."
            : "Cancelling …";
        _cancel?.Cancel();
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e) => Browse(GameBox, "Select the game folder");

    private void BrowseCache_Click(object sender, RoutedEventArgs e) => Browse(CacheBox, "Select the cache folder");

    private void BrowseRepo_Click(object sender, RoutedEventArgs e) => Browse(RepoBox, "Select the checkout folder");

    private void Browse(TextBox box, string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (Directory.Exists(box.Text))
            dialog.InitialDirectory = box.Text;
        if (dialog.ShowDialog(this) == true)
            box.Text = dialog.FolderName;
    }

    private void Paths_Changed(object sender, TextChangedEventArgs e)
    {
        if (_showing || sender is not TextBox box)
            return;
        var folder = box.Text.Trim().Trim('"');
        if (box == GameBox)
            _settings = _settings with { GameDir = folder };
        else if (box == CacheBox)
            _settings = _settings.With(_fork.Id, State with { CacheDir = folder });
        else
            _settings = _settings.With(_fork.Id, State with { RepoDir = folder });
        ShowCheckButton();
        ShowHint();
    }

    // ---------- check out, update and build ----------

    /// <param name="askOnFailure">A click asked for it: say what is wrong in a message.</param>
    /// <param name="onlyCheck">Nothing is going to be run afterwards, so there is nothing to carry on with.</param>
    /// <returns>True when warmup or game may go on.</returns>
    private async Task<bool> UpdateAndBuild(bool askOnFailure = false, bool onlyCheck = false)
    {
        var setup = Setup;
        if (VictoryIsRunning())
            return Stop("victory.exe is already running. Please close it first.");
        if (PathCheck.RepoFolder(setup) is { } folderProblem)
            return Stop(folderProblem);
        if (!Trust())
        {
            StatusText.Text = "Cancelled. Nothing was checked out or built.";
            return false;
        }
        if (Tools.Find("git") is not { } git)
            return Stop("git was not found.");
        var tools = new Dictionary<string, string>();
        foreach (var name in _fork.Build.Select(step => step.Tool).Distinct())
        {
            if (Tools.Find(name) is not { } path)
                return Stop(name == "cargo"
                    ? "cargo was not found (Rust is not installed)."
                    : $"{name} was not found, this fork needs it for the build.");
            tools[name] = path;
        }

        _log.Open(Path.Combine(Home, "logs", "update.log"));
        _log.Add($"Fork: {_fork.Name} ({_fork.Url}) in {setup.RepoDir}");
        _clock.Restart();
        _checked = null;
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        WarmBar.IsIndeterminate = true;
        try
        {
            VersionText.Text = "checking …";
            StatusText.Text = ForkCheckout.Exists(setup.RepoDir) ? "Checking the checkout …" : "Checking out the fork …";
            AllowCancel(true);
            var checkoutProblem = await ForkCheckout.EnsureAsync(git, _fork.Url, setup.RepoDir, _log.Add, cancel.Token);
            AllowCancel(false);
            if (checkoutProblem is not null)
                return Stop(checkoutProblem);

            StatusText.Text = "Checking for new commits …";
            var updater = new RepoUpdater(git, setup.RepoDir);
            var update = await updater.CheckAndPullAsync(_log.Add, CancellationToken.None);
            _head = update.Head;
            if (PathCheck.Checkout(setup) is { } layoutProblem)
                return Stop(layoutProblem);
            var sourceId = await updater.SourceIdAsync(setup.SourcePaths(), _log.Add, CancellationToken.None);

            var buildOk = await Build(setup, tools, cancel.Token);
            var warm = File.Exists(setup.ExePath()) ? await WarmKindOf(setup) : WarmKind.None;
            if (File.Exists(setup.ExePath()))
                _checked = (_fork.Id, setup.RepoDir, sourceId, warm);

            var commit = _head.Length >= 7 ? _head[..7] : "unknown";
            var state = update.State switch
            {
                UpdateState.UpToDate => "up to date",
                UpdateState.Updated => $"updated (new commits: {update.NewCommits.Count})",
                UpdateState.CheckFailed => "NOT checked",
                _ => "NOT up to date",
            };
            VersionText.Text = $"{commit} · {state} · {(buildOk ? "release built" : "build FAILED")}";
            StatusText.Text = "Ready.";

            var updateOk = update.State is UpdateState.UpToDate or UpdateState.Updated;
            if (updateOk && buildOk)
                return true;

            var problem = string.Join("\n\n", new[]
            {
                updateOk ? null : update.Message,
                buildOk ? null : "The build failed, details in logs\\update.log.",
            }.Where(text => text is not null));
            StatusText.Text = problem;
            if (!askOnFailure)
                return false;
            if (onlyCheck || !File.Exists(setup.ExePath()))
            {
                Warn(problem);
                return false;
            }
            return MessageBox.Show(this, problem + "\n\nCarry on with the program that is already there?", Title,
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            VersionText.Text = "cancelled";
            StatusText.Text = "Cancelled.";
            return false;
        }
        finally
        {
            _cancel = null;
            AllowCancel(false);
            WarmBar.IsIndeterminate = false;
            ShowCheckButton();
            ShowHint();
        }

        bool Stop(string message)
        {
            VersionText.Text = message;
            StatusText.Text = "Not ready.";
            if (askOnFailure)
                Warn(message);
            return false;
        }
    }

    /// <summary>Runs the fork's build steps. A step that depends on new commits is left out while nothing changed.</summary>
    private async Task<bool> Build(ForkSetup setup, IReadOnlyDictionary<string, string> tools, CancellationToken cancel)
    {
        var unchanged = State.LastBuiltCommit == _head && _head.Length > 0 && File.Exists(setup.ExePath());
        for (var i = 0; i < _fork.Build.Count; i++)
        {
            var step = _fork.Build[i];
            if (step.OnlyAfterChanges && unchanged)
            {
                _log.Add($"Skipped, unchanged since the last build: {step.Tool} {string.Join(' ', step.Arguments)}");
                continue;
            }
            var count = _fork.Build.Count > 1 ? $" Step {i + 1} of {_fork.Build.Count}." : "";
            StatusText.Text = "Building the release (only takes long after changes) …" + count;
            AllowCancel(step.Cancellable);
            var code = await ProcessRunner.RunAsync(BuildCommand(setup, step, tools), _log.Add,
                step.Cancellable ? cancel : CancellationToken.None);
            AllowCancel(false);
            if (code != 0)
                return false;
        }
        if (!File.Exists(setup.ExePath()))
        {
            _log.Add($"The build finished, but there is no {setup.ExePath()}.");
            return false;
        }
        _settings = _settings.With(_fork.Id, State with { LastBuiltCommit = _head });
        SaveSettings();
        return true;
    }

    private static ProcessStartInfo BuildCommand(ForkSetup setup, BuildStep step, IReadOnlyDictionary<string, string> tools)
    {
        var start = new ProcessStartInfo(tools[step.Tool]) { WorkingDirectory = setup.RuntimeDir()! };
        foreach (var argument in step.Arguments)
            start.ArgumentList.Add(argument);
        // The launcher expects the programs in the checkout's own target folder.
        start.Environment.Remove("CARGO_TARGET_DIR");
        start.Environment["CARGO_TERM_COLOR"] = "never";
        // rustc sits next to cargo, and build scripts call cargo or python themselves; a program started from
        // Explorer may not have these folders on PATH.
        var folders = tools.Values.Select(Path.GetDirectoryName);
        start.Environment["PATH"] = string.Join(Path.PathSeparator,
            folders.Append(Environment.GetEnvironmentVariable("PATH")));
        return start;
    }

    /// <summary>What the built program's help offers, adjusted to the fork.</summary>
    private async Task<WarmKind> WarmKindOf(ForkSetup setup)
    {
        try
        {
            var help = await Help(setup, "--help");
            var commands = CliHelp.Commands(help);
            var warm = commands.Contains("warm") ? await Help(setup, "warm", "--help") : "";
            var translate = commands.Contains("translate") ? await Help(setup, "translate", "--help") : "";
            var precompile = commands.Contains("precompile") ? await Help(setup, "precompile", "--help") : "";
            var kind = WarmPlan.Refine(WarmPlan.Detect(help, warm, translate, precompile),
                setup.Fork.WarmsEveryGameFile);
            _log.Add($"Warmup kind according to the help of the program: {kind}");
            return kind;
        }
        catch (Exception e) when (e is OperationCanceledException or Win32Exception or IOException)
        {
            _log.Add("The help of the program could not be read: " + e.Message);
            return WarmKind.None;
        }
    }

    private static async Task<string> Help(ForkSetup setup, params string[] arguments)
    {
        var start = new ProcessStartInfo(setup.ExePath()) { WorkingDirectory = Home };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        var lines = new List<string>();
        using var limit = new CancellationTokenSource(HelpLimit);
        await ProcessRunner.RunAsync(start, line => { lock (lines) lines.Add(line); }, limit.Token);
        lock (lines)
            return string.Join('\n', lines);
    }

    // ---------- warmup and game ----------

    private async Task Warm()
    {
        if (!Ready(PathCheck.Folders) || !await UpdateAndBuild(askOnFailure: true) || !Ready(PathCheck.Checkout))
            return;

        var setup = Setup;
        var kind = CheckedWarm() ?? WarmKind.None;
        var steps = WarmPlan.Steps(kind, setup.GameDir, setup.Fork.WarmsEveryGameFile);
        if (steps.Count == 0)
        {
            StatusText.Text = WarmPlan.About(WarmKind.None);
            return;
        }
        var starts = steps.Select(arguments => Game(setup, arguments)).ToList();
        WarmPlan.PrepareCache(kind, setup.CacheDir);
        var commit = _head;
        // Taken now: it has to describe what this warmup runs with.
        var fingerprint = Fingerprint();
        var grace = WarmPlan.CancelGrace(kind);

        _log.Open(Path.Combine(Home, "logs", "warm.log"));
        _log.Add($"Fork: {_fork.Name}, Commit {commit}");
        using var cancel = new CancellationTokenSource();
        _cancel = cancel;
        _warmSteps = steps.Select(arguments => arguments[0]).ToList();
        _warmStopsItself = grace is not null;
        _clock.Restart();
        SetActivity(Activity.Warming);
        WarmBar.IsIndeterminate = true;
        try
        {
            var verdicts = new List<WarmVerdict>();
            for (var i = 0; i < starts.Count; i++)
            {
                // The lines go to the reader of the step that wrote them.
                var reader = WarmPlan.Reader(kind, steps[i], State.LastWarmTotal);
                _warmStep = i;
                _warmLine = "";
                _warm = reader;
                _log.Add($"> {Path.GetFileName(setup.ExePath())} {string.Join(' ', steps[i])}");
                var code = await ProcessRunner.RunAsync(starts[i], line =>
                {
                    reader.Feed(line);
                    if (!string.IsNullOrWhiteSpace(line))
                        _warmLine = line;
                    _log.Add(line);
                }, cancel.Token, grace);

                var verdict = code == 0 ? reader.Verdict() : null;
                if (verdict is not { Complete: true })
                {
                    EndWarmDisplay(reader.Status?.Percent ?? 0);
                    var failed = $"Warmup failed at {WarmView.StepName(_warmSteps, i)} (exit code {code}).";
                    // after exit code 0 the reader may know better than "failed" what happened
                    var what = verdict is null
                        ? string.Join(" ", new[] { failed, reader.FailureHint }.Where(part => part is not null))
                        : verdict.Summary ?? failed;
                    StatusText.Text = what + " Details in logs\\warm.log.";
                    return;
                }
                verdicts.Add(verdict);
            }

            var result = verdicts[^1].Summary ?? _warmLine;
            EndWarmDisplay(100);
            StatusText.Text = $"Warmup finished after {Clock()}: " + Shorten(Regex.Replace(result, @"\s+", " "), 220);
            _settings = _settings.With(_fork.Id, State with
            {
                LastWarmCommit = commit,
                LastWarmFingerprint = fingerprint,
                LastWarmTotal = verdicts.Select(verdict => verdict.RememberTotal).FirstOrDefault(total => total is not null),
            });
            SaveSettings();
            ShowHint();
        }
        catch (OperationCanceledException)
        {
            EndWarmDisplay(null);
            StatusText.Text = kind == WarmKind.GameFiles
                ? "Warmup cancelled. The pipeline library was not saved, please repeat the warmup later."
                : "Warmup cancelled, please repeat it later.";
        }
        finally
        {
            _warm = null;
            _warmStopsItself = false;
            _cancel = null;
        }
    }

    /// <summary>The warmup is over: the bar stops, and nothing overwrites the message any more.</summary>
    /// <param name="percent">Where the bar stays, or null to leave it.</param>
    private void EndWarmDisplay(double? percent)
    {
        _warm = null;
        _warmStopsItself = false;
        SetActivity(Activity.Updating);
        WarmBar.IsIndeterminate = false;
        if (percent is { } value)
            WarmBar.Value = value;
    }

    private async Task Play()
    {
        if (!Ready(PathCheck.Folders) || !await UpdateAndBuild(askOnFailure: true) || !Ready(PathCheck.Checkout))
            return;
        if (!await CheckPrograms())
        {
            StatusText.Text = "Start cancelled.";
            return;
        }

        var setup = Setup;
        var start = Game(setup, ["run", setup.GameExe(), "--execute"]);
        SaveSettings();
        _log.Open(Path.Combine(Home, "logs", "game.log"));
        _log.Add($"Fork: {_fork.Name}, Commit {_head}");
        _clock.Restart();
        SetActivity(Activity.Playing);
        var code = await ProcessRunner.RunAsync(start, _log.Add, CancellationToken.None);
        SetActivity(Activity.Updating);
        StatusText.Text = code == 0
            ? $"Game closed after {Clock()}."
            : $"The game ended with exit code {code} after {Clock()}. Details in logs\\game.log.";
    }

    // ---------- programs to close before the game ----------

    /// <summary>Shows what is worth closing and closes what was chosen. A failing check never blocks the start.</summary>
    /// <returns>False when the user does not want to start.</returns>
    private async Task<bool> CheckPrograms()
    {
        try
        {
            StatusText.Text = "Checking running programs …";
            WarmBar.IsIndeterminate = true;
            var scan = await ProcessScanner.ScanAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            if (scan.Warning is { } warning)
                _log.Add(warning);
            var found = ProgramCheck.Select(scan.Processes, scan.OwnSession, scan.FreeRamBytes,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            _log.Add($"Program check: programs reported: {found.Count}");
            if (found.Count == 0)
                return true;

            var dialog = new ProgramDialog(found) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice != ProgramChoice.CloseSelected)
                return dialog.Choice == ProgramChoice.StartAnyway;

            StatusText.Text = "Closing the selected programs …";
            _log.Add("Closing: " + Names(dialog.Selected));
            return await ProgramCloser.CloseForStartAsync(dialog.Selected, TimeSpan.FromSeconds(8), AskForce,
                stuck =>
                {
                    _log.Add("Could not be closed: " + Names(stuck));
                    Warn("These programs could not be closed, the game starts anyway:\n\n" + Bullets(stuck));
                }, CancellationToken.None);
        }
        catch (Exception e)
        {
            _log.Add("Program check failed: " + e);
            return true;
        }
        finally
        {
            WarmBar.IsIndeterminate = false;
        }
    }

    private ForceAnswer AskForce(IReadOnlyList<ProgramCandidate> remaining)
    {
        var answer = MessageBox.Show(this,
            "These programs are still running:\n\n" + Bullets(remaining) +
            "\n\nIf one of them is asking whether to save, please answer it there first." +
            "\n\nYes: force close and start. Anything not saved is lost." +
            "\nNo: leave them running and start." +
            "\nCancel: do not start.",
            "Force close?", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.No);
        var choice = answer switch
        {
            MessageBoxResult.Yes => ForceAnswer.Force,
            MessageBoxResult.No => ForceAnswer.Leave,
            _ => ForceAnswer.Cancel,
        };
        _log.Add($"Still running: {Names(remaining)}. Answer: {choice}");
        return choice;
    }

    private static string Names(IEnumerable<ProgramCandidate> programs) => string.Join(", ", programs.Select(p => p.Name));

    private static string Bullets(IEnumerable<ProgramCandidate> programs) =>
        string.Join("\n", programs.Select(p => "•  " + p.Name));

    private bool Ready(Func<ForkSetup, string?> check)
    {
        if (check(Setup) is not { } problem)
            return true;
        Warn(problem);
        return false;
    }

    /// <summary>Warmup and game get the same environment: the cache key depends on it.</summary>
    private static ProcessStartInfo Game(ForkSetup setup, IEnumerable<string> arguments)
    {
        // Read after the update: a tuning script comes from the checkout.
        var settings = setup.ReadSettings();
        Directory.CreateDirectory(setup.CacheDir);
        if (setup.DataDir is { } data)
            Directory.CreateDirectory(data);
        var exe = setup.ExePath();
        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = setup.Fork.RunsFromExeFolder ? Path.GetDirectoryName(exe)! : Home,
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        LaunchEnvironment.Apply(start.Environment, settings, setup.GameDir, setup.CacheDir, setup.DataDir);
        return start;
    }

    // ---------- window state ----------

    /// <summary>One thing at a time, and no error may take the window down.</summary>
    private async Task Guard(Func<Task> action)
    {
        if (_activity != Activity.Idle || TypingUrl)
            return;
        SetActivity(Activity.Updating);
        try
        {
            await action();
        }
        catch (Exception e)
        {
            _log.Add(e.ToString());
            StatusText.Text = "Error: " + e.Message;
            Warn(e.Message);
        }
        finally
        {
            WarmBar.IsIndeterminate = false;
            SetActivity(Activity.Idle);
            if (_closeWhenIdle)
                Close();
        }
    }

    private void SetActivity(Activity activity)
    {
        _activity = activity;
        var idle = activity == Activity.Idle;
        foreach (var control in new Control[] { ForkBox, UrlBox, AddUrlButton, RemoveForkButton, GameBox, GameBrowse, UpdateButton })
            control.IsEnabled = idle;
        var forkChosen = idle && !TypingUrl;
        foreach (var control in new Control[] { CheckButton, PlayButton, CacheBox, RepoBox, CacheBrowse, RepoBrowse })
            control.IsEnabled = forkChosen;
        WarmButton.IsEnabled = forkChosen && CheckedWarm() != WarmKind.None;
        ControlsButton.IsEnabled = !TypingUrl;
        CancelButton.IsEnabled = activity == Activity.Warming;
        PlayButton.Content = activity == Activity.Playing ? "GAME RUNNING …" : "START GAME";
    }

    /// <summary>For the parts of a check that may be stopped: cloning and the cargo build.</summary>
    private void AllowCancel(bool allowed) => CancelButton.IsEnabled = allowed;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBar.Evening(this);
    }

    private void Refresh()
    {
        if (_log.TryTakeTail(out var text))
        {
            LogBox.Text = text;
            LogBox.ScrollToEnd();
        }
        if (_activity == Activity.Warming && _cancel is { IsCancellationRequested: false })
        {
            if (_warm is { } warm)
            {
                var view = WarmView.Of(warm.Status, _warmSteps, _warmStep, Clock(), _warmLine);
                WarmBar.IsIndeterminate = view.Percent is null;
                if (view.Percent is { } percent)
                    WarmBar.Value = percent;
                StatusText.Text = view.Text;
            }
        }
        else if (_activity == Activity.Playing)
        {
            StatusText.Text = $"The game has been running for {Clock()}. To quit, close the game window.";
        }
        else if (Volatile.Read(ref _downloadPercent) is var percent and >= 0 && _cancel is { IsCancellationRequested: false })
        {
            WarmBar.Value = percent;
            StatusText.Text = $"Downloading version {_update?.Version} … {percent}%";
        }
    }

    private static string Shorten(string text, int length) => text.Length <= length ? text : text[..length] + " …";

    private string Clock() => _clock.Elapsed.ToString(_clock.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");

    /// <returns>The warmup of the checked build, or null while it is unknown.</returns>
    private WarmKind? CheckedWarm() =>
        _checked is { } last && last.ForkId == _fork.Id && last.RepoDir == Setup.RepoDir ? last.Warm : null;

    private void ShowHint()
    {
        if (TypingUrl || CheckedWarm() is not { } kind)
        {
            HintText.Text = "";
            return;
        }
        var hint = kind == WarmKind.None ? "" : WarmState.Hint(State, Fingerprint());
        HintText.Text = string.Join(" ", new[] { hint, WarmPlan.About(kind) }.Where(part => part.Length > 0));
    }

    /// <returns>Null while it cannot be told: before the first check of this fork and checkout.</returns>
    private string? Fingerprint()
    {
        var setup = Setup;
        if (CheckedWarm() is null || _checked?.SourceId is not { } sourceId)
            return null;
        try
        {
            return WarmState.Fingerprint(sourceId, setup.ReadSettings(), setup.GameDir, setup.CacheDir, setup.DataDir);
        }
        // The hint must not get in the way; warmup and game report an unreadable script themselves.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            return null;
        }
    }

    private void WarnAboutSettings()
    {
        _log.Add($"{SettingsFile} could not be read: {_settings.LoadProblem}");
        Warn("settings.json could not be read. The default values apply. So that nothing gets lost, the file is not " +
             "overwritten: changes only last until you close the launcher.\n\n" +
             "Repair or delete the file and restart the launcher, then it saves again.\n\n" +
             $"{SettingsFile}\n{_settings.LoadProblem}");
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(SettingsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Add("Settings not saved: " + e.Message);
        }
    }

    private static bool VictoryIsRunning() =>
        Process.GetProcessesByName("victory").Length > 0 || Process.GetProcessesByName("kelvaroq-engine").Length > 0;

    private void Warn(string message) =>
        MessageBox.Show(this, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        switch (_activity)
        {
            case Activity.Playing:
                e.Cancel = true;
                Warn("The game is still running. Please close the game window first.");
                break;
            case Activity.Updating when !CancelButton.IsEnabled:
                e.Cancel = true;
                Warn("The launcher is still busy. Please wait a moment.");
                break;
            case Activity.Updating:
            case Activity.Warming:
                e.Cancel = true;
                var question = _activity == Activity.Warming ? "The warmup is still running. Cancel it and close?"
                    : Volatile.Read(ref _downloadPercent) >= 0 ? "The update is still downloading. Cancel it and close?"
                    : "The checkout or build is still running. Cancel it and close?";
                if (MessageBox.Show(this, question, Title, MessageBoxButton.YesNo, MessageBoxImage.Question,
                        MessageBoxResult.No) == MessageBoxResult.Yes && _cancel is not null)
                {
                    _closeWhenIdle = true;
                    RequestCancel();
                }
                break;
        }
    }
}
