using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class ForkCatalogTests
{
    [Theory]
    [InlineData("anyxboxx", "https://github.com/LooperSalty/AnyXboxX")]
    [InlineData("klbf-lab", "https://github.com/PastiiJay/klbf-lab")]
    [InlineData("vice-tay", "https://github.com/TaynanGT/Vice-Tay")]
    [InlineData("victory-7900xtx", "https://github.com/avatarmanz/VIctory-7900XTX")]
    [InlineData("kelvaroq", "https://github.com/fablinoo/kelvaroq")]
    [InlineData("vitoriabr", "https://github.com/uiliannn/VItoriaBr")]
    [InlineData("victory-fork", "https://github.com/fxceless/victory-fork")]
    public void Has_the_chosen_forks_built_in(string id, string url) =>
        Assert.Equal(url, Assert.Single(ForkCatalog.BuiltIn, f => f.Id == id).Url);

    [Fact]
    public void Ids_are_unique_and_safe_as_folder_names()
    {
        Assert.Equal(7, ForkCatalog.BuiltIn.Count);
        Assert.Equal(7, ForkCatalog.BuiltIn.Select(f => f.Id).Distinct().Count());
        Assert.All(ForkCatalog.BuiltIn, f => Assert.Matches("^[a-z0-9-]+$", f.Id));
        Assert.All(ForkCatalog.BuiltIn, f => Assert.Equal(f.Url, GitUrl.Parse(f.Url).Url));
    }

    [Fact]
    public void Every_cargo_build_keeps_the_lock_file_as_it_is() =>
        // a rewritten Cargo.lock would be a tracked change and block the next update
        Assert.All(ForkCatalog.BuiltIn.Append(ForkCatalog.Custom("https://github.com/a/b")),
            f => Assert.Contains("--locked", f.Build.First(step => step.Tool == "cargo").Arguments));

    [Fact]
    public void Kelvaroq_builds_its_engine_in_a_second_step_that_cannot_be_cancelled()
    {
        var fork = ForkCatalog.Find("kelvaroq", []);

        Assert.Equal(["cargo", "python"], fork.Build.Select(s => s.Tool));
        Assert.Equal(["scripts/build_native.py"], fork.Build[1].Arguments);
        Assert.True(fork.Build[1].OnlyAfterChanges);
        // it maps a temporary drive letter and has to remove it itself
        Assert.False(fork.Build[1].Cancellable);
        Assert.Equal(@"target\release\native\kelvaroq-engine.exe", fork.Exe);
        Assert.Contains("subst", fork.Note);
    }

    [Fact]
    public void Only_anyxboxx_reads_its_settings_from_the_checkout_and_shares_the_standard_save_folder()
    {
        var shared = Assert.Single(ForkCatalog.BuiltIn, f => !f.OwnDataDir);
        Assert.Equal("anyxboxx", shared.Id);
        Assert.Equal(@"crates\anyxboxx\src\run\settings.rs",
            Assert.Single(ForkCatalog.BuiltIn, f => f.TuningFile is not null).TuningFile);
        Assert.Equal("third_party\\victory", shared.RuntimeDir);
        Assert.Equal("1024", shared.Settings["VICTORY_CARD_BUFFERS_MB"]);
        Assert.Equal(WarmKind.GameFiles, shared.Warm);
    }

    [Fact]
    public void Settings_never_name_the_folders_the_launcher_sets() =>
        Assert.All(ForkCatalog.BuiltIn.SelectMany(f => f.Settings.Keys), name =>
        {
            Assert.StartsWith("VICTORY_", name);
            Assert.DoesNotContain(name, new[] { "VICTORY_GAME_DIR", "VICTORY_CACHE_DIR", "VICTORY_DATA_DIR", "VICTORY_CONFIG" });
        });

    [Fact]
    public void An_own_url_gets_a_general_profile_with_a_stable_folder_name()
    {
        var fork = ForkCatalog.Custom("https://github.com/Some.One/My_VIctory");

        Assert.True(fork.IsCustom);
        Assert.Equal("My_VIctory (Some.One)", fork.Name);
        Assert.Matches("^custom-some-one-my-victory-[0-9a-f]{8}$", fork.Id);
        Assert.Equal(fork.Id, ForkCatalog.Custom("https://github.com/some.one/my_victory").Id);
        Assert.Null(fork.RuntimeDir);
        Assert.Null(fork.Warm);
        Assert.Empty(fork.Settings);
        Assert.True(fork.OwnDataDir);
    }

    [Fact]
    public void Names_that_look_alike_still_get_different_folders() =>
        Assert.NotEqual(ForkCatalog.Custom("https://github.com/a/b-c").Id, ForkCatalog.Custom("https://github.com/a-b/c").Id);

    [Fact]
    public void A_very_long_url_still_gives_a_short_folder_name() =>
        Assert.True(ForkCatalog.Custom($"https://github.com/{new string('a', 90)}/{new string('b', 90)}").Id.Length <= 60);

    [Fact]
    public void The_list_is_the_built_in_forks_then_the_own_ones()
    {
        var all = ForkCatalog.All(["https://github.com/me/mine"]);

        Assert.Equal(8, all.Count);
        Assert.Equal("anyxboxx", all[0].Id);
        Assert.True(all[^1].IsCustom);
    }

    [Fact]
    public void An_unknown_choice_falls_back_to_anyxboxx() =>
        Assert.Equal("anyxboxx", ForkCatalog.Find("removed-fork", []).Id);
}
