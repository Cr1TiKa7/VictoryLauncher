using System.Diagnostics;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

/// <summary>Reads the real processes of this machine (read only). Needs a graphics card.</summary>
public class ProcessScannerTests
{
    [Fact]
    public async Task The_scan_knows_who_started_a_process()
    {
        // waits for input, so it lives until the test ends it
        var start = new ProcessStartInfo("cmd.exe", "/c pause") { CreateNoWindow = true, RedirectStandardInput = true };
        using var child = Process.Start(start)!;
        try
        {
            var scan = await ProcessScanner.ScanAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None);

            Assert.Equal(Environment.ProcessId, Assert.Single(scan.Processes, p => p.Id == child.Id).ParentId);
        }
        finally
        {
            child.Kill();
        }
    }

    [Fact]
    [Trait("Category", "Desktop")] // needs the counters of a graphics card, left out on the build server
    public async Task The_scan_knows_this_process_and_the_video_memory_in_use()
    {
        var scan = await ProcessScanner.ScanAsync(TimeSpan.FromMilliseconds(500), CancellationToken.None);

        using var me = Process.GetCurrentProcess();
        var found = Assert.Single(scan.Processes, p => p.Id == me.Id);
        Assert.Equal(me.SessionId, scan.OwnSession);
        Assert.Equal(me.SessionId, found.SessionId);
        Assert.Equal(me.StartTime, found.Started);
        Assert.Equal(Environment.ProcessPath, found.Path, ignoreCase: true);
        Assert.True(found.PrivateBytes > 0);
        Assert.True(scan.FreeRamBytes > 0);
        Assert.Null(scan.Warning);
        Assert.Contains(scan.Processes, p => p.VramBytes > 0);
        Assert.All(scan.Processes, p => Assert.InRange(p.CpuPercent, 0, 100));
    }
}
