namespace WitteNog.Application.Queries;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record GetAllTasksQuery(string VaultPath) : IRequest<IReadOnlyList<TaskItem>>;

public class GetAllTasksQueryHandler : IRequestHandler<GetAllTasksQuery, IReadOnlyList<TaskItem>>
{
    private readonly ITaskRepository _taskRepo;

    public GetAllTasksQueryHandler(ITaskRepository taskRepo) => _taskRepo = taskRepo;

    public Task<IReadOnlyList<TaskItem>> Handle(GetAllTasksQuery request, CancellationToken ct)
    {
        var tasks = _taskRepo.GetAll(request.VaultPath)
            .OrderBy(t => t.Priority ?? 6)
            .ThenBy(t => t.Deadline.HasValue ? t.Deadline.Value.ToDateTime(TimeOnly.MinValue) : DateTime.MaxValue)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<TaskItem>>(tasks);
    }
}
