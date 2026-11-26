using System.Diagnostics;
using System.Globalization;

namespace VictoryLauncher.Core;

public enum UpdateState { UpToDate, Updated, CheckFailed, Blocked }

public sealed record UpdateResult(UpdateState State, string Head, IReadOnlyList<string> NewCommits, string? Message);

/// <summary>Brings the checkout to the newest commit of its upstream branch, by fast forward only.</summary>
public sealed class RepoUpdater(string git, string repoDir)
{
    public TimeSpan FetchLimit { get; init; } = TimeSpan.FromMinutes(2);

    public async Task<UpdateResult> CheckAndPullAsync(Action<string> log, CancellationToken cancel)
    {
        var (headCode, head) = await Git(log, cancel, "rev-parse", "HEAD");
        if (headCode != 0)
            return new(UpdateState.CheckFailed, "", [], "The checkout folder is not a readable git repository.\n" + Text(head));
        var current = head[0];

        // A hanging connection must not lock the launcher: the window cannot be closed while it checks.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(FetchLimit);
        int fetchCode;
        List<string> fetch;
        try
        {
            (fetchCode, fetch) = await Git(log, limit.Token, "fetch", "--prune");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new(UpdateState.CheckFailed, current, [], "Could not check: GitHub did not answer in time.");
        }
        if (fetchCode != 0)
            return new(UpdateState.CheckFailed, current, [], "Could not check (offline?).\n" + Text(fetch));

        var (listCode, newCommits) = await Git(log, cancel, "log", "--oneline", "--no-decorate", "HEAD..@{u}");
        if (listCode != 0)
            return new(UpdateState.Blocked, current, [], "The checked out state does not follow a branch on GitHub.\n" + Text(newCommits));
        if (newCommits.Count == 0)
            return new(UpdateState.UpToDate, current, [], null);

        // Untracked files (logs, own scripts) do not count: the merge refuses by itself if it would overwrite one.
        var (statusCode, changes) = await Git(log, cancel, "status", "--porcelain", "--untracked-files=no");
        if (statusCode != 0 || changes.Count > 0)
            return new(UpdateState.Blocked, current, newCommits,
                "There are new commits, but also your own changes in the checkout. Nothing was overwritten.\n" + Text(changes));

        // Not cancellable: a merge that is killed half way leaves a broken checkout.
        var (mergeCode, merge) = await Git(log, CancellationToken.None, "merge", "--ff-only", "@{u}");
        if (mergeCode != 0)
            return new(UpdateState.Blocked, current, newCommits,
                "Update not possible, the checkout has diverged from the state on GitHub.\n" + Text(merge));

        var (_, updated) = await Git(log, CancellationToken.None, "rev-parse", "HEAD");
        return new(UpdateState.Updated, updated[0], newCommits, null);
    }

    /// <returns>
    /// Text that changes with the content of <paramref name="paths"/> at the checked out commit, or null. For a single
    /// path it is git's own id; paths that do not exist are left out.
    /// </returns>
    public async Task<string?> SourceIdAsync(IReadOnlyList<string> paths, Action<string> log, CancellationToken cancel)
    {
        if (paths.Count == 1)
        {
            var (oneCode, one) = await Git(log, cancel, "rev-parse", "--verify", "HEAD:" + paths[0]);
            return oneCode == 0 && one.Count == 1 ? one[0] : null;
        }
        var (code, lines) = await Git(log, cancel, ["ls-tree", "HEAD", "--", .. paths]);
        return code == 0 && lines.Count > 0 ? string.Join('\n', lines) : null;
    }

    private static string Text(IEnumerable<string> lines) => string.Join('\n', lines.TakeLast(8));

    /// <returns>Exit code and lines: standard output on success, everything otherwise.</returns>
    private async Task<(int Code, List<string> Lines)> Git(Action<string> log, CancellationToken cancel, params string[] args)
    {
        var start = new ProcessStartInfo(git) { WorkingDirectory = repoDir };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        var lines = new List<string>();
        log("> git " + string.Join(' ', args));
        var code = await ProcessRunner.RunAsync(start, line =>
        {
            lock (lines) lines.Add(line);
            log(line);
        }, cancel);
        // git writes warnings (line endings, hints) to the error pipe even when it succeeds
        if (code == 0)
            lines.RemoveAll(l => l.StartsWith("warning:", StringComparison.Ordinal) || l.StartsWith("hint:", StringComparison.Ordinal));
        return (code, lines);
    }
}
