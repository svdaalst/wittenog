using MediatR;
using Microsoft.Extensions.DependencyInjection;
using WitteNog.Application.Queries;
using WitteNog.Application.Tests.Fakes;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

namespace WitteNog.Application.Tests.Queries;

public class GetAllTasksQueryTests
{
    private const string VaultPath = "/vault";
    private static readonly DateTimeOffset Now = new(2026, 3, 21, 0, 0, 0, TimeSpan.Zero);

    private static IMediator BuildMediator(ITaskRepository repo)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssemblyContaining<GetAllTasksQueryHandler>());
        services.AddSingleton(repo);
        return services.BuildServiceProvider().GetRequiredService<IMediator>();
    }

    private static TaskItem MakeTask(string id, string filePath, string desc,
        int? priority = null, DateOnly? deadline = null)
        => new(id, filePath, 0, "- [ ] " + desc, desc, null, deadline, priority, Now);

    [Fact]
    public async Task GetAllTasks_SortsByPriorityThenDeadline()
    {
        var tasks = new[]
        {
            MakeTask("a", "/vault/a.md", "Low",    priority: 5),
            MakeTask("b", "/vault/b.md", "High",   priority: 1),
            MakeTask("c", "/vault/c.md", "Medium", priority: 3),
        };
        var repo = new FakeTaskRepository(tasks);
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllTasksQuery(VaultPath));

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result[0].Priority);
        Assert.Equal(3, result[1].Priority);
        Assert.Equal(5, result[2].Priority);
    }

    [Fact]
    public async Task GetAllTasks_NullPriorityComesLast()
    {
        var tasks = new[]
        {
            MakeTask("a", "/vault/a.md", "No priority", priority: null),
            MakeTask("b", "/vault/b.md", "P3",          priority: 3),
        };
        var repo = new FakeTaskRepository(tasks);
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllTasksQuery(VaultPath));

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result[0].Priority);
        Assert.Null(result[1].Priority);
    }

    [Fact]
    public async Task GetAllTasks_SameP_SortsByDeadlineAscending()
    {
        var tasks = new[]
        {
            MakeTask("a", "/vault/a.md", "Later",   priority: 2, deadline: new DateOnly(2026, 4, 1)),
            MakeTask("b", "/vault/b.md", "Earlier", priority: 2, deadline: new DateOnly(2026, 3, 25)),
        };
        var repo = new FakeTaskRepository(tasks);
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllTasksQuery(VaultPath));

        Assert.Equal("Earlier", result[0].Description);
        Assert.Equal("Later", result[1].Description);
    }

    [Fact]
    public async Task GetAllTasks_SameP_NullDeadlineComesLast()
    {
        var tasks = new[]
        {
            MakeTask("a", "/vault/a.md", "No deadline",    priority: 1, deadline: null),
            MakeTask("b", "/vault/b.md", "Has deadline",   priority: 1, deadline: new DateOnly(2026, 4, 1)),
        };
        var repo = new FakeTaskRepository(tasks);
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllTasksQuery(VaultPath));

        Assert.Equal("Has deadline", result[0].Description);
        Assert.Equal("No deadline", result[1].Description);
    }

    [Fact]
    public async Task GetAllTasks_EmptyVault_ReturnsEmpty()
    {
        var repo = new FakeTaskRepository([]);
        var mediator = BuildMediator(repo);

        var result = await mediator.Send(new GetAllTasksQuery(VaultPath));

        Assert.Empty(result);
    }
}
