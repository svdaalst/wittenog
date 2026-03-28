namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface INoteRepository : IMarkdownStorage
{
    Task<IReadOnlyList<AtomicNote>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default);
}
