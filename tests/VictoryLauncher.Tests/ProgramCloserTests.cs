using System.Diagnostics;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

/// <summary>Closes only throwaway programs that these tests start themselves.</summary>
[Trait("Category", "Desktop")] // needs real windows, left out on the build server
public class ProgramCloserTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ShortWait = TimeSpan.FromSeconds(2);

    private readonly string _root = Directory.CreateTempSubdirectory("anyx-close-").FullName;
    private readonly List<Process> _started = [];

    public void Dispose()
    {
        foreach (var process in _started)
        {
            if (!process.HasExited)
                process.Kill();
            process.WaitForExit(10_000);
            process.Dispose();
        }
        Directory.Delete(_root, true);
    }

    /// <summary>A program with windows placed off screen; with <paramref name="refuses"/> they stay open on close.</summary>
    private Process StartWindows(int count = 1, bool refuses = false)
    {
        var script = Path.Combine(_root, $"windows{_started.Count}.ps1");
        File.WriteAllText(script, $$"""
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            $context = New-Object System.Windows.Forms.ApplicationContext
            $script:open = {{count}}
            foreach ($i in 1..{{count}}) {
                $form = New-Object System.Windows.Forms.Form
                $form.Text = "AnyXboxX launcher test window $i"
                $form.ShowInTaskbar = $false
                $form.StartPosition = 'Manual'
                $form.Location = New-Object System.Drawing.Point(-32000, -32000)
                if (${{(refuses ? "true" : "false")}}) { $form.add_FormClosing({ param($s, $e) $e.Cancel = $true }) }
                $form.add_FormClosed({ $script:open--; if ($script:open -eq 0) { $context.ExitThread() } })
                $form.Show()
            }
            [Console]::Out.WriteLine('shown')
            [System.Windows.Forms.Application]::Run($context)
            """);
        var start = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script })
            start.ArgumentList.Add(argument);
        var process = Process.Start(start)!;
        _started.Add(process);
        Assert.Equal("shown", process.StandardOutput.ReadLine());
        return process;
    }

    private static ProgramCandidate Candidate(Process process, TimeSpan startOffset = default) =>
        new("test program", "test.exe", [new ProcessRef(process.Id, process.StartTime + startOffset)], 0, 0, 0, "test");

    [Fact]
    public async Task Asking_to_close_ends_a_program_that_agrees()
    {
        var program = StartWindows();

        var remaining = await ProgramCloser.RequestCloseAsync([Candidate(program)], Wait, CancellationToken.None);

        Assert.Empty(remaining);
        Assert.True(program.HasExited);
    }

    [Fact]
    public async Task Asking_to_close_reaches_every_window()
    {
        var program = StartWindows(count: 3);

        var remaining = await ProgramCloser.RequestCloseAsync([Candidate(program)], Wait, CancellationToken.None);

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task A_program_that_refuses_stays_and_is_reported()
    {
        var refusing = StartWindows(refuses: true);
        var agreeing = StartWindows();

        var remaining = await ProgramCloser.RequestCloseAsync(
            [Candidate(refusing), Candidate(agreeing)], ShortWait, CancellationToken.None);

        Assert.Equal(refusing.Id, Assert.Single(Assert.Single(remaining).Processes).Id);
        Assert.False(refusing.HasExited);
    }

    [Fact]
    public async Task Forcing_ends_only_the_listed_program()
    {
        var listed = StartWindows(refuses: true);
        var other = StartWindows(refuses: true);

        var remaining = await ProgramCloser.ForceCloseAsync([Candidate(listed)], Wait, CancellationToken.None);

        Assert.Empty(remaining);
        Assert.True(listed.HasExited);
        Assert.False(other.HasExited);
    }

    private static async Task<(bool Start, List<string> Asked)> CloseForStart(ForceAnswer answer,
        params Process[] programs)
    {
        var asked = new List<string>();
        var start = await ProgramCloser.CloseForStartAsync([.. programs.Select(p => Candidate(p))], ShortWait,
            remaining =>
            {
                asked.AddRange(remaining.SelectMany(r => r.Processes).Select(r => r.Id.ToString()));
                return answer;
            },
            _ => Assert.Fail("nothing should be stuck"), CancellationToken.None);
        return (start, asked);
    }

    [Theory]
    [InlineData(ForceAnswer.Force)]
    [InlineData(ForceAnswer.Cancel)]
    public async Task No_second_question_when_everything_closes_politely(ForceAnswer unused)
    {
        var (start, asked) = await CloseForStart(unused, StartWindows());

        Assert.True(start);
        Assert.Empty(asked);
    }

    [Theory]
    [InlineData(ForceAnswer.Leave, true)]
    [InlineData(ForceAnswer.Cancel, false)]
    public async Task Without_a_yes_nothing_is_forced(ForceAnswer answer, bool starts)
    {
        var refusing = StartWindows(refuses: true);

        var (start, asked) = await CloseForStart(answer, refusing);

        Assert.Equal(starts, start);
        Assert.Equal([refusing.Id.ToString()], asked);
        Assert.False(refusing.WaitForExit(500));
    }

    [Fact]
    public async Task A_yes_forces_what_is_left_and_starts()
    {
        var refusing = StartWindows(refuses: true);

        var (start, _) = await CloseForStart(ForceAnswer.Force, refusing);

        Assert.True(start);
        Assert.True(refusing.HasExited);
    }

    [Fact]
    public async Task A_reused_process_id_is_never_touched()
    {
        var newcomer = StartWindows();
        // The listed process had this id, but started at another time.
        var stale = Candidate(newcomer, startOffset: TimeSpan.FromMinutes(-5));

        Assert.Empty(ProgramCloser.StillRunning([stale]));
        await ProgramCloser.RequestCloseAsync([stale], ShortWait, CancellationToken.None);
        await ProgramCloser.ForceCloseAsync([stale], ShortWait, CancellationToken.None);

        Assert.False(newcomer.WaitForExit(1000));
    }
}
