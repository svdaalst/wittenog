using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetFlowsForDateQueryTests
{
    private static IMediator BuildMediator(IFlowRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetFlowsForDateQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static FlowDiagram MakeFlow(string id, DateTimeOffset lastModified, params string[] links) =>
        new(id, $"/vault/{id}.flow", id,
            Array.Empty<FlowNode>(), Array.Empty<FlowEdge>(),
            links, lastModified);

    [Fact]
    public async Task Handle_ReturnsOnlyFlowsWithMatchingDateLink()
    {
        var repo = new FakeFlowRepository(new[]
        {
            MakeFlow("flow-1", DateTimeOffset.UtcNow, "2026-03-30", "ProjectX"),
            MakeFlow("flow-2", DateTimeOffset.UtcNow, "2026-04-01"),
            MakeFlow("flow-3", DateTimeOffset.UtcNow, "2026-03-30"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetFlowsForDateQuery("/vault", "2026-03-30"));

        Assert.Equal(2, result.Count);
        Assert.All(result, f => Assert.Contains("2026-03-30", f.WikiLinks));
    }

    [Fact]
    public async Task Handle_NoMatch_ReturnsEmpty()
    {
        var repo = new FakeFlowRepository(new[]
        {
            MakeFlow("flow-1", DateTimeOffset.UtcNow, "2026-04-01")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetFlowsForDateQuery("/vault", "2026-03-30"));

        Assert.Empty(result);
    }
}
