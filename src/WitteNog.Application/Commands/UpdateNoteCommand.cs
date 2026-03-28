namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;

public record UpdateNoteCommand(string FilePath, string NewContent, string? TabQuery = null) : IRequest<AtomicNote>;

public class UpdateNoteCommandHandler : IRequestHandler<UpdateNoteCommand, AtomicNote>
{
    private readonly IMarkdownStorage _storage;
    private readonly NoteParser _parser;
    private readonly IWikiLinkParser _linkParser;

    public UpdateNoteCommandHandler(
        IMarkdownStorage storage, NoteParser parser, IWikiLinkParser linkParser)
    {
        _storage = storage;
        _parser = parser;
        _linkParser = linkParser;
    }

    public async Task<AtomicNote> Handle(UpdateNoteCommand request, CancellationToken ct)
    {
        var sections = _parser.SplitIntoSections(request.NewContent);

        if (sections.Count <= 1)
        {
            var slug = Path.GetFileNameWithoutExtension(request.FilePath);
            var title = _parser.ExtractTitle(request.NewContent);
            var links = _linkParser.ExtractLinks(request.NewContent);
            var note = new AtomicNote(slug, request.FilePath, title, request.NewContent, links, DateTimeOffset.UtcNow);
            await _storage.WriteAsync(note, ct);
            return note;
        }

        var firstSlug = Path.GetFileNameWithoutExtension(request.FilePath);
        var vaultDir = Path.GetDirectoryName(request.FilePath)!;
        var backLink = !string.IsNullOrEmpty(request.TabQuery)
            ? $"\n\n[[{request.TabQuery}]]"
            : string.Empty;

        foreach (var (sectionTitle, sectionContent) in sections.Skip(1))
        {
            var newSlug = _parser.GenerateSlug(sectionTitle);
            var newPath = Path.Combine(vaultDir, $"{newSlug}.md");

            if (await _storage.ExistsAsync(newPath, ct))
                continue;

            var newContent = sectionContent + backLink;
            var newLinks = _linkParser.ExtractLinks(newContent);
            var newNote = new AtomicNote(newSlug, newPath, sectionTitle, newContent, newLinks, DateTimeOffset.UtcNow);
            await _storage.WriteAsync(newNote, ct);
        }

        var firstContent = sections[0].Content;
        var firstTitle = sections[0].Title;
        var firstLinks = _linkParser.ExtractLinks(firstContent);
        var firstNote = new AtomicNote(firstSlug, request.FilePath, firstTitle, firstContent, firstLinks, DateTimeOffset.UtcNow);
        await _storage.WriteAsync(firstNote, ct);
        return firstNote;
    }
}
