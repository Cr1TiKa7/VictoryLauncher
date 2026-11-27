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

        Assert.Equal("Y, Left Shift or right mouse button", KeysFor(rows, "LT"));
        Assert.Equal("Z", KeysFor(rows, "Y"));
        Assert.Equal("C, Space or left mouse button", KeysFor(rows, "RT"));
    }

    [Fact]
    public void By_position_the_sticks_follow_a_french_keyboard()
    {
        var rows = KeyBindings.Rows(KeyScheme.ByPosition, French);

        Assert.Equal("Z Q S D", KeysFor(rows, "Left stick"));
        Assert.Equal("A", KeysFor(rows, "LB"));
    }

    [Fact]
    public void By_position_only_the_right_stick_and_the_triggers_have_more_than_the_standard_keys()
    {
        var standard = KeyBindings.Rows(KeyScheme.Standard, Qwerty);
        var byPosition = KeyBindings.Rows(KeyScheme.ByPosition, Qwerty);

        Assert.Equal(standard.Select(row => row.Pad), byPosition.Select(row => row.Pad));
        Assert.Equal(["Right stick", "LT", "RT"], byPosition.Except(standard).Select(row => row.Pad));
        Assert.Equal("I J K L or mouse", KeysFor(byPosition, "Right stick"));
    }

    [Fact]
    public void Space_and_left_shift_are_not_looked_up_in_the_keyboard_layout()
    {
        var rows = KeyBindings.Rows(KeyScheme.ByPosition, _ => "?");

        Assert.Equal("?, Left Shift or right mouse button", KeysFor(rows, "LT"));
        Assert.Equal("?, Space or left mouse button", KeysFor(rows, "RT"));
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
    public void The_nvidia_fork_takes_the_keys_next_to_a_controller()
    {
        var notes = NotesOf("victory-nvidia");

        Assert.Contains("also while a controller is connected", notes);
        Assert.DoesNotContain("no controller", notes);
        Assert.Contains("F1", notes);
        Assert.Contains("F3", notes);
        Assert.Contains("no controller is connected", NotesOf("klbf-lab"));
    }

    [Fact]
    public void Dexnom_names_the_dualsense_too()
    {
        Assert.Contains("DualSense", NotesOf("dexnom"));
        Assert.DoesNotContain("DualSense", NotesOf("vitoriabr"));
    }

    [Theory]
    [InlineData("anyxboxx")]
    [InlineData("vice-tay")]
    public void The_forks_with_full_screen_keys_name_them(string id)
    {
        Assert.Contains("F11 or Alt+Enter", NotesOf(id));
        Assert.DoesNotContain("F11", NotesOf("victory-7900xtx"));
    }

    [Fact]
    public void Anyxboxx_says_that_keys_go_by_place()
    {
        Assert.Contains("place on the keyboard", NotesOf("anyxboxx"));
        Assert.DoesNotContain("place on the keyboard", NotesOf("vice-tay"));
    }

    [Fact]
    public void Anyxboxx_explains_how_to_take_and_give_back_the_mouse()
    {
        var notes = NotesOf("anyxboxx");

        Assert.Contains("click into the game's picture or press F9", notes);
        Assert.Contains("That click does not fire", notes);
        Assert.Contains("F9 again or leaving the window gives it back", notes);
        Assert.DoesNotContain("F9", NotesOf("dexnom"));
    }

    [Fact]
    public void Only_anyxboxx_takes_the_keyboard_beside_a_connected_controller()
    {
        Assert.Contains("also while a controller is connected", NotesOf("anyxboxx"));
        Assert.DoesNotContain("no controller is connected", NotesOf("anyxboxx"));
        Assert.Contains("no controller is connected", NotesOf("vice-tay"));
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
