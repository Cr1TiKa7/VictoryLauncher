using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace VictoryLauncher.Core;

public static class ProcessRunner
{
    /// <summary>
    /// Runs a child without a console window. Both pipes are drained as the child writes, otherwise a chatty child
    /// blocks on a full pipe. <paramref name="onLine"/> is called from thread pool threads, possibly at the same time.
    /// </summary>
    /// <param name="closeInputGrace">
    /// For a child that stops by itself when its input ends: the input is held open, cancelling closes it, and the
    /// child is only ended when it is still running after this time. Without it, cancelling ends the child at once.
    /// </param>
    public static async Task<int> RunAsync(ProcessStartInfo start, Action<string> onLine, CancellationToken cancel,
        TimeSpan? closeInputGrace = null)
    {
        cancel.ThrowIfCancellationRequested();
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        if (closeInputGrace is not null)
            start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) onLine(e.Data); };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (closeInputGrace is not { } grace || !await StoppedByItself(process, grace).ConfigureAwait(false))
                Kill(process);
            throw;
        }
        return process.ExitCode;
    }

    /// <returns>Whether the child ended within the grace after its input was closed.</returns>
    private static async Task<bool> StoppedByItself(Process process, TimeSpan grace)
    {
        try
        {
            process.StandardInput.Close();
        }
        catch (IOException)
        {
            // it is just ending
        }
        // awaited, not blocking: cancelling comes from the window's thread
        using var limit = new CancellationTokenSource(grace);
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // it ended on its own in the meantime
        }
    }
}

public static class Tools
{
    /// <summary>Cargo's own folder first: a program started from Explorer may not have it on PATH.</summary>
    public static string? Find(string name)
    {
        var cargoBin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin");
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new[] { cargoBin }.Concat(folders)
            .Select(folder => Path.Combine(folder.Trim('"'), name + ".exe"))
            .FirstOrDefault(File.Exists);
    }
}
