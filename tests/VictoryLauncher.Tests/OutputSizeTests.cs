using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class OutputSizeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("anyx-size-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static ForkProfile Fork(string id) => ForkCatalog.Find(id, []);

    /// <summary>AnyXboxX with a checkout of its own here: its settings are read from a file in it.</summary>
    private ForkSetup AnyXboxX(string? chosen = null, bool seriesS = false)
    {
        var tuning = Path.Combine(_root, "repo", "crates", "anyxboxx", "src", "run", "settings.rs");
        Directory.CreateDirectory(Path.GetDirectoryName(tuning)!);
        File.WriteAllText(tuning, """
            pub const TUNED: &[(&str, &str)] = &[("VICTORY_RELAY", "2")];
            """);
        var state = new ForkState { RepoDir = Path.Combine(_root, "repo"), OutputSize = chosen, SeriesS = seriesS };
        return new LauncherSettings().With("anyxboxx", state).Setup(Fork("anyxboxx"));
    }

    private static (string? Display, string? Output) SizeVariables(ForkSetup setup)
    {
        var settings = setup.ReadSettings();
        return (settings.GetValueOrDefault("VICTORY_DISPLAY_MODE"), settings.GetValueOrDefault("VICTORY_OUTPUT"));
    }

    [Fact]
    public void The_choices_are_sizes_the_title_has_a_mode_for_and_its_own()
    {
        Assert.Equal(
            ["1280x720", "1920x1080 (default)", "2560x1440", "3840x2160 (title's own)"],
            OutputSize.Choices.Select(choice => choice.Label));
        Assert.Equal(["anyxboxx"], ForkCatalog.BuiltIn.Where(fork => fork.ChoosesOutputSize).Select(fork => fork.Id));
        Assert.False(ForkCatalog.Custom("https://github.com/a/b").ChoosesOutputSize);
    }

    [Theory]
    [InlineData(null, "1920x1080")]
    [InlineData("1280x720", "1280x720")]
    [InlineData("1920x1080", "1920x1080")]
    [InlineData("2560x1440", "2560x1440")]
    // not one of the title's modes, for example typed into settings.json
    [InlineData("1600x900", "1920x1080")]
    public void The_chosen_size_goes_into_both_variables(string? chosen, string size) =>
        Assert.Equal((size, size), SizeVariables(AnyXboxX(chosen)));

    [Fact]
    public void The_titles_own_size_sets_neither_variable()
    {
        var setup = AnyXboxX(OutputSize.TitlesOwn);

        Assert.Equal((null, null), SizeVariables(setup));
        Assert.Equal("2", setup.ReadSettings()["VICTORY_RELAY"]);
    }

    [Fact]
    public void A_series_s_gets_no_size_whatever_is_chosen()
    {
        var setup = AnyXboxX("1280x720", seriesS: true);

        Assert.Null(setup.Output);
        Assert.Equal((null, null), SizeVariables(setup));
    }

    [Fact]
    public void Another_fork_takes_no_size()
    {
        var settings = new LauncherSettings().With("vice-tay", new ForkState { OutputSize = "1280x720" });

        var setup = settings.Setup(Fork("vice-tay"));

        Assert.Null(setup.Output);
        Assert.False(setup.ReadSettings().ContainsKey("VICTORY_OUTPUT"));
        Assert.False(setup.ReadSettings().ContainsKey("VICTORY_DISPLAY_MODE"));
    }

    [Theory]
    [InlineData("1280x720")]
    [InlineData(OutputSize.TitlesOwn)]
    public void The_choice_is_kept_in_the_settings_file(string chosen)
    {
        var path = Path.Combine(_root, "settings.json");
        new LauncherSettings().With("anyxboxx", new ForkState { OutputSize = chosen }).Save(path);

        var setup = LauncherSettings.Load(path).Setup(Fork("anyxboxx"));

        Assert.Equal(chosen, setup.Output?.Id);
    }

    [Fact]
    public void A_settings_file_from_before_the_choice_means_1080p()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{ "Forks": { "anyxboxx": { "LastBuiltCommit": "abc" } } }""");

        Assert.Equal("1920x1080", LauncherSettings.Load(path).Setup(Fork("anyxboxx")).Output?.Id);
    }

    [Fact]
    public void Another_size_does_not_ask_for_a_new_warmup()
    {
        // the translated shaders do not depend on it: neither variable is part of their cache key
        static string Fingerprint(ForkSetup setup) =>
            WarmState.Fingerprint("source", setup.WarmCacheSettings(), @"C:\game", @"C:\cache");

        var first = Fingerprint(AnyXboxX());

        Assert.Equal(first, Fingerprint(AnyXboxX("1280x720")));
        Assert.Equal(first, Fingerprint(AnyXboxX(OutputSize.TitlesOwn)));
        Assert.Equal("2", AnyXboxX().WarmCacheSettings()["VICTORY_RELAY"]);
    }

    [Fact]
    public void A_size_a_fork_always_sets_stays_part_of_the_warmup_hint()
    {
        var setup = new LauncherSettings().Setup(Fork("vitoriabr"));

        Assert.Equal("1920x1080", setup.WarmCacheSettings()["VICTORY_DISPLAY_MODE"]);
        Assert.Equal(setup.ReadSettings().Count, setup.WarmCacheSettings().Count);
    }
}
