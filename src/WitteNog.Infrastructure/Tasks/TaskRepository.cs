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
        var allTasks = _cache.GetTasks(vaultPath);
        var task = allTasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null)
            throw new InvalidOperationException(
                $"Taak niet gevonden. Gezocht op ID: '{taskId}' in vault '{vaultPath}'. " +
                $"Cache bevat {allTasks.Count} taken: [{string.Join(", ", allTasks.Select(t => $"'{t.Id}'"))}]");

        if (!_fs.File.Exists(task.FilePath))
            throw new InvalidOperationException($"Taakbestand niet gevonden: {task.FilePath}");

        var lines = await Task.Run(() => _fs.File.ReadAllLines(task.FilePath), ct);

        int targetLine = -1;

        // Snelpad: gecachte regelnummer klopt nog
        if (task.LineNumber < lines.Length
            && lines[task.LineNumber].Contains("- [ ]")
            && string.Equals(lines[task.LineNumber], task.RawLine, StringComparison.Ordinal))
        {
            targetLine = task.LineNumber;
        }
        else
        {
            // Fallback: zoek de originele regel in het hele bestand
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.Equals(lines[i], task.RawLine, StringComparison.Ordinal)
                    && lines[i].Contains("- [ ]"))
                {
                    targetLine = i;
                    break;
                }
            }
        }

        if (targetLine == -1)
            throw new InvalidOperationException(
                $"Taak '{task.Description}' niet gevonden als open taak in {task.FilePath}. " +
                "Het bestand is mogelijk gewijzigd sinds de laatste scan.");

        lines[targetLine] = lines[targetLine].Replace("- [ ]", "- [x]");
        await Task.Run(() => _fs.File.WriteAllLines(task.FilePath, lines), ct);

        // Verify the write actually persisted — catches path mismatches (OneDrive, shadow copy, etc.)
        var written = await Task.Run(() => _fs.File.ReadAllLines(task.FilePath), ct);
        if (written.Length <= targetLine || !written[targetLine].Contains("- [x]"))
            throw new InvalidOperationException(
                $"Schrijven naar '{task.FilePath}' leek te slagen maar verificatie mislukte. " +
                $"Regel {targetLine} bevat nu: '{(written.Length > targetLine ? written[targetLine] : "(leeg)")}'.");

        _cache.RemoveTask(vaultPath, taskId);
    }
}
