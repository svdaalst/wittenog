namespace WitteNog.Infrastructure.Parsing;

using System.Text.RegularExpressions;
using WitteNog.Core.Interfaces;

public class WikiLinkParser : IWikiLinkParser
{
    private static readonly Regex LinkRegex =
        new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);
    private static readonly Regex DateRegex =
        new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public IReadOnlyList<string> ExtractLinks(string markdown) =>
        LinkRegex.Matches(markdown)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList()
            .AsReadOnly();

    public bool IsDateLink(string link) => DateRegex.IsMatch(link);
}
