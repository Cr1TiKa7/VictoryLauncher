using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class ConsoleProfileTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("anyx-console-").FullName;

    public void Dispose() => Directory.Delete(_root, true);

    private static ForkProfile Fork(string id) => ForkCatalog.Find(id, []);

    private static LauncherSettings OnSeriesS(string id) =>
        new LauncherSettings().With(id, new ForkState { SeriesS = true });

    [Fact]
    public void Series_x_starts_every_fork_the_way_it_always_did() =>
        Assert.All(ForkCatalog.BuiltIn, fork =>
        {
            var setup = new LauncherSettings().Setup(fork);

            Assert.False(setup.SeriesS);
            Assert.Equal(["run", setup.GameExe(), "--execute"], setup.PlayArguments());
        });

    [Fact]
    public void Series_s_adds_the_console_switch_for_the_chosen_fork_only()
    {
        var settings = OnSeriesS("vice-tay");

        var chosen = settings.Setup(Fork("vice-tay"));
        var other = settings.Setup(Fork("kelvaroq"));

        Assert.Equal(["run", chosen.GameExe(), "--execute", "--console", "series-s"], chosen.PlayArguments());
        Assert.Equal(["run", other.GameExe(), "--execute"], other.PlayArguments());
    }

    [Fact]
    public void The_choice_is_kept_in_the_settings_file()
    {
        var path = Path.Combine(_root, "settings.json");
        OnSeriesS("vice-tay").Save(path);

        var loaded = LauncherSettings.Load(path);

        Assert.True(loaded.State("vice-tay").SeriesS);
        Assert.True(loaded.Setup(Fork("vice-tay")).SeriesS);
    }

    [Fact]
    public void A_settings_file_from_before_the_choice_means_series_x()
    {
        var path = Path.Combine(_root, "settings.json");
        File.WriteAllText(path, """{ "Forks": { "vice-tay": { "LastBuiltCommit": "abc" } } }""");

        Assert.False(LauncherSettings.Load(path).Setup(Fork("vice-tay")).SeriesS);
    }

    [Theory]
    [InlineData("anyxboxx", "5 ms")]
    [InlineData("klbf-lab", "VICTORY_FFX_DIR")]
    [InlineData("vice-tay", "all sessions")]
    [InlineData("victory-7900xtx", "slower than Series X with 1080p output")]
    [InlineData("kelvaroq", "says nothing about how well")]
    [InlineData("vitoriabr", "says nothing about Series S")]
    [InlineData("victory-fork", "black picture")]
    [InlineData("dexnom", "all use Series X")]
    public void Every_built_in_fork_says_what_is_known_about_its_series_s(string id, string part) =>
        Assert.Contains(part, Fork(id).SeriesSNote);

    [Fact]
    public void An_own_url_says_that_series_s_is_not_checked() =>
        Assert.Contains("Not checked", ForkCatalog.Custom("https://github.com/a/b").SeriesSNote);
}
