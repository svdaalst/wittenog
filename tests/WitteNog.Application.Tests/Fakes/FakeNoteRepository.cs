using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Fakes;

public class FakeNoteRepository : INoteRepository
{
    private readonly List<AtomicNote> _notes;

    public FakeNoteRepository(IEnumerable<AtomicNote> notes)
        => _notes = notes.ToList();

    public Task<IReadOnlyList<AtomicNote>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AtomicNote>>(
            _notes.Where(n => n.WikiLinks.Contains(link)).ToList());

    public Task<AtomicNote?> ReadAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult(_notes.FirstOrDefault(n => n.FilePath == filePath));

    public Task WriteAsync(AtomicNote note, CancellationToken ct = default)
    {
        var existing = _notes.FirstOrDefault(n => n.FilePath == note.FilePath);
        if (existing != null) _notes.Remove(existing);
        _notes.Add(note);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string filePath, CancellationToken ct = default)
        => Task.FromResult(_notes.Any(n => n.FilePath == filePath));

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        _notes.RemoveAll(n => n.FilePath == filePath);
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<AtomicNote> ReadAllAsync(
        string vaultPath,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var note in _notes)
        {
            ct.ThrowIfCancellationRequested();
            yield return await Task.FromResult(note);
        }
    }

    public IReadOnlyList<AtomicNote> All => _notes.AsReadOnly();
}
