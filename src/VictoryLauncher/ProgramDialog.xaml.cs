using System.Windows;
using System.Windows.Controls;
using VictoryLauncher.Core;

namespace VictoryLauncher;

public enum ProgramChoice { Abort, StartAnyway, CloseSelected }

/// <summary>Lists the programs worth closing. Nothing is preselected.</summary>
public partial class ProgramDialog : Window
{
    public sealed class Row(ProgramCandidate program)
    {
        public ProgramCandidate Program { get; } = program;
        public string Name => Program.Name;
        public string Reason => Program.Reason;
        public string Path => Program.Path;
        public bool IsSelected { get; set; }
    }

    private readonly List<Row> _rows;

    public ProgramDialog(IReadOnlyList<ProgramCandidate> programs)
    {
        InitializeComponent();
        _rows = [.. programs.Select(p => new Row(p))];
        ProgramList.ItemsSource = _rows;
    }

    public ProgramChoice Choice { get; private set; } = ProgramChoice.Abort;

    public IReadOnlyList<ProgramCandidate> Selected => [.. _rows.Where(r => r.IsSelected).Select(r => r.Program)];

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        TitleBar.Evening(this);
    }

    // Checked and Unchecked, not Click: a screen reader ticks a box without clicking it.
    private void Program_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: Row row } box)
            row.IsSelected = box.IsChecked == true;
        CloseSelectedButton.IsEnabled = _rows.Any(r => r.IsSelected);
    }

    private void CloseSelected_Click(object sender, RoutedEventArgs e) => Finish(ProgramChoice.CloseSelected);

    private void Start_Click(object sender, RoutedEventArgs e) => Finish(ProgramChoice.StartAnyway);

    private void Finish(ProgramChoice choice)
    {
        Choice = choice;
        DialogResult = true;
    }
}
