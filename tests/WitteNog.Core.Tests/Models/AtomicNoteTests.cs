using WitteNog.Core.Models;

namespace WitteNog.Core.Tests.Models;

public class AtomicNoteTests
{
    [Fact]
    public void AtomicNote_WithWikiLinks_StoresLinksCorrectly()
    {
        var note = new AtomicNote(
            Id: "standup-2026-03-18",
            FilePath: "/vault/standup-2026-03-18.md",
            Title: "Standup 2026-03-18",
            Content: "Besproken met [[ProjectX]] team. Datum: [[2026-03-18]].",
            WikiLinks: new[] { "ProjectX", "2026-03-18" },
            LastModified: DateTimeOffset.Parse("2026-03-18T09:00:00Z")
        );

        Assert.Equal("standup-2026-03-18", note.Id);
        Assert.Contains("ProjectX", note.WikiLinks);
        Assert.Contains("2026-03-18", note.WikiLinks);
    }

    [Fact]
    public void AtomicNote_IsImmutable_ViaRecord()
    {
        var note = new AtomicNote(
            Id: "test",
            FilePath: "/vault/test.md",
            Title: "Test",
            Content: "# Test",
            WikiLinks: Array.Empty<string>(),
            LastModified: DateTimeOffset.UtcNow
        );

        var updated = note with { Title = "Updated" };

        Assert.Equal("Test", note.Title);
        Assert.Equal("Updated", updated.Title);
    }
}
