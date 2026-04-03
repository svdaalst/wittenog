namespace WitteNog.Infrastructure.Storage;

using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public class FlowRepository : IFlowRepository
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

    public FlowRepository(IFileSystem fs, IWikiLinkParser linkParser)
    {
        _fs = fs;
        _linkParser = linkParser;
    }

    public async IAsyncEnumerable<FlowDiagram> ReadAllAsync(
        string vaultPath,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!_fs.Directory.Exists(vaultPath)) yield break;
        var files = _fs.Directory.GetFiles(vaultPath, "*.flow", SearchOption.AllDirectories);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var diagram = await ReadAsync(file, ct);
            if (diagram != null) yield return diagram;
        }
    }

    public async Task<IReadOnlyList<FlowDiagram>> FindByWikiLinkAsync(
        string vaultPath, string link, CancellationToken ct = default)
    {
        var results = new List<FlowDiagram>();
        await foreach (var diagram in ReadAllAsync(vaultPath, ct))
        {
            if (diagram.WikiLinks.Contains(link))
                results.Add(diagram);
        }
        return results.AsReadOnly();
    }

    public async Task WriteAsync(FlowDiagram diagram, CancellationToken ct = default)
    {
        var dir = _fs.Path.GetDirectoryName(diagram.FilePath)!;
        if (string.IsNullOrEmpty(dir)) dir = ".";

        var newFileName = BuildFileName(diagram) + ".flow";
        var newFilePath = _fs.Path.Combine(dir, newFileName);

        // If the filename changed (e.g. WikiLinks updated), delete the old file first
        if (!string.Equals(diagram.FilePath, newFilePath, StringComparison.OrdinalIgnoreCase)
            && _fs.File.Exists(diagram.FilePath))
        {
            _fs.File.Delete(diagram.FilePath);
        }

        if (!_fs.Directory.Exists(dir))
            _fs.Directory.CreateDirectory(dir);

        var dto = new FlowFileDto(
            Version: 1,
            Nodes: diagram.Nodes.Select(n => new FlowNodeDto(
                n.Id, n.X, n.Y, n.Width, n.Height, n.Text,
                n.Shape.ToString().ToLowerInvariant())).ToArray(),
            Edges: diagram.Edges.Select(e => new FlowEdgeDto(
                e.Id, e.FromNodeId, e.ToNodeId, e.Label, e.FromPort, e.ToPort, e.ArrowStart, e.ArrowEnd)).ToArray());

        var json = JsonSerializer.Serialize(dto, JsonOptions);
        await Task.Run(() => _fs.File.WriteAllText(newFilePath, json), ct);
    }

    public Task DeleteAsync(string filePath, CancellationToken ct = default)
    {
        if (_fs.File.Exists(filePath))
            _fs.File.Delete(filePath);
        return Task.CompletedTask;
    }

    private async Task<FlowDiagram?> ReadAsync(string filePath, CancellationToken ct)
    {
        if (!_fs.File.Exists(filePath)) return null;
        try
        {
            var json = await Task.Run(() => _fs.File.ReadAllText(filePath), ct);
            var dto = JsonSerializer.Deserialize<FlowFileDto>(json, JsonOptions);
            if (dto == null) return null;

            var stem = _fs.Path.GetFileNameWithoutExtension(filePath);
            var wikiLinks = _linkParser.ExtractLinks(stem);
            var title = WikiLinkTokenRegex.Replace(stem, "").Trim();
            var lastWrite = _fs.FileInfo.New(filePath).LastWriteTimeUtc;

            var nodes = dto.Nodes.Select(n => new FlowNode(
                n.Id, n.X, n.Y, n.Width, n.Height, n.Text,
                ParseShape(n.Shape))).ToList();
            var edges = dto.Edges.Select(e => new FlowEdge(
                e.Id, e.FromNodeId, e.ToNodeId, e.Label, e.FromPort, e.ToPort, e.ArrowStart, e.ArrowEnd)).ToList();

            return new FlowDiagram(
                Id: stem,
                FilePath: filePath,
                Title: title,
                Nodes: nodes,
                Edges: edges,
                WikiLinks: wikiLinks,
                LastModified: new DateTimeOffset(lastWrite, TimeSpan.Zero));
        }
        catch
        {
            return null;
        }
    }

    private static string BuildFileName(FlowDiagram diagram)
    {
        var parts = diagram.WikiLinks.Select(l => $"[[{l}]]").ToList();
        if (!string.IsNullOrWhiteSpace(diagram.Title))
            parts.Add(diagram.Title.Trim());
        return parts.Count > 0 ? string.Join(" ", parts) : "diagram";
    }

    private static NodeShape ParseShape(string shape) => shape.ToLowerInvariant() switch
    {
        "diamond" => NodeShape.Diamond,
        "ellipse" => NodeShape.Ellipse,
        _ => NodeShape.Rect,
    };

    private record FlowFileDto(int Version, FlowNodeDto[] Nodes, FlowEdgeDto[] Edges);
    private record FlowNodeDto(string Id, double X, double Y, double Width, double Height, string Text, string Shape);
    private record FlowEdgeDto(string Id, string FromNodeId, string ToNodeId, string? Label, string? FromPort = null, string? ToPort = null, bool ArrowStart = false, bool ArrowEnd = true);
}
