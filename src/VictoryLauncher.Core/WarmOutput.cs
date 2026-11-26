using System.Globalization;
using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>How far one step of a warmup is.</summary>
/// <param name="Total">Null while only a count is known.</param>
/// <param name="Percent">Null without a total.</param>
/// <param name="Estimated">The total is a guess and is called one.</param>
public sealed record WarmStatus(int Count, int? Total, double? Percent, string Unit, bool Estimated = false)
{
    public string Describe()
    {
        if (Total is not { } total)
            return $"{Number(Count)} {Unit}";
        var of = Estimated ? $"about {Number(total)} (estimated)" : Number(total);
        return string.Create(CultureInfo.InvariantCulture, $"{Number(Count)} of {of} {Unit} · {Percent:0}%");
    }

    /// <summary>With English separators like the rest of the window, whatever Windows is set to.</summary>
    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}

/// <summary>What the output of a step says after it ended with exit code 0.</summary>
/// <param name="Complete">False: the step did not do its work, whatever the exit code says.</param>
/// <param name="Summary">One line about the result, or null where the output gives none.</param>
/// <param name="RememberTotal">The total of a step that does not print one while it runs, for the next time.</param>
public sealed record WarmVerdict(bool Complete, string? Summary = null, int? RememberTotal = null);

/// <summary>
/// Reads the lines of one warmup step. Lines it does not understand are left out. The lines arrive on thread pool
/// threads, where an exception would end the launcher: reading one must not throw.
/// </summary>
public abstract class WarmOutput
{
    private readonly Lock _gate = new();

    public void Feed(string line)
    {
        lock (_gate)
            Read(line);
    }

    /// <summary>Null while there is nothing to show but the output itself.</summary>
    public WarmStatus? Status
    {
        get { lock (_gate) return Current(); }
    }

    /// <summary>Only asked after exit code 0.</summary>
    public WarmVerdict Verdict()
    {
        lock (_gate)
            return Judge();
    }

    /// <summary>What to add to the message about a failed run, or null.</summary>
    public virtual string? FailureHint => null;

    /// <summary>The share done; only an end line may make it 100.</summary>
    protected static double Share(int count, int total, bool ended) =>
        ended ? 100 : Math.Min(99, 100.0 * count / total);

    protected static bool TryNumber(Match match, string group, out int number) =>
        int.TryParse(match.Groups[group].ValueSpan, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    protected abstract void Read(string line);

    protected abstract WarmStatus? Current();

    protected abstract WarmVerdict Judge();
}
