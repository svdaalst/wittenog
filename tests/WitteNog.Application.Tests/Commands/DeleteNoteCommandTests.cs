using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Commands;

public class DeleteNoteCommandTests
{
    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<DeleteNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static AtomicNote MakeNote(string filePath) =>
        new(Path.GetFileNameWithoutExtension(filePath), filePath,
            "Test", "# Test", Array.Empty<string>(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_ExistingNote_RemovesItFromRepository()
    {
        const string filePath = "/vault/to-delete.md";
        var repo = new FakeNoteRepository(new[] { MakeNote(filePath) });
        var mediator = BuildMediator(repo);

        await mediator.Send(new DeleteNoteCommand(filePath));

        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task Handle_NonExistentNote_CompletesWithoutError()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        // Should not throw
        await mediator.Send(new DeleteNoteCommand("/vault/missing.md"));
    }

    [Fact]
    public async Task Handle_OnlyTargetNoteIsDeleted_OtherNotesRemain()
    {
        var target = MakeNote("/vault/target.md");
        var other  = MakeNote("/vault/other.md");
        var repo   = new FakeNoteRepository(new[] { target, other });
        var mediator = BuildMediator(repo);

        await mediator.Send(new DeleteNoteCommand("/vault/target.md"));

        Assert.Single(repo.All);
        Assert.Equal("/vault/other.md", repo.All[0].FilePath);
    }
}
