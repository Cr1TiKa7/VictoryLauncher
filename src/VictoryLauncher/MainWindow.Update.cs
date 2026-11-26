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
        OwnVersion == ReleaseVersion.Local ? "VictoryLauncher (local build)" : $"VictoryLauncher {OwnVersion}";

    private UpdateInfo? _update;
    private int _downloadPercent = -1;
    // Set once the new program is in place. It is started when this window has closed.
    private (ProcessStartInfo Start, string Old)? _successor;

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
            UpdateText.Text = $"Version {update.Version} is out, you have {OwnVersion}.";
            UpdateBar.Visibility = Visibility.Visible;
        }
        catch (Exception e)
        {
            _log.Add("Update check failed: " + e.Message);
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
            _log.Add($"Update from {OwnVersion} to {update.Version}: {update.Download}");
            StatusText.Text = $"Downloading version {update.Version} …";
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
            // A single file loads its parts from its own file when they are first needed. The swap puts another
            // program there, so what has not been used by then cannot be loaded any more. Saving and preparing the
            // restart come first, and the swap is the last step.
            SaveSettings();
            var restart = RestartOf(exe);
            _log.Add($"Version {update.Version} is downloaded, swapping and restarting.");
            _successor = (restart, AppUpdate.Swap(fresh, exe));
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = cancel.IsCancellationRequested
                ? "Update cancelled."
                : "The download took too long, please try again later.";
        }
        catch (Exception problem)
        {
            _log.Add(problem.ToString());
            StatusText.Text = "Update failed: " + problem.Message;
            Warn("The update failed, the launcher stays as it is.\n\n" + problem.Message);
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

    private static ProcessStartInfo RestartOf(string exe)
    {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = Path.GetDirectoryName(exe)! };
        foreach (var argument in AppUpdate.RestartArguments(Environment.ProcessId))
            start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>The last thing before the process ends.</summary>
    private void StartSuccessor()
    {
        if (_successor is not { } successor)
            return;
        try
        {
            using var started = Process.Start(successor.Start);
        }
        catch (Exception problem)
        {
            string aftermath;
            try
            {
                AppUpdate.Restore(successor.Old, successor.Start.FileName);
                aftermath = "The old version is back in place.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                aftermath = $"The old version is here: {successor.Old}";
            }
            MessageBox.Show($"The new version could not be started. {aftermath}\n\n{problem.Message}",
                "VictoryLauncher", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
