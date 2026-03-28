namespace WitteNog.Core.Parsing;

using System.Text.RegularExpressions;

public class NoteParser
{
    private static readonly Regex TitleRegex =
        new(@"^#\s+(.+)$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex SlugInvalidChars =
        new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public string ExtractTitle(string markdown)
    {
        var match = TitleRegex.Match(markdown);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    public string GenerateSlug(string title) =>
        SlugInvalidChars.Replace(title.ToLowerInvariant(), "-").Trim('-');

    public IReadOnlyList<(string Title, string Content)> SplitIntoSections(string markdown)
    {
        var matches = TitleRegex.Matches(markdown);

        if (matches.Count <= 1)
        {
            var title = matches.Count == 1 ? matches[0].Groups[1].Value.Trim() : string.Empty;
            return new[] { (title, markdown) };
        }

        var sections = new List<(string Title, string Content)>(matches.Count);
        for (int i = 0; i < matches.Count; i++)
        {
            var title = matches[i].Groups[1].Value.Trim();
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : markdown.Length;
            var content = markdown[start..end].TrimEnd();
            sections.Add((title, content));
        }
        return sections.AsReadOnly();
    }
}
