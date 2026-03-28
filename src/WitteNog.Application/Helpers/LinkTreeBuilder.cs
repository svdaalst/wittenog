namespace WitteNog.Application.Helpers;

using System.Text.RegularExpressions;
using WitteNog.Core.Models;

public static class LinkTreeBuilder
{
    private static readonly Regex DateRegex = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    private static readonly string[] MaandNamen =
        ["Januari", "Februari", "Maart", "April", "Mei", "Juni",
         "Juli", "Augustus", "September", "Oktober", "November", "December"];

    public static (IReadOnlyList<LinkTreeNode> ActiveTopics,
                   IReadOnlyList<LinkTreeNode> ArchivedTopics,
                   LinkTreeNode? Dates) Build(
        IEnumerable<string> links,
        IReadOnlySet<string> archivedLinks,
        DateTimeOffset now)
    {
        var all        = links.Distinct().ToList();
        var dateLinks  = all.Where(l => DateRegex.IsMatch(l)).ToList();
        var topicLinks = all.Where(l => !DateRegex.IsMatch(l)).ToList();

        var activeTopics   = BuildTopicTree(topicLinks.Where(l => !archivedLinks.Contains(l)));
        var archivedTopics = BuildTopicTree(topicLinks.Where(l =>  archivedLinks.Contains(l)));
        var dates = dateLinks.Count > 0 ? BuildDateTree(dateLinks, now) : null;

        return (activeTopics, archivedTopics, dates);
    }

    // Backward-compat overload — bestaande aanroepen blijven werken
    public static (IReadOnlyList<LinkTreeNode> Topics, LinkTreeNode? Dates) Build(
        IEnumerable<string> links, DateTimeOffset now)
    {
        var (active, _, dates) = Build(links, new HashSet<string>(), now);
        return (active, dates);
    }

    public static IReadOnlyList<LinkTreeNode> BuildTopicTree(IEnumerable<string> topicLinks)
    {
        var root = new SortedDictionary<string, object>();  // string → LinkTreeNode (leaf) or SortedDictionary (folder)

        foreach (var link in topicLinks.Distinct().OrderBy(l => l))
        {
            var parts = link.Split('/');
            InsertParts(root, parts, 0, link);
        }

        return ToNodes(root);
    }

    private static void InsertParts(SortedDictionary<string, object> dict, string[] parts, int depth, string fullLink)
    {
        var key = parts[depth];
        if (depth == parts.Length - 1)
        {
            // Leaf — only insert if not already a folder
            if (!dict.ContainsKey(key))
                dict[key] = fullLink;
        }
        else
        {
            // Folder
            if (!dict.TryGetValue(key, out var existing) || existing is string)
                dict[key] = new SortedDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            InsertParts((SortedDictionary<string, object>)dict[key], parts, depth + 1, fullLink);
        }
    }

    private static IReadOnlyList<LinkTreeNode> ToNodes(SortedDictionary<string, object> dict)
    {
        var nodes = new List<LinkTreeNode>();
        foreach (var (name, value) in dict)
        {
            if (value is string fullLink)
                nodes.Add(new LinkTreeNode(name, fullLink, []));
            else
                nodes.Add(new LinkTreeNode(name, null, ToNodes((SortedDictionary<string, object>)value)));
        }
        return nodes.AsReadOnly();
    }

    public static LinkTreeNode BuildDateTree(IEnumerable<string> dateLinks, DateTimeOffset now)
    {
        var parsed = dateLinks
            .Distinct()
            .Select(l =>
            {
                if (DateTimeOffset.TryParseExact(l, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d))
                    return (Link: l, Date: (DateTimeOffset?)d);
                return (Link: l, Date: null);
            })
            .Where(x => x.Date.HasValue)
            .Select(x => (x.Link, Date: x.Date!.Value))
            .OrderByDescending(x => x.Date)
            .ToList();

        var weekAgo  = now.AddDays(-7).Date;
        var recent   = parsed.Where(x => x.Date.Date >= weekAgo).ToList();
        var older    = parsed.Where(x => x.Date.Date < weekAgo)
                             .GroupBy(x => x.Date.Year)
                             .OrderByDescending(g => g.Key);

        var children = new List<LinkTreeNode>();

        if (recent.Count > 0)
        {
            var recentLeaves = recent
                .Select(x => new LinkTreeNode(x.Link, x.Link, []))
                .ToList();
            children.Add(new LinkTreeNode("Afgelopen week", null, recentLeaves));
        }

        foreach (var yearGroup in older)
        {
            var monthNodes = yearGroup
                .GroupBy(x => x.Date.Month)
                .OrderByDescending(g => g.Key)
                .Select(mg => new LinkTreeNode(
                    $"{mg.Key:D2} - {MaandNamen[mg.Key - 1]}", null,
                    mg.OrderByDescending(x => x.Date)
                      .Select(x => new LinkTreeNode(x.Link, x.Link, []))
                      .ToList()
                ))
                .ToList();
            children.Add(new LinkTreeNode(yearGroup.Key.ToString(), null, monthNodes));
        }

        return new LinkTreeNode("📅 Datums", null, children);
    }
}
