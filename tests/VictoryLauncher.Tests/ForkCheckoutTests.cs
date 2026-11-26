using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class ForkCheckoutTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("anyx-clone-").FullName;
    private readonly string _origin;

    public ForkCheckoutTests()
    {
        _origin = Path.Combine(_root, "origin");
        Directory.CreateDirectory(_origin);
        TestGit.Run(_origin, "init", "--initial-branch=main");
        File.WriteAllText(Path.Combine(_origin, "readme.txt"), "hello");
        TestGit.Run(_origin, "add", "readme.txt");
        TestGit.Run(_origin, "commit", "-m", "first");
    }

    public void Dispose() => TestGit.Delete(_root);

    private string Folder(params string[] parts) => Path.Combine([_root, .. parts]);

    private Task<string?> Ensure(string folder, string? url = null, CancellationToken cancel = default) =>
        ForkCheckout.EnsureAsync("git", url ?? _origin, folder, _ => { }, cancel);

    private string[] Leftovers(string parent) => Directory.Exists(parent)
        ? [.. Directory.GetDirectories(parent, "*.cloning-*")]
        : [];

    [Fact]
    public async Task Clones_into_a_folder_that_does_not_exist_yet()
    {
        var target = Folder("forks", "one");

        Assert.Null(await Ensure(target));

        Assert.Equal("hello", File.ReadAllText(Path.Combine(target, "readme.txt")));
        Assert.True(ForkCheckout.Exists(target));
        Assert.Empty(Leftovers(Folder("forks")));
    }

    [Fact]
    public async Task Clones_into_an_empty_folder()
    {
        var target = Folder("empty");
        Directory.CreateDirectory(target);

        Assert.Null(await Ensure(target));
        Assert.True(ForkCheckout.Exists(target));
    }

    [Fact]
    public async Task Accepts_the_checkout_that_is_already_there_and_leaves_it_alone()
    {
        var target = Folder("again");
        await Ensure(target);
        File.WriteAllText(Path.Combine(target, "own.txt"), "mine");

        Assert.Null(await Ensure(target));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(target, "own.txt")));
    }

    [Fact]
    public async Task Refuses_a_checkout_of_another_repository()
    {
        var target = Folder("other");
        await Ensure(target);

        var problem = await Ensure(target, "https://github.com/somebody/else");

        Assert.Contains("anderes Repository", problem);
    }

    [Fact]
    public async Task Refuses_a_folder_with_other_files_and_keeps_them()
    {
        var target = Folder("full");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "photo.jpg"), "x");

        var problem = await Ensure(target);

        Assert.Contains("nicht leer", problem);
        Assert.Equal("photo.jpg", Path.GetFileName(Assert.Single(Directory.GetFileSystemEntries(target))));
    }

    [Fact]
    public async Task A_failed_clone_leaves_nothing_behind()
    {
        var target = Folder("forks2", "gone");

        var problem = await Ensure(target, Folder("no-such-origin"));

        Assert.Contains("Auschecken fehlgeschlagen", problem);
        Assert.False(Directory.Exists(target));
        Assert.Empty(Leftovers(Folder("forks2")));
    }

    [Fact]
    public async Task A_cancelled_clone_leaves_nothing_behind()
    {
        var target = Folder("forks3", "cancelled");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Ensure(target, cancel: new CancellationToken(canceled: true)));

        Assert.False(Directory.Exists(target));
        Assert.Empty(Leftovers(Folder("forks3")));
    }
}
