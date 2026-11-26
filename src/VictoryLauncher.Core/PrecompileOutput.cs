using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>
/// `precompile` of kelvaroq: lines of "precompile done total failed", the last one counts. It also ends with exit
/// code 0 when it was stopped early, so the lines say whether it is done.
/// </summary>
public sealed partial class PrecompileOutput : WarmOutput
{
    [GeneratedRegex(@"^precompile (?<done>\d+) (?<total>\d+) (?<failed>\d+)\s*$")]
    private static partial Regex Report();

    private (int Done, int Total, int Failed)? _last;

    protected override void Read(string line)
    {
        if (Report().Match(line) is { Success: true } report && TryNumber(report, "done", out var done)
            && TryNumber(report, "total", out var total) && TryNumber(report, "failed", out var failed))
        {
            _last = (done, total, failed);
        }
    }

    protected override WarmStatus? Current()
    {
        if (_last is not { Total: > 0 } last)
            return null;
        var count = Math.Min(last.Done, last.Total);
        return new(count, last.Total, Share(count, last.Total, count == last.Total), "pipelines");
    }

    protected override WarmVerdict Judge()
    {
        if (_last is not { } last)
            return new(false, "Warmup incomplete: the program reported no progress. Start it again.");
        if (last.Total == 0)
            return new(true, "Nothing to build: kelvaroq only knows pipelines that a game run has used.");
        if (last.Done < last.Total)
            return new(false, $"Warmup incomplete: {last.Done} of {last.Total} pipelines. Start it again.");
        return new(true, last.Failed == 0
            ? $"{last.Total} pipelines built"
            : $"{last.Total - last.Failed} pipelines built, {last.Failed} failed and will be tried again next time");
    }
}
