using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Fakes;

public class FakeDrawingRepository : IDrawingRepository
{
    private readonly List<Drawing> _drawings;

    public FakeDrawingRepository(IEnumerable<Drawing> drawings)
        => _drawings = drawings.ToList();

    public Task<IReadOnlyList<Drawing>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Drawing>>(
            _drawings.Where(d => d.WikiLinks.Contains(link)).ToList());

    public async IAsyncEnumerable<Drawing> ReadAllAsync(
        string vaultPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var d in _drawings)
        {
            ct.ThrowIfCancellationRequested();
            yield return await Task.FromResult(d);
        }
    }

    public Task WriteAsync(Drawing drawing, CancellationToken ct = default)
    {
        var existing = _drawings.FirstOrDefault(d => d.FilePath == drawing.FilePath);
        if (existing != null) _drawings.Remove(existing);
        _drawings.Add(drawing);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        _drawings.RemoveAll(d => d.FilePath == filePath);
        return Task.CompletedTask;
    }

    public IReadOnlyList<Drawing> All => _drawings.AsReadOnly();
}
