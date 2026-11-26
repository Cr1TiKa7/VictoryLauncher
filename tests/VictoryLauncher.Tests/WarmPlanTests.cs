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

    // `victory.exe --help` and `warm --help` of AnyXboxX at 37d4686, as printed (descriptions shortened).
    private const string CombinedBaseHelp =
        "Run an Xbox Series title natively on Windows\n\nUsage: victory.exe <COMMAND>\n\nCommands:\n" +
        "  inspect      Summarise one PE image\n  census       Aggregate the imports\n" +
        "  lib-exports  List the `dll!export` pairs\n  shader-info  Find shader containers\n" +
        "  translate    Run every shader found\n  shaders      Translate every shader found\n" +
        "  warm         Make the host pipelines of every pipeline the title ever created\n" +
        "  run          Prepare, map and bind a title executable\n  config       Show the resolved configuration\n" +
        "  help         Print this message or the help of the given subcommand(s)\n\n" +
        "Options:\n  -h, --help     Print help\n  -V, --version  Print version";

    private const string CombinedBaseWarmHelp =
        "Usage: victory.exe warm [OPTIONS]\n\nOptions:\n" +
        "      --jobs <JOBS>    Processes at once (default: two thirds of the logical cores)\n" +
        "      --limit <LIMIT>  At most this many pipelines\n      --again          Those made before too\n" +
        "      --used           Only the pipelines earlier runs drew with\n  -h, --help           Print help";

    [Fact]
    public void AnyXboxX_on_its_combined_base_translates_shaders_first() =>
        Assert.Equal(WarmKind.ShadersThenPipelines,
            WarmPlan.Detect(CombinedBaseHelp, CombinedBaseWarmHelp, TranslateWithCache));

    // A checkout from before that base, which the window offers to carry on with, keeps its old warmup.
    [Theory]
    [InlineData(WarmKind.GameFolder, true, WarmKind.GameFiles)]
    [InlineData(WarmKind.GameFolder, false, WarmKind.GameFolder)]
    [InlineData(WarmKind.ShadersThenPipelines, true, WarmKind.ShadersThenPipelines)]
    [InlineData(WarmKind.None, true, WarmKind.None)]
    public void A_fork_that_warms_every_game_file_keeps_its_old_way(WarmKind detected, bool everyFile, WarmKind kind) =>
        Assert.Equal(kind, WarmPlan.Refine(detected, everyFile));

    [Fact]
    public void The_shader_step_can_be_given_every_game_file()
    {
        var game = Directory.CreateTempSubdirectory("anyx-warm-").FullName;
        try
        {
            foreach (var name in (string[])["shaders.rpf", "anim.rpf", "game_scarlett_master.exe",
                         "XboxBvhBuilder.dll", "amd_fidelityfx_upscaler_xbox.dll", "other.dll"])
                File.WriteAllText(Path.Combine(game, name), "x");
            string In(string name) => Path.Combine(game, name);

            var steps = WarmPlan.Steps(WarmKind.ShadersThenPipelines, game, everyGameFile: true);

            Assert.Equal(
                [["shaders", In("anim.rpf"), In("shaders.rpf"), In("game_scarlett_master.exe"),
                    In("XboxBvhBuilder.dll"), In("amd_fidelityfx_upscaler_xbox.dll")], ["warm", "--used"]], steps);
        }
        finally
        {
            Directory.Delete(game, true);
        }
    }
}
