using System.Windows;
using VictoryLauncher.Core;

namespace VictoryLauncher;

/// <summary>Shows which key stands in for which input of the controller with one fork.</summary>
public partial class ControlsDialog : Window
{
    public ControlsDialog(ForkProfile fork)
    {
        InitializeComponent();
        ForkText.Text = fork.Name;
        // asked on the window's thread, which has the keyboard layout the user types with
        KeyList.ItemsSource = KeyBindings.Rows(fork.Keys, KeyboardPosition.Label);
        NoteList.ItemsSource = KeyBindings.Notes(fork);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBar.Evening(this);
    }
}
