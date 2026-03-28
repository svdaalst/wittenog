using WitteNog.Core.Parsing;

namespace WitteNog.Core.Tests.Parsing;

public class TaskParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 21, 0, 0, 0, TimeSpan.Zero);
    private const string FilePath = "/vault/note.md";

    // ── Basic task recognition ─────────────────────────────────────────────────

    [Fact]
    public void TryParseLine_BasicTask_ParsesDescription()
    {
        var task = TaskParser.TryParseLine("- [ ] Do the thing", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal("Do the thing", task.Description);
    }

    [Fact]
    public void TryParseLine_CompletedTask_ReturnsNull()
    {
        var task = TaskParser.TryParseLine("- [x] Done already", 0, FilePath, Now);
        Assert.Null(task);
    }

    [Fact]
    public void TryParseLine_NonTaskLine_ReturnsNull()
    {
        Assert.Null(TaskParser.TryParseLine("# Heading", 0, FilePath, Now));
        Assert.Null(TaskParser.TryParseLine("Regular paragraph", 0, FilePath, Now));
        Assert.Null(TaskParser.TryParseLine("- bullet without checkbox", 0, FilePath, Now));
    }

    // ── Priority parsing ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("- [ ] Task !P1", 1)]
    [InlineData("- [ ] Task !P2", 2)]
    [InlineData("- [ ] Task !P3", 3)]
    [InlineData("- [ ] Task !P4", 4)]
    [InlineData("- [ ] Task !P5", 5)]
    public void TryParseLine_Priority_ParsedCorrectly(string line, int expected)
    {
        var task = TaskParser.TryParseLine(line, 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal(expected, task.Priority);
    }

    [Theory]
    [InlineData("- [ ] Task !p1", 1)]
    [InlineData("- [ ] Task !p3", 3)]
    [InlineData("- [ ] Task !P5", 5)]
    public void TryParseLine_Priority_IsCaseInsensitive(string line, int expected)
    {
        var task = TaskParser.TryParseLine(line, 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal(expected, task.Priority);
    }

    [Fact]
    public void TryParseLine_NoPriority_PriorityIsNull()
    {
        var task = TaskParser.TryParseLine("- [ ] Task without priority", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Null(task.Priority);
    }

    // ── Deadline parsing ───────────────────────────────────────────────────────

    [Fact]
    public void TryParseLine_WithDeadline_ParsedCorrectly()
    {
        var task = TaskParser.TryParseLine("- [ ] Task @2026-03-25", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal(new DateOnly(2026, 3, 25), task.Deadline);
    }

    [Fact]
    public void TryParseLine_NoDeadline_DeadlineIsNull()
    {
        var task = TaskParser.TryParseLine("- [ ] Task without date", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Null(task.Deadline);
    }

    // ── Project link parsing ───────────────────────────────────────────────────

    [Fact]
    public void TryParseLine_WithProjectLink_ParsedCorrectly()
    {
        var task = TaskParser.TryParseLine("- [ ] [[ProjectX]] Do work", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal("ProjectX", task.ProjectLink);
        Assert.Equal("Do work", task.Description);
    }

    [Fact]
    public void TryParseLine_WithNestedProjectLink_ParsedCorrectly()
    {
        var task = TaskParser.TryParseLine("- [ ] [[Projects/Alpha]] Do work", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal("Projects/Alpha", task.ProjectLink);
    }

    [Fact]
    public void TryParseLine_NoProjectLink_ProjectLinkIsNull()
    {
        var task = TaskParser.TryParseLine("- [ ] Just a task", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Null(task.ProjectLink);
    }

    // ── Full syntax ────────────────────────────────────────────────────────────

    [Fact]
    public void TryParseLine_FullSyntax_AllFieldsParsed()
    {
        var task = TaskParser.TryParseLine(
            "- [ ] [[ProjectX]] Taakomschrijving @2026-03-25 !P2", 5, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal("Taakomschrijving", task.Description);
        Assert.Equal("ProjectX", task.ProjectLink);
        Assert.Equal(new DateOnly(2026, 3, 25), task.Deadline);
        Assert.Equal(2, task.Priority);
        Assert.Equal(5, task.LineNumber);
        Assert.Equal($"{FilePath}:5", task.Id);
    }

    // ── Edge cases ─────────────────────────────────────────────────────────────

    [Fact]
    public void TryParseLine_LeadingSpaces_StillParsed()
    {
        var task = TaskParser.TryParseLine("  - [ ] Indented task !P3", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal(3, task.Priority);
    }

    [Fact]
    public void TryParseLine_TrailingWhitespace_Trimmed()
    {
        var task = TaskParser.TryParseLine("- [ ] Task with spaces   ", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal("Task with spaces", task.Description);
    }

    [Fact]
    public void TryParseLine_EmptyDescription_StillParsed()
    {
        var task = TaskParser.TryParseLine("- [ ] [[Project]] @2026-01-01 !P1", 0, FilePath, Now);
        Assert.NotNull(task);
        Assert.Equal(string.Empty, task.Description);
    }

    // ── ParseAllTasks ──────────────────────────────────────────────────────────

    [Fact]
    public void ParseAllTasks_MixedLines_ReturnsOnlyOpenTasks()
    {
        var lines = new[]
        {
            "# Note title",
            "- [ ] Open task !P1",
            "- [x] Completed task",
            "Some text",
            "- [ ] Another open task @2026-04-01",
        };
        var tasks = TaskParser.ParseAllTasks(lines, FilePath, Now);
        Assert.Equal(2, tasks.Count);
        Assert.Equal("Open task", tasks[0].Description);
        Assert.Equal("Another open task", tasks[1].Description);
        Assert.Equal(1, tasks[0].LineNumber);
        Assert.Equal(4, tasks[1].LineNumber);
    }

    [Fact]
    public void ParseAllTasks_EmptyFile_ReturnsEmpty()
    {
        var tasks = TaskParser.ParseAllTasks([], FilePath, Now);
        Assert.Empty(tasks);
    }
}
