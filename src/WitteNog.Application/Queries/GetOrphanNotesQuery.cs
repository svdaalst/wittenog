namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetOrphanNotesQuery(string VaultPath) : IRequest<IReadOnlyList<AtomicNote>>;

public class GetOrphanNotesQueryHandler : IRequestHandler<GetOrphanNotesQuery, IReadOnlyList<AtomicNote>>
{
    private readonly INoteRepository _repo;

    public GetOrphanNotesQueryHandler(INoteRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<AtomicNote>> Handle(GetOrphanNotesQuery request, CancellationToken ct)
    {
        var orphans = new List<AtomicNote>();
        await foreach (var note in _repo.ReadAllAsync(request.VaultPath, ct))
            if (note.WikiLinks.Count == 0)
                orphans.Add(note);
        return orphans.OrderBy(n => n.Title).ToList().AsReadOnly();
    }
}
