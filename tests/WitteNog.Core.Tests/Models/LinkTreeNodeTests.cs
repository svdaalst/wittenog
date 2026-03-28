using WitteNog.Core.Models;

namespace WitteNog.Core.Tests.Models;

public class LinkTreeNodeTests
{
    [Fact]
    public void IsFolder_NodeWithChildren_ReturnsTrue()
    {
        var child = new LinkTreeNode("child", "child", []);
        var folder = new LinkTreeNode("folder", null, new[] { child });

        Assert.True(folder.IsFolder);
    }

    [Fact]
    public void IsFolder_LeafNodeWithNoChildren_ReturnsFalse()
    {
        var leaf = new LinkTreeNode("leaf", "leaf", []);

        Assert.False(leaf.IsFolder);
    }

    [Fact]
    public void LinkTreeNode_IsImmutable_ViaRecord()
    {
        var node = new LinkTreeNode("original", "link", []);
        var renamed = node with { Name = "renamed" };

        Assert.Equal("original", node.Name);
        Assert.Equal("renamed", renamed.Name);
    }
}
