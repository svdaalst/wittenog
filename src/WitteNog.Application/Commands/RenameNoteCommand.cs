namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record RenameNoteCommand(string FilePath, string NewSlug, string VaultPath) : IRequest<AtomicNote>;

public class RenameNoteCommandHandler : IRequestHandler<RenameNoteCommand, AtomicNote>
{
    private readonly IMarkdownStorage _storage;
    private readonly IWikiLinkParser _linkParser;

    public RenameNoteCommandHandler(IMarkdownStorage storage, IWikiLinkParser linkParser)
    {
        _storage = storage;
        _linkParser = linkParser;
    }

    public async Task<AtomicNote> Handle(RenameNoteCommand request, CancellationToken ct)
    {
        var oldSlug = Path.GetFileNameWithoutExtension(request.FilePath);
        var vaultDir = Path.GetDirectoryName(request.FilePath)!;
        var newPath = Path.Combine(vaultDir, $"{request.NewSlug}.md");

        var old = await _storage.ReadAsync(request.FilePath, ct);
        var content = old?.Content ?? string.Empty;

        var newNote = new AtomicNote(request.NewSlug, newPath, request.NewSlug,
            content, _linkParser.ExtractLinks(content), DateTimeOffset.UtcNow);
        await _storage.WriteAsync(newNote, ct);
        await _storage.DeleteAsync(request.FilePath, ct);

        // Collect all notes first, then update [[OldSlug]] → [[NewSlug]] to avoid modifying during iteration
        var allNotes = new List<AtomicNote>();
        await foreach (var note in _storage.ReadAllAsync(request.VaultPath, ct))
            allNotes.Add(note);

        foreach (var note in allNotes)
        {
            if (!note.WikiLinks.Contains(oldSlug)) continue;
            var updated = note.Content.Replace($"[[{oldSlug}]]", $"[[{request.NewSlug}]]");
            var updatedLinks = _linkParser.ExtractLinks(updated);
            await _storage.WriteAsync(note with { Content = updated, WikiLinks = updatedLinks, LastModified = DateTimeOffset.UtcNow }, ct);
        }

        return newNote;
    }
}
