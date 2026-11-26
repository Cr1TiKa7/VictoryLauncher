using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VictoryLauncher.Core;

/// <summary>The answer to the second question, about programs that did not close.</summary>
public enum ForceAnswer { Force, Leave, Cancel }

/// <summary>
/// Closes the processes of listed programs and nothing else: each one is checked by id and start time, and no
/// process tree is ever ended. Every method returns what still runs.
/// </summary>
public static class ProgramCloser
{
    private enum State { Gone, Listed, Unverifiable }

    /// <summary>Closes the chosen programs before the game. Nothing is forced without the answer "Force".</summary>
    /// <param name="askForce">Asked only when something still runs after <paramref name="wait"/>.</param>
    /// <param name="reportStuck">Told what could not even be forced.</param>
    /// <returns>True when the game should start.</returns>
    public static async Task<bool> CloseForStartAsync(IReadOnlyList<ProgramCandidate> chosen, TimeSpan wait,
        Func<IReadOnlyList<ProgramCandidate>, ForceAnswer> askForce,
        Action<IReadOnlyList<ProgramCandidate>> reportStuck, CancellationToken cancel)
    {
        var remaining = await RequestCloseAsync(chosen, wait, cancel);
        if (remaining.Count == 0)
            return true;
        switch (askForce(remaining))
        {
            case ForceAnswer.Cancel:
                return false;
            case ForceAnswer.Force:
                // What ended while the question was open is skipped by the check before each kill.
                var stuck = await ForceCloseAsync(remaining, wait, cancel);
                if (stuck.Count > 0)
                    reportStuck(stuck);
                return true;
            default:
                return true;
        }
    }

    /// <summary>The same as a click on the X of every window: a program can still ask whether to save.</summary>
    public static Task<IReadOnlyList<ProgramCandidate>> RequestCloseAsync(IReadOnlyList<ProgramCandidate> programs,
        TimeSpan wait, CancellationToken cancel)
    {
        var ids = new HashSet<uint>();
        ForEachListed(programs, process => ids.Add((uint)process.Id));
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var id);
            if (ids.Contains(id) && IsOnItsOwn(window))
                PostMessageW(window, 0x0010 /* WM_CLOSE */, 0, 0);
            return true;
        }, 0);
        return WaitAsync(programs, wait, cancel);
    }

    // A window with an X of its own: shown, and not a dialog of another shown window. (Windows without a
    // taskbar button often have a hidden owner.)
    private static bool IsOnItsOwn(nint window)
    {
        if (!IsWindowVisible(window))
            return false;
        var owner = GetWindow(window, 4 /* owner */);
        return owner == 0 || !IsWindowVisible(owner);
    }

    /// <summary>Ends the processes at once. What was not saved is lost.</summary>
    public static Task<IReadOnlyList<ProgramCandidate>> ForceCloseAsync(IReadOnlyList<ProgramCandidate> programs,
        TimeSpan wait, CancellationToken cancel)
    {
        ForEachListed(programs, process => process.Kill());
        return WaitAsync(programs, wait, cancel);
    }

    /// <returns>The programs with at least one listed process left, reduced to those processes.</returns>
    public static IReadOnlyList<ProgramCandidate> StillRunning(IReadOnlyList<ProgramCandidate> programs) =>
    [
        .. programs
            .Select(program => program with { Processes = [.. program.Processes.Where(IsRunning)] })
            .Where(program => program.Processes.Count > 0),
    ];

    private static async Task<IReadOnlyList<ProgramCandidate>> WaitAsync(IReadOnlyList<ProgramCandidate> programs,
        TimeSpan wait, CancellationToken cancel)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var remaining = StillRunning(programs);
            if (remaining.Count == 0 || watch.Elapsed >= wait)
                return remaining;
            await Task.Delay(200, cancel);
        }
    }

    private static bool IsRunning(ProcessRef listed)
    {
        var state = Check(listed, out var process);
        process?.Dispose();
        // A process that cannot be looked at is reported as running, never as closed.
        return state != State.Gone;
    }

    private static void ForEachListed(IReadOnlyList<ProgramCandidate> programs, Action<Process> action)
    {
        foreach (var listed in programs.SelectMany(p => p.Processes))
        {
            if (Check(listed, out var process) != State.Listed)
            {
                process?.Dispose();
                continue;
            }
            try
            {
                action(process!);
            }
            // Ended meanwhile, or Windows refuses: it then shows up as still running.
            catch (Exception e) when (ProcessScanner.IsGoneOrDenied(e))
            {
            }
            finally
            {
                process!.Dispose();
            }
        }
    }

    private static State Check(ProcessRef listed, out Process? process)
    {
        try
        {
            process = Process.GetProcessById(listed.Id);
        }
        catch (ArgumentException)
        {
            process = null;
            return State.Gone;
        }
        try
        {
            return !process.HasExited && process.StartTime == listed.Started ? State.Listed : State.Gone;
        }
        catch (InvalidOperationException)
        {
            return State.Gone;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return State.Unverifiable;
        }
    }

    private delegate bool WindowCallback(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(WindowCallback callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint relation);

    [DllImport("user32.dll")]
    private static extern bool PostMessageW(nint window, uint message, nint w, nint l);
}
