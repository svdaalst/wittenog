using System.IO.Abstractions.TestingHelpers;
using WitteNog.Core.Events;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Infrastructure.Tasks;

namespace WitteNog.Infrastructure.Tests.Tasks;

public class TaskScanServiceTests
{
    private const string VaultPath = "/vault";

    // ── In-memory ITaskCache for tests ─────────────────────────────────────────

    private sealed class MemoryTaskCache : ITaskCache
    {
        // vaultPath → filePath → tasks
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

    private static TaskScanService BuildSut(MockFileSystem fs, ITaskCache cache) =>
        new(fs, cache);

    // ── StartScanning ──────────────────────────────────────────────────────────

    [Fact]
    public void StartScanning_IndexesOpenTasks()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData(
            "# Note\n- [ ] [[ProjectX]] Task one @2026-03-25 !P2\n- [x] Done task"));

        var cache = new MemoryTaskCache();
        var sut = BuildSut(fs, cache);
        sut.StartScanning(VaultPath);

        var tasks = cache.GetTasks(VaultPath);
        Assert.Single(tasks);
        Assert.Equal("Task one", tasks[0].Description);
        Assert.Equal("ProjectX", tasks[0].ProjectLink);
        Assert.Equal(2, tasks[0].Priority);
        Assert.Equal(new DateOnly(2026, 3, 25), tasks[0].Deadline);
    }

    [Fact]
    public void StartScanning_IgnoresCompletedTasks()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData(
            "- [x] Completed\n- [x] Also done"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        Assert.Empty(cache.GetTasks(VaultPath));
    }

    [Fact]
    public void StartScanning_ScansMultipleFiles()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/a.md", new MockFileData("- [ ] Task A"));
        fs.AddFile($"{VaultPath}/b.md", new MockFileData("- [ ] Task B !P1"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        Assert.Equal(2, cache.GetTasks(VaultPath).Count);
    }

    [Fact]
    public void StartScanning_IgnoresArchiveDirectory()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData("- [ ] Active task"));
        fs.AddFile($"{VaultPath}/archive/old.md", new MockFileData("- [ ] Archived task"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        var tasks = cache.GetTasks(VaultPath);
        Assert.Single(tasks);
        Assert.Equal("Active task", tasks[0].Description);
    }

    // ── OnNoteChanged ──────────────────────────────────────────────────────────

    [Fact]
    public void OnNoteChanged_Modified_RescansChangedFile()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData("- [ ] Original task"));

        var cache = new MemoryTaskCache();
        var sut = BuildSut(fs, cache);
        sut.StartScanning(VaultPath);

        Assert.Single(cache.GetTasks(VaultPath));

        // Simulate file update
        fs.File.WriteAllText($"{VaultPath}/note.md",
            "- [ ] Original task\n- [ ] New task !P1");
        sut.OnNoteChanged(new NoteChangedEvent($"{VaultPath}/note.md", NoteChangeType.Modified));

        Assert.Equal(2, cache.GetTasks(VaultPath).Count);
    }

    [Fact]
    public void OnNoteChanged_Deleted_ClearsTasksForFile()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData("- [ ] Task"));

        var cache = new MemoryTaskCache();
        var sut = BuildSut(fs, cache);
        sut.StartScanning(VaultPath);

        Assert.Single(cache.GetTasks(VaultPath));

        fs.File.Delete($"{VaultPath}/note.md");
        sut.OnNoteChanged(new NoteChangedEvent($"{VaultPath}/note.md", NoteChangeType.Deleted));

        Assert.Empty(cache.GetTasks(VaultPath));
    }

    [Fact]
    public void OnNoteChanged_Created_IndexesNewFile()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(VaultPath);

        var cache = new MemoryTaskCache();
        var sut = BuildSut(fs, cache);
        sut.StartScanning(VaultPath);

        Assert.Empty(cache.GetTasks(VaultPath));

        fs.AddFile($"{VaultPath}/new.md", new MockFileData("- [ ] Brand new task !P3"));
        sut.OnNoteChanged(new NoteChangedEvent($"{VaultPath}/new.md", NoteChangeType.Created));

        Assert.Single(cache.GetTasks(VaultPath));
        Assert.Equal("Brand new task", cache.GetTasks(VaultPath)[0].Description);
    }

    [Fact]
    public void OnNoteChanged_ArchiveFile_IsIgnored()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory(VaultPath);

        var cache = new MemoryTaskCache();
        var sut = BuildSut(fs, cache);
        sut.StartScanning(VaultPath);

        fs.AddFile($"{VaultPath}/archive/old.md", new MockFileData("- [ ] Old task"));
        sut.OnNoteChanged(new NoteChangedEvent($"{VaultPath}/archive/old.md", NoteChangeType.Created));

        Assert.Empty(cache.GetTasks(VaultPath));
    }
}
