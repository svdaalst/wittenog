namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;
using WitteNog.Core.Parsing;

public record UpdateNoteCommand(string FilePath, string NewContent, string? TabQuery = null) : IRequest<AtomicNote>;

public class UpdateNoteCommandHandler : IRequestHandler<UpdateNoteCommand, AtomicNote>
{
    private readonly IMarkdownStorage _storage;
    private readonly NoteParser _parser;
    private readonly IWikiLinkParser _linkParser;

    public UpdateNoteCommandHandler(
        IMarkdownStorage storage, NoteParser parser, IWikiLinkParser linkParser)
    {
        _storage = storage;
        _parser = parser;
        _linkParser = linkParser;
    }

    public async Task<AtomicNote> Handle(UpdateNoteCommand request, CancellationToken ct)
    {
        var sections = _parser.SplitIntoSections(request.NewContent);

        if (sections.Count <= 1)
        {
            var slug = Path.GetFileNameWithoutExtension(request.FilePath);
            var title = _parser.ExtractTitle(request.NewContent);
            var links = _linkParser.ExtractLinks(request.NewContent);
            var note = new AtomicNote(slug, request.FilePath, title, request.NewContent, links, DateTimeOffset.UtcNow);
            await _storage.WriteAsync(note, ct);
            return note;
        }

        var firstSlug = Path.GetFileNameWithoutExtension(request.FilePath);
        var vaultDir = Path.GetDirectoryName(request.FilePath)!;
        var parentSlug = !string.IsNullOrEmpty(request.TabQuery) ? request.TabQuery : firstSlug;

        // Plan all child splits up front so we can:
        //  - resolve slug collisions deterministically (H1)  — append "-2", "-3"...
        //  - fold un-slugifiable sections (e.g. headings of just "!!!") back into the
        //    parent file instead of silently dropping their content (H1)
        //  - write in a fail-safe order (H2): parent first, then children, so a mid-loop
        //    crash can never leave duplicated content across files
        var plannedSplits = new List<(string Path, string Slug, string Content)>();
        var foldedIntoParent = new System.Text.StringBuilder();

        foreach (var (sectionTitle, sectionContent) in sections.Skip(1))
        {
            var slugPart = _parser.GenerateSlug(sectionTitle);
            if (string.IsNullOrEmpty(slugPart))
            {
                // The heading has no slug-able characters. Don't drop the user's content;
                // append the whole section back onto the parent file. The user can then
                // rename the heading to something slug-able and re-save to split it.
                foldedIntoParent.Append("\n\n").Append(sectionContent);
                continue;
            }

            var baseSlug = $"{parentSlug}-{slugPart}";
            var newSlug = baseSlug;
            var newPath = Path.Combine(vaultDir, $"{newSlug}.md");
            var suffix = 2;

            // ExistsAsync covers files already on disk; the splits-list lookup covers
            // two same-slug headings within the same save (e.g. "# Notes" then "# notes",
            // both → "parent-notes"). Without this second check the first one wins and
            // the second's content is silently lost.
            while (await _storage.ExistsAsync(newPath, ct)
                || plannedSplits.Any(s => string.Equals(s.Path, newPath, StringComparison.OrdinalIgnoreCase)))
            {
                newSlug = $"{baseSlug}-{suffix++}";
                newPath = Path.Combine(vaultDir, $"{newSlug}.md");
            }

            var rewrittenContent = ReplaceFirstHeading(sectionContent, parentSlug, sectionTitle);
            plannedSplits.Add((newPath, newSlug, rewrittenContent));
        }

        // H2: write the parent FIRST. After this point, the parent file contains only
        // sections[0] (+ any folded un-slugifiable sections). If the process crashes
        // before any child is written, on retry SplitIntoSections returns a single
        // section and the early-exit branch above runs cleanly. If the process crashes
        // partway through writing children, the children that already landed are
        // skipped on retry by the ExistsAsync collision check (now suffixed with -2).
        var firstContent = sections[0].Content + foldedIntoParent.ToString();
        var firstTitle = sections[0].Title;
        var firstLinks = _linkParser.ExtractLinks(firstContent);
        var firstNote = new AtomicNote(firstSlug, request.FilePath, firstTitle, firstContent, firstLinks, DateTimeOffset.UtcNow);
        await _storage.WriteAsync(firstNote, ct);

        foreach (var (path, slug, content) in plannedSplits)
        {
            var links = _linkParser.ExtractLinks(content);
            var title = _parser.ExtractTitle(content);
            var newNote = new AtomicNote(slug, path, title, content, links, DateTimeOffset.UtcNow);
            await _storage.WriteAsync(newNote, ct);
        }

        return firstNote;
    }

    private static string ReplaceFirstHeading(string content, string parentSlug, string sectionTitle)
    {
        var newline = content.IndexOf('\n');
        var newHeading = $"# [[{parentSlug}]] {sectionTitle}";
        return newline >= 0
            ? $"{newHeading}{content[newline..]}"
            : newHeading;
    }
}
