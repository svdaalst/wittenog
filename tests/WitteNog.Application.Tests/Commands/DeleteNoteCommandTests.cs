using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
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
    private static IMediator BuildMediator(FakeNoteRepository repo, IFileSystem? fs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<DeleteNoteCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        services.AddSingleton<IFileSystem>(fs ?? new MockFileSystem());
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static AtomicNote MakeNote(string filePath, string content = "# Test") =>
        new(Path.GetFileNameWithoutExtension(filePath), filePath,
            "Test", content, Array.Empty<string>(), DateTimeOffset.UtcNow);

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

    // ─── M8: attachment garbage collection ────────────────────────────────────────

    [Fact]
    public async Task Handle_DeletesOrphanedAttachments()
    {
        const string vaultPath = "/vault";
        const string notePath = "/vault/screenshots.md";
        const string attAbsPath = "/vault/attachments/screenshots-1234.png";

        var fs = new MockFileSystem();
        fs.AddFile(attAbsPath, new MockFileData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })); // fake PNG

        var note = MakeNote(notePath, $"# Screens\n\n![](attachments/screenshots-1234.png)");
        var repo = new FakeNoteRepository(new[] { note });
        var mediator = BuildMediator(repo, fs);

        await mediator.Send(new DeleteNoteCommand(notePath, vaultPath));

        Assert.False(fs.File.Exists(attAbsPath), "Orphaned attachment should have been deleted");
    }

    [Fact]
    public async Task Handle_KeepsAttachmentsReferencedByOtherNotes()
    {
        const string vaultPath = "/vault";
        const string targetPath = "/vault/target.md";
        const string otherPath = "/vault/other.md";
        const string attAbsPath = "/vault/attachments/shared-9876.png";

        var fs = new MockFileSystem();
        fs.AddFile(attAbsPath, new MockFileData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        var target = MakeNote(targetPath, $"# Target\n\n![](attachments/shared-9876.png)");
        var other  = MakeNote(otherPath,  $"# Other\n\n![](attachments/shared-9876.png) too");
        var repo   = new FakeNoteRepository(new[] { target, other });
        var mediator = BuildMediator(repo, fs);

        await mediator.Send(new DeleteNoteCommand(targetPath, vaultPath));

        Assert.True(fs.File.Exists(attAbsPath),
            "Attachment shared with another note must NOT be deleted");
    }

    [Fact]
    public async Task Handle_NoVaultPath_SkipsAttachmentGc()
    {
        // Backward-compat path: callers that don't pass VaultPath get the old behaviour
        // (no GC). The attachment file should remain even though only the deleted note
        // referenced it — because we have no vault root to scan from.
        const string notePath = "/vault/legacy.md";
        const string attAbsPath = "/vault/attachments/orphan-5555.png";

        var fs = new MockFileSystem();
        fs.AddFile(attAbsPath, new MockFileData(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        var note = MakeNote(notePath, $"# Legacy\n\n![](attachments/orphan-5555.png)");
        var repo = new FakeNoteRepository(new[] { note });
        var mediator = BuildMediator(repo, fs);

        await mediator.Send(new DeleteNoteCommand(notePath));

        Assert.True(fs.File.Exists(attAbsPath));
    }

    [Fact]
    public async Task Handle_NoteWithoutAttachments_NoFileSystemCalls()
    {
        // A note that doesn't reference attachments/ must complete cleanly; we don't
        // want a stray ReadAllAsync over the whole vault for nothing.
        var fs = new MockFileSystem();
        var note = MakeNote("/vault/plain.md", "# Plain\n\nNo images here.");
        var repo = new FakeNoteRepository(new[] { note });
        var mediator = BuildMediator(repo, fs);

        await mediator.Send(new DeleteNoteCommand("/vault/plain.md", "/vault"));

        Assert.Empty(repo.All);
    }
}
