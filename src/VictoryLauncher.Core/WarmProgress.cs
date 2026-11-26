using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// Progress of `warm` as AnyXboxX and DexNom print it. They report a count every 2000 shaders only and no total, so
/// the log line of each translation is counted for the steps in between.
/// </summary>
/// <param name="total">The total of the last time, or an estimate.</param>
/// <param name="estimated">The total is an estimate.</param>
/// <param name="strict">
/// AnyXboxX: the progress shows from the start, and without the result line the warmup is not done. Otherwise the
/// fork may print nothing of this (VItoriaBr), and then the exit code alone decides.
/// </param>
public sealed partial class WarmProgress(int total, bool estimated = false, bool strict = true) : WarmOutput
{
    public const int EstimatedTotal = 25758;

    [GeneratedRegex(@"^\s*\d+ s\s+(?<count>\d+) shaders\s*$")]
    private static partial Regex Report();

    // DexNom adds how many shaders its ray tracing mode leaves out.
    [GeneratedRegex(@"^warm\s+(?<done>\d+) translated, (?<refused>\d+) refused; \d+ compute pipelines, \d+ refused; " +
                    @"(\d+ omitted by ray-tracing mode; )?\d+ s\s*$")]
    private static partial Regex Result();

    private int _logged, _reported;
    private string? _resultLine;
    private int? _resultTotal;
    private bool _understood;

    public int Total { get; } = Math.Max(1, total);

    public int Count => Status?.Count ?? 0;

    public double Percent => Status?.Percent ?? 0;

    public string? ResultLine => Verdict().Summary;

    public int? ResultTotal => Verdict().RememberTotal;

    protected override void Read(string line)
    {
        if (line.Contains(": shader translated ", StringComparison.Ordinal))
        {
            _logged++;
        }
        else if (Report().Match(line) is { Success: true } report && TryNumber(report, "count", out var count))
        {
            _reported = count;
        }
        else if (Result().Match(line) is { Success: true } result
                 && TryNumber(result, "done", out var done) && TryNumber(result, "refused", out var refused))
        {
            _resultLine = line;
            _resultTotal = done + refused;
        }
        else
        {
            return;
        }
        _understood = true;
    }

    protected override WarmStatus? Current()
    {
        if (!strict && !_understood)
            return null;
        var count = _resultTotal ?? Math.Max(_logged, _reported);
        return new(count, Total, Share(count, Total, _resultTotal is not null), "shaders", estimated);
    }

    protected override WarmVerdict Judge() => new(!strict || _resultLine is not null, _resultLine, _resultTotal);
}
