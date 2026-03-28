using System.IO.Abstractions.TestingHelpers;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;
using WitteNog.Infrastructure.Storage;

namespace WitteNog.Infrastructure.Tests.Storage;

public class MarkdownStorageServiceTests
{
    private static MarkdownStorageService BuildSut(MockFileSystem fs) =>
        new(fs, new WikiLinkParser(), new NoteParser());

    [Fact]
    public async Task WriteAsync_CreatesFileWithCorrectContent()
    {
        var fs = new MockFileSystem();
        var sut = BuildSut(fs);
        var note = new AtomicNote(
            Id: "test-note",
            FilePath: "/vault/test-note.md",
            Title: "Test Note",
            Content: "# Test Note\n\nInhoud met [[Link]].",
            WikiLinks: new[] { "Link" },
            LastModified: DateTimeOffset.UtcNow
        );

        await sut.WriteAsync(note);

        Assert.True(fs.FileExists("/vault/test-note.md"));
        Assert.Contains("# Test Note", fs.File.ReadAllText("/vault/test-note.md"));
    }

    [Fact]
    public async Task WriteAsync_CreatesDirectoryIfNotExists()
    {
        var fs = new MockFileSystem();
        var sut = BuildSut(fs);
        var note = new AtomicNote("n", "/new/dir/n.md", "N", "# N", Array.Empty<string>(), DateTimeOffset.UtcNow);

        await sut.WriteAsync(note);

        Assert.True(fs.Directory.Exists("/new/dir"));
    }

    [Fact]
    public async Task ReadAsync_ParsesFileIntoAtomicNote()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/my-note.md", new MockFileData("# My Note\n\nTekst met [[ProjectX]]."));
        var sut = BuildSut(fs);

        var note = await sut.ReadAsync("/vault/my-note.md");

        Assert.NotNull(note);
        Assert.Equal("My Note", note!.Title);
        Assert.Equal("my-note", note.Id);
        Assert.Contains("ProjectX", note.WikiLinks);
    }

    [Fact]
    public async Task ReadAsync_NonExistentFile_ReturnsNull()
    {
        var sut = BuildSut(new MockFileSystem());
        Assert.Null(await sut.ReadAsync("/vault/nonexistent.md"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesFile()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/to-delete.md", new MockFileData("# Delete me"));
        var sut = BuildSut(fs);

        await sut.DeleteAsync("/vault/to-delete.md");

        Assert.False(fs.FileExists("/vault/to-delete.md"));
    }

    [Fact]
    public async Task ReadAllAsync_ReturnsAllMdFiles()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/a.md", new MockFileData("# Note A"));
        fs.AddFile("/vault/b.md", new MockFileData("# Note B"));
        fs.AddFile("/vault/ignore.txt", new MockFileData("not a note"));
        var sut = BuildSut(fs);

        var notes = new List<AtomicNote>();
        await foreach (var note in sut.ReadAllAsync("/vault"))
            notes.Add(note);

        Assert.Equal(2, notes.Count);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentFile_CompletesWithoutError()
    {
        var sut = BuildSut(new MockFileSystem());

        // Should not throw even when the file does not exist
        await sut.DeleteAsync("/vault/ghost.md");
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        var fs = new MockFileSystem();
        fs.AddFile("/vault/exists.md", new MockFileData("# Exists"));
        var sut = BuildSut(fs);

        Assert.True(await sut.ExistsAsync("/vault/exists.md"));
    }

    [Fact]
    public async Task ExistsAsync_MissingFile_ReturnsFalse()
    {
        var sut = BuildSut(new MockFileSystem());

        Assert.False(await sut.ExistsAsync("/vault/missing.md"));
    }
}
