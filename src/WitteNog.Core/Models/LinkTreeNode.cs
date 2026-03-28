namespace WitteNog.Core.Models;

public record LinkTreeNode(
    string Name,
    string? FullLink,
    IReadOnlyList<LinkTreeNode> Children
)
{
    public bool IsFolder => Children.Count > 0;
}
