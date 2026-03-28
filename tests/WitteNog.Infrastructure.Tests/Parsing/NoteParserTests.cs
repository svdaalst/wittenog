using WitteNog.Core.Parsing;

namespace WitteNog.Infrastructure.Tests.Parsing;

public class NoteParserTests
{
    private readonly NoteParser _parser = new();

    [Theory]
    [InlineData("# Standup Notitie\n\nInhoud.", "Standup Notitie")]
    [InlineData("# Eén woord", "Eén woord")]
    [InlineData("Geen koptekst hier.", "")]
    public void ExtractTitle_ReturnsCorrectTitle(string markdown, string expected)
        => Assert.Equal(expected, _parser.ExtractTitle(markdown));

    [Theory]
    [InlineData("Standup Notitie", "standup-notitie")]
    [InlineData("ProjectX Meeting", "projectx-meeting")]
    [InlineData("  spaties  ", "spaties")]
    [InlineData("Café & Bar", "caf-bar")]
    public void GenerateSlug_ReturnsCorrectSlug(string title, string expected)
        => Assert.Equal(expected, _parser.GenerateSlug(title));

    [Fact]
    public void SplitIntoSections_NoHeading_ReturnsSingleSection()
    {
        var md = "Gewone tekst zonder kop.";
        var result = _parser.SplitIntoSections(md);
        Assert.Single(result);
        Assert.Equal(string.Empty, result[0].Title);
        Assert.Equal(md, result[0].Content);
    }

    [Fact]
    public void SplitIntoSections_SingleHeading_ReturnsSingleSection()
    {
        var md = "# Enige Sectie\n\nIets inhoud.";
        var result = _parser.SplitIntoSections(md);
        Assert.Single(result);
        Assert.Equal("Enige Sectie", result[0].Title);
        Assert.Equal(md, result[0].Content);
    }

    [Fact]
    public void SplitIntoSections_TwoHeadings_ReturnsTwoSections()
    {
        var md = "# Sectie A\n\nInhoud A.\n\n# Sectie B\n\nInhoud B.";
        var result = _parser.SplitIntoSections(md);
        Assert.Equal(2, result.Count);
        Assert.Equal("Sectie A", result[0].Title);
        Assert.Equal("Sectie B", result[1].Title);
        Assert.Contains("Inhoud A", result[0].Content);
        Assert.Contains("Inhoud B", result[1].Content);
    }

    [Fact]
    public void SplitIntoSections_EachSectionStartsWithHeadingLine()
    {
        var md = "# Alpha\n\nTekst.\n\n# Beta\n\nMeer.";
        var result = _parser.SplitIntoSections(md);
        Assert.StartsWith("# Alpha", result[0].Content);
        Assert.StartsWith("# Beta", result[1].Content);
    }

    [Fact]
    public void SplitIntoSections_ThreeHeadings_ReturnsThreeSections()
    {
        var md = "# Een\n\nA.\n\n# Twee\n\nB.\n\n# Drie\n\nC.";
        var result = _parser.SplitIntoSections(md);
        Assert.Equal(3, result.Count);
        Assert.Equal("Een", result[0].Title);
        Assert.Equal("Twee", result[1].Title);
        Assert.Equal("Drie", result[2].Title);
    }
}
