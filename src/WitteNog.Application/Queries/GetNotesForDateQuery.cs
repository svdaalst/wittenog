namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetNotesForDateQuery(string VaultPath, string Date)
    : IRequest<IReadOnlyList<AtomicNote>>;

public class GetNotesForDateQueryHandler
    : IRequestHandler<GetNotesForDateQuery, IReadOnlyList<AtomicNote>>
{
    private readonly INoteRepository _repo;

    public GetNotesForDateQueryHandler(INoteRepository repo) => _repo = repo;

    public Task<IReadOnlyList<AtomicNote>> Handle(
        GetNotesForDateQuery request, CancellationToken ct)
        => _repo.FindByWikiLinkAsync(request.VaultPath, request.Date, ct);
}
