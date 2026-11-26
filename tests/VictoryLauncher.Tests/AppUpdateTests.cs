using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("v0.0.1", "0.0.1")]
    [InlineData("10.20.65534", "10.20.65534")]
    public void Reads_three_numbers_with_or_without_a_v(string text, string expected) =>
        Assert.Equal(Version.Parse(expected), ReleaseVersion.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3-beta")]
    [InlineData("01.2.3")]
    [InlineData("1.2.65535")] // the compiler refuses such a part
    [InlineData("1.2.99999999999")]
    [InlineData(" 1.2.3")]
    [InlineData("1.2.3\n")]
    public void Refuses_everything_else(string? text) => Assert.Null(ReleaseVersion.Parse(text));

    [Fact]
    public void An_assembly_version_counts_with_three_numbers() =>
        Assert.Equal(new Version(1, 2, 3), ReleaseVersion.Of(new Version(1, 2, 3, 0)));
}

public class AppUpdateTests : IDisposable
{
    private const string Repo = "https://github.com/Cr1TiKa7/VictoryLauncher/releases/download/";
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly Version Mine = new(1, 0, 0);
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(10);

    private readonly string _root = Directory.CreateTempSubdirectory("anyx-update-").FullName;
    private string Exe => Path.Combine(_root, "VictoryLauncher.exe");

    public void Dispose() => Directory.Delete(_root, true);

    private static string Json(string tag = "v1.1.0", string? name = null, string? url = null, string? digest = "sha256:" + Hash,
        long size = 1000, bool draft = false, bool prerelease = false)
    {
        name ??= $"VictoryLauncher-{tag}-win-x64.zip";
        return JsonSerializer.Serialize(new
        {
            tag_name = tag,
            draft,
            prerelease,
            assets = new object[]
            {
                new { name = "notes.txt", browser_download_url = Repo + tag + "/notes.txt", digest = "sha256:" + Hash, size = 5L },
                new { name, browser_download_url = url ?? Repo + tag + "/" + name, digest, size },
            },
        });
    }

    private sealed class Server(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Asked.Add(request);
            return answer(request, cancel);
        }

        public static Server With(HttpStatusCode code, byte[] body) =>
            new((_, _) => Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) }));
    }

    [Fact]
    public void Offers_a_newer_release_with_its_zip_and_checksum()
    {
        var update = AppUpdate.ReadLatest(Json(), Mine).Update!;

        Assert.Equal(new Version(1, 1, 0), update.Version);
        Assert.Equal(Repo + "v1.1.0/VictoryLauncher-v1.1.0-win-x64.zip", update.Download.AbsoluteUri);
        Assert.Equal(Hash, update.Sha256);
        Assert.Equal(1000, update.Size);
    }

    [Theory]
    [InlineData("v1.0.0")]
    [InlineData("v0.9.9")]
    public void Offers_nothing_that_is_not_newer(string tag) => Assert.Null(AppUpdate.ReadLatest(Json(tag), Mine).Update);

    [Fact]
    public void A_local_build_is_never_offered_anything() =>
        Assert.Null(AppUpdate.ReadLatest(Json(), new Version(0, 0, 0)).Update);

    public static TheoryData<string> Unusable => new()
    {
        Json("nightly"),
        Json(draft: true),
        Json(prerelease: true),
        Json(name: "VictoryLauncher-v1.0.5-win-x64.zip"),
        Json(digest: null),
        Json(digest: Hash),
        Json(digest: "sha1:" + Hash),
        Json(digest: "sha256:1234"),
        Json(size: 0),
        Json(size: 600L * 1024 * 1024),
        Json(url: "https://example.com/Cr1TiKa7/VictoryLauncher/releases/download/v1.1.0/VictoryLauncher-v1.1.0-win-x64.zip"),
        Json(url: "https://github.com/someone/else/releases/download/v1.1.0/VictoryLauncher-v1.1.0-win-x64.zip"),
        Json(url: "http://github.com/Cr1TiKa7/VictoryLauncher/releases/download/v1.1.0/VictoryLauncher-v1.1.0-win-x64.zip"),
        // looks like this repository until the dots are resolved
        Json(url: Repo + "../../../../someone/else/VictoryLauncher-v1.1.0-win-x64.zip"),
        """{"tag_name":"v1.1.0"}""",
        """{"tag_name":"v1.1.0","assets":[null]}""",
        """{"tag_name":5,"assets":"none"}""",
        "[]",
        "{ not json",
    };

    [Theory]
    [MemberData(nameof(Unusable))]
    public void Offers_nothing_it_cannot_check(string json)
    {
        var (update, note) = AppUpdate.ReadLatest(json, Mine);

        Assert.Null(update);
        Assert.NotEmpty(note);
    }

    [Fact]
    public async Task The_check_asks_github_with_a_program_name()
    {
        var server = Server.With(HttpStatusCode.OK, Encoding.UTF8.GetBytes(Json()));

        var (update, _) = await AppUpdate.CheckAsync(new HttpClient(server), Mine, Soon, CancellationToken.None);

        Assert.NotNull(update);
        var asked = Assert.Single(server.Asked);
        Assert.Equal("https://api.github.com/repos/Cr1TiKa7/VictoryLauncher/releases/latest", asked.RequestUri!.AbsoluteUri);
        Assert.Equal("VictoryLauncher", asked.Headers.UserAgent.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)] // no release yet
    [InlineData(HttpStatusCode.Forbidden)] // too many questions in this hour
    public async Task An_error_from_github_is_only_a_note(HttpStatusCode code)
    {
        var client = new HttpClient(Server.With(code, Encoding.UTF8.GetBytes(Json())));

        var (update, note) = await AppUpdate.CheckAsync(client, Mine, Soon, CancellationToken.None);

        Assert.Null(update);
        Assert.NotEmpty(note);
    }

    [Fact]
    public async Task Being_offline_is_only_a_note()
    {
        var client = new HttpClient(new Server((_, _) => throw new HttpRequestException("no network")));

        var (update, note) = await AppUpdate.CheckAsync(client, Mine, Soon, CancellationToken.None);

        Assert.Null(update);
        Assert.Contains("no network", note);
    }

    [Fact]
    public async Task A_server_that_never_answers_is_given_up_on()
    {
        var client = new HttpClient(new Server(async (_, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            throw new InvalidOperationException();
        }));

        var (update, note) = await AppUpdate.CheckAsync(client, Mine, TimeSpan.FromMilliseconds(100), CancellationToken.None)
            .WaitAsync(Soon);

        Assert.Null(update);
        Assert.NotEmpty(note);
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(content);
            }
        return memory.ToArray();
    }

    private static UpdateInfo Info(byte[] zip) => new(new Version(1, 1, 0),
        new Uri(Repo + "v1.1.0/VictoryLauncher-v1.1.0-win-x64.zip"),
        Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(), zip.Length);

    private Task<string> Download(byte[] served, UpdateInfo update, CancellationToken cancel = default) =>
        AppUpdate.DownloadAsync(new HttpClient(Server.With(HttpStatusCode.OK, served)), update, Exe, null, cancel);

    private string[] Files() => [.. Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories)
        .Select(path => Path.GetRelativePath(_root, path)).Order()];

    [Fact]
    public async Task Takes_only_the_launcher_out_of_the_zip()
    {
        File.WriteAllText(Exe, "old");
        var zip = Zip(("README.txt", "read me"), (@"..\evil.txt", "evil"), ("sub/VictoryLauncher.exe", "wrong"),
            ("VictoryLauncher.exe", "new"));

        var fresh = await Download(zip, Info(zip));

        Assert.Equal("new", File.ReadAllText(fresh));
        Assert.Equal("old", File.ReadAllText(Exe));
        Assert.Equal(["VictoryLauncher.exe", Path.GetFileName(fresh)], Files());
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(_root)!, "evil.txt")));
    }

    [Fact]
    public async Task Reports_how_far_the_download_is()
    {
        var zip = Zip(("VictoryLauncher.exe", "new"));
        var seen = new List<(long, long)>();

        await AppUpdate.DownloadAsync(new HttpClient(Server.With(HttpStatusCode.OK, zip)), Info(zip), Exe,
            (done, total) => seen.Add((done, total)), CancellationToken.None);

        Assert.Equal((zip.Length, zip.Length), seen[^1]);
    }

    public static TheoryData<string> Damage => new() { "checksum", "shorter", "longer", "no launcher", "not a zip" };

    [Theory]
    [MemberData(nameof(Damage))]
    public async Task A_download_that_is_not_what_was_announced_leaves_nothing_behind(string damage)
    {
        File.WriteAllText(Exe, "old");
        var zip = Zip((damage == "no launcher" ? "other.exe" : "VictoryLauncher.exe", "new"));
        if (damage == "not a zip")
            zip = Encoding.UTF8.GetBytes("<html>sorry</html>");
        var update = damage switch
        {
            "checksum" => Info(zip) with { Sha256 = Hash },
            "shorter" => Info(zip) with { Size = zip.Length + 1 },
            "longer" => Info(zip) with { Size = zip.Length - 1 },
            _ => Info(zip),
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => Download(zip, update));

        Assert.Equal(["VictoryLauncher.exe"], Files());
        Assert.Equal("old", File.ReadAllText(Exe));
    }

    [Fact]
    public async Task A_refused_download_leaves_nothing_behind()
    {
        var client = new HttpClient(Server.With(HttpStatusCode.NotFound, []));

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            AppUpdate.DownloadAsync(client, Info([1]), Exe, null, CancellationToken.None));

        Assert.Empty(Files());
    }

    [Fact]
    public async Task A_cancelled_download_leaves_nothing_behind()
    {
        var zip = Zip(("VictoryLauncher.exe", "new"));
        using var cancel = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AppUpdate.DownloadAsync(
            new HttpClient(Server.With(HttpStatusCode.OK, zip)), Info(zip), Exe, (_, _) => cancel.Cancel(), cancel.Token));

        Assert.Empty(Files());
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Swapping_puts_the_new_program_in_place_and_keeps_the_old_one_aside()
    {
        Write("VictoryLauncher.exe", "old");
        var fresh = Write("VictoryLauncher.exe.new-0a1b2c3d", "new");

        var old = AppUpdate.Swap(fresh, Exe);

        Assert.Equal("new", File.ReadAllText(Exe));
        Assert.Equal("old", File.ReadAllText(old));
        Assert.False(File.Exists(fresh));
    }

    [Fact]
    public void A_swap_that_fails_leaves_the_old_program_in_place()
    {
        Write("VictoryLauncher.exe", "old");

        Assert.ThrowsAny<IOException>(() => AppUpdate.Swap(Path.Combine(_root, "missing"), Exe));

        Assert.Equal("old", File.ReadAllText(Exe));
        Assert.Equal(["VictoryLauncher.exe"], Files());
    }

    [Fact]
    public void The_old_program_can_be_brought_back()
    {
        Write("VictoryLauncher.exe", "old");
        var old = AppUpdate.Swap(Write("VictoryLauncher.exe.new-0a1b2c3d", "broken"), Exe);

        AppUpdate.Restore(old, Exe);

        Assert.Equal("old", File.ReadAllText(Exe));
    }

    [Fact]
    public void Cleaning_up_removes_its_own_leftovers_and_nothing_else()
    {
        string[] keep =
        [
            "VictoryLauncher.exe", "VictoryLauncher.exe.old", "VictoryLauncher.exe.old-notes.txt",
            "VictoryLauncher.exe.new-0a1b2c3d.bak", "other.exe.old-0a1b2c3d", "settings.json",
        ];
        foreach (var name in keep.Concat(["VictoryLauncher.exe.old-0a1b2c3d", "VictoryLauncher.exe.new-11111111",
                     "VictoryLauncher.exe.zip-ffffffff"]))
            Write(name, "x");

        AppUpdate.CleanUp(Exe);

        Assert.Equal([.. keep.Order()], Files());
    }

    /// <summary>A program that waits for input, so it keeps running until the test ends it.</summary>
    private static Process Waiting() =>
        Process.Start(new ProcessStartInfo("cmd.exe", "/c pause") { CreateNoWindow = true, RedirectStandardInput = true })!;

    [Fact]
    public async Task The_new_version_waits_until_the_old_one_has_ended()
    {
        using var old = Waiting();
        try
        {
            var waiting = Task.Run(() => AppUpdate.WaitForPredecessor(AppUpdate.RestartArguments(old.Id), Soon));

            await Task.Delay(500);
            Assert.False(waiting.IsCompleted);

            old.Kill();
            await waiting.WaitAsync(Soon);
        }
        finally
        {
            old.Kill();
        }
    }

    [Fact]
    public void It_does_not_wait_forever()
    {
        using var old = Waiting();
        try
        {
            var watch = Stopwatch.StartNew();

            AppUpdate.WaitForPredecessor(AppUpdate.RestartArguments(old.Id), TimeSpan.FromMilliseconds(300));

            Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(5));
            Assert.False(old.HasExited);
        }
        finally
        {
            old.Kill();
        }
    }

    [Theory]
    [InlineData]
    [InlineData("--after-update")]
    [InlineData("--after-update", "soon")]
    [InlineData("--after-update", "-4")]
    [InlineData("--after-update", "2000000000")] // no such process any more
    [InlineData("something", "4")]
    public void A_normal_start_does_not_wait(params string[] arguments)
    {
        var watch = Stopwatch.StartNew();

        AppUpdate.WaitForPredecessor(arguments, Soon);

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void A_running_program_can_be_replaced_and_is_cleaned_up_after_it_ended()
    {
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Exe);
        // waits for input, so it keeps running until the test ends it
        var start = new ProcessStartInfo(Exe, "/c pause") { CreateNoWindow = true, RedirectStandardInput = true };
        using var running = Process.Start(start)!;
        string old;
        try
        {
            old = AppUpdate.Swap(Write("VictoryLauncher.exe.new-0a1b2c3d", "new"), Exe);
            AppUpdate.CleanUp(Exe);

            Assert.Equal("new", File.ReadAllText(Exe));
            Assert.False(running.HasExited);
            Assert.True(File.Exists(old));
        }
        finally
        {
            running.Kill();
            running.WaitForExit(10_000);
        }

        AppUpdate.CleanUp(Exe);

        Assert.False(File.Exists(old));
    }
}
