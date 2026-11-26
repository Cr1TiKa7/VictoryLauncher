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
    public static async Task<int> RunAsync(ProcessStartInfo start, Action<string> onLine, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
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
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
            catch (Exception e) when (e is InvalidOperationException or Win32Exception)
            {
                // it ended on its own in the meantime
            }
            throw;
        }
        return process.ExitCode;
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
