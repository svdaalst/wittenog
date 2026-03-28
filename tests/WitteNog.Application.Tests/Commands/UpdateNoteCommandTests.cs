using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Commands;

public class UpdateNoteCommandTests
{
    private static readonly string VaultDir = Path.Combine(Path.GetTempPath(), "vault");

    private static string NoteFile(string name) => Path.Combine(VaultDir, name);

    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<UpdateNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_SingleSection_SavesNormally()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(
            new UpdateNoteCommand(NoteFile("mijn-notitie.md"), "# Mijn Notitie\n\nInhoud."));

        Assert.Single(repo.All);
        Assert.Equal("mijn-notitie", result.Id);
        Assert.Equal("Mijn Notitie", result.Title);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_SplitsIntoSeparateFiles()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Sectie Een\n\nInhoud A.\n\n# Sectie Twee\n\nInhoud B."));

        Assert.Equal(2, repo.All.Count);
        Assert.Contains(repo.All, n => n.Id == "combined");
        Assert.Contains(repo.All, n => n.Id == "sectie-twee");
    }

    [Fact]
    public async Task Handle_MultipleHeadings_ReturnsFirstNote()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        Assert.Equal("combined", result.Id);
        Assert.Equal("Eerste", result.Title);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_NewFileHasNoBackLinkToOriginal()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        var newNote = repo.All.First(n => n.Id == "tweede");
        Assert.DoesNotContain("[[combined]]", newNote.Content);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_OriginalFileHasNoWikiLinksAdded()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        var firstNote = repo.All.First(n => n.Id == "combined");
        Assert.DoesNotContain("[[tweede]]", firstNote.Content);
    }

    [Fact]
    public async Task Handle_WithTabQuery_NewFileContainsTabLink()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer.",
            TabQuery: "2026-03-19"));

        var newNote = repo.All.First(n => n.Id == "tweede");
        Assert.Contains("[[2026-03-19]]", newNote.Content);
        Assert.Contains("2026-03-19", newNote.WikiLinks);
    }

    [Fact]
    public async Task Handle_WithoutTabQuery_NewFileHasNoWikiLinks()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        var newNote = repo.All.First(n => n.Id == "tweede");
        Assert.Empty(newNote.WikiLinks);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_SkipsExistingFile()
    {
        var existing = new AtomicNote(
            "sectie-twee", NoteFile("sectie-twee.md"), "Sectie Twee",
            "# Sectie Twee\n\nOriginele inhoud.",
            Array.Empty<string>(), DateTimeOffset.UtcNow);
        var repo = new FakeNoteRepository(new[] { existing });
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Sectie Een\n\nA.\n\n# Sectie Twee\n\nB."));

        var sectionTwo = repo.All.First(n => n.Id == "sectie-twee");
        Assert.Contains("Originele inhoud", sectionTwo.Content);
    }

    [Fact]
    public async Task Handle_ThreeSections_CreatesTwoNewFiles()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("main.md"),
            "# Hoofd\n\nA.\n\n# Sub Een\n\nB.\n\n# Sub Twee\n\nC."));

        Assert.Equal(3, repo.All.Count);
        var subEen = repo.All.First(n => n.Id == "sub-een");
        var subTwee = repo.All.First(n => n.Id == "sub-twee");
        Assert.DoesNotContain("[[main]]", subEen.Content);
        Assert.DoesNotContain("[[main]]", subTwee.Content);
    }
}
