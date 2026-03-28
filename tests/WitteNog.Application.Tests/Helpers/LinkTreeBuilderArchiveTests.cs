using WitteNog.Application.Helpers;

namespace WitteNog.Application.Tests.Helpers;

public class LinkTreeBuilderArchiveTests
{
    [Fact]
    public void Build_WithArchivedLinks_SplitsActiveAndArchived()
    {
        var links    = new[] { "A", "B", "C", "D", "E" };
        var archived = new HashSet<string> { "C" };

        var (active, archivedTopics, _) = LinkTreeBuilder.Build(links, archived, DateTimeOffset.Now);

        Assert.Equal(4, active.Count);
        Assert.Single(archivedTopics);
        Assert.DoesNotContain(active,          n => n.FullLink == "C");
        Assert.Contains(archivedTopics, n => n.FullLink == "C");
    }

    [Fact]
    public void Build_WithEmptyArchivedSet_AllLinksAreActive()
    {
        var links    = new[] { "A", "B", "C" };
        var archived = new HashSet<string>();

        var (active, archivedTopics, _) = LinkTreeBuilder.Build(links, archived, DateTimeOffset.Now);

        Assert.Equal(3, active.Count);
        Assert.Empty(archivedTopics);
    }

    [Fact]
    public void Build_DateLinksAreNeverArchived()
    {
        var links    = new[] { "A", "2026-03-19" };
        var archived = new HashSet<string> { "2026-03-19" };

        var (active, archivedTopics, dates) = LinkTreeBuilder.Build(links, archived, DateTimeOffset.Now);

        // Date "2026-03-19" is always in the dates tree, not archived topics
        Assert.Single(active);
        Assert.Empty(archivedTopics);
        Assert.NotNull(dates);
    }

    [Fact]
    public void BackwardCompatOverload_ReturnsAllTopics()
    {
        var links = new[] { "A", "B", "C" };

        var (topics, _) = LinkTreeBuilder.Build(links, DateTimeOffset.Now);

        Assert.Equal(3, topics.Count);
    }
}
