using System.Diagnostics;

namespace VictoryLauncher.Core;

/// <summary>Makes sure a fork is checked out. Never touches files that were there before.</summary>
public static class ForkCheckout
{
    private static readonly TimeSpan CloneLimit = TimeSpan.FromMinutes(10);

    public static bool Exists(string repoDir) => Directory.Exists(Path.Combine(repoDir, ".git"));

    /// <returns>Null when <paramref name="repoDir"/> holds a checkout of <paramref name="url"/>, else the reason why not.</returns>
    public static async Task<string?> EnsureAsync(string git, string url, string repoDir, Action<string> log,
        CancellationToken cancel)
    {
        if (Exists(repoDir))
        {
            var (code, lines) = await Git(git, repoDir, log, cancel, "remote", "get-url", "origin");
            if (code == 0 && lines.Count > 0 && GitUrl.Same(lines[0], url))
                return null;
            return $"The checkout folder holds a different repository ({(code == 0 ? lines.FirstOrDefault() : "without origin")}). " +
                   $"Please choose another folder: {repoDir}";
        }
        if (File.Exists(repoDir) || (Directory.Exists(repoDir) && Directory.EnumerateFileSystemEntries(repoDir).Any()))
            return $"The checkout folder is not empty and not a git checkout: {repoDir}";

        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoDir));
        var parent = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(parent);
        // Cloned next to the target and renamed when complete: a broken or cancelled clone never looks like a checkout.
        var partial = $"{target}.cloning-{Guid.NewGuid().ToString("N")[..8]}";
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            limit.CancelAfter(CloneLimit);
            var (code, lines) = await Git(git, parent, log, limit.Token, "clone", "--", url, partial);
            if (code != 0)
                return "Checkout failed.\n" + string.Join('\n', lines.TakeLast(8));
            if (Directory.Exists(target))
                Directory.Delete(target); // empty, checked above; not recursive on purpose
            Directory.Move(partial, target);
            return null;
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return "Checkout failed: it took too long.";
        }
        finally
        {
            Remove(partial, log);
        }
    }

    /// <summary>Removes only the folder this run created for itself.</summary>
    private static void Remove(string partial, Action<string> log)
    {
        if (!Directory.Exists(partial))
            return;
        try
        {
            // git marks its object files read only
            foreach (var file in Directory.EnumerateFiles(partial, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(partial, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log($"Leftovers of a cancelled clone were not removed: {partial} ({e.Message})");
        }
    }

    private static async Task<(int Code, List<string> Lines)> Git(string git, string folder, Action<string> log,
        CancellationToken cancel, params string[] args)
    {
        var start = new ProcessStartInfo(git) { WorkingDirectory = folder };
        // no password questions: nobody could answer them, and only public forks are meant
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GCM_INTERACTIVE"] = "never";
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        var lines = new List<string>();
        log("> git " + string.Join(' ', args));
        var code = await ProcessRunner.RunAsync(start, line =>
        {
            lock (lines) lines.Add(line);
            log(line);
        }, cancel);
        if (code == 0)
            lines.RemoveAll(l => l.StartsWith("warning:", StringComparison.Ordinal));
        return (code, lines);
    }
}
