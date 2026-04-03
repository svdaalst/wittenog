namespace WitteNog.Core.Models;

public record FlowNode(
    string Id,
    double X,
    double Y,
    double Width,
    double Height,
    string Text,
    NodeShape Shape
);

public enum NodeShape { Rect, Diamond, Ellipse }
