using Avalonia.Threading;

namespace PokePic.Editor.Services;

/// <summary>
/// Watches a folder and calls back on the UI thread once changes settle,
/// e.g. when an image of the template is saved from a graphics program.
/// </summary>
public sealed class FolderWatcher : IDisposable
{
    private readonly DispatcherTimer _debounce;
    private FileSystemWatcher? _watcher;
    private string? _ignoredFile;

    public FolderWatcher(Action<IReadOnlyCollection<string>> onChanged)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            var files = changed.ToArray();
            changed.Clear();
            onChanged(files);
        };
        Changed = path => Dispatcher.UIThread.Post(() =>
        {
            changed.Add(path);
            _debounce.Stop();
            _debounce.Start();
        });
    }

    private Action<string> Changed { get; }

    /// <param name="ignoredFile">A file whose changes are not reported (the template being edited).</param>
    public void Watch(string? directory, string? ignoredFile = null)
    {
        _watcher?.Dispose();
        _watcher = null;
        _ignoredFile = ignoredFile is null ? null : Path.GetFullPath(ignoredFile);
        if (directory is null || !Directory.Exists(directory))
            return;

        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += OnEvent;
        _watcher.Created += OnEvent;
        _watcher.Deleted += OnEvent;
        _watcher.Renamed += OnEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (_ignoredFile is not null && string.Equals(Path.GetFullPath(e.FullPath), _ignoredFile, StringComparison.OrdinalIgnoreCase))
            return;
        Changed(e.FullPath);
    }

    public void Dispose()
    {
        _debounce.Stop();
        _watcher?.Dispose();
    }
}
