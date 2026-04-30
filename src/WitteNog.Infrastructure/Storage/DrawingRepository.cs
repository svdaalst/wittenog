namespace WitteNog.Infrastructure.Storage;

using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public class DrawingRepository : IDrawingRepository
{
    private readonly IFileSystem _fs;
    private readonly IWikiLinkParser _linkParser;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Regex WikiLinkTokenRegex =
        new(@"\[\[[^\]]+\]\]", RegexOptions.Compiled);

    public DrawingRepository(IFileSystem fs, IWikiLinkParser linkParser)
    {
        _fs = fs;
        _linkParser = linkParser;
    }

    public async IAsyncEnumerable<Drawing> ReadAllAsync(
        string vaultPath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_fs.Directory.Exists(vaultPath)) yield break;
        var files = _fs.Directory.GetFiles(vaultPath, "*.drawing", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var drawing = await ReadAsync(file, ct);
            if (drawing != null) yield return drawing;
        }
    }

    public async Task<IReadOnlyList<Drawing>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default)
    {
        var results = new List<Drawing>();
        await foreach (var drawing in ReadAllAsync(vaultPath, ct))
        {
            if (drawing.WikiLinks.Contains(link))
                results.Add(drawing);
        }
        return results.AsReadOnly();
    }

    public async Task WriteAsync(Drawing drawing, CancellationToken ct = default)
    {
        var dir = _fs.Path.GetDirectoryName(drawing.FilePath)!;
        if (string.IsNullOrEmpty(dir)) dir = ".";

        var newFileName = BuildFileName(drawing) + ".drawing";
        var newFilePath = _fs.Path.Combine(dir, newFileName);

        // If the filename changed (e.g. WikiLinks updated), delete the old file first
        if (!string.Equals(drawing.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)
            && _fs.File.Exists(drawing.FilePath))
        {
            _fs.File.Delete(drawing.FilePath);
        }

        if (!_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);

        var dto = new DrawingFileDto(
            Version: 1,
            Background: drawing.Background.ToString().ToLowerInvariant(),
            Width: drawing.Width,
            Height: drawing.Height,
            ImageBase64: drawing.ImageBase64);

        // Atomic write: serialise to a .tmp sidecar then rename over the target.
        // A crash mid-write leaves the original .drawing fully intact rather than
        // truncated. Streaming serialisation avoids materialising the full base64
        // string twice in memory (the imageBase64 field can be several MB).
        var tmpPath = newFilePath + ".tmp";
        await using (var stream = _fs.File.Create(tmpPath))
            await JsonSerializer.SerializeAsync(stream, dto, JsonOptions, ct);

        _fs.File.Move(tmpPath, newFilePath, overwrite: true);
    }

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        if (_fs.File.Exists(filePath))
            _fs.File.Delete(filePath);
        return Task.CompletedTask;
    }

    private async Task<Drawing?> ReadAsync(string filePath, CancellationToken ct)
    {
        if (!_fs.File.Exists(filePath)) return null;
        try
        {
            DrawingFileDto? dto;
            await using (var stream = _fs.File.OpenRead(filePath))
                dto = await JsonSerializer.DeserializeAsync<DrawingFileDto>(stream, JsonOptions, ct);

            if (dto == null) return null;

            var stem = _fs.Path.GetFileNameWithoutExtension(filePath);
            var wikiLinks = _linkParser.ExtractLinks(stem);
            var title = WikiLinkTokenRegex.Replace(stem, "").Trim();
            var lastWrite = _fs.FileInfo.New(filePath).LastWriteTimeUtc;

            return new Drawing(
                Id: stem,
                FilePath: filePath,
                Title: title,
                Background: ParseBackground(dto.Background),
                Width: dto.Width > 0 ? dto.Width : 1920,
                Height: dto.Height > 0 ? dto.Height : 1080,
                ImageBase64: dto.ImageBase64 ?? string.Empty,
                WikiLinks: wikiLinks,
                LastModified: new DateTimeOffset(lastWrite, TimeSpan.Zero));
        }
        catch
        {
            return null;
        }
    }

    private static string BuildFileName(Drawing drawing)
    {
        var parts = drawing.WikiLinks.Select(l => $"[[{l}]]").ToList();
        if (!string.IsNullOrWhiteSpace(drawing.Title))
            parts.Add(drawing.Title.Trim());
        return parts.Count > 0 ? string.Join(" ", parts) : "drawing";
    }

    private static DrawingBackground ParseBackground(string? s) =>
        s?.ToLowerInvariant() switch
        {
            "dot"   => DrawingBackground.Dot,
            "ruled" => DrawingBackground.Ruled,
            _       => DrawingBackground.Plain,
        };

    private record DrawingFileDto(
        int Version,
        string Background,
        int Width,
        int Height,
        string? ImageBase64);
}
