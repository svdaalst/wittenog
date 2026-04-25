namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;

public record CreateNoteCommand(string VaultPath, string Title, string InitialContent = "", string ParentSlug = "", string HeadingParentSlug = "")
    : IRequest<AtomicNote>;

public class CreateNoteCommandHandler : IRequestHandler<CreateNoteCommand, AtomicNote>
{
    private readonly IMarkdownStorage _storage;
    private readonly NoteParser _parser;
    private readonly IWikiLinkParser _linkParser;

    public CreateNoteCommandHandler(
        IMarkdownStorage storage, NoteParser parser, IWikiLinkParser linkParser)
    {
        _storage = storage;
        _parser = parser;
        _linkParser = linkParser;
    }

    public async Task<AtomicNote> Handle(CreateNoteCommand request, CancellationToken ct)
    {
        var slug = string.IsNullOrEmpty(request.ParentSlug)
            ? _parser.GenerateSlug(request.Title)
            : $"{request.ParentSlug}-{_parser.GenerateSlug(request.Title)}";
        var filePath = Path.Combine(request.VaultPath, $"{slug}.md");
        string heading;
        if (!string.IsNullOrEmpty(request.ParentSlug))
            heading = $"# [[{request.ParentSlug}]] {request.Title}";
        else if (!string.IsNullOrEmpty(request.HeadingParentSlug))
            heading = $"# [[{request.HeadingParentSlug}]]";
        else
            heading = $"# {request.Title}";
        var content = string.IsNullOrWhiteSpace(request.InitialContent)
            ? $"{heading}\n\n"
            : $"{heading}\n\n{request.InitialContent}";
        var links = _linkParser.ExtractLinks(content);
        var note = new AtomicNote(slug, filePath, request.Title, content, links, DateTimeOffset.UtcNow);
        await _storage.WriteAsync(note, ct);
        return note;
    }
}
