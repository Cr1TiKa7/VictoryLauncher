using System.Diagnostics;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class WarmProgressTests
{
    private const string Translated =
        "2026-11-25T22:28:53.74Z  INFO victory_gpu::pipeline: shader translated stage=\"compute\" hash=52 user_data=4";
    private const string Result =
        "warm         25753 translated, 5 refused; 2729 compute pipelines, 0 refused; 410 s";

    [Fact]
    public void Takes_the_periodic_report()
    {
        var p = new WarmProgress(25758);
        p.Feed("    63 s  2000 shaders");
        Assert.Equal(2000, p.Count);
        Assert.InRange(p.Percent, 7.7, 7.8);
        Assert.Null(p.ResultLine);
    }

    [Fact]
    public void Counts_translation_lines_and_keeps_the_larger_number()
    {
        var p = new WarmProgress(100);
        p.Feed(Translated);
        p.Feed(Translated);
        Assert.Equal(2, p.Count);
        p.Feed("     1 s  2000 shaders");
        p.Feed(Translated);
        Assert.Equal(2000, p.Count);
    }

    [Fact]
    public void Stays_below_100_percent_until_the_result_line()
    {
        var p = new WarmProgress(1000);
        p.Feed("     9 s  2000 shaders");
        Assert.Equal(99, p.Percent);
    }

    [Fact]
    public void The_result_line_finishes()
    {
        var p = new WarmProgress(30000);
        p.Feed(Result);
        Assert.Equal(Result, p.ResultLine);
        Assert.Equal(25758, p.ResultTotal);
        Assert.Equal(25758, p.Count);
        Assert.Equal(100, p.Percent);
    }

    [Fact]
    public void Ignores_the_warm_up_summary_of_a_run()
    {
        var p = new WarmProgress(100);
        p.Feed("warm-up      12 shaders so far, 3 s");
        Assert.Equal(0, p.Count);
        Assert.Null(p.ResultLine);
    }
}

public class ProcessRunnerTests
{
    private static ProcessStartInfo Cmd(string command)
    {
        var psi = new ProcessStartInfo("cmd.exe");
        psi.ArgumentList.Add("/c");
        psi.ArgumentList.Add(command);
        return psi;
    }

    [Fact]
    public async Task Drains_both_pipes_of_a_chatty_child()
    {
        var lines = 0;
        var code = await ProcessRunner.RunAsync(
            Cmd("for /L %i in (1,1,10000) do @(echo out %i & echo err %i 1>&2)"),
            _ => Interlocked.Increment(ref lines), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(0, code);
        Assert.Equal(20000, lines);
    }

    [Fact]
    public async Task Cancel_kills_the_whole_tree_quickly()
    {
        using var cts = new CancellationTokenSource(500);
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProcessRunner.RunAsync(Cmd("ping -n 30 127.0.0.1"), _ => { }, cts.Token));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Finds_git() => Assert.True(File.Exists(Tools.Find("git")));

    [Fact]
    public void An_unknown_tool_is_null() => Assert.Null(Tools.Find("no-such-tool-anyx"));
}
