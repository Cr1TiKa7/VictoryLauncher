using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class WarmReaderTests
{
    // DexNom 64bd58f, apps/victory/src/translate.rs
    private const string DexNomResult =
        "warm         25700 translated, 5 refused; 2700 compute pipelines, 0 refused; 53 omitted by ray-tracing mode; 398 s";
    // VItoriaBr 1361e19, apps/victory/src/warm.rs
    private const string VitoriaResult =
        "containers   25889 distinct: 25685 translated now, 0 already cached, 204 do not translate (142.0 s)";

    [Fact]
    public void The_result_line_with_the_extra_field_counts()
    {
        var reader = new WarmProgress(25758, estimated: true, strict: false);
        reader.Feed(DexNomResult);
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, DexNomResult, 25705), reader.Verdict());
    }

    [Fact]
    public void A_warmup_whose_lines_mean_nothing_here_is_still_done()
    {
        var reader = new WarmProgress(25758, estimated: true, strict: false);
        reader.Feed(VitoriaResult);
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true), reader.Verdict());
    }

    [Fact]
    public void The_strict_reader_shows_from_the_start_and_wants_its_result_line()
    {
        var reader = new WarmProgress(25758);
        Assert.Equal(0, reader.Status!.Count);
        Assert.Equal(new WarmVerdict(false), reader.Verdict());
    }

    [Fact]
    public void An_estimate_is_called_one()
    {
        var reader = new WarmProgress(25758, estimated: true);
        reader.Feed("    63 s  2000 shaders");
        Assert.Equal("2,000 of about 25,758 (estimated) shaders · 8%", reader.Status!.Describe());
    }

    [Theory]
    [InlineData("    63 s  99999999999999999999 shaders")]
    [InlineData("warm         99999999999999999999 translated, 5 refused; 1 compute pipelines, 0 refused; 4 s")]
    public void A_number_too_large_to_read_is_ignored(string line)
    {
        var reader = new WarmProgress(10);
        reader.Feed(line);
        Assert.Equal(0, reader.Count);
        Assert.Null(reader.ResultLine);
    }
}

// Vice-Tay 79d78bc, apps/victory/src/translate.rs
public class ShaderCacheOutputTests
{
    private const string Counting = "1000 shaders in the cache, 14 s";
    private const string Done = "25685 shaders in the cache, 204 do not translate, 142 s";

    [Fact]
    public void The_first_time_it_only_counts()
    {
        var reader = new ShaderCacheOutput(null);
        Assert.Null(reader.Status);
        reader.Feed(Counting);
        Assert.Equal("1,000 shaders", reader.Status!.Describe());
        Assert.Null(reader.Status!.Percent);
    }

    [Fact]
    public void The_end_line_is_not_a_count_and_gives_the_total_to_remember()
    {
        var reader = new ShaderCacheOutput(null);
        reader.Feed(Done);
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, "25685 shaders in the cache, 204 do not translate", 25889), reader.Verdict());
    }

    [Fact]
    public void Later_it_shows_a_percentage_of_the_last_total()
    {
        var reader = new ShaderCacheOutput(2000);
        reader.Feed(Counting);
        Assert.Equal(50, reader.Status!.Percent);
        reader.Feed("3000 shaders in the cache, 40 s");
        Assert.Equal(99, reader.Status!.Percent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_last_total_that_makes_no_sense_is_left_out(int total)
    {
        var reader = new ShaderCacheOutput(total);
        reader.Feed(Counting);
        Assert.Null(reader.Status!.Percent);
    }

    [Fact]
    public void A_number_too_large_to_read_is_ignored()
    {
        var reader = new ShaderCacheOutput(null);
        reader.Feed("99999999999999999999 shaders in the cache, 1 s");
        Assert.Null(reader.Status);
    }
}

// Vice-Tay 79d78bc, apps/victory/src/warm.rs (the same in VIctory-7900XTX 2de9980)
public class KnownPipelinesOutputTests
{
    private const string NothingMade =
        "0 made in 0 s (0 have no host pipeline, 0 ended their process); ms each: median 0.0, p90 0.0, p99 0.0, most 0, sum 0 s";

    private static string Header(int known, int before, int toMake) =>
        $"{known} pipelines known, {before} made before (0123456789abcdef), 0 bad; {toMake} to make, 8 processes";

    [Fact]
    public void Shows_the_share_of_what_is_to_make()
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed(Header(84000, 0, 2000));
        Assert.Equal("0 of 2,000 pipelines · 0%", reader.Status!.Describe());
        reader.Feed("1000 of 2000, 35 s");
        Assert.Equal("1,000 of 2,000 pipelines · 50%", reader.Status!.Describe());
        // the last count can reach the total before the end line is there
        reader.Feed("2000 of 2000, 70 s");
        Assert.Equal(99, reader.Status!.Percent);
    }

    [Fact]
    public void The_end_line_sums_up()
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed(Header(84000, 0, 2000));
        reader.Feed("1999 made in 71 s (3 have no host pipeline, 1 ended their process); ms each: median 12.0, " +
                    "p90 40.1, p99 300.5, most 2100, sum 190 s");
        Assert.Equal(100, reader.Status!.Percent);
        Assert.Equal(new WarmVerdict(true, "1996 pipelines built, 3 have no host pipeline, 1 ended their process"),
            reader.Verdict());
    }

    [Theory]
    [InlineData(0, 0, "No pipelines recorded yet, so there was nothing to build.")]
    [InlineData(84000, 83990, "All 83990 recorded pipelines were built before, nothing new to build.")]
    [InlineData(500, 0, "None of the 500 recorded pipelines had to be built.")]
    public void Says_why_there_was_nothing_to_build(int known, int before, string summary)
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed(Header(known, before, 0));
        reader.Feed(NothingMade);
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true, summary), reader.Verdict());
    }

    [Fact]
    public void A_number_too_large_to_read_is_ignored()
    {
        var reader = new KnownPipelinesOutput();
        reader.Feed("99999999999999999999 of 2000, 35 s");
        Assert.Null(reader.Status);
        Assert.Equal(new WarmVerdict(true), reader.Verdict());
    }
}
