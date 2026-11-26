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

    /// <returns>The command, with its place in the row where there are several.</returns>
    public static string StepName(IReadOnlyList<string> stepNames, int step) =>
        stepNames.Count > 1 ? $"step {step + 1} of {stepNames.Count} ({stepNames[step]})" : stepNames[step];
}
