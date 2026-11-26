using System.Diagnostics;
using System.Text;

namespace VictoryLauncher.Tests;

/// <summary>The change list of a release, built by packaging\Get-ReleaseNotes.ps1. Needs PowerShell 7 and git.</summary>
public sealed class ReleaseNotesScriptTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("release-notes-").FullName;
    private int _files;

    public ReleaseNotesScriptTests() => TestGit.Run(_repo, "init", "--quiet", "--initial-branch=main");

    public void Dispose() => TestGit.Delete(_repo);

    private void Commit(string folder, string subject)
    {
        Directory.CreateDirectory(Path.Combine(_repo, folder));
        File.WriteAllText(Path.Combine(_repo, folder, $"file{++_files}.txt"), subject);
        TestGit.Run(_repo, "add", "--all");
        TestGit.Run(_repo, "commit", "--quiet", "-m", subject);
    }

    private void Tag(string name) => TestGit.Run(_repo, "tag", name);

    private string Notes(string tag)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(AppContext.BaseDirectory, "Get-ReleaseNotes.ps1"),
                     "-Tag", tag, "-Repository", "owner/name",
                 })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error.Result);
        return output.ReplaceLineEndings("\n").Trim();
    }

    private static string Compare(string from, string to) =>
        $"**Full Changelog**: https://github.com/owner/name/compare/{from}...{to}";

    [Fact]
    public void It_lists_what_changed_in_the_launcher_since_the_release_before()
    {
        Commit("src", "Start the launcher");
        Tag("v1.0.0");
        Commit("src", "Show the keys");
        Commit("docs", "Plan the next thing");
        Commit(".github", "Change the build");
        Commit("src", "Fix the warmup");

        Assert.Equal($"## What's new\n\n- Show the keys\n- Fix the warmup\n\n{Compare("v1.0.0", "v1.0.1")}",
            Notes("v1.0.1"));
    }

    [Fact]
    public void The_release_before_is_the_highest_lower_version()
    {
        Commit("src", "Old");
        Tag("v1.9.0");
        Commit("src", "Ten");
        Tag("v1.10.0");
        Tag("nightly");
        Commit("src", "Eleven");
        Tag("v1.11.0"); // a pushed tag exists already when the notes are built
        Tag("v2.0.0");

        Assert.Equal($"## What's new\n\n- Eleven\n\n{Compare("v1.10.0", "v1.11.0")}", Notes("v1.11.0"));
    }

    [Fact]
    public void A_merged_branch_is_listed_without_the_merge_commit()
    {
        Commit("src", "Start");
        Tag("v1.0.0");
        TestGit.Run(_repo, "checkout", "--quiet", "-b", "fix");
        Commit("src", "Fix on a branch");
        TestGit.Run(_repo, "checkout", "--quiet", "main");
        Commit("src", "Work on main");
        TestGit.Run(_repo, "merge", "--quiet", "--no-ff", "-m", "Merge the fix", "fix");

        var notes = Notes("v1.0.1");

        Assert.Contains("- Fix on a branch\n", notes);
        Assert.Contains("- Work on main\n", notes);
        Assert.DoesNotContain("Merge the fix", notes);
    }

    [Fact]
    public void Without_a_change_to_the_launcher_it_says_so()
    {
        Commit("src", "Start");
        Tag("v1.0.0");
        Commit("docs", "Write something");

        Assert.Equal($"## What's new\n\nNo changes to the launcher itself.\n\n{Compare("v1.0.0", "v1.0.1")}",
            Notes("v1.0.1"));
    }

    [Fact]
    public void The_first_release_has_nothing_to_compare_with()
    {
        Commit("src", "Start the launcher");

        Assert.Equal("## What's new\n\n- Start the launcher", Notes("v1.0.0"));
    }

    [Fact]
    public void A_word_with_an_at_sign_notifies_nobody()
    {
        Commit("src", "Start");
        Tag("v1.0.0");
        Commit("src", "Read @someone/types like a@b");

        Assert.Contains("- Read `@someone/types` like a@b\n", Notes("v1.0.1"));
    }

    [Fact]
    public void Letters_beyond_ascii_arrive_unchanged()
    {
        Commit("src", "Start");
        Tag("v1.0.0");
        Commit("src", "Name the key “Ö” …");

        Assert.Contains("- Name the key “Ö” …\n", Notes("v1.0.1"));
    }
}
