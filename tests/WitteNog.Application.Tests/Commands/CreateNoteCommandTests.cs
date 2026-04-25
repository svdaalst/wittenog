using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Commands;

public class CreateNoteCommandTests
{
    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<CreateNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_CreatesNoteWithCorrectSlugAndTitle()
    {
        var repo = new FakeNoteRepository(Array.Empty<WitteNog.Core.Models.AtomicNote>());
        var mediator = BuildMediator(repo);

        var note = await mediator.Send(
            new CreateNoteCommand("/vault", "Mijn Eerste Notitie"));

        Assert.Equal("mijn-eerste-notitie", note.Id);
        Assert.Equal("Mijn Eerste Notitie", note.Title);
        Assert.StartsWith("# Mijn Eerste Notitie", note.Content);
    }

    [Fact]
    public async Task Handle_WithInitialContent_IncludesWikiLinks()
    {
        var repo = new FakeNoteRepository(Array.Empty<WitteNog.Core.Models.AtomicNote>());
        var mediator = BuildMediator(repo);

        var note = await mediator.Send(
            new CreateNoteCommand("/vault", "Standup", "Besproken: [[ProjectX]]."));

        Assert.Contains("ProjectX", note.WikiLinks);
    }

    [Fact]
    public async Task Handle_WithParentSlug_PrefixesSlugAndAddsWikiLink()
    {
        var repo = new FakeNoteRepository(Array.Empty<WitteNog.Core.Models.AtomicNote>());
        var mediator = BuildMediator(repo);

        var note = await mediator.Send(
            new CreateNoteCommand("/vault", "Budget Planning", ParentSlug: "project-ideas"));

        Assert.Equal("project-ideas-budget-planning", note.Id);
        Assert.Equal("Budget Planning", note.Title);
        Assert.StartsWith("# [[project-ideas]] Budget Planning", note.Content);
        Assert.Contains("project-ideas", note.WikiLinks);
    }

    [Fact]
    public async Task Handle_WithoutParentSlug_NoWikiLinkInHeading()
    {
        var repo = new FakeNoteRepository(Array.Empty<WitteNog.Core.Models.AtomicNote>());
        var mediator = BuildMediator(repo);

        var note = await mediator.Send(
            new CreateNoteCommand("/vault", "Standalone Note"));

        Assert.Equal("standalone-note", note.Id);
        Assert.StartsWith("# Standalone Note", note.Content);
        Assert.DoesNotContain("[[", note.Content);
    }

    [Fact]
    public async Task Handle_WithHeadingParentSlug_KeepsSlugButAddsWikiLinkInHeading()
    {
        var repo = new FakeNoteRepository(Array.Empty<WitteNog.Core.Models.AtomicNote>());
        var mediator = BuildMediator(repo);

        var note = await mediator.Send(
            new CreateNoteCommand("/vault", "frontend", HeadingParentSlug: "frontend"));

        Assert.Equal("frontend", note.Id);
        Assert.StartsWith("# [[frontend]]", note.Content);
        Assert.DoesNotContain("frontend]]", note.Content.Substring(note.Content.IndexOf('\n')));
        Assert.Contains("frontend", note.WikiLinks);
    }

    [Fact]
    public async Task Handle_PersistsNoteToRepository()
    {
        var repo = new FakeNoteRepository(Array.Empty<WitteNog.Core.Models.AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new CreateNoteCommand("/vault", "Test Note"));

        Assert.Single(repo.All);
    }
}
