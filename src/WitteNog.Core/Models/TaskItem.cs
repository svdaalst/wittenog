namespace WitteNog.Core.Models;

public record TaskItem(
    string Id,
    string FilePath,
    int LineNumber,
    string RawLine,
    string Description,
    string? ProjectLink,
    DateOnly? Deadline,
    int? Priority,
    DateTimeOffset LastModified
)
{
    public string? SourceFirstWikiLink { get; init; }
}
