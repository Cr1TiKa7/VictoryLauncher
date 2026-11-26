using System.Windows;
using VictoryLauncher.Core;

namespace VictoryLauncher;

public partial class App : Application
{
    private static readonly TimeSpan PredecessorLimit = TimeSpan.FromSeconds(30);

    protected override void OnStartup(StartupEventArgs e)
    {
        // After an update the old version is still ending. It still holds files this one is about to tidy up.
        AppUpdate.WaitForPredecessor(e.Args, PredecessorLimit);
        if (Environment.ProcessPath is { } exe)
            AppUpdate.CleanUp(exe);
        base.OnStartup(e);
    }
}
