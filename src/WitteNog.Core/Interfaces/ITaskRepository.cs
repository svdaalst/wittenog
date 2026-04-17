namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface ITaskRepository
{
    IReadOnlyList<TaskItem> GetAll(string vaultPath);
    Task CompleteTaskAsync(string vaultPath, string taskId, CancellationToken ct = default);
    Task UpdateTaskAsync(string vaultPath, string taskId, DateOnly? deadline, int? priority, CancellationToken ct = default);
    bool HasOpenTasksForFile(string vaultPath, string filePath);
}
