using System.Collections.Concurrent;
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

    private static ProcessStartInfo PowerShell(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe");
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            psi.ArgumentList.Add(argument);
        return psi;
    }

    private static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task The_input_stays_open_while_the_child_works()
    {
        var lines = new ConcurrentQueue<string>();
        var code = await ProcessRunner.RunAsync(PowerShell(
            "$r = [Console]::OpenStandardInput().ReadAsync((New-Object byte[] 1), 0, 1); " +
            "Start-Sleep -Milliseconds 1500; \"ended=$($r.IsCompleted)\"; exit 0"),
            lines.Enqueue, CancellationToken.None, Grace).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(0, code);
        Assert.Contains("ended=False", lines);
    }

    [Fact]
    public async Task Cancel_closes_the_input_and_lets_the_child_finish()
    {
        var lines = new ConcurrentQueue<string>();
        using var cts = new CancellationTokenSource();
        var run = ProcessRunner.RunAsync(PowerShell(
            "'ready'; $null = [Console]::In.ReadToEnd(); Start-Sleep -Milliseconds 500; 'saved'"),
            // not Cancel() here: what waits for it would go on inside this callback, which is still reading the output
            line => { lines.Enqueue(line); if (line == "ready") cts.CancelAfter(1); }, cts.Token, Grace);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(60)));
        Assert.Contains("saved", lines);
    }

    [Fact]
    public async Task A_child_that_ignores_the_closed_input_is_killed_after_the_grace()
    {
        using var cts = new CancellationTokenSource(500);
        var watch = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(
            Cmd("ping -n 30 127.0.0.1"), _ => { }, cts.Token, TimeSpan.FromSeconds(1)));
        Assert.InRange(watch.Elapsed, TimeSpan.FromSeconds(1.4), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task A_child_with_an_open_input_that_ends_by_itself_returns_its_code() =>
        Assert.Equal(7, await ProcessRunner.RunAsync(Cmd("exit 7"), _ => { }, CancellationToken.None, Grace)
            .WaitAsync(TimeSpan.FromSeconds(60)));
}
