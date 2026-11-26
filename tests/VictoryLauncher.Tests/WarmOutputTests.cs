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
