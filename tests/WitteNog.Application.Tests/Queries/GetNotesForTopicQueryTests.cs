using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetNotesForTopicQueryTests
{
    private static IMediator BuildMediator(INoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetNotesForTopicQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static AtomicNote MakeNote(string id, params string[] links) =>
        new(id, $"/vault/{id}.md", id, $"# {id}", links, DateTimeOffset.UtcNow);

    private static AtomicNote MakeNoteWithContent(string id, string content, params string[] links) =>
        new(id, $"/vault/{id}.md", id, content, links, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_MainNoteFirst_ThenFilenameAscending()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNoteWithContent("note-c", "# note-c", "ProjectX"),
            MakeNoteWithContent("ProjectX", "[[ProjectX]]", "ProjectX"),
            MakeNoteWithContent("note-a", "# note-a", "ProjectX"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetNotesForTopicQuery("/vault", "ProjectX"));

        Assert.Equal(3, result.Count);
        Assert.Equal("ProjectX", result[0].Id);
        Assert.Equal("note-a", result[1].Id);
        Assert.Equal("note-c", result[2].Id);
    }

    [Fact]
    public async Task Handle_ReturnsNotesWithMatchingTopicLink()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote("note-1", "ProjectX", "2026-03-18"),
            MakeNote("note-2", "ProjectY"),
            MakeNote("note-3", "ProjectX"),
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetNotesForTopicQuery("/vault", "ProjectX"));

        Assert.Equal(2, result.Count);
        Assert.All(result, n => Assert.Contains("ProjectX", n.WikiLinks));
    }
}
