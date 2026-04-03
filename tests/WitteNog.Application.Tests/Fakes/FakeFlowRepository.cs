using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Fakes;

public class FakeFlowRepository : IFlowRepository
{
    private readonly List<FlowDiagram> _flows;

    public FakeFlowRepository(IEnumerable<FlowDiagram> flows)
        => _flows = flows.ToList();

    public Task<IReadOnlyList<FlowDiagram>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FlowDiagram>>(
            _flows.Where(f => f.WikiLinks.Contains(link)).ToList());

    public async IAsyncEnumerable<FlowDiagram> ReadAllAsync(
        string vaultPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var f in _flows)
        {
            ct.ThrowIfCancellationRequested();
            yield return await Task.FromResult(f);
        }
    }

    public Task WriteAsync(FlowDiagram diagram, CancellationToken ct = default)
    {
        var existing = _flows.FirstOrDefault(f => f.FilePath == diagram.FilePath);
        if (existing != null) _flows.Remove(existing);
        _flows.Add(diagram);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        _flows.RemoveAll(f => f.FilePath == filePath);
        return Task.CompletedTask;
    }

    public IReadOnlyList<FlowDiagram> All => _flows.AsReadOnly();
}
