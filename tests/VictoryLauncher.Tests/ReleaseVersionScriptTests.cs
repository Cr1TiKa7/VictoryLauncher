using System.Diagnostics;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

/// <summary>The rules of the release action, in packaging\Get-ReleaseVersion.ps1. Needs PowerShell 7, as the action does.</summary>
public class ReleaseVersionScriptTests
{
    private const string Main = "refs/heads/main";

    private static (int Code, string Output, string Error) Run(string eventName, string gitRef, string requested = "",
        string existingTags = "")
    {
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[]
                 {
                     "-NoProfile", "-NonInteractive", "-File",
                     Path.Combine(AppContext.BaseDirectory, "Get-ReleaseVersion.ps1"),
                     "-EventName", eventName, "-Ref", gitRef, "-Requested", requested, "-ExistingTags", existingTags,
                 })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.ReplaceLineEndings("\n").Trim(), error.Result);
    }

    [Theory]
    [InlineData("1.0.0", "", "1.0.0")]
    [InlineData("v1.2.0", "v1.0.0 v1.1.9", "1.2.0")]
    [InlineData(" 2.0.0 ", "v1.10.0 v1.9.0", "2.0.0")] // typed with a space by hand
    [InlineData("1.10.0", "v1.9.0", "1.10.0")] // compared as numbers, not as text
    [InlineData("1.0.0", "nightly test-tag", "1.0.0")]
    public void A_click_on_run_workflow_releases_the_typed_version(string typed, string tags, string version)
    {
        var (code, output, error) = Run("workflow_dispatch", Main, typed, tags);

        Assert.True(code == 0, error);
        Assert.Equal($"version={version}\ntag=v{version}", output);
        Assert.Equal(Version.Parse(version), ReleaseVersion.Parse("v" + version));
    }

    [Theory]
    [InlineData(Main, "", "")]
    [InlineData(Main, "1.0", "")]
    [InlineData(Main, "1.0.0-beta", "")]
    [InlineData(Main, "1.0.0; echo hi", "")]
    [InlineData(Main, "01.0.0", "")]
    [InlineData(Main, "1.0.65535", "")]
    [InlineData(Main, "0.0.0", "")] // reserved for builds nobody released
    [InlineData(Main, "1.0.0", "v1.0.0")] // taken
    [InlineData(Main, "1.0.0", "v0.9.0 v1.1.0")] // not higher than what is out
    [InlineData("refs/heads/feature", "1.0.0", "")] // only main is released
    public void A_version_that_cannot_be_released_stops_the_run(string gitRef, string typed, string tags)
    {
        var (code, output, error) = Run("workflow_dispatch", gitRef, typed, tags);

        Assert.NotEqual(0, code);
        Assert.Empty(output);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void A_pushed_tag_releases_its_own_version()
    {
        var (code, output, error) = Run("push", "refs/tags/v1.4.2", existingTags: "v1.4.2 v2.0.0");

        Assert.True(code == 0, error);
        Assert.Equal("version=1.4.2\ntag=v1.4.2", output);
    }

    [Theory]
    [InlineData("refs/tags/v1.4")]
    [InlineData("refs/tags/v1.4.2-rc1")]
    [InlineData("refs/tags/1.4.2")] // the launcher looks for tags that start with a v
    [InlineData("refs/tags/v0.0.0")]
    public void A_tag_that_is_no_version_stops_the_run(string gitRef) => Assert.NotEqual(0, Run("push", gitRef).Code);

    [Theory]
    [InlineData("push", Main)]
    [InlineData("pull_request", "refs/pull/3/merge")]
    public void Everything_else_builds_without_releasing(string eventName, string gitRef)
    {
        var (code, output, error) = Run(eventName, gitRef, existingTags: "v1.0.0");

        Assert.True(code == 0, error);
        Assert.Equal("version=0.0.0\ntag=", output);
    }
}
