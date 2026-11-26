using System.Runtime.InteropServices;

namespace VictoryLauncher.Core;

/// <summary>How a fork's runtime finds the letter keys of its keyboard pad.</summary>
public enum KeyScheme
{
    /// <summary>By their virtual key: the key that carries the letter.</summary>
    Standard,
    /// <summary>By their place on a QWERTY keyboard, whatever is printed there (AnyXboxX).</summary>
    ByPosition,
}

/// <summary>One input of the controller and what stands in for it.</summary>
public sealed record PadKey(string Pad, string Keys);

/// <summary>
/// The keyboard pad of the forks, as their `gameinput.rs` (and `keymap.rs` of AnyXboxX) had it on 2026-11-26. It is
/// written down here, so a fork that changes its keys is shown wrong until this table follows.
/// </summary>
public static class KeyBindings
{
    /// <param name="keyAt">What the key is called that lies where QWERTY has the letter.</param>
    public static IReadOnlyList<PadKey> Rows(KeyScheme scheme, Func<char, string> keyAt)
    {
        var byPosition = scheme == KeyScheme.ByPosition;
        string Letters(string qwerty) =>
            string.Join(' ', qwerty.Select(letter => byPosition ? keyAt(letter) : letter.ToString()));

        return
        [
            new("Left stick", Letters("WASD")),
            new("Right stick", Letters("IJKL")),
            new("A", "Enter"),
            new("B", "Backspace"),
            new("X", Letters("X")),
            new("Y", Letters("Y")),
            new("LB", Letters("Q")),
            new("RB", Letters("E")),
            new("LT", Letters("Z")),
            new("RT", Letters("C")),
            new("Left stick click (L3)", Letters("F")),
            new("Right stick click (R3)", Letters("H")),
            new("D-pad", "Arrow keys"),
            new("Menu (Start)", "Esc"),
            new("View (Back)", "Tab"),
        ];
    }

    /// <returns>What to know about the keys of a fork, one sentence or two each.</returns>
    public static IReadOnlyList<string> Notes(ForkProfile fork)
    {
        var pad = fork.Id == "dexnom" ? "controller (also no DualSense on a cable)" : "controller";
        var notes = new List<string>
        {
            $"The keys only count while the game window has the focus and no {pad} is connected.",
        };
        if (fork.Keys == KeyScheme.ByPosition)
        {
            notes.Add("The letters go by their place on the keyboard. They are named as your current keyboard layout " +
                      "has them.");
        }
        if (fork.Id == "vice-tay")
            notes.Add("F11 or Alt+Enter switches between full screen and a window.");
        if (fork.IsCustom)
            notes.Add("Own URL: this is the usual table of the VIctory runtime, not checked for this fork.");
        return notes;
    }
}

/// <summary>Names the key at a place of the keyboard the way AnyXboxX finds it.</summary>
public static class KeyboardPosition
{
    // Set 1 scan codes of the letter keys, by the QWERTY letter at that place.
    private static readonly Dictionary<char, uint> ScanCodes = new()
    {
        ['Q'] = 0x10, ['W'] = 0x11, ['E'] = 0x12, ['R'] = 0x13, ['T'] = 0x14, ['Y'] = 0x15, ['U'] = 0x16,
        ['I'] = 0x17, ['O'] = 0x18, ['P'] = 0x19, ['A'] = 0x1E, ['S'] = 0x1F, ['D'] = 0x20, ['F'] = 0x21,
        ['G'] = 0x22, ['H'] = 0x23, ['J'] = 0x24, ['K'] = 0x25, ['L'] = 0x26, ['Z'] = 0x2C, ['X'] = 0x2D,
        ['C'] = 0x2E, ['V'] = 0x2F, ['B'] = 0x30, ['N'] = 0x31, ['M'] = 0x32,
    };

    private const uint ScanCodeToVirtualKey = 1;
    private const uint VirtualKeyToCharacter = 2;

    /// <summary>Asks Windows about the keyboard layout of the calling thread.</summary>
    public static string Label(char letter) => Label(letter,
        scanCode => MapVirtualKey(scanCode, ScanCodeToVirtualKey),
        key => MapVirtualKey(key, VirtualKeyToCharacter));

    /// <param name="scanToKey">The virtual key at a scan code, 0 when unknown.</param>
    /// <param name="keyToChar">The character of a virtual key, 0 without one.</param>
    public static string Label(char letter, Func<uint, uint> scanToKey, Func<uint, uint> keyToChar)
    {
        var key = ScanCodes.TryGetValue(letter, out var scanCode) ? scanToKey(scanCode) : 0;
        if (key is >= 'A' and <= 'Z')
            return ((char)key).ToString();
        // the highest bit marks a dead key, the character is in the rest
        var character = key == 0 ? '\0' : (char)(keyToChar(key) & 0xFFFF);
        return char.IsControl(character) || char.IsWhiteSpace(character)
            ? letter.ToString()
            : char.ToUpperInvariant(character).ToString();
    }

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
