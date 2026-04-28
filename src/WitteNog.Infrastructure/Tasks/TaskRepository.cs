namespace WitteNog.Infrastructure.Tasks;

using System.IO.Abstractions;
using System.Text.RegularExpressions;
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

        // M7: hold the file open with FileShare.None across the entire read-modify-write
        // so a concurrent UpdateNoteCommand or an external editor cannot slip a write
        // in between our read and our write — that race used to silently lose the
        // user's edits, with TaskRepository overwriting the file using stale content.
        // If the file is genuinely held by another process we now throw IOException
        // (loud failure) rather than silently corrupt.
        int targetLine;
        var lines = new List<string>();
        await using (var fs = _fs.FileStream.New(
                         filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            // Read line-by-line — matches ReadAllLines semantics (handles \n and \r\n,
            // strips trailing line break, drops the empty element after a final newline).
            using (var reader = new StreamReader(fs, leaveOpen: true))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct)) is not null)
                    lines.Add(line);
            }

            targetLine = -1;
            // Snelpad: regelnummer uit taak-ID klopt nog
            if (lineNumber < lines.Count && lines[lineNumber].Contains("- [ ]"))
            {
                targetLine = lineNumber;
            }
            else
            {
                // Fallback: zoek via de gecachte ruwe tekstregel
                var cachedTask = _cache.GetTasks(vaultPath).FirstOrDefault(t => t.Id == taskId);
                if (cachedTask != null)
                {
                    for (int i = 0; i < lines.Count; i++)
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

            // Truncate and rewrite under the same lock. WriteLineAsync per line matches
            // WriteAllLines (which also writes Environment.NewLine after every line,
            // including the last → file ends with a trailing newline).
            fs.Position = 0;
            fs.SetLength(0);
            await using var writer = new StreamWriter(fs, leaveOpen: true) { NewLine = Environment.NewLine };
            foreach (var l in lines)
                await writer.WriteLineAsync(l.AsMemory(), ct);
            await writer.FlushAsync(ct);
        }

        // Verify the write actually persisted — catches path mismatches (OneDrive, shadow copy, etc.)
        var written = await Task.Run(() => _fs.File.ReadAllLines(filePath), ct);
        if (written.Length <= targetLine || !written[targetLine].Contains("- [x]"))
            throw new InvalidOperationException(
                $"Schrijven naar '{filePath}' leek te slagen maar verificatie mislukte. " +
                $"Regel {targetLine} bevat nu: '{(written.Length > targetLine ? written[targetLine] : "(leeg)")}'.");

        _cache.RemoveTask(vaultPath, taskId);
    }

    public async Task UpdateTaskAsync(string vaultPath, string taskId, DateOnly? deadline, int? priority, CancellationToken ct = default)
    {
        var lastColon = taskId.LastIndexOf(':');
        if (lastColon < 0 || !int.TryParse(taskId[(lastColon + 1)..], out var lineNumber))
            throw new InvalidOperationException($"Ongeldig taak-ID formaat: '{taskId}'");

        var filePath = taskId[..lastColon];
        if (!_fs.File.Exists(filePath))
            throw new InvalidOperationException($"Taakbestand niet gevonden: {filePath}");

        var lines = await Task.Run(() => _fs.File.ReadAllLines(filePath), ct);

        int targetLine = -1;
        if (lineNumber < lines.Length && lines[lineNumber].Contains("- [ ]"))
        {
            targetLine = lineNumber;
        }
        else
        {
            var cachedTask = _cache.GetTasks(vaultPath).FirstOrDefault(t => t.Id == taskId);
            if (cachedTask != null)
                for (int i = 0; i < lines.Length; i++)
                    if (string.Equals(lines[i], cachedTask.RawLine, StringComparison.Ordinal) && lines[i].Contains("- [ ]"))
                    { targetLine = i; break; }
        }

        if (targetLine == -1)
            throw new InvalidOperationException($"Taak niet gevonden in {filePath}.");

        lines[targetLine] = RewriteLine(lines[targetLine], deadline, priority);
        await Task.Run(() => _fs.File.WriteAllLines(filePath, lines), ct);
    }

    private static string RewriteLine(string rawLine, DateOnly? deadline, int? priority)
    {
        var line = Regex.Replace(rawLine, @"\s*@@?\d{4}-\d{2}-\d{2}", "");
        line = Regex.Replace(line, @"\s*![Pp][1-5]", "").TrimEnd();
        if (deadline.HasValue) line += $" @{deadline.Value:yyyy-MM-dd}";
        if (priority.HasValue) line += $" !P{priority}";
        return line;
    }
}
