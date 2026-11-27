using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

/// <summary>
/// `warm` builds only pipelines a game run has written down, and the game writes them down only with
/// VICTORY_PIPELINE_BOOK=1 (crates\victory-gpu\src\pipeline\known.rs of AnyXboxX 5a08a33 and Vice-Tay 8edc378).
/// </summary>
public class PipelineRecordingTests
{
    private const string Switch = ForkProfile.PipelineBookSwitch;

    private static readonly string[] Recording = ["anyxboxx", "vice-tay", "victory-7900xtx"];

    [Theory]
    [InlineData("anyxboxx")]
    [InlineData("vice-tay")]
    [InlineData("victory-7900xtx")]
    public void The_forks_with_a_pipeline_warmup_record_what_the_game_creates(string id)
    {
        var fork = ForkCatalog.Find(id, []);

        Assert.Equal("1", fork.Settings[Switch]);
        Assert.True(fork.RecordsPipelines);
    }

    [Fact]
    public void No_other_fork_records()
    {
        var others = ForkCatalog.BuiltIn.Where(fork => !Recording.Contains(fork.Id))
            .Append(ForkCatalog.Custom("https://github.com/a/b"));

        Assert.All(others, fork =>
        {
            Assert.False(fork.Settings.ContainsKey(Switch), fork.Id);
            Assert.False(fork.RecordsPipelines, fork.Id);
        });
    }

    [Fact]
    public void AnyXboxX_records_beside_the_settings_from_its_own_file()
    {
        var repo = Directory.CreateTempSubdirectory("anyx-book-").FullName;
        try
        {
            var fork = ForkCatalog.Find("anyxboxx", []);
            var file = Path.Combine(repo, fork.TuningFile!);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "settings.rs"), file);

            var settings = new ForkSetup(fork, repo, @"G:\cache", null, @"G:\game").ReadSettings();

            Assert.Equal("1", settings[Switch]);
            Assert.True(settings.Count > 1, "the settings of the file are gone");
        }
        finally
        {
            Directory.Delete(repo, true);
        }
    }

    [Fact]
    public void The_pipeline_step_builds_everything_recorded_and_not_only_what_was_drawn_with() =>
        Assert.Equal(["warm"], WarmPlan.Steps(WarmKind.ShadersThenPipelines, @"G:\game")[^1]);

    [Fact]
    public void A_rebuild_also_builds_what_was_made_before()
    {
        var steps = WarmPlan.Steps(WarmKind.ShadersThenPipelines, @"G:\game", rebuild: true);

        Assert.Equal(["shaders", Path.Combine(@"G:\game", "shaders.rpf")], steps[0]);
        Assert.Equal(["warm", "--again"], steps[1]);
    }

    [Theory]
    [InlineData(WarmKind.None)]
    [InlineData(WarmKind.GameFolder)]
    [InlineData(WarmKind.ShaderPack)]
    [InlineData(WarmKind.Precompile)]
    public void The_other_kinds_know_no_rebuild(WarmKind kind) =>
        Assert.Equal(WarmPlan.Steps(kind, @"G:\game"), WarmPlan.Steps(kind, @"G:\game", rebuild: true));

    [Fact]
    public void A_recording_fork_says_to_play_first()
    {
        var text = WarmPlan.About(WarmKind.ShadersThenPipelines, records: true);

        Assert.Contains("Play once first", text);
        // it stands above the start button together with the warmup hint, the smallest window has little room
        Assert.True(text.Length <= 110, $"{text.Length} characters");
    }

    [Fact]
    public void A_fork_that_does_not_record_says_so() =>
        Assert.Contains("does not switch on", WarmPlan.About(WarmKind.ShadersThenPipelines, records: false));

    [Theory]
    [InlineData(WarmKind.ShadersThenPipelines, "goes on where this one stopped")]
    [InlineData(WarmKind.GameFiles, "pipeline library was not saved")]
    [InlineData(WarmKind.GameFolder, "please repeat it later")]
    [InlineData(WarmKind.Precompile, "please repeat it later")]
    public void A_cancelled_warmup_says_what_is_left(WarmKind kind, string expected) =>
        Assert.Contains(expected, WarmPlan.CancelledText(kind));

    // `--again` skips nothing, so what a cancelled rebuild left out is only built by another rebuild.
    [Fact]
    public void A_cancelled_rebuild_does_not_promise_to_go_on()
    {
        var text = WarmPlan.CancelledText(WarmKind.ShadersThenPipelines, rebuild: true);

        Assert.DoesNotContain("goes on", text);
        Assert.Contains("Rebuild all pipelines", text);
    }
}
