using System.IO.Abstractions;
using System.Text.Json;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Infrastructure.Settings;

public class JsonSettingsProvider : ILinkMetadataService, IVaultSettings, ITaskCache
{
    private static readonly System.Text.Json.JsonSerializerOptions IndentedJson =
        new() { WriteIndented = true };

    private readonly IFileSystem _fs;
    private string? _loadedVault;
    private VaultSettings _settings = new();
    // Derived from _settings.ArchivedLinks for O(1) case-insensitive lookup.
    private HashSet<string> _archivedSet = new(StringComparer.OrdinalIgnoreCase);

    public event Action? MetadataChanged;
    public event Action<string>? ArchiveGuardTriggered;

    public JsonSettingsProvider(IFileSystem fs) => _fs = fs;

    private static string MetadataDir(string vaultPath) =>
        Path.Combine(vaultPath, ".metadata");

    private static string SettingsPath(string vaultPath) =>
        Path.Combine(MetadataDir(vaultPath), "vault-settings.json");

    private void MigrateIfNeeded(string vaultPath)
    {
        var oldPath = Path.Combine(vaultPath, "vault-settings.json");
        var newPath = SettingsPath(vaultPath);
        if (_fs.File.Exists(oldPath) && !_fs.File.Exists(newPath))
        {
            _fs.Directory.CreateDirectory(MetadataDir(vaultPath));
            _fs.File.Move(oldPath, newPath);
        }
    }

    private VaultSettings EnsureLoaded(string vaultPath)
    {
        if (_loadedVault == vaultPath) return _settings;
        MigrateIfNeeded(vaultPath);
        var path = SettingsPath(vaultPath);
        _settings = _fs.File.Exists(path)
            ? JsonSerializer.Deserialize<VaultSettings>(_fs.File.ReadAllText(path)) ?? new VaultSettings()
            : new VaultSettings();
        _archivedSet = new HashSet<string>(_settings.ArchivedLinks, StringComparer.OrdinalIgnoreCase);
        _loadedVault = vaultPath;
        return _settings;
    }

    private void Persist(string vaultPath)
    {
        // Sync the mutable _archivedSet back into the immutable record before writing.
        _settings = _settings with { ArchivedLinks = _archivedSet.OrderBy(l => l).ToList() };
        _fs.Directory.CreateDirectory(MetadataDir(vaultPath));
        var json = JsonSerializer.Serialize(_settings, IndentedJson);
        _fs.File.WriteAllText(SettingsPath(vaultPath), json);
    }

    // ── ILinkMetadataService ────────────────────────────────────────────────────

    public bool IsArchived(string vaultPath, string link)
    {
        EnsureLoaded(vaultPath);
        return _archivedSet.Contains(link);
    }

    public void SetArchivedStatus(string vaultPath, string link, bool archived)
    {
        EnsureLoaded(vaultPath);
        if (archived)
        {
            _archivedSet.Add(link);
            var hasOpenTasks = _settings.Tasks.Any(t =>
                string.Equals(t.ProjectLink, link, StringComparison.OrdinalIgnoreCase));
            if (hasOpenTasks)
                ArchiveGuardTriggered?.Invoke(link);
        }
        else
        {
            _archivedSet.Remove(link);
        }
        Persist(vaultPath);
        MetadataChanged?.Invoke();
    }

    public IReadOnlySet<string> GetArchivedLinks(string vaultPath)
    {
        EnsureLoaded(vaultPath);
        return _archivedSet;
    }

    public void InvalidateCache(string vaultPath)
    {
        if (_loadedVault == vaultPath) _loadedVault = null;
    }

    // ── IVaultSettings ──────────────────────────────────────────────────────────

    public TranscriptionSettings GetTranscriptionSettings(string vaultPath) =>
        EnsureLoaded(vaultPath).Transcription;

    public void SaveTranscriptionSettings(string vaultPath, TranscriptionSettings settings)
    {
        EnsureLoaded(vaultPath);
        _settings = _settings with { Transcription = settings };
        Persist(vaultPath);
    }

    // ── ITaskCache ──────────────────────────────────────────────────────────────

    public IReadOnlyList<TaskItem> GetTasks(string vaultPath)
    {
        EnsureLoaded(vaultPath);
        return _settings.Tasks.Select(MapToTaskItem).ToList().AsReadOnly();
    }

    public void SetTasksForFile(string vaultPath, string filePath, IReadOnlyList<TaskItem> tasks)
    {
        EnsureLoaded(vaultPath);
        var others = _settings.Tasks
            .Where(t => !string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            .ToList();
        others.AddRange(tasks.Select(MapToData));
        _settings = _settings with { Tasks = others };
        Persist(vaultPath);
    }

    public void RemoveTask(string vaultPath, string taskId)
    {
        EnsureLoaded(vaultPath);
        _settings = _settings with { Tasks = _settings.Tasks.Where(t => t.Id != taskId).ToList() };
        Persist(vaultPath);
    }

    public void ClearTasksForFile(string vaultPath, string filePath)
    {
        EnsureLoaded(vaultPath);
        _settings = _settings with
        {
            Tasks = _settings.Tasks
                .Where(t => !string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList()
        };
        Persist(vaultPath);
    }

    // ── Mapping helpers ─────────────────────────────────────────────────────────

    private static TaskItem MapToTaskItem(TaskItemData d) => new(
        Id: d.Id,
        FilePath: d.FilePath,
        LineNumber: d.LineNumber,
        RawLine: d.RawLine,
        Description: d.Description,
        ProjectLink: d.ProjectLink,
        Deadline: d.Deadline != null ? DateOnly.Parse(d.Deadline) : null,
        Priority: d.Priority,
        LastModified: DateTimeOffset.Parse(d.LastModified)
    ) { SourceFirstWikiLink = d.SourceFirstWikiLink };

    private static TaskItemData MapToData(TaskItem t) => new(
        Id: t.Id,
        FilePath: t.FilePath,
        LineNumber: t.LineNumber,
        RawLine: t.RawLine,
        Description: t.Description,
        ProjectLink: t.ProjectLink,
        Deadline: t.Deadline?.ToString("yyyy-MM-dd"),
        Priority: t.Priority,
        LastModified: t.LastModified.ToString("O"),
        SourceFirstWikiLink: t.SourceFirstWikiLink
    );
}

internal record VaultSettings
{
    public List<string> ArchivedLinks { get; init; } = [];
    public TranscriptionSettings Transcription { get; init; } = new();
    public List<TaskItemData> Tasks { get; init; } = [];
}

internal record TaskItemData(
    string Id,
    string FilePath,
    int LineNumber,
    string RawLine,
    string Description,
    string? ProjectLink,
    string? Deadline,
    int? Priority,
    string LastModified,
    string? SourceFirstWikiLink = null
);
