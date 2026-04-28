using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Commands;

public class UpdateNoteCommandTests
{
    private static readonly string VaultDir = Path.Combine(Path.GetTempPath(), "vault");

    private static string NoteFile(string name) => Path.Combine(VaultDir, name);

    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<UpdateNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_SingleSection_SavesNormally()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(
            new UpdateNoteCommand(NoteFile("mijn-notitie.md"), "# Mijn Notitie\n\nInhoud."));

        Assert.Single(repo.All);
        Assert.Equal("mijn-notitie", result.Id);
        Assert.Equal("Mijn Notitie", result.Title);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_SplitsIntoSeparateFiles()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Sectie Een\n\nInhoud A.\n\n# Sectie Twee\n\nInhoud B."));

        Assert.Equal(2, repo.All.Count);
        Assert.Contains(repo.All, n => n.Id == "combined");
        Assert.Contains(repo.All, n => n.Id == "combined-sectie-twee");
    }

    [Fact]
    public async Task Handle_MultipleHeadings_ReturnsFirstNote()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        Assert.Equal("combined", result.Id);
        Assert.Equal("Eerste", result.Title);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_NewFileHasWikiLinkBackToOriginal()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        var newNote = repo.All.First(n => n.Id == "combined-tweede");
        Assert.Contains("[[combined]]", newNote.Content);
        Assert.Contains("combined", newNote.WikiLinks);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_OriginalFileHasNoWikiLinksAdded()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        var firstNote = repo.All.First(n => n.Id == "combined");
        Assert.DoesNotContain("[[tweede]]", firstNote.Content);
    }

    [Fact]
    public async Task Handle_WithTabQuery_NewFileHasTabQueryPrefix()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer.",
            TabQuery: "2026-03-19"));

        var newNote = repo.All.First(n => n.Id == "2026-03-19-tweede");
        Assert.StartsWith("2026-03-19-", newNote.Id);
        Assert.Equal("[[2026-03-19]] Tweede", newNote.Title);
        Assert.StartsWith("# [[2026-03-19]] Tweede", newNote.Content);
    }

    [Fact]
    public async Task Handle_WithTabQuery_NewFileHasWikiLinkInTitle()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer.",
            TabQuery: "2026-03-19"));

        var newNote = repo.All.First(n => n.Id == "2026-03-19-tweede");
        Assert.StartsWith("# [[2026-03-19]] Tweede", newNote.Content);
        Assert.Contains("2026-03-19", newNote.WikiLinks);
    }

    [Fact]
    public async Task Handle_WithoutTabQuery_NewFileHasWikiLinkToParent()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Eerste\n\nBody.\n\n# Tweede\n\nMeer."));

        var newNote = repo.All.First(n => n.Id == "combined-tweede");
        Assert.Contains("combined", newNote.WikiLinks);
    }

    [Fact]
    public async Task Handle_MultipleHeadings_SuffixesOnExistingFileCollision()
    {
        // H1 fix: a colliding child slug used to silently drop the new section's
        // content. Now it suffixes "-2" so nothing is lost.
        var existing = new AtomicNote(
            "combined-sectie-twee", NoteFile("combined-sectie-twee.md"), "Sectie Twee",
            "# Sectie Twee\n\nOriginele inhoud.",
            Array.Empty<string>(), DateTimeOffset.UtcNow);
        var repo = new FakeNoteRepository(new[] { existing });
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("combined.md"),
            "# Sectie Een\n\nA.\n\n# Sectie Twee\n\nB."));

        // The pre-existing file is preserved untouched.
        var original = repo.All.First(n => n.Id == "combined-sectie-twee");
        Assert.Contains("Originele inhoud", original.Content);

        // The new section's content lands at a -2 suffix, not silently lost.
        var deduped = repo.All.First(n => n.Id == "combined-sectie-twee-2");
        Assert.Contains("B.", deduped.Content);
    }

    [Fact]
    public async Task Handle_TwoHeadingsWithSameSlug_BothPersist()
    {
        // Different casing / punctuation can produce the same slug. Previously the
        // second one was silently dropped (sections.Skip(1).ExistsAsync skipped, and
        // the parent file got only sections[0]). Now both get distinct files.
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("parent.md"),
            "# Hoofd\n\nA.\n\n# Notes\n\nfirst.\n\n# notes\n\nsecond."));

        Assert.Contains(repo.All, n => n.Id == "parent-notes");
        Assert.Contains(repo.All, n => n.Id == "parent-notes-2");
        var first = repo.All.First(n => n.Id == "parent-notes");
        var second = repo.All.First(n => n.Id == "parent-notes-2");
        Assert.Contains("first.", first.Content);
        Assert.Contains("second.", second.Content);
    }

    [Fact]
    public async Task Handle_HeadingWithUnslugifiableTitle_FoldsBackIntoParent()
    {
        // A heading like "# !!!" produces an empty slug. We can't make a file from it,
        // but we mustn't drop the user's content either — so the section folds back
        // into the parent file as part of sections[0].
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("parent.md"),
            "# Hoofd\n\nA.\n\n# !!!\n\nimportant body."));

        Assert.Single(repo.All);
        var parent = repo.All.Single();
        Assert.Equal("parent", parent.Id);
        Assert.Contains("A.", parent.Content);
        Assert.Contains("important body.", parent.Content);
        Assert.Contains("# !!!", parent.Content);
    }

    [Fact]
    public async Task Handle_WriteOrdering_ParentWrittenBeforeChildren()
    {
        // H2: a crash between the parent shrink and the child writes must not leave
        // the user with duplicate content. We assert this by injecting a wrapper that
        // throws on the SECOND write. With the parent-first ordering, the parent file
        // gets shrunk to sections[0] and no child file lands. Before H2, the child
        // would land first and the parent would still hold all sections — duplication.
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        // Pre-seed the original file so we can observe its post-crash state.
        await repo.WriteAsync(new AtomicNote(
            "combined", NoteFile("combined.md"), "Old",
            "# Sectie Een\n\nA.\n\n# Sectie Twee\n\nB.",
            Array.Empty<string>(), DateTimeOffset.UtcNow));

        var crashing = new ThrowOnSecondWriteRepo(repo);
        var mediator = BuildMediatorWithRepo(crashing, repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(
            new UpdateNoteCommand(
                NoteFile("combined.md"),
                "# Sectie Een\n\nA.\n\n# Sectie Twee\n\nB.")));

        // After the crash: the parent has been shrunk to sections[0]. There is no
        // child file with the duplicated content of sections[1].
        var parent = repo.All.Single(n => n.Id == "combined");
        Assert.Contains("A.", parent.Content);
        Assert.DoesNotContain("Sectie Twee", parent.Content);
        Assert.DoesNotContain(repo.All, n => n.Id == "combined-sectie-twee");
    }

    // Wraps the fake repo and throws on the Nth WriteAsync call. Lets a test simulate
    // a crash partway through the split sequence and observe the resulting state.
    private sealed class ThrowOnSecondWriteRepo : IMarkdownStorage
    {
        private readonly IMarkdownStorage _inner;
        private int _writes;
        public ThrowOnSecondWriteRepo(IMarkdownStorage inner) => _inner = inner;
        public Task<AtomicNote?> ReadAsync(string filePath, CancellationToken ct = default) => _inner.ReadAsync(filePath, ct);
        public Task<bool> ExistsAsync(string filePath, CancellationToken ct = default) => _inner.ExistsAsync(filePath, ct);
        public Task DeleteAsync(string filePath, CancellationToken ct = default) => _inner.DeleteAsync(filePath, ct);
        public IAsyncEnumerable<AtomicNote> ReadAllAsync(string vaultPath, CancellationToken ct = default) => _inner.ReadAllAsync(vaultPath, ct);
        public Task WriteAsync(AtomicNote note, CancellationToken ct = default)
        {
            _writes++;
            if (_writes == 2) throw new InvalidOperationException("simulated crash");
            return _inner.WriteAsync(note, ct);
        }
    }

    // Builds a mediator where IMarkdownStorage is the crashing wrapper but
    // INoteRepository is still the underlying fake (for the rare command that needs both).
    private static IMediator BuildMediatorWithRepo(IMarkdownStorage storage, FakeNoteRepository fake)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<UpdateNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(storage);
        services.AddSingleton<INoteRepository>(fake);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    [Fact]
    public async Task Handle_ThreeSections_CreatesTwoNewFiles()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await mediator.Send(new UpdateNoteCommand(
            NoteFile("main.md"),
            "# Hoofd\n\nA.\n\n# Sub Een\n\nB.\n\n# Sub Twee\n\nC."));

        Assert.Equal(3, repo.All.Count);
        var subEen = repo.All.First(n => n.Id == "main-sub-een");
        var subTwee = repo.All.First(n => n.Id == "main-sub-twee");
        Assert.Contains("[[main]]", subEen.Content);
        Assert.Contains("[[main]]", subTwee.Content);
    }
}
