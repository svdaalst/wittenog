using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Commands;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Commands;

public class CompleteTaskCommandTests
{
    private const string VaultPath = "/vault";
    private const string FilePath = "/vault/note.md";
    private static readonly DateTimeOffset Now = new(2026, 3, 21, 0, 0, 0, TimeSpan.Zero);

    private static IMediator BuildMediator(ITaskRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<CompleteTaskCommandHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static TaskItem MakeTask(string id, int line = 1) =>
        new(id, FilePath, line, "- [ ] Do the thing", "Do the thing", null, null, null, Now);

    [Fact]
    public async Task CompleteTask_RemovesFromRepository()
    {
        var task = MakeTask("task-1");
        var files = new Dictionary<string, List<string>>
        {
            [FilePath] = ["# Note", "- [ ] Do the thing"]
        };
        var repo = new FakeTaskRepository([task], files);
        var mediator = BuildMediator(repo);

        await mediator.Send(new CompleteTaskCommand(VaultPath, task.Id));

        Assert.Empty(repo.All);
    }

    [Fact]
    public async Task CompleteTask_UpdatesFileContent()
    {
        var task = MakeTask("task-1", line: 1);
        var fileLines = new List<string> { "# Note", "- [ ] Do the thing" };
        var files = new Dictionary<string, List<string>> { [FilePath] = fileLines };
        var repo = new FakeTaskRepository([task], files);
        var mediator = BuildMediator(repo);

        await mediator.Send(new CompleteTaskCommand(VaultPath, task.Id));

        Assert.Equal("- [x] Do the thing", fileLines[1]);
    }

    [Fact]
    public async Task CompleteTask_UnknownId_DoesNothing()
    {
        var task = MakeTask("task-1");
        var repo = new FakeTaskRepository([task]);
        var mediator = BuildMediator(repo);

        await mediator.Send(new CompleteTaskCommand(VaultPath, "unknown-id"));

        Assert.Single(repo.All);
    }

    [Fact]
    public async Task CompleteTask_MultipleTasksInFile_OnlyCompletesCorrectOne()
    {
        var task1 = new TaskItem("t1", FilePath, 1, "- [ ] Task 1", "Task 1", null, null, 1, Now);
        var task2 = new TaskItem("t2", FilePath, 2, "- [ ] Task 2", "Task 2", null, null, 2, Now);
        var fileLines = new List<string> { "# Note", "- [ ] Task 1", "- [ ] Task 2" };
        var files = new Dictionary<string, List<string>> { [FilePath] = fileLines };
        var repo = new FakeTaskRepository([task1, task2], files);
        var mediator = BuildMediator(repo);

        await mediator.Send(new CompleteTaskCommand(VaultPath, "t1"));

        Assert.Single(repo.All);
        Assert.Equal("t2", repo.All[0].Id);
        Assert.Equal("- [x] Task 1", fileLines[1]);
        Assert.Equal("- [ ] Task 2", fileLines[2]);
    }
}
