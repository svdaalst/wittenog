namespace WitteNog.Core.Models;

public record FlowEdge(
    string Id,
    string FromNodeId,
    string ToNodeId,
    string? Label = null
);
