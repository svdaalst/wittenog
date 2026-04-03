namespace WitteNog.Core.Models;

public record FlowEdge(
    string Id,
    string FromNodeId,
    string ToNodeId,
    string? Label = null,
    string? FromPort = null,
    string? ToPort = null,
    bool ArrowStart = false,
    bool ArrowEnd = true
);
