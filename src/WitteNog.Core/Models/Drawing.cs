namespace WitteNog.Core.Models;

public record Drawing(
    string Id,
    string FilePath,
    string Title,
    DrawingBackground Background,
    int Width,
    int Height,
    string ImageBase64,
    IReadOnlyList<string> WikiLinks,
    DateTimeOffset LastModified
);

public enum DrawingBackground { Plain, Dot, Ruled }
