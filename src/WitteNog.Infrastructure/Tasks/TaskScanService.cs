namespace WitteNog.Infrastructure.Tasks;

using System.IO.Abstractions;
using WitteNog.Core.Events;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Parsing;

public class TaskScanService
{
    private readonly IFileSystem _fs;
    private readonly ITaskCache _cache;
    private string? _currentVaultPath;

    public TaskScanService(IFileSystem fs, ITaskCache cache)
    {
        _fs = fs;
        _cache = cache;
    }

    public void StartScanning(string vaultPath)
    {
        _currentVaultPath = vaultPath;
        ScanVault(vaultPath);
    }

    public void OnNoteChanged(NoteChangedEvent e)
    {
        if (_currentVaultPath == null) return;
        // Normalize the path so it matches the paths returned by GetFiles
        var filePath = _fs.Path.GetFullPath(e.FilePath);
        if (IsInArchive(filePath)) return;

        switch (e.ChangeType)
        {
            case NoteChangeType.Deleted:
                _cache.ClearTasksForFile(_currentVaultPath, filePath);
                break;
            case NoteChangeType.Created:
            case NoteChangeType.Modified:
                ScanFile(_currentVaultPath, filePath);
                break;
        }
    }

    private void ScanVault(string vaultPath)
    {
        if (!_fs.Directory.Exists(vaultPath)) return;

        var files = _fs.Directory.GetFiles(vaultPath, "*.md", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            if (!IsInArchive(file))
                ScanFile(vaultPath, file);
        }
    }

    private void ScanFile(string vaultPath, string filePath)
    {
        if (!_fs.File.Exists(filePath))
        {
            _cache.ClearTasksForFile(vaultPath, filePath);
            return;
        }

        var lines = _fs.File.ReadAllLines(filePath);
        var lastModified = new DateTimeOffset(_fs.FileInfo.New(filePath).LastWriteTimeUtc, TimeSpan.Zero);
        var tasks = TaskParser.ParseAllTasks(lines, filePath, lastModified);
        _cache.SetTasksForFile(vaultPath, filePath, tasks);
    }

    private static bool IsInArchive(string filePath) =>
        filePath.Contains(
            Path.DirectorySeparatorChar + "archive" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase) ||
        filePath.Contains(
            Path.AltDirectorySeparatorChar + "archive" + Path.AltDirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
