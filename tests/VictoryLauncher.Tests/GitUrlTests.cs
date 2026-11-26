using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class GitUrlTests
{
    [Theory]
    [InlineData("https://github.com/name/project", "https://github.com/name/project")]
    [InlineData("  https://github.com/name/project.git/  ", "https://github.com/name/project")]
    [InlineData("https://github.com/name/project/", "https://github.com/name/project")]
    [InlineData("HTTPS://GitHub.com/Name/Project", "https://github.com/Name/Project")]
    [InlineData("https://codeberg.org/group/sub/project.git", "https://codeberg.org/group/sub/project")]
    public void Accepts_a_repository_address(string typed, string expected)
    {
        var (url, error) = GitUrl.Parse(typed);

        Assert.Null(error);
        Assert.Equal(expected, url);
    }

    [Theory]
    [InlineData("")]
    [InlineData("name/project")]
    [InlineData("http://github.com/name/project")] // unencrypted: the code that gets run could be swapped on the way
    [InlineData("git@github.com:name/project.git")]
    [InlineData("ssh://git@github.com/name/project")]
    [InlineData("file:///C:/somewhere")]
    [InlineData(@"C:\somewhere")]
    [InlineData("ext::sh -c calc")]
    [InlineData("--upload-pack=calc")]
    [InlineData("https://user:token@github.com/name/project")] // would be stored in settings.json
    [InlineData("https://github.com/name/project?x=1")]
    [InlineData("https://github.com/name/project#main")]
    [InlineData("https://github.com/na me/project")]
    [InlineData("https://github.com/")]
    [InlineData("https://github.com/name")]
    [InlineData("https://github.com/name/project/tree/dev")]
    public void Refuses_everything_else_with_a_reason(string typed)
    {
        var (url, error) = GitUrl.Parse(typed);

        Assert.Null(url);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("https://github.com/A/B.git", "https://github.com/a/b/", true)]
    [InlineData("https://github.com/a/b", "https://github.com/a/c", false)]
    [InlineData("https://github.com/a/b", "https://example.com/a/b", false)]
    public void Compares_addresses_without_spelling_differences(string first, string second, bool same) =>
        Assert.Equal(same, GitUrl.Same(first, second));
}
