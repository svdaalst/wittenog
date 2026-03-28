using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Infrastructure.Tests.Parsing;

public class WikiLinkParserTests
{
    private readonly WikiLinkParser _parser = new();

    [Fact]
    public void ExtractLinks_FindsAllWikiLinks()
    {
        var links = _parser.ExtractLinks("Besproken met [[ProjectX]] team. Datum: [[2026-03-18]].");
        Assert.Equal(2, links.Count);
        Assert.Contains("ProjectX", links);
        Assert.Contains("2026-03-18", links);
    }

    [Fact]
    public void ExtractLinks_NoLinks_ReturnsEmpty()
    {
        var links = _parser.ExtractLinks("Gewone tekst zonder links.");
        Assert.Empty(links);
    }

    [Fact]
    public void IsDateLink_DateString_ReturnsTrue()
        => Assert.True(_parser.IsDateLink("2026-03-18"));

    [Fact]
    public void IsDateLink_TopicString_ReturnsFalse()
        => Assert.False(_parser.IsDateLink("ProjectX"));

    [Fact]
    public void ExtractLinks_DeduplicatesLinks()
    {
        var links = _parser.ExtractLinks("Zie [[ProjectX]] en ook [[ProjectX]].");
        Assert.Single(links);
    }

    [Fact]
    public void ExtractLinks_EmptyString_ReturnsEmpty()
        => Assert.Empty(_parser.ExtractLinks(string.Empty));
}
