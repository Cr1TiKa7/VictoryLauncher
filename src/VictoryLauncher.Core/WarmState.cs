using System.Security.Cryptography;
using System.Text;

namespace VictoryLauncher.Core;

/// <summary>Tells whether the last warmup still fits what the next run will use.</summary>
public static class WarmState
{
    /// <summary>
    /// Covers what the shader and pipeline caches can depend on: the runtime's sources and the VICTORY_* variables
    /// (with the game and cache folder). Commits elsewhere in the repository leave it as it is.
    /// </summary>
    public static string Fingerprint(string sourceId, IReadOnlyDictionary<string, string> settings,
        string gameDir, string cacheDir, string? dataDir = null)
    {
        var variables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        // Windows does not care how a folder is written.
        LaunchEnvironment.Apply(variables, settings, Folder(gameDir), Folder(cacheDir),
            dataDir is null ? null : Folder(dataDir));

        var text = new StringBuilder(sourceId);
        foreach (var (name, value) in variables.OrderBy(v => v.Key, StringComparer.Ordinal))
            text.Append('\0').Append(name).Append('=').Append(value);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static string Folder(string path) => Path.TrimEndingDirectorySeparator(path).ToUpperInvariant();

    /// <param name="current">The fingerprint of the next run, or null while it is unknown.</param>
    /// <returns>The hint to show, or an empty string.</returns>
    public static string Hint(ForkState state, string? current)
    {
        if (current is null || state.LastWarmFingerprint == current)
            return "";
        return state.LastWarmCommit is null
            ? "No warmup with this launcher yet. A warmup is recommended before the first start."
            : "The runtime, the settings or the folders have changed since the last warmup. " +
              "A new warmup is recommended.";
    }
}
