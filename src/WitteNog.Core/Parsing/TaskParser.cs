namespace WitteNog.Core.Parsing;

using System.Text.RegularExpressions;
using WitteNog.Core.Models;

public static class TaskParser
{
    // Matches: - [ ] [[OptionalProject]] Description @YYYY-MM-DD !P1
    // desc uses \s* before @ and ! so the deadline/priority may appear with or without a preceding space
    private static readonly Regex TaskRegex = new(
        @"^\s*-\s\[ \]\s+(?:\[\[(?<project>[^\]]+)\]\]\s*)?(?<desc>.*?)\s*(?:@(?<date>\d{4}-\d{2}-\d{2}))?\s*(?:!(?<prio>[Pp][1-5]))?\s*$",
        RegexOptions.Compiled);

    public static TaskItem? TryParseLine(string line, int lineNumber, string filePath, DateTimeOffset lastModified)
    {
        var match = TaskRegex.Match(line);
        if (!match.Success)
            return null;

        var description = match.Groups["desc"].Value.Trim();
        var projectLink = match.Groups["project"].Success ? match.Groups["project"].Value.Trim() : null;

        DateOnly? deadline = null;
        if (match.Groups["date"].Success && DateOnly.TryParse(match.Groups["date"].Value, out var d))
            deadline = d;

        int? priority = null;
        if (match.Groups["prio"].Success)
            priority = int.Parse(match.Groups["prio"].Value[1].ToString());

        return new TaskItem(
            Id: $"{filePath}:{lineNumber}",
            FilePath: filePath,
            LineNumber: lineNumber,
            RawLine: line,
            Description: description,
            ProjectLink: projectLink,
            Deadline: deadline,
            Priority: priority,
            LastModified: lastModified
        );
    }

    public static IReadOnlyList<TaskItem> ParseAllTasks(string[] lines, string filePath, DateTimeOffset lastModified)
    {
        var tasks = new List<TaskItem>();
        for (int i = 0; i < lines.Length; i++)
        {
            var task = TryParseLine(lines[i], i, filePath, lastModified);
            if (task != null)
                tasks.Add(task);
        }
        return tasks.AsReadOnly();
    }
}
