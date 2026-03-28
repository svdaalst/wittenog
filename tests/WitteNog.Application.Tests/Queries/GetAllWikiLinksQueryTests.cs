using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Queries;

public class GetAllWikiLinksQueryTests
{
    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetAllWikiLinksQueryHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static AtomicNote MakeNote(string id, params string[] links) =>
        new(id, $"/vault/{id}.md", id, "", links, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_EmptyVault_ReturnsEmptyList()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllWikiLinksQuery("/vault"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_SingleNote_ReturnsItsLinks()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote("a", "Projecten/Solude", "2026-03-19")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllWikiLinksQuery("/vault"));

        Assert.Contains("Projecten/Solude", result);
        Assert.Contains("2026-03-19", result);
    }

    [Fact]
    public async Task Handle_MultipleNotes_DeduplicatesLinks()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote("a", "Projecten/Solude", "2026-03-19"),
            MakeNote("b", "Projecten/Solude", "Algemeen"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllWikiLinksQuery("/vault"));

        Assert.Equal(result.Count, result.Distinct().Count());
        Assert.Contains("Projecten/Solude", result);
        Assert.Contains("Algemeen", result);
    }

    [Fact]
    public async Task Handle_ReturnsLinksSortedAlphabetically()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote("a", "Zebra", "Appel", "Midden")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllWikiLinksQuery("/vault"));

        Assert.Equal(result.OrderBy(l => l).ToList(), result.ToList());
    }
}
