using System.IO.Abstractions.TestingHelpers;
using WitteNog.Infrastructure.Parsing;
using WitteNog.Infrastructure.Storage;

namespace WitteNog.Infrastructure.Tests.Storage;

public class FlowRepositoryTests
{
    private static FlowRepository BuildSut(MockFileSystem fs) =>
        new(fs, new WikiLinkParser());

    private const string EmptyFlowJson = """{"version":1,"nodes":[],"edges":[]}""";

    // ── WikiLink extraction from filename ──────────────────────────────────

    [Fact]
    public async Task FindByWikiLink_MatchesFilenameLinks()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]] Mijn diagram.flow", new MockFileData(EmptyFlowJson));
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Single(results);
        Assert.Contains("ProjectX", results[0].WikiLinks);
    }

    [Fact]
    public async Task FindByWikiLink_DoesNotMatchContentLinks_OnlyFilename()
    {
        // A node contains [[ProjectX]] in its text, but the filename has no WikiLink
        var json = """{"version":1,"nodes":[{"id":"1","x":0,"y":0,"width":100,"height":60,"text":"[[ProjectX]]","shape":"rect"}],"edges":[]}""";
        var fs = new MockFileSystem();
        fs.AddFile("/vault/Diagram zonder links.flow", new MockFileData(json));
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindByWikiLink_MultipleLinksInFilename_MatchesEach()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[2026-03-30]] [[ProjectX]].flow", new MockFileData(EmptyFlowJson));
        var sut = BuildSut(fs);

        var byDate = await sut.FindByWikiLinkAsync("/vault", "2026-03-30");
        var byTopic = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Single(byDate);
        Assert.Single(byTopic);
        Assert.Equal(byDate[0].FilePath, byTopic[0].FilePath);
    }

    [Fact]
    public async Task FindByWikiLink_NoMatchingLink_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectY]].flow", new MockFileData(EmptyFlowJson));
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Empty(results);
    }

    // ── Title extraction ───────────────────────────────────────────────────

    [Fact]
    public async Task ReadAll_ExtractsTitleFromFilename_ExcludingWikiLinks()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]] Mijn Proces.flow", new MockFileData(EmptyFlowJson));
        var sut = BuildSut(fs);

        var results = new List<WitteNog.Core.Models.FlowDiagram>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Single(results);
        Assert.Equal("Mijn Proces", results[0].Title);
    }

    [Fact]
    public async Task ReadAll_FilenameOnlyWikiLinks_TitleIsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]].flow", new MockFileData(EmptyFlowJson));
        var sut = BuildSut(fs);

        var results = new List<WitteNog.Core.Models.FlowDiagram>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Equal(string.Empty, results[0].Title);
    }

    // ── Read/write round-trip ──────────────────────────────────────────────

    [Fact]
    public async Task ReadAll_SkipsMalformedJsonFiles()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]] valid.flow", new MockFileData(EmptyFlowJson));
        fs.AddFile("/vault/[[ProjectY]] broken.flow", new MockFileData("not json {{{{"));
        var sut = BuildSut(fs);

        var results = new List<WitteNog.Core.Models.FlowDiagram>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Single(results);
    }

    [Fact]
    public async Task ReadAll_EmptyVault_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/vault");
        var sut = BuildSut(fs);

        var results = new List<WitteNog.Core.Models.FlowDiagram>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var fs = new MockFileSystem();
        const string path = "/vault/[[ProjectX]].flow";
        fs.AddFile(path, new MockFileData(EmptyFlowJson));
        var sut = BuildSut(fs);

        await sut.DeleteAsync(path);

        Assert.False(fs.File.Exists(path));
    }
}
