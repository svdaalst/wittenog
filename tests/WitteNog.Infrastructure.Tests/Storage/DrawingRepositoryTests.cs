using System.IO.Abstractions.TestingHelpers;
using System.Text.Json;
using WitteNog.Core.Models;
using WitteNog.Infrastructure.Parsing;
using WitteNog.Infrastructure.Storage;

namespace WitteNog.Infrastructure.Tests.Storage;

public class DrawingRepositoryTests
{
    private static DrawingRepository BuildSut(MockFileSystem fs) =>
        new(fs, new WikiLinkParser());

    // A minimal valid .drawing JSON with a transparent 1×1 PNG base64.
    private const string EmptyPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=";

    private static string MakeDrawingJson(string background = "plain", int w = 1920, int h = 1080, string? imageBase64 = null) =>
        JsonSerializer.Serialize(new
        {
            version = 1,
            background,
            width = w,
            height = h,
            imageBase64 = imageBase64 ?? EmptyPng,
        });

    // ── WikiLink extraction from filename ──────────────────────────────────

    [Fact]
    public async Task FindByWikiLink_MatchesFilenameLinks()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]] Mijn tekening.drawing", new MockFileData(MakeDrawingJson()));
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Single(results);
        Assert.Contains("ProjectX", results[0].WikiLinks);
    }

    [Fact]
    public async Task FindByWikiLink_DoesNotMatchContentLinks_OnlyFilename()
    {
        // The imageBase64 field happens to contain "[[ProjectX]]" but the filename has no WikiLink.
        var json = MakeDrawingJson(imageBase64: Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("[[ProjectX]]")));
        var fs = new MockFileSystem();
        fs.AddFile("/vault/Tekening zonder links.drawing", new MockFileData(json));
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindByWikiLink_MultipleLinksInFilename_MatchesEach()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[2026-03-30]] [[ProjectX]].drawing", new MockFileData(MakeDrawingJson()));
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
        fs.AddFile("/vault/[[ProjectY]].drawing", new MockFileData(MakeDrawingJson()));
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Empty(results);
    }

    // ── Title extraction ───────────────────────────────────────────────────

    [Fact]
    public async Task ReadAll_ExtractsTitleFromFilename_ExcludingWikiLinks()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]] Mijn Schets.drawing", new MockFileData(MakeDrawingJson()));
        var sut = BuildSut(fs);

        var results = new List<Drawing>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Single(results);
        Assert.Equal("Mijn Schets", results[0].Title);
    }

    [Fact]
    public async Task ReadAll_FilenameOnlyWikiLinks_TitleIsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]].drawing", new MockFileData(MakeDrawingJson()));
        var sut = BuildSut(fs);

        var results = new List<Drawing>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Equal(string.Empty, results[0].Title);
    }

    // ── Background enum round-trip ─────────────────────────────────────────

    [Theory]
    [InlineData("plain",   DrawingBackground.Plain)]
    [InlineData("dot",     DrawingBackground.Dot)]
    [InlineData("ruled",   DrawingBackground.Ruled)]
    [InlineData("moon",    DrawingBackground.Plain)]   // unknown → Plain
    [InlineData("",        DrawingBackground.Plain)]   // empty → Plain
    public async Task ReadAll_ParsesBackgroundCorrectly(string jsonBg, DrawingBackground expected)
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/test.drawing", new MockFileData(MakeDrawingJson(background: jsonBg)));
        var sut = BuildSut(fs);

        var results = new List<Drawing>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Single(results);
        Assert.Equal(expected, results[0].Background);
    }

    // ── Read/write round-trip ──────────────────────────────────────────────

    [Fact]
    public async Task WriteThenRead_RoundTripsBase64ImageBytes()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/vault");
        var sut = BuildSut(fs);

        var drawing = new Drawing(
            Id: "[[2026-04-30]] test",
            FilePath: "/vault/[[2026-04-30]] test.drawing",
            Title: "test",
            Background: DrawingBackground.Dot,
            Width: 800,
            Height: 600,
            ImageBase64: EmptyPng,
            WikiLinks: new[] { "2026-04-30" },
            LastModified: DateTimeOffset.UtcNow);

        await sut.WriteAsync(drawing);

        var results = new List<Drawing>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Single(results);
        Assert.Equal(EmptyPng, results[0].ImageBase64);
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsBackgroundEnum()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/vault");
        var sut = BuildSut(fs);

        var drawing = new Drawing(
            Id: "ruled-test",
            FilePath: "/vault/ruled-test.drawing",
            Title: "ruled-test",
            Background: DrawingBackground.Ruled,
            Width: 1920,
            Height: 1080,
            ImageBase64: EmptyPng,
            WikiLinks: Array.Empty<string>(),
            LastModified: DateTimeOffset.UtcNow);

        await sut.WriteAsync(drawing);

        var results = new List<Drawing>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Single(results);
        Assert.Equal(DrawingBackground.Ruled, results[0].Background);
    }

    [Fact]
    public async Task ReadAll_SkipsMalformedJsonFiles()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/[[ProjectX]] valid.drawing", new MockFileData(MakeDrawingJson()));
        fs.AddFile("/vault/[[ProjectY]] broken.drawing", new MockFileData("not json {{{{"));
        var sut = BuildSut(fs);

        var results = new List<Drawing>();
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

        var results = new List<Drawing>();
        await foreach (var d in sut.ReadAllAsync("/vault"))
            results.Add(d);

        Assert.Empty(results);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var fs = new MockFileSystem();
        const string path = "/vault/[[ProjectX]].drawing";
        fs.AddFile(path, new MockFileData(MakeDrawingJson()));
        var sut = BuildSut(fs);

        await sut.DeleteAsync(path);

        Assert.False(fs.File.Exists(path));
    }
}
