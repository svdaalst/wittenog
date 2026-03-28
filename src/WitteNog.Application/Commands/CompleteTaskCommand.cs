namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;

public record CompleteTaskCommand(string VaultPath, string TaskId) : IRequest;

public class CompleteTaskCommandHandler : IRequestHandler<CompleteTaskCommand>
{
    private readonly ITaskRepository _taskRepo;

    public CompleteTaskCommandHandler(ITaskRepository taskRepo) => _taskRepo = taskRepo;

    public Task Handle(CompleteTaskCommand request, CancellationToken ct)
        => _taskRepo.CompleteTaskAsync(request.VaultPath, request.TaskId, ct);
}
