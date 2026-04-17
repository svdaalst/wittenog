namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;

public record UpdateTaskCommand(string VaultPath, string TaskId, DateOnly? Deadline, int? Priority) : IRequest;

public class UpdateTaskCommandHandler : IRequestHandler<UpdateTaskCommand>
{
    private readonly ITaskRepository _taskRepo;

    public UpdateTaskCommandHandler(ITaskRepository taskRepo) => _taskRepo = taskRepo;

    public Task Handle(UpdateTaskCommand request, CancellationToken ct)
        => _taskRepo.UpdateTaskAsync(request.VaultPath, request.TaskId, request.Deadline, request.Priority, ct);
}
