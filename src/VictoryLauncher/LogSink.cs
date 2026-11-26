using System.IO;

namespace VictoryLauncher;

/// <summary>
/// Takes lines from any thread: all go to the log file, the newest ones are kept for the window. The window reads them
/// on a timer, because a run writes far more lines than a window can show one by one.
/// </summary>
internal sealed class LogSink : IDisposable
{
    private const int Kept = 200;
    private const int LongestShown = 400;

    private readonly Lock _gate = new();
    private readonly Queue<string> _tail = new();
    private StreamWriter? _file;
    private bool _changed;

    /// <summary>Starts a new log file; the window's tail goes on.</summary>
    public void Open(string path)
    {
        lock (_gate)
        {
            _file?.Dispose();
            _file = null;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
        }
    }

    public void Add(string line)
    {
        lock (_gate)
        {
            _file?.WriteLine(line);
            _tail.Enqueue(line.Length > LongestShown ? line[..LongestShown] + " …" : line);
            while (_tail.Count > Kept)
                _tail.Dequeue();
            _changed = true;
        }
    }

    public bool TryTakeTail(out string text)
    {
        lock (_gate)
        {
            text = "";
            if (!_changed)
                return false;
            _changed = false;
            _file?.Flush();
            text = string.Join(Environment.NewLine, _tail);
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _file?.Dispose();
            _file = null;
        }
    }
}
