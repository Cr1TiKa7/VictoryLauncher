using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>The ways forks fill their caches ahead of a run.</summary>
public enum WarmKind
{
    None,
    /// <summary>`warm` with every archive and binary of the game; reports progress the launcher can read.</summary>
    GameFiles,
    /// <summary>`warm` with the game folder.</summary>
    GameFolder,
    /// <summary>`shaders` for the shader pack, then `warm --used` for the pipelines earlier runs drew with.</summary>
    ShadersThenPipelines,
    /// <summary>`translate --cache --pipelines` for the shader pack.</summary>
    ShaderPack,
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
    public static WarmKind Detect(string help, string warmHelp, string translateHelp)
    {
        var commands = CliHelp.Commands(help);
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

    /// <returns>The argument lists to run one after another.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Steps(WarmKind kind, string gameDir)
    {
        var shaderPack = Path.Combine(gameDir, ShaderPackName);
        return kind switch
        {
            WarmKind.GameFiles => [["warm", .. WarmInputs.Collect(gameDir)]],
            WarmKind.GameFolder => [["warm", gameDir]],
            WarmKind.ShadersThenPipelines => [["shaders", shaderPack], ["warm", "--used"]],
            WarmKind.ShaderPack => [["translate", "--cache", "--pipelines", "--jobs", "8", shaderPack]],
            _ => [],
        };
    }

    /// <returns>A sentence about the kind, or an empty string.</returns>
    public static string About(WarmKind kind) => kind switch
    {
        WarmKind.None => "This fork has no warmup command. The shaders are built while you play.",
        WarmKind.ShadersThenPipelines => "The warmup translates the shaders and then builds the pipelines that earlier " +
                                         "game runs used. Before the first run there are none yet.",
        _ => "",
    };
}
