using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetNotesForDateQueryTests
{
    private static IMediator BuildMediator(INoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetNotesForDateQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static AtomicNote MakeNote(string id, params string[] links) =>
        new(id, $"/vault/{id}.md", id, $"# {id}", links, DateTimeOffset.UtcNow);

    private static AtomicNote MakeNoteWithContent(string id, string content, params string[] links) =>
        new(id, $"/vault/{id}.md", id, content, links, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_ReturnsNotesWithMatchingDateLink()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote("note-1", "2026-03-18", "ProjectX"),
            MakeNote("note-2", "2026-03-19"),
            MakeNote("note-3", "2026-03-18"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetNotesForDateQuery("/vault", "2026-03-18"));

        Assert.Equal(2, result.Count);
        Assert.All(result, n => Assert.Contains("2026-03-18", n.WikiLinks));
    }

    [Fact]
    public async Task Handle_MainNoteFirst_ThenFilenameDescending()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNoteWithContent("note-c", "# note-c", "2026-03-18"),
            MakeNoteWithContent("2026-03-18", "[[2026-03-18]]", "2026-03-18"),
            MakeNoteWithContent("note-a", "# note-a", "2026-03-18"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetNotesForDateQuery("/vault", "2026-03-18"));

        Assert.Equal(3, result.Count);
        Assert.Equal("2026-03-18", result[0].Id);
        Assert.Equal("note-c", result[1].Id);
        Assert.Equal("note-a", result[2].Id);
    }

    [Fact]
    public async Task Handle_NoMatchingNotes_ReturnsEmpty()
    {
        var repo = new FakeNoteRepository(new[] { MakeNote("note-1", "2026-03-19") });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetNotesForDateQuery("/vault", "2026-03-18"));

        Assert.Empty(result);
    }
}
