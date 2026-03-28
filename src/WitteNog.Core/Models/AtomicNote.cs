namespace WitteNog.Core.Models;

public record AtomicNote(
    string Id,
    string FilePath,
    string Title,
    string Content,
    IReadOnlyList<string> WikiLinks,
    DateTimeOffset LastModified
);
