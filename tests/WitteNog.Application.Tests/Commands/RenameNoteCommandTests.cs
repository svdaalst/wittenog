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

    // ---- Slug validation (C3 — security) -------------------------------------------------

    [Theory]
    [InlineData("..\\foo")]      // Windows path traversal
    [InlineData("../foo")]       // POSIX path traversal
    [InlineData("foo/bar")]      // forward slash
    [InlineData("foo\\bar")]     // backslash
    [InlineData("foo:bar")]      // NTFS-illegal
    [InlineData("foo*bar")]      // NTFS-illegal
    [InlineData("foo?bar")]      // NTFS-illegal
    [InlineData("foo\"bar")]     // NTFS-illegal
    [InlineData("foo<bar")]      // NTFS-illegal
    [InlineData("foo|bar")]      // NTFS-illegal
    [InlineData("-leading-hyphen")] // must start alnum
    [InlineData("_leading-underscore")] // must start alnum
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con")]          // Windows reserved
    [InlineData("PRN")]
    [InlineData("aux")]
    [InlineData("nul")]
    [InlineData("com1")]
    [InlineData("LPT9")]
    public void ValidateSlug_RejectsInvalidSlugs(string badSlug)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            RenameNoteCommandHandler.ValidateSlug(badSlug, V("")));
        Assert.NotEmpty(ex.Message);
    }

    [Theory]
    [InlineData("simple")]
    [InlineData("with-hyphen")]
    [InlineData("with_underscore")]
    [InlineData("MixedCase123")]
    [InlineData("a")]              // single char
    [InlineData("0starts-with-digit")]
    public void ValidateSlug_AcceptsValidSlugs(string goodSlug)
    {
        // Should not throw
        RenameNoteCommandHandler.ValidateSlug(goodSlug, V(""));
    }

    [Fact]
    public async Task Handle_RejectsPathTraversalSlug()
    {
        var repo = new FakeNoteRepository(new[] { MakeNote(V("real.md"), "content") });
        var mediator = BuildMediator(repo);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new RenameNoteCommand(V("real.md"), "..\\evil", V(""))));

        // The original note must still exist; nothing was written outside the vault.
        Assert.Contains(repo.All, n => n.FilePath == V("real.md"));
    }

    [Fact]
    public async Task Handle_RejectsReservedName()
    {
        var repo = new FakeNoteRepository(new[] { MakeNote(V("real.md"), "content") });
        var mediator = BuildMediator(repo);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new RenameNoteCommand(V("real.md"), "CON", V(""))));
    }

    // ---- Leading H1 rewrite ---------------------------------------------------------------
    // Renaming a file must also update the in-file "# old-title" line so the displayed
    // title bar follows the rename. Without this, after a vault refresh the title would
    // snap back to the old H1.

    [Fact]
    public async Task Handle_RewritesLeadingH1ToNewSlug()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(V("old-name.md"), "# old-name\n\nbody")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(
            new RenameNoteCommand(V("old-name.md"), "new-name", V("")));

        Assert.StartsWith("# new-name\n", result.Content);
        Assert.Contains("body", result.Content);
    }

    [Fact]
    public async Task Handle_PreservesParentWikilinkInH1()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(V("project-old.md"), "# [[project]] old\n\nbody")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(
            new RenameNoteCommand(V("project-old.md"), "project-new", V("")));

        Assert.StartsWith("# [[project]] project-new\n", result.Content);
    }

    [Fact]
    public async Task Handle_LeavesContentAlone_WhenNoLeadingH1()
    {
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(V("plain.md"), "Just a paragraph, no heading.")
        });
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(
            new RenameNoteCommand(V("plain.md"), "renamed", V("")));

        Assert.Equal("Just a paragraph, no heading.", result.Content);
    }

    [Fact]
    public void RewriteLeadingH1_PreservesCRLF()
    {
        var rewritten = RenameNoteCommandHandler.RewriteLeadingH1(
            "# old\r\n\r\nbody\r\n", "fresh");

        Assert.Equal("# fresh\r\n\r\nbody\r\n", rewritten);
    }

    [Fact]
    public void RewriteLeadingH1_OnlyTouchesFirstH1()
    {
        // A subsequent H1 inside the body must not be rewritten — that's the splitting
        // boundary used by UpdateNoteCommand and would corrupt downstream sections.
        var rewritten = RenameNoteCommandHandler.RewriteLeadingH1(
            "# old\n\nbody\n\n# Another\n\nmore", "fresh");

        Assert.StartsWith("# fresh\n", rewritten);
        Assert.Contains("# Another", rewritten);
    }
}
