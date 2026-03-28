namespace WitteNog.Core.Interfaces;

public interface IWikiLinkParser
{
    IReadOnlyList<string> ExtractLinks(string markdown);
    bool IsDateLink(string link);
}
