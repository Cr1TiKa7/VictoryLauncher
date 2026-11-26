using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// `shaders` of Vice-Tay and VIctory-7900XTX: a count every 1000 shaders and no total, only the end line has one.
/// </summary>
/// <param name="lastTotal">The total of the last time. Without a sensible one there is only a count.</param>
public sealed partial class ShaderCacheOutput(int? lastTotal) : WarmOutput
{
    [GeneratedRegex(@"^(?<count>\d+) shaders in the cache, \d+ s\s*$")]
    private static partial Regex Report();

    // begins like the report, but counts the shaders that do not translate as well
    [GeneratedRegex(@"^(?<count>\d+) shaders in the cache, (?<refused>\d+) do not translate, \d+ s\s*$")]
    private static partial Regex Result();

    private readonly int? _lastTotal = lastTotal > 0 ? lastTotal : null;
    private int? _count, _endTotal;
    private string? _summary;

    protected override void Read(string line)
    {
        if (Report().Match(line) is { Success: true } report && TryNumber(report, "count", out var count))
        {
            _count = count;
        }
        else if (Result().Match(line) is { Success: true } result
                 && TryNumber(result, "count", out var cached) && TryNumber(result, "refused", out var refused))
        {
            _endTotal = cached + refused;
            _summary = $"{cached} shaders in the cache, {refused} do not translate";
        }
    }

    protected override WarmStatus? Current()
    {
        if (_endTotal is { } all)
            return new(all, all, 100, "shaders");
        if (_count is not { } count)
            return null;
        double? percent = _lastTotal is { } total ? Share(count, total, ended: false) : null;
        return new(count, _lastTotal, percent, "shaders");
    }

    protected override WarmVerdict Judge() => new(true, _summary, _endTotal);
}

/// <summary>
/// `warm` of Vice-Tay and VIctory-7900XTX: it builds the pipelines an earlier game run recorded. The first line says
/// how many are to make, then a count follows every 1000.
/// </summary>
public sealed partial class KnownPipelinesOutput : WarmOutput
{
    [GeneratedRegex(@"^(?<known>\d+) pipelines known, (?<before>\d+) made before \([^)]*\), \d+ bad; " +
                    @"(?<make>\d+) to make, \d+ processes\s*$")]
    private static partial Regex Header();

    [GeneratedRegex(@"^(?<count>\d+) of (?<total>\d+), \d+ s\s*$")]
    private static partial Regex Report();

    // the times of the single pipelines follow after "ms each:"
    [GeneratedRegex(@"^(?<made>\d+) made in \d+ s \((?<refused>\d+) have no host pipeline, " +
                    @"(?<crashed>\d+) ended their process\); ms each:")]
    private static partial Regex Result();

    private int _count;
    private int? _toMake;
    private bool _ended;
    private string? _nothingToMake, _built;

    protected override void Read(string line)
    {
        if (Header().Match(line) is { Success: true } header && TryNumber(header, "known", out var known)
            && TryNumber(header, "before", out var before) && TryNumber(header, "make", out var make))
        {
            _toMake = make;
            _nothingToMake = make > 0 ? null : WhyNothing(known, before);
        }
        else if (Report().Match(line) is { Success: true } report
                 && TryNumber(report, "count", out var count) && TryNumber(report, "total", out var total))
        {
            _count = count;
            _toMake = total;
        }
        else if (Result().Match(line) is { Success: true } result && TryNumber(result, "made", out var made)
                 && TryNumber(result, "refused", out var refused) && TryNumber(result, "crashed", out var crashed))
        {
            // "made" includes the ones without a host pipeline, not the ones that ended their process
            _ended = true;
            _built = $"{made - refused} pipelines built, {refused} have no host pipeline, " +
                     $"{crashed} ended their process";
        }
    }

    protected override WarmStatus? Current()
    {
        if (_toMake is not { } total || total <= 0)
            return null;
        var count = _ended ? total : _count;
        return new(count, total, Share(count, total, _ended), "pipelines");
    }

    protected override WarmVerdict Judge() => new(true, _nothingToMake ?? _built);

    private static string WhyNothing(int known, int before) =>
        known == 0 ? "No pipelines recorded yet, so there was nothing to build."
        : before > 0 ? $"All {before} recorded pipelines were built before, nothing new to build."
        : $"None of the {known} recorded pipelines had to be built.";
}
