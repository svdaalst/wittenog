namespace WitteNog.Infrastructure.Tasks;

using System.IO.Abstractions;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public class TaskRepository : ITaskRepository
{
    private readonly ITaskCache _cache;
    private readonly IFileSystem _fs;

    public TaskRepository(ITaskCache cache, IFileSystem fs)
    {
        _cache = cache;
        _fs = fs;
    }

    public IReadOnlyList<TaskItem> GetAll(string vaultPath) =>
        _cache.GetTasks(vaultPath);

    public bool HasOpenTasksForFile(string vaultPath, string filePath) =>
        _cache.GetTasks(vaultPath).Any(t =>
            string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    public async Task CompleteTaskAsync(string vaultPath, string taskId, CancellationToken ct = default)
    {
        // Parse file path and line number directly from the task ID ("{filePath}:{lineNumber}").
        // This allows completion to work even when the cache is empty or stale.
        var lastColon = taskId.LastIndexOf(':');
        if (lastColon < 0 || !int.TryParse(taskId[(lastColon + 1)..], out var lineNumber))
            throw new InvalidOperationException($"Ongeldig taak-ID formaat: '{taskId}'");

        var filePath = taskId[..lastColon];

        if (!_fs.File.Exists(filePath))
            throw new InvalidOperationException($"Taakbestand niet gevonden: {filePath}");

        var lines = await Task.Run(() => _fs.File.ReadAllLines(filePath), ct);

        int targetLine = -1;

        // Snelpad: regelnummer uit taak-ID klopt nog
        if (lineNumber < lines.Length && lines[lineNumber].Contains("- [ ]"))
        {
            targetLine = lineNumber;
        }
        else
        {
            // Fallback: zoek via de gecachte ruwe tekstregel
            var cachedTask = _cache.GetTasks(vaultPath).FirstOrDefault(t => t.Id == taskId);
            if (cachedTask != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.Equals(lines[i], cachedTask.RawLine, StringComparison.Ordinal)
                        && lines[i].Contains("- [ ]"))
                    {
                        targetLine = i;
                        break;
                    }
                }
            }
        }

        if (targetLine == -1)
            throw new InvalidOperationException(
                $"Taak niet gevonden als open taak in {filePath}. " +
                "Het bestand is mogelijk gewijzigd sinds de laatste scan.");

        lines[targetLine] = lines[targetLine].Replace("- [ ]", "- [x]");
        await Task.Run(() => _fs.File.WriteAllLines(filePath, lines), ct);

        // Verify the write actually persisted — catches path mismatches (OneDrive, shadow copy, etc.)
        var written = await Task.Run(() => _fs.File.ReadAllLines(filePath), ct);
        if (written.Length <= targetLine || !written[targetLine].Contains("- [x]"))
            throw new InvalidOperationException(
                $"Schrijven naar '{filePath}' leek te slagen maar verificatie mislukte. " +
                $"Regel {targetLine} bevat nu: '{(written.Length > targetLine ? written[targetLine] : "(leeg)")}'.");

        _cache.RemoveTask(vaultPath, taskId);
    }
}
