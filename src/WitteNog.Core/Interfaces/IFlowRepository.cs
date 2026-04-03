namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface IFlowRepository
{
    IAsyncEnumerable<FlowDiagram> ReadAllAsync(string vaultPath, CancellationToken ct = default);
    Task<IReadOnlyList<FlowDiagram>> FindByWikiLinkAsync(string vaultPath, string link, CancellationToken ct = default);
    Task WriteAsync(FlowDiagram diagram, CancellationToken ct = default);
    Task DeleteAsync(string filePath, CancellationToken ct = default);
}
