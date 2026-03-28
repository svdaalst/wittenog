using WitteNog.Application.Helpers;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Helpers;

public class LinkTreeBuilderTests
{
    // ── BuildTopicTree ────────────────────────────────────────────────────────

    [Fact]
    public void BuildTopicTree_EmptyList_ReturnsEmpty()
    {
        var result = LinkTreeBuilder.BuildTopicTree([]);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildTopicTree_FlatLinks_ReturnsSortedLeafNodes()
    {
        var result = LinkTreeBuilder.BuildTopicTree(["Zebra", "Alpha", "Midden"]);

        Assert.Equal(3, result.Count);
        // Nodes are sorted alphabetically
        Assert.Equal("Alpha", result[0].Name);
        Assert.Equal("Midden", result[1].Name);
        Assert.Equal("Zebra", result[2].Name);
        Assert.All(result, n => Assert.False(n.IsFolder));
    }

    [Fact]
    public void BuildTopicTree_HierarchicalPaths_CreatesFolderStructure()
    {
        var result = LinkTreeBuilder.BuildTopicTree(["Projecten/Alpha", "Projecten/Beta"]);

        // One folder named "Projecten" with two children
        Assert.Single(result);
        var folder = result[0];
        Assert.Equal("Projecten", folder.Name);
        Assert.True(folder.IsFolder);
        Assert.Null(folder.FullLink);
        Assert.Equal(2, folder.Children.Count);
        Assert.Equal("Alpha", folder.Children[0].Name);
        Assert.Equal("Projecten/Alpha", folder.Children[0].FullLink);
        Assert.Equal("Beta", folder.Children[1].Name);
        Assert.Equal("Projecten/Beta", folder.Children[1].FullLink);
    }

    [Fact]
    public void BuildTopicTree_DeeplyNested_BuildsFullHierarchy()
    {
        var result = LinkTreeBuilder.BuildTopicTree(["A/B/C"]);

        var a = result[0];
        Assert.Equal("A", a.Name);
        Assert.True(a.IsFolder);

        var b = a.Children[0];
        Assert.Equal("B", b.Name);
        Assert.True(b.IsFolder);

        var c = b.Children[0];
        Assert.Equal("C", c.Name);
        Assert.Equal("A/B/C", c.FullLink);
        Assert.False(c.IsFolder);
    }

    [Fact]
    public void BuildTopicTree_LeafPromotedToFolder_WhenDeeperPathAdded()
    {
        // "A" is a leaf first, then "A/Sub" forces "A" to become a folder.
        // The leaf "A" is replaced by a folder (the standalone link is dropped).
        var result = LinkTreeBuilder.BuildTopicTree(["A", "A/Sub"]);

        Assert.Single(result);
        var folder = result[0];
        Assert.Equal("A", folder.Name);
        Assert.True(folder.IsFolder);
        Assert.Single(folder.Children);
        Assert.Equal("Sub", folder.Children[0].Name);
        Assert.Equal("A/Sub", folder.Children[0].FullLink);
    }

    [Fact]
    public void BuildTopicTree_DuplicateLinks_Deduplicated()
    {
        var result = LinkTreeBuilder.BuildTopicTree(["A", "A", "B"]);

        Assert.Equal(2, result.Count);
    }

    // ── BuildDateTree ─────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now = new(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildDateTree_RecentDates_AppearsUnderAfgelopenWeek()
    {
        var recent = new[] { "2026-03-19", "2026-03-18" };

        var root = LinkTreeBuilder.BuildDateTree(recent, Now);

        Assert.Equal("📅 Datums", root.Name);
        var weekNode = root.Children.FirstOrDefault(c => c.Name == "Afgelopen week");
        Assert.NotNull(weekNode);
        Assert.Equal(2, weekNode!.Children.Count);
    }

    [Fact]
    public void BuildDateTree_OldDates_GroupedByYearAndMonth()
    {
        var old = new[] { "2025-01-10", "2025-01-15", "2024-06-01" };

        var root = LinkTreeBuilder.BuildDateTree(old, Now);

        // No "Afgelopen week" — all dates are old
        Assert.DoesNotContain(root.Children, c => c.Name == "Afgelopen week");

        var year2025 = root.Children.FirstOrDefault(c => c.Name == "2025");
        Assert.NotNull(year2025);
        // January: month node "01 - Januari" with 2 leaves
        var jan = year2025!.Children.FirstOrDefault(c => c.Name.StartsWith("01"));
        Assert.NotNull(jan);
        Assert.Equal(2, jan!.Children.Count);

        var year2024 = root.Children.FirstOrDefault(c => c.Name == "2024");
        Assert.NotNull(year2024);
    }

    [Fact]
    public void BuildDateTree_MixedRecentAndOld_BothSectionsPresent()
    {
        var dates = new[] { "2026-03-19", "2025-06-01" };

        var root = LinkTreeBuilder.BuildDateTree(dates, Now);

        Assert.Contains(root.Children, c => c.Name == "Afgelopen week");
        Assert.Contains(root.Children, c => c.Name == "2025");
    }

    [Fact]
    public void BuildDateTree_DuplicateDates_Deduplicated()
    {
        var dates = new[] { "2026-03-19", "2026-03-19" };

        var root = LinkTreeBuilder.BuildDateTree(dates, Now);

        var weekNode = root.Children.First(c => c.Name == "Afgelopen week");
        Assert.Single(weekNode.Children);
    }

    [Fact]
    public void BuildDateTree_DatesOrderedDescending()
    {
        var dates = new[] { "2026-03-14", "2026-03-16", "2026-03-15" };

        var root = LinkTreeBuilder.BuildDateTree(dates, Now);

        var weekNode = root.Children.First(c => c.Name == "Afgelopen week");
        Assert.Equal("2026-03-16", weekNode.Children[0].Name);
        Assert.Equal("2026-03-15", weekNode.Children[1].Name);
        Assert.Equal("2026-03-14", weekNode.Children[2].Name);
    }

    // ── Build (top-level) ─────────────────────────────────────────────────────

    [Fact]
    public void Build_EmptyLinks_ReturnsEmptyTreesAndNullDates()
    {
        var (active, archived, dates) = LinkTreeBuilder.Build([], new HashSet<string>(), Now);

        Assert.Empty(active);
        Assert.Empty(archived);
        Assert.Null(dates);
    }

    [Fact]
    public void Build_OnlyDateLinks_ReturnsEmptyTopicsAndDateTree()
    {
        var links = new[] { "2026-03-20", "2026-03-19" };

        var (active, archived, dates) = LinkTreeBuilder.Build(links, new HashSet<string>(), Now);

        Assert.Empty(active);
        Assert.Empty(archived);
        Assert.NotNull(dates);
    }

    [Fact]
    public void Build_DuplicateLinks_Deduplicated()
    {
        var links = new[] { "A", "A", "B" };

        var (active, _, _) = LinkTreeBuilder.Build(links, new HashSet<string>(), Now);

        Assert.Equal(2, active.Count);
    }
}
