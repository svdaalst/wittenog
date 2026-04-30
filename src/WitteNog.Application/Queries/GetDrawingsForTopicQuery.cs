namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetDrawingsForTopicQuery(string VaultPath, string Topic)
    : IRequest<IReadOnlyList<Drawing>>;

public class GetDrawingsForTopicQueryHandler
    : IRequestHandler<GetDrawingsForTopicQuery, IReadOnlyList<Drawing>>
{
    private readonly IDrawingRepository _repo;

    public GetDrawingsForTopicQueryHandler(IDrawingRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<Drawing>> Handle(
        GetDrawingsForTopicQuery request, CancellationToken ct)
    {
        var drawings = await _repo.FindByWikiLinkAsync(request.VaultPath, request.Topic, ct);
        return drawings.OrderByDescending(d => d.LastModified).ToList();
    }
}
