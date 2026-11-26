using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using VictoryLauncher.Core;

namespace VictoryLauncher;

/// <summary>The launcher's own updates.</summary>
public partial class MainWindow
{
    private static readonly Version OwnVersion = ReleaseVersion.Of(typeof(MainWindow).Assembly.GetName().Version!);
    private static readonly TimeSpan CheckLimit = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadLimit = TimeSpan.FromMinutes(10);
    private static readonly HttpClient Web = new() { Timeout = Timeout.InfiniteTimeSpan };

    // Inside a single file the assembly has no path of its own. That is what a release ships; anything else is made
    // of several files, and replacing one of them would not update it.
#pragma warning disable IL3000
    private static bool IsSingleFile => string.IsNullOrEmpty(typeof(MainWindow).Assembly.Location);
#pragma warning restore IL3000

    private static bool CanUpdate => OwnVersion != ReleaseVersion.Local && IsSingleFile;

    private static string WindowName =>
        OwnVersion == ReleaseVersion.Local ? "VictoryLauncher (Eigenbau)" : $"VictoryLauncher {OwnVersion}";

    private UpdateInfo? _update;
    private int _downloadPercent = -1;
    // Set once the new program is in place. It is started when this window has saved everything and closed.
    private (string Exe, string Old)? _successor;

    /// <summary>Runs next to whatever else happens on opening. Nothing here may get in the way.</summary>
    private async Task CheckForUpdate()
    {
        if (!CanUpdate)
            return;
        try
        {
            var (update, note) = await AppUpdate.CheckAsync(Web, OwnVersion, CheckLimit, CancellationToken.None);
            _log.Add(note);
            if (update is null)
                return;
            _update = update;
            UpdateText.Text = $"Version {update.Version} ist da, du hast {OwnVersion}.";
            UpdateBar.Visibility = Visibility.Visible;
        }
        catch (Exception e)
        {
            _log.Add("Suche nach Updates fehlgeschlagen: " + e.Message);
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_activity != Activity.Idle || _update is not { } update || Environment.ProcessPath is not { } exe)
            return;
        SetActivity(Activity.Updating);
        using var cancel = new CancellationTokenSource();
        using var limited = CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);
        limited.CancelAfter(DownloadLimit);
        _cancel = cancel;
        try
        {
            _log.Open(Path.Combine(Home, "logs", "selfupdate.log"));
            _log.Add($"Update von {OwnVersion} auf {update.Version}: {update.Download}");
            StatusText.Text = $"Lade Version {update.Version} …";
            WarmBar.Value = 0;
            AllowCancel(true);
            var token = limited.Token;
            // off the window's thread: hashing and unpacking would let it stutter
            string fresh;
            try
            {
                fresh = await Task.Run(() => AppUpdate.DownloadAsync(Web, update, exe,
                    (done, total) => Volatile.Write(ref _downloadPercent, (int)(done * 100 / total)), token));
            }
            finally
            {
                // before any message: the timer goes on behind it and would write the progress over the status
                Volatile.Write(ref _downloadPercent, -1);
            }
            AllowCancel(false);
            _successor = (exe, AppUpdate.Swap(fresh, exe));
            _log.Add($"Version {update.Version} liegt bereit, starte neu.");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = cancel.IsCancellationRequested
                ? "Update abgebrochen."
                : "Der Download hat zu lange gedauert, bitte später noch einmal versuchen.";
        }
        catch (Exception problem)
        {
            _log.Add(problem.ToString());
            StatusText.Text = "Update fehlgeschlagen: " + problem.Message;
            Warn("Das Update ist fehlgeschlagen, der Launcher bleibt, wie er ist.\n\n" + problem.Message);
        }
        finally
        {
            _cancel = null;
            AllowCancel(false);
            SetActivity(Activity.Idle);
        }
        if (_successor is not null || _closeWhenIdle)
            Close();
    }

    /// <summary>The last thing before the process ends.</summary>
    private void StartSuccessor()
    {
        if (_successor is not { } successor)
            return;
        try
        {
            var start = new ProcessStartInfo(successor.Exe) { WorkingDirectory = Path.GetDirectoryName(successor.Exe)! };
            foreach (var argument in AppUpdate.RestartArguments(Environment.ProcessId))
                start.ArgumentList.Add(argument);
            using var started = Process.Start(start);
        }
        catch (Exception problem)
        {
            string aftermath;
            try
            {
                AppUpdate.Restore(successor.Old, successor.Exe);
                aftermath = "Die alte Version ist wieder an ihrem Platz.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                aftermath = $"Die alte Version liegt hier: {successor.Old}";
            }
            MessageBox.Show($"Die neue Version ließ sich nicht starten. {aftermath}\n\n{problem.Message}",
                "VictoryLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
