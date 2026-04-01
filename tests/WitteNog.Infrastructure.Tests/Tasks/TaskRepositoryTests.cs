using System.IO.Abstractions.TestingHelpers;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Infrastructure.Tasks;

namespace WitteNog.Infrastructure.Tests.Tasks;

public class TaskRepositoryTests
{
    private const string VaultPath = "/vault";
    private const string FilePath = "/vault/note.md";
    private static readonly DateTimeOffset Now = new(2026, 3, 30, 0, 0, 0, TimeSpan.Zero);

    // ── In-memory ITaskCache for tests ─────────────────────────────────────────

    private sealed class MemoryTaskCache : ITaskCache
    {
        private readonly Dictionary<string, Dictionary<string, List<TaskItem>>> _store = new();

        public IReadOnlyList<TaskItem> GetTasks(string vaultPath)
        {
            if (!_store.TryGetValue(vaultPath, out var byFile)) return [];
            return byFile.Values.SelectMany(t => t).ToList().AsReadOnly();
        }

        public void SetTasksForFile(string vaultPath, string filePath, IReadOnlyList<TaskItem> tasks)
        {
            if (!_store.ContainsKey(vaultPath)) _store[vaultPath] = new();
            _store[vaultPath][filePath] = tasks.ToList();
        }

        public void RemoveTask(string vaultPath, string taskId)
        {
            if (!_store.TryGetValue(vaultPath, out var byFile)) return;
            foreach (var list in byFile.Values)
                list.RemoveAll(t => t.Id == taskId);
        }

        public void ClearTasksForFile(string vaultPath, string filePath)
        {
            if (_store.TryGetValue(vaultPath, out var byFile))
                byFile.Remove(filePath);
        }
    }

    private static TaskItem MakeTask(int lineNumber, string rawLine = "- [ ] Do the thing") =>
        new($"{FilePath}:{lineNumber}", FilePath, lineNumber, rawLine,
            "Do the thing", null, null, null, Now);

    private static TaskRepository BuildSut(MockFileSystem fs, ITaskCache cache) =>
        new(cache, fs);

    // ── Tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteTask_FastPath_UpdatesCorrectLine()
    {
        var fs = new MockFileSystem();
        fs.AddFile(FilePath, new MockFileData("# Note\n- [ ] Do the thing"));

        var cache = new MemoryTaskCache();
        var task = MakeTask(lineNumber: 1);
        cache.SetTasksForFile(VaultPath, FilePath, [task]);

        await BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task.Id);

        var lines = fs.File.ReadAllLines(FilePath);
        Assert.Equal("- [x] Do the thing", lines[1]);
        Assert.Empty(cache.GetTasks(VaultPath));
    }

    [Fact]
    public async Task CompleteTask_StaleLineNumber_FallbackUpdatesCorrectLine()
    {
        // File has two extra lines added above the task since the cache was built.
        var fs = new MockFileSystem();
        fs.AddFile(FilePath, new MockFileData(
            "# Note\n## Extra heading\nSome text\n- [ ] Do the thing"));

        var cache = new MemoryTaskCache();
        // Cache still thinks the task is at line 1 (stale).
        var task = MakeTask(lineNumber: 1);
        cache.SetTasksForFile(VaultPath, FilePath, [task]);

        await BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task.Id);

        var lines = fs.File.ReadAllLines(FilePath);
        Assert.Equal("- [x] Do the thing", lines[3]);
        Assert.Empty(cache.GetTasks(VaultPath));
    }

    [Fact]
    public async Task CompleteTask_AlreadyCompleted_Throws()
    {
        var fs = new MockFileSystem();
        // File already has - [x], but cache still has - [ ] as RawLine.
        fs.AddFile(FilePath, new MockFileData("# Note\n- [x] Do the thing"));

        var cache = new MemoryTaskCache();
        var task = MakeTask(lineNumber: 1);
        cache.SetTasksForFile(VaultPath, FilePath, [task]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task.Id));
    }

    [Fact]
    public async Task CompleteTask_FileNotFound_Throws()
    {
        var fs = new MockFileSystem();
        // No file added — it doesn't exist.

        var cache = new MemoryTaskCache();
        var task = MakeTask(lineNumber: 0);
        cache.SetTasksForFile(VaultPath, FilePath, [task]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task.Id));
    }

    [Fact]
    public async Task CompleteTask_MultipleTasksInFile_OnlyCompletesTarget()
    {
        var fs = new MockFileSystem();
        fs.AddFile(FilePath, new MockFileData("# Note\n- [ ] Task one\n- [ ] Task two"));

        var cache = new MemoryTaskCache();
        var task1 = new TaskItem($"{FilePath}:1", FilePath, 1, "- [ ] Task one", "Task one", null, null, null, Now);
        var task2 = new TaskItem($"{FilePath}:2", FilePath, 2, "- [ ] Task two", "Task two", null, null, null, Now);
        cache.SetTasksForFile(VaultPath, FilePath, [task1, task2]);

        await BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task1.Id);

        var lines = fs.File.ReadAllLines(FilePath);
        Assert.Equal("- [x] Task one", lines[1]);
        Assert.Equal("- [ ] Task two", lines[2]);
        Assert.Single(cache.GetTasks(VaultPath));
        Assert.Equal(task2.Id, cache.GetTasks(VaultPath)[0].Id);
    }

    [Fact]
    public async Task CompleteTask_DuplicateLines_FastPathWinsOnLineNumber()
    {
        // Two identical task lines; cached task points to line 2 (index 2).
        var fs = new MockFileSystem();
        fs.AddFile(FilePath, new MockFileData(
            "# Note\n- [ ] Do the thing\n- [ ] Do the thing"));

        var cache = new MemoryTaskCache();
        var task = MakeTask(lineNumber: 2);
        cache.SetTasksForFile(VaultPath, FilePath, [task]);

        await BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task.Id);

        var lines = fs.File.ReadAllLines(FilePath);
        Assert.Equal("- [ ] Do the thing", lines[1]); // line 1 unchanged
        Assert.Equal("- [x] Do the thing", lines[2]); // line 2 completed
    }

    [Fact]
    public async Task CompleteTask_EmptyCache_StillCompletesViaLineNumber()
    {
        // Reproduces the production bug: JsonSettingsProvider returns Tasks:[] from disk
        // because cache was invalidated, but the task ID encodes the file path and line number.
        var fs = new MockFileSystem();
        fs.AddFile(FilePath, new MockFileData("# Note\n- [ ] Do the thing"));

        var cache = new MemoryTaskCache();
        // Cache is intentionally empty — simulates cache invalidation scenario.

        var task = MakeTask(lineNumber: 1);
        await BuildSut(fs, cache).CompleteTaskAsync(VaultPath, task.Id);

        var lines = fs.File.ReadAllLines(FilePath);
        Assert.Equal("- [x] Do the thing", lines[1]);
    }
}
