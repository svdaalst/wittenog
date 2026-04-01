using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text;
using Microsoft.Win32.SafeHandles;
using WitteNog.Core.Events;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Infrastructure.Parsing;
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
        new(fs, cache, new WikiLinkParser());

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

    // ── IOException resilience ─────────────────────────────────────────────────

    [Fact]
    public void OnNoteChanged_Modified_WhenFileIsLocked_DoesNotThrowAndPreservesCachedTasks()
    {
        const string filePath = $"{VaultPath}/note.md";
        var innerFs = new MockFileSystem();
        innerFs.AddFile(filePath, new MockFileData("- [ ] Existing task"));

        var cache = new MemoryTaskCache();
        // Eerste scan met normaal bestandssysteem vult de cache
        BuildSut(innerFs, cache).StartScanning(VaultPath);
        Assert.Single(cache.GetTasks(VaultPath));

        // Simuleer een vergrendeld bestand: IOException op ReadAllLines
        var lockedPath = innerFs.Path.GetFullPath(filePath);
        var lockedFs = new LockedFileSystem(innerFs, lockedPath);
        var sutLocked = new TaskScanService(lockedFs, cache, new WikiLinkParser());
        sutLocked.StartScanning(VaultPath); // registreert vault path; IOException wordt intern gevangen

        var ex = Record.Exception(() =>
            sutLocked.OnNoteChanged(new NoteChangedEvent(filePath, NoteChangeType.Modified)));

        Assert.Null(ex);                          // mag niet crashen
        Assert.Single(cache.GetTasks(VaultPath)); // cache ongewijzigd
    }

    // ── SourceFirstWikiLink ───────────────────────────────────────────────────

    [Fact]
    public void StartScanning_FileWithWikiLink_SetsSourceFirstWikiLink()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/2026-04-01.md", new MockFileData(
            "# Daily\n[[ProjectA]] Some content\n- [ ] Do a thing"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        var tasks = cache.GetTasks(VaultPath);
        Assert.Single(tasks);
        Assert.Equal("ProjectA", tasks[0].SourceFirstWikiLink);
    }

    [Fact]
    public void StartScanning_FileWithNoWikiLinks_SourceFirstWikiLinkIsNull()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData(
            "# Note\n- [ ] Task without links"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        var tasks = cache.GetTasks(VaultPath);
        Assert.Single(tasks);
        Assert.Null(tasks[0].SourceFirstWikiLink);
    }

    [Fact]
    public void StartScanning_FileWithMultipleWikiLinks_UsesFirst()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData(
            "# Note\n[[FirstLink]] content\n[[SecondLink]] more\n- [ ] A task"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        var tasks = cache.GetTasks(VaultPath);
        Assert.Single(tasks);
        Assert.Equal("FirstLink", tasks[0].SourceFirstWikiLink);
    }

    [Fact]
    public void StartScanning_MultipleTasksInFile_AllGetSameSourceFirstWikiLink()
    {
        var fs = new MockFileSystem();
        fs.AddFile($"{VaultPath}/note.md", new MockFileData(
            "[[ProjectA]]\n- [ ] Task one\n- [ ] Task two"));

        var cache = new MemoryTaskCache();
        BuildSut(fs, cache).StartScanning(VaultPath);

        var tasks = cache.GetTasks(VaultPath);
        Assert.Equal(2, tasks.Count);
        Assert.All(tasks, t => Assert.Equal("ProjectA", t.SourceFirstWikiLink));
    }

    // ── Test doubles ──────────────────────────────────────────────────────────

    private sealed class LockedFileSystem : IFileSystem
    {
        private readonly MockFileSystem _inner;

        public LockedFileSystem(MockFileSystem inner, string lockedPath)
        {
            _inner = inner;
            File = new LockedFileWrapper(inner.File, lockedPath);
        }

        public IFile File { get; }
        public IDirectory Directory => _inner.Directory;
        public IFileInfoFactory FileInfo => _inner.FileInfo;
        public IFileStreamFactory FileStream => _inner.FileStream;
        public IPath Path => _inner.Path;
        public IDirectoryInfoFactory DirectoryInfo => _inner.DirectoryInfo;
        public IDriveInfoFactory DriveInfo => _inner.DriveInfo;
        public IFileSystemWatcherFactory FileSystemWatcher => _inner.FileSystemWatcher;
        public IFileVersionInfoFactory FileVersionInfo => _inner.FileVersionInfo;
    }

    private sealed class LockedFileWrapper : IFile
    {
        private readonly IFile _inner;
        private readonly string _lockedPath;

        public LockedFileWrapper(IFile inner, string lockedPath)
        {
            _inner = inner;
            _lockedPath = lockedPath;
        }

        public IFileSystem FileSystem => _inner.FileSystem;

        // Gooit IOException voor het vergrendelde pad; delegeert de rest naar _inner
        public string[] ReadAllLines(string path) =>
            path == _lockedPath
                ? throw new IOException("The process cannot access the file because it is being used by another process.")
                : _inner.ReadAllLines(path);

        public string[] ReadAllLines(string path, Encoding encoding) =>
            path == _lockedPath
                ? throw new IOException("The process cannot access the file because it is being used by another process.")
                : _inner.ReadAllLines(path, encoding);

        // Alle overige IFile-leden delegeren naar _inner
        public void AppendAllBytes(string path, byte[] bytes) => _inner.AppendAllBytes(path, bytes);
        public void AppendAllBytes(string path, ReadOnlySpan<byte> bytes) => _inner.AppendAllBytes(path, bytes);
        public Task AppendAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken) => _inner.AppendAllBytesAsync(path, bytes, cancellationToken);
        public Task AppendAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => _inner.AppendAllBytesAsync(path, bytes, cancellationToken);
        public void AppendAllLines(string path, IEnumerable<string> contents) => _inner.AppendAllLines(path, contents);
        public void AppendAllLines(string path, IEnumerable<string> contents, Encoding encoding) => _inner.AppendAllLines(path, contents, encoding);
        public Task AppendAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken) => _inner.AppendAllLinesAsync(path, contents, cancellationToken);
        public Task AppendAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken) => _inner.AppendAllLinesAsync(path, contents, encoding, cancellationToken);
        public void AppendAllText(string path, string contents) => _inner.AppendAllText(path, contents);
        public void AppendAllText(string path, string contents, Encoding encoding) => _inner.AppendAllText(path, contents, encoding);
        public void AppendAllText(string path, ReadOnlySpan<char> contents) => _inner.AppendAllText(path, contents);
        public void AppendAllText(string path, ReadOnlySpan<char> contents, Encoding encoding) => _inner.AppendAllText(path, contents, encoding);
        public Task AppendAllTextAsync(string path, string contents, CancellationToken cancellationToken) => _inner.AppendAllTextAsync(path, contents, cancellationToken);
        public Task AppendAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken) => _inner.AppendAllTextAsync(path, contents, encoding, cancellationToken);
        public Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, CancellationToken cancellationToken) => _inner.AppendAllTextAsync(path, contents, cancellationToken);
        public Task AppendAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding, CancellationToken cancellationToken) => _inner.AppendAllTextAsync(path, contents, encoding, cancellationToken);
        public StreamWriter AppendText(string path) => _inner.AppendText(path);
        public void Copy(string sourceFileName, string destFileName) => _inner.Copy(sourceFileName, destFileName);
        public void Copy(string sourceFileName, string destFileName, bool overwrite) => _inner.Copy(sourceFileName, destFileName, overwrite);
        public FileSystemStream Create(string path) => _inner.Create(path);
        public FileSystemStream Create(string path, int bufferSize) => _inner.Create(path, bufferSize);
        public FileSystemStream Create(string path, int bufferSize, FileOptions options) => _inner.Create(path, bufferSize, options);
        public IFileSystemInfo CreateSymbolicLink(string path, string pathToTarget) => _inner.CreateSymbolicLink(path, pathToTarget);
        public StreamWriter CreateText(string path) => _inner.CreateText(path);
        public void Decrypt(string path) => _inner.Decrypt(path);
        public void Delete(string path) => _inner.Delete(path);
        public void Encrypt(string path) => _inner.Encrypt(path);
        public bool Exists(string? path) => _inner.Exists(path);
        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
        public FileAttributes GetAttributes(SafeFileHandle fileHandle) => _inner.GetAttributes(fileHandle);
        public DateTime GetCreationTime(string path) => _inner.GetCreationTime(path);
        public DateTime GetCreationTime(SafeFileHandle fileHandle) => _inner.GetCreationTime(fileHandle);
        public DateTime GetCreationTimeUtc(string path) => _inner.GetCreationTimeUtc(path);
        public DateTime GetCreationTimeUtc(SafeFileHandle fileHandle) => _inner.GetCreationTimeUtc(fileHandle);
        public DateTime GetLastAccessTime(string path) => _inner.GetLastAccessTime(path);
        public DateTime GetLastAccessTime(SafeFileHandle fileHandle) => _inner.GetLastAccessTime(fileHandle);
        public DateTime GetLastAccessTimeUtc(string path) => _inner.GetLastAccessTimeUtc(path);
        public DateTime GetLastAccessTimeUtc(SafeFileHandle fileHandle) => _inner.GetLastAccessTimeUtc(fileHandle);
        public DateTime GetLastWriteTime(string path) => _inner.GetLastWriteTime(path);
        public DateTime GetLastWriteTime(SafeFileHandle fileHandle) => _inner.GetLastWriteTime(fileHandle);
        public DateTime GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);
        public DateTime GetLastWriteTimeUtc(SafeFileHandle fileHandle) => _inner.GetLastWriteTimeUtc(fileHandle);
        public UnixFileMode GetUnixFileMode(string path) => _inner.GetUnixFileMode(path);
        public UnixFileMode GetUnixFileMode(SafeFileHandle fileHandle) => _inner.GetUnixFileMode(fileHandle);
        public void Move(string sourceFileName, string destFileName) => _inner.Move(sourceFileName, destFileName);
        public void Move(string sourceFileName, string destFileName, bool overwrite) => _inner.Move(sourceFileName, destFileName, overwrite);
        public FileSystemStream Open(string path, FileMode mode) => _inner.Open(path, mode);
        public FileSystemStream Open(string path, FileMode mode, FileAccess access) => _inner.Open(path, mode, access);
        public FileSystemStream Open(string path, FileMode mode, FileAccess access, FileShare share) => _inner.Open(path, mode, access, share);
        public FileSystemStream Open(string path, FileStreamOptions options) => _inner.Open(path, options);
        public FileSystemStream OpenRead(string path) => _inner.OpenRead(path);
        public StreamReader OpenText(string path) => _inner.OpenText(path);
        public FileSystemStream OpenWrite(string path) => _inner.OpenWrite(path);
        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
        public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) => _inner.ReadAllBytesAsync(path, cancellationToken);
        public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken) => _inner.ReadAllLinesAsync(path, cancellationToken);
        public Task<string[]> ReadAllLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken) => _inner.ReadAllLinesAsync(path, encoding, cancellationToken);
        public string ReadAllText(string path) => _inner.ReadAllText(path);
        public string ReadAllText(string path, Encoding encoding) => _inner.ReadAllText(path, encoding);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) => _inner.ReadAllTextAsync(path, cancellationToken);
        public Task<string> ReadAllTextAsync(string path, Encoding encoding, CancellationToken cancellationToken) => _inner.ReadAllTextAsync(path, encoding, cancellationToken);
        public IEnumerable<string> ReadLines(string path) => _inner.ReadLines(path);
        public IEnumerable<string> ReadLines(string path, Encoding encoding) => _inner.ReadLines(path, encoding);
        public IAsyncEnumerable<string> ReadLinesAsync(string path, CancellationToken cancellationToken) => _inner.ReadLinesAsync(path, cancellationToken);
        public IAsyncEnumerable<string> ReadLinesAsync(string path, Encoding encoding, CancellationToken cancellationToken) => _inner.ReadLinesAsync(path, encoding, cancellationToken);
        public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName) => _inner.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
        public void Replace(string sourceFileName, string destinationFileName, string destinationBackupFileName, bool ignoreMetadataErrors) => _inner.Replace(sourceFileName, destinationFileName, destinationBackupFileName, ignoreMetadataErrors);
        public IFileSystemInfo ResolveLinkTarget(string linkPath, bool returnFinalTarget) => _inner.ResolveLinkTarget(linkPath, returnFinalTarget);
        public void SetAttributes(string path, FileAttributes fileAttributes) => _inner.SetAttributes(path, fileAttributes);
        public void SetAttributes(SafeFileHandle fileHandle, FileAttributes fileAttributes) => _inner.SetAttributes(fileHandle, fileAttributes);
        public void SetCreationTime(string path, DateTime creationTime) => _inner.SetCreationTime(path, creationTime);
        public void SetCreationTime(SafeFileHandle fileHandle, DateTime creationTime) => _inner.SetCreationTime(fileHandle, creationTime);
        public void SetCreationTimeUtc(string path, DateTime creationTimeUtc) => _inner.SetCreationTimeUtc(path, creationTimeUtc);
        public void SetCreationTimeUtc(SafeFileHandle fileHandle, DateTime creationTimeUtc) => _inner.SetCreationTimeUtc(fileHandle, creationTimeUtc);
        public void SetLastAccessTime(string path, DateTime lastAccessTime) => _inner.SetLastAccessTime(path, lastAccessTime);
        public void SetLastAccessTime(SafeFileHandle fileHandle, DateTime lastAccessTime) => _inner.SetLastAccessTime(fileHandle, lastAccessTime);
        public void SetLastAccessTimeUtc(string path, DateTime lastAccessTimeUtc) => _inner.SetLastAccessTimeUtc(path, lastAccessTimeUtc);
        public void SetLastAccessTimeUtc(SafeFileHandle fileHandle, DateTime lastAccessTimeUtc) => _inner.SetLastAccessTimeUtc(fileHandle, lastAccessTimeUtc);
        public void SetLastWriteTime(string path, DateTime lastWriteTime) => _inner.SetLastWriteTime(path, lastWriteTime);
        public void SetLastWriteTime(SafeFileHandle fileHandle, DateTime lastWriteTime) => _inner.SetLastWriteTime(fileHandle, lastWriteTime);
        public void SetLastWriteTimeUtc(string path, DateTime lastWriteTimeUtc) => _inner.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        public void SetLastWriteTimeUtc(SafeFileHandle fileHandle, DateTime lastWriteTimeUtc) => _inner.SetLastWriteTimeUtc(fileHandle, lastWriteTimeUtc);
        public void SetUnixFileMode(string path, UnixFileMode mode) => _inner.SetUnixFileMode(path, mode);
        public void SetUnixFileMode(SafeFileHandle fileHandle, UnixFileMode mode) => _inner.SetUnixFileMode(fileHandle, mode);
        public void WriteAllBytes(string path, byte[] bytes) => _inner.WriteAllBytes(path, bytes);
        public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes) => _inner.WriteAllBytes(path, bytes);
        public Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken) => _inner.WriteAllBytesAsync(path, bytes, cancellationToken);
        public Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken) => _inner.WriteAllBytesAsync(path, bytes, cancellationToken);
        public void WriteAllLines(string path, string[] contents) => _inner.WriteAllLines(path, contents);
        public void WriteAllLines(string path, IEnumerable<string> contents) => _inner.WriteAllLines(path, contents);
        public void WriteAllLines(string path, string[] contents, Encoding encoding) => _inner.WriteAllLines(path, contents, encoding);
        public void WriteAllLines(string path, IEnumerable<string> contents, Encoding encoding) => _inner.WriteAllLines(path, contents, encoding);
        public Task WriteAllLinesAsync(string path, IEnumerable<string> contents, CancellationToken cancellationToken) => _inner.WriteAllLinesAsync(path, contents, cancellationToken);
        public Task WriteAllLinesAsync(string path, IEnumerable<string> contents, Encoding encoding, CancellationToken cancellationToken) => _inner.WriteAllLinesAsync(path, contents, encoding, cancellationToken);
        public void WriteAllText(string path, string contents) => _inner.WriteAllText(path, contents);
        public void WriteAllText(string path, string contents, Encoding encoding) => _inner.WriteAllText(path, contents, encoding);
        public void WriteAllText(string path, ReadOnlySpan<char> contents) => _inner.WriteAllText(path, contents);
        public void WriteAllText(string path, ReadOnlySpan<char> contents, Encoding encoding) => _inner.WriteAllText(path, contents, encoding);
        public Task WriteAllTextAsync(string path, string contents, CancellationToken cancellationToken) => _inner.WriteAllTextAsync(path, contents, cancellationToken);
        public Task WriteAllTextAsync(string path, string contents, Encoding encoding, CancellationToken cancellationToken) => _inner.WriteAllTextAsync(path, contents, encoding, cancellationToken);
        public Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, CancellationToken cancellationToken) => _inner.WriteAllTextAsync(path, contents, cancellationToken);
        public Task WriteAllTextAsync(string path, ReadOnlyMemory<char> contents, Encoding encoding, CancellationToken cancellationToken) => _inner.WriteAllTextAsync(path, contents, encoding, cancellationToken);
    }
}
