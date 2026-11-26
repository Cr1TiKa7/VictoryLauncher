using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// Reads the VICTORY_* settings from AnyXboxX's crates\anyxboxx\src\run\settings.rs, so that updates carry over.
/// Where there is an ALWAYS table (AnyXboxX until 04a08f5) it is applied first, then TUNED. The other tables there
/// are opt in and stay out.
/// </summary>
public static partial class Tuning
{
    [GeneratedRegex(@"//[^\r\n]*")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\(\s*""(?<name>VICTORY_\w+)""\s*,\s*""(?<value>[^""\r\n]*)""\s*,?\s*\)")]
    private static partial Regex Entry();

    public static IReadOnlyDictionary<string, string> Parse(string source)
    {
        var code = Comment().Replace(source, "");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Looked for loosely: an ALWAYS table in another form must not be taken for none.
        string[] tables = Regex.IsMatch(code, @"\bconst\s+ALWAYS\b") ? ["ALWAYS", "TUNED"] : ["TUNED"];
        foreach (var table in tables)
            foreach (Match entry in Entries(code, table))
                values[entry.Groups["name"].Value] = entry.Groups["value"].Value;
        return values;
    }

    private static MatchCollection Entries(string code, string table)
    {
        var found = Regex.Match(code,
            $@"pub\s+const\s+{table}\s*:\s*&\[\(&str,\s*&str\)\]\s*=\s*&\[(?<body>.*?)\]\s*;", RegexOptions.Singleline);
        var body = found.Groups["body"].Value;
        var entries = Entry().Matches(body);
        // Whatever is left besides commas is an entry in a form this parser does not know.
        if (!found.Success || entries.Count == 0 || Entry().Replace(body, "").Any(c => c != ',' && !char.IsWhiteSpace(c)))
            throw new InvalidDataException(
                $"The settings table {table} in settings.rs of AnyXboxX was not read completely. " +
                "The file has probably changed.");
        return entries;
    }
}
