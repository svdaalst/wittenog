using System.IO.Abstractions.TestingHelpers;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;
using WitteNog.Infrastructure.Storage;

namespace WitteNog.Infrastructure.Tests.Storage;

public class NoteRepositoryTests
{
    private static NoteRepository BuildSut(MockFileSystem fs) =>
        new(fs, new WikiLinkParser(), new NoteParser());

    private static MockFileSystem BuildVault(int count, string topic)
    {
        var fs = new MockFileSystem();
        for (int i = 0; i < count; i++)
        {
            var date = $"2026-03-{(i + 1):D2}";
            var content = i % 2 == 0
                ? $"# Note {i}\n\nBesproken met [[{topic}]]. Datum: [[{date}]]."
                : $"# Note {i}\n\nAnders onderwerp. Datum: [[{date}]].";
            fs.AddFile($"/vault/note-{i}.md", new MockFileData(content));
        }
        return fs;
    }

    [Fact]
    public async Task FindByWikiLink_TopicFilter_ReturnsMatchingNotes()
    {
        var fs = BuildVault(10, "ProjectX");
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Equal(5, results.Count); // even-indexed notes: 0,2,4,6,8
    }

    [Fact]
    public async Task FindByWikiLink_DateFilter_ReturnsMatchingNotes()
    {
        var fs = BuildVault(10, "ProjectX");
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "2026-03-01");

        Assert.Single(results); // alleen note-0
    }

    [Fact]
    public async Task FindByWikiLink_NonExistentLink_ReturnsEmpty()
    {
        var fs = BuildVault(10, "ProjectX");
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "OnbestaandOnderwerp");

        Assert.Empty(results);
    }

    [Fact]
    public async Task FindByWikiLink_EmptyVault_ReturnsEmpty()
    {
        var fs = new MockFileSystem();
        fs.AddDirectory("/vault");
        var sut = BuildSut(fs);

        var results = await sut.FindByWikiLinkAsync("/vault", "ProjectX");

        Assert.Empty(results);
    }
}
