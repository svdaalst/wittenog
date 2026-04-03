namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record CreateFlowCommand(
    string VaultPath,
    IReadOnlyList<string> WikiLinks,
    string Title = "")
    : IRequest<FlowDiagram>;

public class CreateFlowCommandHandler : IRequestHandler<CreateFlowCommand, FlowDiagram>
{
    private readonly IFlowRepository _repo;

    public CreateFlowCommandHandler(IFlowRepository repo) => _repo = repo;

    public async Task<FlowDiagram> Handle(CreateFlowCommand request, CancellationToken ct)
    {
        var parts = request.WikiLinks.Select(l => $"[[{l}]]").ToList();
        if (!string.IsNullOrWhiteSpace(request.Title))
            parts.Add(request.Title.Trim());
        var stem = parts.Count > 0 ? string.Join(" ", parts) : "diagram";
        var filePath = Path.Combine(request.VaultPath, stem + ".flow");

        var diagram = new FlowDiagram(
            Id: stem,
            FilePath: filePath,
            Title: request.Title,
            Nodes: Array.Empty<FlowNode>(),
            Edges: Array.Empty<FlowEdge>(),
            WikiLinks: request.WikiLinks,
            LastModified: DateTimeOffset.UtcNow);

        await _repo.WriteAsync(diagram, ct);
        return diagram;
    }
}
