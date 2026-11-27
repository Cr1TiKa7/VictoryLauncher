using System.Globalization;
using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// Reads the VICTORY_* settings from AnyXboxX's crates\anyxboxx\src\run\settings.rs, so that updates carry over.
/// Where there is an ALWAYS table (AnyXboxX until 04a08f5) it is applied first, then TUNED. Where there are worker
/// pools for larger CPUs (since f99cbee), the ones for this PC come last. The other tables there are opt in and stay
/// out.
/// </summary>
public static partial class Tuning
{
    /// <summary>Overrides the number of logical CPUs, for "anyxboxx run" and for the launcher.</summary>
    private const string CpusVariable = "ANYXBOXX_LOGICAL_CPUS";

    /// <summary>
    /// The pools settings.rs may have, smallest first, each a table NAME_POOLS used from NAME_CPU_THRESHOLD logical
    /// CPUs. The rule itself is code there (pool_sizes), so it is repeated here: the last one the CPU reaches counts.
    /// </summary>
    private static readonly string[] PoolTiers = ["BIG", "HUGE"];

    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\(\s*""(?<name>VICTORY_\w+)""\s*,\s*""(?<value>[^""\r\n]*)""\s*,?\s*\)")]
    private static partial Regex Entry();

    [GeneratedRegex(@"\bconst\s+(?<tier>\w+?)_(?:POOLS|CPU_THRESHOLD)\b")]
    private static partial Regex PoolConstant();

    [GeneratedRegex(@"\bfn\s+(?:pool_sizes|logical_cpus)\b")]
    private static partial Regex CpuFunction();

    /// <summary>The same count as logical_cpus() in settings.rs.</summary>
    public static int LogicalCpus(Func<string, string?> environment, int detected) =>
        ulong.TryParse(environment(CpusVariable)?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture,
            out var chosen) && chosen > 0
            ? (int)Math.Min(chosen, int.MaxValue)
            : Math.Max(detected, 1);

    /// <param name="logicalCpus">Null: those of this PC, or what the variable says.</param>
    public static IReadOnlyDictionary<string, string> Parse(string source, int? logicalCpus = null)
    {
        var code = Comment().Replace(source, "");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Looked for loosely: an ALWAYS table in another form must not be taken for none.
        string[] tables = Regex.IsMatch(code, @"\bconst\s+ALWAYS\b") ? ["ALWAYS", "TUNED"] : ["TUNED"];
        foreach (var table in tables)
            Add(values, Entries(code, table, @"pub\s+const"));
        var cpus = logicalCpus ?? LogicalCpus(Environment.GetEnvironmentVariable, Environment.ProcessorCount);
        if (PoolSizes(code, cpus) is { } pools)
            Add(values, pools);
        return values;
    }

    private static void Add(Dictionary<string, string> values, MatchCollection entries)
    {
        foreach (Match entry in entries)
            values[entry.Groups["name"].Value] = entry.Groups["value"].Value;
    }

    /// <summary>
    /// Every pool is read, also those this PC is too small for: a change must not go unnoticed there.
    /// </summary>
    /// <returns>The pools for this many CPUs, or null where TUNED stays as it is.</returns>
    private static MatchCollection? PoolSizes(string code, int logicalCpus)
    {
        var found = PoolConstant().Matches(code).Select(match => match.Groups["tier"].Value).ToHashSet();
        if (found.Except(PoolTiers).FirstOrDefault() is { } unknown)
            throw Incomplete($"The worker pools {unknown} in settings.rs of AnyXboxX are not known to this launcher.");
        // Loosely again: pools that got other names must not be taken for none.
        if (found.Count == 0 && !CpuFunction().IsMatch(code))
            return null;

        MatchCollection? chosen = null;
        foreach (var tier in PoolTiers)
        {
            // Only the smallest has to be there: the first version with pools had no other.
            if (tier != PoolTiers[0] && !found.Contains(tier))
                continue;
            var threshold = Threshold(code, $"{tier}_CPU_THRESHOLD");
            // the pool tables are private there
            var entries = Entries(code, $"{tier}_POOLS", @"\bconst");
            if (logicalCpus >= threshold)
                chosen = entries;
        }
        return chosen;
    }

    private static int Threshold(string code, string name)
    {
        var value = Regex.Match(code, $@"\bconst\s+{name}\s*:\s*usize\s*=\s*(?<value>\d[\d_]*)\s*;").Groups["value"].Value;
        return int.TryParse(value.Replace("_", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw Incomplete($"The threshold {name} in settings.rs of AnyXboxX was not read.");
    }

    private static MatchCollection Entries(string code, string table, string declaration)
    {
        var found = Regex.Match(code,
            $@"{declaration}\s+{table}\s*:\s*&\[\(&str,\s*&str\)\]\s*=\s*&\[(?<body>.*?)\]\s*;", RegexOptions.Singleline);
        var body = found.Groups["body"].Value;
        var entries = Entry().Matches(body);
        // Whatever is left besides commas is an entry in a form this parser does not know.
        if (!found.Success || entries.Count == 0 || Entry().Replace(body, "").Any(c => c != ',' && !char.IsWhiteSpace(c)))
            throw Incomplete($"The settings table {table} in settings.rs of AnyXboxX was not read completely.");
        return entries;
    }

    private static InvalidDataException Incomplete(string what) => new($"{what} The file has probably changed.");
}
