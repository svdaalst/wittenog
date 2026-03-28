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
        var content = await Task.Run(() => _fs.File.ReadAllText(filePath), ct);
        var lastWrite = _fs.FileInfo.New(filePath).LastWriteTimeUtc;
        return Parse(filePath, content, lastWrite);
    }

    public async Task WriteAsync(AtomicNote note, CancellationToken ct = default)
    {
        var dir = _fs.Path.GetDirectoryName(note.FilePath)!;
        if (!_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);
        await Task.Run(() => _fs.File.WriteAllText(note.FilePath, note.Content), ct);
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
