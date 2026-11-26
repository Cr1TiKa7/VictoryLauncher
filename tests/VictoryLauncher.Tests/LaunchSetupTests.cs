using System.Runtime.InteropServices;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class LaunchSetupTests : IDisposable
{
    private const string Mine = "https://github.com/me/mine";
    private readonly string _root = Directory.CreateTempSubdirectory("anyx-setup-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private string Touch(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }

    private static ForkProfile Fork(string id) => ForkCatalog.Find(id, [Mine]);

    private ForkSetup ValidSetup()
    {
        Touch("repo", "third_party", "victory", "Cargo.toml");
        Touch("repo", "crates", "anyxboxx", "src", "run", "settings.rs");
        Touch("game", "game_scarlett_master.exe");
        return new ForkSetup(Fork("anyxboxx"), Path.Combine(_root, "repo"), Path.Combine(_root, "cache"), null,
            Path.Combine(_root, "game"));
    }

    private static string? Problem(ForkSetup setup) => PathCheck.Folders(setup) ?? PathCheck.Checkout(setup);

    [Fact]
    public void Defaults_match_the_spec()
    {
        var settings = LauncherSettings.Load(Path.Combine(_root, "missing.json"));
        var anyx = settings.Setup(Fork("anyxboxx"));

        Assert.Equal("anyxboxx", settings.Selected().Id);
        Assert.Equal(@"G:\DevElop", anyx.GameDir);
        Assert.Equal(@"G:\AnyXboxX", anyx.RepoDir);
        Assert.Equal(@"G:\victory-data\cache-anyxboxx", anyx.CacheDir);
        Assert.Null(anyx.DataDir);
        Assert.Null(settings.State("anyxboxx").LastWarmCommit);
    }

    [Fact]
    public void Every_other_fork_gets_folders_of_its_own()
    {
        var setup = new LauncherSettings().Setup(Fork("vice-tay"));

        Assert.Equal(@"G:\victory-data\forks\vice-tay", setup.RepoDir);
        Assert.Equal(@"G:\victory-data\cache-vice-tay", setup.CacheDir);
        Assert.Equal(@"G:\victory-data\data-vice-tay", setup.DataDir);
    }

    [Fact]
    public void Chosen_folders_are_kept_per_fork()
    {
        var settings = new LauncherSettings().With("vice-tay", new ForkState { RepoDir = @"D:\r", CacheDir = @"D:\c" });

        Assert.Equal(@"D:\r", settings.Setup(Fork("vice-tay")).RepoDir);
        Assert.Equal(@"D:\c", settings.Setup(Fork("vice-tay")).CacheDir);
        Assert.Equal(@"G:\victory-data\cache-kelvaroq", settings.Setup(Fork("kelvaroq")).CacheDir);
    }

    [Fact]
    public void Settings_survive_a_save_and_a_broken_file_gives_defaults_to_work_with()
    {
        var path = Path.Combine(_root, "settings.json");
        var saved = new LauncherSettings { GameDir = @"D:\game", SelectedFork = "kelvaroq" }
            .AddCustom(Mine)
            .With("kelvaroq", new ForkState { LastWarmCommit = "abc", LastWarmTotal = 7, TrustedUrl = "u", LastBuiltCommit = "def" });
        saved.Save(path);

        var loaded = LauncherSettings.Load(path);

        Assert.Equal("kelvaroq", loaded.SelectedFork);
        Assert.Equal(@"D:\game", loaded.GameDir);
        Assert.Equal([Mine], loaded.CustomUrls);
        Assert.Equal(saved.State("kelvaroq"), loaded.State("kelvaroq"));

        File.WriteAllText(path, "{ not json");
        Assert.Equal("anyxboxx", LauncherSettings.Load(path).SelectedFork);
        Assert.Empty(LauncherSettings.Load(path).CustomUrls);
    }

    private string SavedSettings(string fork)
    {
        var path = Path.Combine(_root, "settings.json");
        new LauncherSettings { SelectedFork = fork }.AddCustom(Mine).Save(path);
        return path;
    }

    // Right after an update the new version reads the file its predecessor has only just written.
    [Fact]
    public async Task A_file_that_is_held_for_a_moment_is_waited_for()
    {
        var path = SavedSettings("kelvaroq");
        var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = new Thread(() =>
        {
            Thread.Sleep(400);
            held.Dispose();
        });
        release.Start();

        var loaded = await Task.Run(() => LauncherSettings.Load(path));
        release.Join();

        Assert.Null(loaded.LoadProblem);
        Assert.Equal("kelvaroq", loaded.SelectedFork);
        Assert.Equal([Mine], loaded.CustomUrls);
    }

    [Fact]
    public async Task Saving_waits_for_a_file_that_is_held_for_a_moment()
    {
        var path = SavedSettings("kelvaroq");
        var loaded = LauncherSettings.Load(path);
        var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() =>
        {
            Thread.Sleep(400);
            held.Dispose();
        });
        release.Start();

        await Task.Run(() => (loaded with { SelectedFork = "vice-tay" }).Save(path));
        release.Join();

        Assert.Equal("vice-tay", LauncherSettings.Load(path).SelectedFork);
    }

    [Fact]
    public void A_file_that_stays_unreadable_is_never_overwritten()
    {
        var path = SavedSettings("kelvaroq");
        var before = File.ReadAllBytes(path);
        LauncherSettings loaded;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            loaded = LauncherSettings.Load(path);

        (loaded with { SelectedFork = "vice-tay" }).With("vice-tay", new ForkState { TrustedUrl = "u" }).Save(path);

        Assert.NotNull(loaded.LoadProblem);
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_damaged_file_stays_as_it_is()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{ "SelectedFork": "kelvaroq", """);

        var loaded = LauncherSettings.Load(path);
        loaded.Save(path);

        Assert.NotNull(loaded.LoadProblem);
        Assert.Equal("""{ "SelectedFork": "kelvaroq", """, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null)] // not there yet
    [InlineData("")] // nothing in it that could get lost
    [InlineData(" \r\n")]
    public void Without_anything_to_lose_the_defaults_are_saved(string? content)
    {
        var path = Path.Combine(_root, "settings.json");
        if (content is not null)
            File.WriteAllText(path, content);

        var loaded = LauncherSettings.Load(path);
        (loaded with { SelectedFork = "kelvaroq" }).Save(path);

        Assert.Null(loaded.LoadProblem);
        Assert.Equal("kelvaroq", LauncherSettings.Load(path).SelectedFork);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string newName, string existingFile, IntPtr security);

    [Fact]
    public void Saving_replaces_the_file_in_one_step()
    {
        var path = SavedSettings("kelvaroq");
        // a second name for the old content: writing into the file would change it too, replacing the file does not
        var before = Path.Combine(_root, "before.json");
        Assert.True(CreateHardLink(before, path, IntPtr.Zero));

        new LauncherSettings { SelectedFork = "vice-tay" }.Save(path);

        Assert.Equal("vice-tay", LauncherSettings.Load(path).SelectedFork);
        Assert.Equal("kelvaroq", LauncherSettings.Load(before).SelectedFork);
        Assert.Equal(["before.json", "settings.json"], Directory.GetFiles(_root).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void A_settings_file_of_the_anyxboxx_only_version_moves_over()
    {
        var path = Path.Combine(_root, "old.json");
        File.WriteAllText(path, """
            { "RepoDir": "E:\\repo", "GameDir": "E:\\game", "CacheDir": "E:\\cache",
              "LastWarmCommit": "abc", "LastWarmFingerprint": "f0", "LastWarmTotal": 25758 }
            """);

        var loaded = LauncherSettings.Load(path);

        Assert.Equal(@"E:\game", loaded.GameDir);
        Assert.Equal(@"E:\repo", loaded.Setup(Fork("anyxboxx")).RepoDir);
        Assert.Equal(@"E:\cache", loaded.Setup(Fork("anyxboxx")).CacheDir);
        var state = loaded.State("anyxboxx");
        Assert.Equal(("abc", "f0", 25758), (state.LastWarmCommit, state.LastWarmFingerprint, state.LastWarmTotal));

        loaded.Save(path);
        Assert.Equal(@"E:\repo", LauncherSettings.Load(path).Setup(Fork("anyxboxx")).RepoDir);
        Assert.Null(LauncherSettings.Load(path).RepoDir);
    }

    [Fact]
    public void Broken_lists_in_the_file_do_not_break_the_start()
    {
        var path = Path.Combine(_root, "nulls.json");
        File.WriteAllText(path, """{ "CustomUrls": null, "Forks": null, "GameDir": null, "DataRoot": null, "SelectedFork": null }""");

        var loaded = LauncherSettings.Load(path);

        Assert.Empty(loaded.CustomUrls);
        Assert.Equal(@"G:\DevElop", loaded.GameDir);
        Assert.Equal(@"G:\victory-data\forks\kelvaroq", loaded.Setup(Fork("kelvaroq")).RepoDir);
    }

    [Fact]
    public void Own_urls_are_checked_when_read_from_the_file()
    {
        var path = Path.Combine(_root, "bad.json");
        File.WriteAllText(path, """{ "CustomUrls": ["http://x.example/a/b", "https://github.com/me/mine", null, "--x"] }""");

        Assert.Equal([Mine], LauncherSettings.Load(path).CustomUrls);
    }

    [Fact]
    public void Adding_and_removing_an_own_url()
    {
        var settings = new LauncherSettings().AddCustom(Mine).AddCustom("https://github.com/ME/Mine");

        Assert.Single(settings.CustomUrls);
        Assert.Empty(settings.RemoveCustom(Mine).CustomUrls);
    }

    [Fact]
    public void Only_anyxboxx_is_trusted_without_a_question()
    {
        var settings = new LauncherSettings();

        Assert.True(settings.IsTrusted(Fork("anyxboxx")));
        Assert.All(ForkCatalog.BuiltIn.Where(f => f.Id != "anyxboxx").Append(ForkCatalog.Custom(Mine)),
            fork => Assert.False(settings.IsTrusted(fork)));
    }

    [Fact]
    public void Trust_belongs_to_the_address_it_was_given_for()
    {
        var kelvaroq = Fork("kelvaroq");
        var settings = new LauncherSettings().With("kelvaroq", new ForkState { TrustedUrl = kelvaroq.Url });

        Assert.True(settings.IsTrusted(kelvaroq));
        Assert.False(settings.IsTrusted(kelvaroq with { Url = "https://github.com/someone/else" }));
        Assert.False(settings.IsTrusted(Fork("vice-tay")));
    }

    [Fact]
    public void Finds_the_runtime_of_an_own_fork_at_the_root_or_under_third_party()
    {
        var setup = new ForkSetup(ForkCatalog.Custom(Mine), Path.Combine(_root, "mine"), @"G:\c", null, @"G:\g");
        Assert.Null(setup.RuntimeDir());

        Touch("mine", "third_party", "victory", "apps", "victory", "Cargo.toml");
        Assert.Equal(Path.Combine(setup.RepoDir, "third_party", "victory"), setup.RuntimeDir());
        Assert.Equal(["third_party/victory"], setup.SourcePaths());

        Touch("mine", "apps", "victory", "Cargo.toml");
        Assert.Equal(setup.RepoDir, setup.RuntimeDir());
        Assert.Contains("crates", setup.SourcePaths());
    }

    [Fact]
    public void The_exe_lies_under_the_runtime_folder()
    {
        var setup = ValidSetup();

        Assert.Equal(Path.Combine(setup.RepoDir, "third_party", "victory", "target", "release", "victory.exe"), setup.ExePath());
        Assert.Equal(["third_party/victory"], setup.SourcePaths());
    }

    [Fact]
    public void Settings_of_a_fork_override_the_ones_from_its_checkout()
    {
        var setup = ValidSetup();
        setup = setup with
        {
            Fork = setup.Fork with
            {
                Settings = new Dictionary<string, string> { ["VICTORY_CARD_BUFFERS_MB"] = "1024" },
            },
        };
        File.WriteAllText(Path.Combine(setup.RepoDir, "crates", "anyxboxx", "src", "run", "settings.rs"), """
            pub const TUNED: &[(&str, &str)] = &[("VICTORY_CARD_BUFFERS_MB", "1536"), ("VICTORY_RELAY", "2")];
            """);

        var settings = setup.ReadSettings();

        Assert.Equal("1024", settings["VICTORY_CARD_BUFFERS_MB"]);
        Assert.Equal("2", settings["VICTORY_RELAY"]);
    }

    [Fact]
    public void A_fork_without_a_tuning_file_uses_its_fixed_settings()
    {
        var setup = new LauncherSettings().Setup(Fork("victory-fork"));

        Assert.Equal("1", setup.ReadSettings()["VICTORY_TEXTURE_BACK"]);
    }

    [Fact]
    public void Accepts_a_valid_setup() => Assert.Null(Problem(ValidSetup()));

    [Fact]
    public void Folders_are_fine_before_anything_is_checked_out() =>
        Assert.Null(PathCheck.Folders(ValidSetup() with { RepoDir = Path.Combine(_root, "not-yet") }));

    [Fact]
    public void Names_what_is_missing_in_the_checkout()
    {
        var setup = ValidSetup() with { RepoDir = Path.Combine(_root, "not-yet") };

        Assert.Contains("Cargo.toml", PathCheck.Checkout(setup));
    }

    [Fact]
    public void An_own_fork_without_the_victory_layout_is_named_as_such()
    {
        Touch("flat", "Cargo.toml");
        var setup = ValidSetup() with { Fork = ForkCatalog.Custom(Mine), RepoDir = Path.Combine(_root, "flat") };

        Assert.Contains("No VIctory layout", PathCheck.Checkout(setup));
    }

    [Fact]
    public void Names_the_missing_game_executable()
    {
        var setup = ValidSetup() with { GameDir = Path.Combine(_root, "empty") };

        Assert.Contains("game_scarlett_master.exe", PathCheck.Folders(setup));
    }

    [Theory]
    [InlineData("repo")]
    [InlineData(@"repo\cache")]
    [InlineData("game")]
    [InlineData(@"GAME\cache\")]
    public void Refuses_a_cache_inside_the_checkout_or_the_game(string folder)
    {
        var setup = ValidSetup() with { CacheDir = Path.Combine(_root, folder) };

        Assert.Contains("The cache must not", PathCheck.Folders(setup));
    }

    [Theory]
    [InlineData(@"repo\data")]
    [InlineData(@"game\data")]
    public void Refuses_saves_inside_the_checkout_or_the_game(string folder)
    {
        var setup = ValidSetup() with { DataDir = Path.Combine(_root, folder) };

        Assert.Contains("save games", PathCheck.Folders(setup));
    }

    [Fact]
    public void Never_checks_out_into_the_game_folder()
    {
        var setup = ValidSetup() with { RepoDir = Path.Combine(_root, "game", "fork") };

        Assert.Contains("game folder", PathCheck.Folders(setup));
    }

    [Fact]
    public void Checking_out_and_building_needs_no_game_but_never_goes_into_its_folder()
    {
        var setup = ValidSetup() with { GameDir = Path.Combine(_root, "no-game-here") };

        Assert.Null(PathCheck.RepoFolder(setup));
        Assert.Contains("game folder", PathCheck.RepoFolder(setup with { RepoDir = Path.Combine(setup.GameDir, "x") }));
        Assert.NotNull(PathCheck.RepoFolder(setup with { RepoDir = "relative" }));
        // an empty game box must not make every folder look as if it were inside
        Assert.Null(PathCheck.RepoFolder(setup with { GameDir = "" }));
    }

    [Fact]
    public void A_sibling_with_the_same_prefix_is_not_inside() =>
        Assert.Null(Problem(ValidSetup() with { CacheDir = Path.Combine(_root, "repo-cache") }));

    [Theory]
    [InlineData("")]
    [InlineData("cache")]
    public void Relative_or_empty_folders_are_refused(string folder)
    {
        Assert.NotNull(PathCheck.Folders(ValidSetup() with { CacheDir = folder }));
        Assert.NotNull(PathCheck.Folders(ValidSetup() with { RepoDir = folder }));
    }

    [Fact]
    public void Environment_drops_stray_variables_and_sets_settings_and_folders()
    {
        var env = new Dictionary<string, string?> { ["VICTORY_FOO"] = "1", ["victory_bar"] = "1", ["PATH"] = "p" };
        var settings = new Dictionary<string, string> { ["VICTORY_RELAY"] = "2" };

        LaunchEnvironment.Apply(env, settings, @"G:\game", @"G:\cache", @"G:\data");

        Assert.False(env.ContainsKey("VICTORY_FOO"));
        Assert.False(env.ContainsKey("victory_bar"));
        Assert.Equal("p", env["PATH"]);
        Assert.Equal("2", env["VICTORY_RELAY"]);
        Assert.Equal(@"G:\game", env["VICTORY_GAME_DIR"]);
        Assert.Equal(@"G:\cache", env["VICTORY_CACHE_DIR"]);
        Assert.Equal(@"G:\data", env["VICTORY_DATA_DIR"]);
    }

    [Fact]
    public void Without_a_data_folder_the_runtime_keeps_its_standard_one()
    {
        var env = new Dictionary<string, string?> { ["VICTORY_DATA_DIR"] = "stray" };

        LaunchEnvironment.Apply(env, new Dictionary<string, string>(), @"G:\game", @"G:\cache", null);

        Assert.False(env.ContainsKey("VICTORY_DATA_DIR"));
    }

    [Fact]
    public void Warm_inputs_are_sorted_archives_then_the_binaries_present()
    {
        foreach (var name in new[] { "b.rpf", "a.rpf", "game_scarlett_master.exe", "XboxBvhBuilder.dll", "other.txt" })
            Touch("g", name);
        var dir = Path.Combine(_root, "g");

        var names = WarmInputs.Collect(dir).Select(Path.GetFileName);

        Assert.Equal(["a.rpf", "b.rpf", "game_scarlett_master.exe", "XboxBvhBuilder.dll"], names);
    }
}
