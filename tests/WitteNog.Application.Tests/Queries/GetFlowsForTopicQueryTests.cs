using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetFlowsForTopicQueryTests
{
    private static IMediator BuildMediator(IFlowRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetFlowsForTopicQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static FlowDiagram MakeFlow(string id, DateTimeOffset lastModified, params string[] links) =>
        new(id, $"/vault/{id}.flow", id,
            Array.Empty<FlowNode>(), Array.Empty<FlowEdge>(),
            links, lastModified);

    [Fact]
    public async Task Handle_ReturnsOnlyFlowsWithMatchingWikiLink()
    {
        var repo = new FakeFlowRepository(new[]
        {
            MakeFlow("flow-a", DateTimeOffset.UtcNow, "ProjectX"),
            MakeFlow("flow-b", DateTimeOffset.UtcNow, "ProjectY"),
            MakeFlow("flow-c", DateTimeOffset.UtcNow, "ProjectX", "ProjectY"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetFlowsForTopicQuery("/vault", "ProjectX"));

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Contains("ProjectX", f.WikiLinks));
    }

    [Fact]
    public async Task Handle_SortsByLastModifiedDescending()
    {
        var t1 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var t3 = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

        var repo = new FakeFlowRepository(new[]
        {
            MakeFlow("old", t1, "ProjectX"),
            MakeFlow("newest", t2, "ProjectX"),
            MakeFlow("middle", t3, "ProjectX"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetFlowsForTopicQuery("/vault", "ProjectX"));

        Assert.Equal("newest", result[0].Id);
        Assert.Equal("middle", result[1].Id);
        Assert.Equal("old", result[2].Id);
    }

    [Fact]
    public async Task Handle_EmptyVault_ReturnsEmpty()
    {
        var repo = new FakeFlowRepository(Array.Empty<FlowDiagram>());
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetFlowsForTopicQuery("/vault", "ProjectX"));

        Assert.Empty(result);
    }
}
