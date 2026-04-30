namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetDrawingsForDateQuery(string VaultPath, string Date)
    : IRequest<IReadOnlyList<Drawing>>;

public class GetDrawingsForDateQueryHandler
    : IRequestHandler<GetDrawingsForDateQuery, IReadOnlyList<Drawing>>
{
    private readonly IDrawingRepository _repo;

    public GetDrawingsForDateQueryHandler(IDrawingRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<Drawing>> Handle(
        GetDrawingsForDateQuery request, CancellationToken ct)
    {
        var drawings = await _repo.FindByWikiLinkAsync(request.VaultPath, request.Date, ct);
        return drawings.OrderByDescending(d => d.LastModified).ToList();
    }
}
