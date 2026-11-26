using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class ProgramCheckTests
{
    private const long Mb = 1024 * 1024;
    private const long PlentyOfRam = 16 * 1024 * Mb;
    private const long LittleRam = 6 * 1024 * Mb;
    private const string Windows = @"C:\Windows";
    private static int _nextId = 100;

    private static ProcessInfo Process(string name, long vramMb = 0, long privateMb = 0, double cpu = 0,
        string? path = @"C:\Apps\{name}\{name}.exe", int session = 1, string description = "") =>
        new(++_nextId, new DateTime(2026, 1, 1), name, path?.Replace("{name}", name), description, session,
            vramMb * Mb, privateMb * Mb, cpu);

    private static IReadOnlyList<ProgramCandidate> Select(long freeRam, params ProcessInfo[] processes) =>
        ProgramCheck.Select(processes, ownSession: 1, freeRam, Windows);

    [Theory]
    [InlineData(500, "high video memory")]
    [InlineData(400, "high video memory")]
    [InlineData(60, "medium video memory")]
    public void Lists_a_program_that_holds_video_memory(int mb, string reason)
    {
        var found = Assert.Single(Select(PlentyOfRam, Process("game", vramMb: mb)));
        Assert.Equal(reason, found.Reason);
        Assert.Equal("game", found.Name);
    }

    [Fact]
    public void Leaves_out_a_quiet_program() =>
        Assert.Empty(Select(PlentyOfRam, Process("editor", vramMb: 59, privateMb: 4000, cpu: 1.9)));

    [Fact]
    public void Lists_a_program_that_keeps_the_processor_busy() =>
        Assert.Equal("busy CPU", Assert.Single(Select(PlentyOfRam, Process("busy", cpu: 2))).Reason);

    [Fact]
    public void Memory_counts_only_when_it_is_getting_tight()
    {
        Assert.Empty(Select(PlentyOfRam, Process("editor", privateMb: 500)));
        Assert.Equal("high RAM use", Assert.Single(Select(LittleRam, Process("editor", privateMb: 500))).Reason);
        Assert.Empty(Select(LittleRam, Process("editor", privateMb: 499)));
    }

    [Fact]
    public void Processes_of_one_program_are_added_up_and_stay_individually_known()
    {
        var found = Assert.Single(Select(PlentyOfRam,
            Process("chrome", vramMb: 30), Process("chrome", vramMb: 30), Process("chrome", cpu: 3)));

        Assert.Equal(60 * Mb, found.VramBytes);
        Assert.Equal(3, found.Processes.Count);
        Assert.Equal("medium video memory, busy CPU", found.Reason);
    }

    [Fact]
    public void The_same_name_from_another_file_is_another_program() =>
        Assert.Equal(2, Select(PlentyOfRam,
            Process("javaw", vramMb: 100, path: @"C:\one\javaw.exe"),
            Process("javaw", vramMb: 100, path: @"C:\two\javaw.exe")).Count);

    [Fact]
    public void Shows_the_description_of_the_file_when_there_is_one() =>
        Assert.Equal("Google Chrome",
            Assert.Single(Select(PlentyOfRam, Process("chrome", vramMb: 100, description: "Google Chrome"))).Name);

    [Theory]
    [InlineData("claude")]
    [InlineData("victory")]
    [InlineData("git")]
    [InlineData("cargo")]
    [InlineData("rustc")]
    [InlineData("VictoryLauncher")]
    [InlineData("kelvaroq")]
    [InlineData("kelvaroq-engine")]
    [InlineData("msedgewebview2")]
    [InlineData("MsMpEng")]
    [InlineData("dwm")]
    [InlineData("explorer")]
    [InlineData("CSRSS")]
    public void Never_offers_a_protected_program(string name) =>
        Assert.Empty(Select(PlentyOfRam, Process(name, vramMb: 2000, cpu: 50)));

    [Theory]
    [InlineData(@"C:\Windows\System32\anything.exe")]
    [InlineData(@"c:\windows\SystemApps\x\app.exe")]
    [InlineData(null)] // elevated or protected: the file cannot be read, and it could not be closed either
    public void Never_offers_windows_or_unreadable_programs(string? path) =>
        Assert.Empty(Select(PlentyOfRam, Process("anything", vramMb: 2000, path: path)));

    [Fact]
    public void A_folder_that_only_starts_like_the_windows_folder_is_fine() =>
        Assert.Single(Select(PlentyOfRam, Process("tool", vramMb: 100, path: @"C:\WindowsApps-like\tool.exe")));

    [Fact]
    public void Never_offers_another_session() =>
        Assert.Empty(Select(PlentyOfRam, Process("service", vramMb: 2000, session: 0)));

    private static ProcessInfo Child(ProcessInfo parent, string name, long vramMb = 2000) =>
        Process(name, vramMb: vramMb) with { ParentId = parent.Id };

    [Theory]
    [InlineData("claude")]
    [InlineData("VictoryLauncher")]
    public void Never_offers_anything_started_by_claude_or_the_launcher(string root)
    {
        var top = Process(root);
        var shell = Child(top, "pwsh");
        var server = Child(shell, "node");

        Assert.Empty(Select(PlentyOfRam, server, shell, top));
    }

    [Fact]
    public void Another_copy_of_the_same_program_is_still_offered_without_claudes()
    {
        var top = Process("claude");
        var claudes = Child(top, "pwsh");
        var own = Process("pwsh", vramMb: 100);

        var found = Assert.Single(Select(PlentyOfRam, top, claudes, own));

        Assert.Equal(own.Id, Assert.Single(found.Processes).Id);
    }

    [Fact]
    public void A_parent_id_that_now_belongs_to_a_newer_process_is_not_followed()
    {
        var newer = Process("claude") with { Started = new DateTime(2026, 1, 2) };
        var older = Child(newer, "editor");

        Assert.Single(Select(PlentyOfRam, newer, older));
    }

    [Fact]
    public void A_program_started_from_the_desktop_is_offered()
    {
        var desktop = Process("explorer");

        Assert.Single(Select(PlentyOfRam, desktop, Child(desktop, "game")));
    }

    [Fact]
    public void A_loop_of_parent_ids_ends()
    {
        var first = Process("one", vramMb: 100);
        var second = Child(first, "two", vramMb: 100);

        Assert.Equal(2, Select(PlentyOfRam, first with { ParentId = second.Id }, second).Count);
    }

    [Fact]
    public void The_biggest_video_memory_user_comes_first() =>
        Assert.Equal(["big", "small", "cpu"], Select(PlentyOfRam,
            Process("cpu", cpu: 9), Process("small", vramMb: 100), Process("big", vramMb: 900)).Select(c => c.Name));
}
