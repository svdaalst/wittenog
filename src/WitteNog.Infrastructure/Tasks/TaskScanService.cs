namespace WitteNog.Infrastructure.Tasks;

using System.IO.Abstractions;
using WitteNog.Core.Events;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;

public class TaskScanService
{
    private readonly IFileSystem _fs;
    private readonly ITaskCache _cache;
    private readonly IWikiLinkParser _wikiLinkParser;
    private string? _currentVaultPath;

    public TaskScanService(IFileSystem fs, ITaskCache cache, IWikiLinkParser wikiLinkParser)
    {
        _fs = fs;
        _cache = cache;
        _wikiLinkParser = wikiLinkParser;
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

        var allFiles = _fs.Directory.GetFiles(vaultPath, "*.md", SearchOption.AllDirectories);
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in allFiles)
        {
            if (!IsInArchive(file))
            {
                scannedPaths.Add(file);
                ScanFile(vaultPath, file);
            }
        }

        // Evict cached tasks whose source file no longer exists or has been archived
        var stalePaths = _cache.GetTasks(vaultPath)
            .Select(t => t.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(p => !scannedPaths.Contains(p))
            .ToList();
        foreach (var stalePath in stalePaths)
            _cache.ClearTasksForFile(vaultPath, stalePath);
    }

    private void ScanFile(string vaultPath, string filePath)
    {
        if (!_fs.File.Exists(filePath))
        {
            _cache.ClearTasksForFile(vaultPath, filePath);
            return;
        }

        string[] lines;
        DateTimeOffset lastModified;
        try
        {
            lines = _fs.File.ReadAllLines(filePath);
            lastModified = new DateTimeOffset(_fs.FileInfo.New(filePath).LastWriteTimeUtc, TimeSpan.Zero);
        }
        catch (IOException)
        {
            // Bestand is tijdelijk vergrendeld door een lopende write-operatie.
            // Cache ongewijzigd laten; de volgende FSW-event scant opnieuw.
            return;
        }

        IReadOnlyList<TaskItem> tasks = TaskParser.ParseAllTasks(lines, filePath, lastModified);

        string? firstWikiLink = null;
        foreach (var line in lines)
        {
            var links = _wikiLinkParser.ExtractLinks(line);
            if (links.Count > 0) { firstWikiLink = links[0]; break; }
        }

        if (firstWikiLink != null)
            tasks = tasks.Select(t => t with { SourceFirstWikiLink = firstWikiLink })
                         .ToList().AsReadOnly();

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
