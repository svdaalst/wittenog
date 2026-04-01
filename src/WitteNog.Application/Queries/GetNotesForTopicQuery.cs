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

    public async Task<IReadOnlyList<AtomicNote>> Handle(
        GetNotesForTopicQuery request, CancellationToken ct)
    {
        var notes = await _repo.FindByWikiLinkAsync(request.VaultPath, request.Topic, ct);
        return notes
            .OrderBy(n => n.Content.Trim() == $"[[{request.Topic}]]" ? 0 : 1)
            .ThenByDescending(n => n.Id)
            .ToList();
    }
}
