using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;
using WitteNog.Infrastructure.Parsing;

namespace WitteNog.Application.Tests.Commands;

public class AppendTranscriptionCommandTests
{
    private static IMediator BuildMediator(FakeNoteRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<AppendTranscriptionCommandHandler>());
        services.AddSingleton<IMarkdownStorage>(repo);
        services.AddSingleton<INoteRepository>(repo);
        services.AddSingleton<NoteParser>();
        services.AddSingleton<IWikiLinkParser, WikiLinkParser>();
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static AtomicNote MakeNote(string filePath, string content) =>
        new(Path.GetFileNameWithoutExtension(filePath), filePath,
            "Test", content, Array.Empty<string>(), DateTimeOffset.UtcNow);

    // Scenario 1: No ## Transcriptie section — it should be created at the end
    [Fact]
    public async Task Handle_NoSection_CreatesTranscriptieSectionAtEnd()
    {
        const string filePath = "/vault/standup.md";
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(filePath, "# Standup\n\nBesproken: [[ProjectX]].")
        });
        var mediator = BuildMediator(repo);

        await mediator.Send(new AppendTranscriptionCommand(
            filePath, "Volgende sprint start maandag.", DateTimeOffset.Now, null));

        var updated = repo.All.Single();
        Assert.Contains("## Transcriptie", updated.Content);
        Assert.Contains("Volgende sprint start maandag.", updated.Content);
        // Section must appear after the original body
        var bodyEnd = updated.Content.IndexOf("[[ProjectX]].", StringComparison.Ordinal);
        var headerIdx = updated.Content.IndexOf("## Transcriptie", StringComparison.Ordinal);
        Assert.True(headerIdx > bodyEnd);
    }

    // Scenario 2: Section exists — new entry appended inside the section
    [Fact]
    public async Task Handle_ExistingSection_AppendsInsideSection()
    {
        const string filePath = "/vault/standup.md";
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(filePath,
                "# Standup\n\n## Transcriptie\n\n### 09:00\n\nEerste opname.")
        });
        var mediator = BuildMediator(repo);

        await mediator.Send(new AppendTranscriptionCommand(
            filePath, "Tweede opname tekst.", DateTimeOffset.Now, null));

        var updated = repo.All.Single();
        Assert.Contains("Eerste opname.", updated.Content);
        Assert.Contains("Tweede opname tekst.", updated.Content);
        // Both entries must appear after ## Transcriptie
        var sectionIdx = updated.Content.IndexOf("## Transcriptie", StringComparison.Ordinal);
        var entry2Idx  = updated.Content.IndexOf("Tweede opname tekst.", StringComparison.Ordinal);
        Assert.True(entry2Idx > sectionIdx);
    }

    // Scenario 3: Section followed by another ## — entry injected before the next heading
    [Fact]
    public async Task Handle_SectionFollowedByHeading_InjectsBeforeNextHeading()
    {
        const string filePath = "/vault/note.md";
        const string original =
            "# Note\n\n## Transcriptie\n\n### 08:00\n\nTekst.\n\n## Conclusie\n\nSlot.";
        var repo = new FakeNoteRepository(new[] { MakeNote(filePath, original) });
        var mediator = BuildMediator(repo);

        await mediator.Send(new AppendTranscriptionCommand(
            filePath, "Nieuwe invoer.", DateTimeOffset.Now, null));

        var updated = repo.All.Single();
        var sectionIdx   = updated.Content.IndexOf("## Transcriptie", StringComparison.Ordinal);
        var conclusieIdx = updated.Content.IndexOf("## Conclusie", StringComparison.Ordinal);
        var nieuweIdx    = updated.Content.IndexOf("Nieuwe invoer.", StringComparison.Ordinal);

        Assert.True(nieuweIdx > sectionIdx,  "Entry must be after ## Transcriptie");
        Assert.True(nieuweIdx < conclusieIdx, "Entry must be before ## Conclusie");
    }

    // Scenario 4: Content outside the Transcriptie section is preserved exactly
    [Fact]
    public async Task Handle_PreservesContentOutsideTranscriptieSection()
    {
        const string filePath = "/vault/note.md";
        const string originalBody = "# Dagboek\n\nNiet aanraken.\n\n[[2026-03-20]]";
        var repo = new FakeNoteRepository(new[] { MakeNote(filePath, originalBody) });
        var mediator = BuildMediator(repo);

        await mediator.Send(new AppendTranscriptionCommand(
            filePath, "Toegevoegde spraak.", DateTimeOffset.Now, null));

        var updated = repo.All.Single();
        Assert.StartsWith(originalBody, updated.Content);
    }

    // Scenario 5b: AudioFilePath provided → relative link appears in the entry
    [Fact]
    public async Task Handle_WithAudioFilePath_InsertsRelativeLink()
    {
        const string filePath  = "/vault/standup.md";
        const string audioPath = "/vault/recordings/recording-20260320-143022.wav";
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(filePath, "# Standup")
        });
        var mediator = BuildMediator(repo);

        await mediator.Send(new AppendTranscriptionCommand(
            filePath, "Tekst.", DateTimeOffset.Now, audioPath));

        var content = repo.All.Single().Content;
        Assert.Contains("[🔊 Opname](recordings/recording-20260320-143022.wav)", content);
    }

    // Scenario 5c: No AudioFilePath → no link in the entry
    [Fact]
    public async Task Handle_WithoutAudioFilePath_NoLinkInserted()
    {
        const string filePath = "/vault/standup.md";
        var repo = new FakeNoteRepository(new[]
        {
            MakeNote(filePath, "# Standup")
        });
        var mediator = BuildMediator(repo);

        await mediator.Send(new AppendTranscriptionCommand(
            filePath, "Tekst.", DateTimeOffset.Now, null));

        var content = repo.All.Single().Content;
        Assert.DoesNotContain("🔊", content);
    }

    // Scenario 5: File not found → FileNotFoundException
    [Fact]
    public async Task Handle_NoteNotFound_ThrowsFileNotFoundException()
    {
        var repo = new FakeNoteRepository(Array.Empty<AtomicNote>());
        var mediator = BuildMediator(repo);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            mediator.Send(new AppendTranscriptionCommand(
                "/vault/missing.md", "Tekst.", DateTimeOffset.Now, null)));
    }
}
