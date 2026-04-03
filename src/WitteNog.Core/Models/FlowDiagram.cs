namespace WitteNog.Core.Models;

public record FlowDiagram(
    string Id,
    string FilePath,
    string Title,
    IReadOnlyList<FlowNode> Nodes,
    IReadOnlyList<FlowEdge> Edges,
    IReadOnlyList<string> WikiLinks,
    DateTimeOffset LastModified
);
