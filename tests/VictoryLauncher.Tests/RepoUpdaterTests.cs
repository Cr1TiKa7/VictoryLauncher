using System.Diagnostics;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class RepoUpdaterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("anyx-git-").FullName;
    private readonly string _origin, _author, _clone;

    public RepoUpdaterTests()
    {
        _origin = Path.Combine(_root, "origin.git");
        _author = Path.Combine(_root, "author");
        _clone = Path.Combine(_root, "clone");
        Git(_root, "init", "--bare", "--initial-branch=main", _origin);
        Git(_root, "clone", _origin, _author);
        Commit("first", "a.txt", "1");
        Git(_author, "push", "-u", "origin", "HEAD:main");
        Git(_root, "clone", _origin, _clone);
    }

    public void Dispose()
    {
        // git marks its object files read only
        foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }

    private static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.name=test", "-c", "user.email=test@example.invalid" }.Concat(args))
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var error = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, error.Result);
        return output.Trim();
    }

    private void Commit(string message, string file, string content)
    {
        var path = Path.Combine(_author, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        Git(_author, "add", file);
        Git(_author, "commit", "-m", message);
    }

    private void PushNewCommit(string file = "a.txt")
    {
        Commit("second change", file, "2");
        Git(_author, "push", "origin", "HEAD:main");
    }

    private Task<UpdateResult> Run() =>
        new RepoUpdater("git", _clone).CheckAndPullAsync(_ => { }, CancellationToken.None);

    [Fact]
    public async Task Reports_up_to_date()
    {
        var result = await Run();
        Assert.Equal(UpdateState.UpToDate, result.State);
        Assert.Equal(Git(_clone, "rev-parse", "HEAD"), result.Head);
    }

    [Fact]
    public async Task Pulls_a_new_commit_and_lists_it()
    {
        PushNewCommit();
        var result = await Run();
        Assert.Equal(UpdateState.Updated, result.State);
        Assert.Equal(Git(_author, "rev-parse", "HEAD"), result.Head);
        Assert.Contains("second change", Assert.Single(result.NewCommits));
    }

    [Fact]
    public async Task A_tracked_change_blocks_the_update_and_is_kept()
    {
        PushNewCommit("b.txt");
        File.WriteAllText(Path.Combine(_clone, "a.txt"), "mine");
        var before = Git(_clone, "rev-parse", "HEAD");

        var result = await Run();

        Assert.Equal(UpdateState.Blocked, result.State);
        Assert.Equal(before, result.Head);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_clone, "a.txt")));
    }

    [Fact]
    public async Task Untracked_files_do_not_block()
    {
        PushNewCommit();
        File.WriteAllText(Path.Combine(_clone, "run.log"), "log");
        Assert.Equal(UpdateState.Updated, (await Run()).State);
    }

    [Fact]
    public async Task A_diverged_checkout_is_blocked()
    {
        PushNewCommit();
        File.WriteAllText(Path.Combine(_clone, "own.txt"), "own");
        Git(_clone, "add", "own.txt");
        Git(_clone, "commit", "-m", "own work");
        var own = Git(_clone, "rev-parse", "HEAD");

        var result = await Run();

        Assert.Equal(UpdateState.Blocked, result.State);
        Assert.Equal(own, result.Head);
    }

    private Task<string?> SourceId(params string[] paths) =>
        new RepoUpdater("git", _clone).SourceIdAsync(paths, _ => { }, CancellationToken.None);

    private Task<string?> RuntimeTree() => SourceId("third_party/victory");

    [Fact]
    public async Task Runtime_tree_changes_only_with_the_runtime_folder()
    {
        PushNewCommit("third_party/victory/lib.rs");
        await Run();
        var first = await RuntimeTree();

        PushNewCommit("prototype.py");
        Assert.Equal(UpdateState.Updated, (await Run()).State);
        var afterOtherFile = await RuntimeTree();

        Commit("runtime change", "third_party/victory/lib.rs", "3");
        Git(_author, "push", "origin", "HEAD:main");
        await Run();
        var afterRuntime = await RuntimeTree();

        Assert.NotNull(first);
        Assert.Equal(first, afterOtherFile);
        Assert.NotEqual(first, afterRuntime);
    }

    [Fact]
    public async Task Runtime_tree_is_unknown_without_a_runtime_folder() => Assert.Null(await RuntimeTree());

    [Fact]
    public async Task The_id_of_one_folder_is_gits_own_id_for_it()
    {
        // unchanged from the AnyXboxX only version, so its warmup stays valid
        PushNewCommit("third_party/victory/lib.rs");
        await Run();

        Assert.Equal(Git(_clone, "rev-parse", "HEAD:third_party/victory"), await RuntimeTree());
    }

    [Fact]
    public async Task Several_source_paths_ignore_a_new_readme()
    {
        string[] sources = ["apps", "crates", "Cargo.lock", "not-there"];
        PushNewCommit("crates/lib.rs");
        await Run();
        var first = await SourceId(sources);

        PushNewCommit("README.md");
        await Run();
        var afterReadme = await SourceId(sources);

        PushNewCommit("Cargo.lock");
        await Run();
        var afterLock = await SourceId(sources);

        Assert.NotNull(first);
        Assert.Equal(first, afterReadme);
        Assert.NotEqual(first, afterLock);
    }

    [Fact]
    public async Task No_source_path_at_all_gives_no_id() => Assert.Null(await SourceId("apps", "crates"));

    [Fact]
    public async Task A_fetch_over_the_time_limit_is_a_failed_check()
    {
        var updater = new RepoUpdater("git", _clone) { FetchLimit = TimeSpan.FromTicks(1) };

        var result = await updater.CheckAndPullAsync(_ => { }, CancellationToken.None);

        Assert.Equal(UpdateState.CheckFailed, result.State);
        Assert.NotEmpty(result.Head);
    }

    [Fact]
    public async Task An_unreachable_origin_is_a_failed_check()
    {
        Git(_clone, "remote", "set-url", "origin", Path.Combine(_root, "gone.git"));
        var result = await Run();
        Assert.Equal(UpdateState.CheckFailed, result.State);
        Assert.NotNull(result.Message);
    }
}
