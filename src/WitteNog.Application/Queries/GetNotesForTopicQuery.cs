namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetNotesForTopicQuery(string VaultPath, string Topic)
    : IRequest<IReadOnlyList<AtomicNote>>;

public class GetNotesForTopicQueryHandler
    : IRequestHandler<GetNotesForTopicQuery, IReadOnlyList<AtomicNote>>
{
    private readonly INoteRepository _repo;

    public GetNotesForTopicQueryHandler(INoteRepository repo) => _repo = repo;

    public Task<IReadOnlyList<AtomicNote>> Handle(
        GetNotesForTopicQuery request, CancellationToken ct)
        => _repo.FindByWikiLinkAsync(request.VaultPath, request.Topic, ct);
}
