namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;

public record GetAllWikiLinksQuery(string VaultPath) : IRequest<IReadOnlyList<string>>;

public class GetAllWikiLinksQueryHandler : IRequestHandler<GetAllWikiLinksQuery, IReadOnlyList<string>>
{
    private readonly INoteRepository _repo;
    private readonly IFlowRepository _flowRepo;

    public GetAllWikiLinksQueryHandler(INoteRepository repo, IFlowRepository flowRepo)
    {
        _repo = repo;
        _flowRepo = flowRepo;
    }

    public async Task<IReadOnlyList<string>> Handle(GetAllWikiLinksQuery request, CancellationToken ct)
    {
        var links = new HashSet<string>();
        await foreach (var note in _repo.ReadAllAsync(request.VaultPath, ct))
            foreach (var link in note.WikiLinks)
                links.Add(link);
        await foreach (var flow in _flowRepo.ReadAllAsync(request.VaultPath, ct))
            foreach (var link in flow.WikiLinks)
                links.Add(link);
        return links.OrderBy(l => l).ToList().AsReadOnly();
    }
}
