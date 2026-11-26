namespace VictoryLauncher.Core;

/// <param name="Path">The program file, or null when it cannot be read (elevated or protected process).</param>
/// <param name="ParentId">The id of the process that started this one, 0 when unknown.</param>
public sealed record ProcessInfo(int Id, DateTime Started, string Name, string? Path, string Description,
    int SessionId, long VramBytes, long PrivateBytes, double CpuPercent, int ParentId = 0);

/// <summary>Id and start time: Windows reuses process ids.</summary>
public sealed record ProcessRef(int Id, DateTime Started);

/// <summary>A program worth closing before the game starts, with all its processes.</summary>
public sealed record ProgramCandidate(string Name, string Path, IReadOnlyList<ProcessRef> Processes,
    long VramBytes, long PrivateBytes, double CpuPercent, string Reason);

public static class ProgramCheck
{
    private const long Mb = 1024 * 1024;
    private const long ListedVram = 60 * Mb;
    private const long HighVram = 400 * Mb;
    private const double BusyCpuPercent = 2;
    private const long TightFreeRam = 8 * 1024 * Mb;
    private const long ListedPrivate = 500 * Mb;

    // Windows and its guards, parts other programs rely on, and what this launcher and its work need.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "dwm", "explorer", "csrss", "svchost", "winlogon", "wininit", "services", "lsass", "smss", "fontdrvhost",
        "sihost", "taskhostw", "ctfmon", "conhost", "dllhost", "audiodg", "RuntimeBroker",
        "MsMpEng", "NisSrv", "MpDefenderCoreService", "SecurityHealthService", "SecurityHealthSystray",
        "NVDisplay.Container", "nvcontainer", "msedgewebview2",
        "claude", "VictoryLauncher", "victory", "kelvaroq", "kelvaroq-engine", "git", "cargo", "rustc",
    };

    // Whatever these started is part of their work: closing it would break their session or the launcher.
    private static readonly HashSet<string> OwnersOfTheirChildren = new(StringComparer.OrdinalIgnoreCase)
    {
        "claude", "VictoryLauncher",
    };

    public static IReadOnlyList<ProgramCandidate> Select(IEnumerable<ProcessInfo> processes, int ownSession,
        long freeRamBytes, string windowsDir)
    {
        var windows = System.IO.Path.TrimEndingDirectorySeparator(windowsDir) + System.IO.Path.DirectorySeparatorChar;
        var ramIsTight = freeRamBytes < TightFreeRam;
        var all = processes.ToList();
        var byId = all.GroupBy(p => p.Id).ToDictionary(group => group.Key, group => group.First());

        return
        [
            .. all
                .Where(p => p.SessionId == ownSession && p.Path is not null && !Protected.Contains(p.Name)
                            && !p.Path.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
                            && !StartedByAnOwner(p, byId))
                .GroupBy(p => p.Path!, StringComparer.OrdinalIgnoreCase)
                .Select(group => Candidate(group.Key, [.. group], ramIsTight))
                .OfType<ProgramCandidate>()
                .OrderByDescending(c => c.VramBytes)
                .ThenByDescending(c => c.CpuPercent)
                .ThenByDescending(c => c.PrivateBytes),
        ];
    }

    private static bool StartedByAnOwner(ProcessInfo process, Dictionary<int, ProcessInfo> byId)
    {
        var seen = new HashSet<int>();
        while (seen.Add(process.Id) && byId.TryGetValue(process.ParentId, out var parent)
               // Windows reuses ids: a "parent" that is younger than its child is somebody else.
               && parent.Started <= process.Started)
        {
            if (OwnersOfTheirChildren.Contains(parent.Name))
                return true;
            process = parent;
        }
        return false;
    }

    private static ProgramCandidate? Candidate(string path, List<ProcessInfo> group, bool ramIsTight)
    {
        var vram = group.Sum(p => p.VramBytes);
        var memory = group.Sum(p => p.PrivateBytes);
        var cpu = group.Sum(p => p.CpuPercent);

        var reasons = new List<string>();
        if (vram >= ListedVram)
            reasons.Add(vram >= HighVram ? "high video memory" : "medium video memory");
        if (cpu >= BusyCpuPercent)
            reasons.Add("busy CPU");
        if (ramIsTight && memory >= ListedPrivate)
            reasons.Add("high RAM use");
        if (reasons.Count == 0)
            return null;

        var name = group.Select(p => p.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d))?.Trim()
                   ?? group[0].Name;
        return new(name, path, [.. group.Select(p => new ProcessRef(p.Id, p.Started))], vram, memory, cpu,
            string.Join(", ", reasons));
    }
}
