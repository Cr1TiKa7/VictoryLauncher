using VictoryLauncher.Core;

namespace VictoryLauncher.Tests;

public class KeyBindingsTests
{
    private static readonly Func<char, string> Qwerty = letter => letter.ToString();

    /// <summary>On a German keyboard the Y and Z keys are swapped.</summary>
    private static readonly Func<char, string> German = letter => letter switch
    {
        'Y' => "Z",
        'Z' => "Y",
        _ => letter.ToString(),
    };

    private static readonly Func<char, string> French = letter => letter switch
    {
        'Q' => "A",
        'W' => "Z",
        'A' => "Q",
        'Z' => "W",
        _ => letter.ToString(),
    };

    private static string KeysFor(IReadOnlyList<PadKey> rows, string pad) => rows.Single(row => row.Pad == pad).Keys;

    [Fact]
    public void The_standard_table_is_the_one_of_the_runtime()
    {
        Assert.Equal(
        [
            new("Left stick", "W A S D"),
            new("Right stick", "I J K L"),
            new("A", "Enter"),
            new("B", "Backspace"),
            new("X", "X"),
            new("Y", "Y"),
            new("LB", "Q"),
            new("RB", "E"),
            new("LT", "Z"),
            new("RT", "C"),
            new("Left stick click (L3)", "F"),
            new("Right stick click (R3)", "H"),
            new("D-pad", "Arrow keys"),
            new("Menu (Start)", "Esc"),
            new("View (Back)", "Tab"),
        ], KeyBindings.Rows(KeyScheme.Standard, Qwerty));
    }

    [Fact]
    public void The_standard_table_names_the_keys_by_their_letter_on_every_layout()
    {
        var rows = KeyBindings.Rows(KeyScheme.Standard, German);

        Assert.Equal("Z", KeysFor(rows, "LT"));
        Assert.Equal("Y", KeysFor(rows, "Y"));
    }

    [Fact]
    public void By_position_a_german_keyboard_swaps_the_y_button_and_the_left_trigger()
    {
        var rows = KeyBindings.Rows(KeyScheme.ByPosition, German);

        Assert.Equal("Y", KeysFor(rows, "LT"));
        Assert.Equal("Z", KeysFor(rows, "Y"));
        Assert.Equal("C", KeysFor(rows, "RT"));
    }

    [Fact]
    public void By_position_the_sticks_follow_a_french_keyboard()
    {
        var rows = KeyBindings.Rows(KeyScheme.ByPosition, French);

        Assert.Equal("Z Q S D", KeysFor(rows, "Left stick"));
        Assert.Equal("A", KeysFor(rows, "LB"));
    }

    [Fact]
    public void By_position_the_table_has_the_inputs_of_the_standard_one_and_no_others()
    {
        Assert.Equal(KeyBindings.Rows(KeyScheme.Standard, Qwerty), KeyBindings.Rows(KeyScheme.ByPosition, Qwerty));
    }

    [Fact]
    public void Only_anyxboxx_goes_by_position()
    {
        var byPosition = ForkCatalog.BuiltIn.Where(f => f.Keys == KeyScheme.ByPosition).Select(f => f.Id);

        Assert.Equal(["anyxboxx"], byPosition);
        Assert.Equal(KeyScheme.Standard, ForkCatalog.Custom("https://github.com/name/project").Keys);
    }

    private static string NotesOf(string id) => string.Join('\n', KeyBindings.Notes(ForkCatalog.Find(id, [])));

    [Fact]
    public void Every_fork_says_when_the_keyboard_counts()
    {
        Assert.All(ForkCatalog.BuiltIn, fork => Assert.Contains(KeyBindings.Notes(fork),
            note => note.Contains("game window") && note.Contains("controller")));
    }

    [Fact]
    public void Dexnom_names_the_dualsense_too()
    {
        Assert.Contains("DualSense", NotesOf("dexnom"));
        Assert.DoesNotContain("DualSense", NotesOf("vitoriabr"));
    }

    [Fact]
    public void Vice_tay_names_its_full_screen_keys()
    {
        Assert.Contains("F11", NotesOf("vice-tay"));
        Assert.DoesNotContain("F11", NotesOf("victory-7900xtx"));
    }

    [Fact]
    public void Anyxboxx_names_no_mouse_since_its_combined_base_and_says_that_keys_go_by_place()
    {
        Assert.Contains("place on the keyboard", NotesOf("anyxboxx"));
        Assert.DoesNotContain("ouse", NotesOf("anyxboxx"));
        Assert.DoesNotContain("F9", NotesOf("anyxboxx"));
    }

    [Fact]
    public void An_own_url_gets_the_standard_table_with_a_warning()
    {
        var custom = ForkCatalog.Custom("https://github.com/name/project");

        Assert.Contains(KeyBindings.Notes(custom), note => note.Contains("not checked for this fork"));
        Assert.DoesNotContain("not checked", NotesOf("klbf-lab"));
    }

    [Fact]
    public void A_position_is_named_after_the_key_that_lies_there()
    {
        // 0x2C is where QWERTY has Z; German Windows answers with the virtual key Y
        var label = KeyboardPosition.Label('Z', scan => scan == 0x2C ? 'Y' : 0u, _ => 0);

        Assert.Equal("Y", label);
    }

    [Fact]
    public void A_position_without_a_letter_shows_the_character_of_its_key()
    {
        // 0xBE is the full stop key, which Dvorak has where QWERTY has E
        var label = KeyboardPosition.Label('E', _ => 0xBE, key => key == 0xBE ? '.' : 0u);

        Assert.Equal(".", label);
    }

    [Fact]
    public void An_unknown_position_keeps_the_qwerty_letter()
    {
        Assert.Equal("W", KeyboardPosition.Label('W', _ => 0, _ => 0));
        Assert.Equal("W", KeyboardPosition.Label('W', _ => 0xBE, _ => 0));
    }

    [Theory]
    [InlineData('W', 0x11u)]
    [InlineData('A', 0x1Eu)]
    [InlineData('C', 0x2Eu)]
    [InlineData('Y', 0x15u)]
    [InlineData('L', 0x26u)]
    public void The_scan_codes_are_the_ones_of_the_runtime(char letter, uint scanCode)
    {
        var asked = 0u;
        KeyboardPosition.Label(letter, scan => { asked = scan; return 0; }, _ => 0);

        Assert.Equal(scanCode, asked);
    }
}
