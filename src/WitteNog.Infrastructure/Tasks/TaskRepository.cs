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
        var task = _cache.GetTasks(vaultPath).FirstOrDefault(t => t.Id == taskId);
        if (task == null) return;

        if (!_fs.File.Exists(task.FilePath)) return;

        var lines = await Task.Run(() => _fs.File.ReadAllLines(task.FilePath), ct);
        if (task.LineNumber >= lines.Length) return;

        // Guard against stale cache: verify the line still contains an open checkbox
        var currentLine = lines[task.LineNumber];
        if (!currentLine.Contains("- [ ]")) return;

        lines[task.LineNumber] = currentLine.Replace("- [ ]", "- [x]");
        await Task.Run(() => _fs.File.WriteAllLines(task.FilePath, lines), ct);

        _cache.RemoveTask(vaultPath, taskId);
    }
}
