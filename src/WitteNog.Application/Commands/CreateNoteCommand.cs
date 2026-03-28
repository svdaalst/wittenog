namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;

public record CreateNoteCommand(string VaultPath, string Title, string InitialContent = "")
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
        var slug = _parser.GenerateSlug(request.Title);
        var filePath = Path.Combine(request.VaultPath, $"{slug}.md");
        var content = string.IsNullOrWhiteSpace(request.InitialContent)
            ? $"# {request.Title}\n\n"
            : $"# {request.Title}\n\n{request.InitialContent}";
        var links = _linkParser.ExtractLinks(content);
        var note = new AtomicNote(slug, filePath, request.Title, content, links, DateTimeOffset.UtcNow);
        await _storage.WriteAsync(note, ct);
        return note;
    }
}
