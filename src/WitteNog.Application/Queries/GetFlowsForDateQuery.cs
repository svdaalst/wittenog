namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetFlowsForDateQuery(string VaultPath, string Date)
    : IRequest<IReadOnlyList<FlowDiagram>>;

public class GetFlowsForDateQueryHandler
    : IRequestHandler<GetFlowsForDateQuery, IReadOnlyList<FlowDiagram>>
{
    private readonly IFlowRepository _repo;

    public GetFlowsForDateQueryHandler(IFlowRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<FlowDiagram>> Handle(
        GetFlowsForDateQuery request, CancellationToken ct)
    {
        var flows = await _repo.FindByWikiLinkAsync(request.VaultPath, request.Date, ct);
        return flows.OrderByDescending(f => f.LastModified).ToList();
    }
}
