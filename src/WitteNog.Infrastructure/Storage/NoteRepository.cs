namespace WitteNog.Infrastructure.Storage;

using System.IO.Abstractions;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

public class NoteRepository : MarkdownStorageService, INoteRepository
{
    public NoteRepository(IFileSystem fs, IWikiLinkParser linkParser, NoteParser noteParser)
        : base(fs, linkParser, noteParser) { }

    public async Task<IReadOnlyList<AtomicNote>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default)
    {
        var results = new List<AtomicNote>();
        await foreach (var note in ReadAllAsync(vaultPath, ct))
        {
            if (note.WikiLinks.Contains(link))
                results.Add(note);
        }
        return results.AsReadOnly();
    }
}
