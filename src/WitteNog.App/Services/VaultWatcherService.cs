namespace WitteNog.App.Services;

using WitteNog.Core.Events;
using WitteNog.Infrastructure.Tasks;
using WitteNog.Infrastructure.Watching;

public class VaultWatcherService : IDisposable
{
    private VaultWatcher? _watcher;
    private string _currentPath = string.Empty;
    private readonly TaskScanService _taskScanService;

    public event Action<NoteChangedEvent>? NoteChanged;
    public event Action? MetadataChanged;

    public VaultWatcherService(TaskScanService taskScanService)
    {
        _taskScanService = taskScanService;
    }

    public void StartWatching(string vaultPath)
    {
        if (_currentPath == vaultPath) return;
        _watcher?.Dispose();
        _currentPath = vaultPath;
        try
        {
            _watcher = new VaultWatcher(vaultPath);
            _watcher.NoteChanged += e => NoteChanged?.Invoke(e);
            _watcher.NoteChanged += _taskScanService.OnNoteChanged;
            _watcher.MetadataChanged += () => MetadataChanged?.Invoke();
        }
        catch (Exception)
        {
            // FileSystemWatcher unavailable on this platform — rely on TriggerManualRefresh
            _watcher = null;
        }
        _taskScanService.StartScanning(vaultPath);
    }

    /// <summary>
    /// Forces a full refresh of notes and metadata. Called on Android when the app resumes
    /// from background, compensating for unreliable FileSystemWatcher on mobile.
    /// </summary>
    public void TriggerManualRefresh()
    {
        if (string.IsNullOrEmpty(_currentPath)) return;
        _taskScanService.StartScanning(_currentPath);
        MetadataChanged?.Invoke();
        NoteChanged?.Invoke(new NoteChangedEvent(_currentPath, NoteChangeType.Modified));
    }

    public void Dispose() => _watcher?.Dispose();
}
