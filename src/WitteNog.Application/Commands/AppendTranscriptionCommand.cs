namespace WitteNog.Application.Commands;

using System.Text.RegularExpressions;
using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;

public record AppendTranscriptionCommand(
    string FilePath,
    string TranscriptionText,
    DateTimeOffset Timestamp,
    string? AudioFilePath = null) : IRequest<AtomicNote>;

public class AppendTranscriptionCommandHandler
    : IRequestHandler<AppendTranscriptionCommand, AtomicNote>
{
    private const string SectionHeader = "## Transcriptie";

    // Matches the start of a H1 or H2 heading — used to find the next section boundary.
    // Does NOT match H3+ so that ### timestamp entries inside Transcriptie are not treated as boundaries.
    private static readonly Regex NextH2OrH1 =
        new(@"^#{1,2}\s", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly IMarkdownStorage _storage;
    private readonly NoteParser _parser;
    private readonly IWikiLinkParser _linkParser;

    public AppendTranscriptionCommandHandler(
        IMarkdownStorage storage, NoteParser parser, IWikiLinkParser linkParser)
    {
        _storage = storage;
        _parser = parser;
        _linkParser = linkParser;
    }

    public async Task<AtomicNote> Handle(
        AppendTranscriptionCommand request, CancellationToken ct)
    {
        var note = await _storage.ReadAsync(request.FilePath, ct)
            ?? throw new FileNotFoundException(
                "Notitiebestand niet gevonden.", request.FilePath);

        var entry = BuildEntry(request.TranscriptionText, request.Timestamp,
            request.AudioFilePath, request.FilePath);
        var newContent = InjectEntry(note.Content, entry);

        var links = _linkParser.ExtractLinks(newContent);
        var updated = note with
        {
            Content = newContent,
            WikiLinks = links,
            LastModified = DateTimeOffset.UtcNow
        };
        await _storage.WriteAsync(updated, ct);
        return updated;
    }

    private static string BuildEntry(
        string text, DateTimeOffset timestamp,
        string? audioFilePath, string noteFilePath)
    {
        var label = timestamp.ToLocalTime().ToString("HH:mm");

        var audioLink = string.Empty;
        if (audioFilePath is not null)
        {
            var noteDir = Path.GetDirectoryName(noteFilePath) ?? string.Empty;
            var relative = Path.GetRelativePath(noteDir, audioFilePath)
                               .Replace('\\', '/');
            audioLink = $"\n[🔊 Opname]({relative})";
        }

        return $"\n\n### {label}{audioLink}\n\n{text.Trim()}\n";
    }

    private static string InjectEntry(string content, string entry)
    {
        var sectionIdx = content.IndexOf(SectionHeader, StringComparison.OrdinalIgnoreCase);

        if (sectionIdx >= 0)
        {
            // Find the next H1/H2 heading after the section header (not inside it)
            var searchFrom = sectionIdx + SectionHeader.Length;
            var nextHeading = NextH2OrH1.Match(content, searchFrom);

            if (nextHeading.Success)
            {
                // Insert before the next heading
                var insertAt = nextHeading.Index;
                return content[..insertAt].TrimEnd() + entry + "\n" + content[insertAt..];
            }
            else
            {
                // Transcriptie is the last section — append at end
                return content.TrimEnd() + entry;
            }
        }
        else
        {
            // No Transcriptie section yet — create one at the bottom
            return content.TrimEnd() + "\n\n" + SectionHeader + entry;
        }
    }
}
