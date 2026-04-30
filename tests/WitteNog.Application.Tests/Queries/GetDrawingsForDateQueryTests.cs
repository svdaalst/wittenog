using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetDrawingsForDateQueryTests
{
    private static IMediator BuildMediator(IDrawingRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetDrawingsForDateQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static Drawing MakeDrawing(string id, DateTimeOffset lastModified, params string[] links) =>
        new(id, $"/vault/{id}.drawing", id,
            DrawingBackground.Plain, 1920, 1080, string.Empty,
            links, lastModified);

    [Fact]
    public async Task Handle_ReturnsOnlyDrawingsWithMatchingDateLink()
    {
        var repo = new FakeDrawingRepository(new[]
        {
            MakeDrawing("drawing-1", DateTimeOffset.UtcNow, "2026-03-30", "ProjectX"),
            MakeDrawing("drawing-2", DateTimeOffset.UtcNow, "2026-04-01"),
            MakeDrawing("drawing-3", DateTimeOffset.UtcNow, "2026-03-30"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetDrawingsForDateQuery("/vault", "2026-03-30"));

        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Contains("2026-03-30", d.WikiLinks));
    }

    [Fact]
    public async Task Handle_NoMatch_ReturnsEmpty()
    {
        var repo = new FakeDrawingRepository(new[]
        {
            MakeDrawing("drawing-1", DateTimeOffset.UtcNow, "2026-04-01")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetDrawingsForDateQuery("/vault", "2026-03-30"));

        Assert.Empty(result);
    }
}
