using System;
using System.IO;
using Timer = System.Timers.Timer;

namespace Teleprompter.App.Services;

/// <summary>
/// Watches one script file and fires a debounced callback when it changes.
///
/// The parent directory is watched rather than the file itself because Word
/// saves through a temp-file-and-rename dance: the target briefly disappears
/// and comes back under a rename, which direct file watching misses. Any
/// create/change/delete/rename touching the file name re-arms the debounce,
/// and the callback fires once the dust has settled.
/// </summary>
public sealed class ScriptFileWatcher : IDisposable
{
    private const int DebounceMs = 500;

    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounce;
    private readonly string _fileName;

    public ScriptFileWatcher(string filePath, Action changed)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(filePath))
            ?? throw new ArgumentException("The script path has no directory.", nameof(filePath));
        _fileName = Path.GetFileName(filePath);

        _debounce = new Timer(DebounceMs) { AutoReset = false };
        _debounce.Elapsed += (_, _) => changed();

        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            IncludeSubdirectories = false
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Deleted += OnFileEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (Matches(e.Name))
        {
            Arm();
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (Matches(e.Name) || Matches(e.OldName))
        {
            Arm();
        }
    }

    private bool Matches(string? candidate)
        => string.Equals(candidate, _fileName, StringComparison.OrdinalIgnoreCase);

    private void Arm()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debounce.Dispose();
    }
}
