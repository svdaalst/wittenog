namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record CreateDrawingCommand(
    string VaultPath,
    IReadOnlyList<string> WikiLinks,
    string Title = "")
    : IRequest<Drawing>;

public class CreateDrawingCommandHandler : IRequestHandler<CreateDrawingCommand, Drawing>
{
    // 1×1 transparent PNG — replaced on the first real save.
    private const string EmptyPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    private readonly IDrawingRepository _repo;

    public CreateDrawingCommandHandler(IDrawingRepository repo) => _repo = repo;

    public async Task<Drawing> Handle(CreateDrawingCommand request, CancellationToken ct)
    {
        var parts = request.WikiLinks.Select(l => $"[[{l}]]").ToList();
        if (!string.IsNullOrWhiteSpace(request.Title))
            parts.Add(request.Title.Trim());
        var stem = parts.Count > 0 ? string.Join(" ", parts) : "drawing";
        var filePath = Path.Combine(request.VaultPath, stem + ".drawing");

        var drawing = new Drawing(
            Id: stem,
            FilePath: filePath,
            Title: request.Title,
            Background: DrawingBackground.Plain,
            Width: 1920,
            Height: 1080,
            ImageBase64: EmptyPng,
            WikiLinks: request.WikiLinks,
            LastModified: DateTimeOffset.UtcNow);

        await _repo.WriteAsync(drawing, ct);
        return drawing;
    }
}
