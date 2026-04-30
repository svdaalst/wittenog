using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetDrawingsForTopicQueryTests
{
    private static IMediator BuildMediator(IDrawingRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetDrawingsForTopicQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static Drawing MakeDrawing(string id, DateTimeOffset lastModified, params string[] links) =>
        new(id, $"/vault/{id}.drawing", id,
            DrawingBackground.Plain, 1920, 1080, string.Empty,
            links, lastModified);

    [Fact]
    public async Task Handle_ReturnsOnlyDrawingsWithMatchingTopicLink()
    {
        var repo = new FakeDrawingRepository(new[]
        {
            MakeDrawing("drawing-1", DateTimeOffset.UtcNow, "Ontwerp", "2026-04-01"),
            MakeDrawing("drawing-2", DateTimeOffset.UtcNow, "Architectuur"),
            MakeDrawing("drawing-3", DateTimeOffset.UtcNow, "Ontwerp"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetDrawingsForTopicQuery("/vault", "Ontwerp"));

        Assert.Equal(2, result.Count);
        Assert.All(result, d => Assert.Contains("Ontwerp", d.WikiLinks));
    }

    [Fact]
    public async Task Handle_NoMatch_ReturnsEmpty()
    {
        var repo = new FakeDrawingRepository(new[]
        {
            MakeDrawing("drawing-1", DateTimeOffset.UtcNow, "Architectuur")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetDrawingsForTopicQuery("/vault", "Ontwerp"));

        Assert.Empty(result);
    }
}
