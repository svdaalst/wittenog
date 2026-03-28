namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface ITaskCache
{
    IReadOnlyList<TaskItem> GetTasks(string vaultPath);
    void SetTasksForFile(string vaultPath, string filePath, IReadOnlyList<TaskItem> tasks);
    void RemoveTask(string vaultPath, string taskId);
    void ClearTasksForFile(string vaultPath, string filePath);
}
