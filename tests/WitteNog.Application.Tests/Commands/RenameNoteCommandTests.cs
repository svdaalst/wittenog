using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Commands;

public class RenameNoteCommandTests
{
    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<RenameNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static string V(string name) =>
        Path.Combine(Path.GetTempPath(), "vault-rename-test", name);

    private static AtomicNote MakeNote(string path, string content, IEnumerable<string>? links = null) =>
        new(Path.GetFileNameWithoutExtension(path), path,
            Path.GetFileNameWithoutExtension(path), content,
            (links ?? Array.Empty<string>()).ToList().AsReadOnly(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task Handle_CreatesNoteAtNewPath()
    {
        var repo = new FakeNoteRepository(new[] { MakeNote(V("old-name.md"), "content") });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new RenameNoteCommand(V("old-name.md"), "new-name", V("")));

        Assert.Equal("new-name", result.Id);
        Assert.Equal(V("new-name.md"), result.FilePath);
    }

    [Fact]
    public async Task Handle_DeletesOldFile()
    {
        var repo = new FakeNoteRepository(new[] { MakeNote(V("old-name.md"), "content") });
        var mediator = BuildMediator(repo);

        await mediator.Send(new RenameNoteCommand(V("old-name.md"), "new-name", V("")));

        Assert.DoesNotContain(repo.All, n => n.FilePath == V("old-name.md"));
    }

    [Fact]
    public async Task Handle_PreservesContent()
    {
        const string content = "Some note content here.";
        var repo = new FakeNoteRepository(new[] { MakeNote(V("old.md"), content) });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new RenameNoteCommand(V("old.md"), "new", V("")));

        Assert.Equal(content, result.Content);
    }

    [Fact]
    public async Task Handle_UpdatesWikiLinksInOtherNotes()
    {
        var target = MakeNote(V("original.md"), "# Original");
        var linker = MakeNote(V("other.md"), "See [[original]] for details.", new[] { "original" });
        var repo = new FakeNoteRepository(new[] { target, linker });
        var mediator = BuildMediator(repo);

        await mediator.Send(new RenameNoteCommand(V("original.md"), "renamed", V("")));

        var updated = repo.All.First(n => n.Id == "other");
        Assert.Contains("[[renamed]]", updated.Content);
        Assert.DoesNotContain("[[original]]", updated.Content);
    }

    [Fact]
    public async Task Handle_DoesNotTouchNotesWithoutOldLink()
    {
        var target = MakeNote(V("original.md"), "content");
        var unrelated = MakeNote(V("unrelated.md"), "No links here.");
        var repo = new FakeNoteRepository(new[] { target, unrelated });
        var mediator = BuildMediator(repo);

        await mediator.Send(new RenameNoteCommand(V("original.md"), "renamed", V("")));

        var untouched = repo.All.First(n => n.Id == "unrelated");
        Assert.Equal("No links here.", untouched.Content);
    }
}
