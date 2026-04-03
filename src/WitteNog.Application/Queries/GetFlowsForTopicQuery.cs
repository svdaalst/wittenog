namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetFlowsForTopicQuery(string VaultPath, string Topic)
    : IRequest<IReadOnlyList<FlowDiagram>>;

public class GetFlowsForTopicQueryHandler
    : IRequestHandler<GetFlowsForTopicQuery, IReadOnlyList<FlowDiagram>>
{
    private readonly IFlowRepository _repo;

    public GetFlowsForTopicQueryHandler(IFlowRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<FlowDiagram>> Handle(
        GetFlowsForTopicQuery request, CancellationToken ct)
    {
        var flows = await _repo.FindByWikiLinkAsync(request.VaultPath, request.Topic, ct);
        return flows.OrderByDescending(f => f.LastModified).ToList();
    }
}
