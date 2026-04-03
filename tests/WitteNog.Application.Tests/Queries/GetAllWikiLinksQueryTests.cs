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
    private static IMediator BuildMediator(FakeNoteRepository noteRepo, FakeFlowRepository flowRepo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetAllWikiLinksQueryHandler>());
        services.AddSingleton<IMarkdownStorage>(noteRepo);
        services.AddSingleton<INoteRepository>(noteRepo);
        services.AddSingleton<IFlowRepository>(flowRepo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static IMediator BuildMediator(FakeNoteRepository noteRepo) =>
        BuildMediator(noteRepo, new FakeFlowRepository(Array.Empty<FlowDiagram>()));

    private static AtomicNote MakeNote(string id, params string[] links) =>
        new(id, $"/vault/{id}.md", id, "", links, DateTimeOffset.UtcNow);

    private static FlowDiagram MakeFlow(string id, params string[] links) =>
        new(id, $"/vault/{id}.flow", id,
            Array.Empty<FlowNode>(), Array.Empty<FlowEdge>(),
            links, DateTimeOffset.UtcNow);

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

    [Fact]
    public async Task Handle_FlowFilenameLinks_IncludedInResult()
    {
        var noteRepo = new FakeNoteRepository(new[]
        {
            MakeNote("a", "ProjectX")
        });
        var flowRepo = new FakeFlowRepository(new[]
        {
            MakeFlow("[[2026-03-30]] [[ProjectY]]", "2026-03-30", "ProjectY")
        });
        var mediator = BuildMediator(noteRepo, flowRepo);

        var result = await mediator.Send(new GetAllWikiLinksQuery("/vault"));

        Assert.Contains("ProjectX", result);
        Assert.Contains("ProjectY", result);
        Assert.Contains("2026-03-30", result);
    }

    [Fact]
    public async Task Handle_FlowAndNoteShareLink_DeduplicatesIt()
    {
        var noteRepo = new FakeNoteRepository(new[]
        {
            MakeNote("a", "Shared")
        });
        var flowRepo = new FakeFlowRepository(new[]
        {
            MakeFlow("[[Shared]]", "Shared")
        });
        var mediator = BuildMediator(noteRepo, flowRepo);

        var result = await mediator.Send(new GetAllWikiLinksQuery("/vault"));

        Assert.Single(result, l => l == "Shared");
    }
}
