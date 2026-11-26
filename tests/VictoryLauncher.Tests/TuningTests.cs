using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class TuningTests
{
    private static string Source() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "settings.rs"));

    [Theory]
    [InlineData("VICTORY_CARD_WINDOWS_MB", "1536")]
    [InlineData("VICTORY_VRAM_IDLE_MS", "8000,2000,1000")]
    [InlineData("VICTORY_PIPELINE_WAIT_MS", "2500")]
    [InlineData("VICTORY_DISCORD", "0")]
    public void Reads_the_tuned_table_and_the_unconditional_one(string name, string value) =>
        Assert.Equal(value, Tuning.Parse(Source())[name]);

    [Fact]
    public void The_tuned_table_wins_over_the_unconditional_one() =>
        Assert.Equal("2", Tuning.Parse(Source())["VICTORY_OCEAN_READBACK"]);

    [Fact]
    public void Leaves_out_the_optional_table_comments_and_other_constants() =>
        Assert.Equal(
            ["VICTORY_CARD_WINDOWS_MB", "VICTORY_DISCORD", "VICTORY_OCEAN_READBACK", "VICTORY_PIPELINE_WAIT_MS",
                "VICTORY_VRAM_IDLE_MS"],
            Tuning.Parse(Source()).Keys.Order(StringComparer.Ordinal));

    private const string Always = """pub const ALWAYS: &[(&str, &str)] = &[("VICTORY_DISCORD", "0")];""";
    private const string Tuned = """pub const TUNED: &[(&str, &str)] = &[("VICTORY_RELAY", "2")];""";

    [Theory]
    [InlineData("")]
    [InlineData(Always)]
    [InlineData(Tuned)]
    [InlineData(Always + "\npub const TUNED: &[(&str, &str)] = &[\n];")]
    // An entry the launcher cannot read must not be dropped without a word.
    [InlineData(Always + "\npub const TUNED: &[(&str, &str)] = &[(\"VICTORY_RELAY\", \"2\"), (\"VICTORY_MODE\", DEFAULT_MODE)];")]
    public void Refuses_a_file_it_cannot_read_completely(string source) =>
        Assert.Throws<InvalidDataException>(() => Tuning.Parse(source));
}
