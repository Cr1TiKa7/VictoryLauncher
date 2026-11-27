namespace VictoryLauncher.Core;

/// <summary>
/// A size the game can be asked to put out, for a fork that offers the choice. The list is the fixed one of the
/// launcher of AnyXboxX (crates\anyxboxx-launcher\src\options.rs as of a445308), without what changes nothing:
/// the runtime resizes the title's output only to the title's own modes, so its 1600x900 and a size of one's own
/// are left out. Its 3840x2160 is the title's own here, which sets nothing.
/// </summary>
public sealed record OutputSize(string Id, string Label)
{
    public const string TitlesOwn = "title";

    private const string Default = "1920x1080";

    /// <summary>What a chosen size goes into. VICTORY_DISPLAY_MODE alone would not resize the title's output.</summary>
    internal static readonly string[] Variables = ["VICTORY_DISPLAY_MODE", "VICTORY_OUTPUT"];

    public static IReadOnlyList<OutputSize> Choices { get; } =
    [
        new("1280x720", "1280x720"),
        new(Default, Default + " (default)"),
        new("2560x1440", "2560x1440"),
        new(TitlesOwn, "3840x2160 (title's own)"),
    ];

    /// <returns>The choice with this id; the default for none or for one that is not offered.</returns>
    public static OutputSize Of(string? id) =>
        Choices.FirstOrDefault(choice => choice.Id == id) ?? Choices.First(choice => choice.Id == Default);
}
