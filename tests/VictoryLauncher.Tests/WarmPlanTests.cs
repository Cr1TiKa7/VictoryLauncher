using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class WarmPlanTests
{
    // `victory.exe --help` as AnyXboxX's build prints it; other forks differ in the command list.
    private static string Help(params string[] commands) =>
        "Run an Xbox Series title natively on Windows\n\nUsage: victory.exe <COMMAND>\n\nCommands:\n" +
        string.Concat(commands.Select(c => $"  {c,-12} Translate the shaders and warm things\n")) +
        "  help         Print this message or the help of the given subcommand(s)\n\n" +
        "Options:\n  -h, --help     Print help\n  -V, --version  Print version";

    private const string WarmFiles =
        "Usage: victory.exe warm [OPTIONS] [PATHS]...\n\nArguments:\n  [PATHS]...  \n\nOptions:\n" +
        "      --jobs <JOBS>   Worker threads (default: every logical core)";

    private const string WarmPipelines =
        "Usage: victory.exe warm [OPTIONS]\n\nOptions:\n      --jobs <JOBS>    Processes at once\n" +
        "      --again          Those made before too\n      --used           Only the pipelines earlier runs drew with";

    private const string TranslateWithCache =
        "Usage: victory.exe translate [OPTIONS] [PATHS]...\n\nOptions:\n      --pipelines    Also create a pipeline\n" +
        "      --cache        With --pipelines: fill the shader cache\n      --jobs <JOBS>  Worker threads";

    private const string TranslateWithHashFile =
        "Usage: victory.exe translate [OPTIONS] [PATHS]...\n\nOptions:\n      --pipelines    Also create a pipeline\n" +
        "      --warm <WARM>  Warm the run's shader cache: a file of shader hashes\n      --cached-only  Something else";

    [Fact]
    public void Reads_the_command_names_and_nothing_else() =>
        Assert.Equal(["inspect", "warm", "run", "help"], CliHelp.Commands(Help("inspect", "warm", "run")).Order(
            Comparer<string>.Create((a, b) => Array.IndexOf(["inspect", "warm", "run", "help"], a)
                .CompareTo(Array.IndexOf(["inspect", "warm", "run", "help"], b)))));

    [Fact]
    public void A_warm_that_takes_paths_gets_the_game_folder() =>
        Assert.Equal(WarmKind.GameFolder, WarmPlan.Detect(Help("translate", "warm", "run"), WarmFiles, TranslateWithHashFile));

    [Fact]
    public void A_warm_for_used_pipelines_comes_after_the_shaders() =>
        Assert.Equal(WarmKind.ShadersThenPipelines,
            WarmPlan.Detect(Help("translate", "shaders", "warm", "run"), WarmPipelines, TranslateWithCache));

    [Fact]
    public void Without_warm_a_translate_that_fills_the_cache_does() =>
        Assert.Equal(WarmKind.ShaderPack, WarmPlan.Detect(Help("translate", "run"), "", TranslateWithCache));

    [Fact]
    public void Otherwise_there_is_no_warmup()
    {
        Assert.Equal(WarmKind.None, WarmPlan.Detect(Help("translate", "run"), "", TranslateWithHashFile));
        // an unknown kind of warm is not guessed at
        Assert.Equal(WarmKind.None, WarmPlan.Detect(Help("warm", "run"), WarmPipelines, TranslateWithHashFile));
    }

    [Fact]
    public void A_command_that_is_only_mentioned_in_a_description_does_not_count() =>
        Assert.DoesNotContain("shaders", CliHelp.Commands(Help("warm")));

    [Fact]
    public void Builds_the_commands_of_each_kind()
    {
        var shaders = Path.Combine(@"G:\game", "shaders.rpf");

        Assert.Equal([["warm", @"G:\game"]], WarmPlan.Steps(WarmKind.GameFolder, @"G:\game"));
        Assert.Equal([["shaders", shaders], ["warm", "--used"]], WarmPlan.Steps(WarmKind.ShadersThenPipelines, @"G:\game"));
        Assert.Equal([["translate", "--cache", "--pipelines", "--jobs", "8", shaders]],
            WarmPlan.Steps(WarmKind.ShaderPack, @"G:\game"));
        Assert.Empty(WarmPlan.Steps(WarmKind.None, @"G:\game"));
    }

    // `warm --used` writes into <cache>\pipelines and fails when only a game run has not created it yet.
    [Fact]
    public void The_pipeline_warmup_gets_the_folder_it_writes_into()
    {
        var root = Directory.CreateTempSubdirectory("anyx-warm-").FullName;
        try
        {
            var cache = Path.Combine(root, "cache");
            var pipelines = Path.Combine(cache, "pipelines");

            WarmPlan.PrepareCache(WarmKind.ShadersThenPipelines, cache);

            Assert.True(Directory.Exists(pipelines));

            // again, over what an earlier run left there
            File.WriteAllText(Path.Combine(pipelines, "warm-done.txt"), "made");
            WarmPlan.PrepareCache(WarmKind.ShadersThenPipelines, cache);

            Assert.Equal("made", File.ReadAllText(Path.Combine(pipelines, "warm-done.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(WarmKind.None)]
    [InlineData(WarmKind.GameFiles)]
    [InlineData(WarmKind.GameFolder)]
    [InlineData(WarmKind.ShaderPack)]
    [InlineData(WarmKind.Precompile)]
    public void The_other_kinds_get_no_folders(WarmKind kind)
    {
        var cache = Directory.CreateTempSubdirectory("anyx-warm-").FullName;
        try
        {
            WarmPlan.PrepareCache(kind, cache);

            Assert.Empty(Directory.GetFileSystemEntries(cache));
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }

    [Fact]
    public void The_game_files_kind_passes_every_input()
    {
        var game = Directory.CreateTempSubdirectory("anyx-warm-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(game, "a.rpf"), "x");

            var step = Assert.Single(WarmPlan.Steps(WarmKind.GameFiles, game));

            Assert.Equal(["warm", Path.Combine(game, "a.rpf")], step);
        }
        finally
        {
            Directory.Delete(game, true);
        }
    }

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
}
