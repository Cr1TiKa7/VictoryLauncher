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
    [InlineData(Always + "\npub const TUNED: &[(&str, &str)] = &[\n];")]
    // An entry the launcher cannot read must not be dropped without a word.
    [InlineData(Always + "\npub const TUNED: &[(&str, &str)] = &[(\"VICTORY_RELAY\", \"2\"), (\"VICTORY_MODE\", DEFAULT_MODE)];")]
    // The table is there, in a form this parser does not know: that is not the same as no table.
    [InlineData("pub const ALWAYS: [(&str, &str); 1] = [(\"VICTORY_DISCORD\", \"0\")];\n" + Tuned)]
    public void Refuses_a_file_it_cannot_read_completely(string source) =>
        Assert.Throws<InvalidDataException>(() => Tuning.Parse(source));

    // AnyXboxX 04a08f5 dropped ALWAYS. Shortened from its settings.rs at 37d4686.
    private const string CombinedBase = """
        /// `third_party/victory/scripts/play.ps1`'s settings without `-Safe`, in its order. Applied unless
        /// `--no-tuned-settings`.
        pub const TUNED: &[(&str, &str)] = &[
            ("VICTORY_TEXTURE_NO_WAIT", "1"),
            ("VICTORY_RELAY", "2"),
            ("VICTORY_AMBIENT", "0.8"),
        ];

        /// What Linux (Proton) + AMD adds to [`TUNED`].
        pub const LINUX_AMD_EXTRA: &[(&str, &str)] = &[
            ("VICTORY_FETCH_CHECK", "1"),
            ("VKD3D_CONFIG", "descriptor_heap"),
        ];

        pub const QUALITY: &[(&str, &str)] = &[
            ("VICTORY_CARD_BUFFERS_MB", "1024"),
        ];

        pub const GAME_DIR: &str = "VICTORY_GAME_DIR";
        """;

    [Fact]
    public void A_file_without_the_unconditional_table_gives_the_tuned_one() =>
        Assert.Equal(["VICTORY_AMBIENT=0.8", "VICTORY_RELAY=2", "VICTORY_TEXTURE_NO_WAIT=1"],
            Tuning.Parse(CombinedBase).Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal));
}
