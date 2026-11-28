using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>The ways forks fill their caches ahead of a run.</summary>
public enum WarmKind
{
    None,
    /// <summary>
    /// `warm` with every archive and binary of the game; reports progress the launcher can read. AnyXboxX until its
    /// combined base (04a08f5).
    /// </summary>
    GameFiles,
    /// <summary>`warm` with the game folder.</summary>
    GameFolder,
    /// <summary>`shaders` for the shader pack, then `warm` for the pipelines that game runs have recorded.</summary>
    ShadersThenPipelines,
    /// <summary>`translate --cache --pipelines` for the shader pack.</summary>
    ShaderPack,
    /// <summary>`precompile`: builds the pipelines earlier game runs used (kelvaroq).</summary>
    Precompile,
}

/// <summary>Reads the help a built fork prints. Forks change often, so their commands are asked for, not assumed.</summary>
public static partial class CliHelp
{
    [GeneratedRegex(@"^  (?<name>[a-z][a-z0-9-]*)(\s|$)")]
    private static partial Regex CommandLine();

    /// <returns>The names listed below "Commands:".</returns>
    public static IReadOnlySet<string> Commands(string help)
    {
        var commands = new HashSet<string>(StringComparer.Ordinal);
        var listing = false;
        foreach (var line in help.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("Commands:", StringComparison.Ordinal))
                listing = true;
            else if (listing && line.Length > 0 && !char.IsWhiteSpace(line[0]))
                break;
            else if (listing && CommandLine().Match(line) is { Success: true } match)
                commands.Add(match.Groups["name"].Value);
        }
        return commands;
    }

    public static bool HasOption(string help, string option) =>
        Regex.IsMatch(help, $@"(?m)^\s+(-\w, )?{Regex.Escape(option)}(\s|$)");
}

public static class WarmPlan
{
    private const string ShaderPackName = "shaders.rpf";

    /// <param name="help">Output of `--help`.</param>
    /// <param name="warmHelp">Output of `warm --help`, empty without that command.</param>
    /// <param name="translateHelp">Output of `translate --help`, empty without that command.</param>
    /// <param name="precompileHelp">Output of `precompile --help`, empty without that command.</param>
    public static WarmKind Detect(string help, string warmHelp, string translateHelp, string precompileHelp = "")
    {
        var commands = CliHelp.Commands(help);
        // --check belongs to the precompile whose output the launcher can read
        if (commands.Contains("precompile") && CliHelp.HasOption(precompileHelp, "--check"))
            return WarmKind.Precompile;
        if (commands.Contains("warm"))
        {
            if (CliHelp.HasOption(warmHelp, "--used"))
                return commands.Contains("shaders") ? WarmKind.ShadersThenPipelines : WarmKind.None;
            return warmHelp.Contains("[PATHS]", StringComparison.Ordinal) ? WarmKind.GameFolder : WarmKind.None;
        }
        return commands.Contains("translate") && CliHelp.HasOption(translateHelp, "--cache")
               && CliHelp.HasOption(translateHelp, "--pipelines")
            ? WarmKind.ShaderPack
            : WarmKind.None;
    }

    /// <returns>The kind for a fork whose warmup takes every game file, otherwise the detected one.</returns>
    public static WarmKind Refine(WarmKind detected, bool everyGameFile) =>
        everyGameFile && detected == WarmKind.GameFolder ? WarmKind.GameFiles : detected;

    /// <param name="everyGameFile">The `shaders` step gets every archive and binary, not only the shader pack.</param>
    /// <param name="rebuild">The pipeline step also builds what it has built before. Other kinds have no such step.</param>
    /// <returns>The argument lists to run one after another.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Steps(WarmKind kind, string gameDir, bool everyGameFile = false,
        bool rebuild = false)
    {
        var shaderPack = Path.Combine(gameDir, ShaderPackName);
        return kind switch
        {
            WarmKind.GameFiles => [["warm", .. WarmInputs.Collect(gameDir)]],
            WarmKind.GameFolder => [["warm", gameDir]],
            WarmKind.ShadersThenPipelines =>
            [
                ["shaders", .. everyGameFile ? WarmInputs.Collect(gameDir) : [shaderPack]],
                // all that was recorded, those drawn with first; without --again it skips what it made before
                rebuild ? ["warm", "--again"] : ["warm"],
            ],
            WarmKind.ShaderPack => [["translate", "--cache", "--pipelines", "--jobs", "8", shaderPack]],
            WarmKind.Precompile => [["precompile"]],
            _ => [],
        };
    }

    /// <returns>A new reader for the output of one step.</returns>
    /// <param name="step">The arguments of that step.</param>
    /// <param name="lastTotal">The shader total of the last time, for the steps that print none while they run.</param>
    public static WarmOutput Reader(WarmKind kind, IReadOnlyList<string> step, int? lastTotal)
    {
        var estimated = lastTotal is not > 0;
        var total = estimated ? WarmProgress.EstimatedTotal : lastTotal!.Value;
        return kind switch
        {
            WarmKind.GameFiles => new WarmProgress(total, estimated),
            WarmKind.GameFolder => new WarmProgress(total, estimated, strict: false),
            WarmKind.ShadersThenPipelines when step[0] == "shaders" => new ShaderCacheOutput(lastTotal),
            WarmKind.ShadersThenPipelines => new KnownPipelinesOutput(),
            WarmKind.ShaderPack => new ShaderPackOutput(),
            WarmKind.Precompile => new PrecompileOutput(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind has no warmup."),
        };
    }

    /// <returns>
    /// How long a cancelled warmup may take to stop by itself after its input was closed, or null where the process
    /// is ended right away.
    /// </returns>
    public static TimeSpan? CancelGrace(WarmKind kind) =>
        kind == WarmKind.Precompile ? TimeSpan.FromSeconds(120) : null;

    /// <summary>Creates what a warmup expects to be there in the cache.</summary>
    public static void PrepareCache(WarmKind kind, string cacheDir)
    {
        // `warm` writes its lists there, but only a game run creates the folder (seen with Vice-Tay).
        if (kind == WarmKind.ShadersThenPipelines)
            Directory.CreateDirectory(Path.Combine(cacheDir, "pipelines"));
    }

    /// <returns>What a cancelled warmup left behind.</returns>
    public static string CancelledText(WarmKind kind, bool rebuild = false) => kind switch
    {
        WarmKind.GameFiles =>
            "Warmup cancelled. The pipeline library was not saved, please repeat the warmup later.",
        // `warm` writes down every pipeline as soon as it is made, and `--again` skips none of those
        WarmKind.ShadersThenPipelines when rebuild =>
            "Warmup cancelled. A rebuild starts over: to finish it, warm up again with Rebuild all pipelines ticked.",
        WarmKind.ShadersThenPipelines => "Warmup cancelled. The next one goes on where this one stopped.",
        _ => "Warmup cancelled, please repeat it later.",
    };

    /// <param name="records">The game of this fork writes down its pipelines.</param>
    /// <returns>A sentence about the kind, or an empty string.</returns>
    public static string About(WarmKind kind, bool records = false) => kind switch
    {
        WarmKind.None => "This fork has no warmup command. The shaders are built while you play.",
        WarmKind.ShadersThenPipelines when records =>
            "Play once first: the game records its pipelines, and the next warmup builds them ahead.",
        WarmKind.ShadersThenPipelines => "The warmup translates the shaders. Its pipeline step needs a record of game " +
                                         "runs, which the launcher does not switch on for this fork.",
        WarmKind.Precompile => "The warmup builds ahead the pipelines that your earlier game runs used. That helps " +
                               "mainly after an update, not in places you have not seen yet. Play once first.",
        _ => "",
    };
}
