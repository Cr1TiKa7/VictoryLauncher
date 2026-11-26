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

    /// <summary>False: saves stay in the runtime's standard folder.</summary>
    public bool OwnDataDir { get; init; } = true;

    /// <summary>Where the fingerprint for the warmup hint looks; null: the usual folders of the layout.</summary>
    public IReadOnlyList<string>? SourcePaths { get; init; }

    /// <summary>A note shown with the fork.</summary>
    public string Note { get; init; } = "";

    public bool IsCustom { get; init; }
}

/// <summary>
/// The built in forks and the profile for an own URL. Their settings are a snapshot of 2026-11-26, taken from each
/// fork's start script or README and fitted to a graphics card with 10 GB.
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
            WarmsEveryGameFile = true, OwnDataDir = false,
            Note = "Settings from crates\\anyxboxx\\src\\run\\settings.rs (the values of \"anyxboxx run\"), " +
                   "they follow the updates.",
        },
        new()
        {
            Id = "klbf-lab", Name = "klbf-lab (PastiiJay)", Url = "https://github.com/PastiiJay/klbf-lab",
            Settings = Table("LATE_WAIT=1", "WIDE_BACK=1", "PIPELINE_WAIT_MS=24", "RELAY_COPY_QUEUE=0"),
            Note = "Tuned for AMD. Without the Series S profile, which would need AMD's FidelityFX SDK. " +
                   "The start screen and the menu may stay black, then press A on the controller.",
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
        },
        new()
        {
            Id = "vitoriabr", Name = "VItoriaBr (uiliannn)", Url = "https://github.com/uiliannn/VItoriaBr",
            Settings = Table(
                "RELAY=0", "FILL_WAIT=1", "CARD_BUFFERS_MB=1024", "TEXTURE_BUDGET_MB=5120", "PAGE_WRITES=slots",
                "AMBIENT=0.6", "TEXTURE_NO_WAIT=1", "WIDE_REFRESH=1", "WIDE_MERGE=1", "JOIN_COPIES=1",
                "DISPLAY_MODE=1920x1080"),
            Note = "The author's preset for 8 GB cards, the one for 16 GB is too large for 10 GB.",
        },
        new()
        {
            Id = "victory-fork", Name = "victory-fork (fxceless)", Url = "https://github.com/fxceless/victory-fork",
            Settings = Table("TEXTURE_BACK=1"),
            Note = "According to the author, measured on an RTX 4070 with 12 GB.",
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
