namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface IMarkdownStorage
{
    Task<AtomicNote?> ReadAsync(string filePath, CancellationToken ct = default);
    Task WriteAsync(AtomicNote note, CancellationToken ct = default);
    Task DeleteAsync(string filePath, CancellationToken ct = default);
    IAsyncEnumerable<AtomicNote> ReadAllAsync(string vaultPath, CancellationToken ct = default);
    Task<bool> ExistsAsync(string filePath, CancellationToken ct = default);
}
