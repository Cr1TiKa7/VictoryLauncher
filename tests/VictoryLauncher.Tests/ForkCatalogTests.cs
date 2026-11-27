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
    [InlineData("dexnom", "https://github.com/DexNom/VIctory")]
    [InlineData("victory-nvidia", "https://github.com/trustno1-coder/VIctory-NVIDIA")]
    public void Has_the_chosen_forks_built_in(string id, string url) =>
        Assert.Equal(url, Assert.Single(ForkCatalog.BuiltIn, f => f.Id == id).Url);

    [Fact]
    public void Ids_are_unique_and_safe_as_folder_names()
    {
        Assert.Equal(9, ForkCatalog.BuiltIn.Count);
        Assert.Equal(9, ForkCatalog.BuiltIn.Select(f => f.Id).Distinct().Count());
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
    public void Dexnom_fetches_its_format_tables_before_cargo_and_starts_the_way_its_author_does_on_nvidia()
    {
        var fork = ForkCatalog.Find("dexnom", []);

        // the tables are not in the repository, cargo cannot build without them
        Assert.Equal(["powershell", "cargo"], fork.Build.Select(s => s.Tool));
        Assert.Equal(["-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", "Initialize-FormatTables.ps1"],
            fork.Build[0].Arguments);
        Assert.Equal(["build", "--release", "--locked", "-p", "victory", "--features", "diagnostics", "--bins"],
            fork.Build[1].Arguments);
        // `--no-rt` as a variable, the two variables from the README, and the one its own starter sets
        Assert.Equal(
            ["VICTORY_AMBIENT_FALLBACK=1", "VICTORY_DEBUG_HUD=1", "VICTORY_FSR_4K=0", "VICTORY_RAY_TRACING=0"],
            fork.Settings.Select(pair => $"{pair.Key}={pair.Value}").Order(StringComparer.Ordinal));
        Assert.Contains("untested", fork.Note);
        Assert.Contains("Marvisak/VIctory", fork.Note);
    }

    [Fact]
    public void The_nvidia_fork_gets_the_settings_of_its_own_start_without_dlss_and_without_recording()
    {
        var fork = ForkCatalog.Find("victory-nvidia", []);

        Assert.Equal(
            [
                "AMBIENT=0.8", "AMBIENT_AIR=1.5", "AMBIENT_SPECULAR=1", "CARD_AWAKE=1", "CARD_BUFFERS_MIN_KB=64",
                "CARD_REHASH_RUNS=1", "CARD_STEPS=2", "EARLY_DEPTH=1", "FRAME_INTERVAL_US=1000", "HEAP_SYNC_AT_SUBMIT=1",
                "HEAP_SYNC_FIRST=1", "INDIRECT_BATCH=2", "INDIRECT_WINDOW=1", "JOIN_COPIES=1", "JOIN_PARTS=8",
                "LEAVE_OUT=02384a60fda6f2d0,731a39171b568298", "LEVELS_ON_DEMAND=0", "MAP_THREADS=1", "POOL_SMALL=0",
                "PRELUDE_UPLOADS=1", "QUEUE_HOST_TEXTURES=0", "RELAY=2", "RELAY_COPY=1", "RIPE_BLOCKS=0", "SHOW_AFTER=1",
                "SHOW_NEW_LATER=1", "SOFT_VIEWS=copies", "SPANS_QUICK=1", "SYNC_FINE=1", "SYNC_IN_PLACE=1",
                "TABLED_REREAD=1", "TEXTURE_CATCHUP=0", "TEXTURE_NO_WAIT=1", "TEXTURE_POOL=1", "TEXTURE_STAGE_MS=16",
                "THIN=1", "UPLOAD_AHEAD=1", "WC_NO_BACK=1", "WC_NO_BACK_MIN_KB=1024", "WIDE_BACK=1",
            ],
            fork.Settings.Select(pair => $"{pair.Key["VICTORY_".Length..]}={pair.Value}").Order(StringComparer.Ordinal));
        Assert.Equal(["VICTORY_NATIVE_1440P=1"], fork.SeriesXSettings.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.Contains("DLSS", fork.Note);
        Assert.Contains("cannot save", fork.Note);
        Assert.Same(fork, Assert.Single(ForkCatalog.BuiltIn, f => f.SeriesXSettings.Count > 0));
    }

    [Fact]
    public void Only_anyxboxx_reads_its_settings_from_the_checkout_and_shares_the_standard_save_folder()
    {
        var shared = Assert.Single(ForkCatalog.BuiltIn, f => !f.OwnDataDir);
        Assert.Equal("anyxboxx", shared.Id);
        Assert.Equal(@"crates\anyxboxx\src\run\settings.rs",
            Assert.Single(ForkCatalog.BuiltIn, f => f.TuningFile is not null).TuningFile);
        Assert.Equal("third_party\\victory", shared.RuntimeDir);
        // its settings are the ones of its table, the launcher only adds the pipeline record
        Assert.Equal("VICTORY_PIPELINE_BOOK", Assert.Single(shared.Settings).Key);
        // its shaders are not all in the shader pack
        Assert.Same(shared, Assert.Single(ForkCatalog.BuiltIn, f => f.WarmsEveryGameFile));
    }

    [Fact]
    public void Settings_never_name_the_folders_the_launcher_sets() =>
        Assert.All(ForkCatalog.BuiltIn.SelectMany(f => f.Settings.Keys.Concat(f.SeriesXSettings.Keys)), name =>
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
        Assert.False(fork.WarmsEveryGameFile);
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

        Assert.Equal(10, all.Count);
        Assert.Equal("anyxboxx", all[0].Id);
        Assert.True(all[^1].IsCustom);
    }

    [Fact]
    public void An_unknown_choice_falls_back_to_anyxboxx() =>
        Assert.Equal("anyxboxx", ForkCatalog.Find("removed-fork", []).Id);
}
