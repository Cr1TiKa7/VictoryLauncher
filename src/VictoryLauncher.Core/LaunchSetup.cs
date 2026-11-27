using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VictoryLauncher.Core;

/// <summary>What the launcher remembers about one fork.</summary>
public sealed record ForkState
{
    /// <summary>Folders chosen by the user; null: the standard ones.</summary>
    public string? RepoDir { get; init; }
    public string? CacheDir { get; init; }

    public string? LastWarmCommit { get; init; }
    public string? LastWarmFingerprint { get; init; }
    public int? LastWarmTotal { get; init; }

    /// <summary>The commit of the last build in which every step succeeded.</summary>
    public string? LastBuiltCommit { get; init; }

    /// <summary>The address the user agreed to build and run.</summary>
    public string? TrustedUrl { get; init; }

    /// <summary>The game sees a Series S instead of a Series X.</summary>
    public bool SeriesS { get; init; }

    /// <summary>The id of the chosen <see cref="Core.OutputSize"/>; null: the default.</summary>
    public string? OutputSize { get; init; }
}

public sealed record LauncherSettings
{
    private const string DefaultGameDir = @"G:\DevElop";
    private const string DefaultDataRoot = @"G:\victory-data";
    private const string DefaultAnyxRepo = @"G:\AnyXboxX";

    public string GameDir { get; init; } = DefaultGameDir;

    /// <summary>Checkouts, caches and saves of the forks go below this folder.</summary>
    public string DataRoot { get; init; } = DefaultDataRoot;

    public string SelectedFork { get; init; } = ForkCatalog.DefaultId;
    public IReadOnlyList<string> CustomUrls { get; init; } = [];
    public IReadOnlyDictionary<string, ForkState> Forks { get; init; } = new Dictionary<string, ForkState>();

    // The settings file of the AnyXboxX only version had these at the top; Load moves them to that fork.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RepoDir { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? CacheDir { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LastWarmCommit { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? LastWarmFingerprint { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? LastWarmTotal { get; init; }

    /// <summary>Set when there is a settings file that could not be read. Then these are the defaults.</summary>
    [JsonIgnore] public string? LoadProblem { get; init; }

    public ForkProfile Selected() => ForkCatalog.Find(SelectedFork, CustomUrls);

    public ForkState State(string id) => Forks.GetValueOrDefault(id) ?? new();

    public LauncherSettings With(string id, ForkState state) =>
        this with { Forks = new Dictionary<string, ForkState>(Forks) { [id] = state } };

    public LauncherSettings AddCustom(string url) =>
        CustomUrls.Any(known => GitUrl.Same(known, url)) ? this : this with { CustomUrls = [.. CustomUrls, url] };

    public LauncherSettings RemoveCustom(string url) =>
        this with { CustomUrls = [.. CustomUrls.Where(known => !GitUrl.Same(known, url))] };

    /// <summary>AnyXboxX was agreed to when the launcher was made for it; everything else is asked about once.</summary>
    public bool IsTrusted(ForkProfile fork) =>
        (fork.Id == ForkCatalog.DefaultId && fork.Url == ForkCatalog.BuiltIn[0].Url) || State(fork.Id).TrustedUrl == fork.Url;

    public ForkSetup Setup(ForkProfile fork)
    {
        var state = State(fork.Id);
        var repo = state.RepoDir ?? (fork.Id == ForkCatalog.DefaultId
            ? DefaultAnyxRepo
            : Path.Combine(DataRoot, "forks", fork.Id));
        var cache = state.CacheDir ?? Path.Combine(DataRoot, "cache-" + fork.Id);
        var data = fork.OwnDataDir ? Path.Combine(DataRoot, "data-" + fork.Id) : null;
        return new(fork, repo, cache, data, GameDir)
            { SeriesS = state.SeriesS, ChosenOutputSize = state.OutputSize };
    }

    // Somebody else may hold the file for a moment, a virus scanner for example.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(100);

    public static LauncherSettings Load(string path)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var text = File.ReadAllText(path);
                return string.IsNullOrWhiteSpace(text)
                    ? new()
                    : (JsonSerializer.Deserialize<LauncherSettings>(text) ?? new()).Cleaned();
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                return new();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && waited.Elapsed < Patience)
            {
                Thread.Sleep(Pause);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return new() { LoadProblem = e.Message };
            }
        }
    }

    /// <summary>
    /// Puts a complete new file in place of the old one. A file that could not be read is left alone: it holds more
    /// than the defaults that were taken instead.
    /// </summary>
    public void Save(string path)
    {
        if (LoadProblem is not null)
            return;
        var fresh = path + ".tmp";
        File.WriteAllText(fresh, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                File.Move(fresh, path, overwrite: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && waited.Elapsed < Patience)
            {
                Thread.Sleep(Pause);
            }
        }
    }

    /// <summary>The file can be edited by hand: repair what is missing and check every own address again.</summary>
    private LauncherSettings Cleaned()
    {
        var forks = (Forks ?? new Dictionary<string, ForkState>())
            .Where(entry => entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value);

        if ((RepoDir ?? CacheDir ?? LastWarmCommit ?? LastWarmFingerprint) is not null && !forks.ContainsKey(ForkCatalog.DefaultId))
            forks[ForkCatalog.DefaultId] = new()
            {
                RepoDir = RepoDir,
                CacheDir = CacheDir,
                LastWarmCommit = LastWarmCommit,
                LastWarmFingerprint = LastWarmFingerprint,
                LastWarmTotal = LastWarmTotal,
            };

        return this with
        {
            GameDir = GameDir ?? DefaultGameDir,
            DataRoot = DataRoot ?? DefaultDataRoot,
            SelectedFork = SelectedFork ?? ForkCatalog.DefaultId,
            CustomUrls = [.. (CustomUrls ?? []).Select(url => GitUrl.Parse(url).Url).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase)],
            Forks = forks,
            RepoDir = null,
            CacheDir = null,
            LastWarmCommit = null,
            LastWarmFingerprint = null,
            LastWarmTotal = null,
        };
    }
}

/// <summary>One fork with the folders it uses.</summary>
public sealed record ForkSetup(ForkProfile Fork, string RepoDir, string CacheDir, string? DataDir, string GameDir)
{
    private static readonly string[] RootSources = ["apps", "crates", "Cargo.toml", "Cargo.lock", "rust-toolchain.toml"];

    /// <summary>The layouts an own URL may have: the runtime at the root, or kept under third_party.</summary>
    private static readonly string[] KnownLayouts = ["", @"third_party\victory"];

    /// <summary>The game sees a Series S instead of a Series X.</summary>
    public bool SeriesS { get; init; }

    /// <summary>The id of the chosen output size, as it is in the settings file.</summary>
    public string? ChosenOutputSize { get; init; }

    /// <summary>The output size of the next run, or null where the fork or the console offers none.</summary>
    public OutputSize? Output => Fork.ChoosesOutputSize && !SeriesS ? OutputSize.Of(ChosenOutputSize) : null;

    /// <summary>
    /// How the game is started. Series X is the runtime's own default, so only Series S is named.
    /// </summary>
    public IReadOnlyList<string> PlayArguments() => SeriesS
        ? ["run", GameExe(), "--execute", "--console", "series-s"]
        : ["run", GameExe(), "--execute"];

    /// <returns>The full path of the runtime folder, or null while an own fork shows no known layout.</returns>
    public string? RuntimeDir() => Relative() is { } relative ? Path.Combine(RepoDir, relative) : null;

    public string ExePath() => Path.Combine(RuntimeDir() ?? RepoDir, Fork.Exe);

    public string GameExe() => Path.Combine(GameDir, PathCheck.GameExeName);

    public string? TuningFile() => Fork.TuningFile is { } file ? Path.Combine(RepoDir, file) : null;

    /// <summary>The paths, as git names them, whose content the caches can depend on.</summary>
    public IReadOnlyList<string> SourcePaths() =>
        Fork.SourcePaths ?? (Relative() is { Length: > 0 } folder ? [folder.Replace('\\', '/')] : RootSources);

    /// <summary>The VICTORY_* variables of this fork. Read after an update: a tuning file is part of the checkout.</summary>
    public IReadOnlyDictionary<string, string> ReadSettings()
    {
        var settings = BaseSettings();
        if (Output is { } output && output.Id != OutputSize.TitlesOwn)
        {
            foreach (var name in OutputSize.Variables)
                settings[name] = output.Id;
        }
        return settings;
    }

    /// <summary>
    /// What the warmup hint compares: everything but a chosen output size. The translated shaders do not depend on
    /// it, so changing it asks for no new warmup.
    /// </summary>
    public IReadOnlyDictionary<string, string> WarmCacheSettings() => BaseSettings();

    private Dictionary<string, string> BaseSettings()
    {
        var settings = TuningFile() is { } file
            ? new Dictionary<string, string>(Tuning.Parse(File.ReadAllText(file)), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in Fork.Settings)
            settings[name] = value;
        if (!SeriesS)
        {
            foreach (var (name, value) in Fork.SeriesXSettings)
                settings[name] = value;
        }
        return settings;
    }

    private string? Relative() => Fork.RuntimeDir
        ?? KnownLayouts.FirstOrDefault(layout => File.Exists(Path.Combine(RepoDir, layout, "apps", "victory", "Cargo.toml")));
}

public static class PathCheck
{
    public const string GameExeName = "game_scarlett_master.exe";

    /// <summary>What has to hold before a fork is checked out or built. No game is needed for that.</summary>
    /// <returns>A message for the user, or null.</returns>
    public static string? RepoFolder(ForkSetup s)
    {
        if (!Path.IsPathFullyQualified(s.RepoDir))
            return "Please enter a full path for the checkout folder.";
        if (Path.IsPathFullyQualified(s.GameDir) && IsInside(s.RepoDir, s.GameDir))
            return "The checkout must not be inside the game folder, nothing is written there.";
        return null;
    }

    /// <summary>What has to hold before the game's files are read or anything is written for a run.</summary>
    /// <returns>A message for the user, or null.</returns>
    public static string? Folders(ForkSetup s)
    {
        if (!Path.IsPathFullyQualified(s.GameDir) || !File.Exists(s.GameExe()))
            return $"{GameExeName} is missing in the game folder: {s.GameDir}";
        if (RepoFolder(s) is { } problem)
            return problem;
        if (!Path.IsPathFullyQualified(s.CacheDir))
            return "Please enter a full path for the cache.";
        if (IsInside(s.CacheDir, s.RepoDir))
            return "The cache must not be inside the checkout (the runtime refuses that).";
        if (IsInside(s.CacheDir, s.GameDir))
            return "The cache must not be inside the game folder, nothing is written there.";
        if (s.DataDir is { } data)
        {
            if (!Path.IsPathFullyQualified(data))
                return "The folder for the save games (DataRoot in settings.json) is not a full path.";
            if (IsInside(data, s.RepoDir) || IsInside(data, s.GameDir))
                return "The save games must not be inside the checkout or the game folder.";
        }
        return null;
    }

    /// <summary>What the checkout has to contain.</summary>
    /// <returns>A message for the user, or null.</returns>
    public static string? Checkout(ForkSetup s)
    {
        if (s.RuntimeDir() is not { } runtime)
            return $"No VIctory layout found (apps\\victory is missing, also below third_party\\victory): {s.RepoDir}";
        if (!File.Exists(Path.Combine(runtime, "Cargo.toml")))
            return $"Cargo.toml is missing in the checkout: {runtime}";
        if (s.TuningFile() is { } file && !File.Exists(file))
            return $"{s.Fork.TuningFile} is missing in the checkout: {s.RepoDir}";
        return null;
    }

    private static bool IsInside(string path, string folder)
    {
        static string Normal(string p) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)) + Path.DirectorySeparatorChar;
        return Normal(path).StartsWith(Normal(folder), StringComparison.OrdinalIgnoreCase);
    }
}

public static class LaunchEnvironment
{
    /// <summary>Warmup and game must get the same variables: the cache key depends on them.</summary>
    /// <param name="dataDir">Where saves go, or null for the runtime's standard folder.</param>
    public static void Apply(IDictionary<string, string?> env, IReadOnlyDictionary<string, string> settings,
        string gameDir, string cacheDir, string? dataDir = null)
    {
        foreach (var stray in env.Keys.Where(k => k.StartsWith("VICTORY_", StringComparison.OrdinalIgnoreCase)).ToList())
            env.Remove(stray);
        foreach (var (name, value) in settings)
            env[name] = value;
        env["VICTORY_GAME_DIR"] = gameDir;
        env["VICTORY_CACHE_DIR"] = cacheDir;
        if (dataDir is not null)
            env["VICTORY_DATA_DIR"] = dataDir;
    }
}

public static class WarmInputs
{
    private static readonly string[] Binaries =
        [PathCheck.GameExeName, "XboxBvhBuilder.dll", "amd_fidelityfx_upscaler_xbox.dll"];

    public static string[] Collect(string gameDir) =>
    [
        .. Directory.GetFiles(gameDir, "*.rpf").Order(StringComparer.OrdinalIgnoreCase),
        .. Binaries.Select(name => Path.Combine(gameDir, name)).Where(File.Exists),
    ];
}
