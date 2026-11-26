using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// `translate` of klbf-lab: after the shaders it builds the pipelines of the shader pack, and reports how far it is
/// about once a minute.
/// </summary>
public sealed partial class ShaderPackOutput : WarmOutput
{
    [GeneratedRegex(@"^pipelines\s+\d+ in the pack, \d+ already pre-built, (?<make>\d+) to make on \d+ threads\s*$")]
    private static partial Regex Header();

    // the path of the pack stands in front and can hold anything, so only the end is fixed
    [GeneratedRegex(@": (?<count>\d+)/(?<total>\d+) pipelines, \d+ made, \d+ found in the library, \d+ s\s*$")]
    private static partial Regex Report();

    [GeneratedRegex(@"^pipelines\s+\d+ in the pack, [\d.]+ s: (?<made>\d+) made, " +
                    @"(?<found>\d+) found in the library, (?<already>\d+) already pre-built\s*$")]
    private static partial Regex Result();

    private int _count;
    private int? _toMake;
    private string? _summary;

    public override string? FailureHint =>
        "Start the warmup again: it then skips the pipeline the driver died on.";

    protected override void Read(string line)
    {
        if (Header().Match(line) is { Success: true } header && TryNumber(header, "make", out var make))
        {
            _toMake = make;
        }
        else if (Report().Match(line) is { Success: true } report
                 && TryNumber(report, "count", out var count) && TryNumber(report, "total", out var total))
        {
            _count = count;
            _toMake = total;
        }
        else if (Result().Match(line) is { Success: true } result && TryNumber(result, "made", out var made)
                 && TryNumber(result, "found", out var found) && TryNumber(result, "already", out var already))
        {
            _summary = $"{made} pipelines built, {found} found in the library, {already} already pre-built";
        }
    }

    protected override WarmStatus? Current()
    {
        if (_toMake is not { } total || total <= 0)
            return null;
        var ended = _summary is not null;
        // pipelines made first after a crash are counted but are not part of the total
        var count = ended ? total : Math.Min(_count, total);
        return new(count, total, Share(count, total, ended), "pipelines");
    }

    protected override WarmVerdict Judge() => new(true, _summary);
}
