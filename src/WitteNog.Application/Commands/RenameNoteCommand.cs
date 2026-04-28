namespace WitteNog.Application.Commands;

using System.Text.RegularExpressions;
using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record RenameNoteCommand(string FilePath, string NewSlug, string VaultPath) : IRequest<AtomicNote>;

public class RenameNoteCommandHandler : IRequestHandler<RenameNoteCommand, AtomicNote>
{
    // Slug rules: starts with alphanumeric, then alphanumeric / hyphen / underscore, max 100 chars.
    // This blocks path separators, "..", spaces, and any character invalid on NTFS or POSIX FS.
    private static readonly Regex ValidSlug =
        new(@"^[a-z0-9][a-z0-9\-_]{0,99}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Windows reserved device names — opening or renaming to these throws opaque IOExceptions
    // and on some flows can be exploited to redirect IO. Reject up-front.
    private static readonly HashSet<string> WindowsReservedNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "con", "prn", "aux", "nul",
            "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
            "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
        };

    private readonly IMarkdownStorage _storage;
    private readonly IWikiLinkParser _linkParser;

    public RenameNoteCommandHandler(IMarkdownStorage storage, IWikiLinkParser linkParser)
    {
        _storage = storage;
        _linkParser = linkParser;
    }

    /// <summary>
    /// Validates a rename slug. Throws ArgumentException when the slug is malformed,
    /// reserved, or would resolve to a path outside <paramref name="vaultPath"/>.
    /// Public so the UI can pre-validate before sending the command.
    /// </summary>
    public static void ValidateSlug(string slug, string vaultPath)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Slug mag niet leeg zijn.", nameof(slug));

        if (!ValidSlug.IsMatch(slug))
            throw new ArgumentException(
                $"Ongeldige notitienaam '{slug}'. Toegestaan: letters, cijfers, '-' en '_' (max 100 tekens, begint met letter of cijfer).",
                nameof(slug));

        if (WindowsReservedNames.Contains(slug))
            throw new ArgumentException(
                $"'{slug}' is een gereserveerde Windows-naam.", nameof(slug));

        // Final defense-in-depth: even with the regex above, resolve the would-be path and
        // confirm it stays under the vault root. Catches symlinks, edge cases, future regex
        // bugs, and any Path.Combine quirk.
        var newPath = Path.GetFullPath(Path.Combine(vaultPath, $"{slug}.md"));
        var vaultRoot = Path.GetFullPath(
            vaultPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar);
        if (!newPath.StartsWith(vaultRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Notitienaam '{slug}' resolveert buiten de vault.", nameof(slug));
    }

    // Matches a leading H1 line, optionally with a "[[parent]]" wikilink prefix.
    // Group "parent" captures the prefix incl. trailing space (or empty), group "title"
    // captures the rest of the heading text. Lets us rewrite "# old-name" or
    // "# [[parent]] old-name" → "# new-name" / "# [[parent]] new-name".
    // Uses [ \t]* (not \s*) before the line break so a blank line after the heading is
    // preserved rather than swallowed.
    private static readonly Regex LeadingH1Regex =
        new(@"^#[ \t]+(?<parent>\[\[[^\]]+\]\][ \t]+)?(?<title>.*?)[ \t]*(\r?\n|$)",
            RegexOptions.Compiled);

    public async Task<AtomicNote> Handle(RenameNoteCommand request, CancellationToken ct)
    {
        ValidateSlug(request.NewSlug, request.VaultPath);

        var oldSlug = Path.GetFileNameWithoutExtension(request.FilePath);
        var vaultDir = Path.GetDirectoryName(request.FilePath)!;
        var newPath = Path.Combine(vaultDir, $"{request.NewSlug}.md");

        var old = await _storage.ReadAsync(request.FilePath, ct);
        var content = old?.Content ?? string.Empty;

        // Rewrite the leading "# old-title" line so the displayed title (extracted from
        // the H1 by NoteParser.ExtractTitle on next read) follows the rename. Without
        // this, renaming a file leaves the in-file H1 unchanged → the title bar still
        // shows the old name after a vault refresh. Only the FIRST H1 is touched, and a
        // "[[parent]] " prefix from split notes is preserved.
        content = RewriteLeadingH1(content, request.NewSlug);

        var newNote = new AtomicNote(request.NewSlug, newPath, request.NewSlug,
            content, _linkParser.ExtractLinks(content), DateTimeOffset.UtcNow);
        await _storage.WriteAsync(newNote, ct);
        await _storage.DeleteAsync(request.FilePath, ct);

        // Collect all notes first, then update [[OldSlug]] → [[NewSlug]] to avoid modifying during iteration
        var allNotes = new List<AtomicNote>();
        await foreach (var note in _storage.ReadAllAsync(request.VaultPath, ct))
            allNotes.Add(note);

        foreach (var note in allNotes)
        {
            if (!note.WikiLinks.Contains(oldSlug)) continue;
            var updated = note.Content.Replace($"[[{oldSlug}]]", $"[[{request.NewSlug}]]");
            var updatedLinks = _linkParser.ExtractLinks(updated);
            await _storage.WriteAsync(note with { Content = updated, WikiLinks = updatedLinks, LastModified = DateTimeOffset.UtcNow }, ct);
        }

        return newNote;
    }

    /// <summary>
    /// Rewrites the first H1 line in <paramref name="content"/> so its title text is
    /// <paramref name="newSlug"/>, preserving any "[[parent]]" wikilink prefix.
    /// If there is no leading H1, the content is returned unchanged.
    /// </summary>
    internal static string RewriteLeadingH1(string content, string newSlug)
    {
        if (string.IsNullOrEmpty(content)) return content;

        var match = LeadingH1Regex.Match(content);
        if (!match.Success || match.Index != 0) return content;

        var parent = match.Groups["parent"].Value; // "" or "[[parent]] "
        // The original heading captured everything from "# " through the line break.
        // Replace just that span with the new heading text + the same trailing newline.
        var trailing = match.Length > 0 && match.Value.EndsWith('\n')
            ? (match.Value.EndsWith("\r\n") ? "\r\n" : "\n")
            : string.Empty;
        var rebuilt = $"# {parent}{newSlug}{trailing}";
        return rebuilt + content[match.Length..];
    }
}
