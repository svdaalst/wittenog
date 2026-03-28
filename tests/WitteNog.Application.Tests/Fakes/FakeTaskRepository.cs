using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Fakes;

public class FakeTaskRepository : ITaskRepository
{
    private readonly List<TaskItem> _tasks;
    // Simulates file content: filePath → lines
    private readonly Dictionary<string, List<string>> _files;

    public FakeTaskRepository(IEnumerable<TaskItem> tasks,
        Dictionary<string, List<string>>? files = null)
    {
        _tasks = tasks.ToList();
        _files = files ?? new Dictionary<string, List<string>>();
    }

    public IReadOnlyList<TaskItem> GetAll(string vaultPath) => _tasks.AsReadOnly();

    public bool HasOpenTasksForFile(string vaultPath, string filePath) =>
        _tasks.Any(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

    public Task CompleteTaskAsync(string vaultPath, string taskId, CancellationToken ct = default)
    {
        var task = _tasks.FirstOrDefault(t => t.Id == taskId);
        if (task == null) return Task.CompletedTask;

        if (_files.TryGetValue(task.FilePath, out var lines) && task.LineNumber < lines.Count)
            lines[task.LineNumber] = lines[task.LineNumber].Replace("- [ ]", "- [x]");

        _tasks.Remove(task);
        return Task.CompletedTask;
    }

    public IReadOnlyList<TaskItem> All => _tasks.AsReadOnly();
}
