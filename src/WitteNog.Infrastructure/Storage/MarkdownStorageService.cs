namespace WitteNog.Infrastructure.Storage;

using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

public class MarkdownStorageService : IMarkdownStorage
{
    private readonly IFileSystem _fs;
    private readonly IWikiLinkParser _linkParser;
    private readonly NoteParser _noteParser;

    public MarkdownStorageService(IFileSystem fs, IWikiLinkParser linkParser, NoteParser noteParser)
    {
        _fs = fs;
        _linkParser = linkParser;
        _noteParser = noteParser;
    }

    public async Task<AtomicNote?> ReadAsync(string filePath, CancellationToken ct = default)
    {
        if (!_fs.File.Exists(filePath)) return null;
        // Use the genuine async API instead of Task.Run-over-sync: avoids burning a
        // thread-pool thread on blocking IO and properly honours the cancellation token.
        var content = await _fs.File.ReadAllTextAsync(filePath, ct);
        var lastWrite = _fs.FileInfo.New(filePath).LastWriteTimeUtc;
        return Parse(filePath, content, lastWrite);
    }

    public async Task WriteAsync(AtomicNote note, CancellationToken ct = default)
    {
        var dir = _fs.Path.GetDirectoryName(note.FilePath)!;
        if (!_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);

        // H5: atomic write. Without a tmp+move, a crash mid-WriteAllText leaves the
        // user with a truncated or empty .md and the rest of their note gone forever.
        // Writing to a sidecar and then File.Move(overwrite:true) keeps the original
        // file fully readable until the new content is fully on disk. On NTFS Move
        // with overwrite is implemented as ReplaceFile, which is atomic on the same
        // volume.
        var tmpPath = note.FilePath + ".tmp";
        await _fs.File.WriteAllTextAsync(tmpPath, note.Content, ct);
        _fs.File.Move(tmpPath, note.FilePath, overwrite: true);
    }

    public Task<bool> ExistsAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult(_fs.File.Exists(filePath));

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        if (_fs.File.Exists(filePath))
            _fs.File.Delete(filePath);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AtomicNote> ReadAllAsync(
        string vaultPath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = _fs.Directory.GetFiles(vaultPath, "*.md", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var note = await ReadAsync(file, ct);
            if (note != null) yield return note;
        }
    }

    private AtomicNote Parse(string filePath, string content, DateTime lastModified)
    {
        var slug = _fs.Path.GetFileNameWithoutExtension(filePath);
        var title = _noteParser.ExtractTitle(content);
        var links = _linkParser.ExtractLinks(content);
        return new AtomicNote(slug, filePath, title, content, links,
            new DateTimeOffset(lastModified, TimeSpan.Zero));
    }
}
