using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <param name="Warning">A note when part of the scan did not work, otherwise null.</param>
public sealed record ScanResult(IReadOnlyList<ProcessInfo> Processes, int OwnSession, long FreeRamBytes, string? Warning);

/// <summary>Reads what the processes of this Windows session hold. Changes nothing.</summary>
public static class ProcessScanner
{
    /// <param name="sample">How long processor use is measured.</param>
    public static Task<ScanResult> ScanAsync(TimeSpan sample, CancellationToken cancel) =>
        Task.Run(() => Scan(sample, cancel), cancel);

    private static async Task<ScanResult> Scan(TimeSpan sample, CancellationToken cancel)
    {
        using var current = Process.GetCurrentProcess();
        var ownSession = current.SessionId;
        var all = Process.GetProcesses();
        try
        {
            var own = all.Where(p => p.SessionId == ownSession).ToList();
            var before = own.ToDictionary(p => p.Id, CpuTime);
            var watch = Stopwatch.StartNew();
            await Task.Delay(sample, cancel);
            var span = watch.Elapsed;

            var videoMemory = GpuMemory.PerProcess(out var warning);
            var parents = ProcessTree.Parents();
            var descriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<ProcessInfo>();
            foreach (var process in own)
            {
                cancel.ThrowIfCancellationRequested();
                try
                {
                    // Without a start time the process could not be told apart from a later one: leave it out.
                    var started = process.StartTime;
                    process.Refresh();
                    var path = ProgramFile(process);
                    var cpu = before[process.Id] is { } first && CpuTime(process) is { } last
                        ? Math.Clamp((last - first) / span / Environment.ProcessorCount * 100, 0, 100)
                        : 0;
                    found.Add(new(process.Id, started, process.ProcessName, path, Description(path, descriptions),
                        ownSession, videoMemory.GetValueOrDefault(process.Id), process.PrivateMemorySize64, cpu,
                        parents.GetValueOrDefault(process.Id)));
                }
                catch (Exception e) when (IsGoneOrDenied(e))
                {
                }
            }
            return new(found, ownSession, FreeRam(), warning);
        }
        finally
        {
            foreach (var process in all)
                process.Dispose();
        }
    }

    // Which process started which, from a snapshot of the process list.
    private static class ProcessTree
    {
        private const uint SnapshotProcesses = 0x2;

        /// <returns>Process id to parent id. Empty when the snapshot cannot be taken.</returns>
        public static Dictionary<int, int> Parents()
        {
            var result = new Dictionary<int, int>();
            var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
            if (snapshot == -1)
                return result;
            try
            {
                var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
                for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
                    result[(int)entry.Id] = (int)entry.ParentId;
            }
            finally
            {
                CloseHandle(snapshot);
            }
            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct ProcessEntry
        {
            public uint Size, Usage, Id;
            public nint DefaultHeap;
            public uint Module, Threads, ParentId;
            public int BasePriority;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string ExeFile;
        }

        [DllImport("kernel32.dll")]
        private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Process32NextW(nint snapshot, ref ProcessEntry entry);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(nint handle);
    }

    internal static bool IsGoneOrDenied(Exception e) =>
        e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException;

    private static TimeSpan? CpuTime(Process process)
    {
        try
        {
            process.Refresh();
            return process.TotalProcessorTime;
        }
        catch (Exception e) when (IsGoneOrDenied(e))
        {
            return null;
        }
    }

    private static string? ProgramFile(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception e) when (IsGoneOrDenied(e))
        {
            return null;
        }
    }

    private static string Description(string? path, Dictionary<string, string> known)
    {
        if (path is null)
            return "";
        if (known.TryGetValue(path, out var description))
            return description;
        try
        {
            description = FileVersionInfo.GetVersionInfo(path).FileDescription ?? "";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            description = "";
        }
        return known[path] = description;
    }

    private static long FreeRam()
    {
        var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        // Unknown counts as plenty: memory alone is then no reason to list a program.
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailablePhysical : long.MaxValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}

/// <summary>
/// Dedicated video memory per process from Windows' performance counters, asked for by their English names.
/// Shared allocations count more than once, so the numbers are only good for ranking.
/// </summary>
internal static partial class GpuMemory
{
    private const uint FormatLarge = 0x400;
    private const uint MoreData = 0x800007D2;

    [GeneratedRegex(@"^pid_(\d+)_")]
    private static partial Regex Instance();

    public static Dictionary<int, long> PerProcess(out string? warning)
    {
        var result = new Dictionary<int, long>();
        var status = PdhOpenQueryW(null, 0, out var query);
        if (status != 0)
        {
            warning = Failed(status);
            return result;
        }
        try
        {
            status = PdhAddEnglishCounterW(query, @"\GPU Process Memory(*)\Dedicated Usage", 0, out var counter);
            if (status == 0)
                status = PdhCollectQueryData(query);
            if (status != 0)
            {
                warning = Failed(status);
                return result;
            }

            uint size = 0;
            status = PdhGetFormattedCounterArrayW(counter, FormatLarge, ref size, out var count, 0);
            if (status != MoreData)
            {
                warning = Failed(status);
                return result;
            }
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                status = PdhGetFormattedCounterArrayW(counter, FormatLarge, ref size, out count, buffer);
                if (status != 0)
                {
                    warning = Failed(status);
                    return result;
                }
                var itemSize = Marshal.SizeOf<CounterItem>();
                for (var i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<CounterItem>(buffer + i * itemSize);
                    var match = Instance().Match(Marshal.PtrToStringUni(item.Name) ?? "");
                    if (item.Status <= 1 && match.Success && int.TryParse(match.Groups[1].Value, out var id))
                        result[id] = result.GetValueOrDefault(id) + item.Value; // one entry per graphics adapter
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            _ = PdhCloseQuery(query);
        }
        warning = null;
        return result;
    }

    private static string Failed(uint status) => $"The video memory per program cannot be read (error 0x{status:X8}).";

    [StructLayout(LayoutKind.Sequential)]
    private struct CounterItem
    {
        public nint Name;
        public uint Status;
        public long Value;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQueryW(string? source, nint user, out nint query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounterW(nint query, string path, nint user, out nint counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(nint query);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterArrayW(nint counter, uint format, ref uint size, out uint count, nint buffer);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(nint query);
}
