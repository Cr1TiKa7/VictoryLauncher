using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class WarmStateTests
{
    private static readonly Dictionary<string, string> Tuned = new() { ["VICTORY_RELAY"] = "2", ["VICTORY_ASK_ONCE"] = "1" };

    private static string Print(string tree = "tree1", Dictionary<string, string>? tuned = null,
        string game = @"G:\game", string cache = @"G:\cache") =>
        WarmState.Fingerprint(tree, tuned ?? Tuned, game, cache);

    [Fact]
    public void The_same_inputs_give_the_same_fingerprint_whatever_the_order()
    {
        var reversed = new Dictionary<string, string> { ["VICTORY_ASK_ONCE"] = "1", ["VICTORY_RELAY"] = "2" };
        Assert.Equal(Print(), Print(tuned: reversed));
    }

    [Fact]
    public void Spelling_of_a_folder_does_not_matter() =>
        Assert.Equal(Print(), Print(game: @"g:\GAME\", cache: @"g:\Cache\"));

    [Fact]
    public void Runtime_settings_and_folders_each_change_the_fingerprint()
    {
        var changed = new Dictionary<string, string>(Tuned) { ["VICTORY_RELAY"] = "1" };
        var all = new[] { Print(), Print(tree: "tree2"), Print(tuned: changed), Print(game: @"G:\other"), Print(cache: @"G:\other") };
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    [Fact]
    public void An_anyxboxx_warmup_of_the_earlier_version_stays_valid()
    {
        // value of the earlier version for these inputs, computed independently of this code
        var settings = new Dictionary<string, string>(Tuned) { ["VICTORY_CARD_BUFFERS_MB"] = "1024" };

        Assert.Equal("92379676831293491E8E4C8A997B70644000AD980EE6DE2440EDFC9A4704D78C", Print(tuned: settings));
    }

    [Fact]
    public void Another_save_folder_changes_the_fingerprint() =>
        Assert.NotEqual(Print(), WarmState.Fingerprint("tree1", Tuned, @"G:\game", @"G:\cache", @"G:\data"));

    [Fact]
    public void Hint_asks_for_a_first_warmup()
    {
        Assert.Contains("Noch kein Warmup", WarmState.Hint(new ForkState(), "now"));
    }

    [Fact]
    public void No_hint_when_the_fingerprint_matches()
    {
        var state = new ForkState { LastWarmCommit = "old commit", LastWarmFingerprint = "now" };
        Assert.Equal("", WarmState.Hint(state, "now"));
    }

    [Theory]
    [InlineData("before")]
    [InlineData(null)] // warmed by an earlier launcher version, which stored no fingerprint
    public void Hint_after_a_relevant_change(string? stored)
    {
        var state = new ForkState { LastWarmCommit = "abc", LastWarmFingerprint = stored };
        Assert.Contains("Seit dem letzten Warmup", WarmState.Hint(state, "now"));
    }

    [Fact]
    public void No_hint_while_the_current_state_is_unknown() =>
        Assert.Equal("", WarmState.Hint(new ForkState(), null));
}
