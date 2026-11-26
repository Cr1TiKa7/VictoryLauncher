namespace VictoryLauncher.Core;

/// <summary>What the window shows while a warmup runs.</summary>
/// <param name="Percent">Null: the bar runs without an end.</param>
public sealed record WarmView(string Text, double? Percent)
{
    private const int LastLineLength = 110;

    /// <param name="status">Null while the output of the step says nothing the launcher can read.</param>
    /// <param name="stepNames">The command of each step.</param>
    /// <param name="step">The running step, counted from 0.</param>
    public static WarmView Of(WarmStatus? status, IReadOnlyList<string> stepNames, int step, string clock,
        string lastLine)
    {
        if (status is null)
        {
            if (lastLine.Length > LastLineLength)
                lastLine = lastLine[..LastLineLength] + " …";
            return new($"Warmup {StepName(stepNames, step)} · {clock}\n{lastLine}", null);
        }
        var of = stepNames.Count > 1 ? $"Step {step + 1} of {stepNames.Count} · " : "";
        return new($"{of}{status.Describe()} · {clock}", status.Percent);
    }

    /// <summary>What to say after a warmup whose steps all did their work.</summary>
    /// <param name="verdicts">One per step, in their order.</param>
    /// <param name="lastLine">The last line of the output.</param>
    public static string Finished(IReadOnlyList<WarmVerdict> verdicts, string lastLine)
    {
        // Every step's: the last one alone can say "nothing to build" after minutes of work in the one before.
        var summaries = verdicts.Select(verdict => verdict.Summary).OfType<string>().ToList();
        return summaries.Count == 0
            ? lastLine
            : string.Join(" ", summaries.SkipLast(1).Select(Sentence).Append(summaries[^1]));
    }

    private static string Sentence(string text) => text.EndsWith('.') ? text : text + ".";

    /// <returns>The command, with its place in the row where there are several.</returns>
    public static string StepName(IReadOnlyList<string> stepNames, int step) =>
        stepNames.Count > 1 ? $"step {step + 1} of {stepNames.Count} ({stepNames[step]})" : stepNames[step];
}
