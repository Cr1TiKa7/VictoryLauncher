using System.Globalization;
using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// Progress of `victory warm`. It reports a count every 2000 shaders only and no total, so the log line of each
/// translation is counted for the steps in between.
/// </summary>
public sealed partial class WarmProgress(int total)
{
    public const int EstimatedTotal = 25758;

    [GeneratedRegex(@"^\s*\d+ s\s+(?<count>\d+) shaders\s*$")]
    private static partial Regex Report();

    [GeneratedRegex(@"^warm\s+(?<done>\d+) translated, (?<refused>\d+) refused; \d+ compute pipelines, \d+ refused; \d+ s\s*$")]
    private static partial Regex Result();

    private readonly Lock _gate = new();
    private int _logged, _reported;
    private string? _resultLine;
    private int? _resultTotal;

    public int Total { get; } = Math.Max(1, total);

    public int Count
    {
        get { lock (_gate) return _resultTotal ?? Math.Max(_logged, _reported); }
    }

    public double Percent
    {
        get { lock (_gate) return _resultTotal is null ? Math.Min(99, 100.0 * Math.Max(_logged, _reported) / Total) : 100; }
    }

    public string? ResultLine { get { lock (_gate) return _resultLine; } }

    public int? ResultTotal { get { lock (_gate) return _resultTotal; } }

    public void Feed(string line)
    {
        lock (_gate)
        {
            if (line.Contains(": shader translated ", StringComparison.Ordinal))
                _logged++;
            else if (Report().Match(line) is { Success: true } report)
                _reported = Number(report, "count");
            else if (Result().Match(line) is { Success: true } result)
            {
                _resultLine = line;
                _resultTotal = Number(result, "done") + Number(result, "refused");
            }
        }
    }

    private static int Number(Match match, string group) =>
        int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}
