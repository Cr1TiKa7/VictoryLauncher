using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace VictoryLauncher.Core;

/// <summary>One command of a fork's build, run in its runtime folder.</summary>
public sealed record BuildStep(string Tool, IReadOnlyList<string> Arguments)
{
    /// <summary>Skipped while the commit is the one last built and the exe is there.</summary>
    public bool OnlyAfterChanges { get; init; }
    public bool Cancellable { get; init; } = true;
}

/// <summary>What the launcher has to know about one fork.</summary>
public sealed record ForkProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Url { get; init; }

    /// <summary>The Cargo workspace inside the checkout, "" for its root. Null: look for it (own URLs).</summary>
    public string? RuntimeDir { get; init; } = "";

    public IReadOnlyList<BuildStep> Build { get; init; } = [ForkCatalog.CargoBuild];

    /// <summary>The program that runs the game, below the runtime folder.</summary>
    public string Exe { get; init; } = @"target\release\victory.exe";

    public bool RunsFromExeFolder { get; init; }

    /// <summary>
    /// Not all of the fork's shaders are in the shader pack: its warmup is given every archive and binary of the game.
    /// </summary>
    public bool WarmsEveryGameFile { get; init; }

    /// <summary>A file in the checkout with the VICTORY_* tables; read again after every update.</summary>
    public string? TuningFile { get; init; }

    /// <summary>VICTORY_* variables for warmup and game. They win over the tuning file.</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>VICTORY_* variables that only go to a Series X; as a Series S the program does not take them.</summary>
    public IReadOnlyDictionary<string, string> SeriesXSettings { get; init; } = new Dictionary<string, string>();

    /// <summary>False: saves stay in the runtime's standard folder.</summary>
    public bool OwnDataDir { get; init; } = true;

    /// <summary>Where the fingerprint for the warmup hint looks; null: the usual folders of the layout.</summary>
    public IReadOnlyList<string>? SourcePaths { get; init; }

    /// <summary>How its keyboard pad finds the letter keys.</summary>
    public KeyScheme Keys { get; init; } = KeyScheme.Standard;

    /// <summary>Its keyboard pad stays in use beside a connected controller.</summary>
    public bool KeysNextToController { get; init; }

    /// <summary>The window offers the size the game puts out, on a Series X.</summary>
    public bool ChoosesOutputSize { get; init; }

    /// <summary>A note shown with the fork.</summary>
    public string Note { get; init; } = "";

    /// <summary>What is known about this fork as a Series S, shown while that is chosen.</summary>
    public string SeriesSNote { get; init; } = "";

    public bool IsCustom { get; init; }
}

/// <summary>
/// The built in forks and the profile for an own URL. Their settings are a snapshot of 2026-11-26 (VIctory-NVIDIA:
/// 2026-11-27), taken from each fork's start script or README and fitted to a graphics card with 10 GB.
/// </summary>
public static partial class ForkCatalog
{
    public const string DefaultId = "anyxboxx";

    public static readonly BuildStep CargoBuild = new("cargo", ["build", "--release", "--locked", "-p", "victory"]);

    public static IReadOnlyList<ForkProfile> BuiltIn { get; } =
    [
        new()
        {
            Id = DefaultId, Name = "AnyXboxX (LooperSalty)", Url = "https://github.com/LooperSalty/AnyXboxX",
            RuntimeDir = @"third_party\victory", TuningFile = @"crates\anyxboxx\src\run\settings.rs",
            WarmsEveryGameFile = true, OwnDataDir = false, Keys = KeyScheme.ByPosition,
            // Its own launcher has this choice too. That one has no Series S, so a Series S is left as it was.
            ChoosesOutputSize = true,
            Note = "Settings from crates\\anyxboxx\\src\\run\\settings.rs (the values of \"anyxboxx run\"), " +
                   "they follow the updates.",
            SeriesSNote = "Its runtime README: about 5 ms a frame faster, a softer picture (measured on AMD). " +
                          "The launcher of AnyXboxX does not offer Series S, so no output size is set then.",
        },
        new()
        {
            Id = "klbf-lab", Name = "klbf-lab (PastiiJay)", Url = "https://github.com/PastiiJay/klbf-lab",
            Settings = Table("LATE_WAIT=1", "WIDE_BACK=1", "PIPELINE_WAIT_MS=24", "RELAY_COPY_QUEUE=0"),
            Note = "Tuned for AMD. The start screen and the menu may stay black, then press A on the controller.",
            SeriesSNote = "Its README recommends Series S, but that needs AMD's FidelityFX SDK in VICTORY_FFX_DIR. " +
                          "The launcher does not pass this variable on: according to the README the game then " +
                          "crashes once it starts upscaling.",
        },
        new()
        {
            Id = "vice-tay", Name = "Vice-Tay (TaynanGT)", Url = "https://github.com/TaynanGT/Vice-Tay",
            Settings = Table(
                "TEXTURE_NO_WAIT=1", "RELAY=2", "WIDE_BACK=1", "CARD_REHASH_RUNS=1", "JOIN_COPIES=1",
                "INDIRECT_WINDOW=1", "RELAY_COPY=1", "CARD_STEPS=2", "TITLE_PRIORITIES=0", "SHOW_AFTER=1",
                "AMBIENT=0.8", "D3D12_DRED=light", "SKIP_PIPELINES=none",
                "THIN_THREADS=12", "UNDER_THREADS=8", "LAND_THREADS=6", "MAP_THREADS=auto", "PLACED_ORDER=1",
                "WAVE_WIDE_ONLY=6f225b641c5b0708,5dc3b25986622949,369cbe906ea72764,2cc4c197ee29e291",
                "THIN=1", "JOIN_PARTS=8", "UPLOAD_AHEAD=1", "INDIRECT_BATCH=2", "FULLSCREEN=1"),
            Note = "Settings from its play.ps1 for NVIDIA (measured on an RTX 4090 with 16 cores). " +
                   "Full screen, F11 switches to a window. The author expects plenty of RAM.",
            SeriesSNote = "Its author plays all sessions on Series S. Its README: a smaller picture, lighter on " +
                          "the card, faster.",
        },
        new()
        {
            Id = "victory-7900xtx", Name = "VIctory-7900XTX (avatarmanz)",
            Url = "https://github.com/avatarmanz/VIctory-7900XTX",
            Settings = Table(
                "TEXTURE_NO_WAIT=1", "RELAY=2", "WIDE_BACK=1", "CARD_REHASH_RUNS=1", "JOIN_COPIES=1",
                "INDIRECT_WINDOW=1", "RELAY_COPY=1", "CARD_STEPS=2", "INDIRECT_BATCH=2", "MAP_THREADS=1",
                "TITLE_PRIORITIES=0", "CARD_AWAKE=1", "SHOW_AFTER=1", "AMBIENT=0.8", "D3D12_DRED=light",
                "THIN=1", "JOIN_PARTS=8", "UPLOAD_AHEAD=1"),
            Note = "Tuned for an AMD RX 7900 XTX. The author has not measured it on NVIDIA.",
            SeriesSNote = "Its README calls Series S about 5 ms a frame faster, but one of its tables shows it " +
                          "slower than Series X with 1080p output (measured on AMD).",
        },
        new()
        {
            Id = "kelvaroq", Name = "kelvaroq (fablinoo)", Url = "https://github.com/fablinoo/kelvaroq",
            Build =
            [
                new("cargo", ["build", "--release", "--locked"]),
                new("python", ["scripts/build_native.py"]) { OnlyAfterChanges = true, Cancellable = false },
            ],
            Exe = @"target\release\native\kelvaroq-engine.exe", RunsFromExeFolder = true,
            SourcePaths = ["src", "native", "scripts", "build.rs", "Cargo.toml", "Cargo.lock"],
            Settings = Table("TEXTURE_STAGE_MB=128", "CARD_BUFFERS_MB=512", "QUEUE2_LANDING_MB=64", "WIDE_THREADS=8"),
            Note = "Its own layout: also needs Python 3.11+ and the Windows SDK. The second build step " +
                   "downloads a fixed state of fxceless/victory-fork, applies patches and temporarily maps " +
                   "a drive for that (subst). It cannot be cancelled.",
            SeriesSNote = "Its README names the switch, but says nothing about how well Series S works.",
        },
        new()
        {
            Id = "vitoriabr", Name = "VItoriaBr (uiliannn)", Url = "https://github.com/uiliannn/VItoriaBr",
            Settings = Table(
                "RELAY=0", "FILL_WAIT=1", "CARD_BUFFERS_MB=1024", "TEXTURE_BUDGET_MB=5120", "PAGE_WRITES=slots",
                "AMBIENT=0.6", "TEXTURE_NO_WAIT=1", "WIDE_REFRESH=1", "WIDE_MERGE=1", "JOIN_COPIES=1",
                "DISPLAY_MODE=1920x1080"),
            Note = "The author's preset for 8 GB cards, the one for 16 GB is too large for 10 GB.",
            SeriesSNote = "The fork knows the switch, its README says nothing about Series S.",
        },
        new()
        {
            Id = "victory-fork", Name = "victory-fork (fxceless)", Url = "https://github.com/fxceless/victory-fork",
            Settings = Table("TEXTURE_BACK=1"),
            Note = "According to the author, measured on an RTX 4070 with 12 GB.",
            SeriesSNote = "A tester of this fork got a black picture in the world with Series S " +
                          "(run 304 in its diagnostics).",
        },
        new()
        {
            Id = "dexnom", Name = "VIctory (DexNom)", Url = "https://github.com/DexNom/VIctory",
            Build =
            [
                // the fork's own script; RemoteSigned runs a file from a checkout also where scripts are off
                new("powershell",
                    ["-NoProfile", "-ExecutionPolicy", "RemoteSigned", "-File", "Initialize-FormatTables.ps1"]),
                new("cargo",
                    ["build", "--release", "--locked", "-p", "victory", "--features", "diagnostics", "--bins"]),
            ],
            // its README's experimental NVIDIA launch (`--no-rt` as a variable), and DEBUG_HUD like its own starter
            Settings = Table("RAY_TRACING=0", "FSR_4K=0", "AMBIENT_FALLBACK=1", "DEBUG_HUD=1"),
            Note = "Its experimental NVIDIA mode, ray tracing and FSR off. Developed for an AMD Radeon with 24 GB, " +
                   "NVIDIA gameplay is untested by the author. The build downloads format tables from " +
                   "Marvisak/VIctory and checks their SHA-256.",
            SeriesSNote = "The fork knows the switch, but the starters of its author all use Series X.",
        },
        new()
        {
            Id = "victory-nvidia", Name = "VIctory-NVIDIA (trustno1-coder)",
            Url = "https://github.com/trustno1-coder/VIctory-NVIDIA", KeysNextToController = true,
            // apps\victory\src\play.rs as of c437b05: its table and "no frame rate cap". Left out: DLSS and frame
            // generation (they need NVIDIA's files) and the record of the pipelines.
            Settings = Table(
                "THIN=1", "JOIN_PARTS=8", "RELAY=2", "RELAY_COPY=1", "WIDE_BACK=1", "CARD_REHASH_RUNS=1",
                "JOIN_COPIES=1", "CARD_STEPS=2", "CARD_AWAKE=1", "CARD_BUFFERS_MIN_KB=64", "INDIRECT_WINDOW=1",
                "INDIRECT_BATCH=2", "UPLOAD_AHEAD=1", "SHOW_AFTER=1", "MAP_THREADS=1", "WC_NO_BACK=1",
                "WC_NO_BACK_MIN_KB=1024", "SOFT_VIEWS=copies", "TEXTURE_POOL=1", "RIPE_BLOCKS=0", "TEXTURE_NO_WAIT=1",
                "TEXTURE_CATCHUP=0", "TEXTURE_STAGE_MS=16", "LEVELS_ON_DEMAND=0", "PRELUDE_UPLOADS=1",
                "SHOW_NEW_LATER=1", "POOL_SMALL=0", "TABLED_REREAD=1", "QUEUE_HOST_TEXTURES=0", "HEAP_SYNC_FIRST=1",
                "SYNC_IN_PLACE=1", "SPANS_QUICK=1", "SYNC_FINE=1", "HEAP_SYNC_AT_SUBMIT=1", "AMBIENT=0.8",
                "AMBIENT_AIR=1.5", "AMBIENT_SPECULAR=1", "EARLY_DEPTH=1",
                "LEAVE_OUT=02384a60fda6f2d0,731a39171b568298", "FRAME_INTERVAL_US=1000"),
            SeriesXSettings = Table("NATIVE_1440P=1"),
            Note = "Settings of its own start for NVIDIA RTX: 2560x1440, no frame rate cap. DLSS and frame generation " +
                   "are off, the launcher does not download NVIDIA's files. Its author measured with DLSS on an " +
                   "RTX 5070 with 12 GB. The F1 menu opens, but cannot save here.",
            SeriesSNote = "Its README: 1280x720 inside, faster and softer. The 2560x1440 setting is left out then, " +
                          "the program only takes it for a Series X.",
        },
    ];

    /// <param name="url">An address accepted by <see cref="GitUrl.Parse"/>.</param>
    public static ForkProfile Custom(string url)
    {
        var parts = new Uri(url).AbsolutePath.Trim('/').Split('/');
        var (owner, repo) = (parts[0], parts[^1]);

        var readable = Unsafe().Replace($"{owner}-{repo}".ToLowerInvariant(), "-").Trim('-');
        // the hash keeps look-alike names apart, the cut keeps paths short
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url.ToLowerInvariant())))[..8];
        if (readable.Length > 40)
            readable = readable[..40].TrimEnd('-');

        return new()
        {
            Id = $"custom-{readable}-{hash.ToLowerInvariant()}",
            Name = $"{repo} ({owner})",
            Url = url,
            RuntimeDir = null,
            IsCustom = true,
            Note = "Own URL: no special settings, the defaults of the runtime apply.",
            SeriesSNote = "Not checked for an own Git URL. A program that does not know the switch will not start.",
        };
    }

    public static IReadOnlyList<ForkProfile> All(IEnumerable<string> customUrls) => [.. BuiltIn, .. customUrls.Select(Custom)];

    public static ForkProfile Find(string? id, IEnumerable<string> customUrls)
    {
        var all = All(customUrls);
        return all.FirstOrDefault(f => f.Id == id) ?? all[0];
    }

    private static Dictionary<string, string> Table(params string[] entries) =>
        entries.Select(entry => entry.Split('=', 2)).ToDictionary(pair => "VICTORY_" + pair[0], pair => pair[1]);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Unsafe();
}

/// <summary>Addresses of repositories whose code the launcher will run.</summary>
public static class GitUrl
{
    private const string Example = "https://github.com/name/project";

    /// <returns>The address in its stored form, or the reason why it is refused.</returns>
    public static (string? Url, string? Error) Parse(string? text)
    {
        var typed = (text ?? "").Trim();
        if (typed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))
            || !typed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(typed, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
            return (null, $"Please enter an https address, for example {Example}");
        if (uri.UserInfo.Length > 0)
            return (null, "The address must not contain credentials.");
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return (null, $"Please enter only the address of the repository, for example {Example}");

        var path = Trimmed(uri.AbsolutePath);
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var github = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);
        if (parts.Length < 2 || (github && parts.Length != 2))
            return (null, $"Please enter the address of the repository, for example {Example}");

        return ($"https://{uri.Authority}/{string.Join('/', parts)}", null);
    }

    public static bool Same(string first, string second) =>
        string.Equals(Trimmed(first.Trim()), Trimmed(second.Trim()), StringComparison.OrdinalIgnoreCase);

    private static string Trimmed(string address)
    {
        address = address.TrimEnd('/', '\\');
        return address.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? address[..^4] : address;
    }
}
