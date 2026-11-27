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

    [Theory]
    [InlineData(1)]
    [InlineData(32)]
    public void A_file_without_the_unconditional_table_gives_the_tuned_one(int cpus) =>
        Assert.Equal(["VICTORY_AMBIENT=0.8", "VICTORY_RELAY=2", "VICTORY_TEXTURE_NO_WAIT=1"],
            Tuning.Parse(CombinedBase, cpus).Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal));

    // Shortened from AnyXboxX's settings.rs at f9b8d43: larger worker pools by the number of logical CPUs.
    private const string Pools = """
        pub const TUNED: &[(&str, &str)] = &[
            ("VICTORY_RELAY", "2"),
            ("VICTORY_THIN_THREADS", "8"),
            ("VICTORY_UNDER_THREADS", "6"),
            ("VICTORY_LAND_THREADS", "4"),
        ];

        /// Logical CPUs from which the three worker pools below replace the 8-core `TUNED` sizes.
        pub const BIG_CPU_THRESHOLD: usize = 16;

        const BIG_POOLS: &[(&str, &str)] = &[
            ("VICTORY_THIN_THREADS", "12"),
            ("VICTORY_UNDER_THREADS", "8"),
            ("VICTORY_LAND_THREADS", "6"),
        ];

        pub const HUGE_CPU_THRESHOLD: usize = 24;

        /// [`BIG_POOLS`] with 8 landing threads.
        const HUGE_POOLS: &[(&str, &str)] = &[
            ("VICTORY_THIN_THREADS", "12"),
            ("VICTORY_UNDER_THREADS", "8"),
            ("VICTORY_LAND_THREADS", "8"),
        ];

        pub fn pool_sizes(logical_cpus: usize) -> Option<&'static [(&'static str, &'static str)]> {
            if logical_cpus >= HUGE_CPU_THRESHOLD {
                Some(HUGE_POOLS)
            } else {
                (logical_cpus >= BIG_CPU_THRESHOLD).then_some(BIG_POOLS)
            }
        }

        pub const LOGICAL_CPUS_VAR: &str = "ANYXBOXX_LOGICAL_CPUS";

        pub fn logical_cpus() -> usize {
            1
        }
        """;

    private static (string Thin, string Under, string Land) Threads(IReadOnlyDictionary<string, string> settings) =>
        (settings["VICTORY_THIN_THREADS"], settings["VICTORY_UNDER_THREADS"], settings["VICTORY_LAND_THREADS"]);

    /// <summary>The source with one place changed. The place has to be there, or a test would check nothing.</summary>
    private static string Changed(string source, string old, string replacement)
    {
        Assert.Contains(old, source);
        return source.Replace(old, replacement);
    }

    [Theory]
    [InlineData(1, "8", "6", "4")]
    [InlineData(8, "8", "6", "4")]
    [InlineData(15, "8", "6", "4")]
    [InlineData(16, "12", "8", "6")]
    [InlineData(23, "12", "8", "6")]
    [InlineData(24, "12", "8", "8")]
    [InlineData(32, "12", "8", "8")]
    public void The_worker_pools_grow_with_the_logical_cpus(int cpus, string thin, string under, string land)
    {
        var settings = Tuning.Parse(Pools, cpus);

        Assert.Equal((thin, under, land), Threads(settings));
        // the pools replace nothing but their own entries
        Assert.Equal("2", settings["VICTORY_RELAY"]);
        Assert.Equal(4, settings.Count);
    }

    [Theory]
    [InlineData(39, "12", "8", "6")]
    [InlineData(40, "12", "8", "8")]
    [InlineData(9, "8", "6", "4")]
    [InlineData(10, "12", "8", "6")]
    public void The_thresholds_are_the_ones_in_the_file(int cpus, string thin, string under, string land)
    {
        var source = Changed(Changed(Pools, "BIG_CPU_THRESHOLD: usize = 16;", "BIG_CPU_THRESHOLD: usize = 10;"),
            "HUGE_CPU_THRESHOLD: usize = 24;", "HUGE_CPU_THRESHOLD: usize = 4_0;");

        Assert.Equal((thin, under, land), Threads(Tuning.Parse(source, cpus)));
    }

    // AnyXboxX f99cbee, the first version with pools, had only the BIG ones.
    [Theory]
    [InlineData(8, "8", "6", "4")]
    [InlineData(16, "12", "8", "6")]
    [InlineData(32, "12", "8", "6")]
    public void A_file_with_only_the_big_pools_keeps_them_on_every_larger_cpu(int cpus, string thin, string under, string land)
    {
        var start = Pools.IndexOf("pub const HUGE_CPU_THRESHOLD", StringComparison.Ordinal);
        var end = Pools.IndexOf("pub fn pool_sizes", StringComparison.Ordinal);

        Assert.Equal((thin, under, land), Threads(Tuning.Parse(Pools.Remove(start, end - start), cpus)));
    }

    // Each of these is refused on a small machine too: a change must not go unnoticed there.
    [Theory]
    // a table without its threshold, a threshold without its table
    [InlineData("pub const BIG_CPU_THRESHOLD: usize = 16;", "")]
    [InlineData("pub const HUGE_CPU_THRESHOLD: usize = 24;", "")]
    [InlineData("const BIG_POOLS:", "const OTHER:")]
    [InlineData("const HUGE_POOLS:", "const OTHER:")]
    // a threshold the launcher cannot read
    [InlineData("usize = 16;", "usize = 2 * 8;")]
    [InlineData("HUGE_CPU_THRESHOLD: usize = 24;", "HUGE_CPU_THRESHOLD: u32 = 24;")]
    [InlineData("usize = 16;", "usize = 99999999999;")]
    // tables the launcher cannot read completely
    [InlineData("(\"VICTORY_LAND_THREADS\", \"6\"),", "(\"VICTORY_LAND_THREADS\", DEFAULT_LAND),")]
    [InlineData("const HUGE_POOLS: &[(&str, &str)] = &[", "const HUGE_POOLS: [(&str, &str); 3] = [")]
    // a further tier must not be dropped without a word
    [InlineData("pub fn pool_sizes", "const GIANT_POOLS: &[(&str, &str)] = &[(\"VICTORY_LAND_THREADS\", \"10\")];\npub fn pool_sizes")]
    [InlineData("pub fn pool_sizes", "pub const GIANT_CPU_THRESHOLD: usize = 32;\npub fn pool_sizes")]
    public void Refuses_pools_it_cannot_read_completely(string old, string replacement) =>
        Assert.Throws<InvalidDataException>(() => Tuning.Parse(Changed(Pools, old, replacement), 8));

    // The CPU dependent sizes are there under names this launcher does not know.
    [Theory]
    [InlineData("pub fn pool_sizes(cpus: usize) {}")]
    [InlineData("pub fn logical_cpus() -> usize { 1 }")]
    public void Refuses_a_file_that_asks_for_the_cpus_without_the_known_pools(string function) =>
        Assert.Throws<InvalidDataException>(() => Tuning.Parse(Tuned + "\n" + function, 8));

    [Theory]
    [InlineData("24", 24)]
    [InlineData(" 32 \r\n", 32)]
    [InlineData("+16", 16)] // Rust reads that as a number too
    [InlineData("99999999999", int.MaxValue)] // and this one, as a very large CPU
    [InlineData(null, 12)]
    [InlineData("", 12)]
    [InlineData("0", 12)]
    [InlineData("-3", 12)]
    [InlineData("2.5", 12)]
    [InlineData("1 000", 12)]
    [InlineData("many", 12)]
    public void A_positive_number_in_the_variable_overrides_the_cpu_count(string? value, int expected) =>
        Assert.Equal(expected,
            Tuning.LogicalCpus(name => name == "ANYXBOXX_LOGICAL_CPUS" ? value : null, 12));

    [Fact]
    public void An_unknown_cpu_count_is_one() => Assert.Equal(1, Tuning.LogicalCpus(_ => null, 0));
}
