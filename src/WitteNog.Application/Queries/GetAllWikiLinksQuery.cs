namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;

public record GetAllWikiLinksQuery(string VaultPath) : IRequest<IReadOnlyList<string>>;

public class GetAllWikiLinksQueryHandler : IRequestHandler<GetAllWikiLinksQuery, IReadOnlyList<string>>
{
    private readonly INoteRepository _repo;

    public GetAllWikiLinksQueryHandler(INoteRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<string>> Handle(GetAllWikiLinksQuery request, CancellationToken ct)
    {
        var links = new HashSet<string>();
        await foreach (var note in _repo.ReadAllAsync(request.VaultPath, ct))
            foreach (var link in note.WikiLinks)
                links.Add(link);
        return links.OrderBy(l => l).ToList().AsReadOnly();
    }
}
