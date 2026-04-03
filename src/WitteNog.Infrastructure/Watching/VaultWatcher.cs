namespace WitteNog.Infrastructure.Watching;

using WitteNog.Core.Events;

public class VaultWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly FileSystemWatcher? _metaWatcher;

    public event Action<NoteChangedEvent>? NoteChanged;
    public event Action? MetadataChanged;

    public VaultWatcher(string vaultPath)
    {
        _watcher = new FileSystemWatcher(vaultPath, "*")
        {
            IncludeSubdirectories = true,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        _watcher.Created += (_, e) =>
        {
            if (IsTrackedExtension(e.FullPath))
                NoteChanged?.Invoke(new NoteChangedEvent(e.FullPath, NoteChangeType.Created));
        };
        _watcher.Changed += (_, e) =>
        {
            if (IsTrackedExtension(e.FullPath))
                NoteChanged?.Invoke(new NoteChangedEvent(e.FullPath, NoteChangeType.Modified));
        };
        _watcher.Deleted += (_, e) =>
        {
            if (IsTrackedExtension(e.FullPath))
                NoteChanged?.Invoke(new NoteChangedEvent(e.FullPath, NoteChangeType.Deleted));
        };
        _watcher.Renamed += (_, e) =>
        {
            if (IsTrackedExtension(e.FullPath))
                NoteChanged?.Invoke(new NoteChangedEvent(e.FullPath, NoteChangeType.Modified));
        };

        var metaDir = Path.Combine(vaultPath, ".metadata");
        if (Directory.Exists(metaDir))
        {
            _metaWatcher = new FileSystemWatcher(metaDir, "vault-settings.json")
            {
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.LastWrite
            };
            _metaWatcher.Changed += (_, _) => MetadataChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _metaWatcher?.Dispose();
    }

    private static bool IsTrackedExtension(string path)
    {
        var ext = Path.GetExtension(path);
        return ext is ".md" or ".flow";
    }
}
