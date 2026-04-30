namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface IDrawingRepository
{
    IAsyncEnumerable<Drawing> ReadAllAsync(string vaultPath, CancellationToken ct = default);
    Task<IReadOnlyList<Drawing>> FindByWikiLinkAsync(string vaultPath, string link, CancellationToken ct = default);
    Task WriteAsync(Drawing drawing, CancellationToken ct = default);
    Task DeleteAsync(string filePath, CancellationToken ct = default);
}
